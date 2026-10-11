using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O KI DIVINO NA ABA WORLD -- a linha "Atmosfera" do cartao Ceu e o cartao "Ki divino" (o medidor do
/// despertar e, depois dele, a maestria). Pedido do dono de 2026-10-10; a regra esta em
/// `Core/Forms/DespertarDivino.cs` e em `Core/World/AtmosferaDivina.cs`.
///
/// DUAS FONTES, E CADA UMA DIZ UMA COISA:
///   * o LUGAR ser divino e funcao pura da seed (`AtmosferaDivina.Tem`): o cliente responde sozinho, do
///     mesmo jeito que o servidor, e e a MESMA pergunta que marca os mundos na carta estelar;
///   * o MEDIDOR e a MAESTRIA sao do personagem, e so o servidor os conhece (`S2C.GodKi`).
///
/// O CARTAO SO EXISTE PRA QUEM TEM O QUE LER NELE: num mundo divino, com o medidor ja andado ou com o ki
/// divino desperto. Pra todo o resto a aba World e a de sempre.
/// </summary>
public partial class MenuJogo
{
	/// <summary>O dourado do ki divino na interface: a linha da atmosfera, a barra do cartao e a marca na carta estelar.</summary>
	internal static readonly Color OuroDivino = new("ffd86b");

	/// <summary>O chao em que o cliente esta tem atmosfera divina? A seed do universo chega no login; antes dela, nao.</summary>
	internal static bool LugarDivino(GameClient cli) =>
		cli.SeedDoUniverso != 0 && AtmosferaDivina.Tem(cli.SeedDoUniverso, cli.Zone);

	/// <summary>Este corpo da carta estelar tem atmosfera divina? E o que desenha o anel dourado e a linha da ficha.</summary>
	internal static bool MundoDivino(PlanetaNoEspaco p) =>
		GameClient.Instance is { SeedDoUniverso: not 0UL } cli && AtmosferaDivina.Tem(cli.SeedDoUniverso, p);

	/// <summary>A LINHA DA ATMOSFERA no cartao Ceu. So aparece onde ha o que dizer: o ar comum nao ganha linha.</summary>
	private static void LinhaDaAtmosfera(GameClient cli, Control corpoDoCeu)
	{
		if (LugarDivino(cli)) Linha("Atmosfera", "divina", OuroDivino, corpoDoCeu);
	}

	/// <summary>
	/// O CARTAO "Ki divino": o medidor do despertar pra quem ainda nao despertou, a maestria pra quem ja.
	///
	/// SEM NUMERO DE REQUISITO, de proposito -- e o mesmo sigilo da aba Forms: o requisito e pessoal e e
	/// BP BASE, e publicar "faltam 40 milhoes" seria entregar o proprio BP a quem nao tem scouter. O que
	/// o cartao diz e o que o corpo sente: alcanca, ou ainda nao.
	/// </summary>
	private void BlocoDoKiDivino(GameClient cli)
	{
		GameClient.KiDivinoVisto k = cli.KiDivino;
		bool lugar = LugarDivino(cli);
		if (!k.Desperto && !lugar && k.Exposicao <= 0) return;

		VBoxContainer corpo = Cartao("Ki divino", destaque: !k.Desperto && lugar && k.AlcancaORequisito);

		if (k.Desperto)
		{
			Linha("Ki divino", "desperto", OuroDivino, corpo);
			LinhaComBarra("Maestria divina", $"{k.Maestria:0.0}%", k.Maestria / 100.0, OuroDivino, corpo);
			Nota("A maestria divina cresce USANDO o ki divino, numa forma que o peça -- o dobro em combate. "
				 + $"Ela abre os degraus seguintes: {Jandirus.Core.Forms.Catalogo.GodkiBluePct:0}%, "
				 + $"{Jandirus.Core.Forms.Catalogo.GodkiRoyalePct:0}% e {Jandirus.Core.Forms.Catalogo.GodkiUiUePct:0}%.", corpo);
			return;
		}

		if (k.AlcancaORequisito)
		{
			LinhaComBarra("Despertar", $"{k.Exposicao}%", k.Exposicao / DespertarDivino.ExposicaoCheia, OuroDivino, corpo);
			Nota(lugar
					 ? "O seu corpo absorve o ar deste mundo: fique aqui e o ki divino desperta. Trocar golpes com quem já o tem "
					   + "também enche o medidor."
					 : "O que você já absorveu não se perde. O medidor só anda num mundo de atmosfera divina, ou trocando "
					   + "golpes de verdade com quem tem ki divino.", corpo);
			return;
		}

		Linha("Despertar", "o corpo ainda não alcança", Tema.TextoFraco, corpo);
		Nota("Falta poder de BASE pra o ki divino despertar em você -- forma, raiva e Kaio-ken não contam. "
			 + "Enquanto isso o tempo aqui não enche nada.", corpo);
	}

	/// <summary>O pedaco do ki divino na assinatura da aba World, nos arredondamentos em que o cartao desenha.</summary>
	private static string AssinaturaDoKiDivino(GameClient c) =>
		$"{(byte)c.KiDivino.Bits}:{c.KiDivino.Exposicao}:{c.KiDivino.Maestria:0.0}:{LugarDivino(c)}";
}
