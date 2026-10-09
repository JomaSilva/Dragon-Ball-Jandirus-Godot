namespace Jandirus.Tools;

/// <summary>
/// QUAL ARQUIVO O DM QUER DIZER COM `icon = 'Lab.dmi'` -- e onde a copia dele mora em `Assets/Sprites`.
///
/// ============================ O NOME NAO BASTA ============================
/// O DM escreve so o nome do arquivo; quem acha a pasta e a lista de `#define FILE_DIR` do `.dme`.
/// O tileset do port acha a folha pelo MESMO nome, sem pasta -- e `Assets/Sprites` tem tambem a
/// arvore `DU/`, de outro jogo, com arquivos de mesmo nome. Onde a `DU/` ganhou, o mapa pinta o
/// desenho de outro jogo: `/obj/Raw_Material/Quartz` (`White rock.dmi`, um minerio de 32x32) saia com
/// a `DU/Map/white rock.png`, uma rocha de 250x250 -- tres rochas de oito tiles em Icer.
///
/// QUAL DOS ARQUIVOS DE MESMO NOME DENTRO DO PROPRIO JOGO: o primeiro, na ordem dos FILE_DIR, que TEM
/// o estado pedido; se nenhum tem, o primeiro. SUPOSICAO DECLARADA: a ordem em que o compilador do
/// BYOND resolve nome repetido nao foi medida no engine. O que se sabe e que `Namek.dmi` existe em
/// `Icons/Character Icons/Namekians` (o boneco, sem os estados das casas) e em `Icons/Turfs` (as
/// casas), e que o jogo original mostra casas.
/// ==========================================================================
/// </summary>
internal sealed class IconesDoDm
{
	private readonly string _raiz;
	private readonly Dictionary<string, List<string>> _porNome = new(StringComparer.OrdinalIgnoreCase);

	/// <param name="dmmDir">A pasta `Maps` do original: o `.dme` e as pastas dos FILE_DIR ficam ao lado dela.</param>
	internal IconesDoDm(string dmmDir)
	{
		_raiz = Directory.GetParent(Path.GetFullPath(dmmDir))?.FullName ?? "";
		string? dme = _raiz.Length == 0 ? null : Directory.GetFiles(_raiz, "*.dme").FirstOrDefault();
		if (dme == null) return;

		foreach (string linha in File.ReadAllLines(dme))
		{
			string l = linha.Trim();
			if (!l.StartsWith("#define FILE_DIR", StringComparison.Ordinal)) continue;
			string pasta = Path.Combine(_raiz, l["#define FILE_DIR".Length..].Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar));
			if (!Directory.Exists(pasta)) continue;
			foreach (string arq in Directory.GetFiles(pasta).OrderBy(a => a, StringComparer.OrdinalIgnoreCase))
			{
				string nome = Path.GetFileName(arq);
				if (!_porNome.TryGetValue(nome, out List<string>? l2)) _porNome[nome] = l2 = [];
				if (!l2.Contains(arq, StringComparer.OrdinalIgnoreCase)) l2.Add(arq);
			}
		}
	}

	/// <summary>O arquivo do original que este `icon` + `icon_state` usa, ou nulo se o nome nao existe em FILE_DIR nenhum.</summary>
	internal string? Resolver(string icone, string estado)
	{
		string limpo = icone.Replace('\\', '/');
		if (limpo.Contains('/'))
		{
			string direto = Path.Combine(_raiz, limpo.Replace('/', Path.DirectorySeparatorChar));
			return File.Exists(direto) ? direto : null;
		}
		if (!_porNome.TryGetValue(limpo, out List<string>? candidatos)) return null;
		return candidatos.FirstOrDefault(c => DmiFile.Read(c) is { } m && m.States.Any(s => s.Name == estado))
			   ?? candidatos[0];
	}

	/// <summary>
	/// ONDE A COPIA DESTE ARQUIVO MORA (ou moraria) DENTRO DE `Assets/Sprites`, relativo a ela: o mesmo
	/// caminho sem o `Icons/` da frente e com `.png` no fim -- a regra com que as folhas do jogo foram
	/// copiadas (`Icons/Turfs/Namek.dmi` -> `Turfs/Namek.png`). O que esta fora de `Icons/` (as
	/// arvores soltas de `Images/`) guarda o caminho inteiro.
	/// </summary>
	internal string Espelho(string arquivoDoDm)
	{
		string rel = Path.GetRelativePath(_raiz, arquivoDoDm).Replace('\\', '/');
		if (rel.StartsWith("Icons/", StringComparison.OrdinalIgnoreCase)) rel = rel["Icons/".Length..];
		return Path.ChangeExtension(rel, ".png");
	}
}
