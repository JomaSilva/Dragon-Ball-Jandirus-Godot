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

	private static void NoAlvo(string cmd)
	{
		int id = C?.AlvoId ?? 0;
		if (id == 0) { Chat.Sistema("marque alguem antes (duplo clique nele)."); return; }
		C?.SendVerbo(cmd, id.ToString());
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
			() => C?.SendVerbo("graus")));

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
			() => NoAlvo("cargo_nomear")));

		Verbos.Registrar(new Verbo("Accept Elder Seat", Verbos.Outros,
			"Aceita o assento de Anciao que te ofereceram.",
			() => C?.SendVerbo("cargo_nomear_aceitar")));

		Verbos.Registrar(new Verbo("Decline Elder Seat", Verbos.Outros,
			"Recusa o assento de Anciao.",
			() => C?.SendVerbo("cargo_nomear_recusar")));

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
			() => C?.SendVerbo("juventude_aceitar")));

		Verbos.Registrar(new Verbo("Decline Youth", Verbos.Outros,
			"Recusa a oferta de voltar a ser jovem.",
			() => C?.SendVerbo("juventude_recusar")));

		Verbos.Registrar(new Verbo("Name Heir", Verbos.Outros,
			"So o Rei de Vegeta: poe quem esta marcado na linha de sucessao do trono.",
			() => NoAlvo("cargo_herdeiro")));

		Verbos.Registrar(new Verbo("Remove Heir", Verbos.Outros,
			"Tira quem esta marcado da linha de sucessao de Vegeta.",
			() => NoAlvo("cargo_herdeiro_tirar")));

		Verbos.Registrar(new Verbo("Line of Succession", Verbos.Outros,
			"Quem herda o trono de Vegeta, na ordem.",
			() => C?.SendVerbo("cargo_herdeiros")));

		Verbos.Registrar(new Verbo("Challenge God of Destruction", Verbos.Outros,
			"Desafia FORMALMENTE o portador do titulo. Exige God Ki desperto; nocaute decide.",
			() => C?.SendVerbo("cargo_desafiar")));

		Verbos.Registrar(new Verbo("Accept Challenge", Verbos.Outros,
			"Aceita agora o duelo pelo titulo.",
			() => C?.SendVerbo("cargo_duelo_aceitar")));

		Verbos.Registrar(new Verbo("Postpone Challenge", Verbos.Outros,
			"Adia o duelo. O terceiro adiamento na mesma semana e covardia e custa o titulo.",
			() => C?.SendVerbo("cargo_duelo_adiar")));

		Verbos.Registrar(new Verbo("Title Status", Verbos.Outros,
			"O titulo de God of Destruction: quem carrega, a tarefa em aberto, falhas e prazos.",
			() => C?.SendVerbo("cargo_titulo")));

		// ============================ OS DEVERES DO CARGO ============================
		// O `Meu Rank` do original. Sem ele o motor de tarefas seria invisivel: o prazo so apareceria
		// quando ja tivesse vencido, e a destituicao chegaria como castigo sem aviso. Ver
		// `GameServer.CargoMissoes.cs`.
		Verbos.Registrar(new Verbo("Rank Duty", Verbos.Outros,
			"Os deveres do seu cargo: a tarefa em aberto, o prazo em dias in-game, as falhas e o renome.",
			() => C?.SendVerbo("cargo_deveres")));

		Verbos.Registrar(new Verbo("Fund Earth", Verbos.Outros,
			"So o Presidente da Terra, e so com a tarefa de verba aberta: deposita a verba no cofre da Terra.",
			() => C?.SendVerbo("cargo_verba")));

		// =====================================================================
		// A SALA DO TEMPO -- os dois verbs do GUARDIAO DA TERRA
		//
		// A CHAVE E O RESGATE NASCEM JUNTOS, e os dois sao do mesmo cargo. O `Core/Ranks/Ranks.cs`
		// ja descrevia o Guardiao como quem "faz chaves da Sala do Tempo e autoriza a entrada", e
		// nenhum dos dois verbos existia -- era promessa escrita e nao ligada.
		//
		// ELES APARECEM PRA TODO MUNDO de proposito: quem nao e Guardiao le a recusa e descobre que
		// a Sala do Tempo tem dono. Esconder ensinaria menos, e a permissao e do servidor de
		// qualquer jeito (ver `GameServer.SalaDoTempo.cs`).
		Verbos.Registrar(new Verbo("Time Chamber: Authorize", Verbos.Outros,
			"So o Guardiao da Terra. Da ao alvo marcado UMA entrada na Sala do Tempo.",
			() => NoAlvo("sala_autorizar")));

		Verbos.Registrar(new Verbo("Time Chamber: Release", Verbos.Outros,
			"So o Guardiao da Terra. Destranca a porta pra quem ficou preso la dentro.",
			() => NoAlvo("sala_soltar")));

		Verbos.Registrar(new Verbo("Tech Catalog", Verbos.Outros,
			"O catalogo de construcoes, com o custo e o motivo de cada nao.",
			() => C?.SendTech("lista")));

		Verbos.Registrar(new Verbo("Bolt", Verbos.Outros,
			"Aparafusa (ou solta) a construcao ao seu lado. Sem aparafusar, ela nao funciona.",
			() => C?.SendTech("aparafusar")));

		Verbos.Registrar(new Verbo("Study", Verbos.Outros,
			"Debruca-se sobre a bancada de pesquisa. E o unico jeito de ganhar tecnologia.",
			() => C?.SendTech("estudar")));

		Verbos.Registrar(new Verbo("Who", Verbos.Outros,
			"Quem esta no mundo agora, e onde.",
			() => C?.SendVerbo("quem")));

		// =====================================================================
		// O CONVIVIO -- os verbs de `Contacts.dm` e `Friendship.dm`
		//
		// OS TRES QUE PEDEM ALGUEM AGEM SOBRE O ALVO MARCADO, e nao sobre um nome digitado. No
		// BYOND eles eram `set src in oview(N)` -- "aquele ali, o que eu estou vendo" --, e o gesto
		// equivalente aqui e o duplo clique que o jogador ja usa pra tudo. O ALCANCE de cada um
		// continua sendo o de la, conferido no servidor: 3 tiles pra pedir amizade, 5 pra declarar
		// rival, 10 pra anotar alguem.
		//
		// A DECLARACAO DE RELACAO NAO ESTA AQUI de proposito: ela nao pede presenca (no original
		// mora no proprio Contact, que e seu e funciona com a pessoa offline) e precisa escolher
		// ENTRE NOVE opcoes com custo diferente. Isso e uma tela, nao um botao -- ela vive na aba
		// People, ao lado da pessoa a que se refere.
		// =====================================================================
		Verbos.Registrar(new Verbo("Remember Person", Verbos.Outros,
			"Guarda o rosto de quem voce marcou. Ele passa a aparecer na aba People.",
			() => NoAlvo("conhecer"), TemAlvo));

		Verbos.Registrar(new Verbo("Request Friendship", Verbos.Outros,
			"Pede amizade a quem voce marcou. Conviver aproxima ate certo ponto -- so um pedido "
			+ "aceito faz de alguem um amigo de verdade.",
			() => NoAlvo("amizade_pedir"), TemAlvo));

		Verbos.Registrar(new Verbo("Declare Rival", Verbos.Outros,
			"Declara (ou desfaz) rivalidade com quem voce marcou. Os golpes dele em voce e nos seus "
			+ "amigos passam a alimentar odio.",
			() => NoAlvo("rival"), TemAlvo));

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
		// pediu pra nao trazer; e os que dependem de sistema nao portado (torneio, esferas, fusao,
		// magia, dungeons).
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
			"Cura o alvo marcado por completo -- membros inclusive. Sem alvo, cura voce.",
			() => C?.SendVerbo("admin_curar", (C?.AlvoId ?? 0).ToString())));

		// `MassRevive()` (Admin.dm:294) aplicado a um so: de pe, inteiro, Ki cheio.
		Verbos.Registrar(new Verbo("Revive Target", Verbos.Admin,
			"Poe o alvo marcado de pe, mesmo morto. Sem alvo, revive voce.",
			() => C?.SendVerbo("admin_reviver", (C?.AlvoId ?? 0).ToString())));

		Verbos.Registrar(new Verbo("Knock Out Target", Verbos.Admin,
			"Poe o alvo marcado no chao.",
			() => NoAlvo("admin_kb"), TemAlvo));

		// o `Kill()` do original (Admin.dm:350)
		Verbos.Registrar(new Verbo("Kill Target", Verbos.Admin,
			"Mata o alvo marcado. Ele renasce pelo caminho normal.",
			() => NoAlvo("admin_matar"), TemAlvo));

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
			() => NoAlvo("admin_ir"), TemAlvo));

		Verbos.Registrar(new Verbo("Bring Target", Verbos.Admin,
			"Traz o alvo marcado ate voce.",
			() => NoAlvo("admin_trazer"), TemAlvo));

		// `MassSummon()` (Admin.dm:305)
		Verbos.Registrar(new Verbo("Bring Everyone", Verbos.Admin,
			"Traz TODO MUNDO do servidor pra onde voce esta.",
			() => C?.SendVerbo("admin_trazer_todos")));

		// `Return_Mob_To_Spawn()` (SpawnPoints.dm:37) / `Send_Spawn()` (Admin.dm:30)
		Verbos.Registrar(new Verbo("Send Target To Spawn", Verbos.Admin,
			"Manda o alvo marcado de volta ao ponto de partida.",
			() => NoAlvo("admin_spawn_alvo"), TemAlvo));

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
			() => NoAlvo("admin_sala_soltar"), TemAlvo));

		// ---------------------------------------------------------- enxergar
		// O `Assess()` (Assess.dm) -- o verb de admin que mais importa: e o unico jeito de ler o BP
		// alheio sem scouter, e sem ele nao ha como julgar uma denuncia de trapaca.
		Verbos.Registrar(new Verbo("Assess Target", Verbos.Admin,
			"A ficha COMPLETA do alvo marcado, sem sigilo: BP, forma, maestrias, corpo, conta.",
			() => C?.SendVerbo("admin_ficha", (C?.AlvoId ?? 0).ToString())));

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

		// ---------------------------------------------------------- punir
		// `Boot()` (Admin.dm:450)
		Verbos.Registrar(new Verbo("Boot Target", Verbos.Admin,
			"Desconecta o alvo marcado. Ele pode voltar.",
			() => NoAlvo("admin_expulsar"), TemAlvo));

		// `Mute()`/`UnMute()` (Admin.dm:459-472). Alterna: o mesmo botao cala e descala.
		Verbos.Registrar(new Verbo("Mute Target", Verbos.Admin,
			"Cala (ou descala) o alvo marcado. Vale pra conta, nao so pro personagem.",
			() => NoAlvo("admin_calar"), TemAlvo));

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
			"Tira este planeta da lista de mortos -- ele volta a existir, a se povoar e a receber "
			+ "pouso. E a unica volta que existe: nao ha desejo de Dragon Ball neste port.",
			() => C?.SendVerbo("admin_restaurar_planeta")));
	}

	/// <summary>Zera o registro -- o personagem trocou (volta ao menu de slots).</summary>
	public static void Limpar() => _registrados = false;
}
