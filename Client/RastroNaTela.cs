using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O RASTRO DO SENSE NA TELA (2026-10-19) -- a regra e quem pode ser seguido estao em <see cref="RastroDoSense"/>. Duas coisas:
///   * o SELO, no alto e no meio da tela: quem, a quantos tiles e pra que lado (ou em que lugar, se em outra zona), e o botao que
///     para. Sem a pessoa na lista ele fica apagado, dizendo que o rastro se perdeu -- e volta a acender sozinho quando ela voltar;
///   * a SETA, em volta do seu corpo, apontando pra ela. Ela SOME com a pessoa perto o bastante pra se ver na tela (o `PointArrow`
///     do DU esconde a seta a menos de 40% da altura da tela, `Misc/Utilities.dm:50-88`) e com a pessoa em outra zona (entre dois
///     planetas nao ha "pra que lado").
///
/// DE ONDE SAI O LADO, do mais fino pro mais grosso: a posicao do corpo no meu snapshot (so de quem a lista trouxe com nome e e o
/// unico com aquele nome -- `World.PosicaoDoUnicoComONome`); a celula exata que o SCOUTER manda; e o rumo de oito pontos da lista (o
/// `get_dir` do DM: ele sempre aponta pra um lado que encurta a distancia, entao segui-lo chega).
///
/// SEM SHADER, como o disco do radar (`RadarNaTela`): `_Draw` com poligono e linha, o material padrao do canvas.
/// </summary>
public partial class RastroNaTela : Control
{
	public static RastroNaTela? Instancia { get; private set; }

	/// <summary>A que distancia do centro do corpo (px de tela) a seta gira.</summary>
	public const float RaioDaSeta = 74f;

	/// <summary>O comprimento da seta, da base a ponta (px de tela).</summary>
	public const float TamanhoDaSeta = 18f;

	/// <summary>O DU: com o alvo a menos desta fracao da altura da tela, a seta some -- ele ja se ve.</summary>
	public const float FracaoDaTelaQueEscondeASeta = 0.4f;

	private static readonly Color CorDaSeta = new("8fdcff");
	private static readonly Color ContornoDaSeta = new("0b2a3ad9");

	private PanelContainer _selo = null!;
	private Label _texto = null!;

	/// <summary>A presenca seguida no ultimo pacote lido (nula = perdeu o rastro).</summary>
	private Protocol.PresencaState? _leitura;

	/// <summary>
	/// DEFEITO INJETADO (robo `--diagsenserastro`): o selo so le a lista quando o rastro COMECA -- o pacote seguinte do Sense nao chega na
	/// tela, e a distancia e o rumo ficam os do clique. Falso em jogo.
	/// </summary>
	internal static bool RastroCongeladoDeTeste;

	// ---- o que foi desenhado (as portas do robo) ----
	internal string TextoDeTeste => _selo.Visible ? _texto.Text : "";
	internal bool SetaNaTelaDeTeste { get; private set; }
	internal Vector2 DirecaoDaSetaDeTeste { get; private set; }
	/// <summary>De onde saiu o lado da ultima seta: "corpo" (o snapshot), "celula" (o scouter) ou "rumo" (os oito pontos).</summary>
	internal string FonteDaSetaDeTeste { get; private set; } = "";

	public override void _Ready()
	{
		Instancia = this;
		AnchorRight = 1;
		AnchorBottom = 1;
		MouseFilter = MouseFilterEnum.Ignore;

		_selo = new PanelContainer
		{
			Name = "Selo",
			AnchorLeft = 0.5f, AnchorRight = 0.5f,
			OffsetLeft = -230, OffsetRight = 230, OffsetTop = 14,
			GrowHorizontal = GrowDirection.Both,
			Visible = false,
		};
		_selo.AddThemeStyleboxOverride("panel", Tema.Caixa(Tema.Painel, Tema.Ki, 8));
		AddChild(_selo);

		var linha = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		linha.AddThemeConstantOverride("separation", 10);
		_selo.AddChild(linha);

		_texto = new Label { VerticalAlignment = VerticalAlignment.Center };
		_texto.AddThemeFontSizeOverride("font_size", 13);
		linha.AddChild(_texto);

		var parar = new Button { Text = "parar", FocusMode = FocusModeEnum.None, TooltipText = "Parar de seguir pelo Sense" };
		parar.AddThemeFontSizeOverride("font_size", 12);
		parar.Pressed += RastroDoSense.Parar;
		linha.AddChild(parar);

		// A SESSAO NASCE SEM RASTRO: o estado e estatico e sobreviveria ao relog, seguindo alguem do personagem anterior.
		RastroDoSense.Parar();
		RastroDoSense.Mudou += Atualizar;
		if (GameClient.Instance is { } cli) cli.SentidosMudaram += AoSentidos;
		Atualizar();
	}

