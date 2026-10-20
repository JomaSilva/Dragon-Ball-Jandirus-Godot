using Jandirus.Core.Verbos;

namespace Jandirus.Client;

/// <summary>
/// OS VERBS QUE TODO PERSONAGEM TEM -- as abas "Other" e "Admin" do menu.
///
/// ============================ O QUE ENTROU E O QUE NAO ============================
/// O original tem 91 verbs em "Other" e ~90 em "Admin". O dono pediu "os principais e mais
/// importantes... nao precisa colocar verb que muda variavel tipo o que muda ganhos de BP".
///
/// A regra que usei pra cortar: **so entra verb com mecanica pronta**. Um botao que nao faz nada e
/// pior que um botao que nao existe, porque ele promete -- e este projeto ja tropecou nisso mais de
/// uma vez. Ficaram de fora, por enquanto, os que dependem de sistema nao portado (casamento,
/// faccao, torneio, biografia, musica em streaming) e os de balanceamento.
///
/// ITENS SAIRAM DESSA LISTA: a mochila existe (`Core/Items/Inventario.cs` +
/// `GameServer.Mochila.cs`), com catalogo, largar e equipar -- e foi ela que acendeu o bit do
/// scouter, que dormia escrito e sem dono. As acoes de item nao entram aqui de proposito: elas
/// moram na TELA DA MOCHILA (tecla I, `TelaDeInventario`), porque pedem um item selecionado na
/// grade -- um verb de clique seco nao teria em que agir.
/// ==================================================================================
///
/// OS DE ADMIN AGEM SOBRE O ALVO MARCADO, e nao sobre um nome digitado. O BYOND tinha `input()`
/// bloqueante pra pedir o nome; aqui ja existe um gesto melhor e que o jogador ja usa -- duplo
/// clique marca alguem. Menos codigo, menos caixa de dialogo, e funciona com nome repetido.
///
/// A ABA ADMIN SO EXISTE PRA QUEM E ADMIN, e quem decide isso e o servidor
/// (<see cref="Jandirus.Net.Protocol.Poder.Admin"/>, ver `MenuJogo.Abas`). Esconder o botao e
/// conveniencia; a permissao e conferida DE NOVO no servidor, sempre.
/// </summary>
public static class VerbosDoJogo
{
	private static bool _registrados;

	private static GameClient? C => GameClient.Instance;

	/// <summary>Ha alguem marcado? E o que os verbs de admin exigem.</summary>
	private static bool TemAlvo() => (C?.AlvoId ?? 0) != 0;

	// ============================ QUEM VE O QUE (`Verbo.Requisito`) ============================
	// Os dois pedidos do dono: *"verbos de namek ... e alguns de rank deveriam aparecer somente se eu fosse
	// namek ou tivesse o rank em questao"* (2026-09-03) e *"dar uma limpa em verbs q sao exclusivas de raças
	// e deveriam so aparecer se o player tem os requisitos ... como esses verbs de clan dragao, split form
	// (coisa q n tenho mas tem os verbs), verb de fusao namek"* (2026-10-10). No original o verb entra em
	// `verbs` junto com o que o concede (o cargo, a skill, o objeto) e sai com ele; aqui o catalogo e um so e
	// quem filtra e a tela.
	//
	// O REQUISITO NAO SE ESCREVE AQUI: ele e a linha do COMANDO na tabela unica
	// (`Core/Verbos/RequisitosDosVerbos.cs`), que serve este menu, a busca, as teclas, os botoes da aba World
	// e a bancada que confere a tabela contra o que o servidor recusa. Verb sem linha la nao tem requisito e
	// aparece pra todo mundo. A PERMISSAO CONTINUA NO SERVIDOR: isto e so o que aparece.
	// ============================================================================================
	private static Requisito Do(string comando) => RequisitosDosVerbos.DoVerbo(comando);

	private static void NoAlvo(string cmd)
	{
		int id = C?.AlvoId ?? 0;
		if (id == 0) { Chat.Sistema("marque alguem antes (duplo clique nele)."); return; }
		C?.SendVerbo(cmd, id.ToString());
	}

	/// <summary>
	/// UM VERB DE ADMIN SOBRE ALGUEM. Com alguem marcado, vai nele; sem ninguem -- ou com a marca em MIM
	/// mesmo -- abre a lista de quem esta online pra escolher (o `input(... in world)` do original). O dono
	/// (2026-09-05): *"verbs de adm que dizem que afetam quem voce ta com target, caso voce tenha target
	/// em si mesmo ou em ninguem, o jogo vai mostrar a lista dos jogadores online pra voce escolher qual
	/// voce vai afetar"*. Por isso estes verbs nao trazem mais o `TemAlvo`: sem alvo eles PERGUNTAM.
	/// </summary>
	private static void NoAlvoDeAdmin(string cmd, string rotulo)
	{
		if (C is not { } c) return;
		if (MenuJogo.PrecisaEscolherAlvo(c.AlvoId, c.LocalId)) { MenuJogo.Instancia?.PedirAlvoDeAdmin(cmd, rotulo); return; }
		c.SendVerbo(cmd, c.AlvoId.ToString());
	}

