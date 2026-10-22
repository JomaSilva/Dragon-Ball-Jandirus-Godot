using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-21) AS SUB-ZONAS NA TELA, E A TRILHA DO PERIGO -- os dois pedidos do dono do mesmo dia, do lado do cliente:
///   * *"sincronizar o horario, clima e efeitos de destruiçao de planetas em sub zonas do planeta, como cavernas do planeta vegeta com o
///     proprio planeta, lookout com a terra"*: o CEU desenhado e o do planeta da sub-zona -- a hora, a lua, o clima. A regra mora no Core
///     (<see cref="SubZonas"/>); aqui ela vira a luz da cena (<see cref="AcertarCeuDoLugar"/>), e a unica coisa que pede o quadro e o
///     castelo do Rei Vegeta, que fica num canto do Lookout e tem o ceu de Vegeta (<see cref="TickDoCeuDoLugar"/>);
///   * *"enquanto vc estiver em um planeta q esta explodindo, a trilha sonora do jogo vai ser a mesma q toca ao entrar em combate, o mesmo
///     vale quando vc ta em um planeta q ta com boss de saga ativo"*: a camada de COMBATE do `AudioDirector` fica de pe enquanto o chao em
///     que se pisa esta em perigo (<see cref="TickDaTrilhaDoPerigo"/>), com a mesma pasta de faixas da briga. Acabado o perigo, a musica do
///     lugar volta pelas transicoes de sempre.
/// O ceu forcado, o tremor e o fim do mundo chegam do servidor pelos canais deles (`GameServer.SubZonas.cs`); o que o cliente decide
/// sozinho e o que ele DESENHA e o que ele TOCA.
/// </summary>
public partial class World
{
	// =====================================================================
	// O CEU DO LUGAR
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (robo `--diagsubzonas`): o ceu so e escolhido na carga da zona -- quem voa do Lookout pro castelo do Rei Vegeta
	/// continua debaixo do ceu da Terra. Lido pela propria linha, em <see cref="TickDoCeuDoLugar"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool CeuSoNaCargaDeTeste;

	/// <summary>A zona cujo ceu esta na luz da cena agora -- o planeta da sub-zona, ou a propria zona.</summary>
	private ZoneKey _ceuNaLuz;

	/// <summary>De que zona e o ceu que a cena desenha agora. So o robo le.</summary>
	public ZoneKey CeuNaLuzDeTeste => _ceuNaLuz;

	/// <summary>
	/// POE NA LUZ O CEU DESTE LUGAR: o relogio, a ficha de clima e o sal do sorteio do planeta do chao (a caverna e o Lookout, o do planeta
	/// deles; o castelo do Rei Vegeta, o de Vegeta). <paramref name="onde"/> e o ponto de chegada da carga, ou nulo pra posicao do corpo.
	///
	/// `--menteantiga` DESFAZ A LUZ JUNTO -- ver o comentario do `CarregarZona`, onde a razao esta escrita: a mente vira o interior generico
	/// de antes, `Ceu.SemCeu`.
	/// </summary>
	private void AcertarCeuDoLugar(ZoneKey zona, Vector2? onde)
	{
		Vector2 p = onde ?? PosicaoLocal ?? Vector2.Zero;
		ZoneKey ceu = SubZonas.ZonaDoCeu(zona, new Vec2(p.X, p.Y));
		_ceuNaLuz = ceu;
		_luzDoMundo.Relogio = MenteAntiga && DimensaoMental.EhAMente(zona)
			? Jandirus.Core.World.Ceu.SemCeu
			: Planetas.Relogio(ceu);
		_luzDoMundo.ClimaDaqui = Planetas.Clima(ceu);
		_luzDoMundo.SalDoClima = Clima.SalDaZona(ceu);
	}

	/// <summary>
	/// A FRONTEIRA DO CASTELO, quadro a quadro. So o Lookout tem dois ceus (`SubZonas.TemDoisCeus`), e e a unica zona em que isto faz
	/// alguma conta: quem voa por cima das nuvens do Templo ate o castelo do Rei Vegeta passa pro ceu de Vegeta (e o servidor manda o ceu
	/// forcado de la no segundo seguinte -- `GameServer.TickDasSubZonas`).
	/// </summary>
	private void TickDoCeuDoLugar()
	{
		if (CeuSoNaCargaDeTeste || !SubZonas.TemDoisCeus(_zonaDoAtual) || PosicaoLocal is not { } p) return;
		if (SubZonas.ZonaDoCeu(_zonaDoAtual, new Vec2(p.X, p.Y)).Equals(_ceuNaLuz)) return;
		AcertarCeuDoLugar(_zonaDoAtual, p);
	}

