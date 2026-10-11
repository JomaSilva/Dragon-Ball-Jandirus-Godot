using System.Text.Json;
using Godot;
using Jandirus.Core.Ai;
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
		long paredeGuardada = AdiantoDoRelogioDeTeste;
		_bwRelogioBase = paredeGuardada;
		_bwAndado = 0;
		_bwCorredor = null;
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
			BwOKitNoCerebro(pl);
			BwALinhaDoTempo(pl, boo);
			BwALutaNaTerra(pl, forjados);
			BwOKitNaTerra(forjados);
			BwOWhisNaTerra(forjados);
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
			BwOKitEmCasa(pl, zona, forjados);
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
			BillsEWhis.HakaiDesligadoDeTeste = BillsEWhis.SemKitNaTerraDeTeste = BillsEWhis.FuryNaTerraDeTeste = BillsEWhis.KitNoTreinoDeTeste = false;
			BillsEWhis.WhisColadoNaLutaDeTeste = BillsEWhis.WhisNaoVoltaDeTeste = BillsEWhis.WhisTocavelNaLutaDeTeste = false;
			BillsEWhis.WhisCaladoDeTeste = BillsEWhis.WhisRepeteDeTeste = BillsEWhis.BillsVoltaNumPassoDeTeste = false;
			AdiantoDoRelogioDeTeste = paredeGuardada;
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
		todas.Add(BillsEWhis.BillsApagouODesafiante);
		todas.AddRange(BillsEWhis.FalasDeApagar);
		todas.AddRange(BillsEWhis.PalpitesDeComida);
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
	// 0b) O KIT NO CEREBRO -- a escolha de cada habilidade, com a percepcao na mao (sem mundo nenhum)
	// =====================================================================
	private void BwOKitNoCerebro(ServerPlayer pl)
	{
		GD.Print("[bills] ---- 0b) o kit do Deus da Destruicao no cerebro: QUANDO ele pede cada habilidade (percepcao sintetica, sem mundo)");
		const float T = ZoneCollision.TileSize;
		const double dt = 1.0 / 30;
		Fighter f = pl.Ficha;

		// ---- as chaves do dono, uma por luta ----
		AfirmarBw("as duas chaves do Hakai estao LIGADAS (o kit completo): a da luta da Terra e a do desafio, cada uma com o seu nome",
				  BillsEWhis.OBillsExecuta(noDesafio: false) && BillsEWhis.OBillsExecuta(noDesafio: true));
		BillsEWhis.HakaiDesligadoDeTeste = true;
		AfirmarBw("(as chaves viradas) com o Hakai desligado nenhuma das duas lutas executa",
				  !BillsEWhis.OBillsExecuta(noDesafio: false) && !BillsEWhis.OBillsExecuta(noDesafio: true));
		BillsEWhis.HakaiDesligadoDeTeste = false;

		// ---- os dois tiros, do jeito que a IA os le ----
		AfirmarBw("o PRECO que a IA le dos dois tiros e a conta do proprio verb: 40 x BaseDrain o Fury, 60 x BaseDrain a esfera",
				  f.BaseDrain() > 0 && Math.Abs(KitDoDeusNaIa.Furia.CustoDeKi(f) - KitDoDeus.FuriaKi * f.BaseDrain()) < 1e-9
				  && Math.Abs(KitDoDeusNaIa.Esfera.CustoDeKi(f) - KitDoDeus.EsferaKi * f.BaseDrain()) < 1e-9,
				  $"BaseDrain {f.BaseDrain():0.###}: {KitDoDeusNaIa.Furia.CustoDeKi(f):0.##} / {KitDoDeusNaIa.Esfera.CustoDeKi(f):0.##}");
		AfirmarBw("...os ids sao os dos verbs do kit, a janela da esfera e mais curta que a do Fury (ela so sai de perto), e nenhuma passa do alcance fisico dos tiros",
				  KitDoDeusNaIa.Furia.Id == KitDoDeus.VerbFuria && KitDoDeusNaIa.Esfera.Id == KitDoDeus.VerbEsfera
				  && KitDoDeusNaIa.Esfera.AlcanceMaxTiles < KitDoDeusNaIa.Furia.AlcanceMaxTiles
				  && KitDoDeusNaIa.Furia.AlcanceMaxTiles <= KitDoDeus.AlcanceDosTiros);

		var osDois = new Arsenal([Resolvido(KitDoDeusNaIa.Furia, f), Resolvido(KitDoDeusNaIa.Esfera, f)]);
		var soOFury = new Arsenal([Resolvido(KitDoDeusNaIa.Furia, f)]);
		Cerebro Novo(bool kit, Arsenal tiros) =>
			new() { Inteligencia = 0.9, Poderes = new Capacidades { KitDoDeus = kit, DeLonge = tiros, TemComQueAparar = true } };
		Percepcao Ver(float tiles, bool caido = false, bool hakai = false, int seguro = 0, bool vindo = false, bool andando = false, double ki = 1e9) => new()
		{
			DoAlvo = new Vec2(tiles * T, 0), TemAlvo = true, IdDoAlvo = 77, AlvoCaido = caido, AlvoSeMovendo = andando,
			VidaFrac = 1, KiFrac = ki > 0 ? 1 : 0, Ki = ki, FolegoFrac = 1, MeuPoder = 100, PoderDoAlvo = 50, VidaDoAlvo = 1, LinhaLivre = true,
			HakaiPegaOAlvo = hakai, Segurando = seguro, HaKiPraAnular = vindo,
		};
		// O QUE O CEREBRO PEDE, tique a tique, vendo a mesma cena por `segundos`: (quando, o comando).
		List<(double Em, Comando C)> Ver30(Cerebro c, Percepcao p, double segundos)
		{
			var rng = new Random(11);
			var visto = new List<(double, Comando)>();
			for (double t = 0; t < segundos; t += dt) visto.Add((t, c.Pensar(p, dt, rng)));
			return visto;
		}
		List<string> Pedidos(Cerebro c, Percepcao p, double segundos) =>
			[.. Ver30(c, p, segundos).Where(x => x.C.Habilidade is { Length: > 0 }).Select(x => x.C.Habilidade!)];

		// ---- QUANDO ATIRA: a escolha e a de todo NPC que atira, entre as duas linhas do kit ----
		List<string> longe = Pedidos(Novo(true, osDois), Ver(12), 3);
		AfirmarBw("QUANDO ATIRA -- alvo de pe a doze tiles: o Destruction God's Fury (a esfera nao alcanca a janela dela)",
				  longe.Count > 0 && longe.TrueForAll(h => h == KitDoDeus.VerbFuria), string.Join(",", longe));
		List<string> perto = Pedidos(Novo(true, osDois), Ver(5), 3);
		AfirmarBw("...alvo PARADO a cinco tiles: a Destroyer Sphere (cabe na metade de dentro da janela, e e o tiro maior)",
				  perto.Count > 0 && perto[0] == KitDoDeus.VerbEsfera, string.Join(",", perto));
		List<string> correndo = Pedidos(Novo(true, osDois), Ver(5, andando: true), 3);
		AfirmarBw("...o MESMO alvo a cinco tiles, mas ANDANDO: o Fury (a esfera e lenta de mirar; o risco dela passa da tolerancia)",
				  correndo.Count > 0 && correndo.TrueForAll(h => h == KitDoDeus.VerbFuria), string.Join(",", correndo));
		List<string> semEsfera = Pedidos(Novo(true, soOFury), Ver(5), 3);
		AfirmarBw("...e com a esfera FORA do arsenal (o Hakai desligado pra luta) o tiro e o Fury, mesmo de perto",
				  semEsfera.Count > 0 && semEsfera.TrueForAll(h => h == KitDoDeus.VerbFuria), string.Join(",", semEsfera));

		// ---- QUANDO ANULA ----
		List<string> anula = Pedidos(Novo(true, Arsenal.Vazio), Ver(12, vindo: true), 1.2);
		AfirmarBw("QUANDO ANULA -- ha ki alheio vindo que a anulacao apagaria: o pedido sai no PRIMEIRO tique, e de novo a cada meio segundo enquanto a percepcao disser sim (a recarga de verdade e do verb)",
				  anula.Count is 2 or 3 && anula.TrueForAll(h => h == KitDoDeus.VerbAnular), string.Join(",", anula));
		AfirmarBw("...e sem ki vindo ele nao gasta o gesto", !Pedidos(Novo(true, Arsenal.Vazio), Ver(12), 1.2).Contains(KitDoDeus.VerbAnular));
		AfirmarBw("(controle: um cerebro SEM o kit) a mesma percepcao nao pede anulacao nenhuma",
				  !Pedidos(Novo(false, Arsenal.Vazio), Ver(12, vindo: true), 1.2).Contains(KitDoDeus.VerbAnular));

		// ---- QUANDO AGARRA E PETELECA ----
		List<(double Em, Comando C)> colado = Ver30(Novo(true, Arsenal.Vazio), Ver(1, ki: 0), 7);
		(double Em, Comando C) bote = colado.Find(x => x.C.Habilidade == KitDoDeusNaIa.VerbAgarrar);
		AfirmarBw("QUANDO AGARRA -- colado num alvo de pe: UM pedido do `agarrar` do jogador, e so depois dos primeiros segundos de soco",
				  colado.Count(x => x.C.Habilidade == KitDoDeusNaIa.VerbAgarrar) == 1 && bote.Em >= Cerebro.SegundosAteOPrimeiroPeteleco - 0.1
				  && colado.Exists(x => x.Em < bote.Em && (x.C.Leve || x.C.Pesado)), $"agarrou aos {bote.Em:0.00} s");
		AfirmarBw("(controle: sem o kit) sete segundos colado e nenhum agarrao", !Ver30(Novo(false, Arsenal.Vazio), Ver(1, ki: 0), 7).Exists(x => x.C.Habilidade == KitDoDeusNaIa.VerbAgarrar));
		List<(double Em, Comando C)> quaseNaMao = Ver30(Novo(true, Arsenal.Vazio), Ver(1.5f, ki: 0), 7);
		AfirmarBw("...a um tile e meio (onde o soco, que tem arranque, ja alcanca e a MAO ainda nao): na hora do bote ele da o passo que falta em vez de pedir um agarrao que nao pegaria ninguem",
				  !quaseNaMao.Exists(x => x.C.Habilidade == KitDoDeusNaIa.VerbAgarrar)
				  && quaseNaMao.Exists(x => x.Em > Cerebro.SegundosAteOPrimeiroPeteleco && x.C.Rumo.X > 0.9f && !x.C.Leve && !x.C.Pesado));

		List<(double Em, Comando C)> naMao = Ver30(Novo(true, Arsenal.Vazio), Ver(0.5f, seguro: 77), 1.2);
		(double Em, Comando C) flick = naMao.Find(x => x.C.Habilidade is { Length: > 0 });
		AfirmarBw("...e PETELECA -- com alguem nos bracos o corpo PARA (sem passo, que arremessaria; sem soco, que estrangularia) e, depois do instante de se ver o agarrao, pede o POWER FLICK",
				  flick.C.Habilidade == KitDoDeus.VerbFlick && flick.Em >= Cerebro.SegundosComAPresaNaMao - 0.05
				  && naMao.TrueForAll(x => x.C.Rumo.LengthSquared < 1e-6f && !x.C.Leve && !x.C.Pesado), $"aos {flick.Em:0.00} s");
		AfirmarBw("...quem esta nos bracos E O ALVO e o Hakai o pega: em vez do peteleco, o HAKAI (ele e um movimento de agarrao)",
				  Pedidos(Novo(true, Arsenal.Vazio), Ver(0.5f, seguro: 77, hakai: true), 1.2).FirstOrDefault() == KitDoDeus.VerbHakai);
		List<string> boteErrado = Pedidos(Novo(true, Arsenal.Vazio), Ver(0.5f, seguro: 55), 1.2);
		AfirmarBw("...o bote pegou OUTRO corpo (nao e quem ele enfrenta): nada de peteleco -- ele pede o `agarrar` de novo, duas vezes (o segundo toque levanta, o terceiro SOLTA)",
				  boteErrado.Count == 2 && boteErrado.TrueForAll(h => h == KitDoDeusNaIa.VerbAgarrar), string.Join(",", boteErrado));
		AfirmarBw("...e nem o alvo no ponto do Hakai muda isso: com OUTRO nos bracos, ninguem e executado pela mao errada",
				  Pedidos(Novo(true, Arsenal.Vazio), Ver(0.5f, seguro: 55, caido: true, hakai: true), 1.2).TrueForAll(h => h == KitDoDeusNaIa.VerbAgarrar));
		List<(double Em, Comando C)> preso = Ver30(Novo(true, Arsenal.Vazio), Ver(0.5f, seguro: 77, ki: 0), Cerebro.PrazoComAPresaNaMao + 2);
		AfirmarBw("...e se o gesto nunca sai (o verb recusando), ele larga de mao: passado o prazo o plano volta -- e o primeiro passo arremessa quem ele segurava",
				  preso.Exists(x => x.Em > Cerebro.PrazoComAPresaNaMao && (x.C.Rumo.LengthSquared > 1e-6f || x.C.Leve || x.C.Pesado)));
		AfirmarBw("(controle: sem o kit) alguem nos bracos nao tira pedido de peteleco",
				  !Pedidos(Novo(false, Arsenal.Vazio), Ver(0.5f, seguro: 77), 1.2).Contains(KitDoDeus.VerbFlick));

		// ---- QUANDO EXECUTA ----
		Cerebro juiz = Novo(true, osDois);
		List<string> naMaoDele = Pedidos(juiz, Ver(1, caido: true, hakai: true), 1.2);
		AfirmarBw("QUANDO EXECUTA -- o alvo CAIDO, no ponto do Hakai e ao alcance da mao: o plano vira EXECUTAR e o pedido e o HAKAI",
				  juiz.Atual == Plano.Executar && naMaoDele.Count > 0 && naMaoDele.TrueForAll(h => h == KitDoDeus.VerbHakai), string.Join(",", naMaoDele));
		List<string> deLonge = Pedidos(Novo(true, osDois), Ver(6, caido: true, hakai: true), 1.2);
		AfirmarBw("...o mesmo alvo a seis tiles, com a esfera no arsenal: a DESTROYER SPHERE (o Hakai a distancia)",
				  deLonge.Count > 0 && deLonge.TrueForAll(h => h == KitDoDeus.VerbEsfera), string.Join(",", deLonge));
		List<(double Em, Comando C)> foraDaMao = Ver30(Novo(true, osDois), Ver(1.5f, caido: true, hakai: true), 1.2);
		AfirmarBw("...o caido a um tile e meio (fora da mao, e perto demais pra esfera): ele ANDA ate la em vez de pedir um Hakai que nao acharia ninguem",
				  foraDaMao.TrueForAll(x => x.C.Habilidade is not { Length: > 0 }) && foraDaMao.Exists(x => x.C.Rumo.X > 0.9f));
		List<(double Em, Comando C)> andando = Ver30(Novo(true, soOFury), Ver(6, caido: true, hakai: true), 1.2);
		AfirmarBw("...e sem a esfera ele ANDA ate o caido (nenhum pedido, o rumo e o do alvo)",
				  andando.TrueForAll(x => x.C.Habilidade is not { Length: > 0 }) && andando.Exists(x => x.C.Rumo.X > 0.9f));
		Cerebro manso = Novo(true, osDois);
		AfirmarBw("(controle) caido FORA do ponto do Hakai: nada -- o plano e o de sempre (alvo no chao, a luta acabou)",
				  Pedidos(manso, Ver(1, caido: true), 1.2).Count == 0 && manso.Atual == Plano.Nada);
		Cerebro semKit = Novo(false, osDois);
		AfirmarBw("(controle: sem o kit) o MESMO alvo no ponto do Hakai nao vira execucao",
				  Pedidos(semKit, Ver(1, caido: true, hakai: true), 1.2).Count == 0 && semKit.Atual == Plano.Nada);

		// ---- os comentarios do Whis: a ordem em que saem ----
		var sorte = new Random(3);
		int n = BillsEWhis.PalpitesDeComida.Length, ultimo = -1;
		bool rodadaInteira = true, emendaLimpa = true;
		for (int rodada = 0; rodada < 40; rodada++)
		{
			int[] ordem = BillsEWhis.SacolaDePalpites(n, ultimo, sorte);
			rodadaInteira &= ordem.Length == n && ordem.Distinct().Count() == n && ordem.All(i => i >= 0 && i < n);
			emendaLimpa &= ordem[^1] != ultimo;
			ultimo = ordem[0];   // (quem usa tira do FIM: o ultimo dito de uma rodada e o primeiro da lista)
		}
		AfirmarBw($"os comentarios do Whis saem em RODADAS: os {n} uma vez cada antes de qualquer um repetir, e a emenda de uma rodada na outra tambem nao repete (40 rodadas)",
				  n >= 8 && rodadaInteira && emendaLimpa && BillsEWhis.PalpitesDeComida.Distinct().Count() == n);
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
		BwPassar(1.1);
		AfirmarBw("...e entao DESISTE da briga: larga o kit e VOLTA ANDANDO pro cochilo (ou ja chegou, se o caminho era curto)",
				  _bills.Modo is ModoDivino.Voltando or ModoDivino.Cochilando && b.Cerebro == null && !EhDeusDaDestruicao(b), $"{_bills.Modo}");
		for (int i = 0; i < 30 * 14 && _bills.Modo != ModoDivino.Cochilando; i++) TickDeBills(Protocol.TickSeconds);
		AfirmarBw("...e chegando la volta a cochilar: no posto, inteiro e sem mente",
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
	// AS FERRAMENTAS DAS LUTAS DE VERDADE (o kit e o Whis): o MUNDO anda, e nao so o tique deles
	// =====================================================================

	/// <summary>O relogio de parede da bancada (as cargas, as recargas e os gestos do kit sao em milissegundos).</summary>
	private long _bwRelogioBase;
	private double _bwAndado;

	/// <summary>A faixa de chao firme das cenas da Terra, achada uma vez.</summary>
	private Vec2? _bwCorredor;

	/// <summary>Em algum tique das lutas da Terra o Bills esteve na Fury?</summary>
	private bool _bwViuAFury;

	/// <summary>
	/// Tantos segundos de MUNDO pelos tiques de producao, na ordem do `Tick()`: as grades, o combate, o tique deles,
	/// AS MENTES (`TickDosCorposSemDono` -- e daqui que sai TODA decisao do Bills: a bancada nunca aperta um verb do
	/// kit por ele), o arremesso, o agarrao, os tiros e o pulso que solta os tiros do kit. Para no tique em que
	/// `ateQue` responde sim, e devolve se respondeu.
	/// </summary>
	private bool BwLutar(double segundos, Func<bool>? ateQue = null)
	{
		int n = Math.Max(1, (int)Math.Round(segundos / Protocol.TickSeconds));
		for (int i = 0; i < n; i++)
		{
			_bwAndado += Protocol.TickSeconds * 1000.0;
			AdiantoDoRelogioDeTeste = _bwRelogioBase + (long)_bwAndado;
			MontarAsGrades();
			TickCombate(Protocol.TickSeconds);
			TickDeBills(Protocol.TickSeconds);
			TickDosCorposSemDono(Protocol.TickSeconds);
			TickDoEmpurrao();
			TickDoAgarrao(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
			PulsoDoKitDoDeus();
			if (_visita.Fase == FaseDaVisita.NaTerra && _bills.Corpo is { } b && b.Forma.Atual == BillsEWhis.IdDaFury) _bwViuAFury = true;
			if (ateQue != null && ateQue()) return true;
		}
		return false;
	}

	/// <summary>Os tiros VIVOS deste dono, na zona dele, com este dom (o do Fury derruba a guarda; o da esfera e o Hakai).</summary>
	private int BwTiros(ServerPlayer dono, DomDeProjetil dom) =>
		ProjeteisDaZona(dono.Zone.Hash).Count(p => p.Vivo && p.Dono == dono.Id && p.Dom == dom);

	private void BwLimparOsTiros(ZoneKey zona)
	{
		foreach (Projetil p in ProjeteisDaZona(zona.Hash).ToList())
			if (p.Vivo) Matar(p, FimDeProjetil.Apagou);
		TickDosProjeteis(Protocol.TickSeconds);
	}

	/// <summary>O que este corpo pode atirar de longe AGORA, do jeito que o cerebro dele le (os ids, na ordem).</summary>
	private string BwArsenal(ServerPlayer npc)
	{
		Arsenal a = LerCapacidades(npc).DeLonge;
		return string.Join(", ", Enumerable.Range(0, a.Quantas).Select(i => a[i].Id));
	}

	/// <summary>
	/// A PONTA OESTE de uma faixa de chao firme na Terra, o mais perto do cochilo dele: tres fileiras, tres celulas pra
	/// oeste dela e `tiles` pra leste. As cenas de tiro precisam de linha livre, e o posto de verdade so garante a clareira.
	/// </summary>
	private Vec2? BwCorredorNaTerra(int tiles)
	{
		if (MapaDaZonaOuCatalogo(TerraDaVisita) is not { } mapa) return null;
		const float T = ZoneCollision.TileSize;
		int cx0 = (int)MathF.Floor(_bills.Posto.X / T), cy0 = (int)MathF.Floor(_bills.Posto.Y / T);
		for (int r = 0; r <= 70; r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
				{
					if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
					bool livre = true;
					for (int i = -3; i <= tiles && livre; i++)
						for (int j = -1; j <= 1 && livre; j++)
							livre = mapa.ServeDeChao(cx0 + dx + i, cy0 + dy + j);
					if (livre) return mapa.CentroDaCelula(cx0 + dx, cy0 + dy);
				}
		return null;
	}

	/// <summary>
	/// A CENA DA TERRA, do zero: os tiros somem, os dois renascem (o Bills cochilando), e os POSTOS vao pra faixa de
	/// chao firme -- o do Bills NO MEIO dela (doze tiles livres a oeste, catorze a leste: quem a cena poe a leste dele
	/// tem linha livre, e o Whis, que recua pro lado em que esta, tem chao pra recuar sem atravessar a luta), o do
	/// Whis a `whisEm` tiles dele na mesma fileira. Nulo se nao montou.
	/// </summary>
	private (ServerPlayer B, ServerPlayer W, Vec2 Ponta)? BwCenaNaTerra(float whisEm = -1)
	{
		const float T = ZoneCollision.TileSize;
		BwLimparOsTiros(TerraDaVisita);
		CarregarBills();
		BwPassar(1);
		if (_bills.Corpo is not { } b || _whis.Corpo is not { } w) return null;
		_bwCorredor ??= BwCorredorNaTerra(26);
		if (_bwCorredor is not { } oeste) return null;
		Vec2 ponta = oeste + new Vec2(12 * T, 0);

		_bills.Posto = ponta;
		b.Pos = ponta;
		_whis.Posto = ponta + new Vec2(whisEm * T, 0);
		w.Pos = _whis.Posto;
		_ladoDoBills = null;
		return (b, w, ponta);
	}

	// =====================================================================
	// 2b) O KIT NA TERRA -- cada habilidade, pedida pelo CEREBRO dele e cumprida pelos verbs do kit
	// =====================================================================
	private void BwOKitNaTerra(List<ServerPlayer> forjados)
	{
		GD.Print("[bills] ---- 2b) o kit do Deus da Destruicao na luta da Terra (15%, sem a Fury): cada habilidade pelo caminho da IA");
		const float T = ZoneCollision.TileSize;
		var cena = new List<ServerPlayer>();
		ServerPlayer b = null!, w = null!;
		Vec2 ponta = default;
		List<string> avisos = [];
		List<string>? avisosDeAntes = EscutaDeAvisos;

		ServerPlayer Por(string nome, Vec2 onde, double bp)
		{
			ServerPlayer c = BwForjar(nome, "Human", TerraDaVisita, onde, bp, forjados);
			cena.Add(c);
			return c;
		}
		bool Cena()
		{
			foreach (ServerPlayer c in cena) { BwRecolher(c); forjados.Remove(c); }
			cena.Clear();
			avisos = [];
			EscutaDeAvisos = avisos;
			if (BwCenaNaTerra() is not { } nova) { AfirmarBw("(montagem) a cena da Terra: os dois renascidos, e uma faixa de chao firme perto do cochilo", false); return false; }
			(b, w, ponta) = nova;
			return true;
		}
		void SemKi() => b.Ficha.Ki = 0;
		void DePe(ServerPlayer c) { if (c.Ficha.HP < 70 && !c.Ficha.KO && !c.Ficha.dead) c.Combate.Curar(); }

		_bwViuAFury = false;
		try
		{
			// ---------------------------------------------------------------- (a) acordado: o kit armado, sem a Fury
			if (!Cena()) return;
			AfirmarBw("(controle) COCHILANDO ele nao carrega o kit: o corpo nao responde como o Deus, e o arsenal so tem o que ele sabe de livro",
					  _bills.Modo == ModoDivino.Cochilando && !EhDeusDaDestruicao(b) && !LerCapacidades(b).KitDoDeus && !BwArsenal(b).Contains(KitDoDeus.VerbFuria),
					  BwArsenal(b));

			ServerPlayer saco = Por("Saco", ponta + new Vec2(12 * T, 0), 100e9);
			MarcarAgressao(b, saco);
			BwLutar(0.1);
			string arsenal = BwArsenal(b);
			AfirmarBw("ACORDADO NA TERRA ele carrega o KIT do Deus da Destruicao: o corpo responde como o Deus (sem escrever no trono), o cerebro sabe do kit, e os DOIS tiros entram no arsenal ao lado do que ele sabe de livro",
					  _bills.Modo == ModoDivino.Lutando && EhDeusDaDestruicao(b) && TronoVago(BillsEWhis.CargoDoBills) && LerCapacidades(b).KitDoDeus
					  && arsenal.Contains(KitDoDeus.VerbFuria) && arsenal.Contains(KitDoDeus.VerbEsfera) && !SemHakai(b), arsenal);
			AfirmarBw("...e SEM A FURY (\"menos o fury\"): a forma e a de repouso, e o poder continua cravado nos 15% -- o base e o expresso",
					  b.Forma.Atual == Catalogo.IdDoPiso(Perfil(b)) && Math.Abs(b.Ficha.BP - 150e9) < 1 && Math.Abs(b.Ficha.expressedBP - 150e9) <= 150e9 * 0.03,
					  $"forma '{b.Forma.Atual}', base {b.Ficha.BP:N0}, expresso {b.Ficha.expressedBP:N0}");

			// ---------------------------------------------------------------- (b) quando atira: o Fury
			double ki0 = b.Ficha.Ki;
			bool viuEsfera = false;
			bool atirou = BwLutar(8, () => { viuEsfera |= BwTiros(b, DomDeProjetil.Hakai) > 0; return BwTiros(b, DomDeProjetil.DerrubaAGuarda) > 0; });
			AfirmarBw("QUANDO ATIRA -- alvo de pe a doze tiles (na janela do Fury, fora da da esfera), linha livre: o cerebro pede o DESTRUCTION GOD'S FURY, o verb cobra o Ki e a bola nasce com o dom de derrubar a guarda",
					  atirou && !viuEsfera && b.Ficha.Ki < ki0, $"plano {b.Cerebro?.Atual}, ki {ki0:0.#} -> {b.Ficha.Ki:0.#} de {b.Ficha.MaxKi:0.#}, arsenal [{arsenal}]");

			// ---------------------------------------------------------------- (c) quando atira: a esfera
			if (!Cena()) return;
			ServerPlayer parado = Por("Parado", ponta + new Vec2(5 * T, 0), 100e9);
			MarcarAgressao(b, parado);
			bool esfera = BwLutar(8, () => BwTiros(b, DomDeProjetil.Hakai) > 0);
			AfirmarBw("...e com o alvo PARADO a cinco tiles (a metade de dentro da janela dela) o tiro escolhido e a DESTROYER SPHERE: a bola nasce com o dom do HAKAI",
					  esfera, $"plano {b.Cerebro?.Atual}, ki {b.Ficha.Ki:0.#} de {b.Ficha.MaxKi:0.#}, arsenal [{BwArsenal(b)}]");

			BillsEWhis.HakaiDesligadoDeTeste = true;
			try
			{
				if (!Cena()) return;
				parado = Por("Parado", ponta + new Vec2(5 * T, 0), 100e9);
				MarcarAgressao(b, parado);
				viuEsfera = false;
				bool fury = BwLutar(8, () => { viuEsfera |= BwTiros(b, DomDeProjetil.Hakai) > 0; return BwTiros(b, DomDeProjetil.DerrubaAGuarda) > 0; });
				BwLutar(3, () => { viuEsfera |= BwTiros(b, DomDeProjetil.Hakai) > 0; return false; });
				AfirmarBw("(a chave `HakaiNaTerra` virada) a MESMA cena sem o Hakai: a esfera sai do arsenal (ela E o Hakai a distancia) e o tiro que nasce e o Fury -- nenhuma esfera em onze segundos",
						  fury && !viuEsfera && SemHakai(b) && !BwArsenal(b).Contains(KitDoDeus.VerbEsfera) && BwArsenal(b).Contains(KitDoDeus.VerbFuria), BwArsenal(b));
			}
			finally { BillsEWhis.HakaiDesligadoDeTeste = false; }

			// ---------------------------------------------------------------- (d) quando anula
			if (!Cena()) return;
			saco = Por("Saco", ponta + new Vec2(14 * T, 0), 100e9);
			MarcarAgressao(b, saco);
			BwLutar(1.2, () => { SemKi(); return false; });   // (sem Ki ele nao atira: nenhum tiro dele no caminho do que vem)
			ServerPlayer fraco = Por("AtiradorFraco", ponta + new Vec2(9 * T, T), 50e9);
			ServerPlayer forte = Por("AtiradorForte", ponta + new Vec2(9 * T, -T), 300e9);
			var bola = new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast, BaseDano = 0.05, Velocidade = 1, AlcanceTiles = 60, Deflectivel = false };
			double hp0 = b.Ficha.HP;
			Projetil doFraco = Disparar(fraco, bola, rumoDado: new Vec2(-1, 0), deOnde: b.Pos + new Vec2(6 * T, 0));
			BwLutar(1.5, () => { SemKi(); return !doFraco.Vivo; });
			bool recarga = _kitDoDeus.TryGetValue(b.Id, out EstadoDoKit? kit) && kit.AnulacaoProntaEmMs > NowMs();
			AfirmarBw("QUANDO ANULA -- um ataque de ki MAIS FRACO vindo pra cima dele: o cerebro pede a ENERGY NULLIFICATION, o tiro deixa de existir antes de encostar e a recarga de cinco minutos arma",
					  doFraco is { Vivo: false, Fim: FimDeProjetil.Apagou } && recarga && Disse(avisos, "DEIXAM DE EXISTIR") && b.Ficha.HP >= hp0 - 1e-6,
					  $"fim {doFraco.Fim}, recarga {recarga}, HP {hp0:0.0} -> {b.Ficha.HP:0.0} | {Ultimos(avisos)}");

			// (a recarga agora esta armada: pra a segunda prova o relogio do kit volta -- o que se mede e o CRITERIO, nao a recarga)
			if (kit != null) kit.AnulacaoProntaEmMs = 0;
			Projetil doForte = Disparar(forte, bola, rumoDado: new Vec2(-1, 0), deOnde: b.Pos + new Vec2(6 * T, 0));
			BwLutar(1.5, () => { SemKi(); return !doForte.Vivo; });
			AfirmarBw("...e o ataque que carrega MAIS poder que ele NAO e anulado (o criterio do verb): o cerebro nem gasta o gesto, e a recarga nao arma",
					  (doForte.Vivo || doForte.Fim != FimDeProjetil.Apagou) && kit is { AnulacaoProntaEmMs: 0 }, $"vivo {doForte.Vivo}, fim {doForte.Fim}");

			// ---------------------------------------------------------------- (e) quando agarra e peteleca
			if (!Cena()) return;
			ServerPlayer parrudo = Por("Parrudo", ponta + new Vec2(T, 0), 900e9);
			MarcarAgressao(b, parrudo);
			b.Knockback = false;   // (os socos dele nao arremessam nesta cena: o alvo fica onde esta ate o peteleco)
			bool agarrou = false, soltou = false;
			double inicio = _bwAndado, agarrouAos = -1;
			Vec2 noBote = default, doBills = default;
			BwLutar(18, () =>
			{
				SemKi();   // (sem Ki ele nao atira: a cena e a do corpo a corpo)
				DePe(parrudo);
				if (!agarrou && parrudo.AgarradoPorId == b.Id)
				{
					agarrou = true;
					agarrouAos = (_bwAndado - inicio) / 1000;
					noBote = parrudo.Pos;
					doBills = b.Pos;
				}
				soltou |= agarrou && parrudo.AgarradoPorId == 0;
				return soltou;
			});
			int tiquesDoVoo = parrudo.TiquesIniciaisDoVoo, noAr = parrudo.TiquesDeVoo;
			double forcaDoVoo = parrudo.ForcaDoVoo;
			string naFrente = string.Join(",", ZoneList(TerraDaVisita.Hash).Where(o => o != parrudo && o != b && (o.Pos - noBote).Length < 4 * T).Select(o => o.Name));
			float voou = 0;
			BwLutar(1.2, () => { SemKi(); voou = Math.Max(voou, (parrudo.Pos - noBote).Length); return false; });
			AfirmarBw("QUANDO AGARRA E PETELECA -- colado num alvo de pe (e sem Ki pra atirar): depois de uns segundos de soco o cerebro pede o AGARRAR do jogador e, com a presa nos bracos, o POWER FLICK -- ela e solta e ARREMESSADA com os numeros do kit (nove tiques, a forca e o poder dele)",
					  agarrou && soltou && Disse(avisos, "PETELECO") && agarrouAos >= Cerebro.SegundosAteOPrimeiroPeteleco - 0.6
					  && tiquesDoVoo == KitDoDeus.FlickTiquesDeVoo && Math.Abs(forcaDoVoo - b.Ficha.expressedBP) <= b.Ficha.expressedBP * 0.05 && voou > 2 * T,
					  $"agarrou aos {agarrouAos:0.0} s (Bills em {doBills.X:0},{doBills.Y:0}; presa em {noBote.X:0},{noBote.Y:0}; por perto: [{naFrente}]), voo de {tiquesDoVoo} tiques (no ar: {noAr}), forca {forcaDoVoo:N0}, voou {voou / T:0.0} tiles, plano {b.Cerebro?.Atual} | {Ultimos(avisos.Where(t => t.Contains("PETELECO") || t.Contains("agarra")).ToList())}");

			// ---------------------------------------------------------------- (e2) o bote que pega OUTRO corpo
			if (!Cena()) return;
			parrudo = Por("Parrudo", ponta + new Vec2(1.1f * T, 0), 900e9);
			ServerPlayer curioso = Por("Curioso", ponta + new Vec2(0.7f * T, 0), 900e9);   // (nunca bateu nele: esta so no caminho)
			MarcarAgressao(b, parrudo);
			b.Knockback = false;
			bool pegouOCurioso = false, largou = false;
			BwLutar(18, () =>
			{
				SemKi();
				DePe(parrudo);
				pegouOCurioso |= curioso.AgarradoPorId == b.Id;
				largou |= pegouOCurioso && curioso.AgarradoPorId == 0;
				return largou;
			});
			BwLutar(1, () => { SemKi(); return false; });
			AfirmarBw("...e o peteleco e SO de quem ele enfrenta: se o bote pega OUTRO corpo que estava na frente (um curioso que nunca bateu nele), ele o LARGA pelo proprio verb -- sem peteleco, sem arremesso e sem ferida",
					  pegouOCurioso && largou && curioso.TiquesIniciaisDoVoo == 0 && curioso.Ficha.HP >= 99.9 && !Disse(avisos, "PETELECO") && !_desafiantesNaTerra.Contains(curioso.Id),
					  $"pegou={pegouOCurioso}, largou={largou}, voo armado {curioso.TiquesIniciaisDoVoo}, HP {curioso.Ficha.HP:0.0} | {Ultimos(avisos.Where(t => t.Contains("PETELECO") || t.Contains("solta") || t.Contains("levanta")).ToList())}");

			// ---------------------------------------------------------------- (f) quando executa: o Hakai, pela mao
			// ---------------------------------------------------------------- (f) quando executa: o Hakai, pela mao
			if (!Cena()) return;
			ServerPlayer cond = Por("Condenado", ponta + new Vec2(T, 0), 100e9);
			MarcarAgressao(b, cond);
			BwLutar(0.1);
			TorsoEm(cond, 0.10);
			cond.Combate.Nocautear(30);
			EscutaDeFalas!.Clear();
			bool virouPresa = false;
			bool apagou = BwLutar(8, () => { SemKi(); virouPresa |= cond.Ficha.KO && b.Papel!.PresaDoRoteiro == cond.Id; return cond.Ficha.dead; });
			AfirmarBw("QUANDO EXECUTA -- quem cai na briga NO PONTO DO HAKAI (o torso em 10%, o poder abaixo de tres quartos do dele) CONTINUA sendo presa: o cerebro troca o plano pra executar, pede o HAKAI e o alvo e APAGADO -- a morte de verdade",
					  virouPresa && apagou && cond.Ficha.dead && Disse(avisos, "HAKAI!"),
					  $"presa={virouPresa}, morto={cond.Ficha.dead}, torso {FracaoDoTorso(cond):0.00}, plano {b.Cerebro?.Atual} | {Ultimos(avisos.Where(t => t.Contains("HAKAI")).ToList())}");
			AfirmarBw("...com a MARCA do Hakai na alma: o Enma cobra o DOBRO pra traze-la de volta",
					  cond.Ficha.hakai_mark && Math.Abs(PrecoDoEnmaPara(cond) - Alem.PrecoDoReviveDoEnma * KitDoDeus.HakaiPrecoDoRevive) < 1e-6, $"preco {PrecoDoEnmaPara(cond):N0}");
			BwLutar(2.5);
			AfirmarBw("...e o Bills resmunga depois de apagar (uma das falas de apagar, na boca dele)",
					  EscutaDeFalas.Any(e => e.Quem == b.Id && BillsEWhis.FalasDeApagar.Contains(e.Texto)));

			// ---------------------------------------------------------------- (g) quando executa: a esfera, de longe
			if (!Cena()) return;
			ServerPlayer distante = Por("CondenadoDeLonge", ponta + new Vec2(6 * T, 0), 100e9);
			MarcarAgressao(b, distante);
			BwLutar(0.1);
			TorsoEm(distante, 0.10);
			distante.Combate.Nocautear(30);
			viuEsfera = false;
			BwLutar(12, () => { viuEsfera |= BwTiros(b, DomDeProjetil.Hakai) > 0; return distante.Ficha.dead || FracaoDoTorso(distante) > 0.16; });
			AfirmarBw("...o MESMO caido a seis tiles: o pedido e a DESTROYER SPHERE (o Hakai a distancia) -- e ou ela o apaga, com a marca, ou o impacto levanta o torso acima do criterio (a regra do kit: mede-se DEPOIS do dano)",
					  viuEsfera && (distante.Ficha.dead ? distante.Ficha.hakai_mark : FracaoDoTorso(distante) > 0.15),
					  $"esfera={viuEsfera}, morto={distante.Ficha.dead}, torso {FracaoDoTorso(distante):0.00}");

			// ---------------------------------------------------------------- (h) a chave do dono desliga
			BillsEWhis.HakaiDesligadoDeTeste = true;
			try
			{
				if (!Cena()) return;
				cond = Por("Poupado", ponta + new Vec2(T, 0), 100e9);
				MarcarAgressao(b, cond);
				BwLutar(0.1);
				TorsoEm(cond, 0.10);
				cond.Combate.Nocautear(30);
				virouPresa = false;
				BwLutar(4, () => { SemKi(); virouPresa |= cond.Ficha.KO && b.Papel!.PresaDoRoteiro == cond.Id; return cond.Ficha.dead; });
				AfirmarBw("(a chave `HakaiNaTerra` virada) a MESMA queda sem o Hakai: quem caiu so caiu -- nao vira presa, nao e apagado, e o Bills fica parado esperando alguem de pe (a afirmacao do Hakai reprova)",
						  !cond.Ficha.dead && !virouPresa && !OHakaiPega(b, cond) && b.Cerebro == null && EhDeusDaDestruicao(b), $"morto={cond.Ficha.dead}, presa={virouPresa}");
			}
			finally { BillsEWhis.HakaiDesligadoDeTeste = false; }

			// ---------------------------------------------------------------- (i) os dois criterios sao os do kit
			if (!Cena()) return;
			ServerPlayer inteiro = Por("TorsoInteiro", ponta + new Vec2(T, 0), 100e9);
			ServerPlayer gigante = Por("ForteDemais", ponta + new Vec2(T, T), 20e12);
			MarcarAgressao(b, inteiro);
			BwLutar(0.1);
			MarcarAgressao(b, gigante);
			BwLutar(0.1);
			TorsoEm(inteiro, 0.60);
			inteiro.Combate.Nocautear(30);
			TorsoEm(gigante, 0.10);
			gigante.Combate.Nocautear(30);
			BwLutar(4, () => { SemKi(); return inteiro.Ficha.dead || gigante.Ficha.dead; });
			AfirmarBw("os DOIS criterios sao os do kit: caido com o torso em 60% nao e executado, e caido com o torso em 10% mas com mais de tres quartos do poder dele tambem nao",
					  !inteiro.Ficha.dead && !gigante.Ficha.dead && b.Papel!.PresaDoRoteiro == 0 && gigante.Ficha.expressedBP > b.Ficha.expressedBP * KitDoDeus.HakaiFracaoDeBp,
					  $"expresso do forte {gigante.Ficha.expressedBP:N0} contra {b.Ficha.expressedBP:N0}");

			// ---------------------------------------------------------------- (j) quem esta de pe vem antes
			if (!Cena()) return;
			ServerPlayer dePe = Por("AindaDePe", ponta + new Vec2(9 * T, 0), 20e12);
			ServerPlayer caido = Por("Caido", ponta + new Vec2(T, T), 100e9);
			MarcarAgressao(b, caido);
			BwLutar(0.1);
			MarcarAgressao(b, dePe);
			BwLutar(0.1);
			TorsoEm(caido, 0.10);
			caido.Combate.Nocautear(40);
			bool caidoFoiPresa = false;
			BwLutar(3, () => { SemKi(); DePe(dePe); caidoFoiPresa |= b.Papel!.PresaDoRoteiro == caido.Id; return false; });
			AfirmarBw("ENQUANTO HA ALGUEM DE PE NA BRIGA, quem caiu no ponto do Hakai NAO e a presa: quem ainda luta segura o Bills longe do companheiro",
					  !caido.Ficha.dead && !caidoFoiPresa && b.Papel!.PresaDoRoteiro == dePe.Id, $"presa {b.Papel!.PresaDoRoteiro} (de pe {dePe.Id}, caido {caido.Id})");
			AfirmarBw("...e quem caiu SAI da lista de quem esta na briga na hora, mesmo com o ultimo agressor de pe (era \"derrubado\" de novo a cada tique)",
					  !_desafiantesNaTerra.Contains(caido.Id) && _caidosNaTerra.Contains(caido.Id) && _desafiantesNaTerra.Contains(dePe.Id));
			dePe.Pos = new Vec2(_bills.Posto.X + (BillsEWhis.TerritorioTiles + 5) * T, _bills.Posto.Y);
			bool depois = BwLutar(14, () => { SemKi(); return caido.Ficha.dead; });
			AfirmarBw("...e quando nao sobra ninguem de pe no territorio, o Bills VOLTA ate o caido e o apaga",
					  depois && caido.Ficha.hakai_mark, $"morto={caido.Ficha.dead}, a {(caido.Pos - b.Pos).Length / T:0.0} tiles dele");

			// ---------------------------------------------------------------- (k) o God's Temper (a passiva)
			if (!Cena()) return;
			saco = Por("Saco", ponta + new Vec2(14 * T, 0), 100e9);
			for (int i = 0; i < 12; i++) FerirUmMembroG10(b, saco, 0.01, letal: false);
			AfirmarBw("(controle) COCHILANDO, doze danos sem guarda nao disparam surto nenhum: o God's Temper e do kit, e o kit e da luta",
					  !TemBuff(b, KitDoDeus.IdDoTemper));
			MarcarAgressao(b, saco);
			BwLutar(0.2, () => { SemKi(); return false; });
			double off0 = b.Ficha.Tphysoff;
			for (int tentativa = 0; tentativa < 10 && !TemBuff(b, KitDoDeus.IdDoTemper); tentativa++)
			{
				if (b.Combate.Bloqueando) { BwLutar(0.3, () => { SemKi(); return false; }); continue; }
				for (int i = 0; i < KitDoDeus.TemperGolpes; i++) FerirUmMembroG10(b, saco, 0.01, letal: false);
			}
			AfirmarBw("O GOD'S TEMPER (a passiva) -- ACORDADO, dez danos seguidos sem guarda e a ira transborda: o surto de ataque e defesa do kit entra no corpo dele",
					  _bills.Modo == ModoDivino.Lutando && TemBuff(b, KitDoDeus.IdDoTemper) && b.Ficha.Tphysoff > off0 && Disse(avisos, "ira do Deus"),
					  $"Tphysoff {off0:0.###} -> {b.Ficha.Tphysoff:0.###} | {Ultimos(avisos.Where(t => t.Contains("ira")).ToList())}");
			AfirmarBw("...e o surto NAO mexe no poder: o base continua nos 15% (o que sobe e o ataque e a defesa fisica, por dez segundos)",
					  Math.Abs(b.Ficha.BP - 150e9) < 1, $"{b.Ficha.BP:N0}");

			// ---------------------------------------------------------------- (l) nunca a Fury
			AfirmarBw("em NENHUM tique das lutas da Terra desta bancada o Bills esteve na Fury (\"menos o fury\")", !_bwViuAFury);
			BillsEWhis.FuryNaTerraDeTeste = true;
			try
			{
				if (!Cena()) return;
				saco = Por("Saco", ponta + new Vec2(12 * T, 0), 100e9);
				MarcarAgressao(b, saco);
				BwLutar(0.2);
				AfirmarBw("(defeito injetado: a luta da Terra TAMBEM veste a Fury) a forma dele vira a Fury e o poder expresso salta dos 15% -- as duas afirmacoes do \"menos o fury\" reprovam",
						  b.Forma.Atual == BillsEWhis.IdDaFury && _bwViuAFury && b.Ficha.expressedBP > 150e9 * 2, $"forma '{b.Forma.Atual}', expresso {b.Ficha.expressedBP:N0}");
			}
			finally { BillsEWhis.FuryNaTerraDeTeste = false; _bwViuAFury = false; }

			// ---------------------------------------------------------------- (m) sem o kit, nada disso acontece
			BillsEWhis.SemKitNaTerraDeTeste = true;
			try
			{
				if (!Cena()) return;
				saco = Por("Saco", ponta + new Vec2(12 * T, 0), 100e9);
				MarcarAgressao(b, saco);
				bool doKit = BwLutar(8, () => BwTiros(b, DomDeProjetil.DerrubaAGuarda) + BwTiros(b, DomDeProjetil.Hakai) > 0);
				AfirmarBw("(defeito injetado: ele acorda SEM o kit) oito segundos com o alvo na janela e nenhum tiro do kit: o corpo nao e o Deus, o cerebro nao sabe de kit e o arsenal nao tem os dois -- as afirmacoes de tiro reprovam",
						  !doKit && _bills.Modo == ModoDivino.Lutando && !EhDeusDaDestruicao(b) && !LerCapacidades(b).KitDoDeus && !BwArsenal(b).Contains(KitDoDeus.VerbFuria), BwArsenal(b));

				doFraco = Disparar(Por("AtiradorFraco", ponta + new Vec2(9 * T, T), 50e9), bola, rumoDado: new Vec2(-1, 0), deOnde: b.Pos + new Vec2(6 * T, 0));
				BwLutar(1.5, () => { SemKi(); return !doFraco.Vivo; });
				AfirmarBw("(o mesmo defeito) o ataque fraco que vinha NAO e anulado: ele chega (ou passa), e nenhuma recarga arma",
						  !(doFraco is { Vivo: false, Fim: FimDeProjetil.Apagou }) && !_kitDoDeus.ContainsKey(b.Id), $"vivo {doFraco.Vivo}, fim {doFraco.Fim}");

				if (!Cena()) return;
				parrudo = Por("Parrudo", ponta + new Vec2(T, 0), 900e9);
				MarcarAgressao(b, parrudo);
				b.Knockback = false;
				agarrou = false;
				BwLutar(10, () => { SemKi(); DePe(parrudo); agarrou |= parrudo.AgarradoPorId == b.Id; return agarrou; });
				AfirmarBw("(o mesmo defeito) dez segundos colado num alvo de pe e nenhum agarrao: sem o kit nao ha peteleco pra dar",
						  !agarrou);

				cond = Por("Condenado", ponta + new Vec2(T, T), 100e9);
				MarcarAgressao(b, cond);
				BwLutar(0.1);
				TorsoEm(cond, 0.10);
				cond.Combate.Nocautear(30);
				parrudo.Pos = new Vec2(_bills.Posto.X + (BillsEWhis.TerritorioTiles + 5) * T, _bills.Posto.Y);
				BwLutar(4, () => { SemKi(); return cond.Ficha.dead; });
				AfirmarBw("(o mesmo defeito) quem cai no ponto do Hakai so cai: ninguem o executa",
						  !cond.Ficha.dead && b.Papel!.PresaDoRoteiro == 0);
			}
			finally { BillsEWhis.SemKitNaTerraDeTeste = false; }
		}
		finally
		{
			BillsEWhis.HakaiDesligadoDeTeste = BillsEWhis.SemKitNaTerraDeTeste = BillsEWhis.FuryNaTerraDeTeste = false;
			EscutaDeAvisos = avisosDeAntes;
			foreach (ServerPlayer c in cena) { BwRecolher(c); forjados.Remove(c); }
			BwLimparOsTiros(TerraDaVisita);
		}
	}

	// =====================================================================
	// 2c) O WHIS NA TERRA -- ao lado fora da luta; de longe, intocavel e falando de comida durante ela
	// =====================================================================
	private void BwOWhisNaTerra(List<ServerPlayer> forjados)
	{
		GD.Print("[bills] ---- 2c) o Whis: segue o Bills fora da luta, assiste de longe (intocavel, comentando comida) durante ela, e volta no fim");
		const float T = ZoneCollision.TileSize;
		var cena = new List<ServerPlayer>();
		ServerPlayer b = null!, w = null!;
		Vec2 ponta = default;

		ServerPlayer Por(string nome, Vec2 onde, double bp)
		{
			ServerPlayer c = BwForjar(nome, "Human", TerraDaVisita, onde, bp, forjados);
			cena.Add(c);
			return c;
		}
		// (o Whis a OESTE do Bills quando o Bills vai andar pra leste, e a LESTE quando a briga e a oeste: nos dois casos
		// o caminho dele nao passa por dentro de ninguem, e o que se mede e a caminhada)
		bool Cena(float whisEm)
		{
			foreach (ServerPlayer c in cena) { BwRecolher(c); forjados.Remove(c); }
			cena.Clear();
			if (BwCenaNaTerra(whisEm) is not { } nova) { AfirmarBw("(montagem) a cena da Terra pro Whis", false); return false; }
			(b, w, ponta) = nova;
			return true;
		}
		float Tiles(Vec2 x, Vec2 y) => (x - y).Length / T;
		void DePe(ServerPlayer c) { if (c.Ficha.HP < 70 && !c.Ficha.KO && !c.Ficha.dead) c.Combate.Curar(); }

		// OS PASSOS DE UM CORPO, tique a tique: andou? qual foi o maior passo? (um salto de varios tiles e teleporte, nao caminhada)
		bool andouW = false, andouB = false, tocavel = false;
		float maiorPassoW = 0, maiorPassoB = 0;
		Vec2 antesW = default, antesB = default;
		void Zerar() { andouW = andouB = tocavel = false; maiorPassoW = maiorPassoB = 0; antesW = w.Pos; antesB = b.Pos; }
		void Olhar()
		{
			andouW |= w.Moving;
			andouB |= b.Moving;
			tocavel |= !w.Combate.Intocavel;
			maiorPassoW = Math.Max(maiorPassoW, (w.Pos - antesW).Length);
			maiorPassoB = Math.Max(maiorPassoB, (b.Pos - antesB).Length);
			antesW = w.Pos;
			antesB = b.Pos;
		}

		// O QUE O WHIS DISSE, com a hora (o relogio do sistema deles) em que a bancada ouviu.
		var ditas = new List<(double Em, string Texto)>();
		int ouvidas = 0;
		void Colher()
		{
			for (; ouvidas < EscutaDeFalas!.Count; ouvidas++)
				if (EscutaDeFalas[ouvidas].Quem == w.Id) ditas.Add((_relogioDivino, EscutaDeFalas[ouvidas].Texto));
		}
		void Assistir(double segundos, Func<bool>? ate = null)
		{
			int n = (int)Math.Round(segundos / Protocol.TickSeconds);
			for (int i = 0; i < n; i++)
			{
				TickDeBills(Protocol.TickSeconds);
				Colher();
				if (ate != null && ate()) return;
			}
		}

		try
		{
			if (!Cena(-BillsEWhis.TilesEntreOsDois)) return;
			AfirmarBw("(montagem) parados, o Whis esta a dois tiles do Bills -- sem mente, intocavel e no modo de objeto",
					  Math.Abs(Tiles(w.Pos, b.Pos) - BillsEWhis.TilesEntreOsDois) < 0.1f && _whis.Modo == ModoDivino.Parado && w.Cerebro == null && w.Combate.Intocavel);

			// ---------------------------------------------------------------- 1) fora de luta, ele SEGUE
			b.Pos = ponta + new Vec2(9 * T, 0);   // (o Bills, ainda cochilando, aparece nove tiles adiante)
			Zerar();
			bool aoLado = BwLutar(14, () => { Olhar(); return !w.Moving && Math.Abs(Tiles(w.Pos, b.Pos) - BillsEWhis.TilesEntreOsDois) < 0.6f; });
			AfirmarBw("FORA DE LUTA ELE SEGUE: com o Bills nove tiles adiante, o Whis VAI ANDANDO ate o lado dele e para a dois tiles -- sem mente, e intocavel o caminho inteiro",
					  aoLado && andouW && maiorPassoW < 1.5f * T && !tocavel && w.Cerebro == null && _whis.Modo == ModoDivino.Parado && !_whisAssiste,
					  $"a {Tiles(w.Pos, b.Pos):0.0} tiles, andou={andouW}, maior passo {maiorPassoW:0.0} px");

			BillsEWhis.WhisNaoVoltaDeTeste = true;
			b.Pos = ponta;
			BwLutar(4);
			AfirmarBw("(defeito injetado: o Whis nao vai pro lado do Bills) o Bills volta ao posto e o Whis fica onde estava, a nove tiles -- a afirmacao de cima reprova",
					  Tiles(w.Pos, b.Pos) > 6, $"a {Tiles(w.Pos, b.Pos):0.0} tiles");
			BillsEWhis.WhisNaoVoltaDeTeste = false;
			bool noPosto = BwLutar(14, () => !w.Moving && (w.Pos - _whis.Posto).Length < 1);
			AfirmarBw("(sem o defeito) ele volta andando pro lado do Bills, que esta no posto: o posto dele", noPosto, $"a {(w.Pos - _whis.Posto).Length:0.0} px do posto");

			// ---------------------------------------------------------------- 2) a luta comeca: de longe
			if (!Cena(BillsEWhis.TilesEntreOsDois)) return;
			ServerPlayer rival = Por("Rival", ponta + new Vec2(-T, 0), 20e12);
			EscutaDeFalas!.Clear();
			ouvidas = 0;
			MarcarAgressao(b, rival);
			b.Knockback = false;   // (os socos dele nao arremessam nesta cena: a luta fica no lugar, e o mirante tambem)
			double inicioDaLuta = _relogioDivino;
			Zerar();
			bool deLonge = BwLutar(16, () =>
			{
				b.Ficha.Ki = 0;
				DePe(rival);
				Olhar();
				Colher();
				return _whisAssiste && !w.Moving && (w.Pos - _miranteDoWhis).Length < 1
					   && Tiles(w.Pos, b.Pos) >= BillsEWhis.TilesPertoDemais && Tiles(w.Pos, rival.Pos) >= BillsEWhis.TilesPertoDemais;
			});
			Jandirus.Core.World.Facing praLuta = MoveRules.FacingFrom(b.Pos - w.Pos, w.Facing);
			AfirmarBw("COMECOU A LUTA (alguem bateu no Bills): o Whis SE AFASTA andando e fica vendo de longe -- a pelo menos seis tiles do Bills e de quem o enfrenta (o mirante e a nove do meio da luta), virado pra ela",
					  deLonge && andouW && maiorPassoW < 1.5f * T && w.Facing == praLuta,
					  $"chegou={deLonge}, a {Tiles(w.Pos, b.Pos):0.0} tiles do Bills e {Tiles(w.Pos, rival.Pos):0.0} do rival, a {(w.Pos - _miranteDoWhis).Length:0.0} px do mirante, andando={w.Moving}, andou={andouW}, maior passo {maiorPassoW:0.0} px, virado pra {w.Facing} (a luta fica a {praLuta})");
			AfirmarBw("...INTOCAVEL do primeiro passo ao mirante: o modo dele continua o de objeto, sem mente",
					  !tocavel && _whis.Modo == ModoDivino.Parado && w.Cerebro == null && w.Combate.Intocavel);

			// ---------------------------------------------------------------- 3) ninguem o alcanca
			ServerPlayer abusado = Por("Abusado", w.Pos + new Vec2(-0.8f * T, 0), 20e12);
			abusado.Facing = Jandirus.Core.World.Facing.East;
			double hpW = w.Ficha.HP;
			bool naMira = ReferenceEquals(AlvoNaFrente(abusado), w), noBote = ReferenceEquals(CorpoNaFrente(abusado), w);
			// (o defeito e medido ANTES do soco, com os dois onde a cena os pos: o soco que nao acha ninguem na frente
			// ainda pode arrancar atras de outro corpo, e ai quem bate ja nao esta mais colado no Whis)
			BillsEWhis.WhisTocavelNaLutaDeTeste = true;
			bool comODefeito = !w.Combate.Intocavel && ReferenceEquals(AlvoNaFrente(abusado), w);
			BillsEWhis.WhisTocavelNaLutaDeTeste = false;
			Atacar(abusado, Protocol.Golpe.Leve);
			Assistir(0.2);
			AfirmarBw("...e ninguem o alcanca: colado nele e de frente, o golpe nao o acha, o agarrao nao o pega e o soco nao tira vida dele",
					  !naMira && !noBote && w.Ficha.HP >= hpW && w.UltimoAgressor != abusado.Id, $"mira={naMira}, bote={noBote}, HP {w.Ficha.HP:0.0}");
			AfirmarBw("(defeito injetado: assistindo, ele pode ser atingido) o MESMO golpe passa a acha-lo -- a afirmacao de cima reprova",
					  comODefeito);
			BwRecolher(abusado);
			cena.Remove(abusado);
			forjados.Remove(abusado);

			// ---------------------------------------------------------------- 4) ...comentando comida
			// (daqui em diante so o tique DELES anda: ninguem bate em ninguem, e a luta fica de pe o tempo que for)
			if (rival.Ficha.KO) rival.Combate.Levantar();
			rival.Combate.Curar();
			rival.Pos = ponta + new Vec2(-T, 0);
			Assistir(0.1);
			AfirmarBw("(montagem) a luta continua de pe pra medir os comentarios: o Bills acordado, o rival de pe, o Whis assistindo",
					  _bills.Modo == ModoDivino.Lutando && b.Papel!.PresaDoRoteiro == rival.Id && _whisAssiste, $"{_bills.Modo}");
			int n = BillsEWhis.PalpitesDeComida.Length;
			Assistir(60 * 8, () => ditas.Count > n);
			List<string> textos = [.. ditas.Select(d => d.Texto)];
			AfirmarBw($"ELE COMENTA COMIDA: {n + 1} comentarios seguidos, todos da lista dele -- e nenhuma outra fala saiu da boca dele durante a luta",
					  ditas.Count == n + 1 && textos.TrueForAll(t => BillsEWhis.PalpitesDeComida.Contains(t)), $"{ditas.Count} fala(s): {string.Join(" | ", textos.Take(3))}");
			AfirmarBw($"...SEM REPETIR em sequencia: os {n} primeiros sao os {n} da lista, cada um uma vez, e o seguinte nao e o que acabou de ser dito",
					  ditas.Count > n && textos.Take(n).Distinct().Count() == n && textos[n] != textos[n - 1]);
			double menor = double.MaxValue, maior = 0;
			for (int i = 1; i < ditas.Count; i++)
			{
				menor = Math.Min(menor, ditas[i].Em - ditas[i - 1].Em);
				maior = Math.Max(maior, ditas[i].Em - ditas[i - 1].Em);
			}
			AfirmarBw("...ESPACADOS: o primeiro uns cinco segundos depois de a luta comecar, e entre um e o seguinte nunca menos de dezoito segundos (nem mais de vinte e oito)",
					  ditas.Count > 1 && ditas[0].Em - inicioDaLuta is > 4.5 and < 6.5 && menor >= BillsEWhis.SegundosEntrePalpites - 0.1
					  && maior <= BillsEWhis.SegundosEntrePalpites + BillsEWhis.FolgaEntrePalpites + 0.2,
					  $"primeiro aos {(ditas.Count > 0 ? ditas[0].Em - inicioDaLuta : -1):0.0} s; intervalos de {menor:0.0} a {maior:0.0} s");
			AfirmarBw("...e nenhum comentario tem um algarismo: ele fala de comida, nao de poder",
					  textos.TrueForAll(t => !t.Any(char.IsDigit)));

			BillsEWhis.WhisRepeteDeTeste = true;
			int antesDoDefeito = ditas.Count;
			Assistir(70, () => ditas.Count >= antesDoDefeito + 2);
			AfirmarBw("(defeito injetado: ele repete o mesmo comentario) os dois seguintes saem IGUAIS ao ultimo -- a afirmacao do \"sem repetir\" reprova",
					  ditas.Count >= antesDoDefeito + 2 && ditas[^1].Texto == ditas[^2].Texto && ditas[^2].Texto == ditas[antesDoDefeito - 1].Texto);
			BillsEWhis.WhisRepeteDeTeste = false;
			BillsEWhis.WhisCaladoDeTeste = true;
			int antesDoSilencio = ditas.Count;
			Assistir(45);
			AfirmarBw("(defeito injetado: ele assiste calado) quarenta e cinco segundos de luta e nenhum comentario -- a afirmacao de cima reprova",
					  ditas.Count == antesDoSilencio);
			BillsEWhis.WhisCaladoDeTeste = false;

			// ---------------------------------------------------------------- 5) a luta acaba: os dois voltam, a pe
			b.Pos = ponta + new Vec2(5 * T, 0);   // (a briga levou o Bills pra cinco tiles do cochilo)
			rival.Pos = new Vec2(_bills.Posto.X + (BillsEWhis.TerritorioTiles + 5) * T, _bills.Posto.Y);
			BwLutar(BillsEWhis.SegundosAteVoltarACochilar - 1);
			AfirmarBw("sem ninguem de pe no territorio o Bills ESPERA: ainda acordado, ainda com o kit -- e o Whis ainda de longe",
					  _bills.Modo == ModoDivino.Lutando && EhDeusDaDestruicao(b) && _whisAssiste);
			bool desistiu = BwLutar(3, () => _bills.Modo == ModoDivino.Voltando);
			AfirmarBw("...e entao DESISTE da briga: larga o kit na hora (o corpo deixa de ser o Deus, o cerebro some) e fica no modo de quem volta, ainda a cinco tiles do cochilo -- e ainda da pra bater nele",
					  desistiu && !EhDeusDaDestruicao(b) && b.Cerebro == null && !LerCapacidades(b).KitDoDeus && !BwArsenal(b).Contains(KitDoDeus.VerbFuria)
					  && Tiles(b.Pos, _bills.Posto) > 4 && !b.Combate.Intocavel, $"{_bills.Modo}, a {Tiles(b.Pos, _bills.Posto):0.0} tiles do posto");
			Zerar();
			bool emCasa = BwLutar(18, () => { Olhar(); return _bills.Modo == ModoDivino.Cochilando && !w.Moving && (w.Pos - _whis.Posto).Length < 1; });
			AfirmarBw("O BILLS VOLTA A PE pro cochilo e O WHIS VOLTA PRO LADO DELE: os dois andaram (nenhum salto), o Bills dorme inteiro no posto e o Whis para a dois tiles, virado pra frente, intocavel",
					  emCasa && andouB && andouW && maiorPassoB < 1.5f * T && maiorPassoW < 1.5f * T && (b.Pos - _bills.Posto).Length < 1 && b.Ficha.HP >= 99.9
					  && !_whisAssiste && w.Facing == Jandirus.Core.World.Facing.South && !tocavel,
					  $"Bills: {_bills.Modo}, andou={andouB}, maior passo {maiorPassoB:0.0} px | Whis: a {(w.Pos - _whis.Posto).Length:0.0} px do posto, andou={andouW}, maior passo {maiorPassoW:0.0} px");

			// ---------------------------------------------------------------- 6) os contra-exemplos da volta e do mirante
			BillsEWhis.BillsVoltaNumPassoDeTeste = true;
			rival.Pos = ponta + new Vec2(-T, 0);
			MarcarAgressao(b, rival);
			BwLutar(0.3);
			b.Pos = ponta + new Vec2(5 * T, 0);
			rival.Pos = new Vec2(_bills.Posto.X + (BillsEWhis.TerritorioTiles + 5) * T, _bills.Posto.Y);
			Zerar();
			bool dormiu = BwLutar(BillsEWhis.SegundosAteVoltarACochilar + 6, () => { Olhar(); return _bills.Modo == ModoDivino.Cochilando; });
			AfirmarBw("(defeito injetado: ele volta num passo so, como era) o Bills APARECE no posto sem andar -- um salto de cinco tiles, e a afirmacao da volta a pe reprova",
					  dormiu && !andouB && maiorPassoB > 3 * T, $"andou={andouB}, maior passo {maiorPassoB:0.0} px");
			BillsEWhis.BillsVoltaNumPassoDeTeste = false;
			BwLutar(14, () => !w.Moving && (w.Pos - _whis.Posto).Length < 1);

			BillsEWhis.WhisColadoNaLutaDeTeste = true;
			rival.Pos = ponta + new Vec2(-T, 0);
			MarcarAgressao(b, rival);
			BwLutar(8, () => { b.Ficha.Ki = 0; DePe(rival); return false; });
			AfirmarBw("(defeito injetado: o Whis continua colado no Bills durante a luta) oito segundos de briga e ele esta a menos de seis tiles dos dois -- a afirmacao do \"de longe\" reprova",
					  _bills.Modo == ModoDivino.Lutando && !_whisAssiste && Tiles(w.Pos, b.Pos) < BillsEWhis.TilesPertoDemais, $"a {Tiles(w.Pos, b.Pos):0.0} tiles");
			BillsEWhis.WhisColadoNaLutaDeTeste = false;
		}
		finally
		{
			BillsEWhis.WhisColadoNaLutaDeTeste = BillsEWhis.WhisNaoVoltaDeTeste = BillsEWhis.WhisTocavelNaLutaDeTeste = false;
			BillsEWhis.WhisCaladoDeTeste = BillsEWhis.WhisRepeteDeTeste = BillsEWhis.BillsVoltaNumPassoDeTeste = false;
			foreach (ServerPlayer c in cena) { BwRecolher(c); forjados.Remove(c); }
			BwLimparOsTiros(TerraDaVisita);

			// A HISTORIA SEGUE DA TERRA, com os dois nos postos DE VERDADE (os da cena eram da faixa de chao firme).
			CarregarBills();
			BwPassar(1);
		}
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
	// 9b) EM CASA -- o treino sem kit, o desafio com o kit inteiro e a Fury, e o Whis assistindo as lutas do Bills
	// =====================================================================
	private void BwOKitEmCasa(ServerPlayer pl, ZoneKey zona, List<ServerPlayer> forjados)
	{
		GD.Print("[bills] ---- 9b) em casa: o treino continua sem kit, o desafio e com o kit INTEIRO e a Fury, e o Whis assiste as lutas do Bills");
		if (_bills.Corpo is not { } b || _whis.Corpo is not { } w) { AfirmarBw("(montagem) os dois estao em casa", false); return; }
		const float T = ZoneCollision.TileSize;
		var cena = new List<ServerPlayer>();
		List<string> avisos = [];
		List<string>? avisosDeAntes = EscutaDeAvisos;
		float Tiles(Vec2 x, Vec2 y) => (x - y).Length / T;
		ServerPlayer Desafiante(string nome)
		{
			ServerPlayer c = BwForjar(nome, "Human", zona, new Vec2(b.Pos.X, b.Pos.Y + 40), 5e15, forjados);
			c.Ficha.GravMastered = 500;
			c.Ficha.godki = new GodKiState { awakened = true, mastery = 100 };
			cena.Add(c);
			return c;
		}
		void Enfraquecer(ServerPlayer c)
		{
			c.Ficha.BP = 5e9;
			c.Ficha.Statify();
			c.Ficha.PowerLevel();
		}
		void DePe(ServerPlayer c) { if (c.Ficha.HP < 70 && !c.Ficha.KO && !c.Ficha.dead) c.Combate.Curar(); }

		try
		{
			EscutaDeAvisos = avisos;
			// (a secao de cima acabou uma aula do Bills neste mesmo instante: o Whis ainda esta voltando do mirante)
			for (int i = 0; i < 30 * 16 && (_whisAssiste || w.Moving || (w.Pos - _whis.Posto).Length >= 1); i++) TickDeBills(Protocol.TickSeconds);
			AfirmarBw("(montagem) parados em casa: os dois nos postos, sem luta, e o Whis nao esta assistindo nada",
					  _bills.Modo == ModoDivino.Parado && _whis.Modo == ModoDivino.Parado && (b.Pos - _bills.Posto).Length < 1 && (w.Pos - _whis.Posto).Length < 1 && !_whisAssiste);

			// ---------------------------------------------------------------- 1) o treino com o Bills: nada mudou
			pl.Pos = new Vec2(b.Pos.X, b.Pos.Y + 40);
			EscutaDeFalas!.Clear();
			ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComBills, "");
			BwPassar(0.3);
			AfirmarBw("O TREINO COM O BILLS NAO MUDOU: sem kit (o corpo nao e o Deus, o cerebro nao sabe de kit, o arsenal so tem o que ele sabe de livro) e sem Fury",
					  _bills.Sessao is { Desafio: false } && !EhDeusDaDestruicao(b) && !LerCapacidades(b).KitDoDeus && !BwArsenal(b).Contains(KitDoDeus.VerbFuria)
					  && !BwArsenal(b).Contains(KitDoDeus.VerbEsfera) && b.Forma.Atual == Catalogo.IdDoPiso(Perfil(b)), BwArsenal(b));

			bool tocavel = false;
			for (int i = 0; i < 30 * 10; i++) { TickDeBills(Protocol.TickSeconds); tocavel |= !w.Combate.Intocavel; }
			AfirmarBw("O WHIS ASSISTE AO TREINO DO BILLS DE LONGE: fora do posto, a pelo menos seis tiles do Bills e de quem treina, intocavel o tempo todo -- e ja comentou comida",
					  _whisAssiste && _whis.Modo == ModoDivino.Parado && !tocavel && Tiles(w.Pos, b.Pos) >= BillsEWhis.TilesPertoDemais && Tiles(w.Pos, pl.Pos) >= BillsEWhis.TilesPertoDemais
					  && EscutaDeFalas.Any(e => e.Quem == w.Id && BillsEWhis.PalpitesDeComida.Contains(e.Texto)),
					  $"a {Tiles(w.Pos, b.Pos):0.0} tiles do Bills, {Tiles(w.Pos, _whis.Posto):0.0} do posto");

			TorsoEm(pl, 0.10);
			pl.Combate.Nocautear(30);
			BwPassar(0.1);
			AfirmarBw("...e NINGUEM MORRE NO TREINO: quem cai com o torso em 10% (o ponto do Hakai) so perde a aula -- vivo, de pe, inteiro e sem marca nenhuma",
					  _bills.Sessao == null && !pl.Ficha.dead && !pl.Ficha.KO && pl.Ficha.HP >= 99 && !pl.Ficha.hakai_mark, $"HP {pl.Ficha.HP:0.0}");
			bool voltou = false;
			for (int i = 0; i < 30 * 14 && !voltou; i++) { TickDeBills(Protocol.TickSeconds); voltou = !w.Moving && (w.Pos - _whis.Posto).Length < 1; }
			AfirmarBw("...acabada a aula o Whis VOLTA pro lado do Bills: no posto dele, sem assistir a mais nada",
					  voltou && !_whisAssiste && _whis.Modo == ModoDivino.Parado && w.Combate.Intocavel, $"a {(w.Pos - _whis.Posto).Length:0.0} px do posto");

			BillsEWhis.KitNoTreinoDeTeste = true;
			try
			{
				ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComBills, "");
				BwPassar(0.1);
				AfirmarBw("(defeito injetado: o treino tambem arma o kit) o corpo dele passa a responder como o Deus no meio de uma aula -- a afirmacao do \"treino nao mudou\" reprova",
						  _bills.Sessao != null && EhDeusDaDestruicao(b));
				EncerrarASessao(_bills, FimDaSessao.ParceiroParou);
			}
			finally { BillsEWhis.KitNoTreinoDeTeste = false; }
			AfirmarBw("(sem o defeito, e com a aula encerrada) ele nao carrega kit nenhum", !EhDeusDaDestruicao(b) && _bills.Modo == ModoDivino.Parado);
			for (int i = 0; i < 30 * 14 && (w.Moving || (w.Pos - _whis.Posto).Length >= 1); i++) TickDeBills(Protocol.TickSeconds);

			// ---------------------------------------------------------------- 2) quando e o Whis quem da aula, o Bills fica como esta
			pl.Pos = new Vec2(w.Pos.X, w.Pos.Y + 40);
			EscutaDeFalas.Clear();
			ComandoDeInteracao(pl, BillsEWhis.VerboTreinarComWhis, "");
			BwPassar(8);
			AfirmarBw("QUANDO E O WHIS QUEM DA AULA, o Bills fica como esta: no posto, objeto intocavel -- nao assiste, nao anda e ninguem comenta comida",
					  _whis.Sessao != null && !_whisAssiste && _bills.Modo == ModoDivino.Parado && (b.Pos - _bills.Posto).Length < 1 && b.Combate.Intocavel && b.Cerebro == null
					  && !EscutaDeFalas.Any(e => BillsEWhis.PalpitesDeComida.Contains(e.Texto)));
			EncerrarASessao(_whis, FimDaSessao.ParceiroParou);
			BwPassar(0.2);

			// ---------------------------------------------------------------- 3) o desafio: o kit inteiro, com a Fury
			ServerPlayer d1 = Desafiante("Desafiante");
			List<string> pedido = Ouvir(() => ComandoDeInteracao(d1, BillsEWhis.VerboDesafiar, ""));
			EscutaDeAvisos = avisos;
			AfirmarBw("(montagem) um desafiante de bancada, com o ki divino desperto, abriu o desafio pelo titulo",
					  _bills.Sessao is { Desafio: true } aberta && ReferenceEquals(aberta.Parceiro, d1), Ultimos(pedido));
			if (_bills.Sessao == null) return;
			BwLutar(Duelo.Contagem / 1000.0 + 0.5, () => _bills.Sessao is { Lutando: true });
			BwLutar(1.3, () => { b.Ficha.Ki = 0; DePe(d1); return false; });
			string arsenal = BwArsenal(b);
			AfirmarBw("NO DESAFIO ELE CARREGA O KIT INTEIRO, COM A FURY POR CIMA: o corpo e o Deus, a forma e a Fury, o cerebro sabe do kit, os dois tiros estao no arsenal e o Hakai deste duelo esta ligado",
					  _bills.Sessao is { Lutando: true } && EhDeusDaDestruicao(b) && b.Forma.Atual == BillsEWhis.IdDaFury && b.Cerebro is { Poderes.KitDoDeus: true }
					  && !SemHakai(b) && arsenal.Contains(KitDoDeus.VerbFuria) && arsenal.Contains(KitDoDeus.VerbEsfera), $"forma '{b.Forma.Atual}', arsenal [{arsenal}]");
			AfirmarBw("...e o Whis ASSISTE AO DESAFIO: fora do posto e intocavel", _whisAssiste && w.Combate.Intocavel && (w.Pos - _whis.Posto).Length > T);

			ServerPlayer mirim = BwForjar("AtiradorMirim", "Human", zona, b.Pos + new Vec2(3 * T, 0), 1e9, forjados);
			cena.Add(mirim);
			var bola = new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast, BaseDano = 0.05, Velocidade = 1, AlcanceTiles = 60, Deflectivel = false };
			Projetil vindo = Disparar(mirim, bola, rumoDado: new Vec2(-1, 0), deOnde: b.Pos + new Vec2(2.5f * T, 0));
			BwLutar(1, () => { b.Ficha.Ki = 0; DePe(d1); return !vindo.Vivo; });
			AfirmarBw("...e ele o USA, pelo cerebro: um ataque de ki fraco vindo pra cima dele no meio do duelo e ANULADO (a Energy Nullification), sem ninguem ter apertado nada por ele",
					  vindo is { Vivo: false, Fim: FimDeProjetil.Apagou } && Disse(avisos, "DEIXAM DE EXISTIR") && _bills.Sessao is { Lutando: true },
					  $"fim {vindo.Fim} | {Ultimos(avisos.Where(t => t.Contains("EXISTIR")).ToList())}");

			// ---------------------------------------------------------------- 4) o desafio pode matar: o Hakai
			Enfraquecer(d1);
			d1.Pos = b.Pos + new Vec2(T, 0);
			TorsoEm(d1, 0.10);
			d1.Combate.Nocautear(30);
			EscutaDeFalas.Clear();
			EscutaDasSagas!.Clear();
			bool esperou = false;
			BwLutar(BillsEWhis.SegundosPraExecutar + 3, () => { b.Ficha.Ki = 0; esperou |= _bills.Sessao is { ExecutaAte: > 0 }; return _bills.Sessao == null; });
			AfirmarBw("NO DESAFIO, QUEM CAI NO PONTO DO HAKAI E APAGADO: o juiz espera, o cerebro do Bills pede o HAKAI, o desafiante MORRE com a marca -- e o duelo acaba pela morte, com a fala dele e o titulo defendido",
					  esperou && _bills.Sessao == null && d1.Ficha.dead && d1.Ficha.hakai_mark && Disse(avisos, "HAKAI!")
					  && EscutaDeFalas.Any(e => e.Quem == b.Id && e.Texto == BillsEWhis.BillsApagouODesafiante) && EscutaDasSagas.Any(t => t.Contains("defendeu")),
					  $"esperou={esperou}, morto={d1.Ficha.dead}, marca={d1.Ficha.hakai_mark} | {string.Join(" | ", EscutaDeFalas.Where(e => e.Quem == b.Id).Select(e => e.Texto))}");
			AfirmarBw("...o trono continua vago, e o Bills volta a ser objeto: sem Fury, sem kit, no posto",
					  TronoVago(BillsEWhis.CargoDoBills) && _bills.Modo == ModoDivino.Parado && !EhDeusDaDestruicao(b) && b.Forma.Atual != BillsEWhis.IdDaFury && (b.Pos - _bills.Posto).Length < 1);

			BillsEWhis.HakaiDesligadoDeTeste = true;
			try
			{
				ServerPlayer d2 = Desafiante("Desafiante2");
				ComandoDeInteracao(d2, BillsEWhis.VerboDesafiar, "");
				BwLutar(Duelo.Contagem / 1000.0 + 0.5, () => _bills.Sessao is { Lutando: true });
				bool semHakai = SemHakai(b);
				string semEsfera = BwArsenal(b);
				Enfraquecer(d2);
				TorsoEm(d2, 0.10);
				d2.Combate.Nocautear(30);
				EscutaDeFalas.Clear();
				BwLutar(0.3, () => _bills.Sessao == null);
				AfirmarBw("(a chave `HakaiNoDesafio` virada) a MESMA queda sem o Hakai: o duelo acaba no nocaute, do jeito de sempre -- o desafiante perde, e sai VIVO e inteiro; a esfera nem estava no arsenal",
						  semHakai && !semEsfera.Contains(KitDoDeus.VerbEsfera) && semEsfera.Contains(KitDoDeus.VerbFuria) && _bills.Sessao == null
						  && !d2.Ficha.dead && !d2.Ficha.KO && d2.Ficha.HP >= 99 && !d2.Ficha.hakai_mark
						  && EscutaDeFalas.Any(e => e.Quem == b.Id && e.Texto == BillsEWhis.BillsVenceuODesafio),
						  $"morto={d2.Ficha.dead}, HP {d2.Ficha.HP:0.0}, arsenal [{semEsfera}]");
			}
			finally { BillsEWhis.HakaiDesligadoDeTeste = false; }
		}
		finally
		{
			BillsEWhis.HakaiDesligadoDeTeste = BillsEWhis.KitNoTreinoDeTeste = false;
			EscutaDeAvisos = avisosDeAntes;
			if (_bills.Sessao != null) EncerrarASessao(_bills, FimDaSessao.MestreSumiu);
			if (_whis.Sessao != null) EncerrarASessao(_whis, FimDaSessao.MestreSumiu);
			foreach (ServerPlayer c in cena) { _desafiosPerdidos.Remove(c.Assinatura); BwRecolher(c); forjados.Remove(c); }
			BwLimparOsTiros(zona);

			// OS DOIS NOS POSTOS, pro resto da bancada (o Whis volta andando; se empacou, a caminhada dele mesma o poe la).
			for (int i = 0; i < 30 * 16 && _whis.Corpo is { } de && (de.Moving || (de.Pos - _whis.Posto).Length >= 1); i++) TickDeBills(Protocol.TickSeconds);
			pl.Combate.Curar();
			pl.Combate.SincronizarVida();
		}
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
		AfirmarBw("...e fora do desafio ele NAO carrega o kit (a consagracao durou o duelo): parado em casa ele e objeto, e no treino o poder e o cravado, sem o surto do God's Temper",
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
