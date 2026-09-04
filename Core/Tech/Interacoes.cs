namespace Jandirus.Core.Tech;

/// <summary>
/// O QUE DA PRA FAZER COM CADA COISA DO MUNDO -- o catalogo de interacoes.
///
/// ============================ POR QUE ISTO EXISTE ============================
/// No original nao ha tecla de interagir: cada objeto do mapa carrega os proprios verbs, e o
/// jogador os alcanca por clique direito (`set src in oview(1)`). Funciona, e e invisivel -- nada
/// na tela diz que aquela arvore tem maca, que aquele console e um banco, ou que aquela bancada
/// aceita estudo. Descobre-se clicando com o botao direito em tudo.
///
/// O dono pediu outra coisa, e melhor: "chegar perto do objeto e apertar E, assim vai abrir uma
/// tela q seria um menu te interaçoes possiveis daquele objeto, isso serve pra tudo". Uma porta
/// so, sempre a mesma, pra qualquer coisa com que se possa mexer.
///
/// PORTAS FICAM DE FORA, por pedido dele e porque ja tem o gesto certo: elas abrem por ENCOSTAR.
/// Pedir um menu de duas opcoes pra atravessar um corredor seria trocar um gesto bom por um passo.
/// =============================================================================
///
/// ============================ ELE E DADO, E NAO CODIGO ESPALHADO ============================
/// A lista de acoes de cada tipo vive AQUI, num lugar so, e nao dentro do widget que a desenha nem
/// do metodo que a executa. Isso importa por dois motivos:
///
///   * O CLIENTE E O SERVIDOR LEEM A MESMA TABELA. O menu mostra o que o servidor aceita, porque e
///     a mesma lista -- nao ha como o botao existir e o comando nao, nem o contrario.
///   * ACRESCENTAR UMA MAQUINA E ACRESCENTAR UMA LINHA. Nenhuma tela precisa saber que um banco
///     existe; ela pergunta ao catalogo o que aquele tipo faz.
/// ============================================================================================
/// </summary>
public static class Interacoes
{
	/// <summary>
	/// UMA ACAO POSSIVEL: o que o botao diz, o verbo que ele manda, e o argumento.
	///
	/// O VERBO E O MESMO CANAL QUE JA EXISTE (`C2S.Verbo`), e nao um pacote novo. Os comandos de
	/// banco, de estudo e de construcao ja viajavam por ele -- o menu so passa a ser uma segunda
	/// porta pros mesmos verbos, em vez de um sistema paralelo que faria as mesmas coisas por
	/// outro caminho e divergiria na primeira mudanca.
	/// </summary>
	public readonly record struct Acao(string Rotulo, string Verbo, string Arg = "", string Dica = "",
									   Forma Forma = Forma.Direta, double Min = 0, double Max = 0);

	/// <summary>
	/// COMO A ACAO PERGUNTA. E o que o DM fazia com `input()`, e cada tipo dele vira um destes.
	///
	/// ============================ O MENU PRECISAVA APRENDER A PERGUNTAR ============================
	/// A primeira versao so sabia mandar um verbo e fechar. Isso cobre "ver saldo" e "colher", mas
	/// nao cobre a maquina de gravidade, que e o caso que o dono descreveu: "se clicar no mudar
	/// gravidade vai abrir uma caixa parecida com uma calculadora so q so tem os numero de 0 a 9".
	///
	/// No original essas perguntas sao `input(...) as num` e `input(...) in list(...)` -- caixas de
	/// dialogo do BYOND. Aqui elas viram uma segunda pagina do mesmo menu, porque abrir uma janela
	/// do sistema no meio de um jogo em tela cheia e o tipo de coisa que rouba o foco e nao volta.
	/// ===============================================================================================
	/// </summary>
	public enum Forma : byte
	{
		/// <summary>Manda o verbo e fecha. O caso comum.</summary>
		Direta = 0,

		/// <summary>Abre outra lista de acoes -- as do `Arg` como chave de submenu.</summary>
		Submenu = 1,

		/// <summary>
		/// Abre o teclado numerico. O numero digitado vira o ARGUMENTO do verbo, e
		/// <see cref="Acao.Min"/>/<see cref="Acao.Max"/> sao a faixa que o teclado aceita.
		/// </summary>
		Numero = 2,
	}

