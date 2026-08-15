namespace Jandirus.Core.World;

/// <summary>
/// O VACUO COBRA -- quem nao respira no espaco sufoca, por segundo, ate morrer.
///
/// ============================ O QUE ESTE ARQUIVO E, EM UMA FRASE ============================
/// Ele e a REGRA: quem respira, quanto custa nao respirar e em quanto tempo o corpo cede. Quem
/// pergunta "este corpo esta no vacuo AGORA" e o servidor (`GameServer.Vacuo.cs`), porque isso
/// depende de zona, de nave e de mochila -- coisas que o Core nao conhece.
/// ========================================================================================
///
/// ============================ O PEDIDO DO DONO, E O QUE O DM DIZ ============================
/// O dono pediu: *"faca todas as racas MENOS a FROST DEMON, ALIEN e MAJIN sofrerem DANO POR
/// SEGUNDO NO ESPACO pois elas n conseguem respirar la"*. Sao TRES racas, e e essa lista que esta
/// escrita em <see cref="RacasQueRespiram"/>.
///
/// **O DM e MUITO mais generoso, e a divergencia e deliberada.** A flag de la se chama
/// `spacebreather` e nasce do genoma (`Genetic_Datum.dm:299-300`, `assign_Space_Breath()`, lendo
/// `misc_stats["Space Breath"]`). Medindo os 23 protos raciais do original, **DEZESSEIS** respiram:
///
///     Alien (statalien.dm:270)        Android (statandroid.dm:48)     Arlian (statarlian.dm:36)
///     Bio-Android (statbiodroid.dm:51) Demon (statdemon.dm:38)        Halfbreed (stathalfbreed.dm:44)
///     Frost Demon/Icer (staticer.dm:48) Kai (statkai.dm:43)           Majin (statmajin.dm:47)
///     Makyo (statmakyo.dm:37)         Meta (statmeta.dm:37)           Saibamen (statsaiba.dm:39)
///     Shapeshifter (statshapeshifter.dm:37) Spirit Doll (statspirit.dm:37)
///     Tsujin (stattsujin.dm:48)       Yardrat (statyardrat.dm:49)
///
/// e SETE nao: Demigod, Gray, Heran, Human, Kanassa-Jin, Namekian (rework de 2026-07-18,
/// `statnamek.dm:60`) e Saiyan.
///
/// O `races.json` deste port JA CARREGA esse campo (`misc["Space Breath"]`, os 24 protos, com os
/// valores exatos do DM) -- e nenhuma linha de C# jamais o leu. **Ele continua nao sendo lido**, de
/// proposito: obedecer o dono e obedecer o dono, e ler o JSON aqui daria calado a respiracao a treze
/// racas que ele acabou de mandar sufocar. Se um dia ele quiser 1:1 com o original, a troca e de
/// UMA linha -- <see cref="RacaRespira"/> passa a perguntar `RaceProto.MiscStat("Space Breath") > 0`.
/// ========================================================================================
///
/// ============================ E POR ISSO A PERGUNTA NAO E UMA LISTA DE RACA ============================
/// <see cref="RespiraNoVacuo"/> recebe a raca E um `folegoConcedido`. O segundo termo existe porque
/// no DM `spacebreather` **nao e so raca**, e ha tres portadores ja mapeados que este port ainda nao
/// tem -- e quando tiver, eles plugam AQUI sem tocar no dano:
///
///   * o **Deus da Destruicao** ganha a flag pelo CARGO (`Ranks/GodOfDestruction.dm:121`) e a devolve
///     ao perde-lo (`:143`). **Este ja esta ligado** -- ver <see cref="CargosQueRespiram"/>, que a
///     bancada `--vacuoteste` cobra nominalmente (a promessa estava escrita em `Ranks.cs:556` e nao
///     era cumprida por linha nenhuma);
///   * o **androide de laboratorio** ganha ao ser convertido (`DNALabs.dm:185`) -- maquina nao respira;
///   * o **Rebreather_Module**, implante de torax de ciborgue (`Cyborgs.dm:751-770`), que mexe em
///     `spacebreather` e nao em `spacesuit`, ou seja e permanente enquanto instalado.
///
/// E ha um quarto que este port ja poderia querer: a **fusao** (`Fusion.dm:278-279`) -- se um dos dois
/// respira, o fundido respira, e o defunde restaura (`:317,371`).
/// ==================================================================================================
/// </summary>
public static class Vacuo
{
	/// <summary>
	/// QUANTO TEMPO UM CORPO DESPROTEGIDO AGUENTA NO VACUO, em segundos.
	///
	/// **Vinte, e o numero e do original.** `Stats.dm:120` nasce com `spacetime = 100`, e o laco do
	/// `mob/proc/Stats()` decrementa um por volta com `sleep(sleep_tiem)` e `sleep_tiem = 2`
	/// (`Stats.dm:126` e `:511`) -- e `sleep(2)` no BYOND e 0,2 s, nao 2 ticks. 100 x 0,2 = 20 s.
	/// Ao sair, o contador volta a 100 na hora (`Stats.dm:223-224`), e e isso que este port faz
	/// tambem: nao ha "meio sufocado" guardado entre duas idas ao espaco.
	///
	/// ============================ O QUE MUDOU EM RELACAO AO DM ============================
	/// La, ao fim dos 20 s, o codigo e `to_chat(view(), "[src] suffocates and dies!")` seguido de
	/// `spawn Death()` -- **zero de vida perdida, sem nocaute, sem membro ferido, morte instantanea**.
	/// O dono pediu DANO POR SEGUNDO, entao o prazo virou ORCAMENTO: os mesmos 20 s, gastos como
	/// dano espalhado. O corpo morre na mesma hora do original, mas visivelmente -- ele sangra, ele
	/// desmaia antes (o nocaute cai aos 16 s, quando o primeiro nucleo cruza os 20% de
	/// `Regras.LimiarQuebra`), e da tempo de alguem puxar quem ficou pra tras.
	/// ==================================================================================
	/// </summary>
	public const double SegundosDeFolego = 20.0;

