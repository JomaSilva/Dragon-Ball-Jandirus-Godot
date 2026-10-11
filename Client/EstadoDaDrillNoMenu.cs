using Godot;
using Jandirus.Core.Tech;

namespace Jandirus.Client;

/// <summary>
/// O PAINEL DA DRILL, EM CIMA DOS BOTOES DO MENU DA TECLA E: nivel, taxa, estoque com o teto, a reserva do
/// planeta e o preco da proxima melhoria -- o que o dono pediu pra aparecer no *"menu de interaçao"* da
/// maquina (2026-10-10).
///
/// ============================ POR QUE ELE EXISTE, SE HA "VER ESTADO" ============================
/// As outras maquinas dizem o estado no chat ("Ver estado"), e a tabela de botoes do menu e estatica: ela
/// nao sabe o preco de uma melhoria que muda a cada nivel. Na Drill a decisao do jogador e exatamente
/// essa -- vale pagar 75 mil zeni ou 150 mil recursos por mais um por segundo? --, e ter de fechar o menu,
/// ler o chat e reabrir pra escolher a moeda e o tipo de passo que faz ninguem melhorar nada.
/// ================================================================================================
///
/// ELE NAO DECIDE NADA: pergunta ao servidor quando entra na tela (`drill_ver`) e de cinco em cinco
/// segundos enquanto esta nela, e escreve as linhas que o Core monta (<see cref="Drill.Descrever"/>, as
/// mesmas do "Ver estado"). Entre duas respostas o estoque anda sozinho pela taxa
/// (<see cref="Drill.Adiante"/>): e a conta do servidor, feita aqui so pra o numero nao ficar parado.
///
/// Quem o poe na tela e o `MenuDeInteracao.Desenhar`, quando o alvo e uma Drill; ele sai junto com os
/// botoes, a cada vez que o menu se redesenha.
/// </summary>
public partial class EstadoDaDrillNoMenu : VBoxContainer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada --diagdrill): o painel recebe o estado e nao escreve nada -- o menu de antes,
	/// so com os botoes. Lido pela propria linha que escreve, em <see cref="Escrever"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool MudoDeTeste;

	/// <summary>De quanto em quanto ele pergunta de novo, com o menu aberto.</summary>
	private const double SegundosEntrePerguntas = Drill.SegundosEntreLeituras;

	/// <summary>De quanto em quanto ele reescreve o estoque que anda.</summary>
	private const double SegundosEntreEscritas = 0.25;

	private static EstadoDaDrillNoMenu? _vivo;

	/// <summary>O painel que esta NA TELA agora, ou nulo. SO PRA BANCADA (`--diagdrill`).</summary>
	public static EstadoDaDrillNoMenu? NaTela =>
		_vivo is { } v && IsInstanceValid(v) && v.IsInsideTree() && v.IsVisibleInTree() ? v : null;

	/// <summary>O que o painel esta mostrando. SO PRA BANCADA.</summary>
	public string TextoDesenhado => IsInstanceValid(_texto) ? _texto.Text : "";

	private Label _texto = null!;
	private ulong _pergunteiEm;
	private double _desdeAPergunta, _desdeAEscrita;

	public override void _Ready()
	{
		_vivo = this;
		_texto = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(340, 0),
			Text = "consultando a Drill...",
		};
		_texto.AddThemeFontSizeOverride("font_size", 14);
		AddChild(_texto);
		AddChild(new HSeparator());

		if (GameClient.Instance is { } cli) cli.DrillMudou += Escrever;
		Perguntar();
	}

	public override void _ExitTree()
	{
		if (_vivo == this) _vivo = null;
		// O `-=` DO PAR: o painel morre a cada redesenho do menu e o `GameClient` nao.
		if (GameClient.Instance is { } cli) cli.DrillMudou -= Escrever;
	}

	public override void _Process(double delta)
	{
		// O MENU FECHADO NAO PERGUNTA: o painel continua na arvore (escondido) ate o proximo redesenho.
		if (!IsVisibleInTree()) return;

		_desdeAPergunta += delta;
		if (_desdeAPergunta >= SegundosEntrePerguntas) Perguntar();

		_desdeAEscrita += delta;
		if (_desdeAEscrita >= SegundosEntreEscritas) Escrever();
	}

	private void Perguntar()
	{
		_desdeAPergunta = 0;
		if (_pergunteiEm == 0) _pergunteiEm = Time.GetTicksMsec();
		GameClient.Instance?.SendVerbo(Drill.VerboVer);
	}

	/// <summary>
	/// ESCREVE O QUE O SERVIDOR DISSE -- andado ate agora. Uma resposta de ANTES da minha pergunta e de outra
	/// Drill (ou desta, velha): enquanto a minha nao chega, o painel diz que esta consultando.
	/// </summary>
	private void Escrever()
	{
		_desdeAEscrita = 0;
		if (MudoDeTeste || !IsInstanceValid(_texto)) return;
		if (GameClient.Instance is not { DrillVista: { } visto } cli || cli.DrillVistaEm < _pergunteiEm) return;

		if (visto.Id == 0) { _texto.Text = "você se afastou da Drill."; return; }

		double segundos = (Time.GetTicksMsec() - cli.DrillVistaEm) / 1000.0;
		_texto.Text = string.Join("\n", Drill.Descrever(Drill.Adiante(visto, segundos)));
	}
}
