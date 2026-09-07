using Godot;

namespace Jandirus.Client;

/// <summary>
/// O CONVITE DO TORNEIO, no canto inferior direito: titulo, quanto tempo falta pra responder, e os
/// dois botoes -- Participar e Recusar (o `alert(M, invite_text(), ..., "Participar", "Ignorar")` do
/// `Tournament.dm:338`, que la travava a tela do jogador ate ele responder).
///
/// ============================ O SERVIDOR E QUEM DECIDE ============================
/// O painel so PERGUNTA: os botoes mandam `trn_participar` / `trn_recusar` pelo canal de verbos e o
/// servidor confere de novo se ha vaga, se a inscricao ainda esta aberta e se a pessoa e elegivel.
/// O prazo aqui e o do pacote (`S2C.Torneio`), e vencido ele o painel some sozinho -- um convite
/// sem resposta e um "nao", como no DM (quem ignorava o alert nao entrava).
/// ==================================================================================
/// </summary>
public partial class PainelDoTorneio : PanelContainer
{
	private Label _titulo = null!, _prazo = null!;
	private Button _participar = null!, _recusar = null!;
	private double _restante;
	private byte _tipo;

	/// <summary>Largura e altura do painel, em px de tela; o canto e o mesmo do painel da direita (14 px da borda).</summary>
	private const float Largura = 300f, Altura = 112f;

	public bool VisivelDeTeste => Visible;
	public double SegundosDeTeste => _restante;
	public string TituloDeTeste => _titulo.Text;
	public byte TipoDeTeste => _tipo;
	public void ClicarParticiparDeTeste() => Participar();
	public void ClicarRecusarDeTeste() => Recusar();

	public override void _Ready()
	{
		AnchorLeft = 1; AnchorRight = 1; AnchorTop = 1; AnchorBottom = 1;
		OffsetLeft = -Largura - 14; OffsetRight = -14;
		OffsetTop = -Altura - 40; OffsetBottom = -40;
		GrowHorizontal = GrowDirection.Begin;
		GrowVertical = GrowDirection.Begin;
		Tema.Aplicar(this);
		var v = new VBoxContainer();
		v.AddThemeConstantOverride("separation", 6);
		AddChild(v);
		_titulo = Tema.Legenda("", Tema.Destaque, 14);
		_titulo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_titulo.CustomMinimumSize = new Vector2(Largura - 24, 0);
		v.AddChild(_titulo);
		_prazo = Tema.Legenda("", Tema.TextoFraco, 12);
		v.AddChild(_prazo);
		var linha = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		linha.AddThemeConstantOverride("separation", 8);
		v.AddChild(linha);
		_participar = new Button { Text = "Participar" };
		_participar.Pressed += Participar;
		linha.AddChild(_participar);
		_recusar = new Button { Text = "Recusar" };
		_recusar.Pressed += Recusar;
		linha.AddChild(_recusar);
		Visible = false;
		SetProcess(false);
	}

	/// <summary>O convite chegou (`S2C.Torneio`, aviso 1).</summary>
	public void Convidar(byte tipo, int segundos, string titulo)
	{
		_tipo = tipo;
		_titulo.Text = titulo.Length > 0 ? titulo : (tipo == 2 ? "O TORNEIO DO OUTRO MUNDO vai comecar!" : "O TORNEIO DE ARTES MARCIAIS vai comecar!");
		_restante = Math.Max(1, segundos);
		Mostrar();
		Visible = true;
		SetProcess(true);
	}

	/// <summary>O convite fechou (respondido, vencido, ou o torneio cancelou) -- `S2C.Torneio`, aviso 2.</summary>
	public void Fechar()
	{
		Visible = false;
		SetProcess(false);
	}

	public override void _Process(double delta)
	{
		_restante -= delta;
		if (_restante <= 0) { Fechar(); return; }
		Mostrar();
	}

	private void Mostrar() => _prazo.Text = $"responda em {Math.Ceiling(_restante):0} s";

	private void Participar()
	{
		GameClient.Instance?.SendVerbo("trn_participar");
		Fechar();
	}

	private void Recusar()
	{
		GameClient.Instance?.SendVerbo("trn_recusar");
		Fechar();
	}
}
