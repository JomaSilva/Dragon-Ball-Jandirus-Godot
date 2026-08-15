using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// O CENSO DO QUE BLOQUEIA E NAO APARECE -- `censo &lt;pastaMaps&gt; &lt;pastaCode&gt; &lt;pastaDmm&gt;`.
///
/// ============================ A QUEIXA QUE A TROUXE ============================
/// *"em VARIOS MAPAS PRE-FEITOS tem VARIOS TILES INVISIVEIS COM COLISAO, onde era pra ser MESA, ou
/// uma MAQUINA etc, simplesmente N TEM SPRITE NENHUM. ai quando eu soco ele QUEBRA e faz TODOS OS
/// EFEITOS mas N TINHA NADA LA, so colisao"*.
///
/// A varredura anterior (`CidadeBench`) fechou com "nenhuma celula solida sem dono, 0 em 27 zonas"
/// -- e o dono achou mais. Entao a pergunta dela nao cobre este caso, e descobrir POR QUE e metade
/// do trabalho.
/// ==============================================================================
///
/// ============================ A PERGUNTA QUE MUDA TUDO ============================
/// A `CidadeBench` pergunta **"esta CELULA tem desenho?"**. Aqui a pergunta e
/// **"o que BLOQUEIA nesta celula tem desenho?"** -- e as duas so coincidem numa celula que tenha
/// uma coisa so.
///
/// Uma mesa densa em cima de um piso de ladrilho e o contra-exemplo, e e o caso do dono:
///
///   * o piso desenha, entao a celula TEM desenho -> a `Mudas` da bancada velha nao a ve;
///   * a mesa e densa no `.dmm`, entao a parede TEM dono -> a `Inventadas` tambem nao a ve;
///   * e a mesa, essa, nao foi desenhada por ninguem.
///
/// O jogador ve chao livre, esbarra no nada, soca, e o nada quebra com todos os efeitos.
/// =================================================================================
///
/// ============================ ONDE O CONVERSOR ABRE ESSE BURACO ============================
/// No `MapConverter.Por`, que e quem pinta uma camada e quem carimba a colisao. A regra dele e
/// "so bloqueia o que foi desenhado" -- e ela tem UMA excecao, escrita para a borda do mundo:
///
///     if (td.Icon == null) { if (td.Density &amp;&amp; !costuras.Contains((x,y))) muros.Add((x,y)); return false; }
///
/// O comentario ao lado explica a excecao com `/turf/Other/Blank`, que e denso, sem icone e
/// invisivel DE PROPOSITO -- o limite do retangulo. So que a condicao nao diz `Blank`: ela diz
/// **"denso e sem icone"**. Qualquer typepath cujo `icon` o `DmTurfScanner` nao resolva cai nela e
/// vira parede sem desenho -- e o relatorio de "PAREDE INVISIVEL" do proprio conversor nao o pega,
/// porque ele compara com `desenhadas`, e o PISO ja pos a celula la.
/// ==========================================================================================
///
/// ============================ O QUE A PRIMEIRA MEDICAO DEU, E ELA CORRIGE A HIPOTESE ============================
/// A mesa muda sobre o piso pintado e uma hipotese BOA e, no `Assets/Maps` de hoje, FALSA: o censo
/// deu **0 celulas** nessa forma. Todo bloqueador de todo mapa ou desenha, ou e `/turf/Other/Blank`.
/// A excecao do `td.Icon == null` existe e e larga, mas hoje **um typepath so** passa por ela.
///
/// O que ela achou de verdade foi outra coisa, e nao e um buraco de ARTE -- e um buraco de REGRA:
///
///   * `/turf/Other/Blank` nao tem `icon` NENHUM no DM. Ele e invisivel no BYOND tambem, entao nao
///     ha arte perdida a recuperar: **1.702 dessas celulas encostam em chao pisavel** (Lookout e
///     Arconia), e 760 delas tem chao dos DOIS lados -- a forma exata da costura de Hera;
///   * e no DM ele declara **`destroyable = 0`** (`Turfs.dm:72`). O port nao extrai esse campo:
///     ele o aproxima pelo anel de 2 celulas do `ZoneCollision.NaBorda`. Resultado medido:
///     **1.690 das 1.702 podem ser socadas e DERRUBADAS**, com baque, poeira e a celula virando
///     terra batida -- literalmente "soco, quebra, faz todos os efeitos, e nao tinha nada la".
///
/// Por isso este arquivo conta as duas coisas separadas: o que nao desenha (hoje, 0 fora do vazio)
/// e o VAZIO DELIBERADO, que nao e defeito de desenho e e defeito de destruicao.
/// =============================================================================================================
///
/// O QUE ELE CONTA, por origem (a divisao que o dono pediu):
///   1. TILE do `.pedacos` (a celula do `.col`) -- o caso acima;
///   2. MAQUINA do `.objetos` -- catalogo/arte/`.tres`/`icon_state`/import;
///   3. PORTA do `.portas` -- o `.tres` e os quatro estados;
///   4. OBRA ERGUIDA -- nao mora em mapa nenhum (vive no `mundo.json`), entao ela e conferida
///      pelo CATALOGO: toda construcao densa tem que ter arte que carrega.
///
/// ============================ ELA DEIXOU DE SER SO UM CENSO ============================
/// Na fase 0 este arquivo so CONTAVA, e o resultado disso foi: tudo medido, tudo impresso, e a
/// `CidadeBench` seguiu 56 OK / 0 FALHAS com 1.702 paredes invisiveis alcancaveis no disco. Numero
/// impresso nao e asserção -- ele so vira guarda quando reprova. Agora ela **devolve 1** em tres
/// casos, e cada um e um conserto diferente:
///
///   1. celula que bloqueia e cujo bloqueador nao desenha -- **inclusive MAQUINA sem arte**, que era
///      a isencao exata da varredura antiga ("quem responde por esta parede?" aceitava a maquina
///      como dono, sem nunca perguntar se ela aparecia);
///   2. construcao DENSA do catalogo sem arte que carregue;
///   3. celula do VAZIO DELIBERADO que ainda **cede a um soco** -- ver `vazioQuebravel`. O perdao ao
///      invisivel-de-origem passou a ser CONDICIONADO ao `.duro`, porque no original as duas coisas
///      andam juntas: o Blank e invisivel E indestrutivel (`Turfs.dm:69-77`).
/// =======================================================================================
///
/// ELA NAO CONSERTA NADA -- quem conserta e o comando `duro` (o plano do indestrutivel) e o
/// pipeline de arte. Aqui ela conta, nomeia, diz o porque de cada tipo e REPROVA.
/// </summary>
public static class CensoMudoBench
{
	/// <summary>Por que uma coisa que bloqueia nao aparece. Cada motivo e um conserto diferente.</summary>
	private enum Motivo
	{
		Desenha,               // tudo certo
		SemIconeNoDM,          // o typepath nao tem `icon` na arvore do DM -- a excecao da borda
		AtlasForaDoTileset,    // tem `icon`, e o `.dmi` nunca virou atlas no tileset
		EstadoForaDoAtlas,     // o atlas existe e o `icon_state` nao -- o conversor cai no quadro 0
		NaoPintado,            // resolveria, e a celula do `.pedacos` nao aponta pra ele
		ForaDoCatalogo,        // maquina cujo `create_type` nao esta no `construcoes.json`
		CatalogoSemArte,       // esta no catalogo com `arte` vazia
		TresAusente,           // a `arte` aponta pra um `.tres` que nao existe
		TresSemEstado,         // o `.tres` existe e nao tem a animacao do `icon_state`
		PngSemImport,          // o PNG existe e nunca foi importado pelo Godot
		QuadroErrado,          // DESENHA -- mas o quadro 0 de outra coisa, porque o estado nao existe
	}

