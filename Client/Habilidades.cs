using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// O QUE ESTE PERSONAGEM PODE ACIONAR, virando botao no menu (tecla P, aba Skills).
///
/// Cada habilidade aqui e um <see cref="Verbo"/> que manda um id pelo canal unico de habilidade
/// (ver `GameServer.Raciais.cs`). O cliente NAO decide se pode: ele oferece, o servidor valida e
/// responde pelo chat. Botao que aparece e e recusado com um motivo ensina o jogo; botao que nao
/// aparece esconde o jogo.
///
/// COMO PLUGAR UMA TECNICA NOVA (e o caminho que as 319 skills vao usar):
///   1. um `case` no `UsarHabilidade` do servidor;
///   2. uma linha aqui dizendo quem ve o botao.
/// </summary>
public static class Habilidades
{
	/// <summary>
	/// Refaz a lista a partir da raca e do que foi aprendido. Chamado quando a ficha lenta
	/// chega -- e ela que traz a raca, e ela so chega depois de entrar no mundo.
	/// </summary>
	public static void Montar(string raca)
	{
		Verbos.Limpar();
		// `Verbos.Limpar()` apaga TUDO, inclusive os verbs fixos de Other/Admin. Sem isto eles
		// sumiriam na primeira troca de personagem e so voltariam no relog.
		VerbosDoJogo.Limpar();
		VerbosDoJogo.Registrar();

		// ============================ A PORTA DA MEDITACAO PROFUNDA ============================
		// A MENTE E DE TODO MUNDO: treinar contra si mesmo nao e dom de raca nenhuma. E ela nao pende
		// de skill: no original o transe e uma OPCAO do proprio verb `Meditate` (`MindMeditate.dm`,
		// cabecalho), e nao algo que se compra.
		//
		// O `Disponivel` e o unico portao do cliente, e ele e o mesmo do servidor (`Ficha.med`): quem
		// nao esta meditando ve o botao APAGADO, que e como se descobre que a tecla M vem antes.
		// ==================================================================================
		Verbos.Registrar(new Verbo(
			"Meditação profunda",
			Verbos.Aprendizado,
			"Medite (M) e mergulhe na própria mente pra lutar contra uma cópia exata de você -- mesmo "
			+ "poder, mesma velocidade: a luta mede a sua habilidade, não o seu BP.\n\n"
			+ "Lá dentro nada é real: ferimentos NÃO voltam com você, não há Zenkai, e o treino rende "
			+ "só um quarto do normal. Seu corpo fica meditando onde estava -- quem bater nele te "
			+ "acorda na hora.\n\n"
			+ "Se você meditar COLADO em alguém que já está em transe, você entra na mente DELE "
			+ "(e o reflexo dele se desfaz: lá dentro ficam só vocês dois).",
			() => GameClient.Instance?.SendHabilidade("mente"),
			() => GameClient.Instance?.Atividade == Jandirus.Net.Protocol.Activity.Meditando)
			{ Chave = "hab:mente" });

		Verbos.Registrar(new Verbo(
			"Sair da mente",
			Verbos.Aprendizado,
			"Abre os olhos e volta pro seu corpo -- exatamente como ele estava quando você entrou.",
			() => GameClient.Instance?.SendHabilidade("sairdamente")) { Chave = "hab:sairdamente" });

		DosChefesDaMente();

		Verbos.Registrar(new Verbo(
			"Decolar",
			Verbos.Outros,
			"Deixa a superficie e sobe ao espaco. So funciona de um planeta que esta no mapa do "
			+ "universo -- do Outro Mundo nao se decola.",
			() => GameClient.Instance?.SendHabilidade("decolar")) { Chave = "hab:decolar" });

		// ============================ O NADO TEM VERB, E O VOO NAO -- NAO E CONTRADICAO ============================
		// O dono pediu os dois separadamente e por escrito: *"vamos colocar o VERB DE SWIM"*, depois
		// de ter cortado o verb do voo. A diferenca e de DESCOBERTA, e ela e real:
		//
		//   * voar libera sozinho na metade da maestria de Ki, e o servidor ANUNCIA no degrau -- quem
		//     chegou la ja sabe;
		//   * nadar nao libera nada e nao avisa ninguem: e uma acao que todo personagem sempre teve
		//     (no DM e um `mob/verb` seco, sem skill, sem custo pra ligar) e que so serve na beira de
		//     um lago. Sem um botao, a unica forma de descobrir que ela existe seria alguem contar.
		//
		// NA ABA "Skills" porque e la que ele mora no original (`set category="Skills"`, `Swim.dm:6`).
		//
		// APARECE SEMPRE, E NUNCA APAGADO. A condicao de uso ("tem agua aqui?") depende do tile a
		// frente, que muda a cada passo -- um `Disponivel` piscaria no menu aberto e, pior, o cliente
		// teria que responder sozinho uma pergunta que quem responde e o servidor (ele e o dono do
		// mapa de agua). Clicar fora d'agua devolve "nao da pra nadar aqui", que e a mensagem do
		// original e ensina mais do que um botao cinza.
		// ======================================================================================================
		Verbos.Registrar(new Verbo(
			"Nadar",
			Verbos.Skills,
			"Liga e desliga o nado. Só funciona com água à sua frente (ou embaixo de você): a água "
			+ "não se atravessa a pé -- só nadando ou voando.\n\n"
			+ "Nadando você entra na pose de voo SEM sair do chão, anda mais devagar e gasta Ki aos "
			+ "poucos; quanto mais você nada, mais barato fica. Chegar no seco desliga sozinho, e "
			+ "ficar sem Ki também (aí você é levado pra margem).",
			() => GameClient.Instance?.SendHabilidade("nadar")) { Chave = "hab:nadar" });

		// ============================ O VOO NAO TEM VERB, E NAO DEVE TER ============================
		// Eu tinha posto dois botoes aqui -- "Voar" e "Velocidade de voo" -- atras da skill
		// `/datum/skill/flying`. O dono cortou os dois:
		//
		//   * voar LIBERA SOZINHO em metade da maestria de Ki (que se treina MEDITANDO), e antes
		//     disso nem deve aparecer. E tecla F, nao botao: quem chegou la ja sabe, porque o
		//     servidor avisa no degrau.
		//   * "velocidade de voo" nao existe -- e o SHIFT, a mesma tecla de correr no chao.
		//
		// Fica anotado pra ninguem "consertar" isto de volta: um verb aqui reintroduziria uma
		// compra pra uma coisa que e consequencia de treinar, e uma marcha que se esquece ligada
		// drenando o Ki mais caro do jogo. Ver `GameServer.Voo.cs`.
		// ==========================================================================================

		// ============================ O OOZARU TINHA IDA E NAO TINHA VOLTA ============================
		// `case "oozaru": ReverterOozaru(pl)` existe em `GameServer.Raciais.cs:159` desde que o Oozaru
		// foi portado, e NINGUEM no jogo conseguia manda-lo: nao havia tecla, botao nem verb. Era o
		// caso classico deste port -- a regra escrita de um lado do fio e sem chamador do outro.
		//
		// A IDA mora embaixo da lua (o botao vermelho do `LuaNoCeu`); a VOLTA mora aqui, e nao la, de
		// proposito: virar e um gesto que se faz OLHANDO pra lua, voltar e uma acao que se procura no
		// menu. Um mesmo lugar que troca de significado ensinaria que os dois sao a mesma coisa.
		//
		// A raca decide quem VE (o mesmo par de `GameServer.Combat.cs:107` -- e o unico jeito de haver
		// um rabo pra a lua morder), e o estado decide quem pode APERTAR. Apagado e visivel de novo
		// pelo motivo de sempre: quem nunca virou macaco descobre pelo botao cinza que da pra virar.
		if (raca is "Saiyan" or "Halfbreed")
			Verbos.Registrar(new Verbo(
				"Voltar ao normal",
				Verbos.Skills,
				"Domina o Oozaru e volta ao seu corpo. Exige dominio sobre a fera -- e o Oozaru "
				+ "Dourado nao volta por vontade: ele se cansa, ou vira Super Saiyajin 4.",
				() => GameClient.Instance?.SendHabilidade("oozaru"),
				() => GameClient.Instance is { } c
					  && c.MeuOozaru != Jandirus.Core.Forms.FormaOozaru.Nao) { Chave = "hab:oozaru" });

		if (raca is "Namekian" or "Majin" or "BioAndroid" or "Shapeshifter")
			Verbos.Registrar(new Verbo(
				"Regenerar",
				Verbos.Skills,
				"Faz um membro decepado voltar a crescer, ou cura por inteiro o mais ferido. "
				+ "Custa 70% do Ki maximo e tem 10s de espera.",
				() => GameClient.Instance?.SendHabilidade("regenerar"),
				// APAGA quando falta Ki, mas continua na lista: saber que a tecnica existe e que
				// falta Ki e informacao util; sumir com ela e esconder o jogo.
				() => GameClient.Instance is { } c && c.Sheet.MaxKi > 0 && c.Sheet.Ki >= c.Sheet.MaxKi * 0.7)
				{ Chave = "hab:regenerar" });

		DoDiscipulado();
		DoEnsinoDeSkill();
		DasDisciplinas();
		DasSkills();
		DosEstilos();
		DasCustomizadas();
	}

