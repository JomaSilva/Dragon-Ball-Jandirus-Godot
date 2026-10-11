using System.Text;
using Godot;
using Jandirus.Core.Social;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A ABA PEOPLE -- quem voce conhece, e o que cada pessoa e PRA VOCE.
///
/// ============================ O PEDIDO DO DONO (2026-10-10), E O QUE ELE VIROU NA TELA ============================
/// *"tela de conhecidos (people) a galera ta se confundindo MUITO com ela ... a galera n ta entendendo direito a
/// diferenca de vc selecionar oq e aquela pessoa pra vc e oq o jogo diz com base nas acoes, seria melhor so deixar
/// o player escolher oq aquela pessoa e pra ele e tirar isso de afinidade e so deixar convivio e declaracao"*.
///
/// A aba tinha, por pessoa, QUATRO rotulos lado a lado -- o grau de afinidade que o jogo deduzia ("Inimigo
/// mortal"), a declaracao do jogador ("neutro"), "RIVAL" e o grau de odio --, uma barra de afinidade, uma linha
/// de convivio e nove botoes. Agora cada cartao tem DUAS coisas, e so elas:
///
///   * o CONVIVIO: um numero com barra, que o jogo conta e que so sobe;
///   * UMA pergunta -- "Essa pessoa e pra voce:" -- com a regua das nove respostas. A escolhida fica acesa na
///     cor do tom dela; a que o convivio ainda nao paga fica apagada, com o convivio que pede escrito nela.
///
/// E embaixo da regua, UMA linha dizendo o que aquela escolha faz no jogo. O texto dela NAO e escrito aqui: e
/// montado perguntando ao `Core.Social.Convivio` o que a declaracao faz (`EnfurecePorMorte` e irmas), que sao as
/// mesmas perguntas que o servidor faz -- a tela nao tem como prometer uma regra que o jogo nao cumpre.
///
/// O que saiu daqui junto com a afinidade: o cartao do pedido de amizade, as pilulas de estado, a barra de
/// afinidade e a legenda de tres medidas. E saiu a CLASSE da linha de cada pessoa ("Saiyan / Elite"): ela nunca
/// aparece pra ninguem (`GameServer.Sigilo.cs`, regra 2), e o servidor deixou de manda-la.
///
/// E O CARTAO SE ABRE (segundo pedido do dono, no mesmo dia): clicar na pessoa mostra o que voce sabe dela --
/// idade, genero, cargo, ha quanto tempo se conhecem e a HISTORIA que ela escreveu --, e so pra quem ja conviveu
/// o bastante (`Convivio.ConvivioParaDetalhes`). Quem confere e o servidor; ver o bloco "OS DETALHES" abaixo.
/// =================================================================================================================
/// </summary>
public partial class MenuJogo
{
	/// <summary>O teto da barra de convivio: onde so o tempo junto para, e o preco da declaracao mais cara.</summary>
	private const int ConvivioCheio = Convivio.TetoDoConvivioPorTempo;

	// =====================================================================
	// A PAGINA
	// =====================================================================
	private void Gente()
	{
		if (GameClient.Instance is not { } cli) return;

		// QUEM GANHA CARTAO: quem ja tem convivio comigo, ou quem eu ja classifiquei. Quem eu so vi passar
		// (convivio zero, nada declarado) nao tem o que escolher -- com zero so o "neutro" esta aberto --,
		// entao vai num cartao so, por nome, em vez de encher a lista de reguas apagadas.
		var comCartao = new List<GameClient.ConhecidoInfo>();
		var deVista = new List<GameClient.ConhecidoInfo>();
		foreach (GameClient.ConhecidoInfo k in cli.Conhecidos) (TemCartao(k) ? comCartao : deVista).Add(k);

		// A FAIXA: o numero desta aba, e a frase que resume a aba inteira.
		Faixa("Conhecidos", cli.Conhecidos.Count.ToString(),
			  cli.Conhecidos.Count == 0
				  ? "ninguém ainda  ·  quem ficar alguns segundos perto de você entra nesta lista"
				  : "gente que você já encontrou  ·  é VOCÊ quem diz o que cada pessoa é pra você");

		ComoFunciona();

		Secao("Quem você conhece");
		if (comCartao.Count == 0)
			Aviso("Você ainda não conviveu com ninguém. Fique perto de outro jogador: a cada "
				  + $"{Convivio.SegundosPertoPorPonto:0} segundos vocês ganham um ponto de convívio, e ele aparece aqui.");
		foreach (GameClient.ConhecidoInfo c in comCartao) CartaoDePessoa(cli, c);

		if (deVista.Count > 0)
		{
			VBoxContainer vista = Cartao("Só de vista");
			Nota("Você cruzou com essas pessoas, mas ainda não conviveu com elas. Com o primeiro ponto de "
				 + "convívio cada uma ganha um cartão aqui em cima.", vista);
			vista.AddChild(Pilulas([.. deVista.Select(k => (k.Nome, Tema.TextoFraco))]));
		}

		// QUEM ESTA POR PERTO: os nomes em pilulas, num cartao so. E a lista de quem esta na tela agora, e
		// ela muda quando alguem entra ou sai do campo de visao -- por isso os nomes entram na assinatura
		// da aba (ver `ExtraDaAssinaturaDeGente`).
		VBoxContainer perto = Cartao("Quem está por perto");
		List<string> nomes = World.Instancia?.NomesVisiveis() ?? [];
		if (nomes.Count == 0) Nota("Ninguém no seu campo de visão.", perto);
		else perto.AddChild(Pilulas([.. nomes.Select(n => (n, Tema.Texto))]));
	}

