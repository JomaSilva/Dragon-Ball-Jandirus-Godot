namespace Jandirus.Core;

/// <summary>
/// A VERSAO DO JOGO -- a fonte UNICA dela.
///
/// O dono (2026-10-11): *"uma coisa q falta no jogo, versionamento, o jogo poderia ter no menu mostrando qual
/// versao ele ta, e ao entrar em um server o server checa a versao do cliente / cliente checa a versao do
/// server, e se n bater o jogo manda pro menu avisando q o cliente ta em alguma versao na frente ou atras do
/// server"*.
///
/// ============================ O QUE SAI DAQUI ============================
///   * o TEXTO que o jogador le no canto da primeira tela e da pausa (<see cref="Texto"/>, "0.1.0");
///   * o NUMERO ordenavel que viaja no fio e diz quem esta na frente (<see cref="Numero"/>);
///   * a COMPARACAO (<see cref="Comparar"/>) e as FRASES do aviso, com a direcao -- ATRASADO ou ADIANTADO.
///
/// Mora no `Core` porque as duas pontas leem o MESMO arquivo: o servidor pra recusar, o cliente pra avisar.
/// O que e do fio (o pedido de conexao, a carga da recusa, a impressao digital do protocolo) esta em
/// `Net/FioDaVersao.cs`; quem recusa e `Server/GameServer.Versao.cs`; quem avisa e `Client/GameClient.Versao.cs`
/// + `Client/Boot.Versao.cs`.
/// ==========================================================================
/// </summary>
public static class Versao
{
	// =========================================================================================================
	//   >>>>>>>>>>   E AQUI QUE SE SOBE A VERSAO: A LINHA `Maior, Menor, Remendo` LOGO ABAIXO, E SO ELA.   <<<<<<<<<<
	//
	//   QUANDO SUBIR: toda vez que um build novo for pra RUA -- pros jogadores ou pro servidor dedicado.
	//   Cliente e servidor so conversam com os TRES numeros iguais, entao subir a versao e o que faz o jogo
	//   de quem nao atualizou voltar ao menu com a frase "o seu jogo esta ATRASADO", em vez de entrar e
	//   quebrar no meio (pacote lido torto, botao que o servidor nao entende).
	//
	//     Remendo (o 3o numero): conserto, ajuste, atualizacao pequena.               0.1.0 -> 0.1.1
	//     Menor   (o 2o numero): sistema ou conteudo novo; o remendo volta a zero.    0.1.4 -> 0.2.0
	//     Maior   (o 1o numero): lancamento, ou mudanca que deixa save velho pra tras. 0.9.2 -> 1.0.0
	//
	//   Cada parte vai de 0 a 999. O texto do menu, o numero do fio e as frases saem TODOS desta linha.
	//
	//   A UNICA COPIA e o `config/version` do `project.godot` (e o que o EXPORT carimba nas propriedades do
	//   .exe): troque la tambem. Se as duas divergirem o servidor avisa no log ao subir, e a bancada
	//   `--versaoteste` reprova.
	//
	//   E SE EU ESQUECER DE SUBIR? Ha uma segunda trava, automatica: a impressao digital do protocolo
	//   (`FioDaVersao.DigitalDoProtocolo`). Opcode novo, constante de layout mudada ou campo novo num estado
	//   do fio mudam a digital sozinhos, e ai o aviso e "mesma versao, mas builds diferentes". Ela NAO ve
	//   regra do `Core` nem campo novo escrito a mao dentro de um pacote -- pra isso, so subindo a versao.
	// =========================================================================================================
	public const int Maior = 0, Menor = 2, Remendo = 0;

	/// <summary>Teto de cada uma das tres partes. Tres digitos por parte: o numero ordenavel cabe folgado num `int`.</summary>
	public const int TetoDaParte = 999;

