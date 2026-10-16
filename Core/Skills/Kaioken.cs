namespace Jandirus.Core.Skills;

/// <summary>
/// **O KAIO-KEN EM NUMEROS** -- a tabela do multiplicador e as medidas do risco, numa casa so. O servidor cobra por elas
/// (`GameServer.Tecnicas.G2`) e a tela de escolha as escreve antes de acender (`Client/TelaDeKaioken`): duas copias da
/// mesma tabela seriam a tela prometendo um poder e o servidor entregando outro.
///
/// ============================ TUDO LITERAL DE `kaioken.dm` ============================
///   * o multiplicador escolhido ("x20") e o TIER, e nao o poder: `KaiokenMult` (`:11-19`) -- x2 = 1,2x, x20 = 1,75x,
///     x100 = 4x;
///   * o pedido vira um inteiro de 2 a 100 (`SetKaioken`, `:148-150`: o piso de 2, o `round()` -- que no DM e PISO -- e o
///     teto de 100);
///   * a maestria de quem destrava a tecnica e 4 (o `1` da `mob/var`, `:3`, mais os `+3` do `after_learn`, `:93`);
///   * acima da maestria TUDO drena na razao `vezes / maestria` -- o Ki, o folego e a propria carne (o `Loop` do buff,
///     `:190-192`); dentro dela a carne ainda paga um piso (`max(razao * 3, 1)`);
///   * o Ki acabando com o multiplicador acima do DOBRO da maestria despedaca o corpo (`:199-202`), e cair nocauteado acima
///     de 1,6x a maestria tambem (`KaiokenRevert`, `:239-242`).
///
/// (v0.3.0) Mudou-se pra ca quando a tela passou a deixar ESCOLHER o multiplicador (o dono: *"n ta dando pra escolher o
/// multiplicador"*) -- ate entao so o servidor precisava da tabela.
/// ======================================================================================
/// </summary>
public static class Kaioken
{
	public const int VezesMinimas = 2, VezesMaximas = 100;

	/// <summary>A maestria de quem destrava a tecnica: o `1` da `mob/var` mais os `+3` do `after_learn` (`kaioken.dm:93`).</summary>
	public const double MaestriaInicial = 4;

	/// <summary>Acima de quantas vezes a maestria o Ki acabando despedaca o corpo (`kaioken.dm:199`).</summary>
	public const double RazaoQueEstouraSemKi = 2.0;

	/// <summary>Acima de quantas vezes a maestria cair NOCAUTEADO despedaca o corpo (`kaioken.dm:239`).</summary>
	public const double RazaoQueEstouraNoNocaute = 1.6;

	/// <summary>
	/// (v0.3.0) O KAIO-KEN CABE NESTA FORMA? Nao no Deus -- regra do dono, *"o god n pode usar kaioken junto"*.
	///
	/// DIVERGENCIA DECLARADA: no DM o `SetKaioken` so barra o ki divino COM Super Saiyajin por baixo (`kaioken.dm:141-147`,
	/// o teto x20 do Blue), e o SSG acende o Kaio-ken sem teto nenhum. Quem cobra aqui sao o botao (o servidor recusa
	/// acender no Deus) e a entrada na forma (virar Deus apaga o Kaio-ken aceso) -- os dois em `GameServer`.
	/// </summary>
	public static bool CabeNaForma(Jandirus.Core.Forms.FormaDef? d) => !Jandirus.Core.Forms.Catalogo.EhDeusSemSuperSaiyajin(d);

	/// <summary>A TABELA DO MULTIPLICADOR REAL DE PODER, literal de `kaioken.dm:11-19`.</summary>
	public static double Multiplicador(double vezes)
	{
		if (vezes <= 2) return 1.2;
		if (vezes <= 3) return 1.2 + (vezes - 2) * 0.1;
		if (vezes <= 4) return 1.3 + (vezes - 3) * 0.1;
		if (vezes <= 10) return 1.4 + (vezes - 4) * 0.1 / 6;
		if (vezes <= 20) return 1.5 + (vezes - 10) * 0.25 / 10;
		if (vezes <= 50) return 1.75 + (vezes - 20) * 0.25 / 30;
		if (vezes <= 100) return 2.0 + (vezes - 50) * 2.0 / 50;
		return 4.0;
	}

	/// <summary>
	/// O PEDIDO VIRA UM MULTIPLICADOR VALIDO: o piso de 2, o inteiro por baixo e o teto de 100 (`SetKaioken`,
	/// `kaioken.dm:148-150` -- o `round()` de um argumento no DM e PISO, e nao arredondamento).
	/// </summary>
	public static int Valido(double pedido) =>
		(int)Math.Clamp(Math.Floor(Math.Max(pedido, VezesMinimas)), VezesMinimas, VezesMaximas);

	/// <summary>O QUE O MULTIPLICADOR CUSTA CONTRA A MAESTRIA -- os quatro degraus do `Loop` e do `KaiokenRevert`.</summary>
	public enum Risco
	{
		/// <summary>Dentro da maestria: o preco de sempre.</summary>
		NaMaestria,
		/// <summary>Acima dela: tudo drena na razao `vezes / maestria`.</summary>
		AcimaDaMaestria,
		/// <summary>Acima de 1,6x: cair nocauteado com ele aceso despedaca o corpo.</summary>
		QuebraNoNocaute,
		/// <summary>Acima do dobro: o Ki acabando tambem.</summary>
		QuebraSemKi,
	}

	public static Risco RiscoDe(int vezes, double maestria)
	{
		double m = Math.Max(maestria, 0.01);
		return vezes > m * RazaoQueEstouraSemKi ? Risco.QuebraSemKi
			: vezes > m * RazaoQueEstouraNoNocaute ? Risco.QuebraNoNocaute
			: vezes > m ? Risco.AcimaDaMaestria
			: Risco.NaMaestria;
	}

	/// <summary>A FRASE DO RISCO, como a tela de escolha a escreve embaixo do multiplicador.</summary>
	public static string FraseDoRisco(int vezes, double maestria)
	{
		double razao = vezes / Math.Max(maestria, 0.01);
		return RiscoDe(vezes, maestria) switch
		{
			Risco.NaMaestria =>
				"Dentro da sua maestria: o corpo aguenta. O Kaio-ken cobra Ki, vigor e um pouco da carne enquanto estiver aceso.",
			Risco.AcimaDaMaestria =>
				$"Acima da sua maestria: o Ki, o vigor e a carne vão embora {razao:0.#}x mais rápido.",
			Risco.QuebraNoNocaute =>
				$"Bem acima da sua maestria ({razao:0.#}x mais dreno): se você cair nocauteado com ele aceso, o corpo se despedaça.",
			_ =>
				$"Mais que o dobro da sua maestria ({razao:0.#}x mais dreno): se o Ki acabar -- ou se você cair --, o corpo se despedaça.",
		};
	}
}
