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
	/// `pixel_x`/`pixel_y`: o desenho NAO mora no canto do tile.
	///
	/// A Research Bench e 96x64 com `pixel_x = -32` -- ela transborda um tile pra cada lado, e o
	/// tile dela e o do MEIO. Sem este par, uma construcao de tres tiles de largura aparece um
	/// tile pra direita de onde ela esta.
	///
	/// Quem le hoje e o catalogo de tecnologia (`DmTechScanner.Resolver`). O conversor de mapa
	/// ainda desenha tudo colado no tile -- os `pixel_x` do cenario ficam anotados como divida.
	/// </summary>
	public double PixelX, PixelY;

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
	/// Qual ARQUIVO de atlas este typepath acabou usando (`res://...`), depois de resolver
	/// nomes repetidos. O `Icon` e so o nome que o DM escreveu -- e ha 79 nomes que apontam
	/// pra mais de um arquivo.
	/// </summary>
	public string? Atlas;
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
		@"^(icon|icon_state|density|opacity|Water|isHD|getWidth|getHeight|pixel_x|pixel_y)\s*=\s*(.+?)\s*$",
		RegexOptions.Compiled);

	/// <summary>`Nome propriedade = valor` numa linha so -- forma que o DM aceita e o jogo usa.</summary>
	private static readonly Regex RxUmaLinha = new(
		@"^([A-Za-z_][A-Za-z0-9_/]*)\s+(icon|icon_state|density|opacity|Water|isHD|getWidth|getHeight|pixel_x|pixel_y)\s*=\s*(.+?)\s*$",
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

	private static void AplicarProp(TurfDef d, string prop, string val)
	{
		switch (prop)
		{
			case "icon": d.Icon = Unquote(val); break;
			case "icon_state": d.IconState = Unquote(val); break;
			case "density": d.Density = val.StartsWith('1'); d.DensitySet = true; break;
			case "opacity": d.Opacity = val.StartsWith('1'); d.OpacitySet = true; break;
			case "Water": d.Water = val.StartsWith('1'); d.WaterSet = true; break;
			case "isHD": d.IsHD = val.StartsWith('1'); d.IsHDSet = true; break;
			case "getWidth":
				if (int.TryParse(val, out int gw)) { d.GetWidth = gw; d.TamanhoSet = true; }
				break;
			case "getHeight":
				if (int.TryParse(val, out int gh)) { d.GetHeight = gh; d.TamanhoSet = true; }
				break;
			case "pixel_x":
				if (double.TryParse(val, System.Globalization.NumberStyles.Float,
									System.Globalization.CultureInfo.InvariantCulture, out double pxv)) d.PixelX = pxv;
				break;
			case "pixel_y":
				if (double.TryParse(val, System.Globalization.NumberStyles.Float,
									System.Globalization.CultureInfo.InvariantCulture, out double pyv)) d.PixelY = pyv;
				break;
		}
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
			if (d.Icon != null && d.IconState != null && d.DensitySet && d.OpacitySet && d.WaterSet) continue;
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
				// `isHD` mora no PAI (`/turf/HDTurfs`) e o tamanho em cada filho -- sem herdar
				// os dois, nenhum turf HD e reconhecido como tal
				if (!d.IsHDSet && pai.IsHDSet) { d.IsHD = pai.IsHD; d.IsHDSet = true; }
				if (!d.TamanhoSet && pai.TamanhoSet)
				{
					d.GetWidth = pai.GetWidth; d.GetHeight = pai.GetHeight; d.TamanhoSet = true;
				}
				d.Parent ??= p;
				if (d.Icon != null && d.IconState != null && d.DensitySet && d.OpacitySet
					&& d.WaterSet && d.IsHDSet && d.TamanhoSet) break;
			}
		}
	}

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