	private static readonly Acao[] Nenhuma = [];

	/// <summary>
	/// A QUE DISTANCIA A COISA RESPONDE, em pixels -- **o unico numero de alcance deste gesto**.
	///
	/// ============================ POR QUE ELE MUDOU DE CASA ============================
	/// Ele existia em TRES casas com dois valores: `MenuDeInteracao.Alcance` (64) desenhava o menu,
	/// `GameServer.AlcanceDeUso` (64) aceitava o verbo, e `NaveGrande.AlcanceDaPeca` (48) media o
	/// console da ponte e a plataforma de saida. Os dois primeiros concordavam por acaso; o terceiro
	/// nao, e a diferenca era uma BANDA MORTA de 16 px: entre 48 e 64 px do console o cliente abria o
	/// menu e o servidor recusava com "você precisa estar no console da ponte". Duas celulas
	/// ortogonais dao exatamente 64 px -- ou seja, o defeito nao era teorico.
	///
	/// E e o defeito que o proprio `MenuDeInteracao` diz que nao pode acontecer: "um menu que abre a
	/// tres tiles mandaria um comando que o servidor recusa por distancia, e o jogador leria 'longe
	/// demais' olhando pra um botao que o jogo acabou de oferecer".
	///
	/// Sao dois tiles. No DM cada objeto declara o seu (`set src in oview(1)` = um tile); aqui a
	/// regra e uma so pra tudo com que se possa mexer, porque o GESTO e um so.
	/// ==================================================================================
	/// </summary>
	public const float Alcance = 64f;

