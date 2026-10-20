namespace Jandirus.Client;

// (2026-10-19) A CENA DE FORMA POR CIMA DA AURA DE CARGA. Arquivo proprio pra o `CargaVisual.cs` so ganhar a guarda do `Pintar`, e pra
// o codigo novo que conversa com isto (a cena e a bancada) nao depender de linha nova num arquivo velho.
public partial class CargaVisual
{
	/// <summary>
	/// QUANTAS CINEMATICAS DE FORMA ESTAO POR CIMA DESTE CORPO AGORA -- e enquanto houver uma, esta chama nao desenha: fica so a chama da
	/// cena. Quem conta e a propria cena (`Transformacao.CalarACargaDoCorpo` no nascimento, o `Soltar` em todo caminho de saida), e o
	/// pedido do dono esta escrito la.
	///
	/// NAO E O `FormaAcesa` DE VOLTA (ver o topo do <see cref="Pintar"/>): aquele calava a carga enquanto a FORMA tivesse aura propria, ou
	/// seja a partida inteira, e era o "segurar C em Super Saiyajin nao acende nada". Isto dura a CENA e so ela -- o unico momento em que
	/// ha uma segunda chama desenhada no corpo. O estado (`_ligado`, `_excesso`) continua sendo escrito pelo servidor durante a cena; o
	/// que se cala e o desenho, e por isso ele volta sozinho no fim se o Ki ainda estiver acima de 100%.
	///
	/// CONTADOR E NAO `bool`, pela licao da tranca do corpo (`Transformacao.PrendendoOCorpo`): com duas cenas no mesmo corpo (virar duas
	/// vezes seguidas), o fim da primeira devolveria a chama no meio da segunda.
	/// </summary>
	private int _cenasPorCima;

	/// <summary>Uma cena de forma comecou (<c>true</c>) ou acabou (<c>false</c>) por cima deste corpo. Ver <see cref="_cenasPorCima"/>.</summary>
	public void CenaPorCima(bool comecou)
	{
		_cenasPorCima = Math.Max(0, _cenasPorCima + (comecou ? 1 : -1));
		Pintar();
	}

	/// <summary>Ha cena de forma calando esta chama agora? Pra bancada.</summary>
	public bool CaladaPelaCenaDeTeste => _cenasPorCima > 0;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagbrilhoforma`): a cena de forma nao cala mais a aura de carga -- a chama do Ki acima de 100% fica
	/// acesa por cima da chama da cena, as duas empilhadas no mesmo corpo (o jogo de antes). Lido pela propria linha do conserto, a guarda
	/// do <see cref="Pintar"/>, e por isso ele vale tambem pra cena que JA estava calando quando ele ligou. Falso em jogo, sempre.
	///
	/// A `--diagforma` tambem o liga, no passo 2 dela: ela deixa de proposito duas cenas vivas em cima do corpo (a do teto e a que ela
	/// espera ate o passo 9) enquanto, no mesmo quadro, mede a carga pela tecla C e pelo Ki -- e o que se mede la e a carga, e nao esta
	/// regra. E o mesmo uso que a `--solteste` faz do `LemeApanhaDeTeste`: ligar o defeito da regra vizinha pra medir a propria linha
	/// sozinha.
	/// </summary>
	public static bool CargaPorCimaDaCenaDeTeste;
}