	/// <summary>
	/// UM BOTAO POR CHEFE QUE ESTE PERSONAGEM JA VIU -- a luta simulada dentro da propria mente.
	///
	/// ============================ A LISTA E DO SERVIDOR, E O NOME VEM COM ELA ============================
	/// O cliente nao le o `npcs.json`: quem sabe que voce viu Freeza -- e como Freeza se chama -- e o
	/// servidor, e ele manda os dois no `S2C.MenteChefes`. Mesmo desenho do
	/// <see cref="DasCustomizadas"/>, que ja transforma "escolha uma das dez" em dez botoes: o canal
	/// de habilidade carrega um id de texto e mais nada, entao o MENU e a propria lista de verbs.
	/// ================================================================================================
	///
	/// SEM `Disponivel`, e e a regra da casa (ver `Verbo.Disponivel`): o botao aparece fora da mente
	/// tambem, e apertar la fora responde *"isso so acontece dentro da sua mente"*. Um botao que
	/// aparece e recusa com uma frase ensina o jogo; um botao que some esconde o jogo -- e aqui o que
	/// ele ensina e justamente que a lembranca existe e onde ela se usa.
	/// </summary>
	public static void DosChefesDaMente()
	{
		if (GameClient.Instance is not { } cli || cli.ChefesVistos.Count == 0) return;

		foreach (GameClient.ChefeVisto c in cli.ChefesVistos)
		{
			string molde = c.Molde;
			Verbos.Registrar(new Verbo(
				$"Enfrentar na mente: {c.Nome}",
				Verbos.Aprendizado,
				$"Ergue {c.Nome} da sua memória, dentro da sua mente, com a ficha inteira dele -- as "
				+ "transformações, os estágios e o poder de hoje. É treino: ele não decepa nem mata, "
				+ "nada do que acontecer volta com você, e não há Zenkai.\n\n"
				+ "Só funciona na SUA mente, e só se você estiver sozinho nela.",
				() => GameClient.Instance?.SendHabilidade($"mente_chefe:{molde}"))
				{ Chave = $"hab:mente_chefe:{molde}" });
		}
	}

