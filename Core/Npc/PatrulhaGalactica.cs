namespace Jandirus.Core.Npc;

/// <summary>
/// ============================ A PATRULHA GALACTICA -- OS NUMEROS (dono, 2026-10-31, o pedido 198) ============================
/// *"É a policia do jogo, jogadores q matam muitos npcs de um planeta, destroem planetas , etc vao ganhando níveis de procurado na galaxia,
/// ao qual npcs da patrulha galatica irao atras."* Aqui moram so os numeros e as contas puras -- quem vira procurado, quanto vale a cabeca,
/// quanto tempo de pena, quantos patrulheiros e de que tamanho, e quanto a Patrulha demora pra chegar. O que acontece no mundo (o pouso, a
/// cacada, a prisao, a denuncia, o Deus da Destruicao) e do servidor: `GameServer.PatrulhaGalactica.cs`.
///
/// ESCOLHAS MINHAS (o pedido nao diz os numeros; todas a confirmar com o dono):
///   * O CRIME vira PONTOS: cada habitante morto 1 (a realeza 3), cada planeta destruido 25, cada patrulheiro derrotado 2 (resistir a
///     prisao). Os niveis abrem em 5, 15, 30, 60 e 100 pontos -- cinco habitantes ja sao o nivel 1, um planeta destruido o nivel 2, quatro o 5.
///   * NAO HA ESQUECIMENTO: o nivel so zera quando a pena e cumprida (o pedido nao fala de prazo).
///   * A RECOMPENSA pela cabeca: 500 mil, 2, 5, 12 e 25 milhoes de zeni, do nivel 1 ao 5.
///   * A PENA: 5, 10, 20, 40 e 60 minutos reais (*"ate 1h com nivel de procurado mais alto"*).
///   * O ESQUADRAO: 2, 3, 4, 5 e 6 patrulheiros, cada um com 60%, 80%, 100%, 120% e 150% do BP de quem e procurado.
///   * A CHEGADA: 60 s depois de o procurado pousar num planeta pre-feito em que a Patrulha nunca o viu; 10 s onde ja o viu (*"rapidamente
///     localizado"*); e 2 min entre um esquadrao derrotado e o proximo.
/// ===============================================================================================================================
/// </summary>
public static class PatrulhaGalactica
{
	/// <summary>O nivel mais alto de procurado.</summary>
	public const int NivelMaximo = 5;

	/// <summary>Os pontos de cada crime (ver o cabecalho).</summary>
	public const double PontosPorHabitante = 1, PontosPorRealeza = 3, PontosPorPlaneta = 25, PontosPorPatrulheiro = 2;

	/// <summary>Os pontos que abrem os niveis 1 a 5.</summary>
	public static readonly double[] Limiares = [5, 15, 30, 60, 100];

	/// <summary>O nivel de procurado destes pontos: 0 (livre) a <see cref="NivelMaximo"/>.</summary>
	public static int Nivel(double pontos)
	{
		int n = 0;
		foreach (double l in Limiares)
			if (pontos >= l) n++;
		return n;
	}

	/// <summary>A recompensa pela cabeca, em zeni, por nivel (o 0 nao vale nada).</summary>
	public static readonly double[] Recompensas = [0, 500_000, 2_000_000, 5_000_000, 12_000_000, 25_000_000];

	/// <summary>A pena, em minutos reais, por nivel.</summary>
	public static readonly double[] MinutosDePena = [0, 5, 10, 20, 40, 60];

	/// <summary>Quantos patrulheiros descem da nave, por nivel.</summary>
	public static readonly int[] Patrulheiros = [0, 2, 3, 4, 5, 6];

	/// <summary>A forca de cada patrulheiro: a fracao do BP de quem e procurado, por nivel.</summary>
	public static readonly double[] ForcaDoPatrulheiro = [0, 0.6, 0.8, 1.0, 1.2, 1.5];

	public static double Recompensa(int nivel) => Recompensas[Math.Clamp(nivel, 0, NivelMaximo)];
	public static double MinutosDaPena(int nivel) => MinutosDePena[Math.Clamp(nivel, 0, NivelMaximo)];
	public static int QuantosPatrulheiros(int nivel) => Patrulheiros[Math.Clamp(nivel, 0, NivelMaximo)];
	public static double Forca(int nivel) => ForcaDoPatrulheiro[Math.Clamp(nivel, 0, NivelMaximo)];

	/// <summary>A Patrulha chega ao planeta onde ja viu o procurado (*"rapidamente localizado e atacado"*).</summary>
	public const double SegundosNoPlanetaConhecido = 10;

	/// <summary>...e ao planeta pre-feito em que nunca o viu.</summary>
	public const double SegundosNoPlanetaNovo = 60;

