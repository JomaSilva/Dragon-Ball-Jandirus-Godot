using System.Text.RegularExpressions;

namespace Jandirus.Tools;

/// <summary>O que um turf precisa ter pra virar tile: aparencia e se bloqueia passagem.</summary>
public sealed class TurfDef
{
	public string Path = "";
	public string? Icon;        // arquivo .dmi
	public string? IconState;   // estado dentro do .dmi (vazio = estado "")
	public bool Density;        // 1 = parede
	public bool Opacity;        // 1 = bloqueia luz (vira LightOccluder no Godot)
	public string? Parent;

	// "declarou de verdade?" -- sem isto nao da pra distinguir `density = 0` (escrito) de
	// "nunca falou de densidade" (herda do pai). Era o que fazia TODA parede do jogo passar
	// batido: `/turf/Wall` declara density=1 e os 18 filhos so trocam o icon_state.
	public bool DensitySet;
	public bool OpacitySet;

	/// <summary>
	/// `Water = 1` -- A TERCEIRA CLASSE DE CELULA, e o dado que este scanner jogava fora.
	///
	/// A agua do original nao e `density` (ela e 0 em todo turf de agua de mapa): quem para o pe e
	/// o `Enter()`, que chama o `testWaters()` (`Swim.dm:26-38`) e deixa passar quem voa, quem
	/// nada, quem esta de barco e quem esta sendo arremessado. A UNICA marca que distingue esses
	/// turfs no codigo e esta flag, declarada em `Turfs.dm:41` e ligada em 38 typepaths.
	///
	/// Sem ela o conversor nao tinha por onde reconhecer 1,36 milhao de celulas de agua nos 26
	/// andares -- a informacao estava no `.dm`, era lida linha a linha, e era descartada aqui.
	/// Quem interpreta a flag (e quem tira o CEU de dentro dela) e o <see cref="Aguas"/>.
	/// </summary>
	public bool Water;
	public bool WaterSet;

	/// <summary>
	/// `destroyable = 0` -- ESTA CELULA NAO SE QUEBRA, e o campo que o port inteiro nao extraia.
	///
	/// ============================ POR QUE ELE E O CAMPO QUE FALTAVA ============================
	/// A resistencia padrao de todo turf e VINTE (`buildable.dm:353-360`), ou seja praticamente tudo
	/// cede ao primeiro soco de qualquer um. Quem protege um pedaco do mapa no original NAO e a
	/// resistencia: e este booleano, e ele e conferido num funil unico -- `turf/proc/Destroy()`
	/// (`Modules/Turfs/NewTurfs.dm:2-4`), que abre com `if(src.destroyable)` e simplesmente NAO FAZ
	/// NADA quando ele e zero.
	///
	/// E dai vem a queixa do dono, na letra: `/turf/Other/Blank` (`Turfs.dm:69-77`) e denso, nao tem
	/// `icon` NENHUM -- invisivel no BYOND tambem -- e declara `destroyable = 0`. No original socar
	/// esse vazio toca o som e levanta poeira (as duas coisas acontecem ANTES do `Destroy()`, em
	/// `attack_proc.dm:99-105`) e a celula NAO cai. No port ela caia: viravam terra batida, a colisao
	/// abria, e dava pra andar pra dentro do nada. "Soco, quebra, faz todos os efeitos, e nao tinha
	/// nada la" e exatamente a diferenca entre ter e nao ter esta linha.
	///
	/// SAO 688.418 CELULAS nos 40 andares, 1.702 delas encostando em chao pisavel.
	/// ==========================================================================================
	///
	/// HERDA, e sem isso ele nao serve pra nada: `turf/Teleporters` declara `destroyable = 0` uma
	/// vez e os 20 e poucos filhos so trocam o icone -- a mesma armadilha que o `DensitySet` la em
	/// cima descreve. O padrao e `turf/var/destroyable = 1` (`Turfs.dm:199`), entao "nao declarou"
	/// significa DESTRUTIVEL, e por isso o par de campos existe.
	/// </summary>
	public bool Destroyable = true;
	public bool DestroyableSet;

