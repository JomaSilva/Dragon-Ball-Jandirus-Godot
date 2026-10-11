using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;

namespace Jandirus.Core.Ai;

/// <summary>
/// O KIT DO DEUS DA DESTRUICAO, DO JEITO QUE A IA O LE -- os dois tiros dele como linhas de arsenal, e o id do
/// verb de agarrar.
///
/// ============================ O QUE MORA AQUI, E O QUE NAO ============================
/// Os NUMEROS DO KIT sao os de `Ranks.KitDoDeus` (o porte de `GodOfDestruction.dm`) e nao se repetem: custo,
/// dano, alcance fisico, carga e criterios do Hakai saem de la, pelas mesmas funcoes que os verbs chamam. O que
/// este arquivo declara e o que nenhum verb diz e toda linha de `TecnicasDeLonge` tem de dizer -- a JANELA em
/// que a IA acerta e o quanto o tiro perdoa --, e isso e julgamento sobre a IA, nao regra do kit.
///
/// QUEM DECIDE e o `Cerebro` (o `EscolherTiro` de sempre, e o `Cerebro.Kit.cs`); quem preenche e o servidor, e
/// so pra corpo sem dono consagrado.
/// ======================================================================================
/// </summary>
public static class KitDoDeusNaIa
{
	/// <summary>
	/// O DESTRUCTION GOD'S FURY: o tiro de toda hora. Janela de 3 a 16 tiles (colado se soca; o teto fisico e 30,
	/// encurtado pela razao de toda linha da tabela -- a janela e onde ele ACERTA), sem conjuracao (a carga do verb
	/// ja e a pausa: `KitDoDeus.TiquesDeCarga`), linha livre (e uma bola reta, que morre no cenario) e precisao de
	/// raio: ele mira o torso e nao se deflete.
	/// </summary>
	public static readonly TecnicasDeLonge.Linha Furia =
		new(KitDoDeus.VerbFuria, 3f, 16f, 0, f => KitDoDeus.CustoDoTiro(esfera: false, f), true, 0.80);

	/// <summary>
	/// A DESTROYER SPHERE: mais cara e mais forte, e a IA so a larga de perto e em quem esta quase parado -- janela
	/// de 3 a 10 tiles e a precisao de um ponto grande, lento de mirar (0,60). Com a tolerancia de um cerebro
	/// esperto isso da: alvo parado na metade de dentro da janela, a esfera; alvo correndo ou longe, o Fury. E e
	/// ela o Hakai A DISTANCIA (`Cerebro.Execucao`).
	/// </summary>
	public static readonly TecnicasDeLonge.Linha Esfera =
		new(KitDoDeus.VerbEsfera, 3f, 10f, 0, f => KitDoDeus.CustoDoTiro(esfera: true, f), true, 0.60);

	/// <summary>
	/// O id do verb de AGARRAR no canal de habilidade -- o mesmo que a tecla do jogador manda. O Power Flick so
	/// existe com alguem nos bracos, e quem poe alguem nos bracos e este verb.
	/// </summary>
	public const string VerbAgarrar = "agarrar";
}
