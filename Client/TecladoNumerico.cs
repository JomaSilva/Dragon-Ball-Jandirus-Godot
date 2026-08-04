using Godot;

namespace Jandirus.Client;

/// <summary>
/// A CALCULADORA -- dez teclas, apagar, limpar e confirmar.
///
/// ============================ POR QUE NAO UMA CAIXA DE TEXTO ============================
/// Uma `LineEdit` seria menos codigo e estaria errada por dois motivos. O primeiro e o pedido do
/// dono, que descreveu exatamente isto: "uma caixa parecida com uma calculadora so q so tem os
/// numero de 0 a 9". O segundo e o teclado do JOGO: andar, socar e voar estao todos em letras, e
/// uma caixa de texto com foco engole todas elas -- o jogador digitaria o numero e descobriria que
/// nao consegue mais andar ate clicar fora.
///
/// Com botoes nao ha foco de texto, e o `Foco.Digitando` do resto do jogo continua falso.
/// ========================================================================================
///
/// ============================ ELE E UM WIDGET, E NAO UMA TELA ============================
/// Nasceu dentro do menu de interacao (pra ajustar a gravidade) e precisou existir tambem no
/// inventario (pra ajustar os pesos). Duas copias do mesmo teclado divergiriam na primeira
/// correcao -- e a primeira correcao seria em coisa como "zero a esquerda nao entra", que e
/// exatamente o tipo de regra que so uma das copias receberia.
/// =========================================================================================
/// </summary>
public partial class TecladoNumerico : PanelContainer
{
	/// <summary>Quantos algarismos cabem. Quatro cobrem os 500 do maior teto do jogo com folga.</summary>
	private const int MaxDigitos = 4;

	private string _digitado = "";
	private Label _visor = null!;
	private readonly double _min, _max;
	private readonly Action<double> _aoConfirmar;
	private readonly Action _aoCancelar;

	public TecladoNumerico(string titulo, double min, double max,
						   Action<double> aoConfirmar, Action aoCancelar)
	{
		_min = min;
		_max = max;
		_aoConfirmar = aoConfirmar;
		_aoCancelar = aoCancelar;
		Montar(titulo);
	}

	private void Montar(string titulo)
	{
		AddThemeStyleboxOverride("panel", Tema.Caixa(Tema.Painel, Tema.BordaViva, 12));

		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(210, 0) };
		caixa.AddThemeConstantOverride("separation", 6);
		AddChild(caixa);

		var t = new Label { Text = titulo, HorizontalAlignment = HorizontalAlignment.Center };
		caixa.AddChild(t);

		_visor = new Label
		{
			Text = "0",
			HorizontalAlignment = HorizontalAlignment.Right,
			CustomMinimumSize = new Vector2(0, 36),
		};
		_visor.AddThemeFontSizeOverride("font_size", 28);
		caixa.AddChild(_visor);

		var faixa = new Label
		{
			Text = $"de {_min:0} a {_max:0}",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		faixa.AddThemeColorOverride("font_color", Tema.TextoFraco);
		faixa.AddThemeFontSizeOverride("font_size", 12);
		caixa.AddChild(faixa);

		var grade = new GridContainer { Columns = 3 };
		grade.AddThemeConstantOverride("h_separation", 4);
		grade.AddThemeConstantOverride("v_separation", 4);
		caixa.AddChild(grade);

		// 1..9 e depois o zero, que e a ordem de um teclado numerico de verdade.
		for (int i = 1; i <= 9; i++)
		{
			string d = i.ToString();
			grade.AddChild(Tecla(d, () => { if (_digitado.Length < MaxDigitos) _digitado += d; }));
		}

		grade.AddChild(Tecla("<", () => { if (_digitado.Length > 0) _digitado = _digitado[..^1]; }));

		// ZERO A ESQUERDA NAO ENTRA: "007" e um numero que ninguem quis digitar.
		grade.AddChild(Tecla("0", () => { if (_digitado.Length is > 0 and < MaxDigitos) _digitado += "0"; }));

		grade.AddChild(Tecla("C", () => _digitado = ""));

		var linha = new HBoxContainer();
		var cancelar = new Button { Text = "Cancelar", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		cancelar.Pressed += () => _aoCancelar();
		linha.AddChild(cancelar);

		var ok = new Button { Text = "Confirmar", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		ok.Pressed += () => { if (Cabe(out double v)) _aoConfirmar(v); };
		linha.AddChild(ok);
		caixa.AddChild(linha);

		Repintar();
	}

	private Button Tecla(string rotulo, Action aperto)
	{
		var b = new Button { Text = rotulo, CustomMinimumSize = new Vector2(60, 44) };
		b.Pressed += () => { aperto(); Repintar(); };
		return b;
	}

	private bool Cabe(out double valor)
	{
		valor = 0;
		return double.TryParse(_digitado.Length == 0 ? "0" : _digitado, out valor)
			   && valor >= _min && valor <= _max;
	}

	/// <summary>
	/// O VISOR AVISA ANTES DO BOTAO RECUSAR: numero fora da faixa fica vermelho ENQUANTO se digita,
	/// em vez de virar um "nao pode" depois do confirmar -- que e quando o jogador ja decidiu.
	/// </summary>
	private void Repintar()
	{
		_visor.Text = _digitado.Length == 0 ? "0" : _digitado;
		_visor.AddThemeColorOverride("font_color", Cabe(out _) ? Tema.Destaque : Tema.Perigo);
	}
}
