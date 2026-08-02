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
		@"^(icon|icon_state|density|opacity)\s*=\s*(.+?)\s*$", RegexOptions.Compiled);

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
				string val = m.Groups[2].Value.Trim();
				switch (m.Groups[1].Value)
				{
					case "icon": d.Icon = Unquote(val); break;
					case "icon_state": d.IconState = Unquote(val); break;
					case "density": d.Density = val.StartsWith('1'); d.DensitySet = true; break;
					case "opacity": d.Opacity = val.StartsWith('1'); d.OpacitySet = true; break;
				}
				continue;
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
			if (d.Icon != null && d.IconState != null && d.DensitySet && d.OpacitySet) continue;
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
				d.Parent ??= p;
				if (d.Icon != null && d.IconState != null && d.DensitySet && d.OpacitySet) break;
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
