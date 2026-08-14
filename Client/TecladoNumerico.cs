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
	/// <summary>
	/// QUANTOS ALGARISMOS CABEM -- os do PROPRIO TETO, e nao um numero escrito aqui.
	///
	/// ============================ ELE ERA 4, E ISSO TRANCAVA A NAVE ============================
	/// A constante nascia com o comentario "quatro cobrem os 500 do maior teto do jogo com folga", e
	/// era verdade: o unico cliente deste teclado era a maquina de gravidade (teto 500). Depois
	/// chegou a senha da Capital Ship, que se declara `Forma.Numero, 0, 999999` e cujo botao promete
	/// "um código de até 6 dígitos" -- e o quinto algarismo simplesmente NAO ENTRAVA. O visor nem
	/// ficava vermelho: ele parava de aceitar dedo, que e o jeito mais silencioso de recusar.
	///
	/// Ninguem tinha como notar: o teclado nao reclama, e quem trancou a nave com "1234" acha que
	/// escolheu um codigo de quatro digitos por vontade propria.
	///
	/// Agora o teto de algarismos SAI DO TETO DO NUMERO, entao ele nao pode discordar dele: a
	/// gravidade continua com tres (500), a senha ganha os seis que o rotulo promete, e o proximo
	/// cliente deste widget nasce certo sem ninguem lembrar de mexer aqui. Ver a bancada
	/// `--diagembarque`, familia 6, que cobra isso pra TODA acao `Forma.Numero` do catalogo.
	/// ======================================================================================
	/// </summary>
	private int MaxDigitos => Math.Max(1, ((long)Math.Max(_max, 0)).ToString().Length);

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
		grade.AddChild(Tecla("0", () =>
		{
			if (_digitado.Length > 0 && _digitado.Length < MaxDigitos) _digitado += "0";
		}));

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

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--diagembarque`)
	// =====================================================================
	/// <summary>O que o VISOR esta mostrando. E o unico jeito de ver o que o dedo conseguiu digitar.</summary>
	public string VisorDeTeste => IsInstanceValid(_visor) ? _visor.Text : "";

	/// <summary>
	/// APERTA UMA TECLA DESTE TECLADO pelo rotulo ("7", "&lt;", "C", "Confirmar", "Cancelar").
	///
	/// Pelo SINAL do botao, e nao pelo `Action` que ele guarda: o teto de algarismos mora dentro do
	/// tratador, e uma bancada que escrevesse `_digitado` na mao mediria o atalho -- ela passaria com
	/// o teto quebrado, que e exatamente o defeito que esta superficie existe pra pegar.
	/// </summary>
	public bool ApertarTecla(string rotulo)
	{
		foreach (Button b in Botoes(this))
			if (b.Text == rotulo) { b.EmitSignal(BaseButton.SignalName.Pressed); return true; }
		return false;
	}

	private static IEnumerable<Button> Botoes(Node raiz)
	{
		foreach (Node n in raiz.GetChildren())
		{
			if (n is Button b) yield return b;
			foreach (Button f in Botoes(n)) yield return f;
		}
	}
}
