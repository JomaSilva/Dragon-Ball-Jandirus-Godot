using System.Text.Json;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Items;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// BILLS E WHIS NO MUNDO -- a visita a Terra, a casa deles, quem existe e quem fala (dono, 2026-10-11).
///
/// Os numeros, a escolha do planeta e as falas moram em <see cref="BillsEWhis"/> (Core). Aqui mora o que e do
/// servidor: o estado que vai pro disco, os dois corpos, o calendario, a luta na Terra e os dois pacotes. As lutas
/// 1 contra 1 da casa (treino e desafio pelo titulo) estao em `GameServer.BillsTreino.cs`.
///
/// ============================ OS CORPOS SAO DEDUZIDOS, E NAO GUARDADOS ============================
/// O disco guarda so a FASE da historia (`visita-de-bills.json`). Onde cada um deveria estar e funcao de tres
/// perguntas feitas de segundo em segundo (<see cref="GarantirOsDivinos"/>):
///
///   * o cargo DELE esta vago? (<see cref="BillsEWhis.Existe"/> -- o Bills pelo trono do Deus da Destruicao, o
///     Whis pelo do Anjo; cada um sozinho)
///   * em que fase a historia esta? (na Terra, em casa, ou em lugar nenhum)
///   * o lugar existe agora? (a Terra de pe; o mundo da casa gerado)
///
/// Corpo que deveria estar e nao esta NASCE; corpo que nao deveria estar SAI. E isso que faz o reinicio, a parada
/// do servidor no mesmo processo e a limpeza total darem no mesmo: os tres somem com os corpos, e o tique seguinte
/// os poe de volta onde a fase manda -- nao ha "retomar" pra ninguem esquecer de chamar.
/// ==================================================================================================
///
/// ============================ PARADOS, ELES SAO ESTATUA ============================
/// Corpo sem `Cerebro` nao e dirigido por ninguem (`TickDosCorposSemDono`), entao parado e literal: sem mente,
/// sem rotina e sem passeio. E INTOCAVEL pelo gancho que a cinematica de transformacao ja usa
/// (`CombatState.EmCinematica`, lido pelos dezoito leitores de `Intocavel`): golpe, raio, agarrao e dano em area
/// nao os alcancam. A unica hora em que da pra bater no Bills sem pedir e a visita a Terra -- e pra isso que ele
/// foi.
/// ====================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// 0. O ESTADO
	// =====================================================================

	/// <summary>
	/// O arquivo da visita, ao lado dos outros arquivos de mundo. COM HIFEN de proposito: o nome de arquivo de uma
	/// conta nunca tem hifen (`AccountStore.Arquivo` troca tudo que nao e letra ou numero por `_`), entao conta
	/// nenhuma -- nem uma "bills" que ja exista -- alcanca este nome.
	/// </summary>
	internal const string NomeDoArquivoDaVisita = "visita-de-bills.json";

	private string CaminhoDaVisita => System.IO.Path.Combine(_store?.Pasta ?? ".", NomeDoArquivoDaVisita);

	private EstadoDaVisita _visita = new();

	/// <summary>O planeta que ele visita (o nome da zona no manifesto dos mapas).</summary>
	private static readonly ZoneKey TerraDaVisita = ZoneKey.Premade("Earth");

	/// <summary>Como um dos dois esta no mundo agora.</summary>
	private enum ModoDivino : byte
	{
		/// <summary>Estatua intocavel, com o menu da tecla E quando esta em casa.</summary>
		Parado = 0,

		/// <summary>O Bills na Terra, dormindo: estatua, mas da pra bater -- e o convite.</summary>
		Cochilando,

		/// <summary>O Bills na Terra, acordado por quem bateu nele.</summary>
		Lutando,

		/// <summary>Falando um roteiro (a derrota, a coroacao): parado e intocavel ate a ultima linha.</summary>
		EmCena,

		/// <summary>Numa luta 1 contra 1 da casa. Ver `GameServer.BillsTreino.cs`.</summary>
		NaSessao,
	}

	/// <summary>Um dos dois: quem e, o corpo dele no mundo (se houver) e onde fica o posto dele.</summary>
	private sealed class CorpoDivino(QuemFala quem, string molde, string cargo)
	{
		public readonly QuemFala Quem = quem;
		public readonly string Molde = molde, Cargo = cargo;

		public ServerPlayer? Corpo;
		public ModoDivino Modo;
		public ZoneKey Zona;
		public Vec2 Posto;
		public SessaoDivina? Sessao;

		/// <summary>Quando ele pode falar sozinho de novo com a visita (relogio do sistema, em segundos).</summary>
		public double ProximaRecepcao;
	}

	private readonly CorpoDivino _bills = new(QuemFala.Bills, BillsEWhis.MoldeDoBills, BillsEWhis.CargoDoBills);
	private readonly CorpoDivino _whis = new(QuemFala.Whis, BillsEWhis.MoldeDoWhis, BillsEWhis.CargoDoWhis);

	/// <summary>
	/// O RELOGIO DESTE SISTEMA, em segundos: o `dt` acumulado do tique. E o de todos os prazos daqui (roteiros,
	/// treino, cochilo) -- e nao o de parede, pelo mesmo motivo do passeio do habitante: o de parede nao e
	/// controlavel por bancada nenhuma.
	/// </summary>
	private double _relogioDivino;

	private int _tiquesDivinos;

	/// <summary>Um roteiro falado esta no ar: ninguem nasce, sai ou troca de lugar no meio da fala.</summary>
	private bool _cenaDivina;

	/// <summary>As linhas e os gestos agendados, em ordem de hora (relogio do sistema).</summary>
	private readonly List<(double Em, Action Fazer)> _roteiroDivino = [];

	/// <summary>Quem bateu no Bills na Terra e ainda nao foi derrubado por ele.</summary>
	private readonly HashSet<int> _desafiantesNaTerra = [];

	private double _semPresaNaTerra, _proximaFalaDeDerrubar;

	/// <summary>O que cada jogador ja recebeu no `S2C.CasaDeBills` (a semente da casa; 0 = "nao revelada").</summary>
	private readonly Dictionary<int, (ServerPlayer Quem, ulong Casa)> _casaVistaPor = [];

	/// <summary>A casa que o mundo conhece -- pra anunciar quando ela mudar de lugar.</summary>
	private ulong _casaAnunciada;

	/// <summary>O que cada jogador ja recebeu no `S2C.DivinoPerto`.</summary>
	private readonly Dictionary<int, (ServerPlayer Quem, int Corpo, string Tipo)> _divinoApontado = [];

	/// <summary>O que o sistema anunciou e mandou -- ligado so pela bancada (`--billsteste`).</summary>
	internal static List<(int Para, byte[] Fio)>? EscutaDeBills;

	/// <summary>`--billsdias N`: a manivela do dono pra nao esperar o mes inteiro (0 = pousa assim que Boo cair).</summary>
	private static readonly int DiasDeEsperaPelaFlag = LerOsDiasDaFlag();

	private static int LerOsDiasDaFlag()
	{
		string[] args = OS.GetCmdlineArgs();
		int i = Array.IndexOf(args, "--billsdias");
		return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int n) && n >= 0 ? n : -1;
	}

	// =====================================================================
	// 1. O DISCO
	// =====================================================================

	/// <summary>
	/// LE A VISITA DO DISCO. Sem arquivo e a historia adormecida -- e ele NAO grava nada aqui: o primeiro boot e o
	/// boot depois de uma limpeza total tem de deixar a pasta igual, e o arquivo so nasce quando a fase muda.
	/// </summary>
	private void CarregarBills()
	{
		ZerarBills();
		if (DiasDeEsperaPelaFlag >= 0)
		{
			BillsEWhis.EsperaDeTeste = DiasDeEsperaPelaFlag;
			GD.Print($"[server] BANCADA: o Bills pousa {DiasDeEsperaPelaFlag} dia(s) in-game depois de Boo cair (--billsdias)");
		}

		try
		{
			if (System.IO.File.Exists(CaminhoDaVisita))
				_visita = JsonSerializer.Deserialize<EstadoDaVisita>(
					System.IO.File.ReadAllText(CaminhoDaVisita),
					new JsonSerializerOptions { IncludeFields = true }) ?? new EstadoDaVisita();
		}
		catch (Exception e) { GD.PushWarning($"[server] {NomeDoArquivoDaVisita} ilegivel: {e.Message}"); }

		GD.Print($"[server] Bills e Whis: a historia esta em '{_visita.Fase}'"
			   + (_visita.Fase == FaseDaVisita.ACaminho ? $" (pousa no dia {_visita.DiaDaChegada}; hoje e {DiaDoMundo})" : ""));
	}

	private void SalvarBills()
	{
		// O PORTAO DA LIMPEZA: no meio dela nada grava (ela apaga a pasta por ultimo, e um arquivo escrito aqui
		// sobraria como "mundo velho").
		if (_store == null || _limpezaEmCurso) return;
		try
		{
			string tmp = CaminhoDaVisita + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(_visita,
				new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDaVisita, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra salvar {NomeDoArquivoDaVisita}: {e.Message}"); }
	}

	/// <summary>
	/// A LIMPEZA TOTAL: a historia volta a dormir e tudo o que e de sessao e esquecido. SO MEMORIA -- quem apaga o
	/// arquivo e a limpeza (a pasta inteira), e um zerador que gravasse poria arquivo num mundo recem-nascido.
	/// Os corpos nao sao tirados daqui: a limpeza ja esvaziou o mundo, e o tique os da por perdidos.
	/// </summary>
	private void ZerarBills()
	{
		// QUEM AINDA ESTIVER NO MUNDO SAI DELE (so memoria: `RemoverNpc` nao grava nada). A limpeza normalmente ja
		// esvaziou o mundo antes de chegar aqui; a recarga do disco das bancadas, nao.
		foreach (CorpoDivino c in (CorpoDivino[])[_bills, _whis])
		{
			SoltarOParceiro(c);
			if (c.Corpo is { } npc && _players.TryGetValue(npc.Id, out ServerPlayer? vivo) && ReferenceEquals(vivo, npc))
				RemoverNpc(npc);
			c.Corpo = null;
			c.Modo = ModoDivino.Parado;
		}

		_visita = new EstadoDaVisita();
		EsquecerACenaDivina();
		_desafiosPerdidos.Clear();
		_casaAnunciada = 0;
		_casaVistaPor.Clear();
		_divinoApontado.Clear();
		_postos = null;
		_cientesDoRumo.Clear();
	}

	/// <summary>Larga o que e de CENA: o roteiro no ar e a briga da Terra. Nao toca em corpo, em fase nem em disco.</summary>
	private void EsquecerACenaDivina()
	{
		_roteiroDivino.Clear();
		_cenaDivina = false;
		_desafiantesNaTerra.Clear();
		_semPresaNaTerra = 0;
	}

	/// <summary>
	/// O CORPO QUE EU ANOTEI AINDA E O DO MUNDO? A limpeza total e a parada do servidor esvaziam `_players` por
	/// fora, e id de corpo se reusa -- por isso a pergunta e pelo OBJETO, e nao pelo numero. Devolve `true` quando
	/// ele tinha sumido (e ja foi esquecido).
	/// </summary>
	private bool DarPorPerdido(CorpoDivino c)
	{
		if (c.Corpo is not { } npc) return false;
		if (_players.TryGetValue(npc.Id, out ServerPlayer? vivo) && ReferenceEquals(vivo, npc)) return false;

		GD.Print($"[server] {c.Molde}: o corpo saiu do mundo por fora -- ele renasce onde a fase mandar");
		SoltarOParceiro(c);
		c.Corpo = null;
		c.Modo = ModoDivino.Parado;
		return true;
	}

	// =====================================================================
	// 2. O TIQUE
	// =====================================================================

	/// <summary>
	/// O TIQUE DE BILLS E WHIS -- no tique cheio (30 Hz), logo depois dos relogios do corpo. A parte lenta (o
	/// calendario, quem existe, a casa no fio) anda de segundo em segundo; a parte rapida e so das lutas, e num
	/// servidor sem luta nenhuma ela e duas comparacoes de campo.
	///
	/// DEPOIS do `TickDosRelogiosDoCorpo` de proposito: e la que a precisao do Instinto drena, e e aqui que a do
	/// Whis volta ao cheio -- o golpe seguinte ja o encontra a 100%.
	/// </summary>
	private void TickDeBills(double dt)
	{
		if (BillsDeTeste) DispararABancadaDoBills();   // a bancada `--billsteste` (so com a flag) -- ver `GameServer.BillsTeste.cs`

		// UM ERRO AQUI NAO PODE MATAR O TIQUE DO SERVIDOR -- nem calar: e a mesma licao da cadeia de sagas.
		try
		{
			_relogioDivino += dt;
			_tiquesDivinos++;

			if (_tiquesDivinos % TicksPorSegundo == 0)
			{
				PassoDaVisitaDeBills();
				GarantirOsDivinos();
				ContarACasaAQuemFalta();
				ReceberAsVisitas();
				DizerORumoAQuemChega();
			}

			TocarORoteiroDivino();
			GuiarOBillsNaTerra(dt);
			GuiarAsSessoesDivinas(dt);

			if (_tiquesDivinos % TicksEntreFatos == 0) ApontarODivinoPerto();
		}
		catch (Exception e) { GD.PushError($"[server] o tique de Bills e Whis quebrou: {e}"); }
	}

	// =====================================================================
	// 3. A LINHA DO TEMPO
	// =====================================================================

	private bool TronoVago(string cargo) => !_tronos.TryGetValue(cargo, out string? dono) || dono.Length == 0;

	private bool BillsExiste => BillsEWhis.Existe(TronoVago(BillsEWhis.CargoDoBills));
	private bool WhisExiste => BillsEWhis.Existe(TronoVago(BillsEWhis.CargoDoWhis));

	private bool TerraDePe => _catalogo?.Get(TerraDaVisita) != null && !ZonaMorta(TerraDaVisita);

	/// <summary>
	/// O CALENDARIO: a saga de Boo vencida acorda o Bills, e um mes in-game depois ele pousa. A conta e do Core
	/// (<see cref="BillsEWhis.Passo"/>); aqui se le o mundo, se grava e se anuncia.
	/// </summary>
	private void PassoDaVisitaDeBills()
	{
		bool booVencido = _sagas.Do(BillsEWhis.SagaQueOAcorda) is { Fase: FaseDaSaga.Vencida };

		switch (BillsEWhis.Passo(_visita, booVencido, DiaDoMundo, TerraDePe))
		{
			case PassoDaVisita.Despertou:
				SalvarBills();
				AnunciarNoMundo(BillsEWhis.AvisoDoDespertar);
				GD.Print($"[server] BILLS: Boo caiu -- ele pousa na Terra no dia {_visita.DiaDaChegada} (hoje e {DiaDoMundo})");
				break;

			case PassoDaVisita.Pousou:
				SalvarBills();
				PousarNaTerra();
				break;
		}

		// A VISITA SEM O BILLS NAO TEM COMO ACABAR: so um admin tira o titulo dele das maos (o trono esta vago
		// enquanto ele existe), e ai nao ha quem derrubar. Ela acaba aqui, sem luta, e a casa fica aberta.
		if (_visita.Fase == FaseDaVisita.NaTerra && !BillsExiste && !_cenaDivina) EncerrarAVisitaSemLuta();
	}

	/// <summary>O dia em que ele chega: os dois nascem na Terra e o mundo fica sabendo.</summary>
	private void PousarNaTerra()
	{
		GarantirOsDivinos();

		// SEM O BILLS NAO HA CHEGADA PRA ANUNCIAR: o titulo dele ganhou dono antes da visita (so um admin consegue,
		// porque sem ele no mundo nao ha quem desafiar). Quem fecha a historia e o `EncerrarAVisitaSemLuta`, no mesmo
		// passo do calendario -- e o Whis, se existir, conta onde fica a casa.
		if (_bills.Corpo is not { } b)
		{
			GD.Print($"[server] BILLS: o dia da visita chegou (dia {DiaDoMundo}) e ele nao existe -- o trono dele tem dono");
			return;
		}

		AnunciarNoMundo(BillsEWhis.AvisoDaChegada);

		// QUEM ESTA NA TERRA GANHA O RUMO: e um mapa de 500 tiles, e "ele pousou" sem dizer onde mandaria o
		// planeta inteiro procurar no escuro. E a mesma frase de rumo do vilao avulso.
		foreach (ServerPlayer p in ZoneList(TerraDaVisita.Hash))
		{
			if (p.Peer == null) continue;
			Avisar(p, $"Bills pousou {RumoDe(p.Pos, b.Pos)}.");
			_cientesDoRumo[p.Id] = (p, TerraDaVisita.Hash);   // ja ouviu: o rumo de "quem chega" nao e pra ele
		}

		double em = 1.5;
		foreach (Fala f in BillsEWhis.FalasDaChegada)
		{
			Fala linha = f;
			Agendar(em, () => DizerDivino(linha.Quem, linha.Texto));
			em += BillsEWhis.SegundosEntreFalas;
		}
		GD.Print($"[server] BILLS pousou na Terra (dia {DiaDoMundo}) em ({b.Pos.X:0},{b.Pos.Y:0}) | BP {b.Ficha.BP:N0}");
	}

	// =====================================================================
	// 4. QUEM EXISTE, E ONDE
	// =====================================================================

	/// <summary>A CASA DE AGORA: a mais pesada da lista do universo que ainda nao foi destruida.</summary>
	private CasaDeBills? CasaAtual() => BillsEWhis.Casa(SeedDoUniverso, PlanetaMorto);

	/// <summary>
	/// ONDE OS DOIS DEVERIAM ESTAR AGORA -- a zona e o posto de cada um. Nulo quando nao ha lugar: fase sem corpo,
	/// a Terra destruida, ou o mundo da casa ainda nascendo (ele e ENCOMENDADO aqui, e a resposta vem nos tiques
	/// seguintes -- gerar mil por mil tiles nao cabe num tique).
	/// </summary>
	private (ZoneKey Zona, Vec2 Bills, Vec2 Whis)? LugarDosDivinos()
	{
		const float t = ZoneCollision.TileSize;
		float lado = BillsEWhis.TilesEntreOsDois * t;

		switch (_visita.Fase)
		{
			case FaseDaVisita.NaTerra:
			{
				if (!TerraDePe || MapaDaZonaOuCatalogo(TerraDaVisita) is not { } mapa) return null;
				if (_postos is { } ja && ja.Zona == TerraDaVisita.Hash) return (TerraDaVisita, ja.Bills, ja.Whis);

				(Vec2 b, Vec2 w) = PostosLadoALado(mapa, PontoDeHabitante(TerraDaVisita, BillsEWhis.LugarNaTerra), null);
				_postos = (TerraDaVisita.Hash, b, w);
				return (TerraDaVisita, b, w);
			}

			case FaseDaVisita.EmCasa:
			{
				if (CasaAtual() is not { } casa) return null;
				ZoneKey zona = Espaco.ZonaDe(casa.Corpo);
				if (MundoDaZona(zona, casa.Corpo) is not { } viva) return null;
				if (_postos is { } ja && ja.Zona == zona.Hash) return (zona, ja.Bills, ja.Whis);

				// ALGUNS PASSOS AO NORTE DO POUSO: todo mundo que chega cai no mesmo tile, e eles nao podem estar nele.
				(Vec2 b, Vec2 w) = PostosLadoALado(viva.Colisao,
					new Vec2(viva.Chegada.X - lado / 2, viva.Chegada.Y - BillsEWhis.TilesDoPouso * t), viva.Chegada);
				_postos = (zona.Hash, b, w);
				return (zona, b, w);
			}

			default:
				return null;
		}
	}

	/// <summary>
	/// Os postos ja escolhidos pra zona em que eles estao: a escolha e uma varredura (ate uns milhares de celulas
	/// num terreno fechado) e este metodo e perguntado de segundo em segundo. Esquecido com a historia (`ZerarBills`).
	/// </summary>
	private (ulong Zona, Vec2 Bills, Vec2 Whis)? _postos;

	/// <summary>DEFEITO INJETADO (bancada `--billsteste`): o posto do Whis volta a ser "o ponto livre mais perto do lado do Bills". Falso em jogo, sempre.</summary>
	internal bool PostoDoWhisPeloPontoLivreDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--billsteste`): os postos nao procuram clareira -- a primeira fileira firme serve, mesmo atras de uma arvore. Falso em jogo, sempre.</summary>
	internal bool PostoSemClareiraDeTeste;

	/// <summary>
	/// OS DOIS POSTOS NUM MAPA, do melhor pro que houver -- sempre o mais perto do ponto pedido, e nunca em cima do pouso:
	///
	///   1. numa CLAREIRA: duas celulas de chao firme na mesma fileira, a <see cref="BillsEWhis.TilesEntreOsDois"/>
	///      uma da outra, com chao firme entre elas e na fileira de baixo (onde o jogador para pra falar com eles),
	///      e nada que barre na janela da copa (<see cref="BillsEWhis.ClareiraColunas"/> pra cada lado,
	///      <see cref="BillsEWhis.ClareiraFileiras"/> pra baixo);
	///   2. sem clareira no raio da busca, a mesma fileira firme, sem a exigencia da janela;
	///   3. sem fileira nenhuma (um mundo so de agua), o ponto livre mais perto.
	///
	/// AS DUAS EXIGENCIAS FORAM MEDIDAS, uma por rodada da `--diagbills` (2026-10-11). "Um ponto livre perto do lado
	/// dele": o pouso caiu numa lingua de areia entre dois lagos, o tile do lado do Bills era agua, e o ponto livre
	/// mais perto era a celula na DIAGONAL do proprio Bills -- os dois ficaram quase um em cima do outro, e a tecla
	/// E de quem parava "ao lado do Whis" abria o menu do Bills. "A primeira fileira firme": caiu atras de um
	/// pinheiro, e a foto dos dois parados em casa so tinha o Whis -- a copa cobria o Bills inteiro.
	/// </summary>
	private (Vec2 Bills, Vec2 Whis) PostosLadoALado(ZoneCollision mapa, Vec2 desejado, Vec2? pouso)
	{
		const float t = ZoneCollision.TileSize;
		int passo = (int)MathF.Round(BillsEWhis.TilesEntreOsDois);
		int cx0 = (int)MathF.Floor(desejado.X / t), cy0 = (int)MathF.Floor(desejado.Y / t);
		float folga = BillsEWhis.FolgaDoPousoTiles * t;

		if (!PostoDoWhisPeloPontoLivreDeTeste)
			for (int exigencia = PostoSemClareiraDeTeste ? 1 : 0; exigencia <= 1; exigencia++)
				for (int r = 0; r <= BillsEWhis.BuscaDoPostoTiles; r++)
					for (int dy = -r; dy <= r; dy++)
						for (int dx = -r; dx <= r; dx++)
						{
							if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;   // so a borda do anel: o miolo ja foi olhado

							int cx = cx0 + dx, cy = cy0 + dy;
							bool firme = true;
							for (int i = 0; i <= passo && firme; i++) firme = mapa.ServeDeChao(cx + i, cy);
							if (!firme) continue;
							if (exigencia == 0 && !Clareira(mapa, cx, cy, passo)) continue;

							Vec2 b = mapa.CentroDaCelula(cx, cy), w = mapa.CentroDaCelula(cx + passo, cy);
							if (pouso is { } p && ((b - p).Length < folga || (w - p).Length < folga)) continue;
							return (b, w);
						}

		Vec2 so = mapa.PontoLivrePerto(desejado);
		return (so, mapa.PontoLivrePerto(new Vec2(so.X + passo * t, so.Y)));
	}

	/// <summary>
	/// A fileira `cx..cx+passo` de `cy` esta numa CLAREIRA? Chao firme na fileira de baixo (e ali que o jogador para),
	/// e nenhuma celula que barre na janela da copa. Ver <see cref="PostosLadoALado"/>.
	/// </summary>
	private static bool Clareira(ZoneCollision mapa, int cx, int cy, int passo)
	{
		for (int i = 0; i <= passo; i++)
			if (!mapa.ServeDeChao(cx + i, cy + 1)) return false;

		for (int dy = 0; dy <= BillsEWhis.ClareiraFileiras; dy++)
			for (int dx = -BillsEWhis.ClareiraColunas; dx <= passo + BillsEWhis.ClareiraColunas; dx++)
				if (mapa.BlockedCell(cx + dx, cy + dy)) return false;
		return true;
	}

	/// <summary>
	/// O LACO "EXISTE &lt;=&gt; CARGO VAGO", de segundo em segundo. Idempotente e derivado -- ver o cabecalho.
	/// </summary>
	private void GarantirOsDivinos()
	{
		// O MUNDO FOI ESVAZIADO POR FORA (a parada do servidor no mesmo processo, a limpeza total): o que era de
		// cena morre junto, como morreria num reinicio de verdade -- um roteiro que continuasse falando depois de
		// o servidor "voltar" seria a diferenca entre parar e fechar.
		if (DarPorPerdido(_bills) | DarPorPerdido(_whis)) EsquecerACenaDivina();

		// NO MEIO DE UMA FALA ninguem nasce nem sai: a derrota e a coroacao mudam a fase (ou o trono) ANTES de os
		// dois se despedirem, e sem esta linha eles sumiriam com a frase pela metade.
		if (_cenaDivina) return;

		(ZoneKey Zona, Vec2 Bills, Vec2 Whis)? lugar = LugarDosDivinos();
		Garantir(_bills, BillsExiste, lugar?.Zona, lugar?.Bills);
		Garantir(_whis, WhisExiste, lugar?.Zona, lugar?.Whis);
	}

	private void Garantir(CorpoDivino c, bool existe, ZoneKey? zona, Vec2? posto)
	{
		bool deve = existe && zona != null && posto != null;
		if (c.Corpo != null && (!deve || c.Zona.Hash != zona!.Value.Hash)) RetirarODivino(c);
		if (deve && c.Corpo == null) NascerODivino(c, zona!.Value, posto!.Value);
	}

	/// <summary>
	/// POE UM DOS DOIS NO MUNDO -- pela <see cref="NascerNpc"/> de todo corpo sem dono, e o que ele ganha depois e
	/// o que so um deus tem: nao perde membro, nao morre (o golpe que mataria o DERRUBA) e, parado, nao e tocado.
	/// </summary>
	private void NascerODivino(CorpoDivino c, ZoneKey zona, Vec2 posto)
	{
		// O "lugar" entra na semente do corpo: um numero por personagem, e a ficha sai igual em todo reinicio.
		ulong lugar = BillsEWhis.LugarNaTerra + (c.Quem == QuemFala.Whis ? 1UL : 0UL);
		ServerPlayer? npc = NascerNpc(c.Molde, zona, posto, lugar);
		if (npc?.Papel == null)
		{
			GD.PushError($"[server] o molde '{c.Molde}' nao nasceu -- confira o npcs.json");
			return;
		}

		c.Corpo = npc;
		c.Zona = zona;
		c.Posto = posto;
		c.Sessao = null;
		c.ProximaRecepcao = _relogioDivino + 8;

		npc.Combate.MembrosFirmes = true;
		npc.Combate.Letal = false;   // ele derruba; nao mata (e o padrao de todo corpo sem dono, escrito aqui pra ficar dito)

		// INTOCAVEL ENQUANTO PARADO -- pendurado no gancho que a cinematica ja usa, e por CIMA do que o
		// `PrepararCombate` instalou (a cena de forma continua valendo: a Fury tem uma).
		Func<bool>? cena = npc.Combate.EmCinematica;
		npc.Combate.EmCinematica = () => DivinoIntocavel(c) || cena?.Invoke() == true;

		// O GOLPE QUE MATARIA O DERRUBA. A morte de um deus pela mao de um mortal nao esta no pedido -- o que
		// esta e "caso os players derrotem ele" --, e um corpo morto nao se levanta pra dizer as coordenadas.
		npc.Combate.NegarMorte = DerrubarEmVezDeMorrer;

		// NA TERRA o Bills dorme e luta com os 15%; em casa (e o Whis, sempre) o poder de repouso e o inteiro --
		// e ninguem o mede em luta: la o que vale e o que a luta cravar (o triplo de quem treina, ou o trilhao).
		bool billsNaTerra = c.Quem == QuemFala.Bills && zona.Equals(TerraDaVisita);
		ParOuDespertar(c, billsNaTerra ? ModoDivino.Cochilando : ModoDivino.Parado);
		CravarOPoder(npc, billsNaTerra ? BillsEWhis.PoderNaTerra : BillsEWhis.PoderTotal);
		RestaurarODivino(npc);

		GD.Print($"[server] {npc.Name} esta em {zona} ({posto.X:0},{posto.Y:0}) | {c.Modo} | BP {npc.Ficha.BP:N0}");
	}

	/// <summary>Tira um dos dois do mundo (o cargo ganhou dono, ou a fase mudou de lugar), encerrando a luta que houver.</summary>
	private void RetirarODivino(CorpoDivino c)
	{
		if (c.Corpo is not { } npc) return;
		if (c.Sessao != null) EncerrarASessao(c, FimDaSessao.MestreSumiu);
		c.Corpo = null;
		c.Modo = ModoDivino.Parado;
		if (c == _bills) { _desafiantesNaTerra.Clear(); _semPresaNaTerra = 0; }
		RemoverNpc(npc);
		GD.Print($"[server] {npc.Name} saiu de {c.Zona}");
	}

	/// <summary>O dono deste corpo entre os dois, ou nulo.</summary>
	private CorpoDivino? DivinoDe(ServerPlayer? p) =>
		p == null ? null : ReferenceEquals(_bills.Corpo, p) ? _bills : ReferenceEquals(_whis.Corpo, p) ? _whis : null;

	/// <summary>
	/// ESTE CORPO NAO PODE SER ATINGIDO AGORA? Parado ou falando, nunca. O Bills que cochila na Terra PODE (e o
	/// convite); quem luta, pode; e o desafiado so depois da contagem.
	/// </summary>
	private static bool DivinoIntocavel(CorpoDivino c) => c.Modo switch
	{
		ModoDivino.Parado or ModoDivino.EmCena => true,
		ModoDivino.NaSessao => c.Sessao is not { Lutando: true },
		_ => false,
	};

	/// <summary>
	/// A MORTE NEGADA VIRA NOCAUTE. O corpo fica com cada membro num fio (o mesmo piso de 5% do seguro da Aura of
	/// Destruction) e cai -- e cair e a derrota que este sistema le.
	/// </summary>
	private static bool DerrubarEmVezDeMorrer(CombatState cs)
	{
		foreach (BodyPart p in cs.Corpo.Partes)
		{
			if (p.Decepado) continue;
			double piso = p.VidaMax * 0.05;
			if (p.Vida < piso) p.Vida = piso;
		}
		cs.SincronizarVida();
		if (!cs.F.KO) cs.Nocautear(BillsEWhis.SegundosNoChao * 4, porVital: true);
		return true;
	}

	/// <summary>Troca o modo, e com ele a mente: so quem luta tem cerebro.</summary>
	private void ParOuDespertar(CorpoDivino c, ModoDivino modo)
	{
		c.Modo = modo;
		if (c.Corpo is not { } npc) return;

		bool luta = modo is ModoDivino.Lutando or ModoDivino.NaSessao;
		if (!luta)
		{
			npc.Cerebro = null;
			npc.Moving = false;
			if (npc.Papel != null) npc.Papel.PresaDoRoteiro = 0;
		}
		else if (npc.Cerebro == null && npc.Papel != null) npc.Cerebro = CerebroDeNascimento(npc.Papel);
	}

	/// <summary>
	/// CRAVA O PODER BASE do corpo -- no `Papel` (que o `TickDoRoteiro` reancora de segundo em segundo, como o de
	/// todo chefe) e na ficha, na hora.
	/// </summary>
	private static void CravarOPoder(ServerPlayer npc, double bpBase)
	{
		if (npc.Papel is { } papel)
		{
			if (papel.BpsPinados.Length == 0) papel.BpsPinados = [bpBase];
			else papel.BpsPinados[0] = bpBase;
		}
		if (Math.Abs(npc.Ficha.BP - bpBase) <= bpBase * 0.0005) return;

		npc.Ficha.BP = bpBase;
		npc.Ficha.Statify();
		npc.Ficha.PowerLevel();
		npc.SigAtributos = "";
	}

	/// <summary>De pe, inteiro, descansado e calmo -- e fora de combate.</summary>
	private static void RestaurarODivino(ServerPlayer npc)
	{
		npc.Combate.Curar();
		npc.Combate.Stun = 0;
		npc.Combate.EmCombate = 0;
		npc.Combate.LutandoDeVerdade = 0;
		npc.Combate.BrigandoComAlguem = 0;
		SorteioDeNpc.Normalizar(npc.Ficha);
		npc.Combate.SincronizarVida();
		npc.UltimoAgressor = 0;
		npc.RancorAte = 0;
		npc.PresaEngajada = 0;
		npc.SigAtributos = "";
	}

	/// <summary>De volta ao posto (num passo so: deus nao anda de volta pra casa), virado pra frente.</summary>
	private static void VoltarAoPosto(CorpoDivino c)
	{
		if (c.Corpo is not { } npc) return;
		npc.Pos = c.Posto;
		npc.Moving = false;
		npc.Facing = Jandirus.Core.World.Facing.South;
	}

	// =====================================================================
	// 5. O ROTEIRO FALADO
	// =====================================================================

	/// <summary>Agenda um gesto pra daqui a tantos segundos (relogio do sistema).</summary>
	private void Agendar(double segundos, Action fazer)
	{
		double em = _relogioDivino + Math.Max(segundos, 0);
		int i = _roteiroDivino.Count;
		while (i > 0 && _roteiroDivino[i - 1].Em > em) i--;
		_roteiroDivino.Insert(i, (em, fazer));
	}

	/// <summary>DEFEITO INJETADO (bancada `--billsteste`): o roteiro falado NAO se cala quando a briga comeca -- as linhas da chegada saem por cima dela. Falso em jogo, sempre.</summary>
	internal bool RoteiroNaoSeCalaDeTeste;

	/// <summary>
	/// O ROTEIRO NO AR SE CALA: o que estava agendado e nao saiu, nao sai mais. Pra quando a cena muda por baixo
	/// da fala -- quem bate no Bills no meio da fala de chegada o acorda, e quem o derruba nela ouve a da derrota:
	/// as linhas velhas nao podem sair INTERCALADAS com as novas (medido na `--diagbills`, 2026-10-11: "Enquanto
	/// ninguem se decide, eu cochilo" saiu no meio da despedida).
	///
	/// So e chamado de onde o que esta na fila sao FALAS (o Bills cochilando ou lutando na Terra): os gestos que
	/// nao podem se perder -- a partida, o fim da coroacao -- vivem atras do `_cenaDivina`, e ali nada cala nada.
	/// </summary>
	private void CalarORoteiro()
	{
		if (!RoteiroNaoSeCalaDeTeste) _roteiroDivino.Clear();
	}

	private void TocarORoteiroDivino()
	{
		while (_roteiroDivino.Count > 0 && _roteiroDivino[0].Em <= _relogioDivino)
		{
			Action fazer = _roteiroDivino[0].Fazer;
			_roteiroDivino.RemoveAt(0);
			fazer();
		}
	}

	/// <summary>
	/// UM DOS DOIS FALA -- pelo funil de fala de todo corpo (`Falar`): quem esta a vista le "Bills diz, '...'" no
	/// chat e ve o balao sobre a cabeca dele. Se o corpo nao esta no mundo (o cargo dele tem dono), a linha nao e
	/// dita: ninguem fala por ele.
	/// </summary>
	private void DizerDivino(QuemFala quem, string texto, Protocol.Fala canal = Protocol.Fala.Diz)
	{
		if ((quem == QuemFala.Bills ? _bills : _whis).Corpo is not { } corpo) return;
		_ultimaFala.Remove(corpo.Id);   // o teto de 400 ms e contra cliente tagarela; um roteiro nao pode perder linha
		Falar(corpo, canal, texto);
	}

	/// <summary>Uma fala sorteada de uma lista.</summary>
	private string Sortear(string[] falas) => falas[_rng.Next(falas.Length)];

	// =====================================================================
	// 6. A LUTA NA TERRA
	// =====================================================================

	/// <summary>
	/// O BILLS NA TERRA: dorme ate apanhar, luta com quem bateu (e so com quem bateu), e volta a dormir inteiro
	/// quando nao sobra ninguem de pe por perto. Cair e a derrota.
	///
	/// QUEM ELE ENFRENTA E IMPOSTO DAQUI (`PapelDeNpc.PresaDoRoteiro`, o mesmo canal do torneio e do Cell), e nao
	/// deixado pra caca de chefe: um chefe solto caca qualquer jogador a vinte tiles, e a Terra e onde os novatos
	/// nascem. O rancor de NPC pacifico tambem nao serve: ele so se arma no soco (`MarcarAgressao`), e quem
	/// atirasse de longe nunca seria revidado -- aqui se le o `UltimoAgressor`, que o raio tambem escreve.
	/// </summary>
	private void GuiarOBillsNaTerra(double dt)
	{
		if (_visita.Fase != FaseDaVisita.NaTerra || _bills.Corpo is not { } b) return;
		if (_bills.Modo is not (ModoDivino.Cochilando or ModoDivino.Lutando)) return;

		if (b.Ficha.KO) { DerrotaNaTerra(); return; }

		// QUEM BATEU ENTRA NA BRIGA (e quem ja estava nela continua, ate cair ou ir embora). SO GENTE: o pedido e
		// "caso os PLAYERS derrotem ele" -- um bicho bravo ou um vilao de saga que esbarre nele nao o tira do
		// cochilo, e nao vira "quem derrubou o Bills" no anuncio.
		if (b.UltimoAgressor != 0 && _players.TryGetValue(b.UltimoAgressor, out ServerPlayer? quem)
			&& (EhPessoa(quem) || BichoAcordaOBillsDeTeste) && DivinoDe(quem) == null && ValeNaTerra(quem))
			_desafiantesNaTerra.Add(quem.Id);

		ServerPlayer? presa = PresaNaTerra(b);

		if (_bills.Modo == ModoDivino.Cochilando)
		{
			if (presa == null) return;
			ParOuDespertar(_bills, ModoDivino.Lutando);
			_semPresaNaTerra = 0;
			CalarORoteiro();   // acordado no meio da fala de chegada: o resto dela nao sai por cima da briga
			DizerDivino(QuemFala.Bills, Sortear(BillsEWhis.FalasDeAcordar));
			GD.Print($"[server] BILLS acordou: {presa.Name} bateu nele");
		}

		if (presa != null)
		{
			_semPresaNaTerra = 0;
			if (b.Cerebro == null && b.Papel != null) b.Cerebro = CerebroDeNascimento(b.Papel);
			b.Papel!.PresaDoRoteiro = presa.Id;
			return;
		}

		// NINGUEM DE PE NO TERRITORIO: ele para onde esta, e depois de uns segundos volta a dormir INTEIRO.
		b.Papel!.PresaDoRoteiro = 0;
		b.Cerebro = null;
		b.Moving = false;
		_semPresaNaTerra += dt;
		if (_semPresaNaTerra < BillsEWhis.SegundosAteVoltarACochilar) return;

		_desafiantesNaTerra.Clear();
		_semPresaNaTerra = 0;
		VoltarAoPosto(_bills);
		RestaurarODivino(b);
		ParOuDespertar(_bills, ModoDivino.Cochilando);
		DizerDivino(QuemFala.Bills, BillsEWhis.FalaDeVoltarACochilar);
		GD.Print("[server] BILLS voltou a cochilar (ninguem de pe no territorio)");
	}

	/// <summary>DEFEITO INJETADO (bancada `--billsteste`): qualquer corpo do mundo que o acerte entra na briga -- um bicho, um vilao. Falso em jogo, sempre.</summary>
	internal bool BichoAcordaOBillsDeTeste;

	/// <summary>Este corpo esta em condicao de ser enfrentado pelo Bills na Terra: vivo, de pe, na zona e dentro do territorio.</summary>
	private bool ValeNaTerra(ServerPlayer p) =>
		!p.Ficha.dead && !p.Ficha.KO && p.Zone.Hash == _bills.Zona.Hash
		&& (p.Pos - _bills.Posto).Length <= BillsEWhis.TerritorioTiles * ZoneCollision.TileSize;

	/// <summary>
	/// QUEM ELE ENFRENTA AGORA: o ultimo que bateu, se ainda vale; senao o mais perto dos que ja bateram. Quem ele
	/// DERRUBA sai da briga (so volta batendo de novo) -- sem isso, levantar do nocaute ao lado dele seria cair de
	/// novo, sem conseguir ir embora.
	/// </summary>
	private ServerPlayer? PresaNaTerra(ServerPlayer b)
	{
		ServerPlayer? melhor = null;
		float perto = float.MaxValue;
		List<int>? fora = null;

		foreach (int id in _desafiantesNaTerra)
		{
			if (!_players.TryGetValue(id, out ServerPlayer? p) || p.Ficha.dead || p.Zone.Hash != _bills.Zona.Hash)
			{ (fora ??= []).Add(id); continue; }

			if (p.Ficha.KO)
			{
				(fora ??= []).Add(id);
				if (b.UltimoAgressor == id) b.UltimoAgressor = 0;
				AoDerrubarNaTerra(p);
				continue;
			}

			if (!ValeNaTerra(p)) continue;
			if (id == b.UltimoAgressor) return p;

			float d = (p.Pos - b.Pos).LengthSquared;
			if (d < perto) { perto = d; melhor = p; }
		}

		if (fora != null) foreach (int id in fora) _desafiantesNaTerra.Remove(id);
		return melhor;
	}

	private void AoDerrubarNaTerra(ServerPlayer caido)
	{
		GD.Print($"[server] BILLS derrubou {caido.Name}");
		if (_relogioDivino < _proximaFalaDeDerrubar) return;
		_proximaFalaDeDerrubar = _relogioDivino + 6;
		DizerDivino(QuemFala.Bills, Sortear(BillsEWhis.FalasDeDerrubar));
		if (_rng.Next(3) == 0) Agendar(2.2, () => DizerDivino(QuemFala.Whis, BillsEWhis.WhisTranquiliza));
	}

	/// <summary>
	/// ELE CAIU NA TERRA. A fase vira "em casa" NA HORA e vai pro disco antes de qualquer fala -- um servidor que
	/// caisse no meio da despedida nao pode devolver aos jogadores um Bills de pe na Terra. Depois ele se levanta,
	/// diz o que tem a dizer (as coordenadas, entre elas) e os dois vao embora.
	/// </summary>
	private void DerrotaNaTerra()
	{
		if (_bills.Corpo is not { } b) return;

		string heroi = _players.TryGetValue(b.UltimoAgressor, out ServerPlayer? quem) && EhPessoa(quem) && DivinoDe(quem) == null
			? NomeVisivel(quem) : "";

		_visita.Fase = FaseDaVisita.EmCasa;
		_visita.DiaDaChegada = -1;
		_visita.DerrubadoPor = heroi;
		SalvarBills();

		CalarORoteiro();   // o que ele ainda ia dizer (a chegada, um comentario do Whis) da lugar a despedida
		_cenaDivina = true;
		_desafiantesNaTerra.Clear();
		ParOuDespertar(_bills, ModoDivino.EmCena);
		GD.Print($"[server] BILLS FOI DERRUBADO NA TERRA" + (heroi.Length > 0 ? $" por {heroi}" : "") + " -- a casa dele esta aberta");

		(string coordenadas, string mundo) = EnderecoDaCasa();

		Agendar(BillsEWhis.SegundosNoChao, () =>
		{
			if (_bills.Corpo is not { } de) return;
			RestaurarODivino(de);
		});

		double em = BillsEWhis.SegundosNoChao + 1;
		foreach (Fala f in BillsEWhis.FalasDaDerrota)
		{
			Fala linha = f;
			Agendar(em, () => DizerDivino(linha.Quem, BillsEWhis.Preencher(linha.Texto, coordenadas, mundo)));
			em += BillsEWhis.SegundosEntreFalas;
		}

		Agendar(em, () => PartirDaTerra(BillsEWhis.Preencher(
			BillsEWhis.AvisoDaDerrota, coordenadas, mundo, heroi.Length > 0 ? heroi : "alguém")));
	}

	/// <summary>
	/// A VISITA QUE ACABA SEM LUTA: o titulo do Bills ganhou dono (so um admin consegue, com ele na Terra) e nao ha
	/// mais quem derrubar. O Whis, se estiver la, diz onde fica a casa; a fase vira "em casa" do mesmo jeito.
	/// </summary>
	private void EncerrarAVisitaSemLuta()
	{
		_visita.Fase = FaseDaVisita.EmCasa;
		_visita.DiaDaChegada = -1;
		_visita.DerrubadoPor = "";
		SalvarBills();

		(string coordenadas, string mundo) = EnderecoDaCasa();
		CalarORoteiro();
		_cenaDivina = true;
		GD.Print("[server] BILLS: a visita acabou sem luta (o titulo dele tem dono) -- a casa esta aberta");

		DizerDivino(QuemFala.Whis, BillsEWhis.Preencher(BillsEWhis.WhisEncerraAVisita, coordenadas, mundo));
		Agendar(BillsEWhis.SegundosEntreFalas,
				() => PartirDaTerra(BillsEWhis.Preencher(BillsEWhis.AvisoDaVisitaEncerrada, coordenadas, mundo)));
	}

	/// <summary>Os dois saem da Terra, o mundo ouve o endereco e a carta de todo mundo passa a marca-lo.</summary>
	private void PartirDaTerra(string aviso)
	{
		_cenaDivina = false;
		AnunciarNoMundo(aviso);

		// QUEM OUVIU O ANUNCIO JA SABE: a noticia de "quem nao estava la" nao e pra eles.
		ulong casa = CasaAtual()?.Corpo.Seed ?? 0;
		foreach (ServerPlayer p in _players.Values)
			if (EhJogador(p)) p.Ficha.CasaDeBillsOuvida = casa;
		_casaAnunciada = casa;

		GarantirOsDivinos();        // a fase ja e "em casa": os dois saem da Terra agora, e nascem la quando o mundo estiver pronto
		ContarACasaAQuemFalta();
	}

	/// <summary>As coordenadas e o nome do mundo da casa de agora, do jeito que as falas e os anuncios os escrevem.</summary>
	private (string Coordenadas, string Mundo) EnderecoDaCasa() =>
		CasaAtual() is { } casa
			? (BillsEWhis.Coordenadas(casa), NomeDoMundo(casa.Corpo))
			: ("um lugar que não existe mais", "nenhum");

	// =====================================================================
	// 7. A CASA NO FIO, E QUEM NAO ESTAVA LA
	// =====================================================================

	/// <summary>A casa esta REVELADA? So depois de a visita acabar -- antes disso ninguem a conhece, e o pacote nao a leva.</summary>
	private bool CasaRevelada => _visita.Fase == FaseDaVisita.EmCasa || BillsEWhis.CasaVazaDeTeste;

	/// <summary>
	/// CONTA A CASA A QUEM AINDA NAO SABE (de segundo em segundo; so sai pacote quando a resposta muda pra aquele
	/// jogador): e o que faz a carta estelar de todo mundo marcar o planeta, e e por aqui que "quem nao estava la"
	/// fica sabendo -- o login recebe o pacote e, uma vez por personagem, a noticia por escrito.
	///
	/// A CASA PODE MUDAR DE LUGAR: se o planeta deles for destruido, a casa passa a ser o seguinte da lista -- os
	/// corpos renascem la pelo <see cref="GarantirOsDivinos"/>, e o mundo e avisado aqui.
	/// </summary>
	private void ContarACasaAQuemFalta()
	{
		CasaDeBills? casa = CasaRevelada ? CasaAtual() : null;
		ulong semente = casa?.Corpo.Seed ?? 0;

		if (_visita.Fase == FaseDaVisita.EmCasa && semente != 0 && _casaAnunciada != semente && !_cenaDivina)
		{
			if (_casaAnunciada != 0)
			{
				AnunciarNoMundo(BillsEWhis.Preencher(BillsEWhis.AvisoDaMudanca, BillsEWhis.Coordenadas(casa!.Value),
													 NomeDoMundo(casa.Value.Corpo)));
				// QUEM OUVIU O ANUNCIO JA SABE (a mesma regra da partida da Terra).
				foreach (ServerPlayer p in _players.Values)
					if (EhJogador(p)) p.Ficha.CasaDeBillsOuvida = semente;
			}
			_casaAnunciada = semente;
		}

		foreach (ServerPlayer pl in _players.Values)
		{
			if (!EhJogador(pl)) continue;

			if (!_casaVistaPor.TryGetValue(pl.Id, out (ServerPlayer Quem, ulong Casa) visto)
				|| !ReferenceEquals(visto.Quem, pl) || visto.Casa != semente)
			{
				_casaVistaPor[pl.Id] = (pl, semente);
				MandarACasaDeBills(pl, casa);
			}

			// A NOTICIA, UMA VEZ POR PERSONAGEM (e de novo se a casa mudar): quem estava online ja foi marcado.
			if (casa is { } c && _visita.Fase == FaseDaVisita.EmCasa && !_cenaDivina
				&& pl.Ficha.CasaDeBillsOuvida != semente)
			{
				pl.Ficha.CasaDeBillsOuvida = semente;
				Avisar(pl, BillsEWhis.Preencher(BillsEWhis.NoticiaDaCasa, BillsEWhis.Coordenadas(c), NomeDoMundo(c.Corpo)));
			}
		}

		if (_casaVistaPor.Count > _players.Count)
			foreach (int id in _casaVistaPor.Keys.ToList())
				if (!_players.ContainsKey(id)) _casaVistaPor.Remove(id);
	}

	/// <summary>
	/// `S2C.CasaDeBills`: `bool revelada` + (so se revelada) `int sx`, `int sy`, `byte orbita`, `ulong semente do
	/// mundo`. NENHUM numero de poder -- e, antes da revelacao, nenhum endereco.
	/// </summary>
	private static void MandarACasaDeBills(ServerPlayer pl, CasaDeBills? casa)
	{
		var w = Protocol.Begin(Protocol.S2C.CasaDeBills);
		w.Put(casa != null);
		if (casa is { } c)
		{
			w.Put(c.Sx);
			w.Put(c.Sy);
			w.Put((byte)c.K);
			w.Put(c.Corpo.Seed);
		}
		EscutaDeBills?.Add((pl.Id, w.CopyData()));
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Quem ja ouviu o rumo NESTA estada (o jogador e o mundo em que o ouviu). Sair do mundo e voltar ouve de novo.</summary>
	private readonly Dictionary<int, (ServerPlayer Quem, ulong Zona)> _cientesDoRumo = [];

	/// <summary>DEFEITO INJETADO (bancada `--billsteste`): ninguem ouve o rumo ao chegar -- "eles estao aqui", sem dizer onde. Falso em jogo, sempre.</summary>
	internal bool SemRumoAoChegarDeTeste;

	/// <summary>
	/// O RUMO, A QUEM CHEGA no mundo em que eles estao -- uma vez por chegada, de segundo em segundo. O anuncio do
	/// pouso so alcanca quem estava online naquele instante; quem chega depois (e a Terra e onde os novatos nascem)
	/// nao tem como saber pra que lado de um mapa de quinhentos tiles ele cochila. Na casa vale igual: o posto deles
	/// fica onde houver clareira, que pode ser a vinte tiles do pouso.
	/// </summary>
	private void DizerORumoAQuemChega()
	{
		ServerPlayer? ancora = _bills.Corpo ?? _whis.Corpo;
		if (ancora == null || _cenaDivina || _visita.Fase is not (FaseDaVisita.NaTerra or FaseDaVisita.EmCasa))
		{
			if (ancora == null && _cientesDoRumo.Count > 0) _cientesDoRumo.Clear();
			return;
		}

		ulong zona = ancora.Zone.Hash;
		string frase = _bills.Corpo == null ? BillsEWhis.RumoDoWhis
			: _visita.Fase == FaseDaVisita.NaTerra ? BillsEWhis.RumoNaTerra
			: _whis.Corpo == null ? BillsEWhis.RumoDoBills
			: BillsEWhis.RumoDosDois;
		foreach (ServerPlayer pl in ZoneList(zona))
		{
			if (!EhJogador(pl)) continue;
			if (_cientesDoRumo.TryGetValue(pl.Id, out (ServerPlayer Quem, ulong Zona) visto)
				&& ReferenceEquals(visto.Quem, pl) && visto.Zona == zona) continue;

			_cientesDoRumo[pl.Id] = (pl, zona);
			if (!SemRumoAoChegarDeTeste) Avisar(pl, frase.Replace("{rumo}", RumoDe(pl.Pos, ancora.Pos)));
		}

		// QUEM SAIU DO MUNDO (ou do jogo) esquece: voltando, ouve de novo.
		if (_cientesDoRumo.Count > 0)
			foreach (int id in _cientesDoRumo.Keys.ToList())
				if (!_players.TryGetValue(id, out ServerPlayer? p) || !ReferenceEquals(p, _cientesDoRumo[id].Quem) || p.Zone.Hash != zona)
					_cientesDoRumo.Remove(id);
	}

	/// <summary>
	/// OS DOIS RECEBEM A VISITA: parados em casa, cada um diz alguma coisa de vez em quando a quem chega perto. So
	/// enfeite -- mas e o que faz dois sprites parados serem o Bills e o Whis.
	/// </summary>
	private void ReceberAsVisitas()
	{
		if (_visita.Fase != FaseDaVisita.EmCasa || _cenaDivina) return;
		Receber(_whis);
		Receber(_bills);

		void Receber(CorpoDivino c)
		{
			if (c.Corpo is not { } npc || c.Modo != ModoDivino.Parado || _relogioDivino < c.ProximaRecepcao) return;

			float raio = BillsEWhis.TilesDaRecepcao * ZoneCollision.TileSize;
			bool temVisita = false;
			foreach (ServerPlayer p in ZoneList(npc.Zone.Hash))
				if (EhJogador(p) && !p.Ficha.dead && (p.Pos - npc.Pos).Length <= raio) { temVisita = true; break; }
			if (!temVisita) return;

			c.ProximaRecepcao = _relogioDivino + BillsEWhis.SegundosEntreRecepcoes;
			if (c.Quem == QuemFala.Whis) DizerDivino(QuemFala.Whis, Sortear(BillsEWhis.WhisRecebe));
			else DizerDivino(QuemFala.Bills, BillsEWhis.BillsDorme, Protocol.Fala.Emote);
		}
	}

	// =====================================================================
	// 8. O TRAJE DE QUEM GANHA O TITULO
	// =====================================================================

	/// <summary>DEFEITO INJETADO (bancada `--billsteste`): o traje e entregue de novo a cada reconciliacao. Falso em jogo, sempre.</summary>
	internal bool TrajeTodaVezDeTeste;

	/// <summary>
	/// O TRAJE CHEGA COM O TITULO -- "players q virarem deus da destruicao tb vao ganhar ela de graca".
	///
	/// Pendurado na `ReconciliarDadiva`, que e o lugar unico por onde todo caminho de cargo passa (duelo entre
	/// jogadores, outorga de admin, vencer o Bills) e que roda tambem no login e de segundo em segundo: e assim
	/// que quem JA era o Deus no dia em que isto entrou no jogo recebe o dele ao entrar. UMA vez por personagem
	/// (`Fighter.TrajeDoDeusRecebido`), e a peca fica com ele quando o titulo passa -- e roupa, e nao poder; quem
	/// leva o poder embora e o resto da reconciliacao.
	///
	/// Com a mochila cheia nada e marcado: o tique seguinte tenta de novo, ate caber.
	/// </summary>
	private void ReconciliarOTrajeDoDeus(ServerPlayer pl)
	{
		if (pl.Ficha.TrajeDoDeusRecebido && !TrajeTodaVezDeTeste) return;
		if (!string.Equals(CargoDe(pl.Conta), BillsEWhis.CargoDoBills, StringComparison.OrdinalIgnoreCase)) return;

		string item = new RoupaGuardada(BillsEWhis.Traje, null).Id;
		if (CatalogoDeItens.Get(item) == null) return;   // a arte nao esta no catalogo deste servidor: nao ha o que entregar
		if (pl.Mochila.Guardar(item) != 0) return;

		pl.Ficha.TrajeDoDeusRecebido = true;
		MandarMochila(pl);
		Avisar(pl, "o traje do Deus da Destruição chega às suas mãos com o título. Está na sua mochila: vista quando quiser.");
		GD.Print($"[server] {pl.Name} recebeu o traje do Deus da Destruicao");
	}

	// =====================================================================
	// 9. A MAO DO ADMIN -- pra testar sem esperar um mes
	// =====================================================================

	/// <summary>
	/// `admin_bills &lt;o que&gt;`: `estado` (onde a historia esta), `avancar` (o passo seguinte dela: Boo "cai", o mes
	/// "passa", ele "e derrubado"), `casa` (salta pra orbita da casa dele; so do espaco), `aclimatar` (a maestria de
	/// gravidade do admin sobe ate a da casa) e `reiniciar` (a historia volta a dormir). Nenhum deles mexe em titulo
	/// de ninguem.
	/// </summary>
	private void AdminBills(ServerPlayer adm, string arg)
	{
		switch (arg.Trim().ToLowerInvariant())
		{
			case "avancar":
				switch (_visita.Fase)
				{
					case FaseDaVisita.Dormindo:
						_visita.Fase = FaseDaVisita.ACaminho;
						_visita.DiaDaChegada = DiaDoMundo + (BillsEWhis.EsperaDeTeste >= 0 ? BillsEWhis.EsperaDeTeste : BillsEWhis.DiasAteAVisita);
						SalvarBills();
						AnunciarNoMundo(BillsEWhis.AvisoDoDespertar);
						break;
					case FaseDaVisita.ACaminho:
						_visita.DiaDaChegada = DiaDoMundo;   // o mes "passou": o proximo passo do calendario o pousa
						SalvarBills();
						break;
					case FaseDaVisita.NaTerra:
						if (_bills.Corpo is { } b && !_cenaDivina) { b.UltimoAgressor = adm.Id; b.Combate.Nocautear(BillsEWhis.SegundosNoChao * 4); }
						else if (!_cenaDivina) EncerrarAVisitaSemLuta();
						break;
				}
				Avisar(adm, $"[admin] Bills: {DescricaoDaVisita()}");
				break;

			case "casa":
				AdminIrParaACasaDeBills(adm);
				break;

			// A GRAVIDADE DA CASA E A REGRA DO DONO ("gravidade altissima"), e ela esmaga quem pousa sem a maestria:
			// sem isto o admin nao teria como conferir a casa com um personagem de teste.
			case "aclimatar":
				if (CasaAtual() is not { } la) { Avisar(adm, "[admin] este universo nao tem casa pro Bills (nenhum mundo gerado na regiao)."); break; }
				if (adm.Ficha.GravMastered < la.Gravidade)
				{
					adm.Ficha.GravMastered = la.Gravidade;
					AplicarGravidade(adm);
				}
				Avisar(adm, $"[admin] a sua maestria de gravidade e {adm.Ficha.GravMastered:0}x (a casa do Bills pesa {la.Gravidade:0}x).");
				break;

			case "reiniciar":
				ZerarBills();
				SalvarBills();
				Avisar(adm, "[admin] Bills: a historia voltou a dormir (a saga de Boo, se ja foi vencida, o acorda de novo no proximo segundo).");
				break;

			default:
				Avisar(adm, $"[admin] Bills: {DescricaoDaVisita()}");
				break;
		}
	}

	private string DescricaoDaVisita()
	{
		(string coordenadas, string mundo) = EnderecoDaCasa();
		string fase = _visita.Fase switch
		{
			FaseDaVisita.Dormindo => "dormindo (espera a saga de Boo ser vencida)",
			FaseDaVisita.ACaminho => $"a caminho -- pousa na Terra no dia {_visita.DiaDaChegada} (hoje e o dia {DiaDoMundo})",
			FaseDaVisita.NaTerra => "na Terra, esperando quem o derrube",
			_ => "em casa",
		};
		return $"{fase} | Bills {(BillsExiste ? "existe" : "nao existe (o titulo dele tem dono)")}"
			 + $"{(_bills.Corpo != null ? $" [{_bills.Modo}]" : "")}, Whis {(WhisExiste ? "existe" : "nao existe (ha um Anjo)")}"
			 + $" | casa: {coordenadas} ({mundo})";
	}

	/// <summary>
	/// Salta pra ORBITA da casa do Bills -- so do espaco, e sem pousar: e o molde do "Ir Para O Mundo Divino" (um
	/// verb de admin que troca de zona sozinho larga no vacuo quem o dispara do chao). Encostar no disco pousa pelo
	/// caminho de verdade.
	/// </summary>
	private void AdminIrParaACasaDeBills(ServerPlayer adm)
	{
		if (!Espaco.EhEspaco(adm.Zone)) { Avisar(adm, "[admin] decole primeiro: este salto so sai do espaco."); return; }
		if (CasaAtual() is not { } casa) { Avisar(adm, "[admin] este universo nao tem casa pro Bills (nenhum mundo gerado na regiao)."); return; }

		MoveToZone(adm.Id, ZonaDoEspaco, Espaco.PontoDeDecolagem(casa.Corpo));
		adm.ChunkAtual = ChunkId.De(adm.Pos);
		MandarVizinhanca(adm);
		Avisar(adm, $"[admin] orbita de {NomeDoMundo(casa.Corpo)} ({BillsEWhis.Coordenadas(casa)}), gravidade {casa.Gravidade:0}x "
				  + $"(a sua maestria: {adm.Ficha.GravMastered:0}x). Encoste no disco pra pousar.");
	}
}
