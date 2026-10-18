using Jandirus.Core.World;

namespace Jandirus.Core.Ai;

/// <summary>
/// AS ATIVAS RACIAIS QUE NAO CABEM NUMA LINHA DO REPERTORIO, NAS DECISOES DO CEREBRO (o dono, 2026-10-17: *"n esqueça de fazer as IAs
/// saberem usar as passivas e ativas da raça"*). As outras onze sao linhas do repertorio (`AtivasRaciaisNaIa`) e saem pelo
/// `ComORepertorio` de sempre. Tres pedem receita propria:
///
///   O DEMONIC SAIYAN  pronto (`Capacidades.DemonicoPronto`: o kit, a forma Lendaria, os 2 minutos de luta, a recarga), o cerebro o pede
///                     quando da pra ficar 15 s PARADO: carregando o Ki (o plano de recuperar ja decidiu que ninguem esta perto) ou com o
///                     alvo longe (<see cref="DistanciaSegura"/>). Colado ele apanharia, e o primeiro empurrao desfaria a carga.
///   O RUTHLESS MIND   a execucao entra pela MESMA porta do Hakai (`ExecutaOAlvo` -> `Plano.Executar`, `Cerebro.Kit.cs`): o alvo caido
///                     e a execucao na mao (`Capacidades.ExecucaoRacialPronta` -- o verb exige o golpe LETAL, e corpo sem dono no port
///                     so derruba: ela acende so pra quem foi feito letal).
///   O DESABAFO        os dois Transbordar cobram do TRONCO, e a conta dispara depois do prazo de desabafar
///                     (`Passivas.TroncoPorSegundoDoTransbordar`: quem nao desabafa cai). Entao o segundo aperto nao espera o Ki baixo nem
///                     o fim da luta como o dos outros reforcos: sai com o alvo perto (a rajada e um golpe -- doze tiros em volta), com a
///                     luta acabada, ou vencida a <see cref="FolgaDoDesabafo"/>.
///
/// PARADO PELA RACA: enquanto a esfera carrega ou a execucao corre, o corpo NAO ANDA (andar desfaz as duas) -- guarda, de frente pro
/// alvo, e nenhum plano ate elas acabarem.
/// </summary>
public sealed partial class Cerebro
{
	/// <summary>
	/// Entre duas tentativas do Demonic. A carga que se desfaz (o corpo foi empurrado) nao gasta a recarga, e sem esta pausa ele a pediria
	/// de novo no quadro seguinte -- de pe no meio da luta, a espera do proximo empurrao.
	/// </summary>
	public const double PausaDoDemonico = 20;

	/// <summary>
	/// Passado o prazo de desabafar, quanto o transbordar ainda espera um alvo perto. Dez segundos custam ao tronco uns 4% a mais
	/// (`Passivas.TroncoPorSegundoDoTransbordar`) e valem mais um degrau do bonus; vinte ja custariam 15%.
	/// </summary>
	public const double FolgaDoDesabafo = 10;

	/// <summary>A rajada sai com o alvo ate aqui: doze tiros em leque, um a cada 30 graus, acertam quem esta perto.</summary>
	public const float AlcanceDoDesabafo = 5 * ZoneCollision.TileSize;

	/// <summary>
	/// Entre dois pedidos de desabafo. Maior que o relogio de 1 Hz das capacidades: o transbordar desabafado ainda aparece pronto por ate um
	/// segundo, e um aperto nesse meio tempo o RELIGARIA.
	/// </summary>
	public const double PausaDoDesabafo = 3;

	/// <summary>DEFEITO INJETADO (bancada `--iaraciaisteste`): o cerebro nunca desabafa -- o transbordar corre ate o tronco derrubar o corpo.</summary>
	public static bool SemDesabafoDeTeste;

	private double _pausaDoDemonico, _pausaDoDesabafo;

	/// <summary>Os relogios da raca, no topo do <see cref="Pensar"/> com os outros.</summary>
	private void TicarARaca(double dt)
	{
		if (_pausaDoDemonico > 0) _pausaDoDemonico -= dt;
		if (_pausaDoDesabafo > 0) _pausaDoDesabafo -= dt;
	}

	/// <summary>A raca prende o corpo no lugar (a esfera do Demonic, a execucao): guarda e olha o alvo, sem dar um passo.</summary>
	private static Comando? ParadoPelaRaca(in Percepcao p) =>
		p.ParadoPelaRaca ? new Comando { Guardar = true, Olhar = p.TemAlvo ? p.Direcao : default } : null;

	/// <summary>O Demonic agora? Pronto, numa luta de verdade, e com o corpo podendo ficar parado: carregando o Ki, ou com o alvo longe.</summary>
	private Comando? DemonicoAgora(in Percepcao p)
	{
		if (!Poderes.DemonicoPronto || _pausaDoDemonico > 0 || RepertorioCalado || !p.TemAlvo || p.AlvoCaido || p.NaAgua) return null;
		if (Atual != Plano.Recuperar && p.Distancia < DistanciaSegura) return null;
		_pausaDoDemonico = PausaDoDemonico;
		if (Explicando) Porque = "raca: o Demonic Saiyan (da pra ficar parado)";
		return new Comando { Habilidade = Races.Passivas.Demonico, Guardar = true, Olhar = p.Direcao };
	}

	/// <summary>
	/// O DESABAFO, por cima do gesto do plano: um pulso, e o corpo nao para. Ele marca o USO da linha -- e o reforco descansa a partir dali
	/// (`ReforcoPraLigar`): o transbordar desabafado nao religa no segundo seguinte.
	/// </summary>
	private Comando ComODesabafo(in Percepcao p, Comando gesto)
	{
		if (Poderes.Desabafo is not { } id || _pausaDoDesabafo > 0 || SemDesabafoDeTeste || gesto.Habilidade is { Length: > 0 }) return gesto;
		bool perto = p.TemAlvo && !p.AlvoCaido && p.Distancia <= AlcanceDoDesabafo;
		bool acabou = _semAlvoHa >= SegundosSemAlvoAteDesligar;
		if (!perto && !acabou && Poderes.DesabafoHa < FolgaDoDesabafo) return gesto;
		_pausaDoDesabafo = PausaDoDesabafo;
		Usei(id);
		if (Explicando) Porque = $"raca: desabafar ({(perto ? "o alvo esta perto" : acabou ? "a luta acabou" : "a folga venceu")})";
		return gesto with { Habilidade = id };
	}
}
