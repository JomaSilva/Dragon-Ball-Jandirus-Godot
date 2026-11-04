using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ A GOSMA DO MAJIN (dono, 2026-11-01, o pedido 219) ============================
/// A regra e as frases moram no Core (`Races.GosmaDoMajin`, com o porque de cada numero); aqui mora o CORPO dela e os passos.
///
/// A GOSMA E UM CORPO DO MUNDO, sem dono de rede e sem cerebro (como a imagem do interior do Majin): e assim que ela chega a tela de todo
/// mundo pelo snapshot de sempre, apanha de golpe pelas portas de sempre e some quando sai -- e a tela a desenha como gosma pelo valor
/// `Ocupacao.Gosma` (`Client/GosmaDesenhada.cs`). Quem a anda e o <see cref="TickDasGosmas"/>, a 30 Hz, por cinco passos:
///   * ESCONDIDA no lugar onde o Majin a largou: `Oculto` (ninguem a ve) e intocavel (o `Health = 1.#INF` do `GooHide`); olha em volta aos
///     4 s e depois de 8 em 8 s, ate 15 tiles, atras de um JOGADOR nocauteado ou mais fraco que ela;
///   * CACANDO: a vista e tocavel (qualquer golpe a desfaz), cinco tiles por segundo, ate 30 tiles;
///   * GRUDADA: em cima da presa, nove conferidas de um segundo -- quem chega ao poder dela de pe se solta;
///   * LEVANDO (o dono, 2026-11-01): a presa ENGOLIDA (o corpo some pra todo mundo, nao anda nem age, nao apanha) e a gosma voa rapido ate o
///     Majin; o `LevadoPor` do snapshot e a gosma, e e por ele que a tela do engolido o desenha nela -- a camera vai junto. Ao ENCOSTAR no
///     Majin, a absorcao de sempre (`SelarNoMajin`: o interior e a imagem);
///   * VOLTANDO pro lugar, e la se esconde de novo.
/// O MAJIN QUE SAI DA ZONA (ou do jogo) DESFAZ as gosmas dele, e quem ia dentro cai onde a gosma estava, a vista.
/// ==================================================================================================================
/// </summary>
public partial class GameServer
{
	private enum PassoDaGosma : byte { Escondida, Cacando, Grudada, Levando, Voltando }

	/// <summary>Uma gosma no mapa.</summary>
	private sealed class GosmaPosta
	{
		/// <summary>O id do corpo dela.</summary>
		public int Corpo;

		/// <summary>O Majin que a largou.</summary>
		public int Dono;

		/// <summary>Onde ela espreita.</summary>
		public Vec2 Lugar;

		/// <summary>O poder dela -- 70% do dono ao larga-la (`Races.GosmaDoMajin.FracaoDoPoder`).</summary>
		public double Poder;

		/// <summary>O dono paga os 27% enquanto ela existe? O chefe da saga nao: o poder dele e o da saga.</summary>
		public bool CobraDoDono;

		public PassoDaGosma Passo = PassoDaGosma.Escondida;

		/// <summary>Quem ela persegue, segura ou leva (0 = ninguem).</summary>
		public int Presa;

		/// <summary>Segundos ate a proxima olhada (escondida) ou a proxima conferida (grudada).</summary>
		public double Relogio;

		/// <summary>Grudada: quantas conferidas a presa ja perdeu.</summary>
		public int Conferidas;

		/// <summary>O ultimo que ela levou -- `goo_last_absorbed_key`: nao vai atras do mesmo duas vezes seguidas.</summary>
		public int UltimoLevado;
	}

	/// <summary>As gosmas no mapa, pelo id do corpo de cada uma.</summary>
	private readonly Dictionary<int, GosmaPosta> _gosmas = new();

	/// <summary>A gosma de cada Majin (uma por Majin -- `MajinGooExists`).</summary>
	private readonly Dictionary<int, int> _gosmaDoDono = new();

