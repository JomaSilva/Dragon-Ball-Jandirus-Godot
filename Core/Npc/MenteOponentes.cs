namespace Jandirus.Core.Npc;

/// <summary>
/// ============================ QUEM SE ENFRENTA NA MENTE (2026-10-27, o pedido 124c) ============================
/// O documento de um jogador que o dono mandou: *"instead of automatically creating a clone when you enter your inner world, give the
/// player a dropdown list of every being they have become familiar with, with npcs, players, and saga bosses sorted into different
/// categories, make the bp rewards for fighting in this meditation scale on how powerful the enemy is, and disable the ability to end fights
/// early, but if the player is defeated by the npc they summon they are kicked out of the meditation and unable to deeply meditate for a
/// time"*. As escolhas do dono: "como ele pediu, com uma lista com foto de cada um" e "Vitoria e escolhe outro" (derrubar a lembranca a
/// desfaz, paga e devolve a lista -- o reflexo que se reerguia pra sempre acabou). O servidor: `GameServer.MenteLista.cs`; a tela:
/// `Client/TelaDaMente.cs`.
/// </summary>
public static class MenteOponentes
{
	/// <summary>As quatro categorias da lista, na ordem da tela.</summary>
	public enum Tipo : byte { Reflexo = 1, Habitante = 2, Jogador = 3, Chefe = 4 }

	/// <summary>O pedido pelo canal de habilidade: `mente_oponente:&lt;tipo&gt;:&lt;id&gt;`.</summary>
	public const string Prefixo = "mente_oponente:";

	/// <summary>Quanto tempo quem perde na mente fica sem mergulhar: o foco abalado.</summary>
	public const double MinutosSemMergulhoAoPerder = 30;

	/// <summary>
	/// O PREMIO DA VITORIA (escolha minha, 2026-10-27, pro "BP pela forca do oponente"): o BP de tantos segundos de treino (os passes do T,
	/// no ritmo da mente), vezes a forca dele sobre a sua (o poder expresso de cada um), entre o piso e o teto.
	/// </summary>
	public const double SegundosDeTreinoDaVitoria = 60;
	public const double FatorMinimo = 0.25, FatorMaximo = 4;

	public static double Fator(double poderDele, double meuPoder) =>
		Math.Clamp(Math.Max(poderDele, 1) / Math.Max(meuPoder, 1), FatorMinimo, FatorMaximo);

	public static string Pedido(Tipo tipo, string id) => $"{Prefixo}{(byte)tipo}:{id}";

	/// <summary>Le o que vem depois do prefixo: `&lt;tipo&gt;:&lt;id&gt;`.</summary>
	public static bool LerPedido(string resto, out Tipo tipo, out string id)
	{
		tipo = 0;
		id = "";
		int i = resto.IndexOf(':');
		if (i <= 0 || !byte.TryParse(resto[..i], out byte b) || b < (byte)Tipo.Reflexo || b > (byte)Tipo.Chefe) return false;
		tipo = (Tipo)b;
		id = resto[(i + 1)..];
		return true;
	}

	/// <summary>O rotulo de cada categoria na tela.</summary>
	public static string Categoria(Tipo t) => t switch
	{
		Tipo.Reflexo => "Você",
		Tipo.Habitante => "Habitantes",
		Tipo.Jogador => "Jogadores",
		Tipo.Chefe => "Chefes de saga",
		_ => "",
	};

	/// <summary>O tipo de NPC que entra entre os HABITANTES: o cidadao, o lutador, o vilao, o inimigo -- o bicho nao (e o chefe tem a dele).</summary>
	public static bool TipoDeHabitante(TipoDeNpc t) => t is TipoDeNpc.Cidadao or TipoDeNpc.Lutador or TipoDeNpc.Vilao or TipoDeNpc.Inimigo;

	/// <summary>
	/// O HABITANTE LEMBRADO E O MOLDE E O PLANETA ONDE FOI VISTO (`cidadao@Namek`): o cidadao nao tem raca sem o planeta (ela sai do berco
	/// dele, ver `SorteioDeNpc.RacasPossiveis`) -- o de Namek e um namekuseijin, e o da Terra um humano. Uma linha por (molde, planeta).
	/// </summary>
	public static string ChaveDeHabitante(string molde, string planeta) => $"{molde}@{planeta}";

	public static bool LerHabitante(string chave, out string molde, out string planeta)
	{
		int i = chave.IndexOf('@');
		molde = i > 0 ? chave[..i] : "";
		planeta = i > 0 ? chave[(i + 1)..] : "";
		return i > 0 && planeta.Length > 0;
	}
}