	/// <summary>
	/// MESTRE E ALUNO -- os verbos da aba "Learning", que e onde eles moram no original
	/// (`set category = "Learning"`, `MasterStudent.dm:423` e `:477`).
	///
	/// ============================ TODOS APARECEM SEMPRE, E NENHUM E CONDICIONADO ============================
	/// Nao ha `Disponivel` em nenhum deles, e nao e descuido. As perguntas que decidiriam
	/// ("tenho mestre?", "quantos alunos?", "ha convite pendente?") sao **estado do servidor**, e
	/// hoje nada disso viaja pro cliente -- o dia em que viajar, e o dia de acender/apagar. Ate la
	/// vale a regra da casa (ver `Verbo.Disponivel`): um verb que aparece e recusa com uma frase
	/// ensina o jogo; um verb que nao existe esconde o jogo.
	///
	/// E a recusa nunca e muda: cada uma delas tem mensagem propria em
	/// `GameServer.Mestre.PorQueNaoVincula` -- inclusive "voce precisa ser 3x mais forte", que e a
	/// unica maneira de o jogador descobrir o portao.
	/// ==================================================================================================
	/// </summary>
	private static void DoDiscipulado()
	{
		Verbos.Registrar(new Verbo(
			"Convidar aluno",
			Verbos.Aprendizado,
			"Oferece a quem esta na sua frente um lugar como seu aluno. Voce precisa ser pelo menos "
			+ $"{Jandirus.Core.Skills.Discipulado.RazaoDeBp:0}x mais forte que ele (BP de verdade, nao o "
			+ $"aparente), e pode guiar no maximo {Jandirus.Core.Skills.Discipulado.MaxAlunos} pessoas.",
			() => GameClient.Instance?.SendHabilidade("mst_convidar")) { Chave = "hab:mst_convidar" });

		Verbos.Registrar(new Verbo(
			"Aceitar",
			Verbos.Aprendizado,
			"Aceita o convite de mestre -- ou o ensinamento que ele esta te oferecendo agora.",
			() => GameClient.Instance?.SendHabilidade("mst_aceitar")) { Chave = "hab:mst_aceitar" });

		Verbos.Registrar(new Verbo(
			"Recusar",
			Verbos.Aprendizado,
			"Recusa o convite ou o ensinamento pendente.",
			() => GameClient.Instance?.SendHabilidade("mst_recusar")) { Chave = "hab:mst_recusar" });

		Verbos.Registrar(new Verbo(
			"Ajudar a transformar",
			Verbos.Aprendizado,
			"Provoca (ou guia) o aluno na sua frente ate uma transformacao. Com um mestre ao lado, o "
			+ "poder minimo que aquela forma exige DELE cai pela metade -- e o corte fica pra sempre. "
			+ "O aluno precisa ja ter visto a forma, ou voce precisa possui-la. "
			+ $"Custa {Jandirus.Core.Skills.Discipulado.RecargaDeEnsinoSegundos / 60:0} min de folego.",
			() => GameClient.Instance?.SendHabilidade("mst_ensinar")) { Chave = "hab:mst_ensinar" });

		Verbos.Registrar(new Verbo(
			"Dispensar aluno",
			Verbos.Aprendizado,
			"Desfaz o vinculo com o aluno que estiver na sua frente.",
			() => GameClient.Instance?.SendHabilidade("mst_dispensar")) { Chave = "hab:mst_dispensar" });

		Verbos.Registrar(new Verbo(
			"Deixar meu mestre",
			Verbos.Aprendizado,
			"Segue o proprio caminho. O ganho extra de treinar contra ele acaba junto.",
			() => GameClient.Instance?.SendHabilidade("mst_largar")) { Chave = "hab:mst_largar" });
	}

