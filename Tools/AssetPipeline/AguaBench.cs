using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// A BANCADA DA AGUA -- e ela e uma LISTA DE NEGACOES, porque o pedido do dono era uma.
///
/// ============================ POR QUE FAMILIAS, E NAO UMA LISTA DE PROVAS ============================
/// O dono nao descreveu um recurso: ele descreveu uma CLASSE DE CELULA dizendo, item por item, o que
/// ela NAO e --
///
///     "todo TILE DE AGUA n da pra passar andando, so NADANDO ou usando o FLIGHT, ela e tipo uma
///      parede, mas N DA PRA SOCAR (igual o chao), n tem SOMBRA nem nada disso, simplesmente e um
///      chao q n da pra andar por cima, so voando ou nadando"
///
/// Cada negacao dessas tem um CONSUMIDOR diferente no jogo (o passo, o punho, o `.vis`, a IA, o
/// gerador), e o jeito conhecido de uma passar batido e provar so a primeira -- "nao da pra andar
/// por cima, pronto" -- e descobrir seis meses depois que o lago fazia sombra ou que dava pra socar.
/// Entao cada negacao virou uma FAMILIA: um grupo de provas que morrem juntas e que tem dono.
///
/// ============================ E CADA FAMILIA TEM QUE SABER REPROVAR ============================
/// Uma prova que nunca ficou vermelha nao e uma prova, e uma frase. Toda familia aqui declara os
/// DEFEITOS que ela existe pra pegar, a bancada INJETA cada um e exige que a familia fique vermelha.
/// Se ela continuar verde com o defeito dentro, isso e reportado como `CEGA` -- e um buraco de
/// cobertura, nao um sucesso.
///
/// Os defeitos nao sao inventados: cada um e um erro que este projeto ja cometeu ou que a proxima
/// pessoa cometeria de boa fe. "Marcar agua no `.col`" e a tentacao obvia (ela *e* tipo uma parede);
/// "o chamador esqueceu de passar o modo" foi achado de verdade no `GameServer.Voo.cs:315`; "a agua
/// entrou no `.vis`" e o que acontece no dia em que alguem por `Density = true` no `DmTurfScanner`.
///
/// ============================ OS CONTRA-EXEMPLOS SAO METADE DA BANCADA ============================
/// Metade das provas daqui afirma o CONTRARIO da agua, e elas nao sao enfeite: sem "a parede normal
/// QUEBRA", um soco que nunca alcanca nada passaria verde e o cenario destrutivel estaria morto sem
/// ninguem notar; sem "a parede normal ESCONDE", uma vista que atravessa tudo passaria verde; sem "o
/// VOO sobe e faz sombra", um jogo sem sombra nenhuma passaria verde. Um sistema so esta provado
/// quando as duas respostas -- sim e nao -- saem do mesmo lugar.
/// ==================================================================================================
///
///     dotnet run --project Tools/AssetPipeline -- agua-prova [pastaMaps] [raizDoRepo]
/// </summary>
public static class AguaBench
{
	// ==================================================================================
	// AS PECAS: o cenario (dados) e as regras (as funcoes de producao que o jogo chama)
	// ==================================================================================

	/// <summary>O passo do movimento, com o modo explicito. E o `MoveRules.Advance` de producao.</summary>
	private delegate Vec2 PassoDelegado(Vec2 pos, Vec2 dir, float dt, float vel,
										ZoneCollision? mapa, ModoDeTravessia modo);

	/// <summary>
	/// AS PERGUNTAS QUE O JOGO FAZ -- por padrao, as funcoes DE PRODUCAO, ponto.
	///
	/// ============================ POR QUE DELEGADOS E NAO CHAMADAS DIRETAS ============================
	/// Porque e isto que permite injetar o defeito de verdade. Nem todo defeito e um dado errado --
	/// "o soco parou de perguntar a classe e voltou a olhar so o bitset" e uma mudanca de CODIGO, e a
	/// unica forma honesta de provar que a familia pegaria isso e rodar as MESMAS provas contra o
	/// codigo mutante. Trocar um campo aqui e trocar a implementacao debaixo das provas.
	///
	/// O caminho saudavel nao paga nada por isso: o valor inicial de cada campo e o metodo de
	/// producao, e a bancada verde e a bancada medindo o jogo.
	/// ================================================================================================
	/// </summary>
	private sealed class Regras
	{
		/// <summary>`MoveRules.Occupied` -- esta celula PARA este corpo?</summary>
		public Func<ZoneCollision, Vec2, ModoDeTravessia, bool> Para = MoveRules.Occupied;

		/// <summary>`MoveRules.Advance` -- o passo com deslize.</summary>
		public PassoDelegado Passo =
			(p, d, dt, v, m, modo) => MoveRules.Advance(p, d, dt, v, m, out _, false, modo);

		/// <summary>`ClasseDeAgua.SocoAlcanca` -- o punho alcanca o cenario desta celula?</summary>
		public Func<ZoneCollision, int, int, bool> SocoAlcanca = ClasseDeAgua.SocoAlcanca;

		/// <summary>
		/// `MoveRules.ValidateStep` -- o servidor conferindo o passo que o cliente afirmou.
		///
		/// Ela entra aqui, e nao como chamada direta, porque o defeito "o chamador esqueceu o modo"
		/// tem DOIS lados: se so o passo do cliente for consertado, o servidor recusa o que o cliente
		/// faz de certo -- e o sintoma disso e o corpo tremendo, que e pior que a agua nao funcionar.
		/// </summary>
		public Func<Vec2, Vec2, ZoneCollision, ModoDeTravessia, bool> Valida =
			(de, ate, m, modo) =>
			{
				float orcamento = 0f;
				return MoveRules.ValidateStep(de, ate, 1f / 30, 1f, m, ref orcamento, out _, false, modo);
			};

		/// <summary>A vista de um ponto a outro, pelo mapa do que CEGA (o `.vis`).</summary>
		public Func<ZoneCollision, Vec2, Vec2, bool> Ve = (vis, a, b) => !vis.PathBlocked(a, b);

		/// <summary>`Voo.TemSombra` -- esta altura projeta sombra no chao?</summary>
		public Func<float, bool> TemSombra = Voo.TemSombra;

		/// <summary>`GeradorDeTerreno.Gerar` -- o mundo por semente.</summary>
		public Func<ParametrosDeTerreno, TerrenoGerado> Gerar = GeradorDeTerreno.Gerar;

		/// <summary>
		/// `ZoneCollision.PontoLivrePerto` -- o funil de "onde da pra POR um corpo".
		///
		/// E por ele que passam o berco, o pouso de nave, a invasao e o povoamento, e e nele que a
		/// clausula de agua foi escrita. Ele entra aqui porque e o unico dos dois caminhos de pouso
		/// que da pra reprovar de verdade -- ver a familia 6.
		/// </summary>
		public Func<ZoneCollision, Vec2, Vec2> PontoLivre = (m, v) => m.PontoLivrePerto(v);

		/// <summary>
		/// A ALTURA QUE O NADO PRODUZ. Zero -- e a frase inteira do dono ("N CRIA A SOMBRA em baixo e
		/// NEM FICA MAIS ALTO") sai dela sozinha. Injetar um valor aqui e o defeito "nadar virou voo
		/// rente ao chao".
		/// </summary>
		public float AlturaDoNado;

		/// <summary>
		/// COMO A BANCADA LE O FONTE DO SERVIDOR. Existe pra a prova estrutural da familia 5 poder
		/// ser reprovada: o defeito e injetado devolvendo o arquivo com uma linha a mais.
		/// </summary>
		public Func<string, string> LerFonte = File.ReadAllText;

		/// <summary>O que a injecao faz com o mapa depois de montado (por padrao, nada).</summary>
		public Action<Cenario> MexerNoMapa = _ => { };
	}

