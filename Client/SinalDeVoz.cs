using Godot;
using Jandirus.Core.Social;

namespace Jandirus.Client;

/// <summary>
/// ============================ DE QUEM E ESSA VOZ? ============================
/// Um sinal discreto sobre a cabeca de quem esta falando. Sem ele, uma voz numa briga de tres e uma
/// voz vinda de lugar nenhum: o jogador ouve alguem dizer "sai da frente" e olha pra tres bonecos
/// identicos. E o MESMO buraco que o <see cref="BalaoDeFala"/> existe pra tapar no texto -- ali "o
/// chat conta tudo e nao diz quem"; aqui o alto-falante conta tudo e nao diz quem.
///
/// A voz e PIOR nesse aspecto que o texto, e por isso este sinal nao e opcional: a fala escrita deixa
/// uma linha no painel que da pra reler; a falada nao deixa nada. Quem nao viu de quem era perdeu.
/// ==========================================================================
///
/// ============================ ELE NAO TEM PACOTE PROPRIO ============================
/// Ninguem manda "fulano esta falando". O sinal acende porque **estao chegando quadros de voz** dessa
/// pessoa (`VozOuvida.Falando`) -- ou, no meu proprio corpo, porque o meu microfone esta aberto
/// (`Microfone.Falando`).
///
/// Isso nao e economia de banda, e correcao: o sinal acende exatamente pra quem esta ouvindo aquela
/// voz. Um pacote proprio teria que reimplementar o corte de alcance, o teto de quatro falantes e a
/// parede -- e no dia em que divergisse do corte de verdade apareceria uma boca mexendo em silencio,
/// que e a mentira mais dificil de rastrear de todas.
/// ================================================================================
///
/// ============================ E O RADIO DO SCOUTER ACENDE O MESMO SINAL, EM VERDE ============================
/// Quem transmite pelo radio (e quem eu OUCO pelo radio, se estiver na minha tela) ganha as barras em verde e
/// uma anteninha ao lado -- a mesma pergunta ("de quem e essa voz?"), respondida pela outra fonte:
/// `RadioOuvido.Falando` pros outros, `Microfone.NoRadio` pra mim. As duas vozes juntas mostram as barras da
/// voz de perto COM a antena. Quem esta longe demais pra aparecer na tela e dito pela faixa do `RadioOuvido`.
/// ==============================================================================================================
///
/// Desenhado em `_Draw` pelo mesmo motivo do balao: um `Sprite2D` com textura traria um asset novo pra
/// desenhar tres barrinhas, e um no de UI traria a fonte do TEMA (que e de tela e nao acompanha o zoom
/// do mapa).
/// </summary>
/// <remarks>
/// <see cref="INaoSomeComOCorpo"/>: quem esta falando continua falando enquanto o Zanzoken troca o
/// corpo pelas listras -- e justamente ai (no meio de uma esquiva) que saber quem falou mais importa.
/// Mesma decisao do balao, pelo mesmo motivo.
/// </remarks>
public partial class SinalDeVoz : Node2D, ISobeComOCorpo, INaoSomeComOCorpo
{
	/// <summary>
	/// De quem e este corpo. Posto por quem cria o no; 0 = ninguem, e o sinal nunca acende.
	/// </summary>
	public int Id;

	/// <summary>Este corpo e o MEU? Ai quem responde e o microfone, e nao a rede.</summary>
	public bool Meu;

	/// <summary>
	/// Onde o sinal fica, em pixels acima da origem do corpo.
	///
	/// -34, quatro acima da base do balao de fala (-26): quando alguem fala e escreve ao mesmo tempo,
	/// os dois aparecem e nenhum tapa o outro -- o balao cresce PRA CIMA a partir de -26, entao o
	/// sinal fica encostado nele e sobe junto na leitura, sem sobrepor a primeira linha.
	/// </summary>
	private const float AlturaBase = -34f;

	/// <summary>Meia largura do desenho. Tres barras de 2 px com 2 px de vao = 10 px.</summary>
	private const float Meia = 5f;

	/// <summary>
	/// O DESLOCAMENTO DE ALTITUDE, escrito pela varredura do voo (`SubirComOVoo.Aplicar`) porque esta
	/// classe declara <see cref="ISobeComOCorpo"/>.
	///
	/// PROPRIEDADE E NAO `Position` NA MAO, e o motivo esta escrito no irmao dela em `BalaoDeFala`:
	/// quem escrevia a posicao direto apagava a <see cref="AlturaBase"/> junto, e o sinal ia parar no
	/// umbigo de quem subisse -- defeito invisivel no chao, onde o deslocamento e zero e a conta da no
	/// mesmo por acidente. Sem esta propriedade, o sinal ficaria colado ao chao enquanto o corpo voa.
	/// </summary>
	public Vector2 Deslocamento
	{
		set
		{
			_posto = true;
			Position = new Vector2(0, AlturaBase) + value;
		}
	}

	/// <summary>A varredura do voo ja escreveu a posicao deste no. Ver <see cref="_Ready"/>.</summary>
	private bool _posto;

	/// <summary>DEFEITO INJETADO (bancada `--radiovivo`): o sinal nao nasce na altura de base -- fica na origem do corpo ate o corpo mudar de altitude, como era antes de 2026-10-10. Lido pelo <see cref="_Ready"/>.</summary>
	public static bool SemAlturaDeBaseDeTeste;

