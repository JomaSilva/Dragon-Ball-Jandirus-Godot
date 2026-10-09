using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// O CENSO DO QUE BLOQUEIA E NAO APARECE -- `censo &lt;pastaMaps&gt; &lt;pastaCode&gt; &lt;pastaDmm&gt; [pastaPorCima]`.
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
	/// <param name="pastaPorCima">
	/// A pasta de rascunho do comando `repintar`, ou nula. Os arquivos de zona que existirem nela sao
	/// lidos no lugar dos publicados: o censo julga o disco COMO FICARIA com o rascunho aplicado. E a
	/// mesma chave da `CidadeBench`, pelo mesmo motivo -- um rascunho que deixa esta bancada vermelha
	/// tem que ser visto antes de ir pro `Assets/Maps`, e nao depois.
	/// </param>
	public static int Run(string pastaMaps, string pastaCode, string pastaDmm, bool semDuro = false,
						  string? pastaPorCima = null)
	{
		_porCima = pastaPorCima;
		Console.WriteLine("=== CENSO: O QUE BLOQUEIA E NAO APARECE ===\n");
		if (semDuro)
			Console.WriteLine("!! `--semduro`: o plano do indestrutivel NAO sera lido. Este e o CONTROLE\n"
							+ "!! NEGATIVO -- o censo TEM que reprovar aqui. Se ele passar, a guarda morreu.\n");
		if (_porCima != null)
			Console.WriteLine($"JULGANDO O DISCO COM O RASCUNHO POR CIMA: {_porCima}\n");

		string manifesto = Path.Combine(pastaMaps, "manifest.json");
		if (!File.Exists(manifesto)) { Console.WriteLine($"sem {manifesto}"); return 1; }
		ZoneCatalog cat = ZoneCatalog.Parse(File.ReadAllText(manifesto));

		// O RASCUNHO QUE ESPELHA O REPO traz o indice e o catalogo DELE na pasta `Data` ao lado da de mapas
		// (o comando `acrescentar` acrescenta fontes ao tileset): um `.pedacos` do rascunho so se julga
		// com o indice que conhece as fontes dele.
		string dataDir = Path.Combine(Path.GetDirectoryName(pastaMaps.TrimEnd('/', '\\'))!, "Data");
		string Dado(string nome)
		{
			string doRascunho = _porCima == null ? "" : Path.GetFullPath(Path.Combine(_porCima, "..", "Data", nome));
			if (doRascunho.Length > 0 && File.Exists(doRascunho))
			{
				Console.WriteLine($"   {nome}: o do rascunho ({doRascunho})");
				return doRascunho;
			}
			return Path.Combine(dataDir, nome);
		}
		CatalogoDeObras obras = CatalogoDeObras.Parse(File.ReadAllText(Dado("construcoes.json")));
		CatalogoDeTiles tiles = CatalogoDeTiles.Parse(File.ReadAllText(Dado("tiles.json")));

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
		var atrasDaBarreira = new List<Achado>();// a aresta aparece e o corpo denso da mesma celula nao
		CelulaDeExemplo? arestaAntesDoCorpo = null;

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
					string? quemDesenha = null;
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
							quemDesenha = bp;
							disfarcados.Add(new Achado(rot, x, y, bp, m, det));
							break;
						}
						if (m == Motivo.Desenha) { quemDesenha = bp; break; }
						pior ??= new Achado(rot, x, y, bp, m, det);
					}

					// a primeira celula do censo em que o mapa lista a aresta ANTES de um corpo denso com
					// arte: e a composicao do contra-exemplo la embaixo
					arestaAntesDoCorpo ??= ArestaAntesDoCorpo(rot, x, y, lv, turfs, tiles, obras);

					if (quemDesenha != null)
					{
						// ============================ A BARREIRA QUE APARECE NAO DESCULPA O CORPO ============================
						// "Basta um aparecer" vale entre iguais. Uma aresta de penhasco e um risco no chao: ela
						// desenhar nao conta ao jogador que ha um PINHEIRO naquela celula. Em Icer o mapa lista a
						// aresta antes da arvore em nove celulas, o conversor dava a camada de objetos ao primeiro
						// `/obj` da lista, e este censo fechava verde com nove arvores densas sem pintura.
						// ====================================================================================================
						if (EhBarreira(quemDesenha)
							&& CorpoMudo(rot, travas, x, y, lv, turfs, tiles, obras, pintado.GetValueOrDefault(k),
										 maquinas.GetValueOrDefault(k), portas.GetValueOrDefault(k), raizProjeto) is { } mudo)
							atrasDaBarreira.Add(mudo);
						continue;
					}

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

		// =====================================================================
		// O CORPO ATRAS DA BARREIRA: a aresta aparece, a arvore densa da mesma celula nao
		// =====================================================================
		Console.WriteLine($"\n=== O CORPO ATRAS DA BARREIRA: {atrasDaBarreira.Count} celula(s) em que a aresta aparece "
						  + "e o corpo denso NAO ===");
		Console.WriteLine("    (a celula bloqueia e mostra so o risco do penhasco; o que para o corpo esta no `.dmm` e nao");
		Console.WriteLine("     e pintado. Conserto: o comando `repintar` -- ver `MapConverter.DoisDaCelula`.)");
		foreach (var g in atrasDaBarreira.GroupBy(a => (a.Zona, a.Tipo)).OrderByDescending(g => g.Count()))
			Console.WriteLine($"{g.Count(),9}  {g.Key.Zona,-24} {g.Key.Tipo,-34} "
							  + $"ex.: {string.Join(" ", g.Take(4).Select(a => $"({a.X},{a.Y})"))}");
		if (atrasDaBarreira.Count == 0) Console.WriteLine("    (nenhuma)");
		int cobrancaCega = ContraExemploDoCorpo(arestaAntesDoCorpo, turfs, tiles, obras, raizProjeto);

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
		int fontesErradas = FontesAcrescentadas(cat, pastaMaps, pastaDmm, tiles, raizProjeto)
							+ Pilhas(cat, pastaMaps, tiles, obras, turfs, porZ);

		int reprovas = achados.Count + ruins + vazioQuebravel.Count + atrasDaBarreira.Count + cobrancaCega + fontesErradas;
		Console.WriteLine(reprovas == 0
			? "\n=== VEREDITO: nada bloqueia sem aparecer, e o vazio que e invisivel de origem NAO CEDE "
			  + "a soco nenhum. ==="
			: $"\n=== VEREDITO: REPROVADO -- {achados.Count} celula(s) mudas, {ruins} construcao(oes) "
			  + $"densa(s) sem arte, {vazioQuebravel.Count} celula(s) de vazio que ainda quebram, "
			  + $"{atrasDaBarreira.Count} corpo(s) sem pintura atras de uma barreira"
			  + (fontesErradas > 0 ? $", {fontesErradas} afirmacao(oes) das fontes novas e das pilhas" : "")
			  + (cobrancaCega > 0 ? $" e {cobrancaCega} afirmacao(oes) do contra-exemplo que nao fecharam" : "") + ". ===");
		return reprovas == 0 ? 0 : 1;
	}

	// =====================================================================
	/// <summary>
	/// AS CELULAS EM QUE O NOME DA FOLHA NAO BASTA PRA SABER A FONTE -- uma por caso, com a textura e a
	/// ancora ESCRITAS AQUI (medidas no `.dmi` do original e no `.dmm`, nao perguntadas ao conversor):
	///   K     a folha do JOGO, e nao a homonima da arvore `DU/` (`White rock.dmi` do minerio e 32x32; a
	///         `DU/Map/white rock.png` e uma rocha de 250x250. O `ATM` da `Lab.dmi` tem outro desenho la);
	///   N     o desenho que o BYOND nao centra: a estatua de 98x164 (`-(98-32)/2`, `(164-32)/2`) e a
	///         fonte de sangue de 145x112 do Inferno;
	///   arte  a folha que nunca tinha sido copiada: as arvores `jungletree3.png` de Hera.
	/// </summary>
	private static readonly (int Z, int X, int Y, string Tipo, string Icone, string Estado, string Textura, string Ancora, string Familia)[] FontesCobradas =
	[
		(4, 439, 376, "/obj/Raw_Material/Quartz", "White rock.dmi", "", "res://Assets/Sprites/Misc/Objects/Materials/Ores/White rock.png", "", "K"),
		(10, 88, 6, "/obj/buildables/ATM", "Lab.dmi", "ATM", "res://Assets/Sprites/Misc/Objects/Technology/Lab.png", "", "K"),
		(9, 56, 119, "/turf/decor/HellStatue1", "Hell Statue.dmi", "", "res://Assets/Sprites/Turfs/Hell Statue.png", "@-33,66", "N"),
		(9, 50, 31, "/turf/decor/PondBlood", "Pond Blood.dmi", "", "res://Assets/Sprites/Turfs/Pond Blood.png", "@-56,40", "N"),
		(11, 200, 73, "/obj/Trees/JungleTree2", "jungletree3.png", "", "res://Assets/Sprites/Trees/jungletree3.png", "", "arte"),
	];

	/// <summary>
	/// COBRA AS FONTES ACRESCENTADAS nas celulas da tabela, e prova com defeito injetado que a cobranca
	/// nao e cega: a escolha de folha (`MapConverter.FolhaDoTipoParaBancada`) e a de ancora
	/// (`MapConverter.AncoraDoTipo`) sao chamadas do jeito certo e com o defeito ligado, e o resultado
	/// com defeito tem que ser exatamente o que a cobranca recusa.
	/// </summary>
	/// <returns>Quantas afirmacoes nao fecharam.</returns>
	private static int FontesAcrescentadas(ZoneCatalog cat, string pastaMaps, string pastaDmm, CatalogoDeTiles tiles,
										   string raizProjeto)
	{
		Console.WriteLine("\n=== A FONTE QUE O NOME DA FOLHA NAO DA: a folha do jogo (K), a ancora do BYOND (N), a arte que faltava ===");
		int falhas = 0;
		void Afirmar(string nome, bool ok)
		{
			Console.WriteLine($"    {(ok ? "ok   " : "FALHA")}  {nome}");
			if (!ok) falhas++;
		}
		bool Serve(AtlasDeTiles a, string textura, string ancora) =>
			a.ResPath == textura && (ancora.Length == 0 ? !a.Nome.Contains('@') : a.Nome.EndsWith(ancora, StringComparison.Ordinal));

		var icones = new IconesDoDm(pastaDmm);
		string sprites = Path.Combine(raizProjeto, "Assets", "Sprites");
		string rascunho = _porCima == null ? raizProjeto : Path.GetFullPath(Path.Combine(_porCima, "..", ".."));
		string Arquivo(string res) => Path.GetFullPath(Path.Combine(raizProjeto, res["res://".Length..].Replace('/', Path.DirectorySeparatorChar)));

		foreach ((int z, int x, int y, string tipo, string icone, string estado, string textura, string ancora, string familia) in FontesCobradas)
		{
			ZoneEntry? e = cat.Entradas.FirstOrDefault(q => q.Z == z);
			string arq = e == null ? "" : Local(pastaMaps, e.Pedacos);
			var pintadas = new List<AtlasDeTiles>();
			if (File.Exists(arq) && PedacosDoMapa.Ler(File.ReadAllBytes(arq)) is { } ped)
				for (int c = 0; c < ped.Camadas.Length; c++)
				{
					if (!ped.Achar(x / ped.Lado, y / ped.Lado, c, out int ini, out int q)) continue;
					for (int i = 0; i < q; i++)
					{
						CelulaDePedaco cel = ped.Celula(ini, i);
						if (cel.X == x && cel.Y == y && tiles.Todos.FirstOrDefault(a => a.Fonte == cel.Fonte) is { } atlas) pintadas.Add(atlas);
					}
				}
			Afirmar($"{familia,-4} {e?.Zona}(z{z}) ({x},{y}) {tipo}: pintado com {textura["res://Assets/Sprites/".Length..]}"
					+ (ancora.Length > 0 ? $", ancora {ancora}" : "")
					+ (pintadas.Count == 0 ? " -- NADA pintado" : $" -- pintado: {string.Join(" + ", pintadas.Select(a => a.Nome))}"),
					pintadas.Any(a => Serve(a, textura, ancora)));

			// O CONTRA-EXEMPLO: a mesma pergunta feita ao conversor, sem e com o defeito
			string folhaPresa = Path.GetFileNameWithoutExtension(icone);
			AtlasDeTiles? presa = tiles.Atlas(folhaPresa);
			string? pngDaPresa = presa == null || presa.ResPath == textura ? null : Arquivo(presa.ResPath);
			(string Folha, string Sufixo) Escolha()
			{
				string png = MapConverter.FolhaDoTipoParaBancada(icones, sprites, rascunho, icone, estado, pngDaPresa);
				// o tamanho do icone sai da folha escolhida; da arte que ainda nao foi copiada, do original
				string? legivel = File.Exists(png) ? png : icones.Resolver(icone, estado);
				DmiFile.Result? m = png.Length == 0 || legivel == null ? null : DmiFile.Read(legivel);
				(int X, int Y)? o = m == null ? null : MapConverter.AncoraDoTipo(tipo, 0, 0, m.IconWidth, m.IconHeight);
				return (png, o is { } a ? $"@{a.X},{a.Y}" : "");
			}
			bool Certa((string Folha, string Sufixo) r) =>
				r.Folha.Replace('\\', '/').EndsWith(textura["res://".Length..], StringComparison.OrdinalIgnoreCase) && r.Sufixo == ancora;

			(string Folha, string Sufixo) semDefeito = Escolha();
			Afirmar($"     ...o conversor escolhe essa fonte ({Path.GetFileName(semDefeito.Folha)}{semDefeito.Sufixo})", Certa(semDefeito));

			MapConverter.FolhaPeloNomeDeTeste = familia != "N";
			MapConverter.TudoCentradoDeTeste = familia == "N";
			try
			{
				(string Folha, string Sufixo) comDefeito = Escolha();
				Afirmar($"     (defeito injetado: {(familia == "N" ? "todo desenho centrado" : "a folha achada so pelo nome")}) "
						+ $"sairia {(comDefeito.Folha.Length == 0 ? "SEM folha" : Path.GetRelativePath(sprites, comDefeito.Folha).Replace('\\', '/'))}"
						+ $"{comDefeito.Sufixo} -- e a cobranca de cima acusa", !Certa(comDefeito));
			}
			finally
			{
				MapConverter.FolhaPeloNomeDeTeste = false;
				MapConverter.TudoCentradoDeTeste = false;
			}
		}
		return falhas;
	}

	/// <summary>
	/// AS CELULAS QUE NAO CABEM EM "CHAO, DECORACAO E OBJETOS", uma por jeito de nao caber, com o numero de
	/// desenhos ESCRITO AQUI (contado no `.dmm`):
	///   Namek (69,222)     a quina da ponte: `N025`, `decor/bridgeW` e `Bridge/Edges/bridgeS` -- 3;
	///   Terra (164,7)      tres arestas (`Edge5E`, `Edge5N`, `Edge5W`) sobre o gelo -- 4;
	///   Lookout (332,462)  o piso, a faca `o3` e a mesa `/turf/decor/Table4` por cima dela -- 3, e a faca
	///                      numa camada ABAIXO da mesa (`layer` 3 contra 4);
	///   estacao (92,28)    o vidro: cinco turfs empilhados -- 5.
	/// </summary>
	private static readonly (int Z, int X, int Y, int Desenhos, string Baixo, string Cima, string Oque)[] PilhasCobradas =
	[
		(2, 69, 222, 3, "", "", "a quina da ponte de Namek"),
		(1, 164, 7, 4, "", "", "as tres arestas sobre o gelo"),
		(12, 332, 462, 3, "/obj/buildables/o3", "/turf/decor/Table4", "a mesa por cima da faca"),
		(27, 92, 28, 5, "", "", "o vidro da estacao"),
	];

	/// <summary>Os tiles pintados numa celula do andar: a camada (pela ordem do `.pedacos`) e a fonte de cada um.</summary>
	private static List<(int Camada, AtlasDeTiles Atlas, int X, int Y)> PintadoNaCelula(ZoneCatalog cat, string pastaMaps,
																				 CatalogoDeTiles tiles, int z, int x, int y)
	{
		var saida = new List<(int, AtlasDeTiles, int, int)>();
		ZoneEntry? e = cat.Entradas.FirstOrDefault(q => q.Z == z);
		string arq = e == null ? "" : Local(pastaMaps, e.Pedacos);
		if (!File.Exists(arq) || PedacosDoMapa.Ler(File.ReadAllBytes(arq)) is not { } ped) return saida;

		for (int c = 0; c < ped.Camadas.Length; c++)
		{
			if (!ped.Achar(x / ped.Lado, y / ped.Lado, c, out int ini, out int q)) continue;
			for (int i = 0; i < q; i++)
			{
				CelulaDePedaco cel = ped.Celula(ini, i);
				if (cel.X == x && cel.Y == y && tiles.Todos.FirstOrDefault(a => a.Fonte == cel.Fonte) is { } atlas)
					saida.Add((c, atlas, cel.Ax, cel.Ay));
			}
		}
		return saida;
	}

	/// <summary>
	/// COBRA AS PILHAS da tabela e prova, com o defeito injetado (`MapConverter.SoTresDesenhosDeTeste`),
	/// que a cobranca acusa quando a celula volta a guardar so tres desenhos. A lista de desenhos da
	/// celula e montada AQUI, do `.dmm` (turfs sem repeticao, objetos soltos com arte).
	/// </summary>
	/// <returns>Quantas afirmacoes nao fecharam.</returns>
	private static int Pilhas(ZoneCatalog cat, string pastaMaps, CatalogoDeTiles tiles, CatalogoDeObras obras,
							  Dictionary<string, TurfDef> turfs, Dictionary<int, (DmmMap.Result D, DmmLevel N)> porZ)
	{
		Console.WriteLine("\n=== A CELULA QUE NAO CABE EM TRES DESENHOS: turf do meio, terceira aresta, turf por cima de objeto ===");
		int falhas = 0;
		void Afirmar(string nome, bool ok)
		{
			Console.WriteLine($"    {(ok ? "ok   " : "FALHA")}  {nome}");
			if (!ok) falhas++;
		}
		bool Tinta(string tp, out AtlasDeTiles? a)
		{
			a = turfs.TryGetValue(DmmMap.BasePath(tp), out TurfDef? td) && td.Icon != null
				? tiles.Atlas(Path.GetFileNameWithoutExtension(td.Icon)) : null;
			return a != null;
		}

		foreach ((int z, int x, int y, int esperados, string baixo, string cima, string oque) in PilhasCobradas)
		{
			if (!porZ.TryGetValue(z, out (DmmMap.Result D, DmmLevel N) lv)) { Afirmar($"z{z}: sem `.dmm`", false); continue; }
			List<(int Camada, AtlasDeTiles Atlas, int X, int Y)> pintado = PintadoNaCelula(cat, pastaMaps, tiles, z, x, y);
			Afirmar($"z{z} ({x},{y}) {oque}: {esperados} desenhos na celula -- pintados {pintado.Count} "
					+ $"({string.Join(", ", pintado.Select(p => $"camada {p.Camada}: {p.Atlas.Nome}"))})", pintado.Count == esperados);

			// os desenhos da celula, do `.dmm`: os turfs (vale a ultima vez de cada um) e os objetos soltos
			string[] tipos = lv.D.Keys[lv.N.Cells[x, y]!];
			var daCelula = new List<string>();
			foreach (string tp in tipos.Where(t => DmmMap.BasePath(t).StartsWith("/turf", StringComparison.Ordinal)))
			{
				daCelula.Remove(tp);
				daCelula.Add(tp);
			}
			var desenhos = new List<MapConverter.Desenho>();
			foreach (string tp in daCelula)
				if (Tinta(tp, out _))
					desenhos.Add(new MapConverter.Desenho(tp, Turf: true, Real: tp == daCelula[^1], CabeNumTile: true, turfs[DmmMap.BasePath(tp)].Camada));
			foreach (string tp in Soltos(lv, x, y, obras))
				if (Tinta(tp, out AtlasDeTiles? a))
					desenhos.Add(new MapConverter.Desenho(tp, Turf: false, Real: false, a is { LarguraDoIcone: 32, AlturaDoIcone: 32 },
														  turfs[DmmMap.BasePath(tp)].Camada));

			int Conta()
			{
				(List<MapConverter.Desenho> chatos, MapConverter.Desenho? noObjetos) = MapConverter.PilhaDaCelula(desenhos);
				return chatos.Count + (noObjetos == null ? 0 : 1);
			}
			Afirmar($"     ...a pilha do conversor tem os {esperados} (precisa de pilha: {MapConverter.PrecisaDePilha(desenhos)})",
					MapConverter.PrecisaDePilha(desenhos) && Conta() == esperados);

			if (baixo.Length > 0)
			{
				(List<MapConverter.Desenho> chatos, _) = MapConverter.PilhaDaCelula(desenhos);
				int iBaixo = chatos.FindIndex(d => DmmMap.BasePath(d.Tipo) == baixo), iCima = chatos.FindIndex(d => DmmMap.BasePath(d.Tipo) == cima);
				// ...e no `.pedacos` a folha de um esta numa camada de numero MENOR que a do outro
				Tinta(baixo, out AtlasDeTiles? folhaDeBaixo);
				Tinta(cima, out AtlasDeTiles? folhaDeCima);
				int camadaDeBaixo = pintado.FindIndex(p => p.Atlas == folhaDeBaixo), camadaDeCima = pintado.FindIndex(p => p.Atlas == folhaDeCima);
				Afirmar($"     ...e nela `{baixo}` fica ABAIXO de `{cima}` (posicoes {iBaixo} e {iCima}); no `.pedacos`: "
						+ string.Join(" < ", pintado.OrderBy(p => p.Camada).Select(p => p.Atlas.Nome)),
						iBaixo >= 0 && iCima > iBaixo && camadaDeBaixo >= 0 && camadaDeCima >= 0
						&& pintado[camadaDeBaixo].Camada < pintado[camadaDeCima].Camada);
			}

			MapConverter.SoTresDesenhosDeTeste = true;
			try
			{
				Afirmar($"     (defeito injetado: so tres desenhos por celula) a pilha ficaria com {Conta()} -- e a cobranca de cima acusa",
						Conta() != esperados);
			}
			finally { MapConverter.SoTresDesenhosDeTeste = false; }
		}
		return falhas;
	}

	/// <summary>Uma celula real do `.dmm` em que o mapa lista a aresta antes de um corpo denso com arte.</summary>
	private sealed record CelulaDeExemplo(string Zona, int X, int Y, (DmmMap.Result D, DmmLevel N) Lv,
										  List<string> Soltos, string Corpo);

	/// <summary>Barreira do original: para o corpo sem ser densa (`barrier.dm:61-62`) e se desenha rente ao chao.</summary>
	private static bool EhBarreira(string bp) => bp.StartsWith("/obj/barrier/", StringComparison.Ordinal);

	/// <summary>
	/// O CORPO DENSO QUE NAO E PINTADO NUMA CELULA EM QUE A BARREIRA E -- ou nulo, se algum corpo dela
	/// aparece (entre corpos, basta um) ou se ela nao tem corpo nenhum.
	///
	/// So acusa o que o conversor PODERIA ter pintado (`Motivo.NaoPintado`): arte que nunca entrou no
	/// tileset e outro conserto, e o que nao tem `icon` no DM nao aparecia la tambem.
	/// </summary>
	private static Achado? CorpoMudo(string rot, List<string> travas, int x, int y,
									 in (DmmMap.Result D, DmmLevel N) lv, Dictionary<string, TurfDef> turfs,
									 CatalogoDeTiles tiles, CatalogoDeObras obras,
									 List<(int F, int X, int Y)>? pintado, string? maquinaAqui, string? portaAqui,
									 string raiz)
	{
		Achado? mudo = null;
		foreach (string bp in travas)
		{
			if (!bp.StartsWith("/obj/", StringComparison.Ordinal) || EhBarreira(bp)) continue;
			(Motivo m, string det) = Julgar(bp, x, y, lv, turfs, tiles, obras, pintado, maquinaAqui, portaAqui, raiz);
			if (m is Motivo.Desenha or Motivo.QuadroErrado) return null;
			if (m == Motivo.NaoPintado) mudo ??= new Achado(rot, x, y, bp, m, det);
		}
		return mudo;
	}

	/// <summary>
	/// Os `/obj` soltos da celula, na ordem do mapa e sem repeticao -- a lista que o conversor entrega
	/// ao `MapConverter.DoisDaCelula`. Reescrita aqui de proposito (ver a nota do <see cref="Travas"/>).
	/// </summary>
	private static List<string> Soltos(in (DmmMap.Result D, DmmLevel N) lv, int x, int y, CatalogoDeObras obras)
	{
		var saida = new List<string>();
		if (x >= lv.N.Width || y >= lv.N.Height) return saida;
		string? k = lv.N.Cells[x, y];
		if (k == null || !lv.D.Keys.TryGetValue(k, out string[]? tipos)) return saida;

		foreach (string tp in tipos)
		{
			string bp = DmmMap.BasePath(tp);
			if (!bp.StartsWith("/obj", StringComparison.Ordinal) || bp == "/obj/Tornado" || Passagens.Eh(bp)
				|| obras.PorTypepath(bp) != null) continue;
			if (!saida.Contains(tp)) saida.Add(tp);
		}
		return saida;
	}

	private static MapConverter.Solto FichaDoSolto(string tp, Dictionary<string, TurfDef> turfs, CatalogoDeTiles tiles)
	{
		string bp = DmmMap.BasePath(tp);
		turfs.TryGetValue(bp, out TurfDef? td);
		AtlasDeTiles? a = td?.Icon == null ? null : tiles.Atlas(Path.GetFileNameWithoutExtension(td.Icon));
		return new MapConverter.Solto(a != null, a is { LarguraDoIcone: 32, AlturaDoIcone: 32 },
									  MapConverter.EhAresta(bp), td?.Camada ?? 3);
	}

	/// <summary>O primeiro quadro que o tileset tem pra este typepath, ou nulo.</summary>
	private static (int F, int X, int Y)? Tinta(string? tp, Dictionary<string, TurfDef> turfs, CatalogoDeTiles tiles)
	{
		if (tp == null || !turfs.TryGetValue(DmmMap.BasePath(tp), out TurfDef? td) || td.Icon == null) return null;
		return tiles.Achar(Path.GetFileNameWithoutExtension(td.Icon), td.IconState ?? "");
	}

	/// <summary>Esta celula lista uma aresta com arte ANTES de um corpo denso com arte? Devolve-a pro contra-exemplo.</summary>
	private static CelulaDeExemplo? ArestaAntesDoCorpo(string rot, int x, int y, in (DmmMap.Result D, DmmLevel N) lv,
													   Dictionary<string, TurfDef> turfs, CatalogoDeTiles tiles,
													   CatalogoDeObras obras)
	{
		List<string> soltos = Soltos(lv, x, y, obras);
		bool aresta = false;
		foreach (string tp in soltos)
		{
			string bp = DmmMap.BasePath(tp);
			if (Tinta(tp, turfs, tiles) == null) continue;
			if (MapConverter.EhAresta(bp)) aresta = true;
			else if (aresta && !EhBarreira(bp) && turfs[bp].Density) return new CelulaDeExemplo(rot, x, y, lv, soltos, tp);
		}
		return null;
	}

	/// <summary>
	/// O CONTRA-EXEMPLO da cobranca do corpo atras da barreira, numa celula REAL do mapa.
	///
	/// A cobranca le o `.pedacos`, e um `.pedacos` consertado nunca a faria falar -- "esta verde" ficaria
	/// igual a "nao esta olhando". Entao a celula e pintada DE CONTA duas vezes, com o que a escolha do
	/// conversor (`MapConverter.DoisDaCelula`) manda pintar: do jeito certo a cobranca cala; com o
	/// defeito injetado (`PrimeiroObjetoVenceDeTeste`) a aresta fica com a camada, o corpo fica sem
	/// nenhuma, e ela TEM que acusar.
	/// </summary>
	/// <returns>Quantas afirmacoes nao fecharam.</returns>
	private static int ContraExemploDoCorpo(CelulaDeExemplo? exemplo, Dictionary<string, TurfDef> turfs,
											CatalogoDeTiles tiles, CatalogoDeObras obras, string raiz)
	{
		if (exemplo is not { } c)
		{
			Console.WriteLine("    FALHA  nenhuma celula que bloqueia lista uma aresta antes de um corpo denso: "
							  + "o contra-exemplo ficou sem onde rodar");
			return 1;
		}

		int falhas = 0;
		void Afirmar(string nome, bool ok)
		{
			Console.WriteLine($"    {(ok ? "ok   " : "FALHA")}  {nome}");
			if (!ok) falhas++;
		}

		List<string> travas = Travas(c.Lv, c.X, c.Y, turfs);
		Achado? Cobrar()
		{
			(string? objeto, string? embaixo, _) = MapConverter.DoisDaCelula(c.Soltos, tp => FichaDoSolto(tp, turfs, tiles));
			var tinta = new List<(int F, int X, int Y)>();
			if (Tinta(objeto, turfs, tiles) is { } t1) tinta.Add(t1);
			if (Tinta(embaixo, turfs, tiles) is { } t2) tinta.Add(t2);
			return CorpoMudo(c.Zona, travas, c.X, c.Y, c.Lv, turfs, tiles, obras, tinta, null, null, raiz);
		}

		string onde = $"{c.Zona}({c.X},{c.Y}) [{string.Join(", ", c.Soltos.Select(DmmMap.BasePath))}]";
		Afirmar($"contra-exemplo em {onde}: com a escolha do conversor o corpo e pintado e a cobranca cala", Cobrar() == null);

		MapConverter.PrimeiroObjetoVenceDeTeste = true;
		try
		{
			Afirmar("(defeito injetado: o primeiro objeto da lista fica com a camada) a cobranca ACUSA "
					+ $"{DmmMap.BasePath(c.Corpo)}", Cobrar() is { } a && a.Tipo == DmmMap.BasePath(c.Corpo));
		}
		finally { MapConverter.PrimeiroObjetoVenceDeTeste = false; }
		return falhas;
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
		string estado = td.IconState ?? "";

		// A FOLHA DO NOME E AS FONTES ACRESCENTADAS QUE DIVIDEM O NOME COM ELA. O tileset pode ter mais de
		// uma fonte pra mesma folha -- a variante com a ancora do BYOND (`Hell Statue@-33,66`) e a folha do
		// jogo no lugar da homonima (`White rock~Misc.Objects.Materials.Ores`); ver
		// `MapConverter.FonteDoTipo`. A pergunta daqui e "o tipo aparece?", e ele aparece pintado por
		// qualquer uma delas. Vale o veredito da primeira que disser que sim; senao, o da folha do nome.
		(Motivo, string)? daPrimeira = null;
		foreach (AtlasDeTiles folha in tiles.Todos.Where(t => NomeDaFolha(t.Nome).Equals(atlas, StringComparison.OrdinalIgnoreCase))
											 .OrderBy(t => t.Fonte))
		{
			(Motivo m, string det) v = NaFolha(folha);
			if (v.m is Motivo.Desenha or Motivo.QuadroErrado) return v;
			daPrimeira ??= v;
		}
		return daPrimeira ?? (Motivo.AtlasForaDoTileset, $"`{td.Icon}` nao virou atlas");

		(Motivo, string) NaFolha(AtlasDeTiles a)
		{
		// A PROVA E POR TYPEPATH, E NAO POR CELULA. "A celula tem alguma tinta" e exatamente a
		// leitura que deixou a queixa do dono passar: o piso pinta, a mesa nao, e a celula parece
		// desenhada. O que vale e a celula apontar pra ESTA folha.
		if (a.Estados.TryGetValue(estado, out (int X, int Y) quadro))
		{
			(int Fonte, int X, int Y) trio = (a.Fonte, quadro.X, quadro.Y);
			if (pintado != null && pintado.Contains((trio.Fonte, trio.X, trio.Y))) return (Motivo.Desenha, "");

			// OUTRA DIRECAO DO MESMO ESTADO E O MESMO TIPO APARECENDO. O conversor pinta o quadro da
			// direcao do tipo (`dir`, `barrier.dm:92-211`: as arestas viradas pro norte, leste e oeste
			// moram 1, 2 e 3 quadros depois do sul), e o indice so guarda o primeiro. A pergunta daqui
			// e "ele aparece?", e nao "pra onde ele olha": vale qualquer quadro DENTRO do estado.
			if (pintado != null)
			{
				int primeiro = trio.Y * a.Colunas + trio.X, quadros = QuadrosDoEstado(a, trio, raiz);
				foreach ((int f, int px, int py) in pintado)
				{
					int adiante = py * a.Colunas + px - primeiro;
					if (f == trio.Fonte && adiante > 0 && adiante < quadros) return (Motivo.Desenha, "");
				}
			}

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
	}

	/// <summary>O nome da FOLHA numa entrada do indice: o que vem antes da pasta (`~`) e da ancora (`@`) de uma fonte acrescentada.</summary>
	private static string NomeDaFolha(string nomeNoIndice)
	{
		int corte = nomeNoIndice.IndexOfAny(['~', '@']);
		return corte < 0 ? nomeNoIndice : nomeNoIndice[..corte];
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

	/// <summary>A pasta lida por cima da publicada (ver o <c>pastaPorCima</c> do <see cref="Run"/>), ou nula.</summary>
	private static string? _porCima;

	/// <summary>O arquivo de zona que o jogo leria: o do rascunho por cima, quando ha um, senao o publicado.</summary>
	private static string Local(string pastaMaps, string res)
	{
		if (res.Length == 0) return "";
		string nome = Path.GetFileName(res);
		return _porCima != null && File.Exists(Path.Combine(_porCima, nome))
			? Path.Combine(_porCima, nome)
			: Path.Combine(pastaMaps, nome);
	}

	private static readonly Dictionary<string, DmiFile.Result?> _folhas = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// QUANTOS QUADROS O ESTADO QUE COMECA NESTE QUADRO OCUPA NA FOLHA (direcoes x quadros), lido do
	/// proprio `.png`. 1 quando a folha nao abre ou nenhum estado comeca ali.
	///
	/// E a leitura DA BANCADA, e nao a do conversor (`MapConverter.Coord`): o indice de tiles so guarda
	/// o primeiro quadro de cada estado, e pra saber ate onde o estado vai e preciso a folha.
	/// </summary>
	private static int QuadrosDoEstado(AtlasDeTiles a, (int Fonte, int X, int Y) primeiro, string raiz)
	{
		if (!_folhas.TryGetValue(a.ResPath, out DmiFile.Result? folha))
		{
			string png = Path.Combine(raiz, a.ResPath.Replace("res://", "").Replace('/', Path.DirectorySeparatorChar));
			_folhas[a.ResPath] = folha = File.Exists(png) ? DmiFile.Read(png) : null;
		}
		if (folha == null) return 1;

		int alvo = primeiro.Y * a.Colunas + primeiro.X, indice = 0;
		foreach (DmiState st in folha.States)
		{
			int tamanho = Math.Max(1, st.Dirs) * Math.Max(1, st.Frames);
			if (indice == alvo) return tamanho;
			indice += tamanho;
		}
		return 1;
	}
}