	/// <summary>
	/// O MAPA DE TESTE: um lago de 4 tiles de largura e um muro de verdade, no mesmo mundo.
	///
	/// Os dois juntos, e nao um por vez, porque quase toda prova daqui e um PAR: "a agua faz X" so
	/// significa alguma coisa ao lado de "a parede faz Y". Ter os dois no mesmo mapa tambem garante
	/// que a diferenca medida vem da CLASSE DA CELULA, e nao de dois mapas diferentes.
	///
	///        col:  . . . . . . . . [muro em MuroX]
	///        agua: . . [LagoX0..LagoX1] . . . . .
	///        vis:  . . . . . . . . [muro em MuroX]   <- a agua NAO entra aqui
	/// </summary>
	private sealed class Cenario
	{
		public const int Lado = 48;
		public const int LagoX0 = 18, LagoX1 = 21;
		public const int MuroX = 34;
		public const int Linha = 20;

		/// <summary>O mapa que PARA o corpo. E ele que carrega o plano de agua.</summary>
		public required ZoneCollision Col { get; init; }

		/// <summary>O mapa que CEGA -- o irmao do `.vis`. So o muro entra aqui.</summary>
		public required ZoneCollision Vis { get; init; }

		/// <summary>
		/// O MESMO MUNDO SEM NADA DENTRO -- a regua do deslize.
		///
		/// Ele existe porque uma prova de deslize sem controle fica verde com o deslize QUEBRADO: um
		/// corpo indo na diagonal ja anda em Y ate encostar, e esse pedaco sozinho passou por
		/// "deslizou" na primeira versao desta bancada (a injecao "o deslize sumiu" saiu CEGA). A
		/// pergunta certa nao e "andou em Y?", e "andou em Y TANTO QUANTO andaria sem obstaculo?".
		/// </summary>
		public required ZoneCollision Vazio { get; init; }

		/// <summary>
		/// O MESMO MAPA COM O LAGO VIRADO PAREDE -- o controle da familia 7.
		///
		/// Nao e um defeito injetado: e a REFERENCIA. A afirmacao "a IA nao trava na agua" so tem
		/// conteudo comparada com o que ela ja fazia num muro, que e o obstaculo que sempre existiu.
		/// </summary>
		public required ZoneCollision LagoComoParede { get; init; }

		/// <summary>Centro de um tile, na altura em que a caixa dos pes cabe dentro dele.</summary>
		public static Vec2 NoTile(int cx, int cy) =>
			new(cx * ZoneCollision.TileSize + 16, cy * ZoneCollision.TileSize + 16 - MoveRules.FeetOffsetY);

		public static Vec2 Oeste => NoTile(10, Linha);        // terra firme, antes do lago
		public static Vec2 Leste => NoTile(26, Linha);        // terra firme, depois do lago
		public static Vec2 Dentro => NoTile(19, Linha);       // dentro do lago
		public static Vec2 DepoisDoMuro => NoTile(40, Linha); // do outro lado do muro
	}

	// ==================================================================================
	// AS DUAS INJECOES DE DADO -- e elas leem o plano de agua em vez de saber coordenadas
	// ==================================================================================

	/// <summary>
	/// "ALGUEM MARCOU AGUA NO `.col`" -- a tentacao obvia, porque ela *e* tipo uma parede.
	///
	/// Le o proprio plano de agua do mapa em vez de carregar as coordenadas do lago sintetico: assim
	/// a mesma injecao vale pro cenario de teste E pro mapa da Terra, sem duas versoes pra manter.
	/// </summary>
	private static void AguaViraParede(Cenario c)
	{
		for (int y = 0; y < c.Col.Height; y++)
			for (int x = 0; x < c.Col.Width; x++)
				if (c.Col.EhAgua(x, y)) c.Col.Bloquear(x, y);
	}

	/// <summary>"A AGUA ENTROU NO `.vis`" -- o `Density = true` no `DmTurfScanner`.</summary>
	private static void AguaVaiCegar(Cenario c)
	{
		for (int y = 0; y < c.Col.Height; y++)
			for (int x = 0; x < c.Col.Width; x++)
				if (c.Col.EhAgua(x, y)) c.Vis.Bloquear(x, y);
	}

	/// <summary>Monta o cenario e deixa a injecao mexer nele.</summary>
	private static Cenario Montar(Regras r)
	{
		int w = Cenario.Lado, h = Cenario.Lado;
		int bytes = (w * h + 7) / 8;
		var col = new byte[bytes];
		var agua = new byte[bytes];
		var vis = new byte[bytes];
		var lagoParede = new byte[bytes];

		for (int y = 0; y < h; y++)
		{
			for (int x = Cenario.LagoX0; x <= Cenario.LagoX1; x++)
			{
				Marcar(agua, y * w + x);
				Marcar(lagoParede, y * w + x);   // o controle: o mesmo lago, como parede
			}
			// o muro de verdade: bloqueia (col) E cega (vis), nos tres mapas
			Marcar(col, y * w + Cenario.MuroX);
			Marcar(vis, y * w + Cenario.MuroX);
			Marcar(lagoParede, y * w + Cenario.MuroX);
		}

		ZoneCollision mapa = ZoneCollision.Montar(w, h, col);
		mapa.DefinirAgua(agua);

		var c = new Cenario
		{
			Col = mapa,
			Vis = ZoneCollision.Montar(w, h, vis),
			LagoComoParede = ZoneCollision.Montar(w, h, lagoParede),
			Vazio = ZoneCollision.Montar(w, h, new byte[bytes]),
		};
		r.MexerNoMapa(c);
		return c;

		static void Marcar(byte[] b, int i) => b[i >> 3] |= (byte)(1 << (i & 7));
	}

	// ==================================================================================
	// O MAPA DE VERDADE: a Terra, com 44% de agua
	// ==================================================================================

	/// <summary>
	/// O DADO DE PRODUCAO. Cenario sintetico prova a REGRA; so o mapa de verdade prova que a regra
	/// foi APLICADA ao mundo em que se joga -- sao 110 mil celulas de agua escritas por um conversor
	/// que pode ter marcado a coisa errada.
	///
	/// Os bytes ficam em cache e cada rodada faz um `Load` NOVO: as injecoes mexem no objeto (a
	/// camada de obras do `Bloquear`), e um objeto compartilhado levaria o defeito de uma familia
	/// pra dentro da seguinte.
	/// </summary>
	private sealed class Terra
	{
		public required byte[] BytesCol { get; init; }
		public required byte[] BytesAgua { get; init; }
		public required byte[] BytesVis { get; init; }

		/// <summary>Uma travessia de verdade: seco, agua estreita o bastante pra atravessar, seco.</summary>
		public int TravessiaX, TravessiaY, TravessiaLargura;

		public (ZoneCollision col, ZoneCollision vis) Abrir()
		{
			ZoneCollision col = ZoneCollision.Load(BytesCol)!;
			col.CarregarAgua(BytesAgua);
			return (col, ZoneCollision.Load(BytesVis)!);
		}
	}

	private static Terra? _terra;

	private static Terra? CarregarTerra(string pastaMaps)
	{
		string col = Path.Combine(pastaMaps, "z01_Earth.col");
		string agua = Path.Combine(pastaMaps, "z01_Earth.agua");
		string vis = Path.Combine(pastaMaps, "z01_Earth.vis");
		if (!File.Exists(col) || !File.Exists(agua) || !File.Exists(vis)) return null;

		var t = new Terra
		{
			BytesCol = File.ReadAllBytes(col),
			BytesAgua = File.ReadAllBytes(agua),
			BytesVis = File.ReadAllBytes(vis),
		};

		// ACHA UM RIO DE VERDADE: uma faixa horizontal com terra seca dos dois lados. E o unico
		// jeito de a prova "atravessou nadando" ter significado no mapa real -- num oceano de 200
		// tiles ninguem atravessa em 10 segundos, e a prova ficaria vermelha estando tudo certo.
		(ZoneCollision mapa, _) = t.Abrir();
		for (int y = 4; y < mapa.Height - 4 && t.TravessiaLargura == 0; y++)
			for (int x = 8; x < mapa.Width - 48; x++)
			{
				if (!mapa.EhAgua(x, y) || mapa.EhAgua(x - 1, y)) continue;   // so o comeco do rio
				int n = 0;
				while (n < 24 && mapa.EhAgua(x + n, y)) n++;
				if (n < 3 || mapa.EhAgua(x + n, y)) continue;                // largo demais: pula
				if (!SecoELivre(mapa, x - 6, x - 1, y) || !SecoELivre(mapa, x + n, x + n + 7, y)) continue;
				t.TravessiaX = x; t.TravessiaY = y; t.TravessiaLargura = n;
				break;
			}
		return t;

		static bool SecoELivre(ZoneCollision m, int x0, int x1, int y)
		{
			for (int x = x0; x <= x1; x++)
				if (m.EhAgua(x, y) || m.BlockedCell(x, y) || m.EhAgua(x, y + 1) || m.BlockedCell(x, y + 1))
					return false;
			return true;
		}
	}

