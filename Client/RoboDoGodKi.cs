using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Verbos;
using Jandirus.Core.World;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// A BANCADA DE TELA DO KI DIVINO (`--diaggodki`) -- o que o JOGADOR ve do despertar.
///
/// A irma de servidor (`--godkiteste`) prova a regra: quem acumula, quanto, e o que o despertar escreve. Esta
/// prova a outra metade do pedido do dono -- *"o jogador precisa SABER que esta num"* --, do lado de ca do fio:
///
///   F1  o mortal comum, onde nasceu: nenhuma linha de atmosfera, nenhum cartao, nenhum botao de desafio -- e
///       as duas pontas concordam sobre o lugar (o bit do servidor e a funcao pura do cliente);
///   F2  a VIAGEM de verdade: no chao o verb de admin recusa; o robo decola (a decolagem das bancadas), o
///       verb o larga em orbita -- onde o disco do mundo tem o node do HALO, e os outros discos nao --, o
///       corpo voa contra o disco e POUSA; o aviso chega no chat, OS PONTINHOS DO AR acendem (`PoeiraDivina`),
///       a linha "Atmosfera" nasce no cartao Ceu e o cartao "Ki divino" diz que o corpo ainda nao alcanca;
///   F3  o BP base chega ao requisito: o cartao vira MEDIDOR, e ele anda na tela sozinho;
///   F4  o despertar: o retrato, o pacote do anel de luz (`S2C.GodKiDespertou`) e o efeito NASCIDO no mundo,
///       a maestria no cartao e o botao "Challenge God of Destruction" nascendo na aba Other;
///   F5  de volta a um lugar comum: a linha da atmosfera some, os pontinhos apagam, o cartao do personagem fica;
///   F6  um mundo divino que ESTOURA leva o halo junto (planeta de palco, estourado pela porta do desenho).
///
/// QUATRO DEFEITOS INJETADOS: o servidor achando que todo chao e divino (`GodKiEmQualquerChaoDeTeste`: as duas
/// pontas passam a discordar), o retrato que so sai uma vez (`GodKiCongeladoDeTeste`: o medidor para na tela),
/// o ar divino que nao desenha nada (`PoeiraDivina.ApagadaDeTeste`) e o halo que nao acompanha o estouro
/// (`PlanetaDesenhado.HaloFicaNoEstouroDeTeste`).
///
/// O QUE ELA NAO PROVA: o som, e a marca dourada desenhada na carta estelar -- dela so se confere a PERGUNTA
/// (`MenuJogo.MundoDivino`), que e a mesma do anel. COR E FORMA nenhum numero daqui mede: com janela
/// (`--janela` no lugar do `--headless`) ela FOTOGRAFA o halo do planeta em orbita, os pontinhos do ar, a aba World e o
/// instante do despertar (`user://godki-NN-*.png`), pro olho de quem confere.
///
///     Godot --headless --path . --host --rede 8251 --diaggodki --raca Human --conta bancada_dgodki --nome Mortal
/// </summary>
public partial class RoboDoGodKi : Node
{
	private int _ok, _falha, _injOk, _injFalha;
	private readonly List<string> _reprovadas = [];

	/// <summary>O que o servidor disse a este cliente no chat, desde a ultima limpeza.</summary>
	private readonly List<string> _falas = [];

	/// <summary>De quem o `S2C.GodKiDespertou` chegou.</summary>
	private readonly List<int> _despertos = [];

	/// <summary>Quantos retratos (`S2C.GodKi`) chegaram.</summary>
	private int _retratos;

	private bool _escutando;

	/// <summary>Quantas fotos sairam (so na rodada de janela).</summary>
	private int _fotos;

	private static void Nota(string s) => GD.Print("[dgodki] " + s);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK     " : "  FALHA  ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private void Injeta(string oque, bool ficouVermelha, string detalhe = "")
	{
		Nota((ficouVermelha ? "  pegou  " : "  PASSOU ") + "(defeito injetado) " + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ficouVermelha) _injOk++;
		else { _injFalha++; _reprovadas.Add("(defeito injetado) " + oque); }
	}

	private static GameClient? C => GameClient.Instance;
	private static MenuJogo? M => MenuJogo.Instancia;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	public override void _Ready() => _ = Rodar();

