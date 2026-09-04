using System;
using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A CAIXA DE TEXTO DO MENU E ============================
/// O `input(...) as text` do DM, que o menu nao sabia fazer -- o historico esta escrito em
/// `Cadaver.EpitafioPadrao`: *"o menu deste port sabe fazer duas perguntas -- escolher da lista e
/// digitar numero -- e nao sabe pedir texto"*. O dono pediu a terceira (2026-09-04): *"ao criar a
/// lapide deveria ter a opcao de escrever manualmente na lapide, abrindo uma caixa de texto"*.
///
/// Irma do <see cref="TecladoNumerico"/>: mesmo painel, mesmo par de botoes, mesmo ciclo de vida
/// (o `MenuDeInteracao` a poe num `CenterContainer` e a solta ao fechar). O que muda e o campo, que e
/// um `LineEdit` de verdade -- e por isso ela precisa avisar o resto do jogo que alguem esta
/// DIGITANDO (<see cref="Digitando"/>, a quinta fonte de `Foco.Digitando`): sem isso o "E" do
/// epitafio abriria outro menu e o espaco daria um soco.
///
/// O TEXTO VAI COMO ARGUMENTO DO VERBO, cru. Quem apara, tira quebra de linha e corta no teto e o
/// servidor (`Cadaver.EpitafioLimpo`) -- a caixa so limita o COMPRIMENTO (`MaxLength`), porque o fio
/// tem teto proprio (`Protocol.MaxArgDeVerbo`) e um pacote maior que ele nem sai.
/// =====================================================================================
/// </summary>
public partial class CaixaDeTexto : PanelContainer
{
	/// <summary>Ha uma caixa destas aberta com o teclado dentro dela? Lido por `Foco.Digitando`.</summary>
	public static bool Digitando { get; private set; }

	private readonly string _titulo, _inicial;
	private readonly int _teto;
	private readonly Action<string> _aoConfirmar;
	private readonly Action _aoCancelar;
	private LineEdit _campo = null!;

	public CaixaDeTexto(string titulo, string inicial, int teto, Action<string> aoConfirmar, Action aoCancelar)
	{
		_titulo = titulo;
		_inicial = inicial;
		_teto = Math.Max(1, teto);
		_aoConfirmar = aoConfirmar;
		_aoCancelar = aoCancelar;
	}

	public override void _Ready()
	{
		AddThemeStyleboxOverride("panel", Tema.Caixa(Tema.Painel, Tema.BordaViva, 12));

		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
		caixa.AddThemeConstantOverride("separation", 6);
		AddChild(caixa);

		caixa.AddChild(new Label { Text = _titulo, HorizontalAlignment = HorizontalAlignment.Center });

		_campo = new LineEdit
		{
			Text = _inicial,
			MaxLength = _teto,
			CustomMinimumSize = new Vector2(0, 36),
			PlaceholderText = "escreva aqui",
		};
		// ENTER GRAVA. E o gesto de toda caixa de texto; sem ele a pessoa digita, aperta Enter, e nada.
		_campo.TextSubmitted += _ => Confirmar();
		caixa.AddChild(_campo);

		var dica = new Label { Text = $"até {_teto} letras -- Enter grava, Esc cancela", HorizontalAlignment = HorizontalAlignment.Center };
		dica.AddThemeColorOverride("font_color", Tema.TextoFraco);
		dica.AddThemeFontSizeOverride("font_size", 12);
		caixa.AddChild(dica);

		var linha = new HBoxContainer();
		var cancelar = new Button { Text = "Cancelar", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		cancelar.Pressed += () => _aoCancelar();
		linha.AddChild(cancelar);
		var ok = new Button { Text = "Gravar", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		ok.Pressed += Confirmar;
		linha.AddChild(ok);
		caixa.AddChild(linha);

		// O FOCO VAI PRO CAMPO, com o padrao selecionado: quem quer o "Aqui jaz Fulano" so aperta
		// Enter; quem quer outra coisa comeca a digitar por cima. Adiado porque no `_Ready` o node
		// ainda nao esta na arvore de foco.
		Digitando = true;
		_campo.CallDeferred(Control.MethodName.GrabFocus);
		_campo.CallDeferred(LineEdit.MethodName.SelectAll);
	}

	public override void _ExitTree() => Digitando = false;

	public override void _UnhandledKeyInput(InputEvent ev)
	{
		// ESC CANCELA -- e fica aqui, e nao no menu de pausa: com `Digitando` ligado os atalhos do jogo
		// estao mudos, entao quem tem que ouvir o Esc e a propria caixa.
		if (ev is InputEventKey { Pressed: true, Keycode: Key.Escape })
		{
			GetViewport().SetInputAsHandled();
			_aoCancelar();
		}
	}

	private void Confirmar() => _aoConfirmar(_campo.Text);

	/// <summary>Só bancada: o que está no campo agora.</summary>
	public string TextoDeTeste => _campo.Text;

	/// <summary>Só bancada: troca o texto como quem apaga e digita por cima, e devolve o que estava lá (o padrão do DM).</summary>
	public string DigitarDeTeste(string texto)
	{
		string antes = _campo.Text;
		_campo.Text = texto;
		return antes;
	}

	/// <summary>Só bancada: o botão "Gravar".</summary>
	public void ConfirmarDeTeste() => Confirmar();
}
