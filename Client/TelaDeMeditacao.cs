using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A TECLA M PERGUNTA: meditar normal, ou mergulhar na propria mente?
///
/// ============================ O PEDIDO, LITERAL ============================
/// *"N faca a meditacao profunda ser um VERB DO MENU DO P. faca q ao MEDITAR uma TELINHA vai abrir
/// e perguntar se vc quer so MEDITAR NORMAL ou ir pra MEDITACAO PROFUNDA"*.
///
/// O QUE HAVIA ANTES eram DOIS gestos que nao se apresentavam um ao outro: a tecla M (meditar) e um
/// verb "Meditação profunda" perdido na aba Aprendizado do menu P -- **apagado** enquanto voce nao
/// estivesse meditando. Ou seja: pra achar a mente era preciso ja saber que existia a tecla M, ir
/// ate o menu, abrir a aba certa e reparar num botao que so acende em um estado especifico. Quem
/// nunca leu o guia nunca soube que a dimensao mental existia.
///
/// Hoje a porta e o proprio gesto: quem aperta M ve as duas saidas lado a lado, com o que ha em
/// cada uma escrito na tela. O verb do menu P foi **DELETADO** (ver `Habilidades.Montar`) -- um verb
/// morto no menu e exatamente o tipo de coisa que confunde depois.
/// ==========================================================================
///
/// ============================ O QUE ELA NAO FAZ ============================
/// **NAO DECIDE NADA, E NAO GUARDA NADA.** Ela nem manda pacote: quem manda e o
/// <see cref="LocalPlayer"/>, que e o dono do `_atividade` -- a pose do proprio corpo sai de la, e
/// uma telinha que falasse direto com o `GameClient` deixaria o LocalPlayer achando que ninguem
/// estava meditando (o M seguinte reabriria a pergunta em vez de parar a meditacao).
///
/// **E NAO INVENTA REGRA.** As recusas que ela desenha sao as do servidor, lidas da MESMA funcao
/// (<see cref="DimensaoMental.PorQueNaoMergulhar"/>). O botao apagado aqui e sempre um "nao" que o
/// servidor tambem diria.
///
/// **E NAO TRAVA TECLA NENHUMA** -- ela nao entra no <see cref="Foco"/>, exatamente como as irmas
/// dela (o menu da tecla E, a mochila, a mesa de tecnicas). Isso e o que responde a pergunta "e se
/// o jogador levar um golpe com ela aberta?": nao ha nada pra destravar, o jogo continua correndo
/// por baixo, e quem for nocauteado com a telinha na tela a ve FECHAR sozinha (ver `_Process`) --
/// nunca ficar presa oferecendo uma escolha que ja nao existe.
/// ===========================================================================
///
/// O MOLDE E O DO <see cref="MenuDeInteracao"/> (a tecla E), ate na camada 4: `CanvasLayer` +
/// `CenterContainer` + `Tema.Painel1(14)` + uma coluna de botoes + o rodape "(Esc)". Nao ha desenho
/// novo aqui, e nao devia haver -- este jogo ja tem uma cara pra "escolha uma coisa".
/// </summary>
public partial class TelaDeMeditacao : CanvasLayer
{
	/// <summary>A telinha viva, ou nula antes do mundo. E por aqui que a tecla M a alcanca.</summary>
	public static TelaDeMeditacao? Instancia { get; private set; }

	private Control _raiz = null!;
	private Button _normal = null!;
	private Button _profunda = null!;
	private Label _motivo = null!;

	/// <summary>
	/// O QUE FAZER COM A RESPOSTA -- posto a cada <see cref="Abrir"/>, e sempre por ATRIBUICAO.
	///
	/// Nunca `+=`: assinatura acumulada e o defeito que este port ja pagou caro (as telas que
	/// assinavam com lambda e deixavam 19 orfaos por relog). Aqui ha um campo so, quem abre escreve
	/// nele, e cancelar o apaga -- nao ha lista pra crescer.
	/// </summary>
	private Action<bool>? _resposta;

	/// <summary>A zona em que a telinha abriu -- ver `_Process`: mudar de mundo com ela aberta a fecha.</summary>
	private ZoneKey _zonaDeAbertura;

	public override void _Ready()
	{
		Layer = 4;   // a mesma do menu da tecla E: acima do menu do jogo (3), abaixo do QTE (5)
		Instancia = this;
		Montar();
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
	}