	/// <summary>
	/// O ENSINO DE SKILL -- o `Teach_Skill` e o `Forget_Studied_Skill` do
	/// `Code/Modules/Skills/Skills Master/teachable.dm`, que la tambem sao `set category = "Learning"`.
	///
	/// ============================ O MENU DO DM VIRA UM BOTAO POR SKILL ============================
	/// La o verb abre `input("Teach which Skill?") in Teach`. Aqui o canal de habilidade carrega um
	/// id de texto e mais nada -- entao o menu vira a propria lista de verbs, um por skill que este
	/// personagem sabe e que se ensina, com o typepath no sufixo. E o mesmo desenho do
	/// <see cref="DasCustomizadas"/>, que ja transforma "escolha uma das dez" em dez botoes.
	///
	/// ============================ O CLIENTE NAO SABE O QUE FOI ENSINADO A ELE ============================
	/// O `wastaught` **nao viaja no fio**: o pacote `S2C.Skills` manda os typepaths aprendidos e
	/// mais nada, e acrescentar uma segunda lista la mudaria o formato pra atender a um botao.
	///
	/// A consequencia e deliberada e boa: quem aprendeu de um mestre VE o botao de ensinar e leva a
	/// recusa `"foi ENSINADO a voce, e quem aprendeu de um mestre nao repassa"`. E a regra da casa
	/// (ver `Verbo.Disponivel`) caindo no melhor caso possivel -- a frase de recusa e o unico lugar
	/// do jogo onde a regra central deste sistema e dita em voz alta. Esconder o botao esconderia a
	/// regra.
	/// ==================================================================================================
	/// </summary>
	public static void DoEnsinoDeSkill()
	{
		SkillCatalog? cat = MenuJogo.CatalogoPublico();
		GameClient? cli = GameClient.Instance;
		if (cat == null || cli == null) return;

		Verbos.Registrar(new Verbo(
			"Aceitar a licao",
			Verbos.Aprendizado,
			"Aceita a habilidade que alguem esta te ensinando agora. Voce precisa TER os marcos que "
			+ "ela exige -- mas aprender nao gasta marco nenhum.",
			() => GameClient.Instance?.SendHabilidade("ens_licao_sim")) { Chave = "hab:ens_licao_sim" });

		Verbos.Registrar(new Verbo(
			"Recusar a licao",
			Verbos.Aprendizado,
			"Recusa a habilidade oferecida.",
			() => GameClient.Instance?.SendHabilidade("ens_licao_nao")) { Chave = "hab:ens_licao_nao" });

		foreach (string path in cli.SkillsAprendidas.OrderBy(p => p, StringComparer.Ordinal))
		{
			Skill? s = cat.Get(path);
			if (s == null || s.Arvore || !s.Ensinavel || s.Nome.Length == 0) continue;
			string idLocal = path;

			Verbos.Registrar(new Verbo(
				$"Ensinar: {s.Nome}",
				Verbos.Aprendizado,
				$"Mostra {s.Nome} a quem estiver colado na sua frente. Ele precisa ter "
				+ $"{Jandirus.Core.Skills.SkillCatalog.CustoDoEnsino(s)} marco(s) livre(s) -- e nao gasta "
				+ "nenhum. Quem aprende assim NAO pode repassar, e voce fica "
				+ $"{Jandirus.Core.Skills.EnsinoDeSkill.RecargaSegundos:0} s sem poder ensinar de novo. "
				+ "Voce nao ganha nada com isso.",
				() => GameClient.Instance?.SendHabilidade($"ens_ensinar:{idLocal}"))
				{ Chave = $"hab:ens_ensinar:{idLocal}" });

			Verbos.Registrar(new Verbo(
				$"Esquecer a licao: {s.Nome}",
				Verbos.Aprendizado,
				$"Deixa {s.Nome} pra tras -- **so funciona se alguem te ensinou**. O que voce comprou "
				+ "com os proprios marcos nao se esquece por aqui. Nenhum marco volta, porque nenhum "
				+ "tinha sido gasto.",
				() => GameClient.Instance?.SendHabilidade($"ens_esquecer:{idLocal}"))
				{ Chave = $"hab:ens_esquecer:{idLocal}" });
		}
	}