	/// <summary>O planeta do chao em que o corpo local pisa (o da sub-zona, ou a propria zona). Ver `TickDaAgonia`.</summary>
	private ZoneKey PlanetaDoMeuChao(GameClient cli) =>
		PosicaoLocal is { } p ? SubZonas.ZonaDoCeu(cli.Zone, new Vec2(p.X, p.Y)) : SubZonas.ZonaDoCeu(cli.Zone);

	// =====================================================================
	// A TRILHA DO PERIGO
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (robo `--diagsubzonas`): o chao em perigo nao toca nada -- o jogo de antes de 2026-10-21. Lido pela propria linha,
	/// em <see cref="TickDaTrilhaDoPerigo"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool PerigoMudoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (robo `--diagsubzonas`): a tag de combate caindo cala a trilha mesmo com o chao em perigo -- a regra da tag de
	/// antes, sozinha. Lido pela propria linha, em <see cref="CombateSeguradoPeloPerigo"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool TagCalaOPerigoDeTeste;

	/// <summary>O perigo do chao esta segurando a camada de combate agora?</summary>
	private bool _perigoTocando;

	/// <summary>O perigo do chao em que o corpo local pisa, como o ultimo quadro viu. So o robo le.</summary>
	public PerigoDoChao PerigoDoChaoDeTeste { get; private set; }

	/// <summary>A tag de combate caiu: com o chao em perigo a camada fica -- quem a solta e o fim do perigo.</summary>
	private bool CombateSeguradoPeloPerigo => _perigoTocando && !TagCalaOPerigoDeTeste;

	/// <summary>So o robo `--diagsubzonas`: poe a tag de combate de pe por uns segundos, pra ver se a queda dela cala o perigo.</summary>
	public void TagCurtaDeTeste(double segundos) => _lutaAte = segundos;

	/// <summary>
	/// O CHAO ESTA EM PERIGO? Entao a camada de COMBATE fica de pe, com a faixa sorteada da mesma pasta da briga (`Trilha.Combate`), e o
	/// `AudioDirector` encadeia a seguinte quando ela acaba -- e o mesmo pedido que a tag de combate faz. Os dois donos do pedido se
	/// respeitam: o perigo que comeca no meio de uma briga nao troca a faixa, e a briga que comeca no perigo tambem nao (`AoGolpe`); a tag
	/// que cai no perigo nao cala nada (`_Process`), e o perigo que acaba no meio da briga deixa a tag soltar quando cair.
	///
	/// QUEM DECIDE O PERIGO E O CORE (`PlanetaEmPerigo.De`): o planeta do chao (a sub-zona conta como o planeta dela, a Sala do Tempo
	/// nao), explodindo ou morrendo pelo `S2C.Mortos`, ou com chefe de saga pelo `S2C.ChefesNosPlanetas`.
	///
	/// SEM CORPO NA TELA (a carga de uma zona) NAO DECIDE NADA: o quadro em que o corpo some nao pode calar a trilha e o seguinte sortear
	/// outra faixa.
	/// </summary>
	private void TickDaTrilhaDoPerigo()
	{
		if (GameClient.Instance is not { } cli || PosicaoLocal is not { } p) return;

		PerigoDoChao agora = PlanetaEmPerigo.De(cli.Zone, new Vec2(p.X, p.Y), cli.Mortos, cli.PlanetasComChefe);
		PerigoDoChaoDeTeste = agora;
		bool perigo = agora != PerigoDoChao.Nenhum && !PerigoMudoDeTeste;
		if (perigo == _perigoTocando) return;
		_perigoTocando = perigo;

		// A TAG DE COMBATE DE PE JA SEGURA A CAMADA: o perigo que comeca no meio da briga nao sorteia outra faixa, e o que acaba no meio
		// dela deixa a tag soltar quando cair.
		if (_lutaAte > 0) return;

		if (perigo)
			AudioDirector.Instance?.Musica(Trilha.Combate(), AudioDirector.Camada.Combate,
										   agora == PerigoDoChao.Explodindo ? "o planeta deste chao esta EXPLODINDO" : "ha CHEFE DE SAGA neste planeta");
		else
			AudioDirector.Instance?.PararCamada(AudioDirector.Camada.Combate, "o perigo do chao acabou");
	}
}