	/// <summary>
	/// `pixel_x`/`pixel_y`: o desenho NAO mora no canto do tile.
	///
	/// A Research Bench e 96x64 com `pixel_x = -32` -- ela transborda um tile pra cada lado, e o
	/// tile dela e o do MEIO. Sem este par, uma construcao de tres tiles de largura aparece um
	/// tile pra direita de onde ela esta.
	///
	/// Quem le: o catalogo de tecnologia (`DmTechScanner.Resolver`) e o conversor de mapa, que tira
	/// daqui a ancora do tile (`MapConverter.AncoraDoTipo`).
	///
	/// HERDA, como toda variavel do DM: os rifts (`/obj/Rift/GodKi_Enter_Rift`) nao escrevem nada e
	/// saem centrados na celula porque `/obj/Rift` declara `pixel_x = -34` e `pixel_y = -34`
	/// (`Rifts.dm:19-20`). O par `...Set` diz se o valor foi DECLARADO -- zero tambem e um valor.
	/// </summary>
	public double PixelX, PixelY;
	public bool PixelXSet, PixelYSet;

	/// <summary>
	/// TURF HD: o desenho nao e UM tile, e um MOSAICO montado por coordenada.
	///
	/// O `autofill()` do original (`Turfs.dm:50-57`) escreve o icon_state em RUNTIME:
	/// `"[x % (getWidth/32)],[y % (getHeight/32)]"`, ou `"[x&1],[y&1]"` quando nao ha tamanho.
	/// Como isso mora num corpo de proc, o scanner nao ve -- e sem estes tres campos o tile
	/// sai sempre o mesmo, o quadro 0, em 22% da Terra. E a grama toda igual e errada.
	/// </summary>
	public bool IsHD;
	public int GetWidth, GetHeight;
	public bool IsHDSet, TamanhoSet;

	/// <summary>
	/// `dir` -- PRA ONDE O DESENHO OLHA, e o campo que fazia toda borda de penhasco sair virada pro sul.
	///
	/// As 24 arestas (`/obj/barrier/Edges/Edge1N`..`Edge6S`, `barrier.dm:92-211`) sao SEIS desenhos, e
	/// nao vinte e quatro: cada estado da folha (`"1"`..`"6"`) tem quatro direcoes, e o que separa a
	/// `Edge2N` da `Edge2W` e so esta linha. Sem ela o conversor pintava o primeiro quadro (o SUL) em
	/// todas -- 857 arestas viradas em Vegeta, 10.596 nos quatro mapas.
	///
	/// O valor e o numero do BYOND (<see cref="Direcoes"/>); o padrao e SUL (`dir var (atom)` da
	/// referencia do DM). HERDA como os outros campos: ver o `Resolve`.
	/// </summary>
	public int Dir = Direcoes.Sul;
	public bool DirSet;

	/// <summary>
	/// `layer` -- A ALTURA EM QUE O BYOND DESENHA ESTE TIPO. So interessa onde o `.dmm` EMPILHA dois
	/// turfs na mesma celula: o de baixo vira underlay do de cima, e underlay desenha na camada DELE
	/// (ver o `Por` do `MapConverter`). `/turf/decor` declara `layer=4` (`Turfs.dm:1657`) e por isso
	/// uma mesa listada ANTES do piso aparece POR CIMA dele.
	/// </summary>
	public double Layer;
	public bool LayerSet;

	/// <summary>
	/// A camada que vale: a declarada (ou herdada), senao o padrao do BYOND pro tipo -- TURF_LAYER 2,
	/// OBJ_LAYER 3 (`layer var (atom)` da referencia do DM).
	/// </summary>
	public double Camada => LayerSet ? Layer : Path.StartsWith("/turf", StringComparison.Ordinal) ? 2 : 3;

