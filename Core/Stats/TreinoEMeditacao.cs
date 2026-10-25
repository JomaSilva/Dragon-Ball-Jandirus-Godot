namespace Jandirus.Core.Stats;

/// <summary>
/// **QUANDO SE PODE TREINAR E MEDITAR** -- a regra pura do `C2S.Activity`, pra comecar e pra continuar.
///
/// ============================ DE ONDE VEM ============================
/// O dono (2026-10-11), sobre a lista de validacoes fracas do servidor: *"resolva todos esses problemas"*. Treinar
/// e meditar eram DECLARADOS pelo cliente e o servidor so anotava: um cliente mexido treinava andando, voando,
/// brigando ou nocauteado, e o ganho de treino e de gravidade corria junto. A unica regra ("nao se treina
/// correndo") morava no cliente.
///
/// O QUE O ORIGINAL PERGUNTA, e onde:
///   * `Train()`:    `if(!blasting&amp;&amp;!flight&amp;&amp;!KO)` (`Train.dm:72`);
///   * `Meditate()`: `if(!blasting&amp;&amp;!flight&amp;&amp;!KO&amp;&amp;!swim&amp;&amp;!boat)` (`Meditate.dm:23`);
///   * o ganho de cada volta: `trainproc` pede `!KO` (`Train.dm:87`) e `medproc` tambem (`Meditate.dm:60`);
///   * `Deoccupy()` (`medtrain freemove.dm:1-12`: "You stop meditating." / "You stop training.") derruba os dois,
///     e quem o chama e cada PASSO (`movement handler.dm:217-219`) e a DECOLAGEM (`start_flying()`,
///     `flying.dm:107-108`);
///   * o nocaute desliga os dois (`KO()`, `KO.dm:56-57` -- ja portado no `CombatState.Nocautear`);
///   * pilotar conta como voo (`pilot.flight = 1`, `PlanetTech.dm:135`): ao leme nao se treina.
///
/// O QUE O ORIGINAL NAO DIZ, e ficou pela regra mais simples (decisoes desta rodada, anotadas):
///   * BRIGA: quem trocou golpe com ALGUEM nos ultimos segundos (o relogio de luta, o mesmo da mineracao) nao
///     comeca, e quem apanha ou bate para. E o "igual meditacao: vc n pode se mover nem fazer nada" que o dono
///     escreveu da mineracao, lido de volta pra meditacao.
///   * AGARRADO: "preso num agarrao voce nao se concentra" -- a frase que as tecnicas de teleporte ja dizem
///     (`GameServer.Tecnicas.G4.cs`).
///   * POSSUIDO (a fera, a furia): quem perdeu as redeas nao treina nem medita. Ja era regra em duas pontas (o
///     despacho recusa o pacote, a posse desliga o que estava ligado); esta e a terceira.
///
/// O QUE FICOU DE FORA DE PROPOSITO:
///   * **O MORTO DE PE TREINA**: o `Train()` nao pergunta `dead`, e o Outro Mundo e onde o morto treina. Quem
///     nao treina e o CADAVER (o corpo no chao, antes da viagem).
///   * **O PRESO DA SALA DO TEMPO, O SELADO E QUEM ESPERA A VEZ NO TORNEIO TREINAM**: a Sala existe pra isso, o
///     `Train()` nao pergunta por selo, e o participante do torneio ja entra na espera com a atividade que tinha.
///   * **O ESMAGAMENTO NAO BARRA**: treinar sob gravidade pesada e o proposito dela (na mineracao ele barra). (2026-10-23) O que
///     barra e o PESO DEMAIS -- acima de 8x o limite do corpo (dono: *"vc nem conseguiria treinar assim"*, `Fighter.PesoDemaisPraTreinar`);
///     gravidade alta sem peso nenhum continua sendo treino.
///   * `Swim()` tambem chama `Deoccupy()` uma vez (`Swim.dm:8`), mas o `Train()` nao pergunta `swim`: no DM se
///     volta a treinar dentro d'agua na tecla seguinte. Aqui nadar so barra a MEDITACAO, que e o que o verbo diz.
///   * O raio na mao (`charging`/`beaming`) nao barra: no DM e ao contrario -- treinar ou meditar SOLTA o raio
///     (`AreYaBeamingKid`), e o `TickDosCanaisDeKi` ja faz isso. O que barra e o `blasting` (uma tecnica
///     sustentada no ar), que e o que os dois verbos perguntam.
/// =====================================================================
/// </summary>
public static class TreinoEMeditacao
{
	/// <summary>
	/// O efeito (`S2C.Efeito`, a zero) com que o servidor conta ao dono do corpo que a atividade DELE caiu, ou que
	/// o pedido foi recusado: a pose e o HUD do cliente so sabem do que ele mesmo declarou.
	/// </summary>
	public const string EfeitoDeQueCaiu = "atividade_caiu";