	/// <summary>Quem vai dentro de uma gosma agora -> o corpo dela.</summary>
	private readonly Dictionary<int, int> _engolidoPor = new();

	/// <summary>A vida da gosma a vista: qualquer golpe a tira daqui, e o tique a desfaz.</summary>
	private const double VidaDaGosma = 100;

	private static float Tile => ZoneCollision.TileSize;

	/// <summary>A tinta da gosma de quem nao escolheu a cor da pele: o rosa do Boo (o mesmo da foto do Majin).</summary>
	private static readonly Jandirus.Core.Appearance.Rgb RosaDaGosmaSemTinta = new(0xff, 0x9a, 0xb5);

	/// <summary>
	/// A VELOCIDADE DO VOO COM A PRESA DENTRO, em tiles/s, so nas fotos (`--diaggosma`); 0 = a do jogo (<see cref="GosmaDoMajin.TilesPorSegundoVoando"/>).
	/// A 18 tiles/s o voo de 9 tiles dura meio segundo: medido na r2, com o mundo ainda nascendo os habitantes (quadros de 130 ms), o aviso de
	/// engolido entrava e saia entre duas olhadas do robo. O caminho e o mesmo; so a foto voa devagar. Zero em jogo, sempre.
	/// </summary>
	internal static double VooLevandoDeTeste;

	/// <summary>Este corpo e uma gosma?</summary>
	private bool EhGosma(ServerPlayer pl) => _gosmas.ContainsKey(pl.Id);

	/// <summary>Este corpo vai dentro de uma gosma agora?</summary>
	private bool NaGosma(int id) => _engolidoPor.ContainsKey(id);

	/// <summary>Este Majin tem uma gosma no mapa?</summary>
	private bool TemGosma(int dono) => _gosmaDoDono.ContainsKey(dono);

	/// <summary>A gosma escondida: ninguem a ve (`EstaOculto`) e ninguem a toca (o gancho `EmCinematica` do corpo dela).</summary>
	private bool GosmaEscondida(int id) => _gosmas.TryGetValue(id, out GosmaPosta? g) && g.Passo == PassoDaGosma.Escondida;

	/// <summary>A gosma que leva este corpo (0 = nenhuma) -- o `LevadoPor` do snapshot.</summary>
	private int GosmaQueLeva(int id) => _engolidoPor.TryGetValue(id, out int g) ? g : 0;

