using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Races;
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
///   G. NOCAUTEADO NO X1 ELE MORRE NA LUTA, e volta Super Perfeito NA ARENA, solto: a luta vira livre.
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
	private static readonly string[] SangueDoCellDoDono = ["Saiyan", "Namekian", "Icer"];
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
		TickDasSagas();
		ServerPlayer? volta = _players.TryGetValue(ec.Corpo, out ServerPlayer? v) ? v : null;
		AfirmarTcel("**E VOLTA SUPER PERFEITO, NA ARENA -- E A LUTA VIRA LIVRE** (*\"volta super perfeito e apartir dai a luta vira livre, todos vs "
				  + "cell\"*): 5 bilhoes, o torneio acabou, e ele esta solto, tocavel e letal",
				  volta?.Papel != null && volta.Id != idDoPerfeito && volta.Papel.Estagio == 3 && Math.Abs(volta.Ficha.BP - 5_000_000_000) < 1
				  && arena.ContemEm(volta.Pos) && _torneioDoCell == null && !ec.NoTorneio && !CellForaDaVez(volta) && !volta.Combate.Intocavel
				  && volta.Combate.Letal && EscutaDoTorneioDoCell.Any(f => f.Contains("SUPER PERFEITO", StringComparison.Ordinal)),
				  $"corpo {volta != null}; degrau {volta?.Papel?.Estagio + 1}; BP {volta?.Ficha.BP:N0}; torneio {_torneioDoCell != null}; {EscutaDoCell()}");
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
