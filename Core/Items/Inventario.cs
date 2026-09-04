namespace Jandirus.Core.Items;

/// <summary>
/// UM TIPO DE ITEM -- o que ele e, como se desenha, e o que da pra fazer com ele.
///
/// ============================ POR QUE UM CATALOGO, E NAO CLASSES ============================
/// No DM cada item e um typepath com verbs proprios, e isso funciona la porque o objeto E o
/// codigo. Aqui um item precisa atravessar a rede e o disco, e uma hierarquia de classes obrigaria
/// a serializar POLIMORFISMO -- o pior formato possivel pra uma coisa que so precisa dizer "sou
/// uma maca, tenho seis de nutricao".
///
/// A pilha guarda um ID e uma quantidade. Todo o resto e olhado aqui.
/// ============================================================================================
/// </summary>
public sealed record ItemDef(
	string Id,
	string Nome,
	string Descricao,
	string Arte,
	string Estado,
	bool Empilhavel,
	double Nutricao = 0,
	string[]? Acoes = null)
{
	/// <summary>
	/// O QUE DA PRA FAZER COM ELE. Verbos, como no resto do jogo -- ver `Tech.Interacoes`.
	///
	/// "largar" nao entra aqui: ela vale pra TODO item e o menu a acrescenta sozinha. Repeti-la em
	/// cada linha do catalogo seria trinta chances de esquecer uma.
	/// </summary>
	public string[] AcoesDoItem => Acoes ?? (Nutricao > 0 ? ["comer"] : []);
}

/// <summary>
/// O CATALOGO DE ITENS. Pequeno de proposito: so o que o port ja sabe FAZER.
///
/// ============================ ELE COMECA PEQUENO E ISSO E CERTO ============================
/// O DM tem centenas de itens. Despejar todos aqui daria um inventario cheio de coisas que nao
/// respondem -- e um item que nao faz nada e pior que um item que nao existe: ele promete.
///
/// Cada item entra quando a mecanica dele entra. A maca abre a lista porque comer ja funciona
/// (ver `Stats.Nutricao`).
/// ===========================================================================================
/// </summary>
public static class CatalogoDeItens
{
	public const string Maca = "Apple";
	public const string Scouter = "Scouter";
	public const string Pesos = "Weights";
	public const string Bandagem = "Bandages";
	public const string KitMedico = "First_Aid_Kit";
	public const string Pa = "Shovel";
	public const string Furadeira = "Hand_Drill";

	/// <summary>
	/// A SEMENTE SENZU -- `obj/items/food/Senzu` (`Stamina/Food.dm:19-48`), que o cargo de Assistente do
	/// Guardiao cultiva (`Grow_Senzu_Bean`, `:2-13`: um minuto por semente). Comer cura TODO membro
	/// nao decepado ate o cheio e alimenta 10; o corpo so aceita uma de cada vez (`Senzu + 4 <= 4`).
	/// "acudir" e o `Use_on` num caido (`:57-66`). Ver o lote G12.
	/// </summary>
	public const string Senzu = "Senzu";

	/// <summary>
	/// OS BRINCOS POTARA -- insignia do cargo de Kaioshin, e a porta da fusao Potara.
	///
	/// ============================ UM ITEM, E NAO DOIS BRINCOS PAREADOS ============================
	/// No DM sao dois objetos (`potaraleft.dmi` / `potararight.dmi`) que precisam ser PAREADOS num
	/// verb (`Pair_Earring`, `Fusion.dm:580`), equipados nos dois, e ai a fusao acontece sozinha por
	/// proximidade. Esse minigame inteiro existe porque la nao ha convite: o pareamento E o convite,
	/// feito com antecedencia.
	///
	/// O dono trocou isso por um pedido e um aceite (*"clicar neles da a opcao de jogar pro alvo
	/// atual; o alvo recebe um pedido que pode aceitar"*), e com o convite explicito o par perde a
	/// funcao -- sobrariam dois itens e tres verbs pra fazer o que uma linha de aceite ja faz. Entao
	/// e UM item: o par, como o jogador fala dele ("os brincos").
	/// ==========================================================================================
	/// </summary>
	public const string BrincosPotara = "Potara_Earrings";