	/// <summary>
	/// O NUMERO ORDENAVEL: maior x 1.000.000 + menor x 1.000 + remendo. E ELE que viaja no fio, e so ele: o texto
	/// da outra ponta e refeito do numero (<see cref="TextoDe"/>), entao nenhuma string vinda de fora chega a
	/// tela nem ao log.
	/// </summary>
	public const int Numero = Maior * 1_000_000 + Menor * 1_000 + Remendo;

	/// <summary>O maior numero que ainda e uma versao: 999.999.999.</summary>
	public const int NumeroMaximo = TetoDaParte * 1_000_000 + TetoDaParte * 1_000 + TetoDaParte;

	/// <summary>O TEXTO que o jogador le: "0.1.0".</summary>
	public static readonly string Texto = TextoDe(Numero);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--versaoteste`): a comparacao volta a olhar SO o numero -- o jogo sem a
	/// impressao digital do protocolo, em que dois builds diferentes com a versao esquecida no mesmo numero
	/// conversam ate o primeiro pacote torto. Lido pela propria regra, em <see cref="Comparar"/>.
	/// </summary>
	public static bool DigitalForaDaContaDeTeste;

	/// <summary>O texto de um numero de versao ("0.3.12"). Numero que nao e versao (negativo, acima do teto) sai "?".</summary>
	public static string TextoDe(int numero) =>
		numero < 0 || numero > NumeroMaximo ? "?" : $"{numero / 1_000_000}.{numero / 1_000 % 1_000}.{numero % 1_000}";

	/// <summary>
	/// O inverso de <see cref="TextoDe"/>: "0.3.12" -> 3012. Aceita SO tres numeros de um a tres digitos
	/// separados por ponto -- e o formato que o `config/version` do `project.godot` tem de ter (o export do
	/// Godot so carimba o .exe com versao feita de numeros e pontos).
	/// </summary>
	public static bool TentarLer(string? texto, out int numero)
	{
		numero = 0;
		string[] partes = (texto ?? "").Trim().Split('.');
		if (partes.Length != 3) return false;
		int soma = 0;
		foreach (string p in partes)
		{
			if (p.Length is 0 or > 3 || !p.All(char.IsAsciiDigit)) return false;
			soma = soma * 1_000 + int.Parse(p);
		}
		numero = soma;
		return true;
	}

	/// <summary>Em que o jogo (o cliente) e o servidor desencontram -- sempre DO PONTO DE VISTA DO JOGO.</summary>
	public enum Desencontro : byte
	{
		/// <summary>Mesma versao e mesmo protocolo: entra.</summary>
		Nenhum = 0,
		/// <summary>O servidor esta numa versao MAIOR: quem tem de atualizar e o jogador.</summary>
		JogoAtrasado = 1,
		/// <summary>O servidor esta numa versao MENOR: quem tem de atualizar e o servidor.</summary>
		JogoAdiantado = 2,
		/// <summary>Mesmo numero, protocolo diferente: dois builds distintos, e a versao nao subiu entre eles.</summary>
		OutroBuild = 3,
	}

	/// <summary>
	/// A REGRA INTEIRA: primeiro o numero (ele da a DIRECAO), depois a impressao digital do protocolo (ela pega
	/// o build que mudou sem a versao subir). As duas pontas chamam esta funcao -- o servidor pra recusar o
	/// pedido de conexao, o cliente pra conferir a versao que o servidor mandou.
	/// </summary>
	public static Desencontro Comparar(IdentidadeDeBuild jogo, IdentidadeDeBuild servidor)
	{
		if (jogo.Numero < servidor.Numero) return Desencontro.JogoAtrasado;
		if (jogo.Numero > servidor.Numero) return Desencontro.JogoAdiantado;
		if (jogo.Digital != servidor.Digital && !DigitalForaDaContaDeTeste) return Desencontro.OutroBuild;
		return Desencontro.Nenhum;
	}

	/// <summary>
	/// O AVISO que o jogador le quando a versao nao bate: o titulo do painel, a frase inteira (com as duas
	/// versoes e a direcao) e um resumo de uma linha (o que fica na linha de estado do menu, depois do OK).
	/// </summary>
	public readonly record struct Aviso(string Titulo, string Frase, string Resumo);

	/// <summary>
	/// O aviso de um desencontro entre o jogo e o servidor; nulo quando os dois batem. O TEXTO E DO JOGADOR
	/// (com acento); a direcao vai em maiusculas porque e a unica palavra que diz QUEM tem de atualizar.
	/// </summary>
	public static Aviso? AvisoDoDesencontro(IdentidadeDeBuild jogo, IdentidadeDeBuild servidor)
	{
		string j = jogo.Texto, s = servidor.Texto;
		return Comparar(jogo, servidor) switch
		{
			Desencontro.JogoAtrasado => new Aviso(
				"VERSÃO DIFERENTE DA DO SERVIDOR",
				$"Este servidor está na versão {s} e o seu jogo na {j}: o seu jogo está ATRASADO — atualize o jogo.",
				$"seu jogo ({j}) está ATRASADO: o servidor está na {s}"),
			Desencontro.JogoAdiantado => new Aviso(
				"VERSÃO DIFERENTE DA DO SERVIDOR",
				$"Este servidor está na versão {s} e o seu jogo na {j}: o seu jogo está ADIANTADO — o servidor ainda não foi atualizado.",
				$"seu jogo ({j}) está ADIANTADO: o servidor está na {s}"),
			Desencontro.OutroBuild => new Aviso(
				"BUILD DIFERENTE DO DO SERVIDOR",
				$"Este servidor e o seu jogo estão na mesma versão ({j}), mas são builds diferentes (o protocolo mudou): "
				+ $"o do servidor é {servidor.DigitalCurta} e o do seu jogo é {jogo.DigitalCurta} — atualize os dois para o mesmo build.",
				$"mesma versão ({j}), mas builds diferentes (o protocolo mudou)"),
			_ => null,
		};
	}

	/// <summary>
	/// O aviso de quando o servidor ACEITA a conexao e nunca diz a versao dele: e um servidor de antes desta
	/// checagem (ele so confere a senha de entrada). Sem numero do outro lado nao ha direcao pra dizer -- so
	/// que e o servidor que ficou pra tras.
	/// </summary>
	public static Aviso AvisoDeServidorSemVersao(IdentidadeDeBuild jogo) => new(
		"SERVIDOR SEM VERSÃO",
		$"Este servidor não informou a versão dele: é um servidor antigo, de antes da checagem de versão. "
		+ $"O seu jogo está na {jogo.Texto} — o servidor precisa ser atualizado.",
		$"servidor sem versão (antigo); o seu jogo está na {jogo.Texto}");

	/// <summary>
	/// FNV-1a de 64 bits sobre o UTF-8 do texto. E a conta da impressao digital do protocolo, e mora aqui
	/// porque tem de dar o MESMO numero em toda maquina e em todo processo: o `GetHashCode` de string do .NET
	/// e sorteado a cada execucao, e duas pontas comparando o resultado dele nunca bateriam.
	/// </summary>
	public static ulong Fnv1a64(string texto)
	{
		ulong h = 14695981039346656037UL;
		foreach (byte b in System.Text.Encoding.UTF8.GetBytes(texto))
		{
			h ^= b;
			h *= 1099511628211UL;
		}
		return h;
	}
}

/// <summary>
/// O QUE UM BUILD DIZ DE SI no aperto de mao: o numero ordenavel da versao (<see cref="Versao.Numero"/>) e a
/// impressao digital do protocolo dele. Doze bytes no fio; o texto ("0.1.0") e refeito do numero em cada ponta.
/// </summary>
public readonly record struct IdentidadeDeBuild(int Numero, ulong Digital)
{
	/// <summary>A versao como o jogador le: "0.1.0".</summary>
	public string Texto => Versao.TextoDe(Numero);

	/// <summary>Os oito primeiros digitos hexadecimais da digital: o bastante pra distinguir dois builds a olho.</summary>
	public string DigitalCurta => Digital.ToString("x16")[..8];
}
