using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// A MESA DE MONTAGEM DE TECNICAS DE KI -- o `CreateAttackWindow` do DM
/// (`customattacks.dm:605-1400`), que la e uma janela `.dmf` com trinta widgets nomeados a mao.
///
/// ============================ ELA NAO SABE UM PRECO SEQUER ============================
/// Nenhum botao daqui calcula, confere ou desconta ponto. Cada um manda `ca_comprar &lt;compra&gt;`
/// e espera o pacote de volta; o numero de pontos que aparece na tela e o `Gasto` que o SERVIDOR
/// devolveu, calculado pelo `Core.Skills.TecnicaCustomizada`.
///
/// Isso e a regra 4 da casa, e aqui ela vale em dobro: a tabela de precos tem dezoito linhas, e o
/// proprio DM -- que a tinha em dezoito copias, uma por botao -- escreveu TRES delas diferentes das
/// outras quinze. Uma segunda copia no cliente divergiria no primeiro ajuste, e o sintoma seria o
/// pior possivel: a tela mostrando um saldo e o servidor cobrando outro.
///
/// O botao APAGADO tambem nao e decisao daqui: ele so le o `Restantes` que veio. Quando a guarda de
/// verdade recusar, a recusa chega pelo chat com o motivo do DM.
/// ==================================================================================
///
/// ============================ UMA TELA, DOIS ESTADOS ============================
/// SEM MESA ABERTA: a lista das tecnicas de pe, com "editar" e "esquecer" em cada uma, e o botao de
/// criar (apagado quando o teto de dez ja bateu).
/// COM MESA ABERTA: o rascunho, os pontos restantes, e as compras.
///
/// A troca de estado e o servidor quem decide (o `bool` da mesa no pacote), e nao um clique local.
/// Uma tela que se abre sozinha em "modo edicao" antes de o servidor concordar e uma tela que mente
/// quando o pacote se perde.
/// ============================================================================
/// </summary>
public partial class TelaDeTecnicas : CanvasLayer
{
	public static TelaDeTecnicas? Instancia { get; private set; }

	private Control _raiz = null!;
	private VBoxContainer _corpo = null!;
	private Label _titulo = null!;

	/// <summary>O campo de texto aberto agora (nome/descricao/grito), ou nulo.</summary>
	private LineEdit? _campo;

	/// <summary>
	/// ESTOU ESCREVENDO NUM CAMPO DESTA TELA? Lido pelo <see cref="Foco"/>, que e a pergunta unica
	/// que todo leitor de teclado do jogo faz antes de agir.
	///
	/// ESTATICO como o `Chat.Digitando` e o `MenuJogo.Digitando`, e pelo mesmo motivo: quem
	/// pergunta (o movimento, o soco, o treino, os atalhos do HUD) nao tem esta tela na mao, e nao
	/// deve ter -- ele so quer saber se o teclado esta ocupado.
	/// </summary>
	public static bool Digitando { get; private set; }

	public override void _Ready()
	{
		Instancia = this;
		Layer = 4;   // a mesma da mochila e do menu de interacao: sao as telas "do mundo"
		Montar();

		if (GameClient.Instance is { } cli) cli.CustomizadasMudaram += AoMudar;
	}

