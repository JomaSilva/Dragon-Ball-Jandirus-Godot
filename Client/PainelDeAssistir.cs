using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O CANTO INFERIOR ESQUERDO DO TORNEIO -- o pedido do dono (2026-09-07): *"aparecer no canto inferior
/// esquerdo (assistir torneio) para aqueles que estao esperando sua vez ou aqueles que nao estao
/// participando"*.
///
/// Dois botoes: ASSISTIR (a camera vai pra luta de agora e volta quando se aperta de novo -- e o
/// `client.eye = fighter` do `Assistir_Torneio` do DM, `Tournament.dm:597-608`, sem o `input()` de
/// escolher lutador: aqui ela mira no MEIO dos dois) e CHAVEAMENTO (reabre o painel da chave).
///
/// QUEM VE ISTO e decidido pelo `S2C.Chave`: todo mundo na zona do torneio e todo participante recebe
/// o retrato; o painel aparece quando ha chave e a pessoa NAO e um dos dois que estao lutando -- quem
/// esta na luta tem coisa melhor pra fazer, e a camera dela e a dela. Some quando o torneio acaba.
/// </summary>
public partial class PainelDeAssistir : PanelContainer
{
	private Label _titulo = null!;
	private Button _assistir = null!, _chave = null!;
	private bool _assistindo;

	private const float Largura = 250f, Altura = 74f;

	/// <summary>
	/// O respiro entre este canto e o que esta embaixo dele (o chat, ou a faixa da mineracao).
	///
	/// ============================ ELE LE O RETANGULO DO CHAT, E NAO UM NUMERO DELE ============================
	/// O painel fica logo ACIMA do chat, ainda no canto esquerdo. A altura do chat estava copiada aqui (236 px) -- e
	/// envelheceu calada: o chat cresceu pra 280 e este canto ficou 46 px por DENTRO dele, com as falas desenhadas
	/// por cima dos dois botoes. Agora que o chat tem o tamanho que o jogador quiser (`Chat.Tamanho.cs`), o lugar
	/// sai do painel dele NA TELA (<see cref="Assentar"/>); os 236 ficaram so pro defeito de bancada.
	/// ==========================================================================================================
	/// </summary>
	private const float Folga = 10f;

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagjanelas): o canto volta a contar que o chat tem 236 px de altura, qualquer
	/// que seja o tamanho dele -- e fica por baixo das falas. Falso em jogo, sempre.
	/// </summary>
	public static bool AlturaFixaDoChatDeTeste;
	private const float AlturaDoChatDeAntes = 236f;

	public bool VisivelDeTeste => Visible;
	public bool AssistindoDeTeste => _assistindo;
	public string TituloDeTeste => _titulo.Text;
	public void ClicarAssistirDeTeste() => AlternarAssistir();
	public void ClicarChaveDeTeste() => Hud.Instancia?.AlternarChave();

	public override void _Ready()
	{
		AnchorLeft = 0; AnchorRight = 0; AnchorTop = 1; AnchorBottom = 1;
		Assentar();
		GrowHorizontal = GrowDirection.End;
		GrowVertical = GrowDirection.Begin;
		Tema.Aplicar(this);
		var v = new VBoxContainer();
		v.AddThemeConstantOverride("separation", 6);
		AddChild(v);
		_titulo = Tema.Legenda("", Tema.TextoFraco, 12);
		v.AddChild(_titulo);
		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 8);
		v.AddChild(linha);
		_assistir = new Button { Text = "Assistir torneio" };
		_assistir.Pressed += AlternarAssistir;
		linha.AddChild(_assistir);
		_chave = new Button { Text = "Chaveamento" };
		_chave.Pressed += () => Hud.Instancia?.AlternarChave();
		linha.AddChild(_chave);
		Visible = false;
		SetProcess(false);   // so se assenta enquanto esta na tela: ver `Receber` e `Esconder`
	}

	/// <summary>
	/// **O LUGAR DESTE CANTO, PELO QUE ESTA EMBAIXO DELE AGORA**: em cima do chat (o retangulo REAL dele), e em cima
	/// da faixa da mineracao quando ela esta na tela -- as duas moram ali. Sem vao pra isso (um chat alto e um
	/// painel de status grande), a direita do chat (`Janelas.JuntoDoChat`). So escreve quando o lugar muda.
	/// </summary>
	private void Assentar()
	{
		float esquerda = 14f, pe = -AlturaDoChatDeAntes - Folga;
		if (!AlturaFixaDoChatDeTeste && Chat.RetanguloNaTela is { } chat && IsInsideTree())
		{
			float tela = GetViewportRect().Size.Y;
			float empilhado = PainelDeMineracao.Instancia is { NaTela: true } faixa ? faixa.RetanguloNaTela.Size.Y + Janelas.FolgaEntrePecas : 0f;
			Vector2 canto = Janelas.JuntoDoChat(chat, new Vector2(Largura, Math.Max(Altura, Size.Y)), empilhado,
												Hud.RetanguloDoStatus?.End.Y ?? 0f, Folga);
			esquerda = canto.X;
			pe = canto.Y - tela;   // (ancora de baixo: o deslocamento e negativo)
		}

		if (OffsetLeft != esquerda) { OffsetLeft = esquerda; OffsetRight = esquerda + Largura; }
		if (OffsetBottom != pe) { OffsetTop = pe - Altura; OffsetBottom = pe; }
	}

	public override void _Process(double delta) => Assentar();

	/// <summary>O retrato chegou (`S2C.Chave`). Decide se este canto aparece, e pra quem.</summary>
	public void Receber(ChaveNaTela c, int localId)
	{
		if (c.Aviso == 2) { Esconder(); return; }
		bool lutando = localId != 0 && (c.CorpoA == localId || c.CorpoB == localId);
		if (c.Fase == 0 || lutando) { if (Visible) Esconder(); return; }
		_titulo.Text = c.MinhaChave >= 0 ? "TORNEIO: aguarde a sua vez" : "TORNEIO em andamento";
		Assentar();   // ANTES de aparecer: o chat pode ter mudado de tamanho desde a ultima vez
		Visible = true;
		SetProcess(true);
	}

	private void Esconder()
	{
		if (_assistindo) AlternarAssistir();
		Visible = false;
		SetProcess(false);
	}

	/// <summary>SO PRA BANCADA: poe o canto na tela (ou o tira) sem torneio nenhum, e devolve onde ele ficou.</summary>
	public Rect2 MostrarDeTeste(bool naTela)
	{
		Visible = naTela;
		SetProcess(naTela);
		Assentar();
		return GetGlobalRect();
	}

	private void AlternarAssistir()
	{
		_assistindo = !_assistindo;
		World.Instancia?.AssistirTorneio(_assistindo);
		_assistir.Text = _assistindo ? "Parar de assistir" : "Assistir torneio";
	}
}