	/// <summary>Esta pessoa ganha cartao proprio? Ver o comentario no <see cref="Gente"/>.</summary>
	private static bool TemCartao(GameClient.ConhecidoInfo k) =>
		k.Familiaridade > 0 || (Relacao)k.Relacao is not (Relacao.Nenhuma or Relacao.Neutro);

	/// <summary>
	/// A ABA INTEIRA EM DUAS LINHAS: as duas coisas que ela tem, quem escreve cada uma, e pra que serve
	/// escolher. CURTA de proposito -- a primeira versao tinha dois paragrafos e empurrava o primeiro cartao
	/// pra fora da tela; o detalhe de cada escolha mora na linha de efeito do proprio cartao. Os numeros saem
	/// das constantes que o servidor usa (`Convivio`), pra explicacao nunca mentir sobre a regra.
	/// </summary>
	private void ComoFunciona()
	{
		VBoxContainer corpo = Cartao("Como funciona");
		LinhaDeLegenda(TituloDoConvivio, Tema.Ki,
					   "o jogo conta. Sobe sozinho com o tempo por perto (1 ponto a cada "
					   + $"{Convivio.SegundosPertoPorPonto:0} s), com conversa e com luta — e nunca desce.", corpo);
		LinhaDeLegenda(TituloDaEscolha, Tema.Destaque,
					   "você escolhe, e o jogo não mexe na sua escolha. Mais convívio libera mais opções. Ver um "
					   + "amigo cair ou morrer nas mãos de um inimigo é o que desperta o Super Saiyajin — quanto maior a dor, "
					   + "menos poder ele pede.", corpo);
	}

	/// <summary>Os dois titulos da legenda -- as DUAS coisas desta aba. As bancadas os procuram por aqui.</summary>
	internal const string TituloDoConvivio = "CONVÍVIO", TituloDaEscolha = "O QUE ELA É PRA VOCÊ";

