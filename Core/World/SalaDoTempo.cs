namespace Jandirus.Core.World;

/// <summary>
/// O QUE AS DUAS PONTAS PRECISAM SABER SOBRE A SALA DO TEMPO -- e nada mais que isso.
///
/// ============================ POR QUE UMA CASA SO ============================
/// A Sala e a unica zona do jogo que **nao tem beirada**: o quarto desenhado a mao (500x500,
/// praticamente um tile branco repetido) fica no meio de um vazio branco que continua pra sempre.
/// Isso e um pedido do dono e nao existe no original -- la a sala era o mapa e acabou.
///
/// Essa unica frase ("aqui nao ha borda") e lida em TRES lugares que nao se conhecem:
///
///   * o SERVIDOR, pra a colisao fora do bitset ser chao livre e nao parede (`ZoneCollision`);
///   * o CLIENTE, pela mesma colisao -- se as duas pontas discordarem sobre onde ha parede, o
///     corpo treme no muro (e o modo de falha classico deste projeto);
///   * o PINTOR do cenario, pra pedir pedacos fora da faixa do `.pedacos` (`PlanetaPreFeito`).
///
/// Escrever `nome == "Hyperbolic_Time_Chamber"` nos tres seria a mesma regra dita tres vezes, e a
/// primeira a envelhecer seria a do cliente, calada -- exatamente o que aconteceu com o
/// `ZonaEhPlaneta` antes de virar <see cref="Espaco.EhPlaneta"/>. Aqui e uma so.
/// =============================================================================
///
/// ============================ E ELA JA ERA DUAS ============================
/// `GameServer.SalaDoTempo.ZonaDaSala` guardava este mesmo nome pro gate da porta. Ele passou a
/// apontar pra ca em vez de repetir a string: o nome da zona e do MUNDO, nao do servidor.
/// ==========================================================================
///
/// O nome e o da AREA do `.dmm` (o que o manifesto de mapas chama de `zona`), e **nao** o da ficha
/// de planeta -- o `planetas.json` a chama de "Hyperbolic Time Dimension", que e o nome do
/// `switch(Planet)` do DM, e quem casa os dois e o apelido em `CatalogoDePlanetas`.
/// </summary>
public static class SalaDoTempo
{
	/// <summary>O nome da zona (a area do `.dmm` do z13).</summary>
	public const string Zona = "Hyperbolic_Time_Chamber";

	/// <summary>Esta zona e a Sala do Tempo?</summary>
	public static bool EhASala(string nomeDaZona) =>
		string.Equals(nomeDaZona, Zona, StringComparison.OrdinalIgnoreCase);

	/// <summary>A mesma pergunta com a chave de zona -- so `Premade` pode ser a Sala.</summary>
	public static bool EhASala(ZoneKey z) => z.Kind == ZoneKey.KindPremade && EhASala(z.Name);

	/// <summary>
	/// ESTA ZONA NAO TEM BEIRADA: fora do mapa desenhado ha chao, e nao o fim do mundo.
	///
	/// Hoje so a Sala responde `true`, e o metodo existe separado do <see cref="EhASala"/> de
	/// proposito: quem le a colisao nao quer saber QUE lugar e este, quer saber se o bitset acaba
	/// o mundo ou nao. Se um dia outro vazio existir (uma dimensao mental, um limbo), ele entra
	/// aqui sem que a colisao precise aprender nomes de mapa.
	///
	/// CUIDADO AO ACRESCENTAR: a zona que responder `true` aqui tambem tem que ficar fora da volta
	/// do planeta (<see cref="Espaco.EhPlaneta"/>), senao o corpo da a volta na beirada do BITSET e
	/// nunca chega ao vazio. A Sala ja esta fora porque nao aparece na carta estelar.
	/// </summary>
	public static bool SemBorda(string nomeDaZona) => EhASala(nomeDaZona);

	/// <inheritdoc cref="SemBorda(string)"/>
	public static bool SemBorda(ZoneKey z) => EhASala(z);