	/// <summary>O folego entre um esquadrao derrotado e o proximo.</summary>
	public const double SegundosEntreEsquadroes = 120;

	/// <summary>A parte da recompensa de quem DENUNCIA (*"ganhar uma parte da recompensa (25%)"*) -- e de quem ENTREGA o corpo desmaiado (100%).</summary>
	public const double ParteDaDenuncia = 0.25, ParteDaEntrega = 1.0;

	/// <summary>Por quanto tempo a denuncia vale: a prisao dentro dele paga quem denunciou.</summary>
	public const double MinutosDaDenuncia = 30;

	/// <summary>Quantos tiles do corpo desmaiado quem chama a Patrulha pode estar, e a que distancia da nave os patrulheiros descem.</summary>
	public const int TilesDaEntrega = 4, TilesDoPouso = 5;

	/// <summary>
	/// (2026-10-31, o pedido 202) CADA PATRULHEIRO NA SUA NAVE -- o dono: *"cada npc da patrulha deveria ser uma space pode pousando (com
	/// cratera igual as normais ao entrarem em planetas) [...] eles deveriam pousar de forma dessincronizada e dessas pods sai 1 npc de cada
	/// (cuidado pra n fazer 2 pods pousarem no mesmo lugar ou mt perto pra bugar o sprite da cratera"*. Os pontos ficam a pelo menos
	/// <see cref="TilesEntrePods"/> tiles um do outro -- a cratera do pod tem de 4 a 6 tiles de lado a lado (`Client/Decalques`) --, em aneis
	/// de <see cref="VagasPorAnel"/> vagas a partir de <see cref="TilesDoPouso"/> tiles do procurado, cada anel <see cref="TilesEntreAneis"/>
	/// mais largo; e os pousos vem um a um, de <see cref="SegundosEntrePodsMin"/> a <see cref="SegundosEntrePodsMax"/> s depois do anterior.
	/// </summary>
	public const int TilesEntrePods = 7, VagasPorAnel = 8, TilesEntreAneis = 3, AneisDoPouso = 5;
	public const double SegundosEntrePodsMin = 0.6, SegundosEntrePodsMax = 1.6;

	/// <summary>
	/// O PLANETA PRISAO: o mapa da Terra Negativa (`z20_Negative_Earth`), que nenhuma outra regra do jogo usa -- sem povo, sem saga, fora da
	/// carta estelar (ninguem chega de nave). Na boca do jogo, "o Planeta Prisão da Patrulha Galáctica".
	/// </summary>
	public const string ZonaDaPrisao = "Negative_Earth";

	/// <summary>O nome do planeta prisao na boca do jogo.</summary>
	public const string NomeDaPrisao = "Planeta Prisão da Patrulha Galáctica";

	/// <summary>O molde dos patrulheiros (`npcs.json`): qualquer raca jogavel, com a armadura do exercito do Freeza.</summary>
	public const string MoldeDoPatrulheiro = "patrulheiro_galactico";

	/// <summary>As armaduras do exercito do Freeza (a tabela do soldado, `RoupaDeNpc`): cada patrulheiro veste uma, sorteada.</summary>
	public static readonly string[] Armaduras = ["Armor 8", "Armor Bardock", "Nappa Armor", "RaditzArmorTobiUchiha"];

	/// <summary>A frase do campo de forca -- a de toda saida do planeta prisao enquanto a pena corre.</summary>
	/// <summary>
	/// (2026-10-31, o pedido 198i) O CARGO: o poder pra entrar (*"requisito: 50k de BP"*; o karma acima de 0 e o "nao procurado" sao do
	/// servidor), quanto dura uma missao (dois dias do jogo), e o premio da Patrulha por cumpri-la -- alem do que o vilao e a recompensa
	/// ja pagam. Escolhas minhas, a confirmar.
	/// </summary>
	public const double BpParaEntrar = 50_000, MinutosDaMissao = 48, ZeniDaMissao = 250_000;
	public const int KarmaDaMissao = 3;

	/// <summary>
	/// (2026-10-31, o pedido 200) A MISSAO CONTRA UM VILAO POS-SAGA paga mais -- o dono: *"Faca tb as missoes da patrulha galatica dar
	/// zenni"*. Alem do karma de chefe derrotado que a revanche ja da. Escolha minha, a confirmar.
	/// </summary>
	public const double ZeniDaMissaoDeVilao = 1_000_000;

	public static string FraseDoCampoDeForca(string falta) =>
		$"um campo de força envolve o {NomeDaPrisao}: nada sai daqui enquanto a sua pena não acabar (faltam {falta}).";
}