	public override void _ExitTree()
	{
		RastroDoSense.Mudou -= Atualizar;
		if (GameClient.Instance is { } cli) cli.SentidosMudaram -= AoSentidos;
		RastroDoSense.Parar();
		if (Instancia == this) Instancia = null;
	}

	private void AoSentidos()
	{
		if (!RastroCongeladoDeTeste) Atualizar();
	}

	/// <summary>Le a lista de agora e reescreve o selo. Chamado a cada pacote do Sense e quando o rastro muda.</summary>
	private void Atualizar()
	{
		GameClient? cli = GameClient.Instance;
		if (!RastroDoSense.Ativo || cli == null)
		{
			_selo.Visible = false;
			_leitura = null;
			QueueRedraw();
			return;
		}
		_leitura = RastroDoSense.Leitura(cli.Sentidos);
		_selo.Visible = true;
		_texto.Text = Frase(_leitura, RastroDoSense.Rotulo);
		_texto.AddThemeColorOverride("font_color", _leitura == null ? Tema.TextoFraco : Tema.Texto);
		QueueRedraw();
	}

	/// <summary>O texto do selo. Publico pra a aba Sense escrever a mesma frase no cartao do rastro.</summary>
	public static string Frase(Protocol.PresencaState? p, string rotulo)
	{
		if (p is not { } x) return $"Rastro perdido: {rotulo} -- fora do alcance do seu Sense";
		if (x.Distancia == Protocol.DistanciaDesconhecida)
			return x.Zona.Length > 0 ? $"Rastro: {rotulo} · em {x.Zona}" : $"Rastro: {rotulo} · longe demais pra dizer onde";
		if (x.Distancia == 0 || x.Rumo == 0) return $"Rastro: {rotulo} · aqui do seu lado";
		return $"Rastro: {rotulo} · {x.Distancia} tiles · {Protocol.NomeDoRumo(x.Rumo)}";
	}

	public override void _Process(double delta)
	{
		if (!RastroDoSense.Ativo) return;
		// SEM SENSE NEM SCOUTER A LISTA PARA DE CHEGAR, e a ultima ficaria na memoria apontando pra onde a pessoa ESTAVA. O rastro
		// para junto, dizendo por que.
		if (GameClient.Instance is { } cli && !cli.Atributos.Tem(Protocol.Poder.Sense) && !cli.Atributos.Tem(Protocol.Poder.Scouter))
		{
			Chat.Sistema("sem o Sense (nem scouter) o rastro se perdeu.");
			RastroDoSense.Parar();
			return;
		}
		// a seta acompanha o SEU corpo a cada quadro (e o do alvo, quando a posicao dele e conhecida)
		QueueRedraw();
	}

	public override void _Draw()
	{
		SetaNaTelaDeTeste = false;
		FonteDaSetaDeTeste = "";
		if (_leitura is not { } p || p.Distancia == Protocol.DistanciaDesconhecida) return;
		if (World.Instancia is not { } w || w.PosicaoLocal is not { } eu) return;

		Transform2D tela = w.GetViewport().GetCanvasTransform();
		Vector2 centro = tela * eu;
		Vector2 lado;
		float pxAteEle;
		if (p.Nome.Length > 0 && w.PosicaoDoUnicoComONome(p.Nome) is { } ele)
		{
			lado = tela * ele - centro;
			pxAteEle = lado.Length();
			FonteDaSetaDeTeste = "corpo";
		}
		else if (p.X >= 0 && p.Y >= 0)
		{
			// O SCOUTER MANDA A CELULA: o centro dela, no mundo
			var celula = new Vector2((p.X + 0.5f) * ZoneCollision.TileSize, (p.Y + 0.5f) * ZoneCollision.TileSize);
			lado = tela * celula - centro;
			pxAteEle = lado.Length();
			FonteDaSetaDeTeste = "celula";
		}
		else if (RastroDoSense.DirecaoDoRumo(p.Rumo) is { } rumo)
		{
			lado = rumo;
			pxAteEle = p.Distancia * ZoneCollision.TileSize * tela.Scale.Y;
			FonteDaSetaDeTeste = "rumo";
		}
		else return;

		if (lado.LengthSquared() < 0.0001f || pxAteEle < FracaoDaTelaQueEscondeASeta * GetViewportRect().Size.Y) return;
		Vector2 d = lado.Normalized();
		Vector2 n = new(-d.Y, d.X);
		Vector2 ponta = centro + d * (RaioDaSeta + TamanhoDaSeta);
		Vector2 baseDaSeta = centro + d * RaioDaSeta;
		Vector2[] tri = [ponta, baseDaSeta + n * (TamanhoDaSeta * 0.55f), baseDaSeta - n * (TamanhoDaSeta * 0.55f)];
		DrawColoredPolygon(tri, CorDaSeta);
		DrawPolyline([tri[0], tri[1], tri[2], tri[0]], ContornoDaSeta, 2f, true);
		SetaNaTelaDeTeste = true;
		DirecaoDaSetaDeTeste = d;
	}
}
