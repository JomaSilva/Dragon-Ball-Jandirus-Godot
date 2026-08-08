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

	/// <summary>
	/// ============================ PULA PRA A PROXIMA NOITE DE LUA CHEIA ============================
	/// Existe porque o Oozaru depende de uma condicao que o testador NAO controla: lua cheia, no
	/// ceu, a noite. Esperar isso acontecer sozinho e esperar dias, e um sistema que so da pra
	/// testar por acaso nao e testado.
	///
	/// NAO INVENTA CEU: o relogio do universo ja tem o deslocamento `_adiantoDoCeu`, e este verb so
	/// PROCURA o proximo instante em que a ficha deste planeta diz "cheia, no ceu, de noite" e
	/// adianta ate la. O ceu continua sendo funcao pura do tempo, entao o cliente chega na mesma
	/// conclusao sozinho -- nenhum pacote novo.
	///
	/// Passo grosso de um minuto e depois recua ate a BORDA da janela: cair no meio dela largaria o
	/// testador com pouca noite pela frente.
	///
	/// O CLIMA NAO E MEXIDO AQUI de proposito -- quem manda no tempo e o `admin_clima`. Juntar as
	/// duas coisas num verb so esconderia qual das duas resolveu.
	/// ================================================================================================
	/// </summary>
	private void AdminLuaCheia(ServerPlayer pl)
	{
		RelogioDoPlaneta relogio = RelogioDaZona(pl.Zone);
		double agora = TempoDoMundo;
		const double Teto = 60 * 60 * 24 * 45;   // 45 dias: planeta sem lua nao adianta procurar mais
		const double Passo = 60;

		double achou = -1;
		for (double d = 0; d < Teto; d += Passo)
		{
			EstadoDoCeu c = Ceu.De(relogio, agora + d);
			if (!c.Cheia || !c.LuaNoCeu || !c.Noite) continue;
			achou = d;
			for (double t = d; t > 0; t -= 5)
			{
				EstadoDoCeu antes = Ceu.De(relogio, agora + t - 5);
				if (!antes.Cheia || !antes.LuaNoCeu || !antes.Noite) break;
				achou = t - 5;
			}
			break;
		}

		if (achou < 0)
		{
			Avisar(pl, "[admin] nao achei lua cheia noturna nesta zona em 45 dias -- este planeta tem lua?");
			return;
		}

		_adiantoDoCeu += achou;
		EstadoDoCeu fim = CeuDe(pl);
		Avisar(pl, $"[admin] ceu adiantado {achou / 3600:0.#} h: lua {(fim.Cheia ? "CHEIA" : "nao cheia")}, "
				 + $"altura {fim.Altura:0.00}, {(fim.Noite ? "noite" : "dia")}. "
				 + "Encoberta? use o clima pra limpar.");
	}

	/// <summary>
	/// ============================ PULA PRA O PRÓXIMO MEIO-DIA ============================
	/// Irmão do <see cref="AdminLuaCheia"/>, e existe pelo mesmo motivo escrito lá -- só que a
	/// condição que o testador não controla aqui é a mais banal de todas: a LUZ.
	///
	/// UM DIA INTEIRO DURA 24 MINUTOS (`Ceu.SegundosPorDia`), então qualquer bancada que julgue COR
	/// tira metade das fotos no escuro por puro azar do relógio de parede. Isso não é hipótese: a
	/// bancada `--diagolhada` rodou duas vezes com o mesmo código e a mesma conta, a primeira às
	/// 07:11 de tarde-no-jogo e a segunda de noite -- na segunda o pixel mais claro do recorte
	/// inteiro era `#1e274d` e o cabelo dourado do C-Type media quase preto. As duas rodadas
	/// salvaram as fotos com `ok` no log. Uma bancada de cor que depende da hora em que alguém a
	/// roda não mede cor nenhuma.
	///
	/// ============================ POR QUE CONTA, E NÃO VARREDURA COMO O DA LUA ============================
	/// O da lua VARRE (passo de um minuto, 45 dias) porque a condição dele é composta -- cheia, no
	/// céu, de noite -- e não tem inversa. "Meio-dia" tem: a hora local é uma função afim do tempo
	/// (<see cref="RelogioDoPlaneta.DiaLocal"/>), então dá pra RESOLVER em vez de procurar. Forçar as
	/// duas no mesmo ajudante deixaria esta aqui varrendo um dia inteiro atrás de um número que ela
	/// já sabe calcular, e deixaria a outra sem o recuo até a borda da janela, que é o que impede o
	/// testador de cair no último minuto da lua cheia.
	///
	/// O CLIMA NÃO É MEXIDO AQUI, pelo mesmo motivo do irmão: quem manda no tempo é o `admin_clima`.
	/// Meio-dia debaixo de tempestade continua escuro, e é o clima que tem que resolver isso -- juntar
	/// as duas coisas esconderia qual das duas resolveu.
	/// ====================================================================================================
	/// </summary>
	private void AdminMeioDia(ServerPlayer pl)
	{
		RelogioDoPlaneta relogio = RelogioDaZona(pl.Zone);
		double dia = relogio.DiaLocal(TempoDoMundo);

		// A PARTE INTEIRA É QUAL DIA, A FRAÇÃO É A HORA. O `+1` cobre o caso de já ter passado do
		// meio-dia de hoje: sem ele o adianto sairia negativo e o mundo ANDARIA PRA TRÁS -- e o
		// `_adiantoDoCeu` é acumulado, então isso não se desfaria sozinho.
		double alvo = Math.Floor(dia) + Ceu.MeioDia;
		if (alvo <= dia) alvo += 1;

		_adiantoDoCeu += (alvo - dia) * relogio.SegundosPorDia;

		// O QUE O CÉU FICOU, e não o que foi pedido. Num mundo de noite eterna (`TemDia` falso) o
		// `HoraVisivel` prende a hora e este verb não clareia nada -- e quem leu "pulei pro meio-dia"
		// iria julgar a cor de um cabelo no escuro achando que estava de dia.
		EstadoDoCeu fim = CeuDe(pl);
		Avisar(pl, $"[admin] ceu adiantado pro meio-dia: hora {fim.Hora:0.00}, "
				 + $"{(fim.Noite ? "NOITE (este mundo nao tem dia)" : "dia")}. "
				 + "Encoberto? use o clima pra limpar.");
	}

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
		// decisão.
		Avisar(pl, "seu rabo se enrija sozinho. Alguma coisa em voce quer olhar pra cima.");

		// ============================ A LUA NAO TRANSFORMA MAIS SOZINHA ============================
		// Aqui havia um `Apeshit(pl)` -- a lua cheia nascia e o corpo virava, sem o jogador decidir.
		//
		// O dono pediu outra coisa: um botao vermelho embaixo da lua, escrito "olhar pra lua", que
		// so aparece na lua cheia. Olhar e uma ESCOLHA, e e ela que dispara. Deixar a linha aqui
		// faria dois gatilhos pra a mesma coisa -- e o automatico venceria sempre, tornando o botao
		// decorativo.
		//
		// A frase acima ("seu rabo se enrija sozinho...") continua, e agora ela ganha funcao: e o
		// aviso que manda o jogador olhar pro canto da tela.
		//
		// `Apeshit` segue sendo o funil unico: a lua artificial e as ondas de Blutz, quando vierem,
		// entram por ele -- so que agora quem o chama pra a lua natural e o botao.
		// ================================================================================
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
