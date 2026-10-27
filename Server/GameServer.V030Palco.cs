using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Items;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagv030` (a versao 0.3.0 na tela): o robo e um cliente no mesmo processo do servidor
/// (`--host`), e estas portas montam a cena do lado de la -- a raiva, o rabo perdido, a arte dos pesos, um aviso num
/// tom. Cada uma escreve o MINIMO que a cena pede; a regra que se mede continua sendo a de producao (o retrato do poder,
/// o boneco do corpo, o funil do chat, a mochila).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>A raiva deste corpo, escrita nas duas janelas (60 s), pra o HUD desenha-la. `Nenhuma` apaga as duas.</summary>
	internal void PalcoV30_Raiva(int id, NivelDeRaiva n)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		long ate = NowMs() + 60_000;
		pl.RaivaComumAte = n == NivelDeRaiva.Comum ? ate : 0;
		pl.FuriaExtremaAte = n == NivelDeRaiva.Extrema ? ate : 0;
	}

	/// <summary>O rabo deste corpo arrancado PRA SEMPRE, com a ferida aberta por estes segundos. Falso sem rabo.</summary>
	internal bool PalcoV30_RaboPerdido(int id, double segundosDeFerida)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate?.Corpo.Achar(Jandirus.Core.Races.RaboDoSaiyajin.Membro) is not { } rabo)
			return false;
		rabo.Decepado = true;
		rabo.Perdido = true;
		rabo.FeridaAberta = segundosDeFerida;
		pl.Combate.SincronizarVida();
		return true;
	}

	/// <summary>Passa o relogio das feridas que fecham pela porta de producao (`TickDaFeridaDoRabo`).</summary>
	internal void PalcoV30_PassarOTempoDaFerida(double segundos) => TickDaFeridaDoRabo(segundos);

	/// <summary>O rabo de volta, inteiro -- a bancada nao deixa o personagem dela sem rabo.</summary>
	internal void PalcoV30_DevolverORabo(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate?.Corpo.Achar(Jandirus.Core.Races.RaboDoSaiyajin.Membro) is not { } rabo)
			return;
		rabo.Decepado = false;
		rabo.Perdido = false;
		rabo.FeridaAberta = 0;
		rabo.Vida = rabo.VidaMax;
		pl.Combate.SincronizarVida();
	}

	/// <summary>
	/// Escolhe pros pesos deste corpo uma arte que nao e a de fabrica, pela porta de producao (`EscolherAArteDosPesos`), e
	/// devolve o nome dela e o caminho da folha. Vazios sem catalogo.
	/// </summary>
	internal (string Nome, string Caminho) PalcoV30_ArteDosPesos(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _visual == null) return ("", "");
		(string Nome, string Caminho) outra = PesoDeTreino.Escolhiveis(_visual).Find(e => e.Nome != PesoDeTreino.ArtePadrao);
		if (string.IsNullOrEmpty(outra.Nome)) return ("", "");
		if (pl.Mochila.Pilhas.Find(p => PesoDeTreino.EhPeso(p.Id)).Id is not { Length: > 0 } pesos) return ("", "");
		EscolherAArteDosPesos(pl, pesos, outra.Nome);
		return outra;
	}

	/// <summary>Os pesos voltam a arte de fabrica.</summary>
	internal void PalcoV30_SemArteDosPesos(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		// (2026-10-18) a arte mora no id de cada peso: todo peso da mochila volta a de fabrica
		foreach (Jandirus.Core.Items.Pilha p in pl.Mochila.Pilhas.Where(p => PesoDeTreino.EhPeso(p.Id)).ToList())
			TrocarOPeso(pl, p.Id, PesoDeTreino.IdDe(PesoDeTreino.MaximoDe(p.Id), ""));
		pl.SigAtributos = "";
	}

	/// <summary>(2026-10-15) O corpo se desfaz em pedacos pela porta do Core (`CombatState.Desfazer`) -- o que se mede e a tela.</summary>
	internal bool PalcoV30_Desfazer(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate is not { } c) return false;
		c.Desfazer();
		return true;
	}

	/// <summary>O corpo em pedacos se refaz AGORA -- o fim do relogio sem esperar os 30 s (`Levantar`).</summary>
	internal void PalcoV30_Refazer(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate is { Desfeito: true } c) c.Levantar();
	}

	/// <summary>
	/// (2026-10-18) UM GOLPE QUE ARREMESSA, no corpo `alvo`, vindo de `deQuem` -- pela porta de producao do soco pesado (`Arremessar`), com a
	/// forca e a duracao do teto. Devolve se o corpo saiu voando. E o "e se eu bater onde estava o Majin?" do robo `--diagpedacos`.
	/// </summary>
	internal bool PalcoV30_Arremessar(int deQuem, int alvo)
	{
		if (!_players.TryGetValue(deQuem, out ServerPlayer? a) || !_players.TryGetValue(alvo, out ServerPlayer? d)) return false;
		Vec2 rumo = d.Pos - a.Pos;
		Arremessar(d, rumo.LengthSquared > 0 ? rumo.Normalized() : new Vec2(1, 0), Math.Max(a.Ficha.expressedBP, 1) * 10, Empurrao.TiquesMax);
		return d.TiquesDeVoo > 0;
	}

	/// <summary>(2026-10-18) Desloca o corpo `id` no servidor, sem colisao -- o "e se o corpo andasse?" da tela dos pedacos.</summary>
	internal void PalcoV30_Deslocar(int id, Vec2 quanto)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Pos += quanto;
	}

	/// <summary>(2026-10-18) Quantos segundos do nocaute ainda faltam pro corpo `id` levantar, no relogio do SERVIDOR (o desfeito inclusive).</summary>
	internal double PalcoV30_RestoDoNocaute(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate is { } c ? c.NocauteRestante : 0;

	/// <summary>(2026-10-18) Onde o corpo `id` esta no servidor, e se ele esta voando (o arremesso em curso).</summary>
	internal (Vec2 Pos, bool Voando) PalcoV30_OndeEsta(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? (pl.Pos, pl.TiquesDeVoo > 0) : (default, false);

	/// <summary>
	/// (2026-10-15) Alterna a Forma Gigante deste corpo pela tecnica de producao (`FormaGigante`), sem a trava de quem
	/// acabou de mudar de tamanho. Devolve se ele ficou gigante.
	/// </summary>
	internal bool PalcoV30_Gigante(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		_giganteTravaG2.Remove(id);
		FormaGigante(pl);
		return TemBuff(pl, BuffGiganteG2);
	}

	/// <summary>(2026-10-15) Um golpe de tecnica deste corpo naquele, pelo funil de producao das tecnicas que socam sozinhas (`GolpeG3`).</summary>
	internal bool PalcoV30_GolpeDeTecnica(int idDeQuemBate, int idDoAlvo)
	{
		if (!_players.TryGetValue(idDeQuemBate, out ServerPlayer? a) || !_players.TryGetValue(idDoAlvo, out ServerPlayer? d)) return false;
		GolpeG3(a, d, addDano: 0, nivel: 2);
		return true;
	}

	/// <summary>Um aviso do servidor neste tom, pelo funil de producao (`Avisar`).</summary>
	internal void PalcoV30_Avisar(int id, string texto, Protocol.Fala tom)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) Avisar(pl, texto, tom);
	}

	// =====================================================================
	// (v0.3.0, itens 7 e 11) A MESA DO ENMA: o passe e a reencarnacao pelos verbos de producao que o robo manda
	// =====================================================================
	/// <summary>Este corpo morto, de pe e de aureola, um tile a leste do Enma, com este zeni e o karma neutro. Falso sem o Enma.</summary>
	internal bool PalcoV30_MortoAoLadoDoEnma(int id, double zeni)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		if (_noChao.Find(o => o.Tipo == Alem.TipoDoEnma && o.Zona.Equals(alem)) is not { } enma) return false;
		pl.Ficha.dead = true;
		pl.Ficha.KO = false;
		// A BRIGA ACABA COM A MORTE: o `Morrer()` de producao zera as marcas de luta (`CombatState.Morrer`), e este palco escreve a
		// morte direto -- sem estas linhas o fantasma ainda "brigava" e a meditacao era recusada (achado da familia P da `--diagv030`).
		pl.Combate.EmCombate = 0;
		pl.Combate.LutandoDeVerdade = 0;
		pl.MorteJaViajou = true;
		pl.RelogioDaMorte = long.MaxValue;
		pl.Karma = Math.Max(pl.Karma, 0);
		pl.Ficha.hell_lockout_until = 0;
		pl.Ficha.passe_do_enma_ate = 0;
		pl.Ficha.Zeni = zeni;
		MoveToZone(pl.Id, alem, new Vec2(enma.X + ZoneCollision.TileSize, enma.Y));
		MandarFicha(pl);
		return true;
	}

	/// <summary>De volta a vida pelo `Renascer` de producao (o berco) -- a bancada nao deixa o personagem dela morto.</summary>
	internal void PalcoV30_Reviver(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !pl.Ficha.dead) return;
		pl.Ficha.passe_do_enma_ate = 0;
		Renascer(pl);
		MandarFicha(pl);
	}

	/// <summary>O BP base deste corpo -- o robo arma a reencarnacao com um numero conhecido.</summary>
	internal void PalcoV30_BpBase(int id, double bp)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Ficha.BP = bp;
	}

	/// <summary>(2026-10-26) O banco do potencial deste corpo, cravado -- a familia L confere que o renascido o leva.</summary>
	internal void PalcoV30_Banco(int id, double banco)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Ficha.hiddenpotential = banco;
	}

	/// <summary>(v0.3.0, item 30) O Spin Attack deste corpo pelo funil de producao (`GiratorioG7`), com o Ki cheio e a recarga zerada.</summary>
	internal void PalcoV30_Giro(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		_prontoG3.Remove(id);
		GiratorioG7(pl);
	}

	/// <summary>(v0.3.0, item 33) A marca deste corpo (0 = ninguem): a ronda das miragens depende dela.</summary>
	internal void PalcoV30_Marcar(int id, int alvo)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.AlvoId = alvo;
	}

	/// <summary>(v0.3.0, item 33) As miragens da Zanzoken Afterimage deste corpo acabam agora (e a deflexao emprestada volta).</summary>
	internal void PalcoV30_DesligarAsMiragens(int id) => EsquecerG14(id);

	/// <summary>(v0.3.0, item 28) A porcentagem guardada dos pesos deste corpo some -- o personagem que nunca ajustou.</summary>
	internal void PalcoV30_EsquecerAPorcentagem(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.pesoPct = 0;
		pl.SigAtributos = "";
	}

	/// <summary>A conta e o nome deste corpo.</summary>
	internal (string Conta, string Nome) PalcoV30_QuemE(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? (pl.Conta, pl.Name) : ("", "");

	/// <summary>O corpo VIVO desta conta com este nome, o BP base e o banco do potencial dele -- nulo se nao ha.</summary>
	internal (int Id, double BP, double Potencial)? PalcoV30_DaConta(string conta, string nome) =>
		_players.Values.FirstOrDefault(p => string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase)
											&& string.Equals(p.Name, nome, StringComparison.OrdinalIgnoreCase)
											&& !p.PersonagemConsumido) is { } achado
			? (achado.Id, achado.Ficha.BP, achado.Ficha.hiddenpotential)
			: null;

	/// <summary>
	/// A conta (a viva, ou a do disco): este slot esta ocupado? E o eco que ela guarda -- o BP base de quem reencarnou, de que o
	/// proximo leva 10% (2026-10-26, como no DU).
	/// </summary>
	internal (bool Ocupado, double? Eco) PalcoV30_Conta(string conta, int slot)
	{
		AccountSave? acc = ContaViva(conta) ?? _store?.Carregar(conta);
		return (acc?.Slots[slot] != null, acc?.Reencarnacao?.BPAntigo);
	}

	/// <summary>
	/// Esquece o eco desta conta, nas copias vivas e no disco: a prova do defeito da despedida reencarna de verdade, e um eco
	/// pendurado faria o personagem da proxima rodada nascer forte.
	/// </summary>
	internal void PalcoV30_EsquecerOEco(string conta)
	{
		EmTodaCopiaViva(conta, v => v.Reencarnacao = null);
		if (_store?.Carregar(conta) is { Reencarnacao: not null } noDisco)
		{
			noDisco.Reencarnacao = null;
			GravarAConta(noDisco);
		}
	}

	// =====================================================================
	// (v0.3.0, item 5) O CHEFE DE SAGA CACA O PLANETA INTEIRO
	// =====================================================================
	/// <summary>
	/// Um Freeza a ~30 tiles deste corpo (longe dos vinte do `MAX_AGGRO_RANGE`) e a pergunta de producao (`PresaDoHostil`),
	/// tres vezes: fora de uma saga, da saga, e da saga com o defeito do raio comum. O chefe sai do mundo na mesma chamada --
	/// nenhum tique de IA roda no meio. A distancia vai junto (o palco procura chao nos quatro rumos).
	/// </summary>
	internal (bool DaSaga, bool Avulso, bool ComDefeito, float Tiles) PalcoV30_ChefeCacaOPlaneta(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return default;
		const float t = ZoneCollision.TileSize;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		Vec2 longe = pl.Pos;
		foreach (Vec2 rumo in new[] { new Vec2(1, 0), new Vec2(-1, 0), new Vec2(0, 1), new Vec2(0, -1) })
		{
			Vec2 alvo = pl.Pos + rumo * (30 * t);
			Vec2 achado = mapa?.PontoLivrePerto(alvo) ?? alvo;
			if ((achado - pl.Pos).Length > (longe - pl.Pos).Length) longe = achado;
			if ((longe - pl.Pos).Length >= 25 * t) break;
		}
		ServerPlayer? chefe = NascerNpc("freeza_vegeta", pl.Zone, longe, 9_200);
		if (chefe?.Papel == null) return default;
		try
		{
			float tiles = (chefe.Pos - pl.Pos).Length / t;
			chefe.Papel.DaSaga = false;
			bool avulso = PresaDoHostil(chefe) == pl;
			chefe.PresaEngajada = 0;
			chefe.Papel.DaSaga = true;
			bool daSaga = PresaDoHostil(chefe) == pl;
			chefe.PresaEngajada = 0;
			ChefeComRaioComumDeTeste = true;
			bool comDefeito = PresaDoHostil(chefe) == pl;
			return (daSaga, avulso, comDefeito, tiles);
		}
		finally
		{
			ChefeComRaioComumDeTeste = false;
			RemoverNpc(chefe);
		}
	}

	// =====================================================================
	// (v0.3.0, item 35) O CHEFE DE SAGA, pelo nascimento de producao
	// =====================================================================
	private readonly List<ServerPlayer> _chefesDaBancadaV30 = [];

	/// <summary>Um chefe de saga NASCE pela porta de producao (`NascerChefeDaSaga`, a mesma da cadeia), no planeta pedido.</summary>
	private ServerPlayer? NascerChefeDeBancadaV30(string molde, string planeta)
	{
		if (_moldes?.Get(molde) is not { } m) return null;
		var ec = new EstadoDoChefe { Molde = molde, Bps = [.. m.Estagios.Select(s => s.Bp)] };
		var est = new EstadoDoElo { Id = "bancada_dv30_" + molde, Chefes = [ec] };
		var elo = new EloDaSaga
		{
			Id = est.Id, Nome = "bancada", Planeta = planeta,
			Chefes = [new ChefeDaSaga { Molde = molde, AnuncioDaChegada = $"{m.Nome} chegou -- e a bancada viu." }],
		};
		NascerChefeDaSaga(elo, est, ec);
		if (!_players.TryGetValue(ec.Corpo, out ServerPlayer? c)) return null;
		_chefesDaBancadaV30.Add(c);
		return c;
	}

	/// <summary>A chegada pra todos: um Freeza nasce NOUTRO planeta (o host nao o ve no mundo -- so a cena). O id, o nome e o planeta.</summary>
	internal (int Id, string Nome, string Planeta) PalcoV30_ChegadaDoChefe(int id, bool semCena = false)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return default;
		string planeta = pl.Zone.Name == "Namek" ? "Vegeta" : "Namek";
		ChegadaSemCenaDeTeste = semCena;
		try { return NascerChefeDeBancadaV30("freeza_namek", planeta) is { } c ? (c.Id, c.Name, NomeDoPlaneta(planeta)) : default; }
		finally { ChegadaSemCenaDeTeste = false; }
	}

	/// <summary>Um Freeza de saga AQUI, a tres tiles do host e sem mente -- a pergunta e a cena da troca de corpo, e nao a luta.</summary>
	internal int PalcoV30_ChefeAqui(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !pl.Zone.Equals(ZoneKey.Premade(pl.Zone.Name))) return 0;
		if (NascerChefeDeBancadaV30("freeza_namek", pl.Zone.Name) is not { } c) return 0;
		c.Cerebro = null;
		LargarOInput(c);
		Vec2 perto = MapaDaZonaOuCatalogo(pl.Zone)?.PontoLivrePerto(pl.Pos + new Vec2(3 * ZoneCollision.TileSize, 0)) ?? pl.Pos;
		CravarPosicao(c, perto);
		return c.Id;
	}

	/// <summary>O degrau seguinte do chefe pelo `AvancarEstagio` de producao -- o mesmo que o dano dispara. Devolve se ele ficou intocavel (em cena).</summary>
	internal bool PalcoV30_AvancarOChefe(int chefe, bool semCena = false)
	{
		if (!_players.TryGetValue(chefe, out ServerPlayer? c) || c.Papel == null) return false;
		ChefeSemCenaDeTeste = semCena;
		try { AvancarEstagio(c); }
		finally { ChefeSemCenaDeTeste = false; }
		return c.Combate.Intocavel;
	}

	/// <summary>Tira do mundo os chefes que a bancada fez nascer.</summary>
	internal void PalcoV30_TirarOsChefes()
	{
		foreach (ServerPlayer c in _chefesDaBancadaV30) if (_players.ContainsKey(c.Id)) RemoverNpc(c);
		_chefesDaBancadaV30.Clear();
	}

	// =====================================================================
	// (v0.3.0, item 22) O ULTIMATO DO FREEZA, pelo caminho de producao da saga
	// =====================================================================
	private (EloDaSaga Elo, EstadoDoElo Est, EstadoDoChefe Ec, ServerPlayer Chefe, int Quem, ZoneKey Volta, Vec2 VoltaPos, double VidaAntes)? _ultimatoV30;

	/// <summary>
	/// Este corpo vai (vivo) pro Outro Mundo, um Freeza de saga nasce a cinco tiles dele, e o ULTIMATO da saga de Vegeta
	/// ja vencido roda uma vez -- e ele ergue a Supernova (`CorrerOUltimato`, a porta de producao). A saga e uma COPIA com
	/// o planeta trocado pelo Outro Mundo, que NAO e um planeta: na explosao o `ComecarDestruicao` recusa, e nada e
	/// destruido. Devolve o id do chefe (0 = nao montou).
	/// </summary>
	internal int PalcoV30_Ultimato(int id, string saga = "freeza_vegeta")
	{
		PalcoV30_DesfazerOUltimato();
		// (2026-10-17) A SAGA E ESCOLHIDA: o Freeza de Namek ergue a Death Ball pela mesma porta (`ataqueDoUltimato` da saga).
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _moldes?.Cadeia.FirstOrDefault(e => e.Id == saga) is not { } vegeta)
			return 0;
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		ZoneKey volta = pl.Zone;
		Vec2 voltaPos = pl.Pos;
		if (!pl.Zone.Equals(alem)) MoveToZone(pl.Id, alem, MesaDoEnma(alem));
		Vec2 onde = MapaDaZonaOuCatalogo(alem)?.PontoLivrePerto(pl.Pos + new Vec2(5 * ZoneCollision.TileSize, 0))
					?? pl.Pos + new Vec2(5 * ZoneCollision.TileSize, 0);
		ServerPlayer? chefe = NascerNpc(vegeta.Chefes[0].Molde, alem, onde, 9_300);
		if (chefe?.Papel == null) return 0;
		chefe.Papel.DaSaga = true;

		var elo = new EloDaSaga
		{
			Id = "bancada_supernova", Nome = vegeta.Nome, Planeta = alem.Name, DestroiPlaneta = true,
			MinutosDeLuta = vegeta.MinutosDeLuta, MinutosDeOcio = vegeta.MinutosDeOcio,
			AnuncioDoUltimato = vegeta.AnuncioDoUltimato, Chefes = vegeta.Chefes, AtaqueDoUltimato = vegeta.AtaqueDoUltimato,
		};
		var ec = new EstadoDoChefe
		{
			Molde = vegeta.Chefes[0].Molde, Fase = FaseDoChefe.NoMundo, Corpo = chefe.Id, Engajou = true,
			UltimatoEm = _relogioDoMundo - 1,
		};
		var est = new EstadoDoElo { Id = elo.Id, Fase = FaseDaSaga.EmCurso, Chefes = [ec] };
		_ultimatoV30 = (elo, est, ec, chefe, pl.Id, volta, voltaPos, pl.Ficha.KO ? -1 : pl.Ficha.HP);
		CorrerOUltimato(elo, est, ec, chefe);
		return chefe.Id;
	}

	/// <summary>Uma volta do ultimato, como o tique da saga faria. Verdadeiro enquanto o chefe ainda esta no mundo.</summary>
	internal bool PalcoV30_TiqueDoUltimato()
	{
		if (_ultimatoV30 is not { } u || !_players.ContainsKey(u.Chefe.Id)) return false;
		CorrerOUltimato(u.Elo, u.Est, u.Ec, u.Chefe);
		return _players.ContainsKey(u.Chefe.Id);
	}

	/// <summary>
	/// Quem a bancada pos ao lado do chefe saiu do ultimato INTEIRO? De pe e com a vida de quando a Supernova comecou: o
	/// chefe com o sol erguido a cinco tiles nao soca, nao investe e nao atira (`MaosOcupadasDaIa`).
	/// </summary>
	internal (bool Intacto, double Antes, double Agora) PalcoV30_IntactoNoUltimato() =>
		_ultimatoV30 is { } u && _players.TryGetValue(u.Quem, out ServerPlayer? pl)
			? (u.VidaAntes >= 0 && !pl.Ficha.KO && pl.Ficha.HP >= u.VidaAntes - 0.5, u.VidaAntes, pl.Ficha.HP)
			: default;

	/// <summary>O chefe esta concentrando a esfera do ultimato (a Supernova ou a Death Ball)? Intocavel? Parado?</summary>
	internal (bool Supernova, bool Intocavel, bool Parado) PalcoV30_ComoEstaOChefe(int id) =>
		_players.TryGetValue(id, out ServerPlayer? c)
			? (CorpoOcupado.NoUltimato(OcupacaoDe(c)), c.Combate?.Intocavel == true, !PodeMexerOCorpo(c))
			: default;

	// =====================================================================
	// (v0.3.0, adendo 11) O REFUGIO PELA TERRA: o natal deste corpo morre num palco, e a oferta sai pelo funil de producao
	// =====================================================================
	private PalcoDeMortes? _refugioV30Palco;
	private Jandirus.Core.Races.Berco _refugioV30Berco;

	/// <summary>
	/// O NATAL DESTE CORPO ACABA: o berco passa a ser Namek, Namek morre num palco de bancada (o `planetas-mortos.json` do
	/// dono fica intacto) e a oferta do refugio sai como na chegada ao Outro Mundo -- a tela abre sozinha. Falso se Namek nao
	/// morreu.
	/// </summary>
	internal bool PalcoV30_SemNatal(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		PalcoV30_DesfazerSemNatal(id);
		_refugioV30Berco = pl.Berco;
		pl.Berco = new Jandirus.Core.Races.Berco { Planeta = "Namek", PreFeito = true, Natal = "Namek" };
		_refugioV30Palco = PalcoDeMortesDeBancada();
		if (!MatarPlanetaNoPalco(ZoneKey.Premade("Namek"), "diagv030: o refugio pela Terra")) return false;
		pl.RefugioPerguntado = false;
		OferecerORefugio(pl, podeAbrir: true);
		return true;
	}

	/// <summary>A oferta de novo, sem pedir a tela -- o robo a usa depois de ligar o defeito.</summary>
	internal void PalcoV30_OfertaDoRefugio(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) OferecerORefugio(pl, podeAbrir: false);
	}

	/// <summary>O que o servidor guarda: este corpo escolheu voltar pela Terra?</summary>
	internal bool PalcoV30_RefugioNaTerra(int id) => _players.TryGetValue(id, out ServerPlayer? pl) && pl.RefugioNaTerra;

	/// <summary>Fecha o palco: Namek volta, o berco de antes volta, a escolha se apaga e a tela recebe o "berco de pe".</summary>
	internal void PalcoV30_DesfazerSemNatal(int id)
	{
		if (_refugioV30Palco is not { } palco) return;
		_refugioV30Palco = null;
		palco.Dispose();
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Berco = _refugioV30Berco;
		pl.RefugioNaTerra = false;
		ComandoDeRefugio(pl, "refugio", "");   // com o berco de pe ele manda o pacote "limpo" -- a tela e o cartao somem
	}

	/// <summary>Fecha o palco do ultimato: o chefe sai do mundo (se ainda esta) e o corpo volta pra onde estava.</summary>
	internal void PalcoV30_DesfazerOUltimato()
	{
		if (_ultimatoV30 is not { } u) return;
		_ultimatoV30 = null;
		if (_players.ContainsKey(u.Chefe.Id)) RemoverNpc(u.Chefe);
		if (_players.TryGetValue(u.Quem, out ServerPlayer? pl) && !pl.Zone.Equals(u.Volta)) MoveToZone(pl.Id, u.Volta, u.VoltaPos);
	}

	// =====================================================================
	// (v0.3.0, adendo 12) O KAIO-KEN, A GENKIDAMA E A PERGUNTA DO REFUGIO -- os palcos das familias KK, GK e TR
	// =====================================================================
	/// <summary>
	/// A LICAO DO SR. KAIOH, pela porta de producao (`LicaoDeNpc`: o livro, os poderes e o pacote das skills): o corpo aprende
	/// o Kaio-ken. E a cena comeca de quem acabou de aprender -- sem o ultimo multiplicador, na maestria inicial, apagado e com
	/// o Ki ate 100% (acima disso o X so libera o excesso). Verdadeiro se ele sabe.
	/// </summary>
	internal bool PalcoV30_AprenderOKaioken(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		bool sabe = LicaoDeNpc(pl, NpcsDeConversa.SkillDoKaioken) || pl.Livro.Sabe(NpcsDeConversa.SkillDoKaioken);
		if (TemBuff(pl, BuffKaiokenG2)) ReverterKaiokenG2(pl, porKO: false);
		pl.Ficha.KaiokenVezes = 0;
		pl.Ficha.KaiokenMastery = 0;
		pl.Ficha.Ki = Math.Min(pl.Ficha.Ki, pl.Ficha.MaxKi);
		pl.SigAtributos = "";
		return sabe;
	}

	/// <summary>O Kaio-ken deste corpo, como o servidor o ve: aceso, em que multiplicador, e a maestria que vale.</summary>
	internal (bool Aceso, double Vezes, double Maestria) PalcoV30_Kaioken(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl)
			? (TemBuff(pl, BuffKaiokenG2), _kaiokenAmtG2.GetValueOrDefault(pl.Id), MaestriaKaiokenG2(pl))
			: default;

	/// <summary>Apaga o Kaio-ken deste corpo, se aceso -- a limpeza do robo.</summary>
	internal void PalcoV30_ApagarOKaioken(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && TemBuff(pl, BuffKaiokenG2)) ReverterKaiokenG2(pl, porKO: false);
	}

	/// <summary>A outra licao do Sr. Kaioh (a Genkidama, `LicaoDeNpc`), e o Ki cheio pra erguer a bomba (ela custa 90%).</summary>
	internal bool PalcoV30_AprenderAGenkidama(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		bool sabe = LicaoDeNpc(pl, NpcsDeConversa.SkillDaGenkidama) || pl.Livro.Sabe(NpcsDeConversa.SkillDaGenkidama);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		return sabe;
	}

	/// <summary>A fase da Genkidama deste corpo: -1 nenhuma; 0 formando, 1 crescendo, 2 disparo pedido, 3 lancada (a trava).</summary>
	internal int PalcoV30_FaseDaGenkidama(int id) => _genkidamaG12.TryGetValue(id, out EstadoDaGenkidamaG12? g) ? g.Fase : -1;

	/// <summary>A oferta do refugio como no LOGIN (`podeAbrir`): sem a marca a tela se empurra, com ela nao.</summary>
	internal void PalcoV30_OfertaDoLogin(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) OferecerORefugio(pl, podeAbrir: true);
	}

	/// <summary>A marca do "ja perguntei" deste corpo -- a que o disco guarda (`ServerPlayer.RefugioPerguntado`).</summary>
	internal bool PalcoV30_RefugioPerguntado(int id) => _players.TryGetValue(id, out ServerPlayer? pl) && pl.RefugioPerguntado;

	/// <summary>(adendo 18) A Death Ball pelo livro de verdade (a skill de cargo do Freeza), o Ki cheio e a mao livre.</summary>
	internal bool PalcoV30_AprenderADeathBall(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		if (!pl.Livro.Sabe(PathDeathBallG12)) { pl.Livro.Dar(PathDeathBallG12); MandarSkills(pl); }
		// O TANQUE QUE PAGA A BOLA INTEIRA: o aperto pede 150 x o dreno-base e cada estagio mais um terco disso -- mais que o tanque inteiro
		// do personagem novo da bancada (Ki maximo ~140 contra 205). O Ki ACIMA de 100% nao serve de tanque: o excesso escorre
		// (`CargaDeKi.PrecoDoExcesso`, acima do teto seguro a 10% por segundo dividido pela pericia) e o aperto chegava ao servidor com o Ki
		// ja abaixo do preco -- "isso pede pelo menos 205 de energia" (os job94 e job95; antes passava pela folga de uns quadros). O tanque
		// cresce pelo caminho de que ele sai, o `baseKi` (ver `FotoDaVariedade`); o preco cresce com a RAIZ do tanque, entao dobrar o tanque
		// ate caberem tres apertos termina.
		for (int i = 0; i < 16 && pl.Ficha.MaxKi < 3 * 150 * pl.Ficha.BaseDrain(); i++)
		{
			pl.Ficha.baseKi = Math.Max(pl.Ficha.baseKi, 1) * 2;
			pl.Ficha.Statify();
		}
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		_blastPronto.Remove(pl.Id);
		return true;
	}

	/// <summary>A Death Ball deste corpo: -1 nenhuma; 0 carregando; 1 lancada e guiada (a trava).</summary>
	internal int PalcoV30_EstadoDaDeathBall(int id) =>
		_deathBallG12.TryGetValue(id, out EstadoDaDeathBallG12? db) ? (db.Lancada ? 1 : 0) : -1;

	/// <summary>O aperto que larga a guia, pela porta de producao -- so com a bola lancada e guiada (nunca abre outra).</summary>
	internal void PalcoV30_LargarAGuiaDaDeathBall(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && _deathBallG12.TryGetValue(id, out EstadoDaDeathBallG12? db)
			&& db.Lancada && db.Guiando)
			DeathBallG12(pl);
	}

	/// <summary>
	/// (adendo 18) O MORTO NO OUTRO MUNDO COM A TRANSMISSAO (a skill de verdade) e um VIVO na Terra pra mirar. Desde 2026-10-21 o
	/// alcance e o nivel da skill, como o do Sense (`TransmissaoEmNiveis`): do Outro Mundo a Terra so o nivel 5, o universo, alcanca --
	/// e ele concentra em 1 s. Devolve o nome do vivo, ou vazio.
	/// </summary>
	internal string PalcoV30_MortoComUmVivoNaTerra(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !PalcoV30_MortoAoLadoDoEnma(id, 0)) return "";
		if (!pl.Livro.Sabe("/datum/skill/shunkanido")) { pl.Livro.Dar("/datum/skill/shunkanido"); MandarSkills(pl); }
		pl.Niveis.Por(Jandirus.Core.Skills.TransmissaoEmNiveis.Path, Jandirus.Core.Skills.TransmissaoEmNiveis.Universo);
		pl.Ficha.teleskill = 100;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		ZoneKey terra = ZoneKey.Premade("Earth");
		ServerPlayer vivo = ForjarV2("Kuririn", PontoDeNascimento(terra) + new Vec2(ZoneCollision.TileSize * 3, 0), Math.Max(pl.Ficha.BP, 1) * 10);
		vivo.Conta = "bancada_dvtrinta_vivo";   // a ASSINATURA: a transmissao so mira pessoa
		return vivo.Name;
	}

	/// <summary>Os corpos forjados pela bancada saem do mundo (o vivo da transmissao) -- o jogador nao e um deles.</summary>
	internal void PalcoV30_LimparOsCorposDaBancada() => LimparTudoDaBancada();
}
