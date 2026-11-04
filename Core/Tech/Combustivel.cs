using Jandirus.Core.World;

namespace Jandirus.Core.Tech;

/// <summary>
/// (2026-10-18) O COMBUSTIVEL DAS NAVES -- os NUMEROS, e so eles. Quem gasta, abastece, para a nave e manda a barra e o
/// `GameServer.Combustivel.cs`; quem desenha a barra e o `PainelDeCombustivel`.
///
/// O dono: *"a nave tanto pod quanto a capital ship deveria ter combustivel q gasta com a distancia andada, assim precisando
/// pousar de forma estrategica em planetas pra pegar recursos e abastecer a nave, entao se a nave viaja mt rapido, ela gasta mais
/// combustivel por segundo ja q ela viaja uma maior distancia por segundo"*. Perguntado no mesmo dia, ele escolheu: ao leme quem
/// gasta e SO a nave (o Ki do piloto nao desce por pilotar), e com o tanque vazio a nave PARA DE VEZ -- no espaco ela fica a deriva
/// ate alguem trazer recursos.
///
/// ============================ DESIGN NOVO DO DONO -- NADA DISTO EXISTE NO DM ============================
/// Nenhuma das tres naves do original consome nada (`grep fuel` no Finale: zero). Os numeros abaixo sao ESCOLHA MINHA, medidos contra
/// o tamanho do universo deste port (`Espaco.DistanciaTerraNamek` ~50 mil tiles; um sistema solar a cada ~2 mil tiles, `Sistemas`):
///
///     nave            tanque   gasto por tile   alcance de um tanque    Terra->Namek
///     Spacepod         1.000        0,05          20.000 tiles (~40%)    duas paradas
///     Capital Ship     5.000        0,20          25.000 tiles (~50%)    uma parada
///
/// O GASTO E POR DISTANCIA, e so por ela -- a frase do dono. O POR SEGUNDO sai sozinho: o pod no degrau 1 anda 5 tiles/s e gasta
/// 0,25/s; no 39 anda 195 tiles/s e gasta 9,75/s. A viagem custa o mesmo tanque nas duas velocidades; a rapida so o esvazia mais cedo.
///
/// O FOGUETE NAO TEM TANQUE: ele e uma janela de 25 s no espaco que volta sozinha (`Naves.SegundosEmOrbita`) -- nao ha viagem pra
/// pagar.
///
/// (2026-10-31, o pedido 209) O TANQUE CRESCE POR DEGRAUS -- *"adicione uma forma de melhoria na pod e capital ship de aumentar o tanque
/// de combustivel"*. Ver <see cref="DegrausDoTanque"/>.
/// ==========================================================================================================
/// </summary>
public static class Combustivel
{
	/// <summary>Esta nave tem tanque? O pod (as duas entradas do catalogo) e a Capital Ship; o foguete nao.</summary>
	public static bool TemTanque(string tipo) => Naves.EhPod(tipo) || NaveGrande.EhNaveGrande(tipo);

	/// <summary>
	/// O tanque cheio desta nave, em unidades de combustivel, com os <paramref name="degraus"/> de melhoria que ela tem (o pedido 209;
	/// zero = o de fabrica). Zero pra quem nao tem tanque.
	/// </summary>
	public static double Capacidade(string tipo, int degraus = 0) =>
		(Naves.EhPod(tipo) ? TanqueDoPod : NaveGrande.EhNaveGrande(tipo) ? TanqueDaNaveGrande : 0)
		* (1 + FracaoPorDegrau * Math.Clamp(degraus, 0, DegrausDoTanque));

	/// <summary>O tanque do pod. Ver a tabela do cabecalho.</summary>
	public const double TanqueDoPod = 1_000;

	/// <summary>O tanque da Capital Ship: cinco do pod -- ela e uma sala de 100x100 que voa.</summary>
	public const double TanqueDaNaveGrande = 5_000;

	/// <summary>Quanto esta nave gasta por TILE andado. Ver a tabela do cabecalho.</summary>
	public static double GastoPorTile(string tipo) =>
		Naves.EhPod(tipo) ? GastoPorTileDoPod : NaveGrande.EhNaveGrande(tipo) ? GastoPorTileDaNaveGrande : 0;

	public const double GastoPorTileDoPod = 0.05;
	public const double GastoPorTileDaNaveGrande = 0.20;

	/// <summary>Quantos tiles um tanque cheio leva esta nave. So conta -- quem gasta e o <see cref="GastoDoPasso"/>.</summary>
	public static double TilesDeAlcance(string tipo, int degraus = 0) =>
		GastoPorTile(tipo) > 0 ? Capacidade(tipo, degraus) / GastoPorTile(tipo) : 0;