	/// <summary>
	/// AS TECNICAS QUE O JOGADOR INVENTOU -- uma por botao, mais a porta da mesa de montagem.
	///
	/// ============================ ELAS NAO PASSAM PELO `DasSkills` ============================
	/// Todo botao de tecnica ate aqui nasce de uma SKILL aprendida: le-se o livro, pergunta-se ao
	/// catalogo quais verbos ela destrava, e registra-se um por verbo. Uma tecnica inventada nao tem
	/// skill nenhuma atras dela -- no DM o verbo e concedido pelo `after_learn()` do proprio datum
	/// (`customattacks.dm:122`), sem `assignverb` e sem arvore. Ela vem pelo pacote
	/// `S2C.Customizadas`, que e o unico lugar em que ela existe.
	///
	/// A MESA FICA EM "Learning" e os TIROS em "Skills", que sao exatamente as duas categorias do
	/// original: `Create_Attack`/`Customize_Attack`/`Forget_Attack` sao `set category = "Learning"`
	/// (`:223`), e os dez `Custom_Attack&lt;n&gt;` sao `set category = "Skills"` (`:176`).
	/// ======================================================================================
	/// </summary>
	public static void DasCustomizadas()
	{
		Verbos.Registrar(new Verbo(
			"Inventar técnicas de ki",
			Verbos.Aprendizado,
			"Abre a mesa onde você desenha suas próprias técnicas: raio, bola ou teleguiado, com "
			+ $"{Jandirus.Core.Skills.TecnicaCustomizada.PontosTotais} pontos pra distribuir entre "
			+ "potência, carga, custo de energia, velocidade e alcance. Cabem "
			+ $"{Jandirus.Core.Skills.TecnicaCustomizada.Maximo} na sua cabeça.",
			() => TelaDeTecnicas.Instancia?.Abrir()) { Chave = "ui:mesadetecnicas" });

		if (GameClient.Instance is not { } cli) return;

		foreach (Jandirus.Core.Skills.TecnicaCustomizada t in cli.Customizadas)
		{
			if (!t.Criada) continue;
			string verbo = t.Verbo;
			string tipo = t.Tipo switch
			{
				Jandirus.Core.Combat.TipoDeProjetil.Beam => "Raio canalizado",
				Jandirus.Core.Combat.TipoDeProjetil.Blast => "Bola solta",
				_ => "Esfera teleguiada",
			};

			// A DESCRICAO MOSTRA OS NUMEROS DE VERDADE, e nao so o texto que o jogador escreveu:
			// ele gastou pontos escolhendo cada um, e nao tem outro lugar pra confirmar que a compra
			// pegou sem reabrir a mesa.
			string ficha = $"{tipo}. Potência {t.BaseDano:0.#}, energia {t.CustoKi:0}, "
						 + $"velocidade {t.Velocidade:0.#}, alcance {t.Alcance:0} tiles";
			if (t.Tipo == Jandirus.Core.Combat.TipoDeProjetil.Beam)
				ficha += $", carga {t.CargaMinima:0.#}s" + (t.Instantaneo ? " (sai sozinho)" : "");
			if (t.UsaStamina) ficha += $", {t.CustoStamina:0} de fôlego";

			Verbos.Registrar(new Verbo(
				t.Nome,
				Verbos.Skills,
				t.Desc.Length > 0 ? $"{t.Desc}\n\n{ficha}." : $"{ficha}.",
				() => GameClient.Instance?.SendHabilidade(verbo)) { Chave = $"custom:{verbo}" });
		}
	}

