namespace Jandirus.Core.Items;

/// <summary>O que pode estar largado no chao. O byte viaja no `S2C.Chao`.</summary>
public enum TipoDeLargado : byte
{
	/// <summary>Um item da mochila (uma pilha dele): <see cref="Largado.Item"/> diz qual.</summary>
	Item = 0,

	/// <summary>Um monte de zeni -- o `/obj/Zenni` do original (`Inventory.dm:119-151`).</summary>
	Zeni = 1,

	/// <summary>Uma bolsa de recursos (a materia-prima de `Economia.Recurso`). Nao existe no DM: la recurso era zeni.</summary>
	Recursos = 2,
}

/// <summary>
/// UMA COISA LARGADA NO CHAO: o que e, quanto, onde, e ate quando fica. Vive na memoria do servidor, por zona
/// (`GameServer.Chao.cs`).
/// </summary>
public sealed class Largado
{
	/// <summary>O numero dela no servidor. E o que o cliente manda de volta pra pegar ESTA, e o nome do node na tela.</summary>
	public int Id;

	public TipoDeLargado Tipo;

	/// <summary>O id do item (`CatalogoDeItens`), quando <see cref="Tipo"/> e <see cref="TipoDeLargado.Item"/>. Vazio no resto.</summary>
	public string Item = "";

	/// <summary>Quantas unidades do item, ou a quantia de zeni ou de recursos.</summary>
	public double Quantidade;

	/// <summary>O centro da celula em que ela esta, em pixels do mundo.</summary>
	public float X, Y;

	/// <summary>O instante (relogio do servidor, ms) em que ela some sozinha -- ver <see cref="Chao.SegundosNoChao"/>.</summary>
	public long SomeMs;
}

/// <summary>
/// **LARGAR E PEGAR DO CHAO** -- itens, zeni e recursos.
///
/// Dono, 2026-10-13: *"poder dropar itens e dinheiro ou recurso"*, e depois: *"ter a opçao de dropar zeni e recursos
/// (ambos ja tem sprite, o zeni tinha la no dm, e o de recursos tb tem q era uma bolsinha branca)"*.
///
/// ============================ O QUE O ORIGINAL FAZ, E O QUE VEIO ============================
///   * O ITEM: `obj/items/verb/Drop(n)` (`Grabbing.dm:240-261`) tira `n` da pilha e poe no chao, um passo a frente
///     de quem larga (`DropMe`: `loc = TargetMob.loc`, `step(src, TargetMob.dir)`, `:285-296`); o que esta equipado
///     nao sai ("You must unequip it first"). O `Get()` (`:221-235`) pega o que esta a um passo, se ha vaga na
///     mochila e quem pega esta de pe. Os dois avisam quem esta a vista. VEIO ASSIM.
///   * O ZENI: `obj/Zenni/verb/Drop()` (`Inventory.dm:124-142`) pergunta quanto, tira da carteira e cria o monte um
///     passo a frente; o `Get()` (`:144-151`) soma na carteira. VEIO ASSIM, e o recurso e a mesma coisa com outro
///     saldo (no DM recurso nao existia a parte).
///   * A ARTE DO ZENI e a moeda girando do `ZenniIcon.dmi` -- os `icon_state` "Zenni1".."Zenni4" que o `Drop` escreve
///     por quantia NAO EXISTEM naquele arquivo (ele tem um estado so, sem nome), entao no original todo monte e a
///     mesma moeda. A dos recursos e a bolsa branca da folha `Misc` (estado `zennibag`).
///
/// ============================ O QUE NAO VEIO, E POR QUE ============================
///   * **DAR NA MAO DE QUEM ESTA NA FRENTE.** O `Drop` do DM, com um jogador no passo a frente, poe o item direto na
///     mochila DELE (`:243-249`), sem perguntar. Aqui o item sempre cai no chao e pega quem quiser: uma mochila que
///     enche de coisa alheia sem aceite e o tipo de gesto que este port ja trocou por pedido e resposta em todo
///     lugar (a fusao, o treino, o potencial).
///   * **O CHAO NAO VAI PRO DISCO.** O zeni do original tem `SaveItem = 0` de proposito (*"That way tons of it won't
///     be lying on the ground after a reboot"*); aqui a regra vale pra tudo o que se larga, e mais: o que ninguem
///     pega some depois de <see cref="SegundosNoChao"/>, e cada zona guarda no maximo <see cref="MaximoPorZona"/>
///     coisas (a mais velha sai). Largar e pra passar adiante, nao pra guardar -- pra guardar existe o banco.
/// =================================================================================
/// </summary>
public static class Chao
{
	/// <summary>Larga zeni ou recursos da carteira: `chao_largar zeni/500`. (O item sai pelo `item_largar` da mochila.)</summary>
	public const string VerboLargar = "chao_largar";