	// =====================================================================
	// O RITMO DA SALA -- os dois multiplicadores que a zona impoe
	// =====================================================================
	/// <summary>
	/// MAESTRIA DE FORMA SOBE 4x DENTRO. **Regra do dono (13.6d), e ela nao e o 280 do BP.**
	///
	/// O ganho de BP vale 280x porque a Sala e tempo comprimido -- um dia la dentro vale um ano de
	/// treino la fora (28 dias x 10 meses; ver <see cref="Stats.GainKnobs.TimeChamberMult"/>).
	/// Maestria e outra moeda: e o COSTUME de sustentar a forma. Acelerar as duas no mesmo fator
	/// entregaria a escada inteira dominada numa sessao de 48 minutos, e a maestria e justamente o
	/// que dispensa a cinematica e destrava os graus -- a sessao pagaria pela propria remocao.
	///
	/// Os dois numeros sao DELIBERADAMENTE diferentes. Esta frase existe aqui pra que ninguem, ao ler
	/// `TimeChamber.dm` e ver um 280 so, "corrija" a inconsistencia.
	/// </summary>
	public const double MaestriaMult = 4;

	/// <summary>
	/// ESCREVE OS DOIS RITMOS NA FICHA -- entrando E saindo, numa chamada so.
	///
	/// ============================ POR QUE OS DOIS JUNTOS, E POR QUE NO CORE ============================
	/// Sao dois sistemas (BP e maestria) com dois numeros (280 e 4), e a tentacao e cada um ligar o
	/// seu onde precisa. O modo de falha disso e conhecido e barato de evitar: **ligar e esquecer de
	/// desligar**. Um jogador que saisse da Sala com `zoneGainMult = 280` no bolso treinaria a 280x
	/// na Terra pra sempre, e nada na tela diria de onde veio.
	///
	/// Aqui a pergunta e feita UMA vez ("esta zona e a Sala?") e as duas respostas saem juntas, nas
	/// duas direcoes. O servidor chama isto em TODA troca de zona (`GameServer.AplicarGravidade`, que
	/// e o funil por onde login, passagem, nave, admin e planeta gerado passam) -- e como a funcao
	/// escreve o valor NEUTRO fora da Sala, esquecer de chamar na saida e impossivel: a proxima
	/// chamada, seja onde for, ja limpa.
	///
	/// **`HBTCMod` NAO ENTRA AQUI, e isso e a decisao registrada.** O BYOND roda dois sistemas de Sala
	/// ao mesmo tempo -- o novo (`TimeChamber.dm`, 280x via `htc_gain_mult()`) e o legado
	/// (`Stats.dm:486-510`, `HBTCMod = 25` multiplicando `Egains`) --, e nos caminhos de treino eles
	/// se MULTIPLICAM: 7000x. Ninguem desenhou isso. O port porta so o novo; `Fighter.HBTCMod` fica
	/// valendo 1 pra sempre porque `Fighter.Power.cs` o le dentro de `Egains` e apaga-lo mexeria numa
	/// formula que nao e desta camada. Quem for mexer aqui: **nao escreva `HBTCMod`**, ou o ganho da
	/// Sala volta a ser 7000x sem ninguem ter pedido.
	/// ================================================================================================
	/// </summary>
	/// <param name="sessaoValendo">
	/// A SESSAO DESTE CORPO AINDA RENDE? Ver <see cref="SessaoValendo"/>.
	///
	/// Estar DENTRO nao basta: aos 2 dias in-game os bonus acabam e a pessoa continua la (a regra do
	/// dono e que a sala **nao expulsa**, ela prende -- 13.6c). Sem este segundo argumento, quem
	/// ficasse preso treinaria a 280x pra sempre, e a prisao viraria premio.
	/// </param>
	public static void AplicarRitmo(Stats.Fighter f, string nomeDaZona, bool sessaoValendo = true)
	{
		bool dentro = EhASala(nomeDaZona) && sessaoValendo;
		f.zoneGainMult = dentro ? Stats.GainKnobs.TimeChamberMult : 1;
		f.zoneMasteryMult = dentro ? MaestriaMult : 1;
	}

