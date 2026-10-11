using Godot;

namespace Jandirus.Client;

/// <summary>
/// A ALCA -- o pedaco invisivel de uma peca da tela que o mouse PEGA pra mexer nela: a borda e o canto que
/// redimensionam (o menu P, o chat), o canto que escala (os paineis do topo) e a barra que arrasta (o titulo do
/// menu P). Pedido do dono de 2026-10-10 (*"redimensionar ou mudar de lugar o menu P como se fosse uma janela do
/// windows"*).
///
/// ============================ ELA SO EXISTE ONDE SE PEGA ============================
/// A alca cobre o retangulo inteiro da peca, mas so a FAIXA de borda dela e "ela" pro mouse (`_HasPoint`): o
/// miolo continua sendo da peca que esta embaixo -- os botoes do menu, o texto do chat, o mundo atras do painel
/// de status. Nao ha no nenhum por cima do conteudo.
///
/// E ELA NAO DESENHA NADA PARADA. A borda pega acende com o mouse em cima (e o cursor vira o de redimensionar);
/// longe do mouse a tela e exatamente a de antes -- as bancadas que fotografam continuam fotografando o mesmo.
///
/// ============================ O CLIQUE DELA NAO E DO MUNDO ============================
/// O mundo le o mouse em dois lugares: o duplo clique (alvo e Zanzoken, `World._UnhandledInput`) e o modo de
/// construir (`ModoDeConstruir._Input`). Contra o primeiro, a alca PARA o evento (`MouseFilter.Stop`): o que ela
/// pegou nao chega no `_UnhandledInput`. Contra o segundo -- que roda ANTES da interface -- e o proprio modo de
/// construir que pergunta se o mouse esta numa alca (`MouseNaInterface`).
///
/// Quem usa a alca diz o que fazer com o arrasto (<see cref="AoPegar"/>, <see cref="AoArrastar"/>,
/// <see cref="AoSoltar"/>): ela nao conhece menu, chat nem painel. O arrasto sai da posicao do EVENTO, e nao do
/// cursor do sistema -- uma bancada que empurra eventos sem mexer no cursor arrasta igual ao dedo.
/// ====================================================================================
/// </summary>
public partial class AlcaDeJanela : Control
{
	/// <summary>Os lados de uma peca. `Mover` nao e borda: e a alca de arrastar a peca inteira (a barra de titulo).</summary>
	[Flags]
	public enum Lado { Nenhum = 0, Esquerda = 1, Direita = 2, Cima = 4, Baixo = 8, Mover = 16 }

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagjanelas): a alca deixa o clique passar -- o duplo clique em cima de uma
	/// borda chega ao mundo (marca alvo, da o Zanzoken). Lido pela propria linha do filtro, em `_GuiInput`.
	/// </summary>
	public static bool VazaOCliqueDeTeste;

	/// <summary>Que lados desta peca se pegam. So `Mover` = a alca inteira arrasta a peca.</summary>
	public Lado Lados = Lado.Nenhum;

	/// <summary>A espessura da faixa de borda que o mouse pega, em px.</summary>
	public float Faixa = 8f;

	/// <summary>O lado do quadrado de canto: ali dentro a faixa pega DUAS bordas de uma vez.</summary>
	public float Canto = 18f;

	/// <summary>Quanto a alca sobra pra FORA da peca, em px: a linha acesa e desenhada em cima da borda de verdade.</summary>
	public float Sobra;

	/// <summary>O arrasto comecou: que lados foram pegos.</summary>
	public Action<Lado>? AoPegar;

	/// <summary>O mouse andou com o botao apertado: os lados pegos e o quanto andou DESDE que pegou, em px da tela.</summary>
	public Action<Lado, Vector2>? AoArrastar;

	/// <summary>O botao subiu: e a hora de gravar.</summary>
	public Action? AoSoltar;

	private Lado _sobre, _pego;
	private bool _arrastando;
	private Vector2 _inicio;

	/// <summary>Ha um arrasto em curso nesta alca?</summary>
	public bool Arrastando => _arrastando;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Stop;
		FocusMode = FocusModeEnum.None;   // nunca o teclado: Espaco e Enter sao o soco e o chat
	}

	private bool Tem(Lado l) => (Lados & l) != 0;

	/// <summary>
	/// QUE LADOS ESTE PONTO PEGA (em px desta alca). Fora da faixa de borda: nenhum -- o ponto e da peca de baixo.
	/// Dentro do quadrado de um canto cujas duas bordas existem: as duas.
	/// </summary>
	public Lado ZonaEm(Vector2 p)
	{
		if (Janelas.Arranjo.Travado) return Lado.Nenhum;
		if (p.X < 0f || p.Y < 0f || p.X >= Size.X || p.Y >= Size.Y) return Lado.Nenhum;
		if (Lados == Lado.Mover) return Lado.Mover;

		bool naFaixa = (Tem(Lado.Esquerda) && p.X < Faixa) || (Tem(Lado.Direita) && p.X >= Size.X - Faixa)
					|| (Tem(Lado.Cima) && p.Y < Faixa) || (Tem(Lado.Baixo) && p.Y >= Size.Y - Faixa);
		if (!naFaixa) return Lado.Nenhum;

		Lado z = Lado.Nenhum;
		if (Tem(Lado.Esquerda) && p.X < Canto) z |= Lado.Esquerda;
		else if (Tem(Lado.Direita) && p.X >= Size.X - Canto) z |= Lado.Direita;
		if (Tem(Lado.Cima) && p.Y < Canto) z |= Lado.Cima;
		else if (Tem(Lado.Baixo) && p.Y >= Size.Y - Canto) z |= Lado.Baixo;
		return z;
	}

	/// <summary>SO A FAIXA E "A ALCA" PRO MOUSE: o miolo continua sendo de quem esta embaixo. Ver o cabecalho.</summary>
	public override bool _HasPoint(Vector2 ponto) => _arrastando || ZonaEm(ponto) != Lado.Nenhum;

	/// <summary>O cursor diz o que o arrasto vai fazer, antes do clique. (O motor pede o formato como numero.)</summary>
	public override int _GetCursorShape(Vector2 ponto) => (int)CursorDe(_arrastando ? _pego : ZonaEm(ponto));

	private static CursorShape CursorDe(Lado z)
	{
		bool h = (z & (Lado.Esquerda | Lado.Direita)) != 0, v = (z & (Lado.Cima | Lado.Baixo)) != 0;
		if (z == Lado.Mover) return CursorShape.Move;
		if (h && v)
		{
			bool descendo = z == (Lado.Esquerda | Lado.Cima) || z == (Lado.Direita | Lado.Baixo);
			return descendo ? CursorShape.Fdiagsize : CursorShape.Bdiagsize;
		}
		return h ? CursorShape.Hsize : v ? CursorShape.Vsize : CursorShape.Arrow;
	}

	public override void _GuiInput(InputEvent evento)
	{
		// O CLIQUE PARA AQUI. Com o defeito ligado ele segue pro pai (que o ignora) e sobra pro mundo.
		MouseFilter = VazaOCliqueDeTeste ? MouseFilterEnum.Pass : MouseFilterEnum.Stop;

		switch (evento)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
				if (b.Pressed)
				{
					Lado z = ZonaEm(b.Position);
					if (z == Lado.Nenhum) return;
					_pego = z;
					_arrastando = true;
					_inicio = b.GlobalPosition;
					AoPegar?.Invoke(z);
				}
				else if (_arrastando)
				{
					_arrastando = false;
					AoSoltar?.Invoke();
				}
				QueueRedraw();
				break;

			case InputEventMouseMotion m:
				if (_arrastando) AoArrastar?.Invoke(_pego, m.GlobalPosition - _inicio);
				else
				{
					Lado z = ZonaEm(m.Position);
					if (z == _sobre) break;
					_sobre = z;
					QueueRedraw();
				}
				break;
		}
	}

	public override void _Notification(int o)
	{
		// O MOUSE SAIU DA FAIXA: a borda apaga. (Durante o arrasto ela fica acesa: `_pego` manda.)
		if (o == NotificationMouseExit && _sobre != Lado.Nenhum)
		{
			_sobre = Lado.Nenhum;
			QueueRedraw();
		}
	}

	/// <summary>
	/// A BORDA ACESA: uma linha de 2 px em cima da borda pega (ou um "L" no canto), no laranja de foco do tema. So
	/// com o mouse em cima ou no meio de um arrasto -- parada, a alca nao desenha nada. A barra de arrastar nao
	/// desenha nunca: quem diz que ela pega e o cursor.
	/// </summary>
	public override void _Draw()
	{
		Lado z = _arrastando ? _pego : _sobre;
		if (z == Lado.Nenhum || z == Lado.Mover) return;

		var cor = new Color(Tema.BordaViva, _arrastando ? 1f : 0.85f);
		const float G = 2f;
		bool canto = (z & (Lado.Esquerda | Lado.Direita)) != 0 && (z & (Lado.Cima | Lado.Baixo)) != 0;
		// no canto, um "L" curto; na borda, a borda inteira (por dentro do que sobra pra fora da peca)
		float largura = Size.X - 2f * Sobra, altura = Size.Y - 2f * Sobra;
		float trechoX = canto ? Math.Min(Canto + 10f, largura) : largura;
		float trechoY = canto ? Math.Min(Canto + 10f, altura) : altura;

		if ((z & Lado.Cima) != 0)
			DrawRect(new Rect2((z & Lado.Direita) != 0 ? Size.X - Sobra - trechoX : Sobra, Sobra, trechoX, G), cor);
		if ((z & Lado.Baixo) != 0)
			DrawRect(new Rect2((z & Lado.Direita) != 0 ? Size.X - Sobra - trechoX : Sobra, Size.Y - Sobra - G, trechoX, G), cor);
		if ((z & Lado.Esquerda) != 0)
			DrawRect(new Rect2(Sobra, (z & Lado.Baixo) != 0 ? Size.Y - Sobra - trechoY : Sobra, G, trechoY), cor);
		if ((z & Lado.Direita) != 0)
			DrawRect(new Rect2(Size.X - Sobra - G, (z & Lado.Baixo) != 0 ? Size.Y - Sobra - trechoY : Sobra, G, trechoY), cor);
	}

	/// <summary>Poe a alca em cima de um retangulo da tela (em px do pai dela), sobrando <see cref="Sobra"/> pra fora.</summary>
	public void Cobrir(Rect2 peca)
	{
		Rect2 r = peca.Grow(Sobra);
		if (Position != r.Position) Position = r.Position;
		if (Size != r.Size) Size = r.Size;
	}

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--diagjanelas`)
	// =====================================================================
	/// <summary>
	/// O PONTO DA TELA onde o mouse pega estes lados (o meio da borda; no canto, o canto). A bancada clica AQUI
	/// com eventos de mouse de verdade -- e a mesma conta de <see cref="ZonaEm"/>, vista de fora.
	/// </summary>
	public Vector2 PontoDeTeste(Lado lados)
	{
		float meio = Faixa / 2f;
		float x = (lados & Lado.Esquerda) != 0 ? meio : (lados & Lado.Direita) != 0 ? Size.X - meio : Size.X / 2f;
		float y = (lados & Lado.Cima) != 0 ? meio : (lados & Lado.Baixo) != 0 ? Size.Y - meio : Size.Y / 2f;
		return GetGlobalRect().Position + new Vector2(x, y);
	}

	/// <summary>A borda esta acesa (o mouse em cima ou um arrasto em curso)?</summary>
	public bool AcesaDeTeste => (_arrastando ? _pego : _sobre) is not (Lado.Nenhum or Lado.Mover);
}
