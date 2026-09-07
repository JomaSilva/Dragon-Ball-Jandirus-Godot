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
	/// <summary>O chat ocupa o canto de baixo (236 px, `Chat.cs`); o painel fica logo ACIMA dele, ainda no canto esquerdo.</summary>
	private const float AlturaDoChat = 236f, Folga = 10f;

	public bool VisivelDeTeste => Visible;
	public bool AssistindoDeTeste => _assistindo;
	public string TituloDeTeste => _titulo.Text;
	public void ClicarAssistirDeTeste() => AlternarAssistir();
	public void ClicarChaveDeTeste() => Hud.Instancia?.AlternarChave();

	public override void _Ready()
	{
		AnchorLeft = 0; AnchorRight = 0; AnchorTop = 1; AnchorBottom = 1;
		OffsetLeft = 14; OffsetRight = 14 + Largura;
		OffsetTop = -Altura - AlturaDoChat - Folga; OffsetBottom = -AlturaDoChat - Folga;
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
	}

	/// <summary>O retrato chegou (`S2C.Chave`). Decide se este canto aparece, e pra quem.</summary>
	public void Receber(ChaveNaTela c, int localId)
	{
		if (c.Aviso == 2) { Esconder(); return; }
		bool lutando = localId != 0 && (c.CorpoA == localId || c.CorpoB == localId);
		if (c.Fase == 0 || lutando) { if (Visible) Esconder(); return; }
		_titulo.Text = c.MinhaChave >= 0 ? "TORNEIO: aguarde a sua vez" : "TORNEIO em andamento";
		Visible = true;
	}

	private void Esconder()
	{
		if (_assistindo) AlternarAssistir();
		Visible = false;
	}

	private void AlternarAssistir()
	{
		_assistindo = !_assistindo;
		World.Instancia?.AssistirTorneio(_assistindo);
		_assistir.Text = _assistindo ? "Parar de assistir" : "Assistir torneio";
	}
}
