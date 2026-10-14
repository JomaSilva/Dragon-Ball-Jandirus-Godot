using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// **OS AVISOS DO CORPO, NO PAINEL DE STATUS** -- as linhas que dizem ao dono do corpo um estado que o SPRITE nao
/// mostra: a guarda erguida, o atordoamento, o agarrao.
///
/// ============================ POR QUE ELAS PRECISARAM EXISTIR ============================
/// O dono (2026-10-12): *"adicione algum aviso no canto da tela ao apertar o botao de guarda pra avisar q vc ta com
/// a guarda levantada, ja q n tem diferença de sprite e a galera tava confusa, assim como um aviso apenas pro
/// jogador local q ele ta stunado/atordoado"*. A guarda nao tem pose propria na folha do BYOND, e o atordoamento
/// e um relogio do servidor: de fora os dois sao um corpo parado, e por dentro eram uma tecla que "nao funcionava".
///
/// O agarrao entrou junto pelo mesmo motivo: quem e pego precisa ler que apertar as direcoes e a saida, e ver a
/// luta andar.
///
/// QUEM DIZ E A FICHA (`SheetState`), e nao a tecla: a guarda que o servidor RECUSA (sem braco nem perna pra
/// aparar) nao acende o aviso, e a que ele ergue acende mesmo se o pedido veio de outro lugar.
///
/// SO O DONO LE: a ficha e um pacote pessoal. Ninguem em volta fica sabendo que o outro esta atordoado.
/// ========================================================================================
/// </summary>
public partial class Hud
{
	private VBoxContainer _avisosDoCorpo = null!;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagv020`): o painel nao escreve aviso nenhum -- o HUD de antes de 2026-10-12.
	/// Lido pelo <see cref="MostrarOsAvisosDoCorpo"/>.
	/// </summary>
	public static bool AvisosDoCorpoMudosDeTeste;

	/// <summary>As linhas como sairam na tela, na ordem. Pra bancada, e so pra ela.</summary>
	public List<string> AvisosDoCorpoDeTeste =>
		[.. _avisosDoCorpo.GetChildren().OfType<Label>().Where(l => l.Visible).Select(l => l.Text)];

	/// <summary>Reescreve os avisos agora, com esta ficha (a bancada nao espera o pacote de 5 Hz).</summary>
	public void RedesenharOsAvisosDoCorpoDeTeste(SheetState f) => MostrarOsAvisosDoCorpo(f);

	/// <summary>A coluna nasce vazia, no pe do painel de status: sem aviso o painel e o de sempre.</summary>
	private void MontarOsAvisosDoCorpo(VBoxContainer coluna)
	{
		_avisosDoCorpo = new VBoxContainer { Name = "AvisosDoCorpo", Visible = false };
		_avisosDoCorpo.AddThemeConstantOverride("separation", 2);
		coluna.AddChild(_avisosDoCorpo);
	}

	private void MostrarOsAvisosDoCorpo(SheetState f)
	{
		List<(string Texto, Color Cor)> avisos = AvisosDoCorpoMudosDeTeste ? [] : AvisosDoCorpo(f);

		// AS LINHAS SAO REAPROVEITADAS: a ficha chega cinco vezes por segundo, e criar e soltar um Label a cada uma
		// seria lixo pro coletor por um texto que quase nunca muda.
		List<Label> linhas = [.. _avisosDoCorpo.GetChildren().OfType<Label>()];
		for (int i = linhas.Count; i < avisos.Count; i++)
		{
			var l = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
			l.AddThemeFontSizeOverride("font_size", 14);
			_avisosDoCorpo.AddChild(l);
			linhas.Add(l);
		}
		for (int i = 0; i < linhas.Count; i++)
		{
			bool vale = i < avisos.Count;
			linhas[i].Visible = vale;
			if (!vale) continue;
			if (linhas[i].Text != avisos[i].Texto) linhas[i].Text = avisos[i].Texto;
			linhas[i].AddThemeColorOverride("font_color", avisos[i].Cor);
		}
		_avisosDoCorpo.Visible = avisos.Count > 0;
	}

	// =====================================================================
	// AS FRASES -- funcao pura da ficha
	// =====================================================================
	public const string AvisoDeGuarda = "GUARDA LEVANTADA";
	public const string AvisoDeAtordoado = "ATORDOADO";

	/// <summary>
	/// O QUE O PAINEL DIZ DESTE CORPO AGORA, de cima pra baixo. Caido ou morto nao ha aviso: o corpo no chao ja
	/// e a leitura, e "atordoado" por cima de um nocaute seria ruido.
	/// </summary>
	public static List<(string Texto, Color Cor)> AvisosDoCorpo(SheetState f)
	{
		List<(string, Color)> l = [];
		if (f.Imobilizado) return l;

		if (f.Agarrado)
		{
			string comeco = f.RaboPreso ? "RABO PRESO" : f.Estrangulado ? "ESTRANGULADO" : "AGARRADO";
			l.Add(($"{comeco} · aperte as teclas de andar pra se soltar ({f.Escape}%)", Tema.Perigo));
		}
		if (f.Atordoado) l.Add((AvisoDeAtordoado, Tema.Perigo));
		if (f.Guarda) l.Add((AvisoDeGuarda, Tema.Ki));
		if (f.Estrangulando) l.Add(("ESTRANGULANDO · parado enquanto aperta · o soco afrouxa", Tema.Destaque));
		else if (f.Segurando) l.Add(("SEGURANDO ALGUÉM · ande pra arremessar · o soco estrangula", Tema.Destaque));
		return l;
	}
}
