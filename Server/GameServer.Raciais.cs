using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// O QUE CADA POVO TEM DE PROPRIO -- a parte ATIVA e as ligacoes que faltavam.
///
/// O ZENKAI EM SI JA ESTAVA PORTADO em <see cref="Fighter.GainZenkai"/> (etapa de combate), com
/// teto, recarga de uma hora, bonus de "em frangalhos", aposentadoria por BP e a comparacao por
/// PODER EFETIVO nos dois lados. O que faltava era ele disparar no NOCAUTE tambem -- ver
/// <see cref="ZenkaiPorDerrota"/>.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// (2026-10-24) O BP BASE QUE O SSJ3 PEDE A ESTE CORPO -- a porta do limiar pessoal (`LimiaresPessoais.Porta`, a mesma que o
	/// portao da forma cobra), ou a de fabrica (`Catalogo.PortaSsj3`) pro corpo sem limiar sorteado, o de NPC.
	/// </summary>
	private static double RequisitoDoSsj3De(ServerPlayer pl) =>
		Jandirus.Core.Forms.Catalogo.Def("ssj3") is { } ssj3 && pl.Forma?.Limiares is { } lim
			? lim.Porta(ssj3) : Jandirus.Core.Forms.Catalogo.PortaSsj3;

	/// <summary>
	/// O ZENKAI DA DERROTA -- todo nocaute e toda morte (`AoPerderALuta`). "Derrotado" e nocauteado OU morto: numa luta nao-letal
	/// ninguem morre, e o Zenkai nao pode depender da morte. (2026-10-26) E O BANCO DO DU: a derrota solta de 20 a 80% do banco do Zenkai,
	/// em dobro na morte e com a maioria do corpo em estado critico -- ver `Fighter.SoltarOZenkai`. Sem recarga (o dono: *"todo nocaute
	/// sem recarga"*), e sem precisar de um vencedor mais forte.
	/// </summary>
	private void ZenkaiPorDerrota(ServerPlayer vitima, ServerPlayer vencedor, bool morreu)
	{
		if (vitima == vencedor || vitima.Combate == null) return;
		double ganho = vitima.Ficha.SoltarOZenkai(morreu, vitima.Combate.Corpo.MaioriaEmEstadoCritico(), RequisitoDoSsj3De(vitima));
		if (ganho <= 0) return;
		GD.Print($"[server] Zenkai de {vitima.Name}: +{ganho:0} de BP base{(morreu ? " (na morte)" : "")}");
		// UMA FRASE SO, sem numero (o dono: *"pode ser apenas uma mensagem algo como 'seu corpo se torna mais forte apos sofrer'"*) -- e o
		// BP continua sendo segredo de quem nao tem scouter.
		Avisar(vitima, FraseDoZenkai, Jandirus.Net.Protocol.Fala.AvisoDeZenkai);
	}

	/// <summary>(2026-10-26) A frase do Zenkai -- a unica.</summary>
	internal const string FraseDoZenkai = "seu corpo se torna mais forte após sofrer.";

	// =====================================================================
	// REGENERACAO -- a habilidade ATIVA do Namekuseijin
	//
	// E SO DELE (e de quem COMPRAR a skill `Regenerate` nas arvores de Alien/Android/Demonio). O
	// Majin nao esta aqui e nao precisa estar: a regeneracao dele e PASSIVA e roda ate no meio da
	// briga -- ver `Regeneracao` e `RegenerarPassivo`. As duas nao se somam em ninguem.
	// =====================================================================
	/// <summary>
	/// ============================ 70% DO KI **MAXIMO**, e o "maximo" e o preco ============================
	/// `#define NAMEK_REGEN_KI_COST 0.7` (`namekian.dm:155`), cobrado como
	/// `var/cost = usr.MaxKi * 0.7` (`:170`). **Do MAXIMO e nao do atual**, e essa e a diferenca
	/// entre uma habilidade cara e uma habilidade que fica barata justo quando voce esta acabado:
	/// com o tanque na reserva o custo continua sendo 70% do tanque cheio, ou seja **nao da**.
	///
	/// Foi este numero que aposentou a regeneracao passiva do Namekuseijin no proprio DM -- o
	/// comentario do rework em `Genetic_Datum.dm:249-252` diz isso. Ele nao cura em combate porque a
	/// cura dele custa uma barra de Ki inteira, e nao porque esqueceram de lhe dar.
	/// ==================================================================================================
	/// </summary>
	private const double RegenCustoKi = 0.7;

	/// <summary>`#define NAMEK_REGEN_CD 100` (`namekian.dm:156`) -- 100 decisegundos = 10 s.</summary>
	private const long RegenRecargaMs = 10_000;

	/// <summary>
	/// ============================ QUEM TEM A ATIVA -- e NAO e quem regenera membro ============================
	/// Isto perguntava `Regenera(pl.Race)`, ou seja **reusava o `canheallopped`**. Eram duas perguntas
	/// diferentes coladas numa: `canheallopped` diz se o membro perdido volta SOZINHO; este verb diz
	/// quem sabe faze-lo voltar DE PROPOSITO. O DM as separa e o gate dele e explicito --
	/// `namekian.dm:163`: `if(!(usr.Race == "Namekian" || usr.Parent_Race == "Namekian")) return`.
	///
	/// A consequencia do atalho era concreta: **Majin, Bio-Androide e Shapeshifter ganhavam de graca
	/// a habilidade racial do Namekuseijin**. O Majin nao precisa dela (ele refaz membro sozinho em
	/// ~19 s), e o Shapeshifter nao deveria ter nem uma coisa nem a outra.
	///
	/// A SEGUNDA METADE CONTINUA: a skill `Regenerate` pende das arvores de Alien, Android e Demonio
	/// (`Race Trees/alien.dm:75-99`) -- quem a COMPRA sabe regenerar mesmo sem ser de Namek. La ela e
	/// canalizada e so devolve membro decepado `if(canheallopped)` (`:90`); aqui ela entra pelo mesmo
	/// verb, que e o desvio consciente que o port ja fazia.
	/// ====================================================================================================
	/// </summary>
	private bool PodeRegenerar(ServerPlayer pl) =>
		pl.Race is "Namekian" || Jandirus.Core.Races.SangueDoBio.Carrega(pl.Ficha, "Namekian") || SabeTecnica(pl, "Regenerate");

	/// <summary>
	/// REGENERA. Prefere devolver um membro DECEPADO; sem nenhum, cura o mais ferido ao cheio.
	///
	/// A ORDEM IMPORTA: um Namekuseijin sem braco que gastasse 70% do Ki curando um arranhao na
	/// perna teria jogado a habilidade fora e continuaria sem braco.
	///
	/// ============================ SO QUEM CONSEGUE SE CONCENTRAR ============================
	/// `if(usr.KO || usr.dead || usr.med || usr.train)` -- *"Voce nao consegue se concentrar para regenerar
	/// agora."* (`namekian.dm:164-166`); e o `Regenerate` das arvores de Alien/Android/Demonio, que entra por
	/// este mesmo verb, abre com o mesmo trio: `!KO && !med && !train` (`alien.dm:81`). O port so perguntava
	/// `dead`: nocauteado no chao, o Namekuseijin refazia o braco -- e quem meditava ou treinava tambem.
	/// ====================================================================================
	/// </summary>
	private void Regenerar(ServerPlayer pl)
	{
		if (!PodeRegenerar(pl)) { Avisar(pl, "seu corpo nao regenera assim."); return; }
		if (pl.Combate == null || EhCadaver(pl)) { Avisar(pl, "agora nao."); return; }
		if (!RegeneraCaidoDeTeste && (pl.Ficha.KO || pl.Ficha.med || pl.Ficha.train))
		{
			Avisar(pl, "voce nao consegue se concentrar pra regenerar agora ("
					 + (pl.Ficha.KO ? "esta caido" : pl.Ficha.med ? "saia da meditacao primeiro" : "pare o treino primeiro") + ").");
			return;
		}

		long agora = NowMs();
		if (agora < pl.RegenLivreEm)
		{
			Avisar(pl, $"ainda nao ({(pl.RegenLivreEm - agora) / 1000 + 1}s).");
			return;
		}

		double custo = pl.Ficha.MaxKi * RegenCustoKi;
		if (pl.Ficha.Ki < custo)
		{
			Avisar(pl, $"Ki insuficiente: regenerar custa {RegenCustoKi * 100:0}% do maximo.");
			return;
		}

		// 1) um membro DECEPADO, e nunca um cuja raiz tambem caiu -- a mao volta junto com o
		//    braco, entao escolher a mao separada desperdicaria a regeneracao
		BodyPart? alvo = pl.Combate.Corpo.Partes.FirstOrDefault(
			p => p.Decepado && !p.Perdido && !Caiu(pl.Combate.Corpo, p.Dono));

		// 2) sem nenhum decepado, o mais machucado
		alvo ??= pl.Combate.Corpo.Partes
			.Where(p => !p.Decepado && p.Fracao < 1)
			.OrderBy(p => p.Fracao)
			.FirstOrDefault();

		if (alvo == null) { Avisar(pl, "seu corpo ja esta inteiro."); return; }

		bool voltou = alvo.Decepado;
		if (voltou) pl.Combate.Corpo.Regenerar(alvo);

		// O `Regenerar` do corpo devolve o membro com 70% de vida. A habilidade completa o
		// resto: regenerar de proposito, gastando quase todo o Ki, nao pode deixar o braco
		// pela metade -- essa era a queixa que gerou o rework no original.
		alvo.Vida = alvo.VidaMax;

		pl.Ficha.Ki -= custo;
		pl.RegenLivreEm = agora + RegenRecargaMs;
		pl.Combate.SincronizarVida();
		AjustarGanhoDoRabo(pl);

		GD.Print($"[server] {pl.Name} regenerou {alvo.Nome}{(voltou ? " (DECEPADO)" : "")}");
		Avisar(pl, voltou ? $"seu {alvo.Nome} volta a crescer." : $"seu {alvo.Nome} se refaz por inteiro.");
	}

	private static bool Caiu(Body corpo, string? nome) =>
		nome != null && corpo.Achar(nome) is { Decepado: true };

	// =====================================================================
	// O CANAL DE HABILIDADE
	// =====================================================================
	/// <summary>
	/// UM PACOTE PRA TODA HABILIDADE ATIVA. O cliente manda um ID; quem sabe o que aquilo faz e
	/// o servidor.
	///
	/// E deliberado que nao haja um pacote por tecnica: cada skill nova exigiria um id de
	/// protocolo, uma serializacao e um caso no dispatch -- e sao trezentas. Com um canal so, a
	/// tecnica nova precisa de UMA linha aqui e uma no registro de verbs do cliente.
	/// </summary>
	private void UsarHabilidade(ServerPlayer pl, string id)
	{
		// A AREA DE ESPERA DO TORNEIO (`canfight = 0` do `apply_hold`) fecha as habilidades tambem.
		if (PresoNoTorneio(pl.Id)) { Avisar(pl, "voce esta na area de espera do torneio: sem tecnicas ate a sua vez."); return; }
		// (2026-10-18) QUEM DORME E APERTA OUTRA COISA ACORDA ANTES -- e o sono se perde. Ver `GameServer.SonoDoMajin.cs`.
		if (pl.SonoDesde >= 0 && !string.Equals(id, Jandirus.Core.Races.SonoDoMajin.Verbo, StringComparison.Ordinal))
			AcordarDoSono(pl, "você se levanta pra agir.");
		switch (id)
		{
			case "regenerar": Regenerar(pl); break;
			// A TELA ONDULA PRIMEIRO, e so entao se viaja -- ver `ComecarOMergulho`. A entrada em si
			// (`EntrarNaMente`) roda 1,8 s depois, no fim da onda.
			case "mente": ComecarOMergulho(pl); break;

			// A SAIDA VOLUNTARIA E SECA, e nao por esquecimento: o pedido pede a gota na IDA e na
			// volta por VITORIA. Abrir os olhos por vontade propria e sair da nave -- nao e um
			// acontecimento, e um gesto.
			case "sairdamente": SairDoTranse(pl); break;
			// SEM BOTAO NO MENU desde 2026-09-03 (pedido do dono): a porta do jogador pro espaco e voar
			// pra cima ate romper a atmosfera (`TentarRomperAAtmosfera`, com os tres portoes do
			// `Space_Flight`, `Mindo.dm:162`) -- ou uma nave. O id fica SO PRAS BANCADAS que precisam
			// chegar ao espaco sem voar, e so quando o processo do servidor subiu com a flag de uma delas
			// (`BancadasQueDecolamSemVoar`, `GameServer.Validacoes.cs`).
			//
			// ERA UM SALTO DE GRACA (a divida que este comentario admitia ate 2026-10-10): um cliente
			// mexido saia de qualquer luta, nocauteado inclusive, sem Ki, sem poder e sem saber voar. E
			// cobrar aqui so os tres portoes nao fechava: quem os cumpre ainda pularia a subida ate o teto
			// (no DM o verb espera 5 s e cai com qualquer dano, `Mindo.dm:177-181`). Entao a habilidade
			// nao leva ninguem: a porta e uma so, e ela ja confere tudo.
			case "decolar":
				if (DecolagemDeBancada || DecolarSemPortaoDeTeste) Decolar(pl);
				else Avisar(pl, "pra deixar o planeta, suba voando ate romper a atmosfera (ou embarque numa nave).");
				break;
			case "voar": AlternarVoo(pl); break;
			// O `Swim` do DM (`Swim.dm:5`). Entra pelo MESMO cano do voo -- uma linha aqui e uma no
			// registro de verbs do cliente, que e o preco que este canal cobra por acao nova.
			case "nadar": AlternarNado(pl); break;
			// O `Toggle Readiness` do DM (`Power Control.dm:39-52`): relaxar (5 de poder, fora do Sense e do scouter)
			// e ficar pronto (100%). Mesmo cano do voo e do nado: no DM e `mob/default/verb`, e nao uma tecnica do
			// livro -- aqui relaxar pede o controle de ki, e quem cobra e o proprio `AlternarProntidao`
			// (`GameServer.PoderEmUso.cs`). E tambem o canal que atende o `Conceal_Power` do DM (`CensoDeSkills`).
			case "prontidao": AlternarProntidao(pl); break;
			// O `Grab` do DM (`Grabbing.dm:48`). Mesmo cano do voo e do nado, e pela mesma razao: e um
			// verb do CORPO e nao uma tecnica do livro, entao o `UsarTecnica` nunca o acharia. Uma
			// tecla so cicla os tres estados (pegar / levantar no colo / soltar) -- ver
			// `GameServer.Agarrao.cs`.
			case "agarrar": AlternarAgarrao(pl); break;
			case "oozaru": ReverterOozaru(pl); break;
			// A ABSORCAO DO BIO-ANDROIDE. Entra por aqui e nao pelo `UsarTecnica` por uma razao
			// concreta: no DM ela nao e um verb do livro, e um verb de um OBJETO que a skill
			// `bioabsorb` concede (`obj/Bio_Absorb`, `Race Trees/bioandroid.dm:21-23`) -- o mesmo
			// desenho do Oozaru duas linhas acima, que tambem e do corpo e nao do livro. O gate de
			// quem PODE fica no proprio `AbsorverBio` e no registro de verb do cliente.
			//
			// E O MAJIN ENTRA PELO MESMO VERB, bifurcado pela raca como no DM (`Absorption.dm:83`): o bio
			// CONSOME, o Majin SELA -- a vitima vai viva pro interior dele. Ver `GameServer.AbsorcaoMajin.cs`.
			// (2026-10-24) O BIO-ANDROIDE BEBE SEMPRE por aqui, com o sangue que tiver: ate a v0.4.4 o de sangue Majin so selava e nao
			// evoluia. O selo dele e o botao proprio logo abaixo -- ver `GameServer.BioMajin.cs`.
			case "absorver":
				if (SelaPeloAbsorver(pl)) AbsorverMajin(pl); else AbsorverBio(pl);
				break;
			case "absorver_majin": AbsorverComoMajin(pl); break;
			// O `Expel` do mesmo objeto (`Absorption.dm:104-107`): todos de uma vez. Um so chega la embaixo,
			// como `expelir:<numero>`.
			case "expelir": ExpelirDoMajin(pl, ""); break;
			// A POSTURA DO ANDROIDE DE ABSORCAO. Mesmo cano, mesma razao: no DM ela e um
			// `mob/keyable/verb` concedido pela CONVERSAO (`assignverb`, `DNALabs.dm:220`) e nao uma
			// skill do livro -- entao o `UsarTecnica` nunca a acharia.
			case "absorver_ki": AlternarPosturaDeKi(pl); break;
			case "oozaru_olhar": OlharParaALua(pl); break;
			// (2026-10-17) A LARVA DO BIO-ANDROIDE (o adendo do dono): vestir a carapaca e se enterrar -- verbs do CORPO, como o absorver. Ver
			// `GameServer.Larva.cs`.
			case "larva": VirarLarva(pl); break;
			case "enterrar": AlternarEnterrar(pl); break;
			// (2026-10-17) O NUCLEO DO BIO (o adendo do dono): `nucleo:<membro>` escolhe; o `nucleo` sozinho e o aperto de quem nao tem o botao.
			case "nucleo": EscolherNucleo(pl, ""); break;
			// AS TECNICAS DE SKILL entram pelo mesmo cano, pelo nome do verb do DM
			// ("Solar_Flare"). Ver GameServer.Tecnicas.cs.
			default:
				// A LEMBRANCA DE UM CHEFE. Prefixo + argumento, como o `ens_ensinar:<typepath>` do
				// ensino ja fazia: um botao por chefe visto, e nenhum id novo no protocolo. Vem antes
				// das tecnicas porque nao E tecnica -- nao esta no livro que o `UsarTecnica` varre.
				// Ver `GameServer.Mente.cs`.
				if (id.StartsWith("mente_chefe:", StringComparison.Ordinal))
				{ ChamarChefe(pl, id["mente_chefe:".Length..]); break; }
				if (id.StartsWith("nucleo:", StringComparison.Ordinal))
				{ EscolherNucleo(pl, id["nucleo:".Length..]); break; }
				// (2026-10-17) AS SKILLS DO DNA DO BIO (o adendo do dono): `dna:<skill>` escolhe uma. Ver `GameServer.SkillsDoDna.cs`.
				if (id.StartsWith(Jandirus.Core.Races.SkillsRaciais.PrefixoDaEscolha, StringComparison.Ordinal))
				{ EscolherSkillDoDna(pl, id[Jandirus.Core.Races.SkillsRaciais.PrefixoDaEscolha.Length..]); break; }
				// EXPELIR UM SO de dentro do Majin: um botao por absorvido, no mesmo molde do de cima.
				if (id.StartsWith("expelir:", StringComparison.Ordinal))
				{ ExpelirDoMajin(pl, id["expelir:".Length..]); break; }
				// O KIT DO DEUS DA DESTRUICAO, pelo mesmo motivo da linha de baixo: nao e skill de livro, e do
				// TITULO -- cada verb confere o trono na hora. Ver `GameServer.KitDoDeus.cs`.
				if (UsarKitDoDeus(pl, id)) break;
				// AS DISCIPLINAS DIVINAS entram antes das tecnicas de skill: elas nao SAO skills
				// (nao se compram com marco), e por isso nao estao no livro que o UsarTecnica varre.
				if (UsarDisciplina(pl, id)) break;
				// O DISCIPULADO entra pelo mesmo cano, e antes das tecnicas pelo mesmo motivo das
				// disciplinas: ele nao E skill (nao se compra com marco), entao nao esta no livro
				// que o `UsarTecnica` varre. Ver `GameServer.Mestre.cs`.
				if (UsarVerboDeMestre(pl, id)) break;
				// O ENSINO DE SKILL entra aqui e nao no `UsarTecnica` porque ele nao E uma tecnica:
				// o id que chega e `ens_ensinar:<typepath>`, um GESTO sobre uma skill, e nao o verb
				// que a skill destrava. Ver `GameServer.Ensino.cs` -- e o cabecalho de la explica
				// por que ele nao pergunta nada ao discipulado da linha acima.
				if (UsarVerboDeEnsino(pl, id)) break;
				// A FUSAO entra aqui pelo mesmo motivo das tres linhas acima: ela nao E skill. A
				// `Fusion_Dance` do livro so serve de PORTAO (quem nao a tem ouve a recusa); o gesto
				// de convidar, aceitar e passar o controle nao esta no livro que o `UsarTecnica`
				// varre. Ver `GameServer.Fusao.cs`.
				if (UsarVerboDeFusao(pl, id)) break;
				// OS FILHOS (2026-10-17): pedir e responder nao sao skill nenhuma -- o mesmo motivo das de cima. Ver `GameServer.Filhos.cs`.
				if (UsarVerboDeFilho(pl, id)) break;
				// (2026-10-18) O NEO TUFFLE: possuir nao e skill do livro -- vem da maquina. Ver `GameServer.NeoTuffle.cs`.
				if (UsarVerboDoTuffle(pl, id)) break;
				// (2026-10-19) A TRAVESSIA DO KING YEMMA: e do trono, e nao do livro. Ver `GameServer.TravessiaDoJuiz.cs`.
				if (UsarVerboDoJuiz(pl, id)) break;
				// (2026-10-23) O RITUAL MISTICO DO VELHO KAIOHSHIN: tambem e do trono. Ver `GameServer.RitualDoKaioshin.cs`.
				if (UsarVerboDoRitual(pl, id)) break;
				// (v0.4.0) AS ATIVAS RACIAIS -- vem com a raca, e nao do livro. Ver `GameServer.Passivas.cs`.
				if (UsarAtivaRacial(pl, id)) break;
				if (!UsarTecnica(pl, id)) Avisar(pl, $"habilidade desconhecida: {id}");
				break;
		}
	}
}
