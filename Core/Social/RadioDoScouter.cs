using Jandirus.Core.World;

namespace Jandirus.Core.Social;

/// <summary>
/// ============================ O RADIO DO SCOUTER: AS REGRAS, LONGE DE QUALQUER MOTOR ============================
/// *"faca com q o scouter tenha uma opcao de falar no voice chat via radio, ao falar no radio vc escolhe uma
/// frequencia e os jogadores q estao no mesmo planeta q vc ou em uma distancia de 500 tiles de vc no espaco
/// com scouter na mesma frequencia podem escutar sua voz no ouvido esquerdo deles com um efeito de radio"*
/// -- o dono, 2026-10-10.
///
/// A frase tem quatro portoes e um jeito de soar. Os portoes moram AQUI, puros, porque sao o que as duas
/// pontas e as duas bancadas precisam concordar; quem os aplica e o servidor (`GameServer.Radio.cs`), e o
/// jeito de soar e do cliente (`Client/RadioOuvido.cs`, `Client/EfeitoDeRadio.cs`).
///
///   | portao        | a regra                                                                    |
///   |---------------|----------------------------------------------------------------------------|
///   | o aparelho    | quem FALA e quem OUVE precisam do scouter LIGADO (`Poder.Scouter`)          |
///   | a frequencia  | um numero de <see cref="FrequenciaMinima"/> a <see cref="FrequenciaMaxima"/>, igual nas duas pontas |
///   | o mundo       | os dois na MESMA zona -- o planeta inteiro, sem limite de distancia         |
///   | o espaco      | no espaco a zona e uma so pro universo: ai vale <see cref="AlcanceNoEspacoEmTiles"/> |
///
/// ============================ A LEI DA VOZ VALE AQUI TAMBEM ============================
/// *"Quem esta fora NAO RECEBE BYTE NENHUM"* (ver o cabecalho de <see cref="VozLocal"/>). Frequencia errada
/// nao e "chega e o cliente descarta": o quadro nao sai do servidor. Um cliente que le pacotes nao ouve a
/// frequencia dos outros -- e por isso nada aqui e decidido por quem recebe.
/// ======================================================================================
///
/// ============================ NO DM O RADIO ERA DE TEXTO, E NAO TINHA ALCANCE ============================
/// `obj/items/Scouter` (`Tier 1.5.dm:324-352`): `var/channel = 1`, o verb `Scouter_Speak(msg as text)` manda a
/// frase a TODO scouter equipado no mesmo canal, em qualquer lugar do universo, e `Change_Channel(chann as
/// text)` troca o canal. O que o dono pediu e outra coisa (voz, planeta, 500 tiles): vale o pedido. Do DM
/// ficaram o canal de fabrica (1) e a exigencia de o aparelho estar EQUIPADO (`nO.suffix`, `:337`).
/// =========================================================================================================
/// </summary>
public static class RadioDoScouter
{
	// =====================================================================
	// A FREQUENCIA
	// =====================================================================
	/// <summary>A menor frequencia que o aparelho sintoniza.</summary>
	public const int FrequenciaMinima = 1;

	/// <summary>
	/// A maior. Tres algarismos: cabe no teclado numerico da tela (que tira o numero de algarismos do proprio
	/// teto) e e muito mais canal do que um servidor tem de gente -- quem quer conversa reservada acha uma
	/// frequencia vazia sem que isso vire senha de seis digitos.
	/// </summary>
	public const int FrequenciaMaxima = 999;

	/// <summary>
	/// A frequencia de fabrica: 1, o `var/channel = 1` do scouter do DM (`Tier 1.5.dm:328`). E de proposito a
	/// MESMA pra todo mundo: quem liga um scouter pela primeira vez ja cai no canal aberto do planeta.
	/// </summary>
	public const int FrequenciaPadrao = 1;

	/// <summary>
	/// O verbo que troca a frequencia: `radio_frequencia &lt;numero&gt;`, pelo canal de verbos (`C2S.Verbo`). Uma
	/// constante so pras duas pontas: a tela (`MenuJogo.Radio.cs`) manda e o servidor (`ComandoDeRadio`) le.
	/// </summary>
	public const string VerboDeSintonizar = "radio_frequencia";