	/// <summary>
	/// Qual ARQUIVO de atlas este typepath acabou usando (`res://...`), depois de resolver
	/// nomes repetidos. O `Icon` e so o nome que o DM escreveu -- e ha 79 nomes que apontam
	/// pra mais de um arquivo.
	/// </summary>
	public string? Atlas;

	/// <summary>
	/// A MESMA FICHA COM AS VARIAVEIS QUE UMA INSTANCIA DO MAPA TROCA (`Teleporter{icon = '...';
	/// icon_state = "13"}`). E uma copia: a ficha do tipo continua intacta pra quem nao as troca. O
	/// `Atlas` volta a nulo porque o icone pode ter mudado -- quem resolve e o conversor, de novo.
	/// </summary>
	public TurfDef ComVariaveisDeInstancia(IReadOnlyDictionary<string, string> vars)
	{
		var copia = (TurfDef)MemberwiseClone();
		copia.Atlas = null;
		foreach (string campo in DmTurfScanner.VariaveisDeAparencia)
			if (vars.TryGetValue(campo, out string? valor)) DmTurfScanner.AplicarProp(copia, campo, valor);
		copia.PixelX += Passo(vars, "step_x");
		copia.PixelY += Passo(vars, "step_y");
		return copia;
	}

	private static double Passo(IReadOnlyDictionary<string, string> vars, string campo) =>
		vars.TryGetValue(campo, out string? v)
		&& double.TryParse(v.Trim(), System.Globalization.NumberStyles.Float,
						   System.Globalization.CultureInfo.InvariantCulture, out double passo) ? passo : 0;
}

/// <summary>
/// AS DIRECOES DO BYOND, e onde cada uma mora dentro de um estado do `.dmi`.
///
/// O numero e o do motor (`NORTH` 1, `SOUTH` 2, `EAST` 4, `WEST` 8; as diagonais sao a soma). A ORDEM
/// DENTRO DA FOLHA e outra coisa, e e fixa: sul, norte, leste, oeste, sudeste, sudoeste, nordeste,
/// noroeste -- um estado de 4 direcoes guarda as quatro primeiras, um de 8 guarda todas.
/// </summary>
public static class Direcoes
{
	public const int Norte = 1, Sul = 2, Leste = 4, Oeste = 8;

	private static readonly int[] OrdemNaFolha = [Sul, Norte, Leste, Oeste, Sul | Leste, Sul | Oeste, Norte | Leste, Norte | Oeste];

	/// <summary>`NORTH`, `SOUTHWEST`... ou o numero puro, que e como o `.dmm` escreve (`dir = 4`). Nulo = nao e direcao.</summary>
	public static int? Ler(string valor) => valor.Trim() switch
	{
		"NORTH" => Norte,
		"SOUTH" => Sul,
		"EAST" => Leste,
		"WEST" => Oeste,
		"NORTHEAST" => Norte | Leste,
		"NORTHWEST" => Norte | Oeste,
		"SOUTHEAST" => Sul | Leste,
		"SOUTHWEST" => Sul | Oeste,
		string s when int.TryParse(s, out int n) && Array.IndexOf(OrdemNaFolha, n) >= 0 => n,
		_ => null,
	};

	/// <summary>
	/// QUANTOS QUADROS ADIANTE do primeiro esta a direcao pedida, num estado com
	/// <paramref name="dirsDoEstado"/> direcoes. Estado sem direcao (1) so tem um desenho, e ele vale
	/// pra todas.
	///
	/// DEVOLVE -1 QUANDO O ESTADO NAO TEM A DIRECAO (uma diagonal num estado de 4). O BYOND escolhe ai
	/// "a mais proxima da orientacao anterior" (`dir var (atom)`), que um mapa parado nao tem; quem
	/// chama decide o que pintar e CONTA o caso. Nenhum dos quatro mapas chega aqui hoje.
	/// </summary>
	public static int NaFolha(int dir, int dirsDoEstado)
	{
		if (dirsDoEstado <= 1) return 0;
		int i = Array.IndexOf(OrdemNaFolha, dir);
		return i >= 0 && i < dirsDoEstado ? i : -1;
	}
}

