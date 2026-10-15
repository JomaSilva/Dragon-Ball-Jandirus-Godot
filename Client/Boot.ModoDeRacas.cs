using Godot;
using Jandirus.Core.Races;

namespace Jandirus.Client;

/// <summary>
/// ============================ A TELA DE HOSPEDAR (v0.3.0, item 34 do pedido 15) ============================
/// *"ao criar um server/hostear o criador vai ter uma tela q vai ter 2 opçoes, raças aleatorias onde o jogador n pode
/// escolher sua raça inicial, sendo aleatoria, e o modo padrao, onde o jogador escolhe"*.
///
/// O "Hospedar partida" (e o perfil de servidor proprio) passam por aqui ANTES de o servidor subir: quem hospeda escolhe o
/// modo, ele vai pro `servidor.json` (`GameServer.EscolherModoDeRacas`) e o servidor sobe ja com ele. O modo de hoje
/// aparece escrito em cima, pra quem hospeda todo dia ver o que ja escolheu e passar com um clique. Depois, quem muda e a
/// aba Admin do menu P -- e por isso um servidor que JA esta de pe neste processo (voltou ao menu sem fechar o jogo) nao
/// pergunta de novo.
///
/// OS DOIS BOTOES TEM ROTULO FIXO ("Modo padrão" / "Raças aleatórias"): os robos que hospedam pela tela de verdade
/// (`--diagversao`, `--diagabertura`, `--diagdecalque`) os acham pelo texto, como o dedo do jogador.
/// ==========================================================================================================
/// </summary>
public partial class Boot
{
	/// <summary>A tela de hospedar aberta agora, ou nula.</summary>
	private Control? _escolhaDoModo;

	/// <summary>A tela de hospedar esta na frente? (So bancada.)</summary>
	internal bool EscolhaDoModoNaTela => _escolhaDoModo != null && IsInstanceValid(_escolhaDoModo);

	/// <summary>O "Hospedar partida": a pergunta do modo primeiro -- ou direto, se o servidor deste processo ja esta de pe.</summary>
	private void PerguntarOModoEHospedar()
	{
		if (Jandirus.Server.GameServer.Instance is not { } srv) { _status.Text = "servidor indisponivel"; return; }
		if (srv.Running) { Hospedar(); return; }
		if (EscolhaDoModoNaTela) return;

		ModoDeRacas hoje = Jandirus.Server.GameServer.ModoDeRacasGravado();
		Node camada = _painel.GetParent();

		var veu = new ColorRect
		{
			Name = "EscolhaDoModo",
			Color = new Color(0, 0, 0, 0.72f),
			AnchorRight = 1, AnchorBottom = 1,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		Tema.Aplicar(centro);
		veu.AddChild(centro);

		PanelContainer moldura = Tema.Painel1(20);
		centro.AddChild(moldura);
		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
		caixa.AddThemeConstantOverride("separation", 10);
		moldura.AddChild(caixa);

		var titulo = new Label { Text = "COMO AS RAÇAS FUNCIONAM NESTE SERVIDOR?", HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 22);
		caixa.AddChild(titulo);
		caixa.AddChild(Fraco("Vale para os personagens criados daqui pra frente -- quem já tem personagem fica com ele. "
						   + "Dá pra mudar depois, na aba Admin do menu P."));
		var atual = new Label
		{
			Text = hoje == ModoDeRacas.Aleatorio ? "Hoje este servidor está com raças aleatórias." : "Hoje este servidor está no modo padrão.",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		atual.AddThemeColorOverride("font_color", Tema.Destaque);
		caixa.AddChild(atual);
		caixa.AddChild(new HSeparator());

		Opcao(caixa, "Modo padrão", "Cada jogador escolhe a própria raça na criação do personagem.", ModoDeRacas.Padrao, hoje);
		Opcao(caixa, "Raças aleatórias",
			  "A raça inicial é sorteada: Alien 22%, Terráqueo 20%, Namekuseijin 18%, Saiyajin 15%, Frost Demon 8%, "
			+ "Majin 4% e as outras treze raças 1% cada. A linhagem do Saiyajin e o clã do Namekuseijin também saem no "
			+ "sorteio; a classe (Elite, Lendário...) continua a de sempre.", ModoDeRacas.Aleatorio, hoje);

		var voltar = new Button { Text = "Voltar", TooltipText = "não hospeda agora" };
		voltar.Pressed += FecharEscolhaDoModo;
		caixa.AddChild(voltar);

		camada.AddChild(veu);
		_escolhaDoModo = veu;
	}

	private static Label Fraco(string texto)
	{
		var l = new Label { Text = texto, AutowrapMode = TextServer.AutowrapMode.Word, HorizontalAlignment = HorizontalAlignment.Center };
		l.AddThemeColorOverride("font_color", Tema.TextoFraco);
		l.AddThemeFontSizeOverride("font_size", 13);
		return l;
	}

	/// <summary>Uma opcao da tela: o botao (rotulo fixo) e a frase do que ele faz. A de hoje vem marcada.</summary>
	private void Opcao(VBoxContainer caixa, string rotulo, string frase, ModoDeRacas modo, ModoDeRacas hoje)
	{
		var b = new Button
		{
			Text = rotulo,
			ToggleMode = true,
			ButtonPressed = modo == hoje,
			CustomMinimumSize = new Vector2(0, 40),
		};
		b.AddThemeFontSizeOverride("font_size", 17);
		b.Pressed += () =>
		{
			if (Jandirus.Server.GameServer.Instance is { } srv) srv.EscolherModoDeRacas(modo, "a tela de hospedar");
			FecharEscolhaDoModo();
			Hospedar();
		};
		caixa.AddChild(b);
		caixa.AddChild(Fraco(frase));
	}

	private void FecharEscolhaDoModo()
	{
		if (EscolhaDoModoNaTela) _escolhaDoModo!.QueueFree();
		_escolhaDoModo = null;
	}
}