	/// <summary>
	/// O MAIOR PASSO DE UM TIQUE QUE CONTA COMO VIAGEM: o dobro do passo da nave mais rapida (39x, ~208 px por tique). Um salto maior que
	/// isso entre dois tiques nao foi andado -- foi teleporte (uma tecnica do piloto, um ajuste do servidor) --, e cobrar milhares de
	/// pixels de uma vez esvaziaria o tanque por uma coisa que a nave nao fez. Vale o teto, e nao zero: um arranque de verdade anda.
	/// </summary>
	public static double MaiorPassoQueConta => 2 * Naves.PassoPorTique(Naves.VelocidadeMaxima);

	/// <summary>
	/// QUANTO ESTA NAVE GASTA PRA ANDAR <paramref name="px"/> PIXELS -- a regra inteira do dono numa conta: so a distancia, aparada pelo
	/// <see cref="MaiorPassoQueConta"/>. Quem nao tem tanque gasta zero.
	/// </summary>
	public static double GastoDoPasso(string tipo, double px) =>
		Math.Clamp(px, 0, MaiorPassoQueConta) / ZoneCollision.TileSize * GastoPorTile(tipo);

	/// <summary>
	/// ABASTECER: quantos RECURSOS pagam uma unidade de combustivel. Recurso e o que se minera num planeta (o verb Minerar, `Recurso`) --
	/// *"pousar ... em planetas pra pegar recursos e abastecer a nave"*. Um por um: o tanque do pod enche com cinco minutos e meio de
	/// mineracao na mao, ou 17 s de pa.
	/// </summary>
	public const double RecursosPorUnidade = 1.0;

	/// <summary>
	/// A FOLGA DE "CHEIO": o tanque que esta a menos de meia unidade da boca conta como cheio. O gasto e em fracao de unidade a cada
	/// tique, e sem a folga uma nave que andou um pixel nunca mais estaria "cheia" pra ir pra mochila.
	/// </summary>
	public const double FolgaDoCheio = 0.5;

	/// <summary>O tanque desta nave esta cheio (com a folga)?</summary>
	public static bool Cheio(string tipo, double nivel, int degraus = 0) => nivel >= Capacidade(tipo, degraus) - FolgaDoCheio;

	/// <summary>A fracao do tanque (0 a 1). Zero pra quem nao tem tanque.</summary>
	public static double Fracao(string tipo, double nivel, int degraus = 0) =>
		Capacidade(tipo, degraus) > 0 ? Math.Clamp(nivel / Capacidade(tipo, degraus), 0, 1) : 0;

	// =====================================================================
	// (2026-10-31, o pedido 209) O TANQUE MAIOR
	// =====================================================================
	/// <summary>
	/// QUANTOS DEGRAUS O TANQUE SOBE. O dono: *"adicione uma forma de melhoria na pod e capital ship de aumentar o tanque de
	/// combustivel"*. Os numeros foram ESCOLHA MINHA, e o dono os confirmou em 2026-11-01 (*"Legal, ta bom assim oq vc escolheu"*):
	/// cada degrau soma METADE do tanque de fabrica (<see cref="FracaoPorDegrau"/>: o pod +500, a Capital Ship +2.500), e no ultimo o
	/// tanque e 6x o de fabrica -- o pod vai 120 mil tiles sem parar (a Terra->Namek e a volta), a Capital Ship 150 mil. O espaco novo vem
	/// VAZIO (ver `GameServer.MelhorarOTanque`): o tanque maior poupa paradas, e nao combustivel.
	/// </summary>
	public const int DegrausDoTanque = 10;

	/// <summary>O que cada degrau soma, em fracao do tanque de fabrica.</summary>
	public const double FracaoPorDegrau = 0.5;

	/// <summary>
	/// O PRECO DE UM DEGRAU: a escada da velocidade (`Naves.CustoDoUpgrade`, o degrau seguinte custa mais) no tamanho do tanque --
	/// 10 zeni por unidade que o degrau soma, vezes o numero do degrau. O pod: 5.000 zeni o primeiro, 50.000 o decimo (275 mil os dez);
	/// a Capital Ship, cinco vezes isso. Em recursos, o dobro (a regra de toda melhoria, `Recurso.CustoEmRecursos`).
	/// </summary>
	public static double CustoDoDegrau(string tipo, int degraus) =>
		ZeniPorUnidadeDoDegrau * Capacidade(tipo) * FracaoPorDegrau * (Math.Max(degraus, 0) + 1);

	public const double ZeniPorUnidadeDoDegrau = 10;
}