/// <summary>
/// Le a arvore de tipos DM pra descobrir COMO cada turf se parece.
///
/// O .dmm so guarda o TYPEPATH de cada celula ("/turf/Other/Stars"); a aparencia mora no
/// codigo. Como a arvore do DM e por INDENTACAO (igual Python), da pra reconstruir os
/// caminhos completos sem compilar nada.
///
/// DUAS ARMADILHAS que este scanner trata:
///   1) HERANCA: `/turf/Other/Stars_Exit` herda o icon de `/turf/Other/Stars`? NAO -- herda
///      do PAI na arvore de tipos (`/turf/Other`). A resolucao sobe o typepath ate achar
///      quem define a propriedade.
///   2) CORPO DE PROC: `New()` dentro de um turf costuma fazer `icon_state = "..."` com
///      valor dinamico. Isso NAO e a aparencia do tipo. Toda subarvore de um identificador
///      que termina em "(...)" e ignorada.
/// </summary>
public static class DmTurfScanner
{
	private static readonly Regex RxProp = new(
		@"^(icon|icon_state|dir|layer|density|opacity|Water|destroyable|isHD|getWidth|getHeight|pixel_x|pixel_y)\s*=\s*(.+?)\s*$",
		RegexOptions.Compiled);

	/// <summary>`Nome propriedade = valor` numa linha so -- forma que o DM aceita e o jogo usa.</summary>
	private static readonly Regex RxUmaLinha = new(
		@"^([A-Za-z_][A-Za-z0-9_/]*)\s+(icon|icon_state|dir|layer|density|opacity|Water|destroyable|isHD|getWidth|getHeight|pixel_x|pixel_y)\s*=\s*(.+?)\s*$",
		RegexOptions.Compiled);

	/// <summary>
	/// O QUE UMA INSTANCIA DO `.dmm` PODE TROCAR NA APARENCIA do tipo. Dos 19 nomes de variavel que os
	/// quatro mapas escrevem entre chaves, os tres primeiros escolhem QUAL desenho a celula mostra e os
	/// dois ultimos ONDE ele cai (os outros sao destino de teleporte, nome, berco...). O `step_x`/
	/// `step_y` -- o micro-ondas dez pixels acima do tampo da mesa -- tambem desloca o desenho, e SOMA
	/// no par. Quem os aplica e o <see cref="TurfDef.ComVariaveisDeInstancia"/>.
	/// </summary>
	internal static readonly string[] VariaveisDeAparencia = ["icon", "icon_state", "dir", "pixel_x", "pixel_y"];

	/// <summary>A instancia do mapa troca alguma coisa que muda o desenho ou onde ele cai?</summary>
	internal static bool MudaAparencia(IReadOnlyDictionary<string, string> vars) =>
		VariaveisDeAparencia.Any(vars.ContainsKey) || vars.ContainsKey("step_x") || vars.ContainsKey("step_y");

	/// <summary>`layer = MOB_LAYER+1`: uma das constantes do `stddef.dm`, com ou sem soma.</summary>
	private static readonly Regex RxCamada = new(
		@"^(AREA_LAYER|TURF_LAYER|OBJ_LAYER|MOB_LAYER|FLY_LAYER|\d+(?:\.\d+)?)\s*(?:([+-])\s*(\d+(?:\.\d+)?))?$",
		RegexOptions.Compiled);

	public static Dictionary<string, TurfDef> Scan(string codeRoot)
	{
		var defs = new Dictionary<string, TurfDef>(StringComparer.Ordinal);

		foreach (string file in Directory.GetFiles(codeRoot, "*.dm", SearchOption.AllDirectories))
			ScanFile(file, defs);

		Resolve(defs);
		return defs;
	}

