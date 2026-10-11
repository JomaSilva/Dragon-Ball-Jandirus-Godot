using System.Text.Json;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Items;
using Jandirus.Core.Npc;
using Jandirus.Core.Ranks;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DE BILLS E WHIS (`--billsteste`) -- a historia inteira, do dia em que Boo cai ao titulo, pelo codigo de
/// producao: o tique (`TickDeBills`), o despacho de verbs da tecla E (`ComandoDeInteracao`), o funil de derrota
/// (`AoPerderALuta`), a reconciliacao de cargo (`ReconciliarDadiva`) e o motor das disciplinas.
///
///     Godot --headless --host --rede 8330 --billsteste --raca Saiyan --conta bancada_bills --nome Fulano
///
/// Roda UMA vez, no primeiro tique em que ha um jogador de verdade (o host): varias regras daqui perguntam
/// "isto e um jogador?" (a noticia da casa, o menu apontado), e num `--server` sem login nao ha quem responda sim.
///
/// CADA REGRA TEM O SEU CONTRA-EXEMPLO: um defeito injetado (os `...DeTeste` de `BillsEWhis` e deste servidor)
/// que a mesma afirmacao tem de REPROVAR. O que a bancada mexe -- a fase, os tronos, a saga de Boo, o relogio do
/// ceu, a ficha do host, os arquivos -- e fotografado na entrada e devolvido no `finally`.
/// </summary>
public partial class GameServer
{
	private static readonly bool BillsDeTeste = Array.IndexOf(OS.GetCmdlineArgs(), "--billsteste") >= 0;
	private bool _billsTestou;

	/// <summary>A faixa de ids dos corpos forjados por esta bancada.</summary>
	private const int IdBaseDoBillsDeTeste = 96_600;

	private int _bwOk, _bwFalhou, _bwCorpos;

	/// <summary>Chamado do <see cref="TickDeBills"/>: roda UMA vez, no primeiro tique em que ha um jogador de verdade.</summary>
	private void DispararABancadaDoBills()
	{
		if (_billsTestou || Jogadores.FirstOrDefault() is not { } host) return;
		_billsTestou = true;
		RodarBancadaDoBills(host);
	}

	private void AfirmarBw(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _bwOk++; GD.Print($"[bills]   OK    {oque}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); return; }
		_bwFalhou++;
		GD.PrintErr($"[bills]   FALHA {oque}   [{detalhe}]");
	}

	/// <summary>Tantos segundos de mundo PRO SISTEMA, pelo tique de producao (30 por segundo).</summary>
	private void BwPassar(double segundos)
	{
		int n = Math.Max(1, (int)Math.Round(segundos / Protocol.TickSeconds));
		for (int i = 0; i < n; i++) TickDeBills(Protocol.TickSeconds);
	}

	/// <summary>Um dia in-game, pela manivela do ceu -- a mesma das sagas -- e um segundo de tique pra o calendario ler.</summary>
	private void BwUmDia()
	{
		_adiantoDoCeu += Ceu.SegundosPorDia;
		BwPassar(1);
	}

	private ServerPlayer BwForjar(string nome, string raca, ZoneKey zona, Vec2 onde, double bp, List<ServerPlayer> forjados)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDoBillsDeTeste + _bwCorpos++,
			Peer = null,
			Name = nome,
			Race = raca,
			Genero = "Male",
			Idade = 25,
			Zone = zona,
			Pos = onde,
			Conta = $"bancada_bills_{(char)('a' + _bwCorpos)}",
			Slot = 0,
			LastInputMs = NowMs(),
			Ficha = new Fighter { Race = raca, BP = bp },
			Livro = new Jandirus.Core.Skills.SkillBook(),
			Knockback = false,
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.Tick(agoraMs: NowMs());
		forjados.Add(novo);
		return novo;
	}

	private void BwRecolher(ServerPlayer corpo)
	{
		corpo.Peer = null;
		_npcsPraTirar.Remove(corpo);
		EsquecerTecnicas(corpo.Id);
		_players.Remove(corpo.Id);
		ZoneList(corpo.Zone.Hash).Remove(corpo);
	}

	/// <summary>Leva um corpo pra outra zona sem mandar pacote de troca de zona ao cliente dele (a tela do host fica onde esta).</summary>
	private void BwLevar(ServerPlayer corpo, ZoneKey zona, Vec2 onde)
	{
		LiteNetLib.NetPeer? tela = corpo.Peer;
		corpo.Peer = null;
		if (corpo.Zone.Equals(zona)) corpo.Pos = onde;
		else MoveToZone(corpo.Id, zona, onde);
		corpo.Peer = tela;
	}

	private static bool BwContem(byte[] onde, byte[] oQue)
	{
		if (oQue.Length == 0) return false;
		for (int i = 0; i + oQue.Length <= onde.Length; i++)
			if (onde.AsSpan(i, oQue.Length).SequenceEqual(oQue)) return true;
		return false;
	}

	/// <summary>Le a fase da visita DIRETO DO ARQUIVO (a memoria provaria so que alguem escreveu num campo). Nulo sem arquivo.</summary>
	private FaseDaVisita? BwFaseNoDisco()
	{
		try
		{
			if (!System.IO.File.Exists(CaminhoDaVisita)) return null;
			return JsonSerializer.Deserialize<EstadoDaVisita>(System.IO.File.ReadAllText(CaminhoDaVisita),
				new JsonSerializerOptions { IncludeFields = true })?.Fase;
		}
		catch { return null; }
	}

	/// <summary>O ultimo `S2C.CasaDeBills` que este jogador recebeu, aberto. Nulo se nenhum.</summary>
	private static (bool Revelada, int Sx, int Sy, int K, ulong Seed, int Bytes)? BwUltimaCasa(int para)
	{
		if (EscutaDeBills == null) return null;
		for (int i = EscutaDeBills.Count - 1; i >= 0; i--)
		{
			(int quem, byte[] fio) = EscutaDeBills[i];
			if (quem != para || fio.Length == 0 || fio[0] != (byte)Protocol.S2C.CasaDeBills) continue;
			var r = new NetDataReader(fio);
			r.GetByte();
			bool revelada = r.GetBool();
			return revelada
				? (true, r.GetInt(), r.GetInt(), r.GetByte(), r.GetULong(), fio.Length)
				: (false, 0, 0, 0, 0UL, fio.Length);
		}
		return null;
	}

	/// <summary>O ultimo `S2C.DivinoPerto` que este jogador recebeu, aberto. Nulo se nenhum.</summary>
	private static (int Corpo, string Tipo, string Nome)? BwUltimoApontado(int para)
	{
		if (EscutaDeBills == null) return null;
		for (int i = EscutaDeBills.Count - 1; i >= 0; i--)
		{
			(int quem, byte[] fio) = EscutaDeBills[i];
			if (quem != para || fio.Length == 0 || fio[0] != (byte)Protocol.S2C.DivinoPerto) continue;
			var r = new NetDataReader(fio);
			r.GetByte();
			return (r.GetInt(), r.GetString(), r.GetString());
		}
		return null;
	}