	/// <summary>
	/// AS ACOES DE UM TIPO DE OBRA. O `tipo` e o id do catalogo, que e a folha do typepath do DM
	/// ("Bank", "Research_Station", "AppleTree").
	/// </summary>
	public static Acao[] De(string tipo) => tipo switch
	{
		"Bank" =>
		[
			new Acao("Ver saldo", "banco_ver", "", "quanto você tem no bolso e no cofre"),
			new Acao("Depositar tudo", "banco_depositar", "", "guarda o zeni do bolso -- ninguém rouba do cofre"),
			new Acao("Sacar tudo", "banco_sacar", "", "tira o zeni do cofre pro bolso"),
		],

		// SEM "Fabricar..." desde 2026-09-03: fabricar mora na aba Tech do menu P (o catalogo com icone,
		// custo e o motivo de cada recusa). A bancada e onde se ESTUDA -- e o que se fabrica sai da
		// tecnologia estudada, nao do lugar em que se esta.
		"Research_Station" =>
		[
			new Acao("Estudar", "estudar", "", "fica debruçado na bancada ganhando tecnologia"),
			new Acao("Pegar", "pegar", "", "recolhe a bancada pra mochila (só a sua)"),
		],

		// ============================ O ENMA DAIOH, NA MESA DELE ============================
		// No DM e um `Click()` a ate 2 tiles do NPC (`SkyNPCs.dm:242-248`) que abre um `input()` com
		// quatro opcoes (`:159-167`). Aqui e o menu E da obra fixa que o servidor semeia no Outro Mundo
		// (`GameServer.Alem.SemearOEnma`). So a volta paga esta portada; reencarnar a 10% e o treino
		// com o Sr. Kaioh sao as proximas (o dono: "o treinamento do kaioh faremos depois").
		"Enma_Daioh" =>
		[
			new Acao("Ouvir o Enma", "enma_ouvir", "", "ele lê a sua ficha e diz o preço da volta"),
			new Acao("Voltar à vida (1.000.000 de zeni)", "enma_reviver", "", "o corpo volta fraco: BP a 25% por uma hora"),
		],

		"AppleTree" =>
		[
			new Acao("Colher uma maçã", "colher", "", "a árvore leva um tempo pra repor"),
		],

		// ============================ A MAQUINA DE GRAVIDADE ============================
		// Tres portas, como no original: o Click abre o painel de gravidade (`input as num`), o
		// verb Upgrade abre a lista de melhorias, e o Info escreve o estado no chat.
		//
		// O TETO NA ACAO E O GLOBAL (500), e nao o desta maquina: quem sabe o `Max` de UMA maquina
		// e o servidor, que tem o estado dela -- o catalogo e estatico e vale pra todas. O teclado
		// impede de digitar 9000, e o servidor apara o resto contra o `Max` real. Duas guardas, e a
		// de baixo e a que conta.
		"Gravity" =>
		[
			new Acao("Ajustar gravidade", "grav_definir", "", "afeta todo mundo dentro do campo",
					 Forma.Numero, 0, TetoDeGravidade),
			new Acao("Melhorar...", "grav_upgrades", "upgrades", "gasta zeni pra empurrar os limites",
					 Forma.Submenu),
			new Acao("Ver estado", "grav_info", "", "força, bateria, alcance"),
			new Acao("Pegar", "pegar", "", "recolhe a máquina pra mochila (só a sua)"),
		],

		// O SUBMENU DE MELHORIAS -- a lista do `verb/Upgrade` do DM (Gravity.dm:224-276).
		//
		// OS PRECOS NAO ESTAO AQUI de proposito: cada um depende do estado ATUAL da maquina
		// (`5*Max`, `50*MaxEnergy`, `500*(Range+1)`), e o catalogo nao tem esse estado. Quem escreve
		// o preco na tela e o servidor, no `grav_info`; aqui ficam so os nomes e o que cada um faz.
		"Gravity/upgrades" =>
		[
			new Acao("Força do campo", "grav_up", "forca", "multiplica por 1,2 o teto desta máquina"),
			new Acao("Bateria", "grav_up", "bateria", "dobra a carga e enche na hora"),
			new Acao("Alcance", "grav_up", "alcance", "mais um tile de raio (o teto é 10)"),
			new Acao("Estabilização", "grav_up", "estabilidade", "uma vez só, e cara"),
			new Acao("Regeneração de nanites", "grav_up", "nanites", "recarrega sozinha quando desligada"),
		],

		// ============================ O MAINFRAME E OS DOIS LABORATORIOS ============================
		// No original sao TRES objetos: o mainframe vira um `DNALab/Android` ou um `DNALab/Bio` na
		// instalacao, e cada um tem os proprios verbs. No port o mainframe CONTINUA sendo a mesma
		// obra e guarda qual laboratorio foi instalado (`Obra.Lab`).
		//
		// POR ISSO O MENU OFERECE TUDO, e quem recusa e o servidor. Ele ja sabia recusar -- os
		// verbos `lab_androide`, `androide_absorcao`, `colher_dna` e `gestar` conferem raca,
		// tecnologia, zeni e qual lab esta instalado desde que existem. Filtrar aqui exigiria que o
		// catalogo conhecesse o estado de UMA obra, e ele e estatico e vale pra todas.
		// =============================================================================================
		"Android_Creation_Mainframe" =>
		[
			new Acao("Aparafusar / soltar", "aparafusar", "", "instalação exige o mainframe no chão"),
			new Acao("Instalar laboratório...", "lab_menu", "labs", "escolha definitiva, e consome o mainframe",
					 Forma.Submenu),
			new Acao("Converter meu corpo...", "and_menu", "androide", "só no Android Lab", Forma.Submenu),
			new Acao("Colher DNA de um nocauteado", "colher_dna", "", "só no Bio-Android Lab"),
			new Acao("Gestar bio-androide", "gestar", "", "leva um mês in-game"),
		],

		"Android_Creation_Mainframe/labs" =>
		[
			new Acao("Android Lab", "lab_androide", "", "transforma VOCÊ em androide"),
			new Acao("Bio-Android Lab", "lab_bio", "", "cria um bio-androide a partir de DNAs"),
		],

		"Android_Creation_Mainframe/androide" =>
		[
			new Acao("Androide de Absorção", "androide_absorcao", "", "suga ataques de ki"),
			new Acao("Androide de Energia Infinita", "androide_infinito", "", "nunca cansa nem come"),
		],

		// ============================ AS MAQUINAS QUE SO PRECISAM DE UM BOTAO ============================
		// Regenerador e campo bio trabalham SOZINHOS -- no original o regenerador cura quem esta em
		// cima dele, e o campo cura tudo num raio. Nenhum dos dois tem verb de "usar": eles so
		// precisam estar ligados e aparafusados.
		//
		// Por isso a unica acao deles e o aparafusar, e o "ver estado" -- que existe pra a maquina
		// poder DIZER que esta funcionando. Uma maquina que cura em silencio e uma maquina que o
		// jogador acha quebrada.
		// =================================================================================================
		"Regenerator" or "Bio_Field" =>
		[
			new Acao("Aparafusar / soltar", "aparafusar", "", "só funciona aparafusada"),
			new Acao("Ver estado", "maq_info", ""),
		],

		// ============================ A PORTA DA SALA DO TEMPO ============================
		// ELA E A UNICA "PORTA" DESTE MENU, e a excecao tem motivo. O cabecalho desta classe diz
		// que portas ficam de fora porque ja tem o gesto certo -- elas abrem por ENCOSTAR. Esta
		// NAO abre: no DM o `Enter()` do `tohbtc` devolve **0** e chama `htc_try_enter()`, que
		// confere autorizacao do Guardiao e recarga de 24 h antes de mover o corpo. Ela e um
		// objeto que RESPONDE com desenho de porta, e o gesto certo pra ela e mesmo o menu.
		//
		// A SEGUNDA ACAO NAO E ENFEITE. A sala PRENDE quem passa de 50 minutos (regra do dono
		// 13.6c), e um custo que so se descobre do lado de dentro e uma pegadinha. Ler as regras
		// e a metade "aviso claro" da decisao sobre esse risco -- ver `GameServer.SalaDoTempo.cs`.
		"Time_Chamber_Door" =>
		[
			new Acao("Entrar", "sala_entrar", "", "precisa da chave do Guardião da Terra"),
			new Acao("Ler as regras da porta", "sala_regras", "",
					 "o que a sala cobra, e o que acontece se você demorar pra sair"),
		],

		// ============================ A COMIDA DA SALA DO TEMPO ============================
		// Duas porcoes nascem perto da porta enquanto a sessao rende, e uma nova so aparece quando
		// alguem come uma (regra do dono 13.6b -- reposicao por CONSUMO, nunca por relogio). Ver
		// `GameServer.SalaSessao.cs`.
		//
		// O VERBO E DA SALA (`sala_comer`) E NAO O `item_comer` DA MOCHILA, e a diferenca nao e
		// cosmetica: a porcao NAO passa pela mochila. Se passasse, dava pra encher a mochila de
		// comida da sala e sair com ela -- o teto de duas porcoes viraria um teto de duas por vez,
		// que e outra coisa.
		"Cooked_Meat" =>
		[
			new Acao("Comer", "sala_comer", "", "uma refeição inteira -- e outra aparece no lugar"),
		],

		"Punching_Bag" or "Punching_Machine" =>
		[
			new Acao("Treinar aqui", "treinar_saco", "", "bater no saco rende mais que treinar no ar"),
			new Acao("Aparafusar / soltar", "aparafusar", ""),
		],

		// ============================ A SPACEPOD, POUSADA ============================
		// Os verbs do `obj/Spacepod` do original que se usam DE FORA: `Use` (:119), `Upgrade` (:190)
		// e `Info` (:185). O `Pegar` nao e do DM -- la o pod e `SaveItem=1` e entra no `contents` por
		// outro caminho --, mas e a mesma porta que toda construcao deste port ja tem, e sem ela uma
		// nave assentada no lugar errado ficaria la pra sempre.
		//
		// `nave_lancar` NAO ESTA AQUI, e a razao e que ele nunca funcionou daqui: quem esta DE PE ao
		// lado do pod nao e `NaveDoPiloto`, e o `LancarNave` responde "ninguém está pilotando -- entre
		// na nave primeiro" (`GameServer.Nave.cs`). Depois de embarcar o pod sai da lista de
		// construcoes da zona e este menu deixa de existir -- ou seja, o botao era inalcancavel em
		// estado util. Ele mora no menu do VEICULO MONTADO, que e quando ele vale. Ver
		// <see cref="DoVeiculo"/>.
		"Spacepod" or "Personal_Spacepod" =>
		[
			new Acao("Entrar", "nave_usar", "", "pilotando, a nave anda com você e atravessa água e lava"),
			new Acao("Melhorar velocidade", "nave_melhorar", "", "cada degrau custa 1000 x a velocidade atual"),
			new Acao("Ver estado", "nave_info", "", "velocidade, o quanto ela corta a viagem, e o próximo preço"),
			new Acao("Pegar", "nave_pegar", "", "recolhe a nave pra mochila (só a sua)"),
		],

		// ============================ O FOGUETE, POUSADO ============================
		// Os verbs do `obj/Rocketship` que sao de VOO (`PlanetTech.dm:253, 293, 320`) -- `Channel` e
		// `Rocket_Speak` (o radio) ficam de fora porque o radio e outro sistema.
		//
		// ELE NAO TEM "melhorar": o `var/Speed` existe na declaracao dele (:250) e NENHUM verb o
		// altera -- nao ha `Upgrade` no foguete. Aqui vale a mesma coisa; o que ele tem a mais e o
		// `Recondicionar`, que a Spacepod nao tem porque a Spacepod nao se gasta -- e ele fica aqui,
		// e nao so no veiculo montado, porque um foguete gasto NAO deixa ninguem entrar (`UsarNave`
		// recusa): consertar de fora e o unico jeito de consertar.
		//
		// `nave_lancar` saiu daqui pelo mesmo motivo do pod -- ver acima.
		"Rocket_Ship" =>
		[
			new Acao("Entrar", "nave_usar", "", "o foguete anda com você enquanto estiver dentro"),
			new Acao("Recondicionar", "nave_recondicionar", "", "um foguete usado só voa de novo pagando 100.000z"),
			new Acao("Ver estado", "nave_info", "", "se ele ainda está inteiro, e o que a viagem faz"),
			new Acao("Pegar", "nave_pegar", "", "recolhe o foguete pra mochila (só o seu)"),
		],

		// ============================ A CAPITAL SHIP, POR FORA ============================
		// O `Click()` do `obj/PlayerShip` (`ShipVessel.dm:91-99`) faz UMA coisa: `board(usr)`. Todo o
		// resto dela (observar, pilotar, lancar) mora no computador da ponte, que so existe LA
		// DENTRO -- e esse e o desenho: a nave grande nao se dirige de fora.
		//
		// `nave_lancar` NAO esta aqui de proposito, e a diferenca com o pod e a coisa toda: o pod se
		// lanca de fora porque voce esta VESTINDO ele; esta se lanca da ponte.
		NaveGrande.Tipo =>
		[
			new Acao("Embarcar", "nave_embarcar", "", "entra no interior -- a ponte fica no canto"),
			new Acao("Ver estado", "nave_info", "", "casco, dono, e onde ela está"),
			// A SENHA E UM NUMERO, e o DM pede TEXTO (`input(...) as text|null`, `ShipVessel.dm:65`).
			// A troca nao e gosto: o menu deste port sabe fazer duas perguntas -- escolher da lista e
			// digitar numero (ver `Forma`) -- e nao sabe pedir texto. Inventar um terceiro tipo de
			// pergunta pra UM campo, que abriria caixa de texto por cima de um jogo em tela cheia,
			// custaria mais do que um cadeado de nave vale. ZERO DESTRANCA.
			new Acao("Trancar com senha", "nave_senha", "", "um código de até 6 dígitos -- 0 destranca (o dono nunca precisa dele)",
					 Forma.Numero, 0, 999999),
			new Acao("Pegar", "nave_pegar", "", "recolhe a nave pra mochila (só a sua, e só vazia)"),
		],

		// ============================ O COMPUTADOR DA PONTE ============================
		// Os tres verbs do `obj/ShipControl` (`ShipVessel.dm:295, 305, 350`), na ordem em que ele os
		// declara. Ver `GameServer.NaveGrande.cs` pro que cada um virou aqui -- em especial o
		// `Observe`, que no DM e uma camera (`client.eye = ship`) e aqui e a carta estelar.
		"Ship_Control" =>
		[
			new Acao("Observar", "nave_observar", "", "de fora da nave: onde ela está na galáxia"),
			new Acao("Pilotar", "nave_pilotar", "", "você assume o leme e a nave anda com você"),
			new Acao("Lançar", "nave_lancar", "", "do chão de um planeta pro espaço, em 10 segundos"),
			new Acao("Ver estado", "nave_info", "", "casco, dono e velocidade"),
			new Acao("Melhorar velocidade", "nave_melhorar", "", "cada degrau custa 1000 x a velocidade atual"),
		],

		// ============================ A PLATAFORMA DE SAIDA ============================
		// No DM ela dispara por PISAR (`Crossed`, `ShipVessel.dm:276-282`) e pergunta com um `alert`.
		// Aqui ela e um menu, e a troca e deliberada: um `alert` que abre sozinho toda vez que alguem
		// cruza o meio da propria nave e a coisa que mais irrita numa sala de 100x100 -- o proprio DM
		// precisou nascer o jogador um tile ao lado dela pra ela nao disparar na chegada.
		"Ship_Pad" =>
		[
			new Acao("Desembarcar", "nave_sair", "", "sai pra onde a nave estiver agora"),
		],

		// ============================ A LAPIDE ============================
		// Ela nasce do `GenerateCross` (`Corpse.dm:50-73`), e o sorteio de la -- `gravecon =
		// pick(1,2,3,4,5)` -- e o motivo de serem CINCO tipos e nao um: o estado do sprite, no port,
		// vem do catalogo por TIPO. Cinco linhas no `construcoes.json` sao o `switch` do original
		// escrito onde ele ja estaria; a alternativa era um campo `Estado` novo na `Obra`, que iria
		// pro disco em cada uma das 105 construcoes pra ser usado por uma so.
		//
		// UMA ACAO SO, E ELA E DE LEITURA. No DM a lapide nao tem verb nenhum -- ela e cenario com
		// `desc`, que se le por `Examine`. Aqui o `Examine` nao existe, e o epitafio ja esta no TITULO
		// do menu (ele viaja como nome da obra); este botao e o que lhe da um lugar no CHAT, onde ele
		// fica e onde quem passar depois pode rolar pra cima e ler.
		//
		// **NAO HA "Pegar"**, e a ausencia e regra: um tumulo que cabe na mochila e um tumulo que
		// alguem leva embora. Ele cai como toda obra cai -- na porrada (`Armadura.Bater`).
		"Grave_1" or "Grave_2" or "Grave_3" or "Grave_4" or "Grave_5" =>
		[
			new Acao("Ler a lápide", "lapide_ler", "", "o que ficou escrito de quem está enterrado aqui"),
		],

		_ => Nenhuma,
	};