	/// <summary>
	/// AS TRES RACAS QUE O DONO NOMEOU. **"Icer" e o nome do proto; "Frost Demon" e o da CLASSE**
	/// dentro dele -- ver <see cref="Jandirus.Core.Races.FormasDeFrost.Raca"/>, que documenta que um
	/// `Race == "Frost Demon"` nunca casa com ninguem. As duas grafias entram aqui pelo mesmo motivo
	/// que o `EhFrost` de la aceita as duas: o erro e facil e mudo.
	/// </summary>
	public static readonly string[] RacasQueRespiram =
	[
		Jandirus.Core.Races.FormasDeFrost.Raca,          // "Icer"
		Jandirus.Core.Races.FormasDeFrost.ClasseNormal,  // "Frost Demon", a grafia que circula
		"Alien",
		"Majin",
	];

	/// <summary>Esta raca respira no vacuo por nascimento?</summary>
	public static bool RacaRespira(string? raca)
	{
		if (string.IsNullOrEmpty(raca)) return false;
		foreach (string r in RacasQueRespiram)
			if (string.Equals(r, raca, System.StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	/// <summary>
	/// ============================ OS CARGOS QUE DAO FOLEGO ============================
	/// **O `spacebreather` do DM nao e so raca, e o Deus da Destruicao e a prova.** Ele ganha a flag
	/// ao assumir o titulo (`Ranks/GodOfDestruction.dm:118-121`, junto com o `noage`) e a DEVOLVE ao
	/// perde-lo (`:143`) -- ou seja o folego e do CARGO e nao do corpo, e sai junto com o trono.
	///
	/// **ISTO ESTAVA ESCRITO E NAO ESTAVA LIGADO**, que e o defeito mais caro deste repo: a propria
	/// ficha do cargo neste port ja prometia *"Nao envelhece, **respira no vacuo**, e carrega o
	/// Hakai"* (`Core/Ranks/Ranks.cs:556` e a linha de `Concede` que cita `GodOfDestruction.dm:105`),
	/// e nenhuma linha de codigo lia isso. A bancada `--vacuoteste` pede uma prova nominal pra ele --
	/// e uma promessa escrita na descricao de um cargo que o sistema nao cumpre e pior que uma
	/// ausencia, porque o jogador leu.
	///
	/// A lista e por CHAVE de cargo (<see cref="Jandirus.Core.Ranks.RankDef.Chave"/>) e nao por nome
	/// exibido, pelo mesmo motivo de sempre: nome se traduz, chave nao.
	/// ==============================================================================
	/// </summary>
	public static readonly string[] CargosQueRespiram = ["godofdestruction"];

	/// <summary>Este cargo concede folego no vacuo? ("" = nenhum cargo.)</summary>
	public static bool CargoRespira(string? cargo)
	{
		if (string.IsNullOrEmpty(cargo)) return false;
		foreach (string c in CargosQueRespiram)
			if (string.Equals(c, cargo, System.StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	/// <summary>
	/// ============================ A PERGUNTA UNICA ============================
	/// **Todo caminho que precise saber se um corpo aguenta o vacuo passa por aqui.** Nao ha uma
	/// segunda lista de raca em lugar nenhum do port, e nao pode haver: a primeira a divergir seria
	/// a que alguem esqueceu de atualizar no dia em que o dono mudasse a lista.
	///
	/// `racaDoPai` entra pela MESMA regra que todo requisito racial deste port ja segue -- o DM
	/// pergunta `Race == X || Parent_Race == X` e meio-sangue vale pelos dois (ver
	/// `Core/Ranks/Ranks.cs` e `PortasDeCargo.cs:135`). Filho de Majin com humano respira.
	///
	/// `traje` e a roupa espacial e `folegoConcedido` e tudo o mais (cargo, conversao, implante,
	/// fusao). Sao dois parametros e nao um porque eles tem VIDAS diferentes: o traje sai da mochila
	/// e pode ser largado no chao; o folego concedido e do corpo.
	///
	/// **O ABRIGO NAO ESTA AQUI de proposito.** Quem esta dentro de uma pod ou de uma nave nao
	/// "respira o vacuo": ele nao esta no vacuo. Misturar as duas coisas faria esta funcao precisar
	/// saber de naves, e ela nao sabe nem o que e uma zona.
	/// ======================================================================
	/// </summary>
	public static bool RespiraNoVacuo(string? raca, string? racaDoPai = null,
									  bool folegoConcedido = false, bool traje = false)
		=> folegoConcedido || traje || RacaRespira(raca) || RacaRespira(racaDoPai);

	/// <summary>
	/// O DANO POR SEGUNDO **em cada membro**, a partir da vida cheia de um nucleo.
	///
	/// ============================ POR QUE ELE NAO OLHA O BP ============================
	/// O calor da estrela olha (`CalorDaEstrela.DanoPorSegundo` divide pelo poder do corpo), e esta
	/// e a diferenca inteira entre as duas coisas: **fogo e uma forca que se resiste, ar e ar**. Um
	/// corpo de 1e13 de BP nao tem oxigenio nenhum a mais que um de 25 -- e no DM ele tambem nao
	/// tinha: `spacetime` e o mesmo 100 pra qualquer um. Sem esta linha escrita, o primeiro
	/// rebalanceamento poria um divisor de BP aqui "por simetria com o sol" e o fim de jogo inteiro
	/// passaria a nadar no vacuo de graca.
	///
	/// **A REGENERACAO NAO PRECISA SER COMPENSADA**, e isso e medido e nao suposto: o dano entra pelo
	/// `EspalharDanoG3`, que chama `EntrarEmCombate()`, e a cura passiva (`RegenerarPassivo`) tem
	/// `c.EmCombate > 0` na propria guarda. Ou seja o corpo sufocando nao regenera, e os 20 s sao 20 s.
	/// (E o contrario do sol, que precisou de `CalorDaEstrela.FatorMinimo` justamente porque a coroa
	/// nao poe ninguem em combate.)
	/// ==============================================================================
	/// </summary>
	public static double DanoPorSegundo(double vidaCheiaDoNucleo) =>
		vidaCheiaDoNucleo <= 0 ? 0 : vidaCheiaDoNucleo / SegundosDeFolego;
}
