using System.Globalization;
using System.Text;

namespace Jandirus.Tools;

/// <summary>Um prototipo racial extraido do DM.</summary>
public sealed class ProtoDef
{
    public string Name = "";
    public Dictionary<string, double> Stats = new(StringComparer.Ordinal);
    public Dictionary<string, double> Misc = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, double>> ClassStats = new(StringComparer.Ordinal);
    public List<(string Classe, int Chance)> ClassSpread = [];
}

/// <summary>
/// Extrai os numeros raciais direto do DM: `/datum/genetics/proto/X` com `m_stats`,
/// `misc_stats`, `class_stats` e `Class_Spread`.
///
/// POR QUE EXTRAIR EM VEZ DE DIGITAR: sao 19+ racas x ~26 stats, mais as classes. Transcrever
/// a mao erra digito e congela: mexer num numero no BYOND obrigaria a mexer aqui tambem.
/// Assim, reconverter e um comando.
///
/// O parser le literais `list("chave" = valor, ...)` com ANINHAMENTO (o class_stats e uma
/// lista de listas), equilibrando parenteses e ignorando virgula dentro de sublista.
/// </summary>
public static class DmProtoScanner
{
    public static List<ProtoDef> Scan(string codeRoot)
    {
        var saida = new List<ProtoDef>();

        foreach (string file in Directory.GetFiles(codeRoot, "*.dm", SearchOption.AllDirectories))
        {
            string texto = File.ReadAllText(file);
            int i = 0;
            while (true)
            {
                int p = texto.IndexOf("/datum/genetics/proto/", i, StringComparison.Ordinal);
                if (p < 0) break;

                // so aceita a DECLARACAO do tipo (comeco de linha), nao uma referencia no meio de codigo
                int inicioLinha = texto.LastIndexOf('\n', p) + 1;
                string prefixo = texto[inicioLinha..p].Trim();
                if (prefixo.Length > 0) { i = p + 1; continue; }

                int fimLinha = texto.IndexOf('\n', p);
                if (fimLinha < 0) fimLinha = texto.Length;
                string tipo = texto[p..fimLinha].Trim();
                string nome = tipo[(tipo.LastIndexOf('/') + 1)..].Trim();
                if (nome.Length == 0 || nome.Contains(' ')) { i = p + 1; continue; }

                // o corpo vai ate a proxima declaracao de topo
                int fimBloco = ProximoTopo(texto, fimLinha);
                string corpo = texto[fimLinha..fimBloco];

                var def = new ProtoDef { Name = nome };
                string? nomeDeclarado = LerString(corpo, "Name");
                if (!string.IsNullOrEmpty(nomeDeclarado)) def.Name = nomeDeclarado;

                LerPlano(corpo, "m_stats", def.Stats);
                LerPlano(corpo, "misc_stats", def.Misc);
                LerAninhado(corpo, "class_stats", def.ClassStats);

                var spread = new Dictionary<string, double>(StringComparer.Ordinal);
                LerPlano(corpo, "Class_Spread", spread);
                foreach ((string c, double v) in spread) def.ClassSpread.Add((c, (int)v));

                if (def.Stats.Count > 0 || def.Misc.Count > 0) saida.Add(def);
                i = fimBloco;
            }
        }

        return saida;
    }

    /// <summary>Fim do bloco: a proxima linha que comeca na coluna 0 com conteudo.</summary>
    private static int ProximoTopo(string s, int from)
    {
        int i = from;
        while (i < s.Length)
        {
            int nl = s.IndexOf('\n', i);
            if (nl < 0) return s.Length;
            int prox = nl + 1;
            if (prox < s.Length && s[prox] != '\t' && s[prox] != ' ' && s[prox] != '\r' && s[prox] != '\n')
                return prox;
            i = prox;
        }
        return s.Length;
    }

    private static string? LerString(string corpo, string chave)
    {
        int i = corpo.IndexOf(chave + " =", StringComparison.Ordinal);
        if (i < 0) i = corpo.IndexOf(chave + "=", StringComparison.Ordinal);
        if (i < 0) return null;
        int a = corpo.IndexOf('"', i);
        if (a < 0) return null;
        int b = corpo.IndexOf('"', a + 1);
        return b < 0 ? null : corpo[(a + 1)..b];
    }

    private static void LerPlano(string corpo, string chave, Dictionary<string, double> destino)
    {
        string? lista = ExtrairLista(corpo, chave);
        if (lista == null) return;
        foreach ((string k, string v) in Itens(lista))
            if (double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                destino[k] = n;
    }

    private static void LerAninhado(string corpo, string chave, Dictionary<string, Dictionary<string, double>> destino)
    {
        string? lista = ExtrairLista(corpo, chave);
        if (lista == null) return;
        foreach ((string k, string v) in Itens(lista))
        {
            string val = v.Trim();
            if (!val.StartsWith("list(", StringComparison.Ordinal)) continue;
            string dentro = val[5..^1];
            var sub = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach ((string sk, string sv) in Itens(dentro))
                if (double.TryParse(sv.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                    sub[sk] = n;
            if (sub.Count > 0) destino[k] = sub;
        }
    }

    /// <summary>Devolve o INTERIOR do `list(...)` daquela propriedade, com parenteses equilibrados.</summary>
    private static string? ExtrairLista(string corpo, string chave)
    {
        int i = 0;
        while (true)
        {
            i = corpo.IndexOf(chave, i, StringComparison.Ordinal);
            if (i < 0) return null;
            // tem que ser a propriedade, nao um sufixo de outra palavra
            bool inicioOk = i == 0 || !char.IsLetterOrDigit(corpo[i - 1]) && corpo[i - 1] != '_';
            int j = i + chave.Length;
            while (j < corpo.Length && (corpo[j] == ' ' || corpo[j] == '\t')) j++;
            if (!inicioOk || j >= corpo.Length || corpo[j] != '=') { i += chave.Length; continue; }

            int abre = corpo.IndexOf("list(", j, StringComparison.Ordinal);
            if (abre < 0) return null;
            int p = abre + 4; // no '('
            int nivel = 0;
            for (int k = p; k < corpo.Length; k++)
            {
                if (corpo[k] == '(') nivel++;
                else if (corpo[k] == ')')
                {
                    nivel--;
                    if (nivel == 0) return corpo[(p + 1)..k];
                }
            }
            return null;
        }
    }

    /// <summary>Separa `"chave" = valor` no nivel 0, respeitando sublistas e comentarios.</summary>
    private static IEnumerable<(string Chave, string Valor)> Itens(string s)
    {
        var atual = new StringBuilder();
        int nivel = 0;
        var partes = new List<string>();

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')   // comentario ate o fim da linha
            {
                int nl = s.IndexOf('\n', i);
                if (nl < 0) break;
                i = nl;
                continue;
            }
            if (c == '(') nivel++;
            if (c == ')') nivel--;
            if (c == ',' && nivel == 0) { partes.Add(atual.ToString()); atual.Clear(); continue; }
            atual.Append(c);
        }
        if (atual.Length > 0) partes.Add(atual.ToString());

        foreach (string bruta in partes)
        {
            string item = bruta.Trim();
            if (item.Length == 0) continue;
            int eq = -1, n2 = 0;
            for (int i = 0; i < item.Length; i++)
            {
                if (item[i] == '(') n2++;
                else if (item[i] == ')') n2--;
                else if (item[i] == '=' && n2 == 0) { eq = i; break; }
            }
            if (eq < 0) continue;
            string chave = item[..eq].Trim().Trim('"');
            string valor = item[(eq + 1)..].Trim();
            if (chave.Length > 0) yield return (chave, valor);
        }
    }
}
