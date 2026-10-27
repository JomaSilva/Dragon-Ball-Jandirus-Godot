using System.Text.Json;
using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O CAMPEAO DO MUNDO ANTERIOR -- o pedido 94 do dono (2026-10-25): *"faça q sempre q o jogo wipar (limpar todos os dados) o jogo cheque
/// o save do jogador mais forte e salve ele como um npcs secreto, com todas as skills bp etc, e assim no late game do novo wipe, esse
/// player aparece como boss de evento ao qual os jogadores tem q derrotar ... caso ele tenha transformaçoes, ele vira elas etc (a mais
/// forte q ele tiver)"*. E as escolhas dele (2026-10-26): ele aparece quando o jogador mais forte do mundo novo alcanca METADE do BP base
/// que o campeao tinha; luta no LETAL; aparece NA TERRA, uma vez por wipe; quem o derrota ganha o anuncio e uma quantia de zeni.
///
/// ============================ O CICLO ============================
///   1. A LIMPEZA GUARDA O MAIS FORTE (<see cref="GuardarOCampeaoDoWipe"/>, o passo 1b do `ExecutarLimpeza`, antes de todo mundo cair):
///      de toda conta do disco, o personagem de MAIOR BP BASE -- e quem esta online entra com o save de AGORA, e nao com o do ultimo
///      autosave. O save inteiro vai pro `campeao-do-wipe.json`, que a vassoura POUPA (`SistemaDoMundo.Preservar`, o registro em
///      `GameServer.Limpeza.cs`): e a unica coisa do mundo velho que atravessa pro novo.
///   2. NO MUNDO NOVO ELE ESPERA, em segredo. A 1 Hz (<see cref="TickDoCampeaoDoWipe"/>) o servidor pergunta se algum jogador vivo ja
///      tem metade do BP base dele (<see cref="FracaoDoGatilho"/>).
///   3. ELE NASCE NA TERRA com o corpo do save -- o nome, a raca, a aparencia, a ficha, as skills e as formas que despertou -- e entra na
///      MAIS FORTE delas (<see cref="FormaMaisForte"/>). O corpo e um chefe de ROTEIRO DE UM DEGRAU (<see cref="MoldeDoCampeao"/>): o
///      degrau crava o BP base do save e a forma, e o `TickDoRoteiro` reancora o BP todo segundo (ele nao cresce apanhando). Letal, Ki de
///      chefe e caca o planeta inteiro (`PapelDeNpc.DaSaga`), como um chefe de saga; o mundo e avisado, com a cena de chegada de chefe.
///   4. QUEM O DERRUBA (nocaute ou morte por um JOGADOR; o Majin, so pela morte -- <see cref="CampeaoDoWipeFoiDerrotado"/>) leva o
///      premio, e o mundo ouve. O corpo sai no segundo seguinte, e ele nao volta mais neste wipe.
///   5. O REINICIO NAO O APAGA: o arquivo diz em que fase ele esta, e quem estava na Terra volta a ela no primeiro tique.
/// ================================================================
///
/// ============================ O QUE NAO VIRA CAMPEAO ============================
///   * As contas de BANCADA (`bancada...`): o save de um servidor de verdade pode ter sobra de bancada, e um "bancada_x" de BP
///     inventado nao pode virar o chefe do mundo novo -- a mesma regra do disco das sagas (`QuemEstaNoDisco`). E pelo mesmo motivo elas
///     nao acordam o campeao: uma bancada que poe BP alto num jogador nao pode soltar um chefe letal no meio dela.
///   * Um mundo que nem saiu do comeco (<see cref="BpMinimoDoCampeao"/>): ai a limpeza nao troca o campeao -- o de antes continua
///     guardado e volta a esperar o mundo novo. E o que impede uma limpeza feita duas vezes seguidas de trocar o campeao de verdade pelo
///     novato de cinco minutos.
/// ===============================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O arquivo do campeao, na pasta de saves. A limpeza o POUPA -- ver o registro em `GameServer.Limpeza.cs`.</summary>
	private const string ArquivoDoCampeao = "campeao-do-wipe.json";

	/// <summary>O molde do corpo dele: feito na hora, do save (ele nao mora no `npcs.json`).</summary>
	internal const string MoldeDoCampeao = "campeao_do_wipe";

	/// <summary>O GATILHO: um jogador do mundo novo com METADE do BP base que o campeao tinha (a escolha do dono, 2026-10-26).</summary>
	internal const double FracaoDoGatilho = 0.5;

	/// <summary>ONDE ELE APARECE: "Na Terra" (a escolha do dono, 2026-10-26), anunciado a todos.</summary>
	internal const string PlanetaDoCampeao = "Earth";

	/// <summary>
	/// O PISO DE UM CAMPEAO: um milhao de BP base. Abaixo disso o mundo nem saiu do comeco (a escada das formas comeca em 1,5 milhao), e a
	/// limpeza nao troca o campeao guardado -- ver o cabecalho.
	/// </summary>
	internal const double BpMinimoDoCampeao = 1_000_000;

	/// <summary>
	/// O PREMIO de quem o derrota ("Anuncio + zeni", a escolha do dono): um milhao -- acima da bolsa de qualquer chefe de saga
	/// (`BolsaDeNpc.ZeniMaxDoChefe`, 800 mil), porque ele e o chefe mais forte que o mundo novo vai ver. Viaja na bolsa dele, como o
	/// premio dos viloes.
	/// </summary>
	internal const double PremioDoCampeao = 1_000_000;

	internal enum FaseDoCampeao { Esperando, NoMundo, Derrotado }

	/// <summary>O que vai pro disco: o save inteiro do campeao, de onde ele veio, e em que pe a historia dele esta neste wipe.</summary>
	internal sealed class CampeaoGuardado
	{
		/// <summary>O SAVE INTEIRO, como estava no fim do mundo dele -- ficha, skills, formas, aparencia.</summary>
		public CharacterSave Personagem = new();

		/// <summary>A conta e a vaga de onde ele saiu -- so pro log: a conta nao existe mais.</summary>
		public string ContaDeOrigem = "";
		public int Slot;

		/// <summary>O BP BASE no fim do mundo dele: a regua do gatilho, e o BP do corpo.</summary>
		public double BpBase;

		public FaseDoCampeao Fase;

		/// <summary>Quando a limpeza o guardou (ms de parede).</summary>
		public long GuardadoEm;

		/// <summary>Quem o derrotou neste wipe (vazio enquanto ninguem).</summary>
		public string VencidoPor = "";
	}

	private CampeaoGuardado? _campeao;

	/// <summary>O id do corpo dele no mundo agora. Zero = nenhum -- e e o que o boot tem: o corpo nao atravessa o reinicio.</summary>
	private int _corpoDoCampeao;

	/// <summary>A faixa propria de "lugar" (o ponto de habitante quando ninguem esta na Terra), longe das do povoamento e dos viloes.</summary>
	private ulong _lugarDoCampeao = 0x7130_0000UL;

	/// <summary>(`--campeaoteste`) As contas de bancada contam -- e o unico jeito de a bancada usar o caminho de producao. Falso em jogo, sempre.</summary>
	internal static bool CampeaoComAsBancadasDeTeste;

	private static readonly JsonSerializerOptions OpcoesDoCampeao = new() { IncludeFields = true, WriteIndented = true };

	private string CaminhoDoCampeao => System.IO.Path.Combine(_store!.Pasta, ArquivoDoCampeao);

	private static bool ContaQueNaoCompete(string conta) =>
		!CampeaoComAsBancadasDeTeste && conta.StartsWith("bancada", StringComparison.OrdinalIgnoreCase);

	/// <summary>Uma copia funda do save: o corpo nao pode dividir a ficha com o que esta guardado (a luta escreveria no arquivo).</summary>
	private static CharacterSave CopiaDoSave(CharacterSave s) =>
		JsonSerializer.Deserialize<CharacterSave>(JsonSerializer.Serialize(s, OpcoesDoCampeao), OpcoesDoCampeao)!;

	// =====================================================================
	// O DISCO
	// =====================================================================
	/// <summary>No boot (e na recarga das bancadas da limpeza): o campeao guardado e em que fase ele esta. O corpo e zero.</summary>
	private void CarregarCampeaoDoWipe()
	{
		_campeao = null;
		_corpoDoCampeao = 0;
		if (_store == null || !System.IO.File.Exists(CaminhoDoCampeao)) return;
		try
		{
			_campeao = JsonSerializer.Deserialize<CampeaoGuardado>(System.IO.File.ReadAllText(CaminhoDoCampeao), OpcoesDoCampeao);
		}
		catch (Exception e) { GD.PushWarning($"[campeao] {ArquivoDoCampeao} ilegivel: {e.Message}"); }
		if (_campeao?.Personagem?.Ficha == null) { _campeao = null; return; }
		GD.Print($"[campeao] guardado: '{_campeao.Personagem.Nome}' ({_campeao.Personagem.Raca}, BP base {_campeao.BpBase:N0}) | fase {_campeao.Fase}");
	}

	private void SalvarCampeaoDoWipe()
	{
		if (_store == null || _campeao == null) return;
		try
		{
			string tmp = CaminhoDoCampeao + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(_campeao, OpcoesDoCampeao));
			System.IO.File.Move(tmp, CaminhoDoCampeao, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[campeao] nao deu pra salvar {ArquivoDoCampeao}: {e.Message}"); }
	}

	// =====================================================================
	// A LIMPEZA GUARDA O MAIS FORTE
	// =====================================================================
	/// <summary>
	/// O PASSO 1b DO `ExecutarLimpeza`: com o portao ja fechado e ANTES de todo mundo cair. O personagem de maior BP base vira o campeao
	/// guardado, na fase de espera; sem ninguem acima do <see cref="BpMinimoDoCampeao"/>, o campeao de antes (se houver) fica e volta a
	/// esperar. Um erro aqui nao pode parar a limpeza -- ele so nao guarda ninguem, e diz.
	/// </summary>
	private void GuardarOCampeaoDoWipe()
	{
		try
		{
			if (OMaisForteDoMundo() is { } n)
			{
				_campeao = new CampeaoGuardado
				{
					Personagem = CopiaDoSave(n.Save), ContaDeOrigem = n.Conta, Slot = n.Slot, BpBase = n.Save.Ficha.BP,
					Fase = FaseDoCampeao.Esperando, GuardadoEm = NowMs(),
				};
				GD.Print($"[campeao] a limpeza guardou o campeao deste mundo: '{n.Save.Nome}' ({n.Conta}, vaga {n.Slot + 1}) -- "
					   + $"{n.Save.Raca}, BP base {n.Save.Ficha.BP:N0}");
			}
			else if (_campeao != null)
			{
				_campeao.Fase = FaseDoCampeao.Esperando;
				_campeao.VencidoPor = "";
				GD.Print($"[campeao] ninguem deste mundo chegou a {BpMinimoDoCampeao:N0} de BP base -- o campeao guardado "
					   + $"('{_campeao.Personagem.Nome}') continua, e volta a esperar o mundo novo");
			}
			else GD.Print($"[campeao] ninguem deste mundo chegou a {BpMinimoDoCampeao:N0} de BP base -- o mundo novo nao tera campeao");
			_corpoDoCampeao = 0;
			SalvarCampeaoDoWipe();
		}
		catch (Exception e) { GD.PushError($"[campeao] guardar o campeao quebrou (a limpeza segue): {e}"); }
	}

	/// <summary>
	/// O PERSONAGEM DE MAIOR BP BASE de toda conta do disco -- com o save de AGORA de quem esta online (o do disco e o do ultimo autosave, e
	/// o portao da limpeza ja fechou). Fora as contas de bancada e quem esta abaixo do piso. Nulo = ninguem. Tambem e a linha da previa.
	/// </summary>
	private (CharacterSave Save, string Conta, int Slot)? OMaisForteDoMundo()
	{
		if (_store == null) return null;
		long agora = NowMs();
		var vivos = new Dictionary<(string, int), (string Conta, CharacterSave Save)>();
		foreach (ServerPlayer p in Jogadores)
			if (p.Conta.Length > 0 && p.Slot >= 0) vivos[(p.Conta.ToLowerInvariant(), p.Slot)] = (p.Conta, AccountStore.DeJogador(p, agora));

		(CharacterSave Save, string Conta, int Slot)? melhor = null;
		void Pesar(CharacterSave? s, string conta, int slot)
		{
			if (s?.Ficha is not { } f || f.BP < BpMinimoDoCampeao || ContaQueNaoCompete(conta)) return;
			if (melhor is { } m && m.Save.Ficha.BP >= f.BP) return;
			melhor = (s, conta, slot);
		}
		foreach (AccountSave a in _store.Todas())
			for (int i = 0; i < a.Slots.Length; i++)
				Pesar(vivos.Remove((a.Conta.ToLowerInvariant(), i), out var vivo) ? vivo.Save : a.Slots[i], a.Conta, i);
		// ...e quem esta online sem a conta no disco ainda (o personagem nasceu e a primeira gravacao nao chegou)
		foreach (((string, int) chave, (string conta, CharacterSave save)) in vivos) Pesar(save, conta, chave.Item2);
		return melhor;
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>A 1 Hz: acorda o campeao quando alguem chega a metade dele, devolve-o a Terra depois de um reinicio e tira o derrotado.</summary>
	private void TickDoCampeaoDoWipe()
	{
		if (_campeao is not { } c) return;
		switch (c.Fase)
		{
			case FaseDoCampeao.Esperando:
				if (QuemAlcancaOCampeao(c) is { } quem)
				{
					GD.Print($"[campeao] {quem.Name} ({quem.Conta}) chegou a {quem.Ficha.BP:N0} de BP base -- metade do campeao guardado "
						   + $"({c.BpBase:N0}): ele desperta");
					NascerOCampeao(c, retomando: false);
				}
				break;

			case FaseDoCampeao.NoMundo:
				// O BOOT: o arquivo diz "na Terra", e o corpo nao atravessou o reinicio.
				if (_corpoDoCampeao == 0) { NascerOCampeao(c, retomando: true); break; }
				if (_players.ContainsKey(_corpoDoCampeao)) break;
				// O CORPO SUMIU SEM NINGUEM O DERROTAR (morto por um NPC, tirado por outra porta): o evento acaba, sem premio.
				_corpoDoCampeao = 0;
				c.Fase = FaseDoCampeao.Derrotado;
				SalvarCampeaoDoWipe();
				AnunciarNoMundo(EhCampea(c.Personagem.Genero)
					? $"{c.Personagem.Nome}, a campeã do mundo anterior, sumiu sem ser derrotada."
					: $"{c.Personagem.Nome}, o campeão do mundo anterior, sumiu sem ser derrotado.");
				break;

			case FaseDoCampeao.Derrotado:
				// O DERROTADO SAI no segundo seguinte a derrota, e nao dentro dela: a derrota acontece na resolucao de um golpe, com alguem
				// percorrendo a lista da zona (o mesmo argumento do vilao avulso).
				if (_corpoDoCampeao != 0 && _players.TryGetValue(_corpoDoCampeao, out ServerPlayer? corpo)) RemoverNpc(corpo);
				_corpoDoCampeao = 0;
				break;
		}
	}

	/// <summary>
	/// O JOGADOR VIVO DE MAIOR BP BASE que ja tem a metade do campeao -- nulo = ninguem ainda. **BP BASE**, e nao o expresso (a mesma regua
	/// do marco das sagas, `QuemBateOMarco`).
	/// </summary>
	private ServerPlayer? QuemAlcancaOCampeao(CampeaoGuardado c)
	{
		double regua = c.BpBase * FracaoDoGatilho;
		ServerPlayer? melhor = null;
		foreach (ServerPlayer p in Jogadores)
		{
			if (p.Ficha.dead || ContaQueNaoCompete(p.Conta) || p.Ficha.BP < regua) continue;
			if (melhor == null || p.Ficha.BP > melhor.Ficha.BP) melhor = p;
		}
		return melhor;
	}

	private static bool EhCampea(string genero) => string.Equals(genero, "Female", StringComparison.OrdinalIgnoreCase);

	// =====================================================================
	// O CORPO
	// =====================================================================
	/// <summary>
	/// O CAMPEAO NASCE NA TERRA, com o corpo do save montado como o `Entrar` monta o de um jogador (`ParaJogador`, as skills, os niveis, as
	/// formas) -- e por cima, o que faz dele o chefe: o molde de um degrau so (o BP base cravado e a forma mais forte), a mente de chefe, o
	/// golpe letal, o Ki que nao acaba, a caca do planeta inteiro e o premio na bolsa. <paramref name="retomando"/> = ele ja estava na Terra
	/// antes de um reinicio: a frase e outra, e nao ha cena de chegada.
	/// </summary>
	private ServerPlayer? NascerOCampeao(CampeaoGuardado c, bool retomando)
	{
		var zona = ZoneKey.Premade(PlanetaDoCampeao);
		if (_catalogo?.Get(zona) == null)
		{
			GD.PushError("[campeao] a Terra nao esta no manifesto de mapas -- o campeao nao nasce");
			return null;
		}

		CharacterSave s = CopiaDoSave(c.Personagem);
		var npc = new ServerPlayer { Id = _nextId++, Peer = null, LastInputMs = NowMs() };
		AccountStore.ParaJogador(s, npc);
		MigrarInteligencia(npc);
		MigrarPoderDaRaca(npc);
		// DE PE, INTEIRO, COM O PODER TODO: o save pode ter sido gravado com ele morto, nocauteado, oculto, contido ou de peso de treino.
		npc.Ficha.dead = false;
		npc.Ficha.KO = false;
		npc.Ficha.Weighted = 0;
		npc.Ficha.pesoVestido = "";
		AcordarVisivel(npc);
		AcordarComOPoderInteiro(npc);

		// O CORPO DO JOGADOR, pelas mesmas portas do `Entrar` (o combate antes: os efeitos perguntam por ele).
		PrepararCombate(npc, null);
		PrepararSkills(npc, s);
		PrepararCustomizadas(npc, s);
		npc.Niveis = new Jandirus.Core.Skills.NiveisDeSkill();
		npc.Niveis.DoSave(s.Niveis);
		npc.Niveis.Aplicar(npc.Ficha);
		AplicarPoderes(npc);
		AplicarEfeitos(npc);
		RestaurarFormaEDisciplina(npc, s);

		// O CHEFE: um degrau so -- o BP base do save e a forma mais forte que ele despertou. O `EntrarNoDegrau` (la embaixo) entra nela e
		// crava o BP, e o `TickDoRoteiro` o reancora todo segundo.
		string forte = FormaMaisForte(npc);
		ulong semente = Espaco.Misturar(SeedDoUniverso, Espaco.Hash64(MoldeDoCampeao), (ulong)c.GuardadoEm);
		var molde = new MoldeDeNpc
		{
			Id = MoldeDoCampeao, Nome = s.Nome, Tipo = TipoDeNpc.Chefe, TipoDeclarado = true,
			Racas = [npc.Race], Classe = npc.Class, Generos = [npc.Genero],
			// o temperamento de quem foi o mais forte do mundo: corajoso, furioso e esperto (os numeros de `Temperamento.Montar`)
			Coragem = 90, Furia = 60, Inteligencia = 70, Agressividade = 80,
			ChanceDeVoar = 1, ChanceDeKi = 1,
			// ELE JA ESTA NA MAIS FORTE que tem: nao sobe pra uma forma que nunca despertou.
			AscendePorDecisao = false,
			Estagios = [new EstagioDeChefe { Bp = c.BpBase, Forma = forte == Catalogo.IdBase ? "" : forte }],
		};
		npc.Papel = new PapelDeNpc(molde, semente) { DaSaga = true };
		npc.Cerebro = Temperamento.Montar(molde, semente % 1000 / 1000.0, semente);
		npc.Perfil = Jandirus.Core.Ai.PerfilDeCombate.Sortear(molde.ChanceDeVoar, molde.ChanceDeKi, semente).PeloPoder(c.BpBase);
		npc.Zone = zona;
		npc.Pos = LugarDeVilao(zona) ?? PontoDeHabitante(zona, ++_lugarDoCampeao);
		npc.Berco = new Jandirus.Core.Races.Berco { Planeta = zona.Name, Natal = zona.Name, Seed = zona.Seed, PreFeito = true };
		npc.Ficha.Zeni = PremioDoCampeao;

		PorNoMundo(npc);
		npc.Combate.Letal = true;
		npc.Ficha.KiDeChefe = true;
		npc.Papel.Estagio = 0;
		EntrarNoDegrau(npc, cura: 0, anunciar: false);
		TrocarAparencias(npc);

		_corpoDoCampeao = npc.Id;
		c.Fase = FaseDoCampeao.NoMundo;
		SalvarCampeaoDoWipe();

		bool ela = EhCampea(npc.Genero);
		string frase = (retomando, ela) switch
		{
			(true, true) => $"{npc.Name}, A CAMPEÃ DO MUNDO ANTERIOR, CONTINUA NA TERRA -- a ameaça não passou.",
			(true, false) => $"{npc.Name}, O CAMPEÃO DO MUNDO ANTERIOR, CONTINUA NA TERRA -- a ameaça não passou.",
			(false, true) => $"{npc.Name}, A CAMPEÃ DO MUNDO ANTERIOR, DESPERTOU NA TERRA!",
			(false, false) => $"{npc.Name}, O CAMPEÃO DO MUNDO ANTERIOR, DESPERTOU NA TERRA!",
		};
		AnunciarNoMundo(frase);
		if (!retomando)
		{
			AnunciarNoMundo($"O mais forte do mundo que acabou voltou como chefe e caça quem estiver na Terra. Quem derrotar {npc.Name} ganha "
						  + $"{PremioDoCampeao:N0} zeni.");
			AnunciarChegadaDoChefe(npc, zona.Name, frase);
		}

		GD.Print($"[campeao] '{npc.Name}' {(retomando ? "voltou" : "despertou")} na Terra em ({npc.Pos.X:0}, {npc.Pos.Y:0}) | {npc.Race}/{npc.Class} "
			   + $"| BP base {npc.Ficha.BP:N0} | forma {npc.Forma.Atual} | {npc.Livro.Aprendidas.Count} skills | premio {npc.Ficha.Zeni:N0} zeni");
		return npc;
	}

	/// <summary>
	/// A FORMA MAIS FORTE QUE ELE TEM: das que ele despertou em vida, a de maior multiplicador -- o pedido: *"ele vira elas etc (a mais forte
	/// q ele tiver)"*. Fora o OOZARU: o macaco e da lua e do rabo, e nao uma forma de luta que se sustenta num chefe que caca o planeta.
	/// </summary>
	private string FormaMaisForte(ServerPlayer corpo)
	{
		PerfilDeFormas perfil = Perfil(corpo);
		string melhor = Catalogo.IdBase;
		double maior = 1;
		foreach (int rede in corpo.Forma.Liberadas)
		{
			if (Catalogo.PorRede((ushort)rede) is not { } d || d.Id == Catalogo.IdBase || d.Linha == LinhaDeForma.Oozaru) continue;
			double m = Catalogo.Multiplicador(d.Id, corpo.Forma.Maestria, perfil);
			if (m > maior) { maior = m; melhor = d.Id; }
		}
		return melhor;
	}

	// =====================================================================
	// A DERROTA
	// =====================================================================
	/// <summary>
	/// O FUNIL DA DERROTA PASSOU PELO CAMPEAO (`AoPerderALuta`, o nocaute ou a morte). So um JOGADOR o derrota (ou o clone dele: o merito e
	/// de quem manda) -- um NPC que o derruba nao encerra nada, ele levanta. O Majin so cai pela morte (o soco que o desfaz e o nocaute so o
	/// derrubam por um tempo), a regra do dono pro Majin chefe (`SoAMorteDerrota`). O premio vai pra quem o derrotou, o mundo ouve, e o
	/// tique tira o corpo no segundo seguinte.
	/// </summary>
	private void CampeaoDoWipeFoiDerrotado(ServerPlayer vitima, ServerPlayer algoz, bool morreu)
	{
		if (_campeao is not { Fase: FaseDoCampeao.NoMundo } c || _corpoDoCampeao == 0 || vitima.Id != _corpoDoCampeao) return;

		ServerPlayer? heroi = EhJogador(algoz) ? algoz
			: algoz.DonoDoClone != 0 && _players.TryGetValue(algoz.DonoDoClone, out ServerPlayer? dono) && EhJogador(dono) ? dono
			: null;
		if (heroi == null) return;

		if (!morreu && CorpoDeMajin(vitima))
		{
			Avisar(heroi, $"{vitima.Name} ainda não caiu de vez: um Majin só é derrotado por um golpe de ki letal.");
			return;
		}

		c.Fase = FaseDoCampeao.Derrotado;
		c.VencidoPor = heroi.Name;
		SalvarCampeaoDoWipe();

		// O PREMIO DA BOLSA dele, direto pra carteira -- como o dos viloes (cai em nocaute, e o corpo sai no segundo seguinte).
		double premio = Math.Floor(vitima.Ficha.Zeni);
		if (premio >= 1)
		{
			vitima.Ficha.Zeni = 0;
			heroi.Ficha.Zeni += premio;
			MandarCarteira(heroi);
			Avisar(heroi, $"o prêmio é seu: {premio:N0} zeni.");
		}
		AnunciarNoMundo(EhCampea(c.Personagem.Genero)
			? $"{NomeVisivel(heroi)} derrotou {c.Personagem.Nome}, a campeã do mundo anterior!"
			: $"{NomeVisivel(heroi)} derrotou {c.Personagem.Nome}, o campeão do mundo anterior!");
		GD.Print($"[campeao] {heroi.Name} ({heroi.Conta}) derrotou '{c.Personagem.Nome}' e levou {premio:N0} zeni");
	}

	// =====================================================================
	// O ADMIN
	// =====================================================================
	/// <summary>
	/// O VERBO DO ADMIN: o campeao guardado desperta na Terra AGORA, sem esperar os 50% -- e a aparicao dele neste wipe (o derrotado tambem
	/// volta: e uma ferramenta de teste). Sem campeao guardado, diz por que; com ele de pe, diz onde.
	/// </summary>
	private void AdminCampeaoDoWipe(ServerPlayer adm)
	{
		if (_campeao is not { } c)
		{
			Avisar(adm, $"nenhum campeão guardado: a limpeza guarda o personagem mais forte do mundo (com {BpMinimoDoCampeao:N0} de BP base ou mais) quando roda.");
			return;
		}
		if (_corpoDoCampeao != 0 && _players.ContainsKey(_corpoDoCampeao))
		{
			Avisar(adm, $"{c.Personagem.Nome} já está na Terra.");
			return;
		}
		_corpoDoCampeao = 0;
		if (NascerOCampeao(c, retomando: false) == null) { Avisar(adm, "o campeão não pôde despertar (a Terra não tem mapa)."); return; }
		Registrar(adm, $"chamou o campeao do wipe '{c.Personagem.Nome}' (BP base {c.BpBase:N0}) na Terra");
		Avisar(adm, $"{c.Personagem.Nome} despertou na Terra (BP base {c.BpBase:N0}). É a aparição dele neste wipe.");
	}

	/// <summary>O campeao guardado (o nome, a fase, o corpo de agora). SO PRA BANCADA.</summary>
	internal (string Nome, double BpBase, FaseDoCampeao Fase, int Corpo)? CampeaoDeTeste =>
		_campeao is { } c ? (c.Personagem.Nome, c.BpBase, c.Fase, _corpoDoCampeao) : null;
}