	/// <summary>Pega do chao: sem argumento, o que estiver mais perto; `#12`, aquela coisa.</summary>
	public const string VerboPegar = "chao_pegar";

	/// <summary>A palavra que larga a pilha inteira: `item_largar Apple/tudo` -- o `Drop_All()` do original (`Grabbing.dm:236-239`).</summary>
	public const string PalavraDeTudo = "tudo";

	/// <summary>Ate onde se alcanca o que esta no chao: o mesmo alcance da tecla E, medido por eixo como ela.</summary>
	public const float Alcance = Tech.Interacoes.Alcance;

	/// <summary>Quanto tempo uma coisa largada espera alguem antes de sumir.</summary>
	public const double SegundosNoChao = 30 * 60;

	/// <summary>Quantas coisas largadas uma zona guarda. Passou disso, a mais velha some.</summary>
	public const int MaximoPorZona = 150;

	/// <summary>A moeda girando -- `icon='ZenniIcon.dmi'` (`Inventory.dm:120`).</summary>
	public const string FolhaDoZeni = "res://Assets/Sprites/Misc/ZenniIcon.tres";

	/// <summary>A bolsa branca dos recursos: a folha `Misc`, estado `zennibag`.</summary>
	public const string FolhaDosRecursos = "res://Assets/Sprites/Misc/Misc.tres", EstadoDosRecursos = "zennibag";

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--v021teste` e `--diagv021`): largar um item volta a APAGA-LO -- a v0.2.0, em que o
	/// botao da mochila era "Jogar fora". Lido pelo `GameServer.LargarItem`. Falso em jogo, sempre.
	/// </summary>
	public static bool LargarApagaDeTeste;

	/// <summary>A folha e o estado que desenham esta coisa no chao. O item usa a arte dele do catalogo.</summary>
	public static (string Arte, string Estado) Arte(TipoDeLargado tipo, string item) => tipo switch
	{
		TipoDeLargado.Zeni => (FolhaDoZeni, ""),
		TipoDeLargado.Recursos => (FolhaDosRecursos, EstadoDosRecursos),
		_ => CatalogoDeItens.Get(item) is { } def ? (def.Arte, def.Estado) : ("", ""),
	};

	/// <summary>Como esta coisa se chama numa frase: "1.500 zeni", "300 recursos", "Maçã", "3x Maçã".</summary>
	public static string Nome(TipoDeLargado tipo, string item, double quantidade) => tipo switch
	{
		TipoDeLargado.Zeni => $"{Math.Floor(quantidade):N0} {Economia.Recurso.PalavraDeZeni}",
		TipoDeLargado.Recursos => $"{Math.Floor(quantidade):N0} {Economia.Recurso.PalavraDeRecursos}",
		_ => (quantidade > 1 ? $"{quantidade:0}x " : "") + (CatalogoDeItens.Get(item)?.Nome ?? item),
	};

	/// <summary>
	/// ESTAS DUAS COISAS VIRAM UM MONTE SO quando caem na mesma celula? O zeni com o zeni, o recurso com o recurso, e
	/// o item que EMPILHA com outro igual. Maquina, roupa e livro ficam cada um no seu monte: a mochila tambem nao os
	/// junta (`Inventario.Guardar`).
	/// </summary>
	public static bool Junta(Largado a, TipoDeLargado tipo, string item) =>
		a.Tipo == tipo
		&& (tipo != TipoDeLargado.Item
			|| (string.Equals(a.Item, item, StringComparison.OrdinalIgnoreCase) && CatalogoDeItens.Get(item) is { Empilhavel: true }));

	/// <summary>
	/// QUANTO LARGAR, a partir do que o jogador escreveu e do que ele tem: vazio = 1, <see cref="PalavraDeTudo"/> = tudo,
	/// um numero = esse tanto (nunca mais do que ha). Zero quando o texto nao e nada disso.
	/// </summary>
	public static double Quanto(string pedido, double tem, double semPedido = 1)
	{
		if (pedido.Length == 0) return Math.Min(semPedido, tem);
		if (string.Equals(pedido, PalavraDeTudo, StringComparison.OrdinalIgnoreCase)) return tem;
		return double.TryParse(pedido, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n)
			   && !double.IsNaN(n) && !double.IsInfinity(n) && n >= 1
			? Math.Min(Math.Floor(n), tem)
			: 0;
	}
}
