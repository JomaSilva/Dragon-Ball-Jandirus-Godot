using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.Torneio;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--torneiocellteste` -- O TORNEIO DO CELL (dono, 2026-10-19). O pedido e as respostas do dono estao em `Core/Npc/TorneioDoCell.cs`.
/// Quatro jogadores entram pela porta (Goku, Kuririn, Vegeta e Gohan) e cada familia mede pelas funcoes de PRODUCAO -- o monitor e a virada
/// do dia da saga (`TickDasSagas`, `VirouODia`), o tique do torneio (`TickDoTorneioDoCell`), o roteiro do chefe (`TickDoRoteiro`) --, com o
/// contra-exemplo de cada regra nova:
///   A. A FORMA PERFEITA MARCA O TORNEIO: ele anuncia, sai do mundo e fica a caminho, com 5 dias e a marca no disco. Contra: `CellSemTorneioDeTeste`.
///   B. OS DIAS E A ARENA: o lembrete de cada dia; no 5o ele nasce NA ARENA, parado e intocavel; o torneio de artes marciais nao abre.
///   C. SO JOGADOR, UM POR VEZ: o habitante na arena nao desafia (contra: `NpcDesafiaOCellDeTeste`); o primeiro jogador a pisar e chamado, os
///      golpes ficam nao letais, e quem pisa depois e posto pra fora (contra: `ArenaNaoEsvaziaDeTeste`); a contagem e o "COMECEM!".
///   D. O X1 E X1: o golpe de quem se mete e desfeito e ele fica fora (contra: `QuemSeMeteFicaDeTeste`); o do desafiante fica.
///   R. NO X1 ELE NAO TROCA DE FORMA POR DANO: a Super Perfeita e a da morte. Contra: `CellTransformaNoTorneioDeTeste`.
///   E. PISOU FORA, PERDEU: o ring-out do desafiante, e a chance que nao volta (nem pra quem se meteu).
///   S. O SANGUE DO CELL (2026-10-19): Saiyajin, Namekuseijin e Frost Demon, as seis skills de raca escolhidas desse sangue, o Zenkai, o
///      folego no vacuo e a Regenerate.
///   F. O CELL PISA FORA E A REGRA CAI: a luta segue, e o desafiante fora ja nao perde. Contra: `RegraNaoCaiDeTeste`. E (2026-10-19) ele solta
///      os SETE CELL JR -- clones dele com 90% do BP, a folha do Cell Jr --, que miram a plateia e nunca o desafiante; o golpe da plateia neles
///      fica, o deles no desafiante e desfeito. Contra: `SemCellJrDeTeste`.
///   G. NOCAUTEADO NO X1 ELE MORRE NA LUTA, e volta Super Perfeito, solto: a luta vira livre. (2026-10-28) A volta e pela Transmissao, ao lado
///      de quem o matou (o pedido 147; antes, no meio da arena).
///   H. A FILA VAZIA POR 5 MIN: o Kamehameha, e a Terra comecando a explodir (a bancada aborta a destruicao antes do commit). (2026-10-19) O
///      Kamehameha e o RAIO da tecnica, 2x (contra: `KamehamehaDoTamanhoNormalDeTeste`), pelo laco do jogo: o canal, a cabeca, o fim.
///   K. (2026-10-19) O EMBATE CONTRA O KAMEHAMEHA, Gohan de frente pra ele com o Kamehameha DELE, pelo laco do jogo: o empate o faz carregar
///      outro (contra: `EmpateExplodeATerraDeTeste`); Gohan vencendo, o Cell morre NA HORA (contra: `CellSobreviveAoEmbateDeTeste`) e DE VEZ --
///      a saga fecha vencida, sem a volta Super Perfeita (contra: `CellVoltaDoEmbateDeTeste`); o Cell vencendo, o Kamehameha segue e a Terra
///      explode. Os quatro ficam intocaveis: o raio atravessa quem esta intocavel (`Colidiu`), e a bancada precisa deles de pe.
///   L. (2026-10-20) O SUPER PERFEITO PERDENDO -- o tronco ou a cabeca abaixo de 25% (a regra pura: 26% e 25% ainda nao, braco nao conta) --,
///      na luta livre, solta o MESMO Kamehameha pelo vigia do tique (contra: `SuperPerfeitoNaoSoltaDeTeste`); o Perfeito nao. Gohan vencendo a
///      disputa, ele morre de vez; Gohan PERDENDO, a Terra explode (*"Se o jogador perder o clash obviamente a terra tb explode"*). E ele SEMPRE o
///      solta: nocauteado, levanta e solta (contra: `SuperPerfeitoCaidoNaoSoltaDeTeste`); e o golpe que o mataria vira o Kamehameha (contra:
///      `SuperPerfeitoMorreDeTeste`).
///   M. (2026-10-20) O CELL CACA OS ANDROIDES: sem androide caido, o alvo do Cell Imperfeito e o androide de pe mais perto, mesmo com um
///      jogador mais perto (contra: `CellNaoCacaOsAndroidesDeTeste`); o golpe dele que mataria o androide so o derruba (contra:
///      `CellMataOAndroideDeTeste`); e o caido e absorvido. E no x1 o monitor da saga nao apaga o desafiante do Cell -- familia D (contra:
///      `AbsorcaoMexeNoTorneioDeTeste`). E (2026-10-20, depois da g6) perto do caido ele da o BOTE e o absorve (contra: `CellSemBoteDeTeste`), e na
///      cena da transformacao nao absorve ninguem (contra: `ChefeAgeNaCenaDeTeste`).
///   P. (2026-10-20) NA CENA DA TRANSFORMACAO O CELL NAO AGE: ele larga o raio que segurava e fica intocavel, e por oito segundos de jogo com um
///      jogador colado nele nao golpeia, nao atira e nao anda (contra: `ChefeAgeNaCenaDeTeste`). OITO segundos: com o raio na mao ele so age
///      quando a carga fecha, e a g5 mediu tres -- a contraprova sem as duas linhas passou nessa janela.
///   Q. (2026-10-20) ...E TODO CHEFE DE SAGA (*"todo chefe de saga fica parado, sem atacar e intocável na cena da própria transformação (o Freeza
///      também)"*): o Freeza de Namek larga o Death Beam e fica intocavel na troca de corpo, e na forma final nao age por dez segundos de jogo
///      com um jogador colado nele (contra: `ChefeAgeNaCenaDeTeste`); e a Death Ball do ultimato, que escreve o mesmo relogio da cena, segue
///      como era -- o cerebro ligado.
///   W. (2026-10-20) O DOBRO DO JOGADOR MAIS FORTE (*"faça os stats do boss(todos os de saga e contando o bills) ser o dobro do jogador mais
///      forte, a pericia deles sempre é a maxima"*): o Cell Perfeito da saga nasce com os oito atributos da aba Stats no dobro dos do Goku (o de
///      maior BP) e as pericias de luta em 100 (a de raio em 250, a do Gohan), e acompanha o Goku no tique seguinte do roteiro; o raio de
///      potencia 2 de quem tem 1,5 bilhao (o caso do dono) nao o derruba em doze segundos, e o embate e dele; o Bills tambem, o Whis nao, e todo
///      chefe de saga (contra: `ChefeSemODobroDeTeste`). (2026-10-27, o pedido 138) O DOBRO VIROU 1,25x: a regua e o numero do pedido
///      (<see cref="TcelVezesDoPedido"/>, e NAO o `VezesOMaisForte` do servidor -- a contraprova muda aquele), e o raio do caso do dono passou a
///      ser MEDIDO (impresso) e comparado com o da ficha de recem-criado, que tem de tirar mais de tres vezes o que tira do chefe de agora.
///   J. (2026-10-20) A CABECA DO CELL JR DE FRENTE: nas 24 poses da folha dele, de frente a crista chega tao alto quanto de costas -- a arte do DM
///      cortava as duas pontas de frente (contra: a mesma arte, a folha dos mobs do DU, sem o conserto).
///
/// O MUNDO VOLTA COMO ESTAVA: as sagas e o relogio sao devolvidos (e regravados), os chefes saem, a Terra e devolvida e as contas sao apagadas.
/// </summary>
public sealed partial class GameServer
{
	private bool _tcelRodando, _tcelJaRodou;
	private int _tcelOk, _tcelFalhou;

	private void AfirmarTcel(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _tcelOk++; GD.Print($"[torneiocell]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _tcelFalhou++; GD.PrintErr($"[torneiocell]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoTorneioDoCell(int porta)
	{
		if (_tcelRodando || _tcelJaRodou) return;
		_tcelRodando = _tcelJaRodou = true;
		_tcelOk = _tcelFalhou = 0;
		GD.Print("[torneiocell] ================ O TORNEIO DO CELL (dono, 2026-10-19) ================");

		EstadoDasSagas guardado = _sagas;
		double relogioGuardado = _relogioDoMundo;
		var terra = ZoneKey.Premade("Earth");
		ClienteCruDeTeste ca = new(), cb = new(), cc = new(), cd = new();
		var mesa = new MesaDeClientesCrus(this, porta, ca, cb, cc, cd);
		string[] contas = ["bancada_tc_goku", "bancada_tc_kuririn", "bancada_tc_vegeta", "bancada_tc_gohan"];
		try
		{
			ServerPlayer? goku = mesa.EntraNoMundo(ca, contas[0], "Goku"), kuririn = mesa.EntraNoMundo(cb, contas[1], "Kuririn");
			ServerPlayer? vegeta = mesa.EntraNoMundo(cc, contas[2], "Vegeta"), gohan = mesa.EntraNoMundo(cd, contas[3], "Gohan");
			Arena arena = _torneioCfg.Terra.Arena;
			EloDaSaga? elo = Array.Find(_cadeia, e => e.Id == "cell");
			ChefeDaSaga? ficha = elo == null ? null : Array.Find(elo.Chefes, c => c.Molde == "cell");
			AfirmarTcel("(montagem) os quatro jogadores entram pela porta; o Cell da saga tem o torneio na Perfeita (degrau 3, 5 dias) e a arena da Terra tem mapa",
					  goku != null && kuririn != null && vegeta != null && gohan != null && EhJogador(goku) && ficha is { TorneioNoDegrau: 3, DiasAteOTorneio: 5 }
					  && MapaDaZonaOuCatalogo(terra) != null && _moldes != null, $"ficha {ficha?.TorneioNoDegrau}/{ficha?.DiasAteOTorneio}; {arena}");
			if (goku == null || kuririn == null || vegeta == null || gohan == null || elo == null || ficha == null || _moldes == null) return;

			ServerPlayer[] todos = [goku, kuririn, vegeta, gohan];
			for (int i = 0; i < todos.Length; i++) Transportar(todos[i], terra, ForaDaArenaDeTeste(arena, i));
			AfirmarTcel("(montagem) os quatro na Terra, fora da arena", todos.All(p => p.Zone.Hash == terra.Hash && !arena.ContemEm(p.Pos)),
					  string.Join(", ", todos.Select(p => $"{p.Name} {p.Zone.Name} ({p.Pos.X / 32:0},{p.Pos.Y / 32:0})")));
			EscutaDoTorneioDoCell = [];
			EscutaDasSagas = [];

			TcelACabecaDoCellJr();
			if (TcelAFormaPerfeita(elo, ficha) is not { } ec) return;
			if (TcelOsDiasEAArena(ec, arena) is not { } cell) return;
			bool letalDoGoku = goku.Combate.Letal, letalDoKuririn = kuririn.Combate.Letal;
			if (!TcelSoJogador(cell, arena, goku, kuririn)) return;
			TcelOX1EX1(cell, goku, vegeta);
			TcelSemFormaPorDano(cell);
			TcelPisouForaPerdeu(cell, arena, goku, vegeta, letalDoGoku);
			if (!TcelARegraCai(cell, arena, kuririn)) return;
			TcelMortoNaLuta(cell, arena, ec, kuririn, letalDoKuririn);
			TcelAFilaVazia(arena, todos);
			TcelOEmbate(arena, todos);
			TcelOSuperPerfeitoPerdendo(arena, todos);
			TcelACacaAosAndroides(todos);
			TcelACenaDaTransformacao(todos);
			TcelACenaDoFreeza(arena, todos);
			TcelODobroDoMaisForte(arena, todos);
		}
		catch (Exception e)
		{
			AfirmarTcel("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			CellSemTorneioDeTeste = QuemSeMeteFicaDeTeste = RegraNaoCaiDeTeste = ArenaNaoEsvaziaDeTeste = false;
			CellTransformaNoTorneioDeTeste = NpcDesafiaOCellDeTeste = CellCacaNaEsperaDeTeste = SemCellJrDeTeste = CellJrSemOLivroDoCellDeTeste = false;
			KamehamehaDoTamanhoNormalDeTeste = CellSobreviveAoEmbateDeTeste = CellVoltaDoEmbateDeTeste = EmpateExplodeATerraDeTeste = false;
			SuperPerfeitoNaoSoltaDeTeste = SuperPerfeitoCaidoNaoSoltaDeTeste = SuperPerfeitoMorreDeTeste = false;
			CellNaoCacaOsAndroidesDeTeste = CellMataOAndroideDeTeste = AbsorcaoMexeNoTorneioDeTeste = CellSemBoteDeTeste = false;
			ChefeAgeNaCenaDeTeste = false;
			ChefeSemODobroDeTeste = false;
			foreach (int id in _canais.Keys.ToList()) FecharCanal(id, _canais[id], null);
			foreach (int id in _emEmbateDeKi.Keys.ToList()) SoltarDoEmbateDeKi(id);
			EncerrarOTorneioDoCell();
			_kamehamehaDoCell = null;
			foreach (int id in _cellJrs.ToList()) if (_players.TryGetValue(id, out ServerPlayer? jr)) RemoverNpc(jr);
			_cellJrs.Clear();
			if (_torneio is { } aberto) EncerrarTorneio(aberto);
			EscutaDoTorneioDoCell = null;
			EscutaDasSagas = null;
			foreach (ServerPlayer c in ChefesNoMundo()) RemoverNpc(c);
			if (MorteDaZona(terra) is { Fase: FaseDaMorte.Explodindo }) AbortarMorte(terra, "bancada do torneio do Cell");
			_relogioDoMundo = relogioGuardado;
			_sagas = guardado;
			_sagasPorRetomar = false;
			SalvarSagas();
			LimparTudoDaBancada();
			mesa.Fecha();
			if (_store != null)
				foreach (string conta in contas)
					try
					{
						string caminho = System.IO.Path.Combine(_store.Pasta, conta + ".json");
						if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
					}
					catch (Exception e) { GD.PushWarning($"[torneiocell] nao apaguei '{conta}': {e.Message}"); }
			_tcelRodando = false;
			GD.Print($"[torneiocell] ================ {_tcelOk} passaram, {_tcelFalhou} falharam ================");
		}
	}

	// =====================================================================
	// OS LUGARES
	// =====================================================================

	/// <summary>Um chao livre ao sul da arena, um por jogador (cinco celulas alem da beirada, dois tiles entre um e outro).</summary>
	private Vec2 ForaDaArenaDeTeste(Arena a, int i)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(ZoneKey.Premade("Earth"));
		Vec2 alvo = new Vec2((a.X1 + a.X2) / 2 + 2 * i + 0.5f, a.Y2 + 5 + 0.5f) * ZoneCollision.TileSize;
		return mapa?.PontoLivrePerto(alvo) ?? alvo;
	}

	/// <summary>Uma celula DENTRO da arena, na terceira linha a partir do norte, deslocada do meio.</summary>
	private static Vec2 DentroDaArenaDeTeste(Arena a, int dx) =>
		new Vec2((a.X1 + a.X2) / 2 + dx + 0.5f, a.Y1 + 3 + 0.5f) * ZoneCollision.TileSize;

	private string EscutaDoCell() => string.Join(" | ", EscutaDoTorneioDoCell ?? []);

	// =====================================================================
	// J. A CABECA DO CELL JR DE FRENTE (dono, 2026-10-20)
	// =====================================================================
	/// <summary>A folha do Cell Jr como o DM a deixou (a dos mobs do DU, a mesma arte e as mesmas poses): o contra-exemplo da familia J.</summary>
	private const string FolhaDoCellJrDoDm = "res://Assets/Sprites/DU/Mobs/Cell Jr.tres";

	private void TcelACabecaDoCellJr()
	{
		GD.Print("[torneiocell] -- J) a cabeca do Cell Jr de frente: a crista inteira, como de costas");
		(int poses, int inteiras, string pior) = TcelCabecasDeFrente(TorneioDoCell.FolhaDoCellJr);
		AfirmarTcel("**O CELL JR DE FRENTE TEM A CABECA INTEIRA** (*\"os cell jr tao com o topo da cabeça cortados\"*): nas 24 poses da folha dele, a "
					+ "crista de frente chega tao alto quanto a de costas -- as duas pontas, que a arte do DM cortava de frente",
					poses == 24 && inteiras == poses, $"{inteiras} de {poses} poses; {pior}");
		(int posesDoDm, int inteirasDoDm, string piorDoDm) = TcelCabecasDeFrente(FolhaDoCellJrDoDm);
		AfirmarTcel("   CONTRA-EXEMPLO (a arte do DM, a folha dos mobs do DU, sem o conserto): a cabeca de frente sai cortada -- a regua de cima reprovaria",
					posesDoDm == 24 && inteirasDoDm == 0, $"{inteirasDoDm} de {posesDoDm} poses; {piorDoDm}");
	}

	/// <summary>
	/// QUANTAS POSES DE FRENTE CHEGAM AO TOPO DAS DE COSTAS: cada quadro `*_south` contra o `*_north` da mesma posicao da animacao -- a primeira
	/// linha com desenho tem de ser a mesma (a cabeca e uma so; de frente ou de costas, ela vai ate o mesmo lugar). O PNG da folha e lido do
	/// DISCO (`Image.LoadFromFile`), pelos retangulos da `SpriteFrames` dela: a bancada roda sem tela, e o que se mede e a arte que vai pro jogo.
	/// Devolve (poses, quantas chegam, a primeira que nao chega).
	/// </summary>
	private static (int Poses, int Inteiras, string Pior) TcelCabecasDeFrente(string folha)
	{
		if (GD.Load<SpriteFrames>(folha) is not { } sf) return (0, 0, $"a folha '{folha}' nao carregou");
		Image? img = null;
		int poses = 0, inteiras = 0;
		string pior = "";
		foreach (string anim in sf.GetAnimationNames())
		{
			if (!anim.EndsWith("_south", StringComparison.Ordinal)) continue;
			string costas = anim[..^"_south".Length] + "_north";
			if (!sf.HasAnimation(costas)) continue;
			for (int i = 0; i < sf.GetFrameCount(anim) && i < sf.GetFrameCount(costas); i++)
			{
				if (sf.GetFrameTexture(anim, i) is not AtlasTexture frente || sf.GetFrameTexture(costas, i) is not AtlasTexture tras) continue;
				img ??= Image.LoadFromFile(frente.Atlas.ResourcePath);
				if (img == null) return (poses, inteiras, $"o PNG '{frente.Atlas.ResourcePath}' nao abriu");
				poses++;
				int deFrente = TcelPrimeiraLinha(img, frente.Region), deCostas = TcelPrimeiraLinha(img, tras.Region);
				if (deFrente == deCostas) inteiras++;
				else if (pior.Length == 0) pior = $"{anim}[{i}]: de frente comeca na linha {deFrente}, de costas na {deCostas}";
			}
		}
		return (poses, inteiras, pior);
	}

	/// <summary>A primeira linha do retangulo com algum pixel desenhado (-1 = vazio).</summary>
	private static int TcelPrimeiraLinha(Image img, Rect2 r)
	{
		for (int y = 0; y < (int)r.Size.Y; y++)
			for (int x = 0; x < (int)r.Size.X; x++)
				if (img.GetPixel((int)r.Position.X + x, (int)r.Position.Y + y).A > 0) return y;
		return -1;
	}

	// =====================================================================
	// A. A FORMA PERFEITA MARCA O TORNEIO
	// =====================================================================
	private EstadoDoChefe? TcelAFormaPerfeita(EloDaSaga elo, ChefeDaSaga ficha)
	{
		GD.Print("[torneiocell] -- A) a forma Perfeita marca o torneio: ele anuncia e some");
		SagasLimpas();
		foreach (string saga in new[] { "freeza_vegeta", "freeza_namek" }) _sagas.Do(saga)!.Fase = FaseDaSaga.Vencida;
		(_, _, EstadoDoChefe ec) = SagaComOChefeNoMundo("cell", "cell");
		ServerPlayer? cell = _players.TryGetValue(ec.Corpo, out ServerPlayer? c0) ? c0 : null;
		if (cell?.Papel == null) { AfirmarTcel("(montagem) o Cell nasce pela saga, na Terra", false, $"{ec.Fase}"); return null; }
		TcelOSangueDoCell(cell);

		// A PERFEITA, pelo caminho do roteiro (sem a cena: a cena e a da absorcao, e o anuncio espera ela acabar -- a bancada a pula)
		cell.Papel.Estagio = ficha.TorneioNoDegrau - 1;
		EntrarNoDegrau(cell, cura: 0, anunciar: false);
		int id = cell.Id;

		CellSemTorneioDeTeste = true;
		try { TickDasSagas(); }
		finally { CellSemTorneioDeTeste = false; }
		AfirmarTcel("   CONTRA-EXEMPLO (o Cell sem torneio, o jogo de antes): Perfeito, ele segue no mundo -- a regua de baixo reprovaria",
				  _players.ContainsKey(id) && ec is { Fase: FaseDoChefe.NoMundo, NoTorneio: false }, $"{ec.Fase}");

		EscutaDoTorneioDoCell!.Clear();
		TickDasSagas();
		AfirmarTcel("**A FORMA PERFEITA MARCA O TORNEIO** (*\"se ele virar perfeito, ele avisa q em 5 dias tera um torneio do cell\"*): ele anuncia, SAI do "
				  + "mundo e fica a caminho -- Perfeito, com 5 dias e a marca do torneio",
				  !_players.ContainsKey(id) && ec is { Fase: FaseDoChefe.ACaminho, NoTorneio: true, DiasRestantes: 5, Degrau: 2 }
				  && EscutaDoTorneioDoCell.Any(f => f.Contains("TORNEIO DO CELL", StringComparison.Ordinal) && f.Contains("5 dia", StringComparison.Ordinal)),
				  $"no mundo {_players.ContainsKey(id)}; {ec.Fase}; torneio {ec.NoTorneio}; dias {ec.DiasRestantes}; degrau {ec.Degrau + 1}; {EscutaDoCell()}");
		EstadoDoChefe? noDisco = SagasDoDisco()?.Do("cell")?.Chefes.Find(c => c.Molde == "cell");
		AfirmarTcel("...e a marca vai pro DISCO com a saga: o torneio marcado atravessa o reinicio", noDisco is { NoTorneio: true, DiasRestantes: 5 },
				  $"{noDisco?.NoTorneio} / {noDisco?.DiasRestantes}");
		return ec;
	}

	// =====================================================================
	// S. O SANGUE DO CELL (dono, 2026-10-19)
	// =====================================================================
	private static readonly string[] SangueDoCellDoDono = ["Saiyan", "Namekian", "Frost Demon"];
	private const string Regenerate = "/datum/skill/general/regenerate";

	/// <summary>O MESMO LIVRO: as mesmas skills, cada uma no mesmo nivel (o clone do Cell Jr).</summary>
	private static bool MesmoLivro(ServerPlayer a, ServerPlayer b) =>
		a.Livro != null && b.Livro != null && a.Livro.Aprendidas.Count == b.Livro.Aprendidas.Count
		&& b.Livro.Aprendidas.All(p => a.Livro.Sabe(p) && a.Niveis.Nivel(p) == b.Niveis.Nivel(p));

	/// <summary>As skills de raca deste corpo (os bits escolhidos do sangue), uma por linha do catalogo.</summary>
	private static List<SkillDeRaca> RaciaisDe(ServerPlayer c) =>
		[.. SkillsRaciais.Todas.Where(d => (c.Ficha.bio_skills & (uint)d.Bit) != 0)];

	private void TcelOSangueDoCell(ServerPlayer cell)
	{
		GD.Print("[torneiocell] -- S) o sangue do Cell: Saiyajin, Namekuseijin e Frost Demon (dono, 2026-10-19)");
		KitRacial kits = KitRacial.Nenhum;
		foreach (string r in SangueDoCellDoDono) kits |= Passivas.KitDe(r, "", "");
		List<SkillDeRaca> seis = RaciaisDe(cell);
		AfirmarTcel("**O CELL E UM BIO-ANDROIDE DO SANGUE SAIYAJIN, NAMEKUSEIJIN E FROST DEMON** (*\"o cell vai ser um bio androide com o dna das "
					+ "seguintes raças Saiyajins, Namekuseijins, frost demon\"*) -- e so delas: o humano saiu",
					cell.Race == "BioAndroid" && cell.Ficha.bio_racas_dna.SequenceEqual(SangueDoCellDoDono),
					$"{cell.Race}: {string.Join("+", cell.Ficha.bio_racas_dna)}");
		AfirmarTcel("**...COM AS SEIS SKILLS DE RACA DESSE SANGUE** (*\"bio androides escolhem ate 6 skills raciais das raças q eles pegaram o "
					+ "dna\"*): seis, e todas dos kits Saiyajin, Namekuseijin e Frost Demon",
					seis.Count == SkillsRaciais.LimiteDoBio && seis.All(d => (d.Kit & kits) != 0),
					string.Join(", ", seis.Select(d => $"{d.Nome} ({d.Kit})")));
		AfirmarTcel("**...E AS PASSIVAS DAS TRES RACAS**: o Zenkai do Saiyajin, o folego no vacuo do Frost Demon e a Regenerate",
					cell.Ficha.HasZenkai() && cell.Ficha.bio_dna_respira && cell.Livro!.Sabe(Regenerate),
					$"zenkai {cell.Ficha.HasZenkai()}; vacuo {cell.Ficha.bio_dna_respira}; regenerate {cell.Livro?.Sabe(Regenerate)}");
	}

	// =====================================================================
	// B. OS DIAS E A ARENA
	// =====================================================================
	private ServerPlayer? TcelOsDiasEAArena(EstadoDoChefe ec, Arena arena)
	{
		GD.Print("[torneiocell] -- B) os dias e a arena");
		EscutaDasSagas!.Clear();
		for (int dia = 0; dia < 4; dia++) VirouODia();
		AfirmarTcel("a cada virada de dia o mundo ouve quanto falta pro torneio (4, 3, 2, 1) -- e ele ainda nao chegou",
				  ec is { Fase: FaseDoChefe.ACaminho, DiasRestantes: 1 }
				  && EscutaDasSagas.Any(f => f.Contains("Faltam 4 dia", StringComparison.Ordinal))
				  && EscutaDasSagas.Any(f => f.Contains("Faltam 1 dia", StringComparison.Ordinal)),
				  $"{ec.Fase} {ec.DiasRestantes}; {string.Join(" | ", EscutaDasSagas)}");

		EscutaDoTorneioDoCell!.Clear();
		VirouODia();
		ServerPlayer? cell = _players.TryGetValue(ec.Corpo, out ServerPlayer? c) ? c : null;
		AfirmarTcel("**NO DIA, ELE NASCE NA ARENA DA TERRA** -- Perfeito, parado e intocavel -- e o torneio abre",
				  cell?.Papel != null && _torneioDoCell is { Fase: FaseDoTorneioDoCell.Esperando } && cell.Zone.Name == "Earth" && arena.ContemEm(cell.Pos)
				  && cell.Papel.Estagio == 2 && cell.Combate.Intocavel && CellForaDaVez(cell)
				  && EscutaDoTorneioDoCell.Any(f => f.Contains("COMEÇOU", StringComparison.Ordinal)),
				  $"corpo {cell != null}; fase {_torneioDoCell?.Fase}; {cell?.Zone.Name} ({cell?.Pos.X / 32:0},{cell?.Pos.Y / 32:0}); degrau "
				  + $"{cell?.Papel?.Estagio + 1}; intocavel {cell?.Combate.Intocavel}; {EscutaDoCell()}");
		if (cell == null || _torneioDoCell == null) return null;

		// A PORTA DO CEREBRO (`ForaDaVezNoTorneio`, a que o tique do NPC e o reflexo do feixe perguntam): entre as lutas ele nao caca ninguem
		CellCacaNaEsperaDeTeste = true;
		bool soltoComODefeito;
		try { soltoComODefeito = !ForaDaVezNoTorneio(cell); }
		finally { CellCacaNaEsperaDeTeste = false; }
		AfirmarTcel("...e a IA o deixa PARADO entre as lutas: a porta do cerebro (`ForaDaVezNoTorneio`) responde por ele -- ele espera o desafio, nao "
				  + "caca ninguem", ForaDaVezNoTorneio(cell));
		AfirmarTcel("   CONTRA-EXEMPLO (a IA solta, a da saga): ele cacaria quem estivesse perto -- a regua de cima reprovaria", soltoComODefeito);

		ComecarTorneio(TipoDeTorneio.Terra, "bancada do torneio do Cell");
		bool abriu = _torneio != null;
		if (_torneio is { } t) EncerrarTorneio(t);
		AfirmarTcel("...e o TORNEIO DE ARTES MARCIAIS NAO ABRE com o Cell na arena (a arena e uma so)", !abriu);
		return cell;
	}

	// =====================================================================
	// C. SO JOGADOR, UM POR VEZ
	// =====================================================================
	private bool TcelSoJogador(ServerPlayer cell, Arena arena, ServerPlayer goku, ServerPlayer kuririn)
	{
		GD.Print("[torneiocell] -- C) so jogador desafia, um por vez");
		TorneioDoCellVivo t = _torneioDoCell!;
		var terra = ZoneKey.Premade("Earth");

		// A ARENA SEM HABITANTE NENHUM antes de medir -- o povoamento pode ter soltado alguem ali, e o crivo do jogador e o que se mede
		foreach (ServerPlayer o in ZoneList(terra.Hash).ToList())
			if (o.Id != cell.Id && !EhJogador(o) && arena.ContemEm(o.Pos)) Transportar(o, terra, ForaDaArenaDeTeste(arena, 6));

		// UM HABITANTE NA ARENA
		MoldeDeNpc? comum = _moldes!.Todos.FirstOrDefault(m => m.Tipo == TipoDeNpc.Cidadao);
		ServerPlayer povo = ForjarComSkills("PovoDaArena", DentroDaArenaDeTeste(arena, -8), 5_000);
		if (comum != null) povo.Papel = new PapelDeNpc(comum, 77);
		povo.Conta = "";
		Transportar(povo, terra, DentroDaArenaDeTeste(arena, -8));
		NpcDesafiaOCellDeTeste = true;
		ServerPlayer? comODefeito;
		try { comODefeito = ProximoDesafiante(t); }
		finally { NpcDesafiaOCellDeTeste = false; }
		t.NaArenaDesde.Clear();
		ServerPlayer? semODefeito = ProximoDesafiante(t);
		AfirmarTcel("**SO JOGADOR DESAFIA** (*\"so entra player sem npc\"*): o habitante na arena nao e chamado",
				  comum != null && semODefeito == null && t.Fase == FaseDoTorneioDoCell.Esperando, $"{semODefeito?.Name ?? "ninguem"}");
		AfirmarTcel("   CONTRA-EXEMPLO (o NPC como jogador): o habitante seria o desafiante -- a regua de cima reprovaria", comODefeito == povo,
				  $"{comODefeito?.Name ?? "ninguem"}");
		RemoverNpc(povo);

		// GOKU PISA NA ARENA, E KURIRIN LOGO DEPOIS
		Transportar(goku, terra, DentroDaArenaDeTeste(arena, -4));
		TickDoTorneioDoCell(0.1);
		AfirmarTcel("**QUEM PISA NA ARENA DESAFIA**: Goku e chamado -- curados, frente a frente na linha do meio (ele a oeste), e os golpes NAO LETAIS "
				  + "pros dois (*\"nocautear apenas\"*)",
				  t is { Fase: FaseDoTorneioDoCell.Contagem } && t.Desafiante == goku.Id && !goku.Combate.Letal && !cell.Combate.Letal
				  && arena.ContemEm(goku.Pos) && arena.ContemEm(cell.Pos) && goku.Pos.X < cell.Pos.X,
				  $"fase {t.Fase}; desafiante {t.NomeDoDesafiante}; letal {goku.Combate.Letal}/{cell.Combate.Letal}");
		if (t.Desafiante != goku.Id) return false;

		Transportar(kuririn, terra, DentroDaArenaDeTeste(arena, 4));
		ArenaNaoEsvaziaDeTeste = true;
		bool ficouComODefeito;
		try { TickDoTorneioDoCell(0.1); ficouComODefeito = arena.ContemEm(kuririn.Pos); }
		finally { ArenaNaoEsvaziaDeTeste = false; }
		t.ProximaVarrida = 0;
		TickDoTorneioDoCell(0.1);
		AfirmarTcel("...e o x1 e x1: Kuririn, que pisou depois, e posto pra FORA da arena", !arena.ContemEm(kuririn.Pos) && t.Desafiante == goku.Id,
				  $"({kuririn.Pos.X / 32:0},{kuririn.Pos.Y / 32:0})");
		AfirmarTcel("   CONTRA-EXEMPLO (a arena aberta a todos): Kuririn ficaria dentro -- a regua de cima reprovaria", ficouComODefeito);

		for (int i = 0; i < 3; i++) TickDoTorneioDoCell(1.0);
		AfirmarTcel("a contagem acaba no COMECEM!: o Cell fica tocavel, e o alvo dele e o Goku -- so ele",
				  t.Fase == FaseDoTorneioDoCell.Luta && !cell.Combate.Intocavel && cell.Papel!.PresaDoRoteiro == goku.Id
				  && EscutaDoTorneioDoCell!.Contains("COMECEM!"), $"fase {t.Fase}; alvo {cell.Papel?.PresaDoRoteiro}; {EscutaDoCell()}");
		return t.Fase == FaseDoTorneioDoCell.Luta;
	}

	// =====================================================================
	// D. O X1 E X1
	// =====================================================================
	private void TcelOX1EX1(ServerPlayer cell, ServerPlayer goku, ServerPlayer vegeta)
	{
		GD.Print("[torneiocell] -- D) o x1 e x1: o golpe de quem se mete e desfeito");
		TorneioDoCellVivo t = _torneioDoCell!;
		BodyPart membro = cell.Combate.Corpo.Partes.OrderByDescending(p => p.VidaMax).First();
		TickDoTorneioDoCell(0.05);   // a foto da vida
		double inteira = membro.Vida;

		void Golpe(ServerPlayer quem)
		{
			membro.Vida -= membro.VidaMax * 0.1;
			cell.Combate.SincronizarVida();
			MarcarAgressao(cell, quem);
		}

		Golpe(vegeta);
		QuemSeMeteFicaDeTeste = true;
		try { TickDoTorneioDoCell(0.05); }
		finally { QuemSeMeteFicaDeTeste = false; }
		bool ficou = membro.Vida < inteira - 1;
		membro.Vida = inteira;
		cell.Combate.SincronizarVida();
		TickDoTorneioDoCell(0.05);   // a foto de novo, inteira

		Golpe(vegeta);
		TickDoTorneioDoCell(0.05);
		AfirmarTcel("**O X1 E X1** (*\"os playes enfrentam ele no x1\"*): o golpe de Vegeta, de fora, e DESFEITO -- e Vegeta fica fora do torneio",
				  Math.Abs(membro.Vida - inteira) < 1e-6 && t.Barrados.Contains(vegeta.Assinatura) && t.Fase == FaseDoTorneioDoCell.Luta,
				  $"vida {membro.Vida:0.##} de {inteira:0.##}; barrado {t.Barrados.Contains(vegeta.Assinatura)}");
		AfirmarTcel("   CONTRA-EXEMPLO (o golpe de fora fica): o Cell perderia a vida do golpe de Vegeta -- a regua de cima reprovaria", ficou);

		Golpe(goku);
		double depois = membro.Vida;
		TickDoTorneioDoCell(0.05);
		AfirmarTcel("...e o golpe do Goku, o desafiante, FICA", Math.Abs(membro.Vida - depois) < 1e-6 && depois < inteira - 1,
				  $"vida {membro.Vida:0.##} (depois do golpe {depois:0.##})");
		// (2026-10-20) O ALVO DO CELL NO X1 E O DESAFIANTE, MESMO COM O MONITOR DA SAGA (uma vez por segundo, `MonitorarChefe`): a absorcao, que
		// mexe no alvo imposto, nao toca o Cell do torneio. Primeiro com o defeito (ela mexe, o jogo de antes) e depois de verdade.
		EloDaSaga eloDoCell = Array.Find(_cadeia, e => e.Id == "cell")!;
		EstadoDoElo estDoCell = _sagas.Do("cell")!;
		EstadoDoChefe ecDoCell = estDoCell.Chefes.Find(c => c.Corpo == cell.Id)!;
		AbsorcaoMexeNoTorneioDeTeste = true;
		int alvoComODefeito;
		try { MonitorarChefe(eloDoCell, estDoCell, ecDoCell); alvoComODefeito = cell.Papel!.PresaDoRoteiro; }
		finally { AbsorcaoMexeNoTorneioDeTeste = false; }
		cell.Papel!.PresaDoRoteiro = goku.Id;   // (o que o defeito apagou volta: o x1 segue)
		MonitorarChefe(eloDoCell, estDoCell, ecDoCell);
		AfirmarTcel("**NO X1 O ALVO DO CELL E O DESAFIANTE, E O MONITOR DA SAGA NAO O APAGA** -- ele roda uma vez por segundo e apagava: o Cell ia "
				  + "atras de quem estivesse mais perto, ate de quem assiste", cell.Papel.PresaDoRoteiro == goku.Id,
				  $"alvo {cell.Papel.PresaDoRoteiro} (o desafiante {goku.Id})");
		AfirmarTcel("   CONTRA-EXEMPLO (a absorcao mexendo no Cell do torneio, o jogo de antes): o alvo sumiria -- a regua de cima reprovaria",
				  alvoComODefeito == 0, $"alvo {alvoComODefeito}");
		CurarPraLuta(cell);
		TickDoTorneioDoCell(0.05);
	}

	// =====================================================================
	// R. NO X1 ELE NAO TROCA DE FORMA POR DANO
	// =====================================================================
	private void TcelSemFormaPorDano(ServerPlayer cell)
	{
		GD.Print("[torneiocell] -- R) no x1 ele nao troca de forma por dano: a Super Perfeita e a da morte");
		double gatilho = cell.Papel!.GatilhoAtual;
		PorPiorMembroEm(cell, gatilho - 0.01);
		CellTransformaNoTorneioDeTeste = true;
		int degrauComODefeito;
		try { TickDoRoteiro(); degrauComODefeito = cell.Papel.Estagio; }
		finally { CellTransformaNoTorneioDeTeste = false; }
		if (cell.Papel.Estagio != 2)
		{
			cell.Papel.Estagio = 2;
			EntrarNoDegrau(cell, cura: 0, anunciar: false);
			cell.CenaSegundos = 0;
		}
		PorPiorMembroEm(cell, gatilho - 0.01);
		TickDoRoteiro();
		int degrau = cell.Papel.Estagio;
		if (degrau != 2)
		{
			// (desfeito aqui tambem: o resto da bancada precisa dele Perfeito, e esta afirmacao ja ficou vermelha)
			cell.Papel.Estagio = 2;
			EntrarNoDegrau(cell, cura: 0, anunciar: false);
			cell.CenaSegundos = 0;
		}
		AfirmarTcel("**NO X1 ELE NAO TROCA DE FORMA POR DANO**: o membro abaixo do gatilho da Perfeita e ele segue Perfeito -- a Super Perfeita e a da "
				  + "morte (*\"se ele for derrotado por nocaute ... volta super perfeito\"*)", gatilho > 0 && degrau == 2,
				  $"gatilho {gatilho}; degrau {degrau + 1}");
		AfirmarTcel("   CONTRA-EXEMPLO (o roteiro da saga): ele viraria Super Perfeito no meio da luta -- a regua de cima reprovaria", degrauComODefeito == 3,
				  $"degrau {degrauComODefeito + 1}");
		CurarPraLuta(cell);
		TickDoTorneioDoCell(0.05);
	}

	// =====================================================================
	// E. PISOU FORA, PERDEU
	// =====================================================================
	private void TcelPisouForaPerdeu(ServerPlayer cell, Arena arena, ServerPlayer goku, ServerPlayer vegeta, bool letalDoGoku)
	{
		GD.Print("[torneiocell] -- E) pisou fora, perdeu -- e a chance nao volta");
		TorneioDoCellVivo t = _torneioDoCell!;
		var terra = ZoneKey.Premade("Earth");
		if (goku.Voando) AlternarVoo(goku);
		Transportar(goku, terra, ForaDaArenaDeTeste(arena, 0));
		TickDoTorneioDoCell(0.05);
		AfirmarTcel("**PISOU FORA, PERDEU** (*\"se sair da arena vc perde\"*): Goku fora da arena, a pe, e eliminado por RING-OUT -- volta a ser letal, e o "
				  + "Cell volta pro meio, curado e intocavel",
				  t.Fase == FaseDoTorneioDoCell.Intervalo && t.JaLutaram.Contains(goku.Assinatura) && goku.Combate.Letal == letalDoGoku
				  && EscutaDoTorneioDoCell!.Any(f => f.Contains("RING-OUT", StringComparison.Ordinal)) && cell.Combate.Intocavel && arena.ContemEm(cell.Pos),
				  $"fase {t.Fase}; ja lutou {t.JaLutaram.Contains(goku.Assinatura)}; {EscutaDoCell()}");

		for (int i = 0; i < 9; i++) TickDoTorneioDoCell(1.0);   // o intervalo
		Transportar(goku, terra, DentroDaArenaDeTeste(arena, -4));
		TickDoTorneioDoCell(0.1);
		AfirmarTcel("...e ele teve a chance dele: pisando de novo na arena e posto pra fora (*\"cada jogador tem uma chance\"*)",
				  !arena.ContemEm(goku.Pos) && t.Fase == FaseDoTorneioDoCell.Esperando, $"fase {t.Fase}; ({goku.Pos.X / 32:0},{goku.Pos.Y / 32:0})");
		Transportar(vegeta, terra, DentroDaArenaDeTeste(arena, -2));
		TickDoTorneioDoCell(0.1);
		AfirmarTcel("...nem Vegeta, que se meteu na luta de outro, desafia", !arena.ContemEm(vegeta.Pos) && t.Fase == FaseDoTorneioDoCell.Esperando,
				  $"fase {t.Fase}");
	}

	// =====================================================================
	// F. O CELL PISA FORA E A REGRA CAI
	// =====================================================================
	private bool TcelARegraCai(ServerPlayer cell, Arena arena, ServerPlayer kuririn)
	{
		GD.Print("[torneiocell] -- F) o Cell pisa fora da arena e a regra cai");
		TorneioDoCellVivo t = _torneioDoCell!;
		var terra = ZoneKey.Premade("Earth");
		Transportar(kuririn, terra, DentroDaArenaDeTeste(arena, 4));
		TickDoTorneioDoCell(0.1);
		for (int i = 0; i < 3; i++) TickDoTorneioDoCell(1.0);
		AfirmarTcel("(montagem) Kuririn e o desafiante, e a luta comecou", t.Fase == FaseDoTorneioDoCell.Luta && t.Desafiante == kuririn.Id,
				  $"fase {t.Fase}; {t.NomeDoDesafiante}");
		if (t.Desafiante != kuririn.Id || t.Fase != FaseDoTorneioDoCell.Luta) return false;

		if (cell.Voando) AlternarVoo(cell);
		Transportar(cell, terra, ForaDaArenaDeTeste(arena, 5));
		RegraNaoCaiDeTeste = true;
		try { TickDoTorneioDoCell(0.05); }
		finally { RegraNaoCaiDeTeste = false; }
		bool regraComODefeito = t.RegraDaArena;
		// (2026-10-19) O CONTRA-EXEMPLO DOS CELL JR, antes: a regra cai e nenhum nasce. Depois a regra volta de pe pra o mesmo instante se repetir.
		SemCellJrDeTeste = true;
		try { TickDoTorneioDoCell(0.05); }
		finally { SemCellJrDeTeste = false; }
		int jrsComODefeito = _cellJrs.Count;
		t.RegraDaArena = true;
		TickDoTorneioDoCell(0.05);
		AfirmarTcel("**O CELL PISA FORA E A REGRA CAI** (*\"ai ele fala q essa regra é inutil e tira ela, e a luta continua\"*): ele declara a regra "
				  + "inutil, e a luta SEGUE",
				  !t.RegraDaArena && t.Fase == FaseDoTorneioDoCell.Luta && t.Desafiante == kuririn.Id
				  && EscutaDoTorneioDoCell!.Contains(TorneioDoCell.FalaDaRegra), $"regra {t.RegraDaArena}; fase {t.Fase}; {EscutaDoCell()}");
		AfirmarTcel("   CONTRA-EXEMPLO (a regra de pe): o Cell fora nao mudaria nada -- a regua de cima reprovaria", regraComODefeito);
		TcelOsCellJr(cell, kuririn, jrsComODefeito);

		if (kuririn.Voando) AlternarVoo(kuririn);
		Transportar(kuririn, terra, ForaDaArenaDeTeste(arena, 1));
		TickDoTorneioDoCell(0.05);
		AfirmarTcel("...e daqui em diante ninguem perde por sair: Kuririn fora da arena, a pe, segue lutando",
				  t.Fase == FaseDoTorneioDoCell.Luta && t.Desafiante == kuririn.Id && !arena.ContemEm(kuririn.Pos), $"fase {t.Fase}");
		Transportar(cell, terra, DentroDaArenaDeTeste(arena, 2));
		Transportar(kuririn, terra, DentroDaArenaDeTeste(arena, -2));
		TickDoTorneioDoCell(0.05);
		return true;
	}

	// =====================================================================
	// F2. OS CELL JR (dono, 2026-10-19)
	// =====================================================================
	private void TcelOsCellJr(ServerPlayer cell, ServerPlayer kuririn, int jrsComODefeito)
	{
		GD.Print("[torneiocell] -- F2) os sete Cell Jr: clones do Cell, 90% do BP, na plateia");
		List<ServerPlayer> jrs = [.. _cellJrs.Where(_players.ContainsKey).Select(id => _players[id])];
		AfirmarTcel("**O CELL CAI DA ARENA E SOLTA OS CELL JR** (*\"ele vai spawnar cell jr ... ao todo 7\"*): sete, com a folha do Cell Jr, e ele avisa",
					jrs.Count == TorneioDoCell.QuantosCellJr && jrs.All(j => j.Race == "BioAndroid" && j.Visual.Corpo == TorneioDoCell.CorpoDoCellJr)
					&& EscutaDoTorneioDoCell!.Contains(TorneioDoCell.FalaDosCellJr),
					$"{jrs.Count} Cell Jr; corpos {string.Join(",", jrs.Select(j => j.Visual.Corpo))}");
		AfirmarTcel("   CONTRA-EXEMPLO (o Cell que so derruba a regra): nenhum Cell Jr -- a regua de cima reprovaria", jrsComODefeito == 0, $"{jrsComODefeito}");
		AfirmarTcel("**CADA UM E UM CLONE DO CELL COM 90% DO BP DELE** (*\"os cell jr vao ser literalmente um clone do cell original mas com 90% do "
					+ "bp dele\"*): o mesmo sangue, as mesmas seis skills de raca, o MESMO LIVRO (cada skill do Cell, no nivel dele, a Regenerate junto) -- e "
					+ "90% do BP do Cell Perfeito",
					jrs.Count > 0 && jrs.All(j => Math.Abs(j.Ficha.BP - cell.Ficha.BP * TorneioDoCell.FracaoDoBpDoCellJr) < 1
										  && j.Ficha.bio_racas_dna.SequenceEqual(cell.Ficha.bio_racas_dna) && j.Ficha.bio_skills == cell.Ficha.bio_skills
										  && j.Livro!.Sabe(Regenerate) && MesmoLivro(j, cell)),
					$"BP {jrs.FirstOrDefault()?.Ficha.BP:N0} (o Cell {cell.Ficha.BP:N0}); sangue {string.Join("+", jrs.FirstOrDefault()?.Ficha.bio_racas_dna ?? [])}; "
					+ $"livro {jrs.FirstOrDefault()?.Livro?.Aprendidas.Count} skills (o Cell {cell.Livro?.Aprendidas.Count})");
		// O CONTRA-EXEMPLO DO LIVRO: um Cell Jr pela mesma porta de producao, sem a copia (o sorteio do molde, pelo BP de nascimento) -- e tirado
		CellJrSemOLivroDoCellDeTeste = true;
		ServerPlayer? doMolde;
		try { doMolde = NascerUmCellJr(_torneioDoCell!, cell, cell.Pos + new Vec2(0, 4 * ZoneCollision.TileSize)); }
		finally { CellJrSemOLivroDoCellDeTeste = false; }
		bool outroLivro = doMolde != null && !MesmoLivro(doMolde, cell);
		int skillsDoMolde = doMolde?.Livro?.Aprendidas.Count ?? 0;
		if (doMolde != null) RemoverNpc(doMolde);
		AfirmarTcel("   CONTRA-EXEMPLO (o Cell Jr so do molde, sem o livro do Cell): o sorteio dele nao da o livro do Cell -- a regua de cima reprovaria",
					outroLivro, $"{skillsDoMolde} skills (o Cell {cell.Livro?.Aprendidas.Count})");
		var plateia = new HashSet<int>(Plateia(_torneioDoCell!).Select(p => p.Id));
		AfirmarTcel("**ELES MIRAM A PLATEIA** (*\"vao mirar nos participantes q nao estao lutando no torneio\"*): todo Cell Jr tem um alvo de fora da "
					+ "luta -- e nenhum o desafiante",
					jrs.Count == TorneioDoCell.QuantosCellJr && plateia.Count > 0 && jrs.All(j => plateia.Contains(j.Papel!.PresaDoRoteiro))
					&& jrs.All(j => j.Papel!.PresaDoRoteiro != kuririn.Id),
					$"plateia {string.Join(",", plateia)}; alvos {string.Join(",", jrs.Select(j => j.Papel!.PresaDoRoteiro))}; desafiante {kuririn.Id}");
		if (jrs.Count == 0) return;   // (sem Cell Jr nao ha o que medir abaixo -- as afirmacoes de cima ja ficaram vermelhas)

		// O GOLPE DA PLATEIA NUM CELL JR FICA; O DE UM CELL JR NO DESAFIANTE E DESFEITO
		ServerPlayer jr = jrs[0];
		// (quem bate e o alvo dele -- ou, sem alvo, alguem da plateia: a mira e a regua de cima, e esta mede o golpe; sem plateia nao ha quem bata)
		int quemId = _players.ContainsKey(jr.Papel!.PresaDoRoteiro) ? jr.Papel.PresaDoRoteiro : plateia.FirstOrDefault();
		if (!_players.TryGetValue(quemId, out ServerPlayer? quemBate)) return;
		BodyPart doJr = jr.Combate.Corpo.Partes.OrderByDescending(p => p.VidaMax).First();
		doJr.Vida -= doJr.VidaMax * 0.1;
		jr.Combate.SincronizarVida();
		MarcarAgressao(jr, quemBate);
		double jrFerido = doJr.Vida;
		BodyPart doKuririn = kuririn.Combate.Corpo.Partes.OrderByDescending(p => p.VidaMax).First();
		double kuririnInteiro = doKuririn.Vida;
		doKuririn.Vida -= doKuririn.VidaMax * 0.1;
		kuririn.Combate.SincronizarVida();
		MarcarAgressao(kuririn, jr);
		TickDoTorneioDoCell(0.05);
		AfirmarTcel("**A PLATEIA LUTA CONTRA OS CELL JR, E NAO CONTRA O CELL** (*\"vao poder lutar mas nao contra o cell, somente contra os cell jr\"*): "
					+ "o golpe nele fica -- e o de um Cell Jr no desafiante e desfeito (o x1 continua)",
					Math.Abs(doJr.Vida - jrFerido) < 1e-6 && jrFerido < doJr.VidaMax - 1 && Math.Abs(doKuririn.Vida - kuririnInteiro) < 1e-6,
					$"Cell Jr {doJr.Vida:0.##} de {doJr.VidaMax:0.##}; Kuririn {doKuririn.Vida:0.##} (era {kuririnInteiro:0.##})");
	}

	// =====================================================================
	// G. NOCAUTEADO NO X1, ELE MORRE NA LUTA -- E VOLTA SUPER PERFEITO
	// =====================================================================
	private void TcelMortoNaLuta(ServerPlayer cell, Arena arena, EstadoDoChefe ec, ServerPlayer kuririn, bool letalDoKuririn)
	{
		GD.Print("[torneiocell] -- G) nocauteado no x1, o Cell morre na luta e volta Super Perfeito");
		TorneioDoCellVivo t = _torneioDoCell!;
		int idDoPerfeito = cell.Id;
		cell.UltimoAgressor = kuririn.Id;
		cell.Combate.Nocautear(60);
		TickDoTorneioDoCell(0.05);
		AfirmarTcel("**NOCAUTEADO NO X1, O CELL MORRE NA LUTA** (*\"se ele for derrotado por nocaute o jogo vai falar q ele foi morto na luta\"*): o mundo "
				  + "ouve que Kuririn o matou, e Kuririn volta a ser letal",
				  cell.Ficha.dead && t.Fase == FaseDoTorneioDoCell.Acabou && kuririn.Combate.Letal == letalDoKuririn
				  && EscutaDoTorneioDoCell!.Any(f => f.Contains("MORTO na luta", StringComparison.Ordinal)),
				  $"morto {cell.Ficha.dead}; fase {t.Fase}; {EscutaDoCell()}");

		TickDasSagas();
		AfirmarTcel("...e a saga o poe voltando do alem, no degrau Super Perfeito -- a arena continua dele ate ele voltar",
				  ec is { Fase: FaseDoChefe.Derrotado, Degrau: 3 } && ec.RessurgeEm > 0 && ArenaComOCell, $"{ec.Fase}; degrau {ec.Degrau + 1}");

		_relogioDoMundo += 61;
		EscutaDoTorneioDoCell!.Clear();
		List<string> falas = Ouvir(() => TickDasSagas());
		ServerPlayer? volta = _players.TryGetValue(ec.Corpo, out ServerPlayer? v) ? v : null;
		AfirmarTcel("**E VOLTA SUPER PERFEITO -- E A LUTA VIRA LIVRE** (*\"volta super perfeito e apartir dai a luta vira livre, todos vs "
				  + "cell\"*): 5 bilhoes, o torneio acabou, e ele esta solto, tocavel e letal",
				  volta?.Papel != null && volta.Id != idDoPerfeito && volta.Papel.Estagio == 3 && Math.Abs(volta.Ficha.BP - 5_000_000_000) < 1
				  && _torneioDoCell == null && !ec.NoTorneio && !CellForaDaVez(volta) && !volta.Combate.Intocavel
				  && volta.Combate.Letal && EscutaDoTorneioDoCell.Any(f => f.Contains("SUPER PERFEITO", StringComparison.Ordinal)),
				  $"corpo {volta != null}; degrau {volta?.Papel?.Estagio + 1}; BP {volta?.Ficha.BP:N0}; torneio {_torneioDoCell != null}; {EscutaDoCell()}");
		// (2026-10-28) E A VOLTA E PELA TRANSMISSAO, ao lado de quem o matou na luta -- antes era no meio da arena
		double tiles = volta == null ? -1 : Vec2.Distance(volta.Pos, kuririn.Pos) / ZoneCollision.TileSize;
		AfirmarTcel("(2026-10-28) **...AO LADO DE QUEM O MATOU, PELA TRANSMISSAO** (pedido 147, *\"spawnar com o mesmo efeito e mensagem no chat do "
				  + "instant transmission proximo ao player q matou ele na forma perfeita\"*): o tile ao lado de Kuririn, e Kuririn ouve a frase",
				  volta != null && volta.Zone.Hash == kuririn.Zone.Hash && tiles <= 1.05 && Disse(falas, $"{volta.Name} aparece num instante ao seu lado"),
				  $"a {tiles:0.##} tiles de Kuririn, ele em ({volta?.Pos.X / 32:0},{volta?.Pos.Y / 32:0}), o meio da arena em "
				  + $"({arena.Centro.X / 32:0},{arena.Centro.Y / 32:0}); {string.Join(" | ", falas)}");
		List<ServerPlayer> soltos = [.. _cellJrs.Where(_players.ContainsKey).Select(id => _players[id])];
		AfirmarTcel("...e os Cell Jr que sobraram ficam SOLTOS na luta livre: sem alvo imposto, a IA hostil deles escolhe quem estiver perto",
					soltos.Count > 0 && soltos.All(j => j.Papel!.PresaDoRoteiro == 0 && !CellForaDaVez(j)), $"{soltos.Count} soltos");
	}

	// =====================================================================
	// H. A FILA VAZIA POR 5 MIN: O KAMEHAMEHA
	// =====================================================================
	private void TcelAFilaVazia(Arena arena, ServerPlayer[] todos)
	{
		GD.Print("[torneiocell] -- H) a fila vazia por 5 min: o Kamehameha na Terra -- o raio da tecnica, 2x, ate o fim");
		var terra = ZoneKey.Premade("Earth");
		SagasLimpas();
		int jrsAntes = _cellJrs.Count;
		TickDoTorneioDoCell(1.0);
		AfirmarTcel("(2026-10-19) acabada a saga deles, os Cell Jr que sobraram VAO EMBORA com ela", jrsAntes > 0 && _cellJrs.Count == 0,
					$"antes {jrsAntes}, agora {_cellJrs.Count}");
		(ServerPlayer Cell, TorneioDoCellVivo T, EstadoDoElo Est)? montado = TcelTorneioDeNovo(arena, todos);
		AfirmarTcel("(montagem) o torneio abriu de novo, com o Cell na arena e ninguem nela",
				  montado is { } m0 && m0.T.Fase == FaseDoTorneioDoCell.Esperando, $"fase {_torneioDoCell?.Fase}");
		if (montado is not { } m) return;
		(ServerPlayer cell, TorneioDoCellVivo t, EstadoDoElo est) = m;

		EscutaDoTorneioDoCell!.Clear();
		for (int i = 0; i < 239; i++) TickDoTorneioDoCell(1.0);
		bool aindaNao = t.Fase == FaseDoTorneioDoCell.Esperando && !_canais.ContainsKey(cell.Id)
			&& !EscutaDoTorneioDoCell.Contains(TorneioDoCell.FalaDoUltimoMinuto);
		for (int i = 0; i < 61; i++) TickDoTorneioDoCell(1.0);
		bool avisou = EscutaDoTorneioDoCell.Contains(TorneioDoCell.FalaDoUltimoMinuto);
		CanalDeKi? canal = _canais.GetValueOrDefault(cell.Id);
		AfirmarTcel("**A FILA VAZIA POR 5 MIN E ELE VENCEU TODOS** (*\"se ele vencer todos os jogadores ele explode a terra com um kamehameha\"*): aos "
				  + "4 min nada ainda (e o aviso do ultimo minuto), aos 5 ele CARREGA o KAMEHAMEHA -- o canal da tecnica, na mao, intocavel",
				  aindaNao && avisou && t.Fase == FaseDoTorneioDoCell.Kamehameha && canal is { Verbo: "Kamehameha", Raio: null } && cell.Combate.Intocavel,
				  $"aos 4 min {aindaNao}; aviso {avisou}; fase {t.Fase}; canal {canal?.Verbo}; {EscutaDoCell()}");
		if (canal == null) return;

		// O CONTRA-EXEMPLO DO TAMANHO, no mesmo instante: o canal fechado e aberto de novo com o defeito -- e de novo sem ele, que e o que segue
		double onda = canal.Receita.MultDeOnda, escalaNoCanal = canal.Receita.EscalaVisual;
		FecharCanal(cell.Id, canal, null);
		KamehamehaDoTamanhoNormalDeTeste = true;
		double escalaComODefeito;
		try { CarregarOKamehamehaDoCell(cell); escalaComODefeito = _canais.GetValueOrDefault(cell.Id)?.Receita.EscalaVisual ?? 0; }
		finally { KamehamehaDoTamanhoNormalDeTeste = false; }
		if (_canais.TryGetValue(cell.Id, out CanalDeKi? doDefeito)) FecharCanal(cell.Id, doDefeito, null);
		CarregarOKamehamehaDoCell(cell);

		// O MAIS PERTO FICA A OESTE (Goku, dez tiles), e o Cell comeca olhando pro SUL: a mira e o olhar do raio tem de vir do Kamehameha, que
		// mira o jogador mais perto a cada tique da carga -- e nao do lado pra onde ele por acaso ja olhava
		Transportar(todos[0], terra, cell.Pos + new Vec2(-10 * ZoneCollision.TileSize, 0));
		cell.Facing = Facing.South;

		// A MARCA SE LE NA CARGA: alguns tiques do jogo com o canal ainda carregando. No nascimento do raio a mira de sempre (`Marcado`) a apaga
		// -- o marcado esta intocavel, e o raio sai pelo olhar --, e a rodada f1 reprovou esta regua lendo a marca depois.
		for (int i = 0; i < 5; i++) TcelUmTiqueDoJogo();
		int alvoNaCarga = cell.AlvoId;

		// O LACO DO JOGO ate o raio sair da mao
		Projetil? raio = null;
		for (int i = 0; i < 30 * 10 && raio == null; i++) { TcelUmTiqueDoJogo(); raio = _canais.GetValueOrDefault(cell.Id)?.Raio; }
		ServerPlayer maisPerto = todos.OrderBy(p => (p.Pos - cell.Pos).LengthSquared).First();
		Vec2 praEle = (maisPerto.Pos - cell.Pos).Normalized();
		ArteDeKi arteDoCell = ArteDeProjetil.De("Kamehameha", cell.Race, cell.Class, SementeDeArte(cell));
		AfirmarTcel("**O KAMEHAMEHA E O RAIO DA TECNICA, 2x O TAMANHO NORMAL** (*\"usar o proprio efeito de beam do kamehameha q ja tem no jogo, e "
					+ "aumentar ele pra 2x o tamanho normal\"*): um raio, com a arte do Kamehameha DELE e a escala 2x a do raio comum, no jogador mais perto",
					raio is { Tipo: TipoDeProjetil.Beam } && raio.Arte == arteDoCell && Math.Abs(raio.EscalaVisual - 2 * onda) < 1e-6
					&& Math.Abs(escalaNoCanal - 2 * onda) < 1e-6 && alvoNaCarga == maisPerto.Id && raio.Rumo.X * praEle.X + raio.Rumo.Y * praEle.Y > 0.9f,
					$"tipo {raio?.Tipo}; arte {raio?.Arte} (a dele {arteDoCell}); escala {raio?.EscalaVisual:0.##} (o comum {onda:0.##}); alvo na carga {alvoNaCarga} "
					+ $"(o mais perto {maisPerto.Id})");
		AfirmarTcel("   CONTRA-EXEMPLO (o raio comum): ele sairia com a escala do Kamehameha de qualquer um -- a regua de cima reprovaria",
					Math.Abs(escalaComODefeito - onda) < 1e-6, $"{escalaComODefeito:0.##}");

		// ...ATE O FIM: ninguem disputa (os quatro estao intocaveis: ele atravessa)
		for (int i = 0; i < 30 * 15 && _torneioDoCell != null; i++) TcelUmTiqueDoJogo();
		AfirmarTcel("...e o Kamehameha chega ao fim: a TERRA comeca a explodir (o gancho de destruicao das sagas), a saga fecha CONSUMADA e o Cell sai "
					+ "do mundo",
				  MorteDaZona(terra)?.Fase == FaseDaMorte.Explodindo && est.Fase == FaseDaSaga.Consumada && est.Condenado
				  && !_players.ContainsKey(cell.Id) && _torneioDoCell == null,
				  $"terra {MorteDaZona(terra)?.Fase}; saga {est.Fase}; condenada {est.Condenado}; no mundo {_players.ContainsKey(cell.Id)}; raio {raio?.Fim}");
		AbortarMorte(terra, "bancada do torneio do Cell");
		AfirmarTcel("(a bancada devolve a Terra: a destruicao e abortada antes do commit)", MorteDaZona(terra) is not { Fase: FaseDaMorte.Explodindo },
				  $"{MorteDaZona(terra)?.Fase}");
	}

	// =====================================================================
	// K. O EMBATE CONTRA O KAMEHAMEHA (dono, 2026-10-19)
	// =====================================================================
	/// <summary>Quantos tiles ao sul do Cell o Gohan fica, de frente pra ele: dentro da arena, e mais perto dele que os outros tres.</summary>
	private const int TilesDoEmbate = 12;

	private void TcelOEmbate(Arena arena, ServerPlayer[] todos)
	{
		GD.Print("[torneiocell] -- K) o embate de ki contra o Kamehameha: vencido, o Cell morre na hora -- e de vez");
		var terra = ZoneKey.Premade("Earth");
		ServerPlayer gohan = todos[3];

		// ---- O EMPATE, primeiro com o defeito (a Terra explodiria) e depois de verdade (ele carrega outro)
		var empate1 = TcelGohanContraOKamehameha(arena, todos, gohan, vezesOBpDoCell: 1);
		AfirmarTcel("(montagem) Gohan de frente pro Cell, os dois Kamehamehas saem juntos -- e a disputa de ki abre, FEIXE CONTRA FEIXE",
					empate1 is { } e0 && e0.D.A.Feixe != null && e0.D.B.Feixe != null, $"{empate1 is not null}");
		if (empate1 is not { } e1) return;
		DisputaDeKi d1 = e1.D;
		EmpateExplodeATerraDeTeste = true;
		try { Empatar(d1); TickDoTorneioDoCell(Protocol.TickSeconds); }
		finally { EmpateExplodeATerraDeTeste = false; }
		bool explodiuComODefeito = MorteDaZona(terra) is { Fase: FaseDaMorte.Explodindo };
		if (explodiuComODefeito) AbortarMorte(terra, "bancada do torneio do Cell");

		if (TcelGohanContraOKamehameha(arena, todos, gohan, vezesOBpDoCell: 1) is not { } e2) return;
		(ServerPlayer cellDoEmpate, TorneioDoCellVivo tDoEmpate, _, DisputaDeKi d2) = e2;
		EscutaDoTorneioDoCell!.Clear();
		Empatar(d2);
		TickDoTorneioDoCell(Protocol.TickSeconds);
		AfirmarTcel("**EMPATOU: OS DOIS ESTOURAM, E ELE CARREGA OUTRO** -- a Terra fica, e o Kamehameha volta pra mao dele",
					MorteDaZona(terra) == null && tDoEmpate.Fase == FaseDoTorneioDoCell.Kamehameha && _torneioDoCell == tDoEmpate
					&& _canais.GetValueOrDefault(cellDoEmpate.Id) is { Verbo: "Kamehameha", Raio: null }
					&& EscutaDoTorneioDoCell.Contains(TorneioDoCell.KamehamehaEmpatou(cellDoEmpate.Name)),
					$"terra {MorteDaZona(terra)?.Fase}; fase {tDoEmpate.Fase}; canal {_canais.GetValueOrDefault(cellDoEmpate.Id)?.Verbo}; {EscutaDoCell()}");
		AfirmarTcel("   CONTRA-EXEMPLO (o empate que explode): a Terra comecaria a explodir -- a regua de cima reprovaria", explodiuComODefeito);

		// ---- GOHAN VENCE, primeiro com o defeito (o Cell seguiria vivo) e depois de verdade
		bool vivoComODefeito = false;
		if (TcelGohanContraOKamehameha(arena, todos, gohan, vezesOBpDoCell: 100) is { } e3)
		{
			ServerPlayer cellDoDefeito = e3.Cell;
			CellSobreviveAoEmbateDeTeste = true;
			try { vivoComODefeito = TcelGohanApertaAteOFim(gohan, cellDoDefeito) && !cellDoDefeito.Ficha.dead; }
			finally { CellSobreviveAoEmbateDeTeste = false; }
			for (int i = 0; i < 30 * 15 && _torneioDoCell != null; i++) TcelUmTiqueDoJogo();
			if (MorteDaZona(terra) is { Fase: FaseDaMorte.Explodindo }) AbortarMorte(terra, "bancada do torneio do Cell");
		}

		if (TcelGohanContraOKamehameha(arena, todos, gohan, vezesOBpDoCell: 100) is not { } e4) return;
		(ServerPlayer cell, _, EstadoDoElo est, _) = e4;
		EscutaDoTorneioDoCell.Clear();
		EscutaDasSagas!.Clear();
		bool venceu = TcelGohanApertaAteOFim(gohan, cell);
		AfirmarTcel("**GOHAN VENCE A DISPUTA E O CELL MORRE NA HORA** (*\"se outro jogador jogar um beam contra esse kamehameha pode ter colisao de ki, e "
					+ "se o jogador vencer essa colisao o cell morre na hora\"*): o Cell morto, o mundo ouve que ele foi desintegrado, o torneio acabou -- e a "
					+ "Terra fica",
					venceu && cell.Ficha.dead && EscutaDoTorneioDoCell.Contains(TorneioDoCell.CellDesintegrado(cell.Name, gohan.Name))
					&& _torneioDoCell == null && MorteDaZona(terra) == null,
					$"venceu {venceu}; morto {cell.Ficha.dead}; torneio {_torneioDoCell != null}; terra {MorteDaZona(terra)?.Fase}; {EscutaDoCell()}");
		AfirmarTcel("   CONTRA-EXEMPLO (o embate de sempre): o Cell seguiria vivo -- a regua de cima reprovaria", vivoComODefeito);

		// ---- ...E DE VEZ: o monitor da saga, primeiro com o defeito (a volta da morte na luta) e, desfeito o que ele fez, de verdade
		if (!cell.Ficha.dead)
		{
			AfirmarTcel("**...E MORRE DE VEZ** (*\"mesma coisa q no anime com gohan\"*): a saga NAO o traz Super Perfeito -- ela fecha VENCIDA, e Gohan e o heroi",
						false, "o Cell nem morreu");
			TcelOCellVence(arena, todos, gohan);
			return;
		}
		EstadoDoChefe ec = est.Chefes.Find(x => x.Molde == "cell")!;
		(FaseDoChefe fase, int degrau, int corpo, double ressurge) = (ec.Fase, ec.Degrau, ec.Corpo, ec.RessurgeEm);
		CellVoltaDoEmbateDeTeste = true;
		bool voltariaComODefeito;
		try { TickDasSagas(); voltariaComODefeito = ec.RessurgeEm > 0; }
		finally { CellVoltaDoEmbateDeTeste = false; }
		(ec.Fase, ec.Degrau, ec.Corpo, ec.RessurgeEm) = (fase, degrau, corpo, ressurge);
		est.Fase = FaseDaSaga.EmCurso;
		EscutaDasSagas.Clear();
		TickDasSagas();
		AfirmarTcel("**...E MORRE DE VEZ** (*\"mesma coisa q no anime com gohan\"*): a saga NAO o traz Super Perfeito -- ela fecha VENCIDA, e Gohan e o heroi",
					ec.Fase == FaseDoChefe.Derrotado && ec.RessurgeEm == 0 && est.Fase == FaseDaSaga.Vencida
					&& EscutaDasSagas.Any(f => f.Contains(gohan.Name, StringComparison.Ordinal) && f.Contains("HERÓI", StringComparison.Ordinal)),
					$"{ec.Fase}; volta em {ec.RessurgeEm:0}; saga {est.Fase}; {string.Join(" | ", EscutaDasSagas)}");
		AfirmarTcel("   CONTRA-EXEMPLO (a morte da luta): ele voltaria Super Perfeito em um minuto -- a regua de cima reprovaria", voltariaComODefeito);
		TcelOCellVence(arena, todos, gohan);
	}

	/// <summary>O CELL VENCE A DISPUTA: o Kamehameha segue, e a Terra explode.</summary>
	private void TcelOCellVence(Arena arena, ServerPlayer[] todos, ServerPlayer gohan)
	{
		var terra = ZoneKey.Premade("Earth");
		if (TcelGohanContraOKamehameha(arena, todos, gohan, vezesOBpDoCell: 0.001) is not { } e5) return;
		(ServerPlayer cellQueVence, _, EstadoDoElo estDele, _) = e5;
		for (int i = 0; i < 30 * 40 && _emEmbateDeKi.ContainsKey(gohan.Id); i++) TcelUmTiqueDoJogo();
		bool gohanPerdeu = !_canais.ContainsKey(gohan.Id) && !cellQueVence.Ficha.dead;
		for (int i = 0; i < 30 * 20 && _torneioDoCell != null; i++) TcelUmTiqueDoJogo();
		AfirmarTcel("**O CELL VENCE A DISPUTA: O KAMEHAMEHA SEGUE, E A TERRA EXPLODE**",
					gohanPerdeu && MorteDaZona(terra)?.Fase == FaseDaMorte.Explodindo && estDele.Fase == FaseDaSaga.Consumada,
					$"Gohan perdeu {gohanPerdeu}; terra {MorteDaZona(terra)?.Fase}; saga {estDele.Fase}");
		AbortarMorte(terra, "bancada do torneio do Cell");
	}

	// =====================================================================
	// L. O SUPER PERFEITO PERDENDO SOLTA O KAMEHAMEHA (dono, 2026-10-20)
	// =====================================================================
	private void TcelOSuperPerfeitoPerdendo(Arena arena, ServerPlayer[] todos)
	{
		GD.Print("[torneiocell] -- L) o Super Perfeito perdendo, caido ou morrendo solta o Kamehameha que explode a Terra -- sempre");
		var terra = ZoneKey.Premade("Earth");
		ServerPlayer gohan = todos[3];
		int ultimo = _moldes!.Get("cell")!.Estagios.Length - 1;

		// ---- A MONTAGEM: o Cell PERFEITO da saga na Terra, sem torneio (a luta livre), inteiro -- e um tique do jogo nao o faz soltar nada
		if (TcelCellDaSagaSemTorneio(arena, todos, degrau: ultimo - 1) is not { } m1)
		{
			AfirmarTcel("(montagem) o Cell da saga na Terra, sem torneio", false, "nao nasceu");
			return;
		}
		ServerPlayer perfeito = m1.Cell;
		TickDoTorneioDoCell(Protocol.TickSeconds);
		AfirmarTcel("(montagem) o Cell PERFEITO da saga na Terra, sem torneio, de pe e inteiro -- e um tique do jogo nao o faz soltar nada",
					perfeito.Papel!.Estagio == ultimo - 1 && _torneioDoCell == null && _kamehamehaDoCell == null && !_canais.ContainsKey(perfeito.Id)
					&& !perfeito.Ficha.dead && m1.Ec.Fase == FaseDoChefe.NoMundo,
					$"degrau {perfeito.Papel.Estagio + 1}; torneio {_torneioDoCell != null}; kamehameha {_kamehamehaDoCell != null}; {m1.Ec.Fase}");

		// ---- A REGRA PURA, no corpo dele: so o tronco e a cabeca contam, e estritamente abaixo de 25%
		Body corpo = perfeito.Combate.Corpo;
		BodyPart? tronco = corpo.Partes.Find(p => p.Nome == Body.NomeDoTronco), cabeca = corpo.Partes.Find(p => p.Nome == "Cabeca"),
				  braco = corpo.Partes.Find(p => p.Nome == "Braco direito");
		bool Com(double t, double c, double b)
		{
			tronco!.Vida = t * tronco.VidaMax;
			cabeca!.Vida = c * cabeca.VidaMax;
			braco!.Vida = b * braco.VidaMax;
			return TorneioDoCell.Perdendo(corpo);
		}
		bool membros = tronco != null && cabeca != null && braco != null;
		bool inteiro = membros && Com(1, 1, 1), aos26 = membros && Com(0.26, 0.26, 1), aos25 = membros && Com(0.25, 0.25, 1);
		bool tronco24 = membros && Com(0.24, 1, 1), cabeca24 = membros && Com(1, 0.24, 1), braco10 = membros && Com(1, 1, 0.10);
		if (membros) Com(1, 1, 1);
		AfirmarTcel("**PERDENDO E O TRONCO OU A CABECA ABAIXO DE 25%** (*\"hp do torso ou cabeça abaixo de 25%\"*): inteiro, a 26% e a 25% ainda nao; o "
					+ "tronco a 24%, sim; a cabeca a 24%, sim; um braco a 10% com o tronco e a cabeca inteiros, nao",
					membros && !inteiro && !aos26 && !aos25 && tronco24 && cabeca24 && !braco10,
					$"membros {membros}; inteiro {inteiro}; 26% {aos26}; 25% {aos25}; tronco 24% {tronco24}; cabeca 24% {cabeca24}; braco 10% {braco10}");

		// ---- ...SO NO SUPER PERFEITO: o Perfeito com o tronco a 24% segue lutando
		TcelTroncoA(perfeito, 0.24);
		TickDoTorneioDoCell(Protocol.TickSeconds);
		AfirmarTcel("**...SO NO SUPER PERFEITO** (*\"Ele no super perfeito\"*): o Cell PERFEITO fora do torneio, com o tronco a 24%, segue lutando",
					_kamehamehaDoCell == null && !_canais.ContainsKey(perfeito.Id),
					$"kamehameha {_kamehamehaDoCell != null}; canal {_canais.GetValueOrDefault(perfeito.Id)?.Verbo}");

		// ---- O SUPER PERFEITO PERDENDO, primeiro com o defeito (ele segue lutando, o jogo de antes) e depois de verdade: o MESMO Kamehameha
		if (TcelCellDaSagaSemTorneio(arena, todos, degrau: ultimo) is not { } m2)
		{
			AfirmarTcel("**O SUPER PERFEITO PERDENDO SOLTA O KAMEHAMEHA**", false, "o Super Perfeito nao nasceu");
			return;
		}
		(ServerPlayer cell, EstadoDoElo est, EstadoDoChefe ec) = m2;
		TcelTroncoA(cell, 0.24);
		SuperPerfeitoNaoSoltaDeTeste = true;
		bool seguiriaLutando;
		try { TickDoTorneioDoCell(Protocol.TickSeconds); seguiriaLutando = _kamehamehaDoCell == null && !_canais.ContainsKey(cell.Id); }
		finally { SuperPerfeitoNaoSoltaDeTeste = false; }
		EscutaDoTorneioDoCell!.Clear();
		TickDoTorneioDoCell(Protocol.TickSeconds);
		CanalDeKi? canal = _canais.GetValueOrDefault(cell.Id);
		AfirmarTcel("**O SUPER PERFEITO PERDENDO SOLTA O KAMEHAMEHA** (*\"Ele no super perfeito tb usaria o kamehameha q destrói planeta se ele estiver "
					+ "perdendo\"*): na luta livre, com o tronco a 24%, ele para e carrega o MESMO Kamehameha -- o canal da tecnica, 2x, intocavel e parado -- "
					+ "e o mundo ouve",
					_kamehamehaDoCell is { } k && k.Cell == cell.Id && canal is { Verbo: "Kamehameha", Raio: null }
					&& Math.Abs(canal.Receita.EscalaVisual - 2 * canal.Receita.MultDeOnda) < 1e-6 && cell.Combate.Intocavel && CellForaDaVez(cell)
					&& _torneioDoCell == null && EscutaDoTorneioDoCell.Contains(TorneioDoCell.AnuncioDoKamehamehaDoSuperPerfeito(cell.Name)),
					$"kamehameha {_kamehamehaDoCell?.Cell}; canal {canal?.Verbo}; escala {canal?.Receita.EscalaVisual:0.##} (o comum {canal?.Receita.MultDeOnda:0.##}); "
					+ $"intocavel {cell.Combate.Intocavel}; parado {CellForaDaVez(cell)}; {EscutaDoCell()}");
		AfirmarTcel("   CONTRA-EXEMPLO (o jogo de antes): perdendo, ele seguiria lutando -- a regua de cima reprovaria", seguiriaLutando);

		// ---- GOHAN VENCE A DISPUTA: o Super Perfeito morre na hora, e de vez -- a saga fecha vencida (ele nao tem outra volta)
		DisputaDeKi? d3 = TcelGohanEntraNaFrente(cell, gohan, vezesOBpDoCell: 100);
		EscutaDoTorneioDoCell.Clear();
		EscutaDasSagas!.Clear();
		bool venceu = d3 != null && TcelGohanApertaAteOFim(gohan, cell);
		bool naHora = cell.Ficha.dead && _kamehamehaDoCell == null && MorteDaZona(terra) == null
			&& EscutaDoTorneioDoCell.Contains(TorneioDoCell.CellDesintegrado(cell.Name, gohan.Name));
		TickDasSagas();
		AfirmarTcel("**...E E O MESMO KAMEHAMEHA: GOHAN VENCE A DISPUTA E O SUPER PERFEITO MORRE DE VEZ** -- desintegrado na hora, a Terra inteira, e a "
					+ "saga fecha VENCIDA, com Gohan de heroi",
					venceu && naHora && ec.Fase == FaseDoChefe.Derrotado && ec.RessurgeEm == 0 && est.Fase == FaseDaSaga.Vencida
					&& EscutaDasSagas.Any(f => f.Contains(gohan.Name, StringComparison.Ordinal) && f.Contains("HERÓI", StringComparison.Ordinal)),
					$"disputa {d3 != null}; venceu {venceu}; morto {cell.Ficha.dead}; kamehameha {_kamehamehaDoCell != null}; terra {MorteDaZona(terra)?.Fase}; "
					+ $"{ec.Fase}; volta em {ec.RessurgeEm:0}; saga {est.Fase}; {EscutaDoCell()} | {string.Join(" | ", EscutaDasSagas)}");

		// ---- CAIDO: o golpe alto de uma vez pula a janela dos 25% e o nocauteia -- ele LEVANTA e solta (primeiro com o defeito: fica no chao)
		if (TcelCellDaSagaSemTorneio(arena, todos, degrau: ultimo) is not { } m3)
		{
			AfirmarTcel("**...E CAIDO TAMBEM: NOCAUTEADO POR UM GOLPE ALTO, ELE LEVANTA E SOLTA O KAMEHAMEHA**", false, "o Super Perfeito nao nasceu");
			return;
		}
		ServerPlayer caido = m3.Cell;
		TcelTroncoA(caido, 0.10);
		caido.Combate.Nocautear(30);
		SuperPerfeitoCaidoNaoSoltaDeTeste = true;
		bool ficariaNoChao;
		try { TickDoTorneioDoCell(Protocol.TickSeconds); ficariaNoChao = caido.Ficha.KO && _kamehamehaDoCell == null; }
		finally { SuperPerfeitoCaidoNaoSoltaDeTeste = false; }
		EscutaDoTorneioDoCell.Clear();
		TickDoTorneioDoCell(Protocol.TickSeconds);
		AfirmarTcel("**...E CAIDO TAMBEM: NOCAUTEADO POR UM GOLPE ALTO, ELE LEVANTA E SOLTA O KAMEHAMEHA** (*\"Caso ele seja nocauteado por sofrer um "
					+ "alto dano de uma vez, ele começa a usar o kamehameha\"*): o tronco a 10% e no chao -- um tique, e ele esta de pe, carregando, intocavel",
					!caido.Ficha.KO && _kamehamehaDoCell?.Cell == caido.Id && _canais.GetValueOrDefault(caido.Id) is { Verbo: "Kamehameha", Raio: null }
					&& caido.Combate.Intocavel && EscutaDoTorneioDoCell.Contains(TorneioDoCell.AnuncioDoKamehamehaDoSuperPerfeitoCaido(caido.Name)),
					$"caido {caido.Ficha.KO}; kamehameha {_kamehamehaDoCell?.Cell}; canal {_canais.GetValueOrDefault(caido.Id)?.Verbo}; "
					+ $"intocavel {caido.Combate.Intocavel}; {EscutaDoCell()}");
		AfirmarTcel("   CONTRA-EXEMPLO (o vigia so de quem esta de pe): ele ficaria no chao, sem Kamehameha -- a regua de cima reprovaria", ficariaNoChao);

		// ---- E GOHAN PERDENDO A DISPUTA: o Kamehameha do Super Perfeito segue, e a Terra explode
		if (TcelCellDaSagaSemTorneio(arena, todos, degrau: ultimo) is not { } m4)
		{
			AfirmarTcel("**...E GOHAN PERDENDO A DISPUTA, O KAMEHAMEHA DO SUPER PERFEITO SEGUE E A TERRA EXPLODE**", false, "o Super Perfeito nao nasceu");
			return;
		}
		(ServerPlayer cellQueVence, EstadoDoElo estDele, _) = m4;
		TcelTroncoA(cellQueVence, 0.24);
		TickDoTorneioDoCell(Protocol.TickSeconds);
		DisputaDeKi? d4 = TcelGohanEntraNaFrente(cellQueVence, gohan, vezesOBpDoCell: 0.001);
		for (int i = 0; i < 30 * 40 && d4 != null && _emEmbateDeKi.ContainsKey(gohan.Id); i++) TcelUmTiqueDoJogo();
		bool gohanPerdeu = d4 != null && !_canais.ContainsKey(gohan.Id) && !cellQueVence.Ficha.dead;
		for (int i = 0; i < 30 * 20 && _kamehamehaDoCell != null; i++) TcelUmTiqueDoJogo();
		AfirmarTcel("**...E GOHAN PERDENDO A DISPUTA, O KAMEHAMEHA DO SUPER PERFEITO SEGUE E A TERRA EXPLODE** (*\"Se o jogador perder o clash obviamente "
					+ "a terra tb explode\"*)",
					gohanPerdeu && MorteDaZona(terra)?.Fase == FaseDaMorte.Explodindo && estDele.Fase == FaseDaSaga.Consumada
					&& !_players.ContainsKey(cellQueVence.Id),
					$"disputa {d4 != null}; Gohan perdeu {gohanPerdeu}; terra {MorteDaZona(terra)?.Fase}; saga {estDele.Fase}; "
					+ $"no mundo {_players.ContainsKey(cellQueVence.Id)}");
		if (MorteDaZona(terra) is { Fase: FaseDaMorte.Explodindo }) AbortarMorte(terra, "bancada do torneio do Cell");

		// ---- O GOLPE QUE O MATARIA VIRA O KAMEHAMEHA, pela porta unica da morte (`Morrer`): primeiro com o defeito (a porta de sempre) e depois de
		// verdade, com a cabeca arrancada e o tronco a 5%
		if (TcelCellDaSagaSemTorneio(arena, todos, degrau: ultimo) is not { } m5)
		{
			AfirmarTcel("**...E O GOLPE QUE O MATARIA VIRA O KAMEHAMEHA**", false, "o Super Perfeito nao nasceu");
			return;
		}
		ServerPlayer doDefeito = m5.Cell;
		TcelTroncoA(doDefeito, 0);
		SuperPerfeitoMorreDeTeste = true;
		bool viraria;
		try { doDefeito.Combate.Morrer(); viraria = _kamehamehaDoCell != null; }
		finally { SuperPerfeitoMorreDeTeste = false; }

		if (TcelCellDaSagaSemTorneio(arena, todos, degrau: ultimo) is not { } m6)
		{
			AfirmarTcel("**...E O GOLPE QUE O MATARIA VIRA O KAMEHAMEHA**", false, "o Super Perfeito nao nasceu");
			return;
		}
		ServerPlayer morrendo = m6.Cell;
		TcelTroncoA(morrendo, 0.05);
		if (morrendo.Combate.Corpo.Partes.Find(p => p.Nome == "Cabeca") is { } aCabeca) morrendo.Combate.Corpo.Decepar(aCabeca);
		morrendo.Combate.SincronizarVida();
		EscutaDoTorneioDoCell.Clear();
		bool morreu = morrendo.Combate.Morrer();
		AfirmarTcel("**...E O GOLPE QUE O MATARIA VIRA O KAMEHAMEHA** (*\"basicamente deveria ser impossível ele nao jogar o kamehameha no final\"*): a "
					+ "cabeca arrancada e o tronco a 5%, pela porta unica da morte -- ele nao morre: a cabeca volta, ele levanta por um fio e carrega, intocavel",
					!morreu && !morrendo.Ficha.dead && !morrendo.Ficha.KO && !morrendo.Combate.Corpo.DeveMorrer() && !morrendo.Combate.Corpo.DeveNocautear()
					&& _kamehamehaDoCell?.Cell == morrendo.Id && _canais.GetValueOrDefault(morrendo.Id) is { Verbo: "Kamehameha", Raio: null }
					&& morrendo.Combate.Intocavel && EscutaDoTorneioDoCell.Contains(TorneioDoCell.AnuncioDoKamehamehaDoSuperPerfeitoCaido(morrendo.Name)),
					$"morreu {morreu}; morto {morrendo.Ficha.dead}; caido {morrendo.Ficha.KO}; perdida {morrendo.Combate.Corpo.DeveMorrer()}; "
					+ $"no chao {morrendo.Combate.Corpo.DeveNocautear()}; kamehameha {_kamehamehaDoCell?.Cell}; canal {_canais.GetValueOrDefault(morrendo.Id)?.Verbo}; "
					+ $"intocavel {morrendo.Combate.Intocavel}; {EscutaDoCell()}");
		AfirmarTcel("   CONTRA-EXEMPLO (a porta da morte de sempre): o golpe letal nao viraria o Kamehameha -- a regua de cima reprovaria", !viraria);
	}

	// =====================================================================
	// M. O CELL CACA OS ANDROIDES (dono, 2026-10-20)
	// =====================================================================
	private void TcelACacaAosAndroides(ServerPlayer[] todos)
	{
		GD.Print("[torneiocell] -- M) o Cell Imperfeito caca os androides: prioridade maxima, derruba e absorve");
		var terra = ZoneKey.Premade("Earth");
		ServerPlayer goku = todos[0];
		if (MorteDaZona(terra) is { Fase: FaseDaMorte.Explodindo }) AbortarMorte(terra, "bancada do torneio do Cell");
		SagasLimpas();
		TickDoTorneioDoCell(1.0);   // (o Kamehameha e o torneio de antes, se ficaram, fecham: o corpo deles saiu com as sagas)
		foreach (string saga in new[] { "freeza_vegeta", "freeza_namek" }) _sagas.Do(saga)!.Fase = FaseDaSaga.Vencida;
		EloDaSaga elo = Array.Find(_cadeia, e => e.Id == "cell")!;
		EstadoDoElo est = _sagas.Do("cell")!;
		est.Disparou = true;
		est.Fase = FaseDaSaga.EmCurso;
		EstadoDoChefe Nascer(string molde)
		{
			var ec = new EstadoDoChefe
			{
				Molde = molde, Fase = FaseDoChefe.ACaminho, DiasRestantes = 0, Bps = Sagas.Pinar(_moldes!.Get(molde)!, MediaDoServidor()),
			};
			est.Chefes.Add(ec);
			NascerChefeDaSaga(elo, est, ec, ComoChegou.Retomando);
			return ec;
		}
		EstadoDoChefe e17 = Nascer("androide_17"), e18 = Nascer("androide_18"), eCell = Nascer("cell");
		ServerPlayer? a17 = _players.GetValueOrDefault(e17.Corpo), a18 = _players.GetValueOrDefault(e18.Corpo), cell = _players.GetValueOrDefault(eCell.Corpo);
		AfirmarTcel("(montagem) a saga do Cell com os dois androides e o Cell IMPERFEITO no mundo",
					a17 != null && a18 != null && cell?.Papel is { Estagio: 0 }, $"17 {a17 != null}; 18 {a18 != null}; cell degrau {cell?.Papel?.Estagio + 1}");
		if (a17 == null || a18 == null || cell == null) return;

		// O LUGAR: o Cell no meio da arena, o Goku a TRES tiles (o jogador mais perto, e tocavel), o 17 a dez e a 18 a quinze
		Vec2 centro = _torneioCfg.Terra.Arena.Centro;
		const float T = ZoneCollision.TileSize;
		Transportar(cell, terra, centro);
		Transportar(a17, terra, centro + new Vec2(10 * T, 0));
		Transportar(a18, terra, centro + new Vec2(15 * T, 0));
		foreach (ServerPlayer p in todos) p.Combate.Carencia = 0;
		Transportar(goku, terra, centro + new Vec2(-3 * T, 0));

		// ---- A CACA, PRIORIDADE MAXIMA: o monitor da saga (uma vez por segundo) impoe o androide de pe mais perto, mesmo com o Goku mais perto
		CellNaoCacaOsAndroidesDeTeste = true;
		ServerPlayer? presaComODefeito;
		try { MonitorarChefe(elo, est, eCell); presaComODefeito = PresaDoNpc(cell); }
		finally { CellNaoCacaOsAndroidesDeTeste = false; }
		MonitorarChefe(elo, est, eCell);
		ServerPlayer? presa = PresaDoNpc(cell);
		AfirmarTcel("**O CELL CACA OS ANDROIDES, PRIORIDADE MAXIMA** (*\"faça ele atacar os androides como prioridade maxima dele\"*): com o Goku a "
					+ "3 tiles e o 17 a 10, o alvo do Cell Imperfeito e o 17 -- o androide de pe mais perto",
					cell.Papel!.PresaDoRoteiro == a17.Id && presa == a17,
					$"alvo imposto {cell.Papel.PresaDoRoteiro}; presa {presa?.Name} (17 {a17.Id}, Goku {goku.Id})");
		AfirmarTcel("   CONTRA-EXEMPLO (o jogo de antes: ele so ia no androide que outro derrubou): a presa seria um jogador -- a regua de cima reprovaria",
					presaComODefeito is { } pc && EhJogador(pc), $"presa {presaComODefeito?.Name}");

		// ---- O GOLPE DELE QUE MATARIA O ANDROIDE SO O DERRUBA: primeiro com o defeito (mata) na 18, e de verdade no 17
		bool Derrubado(ServerPlayer a) => !a.Ficha.dead && a.Ficha.KO && !a.Combate.Corpo.DeveMorrer();
		a18.UltimoAgressor = cell.Id;
		TcelTroncoA(a18, 0);
		CellMataOAndroideDeTeste = true;
		bool derrubadoComODefeito;
		try { a18.Combate.Morrer(); derrubadoComODefeito = Derrubado(a18); }
		finally { CellMataOAndroideDeTeste = false; }
		a17.UltimoAgressor = cell.Id;
		TcelTroncoA(a17, 0);
		bool morreu = a17.Combate.Morrer();
		AfirmarTcel("**O GOLPE DO CELL QUE MATARIA O ANDROIDE SO O DERRUBA** -- ele caca pra absorver: o 17 com o tronco zerado pelo Cell nao morre, "
					+ "cai nocauteado, por um fio",
					!morreu && Derrubado(a17), $"morreu {morreu}; morto {a17.Ficha.dead}; caido {a17.Ficha.KO}; perdido {a17.Combate.Corpo.DeveMorrer()}");
		AfirmarTcel("   CONTRA-EXEMPLO (o golpe letal de sempre): a 18 nao ficaria so caida -- a regua de cima reprovaria", !derrubadoComODefeito,
					$"morta {a18.Ficha.dead}; caida {a18.Ficha.KO}");

		// ---- ...E O CAIDO E ABSORVIDO: longe, ele vai atras dele (o alvo imposto); perto, da o BOTE e o absorve -- primeiro sem o bote (so
		// encostando, o jogo da rodada g6) a seis tiles, e depois de verdade
		Vec2 doCaido = a17.Pos;
		cell.Pos = doCaido + new Vec2(30 * T, 0);
		MonitorarChefe(elo, est, eCell);
		bool foiAtras = cell.Papel.PresaDoRoteiro == a17.Id && _players.ContainsKey(a17.Id);
		cell.Pos = doCaido + new Vec2(6 * T, 0);
		CellSemBoteDeTeste = true;
		bool absorveuSemBote;
		try { MonitorarChefe(elo, est, eCell); absorveuSemBote = !_players.ContainsKey(a17.Id); }
		finally { CellSemBoteDeTeste = false; }
		EscutaDasSagas!.Clear();
		MonitorarChefe(elo, est, eCell);
		AfirmarTcel("**...E O CAIDO E ABSORVIDO** -- longe (30 tiles) o Cell vai atras do 17 no chao; a 6 tiles ele da o BOTE, encosta e o absorve: a "
					+ "Semi-Perfeita, 1 B",
					foiAtras && e17.Fase == FaseDoChefe.Consumido && !_players.ContainsKey(a17.Id) && cell.Papel.Estagio == 1
					&& (cell.Pos - doCaido).LengthSquared <= 4 * T * T && EscutaDasSagas.Any(f => f.Contains("ABSORVEU", StringComparison.Ordinal)),
					$"foi atras {foiAtras}; 17 {e17.Fase}; degrau {cell.Papel.Estagio + 1}; o Cell a {(cell.Pos - doCaido).Length / T:0.#} tile(s) dele; "
					+ $"{string.Join(" | ", EscutaDasSagas)}");
		AfirmarTcel("   CONTRA-EXEMPLO (sem o bote, so encostando -- o robo da g6 ficou nisso 90 s): a 6 tiles ele nao o absorveria -- a regua de cima reprovaria",
					!absorveuSemBote, $"absorveu {absorveuSemBote}");

		// ---- E NA CENA DA TRANSFORMACAO ELE NAO ABSORVE NINGUEM: uma saga nova, os dois androides caidos a um e tres tiles dele, e a Semi-Perfeita
		// comecando (o `AvancarEstagio` de producao). Primeiro de verdade, depois com o defeito (ele age na cena, e o 17 encostado e engolido)
		SagasLimpas();
		TickDoTorneioDoCell(1.0);
		est = _sagas.Do("cell")!;
		est.Disparou = true;
		est.Fase = FaseDaSaga.EmCurso;
		EstadoDoChefe f17 = Nascer("androide_17"), f18 = Nascer("androide_18"), fCell = Nascer("cell");
		if (!_players.TryGetValue(f17.Corpo, out ServerPlayer? b17) || !_players.TryGetValue(f18.Corpo, out ServerPlayer? b18)
			|| !_players.TryGetValue(fCell.Corpo, out ServerPlayer? cell3) || cell3.Papel == null)
		{
			AfirmarTcel("**...E NA CENA DA TRANSFORMACAO ELE NAO ABSORVE NINGUEM**", false, "a saga nova nao nasceu");
			return;
		}
		Transportar(cell3, terra, centro);
		Transportar(b17, terra, centro + new Vec2(T, 0));
		Transportar(b18, terra, centro + new Vec2(3 * T, 0));
		b17.Combate.Nocautear(120);
		b18.Combate.Nocautear(120);
		AvancarEstagio(cell3);   // a Semi-Perfeita, e a cena dela
		MonitorarChefe(elo, est, fCell);
		bool ninguem = _players.ContainsKey(b17.Id) && _players.ContainsKey(b18.Id) && cell3.Papel.Estagio == 1;
		bool naCena = EmCena(cell3);
		ChefeAgeNaCenaDeTeste = true;
		bool absorveriaNaCena;
		try { MonitorarChefe(elo, est, fCell); absorveriaNaCena = !_players.ContainsKey(b17.Id) || !_players.ContainsKey(b18.Id); }
		finally { ChefeAgeNaCenaDeTeste = false; }
		AfirmarTcel("**...E NA CENA DA TRANSFORMACAO ELE NAO ABSORVE NINGUEM**: os dois androides caidos a um e tres tiles dele, e ele fica na cena -- "
					+ "a proxima absorcao espera ela acabar",
					ninguem && naCena, $"os dois no mundo {ninguem}; em cena {naCena}; degrau {cell3.Papel.Estagio + 1}");
		AfirmarTcel("   CONTRA-EXEMPLO (o Cell agindo na cena): ele engoliria um deles no meio dela -- a regua de cima reprovaria", absorveriaNaCena,
					$"absorveu {absorveriaNaCena}");
	}

	// =====================================================================
	// P. NA CENA DA TRANSFORMACAO O CELL NAO AGE (dono, 2026-10-20)
	// =====================================================================
	private void TcelACenaDaTransformacao(ServerPlayer[] todos)
	{
		GD.Print("[torneiocell] -- P) na cena da transformacao o Cell nao age: larga o raio, nao golpeia, nao atira, e fica intocavel");
		Arena arena = _torneioCfg.Terra.Arena;
		const float T = ZoneCollision.TileSize;

		// A CENA: o Cell Imperfeito da saga com o Kamehameha na mao (o canal aberto, carregando) e um jogador DE PE colado nele -- e o degrau
		// seguinte pelo `AvancarEstagio` de producao (o que a absorcao e o dano chamam): a cena da Semi-Perfeita
		(ServerPlayer Cell, ServerPlayer Alvo)? Montar(ServerPlayer alvo)
		{
			if (TcelCellDaSagaSemTorneio(arena, todos, degrau: 0) is not { } m) return null;
			ServerPlayer cell = m.Cell;
			alvo.Combate.Carencia = 0;
			if (alvo.Ficha.KO) alvo.Combate.Levantar();
			Transportar(alvo, cell.Zone, cell.Pos + new Vec2(-T, 0));
			cell.Ficha.Ki = cell.Ficha.MaxKi;
			RaioNomeadoG6(cell, "Kamehameha");
			AvancarEstagio(cell);
			return (cell, alvo);
		}
		// ---- primeiro com o defeito (o cerebro segue pensando na cena e o raio fica na mao -- o jogo de antes), contra o Kuririn
		ChefeAgeNaCenaDeTeste = true;
		bool seguravaComODefeito = false, agiuComODefeito = false;
		try
		{
			if (Montar(todos[1]) is { } d)
			{
				seguravaComODefeito = _canais.ContainsKey(d.Cell.Id);
				agiuComODefeito = TcelRodarACena(d.Cell, d.Alvo, 8);
			}
		}
		finally { ChefeAgeNaCenaDeTeste = false; }

		// ---- e de verdade, contra o Goku
		if (Montar(todos[0]) is not { } m2)
		{
			AfirmarTcel("**NA CENA DA TRANSFORMACAO ELE LARGA O RAIO E FICA INTOCAVEL**", false, "o Cell nao nasceu");
			return;
		}
		(ServerPlayer cell2, ServerPlayer goku) = m2;
		AfirmarTcel("**NA CENA DA TRANSFORMACAO ELE LARGA O RAIO E FICA INTOCAVEL** (*\"faça o cell nao atacar ou se mover enquanto se transforma na "
					+ "proxima forma. e ele fica intocavel nessa cinematica\"*): a Semi-Perfeita comeca, o Kamehameha que ele carregava some da mao, e "
					+ "ninguem o atinge",
					cell2.Papel!.Estagio == 1 && EmCena(cell2) && cell2.Combate.Intocavel && !_canais.ContainsKey(cell2.Id),
					$"degrau {cell2.Papel.Estagio + 1}; em cena {EmCena(cell2)} ({cell2.CenaSegundos:0.#} s); intocavel {cell2.Combate.Intocavel}; "
					+ $"canal {_canais.GetValueOrDefault(cell2.Id)?.Verbo}");
		Vec2 onde = cell2.Pos;
		bool agiu = TcelRodarACena(cell2, goku, 8);
		AfirmarTcel("**...E NAO AGE ATE ELA ACABAR**: oito segundos de jogo com o Goku colado nele -- nenhum golpe, nenhum raio, e ele nao sai do lugar",
					!agiu && (cell2.Pos - onde).LengthSquared < 1 && EmCena(cell2),
					$"agiu {agiu}; andou {(cell2.Pos - onde).Length:0.#} px; em cena {EmCena(cell2)}");
		AfirmarTcel("   CONTRA-EXEMPLO (o jogo de antes: o cerebro seguia pensando na cena): ele seguiria com o raio na mao e agiria -- a regua de cima reprovaria",
					seguravaComODefeito && agiuComODefeito, $"segurava {seguravaComODefeito}; agiu {agiuComODefeito}");
	}

	// =====================================================================
	// Q. ...E TODO CHEFE DE SAGA NA CENA DA PROPRIA TRANSFORMACAO: O FREEZA (dono, 2026-10-20)
	// =====================================================================
	private void TcelACenaDoFreeza(Arena arena, ServerPlayer[] todos)
	{
		GD.Print("[torneiocell] -- Q) todo chefe de saga na cena da propria transformacao: o Freeza de Namek no corpo novo e na forma final");
		const float T = ZoneCollision.TileSize;
		const string Pedido = "(*\"todo chefe de saga fica parado, sem atacar e intocável na cena da própria transformação (o Freeza também)\"*)";

		// A CENA: o Freeza da saga de Namek no degrau pedido, com o Death Beam na mao (o canal aberto, carregando) e um jogador DE PE colado nele
		// -- e o degrau seguinte pelo `AvancarEstagio` de producao (o que o dano e o golpe que o mataria chamam)
		(ServerPlayer Freeza, ServerPlayer Alvo)? Montar(ServerPlayer alvo, int degrau)
		{
			if (TcelFreezaDaSaga(arena, todos, degrau) is not { } freeza) return null;
			alvo.Combate.Carencia = 0;
			if (alvo.Ficha.KO) alvo.Combate.Levantar();
			Transportar(alvo, freeza.Zone, freeza.Pos + new Vec2(-T, 0));
			freeza.Ficha.Ki = freeza.Ficha.MaxKi;
			RaioNomeadoG6(freeza, "Death_Beam");
			AvancarEstagio(freeza);
			return (freeza, alvo);
		}

		// ---- O CORPO NOVO (a primeira troca, a cena curta de 2,5 s) -- primeiro com o defeito (o jogo de antes: so o Cell parava), contra o Kuririn
		ChefeAgeNaCenaDeTeste = true;
		bool seguravaComODefeito = false;
		try { if (Montar(todos[1], 0) is { } d1) seguravaComODefeito = _canais.ContainsKey(d1.Freeza.Id); }
		finally { ChefeAgeNaCenaDeTeste = false; }
		if (Montar(todos[0], 0) is not { } m1)
		{
			AfirmarTcel("**...E O FREEZA NA TROCA DE CORPO LARGA O RAIO E FICA INTOCAVEL**", false, "o Freeza nao nasceu");
			return;
		}
		ServerPlayer f1 = m1.Freeza;
		AfirmarTcel($"**...E O FREEZA NA TROCA DE CORPO LARGA O RAIO E FICA INTOCAVEL** {Pedido}: a segunda forma comeca, o Death Beam que ele "
					+ "carregava some da mao, ninguem o atinge e o cerebro para",
					f1.Papel!.Estagio == 1 && EmCena(f1) && f1.Combate.Intocavel && !_canais.ContainsKey(f1.Id) && ChefeParadoNaCena(f1),
					$"degrau {f1.Papel.Estagio + 1}; em cena {EmCena(f1)} ({f1.CenaSegundos:0.#} s); intocavel {f1.Combate.Intocavel}; "
					+ $"canal {_canais.GetValueOrDefault(f1.Id)?.Verbo}; parado {ChefeParadoNaCena(f1)}");
		AfirmarTcel("   CONTRA-EXEMPLO (o jogo de antes: so o Cell parava na cena): o Freeza seguiria com o Death Beam na mao -- a regua de cima reprovaria",
					seguravaComODefeito, $"segurava {seguravaComODefeito}");

		// ---- A FORMA FINAL (a cena grande, 28 s): dez segundos de jogo com o jogador colado nele -- primeiro com o defeito, contra o Kuririn.
		// DEZ e nao oito, como na familia P: com o raio na mao ele so age quando a carga fecha, e a carga e o cerebro do Freeza nao sao os do Cell
		ChefeAgeNaCenaDeTeste = true;
		bool agiuComODefeito = false;
		try { if (Montar(todos[1], 3) is { } d2) agiuComODefeito = TcelRodarACena(d2.Freeza, d2.Alvo, 10); }
		finally { ChefeAgeNaCenaDeTeste = false; }
		if (Montar(todos[0], 3) is not { } m2)
		{
			AfirmarTcel("**...E NA FORMA FINAL NAO AGE ATE A CENA ACABAR**", false, "o Freeza nao nasceu");
			return;
		}
		(ServerPlayer f2, ServerPlayer goku) = m2;
		bool naVirada = f2.Papel!.Estagio == 4 && EmCena(f2) && f2.Combate.Intocavel && !_canais.ContainsKey(f2.Id);
		Vec2 onde = f2.Pos;
		bool agiu = TcelRodarACena(f2, goku, 10);
		AfirmarTcel("**...E NA FORMA FINAL NAO AGE ATE A CENA ACABAR**: a forma final comeca sem o raio na mao, e dez segundos de jogo com o Goku "
					+ "colado nele -- nenhum golpe, nenhum raio, e ele nao sai do lugar",
					naVirada && !agiu && (f2.Pos - onde).LengthSquared < 1 && EmCena(f2),
					$"degrau {f2.Papel.Estagio + 1}; na virada {naVirada}; agiu {agiu}; andou {(f2.Pos - onde).Length:0.#} px; em cena {EmCena(f2)} "
					+ $"({f2.CenaSegundos:0.#} s)");
		AfirmarTcel("   CONTRA-EXEMPLO (o jogo de antes): ele agiria no meio da forma final -- a regua de cima reprovaria", agiuComODefeito,
					$"agiu {agiuComODefeito}");

		// ---- E A ESFERA DO ULTIMATO NAO E TRANSFORMACAO: a Death Ball escreve o MESMO relogio da cena (parado e intocavel enquanto ela cresce),
		// e ali o cerebro segue ligado, como era
		f2.CenaSegundos = 0;
		ComecarASupernova(f2, Jandirus.Core.Npc.AtaqueDoUltimato.DeathBall);
		bool naEsfera = EmCena(f2) && f2.SupernovaDesde != 0, pensa = !ChefeParadoNaCena(f2);
		AcabarASupernova(f2);
		AfirmarTcel("**...E A DEATH BALL DO ULTIMATO SEGUE COMO ERA**: ela prende o corpo pelo mesmo relogio da cena, mas nao e transformacao -- o "
					+ "cerebro continua ligado", naEsfera && pensa, $"na esfera {naEsfera}; pensa {pensa}");
	}

	/// <summary>
	/// O JOGO RODANDO NA CENA, por <paramref name="segundos"/>: o tique dos corpos (o cerebro) e o dos canais (o raio que sai da mao). Agiu =
	/// golpeou o alvo, soltou o raio, ou tem um golpe no ar (familias P e Q).
	/// </summary>
	private bool TcelRodarACena(ServerPlayer chefe, ServerPlayer alvo, double segundos)
	{
		long ataqueAntes = chefe.AtaqueAte;
		alvo.UltimoAgressor = 0;
		for (int i = 0; i < (int)(segundos / Protocol.TickSeconds); i++)
		{
			TickDosCorposSemDono(Protocol.TickSeconds);
			TickDosCanaisDeKi(Protocol.TickSeconds);
			if (alvo.UltimoAgressor == chefe.Id || _canais.GetValueOrDefault(chefe.Id)?.Raio != null || chefe.AtaqueAte > ataqueAntes) return true;
		}
		return false;
	}

	/// <summary>
	/// (2026-10-20) O FREEZA DA SAGA DE NAMEK -- as sagas limpas, a de Vegeta vencida e a de Namek em curso com ele chegando hoje NO DEGRAU pedido
	/// (o nascimento de producao, `NascerChefeDaSaga`), trazido pro meio da arena da Terra (o chao que a familia P ja usa: a regra nao olha o
	/// planeta), e os quatro fora dela e INTOCAVEIS. Nulo = nao nasceu.
	/// </summary>
	private ServerPlayer? TcelFreezaDaSaga(Arena arena, ServerPlayer[] todos, int degrau)
	{
		var terra = ZoneKey.Premade("Earth");
		if (MorteDaZona(terra) is { Fase: FaseDaMorte.Explodindo }) AbortarMorte(terra, "bancada do torneio do Cell");
		SagasLimpas();
		TickDoTorneioDoCell(1.0);
		_sagas.Do("freeza_vegeta")!.Fase = FaseDaSaga.Vencida;
		EloDaSaga elo = Array.Find(_cadeia, e => e.Id == "freeza_namek")!;
		EstadoDoElo est = _sagas.Do("freeza_namek")!;
		est.Disparou = true;
		est.Fase = FaseDaSaga.EmCurso;
		var ec = new EstadoDoChefe
		{
			Molde = "freeza_namek", Fase = FaseDoChefe.ACaminho, DiasRestantes = 0, Degrau = degrau,
			Bps = Sagas.Pinar(_moldes!.Get("freeza_namek")!, MediaDoServidor()),
		};
		est.Chefes.Add(ec);
		NascerChefeDaSaga(elo, est, ec);
		for (int i = 0; i < todos.Length; i++)
		{
			Transportar(todos[i], terra, ForaDaArenaDeTeste(arena, i));
			todos[i].Combate.Carencia = CarenciaDeEspera;
		}
		if (!_players.TryGetValue(ec.Corpo, out ServerPlayer? freeza)) return null;
		Transportar(freeza, terra, arena.Centro);
		return freeza;
	}

	// =====================================================================
	// W. O DOBRO DO JOGADOR MAIS FORTE E A PERICIA NO MAXIMO (dono, 2026-10-20)
	// =====================================================================
	private void TcelODobroDoMaisForte(Arena arena, ServerPlayer[] todos)
	{
		GD.Print($"[torneiocell] -- W) os chefes de saga e o Bills com {VezesOMaisForte:0.##}x o jogador mais forte (o pedido 138; era o dobro), e a "
				 + "pericia no maximo");
		const string Pedido = "(*\"faça os stats do boss(todos os de saga e contando o bills) ser o dobro do jogador mais forte, a pericia deles sempre "
							  + "é a maxima\"*)";
		const float T = ZoneCollision.TileSize;
		ServerPlayer goku = todos[0], gohan = todos[3];
		Fighter g = goku.Ficha;
		var guardado = todos.Select(p => (P: p, Bp: p.Ficha.BP, Po: p.Ficha.physoff, Ko: p.Ficha.kioff, Kd: p.Ficha.kidef, Raio: p.Ficha.beamskill)).ToList();
		var extras = new List<ServerPlayer>();

		// O MAXIMO DE CADA PERICIA DE LUTA: 100, ou a maior dos quatro, se algum passou
		bool PericiaNoMaximo(Fighter c, out string fora)
		{
			fora = string.Join(", ", TcelPericias.Where(p => Math.Abs(p.Ler(c) - Math.Max(100, todos.Max(j => p.Ler(j.Ficha)))) > 1e-9)
												 .Select(p => $"{p.Nome} {p.Ler(c):0.#}"));
			return fora.Length == 0;
		}

		try
		{
			// O MAIS FORTE: o Goku, com 1,5 bilhao -- o BP do dono quando o raio dele derrubou o Cell Perfeito -- e os crus de quem treinou (o dobro
			// de um recem-criado nao provaria nada); os outros tres abaixo de um bilhao. E o Gohan com a pericia de raio em 250: a de um jogador
			// acima de 100, que o chefe acompanha
			foreach (ServerPlayer p in todos)
			{
				p.Ficha.BP = p == goku ? 1.5e9 : Math.Min(p.Ficha.BP, 1e9);
				p.Ficha.Statify();
				p.Ficha.PowerLevel();
			}
			g.physoff *= 1.5;
			g.kioff *= 1.8;
			g.kidef *= 1.6;
			g.Statify();
			gohan.Ficha.beamskill = 250;

			// ---- O CELL PERFEITO DA SAGA, pelo nascimento de producao: o dobro chega NO NASCIMENTO, antes de qualquer tique
			if (TcelCellDaSagaSemTorneio(arena, todos, degrau: 2) is not { } m)
			{
				AfirmarTcel("**O CELL PERFEITO NASCE COM 1,25x O JOGADOR MAIS FORTE**", false, "o Cell nao nasceu");
				return;
			}
			ServerPlayer cell = m.Cell;
			AfirmarTcel($"**O CELL PERFEITO NASCE COM 1,25x O JOGADOR MAIS FORTE** {Pedido}, e o pedido 138 (*\"25% acima do player mais forte e nao "
						+ "2x\"*): os oito atributos da aba Stats dele sao 1,25 vez os do Goku, o de maior BP -- antes de qualquer tique",
						TcelAsVezesDe(cell.Ficha, g), $"Cell {TcelOito(cell.Ficha)} | Goku {TcelOito(g)}");
			bool noMaximo = PericiaNoMaximo(cell.Ficha, out string fora);
			AfirmarTcel("**...E A PERICIA NO MAXIMO**: as dezessete pericias de luta em 100 -- e a de raio em 250, porque o Gohan passou de 100",
						noMaximo && cell.Ficha.beamskill == 250,
						noMaximo ? $"raio {cell.Ficha.beamskill:0}; defesa de ki {cell.Ficha.kidefenseskill:0}; carga {cell.Ficha.chargedskill:0}"
								 : $"fora do maximo: {fora}");

			// ---- ...E A CADA SEGUNDO: o Goku sobe a ofensiva de ki (um treino, uma forma) e, no tique seguinte do roteiro, o Cell acompanha
			g.kioff *= 1.5;
			g.Statify();
			bool aindaODeAntes = Math.Abs(cell.Ficha.Rkioff - TcelVezesDoPedido * g.Rkioff) > 0.05 * g.Rkioff;
			TickDoRoteiro();
			AfirmarTcel("**...E ACOMPANHA A CADA SEGUNDO**: o Goku sobe a ofensiva de ki e, no tique seguinte do roteiro, o Cell tem 1,25 vez a nova",
						aindaODeAntes && TcelAsVezesDe(cell.Ficha, g),
						$"antes do tique, as vezes da velha {aindaODeAntes}; Cell {TcelOito(cell.Ficha)} | Goku {TcelOito(g)}");

			// ---- O CASO DO DONO, pela conta do servidor: o raio de potencia 2 do Goku no Cell Perfeito. (2026-10-27, o pedido 138) Com 2x, doze
			// segundos dele mirados no tronco nao tiravam nem metade dos 80 que nocauteiam (24,6); com 1,25x o numero e o que o dono escolheu --
			// ele sai MEDIDO aqui, e a regua de agora e a do contra-exemplo la embaixo: a ficha de recem-criado leva mais de tres vezes isso
			(double Dano, double NoTronco, double Embate) raio = TcelORaioDoGoku(g, cell);
			GD.Print($"[torneiocell]   (medida, o pedido 138) o raio do caso do dono no Cell Perfeito de {VezesOMaisForte:0.##}x: {raio.Dano:0.##} por "
					 + $"batida; 12 s no tronco {raio.NoTronco:0.#} (o nocaute e 80; com 2x eram 24,6)");
			AfirmarTcel("**...E O EMBATE E DELE** (*\"tive uma vantagem enorme no ki mesmo sendo mt mais fraco q o cell\"*): a Onda de Ki do Cell -- a "
						+ "potencia 1, o raio mais fraco que ele usa -- tem mais poder que esse raio",
						raio.Embate > 1, $"Cell/Goku {raio.Embate:0.##}");

			// ---- O BILLS TAMBEM -- e o Whis, que o pedido nao nomeia, fica com a ficha dele
			Vec2 aqui = cell.Pos;
			ServerPlayer? bills = NascerNpc("bills", cell.Zone, aqui + new Vec2(8 * T, 0), 7_600_401);
			ServerPlayer? whis = NascerNpc("whis", cell.Zone, aqui + new Vec2(8 * T, 2 * T), 7_600_402);
			foreach (ServerPlayer? x in new[] { bills, whis }) if (x != null) extras.Add(x);
			AfirmarTcel("**O BILLS TAMBEM TEM 1,25x** (*\"contando o bills\"*), e a pericia no maximo -- e o Whis, que o pedido nao nomeia, fica com "
						+ "a ficha dele",
						bills != null && whis != null && TcelAsVezesDe(bills.Ficha, g) && PericiaNoMaximo(bills.Ficha, out _) && !TcelAsVezesDe(whis.Ficha, g),
						$"Bills {(bills == null ? "-" : TcelOito(bills.Ficha))} | Whis {(whis == null ? "-" : TcelOito(whis.Ficha))} | Goku {TcelOito(g)}");

			// ---- ...E TODO CHEFE DE SAGA: um corpo de cada outro molde que as sagas poem no mundo
			var errados = new List<string>();
			ulong lugar = 7_600_410;
			foreach (string molde in new[] { "freeza_vegeta", "freeza_namek", "androide_17", "androide_18", "majin_boo" })
			{
				ServerPlayer? c = NascerNpc(molde, cell.Zone, aqui + new Vec2(-8 * T, 0), lugar++);
				if (c != null) extras.Add(c);
				if (c == null || !TcelAsVezesDe(c.Ficha, g) || !PericiaNoMaximo(c.Ficha, out _)) errados.Add(molde);
			}
			AfirmarTcel("**...E TODO CHEFE DE SAGA**: o Freeza de Vegeta e o de Namek, os dois androides e o Boo nascem com 1,25x e a pericia no maximo",
						errados.Count == 0, errados.Count == 0 ? "os cinco" : $"fora: {string.Join(", ", errados)}");

			// ---- O CONTRA-EXEMPLO: o Cell Perfeito com a ficha de recem-criado (o jogo de antes)
			ChefeSemODobroDeTeste = true;
			(double Dano, double NoTronco, double Embate)? antes = null;
			bool dobroDeAntes = true, periciaDeAntes = true;
			try
			{
				if (TcelCellDaSagaSemTorneio(arena, todos, degrau: 2) is { } velho)
				{
					antes = TcelORaioDoGoku(g, velho.Cell);
					dobroDeAntes = TcelAsVezesDe(velho.Cell.Ficha, g);
					periciaDeAntes = PericiaNoMaximo(velho.Cell.Ficha, out _);
				}
			}
			finally { ChefeSemODobroDeTeste = false; }
			// (2026-10-27, o pedido 138) a regua do raio com 1,25x: o chefe de agora contra a ficha de recem-criado, pelo mesmo raio
			AfirmarTcel("**...E O CHEFE DE 1,25x AGUENTA O RAIO BEM MAIS QUE A FICHA DE RECEM-CRIADO** (o pedido 138): em doze segundos no tronco, o mesmo "
						+ "raio tira dela mais de tres vezes o que tira dele",
						antes is { } r0 && r0.NoTronco > 3 * raio.NoTronco,
						antes is { } r1 ? $"chefe de agora {raio.NoTronco:0.#} | recem-criado {r1.NoTronco:0.#}" : "o Cell de antes nao nasceu");
			AfirmarTcel("   CONTRA-EXEMPLO (a ficha de recem-criado, o jogo de antes): sem as vezes nem a pericia, o raio o derrubaria em doze segundos e o "
						+ "embate seria do Goku -- as reguas de cima reprovariam",
						antes is { } a && !dobroDeAntes && !periciaDeAntes && a.NoTronco >= 80 && a.Embate < 1,
						antes is { } b ? $"{b.Dano:0.##} por batida; 12 s no tronco {b.NoTronco:0.#}; embate {b.Embate:0.##}" : "o Cell de antes nao nasceu");
		}
		finally
		{
			ChefeSemODobroDeTeste = false;
			foreach (ServerPlayer x in extras) if (_players.ContainsKey(x.Id)) RemoverNpc(x);
			foreach (var s in guardado)
			{
				(s.P.Ficha.BP, s.P.Ficha.physoff, s.P.Ficha.kioff, s.P.Ficha.kidef, s.P.Ficha.beamskill) = (s.Bp, s.Po, s.Ko, s.Kd, s.Raio);
				s.P.Ficha.Statify();
				s.P.Ficha.PowerLevel();
			}
		}
	}

	/// <summary>
	/// O RAIO DE POTENCIA 2 DE QUEM ATIRA (o do dono) num chefe, pela conta do servidor: o dano por batida (`DanoDeKi.Final`), o que doze segundos
	/// dele mirados no tronco tiram (60 batidas, 53% no tronco -- o `Body.Sortear` com a mira), e o embate: o poder da Onda de Ki do chefe (a
	/// potencia 1, o raio mais fraco que ele usa) sobre o desse raio (`EmbateDeKi.PoderDoFeixe`).
	/// </summary>
	private static (double Dano, double NoTronco, double Embate) TcelORaioDoGoku(Fighter quem, ServerPlayer chefe)
	{
		double mods = Projetil.ModsDoTiro(quem, TipoDeProjetil.Beam) * 2;
		double dano = DanoDeKi.Final(mods, 2, 0, quem.expressedBP, chefe.Combate!, bloqueando: false);
		double doChefe = EmbateDeKi.PoderDoFeixe(chefe.Ficha.expressedBP, Projetil.ModsDoTiro(chefe.Ficha, TipoDeProjetil.Beam), 1);
		return (dano, dano * 60 * 0.533, doChefe / EmbateDeKi.PoderDoFeixe(quem.expressedBP, mods, 2));
	}

	/// <summary>Os oito atributos da aba Stats (o `R` de cada um), na ordem do `Fighter.FixarAtributos`.</summary>
	private static double[] TcelOsOito(Fighter f) => [f.Rphysoff, f.Rphysdef, f.Rtechnique, f.Rkioff, f.Rkidef, f.Rkiskill, f.Rspeed, f.Rmagiskill];

	/// <summary>
	/// AS VEZES DO PEDIDO 138 (*"25% acima do player mais forte e nao 2x"*; era o dobro desde 2026-10-20). A regua le ESTE numero, e nao o
	/// `VezesOMaisForte` do servidor: a contraprova por mutacao muda aquele, e uma regua que lesse a constante mutada passaria junto.
	/// </summary>
	private const double TcelVezesDoPedido = 1.25;

	/// <summary>
	/// Este chefe tem AS VEZES DO PEDIDO de cada um dos oito atributos deste jogador? (1% de folga) -- ou, num atributo, MAIS que isso com o
	/// cru no piso de 0,1 do `Fighter.CruPara`: o bonus proprio da ficha dele (buff, forma, raca) ja passa das vezes, e o cru nao desce mais.
	/// Com o dobro isso nao aparecia; com 1,25x aparece no Bills, na 18 e no Boo (a r1 do pedido 138).
	/// </summary>
	private static bool TcelAsVezesDe(Fighter chefe, Fighter jogador) =>
		TcelOsOito(chefe).Zip(TcelOsOito(jogador)).Zip(TcelOsCrus(chefe)).All(t =>
			Math.Abs(t.First.First - TcelVezesDoPedido * t.First.Second) <= 0.01 * TcelVezesDoPedido * t.First.Second
			|| (t.First.First > TcelVezesDoPedido * t.First.Second && t.Second <= 0.1 + 1e-9));

	/// <summary>Os oito CRUS, na ordem de <see cref="TcelOsOito"/> -- o piso do `Fighter.CruPara` e 0,1.</summary>
	private static double[] TcelOsCrus(Fighter f) => [f.physoff, f.physdef, f.technique, f.kioff, f.kidef, f.kiskill, f.speed, f.magiskill];

	/// <summary>Os oito como a aba Stats os escreve (vezes 10).</summary>
	private static string TcelOito(Fighter f) => string.Join(" ", TcelOsOito(f).Select(x => $"{x * 10:0.#}"));

	/// <summary>As dezessete pericias de luta, com o nome que a bancada escreve -- a mesma lista do `Fighter.PericiasNoMaximo`.</summary>
	private static readonly (string Nome, Func<Fighter, double> Ler)[] TcelPericias =
	[
		("debuff", f => f.kidebuffskill), ("defesa de ki", f => f.kidefenseskill), ("eficiencia", f => f.kiefficiencyskill),
		("raio", f => f.beamskill), ("efusao", f => f.kieffusionskill), ("bola", f => f.blastskill), ("rajada", f => f.volleyskill),
		("alvo", f => f.targetedskill), ("teleguiado", f => f.guidedskill), ("perseguicao", f => f.homingskill), ("carga", f => f.chargedskill),
		("kiai", f => f.kiaiskill), ("buff", f => f.kibuffskill), ("leitura", f => f.kiawarenessskill), ("circulacao", f => f.kicirculationskill),
		("coleta", f => f.kigatheringskill), ("controle", f => f.kicontrolskill),
	];

	/// <summary>
	/// (2026-10-20) O CELL DA SAGA NA TERRA, SEM TORNEIO -- a luta livre: as sagas limpas, a do Cell em curso com ele chegando hoje NO DEGRAU
	/// pedido (o nascimento de producao, `NascerChefeDaSaga`; no ultimo, voltando da morte, que e como o Super Perfeito chega), no meio da arena,
	/// e os quatro fora dela e INTOCAVEIS -- o raio atravessa quem esta intocavel, e a bancada precisa deles de pe. Nulo = nao nasceu.
	/// </summary>
	private (ServerPlayer Cell, EstadoDoElo Est, EstadoDoChefe Ec)? TcelCellDaSagaSemTorneio(Arena arena, ServerPlayer[] todos, int degrau)
	{
		var terra = ZoneKey.Premade("Earth");
		if (MorteDaZona(terra) is { Fase: FaseDaMorte.Explodindo }) AbortarMorte(terra, "bancada do torneio do Cell");
		SagasLimpas();
		TickDoTorneioDoCell(1.0);   // (o Kamehameha e o torneio de antes, se ficaram, fecham: o corpo deles saiu com as sagas)
		foreach (string saga in new[] { "freeza_vegeta", "freeza_namek" }) _sagas.Do(saga)!.Fase = FaseDaSaga.Vencida;
		EloDaSaga elo = Array.Find(_cadeia, e => e.Id == "cell")!;
		EstadoDoElo est = _sagas.Do("cell")!;
		est.Disparou = true;
		est.Fase = FaseDaSaga.EmCurso;
		MoldeDeNpc molde = _moldes!.Get("cell")!;
		var ec = new EstadoDoChefe
		{
			Molde = "cell", Fase = FaseDoChefe.ACaminho, DiasRestantes = 0, Degrau = degrau, Bps = Sagas.Pinar(molde, MediaDoServidor()),
		};
		est.Chefes.Add(ec);
		NascerChefeDaSaga(elo, est, ec, degrau == molde.Estagios.Length - 1 ? ComoChegou.Ressurgindo : ComoChegou.Agora);
		for (int i = 0; i < todos.Length; i++)
		{
			Transportar(todos[i], terra, ForaDaArenaDeTeste(arena, i));
			todos[i].Combate.Carencia = CarenciaDeEspera;
		}
		if (!_players.TryGetValue(ec.Corpo, out ServerPlayer? cell)) return null;
		Transportar(cell, terra, arena.Centro);
		return (cell, est, ec);
	}

	/// <summary>O tronco do corpo a esta fracao da vida dele -- o golpe que o deixou assim, sem o golpe.</summary>
	private static void TcelTroncoA(ServerPlayer c, double fracao)
	{
		foreach (BodyPart p in c.Combate.Corpo.Partes)
			if (p.Nome == Body.NomeDoTronco) p.Vida = p.VidaMax * fracao;
		c.Combate.SincronizarVida();
	}

	/// <summary>
	/// O TORNEIO DE NOVO: as sagas limpas, a do Cell em curso com ele chegando hoje (o nascimento de producao o poe na arena), e os quatro fora
	/// dela e INTOCAVEIS -- o raio do Kamehameha atravessa quem esta intocavel (`Colidiu`), e a bancada precisa deles de pe ate o fim.
	/// </summary>
	private (ServerPlayer Cell, TorneioDoCellVivo T, EstadoDoElo Est)? TcelTorneioDeNovo(Arena arena, ServerPlayer[] todos)
	{
		var terra = ZoneKey.Premade("Earth");
		// (a Terra que uma cena anterior deixou explodindo -- a de uma contraprova -- volta antes: cada cena comeca com ela inteira)
		if (MorteDaZona(terra) is { Fase: FaseDaMorte.Explodindo }) AbortarMorte(terra, "bancada do torneio do Cell");
		SagasLimpas();
		TickDoTorneioDoCell(1.0);   // (o torneio de antes, se ficou, fecha: o corpo dele saiu com as sagas)
		foreach (string saga in new[] { "freeza_vegeta", "freeza_namek" }) _sagas.Do(saga)!.Fase = FaseDaSaga.Vencida;
		EstadoDoElo est = _sagas.Do("cell")!;
		est.Disparou = true;
		est.Fase = FaseDaSaga.EmCurso;
		var ec = new EstadoDoChefe
		{
			Molde = "cell", Fase = FaseDoChefe.ACaminho, DiasRestantes = 0, Degrau = 2, NoTorneio = true,
			Bps = Sagas.Pinar(_moldes!.Get("cell")!, MediaDoServidor()),
		};
		est.Chefes.Add(ec);
		for (int i = 0; i < todos.Length; i++)
		{
			Transportar(todos[i], terra, ForaDaArenaDeTeste(arena, i));
			todos[i].Combate.Carencia = CarenciaDeEspera;
		}
		VirouODia(soAsChegadasDeHoje: true);
		if (!_players.TryGetValue(ec.Corpo, out ServerPlayer? cell) || _torneioDoCell is not { } t) return null;
		return (cell, t, est);
	}

	/// <summary>
	/// GOHAN CONTRA O KAMEHAMEHA: o torneio de novo e a fila dada como vazia (o Kamehameha sai pelo tique de producao), e Gohan entra na frente
	/// dele (`TcelGohanEntraNaFrente`). Nulo = a disputa nao abriu.
	/// </summary>
	private (ServerPlayer Cell, TorneioDoCellVivo T, EstadoDoElo Est, DisputaDeKi D)? TcelGohanContraOKamehameha(Arena arena, ServerPlayer[] todos,
		ServerPlayer gohan, double vezesOBpDoCell)
	{
		if (TcelTorneioDeNovo(arena, todos) is not { } m) return null;
		(ServerPlayer cell, TorneioDoCellVivo t, EstadoDoElo est) = m;
		t.FilaVazia = TorneioDoCell.SegundosDeFilaVazia;
		TickDoTorneioDoCell(Protocol.TickSeconds);
		if (t.Fase != FaseDoTorneioDoCell.Kamehameha || TcelGohanEntraNaFrente(cell, gohan, vezesOBpDoCell) is not { } d) return null;
		return (cell, t, est, d);
	}

	/// <summary>
	/// GOHAN ENTRA NA FRENTE do Kamehameha que o Cell carrega: a <see cref="TilesDoEmbate"/> tiles ao sul dele, de frente pra ele, com o BP pedido
	/// (vezes o do Cell) e o Kamehameha DELE -- o verb da tecnica --, que sai no mesmo tique que o do Cell. O laco do jogo roda ate a disputa
	/// abrir. (2026-10-20) Separado do torneio pra servir tambem ao Super Perfeito perdendo (familia L), que nao tem um. Nulo = nao abriu.
	/// </summary>
	private DisputaDeKi? TcelGohanEntraNaFrente(ServerPlayer cell, ServerPlayer gohan, double vezesOBpDoCell)
	{
		if (!_canais.TryGetValue(cell.Id, out CanalDeKi? doCell)) return null;
		Transportar(gohan, cell.Zone, cell.Pos + new Vec2(0, TilesDoEmbate * ZoneCollision.TileSize));
		cell.Facing = Facing.South;   // (a mira de producao faz o mesmo a cada tique da carga; a contraprova do olhar e a da familia H)
		if (gohan.Voando) AlternarVoo(gohan);
		gohan.Facing = Facing.North;
		gohan.AlvoId = cell.Id;
		gohan.Ficha.BP = cell.Ficha.BP * vezesOBpDoCell;
		gohan.Ficha.Statify();
		gohan.Ficha.PowerLevel();
		gohan.Ficha.Ki = gohan.Ficha.MaxKi;
		// (o canal da disputa anterior, se ele venceu, ainda esta de pe -- e o verb, com um canal do mesmo nome aberto, o FECHA em vez de abrir)
		if (_canais.TryGetValue(gohan.Id, out CanalDeKi? velho)) FecharCanal(gohan.Id, velho, null);
		RaioNomeadoG6(gohan, "Kamehameha");
		if (!_canais.TryGetValue(gohan.Id, out CanalDeKi? doGohan)) return null;
		doGohan.CargaRestante = doCell.CargaRestante;   // os dois saem juntos

		for (int i = 0; i < 30 * 10; i++)
		{
			TcelUmTiqueDoJogo();
			if (_emEmbateDeKi.TryGetValue(cell.Id, out DisputaDeKi? d)) return d;
		}
		return null;
	}

	/// <summary>Gohan aperta cada letra da disputa ate ela acabar -- o jogador perfeito da `--embatekiteste`. Venceu = o canal dele de pe e o do Cell nao.</summary>
	private bool TcelGohanApertaAteOFim(ServerPlayer gohan, ServerPlayer cell)
	{
		for (int i = 0; i < 30 * 40 && _emEmbateDeKi.TryGetValue(gohan.Id, out DisputaDeKi? d); i++)
		{
			LadoDeKi meu = d.A.Quem == gohan ? d.A : d.B;
			if (meu.Letra != '\0') TeclaDeQualquerEmbate(gohan, meu.Letra);
			TcelUmTiqueDoJogo();
		}
		return !_emEmbateDeKi.ContainsKey(gohan.Id) && _canais.ContainsKey(gohan.Id) && !_canais.ContainsKey(cell.Id);
	}

	/// <summary>Um tique do jogo pro Kamehameha: os canais, os tiros, os embates e o torneio.</summary>
	private void TcelUmTiqueDoJogo()
	{
		TickDosCanaisDeKi(Protocol.TickSeconds);
		TickDosProjeteis(Protocol.TickSeconds);
		TickDosEmbatesDeKi(Protocol.TickSeconds);
		TickDoTorneioDoCell(Protocol.TickSeconds);
	}
}
