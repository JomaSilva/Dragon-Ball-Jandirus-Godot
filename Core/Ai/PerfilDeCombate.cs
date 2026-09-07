namespace Jandirus.Core.Ai;

/// <summary>
/// O PERFIL DE COMBATE de um corpo dirigido: o que ele ESCOLHE nao usar, mesmo sabendo.
///
/// ============================ O QUE ISTO E, E O QUE NAO E ============================
/// `Capacidades` diz o que o corpo PODE (le as recusas de producao: sabe voar? tem raio? tem
/// Kiai?). O perfil corta por cima: "este aqui luta so com o corpo", "este voa mas nao atira".
/// E o pedido do dono -- *"perfis de NPC com skills variaveis (uns voam, uns usam tecnicas de
/// ki, outros so corpo a corpo)"* -- e tambem o `tourney_can_fly` do `Tournament.dm:100`, que
/// e um perfil sorteado por luta (`TRN_AI_FLY_CHANCE`).
///
/// Ele NUNCA da o que o corpo nao tem: `Filtrar` so desliga. Um NPC com `Voa = true` e sem a
/// maestria de Ki continua sem voar, porque `PodeVoar` ja veio `false` do funil do jogador.
/// =====================================================================================
/// </summary>
public readonly record struct PerfilDeCombate(bool Voa, bool UsaKi)
{
	/// <summary>Sem cortes: tudo o que o corpo sabe, ele usa. E o perfil de todo corpo sem molde.</summary>
	public static readonly PerfilDeCombate Completo = new(true, true);

	/// <summary>So corpo a corpo: nem voa, nem atira, nem sopra.</summary>
	public static readonly PerfilDeCombate SoCorpo = new(false, false);

	/// <summary>
	/// O sorteio de um molde: cada chance e a fracao [0,1] dos corpos daquele molde que saem com
	/// a capacidade. Deterministico pela semente do corpo, como todo o resto do sorteio de NPC --
	/// a mesma semente da o mesmo perfil.
	/// </summary>
	public static PerfilDeCombate Sortear(double chanceDeVoar, double chanceDeKi, ulong semente)
	{
		Random r = Jandirus.Core.Npc.SorteioDeNpc.Sorteador(semente, "perfil");
		return new(r.NextDouble() < chanceDeVoar, r.NextDouble() < chanceDeKi);
	}

	/// <summary>
	/// As capacidades DEPOIS do perfil. So desliga: voo, arsenal de longe e sopro. Reunir Ki
	/// (carregar) fica, porque e coisa do corpo -- um lutador "so corpo" ainda se recupera.
	/// </summary>
	public Capacidades Filtrar(in Capacidades c) => c with
	{
		PodeVoar = c.PodeVoar && Voa,
		DeLonge = UsaKi ? c.DeLonge : Arsenal.Vazio,
		SabeSopro = c.SabeSopro && UsaKi,
	};

	public override string ToString() =>
		Voa && UsaKi ? "completo" : Voa ? "voa, sem ki" : UsaKi ? "ki, sem voo" : "so corpo";
}
