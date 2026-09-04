using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Social;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A ABA PEOPLE -- o "Known People" do original: quem voce conhece, quem esta por perto.
///
/// ============================ A LINGUA DA LEARNING, NESTA ABA ============================
/// Ela era duas secoes de "rotulo ..... valor" (foto de antes: `aba-06-people.png`). Agora o pedido
/// de amizade pendente e um cartao de DESTAQUE no topo (e a unica coisa daqui que expira), a aba
/// abre com a FAIXA de quantas pessoas voce lembra, cada conhecido e um CARTAO (nome e pilulas de
/// estado no cabecalho, a barra de proximidade, a ficha "como visto da ultima vez" como nota, e a
/// fileira das declaracoes dentro dele), e "quem esta por perto" e um cartao com os nomes em
/// pilulas. Os verbos, os textos dos botoes e as regras de apagado sao os de antes.
/// ==========================================================================================
/// </summary>
public partial class MenuJogo
{
	// =====================================================================
	// PEOPLE -- o "Known People" do original
	// =====================================================================
	/// <summary>
	/// ============================ A ABA PEOPLE -- O KNOWN-PEOPLE DO ORIGINAL ============================
	/// Ela tinha SO a lista de quem estava na tela, que e o que o `NomesVisiveis` sabia responder.
	/// Agora ela e o que o `ui_tab_people()` do DM (`HtmlUI.dm:545-560`) mostrava: a lista de quem
	/// voce CONHECE, com a ficha "como visto da ultima vez" e o degrau de proximidade.
	///
	/// AS DUAS SECOES CONVIVEM porque respondem coisas diferentes: "quem esta aqui agora" (que e o
	/// que voce usa pra marcar alguem) e "quem eu conheco" (que e o que sobrevive ao logout). No
	/// original as duas tambem existiam, em telas separadas.
	///
	/// O RETRATO NAO VEIO. La ele e o icone achatado do corpo (`contact_portrait`), servido por
	/// `browse_rsc`; aqui a aparencia e montada em camadas pelo cliente e "a foto de como ele
	/// estava" seria um SEGUNDO caminho de vestir alguem. Ficaram os campos de texto, que sao o que
	/// a aba de la realmente le.
	/// ======================================================================================================
	/// </summary>
	private void Gente()
	{
		if (GameClient.Instance is not { } cli) return;

		// ---- o pedido de amizade pendente vem PRIMEIRO: e o unico item aqui que expira ----
		// Cartao de DESTAQUE (borda laranja): e o que se olha primeiro, e o que some se ninguem responder.
		if (cli.PedidoDeAmizade.Length > 0)
		{
			VBoxContainer pedido = Cartao("Pedido de amizade", destaque: true);
			pedido.AddChild(Cabecalho($"{cli.PedidoDeAmizade} quer ser seu amigo", Tema.Destaque));
			// O que aceitar FAZ sai do `Convivio.AceitarAmizade`: sobe a proximidade ate o degrau de amigo
			// (nunca desce quem ja passou dele) e desfaz uma rivalidade -- e a unica porta de volta de quem
			// esta no negativo.
			Nota($"Aceitar já vale como amizade ({Convivio.ExigenciaDeAmigo:0} de proximidade) e desfaz uma "
				 + "rivalidade; recusar não custa nada.", pedido);
			var fila = new HBoxContainer();
			fila.AddThemeConstantOverride("separation", 6);
			var sim = new Button { Text = "Aceitar", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			sim.Pressed += () => cli.SendVerbo("amizade_aceitar");
			var nao = new Button { Text = "Recusar", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			nao.Pressed += () => cli.SendVerbo("amizade_recusar");
			fila.AddChild(sim);
			fila.AddChild(nao);
			pedido.AddChild(fila);
		}

		// ============================ A FAIXA: QUANTAS PESSOAS VOCE LEMBRA ============================
		// O numero desta aba. A legenda conta amigos e rivais quando ha alguem, e explica COMO se faz
		// um amigo quando nao ha: o tempo ao lado da mesma pessoa, com o minuto vindo da conta do Core
		// (`Convivio.MinutosParaVirarAmigo`) e nunca digitado aqui.
		// ============================================================================================
		int amigos = 0, rivais = 0;
		foreach (GameClient.ConhecidoInfo k in cli.Conhecidos)
		{
			if (k.Amizade >= Convivio.ExigenciaDeAmigo) amigos++;
			if (k.Rival) rivais++;
		}
		Faixa("Conhecidos", cli.Conhecidos.Count.ToString(),
			  cli.Conhecidos.Count == 0
				  ? $"ninguém ainda  ·  amizade nasce do tempo ao lado da mesma pessoa ({Convivio.MinutosParaVirarAmigo:0} min pra virar amigo)"
				  : $"{amigos} amigo{(amigos == 1 ? "" : "s")}  ·  {rivais} rival{(rivais == 1 ? "" : "is")}"
					+ "  ·  a ficha de cada um é como você o viu da última vez");

		Secao("Quem você conhece");
		if (cli.Conhecidos.Count == 0)
			Aviso("Você ainda não conviveu com ninguém de quem valha a pena lembrar.");

		foreach (GameClient.ConhecidoInfo c in cli.Conhecidos) CartaoDePessoa(cli, c);

		// QUEM ESTA POR PERTO: os nomes em pilulas, num cartao so. E a lista que se usa pra marcar
		// alguem, e ela muda quando alguem entra ou sai do campo de visao -- por isso os nomes entram
		// na assinatura da aba (ver `AssinaturaBasica`).
		VBoxContainer perto = Cartao("Quem está por perto");
		List<string> nomes = World.Instancia?.NomesVisiveis() ?? [];
		if (nomes.Count == 0) Nota("Ninguém no seu campo de visão.", perto);
		else perto.AddChild(Pilulas([.. nomes.Select(n => (n, Tema.Texto))]));
	}

	/// <summary>
	/// UM CARTAO DE PESSOA: o nome (ou `??? (assinatura)`, a forma do original pra quem tem vinculo sem
	/// ficha), as pilulas de estado, a barra de proximidade, a ficha como nota e as declaracoes.
	///
	/// AS PILULAS SAO OS ESTADOS QUE SE LEEM SEM LER: o degrau de proximidade (`acquaintance_label`),
	/// a relacao que voce declarou, RIVAL, e o degrau de odio (`enmity_label`). A BARRA e a proximidade
	/// contra o teto de 200 (`Convivio.TetoDeAmizade`): verde subindo, e VERMELHA pra quem esta no
	/// negativo -- a escala vai de -200 a +200 e o zero e onde o afeto vira o contrario dele, entao a
	/// barra de um inimigo mostra o TAMANHO da inimizade, na cor dela.
	/// </summary>
	private void CartaoDePessoa(GameClient cli, GameClient.ConhecidoInfo c)
	{
		// SEM FICHA = so vinculo, sem rosto. `??? (assinatura)` e a forma do original.
		string nome = c.Nome.Length > 0 ? c.Nome : $"??? ({c.Assinatura})";
		string grau = Convivio.RotuloDeProximidade(c.Amizade);
		string odio = Convivio.RotuloDeInimizade(c.Inimizade);
		var rel = (Relacao)c.Relacao;
		bool inimigo = c.Amizade < 0;
		bool amigo = c.Amizade >= Convivio.ExigenciaDeAmigo;

		VBoxContainer corpo = Cartao("");
		MarcarCartaoDeItem(corpo, "pessoa", nome);

		corpo.AddChild(Cabecalho(nome, inimigo ? Tema.Perigo : amigo ? Tema.Destaque : Tema.Texto,
			(grau, inimigo ? Tema.Perigo : amigo ? Tema.Bom : Tema.TextoFraco),
			(rel != Relacao.Nenhuma ? Convivio.NomeDaRelacao(rel) : "", Tema.Destaque),
			(c.Rival ? "RIVAL" : "", Tema.Perigo),
			(odio, Tema.Perigo)));

		LinhaComBarra("proximidade", $"{grau} ({c.Amizade:0})",
					  Math.Min(Math.Abs(c.Amizade) / Convivio.TetoDeAmizade, 1),
					  inimigo ? Tema.Perigo : Tema.Bom, corpo, inimigo ? Tema.Perigo : null);

		Nota($"{c.Raca} / {c.Classe}  ·  como visto da última vez  ·  convívio {c.Familiaridade}"
			 + (odio.Length > 0 ? $"  ·  {odio} ({c.Inimizade:0})" : ""), corpo);

		BotoesDeRelacao(cli, c, corpo);
	}

	/// <summary>
	/// AS DECLARACOES QUE ESTA FAMILIARIDADE JA PAGA -- o verb `Relation()` do original, que la era
	/// um `input()` com a lista inteira e a explicacao dos custos no titulo.
	///
	/// O BOTAO APARECE APAGADO em vez de sumir, que e a regra do menu deste port: saber que "Amor"
	/// existe e que custa 200 de convivio e informacao; esconder e esconder o jogo do jogador.
	///
	/// **QUEM DECIDE E O SERVIDOR.** Isto aqui e conveniencia -- o `VerboRelacao` confere a
	/// familiaridade de novo, porque apagar botao nunca foi permissao.
	///
	/// A FILEIRA QUEBRA LINHA (`HFlowContainer`): sao nove botoes dentro de um cartao, e numa tela
	/// estreita a fila reta sairia pela borda.
	/// </summary>
	private void BotoesDeRelacao(GameClient cli, GameClient.ConhecidoInfo c, Control pai)
	{
		if (c.Nome.Length == 0) return;   // sem ficha nao ha o que declarar

		var fila = new HFlowContainer();
		fila.AddThemeConstantOverride("h_separation", 2);
		fila.AddThemeConstantOverride("v_separation", 2);
		foreach (Relacao r in Convivio.Declaraveis)
		{
			int pede = Convivio.FamiliaridadeExigida(r);
			bool pode = c.Familiaridade >= pede;
			var b = new Button
			{
				Text = Convivio.NomeDaRelacao(r),
				Disabled = !pode || (Relacao)c.Relacao == r,
				TooltipText = pode ? "declarar" : $"exige {pede} de convivio (voce tem {c.Familiaridade})",
			};
			b.AddThemeFontSizeOverride("font_size", 11);
			string sig = c.Assinatura, nome = Convivio.NomeDaRelacao(r);
			b.Pressed += () => cli.SendVerbo("relacao", $"{sig}|{nome}");
			fila.AddChild(b);
		}
		pai.AddChild(fila);
	}

	/// <summary>
	/// O PEDACO DESTA ABA NA ASSINATURA DE CACHE (ver `MenuJogo.Assinatura`): tudo que ESTA aba
	/// desenha e que a assinatura basica (em MenuJogo.cs) nao cobre entra aqui, nos mesmos
	/// arredondamentos em que e desenhado.
	///
	/// A BASICA COBRE o pedido, os vinculos (pontos a inteiro, convivio, relacao, rival, odio) e os
	/// nomes visiveis. Esta pagina tambem ESCREVE, por pessoa, o nome, a raca e a classe da ficha -- a
	/// foto e refeita no maximo uma vez por minuto (`Convivio.IntervaloDoRetratoMs`), entao entram
	/// como comprimento somado, que e barato e muda quando qualquer um deles muda.
	/// </summary>
	private string ExtraDaAssinaturaDeGente(SheetState f)
	{
		if (GameClient.Instance is not { } c) return "";
		int letras = 0;
		foreach (GameClient.ConhecidoInfo k in c.Conhecidos) letras += k.Nome.Length + k.Raca.Length + k.Classe.Length;
		return letras.ToString();
	}
}