	private void Montar()
	{
		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_raiz.AddChild(centro);

		PanelContainer painel = Tema.Painel1(14);
		centro.AddChild(painel);

		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
		caixa.AddThemeConstantOverride("separation", 6);
		painel.AddChild(caixa);

		var titulo = new Label { Text = "Meditar", HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 20);
		caixa.AddChild(titulo);
		caixa.AddChild(new HSeparator());

		// A EXPLICACAO DE CADA CAMINHO VEM NA DICA DO BOTAO (o `TooltipText`), como no menu da tecla E.
		// O texto da profunda e o MESMO que o verb apagado do menu P carregava -- ele explicava bem o
		// que ha la dentro, e jogar fora a explicacao junto com o botao seria perder a parte boa.
		_normal = new Button
		{
			Text = "Meditar normal",
			TooltipText = "Senta e recupera o fôlego onde você está. O corpo fica aqui, acordado -- "
						  + "qualquer tecla de movimento tira você da meditação.",
		};
		_normal.Pressed += EscolherNormal;
		caixa.AddChild(_normal);

		_profunda = new Button
		{
			Text = "Meditação profunda",
			TooltipText =
				"Mergulhe na própria mente pra lutar contra uma cópia exata de você -- mesmo poder, "
				+ "mesma velocidade: a luta mede a sua habilidade, não o seu BP.\n\n"
				+ "Lá dentro nada é real: ferimentos NÃO voltam com você, não há Zenkai, e o treino "
				+ "rende só um quarto do normal. Seu corpo fica meditando onde estava -- quem bater "
				+ "nele te acorda na hora.\n\n"
				+ "Se você meditar COLADO em alguém que já está em transe, você entra na mente DELE "
				+ "(e o reflexo dele se desfaz: lá dentro ficam só vocês dois).",
		};
		_profunda.Pressed += EscolherProfunda;
		caixa.AddChild(_profunda);

		// A LINHA DO MOTIVO. Ela e a metade que faz a recusa valer a pena: um botao cinza sem frase
		// nenhuma e a mesma parede do verb apagado que esta telinha veio substituir.
		_motivo = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(340, 0),
			Visible = false,
		};
		_motivo.AddThemeFontSizeOverride("font_size", 12);
		caixa.AddChild(_motivo);

		caixa.AddChild(new HSeparator());