	private sealed record Achado(string Zona, int X, int Y, string Tipo, Motivo Porque, string Detalhe);

	/// <summary>
	/// <paramref name="semDuro"/> manda IGNORAR o `.duro` -- e o CONTROLE NEGATIVO desta bancada.
	///
	/// ============================ POR QUE ELE E UMA CHAVE E NAO UM EXPERIMENTO A MAO ============================
	/// A guarda do `vazioQuebravel` so vale alguma coisa se alguem ja tiver VISTO ela reprovar. A
	/// primeira conferencia disso foi feita apagando um `.duro` do disco e restaurando depois -- o que
	/// prova a mesma coisa e nao fica: no mes que vem ninguem sabe se a linha ainda pega, e "esta
	/// verde" e indistinguivel de "nao esta olhando".
	///
	/// Com a chave, o controle negativo e um comando e nao um ritual, e ele e o IRMAO EXATO do
	/// `--semduro` do servidor (`GameServer.CarregarZonas`): as duas pontas sabem simular o mundo de
	/// antes do conserto sem tocar em arquivo nenhum.
	/// ==========================================================================================================
	/// </summary>
	public static int Run(string pastaMaps, string pastaCode, string pastaDmm, bool semDuro = false)
	{
		Console.WriteLine("=== CENSO: O QUE BLOQUEIA E NAO APARECE ===\n");
		if (semDuro)
			Console.WriteLine("!! `--semduro`: o plano do indestrutivel NAO sera lido. Este e o CONTROLE\n"
							+ "!! NEGATIVO -- o censo TEM que reprovar aqui. Se ele passar, a guarda morreu.\n");

		string manifesto = Path.Combine(pastaMaps, "manifest.json");
		if (!File.Exists(manifesto)) { Console.WriteLine($"sem {manifesto}"); return 1; }
		ZoneCatalog cat = ZoneCatalog.Parse(File.ReadAllText(manifesto));

		string dataDir = Path.Combine(Path.GetDirectoryName(pastaMaps.TrimEnd('/', '\\'))!, "Data");
		CatalogoDeObras obras = CatalogoDeObras.Parse(File.ReadAllText(Path.Combine(dataDir, "construcoes.json")));
		CatalogoDeTiles tiles = CatalogoDeTiles.Parse(File.ReadAllText(Path.Combine(dataDir, "tiles.json")));

		Console.WriteLine("lendo a arvore de tipos do DM...");
		Dictionary<string, TurfDef> turfs = DmTurfScanner.Scan(Path.GetFullPath(pastaCode));

		Console.WriteLine("lendo os `.dmm` de origem...");
		var porZ = new Dictionary<int, (DmmMap.Result D, DmmLevel N)>();
		int off = 0;
		foreach (string arq in MapConverter.OrdemDoDme(pastaDmm))
		{
			DmmMap.Result d = DmmMap.Read(arq);
			foreach (DmmLevel n in d.Levels) porZ[n.Z + off] = (d, n);
			off += d.Levels.Count;
		}
		Console.WriteLine($"typepaths: {turfs.Count} | construcoes: {obras.Total} | atlas: {tiles.Total} "
						  + $"| andares: {porZ.Count}\n");

		string raizProjeto = Path.GetDirectoryName(Path.GetDirectoryName(pastaMaps.TrimEnd('/', '\\'))!)!;
		var achados = new List<Achado>();
		var semDono = new List<Achado>();   // bloqueia e o `.dmm` nao explica quem
		var disfarcados = new List<Achado>();   // bloqueia e desenha o QUADRO ERRADO (ver Motivo.QuadroErrado)
		var bordas = new List<Achado>();        // o vazio deliberado do mapeador (Blank e barreira)
		var vazioQuebravel = new List<Achado>();// ...e o que dele CEDE a um soco, que e o defeito

		/// ============================ A LISTA QUE O DONO PRECISA DECIDIR ============================
		// O vazio invisivel e HERANCA: o BYOND tambem nao desenhava nada ali, e la ele bloqueia
		// igual. Consertar a DESTRUICAO (o `.duro`) fecha metade da queixa; a outra metade -- "tem
		// tile invisivel COM COLISAO" -- e uma decisao de DESIGN, e nao um bug com resposta certa.
		//
		// Entao aqui ele nao e consertado, e MEDIDO na forma em que a decisao e possivel:
		//   ENCOSTA  o jogador consegue chegar nela a pe (tem chao livre em algum dos 4 lados);
		//   CORTA    ela tem chao livre nos DOIS lados opostos -- ou seja, e uma DIVISORIA no meio
		//            de area andavel, e nao a casca de um bloco macico. E a forma que o jogador
		//            sente como "parede invisivel", e o unico grupo que vale discutir abrir.
		// As colunas/linhas mais carregadas saem junto porque a geometria e a prova: uma coluna
		// unica com 266 celulas e uma emenda de mapeador, e nao ruido.
		// ============================================================================================
		var vazioEncosta = new Dictionary<string, int>(StringComparer.Ordinal);
		var vazioCorta = new Dictionary<string, int>(StringComparer.Ordinal);
		var vazioColunas = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);
		var vazioLinhas = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);

		Console.WriteLine($"{"z",4}  {"zona",-24}{"bloqueia",9}{"invisivel",10}{"borda/vazio",12}{"maquina",8}{"porta",7}");
		Console.WriteLine(new string('-', 78));

		// ============================ E `Entradas`, E NAO `Todas` -- ISSO ERA UM BURACO ============================
		// O `Todas` guarda uma zona por NOME (a de menor z), que e o certo pro JOGO: `ZoneKey` e por
		// nome e so um "Outside" e alcancavel. Numa AUDITORIA DE DISCO isso apagava treze andares: o
		// manifesto tem 40 blocos, treze deles se chamam Outside, e este censo imprimia 27 linhas
		// enquanto o pedido dizia "os 40 mapas". Cada um desses treze tem `.col` e `.duro` proprios.
		//
		// A licao e a mesma da isencao da maquina, um degrau acima: nao adianta matar a isencao do
		// JULGAMENTO se a LISTA por onde ele passa ja chega curta.
		// =========================================================================================================
		int zonasLidas = 0, zonasSemCol = 0;
		foreach (ZoneEntry e in cat.Entradas)
		{
			// O ROTULO LEVA O `z` porque treze andares se chamam "Outside": sem ele o relatorio
			// juntaria os treze numa linha so e "corta 29 celulas em Outside" nao diria em QUAL.
			string rot = $"{e.Zona} (z{e.Z})";

			string arqCol = Local(pastaMaps, e.Colisao);
			if (arqCol.Length == 0 || !File.Exists(arqCol)
				|| ZoneCollision.Load(File.ReadAllBytes(arqCol)) is not { } col
				|| !porZ.TryGetValue(e.Z, out (DmmMap.Result D, DmmLevel N) lv))
			{
				// SAI NOMEADO, e nao calado. Um `continue` mudo aqui e como o censo perdia treze
				// andares sem que a linha de total mudasse de cara.
				zonasSemCol++;
				Console.WriteLine($"{e.Z,4}  {e.Zona,-24}   (sem `.col` ou sem `.dmm` de origem -- nao auditada)");
				continue;
			}
			zonasLidas++;

			// O PLANO DO QUE NAO SE QUEBRA -- e ele e o que TORNA LEGITIMO o vazio invisivel.
			// Ver a nota do `vazioQuebravel` mais abaixo: sem o `.duro`, "o BYOND tambem nao
			// desenhava nada ali" deixa de ser uma justificativa e vira uma desculpa.
			string arqDuro = Local(pastaMaps, e.CaminhoDoDuro);
			if (!semDuro && arqDuro.Length > 0 && File.Exists(arqDuro))
				col.CarregarDuro(File.ReadAllBytes(arqDuro));

			// ============================ E O PLANO DA AGUA, QUE MUDA A CONTA DE "ALCANCAVEL" ============================
			// A lista de decisao abaixo (ENCOSTA/CORTA) chamava de "chao livre" tudo que nao fosse parede
			// -- e agua nao e parede. A bancada da foto tropecou nisso: em Arconia ela escolheu uma costura
			// no meio do LAGO, e a caminhada rendeu 0 px com as duas copias da colisao dizendo que o
			// caminho estava aberto. O corpo tambem para na agua (`ZoneCollision.Bloqueia`).
			//
			// Sem esta linha o relatorio dizia "29 celulas CORTAM area andavel em Arconia" quando a maior
			// parte delas esta dentro d'agua e ninguem chega la a pe -- ou seja, uma lista de decisao com
			// itens que nao existem. Ver `Andavel`.
			// ============================================================================================================
			string arqAgua = Local(pastaMaps, e.CaminhoDaAgua);
			if (arqAgua.Length > 0 && File.Exists(arqAgua)) col.CarregarAgua(File.ReadAllBytes(arqAgua));

			// os desenhos publicados desta zona, por celula: (fonte, ax, ay) de cada camada
			var pintado = new Dictionary<long, List<(int F, int X, int Y)>>();
			string arqPed = Local(pastaMaps, e.Pedacos);
			if (File.Exists(arqPed) && PedacosDoMapa.Ler(File.ReadAllBytes(arqPed)) is { } ped)
				for (int c = 0; c < ped.Camadas.Length; c++)
					for (int cy = ped.Cy0; cy < ped.Cy1; cy++)
						for (int cx = ped.Cx0; cx < ped.Cx1; cx++)
						{
							if (!ped.Achar(cx, cy, c, out int ini, out int q)) continue;
							for (int i = 0; i < q; i++)
							{
								CelulaDePedaco cel = ped.Celula(ini, i);
								long k = (long)cel.Y * col.Width + cel.X;
								(pintado.TryGetValue(k, out List<(int, int, int)>? l) ? l : pintado[k] = [])
									.Add((cel.Fonte, cel.Ax, cel.Ay));
							}
						}

			var maquinas = new Dictionary<long, string>();
			string arqObj = Local(pastaMaps, e.Objetos);
			if (arqObj.Length > 0 && File.Exists(arqObj))
				foreach (ObjetoDoMapa o in ObjetosDoMapa.Parse(File.ReadAllText(arqObj)))
					maquinas[(long)o.Y * col.Width + o.X] = o.Id;

			var portas = new Dictionary<long, string>();
			string arqPor = Local(pastaMaps, e.Portas);
			if (arqPor.Length > 0 && File.Exists(arqPor))
				foreach (PortaDoMapa p in PortasDaZona.Parse(File.ReadAllText(arqPor)))
					portas[(long)p.Y * col.Width + p.X] = p.Arte;

			int bloqueia = 0, invisivel = 0, borda = 0, deMaquina = 0, dePorta = 0;

			for (int y = 0; y < col.Height; y++)
				for (int x = 0; x < col.Width; x++)
				{
					if (!col.BlockedCell(x, y)) continue;
					bloqueia++;
					long k = (long)y * col.Width + x;

					// QUEM BLOQUEIA AQUI, segundo o `.dmm` -- a mesma regra do `MapConverter.Por`
					List<string> travas = Travas(lv, x, y, turfs);
					if (travas.Count == 0)
					{
						// bloqueia e o `.dmm` nao tem nada denso: ou e a planta da cidade de Vegeta,
						// ou e parede que o port inventou. A `CidadeBench` ja cobre isto.
						semDono.Add(new Achado(rot, x, y, "(sem dono no .dmm)", Motivo.SemIconeNoDM, ""));
						continue;
					}

					// BASTA UM APARECER. Se qualquer coisa densa da celula desenha, o jogador ve
					// no que esbarrou -- e nao ha queixa.
					Achado? pior = null;
					bool alguemDesenha = false;
					foreach (string bp in travas)
					{
						(Motivo m, string det) = Julgar(bp, x, y, lv, turfs, tiles, obras,
														pintado.GetValueOrDefault(k), maquinas.GetValueOrDefault(k),
														portas.GetValueOrDefault(k), raizProjeto);
						// QUADRO ERRADO CONTA COMO DESENHO -- e e por isso que ele tem lista propria.
						// A celula mostra alguma coisa, entao ela nao e "parede invisivel" no sentido
						// estrito; so que o que ela mostra e o quadro 0 de outro estado, e quando esse
						// quadro por acaso e o chao, o jogador ve exatamente o que o dono descreveu.
						if (m == Motivo.QuadroErrado)
						{
							alguemDesenha = true;
							disfarcados.Add(new Achado(rot, x, y, bp, m, det));
							break;
						}
						if (m == Motivo.Desenha) { alguemDesenha = true; break; }
						pior ??= new Achado(rot, x, y, bp, m, det);
					}
					if (alguemDesenha) continue;

					invisivel++;
					if (pior!.Porque == Motivo.SemIconeNoDM && EhVazio(pior.Tipo))
					{
						borda++; bordas.Add(pior);

						// ============================ O VAZIO SO E PERDOADO SE ELE NAO CAI ============================
						// Esta e a linha que faltava na varredura antiga, e ela e a licao inteira: a
						// regra "a fonte nao desenha nada ali -> o BYOND tambem nao desenhava -> HERANCA,
						// e nao regressao" estava CERTA sobre o desenho e CEGA sobre a destruicao. Ela
						// nunca perguntou se aquela parede herdada podia ser socada -- e podia: 1.690
						// celulas caiam com poeira, som e terra batida, que e a queixa do dono na letra.
						//
						// No original o vazio e invisivel E indestrutivel ao mesmo tempo (`destroyable = 0`,
						// `Turfs.dm:72`), e as duas coisas andam juntas de proposito: se so a primeira
						// valer, o jogador soca o nada e o nada quebra. Entao o perdao ao invisivel fica
						// CONDICIONADO ao segundo bit -- e uma celula do vazio que nao esta no `.duro` e
						// reprova, com nome e coordenada.
						// =============================================================================================
						if (!col.Indestrutivel(x, y))
							vazioQuebravel.Add(new Achado(rot, x, y, pior.Tipo, pior.Porque,
														  "invisivel E destrutivel: falta no `.duro`"));

						// ...e a MEDICAO pra decisao do dono (ver a nota das quatro variaveis).
						// A beirada do mapa fica de fora: ela nao e divisoria de coisa nenhuma, e
						// o `NaBorda` ja a torna intocavel.
						if (!col.NaBorda(x, y))
						{
							// `Andavel` e nao `!BlockedCell`: agua nao e parede e tambem nao se pisa.
							bool le = Andavel(col, x - 1, y), ld = Andavel(col, x + 1, y);
							bool lc = Andavel(col, x, y - 1), lb = Andavel(col, x, y + 1);
							if (le || ld || lc || lb)
								vazioEncosta[rot] = vazioEncosta.GetValueOrDefault(rot) + 1;
							if ((le && ld) || (lc && lb))
							{
								vazioCorta[rot] = vazioCorta.GetValueOrDefault(rot) + 1;
								Dictionary<int, int> cs = vazioColunas.TryGetValue(rot, out var c1)
									? c1 : vazioColunas[rot] = [];
								Dictionary<int, int> ls = vazioLinhas.TryGetValue(rot, out var l1)
									? l1 : vazioLinhas[rot] = [];
								cs[x] = cs.GetValueOrDefault(x) + 1;
								ls[y] = ls.GetValueOrDefault(y) + 1;
							}
						}
						continue;
					}
					if (maquinas.ContainsKey(k)) deMaquina++;
					else if (portas.ContainsKey(k)) dePorta++;
					achados.Add(pior);
				}

			Console.WriteLine($"{e.Z,4}  {e.Zona,-24}{bloqueia,9}{invisivel,10}{borda,12}{deMaquina,8}{dePorta,7}");
		}

		// =====================================================================
		// O RELATORIO
		// =====================================================================
		// A COBERTURA VEM PRIMEIRO, e nao no rodape: "zero defeitos" so quer dizer alguma coisa
		// depois de "e eu abri TODOS os andares". Enquanto esta linha nao existia, o censo fechava
		// verde tendo pulado treze mapas em silencio.
		Console.WriteLine($"\n--- COBERTURA: {zonasLidas} de {cat.Entradas.Count} andar(es) do manifesto "
						  + $"auditado(s); {zonasSemCol} sem `.col`/`.dmm` ---");

		Console.WriteLine($"\n--- TOTAL: {achados.Count} celula(s) que BLOQUEIAM e cujo bloqueador nao desenha "
						  + $"(fora a borda do mundo) ---\n");

		Console.WriteLine("=== POR TIPO (o que era pra estar la) ===");
		foreach (var g in achados.GroupBy(a => (a.Tipo, a.Porque, a.Detalhe)).OrderByDescending(g => g.Count()))
		{
			Achado a0 = g.First();
			string onde = string.Join(" ", g.Take(4).Select(a => $"{a.Zona}({a.X},{a.Y})"));
			Console.WriteLine($"{g.Count(),8}  {a0.Tipo,-42} {a0.Porque,-20} {a0.Detalhe}");
			Console.WriteLine($"          zonas: {string.Join(", ", g.Select(a => a.Zona).Distinct())}");
			Console.WriteLine($"          ex.: {onde}");
		}

		Console.WriteLine("\n=== POR MOTIVO (cada um e um conserto diferente) ===");
		foreach (var g in achados.GroupBy(a => a.Porque).OrderByDescending(g => g.Count()))
			Console.WriteLine($"{g.Count(),8}  {g.Key,-22} em {g.Select(a => a.Zona).Distinct().Count()} zona(s), "
							  + $"{g.Select(a => a.Tipo).Distinct().Count()} typepath(s)");

		Console.WriteLine("\n=== POR ZONA ===");
		foreach (var g in achados.GroupBy(a => a.Zona).OrderByDescending(g => g.Count()))
			Console.WriteLine($"{g.Count(),8}  {g.Key,-24} {string.Join(", ", g.Select(a => a.Tipo).Distinct().Take(6))}");

		// =====================================================================
		// O DISFARCE: bloqueia, DESENHA, e o que desenha nao e o que devia ser
		// =====================================================================
		Console.WriteLine($"\n=== QUADRO ERRADO: {disfarcados.Count} celula(s) que bloqueiam e pintam o quadro 0 "
						  + "de outro estado ===");
		Console.WriteLine("    (o `icon_state` do typepath nao existe no atlas; o conversor cai no quadro 0 em");
		Console.WriteLine("     silencio. Quando esse quadro e chao/grama, o resultado na tela E a queixa do dono.)");
		foreach (var g in disfarcados.GroupBy(a => (a.Tipo, a.Detalhe)).OrderByDescending(g => g.Count()))
			Console.WriteLine($"{g.Count(),8}  {g.First().Tipo,-42} {g.First().Detalhe}\n"
							  + $"          zonas: {string.Join(", ", g.Select(a => a.Zona).Distinct())}"
							  + $"   ex.: {string.Join(" ", g.Take(4).Select(a => $"{a.Zona}({a.X},{a.Y})"))}");
		if (disfarcados.Count == 0) Console.WriteLine("    (nenhuma)");

		Console.WriteLine($"\n=== O VAZIO DELIBERADO: {bordas.Count} celula(s), por typepath ===");
		foreach (var g in bordas.GroupBy(a => a.Tipo).OrderByDescending(g => g.Count()))
			Console.WriteLine($"{g.Count(),9}  {g.Key,-34} {string.Join(", ", g.Select(a => a.Zona).Distinct().Take(8))}");

		Console.WriteLine($"\n=== ...E QUANTO DELE O JOGADOR ENCOSTA: {vazioEncosta.Values.Sum()} celula(s), "
						  + $"das quais {vazioCorta.Values.Sum()} CORTAM area andavel ===");
		Console.WriteLine("    (nao e defeito de arte -- e HERANCA: no BYOND elas bloqueiam igual e tambem");
		Console.WriteLine("     nao desenham nada. E a LISTA DE DECISAO do dono: abrir uma divisoria dessas");
		Console.WriteLine("     muda a geometria do mapa, entao ninguem a abre sozinho.)");
		foreach (var g in vazioEncosta.OrderByDescending(kv => vazioCorta.GetValueOrDefault(kv.Key)))
		{
			int corta = vazioCorta.GetValueOrDefault(g.Key);
			string cols = vazioColunas.TryGetValue(g.Key, out var cc)
				? string.Join(" ", cc.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"x={kv.Key}({kv.Value})"))
				: "";
			string lins = vazioLinhas.TryGetValue(g.Key, out var ll)
				? string.Join(" ", ll.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"y={kv.Key}({kv.Value})"))
				: "";
			Console.WriteLine($"{g.Value,9}  {g.Key,-24} corta: {corta,6}   {cols} {lins}");
		}
		if (vazioEncosta.Count == 0) Console.WriteLine("    (nenhuma alcancavel)");

		Console.WriteLine($"\n=== ...E QUANTO DELE AINDA CEDE A UM SOCO: {vazioQuebravel.Count} celula(s) ===");
		Console.WriteLine("    (invisivel POR HERANCA e destrutivel POR REGRESSAO -- e a queixa do dono:");
		Console.WriteLine("     \"eu soco, ele QUEBRA e faz TODOS OS EFEITOS, mas nao tinha nada la\".");
		Console.WriteLine("     Conserto: dotnet run --project Tools/AssetPipeline -- duro <BYOND>/Maps <BYOND>/Code Assets/Maps)");
		foreach (var g in vazioQuebravel.GroupBy(a => (a.Zona, a.Tipo)).OrderByDescending(g => g.Count()))
			Console.WriteLine($"{g.Count(),9}  {g.Key.Zona,-24} {g.Key.Tipo,-34} "
							  + $"ex.: {string.Join(" ", g.Take(4).Select(a => $"({a.X},{a.Y})"))}");
		if (vazioQuebravel.Count == 0) Console.WriteLine("    (nenhuma -- todo o vazio esta marcado no `.duro`)");

		if (semDono.Count > 0)
			Console.WriteLine($"\n(mais {semDono.Count} celula(s) bloqueiam sem nada denso no `.dmm` -- planta da "
							  + "cidade de Vegeta e afins; ver a CidadeBench)");

		// =====================================================================
		// A CONSTRUCAO ERGUIDA (a quarta origem: ela nao mora em mapa nenhum)
		// =====================================================================
		Console.WriteLine("\n=== OBRA ERGUIDA: toda construcao DENSA do catalogo tem arte que carrega? ===");
		int ruins = 0;
		foreach (Construcao c in obras.Todas.Where(c => c.Densa).OrderBy(c => c.Id))
		{
			(Motivo m, string det) = JulgarArte(c, raizProjeto);
			if (m == Motivo.Desenha) continue;
			ruins++;
			Console.WriteLine($"   {c.Id,-24} {m,-18} {det}");
		}
		if (ruins == 0) Console.WriteLine("   (nenhuma)");

		// =====================================================================
		// O VEREDITO -- e ele existe porque um censo que so CONTA nao guarda nada
		// =====================================================================
		// ============================ POR QUE ESTE ARQUIVO PASSOU A REPROVAR ============================
		// A fase 0 mediu e imprimiu tudo o que esta acima, e a `CidadeBench` continuou 56 OK / 0
		// FALHAS com 1.702 paredes invisiveis alcancaveis no disco. Numero impresso nao e asserção:
		// ele so vira guarda quando alguem tem que OLHAR pra ele, e ninguem olha um relatorio verde.
		//
		// A ISENCAO QUE MORRE AQUI e a da maquina. A varredura antiga aceitava quatro donos pra uma
		// parede (fonte, porta, maquina, planta da cidade) e uma MAQUINA SEM ARTE e um dono aceito --
		// era o unico caso que o dono conseguia ver e que nada reprovava. Aqui a maquina e julgada
		// pela ARTE, como qualquer tile, e por isso ela entra na conta do `achados`.
		//
		// O VAZIO DELIBERADO (`/turf/Other/Blank`) **nao** entra, e nao e complacencia: ele nao tem
		// `icon` no DM, e invisivel no BYOND tambem, e nao ha arte a recuperar. O defeito dele era
		// outro -- ele CAIA no soco --, e quem responde por isso agora e o `.duro`
		// (`Duros.cs` + `ZoneCollision.Indestrutivel`), medido pelo comando `duro` do pipeline.
		// ==============================================================================================
		int reprovas = achados.Count + ruins + vazioQuebravel.Count;
		Console.WriteLine(reprovas == 0
			? "\n=== VEREDITO: nada bloqueia sem aparecer, e o vazio que e invisivel de origem NAO CEDE "
			  + "a soco nenhum. ==="
			: $"\n=== VEREDITO: REPROVADO -- {achados.Count} celula(s) mudas, {ruins} construcao(oes) "
			  + $"densa(s) sem arte e {vazioQuebravel.Count} celula(s) de vazio que ainda quebram. ===");
		return reprovas == 0 ? 0 : 1;
	}

	// =====================================================================
	/// <summary>
	/// O QUE BLOQUEIA NESTA CELULA, segundo o `.dmm` -- copia da regra do `MapConverter.Por`.
	///
	/// Reescrita aqui de proposito, e nao importada: se as duas pontas chamassem a mesma funcao, um
	/// erro nela deixaria as duas de acordo. Ver a mesma nota na `CidadeBench.Fantasmas`.
	/// </summary>
	private static List<string> Travas(in (DmmMap.Result D, DmmLevel N) lv, int x, int y,
									   Dictionary<string, TurfDef> turfs)
	{
		var saida = new List<string>();
		if (x >= lv.N.Width || y >= lv.N.Height) return saida;
		string? k = lv.N.Cells[x, y];
		if (k == null || !lv.D.Keys.TryGetValue(k, out string[]? tipos)) return saida;

		foreach (string tp in tipos)
		{
			string bp = DmmMap.BasePath(tp);
			if (Passagens.Eh(bp)) continue;      // densa no DM, atravessavel aqui (o Enter() teleporta)
			bool barreira = bp.StartsWith("/obj/barrier/", StringComparison.Ordinal)
							&& !bp.Contains("kaio_gate", StringComparison.OrdinalIgnoreCase);
			if (barreira) { saida.Add(bp); continue; }
			if (turfs.TryGetValue(bp, out TurfDef? td) && td.Density) saida.Add(bp);
		}
		return saida;
	}

	/// <summary>
	/// DA PRA UM CORPO PISAR AQUI? -- parede E agua, a MESMA pergunta que o movimento faz.
	///
	/// Existe porque a versao anterior perguntava so `!BlockedCell`, e agua nao e parede. O resultado
	/// era uma LISTA DE DECISAO inflada: "29 celulas cortam area andavel em Arconia" quando a maior
	/// parte esta dentro do lago e ninguem chega nelas a pe. A bancada da foto foi quem descobriu --
	/// ela escolheu uma dessas e a caminhada rendeu 0 px com toda a colisao dizendo "aberto".
	/// </summary>
	private static bool Andavel(ZoneCollision col, int cx, int cy) =>
		!col.BlockedCell(cx, cy) && !col.EhAgua(cx, cy);

	/// <summary>O vazio deliberado do mapeador: denso, sem icone e invisivel TAMBEM no BYOND.</summary>
	private static bool EhVazio(string bp) =>
		bp.Contains("Blank", StringComparison.OrdinalIgnoreCase)
		|| bp.StartsWith("/obj/barrier/", StringComparison.Ordinal);

	/// <summary>ESTE typepath, nesta celula, aparece na tela?</summary>
	private static (Motivo, string) Julgar(string bp, int x, int y, in (DmmMap.Result D, DmmLevel N) lv,
										   Dictionary<string, TurfDef> turfs, CatalogoDeTiles tiles,
										   CatalogoDeObras obras, List<(int F, int X, int Y)>? pintado,
										   string? maquinaAqui, string? portaAqui, string raiz)
	{
		// 1. MAQUINA: sai do tilemap e vira node (`.objetos`). Quem desenha e o catalogo.
		Construcao? c = obras.PorTypepath(bp);
		if (c != null)
		{
			if (maquinaAqui == null) return (Motivo.ForaDoCatalogo, "o catalogo a conhece e o `.objetos` nao a tem");
			return JulgarArte(c, raiz);
		}

		// 2. PORTA: idem, pelo `.portas`.
		if (portaAqui != null) return JulgarTres(portaAqui, "closed", raiz);

		// 3. TILE: o caminho comum.
		if (!turfs.TryGetValue(bp, out TurfDef? td) || td.Icon == null)
			return (Motivo.SemIconeNoDM, td?.Icon == null ? "sem `icon` na arvore do DM" : "");

		string atlas = Path.GetFileNameWithoutExtension(td.Icon);
		AtlasDeTiles? a = tiles.Atlas(atlas);
		if (a == null) return (Motivo.AtlasForaDoTileset, $"`{td.Icon}` nao virou atlas");

		string estado = td.IconState ?? "";

		// A PROVA E POR TYPEPATH, E NAO POR CELULA. "A celula tem alguma tinta" e exatamente a
		// leitura que deixou a queixa do dono passar: o piso pinta, a mesa nao, e a celula parece
		// desenhada. O que vale e a celula apontar pra ESTA folha.
		if (tiles.Achar(atlas, estado) is { } trio)
		{
			if (pintado != null && pintado.Contains((trio.Fonte, trio.X, trio.Y))) return (Motivo.Desenha, "");

			// TILE HD: o `icon_state` e montado por coordenada em runtime (`autofill`), entao o
			// quadro certo varia de celula pra celula. Aqui basta a folha bater.
			if (td.IsHD && pintado != null && pintado.Any(p => p.F == trio.Fonte)) return (Motivo.Desenha, "");

			return (Motivo.NaoPintado, $"`{atlas}`:\"{estado}\" resolve e a celula nao aponta pra ele");
		}

		// ESTADO QUE O ATLAS NAO TEM: o conversor cai no quadro 0 DA MESMA FOLHA (`Coord`). Se a
		// celula aponta pra ela, ha desenho -- errado, mas ha. Ver o relatorio "cairam no quadro 0".
		if (pintado != null && pintado.Any(p => p.F == a.Fonte))
			return (Motivo.QuadroErrado, $"`{atlas}` nao tem \"{estado}\" -- pintou o quadro 0");
		return (Motivo.EstadoForaDoAtlas, $"`{atlas}` nao tem o estado \"{estado}\"");
	}

	/// <summary>A arte de uma construcao do catalogo, ate o import do PNG.</summary>
	private static (Motivo, string) JulgarArte(Construcao c, string raiz)
	{
		if (c.Arte.Length == 0) return (Motivo.CatalogoSemArte, "`arte` vazia no construcoes.json");
		return JulgarTres(c.Arte, c.Estado.Length > 0 ? c.Estado : "default", raiz);
	}

	private static (Motivo, string) JulgarTres(string res, string estado, string raiz)
	{
		string arq = Path.Combine(raiz, res.Replace("res://", "").Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(arq)) return (Motivo.TresAusente, res);

		string txt = File.ReadAllText(arq);
		var anims = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (System.Text.RegularExpressions.Match m in
				 System.Text.RegularExpressions.Regex.Matches(txt, "\"name\": &\"([^\"]*)\""))
			anims.Add(m.Groups[1].Value);
		if (!anims.Contains(estado) && anims.Count > 0)
			// o cliente cai na PRIMEIRA animacao (ver `ObraDesenhada.MontarSprite`): desenha errado,
			// nao desenha nada. E queixa, mas nao E esta queixa.
			return (Motivo.Desenha, "");

		System.Text.RegularExpressions.Match png = System.Text.RegularExpressions.Regex.Match(
			txt, "\\[ext_resource type=\"Texture2D\" path=\"res://([^\"]+)\"");
		if (!png.Success) return (Motivo.TresSemEstado, "o `.tres` nao aponta pra textura nenhuma");
		string arqPng = Path.Combine(raiz, png.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(arqPng)) return (Motivo.TresAusente, png.Groups[1].Value);
		if (!File.Exists(arqPng + ".import")) return (Motivo.PngSemImport, png.Groups[1].Value);
		return (Motivo.Desenha, "");
	}

	private static string Local(string pastaMaps, string res) =>
		res.Length == 0 ? "" : Path.Combine(pastaMaps, Path.GetFileName(res));
}