	/// <summary>Uma excecao no roteiro e um vermelho com placar, e nao uma bancada muda ate o prazo.</summary>
	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Checa("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			Nota(e.ToString());
			Encerrar(3);
		}
	}

	private void AoFalar(Jandirus.Net.Protocol.Fala canal, string autor, string texto) => _falas.Add(texto);
	private void AoDespertar(int quem) => _despertos.Add(quem);
	private void AoRetrato() => _retratos++;

	private void Encerrar(int codigo)
	{
		// O MUNDO VOLTA: nenhum botao de defeito e nenhum ouvido sobrevivem ao roteiro.
		if (S is { } srv) { srv.GodKiEmQualquerChaoDeTeste = false; srv.GodKiCongeladoDeTeste = false; }
		PoeiraDivina.ApagadaDeTeste = false;
		PlanetaDesenhado.HaloFicaNoEstouroDeTeste = false;
		if (_escutando && C is { } cli)
		{
			cli.Falou -= AoFalar;
			cli.GodKiDespertou -= AoDespertar;
			cli.KiDivinoMudou -= AoRetrato;
		}
		World.Instancia?.PararDeTeste();

		Nota("==================================================================================");
		Nota($" PLACAR: {_ok} OK, {_falha} FALHA(S)   |   DEFEITOS INJETADOS: {_injOk} pegos, {_injFalha} passaram batido");
		foreach (string r in _reprovadas) Nota("   FALHA  " + r);
		int vermelhas = _falha + _injFalha;
		Nota(vermelhas == 0 ? "===== TUDO OK =====" : $"===== {vermelhas} FALHA(S) =====");
		GetTree().Quit(codigo);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Tarefa Percurso()
	{
		Nota("==================================================================================");
		Nota(" O KI DIVINO NA TELA -- o aviso ao pousar, a aba World, o medidor, o despertar e o botao do desafio");
		Nota("==================================================================================");

		bool pronto = await Ate(() => C is { Connected: true } c && c.Atributos.Raca is { Length: > 0 } && c.SeedDoUniverso != 0
									  && M != null && S != null && World.Instancia != null, 90);
		Checa("o mundo chegou (conexao, ficha lenta, seed do universo, menu de pe) e o servidor e deste processo (--host)", pronto,
			  $"raca={C?.Atributos.Raca} seed={C?.SeedDoUniverso} menu={(M != null)} servidor={(S != null)}");
		if (!pronto || M is not { } menu || C is not { } cli || S is not { } srv) { Encerrar(2); return; }

		cli.Falou += AoFalar;
		cli.GodKiDespertou += AoDespertar;
		cli.KiDivinoMudou += AoRetrato;
		_escutando = true;
		int eu = cli.LocalId;

		// A COBAIA: um mortal com METADE do requisito pessoal dele (e que aguenta o chao de qualquer mundo).
		Checa("(montagem) a cobaia existe no servidor", srv.CobaiaDoGodKiDeTeste(eu, 0.5));
		await Segundos(2.5);   // o tique do ki divino e de 1 Hz: o primeiro retrato ja saiu

		menu.Abrir();
		await Quadros(3);

		ZoneKey? chao = await F1_OMortalComum(menu, cli, srv);
		if (chao is { } divino)
		{
			await F3_OMedidorAnda(menu, cli, srv);
			await F4_ODespertar(menu, cli, srv);
			await F5_DeVoltaAoComum(menu, cli, divino);
		}
		await F6_OHaloNoEstouro();

		Encerrar(_falha + _injFalha == 0 ? 0 : 1);
	}

	/// <summary>F1 e F2: o mortal onde nasceu, e a viagem ate um mundo divino. Devolve o chao em que pousou (nulo = nao chegou).</summary>
	private async System.Threading.Tasks.Task<ZoneKey?> F1_OMortalComum(MenuJogo menu, GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F1: o mortal comum, onde nasceu ---");
		int eu = cli.LocalId;
		await Abrir(menu, "World", forcar: true);
		Control? pg = menu.PaginaDeTeste("World");

		Checa("o primeiro retrato do ki divino chegou sozinho, no login", _retratos >= 1, $"{_retratos} retrato(s)");
		Checa("onde a bancada nasceu NAO e divino, e as duas pontas concordam: o bit do servidor e a funcao pura do cliente",
			  Espaco.EhPlaneta(cli.Zone) && !MenuJogo.LugarDivino(cli) && !cli.KiDivino.NaAtmosfera
			  && srv.KiDivinoDeTeste(eu) is { NaAtmosfera: false }, $"zona {cli.Zone.Name}");
		Checa("a aba World e a de sempre: sem linha 'Atmosfera' no cartao Céu e sem cartao 'Ki divino'",
			  pg != null && menu.ValorDesenhado("World", "Atmosfera") == null && !TemCartao(pg, "Ki divino"));
		await Abrir(menu, Verbos.Outros, forcar: true);
		Checa("sem ki divino, o botao 'Challenge God of Destruction' nao existe na aba Other",
			  !BotoesDa(menu, Verbos.Outros).Contains("Challenge God of Destruction"));

		srv.GodKiEmQualquerChaoDeTeste = true;
		try
		{
			bool virou = await Ate(() => cli.KiDivino.NaAtmosfera, 5);
			Injeta("(o servidor achando que todo chao e divino) as duas pontas DISCORDAM sobre o lugar -- a concordancia de cima reprova",
				   virou && !MenuJogo.LugarDivino(cli), $"bit do servidor={cli.KiDivino.NaAtmosfera}, funcao do cliente={MenuJogo.LugarDivino(cli)}");
		}
		finally { srv.GodKiEmQualquerChaoDeTeste = false; }
		await Ate(() => !cli.KiDivino.NaAtmosfera, 5);

		// ---------------------------------------------------------------- F2: A VIAGEM E O POUSO
		Nota("--- F2: a viagem: decolar, a orbita pelo verb de admin, e o POUSO voando contra o disco ---");
		_falas.Clear();
		cli.SendVerbo("admin_mundo_divino");
		bool recusou = await Ate(() => Disse("decole primeiro"), 4);
		Checa("NO CHAO o `admin_mundo_divino` recusa e manda decolar (ele nao muda de zona sozinho)", recusou && !Espaco.EhEspaco(cli.Zone), Ultimas());

		// A HABILIDADE `decolar` SO ATENDE BANCADA (a porta do jogador e subir voando ate romper a atmosfera): a
		// flag deste robo esta na lista `BancadasQueDecolamSemVoar` do servidor (`GameServer.Validacoes.cs`).
		_falas.Clear();
		cli.SendHabilidade("decolar");
		bool decolou = await Ate(() => Espaco.EhEspaco(cli.Zone) && World.Instancia?.PosicaoLocal != null, 15);
		Checa("(montagem) o robo chegou ao espaco pela decolagem das bancadas", decolou, $"zona {cli.Zone.Name} | {Ultimas()}");
		if (!decolou) return null;
		await Ate(() => HalosDesenhados().Discos > 0, 5);   // a vizinhanca chega um pouco depois da zona
		(int discosDaTerra, int errados) = HalosDesenhados();
		Checa("em volta da Terra ha discos de planeta na tela, e o HALO esta so nos de atmosfera divina (aqui, quase sempre nenhum)",
			  discosDaTerra > 0 && errados == 0, $"{discosDaTerra} disco(s), {errados} com o halo trocado");
		Checa("no espaco o ar divino nao desenha nada: os pontinhos sao da SUPERFICIE de um mundo divino",
			  Poeira() is { AcesaDeTeste: false }, Poeira() == null ? "(sem o node)" : "aceso");
		_falas.Clear();
		cli.SendVerbo("admin_mundo_divino");

		// A CHEGADA E UM SALTO DENTRO DA MESMA ZONA (o espaco e uma so): quem diz que chegou e a posicao --
		// o corpo a 90 px do disco de um mundo divino.
		MundoDivino? alvo = null;
		Vector2 onde = default;
		bool emOrbita = await Ate(() =>
		{
			if (World.Instancia?.PosicaoLocal is not { } p) return false;
			onde = p;
			alvo = AtmosferaDivina.MaisProximo(cli.SeedDoUniverso, new Vec2(p.X, p.Y), raioEmCelulas: 1);
			return alvo is { } m && Math.Abs((new Vector2(m.Corpo.Pos.X, m.Corpo.Pos.Y) - p).Length() - (m.Corpo.Raio + 90)) < 40;
		}, 15);
		Checa("DO ESPACO ele larga o corpo em orbita, a 90 px do disco de um mundo de atmosfera divina", emOrbita,
			  alvo is { } a1 ? $"{a1.Corpo.Nome} a {(new Vector2(a1.Corpo.Pos.X, a1.Corpo.Pos.Y) - onde).Length():0} px (raio {a1.Corpo.Raio:0})" : $"zona {cli.Zone.Name}");
		if (!emOrbita || alvo is not { } mundo) return null;

		bool comHalo = await Ate(() => DiscoDe(mundo.Corpo.Seed) is { } d && d.HasNode(PlanetaDesenhado.NomeDoHalo), 5);
		(int discos, int trocados) = HalosDesenhados();
		Checa("**O HALO NASCEU**: o disco deste mundo esta na cena com o node do halo -- e nenhum outro disco a vista o tem sem ser divino",
			  comHalo && trocados == 0, $"{discos} disco(s) a vista, {trocados} com o halo trocado");
		await FotoDoMundo(menu, "orbita-o-halo-dourado", zoom: 1);   // a camera toda aberta, pra o disco caber

		Checa("a carta estelar marca este mundo (o anel dourado sai da MESMA pergunta: `MenuJogo.MundoDivino`), e nao a Terra",
			  MenuJogo.MundoDivino(mundo.Corpo) && !Espaco.PreFeitos().Any(MenuJogo.MundoDivino));

		ZoneKey chao = Espaco.ZonaDe(mundo.Corpo);
		_falas.Clear();   // a frase do verb tambem diz "atmosfera divina": daqui pra frente so vale o que o POUSO disser

		// (2026-10-20) NO ESPACO SO SE ANDA VOANDO (dono): o salto do verb larga o corpo a pe (o `MoveToZone` apaga o voo), e um Mortal
		// novo nem sabe voar. Ele aprende e decola pela porta do jogador (`LigarOVooDaCobaiaNoTeste`, o palco da `--diagespacovoo`).
		Checa("(montagem) a cobaia liga o voo no vazio (so voando se sai do lugar la)", srv.LigarOVooDaCobaiaNoTeste(cli.LocalId));

		// O RUMO E DADO DE NOVO A CADA QUADRO, e nao uma vez so: o salto do verb recarrega a zona no cliente, e a
		// chegada dessa carga DESLIGA o piloto automatico ("chegou noutro lugar: o piloto anterior nao vale mais",
		// `LocalPlayer`). Dado uma vez, o rumo as vezes morria ali -- e o corpo ficava em orbita ate sufocar (foi
		// o vermelho da rodada sem janela de 2026-10-11, que nao tem os quadros da foto pra esconder a corrida).
		// O prazo e menor que o do vacuo (um Human sufoca em ~20 s): se o corpo nao pousar, o vermelho e o do pouso.
		bool pousou = await Ate(() =>
		{
			if (cli.Zone.Equals(chao)) return true;
			World.Instancia?.IrAteDeTeste(mundo.Corpo.Pos);
			return false;
		}, 15);
		World.Instancia?.PararDeTeste();
		Checa($"o corpo POUSOU em {mundo.Corpo.Nome} voando contra o disco (o pouso de producao)", pousou,
			  $"zona {cli.Zone} | o corpo nao anda por: '{World.Instancia?.PorQueOCorpoNaoAnda}'");
		if (!pousou) return null;
		srv.DePeNoTeste(cli.LocalId);   // o vacuo da viagem cobrou vida: o que se mede daqui pra frente e o ar, e nao a asfixia

		bool avisado = await Ate(() => cli.KiDivino.NaAtmosfera && Disse("este mundo tem uma ATMOSFERA DIVINA"), 6);
		Checa("**O AVISO AO POUSAR** chega no chat: \"este mundo tem uma ATMOSFERA DIVINA\"", avisado, Ultimas());
		Checa("...e, com metade do requisito, o jogador le que o corpo ainda nao alcanca", Disse("ainda não o alcança"), Ultimas());
		Checa("as duas pontas concordam que ESTE lugar e divino", MenuJogo.LugarDivino(cli) && cli.KiDivino.NaAtmosfera);

		// OS PONTINHOS DO AR (dono, 2026-10-12): poucos, na tela, e so aqui.
		await Quadros(4);
		PoeiraDivina? poeira = Poeira();
		Checa($"**OS PONTINHOS DO AR DIVINO**: na superficie a poeira esta acesa, com {PoeiraDivina.Pontos} pontos (poucos) e todos dentro da tela",
			  poeira is { AcesaDeTeste: true } && poeira.PontosDesenhadosDeTeste.Count == PoeiraDivina.Pontos && TodosNaTela(poeira),
			  poeira == null ? "(sem o node)" : $"acesa={poeira.AcesaDeTeste}, {poeira.PontosDesenhadosDeTeste.Count} ponto(s)");
		await FotoDoMundo(menu, "superficie-os-pontinhos-do-ar", zoom: 2);
		PoeiraDivina.ApagadaDeTeste = true;
		await Quadros(4);
		Injeta("(o ar divino que nao desenha nada) a poeira apaga num mundo divino -- a checagem de cima reprova",
			   Poeira() is not { AcesaDeTeste: true });
		PoeiraDivina.ApagadaDeTeste = false;
		await Quadros(4);

		await Abrir(menu, "World", forcar: false);
		bool linha = await Ate(() => menu.ValorDesenhado("World", "Atmosfera") == "divina", 4);
		pg = menu.PaginaDeTeste("World");
		Checa("**A LINHA NA ABA WORLD**: o cartao Céu ganhou 'Atmosfera: divina' (sem ninguem forcar o redesenho)", linha,
			  menu.ValorDesenhado("World", "Atmosfera") ?? "(sem linha)");
		Checa("...e o cartao 'Ki divino' nasceu, dizendo que o corpo ainda nao alcanca (sem numero: o requisito e BP base)",
			  pg != null && TemCartao(pg, "Ki divino") && menu.ValorDesenhado("World", "Despertar") == "o corpo ainda não alcança"
			  && !cli.KiDivino.AlcancaORequisito,
			  menu.ValorDesenhado("World", "Despertar") ?? "(sem linha)");
		await Foto("aba-world-a-linha-da-atmosfera");
		await FotoDoCartao(menu, "aba-world-o-cartao-sem-alcance");
		return chao;
	}

	private async Tarefa F3_OMedidorAnda(MenuJogo menu, GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F3: o BP base chega ao requisito: o cartao vira medidor, e ele anda ---");
		int eu = cli.LocalId;
		_falas.Clear();
		srv.BpBaseDoGodKiDeTeste(eu, 1.0);

		bool absorve = await Ate(() => cli.KiDivino.AlcancaORequisito && Disse("começa a absorvê-lo"), 5);
		bool virouMedidor = await Ate(() => Pct(menu.ValorDesenhado("World", "Despertar")) >= 0, 4);
		int antes = Pct(menu.ValorDesenhado("World", "Despertar"));
		Checa("com o BP base NO requisito o servidor avisa que o corpo absorve, e o cartao passa a mostrar o MEDIDOR em pontos",
			  absorve && virouMedidor, $"na tela: {menu.ValorDesenhado("World", "Despertar") ?? "(sem linha)"} | {Ultimas()}");

		bool andou = await Ate(() => Pct(menu.ValorDesenhado("World", "Despertar")) > antes, 14);
		int naTela = Pct(menu.ValorDesenhado("World", "Despertar"));
		double noServidor = srv.KiDivinoDeTeste(eu)?.Exposicao ?? -1;
		Checa("**O MEDIDOR ANDA NA TELA**, sozinho, e diz o que o servidor tem (a um ponto de folga)",
			  andou && Math.Abs(naTela - noServidor) <= 1.5, $"{antes}% -> {naTela}% na tela; {noServidor:0.##}% no servidor");
		await FotoDoCartao(menu, "aba-world-o-medidor");

		srv.GodKiCongeladoDeTeste = true;
		try
		{
			int congelado = Pct(menu.ValorDesenhado("World", "Despertar"));
			double servidorAntes = srv.KiDivinoDeTeste(eu)?.Exposicao ?? 0;
			await Segundos(9);
			double servidorDepois = srv.KiDivinoDeTeste(eu)?.Exposicao ?? 0;
			Injeta("(o retrato so sai uma vez) o cartao fica PARADO enquanto o medidor do servidor anda -- a checagem de cima reprova",
				   Pct(menu.ValorDesenhado("World", "Despertar")) == congelado && servidorDepois - servidorAntes > 1,
				   $"na tela {congelado}% -> {Pct(menu.ValorDesenhado("World", "Despertar"))}%; no servidor {servidorAntes:0.#}% -> {servidorDepois:0.#}%");
		}
		finally { srv.GodKiCongeladoDeTeste = false; }
	}

	private async Tarefa F4_ODespertar(MenuJogo menu, GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F4: o despertar (pelo verb de admin, que passa pelo funil de producao) ---");
		int eu = cli.LocalId;
		_falas.Clear();
		_despertos.Clear();
		int desenhados = World.DespertaresDesenhadosDeTeste;
		cli.SendVerbo("admin_godki", "0");
		bool chegou = await Ate(() => _despertos.Contains(eu), 6);
		if (chegou)
		{
			await Segundos(0.25);   // os aneis abertos pela metade (e, de noite, o clarao ainda forte)
			await FotoDoMundo(menu, "o-despertar-os-aneis-de-luz", zoom: 2);
		}

		bool desperto = await Ate(() => cli.KiDivino.Desperto && Disse("O KI DIVINO DESPERTOU EM VOCÊ"), 6);
		Checa("o retrato diz DESPERTO e o chat conta o despertar ao jogador", desperto, Ultimas());
		Checa("o `S2C.GodKiDespertou` chegou com o MEU id -- e dele que saem o som e o anel de luz", chegou,
			  string.Join(",", _despertos));
		Checa("...e o efeito NASCEU no mundo: o cliente achou o corpo e desenhou o despertar (pacote que chega nao e anel na tela)",
			  World.DespertaresDesenhadosDeTeste == desenhados + 1, $"{World.DespertaresDesenhadosDeTeste - desenhados} desenhado(s)");

		await Abrir(menu, "World", forcar: false);
		string zero = $"{0.0:0.0}%";
		bool cartao = await Ate(() => menu.ValorDesenhado("World", "Ki divino") == "desperto", 4);
		Checa("o cartao troca o medidor pela MAESTRIA: 'Ki divino: desperto' e 'Maestria divina' em 0",
			  cartao && menu.ValorDesenhado("World", "Maestria divina") == zero && menu.ValorDesenhado("World", "Despertar") == null,
			  $"Ki divino={menu.ValorDesenhado("World", "Ki divino")} maestria={menu.ValorDesenhado("World", "Maestria divina")}");
		await FotoDoCartao(menu, "aba-world-desperto");

		bool fato = await Ate(() => (cli.Fatos & Fatos.GodKiDesperto) != 0, 4);
		await Abrir(menu, Verbos.Outros, forcar: false);
		bool botao = await Ate(() => BotoesDa(menu, Verbos.Outros).Contains("Challenge God of Destruction"), 4);
		Checa("**O BOTAO DO DESAFIO NASCE**: o fato `GodKiDesperto` chegou pelo fio e 'Challenge God of Destruction' apareceu na aba Other",
			  fato && botao, $"fato={fato} botao={botao}");
		Checa("...e o servidor concorda: desperto, com o medidor cheio",
			  srv.KiDivinoDeTeste(eu) is { Desperto: true, Exposicao: >= DespertarDivino.ExposicaoCheia });
	}

	private async Tarefa F5_DeVoltaAoComum(MenuJogo menu, GameClient cli, ZoneKey divino)
	{
		Nota("--- F5: de volta a um lugar comum ---");
		cli.SendVerbo("admin_spawn");
		bool saiu = await Ate(() => !cli.Zone.Equals(divino) && Espaco.EhPlaneta(cli.Zone) && !cli.KiDivino.NaAtmosfera, 20);
		await Abrir(menu, "World", forcar: false);
		bool semLinha = await Ate(() => menu.ValorDesenhado("World", "Atmosfera") == null, 4);
		Checa("fora do mundo divino a linha 'Atmosfera' SOME, e o cartao 'Ki divino' fica -- ele agora e do personagem",
			  saiu && semLinha && menu.ValorDesenhado("World", "Ki divino") == "desperto" && !MenuJogo.LugarDivino(cli),
			  $"zona {cli.Zone.Name} | atmosfera={menu.ValorDesenhado("World", "Atmosfera") ?? "(sem linha)"}");
		await Quadros(4);
		Checa("...e os pontinhos do ar APAGAM: num lugar comum a poeira nao desenha nada", Poeira() is { AcesaDeTeste: false });
	}

	/// <summary>
	/// F6: UM MUNDO DIVINO QUE ESTOURA LEVA O HALO JUNTO. Planeta de PALCO, no molde da bancada da agonia: nasce
	/// longe de tudo, estoura pela porta de producao do desenho (`AplicarAgonia` com o prazo vencido) e morre
	/// com o palco. Nada aqui passa pelo servidor: nenhum planeta de verdade e destruido.
	/// </summary>
	private async Tarefa F6_OHaloNoEstouro()
	{
		Nota("--- F6: um mundo divino que estoura leva o halo junto (planeta de palco) ---");
		if (World.Instancia is not { } mundo) { Checa("(montagem) ha um mundo de pe pra pendurar o palco", false); return; }

		var palco = new Node2D { Name = "PalcoDoHalo", Position = new Vector2(-200_000, -200_000) };
		mundo.AddChild(palco);
		try
		{
			(float antes, float depois) = await EstourarNoPalco(palco);
			Checa("**O HALO SOME COM O DISCO**: o mundo divino de palco nasce com o halo aceso e, estourado, o halo se apaga no tempo do disco",
				  antes > 0.5f && depois >= 0f && depois <= 0.02f, $"alfa do halo {antes:0.00} -> {depois:0.00}");

			PlanetaDesenhado.HaloFicaNoEstouroDeTeste = true;
			(antes, depois) = await EstourarNoPalco(palco);
			Injeta("(o halo que nao acompanha o estouro) o disco some e o halo continua aceso em volta de nada -- a checagem de cima reprova",
				   depois > 0.5f, $"alfa do halo {antes:0.00} -> {depois:0.00}");
		}
		finally
		{
			PlanetaDesenhado.HaloFicaNoEstouroDeTeste = false;
			palco.QueueFree();
		}
	}

	/// <summary>Nasce um mundo divino no palco, estoura-o e devolve o alfa do halo antes e depois do tempo em que o disco some.</summary>
	private async System.Threading.Tasks.Task<(float Antes, float Depois)> EstourarNoPalco(Node2D palco)
	{
		var planeta = new PlanetaDesenhado { Name = "MundoDePalco", Nome = "Palco", Raio = 60, Seed = 7, Divino = true };
		palco.AddChild(planeta);
		await Quadros(2);   // o `_Ready` monta o disco
		float antes = AlfaDoHalo(planeta);

		planeta.AplicarAgonia(1.0, 0.0);   // o prazo venceu AGORA: e o `Estourar` de producao
		await Segundos(MortePlanetaria.SegundosDoEstouro * 0.55 + 0.35);
		float depois = AlfaDoHalo(planeta);

		if (IsInstanceValid(planeta)) planeta.QueueFree();
		await Quadros(2);
		return (antes, depois);
	}

	private static float AlfaDoHalo(PlanetaDesenhado planeta) =>
		IsInstanceValid(planeta) && planeta.GetNodeOrNull<Sprite2D>(PlanetaDesenhado.NomeDoHalo) is { } halo ? halo.Modulate.A : -1f;

	// =====================================================================
	// LER A TELA, E ESPERAR
	// =====================================================================
	private bool Disse(string trecho) => _falas.Exists(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private string Ultimas() => string.Join(" | ", _falas.TakeLast(4).Select(f => f.Length > 90 ? f[..90] + "..." : f));

	/// <summary>O numero de um "37%" desenhado, ou -1 quando a linha nao e um medidor (ou nao existe).</summary>
	private static int Pct(string? texto) =>
		texto != null && texto.EndsWith('%') && int.TryParse(texto[..^1], out int n) ? n : -1;

	/// <summary>Sem janela nao ha o que fotografar -- e pedir a imagem ao renderizador de mentira so suja o log com um erro por foto.</summary>
	private static bool SemJanela => DisplayServer.GetName() == "headless";

	/// <summary>
	/// A tela inteira em `user://godki-NN-nome.png`. SO COM JANELA: as fotos sao pro olho de quem confere o que
	/// robo nenhum mede -- o halo em volta do disco, os pontinhos do ar, o anel do despertar, a cara do cartao.
	/// </summary>
	private async Tarefa Foto(string nome)
	{
		if (SemJanela) return;
		await Quadros(2);
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) return;
			string caminho = ProjectSettings.GlobalizePath($"user://godki-{++_fotos:00}-{nome}.png");
			img.SavePng(caminho);
			Nota($"  foto: {caminho}");
		}
		catch (Exception e) { Nota($"  (sem foto de '{nome}': {e.Message})"); }
	}

	/// <summary>O disco desenhado do mundo com esta seed, se ele esta na cena.</summary>
	private static PlanetaDesenhado? DiscoDe(ulong seed) =>
		World.Instancia is { } w ? Todos(w).OfType<PlanetaDesenhado>().FirstOrDefault(d => !d.Premade && d.Seed == seed) : null;

	/// <summary>
	/// Quantos discos de planeta estao na cena, e em quantos o halo esta TROCADO: tem o node sem o mundo ser
	/// divino, ou e divino e nao o tem. A pergunta de controle e a pura (`PlanetaDesenhado.TemAtmosferaDivina`),
	/// e o que se confere e o NODE -- quem monta a vizinhanca (`World`) e quem tem de ter escrito o `Divino`.
	/// </summary>
	private static (int Discos, int Trocados) HalosDesenhados()
	{
		int discos = 0, trocados = 0;
		if (World.Instancia is not { } w) return (0, 0);
		foreach (PlanetaDesenhado d in Todos(w).OfType<PlanetaDesenhado>())
		{
			discos++;
			if (d.HasNode(PlanetaDesenhado.NomeDoHalo) != PlanetaDesenhado.TemAtmosferaDivina(d.Premade, d.Seed)) trocados++;
		}
		return (discos, trocados);
	}

	/// <summary>O node dos pontinhos do ar divino, onde quer que a iluminacao o tenha pendurado.</summary>
	private static PoeiraDivina? Poeira() =>
		World.Instancia is { } w ? Todos(w).OfType<PoeiraDivina>().FirstOrDefault() : null;

	/// <summary>Todos os pontos desenhados caem dentro do que a camera mostra (com a margem de um ponto)?</summary>
	private static bool TodosNaTela(PoeiraDivina poeira)
	{
		if (poeira.GetViewport() is not { } tela) return false;
		Transform2D paraOMundo = tela.CanvasTransform.AffineInverse();
		var visto = new Rect2(paraOMundo * Vector2.Zero, paraOMundo.BasisXform(tela.GetVisibleRect().Size)).Grow(16);
		return poeira.PontosDesenhadosDeTeste.TrueForAll(visto.HasPoint);
	}

	/// <summary>
	/// FOTO DO MUNDO: o menu, o HUD e o chat saem da frente e a camera vai a este zoom; depois tudo volta. (O
	/// HUD e o chat sao camadas de tela: escondidos nao mudam nada do que o roteiro mede, que e o fio e a
	/// arvore do menu.)
	/// </summary>
	private async Tarefa FotoDoMundo(MenuJogo menu, string nome, int zoom)
	{
		if (SemJanela) return;
		int antes = (int)MathF.Round(World.Instancia?.ZoomDaCameraDeTeste ?? 0f);
		menu.Fechar();
		CamadasDeTela(false);
		World.Instancia?.AplicarZoom(zoom);
		try { await Foto(nome); }
		finally
		{
			if (antes > 0) World.Instancia?.AplicarZoom(antes);
			CamadasDeTela(true);
			menu.Abrir();
		}
	}

	private static void CamadasDeTela(bool aVista)
	{
		if (Hud.Instancia is { } hud) hud.Visible = aVista;
		if (Chat.Instancia is { } chat) chat.Visible = aVista;
	}

	/// <summary>FOTO DO CARTAO "Ki divino" da aba World: ele nasce abaixo da dobra, entao a pagina rola ate ele antes.</summary>
	private async Tarefa FotoDoCartao(MenuJogo menu, string nome)
	{
		PanelContainer? cartao = menu.PaginaDeTeste("World") is { } pg
			? Todos(pg).OfType<PanelContainer>().FirstOrDefault(c => c.HasMeta("titulo") && c.GetMeta("titulo").AsString() == "Ki divino")
			: null;
		for (Node? n = cartao?.GetParent(); n != null; n = n.GetParent())
			if (n is ScrollContainer rolagem) { rolagem.EnsureControlVisible(cartao!); break; }
		await Foto(nome);
	}

	private async Tarefa Abrir(MenuJogo menu, string aba, bool forcar)
	{
		menu.IrPara(aba);
		await Quadros(3);
		if (forcar) { menu.ForcarRedesenho(); await Quadros(3); }
	}

	private static HashSet<string> BotoesDa(MenuJogo menu, string aba) =>
		menu.PaginaDeTeste(aba) is { } pg
			? [.. Todos(pg).OfType<Button>().Where(b => b.IsVisibleInTree()).Select(b => b.Text)]
			: [];

	/// <summary>A pagina tem um cartao com este titulo? (o metadado `titulo` que o `MenuJogo.Cartao` grava)</summary>
	private static bool TemCartao(Control pg, string titulo) =>
		Todos(pg).OfType<PanelContainer>().Any(c => c.HasMeta("titulo") && c.GetMeta("titulo").AsString() == titulo && c.IsVisibleInTree());

	private static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node n in Todos(f)) yield return n;
	}

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + s;
		while (Time.GetTicksMsec() / 1000.0 < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
		while (Time.GetTicksMsec() / 1000.0 < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}
}
