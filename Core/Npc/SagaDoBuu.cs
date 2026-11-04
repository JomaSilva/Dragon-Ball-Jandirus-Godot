namespace Jandirus.Core.Npc;

/// <summary>
/// ============================ A SAGA DO MAJIN BOO -- O BOO EM SI (2026-11-01, o pedido 218) ============================
/// O dono, em resumo: o Boo chega GORDO (o Fat Boo, com o sprite do Fat Boo do jogador Corrupted Majin e os poderes de hoje do Majin Boo),
/// NEUTRO, andando pela Terra e so batendo em quem bate nele. Depois de um tempo ele se divide (o Evil Boo e o Fat Boo) e o mau absorve o
/// gordo: nasce o SUPER BOO (o sprite do Majin jogador que absorve a outra metade), agressivo, com 45 bilhoes, que NAO luta no letal -- ele
/// nocauteia e ABSORVE, ficando com a roupa, as skills e as tecnicas de quem engoliu, e dentro dele ha UMA imagem so pra todos os
/// absorvidos. Vencida a imagem, todos saem juntos e ele vira o KID BOO (55 bilhoes): na hora ele joga uma esfera ROSA (a Death Ball, rosa)
/// na Terra -- que entra nos 5 minutos de destruicao -- e some pela Transmissao Instantanea pro Ceu (Heaven), onde fica. Quem o derrota la
/// termina a saga. Em toda etapa ele e um Majin como o do jogador: o soco o despedaca e ele se refaz; so o ki letal o mata.
///
/// AQUI MORA SO A REGRA E OS NUMEROS: os degraus do molde `majin_boo` (o `npcs.json`), o relogio da divisao, o corpo de cada degrau e as
/// frases. Quem move o corpo, absorve, joga a esfera e leva o Kid pro Ceu e o `Server/GameServer.SagaDoBuu.cs`.
///
/// O COMECO DA SAGA (o Babidi, os outros guerreiros) fica pra depois -- o dono: *"faça primeiro a parte do buu em si"*.
/// ==================================================================================================================
/// </summary>
public static class SagaDoBuu
{
	/// <summary>O molde do Boo da saga (`npcs.json`): tres degraus, um por etapa.</summary>
	public const string Molde = "majin_boo";

	/// <summary>Os degraus do molde (0-based, como o `PapelDeNpc.Estagio`).</summary>
	public const int DegrauGordo = 0, DegrauSuper = 1, DegrauKid = 2;

	/// <summary>
	/// QUANTO O FAT BOO ANDA PELA TERRA ANTES DE SE DIVIDIR -- *"apos um tempo o jogo vai falar q ele se dividiu em 2"*. Conta so com ele no
	/// mundo (o servidor fechado nao conta) e atravessa o reinicio (`EstadoDoChefe.SegundosNoDegrau`). ESCOLHA MINHA, a confirmar: 30 minutos.
	/// </summary>
	public const double SegundosAteADivisao = 30 * 60;

	/// <summary>
	/// O CORPO DE CADA DEGRAU. O Fat Boo e o Super Boo sao as folhas da saga do Corrupted Majin jogador -- a forma 1 (o gordo) e a forma 3 (o
	/// que absorveu a outra metade) --, pela faixa reservada dela (`Races.SagaDoMajin.CorpoDaForma`); o Kid Boo e a folha da Forma Pura
	/// (`MajinForm5`, *"o corpo do BOSS Majin Boo (Kid Boo)"*), num indice reservado proprio (<see cref="CorpoDoKidBoo"/>).
	/// </summary>
	public static readonly int CorpoDoFatBoo = Races.SagaDoMajin.CorpoDaForma(1), CorpoDoSuperBoo = Races.SagaDoMajin.CorpoDaForma(3);

	/// <summary>
	/// O KID BOO NUM INDICE RESERVADO do `Appearance.Corpo` -- 204, ao lado do Cell Jr (201), do Guru (202) e da 18 (203). A folha da Forma
	/// Pura do jogador e um corpo de FORMA (sai do catalogo, `CorpoDeForma.MajinPuro`), e o chefe nao entra em forma nenhuma: o desenho viaja
	/// pelo indice, como o da 18.
	/// </summary>
	public const int CorpoDoKidBoo = 204;

	/// <summary>A folha deste indice de corpo, ou nula quando ele nao e o do Kid Boo.</summary>
	public static string? FolhaDoCorpo(int corpo) => corpo == CorpoDoKidBoo ? Races.SagaDoMajin.FolhaDaFormaPura : null;

	/// <summary>
	/// (2026-11-02, o pedido 229) O `"corpoDoBoo"` DE UM DEGRAU DO `npcs.json` NO INDICE DE CORPO -- 1 = o Fat Boo, 2 = o Super Boo, 3 = o Kid
	/// Boo (o dono: *"faça 1= fat 2= super 3 = kid pra ficar facil de entender dps"*); 0, ou fora disso, = o degrau nao troca de corpo. Como o
	/// `"degrauDoBio"` do Cell e o `"corpo"` do Freeza, o desenho e do DADO do degrau e vale pra qualquer corpo que entra nele (a cadeia, o
	/// Inferno, o vilao solto, a lembranca da mente) -- ate o pedido 229 era o codigo da saga, so pro corpo marcado de saga.
	/// </summary>
	public static int IndiceDoCorpoDoBoo(int corpoDoBoo) => corpoDoBoo switch
	{
		1 => CorpoDoFatBoo,
		2 => CorpoDoSuperBoo,
		3 => CorpoDoKidBoo,
		_ => 0,
	};

	/// <summary>(o pedido 229) O maior `"corpoDoBoo"` que existe (o Kid) -- a validacao do molde.</summary>
	public const int UltimoCorpoDoBoo = 3;

	/// <summary>Pra onde o Kid Boo vai depois de jogar a esfera na Terra -- *"ir pro planeta heaven e ficará la"*.</summary>
	public const string ZonaDoKid = World.Alem.ZonaDoCeu;

	/// <summary>Quantos tiles o Super Boo da o bote num caido pra absorve-lo (o do Cell e dez): longe disso, ele vai ate o caido andando.</summary>
	public const int TilesDoBote = 10;

	// =====================================================================
	// AS FRASES
	// =====================================================================
	public const string FraseDaDivisao =
		"MAJIN BOO SE DIVIDIU EM DOIS: um Boo magro e cruel saiu do Boo gordo... e o Boo mau ABSORVEU o gordo! Nasceu o SUPER BOO -- e ele vai atrás de todo mundo.";

	public const string FraseDoKid =
		"o SUPER BOO perdeu todos que tinha absorvido... e o corpo dele encolhe até a forma original: o KID BOO despertou!";

	public const string EmoteDaEsfera = "ergue as mãos ao céu -- e uma esfera ROSA gigantesca começa a crescer sobre ele!";

	public const string FraseDaFuga =
		"o KID BOO jogou uma esfera rosa na Terra e sumiu num instante, rumo ao Céu. Quem quiser acabar com ele vai ter que ir até lá.";
}