	public static bool FrequenciaValida(int f) => f >= FrequenciaMinima && f <= FrequenciaMaxima;

	/// <summary>
	/// A frequencia que VALE pra uma ficha. Numero fora da faixa (um save mexido a mao) cai na de fabrica em
	/// vez de virar um canal que ninguem mais consegue sintonizar.
	/// </summary>
	public static int Sanear(int f) => FrequenciaValida(f) ? f : FrequenciaPadrao;

	// =====================================================================
	// O ALCANCE
	// =====================================================================
	/// <summary>Ate que distancia o radio chega NO ESPACO, em tiles. O numero do pedido.</summary>
	public const int AlcanceNoEspacoEmTiles = 500;

	/// <summary>O mesmo alcance em pixels de mundo (16.000 px: quase oito chunks do espaco).</summary>
	public const float AlcanceNoEspacoPx = AlcanceNoEspacoEmTiles * ZoneCollision.TileSize;

	/// <summary>
	/// QUANTAS VOZES DE RADIO UM OUVINTE RECEBE AO MESMO TEMPO.
	///
	/// E o teto de banda do servidor, pelo argumento de <see cref="VozLocal.MaxFalantesPorOuvinte"/> -- e ele
	/// pesa MAIS aqui: a voz de perto alcanca 22 tiles, o radio alcanca o planeta inteiro. Sem teto, vinte
	/// pessoas falando juntas na frequencia de fabrica seriam vinte fluxos pra cada scouter do planeta.
	///
	/// Quem cai nao e "o mais longe" (no radio nao ha longe): e quem ABRIU a transmissao por ultimo. Como num
	/// radio de verdade, quem pegou o canal primeiro fica com ele.
	/// </summary>
	public const int MaxVozesPorOuvinte = 4;

