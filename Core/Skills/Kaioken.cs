using Jandirus.Core.Forms;

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
	/// (v0.4.2) O QUE IMPEDE O KAIO-KEN NUMA FORMA -- a resposta de <see cref="VetoDaForma"/>. Cada veto tem a sua frase
	/// (<see cref="FraseDoVeto"/>): e o que o jogador le quando o botao recusa e quando a forma apaga o Kaio-ken aceso.
	/// </summary>
	public enum Veto
	{
		/// <summary>A forma sustenta o Kaio-ken.</summary>
		Nenhum,
		/// <summary>O SSG (e o da linha Rose): o ki divino sobre a BASE -- a regra do dono da v0.3.0.</summary>
		Deus,
		/// <summary>Um Super Saiyajin que nao e o Blue.</summary>
		SuperSaiyajin,
		/// <summary>O Blue de quem nao e Saiyajin de classe baixa nem media.</summary>
		ClasseNoBlue,
		/// <summary>O Blue de quem ainda nao tem 50% de maestria divina.</summary>
		MaestriaNoBlue,
	}

	/// <summary>
	/// O KAIO-KEN CABE NESTA FORMA? Duas regras do dono, a de agora por cima da da v0.3.0:
	///   * *"o god n pode usar kaioken junto"* (v0.3.0) -- o SSG nao o sustenta;
	///   * *"faca com q n de pra usar kaioken nas formas de super saiyajin, somente no ssj blue pra quem e saiyajin de classe
	///     baixa e media"* (v0.4.2, 2026-10-19) -- nenhum Super Saiyajin o sustenta (<see cref="Catalogo.EhSuperSaiyajin"/>: a
	///     escada, a do futuro, as lendarias e as divinas acima do SSG), MENOS o Blue, e no Blue so o Saiyajin de classe baixa
	///     ou media (<see cref="ClasseAguentaNoBlue"/>).
	///
	/// OS 50% DE MAESTRIA DIVINA NO BLUE SAO DO DM DO DONO, e nao meus: o rework do ki divino de 2026-07-18 (`kaioken.dm:138-147`)
	/// diz *"so Normal/Low-Class com maestria 50%+ aguentam o Kaioken por cima"*, e o jogo ja PROMETE isso no aviso dos 50%
	/// (`GameServer.TickDaMaestriaDivina`). O port tinha a regra no `GameServer`, MORTA: ela perguntava pelo `Fighter.ssj` e pelo
	/// `godki.usage`, que ninguem escreve aqui (a forma mora no `EstadoDeForma`) -- o Blue acendia o Kaio-ken pra qualquer classe.
	/// O teto de x20 sobre o Blue e do servidor (`GameServer.KaiokenCapNoBlueG2`), que e quem conhece o multiplicador pedido.
	///
	/// DIVERGENCIA DECLARADA, a mesma da v0.3.0 e mais larga: no DM o Kaio-ken acende no SSG e em todo Super Saiyajin sem ki divino.
	/// Quem pergunta sao as TRES portas do servidor: o botao (recusa acender), a entrada na forma (apaga o aceso, de graca) e o
	/// tique do Kaio-ken (apaga o de quem chegou na forma por um caminho que nao passa pela entrada -- o admin, a absorcao, a saga).
	/// </summary>
	public static Veto VetoDaForma(FormaDef? d, string? classe, double maestriaDivina)
	{
		if (Catalogo.EhDeusSemSuperSaiyajin(d)) return Veto.Deus;
		if (!Catalogo.EhSuperSaiyajin(d)) return Veto.Nenhum;
		if (d!.Id != Catalogo.IdBlue) return Veto.SuperSaiyajin;
		if (!ClasseAguentaNoBlue(classe)) return Veto.ClasseNoBlue;
		return maestriaDivina < Catalogo.GodkiRoyalePct ? Veto.MaestriaNoBlue : Veto.Nenhum;
	}

	/// <summary>
	/// A CLASSE QUE AGUENTA O KAIO-KEN NO BLUE: a baixa e a media do Saiyajin -- o `Class == "Normal" || Class == "Low-Class"` do
	/// DM (`kaioken.dm:142`). O Elite fica de fora porque o caminho dele aos 50% e o Blue Evolution (o comentario do proprio DM),
	/// e o New Generation (o meio-Saiyajin que tambem chega no Blue) porque o dono disse SAIYAJIN de classe baixa e media.
	/// </summary>
	public static bool ClasseAguentaNoBlue(string? classe) => classe is "Low-Class" or "Normal";

	/// <summary>A FRASE DO VETO -- a mesma no botao que recusa, na forma que apaga o Kaio-ken aceso e no tique que o expulsa.</summary>
	public static string FraseDoVeto(Veto v, FormaDef? d) => v switch
	{
		Veto.Deus => "o Kaio-ken não se sustenta no Super Saiyajin God: o ki divino não o aguenta.",
		Veto.SuperSaiyajin => $"o Kaio-ken não se sustenta no {d?.Nome ?? "Super Saiyajin"}: em Super Saiyajin, só o Blue o aguenta "
							  + "-- e só pro Saiyajin de classe baixa ou média.",
		Veto.ClasseNoBlue => "no Blue, só o Saiyajin de classe baixa ou média aguenta o Kaio-ken.",
		Veto.MaestriaNoBlue => $"seu corpo ainda não aguenta o Kaio-ken por cima do Blue: ele pede {Catalogo.GodkiRoyalePct:0}% de maestria divina.",
		_ => "",
	};

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
				"Dentro da sua maestria: o corpo aguenta. O Kaio-ken cobra Ki, vigor e um pouco do corpo enquanto estiver aceso.",
			Risco.AcimaDaMaestria =>
				$"Acima da sua maestria: o Ki, o vigor e o corpo se gastam {razao:0.#}x mais rápido.",
			Risco.QuebraNoNocaute =>
				$"Bem acima da sua maestria ({razao:0.#}x mais dreno): se você cair nocauteado com ele aceso, o corpo se despedaça.",
			_ =>
				$"Mais que o dobro da sua maestria ({razao:0.#}x mais dreno): se o Ki acabar -- ou se você cair --, o corpo se despedaça.",
		};
	}
}