	/// <summary>
	/// O SINAL NASCE ACIMA DA CABECA, e nao na origem do corpo.
	///
	/// ============================ ELE SO IA PRO LUGAR QUANDO O CORPO VOAVA ============================
	/// A posicao deste no era escrita SO pela <see cref="Deslocamento"/>, e quem a chama e a varredura do
	/// voo -- que o corpo REMOTO so roda quando a altitude dele MUDA (`RemotePlayer.Altura` sai cedo enquanto
	/// ela e a mesma, e ela nasce zero). Quem nunca tinha saido do chao ficava com o sinal na origem do
	/// corpo: as barrinhas desenhadas em cima do rosto. (O corpo LOCAL escapava: a altitude desenhada dele
	/// nasce -1, e a primeira varredura sempre roda.) Achado na foto da `--radiovivo`, com o sinal verde do
	/// radio; valia igual pra voz de perto desde que o sinal existe.
	///
	/// O `BalaoDeFala`, irmao deste no, sempre se pos no lugar no proprio `_Ready`. Aqui e o mesmo, e so
	/// quando a varredura ainda nao escreveu: um filho que nasce com o corpo JA no ar recebe a altura antes
	/// do `_Ready` (`SubirComOVoo.AoNascer`), e ela nao pode ser apagada.
	/// ==================================================================================================
	/// </summary>
	public override void _Ready()
	{
		if (!_posto && !SemAlturaDeBaseDeTeste) Position = new Vector2(0, AlturaBase);
	}

	/// <summary>
	/// A OSCILACAO DAS BARRAS. Nao e o volume de verdade e nao deve ser: a amplitude do microfone
	/// alheio nao chega aqui (so o payload comprimido chega), e decodificar pra desenhar tres pixels
	/// seria pagar caro pra fingir precisao. O que o sinal afirma e "esta falando", e a oscilacao e o
	/// que faz esse "esta" parecer vivo em vez de um icone congelado.
	/// </summary>
	private double _fase;

	/// <summary>O que o sinal mostra agora: nada, a voz de perto, o radio do scouter, ou os dois.</summary>
	private RadioDoScouter.Destino _aceso;

	public override void _Process(double delta)
	{
		bool perto = Meu ? Microfone.Falando && Microfone.PorPerto : VozOuvida.Instancia?.Falando(Id) == true;
		bool radio = Meu ? Microfone.Falando && Microfone.NoRadio : RadioOuvido.Instancia?.Falando(Id) == true;
		RadioDoScouter.Destino agora = (perto ? RadioDoScouter.Destino.Perto : RadioDoScouter.Destino.Nenhum)
									 | (radio ? RadioDoScouter.Destino.Radio : RadioDoScouter.Destino.Nenhum);

		_fase += delta * 9.0;

		// SO REDESENHA QUANDO MUDA **OU** QUANDO ESTA ACESO. Um `QueueRedraw` por quadro em todo corpo
		// da zona seria trabalho de desenho pra vinte bonecos calados; apagado, este no nao custa nada
		// alem desta comparacao.
		if (agora != _aceso)
		{
			_aceso = agora;
			QueueRedraw();
		}
		else if (_aceso != RadioDoScouter.Destino.Nenhum) QueueRedraw();
	}

	public override void _Draw()
	{
		if (_aceso == RadioDoScouter.Destino.Nenhum) return;
		bool noRadio = (_aceso & RadioDoScouter.Destino.Radio) != 0;

		// BRANCO LEVEMENTE AZULADO, com um contorno escuro por baixo. O contorno nao e enfeite: sem
		// ele o sinal some em cima do ceu claro e em cima da neve -- e o mesmo defeito que so a FOTO
		// mostrou nos raios da forma ("branco some em fundo claro").
		// SO PELO RADIO, as barras sao verdes (a cor da faixa do radio); com a voz de perto no ar elas ficam
		// como sempre foram, e a antena diz o resto.
		Color cor = _aceso == RadioDoScouter.Destino.Radio ? RadioOuvido.CorDoRadio : new Color(0.85f, 0.93f, 1f);
		var sombra = new Color(0, 0, 0, 0.65f);

		for (int i = 0; i < 3; i++)
		{
			// cada barra com a propria defasagem: tres barras subindo juntas parecem um bloco piscando
			// as barras crescem PRA CIMA a partir da origem do no, que a `Deslocamento` ja posicionou
			float alt = 2f + 3f * (float)((Math.Sin(_fase + i * 1.1) + 1) * 0.5);
			float x = -Meia + 1 + i * 4;
			DrawRect(new Rect2(x - 1, -alt - 1, 4, alt + 2), sombra);
			DrawRect(new Rect2(x, -alt, 2, alt), cor);
		}

		// A ANTENA DO RADIO: um mastro fino com a ponta acesa, a direita das barras. Parada (nao oscila): as
		// barras ja dizem "esta falando", ela diz "pelo scouter".
		if (!noRadio) return;
		float ax = Meia + 4;
		DrawRect(new Rect2(ax - 1, -9, 3, 10), sombra);
		DrawRect(new Rect2(ax - 2, -11, 5, 5), sombra);
		DrawRect(new Rect2(ax, -8, 1, 8), RadioOuvido.CorDoRadio);
		DrawRect(new Rect2(ax - 1, -10, 3, 3), RadioOuvido.CorDoRadio);
	}
}