	// ==================================================================================
	// O MOTOR: familias, placar e injecao
	// ==================================================================================

	private sealed class Placar
	{
		public int Ok, Falhas;
		public bool Mudo;
		public readonly List<string> Vermelhas = [];

		public void Prova(string oQue, bool passou)
		{
			if (passou) Ok++; else { Falhas++; Vermelhas.Add(oQue); }
			if (!Mudo) Console.WriteLine($"  [{(passou ? "ok  " : "FALHA")}] {oQue}");
		}

		/// <summary>Prova que nao deu pra fazer (falta o mapa de verdade, falta o fonte).</summary>
		public readonly List<string> SemCobertura = [];
		public void NaoDeu(string oQue)
		{
			SemCobertura.Add(oQue);
			if (!Mudo) Console.WriteLine($"  [ -- ] {oQue}");
		}
	}

	private sealed class Familia
	{
		public required string Nome { get; init; }
		public required string Frase { get; init; }
		public required Action<Regras, Placar> Provas { get; init; }
		public required List<(string Nome, Action<Regras> Injetar)> Defeitos { get; init; }
	}

	public static int Run(string pastaMaps, string raiz)
	{
		_terra = CarregarTerra(pastaMaps);
		Console.WriteLine("============================================================");
		Console.WriteLine(" BANCADA DA AGUA -- uma familia por negacao do pedido");
		Console.WriteLine("============================================================");
		Console.WriteLine(_terra == null
			? $"  mapa de verdade: NAO ENCONTRADO em {pastaMaps} (as provas de dado real ficam de fora)"
			: $"  mapa de verdade: z01_Earth | travessia achada em ({_terra.TravessiaX},{_terra.TravessiaY}), "
			  + $"{_terra.TravessiaLargura} tiles de agua");
		Console.WriteLine($"  fonte do nado    : {(File.Exists(FonteDoNado(raiz)) ? FonteDoNado(raiz) : "NAO ENCONTRADO (prova estrutural fica de fora)")}");
		Console.WriteLine($"  fonte do voo     : {(File.Exists(FonteDoVoo(raiz)) ? FonteDoVoo(raiz) : "NAO ENCONTRADO (prova estrutural fica de fora)")}");

		List<Familia> familias = [
			APeNaoAtravessa(),
			NadandoEVoandoAtravessam(),
			NaoDaPraSocar(),
			NaoFazSombra(),
			NadarNaoSobeNemFazSombra(),
			MesmaSementeMesmoMundo(),
			IaNaoTrava(),
		];

		int provas = 0, falhas = 0, defeitos = 0, cegos = 0, semCobertura = 0;
		var buracos = new List<string>();

		foreach (Familia f in familias)
		{
			Console.WriteLine($"\n=== {f.Nome} ===");
			Console.WriteLine($"    \"{f.Frase}\"");

			var sao = new Placar();
			f.Provas(new Regras(), sao);
			provas += sao.Ok + sao.Falhas;
			falhas += sao.Falhas;
			semCobertura += sao.SemCobertura.Count;
			foreach (string s in sao.SemCobertura) buracos.Add($"{f.Nome}: {s}");

			Console.WriteLine("  -- e ela reprova assim:");
			foreach ((string nome, Action<Regras> injetar) in f.Defeitos)
			{
				defeitos++;
				var r = new Regras();
				injetar(r);
				var p = new Placar { Mudo = true };
				try { f.Provas(r, p); }
				catch (Exception e)
				{
					// UM DEFEITO QUE FAZ A BANCADA ESTOURAR TAMBEM E UM DEFEITO PEGO -- so nao pode
					// passar despercebido, entao ele sai com o nome da excecao.
					p.Prova($"estourou: {e.GetType().Name}", false);
				}

				if (p.Falhas == 0)
				{
					cegos++;
					Console.WriteLine($"     [CEGA] {nome}");
					Console.WriteLine("            ...a familia continuou VERDE com o defeito dentro.");
					buracos.Add($"{f.Nome}: cega para \"{nome}\"");
				}
				else
				{
					Console.WriteLine($"     [pega] {nome}");
					Console.WriteLine($"            -> {p.Falhas} prova(s) em vermelho, a primeira: \"{Curto(p.Vermelhas[0])}\"");
				}
			}
		}

		Console.WriteLine("\n============================================================");
		Console.WriteLine(" PLACAR");
		Console.WriteLine("============================================================");
		Console.WriteLine($"  familias           : {familias.Count}");
		Console.WriteLine($"  provas             : {provas}   ({provas - falhas} verdes, {falhas} vermelhas)");
		Console.WriteLine($"  defeitos injetados : {defeitos}   ({defeitos - cegos} pegos, {cegos} passaram batido)");
		Console.WriteLine($"  provas sem rodar   : {semCobertura}");
		if (buracos.Count > 0)
		{
			Console.WriteLine("\n  O QUE FICOU SEM COBERTURA:");
			foreach (string b in buracos) Console.WriteLine($"    - {b}");
		}
		bool ok = falhas == 0 && cegos == 0;
		Console.WriteLine($"\n  {(ok ? "OK -- toda familia esta verde E sabe ficar vermelha."
							   : "ATENCAO -- ha familia vermelha ou cega acima.")}");
		return ok ? 0 : 1;
	}

	private static string Curto(string s) => s.Length <= 72 ? s : s[..70] + "..";

	// ==================================================================================
	// FAMILIA 1 -- A PE NAO ATRAVESSA
	// ==================================================================================

	/// <summary>
	/// O ITEM LITERAL DO PEDIDO: *"todo TILE DE AGUA n da pra passar andando"*.
	///
	/// As tres provas sao os tres lugares onde "andando" e decidido, e eles nao sao o mesmo codigo:
	/// o PASSO do cliente (`Advance`), a PERGUNTA seca (`Occupied`) e a VALIDACAO do servidor
	/// (`ValidateStep`). Uma so das tres passar seria o pior desfecho deste projeto -- o cliente e o
	/// servidor discordando sobre onde da pra andar, com o corpo tremendo na costura.
	/// </summary>
	private static Familia APeNaoAtravessa() => new()
	{
		Nome = "FAMILIA 1 -- A PE NAO ATRAVESSA",
		Frase = "n da pra passar andando",
		Provas = (r, p) =>
		{
			Cenario c = Montar(r);

			// ---- o passo: anda 10 s de leste contra o lago
			Caminhada a = Andar(r, c.Col, Cenario.Oeste, new Vec2(1, 0), ModoDeTravessia.APe, 600);
			p.Prova($"a pe o corpo PARA na beira (x={a.Fim.X:0}, o lago comeca em {Cenario.LagoX0 * 32})",
					a.Fim.X < Cenario.LagoX0 * 32);
			p.Prova("...e em nenhum quadro os pes entraram na agua", a.PxNaAgua == 0);

			// ---- a pergunta seca
			p.Prova("`Occupied` a pe devolve BLOQUEADO dentro do lago",
					r.Para(c.Col, Cenario.Dentro, ModoDeTravessia.APe));

			// ---- a validacao do servidor: o cliente afirma um passo pra dentro da agua
			Vec2 beira = a.Fim;
			p.Prova("o SERVIDOR recusa o passo que o cliente afirmar por cima da agua",
					!r.Valida(beira, beira + new Vec2(6, 0), c.Col, ModoDeTravessia.APe));

			// ---- e no mapa de verdade
			if (_terra is not { TravessiaLargura: > 0 } t) { p.NaoDeu("o mesmo, no rio de verdade da Terra"); return; }
			(ZoneCollision terra, _) = t.Abrir();
			r.MexerNoMapa(new Cenario { Col = terra, Vis = terra, LagoComoParede = terra, Vazio = terra });

			Vec2 partida = Cenario.NoTile(t.TravessiaX - 4, t.TravessiaY);
			Caminhada b = Andar(r, terra, partida, new Vec2(1, 0), ModoDeTravessia.APe, 600);
			p.Prova($"no rio de verdade da Terra ({t.TravessiaLargura} tiles), a pe ele NAO atravessa",
					b.PxNaAgua == 0 && b.Fim.X < t.TravessiaX * 32);
		},
		Defeitos = [
			("o `.agua` nao foi carregado (plano nulo -- o `.col` abre, a agua some)",
			 r => r.MexerNoMapa = c => c.Col.DefinirAgua(null)),

			("o chamador passou `Voando` por engano (o modo errado nos tres funis)",
			 r => { r.Para = (m, v, _) => MoveRules.Occupied(m, v, ModoDeTravessia.Voando);
					r.Passo = (pos, d, dt, v, m, ignorado) =>
						MoveRules.Advance(pos, d, dt, v, m, out _, false, ModoDeTravessia.Voando);
					r.Valida = (de, ate, m, ignorado) => { float o = 0f;
						return MoveRules.ValidateStep(de, ate, 1f / 30, 1f, m, ref o, out Vec2 _, false,
													  ModoDeTravessia.Voando); }; }),

			("a agua foi gravada no mapa que CEGA e nao no que PARA (os arquivos trocados)",
			 r => r.MexerNoMapa = c => { AguaVaiCegar(c); c.Col.DefinirAgua(null); }),
		],
	};

	// ==================================================================================
	// FAMILIA 2 -- NADANDO ATRAVESSA, VOANDO ATRAVESSA
	// ==================================================================================

	/// <summary>
	/// OS DOIS CONTRA-EXEMPLOS DO PEDIDO: *"so NADANDO ou usando o FLIGHT"*.
	///
	/// Sem eles, "agua e parede" passaria verde na familia 1 inteira -- e parede foi exatamente o
	/// que o dono disse que ela NAO e. Aqui tambem mora a prova que impede o inverso: nadando, a
	/// PAREDE continua parando. Sem ela, "nadar" seria so um nome pra voar.
	/// </summary>
	private static Familia NadandoEVoandoAtravessam() => new()
	{
		Nome = "FAMILIA 2 -- NADANDO ATRAVESSA, VOANDO ATRAVESSA",
		Frase = "so NADANDO ou usando o FLIGHT",
		Provas = (r, p) =>
		{
			Cenario c = Montar(r);

			// ---- NADANDO: atravessa o lago... e para no muro que vem depois
			Caminhada n = Andar(r, c.Col, Cenario.Oeste, new Vec2(1, 0), ModoDeTravessia.Nadando, 600);
			p.Prova($"NADANDO ele atravessa o lago (chegou a x={n.Fim.X:0}, o lago acaba em {(Cenario.LagoX1 + 1) * 32})",
					n.Fim.X > (Cenario.LagoX1 + 1) * 32);
			p.Prova($"...e passou POR CIMA da agua de verdade ({n.PxNaAgua:0} px com os pes nela)",
					n.PxNaAgua > ZoneCollision.TileSize);
			p.Prova($"...e a PAREDE continua parando quem nada (parou em x={n.Fim.X:0} < {Cenario.MuroX * 32})",
					n.Fim.X < Cenario.MuroX * 32);

			// ---- VOANDO BAIXO: a janela da decolagem, em que o corpo AINDA consulta o mapa
			Caminhada v = Andar(r, c.Col, Cenario.Oeste, new Vec2(1, 0), ModoDeTravessia.Voando, 600);
			p.Prova("VOANDO (ainda na janela da decolagem, com mapa) ele atravessa",
					v.Fim.X > (Cenario.LagoX1 + 1) * 32);
			p.Prova("...e a parede tambem para quem voa baixo (so quem passa de 1 tile ignora o mapa)",
					v.Fim.X < Cenario.MuroX * 32);

			// ---- VOANDO ALTO: nem ha mapa (o `isflying` do original manda `mapa = null`)
			p.Prova("voando ALTO nao ha mapa nenhum a consultar (`AtravessaCenario`)",
					Voo.AtravessaCenario(Voo.AlturaQueAtravessa)
					&& r.Passo(Cenario.Dentro, new Vec2(1, 0), 1f / 60, 1f, null, ModoDeTravessia.Voando).X > Cenario.Dentro.X);

			// ---- ARREMESSADO: o `M.KB` do `testWaters`
			Caminhada k = Andar(r, c.Col, Cenario.Oeste, new Vec2(1, 0), ModoDeTravessia.Arremessado, 600);
			p.Prova("ARREMESSADO atravessa (o `M.KB` do `Swim.dm:31`)", k.Fim.X > (Cenario.LagoX1 + 1) * 32);

			// ---- o modo, que e a origem de tudo isso
			p.Prova("o modo sai de UM dono so: a pe/nadando/no ar/arremessado",
					ClasseDeAgua.ModoDe(false, false, false) == ModoDeTravessia.APe
					&& ClasseDeAgua.ModoDe(false, false, true) == ModoDeTravessia.Nadando
					&& ClasseDeAgua.ModoDe(false, true, false) == ModoDeTravessia.Voando
					&& ClasseDeAgua.ModoDe(true, true, true) == ModoDeTravessia.Arremessado);

			// ============================ A PROVA ESTRUTURAL: O POUSO PERGUNTA COM O MODO ============================
			// Esta bancada ja sabia dizer "o chamador esqueceu o modo" -- o defeito esta injetado logo
			// abaixo desde a primeira versao. O que ela NAO sabia era APONTAR o chamador: as funcoes
			// aqui sao puras, e um chamador esquecido mora no servidor, que nao roda daqui.
			//
			// E havia um esquecido de verdade. `GameServer.Voo.cs`, no `DescerAte`, perguntava
			// `MoveRules.Occupied(mapa, pl.Pos)` sem modo -- ou seja, A PE --, e a pe a agua para:
			// quem largava o voo em cima do lago NADANDO caia no desvio do "pousou dentro da pedra",
			// era teleportado pra margem, e o tique seguinte apagava o nado por falta de agua embaixo.
			// Um dos dois caminhos que o jogador tem pra comecar a nadar, morto por um argumento
			// omitido.
			//
			// Ler o fonte e feio, e e o unico jeito honesto: nao ha funcao pura a que perguntar "o
			// pouso passou o modo?". A alternativa era uma COPIA da regra dentro da bancada, e copia
			// que concorda consigo mesma fica verde pra sempre.
			// =====================================================================================================
			string arqVoo = _fonteDoVoo;
			if (arqVoo.Length == 0 || !File.Exists(arqVoo)) p.NaoDeu("o `DescerAte` pergunta com o MODO");
			else
			{
				List<string> nuas = r.LerFonte(arqVoo).Split('\n')
					.Where(l => !l.TrimStart().StartsWith("//"))
					.Where(l => l.Contains("MoveRules.Occupied(mapa, pl.Pos)"))
					.Select(l => l.Trim()).ToList();
				p.Prova($"`{Path.GetFileName(arqVoo)}` nunca pergunta `Occupied` SEM o modo"
						+ (nuas.Count > 0 ? $" (achei: {Curto(nuas[0])})" : ""),
						nuas.Count == 0);
			}

			if (_terra is not { TravessiaLargura: > 0 } t) { p.NaoDeu("atravessar nadando o rio de verdade"); return; }
			(ZoneCollision terra, _) = t.Abrir();
			r.MexerNoMapa(new Cenario { Col = terra, Vis = terra, LagoComoParede = terra, Vazio = terra });
			Vec2 partida = Cenario.NoTile(t.TravessiaX - 4, t.TravessiaY);
			Caminhada b = Andar(r, terra, partida, new Vec2(1, 0), ModoDeTravessia.Nadando, 600);
			p.Prova($"no rio de verdade da Terra, NADANDO ele chega na outra margem "
					+ $"({b.PxNaAgua:0} px sobre agua, saiu em x={b.Fim.X / 32:0})",
					b.PxNaAgua > ZoneCollision.TileSize && b.Fim.X > (t.TravessiaX + t.TravessiaLargura) * 32);
		},
		Defeitos = [
			("a agua foi marcada no `.col` (\"e tipo uma parede\") -- e ai nem quem nada passa",
			 r => r.MexerNoMapa = AguaViraParede),

			("o chamador esqueceu o modo (o `Occupied` sem argumento -- foi o defeito do `DescerAte`)",
			 r => { r.Para = (m, v, _) => MoveRules.Occupied(m, v);
					r.Passo = (pos, d, dt, v, m, ignorado) => MoveRules.Advance(pos, d, dt, v, m, out _); }),

			("o `DescerAte` VOLTOU a perguntar sem o modo (o pouso do nadador vira desvio pra margem)",
			 r => { Func<string, string> ler = r.LerFonte;
					r.LerFonte = a => ler(a)
						+ "\n\t\tif (MoveRules.Occupied(mapa, pl.Pos)) DesviarProLado(pl);\n"; }),

			("`ClasseDeAgua.Bloqueia` passou a valer pra todo modo (a excecao virou regra)",
			 r => { r.Para = (m, v, _) => MoveRules.Occupied(m, v, ModoDeTravessia.APe);
					r.Passo = (pos, d, dt, v, m, ignorado) =>
						MoveRules.Advance(pos, d, dt, v, m, out _, false, ModoDeTravessia.APe); }),
		],
	};

	// ==================================================================================
	// FAMILIA 3 -- NAO DA PRA SOCAR
	// ==================================================================================

	/// <summary>
	/// *"ela e tipo uma parede, mas N DA PRA SOCAR (igual o chao)"*.
	///
	/// O CONTRA-EXEMPLO E OBRIGATORIO AQUI, e mais do que em qualquer outra familia: "nada quebra"
	/// satisfaz "agua nao quebra" perfeitamente, e um soco que parasse de alcancar QUALQUER cenario
	/// deixaria o destrutivel inteiro morto com a bancada verde. Entao toda prova de negacao vem em
	/// par com a mesma pergunta feita a uma parede de verdade.
	/// </summary>
	private static Familia NaoDaPraSocar() => new()
	{
		Nome = "FAMILIA 3 -- NAO DA PRA SOCAR",
		Frase = "N DA PRA SOCAR (igual o chao)",
		Provas = (r, p) =>
		{
			Cenario c = Montar(r);

			p.Prova("o punho NAO alcanca a celula de agua", !r.SocoAlcanca(c.Col, 19, Cenario.Linha));
			p.Prova("...mas alcanca a PAREDE de verdade ao lado (senao 'nada quebra' passaria verde)",
					r.SocoAlcanca(c.Col, Cenario.MuroX, Cenario.Linha));
			p.Prova("...e nao alcanca chao livre nenhum", !r.SocoAlcanca(c.Col, 5, Cenario.Linha));

			// SOCAR NAO MUDA NADA: a celula continua sendo agua depois da pergunta. O soco de
			// verdade so chama `DerrubarCelula` quando esta funcao diz sim -- entao "nada mudou" e
			// exatamente "ela disse nao".
			bool antes = c.Col.EhAgua(19, Cenario.Linha);
			for (int i = 0; i < 8; i++) r.SocoAlcanca(c.Col, 19, Cenario.Linha);
			p.Prova("depois de 8 socos a celula continua sendo agua (nada foi derrubado)",
					antes && c.Col.EhAgua(19, Cenario.Linha));

			if (_terra == null) { p.NaoDeu("as 110 mil celulas de agua da Terra, uma a uma"); return; }
			(ZoneCollision terra, _) = _terra.Abrir();
			r.MexerNoMapa(new Cenario { Col = terra, Vis = terra, LagoComoParede = terra, Vazio = terra });

			int agua = 0, aguaSocavel = 0, parede = 0, paredeSocavel = 0;
			for (int y = 0; y < terra.Height; y++)
				for (int x = 0; x < terra.Width; x++)
				{
					if (terra.EhAgua(x, y)) { agua++; if (r.SocoAlcanca(terra, x, y)) aguaSocavel++; }
					else if (terra.BlockedCell(x, y)) { parede++; if (r.SocoAlcanca(terra, x, y)) paredeSocavel++; }
				}
			p.Prova($"na Terra, {aguaSocavel} das {agua:N0} celulas de agua sao socaveis", aguaSocavel == 0);
			p.Prova($"...e {paredeSocavel:N0} das {parede:N0} paredes SAO (o destrutivel esta vivo)",
					paredeSocavel == parede && parede > 0);
		},
		Defeitos = [
			("o soco voltou a olhar so o bitset, e alguem marcou agua no `.col`",
			 r => { r.SocoAlcanca = (m, x, y) => m.BlockedCell(x, y);
					r.MexerNoMapa = AguaViraParede; }),

			("alguem achou que agua devia ceder ao soco (`Destrutivel` virou true)",
			 r => r.SocoAlcanca = (m, x, y) => m.EhAgua(x, y) || m.BlockedCell(x, y)),

			("o soco parou de alcancar QUALQUER cenario (o destrutivel morreu calado)",
			 r => r.SocoAlcanca = (_, _, _) => false),
		],
	};

	// ==================================================================================
	// FAMILIA 4 -- NAO FAZ SOMBRA
	// ==================================================================================

	/// <summary>
	/// *"n tem SOMBRA nem nada disso"* -- e aqui "sombra" e OCLUSAO DE VISTA, o leque preto que uma
	/// parede projeta. (A outra sombra, a mancha embaixo do corpo, e a familia 5.)
	///
	/// A resposta sai de graca e vale dizer por que: o `.vis` e um arquivo separado do `.col` e so
	/// recebe celula com `density` no DM. Agua tem `density = 0`, entao ela nunca entrou. "De graca"
	/// e justamente o que precisa de prova -- ninguem escreveu uma linha pra isso acontecer, e
	/// ninguem vai escrever uma linha no dia em que parar de acontecer.
	/// </summary>
	private static Familia NaoFazSombra() => new()
	{
		Nome = "FAMILIA 4 -- NAO FAZ SOMBRA (a agua nao cega)",
		Frase = "n tem SOMBRA nem nada disso",
		Provas = (r, p) =>
		{
			Cenario c = Montar(r);

			p.Prova("quem esta na outra margem do lago E VISTO", r.Ve(c.Vis, Cenario.Oeste, Cenario.Leste));
			p.Prova("...mas quem esta atras da PAREDE nao e (senao 'tudo se ve' passaria verde)",
					!r.Ve(c.Vis, Cenario.Leste, Cenario.DepoisDoMuro));
			p.Prova("a classe declara que agua nao cega", !ClasseDeAgua.Cega);

			if (_terra == null) { p.NaoDeu("as celulas de agua da Terra dentro do `.vis` de verdade"); return; }
			(ZoneCollision terra, ZoneCollision vis) = _terra.Abrir();
			r.MexerNoMapa(new Cenario { Col = terra, Vis = vis, LagoComoParede = terra, Vazio = terra });

			int agua = 0, aguaCega = 0, cegas = 0;
			for (int y = 0; y < terra.Height; y++)
				for (int x = 0; x < terra.Width; x++)
				{
					if (vis.BlockedCell(x, y)) cegas++;
					if (!terra.EhAgua(x, y)) continue;
					agua++;
					if (vis.BlockedCell(x, y)) aguaCega++;
				}
			p.Prova($"na Terra, {aguaCega} das {agua:N0} celulas de agua entram no `.vis`", aguaCega == 0);
			p.Prova($"...e o `.vis` nao esta vazio: {cegas:N0} celulas cegam de verdade", cegas > 0);

			if (_terra is not { TravessiaLargura: > 0 } t) { p.NaoDeu("ver o outro lado do rio de verdade"); return; }
			p.Prova($"no rio de verdade ({t.TravessiaLargura} tiles), as duas margens se veem",
					r.Ve(vis, Cenario.NoTile(t.TravessiaX - 2, t.TravessiaY),
						 Cenario.NoTile(t.TravessiaX + t.TravessiaLargura + 1, t.TravessiaY)));
		},
		Defeitos = [
			("a agua entrou no `.vis` (o `Density = true` no `DmTurfScanner`)",
			 r => r.MexerNoMapa = AguaVaiCegar),

			("ninguem pergunta ao `.vis` (a vista atravessa tudo -- so o contra-exemplo pega)",
			 r => r.Ve = (_, _, _) => true),

			("a vista passou a esconder tudo (o leque preto virou o mundo inteiro)",
			 r => r.Ve = (_, _, _) => false),
		],
	};

	// ==================================================================================
	// FAMILIA 5 -- NADAR NAO SOBE E NAO FAZ SOMBRA
	// ==================================================================================

	/// <summary>
	/// *"uma animacao PARECIDA COM A DO FLY, porem N CRIA A SOMBRA em baixo e NEM FICA MAIS ALTO"*.
	///
	/// ============================ ESTA FAMILIA SO EXISTE EM PAR ============================
	/// "Nadar nao sobe e nao faz sombra" e satisfeito perfeitamente por um jogo em que NINGUEM sobe e
	/// NINGUEM faz sombra -- ou seja, pelo voo quebrado. As duas linhas juntas (o nado nao, o voo
	/// sim) sao a unica prova de que as duas poses sao coisas diferentes na tela; separadas, cada uma
	/// e metade de uma frase.
	///
	/// A PROVA ESTRUTURAL fecha o que o resto nao alcanca. "Nadar produz altura ZERO" e uma
	/// propriedade do SERVIDOR (`GameServer.Nado.cs` simplesmente nunca escreve `Altitude`), e nao
	/// ha funcao pura pra perguntar isso -- entao a bancada le o fonte e exige que continue assim. E
	/// literal: o dia em que alguem "melhorar" o nado com um `pl.Altitude = 4f` (a tentacao e obvia,
	/// "so pra dar um flutuadinho"), a sombra volta e o item do pedido morre.
	/// =====================================================================================
	/// </summary>
	private static Familia NadarNaoSobeNemFazSombra() => new()
	{
		Nome = "FAMILIA 5 -- NADAR NAO SOBE E NAO FAZ SOMBRA (o voo faz os dois)",
		Frase = "N CRIA A SOMBRA em baixo e NEM FICA MAIS ALTO",
		Provas = (r, p) =>
		{
			float nado = r.AlturaDoNado;
			float voo = Voo.AlturaDePairar;

			p.Prova($"NADANDO nao ha sombra (altura {nado:0.##} px)", !r.TemSombra(nado));
			p.Prova("...e VOANDO ha (senao 'ninguem faz sombra' passaria verde)", r.TemSombra(voo));

			p.Prova($"NADANDO o desenho nao sobe ({nado * Voo.EscalaNaTela:0.##} px na tela)",
					nado * Voo.EscalaNaTela == 0f);
			p.Prova($"...e VOANDO sobe ({voo * Voo.EscalaNaTela:0} px)", voo * Voo.EscalaNaTela > 1f);

			p.Prova("NADANDO o corpo fica no andar do chao", Voo.Andar(nado) == 0);
			p.Prova("...e VOANDO sai dele", Voo.Andar(voo) >= 1);

			// O ANDAR NAO E DETALHE DE DESENHO: e quem alcanca quem. Nadar tem que continuar sendo
			// alcancavel por quem esta em pe na margem -- e voar rasante, nao (a assimetria do `PodeAcertar`).
			p.Prova("quem nada e alcancado por quem esta em pe no chao (mesmo andar)",
					Voo.PodeAcertar(Voo.Andar(nado), 0) && Voo.PodeAcertar(0, Voo.Andar(nado)));
			p.Prova("...e quem voa rasante NAO (e a vantagem de voar -- as duas poses diferem)",
					Voo.PodeAcertar(Voo.Andar(voo), 0) && !Voo.PodeAcertar(0, Voo.Andar(voo)));

			// ---- a prova estrutural
			string arq = _fonteDoNado;
			if (arq.Length == 0 || !File.Exists(arq)) { p.NaoDeu("o fonte do nado nao escreve `Altitude`"); return; }
			string fonte = r.LerFonte(arq);
			string[] linhas = fonte.Split('\n');
			var escritas = linhas.Where(l => !l.TrimStart().StartsWith("//"))
								 .Where(l => l.Contains("Altitude") &&
											 (l.Contains("Altitude =") || l.Contains("Altitude +=")
											  || l.Contains("Altitude -=")))
								 .Select(l => l.Trim()).ToList();
			p.Prova($"`{Path.GetFileName(arq)}` nao escreve altitude em nenhuma linha"
					+ (escritas.Count > 0 ? $" (achei: {Curto(escritas[0])})" : ""),
					escritas.Count == 0);
		},
		Defeitos = [
			("nadar virou \"voo rente ao chao\" (alguem deu 8 px de altura pro nado)",
			 r => r.AlturaDoNado = 8f),

			("a sombra parou de nascer pra todo mundo (o voo perdeu a dele tambem)",
			 r => r.TemSombra = _ => false),

			("a sombra passou a nascer sempre (o nado ganhou a mancha embaixo)",
			 r => r.TemSombra = _ => true),

			("alguem pos um `pl.Altitude = 4f` no tique do nado (\"so um flutuadinho\")",
			 r => { Func<string, string> ler = r.LerFonte;
					r.LerFonte = a => ler(a) + "\n\t\tpl.Altitude = 4f;   // flutuadinho\n"; }),
		],
	};

	// ==================================================================================
	// FAMILIA 6 -- MESMA SEMENTE, MESMO MUNDO
	// ==================================================================================

	/// <summary>
	/// A agua dos planetas gerados nasce no MESMO laco que a parede, e a busca da clareira passou a
	/// recusar agua -- ou seja, a mudanca entrou no caminho que decide ONDE SE POUSA. Mundo que se
	/// reescreve entre duas geracoes iguais e um mundo que muda debaixo de quem esta nele: o cliente
	/// e o servidor geram cada um o seu a partir da semente e nunca trocam um byte de mapa.
	///
	/// O CONTRA-EXEMPLO AQUI E O MAIS FACIL DE ESQUECER: um gerador que devolvesse SEMPRE o mesmo
	/// mundo, ignorando a semente, passa em "mesma semente = mesmo mundo" com nota maxima.
	/// </summary>
	private static Familia MesmaSementeMesmoMundo() => new()
	{
		Nome = "FAMILIA 6 -- MESMA SEMENTE, MESMO MUNDO",
		Frase = "o terreno gerado sai igual na mesma semente, depois da agua",
		Provas = (r, p) =>
		{
			BiomaDeTerreno[] biomas = [BiomaDeTerreno.Jardim, BiomaDeTerreno.Gelado];
			int divergiu = 0, iguaisEntreSementes = 0, noMar = 0, mundos = 0, comAgua = 0;
			ulong primeira = 0;

			foreach (BiomaDeTerreno bioma in biomas)
				for (int i = 0; i < 8; i++)
				{
					ParametrosDeTerreno par = Params((ulong)(0x4A6UL + (ulong)i * 104729), bioma);

					TerrenoGerado a = r.Gerar(par);
					TerrenoGerado b = r.Gerar(Params(par.Seed, bioma));   // objeto NOVO, mesma semente
					mundos++;

					if (a.Assinatura() != b.Assinatura()) divergiu++;
					else if (!Iguais(a.BytesDeAgua, b.BytesDeAgua)) divergiu++;
					else if (a.SpawnCelX != b.SpawnCelX || a.SpawnCelY != b.SpawnCelY) divergiu++;

					if (i == 0 && bioma == biomas[0]) primeira = a.Assinatura();
					else if (a.Assinatura() == primeira) iguaisEntreSementes++;

					if (a.Colisao.EhAgua(a.SpawnCelX, a.SpawnCelY)) noMar++;
					if (a.BytesDeAgua.Any(v => v != 0)) comAgua++;
				}

			p.Prova($"mesma semente = mesmo mundo, mesma agua e mesmo pouso ({mundos} mundos, {divergiu} divergencia(s))",
					divergiu == 0);
			p.Prova($"...e sementes DIFERENTES dao mundos diferentes ({iguaisEntreSementes} repetido(s))",
					iguaisEntreSementes == 0);
			p.Prova($"...e {comAgua} dos {mundos} mundos tem agua de verdade (senao nada disso mediu agua)",
					comAgua > 0);
			p.Prova($"o gerador nunca manda pousar dentro do mar ({noMar} caso(s))", noMar == 0);

			// ============================ O OUTRO CAMINHO DE POUSO, E E ELE QUE DA PRA REPROVAR ============================
			// Sao DOIS os caminhos que poem um corpo no chao, e so este segundo passa por todo mundo:
			// o berco, o pouso de nave, a invasao e o povoamento chamam `PontoLivrePerto`, e foi nele
			// que a clausula de agua entrou.
			//
			// O PRIMEIRO -- o degrau 2 da clareira do gerador (`AcharOuAbrirClareira`, `exigente == 0`) --
			// FICA SEM COBERTURA, e vale dizer por que em vez de fingir que nao: ele so decide alguma
			// coisa num mundo que nao tenha NENHUMA planicie limpa no mapa inteiro, porque o degrau 1
			// varre o planeta todo antes dele e planicie nunca e agua. Injetar a regra antiga ali
			// deixava a familia VERDE (a bancada marcou `CEGA`), e um defeito que a bancada nao alcanca
			// tem que aparecer no placar, nao sumir. Os seis biomas sorteaveis todos produzem planicie.
			// ==========================================================================================================
			p.NaoDeu("o degrau 2 da clareira do gerador (so decide em mundo sem planicie limpa nenhuma)");
			TerrenoGerado mundo = r.Gerar(Params(0x4A6, BiomaDeTerreno.Jardim));
			if (AcharAgua(mundo.Colisao) is not { } lago) { p.NaoDeu("desviar de um lago no mundo gerado"); }
			else
			{
				Vec2 saida = r.PontoLivre(mundo.Colisao, Centro(lago));
				p.Prova($"pedindo pra por um corpo dentro de um lago do mundo gerado, o funil desvia "
						+ $"(pediu {lago}, deu ({Tile(saida)}))",
						!mundo.Colisao.EhAguaEm(saida) && !mundo.Colisao.BlockedAt(saida));
			}

			if (_terra == null) { p.NaoDeu("o mesmo, no oceano de verdade da Terra"); return; }
			(ZoneCollision terra, _) = _terra.Abrir();
			if (AcharAgua(terra) is not { } oceano) { p.NaoDeu("achar agua na Terra"); return; }
			Vec2 seco = r.PontoLivre(terra, Centro(oceano));
			p.Prova($"...e no oceano de verdade da Terra tambem (pediu {oceano}, deu ({Tile(seco)}))",
					!terra.EhAguaEm(seco) && !terra.BlockedAt(seco));
		},
		Defeitos = [
			("a semente ganhou uma parcela que muda entre geracoes (o classico do relogio)",
			 r => { int n = 0;
					r.Gerar = par => GeradorDeTerreno.Gerar(++n % 2 == 0
						? Params(par.Seed + 1, par.Bioma) : par); }),

			("o gerador passou a ignorar a semente (todo planeta igual)",
			 r => r.Gerar = par => GeradorDeTerreno.Gerar(Params(7, par.Bioma))),

			("o `PontoLivrePerto` voltou a aceitar \"qualquer celula que nao seja parede\" (a regra ANTIGA)",
			 r => r.PontoLivre = PontoLivreDaRegraAntiga),
		],
	};

	/// <summary>Uma celula de agua qualquer, longe da borda -- pra pedir um corpo dentro dela.</summary>
	private static (int X, int Y)? AcharAgua(ZoneCollision m)
	{
		for (int y = 8; y < m.Height - 8; y++)
			for (int x = 8; x < m.Width - 8; x++)
				if (m.EhAgua(x, y)) return (x, y);
		return null;
	}

	private static Vec2 Centro((int X, int Y) c) => new(c.X * 32 + 16, c.Y * 32 + 16);

	/// <summary>
	/// A CELULA de um ponto, pro relato. Com divisao INTEIRA: `{x/32:0}` arredonda, e o centro de um
	/// tile (x*32+16) cai exatamente no meio -- o relato dizia "pediu (103,8), deu (103,8)" com a
	/// resposta certa sendo a celula 102. Numero de relato que mente vale menos que numero nenhum.
	/// </summary>
	private static string Tile(Vec2 v) => $"{(int)MathF.Floor(v.X / 32)}, {(int)MathF.Floor(v.Y / 32)}";

	/// <summary>
	/// O `PontoLivrePerto` COMO ELE ERA ANTES DA AGUA: anel por anel, aceita a primeira celula que
	/// nao seja parede nem beirada. Nao e uma invencao pra ficar vermelha -- e o codigo que estava
	/// escrito ali, e o comentario do proprio `ZoneCollision:426` diz que a linha da agua entrou
	/// depois e por que.
	/// </summary>
	private static Vec2 PontoLivreDaRegraAntiga(ZoneCollision m, Vec2 desejado)
	{
		int cx = (int)MathF.Floor(desejado.X / 32), cy = (int)MathF.Floor(desejado.Y / 32);
		if (Serve(cx, cy)) return desejado;
		for (int r = 1; r <= 64; r++)
			for (int dx = -r; dx <= r; dx++)
				for (int dy = -r; dy <= r; dy++)
				{
					if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
					if (Serve(cx + dx, cy + dy)) return new Vec2((cx + dx) * 32 + 16, (cy + dy) * 32 + 16);
				}
		return desejado;

		bool Serve(int x, int y) => !m.BlockedCell(x, y) && !m.NaBorda(x, y);
	}

	private static ParametrosDeTerreno Params(ulong seed, BiomaDeTerreno bioma) => new()
	{
		Seed = seed,
		Largura = 192,
		Altura = 192,
		Bioma = bioma,
		Gravidade = 5,
		Nome = "bancada-agua",
	};

	private static bool Iguais(byte[] a, byte[] b)
	{
		if (a.Length != b.Length) return false;
		for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
		return true;
	}

	// ==================================================================================
	// FAMILIA 7 -- A IA NAO TRAVA
	// ==================================================================================

	/// <summary>
	/// ============================ O QUE "NAO TRAVA" QUER DIZER AQUI ============================
	/// Nao ha caminhamento nenhum na IA deste port -- nao ha A*, nao ha contorno, nao ha desencalhe
	/// (`GameServer.Ia.PassoDaIa`). O NPC anda no rumo direto do alvo e, contra obstaculo, desliza no
	/// eixo livre e para. Entao a afirmacao verificavel nao e "ele contorna o lago": e
	///
	///     A AGUA NAO E PIOR QUE O MURO QUE JA EXISTIA.
	///
	/// Por isso toda prova daqui e comparada com o MESMO mapa tendo o lago virado parede. Se a agua
	/// travasse de um jeito que a parede nao trava (o corpo vibrando entre dois estados, o passo
	/// virando NaN, o deslize sumindo), a comparacao abre.
	///
	/// E A OUTRA METADE: o NPC que VOA tem que passar por cima. Ele passa pela mesma funcao do
	/// jogador (`ModoDeTravessiaDe`), e o defeito classico -- "a IA ficou com um `if` proprio" -- e
	/// injetado abaixo.
	///
	/// ============================ O QUE ESTA BANCADA **NAO** ALCANCA ============================
	/// `PassoDaIa` mora no assembly do Godot e nao da pra chamar daqui. O que roda aqui sao as DUAS
	/// funcoes de producao que ele chama em sequencia (`ClasseDeAgua.ModoDe` e `MoveRules.Advance`),
	/// que hoje sao o passo inteiro dele -- nao ha mais nada entre elas. No dia em que houver, esta
	/// bancada deixa de cobrir a diferenca, e por isso isto esta escrito aqui e no relatorio.
	/// ==========================================================================================
	/// </summary>
	private static Familia IaNaoTrava() => new()
	{
		Nome = "FAMILIA 7 -- A IA NAO TRAVA CONTRA AGUA",
		Frase = "o lago se comporta como o muro que ja existia; e quem voa passa por cima",
		Provas = (r, p) =>
		{
			Cenario c = Montar(r);

			// ---- O NPC A PE, DE FRENTE: para na margem, e para IGUAL a como pararia num muro.
			ModoDeTravessia aPe = ClasseDeAgua.ModoDe(false, false, false);
			Caminhada naAgua = Andar(r, c.Col, Cenario.Oeste, new Vec2(1, 0), aPe, 600);
			Caminhada noMuro = Andar(r, c.LagoComoParede, Cenario.Oeste, new Vec2(1, 0), aPe, 600);
			p.Prova($"a pe o NPC para na margem, no MESMO ponto em que pararia num muro "
					+ $"(agua x={naAgua.Fim.X:0}, muro x={noMuro.Fim.X:0})",
					Math.Abs(naAgua.Fim.X - noMuro.Fim.X) < 1f);
			p.Prova("...e ele PARA de verdade (nao fica vibrando: o ultimo quadro nao andou)",
					naAgua.UltimoPasso < 0.01f && noMuro.UltimoPasso < 0.01f);

			// ---- O DESLIZE: e ele que evita o travamento de verdade. Em diagonal contra a margem,
			// o eixo X zera e o Y continua -- e o NPC segue andando rente ao lago em vez de congelar.
			//
			// A REGUA E O MAPA VAZIO, e nao um numero escolhido a dedo: indo na diagonal o corpo ja
			// anda em Y ate encostar, e so esse pedaco chega a 240 px -- que passaria por "deslizou"
			// se a prova pedisse "mais que 100". Comparado com o mundo sem obstaculo, o que se mede e
			// se ele CONTINUOU andando depois de o eixo X travar.
			Caminhada diagonal = Andar(r, c.Col, Cenario.Oeste, new Vec2(1, 1), aPe, 600);
			Caminhada diagMuro = Andar(r, c.LagoComoParede, Cenario.Oeste, new Vec2(1, 1), aPe, 600);
			Caminhada diagLivre = Andar(r, c.Vazio, Cenario.Oeste, new Vec2(1, 1), aPe, 600);
			float andouY = diagonal.Fim.Y - Cenario.Oeste.Y, livreY = diagLivre.Fim.Y - Cenario.Oeste.Y;
			p.Prova($"na diagonal ele DESLIZA pela margem em vez de travar "
					+ $"({andouY:0} px em Y, contra {livreY:0} px sem obstaculo nenhum)",
					livreY > 1f && andouY >= livreY * 0.95f);
			p.Prova("...e desliza exatamente como desliza num muro",
					Math.Abs(diagonal.Fim.Y - diagMuro.Fim.Y) < 1f);

			// ---- O NPC QUE VOA: passa por cima.
			ModoDeTravessia voando = ClasseDeAgua.ModoDe(false, true, false);
			Caminhada aereo = Andar(r, c.Col, Cenario.Oeste, new Vec2(1, 0), voando, 600);
			p.Prova($"o NPC que VOA atravessa o lago (chegou a x={aereo.Fim.X:0})",
					aereo.Fim.X > (Cenario.LagoX1 + 1) * 32);

			// ---- E NENHUM QUADRO PODRE: nem NaN, nem pe dentro da agua a pe.
			p.Prova("nenhum quadro com posicao invalida (NaN) em nenhuma das travessias",
					!naAgua.Podre && !diagonal.Podre && !aereo.Podre);
			p.Prova("e a pe ele nunca chegou a pisar na agua", naAgua.PxNaAgua == 0 && diagonal.PxNaAgua == 0);
		},
		Defeitos = [
			("a IA ganhou um `if` proprio e manda sempre `APe` (o NPC que voa trava no lago)",
			 r => { r.Passo = (pos, d, dt, v, m, ignorado) =>
						MoveRules.Advance(pos, d, dt, v, m, out _, false, ModoDeTravessia.APe); }),

			("o deslize sumiu do `Advance` (o passo passou a ser tudo-ou-nada)",
			 r => r.Passo = (pos, d, dt, v, m, modo) =>
			 {
				 Vec2 alvo = MoveRules.Integrate(pos, d, dt, v);
				 return m == null || !MoveRules.Occupied(m, alvo, modo) ? alvo : pos;
			 }),

			("a agua virou parede no `.col` (e ai nem o NPC voador passa)",
			 r => r.MexerNoMapa = AguaViraParede),
		],
	};

	// ==================================================================================
	// O ANDAR: um corpo caminhando de verdade, quadro a quadro
	// ==================================================================================

	private readonly struct Caminhada
	{
		public required Vec2 Fim { get; init; }

		/// <summary>Pixels percorridos COM OS PES NA AGUA -- a medida honesta de "atravessou nadando".</summary>
		public required float PxNaAgua { get; init; }

		/// <summary>Deslocamento do ultimo quadro: zero = parou de verdade, nao ficou vibrando.</summary>
		public required float UltimoPasso { get; init; }

		/// <summary>Algum quadro saiu NaN/infinito.</summary>
		public required bool Podre { get; init; }
	}

	/// <summary>
	/// ANDA DE VERDADE, quadro a quadro, com a mesma cadencia do cliente (60 Hz).
	///
	/// UM QUADRO NAO PROVA NADA -- e esta e uma licao pagas na sessao passada: a 160 px/s o passo e
	/// de 2,7 px, e daqui ate o obstaculo ha um tile inteiro. Uma prova que media "o corpo andou"
	/// nasceu VERMELHA com o codigo certo. Por isso aqui se anda o percurso todo e se pergunta se o
	/// corpo PASSOU.
	/// </summary>
	private static Caminhada Andar(Regras r, ZoneCollision mapa, Vec2 de, Vec2 rumo,
								   ModoDeTravessia modo, int quadros)
	{
		Vec2 pos = de;
		float naAgua = 0f, ultimo = 0f;
		bool podre = false;

		for (int i = 0; i < quadros; i++)
		{
			Vec2 antes = pos;
			pos = r.Passo(pos, rumo, 1f / 60, 1f, mapa, modo);
			if (float.IsNaN(pos.X) || float.IsNaN(pos.Y) || float.IsInfinity(pos.X) || float.IsInfinity(pos.Y))
			{
				podre = true;
				pos = antes;
				break;
			}
			ultimo = (pos - antes).Length;
			// SO CONTA COM OS PES NA AGUA: `NaAgua` usa a MESMA caixa dos pes da colisao (ver
			// `MoveRules.NaAgua`). Somar a distancia "com o bit ligado" mediria a intencao, e nao a
			// travessia -- foi assim que a bancada visual ficou verde com o corpo andando no seco.
			if (MoveRules.NaAgua(mapa, pos)) naAgua += ultimo;
		}

		return new Caminhada { Fim = pos, PxNaAgua = naAgua, UltimoPasso = ultimo, Podre = podre };
	}

	// ==================================================================================
	// O FONTE DO SERVIDOR (prova estrutural da familia 5)
	// ==================================================================================

	private static string _fonteDoNado = "";

	/// <summary>...e o do VOO, que e onde mora o pouso (`DescerAte`). Ver a prova estrutural da familia 2.</summary>
	private static string _fonteDoVoo = "";

	private static string FonteDoNado(string raiz)
		=> _fonteDoNado.Length > 0 ? _fonteDoNado : _fonteDoNado = Procurar(raiz, "GameServer.Nado.cs");

	private static string FonteDoVoo(string raiz)
		=> _fonteDoVoo.Length > 0 ? _fonteDoVoo : _fonteDoVoo = Procurar(raiz, "GameServer.Voo.cs");

	/// <summary>Sobe do diretorio dado ate achar o repo -- a bancada roda tanto da raiz quanto do bin/.</summary>
	private static string Procurar(string raiz, string arquivo)
	{
		var dir = new DirectoryInfo(Path.GetFullPath(raiz));
		for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
		{
			string tentativa = Path.Combine(dir.FullName, "Server", arquivo);
			if (File.Exists(tentativa)) return tentativa;
		}
		return "";
	}
}
