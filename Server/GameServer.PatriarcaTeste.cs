using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Appearance;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--patriarcateste` -- O GRANDE PATRIARCA DE NAMEK (dono, 2026-10-19: *"adicione o patriarca em namek assim como tinha no
/// Finale-master"*): o `mob/npc/Citizen/Guru` do `PlanetPopulation.dm` (`:213-235`, a fabrica em `:444-452`, o povoamento em `:454-460`).
///
///     Godot --headless --server --port 8880 --patriarcateste
///
/// Um cliente cru entra pela porta (o menu da tecla E so e apontado a quem tem par), e cada familia mede pela porta de PRODUCAO, com o
/// defeito injetado que a mesma regua tem de REPROVAR:
///   M. O MOLDE E O PLANO -- o molde `patriarca` (habitante, Namekuseijin do Cla do Dragao, homem, Guru, 9 a 12 mil, `vagueia: false`) e
///      UMA linha do povoamento em Namek, perto do berco; o povo continua espalhado.
///   N. O NASCIMENTO -- o dreno da fila do povoamento o poe a ate 10 tiles do berco de Namek, em chao que serve; a manutencao nao poe um
///      segundo com ele de pe, e poe outro quando ele morre. Contra-exemplo: a linha sem o `pertoDoBerco` (`PertoDoBercoIgnoradoDeTeste`).
///   A. A FOLHA DELE -- o indice reservado, a folha `NamekOld - Guru Style`, sem roupa por cima e sem o tom do povo; e jogador nenhum nasce
///      com ela. Contra-exemplo: o Guru vestido como o povo (`PatriarcaComOCorpoDoPovoDeTeste`).
///   V. NAO SAI VAGANDO -- a vida diaria nao passeia nem aceita spar, e o embaralho do planeta vazio nao o leva. Contra-exemplos: a rotina
///      de todo habitante (`QuemNaoVagueiaPasseiaDeTeste`) e o embaralho que leva (`EmbaralhoLevaQuemNaoVagueiaDeTeste`).
///   P. O MENU E -- o `S2C.DivinoPerto` aponta o Guru a quem esta a um tile (e a ninguem a tres, nem com ele caido), sem poder no pacote;
///      e a tabela tem o botao. Contra-exemplo: o apontador cego (`PatriarcaSemMenuDeTeste`).
///   F. A CONVERSA -- longe ele pede pra chegar perto; o malvisto de Namek e mandado embora; o neutro desperta (o BP sobe, as falas do
///      DM); de novo, "ja foi despertado"; caido, ele nao ouve. Contra-exemplo: o Guru que nao pergunta a reputacao
///      (`PatriarcaIgnoraAReputacaoDeTeste`).
///   R. A REALEZA CUSTA MAIS -- matar o Guru (e o Rei e o Principe de Vegeta) custa -25 de reputacao com o povo dele, e o habitante comum
///      -10 (`REP_KILL_ROYAL`/`REP_KILL_CITIZEN`, PlanetReputation.dm:19-20 e :124); o karma e o mesmo pros dois. Contra-exemplo: a realeza
///      que morre como plebe (`RealezaComoPlebeDeTeste`).
///
/// A FOTO -- a folha desenhada, a dica "[E] Guru", o menu e o chat -- e do robo `--diagpatriarca`.
/// </summary>
public sealed partial class GameServer
{
	private const string ContaDaBancadaDoPatriarca = "bancada_patriarca_ag";

	/// <summary>A faixa de ids dos corpos forjados por esta bancada.</summary>
	private const int IdBaseDoPatriarcaDeTeste = 98_800;

	private bool _ptRodando, _ptJaRodou;
	private int _ptOk, _ptFalhou, _ptCorpos;

	/// <summary>Tudo o que a bancada pos no mundo (os Gurus, os vizinhos e os forjados) -- sai no `finally`.</summary>
	private readonly List<ServerPlayer> _ptNoMundo = [];

	/// <summary>As contas forjadas, pra sairem do livro-caixa da reputacao no fim.</summary>
	private readonly List<string> _ptContas = [];

	private void AfirmarPt(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ptOk++; GD.Print($"[patriarca]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _ptFalhou++; GD.PrintErr($"[patriarca]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoPatriarca(int porta)
	{
		if (_ptRodando || _ptJaRodou) return;
		_ptRodando = _ptJaRodou = true;
		_ptOk = _ptFalhou = 0;
		GD.Print("[patriarca] ================ O GRANDE PATRIARCA DE NAMEK (dono, 2026-10-19) ================");

		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		ZoneKey namek = ZoneKey.Premade("Namek");
		RotinaConfig rotinaDeAntes = _rotina;
		try
		{
			ServerPlayer? pl = mesa.EntraNoMundo(cli, ContaDaBancadaDoPatriarca, "Discipulo");
			ZoneCollision? mapa = MapaDaZonaOuCatalogo(namek);
			AfirmarPt("(montagem) o cliente entra pela porta e e jogador; ha moldes, catalogo de aparencia e o mapa de Namek",
					  pl != null && EhJogador(pl) && _moldes != null && _visual != null && mapa != null);
			if (pl == null || _moldes == null || _visual == null || mapa == null) return;

			OMoldeEOPlanoPt();
			if (ONascimentoPt(namek, mapa) is { } guru)
			{
				AFolhaDoPatriarcaPt(guru);
				// SAI ANTES DAS OUTRAS FAMILIAS: ele nasceu perto do berco, e a praca delas tambem e perto do berco -- dois Gurus a dois
				// tiles um do outro e a conversa nao saberia de qual se fala.
				RemoverNpc(guru);
				_ptNoMundo.Remove(guru);
			}
			if (NaoSaiVagandoPt(namek, mapa) is not { } g) return;
			OMenuEPt(pl, g, namek);
			AConversaPt(g, namek);
			ARealezaCustaMaisPt(pl);
		}
		catch (Exception e)
		{
			AfirmarPt("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			PertoDoBercoIgnoradoDeTeste = false;
			PatriarcaComOCorpoDoPovoDeTeste = false;
			QuemNaoVagueiaPasseiaDeTeste = false;
			EmbaralhoLevaQuemNaoVagueiaDeTeste = false;
			PatriarcaSemMenuDeTeste = false;
			PatriarcaIgnoraAReputacaoDeTeste = false;
			RealezaComoPlebeDeTeste = false;
			EscutaDeBills = null;
			_rotina = rotinaDeAntes;
			foreach (ServerPlayer c in _ptNoMundo) if (_players.ContainsKey(c.Id)) RemoverNpc(c);
			_ptNoMundo.Clear();
			foreach (Dictionary<string, double> livro in _reputacao.Values) foreach (string c in _ptContas) livro.Remove(c);
			_ptContas.Clear();
			SalvarReputacao();
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaBancadaDoPatriarca + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch (Exception e) { GD.PushWarning($"[patriarca] nao apaguei '{ContaDaBancadaDoPatriarca}': {e.Message}"); }
			_ptRodando = false;
			GD.Print($"[patriarca] ================ {_ptOk} OK, {_ptFalhou} FALHA(S) ================");
		}
	}

	private static (int X, int Y) CelulaPt(Vec2 p) =>
		((int)MathF.Floor(p.X / ZoneCollision.TileSize), (int)MathF.Floor(p.Y / ZoneCollision.TileSize));

	// =====================================================================
	// M) O MOLDE E O PLANO
	// =====================================================================
	private void OMoldeEOPlanoPt()
	{
		GD.Print("[patriarca] -- M) o molde e o plano --");
		MoldeDeNpc? m = _moldes!.Get(Patriarca.Molde);
		AfirmarPt("o molde `patriarca` e o `make_namek_guru` (PlanetPopulation.dm:444-452): habitante, Namekuseijin do Cla do Dragao, homem, chamado Guru, BP de 9 a 12 mil -- e o molde nao tem problema nenhum",
				  m is { Tipo: TipoDeNpc.Cidadao, Classe: "Dragon clan", BpMin: 9_000.0, BpMax: 12_000.0 }
				  && m.Racas.SequenceEqual(["Namekian"]) && m.Generos.SequenceEqual(["Male"]) && m.Nomes.SequenceEqual(["Guru"])
				  && m.Problemas().Count == 0,
				  m == null ? "sem molde"
				  : $"{m.Tipo} {string.Join(",", m.Racas)} {m.Classe} {string.Join(",", m.Generos)} {string.Join(",", m.Nomes)} BP {m.BpMin}-{m.BpMax}; problemas: {string.Join(" | ", m.Problemas())}");
		AfirmarPt("...e NAO VAGUEIA (`\"vagueia\": false`, o `idle_wandering = 0` de :219) -- e o habitante comum vagueia (o padrao do campo)",
				  m is { Vagueia: false } && _moldes.Get("cidadao") is { Vagueia: true });

		LinhaDePovoamento[] dele = [.. _moldes.Plano.Where(l => l.Molde == Patriarca.Molde)];
		List<string> problemas = Povoamento.Problemas(_moldes.Plano, _moldes);
		AfirmarPt("o plano tem UMA linha dele -- Namek, um corpo, a ate 10 tiles do berco (o `count_citizens(\"Namek\", \"guru\") < 1` de :459 e o `planet_spawn_turf` de :279-294) -- e o plano inteiro continua sem problema",
				  dele.Length == 1 && dele[0] is { Planeta: "Namek", Quantos: 1, PertoDoBerco: 10, TronoVago: "", CadaDonoTiraUm: "" } && problemas.Count == 0,
				  $"{dele.Length} linha(s): {string.Join("; ", dele.Select(l => $"{l.Planeta}/{l.Quantos}/perto {l.PertoDoBerco}"))}; problemas: {string.Join(" | ", problemas)}");
		AfirmarPt("...e o povo continua espalhado pelo habitat: nenhuma outra linha nasce perto do berco",
				  _moldes.Plano.Where(l => l.Molde != Patriarca.Molde).All(l => l.PertoDoBerco == 0));
	}

	// =====================================================================
	// N) O NASCIMENTO
	// =====================================================================
	private ServerPlayer? ONascimentoPt(ZoneKey namek, ZoneCollision mapa)
	{
		GD.Print("[patriarca] -- N) o nascimento: um so, perto do berco de Namek --");
		(int bx, int by) = CelulaPt(PontoDeNascimento(namek));
		bool PertoDoBerco(Vec2 p)
		{
			(int x, int y) = CelulaPt(p);
			return Math.Abs(x - bx) <= 10 && Math.Abs(y - by) <= 10;
		}

		// UM PEDIDO CUJA VAGA DO HABITAT FICA LONGE DO BERCO: sem isto o contra-exemplo podia cair perto por sorte e passar verde
		ulong lugar = 0;
		for (ulong l = 1; l <= 40 && lugar == 0; l++)
			if (!PertoDoBerco(PontoDeHabitante(namek, l))) lugar = l;
		AfirmarPt("(montagem) achei um pedido cuja vaga do habitat fica a mais de 10 tiles do berco", lugar > 0, $"berco ({bx},{by})");
		if (lugar == 0) return null;

		ServerPlayer? guru = NascerPeloDrenoPt(namek, lugar);
		(int gx, int gy) = guru == null ? (-1, -1) : CelulaPt(guru.Pos);
		AfirmarPt("**ELE NASCE PERTO DO BERCO DE NAMEK** (o `planet_spawn_turf`): pelo dreno de producao da fila do povoamento, a ate 10 tiles por eixo do ponto de nascer, em chao que serve (sem parede, agua nem beirada)",
				  guru != null && guru.Zone.Equals(namek) && PertoDoBerco(guru.Pos) && mapa.ServeDeChao(gx, gy),
				  $"berco ({bx},{by}); ele ({gx},{gy}); a vaga do habitat do mesmo pedido {CelulaPt(PontoDeHabitante(namek, lugar))}");
		AfirmarPt("...Namekuseijin do Cla do Dragao, chamado Guru, homem, com o BP do DM (9 a 12 mil) -- e habitante: nao ataca ninguem sozinho",
				  guru is { Race: "Namekian", Class: "Dragon clan", Name: "Guru", Genero: "Male" } && guru.Ficha.BP is >= 9_000 and <= 12_000
				  && guru.Papel is { Pacifico: true },
				  $"{guru?.Race}/{guru?.Class} {guru?.Name} {guru?.Genero} BP {guru?.Ficha.BP:N0}");

		ServerPlayer? longe;
		PertoDoBercoIgnoradoDeTeste = true;
		try { longe = NascerPeloDrenoPt(namek, lugar); }
		finally { PertoDoBercoIgnoradoDeTeste = false; }
		AfirmarPt("(defeito injetado: a linha sem o `pertoDoBerco`) ele nasce na vaga do habitat, longe do berco -- a regua de cima REPROVA",
				  longe != null && !PertoDoBerco(longe.Pos), longe == null ? "nao nasceu" : $"ele {CelulaPt(longe.Pos)}");
		if (longe != null)
		{
			RemoverNpc(longe);
			_ptNoMundo.Remove(longe);
		}

		// O SORTEIO PERTO DO BERCO, DUZENTAS VEZES: todo pedido cai a ate 10 tiles e em chao que serve (o `!T2.density` das 25 tentativas
		// do DM) -- sem a pergunta do chao, um em cada cinco cairia em parede ou agua --, e o mesmo pedido cai sempre no mesmo ponto
		int foraDoChao = 0, longeDoBerco = 0;
		bool puro = true;
		for (ulong l = 1; l <= 200; l++)
		{
			Vec2 p = PontoDoPedido(Patriarca.Molde, namek, l);
			(int cx, int cy) = CelulaPt(p);
			if (!mapa.ServeDeChao(cx, cy)) foraDoChao++;
			if (!PertoDoBerco(p)) longeDoBerco++;
			if (l <= 5) puro &= PontoDoPedido(Patriarca.Molde, namek, l).Equals(p);
		}
		AfirmarPt("...e DUZENTOS PEDIDOS DELE caem todos a ate 10 tiles do berco, em chao que serve -- e o mesmo pedido cai sempre no mesmo ponto (funcao pura da semente, como a vaga do habitat)",
				  foraDoChao == 0 && longeDoBerco == 0 && puro, $"{foraDoChao} fora do chao; {longeDoBerco} longe do berco; o mesmo pedido no mesmo ponto: {puro}");
		if (guru == null) return null;

		// A MANUTENCAO: com ele de pe ela nao poe outro; com ele morto poe UM (morto nao conta) -- e duas voltas seguidas continuam
		// pondo um so (a fila conta como corpo)
		int comEle = GurusQueAManutencaoPoePt(namek, voltas: 1);
		int morto;
		guru.Ficha.dead = true;
		try { morto = GurusQueAManutencaoPoePt(namek, voltas: 2); }
		finally { guru.Ficha.dead = false; }
		AfirmarPt("**UM SO, E OUTRO QUANDO ELE MORRE** (o `count_citizens(\"Namek\", \"guru\") < 1`, :459): com ele de pe a manutencao nao poe ninguem; morto, poe UM -- e duas voltas seguidas continuam pondo um",
				  comEle == 0 && morto == 1, $"com ele de pe {comEle}; morto, depois de duas voltas {morto}");
		return guru;
	}

	/// <summary>
	/// UM NASCIMENTO PELO DRENO DE PRODUCAO (`TickDoPovoamento`): a fila so com este pedido e o relogio da manutencao adiado, pra que o
	/// tique so drene. Devolve o Guru que nasceu (ele entra na lista do que a bancada tira do mundo).
	/// </summary>
	private ServerPlayer? NascerPeloDrenoPt(ZoneKey zona, ulong lugar)
	{
		var filaAntes = new List<(string Molde, ZoneKey Zona, ulong Lugar)>(_filaDoPovoamento);
		double proxima = _proximaManutencao;
		var jaHavia = new HashSet<int>(_players.Keys);
		try
		{
			_filaDoPovoamento.Clear();
			_filaDoPovoamento.Enqueue((Patriarca.Molde, zona, lugar));
			_proximaManutencao = double.MaxValue;
			TickDoPovoamento(0);
		}
		finally
		{
			_proximaManutencao = proxima;
			_filaDoPovoamento.Clear();
			foreach (var f in filaAntes) _filaDoPovoamento.Enqueue(f);
		}
		ServerPlayer? novo = _players.Values.FirstOrDefault(p => !jaHavia.Contains(p.Id) && EhOPatriarca(p));
		if (novo != null) _ptNoMundo.Add(novo);
		return novo;
	}

	/// <summary>Quantos pedidos do Guru em Namek a manutencao de producao poe numa fila vazia, em N voltas seguidas. A fila volta como estava.</summary>
	private int GurusQueAManutencaoPoePt(ZoneKey namek, int voltas)
	{
		var filaAntes = new List<(string Molde, ZoneKey Zona, ulong Lugar)>(_filaDoPovoamento);
		try
		{
			_filaDoPovoamento.Clear();
			for (int i = 0; i < voltas; i++) Manutencao();
			return _filaDoPovoamento.Count(f => f.Molde == Patriarca.Molde && f.Zona.Hash == namek.Hash);
		}
		finally
		{
			_filaDoPovoamento.Clear();
			foreach (var f in filaAntes) _filaDoPovoamento.Enqueue(f);
		}
	}

	// =====================================================================
	// A) A FOLHA DELE
	// =====================================================================
	private void AFolhaDoPatriarcaPt(ServerPlayer guru)
	{
		GD.Print("[patriarca] -- A) a folha dele --");
		VisualCatalog cat = _visual!;
		static string Nome(string caminho) => System.IO.Path.GetFileNameWithoutExtension(caminho);

		string folha = cat.CorpoSprite(guru.Visual, guru.Race, guru.Genero);
		AfirmarPt("**A FOLHA DO GRANDE PATRIARCA** (o `M.icon = 'NamekOld - Guru Style.dmi'`, :449): o corpo dele e o indice reservado, e o catalogo desenha a folha do Guru -- sem roupa nenhuma por cima (a fabrica do DM nao o veste)",
				  guru.Visual.Corpo == Patriarca.Corpo && folha == Patriarca.Folha && guru.Visual.Roupa.Count == 0,
				  $"corpo {guru.Visual.Corpo}; folha {Nome(folha)}; roupa [{string.Join(", ", guru.Visual.Roupa.Select(r => Nome(r.Caminho)))}]");

		// SEM ROUPA EM SEMENTE NENHUMA: o casaco e o cachecol do povo de Namek saem em quatro de cada cinco sorteios (`RoupaDeNpc`), e um
		// Guru so nao diria se a linha que os tira existe
		int vestidos = 0;
		for (ulong s = 1; s <= 20; s++)
			if (AparenciaDeNpc(guru.Papel!.Molde, guru.Race, guru.Genero, s).Roupa.Count > 0) vestidos++;
		AfirmarPt("...e SEM ROUPA em nenhuma de vinte sementes: a folha ja e o manto dele, e o casaco e o cachecol do povo de Namek nao caem por cima",
				  vestidos == 0, $"{vestidos} de 20 sairam vestidos");

		Appearance clara = guru.Visual.Copiar();
		clara.Tom = 0;
		(Rgb? soma, float brilho) = cat.TintaDoCorpo(clara, "Namekian");
		(_, float doPovo) = cat.TintaDoCorpo(new Appearance { Tom = 0 }, "Namekian");
		AfirmarPt("...e ela sai COMO FOI DESENHADA: com o tom mais claro, o verde claro que clareia o corpo do povo nao mexe nela",
				  soma == null && brilho == 1f && doPovo > 1f, $"brilho {brilho}; o do povo {doPovo}");

		Appearance daCriacao = new() { Corpo = Patriarca.Corpo };
		cat.Sanear(daCriacao, "Namekian", "Male");
		AfirmarPt("...e NINGUEM A ESCOLHE NA CRIACAO: a ficha de um jogador Namekuseijin que chega com o indice dela volta ao corpo do povo no saneamento",
				  daCriacao.Corpo != Patriarca.Corpo && cat.CorpoSprite(daCriacao, "Namekian", "Male") != Patriarca.Folha, $"corpo {daCriacao.Corpo}");

		Appearance doPovoComODefeito;
		PatriarcaComOCorpoDoPovoDeTeste = true;
		try { doPovoComODefeito = AparenciaDeNpc(guru.Papel!.Molde, guru.Race, guru.Genero, guru.Papel.Semente); }
		finally { PatriarcaComOCorpoDoPovoDeTeste = false; }
		Appearance semDefeito = AparenciaDeNpc(guru.Papel.Molde, guru.Race, guru.Genero, guru.Papel.Semente);
		AfirmarPt("(defeito injetado: o Guru vestido como o povo) a mesma aparencia sai com o corpo comum de Namekuseijin -- a regua de cima REPROVA (e sem o defeito a mesma semente da a folha dele)",
				  cat.CorpoSprite(doPovoComODefeito, "Namekian", "Male") != Patriarca.Folha && cat.CorpoSprite(semDefeito, "Namekian", "Male") == Patriarca.Folha,
				  $"com o defeito {Nome(cat.CorpoSprite(doPovoComODefeito, "Namekian", "Male"))}, {doPovoComODefeito.Roupa.Count} peca(s)");
	}

	// =====================================================================
	// V) NAO SAI VAGANDO
	// =====================================================================
	private ServerPlayer? NaoSaiVagandoPt(ZoneKey namek, ZoneCollision mapa)
	{
		GD.Print("[patriarca] -- V) nao sai vagando: a vida diaria parada e o embaralho que nao o leva --");
		Vec2? praca = PracaDeChaoPt(namek, mapa, 4);
		AfirmarPt("(montagem) achei uma praca de chao aberto (9x9, sem parede, agua nem teto) perto do berco de Namek", praca != null);
		if (praca is not { } centro) return null;
		ServerPlayer? g = NascerNpc(Patriarca.Molde, namek, centro, 9_101);
		AfirmarPt("(montagem) o Guru da praca nasceu pela fabrica de producao", g != null);
		if (g == null) return null;
		_ptNoMundo.Add(g);

		// A VIDA DIARIA COM PRESSA (`ConfigDaPressaPt`): sem conversa, todo habitante sai passeando na primeira decisao; com conversa,
		// quem a puxou chama pro spar no fim dela; e todo convite e aceito por quem pode aceitar.
		const float t = ZoneCollision.TileSize;
		RotinaConfig antes = _rotina;
		ServerPlayer? vizinho = null;
		try
		{
			_rotina = ConfigDaPressaPt(antes, comConversa: false);
			VidaDiariaPt v = VidaDiariaDoPatriarcaPt(g, centro);
			AfirmarPt("**ELE NAO SAI DO LUGAR** (o `idle_wandering = 0`, :219): cinco segundos de vida diaria em que todo habitante passearia, e ele fica onde nasceu",
					  v.Andou < 1f && v.Parado && v.Afazer == Afazer.Ocioso, $"andou {v.Andou:0.0} px; {v.Afazer}: {v.PorqueDaVida}");
			AfirmarPt("...e NAO ENTRA EM SPAR (a luta o levaria pra longe do lugar dele): chamado pra conversar ele conversa, convidado pro spar ele diz nao",
					  v.Conversou && !v.Aceitou && v.PorqueDoConvite == "recusa: nao sai do lugar", $"conversou {v.Conversou}; aceitou {v.Aceitou}; {v.PorqueDoConvite}");

			vizinho = NascerNpc("cidadao", namek, centro + new Vec2(t, 0), 9_200);
			if (vizinho != null) _ptNoMundo.Add(vizinho);
			_rotina = ConfigDaPressaPt(antes, comConversa: true);
			ConversaPt c = vizinho == null ? default : ConversaQueElePuxaPt(g, vizinho, centro);
			AfirmarPt("...e A CONVERSA QUE ELE PUXA COM UM VIZINHO ACABA SEM CONVITE: ele conversa como todo habitante, mas nao chama ninguem pro spar",
					  vizinho != null && c.Conversas >= 1 && c.Convites == 0 && c.Afazer != Afazer.Sparring,
					  $"vizinho {vizinho != null}; conversas {c.Conversas}; convites {c.Convites}; {c.Afazer}");

			VidaDiariaPt cd;
			ConversaPt cc;
			QuemNaoVagueiaPasseiaDeTeste = true;
			try
			{
				_rotina = ConfigDaPressaPt(antes, comConversa: false);
				cd = VidaDiariaDoPatriarcaPt(g, centro);
				_rotina = ConfigDaPressaPt(antes, comConversa: true);
				cc = vizinho == null ? default : ConversaQueElePuxaPt(g, vizinho, centro);
			}
			finally { QuemNaoVagueiaPasseiaDeTeste = false; }
			AfirmarPt("(defeito injetado: a rotina de todo habitante) ele sai passeando, aceita o spar e chama o vizinho pro spar -- as tres reguas de cima REPROVAM",
					  cd.Andou > 8f && cd.Aceitou && cc.Convites >= 1, $"andou {cd.Andou:0.0} px; {cd.PorqueDoConvite}; convites {cc.Convites}");
		}
		finally
		{
			_rotina = antes;
			g.Rotina = null;
			g.Pos = centro;
			g.Moving = false;
			if (vizinho != null)
			{
				RemoverNpc(vizinho);
				_ptNoMundo.Remove(vizinho);
			}
		}

		// O EMBARALHO DO PLANETA VAZIO: quarenta minutos sem ninguem em Namek (o raio no teto) e tres vizinhos de controle
		var vizinhos = new List<ServerPlayer>();
		for (int i = 0; i < 3; i++)
			if (NascerNpc("cidadao", namek, centro + new Vec2((i - 1) * 2 * t, 2 * t), 9_201 + (ulong)i) is { } viz)
			{
				vizinhos.Add(viz);
				_ptNoMundo.Add(viz);
			}
		bool crivoDele = PasseiaSozinho(g, null), crivoDoPovo = vizinhos.Count == 3 && vizinhos.All(viz => PasseiaSozinho(viz, null));
		List<Vec2> ondeEstavam = [.. vizinhos.Select(viz => viz.Pos)];
		Vec2 ondeEle = g.Pos;
		bool tinhaMarca = _vazioDesde.TryGetValue(namek.Hash, out long marca);
		try
		{
			_vazioDesde[namek.Hash] = NowMs() - 40 * 60_000L;
			EmbaralharSeEsfriou(namek);
		}
		finally
		{
			if (tinhaMarca) _vazioDesde[namek.Hash] = marca;
			else _vazioDesde.Remove(namek.Hash);
		}
		int andaram = vizinhos.Where((viz, i) => Vec2.Distance(viz.Pos, ondeEstavam[i]) > 1f).Count();
		AfirmarPt("**O EMBARALHO DO PLANETA VAZIO NAO O LEVA** (o dono: 'qualquer um cujo lugar seja o papel dele'): quarenta minutos sem ninguem em Namek, os vizinhos andam e ele fica",
				  !crivoDele && crivoDoPovo && andaram > 0 && Vec2.Distance(g.Pos, ondeEle) < 1f,
				  $"o crivo: ele {crivoDele}, o povo {crivoDoPovo}; andaram {andaram} de {vizinhos.Count}; ele andou {Vec2.Distance(g.Pos, ondeEle):0.0} px");
		bool crivoComDefeito;
		EmbaralhoLevaQuemNaoVagueiaDeTeste = true;
		try { crivoComDefeito = PasseiaSozinho(g, null); }
		finally { EmbaralhoLevaQuemNaoVagueiaDeTeste = false; }
		AfirmarPt("(defeito injetado: o embaralho leva quem nao vagueia) o crivo o deixa passear -- a regua de cima REPROVA", crivoComDefeito);
		foreach (ServerPlayer viz in vizinhos)
		{
			RemoverNpc(viz);
			_ptNoMundo.Remove(viz);
		}
		return g;
	}

	private readonly record struct VidaDiariaPt(float Andou, bool Parado, Afazer Afazer, string PorqueDaVida, bool Conversou, bool Aceitou, string PorqueDoConvite);

	private readonly record struct ConversaPt(int Conversas, int Convites, Afazer Afazer);

	/// <summary>A vida diaria com pressa: ocio de 0,2 s, sem treino; conversa certa (de meio segundo) ou nenhuma; convite e aceite certos.</summary>
	private static RotinaConfig ConfigDaPressaPt(RotinaConfig de, bool comConversa) => new()
	{
		Espalhamento = de.Espalhamento, Frases = de.Frases,
		OciosoMin = 0.2, OciosoMax = 0.2, PasseioMin = 2, PasseioMax = 2, PasseioTiles = 4, ChanceDeTreinar = 0,
		ChanceDeConversar = comConversa ? 1 : 0, ConversaMin = 0.5, ConversaMax = 0.5,
		ChanceDeConvidar = 1, ChanceDeAceitar = 1, RazaoDePoderQueAssusta = 1e9,
	};

	/// <summary>
	/// A CONVERSA QUE ELE PUXA, pelo `TicarARotina` de producao dos dois: o vizinho ganha a rotina e fica ocioso; o Guru decide primeiro
	/// (e o pareamento do servidor chama o vizinho); depois os dois vivem dois segundos -- a conversa de meio segundo acaba la dentro.
	/// </summary>
	private ConversaPt ConversaQueElePuxaPt(ServerPlayer g, ServerPlayer vizinho, Vec2 centro)
	{
		double dt = Protocol.TickSeconds;
		g.Rotina = null;
		vizinho.Rotina = null;
		g.Pos = centro;
		g.Moving = false;
		vizinho.Pos = centro + new Vec2(ZoneCollision.TileSize, 0);
		vizinho.Moving = false;
		TicarARotina(vizinho, dt, dt, out _, out _);
		for (int i = 0; i < 12; i++) TicarARotina(g, dt, dt, out _, out _);
		for (int i = 0; i < 60; i++)
		{
			TicarARotina(g, dt, dt, out _, out _);
			TicarARotina(vizinho, dt, dt, out _, out _);
		}
		Rotina r = g.Rotina!;
		return new ConversaPt(r.Conversas, r.Convites, r.Afazer);
	}

	/// <summary>
	/// CINCO SEGUNDOS DE VIDA DIARIA pelo `TicarARotina` de producao, com a rotina montada do zero (e na montagem que o molde vira
	/// `Parado`); depois um vizinho de mentira o chama pra conversar e o convida pro spar, pela porta que o servidor usa.
	/// </summary>
	private VidaDiariaPt VidaDiariaDoPatriarcaPt(ServerPlayer g, Vec2 centro)
	{
		g.Rotina = null;
		g.Pos = centro;
		g.Moving = false;
		for (int i = 0; i < 150; i++) TicarARotina(g, Protocol.TickSeconds, Protocol.TickSeconds, out _, out _);
		Rotina r = g.Rotina!;
		float andou = Vec2.Distance(g.Pos, centro);
		(bool parado, Afazer afazer, string porque) = (r.Parado, r.Afazer, r.Porque);
		const int vizinho = IdBaseDoPatriarcaDeTeste + 999;
		bool conversou = r.Chamado(vizinho, 5);
		bool aceitou = conversou && r.Convite(vizinho, 1);
		return new VidaDiariaPt(andou, parado, afazer, porque, conversou, aceitou, r.Porque);
	}

	/// <summary>
	/// UMA PRACA DE CHAO ABERTO: o centro do quadrado de (2r+1) celulas mais perto do berco em que toda celula serve de chao e nenhuma
	/// esta sob teto -- pra vida diaria e o menu medirem o Guru no aberto, e nao no corredor de uma casa.
	/// </summary>
	private Vec2? PracaDeChaoPt(ZoneKey zona, ZoneCollision mapa, int r)
	{
		(int bx, int by) = CelulaPt(PontoDeNascimento(zona));
		for (int anel = 0; anel <= 60; anel++)
			for (int dx = -anel; dx <= anel; dx++)
				for (int dy = -anel; dy <= anel; dy++)
				{
					if (Math.Abs(dx) != anel && Math.Abs(dy) != anel) continue;
					bool livre = true;
					for (int x = -r; x <= r && livre; x++)
						for (int y = -r; y <= r && livre; y++)
							livre = mapa.ServeDeChao(bx + dx + x, by + dy + y) && !CelulaSobTeto(zona, bx + dx + x, by + dy + y);
					if (livre) return mapa.CentroDaCelula(bx + dx, by + dy);
				}
		return null;
	}

	// =====================================================================
	// P) O MENU DA TECLA E
	// =====================================================================
	private void OMenuEPt(ServerPlayer pl, ServerPlayer g, ZoneKey namek)
	{
		GD.Print("[patriarca] -- P) o menu da tecla E --");
		Jandirus.Core.Tech.Interacoes.Acao[] acoes = Jandirus.Core.Tech.Interacoes.De(Patriarca.Tipo);
		AfirmarPt("a tabela da tecla E tem o menu dele: um botao, \"Falar com o Grande Patriarca\", que manda o verbo da conversa -- e o cliente o desenha como qualquer menu (`Interacoes.Interativo`)",
				  acoes.Length == 1 && acoes[0].Rotulo == "Falar com o Grande Patriarca" && acoes[0].Verbo == Patriarca.VerboFalar
				  && Jandirus.Core.Tech.Interacoes.Interativo(Patriarca.Tipo),
				  string.Join(" | ", acoes.Select(a => $"{a.Rotulo} -> {a.Verbo}")));

		const float t = ZoneCollision.TileSize;
		EscutaDeBills = [];
		try
		{
			ApontadoPt perto = ApontarAoJogadorPt(pl, namek, g.Pos + new Vec2(0, t));
			AfirmarPt("**A UM TILE DELE, A TECLA E O APONTA**: o `S2C.DivinoPerto` sai com o corpo dele, o menu `Grande_Patriarca` e o nome -- e nada de poder no pacote",
					  perto.Chegou && perto.Corpo == g.Id && perto.Tipo == Patriarca.Tipo && perto.Nome == g.Name && perto.Sobra == 0, $"{perto}");
			ApontadoPt longe = ApontarAoJogadorPt(pl, namek, g.Pos + new Vec2(0, 3 * t));
			AfirmarPt("...a TRES tiles, nao (a caixa da tecla E e de 2 tiles por eixo: o `get_dist(usr, src) > 2` do DM, :223)",
					  longe.Chegou && longe.Corpo == 0, $"{longe}");
			ApontadoPt caido;
			g.Ficha.KO = true;
			try { caido = ApontarAoJogadorPt(pl, namek, g.Pos + new Vec2(0, t)); }
			finally { g.Ficha.KO = false; }
			AfirmarPt("...e com ele CAIDO, nao (`if(dead || KO) return`, :222)", caido.Chegou && caido.Corpo == 0, $"{caido}");
			ApontadoPt cego;
			PatriarcaSemMenuDeTeste = true;
			try { cego = ApontarAoJogadorPt(pl, namek, g.Pos + new Vec2(0, t)); }
			finally { PatriarcaSemMenuDeTeste = false; }
			AfirmarPt("(defeito injetado: o apontador nao enxerga o Guru) a um tile dele nao ha menu -- a regua de cima REPROVA",
					  cego.Chegou && cego.Corpo == 0, $"{cego}");
		}
		finally { EscutaDeBills = null; }
	}

	private readonly record struct ApontadoPt(bool Chegou, int Corpo, string Tipo, string Nome, int Sobra);

	/// <summary>
	/// POE O JOGADOR NESTE PONTO, esquece o que ja tinha sido apontado a ele (o apontador so manda quando a resposta muda) e roda o
	/// apontador de producao; devolve o pacote que saiu pra ele, aberto -- e quantos bytes sobraram depois dos tres campos.
	/// </summary>
	private ApontadoPt ApontarAoJogadorPt(ServerPlayer pl, ZoneKey zona, Vec2 onde)
	{
		if (!pl.Zone.Equals(zona)) MoveToZone(pl.Id, zona, onde);
		pl.Pos = onde;
		_divinoApontado.Remove(pl.Id);
		EscutaDeBills!.Clear();
		ApontarODivinoPerto();
		for (int i = EscutaDeBills.Count - 1; i >= 0; i--)
		{
			(int quem, byte[] fio) = EscutaDeBills[i];
			if (quem != pl.Id || fio.Length == 0 || fio[0] != (byte)Protocol.S2C.DivinoPerto) continue;
			var r = new NetDataReader(fio);
			r.GetByte();
			int corpo = r.GetInt();
			string tipo = r.GetString(), nome = r.GetString();
			return new ApontadoPt(true, corpo, tipo, nome, r.AvailableBytes);
		}
		return new ApontadoPt(false, 0, "", "", -1);
	}

	// =====================================================================
	// F) A CONVERSA
	// =====================================================================
	private void AConversaPt(ServerPlayer g, ZoneKey namek)
	{
		GD.Print("[patriarca] -- F) a conversa: o `Guru/Click()` --");
		const float t = ZoneCollision.TileSize;
		string povo = g.Berco.Natal;

		ServerPlayer andarilho = ForjarPt("Andarilho", namek, g.Pos + new Vec2(0, 3 * t), "bancada_pt_longe");
		List<string> deLonge = Ouvir(() => ComandoDeInteracao(andarilho, Patriarca.VerboFalar, ""));
		AfirmarPt("**DE LONGE ELE PEDE PRA CHEGAR PERTO** (:223-225): a tres tiles, \"Chegue mais perto para falar com o Grande Patriarca.\" -- e nada desperta",
				  Disse(deLonge, "Chegue mais perto para falar com o Grande Patriarca") && andarilho.Ficha.unlockPotential < 1, Ultimos(deLonge));

		ServerPlayer malvisto = ForjarPt("Malfeitor", namek, g.Pos + new Vec2(-t, 0), "bancada_pt_malvisto");
		SomarReputacao(povo, malvisto, Jandirus.Core.Social.Reputacao.LimiarDeMalvisto, "bancada do patriarca");
		List<string> doMalvisto = Ouvir(() => ComandoDeInteracao(malvisto, Patriarca.VerboFalar, ""));
		AfirmarPt("**O MALVISTO DE NAMEK E MANDADO EMBORA** (:226-228, 'pelo menos neutro'): com a reputacao no piso de \"Malvisto\" (-10), \"Sinto o sofrimento do meu povo em suas maos... Saia da minha frente.\" -- e nada desperta",
				  Disse(doMalvisto, "Sinto o sofrimento do meu povo em suas mãos") && malvisto.Ficha.unlockPotential < 1,
				  $"reputacao com {povo}: {ReputacaoDe(povo, malvisto.Conta)} | {Ultimos(doMalvisto)}");
		PatriarcaIgnoraAReputacaoDeTeste = true;
		try { Ouvir(() => ComandoDeInteracao(malvisto, Patriarca.VerboFalar, "")); }
		finally { PatriarcaIgnoraAReputacaoDeTeste = false; }
		AfirmarPt("(defeito injetado: o Guru nao pergunta a reputacao) o malvisto desperta -- a regua de cima REPROVA", malvisto.Ficha.unlockPotential >= 1);

		ServerPlayer discipulo = ForjarPt("Dende", namek, g.Pos + new Vec2(t, 0), "bancada_pt_discipulo");
		SomarReputacao(povo, discipulo, Jandirus.Core.Social.Reputacao.LimiarDeMalvisto + 1, "bancada do patriarca");
		// (2026-10-26) O GURU SOLTA O BANCO DO POTENCIAL 1,25x (o dono, o pedido 95): mil guardados viram 1.250 de BP base
		discipulo.Ficha.hiddenpotential = 1000;
		double bp0 = discipulo.Ficha.BP;
		List<string> desperta = Ouvir(() => ComandoDeInteracao(discipulo, Patriarca.VerboFalar, ""));
		AfirmarPt("**O NEUTRO DESPERTA** (:232-234): com a reputacao logo acima do piso, o BANCO DO POTENCIAL sai 1,25x (o dono, 2026-10-25) -- mil guardados, +1.250 de BP base, o banco zera e a hora de recarga comeca --, com as falas do DM: \"Guru poe a mao sobre a cabeca de...\" e \"Desperte... AGORA!\"",
				  discipulo.Ficha.unlockPotential >= 1 && Math.Abs(discipulo.Ficha.BP - (bp0 + 1250)) < 1e-6 && discipulo.Ficha.hiddenpotential == 0
				  && discipulo.Ficha.liberarPotencialEm > NowMs() + Jandirus.Core.Stats.GainKnobs.RecargaDaLiberacaoMs - 60_000
				  && Disse(desperta, $"{g.Name} põe a mão sobre a cabeça de {discipulo.Name}") && Disse(desperta, "o potencial que você guardou se liberta")
				  && Disse(desperta, "Desperte... AGORA!"),
				  $"reputacao {ReputacaoDe(povo, discipulo.Conta)}; BP {bp0:N0} -> {discipulo.Ficha.BP:N0}, banco {discipulo.Ficha.hiddenpotential:0} | {Ultimos(desperta)}");
		double bp1 = discipulo.Ficha.BP;
		discipulo.Ficha.hiddenpotential = 400;
		List<string> deNovo = Ouvir(() => ComandoDeInteracao(discipulo, Patriarca.VerboFalar, ""));
		AfirmarPt("...e DE NOVO DENTRO DA HORA, nao (o dono: \"1h de recarga\"): o potencial \"ainda se assenta\", e o BP e o banco ficam",
				  Disse(deNovo, "ainda se assenta") && discipulo.Ficha.BP == bp1 && discipulo.Ficha.hiddenpotential == 400,
				  $"BP {bp1:N0} -> {discipulo.Ficha.BP:N0} | {Ultimos(deNovo)}");
		AdiantoDoRelogioDeTeste += Jandirus.Core.Stats.GainKnobs.RecargaDaLiberacaoMs;
		List<string> depoisDaHora;
		try { depoisDaHora = Ouvir(() => ComandoDeInteracao(discipulo, Patriarca.VerboFalar, "")); }
		finally { AdiantoDoRelogioDeTeste -= Jandirus.Core.Stats.GainKnobs.RecargaDaLiberacaoMs; }
		AfirmarPt("**...E PASSADA A HORA, DE NOVO** (era uma vez na vida): o banco novo (400) sai 1,25x, +500",
				  Math.Abs(discipulo.Ficha.BP - (bp1 + 500)) < 1e-6 && discipulo.Ficha.hiddenpotential == 0,
				  $"BP {bp1:N0} -> {discipulo.Ficha.BP:N0} | {Ultimos(depoisDaHora)}");

		// O ANDROIDE DE ABSORCAO GUARDA, MAS NAO SOLTA PELO GURU (o dono: "so libera com tech q da um upgrade ... nao pelos metodos tradicionais")
		ServerPlayer maquina = ForjarPt("Dezenove", namek, g.Pos + new Vec2(0, t), "bancada_pt_androide");
		SomarReputacao(povo, maquina, Jandirus.Core.Social.Reputacao.LimiarDeMalvisto + 1, "bancada do patriarca");
		maquina.Ficha.AndroideAbsorcao = true;
		maquina.Ficha.hiddenpotential = 1000;
		double bpDaMaquina = maquina.Ficha.BP;
		List<string> daMaquina = Ouvir(() => ComandoDeInteracao(maquina, Patriarca.VerboFalar, ""));
		AfirmarPt("**O ANDROIDE DE ABSORCAO NAO SOLTA O BANCO PELO GURU**: ele ouve que so um upgrade de tecnologia o solta, e o BP e o banco ficam",
				  Disse(daMaquina, "upgrade de tecnologia") && maquina.Ficha.BP == bpDaMaquina && maquina.Ficha.hiddenpotential == 1000, Ultimos(daMaquina));
		AbsorcaoLiberaDeTeste = true;
		try { Ouvir(() => ComandoDeInteracao(maquina, Patriarca.VerboFalar, "")); }
		finally { AbsorcaoLiberaDeTeste = false; }
		AfirmarPt("(defeito injetado: o androide de absorcao solta pelos caminhos de sempre) o banco dele sai -- a regua de cima REPROVA",
				  maquina.Ficha.hiddenpotential == 0 && maquina.Ficha.BP > bpDaMaquina);

		ServerPlayer aprendiz = ForjarPt("Cargo", namek, g.Pos + new Vec2(0, -t), "bancada_pt_aprendiz");
		List<string> comEleCaido;
		g.Ficha.KO = true;
		try { comEleCaido = Ouvir(() => ComandoDeInteracao(aprendiz, Patriarca.VerboFalar, "")); }
		finally { g.Ficha.KO = false; }
		AfirmarPt("**CAIDO, ELE NAO OUVE** (`if(dead || KO) return`, :222): nada desperta -- e o servidor diz por que (o DM ficava calado)",
				  aprendiz.Ficha.unlockPotential < 1 && Disse(comEleCaido, "não pode te ouvir"), Ultimos(comEleCaido));
	}

	// =====================================================================
	// R) A REALEZA CUSTA MAIS
	// =====================================================================
	/// <summary>
	/// O `(C.pop_role == "commoner") ? REP_KILL_CITIZEN : REP_KILL_ROYAL` (PlanetReputation.dm:124). Cada corpo nasce pela fabrica de producao,
	/// apanha do jogador pelo funil do golpe (`MarcarAgressao`) e morre pelo gancho de producao (`MorreuUmCorpoSemDono`) -- a medida do "o lider
	/// custa mais" da `--conquistateste`. Os numeros esperados sao os do DM, escritos aqui, e nao as constantes do port: uma constante errada
	/// passaria por uma regua que lesse a propria constante.
	/// </summary>
	private void ARealezaCustaMaisPt(ServerPlayer pl)
	{
		GD.Print("[patriarca] -- R) a realeza custa mais: o `REP_KILL_ROYAL` --");
		ZoneKey namek = ZoneKey.Premade("Namek"), vegeta = ZoneKey.Premade("Vegeta");
		_ptContas.Add(pl.Conta);   // o livro-caixa do matador tambem sai no fim
		int karmaDeAntes = pl.Karma;
		ulong lugar = 9_300;

		// quanto a reputacao com o planeta do morto e o karma do matador andaram com esta morte -- partindo do neutro, sem limiar no meio
		(double Rep, int Karma) CustoDeMatar(string molde, ZoneKey zona)
		{
			ServerPlayer? v = NascerNpc(molde, zona, PontoDeNascimento(zona), lugar++);
			if (v == null) return (double.NaN, 0);
			_ptNoMundo.Add(v);
			SomarReputacao(zona.Name, pl, -ReputacaoDe(zona.Name, pl.Conta), "bancada do patriarca");
			int karma0 = pl.Karma;
			MarcarAgressao(v, pl);
			v.Ficha.dead = true;
			MorreuUmCorpoSemDono(v);
			RemoverNpc(v);
			_ptNoMundo.Remove(v);
			return (ReputacaoDe(zona.Name, pl.Conta), pl.Karma - karma0);
		}
		static string Custo((double Rep, int Karma) c) => $"{c.Rep:0.#} de reputacao, {c.Karma:+0;-0} de karma";

		try
		{
			var plebeu = CustoDeMatar("cidadao", namek);
			var guru = CustoDeMatar(Patriarca.Molde, namek);
			var rei = CustoDeMatar(MoldeDoReiVegeta, vegeta);
			var principe = CustoDeMatar(MoldeDoPrincipeVegeta, vegeta);
			AfirmarPt("**O HABITANTE COMUM CUSTA -10** com o povo dele (`REP_KILL_CITIZEN`, PlanetReputation.dm:19)", plebeu.Rep == -10, Custo(plebeu));
			AfirmarPt("**MATAR O GURU CUSTA -25** com Namek (`REP_KILL_ROYAL`, :20 -- o `pop_role = \"guru\"` nao e \"commoner\")", guru.Rep == -25, Custo(guru));
			AfirmarPt("**...O REI DE VEGETA TAMBEM** (o `King`, PlanetPopulation.dm:187-188)", rei.Rep == -25, Custo(rei));
			AfirmarPt("**...E O PRINCIPE** (o \"prince\" do `make_saiyan_elite`, PlanetPopulation.dm:433)", principe.Rep == -25, Custo(principe));
			AfirmarPt("...e o KARMA NAO SEPARA a realeza: o `lose_npc_kill_karma` (SkyNPCs.dm:120-122) tira o mesmo do Guru e do plebeu",
					  plebeu.Karma < 0 && guru.Karma == plebeu.Karma, $"plebeu {plebeu.Karma}, Guru {guru.Karma}");

			(double Rep, int Karma) guruPlebeu, reiPlebeu, principePlebeu;
			RealezaComoPlebeDeTeste = true;
			try
			{
				guruPlebeu = CustoDeMatar(Patriarca.Molde, namek);
				reiPlebeu = CustoDeMatar(MoldeDoReiVegeta, vegeta);
				principePlebeu = CustoDeMatar(MoldeDoPrincipeVegeta, vegeta);
			}
			finally { RealezaComoPlebeDeTeste = false; }
			AfirmarPt("(defeito injetado: a realeza morre como plebe) o Guru, o Rei e o Principe custam os -10 do habitante -- as tres reguas de cima REPROVAM",
					  guruPlebeu.Rep == -10 && reiPlebeu.Rep == -10 && principePlebeu.Rep == -10,
					  $"Guru {Custo(guruPlebeu)}; Rei {Custo(reiPlebeu)}; Principe {Custo(principePlebeu)}");
		}
		finally { pl.Karma = karmaDeAntes; }
	}

	/// <summary>Um corpo de gente (sem par) com conta propria -- a reputacao e por conta -- posto no mundo pela porta de producao.</summary>
	private ServerPlayer ForjarPt(string nome, ZoneKey zona, Vec2 onde, string conta)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDoPatriarcaDeTeste + _ptCorpos++,
			Peer = null,
			Name = nome,
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = zona,
			Pos = onde,
			Conta = conta,
			Slot = 0,
			Ficha = new Fighter { Race = "Human", BP = 10_000 },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.Tick(agoraMs: NowMs());
		_ptNoMundo.Add(novo);
		_ptContas.Add(conta);
		return novo;
	}
}