	private static void ScanFile(string file, Dictionary<string, TurfDef> defs)
	{
		string[] linhas = File.ReadAllLines(file);
		// pilha de (indentacao, caminho acumulado, dentroDeProc)
		var pilha = new List<(int Indent, string Path, bool InProc)>();

		foreach (string bruta in linhas)
		{
			if (bruta.TrimStart().StartsWith("//")) continue;
			string sem = bruta.TrimEnd();
			if (sem.Trim().Length == 0) continue;

			int indent = 0;
			while (indent < sem.Length && (sem[indent] == '\t' || sem[indent] == ' ')) indent++;
			string conteudo = sem[indent..];

			// corta comentario de fim de linha (fora de string)
			int c = IndexOfComment(conteudo);
			if (c >= 0) conteudo = conteudo[..c].TrimEnd();
			if (conteudo.Length == 0) continue;

			while (pilha.Count > 0 && pilha[^1].Indent >= indent) pilha.RemoveAt(pilha.Count - 1);

			bool paiEmProc = pilha.Count > 0 && pilha[^1].InProc;
			string paiPath = pilha.Count > 0 ? pilha[^1].Path : "";

			// propriedade?
			Match m = RxProp.Match(conteudo);
			if (m.Success && !paiEmProc && Interessa(paiPath))
			{
				TurfDef d = Get(defs, paiPath);
				AplicarProp(d, m.Groups[1].Value, m.Groups[2].Value.Trim());
				continue;
			}

			// NOME E PROPRIEDADE NA MESMA LINHA: `TileWhite icon='White.dmi'`.
			//
			// O DM aceita declarar o tipo e uma propriedade dele numa linha so, e o jogo usa
			// isso bastante. Sem tratar, o typepath simplesmente NAO EXISTE pro conversor --
			// foi por isso que a Sala do Tempo saiu VAZIA: o chao dela e
			// `/turf/Tile/TileWhite`, declarado exatamente assim, e 248 mil celulas nao
			// tinham tipo pra desenhar.
			if (!paiEmProc && !conteudo.Contains('('))
			{
				Match uma = RxUmaLinha.Match(conteudo);
				if (uma.Success)
				{
					string nome = uma.Groups[1].Value.TrimEnd('/');
					string full1 = nome.StartsWith('/') ? nome
						: (paiPath.Length > 0 ? paiPath + "/" + nome : "/" + nome);

					if (Interessa(full1))
					{
						TurfDef d1 = Get(defs, full1);
						AplicarProp(d1, uma.Groups[2].Value, uma.Groups[3].Value.Trim());
					}
					// entra na pilha: o tipo pode ter mais propriedades indentadas embaixo
					pilha.Add((indent, full1, false));
					continue;
				}
			}

			// proc / verb / bloco de controle: empilha marcado como "dentro de proc"
			bool ehProc = conteudo.Contains('(') || conteudo.StartsWith("var", StringComparison.Ordinal)
					   || conteudo.StartsWith("if", StringComparison.Ordinal)
					   || conteudo.StartsWith("for", StringComparison.Ordinal)
					   || conteudo.Contains('=');
			if (ehProc || paiEmProc)
			{
				pilha.Add((indent, paiPath, true));
				continue;
			}

			// fragmento de typepath
			string frag = conteudo.Trim();
			if (frag.EndsWith('/')) frag = frag.TrimEnd('/');
			string full = frag.StartsWith('/') ? frag : (paiPath.Length > 0 ? paiPath + "/" + frag : "/" + frag);
			pilha.Add((indent, full, false));

			if (Interessa(full)) Get(defs, full);
		}
	}

