namespace Jandirus.Core.Items;

/// <summary>
/// **A ARMADURA DE TECH** -- o `/obj/items/Armor` do original (`Code/Modules/Tech/Armor.dm`), que a aba Tech ja
/// fabricava e que ate 2026-10-12 nao fazia nada: caia na mochila sem acao nenhuma.
///
/// ============================ O QUE O DONO PEDIU (2026-10-12) ============================
///   * *"a sub pasta "Armor" sao sprites da armadura q vc pode fabricar com tech, q ao fabricar uma armadura da
///     tech, vai abrir uma tela pra vc escolher qual sprite vc quer"*;
///   * *"se o npc estiver usando uma armadura (como a maioria dos saiyajins) ainda assim vai dar pra roubar se vc o
///     nocautear, e ter as vantagens de ter uma armadura (armadura faz diferença mais no early game, passou dos 50k
///     de poder nem deveria fazer diferença)"*.
///
/// ============================ COMO ELA EXISTE AQUI ============================
/// A ARMADURA E UMA PECA DE ROUPA cuja folha mora na lista de armaduras do catalogo de aparencia
/// (`VisualCatalog.Armaduras`, a pasta `Clothes/Armor`). Isso e o que o NPC Saiyajin ja vestia, e e o que faz as
/// tres coisas do pedido serem UMA so:
///   * FABRICAR entrega o item cru (<see cref="Id"/>); a tela de escolha (`Client/TelaDeArmadura`) manda a arte, e o
///     servidor troca o item cru pela peca pronta (`Armadura|nome|`, ver `RoupaGuardada`);
///   * VESTIR e pela mochila, como toda roupa -- e so UMA armadura por corpo (`Armor.dm:21-23`);
///   * ROUBAR e o saque do nocauteado, que passou a oferecer a roupa do corpo (`Saque.PecasQueDaPraLevar`): a
///     armadura do NPC sai como a mesma peca, e protege quem a vestir.
///
/// ============================ O QUE ELA RENDE, E A DIVERGENCIA DO DM ============================
/// No DM vestir soma `armorincrease = 2` ao `physdefBuff` (`Armor.dm:13,29`) -- defesa FISICA, somada (e por isso a
/// propria descricao de la avisa: *"its boosts start becoming minor"*). Aqui e a mesma soma, no mesmo lugar da conta
/// (`Fighter.Statify`), com DUAS regras do dono por cima:
///   * ela DESAPARECE COM O PODER: inteira em quem esta comecando, metade aos 25 mil de poder BASE, nada dos 50 mil
///     pra cima (<see cref="Fator"/>);
///   * no DM o NPC veste a armadura como desenho (`npc_wear_armor_icon`, sem `physdefBuff`). Continua assim: quem o
///     servidor protege e o corpo SEM cerebro de IA (`GameServer.VesteArmadura`). A peca roubada dele protege o ladrao.
///
/// O QUE NAO VEIO: o `speedBuff -= 2` (no DM ele nao faz nada -- a conta de velocidade usa `max(speedBuff*Tspeed,
/// 0.1)`, que engole o negativo) e as duas melhorias pagas (`Upgrade()`, 45 e 50 mil zeni), que mexem nesses dois
/// numeros. A arte "custom" (`Icon()` com upload) tambem nao: aqui a lista e a da pasta.
/// </summary>
public static class ArmaduraDeTech
{
	/// <summary>O id do item cru -- o da construcao no `construcoes.json` (`/obj/items/Armor`).</summary>
	public const string Id = "Armor";

	/// <summary>A acao do item cru: escolher a arte. A mochila abre a tela, e o servidor troca o item pela peca.</summary>
	public const string AcaoMoldar = "moldar";

	/// <summary>`armorincrease = 2` (`Armor.dm:13`): o que a armadura soma a defesa fisica de quem esta comecando.</summary>
	public const double Protecao = 2;

	/// <summary>O poder BASE em que a armadura deixa de fazer diferenca. *"passou dos 50k de poder nem deveria fazer diferença"*.</summary>
	public const double PoderEmQueNaoFazDiferenca = 50_000;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v020teste`): a armadura vestida nao protege -- o jogo de antes de 2026-10-12, em
	/// que ela era so um item parado na mochila. Lido pela propria regra, em <see cref="Bonus"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool NaoProtegeDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v020teste`): a protecao nao some com o poder -- a soma crua do DM. Lido pela
	/// propria regra, em <see cref="Fator"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool ProtegeEmQualquerPoderDeTeste;

	/// <summary>Quanto da protecao sobra pra este poder base: 1 no comeco, uma reta ate 0 nos 50 mil.</summary>
	public static double Fator(double poderBase) =>
		ProtegeEmQualquerPoderDeTeste ? 1 : Math.Clamp(1 - poderBase / PoderEmQueNaoFazDiferenca, 0, 1);

	/// <summary>O que a armadura soma a defesa fisica desta ficha AGORA (zero sem armadura no corpo).</summary>
	public static double Bonus(Stats.Fighter f) =>
		!f.armadura || NaoProtegeDeTeste ? 0 : Protecao * Fator(f.BP);

	/// <summary>
	/// ESTA ARTE PODE SER ESCOLHIDA NA TELA? Todas as da pasta, menos a do Beerus: ela esta na lista porque e a
	/// roupa do NPC Bills (o corpo dele e outra folha), e o dono a tirou do que o jogador pode vestir.
	/// </summary>
	public static bool Escolhivel(string nomeDoArquivo) =>
		nomeDoArquivo.Length > 0 && !nomeDoArquivo.Contains("Beerus", StringComparison.OrdinalIgnoreCase);

	/// <summary>As artes que a tela de escolha oferece, na ordem do catalogo: (nome do arquivo, caminho da folha).</summary>
	public static List<(string Nome, string Caminho)> Escolhiveis(Appearance.VisualCatalog cat)
	{
		var saida = new List<(string, string)>();
		foreach (string caminho in cat.Armaduras)
		{
			string nome = Appearance.VisualCatalog.NomeDoArquivo(caminho);
			if (Escolhivel(nome)) saida.Add((nome, caminho));
		}
		return saida;
	}

	/// <summary>A frase da peca pronta, na mochila. Sem numero nenhum: o quanto ela rende depende do poder de quem veste.</summary>
	public const string DescricaoDaPeca =
		"Uma armadura. Protege o corpo contra golpes físicos -- faz diferença pra quem está começando, e nenhuma "
		+ "pra um guerreiro muito forte. Só uma por vez.";

	/// <summary>
	/// A ficha do item CRU (recem-fabricado, ainda sem arte), a partir da construcao do catalogo.
	///
	/// O NOME E O DA CONSTRUCAO (`c.Nome`), como o de toda construcao que vira item: e o que a aba Tech escreve no
	/// card que a fabrica, e o item que cai na mochila tem de ter o nome do botao que o fez. (Na primeira entrega
	/// de 2026-10-12 ele se chamava "Armadura" aqui e "Armor" no card: o robo que fabrica pela tela nao achava o
	/// card, e foi ele quem avisou -- `--diaginstalar`, F1.3b.)
	/// </summary>
	public static ItemDef Ficha(Tech.Construcao c) =>
		new(c.Id, c.Nome,
			"Uma armadura recém-fabricada, ainda sem forma. Escolha a arte dela pra poder vestir: depois de moldada "
			+ "ela protege o corpo contra golpes físicos (faz diferença pra quem está começando).",
			c.Arte, c.Estado, Empilhavel: false, Acoes: [AcaoMoldar]);
}