	/// <summary>
	/// OS BOTOES DAS DISCIPLINAS DIVINAS -- Ultra Instinto e Poder da Destruicao.
	///
	/// ============================ CADA FAIXA APARECE QUANDO DESTRAVA ============================
	/// A lista sai do <see cref="Jandirus.Core.Forms.Disciplinas"/>, entao acrescentar uma faixa la
	/// e ve-la aqui e a mesma coisa -- pela mesma razao do catalogo de formas: uma habilidade que
	/// existe no servidor e nao tem botao e uma habilidade que ninguem usa.
	///
	/// As PASSIVAS entram como texto, sem acao: elas nao tem o que apertar, mas o jogador precisa
	/// saber que as tem. Passiva que ninguem sabe que ganhou e indistinguivel de passiva quebrada.
	/// ========================================================================================
	/// </summary>
	public static void DasDisciplinas()
	{
		if (GameClient.Instance is not { } cli) return;
		Jandirus.Net.Protocol.AtributosState a = cli.Atributos;
		if (a.Disciplina == 0) return;

		Jandirus.Core.Forms.DisciplinaDef def = a.Disciplina == 1
			? Jandirus.Core.Forms.Disciplinas.UltraInstinct
			: Jandirus.Core.Forms.Disciplinas.PoderDaDestruicao;
		string pre = a.Disciplina == 1 ? "ui" : "ue";

		foreach (Jandirus.Core.Forms.Degrau d in def.Degraus)
		{
			if (a.DiscReal < d.Pct) continue;   // ainda nao destravou

			// O TOGGLE (faixa 0%) e a unica passiva com acao: ligar e desligar.
			if (d.Pct <= 0)
			{
				Verbos.Registrar(new Verbo(
					a.DiscLigada ? $"{d.Nome}  (ligada)" : d.Nome,
					Verbos.Skills,
					$"{d.Desc}\n\nEnergia atual {a.DiscAtual:0}% de {a.DiscReal:0}% -- ela CAI "
					+ "enquanto estiver ligada e nao volta em combate. Transformar renova.",
					() => GameClient.Instance?.SendHabilidade($"{pre}_toggle")) { Chave = $"hab:{pre}_toggle" });
				continue;
			}

			if (!d.Ativa)
			{
				// PASSIVA JA GANHA: sem acao, so a ficha. `null` de acao = so leitura.
				Verbos.Registrar(new Verbo($"{d.Nome}  ({d.Pct:0}%)", Verbos.Skills, d.Desc, null)
					{ Chave = $"passiva:{pre}:{d.Pct:0}" });
				continue;
			}

			string id = d.Nome.Contains("Godly", StringComparison.OrdinalIgnoreCase)
				? "ui_godlydisplay" : "ue_hakai";
			Verbos.Registrar(new Verbo(d.Nome, Verbos.Skills, d.Desc,
				() => GameClient.Instance?.SendHabilidade(id)) { Chave = $"hab:{id}" });
		}

		// ENSINAR: quem sabe pode passar adiante. E o unico caminho de a disciplina existir no
		// servidor -- ver a cadeia de ensino em `GameServer.Disciplinas.cs`.
		Verbos.Registrar(new Verbo(
			$"Ensinar {def.Nome}",
			Verbos.Skills,
			$"Ensina o {def.Nome} a quem estiver na sua frente. O aluno precisa de "
			+ $"{Jandirus.Core.Forms.Disciplinas.GodKiParaAprender:0}% de maestria no ki divino e "
			+ "nao pode ter trilhado a outra disciplina -- as duas se excluem, e nao ha volta.",
			() => GameClient.Instance?.SendHabilidade($"{pre}_ensinar")) { Chave = $"hab:{pre}_ensinar" });
	}

