namespace Jandirus.Core.Ranks;

/// <summary>
/// ============================ O RITUAL MISTICO DO VELHO KAIOHSHIN -- pedido do dono, 2026-10-23 ============================
/// *"faca com e kaioshins n tenham a skill unlocked potential e sim uma chamada Elder Kai's Unlock Ability ou em ptbr ritual
/// mistico do Velho Kaiohshin, o jogador fica 10 minutos na frente do outro fazendo o ritual, nenhum pode se mover, ao terminar o
/// potencial oculto desse jogador e despertado e ele ganha a forma mistica (q deve ter o nome renomeado no jogo todo pra Potential
/// Unleashed) , essa forma nao pode ser ensinada diretamente, precisando passar do ritual, so pode fazer esse ritual 1 vez por dia
/// da vida real"*.
///
/// AS DECISOES DO DONO, perguntadas no mesmo dia:
///   * o kit do Kaioshin PERDE a skill `kai/Mystic` -- a que abria a forma e se ensinava, a que o dono chamou de "unlocked potential"
///     -- e GANHA o ritual. O Grand Kai, o Demon Lord e os Ancioes continuam com o Unlock Potential deles (desperta na hora, sem forma);
///   * "so pelo ritual": o CARGO deixa de dar a forma. O proprio Kaioshin precisa que OUTRO Kaioshin faca o ritual nele, e o Kaioshin
///     cuja forma vinha so do cargo a perde quando a skill sai do kit;
///   * UM RITUAL POR DIA DA VIDA REAL, contado no Kaioshin que faz -- e o ritual interrompido antes dos 10 minutos NAO gasta o dia;
///   * "o desejo da; quem tem fica": so o ENSINO sai, por porta nenhuma (o botao, o cartao do aluno, a licao de mestre, a heranca do
///     bio). O desejo do Namek continua dando a forma -- e o defeito em que ela sumia 1 s depois foi consertado --, e quem ja a tem fora
///     do cargo (copia ensinada, desejo) fica com ela;
///   * o nome vira "Potential Unleashed" no jogo todo, nas duas linguas. Os ids internos ficam (o "mistico", a rede 305, a linha
///     `LinhaDeForma.Mistico`, o verbo e a skill `Mystic`): sao chave de save, de maestria, de censo e de robo.
///
/// O QUE MORA AQUI: os nomes e os numeros que as duas pontas dividem (o botao, a trava do cliente, a skill que saiu) e as contas puras
/// (o dia, o que o ritual ainda tem pra dar). A regra viva -- a oferta, a trava, o que quebra o ritual, o fim -- mora no
/// `GameServer.RitualDoKaioshin.cs`; a forma que vira direito gravado, no `GameServer.ReconciliarODomDoMistico`.
/// ==========================================================================================================================
/// </summary>
public static class RitualDoKaioshin
{
	/// <summary>A chave do cargo que faz o ritual (`Core/Ranks/Ranks.cs`): o Kaioshin, em qualquer uma das cinco vagas.</summary>
	public const string Cargo = "kaioshin";

	/// <summary>O id que viaja pelo canal de habilidade (`C2S.Habilidade`); a chave do botao na tabela de requisitos e `hab:` + ele.</summary>
	public const string Verbo = "ritual_kaioshin";

	/// <summary>
	/// O NOME DO RITUAL -- o mesmo no botao (aba Skills), no painel do cargo e nas frases. E o portugues do dono, com a grafia dele
	/// ("Kaiohshin"); o ingles, tambem dele ("Elder Kai's Unlock Ability"), mora no dicionario (`Assets/Data/lingua-en.json`).
	/// </summary>
	public const string Nome = "Ritual Místico do Velho Kaiohshin";

	/// <summary>
	/// A SKILL QUE ERA A FORMA (`/datum/skill/kai/Mystic`, `Race Trees/kai.dm:12-32`): saiu do kit, nao se ensina mais
	/// (`Skills.EnsinoDeSkill.SoPeloRitual`) e a copia que o cargo deu sai do livro. O typepath fica -- e chave de save e de catalogo.
	/// </summary>
	public const string SkillDoMistico = "/datum/skill/kai/Mystic";

	/// <summary>DEZ MINUTOS um na frente do outro (o dono). Relogio de PAREDE, como o dia.</summary>
	public const long DuracaoMs = 10 * 60_000;

	/// <summary>
	/// UM POR DIA DA VIDA REAL (o dono): 24 h de relogio de parede desde o ultimo ritual TERMINADO -- o molde da recarga da Sala do
	/// Tempo (`MsDeRecargaDaSala`). Tempo real e nao do jogo: e o descanso do jogador, nao do personagem.
	/// </summary>
	public const long RecargaMs = 24L * 60 * 60_000;

	/// <summary>Quanto o convite fica na mesa: o minuto do Unlock Potential (`PrazoDaOfertaDePotencialMsG11`), cujo cartao ele usa.</summary>
	public const long PrazoDaOfertaMs = 60_000;

	/// <summary>De quanto em quanto tempo os dois ouvem o progresso no chat: um minuto -- dez marcas num ritual.</summary>
	public const long AvisoACadaMs = 60_000;

	/// <summary>
	/// QUANTO UM CORPO PODE SAIR DO LUGAR sem quebrar o ritual, em pixels. Os dois estao travados (`PodeMexerOCorpo`), entao o que mexe
	/// um deles vem de FORA -- um arremesso, um teleporte, um agarrao: a folga e a do Instant Transmission (`TickDaTransmissaoG11`).
	/// </summary>
	public const float FolgaDaAncoraPx = 4f;

	/// <summary>
	/// QUAO PERTO o alvo tem de estar quando aceita, em tiles. A oferta pede o corpo NA FRENTE (o alcance do soco, `QuemEstaNaFrente`);
	/// quem aceita um minuto depois pode ter virado o rosto, e o que importa no aceite e nao ter saido do lado do Kaioshin.
	/// </summary>
	public const float TilesDoAceite = 1.5f;

	/// <summary>
	/// O EFEITO QUE TRAVA O PASSO NO CLIENTE (`MandarEfeito`, o mesmo canal do `torneio_espera`): o servidor recusa o passo, e o cliente
	/// avisado nem o preve -- senao o boneco andaria e "teleportaria de volta" a cada pacote.
	/// </summary>
	public const string EfeitoDaTrava = "ritual_kaioshin";

	/// <summary>Quanto falta pro Kaioshin poder fazer outro ritual, em ms. Zero = pode. <paramref name="feitoEm"/> zero = nunca fez.</summary>
	public static long FaltaPraOProximo(long feitoEm, long agora) =>
		feitoEm <= 0 ? 0 : Math.Max(0, feitoEm + RecargaMs - agora);

	/// <summary>"5h07m" -- horas e minutos, como a Sala do Tempo diz a recarga dela (o DM tambem mostra os dois).</summary>
	public static string HorasEMinutos(long ms) => $"{ms / 3_600_000}h{ms % 3_600_000 / 60_000:00}m";

	/// <summary>
	/// O QUE O RITUAL AINDA DA A ESTE CORPO: o despertar do potencial (uma vez na vida -- quem ja despertou ganha so a forma) e a
	/// forma. Os dois falsos = nada a dar, e o Kaioshin ouve isso na oferta, antes de o alvo ficar dez minutos parado a toa.
	/// </summary>
	public static (bool Potencial, bool Forma) OQueDa(double unlockPotential, bool temAForma) => (unlockPotential < 1, !temAForma);
}
