using System.Text;
using System.Text.RegularExpressions;

namespace Jandirus.Tools;

/// <summary>
/// O FONTE DO DM COMO O COMPILADOR O VE: os arquivos que o `.dme` inclui, na ordem em que ele os inclui, e cada
/// linha sem comentario nenhum.
///
/// ============================ POR QUE ISTO EXISTE (dono, 2026-10-10) ============================
/// *"o catalogo pode tirar essas armas comentadas"*. Seis armas -- Axe, Staff, Spear, Club, Hammer e Cross --
/// estavam a venda na aba Tech sem existir no jogo original: o bloco delas em `Modules/Tech/Weapons.dm:2-113`
/// esta dentro de um `/* */`, e o leitor do <see cref="DmTechScanner"/> so pulava `//`. A linha `/*Sword` nao
/// parecia uma entrada e sumia; as seis de baixo dela pareciam, e entravam.
///
/// O MESMO FURO ESTRAGAVA DUAS ENTRADAS VIVAS, e essa e a parte que ninguem tinha visto: as propriedades de um
/// bloco comentado caem na entrada de CIMA. O `Books` (`Tier 1.dm:45-50`, 10 zeni e tech 10) saia com o preco e
/// o requisito do `Saibaman_Seed` comentado logo abaixo (100 zeni, tech 12), e a `Hand_Drill` (tech 10) com o
/// requisito do `Shuriken` comentado (tech 15).
///
/// Cada extrator deste pipeline le o DM por conta propria (`File.ReadAllLines` + um `StartsWith("//")`), e so o
/// de skills sabia de bloco (<see cref="DmSkillScanner.SemComentario"/>). Este leitor e o lugar unico: quem le
/// por aqui recebe o arquivo ja limpo, com o MESMO numero de linhas (o `arquivo:linha` de quem cita continua
/// certo) e a indentacao intacta.
///
/// ============================ AS REGRAS, MEDIDAS NO COMPILADOR (BYOND 516.1684) ============================
/// Nao sao as do C, e as diferencas foram medidas com `dm.exe -o` em arvores de uma pagina -- nao lidas na
/// referencia:
///
///   1. `/* */` ANINHA. Um `/* a /* b */ c */` so fecha no segundo `*/`.
///   2. DENTRO de um bloco, o `//` esconde o resto da linha -- o `*/` INCLUSIVE. `/* x // y */` continua aberto
///      na linha seguinte (o compilador acusa "end of file reached inside of comment"). E um `/*` depois do `//`
///      tambem nao conta.
///   3. STRING PROTEGE. `"http://x /* y"` e texto; idem o documento `{"..."}` (que atravessa linhas e aceita
///      aspas dentro) e a string crua `@"..."`. A expressao embutida `"a[x ? "//" : ""]b"` abre outra string
///      dentro da primeira -- por isso a pilha.
///   4. DENTRO de comentario, aspas e apostrofo nao querem dizer nada (`/* don't */` fecha normalmente).
///   5. UM `//` QUE ACABA EM `\` ENGOLE A LINHA DE BAIXO (a continuacao de linha vale dentro do comentario).
///   6. O `/*/datum/x` do DM -- o `/*` colado num typepath -- e so um bloco que abre; nao ha `*/` na abertura.
///
/// O QUE ESTE LEITOR NAO FAZ: preprocessador. `#if 0 ... #endif` tambem tira codigo do jogo, e este fonte nao
/// tem nenhum (as unicas diretivas condicionais sao as duas guardas `#ifndef TRUE`/`FALSE` de
/// `Skill Trees/mobhandlers.dm`). <see cref="Condicionais"/> lista o que houver, pra o dia em que alguem escrever uma.
/// =========================================================================================================
/// </summary>
public static class DmFonte
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `instalar-prova`): `/*` volta a ser texto qualquer -- o leitor de antes de
	/// 2026-10-10, que so entendia `//` e punha seis armas comentadas na aba Tech. Lido pela propria linha que
	/// abre o bloco, em <see cref="SemComentarios"/>.
	/// </summary>
	public static bool BlocoNaoEComentarioDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `instalar-prova`): o `.dme` deixa de mandar -- volta todo `.dm` da pasta, na ordem
	/// do disco, incluido ou nao. Lido pela propria linha do conserto, em <see cref="Arquivos"/>.
	/// </summary>
	public static bool SemDmeDeTeste;

	/// <summary>As linhas de um `.dm` sem comentario: mesmo numero de linhas, mesma indentacao.</summary>
	public static string[] Linhas(string arquivo) => SemComentarios(File.ReadAllLines(arquivo));

	/// <summary>Em que tipo de string o leitor esta. A pilha existe por causa da expressao embutida.</summary>
	private enum Modo
	{
		/// <summary>`"..."`: acaba na aspa, `\` escapa, `[` abre uma expressao.</summary>
		Texto,
		/// <summary>`{"..."}`: so acaba em `"}`, atravessa linhas.</summary>
		Documento,
		/// <summary>`[...]` dentro de uma string: codigo de novo, ate o colchete fechar.</summary>
		Embutido,
		/// <summary>`@"..."`: sem escape e sem expressao.</summary>
		Cru,
		/// <summary>`@{"..."}`.</summary>
		DocumentoCru,
		/// <summary>`'arquivo.dmi'`.</summary>
		Arquivo,
	}

	/// <summary>
	/// TIRA OS COMENTARIOS. A entrada e o arquivo INTEIRO, porque o bloco e o documento `{" "}` atravessam
	/// linhas; a saida tem uma linha por linha de entrada (a que era so comentario sai vazia).
	///
	/// A linha que so tinha comentario depois do codigo fica com os espacos que havia antes dele: quem le ja
	/// faz `TrimEnd`, e mexer no que sobrou seria mexer em string.
	/// </summary>
	public static string[] SemComentarios(IReadOnlyList<string> linhas)
	{
		var saida = new string[linhas.Count];
		var sb = new StringBuilder();
		var pilha = new List<(Modo Modo, int Colchetes)>();
		int bloco = 0;                  // profundidade de `/* */`
		bool comentarioSegue = false;   // o `//` da linha de cima acabou em `\` (regra 5)

		for (int n = 0; n < linhas.Count; n++)
		{
			string l = linhas[n];
			sb.Clear();

			if (comentarioSegue)
			{
				comentarioSegue = l.EndsWith('\\');
				saida[n] = "";
				continue;
			}

			int i = 0;
			while (i < l.Length)
			{
				char c = l[i];
				char p = i + 1 < l.Length ? l[i + 1] : '\0';

				// ---- dentro de um bloco comentado: so tres coisas importam (regras 1, 2 e 4)
				if (bloco > 0)
				{
					if (c == '/' && p == '/') break;
					if (c == '/' && p == '*') { bloco++; i += 2; continue; }
					if (c == '*' && p == '/') { bloco--; i += 2; continue; }
					i++;
					continue;
				}

				Modo? modo = pilha.Count > 0 ? pilha[^1].Modo : null;

				// ---- dentro de uma string: nada e comentario (regra 3)
				if (modo is Modo.Texto or Modo.Documento)
				{
					if (c == '\\') { sb.Append(c); if (p != '\0') sb.Append(p); i += 2; continue; }
					if (c == '[') { pilha.Add((Modo.Embutido, 1)); sb.Append(c); i++; continue; }
					if (modo == Modo.Texto && c == '"') { pilha.RemoveAt(pilha.Count - 1); sb.Append(c); i++; continue; }
					if (modo == Modo.Documento && c == '"' && p == '}')
					{ pilha.RemoveAt(pilha.Count - 1); sb.Append("\"}"); i += 2; continue; }
					sb.Append(c); i++;
					continue;
				}
				if (modo == Modo.Cru)
				{
					if (c == '"') pilha.RemoveAt(pilha.Count - 1);
					sb.Append(c); i++;
					continue;
				}
				if (modo == Modo.DocumentoCru)
				{
					if (c == '"' && p == '}') { pilha.RemoveAt(pilha.Count - 1); sb.Append("\"}"); i += 2; continue; }
					sb.Append(c); i++;
					continue;
				}
				if (modo == Modo.Arquivo)
				{
					if (c == '\'') pilha.RemoveAt(pilha.Count - 1);
					sb.Append(c); i++;
					continue;
				}

				// ---- codigo: no topo do arquivo, ou dentro de uma expressao embutida numa string
				if (modo == Modo.Embutido)
				{
					if (c == '[') pilha[^1] = (Modo.Embutido, pilha[^1].Colchetes + 1);
					else if (c == ']')
					{
						if (pilha[^1].Colchetes <= 1) pilha.RemoveAt(pilha.Count - 1);
						else pilha[^1] = (Modo.Embutido, pilha[^1].Colchetes - 1);
						sb.Append(c); i++;
						continue;
					}
				}
				else
				{
					// SO NO TOPO HA COMENTARIO. O `//` mata o resto da linha (e a de baixo, se esta acaba em
					// `\`); o `/*` abre o bloco -- e e esta a linha que o leitor antigo nao tinha.
					if (c == '/' && p == '/') { comentarioSegue = l.EndsWith('\\'); break; }
					if (c == '/' && p == '*' && !BlocoNaoEComentarioDeTeste) { bloco = 1; i += 2; continue; }
				}

				if (c == '@' && p == '"') { pilha.Add((Modo.Cru, 0)); sb.Append("@\""); i += 2; continue; }
				if (c == '@' && p == '{' && i + 2 < l.Length && l[i + 2] == '"')
				{ pilha.Add((Modo.DocumentoCru, 0)); sb.Append("@{\""); i += 3; continue; }
				if (c == '{' && p == '"') { pilha.Add((Modo.Documento, 0)); sb.Append("{\""); i += 2; continue; }
				if (c == '"') { pilha.Add((Modo.Texto, 0)); sb.Append(c); i++; continue; }
				if (c == '\'') { pilha.Add((Modo.Arquivo, 0)); sb.Append(c); i++; continue; }
				sb.Append(c); i++;
			}

			// FIM DA LINHA. So o documento atravessa; a string comum, a crua e o literal de arquivo acabam aqui
			// (com tudo o que estiver aberto por cima deles), a nao ser que a linha termine em `\`.
			if (pilha.Count > 0 && !l.EndsWith('\\'))
			{
				int presa = pilha.FindIndex(q => q.Modo is Modo.Texto or Modo.Cru or Modo.Arquivo);
				if (presa >= 0) pilha.RemoveRange(presa, pilha.Count - presa);
			}

			saida[n] = sb.ToString();
		}
		return saida;
	}

	private static readonly Regex RxInclude = new("^\\s*#\\s*include\\s+\"(?<a>[^\"]+\\.dm)\"\\s*$",
												  RegexOptions.Compiled | RegexOptions.IgnoreCase);

	/// <summary>
	/// OS `.dm` QUE O COMPILADOR LE, NA ORDEM EM QUE LE: os `#include` do `.dme` que mora ao lado de
	/// <paramref name="pastaCode"/>, filtrados pros que estao dentro dela.
	///
	/// ============================ DOIS MOTIVOS, OS DOIS MEDIDOS ============================
	/// A ORDEM DECIDE QUEM VENCE. O DM aceita o mesmo tipo declarado em dois arquivos (e UM tipo), e a propriedade
	/// escrita nos dois fica com o valor do que foi incluido DEPOIS -- medido rodando um mundo de uma pagina. O
	/// `PDA` esta em `Tech/Camera.dm:16` e em `Tech/Tier 1.dm:66`; o `DNA_Container` em `Cloning Machine.dm:170`
	/// (100.000 zeni, tech 60) e em `Tech/DNALabs.dm:271` (50.000, tech 50, so Humano) -- e o segundo de cada par
	/// e o que o jogo usa.
	///
	/// E O QUE NAO ESTA NO `.dme` NAO E DO JOGO. Ha 19 `.dm` em `Code/` que nenhum `#include` cita (`Magic/charms`,
	/// `materials`, `runes`, `vancian` e os quinze de `Skills/CustomAttacks`): codigo morto de outro jeito.
	///
	/// SEM `.dme` AO LADO (ou com mais de um, ou com um que nao cita nada desta pasta) devolve todo `.dm` da
	/// pasta, na ordem do disco -- que e o que as arvores de mentira das bancadas tem.
	/// ======================================================================================
	/// </summary>
	public static List<string> Arquivos(string pastaCode)
	{
		string raiz = Path.GetFullPath(pastaCode);
		var todos = Directory.GetFiles(raiz, "*.dm", SearchOption.AllDirectories).ToList();

		string? pai = Path.GetDirectoryName(raiz.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (pai == null) return todos;
		string[] dmes = Directory.GetFiles(pai, "*.dme");
		if (dmes.Length != 1 || SemDmeDeTeste) return todos;

		var noDisco = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string a in todos) noDisco[a] = a;

		var doDme = new List<string>();
		var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string linha in Linhas(dmes[0]))
		{
			if (RxInclude.Match(linha) is not { Success: true } m) continue;
			string alvo = Path.GetFullPath(Path.Combine(pai, m.Groups["a"].Value.Replace('\\', Path.DirectorySeparatorChar)));
			if (noDisco.TryGetValue(alvo, out string? achado) && vistos.Add(achado)) doDme.Add(achado);
		}
		return doDme.Count > 0 ? doDme : todos;
	}

	/// <summary>Os `.dm` da pasta que o `.dme` NAO inclui. Vazio quando <see cref="Arquivos"/> caiu na reserva.</summary>
	public static List<string> ForaDoDme(string pastaCode)
	{
		var incluidos = new HashSet<string>(Arquivos(pastaCode), StringComparer.OrdinalIgnoreCase);
		return [.. Directory.GetFiles(Path.GetFullPath(pastaCode), "*.dm", SearchOption.AllDirectories)
						   .Where(a => !incluidos.Contains(a))];
	}

	private static readonly Regex RxCondicional = new(@"^\s*#\s*(if|ifdef|ifndef|elif|else)\b.*$", RegexOptions.Compiled);

	/// <summary>
	/// AS DIRETIVAS CONDICIONAIS DO FONTE (`#if`, `#ifdef`, `#ifndef`, `#elif`, `#else`), com arquivo e linha.
	/// Este leitor NAO as avalia -- ver o cabecalho da classe. A lista existe pra quem roda o `espelho` ver, em
	/// voz alta, se o original ganhou um `#if 0`.
	/// </summary>
	public static List<(string Arquivo, int Linha, string Texto)> Condicionais(string pastaCode)
	{
		var l = new List<(string, int, string)>();
		foreach (string arq in Arquivos(pastaCode))
		{
			string[] linhas = Linhas(arq);
			for (int i = 0; i < linhas.Length; i++)
				if (RxCondicional.IsMatch(linhas[i])) l.Add((arq, i + 1, linhas[i].Trim()));
		}
		return l;
	}

	/// <summary>
	/// ESCREVE O ESPELHO: a pasta de codigo como o compilador a ve -- so os arquivos do `.dme`, cada um sem
	/// comentario, no mesmo caminho relativo e com o mesmo numero de linhas -- em <paramref name="destino"/>.
	///
	/// ============================ PRA QUE SERVE ============================
	/// E o modo de comparacao dos extratores. Cada um le o DM por conta propria e tem (ou nao) o furo do
	/// comentario de bloco; em vez de mexer em todos pra descobrir o que mudaria, roda-se o MESMO extrator duas
	/// vezes -- no original e no espelho -- com a saida numa pasta de rascunho, e compara-se os dois arquivos. O
	/// que diferir e o que aquele extrator tira de codigo que o jogo original nao compila.
	///
	/// E e tambem a prova deste leitor: o espelho compilado pelo `dm.exe -o` tem de dar a MESMA arvore de objetos
	/// do original, linha por linha (medido em 2026-10-10: identica).
	///
	/// Em Latin-1 nos dois sentidos, e nao no UTF-8 do `File.ReadAllLines`: o leitor so olha caracteres ASCII, e
	/// assim cada byte que nao e comentario chega igual do outro lado.
	/// ======================================================================
	/// </summary>
	public static (int Escritos, int ForaDoDme) Espelhar(string pastaCode, string destino)
	{
		string raiz = Path.GetFullPath(pastaCode);
		List<string> arquivos = Arquivos(raiz);
		foreach (string arq in arquivos)
		{
			string saida = Path.Combine(destino, Path.GetRelativePath(raiz, arq));
			Directory.CreateDirectory(Path.GetDirectoryName(saida)!);
			// O `\r` SAI ANTES E VOLTA DEPOIS: o leitor pergunta "a linha acaba em `\`?", e num arquivo CRLF ela
			// acabaria em `\r`. Devolvido no fim, o espelho sai com as quebras do original.
			string[] cruas = File.ReadAllText(arq, Encoding.Latin1).Split('\n');
			var comCr = new bool[cruas.Length];
			for (int i = 0; i < cruas.Length; i++)
				if (comCr[i] = cruas[i].EndsWith('\r')) cruas[i] = cruas[i][..^1];
			string[] limpas = SemComentarios(cruas);
			for (int i = 0; i < limpas.Length; i++)
				if (comCr[i]) limpas[i] += "\r";
			File.WriteAllText(saida, string.Join('\n', limpas), Encoding.Latin1);
		}
		return (arquivos.Count, ForaDoDme(raiz).Count);
	}
}