	// =====================================================================
	// O CORPO NO CHAO
	// =====================================================================
	/// <summary>
	/// ============================ AS ACOES DE UM CADAVER -- a TERCEIRA fonte de alvo da tecla E ============================
	/// *"basta apertar E perto do corpo pra enterrar"* -- o pedido do dono, e esta e a metade dele que
	/// as duas pontas leem.
	///
	/// Ela e a irma do <see cref="DoVeiculo"/> e nasceu pelo mesmo buraco: **tudo neste catalogo e um
	/// objeto NO CHAO**, e o cliente acha o alvo varrendo a lista de construcoes da zona. Um cadaver
	/// nao esta nessa lista e nunca poderia estar -- ele e um CORPO, e corpo viaja pelo snapshot. Por
	/// isso o servidor DIZ qual esta ao alcance (`Protocol.S2C.Cadaver`) e esta funcao diz o que fazer
	/// com ele. **Nao ha menu novo** -- e a mesma tela, com outra pagina.
	/// ========================================================================================================================
	///
	/// ============================ POR QUE E UMA ACAO SO ============================
	/// O `obj/mobCorpse` do DM tem QUATRO verbs (`Corpse.dm:5-49`), e tres deles ficaram de fora com
	/// motivo, nao por corte:
	///
	///   * `Eat()` -- comer o cadaver. Depende do `CanEat` racial e de `currentNutrition += 30`; o
	///     estomago deste port existe, mas comer CORPO nao passa pela mochila nem pelo catalogo de
	///     itens, e o DM tambem larga um `/obj/items/food/corpse` separado no `mobDeath` -- ou seja, la
	///     sao duas coisas diferentes. Fica anotado como divida, e nao como esquecimento;
	///   * `Skin_Corpse()` -- esfolar. Depende da maestria `Life/Skinning`, do `skinlist` de cada molde
	///     e do `alchemyparts`: e o sistema de CRAFTING inteiro (`Skinning.dm`), que nao esta portado.
	///     Um botao aqui seria a porta de um comodo que nao existe;
	///   * `Destroy()` -- destruir. **Este esta portado, e nao por botao**: bater no corpo ate o fim o
	///     desfaz (ver `GameServer.Cadaver.EstaDestrocado`). Como verbo de menu ele seria um "apagar" de
	///     um clique ao lado de "enterrar", e a diferenca entre os dois -- que uma deixa lapide e a
	///     outra nao -- ficaria a cargo do jogador ler duas dicas parecidas.
	///
	/// **O AGARRAR NAO ESTA AQUI, E ISSO E O DESENHO**: *"vc pode AGARRAR o corpo e levar pra outro
	/// lugar"* ja funciona, pela TECLA DE AGARRAR, porque o agarrao foi escrito sobre CORPO e nao
	/// pergunta se o alvo esta vivo. Um botao "agarrar" neste menu seria uma segunda porta pro mesmo
	/// gesto -- e a que divergiria na primeira mudanca.
	/// ==============================================================================
	/// </summary>
	public static Acao[] DoCadaver() =>
	[
		new Acao("Enterrar", "enterrar", "", "abre uma cova e ergue uma lápide -- o corpo descansa aqui"),
	];

