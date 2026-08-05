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

		"Research_Station" =>
		[
			new Acao("Fabricar...", "abrir_tech", "", "a grade do que a sua tecnologia já alcança"),
			new Acao("Estudar", "estudar", "", "fica debruçado na bancada ganhando tecnologia"),
			new Acao("Pegar", "pegar", "", "recolhe a bancada pra mochila (só a sua)"),
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

		"Punching_Bag" or "Punching_Machine" =>
		[
			new Acao("Treinar aqui", "treinar_saco", "", "bater no saco rende mais que treinar no ar"),
			new Acao("Aparafusar / soltar", "aparafusar", ""),
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