	private void RodarBancadaDoBills(ServerPlayer pl)
	{
		_bwOk = _bwFalhou = _bwCorpos = 0;
		GD.Print("[bills] ================ BILLS E WHIS: A VISITA, A CASA, O TREINO E O TITULO (dono, 2026-10-11) ================");

		// ---------------------------------------------------------------- a foto do que vai ser mexido
		Fighter f = pl.Ficha;
		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		ChunkId chunkGuardada = pl.ChunkAtual;
		string origemGuardada = pl.PlanetaDeOrigem;
		double bpGuardado = f.BP, kiGuardado = f.Ki, vigorGuardado = f.stamina, gravidadeGuardada = f.GravMastered;
		GodKiState? godkiGuardado = f.godki;
		long zenkaiGuardado = f.zenkaiReady;
		bool trajeGuardado = f.TrajeDoDeusRecebido, letalGuardado = pl.Combate.Letal, knockbackGuardado = pl.Knockback;
		ulong casaOuvidaGuardada = f.CasaDeBillsOuvida;
		bool mortoGuardado = f.dead, koGuardado = f.KO;
		string formaGuardada = pl.Forma.Atual;
		var liberadasGuardadas = new HashSet<int>(pl.Forma.Liberadas);
		var estreiasGuardadas = new HashSet<int>(pl.Forma.EstreiaVista);
		var uiGuardado = (pl.UltraInstinct.Aprendida, pl.UltraInstinct.Real, pl.UltraInstinct.Atual, pl.UltraInstinct.Ligada);
		var ueGuardado = (pl.PoderDaDestruicao.Aprendida, pl.PoderDaDestruicao.Real, pl.PoderDaDestruicao.Atual, pl.PoderDaDestruicao.Ligada);
		TipoDeDisciplina? disciplinaGuardada = pl.Disciplina;
		var mochilaGuardada = pl.Mochila.Pilhas.ToList();
		var roupaGuardada = pl.Visual.Roupa.ToList();
		var chefesGuardados = new HashSet<string>(pl.ChefesVistos);

		EstadoDaVisita visitaGuardada = _visita;
		bool tinhaArquivo = System.IO.File.Exists(CaminhoDaVisita);
		var tronosGuardados = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		byte[]? tituloGuardado = System.IO.File.Exists(ArquivoDoTitulo) ? System.IO.File.ReadAllBytes(ArquivoDoTitulo) : null;
		double ceuGuardado = _adiantoDoCeu, relogioGuardado = _relogioDivino;
		int esperaGuardada = BillsEWhis.EsperaDeTeste;
		EstadoDoElo? boo = _sagas.Do(BillsEWhis.SagaQueOAcorda);
		FaseDaSaga booGuardado = boo?.Fase ?? FaseDaSaga.Adormecida;

		List<(int Para, byte[] Fio)>? escutaAntes = EscutaDeBills;
		List<string>? sagasAntes = EscutaDasSagas, avisosAntes = EscutaDeAvisos;
		var falasAntes = EscutaDeFalas;
		var anunciosAntes = EscutaDeAnuncios;
		var efeitosAntes = EscutaDeEfeitos;
		EscutaDeBills = [];
		EscutaDasSagas = [];
		EscutaDeFalas = [];
		var forjados = new List<ServerPlayer>();

		try
		{
			AfirmarBw("PRECONDICAO: o host tem dono na tela, e a cadeia de sagas tem o elo de Boo",
					  EhJogador(pl) && boo != null, $"jogador={EhJogador(pl)} boo={(boo != null)}");
			if (boo == null) return;
			if (f.dead || f.KO) pl.Combate.Reviver();
			pl.Knockback = false;

			// O MUNDO DA BANCADA COMECA DO ZERO: historia adormecida, os dois tronos vagos, o mes de verdade.
			CarregarBills();
			_visita = new EstadoDaVisita();
			_tronos.Remove(BillsEWhis.CargoDoBills);
			_tronos.Remove(BillsEWhis.CargoDoWhis);
			BillsEWhis.EsperaDeTeste = -1;
			boo.Fase = FaseDaSaga.EmCurso;

			BwAsContasPuras();
			BwALinhaDoTempo(pl, boo);
			BwALutaNaTerra(pl, forjados);
			CasaDeBills? casa = BwADerrota(pl);
			if (casa is not { } c) return;

			ZoneKey? zonaDaCasa = MundoDaCasaProntoDeTeste(c.Corpo);
			AfirmarBw($"(montagem) o mundo da casa nasceu no servidor: {c.Corpo.Nome}, gravidade {c.Gravidade:0}x, {BillsEWhis.Coordenadas(c)}",
					  zonaDaCasa != null);
			if (zonaDaCasa is not { } zona) return;

			BwACasa(pl, c, zona);
			BwACasaDestruida(pl, c, zona);
			BwQuemExiste(pl);
			BwAVisitaSemOBills(pl);
			BwOMenu(pl, zona, forjados);
			BwOTreinoComOWhis(pl, zona, forjados);
			BwAMaestria(pl, zona);
			BwOTreinoComOBills(pl, zona);
			BwODesafio(pl, zona, forjados);
			BwNenhumPoderNoFio(pl, zona);
			BwALimpezaEAMaoDoAdmin(pl);

			AfirmarBw("a bancada chegou ao fim (sem esta linha, abortar no meio reportaria '0 falhas')", true);
		}
		catch (Exception e)
		{
			AfirmarBw($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			BillsEWhis.ExisteSempreDeTeste = BillsEWhis.TreinoSemMultiploDeTeste = BillsEWhis.TreinoNaoAceleraDeTeste = false;
			BillsEWhis.InstintoQueCansaDeTeste = BillsEWhis.InstintoComFormaDeTeste = BillsEWhis.TreinoEhDerrotaDeTeste = false;
			BillsEWhis.MenuSemValidacaoDeTeste = BillsEWhis.CasaVazaDeTeste = BillsEWhis.DesafioSemFuryDeTeste = false;
			BillsEWhis.FracaoDeTeste = 0;
			BillsEWhis.EsperaDeTeste = esperaGuardada;
			PoderNoFioDeBillsDeTeste = false;
			TrajeTodaVezDeTeste = false;
			RoteiroNaoSeCalaDeTeste = PostoDoWhisPeloPontoLivreDeTeste = PostoSemClareiraDeTeste = SemRumoAoChegarDeTeste = false;
			SaidaSoDePertoDeTeste = BichoAcordaOBillsDeTeste = BillsEWhis.FimSemFolegoDeTeste = false;
			EscutaDeBills = escutaAntes;
			EscutaDasSagas = sagasAntes;
			EscutaDeAvisos = avisosAntes;
			EscutaDeFalas = falasAntes;
			EscutaDeAnuncios = anunciosAntes;
			EscutaDeEfeitos = efeitosAntes;

			try
			{
				// OS DOIS SAEM DO MUNDO E A HISTORIA VOLTA COMO ESTAVA (na memoria e no disco).
				ZerarBills();
				_visita = visitaGuardada;
				_relogioDivino = relogioGuardado;
				if (tinhaArquivo) SalvarBills();
				else try { System.IO.File.Delete(CaminhoDaVisita); } catch { /* a pasta e da bancada */ }

				foreach (ServerPlayer corpo in forjados) BwRecolher(corpo);

				_tronos.Clear();
				foreach ((string cargo, string dono) in tronosGuardados) _tronos[cargo] = dono;
				SalvarCargos();
				// O RELOGIO DO TITULO (carencia, tarefas) volta do que estava no disco: coroar e destituir o zeraram.
				try
				{
					if (tituloGuardado != null) System.IO.File.WriteAllBytes(ArquivoDoTitulo, tituloGuardado);
					else if (System.IO.File.Exists(ArquivoDoTitulo)) System.IO.File.Delete(ArquivoDoTitulo);
				}
				catch { /* a pasta e da bancada */ }
				CarregarTitulo();
				if (boo != null) boo.Fase = booGuardado;
				_adiantoDoCeu = ceuGuardado;

				// O HOST VOLTA COMO ESTAVA: a ficha, as disciplinas, a mochila, a forma, o lugar.
				pl.Combate.Curar();
				f.BP = bpGuardado;
				f.GravMastered = gravidadeGuardada;
				f.godki = godkiGuardado;
				f.zenkaiReady = zenkaiGuardado;
				f.TrajeDoDeusRecebido = trajeGuardado;
				f.CasaDeBillsOuvida = casaOuvidaGuardada;
				f.dead = mortoGuardado;
				f.KO = koGuardado;
				pl.Combate.Letal = letalGuardado;
				pl.Knockback = knockbackGuardado;
				(pl.UltraInstinct.Aprendida, pl.UltraInstinct.Real, pl.UltraInstinct.Atual, pl.UltraInstinct.Ligada) = uiGuardado;
				(pl.PoderDaDestruicao.Aprendida, pl.PoderDaDestruicao.Real, pl.PoderDaDestruicao.Atual, pl.PoderDaDestruicao.Ligada) = ueGuardado;
				pl.Disciplina = disciplinaGuardada;
				AplicarDisciplina(pl);
				pl.Mochila.Pilhas.Clear();
				pl.Mochila.Pilhas.AddRange(mochilaGuardada);
				pl.Visual.Roupa.Clear();
				pl.Visual.Roupa.AddRange(roupaGuardada);
				pl.ChefesVistos = chefesGuardados;
				pl.Forma.Entrar(formaGuardada);
				pl.Forma.Liberadas = liberadasGuardadas;
				pl.Forma.EstreiaVista = estreiasGuardadas;
				pl.CenaSegundos = 0;
				AplicarForma(pl);

				if (!pl.Zone.Equals(zonaGuardada)) BwLevar(pl, zonaGuardada, posGuardada);
				else pl.Pos = posGuardada;
				pl.ChunkAtual = chunkGuardada;
				pl.PlanetaDeOrigem = origemGuardada;
				f.Tick(agoraMs: NowMs());
				f.Ki = Math.Min(kiGuardado, f.MaxKi);
				f.stamina = Math.Min(vigorGuardado, f.maxstamina);
				pl.Combate.SincronizarVida();
				ReconciliarDadiva(pl);
			}
			finally
			{
				GD.Print($"[bills] ================ {_bwOk} OK, {_bwFalhou} FALHA(S) ================");
			}
		}
	}

	// =====================================================================
	// 0) AS CONTAS PURAS -- os numeros do dono, a casa e o calendario sem mundo nenhum
	// =====================================================================
	private void BwAsContasPuras()
	{
		GD.Print("[bills] ---- 0) os numeros do dono e as contas puras");

		AfirmarBw("o poder dele a 100% e UM TRILHAO, e na Terra ele luta com 15% disso: cento e cinquenta bilhoes",
				  BillsEWhis.PoderTotal == 1e12 && Math.Abs(BillsEWhis.PoderNaTerra - 150e9) < 1,
				  $"{BillsEWhis.PoderTotal:N0} / {BillsEWhis.PoderNaTerra:N0}");
		AfirmarBw("o adversario de treino tem o TRIPLO do poder de quem treina",
				  Math.Abs(BillsEWhis.PoderDoTreino(1000) - 3000) < 1e-9);
		AfirmarBw("o mes in-game tem 28 dias (WorldClock.dm:19), e a espera e de UM mes",
				  BillsEWhis.DiasPorMes == 28 && BillsEWhis.DiasAteAVisita == 28);
		AfirmarBw("existe <=> cargo vago (a funcao pura): vago = existe, ocupado = nao existe",
				  BillsEWhis.Existe(true) && !BillsEWhis.Existe(false));

		// O CALENDARIO, SEM MUNDO: os quatro ramos da funcao pura.
		var e = new EstadoDaVisita();
		AfirmarBw("sem Boo vencido o calendario nao faz nada",
				  BillsEWhis.Passo(e, booVencido: false, hoje: 100, terraDePe: true) == PassoDaVisita.Nada && e.Fase == FaseDaVisita.Dormindo);
		AfirmarBw("Boo vencido: a historia acorda e a chegada fica marcada pra 28 dias depois",
				  BillsEWhis.Passo(e, true, 100, true) == PassoDaVisita.Despertou && e.Fase == FaseDaVisita.ACaminho && e.DiaDaChegada == 128,
				  $"{e.Fase} dia {e.DiaDaChegada}");
		AfirmarBw("no dia 127 ele AINDA NAO pousa (o prazo e um prazo)",
				  BillsEWhis.Passo(e, true, 127, true) == PassoDaVisita.Nada && e.Fase == FaseDaVisita.ACaminho);
		AfirmarBw("no dia 128, com a Terra DESTRUIDA, ele espera -- e o prazo vencido nao se perde",
				  BillsEWhis.Passo(e, true, 128, terraDePe: false) == PassoDaVisita.Nada && e.Fase == FaseDaVisita.ACaminho);
		AfirmarBw("...e pousa no primeiro passo em que a Terra existe",
				  BillsEWhis.Passo(e, true, 131, true) == PassoDaVisita.Pousou && e.Fase == FaseDaVisita.NaTerra);

		// A CASA: a mais pesada da regiao, igual em toda pergunta, e diferente noutro universo.
		IReadOnlyList<CasaDeBills> casas = BillsEWhis.Casas(SeedDoUniverso);
		double maior = 0;
		int mundos = 0;
		for (int sy = -BillsEWhis.RaioDaBuscaEmCelulas; sy <= BillsEWhis.RaioDaBuscaEmCelulas; sy++)
			for (int sx = -BillsEWhis.RaioDaBuscaEmCelulas; sx <= BillsEWhis.RaioDaBuscaEmCelulas; sx++)
			{
				if (Sistemas.Do(SeedDoUniverso, sx, sy) is not { } s) continue;
				for (int k = 0; k < s.Orbitas; k++)
				{
					if (k == s.OrbitaPreFeita) continue;
					mundos++;
					maior = Math.Max(maior, MundoProcedural.GravidadeDaSeed(s.Planeta(k).Seed));
				}
			}
		AfirmarBw($"a casa e o mundo gerado de MAIOR GRAVIDADE dos {mundos} da regiao ({maior:0}x)",
				  casas.Count > 0 && casas[0].Gravidade == maior && maior >= 41, $"casa {(casas.Count > 0 ? casas[0].Gravidade : 0):0}x");
		AfirmarBw("...a lista vem em ordem (da mais pesada pra baixo) e guarda reservas pra quando a primeira for destruida",
				  casas.Count == BillsEWhis.CasasGuardadas && casas.Zip(casas.Skip(1)).All(p => p.First.Gravidade >= p.Second.Gravidade),
				  string.Join(" ", casas.Select(c => $"{c.Gravidade:0}x")));
		if (casas.Count == 0) return;

		CasaDeBills casa = casas[0];
		AfirmarBw("...o endereco bate com a carta: a celula e a orbita devolvem ESTE corpo",
				  Sistemas.Do(SeedDoUniverso, casa.Sx, casa.Sy) is { } sis && sis.Planeta(casa.K).Seed == casa.Corpo.Seed
				  && CorpoGerado.DaZona(SeedDoUniverso, Espaco.ZonaDe(casa.Corpo)) is { } volta && volta.Sx == casa.Sx && volta.Sy == casa.Sy && volta.K == casa.K);
		AfirmarBw("...e a mesma pergunta da a mesma casa (funcao pura da semente), e outro universo da outra",
				  BillsEWhis.Casa(SeedDoUniverso)?.Corpo.Seed == casa.Corpo.Seed
				  && BillsEWhis.Casa(SeedDoUniverso ^ 0x5EED0FAD1FFUL)?.Corpo.Seed != casa.Corpo.Seed);
		AfirmarBw("com a primeira destruida, a casa passa a ser a SEGUNDA da lista",
				  casas.Count > 1 && BillsEWhis.Casa(SeedDoUniverso, p => p.Seed == casa.Corpo.Seed)?.Corpo.Seed == casas[1].Corpo.Seed);
		AfirmarBw($"as coordenadas saem como a carta as escreve: \"{BillsEWhis.Coordenadas(casa)}\"",
				  BillsEWhis.Coordenadas(casa) == $"Sistema {casa.Sx}:{casa.Sy}, órbita #{casa.K}");

		// OS POSTOS, NUM MAPA DE BANCADA: uma lingua de chao de UM tile de largura (o caso medido na `--diagbills`:
		// o pouso entre dois lagos) e, a quatro tiles dali, uma fileira firme onde os dois cabem lado a lado.
		const int ladoDoLago = 64;
		var barradas = new byte[ladoDoLago * ladoDoLago / 8];
		for (int y = 0; y < ladoDoLago; y++)
			for (int x = 0; x < ladoDoLago; x++)
			{
				bool chao = (x == 32 && y is >= 20 and <= 44) || (y == 30 && x is >= 36 and <= 40);
				if (!chao) barradas[(y * ladoDoLago + x) >> 3] |= (byte)(1 << ((y * ladoDoLago + x) & 7));
			}
		ZoneCollision lago = ZoneCollision.Montar(ladoDoLago, ladoDoLago, barradas);
		(Vec2 postoB, Vec2 postoW) = PostosLadoALado(lago, lago.CentroDaCelula(32, 32), null);
		AfirmarBw("OS POSTOS, numa lingua de chao de um tile: os dois vao pra fileira firme mais perto, NA MESMA LINHA e a dois tiles um do outro",
				  postoB.Equals(lago.CentroDaCelula(36, 30)) && postoW.Equals(lago.CentroDaCelula(38, 30)),
				  $"Bills ({postoB.X:0},{postoB.Y:0}) Whis ({postoW.X:0},{postoW.Y:0})");
		PostoDoWhisPeloPontoLivreDeTeste = true;
		try
		{
			(Vec2 velhoB, Vec2 velhoW) = PostosLadoALado(lago, lago.CentroDaCelula(32, 32), null);
			AfirmarBw("(defeito injetado: o posto do Whis e 'o ponto livre mais perto do lado dele') na lingua de um tile os dois ficam COLADOS, fora de linha -- e a afirmacao de cima reprova",
					  !(velhoW.Y == velhoB.Y && Math.Abs(velhoW.X - velhoB.X - 2 * ZoneCollision.TileSize) < 0.5f),
					  $"Bills ({velhoB.X:0},{velhoB.Y:0}) Whis ({velhoW.X:0},{velhoW.Y:0})");
		}
		finally { PostoDoWhisPeloPontoLivreDeTeste = false; }

		// A CLAREIRA, NUM CAMPO ABERTO COM UMA ARVORE: a fileira pedida e toda de chao firme, mas ha um tronco duas
		// fileiras abaixo dela -- a copa cobriria quem parasse ali (o outro caso medido na `--diagbills`).
		var comArvore = new byte[ladoDoLago * ladoDoLago / 8];
		comArvore[(34 * ladoDoLago + 33) >> 3] |= (byte)(1 << ((34 * ladoDoLago + 33) & 7));
		ZoneCollision campo = ZoneCollision.Montar(ladoDoLago, ladoDoLago, comArvore);
		bool CopaEmCima(Vec2 bills)
		{
			int cx = (int)MathF.Floor(bills.X / ZoneCollision.TileSize), cy = (int)MathF.Floor(bills.Y / ZoneCollision.TileSize);
			for (int dy = 0; dy <= BillsEWhis.ClareiraFileiras; dy++)
				for (int dx = -BillsEWhis.ClareiraColunas; dx <= 2 + BillsEWhis.ClareiraColunas; dx++)
					if (campo.BlockedCell(cx + dx, cy + dy)) return true;
			return false;
		}
		(Vec2 claroB, Vec2 claroW) = PostosLadoALado(campo, campo.CentroDaCelula(32, 32), null);
		AfirmarBw("OS POSTOS FOGEM DA COPA: com um tronco duas fileiras abaixo da fileira pedida, os dois vao pra clareira mais perto -- lado a lado, sem nada que barre na janela da copa",
				  !CopaEmCima(claroB) && claroW.Y == claroB.Y && Math.Abs(claroW.X - claroB.X - 2 * ZoneCollision.TileSize) < 0.5f
				  && (claroB - campo.CentroDaCelula(32, 32)).Length <= 8 * ZoneCollision.TileSize,
				  $"Bills ({claroB.X / ZoneCollision.TileSize:0.0},{claroB.Y / ZoneCollision.TileSize:0.0}) tiles; o tronco esta em (33,34)");
		PostoSemClareiraDeTeste = true;
		try
		{
			(Vec2 cobertoB, _) = PostosLadoALado(campo, campo.CentroDaCelula(32, 32), null);
			AfirmarBw("(defeito injetado: a primeira fileira firme serve) os dois ficam na fileira pedida, DEBAIXO da copa -- e a afirmacao de cima reprova",
					  CopaEmCima(cobertoB) && cobertoB.Equals(campo.CentroDaCelula(32, 32)));
		}
		finally { PostoSemClareiraDeTeste = false; }

		// O ARQUIVO: ele tem dono na limpeza, e conta nenhuma alcanca o nome dele.
		AfirmarBw("o arquivo da visita esta INSCRITO na limpeza total (e por isso reservado)",
				  _sistemasDoMundo.Exists(s => s.Meu(NomeDoArquivoDaVisita)) && AccountStore.ArquivosReservados.Contains(NomeDoArquivoDaVisita));
		AfirmarBw("...e conta nenhuma -- nem uma chamada 'bills' ou 'visita-de-bills' -- cai no nome dele",
				  AccountStore.NomeDeArquivo("bills") + ".json" != NomeDoArquivoDaVisita
				  && AccountStore.NomeDeArquivo("visita-de-bills") + ".json" != NomeDoArquivoDaVisita,
				  AccountStore.NomeDeArquivo("visita-de-bills"));

		// AS FALAS NAO DIZEM NUMERO DE PODER (a regra principal do BP): nenhuma tem um algarismo sequer fora dos marcadores.
		var todas = new List<string>
		{
			BillsEWhis.AvisoDoDespertar, BillsEWhis.AvisoDaChegada, BillsEWhis.WhisTranquiliza, BillsEWhis.FalaDeVoltarACochilar,
			BillsEWhis.AvisoDaDerrota, BillsEWhis.WhisEncerraAVisita, BillsEWhis.AvisoDaVisitaEncerrada, BillsEWhis.NoticiaDaCasa,
			BillsEWhis.AvisoDaMudanca, BillsEWhis.BillsDorme, BillsEWhis.WhisComecaOTreino, BillsEWhis.WhisComecaComInstinto,
			BillsEWhis.WhisDerrubouOAluno, BillsEWhis.WhisFoiDerrubado, BillsEWhis.WhisAcabouOTempo, BillsEWhis.WhisAlunoParou,
			BillsEWhis.WhisAlunoFugiu, BillsEWhis.WhisIntrometido, BillsEWhis.WhisOcupado, BillsEWhis.BillsComecaOTreino,
			BillsEWhis.BillsComecaComDestruicao, BillsEWhis.BillsDerrubouOAluno, BillsEWhis.BillsFoiDerrubadoNoTreino,
			BillsEWhis.BillsAcabouOTempo, BillsEWhis.BillsAlunoParou, BillsEWhis.BillsAlunoFugiu, BillsEWhis.BillsIntrometido,
			BillsEWhis.BillsOcupado, BillsEWhis.AvisoDoDesafio, BillsEWhis.BillsVenceuODesafio, BillsEWhis.BillsAcabouOPrazo,
			BillsEWhis.BillsDesafianteSumiu, BillsEWhis.BillsDesafioAnulado, BillsEWhis.AvisoDoDesafioAnulado,
			BillsEWhis.AvisoDoTituloDefendido, BillsEWhis.BillsSemKiDivino, BillsEWhis.BillsDesafioCedo, BillsEWhis.BillsFundido,
			BillsEWhis.ViaDoTitulo, BillsEWhis.RumoNaTerra, BillsEWhis.RumoDosDois, BillsEWhis.RumoDoBills, BillsEWhis.RumoDoWhis,
		};
		todas.AddRange(BillsEWhis.FalasDeAcordar);
		todas.AddRange(BillsEWhis.FalasDeDerrubar);
		todas.AddRange(BillsEWhis.WhisRecebe);
		todas.AddRange(BillsEWhis.WhisDesvia);
		todas.AddRange(BillsEWhis.BillsProvoca);
		foreach (Fala[] roteiro in (Fala[][])[BillsEWhis.FalasDaChegada, BillsEWhis.FalasDaDerrota, BillsEWhis.FalasDoDesafio, BillsEWhis.FalasDaCoroacao])
			todas.AddRange(roteiro.Select(x => x.Texto));
		foreach (string tipo in (string[])[BillsEWhis.TipoWhis, BillsEWhis.TipoBills, BillsEWhis.TipoEmTreino, BillsEWhis.TipoEmDesafio])
			foreach (Interacoes.Acao a in Interacoes.De(tipo)) { todas.Add(a.Rotulo); todas.Add(a.Dica); }

		string? comNumero = todas.Find(t => t.Any(char.IsDigit));
		AfirmarBw($"nenhuma das {todas.Count} falas e rotulos de menu tem um ALGARISMO (ninguem diz poder em numero)",
				  comNumero == null, comNumero ?? "");
		AfirmarBw("...e nenhuma fala passa do que cabe numa linha de chat",
				  todas.All(t => t.Length <= Protocol.MaxFala));
	}

	// =====================================================================
	// 1) A LINHA DO TEMPO -- Boo cai, um mes passa (com um reinicio no meio), ele pousa
	// =====================================================================
	private void BwALinhaDoTempo(ServerPlayer pl, EstadoDoElo boo)
	{
		GD.Print("[bills] ---- 1) a linha do tempo: a saga de Boo, o mes in-game e o reinicio");

		BwPassar(1);
		AfirmarBw("com a saga de Boo em curso nada acontece: a historia dorme e ninguem esta no mundo",
				  _visita.Fase == FaseDaVisita.Dormindo && _bills.Corpo == null && _whis.Corpo == null);

		boo.Fase = FaseDaSaga.Consumada;
		BwPassar(1);
		AfirmarBw("o vilao que VENCE nao o acorda (o pedido e 'apos os jogadores terem derrotado')",
				  _visita.Fase == FaseDaVisita.Dormindo);

		boo.Fase = FaseDaSaga.Vencida;
		long hoje = DiaDoMundo;
		EscutaDasSagas!.Clear();
		BwPassar(1);
		AfirmarBw("Boo VENCIDO acorda a historia: a chegada fica marcada pra daqui a 28 dias in-game",
				  _visita.Fase == FaseDaVisita.ACaminho && _visita.DiaDaChegada == hoje + 28,
				  $"{_visita.Fase}, chegada no dia {_visita.DiaDaChegada} (hoje {hoje})");
		AfirmarBw("...o mundo ouviu que alguma coisa acordou", EscutaDasSagas.Contains(BillsEWhis.AvisoDoDespertar));
		AfirmarBw("...e a fase esta no DISCO, nao so na memoria", BwFaseNoDisco() == FaseDaVisita.ACaminho, $"{BwFaseNoDisco()}");

		for (int d = 1; d <= 10; d++) BwUmDia();
		AfirmarBw("dez dias depois ele AINDA NAO chegou", _visita.Fase == FaseDaVisita.ACaminho && _bills.Corpo == null);

		// ---- o reinicio: a memoria some e volta do disco ----
		long chegada = _visita.DiaDaChegada;
		CarregarBills();
		AfirmarBw("depois de um REINICIO a contagem continua de onde estava (a fase e o dia vem do disco)",
				  _visita.Fase == FaseDaVisita.ACaminho && _visita.DiaDaChegada == chegada, $"{_visita.Fase} dia {_visita.DiaDaChegada}");

		for (int d = 11; d <= 27; d++) BwUmDia();
		AfirmarBw("no dia 27 ele ainda nao pousou (o prazo e um prazo: o teto dispara)",
				  _visita.Fase == FaseDaVisita.ACaminho && _bills.Corpo == null && DiaDoMundo == chegada - 1,
				  $"dia {DiaDoMundo}, chegada {chegada}");

		EscutaDasSagas.Clear();
		EscutaDeFalas!.Clear();
		BwUmDia();
		AfirmarBw("no dia 28 ele POUSA: a fase vira 'na Terra' e os dois estao no mundo",
				  _visita.Fase == FaseDaVisita.NaTerra && _bills.Corpo != null && _whis.Corpo != null, $"{_visita.Fase}");
		AfirmarBw("...o mundo inteiro ouviu a chegada", EscutaDasSagas.Contains(BillsEWhis.AvisoDaChegada));
		AfirmarBw("...e a fase nova esta no disco", BwFaseNoDisco() == FaseDaVisita.NaTerra);
		if (_bills.Corpo is not { } b || _whis.Corpo is not { } w) return;

		AfirmarBw("os dois estao NA TERRA, e o Whis esta AO LADO do Bills: na mesma linha, a dois tiles",
				  b.Zone.Equals(TerraDaVisita) && w.Zone.Equals(TerraDaVisita)
				  && Math.Abs(w.Pos.Y - b.Pos.Y) < 0.5f && Math.Abs(w.Pos.X - b.Pos.X - BillsEWhis.TilesEntreOsDois * ZoneCollision.TileSize) < 0.5f,
				  $"Bills ({b.Pos.X:0},{b.Pos.Y:0}) Whis ({w.Pos.X:0},{w.Pos.Y:0})");
		AfirmarBw("o corpo do Bills e a FOLHA dele (a raca-marcadora) e ele veste o TRAJE; o Whis nao veste peca nenhuma",
				  b.Race == BillsEWhis.RacaDoBills && w.Race == BillsEWhis.RacaDoWhis
				  && _visual?.CorpoSprite(b.Visual, b.Race, b.Genero) == BillsEWhis.FolhaDoCorpo(BillsEWhis.RacaDoBills)
				  && _visual?.CorpoSprite(w.Visual, w.Race, w.Genero) == BillsEWhis.FolhaDoCorpo(BillsEWhis.RacaDoWhis)
				  && b.Visual.Roupa.Count == 1 && b.Visual.Roupa[0].Caminho == _visual?.Peca(BillsEWhis.Traje)
				  && w.Visual.Roupa.Count == 0,
				  $"roupa do Bills: {string.Join(",", b.Visual.Roupa.Select(r => r.Caminho))} | catalogo: {_visual?.Peca(BillsEWhis.Traje)}");
		AfirmarBw("...sem cabelo e sem olhos de gente por cima (a folha ja traz a cara)",
				  !Jandirus.Core.Appearance.VisualCatalog.TemCabelo(b.Race) && !Jandirus.Core.Appearance.VisualCatalog.CorpoTemRosto(b.Visual, b.Race)
				  && !Jandirus.Core.Appearance.VisualCatalog.TemCabelo(w.Race) && !Jandirus.Core.Appearance.VisualCatalog.CorpoTemRosto(w.Visual, w.Race));

		BwPassar(BillsEWhis.FalasDaChegada.Length * BillsEWhis.SegundosEntreFalas + 3);
		AfirmarBw($"os dois falaram ao chegar ({BillsEWhis.FalasDaChegada.Length} linhas, cada uma na boca certa)",
				  BillsEWhis.FalasDaChegada.All(x => EscutaDeFalas.Any(e => e.Texto == x.Texto && e.Quem == (x.Quem == QuemFala.Bills ? b.Id : w.Id))),
				  $"{EscutaDeFalas.Count} fala(s) ouvidas");

		// ---- o reinicio com ele NA TERRA: os corpos somem, a fase volta do disco, e eles renascem la ----
		int idAntigo = b.Id;
		CarregarBills();
		AfirmarBw("um reinicio tira os corpos do mundo...", !_players.ContainsKey(idAntigo) && _bills.Corpo == null);
		BwPassar(1);
		AfirmarBw("...e no segundo seguinte os dois estao DE VOLTA na Terra (a fase mandou; nao ha 'retomar' pra esquecer)",
				  _visita.Fase == FaseDaVisita.NaTerra && _bills.Corpo is { } b2 && b2.Zone.Equals(TerraDaVisita) && _whis.Corpo != null);

		// ---- a parada do servidor no mesmo processo: o mundo esvazia POR FORA, sem avisar este sistema ----
		if (_bills.Corpo is { } b3 && _whis.Corpo is { } w3)
		{
			Agendar(60, () => AfirmarBw("(um roteiro que sobrevivesse a parada falaria aqui -- e nao pode)", false));
			foreach (ServerPlayer corpo in (ServerPlayer[])[b3, w3]) { _players.Remove(corpo.Id); ZoneList(corpo.Zone.Hash).Remove(corpo); }
			BwPassar(1);
			AfirmarBw("com o mundo esvaziado por fora (a parada no mesmo processo), os dois renascem como num reinicio",
					  _bills.Corpo != null && !ReferenceEquals(_bills.Corpo, b3) && _players.ContainsKey(_bills.Corpo.Id) && _whis.Corpo != null);
			AfirmarBw("...e o que era de cena morreu junto (nenhum roteiro agendado atravessa a parada)", _roteiroDivino.Count == 0);
		}
	}

	// =====================================================================
	// 2) A LUTA NA TERRA -- 15% do poder, so contra quem bate, e ele so derruba
	// =====================================================================
	private void BwALutaNaTerra(ServerPlayer pl, List<ServerPlayer> forjados)
	{
		GD.Print("[bills] ---- 2) a luta na Terra");
		if (_bills.Corpo is not { } b || _whis.Corpo is not { } w) { AfirmarBw("(montagem) os dois estao na Terra", false); return; }

		// ---- o numero, conferido NO SERVIDOR ----
		AfirmarBw("o poder BASE dele na Terra e 150 bilhoes (15% de um trilhao) -- na ficha e no pino do papel",
				  Math.Abs(b.Ficha.BP - 150e9) < 1 && Math.Abs(b.Papel!.BpAtual - 150e9) < 1, $"{b.Ficha.BP:N0} / pino {b.Papel!.BpAtual:N0}");
		AfirmarBw("...e descansado o poder EXPRESSO dele e esse mesmo numero (a ficha e neutra: nada multiplica por fora)",
				  Math.Abs(b.Ficha.expressedBP - 150e9) <= 150e9 * 0.02, $"{b.Ficha.expressedBP:N0}");
		b.Ficha.BP *= 3;
		TickDoRoteiro();
		AfirmarBw("empurrar o poder dele pra cima e desfeito no tique seguinte (o pino de chefe)",
				  Math.Abs(b.Ficha.BP - 150e9) < 1, $"{b.Ficha.BP:N0}");

		BillsEWhis.FracaoDeTeste = 1;
		try
		{
			CarregarBills();
			BwPassar(1);
			AfirmarBw("(defeito injetado: ele luta com 100%) o corpo que nasce NAO tem os 150 bilhoes",
					  _bills.Corpo is { } forte && Math.Abs(forte.Ficha.BP - 150e9) > 1, $"{_bills.Corpo?.Ficha.BP:N0}");
		}
		finally { BillsEWhis.FracaoDeTeste = 0; }
		CarregarBills();
		BwPassar(1);
		b = _bills.Corpo!;
		w = _whis.Corpo!;
		AfirmarBw("(sem o defeito) ele renasce com os 150 bilhoes", Math.Abs(b.Ficha.BP - 150e9) < 1, $"{b.Ficha.BP:N0}");

		// ---- acordado no MEIO da fala de chegada: o resto dela se cala (medido na `--diagbills`: saia intercalado) ----
		ServerPlayer despertador = BwForjar("Despertador", "Human", TerraDaVisita, new Vec2(b.Pos.X - 64, b.Pos.Y - 64), 2e9, forjados);
		RoteiroNaoSeCalaDeTeste = true;
		try
		{
			PousarNaTerra();
			BwPassar(1.6);
			MarcarAgressao(b, despertador);
			BwPassar(0.2);
			AfirmarBw("(defeito injetado: o roteiro nao se cala) acordado no meio da fala de chegada, as linhas dela continuam na fila -- sairiam por cima da briga",
					  _bills.Modo == ModoDivino.Lutando && _roteiroDivino.Count > 0, $"{_roteiroDivino.Count} na fila");
		}
		finally { RoteiroNaoSeCalaDeTeste = false; }
		CarregarBills();
		BwPassar(1);
		b = _bills.Corpo!;
		w = _whis.Corpo!;
		PousarNaTerra();
		BwPassar(1.6);
		int naFila = _roteiroDivino.Count;
		MarcarAgressao(b, despertador);
		BwPassar(0.2);
		AfirmarBw("ACORDADO NO MEIO DA FALA DE CHEGADA, o resto dela se cala: a fila do roteiro esvazia (ninguem recita por cima da briga)",
				  naFila > 0 && _bills.Modo == ModoDivino.Lutando && _roteiroDivino.Count == 0, $"{naFila} na fila antes, {_roteiroDivino.Count} depois");
		PousarNaTerra();
		BwPassar(1.6);
		naFila = _roteiroDivino.Count;
		b.Combate.Nocautear(12);
		BwPassar(0.1);
		AfirmarBw("...e DERRUBADO no meio dela, so sobra na fila a despedida (levantar, as sete linhas e a partida): nenhuma linha da chegada",
				  naFila > 0 && _visita.Fase == FaseDaVisita.EmCasa && _roteiroDivino.Count == BillsEWhis.FalasDaDerrota.Length + 2,
				  $"{naFila} na fila antes, {_roteiroDivino.Count} depois");
		BwRecolher(despertador);
		forjados.Remove(despertador);

		// (a derrota de cima foi so pra olhar a fila: a historia volta pra "na Terra", e os dois renascem la)
		ZerarBills();
		_visita.Fase = FaseDaVisita.NaTerra;
		SalvarBills();
		BwPassar(1);
		b = _bills.Corpo!;
		w = _whis.Corpo!;

		// ---- parado: o Bills dorme e DA pra bater; o Whis nunca ----
		AfirmarBw("o Bills COCHILA: sem mente (estatua), mas atacavel -- e o convite",
				  _bills.Modo == ModoDivino.Cochilando && b.Cerebro == null && !b.Combate.Intocavel);
		AfirmarBw("o Whis esta parado e e INTOCAVEL (ninguem o enfrenta por conta propria)",
				  _whis.Modo == ModoDivino.Parado && w.Cerebro == null && w.Combate.Intocavel);
		AfirmarBw("os dois nao perdem membro, e o golpe deles DERRUBA (nao e letal)",
				  b.Combate.MembrosFirmes && w.Combate.MembrosFirmes && !b.Combate.Letal && !w.Combate.Letal);

		// ---- ninguem por perto e atacado: um novato a tres tiles nao e presa ----
		ServerPlayer novato = BwForjar("Novato", "Human", TerraDaVisita, new Vec2(b.Pos.X + 96, b.Pos.Y), 10, forjados);
		BwPassar(2);
		AfirmarBw("um novato parado a tres tiles NAO e atacado (ele nao caca ninguem)",
				  _bills.Modo == ModoDivino.Cochilando && b.Cerebro == null && b.Papel!.PresaDoRoteiro == 0);

		// ---- SO GENTE o acorda: um corpo do mundo (com papel de NPC) que o acerte nao entra na briga ----
		ServerPlayer? bicho = NascerNpc("cidadao", TerraDaVisita, new Vec2(b.Pos.X + 64, b.Pos.Y + 64), 7_101_199);
		if (bicho == null) AfirmarBw("(montagem) um NPC do mundo nasceu ao lado dele", false);
		else
		{
			b.UltimoAgressor = bicho.Id;
			BwPassar(0.2);
			AfirmarBw("um NPC DO MUNDO que o acerte NAO o tira do cochilo (o pedido e 'caso os players derrotem ele')",
					  _bills.Modo == ModoDivino.Cochilando && b.Cerebro == null && _desafiantesNaTerra.Count == 0);
			BichoAcordaOBillsDeTeste = true;
			try
			{
				b.UltimoAgressor = bicho.Id;
				BwPassar(0.2);
				AfirmarBw("(defeito injetado: qualquer corpo o acorda) o NPC entra na briga e o Bills parte pra cima dele -- a afirmacao de cima reprova",
						  _bills.Modo == ModoDivino.Lutando && _desafiantesNaTerra.Contains(bicho.Id));
			}
			finally { BichoAcordaOBillsDeTeste = false; }
			RemoverNpc(bicho);
			CarregarBills();
			BwPassar(1);
			b = _bills.Corpo!;
			w = _whis.Corpo!;
		}

		// ---- quem bate acorda: pelo SOCO (o funil de agressao) ----
		ServerPlayer a1 = BwForjar("Valente", "Human", TerraDaVisita, new Vec2(b.Pos.X - 64, b.Pos.Y), 2e9, forjados);
		EscutaDeFalas!.Clear();
		MarcarAgressao(b, a1);
		BwPassar(0.2);
		AfirmarBw("quem BATE nele o acorda: ele ganha mente e a presa dele e quem bateu",
				  _bills.Modo == ModoDivino.Lutando && b.Cerebro != null && PresaDoNpc(b)?.Id == a1.Id, $"{_bills.Modo}, presa {PresaDoNpc(b)?.Name}");
		AfirmarBw("...e ele reclama de ter sido acordado (uma das falas de acordar, na boca dele)",
				  EscutaDeFalas.Any(e => e.Quem == b.Id && BillsEWhis.FalasDeAcordar.Contains(e.Texto)));
		AfirmarBw("...o novato ao lado CONTINUA fora da briga", PresaDoNpc(b)?.Id != novato.Id);

		// ---- e pelo RAIO: o tiro so escreve o `UltimoAgressor`, sem rancor -- e ele revida do mesmo jeito ----
		ServerPlayer a2 = BwForjar("Atirador", "Human", TerraDaVisita, new Vec2(b.Pos.X + 320, b.Pos.Y), 2e9, forjados);
		b.RancorAte = 0;
		b.UltimoAgressor = a2.Id;
		BwPassar(0.2);
		AfirmarBw("quem o acerta de LONGE (ki: so o `UltimoAgressor`, sem rancor de soco) tambem vira presa",
				  PresaDoNpc(b)?.Id == a2.Id, $"presa {PresaDoNpc(b)?.Name}");

		// ---- quem ele derruba sai da briga; quem sobra, continua ----
		EscutaDeFalas.Clear();
		a2.Combate.Nocautear(60);
		BwPassar(0.2);
		AfirmarBw("derrubado o atirador, a presa volta a ser quem ainda esta de pe na briga",
				  PresaDoNpc(b)?.Id == a1.Id, $"presa {PresaDoNpc(b)?.Name}");
		AfirmarBw("...e ele comenta a queda", EscutaDeFalas.Any(e => e.Quem == b.Id && BillsEWhis.FalasDeDerrubar.Contains(e.Texto)));

		// ---- o territorio: quem sai dele deixa de ser perseguido ----
		Vec2 antes = a1.Pos;
		a1.Pos = new Vec2(_bills.Posto.X + (BillsEWhis.TerritorioTiles + 5) * ZoneCollision.TileSize, _bills.Posto.Y);
		BwPassar(0.2);
		AfirmarBw("quem sai do territorio (40 tiles do cochilo) deixa de ser perseguido -- ele nao e arrastado pelo mapa",
				  b.Papel!.PresaDoRoteiro == 0 && b.Cerebro == null);
		a1.Pos = antes;
		BwPassar(0.2);
		AfirmarBw("...e voltando ao territorio a briga continua", PresaDoNpc(b)?.Id == a1.Id);

		// ---- todo mundo no chao: ele volta a dormir INTEIRO, no posto ----
		b.Combate.Corpo.Partes[0].Vida *= 0.5;
		b.Combate.SincronizarVida();
		b.Pos = new Vec2(_bills.Posto.X + 200, _bills.Posto.Y);
		a1.Combate.Nocautear(60);
		BwPassar(BillsEWhis.SegundosAteVoltarACochilar - 1);
		AfirmarBw("sem ninguem de pe ele ESPERA uns segundos antes de desistir da briga (ainda nao voltou)",
				  _bills.Modo == ModoDivino.Lutando && b.Cerebro == null);
		BwPassar(2);
		AfirmarBw("...e entao volta a cochilar: no posto, inteiro e sem mente",
				  _bills.Modo == ModoDivino.Cochilando && (b.Pos - _bills.Posto).Length < 1 && b.Ficha.HP >= 99.9 && b.Cerebro == null,
				  $"{_bills.Modo}, HP {b.Ficha.HP:0.0}, a {(b.Pos - _bills.Posto).Length:0} px do posto");
		AfirmarBw("...e quem levantar ao lado dele NAO e atacado de novo (so volta a briga quem bater de novo)",
				  ((Func<bool>)(() => { a1.Combate.Levantar(); BwPassar(0.5); return b.Papel!.PresaDoRoteiro == 0 && b.Cerebro == null && _bills.Modo == ModoDivino.Cochilando; }))());

		// ---- ele nao morre: o golpe que mataria o DERRUBA ----
		foreach (BodyPart p in b.Combate.Corpo.Partes) p.Vida = 0;
		b.Combate.SincronizarVida();
		bool morreu = b.Combate.Morrer();
		AfirmarBw("o golpe que MATARIA o derruba: `Morrer` e negado, ele cai nocauteado e continua vivo",
				  !morreu && !b.Ficha.dead && b.Ficha.KO, $"morreu={morreu} dead={b.Ficha.dead} KO={b.Ficha.KO}");
		b.Combate.Levantar();
		RestaurarODivino(b);
	}

	// =====================================================================
	// 3) A DERROTA -- a fala, as coordenadas, a casa revelada
	// =====================================================================
	private CasaDeBills? BwADerrota(ServerPlayer pl)
	{
		GD.Print("[bills] ---- 3) a derrota na Terra: a fala, as coordenadas e a mudanca pra casa");
		if (_bills.Corpo is not { } b || _whis.Corpo is not { } w) { AfirmarBw("(montagem) os dois estao na Terra", false); return null; }
		if (CasaAtual() is not { } casa) { AfirmarBw("(montagem) este universo tem uma casa pro Bills", false); return null; }
		string coordenadas = BillsEWhis.Coordenadas(casa), mundo = NomeDoMundo(casa.Corpo);

		// ---- ANTES da derrota ninguem conhece a casa: o pacote nao leva endereco ----
		EscutaDeBills!.Clear();
		_casaVistaPor.Clear();
		BwPassar(1);
		(bool Revelada, int Sx, int Sy, int K, ulong Seed, int Bytes)? antes = BwUltimaCasa(pl.Id);
		AfirmarBw("ANTES de ele cair, o pacote da casa diz 'nao revelada' e NAO leva endereco (2 bytes: o opcode e o nao)",
				  antes is { Revelada: false, Bytes: 2 }, $"{antes}");

		BillsEWhis.CasaVazaDeTeste = true;
		try
		{
			_casaVistaPor.Clear();
			BwPassar(1);
			AfirmarBw("(defeito injetado: a casa vaza antes da hora) o pacote passa a levar o endereco -- e a afirmacao de cima o pegaria",
					  BwUltimaCasa(pl.Id) is { Revelada: true });
		}
		finally { BillsEWhis.CasaVazaDeTeste = false; }
		_casaVistaPor.Clear();
		BwPassar(1);

		// ---- ele cai ----
		pl.Ficha.CasaDeBillsOuvida = 0;
		EscutaDeFalas!.Clear();
		EscutaDasSagas!.Clear();
		EscutaDeBills.Clear();
		MarcarAgressao(b, pl);
		b.Combate.Nocautear(12);
		BwPassar(0.1);
		AfirmarBw("nocauteado, a fase vira 'em casa' NA HORA -- e vai pro disco antes de qualquer fala",
				  _visita.Fase == FaseDaVisita.EmCasa && BwFaseNoDisco() == FaseDaVisita.EmCasa && _visita.DerrubadoPor == pl.Name,
				  $"{_visita.Fase} / disco {BwFaseNoDisco()} / por '{_visita.DerrubadoPor}'");
		AfirmarBw("...e os dois CONTINUAM na Terra enquanto falam (ninguem some no meio da frase), o Bills ja intocavel",
				  _cenaDivina && _bills.Corpo == b && b.Zone.Equals(TerraDaVisita) && b.Combate.Intocavel);
		BwPassar(1);
		AfirmarBw("...mesmo com o laco de quem-existe rodando", _bills.Corpo == b && _players.ContainsKey(b.Id));

		BwPassar(BillsEWhis.SegundosNoChao + 1 + BillsEWhis.FalasDaDerrota.Length * BillsEWhis.SegundosEntreFalas - 2);
		AfirmarBw("ele se levantou pra falar (de pe, inteiro)", !b.Ficha.KO || _bills.Corpo != b);
		AfirmarBw($"O BILLS DISSE AS COORDENADAS: \"{coordenadas}\"",
				  EscutaDeFalas.Any(e => e.Quem == b.Id && e.Texto.Contains(coordenadas)), $"{EscutaDeFalas.Count} fala(s)");
		AfirmarBw("...e o Whis as repetiu, com o nome do mundo na carta",
				  EscutaDeFalas.Any(e => e.Quem == w.Id && e.Texto.Contains(coordenadas) && e.Texto.Contains(mundo)));
		AfirmarBw($"...as {BillsEWhis.FalasDaDerrota.Length} linhas da despedida sairam, na ordem",
				  EscutaDeFalas.Where(e => e.Quem == b.Id || e.Quem == w.Id).Select(e => e.Texto)
					  .SequenceEqual(BillsEWhis.FalasDaDerrota.Select(x => BillsEWhis.Preencher(x.Texto, coordenadas, mundo))),
				  string.Join(" | ", EscutaDeFalas.Select(e => e.Texto.Length > 24 ? e.Texto[..24] : e.Texto)));

		BwPassar(BillsEWhis.SegundosEntreFalas + 3);
		AfirmarBw("acabada a fala, os dois SAIRAM da Terra",
				  !_players.ContainsKey(b.Id) && !_players.ContainsKey(w.Id) && !ZoneList(TerraDaVisita.Hash).Contains(b));
		AfirmarBw("...o mundo inteiro ouviu o endereco, com o nome de quem o derrubou",
				  EscutaDasSagas.Any(t => t.Contains(coordenadas) && t.Contains(pl.Name)), string.Join(" | ", EscutaDasSagas));

		(bool Revelada, int Sx, int Sy, int K, ulong Seed, int Bytes)? depois = BwUltimaCasa(pl.Id);
		AfirmarBw("...e o pacote da casa agora diz REVELADA, com a celula, a orbita e a semente do mundo certo",
				  depois is { Revelada: true } d && d.Sx == casa.Sx && d.Sy == casa.Sy && d.K == casa.K && d.Seed == casa.Corpo.Seed, $"{depois}");

		// ---- quem nao estava la: a noticia, uma vez por personagem ----
		AfirmarBw("quem estava online ja foi marcado como quem ouviu (a noticia de 'quem nao estava la' nao e pra ele)",
				  pl.Ficha.CasaDeBillsOuvida == casa.Corpo.Seed);
		pl.Ficha.CasaDeBillsOuvida = 0;
		_casaVistaPor.Clear();
		EscutaDeBills.Clear();
		EscutaDeAvisos = [];
		BwPassar(1);
		string noticia = BillsEWhis.Preencher(BillsEWhis.NoticiaDaCasa, coordenadas, mundo);
		AfirmarBw("QUEM NAO ESTAVA LA: no primeiro segundo depois de entrar, recebe o pacote da casa e a noticia por escrito",
				  BwUltimaCasa(pl.Id) is { Revelada: true } && EscutaDeAvisos.Contains(noticia), string.Join(" | ", EscutaDeAvisos));
		EscutaDeAvisos.Clear();
		BwPassar(3);
		AfirmarBw("...UMA vez so (a ficha guarda que ele ja ouviu)", !EscutaDeAvisos.Contains(noticia));
		EscutaDeAvisos = null;
		return casa;
	}

	// =====================================================================
	// 4) A CASA -- os dois parados, intocaveis, sob a gravidade deles
	// =====================================================================
	private void BwACasa(ServerPlayer pl, CasaDeBills casa, ZoneKey zona)
	{
		GD.Print("[bills] ---- 4) a casa: parados como um objeto");
		BwPassar(1);
		if (_bills.Corpo is not { } b || _whis.Corpo is not { } w)
		{
			AfirmarBw("com o mundo da casa pronto, os dois nascem la no segundo seguinte", false, $"bills={_bills.Corpo != null} whis={_whis.Corpo != null}");
			return;
		}

		AfirmarBw($"os dois estao NA CASA ({zona}), e nao mais na Terra",
				  b.Zone.Equals(zona) && w.Zone.Equals(zona) && Espaco.ZonaDe(casa.Corpo).Equals(zona));
		AfirmarBw("PARADOS: sem mente, sem presa, intocaveis -- e o Whis ao lado do Bills",
				  _bills.Modo == ModoDivino.Parado && _whis.Modo == ModoDivino.Parado && b.Cerebro == null && w.Cerebro == null
				  && b.Combate.Intocavel && w.Combate.Intocavel && !b.Moving && !w.Moving
				  && (w.Pos - b.Pos).Length <= (BillsEWhis.TilesEntreOsDois + 2.5f) * ZoneCollision.TileSize);
		ZoneCollision? chaoDaCasa = MundoDaZona(zona)?.Colisao;
		int celX = (int)MathF.Floor(b.Pos.X / ZoneCollision.TileSize), celY = (int)MathF.Floor(b.Pos.Y / ZoneCollision.TileSize);
		AfirmarBw("LADO A LADO DE VERDADE: na mesma linha, a dois tiles um do outro, com chao firme nos tres tiles (num mundo de lagos 'o ponto livre mais perto' era a diagonal)",
				  chaoDaCasa != null && Math.Abs(w.Pos.Y - b.Pos.Y) < 0.5f
				  && Math.Abs(w.Pos.X - b.Pos.X - BillsEWhis.TilesEntreOsDois * ZoneCollision.TileSize) < 0.5f
				  && chaoDaCasa.ServeDeChao(celX, celY) && chaoDaCasa.ServeDeChao(celX + 1, celY) && chaoDaCasa.ServeDeChao(celX + 2, celY),
				  $"Bills ({b.Pos.X:0},{b.Pos.Y:0}) Whis ({w.Pos.X:0},{w.Pos.Y:0})");
		AfirmarBw("...longe do tile de pouso (todo mundo que chega cai nele)",
				  MundoDaZona(zona)?.Chegada is { } chegada && (b.Pos - chegada).Length >= 3 * ZoneCollision.TileSize
				  && (w.Pos - chegada).Length >= 3 * ZoneCollision.TileSize);
		AfirmarBw($"a gravidade daqui ({casa.Gravidade:0}x) nao os esmaga: nasceram aclimatados",
				  !Esmagamento.Esmaga(b.Ficha) && !Esmagamento.Esmaga(w.Ficha), $"maestria {b.Ficha.GravMastered:0}x / {w.Ficha.GravMastered:0}x");

		// INTOCAVEL DE VERDADE: o resolvedor de golpes (o de producao) devolve "nada" contra um corpo parado.
		double vida = b.Ficha.HP;
		GolpeResultado r = MeleeResolver.Resolver(pl.Combate, b.Combate, 0, _rng);
		AfirmarBw("um soco de verdade num deles parado nao encosta (o resolvedor de golpes o recusa)",
				  r.Dano == 0 && b.Ficha.HP == vida && r.Desfecho == default);

		// O QUE E DE CHEFE E NAO E DELES: a memoria de chefe visto (a copia na mente) e o embaralho do planeta frio.
		BwLevar(pl, zona, new Vec2(b.Pos.X, b.Pos.Y + 48));
		pl.ChefesVistos.Remove(BillsEWhis.MoldeDoBills);
		pl.ChefesVistos.Remove(BillsEWhis.MoldeDoWhis);
		TickDoRoteiro();
		AfirmarBw("ver os dois NAO os grava na memoria de chefes (nao ha copia deles pra enfrentar na mente)",
				  !pl.ChefesVistos.Contains(BillsEWhis.MoldeDoBills) && !pl.ChefesVistos.Contains(BillsEWhis.MoldeDoWhis));
		AfirmarBw("...e eles nao sao embaralhados quando o planeta esfria (sao de ficha de chefe)",
				  !PasseiaSozinho(b, null) && !PasseiaSozinho(w, null));

		// O RUMO, A QUEM CHEGA: uma vez por chegada (o host acabou de ser posto neste mundo, ao lado deles).
		_cientesDoRumo.Clear();
		EscutaDeAvisos = [];
		BwLevar(pl, zona, new Vec2(b.Pos.X - 20 * ZoneCollision.TileSize, b.Pos.Y));
		BwPassar(1);
		string rumoEsperado = BillsEWhis.RumoDosDois.Replace("{rumo}", RumoDe(pl.Pos, b.Pos));
		AfirmarBw($"QUEM CHEGA ao mundo deles ouve o RUMO (vinte tiles a oeste: \"{rumoEsperado}\")",
				  EscutaDeAvisos.Contains(rumoEsperado) && rumoEsperado.Contains("leste"), string.Join(" | ", EscutaDeAvisos));
		EscutaDeAvisos.Clear();
		BwPassar(3);
		AfirmarBw("...UMA vez por chegada: parado la, nao ouve de novo", !EscutaDeAvisos.Any(a => a.StartsWith("Bills e Whis estão", StringComparison.Ordinal)));
		BwLevar(pl, TerraDaVisita, PontoDeNascimento(TerraDaVisita));
		BwPassar(1);
		BwLevar(pl, zona, new Vec2(b.Pos.X, b.Pos.Y + ZoneCollision.TileSize));
		EscutaDeAvisos.Clear();
		BwPassar(1);
		AfirmarBw("...e quem SAI do mundo e volta ouve de novo (agora \"bem do seu lado\")",
				  EscutaDeAvisos.Any(a => a.StartsWith("Bills e Whis estão", StringComparison.Ordinal) && a.Contains("do seu lado")), string.Join(" | ", EscutaDeAvisos));
		SemRumoAoChegarDeTeste = true;
		try
		{
			_cientesDoRumo.Clear();
			EscutaDeAvisos.Clear();
			BwPassar(1);
			AfirmarBw("(defeito injetado: ninguem ouve o rumo) quem chega nao e avisado de pra que lado eles estao -- e as afirmacoes de cima reprovam",
					  !EscutaDeAvisos.Any(a => a.StartsWith("Bills e Whis estão", StringComparison.Ordinal)));
		}
		finally { SemRumoAoChegarDeTeste = false; }
		EscutaDeAvisos = null;

		// ELES RECEBEM A VISITA: cada um fala sozinho de vez em quando, com alguem por perto.
		EscutaDeFalas!.Clear();
		_whis.ProximaRecepcao = _bills.ProximaRecepcao = 0;
		BwPassar(1);
		AfirmarBw("com visita por perto o Whis recebe (uma das falas dele) e o Bills ronca (um emote)",
				  EscutaDeFalas.Any(e => e.Quem == w.Id && BillsEWhis.WhisRecebe.Contains(e.Texto))
				  && EscutaDeFalas.Any(e => e.Quem == b.Id && e.Canal == Protocol.Fala.Emote && e.Texto == BillsEWhis.BillsDorme),
				  string.Join(" | ", EscutaDeFalas.Select(e => $"{e.Canal}:{e.Texto}")));
		EscutaDeFalas.Clear();
		BwPassar(3);
		AfirmarBw("...e nao repetem a cada segundo (ha um intervalo)", EscutaDeFalas.Count == 0, $"{EscutaDeFalas.Count}");
	}

	// =====================================================================
	// 5b) A VISITA QUE CHEGA COM O TRONO JA OCUPADO -- ninguem anuncia um Bills que nao existe
	// =====================================================================
	private void BwAVisitaSemOBills(ServerPlayer pl)
	{
		GD.Print("[bills] ---- 5b) o dia da visita com o titulo ja na mao de um jogador");
		EstadoDaVisita guardada = _visita;
		ZerarBills();
		_tronos[BillsEWhis.CargoDoBills] = "conta_de_um_deus";
		try
		{
			_visita = new EstadoDaVisita { Fase = FaseDaVisita.ACaminho, DiaDaChegada = DiaDoMundo };
			EscutaDasSagas!.Clear();
			EscutaDeFalas!.Clear();
			BwPassar(1);
			AfirmarBw("chegado o dia com o titulo OCUPADO (so um admin consegue), o mundo NAO ouve 'Bills pousou na Terra' -- ele nao existe",
					  !EscutaDasSagas.Contains(BillsEWhis.AvisoDaChegada) && _bills.Corpo == null, string.Join(" | ", EscutaDasSagas));
			AfirmarBw("...a visita se encerra sem luta, o Whis (que existe) conta onde fica a casa, e a historia vai pra 'em casa'",
					  _visita.Fase == FaseDaVisita.EmCasa && _whis.Corpo is { } w
					  && EscutaDeFalas.Any(e => e.Quem == w.Id && e.Texto.Contains("Sistema", StringComparison.Ordinal)),
					  $"fase {_visita.Fase}, {EscutaDeFalas.Count} fala(s)");
			BwPassar(BillsEWhis.SegundosEntreFalas + 2);
			AfirmarBw("...e o endereco e anunciado ao mundo do mesmo jeito (a casa abre com o Whis)",
					  EscutaDasSagas.Any(t => t.Contains("A visita de Bills à Terra terminou", StringComparison.Ordinal)), string.Join(" | ", EscutaDasSagas));
		}
		finally
		{
			_tronos.Remove(BillsEWhis.CargoDoBills);
			ZerarBills();
			_visita = guardada;
			SalvarBills();
		}
		BwPassar(1);
		AfirmarBw("(montagem) de volta: os dois em casa, parados", _bills.Corpo != null && _whis.Corpo != null && _visita.Fase == FaseDaVisita.EmCasa);
	}

	// =====================================================================
	// 4b) A CASA DESTRUIDA -- eles se mudam pra seguinte da lista, e o mundo fica sabendo
	// =====================================================================
	private void BwACasaDestruida(ServerPlayer pl, CasaDeBills casa, ZoneKey zona)
	{
		GD.Print("[bills] ---- 4b) a casa destruida: eles se mudam");
		IReadOnlyList<CasaDeBills> casas = BillsEWhis.Casas(SeedDoUniverso);
		if (casas.Count < 2) { AfirmarBw("(montagem) ha uma segunda casa na lista deste universo", false); return; }
		CasaDeBills segunda = casas[1];
		ChaveDePlaneta chave = ChaveDePlaneta.De(casa.Corpo);

		pl.Ficha.CasaDeBillsOuvida = casa.Corpo.Seed;
		EscutaDasSagas!.Clear();
		EscutaDeBills!.Clear();
		EscutaDeAvisos = [];
		_mortos.Por(new EstadoDaMorte { Chave = chave.Texto, Nome = casa.Corpo.Nome, Fase = FaseDaMorte.Destruido, Motivo = "bancada do Bills" });
		try
		{
			BwPassar(1);
			AfirmarBw("A CASA FOI DESTRUIDA: os dois saem dela (a casa passa a ser a seguinte da lista, que ainda esta nascendo)",
					  CasaAtual()?.Corpo.Seed == segunda.Corpo.Seed && (_bills.Corpo == null || !_bills.Corpo.Zone.Equals(zona)));

			ZoneKey? nova = MundoDaCasaProntoDeTeste(segunda.Corpo);
			BwPassar(1);
			AfirmarBw($"...e reaparecem na SEGUNDA casa ({segunda.Corpo.Nome}, {segunda.Gravidade:0}x), parados e lado a lado",
					  nova is { } z && _bills.Corpo is { } b2 && _whis.Corpo is { } w2 && b2.Zone.Equals(z) && w2.Zone.Equals(z)
					  && _bills.Modo == ModoDivino.Parado && Math.Abs(w2.Pos.Y - b2.Pos.Y) < 0.5f);
			string novoEndereco = BillsEWhis.Coordenadas(segunda);
			AfirmarBw($"...o MUNDO ouve a mudanca, com o endereco novo ({novoEndereco})",
					  EscutaDasSagas.Any(t => t.Contains(novoEndereco) && t.Contains("se mudou")), string.Join(" | ", EscutaDasSagas));
			AfirmarBw("...o pacote da casa leva o endereco novo a quem esta online (a carta estelar troca a marca de lugar)",
					  BwUltimaCasa(pl.Id) is { Revelada: true } d && d.Seed == segunda.Corpo.Seed && d.Sx == segunda.Sx && d.Sy == segunda.Sy && d.K == segunda.K,
					  $"{BwUltimaCasa(pl.Id)}");
			AfirmarBw("...e quem ouviu o anuncio NAO recebe, por cima, a noticia de 'quem nao estava la'",
					  pl.Ficha.CasaDeBillsOuvida == segunda.Corpo.Seed && !EscutaDeAvisos.Any(a => a.StartsWith("Corre a notícia", StringComparison.Ordinal)),
					  string.Join(" | ", EscutaDeAvisos));
		}
		finally
		{
			_mortos.Tirar(chave);
			EscutaDeAvisos = null;
		}

		// O PLANETA VOLTA (um admin o restaurou): a casa e funcao pura do que existe, e eles voltam pra primeira.
		BwPassar(1);
		BwPassar(1);
		AfirmarBw("restaurado o planeta, a casa volta a ser a primeira -- e eles voltam pra la",
				  CasaAtual()?.Corpo.Seed == casa.Corpo.Seed && _bills.Corpo is { } b3 && b3.Zone.Equals(zona) && _whis.Corpo is { } w3 && w3.Zone.Equals(zona));
	}

	// =====================================================================
	// 5) EXISTE <=> CARGO VAGO
	// =====================================================================
	private void BwQuemExiste(ServerPlayer pl)
	{
		GD.Print("[bills] ---- 5) existe <=> cargo vago (cada um pelo cargo dele)");
		AfirmarBw("com os dois tronos vagos, os dois existem", _bills.Corpo != null && _whis.Corpo != null);

		_tronos[BillsEWhis.CargoDoWhis] = "conta_de_um_anjo";
		BwPassar(1);
		AfirmarBw("um jogador vira ANJO: o Whis deixa de existir -- e o Bills fica",
				  _whis.Corpo == null && _bills.Corpo != null && !_players.Values.Any(p => p.Race == BillsEWhis.RacaDoWhis));
		_tronos.Remove(BillsEWhis.CargoDoWhis);
		BwPassar(1);
		AfirmarBw("o cargo de Anjo VAGA de novo: o Whis volta (funcao pura do trono, sem estado a mais)", _whis.Corpo != null);

		_tronos[BillsEWhis.CargoDoBills] = "conta_de_um_deus";
		BwPassar(1);
		AfirmarBw("um jogador vira DEUS DA DESTRUICAO: o Bills deixa de existir -- e o Whis fica",
				  _bills.Corpo == null && _whis.Corpo != null && !_players.Values.Any(p => p.Race == BillsEWhis.RacaDoBills));

		BillsEWhis.ExisteSempreDeTeste = true;
		try
		{
			BwPassar(1);
			AfirmarBw("(defeito injetado: o corpo nao olha o trono) com o titulo na mao de um jogador o Bills continua no mundo",
					  _bills.Corpo != null);
		}
		finally { BillsEWhis.ExisteSempreDeTeste = false; }
		BwPassar(1);
		AfirmarBw("(sem o defeito) ele sai de novo", _bills.Corpo == null);

		_tronos.Remove(BillsEWhis.CargoDoBills);
		BwPassar(1);
		AfirmarBw("o titulo VAGA de novo: o Bills volta pra casa", _bills.Corpo is { } b && b.Zone.Equals(_whis.Corpo!.Zone));
	}

	// =====================================================================
	// 6) O MENU -- o servidor aponta, e confere cada pedido
	// =====================================================================
	private void BwOMenu(ServerPlayer pl, ZoneKey zona, List<ServerPlayer> forjados)
	{
		GD.Print("[bills] ---- 6) o menu da tecla E: o que o servidor aponta e o que ele recusa");
		if (_bills.Corpo is not { } b || _whis.Corpo is not { } w) { AfirmarBw("(montagem) os dois estao em casa", false); return; }
		pl.Ficha.GravMastered = 500;
		AplicarGravidade(pl);

		// ---- a tabela do menu ----
		AfirmarBw("o menu do Whis tem UMA opcao (treinar) e o do Bills tem DUAS (treinar e desafiar pelo titulo)",
				  Interacoes.De(BillsEWhis.TipoWhis).Select(a => a.Verbo).SequenceEqual([BillsEWhis.VerboTreinarComWhis])
				  && Interacoes.De(BillsEWhis.TipoBills).Select(a => a.Verbo).SequenceEqual([BillsEWhis.VerboTreinarComBills, BillsEWhis.VerboDesafiar]),
				  string.Join(", ", Interacoes.De(BillsEWhis.TipoBills).Select(a => a.Rotulo)));
		AfirmarBw("...e os quatro verbos cabem no canal de verbs (24 letras)",
				  ((string[])[BillsEWhis.VerboTreinarComWhis, BillsEWhis.VerboTreinarComBills, BillsEWhis.VerboDesafiar, BillsEWhis.VerboParar]).All(v => v.Length <= 24));

		// ---- o que o servidor APONTA ----
		BwLevar(pl, zona, new Vec2(w.Pos.X, w.Pos.Y + 40));
		EscutaDeBills!.Clear();
		_divinoApontado.Clear();
		BwPassar(0.3);
		AfirmarBw("ao lado do Whis, o servidor aponta o corpo DELE com o menu do Whis",
				  BwUltimoApontado(pl.Id) is { } ap && ap.Corpo == w.Id && ap.Tipo == BillsEWhis.TipoWhis && ap.Nome == "Whis", $"{BwUltimoApontado(pl.Id)}");
		BwLevar(pl, zona, new Vec2(b.Pos.X, b.Pos.Y + 40));
		BwPassar(0.3);
		AfirmarBw("ao lado do Bills, aponta o do Bills", BwUltimoApontado(pl.Id) is { } ap2 && ap2.Corpo == b.Id && ap2.Tipo == BillsEWhis.TipoBills);
		BwLevar(pl, zona, new Vec2(b.Pos.X, b.Pos.Y + 10 * ZoneCollision.TileSize));
		BwPassar(0.3);
		AfirmarBw("longe dos dois, o apontado e APAGADO (id zero)", BwUltimoApontado(pl.Id) is { Corpo: 0 });
		int pacotes = EscutaDeBills.Count;
		BwPassar(2);
		AfirmarBw("...e parado nao sai pacote nenhum (so quando a resposta muda)", EscutaDeBills.Count == pacotes, $"{EscutaDeBills.Count - pacotes} a mais");

		// A SESSAO QUE COMECA LONGE DOS DOIS: o cliente atravessa o relog com o apontado velho na memoria.
		_divinoApontado.Clear();
		pacotes = EscutaDeBills.Count;
		BwPassar(0.5);
		AfirmarBw("numa sessao nova, longe dos dois, sai UM 'nenhum' (e o que apaga o apontado velho do cliente) -- e so um",
				  EscutaDeBills.Count == pacotes + 1 && BwUltimoApontado(pl.Id) is { Corpo: 0 }, $"{EscutaDeBills.Count - pacotes} pacote(s)");

		// ---- os pedidos RECUSADOS (cada um com a frase, e nenhum abre luta) ----
		List<string> Pedir(string verbo) => Ouvir(() => ComandoDeInteracao(pl, verbo, ""));
		bool SemLuta() => _whis.Sessao == null && _bills.Sessao == null && _whis.Modo == ModoDivino.Parado && _bills.Modo == ModoDivino.Parado;

		List<string> longe = Pedir(BillsEWhis.VerboTreinarComWhis);
		AfirmarBw("LONGE (dez tiles): o pedido de treino e recusado, com a frase",
				  SemLuta() && Disse(longe, "longe demais"), Ultimos(longe));

		BillsEWhis.MenuSemValidacaoDeTeste = true;
		try
		{
			Pedir(BillsEWhis.VerboTreinarComWhis);
			AfirmarBw("(defeito injetado: o servidor nao confere o pedido) o MESMO pedido de longe abre a luta", _whis.Sessao != null);
		}
		finally
		{
			BillsEWhis.MenuSemValidacaoDeTeste = false;
			if (_whis.Sessao != null) EncerrarASessao(_whis, FimDaSessao.ParceiroParou);
		}

		BwLevar(pl, TerraDaVisita, PontoDeNascimento(TerraDaVisita));
		List<string> outroMundo = Pedir(BillsEWhis.VerboTreinarComBills);
		AfirmarBw("NOUTRO PLANETA (o pedido forjado da Terra): recusado -- 'nao esta neste mundo'",
				  SemLuta() && Disse(outroMundo, "não está neste mundo"), Ultimos(outroMundo));

		BwLevar(pl, zona, new Vec2(w.Pos.X, w.Pos.Y + 40));
		pl.Combate.Nocautear(60);
		List<string> caido = Pedir(BillsEWhis.VerboTreinarComWhis);
		AfirmarBw("CAIDO (nocauteado ao lado dele): recusado", SemLuta() && Disse(caido, "caído"), Ultimos(caido));
		pl.Combate.Levantar();
		pl.Combate.Curar();

		BwLevar(pl, zona, new Vec2(w.Pos.X + (w.Pos.X >= b.Pos.X ? 48 : -48), w.Pos.Y));
		List<string> errado = Pedir(BillsEWhis.VerboDesafiar);
		AfirmarBw("FORJADO: 'desafiar' mandado de quem esta ao alcance do WHIS e fora do alcance do Bills e recusado -- o alvo do verbo e o Bills",
				  SemLuta() && AoAlcanceDaTeclaE(pl, w) && !AoAlcanceDaTeclaE(pl, b) && Disse(errado, "Bills está longe demais"), Ultimos(errado));
		BwLevar(pl, zona, new Vec2(w.Pos.X, w.Pos.Y + 40));

		List<string> semLuta = Pedir(BillsEWhis.VerboParar);
		AfirmarBw("FORJADO: 'parar' sem luta nenhuma so devolve a frase", SemLuta() && Disse(semLuta, "não está em luta"), Ultimos(semLuta));

		pl.Ficha.GravMastered = 1;
		AplicarGravidade(pl);
		List<string> esmagado = Pedir(BillsEWhis.VerboTreinarComWhis);
		AfirmarBw("ESMAGADO (a gravidade daqui prende quem nao a aguenta): recusado -- nao ha como lutar sem se mexer",
				  SemLuta() && Disse(esmagado, "gravidade"), Ultimos(esmagado));
		pl.Ficha.GravMastered = 500;
		AplicarGravidade(pl);

		AfirmarBw("verbo que nao e deste sistema nao e engolido por ele", !ComandoDeBills(pl, "abrir_tech", ""));
	}

	// =====================================================================
	// 7) O TREINO COM O WHIS -- o triplo, o Instinto que nao cansa, e nada de efeito visual
	// =====================================================================
	private void BwOTreinoComOWhis(ServerPlayer pl, ZoneKey zona, List<ServerPlayer> forjados)
	{
		GD.Print("[bills] ---- 7) o treino com o Whis");
		if (_bills.Corpo is not { } b || _whis.Corpo is not { } w) { AfirmarBw("(montagem) os dois estao em casa", false); return; }

		pl.Ficha.BP = 4e9;
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
		pl.Combate.Letal = true;
		BwLevar(pl, zona, new Vec2(w.Pos.X, w.Pos.Y + 40));

		EscutaDeFalas!.Clear();
		EscutaDeAnuncios = [];
		EscutaDeEfeitos = [];
		List<string> ouviu = Ouvir(() => ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComWhis, ""));
		AfirmarBw("o pedido de perto, de pe e no lugar certo ABRE o treino (pelo despacho de verbs de producao)",
				  _whis.Sessao is { Desafio: false, Lutando: true } s && ReferenceEquals(s.Parceiro, pl) && _whis.Modo == ModoDivino.NaSessao, Ultimos(ouviu));
		if (_whis.Sessao == null) return;

		AfirmarBw("...o Whis ganhou mente, a presa dele e SO quem pediu, e ele deixou de ser intocavel",
				  w.Cerebro != null && PresaDoNpc(w)?.Id == pl.Id && !w.Combate.Intocavel);
		AfirmarBw("...ele fala ao comecar, e o Bills ao lado continua parado e intocavel",
				  EscutaDeFalas.Any(e => e.Quem == w.Id && e.Texto == BillsEWhis.WhisComecaOTreino) && _bills.Modo == ModoDivino.Parado && b.Combate.Intocavel);
		AfirmarBw("...e o golpe LETAL de quem treina foi desligado (treino nao mata nem aleija)", !pl.Combate.Letal);

		// ---- o triplo, e ele ACOMPANHA ----
		BwPassar(0.3);
		double razao = w.Ficha.expressedBP / pl.Ficha.expressedBP;
		AfirmarBw("o poder EXPRESSO do Whis e o TRIPLO do poder expresso de quem o chamou",
				  Math.Abs(razao - 3) <= 0.03, $"{razao:0.000}x (Whis {w.Ficha.expressedBP:N0} / jogador {pl.Ficha.expressedBP:N0})");

		pl.Ficha.BP *= 7;                       // o jogador "se transforma": o expresso dele sobe sete vezes
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
		BwPassar(0.3);
		razao = w.Ficha.expressedBP / pl.Ficha.expressedBP;
		AfirmarBw("o jogador fica sete vezes mais forte no meio da luta: o Whis ACOMPANHA, e continua com o triplo",
				  Math.Abs(razao - 3) <= 0.03, $"{razao:0.000}x");

		foreach (BodyPart p in w.Combate.Corpo.Partes) if (p.Papel == Vitalidade.Membro) p.Vida = p.VidaMax * 0.6;
		w.Combate.SincronizarVida();
		w.Ficha.PowerLevel();
		BwPassar(0.3);
		razao = w.Ficha.expressedBP / pl.Ficha.expressedBP;
		AfirmarBw("FERIDO, o Whis continua com o triplo (o pino mira o poder expresso, e nao um base que a ferida derruba)",
				  Math.Abs(razao - 3) <= 0.05, $"{razao:0.000}x com HP {w.Ficha.HP:0}");

		BillsEWhis.TreinoSemMultiploDeTeste = true;
		try
		{
			BwPassar(0.3);
			AfirmarBw("(defeito injetado: sem o multiplo) o Whis fica com o MESMO poder do jogador -- e a afirmacao do triplo reprova",
					  Math.Abs(w.Ficha.expressedBP / pl.Ficha.expressedBP - 3) > 0.5, $"{w.Ficha.expressedBP / pl.Ficha.expressedBP:0.000}x");
		}
		finally { BillsEWhis.TreinoSemMultiploDeTeste = false; }
		BwPassar(0.3);

		// ---- o Instinto: ligado, a 100%, e SEM QUEDA ----
		AfirmarBw("o Ultra Instinto do Whis: aprendido, maestria 100, precisao 100, a passiva LIGADA -- e a esquiva no teto dela",
				  w.UltraInstinct is { Aprendida: true, Ligada: true, Real: 100, Atual: 100 }
				  && Math.Abs(w.Combate.ChanceEsquiva - Disciplinas.ChanceDeEsquiva(100, contraKi: false)) < 1e-9,
				  $"precisao {w.UltraInstinct.Atual:0.0}, esquiva {w.Combate.ChanceEsquiva:0.0}%");

		for (int i = 0; i < 30 * 120; i++) { w.Combate.EntrarEmCombate(); TickDasDisciplinas(w, Protocol.TickSeconds); TickDeBills(Protocol.TickSeconds); _whis.Sessao!.Acaba += 1; }
		AfirmarBw("dois minutos de luta com a passiva ligada: a precisao dele NAO CAIU (a de um jogador teria ido a 40)",
				  w.UltraInstinct.Atual == 100 && Math.Abs(w.Combate.ChanceEsquiva - 45) < 1e-9 && _whis.Sessao != null,
				  $"precisao {w.UltraInstinct.Atual:0.00}, esquiva {w.Combate.ChanceEsquiva:0.00}%");

		BillsEWhis.InstintoQueCansaDeTeste = true;
		try
		{
			for (int i = 0; i < 30 * 60; i++) { w.Combate.EntrarEmCombate(); TickDasDisciplinas(w, Protocol.TickSeconds); TickDeBills(Protocol.TickSeconds); _whis.Sessao!.Acaba += 1; }
			AfirmarBw("(defeito injetado: o Instinto dele cansa como o de um jogador) em um minuto a precisao cai -- e a afirmacao de cima reprova",
					  w.UltraInstinct.Atual < 80, $"precisao {w.UltraInstinct.Atual:0.0}");
		}
		finally { BillsEWhis.InstintoQueCansaDeTeste = false; }
		BwPassar(0.1);
		AfirmarBw("(sem o defeito) a precisao volta ao cheio no tique seguinte", w.UltraInstinct.Atual == 100);

		// ---- a esquiva ACONTECE, pelo resolvedor de producao -- e ele nao fica sem folego pra ela ----
		int esquivas = 0, acertos = 0;
		var sorte = new Random(20261011);
		const int golpes = 12_000;
		for (int i = 0; i < golpes; i++)
		{
			GolpeResultado g = MeleeResolver.Resolver(pl.Combate, w.Combate, 0, sorte);
			if (g.EsquivaAtiva) esquivas++;
			else if (g.Desfecho != Desfecho.Esquivou) acertos++;
			if (w.Ficha.KO) w.Combate.Levantar();
			w.Combate.Curar();
			w.Combate.Bloqueando = false;
			pl.Combate.Stun = 0;
			pl.Combate.Recarga = 0;
			if (i % 4 == 0) { _whis.Sessao!.Acaba += 60; TickDeBills(Protocol.TickSeconds); }
		}
		TickDeBills(Protocol.TickSeconds);
		double taxa = esquivas / (double)Math.Max(esquivas + acertos, 1);
		AfirmarBw($"em {golpes} golpes de verdade, o Instinto desvia de ~45% dos que iam acertar (e nunca falta Ki pra desviar)",
				  esquivas + acertos > 300 && Math.Abs(taxa - 0.45) <= 0.06 && w.Ficha.Ki >= w.Ficha.MaxKi * 0.9,
				  $"{esquivas} esquivas do instinto em {esquivas + acertos} golpes que chegariam = {taxa:P1}; Ki {w.Ficha.Ki / Math.Max(w.Ficha.MaxKi, 1):P0}");

		// ---- nenhum efeito visual: sem forma, sem anuncio de forma, sem pacote de efeito ----
		AfirmarBw("NENHUM efeito visual: o Whis nao entrou em forma nenhuma, e nao saiu anuncio de forma nem pacote de efeito dele",
				  w.Forma.Atual == Catalogo.IdDoPiso(Perfil(w)) && !EscutaDeAnuncios.Any(e => e.Quem == w.Id) && !EscutaDeEfeitos.Any(e => e.Quem == w.Id),
				  $"forma {w.Forma.Atual}, {EscutaDeAnuncios.Count(e => e.Quem == w.Id)} anuncio(s), {EscutaDeEfeitos.Count(e => e.Quem == w.Id)} efeito(s)");

		// ---- cair no treino NAO e derrota ----
		double bpDaLuta = pl.Ficha.BP;
		pl.Ficha.BP = 1000;                     // abaixo do teto do Zenkai (um Saiyajin forte demais ja nao o recebe de ninguem)
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
		BwPassar(0.3);
		pl.Ficha.zenkaiReady = 0;
		double bpAntes = pl.Ficha.BP;
		AoPerderALuta(pl, w, morreu: false);
		AfirmarBw("cair no treino NAO e derrota: nenhum Zenkai (o adversario tem sempre o triplo -- seria uma fabrica)",
				  pl.Ficha.BP == bpAntes, $"{bpAntes:N0} -> {pl.Ficha.BP:N0}");
		BillsEWhis.TreinoEhDerrotaDeTeste = true;
		try
		{
			pl.Ficha.zenkaiReady = 0;
			AoPerderALuta(pl, w, morreu: false);
			AfirmarBw($"(defeito injetado: o treino conta como derrota) o Saiyajin ganha Zenkai por apanhar do Whis ({pl.Race})",
					  pl.Ficha.BP > bpAntes || !pl.Ficha.HasZenkai(), $"{bpAntes:N0} -> {pl.Ficha.BP:N0}");
		}
		finally { BillsEWhis.TreinoEhDerrotaDeTeste = false; }
		pl.Ficha.BP = bpDaLuta;
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
		BwPassar(0.3);

		// ---- ocupado: um de cada vez -- mas o Bills esta livre ----
		ServerPlayer outro = BwForjar("Segundo", "Human", zona, new Vec2(w.Pos.X + 20, w.Pos.Y + 40), 3e9, forjados);
		outro.Ficha.GravMastered = 500;
		AplicarGravidade(outro);
		EscutaDeFalas.Clear();
		outro.Pos = new Vec2(w.Pos.X + 20, w.Pos.Y + 40);
		List<string> ocupado = Ouvir(() => ComandoDeInteracao(outro, BillsEWhis.VerboTreinarComWhis, ""));
		AfirmarBw("um SEGUNDO jogador pede o Whis no meio da aula: recusado ('um de cada vez'), e a aula continua",
				  ReferenceEquals(_whis.Sessao?.Parceiro, pl) && Disse(ocupado, "ocupado") && EscutaDeFalas.Any(e => e.Quem == w.Id && e.Texto == BillsEWhis.WhisOcupado),
				  Ultimos(ocupado));
		outro.Pos = new Vec2(b.Pos.X, b.Pos.Y + 40);
		ComandoDeInteracao(outro, BillsEWhis.VerboTreinarComBills, "");
		AfirmarBw("...mas o BILLS esta livre: o segundo treina com ele ao mesmo tempo (os dois mestres sao independentes)",
				  ReferenceEquals(_bills.Sessao?.Parceiro, outro) && ReferenceEquals(_whis.Sessao?.Parceiro, pl));
		EncerrarASessao(_bills, FimDaSessao.ParceiroParou);

		// ---- o menu de quem esta treinando e a SAIDA ----
		pl.Pos = new Vec2(w.Pos.X, w.Pos.Y + 40);
		_divinoApontado.Clear();
		EscutaDeBills!.Clear();
		BwPassar(0.3);
		AfirmarBw("pra quem esta treinando, o menu apontado e a SAIDA ('Parar o treino')",
				  BwUltimoApontado(pl.Id) is { } ap && ap.Corpo == w.Id && ap.Tipo == BillsEWhis.TipoEmTreino
				  && Interacoes.De(BillsEWhis.TipoEmTreino).Select(a => a.Verbo).SequenceEqual([BillsEWhis.VerboParar]), $"{BwUltimoApontado(pl.Id)}");

		// A SAIDA VALE A QUALQUER DISTANCIA (o mestre tambem luta de longe): o aluno a dez tiles dele, ao lado do Bills.
		pl.Pos = new Vec2(b.Pos.X, b.Pos.Y + 10 * ZoneCollision.TileSize);
		_divinoApontado.Clear();
		BwPassar(0.3);
		AfirmarBw("A SAIDA E APONTADA A QUALQUER DISTANCIA: a dez tiles do Whis (fora do alcance da tecla E), quem treina continua com o 'Parar o treino' na mao",
				  BwUltimoApontado(pl.Id) is { } longeDele && longeDele.Corpo == w.Id && longeDele.Tipo == BillsEWhis.TipoEmTreino, $"{BwUltimoApontado(pl.Id)}");
		pl.Pos = new Vec2(b.Pos.X, b.Pos.Y + 40);
		_divinoApontado.Clear();
		BwPassar(0.3);
		AfirmarBw("...e ela ganha do convite do OUTRO: encostado no Bills (parado), quem treina com o Whis ve a saida do treino, e nao o menu do Bills",
				  BwUltimoApontado(pl.Id) is { } aoLadoDoBills && aoLadoDoBills.Corpo == w.Id && aoLadoDoBills.Tipo == BillsEWhis.TipoEmTreino, $"{BwUltimoApontado(pl.Id)}");
		SaidaSoDePertoDeTeste = true;
		try
		{
			_divinoApontado.Clear();
			BwPassar(0.3);
			AfirmarBw("(defeito injetado: a saida so de perto) encostado no Bills, o apontado vira o menu do BILLS -- e quem quer parar a aula nao tem como (foi o que a `--diagbills` fotografou)",
					  BwUltimoApontado(pl.Id) is { } errado && errado.Corpo == b.Id && errado.Tipo == BillsEWhis.TipoBills, $"{BwUltimoApontado(pl.Id)}");
		}
		finally { SaidaSoDePertoDeTeste = false; }
		outro.Pos = new Vec2(w.Pos.X + 20, w.Pos.Y + 40);
		_divinoApontado.Clear();
		BwPassar(0.3);
		AfirmarBw("...e pro SEGUNDO jogador, encostado no Whis que da aula a outro, nao ha menu nenhum dele",
				  ((Func<bool>)(() =>
				  {
					  (int Corpo, string Tipo, string Nome) doSegundo = DivinoAoAlcance(outro);
					  return doSegundo.Corpo != w.Id;
				  }))());
		pl.Pos = new Vec2(w.Pos.X, w.Pos.Y + 40);

		// ---- o terceiro que se mete ----
		EscutaDeFalas.Clear();
		MarcarAgressao(w, outro);
		BwPassar(0.1);
		AfirmarBw("um TERCEIRO acerta o Whis: a aula ACABA sem resultado, e ele volta ao posto parado, inteiro e intocavel",
				  _whis.Sessao == null && _whis.Modo == ModoDivino.Parado && w.Cerebro == null && w.Combate.Intocavel
				  && (w.Pos - _whis.Posto).Length < 1 && w.Ficha.HP >= 99.9 && EscutaDeFalas.Any(e => e.Quem == w.Id && e.Texto == BillsEWhis.WhisIntrometido));
		AfirmarBw("...o golpe letal de quem treinava VOLTOU como estava, e a passiva do Whis desligou", pl.Combate.Letal && !w.UltraInstinct.Ligada);
		AfirmarBw("...e o poder do Whis voltou ao de repouso (nao ficou com o triplo de ninguem)", Math.Abs(w.Ficha.BP - BillsEWhis.PoderTotal) < 1);

		// ---- o Whis com a FORMA do Instinto seria o efeito visual que um Anjo nao tem ----
		BillsEWhis.InstintoComFormaDeTeste = true;
		try
		{
			EscutaDeAnuncios.Clear();
			string formaDoInstinto = FormaMaisAltaDaDisciplina(TipoDeDisciplina.UltraInstinct);
			ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComWhis, "");
			AfirmarBw($"(defeito injetado: o Whis entra na forma do Instinto, `{formaDoInstinto}`) ele fica NA FORMA e sai anuncio de forma dele -- a afirmacao do 'nenhum efeito visual' reprova",
					  Catalogo.Def(formaDoInstinto) != null && w.Forma.Atual == formaDoInstinto
					  && EscutaDeAnuncios.Any(e => e.Quem == w.Id && e.Para == formaDoInstinto),
					  $"forma '{w.Forma.Atual}', {EscutaDeAnuncios.Count} anuncio(s)");
		}
		finally
		{
			BillsEWhis.InstintoComFormaDeTeste = false;
			if (_whis.Sessao != null) EncerrarASessao(_whis, FimDaSessao.ParceiroParou);
		}
		AfirmarBw("(sem o defeito) ao fim da aula ele esta sem forma nenhuma", w.Forma.Atual == Catalogo.IdDoPiso(Perfil(w)));

