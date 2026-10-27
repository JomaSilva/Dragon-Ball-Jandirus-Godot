namespace Jandirus.Core.Tech;

/// <summary>
/// O SINTETIZADOR DE VIRUS SAIU DO JOGO -- o pedido 98 do dono (2026-10-26): *"melhor tirar do game essas coisas de injeçao, virus, lsd
/// etc"*. Ele era o `obj/items/Virus_Synth` do DM (`Viruses.dm`, `Injections.dm`), portado na v0.2.1 (lote T5): a maquina sintetizava
/// virus (mudancas permanentes na ficha, com risco de mutacao e de morte), antivirus e injecoes (o LSD, a RAGE, os esteroides, a
/// Insta-Death...), e a MUTACAO tirava vida e passava pelo soco. Tudo isso saiu: a maquina nao se fabrica mais (nem o `construcoes.json`
/// nem o extrator a trazem), os produtos nao existem, e as regras -- a mutacao, o contagio, a raiva da injecao, a morte marcada pela
/// agulha, o fim do LSD -- nao rodam.
///
/// ============================ O QUE FICA, E POR QUE ============================
///   * ESTES IDS: a maquina e os dezenove produtos, pra a aposentadoria os achar -- na mochila de quem os tinha
///     (`Items.ItensAposentados`, no login, com o recado) e aparafusada no chao do mundo (`GameServer.TirarOsSintetizadoresDoMundo`, na
///     carga do `mundo.json`).
///   * O EFEITO PERMANENTE DE QUEM JA TOMOU UM VIRUS fica como esta: a ficha e dele, e o que ela ganhou ou perdeu nao se refaz. So o
///     reforco PASSAGEIRO do LSD de quem salvou sob efeito e desfeito no login (`Stats.Fighter.TirarOLsdAntigo`) -- o relogio que o
///     tirava saiu junto.
///   * A ESPERA PARADA de dez segundos (o `sleep(100)` com a vida e o lugar conferidos) nasceu aqui e continua viva: ela e dos kits do
///     androide de absorcao, e mora com eles (`GameServer.KitsDeAndroide.cs`).
/// ==============================================================================
/// </summary>
public static class Sintetizador
{
	/// <summary>A maquina: o id que ela tinha no catalogo de construcoes, e o tipo da obra aparafusada.</summary>
	public const string Tipo = "Virus_Synth";

	/// <summary>Os dezenove produtos que ela fabricava (`Viruses.dm:36-256`, `Injections.dm:1-120`): o id que a mochila guardava e o nome que o jogador via.</summary>
	public static readonly (string Id, string Nome)[] Produtos =
	[
		("Virus_Alfa", "Virus Alfa"), ("Virus_Bravo", "Virus Bravo"), ("Virus_Charlie", "Virus Charlie"), ("Virus_Delta", "Virus Delta"),
		("Virus_Echo", "Virus Echo"), ("Virus_Golf", "Virus Golf"), ("Virus_Hotel", "Virus Hotel"),
		("Anti_Virus_Delta", "Anti-Virus Delta"), ("Anti_Virus_Echo", "Anti-Virus Echo"), ("Anti_Virus_Mike", "Anti-Virus Mike"),
		("Anti_Virus_Kilo", "Anti-Virus Kilo"),
		("Youthenasia", "Youthenasia"), ("Tail_Injection", "Tail Injection"), ("LSD", "LSD"), ("RAGE", "RAGE"), ("Steroids", "Steroids"),
		("Steroids_v2", "Steroids v2"), ("Insta_Death", "Insta-Death"), ("RAGE_v2", "RAGE v2"),
	];
}
