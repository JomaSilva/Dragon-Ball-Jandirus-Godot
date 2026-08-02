using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O painel de status. Substitui o statpanel nativo do BYOND (que morreu no meio do caminho
/// e virou aba HTML la) por nodes do Godot.
///
/// O BP proprio aparece como "???" ate o personagem ter meio de medir poder -- essa e a
/// regra do jogo, nao um detalhe de interface. O que ele SEMPRE ve e o BP real (o que treinou),
/// porque esse ele sente; o expresso e o que um scouter leria.
/// </summary>
public partial class Hud : CanvasLayer
{
	/// <summary>O painel vivo. O mundo chama pra narrar golpe sem precisar procurar o node.</summary>
	public static Hud? Instancia { get; private set; }

	private Label _linha = null!, _atividade = null!, _combate = null!, _relato = null!;
	private ProgressBar _hp = null!, _ki = null!;
	private double _relatoAte;

	public override void _Ready()
	{
		Instancia = this;
		var caixa = new VBoxContainer
		{
			AnchorRight = 0, AnchorBottom = 0,
			OffsetLeft = 12, OffsetTop = 12,
			CustomMinimumSize = new Vector2(240, 0),
		};
		AddChild(caixa);

		_linha = new Label { Text = "..." };
		_linha.AddThemeColorOverride("font_color", new Color("e8e8f0"));
		_linha.AddThemeColorOverride("font_outline_color", new Color("000000"));
		_linha.AddThemeConstantOverride("outline_size", 4);
		caixa.AddChild(_linha);

		_hp = Barra(caixa, new Color("c04a4a"));
		_ki = Barra(caixa, new Color("4a8ac0"));

		_atividade = new Label { Text = "T treinar   M meditar" };
		_atividade.AddThemeColorOverride("font_color", new Color("9fb4d8"));
		_atividade.AddThemeColorOverride("font_outline_color", new Color("000000"));
		_atividade.AddThemeConstantOverride("outline_size", 4);
		caixa.AddChild(_atividade);

		_combate = Texto(caixa, new Color("c8b48a"));
		_relato = Texto(caixa, new Color("ffd7a0"));
		MostrarCombate();

		if (GameClient.Instance is { } cli)
		{
			cli.SheetUpdated += Mostrar;
			cli.ActivityChanged += MostrarAtividade;
			cli.MiraMudou += _ => MostrarCombate();
			cli.LetalidadeMudou += _ => MostrarCombate();
			Mostrar(cli.Sheet);
		}
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
		if (GameClient.Instance is not { } cli) return;
		cli.SheetUpdated -= Mostrar;
		cli.ActivityChanged -= MostrarAtividade;
	}

	private void MostrarAtividade(Protocol.Activity a) => _atividade.Text = a switch
	{
		Protocol.Activity.Treinando => "TREINANDO   (T para parar)",
		Protocol.Activity.Meditando => "MEDITANDO   (M para parar)",
		_ => "T treinar   M meditar",
	};

	/// <summary>Rotulo com contorno preto -- sem ele o texto some sobre o cenario claro.</summary>
	private static Label Texto(Control pai, Color cor)
	{
		var l = new Label();
		l.AddThemeColorOverride("font_color", cor);
		l.AddThemeColorOverride("font_outline_color", new Color("000000"));
		l.AddThemeConstantOverride("outline_size", 4);
		pai.AddChild(l);
		return l;
	}

	// =====================================================================
	// COMBATE
	// =====================================================================
	/// <summary>
	/// O estado de luta em uma linha: onde estou mirando, se a guarda esta erguida e se o
	/// golpe e letal. E aqui que a guarda aparece -- os .dmi nao tem pose de bloqueio, entao
	/// sem esta linha o jogador nao teria como saber que ALT esta fazendo alguma coisa.
	/// </summary>
	private void MostrarCombate()
	{
		if (GameClient.Instance is not { } cli) return;
		string zona = cli.ZonaMirada == 0 ? "livre" : Protocol.ZonaDe(cli.ZonaMirada);
		_combate.Text = "ESPACO socar   SHIFT+ESPACO pesado   ALT guarda\n"
						+ $"mira: {zona} (0-4)   golpe: {(cli.Letal ? "LETAL" : "nao-letal")} (K)";
		_combate.AddThemeColorOverride("font_color", cli.Letal ? new Color("e07070") : new Color("c8b48a"));
	}

	/// <summary>
	/// Narra o golpe pra quem esta nele. Diz ONDE acertou, nao so quanto: o corpo e por
	/// partes, e saber que o braco esta indo embora e o que faz mudar de mira ou recuar.
	/// </summary>
	public void Narrar(Protocol.HitEvent h, int localId)
	{
		bool bati = h.Atacante == localId;
		string quem = bati ? "acertou" : "levou";
		string txt = (Jandirus.Core.Combat.Desfecho)h.Desfecho switch
		{
			Jandirus.Core.Combat.Desfecho.Critico => $"CRITICO! {quem} {h.Dano:0.#} em {h.Membro}",
			Jandirus.Core.Combat.Desfecho.Aparou => $"aparado com {h.Membro} ({h.Dano:0.#})",
			Jandirus.Core.Combat.Desfecho.Contra => bati ? "CONTRA-ATAQUE recebido" : "CONTRA-ATAQUE!",
			Jandirus.Core.Combat.Desfecho.Esquivou => bati ? "esquivou do seu golpe" : "voce esquivou",
			Jandirus.Core.Combat.Desfecho.Errou => bati ? "errou" : "",
			_ => $"{quem} {h.Dano:0.#} em {h.Membro}",
		};
		if (h.Decepou) txt += "  -- MEMBRO ARRANCADO";
		else if (h.Quebrou) txt += "  -- QUEBROU";
		if (h.Morreu) txt += "  -- MORTE";
		else if (h.Nocauteou) txt += "  -- NOCAUTE";

		if (txt.Length == 0) return;
		_relato.Text = txt;
		_relatoAte = 3;
	}

	public override void _Process(double delta)
	{
		if (_relatoAte <= 0) return;
		_relatoAte -= delta;
		if (_relatoAte <= 0) _relato.Text = "";
	}

	private static ProgressBar Barra(Control pai, Color cor)
	{
		var b = new ProgressBar { CustomMinimumSize = new Vector2(220, 14), ShowPercentage = false, MaxValue = 1 };
		var estilo = new StyleBoxFlat { BgColor = cor };
		b.AddThemeStyleboxOverride("fill", estilo);
		pai.AddChild(b);
		return b;
	}

	private void Mostrar(SheetState f)
	{
		string nome = GameClient.Instance?.LocalName ?? "";
		_linha.Text = $"{nome}  [{f.Class}]\nBP {Numero(f.BP)}   expresso {Numero(f.ExpressedBP)}";
		_hp.Value = Mathf.Clamp(f.HP / 100.0, 0, 1);
		_ki.Value = f.MaxKi > 0 ? Mathf.Clamp(f.Ki / f.MaxKi, 0, 1) : 0;
	}

	/// <summary>BP fica em bilhoes rapido: notacao curta em vez de 14 digitos na tela.</summary>
	private static string Numero(double v) => v switch
	{
		>= 1e12 => $"{v / 1e12:0.##} T",
		>= 1e9 => $"{v / 1e9:0.##} B",
		>= 1e6 => $"{v / 1e6:0.##} M",
		>= 1e3 => $"{v / 1e3:0.##} k",
		_ => v.ToString("0.#"),
	};
}
