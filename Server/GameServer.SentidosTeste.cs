using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--sentidosteste` -- A ABA SENSE/SCAN PELO FUNIL DO SERVIDOR.
///
/// ============================ O QUE ELA MEDE, E POR QUE CADA PARTE ============================
///   1. OS TRES ALCANCES do Sense (`Sense2.0.dm:20-35`), e quem fica de fora de cada um: os 15 tiles, o
///      escondido, o Android, o piso de 5 de BP, o NPC (fora do "neste mundo"), o piso de 5 milhoes da
///      galaxia; o nome so de quem se conhece; o poder RELATIVO (e nunca o absoluto); o rumo do `get_dir`;
///   2. O SCOUTER (Scan): a area inteira com BP EXATO e coordenadas, NPC so se for chefe, e o scouter
///      vencendo a skill quando os dois existem;
///   3. O SIGILO NO FIO: o pacote aberto com o LEITOR DO CLIENTE -- no Sense todo BP e NaN, no Scan e
///      o numero; nenhum byte sobrando. E a familia que a INJECAO tem que derrubar (um BP absoluto
///      escrito no ramo do Sense de `GameServer.Sentidos.cs` a deixa vermelha);
///   4. O REENVIO: a 1 Hz, so pra quem sente, e so quando a lista muda.
///
/// CORPOS FORJADOS, sem cliente, como as bancadas vizinhas (`ForjarComSkills`, `CorredorLivre`); cada um
/// com a PROPRIA conta, porque "conhecido" e por assinatura e a forja de projetil da a mesma conta a todos.
/// Roda no boot, sem janela, e o servidor CONTINUA DE PE depois dela (como a `--menteskills`): quem o
/// derruba e o `testar-os-sentidos.bat`, que vigia o log ate o placar aparecer e mata SO o PID que subiu.
/// (Um `GetTree().Quit()` daqui de dentro do boot nao fechou o processo na primeira rodada -- o boot
/// segue, povoa o mundo e abre a porta -- entao nao ha "fechar sozinha": ha o placar, e o .bat.)
/// ==============================================================================================
///
///     Godot --headless --path . --host --rede 7996 --sentidosteste
/// </summary>
public partial class GameServer
{
	private int _snOk, _snFalhou;

	private void AfirmarSn(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _snOk++; GD.Print($"[sentidos]   OK    {oque}"); return; }
		_snFalhou++;
		GD.PrintErr($"[sentidos]   FALHA {oque}   {detalhe}");
	}

	private const string StSense = "/datum/skill/sense";
	private const string StBasicAwareness = "/datum/skill/mind/Basic_Ki_Awareness";

	public void RodarBancadaDosSentidos()
	{
		_snOk = _snFalhou = 0;
		GD.Print("[sentidos] ================ A ABA SENSE/SCAN: ALCANCES, IDENTIDADE, SIGILO E REENVIO ================");
		if (_skills == null)
		{
			AfirmarSn("o catalogo de skills carregou", false);
			GD.Print($"[sentidos] ================ {_snOk} passaram, {_snFalhou} falharam ================");
			return;
		}

		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		List<(int, NetDataWriter)>? escutaAnterior = EscutaDeSentidos;
		try
		{
			OsAlcancesDoSense();
			LimparTudoDaBancada();   // o Scan le a zona INTEIRA: os corpos da familia anterior entrariam nele
			OScouterLeExato();
			LimparTudoDaBancada();
			OSigiloNoFio();
			LimparTudoDaBancada();
			OReenvioSoNaMudanca();
		}
		catch (Exception e)
		{
			_snFalhou++;
			GD.PrintErr($"[sentidos]   FALHA a bancada rodou inteira   {e}");
		}
		finally
		{
			EscutaDeSentidos = escutaAnterior;
			LimparTudoDaBancada();
		}

		// O PLACAR E A ULTIMA LINHA, e e ela que o `.bat` espera: a partir daqui o processo e dele.
		GD.Print($"[sentidos] ================ {_snOk} passaram, {_snFalhou} falharam ================");
	}

	// =====================================================================
	// OS CORPOS
	// =====================================================================
	/// <summary>
	/// Um corpo com identidade PROPRIA: o `Forjar` de projetil da a conta "bancada_projetil" a todos, e
	/// como a assinatura sai de conta+slot, todos seriam a MESMA pessoa pro convivio -- conhecer um seria
	/// conhecer todos. `sense` poe a skill no livro e acende o bit, como a compra e o login fazem.
	/// </summary>
	private ServerPlayer StCorpo(string nome, Vec2 onde, double bp, bool sense = false)
	{
		ServerPlayer pl = ForjarComSkills(nome, onde, bp, skills: sense ? [StSense] : null);
		pl.Conta = "bancada_sentidos_" + nome.ToLowerInvariant().Replace(' ', '_');
		if (sense) AplicarPoderes(pl);
		return pl;
	}

	/// <summary>O mesmo corpo, posto NOUTRA zona (a galaxia): sai da lista da Terra e entra na do lugar pedido.</summary>
	private ServerPlayer StCorpoEm(string nome, string zona, double bp)
	{
		ServerPlayer pl = StCorpo(nome, new Vec2(ZoneCollision.TileSize * 10 + 16, ZoneCollision.TileSize * 10 + 16), bp);
		ZoneList(pl.Zone.Hash).Remove(pl);
		pl.Zone = ZoneKey.Premade(zona);
		ZoneList(pl.Zone.Hash).Add(pl);
		return pl;
	}

	private static Vec2 Em(Vec2 c, int tilesX, int tilesY) =>
		c + new Vec2(tilesX * ZoneCollision.TileSize, tilesY * ZoneCollision.TileSize);

	/// <summary>Acha a presenca de um corpo na lista: pela assinatura (desconhecido) ou pelo nome (conhecido/Scan).</summary>
	private static Protocol.PresencaState? Acha(List<Protocol.PresencaState> lista, ServerPlayer pl)
	{
		foreach (Protocol.PresencaState p in lista)
		{
			if (p.Assinatura.Length > 0 && p.Assinatura == pl.Assinatura) return p;
			if (p.Nome.Length > 0 && p.Nome == pl.Name) return p;
		}
		return null;
	}

	private static string Nomes(List<Protocol.PresencaState> lista) =>
		string.Join(", ", lista.Select(p => (p.Nome.Length > 0 ? p.Nome : "???" + (p.Assinatura.Length > 0 ? $"({p.Assinatura})" : ""))
										   + $"[a{p.Alcance} d{p.Distancia} r{Protocol.NomeDoRumo(p.Rumo)} {p.PoderRelativo}% bp={p.Bp}]"));

	// =====================================================================
	// 1) OS TRES ALCANCES
	// =====================================================================
	private void OsAlcancesDoSense()
	{
		GD.Print("[sentidos] -- 1) OS TRES ALCANCES DO SENSE, e quem fica de fora de cada um");
		Vec2 c = CorredorLivre(28);
		ServerPlayer eu = StCorpo("Vidente", c, 10_000, sense: true);
		ServerPlayer perto = StCorpo("Perto", Em(c, 5, 0), 8_500);
		ServerPlayer longe = StCorpo("Longe", Em(c, 20, 0), 12_000);
		ServerPlayer escondido = StCorpo("Escondido", Em(c, 2, 0), 9_000);
		escondido.Ficha.isconcealed = true;
		ServerPlayer maquina = StCorpo("Maquina", Em(c, 3, 0), 9_000);
		maquina.Race = "Android";
		ServerPlayer fraco = StCorpo("Fraco", Em(c, 4, 0), 1);
		ServerPlayer amigo = StCorpo("Amigo", Em(c, 6, 0), 9_500);
		ServerPlayer diagonal = StCorpo("Diagonal", Em(c, -3, -3), 7_000);
		ServerPlayer tita = StCorpoEm("Tita", "Namek", 6_000_000);
		ServerPlayer forte = StCorpoEm("Forte", "Namek", 100_000);
		eu.Social.Fotografar(amigo.Assinatura, amigo.Name, amigo.Race, "Normal", "Male", 25, NowMs());

		AfirmarSn("preparo: o Vidente tem `Poder.Sense` (a skill no livro acende o bit) e nao tem scouter",
				  TemSense(eu) && !TemScouter(eu));
		AfirmarSn("preparo: o Fraco expressa 5 de BP ou menos (o piso do DM)",
				  fraco.Ficha.expressedBP <= BpMinimoSentivel, $"{fraco.Ficha.expressedBP:0.##}");
		AfirmarSn("preparo: o Tita expressa mais de 5 milhoes e o Forte nao",
				  tita.Ficha.expressedBP > BpDaGalaxia && forte.Ficha.expressedBP <= BpDaGalaxia,
				  $"{tita.Ficha.expressedBP:0} / {forte.Ficha.expressedBP:0}");
		AfirmarSn("preparo: cada corpo tem a propria assinatura, e o Vidente conhece SO o Amigo",
				  amigo.Assinatura != perto.Assinatura && eu.Social.Conhece(amigo.Assinatura) && !eu.Social.Conhece(perto.Assinatura));

		// ---- ALCANCE 1: kiawarenessskill abaixo de 20 ----
		(bool scan, List<Protocol.PresencaState> l1) = SentidosDe(eu);
		AfirmarSn("alcance 1 (kiawarenessskill < 20): o modo e Sense, nao Scan", !scan);
		Protocol.PresencaState? pp = Acha(l1, perto);
		AfirmarSn("...o Perto (5 tiles) aparece, com alcance 'perto'", pp is { Alcance: AlcancePerto }, Nomes(l1));
		AfirmarSn("...o Longe (20 tiles) NAO aparece: 15 e o limite (HtmlUI.dm:371)", Acha(l1, longe) == null, Nomes(l1));
		AfirmarSn("...o Escondido (isconcealed) NAO aparece (HtmlUI.dm:369)", Acha(l1, escondido) == null);
		AfirmarSn("...o Android NAO aparece (HtmlUI.dm:369)", Acha(l1, maquina) == null);
		AfirmarSn("...quem expressa <= 5 de BP NAO aparece (HtmlUI.dm:370)", Acha(l1, fraco) == null);
		AfirmarSn("...o Tita de outro planeta NAO aparece no alcance 1", Acha(l1, tita) == null);
		AfirmarSn("o desconhecido vem SEM nome e com a assinatura -- o \"??? (assinatura)\" do HtmlUI.dm:374",
				  pp is { Nome: "" } d && d.Assinatura == perto.Assinatura && d.Assinatura.Length == 10, $"{pp?.Nome}/{pp?.Assinatura}");
		AfirmarSn("o Amigo (fotografado no convivio) vem COM nome e sem assinatura",
				  Acha(l1, amigo) is { Nome: "Amigo", Assinatura: "" }, Nomes(l1));
		float esperado = (float)Math.Round(perto.Ficha.expressedBP / Math.Max(eu.Ficha.expressedBP, 1) * 100, MidpointRounding.AwayFromZero);
		AfirmarSn($"o poder e RELATIVO ao meu ({esperado}%), inteiro como o `round(x,1)` do DM",
				  pp is { } p1 && Math.Abs(p1.PoderRelativo - esperado) < 1e-6, $"{pp?.PoderRelativo}");
		AfirmarSn("...e nao e o BP absoluto do outro",
				  pp is { } p2 && Math.Abs(p2.PoderRelativo - perto.Ficha.expressedBP) > 1, $"{pp?.PoderRelativo} vs {perto.Ficha.expressedBP:0}");
		AfirmarSn("o BP absoluto vai como NaN (SemLeitura) em TODA presenca do modo Sense", l1.Count > 0 && l1.All(p => double.IsNaN(p.Bp)));
		// (a vida deixou de viajar em 2026-09-04, a pedido do dono -- o registro nem tem o campo; ver `Presenca`)
		AfirmarSn("a distancia e o `get_dist` (5 tiles) e o rumo e E", pp is { Distancia: 5, Rumo: 3 }, $"d{pp?.Distancia} r{pp?.Rumo}");
		Protocol.PresencaState? pd = Acha(l1, diagonal);
		AfirmarSn("o Diagonal (3 tiles a noroeste) vem com rumo NW e distancia 3 -- o `get_dir` junta os dois eixos, o `get_dist` e o maior deles",
				  pd is { Rumo: 8, Distancia: 3 }, $"d{pd?.Distancia} r{pd?.Rumo}");
		int iD = l1.FindIndex(p => p.Assinatura == diagonal.Assinatura), iP = l1.FindIndex(p => p.Assinatura == perto.Assinatura), iA = l1.FindIndex(p => p.Nome == "Amigo");
		AfirmarSn("a lista vem em ordem de distancia: Diagonal (3), Perto (5), Amigo (6)", iD >= 0 && iD < iP && iP < iA, Nomes(l1));

		// ---- ALCANCE 2: Basic Ki Awareness no 100 escreve kiawarenessskill 20 -- pelo motor de niveis ----
		eu.Livro.Dar(StBasicAwareness);
		eu.Niveis.Por(StBasicAwareness, 100);
		MnLogin(eu);
		AfirmarSn("preparo: Basic Ki Awareness no nivel 100 escreve kiawarenessskill 20 (o motor de niveis, como a --arvoreteste prova)",
				  Math.Abs(eu.Ficha.kiawarenessskill - 20) < 1e-9, $"{eu.Ficha.kiawarenessskill}");
		(_, List<Protocol.PresencaState> l2) = SentidosDe(eu);
		Protocol.PresencaState? pl2 = Acha(l2, longe);
		AfirmarSn("alcance 2 (kiawarenessskill >= 20, Sense2.0.dm:27): o Longe aparece, 'neste mundo', 20 tiles a E",
				  pl2 is { Alcance: AlcanceMundo, Distancia: 20, Rumo: 3 }, Nomes(l2));
		AfirmarSn("...o Perto continua 'perto' (o mais proximo vence, como o `shown |= D` do DM)", Acha(l2, perto) is { Alcance: AlcancePerto });
		AfirmarSn("...Escondido, Android e Fraco continuam fora; o Tita de outro planeta tambem",
				  Acha(l2, escondido) == null && Acha(l2, maquina) == null && Acha(l2, fraco) == null && Acha(l2, tita) == null, Nomes(l2));

		MoldeDeNpc? moldeComum = _moldes?.Todos.FirstOrDefault(m => !m.EhChefe);
		if (moldeComum != null)
		{
			// NPC DO MUNDO: tem papel e NAO tem conta (a assinatura sai de conta+slot, e cidadao nasce sem
			// conta -- ver o cabecalho de `GameServer.Convivio.cs`). A forja daqui da conta a todos; tira-se.
			ServerPlayer cidadao = StCorpo("Cidadao", Em(c, 18, 0), 9_000);
			cidadao.Papel = new PapelDeNpc(moldeComum, 7);
			cidadao.Conta = "";
			ServerPlayer vizinho = StCorpo("Vizinho", Em(c, 7, 0), 9_000);
			vizinho.Papel = new PapelDeNpc(moldeComum, 8);
			vizinho.Conta = "";
			(_, List<Protocol.PresencaState> l2b) = SentidosDe(eu);
			// NPC nao tem assinatura: acha-se pela distancia
			AfirmarSn("...um NPC do mundo a 18 tiles NAO entra no 'neste mundo' (o DM varre `player_list`, so jogador)",
					  !l2b.Any(p => p.Distancia == 18), Nomes(l2b));
			AfirmarSn("...mas um NPC a 7 tiles ENTRA no 'perto' (o DM varre `current_area.contents`, todo mob), sem nome nem assinatura",
					  l2b.Any(p => p.Distancia == 7 && p.Alcance == AlcancePerto && p.Nome == "" && p.Assinatura == ""), Nomes(l2b));
		}
		else GD.Print("[sentidos]   --    (sem catalogo de NPCs: a peneira de NPC do alcance 2 nao foi medida)");

		// ---- ALCANCE 3: o contador no 60. Como ele chega la e assunto da arvore da Mente (--menteskills) ----
		eu.Ficha.kiawarenessskill = PericiaDaGalaxia;
		(_, List<Protocol.PresencaState> l3) = SentidosDe(eu);
		Protocol.PresencaState? pt = Acha(l3, tita);
		AfirmarSn("alcance 3 (kiawarenessskill >= 60, Sense2.0.dm:32): o Tita de Namek aparece 'na galaxia', com o lugar e sem coordenada nem distancia",
				  pt is { Alcance: AlcanceGalaxia, Zona: "Namek", X: -1, Y: -1, Distancia: Protocol.DistanciaDesconhecida }, Nomes(l3));
		AfirmarSn("...e o Forte (100 mil) de Namek NAO: 5 milhoes e o piso da galaxia (HtmlUI.dm:389)", Acha(l3, forte) == null, Nomes(l3));
		float esperadoTita = (float)Math.Round(tita.Ficha.BP / Math.Max(eu.Ficha.BP, 1) * 100, MidpointRounding.AwayFromZero);
		AfirmarSn("...o poder do Tita e a razao dos BPs BASE (`D.BP/max(BP,1)`, :393), nao dos expressos",
				  pt is { } t && Math.Abs(t.PoderRelativo - esperadoTita) < 1e-6, $"{pt?.PoderRelativo} vs {esperadoTita}");
		AfirmarSn("...e o BP absoluto continua NaN nos tres alcances", l3.Count >= 3 && l3.All(p => double.IsNaN(p.Bp)));

		// ---- CONTRA-EXEMPLO: sem a skill nao ha lista ----
		ServerPlayer cego = StCorpo("Cego", Em(c, 8, 0), 10_000);
		(bool scanCego, List<Protocol.PresencaState> lc) = SentidosDe(cego);
		AfirmarSn("CONTRA-EXEMPLO: um corpo sem a skill (e sem scouter) nao sente ninguem -- lista vazia, modo Sense",
				  !scanCego && lc.Count == 0 && !TemSense(cego), $"{lc.Count}");
	}

	// =====================================================================
	// 2) O SCOUTER
	// =====================================================================
	private void OScouterLeExato()
	{
		GD.Print("[sentidos] -- 2) O SCOUTER (Scan): BP exato, coordenadas, so chefe entre os NPCs");
		Vec2 c = CorredorLivre(28);
		ServerPlayer eu = StCorpo("Olho", c, 10_000);   // SEM a skill: scouter e aparelho
		ServerPlayer perto = StCorpo("Perto2", Em(c, 4, 0), 8_500);
		ServerPlayer longe = StCorpo("Longe2", Em(c, 22, 0), 12_000);
		ServerPlayer escondido = StCorpo("Escondido2", Em(c, 2, 0), 9_000);
		escondido.Ficha.isconcealed = true;
		eu.PoderesConcedidos |= Protocol.Poder.Scouter;
		AplicarPoderes(eu);
		AfirmarSn("preparo: o scouter ligado acende `Poder.Scouter` (TemScouter) sem skill nenhuma", TemScouter(eu) && !TemSense(eu));

		(bool scan, List<Protocol.PresencaState> l) = SentidosDe(eu);
		AfirmarSn("o modo e Scan", scan);
		Protocol.PresencaState? pp = Acha(l, perto);
		AfirmarSn("o Perto vem com o BP EXATO (`FullNum(round(expressedBP,1))`, HtmlUI.dm:414) e com nome mesmo sem conhecer",
				  pp is { Nome: "Perto2" } p && Math.Abs(p.Bp - Math.Round(perto.Ficha.expressedBP, MidpointRounding.AwayFromZero)) < 1e-9,
				  $"{pp?.Bp} vs {perto.Ficha.expressedBP:0}");
		AfirmarSn("...o Longe (22 tiles) TAMBEM: o scan e a area inteira, sem os 15 tiles", Acha(l, longe) is { Distancia: 22, Rumo: 3 }, Nomes(l));
		(int px, int py) = TileDe(perto.Pos);
		AfirmarSn("...com as coordenadas em tiles (`([E.x],[E.y])`)", pp is { } q && q.X == px && q.Y == py, $"({pp?.X},{pp?.Y}) vs ({px},{py})");
		AfirmarSn("...e sem poder relativo (NaN): o scouter le numero, nao sente",
				  pp is { } r && float.IsNaN(r.PoderRelativo));
		AfirmarSn("...o escondido APARECE no scan (o `ui_tab_scan` nao testa isconcealed -- HtmlUI.dm:407-414, reproduzido e anotado)",
				  Acha(l, escondido) != null, Nomes(l));

		MoldeDeNpc? moldeChefe = _moldes?.Todos.FirstOrDefault(m => m.EhChefe);
		MoldeDeNpc? moldeComum = _moldes?.Todos.FirstOrDefault(m => !m.EhChefe);
		if (moldeChefe != null && moldeComum != null)
		{
			ServerPlayer chefe = StCorpo("Chefe", Em(c, 10, 0), 50_000);
			chefe.Papel = new PapelDeNpc(moldeChefe, 1);
			chefe.Conta = "";   // NPC: papel e sem conta, como o do mundo
			ServerPlayer cidadao = StCorpo("Cidadao2", Em(c, 11, 0), 9_000);
			cidadao.Papel = new PapelDeNpc(moldeComum, 2);
			cidadao.Conta = "";
			(_, l) = SentidosDe(eu);
			AfirmarSn("o chefe NPC aparece no scan, marcado como CHEFE (o `isBoss` do DM)", Acha(l, chefe) is { Chefe: true }, Nomes(l));
			AfirmarSn("...e o NPC comum NAO (HtmlUI.dm:410)", Acha(l, cidadao) == null, Nomes(l));
		}
		else GD.Print("[sentidos]   --    (sem molde de chefe/cidadao no catalogo: a peneira de NPC do scan nao foi medida)");

		eu.Livro.Dar(StSense);
		AplicarPoderes(eu);
		AfirmarSn("com a skill E o scouter, o scouter vence (a aba Sense vira Scan, HtmlUI.dm:402-404)", SentidosDe(eu).Scan);

		eu.PoderesConcedidos &= ~Protocol.Poder.Scouter;
		AplicarPoderes(eu);
		(bool scanDepois, List<Protocol.PresencaState> ls) = SentidosDe(eu);
		AfirmarSn("CONTRA-EXEMPLO: desligado o scouter, a leitura volta a ser Sense e o BP volta a NaN",
				  !scanDepois && ls.Count > 0 && ls.All(p => double.IsNaN(p.Bp)), Nomes(ls));
	}

	// =====================================================================
	// 3) O SIGILO NO FIO
	// =====================================================================
	private void OSigiloNoFio()
	{
		GD.Print("[sentidos] -- 3) O SIGILO NO FIO: o pacote aberto com o leitor do cliente");
		Vec2 c = CorredorLivre(20);
		ServerPlayer eu = StCorpo("Leitor", c, 10_000, sense: true);
		ServerPlayer outro = StCorpo("Outro", Em(c, 3, 0), 20_000);

		(bool scan, List<Protocol.PresencaState> lista) = SentidosDe(eu);
		NetDataWriter w = PacoteDeSentidos(scan, lista);
		var r = new NetDataReader(w.CopyData());
		byte opcode = r.GetByte();
		(bool scanLido, List<Protocol.PresencaState> lido) = r.GetSentidos();
		AfirmarSn("o opcode e S2C.Sentidos e o modo lido e Sense", opcode == (byte)Protocol.S2C.Sentidos && !scanLido);
		AfirmarSn("o pacote acabou exatamente onde o leitor parou (nenhum byte sobrando ou faltando)", r.EndOfData, $"{r.AvailableBytes} sobrando");
		AfirmarSn("o que foi lido e o que foi escrito (mesma contagem; nome, assinatura, alcance, %, distancia, rumo iguais)",
				  lido.Count == lista.Count && lido.Count > 0 && lido.Zip(lista).All(par => Igual(par.First, par.Second)));
		AfirmarSn("SIGILO: no pacote de Sense NENHUM BP absoluto viaja -- todo `Bp` lido e NaN", lido.All(p => double.IsNaN(p.Bp)));
		Protocol.PresencaState? po = Acha(lido, outro);
		float esperado = (float)Math.Round(outro.Ficha.expressedBP / Math.Max(eu.Ficha.expressedBP, 1) * 100, MidpointRounding.AwayFromZero);
		AfirmarSn($"...o que viaja e a razao ({esperado}%), que nao e o numero do Outro ({outro.Ficha.expressedBP:0})",
				  po is { } p && Math.Abs(p.PoderRelativo - esperado) < 1e-6 && Math.Abs(p.PoderRelativo - outro.Ficha.expressedBP) > 1,
				  $"{po?.PoderRelativo}");

		// o mesmo leitor, no alcance da galaxia: continua sem numero
		eu.Ficha.kiawarenessskill = PericiaDaGalaxia;
		(scan, lista) = SentidosDe(eu);
		r = new NetDataReader(PacoteDeSentidos(scan, lista).CopyData());
		r.GetByte();
		(_, lido) = r.GetSentidos();
		AfirmarSn("...mesmo no alcance da galaxia, sem scouter: nada de numero absoluto no fio", lido.Count > 0 && lido.All(p => double.IsNaN(p.Bp)) && r.EndOfData);

		// o scouter: o numero vai
		eu.PoderesConcedidos |= Protocol.Poder.Scouter;
		AplicarPoderes(eu);
		(scan, lista) = SentidosDe(eu);
		r = new NetDataReader(PacoteDeSentidos(scan, lista).CopyData());
		r.GetByte();
		(scanLido, lido) = r.GetSentidos();
		po = Acha(lido, outro);
		AfirmarSn("no pacote de Scan o BP viaja EXATO (o scouter e a porta de leitura, GameServer.Sigilo)",
				  scanLido && po is { } s && Math.Abs(s.Bp - Math.Round(outro.Ficha.expressedBP, MidpointRounding.AwayFromZero)) < 1e-9 && r.EndOfData,
				  $"{po?.Bp} vs {outro.Ficha.expressedBP:0}");
	}

	private static bool Igual(Protocol.PresencaState a, Protocol.PresencaState b) =>
		a.Nome == b.Nome && a.Assinatura == b.Assinatura && a.Alcance == b.Alcance
		&& (a.PoderRelativo.Equals(b.PoderRelativo)) && a.Bp.Equals(b.Bp)
		&& a.Distancia == b.Distancia && a.Rumo == b.Rumo && a.X == b.X && a.Y == b.Y && a.Zona == b.Zona && a.Chefe == b.Chefe;

	// =====================================================================
	// 4) O REENVIO
	// =====================================================================
	private void OReenvioSoNaMudanca()
	{
		GD.Print("[sentidos] -- 4) O REENVIO: a 1 Hz, so pra quem sente, e so quando a lista muda");
		Vec2 c = CorredorLivre(20);
		ServerPlayer eu = StCorpo("Atento", c, 10_000, sense: true);
		ServerPlayer cego = StCorpo("Distraido", Em(c, 1, 0), 10_000);
		ServerPlayer alvo = StCorpo("Andarilho", Em(c, 4, 0), 9_000);

		EscutaDeSentidos = [];
		int Quantos(ServerPlayer p) => EscutaDeSentidos!.Count(e => e.Quem == p.Id);
		int UltimaContagem(ServerPlayer p)
		{
			NetDataWriter? w = EscutaDeSentidos!.LastOrDefault(e => e.Quem == p.Id).Pacote;
			if (w == null) return -1;
			var r = new NetDataReader(w.CopyData());
			r.GetByte();
			return r.GetSentidos().Lista.Count;
		}

		TickDosSentidos();
		// O DISTRAIDO TAMBEM E SENTIDO (esta a 1 tile e tem energia): a lista do Atento tem DOIS. E ele
		// que fica quando o Andarilho some -- a prova de que "esvaziar" nao e a unica mudanca que reenvia.
		AfirmarSn("o primeiro tique manda UM pacote a quem tem Sense, com os dois vizinhos dentro (o Distraido tambem e sentido)",
				  Quantos(eu) == 1 && UltimaContagem(eu) == 2, $"{Quantos(eu)} pacotes, {UltimaContagem(eu)} presencas");
		AfirmarSn("...e NENHUM a quem nao tem Sense nem scouter", Quantos(cego) == 0, $"{Quantos(cego)}");
		TickDosSentidos();
		TickDosSentidos();
		AfirmarSn("dois tiques sem nada mudar: nenhum pacote novo (a assinatura segura)", Quantos(eu) == 1, $"{Quantos(eu)}");

		alvo.Pos = Em(alvo.Pos, 1, 0);
		TickDosSentidos();
		AfirmarSn("o Andarilho anda um tile: a distancia muda e o pacote sai de novo (2)", Quantos(eu) == 2, $"{Quantos(eu)}");
		TickDosSentidos();
		AfirmarSn("...e parado de novo, nada (continua 2)", Quantos(eu) == 2, $"{Quantos(eu)}");

		alvo.Ficha.isconcealed = true;
		TickDosSentidos();
		AfirmarSn("o Andarilho se esconde: ele sai da lista (sobra o Distraido) e o pacote sai de novo (3) -- sumir tambem e mudanca",
				  Quantos(eu) == 3 && UltimaContagem(eu) == 1, $"{Quantos(eu)} pacotes, {UltimaContagem(eu)} presencas");

		eu.Livro.Esquecer(StSense);
		AplicarPoderes(eu);
		TickDosSentidos();
		AfirmarSn("sem a skill o tique nao manda nada (continua 3)", Quantos(eu) == 3 && !TemSense(eu), $"{Quantos(eu)}");

		eu.Livro.Dar(StSense);
		AplicarPoderes(eu);
		TickDosSentidos();
		AfirmarSn("de volta com a skill, a lista sai de novo (4) MESMO igual a de antes (so o Distraido): perder o sentido apaga a assinatura",
				  Quantos(eu) == 4 && UltimaContagem(eu) == 1, $"{Quantos(eu)} pacotes, {UltimaContagem(eu)} presencas");

		alvo.Ficha.isconcealed = false;
		TickDosSentidos();
		AfirmarSn("...e o Andarilho reaparecendo e mais um (5), com os dois dentro de novo",
				  Quantos(eu) == 5 && UltimaContagem(eu) == 2, $"{Quantos(eu)} pacotes, {UltimaContagem(eu)} presencas");
		EscutaDeSentidos = null;
	}
}