	// =====================================================================
	// O VEICULO EM QUE EU ESTOU
	// =====================================================================
	/// <summary>
	/// AS ACOES DA NAVE QUE ESTA EMBAIXO DE VOCE -- a segunda pagina que a tecla E precisava.
	///
	/// ============================ O BURACO QUE ISTO FECHA ============================
	/// Toda coisa deste catalogo e um objeto NO CHAO: o cliente acha o alvo varrendo a lista de
	/// construcoes da zona (`cli.Obras`). Uma nave PILOTADA nao esta nessa lista de proposito --
	/// ela deixou de estar no chao e passou a andar com o corpo (`NavesParadasEm` filtra
	/// `PilotoId == 0`) --, e quem esta AO LEME de uma Capital Ship esta a um mundo de distancia do
	/// console da ponte, que e a unica porta pros verbos dela.
	///
	/// Resultado, antes disto: o piloto era a unica pessoa do servidor que nao alcancava os comandos
	/// do proprio veiculo -- inclusive o de DESCER dele. Era isso que a aba Nav tapava com dezesseis
	/// botoes, e era por isso que ela existia.
	///
	/// Agora o alvo da tecla E pode ser o veiculo montado, e ele ganha a mesma porta que a macieira:
	/// o servidor diz QUAL nave esta embaixo de voce (<c>Protocol.S2C.Veiculo</c>) e esta funcao diz
	/// o que da pra fazer com ela. **Nao ha menu novo** -- e a mesma tela, com outra pagina.
	/// =================================================================================
	///
	/// ============================ POR QUE NAO E O MESMO `De` ============================
	/// Porque a MESMA nave oferece coisas diferentes conforme voce esteja fora ou dentro dela, e sao
	/// quase disjuntas: "Entrar" e "Pegar" so fazem sentido de fora, "Sair" e "Lançar" so fazem
	/// sentido de dentro. Uma lista so obrigaria o servidor a recusar metade dos botoes o tempo
	/// todo, que e exatamente o que o `nave_lancar` no menu do pod parado fazia.
	/// ==================================================================================
	///
	/// O SERVIDOR NAO CONSULTA ESTA TABELA, e nao poderia: os verbos `nave_*` nao passam pelo
	/// `ObraQueAceita` (a nave nao e uma `Obra` -- ver o cabecalho de `Nave`), e cada um deles ja
	/// confere sozinho de onde esta sendo chamado. Aqui a guarda contra cliente mexido continua
	/// sendo a de sempre e ela e a de la: `LancarNave` recusa quem nao pilota, `PilotarDaPonte`
	/// recusa quem nao esta no console, `MelhorarNave` recusa quem nao e dono.
	/// </summary>
	/// <param name="tipo">O id do catalogo da nave montada, como o servidor o mandou.</param>
	public static Acao[] DoVeiculo(string tipo) => tipo switch
	{
		// ============================ AO LEME DA CAPITAL SHIP ============================
		// O pedido do dono, em uma frase: *"ao apertar E DNV vc volta pra dentro da nave"*. E o
		// `return_to_interior` do DM (`ShipVessel.dm:124-136`) -- o mesmo verbo `nave_pilotar` que
		// assumiu o leme o larga, e o servidor ja sabe disso (ver o escape do `ComandoDaNaveGrande`).
		//
		// "Observar" NAO ESTA AQUI: ele e do CONSOLE (`set src in oview(1)`, :296), e do leme voce
		// esta do lado de fora do casco -- de onde a carta estelar ja mostra voce mesmo. Oferecer o
		// botao aqui seria oferecer a recusa.
		NaveGrande.Tipo =>
		[
			new Acao("Voltar à ponte", "nave_pilotar", "", "larga o leme e devolve você pra dentro, ao lado do console"),
			new Acao("Lançar", "nave_lancar", "", "sobe ao espaço com a nave inteira e quem estiver dentro dela"),
			new Acao("Ver estado", "nave_info", "", "casco, dono e velocidade"),
			new Acao("Melhorar velocidade", "nave_melhorar", "", "cada degrau custa 1000 x a velocidade atual"),
		],

		// ============================ DENTRO DE UM POD ============================
		// O `verb/Use` do DM alterna (`PlanetTech.dm:119`), e o `UsarNave` deste port faz igual: com
		// nave embaixo de voce, ele desembarca. Aqui o rotulo pode ser honesto e dizer so "Sair",
		// porque esta pagina so existe quando voce ja esta dentro.
		"Spacepod" or "Personal_Spacepod" =>
		[
			new Acao("Sair da nave", "nave_usar", "", "a nave fica pousada onde você estiver"),
			new Acao("Lançar", "nave_lancar", "", "sobe ao espaço -- a espera encurta com a velocidade"),
			new Acao("Melhorar velocidade", "nave_melhorar", "", "cada degrau custa 1000 x a velocidade atual"),
			new Acao("Ver estado", "nave_info", "", "velocidade, o quanto ela corta a viagem, e o próximo preço"),
		],

		// ============================ DENTRO DO FOGUETE ============================
		// `Recondicionar` entra aqui **alem** de no menu do foguete pousado, e a razao e o instante em
		// que ele passa a fazer falta: o casco vira `Usada` na SUBIDA (`didland=1`), com voce dentro.
		// Quem desce de uma viagem descobre o foguete gasto ainda montado nele.
		"Rocket_Ship" =>
		[
			new Acao("Sair do foguete", "nave_usar", "", "o foguete fica pousado onde você estiver"),
			new Acao("Lançar", "nave_lancar", "", "sobe, dá 25 segundos de espaço, e volta sozinho"),
			new Acao("Recondicionar", "nave_recondicionar", "", "um foguete usado só voa de novo pagando 100.000z"),
			new Acao("Ver estado", "nave_info", "", "se ele ainda está inteiro, e o que a viagem faz"),
		],

		_ => Nenhuma,
	};

