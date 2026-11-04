using System.Globalization;

namespace Jandirus.Core.World;

/// <summary>
/// ============================ OS CUSTOS DO OUTRO MUNDO SAO DO SERVIDOR (2026-11-01, o pedido 213) ============================
/// O dono: *"E faca custo de reviver, custo de visitar mundo dos vivos morto etc ser uma config de adm do server, assim n precisando
/// recompilar o jogo sempre q for alterar algo"*. Os numeros da mesa do Enma moram aqui, VIVOS, e nao mais nas constantes:
///   * o PRECO DA VOLTA paga (`enma_reviver`) e o MULTIPLICADOR de quem foi apagado por Hakai;
///   * o PRECO DO PASSE pros vivos (`enma_passe`) e os DIAS que ele vale.
/// O servidor os le do `servidor.json` (a configuracao DELE, na pasta de saves, ao lado do modo das racas) no boot, e o admin os muda
/// pela aba Admin (o verb `admin_custos_alem`), que os grava de volta no arquivo: trocar um numero e apertar um botao (ou editar o
/// arquivo com o servidor desligado), sem recompilar. O cliente os recebe do servidor (`S2C.CustosDoAlem`, no login e a cada mudanca) e
/// escreve com eles os botoes da mesa. Os PADROES sao as constantes de sempre (<see cref="Alem.PrecoDoReviveDoEnma"/>,
/// <see cref="Alem.PrecoDoPasseDoEnma"/>, <see cref="Alem.DiasDoPasseDoEnma"/> e `KitDoDeus.HakaiPrecoDoRevive`).
///
/// NO HOST (servidor e cliente no mesmo processo) os dois lados leem o MESMO estado: o pacote que o cliente recebe traz os numeros que o
/// servidor ja pos aqui. Por isso o cliente nunca volta isto ao padrao por conta propria.
/// ==============================================================================================================================
/// </summary>
public static class CustosDoAlem
{
	/// <summary>As chaves do verb (`admin_custos_alem chave valor`), na ordem do cartao da aba Admin.</summary>
	public const string ChaveDoRevive = "revive", ChaveDoHakai = "hakai", ChaveDoPasse = "passe", ChaveDosDias = "dias";

	/// <summary>Os tetos: zeni ate um trilhao, o multiplicador de 1x a 100x, o passe de 1 a 365 dias do jogo.</summary>
	public const double PrecoMaximo = 1e12, MultiplicadorMaximo = 100;
	public const int DiasMaximos = 365;

	/// <summary>O preco da volta paga no Enma, em zeni.</summary>
	public static double PrecoDoRevive { get; private set; } = Alem.PrecoDoReviveDoEnma;

	/// <summary>Quantas vezes o preco da volta paga quem foi apagado por Hakai (`hakai_mark`).</summary>
	public static double MultiplicadorDoHakai { get; private set; } = Ranks.KitDoDeus.HakaiPrecoDoRevive;

	/// <summary>O preco do passe pros vivos, em zeni.</summary>
	public static double PrecoDoPasse { get; private set; } = Alem.PrecoDoPasseDoEnma;

	/// <summary>Quantos dias do jogo o passe vale.</summary>
	public static int DiasDoPasse { get; private set; } = Alem.DiasDoPasseDoEnma;

	/// <summary>O prazo do passe em ms de relogio: os dias do jogo, o dia do ceu (`Espaco.SegundosPorDiaInGame`).</summary>
	public static long MsDoPasse => (long)(DiasDoPasse * Espaco.SegundosPorDiaInGame * 1000);

	/// <summary>Os quatro de uma vez, aparados nos limites -- o que vem do arquivo, do fio ou da bancada. Numero que nao e numero vira o padrao.</summary>
	public static void Por(double revive, double hakai, double passe, int dias)
	{
		PrecoDoRevive = Math.Clamp(double.IsFinite(revive) ? Math.Round(revive) : Alem.PrecoDoReviveDoEnma, 0, PrecoMaximo);
		MultiplicadorDoHakai = Math.Clamp(double.IsFinite(hakai) ? hakai : Ranks.KitDoDeus.HakaiPrecoDoRevive, 1, MultiplicadorMaximo);
		PrecoDoPasse = Math.Clamp(double.IsFinite(passe) ? Math.Round(passe) : Alem.PrecoDoPasseDoEnma, 0, PrecoMaximo);
		DiasDoPasse = Math.Clamp(dias, 1, DiasMaximos);
	}

	/// <summary>De volta as constantes de sempre.</summary>
	public static void Padrao() =>
		Por(Alem.PrecoDoReviveDoEnma, Ranks.KitDoDeus.HakaiPrecoDoRevive, Alem.PrecoDoPasseDoEnma, Alem.DiasDoPasseDoEnma);

	/// <summary>
	/// UM NUMERO PELA CHAVE (o verb `admin_custos_alem chave valor`): nulo = mudou; senao, o porque, e nada muda. O preco aceita "350000",
	/// "350.000" e "350,000" (o separador de milhar sai); o multiplicador aceita decimal com virgula ou ponto ("1,5").
	/// </summary>
	public static string? Mudar(string chave, string valor)
	{
		string v = valor.Trim();
		switch (chave.Trim().ToLowerInvariant())
		{
			case ChaveDoRevive:
			case ChaveDoPasse:
				if (!long.TryParse(v.Replace(".", "").Replace(",", "").Replace(" ", ""), NumberStyles.None, CultureInfo.InvariantCulture, out long z)
					|| z > PrecoMaximo)
					return $"preço inválido: '{valor}' (zeni, de 0 a {Zeni(PrecoMaximo)})";
				if (chave.Trim().ToLowerInvariant() == ChaveDoRevive) PrecoDoRevive = z; else PrecoDoPasse = z;
				return null;
			case ChaveDoHakai:
				if (!double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double m)
					|| !double.IsFinite(m) || m < 1 || m > MultiplicadorMaximo)
					return $"multiplicador inválido: '{valor}' (de 1 a {MultiplicadorMaximo:0}, como 2 ou 1,5)";
				MultiplicadorDoHakai = m;
				return null;
			case ChaveDosDias:
				if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int d) || d < 1 || d > DiasMaximos)
					return $"dias inválidos: '{valor}' (de 1 a {DiasMaximos})";
				DiasDoPasse = d;
				return null;
			default:
				return $"não conheço o custo '{chave}' -- use {ChaveDoRevive}, {ChaveDoHakai}, {ChaveDoPasse} ou {ChaveDosDias}";
		}
	}

	/// <summary>Um preco em zeni no formato do jogo ("200.000"): o mesmo dos botoes da mesa, em qualquer maquina.</summary>
	public static string Zeni(double v) => v.ToString("N0", PtBr);

	private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

	/// <summary>Os quatro de agora, numa linha -- o que o admin le no chat e o que o servidor escreve no log.</summary>
	public static string Resumo() =>
		$"volta {Zeni(PrecoDoRevive)} zeni (quem levou Hakai paga {MultiplicadorDoHakai.ToString("0.##", PtBr)}x); "
		+ $"passe {Zeni(PrecoDoPasse)} zeni por {DiasDoPasse} dias";
}