	/// <summary>
	/// Solta a assinatura. O `GameClient` sobrevive ao logout e esta tela nao -- ver o registro
	/// `dbclimax-port-assinaturas-vazadas`: dezenove orfaos por ciclo, todos por lambda que nao da
	/// pra cancelar. Por isso o metodo tem NOME.
	/// </summary>
	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli) cli.CustomizadasMudaram -= AoMudar;
		if (Instancia == this) Instancia = null;
	}

	private void AoMudar() { if (_raiz.Visible) Redesenhar(); }

	private void Montar()
	{
		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_raiz.AddChild(centro);

		PanelContainer painel = Tema.Painel1(16);
		centro.AddChild(painel);

		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
		caixa.AddThemeConstantOverride("separation", 8);
		painel.AddChild(caixa);

		_titulo = new Label { Text = "TÉCNICAS DE KI", HorizontalAlignment = HorizontalAlignment.Center };
		_titulo.AddThemeFontSizeOverride("font_size", 22);
		caixa.AddChild(_titulo);
		caixa.AddChild(new HSeparator());

		// ROLAGEM: dez tecnicas com duas linhas cada, ou a mesa com quinze compras, passam da altura
		// de uma tela de 720. Sem isto o painel cresce pra fora e os botoes de baixo somem.
		var rolagem = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(560, 420),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		caixa.AddChild(rolagem);

		_corpo = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_corpo.AddThemeConstantOverride("separation", 6);
		rolagem.AddChild(_corpo);

		caixa.AddChild(new HSeparator());
		var fechar = new Button { Text = "Fechar (Esc)" };
		fechar.Pressed += Fechar;
		caixa.AddChild(fechar);
	}

	public override void _UnhandledInput(InputEvent evento)
	{
		if (Foco.Digitando) return;
		if (!_raiz.Visible) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k) return;
		if (k.Keycode != Key.Escape) return;

		Fechar();
		GetViewport().SetInputAsHandled();
	}

	public void Abrir()
	{
		_raiz.Visible = true;
		// PEDE A LISTA AO ABRIR em vez de confiar na que chegou no login: entre o login e agora o
		// jogador pode ter esquecido uma tecnica em outra tela, e uma lista velha oferece "editar"
		// uma coisa que nao existe mais.
		GameClient.Instance?.SendVerbo("ca_listar");
		Redesenhar();
	}

	private void Fechar()
	{
		FecharCampo();
		_raiz.Visible = false;
	}

	// =====================================================================
	// O DESENHO
	// =====================================================================
	private void Redesenhar()
	{
		FecharCampo();
		foreach (Node n in _corpo.GetChildren()) n.QueueFree();

		GameClient? cli = GameClient.Instance;
		if (cli == null) return;

		if (cli.Mesa is { } mesa) { DesenharMesa(mesa); return; }

		_titulo.Text = "TÉCNICAS DE KI";
		DesenharLista(cli);
	}

	private void DesenharLista(GameClient cli)
	{
		_corpo.AddChild(Tema.Legenda(
			$"{cli.Customizadas.Count} de {TecnicaCustomizada.Maximo} técnicas inventadas.",
			Tema.TextoFraco));

		foreach (TecnicaCustomizada t in cli.Customizadas)
		{
			PanelContainer p = Tema.Painel1(8);
			var linha = new VBoxContainer();
			p.AddChild(linha);

			var nome = new Label { Text = $"{t.Nome}  —  {NomeDoTipo(t.Tipo)}" };
			nome.AddThemeColorOverride("font_color", Tema.Destaque);
			linha.AddChild(nome);
			linha.AddChild(Tema.Legenda(Resumo(t), Tema.TextoFraco, 12));

			var botoes = new HBoxContainer();
			linha.AddChild(botoes);

			int id = t.Id;
			var editar = new Button { Text = "Ajustar" };
			editar.Pressed += () => GameClient.Instance?.SendVerbo("ca_editar", id.ToString());
			botoes.AddChild(editar);

			// ESQUECER PEDE UM SEGUNDO CLIQUE, e o alerta do DM diz por que: *"This decision is
			// irreversable!"*. O botao troca de texto em vez de abrir uma caixa -- a confirmacao
			// mora onde o dedo ja esta, e um "tem certeza?" que aparece embaixo do cursor e um
			// "tem certeza?" que se clica sem ler.
			var esquecer = new Button { Text = "Esquecer" };
			bool armado = false;
			esquecer.Pressed += () =>
			{
				if (!armado)
				{
					armado = true;
					esquecer.Text = "Esquecer — tem certeza?";
					esquecer.AddThemeColorOverride("font_color", Tema.Perigo);
					return;
				}
				GameClient.Instance?.SendVerbo("ca_esquecer", id.ToString());
			};
			botoes.AddChild(esquecer);

			_corpo.AddChild(p);
		}

		bool cabe = cli.Customizadas.Count < TecnicaCustomizada.Maximo;
		var criar = new Button { Text = "Inventar uma técnica nova", Disabled = !cabe };
		criar.Pressed += () => GameClient.Instance?.SendVerbo("ca_criar");
		_corpo.AddChild(criar);

		// O TETO DIZ POR QUE, e nao so apaga o botao. Um botao cinza sem explicacao e a mesma coisa
		// que o `switch` sem `else` do DM: o jogador nao descobre que ja tem dez.
		if (!cabe)
			_corpo.AddChild(Tema.Legenda(
				$"Cabem {TecnicaCustomizada.Maximo} técnicas na sua cabeça. Esqueça uma para abrir espaço.",
				Tema.Perigo, 12));
	}

	private void DesenharMesa(TecnicaCustomizada m)
	{
		_titulo.Text = m.Criada ? $"AJUSTANDO — {m.Nome}" : "INVENTANDO UMA TÉCNICA";

		// OS PONTOS PRIMEIRO, e grandes: e o unico numero que muda a cada clique, e e por ele que o
		// jogador decide o proximo.
		var pontos = new Label
		{
			Text = $"{m.Restantes} pontos livres",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		pontos.AddThemeFontSizeOverride("font_size", 18);
		pontos.AddThemeColorOverride("font_color", m.Restantes >= 0 ? Tema.Destaque : Tema.Perigo);
		_corpo.AddChild(pontos);
		_corpo.AddChild(Tema.Legenda(
			$"O orçamento é {TecnicaCustomizada.PontosTotais}, e ele é um TETO. Rebaixar potência, encarecer a "
			+ "energia, alongar a carga ou deixar o tiro mais lento DEVOLVE pontos — mas só devolve o que já foi "
			+ $"gasto: não dá para juntar mais de {TecnicaCustomizada.PontosTotais}.",
			Tema.TextoFraco, 11));

		// ============================ O PISO TEM QUE APARECER ANTES DO CLIQUE ============================
		// Com o orcamento intacto toda desvantagem e RECUSADA (nao ha o que estornar), e uma recusa que so
		// chega DEPOIS do clique, pelo chat, e uma regra que o jogador aprende por tentativa e erro. Esta
		// linha e a mesma regra dita antes.
		//
		// E ela e a UNICA coisa que esta tela deduz sobre pontos -- e nao e um preco, e o zero: como o
		// menor estorno da tabela e de 1 ponto, "gasto zero" ja implica "nenhuma desvantagem passa", sem
		// que a tela precise saber quanto custa coisa alguma. Apagar os botoes de desvantagem exigiria
		// saber QUAL lado de cada degrau estorna e por quanto -- que e a segunda copia da tabela de precos
		// que o cabecalho deste arquivo proibe (o DM tinha dezoito, e escreveu tres delas erradas).
		// ============================================================================================
		if (m.Gasto == 0)
			_corpo.AddChild(Tema.Legenda(
				"Você ainda não gastou nada. Desvantagens (menos potência, energia mais cara, carga mais longa, "
				+ "tiro mais lento, gastar fôlego) pagam com pontos de volta — e com o orçamento inteiro não há o "
				+ "que devolver, então elas são RECUSADAS em vez de sair de graça. Compre alguma vantagem primeiro.",
				Tema.Perigo, 11));

		// ---------------------------------------------------------------- tipo
		if (!m.Criada)
		{
			_corpo.AddChild(new HSeparator());
			_corpo.AddChild(Tema.Rotulo("Tipo (só se escolhe uma vez)"));
			var tipos = new HBoxContainer();
			foreach ((TipoDeProjetil t, string rotulo) in new[]
			{
				(TipoDeProjetil.Beam, "Raio"),
				(TipoDeProjetil.Blast, "Bola"),
				(TipoDeProjetil.Guided, "Teleguiado"),
			})
			{
				TipoDeProjetil alvo = t;
				var b = new Button { Text = rotulo, Disabled = m.Tipo == t };
				b.Pressed += () => GameClient.Instance?.SendVerbo("ca_tipo", alvo.ToString().ToLowerInvariant());
				tipos.AddChild(b);
			}
			_corpo.AddChild(tipos);
			_corpo.AddChild(Tema.Legenda(DescricaoDoTipo(m.Tipo), Tema.TextoFraco, 11));
		}
		else
		{
			_corpo.AddChild(Tema.Legenda($"Tipo: {NomeDoTipo(m.Tipo)} (não muda depois de pronta).",
										 Tema.TextoFraco, 12));
		}

		// ---------------------------------------------------------------- textos
		_corpo.AddChild(new HSeparator());
		Texto("Nome", m.Nome, "nome");
		Texto("Descrição", m.Desc, "desc");

		Interruptor($"Gritar ao disparar: \"{m.Grito}\"", m.DizGrito, "grito");
		if (m.DizGrito) Texto("Grito do disparo", m.Grito, "grito");
		if (m.Tipo == TipoDeProjetil.Beam)
		{
			Interruptor($"Gritar ao começar a carregar: \"{m.GritoDeCarga}\"", m.DizGritoDeCarga, "gritocarga");
			if (m.DizGritoDeCarga) Texto("Grito da carga", m.GritoDeCarga, "gritocarga");
		}

		// ---------------------------------------------------------------- as compras
		_corpo.AddChild(new HSeparator());
		Degrau("Potência", $"{m.BaseDano:0.0}", Compra.DanoMais, Compra.DanoMenos,
			   "Multiplica o dano inteiro. +0,1 por ponto.");
		Degrau("Custo de energia", $"{m.CustoKi:0}", Compra.KiMenos, Compra.KiMais,
			   "Baratear custa 1 ponto; encarecer devolve 1. Passo de 40, mínimo 20 — o padrão já é o mínimo.");
		Degrau("Velocidade", $"{m.Velocidade:0.#}", Compra.VelocidadeMais, Compra.VelocidadeMenos,
			   "De 1 pra cima anda de 1 em 1 até 5; de 1 pra baixo, de 0,2 em 0,2 até 0,2.");

		if (m.Tipo == TipoDeProjetil.Beam)
		{
			Degrau("Tempo de carga", $"{m.CargaMinima:0.#}s", Compra.CargaMenos, Compra.CargaMais,
				   "Encurtar custa 1 ponto; alongar devolve 1. Passo de 0,4s, mínimo 0,2s.");

			Interruptor($"Sai sozinho quando termina de carregar ({TecnicaCustomizada.PrecoDoInstantaneo} pontos)",
						m.Instantaneo, null,
						() => GameClient.Instance?.SendVerbo("ca_comprar",
							m.Instantaneo ? nameof(Compra.InstantaneoDesligar) : nameof(Compra.InstantaneoLigar)));

			Numero($"Alcance: {m.Alcance:0} tiles", (int)m.Alcance, TecnicaCustomizada.AlcancePiso, 60,
				   "1 ponto por tile, nos dois sentidos. Mínimo 5, padrão 20.",
				   v => GameClient.Instance?.SendVerbo("ca_comprar", $"{nameof(Compra.Alcance)}/{v}"));

			Numero($"Força pela distância: {m.DistanciaMod:0.0}× por tile",
				   (int)Math.Round(m.DistanciaMod * 10), (int)(TecnicaCustomizada.DistModPiso * 10), 20,
				   "Abaixo de 1,0 o raio morre andando; acima, engrossa. UM ponto a cada 0,1 — "
				   + "o texto do jogo antigo dizia 2, mas a conta dele sempre foi essa. (valor × 10)",
				   v => GameClient.Instance?.SendVerbo("ca_comprar",
						$"{nameof(Compra.DistanciaMod)}/{(v / 10.0).ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
		}

		// ---------------------------------------------------------------- folego
		_corpo.AddChild(new HSeparator());
		// O UNICO ESTORNO DE DOIS PONTOS: ele exige DOIS ja gastos, e nao um. Vale dizer o numero
		// porque a legenda geral do piso fala de "desvantagens" no plural e esta e a cara.
		Interruptor($"Gasta fôlego além da energia (devolve {TecnicaCustomizada.PrecoDaEstamina} pontos — "
					+ $"precisa ter {TecnicaCustomizada.PrecoDaEstamina} gastos)",
					m.UsaStamina, null,
					() => GameClient.Instance?.SendVerbo("ca_comprar",
						m.UsaStamina ? nameof(Compra.StaminaDesligar) : nameof(Compra.StaminaLigar)));
		if (m.UsaStamina)
			Degrau("Fôlego gasto", $"{m.CustoStamina:0}", Compra.StaminaMenos, Compra.StaminaMais,
				   "Cada ponto de fôlego a mais devolve 1 ponto de criação. Mínimo 1.");

		// ---------------------------------------------------------------- fechar
		_corpo.AddChild(new HSeparator());
		var acoes = new HBoxContainer();
		_corpo.AddChild(acoes);

		// SALVAR SO COM O ORCAMENTO FECHADO. Desde o piso do dono, `Gasto` vive preso em 0..5 no
		// `Core` -- entao `Restantes` negativo aqui e um estado que NAO EXISTE mais nem por dentro.
		// O botao apagado fica assim mesmo: ele custa uma linha, cobre um pacote adulterado que
		// tenha escapado do grampo do `CustomWire`, e apagado ele DIZ que ha algo errado em vez de
		// deixar salvar uma tecnica que o servidor recusaria.
		var salvar = new Button { Text = m.Criada ? "Confirmar mudanças" : "Criar a técnica",
								  Disabled = m.Restantes < 0 };
		salvar.Pressed += () => GameClient.Instance?.SendVerbo("ca_salvar");
		acoes.AddChild(salvar);

		var cancelar = new Button { Text = "Descartar" };
		cancelar.Pressed += () => GameClient.Instance?.SendVerbo("ca_cancelar");
		acoes.AddChild(cancelar);
	}

	// =====================================================================
	// OS WIDGETS
	// =====================================================================
	/// <summary>Um par de botoes "mais / menos" em volta de um valor. O `+`/`-` do painel do DM.</summary>
	private void Degrau(string rotulo, string valor, Compra sobe, Compra desce, string dica)
	{
		var linha = new HBoxContainer { TooltipText = dica };
		_corpo.AddChild(linha);

		var nome = new Label { Text = rotulo, CustomMinimumSize = new Vector2(190, 0) };
		linha.AddChild(nome);

		var menos = new Button { Text = "−", CustomMinimumSize = new Vector2(34, 0) };
		menos.Pressed += () => GameClient.Instance?.SendVerbo("ca_comprar", desce.ToString());
		linha.AddChild(menos);

		var v = new Label
		{
			Text = valor,
			CustomMinimumSize = new Vector2(70, 0),
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		v.AddThemeColorOverride("font_color", Tema.Destaque);
		linha.AddChild(v);

		var mais = new Button { Text = "+", CustomMinimumSize = new Vector2(34, 0) };
		mais.Pressed += () => GameClient.Instance?.SendVerbo("ca_comprar", sobe.ToString());
		linha.AddChild(mais);

		linha.AddChild(Tema.Legenda(dica, Tema.TextoFraco, 11));
	}

	/// <summary>Uma caixinha de liga/desliga. `acao` nula = alternar um grito (que e livre).</summary>
	private void Interruptor(string rotulo, bool ligado, string? grito, Action? acao = null)
	{
		var b = new CheckBox { Text = rotulo, ButtonPressed = ligado };
		b.Pressed += () =>
		{
			if (acao != null) { acao(); return; }
			GameClient.Instance?.SendVerbo("ca_grito", grito ?? "");
		};
		_corpo.AddChild(b);
	}

	/// <summary>
	/// UM CAMPO DE TEXTO que so manda ao confirmar (Enter ou perder o foco).
	///
	/// Nao manda a cada letra: cada verbo e uma mensagem confiavel, e "Kamehameha" custaria dez
	/// pacotes e dez redesenhos -- e o redesenho recria o campo, o que arrancaria o cursor da mao
	/// do jogador na segunda letra.
	/// </summary>
	private void Texto(string rotulo, string valor, string campo)
	{
		var linha = new HBoxContainer();
		_corpo.AddChild(linha);
		linha.AddChild(new Label { Text = rotulo, CustomMinimumSize = new Vector2(190, 0) });

		var e = new LineEdit
		{
			Text = valor,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MaxLength = campo == "desc" ? Jandirus.Net.CustomWire.MaxDesc : Jandirus.Net.CustomWire.MaxGrito,
		};
		// `Foco.Digitando` E O QUE IMPEDE AS TECLAS DO JOGO DE DISPARAREM enquanto se escreve. Sem
		// ele, escrever "Kamehameha" manda o personagem meditar (M) e carregar Ki (C) no meio da
		// palavra -- e a tecla Esc fecharia a tela em vez de sair do campo.
		e.FocusEntered += () => Digitando = true;
		e.FocusExited += () => { Digitando = false; Enviar(); };
		e.TextSubmitted += _ => Enviar();
		linha.AddChild(e);
		_campo = e;

		void Enviar()
		{
			if (e.Text == valor) return;   // nada mudou: nao gasta pacote nem redesenho
			GameClient.Instance?.SendVerbo("ca_texto", $"{campo}/{e.Text}");
		}
	}

	/// <summary>Um valor que se digita (alcance, modificador de distancia) -- o `input ... as num` do DM.</summary>
	private void Numero(string rotulo, int valor, int minimo, int maximo, string dica, Action<int> aplicar)
	{
		var linha = new HBoxContainer { TooltipText = dica };
		_corpo.AddChild(linha);
		linha.AddChild(new Label { Text = rotulo, CustomMinimumSize = new Vector2(230, 0) });

		var s = new SpinBox { MinValue = minimo, MaxValue = maximo, Value = valor, Step = 1 };
		// SO NO `value_changed` DO USUARIO: a `SpinBox` dispara o sinal ao ser construida com um
		// valor fora da faixa, e isso mandaria uma compra que ninguem pediu no meio do desenho.
		s.ValueChanged += v => { if ((int)v != valor) aplicar((int)v); };
		linha.AddChild(s);

		_corpo.AddChild(Tema.Legenda(dica, Tema.TextoFraco, 11));
	}

	private void FecharCampo()
	{
		if (_campo == null) return;
		// SOLTA O `Digitando` NA MARRA. O `FocusExited` nao dispara quando o no e destruido no
		// redesenho, e um `Digitando` preso deixa o jogo inteiro sem teclado ate o proximo campo.
		Digitando = false;
		_campo = null;
	}

	private static string NomeDoTipo(TipoDeProjetil t) => t switch
	{
		TipoDeProjetil.Beam => "raio canalizado",
		TipoDeProjetil.Blast => "bola solta",
		_ => "esfera teleguiada",
	};

	private static string DescricaoDoTipo(TipoDeProjetil t) => t switch
	{
		TipoDeProjetil.Beam =>
			"Carrega antes de sair e prende você no lugar enquanto dura — em troca, encosta e mói. "
			+ "É o único tipo com alcance e força-pela-distância ajustáveis.",
		TipoDeProjetil.Blast =>
			"Sai na hora, não prende você, e morre em quem acertar. É o tiro de todo dia.",
		_ =>
			"Persegue o alvo marcado até acertá-lo ou se apagar. Não adianta sair da frente.",
	};

	private static string Resumo(TecnicaCustomizada t)
	{
		string s = $"potência {t.BaseDano:0.#} · energia {t.CustoKi:0} · velocidade {t.Velocidade:0.#}"
				 + $" · alcance {t.Alcance:0}";
		if (t.Tipo == TipoDeProjetil.Beam)
		{
			s += $" · carga {t.CargaMinima:0.#}s";
			if (t.Instantaneo) s += " · sai sozinho";
			if (Math.Abs(t.DistanciaMod - 1) > 1e-6) s += $" · {t.DistanciaMod:0.0}×/tile";
		}
		if (t.UsaStamina) s += $" · {t.CustoStamina:0} de fôlego";
		return s;
	}
}
