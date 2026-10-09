using System.Text;

namespace Jandirus.Tools;

/// <summary>
/// O CATALOGO DE BLOCOS (`blocos.json`) -- os tiles que um jogador ergue, tirados do `/turf/build/*`
/// do original (`Modules/Turfs/Building/buildturfs.dm`).
///
/// ============================ O QUE A JANELA DO DM LISTA ============================
/// `selection_Window_Int(1)` (`BuildWindow.dm:103-115`): todo `typesof(/turf/build)` com `canbuild`,
/// `techskill >= techlevel` e `icon` nao nulo. O `canbuild` e 1 e o `techlevel` e nulo em TODO subtipo
/// (`buildable.dm:366-367`; nenhum redefine), entao a lista e a mesma pra todo mundo: 155 tiles.
/// ====================================================================================
///
/// ============================ O QUE FICA DE FORA, E POR QUE ============================
///   * AGUA, LAVA E CEU DE CONSTRUIR (`Water = 1`, mais `sky` e `afterlifesky`): no DM sao turfs
///     densos com um `Enter()` de nadar. Aqui a agua e uma CLASSE DE CELULA do mapa (`ClasseDeAgua`,
///     com nado, correnteza e afogamento), e nao um tile que se assenta: um "bloco de agua" seria uma
///     parede com desenho de agua.
///   * OS MOSAICOS HD (`isHD`): o icone tem 96 a 512 px e cada celula mostra um pedaco dele conforme a
///     coordenada (`autofill`, `Turfs.dm:50-55`). O catalogo guarda UM quadro por bloco.
///   * A PORTA DE QUATRO DIRECOES (`Door/Door5`, `Turfs 9.dmi`): a folha so tem estados direcionais, e
///     quem desenha porta (`Client/Porta.cs`) toca `closed`/`opening`/`open`/`closing`.
///   * O QUE NAO TEM 32x32, e o que nao tem folha convertida: sem quadro nao ha o que desenhar, e
///     bloco denso sem desenho e parede invisivel.
///   * O REPETIDO: dois tipos com o MESMO quadro e a MESMA classe (`wall6` e `fanceywall`, `stone` e
///     `stonewall`, `wooden` e `woodfloor3`, `Floor2` e `o55`) viram um so. Na paleta seriam dois
///     quadrados iguais.
/// Cada linha recusada sai no relatorio do comando, com o motivo.
/// ======================================================================================
///
/// A CLASSE VEM DA `density`: denso e parede, o resto e piso, `/turf/build/Door/*` e porta. A
/// `opacity` do tipo NAO entra -- ver o cabecalho de `Core/World/Blocos.cs` (toda parede erguida cega).
/// </summary>
public static class BlocosScanner
{
	public sealed record Linha(string Id, string Nome, string Classe, string Folha, int X, int Y, int Lado, string Arte);

	public sealed record Resultado(List<Linha> Linhas, List<(string Id, string Motivo)> Fora);

	private const string Raiz = "/turf/build/";

	public static Resultado Extrair(Dictionary<string, TurfDef> turfs, string spritesDir, string raizDoProjeto)
	{
		// O MESMO INDICE do conversor de mapa (`MapConverter.Convert`): nome da folha -> todos os
		// candidatos, porque a arvore de icones tem nomes repetidos em pastas diferentes e quem
		// desempata e o `icon_state` pedido.
		var porNome = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (string png in Directory.GetFiles(spritesDir, "*.png", SearchOption.AllDirectories))
		{
			string chave = Path.GetFileNameWithoutExtension(png);
			if (!porNome.TryGetValue(chave, out List<string>? l)) porNome[chave] = l = [];
			l.Add(png);
		}

		var linhas = new List<Linha>();
		var fora = new List<(string, string)>();
		var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var folhas = new Dictionary<string, DmiFile.Result?>(StringComparer.OrdinalIgnoreCase);

		foreach (TurfDef td in turfs.Values)
		{
			if (!td.Path.StartsWith(Raiz, StringComparison.Ordinal)) continue;
			string id = td.Path[Raiz.Length..];
			if (id.StartsWith("var/", StringComparison.Ordinal)) continue;   // `turf/build/var/...` nao e tipo

			bool porta = id.StartsWith("Door/", StringComparison.Ordinal);
			if (td.Icon == null) { fora.Add((id, "sem icone (a janela do DM tambem nao lista)")); continue; }
			if (td.IsHD) { fora.Add((id, "mosaico HD")); continue; }
			if (td.Water || id is "sky" or "afterlifesky") { fora.Add((id, "agua, lava ou ceu")); continue; }

			// A PORTA NASCE "Closed" no `New()` (`buildturfs.dm:525-554`); o tipo nao declara estado.
			string estado = porta ? "Closed" : td.IconState ?? "";
			(string? png, DmiFile.Result? meta) = Folha(td.Icon, estado, porNome, folhas);
			if (png == null || meta == null) { fora.Add((id, $"sem folha convertida pra '{td.Icon}'")); continue; }
			if (meta.IconWidth != 32 || meta.IconHeight != 32)
			{
				fora.Add((id, $"quadro de {meta.IconWidth}x{meta.IconHeight}"));
				continue;
			}

			int idx = 0, achado = -1;
			DmiState? st = null;
			foreach (DmiState s in meta.States)
			{
				if (achado < 0 && string.Equals(s.Name, estado, StringComparison.OrdinalIgnoreCase)) { achado = idx; st = s; }
				idx += Math.Max(1, s.Dirs) * Math.Max(1, s.Frames);
			}
			if (achado < 0 || st == null) { fora.Add((id, $"'{td.Icon}' nao tem o estado '{estado}'")); continue; }

			string arte = "";
			if (porta)
			{
				// OS QUATRO ESTADOS, com UMA direcao: e o que o `Porta.cs` sabe tocar.
				bool Tem(string n) => meta.States.Any(s => string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase) && s.Dirs <= 1);
				if (!Tem("Closed") || !Tem("Open") || !Tem("Opening") || !Tem("Closing"))
				{
					fora.Add((id, "porta sem os quatro estados de uma direcao"));
					continue;
				}
				arte = Res(Path.ChangeExtension(png, ".tres"), raizDoProjeto);
				if (!File.Exists(Path.ChangeExtension(png, ".tres")))
				{
					fora.Add((id, "porta sem SpriteFrames convertido"));
					continue;
				}
			}

			int cols = Math.Max(1, meta.SheetWidth / 32);
			string classe = porta ? "porta" : td.Density ? "parede" : "piso";
			string folha = Res(png, raizDoProjeto);

			if (!vistos.Add($"{classe}|{folha}|{achado}")) { fora.Add((id, "mesmo quadro e mesma classe de um anterior")); continue; }

			linhas.Add(new Linha(id, Nome(id, estado, porta), classe, folha, achado % cols, achado / cols, 32, arte));
		}