	/// <summary>
	/// ============================ AS DUAS ROUPAS QUE JA EXISTIAM E VIRAVAM MOVEL ============================
	/// Os dois ids sao os do `construcoes.json` (linhas 64 e 71), extraidos do DM com custo, tech e
	/// arte: `Spacesuit` e a receita de `PlanetTech.dm:230-235` (75.000 / tech 25) e `Rebreather` e a
	/// de `Tier2.dm:24-30` (100.000 / tech 30). **Da pra compra-las na bancada Tech desde sempre** --
	/// e como nao estavam nesta tabela escrita a mao, o <see cref="Get"/> as tratava como CONSTRUCAO
	/// e a unica acao delas era "posicionar": o jogador comprava uma roupa espacial e ela virava
	/// movel no chao. As duas linhas abaixo sao o conserto disso, e sao o que faz o vacuo ter saida.
	///
	/// **NO DM SAO DUAS VARS DIFERENTES E AQUI E UMA SO.** La o Spacesuit faz `usr.spacesuit = 1`
	/// (`PlanetTech.dm:386`) e o Rebreather faz `usr.spacesuit += 1` (`Tier2.dm:198`) -- soma, porque
	/// nada impede de usar os dois. Aqui a pergunta e booleana (<see cref="Vacuo.RespiraNoVacuo"/>),
	/// entao dois trajes nao valem mais que um; e o que o original tambem entrega, ja que o `if` de
	/// `Stats.dm:218` so pergunta `!spacesuit`.
	/// ==================================================================================================
	/// </summary>
	public const string Traje = "Spacesuit";
	public const string Respirador = "Rebreather";

	/// <summary>
	/// O DRAGON RADAR -- `obj/items/Radar` (`Tier 1.5.dm:228`), fabricado na bancada
	/// (`obj/Creatables/Dragon_Radar`, `Tier2.dm:9-15`: 150.000 zeni, tech 40).
	///
	/// ============================ ELE JA ERA COMPRAVEL, E VIRAVA MOVEL ============================
	/// `Dragon_Radar` esta no `construcoes.json` (linha 78) desde que o extrator rodou, entao **da pra
	/// compra-lo na aba Tech desde sempre**. Como ele nao estava nesta tabela escrita a mao, o
	/// <see cref="Get"/> caia no ramo de CONSTRUCAO e a unica acao dele era "posicionar": o jogador
	/// gastava cento e cinquenta mil zeni e ganhava um enfeite pra pousar no chao.
	///
	/// E o mesmo defeito -- e o mesmo conserto -- da Roupa Espacial e do Respirador logo abaixo. O
	/// criterio pra estar aqui e o que o cabecalho do <see cref="Get"/> ja diz: **acao propria**. A
	/// dele e "usar", e ela varre as esferas ACORDADAS deste mundo.
	///
	/// O `Set_Type` DO ORIGINAL NAO FOI PORTADO, e e uma decisao. La o radar sintoniza um tipo
	/// qualquer de objeto (`radarType = O.type`, :244-249) e so acha o que ja se VIU -- um
	/// galinha-e-ovo real, medido na Fase 0: pra o radar achar uma esfera, era preciso enxergar uma
	/// esfera antes. Aqui ele nasce sintonizado nas Esferas do Dragao, que e a unica coisa que
	/// alguem jamais quis achar com ele.
	/// ==========================================================================================
	/// </summary>
	public const string Radar = "Dragon_Radar";

	/// <summary>
	/// O NAV SYSTEM -- `obj/items/Nav_System` (`PlanetTech.dm:2-21`), fabricado na bancada
	/// (`obj/Creatables/Nav_System`, `Tier3.dm:30-36`: 550.000 zeni, tech 55).
	///
	/// ============================ ELE JA ERA COMPRAVEL, E VIRAVA ENFEITE DE CHAO ============================
	/// Mesma historia -- e mesmo conserto -- do Dragon Radar aqui em cima e das duas roupas: `Nav_System`
	/// esta no `construcoes.json` (linha 89) desde que o extrator rodou, entao **da pra compra-lo na aba
	/// Tech desde sempre**. Como ele nao estava nesta tabela escrita a mao, o <see cref="Get"/> caia no
	/// ramo de CONSTRUCAO e a unica acao dele era "posicionar" -- que TIRA da mochila (`Posicionar`, em
	/// `GameServer.Tech.cs`). O jogador gastava meio milhao e ganhava um movel. No original ele e
	/// `/obj/items/`, ou seja coisa que se CARREGA, e nunca foi mobilia.
	///
	/// ============================ E ELE NAO TEM ACAO, E ISSO E DE PROPOSITO ============================
	/// O DM tem um `Power_Switch()` que liga e desliga (`PlanetTech.dm:9-19`, escrevendo `usr.hasnav`).
	/// Aqui a resposta e ESTAR COM ELE, pela mesma razao ja escrita na Roupa Espacial: o liga/desliga
	/// moraria em `ServerPlayer.PoderesConcedidos`, que **nao vai pro disco** -- quem deslogasse com o
	/// nav ligado acordaria com a aba sumida sem entender por que. A mochila e salva (`CharacterStore`),
	/// entao a presenca sobrevive ao relog de graca. E o que se perde e pequeno: no original, desligar
	/// so servia pra trocar qual aba do statpanel abria.
	///
	/// QUEM PERGUNTA E O SERVIDOR, em `GameServer.Sigilo.PoderesVisiveis` -- e o bit `Poder.Nav` que sai
	/// de la e o que faz a aba existir (`MenuJogo.Abas`), como `HtmlUI.dm:138` faz com `hasnav`.
	/// ==================================================================================================
	/// </summary>
	public const string NavSystem = "Nav_System";

