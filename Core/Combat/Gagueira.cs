using System.Text;

namespace Jandirus.Core.Combat;

/// <summary>
/// A GAGUEIRA DE QUEM FALA CAIDO COM O TRONCO FERIDO (dono, 2026-10-27, o pedido 119): *"se vc tentar falar caido com torso ferido o
/// jogo vai fazer vc gagejar no jogo e falar de forma tipo: vc digita "socorro" e o chat vai escrever algo como "s-ssocor-roo""*.
///
/// Palavra a palavra, com os tres tropecos do exemplo do dono: o COMECO que gagueja ("s-s"), a QUEBRA no meio ("socor-ro") e o FIM
/// que se arrasta ("roo"). Quanto mais ferido o tronco, mais palavras tropecam e mais tropecos cada uma leva. O sorteio sai da propria
/// fala (o mesmo texto com o mesmo ferimento sai igual -- e o que deixa a bancada ler o resultado), e so as letras mudam: pontuacao,
/// numeros e espacos passam como estao. Quem decide QUANDO e o servidor (`GameServer.Falar`: caido, fala digitada de boca).
///
/// (2026-10-27, o pedido 127) A CABECA FERIDA TAMBEM -- o dono: *"dano na cabeça deveria tb fazer o chat gagejar"*, e a escolha dele: so
/// caido, igual ao tronco. Com as duas feridas vale a PIOR (<see cref="FracaoQueFala"/>).
/// </summary>
public static class Gagueira
{
	/// <summary>Abaixo disto o tronco esta FERIDO -- a faixa "Ferido" da aba Corpo (`MenuJogo.PalavraDeFerimento`) comeca em 79%.</summary>
	public const double TroncoFerido = 0.80;

	/// <summary>Um tronco nesta fracao de vida (0..1) faz gaguejar?</summary>
	public static bool Gagueja(double fracaoDoTronco) => fracaoDoTronco < TroncoFerido;

	/// <summary>(2026-10-27, o pedido 127) As partes que, feridas, fazem gaguejar: o tronco (o 119) e a cabeca.</summary>
	public static readonly string[] PartesQueFalam = ["Torso", "Cabeca"];

	/// <summary>
	/// (2026-10-27, o pedido 127) A FRACAO QUE FALA deste corpo: a pior das <see cref="PartesQueFalam"/> (a decepada vale zero; a que o corpo nao
	/// tem nao conta). E o numero que o <see cref="Gaguejar"/> recebe no `GameServer.Falar`.
	/// </summary>
	public static double FracaoQueFala(Body corpo)
	{
		double pior = 1;
		foreach (string nome in PartesQueFalam)
			if (corpo.Achar(nome) is { } parte) pior = Math.Min(pior, parte.Decepado ? 0 : parte.Fracao);
		return pior;
	}

