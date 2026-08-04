using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// O CÉU DO MUNDO: o relógio que o servidor manda e a lua que ele anuncia.
///
/// ============================ O QUE MUDOU AQUI ============================
/// Até então o ciclo do dia era um contador LOCAL do cliente: `Iluminacao._Process` somava o
/// `delta` do quadro a partir de 0,42, que era o valor com que o campo nascia. O comentário de lá
/// já dizia "quem manda é o SERVIDOR", mas ninguém nunca mandou -- não havia relógio no servidor
/// nem pacote de hora. Na prática cada cliente tinha o seu dia, e quem entrasse dez minutos depois
/// jogava dez minutos atrasado pra sempre.
///
/// Isso passava enquanto o céu era enfeite. Não passa mais: a lua cheia é o gatilho do Oozaru, e
/// um gatilho que cada máquina calcula sozinha é um jogador virando macaco gigante numa lua que o
/// outro não está vendo.
/// ==========================================================================
///
/// O RELÓGIO É O RELÓGIO DE PAREDE, e isso é de propósito. <see cref="TempoDoMundo"/> sai do UTC,
/// não de um contador que anda com o tique -- então:
///   * não acumula erro (contador de `dt` desliza uns segundos por hora e ninguém percebe);
///   * atravessa reinício de servidor sem nada pra salvar (no DM o `mooncycle` ia pro `AreaSave`);
///   * e o ciclo é o mesmo pra qualquer instância, então "a cheia é de três em três horas" é uma
///     frase que o jogador pode aprender e usar.
///
/// CADA PLANETA TRADUZ ESSE INSTANTE NO PRÓPRIO DIA. Quem faz a conta é o <see cref="Ceu"/>, no
/// Core; aqui só se decide QUANDO é agora e QUEM precisa saber.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>De quantos em quantos segundos o relógio é reenviado pra corrigir deriva.</summary>
	private const double SegundosEntreSincronias = 15;

	/// <summary>
	/// A BANCADA: quantos segundos o mundo está adiantado. Zero em jogo de verdade.
	///
	/// Existe porque a lua cheia da Terra acontece uma vez a cada oito noites de 24 minutos --
	/// mais de TRÊS HORAS de espera. Um sistema que só dá pra ver depois de três horas não é
	/// testado, e o gatilho do Oozaru vai pendurar nele.
	/// </summary>
	private double _adiantoDoCeu;

	/// <summary>
	/// QUE HORAS SÃO NO UNIVERSO, em segundos. É o único número de tempo que existe -- a hora de
	/// cada planeta sai daqui pela ficha dele.
	/// </summary>
	public double TempoDoMundo =>
		DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 + _adiantoDoCeu;

	/// <summary>A ficha de céu de uma zona: rotação, dia/noite e lua. Ver <see cref="Ceu.RelogioDaZona"/>.</summary>
	public RelogioDoPlaneta RelogioDaZona(ZoneKey zona) => Ceu.RelogioDaZona(zona, _planetas);

	/// <summary>O céu que este jogador está vendo agora.</summary>
	public EstadoDoCeu CeuDe(ServerPlayer pl) => Ceu.De(RelogioDaZona(pl.Zone), TempoDoMundo);

	// =====================================================================
	// A BANCADA
	// =====================================================================
	/// <summary>
	/// Lê `--luateste [fase]` e `--horateste <hora>`. Chamado junto das outras flags, ANTES da
	/// guarda do `--server` -- o servidor também sobe dentro do cliente (`--host`), e ali o
	/// `_Ready` sai na linha seguinte.
	/// </summary>
	private void LerBancadaDoCeu(string[] args)
	{
		// `--horateste 0.85`: começa o mundo numa hora escolhida da Terra (0 = meia-noite).
		int hIdx = Array.IndexOf(args, "--horateste");
		if (hIdx >= 0 && hIdx + 1 < args.Length && double.TryParse(args[hIdx + 1],
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out double hv))
		{
			AjustarCeuDaTerra(hora: hv);
			GD.Print($"[server] BANCADA: o mundo comeca as {hv:0.00} do dia da Terra "
					 + $"({Ceu.NomeDaHora(hv)})");
		}

		// `--luateste [fase]`: adianta o mundo até a fase pedida da lua da TERRA, no meio da
		// noite. Sem argumento cai na 5, que é a cheia -- é ela que o Oozaru quer.
		int lIdx = Array.IndexOf(args, "--luateste");
		if (lIdx < 0) return;

		int fase = Ceu.Cheia;
		if (lIdx + 1 < args.Length && int.TryParse(args[lIdx + 1], out int fv) && fv is >= 1 and <= Ceu.Fases)
			fase = fv;

		AjustarCeuDaTerra(hora: 0.90, fase: fase);
		GD.Print($"[server] BANCADA: a Terra entra em {Ceu.NomeDaFase(fase)}, com a lua no alto");
	}

	/// <summary>
	/// EMPURRA O RELÓGIO DO MUNDO até que a TERRA esteja na hora (e, se pedido, na fase) desejada.
	///
	/// A Terra é a régua porque é onde todo mundo nasce. Os outros planetas se movem junto, cada
	/// um pro ponto que a defasagem e a rotação deles mandarem -- é justamente isso que se quer
	/// olhar quando se liga a flag: com a Terra em lua cheia, o que Vegeta está vendo?
	/// </summary>
	private void AjustarCeuDaTerra(double hora, int fase = 0)
	{
		RelogioDoPlaneta terra = RelogioDaZona(ZoneKey.Premade("Earth"));
		double agora = TempoDoMundo;
		double dia = terra.DiaLocal(agora);

		// o próximo instante em que a Terra marca essa hora
		double alvo = Math.Floor(dia) + hora;
		if (alvo <= dia) alvo += 1;

		if (fase > 0)
			// ANDA NOITE A NOITE até a fase bater. São no máximo oito passos, e cada passo é um
			// dia local -- somar "(fase desejada - fase atual)" daria o mesmo resultado só
			// enquanto a hora alvo estivesse à noite, e a flag também serve pra hora do dia.
			for (int n = 0; n < Ceu.Fases && Ceu.FaseEm(terra, alvo) != fase; n++) alvo += 1;

		_adiantoDoCeu += (alvo - dia) * terra.SegundosPorDia;
	}

	// =====================================================================
	// O FIO
	// =====================================================================
	private void MandarCeu(ServerPlayer pl)
	{
		var w = Protocol.Begin(Protocol.S2C.Ceu);
		w.Put(TempoDoMundo);
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	private double _ultimaSincronia;

	/// <summary>
	/// O TIQUE DO CÉU, a 1 Hz. Faz duas coisas: mantém os relógios juntos e anuncia a lua.
	///
	/// UM SEGUNDO BASTA porque nada aqui é de reflexo. A lua leva minutos pra atravessar o céu, e
	/// a diferença entre saber da lua cheia agora ou daqui a um segundo não existe pra ninguém.
	/// </summary>
	private void TickDoCeu()
	{
		double agora = TempoDoMundo;
		TickDoClima();
		TickDoRaio(agora);

		if (agora - _ultimaSincronia >= SegundosEntreSincronias)
		{
			_ultimaSincronia = agora;
			foreach (ServerPlayer pl in _players.Values) MandarCeu(pl);
		}

		foreach (ServerPlayer pl in _players.Values)
		{
			if (pl.Peer == null) continue;   // corpo sem dono não recebe nada
			OlharProCeu(pl, Ceu.De(RelogioDaZona(pl.Zone), agora));
		}
	}

	/// <summary>
	/// O QUE ESTE JOGADOR VÊ AO OLHAR PRA CIMA -- e é daqui que sai o gancho do Oozaru.
	///
	/// Só fala quando algo MUDA: a lua nasceu, a lua se pôs, a fase virou. O DM faz o mesmo, e
	/// pelo mesmo motivo -- as três falas dele ("the full moon is rising", "is out", "is setting")
	/// saem do `CheckTime`, que só roda quando o `daylightcycle` da área muda (`Weather.dm:195`).
	/// </summary>
	private void OlharProCeu(ServerPlayer pl, EstadoDoCeu ceu)
	{
		bool noCeu = ceu.LuaNoCeu;
		bool mudou = noCeu != pl.LuaEstavaNoCeu || (noCeu && ceu.Fase != pl.LuaVista);

		if (!mudou) { pl.LuaEstavaNoCeu = noCeu; pl.LuaVista = noCeu ? ceu.Fase : 0; return; }

		bool nasceu = noCeu && !pl.LuaEstavaNoCeu;
		pl.LuaEstavaNoCeu = noCeu;
		pl.LuaVista = noCeu ? ceu.Fase : 0;

		if (!noCeu)
		{
			if (ceu.Cheia) Avisar(pl, "a lua cheia se poe.");
			return;
		}

		if (!nasceu) return;   // a fase virou com a lua já no céu: acontece, mas não é uma cena

		if (!ceu.Cheia)
		{
			// LUA COMUM SÓ APARECE NO CHAT SE VALER A PENA. Anunciar as oito fases toda noite
			// gastaria em ruído a atenção que a quinta precisa ter.
			if (ceu.Fase is (int)FaseDaLua.CrescenteGibosa or (int)FaseDaLua.MinguanteGibosa)
				Avisar(pl, $"a lua nasce quase inteira -- {Ceu.NomeDaFase(ceu.Fase)}.");
			return;
		}

		LuaCheiaNasceu(pl, ceu);
	}

	/// <summary>
	/// ============================ O GANCHO DO OOZARU ============================
	/// A LUA CHEIA SUBIU no céu deste jogador. É este o ponto em que o Oozaru se pendura, e o
	/// motivo pelo qual todo o resto deste arquivo existe.
	///
	/// O QUE O DM FAZ AQUI (`Modules/Turfs/Weather.dm:195-207`), na ordem:
	///     1. avisa em vermelho que a lua cheia está no céu;
	///     2. confere `Osetting` (o jogador decidiu olhar), `!Apeshit`, `Tail` e `Race=="Saiyan"`;
	///     3. confere que a área NÃO é `Inside` -- de dentro de casa não se olha pra lua;
	///     4. `GoldenApeshit()` se já for SSJ (60% de chance), senão `Apeshit()`.
	///
	/// O QUE JÁ EXISTE NO PORT: o passo 1 (aqui), o rabo do passo 2 (<see cref="TemRaboInteiro"/>)
	/// e o passo 3 -- interior é zona sem céu, então a lua nunca chega a nascer lá dentro
	/// (`Ceu.RelogioDaZona`). O que falta é a forma em si: `Osetting`, `Apeshit` e o buff.
	///
	/// QUANDO O OOZARU FOR PORTADO, ele entra NESTA função e em mais lugar nenhum -- a lua
	/// artificial (`Artificial_Moon`) e as ondas de Blutz da magia chamam o mesmo `Apeshit()` do
	/// DM, então os três gatilhos devem convergir pra uma chamada só, e não pra três cópias da
	/// regra.
	/// ============================================================================
	/// </summary>
	private void LuaCheiaNasceu(ServerPlayer pl, EstadoDoCeu ceu)
	{
		_ = ceu;
		Avisar(pl, "a lua cheia se ergue no horizonte.");

		if (!TemRaboInteiro(pl)) return;

		// QUEM TEM RABO SENTE ANTES DE VER. No anime a transformação começa pelo corpo, não pela
		// decisão -- e é esta linha que vai virar a chamada do Oozaru.
		Avisar(pl, "seu rabo se enrija sozinho. Alguma coisa em voce quer olhar pra cima.");
	}

	/// <summary>
	/// TEM RABO, E INTEIRO? É a condição que o DM checa duas vezes: pra entrar no Oozaru
	/// (`Oozaru.dm:39`, `if(!Apeshit&&Tail&&!KO)`) e pra CONTINUAR nele (`Oozaru.dm:153`, o
	/// `Loop()` desfaz a forma no instante em que o rabo some).
	///
	/// Rabo decepado não conta. É por isso que cortar o rabo do Saiyajin é a jogada clássica --
	/// e, no port, o membro "Rabo" já existe no corpo e já pode ser arrancado em combate.
	/// </summary>
	private static bool TemRaboInteiro(ServerPlayer pl) =>
		pl.Combate?.Corpo.Achar("Rabo") is { Decepado: false };
}