	/// <summary>Quanto o corpo pode sair de onde comecou (px) sem que isso conte como ter andado -- a folga da mineracao.</summary>
	public const float FolgaDePosicao = 4f;

	/// <summary>
	/// POR QUE ESTE CORPO NAO PODE TREINAR (ou meditar) AGORA -- vazio = pode. A MESMA pergunta pra comecar e pra
	/// seguir: <paramref name="emCurso"/> so muda a frase.
	/// </summary>
	/// <param name="meditar">Verdadeiro = meditar; falso = treinar.</param>
	/// <param name="cadaver">Morto e ainda no chao (o morto de pe, no Outro Mundo, nao e cadaver).</param>
	/// <param name="possuido">Sem as redeas: quem dirige o corpo e o servidor.</param>
	/// <param name="fera">Em forma de Oozaru: a fera treina e nao medita (`GameServer.DefinirAtividade`).</param>
	/// <param name="voando">No ar -- por conta propria ou ao leme de uma nave.</param>
	/// <param name="atirando">Com uma tecnica de ki sustentada no ar (o `blasting` do DM).</param>
	/// <param name="emBriga">Trocou golpe com outro corpo nos ultimos segundos.</param>
	/// <param name="saiuDoLugar">Andando, ou fora do ponto em que a atividade comecou.</param>
	/// <param name="pesoDemais">(2026-10-23) Com o peso acima de 8x o limite do corpo (`Fighter.PesoDemaisPraTreinar`): nada rende.</param>
	public static string PorQueNao(bool meditar, bool emCurso, bool ko, bool cadaver, bool possuido, bool fera,
								   bool agarrado, bool voando, bool nadando, bool atirando, bool emBriga,
								   bool saiuDoLugar, bool pesoDemais)
	{
		string o = meditar ? "meditar" : "treinar";
		string fim = meditar ? "você para de meditar." : "você para de treinar.";

		if (ko || cadaver) return emCurso ? fim : $"caído não dá pra {o}.";
		if (possuido) return emCurso ? fim : $"agora não dá pra {o}.";
		if (meditar && fera) return "a fera não medita.";
		// (2026-10-23) O PESO DEMAIS, antes de tudo o que se resolve parando ou pousando: tirar o peso e a unica saida, e a frase a diz
		if (pesoDemais)
			return emCurso ? $"o peso passou de {GainKnobs.RazaoQueImpedeOTreino:0.#}x o que o seu corpo aguenta: {fim}"
						   : $"com esse peso você não consegue {o}: ele passa de {GainKnobs.RazaoQueImpedeOTreino:0.#}x o que o seu corpo aguenta. Tire peso (menu P, aba Equip).";
		if (agarrado) return emCurso ? $"preso num agarrão você não se concentra: {fim}" : "preso num agarrão você não se concentra.";
		if (voando) return emCurso ? $"no ar não dá: {fim}" : $"pouse antes: não dá pra {o} no ar.";
		if (meditar && nadando) return emCurso ? $"dentro d'água não dá: {fim}" : "não dá pra meditar dentro d'água.";
		if (atirando) return emCurso ? fim : $"não dá pra {o} com uma técnica de ki no ar.";
		if (emBriga) return emCurso ? $"a briga interrompe: {fim}" : $"não dá pra {o} no meio de uma briga.";
		if (saiuDoLugar) return emCurso ? fim : $"pare antes: não dá pra {o} andando.";
		return "";
	}
}