		// CANCELAR NAO MEDITA NADA -- e o caminho que mais some das telas de escolha. Ver `Fechar`.
		var cancelar = new Button { Text = "Cancelar (Esc)" };
		cancelar.Pressed += Fechar;
		caixa.AddChild(cancelar);
	}

	// =====================================================================
	// ABRIR, FECHAR, RESPONDER
	// =====================================================================
	/// <summary>
	/// PERGUNTA. <paramref name="resposta"/> recebe `true` pra profunda e `false` pra normal, e
	/// **nao e chamada nenhuma vez** se o jogador cancelar (Esc, o botao, ou o M de novo).
	/// </summary>
	public void Abrir(Action<bool> resposta)
	{
		_resposta = resposta;
		_zonaDeAbertura = GameClient.Instance?.Zone ?? default;
		_raiz.Visible = true;
		Atualizar();
	}

	/// <summary>
	/// FECHA SEM MEDITAR NADA.
	///
	/// A `_resposta` e APAGADA aqui, e nao so ignorada: enquanto ela existir, um clique atrasado (o
	/// sinal de um botao que ja estava sendo apertado quando a tela sumiu) ainda a chamaria -- e o
	/// personagem sentaria pra meditar depois de o jogador ter dito que nao.
	/// </summary>
	public void Fechar()
	{
		_resposta = null;
		_raiz.Visible = false;
	}

	private void EscolherNormal() => Responder(profunda: false);

	private void EscolherProfunda()
	{
		// O SEGUNDO CADEADO. O botao ja esta `Disabled` quando ha recusa, mas quem aperta e um sinal
		// do Godot e a bancada aperta por sinal tambem (`ApertarDesenhado`): a guarda tem que estar
		// no CAMINHO, e nao so na aparencia dele.
		if (Recusa().Length > 0) { Atualizar(); return; }
		Responder(profunda: true);
	}

	private void Responder(bool profunda)
	{
		Action<bool>? quem = _resposta;
		Fechar();               // apaga a `_resposta` ANTES de responder: uma escolha, um envio
		quem?.Invoke(profunda);
	}

	// =====================================================================
	// O QUE ESTA NA TELA AGORA
	// =====================================================================
	/// <summary>
	/// AS RECUSAS SAO AS DO SERVIDOR, e vem da mesma funcao que ele le -- nenhuma regra nasce aqui.
	/// Vazio = pode mergulhar.
	/// </summary>
	private static string Recusa()
	{
		if (GameClient.Instance is not { } cli) return "sem conexão com o servidor.";
		return DimensaoMental.PorQueNaoMergulhar(
			jaEmTranse: DimensaoMental.EhAMente(cli.Zone),
			ko: cli.Sheet.KO,
			morto: cli.Sheet.Morto);
	}

	/// <summary>
	/// O AVISO DE COMBATE -- e um AVISO, nunca uma recusa. Ver `World.NaLuta`: nem o servidor nem o
	/// DM proibem meditar em briga; o que acontece e que o primeiro golpe no corpo te acorda. Dizer
	/// isso e ensinar; apagar o botao seria o cliente legislando.
	/// </summary>
	private static string Aviso() =>
		World.Instancia is { NaLuta: true }
			? "você está em combate: o primeiro golpe no seu corpo te tira do transe na hora."
			: "";

	private void Atualizar()
	{
		string recusa = Recusa();
		_profunda.Disabled = recusa.Length > 0;

		string texto = recusa.Length > 0 ? recusa : Aviso();
		_motivo.Visible = texto.Length > 0;
		_motivo.Text = texto;
		_motivo.AddThemeColorOverride("font_color", recusa.Length > 0 ? Tema.Perigo : Tema.TextoFraco);
	}

	public override void _Process(double delta)
	{
		if (!_raiz.Visible) return;

		// ============================ ELA NAO SOBREVIVE AO QUE A INVALIDA ============================
		// *"se a telinha travar teclas, ela tem que destravar sempre -- inclusive se o jogador levar
		// um golpe com ela aberta"*. Ela nao trava tecla nenhuma (ver o cabecalho), mas o caso do
		// golpe continua sendo real de outro jeito: cair no chao (ou morrer) com a pergunta aberta
		// deixaria na tela uma escolha que nao existe mais.
		//
		// Cair FECHA -- e nao "apaga os dois botoes": quem foi nocauteado tem coisa melhor pra olhar
		// que um menu, e a tecla M ja nao responde nesse estado (`LocalPlayer.LerAcoes` retorna antes).
		// Trocar de mundo com ela aberta fecha pelo mesmo motivo -- inclusive a viagem PRA mente, que
		// e o que acontece um instante depois de escolher "profunda".
		if (GameClient.Instance is not { } cli) { Fechar(); return; }
		if (cli.Sheet.Imobilizado) { Fechar(); return; }
		if (cli.Zone.Hash != _zonaDeAbertura.Hash) { Fechar(); return; }

		// O ESTADO E VIVO: entrar em combate com a telinha aberta acende o aviso na hora, e a recusa
		// que aparecer (entrar em transe por outro caminho, por exemplo) apaga o botao na hora.
		Atualizar();
	}

	/// <summary>
	/// O ESC CANCELA -- e ele precisa vir do `_Input`, e nao do `_UnhandledInput`.
	///
	/// ============================ POR QUE, EXATAMENTE ============================
	/// O `_UnhandledInput` corre a arvore DE BAIXO PRA CIMA, e o `PauseMenu` nasce DEPOIS desta tela
	/// no `Boot` -- ou seja ele veria o Esc primeiro, abriria a pausa por cima da pergunta e
	/// engoliria a tecla (`SetInputAsHandled`). O "Cancelar (Esc)" escrito no rodape seria uma
	/// mentira: a tela continuaria aberta atras da pausa.
	///
	/// O `_Input` roda antes de qualquer `_UnhandledInput`, entao a pergunta -- que e a coisa mais
	/// na frente da tela -- fecha primeiro. E o mesmo caminho (e o mesmo motivo) do fantasma de
	/// assentar construcao, `TelaDeConstrucao._Input`.
	///
	/// **SO O ESC, E SO COM ELA ABERTA**: um `_Input` largo e o jeito de roubar tecla do jogo
	/// inteiro sem ninguem descobrir de onde.
	/// ======================================================================
	/// </summary>
	public override void _Input(InputEvent evento)
	{
		if (!_raiz.Visible || Foco.Digitando) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k) return;
		if (k.Keycode != Key.Escape) return;

		Fechar();
		GetViewport().SetInputAsHandled();
	}

	// =====================================================================
	// SUPERFICIE DE BANCADA
	// =====================================================================
	/// <summary>
	/// A telinha esta na tela? Le o NODE, e nao um `bool` proprio -- pelo mesmo argumento escrito no
	/// `MenuDeInteracao`: o defeito mora no widget, e uma bancada que le a intencao passa por ele.
	/// </summary>
	public bool NaTela => IsInstanceValid(_raiz) && _raiz.Visible;

	/// <summary>Os rotulos desenhados, com o `(apagado)` de quem esta recusado.</summary>
	public List<string> BotoesDesenhados()
	{
		var r = new List<string>();
		if (!NaTela) return r;
		foreach (Button b in new[] { _normal, _profunda })
			if (IsInstanceValid(b)) r.Add(b.Disabled ? $"{b.Text} (apagado)" : b.Text);
		return r;
	}

	/// <summary>A frase que o jogador esta lendo (recusa ou aviso), ou vazio.</summary>
	public string MotivoNaTela => NaTela && IsInstanceValid(_motivo) && _motivo.Visible ? _motivo.Text : "";

	/// <summary>
	/// Aperta um dos dois botoes PELO SINAL, como o dedo faz -- nao chama `Responder` por dentro,
	/// senao a bancada pularia justamente a ligacao (`Pressed +=`) que pode estar faltando.
	/// Falso quando a telinha nao esta na tela ou o botao esta apagado.
	/// </summary>
	public bool ApertarDesenhado(bool profunda)
	{
		if (!NaTela) return false;
		Button b = profunda ? _profunda : _normal;
		if (!IsInstanceValid(b) || b.Disabled) return false;
		b.EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}
}