	/// <summary>
	/// A FALA COM A GAGUEIRA, pra um tronco nesta fracao de vida. Fora da faixa ferida o texto volta igual. Toda fala que tem uma palavra
	/// de duas letras ou mais tropeca em pelo menos uma -- quem le tem que perceber.
	/// </summary>
	public static string Gaguejar(string texto, double fracaoDoTronco)
	{
		if (!Gagueja(fracaoDoTronco) || string.IsNullOrEmpty(texto)) return texto;

		// 0 no comeco da faixa ferida, 1 no tronco quebrado (a linha do nocaute)
		double forca = Math.Clamp((TroncoFerido - fracaoDoTronco) / (TroncoFerido - Regras.LimiarQuebra), 0, 1);
		ulong s = Semente(texto);

		var palavras = new List<(int Ini, int Fim)>();
		for (int i = 0; i < texto.Length;)
		{
			if (!char.IsLetter(texto[i])) { i++; continue; }
			int ini = i;
			while (i < texto.Length && char.IsLetter(texto[i])) i++;
			palavras.Add((ini, i));
		}

		// QUAIS TROPECAM: a chance cresce com o ferimento; se o sorteio poupar todas, a primeira que da tropeca
		var tropecam = new HashSet<int>();
		for (int k = 0; k < palavras.Count; k++)
			if (palavras[k].Fim - palavras[k].Ini >= 2 && Sorteio(ref s) < 0.45 + 0.55 * forca) tropecam.Add(k);
		if (tropecam.Count == 0)
		{
			int primeira = palavras.FindIndex(p => p.Fim - p.Ini >= 2);
			if (primeira < 0) return texto;
			tropecam.Add(primeira);
		}

		var sb = new StringBuilder(texto.Length * 2);
		int feito = 0;
		for (int k = 0; k < palavras.Count; k++)
		{
			(int ini, int fim) = palavras[k];
			sb.Append(texto, feito, ini - feito);
			string palavra = texto[ini..fim];
			sb.Append(tropecam.Contains(k) ? Tropecar(palavra, forca, ref s) : palavra);
			feito = fim;
		}
		sb.Append(texto, feito, texto.Length - feito);
		return sb.ToString();
	}

	/// <summary>
	/// UMA PALAVRA TROPECADA: o comeco que gagueja sempre ("socorro" -> "s-ssocorro"), a quebra antes de uma consoante da segunda metade
	/// ("socor-ro") e o fim que se arrasta na ultima vogal ("roo") -- os dois ultimos pelo sorteio, mais provaveis quanto pior o tronco.
	/// </summary>
	private static string Tropecar(string palavra, double forca, ref ulong s)
	{
		string miolo = palavra;

		// A QUEBRA: so em palavra de cinco letras ou mais, antes de uma consoante que nao seja a primeira nem a ultima letra
		if (miolo.Length >= 5 && Sorteio(ref s) < 0.5 + 0.5 * forca)
		{
			var lugares = new List<int>();
			for (int i = miolo.Length / 2; i < miolo.Length - 1; i++)
				if (!EhVogal(miolo[i])) lugares.Add(i);
			if (lugares.Count > 0)
			{
				int onde = lugares[(int)(Proximo(ref s) % (ulong)lugares.Count)];
				miolo = miolo[..onde] + "-" + miolo[onde..];
			}
		}

		// O FIM QUE SE ARRASTA: a ultima letra, se for vogal, repetida
		if (EhVogal(miolo[^1]) && Sorteio(ref s) < 0.4 + 0.6 * forca) miolo += miolo[^1];

		// O COMECO QUE GAGUEJA: a primeira letra, um hifen, e ela de novo colada na palavra -- "s-ssocorro"
		char primeira = palavra[0];
		return primeira + "-" + char.ToLowerInvariant(primeira) + (char.IsUpper(primeira) ? char.ToLowerInvariant(miolo[0]) + miolo[1..] : miolo);
	}

	private static bool EhVogal(char c) => "aeiouáéíóúâêôãõàAEIOUÁÉÍÓÚÂÊÔÃÕÀ".IndexOf(c) >= 0;

	/// <summary>FNV-1a de 64 bits da fala -- fixo entre processos (o `string.GetHashCode` do .NET muda a cada execucao). A <see cref="FalaSemSentido"/> sorteia por ele tambem.</summary>
	internal static ulong Semente(string texto)
	{
		ulong h = 14695981039346656037UL;
		foreach (char c in texto) { h ^= c; h *= 1099511628211UL; }
		return h | 1;
	}

	/// <summary>O proximo numero do xorshift.</summary>
	internal static ulong Proximo(ref ulong s)
	{
		s ^= s << 13;
		s ^= s >> 7;
		s ^= s << 17;
		return s;
	}

	/// <summary>Um sorteio em [0, 1).</summary>
	internal static double Sorteio(ref ulong s) => (Proximo(ref s) >> 11) * (1.0 / (1UL << 53));
}