	/// <summary>
	/// UM BOTAO POR ESTILO APRENDIDO, mais um pra soltar a postura.
	///
	/// O estilo entra no menu como VERBO e nao como aba propria porque e exatamente o que ele e
	/// no original: uma acao ("Pick Current Style"), nao uma ficha. Quem tem um estilo so nem
	/// precisa abrir nada -- ve o botao, aperta, e o multiplicador ja esta valendo.
	/// </summary>
	public static void DosEstilos()
	{
		if (GameClient.Instance is not { } cli || cli.Estilos.Count == 0) return;

		foreach (GameClient.EstiloInfo e in cli.Estilos)
		{
			string id = e.Id;
			bool ativo = string.Equals(cli.EstiloAtual, id, StringComparison.OrdinalIgnoreCase);
			Verbos.Registrar(new Verbo(
				ativo ? $"{e.Nome}  (em uso)" : e.Nome,
				Verbos.Skills,
				$"Assume a postura de {e.Nome}. Maestria {e.Maestria:0.#} de {e.Teto:0}. "
				+ "A maestria sobe lutando e treinando NESTE estilo, e enferruja nos que voce deixa "
				+ "parados -- por isso escolher importa.",
				() => GameClient.Instance?.SendEstilo(id),
				() => !ativo) { Chave = $"estilo:{id}" });
		}

		Verbos.Registrar(new Verbo(
			"Soltar a postura",
			Verbos.Skills,
			"Luta sem estilo nenhum. Perde os multiplicadores e o bonus de dano da disputa.",
			() => GameClient.Instance?.SendEstilo("-"),
			() => GameClient.Instance is { } c && c.EstiloAtual.Length > 0) { Chave = "estilo:-" });
	}

	/// <summary>
	/// OS BOTOES QUE VIERAM DAS SKILLS APRENDIDAS.
	///
	/// Nenhuma linha aqui conhece tecnica nenhuma: le o que foi aprendido, pergunta ao catalogo
	/// quais verbs aquilo destrava e registra um botao por verb. Portar a proxima tecnica nao
	/// mexe neste arquivo -- so no `UsarTecnica` do servidor e na tabela do <see cref="Tecnicas"/>.
	///
	/// A TECNICA AINDA NAO PORTADA TAMBEM VIRA BOTAO, apagado e dizendo por que. Esconde-la
	/// deixaria o jogador achando que perdeu os marcos; um botao cinza com "o efeito ainda nao
	/// foi trazido do jogo antigo" e a verdade, e some sozinho no dia em que a tecnica sair.
	/// </summary>
	public static void DasSkills()
	{
		SkillCatalog? cat = MenuJogo.CatalogoPublico();
		GameClient? cli = GameClient.Instance;
		if (cat == null || cli == null) return;

		var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string path in cli.SkillsAprendidas)
		{
			Skill? s = cat.Get(path);
			if (s == null) continue;
			foreach (string id in s.Verbos)
			{
				if (!vistos.Add(id)) continue;
				Tecnicas.Tecnica? t = Tecnicas.Get(id);
				if (t == null) continue;

				bool pronta = t.Modo != Modo.NaoPortada;
				string idLocal = id;
				Verbos.Registrar(new Verbo(
					pronta ? t.Nome : $"{t.Nome} (nao portada)",
					t.Aba,
					t.Desc,
					() => GameClient.Instance?.SendHabilidade(idLocal),
					pronta ? null : () => false) { Chave = $"hab:{idLocal}" });
			}
		}
	}
}
