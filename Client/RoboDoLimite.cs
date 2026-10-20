using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.World;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO SHATTERING THE LIMIT (`--diaglimite`, 2026-10-19). Os pedidos do dono: *"faça com q primal saiyans q usem a passiva Shattering
/// the Limit [...] a técnica de ki em questao saia vermelha, e faça q esse "transformado" seja apenas no ssj4, ssj4 fp e ssj4 lb (pra
/// lendario primal é a mesma coisa mas a diferença q seria no lssj4 dele etc) tire prints pra provar no final"*, *"lendario tb tem as
/// lssj4 fp e lssj4 lb n esqueça delas"* -- e, perguntado, a carga na mao vermelha tambem. Depois: *"faça com q o kamehameha seja a unica
/// exceção ao qual ele n precisa gastar 10% do ki pra ativar o bonus de x10, e sim sempre q estiver em umas dessas formas de ssj4 a skill pré
/// feita do kamehameha vira o kamehamehax10 q é vermelho e tem todas os bonus da passiva"*.
///
/// Com janela e `--host`. O palco (`GameServer.LimitePalco.cs`) faz do robo um Primal (e depois um Lendario Primal), poe a forma sem
/// cinematica, crava a cor do ki num AZUL (o APPDATA limpo sorteia uma por rodada, e ela podia sair vermelha) e deixa o Ki em 4x o custo do
/// tiro -- 25% do Ki de agora, acima dos 10% do Shattering (e 2,5%, abaixo, nas cenas do Kamehameha x10). Quem atira e o BOTAO (`UsarHabilidade`). Em cada forma:
///
///   K01..K10. a CARGA na mao, no meio dela -- vermelha no degrau SSJ4, azul fora dele (o node e a foto). Foto `limite-NN-<forma>-carga.png`
///      e a tira `limite-tira-carga.png`.
///   L01..L10. o RAIO, o MESMO Kamehameha em BASE, SSJ1, SSJ3, SSJ4, SSJ4 FP e SSJ4 LB (Primal) e LSSJ3, LSSJ4, LSSJ4 FP e LSSJ4 LB
///      (Lendario Primal). No degrau SSJ4 ele sai 2x (a onda do servidor), com o grito "10x Kamehameha!!", marcado (`Projetil.DoLimite`), e
///      o node do cliente e a FOTO saem VERMELHOS; fora dele, 1x, sem o "10x", e na cor do ki (azul). Foto `limite-NN-<forma>.png` (a tela
///      inteira, com o chat) e a tira `limite-tira.png` (os raios, de cima pra baixo, na ordem destas cenas).
///   K/L11..14. O KAMEHAMEHA X10: o Kamehameha de 2,5% do Ki no SSJ4 e no LSSJ4 LB sai vermelho, 2x e com o "10x" (ele dispensa os 10%);
///      o Galick Ho de 2,5% no SSJ4 sai comum, e o de 25% sai vermelho (as outras tecnicas continuam pedindo os 10%).
///   C1. CONTRA-EXEMPLO (`Passivas.LimiteEmQualquerFormaDeTeste`, a regra de antes): o SSJ1 sai vermelho (a carga e o raio) e com o "10x"
///      -- as reguas do K02/L02 REPROVAM. Fotos `limite-c1-defeito-ssj1*.png`.
///   C2. CONTRA-EXEMPLO (`Passivas.LimiteSemVermelhoDeTeste`): o SSJ4 sai 2x na cor do ki, a carga tambem -- as reguas do vermelho do
///      K04/L04 REPROVAM. Fotos `limite-c2-defeito-ssj4-sem-vermelho*.png`.
///   C3. CONTRA-EXEMPLO (`Passivas.KamehamehaPedeOPisoDeTeste`): o Kamehameha de 2,5% no SSJ4 sai comum -- as reguas do K11/L11 REPROVAM.
///      Fotos `limite-c3-defeito-kamehameha-com-piso*.png`.
///
/// A REGUA DO VERMELHO E DE PIXEL (a das tres fotos da `--diagcargavoo`): com a arvore pausada, o node (a bola da carga ou o raio)
/// escondido, mostrado e escondido de novo; tinta e o pixel que difere das duas fotos sem ele, onde as duas concordam. "Vermelho" = o
/// pixel com R passando G e B por 0,15 na carga e por 0,35 no raio (vermelho saturado: o rosa do miolo e o magenta de uma arte roxa nao
/// contam); a carga vermelha tem pelo menos 35% da tinta assim e a comum no maximo 25%, o raio vermelho 25% e o comum 5%.
///
/// A r1 (2026-10-20) reprovou o RAIO e estava certa: o node vermelho, mas a FOTO azul -- a arte do Kamehameha tem tons proprios e o vermelho
/// SOMADO a eles saia branco-rosado (`ArteDeKiNoCliente.TonsDoLimite` conta o conserto).
///
///     Godot --path . --host --rede 9501 --campoteste 12 --horateste 0.5 --semfauna --diaglimite --raca Saiyan --conta bancada_dlimite --nome Kakarot
/// </summary>
public partial class RoboDoLimite : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ditas = [];
	private readonly List<Image> _tira = [], _tiraDaCarga = [];
	private int _ok;

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>As duas tecnicas prontas: o Kamehameha (o x10) e o Galick Ho (uma outra qualquer, que continua pedindo os 10%).</summary>
	private const string Kame = Passivas.VerboDoKamehameha, Galick = "GalicGun";

	/// <summary>A cor do ki do robo: um azul de Kamehameha, longe do vermelho do Shattering.</summary>
	private static readonly Jandirus.Core.Appearance.Rgb Azul = new(40, 110, 255);

	/// <summary>Quantos tiles a cabeca anda antes da foto: o raio inteiro fora do corpo, e longe do fim do alcance.</summary>
	private const double TilesAntesDaFoto = 5;

	/// <summary>Quanto a bola da carga vive antes da foto: ela enche em meio segundo (`CargaDeRaioVisual`), e a carga dura ~1,8 s.</summary>
	private const double IdadeDaCarga = 0.6;

	/// <summary>
	/// A REGUA DA CARGA: o pixel vermelho (R passando G e B por 0,15), e a fracao da tinta que tem de ser vermelha no Shattering e o teto dela
	/// no comum. Medido nas rodadas r1 e r2 (25 cargas de cada): a vermelha de 43% a 87%, e a AZUL de 0% a 15% -- os tons quentes do desenho
	/// da bola contam um pouco. O teto de 3%, e depois o de 8%, reprovaram cargas azuis de verdade; o piso e o teto ficam no meio da folga.
	/// </summary>
	private static readonly Regua DaCarga = new(0.15f, 0.35f, 0.25f);

	/// <summary>
	/// A REGUA DO RAIO, mais dura no pixel (R passando G e B por 0,35): na r1 o raio do Galick Ho com o vermelho SOMADO saiu magenta e deu 18%
	/// de pixel "vermelho" pela regua da carga (o rosa do miolo contava). Medido na r2: o raio do Shattering (`TonsDoLimite`) de 45% a 71%, e
	/// todo raio que nao e vermelho (o comum, e os dos mutantes) 0%.
	/// </summary>
	private static readonly Regua DoRaio = new(0.35f, 0.25f, 0.05f);

	/// <summary>Uma regua de vermelho: quanto R passa G e B pra o pixel contar, e o piso (Shattering) e o teto (comum) da fracao.</summary>
	private readonly record struct Regua(float Pixel, float Piso, float Teto);

	/// <summary>O 0,12 das outras bancadas de foto: abaixo disso e ruido do viewport.</summary>
	private const float Epsilon = 0.12f;

	private CanvasItem? _escondido;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok     " : "  FALHA  ") + oque);
		if (ok) _ok++;
		else _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --     " + oque);

	public override void _Ready()
	{
		// A ARVORE E PAUSADA PRA FOTOGRAFAR (ver `Obturar`); sem isto o robo congelaria junto.
		ProcessMode = ProcessModeEnum.Always;
		_ = Rodar();
	}

	public override void _ExitTree() => Desligar();

	/// <summary>Os dois defeitos desligados, a arvore solta e o node visivel -- haja o que houver.</summary>
	private void Desligar()
	{
		Passivas.LimiteEmQualquerFormaDeTeste = false;
		Passivas.LimiteSemVermelhoDeTeste = false;
		Passivas.KamehamehaPedeOPisoDeTeste = false;
		if (IsInsideTree() && GetTree() is { Paused: true } t) t.Paused = false;
		if (_escondido != null && IsInstanceValid(_escondido)) _escondido.Visible = true;
	}

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Desligar();
		if (C is { } cli && S is { } srv) srv.PalcoL_Limpar(cli.LocalId);
		Fechar();
	}

	private void Ouvir(Protocol.Fala canal, string quem, string texto) => _ditas.Add(texto);

	private bool Disse(string trecho) => _ditas.Exists(t => t.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo nasceu e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = cli.LocalId;
		World mundo = World.Instancia!;
		// UM CORPO RECEM-CRIADO ENTRA NOCAUTEADO POR UM INSTANTE (a `--diagvariedade` perdeu o primeiro tiro assim)
		bool dePe = await Ate(() => srv.EstadoDaVariedade(eu) is { KO: false, Morto: false }, 30);
		await Segundos(1.5);
		GD.Print("[dlimite] ===== O SHATTERING THE LIMIT SO NO SSJ4, E VERMELHO (dono, 2026-10-19) =====");

		(int skills, _) = srv.ArmarParaAVariedade(eu);
		srv.CravarMeioDiaDaVariedade(eu);
		Nota($"o palco sem povo: {srv.PalcoL_SemPovo(eu)} habitante(s) a menos de 30 tiles sairam do mundo");
		bool praca = srv.AssentarNaBoca(eu, 6);
		// PRO LESTE, ANDANDO: o olhar do corpo local e previsao do cliente, e o pacote dele reescreveria um olhar posto so no servidor
		// (o `VirarPraLeste` da `--diagcargavoo`)
		bool leste = await Ate(() => { mundo.AndarDeTeste(Vector2.Right); return srv.CorpoDaBoca(eu).Olhar == Facing.East; }, 2);
		mundo.PararDeTeste();
		await Segundos(0.35);
		srv.ReporNaBoca(eu);
		await Segundos(1.1);   // a correcao de posicao atravessa o fio
		srv.PalcoL_CorDoKi(eu, Azul);
		bool azul = await Ate(() => mundo.CorDoKiDe(eu) is var c && Mathf.Abs(c.B - Azul.B / 255f) < 0.02f
										  && Mathf.Abs(c.R - Azul.R / 255f) < 0.02f, 6);
		Conferir(dePe && skills > 0 && praca && leste && srv.CorpoDaBoca(eu).Olhar == Facing.East && azul,
				 $"(montagem) o robo de pe, armado ({skills} skills), numa praca de 6 tiles livres, olhando pro leste "
				 + $"({srv.CorpoDaBoca(eu).Olhar}), com o ki AZUL [{mundo.CorDoKiDe(eu)}]");
		await Segundos(0.5);

		cli.Falou += Ouvir;
		try
		{
			(string Tag, string Forma, string Nome, bool Lendario, bool Dobra, string Verbo, string Tecnica, double Fracao)[] cenas =
			[
				("01", Jandirus.Core.Forms.Catalogo.IdBase, "BASE", false, false, Kame, "Kamehameha", 0.25),
				("02", "ssj1", "SSJ1", false, false, Kame, "Kamehameha", 0.25),
				("03", "ssj3", "SSJ3", false, false, Kame, "Kamehameha", 0.25),
				("04", "ssj4", "SSJ4", false, true, Kame, "Kamehameha", 0.25),
				("05", "ssj4_full_power", "SSJ4 FP", false, true, Kame, "Kamehameha", 0.25),
				("06", "ssj4_limit_breaker", "SSJ4 LB", false, true, Kame, "Kamehameha", 0.25),
				("07", "primal_legendary3", "LSSJ3", true, false, Kame, "Kamehameha", 0.25),
				("08", "primal_legendary4", "LSSJ4", true, true, Kame, "Kamehameha", 0.25),
				("09", "primal_legendary4_full_power", "LSSJ4 FP", true, true, Kame, "Kamehameha", 0.25),
				("10", "primal_legendary4_limit_breaker", "LSSJ4 LB", true, true, Kame, "Kamehameha", 0.25),
				// o Kamehameha x10: o barato tambem dobra; o Galick Ho barato nao, e o caro sim
				("11", "ssj4", "SSJ4 KAMEHAMEHA BARATO", false, true, Kame, "Kamehameha", 0.025),
				("12", "primal_legendary4_limit_breaker", "LSSJ4 LB KAMEHAMEHA BARATO", true, true, Kame, "Kamehameha", 0.025),
				("13", "ssj4", "SSJ4 GALICK BARATO", false, false, Galick, "Galick Ho", 0.025),
				("14", "ssj4", "SSJ4 GALICK CARO", false, true, Galick, "Galick Ho", 0.25),
			];
			foreach (var (tag, forma, nome, lendario, dobra, verbo, tecnica, fracao) in cenas)
			{
				Tiro t = await Atirar(srv, eu, forma, lendario, verbo, tecnica, fracao, $"limite-{tag}-{forma}");
				if (t.Raio.Recorte != null) _tira.Add(t.Raio.Recorte);
				if (t.Carga.Recorte != null) _tiraDaCarga.Add(t.Carga.Recorte);
				string tiro = $"{tecnica} de {fracao * 100:0.#}% do Ki do {(lendario ? "Lendario Primal" : "Primal")}";
				Conferir(dobra ? t.Carga.Vermelha : t.Carga.Comum,
						 dobra
							 ? $"K{tag} {nome}: a CARGA do {tiro}, que vai sair 2x, e VERMELHA na mao -- no node e na FOTO [{t.Carga}]"
							 : $"K{tag} {nome}: a carga do {tiro} e da cor do ki (azul) -- no node e na foto [{t.Carga}]");
				Conferir(dobra ? t.RaioDoLimite : t.RaioComum,
						 dobra
							 ? $"L{tag} {nome}: o {tiro} sai 2x, com o grito \"10x\", e VERMELHO -- marcado no servidor, no node e na FOTO [{t}]"
							 : $"L{tag} {nome}: o {tiro} sai comum -- 1x, sem o \"10x\", na cor do ki (azul) no node e na foto [{t}]");
			}
			Colar("limite-tira.png", _tira,
				  "A TIRA DO RAIO (de cima pra baixo): o Kamehameha de 25% em BASE, SSJ1, SSJ3, SSJ4, SSJ4 FP, SSJ4 LB, LSSJ3, LSSJ4, LSSJ4 FP e "
				  + "LSSJ4 LB; o de 2,5% no SSJ4 e no LSSJ4 LB; o Galick Ho de 2,5% e o de 25% no SSJ4");
			Colar("limite-tira-carga.png", _tiraDaCarga, "A TIRA DA CARGA: a bola na mao, nas mesmas cenas e na mesma ordem");

			// ---------------------------------------------------------- C1. a regra de antes: qualquer forma Saiyajin
			Passivas.LimiteEmQualquerFormaDeTeste = true;
			try
			{
				Tiro d = await Atirar(srv, eu, "ssj1", false, Kame, "Kamehameha", 0.25, "limite-c1-defeito-ssj1");
				Conferir(d.RaioDoLimite && d.Carga.Vermelha && !d.RaioComum && !d.Carga.Comum,
						 $"C1 (defeito injetado: `LimiteEmQualquerFormaDeTeste`, a regra de antes) o SSJ1 sai 2x, com o \"10x\", a carga e o raio "
						 + $"vermelhos -- as reguas do K02 e do L02 REPROVAM [carga: {d.Carga}; raio: {d}]");
			}
			finally { Passivas.LimiteEmQualquerFormaDeTeste = false; }

			// ---------------------------------------------------------- C2. o Shattering sem a marca vermelha
			Passivas.LimiteSemVermelhoDeTeste = true;
			try
			{
				Tiro d = await Atirar(srv, eu, "ssj4", false, Kame, "Kamehameha", 0.25, "limite-c2-defeito-ssj4-sem-vermelho");
				Conferir(d.Atirou && d.ProLeste && !d.Marca && Math.Abs(d.Onda - 2) < 1e-6 && d.Gritou10x && d.Raio.Comum && d.Carga.Comum
						 && !d.RaioDoLimite && !d.Carga.Vermelha,
						 $"C2 (defeito injetado: `LimiteSemVermelhoDeTeste`) o SSJ4 sai 2x e com o \"10x\", mas a carga e o raio na cor do ki -- as "
						 + $"reguas do vermelho do K04 e do L04 REPROVAM [carga: {d.Carga}; raio: {d}]");
			}
			finally { Passivas.LimiteSemVermelhoDeTeste = false; }

			// ---------------------------------------------------------- C3. o Kamehameha pedindo os 10%, como as outras
			Passivas.KamehamehaPedeOPisoDeTeste = true;
			try
			{
				Tiro d = await Atirar(srv, eu, "ssj4", false, Kame, "Kamehameha", 0.025, "limite-c3-defeito-kamehameha-com-piso");
				Conferir(d.RaioComum && d.Carga.Comum && !d.RaioDoLimite && !d.Carga.Vermelha,
						 $"C3 (defeito injetado: `KamehamehaPedeOPisoDeTeste`, a regra de antes) o Kamehameha de 2,5% no SSJ4 sai comum, 1x e azul -- as "
						 + $"reguas do K11 e do L11 REPROVAM [carga: {d.Carga}; raio: {d}]");
			}
			finally { Passivas.KamehamehaPedeOPisoDeTeste = false; }
		}
		finally { cli.Falou -= Ouvir; }
	}

	// =====================================================================
	// UM TIRO
	// =====================================================================
	/// <summary>Uma foto de um node (a bola da carga ou o raio): a cor que o cliente deu a ele e a tinta dele na tela.</summary>
	private readonly record struct Foto(bool Achou, Color Cor, int Pixels, float FracaoVermelha, float FracaoAzul, Image? Recorte, Regua Regua)
	{
		private static bool Perto(Color c, float r, float g, float b) => Mathf.Abs(c.R - r) < 0.02f && Mathf.Abs(c.G - g) < 0.02f && Mathf.Abs(c.B - b) < 0.02f;

		public bool NoVermelho => Perto(Cor, Passivas.CorDoLimite.R / 255f, Passivas.CorDoLimite.G / 255f, Passivas.CorDoLimite.B / 255f);

		public bool NoAzul => Perto(Cor, Azul.R / 255f, Azul.G / 255f, Azul.B / 255f);

		/// <summary>O node pintado do vermelho do Shattering, e a foto com tinta vermelha.</summary>
		public bool Vermelha => Achou && NoVermelho && Pixels > 40 && FracaoVermelha >= Regua.Piso;

		/// <summary>O node na cor do ki (azul), e a foto sem tinta vermelha.</summary>
		public bool Comum => Achou && NoAzul && Pixels > 40 && FracaoVermelha <= Regua.Teto;

		public override string ToString() =>
			Achou ? $"node {Cor}; foto: {Pixels} px de tinta, {FracaoVermelha:P0} vermelha, {FracaoAzul:P0} azul" : "SEM NODE";
	}

	/// <summary>O que um tiro deu: o servidor (a marca e a onda), o grito, e as fotos da carga e do raio.</summary>
	private readonly record struct Tiro(bool Atirou, bool Marca, double Onda, bool ProLeste, bool Gritou10x, bool GritouNome, Foto Carga, Foto Raio,
										string Forma, string Resposta)
	{
		/// <summary>O raio do Shattering: 2x, marcado, o "10x", o node e a foto vermelhos (e pro leste, onde a foto mede).</summary>
		public bool RaioDoLimite => Atirou && ProLeste && Marca && Math.Abs(Onda - 2) < 1e-6 && Gritou10x && Raio.Vermelha && Raio.Pixels > 300;

		/// <summary>O raio comum: 1x, sem marca, o grito sem o "10x", o node e a foto na cor do ki (e pro leste).</summary>
		public bool RaioComum => Atirou && ProLeste && !Marca && Math.Abs(Onda - 1) < 1e-6 && GritouNome && !Gritou10x && Raio.Comum && Raio.Pixels > 300;

		public override string ToString() =>
			$"{Forma}; servidor: {(Atirou ? $"onda {Onda:0.##}x, marca {Marca}{(ProLeste ? "" : ", NAO SAIU PRO LESTE")}" : "SEM RAIO")}; grito: {(Gritou10x ? "\"10x\"" : GritouNome ? "sem o 10x" : "nenhum")}; "
			+ Raio + (Resposta.Length > 0 ? $"; {Resposta}" : "");
	}

	private async System.Threading.Tasks.Task<Tiro> Atirar(Jandirus.Server.GameServer srv, int eu, string forma, bool lendario, string verbo,
														  string tecnica, double fracao, string foto)
	{
		World mundo = World.Instancia!;
		srv.PalcoL_Sangue(eu, lendario);
		srv.PalcoL_Forma(eu, forma);
		// o cliente pinta a forma, e o tique racial le o degrau novo -- com a forma SEGURA (o tanque cheio a cada quadro: sem maestria a forma
		// grande esvazia o Ki e cai pra base, e a r1 atirou da base no L09)
		await Ate(() => { srv.PalcoL_SegurarForma(eu, forma); return false; }, 0.8);
		srv.PalcoL_SegurarForma(eu, forma);
		// SEM POVO E DE CARA PRO LESTE: quem passou perto sai, e o corpo que alguem virou volta a olhar pro leste andando (o olhar e do cliente)
		srv.PalcoL_SemPovo(eu);
		if (srv.CorpoDaBoca(eu).Olhar != Facing.East)
		{
			await Ate(() => { mundo.AndarDeTeste(Vector2.Right); return srv.CorpoDaBoca(eu).Olhar == Facing.East; }, 2);
			mundo.PararDeTeste();
			await Segundos(0.35);
			srv.ReporNaBoca(eu);
			await Segundos(1.1);
		}
		string ficha = srv.PalcoL_Ficha(eu);
		_ditas.Clear();
		(string resposta, double custo) = srv.PalcoL_Atirar(eu, verbo, fracao);

		// ---- a CARGA: a bola na mao, cheia
		CargaDeRaioVisual? bola = null;
		bool carregando = await Ate(() => (bola = mundo.CorpoDeTeste(eu)?.GetNodeOrNull<CargaDeRaioVisual>(CargaDeRaioVisual.NomeDoNode)) != null, 3);
		Foto carga = default;
		if (carregando && bola != null)
		{
			await Segundos(IdadeDaCarga);
			if (IsInstanceValid(bola) && srv.PalcoL_Carregando(eu)) carga = await Fotografar(mundo, eu, bola, $"{foto}-carga.png", DaCarga, 50, 50, 50);
			else Nota($"{forma}: a carga acabou antes da foto dela");
		}

		// ---- o RAIO: a cabeca a 5 tiles da mao
		bool nasceu = await Ate(() => srv.PalcoL_Raio(eu).Achou, 6);
		srv.RegarOKiDeFoto(eu);   // o 2x ja foi decidido na carga: o tanque cheio segura o canal ate a foto
		ProjetilDesenhado? no = null;
		bool andou = nasceu && await Ate(() => srv.PalcoL_Raio(eu) is { Achou: true, Andou: >= TilesAntesDaFoto } r
												&& (no = mundo.FindChild($"Tiro{r.Id}", recursive: true, owned: false) as ProjetilDesenhado) != null, 4);
		var raio = srv.PalcoL_Raio(eu);
		Foto doRaio = andou && no != null && raio.Achou ? await Fotografar(mundo, eu, no, $"{foto}.png", DoRaio, 40, 70, 50, ateABorda: true) : default;

		await Ate(() => Disse(tecnica), 1.5);
		var t = new Tiro(raio.Achou, raio.DoLimite, raio.Onda, raio.Rumo.X > 0.9f, Disse("10x "), Disse(tecnica), carga, doRaio, ficha,
						 (nasceu ? "" : "o raio NAO NASCEU: ") + (resposta.Length > 0 ? $"respondeu: {resposta}" : "")
						 + $" (custo {custo:0}; ouvi: {string.Join(" | ", _ditas.TakeLast(2))})");
		srv.LimparOsTirosDaBoca(eu);
		await Segundos(0.6);
		return t;
	}

	// =====================================================================
	// O OBTURADOR E A TINTA
	// =====================================================================
	/// <summary>
	/// A FOTO DE UM NODE: as tres fotos do obturador, a de COM gravada (a tela inteira, com o chat), e a tinta medida numa janela em volta do
	/// corpo -- `esquerda`/`cima`/`baixo` em px de mundo a partir do corpo desenhado; a direita vai ate a borda (o raio) ou o mesmo tanto da
	/// esquerda (a bola na mao).
	/// </summary>
	private async System.Threading.Tasks.Task<Foto> Fotografar(World mundo, int eu, CanvasItem alvo, string arquivo, Regua regua, float esquerda,
															   float cima, float baixo, bool ateABorda = false)
	{
		Color cor = alvo switch { ProjetilDesenhado p => p.Cor, CargaDeRaioVisual c => c.Cor, _ => Colors.Black };
		(Image? com, Image? sem, Image? sem2) = await Obturar(alvo);
		if (com == null || sem == null || sem2 == null) { Nota($"{arquivo}: a janela nao renderizou (headless?) -- sem foto"); return new Foto(true, cor, 0, 0, 0, null, regua); }
		Gravar(com, arquivo);

		Vector2 corpo = NaImagem(com, mundo.PosicaoDesenhadaDe(eu) ?? Vector2.Zero);
		float e = EscalaDaImagem(com);
		int x0 = Math.Clamp((int)(corpo.X - esquerda * e), 0, com.GetWidth() - 1);
		int x1 = ateABorda ? com.GetWidth() : Math.Clamp((int)(corpo.X + esquerda * e), x0 + 1, com.GetWidth());
		int y0 = Math.Clamp((int)(corpo.Y - cima * e), 0, com.GetHeight() - 1), y1 = Math.Clamp((int)(corpo.Y + baixo * e), y0 + 1, com.GetHeight());
		int pixels = 0, vermelhos = 0, azuis = 0;
		Color soma = new(0, 0, 0, 0);
		for (int y = y0; y < y1; y++)
			for (int x = x0; x < x1; x++)
			{
				Color c = com.GetPixel(x, y), s = sem.GetPixel(x, y), s2 = sem2.GetPixel(x, y);
				if (Difere(s, s2) || !Difere(c, s) || !Difere(c, s2)) continue;
				pixels++;
				soma += c;
				if (c.R > 0.35f && c.R - Math.Max(c.G, c.B) > regua.Pixel) vermelhos++;
				if (c.B > 0.45f && c.B - Math.Max(c.R, c.G) > 0.15f) azuis++;
			}
		Image recorte = com.GetRegion(new Rect2I(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0)));
		recorte.Convert(Image.Format.Rgba8);
		if (pixels > 0) Nota($"{arquivo}: tinta {pixels} px, media ({soma.R / pixels:0.00}, {soma.G / pixels:0.00}, {soma.B / pixels:0.00})");
		return new Foto(true, cor, pixels, pixels > 0 ? vermelhos / (float)pixels : 0, pixels > 0 ? azuis / (float)pixels : 0, recorte, regua);
	}

	/// <summary>
	/// ESCONDE, FOTOGRAFA, MOSTRA, FOTOGRAFA, ESCONDE, FOTOGRAFA -- com a arvore pausada e tres quadros de folga a cada troca (`GetImage`
	/// devolve o ULTIMO quadro renderizado). Devolve (com, sem, sem de novo).
	/// </summary>
	private async System.Threading.Tasks.Task<(Image?, Image?, Image?)> Obturar(CanvasItem alvo)
	{
		_escondido = alvo;
		GetTree().Paused = true;
		try
		{
			alvo.Visible = false;
			await Quadros(3);
			Image? sem = Tela();
			if (!IsInstanceValid(alvo)) return (null, null, null);
			alvo.Visible = true;
			await Quadros(3);
			Image? com = Tela();
			if (!IsInstanceValid(alvo)) return (null, null, null);
			alvo.Visible = false;
			await Quadros(3);
			Image? sem2 = Tela();
			return (com, sem, sem2);
		}
		finally
		{
			if (IsInstanceValid(alvo)) alvo.Visible = true;
			_escondido = null;
			GetTree().Paused = false;
		}
	}

	private static bool Difere(Color p, Color q)
		=> MathF.Abs(p.R - q.R) > Epsilon || MathF.Abs(p.G - q.G) > Epsilon || MathF.Abs(p.B - q.B) > Epsilon;

	private Image? Tela()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;
		img.Convert(Image.Format.Rgba8);
		return img;
	}

	/// <summary>De ponto do mundo pra pixel da imagem (a `CanvasTransform` e a razao imagem / viewport).</summary>
	private Vector2 NaImagem(Image img, Vector2 mundo)
	{
		Vector2 v = (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * mundo;
		Vector2 tam = GetViewport()?.GetVisibleRect().Size ?? img.GetSize();
		return new Vector2(v.X * img.GetWidth() / tam.X, v.Y * img.GetHeight() / tam.Y);
	}

	/// <summary>Quantos pixels de imagem vale um pixel de mundo.</summary>
	private float EscalaDaImagem(Image img)
	{
		float e = (NaImagem(img, new Vector2(ZoneCollision.TileSize, 0)) - NaImagem(img, Vector2.Zero)).Length() / ZoneCollision.TileSize;
		return e > 0.01f ? e : 1f;
	}

	private void Gravar(Image img, string nome)
	{
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + nome);
			img.SavePng(caminho);
			_linhas.Add("  foto   " + caminho);
		}
		catch (Exception e) { Nota($"{nome}: sem foto: {e.Message}"); }
	}

	/// <summary>Os recortes um embaixo do outro (o `BlitRect` exige o mesmo formato nos dois lados).</summary>
	private void Colar(string nome, List<Image> pedacos, string rotulo)
	{
		if (pedacos.Count == 0) return;
		const int Vao = 6;
		int larg = 0, alt = 0;
		foreach (Image p in pedacos) { larg = Math.Max(larg, p.GetWidth()); alt += p.GetHeight() + Vao; }
		Image colagem = Image.CreateEmpty(Math.Max(1, larg), Math.Max(1, alt - Vao), false, Image.Format.Rgba8);
		colagem.Fill(new Color(0.06f, 0.06f, 0.06f));
		int y = 0;
		foreach (Image p in pedacos)
		{
			colagem.BlitRect(p, new Rect2I(Vector2I.Zero, p.GetSize()), new Vector2I(0, y));
			y += p.GetHeight() + Vao;
		}
		Nota(rotulo);
		Gravar(colagem, nome);
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

	private void Fechar()
	{
		foreach (string l in _linhas) GD.Print("[dlimite] " + l);
		GD.Print($"[dlimite] ===== {_ok} OK, {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dlimite]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