	/// <summary>
	/// ============================ ESTE ITEM PROTEGE DO VACUO? ============================
	/// A pergunta que o servidor faz na mochila. Duas linhas e uma casa so -- ver
	/// <see cref="Vacuo.RespiraNoVacuo"/>, que e quem decide o resto.
	/// </summary>
	public static bool ProtegeDoVacuo(string id) =>
		string.Equals(id, Traje, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(id, Respirador, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// ============================ ESTE ITEM E O NAV SYSTEM? ============================
	/// A pergunta que o servidor faz na mochila (`GameServer.Sigilo.PoderesVisiveis`). Mora aqui pelo
	/// mesmo motivo da do vacuo logo acima: quem sabe QUAIS ids valem e o catalogo, e nao quem pergunta.
	/// No dia em que um segundo aparelho de navegacao existir, ele entra nesta linha e o servidor nao
	/// precisa ficar sabendo.
	/// ==================================================================================================
	/// </summary>
	public static bool EhNavSystem(string id) =>
		string.Equals(id, NavSystem, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// ============================ OS IDS SAO OS DO CATALOGO DE CONSTRUCOES ============================
	/// "Scouter" aqui e o MESMO "Scouter" de `construcoes.json`, e nao por acaso: e essa igualdade
	/// que faz `Construir` saber que o item vai pra mochila em vez de virar movel no chao. Um
	/// segundo conjunto de nomes exigiria uma tabela de traducao entre os dois, e a primeira coisa
	/// a divergir seria um item novo que alguem cadastrou so num lado.
	/// ==================================================================================================
	/// </summary>
	private static readonly Dictionary<string, ItemDef> Tudo = new(StringComparer.OrdinalIgnoreCase)
	{
		[Maca] = new ItemDef(
			Maca, "Maçã", "Cada mordida é perfeição frutada.",
			"res://Assets/Sprites/Misc/Misc3.tres", "Apple",
			Empilhavel: true, Nutricao: 6),

		// O SCOUTER ACENDE UM SISTEMA QUE JA EXISTIA INTEIRO E MORTO: o bit `Poder.Scouter`, o corte
		// de BP no pacote de ficha e o "???" da tela estao no port desde sempre, com um comentario
		// dizendo "hoje o bit nunca acende -- o port ainda nao tem o item scouter". Ele e o item.
		[Scouter] = new ItemDef(
			Scouter, "Scouter", "Lê o poder de luta de quem você olha. Sem ele, todo número é \"???\".",
			"res://Assets/Sprites/Misc/Objects/Technology/Scouter.tres", "",
			Empilhavel: false, Acoes: ["equipar"]),

		// PESO NAO E EQUIPAMENTO COMUM: ele ATRAPALHA de propósito, e o ganho e o treino que a
		// dificuldade rende. Por isso a acao e "ajustar" e nao "equipar" -- o quanto importa.
		[Pesos] = new ItemDef(
			Pesos, "Pesos", "Atrasa cada passo e cada golpe. Em troca, treinar rende muito mais.",
			// A ARTE E A DO DM: `obj/items/Weight` usa `Clothes_ShortSleeveShirt.dmi` (Tier 1.dm:107) -- os
			// pesos sao ROUPA, e o icone e a camisa de frente. A folha de tech nao tem estado "weights";
			// com ela o icone caia no primeiro estado da folha (um pod de androide), que foi o que o dono viu.
			"res://Assets/Sprites/Clothes/Clothes_ShortSleeveShirt.tres", "walk_south",
			Empilhavel: false, Acoes: ["ajustar", "tirar"]),

		[Bandagem] = new ItemDef(
			Bandagem, "Bandagens", "Fecha o que está aberto. Devagar, e só fora de combate.",
			"res://Assets/Sprites/Misc/Objects/Technology/tech.tres", "bandage",
			Empilhavel: true, Acoes: ["usar"]),

		[KitMedico] = new ItemDef(
			KitMedico, "Kit de Primeiros Socorros", "Dez usos. Cura bem mais que uma bandagem.",
			"res://Assets/Sprites/Misc/Objects/Technology/FirstAid.tres", "",
			Empilhavel: false, Acoes: ["usar"]),

		[Pa] = new ItemDef(
			Pa, "Pá", "Dá pra cavar. Devagar...",
			"res://Assets/Sprites/Misc/Shovel.tres", "",
			Empilhavel: false, Acoes: ["cavar"]),

		[Furadeira] = new ItemDef(
			Furadeira, "Furadeira Manual", "Cava bem mais rápido que uma pá.",
			"res://Assets/Sprites/Misc/Objects/Technology/tech.tres", "drill",
			Empilhavel: false, Acoes: ["cavar"]),

		// `stackable = 1`, `nutrition = 10` (`Food.dm:21-28`). A arte e o `Senzu Bean.dmi` de `:24`.
		[Senzu] = new ItemDef(
			Senzu, "Semente Senzu",
			"Cura o corpo inteiro de uma vez e mata a fome. O corpo só aceita uma de cada vez. "
			+ "\"Acudir\" dá a semente a alguém desacordado ao seu lado.",
			"res://Assets/Sprites/Misc/Objects/Items/Senzu Bean.tres", "",
			Empilhavel: true, Nutricao: 10, Acoes: ["comer", "acudir"]),

		// O ESTADO E "radar" EM MINUSCULA, e o `construcoes.json` diz "Radar" com maiuscula. Os dois
		// funcionam -- o `Sanear` do desenho baixa a caixa antes de procurar a animacao --, mas o que
		// esta no `.tres` e o minusculo, e escrever o nome real e uma reclamacao a menos no log no dia
		// em que alguem mexer no saneamento.
		[Radar] = new ItemDef(
			Radar, "Dragon Radar",
			"Sente as Esferas do Dragão ACORDADAS do mundo em que você está -- e só desse mundo. "
			+ "Esfera que ainda se refaz não aparece.",
			"res://Assets/Sprites/Misc/Misc2.tres", "radar",
			Empilhavel: false, Acoes: ["usar"]),

		// A ARTE E A MESMA DO RADAR (`Misc2.tres`, estado "Radar"): e o que o `construcoes.json` traz, e
		// e o que o DM usa (`icon='Misc2.dmi'`, `icon_state="Radar"`, `PlanetTech.dm:4-5`). O estado vai
		// com R maiusculo porque e assim que ele se chama no `.tres`.
		[NavSystem] = new ItemDef(
			NavSystem, "Nav System",
			"Enquanto estiver com você, a aba Nav existe: a carta estelar da galáxia e o piloto automático. "
			+ "Sem ele, o menu não tem para onde olhar.",
			"res://Assets/Sprites/Misc/Misc2.tres", "Radar",
			Empilhavel: false, Acoes: []),

		// ============================ E ELAS NAO TEM ACAO, E ISSO E O CONSERTO ============================
		// A tentacao obvia era `Acoes: ["equipar"]`, copiando o scouter. **Seria um bug que mata.** O
		// scouter guarda o estado ligado/desligado em `ServerPlayer.PoderesConcedidos`, e esse campo
		// **nao vai pro disco** -- ele nao aparece em nenhuma linha do `CharacterStore`. Quem desloga
		// com o scouter ligado acorda com ele desligado, e hoje isso custa um "???" na tela ate
		// reequipar. Com a roupa espacial custaria a VIDA: deslogar no vacuo e voltar seria morrer em
		// 20 s sem ter feito nada, e o jogador nao teria como saber por que.
		//
		// Entao a protecao e ESTAR COM A ROUPA, e nao um bit ligado: a mochila **e** salva
		// (`CharacterStore.cs:673` e `:769`), entao o abrigo sobrevive ao relog de graca, sem campo
		// novo, sem bit novo no `Protocol.Poder` e sem uma unica linha de persistencia. E o desvio
		// que isso cria em relacao ao DM e pequeno de proposito: la o `Equip()` existe porque a roupa
		// tambem aparece no boneco e tem radio (`PlanetTech.dm:386`), duas coisas que este port ainda
		// nao tem. No dia em que o guarda-roupa entrar, o vestir vira aparencia -- e esta regra do
		// vacuo nao muda, porque ela pergunta a mochila e nao a fantasia.
		// ==============================================================================================
		[Traje] = new ItemDef(
			Traje, "Roupa Espacial",
			"Enquanto estiver com você, dá pra respirar no vácuo. Pesada e feia, mas ninguém sufoca com estilo.",
			"res://Assets/Sprites/Clothes/spacesuit.tres", "",
			Empilhavel: false, Acoes: []),

		// A ARTE E `Assets/Sprites/Clothes/potara.png`, versionada e ja importada (`.ctex` + `.tres`
		// conferidos). **Nao e a `Potara-Equipped.png`**, que esta sendo MOVIDA de pasta por outra
		// sessao neste exato momento: apontar pra um alvo em movimento seria escrever um caminho que
		// quebra na proxima sincronia.
		[BrincosPotara] = new ItemDef(
			BrincosPotara, "Brincos Potara",
			"O par de brincos dos Kaioshin. Mire alguém e jogue um deles: se a pessoa aceitar, vocês "
			+ "se fundem na hora -- de qualquer raça, sem dança, e por meia hora inteira.",
			"res://Assets/Sprites/Clothes/potara.tres", "",
			Empilhavel: false, Acoes: ["jogar"]),

		[Respirador] = new ItemDef(
			Respirador, "Respirador",
			"Enquanto estiver com você, dá pra respirar no vácuo. Cabe no rosto, custa mais e não constrange ninguém.",
			"res://Assets/Sprites/Clothes/Clothes, Ninja Mask.tres", "",
			Empilhavel: false, Acoes: []),
	};

	/// <summary>
	/// O CATALOGO DE CONSTRUCOES, quando alguem o ligou. Ver <see cref="Get"/>.
	///
	/// E uma referencia e nao uma copia: quem monta (servidor e cliente) ja tem o catalogo lido do
	/// JSON, e duplicar 99 fichas pra ter dois formatos do mesmo dado seria a receita de eles
	/// discordarem no dia em que um item novo entrasse.
	/// </summary>
	public static Tech.CatalogoDeObras? Obras;

	/// <summary>
	/// A FICHA DE UM ITEM -- do catalogo escrito a mao, ou derivada de uma CONSTRUCAO.
	///
	/// ============================ TODA CONSTRUCAO E UM ITEM ============================
	/// A tabela acima tem sete linhas; o catalogo de construcoes tem noventa e nove. Escrever as
	/// noventa e nove aqui seria manter duas listas do mesmo mundo em sincronia na mao -- e a
	/// primeira a ficar pra tras seria a que alguem esqueceu de atualizar.
	///
	/// A regra e simples: se o id nao esta na tabela mas EXISTE como construcao, ele vira item com
	/// a arte, o nome e a descricao dela. E o que faz uma maquina de gravidade caber na mochila
	/// entre a bancada e o momento de assenta-la no chao.
	///
	/// AS SETE ESCRITAS A MAO CONTINUAM VALENDO porque elas tem algo que a construcao nao tem:
	/// nutricao, empilhamento, e acoes proprias (comer, equipar, cavar).
	/// ===================================================================================
	///
	/// ============================ E AQUI MORA A REGRA 2 DO DONO, NUMA LINHA SO ============================
	/// *"item que se poe no chao ganha a acao INSTALAR; item de uso pessoal (scouter, armaduras,
	/// pesos) NAO ganha -- ele e equipavel."*
	///
	/// ANTES A REGRA ERA "NAO ESTA NA MINHA TABELA DE NOVE LINHAS", e ela dava "Assentar no chao"
	/// pra **vinte e oito** itens de uso pessoal: a Armadura (que o dono citou pelo nome), as nove
	/// armas de fogo, o Nav System, os Pesos, as luvas de dreno, a bomba de fumaca. Comprar uma
	/// armadura na bancada deixava o jogador com um movel pra pousar no chao.
	///
	/// AGORA QUEM RESPONDE E O CATALOGO -- <see cref="Tech.Construcao.Pessoal"/>, extraido do escopo
	/// dos verbs do DM. A diferenca nao e de estilo: uma tabela a mao envelhece no primeiro item
	/// novo que alguem cadastrar so num lado, e neste projeto isso ja aconteceu tres vezes.
	/// ==================================================================================================
	/// </summary>
	public static ItemDef? Get(string id)
	{
		if (Tudo.TryGetValue(id, out ItemDef? mao)) return mao;
		if (LivroDeEnsinamentos.Ler(id) is { } livro) return livro.Ficha;

		Tech.Construcao? c = Obras?.Get(id);
		if (c == null) return null;

		// UMA CONSTRUCAO NA MOCHILA SO SABE FAZER UMA COISA: virar construcao de novo. "posicionar"
		// e a acao, e ela abre o fantasma no mouse -- ver `TelaDeInventario`.
		//
		// O ITEM PESSOAL FICA SEM ACAO NENHUMA, e isso e honesto: o port ainda nao tem o que a
		// armadura ou a espingarda FAZEM, e um botao "Assentar no chao" nelas nao seria uma acao a
		// mais -- seria a acao ERRADA. Quem ja tem mecanica portada esta na tabela la em cima com o
		// verbo dela (equipar, ajustar, usar, cavar), e e por ali que as proximas entram.
		return new ItemDef(c.Id, c.Nome, c.Desc, c.Arte, c.Estado,
						   Empilhavel: false, Acoes: c.Pessoal ? [] : [AcaoPosicionar]);
	}

	public static IEnumerable<ItemDef> Todos => Tudo.Values;

	/// <summary>
	/// O NOME DA ACAO DE ASSENTAR NO CHAO. Escrito uma vez porque ele e um contrato entre tres
	/// lugares que nao se falam: o catalogo que a produz, o menu do inventario que a desenha, e o
	/// servidor que a cobra antes de plantar qualquer coisa.
	/// </summary>
	public const string AcaoPosicionar = "posicionar";

	/// <summary>
	/// DA PRA ASSENTAR ISTO NO CHAO?
	///
	/// ============================ ELA PERGUNTA A LISTA DE ACOES, E NAO A REGRA ============================
	/// A tentacao era repetir aqui o teste do <see cref="Get"/> (`!Tudo.ContainsKey && !c.Pessoal`).
	/// Seria a MESMA regra escrita em dois lugares -- e a versao antiga disto (`EhConstrucao`) era
	/// exatamente isso: uma pergunta com nome bonito que **nao decidia nada**, porque quem decidia a
	/// acao era o ramo do `Get`, e o unico chamador dela so escolhia a frase do chat. Duas verdades,
	/// e a que tinha nome era a que ninguem consultava.
	///
	/// Perguntando a `AcoesDoItem`, o servidor cobra LITERALMENTE o mesmo botao que o menu desenhou.
	/// Se um dia a acao mudar de nome ou de criterio, as duas pontas mudam juntas ou nenhuma muda.
	/// ==================================================================================================
	/// </summary>
	public static bool PodeAssentarNoChao(string id) =>
		Get(id) is { } def && Array.IndexOf(def.AcoesDoItem, AcaoPosicionar) >= 0;
}

/// <summary>
/// UMA PILHA no inventario: o tipo e quantos.
///
/// RECORD STRUCT porque ela e valor puro. A lista inteira e copiada pra rede e pro disco, e uma
/// classe faria duas pilhas apontarem pro mesmo objeto depois de um `[.. lista]`.
/// </summary>
public record struct Pilha(string Id, int Quantidade);

/// <summary>
/// O QUE O PERSONAGEM CARREGA.
///
/// ============================ TRINTA SLOTS, COMO NO ORIGINAL ============================
/// `inven_max = 30` (`Inventory.dm:158-162`). O limite e em SLOTS e nao em peso, e uma pilha ocupa
/// um slot inteiro por maior que seja -- e o que faz item empilhavel valer a pena e o que da ao
/// jogador uma razao pra escolher o que largar.
/// ========================================================================================
///
/// ============================ EMPILHAR E DO TIPO, NAO DO INVENTARIO ============================
/// O DM decide isso por item (`stackable`), e nao globalmente: maca junta, maquina de gravidade
/// nao. Duas maquinas iguais tem que ocupar dois slots porque cada uma tem ESTADO proprio (bateria,
/// alcance, gravidade ligada) -- fundi-las numa pilha de dois apagaria um dos dois estados.
/// ==============================================================================================
/// </summary>
public sealed class Inventario
{
	public const int Slots = 30;

	public List<Pilha> Pilhas = [];

	public int Ocupados => Pilhas.Count;
	public bool Cheio => Pilhas.Count >= Slots;

	/// <summary>Quantos deste tipo o personagem tem, somando as pilhas.</summary>
	public int Quantos(string id)
	{
		int n = 0;
		foreach (Pilha p in Pilhas)
			if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) n += p.Quantidade;
		return n;
	}

	/// <summary>
	/// GUARDA. Devolve quantos NAO couberam -- zero quer dizer que entrou tudo.
	///
	/// Devolver o resto em vez de um booleano importa: quem chama precisa saber o que fazer com a
	/// sobra (deixar no chao, avisar, cancelar a colheita), e um `false` nao diz quanto sobrou.
	/// </summary>
	public int Guardar(string id, int quantos = 1)
	{
		if (quantos <= 0) return 0;
		ItemDef? def = CatalogoDeItens.Get(id);
		if (def == null) return quantos;

		if (def.Empilhavel)
		{
			for (int i = 0; i < Pilhas.Count; i++)
			{
				if (!string.Equals(Pilhas[i].Id, id, StringComparison.OrdinalIgnoreCase)) continue;
				Pilhas[i] = Pilhas[i] with { Quantidade = Pilhas[i].Quantidade + quantos };
				return 0;
			}
		}

		// UM SLOT POR UNIDADE quando o item nao empilha -- e por isso o laco: guardar tres maquinas
		// de uma vez tem que ocupar tres slots, ou parar quando acabarem.
		int sobrou = quantos;
		while (sobrou > 0)
		{
			if (Cheio) return sobrou;
			int nesteSlot = def.Empilhavel ? sobrou : 1;
			Pilhas.Add(new Pilha(def.Id, nesteSlot));
			sobrou -= nesteSlot;
		}
		return 0;
	}

	/// <summary>
	/// TIRA. Devolve quantos saiu de verdade (pode ser menos do que se pediu).
	///
	/// A pilha que zera SOME da lista: deixar um slot com quantidade zero seria um buraco que
	/// aparece na tela como um quadrado vazio no meio das coisas.
	/// </summary>
	public int Tirar(string id, int quantos = 1)
	{
		int levou = 0;
		for (int i = Pilhas.Count - 1; i >= 0 && levou < quantos; i--)
		{
			if (!string.Equals(Pilhas[i].Id, id, StringComparison.OrdinalIgnoreCase)) continue;

			int daqui = Math.Min(Pilhas[i].Quantidade, quantos - levou);
			levou += daqui;

			int resta = Pilhas[i].Quantidade - daqui;
			if (resta > 0) Pilhas[i] = Pilhas[i] with { Quantidade = resta };
			else Pilhas.RemoveAt(i);
		}
		return levou;
	}

	/// <summary>
	/// JOGA FORA O QUE NAO EXISTE MAIS NO CATALOGO.
	///
	/// Um save antigo pode carregar o id de um item que foi renomeado ou removido, e um id que o
	/// catalogo nao conhece vira um slot que a tela nao consegue desenhar e o menu nao consegue
	/// usar -- ocupando espaco pra sempre. Roda na carga.
	/// </summary>
	public void Sanear()
	{
		Pilhas.RemoveAll(p => CatalogoDeItens.Get(p.Id) == null || p.Quantidade <= 0);
		if (Pilhas.Count > Slots) Pilhas.RemoveRange(Slots, Pilhas.Count - Slots);
	}
}

/// <summary>
/// O LIVRO DE ENSINAMENTOS -- o `/obj/items/book/Skillbook` do original (`KiStatsModule.dm:178-200`),
/// escrito pelo verb `Write_Teachings` e lido por quem sabe a mesma skill num nivel mais baixo.
///
/// ============================ ELE NAO CABE NO CATALOGO ESCRITO A MAO, E POR ISSO E UM ID ============================
/// Todo item deste port e um par `(id, quantidade)`: a mochila nao guarda estado POR UNIDADE
/// (`Pilha`), o pacote de rede so leva o id e o numero, e o save so grava isso. O livro do DM tem
/// tres campos proprios (`skillname`, `level`, `exp`) e duas copias dele nunca sao iguais.
///
/// Havia duas saidas: por um campo de carga na `Pilha` -- o que mexeria no save, no protocolo, na
/// tela do inventario e em todo lugar que copia uma pilha --, ou **por o dado NO PROPRIO ID**. Vai
/// no id, porque o id ja atravessa as tres pontas intacto e porque a mochila nao precisa aprender
/// nada de novo: `Livro|Basic Ki Awareness|48` E o livro.
///
/// O `exp` NAO ENTRA NO ID porque ele nao e um dado independente: no DM os dois campos saem do
/// MESMO numero, o nivel do autor na hora de escrever (`writelevel = round(level/2)`,
/// `writeexp = 2000*max(1,log(2,level))*1.04**level`, `:163-165`). Guardar o nivel do autor guarda
/// os dois, e sem risco de gravar um par que o DM nunca produziria.
///
/// O NOME DA SKILL, E NAO O TYPEPATH, tambem e do DM: o `Study_Book` compara `S.name == skillname`
/// (`:196`). Isso vale de graca TRES coisas -- a tela mostra o livro sem consultar catalogo nenhum,
/// o id fica curto (o maior nome de `/datum/skill/mind` tem 25 letras contra 30 do typepath), e ele
/// nao tem BARRA dentro: o canal de itens do servidor (`GameServer.ComandoDeItem`) parte o argumento
/// na primeira `/` pra carregar um numero junto ("Weights/40"), e um id com typepath seria cortado
/// no primeiro segmento -- o livro viraria "isso nao existe" no clique.
/// ============================================================================================================
/// </summary>
public sealed record LivroDeEnsinamentos(string Skill, int NivelDoAutor)
{
	/// <summary>O prefixo do id. Uma constante porque ela e um contrato entre o catalogo e o servidor.</summary>
	public const string Prefixo = "Livro|";

	/// <summary>
	/// ATE QUE NIVEL ELE ENSINA -- `writelevel = round(nA.level/2)` (`KiStatsModule.dm:164`). O
	/// `round()` de um argumento no BYOND e PISO, nao arredondamento.
	/// </summary>
	public int NivelQueEnsina => (int)Math.Floor(NivelDoAutor / 2.0);

	/// <summary>
	/// QUANTO EXP ELE DA -- `2000 * max(1, log(2, level)) * 1.04**level` (`:165`). Ele ainda passa
	/// pelo `KiSkillGains` do leitor na hora de ler (`:198`), como no original.
	/// </summary>
	public double Exp => 2000 * Math.Max(1, Math.Log2(Math.Max(NivelDoAutor, 1))) * Math.Pow(1.04, NivelDoAutor);

	public string Id => $"{Prefixo}{Skill}|{NivelDoAutor}";

	/// <summary>A ficha que a mochila e a tela leem. "ler" e a acao -- o `Study_Book` do DM.</summary>
	public ItemDef Ficha => new(
		Id,
		$"Ensinamentos: {Skill}",
		$"Um livro escrito por alguem que chegou ao nível {NivelDoAutor} de {Skill}. "
		+ $"Só ensina quem já sabe {Skill} e ainda está no nível {NivelQueEnsina} ou abaixo -- "
		+ "e some ao ser lido.",
		"res://Assets/Sprites/Misc/Objects/Technology/Books.tres", "",
		Empilhavel: false, Acoes: ["ler"]);

	/// <summary>Desmonta um id de livro. Nulo pra qualquer outro id -- e a porta do <see cref="CatalogoDeItens.Get"/>.</summary>
	public static LivroDeEnsinamentos? Ler(string id)
	{
		if (!id.StartsWith(Prefixo, StringComparison.Ordinal)) return null;
		int barra = id.LastIndexOf('|');
		if (barra <= Prefixo.Length - 1) return null;
		string skill = id[Prefixo.Length..barra];
		// NOME VAZIO NAO VIRA LIVRO: um id malformado (de um save adulterado, ou de um cliente
		// remendado) tem que devolver nulo pra o `Sanear` da mochila varrer a pilha, e nao um item
		// sem nome que a tela desenha como um quadrado mudo.
		if (skill.Length == 0 || !int.TryParse(id[(barra + 1)..], out int nivel) || nivel <= 0) return null;
		return new LivroDeEnsinamentos(skill, nivel);
	}
}
