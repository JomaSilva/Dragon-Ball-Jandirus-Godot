using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// O QUE ESTE PERSONAGEM PODE ACIONAR, virando botao no menu (tecla P, aba Skills).
///
/// Cada habilidade aqui e um <see cref="Verbo"/> que manda um id pelo canal unico de habilidade
/// (ver `GameServer.Raciais.cs`). O cliente NAO decide se pode: ele oferece, o servidor valida e
/// responde pelo chat. O botao de quem TEM a habilidade e so nao pode usa-la AGORA aparece e explica
/// por que (falta Ki, falta alvo); o de quem nao a tem -- outra raca, skill que nao aprendeu, convite
/// que ninguem fez -- nao aparece: ver o requisito de cada um em `Core/Verbos/RequisitosDosVerbos.cs`.
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
	///
	/// ============================ QUEM VE CADA BOTAO NAO SE DECIDE AQUI ============================
	/// Este arquivo REGISTRA; quem decide se o verb aparece e o requisito dele na tabela unica
	/// (`Core/Verbos/RequisitosDosVerbos.cs`), achado pela `Chave` (`hab:fus_namek`, `hab:oozaru`...) e lido
	/// pelo `Verbos.Visivel`. Era um `if (raca == ...)` em volta de cada registro -- e, onde nao havia `if`,
	/// era "aparece pra todo mundo e o servidor recusa": a fusao Namekuseijin na tela de um Saiyajin, o
	/// "Aceitar a fusao" de quem ninguem convidou. O dono (2026-10-10) chamou isso de poluido, e a regra
	/// virou: sem o requisito, o botao nao existe. Os `if` que sobraram escolhem TEXTO, nao plateia.
	/// ============================================================================================
	/// </summary>
	public static void Montar(string raca)
	{
		Verbos.Limpar();
		// `Verbos.Limpar()` apaga TUDO, inclusive os verbs fixos de Other/Admin. Sem isto eles
		// sumiriam na primeira troca de personagem e so voltariam no relog.
		VerbosDoJogo.Limpar();
		VerbosDoJogo.Registrar();

		// ============================ A PORTA DA MEDITACAO PROFUNDA MUDOU DE LUGAR ============================
		// Havia aqui um verb "Meditação profunda" nesta aba, apagado enquanto o jogador nao estivesse
		// meditando -- ou seja, um botao que exigia que voce ja soubesse apertar M antes de vir aqui.
		// **Ele foi DELETADO** (nao escondido) a pedido do dono: *"N faca a meditacao profunda ser um
		// VERB DO MENU DO P. faca q ao MEDITAR uma TELINHA vai abrir e perguntar se vc quer so MEDITAR
		// NORMAL ou ir pra MEDITACAO PROFUNDA"*.
		//
		// Hoje a porta e o proprio gesto de meditar: a tecla M abre a <see cref="TelaDeMeditacao"/>, e
		// e la que estao os dois caminhos, a explicacao do que ha na mente e a recusa com o motivo.
		// O CANAL NAO MUDOU -- a telinha manda o mesmo `SendHabilidade("mente")` que este verb mandava.
		//
		// O "Sair da mente" FICA: ele nao e uma escolha de entrada, e a saida de quem ja esta la
		// dentro -- e la dentro nao ha telinha nenhuma.
		// ==============================================================================================

		Verbos.Registrar(new Verbo(
			"Sair da mente",
			Verbos.Aprendizado,
			"Abre os olhos e volta pro seu corpo -- exatamente como ele estava quando você entrou.",
			SaidaDaMente.Pedir) { Chave = "hab:sairdamente" });

		// O `Check_Skill_Stats` DO ORIGINAL: o nivel e o exp de cada skill que sobe sozinha, no chat. Sem ele o
		// jogador meditava uma hora atras do primeiro nivel do Ki Unlocked sem saber se alguma coisa andava.
		Verbos.Registrar(new Verbo(
			"Progresso das skills",
			Verbos.Aprendizado,
			"Mostra no chat o nível de cada skill sua que sobe sozinha e quanto falta pro próximo. "
			+ "Meditar faz as de Ki subirem mais rápido.",
			() => GameClient.Instance?.SendVerbo("skills_progresso")) { Chave = "verbo:skills_progresso" });

		DosChefesDaMente();

		// O VERB "Decolar" SAIU DA ABA OTHER (2026-09-03, pedido do dono). Subir ao espaco e VOAR
		// PRA CIMA ate romper a atmosfera (`GameServer.Voo.TentarRomperAAtmosfera`), que cobra Ki e
		// poder pra isso; um botao de menu que dava o mesmo salto de graca era uma segunda porta,
		// e a mais barata. A habilidade `decolar` continua no servidor pras bancadas que precisam
		// chegar ao espaco sem voar (ver `GameServer.Raciais.UsarHabilidade`).

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

		// RELAXAR / FICAR PRONTO -- o `Toggle Readiness` do original (`mob/default/verb/ReadyUp`, `set
		// category="Skills"`, `Power Control.dm:39-52`). La e de todo corpo; aqui pede o controle de ki (regra do
		// dono de 2026-10-12: *"relaxar e o poder cair deveria ser somente pra quem sabe controlar o proprio
		// poder"*) -- a linha dele esta na tabela de requisitos (`RequisitosDosVerbos`, `hab:prontidao`).
		// E um verb (e nao so o botao da faixa da aba Ki) pra poder ganhar uma TECLA na tela de teclas.
		Verbos.Registrar(new Verbo(
			"Relaxar / ficar pronto",
			Verbos.Skills,
			"Com o poder inteiro: relaxa. O seu poder cai ao mínimo, ninguém mais sente a sua energia (nem "
			+ "scouter) e o vigor volta o mais depressa que há -- mas você fica fraco de verdade, e furioso "
			+ "não consegue. Relaxado, ou em qualquer ponto abaixo de 100%: fica pronto, e o poder volta "
			+ "inteiro na hora.\n\nO estado aparece no painel de status e na aba Ki.",
			() => GameClient.Instance?.SendHabilidade(MenuJogo.VerboDaProntidao)) { Chave = "hab:prontidao" });

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
		// A raca decide quem VE (o requisito de `hab:oozaru` na tabela: o mesmo par de
		// `GameServer.Combat.cs:107` -- e o unico jeito de haver um rabo pra a lua morder), e o estado
		// decide quem pode APERTAR. Apagado e visivel de novo pelo motivo de sempre: quem nunca virou
		// macaco descobre pelo botao cinza que da pra virar.
		Verbos.Registrar(new Verbo(
			"Voltar ao normal",
			Verbos.Skills,
			"Domina o Oozaru e volta ao seu corpo. Exige dominio sobre a fera -- e o Oozaru "
			+ "Dourado nao volta por vontade: ele se cansa, ou vira Super Saiyajin 4.",
			() => GameClient.Instance?.SendHabilidade("oozaru"),
			() => GameClient.Instance is { } c
				  && c.MeuOozaru != Jandirus.Core.Forms.FormaOozaru.Nao) { Chave = "hab:oozaru" });

		// ============================ OS COLETORES DO ANDROIDE DE ABSORCAO ============================
		// PELA CLASSE DE ANDROIDE, e nao mais pela raca. Quem sabe se este corpo saiu do pod de absorcao
		// ou do de energia infinita e o servidor (`Fighter.AndroideAbsorcao`), e esse bit nao viajava:
		// o botao era de TODO androide, e o de ENERGIA INFINITA apertava pra ouvir *"seu corpo nao tem
		// coletores de energia"*. O bit viaja agora (`Fatos.AndroideDeAbsorcao`, no pacote dos fatos),
		// e o androide que nao tem coletores nao tem o botao.
		Verbos.Registrar(new Verbo(
			"Abrir/fechar coletores",
			Verbos.Skills,
			"Só do androide de ABSORÇÃO. Finca os pés e abre os coletores: enquanto estiverem "
			+ "abertos você NÃO ANDA, e todo ataque de ki que te acertar é engolido inteiro -- "
			+ "sem dano, virando energia (6% do seu tanque por golpe, acumulando até o seu teto "
			+ "de carga, o da aba Ki).\n\nAperte de novo pra fechar e voltar a se mover. Cair "
			+ "nocauteado fecha sozinho.",
			() => GameClient.Instance?.SendHabilidade("absorver_ki")) { Chave = "hab:absorver_ki" });

		// ============================ A ABSORCAO: UM VERB, DOIS CORPOS ============================
		// O `absorver` e UM id de proposito: no original os dois sao o verb `Absorb`, e quem decide entre
		// CONSUMIR (o bio-androide) e SELAR (o Majin) e a raca de quem aperta (`Absorption.dm:83`). Aqui
		// a raca escolhe so o TEXTO; quem tem o botao e o requisito de `hab:absorver` na tabela:
		//
		//   * o BIO-ANDROIDE pela raca, LARVA INCLUSIVE. A larva que aperta ouve que a carapaca nao tem os
		//     orgaos de absorcao e que amadurece enterrada -- e essa frase que ENSINA que a evolucao existe e
		//     por onde vem. (O meio-sangue de pai bio o ganha comprando "Tail Absorb".)
		//   * o MAJIN depois de comprar Buu Absorb na arvore dele (`Race Trees/majin.dm:38-40`): o servidor
		//     so sela pra quem tem a skill, e botao de skill que ainda nao se tem nao aparece -- a arvore,
		//     na aba Learning, e onde se descobre que ela existe.
		//
		// E O SERVIDOR PASSOU A CONFERIR QUEM APERTA: ate 2026-10-10 o galho do bio nao olhava a raca, e o
		// unico portao era este registro. Ver `GameServer.TemOrgaosDeAbsorcao`.
		// ==========================================================================================
		Verbos.Registrar(new Verbo(
			"Absorver",
			Verbos.Skills,
			raca == "Majin"
				? "Puxa pra dentro de você alguém NOCAUTEADO e vivo ao seu lado.\n\n"
				  + "Um JOGADOR não morre: ele vai VIVO pro interior do seu corpo, acorda inteiro e encara uma "
				  + "IMAGEM sua, com metade do seu poder. Enquanto ele estiver lá dentro você usa 10% do poder "
				  + "dele, as técnicas dele e veste as roupas dele.\n\n"
				  + "Ele ESCAPA se vencer a imagem, se morrer lá dentro, se você for nocauteado ou se você o "
				  + "expelir -- e você perde na hora o que tinha por causa dele.\n\n"
				  + "Um NPC é devorado por inteiro: morre, e rende os mesmos 10% até você cair."
				// (2026-10-17) A EXECUCAO E A ESCADA DE MARCOS -- o adendo do dono (`GameServer.Absorcao.cs`, `BioAndroids.Andar`).
				: "Uma EXECUÇÃO: você crava a cauda em alguém NOCAUTEADO e vivo ao seu lado e o bebe por 5 segundos, parado -- andar, "
				  + "cair ou ele se levantar desfaz. A vítima SECA, morre e some, sem cadáver.\n\n"
				  + "Você recupera 20% da vida, do fôlego e do Ki, e ganha o poder dela -- mas ele entra pela MÉDIA com o seu, e não "
				  + "pela soma: absorver serve pra compensar, não pra treinar.\n\n"
				  + "E é assim que você EVOLUI: cada jogador vale um marco, e um NPC um quinto; cinco marcos por forma, e o sexto sobe a "
				  + "forma -- semi-perfeito, depois a PERFEITA. Um androide de energia infinita pula direto pra forma seguinte; o de "
				  + "absorção é máquina por dentro e não se bebe. A larva não tem os órgãos pra isso.\n\n"
				  // (2026-10-17) A ABSORCAO DE COMBATE (`GameServer.EstocadaDeAbsorcao`)
				  + "AGARRANDO alguém, o absorver vira uma ESTOCADA: a cauda fura um membro por vez -- braço esquerdo, braço direito, perna "
				  + "esquerda, perna direita, cabeça e por fim o tronco -- e devolve 5% do seu tronco, do Ki e do fôlego. Uma por segundo.",
			() => GameClient.Instance?.SendHabilidade("absorver")) { Chave = "hab:absorver" });

		// (2026-10-24) O BIO-ANDROIDE COM SANGUE MAJIN TEM AS DUAS -- o dono: *"Os dois, em botoes separados"*. O "Absorver" de cima e o
		// dele (bebe e evolui); este sela como o Majin, pelas mesmas portas (`GameServer.BioMajin.cs`). Quem tem o botao: o bio que sabe Buu
		// Absorb, que a arvore do Majin so vende pro sangue Majin -- a linha `hab:absorver_majin` da tabela.
		Verbos.Registrar(new Verbo(
			"Absorver como Majin",
			Verbos.Skills,
			"O sangue Majin te dá o selo do Majin: puxa pra dentro de você alguém NOCAUTEADO e vivo ao seu lado, em vez de bebê-lo.\n\n"
			+ "Um JOGADOR não morre: vai VIVO pro interior do seu corpo e encara uma IMAGEM sua, com metade do seu poder. Enquanto ele "
			+ "estiver lá dentro você usa 10% do poder dele, as técnicas dele e veste as roupas dele. Um NPC é absorvido por inteiro.\n\n"
			+ "Selar NÃO faz você evoluir: os marcos da sua forma só andam pelo Absorver.",
			() => GameClient.Instance?.SendHabilidade("absorver_majin")) { Chave = "hab:absorver_majin" });

		Verbos.Registrar(new Verbo(
			"Expelir todos",
			Verbos.Skills,
			"Cospe de volta todo mundo que está dentro de você, ao seu lado e de pé. Você perde o poder, "
			+ "as técnicas e o que mais tinha por causa deles.",
			() => GameClient.Instance?.SendHabilidade("expelir"),
			() => GameClient.Instance is { AbsorvidosDoMajin.Count: > 0 }) { Chave = "hab:expelir" });

		// (2026-10-17) A LARVA DO BIO-ANDROIDE (o adendo do dono) -- a forma de barata que o bio de LABORATORIO veste quando quer, e o enterrar,
		// que e so dela. Quem tem os botoes e a tabela (`hab:larva`, `hab:enterrar`); quem decide e o servidor (`GameServer.Larva.cs`).
		Verbos.Registrar(new Verbo(
			"Forma de larva",
			Verbos.Skills,
			"Você se recolhe na carapaça de LARVA, por cima do seu degrau: o poder fica selado (5 -- sempre relaxada), nem o sense nem "
			+ "o scouter te acham, você voa sem esforço e se enterra pra se esconder. Não absorve nem se transforma.\n\n"
			+ "Pra sair, enterre-se e fique 5 minutos debaixo da terra sem sair: a carapaça se rompe e você volta ao degrau em que estava.",
			() => GameClient.Instance?.SendHabilidade("larva")) { Chave = "hab:larva" });

		Verbos.Registrar(new Verbo(
			"Enterrar-se / Sair da terra",
			Verbos.Skills,
			"Só a larva: você cava e some na terra -- ninguém te vê nem te alcança, e você não anda. Cinco minutos sem sair e a "
			+ "carapaça amadurece. Sair antes zera a conta.",
			() => GameClient.Instance?.SendHabilidade("enterrar")) { Chave = "hab:enterrar" });

		// (2026-10-17) O NUCLEO DO BIO-ANDROIDE (o adendo do dono): onde ele mora, escolhido UMA vez -- um botao por lugar, e os cinco somem com a
		// escolha (`Fatos.NucleoPorEscolher`). Quem decide e o servidor (`GameServer.Nucleo.cs`).
		foreach ((string lugar, string rotulo) in new[]
			{ ("Braco esquerdo", "braço esquerdo"), ("Braco direito", "braço direito"), ("Perna esquerda", "perna esquerda"),
			  ("Perna direita", "perna direita"), ("Cabeca", "cabeça") })
			Verbos.Registrar(new Verbo(
				$"Núcleo: {rotulo}",
				Verbos.Skills,
				"Escolha UMA vez, pra sempre, onde mora o seu núcleo. Enquanto esse membro estiver inteiro (nem quebrado nem decepado), o núcleo te "
				+ "reergue da morte com 5% de tudo -- duas vezes; na segunda ele detona e destrói o membro.",
				() => GameClient.Instance?.SendHabilidade("nucleo:" + lugar)) { Chave = "hab:nucleo" });

		DoDna();

		DosAbsorvidosDoMajin();

		// SO O NAMEKUSEIJIN. Era `"Namekian" or "Majin" or "BioAndroid" or "Shapeshifter"`, e as tres
		// racas a mais nao vinham de desenho: vinham de reusar a lista do `canheallopped`, que responde
		// outra pergunta (ver `GameServer.Raciais.PodeRegenerar`). O servidor deixou de aceitar delas, e
		// um botao que existe pra ouvir "seu corpo nao regenera assim" e pior que botao nenhum.
		//
		// Quem COMPRA a skill `Regenerate` (arvores de Alien/Android/Demonio) continua com o botao pelo
		// outro caminho: o registro por skill do `DasSkills()`, que le o livro e nao a raca.
		//
		// ESTE `if` FICA, E NAO E PRA ESCONDER: quem esconde e a tabela (`hab:regenerar`). Ele existe por
		// causa do NOME. O botao da skill comprada tambem se chama "Regenerar", e o `Verbos.Registrar`
		// deduplica por nome -- registrado pra todo mundo, este aqui chegaria primeiro, tomaria o nome e
		// ficaria invisivel pra quem nao e de Namek: o Alien que comprou a skill perderia o botao dele.
		if (raca is "Namekian")
			Verbos.Registrar(new Verbo(
				"Regenerar",
				Verbos.Skills,
				"Faz um membro decepado voltar a crescer, ou cura por inteiro o mais ferido. "
				+ "Custa 70% do Ki MAXIMO e tem 10s de espera -- e a unica cura que voce tem no "
				+ "meio de uma luta: seu corpo nao se refaz sozinho enquanto a briga estiver de pe.",
				() => GameClient.Instance?.SendHabilidade("regenerar"),
				// APAGA quando falta Ki, mas continua na lista: saber que a tecnica existe e que
				// falta Ki e informacao util; sumir com ela e esconder o jogo.
				() => GameClient.Instance is { } c && c.Sheet.MaxKi > 0 && c.Sheet.Ki >= c.Sheet.MaxKi * 0.7)
				{ Chave = "hab:regenerar" });

		DoDiscipulado();
		DaFusao();
		DosFilhos();
		DoNeoTuffle();
		DasRaciais();
		DoEnsinoDeSkill();
		DoEstudoDaMente();
		HabilidadesDoDeus.Registrar();
		HabilidadesDoJuiz.Registrar();   // (2026-10-19) a travessia do King Yemma -- ver `HabilidadesDoJuiz.cs`
		HabilidadesDoRitual.Registrar();   // (2026-10-23) o Ritual Mistico do Velho Kaiohshin -- ver `HabilidadesDoRitual.cs`
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
	/// UM BOTAO POR ABSORVIDO -- o "Um" do `Absorb_Expel()` (`Absorption.dm:182-190`), que la abre uma lista
	/// pra escolher. A lista e do servidor (`S2C.AbsorvidosDoMajin`), no mesmo desenho do
	/// <see cref="DosChefesDaMente"/>: o canal de habilidade leva um id de texto e o menu e a propria lista.
	/// </summary>
	private static void DosAbsorvidosDoMajin()
	{
		if (GameClient.Instance is not { } cli) return;

		foreach (GameClient.AbsorvidoDoMajin a in cli.AbsorvidosDoMajin)
		{
			int numero = a.Numero;
			Verbos.Registrar(new Verbo(
				$"Expelir: {a.Nome}",
				Verbos.Skills,
				a.Devorado
					? $"{a.Nome} foi devorado: não há quem devolver. Expelir só abre mão do poder que ele te dava."
					: $"Cospe {a.Nome} de volta, ao seu lado e de pé. Você perde o poder e as técnicas que "
					  + "tinha por causa dele.",
				() => GameClient.Instance?.SendHabilidade($"expelir:{numero}")) { Chave = $"hab:expelir:{numero}" });
		}
	}

	/// <summary>
	/// MESTRE E ALUNO -- os verbos da aba "Learning", que e onde eles moram no original
	/// (`set category = "Learning"`, `MasterStudent.dm:423` e `:477`).
	///
	/// ============================ SO O CONVITE E DE TODO MUNDO; O RESTO E DE QUEM TEM ============================
	/// As perguntas que decidem ("tenho mestre?", "tenho aluno?", "ha convite pendente?") sao estado do
	/// servidor, e este cabecalho dizia: *"hoje nada disso viaja pro cliente -- o dia em que viajar, e o dia
	/// de acender/apagar"*. Viajou (`S2C.Fatos`, 2026-10-10), e a tabela de requisitos faz o que ele pedia:
	/// "Aceitar"/"Recusar" so existem com um convite (ou um ensinamento) na mesa, "Ajudar a transformar" e
	/// "Dispensar aluno" so pra quem guia alguem, "Deixar meu mestre" so pra quem tem mestre.
	///
	/// "CONVIDAR ALUNO" CONTINUA DE TODO MUNDO, e a recusa dele continua ensinando: cada uma tem mensagem
	/// propria em `GameServer.Mestre.PorQueNaoVincula` -- inclusive "voce precisa ser 3x mais forte", que e
	/// a unica maneira de o jogador descobrir o portao.
	/// ======================================================================================================
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
	/// (2026-10-17) OS FILHOS -- o `Have_Child` do "Other" do DM (`Mating.dm:139`). "Ter um filho" so aparece pra quem tem um amor
	/// correspondido no jogo; "Aceitar o filho" e "Recusar o filho", pra quem foi pedido (o cartao do pedido mostra os dois tambem). E o
	/// `Lay_Egg` (`Mating.dm:222`): "Pôr um ovo", das racas de ovo ja crescidas -- elas tem filho sozinhas. Quem decide e a tabela de
	/// requisitos (`RequisitosDosVerbos`), pelos fatos. Ver `GameServer.Filhos.cs`.
	/// </summary>
	/// <summary>
	/// (v0.4.0) AS ATIVAS RACIAIS -- vem com a raca (dono, 2026-10-17). A aba Skills so as mostra pra quem tem o sangue
	/// (`RequisitosDosVerbos`, pelo kit); o servidor decide o resto (recarga, forma, combate). Ver `GameServer.Passivas.cs`.
	/// </summary>
	/// <summary>
	/// (2026-10-17) AS SKILLS DO DNA DO BIO-ANDROIDE -- o adendo do dono: *"o bio androide pode escolher dentre as raças de seu dna quais skills
	/// (passivas e ativas) ele quer, com limite de 6 skills"*.
	///
	/// Os vinte botoes da ESCOLHA, um por skill dos cinco kits de raca: a tabela so mostra os que o sangue dele da e que ele ainda nao tem,
	/// enquanto houver vaga (`hab:dna:&lt;bit&gt;`). E as PASSIVAS ja escolhidas viram texto, sem acao -- passiva que ninguem sabe que ganhou e
	/// indistinguivel de passiva quebrada; as ATIVAS escolhidas ja tem o botao delas (<see cref="DasRaciais"/>). Quem decide e o servidor
	/// (`GameServer.SkillsDoDna.cs`).
	/// </summary>
	private static void DoDna()
	{
		foreach (Jandirus.Core.Races.SkillDeRaca s in Jandirus.Core.Races.SkillsRaciais.Todas)
		{
			string id = s.IdDaEscolha;
			Verbos.Registrar(new Verbo(
				$"DNA: {s.Nome}",
				Verbos.Skills,
				$"{DescricaoDaSkillDeRaca(s.Bit)}\n\nUma escolha do seu DNA: vale pra sempre, e são seis no máximo.",
				() => GameClient.Instance?.SendHabilidade(id)) { Chave = "hab:" + id });
		}

		if (GameClient.Instance is not { } cli || !Jandirus.Core.Races.BioAndroids.EhBio(cli.Atributos.Raca)) return;
		var minhas = (Jandirus.Core.Races.SkillRacial)cli.Atributos.SkillsRaciais;
		foreach (Jandirus.Core.Races.SkillDeRaca s in Jandirus.Core.Races.SkillsRaciais.Todas)
			if (!s.EhAtiva && (minhas & s.Bit) != 0)
				Verbos.Registrar(new Verbo($"{s.Nome}  (do seu DNA)", Verbos.Skills,
					$"{DescricaoDaSkillDeRaca(s.Bit)}\n\nUma das skills que você escolheu do seu sangue. Passiva: vale sozinha.", null)
					{ Chave = $"passiva:dna:{s.Bit}" });
	}

	/// <summary>(2026-10-17) O que cada skill de kit de raca faz -- o botao da escolha do DNA e a linha da passiva escolhida.</summary>
	private static string DescricaoDaSkillDeRaca(Jandirus.Core.Races.SkillRacial s) => s switch
	{
		Jandirus.Core.Races.SkillRacial.GuerreiroVeterano =>
			"Saiyajin: em combate, cada forma de maestria que você sustenta (10 s a primeira, 20 s a segunda, 30 s a terceira...) vale +10% de "
			+ "BP, até +60%. Descer pra uma forma mais fraca zera a conta.",
		Jandirus.Core.Races.SkillRacial.RacaGuerreira => "Saiyajin: com o tronco ou a cabeça abaixo de 50%, ataque físico +20% e defesa física +30%.",
		Jandirus.Core.Races.SkillRacial.Resolucao =>
			"Saiyajin: do 2º golpe seguido do MESMO tipo em diante (soco, lâmina ou ki), cada um dói 2% menos, até -20%, por 10 s.",
		Jandirus.Core.Races.SkillRacial.Orgulho =>
			"Saiyajin: em combate, com o poder abaixo de 75%, 50% ou 25% da barra, a regen de Ki e de fôlego e o ataque e a defesa físicos "
			+ "sobem -- e o Ki Skill cai.",
		Jandirus.Core.Races.SkillRacial.FalsaVantagem =>
			"Saiyajin: com Ki, fôlego ou vida abaixo da metade, a forma mortal (a base inclusive) SOMA no multiplicador -- +0,5x, ou +1x, +2x, "
			+ "+4x pela maior forma dominada -- e todo dreno cai pela metade.",
		Jandirus.Core.Races.SkillRacial.Prodigio =>
			"Meio-Saiyajin: forma mortal nova já começa com 50% de maestria; dominada, ela soma +0,5x (+2x nas divinas) e a regen ativa de Ki "
			+ "sobe 25%.",
		Jandirus.Core.Races.SkillRacial.Potencial => "Meio-Saiyajin: o teto da carga de Ki vai a 225% em vez de 200%.",
		Jandirus.Core.Races.SkillRacial.Talento =>
			"Meio-Saiyajin: técnica de ki dominada rende +10%, e a mesma usada duas vezes seguidas rende o dobro na segunda.",
		Jandirus.Core.Races.SkillRacial.Espirito =>
			"Meio-Saiyajin: quando um membro cai abaixo da metade, você apanha 20% menos por 1 min e fica sem efeitos negativos por 10 s.",
		Jandirus.Core.Races.SkillRacial.Persistencia => "Humano: o treino seguido rende mais -- +1x a cada 10 min sem parar, até 3x.",
		Jandirus.Core.Races.SkillRacial.SegundoFolego =>
			"Humano (ativa): 20 s recuperando Ki e fôlego (metade de cada, no total). Recarga: 2 min.",
		Jandirus.Core.Races.SkillRacial.ForcaDeVontade =>
			"Humano (ativa): por 15 s, sem efeitos negativos e sem morrer -- o golpe fatal te deixa de pé por um fio. Recarga: 10 min.",
		Jandirus.Core.Races.SkillRacial.PoderMaximo =>
			"Humano (ativa, liga e desliga): ataque físico, defesa física e velocidade 1,5x, somando com qualquer forma; gasta Ki.",
		Jandirus.Core.Races.SkillRacial.Resiliencia => "Namekuseijin: você se levanta do nocaute na metade do tempo.",
		Jandirus.Core.Races.SkillRacial.Disrupcao =>
			"Namekuseijin (ativa): cada golpe limpo tira 10% do Ki máximo de quem leva, até 40%, por 30 s. Recarga: 3 min.",
		Jandirus.Core.Races.SkillRacial.Vitalidade =>
			"Namekuseijin: membro ferido ou perdido pesa a metade; com todos inteiros, a regen passiva de Ki e de fôlego sobe 25%.",
		Jandirus.Core.Races.SkillRacial.Controle => "Frost Demon: a carga das técnicas de ki é 2x mais rápida, e o dano de ki sobe 15%.",
		Jandirus.Core.Races.SkillRacial.Tenacidade =>
			"Frost Demon: o nocaute na metade do tempo, e cada membro com pouca vida apanha menos (até -25% abaixo de 10%).",
		Jandirus.Core.Races.SkillRacial.Ira =>
			"Frost Demon (ativa): um kiai joga quem está perto pra longe, e a ira toma conta -- BP +20%, Ki máximo -20%. Recarga: 3 min.",
		Jandirus.Core.Races.SkillRacial.Metamorfose =>
			"Frost Demon: transformar com o tronco ferido cura os membros (+20%), devolve os decepados (com 10%) e acelera a carga de Ki por "
			+ "1 min -- no bio-androide, ao entrar na Super Perfeita. Recarga: 3 min.",
		_ => "",
	};

	private static void DasRaciais()
	{
		void Ativa(string id, string nome, string desc) =>
			Verbos.Registrar(new Verbo(nome, Verbos.Skills, desc, () => GameClient.Instance?.SendHabilidade(id)) { Chave = "hab:" + id });

		Ativa(Jandirus.Core.Races.Passivas.Ferocidade, "Unmatched Ferocity",
			"Primal: por 1 min nada te segura -- sem efeitos negativos, com o poder inteiro e sem o dreno da forma (+30/60/90 s no "
			+ "SSJ4, no Full Power e no Limit Breaker). Recarga: 6 min.");
		Ativa(Jandirus.Core.Races.Passivas.Transbordar, "Overflowing Power",
			"Lendário: o BP sobe 5% a cada 10 s, até +30%, e o corpo paga no tronco -- cada vez mais depois de 30 s. Aperte de novo "
			+ "(depois de 30 s) pra DESABAFAR: o buff sai num GRITO de 3 s -- plantado dentro de uma esfera verde, com tiros de ki saindo "
			+ "pra todo lado em rumos e ritmos sorteados.");
		Ativa(Jandirus.Core.Races.Passivas.ForcaImparavel, "Unstoppable Force",
			"Lendário: 30 s sem recuo (nem arremesso nem atordoamento) e apanhando 10% menos. Recarga: 2 min.");
		Ativa(Jandirus.Core.Races.Passivas.TransbordarKi, "Overflowing Ki",
			"Lendário Primal: o Ki máximo sobe 10% a cada 10 s, até +100%, e o corpo paga no tronco -- cada vez mais depois de 60 s. "
			+ "Aperte de novo (depois de 60 s) pra DESABAFAR num GRITO de 3 s, com tiros de ki saindo pra todo lado.");
		Ativa(Jandirus.Core.Races.Passivas.SedeDeSangue, "Primal Bloodlust",
			"Lendário Primal: 15 golpes sem recuo (20/25/30 no SSJ4, no Full Power e no Limit Breaker), apanhando 5% mais. Cada golpe "
			+ "que você leva vira +2% de dano seu, por 2 min. Recarga: 10 min.");
		Ativa(Jandirus.Core.Races.Passivas.Demonico, "Demonic Saiyan",
			"Lendário Primal, numa forma Lendária e depois de 2 min de luta: fique PARADO 15 s dentro da esfera verde. Depois, a cada "
			+ "15 s, o seu Ki e o seu fôlego vão a 300% por 10 s, até a luta acabar. Enquanto dura, o Ki custa 10% mais e os "
			+ "membros feridos pesam 10% mais. Recarga: 20 min.");
		Ativa(Jandirus.Core.Races.Passivas.SegundoFolego, "Second Wind",
			"Humano: 20 s recuperando Ki e vigor (metade de cada, no total). Recarga: 2 min.");
		Ativa(Jandirus.Core.Races.Passivas.ForcaDeVontade, "Sheer Willpower",
			"Humano: por 15 s, sem efeitos negativos e sem morrer -- o golpe fatal te deixa de pé por um fio. Recarga: 10 min.");
		Ativa(Jandirus.Core.Races.Passivas.PoderMaximo, "Max Power",
			"Humano: liga e desliga. Ataque físico, defesa física e velocidade 1,5x, somando com qualquer forma; gasta Ki enquanto "
			+ "ligado e a cada soco.");
		Ativa(Jandirus.Core.Races.Passivas.Disrupcao, "Spirit Disruption",
			"Namekuseijin: os seus punhos e pernas carregam ki. Cada golpe limpo (sem guarda) tira 10% do Ki máximo de quem leva, até "
			+ "40%, por 30 s; quatro golpes bloqueados ou errados a desfazem. Recarga: 3 min.");
		Ativa(Jandirus.Core.Races.Passivas.Ira, "Unrestrained Wrath",
			"Frost Demon: um kiai joga quem está perto pra longe, e a ira toma conta -- BP +20%, Ki máximo -20%, sem prazo. Dá pra "
			+ "engolir de volta depois de 30 s (aí começa a recarga de 3 min).");
		Ativa(Jandirus.Core.Races.Passivas.MenteCruel, "Ruthless Mind",
			"Frost Demon mutante, com o golpe letal ligado: executa quem está caído na sua frente (3 s parado). Quem não é seu amigo, "
			+ "a 50 tiles, leva -10% em todos os stats por 2 min. Recarga: 5 min.");
		Ativa(Jandirus.Core.Races.Passivas.Sadismo, "Sadistic Nature",
			"Frost Demon mutante: por 1 min, cada golpe que entra sem guarda soma +5% de dano, até +100%. Recarga: 5 min.");
	}

	private static void DosFilhos()
	{
		Verbos.Registrar(new Verbo(
			"Ter um filho",
			Verbos.Outros,
			"Pede pra quem está na sua frente terem um filho. Os dois precisam ter declarado AMOR um pelo outro (aba People) e ser de "
			+ "sexos opostos. Mesma raça dá a mesma raça; Humano, Saiyajin e meio-Saiyajin se misturam num meio-Saiyajin (o androide "
			+ "de energia infinita conta como Humano). O bebê nasce como o próximo personagem que alguém criar.",
			() => GameClient.Instance?.SendHabilidade("filho_pedir")) { Chave = "hab:filho_pedir" });

		Verbos.Registrar(new Verbo(
			"Aceitar o filho",
			Verbos.Outros,
			"Aceita o pedido de filho de quem te ama.",
			() => GameClient.Instance?.SendHabilidade("filho_sim")) { Chave = "hab:filho_sim" });

		Verbos.Registrar(new Verbo(
			"Recusar o filho",
			Verbos.Outros,
			"Recusa o pedido de filho.",
			() => GameClient.Instance?.SendHabilidade("filho_nao")) { Chave = "hab:filho_nao" });

		Verbos.Registrar(new Verbo(
			"Pôr um ovo",
			Verbos.Outros,
			"As raças de ovo têm filho sozinhas: o ovo choca como o próximo personagem que alguém criar, da sua raça e da sua "
			+ "linhagem. Um ovo por vez, e só depois de crescer.",
			() => GameClient.Instance?.SendHabilidade("filho_ovo")) { Chave = "hab:filho_ovo" });
	}

	/// <summary>
	/// (2026-10-18) O NEO TUFFLE -- o verb de quem passou pela maquina do Neo Tuffle Lab (`GameServer.NeoTuffle.cs`). A linha
	/// `hab:tuffle_possuir` da tabela (`RequisitosDosVerbos`) so ESCONDE o botao de quem nao e Neo Tuffle: quem desenha o botao na aba Other
	/// e este registro, e sem ele o verb existia no servidor e nao tinha botao nenhum (a contraprova por mutacao de 2026-10-19 achou).
	/// </summary>
	private static void DoNeoTuffle()
	{
		Verbos.Registrar(new Verbo(
			"Possuir um nocauteado",
			Verbos.Outros,
			"Entra no corpo do jogador nocauteado mais perto e o dirige com o poder dele -- o mundo vê o dono do corpo, de cabelo "
			+ "branco-acinzentado, e ele só assiste. Quando o corpo cair, você é expulso e volta pro seu.",
			() => GameClient.Instance?.SendHabilidade("tuffle_possuir")) { Chave = "hab:tuffle_possuir" });
	}

	/// <summary>
	/// A FUSAO -- os verbos que o DM poe em `set category = "Skills"` (`Fusion.dm:712`) e em
	/// `"Other"` (o Pass Fusion Control, `:514`).
	///
	/// ============================ ELES FICAM NA MESMA ABA DO CONVITE DE MESTRE ============================
	/// "Aceitar" e "Recusar" da fusao dividem a aba com os do discipulado e os do ensino de skill, e
	/// e proposital: sao TRES pendentes diferentes que chegam pelo mesmo tipo de aviso, e o jogador
	/// que recebe um convite tem um lugar so pra procurar. Os ids sao distintos (`fus_sim` /
	/// `mst_aceitar` / `ens_licao_sim`) porque os tres pendentes coexistem -- alguem pode estar sendo
	/// convidado pra fundir enquanto um terceiro lhe oferece um Kamehameha.
	///
	/// **CADA UM E DE QUEM O TEM** (dono, 2026-10-10: *"verb de fusao namek"* na tela de um Saiyajin era
	/// poluicao). A Danca aparece pra quem a aprendeu, a Namekuseijin pra quem e de Namek, e os tres botoes
	/// de resposta so com o convite na mesa -- o estado que decide ("ha convite?", "estou no volante?")
	/// viaja agora nos fatos (`S2C.Fatos`). Os dois portoes de CIRCUNSTANCIA (mesma raca, poder proximo)
	/// continuam sendo frase de recusa: quem sabe a Danca e convida a pessoa errada ouve por que. Ver
	/// `GameServer.Fusao.PorQueNaoFunde`.
	///
	/// **A POTARA NAO TEM VERB**: ela e o ITEM. Clicar nos brincos na mochila e o gesto, e o menu de
	/// acoes do inventario ja monta o botao sozinho a partir do `ItemDef.AcoesDoItem`.
	///
	/// **A NAMEKUSEIJIN TEM**, e ela e a terceira: no DM ela e um verb tambem
	/// (`obj/Namekian_Fusion/verb/Namekian_Fusion` e `mob/keyable/verb`, `Fusion.dm:549-550`), sem
	/// item e sem skill.
	/// =================================================================================================
	/// </summary>
	private static void DaFusao()
	{
		Verbos.Registrar(new Verbo(
			"Dança da Fusão",
			Verbos.Aprendizado,
			"Convida quem está na sua frente pra Dança da Fusão. Vocês DOIS precisam saber a dança, "
			+ "ser da mesma raça (meio-Saiyajin conta como Saiyajin) e ter poder aparente parecido -- "
			+ $"o mais fraco tem que expressar pelo menos {Jandirus.Core.Social.Fusao.LimiarDeProximidade * 100:0}% "
			+ "do mais forte.\n\nSe ele aceitar, os dois fazem a coreografia: "
			+ $"{Jandirus.Core.Social.Fusao.LetrasDaDanca} letras cada um. Acertando tudo, a fusão sai "
			+ "inteira. Errando qualquer passo, ela sai fraca demais até pra se transformar.\n\n"
			+ "Quem convida CONTROLA o corpo, e a fusão dura "
			+ $"{Jandirus.Core.Social.Fusao.EnergiaDaDanca / 60:0} minutos parado -- menos quanto mais "
			+ "alta a transformação.",
			() => GameClient.Instance?.SendHabilidade("fus_danca")) { Chave = "hab:fus_danca" });

		// ============================ A FUSAO NAMEKUSEIJIN -- `Namekian_Fusion` (`Fusion.dm:549-569`) ============================
		// Ela existia no motor inteira e **nao tinha porta**: nenhum caminho de producao chamava
		// `Convidar` com ela. Este botao e a porta -- e ele e SO DE NAMEKUSEIJIN (o requisito de
		// `hab:fus_namek`): o servidor recusa qualquer outra raca, e um botao que nenhum Saiyajin jamais
		// vai poder usar nao ensina nada a ele.
		//
		// **O TEXTO MUDOU COM O PEDIDO DO DONO**, e a mudanca e a coisa mais importante deste bloco: ela
		// deixou de ser uma fusao e virou uma ABSORCAO -- *"o outro namek se for jogador, perde o
		// personagem pra sempre"*. Um botao que ainda dissesse "o controle pode ser passado depois"
		// estaria descrevendo um sistema que nao existe mais, e do lado errado da unica consequencia
		// deste jogo que nao tem desfazer.
		// ==================================================================================================================
		Verbos.Registrar(new Verbo(
			"Fusão Namekuseijin (absorver)",
			Verbos.Aprendizado,
			"Absorve quem está na sua frente. Os DOIS precisam ser Namekuseijin -- não há skill a "
			+ "aprender e não importa o poder de cada um.\n\n"
			+ "O poder dele vira SEU, de vez: você fica com o BP dos dois somado e dobrado, com os "
			+ "melhores atributos dos dois, com as habilidades dele -- e o SUPER NAMEKUSEIJIN desperta "
			+ "em você.\n\n"
			+ "E O PERSONAGEM DELE DEIXA DE EXISTIR, para sempre. Não há separação, não há volta e "
			+ "nem um administrador desfaz. Ele precisa aceitar, e vai ter que confirmar duas vezes."
			+ "\n\nAbsorver um Namekuseijin do MUNDO (um NPC) também funciona, mas rende quase "
			+ "nada: sem atributos, sem habilidades e sem a transformação.",
			() => GameClient.Instance?.SendHabilidade("fus_namek")) { Chave = "hab:fus_namek" });

		Verbos.Registrar(new Verbo(
			"Aceitar a fusão",
			Verbos.Aprendizado,
			"Aceita o convite de fusão que está na sua mesa. Você vira o passageiro: quem convidou "
			+ "dirige o corpo, e você volta quando a fusão acabar.\n\n"
			+ "NÃO serve pra fusão Namekuseijin -- aquela tem botão próprio, porque ela custa o seu "
			+ "personagem.",
			() => GameClient.Instance?.SendHabilidade("fus_sim")) { Chave = "hab:fus_sim" });

		// ============================ O SIM DA ABSORCAO E UM BOTAO SEPARADO, E O NOME DIZ O PRECO ============================
		// Regra N3 do dono. O botao de cima promete *"voce volta quando a fusao acabar"* -- e numa
		// Namekuseijin nao ha volta. Reusa-lo seria colher consentimento sobre a consequencia errada, e
		// a memoria muscular de quem ja aceitou uma Danca apertaria o mesmo lugar sem ler nada.
		//
		// O SERVIDOR PEDE DUAS VEZES (ver `GameServer.ResponderAoConviteDeAbsorcao`): a primeira imprime
		// o nome, a raca, a idade e o poder do personagem que morre; a segunda so vale alguns segundos
		// depois. O NOME deste verb ja carrega o preco porque a lista do menu P e o que muita gente le
		// em vez da descricao.
		// ============================================================================================================
		Verbos.Registrar(new Verbo(
			"Aceitar a ABSORÇÃO (perco o personagem)",
			Verbos.Aprendizado,
			"Aceita o convite de fusão NAMEKUSEIJIN. Isto não é a fusão comum: o seu personagem é "
			+ "APAGADO para sempre, e o poder dele passa pra quem convidou.\n\n"
			+ "Você não vira passageiro, não volta depois e não pode ser revivido. Nem um "
			+ "administrador desfaz.\n\n"
			+ "A sua CONTA continua sua: você poderá criar outro personagem no lugar deste.\n\n"
			+ "O servidor vai pedir confirmação DUAS vezes, com alguns segundos entre elas.",
			() => GameClient.Instance?.SendHabilidade("fus_namek_sim")) { Chave = "hab:fus_namek_sim" });

		Verbos.Registrar(new Verbo(
			"Recusar a fusão",
			Verbos.Aprendizado,
			"Recusa o convite de fusão pendente. (Ignorar também funciona: todo convite expira em "
			+ $"{Jandirus.Core.Social.Fusao.PrazoDoConviteSegundos:0} segundos.)",
			() => GameClient.Instance?.SendHabilidade("fus_nao")) { Chave = "hab:fus_nao" });

		// A FUSAO NAMEKUSEIJIN NAO TEM CONTROLE A PASSAR, e o texto diz isso: nela nao ha dois lados --
		// ha um que absorveu e um que deixou de existir.
		Verbos.Registrar(new Verbo(
			"Passar o controle",
			Verbos.Aprendizado,
			"Entrega o volante do corpo fundido ao seu outro lado. O poder da fusão não muda -- só "
			+ "muda quem está dirigindo. Só quem está no controle pode usar.\n\n"
			+ "(Na fusão Namekuseijin não existe: lá não sobrou ninguém pra receber o volante.)",
			() => GameClient.Instance?.SendHabilidade("fus_passar")) { Chave = "hab:fus_passar" });
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
	///
	/// ============================ O QUE FICA NO CARGO NAO VIRA BOTAO (dono, 2026-10-23) ============================
	/// Do kit de um cargo so os ataques de ki se ensinam (`EnsinoDeSkill.CargoTrancaOEnsino`). Aqui o caso e o
	/// contrario do de cima: isto o cliente SABE, porque esta no catalogo -- e um "Ensinar: Give Permission" que
	/// recusa pra todo Guardiao, sempre, e o botao poluido que o dono mandou limpar em 2026-10-10. O "Esquecer a
	/// licao" da mesma skill fica: ele e de quem a aprendeu, e nao de quem a ensinaria.
	///
	/// O POTENTIAL UNLEASHED TAMBEM NAO (dono, 2026-10-23: so o ritual o da -- `EnsinoDeSkill.SoPeloRitual`), pelo mesmo motivo: o
	/// cliente sabe pelo catalogo, e o botao recusaria pra todo mundo.
	/// ===============================================================================================================
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
			+ "ela exige -- mas aprender nao gasta marco nenhum. (A excecao e a LICAO DE MESTRE: o que o seu "
			+ "mestre nao ensinaria a um estranho gasta os marcos, e a oferta avisa.)",
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

			if (!Jandirus.Core.Skills.EnsinoDeSkill.CargoTrancaOEnsino(s)
				&& !Jandirus.Core.Skills.EnsinoDeSkill.SoPeloRitual(s))
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
				+ "com os proprios marcos nao se esquece por aqui. Nenhum marco volta (nem os da licao "
				+ "de mestre, que custou).",
				() => GameClient.Instance?.SendHabilidade($"ens_esquecer:{idLocal}"))
				{ Chave = $"hab:ens_esquecer:{idLocal}" });
		}
	}

	/// <summary>
	/// O SISTEMA DE ESTUDO DA MENTE -- os dois verbs que ESCOLHEM uma habilidade (lote G13).
	///
	/// ============================ UM BOTAO POR OPCAO, COMO O ENSINO LOGO ACIMA ============================
	/// `Focus_Skill` e `Write_Teachings` abrem um `input()` no DM (`KiStatsModule.dm:88`, `:151-154`)
	/// com as SUAS habilidades da Mente. Aqui nao ha caixa modal e o painel de verbs so tem botao --
	/// entao a lista vira botoes, exatamente como o `DoEnsinoDeSkill` faz com "Ensinar: X" e como o
	/// `DosChefesDaMente` faz com a lembranca de cada chefe.
	///
	/// **SEM ISTO OS DOIS SERIAM PORTADOS E INALCANCAVEIS**: o aperto nu deles LISTA (e util pra
	/// conferir), mas nada na tela monta a string `Focus_Skill:<nome>`. O terceiro verb do lote
	/// (`Study_Other`) nao precisa de botao por opcao -- o alvo dele e o do DUPLO CLIQUE, e o
	/// servidor le o marcado no aperto nu.
	/// ==================================================================================================
	///
	/// A LISTA E DAS `/datum/skill/mind/*` porque e o laco do DM
	/// (`for(var/datum/skill/mind/S in usr.learned_skills)`): sao as 17 da arvore da Mente mais as 33
	/// pericias de Ki, que sao da mesma classe la.
	/// </summary>
	public static void DoEstudoDaMente()
	{
		SkillCatalog? cat = MenuJogo.CatalogoPublico();
		GameClient? cli = GameClient.Instance;
		if (cat == null || cli == null) return;

		// UM BOTAO POR SKILL DA MENTE, mas so pra quem TEM o verb: o `Focus_Skill` nasce no nivel 10 da
		// Basic Ki Awareness e o `Write_Teachings` vem do Ki Unlocked -- e o servidor recusa quem nao os tem
		// ("voce nao sabe Focar Habilidade"). Quem tinha uma skill da Mente e nenhum dos dois via uma
		// fileira de botoes que so sabia recusar. As duas linhas da tabela valem pra familia inteira.
		Jandirus.Core.Verbos.Requisito foco = Jandirus.Core.Verbos.RequisitosDosVerbos.De("hab:Focus_Skill");
		Jandirus.Core.Verbos.Requisito escrita = Jandirus.Core.Verbos.RequisitosDosVerbos.De("hab:Write_Teachings");

		bool alguma = false;
		foreach (string path in cli.SkillsAprendidas.OrderBy(p => p, StringComparer.Ordinal))
		{
			if (!path.StartsWith("/datum/skill/mind/", StringComparison.OrdinalIgnoreCase)) continue;
			if (cat.Get(path) is not { Arvore: false, Nome.Length: > 0 } s) continue;
			alguma = true;
			string nome = s.Nome;

			Verbos.Registrar(new Verbo(
				$"Foco do estudo: {nome}",
				Verbos.Aprendizado,
				$"Escolhe {nome} como o foco do seu ESTUDO. Sozinho isto nao muda nada: o foco so vale enquanto voce "
				+ "ESTUDA alguem mais adiantado que voce nela (marque a pessoa e use Estudar) -- ai so ela adianta, "
				+ "e adianta DEZ VEZES mais do que o estudo espalhado.",
				() => GameClient.Instance?.SendHabilidade($"Focus_Skill:{nome}"))
				{ Chave = $"hab:Focus_Skill:{nome}", Requisito = foco });

			Verbos.Registrar(new Verbo(
				$"Escrever sobre: {nome}",
				Verbos.Aprendizado,
				$"Escreve um livro sobre {nome}. So se escreve MEDITANDO, e leva um minuto por nivel "
				+ "que voce tem nela. O livro fica na mochila e ensina quem ja saiba a habilidade e "
				+ "esteja na METADE do seu nivel ou abaixo.",
				() => GameClient.Instance?.SendHabilidade($"Write_Teachings:{nome}"))
				{ Chave = $"hab:Write_Teachings:{nome}", Requisito = escrita });
		}

		if (!alguma) return;

		Verbos.Registrar(new Verbo(
			"Foco do estudo: nenhum",
			Verbos.Aprendizado,
			"Solta o foco: o estudo volta a adiantar um pouco de TUDO que a pessoa estudada sabe mais "
			+ "que voce, em vez de dez vezes numa so.",
			() => GameClient.Instance?.SendHabilidade("Focus_Skill:nenhuma")) { Chave = "hab:Focus_Skill:nenhuma", Requisito = foco });

		Verbos.Registrar(new Verbo(
			"Parar de escrever",
			Verbos.Aprendizado,
			"Desiste do livro em obra. **Voce perde tudo que ja tinha escrito** -- e assim no original.",
			() => GameClient.Instance?.SendHabilidade("Write_Teachings:parar")) { Chave = "hab:Write_Teachings:parar", Requisito = escrita });
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
			// COMO SE SOLTA, escrito no botao: sem o "sai sozinho" a carga fecha e fica na mao ate o segundo aperto
			// (dono, 2026-10-12), e quem nao leu a mesa nao tem outro lugar pra descobrir.
			if (t.Tipo == Jandirus.Core.Combat.TipoDeProjetil.Beam)
				ficha += $", carga {t.CargaMinima:0.#}s"
					   + (t.Instantaneo ? " (sai sozinho)" : " (aperte de novo para soltar; e mais uma vez para parar)");
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
	/// DEFEITO INJETADO (o robo dos verbs, `RoboDeVerbs` F7): o verb sem corpo volta a virar o botao cinza
	/// "(nao portada)" -- a fileira de botoes mortos da v0.2.0. Lido pelo <see cref="DasSkills"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool BotaoCinzaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (o robo do Kai Kai, `RoboDoKaiKai` K0): a categoria "Outros" do catalogo volta a cair FORA de
	/// toda aba -- o botao do Kai Kai, do Devil Bringer, da Telepatia... so na busca, e fora da tela de teclas. Lido pelo
	/// <see cref="AbaDoBotao"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool OutrosForaDaAbaDeTeste;

	/// <summary>
	/// A ABA DO BOTAO DE UMA TECNICA.
	///
	/// (2026-10-24, dono: "Mover pra aba Other") O catalogo (`Tecnicas.Portadas`) escreve a categoria em portugues,
	/// "Outros", e a aba do menu P se chama "Other" (<see cref="Verbos.Outros"/>, o nome do original). Os ~20 verbos dela
	/// (Kai Kai, Devil Bringer, Telepatia, Rasgo Dimensional, Mafuba, Observar...) nao caiam em aba NENHUMA: so a busca os
	/// achava, e a tela de teclas, que lista as abas, nao os oferecia pra ligar. Agora vao pra Other (o cartao "Diversos",
	/// ate alguem os pendurar num tema da `MenuJogo.Outros`).
	///
	/// MENOS O DECOLAR (`Space_Flight`): ele SAIU da aba Other em 2026-09-03, pedido do dono (o comentario "O VERB
	/// 'Decolar' SAIU DA ABA OTHER", no comeco deste arquivo) -- subir ao espaco e voar pra cima ate romper a atmosfera.
	/// </summary>
	internal static string AbaDoBotao(Tecnicas.Tecnica t) =>
		t.Aba == "Outros" && t.Id != "Space_Flight" && !OutrosForaDaAbaDeTeste ? Verbos.Outros : t.Aba;

	/// <summary>
	/// OS BOTOES QUE VIERAM DAS SKILLS APRENDIDAS.
	///
	/// Nenhuma linha aqui conhece tecnica nenhuma: le o que foi aprendido, pergunta ao catalogo
	/// quais verbs aquilo destrava e registra um botao por verb. Portar a proxima tecnica nao
	/// mexe neste arquivo -- so no `UsarTecnica` do servidor e na tabela do <see cref="Tecnicas"/>.
	///
	/// (2026-10-13) A TECNICA SEM CORPO NAO VIRA MAIS BOTAO. Ela virava um botao cinza escrito "(nao portada)", pra
	/// quem a comprou nao achar que perdeu os marcos -- e o dono viu a fileira deles: *"tem mts verbs mortos q
	/// poderiam ser tirados, e tem verb q diz q faz algo mas n faz nada"*. Sairam. O que protege os marcos agora e a
	/// LOJA: a skill que so promete um verb sem corpo nao se vende (`SkillsFechadas`), e a que da outra coisa junto
	/// diz na ficha qual das habilidades dela ainda nao existe.
	/// </summary>
	public static void DasSkills()
	{
		SkillCatalog? cat = MenuJogo.CatalogoPublico();
		GameClient? cli = GameClient.Instance;
		if (cat == null || cli == null) return;

		var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		void Botao(string id)
		{
			if (!vistos.Add(id)) return;
			Tecnicas.Tecnica? t = Tecnicas.Get(id);
			if (t == null) return;

			// SO O VERB QUE TEM CORPO AQUI. O atendido por outro canal tem corpo, mas o botao (ou a tecla) dele nasce
			// de outro lugar -- a absorcao e o `Absorver` da raca, o voo e uma tecla, a mesa de tecnicas mora na aba
			// de Ki; o que nao tem corpo nenhum nao e botao (ver o cabecalho).
			if (t.Modo == Modo.NaoPortada && !BotaoCinzaDeTeste) return;

			string idLocal = id;
			Verbos.Registrar(new Verbo(
				t.Modo == Modo.NaoPortada ? $"{t.Nome} (nao portada)" : t.Nome,
				AbaDoBotao(t),
				t.Desc,
				// (o `Power_Control` nao viaja sem numero: o botao dele ABRE a faixa da aba Ki, onde o numero se escolhe)
				() => { if (!MenuJogo.AbrirOControleDe(idLocal)) GameClient.Instance?.SendHabilidade(idLocal); },
				t.Modo == Modo.NaoPortada ? () => false : null) { Chave = $"hab:{idLocal}" });
		}

		foreach (string path in cli.SkillsAprendidas)
		{
			Skill? s = cat.Get(path);
			if (s == null) continue;
			foreach (string id in s.Verbos) Botao(id);
		}

		// ============================ OS VERBS QUE O DEGRAU (E A CASA) CONCEDEM ============================
		// Ate aqui o botao so nascia do CATALOGO. Os verbs concedidos por NIVEL (o Hokuto Hyakuretsu Ken no
		// nivel 2 do Hokuto no Shinken; 60 dos 189 do jogo) e os por CASA (a Trindade) nao estao em skill
		// nenhuma -- estao no `niveis.json` cruzado com o NIVEL ATUAL, que so o servidor tem. Ele manda o
		// RESULTADO (`TecnicasDe`, a mesma lista que o `SabeTecnica` aceita) no `S2C.Skills`, e e daqui que
		// o botao deles nasce. Sem esta metade, 16 golpes do lote G10 e 13 do G7 tinham corpo, porta e
		// NENHUM botao -- alcancaveis so por tecla ligada a mao. Ver `Protocol.PorEstadoDeSkills`.
		// ================================================================================================
		foreach (string id in cli.VerbosAtivos) Botao(id);
	}
}
