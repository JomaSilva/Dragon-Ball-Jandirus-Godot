namespace Jandirus.Core.Npc;

/// <summary>
/// **O ZENI QUE UM NPC CARREGA** -- o que se rouba dele nocauteado, e o que fica com quem mata o vilao.
///
/// Dono, 2026-10-13: *"matar vilao deveria dar zenni como reward"* e *"ter como roubar recursos e zeni de nocauteados
/// (npc ou player)"*.
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// No DM o NPC nao CARREGA nada: o zeni nasce na morte (`mob/proc/mobDeath`, `mobDeath.dm:7-21`) -- um `obj/Zenni`
/// no chao com `rand(100,8000)`, ou `rand(500000,800000)` quando `isBoss`. So se ganha matando.
///
/// ============================ O QUE MUDOU, E POR QUE ============================
///   * **A QUANTIA NASCE COM O CORPO**, e nao com a morte: e o que da ao pedido do dono (roubar de NPC nocauteado)
///     alguma coisa pra roubar. Ela sai da MESMA semente do resto da ficha (`SorteioDeNpc`), entao o mesmo habitante
///     carrega a mesma bolsa em todo reinicio.
///   * **O CHEFE DE SAGA** leva os numeros do original, 1:1.
///   * **O CIDADAO leva dez vezes MENOS** que o mob comum do DM (10 a 800 no lugar de 100 a 8.000). La o mob comum e
///     um inimigo de mato que ataca; aqui e o habitante pacifico de uma cidade, sao dezenas por planeta e ele volta a
///     nascer -- com os numeros do original, nocautear a praca inteira seria a melhor fonte de zeni do jogo, na frente
///     da furadeira e do tributo. DECISAO MINHA, declarada: o dono pediu a mecanica, nao a quantia.
///   * **O VILAO AVULSO** (que nao existe no DM) fica entre os dois: e o premio de um evento com relogio e teto
///     (`VilaoAvulso`: um por planeta, a cada 12 a 30 minutos).
///   * O BICHO nao carrega zeni (ele deixa carne), nem o lutador de torneio (ele nasce pra chave e some).
/// ==============================================================================
/// </summary>
public static class BolsaDeNpc
{
	/// <summary>O habitante: um decimo do `rand(100,8000)` do mob comum do DM. Ver o cabecalho.</summary>
	public const double ZeniMinDoCidadao = 10, ZeniMaxDoCidadao = 800;

	/// <summary>O vilao avulso: o premio de quem o mata. Sem par no DM.</summary>
	public const double ZeniMinDoVilao = 5_000, ZeniMaxDoVilao = 15_000;

	/// <summary>O chefe de saga: `A.zenni=rand(500000,800000)` (`mobDeath.dm:17`).</summary>
	public const double ZeniMinDoChefe = 500_000, ZeniMaxDoChefe = 800_000;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): o NPC volta a nascer de bolso vazio -- a v0.2.0. Lido pela propria
	/// <see cref="Zeni"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool NpcSemBolsaDeTeste;

	/// <summary>
	/// Quanto zeni este NPC carrega ao nascer. Funcao pura do tipo e da semente dele: o sorteio e o de sempre (um
	/// passo de mistura da semente), e a quantia e inteira.
	/// </summary>
	public static double Zeni(TipoDeNpc tipo, ulong semente)
	{
		if (NpcSemBolsaDeTeste) return 0;
		(double min, double max) = tipo switch
		{
			TipoDeNpc.Cidadao or TipoDeNpc.Inimigo => (ZeniMinDoCidadao, ZeniMaxDoCidadao),
			TipoDeNpc.Vilao => (ZeniMinDoVilao, ZeniMaxDoVilao),
			TipoDeNpc.Chefe => (ZeniMinDoChefe, ZeniMaxDoChefe),
			_ => (0, 0),
		};
		if (max <= 0) return 0;

		// SplitMix64: um passo, pra a bolsa nao andar junto com os outros sorteios que saem da mesma semente.
		ulong z = unchecked(semente + 0x9E3779B97F4A7C15UL * 77UL);
		z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
		z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
		z ^= z >> 31;
		double fracao = (z >> 11) / (double)(1UL << 53);
		return Math.Floor(min + (max - min) * fracao);
	}
}
