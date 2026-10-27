namespace Jandirus.Core.Items;

/// <summary>
/// ============================ OS ITENS QUE SAIRAM DO JOGO ============================
/// A irma de <see cref="Skills.SkillsAposentadas"/>, pra coisa de mochila. Esta lista e o unico lugar em que a
/// decisao esta escrita; quem a le e o LOGIN (`AccountStore.ParaJogador`, ANTES do <see cref="Inventario.Sanear"/>):
/// o item sai da mochila de quem o tinha e o jogador e avisado do porque
/// (`GameServer.AvisarDosItensAposentados`).
///
/// ============================ POR QUE ISTO EXISTE (dono, 2026-10-10) ============================
/// *"o catalogo pode tirar essas armas comentadas"*. Axe, Staff, Spear, Club, Hammer e Cross estavam a venda
/// na aba Tech por 1.000 zeni sem existir no jogo original (o bloco delas em `Modules/Tech/Weapons.dm:2-113`
/// esta dentro de um `/* */`, e o extrator nao entendia comentario de bloco -- ver `Tools/AssetPipeline/DmFonte`).
/// Aqui nenhuma fazia nada: eram itens pessoais sem acao.
///
/// SEM ESTA LISTA o item sumiria CALADO: o `Sanear` da carga joga fora todo id que o catalogo nao conhece, sem
/// frase -- e quem carregava um machado acharia um espaco vazio na mochila sem ninguem ter dito por que.
///
/// ============================ SEM REEMBOLSO (dono, 2026-10-11) ============================
/// *"sem rembolso [...] ninguem fabricou item nenhum entao n precisa de reembolso"*. A primeira versao desta
/// lista devolvia em zeni o preco da bancada, por unidade. O dono recusou: nenhum save de verdade carregava uma
/// das seis (medido nos saves dele em 2026-10-10), e devolver dinheiro por item que ninguem pagou seria imprimir
/// zeni. O item SAI e a frase diz POR QUE -- a carteira fica como estava. (A irma das skills devolve o MARCO de
/// quem comprou: e outra decisao, de outro dia.)
///
/// ============================ O `Staff` E O MOTIVO DA VERSAO ============================
/// Cinco dos seis ids deixaram de existir. O sexto NAO: o DM tem DOIS `Staff` -- a arma comentada (tech 11) e o
/// cajado magico de `Modules/Magic/Magitech.dm:21-28` (tech 1), que e codigo vivo e continua a venda com o mesmo
/// nome e o mesmo preco. O `construcoes.json` trazia os dois com o mesmo id, e o jogo ficava com o ultimo do
/// arquivo: a arma. Entao um `Staff` numa mochila de ANTES e a arma (sai -- senao viraria o cajado de graca), e
/// um `Staff` comprado DEPOIS e o cajado (fica).
///
/// Quem separa os dois e a <see cref="VersaoAtual"/>, gravada no save (`CharacterSave.ItensVersao`): a limpeza
/// de um item so roda em save mais velho que a versao em que ele saiu -- uma vez por personagem, como a
/// migracao do razao de buffs (`CharacterSave.RazaoVersao`).
/// =========================================================================================
///
/// PRA APOSENTAR OUTRO ITEM: uma linha em <see cref="Todos"/> com a versao NOVA, e <see cref="VersaoAtual"/> sobe
/// junto. Tira-lo do `construcoes.json` (e do extrator, se ele o emitir) e a outra metade.
/// </summary>
public static class ItensAposentados
{
	/// <summary>
	/// Um item que saiu: o id que a mochila guardava, o nome que o jogador via, a versao da lista em que saiu, e
	/// por que -- o pedaco da frase que o jogador le.
	/// </summary>
	public readonly record struct Aposentado(string Id, string Nome, int SaiuNaVersao, string Porque);

	/// <summary>
	/// A VERSAO DESTA LISTA: o maior <see cref="Aposentado.SaiuNaVersao"/> dela. E o que o servidor grava em todo
	/// save (`AccountStore.DeJogador`); save de antes disto existir chega com zero.
	/// </summary>
	public const int VersaoAtual = 2;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--aposentadosteste`): a limpeza nao roda. O item some CALADO no `Sanear`, sem
	/// frase, e o `Staff` de antes fica na mochila como se fosse o cajado magico. E o jogo de antes de 2026-10-10.
	/// Lido pela propria <see cref="Limpar"/>.
	/// </summary>
	public static bool SomeCaladoDeTeste;

	private const string ArmaDesativada = "era uma arma que o jogo original deixou desativada, e aqui não fazia nada";

	/// <summary>(2026-10-26) O porque da maquina e dos produtos do sintetizador de virus -- o pedido 98 do dono.</summary>
	private const string SaiuOSintetizador = "o sintetizador de vírus saiu do jogo, e com ele as injeções, os vírus e o LSD";

	public static readonly Aposentado[] Todos =
	[
		// AS SEIS ARMAS COMENTADAS de `Modules/Tech/Weapons.dm:18-113` (`cost=1000`, `neededtech=11` cada uma).
		new("Axe",    "Axe",    1, ArmaDesativada),
		new("Spear",  "Spear",  1, ArmaDesativada),
		new("Club",   "Club",   1, ArmaDesativada),
		new("Hammer", "Hammer", 1, ArmaDesativada),
		new("Cross",  "Cross",  1, ArmaDesativada),
		// O ID CONTINUA NO CATALOGO (o cajado magico) -- ver o cabecalho. So a mochila de antes da versao 1 perde o dela.
		new("Staff",  "Staff",  1,
			"o Staff de luta era uma arma que o jogo original deixou desativada; o Staff que a aba Tech vende agora é o cajado mágico, outro item"),

		// (2026-10-26, VERSAO 2) O SINTETIZADOR DE VIRUS E O QUE ELE FABRICAVA -- o pedido 98 do dono (*"melhor tirar do game essas coisas de
		// injeçao, virus, lsd etc"*): a maquina recolhida na mochila e os dezenove produtos. Ver `Tech.Sintetizador`.
		new(Tech.Sintetizador.Tipo, "Virus Synth", 2, SaiuOSintetizador),
		.. Tech.Sintetizador.Produtos.Select(p => new Aposentado(p.Id, p.Nome, 2, SaiuOSintetizador)),
	];

	/// <summary>
	/// TIRA DA MOCHILA O QUE SAIU DO JOGO, e conta o que tirou (pra quem chama poder avisar). So tira: nao mexe
	/// em zeni -- ver "SEM REEMBOLSO" no cabecalho.
	///
	/// ANTES DO `Sanear`, sempre: e ele que joga fora o id que o catalogo nao conhece mais, e depois dele nao
	/// sobra de que avisar. (E o `Staff` de antes ele nem tiraria: aquele id o catalogo ainda conhece.)
	/// </summary>
	public static List<(Aposentado Item, int Quantos)> Limpar(Inventario mochila, int versaoDoSave)
	{
		var saiu = new List<(Aposentado, int)>();
		if (SomeCaladoDeTeste) return saiu;

		foreach (Aposentado a in Todos)
		{
			if (versaoDoSave >= a.SaiuNaVersao) continue;

			int quantos = 0;
			foreach (Pilha p in mochila.Pilhas)
				if (p.Quantidade > 0 && string.Equals(p.Id, a.Id, StringComparison.OrdinalIgnoreCase)) quantos += p.Quantidade;
			if (quantos <= 0) continue;

			mochila.Pilhas.RemoveAll(p => string.Equals(p.Id, a.Id, StringComparison.OrdinalIgnoreCase));
			saiu.Add((a, quantos));
		}
		return saiu;
	}
}