		// ---- os outros fins: o aluno cai, o aluno para, o aluno foge, o tempo acaba ----
		pl.Combate.Letal = false;
		ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComWhis, "");
		pl.Combate.Nocautear(60);
		EscutaDeFalas.Clear();
		BwPassar(0.1);
		AfirmarBw("o aluno CAI: a aula acaba, o Whis comenta, e o aluno e posto de pe (ninguem fica no chao sob esta gravidade)",
				  _whis.Sessao == null && !pl.Ficha.KO && EscutaDeFalas.Any(e => e.Quem == w.Id && e.Texto == BillsEWhis.WhisDerrubouOAluno));
		AfirmarBw("...e sai com FOLEGO: por uns segundos ninguem o atinge (a rajada que o mestre ja tinha lancado nao o derruba de novo, fora do treino)",
				  pl.Combate.Intocavel && pl.Combate.Carencia >= BillsEWhis.SegundosDeFolegoAoFim - 0.01, $"carencia {pl.Combate.Carencia:0.0} s");
		pl.Combate.Carencia = 0;
		BillsEWhis.FimSemFolegoDeTeste = true;
		try
		{
			ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComWhis, "");
			pl.Combate.Nocautear(60);
			BwPassar(0.1);
			AfirmarBw("(defeito injetado: sem folego ao fim) o aluno e posto de pe SEM protecao nenhuma -- o golpe que vinha atras o pegaria",
					  _whis.Sessao == null && !pl.Ficha.KO && !pl.Combate.Intocavel);
		}
		finally { BillsEWhis.FimSemFolegoDeTeste = false; }

		ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComWhis, "");
		ComandoDeInteracao(pl, BillsEWhis.VerboParar, "");
		AfirmarBw("o aluno PARA pelo menu: a aula acaba na hora", _whis.Sessao == null && _whis.Modo == ModoDivino.Parado);

		ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComWhis, "");
		pl.Pos = new Vec2(_whis.Posto.X + (BillsEWhis.TilesDoTreino + 3) * ZoneCollision.TileSize, _whis.Posto.Y);
		BwPassar(0.1);
		AfirmarBw("o aluno se AFASTA mais de quarenta tiles: a aula acaba", _whis.Sessao == null);
		pl.Pos = new Vec2(w.Pos.X, w.Pos.Y + 40);

		ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComWhis, "");
		BwPassar(1);
		bool aindaAntes = _whis.Sessao != null;
		_relogioDivino += BillsEWhis.SegundosDeTreino;
		BwPassar(0.1);
		AfirmarBw("o TEMPO acaba (cinco minutos): a aula termina sozinha, pra a vez passar", aindaAntes && _whis.Sessao == null);

		w.Combate.Curar();
		ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComWhis, "");
		BwPassar(0.1);
		EscutaDeFalas.Clear();
		w.Combate.Nocautear(30);
		BwPassar(0.1);
		AfirmarBw("o WHIS cai (quase impossivel, mas a regra existe): a aula acaba com o elogio dele, e ele se levanta inteiro",
				  _whis.Sessao == null && !w.Ficha.KO && w.Ficha.HP >= 99.9 && EscutaDeFalas.Any(e => e.Quem == w.Id && e.Texto == BillsEWhis.WhisFoiDerrubado));
	}

	// =====================================================================
	// 8) A MAESTRIA -- treinar com o mestre certo acelera a disciplina certa
	// =====================================================================
	private void BwAMaestria(ServerPlayer pl, ZoneKey zona)
	{
		GD.Print("[bills] ---- 8) a maestria: o treino acelera a disciplina de quem ja a tem");
		if (_bills.Corpo is not { } b || _whis.Corpo is not { } w) { AfirmarBw("(montagem) os dois estao em casa", false); return; }

		// QUANTO A MAESTRIA REAL SOBE EM DEZ SEGUNDOS DE LUTA com a passiva ligada -- pelo tique de producao das disciplinas.
		double Ganho(TipoDeDisciplina tipo)
		{
			EstadoDeDisciplina est = EstadoDe(pl, tipo), outra = EstadoDe(pl, Disciplinas.Oposta(tipo));
			outra.Aprendida = outra.Ligada = false;
			est.Aprendida = est.Ligada = true;
			est.Real = est.Atual = 10;
			pl.Disciplina = tipo;
			for (int i = 0; i < 300; i++) { pl.Combate.EntrarEmCombate(); TickDasDisciplinas(pl, Protocol.TickSeconds); }
			return est.Real - 10;
		}

		double semTreino = Ganho(TipoDeDisciplina.UltraInstinct);
		AfirmarBw("FORA do treino, dez segundos de luta com o Instinto ligado rendem o de sempre (0,04 por segundo)",
				  Math.Abs(semTreino - Disciplinas.UltraInstinct.RealPorSegundo * 10) < 0.01, $"+{semTreino:0.000}");

		pl.Pos = new Vec2(w.Pos.X, w.Pos.Y + 40);
		ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComWhis, "");
		double comWhis = Ganho(TipoDeDisciplina.UltraInstinct);
		AfirmarBw("TREINANDO COM O WHIS, o mesmo tempo rende o TRIPLO de maestria do Ultra Instinto",
				  _whis.Sessao != null && Math.Abs(comWhis / semTreino - BillsEWhis.AceleracaoDaMaestria) < 0.02, $"+{comWhis:0.000} = {comWhis / semTreino:0.00}x");
		double destruicaoComWhis = Ganho(TipoDeDisciplina.PoderDaDestruicao);
		AfirmarBw("...mas a do Poder da Destruicao NAO acelera com o Whis (cada mestre apura a disciplina dele)",
				  Math.Abs(destruicaoComWhis / semTreino - 1) < 0.02, $"{destruicaoComWhis / semTreino:0.00}x");

		BillsEWhis.TreinoNaoAceleraDeTeste = true;
		try
		{
			double semAceleracao = Ganho(TipoDeDisciplina.UltraInstinct);
			AfirmarBw("(defeito injetado: o treino nao acelera) com o Whis rende o MESMO que sozinho -- e a afirmacao do triplo reprova",
					  Math.Abs(semAceleracao / semTreino - 1) < 0.02, $"{semAceleracao / semTreino:0.00}x");
		}
		finally { BillsEWhis.TreinoNaoAceleraDeTeste = false; }

		AfirmarBw("a primeira fala do Whis pra quem JA TEM o Instinto e a que avisa que ele cresce mais depressa ali",
				  ((Func<bool>)(() =>
				  {
					  EncerrarASessao(_whis, FimDaSessao.ParceiroParou);
					  pl.UltraInstinct.Aprendida = true;
					  pl.PoderDaDestruicao.Aprendida = false;
					  EscutaDeFalas!.Clear();
					  ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComWhis, "");
					  return EscutaDeFalas.Any(e => e.Quem == w.Id && e.Texto == BillsEWhis.WhisComecaComInstinto);
				  }))());
		EncerrarASessao(_whis, FimDaSessao.ParceiroParou);

		pl.Pos = new Vec2(b.Pos.X, b.Pos.Y + 40);
		ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComBills, "");
		double comBills = Ganho(TipoDeDisciplina.PoderDaDestruicao);
		double instintoComBills = Ganho(TipoDeDisciplina.UltraInstinct);
		AfirmarBw("TREINANDO COM O BILLS, e a maestria do Poder da Destruicao (o Ultra Ego) que rende o triplo -- e a do Instinto, nao",
				  _bills.Sessao != null && Math.Abs(comBills / semTreino - BillsEWhis.AceleracaoDaMaestria) < 0.02 && Math.Abs(instintoComBills / semTreino - 1) < 0.02,
				  $"destruicao {comBills / semTreino:0.00}x, instinto {instintoComBills / semTreino:0.00}x");
		EncerrarASessao(_bills, FimDaSessao.ParceiroParou);

		pl.UltraInstinct.Aprendida = pl.UltraInstinct.Ligada = false;
		pl.PoderDaDestruicao.Aprendida = pl.PoderDaDestruicao.Ligada = false;
		pl.Disciplina = null;
		AplicarDisciplina(pl);
	}

	// =====================================================================
	// 9) O TREINO COM O BILLS -- "e a mesma coisa do whis", com a escola dele
	// =====================================================================
	private void BwOTreinoComOBills(ServerPlayer pl, ZoneKey zona)
	{
		GD.Print("[bills] ---- 9) o treino com o Bills");
		if (_bills.Corpo is not { } b) { AfirmarBw("(montagem) o Bills esta em casa", false); return; }

		pl.Pos = new Vec2(b.Pos.X, b.Pos.Y + 40);
		EscutaDeFalas!.Clear();
		ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComBills, "");
		BwPassar(0.3);
		double razao = b.Ficha.expressedBP / pl.Ficha.expressedBP;
		AfirmarBw("o Bills de treino tambem tem o TRIPLO do poder de quem o chamou (e nao o trilhao, nem os 15%)",
				  _bills.Sessao != null && Math.Abs(razao - 3) <= 0.03, $"{razao:0.000}x (BP base {b.Ficha.BP:N0})");
		AfirmarBw("...com o Poder da Destruicao dele ligado a 100%: a reducao de dano da aura esta no corpo",
				  b.PoderDaDestruicao is { Aprendida: true, Ligada: true, Real: 100, Atual: 100 }
				  && Math.Abs(b.Combate.ReducaoDeDano - Disciplinas.ReducaoDeDano(100)) < 1e-9, $"reducao {b.Combate.ReducaoDeDano:0.0}%");
		AfirmarBw("...sem forma nenhuma (a Fury e do desafio), e ele fala ao comecar",
				  b.Forma.Atual == Catalogo.IdDoPiso(Perfil(b)) && EscutaDeFalas.Any(e => e.Quem == b.Id && e.Texto == BillsEWhis.BillsComecaOTreino));

		for (int i = 0; i < 30 * 60; i++) { b.Combate.EntrarEmCombate(); TickDasDisciplinas(b, Protocol.TickSeconds); TickDeBills(Protocol.TickSeconds); _bills.Sessao!.Acaba += 1; }
		AfirmarBw("um minuto de luta: a energia da aura dele nao caiu", b.PoderDaDestruicao.Atual == 100 && _bills.Sessao != null);

		EncerrarASessao(_bills, FimDaSessao.ParceiroParou);
		AfirmarBw("no fim ele volta a ser objeto: parado, intocavel, com o poder de repouso e a aura desligada",
				  _bills.Modo == ModoDivino.Parado && b.Combate.Intocavel && Math.Abs(b.Ficha.BP - BillsEWhis.PoderTotal) < 1 && !b.PoderDaDestruicao.Ligada);
	}

	// =====================================================================
	// 10) O DESAFIO -- 100%, a Fury, o titulo, o Bills some, o traje
	// =====================================================================
	private void BwODesafio(ServerPlayer pl, ZoneKey zona, List<ServerPlayer> forjados)
	{
		GD.Print("[bills] ---- 10) o desafio pelo titulo");
		if (_bills.Corpo is not { } b || _whis.Corpo is not { } w) { AfirmarBw("(montagem) os dois estao em casa", false); return; }

		string traje = new RoupaGuardada(BillsEWhis.Traje, null).Id;
		AfirmarBw("o TRAJE existe como item de mochila (a folha esta no catalogo, fora da grade de roupas da criacao)",
				  CatalogoDeItens.Get(traje) != null && _visual != null && _visual.Armaduras.Contains(_visual.Peca(BillsEWhis.Traje) ?? "")
				  && !_visual.Roupas.Contains(_visual.Peca(BillsEWhis.Traje) ?? ""), $"{_visual?.Peca(BillsEWhis.Traje)}");

		pl.Mochila.Tirar(traje, pl.Mochila.Quantos(traje));
		pl.Ficha.TrajeDoDeusRecebido = false;
		pl.Pos = new Vec2(b.Pos.X, b.Pos.Y + 40);
		List<string> Pedir() => Ouvir(() => ComandoDeInteracao(pl, BillsEWhis.VerboDesafiar, ""));

		// ---- quem pode desafiar ----
		pl.Ficha.godki = null;
		EscutaDeFalas!.Clear();
		List<string> semKi = Pedir();
		AfirmarBw("SEM ki divino desperto o desafio e recusado (a mesma porta do duelo entre jogadores) -- e o Bills responde",
				  _bills.Sessao == null && Disse(semKi, "ki divino") && EscutaDeFalas.Any(e => e.Quem == b.Id && e.Texto == BillsEWhis.BillsSemKiDivino), Ultimos(semKi));
		pl.Ficha.godki = new GodKiState { awakened = true, mastery = 100 };

		// ---- A FURY: e do agente `godkit`; sem ela no catalogo o desafio nao abre (e nao vira um Bills sem forma) ----
		bool temFury = Catalogo.Def(BillsEWhis.IdDaFury) != null;
		AfirmarBw($"a forma Fury (`{BillsEWhis.IdDaFury}`) esta no catalogo de formas -- ela e do agente `godkit`; sem ela o desafio pelo titulo NAO ABRE",
				  temFury, "esperando o `godkit` publicar a forma");
		if (!temFury)
		{
			List<string> fechado = Pedir();
			AfirmarBw("...e sem a Fury o pedido e RECUSADO com a frase (ninguem ganha o titulo de um Bills sem forma)",
					  _bills.Sessao == null && Disse(fechado, "Fury"), Ultimos(fechado));
			return;
		}

		// ---- o duelo comeca: os dois inteiros, a contagem, o mundo avisado ----
		pl.Ficha.BP = 5e9;
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();

		BillsEWhis.DesafioSemFuryDeTeste = true;
		try
		{
			Pedir();
			AfirmarBw("(defeito injetado: o Bills aceita o desafio SEM entrar na Fury) a forma dele nao e a Fury e o poder fica no trilhao seco -- as duas afirmacoes de baixo reprovariam",
					  _bills.Sessao is { Desafio: true } && b.Forma.Atual != BillsEWhis.IdDaFury && b.Ficha.expressedBP < 2e12,
					  $"forma '{b.Forma.Atual}', expresso {b.Ficha.expressedBP:N0}");
			if (_bills.Sessao != null) EncerrarASessao(_bills, FimDaSessao.MestreSumiu);
		}
		finally { BillsEWhis.DesafioSemFuryDeTeste = false; }
		AfirmarBw("(montagem) o desafio desfeito sem vencedor deixa o Bills parado, sem titulo de ninguem e sem espera pra quem pediu",
				  _bills.Sessao == null && _bills.Modo == ModoDivino.Parado && TronoVago(BillsEWhis.CargoDoBills) && !EhDeusDaDestruicao(b));
		pl.Combate.Corpo.Partes[0].Vida *= 0.5;
		pl.Combate.SincronizarVida();
		EscutaDasSagas!.Clear();
		EscutaDeFalas.Clear();
		EscutaDeAnuncios = [];
		List<string> aceito = Pedir();
		AfirmarBw("com ki divino, de pe e ao lado dele, o desafio ABRE -- e o mundo inteiro e avisado do duelo",
				  _bills.Sessao is { Desafio: true, Lutando: false } && EscutaDasSagas.Any(t => t.Contains("DUELO PELO TÍTULO") && t.Contains(pl.Name)), Ultimos(aceito));
		if (_bills.Sessao == null) return;

		AfirmarBw("os dois entram INTEIROS (a regra do duelo), e na contagem ninguem bate em ninguem: o Bills intocavel e sem mente",
				  pl.Ficha.HP >= 99.9 && b.Combate.Intocavel && b.Cerebro == null);
		double mult = MultiplicadorDaForma(b);
		AfirmarBw($"ele esta na FURY (a forma `{BillsEWhis.IdDaFury}`, {mult:0.#}x) -- e o anuncio de forma saiu pra zona",
				  b.Forma.Atual == BillsEWhis.IdDaFury && mult > 1 && EscutaDeAnuncios.Any(e => e.Quem == b.Id && e.Para == BillsEWhis.IdDaFury));
		AfirmarBw("o poder BASE dele e o TRILHAO inteiro (100%), e a Fury multiplica por cima",
				  Math.Abs(b.Ficha.BP - 1e12) < 1 && b.Ficha.expressedBP >= 1e12 * mult * 0.9,
				  $"base {b.Ficha.BP:N0}, expresso {b.Ficha.expressedBP:N0}");
		AfirmarBw("ele entrou pela PORTA DO KIT: no desafio o corpo dele responde como o Deus da Destruicao -- e o TRONO continua vago (a consagracao nao escreve nele)",
				  EhDeusDaDestruicao(b) && TronoVago(BillsEWhis.CargoDoBills));
		for (int i = 0; i < 3; i++) ReconciliarDadiva(b);
		AfirmarBw("...e a reconciliacao do cargo (que derruba a Fury de quem nao tem o titulo) nao tira a dele",
				  b.Forma.Atual == BillsEWhis.IdDaFury);

		BwPassar(Duelo.Contagem / 1000.0 + 0.2);
		AfirmarBw("depois dos cinco segundos: ele ganha mente, a presa e quem desafiou, e deixa de ser intocavel",
				  _bills.Sessao is { Lutando: true } && b.Cerebro != null && PresaDoNpc(b)?.Id == pl.Id && !b.Combate.Intocavel);
		AfirmarBw("...e os dois falaram antes da luta (as tres linhas do desafio)",
				  BillsEWhis.FalasDoDesafio.All(x => EscutaDeFalas.Any(e => e.Texto == x.Texto)));
		BwPassar(2);
		AfirmarBw("o poder dele NAO segue o do desafiante (isso e do treino): continua o trilhao",
				  Math.Abs(b.Ficha.BP - BillsEWhis.PoderNoDesafio(mult)) < 1, $"{b.Ficha.BP:N0}");

		// ---- o desafiante cai: o titulo fica, e ele espera um dia ----
		EscutaDasSagas.Clear();
		pl.Combate.Nocautear(60);
		BwPassar(0.1);
		AfirmarBw("o desafiante CAI: o Bills defende o titulo (anunciado), sai da Fury e volta a ser objeto; o trono continua vago",
				  _bills.Sessao == null && TronoVago(BillsEWhis.CargoDoBills) && b.Forma.Atual != BillsEWhis.IdDaFury && _bills.Modo == ModoDivino.Parado
				  && EscutaDasSagas.Any(t => t.Contains("defendeu") && t.Contains(pl.Name)), string.Join(" | ", EscutaDasSagas));
		AfirmarBw("...e fora do desafio ele NAO carrega o kit (a consagracao durou o duelo): na Terra e no treino o poder e o cravado, sem o surto do God's Temper",
				  !EhDeusDaDestruicao(b));
		List<string> cedo = Pedir();
		AfirmarBw("...e quem perdeu so desafia de novo depois de um dia in-game", _bills.Sessao == null && Disse(cedo, "dia"), Ultimos(cedo));
		_adiantoDoCeu += Ceu.SegundosPorDia * BillsEWhis.DiasEntreDesafiosDoMesmo;

		// ---- um terceiro se mete: o duelo e anulado, sem titulo e sem espera ----
		Pedir();
		BwPassar(Duelo.Contagem / 1000.0 + 0.2);
		ServerPlayer comparsa = BwForjar("Comparsa", "Human", zona, new Vec2(b.Pos.X - 40, b.Pos.Y), 9e12, forjados);
		EscutaDasSagas.Clear();
		MarcarAgressao(b, comparsa);
		b.Combate.Nocautear(30);
		BwPassar(0.1);
		AfirmarBw("um TERCEIRO derruba o Bills no meio do duelo: ANULADO -- ninguem e coroado com a ajuda de um amigo",
				  _bills.Sessao == null && TronoVago(BillsEWhis.CargoDoBills) && !b.Ficha.KO && EscutaDasSagas.Any(t => t.Contains("ANULADO")), string.Join(" | ", EscutaDasSagas));
		List<string> deNovo = Pedir();
		AfirmarBw("...e o anulado NAO custa a espera de um dia a quem desafiou", _bills.Sessao != null, Ultimos(deNovo));

		// ---- o Bills cai: o titulo ----
		BwPassar(Duelo.Contagem / 1000.0 + 0.2);
		EscutaDasSagas.Clear();
		EscutaDeFalas.Clear();
		MarcarAgressao(b, pl);
		b.Combate.Nocautear(30);
		BwPassar(0.1);
		AfirmarBw("O BILLS CAI: quem desafiou E O NOVO DEUS DA DESTRUICAO -- o trono tem a conta dele, na memoria e no disco",
				  string.Equals(CargoDe(pl.Conta), BillsEWhis.CargoDoBills, StringComparison.OrdinalIgnoreCase)
				  && System.IO.File.ReadAllText(ArquivoDeCargos).Contains($"{BillsEWhis.CargoDoBills}\t{pl.Conta}", StringComparison.OrdinalIgnoreCase),
				  $"cargo de {pl.Conta}: '{CargoDe(pl.Conta)}'");
		AfirmarBw("O TRAJE ESTA NA MOCHILA (a mesma peca que o Bills veste), e a ficha marca que ele ja o recebeu",
				  pl.Mochila.Quantos(traje) == 1 && pl.Ficha.TrajeDoDeusRecebido, $"{pl.Mochila.Quantos(traje)} na mochila");
		AfirmarBw("...e da pra VESTI-LO pelo caminho de todo item de roupa",
				  ((Func<bool>)(() =>
				  {
					  pl.Visual.Roupa.Clear();
					  ComandoDeItem(pl, "item_" + RoupaGuardada.AcaoVestir, traje);
					  bool vestiu = pl.Visual.Roupa.Any(r => r.Caminho == _visual?.Peca(BillsEWhis.Traje));
					  if (vestiu) ComandoDeItem(pl, "item_" + RoupaGuardada.AcaoTirar, traje);
					  return vestiu && pl.Mochila.Quantos(traje) == 1;
				  }))());

		AfirmarBw("o Bills ainda esta la, se despedindo (o trono ja tem dono, mas ninguem some no meio da frase)",
				  _cenaDivina && _bills.Corpo == b && _players.ContainsKey(b.Id) && b.Combate.Intocavel);
		BwPassar(1.5 + BillsEWhis.FalasDaCoroacao.Length * BillsEWhis.SegundosEntreFalas + 2);
		AfirmarBw("as quatro linhas da despedida sairam (o Whis fala do traje)",
				  BillsEWhis.FalasDaCoroacao.All(x => EscutaDeFalas.Any(e => e.Texto == x.Texto)), $"{EscutaDeFalas.Count} fala(s)");
		AfirmarBw("...e O BILLS DEIXOU DE EXISTIR: o corpo saiu do mundo -- e o Whis, cujo cargo continua vago, ficou",
				  _bills.Corpo == null && !_players.ContainsKey(b.Id) && _whis.Corpo == w && _players.ContainsKey(w.Id) && !BillsExiste);

		// ---- o traje e UMA vez; e so pra quem tem o titulo ----
		for (int i = 0; i < 3; i++) ReconciliarDadiva(pl);
		AfirmarBw("reconciliar o cargo de novo NAO da outro traje (uma vez por personagem)", pl.Mochila.Quantos(traje) == 1);
		TrajeTodaVezDeTeste = true;
		try
		{
			ReconciliarDadiva(pl);
			AfirmarBw("(defeito injetado: o traje e entregue toda vez) a mochila ganha o segundo -- e a afirmacao de cima reprova",
					  pl.Mochila.Quantos(traje) == 2, $"{pl.Mochila.Quantos(traje)}");
		}
		finally { TrajeTodaVezDeTeste = false; pl.Mochila.Tirar(traje, 1); }

		ServerPlayer plebeu = BwForjar("Plebeu", "Human", zona, new Vec2(w.Pos.X - 80, w.Pos.Y), 1e6, forjados);
		ReconciliarDadiva(plebeu);
		AfirmarBw("quem NAO tem o titulo nao recebe traje nenhum", plebeu.Mochila.Quantos(traje) == 0 && !plebeu.Ficha.TrajeDoDeusRecebido);

		// ---- o titulo por OUTRA porta (o admin): o traje chega pelo mesmo caminho ----
		if (Cargos.Get(BillsEWhis.CargoDoBills) is { } cargo)
		{
			Outorgar(cargo, plebeu);
			AfirmarBw("o titulo por OUTORGA DE ADMIN entrega o traje do mesmo jeito (o lugar unico e a reconciliacao do cargo)",
					  plebeu.Mochila.Quantos(traje) == 1 && plebeu.Ficha.TrajeDoDeusRecebido);
			AfirmarBw("...e quem PERDEU o titulo fica com a peca (e roupa, nao poder)",
					  pl.Mochila.Quantos(traje) == 1 && !string.Equals(CargoDe(pl.Conta), BillsEWhis.CargoDoBills, StringComparison.OrdinalIgnoreCase));

			// ---- o titulo vaga: o Bills volta pra casa, e o desafio reabre ----
			Destronar(BillsEWhis.CargoDoBills, "bancada");
			BwPassar(1);
			AfirmarBw("o titulo VAGA (o Deus foi destituido): o Bills VOLTA a existir, em casa, ao lado do Whis",
					  _bills.Corpo is { } voltou && voltou.Zone.Equals(zona) && _bills.Modo == ModoDivino.Parado && BillsExiste);
		}
	}

	// =====================================================================
	// 11) NENHUM PODER NO FIO -- o canario
	// =====================================================================
	private void BwNenhumPoderNoFio(ServerPlayer pl, ZoneKey zona)
	{
		GD.Print("[bills] ---- 11) a regra principal do BP: nenhum numero de poder nos pacotes novos");
		if (_bills.Corpo is not { } b || _whis.Corpo is not { } w) { AfirmarBw("(montagem) os dois estao em casa", false); return; }

		// TODO PODER QUE ESTE SISTEMA CONHECE, nas codificacoes em que um pacote o levaria.
		var poderes = new List<(string Nome, double Valor)>
		{
			("o trilhao do Bills", BillsEWhis.PoderTotal), ("os 15% dele", BillsEWhis.PoderNaTerra),
			("o base do Bills", b.Ficha.BP), ("o expresso do Bills", b.Ficha.expressedBP),
			("o base do Whis", w.Ficha.BP), ("o expresso do Whis", w.Ficha.expressedBP),
			("o base do host", pl.Ficha.BP), ("o expresso do host", pl.Ficha.expressedBP),
			("o triplo do host", BillsEWhis.PoderDoTreino(pl.Ficha.expressedBP)),
		};
		IEnumerable<(string Nome, byte[] Bytes)> Canarios()
		{
			foreach ((string nome, double v) in poderes)
			{
				if (v <= 0) continue;
				yield return ($"{nome} (double)", BitConverter.GetBytes(v));
				yield return ($"{nome} (float)", BitConverter.GetBytes((float)v));
				yield return ($"{nome} (long)", BitConverter.GetBytes((long)v));
				yield return ($"{nome} (escrito)", System.Text.Encoding.UTF8.GetBytes(((long)v).ToString()));
			}
		}
		string Vazou() => string.Join(", ", EscutaDeBills!.SelectMany(p => Canarios().Where(k => BwContem(p.Fio, k.Bytes)).Select(k => k.Nome)).Distinct());

		// UMA VOLTA COMPLETA DOS DOIS PACOTES: a casa (revelada) e o apontado (convite, saida, nenhum).
		EscutaDeBills!.Clear();
		_casaVistaPor.Clear();
		_divinoApontado.Clear();
		pl.Pos = new Vec2(b.Pos.X, b.Pos.Y + 40);
		BwPassar(1);
		ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComBills, "");
		BwPassar(1);
		if (_bills.Sessao != null) EncerrarASessao(_bills, FimDaSessao.ParceiroParou);
		pl.Pos = new Vec2(b.Pos.X, b.Pos.Y + 12 * ZoneCollision.TileSize);
		BwPassar(1);

		int casas = EscutaDeBills.Count(p => p.Fio[0] == (byte)Protocol.S2C.CasaDeBills);
		int apontados = EscutaDeBills.Count(p => p.Fio[0] == (byte)Protocol.S2C.DivinoPerto);
		AfirmarBw($"os dois pacotes novos sairam nesta volta ({casas} da casa, {apontados} do menu apontado)", casas >= 1 && apontados >= 3);
		AfirmarBw("(controle do canario) a busca ACHA o que esta no pacote: o nome 'Bills' viaja no apontado",
				  EscutaDeBills.Any(p => BwContem(p.Fio, System.Text.Encoding.UTF8.GetBytes("Bills"))));
		AfirmarBw("NENHUM numero de poder em pacote nenhum (o trilhao, os 15%, o triplo, o do host: em double, float, inteiro e por escrito)",
				  Vazou().Length == 0, Vazou());
		AfirmarBw("...e o pacote da casa tem EXATAMENTE os campos do endereco (1 + 1 + 4 + 4 + 1 + 8 bytes)",
				  EscutaDeBills.Where(p => p.Fio[0] == (byte)Protocol.S2C.CasaDeBills).All(p => p.Fio.Length == 19),
				  string.Join(",", EscutaDeBills.Where(p => p.Fio[0] == (byte)Protocol.S2C.CasaDeBills).Select(p => p.Fio.Length)));

		PoderNoFioDeBillsDeTeste = true;
		try
		{
			EscutaDeBills.Clear();
			_divinoApontado.Clear();
			pl.Pos = new Vec2(b.Pos.X, b.Pos.Y + 40);
			BwPassar(0.3);
			AfirmarBw("(defeito injetado: o poder do corpo vai no pacote do menu) o canario PEGA", Vazou().Length > 0, Vazou());
		}
		finally { PoderNoFioDeBillsDeTeste = false; }
	}

	// =====================================================================
	// 12) A LIMPEZA TOTAL E A MAO DO ADMIN
	// =====================================================================
	private void BwALimpezaEAMaoDoAdmin(ServerPlayer pl)
	{
		GD.Print("[bills] ---- 12) a limpeza total e os comandos de admin");
		SistemaDoMundo? inscrito = _sistemasDoMundo.Find(s => s.Meu(NomeDoArquivoDaVisita));
		AfirmarBw("(montagem) a visita esta inscrita na limpeza total", inscrito != null);
		if (inscrito == null) return;

		int idDoBills = _bills.Corpo?.Id ?? 0, idDoWhis = _whis.Corpo?.Id ?? 0;
		AfirmarBw("com a historia em andamento a limpeza conta UM", inscrito.Contar() == 1);

		string pasta = System.IO.Path.GetDirectoryName(CaminhoDaVisita) ?? ".";
		long bytesAntes = System.IO.File.Exists(CaminhoDaVisita) ? new System.IO.FileInfo(CaminhoDaVisita).Length : -1;
		DateTime carimbo = System.IO.File.Exists(CaminhoDaVisita) ? System.IO.File.GetLastWriteTimeUtc(CaminhoDaVisita) : default;
		inscrito.Zerar();
		AfirmarBw("o ZERADOR da limpeza devolve o primeiro boot: a historia dorme, os dois saem do mundo e a contagem e zero",
				  _visita.Fase == FaseDaVisita.Dormindo && inscrito.Contar() == 0 && _bills.Corpo == null && _whis.Corpo == null
				  && !_players.ContainsKey(idDoBills) && !_players.ContainsKey(idDoWhis));
		AfirmarBw("...SO NA MEMORIA: ele nao toca no arquivo (quem apaga a pasta e a limpeza)",
				  (System.IO.File.Exists(CaminhoDaVisita) ? new System.IO.FileInfo(CaminhoDaVisita).Length : -1) == bytesAntes
				  && (!System.IO.File.Exists(CaminhoDaVisita) || System.IO.File.GetLastWriteTimeUtc(CaminhoDaVisita) == carimbo), pasta);

		bool portao = _limpezaEmCurso;
		_limpezaEmCurso = true;
		try
		{
			_visita.Fase = FaseDaVisita.NaTerra;
			SalvarBills();
			AfirmarBw("no MEIO da limpeza nada grava (o portao): o arquivo nao recebe a fase nova", BwFaseNoDisco() != FaseDaVisita.NaTerra);
		}
		finally { _limpezaEmCurso = portao; _visita.Fase = FaseDaVisita.Dormindo; }

		// ---- a mao do admin: a historia inteira sem esperar um mes ----
		if (_sagas.Do(BillsEWhis.SagaQueOAcorda) is { } boo) boo.Fase = FaseDaSaga.EmCurso;
		List<string> estado = Ouvir(() => AdminBills(pl, "estado"));
		AfirmarBw("`admin_bills estado` diz em que pe a historia esta", Disse(estado, "dormindo"), Ultimos(estado));
		AdminBills(pl, "avancar");
		AfirmarBw("`admin_bills avancar` (1a vez): Boo 'cai' -- a historia fica a caminho", _visita.Fase == FaseDaVisita.ACaminho);
		AdminBills(pl, "avancar");
		BwPassar(1);
		AfirmarBw("`admin_bills avancar` (2a vez): o mes 'passa' -- no segundo seguinte ele esta na Terra",
				  _visita.Fase == FaseDaVisita.NaTerra && _bills.Corpo != null);
		AdminBills(pl, "avancar");
		BwPassar(0.1);
		AfirmarBw("`admin_bills avancar` (3a vez): ele 'e derrubado' -- a fase vira 'em casa' e a despedida comeca",
				  _visita.Fase == FaseDaVisita.EmCasa && _cenaDivina);
		List<string> doChao = Ouvir(() => AdminBills(pl, "casa"));
		AfirmarBw("`admin_bills casa` do CHAO e recusado (um salto que troca de zona sozinho largaria no vacuo quem o dispara do chao)",
				  Disse(doChao, "decole") && !Espaco.EhEspaco(pl.Zone), Ultimos(doChao));
		AdminBills(pl, "reiniciar");
		AfirmarBw("`admin_bills reiniciar`: a historia volta a dormir e os dois saem do mundo",
				  _visita.Fase == FaseDaVisita.Dormindo && _bills.Corpo == null && _whis.Corpo == null && !_cenaDivina && _roteiroDivino.Count == 0);
	}
}