	// =====================================================================
	// A SESSAO -- quanto tempo a sala rende, e em que unidade isso se conta
	// =====================================================================
	/// <summary>
	/// A SESSAO DURA DOIS DIAS IN-GAME (`HTC_MAX_YEARS = 2`, um ano de treino por dia de jogo).
	///
	/// ============================ POR QUE DIAS, E NAO MINUTOS ============================
	/// O relogio deste mundo anda um dia a cada <see cref="Ceu.SegundosPorDia"/> segundos reais
	/// (1440 = 24 min), entao 2 dias sao ~48 minutos -- que e o numero que o dono cita. Contar os
	/// 48 MINUTOS direto daria o mesmo resultado hoje e mentiria amanha: no dia em que o dia do
	/// mundo mudar de duracao, a "sessao de 2 anos de treino" continuaria durando 48 minutos e
	/// deixaria de ser dois dias de nada.
	///
	/// O DM conta ANOS (`htc_session_years`) pelo mesmo motivo -- ele diz em voz alta que a conta
	/// anda com o relogio do treino pra que **lag nao roube tempo**. Aqui a conta anda por TIQUE de
	/// passo fixo (`Protocol.TickSeconds`), que e a mesma promessa: um servidor lento entrega a
	/// sessao inteira, so demorando mais em minutos de parede.
	/// =====================================================================================
	/// </summary>
	public const double SessaoEmDias = 2;

	/// <summary>
	/// UM ANO DE IDADE POR DIA IN-GAME dentro (`HTC_AGE_PER_GAME_DAY = 1`). A sessao inteira
	/// envelhece exatamente 2 anos, que e o que ela paga em treino.
	/// </summary>
	public const double AnosPorDia = 1;

	/// <summary>
	/// A JANELA PRA SAIR, EM MINUTOS REAIS (regra do dono 13.6c). Dois minutos entre o fim dos
	/// bonus e a porta trancar.
	///
	/// EM MINUTOS REAIS e nao em dias in-game, e a diferenca e de proposito: a janela nao e tempo
	/// de jogo, e o tempo que um par de pernas leva pra atravessar o quarto e alcancar a porta.
	/// Ela vira dias na <see cref="JanelaEmDias"/> so pra caber na mesma unidade da contagem.
	/// </summary>
	public const double MinutosDaJanela = 2;

	/// <summary>
	/// A TRAVA DE PAREDE (`HTC_WALL_CAP_MULT = 2`): a rede de seguranca do relogio de verdade.
	///
	/// A contagem oficial e por tique, e ela PARA quando o corpo nao esta em jogo (deslogado la
	/// dentro nao treina nem envelhece -- e o `if(!client) continue` do DM). Sem uma segunda conta,
	/// bastaria deslogar pra congelar a sessao pra sempre e voltar dias depois com os 280x
	/// intactos. Esta e a segunda conta: passado o dobro do tempo NOMINAL de parede desde a
	/// entrada, a sessao acabou, tenha o tique contado o que tiver.
	/// </summary>
	public const double TravaDeParedeMult = 2;

	/// <summary>Quantos minutos REAIS dura um dia in-game (24, hoje).</summary>
	public static double MinutosReaisPorDia => Ceu.SegundosPorDia / 60.0;

	/// <summary>Quantos dias in-game passam por segundo real de tique.</summary>
	public static double DiasPorSegundo => 1.0 / Math.Max(Ceu.SegundosPorDia, 1);

	/// <summary>A janela de saida na unidade da contagem (dias in-game).</summary>
	public static double JanelaEmDias => MinutosDaJanela / Math.Max(MinutosReaisPorDia, 1e-9);

	/// <summary>A sessao inteira em minutos REAIS -- 48, com o relogio de hoje. So pra texto.</summary>
	public static double SessaoEmMinutosReais => SessaoEmDias * MinutosReaisPorDia;

	/// <summary>
	/// A TRAVA DE PAREDE em milissegundos reais, contados da ENTRADA. 96 min com o relogio de hoje.
	/// </summary>
	public static double TravaDeParedeEmMs =>
		SessaoEmMinutosReais * TravaDeParedeMult * 60_000;

	/// <summary>Esta sessao ainda rende? (dias ja gastos abaixo do teto).</summary>
	public static bool SessaoValendo(double diasGastos) => diasGastos < SessaoEmDias;

	/// <summary>Quantos minutos REAIS faltam pra a sessao acabar. Negativo depois do fim.</summary>
	public static double MinutosQueFaltam(double diasGastos) =>
		(SessaoEmDias - diasGastos) * MinutosReaisPorDia;
}
