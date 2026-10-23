namespace Jandirus.Core.Lingua;

/// <summary>
/// ============================ AS PALAVRAS DE COMANDO NAS DUAS LINGUAS (2026-10-22) ============================
/// O dono: *"faça o servidor aceitar também os sinônimos em inglês das palavras de comando (sim/yes, tudo/all,
/// nenhuma/none, parar/stop, ceu/heaven, inferno/hell, padrao/default, aleatorio/random) e ajuste os textos em inglês
/// pra citar as palavras em inglês"*.
///
/// Alguns verbos pedem uma PALAVRA como argumento -- o "sim" que confirma abandonar um planeta, o "tudo" de largar a
/// pilha inteira, o `Focus_Skill:nenhuma`. Com o jogo em ingles (`Client/Idioma.cs`) o aviso passou a dizer a palavra em
/// ingles, entao o servidor tem de entender as duas: quem joga em portugues continua digitando "sim", quem joga em ingles
/// digita "yes", e o mesmo servidor atende os dois. A palavra portuguesa e a CANONICA (a chave que o codigo ja comparava);
/// esta tabela diz o que mais vale no lugar dela. O "sortear" da cor do ki nao estava na lista do dono e entrou pelo mesmo
/// motivo: o aviso dele cita a palavra. (O `ceu`/`inferno` do juiz ja aceitava `heaven`/`hell` desde o porte --
/// `GameServer.Tecnicas.G8.cs` --, e a invasao a forca aceita o que comeca com "forc", o "force" incluso: ficaram como
/// estavam.)
///
/// O FUNIL E UM SO: todo verbo que compara argumento com palavra de comando pergunta aqui. A bancada `lingua` conta, no
/// fonte, que nenhum verbo voltou a comparar a palavra a mao (a comparacao direta e o jeito de uma palavra nova esquecer
/// o ingles).
/// ==============================================================================================================
/// </summary>
public static class PalavrasDeComando
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `lingua`): so a palavra portuguesa vale -- o servidor de antes, em que o "yes" que o aviso
	/// em ingles pedia caia no "nao entendi". Lido pelo <see cref="E"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool SoPortuguesDeTeste;

	/// <summary>
	/// O QUE MAIS VALE NO LUGAR DE CADA PALAVRA. A grafia acentuada e a sem acento valem as duas (o servidor ja aceitava
	/// "padrão" e "padrao"); o ingles e o do aviso traduzido -- mude os tres juntos (`Assets/Data/lingua-en.json` e a
	/// tabela `Citadas` da bancada).
	/// </summary>
	private static readonly Dictionary<string, string[]> Sinonimos = new(StringComparer.OrdinalIgnoreCase)
	{
		["sim"] = ["yes"],
		["tudo"] = ["all"],
		["nenhuma"] = ["none"],
		["parar"] = ["stop"],
		["sortear"] = ["random"],
		["padrao"] = ["padrão", "default"],
		["aleatorio"] = ["aleatório", "aleatorias", "aleatórias", "random"],
	};

	/// <summary>
	/// O QUE O JOGADOR DIGITOU E A PALAVRA DE COMANDO <paramref name="pt"/>? Na grafia dela ou num sinonimo da tabela, sem
	/// olhar a caixa nem os espacos das bordas.
	/// </summary>
	public static bool E(string digitado, string pt)
	{
		string d = digitado.Trim();
		if (string.Equals(d, pt, StringComparison.OrdinalIgnoreCase)) return true;
		if (SoPortuguesDeTeste || !Sinonimos.TryGetValue(pt, out string[]? outras)) return false;
		foreach (string o in outras)
			if (string.Equals(d, o, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}
}