	/// <summary>
	/// O TETO DE GRAVIDADE DO MUNDO -- o `gravitycap = 500` do original (Gravity.dm:352).
	///
	/// E o do MUNDO, e nao o de uma maquina: cada maquina tem o proprio `Max`, que comeca em 10 e
	/// sobe com upgrades, e nenhuma passa deste numero por mais que seja melhorada.
	/// </summary>
	public const double TetoDeGravidade = 500;

	/// <summary>
	/// DA PRA MEXER NISTO? Serve ao cliente pra decidir se acende a dica de "aperte E" e ao
	/// servidor pra recusar um menu forjado.
	/// </summary>
	public static bool Interativo(string tipo) => De(tipo).Length > 0;

	/// <summary>
	/// ESTE VERBO PERTENCE A ESTE TIPO?
	///
	/// O SERVIDOR PRECISA PERGUNTAR ISTO. O menu e do cliente, e um cliente mexido manda o verbo
	/// que quiser -- sem esta conferencia, "sacar tudo" funcionaria de pe numa arvore de maca. A
	/// regra e a mesma de sempre: a tela esconde por conveniencia, o servidor recusa por
	/// permissao.
	/// </summary>
	public static bool Aceita(string tipo, string verbo)
	{
		if (Aceita(De(tipo), verbo)) return true;

		// OS SUBMENUS CONTAM. `grav_up` so existe dentro de "Melhorar...", e sem esta volta o
		// servidor recusaria a propria melhoria que o menu acabou de oferecer.
		foreach (Acao a in De(tipo))
			if (a.Forma == Forma.Submenu && Aceita(De($"{tipo}/{a.Arg}"), verbo)) return true;

		return false;
	}

	private static bool Aceita(Acao[] acoes, string verbo)
	{
		foreach (Acao a in acoes)
			if (a.Verbo == verbo) return true;
		return false;
	}
}
