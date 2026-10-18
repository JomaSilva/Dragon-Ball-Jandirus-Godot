using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA GRAVIDADE DO CHAO (`--gravidadeteste`) -- o relato do dono de 2026-10-10:
///
///   *"parece q tem um bug com gravidade do planeta, eu morri e revivi e usei o verb pra me mandar pro spawn,
///   esse planeta era 4x gravidade mas ele consta como 1x dps q eu morri e voltei, investigue e resolva"*.
///
/// NO PRIMEIRO LOGIN, com o host de verdade: ele e admin (conecta da maquina do servidor), a triagem da
/// morte so leva pro Outro Mundo quem tem dono na tela, e os dois verbs do relato (`admin_reviver`,
/// `admin_spawn`) passam pelo despacho de producao. Nada aqui recalcula a regra: o corpo pousa pelo
/// `TickDoEspaco`, morre pelo `Morrer()` + a triagem do tique, volta pelos verbs, mergulha e acorda pela
/// porta da mente, e "loga de novo" pelo save gravado em disco.
///
/// O UNIVERSO E SORTEADO A CADA RODADA (pasta de saves nova = semente nova), entao os mundos NAO sao
/// cravados: a bancada varre a carta em volta da origem e escolhe os primeiros que servem.
///
/// ============================ AS SETE FAMILIAS ============================
///   1. A REGRA -- o chao de qualquer zona: catalogo, mundo sorteado (com ele FORA do cache), o espaco
///      (que e `KindProcedural` e nao e mundo), e dois mundos de mesmo NOME e seeds diferentes.
///   2. A HISTORIA DO DONO -- pousa num mundo de gravidade g, morre, sobe (1x), `admin_reviver`,
///      `admin_spawn` com o mundo ja VIVO: o chao continua g, no servidor e no que sai pro cliente.
///   3. AS VARIACOES -- morre num gerado e o spawn e OUTRO gerado (frio e vivo); gerado -> catalogo;
///      catalogo -> gerado; catalogo -> catalogo; e um spawn de 1x (o conserto nao e "sempre mais que 1").
///   4. AS OUTRAS PORTAS -- pouso, `spawn`, `Renascer`, `admin_spawn_alvo`, `admin_ir`, `admin_trazer`,
///      acordar do transe, `MoveToZone` cru e nascer no mundo. Todas entram com g; com o defeito, todas com 1x.
///   5. O LOGIN -- o save diz 1x (como os do dono), o mundo esta FRIO no servidor, e o chao e g.
///   6. O MUNDO FRIO -- quem acorda num mundo sorteado o faz nascer no servidor, pelas duas portas (o login
///      e o `MoveToZone`): colisao, corpo no ceu, decolagem, e a parede de base de volta no mapa. E a volta
///      da zona pro corpo celeste (`CorpoGerado.DaZona`), mundo a mundo.
///   7. O REFUGIADO -- quem perdeu o planeta natal acorda ACLIMATADO no refugio dele (um planeta morre de
///      verdade, dentro do palco de bancada); o visitante e o refugiado fora do refugio dele, nao.
/// ==========================================================================
///
/// QUATRO DEFEITOS INJETADOS, um por conserto: o chao lido so do `planetas.json`
/// (`GravidadeSoDoCatalogoDeTeste`), o mundo frio que ninguem acorda (`MundoFrioFicaFrioDeTeste`), o mundo
/// que nasce nu com gente dentro (`MundoAcordadoNasceNuDeTeste`) e o refugio que nao aclimata
/// (`RefugioNaoAclimataDeTeste`).
///
///     Godot --headless --path . --host --rede 8210 --gravidadeteste --raca Saiyan
///                      --conta bancada_gravidade --nome Pesado
/// </summary>
public partial class GameServer
{
	/// <summary>Faixa de ids propria -- longe do `_nextId` e das faixas das outras bancadas.</summary>
	private const int IdBaseDaGravidadeDeTeste = 95_500;

	/// <summary>A conta do "relog" da familia 5. Gravada de verdade (na pasta desviada da bancada) e apagada no fim.</summary>
	private const string ContaDaBancadaDaGravidade = "bancada_gravidade_relog";

	/// <summary>A maestria da cobaia durante a bancada: o que se mede e o chao, e nao o corpo cedendo a ele.</summary>
	private const double MaestriaDaCobaiaDaGravidade = 20;

	private bool? _gravidadeDeTeste;
	private int _gcOk, _gcFalhou, _gcCorpos;

	/// <summary>A gravidade e o ganho de treino do ultimo `S2C.Atributos` que SAIU pro host -- o que ele tem na tela.</summary>
	private float _gcNaTela = float.NaN, _gcGanhoNaTela = float.NaN;

	/// <summary>Um mundo sorteado desta rodada: o corpo, o endereco dele e a gravidade.</summary>
	private readonly record struct MundoDaGravidade(PlanetaNoEspaco P, int Sx, int Sy, int K, double G)
	{
		public ZoneKey Zona => Espaco.ZonaDe(P);
	}

	/// <summary>Um lugar da familia 3: onde se morre, ou onde fica o spawn.</summary>
	private sealed record ChaoDaGravidade(string Rotulo, ZoneKey Zona, double G, Berco Berco, MundoDaGravidade? Gerado);