	/// <summary>
	/// Uma linha da legenda: o titulo na cor dele, numa coluna de largura fixa (os dois textos comecam no
	/// mesmo x), e a frase ao lado, que quebra linha.
	/// </summary>
	private static void LinhaDeLegenda(string titulo, Color cor, string texto, Control pai)
	{
		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 8);
		var t = new Label
		{
			Text = titulo, CustomMinimumSize = new Vector2(150, 0),
			SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
		};
		t.AddThemeFontSizeOverride("font_size", 12);
		t.AddThemeColorOverride("font_color", cor);
		linha.AddChild(t);
		Label frase = Nota(texto, linha);
		frase.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		pai.AddChild(linha);
	}

	// =====================================================================
	// O CARTAO DE UMA PESSOA
	// =====================================================================
	/// <summary>
	/// O retrato e a ficha em cima (nome, raca "como voce viu da ultima vez", a barra do convivio); a
	/// pergunta e a regua embaixo; e a linha do que a escolha faz. Clicar no retrato ou em "Ver detalhes" abre
	/// o cartao (<see cref="DetalhesNoCartao"/>).
	///
	/// "NUNCA DISSE NADA" E "NEUTRO" SAO A MESMA COISA PRA QUEM LE: as duas nao mudam regra nenhuma
	/// (`Relacao.Nenhuma` so existe pra o save distinguir quem nunca abriu esta aba). A regua acende o Neutro
	/// nos dois casos -- duas palavras pra "nada" era metade da confusao desta tela.
	/// </summary>
	private void CartaoDePessoa(GameClient cli, GameClient.ConhecidoInfo c)
	{
		Relacao atual = (Relacao)c.Relacao == Relacao.Nenhuma ? Relacao.Neutro : (Relacao)c.Relacao;
		bool aberto = c.Assinatura == _pessoaAberta;
		bool paga = PagaOsDetalhes(c);
		DetalhesDeConhecido? detalhes = aberto && paga ? cli.DetalhesDe(c.Assinatura) : null;

		// O CARTAO ESTA ABERTO, O CONVIVIO PAGA E O CLIENTE NAO TEM OS DETALHES: ou o convivio acabou de pagar com
		// o cartao ja aberto (ele estava trancado, e a pessoa esta ao lado), ou o que estava guardado foi
		// esquecido (troca de personagem). O pedido sai agora -- UMA vez por estado do que o cliente guarda, e
		// nao uma por redesenho.
		if (aberto && paga && detalhes == null) PedirDetalhesUmaVez(cli, c.Assinatura);

		VBoxContainer corpo = Cartao("");
		MarcarCartaoDeItem(corpo, "pessoa", c.Nome);
		corpo.AddThemeConstantOverride("separation", 5);

		var topo = new HBoxContainer();
		topo.AddThemeConstantOverride("separation", 10);
		// A "FOTO" DA ULTIMA VEZ QUE EU A VI -- o dono: *"pra voce nao ter que lembrar so pelo nome"*. Com o
		// cartao aberto ela cresce; e clicar nela abre e fecha os detalhes, como o botao ao lado do nome.
		Control retrato = Retrato(c.Nome, aberto ? EscalaDoRetratoAberto : EscalaDoRetrato);
		retrato.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		retrato.GuiInput += e =>
		{
			if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) AlternarDetalhes(cli, c);
		};
		topo.AddChild(retrato);

		var ficha = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		ficha.AddThemeConstantOverride("separation", 3);
		// O NOME NA COR DO TOM do que eu declarei: verde quem eu gosto, laranja o rival, vermelho o desafeto.
		// E a mesma cor da opcao acesa na regua -- a lista se le de relance sem um segundo rotulo.
		var linhaDoNome = new HBoxContainer();
		linhaDoNome.AddThemeConstantOverride("separation", 8);
		Label nome = Titulo(c.Nome, CorDoTom(atual));
		nome.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		linhaDoNome.AddChild(nome);
		linhaDoNome.AddChild(BotaoDeDetalhes(cli, c, aberto));
		ficha.AddChild(linhaDoNome);
		// SO A RACA: a classe de alguem nunca aparece (`GameServer.Sigilo.cs`, regra 2), e o servidor nem a manda.
		Nota($"{Jandirus.Core.Races.NomeDaRaca.Bonito(c.Raca)}  ·  como você viu da última vez", ficha);
		LinhaComBarra(RotuloDoConvivio, c.Familiaridade.ToString(),
					  Math.Min((double)c.Familiaridade / ConvivioCheio, 1), Tema.Ki, ficha, Tema.Ki);
		if (aberto) DetalhesNoCartao(c, paga, detalhes, ficha);
		topo.AddChild(ficha);
		corpo.AddChild(topo);

		if (aberto && detalhes is { } d) HistoriaNoCartao(d, corpo);

		corpo.AddChild(Tema.Rotulo(PerguntaDaRegua));
		corpo.AddChild(Regua(cli, c, atual));

		Label efeito = Nota(EfeitoDe(atual), corpo);
		efeito.SetMeta("efeito", (int)atual);
	}

	// =====================================================================
	// OS DETALHES -- o cartao ABERTO
	// =====================================================================
	/// <summary>
	/// ============================ CLICAR NUMA PESSOA ABRE O QUE VOCE SABE DELA ============================
	/// Pedido do dono (2026-10-10): *"ao abrir na aba people e clicar em pessoas q vc conhece (tem q ter um certo
	/// nivel de convivio minimo) vc pode ver mais detalhes da pessoa, como idade, a backstory dela etc"*.
	///
	/// O CARTAO SE ABRE NO LUGAR (um por vez): o retrato cresce, e embaixo da barra de convivio entram a idade,
	/// o genero, o cargo, desde quando voces se conhecem e quando voce a viu por ultimo; a HISTORIA que ela
	/// escreveu vem logo abaixo, na largura do cartao. Nada de janela por cima: a regua e a linha de efeito
	/// continuam ali, que e onde se decide o que a pessoa e depois de ler quem ela e.
	///
	/// QUEM TEM OS DETALHES E O SERVIDOR, e ele so os manda a quem conviveu o bastante
	/// (`Convivio.ConvivioParaDetalhes`). Abaixo disso o cartao abre TRANCADO -- a frase diz o que falta -- e
	/// o pedido nem sai daqui: a tela ja sabe a resposta, e o servidor a daria de novo (`VerboDetalhes`).
	/// =====================================================================================================
	/// </summary>
	private string _pessoaAberta = "";

	/// <summary>
	/// O ULTIMO PEDIDO DE DETALHES: de quem, e com que versao do que o cliente guardava
	/// (`GameClient.VersaoDosDetalhes`). Enquanto os dois nao mudarem a resposta esta a caminho, e a pagina
	/// redesenhada nao pede de novo; a versao muda quando a resposta chega (e ai ha o que mostrar) ou quando o
	/// que estava guardado some (e ai o pedido tem que sair outra vez).
	/// </summary>
	private (string De, int Versao) _detalhesPedidos = ("", -1);

	/// <summary>Pede os detalhes desta pessoa ao servidor, se o pedido ja nao estiver a caminho.</summary>
	private void PedirDetalhesUmaVez(GameClient cli, string assinatura)
	{
		(string, int) pedido = (assinatura, cli.VersaoDosDetalhes);
		if (_detalhesPedidos == pedido) return;
		_detalhesPedidos = pedido;
		cli.PedirDetalhes(assinatura);
	}

	/// <summary>O convivio com esta pessoa ja paga os detalhes dela? A mesma conta do servidor, com a mesma constante.</summary>
	private static bool PagaOsDetalhes(GameClient.ConhecidoInfo c) => c.Familiaridade >= Convivio.ConvivioParaDetalhes;

	/// <summary>A escala do retrato com o cartao fechado e aberto (o sprite e de 32 px; so escala inteira nao borra).</summary>
	private const int EscalaDoRetrato = 2, EscalaDoRetratoAberto = 3;

	/// <summary>Abre os detalhes desta pessoa (fechando os de quem estava aberto), ou fecha se ja era ela.</summary>
	private void AlternarDetalhes(GameClient cli, GameClient.ConhecidoInfo c)
	{
		_pessoaAberta = _pessoaAberta == c.Assinatura ? "" : c.Assinatura;
		_detalhesPedidos = ("", -1);

		// A CADA ABERTURA, um pedido novo: a ficha que o servidor guarda pode ter sido refeita desde a ultima
		// vez (a pessoa envelheceu, ganhou um cargo, voces se viram de novo). So pra quem o convivio paga --
		// abaixo dele o pedido NEM SAI: a tela ja sabe a resposta.
		if (_pessoaAberta.Length > 0 && PagaOsDetalhes(c)) PedirDetalhesUmaVez(cli, c.Assinatura);
		Redesenhar();
	}

	/// <summary>
	/// O botao ao lado do nome. Pra quem o convivio ainda nao paga ele traz o preco, como as opcoes fechadas da
	/// regua ("Detalhes (20)") -- e CONTINUA clicavel: abre o cartao trancado, que diz o que falta.
	/// </summary>
	private Button BotaoDeDetalhes(GameClient cli, GameClient.ConhecidoInfo c, bool aberto)
	{
		bool paga = PagaOsDetalhes(c);
		var b = new Button
		{
			Text = aberto ? "Fechar detalhes" : paga ? "Ver detalhes" : $"Detalhes ({Convivio.ConvivioParaDetalhes})",
			FocusMode = Control.FocusModeEnum.None,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			TooltipText = paga
				? "A idade, o cargo e a história dessa pessoa, como você a viu da última vez."
				: $"Trancado: pede {Convivio.ConvivioParaDetalhes} de convívio (vocês têm {c.Familiaridade}).",
		};
		b.SetMeta("detalhes", c.Assinatura);
		b.AddThemeFontSizeOverride("font_size", TamanhoDaLetraDaRegua);
		b.AddThemeStyleboxOverride("normal", Capsula(Tema.PainelAceso, aberto ? Tema.BordaViva : Tema.Borda));
		b.AddThemeStyleboxOverride("hover", Capsula(Tema.PainelAceso.Lightened(0.08f), Tema.BordaViva));
		b.AddThemeStyleboxOverride("pressed", Capsula(Tema.PainelApagado, Tema.BordaViva));
		b.Pressed += () => AlternarDetalhes(cli, c);
		return b;
	}

	/// <summary>
	/// As linhas de detalhe, na coluna da ficha: TRANCADO (com o que falta), BUSCANDO (o pedido saiu e a resposta
	/// ainda nao chegou) ou os fatos. O metadado `detalhes` diz em qual dos tres estados o cartao esta.
	/// </summary>
	private static void DetalhesNoCartao(GameClient.ConhecidoInfo c, bool paga, DetalhesDeConhecido? detalhes, Control pai)
	{
		if (!paga)
		{
			Label trancado = Nota(
				$"DETALHES TRANCADOS  ·  vocês ainda não conviveram o bastante. Com {Convivio.ConvivioParaDetalhes} de convívio "
				+ $"(vocês têm {c.Familiaridade}) você passa a ver a idade, o cargo e a história dessa pessoa. Só ficando por "
				+ $"perto, {TempoQueFalta(Convivio.ConvivioParaDetalhes - c.Familiaridade)}.", pai);
			trancado.AddThemeColorOverride("font_color", Tema.Destaque);
			trancado.SetMeta("detalhes", "trancados");
			return;
		}
		if (detalhes is not { } d)
		{
			Nota("buscando o que você sabe dessa pessoa...", pai).SetMeta("detalhes", "buscando");
			return;
		}

		var fatos = new VBoxContainer();
		fatos.SetMeta("detalhes", "abertos");
		fatos.AddThemeConstantOverride("separation", 1);
		Linha("Idade", $"{d.Idade} anos  ·  {Jandirus.Core.Races.Envelhecimento.Fase(c.Raca, d.Idade)}", null, fatos);
		// RACA SEM GENERO (Namekuseijin, Saibaman...) NAO GANHA A LINHA: a ficha delas carrega um valor de fabrica.
		if (Array.IndexOf(Jandirus.Core.Races.CharacterDraft.SemGenero, c.Raca) < 0)
			Linha("Gênero", d.Genero switch { "Male" => "masculino", "Female" => "feminino", _ => d.Genero }, null, fatos);
		Linha("Cargo", d.Titulo.Length > 0 ? d.Titulo : "nenhum que você saiba", d.Titulo.Length > 0 ? Tema.Destaque : null, fatos);
		// "NAO SEI" NAO SE ESCREVE: a ficha de antes de 2026-10-10 nao guardou quando voces se conheceram.
		if (d.ConheceHaSegundos > 0) Linha("Vocês se conhecem", HaQuantoTempo(d.ConheceHaSegundos), null, fatos);
		Linha("Visto por último", HaQuantoTempo(d.VistoHaSegundos), null, fatos);
		pai.AddChild(fatos);
	}

	/// <summary>
	/// A HISTORIA que a pessoa escreveu na criacao, numa caixa na largura do cartao -- ou a frase dizendo por
	/// que ela nao esta ali: voce ainda nao esteve com a pessoa desde que o convivio abriu os detalhes (a ficha
	/// e uma fotografia: a historia entra nela no proximo encontro), ou ela nao escreveu nenhuma.
	/// </summary>
	private static void HistoriaNoCartao(DetalhesDeConhecido d, Control pai)
	{
		pai.AddChild(Tema.Rotulo("História"));
		bool tem = d.OuviuAHistoria && d.Historia.Length > 0;
		var caixa = new PanelContainer();
		caixa.AddThemeStyleboxOverride("panel", Tema.Caixa(new Color("0e1017"), Tema.Borda, 8));
		Label texto = Nota(tem ? d.Historia
			: d.OuviuAHistoria ? "Essa pessoa não escreveu uma história."
			: "Você ainda não ouviu a história dessa pessoa: ela entra aqui no próximo encontro de vocês.", caixa);
		if (tem) texto.AddThemeColorOverride("font_color", Tema.Texto);
		texto.SetMeta("historia", tem);
		pai.AddChild(caixa);
	}

	/// <summary>
	/// Quanto tempo SO FICANDO POR PERTO rende estes pontos de convivio, em palavras ("falta cerca de 1 minuto",
	/// "faltam cerca de 15 minutos"). A conta sai da taxa que o servidor usa (`Convivio.SegundosPertoPorPonto`).
	/// </summary>
	internal static string TempoQueFalta(int pontos)
	{
		int minutos = (int)Math.Ceiling(Math.Max(pontos, 1) * Convivio.SegundosPertoPorPonto / 60.0);
		return minutos == 1 ? "falta cerca de 1 minuto" : $"faltam cerca de {minutos} minutos";
	}

	/// <summary>"ha 3 dias", "ha 2 h", "ha 5 min", "agora ha pouco" -- o tempo da memoria, sem falsa precisao.</summary>
	internal static string HaQuantoTempo(uint segundos) => segundos switch
	{
		< 90 => "agora há pouco",
		< 90 * 60 => $"há {Math.Max(2, (int)Math.Round(segundos / 60.0))} min",
		< 36 * 3600 => $"há {Math.Max(2, (int)Math.Round(segundos / 3600.0))} h",
		_ => $"há {Math.Max(2, (int)Math.Round(segundos / 86400.0))} dias",
	};

	/// <summary>O rotulo da barra: o nome da medida e, na mesma linha, o que ela mede.</summary>
	internal const string RotuloDoConvivio = "Convívio  ·  quanto vocês já conviveram";

	/// <summary>A pergunta que a regua responde. (O `Tema.Rotulo` a escreve em caixa alta.)</summary>
	internal const string PerguntaDaRegua = "Essa pessoa é pra você:";

	/// <summary>O nome de uma declaracao como a regua o escreve: o do `Convivio`, com a inicial maiuscula.</summary>
	internal static string NomeNaRegua(Relacao r)
	{
		string n = Convivio.NomeDaRelacao(r);
		return n.Length == 0 ? n : char.ToUpperInvariant(n[0]) + n[1..];
	}

	/// <summary>
	/// O TOM de uma declaracao, em cor: verde pro afeto, laranja pros dois rivais, vermelho pro desafeto, e
	/// a cor do texto pro neutro. Quatro tons pra nove opcoes -- a posicao na regua diz o grau.
	/// </summary>
	internal static Color CorDoTom(Relacao r) => r switch
	{
		Relacao.Amor or Relacao.MuitoBom or Relacao.Bom => Tema.Bom,
		Relacao.RivalBom or Relacao.RivalRuim => Tema.Destaque,
		Relacao.Ruim or Relacao.MuitoRuim or Relacao.Odio => Tema.Perigo,
		_ => Tema.Texto,
	};

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagconhecidos`): a linha de efeito volta a prometer furia pela QUEDA -- "cair,
	/// morrer ou ser absorvida" numa lista so, o texto de antes dos dois degraus. Lido pelo <see cref="EfeitoDe"/>.
	/// </summary>
	internal static bool EfeitoEmUmDegrauSoDeTeste;

	/// <summary>
	/// O QUE ESTA ESCOLHA FAZ NO JOGO, numa linha -- montada com as respostas do `Core.Social.Convivio`, que
	/// sao as que o servidor usa pra decidir a raiva. Uma declaracao que ganhe (ou perca) efeito la muda o
	/// texto aqui sem ninguem lembrar de editar a tela.
	///
	/// SAO DOIS DEGRAUS, E A LINHA DIZ QUAL (regra do dono, 2026-10-11; `Core.Combat.RaivaEmDegraus`): ver a pessoa
	/// CAIR da RAIVA -- a comum: sobe o poder e so abre as formas de raiva por um preco MAIOR de BP
	/// (`FormaDef.AgioNaRaivaComum`) --, e ve-la MORRER ou ser ABSORVIDA poe em FURIA -- a extrema: abre as mesmas
	/// formas pelo preco normal e leva o Ki ao teto. A linha antiga prometia furia pelas tres coisas, e passou a
	/// mentir no dia em que o nocaute desceu de degrau. As duas palavras saem em caixa alta na tela, e sao as que
	/// as bancadas procuram: a da RAIVA = a queda desta pessoa mexe comigo; a da FURIA = a morte dela mexe.
	/// (A fusao Namekuseijin nao e "ser absorvida" pra esta regra: so a absorcao do bio e a do Majin enfurecem.)
	/// </summary>
	internal static string EfeitoDe(Relacao r)
	{
		bool queda = Convivio.EnfurecePorQueda(r);
		var extrema = new List<string>();
		if (Convivio.EnfurecePorMorte(r)) extrema.Add("morrer");
		if (Convivio.EnfurecePorAbsorcao(r)) extrema.Add("ser absorvida");
		if (EfeitoEmUmDegrauSoDeTeste && queda) { extrema.Insert(0, "cair"); queda = false; }
		bool enfurece = queda || extrema.Count > 0, treino = Convivio.LutaEhTreino(r);

		string nome = NomeNaRegua(r);
		if (!enfurece && !treino) return $"{nome}: não muda nenhuma regra do jogo. É só a sua anotação.";

		var s = new StringBuilder(nome).Append(": ");
		string lista = extrema.Count <= 1 ? string.Concat(extrema) : string.Join(", ", extrema.Take(extrema.Count - 1)) + " ou " + extrema[^1];
		if (queda && extrema.Count > 0)
			s.Append($"você fica com RAIVA se vir essa pessoa cair nas mãos de um inimigo, e entra em FÚRIA se a vir {lista}.");
		else if (queda)
			s.Append("você fica com RAIVA se vir essa pessoa cair nas mãos de um inimigo.");
		else if (extrema.Count > 0)
			s.Append($"você entra em FÚRIA se vir essa pessoa {lista} nas mãos de um inimigo.");
		if (treino)
			s.Append(enfurece ? " E lutar" : "lutar").Append(" com ela é treino: se ela te derrubar, ninguém se enfurece.");
		return s.ToString();
	}

	// =====================================================================
	// A REGUA -- o seletor
	// =====================================================================
	/// <summary>
	/// AS NOVE RESPOSTAS, do afeto ao desafeto com o neutro no meio (`Convivio.Declaraveis`), numa fila que
	/// quebra linha (`HFlowContainer`). Tres estados, e cada opcao esta em exatamente um:
	///
	///   * ESCOLHIDA -- acesa na cor do tom, e nao clicavel (nao ha o que mudar);
	///   * ABERTA -- o convivio paga: um clique manda o verb `relacao`;
	///   * FECHADA -- apagada, com o convivio que ela pede escrito ao lado. Ela APARECE em vez de sumir, que
	///     e a regra do menu deste port: saber que "Amor" existe e que pede 200 e informacao.
	///
	/// **QUEM DECIDE E O SERVIDOR.** Apagar a opcao e conveniencia -- o `VerboRelacao` confere o convivio de
	/// novo, porque apagar botao nunca foi permissao.
	///
	/// O metadado `declaracao` leva o numero da `Relacao` e o `escolhida` diz qual esta acesa: e por eles
	/// (e nao pelo texto, que muda com o convivio, nem pela cor) que a bancada le a regua.
	/// </summary>
	private static HFlowContainer Regua(GameClient cli, GameClient.ConhecidoInfo c, Relacao atual)
	{
		var fila = new HFlowContainer();
		fila.SetMeta("regua", c.Assinatura);
		fila.AddThemeConstantOverride("h_separation", 3);
		fila.AddThemeConstantOverride("v_separation", 3);

		// AS QUATRO CAPSULAS DESTA REGUA, feitas uma vez e divididas pelas nove opcoes (um `StyleBox` e um
		// recurso: pode vestir varios botoes).
		StyleBoxFlat normal = Capsula(Tema.PainelAceso, Tema.Borda),
					 acesa = Capsula(Tema.PainelAceso.Lightened(0.08f), Tema.BordaViva),
					 apertada = Capsula(Tema.PainelApagado, Tema.BordaViva),
					 apagada = Capsula(Tema.PainelApagado, Tema.BordaApagada);

		foreach (Relacao r in Convivio.Declaraveis)
		{
			int pede = Convivio.FamiliaridadeExigida(r);
			bool escolhida = r == atual;
			bool aberta = c.Familiaridade >= pede;

			var b = new Button
			{
				Text = aberta || escolhida ? NomeNaRegua(r) : $"{NomeNaRegua(r)} ({pede})",
				Disabled = escolhida || !aberta,
				FocusMode = Control.FocusModeEnum.None,
				TooltipText = escolhida ? "É o que você disse. " + EfeitoDe(r)
					: aberta ? EfeitoDe(r)
					: $"Pede {pede} de convívio (vocês têm {c.Familiaridade}). Só ficando por perto, "
					  + $"{TempoQueFalta(pede - c.Familiaridade)}.",
			};
			b.SetMeta("declaracao", (int)r);
			b.SetMeta("escolhida", escolhida);
			b.AddThemeFontSizeOverride("font_size", TamanhoDaLetraDaRegua);
			// A CARA DE UMA OPCAO: uma capsula baixa e estreita (o botao do tema tem 10 px de margem por lado).
			// A letra e a margem sao as que fazem as NOVE caberem numa linha so do cartao quando todas estao
			// abertas -- com a ultima caindo pra linha de baixo, a regua deixava de se ler como regua. A
			// ESCOLHIDA leva chapa, borda e letra na cor do tom; como ela nasce desabilitada, o estilo aceso
			// entra no estado `disabled`.
			if (escolhida)
			{
				Color tom = CorDoTom(r);
				b.AddThemeStyleboxOverride("disabled", Capsula(new Color(tom, 0.22f), tom));
				b.AddThemeColorOverride("font_disabled_color", tom);
			}
			else
			{
				b.AddThemeStyleboxOverride("normal", normal);
				b.AddThemeStyleboxOverride("hover", acesa);
				b.AddThemeStyleboxOverride("pressed", apertada);
				b.AddThemeStyleboxOverride("disabled", apagada);
			}

			if (!b.Disabled)
			{
				string sig = c.Assinatura, nome = Convivio.NomeDaRelacao(r);
				b.Pressed += () => cli.SendVerbo("relacao", $"{sig}|{nome}");
			}
			fila.AddChild(b);
		}
		return fila;
	}

	/// <summary>A letra e a margem lateral das opcoes da regua. Ver o comentario no <see cref="Regua"/>.</summary>
	private const int TamanhoDaLetraDaRegua = 11, MargemDaCapsula = 7;

	/// <summary>A capsula de uma opcao da regua: chapa, borda de 1 px, cantos redondos e pouca margem.</summary>
	private static StyleBoxFlat Capsula(Color fundo, Color borda)
	{
		var s = new StyleBoxFlat
		{
			BgColor = fundo, BorderColor = borda,
			ContentMarginLeft = MargemDaCapsula, ContentMarginRight = MargemDaCapsula,
			ContentMarginTop = 3, ContentMarginBottom = 3,
		};
		s.SetBorderWidthAll(1);
		s.SetCornerRadiusAll(11);
		return s;
	}

	// =====================================================================
	// O RETRATO
	// =====================================================================
	/// <summary>
	/// O RETRATO: a aparencia de `nome` como eu a vi por ultimo (`VistosDeGente`), vestida no MESMO
	/// `CharacterVisual` do mundo -- ou a moldura vazia dizendo "sem foto". Sempre a mesma moldura, do mesmo
	/// tamanho: o cartao nao muda de forma conforme a memoria, e a bancada acha as duas metades.
	///
	/// O TAMANHO: o sprite e um tile de 32 px, em escala INTEIRA (<see cref="EscalaDoRetrato"/> na lista,
	/// <see cref="EscalaDoRetratoAberto"/> no cartao aberto) pra nao borrar o pixel art; a moldura e o sprite
	/// mais uma folga, e o boneco fica no CENTRO dela -- a origem do `CharacterVisual` e o centro do corpo (a
	/// mesma conta da selecao de personagem: `lado / 2 + 8`).
	/// </summary>
	private static Control Retrato(string nome, int escala)
	{
		int lado = 32 * escala + 16;
		// A MOLDURA RECORTA (`ClipContents`): o retrato e um Node2D dentro de um Control, nao respeita o
		// layout e escorreria por cima do nome ao lado -- a mesma armadilha da selecao de personagem.
		var moldura = new Panel
		{
			Name = "Moldura",
			CustomMinimumSize = new Vector2(lado, lado),
			SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
			ClipContents = true,
		};
		moldura.AddThemeStyleboxOverride("panel", Tema.Caixa(new Color("0e1017"), Tema.Borda, 6));

		VistosDeGente.Visto? visto = nome.Length > 0 ? VistosDeGente.De(nome) : null;
		if (visto is { } v && VistosDeGente.Catalogo is { } cat)
		{
			var boneco = new CharacterVisual
			{
				Name = "Boneco",
				Position = new Vector2(lado / 2f, lado / 2f + 2f * escala),
				Scale = new Vector2(escala, escala),
				// O RETRATO PISCA NO RELOGIO DO MUNDO: a aba e remontada a cada redesenho, e um boneco
				// novo nascia no quadro 0 -- que e a piscada. Ver `CharacterVisual.RelogioDoMundo`. A fase
				// e por pessoa, pra galeria nao piscar em coro.
				RelogioDoMundo = true,
				DeslocamentoDoRelogio = FaseDe(nome),
			};
			moldura.AddChild(boneco);
			boneco.Vestir(cat, v.Aparencia, v.Raca, v.Genero);
			moldura.TooltipText = "como você viu essa pessoa da última vez";
		}
		else
		{
			var semFoto = new Label
			{
				Text = "sem foto", Name = "SemFoto",
				AnchorRight = 1, AnchorBottom = 1,
				HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
			};
			semFoto.AddThemeColorOverride("font_color", Tema.TextoFraco);
			semFoto.AddThemeFontSizeOverride("font_size", 11);
			moldura.AddChild(semFoto);
			moldura.TooltipText = "você ainda não viu essa pessoa com os seus olhos";
		}
		return moldura;
	}

	/// <summary>A fase de cada pessoa no relogio do mundo (0..10 s), determinista pelo nome -- o `GetHashCode` de string muda por processo.</summary>
	private static double FaseDe(string nome)
	{
		int h = 17;
		foreach (char c in nome) h = unchecked(h * 31 + c);
		return (h & 0x7fffffff) % 1000 / 100.0;
	}

	// =====================================================================
	// A ASSINATURA
	// =====================================================================
	/// <summary>
	/// A ASSINATURA DE CACHE DESTA ABA (ver `MenuJogo.Assinatura`): TUDO o que a pagina desenha, e nada que
	/// mude sem a tela mudar. Por pessoa: a ficha, o convivio, a declaracao e se ha retrato guardado; e os
	/// nomes visiveis. O convivio entra INTEIRO -- ele e o numero escrito no cartao, e muda no maximo uma
	/// vez a cada 30 s por pessoa ao lado.
	///
	/// Nunca vazia (o `gente` na frente): vazia quer dizer "sem cache", e a pagina seria refeita a cada
	/// pacote de ficha, com as reguas morrendo debaixo do mouse.
	/// </summary>
	private string ExtraDaAssinaturaDeGente(SheetState f)
	{
		var sb = new StringBuilder("gente");
		if (GameClient.Instance is not { } c) return sb.ToString();
		foreach (GameClient.ConhecidoInfo k in c.Conhecidos)
			sb.Append('|').Append(k.Assinatura).Append(',').Append(k.Nome).Append(',').Append(k.Raca).Append(',')
			  .Append(k.Familiaridade).Append(',').Append(k.Relacao)
			  .Append(VistosDeGente.De(k.Nome) != null ? ",f" : ",-");
		sb.Append("||").Append(string.Join(',', World.Instancia?.NomesVisiveis() ?? []));
		// O CARTAO ABERTO E O QUE O SERVIDOR JA CONTOU DELE: abrir, fechar e a resposta chegar mudam a pagina.
		sb.Append("||").Append(_pessoaAberta).Append(',').Append(c.VersaoDosDetalhes);
		return sb.ToString();
	}
}