	internal static void AplicarProp(TurfDef d, string prop, string val)
	{
		switch (prop)
		{
			case "icon": d.Icon = Unquote(val); break;
			case "icon_state": d.IconState = Unquote(val); break;
			// valor que nao e direcao (uma conta, uma variavel) nao e a direcao do TIPO: fica o que havia
			case "dir":
				if (Direcoes.Ler(val) is { } dir) { d.Dir = dir; d.DirSet = true; }
				break;
			// idem a camada: `MOB_LAYER + HAIR_LAYER` e conta de overlay de mob, nao altura de cenario
			case "layer":
				if (LerCamada(val) is { } camada) { d.Layer = camada; d.LayerSet = true; }
				break;
			case "density": d.Density = val.StartsWith('1'); d.DensitySet = true; break;
			case "opacity": d.Opacity = val.StartsWith('1'); d.OpacitySet = true; break;
			case "Water": d.Water = val.StartsWith('1'); d.WaterSet = true; break;
			// `destroyable = 0` e a UNICA forma que aparece no jogo (o padrao ja e 1), mas o teste e
			// pelo valor e nao pelo zero: `destroyable = 1` escrito num filho pra desfazer o `0` do
			// pai e legitimo no DM, e ler so o zero deixaria esse filho indestrutivel de mentira.
			case "destroyable": d.Destroyable = !val.StartsWith('0'); d.DestroyableSet = true; break;
			case "isHD": d.IsHD = val.StartsWith('1'); d.IsHDSet = true; break;
			case "getWidth":
				if (int.TryParse(val, out int gw)) { d.GetWidth = gw; d.TamanhoSet = true; }
				break;
			case "getHeight":
				if (int.TryParse(val, out int gh)) { d.GetHeight = gh; d.TamanhoSet = true; }
				break;
			case "pixel_x":
				if (double.TryParse(val, System.Globalization.NumberStyles.Float,
									System.Globalization.CultureInfo.InvariantCulture, out double pxv))
				{
					d.PixelX = pxv; d.PixelXSet = true;
				}
				break;
			case "pixel_y":
				if (double.TryParse(val, System.Globalization.NumberStyles.Float,
									System.Globalization.CultureInfo.InvariantCulture, out double pyv))
				{
					d.PixelY = pyv; d.PixelYSet = true;
				}
				break;
		}
	}

	/// <summary>O valor de um `layer = ...` do DM. Nulo quando a conta usa nome que nao e do `stddef.dm`.</summary>
	private static double? LerCamada(string val)
	{
		Match m = RxCamada.Match(val.Trim());
		if (!m.Success) return null;

		double b = m.Groups[1].Value switch
		{
			"AREA_LAYER" => 1,
			"TURF_LAYER" => 2,
			"OBJ_LAYER" => 3,
			"MOB_LAYER" => 4,
			"FLY_LAYER" => 5,
			string n => double.Parse(n, System.Globalization.CultureInfo.InvariantCulture),
		};
		if (!m.Groups[3].Success) return b;

		double soma = double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
		return m.Groups[2].Value == "-" ? b - soma : b + soma;
	}

	/// <summary>
	/// Uma celula do .dmm cita turfs E objetos ("/obj/barrier/...", "/obj/Trees/PineTree"),
	/// e no BYOND quem tem `density` bloqueia passagem seja qual for. Ignorar `/obj` fazia
	/// arvore, cerca e barreira virarem cenario atravessavel.
	/// </summary>
	private static bool Interessa(string path) =>
		path.StartsWith("/turf", StringComparison.Ordinal) || path.StartsWith("/obj", StringComparison.Ordinal);