	// =====================================================================
	// O VERBO
	// =====================================================================
	/// <summary>
	/// O `hab:gosma`: sem gosma no mapa, larga uma onde esta; com, a recolhe (`GooTrap()`: `if(DeleteMajinGoo())` primeiro).
	/// </summary>
	private void UsarAGosma(ServerPlayer pl)
	{
		if (TemGosma(pl.Id))
		{
			DesfazerAsGosmasDe(pl.Id, GosmaDoMajin.AoRecolher);
			return;
		}
		if (!AbsorcaoMajin.EhMajin(pl.Race, pl.Ficha.ParentRace)) { Avisar(pl, GosmaDoMajin.RecusaNaoMajin); return; }
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, GosmaDoMajin.RecusaCaido); return; }   // `CanMajinGoo`: `if(KO) return`
		if (!LugarDeGosma(pl)) { Avisar(pl, GosmaDoMajin.RecusaAqui); return; }
		if (PorAGosma(pl, cobra: true) != null) Avisar(pl, GosmaDoMajin.AoPor);
	}

	/// <summary>Onde nao se larga gosma: dentro de alguem, na mente e no espaco -- lugares de onde ela nao teria como voltar a ninguem.</summary>
	private static bool LugarDeGosma(ServerPlayer pl) => !DentroDeUmMajin(pl) && !NaMente(pl) && !Espaco.EhEspaco(pl.Zone);

	/// <summary>
	/// LARGA A GOSMA DE <paramref name="dono"/> onde ele esta, escondida, com 70% do poder que ele usa agora. <paramref name="cobra"/>: ele
	/// fica com 73% enquanto ela existir. Devolve o corpo dela (nulo = nao deu).
	/// </summary>
	private ServerPlayer? PorAGosma(ServerPlayer dono, bool cobra)
	{
		if (TemGosma(dono.Id)) return null;
		double poder = Math.Max(1, dono.Ficha.expressedBP * GosmaDoMajin.FracaoDoPoder);
		string nome = $"gosma de {NomeVisivel(dono)}";
		var f = new Fighter
		{
			Name = nome, Race = dono.Race, BP = poder,
			physoff = 1, physdef = 1, technique = 1, kioff = 1, kidef = 1,
			kiskill = 1, speed = 5, magiskill = 1, Idade = 1,
			maxstamina = 100, stamina = 100,
		};
		f.Statify();
		f.PowerLevel();
		f.Ki = f.MaxKi;
		f.Class = dono.Class;

		var g = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,
			Name = nome,
			Race = dono.Race,
			Class = dono.Class,
			Genero = dono.Genero,
			Idade = 1,
			Zone = dono.Zone,
			Pos = dono.Pos,
			Planeta = dono.Planeta,
			LastInputMs = NowMs(),
			Ficha = f,
			Livro = new Jandirus.Core.Skills.SkillBook(),
			Cerebro = null,   // quem a anda e o `TickDasGosmas`
			// A COR DO DONO: a tela pinta a gosma com a pele e a tinta do corpo que ela veste (`CharacterVisual.CorpoParaOsPedacos`), como os
			// pedacos do Majin desfeito -- `if(base_icon_color) g.icon += base_icon_color` (`Goo Trap.dm:47`).
			Visual = VisualVisivel(dono).Copiar(),
		};
		g.Visual.Roupa.Clear();
		// O MAJIN SEM TINTA ESCOLHIDA (o Boo da saga, cuja folha ja nasce rosa; o jogador que nao tingiu a pele): a `Majin1` da gosma e
		// clara e pede a tinta -- sem ela a gosma saia CINZA (a foto 9 da r3, a do Super Boo). Vai o rosa do Boo.
		g.Visual.CorPele ??= RosaDaGosmaSemTinta;

		var posta = new GosmaPosta
		{
			Corpo = g.Id, Dono = dono.Id, Lugar = dono.Pos, Poder = poder, CobraDoDono = cobra,
			Relogio = GosmaDoMajin.SegundosAtePrimeiraOlhada,
		};
		// O REGISTRO ANTES DO MUNDO: o primeiro snapshot que a levar ja a leva escondida.
		_gosmas[g.Id] = posta;
		_gosmaDoDono[dono.Id] = g.Id;

		PorNoMundo(g);
		g.Ficha.HP = VidaDaGosma;
		g.Combate.Letal = false;
		// ESCONDIDA, NINGUEM A TOCA: o escudo das cenas (`CombatState.Blindado`), pela pergunta e nao por um bit -- ela aparece e o escudo cai
		// no mesmo tique, sem ninguem lembrar de apaga-lo.
		g.Combate.EmCinematica = () => GosmaEscondida(g.Id);
		// O DONO NAO DESFAZ A PROPRIA GOSMA: ela e um pedaco dele. Medido na foto do Super Boo (r2): grudada na presa, ela esta onde a presa
		// esta, e o Kamehameha que ele dava na presa encostava antes nela (`FEIXE CORTADO`) -- a gosma morria no primeiro tiro do proprio dono.
		// O soco, o tiro, o feixe e a explosao DELE passam por ela (`IntocavelPara`, a pergunta de quem sabe quem bate); os de todo o resto a
		// desfazem, como no DU. A barra do torneio que o corpo ja trazia continua valendo.
		Func<CombatState, bool>? barraDeAntes = g.Combate.BarraQuemBate;
		g.Combate.BarraQuemBate = atacante =>
			(_players.TryGetValue(posta.Dono, out ServerPlayer? d) && ReferenceEquals(atacante, d.Combate)) || barraDeAntes?.Invoke(atacante) == true;
		TrocarAparencias(g);

		if (cobra)
		{
			dono.Ficha.gosmaNoMapaMod = GosmaDoMajin.PoderDoMajinComAGosmaNoMapa;
			dono.Ficha.PowerLevel();
			MandarFicha(dono);
		}
		GD.Print($"[server] GOSMA: {dono.Name} largou a gosma #{g.Id} em {dono.Zone.Name} ({dono.Pos.X:0},{dono.Pos.Y:0}), poder {poder:N0}");
		return g;
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>Os passos de todas as gosmas, a 30 Hz. Ver o cabecalho.</summary>
	private void TickDasGosmas()
	{
		if (_gosmas.Count == 0) return;
		foreach (GosmaPosta g in _gosmas.Values.ToArray()) PassoDaGosmaNoTique(g, Protocol.TickSeconds);
	}

	private void PassoDaGosmaNoTique(GosmaPosta g, double dt)
	{
		if (!_players.TryGetValue(g.Corpo, out ServerPlayer? corpo))
		{
			DesfazerAGosma(g, _players.GetValueOrDefault(g.Dono), "");
			return;
		}
		// O DONO SAIU DA ZONA (ou do mundo): a gosma se desfaz -- *"sair da zona desfaz as gosmas"*. A porta de verdade e a troca de zona
		// (`MoveToZone`); esta linha e a rede de quem saiu por outra (o chefe removido, o corpo que sumiu).
		if (!_players.TryGetValue(g.Dono, out ServerPlayer? dono) || dono.Zone.Hash != corpo.Zone.Hash)
		{
			DesfazerAGosma(g, dono, GosmaDoMajin.AoDonoQuandoSai);
			return;
		}
		// A VISTA, QUALQUER GOLPE A DESFAZ (`GooUnhide`: `Health = 1`).
		if (g.Passo != PassoDaGosma.Escondida && (corpo.Ficha.HP < VidaDaGosma - 0.01 || corpo.Ficha.KO || corpo.Ficha.dead))
		{
			DesfazerAGosma(g, dono, GosmaDoMajin.AoDonoQuandoDestroem);
			return;
		}

		ServerPlayer? presa = g.Presa != 0 ? _players.GetValueOrDefault(g.Presa) : null;
		switch (g.Passo)
		{
			case PassoDaGosma.Escondida:
				corpo.Pos = g.Lugar;
				corpo.Moving = false;
				corpo.Ficha.HP = VidaDaGosma;
				g.Relogio -= dt;
				if (g.Relogio > 0) break;
				g.Relogio = GosmaDoMajin.SegundosEntreOlhadas;
				if (PresaDaGosma(g, corpo, dono) is { } achou)
				{
					g.Passo = PassoDaGosma.Cacando;
					g.Presa = achou.Id;
					GD.Print($"[server] GOSMA #{g.Corpo} ({dono.Name}) viu {achou.Name} e aparece");
				}
				break;

			case PassoDaGosma.Cacando:
				if (presa == null || !SegueSendoPresa(corpo, presa)
					|| (presa.Pos - corpo.Pos).Length > GosmaDoMajin.TilesDePerseguicao * Tile)
				{
					GosmaVolta(g);
					break;
				}
				if ((presa.Pos - corpo.Pos).Length <= GosmaDoMajin.TilesDoBote * Tile)
				{
					GosmaGruda(g, corpo, presa, dono);
					break;
				}
				AndarAGosma(corpo, presa.Pos, GosmaDoMajin.TilesPorSegundoCacando * Tile * dt);
				break;

			case PassoDaGosma.Grudada:
				if (presa == null || !SegueSendoPresa(corpo, presa)) { GosmaVolta(g); break; }
				corpo.Pos = presa.Pos;
				corpo.Moving = false;
				g.Relogio -= dt;
				if (g.Relogio > 0) break;
				g.Relogio = GosmaDoMajin.SegundosPorConferida;
				if (GosmaDoMajin.Escapa(presa.Ficha.KO, presa.Ficha.expressedBP, g.Poder))
				{
					Avisar(presa, string.Format(GosmaDoMajin.AoSoltar, NomeVisivel(dono)));
					GD.Print($"[server] GOSMA #{g.Corpo}: {presa.Name} se soltou ({presa.Ficha.expressedBP:N0} contra {g.Poder:N0})");
					GosmaVolta(g);
					break;
				}
				if (++g.Conferidas >= GosmaDoMajin.Conferidas) GosmaEngole(g, presa, dono);
				break;

			case PassoDaGosma.Levando:
				if (presa == null || presa.Zone.Hash != corpo.Zone.Hash)
				{
					if (presa != null) _engolidoPor.Remove(presa.Id);
					GosmaVolta(g);
					break;
				}
				if ((dono.Pos - corpo.Pos).Length <= GosmaDoMajin.TilesDoToque * Tile)
				{
					GosmaEntrega(g, corpo, presa, dono);
					break;
				}
				AndarAGosma(corpo, dono.Pos, (VooLevandoDeTeste > 0 ? VooLevandoDeTeste : GosmaDoMajin.TilesPorSegundoVoando) * Tile * dt);
				// VAI DENTRO: o corpo engolido esta onde a gosma esta, e nada o toca (o escudo da carencia, renovado a cada tique).
				presa.Pos = corpo.Pos;
				presa.Moving = false;
				presa.Combate.Carencia = Math.Max(presa.Combate.Carencia, 0.25);
				break;

			case PassoDaGosma.Voltando:
				if ((g.Lugar - corpo.Pos).Length <= GosmaDoMajin.TilesDoBote * Tile)
				{
					corpo.Pos = g.Lugar;
					corpo.Moving = false;
					g.Passo = PassoDaGosma.Escondida;
					g.Relogio = GosmaDoMajin.SegundosEntreOlhadas;
					corpo.Ficha.HP = VidaDaGosma;
					break;
				}
				AndarAGosma(corpo, g.Lugar, GosmaDoMajin.TilesPorSegundoVoando * Tile * dt);
				break;
		}
	}

	/// <summary>
	/// QUEM ELA PEGA (`IsViableGooTarget` + `CanGooAbsorb`): um JOGADOR (`m.client`) que nao e o dono, vivo, a ate 15 tiles, que nao foi o
	/// ultimo que ela levou, que nao esta dentro de ninguem nem na mente nem em outra gosma, que nao e intocavel pro dono (torneio, cena, zona
	/// segura), nem da mesma conexao dele, nem preso da Sala do Tempo -- e nocauteado ou mais fraco que ela. O mais perto.
	/// </summary>
	private ServerPlayer? PresaDaGosma(GosmaPosta g, ServerPlayer corpo, ServerPlayer dono)
	{
		ServerPlayer? melhor = null;
		float perto = (float)(GosmaDoMajin.TilesDeFaro * Tile);
		perto *= perto;
		foreach (ServerPlayer o in ZoneList(corpo.Zone.Hash))
		{
			if (o.Id == g.Dono || o.Id == g.UltimoLevado || !EhJogador(o) || o.Ficha.dead) continue;
			float d2 = (o.Pos - corpo.Pos).LengthSquared;
			if (d2 > perto) continue;
			if (!PodeSerPegaPelaGosma(o, dono)) continue;
			if (!GosmaDoMajin.Presa(o.Ficha.KO, o.Ficha.expressedBP, g.Poder)) continue;
			perto = d2;
			melhor = o;
		}
		return melhor;
	}

	private bool PodeSerPegaPelaGosma(ServerPlayer o, ServerPlayer dono) =>
		!DentroDeUmMajin(o) && !NaMente(o) && !NaGosma(o.Id) && !EhGosma(o) && !CorpoDerivado(o)
		&& !o.Combate.IntocavelPara(dono.Combate) && !UsoNoMesmoIp(dono, o) && PorQueNaoLevaOPreso(o).Length == 0
		&& !GosmaJaVaiAtras(o.Id);

	/// <summary>Outra gosma ja persegue, segura ou leva este corpo? Uma presa e de uma gosma so.</summary>
	private bool GosmaJaVaiAtras(int id)
	{
		foreach (GosmaPosta g in _gosmas.Values)
			if (g.Presa == id && g.Passo is PassoDaGosma.Cacando or PassoDaGosma.Grudada or PassoDaGosma.Levando) return true;
		return false;
	}

	/// <summary>A presa ainda serve (na perseguicao e grudada): mesma zona, viva, fora de todo interior e de outra gosma.</summary>
	private static bool SegueSendoPresa(ServerPlayer corpo, ServerPlayer presa) =>
		presa.Zone.Hash == corpo.Zone.Hash && !presa.Ficha.dead && !DentroDeUmMajin(presa) && !NaMente(presa);

	/// <summary>Anda a gosma em linha reta ate <paramref name="ate"/>, no maximo <paramref name="passo"/> px. Ela passa por cima de tudo.</summary>
	private static void AndarAGosma(ServerPlayer corpo, Vec2 ate, double passo)
	{
		Vec2 d = ate - corpo.Pos;
		float l = d.Length;
		if (l < 1e-3f) { corpo.Moving = false; return; }
		corpo.Facing = MoveRules.FacingFrom(d, corpo.Facing);
		corpo.Pos = l <= passo ? ate : corpo.Pos + d * (float)(passo / l);
		corpo.Moving = true;
	}

	private void GosmaGruda(GosmaPosta g, ServerPlayer corpo, ServerPlayer presa, ServerPlayer dono)
	{
		g.Passo = PassoDaGosma.Grudada;
		g.Conferidas = 0;
		g.Relogio = GosmaDoMajin.SegundosPorConferida;
		corpo.Pos = presa.Pos;
		Avisar(presa, string.Format(GosmaDoMajin.AoGrudar, NomeVisivel(dono)));
		GD.Print($"[server] GOSMA #{g.Corpo}: grudou em {presa.Name} ({presa.Ficha.expressedBP:N0} contra {g.Poder:N0})");
	}

	/// <summary>
	/// PERDEU OS NOVE SEGUNDOS: a gosma o ENGOLE -- o corpo some (`EstaOculto`), para (`PodeMexerOCorpo`), nao age (`RecusaOComandoDeCorpo`),
	/// sai da grade de corpos, e a tela dele passa a desenha-lo na gosma (`LevadoPor`) -- e ela voa pro dono.
	/// </summary>
	private void GosmaEngole(GosmaPosta g, ServerPlayer presa, ServerPlayer dono)
	{
		g.Passo = PassoDaGosma.Levando;
		_engolidoPor[presa.Id] = g.Corpo;
		presa.Moving = false;
		Avisar(presa, string.Format(GosmaDoMajin.AoEngolir, NomeVisivel(dono)));
		Avisar(dono, string.Format(GosmaDoMajin.AoDonoQuandoEngole, NomeVisivel(presa)));
		MandarFicha(presa);
		GD.Print($"[server] GOSMA #{g.Corpo}: engoliu {presa.Name} e voa pra {dono.Name}");
	}

	/// <summary>
	/// ENCOSTOU NO MAJIN: a absorcao de sempre (`SelarNoMajin`), com o engolido posto onde o Majin esta -- e ali que quem olha ve a
	/// absorcao. Majin caido ou dentro de alguem nao absorve: o engolido cai ali, livre.
	/// </summary>
	private void GosmaEntrega(GosmaPosta g, ServerPlayer corpo, ServerPlayer presa, ServerPlayer dono)
	{
		_engolidoPor.Remove(presa.Id);
		g.UltimoLevado = presa.Id;
		presa.Combate.Carencia = 0;
		if (dono.Ficha.KO || dono.Ficha.dead || DentroDeUmMajin(dono) || NaMente(dono))
		{
			Transportar(presa, presa.Zone, MapaDaZonaOuCatalogo(presa.Zone)?.PontoLivrePerto(corpo.Pos) ?? corpo.Pos);
			Avisar(presa, GosmaDoMajin.AoSoltoNoCaminho);
		}
		else
		{
			presa.Pos = dono.Pos;
			GD.Print($"[server] GOSMA #{g.Corpo}: encostou em {dono.Name} -- ele absorve {presa.Name}");
			SelarNoMajin(dono, presa);
		}
		GosmaVolta(g);
	}

	private static void GosmaVolta(GosmaPosta g)
	{
		g.Passo = PassoDaGosma.Voltando;
		g.Presa = 0;
		g.Conferidas = 0;
	}

	// =====================================================================
	// O FIM DELA
	// =====================================================================
	/// <summary>Desfaz todas as gosmas deste Majin (recolher, sair da zona, sair do jogo, virar o Kid).</summary>
	private void DesfazerAsGosmasDe(int dono, string avisoAoDono)
	{
		if (!_gosmaDoDono.TryGetValue(dono, out int corpo) || !_gosmas.TryGetValue(corpo, out GosmaPosta? g)) return;
		DesfazerAGosma(g, _players.GetValueOrDefault(dono), avisoAoDono);
	}

	/// <summary>
	/// A GOSMA SAI DO MAPA: quem ia dentro cai onde ela estava, a vista e livre; o corpo dela sai do mundo; o dono volta ao poder inteiro.
	/// O registro sai PRIMEIRO -- o resto ja pergunta por um mundo sem ela.
	/// </summary>
	private void DesfazerAGosma(GosmaPosta g, ServerPlayer? dono, string avisoAoDono)
	{
		_gosmas.Remove(g.Corpo);
		if (_gosmaDoDono.TryGetValue(g.Dono, out int dela) && dela == g.Corpo) _gosmaDoDono.Remove(g.Dono);

		ServerPlayer? corpo = _players.GetValueOrDefault(g.Corpo);
		Vec2 onde = corpo?.Pos ?? g.Lugar;
		foreach ((int quem, int gosma) in _engolidoPor.ToArray())
		{
			if (gosma != g.Corpo) continue;
			_engolidoPor.Remove(quem);
			if (!_players.TryGetValue(quem, out ServerPlayer? solto)) continue;
			solto.Combate.Carencia = 0;
			Transportar(solto, solto.Zone, MapaDaZonaOuCatalogo(solto.Zone)?.PontoLivrePerto(onde) ?? onde);
			Avisar(solto, GosmaDoMajin.AoSoltoNoCaminho);
			MandarFicha(solto);
		}
		if (corpo != null) RemoverNpc(corpo);

		if (dono != null && g.CobraDoDono && dono.Ficha.gosmaNoMapaMod < 1)
		{
			dono.Ficha.gosmaNoMapaMod = 1;
			dono.Ficha.PowerLevel();
			MandarFicha(dono);
		}
		if (dono != null && avisoAoDono.Length > 0) Avisar(dono, avisoAoDono);
		GD.Print($"[server] GOSMA #{g.Corpo} desfeita ({dono?.Name ?? "sem dono"})");
	}

	/// <summary>
	/// O DONO TROCOU DE ZONA -- chamado pelo `MoveToZone` antes da viagem. E quem esta sendo levado e mudado de zona por outra porta (um
	/// admin, a absorcao de outro Majin) sai da gosma: ela volta sem ele.
	/// </summary>
	private void GosmaAoTrocarDeZona(ServerPlayer pl)
	{
		DesfazerAsGosmasDe(pl.Id, GosmaDoMajin.AoDonoQuandoSai);
		if (_engolidoPor.Remove(pl.Id)) pl.Combate.Carencia = 0;
	}

	/// <summary>Este id saiu do mundo (inscrito no `EsquecerTecnicas`): as gosmas dele se desfazem, e quem ia numa sai da conta.</summary>
	private void EsquecerAsGosmas(int id)
	{
		DesfazerAsGosmasDe(id, "");
		_engolidoPor.Remove(id);
	}
}
