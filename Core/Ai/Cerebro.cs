using Jandirus.Core.World;

namespace Jandirus.Core.Ai;

/// <summary>O que o cerebro decidiu neste instante. O servidor executa; ele nao pensa.</summary>
public readonly struct Decisao
{
	/// <summary>Pra onde andar (ja normalizado). Zero = ficar parado.</summary>
	public Vec2 Rumo { get; init; }

	public bool Atacar { get; init; }
	public bool Pesado { get; init; }
	public bool Guardar { get; init; }
}

/// <summary>
/// O CEREBRO DE UM LUTADOR SEM DONO.
///
/// E logica PURA (mora no Core, nao conhece Godot nem rede): recebe onde eu estou, onde o alvo
/// esta e como estamos de vida e Ki, e devolve o que apertar. Quem move e quem soca e o
/// servidor -- e pelos MESMOS caminhos do jogador, entao o clone obedece as mesmas regras de
/// parede, cadencia e alcance. Um NPC que se move por fora das regras vira o bug que ninguem
/// reproduz.
///
/// COMO ELE LUTA, e por que assim:
///
///   * MANTEM DISTANCIA DE SOCO e nao cola. Colar transforma a luta numa dancinha em que os dois
///     ficam dentro um do outro -- e ninguem le mais nada na tela.
///   * GUARDA QUANDO ESTA MACHUCADO, e nao o tempo todo. Guarda permanente e chato de lutar
///     contra: o jogador so espera. Guardando com pouca vida ele fica PERIGOSO no fim da luta,
///     que e quando a briga deveria ficar tensa.
///   * RECUA pra respirar quando esta quase caindo. E o comportamento que faz o oponente
///     parecer vivo em vez de um saco de pancada que anda.
///   * TEM TEMPO DE REACAO. Decidir a 30 Hz deixa o clone perfeito e injogavel; ele repensa a
///     cada <see cref="IntervaloDeDecisao"/> e mantem a decisao no meio-tempo, que e o que da a
///     ele a hesitacao humana.
/// </summary>
public sealed class Cerebro
{
	/// <summary>Quanto tempo entre duas decisoes. E o "tempo de reacao" do bicho.</summary>
	public double IntervaloDeDecisao = 0.25;

	/// <summary>A que distancia ele tenta ficar. Um tile: dentro do alcance do soco.</summary>
	public float DistanciaIdeal = 34f;

	/// <summary>Fracao de vida abaixo da qual ele passa a guardar e a recuar.</summary>
	public double VidaCautelosa = 0.45;

	/// <summary>Chance de escolher o golpe pesado quando pode. O resto sai leve.</summary>
	public double ChanceDePesado = 0.35;

	private double _relogio;
	private Decisao _atual;
	private double _recuarAte;

	/// <summary>
	/// Pensa (ou repete o que ja tinha decidido). <paramref name="dt"/> em segundos.
	/// </summary>
	public Decisao Pensar(Vec2 minhaPos, Vec2 posAlvo, double minhaVidaFrac, double meuKiFrac,
						  bool alvoCaido, double dt, Random rng)
	{
		_relogio -= dt;
		_recuarAte -= dt;
		if (_relogio > 0) return _atual;
		_relogio = IntervaloDeDecisao;

		// ALVO NO CHAO: para de bater. Nao e piedade -- e que continuar socando um corpo caido
		// nao muda nada no jogo e deixa a cena feia.
		if (alvoCaido)
		{
			_atual = new Decisao { Rumo = Vec2.Zero };
			return _atual;
		}

		Vec2 d = posAlvo - minhaPos;
		float dist = d.Length;
		Vec2 dir = dist > 0.001f ? d.Normalized() : Vec2.Zero;

		bool machucado = minhaVidaFrac < VidaCautelosa;

		// RESPIRAR. Bem machucado, ele se afasta por um instante em vez de trocar golpe --
		// e o que separa um oponente de um boneco.
		if (machucado && _recuarAte <= 0 && rng.NextDouble() < 0.35)
			_recuarAte = 0.9 + rng.NextDouble() * 0.6;

		if (_recuarAte > 0)
		{
			_atual = new Decisao { Rumo = dir * -1f, Guardar = true };
			return _atual;
		}

		// LONGE: anda pra cima. PERTO DEMAIS: recua um pouco. NA MEDIDA: fica e bate.
		Vec2 rumo = Vec2.Zero;
		if (dist > DistanciaIdeal * 1.4f) rumo = dir;
		else if (dist < DistanciaIdeal * 0.6f) rumo = dir * -1f;

		bool noAlcance = dist <= DistanciaIdeal * 1.6f;
		bool temFolego = meuKiFrac > 0.15;

		_atual = new Decisao
		{
			Rumo = rumo,
			Atacar = noAlcance,
			// pesado custa Ki e trava mais tempo: so quando ha folego, e nem sempre
			Pesado = noAlcance && temFolego && rng.NextDouble() < ChanceDePesado,
			// guarda quando machucado E nao esta atacando neste instante
			Guardar = machucado && !noAlcance,
		};
		return _atual;
	}
}