	/// <summary>Herda icon/icon_state/densidade/opacidade do ancestral mais proximo que define.</summary>
	private static void Resolve(Dictionary<string, TurfDef> defs)
	{
		foreach (TurfDef d in defs.Values)
		{
			if (Completa(d)) continue;
			string p = d.Path;
			while (true)
			{
				int barra = p.LastIndexOf('/');
				if (barra <= 0) break;
				p = p[..barra];
				if (!defs.TryGetValue(p, out TurfDef? pai)) continue;
				d.Icon ??= pai.Icon;
				d.IconState ??= pai.IconState;
				if (!d.DensitySet && pai.DensitySet) { d.Density = pai.Density; d.DensitySet = true; }
				if (!d.OpacitySet && pai.OpacitySet) { d.Opacity = pai.Opacity; d.OpacitySet = true; }
				// A AGUA HERDA, e e por isso que ela precisa passar por aqui: `/turf/Water` liga a
				// flag e os 15 filhos dele (Water1..13, WaterFall, WaterReal) so trocam o icone --
				// exatamente a armadilha que o comentario do `DensitySet` la em cima descreve.
				if (!d.WaterSet && pai.WaterSet) { d.Water = pai.Water; d.WaterSet = true; }
				// O `destroyable` HERDA pelo mesmo motivo, e e onde ele ganha alcance: `turf/Teleporters`
				// e `turf/Arena` declaram uma vez e dezenas de filhos so trocam o icone. Sem esta linha
				// so o pai (que nao aparece em mapa nenhum) ficaria protegido.
				if (!d.DestroyableSet && pai.DestroyableSet)
				{
					d.Destroyable = pai.Destroyable; d.DestroyableSet = true;
				}
				// `isHD` mora no PAI (`/turf/HDTurfs`) e o tamanho em cada filho -- sem herdar
				// os dois, nenhum turf HD e reconhecido como tal
				if (!d.IsHDSet && pai.IsHDSet) { d.IsHD = pai.IsHD; d.IsHDSet = true; }
				if (!d.TamanhoSet && pai.TamanhoSet)
				{
					d.GetWidth = pai.GetWidth; d.GetHeight = pai.GetHeight; d.TamanhoSet = true;
				}
				// A DIRECAO E A CAMADA HERDAM como o resto: `/turf/decor` declara `layer=4` uma vez
				// (`Turfs.dm:1657`) e as mesas, plantas e pedras dele so trocam o icone.
				if (!d.DirSet && pai.DirSet) { d.Dir = pai.Dir; d.DirSet = true; }
				if (!d.LayerSet && pai.LayerSet) { d.Layer = pai.Layer; d.LayerSet = true; }
				if (!d.PixelXSet && pai.PixelXSet) { d.PixelX = pai.PixelX; d.PixelXSet = true; }
				if (!d.PixelYSet && pai.PixelYSet) { d.PixelY = pai.PixelY; d.PixelYSet = true; }
				d.Parent ??= p;
				if (Completa(d)) break;
			}
		}
	}

	/// <summary>
	/// Nao falta herdar mais nada? UMA pergunta pras duas saidas do laco acima (pular o tipo, parar de
	/// subir): com duas listas, um campo novo entra numa e esquece a outra, e o tipo que cair na
	/// lista curta deixa de herdar esse campo sem aviso nenhum.
	/// </summary>
	private static bool Completa(TurfDef d) =>
		d.Icon != null && d.IconState != null && d.DensitySet && d.OpacitySet && d.WaterSet
		&& d.DestroyableSet && d.IsHDSet && d.TamanhoSet && d.DirSet && d.LayerSet && d.PixelXSet && d.PixelYSet;

	private static TurfDef Get(Dictionary<string, TurfDef> defs, string path)
	{
		if (!defs.TryGetValue(path, out TurfDef? d))
		{
			d = new TurfDef { Path = path };
			defs[path] = d;
		}
		return d;
	}

	private static string Unquote(string s)
	{
		s = s.Trim();
		if (s.Length >= 2 && (s[0] == '\'' || s[0] == '"') && s[^1] == s[0]) return s[1..^1];
		return s;
	}

	/// <summary>Acha o "//" que inicia comentario, ignorando o que esta dentro de aspas.</summary>
	private static int IndexOfComment(string s)
	{
		char aspa = '\0';
		for (int i = 0; i < s.Length - 1; i++)
		{
			char ch = s[i];
			if (aspa != '\0') { if (ch == aspa) aspa = '\0'; continue; }
			if (ch is '"' or '\'') { aspa = ch; continue; }
			if (ch == '/' && s[i + 1] == '/') return i;
		}
		return -1;
	}
}
