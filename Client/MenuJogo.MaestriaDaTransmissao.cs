using Godot;
using Jandirus.Core.Skills;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O CARTAO DA MAESTRIA DO INSTANT TRANSMISSION na aba Sense (dono, 2026-10-23: *"a maestria aparece na aba Sense"*). Logo
/// depois da faixa das presencas, pra quem tem a skill: a barra da maestria (0 a 100%), quanto do Ki uma viagem leva e quanto
/// dura a concentracao -- de longe e no campo de visao. OS NUMEROS SAO DO SERVIDOR (`S2C.NivelDaTransmissao`, ver
/// `Net/FioDaTransmissao.cs`), pelas mesmas contas que o verbo cobra: a aba nao calcula nada.
///
/// O TITULO NAO E VAZIO de proposito: a `--diagabas` conta "um cartao por presenca" pelos cartoes SEM titulo, e este nao e uma.
/// O botao "Teletransportar" de cada presenca continua como o dono o pediu (2026-10-21).
/// </summary>
public partial class MenuJogo
{
	/// <summary>O titulo do cartao -- e por ele (o metadado `titulo` do <see cref="Cartao"/>) que os robos o acham.</summary>
	internal const string TituloDoCartaoDaMaestria = "Instant Transmission";

	/// <summary>O rotulo da linha da barra -- o `ValorDesenhado("Sense", ...)` dos robos le o "42%" por ele.</summary>
	internal const string RotuloDaMaestriaDoIt = "maestria";

	private void CartaoDaMaestriaDaTransmissao()
	{
		GameClient? cli = GameClient.Instance;
		if (cli == null || cli.SentidosSaoDoScouter || cli.NivelDaTransmissao <= 0) return;
		TransmissaoState it = cli.Transmissao;
		// SERVIDOR DE ANTES DA MAESTRIA: o pacote acabou no nivel, e o cartao nao tem o que dizer
		if (!it.TemMaestria) return;

		VBoxContainer c = Cartao(TituloDoCartaoDaMaestria);
		LinhaComBarra(RotuloDaMaestriaDoIt, $"{it.Maestria}%", it.Maestria / TransmissaoEmNiveis.MaestriaMaxima,
					  it.Maestria >= TransmissaoEmNiveis.MaestriaMaxima ? Tema.Bom : Tema.Ki, c);
		Linha("concentração", $"{SegundosDoIt(it.MsLonge)} · {SegundosDoIt(it.MsPerto)} no campo de visão", null, c);
		Linha("custo de Ki", $"{it.CustoDeKi}% do seu Ki por viagem", null, c);
		Nota($"A maestria sobe usando: +{TransmissaoEmNiveis.MaestriaPorViagem:0}% a cada Instant Transmission completo "
			 + $"(Yardrat: +{TransmissaoEmNiveis.MaestriaPorViagemDoYardrat:0}%).", c);
	}

	/// <summary>Milissegundos como a tela os le: "3,0 s", "0,2 s".</summary>
	private static string SegundosDoIt(int ms) => $"{ms / 1000.0:0.0} s";
}