	private void AfirmarGc(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _gcOk++; GD.Print($"[gravidade]   OK    {oque}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); return; }
		_gcFalhou++;
		GD.PrintErr($"[gravidade]   FALHA {oque}   [{detalhe}]");
	}

	/// <summary>A unica linha desta bancada no `Entrar`: a flag e lida aqui, e ela roda uma vez so.</summary>
	private void RodarBancadaDaGravidadeDoChaoSePedida(ServerPlayer pl)
	{
		_gravidadeDeTeste ??= Array.IndexOf(OS.GetCmdlineArgs(), "--gravidadeteste") >= 0;
		if (_gravidadeDeTeste != true) return;
		_gravidadeDeTeste = false;
		RodarBancadaDaGravidadeDoChao(pl);
	}

	private void RodarBancadaDaGravidadeDoChao(ServerPlayer pl)
	{
		_gcOk = _gcFalhou = _gcCorpos = 0;
		_gcNaTela = _gcGanhoNaTela = float.NaN;
		GD.Print("[gravidade] ================ O CHAO PUXA O QUE O CHAO PUXA, POR QUALQUER PORTA (dono, 2026-10-10) ================");

		// A FOTO DO HOST. Ele vai morrer, viajar e trocar de berco aqui dentro; tudo volta no `finally`.
		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		Berco bercoGuardado = pl.Berco;
		double maestriaGuardada = pl.Ficha.GravMastered;
		bool mortoGuardado = pl.Ficha.dead, koGuardado = pl.Ficha.KO;
		bool medGuardada = pl.Ficha.med, treinoGuardado = pl.Ficha.train;
		double kiGuardado = pl.Ficha.Ki, vigorGuardado = pl.Ficha.stamina;
		long relogioGuardado = pl.RelogioDaMorte;
		bool viajouGuardado = pl.MorteJaViajou, aureolaGuardada = pl.EnvAureola;
		string origemGuardada = pl.PlanetaDeOrigem;
		ChunkId chunkGuardada = pl.ChunkAtual;

		List<Protocol.AtributosState>? atributosAntes = EscutaDeAtributos;
		HashSet<int> cadaveresDeAntes = GcCadaveres();
		var forjados = new List<ServerPlayer>();

		// A BANCADA DE 1o LOGIN RODA ANTES de o corpo entrar na lista da zona: o `MoveToZone` conta com ele la.
		ZoneList(pl.Zone.Hash).Add(pl);
		EscutaDeAtributos = [];

		try
		{
			AfirmarGc("PRECONDICAO: o host tem dono na tela e e admin (os verbs do relato sao de admin)",
					  EhJogador(pl) && EhAdmin(pl), $"jogador={EhJogador(pl)} admin={EhAdmin(pl)}");

			List<MundoDaGravidade> mundos = GcMundosDaCarta(raio: 10);
			MundoDaGravidade? pesado = null, outro = null;
			foreach (MundoDaGravidade m in mundos)
			{
				if (m.G is < 2 or > 8) continue;   // pesa de verdade, e o mundo e pequeno (gera em poucos ms)
				if (pesado == null) { pesado = m; continue; }
				if (Math.Abs(m.G - pesado.Value.G) > 0.5) { outro = m; break; }
			}
			AfirmarGc($"(montagem) a carta desta rodada tem mundos sorteados ({mundos.Count}), e dois de gravidades diferentes entre 2x e 8x",
					  pesado != null && outro != null,
					  pesado is { } a0 && outro is { } b0 ? $"{a0.P.Nome} {a0.G:0}x e {b0.P.Nome} {b0.G:0}x" : "nao achei");
			if (pesado is not { } p || outro is not { } q) return;

			// A COBAIA DOMINA MAIS DO QUE QUALQUER CHAO DAQUI: o esmagamento e a aclimatacao de berco ficam
			// fora da conta (eles tem as provas deles na familia 4).
			pl.Ficha.GravMastered = Math.Max(pl.Ficha.GravMastered, MaestriaDaCobaiaDaGravidade);
			if (pl.Ficha.dead || pl.Ficha.KO) pl.Combate.Reviver();

			GcARegra(mundos, p, q);
			GcAHistoriaDoDono(pl, p);
			GcAsVariacoes(pl, p, q);
			GcAsOutrasPortas(pl, p, forjados);
			GcOLoginEOMundoFrio(pl, p, mundos, forjados, cadaveresDeAntes);
			GcAVoltaDaZonaProCeu(mundos);
			GcORefugiado(p, mundos, forjados);

			AfirmarGc("a bancada chegou ao fim (sem esta linha, abortar no meio reportaria '0 falhas')", true);
		}
		catch (Exception e)
		{
			AfirmarGc($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			GravidadeSoDoCatalogoDeTeste = false;
			MundoFrioFicaFrioDeTeste = false;
			MundoAcordadoNasceNuDeTeste = false;
			RefugioNaoAclimataDeTeste = false;
			EscutaDeAtributos = atributosAntes;

			try
			{
				if (NaMente(pl) || pl.BonecoLargado != null) SairDoTranse(pl);

				foreach (ServerPlayer f in forjados) GcRecolher(f);
				GcTirarCadaveres(cadaveresDeAntes);
				GcApagarAConta();
				if (_gcParede is { } parede) { TirarBloco(BaseDe(parede.Zona), parede); _gcParede = null; }

				// O HOST VOLTA VIVO, NO LUGAR E COM O BERCO DELE. `Reviver` antes de mover: um corpo morto
				// largado na zona de origem seria a bancada deixando o jogo pior do que encontrou.
				pl.Combate.Reviver();
				pl.Ficha.dead = mortoGuardado;
				pl.Ficha.KO = koGuardado;
				pl.Ficha.Ki = kiGuardado;
				pl.Ficha.stamina = vigorGuardado;
				pl.Ficha.med = medGuardada;
				pl.Ficha.train = treinoGuardado;
				pl.RelogioDaMorte = relogioGuardado;
				pl.MorteJaViajou = viajouGuardado;
				pl.EnvAureola = aureolaGuardada;
				pl.Berco = bercoGuardado;
				pl.Ficha.GravMastered = maestriaGuardada;

				if (!pl.Zone.Equals(zonaGuardada)) MoveToZone(pl.Id, zonaGuardada, posGuardada);
				else { pl.Pos = posGuardada; AplicarGravidade(pl); }
				pl.PlanetaDeOrigem = origemGuardada;
				pl.ChunkAtual = chunkGuardada;
				pl.Combate.SincronizarVida();
				MandarAureola(pl);

				// ...e SAI da lista da zona: o `Entrar` faz o proprio `Add` logo depois desta bancada.
				ZoneList(pl.Zone.Hash).RemoveAll(o => ReferenceEquals(o, pl));
			}
			finally
			{
				GD.Print($"[gravidade] ================ {_gcOk} OK, {_gcFalhou} FALHA(S) ================");
			}
		}
	}

	// =====================================================================
	// 1) A REGRA
	// =====================================================================
	/// <summary>
	/// O CHAO DE QUALQUER ZONA, perguntado ao funil (<see cref="FichaDoChao"/>) sem mover ninguem.
	///
	/// A metade do catalogo e CONTROLE: ela ja valia, e tem que continuar valendo letra por letra (o
	/// apelido de Icer, a Sala do Tempo, o zero do espaco). A metade dos gerados e o conserto.
	/// </summary>
	private void GcARegra(List<MundoDaGravidade> mundos, MundoDaGravidade p, MundoDaGravidade q)
	{
		GD.Print("[gravidade] -- 1) a regra: o chao de qualquer zona --");

		(string Zona, double Esperado)[] doCatalogo =
		[
			("Earth", 1), ("Vegeta", 10), ("Icer", 15), ("Namek", 1), ("Hyperbolic_Time_Chamber", 10),
			(Alem.ZonaDoOutroMundo, 1),
		];
		foreach ((string zona, double esperado) in doCatalogo)
		{
			double g = FichaDoChao(ZoneKey.Premade(zona)).Gravidade;
			AfirmarGc($"catalogo: {zona} puxa {esperado:0}x (o `planetas.json`, como sempre)", Math.Abs(g - esperado) < 1e-9, $"{g:0.##}x");
		}

		// O ESPACO E `KindProcedural` E NAO E UM MUNDO SORTEADO: a seed dele e a do universo, e le-la como
		// seed de planeta daria um peso qualquer ao vacuo.
		double doEspaco = FichaDoChao(ZonaDoEspaco).Gravidade;
		AfirmarGc("o ESPACO continua em ZERO (ele e `KindProcedural`, e a seed dele nao e a de um planeta)",
				  doEspaco == 0, $"{doEspaco:0.##}x; lida como planeta, a seed do universo daria {MundoProcedural.GravidadeDaSeed(ZonaDoEspaco.Seed):0}x");

		// OS GERADOS: o funil, a outra casa do servidor que ja sabia (`MedidasDoPlaneta`) e a conta que o
		// CLIENTE faz ao desenhar o mundo (`MundoProcedural.DaSeed`) tem que dar o mesmo numero.
		int iguais = 0, carregados = 0;
		var distintas = new SortedSet<double>();
		foreach (MundoDaGravidade m in mundos)
		{
			double doFunil = FichaDoChao(m.Zona).Gravidade;
			if (Math.Abs(doFunil - m.G) < 1e-9
				&& Math.Abs(doFunil - MedidasDoPlaneta(m.Zona).Gravidade) < 1e-9
				&& Math.Abs(doFunil - MundoProcedural.DaSeed(m.P.Seed, m.P.Nome).Gravidade) < 1e-9) iguais++;
			if (_zonasGeradas.ContainsKey(m.Zona.Hash)) carregados++;
			distintas.Add(m.G);
		}
		AfirmarGc($"mundo SORTEADO: o chao e o da seed -- o funil, a furia do planeta e o cliente dao o mesmo numero em {mundos.Count} mundos",
				  iguais == mundos.Count && mundos.Count > 0, $"{iguais} de {mundos.Count}");
		AfirmarGc($"...e a resposta NAO depende de o mundo estar carregado no servidor (nenhum dos {mundos.Count} esta)",
				  carregados == 0, $"{carregados} carregado(s)");
		AfirmarGc($"...e ela nao e uma constante: ha {distintas.Count} gravidades diferentes na carta (de {distintas.Min:0}x a {distintas.Max:0}x)",
				  distintas.Count >= 4 && distintas.Max > 1);

		// O NOME NAO E CHAVE: dois mundos de mesmo rotulo e seeds diferentes sao dois chaos.
		var comONomeDoOutro = ZoneKey.Procedural(p.P.Nome, q.P.Seed);
		AfirmarGc($"dois mundos de MESMO NOME e seeds diferentes tem cada um o seu chao ({p.G:0}x e {q.G:0}x)",
				  Math.Abs(FichaDoChao(p.Zona).Gravidade - p.G) < 1e-9
				  && Math.Abs(FichaDoChao(comONomeDoOutro).Gravidade - q.G) < 1e-9);

		GravidadeSoDoCatalogoDeTeste = true;
		try
		{
			double g = FichaDoChao(p.Zona).Gravidade;
			AfirmarGc($"(defeito injetado: o chao lido so do `planetas.json`) a mesma pergunta REPROVA -- {p.P.Nome}, de {p.G:0}x, sai 1x",
					  Math.Abs(g - 1) < 1e-9 && p.G > 1, $"{g:0.##}x");
		}
		finally { GravidadeSoDoCatalogoDeTeste = false; }
	}

	// =====================================================================
	// 2) A HISTORIA DO DONO
	// =====================================================================
	/// <summary>
	/// O RELATO, PASSO A PASSO -- e o que separa a primeira volta dele (4x) da segunda (1x) e so uma
	/// coisa: o mundo estar FRIO ou VIVO no servidor quando o `spawn` e apertado.
	/// </summary>
	private void GcAHistoriaDoDono(ServerPlayer pl, MundoDaGravidade p)
	{
		GD.Print($"[gravidade] -- 2) a historia do dono: {p.P.Nome}, {p.G:0}x --");
		ZoneKey zona = p.Zona;

		// O SPAWN DELE E ESTE MUNDO. No save do dono quem aponta pra la e o refugio (a Terra dele foi
		// destruida); aqui e o berco. Os dois entregam ao MESMO `PousarNo` um berco de mundo gerado.
		pl.Berco = GcBercoEm(p);
		AfirmarGc("(montagem) o mundo esta FRIO no servidor antes do primeiro pouso", !_zonasGeradas.ContainsKey(zona.Hash));

		// (a) A PRIMEIRA CHEGADA: frio -> orbita -> pouso. Era o unico caminho que acertava.
		List<string> falas = Ouvir(() => GcPousar(pl, p.P));
		AfirmarGc("a 1a chegada e pelo POUSO (mundo frio: o berco poe em orbita e o `TickDoEspaco` desce)",
				  pl.Zone.Equals(zona) && _zonasGeradas.ContainsKey(zona.Hash), $"{pl.Zone}");
		GcConferir(pl, $"pousado, o chao puxa {p.G:0}x", p.G);
		AfirmarGc("...e o servidor DISSE isso a ele (\"o chao de ... puxa ... vezes mais forte\")", Disse(falas, "puxa"), Ultimos(falas));
		double efetivaPousado = pl.Ficha.FatorDaGravidade();

		// (b) MORRE, E SOBE.
		GcMorrerESubir(pl);
		AfirmarGc("ele morre e vai pro Outro Mundo", pl.Ficha.dead && Alem.EhOAlem(pl.Zone), $"{pl.Zone}");
		GcConferir(pl, "no Outro Mundo o chao e 1x", 1);

		// (c) OS DOIS VERBS DO RELATO, com o mundo ja VIVO.
		(falas, bool porOrbita) = GcVoltarPeloSpawn(pl);
		AfirmarGc("`admin_reviver` + `admin_spawn`: vivo, e de volta ao mundo dele", !pl.Ficha.dead && pl.Zone.Equals(zona), $"{pl.Zone}");
		AfirmarGc("...pelo ATALHO do mundo ja vivo: sem passar pela orbita (e a volta em que o dono viu 1x)", !porOrbita);
		GcConferir(pl, $"**DEPOIS DE MORRER E VOLTAR PELO `spawn`, O PLANETA DE {p.G:0}x CONTINUA {p.G:0}x**", p.G);
		AfirmarGc("...e o servidor avisa do peso de novo (ele vinha do 1x do Outro Mundo)", Disse(falas, "puxa"), Ultimos(falas));
		AfirmarGc("...e a gravidade que o TREINO enxerga e a mesma de antes de morrer (o numero da tela e o do jogo)",
				  Math.Abs(pl.Ficha.FatorDaGravidade() - efetivaPousado) < 1e-9 && Math.Abs(Esmagamento.Gravidade(pl.Ficha) - p.G) < 1e-9,
				  $"fator do treino {efetivaPousado:0.##} -> {pl.Ficha.FatorDaGravidade():0.##}");
		float ganhoDaVolta = _gcGanhoNaTela;

		// (d) O DEFEITO: a mesma volta, com o funil lendo so o catalogo.
		GravidadeSoDoCatalogoDeTeste = true;
		try
		{
			GcMorrerESubir(pl);
			GcVoltarPeloSpawn(pl);
			float tela = GcNaTela(pl);
			AfirmarGc($"(defeito injetado: o chao lido so do `planetas.json`) a mesma volta REPROVA -- e o relato do dono: "
					  + $"o planeta de {p.G:0}x \"consta como 1x\"",
					  pl.Zone.Equals(zona) && Math.Abs(pl.Ficha.Planetgrav - 1) < 1e-9 && Math.Abs(tela - 1) < 0.05,
					  $"servidor {pl.Ficha.Planetgrav:0.##}x, na tela {tela:0.##}x");
			AfirmarGc("(defeito injetado) ...e nao era so o mostrador: com 1x na ficha o \"Ganho de BP\" da mesma volta sai MENOR",
					  _gcGanhoNaTela < ganhoDaVolta * 0.97f, $"ganho {ganhoDaVolta:0.###}x -> {_gcGanhoNaTela:0.###}x");
		}
		finally { GravidadeSoDoCatalogoDeTeste = false; }

		AplicarGravidade(pl);
		GcConferir(pl, "desligado o defeito, o funil devolve o chao de verdade", p.G);
	}

	// =====================================================================
	// 3) AS VARIACOES
	// =====================================================================
	/// <summary>
	/// PLANETA SORTEADO E PLANETA DO CATALOGO; SPAWN NO MESMO E EM OUTRO. Em todas a sequencia e a do
	/// relato (morre, sobe, `admin_reviver`, `admin_spawn`), e o que muda e onde se morre e onde fica o spawn.
	/// </summary>
	private void GcAsVariacoes(ServerPlayer pl, MundoDaGravidade p, MundoDaGravidade q)
	{
		GD.Print("[gravidade] -- 3) as variacoes: gerado e catalogo, spawn no mesmo planeta e em outro --");

		Berco deVegeta = Bercos.Onde(SeedDoUniverso, "Saiyan", "Normal", "Saiyan", 1, false);
		Berco daTerra = Bercos.Onde(SeedDoUniverso, "Human", "Normal", "", 1, false);
		AfirmarGc("(montagem) os dois bercos de catalogo sao Vegeta e a Terra", deVegeta.Planeta == "Vegeta" && daTerra.Planeta == "Earth",
				  $"{deVegeta.Planeta} / {daTerra.Planeta}");

		var gP = new ChaoDaGravidade($"{p.P.Nome} (gerado)", p.Zona, p.G, GcBercoEm(p), p);
		var gQ = new ChaoDaGravidade($"{q.P.Nome} (gerado)", q.Zona, q.G, GcBercoEm(q), q);
		var vegeta = new ChaoDaGravidade("Vegeta (catalogo)", deVegeta.Zona, FichaDoChao(deVegeta.Zona).Gravidade, deVegeta, null);
		var terra = new ChaoDaGravidade("Terra (catalogo)", daTerra.Zona, FichaDoChao(daTerra.Zona).Gravidade, daTerra, null);

		(ChaoDaGravidade Morre, ChaoDaGravidade Spawn, string Nota)[] casos =
		[
			(gP, gQ, "o spawn e OUTRO gerado, ainda FRIO"),
			(gQ, gP, "o spawn e outro gerado, ja VIVO"),
			(gP, vegeta, "gerado -> catalogo"),
			(vegeta, vegeta, "catalogo -> o mesmo catalogo (o controle: sempre valeu)"),
			(vegeta, gP, "catalogo -> gerado vivo"),
			(gP, terra, "gerado -> um spawn de 1x (o conserto nao e 'sempre mais que 1')"),
		];

		foreach ((ChaoDaGravidade morre, ChaoDaGravidade spawn, string nota) in casos)
		{
			pl.Berco = spawn.Berco;
			GcIrPara(pl, morre);
			bool chegou = pl.Zone.Equals(morre.Zona) && Math.Abs(pl.Ficha.Planetgrav - morre.G) < 1e-9;

			bool spawnVivo = spawn.Gerado == null || _zonasGeradas.ContainsKey(spawn.Zona.Hash);
			GcMorrerESubir(pl);
			(_, bool porOrbita) = GcVoltarPeloSpawn(pl);

			float tela = GcNaTela(pl);
			AfirmarGc($"morre em {morre.Rotulo} {morre.G:0}x, spawn em {spawn.Rotulo}: volta com {spawn.G:0}x  ({nota})",
					  chegou && pl.Zone.Equals(spawn.Zona) && !pl.Ficha.dead && porOrbita == !spawnVivo
					  && Math.Abs(pl.Ficha.Planetgrav - spawn.G) < 1e-9 && Math.Abs(tela - spawn.G) < 0.05,
					  $"chegou em {morre.Rotulo} com o chao certo={chegou}; voltou pra {pl.Zone} {(porOrbita ? "por orbita" : "direto")}; "
					  + $"servidor {pl.Ficha.Planetgrav:0.##}x, na tela {tela:0.##}x");
		}
	}

	// =====================================================================
	// 4) AS OUTRAS PORTAS
	// =====================================================================
	/// <summary>
	/// TODA PORTA DE ENTRADA NUM MUNDO GERADO. Cada uma e uma funcao que poe um corpo dentro de
	/// <paramref name="p"/> pelo caminho de producao e devolve o `Planetgrav` com que ele ficou; a lista
	/// inteira roda duas vezes -- com o conserto (todas com g) e com o defeito (todas com 1x).
	/// </summary>
	private void GcAsOutrasPortas(ServerPlayer pl, MundoDaGravidade p, List<ServerPlayer> forjados)
	{
		GD.Print($"[gravidade] -- 4) as outras portas de entrada em {p.P.Nome} ({p.G:0}x) --");

		ZoneKey zona = p.Zona;
		ZoneKey terra = ZoneKey.Premade("Earth");
		Vec2 chegada = ChegadaDaZonaGerada(zona) ?? default;
		AfirmarGc("(montagem) o mundo esta VIVO no servidor: e nele vivo que as portas erravam", ChegadaDaZonaGerada(zona) != null);
		pl.Berco = GcBercoEm(p);

		void HostNaTerra() { if (!pl.Zone.Equals(terra)) MoveToZone(pl.Id, terra, PontoDeNascimento(terra)); }
		void HostNoMundo() { if (!pl.Zone.Equals(zona)) MoveToZone(pl.Id, zona, chegada); }

		ServerPlayer alvo = GcForjar("gc: o mandado pro berco", terra, PontoDeNascimento(terra), forjados);
		alvo.Berco = GcBercoEm(p);
		alvo.Ficha.GravMastered = MaestriaDaCobaiaDaGravidade;
		ServerPlayer ancora = GcForjar("gc: quem ja esta la", zona, chegada, forjados);
		ServerPlayer visitante = GcForjar("gc: o trazido", terra, PontoDeNascimento(terra), forjados);

		(string Nome, Func<double> Entra)[] portas =
		[
			("o POUSO vindo de orbita (`PousarEmProcedural`)", () =>
			{
				GcPousar(pl, p.P);
				return pl.Ficha.Planetgrav;
			}),
			("o `admin_spawn` com o mundo vivo (o relato)", () =>
			{
				HostNaTerra();
				Verbo(pl, "admin_spawn", "");
				return pl.Zone.Equals(zona) ? pl.Ficha.Planetgrav : double.NaN;
			}),
			("o `Renascer` (o Enma, as esferas e a tecnica de reviver chamam este)", () =>
			{
				HostNaTerra();
				GcMorrerESubir(pl);
				Renascer(pl);
				return pl.Zone.Equals(zona) && !pl.Ficha.dead ? pl.Ficha.Planetgrav : double.NaN;
			}),
			("o `admin_spawn_alvo` (o admin manda OUTRO pro berco dele)", () =>
			{
				if (!alvo.Zone.Equals(terra)) MoveToZone(alvo.Id, terra, PontoDeNascimento(terra));
				Verbo(pl, "admin_spawn_alvo", alvo.Id.ToString());
				return alvo.Zone.Equals(zona) ? alvo.Ficha.Planetgrav : double.NaN;
			}),
			("o `admin_ir` (ir ate quem esta num mundo gerado)", () =>
			{
				HostNaTerra();
				Verbo(pl, "admin_ir", ancora.Id.ToString());
				return pl.Zone.Equals(zona) ? pl.Ficha.Planetgrav : double.NaN;
			}),
			("o `admin_trazer` (puxar alguem pra um mundo gerado)", () =>
			{
				HostNoMundo();
				if (!visitante.Zone.Equals(terra)) MoveToZone(visitante.Id, terra, PontoDeNascimento(terra));
				Verbo(pl, "admin_trazer", visitante.Id.ToString());
				return visitante.Zone.Equals(zona) ? visitante.Ficha.Planetgrav : double.NaN;
			}),
			("ACORDAR DO TRANSE (a mente e 1x; o corpo ficou no mundo)", () =>
			{
				HostNoMundo();
				pl.Ficha.med = true;
				EntrarNaMente(pl);
				bool mergulhou = NaMente(pl) && Math.Abs(pl.Ficha.Planetgrav - 1) < 1e-9;
				Verbo(pl, "mente_sair", "");
				return mergulhou && pl.Zone.Equals(zona) ? pl.Ficha.Planetgrav : double.NaN;
			}),
			("o `MoveToZone` cru (o funil do selo, da fusao, da nave, do torneio, do teleporte de tecnica e da absorcao)", () =>
			{
				HostNaTerra();
				MoveToZone(pl.Id, zona, chegada);
				return pl.Ficha.Planetgrav;
			}),
			("NASCER nele (`PorNoMundo`: NPC, clone, cadaver)", () =>
				GcForjar("gc: um nativo", zona, chegada, forjados).Ficha.Planetgrav),
		];

		foreach ((string nome, Func<double> entra) in portas)
		{
			double g = entra();
			AfirmarGc($"{nome}: entra com {p.G:0}x", Math.Abs(g - p.G) < 1e-9, double.IsNaN(g) ? "a porta nao abriu" : $"{g:0.##}x");
		}

		// ============================ O PESO VALE PRA QUEM CHEGA DE CARONA ============================
		// A consequencia do conserto que o dono vai sentir: quem um admin PUXA pra um mundo pesado passa a
		// sentir o chao (antes entrava com 1x). Um corpo de maestria 1 num chao de g esta a g vezes o
		// limite dele -- e o esmagamento, que ja valia pra quem pousava, vale igual.
		// ============================================================================================
		AfirmarGc($"o TRAZIDO (maestria 1) sente o chao como quem pousa: esmagamento {p.G:0.#}x do limite dele",
				  Math.Abs(Esmagamento.Razao(visitante.Ficha) - p.G) < 1e-6 && Esmagamento.Esmaga(visitante.Ficha),
				  $"razao {Esmagamento.Razao(visitante.Ficha):0.##}");
		ServerPlayer nativo = GcForjar("gc: outro nativo", zona, chegada, forjados);
		AfirmarGc($"...e quem NASCE nele nasce aclimatado (`race.dm:130-131`): maestria {nativo.Ficha.GravMastered:0.#}, sem esmagamento",
				  nativo.Ficha.GravMastered >= p.G - 1e-9 && !Esmagamento.Esmaga(nativo.Ficha),
				  $"razao {Esmagamento.Razao(nativo.Ficha):0.##}");

		GravidadeSoDoCatalogoDeTeste = true;
		try
		{
			var emUm = new List<string>();
			var fora = new List<string>();
			foreach ((string nome, Func<double> entra) in portas)
			{
				double g = entra();
				(Math.Abs(g - 1) < 1e-9 ? emUm : fora).Add($"{nome.Split('(')[0].Trim()}={g:0.##}");
			}
			AfirmarGc($"(defeito injetado: o chao lido so do `planetas.json`) as MESMAS {portas.Length} portas REPROVAM -- todas entram com 1x",
					  emUm.Count == portas.Length, fora.Count > 0 ? "fora do 1x: " + string.Join(", ", fora) : "");
		}
		finally { GravidadeSoDoCatalogoDeTeste = false; }

		HostNaTerra();
		HostNoMundo();
		GcConferir(pl, "desligado o defeito, entrar de novo devolve o chao de verdade", p.G);
	}

	// =====================================================================
	// 5 e 6) O LOGIN, E O MUNDO FRIO
	// =====================================================================
	/// <summary>
	/// QUEM DESLOGOU NUM MUNDO GERADO. E onde os cinco personagens do save do dono estao hoje: salvos em
	/// `Verdejante-113` com `Planetgrav 1` na ficha, e o servidor, depois de um reinicio, sem o mundo.
	///
	/// O "login" e o da `--mestrevivo` (<see cref="Logar"/>: a sequencia do `Entrar` menos o que vira
	/// pacote), sobre um save que foi ao DISCO e voltou. O que o `Entrar` faz e ele nao -- o
	/// `AoPisarNoMundo` -- e chamado aqui, que e a metade do mundo frio.
	/// </summary>
	private void GcOLoginEOMundoFrio(ServerPlayer pl, MundoDaGravidade p, List<MundoDaGravidade> mundos,
									 List<ServerPlayer> forjados, HashSet<int> cadaveresDeAntes)
	{
		GD.Print($"[gravidade] -- 5) o login de quem deslogou em {p.P.Nome} --");
		ZoneKey zona = p.Zona;
		ZoneKey terra = ZoneKey.Premade("Earth");
		if (_store == null) { AfirmarGc("(montagem) o servidor tem pasta de contas", false); return; }

		if (!pl.Zone.Equals(zona)) MoveToZone(pl.Id, zona, ChegadaDaZonaGerada(zona) ?? default);

		// O SAVE VAI AO DISCO E VOLTA (o `Persistir` do logout), e na volta a ficha e posta como as do dono:
		// `Planetgrav` 1 num mundo que nao e 1x.
		var conta = new AccountSave { Conta = ContaDaBancadaDaGravidade, CriadaEm = NowMs() };
		conta.Slots[0] = AccountStore.DeJogador(pl, NowMs());
		_store.Gravar(conta);
		AccountSave? doDisco = _store.Carregar(ContaDaBancadaDaGravidade);
		CharacterSave? salvo = doDisco?.Slots[0];
		AfirmarGc("o save guarda a zona INTEIRA de um mundo gerado: tipo, nome e seed",
				  salvo is { ZonaTipo: ZoneKey.KindProcedural } && salvo.Zona == p.P.Nome && salvo.ZonaSeed == p.P.Seed,
				  salvo == null ? "nao voltou do disco" : $"{salvo.ZonaTipo}/{salvo.Zona}/{salvo.ZonaSeed}");
		if (doDisco == null || salvo == null) return;
		salvo.Ficha.Planetgrav = 1;
		_store.Gravar(doDisco);

		// UMA PAREDE DE BASE NESTE MUNDO: e ela que a familia 6 procura na colisao do mundo que renasce com gente
		// dentro. (A camada das construcoes nao serve de regua: o pomar, ao nascer, ja reescreve aquela.)
		Vec2 chegadaDeAntes = ChegadaDaZonaGerada(zona) ?? default;
		(int X, int Y)? naParede = GcErguerParede(zona, chegadaDeAntes);
		AfirmarGc("(montagem) com o mundo vivo, uma parede de base erguida nele e parede no servidor",
				  naParede is { } viva && MapaDaZona(zona)?.BlockedCell(viva.X, viva.Y) == true);

		// O SERVIDOR "REINICIA": todo mundo sai do planeta e a PODA de producao o solta do cache.
		MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
		foreach (ServerPlayer f in forjados.ToList()) GcRecolher(f);
		forjados.Clear();
		GcTirarCadaveres(cadaveresDeAntes);
		bool frio = GcEsfriar(zona, mundos);
		AfirmarGc("(montagem) o mundo esta FRIO de novo: a poda de producao o soltou, como um reinicio solta",
				  frio && MapaDaZona(zona) == null);
		if (!frio) return;

		ServerPlayer relogado = Logar(ContaDaBancadaDaGravidade, 0, 0);
		forjados.Add(relogado);
		relogado.Pos = new Vec2(salvo.X, salvo.Y);
		AfirmarGc($"**QUEM LOGA NUM MUNDO GERADO ENCONTRA O CHAO DELE**: {p.G:0}x, com o save dizendo 1x e o mundo frio no servidor",
				  relogado.Zone.Equals(zona) && Math.Abs(relogado.Ficha.Planetgrav - p.G) < 1e-9,
				  $"{relogado.Zone}: {relogado.Ficha.Planetgrav:0.##}x");

		GravidadeSoDoCatalogoDeTeste = true;
		try
		{
			AplicarGravidade(relogado);
			AfirmarGc("(defeito injetado: o chao lido so do `planetas.json`) o mesmo login REPROVA -- acorda em 1x",
					  Math.Abs(relogado.Ficha.Planetgrav - 1) < 1e-9, $"{relogado.Ficha.Planetgrav:0.##}x");
		}
		finally { GravidadeSoDoCatalogoDeTeste = false; }
		AplicarGravidade(relogado);

		// --------------------------------------------------------------------- 6) O MUNDO FRIO
		GD.Print("[gravidade] -- 6) o mundo frio: quem acorda nele o faz nascer no servidor --");

		// PRIMEIRO O DEFEITO, que e o estado de antes: ninguem encomenda, e o corpo fica preso no planeta.
		MundoFrioFicaFrioDeTeste = true;
		try
		{
			AoPisarNoMundo(relogado);
			GcColher(zona, voltas: 40);
			List<string> recusa = Ouvir(() => Decolar(relogado));
			AfirmarGc("(defeito injetado: ninguem acorda o mundo) o login REPROVA -- sem colisao, e a decolagem e recusada: "
					  + "\"este lugar nao fica no mapa do universo\"",
					  MapaDaZona(zona) == null && relogado.Zone.Equals(zona) && Disse(recusa, "nao fica no mapa do universo"),
					  Ultimos(recusa));
		}
		finally { MundoFrioFicaFrioDeTeste = false; }

		// AGORA O QUE O `Entrar` FAZ: a mesma chamada, e o mundo nasce fora do tique. (O segundo defeito fica
		// ligado SO nesta colheita: o mundo nasce, mas nasce nu. A colheita certa e a da segunda porta, la embaixo.)
		MundoAcordadoNasceNuDeTeste = true;
		try
		{
			AoPisarNoMundo(relogado);
			AfirmarGc("pisar num mundo frio o ENCOMENDA (fora do tique, como no pouso)",
					  _gerando.ContainsKey(zona.Hash) || _zonasGeradas.ContainsKey(zona.Hash));
			bool nasceu = GcColher(zona, voltas: 900);
			AfirmarGc("...e ele nasce no servidor: ha colisao pra validar o passo", nasceu && MapaDaZona(zona) != null);
			AfirmarGc("(defeito injetado: o mundo que nasce com gente dentro nao se veste) a colheita REPROVA -- a parede de "
					  + "base que o cliente desenha NAO e parede no servidor",
					  naParede is { } nua && MapaDaZona(zona)?.BlockedCell(nua.X, nua.Y) == false);
		}
		finally { MundoAcordadoNasceNuDeTeste = false; }

		PlanetaNoEspaco? noCeu = CorpoDaZona(zona);
		AfirmarGc("...com o corpo celeste CERTO (o mesmo que o pouso registraria): e pra la que a decolagem devolve",
				  noCeu is { } c && c.Seed == p.P.Seed && c.Nome == p.P.Nome && c.Pos.Equals(p.P.Pos) && c.Raio == p.P.Raio,
				  noCeu is { } c2 ? $"{c2.Nome} #{c2.Seed} @ ({c2.Pos.X:0},{c2.Pos.Y:0})" : "nulo");
		AfirmarGc("...e o chao que ele gerou e o que o cliente desenha (mesma chegada do gerador)",
				  ChegadaDaZonaGerada(zona) is { } ch
				  && ch.Equals(GeradorDeTerreno.Gerar(MundoProcedural.DaSeed(p.P.Seed, p.P.Nome).Parametros()).Spawn));

		AoPisarNoMundo(relogado);
		AfirmarGc("pisar de novo num mundo ja vivo nao encomenda nada", !_gerando.ContainsKey(zona.Hash));

		List<string> falas = Ouvir(() => Decolar(relogado));
		AfirmarGc("**QUEM LOGOU NUM MUNDO GERADO CONSEGUE DECOLAR DELE**",
				  Espaco.EhEspaco(relogado.Zone) && relogado.PlanetaDeOrigem == p.P.Nome, $"{relogado.Zone} | {Ultimos(falas)}");
		GcConferir(relogado, "...e em orbita a gravidade volta a zero", 0, naTela: false);

		// ============================ A SEGUNDA PORTA: O `MoveToZone` PRA UM MUNDO FRIO ============================
		// E a saida de uma nave, de um selo ou de uma mente pra um mundo que a poda ja soltou (ou, depois de um
		// reinicio, de quem logou DENTRO da nave pousada nele). Mesmo gancho do login -- o `AoPisarNoMundo` e
		// chamado pelas duas portas --, e aqui a colheita roda com o conserto inteiro: o mundo nasce VESTIDO.
		// ========================================================================================================
		GcRecolher(relogado);
		forjados.Remove(relogado);
		bool frioDeNovo = GcEsfriar(zona, mundos);
		ServerPlayer viajante = GcForjar("gc: quem sai de uma nave", terra, PontoDeNascimento(terra), forjados);
		MoveToZone(viajante.Id, zona, chegadaDeAntes);
		bool acordou = GcColher(zona, voltas: 900);
		AfirmarGc("a SEGUNDA porta (`MoveToZone` pra um mundo frio: a saida de uma nave, de um selo, de uma mente) tambem o acorda",
				  frioDeNovo && acordou && MapaDaZona(zona) != null, $"frio antes={frioDeNovo}, vivo depois={acordou}");
		AfirmarGc("**E O MUNDO QUE NASCE COM GENTE DENTRO NASCE VESTIDO**: a parede de base e parede no servidor, como no cliente",
				  naParede is { } vestida && MapaDaZona(zona)?.BlockedCell(vestida.X, vestida.Y) == true);
		AfirmarGc($"...e quem entrou assim sente o chao dele ({p.G:0}x)", Math.Abs(viajante.Ficha.Planetgrav - p.G) < 1e-9,
				  $"{viajante.Ficha.Planetgrav:0.##}x");
	}

	/// <summary>
	/// A VOLTA DA ZONA PRO CEU, MUNDO A MUNDO (<see cref="CorpoGerado.DaZona"/>). E quem vigia o formato do
	/// rotulo dos mundos: se ele mudar em `SistemaSolar.Planeta`, a volta passa a devolver nulo calada e o
	/// mundo frio volta a ficar frio -- esta contagem e o alarme.
	/// </summary>
	private void GcAVoltaDaZonaProCeu(List<MundoDaGravidade> mundos)
	{
		int certos = 0;
		var homonimos = new Dictionary<string, MundoDaGravidade>(StringComparer.Ordinal);
		(MundoDaGravidade A, MundoDaGravidade B)? par = null;
		string primeiroErro = "";

		foreach (MundoDaGravidade m in mundos)
		{
			if (CorpoGerado.DaZona(SeedDoUniverso, m.Zona) is { } v
				&& v.Sx == m.Sx && v.Sy == m.Sy && v.K == m.K
				&& v.Corpo.Seed == m.P.Seed && v.Corpo.Nome == m.P.Nome && v.Corpo.Pos.Equals(m.P.Pos) && v.Corpo.Raio == m.P.Raio)
				certos++;
			else if (primeiroErro.Length == 0) primeiroErro = $"{m.P.Nome} [{m.Sx}:{m.Sy}]k{m.K}";

			if (par == null && homonimos.TryGetValue(m.P.Nome, out MundoDaGravidade gemeo) && gemeo.P.Seed != m.P.Seed) par = (gemeo, m);
			homonimos[m.P.Nome] = m;
		}
		AfirmarGc($"a volta da zona pro corpo celeste acha o endereco EXATO de cada um dos {mundos.Count} mundos da carta",
				  certos == mundos.Count && mundos.Count > 100, primeiroErro.Length > 0 ? $"{certos} de {mundos.Count}; o primeiro que falhou: {primeiroErro}" : "");

		if (par is { } h)
			AfirmarGc($"...inclusive dois HOMONIMOS ({h.A.P.Nome} em [{h.A.Sx}:{h.A.Sy}] e em [{h.B.Sx}:{h.B.Sy}]): cada seed acha o SEU corpo",
					  CorpoGerado.DaZona(SeedDoUniverso, h.A.Zona) is { } va && va.Sx == h.A.Sx && va.Sy == h.A.Sy
					  && CorpoGerado.DaZona(SeedDoUniverso, h.B.Zona) is { } vb && vb.Sx == h.B.Sx && vb.Sy == h.B.Sy);
		else GD.Print("[gravidade]         (nenhum par de homonimos na carta desta rodada: a prova fica com a conferencia da seed)");

		MundoDaGravidade um = mundos[0];
		bool seedTrocada = CorpoGerado.DaZona(SeedDoUniverso, ZoneKey.Procedural(um.P.Nome, um.P.Seed ^ 1)) == null;
		bool feitoAMao = CorpoGerado.DaZona(SeedDoUniverso, ZoneKey.Premade("Earth")) == null;
		bool oEspaco = CorpoGerado.DaZona(SeedDoUniverso, ZonaDoEspaco) == null;
		bool deBancada = CorpoGerado.DaZona(SeedDoUniverso, ZoneKey.Procedural("Mundo de Bancada", um.P.Seed)) == null;
		AfirmarGc("...e ela NAO inventa corpo: seed trocada, mapa feito a mao, o espaco e uma zona de bancada devolvem nulo",
				  seedTrocada && feitoAMao && oEspaco && deBancada,
				  $"seed trocada={seedTrocada} feito a mao={feitoAMao} espaco={oEspaco} bancada={deBancada}");

		// O MUNDO DE OUTRO UNIVERSO (o save trazido de um servidor com outra semente). A cobaia tem que ser de
		// um sistema SORTEADO: as irmas de orbita de um pre-feito saem da ancora dele, e nao da semente do
		// mundo -- sao o mesmo corpo em todo universo (e por isso que a tela de criacao as lista sem conexao).
		MundoDaGravidade? sorteado = null;
		foreach (MundoDaGravidade m in mundos)
			if (Sistemas.Do(SeedDoUniverso, m.Sx, m.Sy) is { Ancorado: false }) { sorteado = m; break; }
		AfirmarGc("...nem acha, neste universo, o mundo de OUTRA semente de universo (a seed do planeta e a prova)",
				  sorteado is { } s && CorpoGerado.DaZona(SeedDoUniverso ^ 0x5A5A, s.Zona) == null,
				  sorteado is { } s2 ? $"{s2.P.Nome} [{s2.Sx}:{s2.Sy}]k{s2.K}" : "nao ha sistema sorteado na carta");
	}

	// =====================================================================
	// 7) O REFUGIADO
	// =====================================================================
	/// <summary>
	/// QUEM PERDEU O PLANETA NATAL NAO ACORDA ESMAGADO NO REFUGIO -- e o estado do save do dono (a Terra
	/// dele foi destruida, e o refugio do personagem dele e um mundo de 4x).
	///
	/// UM PLANETA MORRE DE VERDADE AQUI, pelo funil de producao e dentro do palco de bancada (que devolve o
	/// registro dos mortos no fim): um mundo sorteado VAZIO faz de natal, e o refugio e o mundo que a
	/// vizinhanca DELE sorteia. Os dois sao escolhidos antes de qualquer morte, com a mesma funcao pura que
	/// o refugio usa (`Refugios.MundosPertoDe`), pra a familia nao depender de sorte.
	///
	/// As duas metades que dao sentido a afirmacao: o VISITANTE e o refugiado FORA do refugio dele continuam
	/// sentindo o chao inteiro -- e, fechado o palco (berco de pe), o mesmo mundo deixa de acostumar quem chega.
	/// </summary>
	private void GcORefugiado(MundoDaGravidade p, List<MundoDaGravidade> mundos, List<ServerPlayer> forjados)
	{
		GD.Print("[gravidade] -- 7) o refugiado: quem perdeu o planeta natal nao acorda esmagado no refugio --");
		ZoneKey terra = ZoneKey.Premade("Earth");

		MundoDaGravidade? oNatal = null;
		PlanetaNoEspaco oRefugio = default;
		foreach (MundoDaGravidade n in mundos)
		{
			if (_zonasGeradas.ContainsKey(n.Zona.Hash) || _gerando.ContainsKey(n.Zona.Hash)) continue;   // nao se mata um mundo em uso
			if (n.P.Nome == p.P.Nome) continue;

			// A VIZINHANCA COMO ELA FICARA DEPOIS DA MORTE DELE: a conta do refugio, com este mundo ja fora.
			Arredores semEle = Refugios.MundosPertoDe(SeedDoUniverso, n.P.Pos,
				c => c.Seed != n.P.Seed && !PlanetaMorto(c) && (!c.Premade || _catalogo?.Get(ZoneKey.Premade(c.Nome)) != null));
			foreach (MundoDeRefugio m in semEle.Mundos)
			{
				double peso = m.Corpo.Premade ? 0 : MundoProcedural.GravidadeDaSeed(m.Corpo.Seed);
				// pesa mais do que um novato domina, e nao e homonimo do natal (o nome igual ja aclimatava)
				if (peso < 2 || m.Corpo.Seed == p.P.Seed || m.Corpo.Nome == n.P.Nome) continue;
				oNatal = n;
				oRefugio = m.Corpo;
				break;
			}
			if (oNatal != null) break;
		}
		AfirmarGc("(montagem) a carta tem um mundo vazio cuja vizinhanca oferece um refugio de 2x ou mais",
				  oNatal != null, oNatal is { } n0 ? $"natal {n0.P.Nome}, refugio {oRefugio.Nome}" : "nao achei");
		if (oNatal is not { } natal) return;

		double g = MundoProcedural.GravidadeDaSeed(oRefugio.Seed);
		ZoneKey zonaDoRefugio = Espaco.ZonaDe(oRefugio);

		// O BERCO NO MUNDO QUE VAI MORRER. O natal dele e o proprio mundo: a ancora do refugio e a posicao dele.
		Berco bercoMorto = BercoDoRefugio(new MundoDeRefugio(natal.P, natal.Sx, natal.Sy, natal.K, 0, true), natal.P.Nome);
		ServerPlayer Refugiado(string nome, ulong semente)
		{
			ServerPlayer r = GcForjar(nome, terra, PontoDeNascimento(terra), forjados);
			r.Berco = bercoMorto;
			r.SeedDoBerco = semente;
			return r;
		}

		ServerPlayer depois;
		using (PalcoDeMortesDeBancada())
		{
			bool morreu = MatarPlanetaNoPalco(natal.Zona, "gravidadeteste: o natal de mentira");
			AfirmarGc($"(montagem) o natal de mentira ({natal.P.Nome}) foi DESTRUIDO pelo funil de producao, dentro do palco de bancada",
					  morreu && ZonaMorta(natal.Zona));
			if (!morreu) return;

			// A SEMENTE DE PERSONAGEM QUE SORTEIA AQUELE MUNDO (o sorteio e uniforme sobre no maximo tres).
			Arredores perto = VizinhancaDeRefugio(natal.P.Pos);
			ulong semente = 0;
			for (ulong s = 1; s <= 4000 && semente == 0; s++)
				if (perto.Sorteia(s) is { } sorteado && sorteado.Corpo.Seed == oRefugio.Seed) semente = s;
			AfirmarGc($"(montagem) a vizinhanca do natal morto sorteia {oRefugio.Nome} ({g:0}x) pra um personagem",
					  semente != 0, $"{perto.Mundos.Count} mundo(s) na vizinhanca");
			if (semente == 0) return;

			ServerPlayer refugiado = Refugiado("gc: o refugiado", semente);
			AfirmarGc("ele PERDEU o berco, e o refugio dele e esse mundo (a mesma escolha que o `DestinoDe` faz, sem os avisos)",
					  PerdeuOBerco(refugiado) && ZonaDoRefugioDe(refugiado) is { } z && z.Equals(zonaDoRefugio));

			List<string> falas = Ouvir(() =>
			{
				MandarProBerco(refugiado);
				if (Espaco.EhEspaco(refugiado.Zone)) PousarDeVerdade(refugiado);
			});
			AfirmarGc($"mandado pro ponto de partida, ele acorda no REFUGIO ({oRefugio.Nome}, {g:0}x), e o jogo conta por que",
					  refugiado.Zone.Equals(zonaDoRefugio) && Math.Abs(refugiado.Ficha.Planetgrav - g) < 1e-9 && Disse(falas, "nao existe mais"),
					  $"{refugiado.Zone} {refugiado.Ficha.Planetgrav:0.##}x | {Ultimos(falas)}");
			AfirmarGc($"**O REFUGIADO NAO ACORDA ESMAGADO**: a maestria dele vai de 1 a {refugiado.Ficha.GravMastered:0.#}, o limite num chao de {g:0}x",
					  refugiado.Ficha.GravMastered >= g - 1e-9 && !Esmagamento.Esmaga(refugiado.Ficha),
					  $"razao {Esmagamento.Razao(refugiado.Ficha):0.##}");

			RefugioNaoAclimataDeTeste = true;
			try
			{
				ServerPlayer cru = Refugiado("gc: o refugiado sem o conserto", semente);
				MandarProBerco(cru);
				if (Espaco.EhEspaco(cru.Zone)) PousarDeVerdade(cru);
				AfirmarGc($"(defeito injetado: so o berco de nascimento aclimata) o mesmo refugiado REPROVA -- acorda a {g:0.#}x do limite dele"
						  + (g >= Esmagamento.RazaoQuePrende ? ", PRESO no chao" : ", perdendo vida e passo"),
						  cru.Zone.Equals(zonaDoRefugio) && Math.Abs(Esmagamento.Razao(cru.Ficha) - g) < 1e-6 && Esmagamento.Esmaga(cru.Ficha),
						  $"{cru.Zone}, razao {Esmagamento.Razao(cru.Ficha):0.##}");
			}
			finally { RefugioNaoAclimataDeTeste = false; }

			// SO O REFUGIO DELE, E SO PRA ELE.
			Vec2 chegada = ChegadaDaZonaGerada(zonaDoRefugio) ?? default;
			ServerPlayer visita = GcForjar("gc: a visita", terra, PontoDeNascimento(terra), forjados);
			MoveToZone(visita.Id, zonaDoRefugio, chegada);
			AfirmarGc("quem so VISITA o refugio alheio sente o chao inteiro (chegar num mundo pesado nao acostuma ninguem)",
					  Math.Abs(visita.Ficha.GravMastered - 1) < 1e-9 && Math.Abs(Esmagamento.Razao(visita.Ficha) - g) < 1e-6,
					  $"maestria {visita.Ficha.GravMastered:0.#}, razao {Esmagamento.Razao(visita.Ficha):0.##}");

			if (ChegadaDaZonaGerada(p.Zona) is { } naOutra)
			{
				ServerPlayer longe = Refugiado("gc: o refugiado noutro mundo", semente);
				MoveToZone(longe.Id, p.Zona, naOutra);
				AfirmarGc($"...e o refugiado so se acostuma ao refugio DELE: em {p.P.Nome} ({p.G:0}x) continua com maestria 1",
						  Math.Abs(longe.Ficha.GravMastered - 1) < 1e-9 && Math.Abs(Esmagamento.Razao(longe.Ficha) - p.G) < 1e-6,
						  $"maestria {longe.Ficha.GravMastered:0.#}, razao {Esmagamento.Razao(longe.Ficha):0.##}");
			}
			else AfirmarGc($"(montagem) {p.P.Nome} esta vivo pra servir de 'outro mundo'", false);

			depois = Refugiado("gc: o que chega depois", semente);
		}

		// O PALCO FECHOU: o natal de mentira existe de novo, e o mundo ao lado volta a ser so um mundo ao lado.
		AfirmarGc("(montagem) fechado o palco, o natal de mentira voltou a existir", !ZonaMorta(natal.Zona));
		MoveToZone(depois.Id, zonaDoRefugio, ChegadaDaZonaGerada(zonaDoRefugio) ?? default);
		AfirmarGc("...e com o berco DE PE o mesmo mundo nao e refugio de ninguem: quem chega nele nao e aclimatado",
				  !PerdeuOBerco(depois) && Math.Abs(depois.Ficha.GravMastered - 1) < 1e-9 && Esmagamento.Esmaga(depois.Ficha),
				  $"maestria {depois.Ficha.GravMastered:0.#}, razao {Esmagamento.Razao(depois.Ficha):0.##}");
	}

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// OS MUNDOS SORTEADOS EM VOLTA DA ORIGEM, do anel de dentro pro de fora. A carta e funcao pura da
	/// semente desta rodada -- nada aqui e cravado.
	/// </summary>
	private List<MundoDaGravidade> GcMundosDaCarta(int raio)
	{
		var l = new List<MundoDaGravidade>();
		for (int anel = 0; anel <= raio; anel++)
			for (int sx = -anel; sx <= anel; sx++)
				for (int sy = -anel; sy <= anel; sy++)
				{
					if (Math.Max(Math.Abs(sx), Math.Abs(sy)) != anel) continue;
					if (Sistemas.Do(SeedDoUniverso, sx, sy) is not { } s) continue;
					for (int k = 0; k < s.Orbitas; k++)
					{
						if (k == s.OrbitaPreFeita) continue;
						PlanetaNoEspaco p = s.Planeta(k);
						if (p.Premade || ZonaMorta(Espaco.ZonaDe(p))) continue;
						l.Add(new MundoDaGravidade(p, sx, sy, k, MundoProcedural.GravidadeDaSeed(p.Seed)));
					}
				}
		return l;
	}

	/// <summary>O berco de quem tem este mundo sorteado como ponto de partida -- o mesmo objeto que o refugio entrega ao `PousarNo`.</summary>
	private static Berco GcBercoEm(MundoDaGravidade m) =>
		BercoDoRefugio(new MundoDeRefugio(m.P, m.Sx, m.Sy, m.K, 0, true), "Earth");

	/// <summary>POUSA pelo caminho de verdade: o corpo vai pra orbita, em cima do disco, e o `TickDoEspaco` desce.</summary>
	private bool GcPousar(ServerPlayer pl, PlanetaNoEspaco p)
	{
		MoveToZone(pl.Id, ZonaDoEspaco, p.Pos);
		pl.ChunkAtual = ChunkId.De(pl.Pos);
		return PousarDeVerdade(pl) > 0 && pl.Zone.Equals(Espaco.ZonaDe(p));
	}

	/// <summary>Leva o host a um lugar: pelo pouso quando o mundo sorteado esta frio, direto quando ja esta vivo ou e de catalogo.</summary>
	private void GcIrPara(ServerPlayer pl, ChaoDaGravidade chao)
	{
		if (pl.Zone.Equals(chao.Zona)) return;
		if (chao.Gerado is not { } g) { MoveToZone(pl.Id, chao.Zona, PontoDeNascimento(chao.Zona)); return; }
		if (ChegadaDaZonaGerada(chao.Zona) is { } chegada) MoveToZone(pl.Id, chao.Zona, chegada);
		else GcPousar(pl, g.P);
	}

	/// <summary>MORRE E SOBE: `Morrer()` (o funil de toda morte) e a triagem que o tique chama quando o prazo do cadaver vence.</summary>
	private void GcMorrerESubir(ServerPlayer pl)
	{
		pl.Combate.Morrer(ignorarSeguro: true);
		pl.RelogioDaMorte = 0;
		VenceuOPrazoDaMorte(pl);
	}

	/// <summary>
	/// A VOLTA DO RELATO: os dois verbs que o dono apertou, pelo despacho de producao. Se o berco estiver
	/// frio o corpo cai em orbita, e o pouso e o do tique. Devolve o que o servidor disse e por onde voltou.
	/// </summary>
	private (List<string> Falas, bool PorOrbita) GcVoltarPeloSpawn(ServerPlayer pl)
	{
		List<string> falas = Ouvir(() =>
		{
			Verbo(pl, "admin_reviver", pl.Id.ToString());
			Verbo(pl, "admin_spawn", "");
		});
		bool porOrbita = Espaco.EhEspaco(pl.Zone);
		if (porOrbita) falas.AddRange(Ouvir(() => PousarDeVerdade(pl)));
		return (falas, porOrbita);
	}

	/// <summary>
	/// O QUE O HOST TEM NA TELA: a gravidade do ultimo `S2C.Atributos` que SAIU pra ele.
	///
	/// `MandarAtributos` e o passo que o `TickCombate` da por tique pra quem tem tela, e ele so emite
	/// quando a assinatura muda. A assinatura NAO e apagada aqui de proposito: se a gravidade mudasse sem
	/// o pacote sair, o numero da tela ficaria o velho -- e e esse numero que a bancada compara.
	/// </summary>
	private float GcNaTela(ServerPlayer pl)
	{
		MandarAtributos(pl);
		if (EscutaDeAtributos is { Count: > 0 } saiu)
		{
			_gcNaTela = saiu[^1].Gravidade;
			_gcGanhoNaTela = saiu[^1].GanhoDeTreino;
			saiu.Clear();
		}
		return _gcNaTela;
	}

	/// <summary>O chao deste corpo e <paramref name="esperado"/> -- na ficha do servidor e (pro host) no que saiu pro cliente.</summary>
	private void GcConferir(ServerPlayer pl, string oque, double esperado, bool naTela = true)
	{
		float tela = naTela ? GcNaTela(pl) : (float)esperado;
		AfirmarGc(oque, Math.Abs(pl.Ficha.Planetgrav - esperado) < 1e-9 && Math.Abs(tela - esperado) < 0.05,
				  $"servidor {pl.Ficha.Planetgrav:0.##}x" + (naTela ? $", na tela {tela:0.##}x" : "") + $" em {pl.Zone}");
	}

	/// <summary>Um corpo sem dono, NOVATO (maestria 1), posto no mundo pela porta de sempre (`PorNoMundo`).</summary>
	private ServerPlayer GcForjar(string nome, ZoneKey zona, Vec2 onde, List<ServerPlayer> forjados)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDaGravidadeDeTeste + _gcCorpos++,
			Peer = null,
			Name = nome,
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = zona,
			Pos = onde,
			Conta = "bancada_gravidade",
			Slot = 0,
			LastInputMs = NowMs(),
			Ficha = new Fighter { Race = "Human", BP = 1000 },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Tick(agoraMs: NowMs());
		forjados.Add(novo);
		return novo;
	}

	/// <summary>Tira um corpo da bancada do mundo, com o mesmo esquecimento do logout.</summary>
	private void GcRecolher(ServerPlayer f)
	{
		_npcsPraTirar.Remove(f);
		EsquecerTecnicas(f.Id);
		_players.Remove(f.Id);
		ZoneList(f.Zone.Hash).Remove(f);
	}

	/// <summary>Os cadaveres que existem agora (cada morte do host deixa um, e cadaver nao tem prazo).</summary>
	private HashSet<int> GcCadaveres()
	{
		var ids = new HashSet<int>();
		foreach (List<ServerPlayer> zona in _zones.Values)
			foreach (ServerPlayer c in zona)
				if (c.ECadaver) ids.Add(c.Id);
		return ids;
	}

	/// <summary>Desfaz, pela porta de producao, os cadaveres que NAO estavam la antes da bancada.</summary>
	private void GcTirarCadaveres(HashSet<int> deAntes)
	{
		foreach (List<ServerPlayer> zona in _zones.Values.ToList())
			foreach (ServerPlayer c in zona.ToList())
				if (c.ECadaver && !deAntes.Contains(c.Id)) DesfazerOCadaver(c);
	}

	/// <summary>
	/// ESFRIA UM MUNDO PELO CAMINHO DE PRODUCAO. Nao ha "esquecer este mundo": quem solta um mundo vazio
	/// e a poda (`PodarZonasVazias`), que roda na colheita de cada mundo novo e so corta com o cache no
	/// teto. Entao a bancada faz o que um servidor movimentado faz -- nascem outros mundos, ate a poda passar.
	/// </summary>
	private bool GcEsfriar(ZoneKey alvo, List<MundoDaGravidade> mundos)
	{
		int nascidos = 0;
		foreach (MundoDaGravidade m in mundos.OrderBy(o => o.G))   // os leves primeiro: sao os menores, e nascem em poucos ms
		{
			if (!_zonasGeradas.ContainsKey(alvo.Hash) || nascidos > TetoDeZonasGeradas + 2) break;
			if (m.Zona.Equals(alvo) || _zonasGeradas.ContainsKey(m.Zona.Hash)) continue;
			ZonaGeradaPronta(m.P);
			nascidos++;
		}
		return !_zonasGeradas.ContainsKey(alvo.Hash);
	}

	/// <summary>A parede que a familia 6 ergueu num mundo sorteado. Sai no `finally`.</summary>
	private BlocoDePe? _gcParede;

	/// <summary>
	/// UMA PAREDE DE BASE numa celula livre a poucos tiles de <paramref name="perto"/>, guardada e posta no mapa
	/// pelas mesmas duas funcoes do `ErguerBloco` (`GuardarBloco` + `RefazerNoMapa`). O custo e as recusas de
	/// construir ficam de fora de proposito: o que a familia 6 mede e a CAMADA no mapa que renasce, e nao a obra.
	/// </summary>
	private (int X, int Y)? GcErguerParede(ZoneKey zona, Vec2 perto)
	{
		if (MapaDaZona(zona) is not { } mapa) return null;
		BlocoDef? def = _blocos?.Todos.FirstOrDefault(d => d.Classe == ClasseDeBloco.Parede);
		if (def == null) return null;

		int cx0 = (int)(perto.X / ZoneCollision.TileSize), cy0 = (int)(perto.Y / ZoneCollision.TileSize);
		for (int d = 4; d < 40; d++)
			for (int dx = -d; dx <= d; dx++)
			{
				int cx = cx0 + dx, cy = cy0 + d;
				if (!mapa.ServeDeChao(cx, cy) || mapa.BlockedCell(cx, cy)) continue;
				if (BlocoEm(zona, cx, cy) != null || ObraNaCelula(zona, cx, cy) != null) continue;

				var b = new BlocoDePe
				{
					Tipo = def.Id, Def = def,
					ZonaTipo = zona.Kind, ZonaNome = zona.Name, ZonaSeed = zona.Seed,
					X = cx, Y = cy,
					DonoConta = "bancada_gravidade", DonoNome = "bancada",
					Resistencia = 1e9,
				};
				GuardarBloco(b);
				RefazerNoMapa(BaseDe(zona), b);
				_gcParede = b;
				return (cx, cy);
			}
		return null;
	}

	/// <summary>Colhe as geracoes como o tique colhe, ate este mundo estar vivo (ou as voltas acabarem).</summary>
	private bool GcColher(ZoneKey zona, int voltas)
	{
		for (int i = 0; i < voltas && !_zonasGeradas.ContainsKey(zona.Hash); i++)
		{
			TickDasGeracoes();
			if (_gerando.Count > 0) System.Threading.Thread.Sleep(1);
		}
		return _zonasGeradas.ContainsKey(zona.Hash);
	}

	/// <summary>A conta do relog e gravada de verdade; ela sai no fim, mesmo se a bancada estourar.</summary>
	private void GcApagarAConta()
	{
		if (_store == null) return;
		try
		{
			string caminho = System.IO.Path.Combine(_store.Pasta, AccountStore.NomeDeArquivo(ContaDaBancadaDaGravidade) + ".json");
			if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
		}
		catch (Exception e) { GD.PushWarning($"[gravidade] nao consegui apagar a conta de bancada: {e.Message}"); }
	}
}