	/// <summary>Chamado quando o personagem entra no mundo. Idempotente.</summary>
	public static void Registrar()
	{
		if (_registrados) return;
		_registrados = true;

		// =====================================================================
		// OTHER
		// =====================================================================
		Verbos.Registrar(new Verbo("Training Session", Verbos.Outros,
			"Comeca uma sessao de treino: marca o seu poder de agora pra medir o ganho depois.",
			() => C?.SendVerbo("sessao_nova")));

		Verbos.Registrar(new Verbo("View Session", Verbos.Outros,
			"Quanto de poder voce ganhou desde que a sessao comecou.",
			() => C?.SendVerbo("sessao_ver")));

		// O `knockbackon` do original. Existe pra treinar com alguem sem arremessa-lo a cada golpe.
		// OS TORNEIOS (Terra e Outro Mundo): o painel do canto responde ao convite, e estes tres verbos
		// sao o mesmo gesto por outro caminho -- e o unico caminho pra ver a situacao.
		Verbos.Registrar(new Verbo("Participar do torneio", Verbos.Outros,
			"Aceita o convite do torneio em andamento (Terra ou Outro Mundo). O painel do canto faz o mesmo.",
			() => C?.SendVerbo("trn_participar")));

		Verbos.Registrar(new Verbo("Recusar o torneio", Verbos.Outros,
			"Recusa o convite do torneio, ou desiste da inscricao enquanto elas estao abertas.",
			() => C?.SendVerbo("trn_recusar")));

		Verbos.Registrar(new Verbo("Situacao do torneio", Verbos.Outros,
			"Em que pe esta o torneio: inscricoes, chave, a luta de agora -- ou quando e o proximo.",
			() => C?.SendVerbo("trn_status")));

		Verbos.Registrar(new Verbo("Chaveamento do torneio", Verbos.Outros,
			"Abre (ou fecha) o chaveamento na tela: todos os nomes, rodada a rodada, com a sua proxima luta pulsando.",
			() => Hud.Instancia?.AlternarChave()));

		Verbos.Registrar(new Verbo("Assistir torneio", Verbos.Outros,
			"A camera vai pra luta de agora (e volta ao apertar de novo). E o botao do canto inferior esquerdo.",
			() => { if (World.Instancia is { } m) m.AssistirTorneio(!m.Assistindo); }));

		Verbos.Registrar(new Verbo("Toggle Knockback", Verbos.Outros,
			"Liga e desliga o arremesso dos SEUS golpes.",
			() => C?.SendVerbo("knockback")));

		// ============================ OS GRAUS DO SUPER SAIYAJIN ============================
		// Pedido do dono. Os dois grades sao um DESVIO da escada -- soco mais pesado, corpo mais
		// lento e mais facil de acertar (ver `Catalogo.Grade2Mods`/`Grade3Mods`) --, e quem nao quer
		// pagar esse preco precisava de um jeito de nao passar por eles.
		//
		// **O BOTAO NAO SABE O ESTADO, E ISSO E DE PROPOSITO.** Quem guarda a preferencia e o
		// servidor (ela e por PERSONAGEM e vai pro disco), e ele responde no chat a cada clique --
		// que e literalmente o que o dono pediu ("ao clicar fala se desativei ou nao"). Um rotulo
		// "ligado/desligado" aqui no cliente seria uma segunda verdade sobre o mesmo fato, e a que
		// erra e sempre a copia: bastaria trocar de personagem pra ela mentir.
		// ==================================================================================
		Verbos.Registrar(new Verbo("Toggle SSJ Grades", Verbos.Outros,
			"Liga e desliga os graus do Super Saiyajin no caminho do C: ligados, o SSJ1 sobe pelo "
			+ "Grade 2 e pelo Grade 3 antes do SSJ2; desligados, vai direto pro SSJ2.",
			() => C?.SendVerbo("graus")) { Requisito = Do("graus") });

		// (2026-10-19) OS CORPOS DAS FORMAS DO FROST DEMON -- o `Icer_Form_Settings` do DM (`icer.dm:77-83`, aba Other), que todo Frost
		// Demon recebe (`fd_login_check`). O botao abre a tela; quem manda o verb `formas_frost` e o Salvar dela. Ver `TelaDeFormasDoFrost`.
		Verbos.Registrar(new Verbo("Icer Form Settings", Verbos.Outros,
			"Re-escolhe o corpo de cada forma do Frost Demon (as supressões do Mutante, a base e as duas evoluções). "
			+ "Trocar os corpos devolve você à forma inicial.",
			() => TelaDeFormasDoFrost.Instancia?.Abrir()) { Requisito = Do("formas_frost") });

		// ============================ O `Goto Spawn` MUDOU DE ABA -- DECISAO DO DONO ============================
		// Ele ficava aqui, em "Other", pra todo mundo. Saiu por ordem do dono (*"tire o verb
		// gotospawn, deixe ele so pra adm"*) e reapareceu la embaixo em `Verbos.Admin`.
		//
		// TROCAR DE ABA NAO E COSMETICA NESTE MENU: a aba Admin so e desenhada pra quem o servidor
		// marcou como admin, E a busca filtra por `Verbos.Visivel` -- sem isso um jogador digitaria
		// "spawn" na barra e receberia o botao de volta. Ver `Verbos.SouAdmin`.
		//
		// E o botao seria so a metade: quem fecha a porta de verdade e o servidor, que deixou de
		// ter `case "spawn"` (`GameServer.Verbos.cs`) -- um cliente mexido nao tem o que mandar.
		// ====================================================================================================

		Verbos.Registrar(new Verbo("Clear Buffs", Verbos.Outros,
			"Derruba todos os efeitos temporarios de cima de voce.",
			() => C?.SendVerbo("limpar_buffs")));

		// OS TRES ABAIXO NAO SAO NOVOS NO SERVIDOR -- os canais ja existiam e ninguem os
		// chamava. Eram sistemas inteiros sem porta de entrada.
		Verbos.Registrar(new Verbo("Ranks", Verbos.Outros,
			"Os cargos do mundo: quem ocupa cada um e o que falta pra voce.",
			() => C?.SendCargo("")));

		// ============================ A FICHA MORAL ============================
		// Ela entra na mesma leva que deu PRODUTORES ao karma (`GameServer.Karma.cs`). O eixo decide
		// nove cargos, nao aparece em barra nenhuma e so se mexe matando (ou deixando de matar) --
		// sem este botao, a primeira noticia que o jogador tem dele e um cargo o recusando.
		Verbos.Registrar(new Verbo("Karma", Verbos.Outros,
			"Sua ficha moral: o numero, a faixa, o que sobe, o que desce e que cargos ele abre ou fecha.",
			() => C?.SendVerbo("karma")));

		// ============================ AS PORTAS DOS CARGOS ============================
		// Os 14 cargos que nao se reivindicam (nomeacao, sucessao e duelo) passaram a ter mecanismo
		// -- ver `GameServer.CargoPortas.cs` e `GameServer.CargoDuelo.cs`. Sem estes botoes eles
		// seriam de novo o que a tabela de cargos ja tinha sido por meses: regra escrita e porta
		// nenhuma. O canal de fala dos cargos (`RankChat`) mora na caixa de chat (`/cargos`),
		// porque falar exige texto e botao nao carrega texto.
		Verbos.Registrar(new Verbo("Appoint Elder", Verbos.Outros,
			"So o Grande Anciao de Namek: oferece a quem esta marcado um assento de Anciao cardeal.",
			() => NoAlvo("cargo_nomear")) { Requisito = Do("cargo_nomear") });

		Verbos.Registrar(new Verbo("Accept Elder Seat", Verbos.Outros,
			"Aceita o assento de Anciao que te ofereceram.",
			() => C?.SendVerbo("cargo_nomear_aceitar")) { Requisito = Do("cargo_nomear_aceitar") });

		Verbos.Registrar(new Verbo("Decline Elder Seat", Verbos.Outros,
			"Recusa o assento de Anciao.",
			() => C?.SendVerbo("cargo_nomear_recusar")) { Requisito = Do("cargo_nomear_recusar") });

		// (2026-10-19) O PATRONATO DO KAIOSHIN (`KaioshinApprentice.dm`): os dois verbos da skill `Kaioshin Apprenticeship` e a resposta de
		// quem foi convidado -- ver `GameServer.AprendizDeKaioshin.cs`. Sem ninguem marcado, o "Name Apprentice" pede a lista de quem por
		// perto pode ser aprendiz (o cartao da aba Other).
		Verbos.Registrar(new Verbo("Name Apprentice", Verbos.Outros,
			"So o Kaioshin no trono: toma como Aprendiz de Kaioshin quem esta marcado (a ate 4 tiles) -- sem ninguem marcado, mostra quem por perto pode ser.",
			() => C?.SendVerbo("aprendiz_nomear", (C?.AlvoId ?? 0) is var id and not 0 ? id.ToString() : "")) { Requisito = Do("aprendiz_nomear") });

		Verbos.Registrar(new Verbo("Dismiss Apprentice", Verbos.Outros,
			"So o Kaioshin: mostra os seus aprendizes pra dispensar um -- ele perde o kit e recebe os Marcos de volta.",
			() => C?.SendVerbo("aprendiz_dispensar", "")) { Requisito = Do("aprendiz_dispensar") });

		Verbos.Registrar(new Verbo("Accept Apprenticeship", Verbos.Outros,
			"Aceita ser Aprendiz de Kaioshin de quem te ofereceu.",
			() => C?.SendVerbo("aprendiz_aceitar")) { Requisito = Do("aprendiz_aceitar") });

		Verbos.Registrar(new Verbo("Decline Apprenticeship", Verbos.Outros,
			"Recusa o aprendizado.",
			() => C?.SendVerbo("aprendiz_recusar")) { Requisito = Do("aprendiz_recusar") });

		// ============================ A RESPOSTA A OFERTA DE JUVENTUDE ============================
		// O `Restore_Youth` do Grand Kai e do Demon Lord (lote G8) abre uma OFERTA, porque no DM o
		// verb abre um `input()` **no alvo** e so escreve a idade se ele disser "Yes"
		// (`OtherworldRankSkills.dm:170-174`). O consentimento e parte da tecnica e nao cortesia:
		// idade mexe em BP neste port, entao rejuvenescer um desafeto seria um debuff.
		//
		// OS DOIS BOTOES SAO DE QUEM RECEBE, e nao de quem tem a skill -- por isso eles moram aqui,
		// na aba de todo mundo, e nao no painel de habilidades.
		Verbos.Registrar(new Verbo("Accept Youth", Verbos.Outros,
			"Aceita a oferta de voltar a ser jovem que te fizeram.",
			() => C?.SendVerbo("juventude_aceitar")) { Requisito = Do("juventude_aceitar") });

		Verbos.Registrar(new Verbo("Decline Youth", Verbos.Outros,
			"Recusa a oferta de voltar a ser jovem.",
			() => C?.SendVerbo("juventude_recusar")) { Requisito = Do("juventude_recusar") });

		// A RESPOSTA A OFERTA DE DESPERTAR O POTENCIAL (`Unlock_Potential`, lote G11): no DM o verb
		// pergunta AO ALVO "Do you want to do this?" (`UnlockPotential.dm:13`) e so entao desperta.
		// Mesmo molde da juventude: os dois botoes sao de quem RECEBE, na aba de todo mundo.
		Verbos.Registrar(new Verbo("Accept Potential", Verbos.Outros,
			"Aceita que despertem o seu potencial adormecido (uma vez na vida).",
			() => C?.SendVerbo("potencial_aceitar")) { Requisito = Do("potencial_aceitar") });

		Verbos.Registrar(new Verbo("Decline Potential", Verbos.Outros,
			"Recusa a oferta de despertar o seu potencial.",
			() => C?.SendVerbo("potencial_recusar")) { Requisito = Do("potencial_recusar") });

		// ============================ AS ORDENS DAS COPIAS DO SPLIT FORM (lote G12) ============================
		// No DM cada copia tem um `Click()` com o menu Follow / Stop / Attack Target / Attack Nearest /
		// Destroy (`Split Forms.dm:28-68`). O port nao tem clique em NPC, entao o menu virou cinco verbos
		// de quem e o DONO, e cada ordem vale pra TODAS as copias dele de uma vez.
		Verbos.Registrar(new Verbo("Split Form: Follow", Verbos.Outros,
			"Suas copias passam a seguir voce (param a dois tiles).",
			() => C?.SendVerbo("splitform_seguir")) { Requisito = Do("splitform_seguir") });
		Verbos.Registrar(new Verbo("Split Form: Stop", Verbos.Outros,
			"Suas copias param onde estao.",
			() => C?.SendVerbo("splitform_parar")) { Requisito = Do("splitform_parar") });
		Verbos.Registrar(new Verbo("Split Form: Attack Target", Verbos.Outros,
			"Suas copias atacam quem voce marcou (duplo clique).",
			() => C?.SendVerbo("splitform_alvo")) { Requisito = Do("splitform_alvo") });
		Verbos.Registrar(new Verbo("Split Form: Attack Nearest", Verbos.Outros,
			"Suas copias atacam quem estiver mais perto delas (menos voce).",
			() => C?.SendVerbo("splitform_perto")) { Requisito = Do("splitform_perto") });
		Verbos.Registrar(new Verbo("Split Form: Destroy", Verbos.Outros,
			"Desfaz todas as suas copias.",
			() => C?.SendVerbo("splitform_destruir")) { Requisito = Do("splitform_destruir") });

		// ============================ A RESPOSTA A GENKIDAMA (lote G12) ============================
		// `alert("Give power? It'll drain 10% of your Ki.")` em quem MEDITA (`SpiritBomb.dm:81`): os dois
		// botoes sao de quem recebe o pedido, pelo mesmo desenho da oferta de juventude acima.
		Verbos.Registrar(new Verbo("Give Power to Spirit Bomb", Verbos.Outros,
			"Doa um decimo do seu Ki -- e o seu poder inteiro -- a Genkidama que alguem esta formando neste mundo.",
			() => C?.SendVerbo("genkidama_doar")) { Requisito = Do("genkidama_doar") });
		Verbos.Registrar(new Verbo("Refuse Spirit Bomb", Verbos.Outros,
			"Guarda a sua energia.",
			() => C?.SendVerbo("genkidama_negar")) { Requisito = Do("genkidama_negar") });

		Verbos.Registrar(new Verbo("Name Heir", Verbos.Outros,
			"So o Rei de Vegeta: poe quem esta marcado na linha de sucessao do trono.",
			() => NoAlvo("cargo_herdeiro")) { Requisito = Do("cargo_herdeiro") });

		Verbos.Registrar(new Verbo("Remove Heir", Verbos.Outros,
			"Tira quem esta marcado da linha de sucessao de Vegeta.",
			() => NoAlvo("cargo_herdeiro_tirar")) { Requisito = Do("cargo_herdeiro_tirar") });

		Verbos.Registrar(new Verbo("Line of Succession", Verbos.Outros,
			"Quem herda o trono de Vegeta, na ordem.",
			() => C?.SendVerbo("cargo_herdeiros")) { Requisito = Do("cargo_herdeiros") });

		Verbos.Registrar(new Verbo("Challenge God of Destruction", Verbos.Outros,
			"Desafia FORMALMENTE o portador do titulo. Exige God Ki desperto; nocaute decide.",
			() => C?.SendVerbo("cargo_desafiar")) { Requisito = Do("cargo_desafiar") });

		Verbos.Registrar(new Verbo("Accept Challenge", Verbos.Outros,
			"Aceita agora o duelo pelo titulo.",
			() => C?.SendVerbo("cargo_duelo_aceitar")) { Requisito = Do("cargo_duelo_aceitar") });

		Verbos.Registrar(new Verbo("Postpone Challenge", Verbos.Outros,
			"Adia o duelo. O terceiro adiamento na mesma semana e covardia e custa o titulo.",
			() => C?.SendVerbo("cargo_duelo_adiar")) { Requisito = Do("cargo_duelo_adiar") });

		Verbos.Registrar(new Verbo("Title Status", Verbos.Outros,
			"O titulo de God of Destruction: quem carrega, a tarefa em aberto, falhas e prazos.",
			() => C?.SendVerbo("cargo_titulo")));

		// ============================ OS DEVERES DO CARGO ============================
		// O `Meu Rank` do original. Sem ele o motor de tarefas seria invisivel: o prazo so apareceria
		// quando ja tivesse vencido, e a destituicao chegaria como castigo sem aviso. Ver
		// `GameServer.CargoMissoes.cs`.
		Verbos.Registrar(new Verbo("Rank Duty", Verbos.Outros,
			"Os deveres do seu cargo: a tarefa em aberto, o prazo em dias in-game, as falhas e o renome.",
			() => C?.SendVerbo("cargo_deveres")) { Requisito = Do("cargo_deveres") });

		Verbos.Registrar(new Verbo("Fund Earth", Verbos.Outros,
			"So o Presidente da Terra, e so com a tarefa de verba aberta: deposita a verba no cofre da Terra.",
			() => C?.SendVerbo("cargo_verba")) { Requisito = Do("cargo_verba") });

		// =====================================================================
		// A SALA DO TEMPO -- os dois verbs do GUARDIAO DA TERRA
		//
		// A CHAVE E O RESGATE NASCEM JUNTOS, e os dois sao do mesmo cargo. O `Core/Ranks/Ranks.cs`
		// ja descrevia o Guardiao como quem "faz chaves da Sala do Tempo e autoriza a entrada", e
		// nenhum dos dois verbos existia -- era promessa escrita e nao ligada.
		//
		// SO QUEM PODE OS VE (2026-09-03). Eles apareciam pra todo mundo "pra ensinar que a Sala tem
		// dono"; o dono do jogo achou isso ruido -- e a Sala continua tendo dono na descricao do cargo,
		// na aba Cargos. A permissao e do servidor de qualquer jeito (ver `GameServer.SalaDoTempo.cs`).
		//
		// A CHAVE E DE QUEM SABE FAZE-LA, e nao so do trono: o servidor cobra a skill `Permission`, que o
		// kit do Guardiao da e o do Mestre Korin tambem. O botao era so do Guardiao, e o Korin ficava com a
		// skill, com o servidor aceitando, e sem ter onde apertar. SOLTAR continua sendo so do Guardiao.
		Verbos.Registrar(new Verbo("Time Chamber: Authorize", Verbos.Outros,
			"So quem sabe fazer a chave (o Guardiao da Terra e o Mestre Korin). Da ao alvo marcado UMA entrada na Sala do Tempo.",
			() => NoAlvo("sala_autorizar")) { Requisito = Do("sala_autorizar") });

		Verbos.Registrar(new Verbo("Time Chamber: Release", Verbos.Outros,
			"So o Guardiao da Terra. Destranca a porta pra quem ficou preso la dentro.",
			() => NoAlvo("sala_soltar")) { Requisito = Do("sala_soltar") });

		Verbos.Registrar(new Verbo("Tech Catalog", Verbos.Outros,
			"O catalogo de construcoes, com o custo e o motivo de cada nao.",
			() => C?.SendTech("lista")));

		Verbos.Registrar(new Verbo("Bolt", Verbos.Outros,
			"Aparafusa (ou solta) a construcao ao seu lado. Sem aparafusar, ela nao funciona.",
			() => C?.SendTech("aparafusar")));

		Verbos.Registrar(new Verbo("Study", Verbos.Outros,
			"Debruca-se sobre a bancada de pesquisa. E o unico jeito de ganhar tecnologia.",
			() => C?.SendTech("estudar")));

		// ============================ MINERAR (dono, 2026-10-10) ============================
		// *"adicionando um verb chamado mineirar ao qual vc comeca a pegar resource por segundo"*. PRA TODO
		// MUNDO (sem `Mostrar`): nao pede raca, cargo nem item -- a pa e a furadeira so aceleram. NAO E UM
		// `SendVerbo`: minerar e uma ATIVIDADE do corpo, como treinar e meditar, e quem a declara e o
		// `LocalPlayer` (a pose e o HUD sao dele). Os numeros da dica saem das constantes do Core, que sao
		// as que o servidor paga.
		//
		// A FRASE E CURTA DE PROPOSITO (tres linhas no cartao, como as vizinhas): a primeira tinha cinco e
		// esticava a fileira inteira. O que saiu dela ja esta escrito onde se le na hora -- o dobro do preco
		// no topo da aba Tech, a reserva do planeta na faixa e na fala de quem comeca a minerar.
		//
		// ELA DIZ ONDE (dono, 2026-10-11: a ceu aberto, ou numa caverna do planeta), no mesmo tamanho de antes: o
		// "por segundo" virou o "/s" do primeiro numero, e os dois "uma" sairam.
		// =====================================================================================
		Verbos.Registrar(new Verbo("Minerar", Verbos.Outros,
			"Tira recursos do chão, a céu aberto ou em caverna: "
			+ $"+{Jandirus.Core.Economia.Recurso.PorSegundoNaMao:0}/s com as mãos, "
			+ $"+{Jandirus.Core.Economia.Recurso.PorSegundoComPa:0} com pá (Shovel) na mochila, "
			+ $"+{Jandirus.Core.Economia.Recurso.PorSegundoComFuradeira:0} com furadeira (Hand Drill). "
			+ "Andar ou agir interrompe; acionar de novo para.",
			LocalPlayer.PedirMineracao) { Chave = "minerar" });

		// (v0.2.1) O GATILHO DAS ARMAS DE FOGO -- o `verb/fire()` do original (`set name="Shoot"`, `set category="Skills"`,
		// `Guns.dm:110-114`). So existe pra quem carrega uma arma (`Fatos.TemArmaDeFogo`).
		Verbos.Registrar(new Verbo("Atirar (arma de fogo)", Verbos.Outros,
			"Aperta o gatilho da primeira arma da mochila que tem munição, energia ou Ki pra atirar. "
			+ "Ponha numa tecla: cada arma tem o intervalo dela entre um tiro e outro.",
			() => C?.SendVerbo(Jandirus.Core.Tech.ArmasDeFogo.VerboAtirar)) { Requisito = Do(Jandirus.Core.Tech.ArmasDeFogo.VerboAtirar) });

		Verbos.Registrar(new Verbo("Who", Verbos.Outros,
			"Quem esta no mundo agora, e onde.",
			() => C?.SendVerbo("quem")));

		// =====================================================================
		// O CONVIVIO -- os verbs de `Contacts.dm`
		//
		// O QUE PEDE ALGUEM AGE SOBRE O ALVO MARCADO, e nao sobre um nome digitado. No BYOND ele era
		// `set src in view(10)` -- "aquele ali, o que eu estou vendo" --, e o gesto equivalente aqui e
		// o duplo clique que o jogador ja usa pra tudo. O ALCANCE continua sendo o de la, conferido no
		// servidor: 10 tiles pra anotar alguem.
		//
		// DIZER O QUE ALGUEM E PRA VOCE (amigo, rival, inimigo...) NAO ESTA AQUI de proposito: nao pede
		// presenca (no original mora no proprio Contact, que e seu e funciona com a pessoa offline) e
		// e uma escolha ENTRE NOVE opcoes com custo diferente. Isso e uma tela, nao um botao -- ela
		// vive na aba People, ao lado da pessoa a que se refere. (O "Request Friendship" e o "Declare
		// Rival" sairam em 2026-10-10 com a afinidade automatica: amigo e rival sao declaracoes de la.)
		// =====================================================================
		Verbos.Registrar(new Verbo("Remember Person", Verbos.Outros,
			"Guarda o rosto de quem voce marcou. Ele passa a aparecer na aba People.",
			() => NoAlvo("conhecer"), TemAlvo));

		Verbos.Registrar(new Verbo("Known People", Verbos.Outros,
			"Pede ao servidor a lista de quem voce conhece.",
			() => C?.SendVerbo("conhecidos")));

		// O BANCO. Ele existe nos mapas do original desde sempre (dezessete deles, um por cidade)
		// e ate agora era celula de tilemap -- desenho parado. Agora e uma construcao de verdade,
		// e estes tres verbos sao o que ele FAZ. Sem argumento = tudo, que e o uso de sempre:
		// chegar, guardar, sair. Ver `GameServer.Banco.cs`.
		Verbos.Registrar(new Verbo("Bank: Balance", Verbos.Outros,
			"Quanto voce tem no bolso e quanto tem guardado. Precisa estar num banco.",
			() => C?.SendVerbo("banco_ver")));

		Verbos.Registrar(new Verbo("Bank: Deposit All", Verbos.Outros,
			"Guarda todo o zeni do bolso. O que esta no banco nao se perde ao morrer.",
			() => C?.SendVerbo("banco_depositar")));

		Verbos.Registrar(new Verbo("Bank: Withdraw All", Verbos.Outros,
			"Tira do cofre tudo o que voce guardou.",
			() => C?.SendVerbo("banco_sacar")));

		// =====================================================================
		// ADMIN -- o servidor confere de novo. Ver GameServer.Admin.cs.
		//
		// SO OS QUE NAO PEDEM TEXTO moram aqui. Os que precisam de um nome ou de um numero
		// (anunciar, promover, banir, quantos marcos) vivem no PAINEL da aba Admin, que tem campo
		// de digitacao -- no BYOND esses eram justamente os que abriam um `input()`.
		//
		// Ficaram de fora do port, com motivo: os que so mexem em variavel de balanceamento
		// (`Gravity_Cap`, `Change_Ascension`, `Set_KO_Time_Mult`, `Global_EXP_Rate`), que o dono
		// pediu pra nao trazer; e os que dependem de sistema nao portado (esferas, fusao, magia,
		// dungeons). O TORNEIO existe desde 2026-09-06, e os verbs dele fecham esta lista.
		//
		// O CLIMA SAIU DESSA LISTA: ele existe agora (ver `Core.World.Clima`), e forcar um tipo
		// e escolher a forca moram no PAINEL da aba Admin, porque pedem dois argumentos.
		//
		// A CONQUISTA DE PLANETAS SAIU TAMBEM, e pelo mesmo motivo: ela existe
		// (`Core/Social/Conquista.cs`, `GameServer.Conquista.cs`, `GameServer.Invasao.cs`, bancada
		// `--conquistateste`). Os verbs dela pedem um planeta por nome, entao seguem a regra desta
		// lista e vao pro PAINEL, nao pra ca. Esta linha ja mentiu uma vez; se a proxima da lista
		// nascer, tire-a daqui NO MESMO dia -- lista de ausencias envelhece calada.
		// =====================================================================

		// ---------------------------------------------------------- corpo do alvo
		Verbos.Registrar(new Verbo("Heal", Verbos.Admin,
			"Cura o alvo marcado por completo -- membros inclusive, sem reviver. Sem alvo (ou com a marca em voce), abre a lista de quem esta online.",
			() => NoAlvoDeAdmin("admin_curar", "Heal")));

		// `MassRevive()` (Admin.dm:294) aplicado a um so: de pe, inteiro, Ki cheio.
		Verbos.Registrar(new Verbo("Revive Target", Verbos.Admin,
			"Poe o alvo marcado de pe, mesmo morto. Sem alvo (ou com a marca em voce), abre a lista de quem esta online.",
			() => NoAlvoDeAdmin("admin_reviver", "Revive Target")));

		Verbos.Registrar(new Verbo("Knock Out Target", Verbos.Admin,
			"Poe o alvo marcado no chao.",
			() => NoAlvoDeAdmin("admin_kb", "Knock Out Target")));

		// o `Kill()` do original (Admin.dm:350)
		Verbos.Registrar(new Verbo("Kill Target", Verbos.Admin,
			"Mata o alvo marcado. Ele renasce pelo caminho normal.",
			() => NoAlvoDeAdmin("admin_matar", "Kill Target")));

		// `World_Heal()` (Admin.dm:410) + `MassRevive()`
		Verbos.Registrar(new Verbo("Heal Everyone", Verbos.Admin,
			"Restaura todo mundo do servidor: vida, membros e Ki.",
			() => C?.SendVerbo("admin_curar_todos")));

		// `KO_All_in_View()` (Admin.dm:394) / `MassKO()` (MapSave.dm:2)
		Verbos.Registrar(new Verbo("Knock Out Everyone Here", Verbos.Admin,
			"Poe no chao todo mundo da SUA zona. Voce fica de pe.",
			() => C?.SendVerbo("admin_kb_todos")));

		// ---------------------------------------------------------- ir e trazer
		Verbos.Registrar(new Verbo("Go To Target", Verbos.Admin,
			"Vai ate o alvo marcado.",
			() => NoAlvoDeAdmin("admin_ir", "Go To Target")));

		Verbos.Registrar(new Verbo("Bring Target", Verbos.Admin,
			"Traz o alvo marcado ate voce.",
			() => NoAlvoDeAdmin("admin_trazer", "Bring Target")));

		// `MassSummon()` (Admin.dm:305)
		Verbos.Registrar(new Verbo("Bring Everyone", Verbos.Admin,
			"Traz TODO MUNDO do servidor pra onde voce esta.",
			() => C?.SendVerbo("admin_trazer_todos")));

		// `Return_Mob_To_Spawn()` (SpawnPoints.dm:37) / `Send_Spawn()` (Admin.dm:30)
		Verbos.Registrar(new Verbo("Send Target To Spawn", Verbos.Admin,
			"Manda o alvo marcado de volta ao ponto de partida.",
			() => NoAlvoDeAdmin("admin_spawn_alvo", "Send Target To Spawn")));

		// O `Goto Spawn` DE ANTES, agora so aqui (decisao do dono). Ele era o unico caminho que
		// atravessava a tranca da Sala do Tempo sem perguntar nada -- ver `GameServer.SalaDoTempo.cs`.
		// NAO exige alvo: este manda o PROPRIO admin, e o de cima manda o alvo marcado.
		Verbos.Registrar(new Verbo("Goto Spawn", Verbos.Admin,
			"Volta VOCE ao seu ponto de partida. Era o verb do jogador; hoje e so de admin.",
			() => C?.SendVerbo("admin_spawn")));

		// A VALVULA DA SALA DO TEMPO. Ela nao existe por conveniencia: a regra do dono e que a sala
		// PRENDE, e quem solta e o Guardiao da Terra -- um jogador preso com o trono vago, ou com o
		// Guardiao offline, e um jogador que **nao pode jogar**. Este botao e a saida escolhida pra
		// esse risco (a outra metade e o aviso na propria porta). Ver `GameServer.SalaDoTempo.cs`.
		Verbos.Registrar(new Verbo("Release From Time Chamber", Verbos.Admin,
			"Destranca a Sala do Tempo pro alvo marcado. A valvula de quando nao ha Guardiao.",
			() => NoAlvoDeAdmin("admin_sala_soltar", "Release From Time Chamber")));

		// ---------------------------------------------------------- enxergar
		// O `Assess()` (Assess.dm) -- o verb de admin que mais importa: e o unico jeito de ler o BP
		// alheio sem scouter, e sem ele nao ha como julgar uma denuncia de trapaca.
		Verbos.Registrar(new Verbo("Assess Target", Verbos.Admin,
			"A ficha COMPLETA do alvo marcado, sem sigilo: BP, forma, maestrias, corpo, conta.",
			() => NoAlvoDeAdmin("admin_ficha", "Assess Target")));

		// `AssessAll()` (Admin.dm:400) + `BP_Lists()` (Stat Balance.dm:23)
		Verbos.Registrar(new Verbo("Assess All", Verbos.Admin,
			"Todo mundo do servidor, do mais forte pro mais fraco.",
			() => C?.SendVerbo("admin_fichas")));

		Verbos.Registrar(new Verbo("Who (admin)", Verbos.Admin,
			"A lista de quem esta no mundo, com a zona de cada um.",
			() => C?.SendVerbo("admin_quem")));

		// `Races()` (Admin.dm:286)
		Verbos.Registrar(new Verbo("Races", Verbos.Admin,
			"Quantos de cada raca estao jogando agora.",
			() => C?.SendVerbo("admin_racas")));

		// `AllIPs()` (Admin.dm:254) -- e assim que se descobre que duas contas sao a mesma pessoa
		Verbos.Registrar(new Verbo("IPs", Verbos.Admin,
			"De que endereco cada um esta conectado.",
			() => C?.SendVerbo("admin_ips")));

		// `Galaxy_Status()` (ProceduralSpace.dm:1704)
		Verbos.Registrar(new Verbo("Galaxy Status", Verbos.Admin,
			"A seed do universo, as zonas com gente e os planetas da sua chunk.",
			() => C?.SendVerbo("admin_galaxia")));

		// NAO TEM ORIGINAL: o DM nao tinha estrela nenhuma. Existe porque a estrela mais perto do
		// nascimento fica a 7.952 px -- dois minutos de voo --, e conferir o sol em jogo sem isto
		// custaria dois minutos por tentativa.
		Verbos.Registrar(new Verbo("Ir Para A Estrela", Verbos.Admin,
			"Salta pro centro da estrela mais proxima. So no espaco -- e ela QUEIMA.",
			() => C?.SendVerbo("admin_estrela")));

		// NAO TEM ORIGINAL: a atmosfera divina e do port. Existe pelo motivo do verb de cima -- o mundo divino mais
		// perto do nascimento fica a varios minutos de voo, e conferir o despertar sem isto custaria a viagem.
		Verbos.Registrar(new Verbo("Ir Para O Mundo Divino", Verbos.Admin,
			"Salta pra orbita de um mundo de atmosfera divina (o mais perto que o seu corpo aguenta). So no espaco; encoste nele pra pousar.",
			() => C?.SendVerbo("admin_mundo_divino")));

		// NAO TEM ORIGINAL: as Salas do Tempo dos mundos sorteados sao do port (dono, 2026-10-11), e um mundo em 400
		// tem uma. No chao o verb so LISTA as das celulas em volta; do espaco ele salta pra orbita da mais perto.
		Verbos.Registrar(new Verbo("Ir Para A Sala Do Tempo", Verbos.Admin,
			"Lista os mundos com Sala do Tempo por perto (a dilatacao de cada uma) e, do espaco, salta pra orbita do mais proximo. Encoste nele pra pousar: a porta fica na montanha mais perto do pouso.",
			() => C?.SendVerbo("admin_sala_do_tempo")));

		// ---------------------------------------------------------- punir
		// `Boot()` (Admin.dm:450)
		Verbos.Registrar(new Verbo("Boot Target", Verbos.Admin,
			"Desconecta o alvo marcado. Ele pode voltar.",
			() => NoAlvoDeAdmin("admin_expulsar", "Boot Target")));

		// `Mute()`/`UnMute()` (Admin.dm:459-472). Alterna: o mesmo botao cala e descala.
		Verbos.Registrar(new Verbo("Mute Target", Verbos.Admin,
			"Cala (ou descala) o alvo marcado. Vale pra conta, nao so pro personagem.",
			() => NoAlvoDeAdmin("admin_calar", "Mute Target")));

		// `UNMUTEALL()` (Admin.dm:275)
		Verbos.Registrar(new Verbo("Unmute All", Verbos.Admin,
			"Devolve a fala a todo mundo que estava calado.",
			() => C?.SendVerbo("admin_descalar")));

		// ---------------------------------------------------------- dar
		// `Reward()` (Rewards.dm:11) -- um marco por clique
		Verbos.Registrar(new Verbo("Give Milestone", Verbos.Admin,
			"Da um marco de habilidade ao alvo marcado. Sem alvo, a voce.",
			() => C?.SendVerbo("admin_marco", $"{C?.AlvoId ?? 0}|1")));

		// `Global_Points()` -- "Global - Give all Milestones." (Stat Balance.dm:13)
		Verbos.Registrar(new Verbo("Give Milestone to Everyone", Verbos.Admin,
			"Da um marco de habilidade a cada jogador do servidor.",
			() => C?.SendVerbo("admin_marco_todos", "1")));

		// `Edit_Masteries()` (Mastery Master.dm:183). So a forma ATUAL: maestria custa tres horas
		// dentro dela, e "dominar tudo" apagaria a progressao inteira de alguem num clique.
		Verbos.Registrar(new Verbo("Master Current Form", Verbos.Admin,
			"Poe em 100% a maestria da forma em que o alvo esta AGORA.",
			() => C?.SendVerbo("admin_dominar", (C?.AlvoId ?? 0).ToString())));

		// O "Force God Ki" do `Godki_Settings()` (godkiattain.dm:76-77): desperta sem requisito e sem medidor.
		Verbos.Registrar(new Verbo("Awaken God Ki", Verbos.Admin,
			"Desperta o ki divino do alvo marcado, ignorando o requisito de BP. Sem alvo, o seu.",
			() => C?.SendVerbo("admin_godki", (C?.AlvoId ?? 0).ToString())));

		// ---------------------------------------------------------- mundo e servidor
		// `Invisible()` (Admin.dm:332) -- usa a mesma invisibilidade da tecnica, sem segundo caminho
		Verbos.Registrar(new Verbo("Toggle Invisible", Verbos.Admin,
			"Some da vista de todo mundo (e volta).",
			() => C?.SendVerbo("admin_invisivel")));

		// `Restaurar_Planeta()` (Planets.dm:258), na escala que este port tem: a zona atual
		Verbos.Registrar(new Verbo("Restore Scenery", Verbos.Admin,
			"Refaz as paredes e o chao que os golpes derrubaram nesta zona.",
			() => C?.SendVerbo("admin_consertar_cenario")));

		// `SaveAll()` (Admin.dm:178) / `Force_All_Backup_Save()` (Misc.dm:437)
		Verbos.Registrar(new Verbo("Save All", Verbos.Admin,
			"Grava agora todos os personagens, o mundo e os cargos.",
			() => C?.SendVerbo("admin_salvar")));

		// A FICHA DO CEU no chat. O painel da aba mostra o TIPO, que muda devagar; os numeros
		// vivos (forca, cinza, quanto a nuvem encobre) so fazem sentido lidos no instante em que
		// se pede -- por isso saem no chat e nao numa pagina que se remonta sozinha.
		Verbos.Registrar(new Verbo("Weather Report", Verbos.Admin,
			"O ceu desta zona agora: o que esta caindo, com que forca, e o que pode cair aqui.",
			() => C?.SendVerbo("admin_clima_ficha")));

		// Forcar um clima pede DOIS argumentos (qual e quao forte) e mora no painel da aba, junto
		// do seletor. Soltar nao pede nenhum, entao cabe aqui -- e e a saida rapida de quem
		// deixou uma nevasca ligada.
		Verbos.Registrar(new Verbo("Release Weather", Verbos.Admin,
			"Solta o ceu desta zona: o clima volta a ser sorteado pelo relogio do mundo.",
			() => C?.SendVerbo("admin_clima_natural")));

		// ============================ A DESTRUICAO DE PLANETA, PELO LADO DO ADMIN ============================
		// Os quatro que faltavam pro sistema fechar. O `Restore Planet` e o `Restaurar_Planeta` do
		// original (`Planets.dm:254-280`) e continua sendo a **unica volta que existe** neste port.
		//
		// O MOTIVO MUDOU DE METADE: as Esferas do Dragao existem agora (`GameServer.Esferas.cs`), e o
		// que ainda falta e a TABELA DE DESEJOS -- a Fase 2. Quando ela chegar, "Heal Planet" tem que
		// entrar pelo `RessuscitarPlaneta` do servidor e nao por um caminho proprio; a decisao esta
		// escrita la, com o defeito do original anotado.
		// ==============================================================================================
		Verbos.Registrar(new Verbo("Villain (target)", Verbos.Admin,
			"Liga/desliga o bit de VILAO do alvo marcado. E o unico jeito de alguem aprender "
			+ "Planet Destroy -- no original tambem e o admin quem designa.",
			() => C?.SendVerbo("admin_vilao", (C?.AlvoId ?? 0).ToString())));

		// (2026-10-16) O VILAO DE SAGA NO INFERNO, sem esperar o sorteio -- ver `Server/GameServer.ViloesDoInferno.cs`.
		Verbos.Registrar(new Verbo("Summon Hell Villain", Verbos.Admin,
			"Faz um vilão de saga já derrotado escapar no Inferno agora (sem nenhum derrotado ainda, vem um da cadeia, pro teste). "
			+ "Quem o derrotar ganha zeni e karma. Um por vez.",
			() => C?.SendVerbo("admin_inferno")));

		Verbos.Registrar(new Verbo("Kill This Planet (slow)", Verbos.Admin,
			"Acende o pavio lento deste planeta: 20 minutos em quatro estagios, e no fim ele explode. "
			+ "Os habitantes vao morrendo e o chao comeca a se abrir.",
			() => C?.SendVerbo("admin_morte_lenta")));

		Verbos.Registrar(new Verbo("Destroy This Planet", Verbos.Admin,
			"Pula o pavio: cinco minutos de tremor e o planeta acaba. Quem estiver aqui com BP "
			+ "expresso abaixo do seu leva 99 em cada membro; nocauteado morre; o resto vai pro espaco.",
			() => C?.SendVerbo("admin_destruir_planeta")));

		Verbos.Registrar(new Verbo("Abort Planet Death", Verbos.Admin,
			"Apaga o pavio deste planeta antes do fim. So funciona ANTES da explosao terminar.",
			() => C?.SendVerbo("admin_abortar_morte")));

		Verbos.Registrar(new Verbo("Restore Planet", Verbos.Admin,
			"Tira ESTE planeta (ou o que esta sob voce, no espaco) da lista de mortos -- ele volta a "
			+ "existir, a se povoar e a receber pouso. De longe, use o cartao \"Planetas destruidos\" desta aba.",
			() => C?.SendVerbo("admin_restaurar_planeta")));

		// ---------------------------------------------------------- torneio
		// O dono (2026-09-07): "verbs pra adm que permita eu forcar torneios etc pra eu poder testar
		// ingame". Nenhum deles tem caminho proprio: sao os mesmos `trn_*` que a agenda e o painel do
		// convite usam, so que apertados na hora. Ver `GameServer.Torneio.cs:ComandoDeTorneio`.
		Verbos.Registrar(new Verbo("Torneio: Forcar Terra", Verbos.Admin,
			"Abre AGORA um Torneio de Artes Marciais na Terra: convites, inscricoes, NPCs nas vagas que "
			+ "sobrarem. O proximo automatico e remarcado a partir de hoje.",
			() => C?.SendVerbo("trn_iniciar", "terra")));

		Verbos.Registrar(new Verbo("Torneio: Forcar Outro Mundo", Verbos.Admin,
			"Abre AGORA o Torneio do Outro Mundo -- so quem esta morto no Alem se inscreve.",
			() => C?.SendVerbo("trn_iniciar", "alem")));

		Verbos.Registrar(new Verbo("Torneio: Pular Espera", Verbos.Admin,
			"Pula a espera da fase de agora: fecha as inscricoes ja, zera a contagem, corta o intervalo. "
			+ "Numa luta, encerra por pontos (vence quem tem mais vida). Aperte de novo a cada fase.",
			() => C?.SendVerbo("trn_avancar")));

		Verbos.Registrar(new Verbo("Torneio: Inscrever Alvo", Verbos.Admin,
			"Inscreve quem esta marcado (ou alguem da lista) no torneio aberto, sem ele responder ao convite.",
			() => NoAlvoDeAdmin("trn_inscrever", "Torneio: Inscrever Alvo")));

		Verbos.Registrar(new Verbo("Torneio: Agenda", Verbos.Admin,
			"Quando e o proximo da Terra e o do Outro Mundo, e em que fase esta o de agora.",
			() => C?.SendVerbo("trn_agenda")));

		Verbos.Registrar(new Verbo("Torneio: Terra Em 1 Min", Verbos.Admin,
			"Marca o proximo Torneio da Terra pra daqui a um minuto -- e a AGENDA quem abre, com convite e "
			+ "tudo, como faria no dia marcado. Serve pra testar o caminho automatico.",
			() => C?.SendVerbo("trn_agenda", "terra 1")));

		Verbos.Registrar(new Verbo("Torneio: Outro Mundo Em 1 Min", Verbos.Admin,
			"Marca o Torneio do Outro Mundo pra daqui a um minuto, pela agenda.",
			() => C?.SendVerbo("trn_agenda", "alem 1")));

		Verbos.Registrar(new Verbo("Torneio: Cancelar", Verbos.Admin,
			"Cancela o torneio em andamento: solta todo mundo, remove os NPCs, ninguem e premiado.",
			() => C?.SendVerbo("trn_cancelar")));

		// ---------------------------------------------------------- bills e whis
		// NAO TEM ORIGINAL: o Bills e o Whis sao do port (dono, 2026-10-11). A historia deles espera a saga de Boo e
		// depois um MES in-game -- estes cinco existem pra conferi-la sem esperar. Todos caem no mesmo
		// `GameServer.AdminBills`. O que RECOMECA e o ultimo de proposito: a `--diagadmin` dispara todos em ordem.
		Verbos.Registrar(new Verbo("Bills: Estado", Verbos.Admin,
			"Em que pe a historia do Bills esta (dormindo, a caminho e em que dia pousa, na Terra, em casa), se ele e "
			+ "o Whis existem agora, e o endereco da casa.",
			() => C?.SendVerbo("admin_bills", "estado")));

		Verbos.Registrar(new Verbo("Bills: Avancar A Historia", Verbos.Admin,
			"Empurra UM passo: dormindo -> a caminho (como se Boo tivesse caido) -> pousa na Terra (como se o mes "
			+ "tivesse passado) -> derrubado (ele conta onde mora e vai pra casa). Aperte de novo a cada passo.",
			() => C?.SendVerbo("admin_bills", "avancar")));

		Verbos.Registrar(new Verbo("Bills: Ir Para A Casa", Verbos.Admin,
			"Salta pra orbita do mundo onde o Bills mora. So no espaco; encoste nele pra pousar. A gravidade de la e "
			+ "altissima -- use \"Bills: Aguentar A Casa\" antes.",
			() => C?.SendVerbo("admin_bills", "casa")));

		Verbos.Registrar(new Verbo("Bills: Aguentar A Casa", Verbos.Admin,
			"Sobe a SUA maestria de gravidade ate a do mundo do Bills (nunca desce), pra voce pousar la sem ser esmagado.",
			() => C?.SendVerbo("admin_bills", "aclimatar")));

		Verbos.Registrar(new Verbo("Bills: Recomecar A Historia", Verbos.Admin,
			"A historia do Bills volta a dormir e os dois saem de onde estiverem. Se a saga de Boo ja foi vencida, ele "
			+ "desperta de novo sozinho. Nao mexe no titulo de ninguem.",
			() => C?.SendVerbo("admin_bills", "reiniciar")));
	}

	/// <summary>Zera o registro -- o personagem trocou (volta ao menu de slots).</summary>
	public static void Limpar() => _registrados = false;
}
