using Godot;
using Jandirus.Core.Combat;

namespace Jandirus.Server;

/// <summary>
/// ============================ O MAJIN DESFEITO EM PEDACOS (dono, 2026-10-15) ============================
/// *"majin n ta dando pra ser nocauteado no soco, tem o efeito de KO mas ele n desmaia,ele deveria desmaiar mas levantar
/// mais rapido q outras raças, majin "morto" por soco n morre de verdade ele so fica em um KO mais longo e começa uma
/// animaçao q tinha no DM representando ele regenerando e voltando a vida, morrer pra ki é oq mata definitivamente"*.
///
/// AS REGRAS MORAM NO CORE, e uma vez so: o piso do desmaio (`CombatState.SegundosMinimosDoDesmaio`), a pergunta do
/// nucleo perdido que o soco, o tiro e o dano direto fazem do mesmo jeito (`CombatState.QuandoONucleoCede`), e o corpo
/// em pedacos com o relogio do nocaute (`CombatState.Desfeito`). A TELA desenha os pedacos pelo `Ocupacao.Desfeito`
/// (`Client/PedacosDoMajin.cs`). Aqui mora so o que o jogador LE -- e quem e Majin (o `CorpoDeMajin`, que o gancho
/// `SeDesfazAoMorrer` pergunta; os dois ganchos sao instalados no `PrepararCombate`).
/// ============================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O gancho `CombatState.AoSeDesfazer`: as frases. *"You will regenerate in 30 seconds."* (`DeathRegen.dm:162`).</summary>
	private void OMajinSeDesfez(ServerPlayer pl)
	{
		Avisar(pl, $"seu corpo se desfaz em pedaços -- você se refaz em {CombatState.SegundosDesfeito:0} segundos.");
		AvisarPertoG3(pl, RaioDaVista, $"{pl.Name} se desfaz em pedaços!", excetoCentro: true);
		GD.Print($"[server] {pl.Name} se DESFEZ em pedacos (Majin) -- se refaz em {CombatState.SegundosDesfeito:0} s");
	}
}
