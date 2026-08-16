using System.Globalization;
using System.Text;

namespace Jandirus.Tools;

/// <summary>
/// .dmm -> uma CENA POR ANDAR (decisao do dono do projeto: cada planeta pre-feito e uma
/// cena instanciada na principal, o que casa com o corte de interesse por zona).
///
/// Gera:
///   Assets/Maps/tileset.tres   -- um TileSet com uma fonte por atlas de turf
///   Assets/Maps/&lt;nome&gt;.tscn -- Node2D + TileMapLayer preenchido
///
/// COLISAO: turf com `density = 1` ganha poligono de fisica; `opacity = 1` ganha oclusor de
/// luz. E o mesmo dado que o SERVIDOR vai carregar pra validar movimento de verdade (hoje
/// ele so confere velocidade).
/// </summary>
public static class MapConverter
{
	/// <summary>
	/// A MARCA DE UMA CELULA: um numero que muda se qualquer campo dela mudar.
	///
	/// Somar os campos nao serviria -- trocar X por Y daria o mesmo total. Passar cada campo por
	/// uma mistura (FNV) faz duas celulas parecidas caírem longe uma da outra, e o XOR das marcas
	/// nao depende da ORDEM, que e justamente o que o agrupamento em pedacos muda.
	/// </summary>
	private static ulong Marca(Jandirus.Core.World.CelulaDePedaco c, int camada)
	{
		ulong h = 1469598103934665603UL;
		ulong[] campos = [(ushort)c.X, (ushort)c.Y, c.Fonte, c.Ax, c.Ay, (ulong)camada];
		foreach (ulong v in campos) h = (h ^ v) * 1099511628211UL;
		return h;
	}

	private static ulong Assinar(List<Jandirus.Core.World.CelulaDePedaco> celulas, int camada)
	{
		ulong x = 0;
		foreach (Jandirus.Core.World.CelulaDePedaco c in celulas) x ^= Marca(c, camada);
		return x;
	}

	private const int Cell = 32;

	/// <summary>
	/// AS CAMADAS NAO CONSTROEM FISICA -- e essa linha vale quase um segundo por mapa.
	///
	/// ============================ O QUE ELA CONSERTA ============================
	/// O tileset traz poligono de colisao em todo tile denso (447 deles), e um `TileMapLayer` com
	/// colisao ligada monta um corpo de fisica pra CADA celula solida no primeiro quadro. Sao tres
	/// camadas de 500x500 por planeta, e o trabalho todo cai num quadro so: medido, 798 ms de tela
	/// congelada so na Terra.
	///
	/// E o pior e que NINGUEM USA. O movimento deste jogo nao passa por fisica do Godot em lugar
	/// nenhum -- nao ha `CharacterBody2D`, `move_and_slide` nem consulta de mundo fisico no cliente
	/// inteiro. Quem decide onde da pra andar e o `ZoneCollision`, o bitset do `.col`, no cliente e
	/// no servidor. A fisica do tilemap era uma copia paralela da mesma verdade, construida toda
	/// troca de mapa e nunca consultada.
	///
	/// A OCLUSAO FICA. Ela e usada de verdade: e o que faz parede esconder o que esta atras
	/// (`Iluminacao` e `Visao`), e sai por outro campo do tileset.
	///
	/// O POLIGONO CONTINUA NO TILESET de proposito: ele nao custa nada parado, e e o que faz o
	/// EDITOR mostrar onde ha parede pra quem for pintar mapa a mao.
	/// ============================================================================
	/// </summary>
	private const string SemFisica = "collision_enabled = false\n";


	private sealed class Fonte
	{
		public int Id;
		public string ResPath = "";

		/// <summary>Caminho no DISCO -- e a chave do dicionario de fontes, e o que o TurfDef guarda.</summary>
		public string Chave = "";
		public int IconW, IconH, Cols;
		public Dictionary<string, int> StateIndex = new(StringComparer.OrdinalIgnoreCase); // icon_state -> indice do 1o quadro

		/// <summary>Os estados como o .dmi os declara -- e daqui que saem as animacoes.</summary>
		public List<DmiState> States = [];

		/// <summary>Quantos quadros a folha tem de verdade (o resto da grade e sobra vazia).</summary>
		public int TotalQuadros;
		public HashSet<(int X, int Y)> Usadas = [];
		public HashSet<(int X, int Y)> Densas = [];
		public HashSet<(int X, int Y)> Opacas = [];

		/// <summary>
		/// O ATLAS COMPANHEIRO desta folha -- uma tira por estado que nao cabia numa linha.
		/// Nulo quando todas as animacoes desta folha ja cabiam. Ver <see cref="AtlasAnimado"/>.
		/// </summary>
		public Fonte? Companheira;

		/// <summary>Estado reempacotado -> a LINHA dele no companheiro. Vazio = nada foi reempacotado.</summary>
		public Dictionary<string, int> Refeitos = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>Por linha do companheiro: quantos quadros e quanto dura cada um (ja em segundos).</summary>
		public List<double[]> Duracoes = [];
	}

	public static void Convert(string dmmDir, string spritesDir, string outDir, Dictionary<string, TurfDef> turfs)
	{
		Directory.CreateDirectory(outDir);

		// A RAIZ DO PROJETO GODOT, achada pelo `project.godot` -- NAO o diretorio de trabalho.
		//
		// Os caminhos `res://` do tileset saiam do cwd, e o resultado era que rodar o pipeline
		// de DENTRO de Tools/AssetPipeline escrevia `res://../../Assets/Sprites/...`: nem o
		// jogo nem o editor acham a textura, o TileSet carrega com ZERO tiles e o editor cospe
		// "TileSetAtlasSource has no tile at (0,0)" pra cada celula do mapa. De qual pasta o
		// comando foi chamado nao pode decidir o conteudo do arquivo gerado.
		string? raizAchada = AcharRaiz(outDir);
		string raiz = raizAchada ?? Directory.GetCurrentDirectory();
		if (raizAchada == null)
			Console.WriteLine("AVISO: nao achei o project.godot subindo de " + outDir +
							  " -- os caminhos res:// vao sair relativos ao diretorio atual");

		// INDICE POR NOME -> TODOS OS CANDIDATOS.
		//
		// A arvore de icones tem 79 nomes REPETIDOS em pastas diferentes, e o DM so escreve o
		// nome (`icon = 'Namek.dmi'`) -- quem resolve o caminho e o FILE_DIR do DreamMaker.
		// Guardar um caminho por nome fazia o ultimo varrido ganhar, e o resultado apareceu na
		// tela: `Character Icons/Namekians/Namek.dmi` (o PERSONAGEM) venceu `Turfs/Namek.dmi`
		// (as CASAS), e as casas de Namek foram desenhadas com Namekuseijins empilhados.
		// `Trees.dmi` tinha o mesmo problema.
		//
		// A desambiguacao e por DADO, nao por palpite: ganha o arquivo que REALMENTE tem o
		// `icon_state` que o typepath pediu.
		var atlasPorNome = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (string png in Directory.GetFiles(spritesDir, "*.png", SearchOption.AllDirectories))
		{
			string chave = Path.GetFileNameWithoutExtension(png);
			if (!atlasPorNome.TryGetValue(chave, out List<string>? l)) atlasPorNome[chave] = l = [];
			l.Add(png);
		}
		int repetidos = atlasPorNome.Count(kv => kv.Value.Count > 1);
		if (repetidos > 0)
			Console.WriteLine($"nomes de atlas repetidos: {repetidos} (resolvidos pelo icon_state)");

		var fontes = new Dictionary<string, Fonte>(StringComparer.OrdinalIgnoreCase);
		var semAtlas = new HashSet<string>();
		int proxId = 0;

		// ---- passada 1: descobrir quais tiles cada mapa usa ----
		// CADA .dmm NUMERA O PROPRIO z A PARTIR DE 1. O z real do jogo vem da ORDEM em que o
		// .dme inclui os arquivos: o 1o mapa ocupa z1..zN, o 2o continua de zN+1, e assim por
		// diante. Sem este deslocamento, quatro mapas diferentes gerariam quatro "z01".
		var mapas = new List<(string Arquivo, DmmMap.Result Dados, int Offset)>();
		int offset = 0;
		foreach (string dmm in OrdemDoDme(dmmDir))
		{
			DmmMap.Result d = DmmMap.Read(dmm);
			mapas.Add((dmm, d, offset));
			offset += d.Levels.Count;
		}

		// ============================ A CIDADE DE VEGETA NAO ESTA NO `.dmm` ============================
		// Ela e o unico cenario do jogo que o original ERGUE POR CODIGO no boot (`VegetaCity.dm`), e
		// por isso nunca existiu no port: quem le so o mapa nao ve o que o mapa nao guarda. Ela entra
		// AQUI, antes da passada 1, porque as pecas dela precisam registrar atlas como qualquer outra
		// celula -- carimbar depois deixaria as paredes sem fonte no tileset.
		//
		// O FREIO E O `temArte`: peca sem desenho nao e carimbada. Ver `CidadeDeVegeta.Erguer`.
		bool TemArte(string bp)
		{
			if (!turfs.TryGetValue(bp, out TurfDef? td) || td.Icon == null) return false;
			Fonte? f = Garantir(td.Icon, td.IconState, raiz, fontes, atlasPorNome, semAtlas, ref proxId);
			return f != null && f.StateIndex.ContainsKey(td.IconState ?? "");
		}
		foreach ((string _, DmmMap.Result d3, int off3) in mapas)
			foreach (DmmLevel n3 in d3.Levels)
			{
				if (n3.Z + off3 != CidadeDeVegeta.Z) continue;
				CidadeDeVegeta.Relatorio r = CidadeDeVegeta.Erguer(d3, n3, TemArte);
				Console.WriteLine($"cidade de Vegeta: {r.Celulas} celulas carimbadas "
								  + $"({r.Paredes} paredes, {r.Portas} portas, {r.Moveis} moveis, "
								  + $"{r.Maquinas} maquinas)");
				// O QUE NAO ENTROU SE ANUNCIA. Peca sem arte que virasse celula densa seria uma
				// parede invisivel -- exatamente o defeito que este pipeline existe pra nao produzir.
				foreach (string bp in r.SemArte)
					Console.WriteLine($"   SEM ARTE, NAO CARIMBADA: {bp} "
									  + $"(icon_state '{(turfs.GetValueOrDefault(bp)?.IconState ?? "")}' "
									  + $"nao existe em '{turfs.GetValueOrDefault(bp)?.Icon ?? "?"}')");
			}

		// TURF E OBJ, os dois. So o turf era registrado, e essa era a causa de duas queixas que
		// pareciam separadas: "falta coisa no mapa" e "tem parede invisivel". Sao a MESMA coisa
		// -- 41% dos prefabs da Terra tem um /obj (arvore, minerio, cerca, cadeira), o desenho
		// ignorava todos e a colisao NAO: uma AppleTree densa virava um muro que ninguem via.
		foreach ((string _, DmmMap.Result dados, int _off) in mapas)
			foreach (string[] tipos in dados.Keys.Values)
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (!Desenhavel(bp)) continue;
					if (!turfs.TryGetValue(bp, out TurfDef? td) || td.Icon == null) continue;

					Fonte? f = Garantir(td.Icon, td.IconState, raiz, fontes, atlasPorNome, semAtlas, ref proxId);
					if (f == null) continue;
					td.Atlas = f.Chave;   // guarda QUAL arquivo venceu: o resto do pipeline usa este

					(int X, int Y) coord = Coord(f, td.IconState);
					f.Usadas.Add(coord);
					if (td.Density) f.Densas.Add(coord);
					if (td.Opacity) f.Opacas.Add(coord);
				}

		// ---- passada 1b: FISICA EM TODO TILE QUE E PAREDE ----
		//
		// Ate aqui so ganhava colisao a celula que algum .dmm usou. Como o tileset agora traz a
		// FOLHA INTEIRA (pra dar pra pintar), a maioria dos tiles de parede ficava sem fisica: o
		// dono pintava um muro no editor e o muro nao parava ninguem.
		//
		// Aqui a densidade vem do TIPO, nao do uso: todo estado de um typepath denso ganha
		// fisica em TODOS os seus quadros e direcoes -- uma parede virada pro norte e parede
		// igual, e a mesma parede no quadro 2 da animacao tambem.
		int marcados = MarcarSolidos(turfs, fontes, atlasPorNome);
		int opacos = fontes.Values.Sum(f => f.Opacas.Count);
		Console.WriteLine($"tiles com fisica : {marcados} | com oclusao: {opacos}");

		// ============================ AS ANIMACOES QUE NAO CABIAM ============================
		// Tem que vir ANTES do tileset E antes das cenas: o reempacote cria FONTES NOVAS e muda a
		// coordenada das celulas que usam esses estados. Fazer depois deixaria as cenas apontando
		// pro quadro parado enquanto o tileset ja anunciava o animado.
		int reempacotadas = Reempacotar(fontes, raiz, ref proxId);
		if (reempacotadas > 0)
			Console.WriteLine($"animacoes reempacotadas: {reempacotadas} (nao cabiam numa linha do atlas)");

		EscreverTileSet(Path.Combine(outDir, "tileset.tres"), fontes);

		// O INDICE DE TILES SAI JUNTO DO TILESET, e tem que ser aqui: os `source id` nascem de
		// um contador por ordem de descoberta, entao um `tiles.json` velho ao lado de um
		// `tileset.tres` novo nao da erro nenhum -- da o SPRITE ERRADO. Gerar os dois na mesma
		// passada e o que impede os dois de envelhecerem em ritmos diferentes.
		//
		// Quem consome: o planeta PROCEDURAL. O gerador do Core devolve `TileVisual(atlas,
		// estado)` -- nomes -- e sem esta tabela nada no jogo sabe transformar isso em celula.
		TileIndex.Resultado idx = TileIndex.Escrever(
			Path.Combine(raiz, "Assets", "Data", "tiles.json"),
			fontes.Values.Select(f => new FonteDeAtlas(
				f.Id, f.Chave, f.ResPath, f.IconW, f.IconH, f.Cols, f.StateIndex)));
		Console.WriteLine($"indice de tiles  : {idx.Atlas} atlas, {idx.Estados} estados"
						  + (idx.Colisoes.Count > 0 ? $" | {idx.Colisoes.Count} nome(s) em disputa" : ""));
		foreach (string c in idx.Colisoes) Console.WriteLine("   " + c);

		// ============================ O MAPA DOS DESTINOS, ANTES DE CONVERTER ============================
		// Uma passagem da Terra aponta pro z 23 (a caverna), que so vai ser convertido daqui a vinte
		// andares. Resolver o destino na hora exigiria que a ordem de conversao casasse com a ordem
		// dos destinos -- o que nao acontece, e nem daria pra garantir com passagens de ida e volta.
		//
		// A CONTA DO Y E A INVERSAO DO BYOND: la o eixo cresce pra CIMA e aqui pra baixo, entao a
		// linha `by` vira `altura - by`. Errar isto poria cada chegada espelhada na vertical -- e,
		// pior, sem sintoma nenhum a nao ser "a caverna me cospe no lugar errado".
		var porZ = new Dictionary<int, (string Nome, int W, int H)>();
		foreach ((string _, DmmMap.Result d2, int off2) in mapas)
			foreach (DmmLevel n2 in d2.Levels)
				porZ[n2.Z + off2] = (NomeDoAndar(d2, n2, off2)[(NomeDoAndar(d2, n2, off2).IndexOf('_') + 1)..],
									 n2.Width, n2.Height);

		Destinos = (bx, by, bz) =>
		{
			if (!porZ.TryGetValue(bz, out (string Nome, int W, int H) alvo)) return null;
			int cx = Math.Clamp(bx - 1, 0, alvo.W - 1);
			int cy = Math.Clamp(alvo.H - by, 0, alvo.H - 1);
			const int t = Jandirus.Core.World.ZoneCollision.TileSize;
			return (alvo.Nome, cx * t + t / 2f, cy * t + t / 2f);
		};