	/// <summary>DEFEITO INJETADO (bancada `--radioteste`): quem esta sem scouter fala e ouve o radio do mesmo jeito. Lido por <see cref="TemAparelho"/>.</summary>
	public static bool SemScouterDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--radioteste`): a frequencia nao separa ninguem. Lido por <see cref="Ouve"/>.</summary>
	public static bool QualquerFrequenciaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--radioteste`): o radio atravessa de um mundo pro outro. Lido por <see cref="Alcanca"/> (e o servidor passa a olhar todo mundo em vez da lista da zona).</summary>
	public static bool AtravessaMundosDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--radioteste`): no espaco o radio chega a qualquer distancia. Lido por <see cref="Alcanca"/>.</summary>
	public static bool EspacoSemLimiteDeTeste;

	/// <summary>O APARELHO: este corpo tem um scouter LIGADO? Vale igual pra quem fala e pra quem ouve.</summary>
	public static bool TemAparelho(bool scouterLigado) => scouterLigado || SemScouterDeTeste;

	/// <summary>
	/// O RADIO CHEGA DE UM PONTO AO OUTRO?
	///
	///   * zonas diferentes: NAO. Outro planeta, o interior de uma nave, a mente de alguem, o Lookout e o Alem
	///     sao zonas proprias -- e de um planeta pro espaco em volta dele tambem nao chega (o pedido diz
	///     "no mesmo planeta" OU "a 500 tiles no espaco", nunca os dois misturados);
	///   * mesma zona que NAO e o espaco: SIM, a qualquer distancia -- e o "mesmo planeta" do pedido, e vale
	///     igual pros lugares fechados (quem divide a ponte de uma nave se ouve);
	///   * os dois no espaco: so ate <see cref="AlcanceNoEspacoPx"/>. O espaco e UMA zona pro universo
	///     inteiro (<see cref="Espaco.NomeDoEspaco"/>), entao "mesma zona" la nao quer dizer nada.
	///
	/// A CONTA E EM `double`: no espaco as posicoes passam de um milhao de pixels, e a diferenca de dois
	/// `float` desse tamanho ja nasce arredondada.
	/// </summary>
	public static bool Alcanca(ZoneKey zonaDeQuemFala, Vec2 deQuemFala, ZoneKey zonaDeQuemOuve, Vec2 deQuemOuve)
	{
		if (zonaDeQuemFala.Hash != zonaDeQuemOuve.Hash) return AtravessaMundosDeTeste;
		if (!Espaco.EhEspaco(zonaDeQuemFala) || EspacoSemLimiteDeTeste) return true;

		double dx = (double)deQuemOuve.X - deQuemFala.X, dy = (double)deQuemOuve.Y - deQuemFala.Y;
		return dx * dx + dy * dy <= (double)AlcanceNoEspacoPx * AlcanceNoEspacoPx;
	}

	/// <summary>
	/// **A REGRA INTEIRA**: este ouvinte recebe a transmissao deste falante? Os tres portoes, do mais barato
	/// pro mais caro: o aparelho nas duas pontas, a frequencia, o alcance.
	/// </summary>
	public static bool Ouve(bool falanteComScouter, int frequenciaDoFalante, ZoneKey zonaDoFalante, Vec2 posDoFalante,
							bool ouvinteComScouter, int frequenciaDoOuvinte, ZoneKey zonaDoOuvinte, Vec2 posDoOuvinte)
	{
		if (!TemAparelho(falanteComScouter) || !TemAparelho(ouvinteComScouter)) return false;
		if (frequenciaDoFalante != frequenciaDoOuvinte && !QualquerFrequenciaDeTeste) return false;
		return Alcanca(zonaDoFalante, posDoFalante, zonaDoOuvinte, posDoOuvinte);
	}

	// =====================================================================
	// PRA ONDE VAI A VOZ DE QUEM APERTA
	// =====================================================================
	/// <summary>Os caminhos de um quadro de voz. Os dois juntos = as duas teclas seguradas.</summary>
	[Flags]
	public enum Destino : byte
	{
		Nenhum = 0,
		/// <summary>A voz de proximidade, a de sempre (`C2S.Voz`).</summary>
		Perto = 1,
		/// <summary>O radio do scouter (`C2S.VozDeRadio`).</summary>
		Radio = 2,
	}

	/// <summary>DEFEITO INJETADO (bancadas `--diagradio` e `--radiovivo`): segurar so a tecla do radio manda a voz pelos DOIS caminhos. Lido por <see cref="ParaOndeVai"/>.</summary>
	public static bool RadioVazaPorPertoDeTeste;

	/// <summary>
	/// PRA ONDE VAI O QUE EU FALO AGORA -- a regra das duas teclas, que o microfone do cliente consulta a cada
	/// quadro. O dono: segurando a do radio a voz vai PELO RADIO (e nao pela proximidade); com as duas
	/// seguradas, vao as duas.
	///
	///   * a tecla do radio so transmite com o scouter ligado. Sem ele ela nao faz nada (e quem aperta e
	///     avisado uma vez pela tela);
	///   * a tecla de voz e a de proximidade, como sempre foi;
	///   * no modo de MICROFONE ABERTO (sem apertar pra falar) a voz de perto esta sempre no ar -- e a tecla
	///     do radio a DESVIA: enquanto ela transmite, a proximidade cala, a menos que a tecla de voz tambem
	///     esteja segurada. Sem isso quem joga de microfone aberto nunca conseguiria falar SO no radio.
	///
	/// O servidor confere tudo de novo (scouter, frequencia, alcance): isto so decide o que o cliente MANDA.
	/// </summary>
	public static Destino ParaOndeVai(bool apertarParaFalar, bool teclaDeVoz, bool teclaDeRadio, bool scouterLigado)
	{
		bool radio = teclaDeRadio && scouterLigado;
		bool perto = apertarParaFalar ? teclaDeVoz : teclaDeVoz || !radio;
		if (radio && RadioVazaPorPertoDeTeste) perto = true;
		return (perto ? Destino.Perto : Destino.Nenhum) | (radio ? Destino.Radio : Destino.Nenhum);
	}
}
