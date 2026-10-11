using Godot;

namespace Jandirus.Client;

/// <summary>
/// **OS DOIS PAINEIS DO TOPO, EM ESCALA.** Pedido do dono de 2026-10-10: *"poder redimensionar o chat (...), o mesmo
/// pros stats do canto superior esquerdo e direito"* -- o painel de STATUS (nome, vida, ki, vigor, nutricao, BP)
/// e o painel do CORPO (a lua, a mira e o boneco de dano).
///
/// ============================ ESCALA, E NAO LARGURA E ALTURA ============================
/// Os dois paineis tem o tamanho do que cabe dentro deles (o da direita ate cresce e encolhe sozinho quando a
/// lua aparece). "Redimensionar" um painel desses e deixa-lo MAIOR ou MENOR inteiro: letra, barra e boneco
/// juntos, de 70% a 160% (<see cref="Janelas.EscalaMinima"/>, <see cref="Janelas.EscalaMaxima"/>).
///
///   * o de STATUS cresce a partir do canto de cima a ESQUERDA (ele nao sai dos 14 px da borda);
///   * o do CORPO, a partir do canto de cima a DIREITA.
///
/// A LETRA NAO BORRA: com `OversamplingWithScale` a fonte e desenhada de novo no tamanho ampliado, em vez de a
/// letra pequena ser esticada. A 100% nada disso e ligado -- o painel e o de sempre.
///
/// COMO SE MEXE: pelo canto de baixo de cada painel (o de fora da tela pra dentro: direito no status, esquerdo
/// no corpo), que acende com o mouse em cima, ou pelos deslizadores da secao "Interface" das opcoes.
///
/// QUEM MORA EM VOLTA le o retangulo REAL daqui (<see cref="RetanguloDoStatus"/>, <see cref="RetanguloDoCorpo"/>):
/// a faixa do radio encosta na beirada de verdade do painel de status, o chat nao sobe alem do pe dele, a
/// janela de desejos comeca embaixo do painel do corpo (ela ja lia o retangulo na tela).
/// ========================================================================================
/// </summary>
public partial class Hud
{
	/// <summary>O lado do quadradinho de canto que pega o mouse, em px.</summary>
	private const float LadoDaAlca = 18f;

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagjanelas): o painel escala SEM redesenhar a fonte -- a letra de 100% e
	/// esticada (os degraus de pixel aparecem a 160%). Lido pela propria linha do `Escalar`. Falso em jogo, sempre.
	/// </summary>
	public static bool LetraEsticadaDeTeste;

	private PanelContainer _painelDeStatus = null!, _painelDoCorpo = null!;
	private AlcaDeJanela _alcaDoStatus = null!, _alcaDoCorpo = null!;
	private float _escalaAoPegar = 1f;

	/// <summary>O retangulo do painel de status NA TELA (ja com a escala), ou nulo sem HUD.</summary>
	public static Rect2? RetanguloDoStatus =>
		Instancia is { } h && IsInstanceValid(h._painelDeStatus) ? h._painelDeStatus.GetGlobalRect() : null;

	/// <summary>O retangulo do painel do corpo NA TELA (ja com a escala), ou nulo sem HUD.</summary>
	public static Rect2? RetanguloDoCorpo =>
		Instancia is { } h && IsInstanceValid(h._painelDoCorpo) ? h._painelDoCorpo.GetGlobalRect() : null;

	/// <summary>
	/// O Y DO PE DO PAINEL DE STATUS, pelo arranjo de AGORA (0 sem HUD). Sai da conta (o tamanho a 100% vezes a
	/// escala que o arranjo manda) e nao do no ja escalado: quem pergunta e o chat, no mesmo aviso de "o arranjo
	/// mudou" em que o painel ainda vai se reaplicar -- a ordem em que os dois sao avisados nao pode decidir nada.
	/// </summary>
	public static float PeDoStatus
	{
		get
		{
			if (Instancia is not { } h || !IsInstanceValid(h._painelDeStatus)) return 0f;
			return h._painelDeStatus.Position.Y + TamanhoBase(h._painelDeStatus).Y * h.EscalaDoArranjo(doStatus: true);
		}
	}

	/// <summary>O tamanho de um painel a 100%: o que ele mede sem escala (e, antes da primeira arrumacao, o minimo dele).</summary>
	private static Vector2 TamanhoBase(Control painel) => painel.Size.Max(painel.GetCombinedMinimumSize());

	/// <summary>A escala que o arranjo manda pra um dos paineis, presa na faixa e nesta tela.</summary>
	private float EscalaDoArranjo(bool doStatus)
	{
		ArranjoDaInterface a = Janelas.Arranjo;
		Control painel = doStatus ? _painelDeStatus : _painelDoCorpo;
		return Janelas.EscalaQueCabe(doStatus ? a.EscalaDoStatus : a.EscalaDoCorpo, TamanhoBase(painel),
									 GetViewport().GetVisibleRect().Size, 0f);
	}

	/// <summary>
	/// Os dois cantos que escalam, irmaos dos paineis (por cima deles). O de status fica no canto de baixo a
	/// direita; o do corpo, no de baixo a esquerda -- os cantos que andam quando o painel cresce.
	/// </summary>
	private void MontarAsEscalas(Control raiz)
	{
		_alcaDoStatus = new AlcaDeJanela
		{
			Name = "AlcaDoStatus", Faixa = LadoDaAlca, Canto = LadoDaAlca, Size = new Vector2(LadoDaAlca, LadoDaAlca),
			Lados = AlcaDeJanela.Lado.Direita | AlcaDeJanela.Lado.Baixo,
			TooltipText = "arraste pra aumentar ou diminuir o painel",
		};
		_alcaDoStatus.AoPegar = _ => _escalaAoPegar = _painelDeStatus.Scale.X;
		_alcaDoStatus.AoArrastar = (_, andou) => Arrastar(doStatus: true, andou);
		_alcaDoStatus.AoSoltar = Janelas.Gravar;
		raiz.AddChild(_alcaDoStatus);

		_alcaDoCorpo = new AlcaDeJanela
		{
			Name = "AlcaDoCorpo", Faixa = LadoDaAlca, Canto = LadoDaAlca, Size = new Vector2(LadoDaAlca, LadoDaAlca),
			Lados = AlcaDeJanela.Lado.Esquerda | AlcaDeJanela.Lado.Baixo,
			TooltipText = "arraste pra aumentar ou diminuir o painel",
		};
		_alcaDoCorpo.AoPegar = _ => _escalaAoPegar = _painelDoCorpo.Scale.X;
		_alcaDoCorpo.AoArrastar = (_, andou) => Arrastar(doStatus: false, andou);
		_alcaDoCorpo.AoSoltar = Janelas.Gravar;
		raiz.AddChild(_alcaDoCorpo);

		_painelDeStatus.ItemRectChanged += AlcasSeguemOsPaineis;
		_painelDoCorpo.ItemRectChanged += AlcasSeguemOsPaineis;

		// `Janelas.Mudou` E ESTATICO e o `SizeChanged` e da raiz: metodo nomeado, e `-=` no `SoltarAsEscalas`.
		Janelas.Mudou += AplicarAsEscalas;
		GetViewport().SizeChanged += AplicarAsEscalas;
		AplicarAsEscalas();

		// O CHAT NASCEU ANTES DESTE PAINEL (`Boot.MontarMundo`) e se prendeu sem saber onde o status termina:
		// agora que ha um pe de verdade, ele confere de novo.
		Janelas.Avisar();
	}

	/// <summary>O `_ExitTree` desta metade.</summary>
	private void SoltarAsEscalas()
	{
		Janelas.Mudou -= AplicarAsEscalas;
		if (GetViewport() is { } vp) vp.SizeChanged -= AplicarAsEscalas;
	}

	/// <summary>Poe os dois paineis na escala do arranjo. Roda no nascimento, quando o arranjo muda e quando a tela muda.</summary>
	private void AplicarAsEscalas()
	{
		if (!IsInstanceValid(_painelDeStatus) || !IsInstanceValid(_painelDoCorpo)) return;
		Escalar(_painelDeStatus, EscalaDoArranjo(doStatus: true), new Vector2(0f, 0f));
		Escalar(_painelDoCorpo, EscalaDoArranjo(doStatus: false), new Vector2(1f, 0f));
		AlcasSeguemOsPaineis();
	}

	/// <summary>
	/// Escala um painel em volta de um canto dele (`pivo`, em fracao do tamanho: 0,0 = em cima a esquerda). A
	/// fonte acompanha a escala (`OversamplingWithScale`) -- menos a 100%, onde o painel fica como sempre foi.
	///
	/// ============================ DUAS COISAS QUE O MOTOR NAO FAZ SOZINHO (as duas medidas na foto) ============================
	///   1. REDESENHAR OS FILHOS. O tamanho da letra e escolhido na hora em que cada rotulo se DESENHA, e mudar a
	///      escala do painel nao redesenha ninguem (so a matriz muda): sem o redesenho a letra de antes
	///      continuava la, esticada -- a primeira foto saiu com os degraus de pixel a 160%.
	///   2. REFAZER A CONTA DA FONTE. Quem guarda "que escala a fonte acompanha" e o proprio modo, calculado quando
	///      ele e LIGADO. Com o modo ja ligado, mudar a escala nao o recalcula na hora: de 160% pra 70% os
	///      rotulos se redesenhavam com a letra de 160% espremida (a segunda foto: "VIDA" ilegivel, e so a
	///      hora -- o unico texto que mudou depois -- nitida). Por isso, a cada mudanca: a escala PRIMEIRO, e o
	///      modo desligado e religado DEPOIS dela.
	/// ==========================================================================================================================
	/// </summary>
	private static void Escalar(Control painel, float escala, Vector2 pivo)
	{
		var alvo = new Vector2(escala, escala);
		CanvasItem.OversamplingWithScaleEnum fonte = escala == 1f || LetraEsticadaDeTeste
			? CanvasItem.OversamplingWithScaleEnum.ParentNode
			: CanvasItem.OversamplingWithScaleEnum.Enabled;
		if (painel.Scale == alvo && painel.PivotOffsetRatio == pivo && painel.OversamplingWithScale == fonte) return;

		painel.PivotOffsetRatio = pivo;
		painel.Scale = alvo;
		RefazerALetra(painel, fonte);
	}

	/// <summary>Religa o modo da fonte com a escala ja no lugar, e redesenha o painel inteiro. Ver <see cref="Escalar"/>.</summary>
	private static void RefazerALetra(Control painel, CanvasItem.OversamplingWithScaleEnum fonte)
	{
		if (!SemReligarAFonteDeTeste) painel.OversamplingWithScale = CanvasItem.OversamplingWithScaleEnum.ParentNode;
		painel.OversamplingWithScale = fonte;
		Redesenhar(painel);
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagjanelas): a escala muda sem o modo da fonte ser religado -- o painel que ja
	/// estava aumentado e passa pra outro tamanho fica com a letra do tamanho ANTERIOR. Falso em jogo, sempre.
	/// </summary>
	public static bool SemReligarAFonteDeTeste;

	/// <summary>Pede o redesenho de um no e de tudo o que esta embaixo dele.</summary>
	private static void Redesenhar(Node no)
	{
		if (no is CanvasItem item) item.QueueRedraw();
		foreach (Node filho in no.GetChildren()) Redesenhar(filho);
	}

	private void AlcasSeguemOsPaineis()
	{
		if (!IsInstanceValid(_alcaDoStatus) || !IsInstanceValid(_alcaDoCorpo)) return;
		Rect2 s = _painelDeStatus.GetGlobalRect(), c = _painelDoCorpo.GetGlobalRect();
		_alcaDoStatus.Position = s.End - new Vector2(LadoDaAlca, LadoDaAlca);
		_alcaDoCorpo.Position = new Vector2(c.Position.X, c.End.Y - LadoDaAlca);
	}

	/// <summary>
	/// O ARRASTO DE UM CANTO vira escala pela DIAGONAL do painel: andar o tamanho de uma diagonal pra fora e +100%.
	/// (Pela diagonal e nao por um eixo so: o canto e puxado nas duas direcoes, e as duas tem de contar.)
	/// </summary>
	private void Arrastar(bool doStatus, Vector2 andou)
	{
		Vector2 t = TamanhoBase(doStatus ? _painelDeStatus : _painelDoCorpo);
		if (t.X < 1f || t.Y < 1f) return;
		// o status cresce pra direita e pra baixo; o corpo, pra ESQUERDA e pra baixo
		var diagonal = new Vector2(doStatus ? t.X : -t.X, t.Y);
		PedirEscala(doStatus, _escalaAoPegar + andou.Dot(diagonal) / diagonal.LengthSquared());
	}

	/// <summary>
	/// **PEDE UMA ESCALA** pra um dos paineis (o canto arrastado, o deslizador das opcoes). Ela e presa na faixa e
	/// no espaco -- o painel de status nao cresce ate encostar no chat -- e guardada em passos de 1%. Vale tambem
	/// sem HUD na tela (as opcoes abertas no lobby): ai so a faixa prende, e o resto se confere ao entrar no mundo.
	/// Nao grava o arquivo: quem grava e quem solta o mouse.
	/// </summary>
	public static void PedirEscala(bool doStatus, float pedida)
	{
		ArranjoDaInterface a = Janelas.Arranjo;
		float s = MathF.Round(pedida * 100f) / 100f;
		if (Instancia is { } h && IsInstanceValid(h._painelDeStatus) && IsInstanceValid(h._painelDoCorpo))
		{
			float chao = doStatus && Chat.RetanguloNaTela is { } chat ? chat.Position.Y - Janelas.FolgaEntrePecas : 0f;
			s = Janelas.EscalaQueCabe(s, TamanhoBase(doStatus ? h._painelDeStatus : h._painelDoCorpo),
									  h.GetViewport().GetVisibleRect().Size, chao);
			s = MathF.Floor(s * 100f + 0.001f) / 100f;   // pra BAIXO: arredondar pra cima passaria um pixel do chao
		}
		else s = Janelas.EscalaQueCabe(s, Vector2.Zero, Vector2.Zero, 0f);

		if (doStatus) a.EscalaDoStatus = s; else a.EscalaDoCorpo = s;
		Janelas.Avisar();
	}

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--diagjanelas`)
	// =====================================================================
	/// <summary>Os dois cantos que escalam.</summary>
	public AlcaDeJanela AlcaDoStatusDeTeste => _alcaDoStatus;
	public AlcaDeJanela AlcaDoCorpoDeTeste => _alcaDoCorpo;

	/// <summary>A escala que cada painel esta usando AGORA (a do no, e nao a do arranjo).</summary>
	public float EscalaDoStatusDeTeste => _painelDeStatus.Scale.X;
	public float EscalaDoCorpoDeTeste => _painelDoCorpo.Scale.X;

	/// <summary>O tamanho de cada painel a 100%.</summary>
	public Vector2 BaseDoStatusDeTeste => TamanhoBase(_painelDeStatus);
	public Vector2 BaseDoCorpoDeTeste => TamanhoBase(_painelDoCorpo);

	/// <summary>A fonte de um painel acompanha a escala dele (a letra e redesenhada, e nao esticada)?</summary>
	public bool FonteAcompanhaDeTeste(bool doStatus) =>
		(doStatus ? _painelDeStatus : _painelDoCorpo).OversamplingWithScale == CanvasItem.OversamplingWithScaleEnum.Enabled;

	/// <summary>
	/// SO PRA BANCADA: refaz a letra dos dois paineis do zero, com a escala de AGORA -- a referencia contra a qual a
	/// bancada compara a foto do que o arrasto deixou na tela. Se as duas fotos diferem, o arrasto deixou letra velha.
	/// </summary>
	public void RefazerALetraDeTeste()
	{
		foreach (Control painel in new Control[] { _painelDeStatus, _painelDoCorpo })
		{
			CanvasItem.OversamplingWithScaleEnum fonte = painel.OversamplingWithScale;
			painel.OversamplingWithScale = CanvasItem.OversamplingWithScaleEnum.ParentNode;
			painel.OversamplingWithScale = fonte;
			Redesenhar(painel);
		}
	}
}