		// ---- passada 2: uma cena por andar + o mapa de colisao que o SERVIDOR le ----
		int cenas = 0, celulas = 0, bloqueadas = 0, totalPortas = 0, totalMaquinas = 0, totalPassagens = 0;
		int totalAgua = 0, totalDuro = 0, totalNuvem = 0;
		var manifesto = new List<string>();
		foreach ((string arquivo, DmmMap.Result dados, int off) in mapas)
			foreach (DmmLevel nivel in dados.Levels)
			{
				string nome = NomeDoAndar(dados, nivel, off);
				string cena = Path.Combine(outDir, nome + ".tscn");

				// A CENA MANDA. O que ela DESENHOU e o que bloqueia -- a colisao sai da mesma
				// passada, nao de uma segunda leitura do .dmm com suas proprias regras. Duas
				// funcoes calculando "o que e parede" por caminhos diferentes divergiam em ~2%
				// das celulas, e divergencia entre o que se ve e o que se atravessa e
				// exatamente a queixa que estamos consertando.
				celulas += EscreverCena(cena, nome, nivel, dados, turfs, fontes, atlasPorNome,
										semAtlas, ref proxId, out HashSet<(int, int)> paredes,
										out Dictionary<(int, int), byte> cegos,
										out List<string> portas,
										out List<string> maquinas,
										out List<string> passagens);
				bloqueadas += EscreverColisao(Path.Combine(outDir, nome + ".col"),
											  nivel.Width, nivel.Height, paredes);
				// mesmo formato, outro proposito: este e o que o CAMPO DE VISAO consulta
				EscreverColisao(Path.Combine(outDir, nome + ".vis"), nivel.Width, nivel.Height, cegos.Keys, cegos);

				// ...e este e a TERCEIRA CLASSE DE CELULA: agua. Mesmo formato de novo, e um
				// arquivo separado pelo mesmo motivo que o `.vis` e separado do `.col` -- as tres
				// perguntas ("para o corpo", "esconde", "e agua") divergem entre si em quase toda
				// celula que importa. Zona seca nao ganha arquivo. Ver `ConverterAguas`.
				List<(int X, int Y)> molhadas = CelulasDeAgua(nivel, dados, turfs);
				string arqAgua = Path.Combine(outDir, nome + ".agua");
				if (molhadas.Count > 0)
				{
					EscreverColisao(arqAgua, nivel.Width, nivel.Height, molhadas);
					totalAgua += molhadas.Count;
				}
				else if (File.Exists(arqAgua)) File.Delete(arqAgua);

				// ...e esta e a QUARTA CLASSE: a nuvem. Mesmo formato de novo, e arquivo separado pelo
				// mesmo motivo dos outros -- "para o corpo", "esconde", "e agua", "cede a um soco" e
				// "e ceu" sao cinco perguntas que divergem entre si em quase toda celula que importa.
				// Zona sem ceu nao ganha arquivo. Ver `ConverterNuvens` e `Core/World/Ceu.cs`.
				List<(int X, int Y)> nuvens = CelulasDeNuvem(nivel, dados, turfs);
				string arqNuvem = Path.Combine(outDir, nome + ".nuvem");
				if (nuvens.Count > 0)
				{
					EscreverColisao(arqNuvem, nivel.Width, nivel.Height, nuvens);
					totalNuvem += nuvens.Count;
				}
				else if (File.Exists(arqNuvem)) File.Delete(arqNuvem);

				// ...e este e O QUE NAO SE QUEBRA: o `destroyable = 0` do original. Arquivo separado
				// pelo mesmo motivo dos outros dois -- "para o corpo", "esconde", "e agua" e "cede a
				// um soco" sao quatro perguntas que divergem entre si em quase toda celula que
				// importa. Ver `ConverterDuros`, que e o caminho que roda sozinho.
				List<(int X, int Y)> duras = CelulasDuras(nivel, dados, turfs);
				string arqDuro = Path.Combine(outDir, nome + ".duro");
				if (duras.Count > 0)
				{
					EscreverColisao(arqDuro, nivel.Width, nivel.Height, duras);
					totalDuro += duras.Count;
				}
				else if (File.Exists(arqDuro)) File.Delete(arqDuro);

				// AS PORTAS DA ZONA. Sai sempre, mesmo vazio: um arquivo que as vezes existe e as
				// vezes nao vira um `if` no leitor, e um `if` a menos vale o punhado de bytes.
				File.WriteAllText(Path.Combine(outDir, nome + ".portas"),
					"[" + string.Join(",\n ", portas) + "]",
					new System.Text.UTF8Encoding(false));
				totalPortas += portas.Count;

				// AS MAQUINAS DO MAPA, pelo mesmo caminho e pelo mesmo motivo das portas.
				File.WriteAllText(Path.Combine(outDir, nome + ".objetos"),
					"[" + string.Join(",\n ", maquinas) + "]",
					new System.Text.UTF8Encoding(false));
				totalMaquinas += maquinas.Count;

				// AS PASSAGENS, pelo mesmo caminho e pelo mesmo motivo das portas.
				File.WriteAllText(Path.Combine(outDir, nome + ".passagens"),
					"[" + string.Join(",\n ", passagens) + "]",
					new System.Text.UTF8Encoding(false));
				totalPassagens += passagens.Count;

				cenas++;

				string zona = nome[(nome.IndexOf('_') + 1)..];
				manifesto.Add($"  {{ \"zona\": \"{zona}\", \"z\": {nivel.Z + off}, \"cena\": \"res://Assets/Maps/{nome}.tscn\", " +
							  $"\"pedacos\": \"res://Assets/Maps/{nome}.pedacos\", " +
							  $"\"colisao\": \"res://Assets/Maps/{nome}.col\", \"visao\": \"res://Assets/Maps/{nome}.vis\", " +
							  $"\"agua\": \"res://Assets/Maps/{nome}.agua\", " +
						  $"\"nuvem\": \"res://Assets/Maps/{nome}.nuvem\", " +
						  $"\"duro\": \"res://Assets/Maps/{nome}.duro\", " +
							  $"\"luzes\": \"res://Assets/Maps/{nome}.luz\", " +
							$"\"portas\": \"res://Assets/Maps/{nome}.portas\", " +
							$"\"objetos\": \"res://Assets/Maps/{nome}.objetos\", " +
							$"\"passagens\": \"res://Assets/Maps/{nome}.passagens\", " +
							  $"\"w\": {nivel.Width}, \"h\": {nivel.Height} }}");
			}

		File.WriteAllText(Path.Combine(outDir, "manifest.json"),
			"[\n" + string.Join(",\n", manifesto) + "\n]\n", new UTF8Encoding(false));

		Conferir(outDir, fontes.Count);