		// A ORDEM DA PALETA: parede, piso, porta -- e dentro de cada uma a ordem do DM. Estavel: o
		// numero do bloco no fio e a posicao aqui, e as duas pontas leem o mesmo arquivo.
		static int Peso(string c) => c switch { "parede" => 0, "piso" => 1, _ => 2 };
		List<Linha> ordenadas = [.. linhas.Select((l, i) => (l, i)).OrderBy(p => Peso(p.l.Classe)).ThenBy(p => p.i).Select(p => p.l)];
		return new Resultado(ordenadas, fora);
	}

	/// <summary>A folha de um icone: com mais de um arquivo do mesmo nome, ganha a que TEM o estado.</summary>
	private static (string? Png, DmiFile.Result? Meta) Folha(string icone, string estado,
		Dictionary<string, List<string>> porNome, Dictionary<string, DmiFile.Result?> lidas)
	{
		if (!porNome.TryGetValue(Path.GetFileNameWithoutExtension(icone), out List<string>? candidatos)) return (null, null);

		DmiFile.Result? Ler(string p)
		{
			if (!lidas.TryGetValue(p, out DmiFile.Result? r)) lidas[p] = r = DmiFile.Read(p);
			return r;
		}

		// A FOLHA DO PROPRIO JOGO PRIMEIRO. `Assets/Sprites/DU` guarda os icones de OUTRA base (a que
		// emprestou alguns efeitos), e ela tem `Door1..4.dmi` com a mesma arte noutra ordem de estados.
		// Na ordem do disco "DU" vem antes de "Turfs" e ganharia: o DM deste jogo compila
		// `Icons/Turfs/Objects/DoorN.dmi`, e e essa que o catalogo aponta.
		List<string> ordem = [.. candidatos.OrderBy(c => c.Replace('\\', '/').Contains("/DU/", StringComparison.Ordinal) ? 1 : 0)];

		foreach (string c in ordem)
			if (Ler(c) is { SemDescricao: false } m
				&& m.States.Any(s => string.Equals(s.Name, estado, StringComparison.OrdinalIgnoreCase)))
				return (c, m);

		return (ordem[0], Ler(ordem[0]));
	}

	private static string Res(string caminho, string raiz) =>
		"res://" + Path.GetRelativePath(raiz, caminho).Replace('\\', '/');

	/// <summary>
	/// O NOME QUE A PALETA MOSTRA. O DM mostra o ultimo no do typepath (`initial(T:name)`); metade
	/// deles e apelido de quem mapeou (`o43`, `wallz`). Onde o no e so letra-e-numero sem sentido,
	/// vale o `icon_state`, que e o que o artista escreveu ("Brick_Floor").
	/// </summary>
	private static string Nome(string id, string estado, bool porta)
	{
		string cru = id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..] : id;
		if (porta) return "Porta " + new string(cru.Where(char.IsDigit).ToArray());
		if (cru.Length <= 3 && cru[0] == 'o' && cru[1..].All(char.IsDigit) && estado.Length > 0 && !estado.All(char.IsDigit))
			cru = estado;

		var sb = new StringBuilder(cru.Replace('_', ' ').Trim());
		if (sb.Length > 0) sb[0] = char.ToUpperInvariant(sb[0]);
		return sb.ToString();
	}

	public static void Escrever(string caminho, List<Linha> linhas)
	{
		static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

		var sb = new StringBuilder("[\n");
		for (int i = 0; i < linhas.Count; i++)
		{
			Linha l = linhas[i];
			sb.Append($"  {{ \"id\": \"{Esc(l.Id)}\", \"nome\": \"{Esc(l.Nome)}\", \"classe\": \"{l.Classe}\", "
					  + $"\"folha\": \"{Esc(l.Folha)}\", \"x\": {l.X}, \"y\": {l.Y}, \"lado\": {l.Lado}, \"arte\": \"{Esc(l.Arte)}\" }}");
			sb.Append(i + 1 < linhas.Count ? ",\n" : "\n");
		}
		sb.Append("]\n");
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(caminho))!);
		File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(false));
	}
}
