using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// **O PODER EM USO, NO PAINEL DE STATUS** -- a linha que diz ao dono do corpo quando ele NAO esta usando o
/// proprio poder inteiro: "USANDO 40% DO PODER", "RELAXADO · 1% DO PODER", "PODER ESCONDIDO".
///
/// ============================ POR QUE ELA PRECISOU EXISTIR ============================
/// O "N% efetivo" ao lado do BP mede o CORPO (Ki, ferimento, fome, peso, gravidade, idade) e nao o controle de
/// poder: quem baixava o proprio poder pra 1% continuava lendo "100% efetivo", e nada na tela dizia por que os
/// socos tinham parado de doer. Esta linha e a outra metade da pergunta "quanto do que eu valho esta saindo".
///
/// SO APARECE FORA DO PADRAO. Com o poder inteiro e a vista ela nao existe -- o painel e o de sempre.
///
/// SEM NUMERO DE PODER: e a porcentagem do PROPRIO poder em uso, que o jogador mesmo escolheu. A regra
/// principal do BP deixa (e razao sobre si mesmo); o BP ao lado continua "???" sem scouter.
/// ======================================================================================
/// </summary>
public partial class Hud
{
	private Label _poderEmUso = null!;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagpoder`): o painel nao escreve a linha -- o estado de antes de 2026-10-11,
	/// em que o jogador a 1% do poder nao tinha como saber. Lido pela propria <see cref="MostrarOPoderEmUso"/>.
	/// </summary>
	public static bool PoderEmUsoMudoDeTeste;

	/// <summary>A linha como ela saiu na tela ("" quando nao aparece). Pra bancada, e so pra ela.</summary>
	public string TextoDoPoderEmUsoDeTeste => _poderEmUso.Visible ? _poderEmUso.Text : "";

	/// <summary>Reescreve a linha agora (a bancada liga e desliga o defeito injetado sem esperar um pacote).</summary>
	public void RedesenharOPoderEmUsoDeTeste() => MostrarOPoderEmUso();

	/// <summary>A linha nasce escondida, logo abaixo da linha do BP.</summary>
	private void MontarOPoderEmUso(VBoxContainer coluna)
	{
		_poderEmUso = new Label { Text = "", Visible = false, Name = "PoderEmUso" };
		_poderEmUso.AddThemeFontSizeOverride("font_size", 12);
		coluna.AddChild(_poderEmUso);
		MostrarOPoderEmUso();
	}

	private void MostrarOPoderEmUso()
	{
		PoderEmUsoState e = GameClient.Instance?.PoderEmUso ?? PoderEmUsoState.Cheio;
		string texto = PoderEmUsoMudoDeTeste ? "" : NoPainel(e);
		_poderEmUso.Text = texto;
		_poderEmUso.Visible = texto.Length > 0;
		_poderEmUso.AddThemeColorOverride("font_color", Cor(e));
	}

	// =====================================================================
	// AS FRASES -- uma fonte pro HUD e pro menu
	// =====================================================================

	/// <summary>
	/// A LINHA DO PAINEL, em caixa alta e curta. Vazia no padrao (100% e a vista): ausencia de linha e "poder
	/// inteiro".
	/// </summary>
	public static string NoPainel(PoderEmUsoState e)
	{
		if (e.Padrao) return "";
		string uso = e.Relaxado ? $"RELAXADO · {e.Porcento}% DO PODER"
			: e.Porcento < 100 ? $"USANDO {e.Porcento}% DO PODER"
			: "";
		if (!e.Escondido) return uso;
		return uso.Length > 0 ? uso + " · ESCONDIDO" : "PODER ESCONDIDO";
	}

	/// <summary>
	/// A FRASE DO MENU, por extenso: as quatro do `relaxedstate` do original ("Relaxed", "Concealing Power",
	/// "Using some power.", "Using Full Power" -- `Power Control.dm:98-108`), com a porcentagem.
	/// </summary>
	public static string PorExtenso(PoderEmUsoState e)
	{
		string uso = e.Relaxado ? $"relaxado ({e.Porcento}% do poder)"
			: e.Porcento < 100 ? $"usando parte do poder ({e.Porcento}%)"
			: "usando todo o poder";
		return e.Escondido ? uso + " · escondendo o poder" : uso;
	}

	/// <summary>Apagado no padrao, laranja quando o corpo esta contido, azul de ki quando o poder esta escondido.</summary>
	public static Color Cor(PoderEmUsoState e) =>
		e.Escondido ? Tema.Ki : e.Porcento < 100 ? Tema.Destaque : Tema.TextoFraco;
}