		Console.WriteLine($"mapas lidos    : {mapas.Count}");
		Console.WriteLine($"cenas geradas  : {cenas}");
		Console.WriteLine($"portas         : {totalPortas}");
		Console.WriteLine($"maquinas       : {totalMaquinas} (saem do tilemap e viram construcao)");
		Console.WriteLine($"passagens      : {totalPassagens} (celulas que levam a outro mapa)");
		Console.WriteLine($"agua           : {totalAgua} celulas (terceira classe: para a pe, nao para nadando/voando)");
		Console.WriteLine($"ceu            : {totalNuvem} celulas (quarta classe: SO quem voa passa; z6/z12 derrubam)");
		Console.WriteLine($"duro           : {totalDuro} celulas (destroyable=0: bloqueia e NAO cede a soco nenhum)");
		Console.WriteLine($"celulas        : {celulas}");
		Console.WriteLine($"fontes no tileset: {fontes.Count}");
		if (semAtlas.Count > 0)
			Console.WriteLine($"SEM atlas ({semAtlas.Count}): {string.Join(", ", semAtlas.Take(8))}");
	}

	/// <summary>
	/// CONFERE O QUE ACABOU DE SAIR. Le o tileset gerado e checa que toda textura referenciada
	/// EXISTE no disco.
	///
	/// Existe por causa de um defeito que passou batido: os caminhos `res://` eram montados a
	/// partir do diretorio de trabalho, entao rodar o comando de dentro da pasta da ferramenta
	/// escrevia `res://../../Assets/...`. O jogo abria (o TileMapLayer so nao desenha o que nao
	/// acha) e o EDITOR e que quebrava, com uma enxurrada de "TileSetAtlasSource has no tile at
	/// (0,0)" -- o pior tipo de defeito, o que so aparece pra quem vai mexer no arquivo.
	/// </summary>
	private static void Conferir(string outDir, int esperadas)
	{
		string tileset = Path.Combine(outDir, "tileset.tres");
		if (!File.Exists(tileset)) return;

		string? raiz = AcharRaiz(outDir);
		int ok = 0;
		var quebradas = new List<string>();

		foreach (string linha in File.ReadAllLines(tileset))
		{
			int i = linha.IndexOf("path=\"res://", StringComparison.Ordinal);
			if (i < 0) continue;
			int ini = i + "path=\"res://".Length;
			int fim = linha.IndexOf('"', ini);
			if (fim < 0) continue;

			string rel = linha[ini..fim];
			string cheio = raiz != null ? Path.Combine(raiz, rel) : rel;
			if (File.Exists(cheio)) ok++;
			else quebradas.Add(rel);
		}

		if (quebradas.Count == 0)
		{
			Console.WriteLine($"tileset conferido: {ok}/{esperadas} texturas existem");
			return;
		}

		Console.WriteLine($"ERRO: {quebradas.Count} textura(s) do tileset NAO existem -- o editor");
		Console.WriteLine("      nao vai conseguir abrir as cenas dos planetas:");
		foreach (string q in quebradas.Take(5)) Console.WriteLine("        res://" + q);
	}

	/// <summary>
	/// Marca fisica e oclusao em todo quadro de todo typepath solido do jogo -- nao so nos que
	/// aparecem nos mapas.
	///
	/// So mexe em atlas que JA estao no tileset: registrar tambem os .dmi que nenhum mapa usa
	/// encheria o arquivo de centenas de fontes que ninguem pediu. Quem quiser um deles, o
	/// caminho e usar o sprite em algum mapa (ou pedir pra incluir a pasta inteira).
	/// </summary>
	private static int MarcarSolidos(Dictionary<string, TurfDef> turfs, Dictionary<string, Fonte> fontes,
		Dictionary<string, List<string>> atlasPorNome)
	{
		int n = 0;
		foreach (TurfDef td in turfs.Values)
		{
			if (td.Icon == null || (!td.Density && !td.Opacity)) continue;
			if (EhPorta(td.Path)) continue;                       // porta e passagem: ver EhPorta

			// PASSAGEM DENSA NAO E PAREDE, e a diferenca era fatal. `toeg` (a subida da torre do
			// Karin), `tohbtc` e `fromhbtc` tem `density = 1` no DM -- mas la o `Enter()` dispara
			// na TENTATIVA de entrar, e o teleporte acontece antes de o bloqueio valer.
			//
			// Aqui a densidade virava um bit de parede no `.col`, e o servidor recusava o passo:
			// o corpo nunca chegava a pisar na celula, e o gatilho que a leva pro Templo Sagrado
			// nunca rodava. Foi o que o dono viu -- "a porta pra subir na torre do karin n ta
			// funcionando". Era uma parede com desenho de escada. Ver `Passagens.Eh`.
			if (Passagens.Eh(td.Path)) continue;

			// O typepath pode nunca ter aparecido num .dmm (e ai nao tem `Atlas`), mas o atlas
			// DELE pode ja estar no tileset por causa de outro typepath. Resolve sem REGISTRAR
			// nada novo: o objetivo e marcar parede no que ja existe, nao inchar o arquivo com
			// folhas que ninguem pediu.
			Fonte? f = td.Atlas != null && fontes.TryGetValue(td.Atlas, out Fonte? direta)
				? direta
				: ResolverExistente(td.Icon, td.IconState, atlasPorNome, fontes);
			if (f == null) continue;

			// acha o estado pelo nome pra saber quantos quadros e direcoes ele ocupa
			if (!f.StateIndex.TryGetValue(td.IconState ?? "", out int idx)) continue;

			DmiState? st = null;
			int passo = 0;
			foreach (DmiState cand in f.States)
			{
				int tam = Math.Max(1, cand.Dirs) * Math.Max(1, cand.Frames);
				if (passo == idx) { st = cand; break; }
				passo += tam;
			}
			if (st == null) continue;

			int total = Math.Max(1, st.Dirs) * Math.Max(1, st.Frames);
			for (int i = 0; i < total; i++)
			{
				(int X, int Y) c = Indice(f, idx + i);
				if (td.Density && f.Densas.Add(c)) n++;
				if (td.Opacity) f.Opacas.Add(c);
			}
		}
		return n;
	}

	/// <summary>Acha uma fonte JA REGISTRADA por nome + estado, sem criar nenhuma nova.</summary>
	private static Fonte? ResolverExistente(string icone, string? estado,
		Dictionary<string, List<string>> atlasPorNome, Dictionary<string, Fonte> fontes)
	{
		string nome = Path.GetFileNameWithoutExtension(icone);
		if (!atlasPorNome.TryGetValue(nome, out List<string>? candidatos)) return null;

		Fonte? primeira = null;
		foreach (string cand in candidatos)
		{
			if (!fontes.TryGetValue(cand, out Fonte? f)) continue;
			primeira ??= f;
			if (f.StateIndex.ContainsKey(estado ?? "")) return f;
		}
		return primeira;
	}

	/// <summary>
	/// PORTA. No BYOND ela e um turf DENSO que abre no `Enter()` -- denso no papel e atravessavel
	/// na pratica, porque encostar nele ja o abre.
	///
	/// ATE AQUI ELA ERA UMA EXCECAO, e uma excecao espalhada por tres lugares: saia da colisao (ou
	/// seja, ficava aberta pra sempre), continuava cegando o campo de visao, e o desenho dela era o
	/// quadro errado. Agora ela SAI DO MAPA COMO ENTIDADE, na lista `.portas`: nasce fechada
	/// (bloqueia e cega, igual ao DM) e quem a abre e o servidor, em runtime.
	///
	/// Este predicado sobrou pra duas coisas: decidir o que vai pra lista, e manter a porta fora do
	/// `MarcarSolidos` -- fisica FIXA do tileset numa coisa que abre voltaria a lacrar a casa.
	/// </summary>
	private static bool EhPorta(string bp) =>
		bp.StartsWith("/turf/Door/", StringComparison.Ordinal)
		// A PORTA DE NAMEK tem o MESMO codigo de abrir (Enter + Open/Close + flick) e mora noutro
		// ramo da arvore -- entao ela nao casava aqui e virava PAREDE. Medido: 27 celulas em z02,
		// col=1 e vis=1. As casas de Namek estavam lacradas.
		|| bp.StartsWith("/turf/NamekBuildings/NamekDoor", StringComparison.Ordinal) ||
		bp.StartsWith("/turf/build/Door/", StringComparison.Ordinal);

	/// <summary>
	/// Typepath que vira TILE na cena. Turf e o chao/parede; obj e tudo que fica em cima dele.
	/// `/mob` fica de fora: NPC nao e cenario, e entidade -- entra pelo servidor, nao pelo mapa.
	/// </summary>
	private static bool Desenhavel(string bp) =>
		bp.StartsWith("/turf", StringComparison.Ordinal) ||
		bp.StartsWith("/obj", StringComparison.Ordinal);

	/// <summary>
	/// Sobe a partir de <paramref name="daqui"/> ate achar a pasta com `project.godot`.
	/// Nulo se nao houver nenhuma.
	/// </summary>
	private static string? AcharRaiz(string daqui)
	{
		var d = new DirectoryInfo(Path.GetFullPath(daqui));
		while (d != null)
		{
			if (File.Exists(Path.Combine(d.FullName, "project.godot"))) return d.FullName;
			d = d.Parent;
		}
		return null;
	}

	/// <summary>
	/// Acha (ou cria) a fonte de atlas de um `icon` + `icon_state`.
	///
	/// QUANDO HA MAIS DE UM ARQUIVO COM O MESMO NOME, ganha o que TEM O ESTADO PEDIDO. Se
	/// nenhum tiver, ganha a folha MAIOR -- folha grande costuma ser a de cenario, e a pequena
	/// e a variante que colidiu de nome.
	/// </summary>
	private static Fonte? Garantir(string icone, string? estado, string raiz,
		Dictionary<string, Fonte> fontes, Dictionary<string, List<string>> atlasPorNome,
		HashSet<string> semAtlas, ref int proxId)
	{
		string chave = Path.GetFileNameWithoutExtension(icone);
		if (!atlasPorNome.TryGetValue(chave, out List<string>? candidatos) || candidatos.Count == 0)
		{
			semAtlas.Add(icone);
			return null;
		}

		string png = candidatos[0];
		if (candidatos.Count > 1)
		{
			string? escolhido = null;
			foreach (string cand in candidatos)
			{
				DmiFile.Result? m = DmiFile.Read(cand);
				if (m == null) continue;
				foreach (DmiState st in m.States)
					if (string.Equals(st.Name, estado ?? "", StringComparison.OrdinalIgnoreCase))
					{ escolhido = cand; break; }
				if (escolhido != null) break;
			}
			png = escolhido ?? candidatos.OrderByDescending(c => new FileInfo(c).Length).First();
		}

		// A CHAVE DO CACHE E O CAMINHO, nao o nome do icone: dois `Namek.dmi` diferentes sao
		// duas fontes diferentes, e guardar por nome faria um sobrescrever o outro de novo.
		if (fontes.TryGetValue(png, out Fonte? existente)) return existente;

		// o .dmi original guarda os metadados; o .png convertido e copia crua dele
		DmiFile.Result? meta = DmiFile.Read(png);
		if (meta == null) { semAtlas.Add(icone); return null; }

		var f = new Fonte
		{
			Id = proxId++,
			Chave = png,
			ResPath = "res://" + Path.GetRelativePath(raiz, png).Replace('\\', '/'),
			IconW = meta.IconWidth,
			IconH = meta.IconHeight,
			Cols = Math.Max(1, meta.SheetWidth / Math.Max(1, meta.IconWidth)),
		};

		int idx = 0;
		foreach (DmiState st in meta.States)
		{
			f.StateIndex.TryAdd(st.Name, idx);
			idx += Math.Max(1, st.Dirs) * Math.Max(1, st.Frames);
		}
		f.States = meta.States;
		f.TotalQuadros = idx;

		fontes[png] = f;
		return f;
	}

	/// <summary>
	/// PLANEJA E COMPOE OS ATLAS COMPANHEIROS: uma tira por estado animado que nao cabia numa linha.
	///
	/// SO O QUADRO DA DIRECAO 0 interessa. O mapa pinta pelo NOME do estado, e o `Coord` sempre
	/// devolve o primeiro quadro (dir 0) -- as outras direcoes de um turf nunca sao usadas pelo
	/// .dmm. Reempacotar as quatro seria quadruplicar a imagem por nada.
	///
	/// A FISICA E A OCLUSAO VAO JUNTO. Um tile de agua que virou tira continua sendo o mesmo turf:
	/// se o original era denso ou opaco, a tira tem que ser tambem, senao a parede animada deixa de
	/// bloquear ao ser reempacotada -- um defeito que so apareceria no tile que anima.
	/// </summary>
	private static int Reempacotar(Dictionary<string, Fonte> fontes, string raiz, ref int proxId)
	{
		var trabalho = new Dictionary<string, (string, int, int, int, List<AtlasAnimado.Tira>)>();
		var novas = new List<Fonte>();
		int total = 0;

		foreach (Fonte f in fontes.Values.ToList())
		{
			var tiras = new List<AtlasAnimado.Tira>();
			int idx = 0;

			foreach (DmiState st in f.States)
			{
				int dirs = Math.Max(1, st.Dirs);
				int quadros = Math.Max(1, st.Frames);
				(int X, int Y) baseC = Indice(f, idx);

				// o MESMO teste do DeclararTiles: se cabe na linha, o caminho antigo ja resolve
				bool cabe = baseC.X + (quadros - 1) * dirs < f.Cols
							&& idx + (quadros - 1) * dirs < f.TotalQuadros;

				if (quadros > 1 && !cabe && st.Name != null)
				{
					var q = new int[quadros];
					for (int k = 0; k < quadros; k++) q[k] = idx + k * dirs;

					var dur = new double[quadros];
					for (int k = 0; k < quadros; k++)
					{
						double d10 = k < st.Delays.Length ? st.Delays[k] : 1;
						dur[k] = Math.Max(d10, 0.1) / 10.0;   // decimos do BYOND -> segundos
					}

					f.Refeitos[st.Name] = tiras.Count;
					f.Duracoes.Add(dur);
					tiras.Add(new AtlasAnimado.Tira { Origem = f.Chave, Quadros = q, Linha = tiras.Count });
					total++;
				}

				idx += dirs * quadros;
			}

			if (tiras.Count == 0) continue;

			string destino = Path.ChangeExtension(f.Chave, null) + AtlasAnimado.Sufixo + ".png";
			int largura = tiras.Max(t => t.Quadros.Length);

			var comp = new Fonte
			{
				Id = proxId++,
				Chave = destino,
				ResPath = "res://" + Path.GetRelativePath(raiz, destino).Replace('\\', '/'),
				IconW = f.IconW,
				IconH = f.IconH,
				Cols = largura,
				TotalQuadros = largura * tiras.Count,
			};

			// a fisica/oclusao do original vale pra tira: e o mesmo turf desenhado noutro lugar
			foreach ((string estado, int linha) in f.Refeitos)
			{
				(int X, int Y) orig = Coord(f, estado);
				if (f.Densas.Contains(orig)) comp.Densas.Add((0, linha));
				if (f.Opacas.Contains(orig)) comp.Opacas.Add((0, linha));
			}

			f.Companheira = comp;
			comp.Duracoes = f.Duracoes;
			novas.Add(comp);
			trabalho[f.Chave] = (destino, f.IconW, f.IconH, f.Cols, tiras);
		}

		if (total == 0) return 0;

		AtlasAnimado.Compor(trabalho, raiz, Environment.GetEnvironmentVariable("GODOT") is { Length: > 0 } g && File.Exists(g) ? g : null);

		// SO ENTRA NO TILESET O QUE FOI ESCRITO DE VERDADE. Se o Godot nao rodou, a fonte
		// companheira apontaria pra um arquivo inexistente e o tileset inteiro falharia ao carregar
		// -- pior que ficar sem a animacao.
		int entraram = 0;
		foreach (Fonte comp in novas)
		{
			if (!File.Exists(comp.Chave)) continue;
			fontes[comp.Chave] = comp;
			entraram++;
		}

		if (entraram == novas.Count) return total;

		// nao deu: desfaz os apontamentos pra ninguem ficar apontando pro vazio
		foreach (Fonte f in fontes.Values)
			if (f.Companheira != null && !File.Exists(f.Companheira.Chave))
			{
				f.Companheira = null;
				f.Refeitos.Clear();
			}
		return 0;
	}

	private static (int X, int Y) Coord(Fonte f, string? state)
	{
		int idx = 0;
		if (state != null) f.StateIndex.TryGetValue(state, out idx);
		return (idx % f.Cols, idx / f.Cols);
	}

	/// <summary>Quantos quadros o .dmi declara pra este estado. 0 = o estado nao existe na folha.</summary>
	private static int Quadros(Fonte f, string estado)
	{
		foreach (DmiState st in f.States)
			if (st.Name == estado) return Math.Max(1, st.Frames);
		return 0;
	}

	/// <summary>
	/// Os quadros deste estado cabem todos na MESMA LINHA do atlas?
	///
	/// E a condicao pra o `DeclararTiles` conseguir declarar o tile animado no atlas ORIGINAL --
	/// com `animation_columns = 0` o Godot le os quadros correndo pra direita a partir da coordenada
	/// base, e virar a linha nao existe. Mesma conta do <see cref="Reempacotar"/>, escrita uma vez.
	/// </summary>
	private static bool CabeNaLinha(Fonte f, string estado)
	{
		int idx = 0;
		foreach (DmiState st in f.States)
		{
			int dirs = Math.Max(1, st.Dirs), quadros = Math.Max(1, st.Frames);
			if (st.Name == estado)
			{
				(int X, int Y) b = Indice(f, idx);
				return b.X + (quadros - 1) * dirs < f.Cols
					   && idx + (quadros - 1) * dirs < f.TotalQuadros;
			}
			idx += dirs * quadros;
		}
		return true;   // estado que nao existe: o relatorio de "quadro 0" ja cobre esse caso
	}

	/// <summary>Le a ordem dos .dmm no .dme: e ela que define o z real de cada mapa.</summary>
	/// <summary>
	/// A ordem em que o `.dme` inclui os `.dmm` -- e ela DECIDE o z de cada nivel (ver a chamada).
	///
	/// `internal` e nao `private` porque a bancada `cidade` precisa da MESMA ordem pra achar, no
	/// `.dmm`, a celula que ela esta julgando no `.col`. Uma segunda copia desta leitura acertaria
	/// hoje e erraria no dia em que alguem acrescentasse um mapa no meio da lista -- e o sintoma
	/// seria a bancada julgando Arconia com as celulas do Inferno, calada.
	/// </summary>
	internal static List<string> OrdemDoDme(string dmmDir)
	{
		var ordem = new List<string>();
		string? raiz = Directory.GetParent(dmmDir)?.FullName;
		string? dme = raiz == null ? null : Directory.GetFiles(raiz, "*.dme").FirstOrDefault();

		if (dme != null)
			foreach (string linha in File.ReadAllLines(dme))
			{
				if (!linha.TrimStart().StartsWith("#include", StringComparison.Ordinal)) continue;
				if (!linha.Contains(".dmm", StringComparison.OrdinalIgnoreCase)) continue;
				string arq = Path.GetFileName(linha.Trim().Trim('"').Replace('\\', '/'));
				string cheio = Path.Combine(dmmDir, arq);
				if (File.Exists(cheio)) ordem.Add(cheio);
			}

		// .dme ausente/ilegivel: cai na ordem alfabetica e avisa (o z pode sair trocado)
		if (ordem.Count == 0)
		{
			Console.WriteLine("AVISO: nao achei a ordem dos mapas no .dme; usando ordem alfabetica");
			ordem.AddRange(Directory.GetFiles(dmmDir, "*.dmm").OrderBy(s => s));
		}
		return ordem;
	}

	/// <summary>O que a raiz da cena precisa declarar sobre si.</summary>
	private readonly record struct FichaDeCena(string Nome, double Gravidade, int Tipo);

	/// <summary>
	/// A ficha do planeta a partir do nome do ANDAR (`z03_Vegeta` -> `Vegeta`).
	///
	/// Os nomes de cena vem numerados por causa do `z` do BYOND; a tabela de gravidade e
	/// indexada pelo nome limpo. Casar os dois aqui evita que a numeracao vaze pro resto do jogo.
	/// </summary>
	private static FichaDeCena FichaDaZona(string nomeDaCena)
	{
		string limpo = nomeDaCena;
		int corte = limpo.IndexOf('_');
		if (corte > 0 && limpo.StartsWith('z')) limpo = limpo[(corte + 1)..];
		limpo = limpo.Replace('_', ' ');

		Jandirus.Core.World.FichaDePlaneta f = Planetas.De(limpo);
		return new FichaDeCena(limpo, f.Gravidade, IndiceDoTipo(f.Tipo));
	}

	/// <summary>
	/// O `TipoDePlaneta` e um enum do CLIENTE e o .tscn guarda enum como INTEIRO. A ordem tem
	/// que bater com a declaracao em `Client/Planeta.cs` -- se alguem reordenar la, Namek vira
	/// deserto e nada acusa. Por isso a lista esta escrita aqui inteira, e nao derivada.
	/// </summary>
	private static int IndiceDoTipo(string tipo) => tipo switch
	{
		"Jardim" => 1,
		"Deserto" => 2,
		"Gelado" => 3,
		"Vulcanico" => 4,
		"Morto" => 5,
		_ => 0,   // Rochoso
	};

	/// <summary>As fichas extraidas do DM. Quem preenche e o Program, antes de converter.</summary>
	private static Jandirus.Core.World.CatalogoDePlanetas Planetas =
		Jandirus.Core.World.CatalogoDePlanetas.Parse("[]");

	public static void UsarPlanetas(Jandirus.Core.World.CatalogoDePlanetas c) => Planetas = c;

	/// <summary>
	/// O CATALOGO DE CONSTRUCOES, pra reconhecer maquina no meio do mapa.
	///
	/// Vazio quando o `construcoes.json` nao existe -- e ai nada e extraido e tudo continua virando
	/// tile, que e o comportamento antigo. Um pipeline pela metade nao pode APAGAR objeto do mapa.
	/// </summary>
	private static Jandirus.Core.Tech.CatalogoDeObras Obras =
		Jandirus.Core.Tech.CatalogoDeObras.Parse("[]");

	public static void UsarObras(Jandirus.Core.Tech.CatalogoDeObras c) => Obras = c;

	/// <summary>
	/// ONDE FICA UMA COORDENADA BYOND, no mundo convertido.
	///
	/// Preenchido antes da segunda passada, porque uma passagem da Terra aponta pra um z que so vai
	/// ser convertido depois -- resolver na hora exigiria que a ordem dos mapas casasse com a ordem
	/// dos destinos, o que nao acontece nem por acaso.
	/// </summary>
	private static Func<int, int, int, (string Zona, float Px, float Py)?>? Destinos;

	private static string NomeDoAndar(DmmMap.Result dados, DmmLevel nivel, int offset)
	{
		// a AREA dominante nomeia o andar: e o nome que o jogo ja usa pro lugar
		var contagem = new Dictionary<string, int>(StringComparer.Ordinal);
		for (int x = 0; x < nivel.Width; x++)
			for (int y = 0; y < nivel.Height; y++)
			{
				string? k = nivel.Cells[x, y];
				if (k == null || !dados.Keys.TryGetValue(k, out string[]? tipos)) continue;
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (!bp.StartsWith("/area", StringComparison.Ordinal)) continue;
					contagem[bp] = contagem.GetValueOrDefault(bp) + 1;
				}
			}

		string dominante = contagem.Count > 0
			? contagem.OrderByDescending(kv => kv.Value).First().Key
			: "/area/Unknown";

		string curto = dominante[(dominante.LastIndexOf('/') + 1)..];
		var sb = new StringBuilder();
		foreach (char c in curto) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
		string nome = sb.Length > 0 ? sb.ToString() : "Area";

		// AREA GENERICA CEDE LUGAR AO NOME DO MUNDO. Nove andares tem `/area/Outside` como area
		// dominante, e o catalogo guarda um por nome -- o Templo e as duas cavernas nunca chegavam
		// a existir. Ver `Passagens.NomeDoZ`.
		int z = nivel.Z + offset;
		if (Passagens.NomeGenerico(nome) && Passagens.NomeDoZ(z) is { } canonico) nome = canonico;

		return $"z{z:00}_{nome}";
	}

	// ---------------------------------------------------------------------
	// .tres do TileSet
	// ---------------------------------------------------------------------
	/// <summary>
	/// O TileSet.
	///
	/// DECLARA TODO QUADRO DE TODO ATLAS, nao so os que o .dmm usou. A versao anterior so
	/// declarava as celulas que apareciam nos mapas -- 464 tiles de uns 20 mil -- e o efeito
	/// pratico era que no editor a esmagadora maioria dos sprites simplesmente NAO EXISTIA pra
	/// pintar. O tileset e a PALETA; ele tem que ter tudo que o .dmi tem.
	///
	/// E TRAZ AS ANIMACOES. Um estado de .dmi com varios quadros e um `delay` vira um tile
	/// ANIMADO do Godot -- agua correndo, porta abrindo, fogo. Antes cada quadro virava um
	/// tile parado e a agua ficava congelada no primeiro.
	/// </summary>
	private static void EscreverTileSet(string caminho, Dictionary<string, Fonte> fontes)
	{
		var ext = new StringBuilder();
		var sub = new StringBuilder();
		var res = new StringBuilder();
		int passos = 1;
		int totalTiles = 0, totalAnimados = 0;

		foreach (Fonte f in fontes.Values.OrderBy(v => v.Id))
		{
			string extId = $"{f.Id}_atlas";
			ext.Append($"[ext_resource type=\"Texture2D\" path=\"{f.ResPath}\" id=\"{extId}\"]\n");
			passos++;

			sub.Append($"[sub_resource type=\"TileSetAtlasSource\" id=\"Atlas_{f.Id}\"]\n");
			sub.Append($"texture = ExtResource(\"{extId}\")\n");
			sub.Append($"texture_region_size = Vector2i({f.IconW}, {f.IconH})\n");

			(int Tiles, int Animados) conta = DeclararTiles(sub, f);
			totalTiles += conta.Tiles;
			totalAnimados += conta.Animados;

			sub.Append('\n');
			passos++;

			res.Append($"sources/{f.Id} = SubResource(\"Atlas_{f.Id}\")\n");
		}

		Console.WriteLine($"tiles no tileset : {totalTiles} ({totalAnimados} animados)");

		var sb = new StringBuilder();
		sb.Append($"[gd_resource type=\"TileSet\" load_steps={passos} format=3]\n\n");
		sb.Append(ext).Append('\n');
		sb.Append(sub);
		sb.Append("[resource]\n");
		sb.Append($"tile_size = Vector2i({Cell}, {Cell})\n");
		sb.Append("physics_layer_0/collision_layer = 1\n");
		sb.Append("occlusion_layer_0/light_mask = 1\n");
		sb.Append(res);

		File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(false));
	}

	// ---------------------------------------------------------------------
	// .tscn do andar
	// ---------------------------------------------------------------------
	private static int EscreverCena(string caminho, string nome, DmmLevel nivel, DmmMap.Result dados,
		Dictionary<string, TurfDef> turfs, Dictionary<string, Fonte> fontes,
		Dictionary<string, List<string>> atlasPorNome, HashSet<string> semAtlas, ref int proxId,
		out HashSet<(int, int)> paredes, out Dictionary<(int, int), byte> cegos,
		out List<string> portasDaCena, out List<string> maquinasDaCena,
		out List<string> passagensDaCena)
	{
		var passagens = new List<string>();
		int semDestino = 0;

		// local de verdade: um `out` nao pode ser capturado por funcao local
		var muros = new HashSet<(int, int)>();

		// O QUE CEGA e diferente do que BLOQUEIA, e por isso sao dois mapas.
		//
		// O `opacity` do BYOND nao serve aqui: este jogo praticamente nao usa (93 ocorrencias no
		// codigo inteiro, quase todas `mouse_opacity`), ou seja, no original dava pra ver o
		// planeta todo atraves das casas. Quem define a regra e o dono: "sombra de tiles como
		// paredes e arvores". Entao CEGA TUDO QUE E DENSO -- turf ou obj, casa, muro, porta,
		// arvore, cerca, pedra.
		//
		// Continua sendo um mapa SEPARADO do `.col` porque os dois divergem nos dois sentidos: a
		// porta cega e nao bloqueia (da pra atravessar), e a borda do mundo bloqueia sem cegar
		// nada de interessante.
		// A IDENTIDADE VAI JUNTO, e nao so a posicao. O `f.Id` + a coordenada de atlas ja sao
		// calculados tres linhas antes de a celula ser marcada como cega -- ate agora esse dado
		// era simplesmente jogado fora, porque o acumulador era um HashSet de coordenadas.
		//
		// A PALETA E POR MAPA e nasce da ordem de descoberta. So a IGUALDADE importa (ver
		// `ZoneCollision.Grupo`), entao numero pequeno basta: sao 186 identidades distintas no
		// jogo INTEIRO e no maximo 60 num mapa so.
		var vendados = new Dictionary<(int, int), byte>();
		var paleta = new Dictionary<(int, int, int), byte>();

		byte GrupoDe(int fonte, int ax, int ay)
		{
			var chave = (fonte, ax, ay);
			if (paleta.TryGetValue(chave, out byte g)) return g;
			// 1..254: o 0 e a borda do mundo e o 255 e "nao sei". Estourando (nunca visto), tudo
			// que passar cai num grupo so -- pior que o ideal, e ainda assim melhor que juntar
			// com o vizinho errado.
			g = (byte)Math.Min(paleta.Count + 1, 254);
			paleta[chave] = g;
			return g;
		}
		// ============================ AS CELULAS NAO VAO MAIS PRA DENTRO DA CENA ============================
		// Ate aqui cada camada saia como um `tile_map_data` no `.tscn`: 9,6 MB de texto so na Terra,
		// 659 ms de parse, e -- o pior -- 708 ms no PRIMEIRO QUADRO, porque o TileMapLayer monta o
		// desenho das 266 mil celulas de uma vez quando o renderizador as pede. Era a travada de
		// tres segundos ao trocar de mapa.
		//
		// Agora elas saem num `.pedacos` ao lado, agrupadas em blocos de 64x64, e o cliente monta
		// so os que a camera alcanca (ver `Core.World.PedacosDoMapa` e `Client.PintorDePedacos`).
		// Enquanto o dado estivesse na cena isso seria impossivel: o Godot o monta inteiro antes de
		// alguem poder escolher.
		// ====================================================================================================
		var bytes = new List<Jandirus.Core.World.CelulaDePedaco>();
		var decoracao = new List<Jandirus.Core.World.CelulaDePedaco>();
		var objetos = new List<Jandirus.Core.World.CelulaDePedaco>();

		// AS MAQUINAS DO MAPA saem da cena e viram uma lista ao lado -- mesmo caminho da porta.
		var interativos = new List<string>();

		// AS PORTAS SAEM DO MAPA COMO LISTA. Ate agora a porta era uma EXCECAO espalhada: o
		// `EhPorta` a tirava da colisao (aberta pra sempre), ela continuava cegando, e o guarda
		// do campo de visao apagava a tela em cima dela. Como entidade, ela nasce FECHADA
		// (bloqueia e cega, como no DM) e o estado passa a ser dela -- nenhum dos tres lugares
		// precisa mais saber que porta existe.
		var portas = new List<string>();

		int usadas = 0, comObj = 0, objSemArte = 0;

		/// <summary>Maquina que o catalogo reconhece mas que nao conseguiu virar desenho.</summary>
		var maquinaSemArte = new Dictionary<string, int>(StringComparer.Ordinal);

		/// <summary>Celulas que ALGUEM desenha: tile de qualquer camada, porta ou maquina.</summary>
		var desenhadas = new HashSet<(int, int)>();

		// ============================ QUAL "VAZIO" E BORDA E QUAL E COSTURA DO MAPEADOR ============================
		// `/turf/Other/Blank` e denso e sem icone, e a regra ate aqui era simples demais: TODO Blank
		// vira parede, porque "denso e sem icone e geometria deliberada". Isso e verdade no ANEL que
		// cerca o retangulo e no VAZIO em volta do Lookout -- e e falso no meio de um oceano.
		//
		// O dono achou o caso pelo lado de dentro: "tem uma PAREDE INVISIVEL no meio do mapa". Medido,
		// z11 (Hera) tem uma COLUNA INTEIRA de Blank em x=250 do `.dmm`, de y=1 a y=500, com
		// `/turf/Water/Water3` nas duas colunas vizinhas. E uma costura que o mapeador deixou ao
		// emendar dois pedacos de agua aberta -- 500 celulas solidas cortando o mapa ao meio, e sobre
		// agua azul lisa ela e literalmente invisivel.
		//
		// A DIFERENCA E TOPOLOGICA, e nao de posicao: o VAZIO tem MIOLO e a COSTURA nao. Uma regiao de
		// Blank que e limite de mundo sempre contem alguma celula cujos quatro vizinhos tambem sao
		// Blank (o anel tem espessura, o vazio do Lookout tem 11.536 celulas de miolo); uma linha de
		// um tile de largura nao tem nenhuma. Testar isso separa os quarenta mapas sem uma unica
		// excecao escrita a mao:
		//
		//     z11 Hera        500 celulas, miolo 0  -> costura   (a queixa do dono)
		//     z13 Sala        500 celulas, miolo 0  -> costura   (o anel da coluna 499)
		//     z05 Arconia       5 celulas, miolo 0  -> costura   (celulas soltas)
		//     z10 Heaven        3 celulas, miolo 0  -> costura
		//     z05 Arconia     313 celulas, miolo 83 -> BORDA     (a mordida no leste do mapa)
		//     z12 Lookout   14.315 celulas, miolo 11.536 -> BORDA (o ceu em volta da plataforma)
		//     z15..z18/z22/z23  mapa inteiro        -> BORDA     (as cavernas e o `Outside`)
		//
		// NAO DA PRA CONSERTAR ISSO DEIXANDO DE BLOQUEAR TUDO que e denso e invisivel: seria devolver
		// o bug que este trecho conserta, com quase dois milhoes de celulas de borda de mundo abertas.
		// ==========================================================================================
		var mudas = new HashSet<(int, int)>();
		for (int y = 0; y < nivel.Height; y++)
			for (int x = 0; x < nivel.Width; x++)
			{
				string? k = nivel.Cells[x, y];
				if (k == null || !dados.Keys.TryGetValue(k, out string[]? tipos)) continue;
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (turfs.TryGetValue(bp, out TurfDef? td) && td.Icon == null && td.Density)
					{ mudas.Add((x, y)); break; }
				}
			}

		var costuras = new HashSet<(int, int)>();
		if (mudas.Count > 0)
		{
			var visto = new HashSet<(int, int)>();
			var fila = new Queue<(int, int)>();
			var comp = new List<(int, int)>();
			foreach ((int, int) semente in mudas)
			{
				if (!visto.Add(semente)) continue;
				comp.Clear();
				fila.Enqueue(semente);
				bool temMiolo = false;
				while (fila.Count > 0)
				{
					(int cx, int cy) = fila.Dequeue();
					comp.Add((cx, cy));
					int vizinhos = 0;
					foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
					{
						(int, int) n = (cx + dx, cy + dy);
						if (!mudas.Contains(n)) continue;
						vizinhos++;
						if (visto.Add(n)) fila.Enqueue(n);
					}
					if (vizinhos == 4) temMiolo = true;
				}
				if (!temMiolo) foreach ((int, int) c in comp) costuras.Add(c);
			}
		}

		// QUANTAS CELULAS APONTAM PRA UMA TIRA. Sem este numero, "178 estados reempacotados" parece
		// prova de que a animacao chegou ao mapa -- e nao e: o reempacotamento escreve o atlas e
		// declara o tile, mas quem faz a animacao APARECER e a celula apontar pra la. Foram duas
		// coisas separadas quebrando em silencio (o PNG sem `.import` e o estado padrao com guarda
		// de nulo), e nenhuma das duas aparecia em contador nenhum.
		int repontadas = 0, repontadasPadrao = 0;
		var semEstado = new Dictionary<string, int>(StringComparer.Ordinal);

		/// <summary>Celulas cujo estado TEM mais de um quadro no .dmi mas foi pintado parado.</summary>
		var semAnimacao = new Dictionary<string, int>(StringComparer.Ordinal);
		var luzes = new List<LuzDeTile>();

		// Poe UM typepath numa camada. Devolve se conseguiu desenhar.
		//
		// `cega` diz se ESTA camada corta a linha de visao. Ver o comentario da chamada dos
		// objetos: cenario solto (arvore, pedra, poste) PARA, mas nao ESCONDE.
		bool Por(string? bp, List<Jandirus.Core.World.CelulaDePedaco> destino, int x, int y, bool cega = true)
		{
			if (bp == null) return false;
			if (!turfs.TryGetValue(bp, out TurfDef? td)) return false;

			// BORDA DO MUNDO. `/turf/Other/Blank` e denso, opaco e NAO TEM ICONE -- e o limite
			// do mapa, invisivel de proposito. A regra "so bloqueia o que da pra ver" (certa
			// pra arvore sem sprite) tirava a parede do vazio: quase 2 MILHOES de celulas, e
			// andares inteiros ficaram sem borda. Denso E SEM ICONE e geometria deliberada, nao
			// arte que faltou.
			// ...MENOS quando este vazio e uma COSTURA e nao o limite do mundo. Ver o levantamento
			// de topologia la em cima: e a parede invisivel que o dono atravessou o mapa pra achar.
			if (td.Icon == null)
			{
				if (td.Density && !costuras.Contains((x, y)))
				{
					muros.Add((x, y));
					if (cega) vendados[(x, y)] = Jandirus.Core.World.ZoneCollision.BordaDoMundo;
				}
				return false;
			}
			if (td.Atlas == null || !fontes.TryGetValue(td.Atlas, out Fonte? f)) return false;

			// ============================ NULO E O ESTADO PADRAO, NAO "SEM ESTADO" ============================
			// O `IconState` chega NULO quando o DM nunca declarou `icon_state` -- e no BYOND isso quer
			// dizer o estado de nome VAZIO da folha, que existe e tem nome `""`. Sao coisas diferentes
			// no C# e a MESMA coisa no jogo.
			//
			// A confusao entre as duas custou TODA animacao cujo estado animado e o padrao. O
			// repontamento pra tira reempacotada tinha uma guarda `estado != null`, entao a celula
			// continuava apontando pro atlas ORIGINAL (quadro parado) enquanto a tira animada ficava no
			// tileset sem ninguem usando. Foi o que o dono viu: "a research table ainda ta sem animaçao
			// nenhuma e so alguns icones q estao" -- a ResearchBench tem 5 quadros no estado padrao.
			//
			// Normalizar aqui, uma vez, e o que faz o resto do metodo parar de ter que lembrar disso.
			// ================================================================================================
			string estado = EstadoDaCelula(td, x, y, nivel.Height) ?? "";

			// A PORTA FECHADA E O ESTADO "Closed". O DM so o escreve dentro do `New()`, e o
			// DmTurfScanner ignora corpo de proc de proposito -- entao `IconState` vinha nulo e o
			// `Coord` caia no indice 0, que e o estado VAZIO da folha (um desenho diferente).
			// Como o indice 0 e o fallback silencioso, nem o relatorio de "estado que faltou"
			// pegava este caso.
			if (EhPorta(bp) && estado.Length == 0) estado = "Closed";
			if (!f.StateIndex.ContainsKey(estado))
			{
				string chave = $"{bp} -> {td.Icon} estado \"{estado}\"";
				semEstado[chave] = semEstado.GetValueOrDefault(chave) + 1;
			}

			(int X, int Y) c = Coord(f, estado);

			// ESTADO REEMPACOTADO MORA NOUTRA FONTE. A celula tem que apontar pra tira, senao ela
			// continua pintando o quadro parado do atlas original enquanto o tile animado que o
			// tileset declarou fica sem ninguem usando.
			Fonte fUsada = f;
			bool naTira = f.Companheira != null && f.Refeitos.TryGetValue(estado, out int linha);
			if (naTira)
			{
				fUsada = f.Companheira!;
				c = (0, f.Refeitos[estado]);
				repontadas++;
				// as do estado PADRAO sao exatamente as que a guarda de nulo comia
				if (estado.Length == 0) repontadasPadrao++;
			}

			// ============================ ANIMACAO PROMETIDA E NAO ENTREGUE ============================
			// O .dmi diz quantos quadros o estado tem. Se ele tem mais de um e a celula NAO acabou numa
			// tira, ela depende de o atlas original ter conseguido declarar o tile animado -- e isso so
			// acontece quando os quadros cabem na MESMA LINHA (ver `Reempacotar`). Fora disso, a celula
			// e pintada parada e ninguem avisa.
			//
			// Este relatorio existe porque "178 estados reempacotados" nao prova nada sobre o que aparece
			// na tela: ja quebrou por PNG sem `.import` e por guarda de nulo no estado padrao, e nenhuma
			// das duas aparecia em contador nenhum. Aqui a pergunta e a que importa -- ESTA CELULA ANIMA?
			// =========================================================================================
			if (!naTira && Quadros(f, estado) is > 1 and var nq && !CabeNaLinha(f, estado))
			{
				string k = $"{bp} -> {td.Icon} \"{estado}\" ({nq} quadros)";
				semAnimacao[k] = semAnimacao.GetValueOrDefault(k) + 1;
			}

			// A PORTA NAO E PINTADA NO TILEMAP -- ela vira um NODE, e o node desenha os quatro
			// estados dela.
			//
			// POR QUE NAO DA PRA DESENHAR O NODE POR CIMA DO TILE: o quadro "Open" do BYOND e um VAO,
			// quase todo transparente (medido na Door1: 64 pixels opacos de 1024). Com o tile fechado
			// embaixo, abrir a porta mostraria a porta fechada atraves do buraco.
			//
			// E POR QUE NAO APAGAR A CELULA EM RUNTIME: a cena da zona fica CACHEADA entre visitas
			// (ver `World.GuardarZonaAtual`). Apagar e repor celula nela e mutar um objeto que
			// sobrevive a saida do planeta -- sair com a porta aberta deixaria o buraco pra sempre.
			//
			// O que fica embaixo e o mesmo que ficava no BYOND: o turf de chao, se o prefab tinha um,
			// e o vazio se nao tinha (la o turf E a camada de baixo, entao porta aberta ja mostrava
			// o escuro).
			bool porta = EhPorta(bp);

			// ============================ MAQUINA NAO E CENARIO ============================
			// Banco, bancada de pesquisa, sala de gravidade, regenerador, os laboratorios: no
			// original todos tem VERBOS (`set src in oview(1)`), ou seja, sao coisas com que se
			// INTERAGE. Aqui eles viravam celula de tilemap -- pintura no chao, sem estado e sem
			// resposta -- ao lado de uma copia construida por jogador, que e um node e funciona.
			// Duas coisas iguais na tela, uma viva e outra nao.
			//
			// Agora saem do tilemap pelo MESMO caminho da porta: uma lista ao lado da cena, e o
			// servidor as registra como construcoes do mapa (ver `.objetos` e
			// `GameServer.CarregarObjetosDoMapa`). O que as reconhece e o `create_type` do
			// catalogo de tecnologia -- a mesma chave que ja diz qual arte e qual densidade cada
			// uma tem, entao nao ha tabela nova pra manter em sincronia.
			//
			// A COLISAO CONTINUA NO `.col`, como a da porta: a maquina nao anda, e o bit ja esta
			// calculado. O que muda e so quem DESENHA.
			Jandirus.Core.Tech.Construcao? maquina = porta ? null : Obras.PorTypepath(bp);

			if (porta)
			{
				// o .tres da folha e o mesmo caminho do .png, com outra extensao. Sai o da fonte
				// ORIGINAL de proposito: o companheiro de animacao (ver AtlasAnimado) so existe pro
				// tileset, e o node quer o SpriteFrames com os estados nomeados.
				string tres = Path.ChangeExtension(f.ResPath, ".tres");
				portas.Add($"{{ \"x\": {x}, \"y\": {y}, \"arte\": \"{tres}\" }}");
			}
			else if (maquina != null)
			{
				interativos.Add($"{{ \"x\": {x}, \"y\": {y}, \"id\": \"{maquina.Id}\" }}");
			}
			else
			{
				// A ALTERNATIVA DO TILE NAO E GRAVADA porque ela sempre foi 0 aqui -- o formato do
				// `.pedacos` a deixa de fora e economiza dois bytes por celula.
				destino.Add(new Jandirus.Core.World.CelulaDePedaco(
					(short)x, (short)y, (ushort)fUsada.Id, (ushort)c.X, (ushort)c.Y));
				usadas++;
			}

			// ESTA CELULA TEM DONO NA TELA -- tile, porta ou maquina. E o outro lado da conta do
			// relatorio de parede fantasma la embaixo: solido sem ninguem que o desenhe e defeito.
			desenhadas.Add((x, y));

			// SO BLOQUEIA O QUE FOI DESENHADO -- e a porta e a excecao declarada, senao a casa
			// fica lacrada com um desenho de porta na frente.
			// ============================ BARREIRA BLOQUEIA SEM SER DENSA ============================
			// `/obj/barrier/*` para o corpo no BYOND por `NOENTER`/`selectivecollide`, e o comentario do
			// proprio jogo diz isso com todas as letras: "Barriers block via selectivecollide/NOENTER
			// (NOT native density)" (barrier.dm:61-62). O pipeline le so `density`, entao as cercas e
			// bordas do original viraram cenario atravessavel.
			//
			// Medido: 8.765 celulas nos 4 mapas -- 1.683 so na Terra. E o maior buraco de colisao do
			// port, e nao aparecia em lugar nenhum porque cada uma parece uma cerca decorativa.
			//
			// APROXIMACAO DECLARADA: o `NOENTER` do original e POR DIRECAO (`list(SOUTH, SOUTHWEST...)`)
			// e o `.col` e 1 bit por celula, sem direcao. Bloquear inteiro erra pro lado seguro -- uma
			// borda de mapa que so barrava por um lado agora barra pelos dois. O `kaio_gate` fica de
			// fora: ele e condicional (`kaiTrainingAllowed`) e virar parede fixa trancaria o treino.
			bool barreira = bp.StartsWith("/obj/barrier/", StringComparison.Ordinal)
							&& !bp.Contains("kaio_gate", StringComparison.OrdinalIgnoreCase);

			// A PORTA BLOQUEIA E CEGA, como no original: `density = 1` e `opacity = 1` enquanto
			// fechada (Doors.dm:56-65). Quem a abre e o servidor, em runtime -- ver GameServer.Portas.
			// A PASSAGEM SAI DA COLISAO pelo mesmo motivo da porta: no DM ela e densa, mas o
			// `Enter()` teleporta ANTES de o bloqueio valer. Marcada como parede, ela vira uma
			// escada que ninguem sobe. Ver `MarcarSolidos`.
			if ((td.Density || barreira) && !Passagens.Eh(bp)) muros.Add((x, y));

			// ...e a porta CEGA mesmo sem bloquear: ela esta fechada no desenho.
			// ============================ O QUE CEGA NAO E O QUE BLOQUEIA ============================
			// O dono fotografou uma PEDRA projetando leque preto de muro. A pedra e
			// `/turf/decor/LargeRock` (`Turfs.dm:1662`): densa -- voce nao passa por cima -- e
			// obviamente nao esconde nada, porque ela bate na canela.
			//
			// TENTEI `opacity` PRIMEIRO, e estava errado. O BYOND tem o campo certo (`opacity = 1`
			// e literalmente "bloqueia visao") e o `DmTurfScanner` ja o extraia. So que ESTE codigo
			// nao o mantem: sao 397 declaracoes de `density` contra 45 de `opacity` no jogo inteiro.
			// Trocar um pelo outro derrubou as celulas que cegam de 5.956 pra 174 -- ou seja, teria
			// deixado os MUROS transparentes. O campo certo existe e o dado nao esta la.
			//
			// O que o dado TEM e a arvore de tipos, e nela `turf/decor` e exatamente a familia do
			// cenario solto (Rock, LargeRock, firewood). E a MESMA regra que ja vale pros `/obj`
			// oito linhas abaixo -- "cenario solto PARA, mas nao ESCONDE" --, agora aplicada a um
			// ramo de turf que tinha escapado dela.
			// =====================================================================================
			bool decor = bp.StartsWith("/turf/decor", StringComparison.Ordinal);
			if (td.Density && cega && !decor) vendados[(x, y)] = GrupoDe(f.Id, c.X, c.Y);

			// FONTE DE LUZ. Fogueira, tocha, lampada e lava acendem o cenario -- ver LightCatalog.
			if (LightCatalog.Da(bp) is { } luz)
				luzes.Add(new LuzDeTile(x, y, luz.Raio, luz.Cor, luz.Forca, luz.Tremula));

			return true;
		}
		for (int y = 0; y < nivel.Height; y++)
			for (int x = 0; x < nivel.Width; x++)
			{
				string? k = nivel.Cells[x, y];
				if (k == null || !dados.Keys.TryGetValue(k, out string[]? tipos)) continue;

				// O ULTIMO TURF VENCE. No DM cada `new /turf/X(loc)` de um prefab SUBSTITUI o
				// anterior -- quem se ve e o do FIM da lista, nao o do comeco. Pegar o primeiro
				// jogava fora tudo que estava POR CIMA do chao: a porta da casa, o litoral
				// curvo, as plantas, as cadeiras, as pedras, as mesas. So na Terra sao 575
				// turfs em 572 celulas, e e metade da queixa "falta coisa no mapa".
				string? fundo = null, topo = null, objeto = null, maquina = null;
				bool tinhaObj = false;

				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);

					// ============================ AS PASSAGENS SAEM COMO LISTA ============================
					// Uma celula que TELEPORTA pra outro mapa nao e cenario nem porta: ela nao abre, nao
					// bloqueia, e o que importa dela e o DESTINO -- que nao cabe num tile.
					//
					// O typepath COMPLETO vai pro extrator, e nao o `bp`: metade das passagens guarda o
					// destino nas propriedades da instancia (`{gotox = 354; gotoy = 2; gotoz = 12}`), e
					// o `BasePath` corta exatamente essa parte. Ver `Passagens`.
					//
					// A CELULA CONTINUA SENDO DESENHADA. No original a boca da caverna e um desenho como
					// outro qualquer, e apagar o tile deixaria um buraco no chao onde havia uma entrada.
					if (Passagens.De(tp) is { } dest && Destinos != null)
					{
						if (Destinos(dest.X, dest.Y, dest.Z) is { } onde)
							passagens.Add($"{{ \"x\": {x}, \"y\": {y}, \"zona\": \"{onde.Zona}\", "
										  + $"\"dx\": {onde.Px:0}, \"dy\": {onde.Py:0}, "
										  + $"\"nome\": \"{(dest.Nome.Length > 0 ? dest.Nome : onde.Zona)}\" }}");
						else semDestino++;
					}

					if (bp.StartsWith("/turf", StringComparison.Ordinal))
					{
						fundo ??= bp;
						topo = bp;                        // sempre o ultimo visto
					}
					else if (bp.StartsWith("/obj", StringComparison.Ordinal))
					{
						tinhaObj = true;

						// ============================ A MAQUINA E A MOBILIA NAO DISPUTAM A CELULA ============================
						// Antes havia um `objeto` so, preenchido pelo PRIMEIRO `/obj` da lista, e isso
						// engoliu o unico banco de Vegeta. A celula (123,286) do `.dmm` e
						// `['/obj/buildables/chair', '/obj/Bank', '/turf/Tile/Tile5', ...]`: a cadeira vem
						// primeiro, travava a variavel, e `Obras.PorTypepath` nunca era chamado com
						// `/obj/Bank`. O banco nao ficou invisivel -- ele nunca chegou ao `.objetos`, e o
						// bit de densidade dele nunca foi assado. Quatro tiles do ponto onde o jogador
						// nasce em Vegeta, e a UNICA maquina perdida dos 26 andares.
						//
						// Elas nao competem porque nao vao pro mesmo lugar: a maquina sai da cena e vira
						// construcao (`interativos`), a mobilia continua sendo celula de tilemap. Cabem
						// as duas na mesma celula, como cabiam no original.
						//
						// SO ESTA CELULA EMPILHA MOVEL EM CIMA DE MAQUINA hoje: das 1.823 celulas com
						// dois ou mais `/obj` nos 26 mapas, 1.797 sao pares de `/obj/barrier/Edges`
						// (colisao, que entra por outro caminho).
						if (Obras.PorTypepath(bp) != null) maquina ??= bp;
						else objeto ??= bp;
					}
				}

				// uma celula com um turf so nao precisa de decoracao; com dois ou mais, o
				// primeiro e o chao e o ULTIMO vai por cima
				bool empilhado = topo != null && !ReferenceEquals(fundo, topo) && fundo != topo;
				Por(fundo, bytes, x, y);
				if (empilhado) Por(topo, decoracao, x, y);

				// A CAMADA DE OBJETOS NAO CEGA -- e o que tira a sombra das arvores.
				//
				// A sombra em si estava CERTA (geometria de bloco projetada a partir dos pes), mas
				// ficava esquisita porque a premissa era errada: uma arvore nao e um muro. Ela tem
				// copa, tronco e vaos, e a sombra dela num campo aberto virava um leque preto de
				// borda reta saindo de cada tronco -- muitos leques, sem nada os unindo, num terreno
				// que deveria ser aberto. Muro projeta sombra porque muro E um plano opaco;
				// vegetacao nao e.
				//
				// A ARVORE CONTINUA PARANDO O CORPO: `muros` (fisica) segue recebendo a celula; so
				// `vendados` (visao) deixa de receber. Bloquear passagem e bloquear visao viraram
				// duas perguntas separadas, que e o que o DM fazia com `density` e `opacity`.
				// A MAQUINA PRIMEIRO, e por um caminho separado: ela sai da cena pela lista
				// `interativos` e nao disputa o tile com a mobilia. Ver o comentario do
				// `Obras.PorTypepath` vinte linhas acima.
				bool posMaq = Por(maquina, objetos, x, y, cega: false);
				if (maquina != null && !posMaq)
					maquinaSemArte[maquina] = maquinaSemArte.GetValueOrDefault(maquina) + 1;

				bool posObj = Por(objeto, objetos, x, y, cega: false);
				if (posObj || posMaq) comObj++;
				if (tinhaObj && !posObj && !posMaq) objSemArte++;
			}

		// ============================ A COSTURA TAMBEM PRECISA FECHAR NO DESENHO ============================
		// Tirar o bloqueio resolve metade: a celula de vazio continua sem tile, e no meio de um oceano
		// azul liso ela vira uma FRESTA PRETA de um tile cortando o mapa de cima a baixo. Trocar
		// "parede invisivel" por "risco preto" e trocar um defeito por outro -- e o segundo o dono ve
		// de longe.
		//
		// A COSTURA PEGA O CHAO DO VIZINHO, e nao um tile escolhido a dedo: ela existe porque o
		// mapeador emendou dois pedacos do MESMO terreno, entao o desenho certo dela e, literalmente,
		// o do lado. Oeste primeiro e depois leste/norte/sul, sempre na mesma ordem -- a saida e a
		// mesma toda vez que o mesmo `.dmm` entra.
		//
		// ISTO NAO INVENTA ARTE. O tile copiado ja esta no mapa, a uma celula de distancia; nada de
		// novo entra no tileset. Onde nao ha vizinho com chao (o anel na beirada do retangulo, onde
		// do lado de fora nao ha nada), a celula continua vazia -- que e o certo, porque ali o mapa
		// realmente acaba.
		if (costuras.Count > 0)
		{
			var chao = new Dictionary<(int, int), Jandirus.Core.World.CelulaDePedaco>();
			foreach (Jandirus.Core.World.CelulaDePedaco c in bytes) chao[(c.X, c.Y)] = c;

			int remendadas = 0;
			foreach ((int x, int y) in costuras.OrderBy(c => c.Item2).ThenBy(c => c.Item1))
			{
				if (chao.ContainsKey((x, y))) continue;
				foreach ((int dx, int dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
				{
					if (!chao.TryGetValue((x + dx, y + dy), out Jandirus.Core.World.CelulaDePedaco v)) continue;
					bytes.Add(v with { X = (short)x, Y = (short)y });
					usadas++;
					remendadas++;
					break;
				}
			}
			if (remendadas > 0)
				Console.WriteLine($"  {nome}: {remendadas} celula(s) de costura remendadas com o chao do vizinho");
		}

		var sb = new StringBuilder();
		sb.Append("[gd_scene load_steps=3 format=3]\n\n");
		sb.Append("[ext_resource type=\"TileSet\" path=\"res://Assets/Maps/tileset.tres\" id=\"1_ts\"]\n");
		sb.Append("[ext_resource type=\"Script\" path=\"res://Client/PlanetaPreFeito.cs\" id=\"1_pl\"]\n\n");

		// A RAIZ ORDENA: sem `y_sort_enabled` aqui as camadas nao se misturam com os
		// personagens -- o Godot so funde a ordenacao quando ela e continua de pai pra filho.
		//
		// E A RAIZ SABE QUE PLANETA ELA E. O script vai colado nela com nome, gravidade e tipo
		// ja preenchidos, entao quem abre `z03_Vegeta.tscn` no editor VE que ali a gravidade e
		// 10. A alternativa -- uma tabela central com essas informacoes -- obriga quem edita o
		// mapa a lembrar de um segundo arquivo, e o que ele esquecer nao falha em teste nenhum:
		// falha no dia em que alguem treinar em Vegeta e ganhar o mesmo que na Terra.
		//
		// O SERVIDOR NAO LE ISTO (ele roda sem cena). Ele le o mesmo dado do `planetas.json`,
		// que sai do MESMO extrator -- uma fonte, duas leituras, cada uma do lado que a usa.
		FichaDeCena ficha = FichaDaZona(nome);
		sb.Append($"[node name=\"{nome}\" type=\"Node2D\"]\n");
		sb.Append("y_sort_enabled = true\n");
		sb.Append("script = ExtResource(\"1_pl\")\n");
		sb.Append($"NomeDoPlaneta = \"{ficha.Nome}\"\n");
		sb.Append($"Gravidade = {ficha.Gravidade.ToString("0.##", CultureInfo.InvariantCulture)}\n");
		sb.Append($"Tipo = {ficha.Tipo}\n");
		sb.Append("Procedural = false\n\n");

		// ============================ TRES CAMADAS, TRES ALTURAS ============================
		// O corpo do jogador desenha em z 0, ordenando por Y junto dos objetos. Pra ele ficar
		// ACIMA do chao e da decoracao e ABAIXO das arvores, as tres precisam de z DIFERENTES --
		// e nao havia degrau entre o chao (-1) e os atores (0), entao a decoracao dividia o z 0
		// com o jogador e qualquer tufo de grama com Y maior desenhava por cima dele.
		//
		//   -2  chao          (grama, terra)
		//   -1  decoracao     (litoral, plantas, cadeiras)
		//    0  atores E objetos, ordenando por Y entre si (arvores)
		// ====================================================================================

		// ============================ AS CAMADAS NASCEM VAZIAS ============================
		// Elas sao a MOLDURA (tileset, altura, ordenacao) e nao o conteudo. As celulas chegam do
		// `.pedacos` em blocos de 64x64, conforme a camera anda -- ver o comentario la em cima e
		// `Client.PintorDePedacos`. Uma camada com dado aqui voltaria a ser montada inteira no
		// primeiro quadro, que e exatamente o que se esta tirando.
		// ==================================================================================

		// O CHAO NUNCA ORDENA e fica sempre embaixo: e chao. Ordenar 250 mil tiles de grama
		// contra os personagens seria caro e nao mudaria nada -- ninguem passa atras da grama.
		sb.Append("[node name=\"Chao\" type=\"TileMapLayer\" parent=\".\"]\n");
		sb.Append("z_index = -2\n");
		sb.Append(SemFisica);
		sb.Append("tile_set = ExtResource(\"1_ts\")\n\n");

		// DECORACAO: o turf que estava POR CIMA do chao no prefab. E onde moram a porta, o
		// litoral curvo, as plantas e as cadeiras -- tudo que o "primeiro turf vence" comia.
		//
		// NAO ORDENA MAIS POR Y. Ela esta inteira abaixo dos atores agora, entao ordenar nao muda
		// desenho nenhum -- so custaria a classificacao de mais uma camada de 250 mil celulas.
		// Nada aqui e alto o bastante pra alguem passar atras: quem tem altura mora em `Objetos`.
		sb.Append("[node name=\"Decor\" type=\"TileMapLayer\" parent=\".\"]\n");
		sb.Append("z_index = -1\n");
		sb.Append(SemFisica);
		sb.Append("tile_set = ExtResource(\"1_ts\")\n\n");

		// SEGUNDA CAMADA: o que fica EM CIMA do chao. Precisa ser um layer proprio porque o
		// TileMapLayer guarda UM tile por celula -- arvore e grama na mesma celula sao duas
		// camadas, nao duas entradas na mesma.
		// OBJETOS ORDENAM POR Y. E o que poe o personagem ATRAS da arvore quando ele esta
		// acima dela e NA FRENTE quando esta abaixo. `z_index` fixo mataria isso: dentro de um
		// mesmo z quem decide e o Y, entre z diferentes quem decide e sempre o z.
		sb.Append("[node name=\"Objetos\" type=\"TileMapLayer\" parent=\".\"]\n");
		sb.Append("y_sort_enabled = true\n");
		sb.Append(SemFisica);
		sb.Append("tile_set = ExtResource(\"1_ts\")\n");

		File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(false));

		// AS CELULAS, AO LADO DA CENA. A ordem dos nomes casa com a ordem em que as camadas foram
		// declaradas acima -- e o que deixa o cliente achar o `TileMapLayer` de cada pedaco sem
		// depender de procurar por nome numa arvore que ele nao montou.
		string arqPedacos = Path.ChangeExtension(caminho, ".pedacos");
		Jandirus.Core.World.PedacosDoMapa.Escrever(
			arqPedacos,
			Jandirus.Core.World.PedacosDoMapa.LadoPadrao,
			["Chao", "Decor", "Objetos"],
			[bytes, decoracao, objetos]);

		// ============================ LER DE VOLTA E CONFERIR A CONTA ============================
		// Um mapa que perde celulas no caminho nao falha: ele DESENHA errado, e so alguem olhando
		// pro chao percebe -- meses depois, sem saber de onde veio. Este pipeline ja produziu um
		// defeito assim (o `tile_map_data` recusado em silencio por causa do numero de formato: a
		// cena carregava, o layer ficava vazio e nada avisava).
		//
		// CONTAR NAO BASTA: uma celula que caisse no balde errado, ou com o X e o Y trocados,
		// passaria por uma conferencia de quantidade. A assinatura mistura camada, posicao e quadro
		// de cada celula e NAO depende da ordem -- que e o que muda de propósito no agrupamento.
		//
		// Sao ~5 ms por mapa pra transformar "confio no meu agrupamento" em "esta escrito".
		int esperadas = bytes.Count + decoracao.Count + objetos.Count;
		ulong assinado = Assinar(bytes, 0) ^ Assinar(decoracao, 1) ^ Assinar(objetos, 2);

		Jandirus.Core.World.PedacosDoMapa? relido =
			Jandirus.Core.World.PedacosDoMapa.Ler(File.ReadAllBytes(arqPedacos));
		if (relido == null)
		{
			Console.WriteLine($"  {nome}: ERRO -- o .pedacos que acabei de escrever nao volta a ler");
		}
		else
		{
			ulong lido = 0;
			for (int c = 0; c < relido.Camadas.Length; c++)
				for (int cy = relido.Cy0; cy < relido.Cy1; cy++)
					for (int cx = relido.Cx0; cx < relido.Cx1; cx++)
					{
						if (!relido.Achar(cx, cy, c, out int ini, out int q)) continue;
						for (int i = 0; i < q; i++) lido ^= Marca(relido.Celula(ini, i), c);
					}

			if (relido.TotalDeCelulas != esperadas || lido != assinado)
				Console.WriteLine($"  {nome}: ERRO NO .pedacos -- escrevi {esperadas} celulas "
								  + $"(assinatura {assinado:X16}) e reli {relido.TotalDeCelulas} "
								  + $"({lido:X16})");
		}

		LightCatalog.Escrever(Path.ChangeExtension(caminho, ".luz"), luzes);
		if (luzes.Count > 0) Console.WriteLine($"  {nome}: {luzes.Count} fontes de luz");
		paredes = muros;
		cegos = vendados;
		portasDaCena = portas;
		maquinasDaCena = interativos;
		passagensDaCena = passagens;
		if (passagens.Count > 0)
			Console.WriteLine($"  {nome}: {passagens.Count} passagem(ns) pra outros mapas");
		if (semDestino > 0)
			Console.WriteLine($"  {nome}: ATENCAO -- {semDestino} passagem(ns) apontam pra um z que nao foi convertido");
		if (interativos.Count > 0)
			Console.WriteLine($"  {nome}: {interativos.Count} maquina(s) saem do tilemap");
		if (comObj > 0 || objSemArte > 0)
			Console.WriteLine($"  {nome}: {comObj} objetos desenhados"
							  + (objSemArte > 0 ? $" | {objSemArte} celulas com objeto SEM arte" : ""));
		if (repontadas > 0)
			Console.WriteLine($"  {nome}: {repontadas} celulas apontam pra uma tira animada"
							  + (repontadasPadrao > 0 ? $" ({repontadasPadrao} no estado PADRAO)" : ""));

		// QUADRO 0 SILENCIOSO. Quando o `icon_state` do typepath nao existe no atlas, o Coord
		// devolve o quadro 0 sem dizer nada -- e o quadro 0 de uma folha qualquer pode ser um
		// Namekuseijin. Foi exatamente assim que as casas apareceram com parede de
		// Namekuseijin. Agora isto e um relatorio, nao uma surpresa na tela.
		if (semEstado.Count > 0)
		{
			int total = semEstado.Values.Sum();
			Console.WriteLine($"     ATENCAO: {total} celulas cairam no quadro 0 " +
							  $"({semEstado.Count} estados que o atlas nao tem)");
			foreach ((string q, int n) in semEstado.OrderByDescending(kv => kv.Value).Take(6))
				Console.WriteLine($"        {n,7}x  {q}");
		}

		// ============================ NADA PODE SER SOLIDO E INVISIVEL CALADO ============================
		// Esta e a regra que faltava, e ela vale mais que os tres consertos que a trouxeram. As duas
		// queixas do dono ("falta o banco" e "tem parede invisivel no meio do mapa") sao o mesmo
		// defeito visto de dois lados: alguma coisa que o servidor sabe que existe e que a tela nao
		// mostra. O port ja produziu esse par pelo menos quatro vezes -- a bandeira de conquista sem
		// `.dmi` convertido, o `Ship_Control`/`Ship_Pad` fora do catalogo, 35 atlas escritos e nunca
		// importados, e agora a costura de Hera.
		//
		// Um erro visivel aqui vale mais que trinta e cinco atlas mudos: o custo de descobrir isto
		// pelo jogo e o dono andando mil tiles ate esbarrar no nada.
		//
		// O QUE E LEGITIMO FICA DE FORA da conta, e sao dois casos so: a BORDA DO MUNDO (`mudas`, o
		// Blank com miolo) e a PASSAGEM, que e desenhada mas nao bloqueia. Tudo o mais que bloqueia
		// tem que ter dono na tela.
		var fantasmas = muros.Where(c => !desenhadas.Contains(c) && !mudas.Contains(c)).ToList();
		if (fantasmas.Count > 0)
		{
			Console.WriteLine($"     PAREDE INVISIVEL: {fantasmas.Count} celula(s) BLOQUEIAM e ninguem as desenha");
			foreach ((int x, int y) in fantasmas.OrderBy(c => c.Item2).ThenBy(c => c.Item1).Take(8))
				Console.WriteLine($"        ({x},{y})");
		}

		if (maquinaSemArte.Count > 0)
		{
			Console.WriteLine($"     MAQUINA SEM DESENHO: {maquinaSemArte.Values.Sum()} celula(s). O catalogo "
							  + "a reconhece e o `.dmi` nao virou arte -- ela NAO entrou no `.objetos`:");
			foreach ((string q, int n) in maquinaSemArte.OrderByDescending(kv => kv.Value))
				Console.WriteLine($"        {n,7}x  {q}");
		}

		if (costuras.Count > 0)
			Console.WriteLine($"  {nome}: {costuras.Count} celula(s) de vazio DESARMADAS "
							  + "(sem miolo -- costura do mapeador, nao limite do mundo)");

		if (semAnimacao.Count > 0)
		{
			Console.WriteLine($"     ANIMACAO PERDIDA: {semAnimacao.Values.Sum()} celulas em "
							  + $"{semAnimacao.Count} estado(s) que TEM mais de um quadro e ficaram paradas");
			foreach ((string q, int n) in semAnimacao.OrderByDescending(kv => kv.Value).Take(8))
				Console.WriteLine($"        {n,7}x  {q}");
		}
		return usadas;
	}

	/// <summary>
	/// Declara os tiles de UM atlas: um por quadro, com os estados de varios quadros virando
	/// tile ANIMADO.
	///
	/// COMO O .dmi GUARDA OS QUADROS -- e a armadilha central daqui: a ordem e
	/// POR QUADRO, POR DIRECAO. Um estado com 4 direcoes e 2 quadros ocupa oito celulas assim:
	///
	///     q1d1 q1d2 q1d3 q1d4 q2d1 q2d2 q2d3 q2d4
	///
	/// ou seja, os quadros de UMA MESMA direcao ficam a `dirs` celulas de distancia, nao
	/// coladas. E por isso que a animacao usa `animation_separation = (dirs-1, 0)`: sem ela, a
	/// agua virada pro norte tocaria os quadros do leste, do oeste e do sul.
	///
	/// O Godot exige que os quadros de uma animacao caibam na MESMA LINHA do atlas (com
	/// `animation_columns = 0`). Quando o estado atravessa a quebra de linha da folha, o tile
	/// vira estatico, quadro a quadro -- melhor um sprite parado que se pode pintar do que uma
	/// animacao que o editor recusa e leva a fonte inteira junto.
	/// </summary>
	private static (int Tiles, int Animados) DeclararTiles(StringBuilder sub, Fonte f)
	{
		var ocupadas = new HashSet<(int X, int Y)>();
		var linhas = new List<(int X, int Y, string Texto)>();
		int animados = 0;

		// SPRITE MAIOR QUE O TILE: BASE NO CHAO DA CELULA, CENTRADO NA HORIZONTAL.
		//
		// Nao e palpite -- e a regra que o proprio jogo escreve. O BYOND ancora o icone no
		// canto INFERIOR ESQUERDO do tile (cresce pra cima e pra direita), e este jogo corrige
		// isso na mao, em Code/Modules/Turfs/Plants.dm:31-35:
		//
		//     var/icon/I = icon(icon,icon_state)
		//     trees_pixelx_cache[ck] = 16 - I.Width()/2
		//     pixel_x = trees_pixelx_cache[ck]
		//
		// `16 - largura/2` poe o CENTRO do icone no centro do tile, e `pixel_y` nunca e tocado
		// -- ou seja: centro no meio, base no chao. A mesma formula reaparece em
		// Reincarnation_Tree e Lumber_Tree. Eu tinha portado o padrao do engine (canto
		// inferior ESQUERDO) e nao a correcao do jogo: a arvore saia 32 px a direita e a
		// colisao ficava visivelmente fora dela.
		//
		// `texture_origin` e SUBTRAIDO da posicao de desenho: positivo move pra CIMA e pra
		// ESQUERDA. O Godot ja desenha centrado na horizontal, entao x = 0; levar a base ao pe
		// da celula pede y = +(altura-32)/2.
		int desY = (f.IconH - Cell) / 2;
		bool grande = f.IconW != Cell || f.IconH != Cell;

		void Ancora((int X, int Y) c, StringBuilder onde)
		{
			if (!grande) return;
			onde.Append($"{c.X}:{c.Y}/0/texture_origin = Vector2i(0, {desY})\n");

			// SEM `y_sort_origin`. Ele parece certo ("ordena pelo pe") e estava meio tile
			// errado: o personagem ordena pelo Y do NO, que e o centro do sprite dele, e os
			// tiles de 32x32 nao recebiam ajuste nenhum. Deslocar so os tiles grandes em +16
			// criava tres referencias diferentes na mesma comparacao.
			//
			// Com todo mundo em 0 a conta fecha sozinha: a base do tile grande fica em
			// centro+16 (por causa do desY acima) e os pes do personagem em centro+16 (sprite
			// de 32 px centrado no no). Os dois +16 se cancelam e ordenar pelo centro passa a
			// ser exatamente ordenar pela base.
		}

		// A CAIXA E DE UMA CELULA, nao do tamanho do desenho. No BYOND densidade e propriedade do
		// TURF, e um turf e um tile: a arvore de 96x96 ocupa UM tile (o do tronco) e o resto e
		// copa que se atravessa. Usando a extensao do icone, a arvore virava um bloco de 3x3
		// centrado no MEIO dela -- e essa e a queixa "a hitbox esta no meio e nao na base".
		string m = Inv(-Cell / 2f), p = Inv(Cell / 2f);
		string umaCelula = $"PackedVector2Array({m}, {m}, {p}, {m}, {p}, {p}, {m}, {p})";

		void Fisica((int X, int Y) c, StringBuilder onde)
		{
			if (f.Densas.Contains(c))
				onde.Append($"{c.X}:{c.Y}/0/physics_layer_0/polygon_0/points = {umaCelula}\n");
			if (f.Opacas.Contains(c))
				onde.Append($"{c.X}:{c.Y}/0/occlusion_layer_0/polygon = {umaCelula}\n");
		}

		bool Estatico(int indice)
		{
			(int X, int Y) c = Indice(f, indice);
			if (!ocupadas.Add(c)) return false;
			var b = new StringBuilder();
			b.Append($"{c.X}:{c.Y}/0 = 0\n");
			Ancora(c, b);
			Fisica(c, b);
			linhas.Add((c.X, c.Y, b.ToString()));
			return true;
		}

		// ============================ O COMPANHEIRO E SO TIRAS ============================
		// Uma linha por estado, comecando sempre na coluna zero -- foi pra isso que ele foi feito.
		// Entao aqui nao ha teste de "cabe": cabe por construcao, e o `animation_columns = 0` que o
		// Godot ja entendia volta a valer sem nenhuma aritmetica de wrap.
		if (f.States.Count == 0 && f.Duracoes.Count > 0)
		{
			for (int linha = 0; linha < f.Duracoes.Count; linha++)
			{
				double[] dur = f.Duracoes[linha];
				var t = new StringBuilder();
				t.Append($"0:{linha}/0 = 0\n");
				t.Append($"0:{linha}/animation_columns = 0\n");
				t.Append($"0:{linha}/animation_speed = 1.0\n");
				for (int q = 0; q < dur.Length; q++)
					t.Append($"0:{linha}/animation_frame_{q}/duration = "
							 + dur[q].ToString("0.####", CultureInfo.InvariantCulture) + "\n");
				Ancora((0, linha), t);
				Fisica((0, linha), t);
				linhas.Add((0, linha, t.ToString()));
				animados++;
				for (int q = 0; q < dur.Length; q++) ocupadas.Add((q, linha));
			}

			foreach ((int X, int Y, string Texto) l in linhas.OrderBy(l => l.Y).ThenBy(l => l.X))
				sub.Append(l.Texto);
			return (linhas.Count, animados);
		}

		int idx = 0;
		foreach (DmiState st in f.States)
		{
			int dirs = Math.Max(1, st.Dirs);
			int quadros = Math.Max(1, st.Frames);

			for (int d = 0; d < dirs; d++)
			{
				(int X, int Y) baseC = Indice(f, idx + d);

				// cabe animar? todos os quadros desta direcao tem que ficar na MESMA linha
				bool cabe = quadros > 1 && baseC.X + (quadros - 1) * dirs < f.Cols
							&& idx + (quadros - 1) * dirs + d < f.TotalQuadros;

				if (!cabe)
				{
					for (int q = 0; q < quadros; q++) Estatico(idx + q * dirs + d);
					continue;
				}

				var b = new StringBuilder();
				b.Append($"{baseC.X}:{baseC.Y}/0 = 0\n");
				// 0 = todos os quadros numa linha so
				b.Append($"{baseC.X}:{baseC.Y}/animation_columns = 0\n");
				if (dirs > 1)
					b.Append($"{baseC.X}:{baseC.Y}/animation_separation = Vector2i({dirs - 1}, 0)\n");
				b.Append($"{baseC.X}:{baseC.Y}/animation_speed = 1.0\n");

				for (int q = 0; q < quadros; q++)
				{
					// o `delay` do BYOND e em DECIMOS de segundo; o Godot quer segundos
					double d10 = q < st.Delays.Length ? st.Delays[q] : 1;
					double seg = Math.Max(d10, 0.1) / 10.0;
					b.Append($"{baseC.X}:{baseC.Y}/animation_frame_{q}/duration = " +
							 seg.ToString("0.####", CultureInfo.InvariantCulture) + "\n");
					ocupadas.Add((baseC.X + q * dirs, baseC.Y));
				}
				Ancora(baseC, b);
				Fisica(baseC, b);
				linhas.Add((baseC.X, baseC.Y, b.ToString()));
				animados++;
			}

			idx += dirs * quadros;
		}

		// sobras: quadros que nenhum estado reivindicou (folha com celulas soltas). Entram
		// como tile parado -- se esta desenhado na imagem, o editor tem que deixar pintar.
		for (int i = 0; i < f.TotalQuadros; i++) Estatico(i);

		foreach ((int _, int _y, string texto) in linhas.OrderBy(l => l.Y).ThenBy(l => l.X))
			sub.Append(texto);

		return (linhas.Count, animados);
	}

	/// <summary>
	/// O ESTADO que esta celula usa. Quase sempre e o do typepath -- menos nos TURFS HD, onde
	/// o desenho e um MOSAICO montado por coordenada.
	///
	/// O `autofill()` do original (`Turfs.dm:50-57`) roda no `New()` de cada turf e escreve
	/// `icon_state = "[x % (getWidth/32)],[y % (getHeight/32)]"`, ou `"[x&1],[y&1]"` quando o
	/// tipo nao declara tamanho. Como isso mora num corpo de proc, o scanner nunca viu: o
	/// estado ficava vazio e TODA celula HD desenhava o mesmo quadro. Sao 55 mil celulas na
	/// Terra -- 22% do planeta com a mesma grama errada.
	///
	/// O EIXO Y: o `autofill` usa o y do BYOND, que conta DE BAIXO PRA CIMA, e o DmmMap guarda
	/// em ordem de arquivo (linha 0 no topo). Sem converter, o mosaico sai espelhado na
	/// vertical -- funciona, e fica sutilmente errado, que e pior que quebrar.
	/// </summary>
	private static string? EstadoDaCelula(TurfDef td, int x, int y, int altura)
	{
		if (!td.IsHD) return td.IconState;
		// tipo HD que ja declara o estado na mao (WaterHD1) manda mais que o autofill
		if (!string.IsNullOrEmpty(td.IconState)) return td.IconState;

		int bx = x + 1;              // o BYOND indexa a partir de 1
		int by = altura - y;         // e conta de baixo pra cima

		if (td.GetWidth > 0 && td.GetHeight > 0)
			return $"{bx % (td.GetWidth / Cell)},{by % (td.GetHeight / Cell)}";

		return $"{bx & 1},{by & 1}";
	}

	/// <summary>Indice linear de quadro -> coordenada na grade do atlas.</summary>
	private static (int X, int Y) Indice(Fonte f, int i) => (i % f.Cols, i / f.Cols);

	private static string Inv(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

	// =====================================================================
	// A AGUA -- a terceira classe de celula (ver Core/World/Agua.cs)
	// =====================================================================

	/// <summary>
	/// AS CELULAS DE AGUA DESTE ANDAR.
	///
	/// ============================ QUAL TURF DECIDE, NUMA CELULA COM VARIOS ============================
	/// O ULTIMO, e e a mesma regra que o desenho ja usa vinte linhas acima ("o ultimo turf vence" --
	/// no DM cada `new /turf/X(loc)` de um prefab SUBSTITUI o anterior, entao quem existe no fim e o
	/// unico que existe). Perguntar pelo PRIMEIRO daria agua onde o mapeador pos uma ponte ou uma
	/// pedra por cima do lago, e chao onde ele pos agua por cima da areia.
	/// ================================================================================================
	///
	/// Sai como funcao propria, e nao dentro do `EscreverCena`, porque ela precisa rodar tambem no
	/// comando `agua` -- que existe justamente pra NAO reconverter os sprites (ver `Program.cs`).
	/// Duas derivacoes de "o que e agua" divergiriam do mesmo jeito que a colisao e a cena
	/// divergiam em 2% das celulas antes de virarem uma passada so.
	/// </summary>
	internal static List<(int X, int Y)> CelulasDeAgua(DmmLevel nivel, DmmMap.Result dados,
													   Dictionary<string, TurfDef> turfs)
	{
		var molhadas = new List<(int, int)>();
		for (int y = 0; y < nivel.Height; y++)
			for (int x = 0; x < nivel.Width; x++)
			{
				string? k = nivel.Cells[x, y];
				if (k == null || !dados.Keys.TryGetValue(k, out string[]? tipos)) continue;

				string? ultimoTurf = null;
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (bp.StartsWith("/turf", StringComparison.Ordinal)) ultimoTurf = bp;
				}

				if (ultimoTurf == null || !turfs.TryGetValue(ultimoTurf, out TurfDef? td)) continue;
				if (Aguas.Eh(ultimoTurf, td)) molhadas.Add((x, y));
			}
		return molhadas;
	}

	// =====================================================================
	// A NUVEM -- a quarta classe de celula (ver Core/World/Ceu.cs)
	// =====================================================================

	/// <summary>
	/// AS CELULAS DE NUVEM DESTE ANDAR.
	///
	/// **A MESMA REGRA DE DESEMPATE DA AGUA**, e por isso ela e uma copia da forma e nao da decisao:
	/// vale o ULTIMO turf da celula, porque no DM cada `new /turf/X(loc)` de um prefab SUBSTITUI o
	/// anterior. Quem existe no fim e o unico que existe. Perguntar pelo primeiro daria nuvem onde o
	/// mapeador pos uma plataforma por cima dela -- e no Templo, que e quase todo ceu com ilhas de
	/// piso, isso seria a diferenca entre um mapa jogavel e um buraco.
	///
	/// QUEM DECIDE E o <see cref="Ceus.Eh"/>, que por sua vez delega pro `Aguas.EhCeu` -- a MESMA
	/// leitura que exclui o ceu da agua vinte linhas acima. Ver o cabecalho de `Ceus`.
	/// </summary>
	internal static List<(int X, int Y)> CelulasDeNuvem(DmmLevel nivel, DmmMap.Result dados,
													  Dictionary<string, TurfDef> turfs)
	{
		var nuvens = new List<(int, int)>();
		for (int y = 0; y < nivel.Height; y++)
			for (int x = 0; x < nivel.Width; x++)
			{
				string? k = nivel.Cells[x, y];
				if (k == null || !dados.Keys.TryGetValue(k, out string[]? tipos)) continue;

				string? ultimoTurf = null;
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (bp.StartsWith("/turf", StringComparison.Ordinal)) ultimoTurf = bp;
				}

				if (ultimoTurf == null || !turfs.TryGetValue(ultimoTurf, out TurfDef? td)) continue;
				if (Nuvens.Eh(ultimoTurf, td)) nuvens.Add((x, y));
			}
		return nuvens;
	}

	/// <summary>
	/// Grava o `.nuvem` de todos os andares E MAIS NADA -- o irmao do <see cref="ConverterAguas"/>, e a
	/// justificativa dele vale palavra por palavra: a conversao cheia reescreveria o tileset, os 40
	/// `.tscn`/`.pedacos` e o indice de sprites pra buscar UM bit por celula.
	///
	/// ZONA SEM NUVEM NAO GANHA ARQUIVO, e um `.nuvem` velho de uma zona que perdeu a nuvem e APAGADO --
	/// mesma regra do `.agua`, e pelo mesmo motivo: um arquivo antigo em que o leitor confia e pior
	/// que arquivo nenhum. Aqui ele seria especialmente feio, porque nuvem que sobrou de um mapa
	/// antigo **derruba gente** em vez de so parar.
	/// </summary>
	public static void ConverterNuvens(string dmmDir, string outDir, Dictionary<string, TurfDef> turfs)
	{
		Directory.CreateDirectory(outDir);

		var mapas = new List<(string Arquivo, DmmMap.Result Dados, int Offset)>();
		int offset = 0;
		foreach (string dmm in OrdemDoDme(dmmDir))
		{
			DmmMap.Result d = DmmMap.Read(dmm);
			mapas.Add((dmm, d, offset));
			offset += d.Levels.Count;
		}

		int andares = 0, comCeu = 0, total = 0, apagados = 0, derrubam = 0;
		foreach ((string _, DmmMap.Result dados, int off) in mapas)
			foreach (DmmLevel nivel in dados.Levels)
			{
				string nome = NomeDoAndar(dados, nivel, off);
				string caminho = Path.Combine(outDir, nome + ".nuvem");
				List<(int X, int Y)> nuvens = CelulasDeNuvem(nivel, dados, turfs);
				andares++;

				if (nuvens.Count == 0)
				{
					if (File.Exists(caminho)) { File.Delete(caminho); apagados++; }
					continue;
				}

				EscreverColisao(caminho, nivel.Width, nivel.Height, nuvens);
				comCeu++;
				total += nuvens.Count;

				// RELER O QUE ACABOU DE SER ESCRITO, pelo mesmo motivo do `.agua`: o que interessa nao
				// e "o conversor decidiu N celulas", e "o objeto que o JOGO consulta responde ceu em N
				// celulas". O `CarregarNuvem` RECUSA CALADO quando o tamanho nao bate, e uma recusa
				// calada aqui seria verde na bancada e chao comum em jogo -- que e literalmente o bug
				// que esta tarefa conserta.
				//
				// E O NOME DA ZONA ENTRA NA RELEITURA, e nao um `true` de conveniencia: e ele que
				// decide se esta nuvem derruba, e conferir o plano sem conferir o desfecho deixaria
				// passar o caso em que o mapa tem ceu e o Core nao sabe pra onde manda-lo.
				string zona = nome[(nome.IndexOf('_') + 1)..];
				var relido = Jandirus.Core.World.ZoneCollision.Montar(
					nivel.Width, nivel.Height, new byte[(nivel.Width * nivel.Height + 7) / 8]);
				int conferidas = 0;
				if (!relido.CarregarNuvem(File.ReadAllBytes(caminho), zona))
					Console.WriteLine($"  {nome,-34} FALHOU: o .nuvem escrito nao volta pelo CarregarNuvem");
				else
					for (int y = 0; y < nivel.Height; y++)
						for (int x = 0; x < nivel.Width; x++)
							if (relido.EhNuvem(x, y)) conferidas++;

				var destino = Jandirus.Core.World.ClasseDeNuvem.DestinoDaQueda(zona);
				if (destino != null) derrubam++;

				string aviso = conferidas == nuvens.Count ? "" : $"  <-- RELEU {conferidas}, DIVERGE";
				Console.WriteLine($"  {nome,-34} {nuvens.Count,8} celulas de ceu  "
								  + (destino is { } d2 ? $"DERRUBA -> {d2.Zona} ({d2.Bx},{d2.By})" : "so barra")
								  + aviso);
			}

		Console.WriteLine($"andares: {andares} | com ceu: {comCeu} ({derrubam} derrubam) | celulas: {total}"
						  + (apagados > 0 ? $" | .nuvem apagados: {apagados}" : ""));
	}

	// =====================================================================
	// O QUE NAO SE QUEBRA -- o `destroyable` do original (ver Duros.cs)
	// =====================================================================

	/// <summary>
	/// AS CELULAS INDESTRUTIVEIS DESTE ANDAR.
	///
	/// ============================ A REGRA NAO E A MESMA DA AGUA, E ISSO IMPORTA ============================
	/// A agua pergunta pelo ULTIMO turf, porque no DM cada `new /turf/X(loc)` de um prefab SUBSTITUI o
	/// anterior -- quem existe no fim e o unico que existe. Aqui vale o mesmo pros TURFS, e por isso
	/// esta funcao tambem procura o ultimo.
	///
	/// So que `destroyable` nao e uma propriedade da CELULA, e da coisa que sobreviveria: uma
	/// `/obj/barrier` coexiste com o turf, e derrubar o chao debaixo dela nao a apaga (no original
	/// `Destroy()` troca o TURF por `/turf/Ground/Ground8` e a barreira continua bloqueando pelo
	/// `selectivecollide`). Como no port a barreira nao e entidade -- ela virou celula solida do
	/// `.col` --, abrir a celula a apagaria de vez. Entao a celula e dura se o ULTIMO TURF for duro
	/// **ou** se houver QUALQUER obj indestrutivel nela. Ver <see cref="Duros"/>.
	/// ======================================================================================================
	///
	/// Sai como funcao propria, e nao dentro do `EscreverCena`, pelo mesmo motivo da agua: ela
	/// precisa rodar tambem no comando `duro`, que existe justamente pra NAO reconverter os sprites.
	/// </summary>
	internal static List<(int X, int Y)> CelulasDuras(DmmLevel nivel, DmmMap.Result dados,
													  Dictionary<string, TurfDef> turfs)
	{
		var duras = new List<(int, int)>();
		for (int y = 0; y < nivel.Height; y++)
			for (int x = 0; x < nivel.Width; x++)
			{
				string? k = nivel.Cells[x, y];
				if (k == null || !dados.Keys.TryGetValue(k, out string[]? tipos)) continue;

				string? ultimoTurf = null;
				bool objDuro = false;
				foreach (string tp in tipos)
				{
					string bp = DmmMap.BasePath(tp);
					if (bp.StartsWith("/turf", StringComparison.Ordinal)) ultimoTurf = bp;
					else if (Duros.EhObjDuro(bp)) objDuro = true;
				}

				if (objDuro) { duras.Add((x, y)); continue; }
				if (ultimoTurf != null && turfs.TryGetValue(ultimoTurf, out TurfDef? td) && Duros.Eh(td))
					duras.Add((x, y));
			}
		return duras;
	}

	/// <summary>
	/// Grava o `.duro` de todos os andares E MAIS NADA -- irmao do <see cref="ConverterAguas"/>, e
	/// pelo mesmo motivo dele: a conversao cheia reescreve o tileset, o `tiles.json`, os 40
	/// `.tscn`/`.pedacos` e o indice de sprites, e o indice resolve nome repetido por `TryAdd` --
	/// rodar tudo pra buscar UM bit por celula reescreveria 21 artes (4 delas genuinamente
	/// diferentes) sem ninguem ter pedido.
	///
	/// A ZONA SEM NADA DURO NAO GANHA ARQUIVO, e um `.duro` velho de uma zona que amoleceu e
	/// APAGADO -- deixar o antigo seria pior que nao ter nenhum: o leitor confiaria nele e uma
	/// parede que virou destrutivel continuaria eterna, calada.
	/// </summary>
	public static void ConverterDuros(string dmmDir, string outDir, Dictionary<string, TurfDef> turfs)
	{
		Directory.CreateDirectory(outDir);

		var mapas = new List<(string Arquivo, DmmMap.Result Dados, int Offset)>();
		int offset = 0;
		foreach (string dmm in OrdemDoDme(dmmDir))
		{
			DmmMap.Result d = DmmMap.Read(dmm);
			mapas.Add((dmm, d, offset));
			offset += d.Levels.Count;
		}

		int andares = 0, comDuro = 0, total = 0, apagados = 0, alcancaveis = 0;
		foreach ((string _, DmmMap.Result dados, int off) in mapas)
			foreach (DmmLevel nivel in dados.Levels)
			{
				string nome = NomeDoAndar(dados, nivel, off);
				string caminho = Path.Combine(outDir, nome + ".duro");
				List<(int X, int Y)> duras = CelulasDuras(nivel, dados, turfs);
				andares++;

				if (duras.Count == 0)
				{
					if (File.Exists(caminho)) { File.Delete(caminho); apagados++; }
					continue;
				}

				EscreverColisao(caminho, nivel.Width, nivel.Height, duras);
				comDuro++;
				total += duras.Count;

				// ============================ RELER O QUE ACABOU DE SER ESCRITO ============================
				// A mesma regra do `.agua`, e pelo mesmo motivo: o que interessa nao e "o conversor
				// decidiu N celulas", e "o objeto que o JOGO consulta responde N". Sao coisas
				// diferentes -- o `.duro` passa por um cabecalho, por um bitset e por um leitor que
				// RECUSA CALADO quando o tamanho nao bate (`ZoneCollision.CarregarDuro`), e uma recusa
				// calada aqui e exatamente o defeito que este projeto mais paga caro: verde na
				// bancada, quebrando o vazio em jogo.
				// ==========================================================================================
				var lido = Jandirus.Core.World.ZoneCollision.Load(File.ReadAllBytes(
					Path.Combine(outDir, nome + ".col")));
				var relido = Jandirus.Core.World.ZoneCollision.Montar(
					nivel.Width, nivel.Height, new byte[(nivel.Width * nivel.Height + 7) / 8]);
				int conferidas = 0, semColisao = 0, tocaveis = 0;
				if (!relido.CarregarDuro(File.ReadAllBytes(caminho)))
					Console.WriteLine($"  {nome,-34} FALHOU: o .duro escrito nao volta pelo CarregarDuro");
				else
					for (int y = 0; y < nivel.Height; y++)
						for (int x = 0; x < nivel.Width; x++)
						{
							if (!relido.Indestrutivel(x, y)) continue;
							conferidas++;
							if (lido == null) continue;
							// DURO E NAO BLOQUEIA e legitimo: `turf/Other/Sky1` tem `density = 0` e
							// `destroyable = 0`. So vale saber quantos sao -- pra esses o bit nao
							// muda desfecho nenhum, ja que so quem bloqueia chega ao `DerrubarCelula`.
							if (!lido.BlockedCell(x, y)) { semColisao++; continue; }
							// O QUE ESTE PASSE CONSERTA, medido: celula dura, que bloqueia, e que
							// ENCOSTA em chao livre -- ou seja, alcancavel por um punho, e portanto
							// uma das que caiam.
							for (int d = 0; d < 4 && conferidas > 0; d++)
							{
								int vx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
								int vy = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
								if (vx < 0 || vy < 0 || vx >= nivel.Width || vy >= nivel.Height) continue;
								if (!lido.BlockedCell(vx, vy)) { tocaveis++; break; }
							}
						}

				alcancaveis += tocaveis;
				string aviso = conferidas == duras.Count ? "" : $"  <-- RELEU {conferidas}, DIVERGE";
				Console.WriteLine($"  {nome,-34} {duras.Count,8} celulas duras"
								  + $" ({tocaveis} encostam em chao livre"
								  + (semColisao > 0 ? $", {semColisao} nem bloqueiam" : "") + ")"
								  + aviso);
			}

		Console.WriteLine($"andares: {andares} | com celula dura: {comDuro} | celulas: {total}"
						  + $" | ALCANCAVEIS A SOCO: {alcancaveis}"
						  + (apagados > 0 ? $" | .duro apagados: {apagados}" : ""));
	}

	/// <summary>
	/// Grava o `.agua` de todos os andares E MAIS NADA -- nem tileset, nem cena, nem sprite.
	///
	/// ============================ POR QUE UM CAMINHO SO PRA ISTO ============================
	/// A conversao cheia reescreve o tileset, o `tiles.json`, os 40 `.tscn`/`.pedacos` e o indice de
	/// sprites -- e o indice resolve nome repetido por `TryAdd`, entao rodar tudo pra buscar UM bit
	/// por celula reescreveria 21 artes (4 delas genuinamente diferentes) sem ninguem ter pedido.
	/// O dado que falta e novo e independente: um bitset por andar, derivado do `.dmm` e da arvore
	/// de tipos, que nao toca em nenhum arquivo que ja existe.
	///
	/// O `.agua` E UM ARQUIVO NOVO, nao uma cauda do `.col`: a cauda do `.col` ja tem dono (o plano
	/// de grupo da sombra, ver `ZoneCollision.Load`), e uma zona sem agua simplesmente nao ganha
	/// arquivo -- o leitor trata a ausencia como "sem agua", que e a verdade.
	/// =======================================================================================
	/// </summary>
	public static void ConverterAguas(string dmmDir, string outDir, Dictionary<string, TurfDef> turfs)
	{
		Directory.CreateDirectory(outDir);

		var mapas = new List<(string Arquivo, DmmMap.Result Dados, int Offset)>();
		int offset = 0;
		foreach (string dmm in OrdemDoDme(dmmDir))
		{
			DmmMap.Result d = DmmMap.Read(dmm);
			mapas.Add((dmm, d, offset));
			offset += d.Levels.Count;
		}

		int andares = 0, comAgua = 0, total = 0, apagados = 0;
		foreach ((string _, DmmMap.Result dados, int off) in mapas)
			foreach (DmmLevel nivel in dados.Levels)
			{
				string nome = NomeDoAndar(dados, nivel, off);
				string caminho = Path.Combine(outDir, nome + ".agua");
				List<(int X, int Y)> molhadas = CelulasDeAgua(nivel, dados, turfs);
				andares++;

				// ZONA SECA NAO GANHA ARQUIVO -- e um `.agua` velho de uma zona que virou seca e
				// APAGADO. Deixar o arquivo antigo seria pior que nao ter nenhum: o leitor confiaria
				// nele e o lago continuaria existindo pra colisao depois de sumir do mapa.
				if (molhadas.Count == 0)
				{
					if (File.Exists(caminho)) { File.Delete(caminho); apagados++; }
					continue;
				}

				EscreverColisao(caminho, nivel.Width, nivel.Height, molhadas);
				comAgua++;
				total += molhadas.Count;

				// ============================ RELER O QUE ACABOU DE SER ESCRITO ============================
				// Nao e paranoia: o que interessa nao e "o conversor decidiu N celulas", e "o objeto
				// que o JOGO consulta responde agua em N celulas". Sao coisas diferentes -- o
				// `.agua` passa por um cabecalho, por um bitset e por um leitor que RECUSA calado
				// quando o tamanho nao bate (`ZoneCollision.CarregarAgua`), e uma recusa calada aqui
				// seria exatamente o defeito que este projeto mais paga caro: verde na bancada, seco
				// em jogo. Entao a conta sai do `EhAgua`, celula por celula.
				//
				// A SOBREPOSICAO COM O `.col` TAMBEM SAI, porque ela e legitima e vale ser vista: uma
				// `/obj/barrier` ou uma ponte por cima do lago deixa a celula parede E agua. Parede
				// vence pra todo mundo (ver `ZoneCollision.Bloqueia`), que e o certo -- ninguem nada
				// atravessando uma cerca.
				var lido = Jandirus.Core.World.ZoneCollision.Load(File.ReadAllBytes(
					Path.Combine(outDir, nome + ".col")));
				int conferidas = 0, sobrepostas = 0;
				var relido = Jandirus.Core.World.ZoneCollision.Montar(
					nivel.Width, nivel.Height, new byte[(nivel.Width * nivel.Height + 7) / 8]);
				if (!relido.CarregarAgua(File.ReadAllBytes(caminho)))
					Console.WriteLine($"  {nome,-34} FALHOU: o .agua escrito nao volta pelo CarregarAgua");
				else
					for (int y = 0; y < nivel.Height; y++)
						for (int x = 0; x < nivel.Width; x++)
							if (relido.EhAgua(x, y))
							{
								conferidas++;
								if (lido != null && lido.BlockedCell(x, y)) sobrepostas++;
							}

				string aviso = conferidas == molhadas.Count ? "" : $"  <-- RELEU {conferidas}, DIVERGE";
				Console.WriteLine($"  {nome,-34} {molhadas.Count,8} celulas de agua"
								  + (sobrepostas > 0 ? $" ({sobrepostas} tambem parede no .col)" : "")
								  + aviso);
			}

		Console.WriteLine($"andares: {andares} | com agua: {comAgua} | celulas: {total}"
						  + (apagados > 0 ? $" | .agua apagados (zona secou): {apagados}" : ""));
	}

	/// <summary>
	/// Mapa de colisao compacto: 1 BIT por celula. Um andar de 500x500 cabe em ~31 KB, entao
	/// o servidor carrega a geometria de todas as zonas sem instanciar cena nenhuma (uma cena
	/// de 250 mil tiles no headless seria absurdo). E o mesmo dado que o cliente usa via
	/// TileMap, so que numa forma que o Core consegue ler sem tocar no Godot.
	/// Cabecalho: "JCOL" + uint16 largura + uint16 altura, depois o bitset em ordem de linha.
	/// </summary>
	/// <summary>
	/// Mapa de colisao compacto: 1 BIT por celula. Um andar de 500x500 cabe em ~31 KB, entao
	/// o servidor carrega a geometria de todas as zonas sem instanciar cena nenhuma.
	/// Cabecalho: "JCOL" + uint16 largura + uint16 altura, depois o bitset em ordem de linha.
	///
	/// As paredes chegam PRONTAS de quem desenhou a cena -- ver EscreverCena.
	/// </summary>
	private static int EscreverColisao(string caminho, int w, int h, IEnumerable<(int, int)> paredes,
									   Dictionary<(int, int), byte>? grupos = null)
	{
		var bits = new byte[(w * h + 7) / 8];
		int bloqueadas = 0;

		foreach ((int x, int y) in paredes)
		{
			if (x < 0 || y < 0 || x >= w || y >= h) continue;
			int i = y * w + x;
			bits[i >> 3] |= (byte)(1 << (i & 7));
			bloqueadas++;
		}

		using var fs = new FileStream(caminho, FileMode.Create, FileAccess.Write);
		fs.Write("JCOL"u8);
		fs.WriteByte((byte)(w & 0xFF)); fs.WriteByte((byte)(w >> 8));
		fs.WriteByte((byte)(h & 0xFF)); fs.WriteByte((byte)(h >> 8));
		fs.Write(bits);

		// O PLANO DE IDENTIDADE, so no `.vis`. Vai DEPOIS do bitset porque o leitor copia apenas
		// os bytes do bitset e ignora a cauda -- entao acrescentar aqui nao quebra nada, e o
		// `.col` do servidor continua exatamente do tamanho que era.
		if (grupos == null) return bloqueadas;
		var plano = new byte[w * h];
		foreach (((int x, int y) c, byte g) in grupos)
			if (c.x >= 0 && c.y >= 0 && c.x < w && c.y < h) plano[c.y * w + c.x] = g;
		fs.Write(plano);
		return bloqueadas;
	}

}
