using Godot;

namespace Jandirus.Client;

/// <summary>
/// **O CHAT REDIMENSIONAVEL.** Pedido do dono de 2026-10-10: *"poder redimensionar o chat pq uns players queriam
/// aumentar e outros diminuir"*.
///
/// O CANTO NAO SAI DO LUGAR (embaixo, a esquerda, a 12 px das bordas): o que muda e a LARGURA, puxando a borda
/// da direita, e a ALTURA, puxando a de cima -- ou as duas pelo canto de cima a direita. As bordas acendem com o
/// mouse em cima (<see cref="AlcaDeJanela"/>); paradas, o chat e exatamente o de antes.
///
/// O tamanho mora no <see cref="ArranjoDaInterface"/> como fracao da tela, e e preso pelo
/// <see cref="Janelas.ChatNaTela"/> toda vez que e aplicado: nunca menor que o minimo, nunca entrando no canto
/// inferior direito, nunca subindo ate o painel de status (o tamanho REAL dele, ja com a escala que o jogador
/// escolheu). De fabrica continua 540 x 280, pelas mesmas contas do `Montar`.
///
/// QUEM SE ANCORA NO CHAT le o retangulo dele daqui (<see cref="RetanguloNaTela"/>) -- a faixa da mineracao e o
/// canto de assistir o torneio --, em vez de copiar os numeros.
/// </summary>
public partial class Chat
{
	/// <summary>A menor altura do TEXTO com o chat mexido. De fabrica continua 150 (`Montar`): a altura do painel e que manda.</summary>
	private const float TextoMinimoMexido = 70f;

	private AlcaDeJanela _alca = null!;
	private Vector2 _tamanhoAoPegar;

	/// <summary>
	/// O RETANGULO DO PAINEL DO CHAT NA TELA, agora, ou nulo sem chat. E daqui que a vizinhanca mede -- a faixa
	/// da mineracao (`PainelDeMineracao`), o canto de assistir (`PainelDeAssistir`), o teto do painel de status.
	/// </summary>
	public static Rect2? RetanguloNaTela =>
		Instancia is { } c && IsInstanceValid(c._painel) ? c._painel.GetGlobalRect() : null;

	/// <summary>
	/// A alca do chat, por cima do painel: a borda de cima e a da direita. Irma do painel e nao filha -- o
	/// painel e um conteiner, e esticaria qualquer filho pro tamanho do conteudo.
	/// </summary>
	private void MontarORedimensionar(Control raiz)
	{
		_alca = new AlcaDeJanela
		{
			Name = "AlcaDoChat", Sobra = 3f,
			Lados = AlcaDeJanela.Lado.Cima | AlcaDeJanela.Lado.Direita,
		};
		_alca.AoPegar = _ => _tamanhoAoPegar = _painel.Size;
		_alca.AoArrastar = Arrastar;
		_alca.AoSoltar = Janelas.Gravar;
		raiz.AddChild(_alca);
		_painel.ItemRectChanged += AlcaSegueOPainel;

		// `Janelas.Mudou` E ESTATICO e o `SizeChanged` e da raiz: metodo nomeado, e `-=` no `SoltarORedimensionar`.
		Janelas.Mudou += AplicarOTamanho;
		GetViewport().SizeChanged += AplicarOTamanho;
		AplicarOTamanho();
	}

	/// <summary>O `_ExitTree` desta metade.</summary>
	private void SoltarORedimensionar()
	{
		Janelas.Mudou -= AplicarOTamanho;
		if (GetViewport() is { } vp) vp.SizeChanged -= AplicarOTamanho;
	}

	private void AlcaSegueOPainel()
	{
		if (IsInstanceValid(_alca)) _alca.Cobrir(_painel.GetRect());
	}

	private void Arrastar(AlcaDeJanela.Lado lados, Vector2 andou)
	{
		Vector2 pedido = _tamanhoAoPegar;
		if ((lados & AlcaDeJanela.Lado.Direita) != 0) pedido.X += andou.X;
		if ((lados & AlcaDeJanela.Lado.Cima) != 0) pedido.Y -= andou.Y;   // a borda de cima sobe: o chat cresce

		Vector2 tela = GetViewport().GetVisibleRect().Size;
		Janelas.GuardarChat(Janelas.Arranjo, Janelas.PrenderOChat(pedido, tela, Hud.PeDoStatus), tela);
		// AVISA TODO MUNDO, e nao so o chat: o teto do painel de status depende de onde o chat termina.
		Janelas.Avisar();
	}

	/// <summary>
	/// POE O PAINEL NO TAMANHO DO ARRANJO. De fabrica sao os numeros do `Montar` (12, 552, -292, -12); mexido, a
	/// fracao guardada presa nesta tela. Roda no nascimento, quando o arranjo muda e quando a tela muda.
	/// </summary>
	private void AplicarOTamanho()
	{
		if (!IsInstanceValid(_painel)) return;
		ArranjoDaInterface a = Janelas.Arranjo;
		Vector2 t = Janelas.ChatNaTela(a, GetViewport().GetVisibleRect().Size, Hud.PeDoStatus);

		const float M = Janelas.ChatMargem;
		// O TEXTO ENCOLHE ANTES DO PAINEL: com o minimo de fabrica (150) um chat baixo nao caberia no proprio
		// painel, e o painel cresceria sozinho de volta.
		_texto.CustomMinimumSize = new Vector2(0, a.ChatMexido ? TextoMinimoMexido : 150f);
		// E SE AINDA ASSIM NAO COUBER (a caixa de escrever aberta num chat minimo), cresce pra CIMA: pra baixo e
		// pra fora da tela.
		_painel.GrowVertical = a.ChatMexido ? Control.GrowDirection.Begin : Control.GrowDirection.End;
		_painel.OffsetLeft = M;
		_painel.OffsetRight = M + t.X;
		_painel.OffsetTop = -M - t.Y;
		_painel.OffsetBottom = -M;
		AlcaSegueOPainel();
	}

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--diagjanelas`)
	// =====================================================================
	/// <summary>A alca de redimensionar do chat.</summary>
	public AlcaDeJanela AlcaDeTeste => _alca;

	/// <summary>A caixa do texto (as falas), em px da tela -- a bancada confere que ela continua dentro do painel.</summary>
	public Rect2 TextoDeTeste => _texto.GetGlobalRect();
}
