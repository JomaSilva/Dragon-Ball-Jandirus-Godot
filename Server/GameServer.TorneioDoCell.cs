using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Torneio;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ O TORNEIO DO CELL (dono, 2026-10-19) ============================
/// O pedido, as respostas do dono e os numeros estao em `Core/Npc/TorneioDoCell.cs`. Aqui mora o que o SERVIDOR faz, na ordem em que
/// acontece:
///   1. O CELL QUE CHEGA NA FORMA DO TORNEIO (o `torneioNoDegrau` da ficha dele na saga, a Perfeita) termina a cinematica, anuncia o
///      torneio e SAI do mundo: a saga o poe "a caminho" com os dias do `diasAteOTorneio` e a marca `NoTorneio`, que vai pro disco com ela
///      (`IrProTorneioDoCell`, chamado pelo monitor da saga);
///   2. NO DIA ele nasce NA ARENA da Terra -- a do torneio de artes marciais -- e espera, parado e intocavel, quem pise nela
///      (`AbrirOTorneioDoCell`, chamado pelo nascimento do chefe; a contagem dos dias e a de toda chegada de chefe);
///   3. O JOGADOR QUE PISA NA ARENA desafia (uma chance por jogador; NPC nao desafia): os dois curados, frente a frente, a contagem, os
///      golpes NAO LETAIS pros dois. O juiz (`JuizDoTorneioDoCell`) desfaz o golpe de quem se mete -- o x1 e x1 --, julga o nocaute, a
///      arena (o desafiante que pisa fora perde; o Cell que pisa fora DERRUBA a regra) e a fuga;
///   3b. (2026-10-19) O CELL QUE CAI DA ARENA, alem de derrubar a regra, solta os sete CELL JR -- clones dele, com 90% do BP -- na plateia
///      (os jogadores de fora da luta perto da arena), e termina o x1 com quem o pos pra fora (`SoltarOsCellJr`, `MirarOsCellJr`);
///   4. O CELL NOCAUTEADO e "morto na luta": a morte da saga o traz Super Perfeito um minuto depois (o `ressurgeNoDegrau` que ja existia),
///      NA ARENA, e dali em diante a luta e livre -- todos contra ele;
///   5. A FILA VAZIA por 5 min (com gente online) e o KAMEHAMEHA -- (2026-10-19) o RAIO da tecnica, 2x, pelo canal de sempre: a carga na mao,
///      a cabeca que voa pro jogador mais perto, o embate de ki (`CarregarOKamehamehaDoCell`, `TickDoKamehamehaDoCell`). Chegou ao fim: o
///      gancho de destruicao das sagas, na Terra (`GanchoDeDestruicao`). PERDEU A DISPUTA: o Cell morre ali, de vez (`KamehamehaDoCellPerdeu`).
///      Empatou: ele carrega outro.
///   6. (2026-10-20) O SUPER PERFEITO PERDENDO -- o tronco ou a cabeca abaixo de 25%, na luta livre -- solta o MESMO Kamehameha
///      (`VigiarOSuperPerfeito`). Por isso o Kamehameha tem estado proprio (`KamehamehaDoCellVivo`), fora do torneio. CAIDO (o golpe alto de uma
///      vez) ele levanta e solta; e o golpe que o mataria vira o Kamehameha (`NegarAMorteDoSuperPerfeito`): no fim, ele sempre o solta.
///
/// O TORNEIO DE ARTES MARCIAIS NAO ABRE enquanto o Cell ocupa a arena, e o Cell que chega com ele aberto espera ele acabar
/// (`ChegadaAdiadaDoCell`). No meio da luta o Cell nao troca de forma por dano (o roteiro pula ele): a Super Perfeita e a da morte.
/// UM REINICIO DO SERVIDOR no meio do torneio o recomeca do zero (a fila e quem ja lutou nao vao pro disco; o Cell volta a esperar na arena).
/// =================================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	private enum FaseDoTorneioDoCell : byte { Esperando, Contagem, Luta, Intervalo, Kamehameha, Acabou }

	/// <summary>O torneio do Cell de pe. Um so: o Cell e um so.</summary>
	private sealed class TorneioDoCellVivo
	{
		public string Elo = "";
		public int Cell;
		public ZoneKey Zona;
		public Arena Arena;
		public FaseDoTorneioDoCell Fase;
		public int Desafiante;
		public string Assinatura = "", NomeDoDesafiante = "";
		/// <summary>As assinaturas de quem ja lutou -- *"cada jogador tem uma chance"*.</summary>
		public readonly HashSet<string> JaLutaram = [];
		/// <summary>As assinaturas de quem se meteu na luta de outro.</summary>
		public readonly HashSet<string> Barrados = [];
		/// <summary>Quem esta na arena esperando a vez, e desde quando (o relogio do torneio): o primeiro a pisar e o proximo.</summary>
		public readonly Dictionary<int, double> NaArenaDesde = [];
		/// <summary>A regra da arena: de pe ate o Cell pisar fora (*"ai ele fala q essa regra é inutil e tira ela"*).</summary>
		public bool RegraDaArena = true;
		public double Relogio, FilaVazia, Prazo, Ausente, Longe, ProximaVarrida;
		public int Contagem;
		public bool LetalDoDesafiante = true, AvisouOUltimoMinuto, CellJrSoltos;
		/// <summary>A vida de cada membro no fim do tique anterior -- e dela que o golpe de quem se mete e desfeito.</summary>
		public double[] VidaDoCell = [], VidaDoDesafiante = [];
	}

	private TorneioDoCellVivo? _torneioDoCell;

	/// <summary>
	/// (2026-10-20) O KAMEHAMEHA QUE EXPLODE A TERRA, na mao ou no ar: o do fim do torneio (a fila vazia) e o do Super Perfeito perdendo, que
	/// acontece na luta livre, sem torneio nenhum -- por isso ele e um estado a parte. Um so: o Cell e um so.
	/// </summary>
	private sealed class KamehamehaDoCellVivo
	{
		public string Elo = "";
		public int Cell;
		/// <summary>O raio que saiu da mao (nulo na carga), e se a ultima disputa dele EMPATOU.</summary>
		public Projetil? Raio;
		public bool Empatou;
	}

	private KamehamehaDoCellVivo? _kamehamehaDoCell;
	private double _esperaDaChegadaDoCell;

	/// <summary>(2026-10-19) Os Cell Jr de pe (ids) -- os do torneio de agora e os que sobraram dele, ate a saga acabar. Nao vai pro disco.</summary>
	private readonly List<int> _cellJrs = [];

	/// <summary>O elo de onde os Cell Jr sairam: acabado ele, eles vao embora (`CellJrVaoEmbora`).</summary>
	private string _eloDosCellJr = "";

	/// <summary>Faixa propria de lugares (sementes) pros Cell Jr, longe dos habitantes, das sagas e do torneio de artes marciais.</summary>
	private const ulong PrimeiroLugarDosCellJr = 7_500_000;
	private ulong _lugarDosCellJr = PrimeiroLugarDosCellJr;

	/// <summary>Gancho de bancada: os anuncios e as falas do torneio do Cell, na ordem. Nulo fora de teste.</summary>
	internal static List<string>? EscutaDoTorneioDoCell;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): o Cell Perfeito nao vai pro torneio -- segue a luta, o jogo de antes.</summary>
	internal static bool CellSemTorneioDeTeste;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): o golpe de quem se mete no x1 fica -- o jogo sem o juiz do x1.</summary>
	internal static bool QuemSeMeteFicaDeTeste;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): o Cell que pisa fora da arena nao derruba a regra -- ela continua valendo pro desafiante.</summary>
	internal static bool RegraNaoCaiDeTeste;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): o NPC que pisa na arena e posto pra fora como quem ja lutou -- o crivo do jogador sem efeito.</summary>
	internal static bool ArenaNaoEsvaziaDeTeste;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): o Cell do torneio troca de forma por dano, como na saga -- o roteiro nao o pula.</summary>
	internal static bool CellTransformaNoTorneioDeTeste;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): o NPC que pisa na arena desafia o Cell, como um jogador.</summary>
	internal static bool NpcDesafiaOCellDeTeste;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): entre as lutas a IA do Cell segue solta -- ele caca quem estiver perto, como na saga.</summary>
	internal static bool CellCacaNaEsperaDeTeste;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): o Cell que cai da arena so derruba a regra -- nenhum Cell Jr nasce (o torneio de antes).</summary>
	internal static bool SemCellJrDeTeste;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): o Cell Jr fica com o livro do sorteio do molde dele, e nao com o do Cell (`NascerUmCellJr`).</summary>
	internal static bool CellJrSemOLivroDoCellDeTeste;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): o Kamehameha sai do tamanho do raio comum da tecnica, e nao 2x (`CarregarOKamehamehaDoCell`).</summary>
	internal static bool KamehamehaDoTamanhoNormalDeTeste;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): o Kamehameha que perde a disputa de ki nao leva o Cell -- so o embate de sempre, o raio do vencedor avancando.</summary>
	internal static bool CellSobreviveAoEmbateDeTeste;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): a morte no embate tem a volta do alem, como a morte na luta (ele voltaria Super Perfeito).</summary>
	internal static bool CellVoltaDoEmbateDeTeste;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): o Kamehameha que EMPATA explode a Terra, como o que chega ao fim.</summary>
	internal static bool EmpateExplodeATerraDeTeste;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): o Super Perfeito perdendo segue lutando -- o jogo de antes (`VigiarOSuperPerfeito`).</summary>
	internal static bool SuperPerfeitoNaoSoltaDeTeste;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): o Super Perfeito nocauteado fica no chao -- o vigia so olha quem esta de pe.</summary>
	internal static bool SuperPerfeitoCaidoNaoSoltaDeTeste;

	/// <summary>DEFEITO INJETADO (`--torneiocellteste`): o golpe que mataria o Super Perfeito segue pela porta da morte de sempre (`NegarAMorteDoSuperPerfeito`).</summary>
	internal static bool SuperPerfeitoMorreDeTeste;

	/// <summary>
	/// (2026-10-19) O CELL QUE PERDEU O KAMEHAMEHA NUM EMBATE morre DE VEZ: o monitor da saga pula a volta do alem pra este corpo (`MorreuOChefe`).
	/// Nao vai pro disco: a morte e lida no tique seguinte das sagas.
	/// </summary>
	private readonly HashSet<int> _cellSemVolta = [];

	/// <summary>Este corpo morreu sem volta do alem? (a pergunta do `MorreuOChefe`)</summary>
	private bool MorreuSemVolta(int corpo) => !CellVoltaDoEmbateDeTeste && _cellSemVolta.Contains(corpo);

	// =====================================================================
	// AS PERGUNTAS DE FORA (a IA, o roteiro, o torneio de artes marciais)
	// =====================================================================

	/// <summary>O Cell do torneio, fora da luta (esperando, na contagem, no intervalo, no Kamehameha): parado no lugar. Ver `ForaDaVezNoTorneio`.</summary>
	internal bool CellForaDaVez(ServerPlayer npc) =>
		// (2026-10-20) o Cell com o Kamehameha na mao: parado ate o raio acabar -- o do Super Perfeito tambem, que nao tem torneio
		(_kamehamehaDoCell is { } k && npc.Id == k.Cell)
		|| (!CellCacaNaEsperaDeTeste && _torneioDoCell is { } t
			&& ((npc.Id == t.Cell && t.Fase != FaseDoTorneioDoCell.Luta)
				// (2026-10-19) e o Cell Jr sem ninguem de fora da luta pra cacar: espera parado -- ele nunca vai no desafiante
				|| (npc.Papel is { PresaDoRoteiro: 0 } && _cellJrs.Contains(npc.Id))));

	/// <summary>Este chefe e o Cell do torneio: o roteiro nao o sobe de degrau por dano (a Super Perfeita e a da morte).</summary>
	internal bool CellNoTorneio(ServerPlayer npc) => !CellTransformaNoTorneioDeTeste && _torneioDoCell is { } t && npc.Id == t.Cell;

	/// <summary>O Cell ocupa a arena da Terra: o torneio de artes marciais nao abre.</summary>
	internal bool ArenaComOCell => _torneioDoCell != null;

	// =====================================================================
	// 1. A FORMA DO TORNEIO: ele anuncia e sai
	// =====================================================================

	/// <summary>
	/// O CELL CHEGOU NA FORMA DO TORNEIO? Anuncia, sai do mundo e fica "a caminho" com os dias contados. Chamado pelo monitor da saga
	/// (`MonitorarChefe`), depois da pergunta da morte e antes do resto. Espera a cinematica da forma nova acabar: ele anuncia DEPOIS de virar.
	/// </summary>
	private bool IrProTorneioDoCell(EloDaSaga elo, EstadoDoChefe ec, ServerPlayer corpo)
	{
		if (CellSemTorneioDeTeste || ec.NoTorneio) return false;
		ChefeDaSaga? ficha = Array.Find(elo.Chefes, c => c.Molde == ec.Molde);
		if (ficha is not { TorneioNoDegrau: > 0 } || ec.Degrau + 1 != ficha.TorneioNoDegrau || EmCena(corpo)) return false;

		int dias = Math.Max(ficha.DiasAteOTorneio, 1);
		Falar(corpo, Protocol.Fala.Diz, TorneioDoCell.FalaDoAnuncio(dias));
		DizerDoTorneioDoCell(TorneioDoCell.Anuncio(corpo.Name, dias));
		ec.NoTorneio = true;
		ec.Fase = FaseDoChefe.ACaminho;
		ec.DiasRestantes = dias;
		ec.Corpo = 0;
		ec.Carregando = false;
		ec.UltimatoEm = 0;
		GD.Print($"[server] SAGA '{elo.Id}': {corpo.Name} chegou ao degrau {ec.Degrau + 1} e foi pro TORNEIO -- volta na arena em {dias} dia(s)");
		RemoverNpc(corpo);
		SalvarSagas();
		return true;
	}

	/// <summary>
	/// O DIA CHEGOU COM O TORNEIO DE ARTES MARCIAIS ABERTO: a contagem da saga segura o Cell "a caminho" (`VirouODia`), e esta volta, uma vez
	/// por segundo, o poe na arena quando ela vagar.
	/// </summary>
	private void ChegadaAdiadaDoCell()
	{
		if (TorneioAtivo || _torneioDoCell != null) return;
		foreach (EloDaSaga elo in _cadeia)
		{
			if (_sagas.Do(elo.Id) is not { Fase: FaseDaSaga.EmCurso } est) continue;
			foreach (EstadoDoChefe ec in est.Chefes.ToList())
				if (ec is { Fase: FaseDoChefe.ACaminho, NoTorneio: true, DiasRestantes: <= 0 })
				{
					NascerChefeDaSaga(elo, est, ec);
					SalvarSagas();
					return;
				}
		}
	}

	// =====================================================================
	// 2. A ARENA: a abertura, e a volta Super Perfeita
	// =====================================================================

	/// <summary>
	/// O CELL DO TORNEIO NASCEU (no dia, num reinicio, ou de volta do alem): chamado pelo `NascerChefeDaSaga` quando a marca `NoTorneio`
	/// esta acesa. Na arena da Terra. Voltando do alem (Super Perfeito), o torneio acaba e a marca apaga: a luta e livre. (2026-10-28) E a
	/// volta e pela Transmissao, ao lado de quem o matou na luta (`ChegarAoLadoDoMatador`); sem ele a alcance, no meio da arena.
	/// </summary>
	private bool AbrirOTorneioDoCell(EloDaSaga elo, EstadoDoChefe ec, ServerPlayer cell, bool ressurgindo, bool retomando)
	{
		PalcoDeTorneio palco = _torneioCfg.Terra;
		if (!string.Equals(palco.Zona, elo.Planeta, StringComparison.OrdinalIgnoreCase)) return false;   // a arena e a da Terra
		var zona = ZoneKey.Premade(palco.Zona);
		Arena arena = palco.Arena;

		string frase;
		if (ressurgindo)
		{
			EncerrarOTorneioDoCell();
			ec.NoTorneio = false;
			// (2026-10-28) ao lado de quem o matou, pela Transmissao (o pedido 147); sem o matador a alcance, no meio da arena, como antes
			if (!ChegarAoLadoDoMatador(cell, ec, zona)) Transportar(cell, zona, arena.Centro);
			frase = TorneioDoCell.VoltaSuperPerfeito(cell.Name);
		}
		else
		{
			_torneioDoCell = new TorneioDoCellVivo { Elo = elo.Id, Cell = cell.Id, Zona = zona, Arena = arena, Fase = FaseDoTorneioDoCell.Esperando };
			cell.Arena = arena;   // o cerebro conhece a beirada (o `edge_margin`): ele nao sai da arena por conta propria
			Transportar(cell, zona, arena.Centro);
			SegurarOCell(cell);
			frase = retomando ? TorneioDoCell.Retomada(cell.Name) : TorneioDoCell.Abertura(cell.Name);
		}
		DizerDoTorneioDoCell(frase);
		if (!retomando) AnunciarChegadaDoChefe(cell, elo.Planeta, frase);
		GD.Print($"[server] SAGA '{elo.Id}': {cell.Name} na arena da Terra -- {(ressurgindo ? "Super Perfeito, luta livre" : "o torneio esta aberto")}");
		return true;
	}

	/// <summary>Fim do torneio (a volta Super Perfeita, o Kamehameha, o Cell que sumiu): o desafiante da vez volta a ser letal.</summary>
	private void EncerrarOTorneioDoCell()
	{
		if (_torneioDoCell is not { } t) return;
		if (t.Desafiante != 0 && _players.TryGetValue(t.Desafiante, out ServerPlayer? d)) d.Combate.Letal = t.LetalDoDesafiante;
		if (_players.TryGetValue(t.Cell, out ServerPlayer? cell))
		{
			if (cell.Combate.Carencia >= CarenciaDeEspera) cell.Combate.Carencia = 0;
			cell.Combate.Letal = true;
			if (cell.Papel != null) cell.Papel.PresaDoRoteiro = 0;
		}
		_torneioDoCell = null;
		MirarOsCellJr();   // (2026-10-19) os Cell Jr que sobraram ficam soltos: a luta e livre
	}

	/// <summary>O Cell entre as lutas: no meio da arena, de pe, intocavel e sem alvo (a IA o deixa parado -- `CellForaDaVez`).</summary>
	private void SegurarOCell(ServerPlayer cell)
	{
		cell.Combate.Carencia = CarenciaDeEspera;
		if (cell.Papel != null) cell.Papel.PresaDoRoteiro = 0;
		cell.QuerSubir = false;
		cell.QuerDescer = cell.Altitude > 0f;
		cell.Moving = false;
	}

	// =====================================================================
	// O TIQUE (30 Hz, do `TickDoTorneio`)
	// =====================================================================

	private void TickDoTorneioDoCell(double dt)
	{
		_esperaDaChegadaDoCell += dt;
		if (_esperaDaChegadaDoCell >= 1)
		{
			_esperaDaChegadaDoCell = 0;
			ChegadaAdiadaDoCell();
			MirarOsCellJr();
			CellJrVaoEmbora();
		}

		// (2026-10-20) O KAMEHAMEHA -- o do fim do torneio e o do Super Perfeito perdendo -- e o VIGIA do Super Perfeito: fora do torneio, porque o
		// do Super Perfeito e na luta livre
		if (_kamehamehaDoCell is { } k) TickDoKamehamehaDoCell(k);
		else VigiarOSuperPerfeito();

		if (_torneioDoCell is not { } t) return;
		if (!_players.TryGetValue(t.Cell, out ServerPlayer? cell) || cell.Papel == null)
		{
			// O CORPO SAIU DO MUNDO POR OUTRO CAMINHO (o admin, a limpeza, o reset da saga): o torneio acaba com ele.
			EncerrarOTorneioDoCell();
			return;
		}
		t.Relogio += dt;
		if (cell.Ficha.dead) return;   // morto na luta: a saga cuida da volta (`MorreuOChefe`), e a abertura dela encerra o torneio

		switch (t.Fase)
		{
			case FaseDoTorneioDoCell.Esperando:
				SegurarOCell(cell);
				if (ProximoDesafiante(t) is { } proximo) { ChamarODesafiante(t, cell, proximo); break; }
				// A FILA VAZIA SO CONTA COM GENTE ONLINE -- a mesma regra do ultimato (`CorrerOUltimato`): sem ninguem pra reagir, a Terra
				// nao explode de madrugada. Servidor vazio recomeca a conta.
				if (_players.Values.Any(p => EhJogador(p) && !p.Ficha.dead)) t.FilaVazia += dt;
				else { t.FilaVazia = 0; t.AvisouOUltimoMinuto = false; }
				if (!t.AvisouOUltimoMinuto && t.FilaVazia >= TorneioDoCell.SegundosDeFilaVazia - TorneioDoCell.SegundosDoUltimoAviso)
				{
					t.AvisouOUltimoMinuto = true;
					Falar(cell, Protocol.Fala.Diz, TorneioDoCell.FalaDoUltimoMinuto);
					EscutaDoTorneioDoCell?.Add(TorneioDoCell.FalaDoUltimoMinuto);
				}
				if (t.FilaVazia >= TorneioDoCell.SegundosDeFilaVazia) Kamehameha(t, cell);
				break;

			case FaseDoTorneioDoCell.Contagem:
				if (!_players.TryGetValue(t.Desafiante, out ServerPlayer? quem) || !LutadorDoCellOk(t, quem))
				{
					TerminarLutaDoCell(t, cell, null, "W.O.");
					break;
				}
				if (t.Relogio >= t.ProximaVarrida) { t.ProximaVarrida = t.Relogio + 0.5; EsvaziarAArena(t); }
				t.Prazo -= dt;
				if (t.Prazo > 0) break;
				t.Contagem--;
				if (t.Contagem > 0) { DizerNaArena(t, $"{t.Contagem}..."); t.Prazo = 1; }
				else ComecarALutaDoCell(t, cell, quem);
				break;

			case FaseDoTorneioDoCell.Luta:
				JuizDoTorneioDoCell(t, cell, dt);
				break;

			case FaseDoTorneioDoCell.Intervalo:
				SegurarOCell(cell);
				t.Prazo -= dt;
				if (t.Prazo <= 0) t.Fase = FaseDoTorneioDoCell.Esperando;
				break;

			case FaseDoTorneioDoCell.Kamehameha:
				break;   // (o Kamehameha anda la em cima, fora do torneio: `TickDoKamehamehaDoCell`)
		}
	}

	// =====================================================================
	// 3. O DESAFIANTE E A LUTA
	// =====================================================================

	/// <summary>
	/// QUEM PISOU NA ARENA E PODE DESAFIAR: jogador, vivo, de pe, que ainda nao lutou e nao se meteu na luta de outro. O primeiro a pisar e o
	/// proximo. Quem pisa sem poder e posto pra fora, com o motivo. NPC nao desafia (*"so entra player sem npc"*) -- ele nem e olhado.
	/// </summary>
	private ServerPlayer? ProximoDesafiante(TorneioDoCellVivo t)
	{
		ServerPlayer? melhor = null;
		double desde = double.MaxValue;
		var dentro = new HashSet<int>();
		foreach (ServerPlayer p in ZoneList(t.Zona.Hash).ToList())
		{
			if (p.Id == t.Cell || p.Ficha.dead || p.Ficha.KO || EhCadaver(p) || !t.Arena.ContemEm(p.Pos)) continue;
			if (!EhJogador(p) && !NpcDesafiaOCellDeTeste) continue;
			if (t.JaLutaram.Contains(p.Assinatura) || t.Barrados.Contains(p.Assinatura))
			{
				Avisar(p, t.Barrados.Contains(p.Assinatura) ? TorneioDoCell.SeMeteu : TorneioDoCell.JaLutou);
				Transportar(p, t.Zona, PontoForaDaArena(t, p.Pos));
				continue;
			}
			dentro.Add(p.Id);
			if (!t.NaArenaDesde.TryGetValue(p.Id, out double quando)) t.NaArenaDesde[p.Id] = quando = t.Relogio;
			if (quando < desde) { desde = quando; melhor = p; }
		}
		foreach (int id in t.NaArenaDesde.Keys.ToList())
			if (!dentro.Contains(id)) t.NaArenaDesde.Remove(id);
		return melhor;
	}

	/// <summary>O desafiante da vez continua na luta: no mundo, na Terra, vivo.</summary>
	private static bool LutadorDoCellOk(TorneioDoCellVivo t, ServerPlayer? c) => c != null && c.Zone.Hash == t.Zona.Hash && !c.Ficha.dead;

	private void ChamarODesafiante(TorneioDoCellVivo t, ServerPlayer cell, ServerPlayer d)
	{
		t.Desafiante = d.Id;
		t.Assinatura = d.Assinatura;
		t.NomeDoDesafiante = d.Name;
		t.NaArenaDesde.Remove(d.Id);
		t.Ausente = t.Longe = 0;
		t.AvisouOUltimoMinuto = false;

		CurarPraLuta(d);
		CurarPraLuta(cell);
		if (cell.Voando) AlternarVoo(cell);
		Transportar(d, t.Zona, PontoDoLadoDaArena(t, 1));
		Transportar(cell, t.Zona, PontoDoLadoDaArena(t, 2));
		CravarOlhar(d, Facing.East);
		cell.Facing = Facing.West;
		// GOLPES NAO LETAIS PROS DOIS -- *"nocautear apenas"*: o mesmo `Letal` que o torneio de artes marciais desliga.
		t.LetalDoDesafiante = d.Combate.Letal;
		d.Combate.Letal = false;
		cell.Combate.Letal = false;

		DizerDoTorneioDoCell(TorneioDoCell.Desafio(cell.Name, d.Name));
		t.Contagem = TorneioDoCell.SegundosDeContagem;
		t.Prazo = 1;
		t.Fase = FaseDoTorneioDoCell.Contagem;
		DizerNaArena(t, $"{t.Contagem}...");
		EsvaziarAArena(t);
	}

	private void ComecarALutaDoCell(TorneioDoCellVivo t, ServerPlayer cell, ServerPlayer d)
	{
		cell.Combate.Carencia = 0;
		cell.Papel!.PresaDoRoteiro = d.Id;
		t.VidaDoCell = Vidas(cell);
		t.VidaDoDesafiante = Vidas(d);
		t.Fase = FaseDoTorneioDoCell.Luta;
		DizerNaArena(t, "COMECEM!");
	}

	/// <summary>
	/// O JUIZ DO X1, a cada tique, nesta ordem: quem sumiu (W.O.), quem se meteu (o golpe e desfeito), o nocaute, a arena, a fuga -- e a
	/// arena sendo so dos dois. A foto da vida de cada um fica pro tique seguinte.
	/// </summary>
	private void JuizDoTorneioDoCell(TorneioDoCellVivo t, ServerPlayer cell, double dt)
	{
		ServerPlayer? d = _players.TryGetValue(t.Desafiante, out ServerPlayer? x) && LutadorDoCellOk(t, x) ? x : null;
		if (d == null)
		{
			t.Ausente += dt;
			if (t.Ausente >= TorneioDoCell.SegundosDeAusencia) TerminarLutaDoCell(t, cell, null, "W.O.");
			return;
		}
		t.Ausente = 0;

		// O X1 E X1: o golpe de quem nao e o desafiante no Cell -- e o de quem nao e o Cell no desafiante -- e desfeito, e quem se meteu
		// fica fora do torneio.
		DesfazerOGolpeDeFora(t, cell, t.VidaDoCell, d);
		DesfazerOGolpeDeFora(t, d, t.VidaDoDesafiante, cell);

		if (d.Ficha.KO) { TerminarLutaDoCell(t, cell, d, "nocaute"); return; }
		if (cell.Ficha.KO) { CellMortoNaLuta(t, cell, d); return; }

		if (TorneioDoCell.PerdePorSair(t.RegraDaArena, d.Voando, t.Arena.ContemEm(d.Pos)))
		{
			DizerNaArena(t, $"{d.Name} pisou fora da arena! RING-OUT!");
			TerminarLutaDoCell(t, cell, d, "ring-out");
			return;
		}
		if (TorneioDoCell.CellDerrubaARegra(t.RegraDaArena, cell.Voando, t.Arena.ContemEm(cell.Pos)) && !RegraNaoCaiDeTeste)
		{
			t.RegraDaArena = false;
			Falar(cell, Protocol.Fala.Diz, TorneioDoCell.FalaDaRegra);
			EscutaDoTorneioDoCell?.Add(TorneioDoCell.FalaDaRegra);
			DizerDoTorneioDoCell(TorneioDoCell.RegraCaiu(cell.Name));
			// (2026-10-19) ...E SOLTA OS CELL JR na plateia, enquanto termina o x1 com quem o pos pra fora -- ver `SoltarOsCellJr`.
			SoltarOsCellJr(t, cell);
		}

		// A FUGA: longe demais da arena, por tempo demais, e desistir.
		if (t.Arena.MargemEmCelulas(d.Pos) < -TorneioDoCell.CelulasDeFuga)
		{
			t.Longe += dt;
			if (t.Longe >= TorneioDoCell.SegundosDeFuga) { TerminarLutaDoCell(t, cell, d, "fugiu da luta"); return; }
		}
		else t.Longe = 0;

		if (t.Relogio >= t.ProximaVarrida) { t.ProximaVarrida = t.Relogio + 0.5; EsvaziarAArena(t); }
		t.VidaDoCell = Vidas(cell);
		t.VidaDoDesafiante = Vidas(d);
	}

	/// <summary>
	/// O GOLPE DE FORA E DESFEITO: o corpo perdeu vida desde o tique anterior e o ultimo a acerta-lo (`UltimoAgressor`, o funil de toda
	/// agressao) nao e o adversario da vez. A vida volta a foto, o membro arrancado volta, o nocaute que ele causou levanta -- e quem se
	/// meteu (sendo jogador) fica fora do torneio. Dano sem autor (o sol, o calor) fica: nao ha de quem desfazer.
	/// </summary>
	private void DesfazerOGolpeDeFora(TorneioDoCellVivo t, ServerPlayer alvo, double[] antes, ServerPlayer adversario)
	{
		if (QuemSeMeteFicaDeTeste) return;
		List<BodyPart> partes = alvo.Combate.Corpo.Partes;
		if (antes.Length != partes.Count) return;
		int quem = alvo.UltimoAgressor;
		if (quem == 0 || quem == adversario.Id || quem == alvo.Id) return;
		bool perdeu = false;
		for (int i = 0; i < partes.Count; i++)
			if (partes[i].Decepado || partes[i].Vida < antes[i] - 1e-6) { perdeu = true; break; }
		if (!perdeu) return;

		for (int i = 0; i < partes.Count; i++)
		{
			if (partes[i].Decepado) alvo.Combate.Corpo.Regenerar(partes[i]);
			partes[i].Vida = Math.Max(partes[i].Vida, antes[i]);
		}
		alvo.Combate.SincronizarVida();
		if (alvo.Ficha.KO) alvo.Combate.Levantar();
		alvo.UltimoAgressor = adversario.Id;
		alvo.SigAtributos = "";

		if (_players.TryGetValue(quem, out ServerPlayer? intruso) && EhJogador(intruso) && t.Barrados.Add(intruso.Assinatura))
		{
			Avisar(intruso, TorneioDoCell.NaoPega(t.NomeDoDesafiante));
			GD.Print($"[server] TORNEIO DO CELL: {intruso.Name} se meteu na luta de {t.NomeDoDesafiante} -- o golpe foi desfeito e ele esta fora");
		}
	}

	/// <summary>A vida de cada membro do corpo agora, na ordem do corpo.</summary>
	private static double[] Vidas(ServerPlayer c) => c.Combate.Corpo.Partes.Select(p => p.Decepado ? 0 : p.Vida).ToArray();

	/// <summary>O Cell venceu a luta (o nocaute, a arena, a fuga, o W.O.): o desafiante ja teve a chance dele, e o Cell volta pro meio.</summary>
	private void TerminarLutaDoCell(TorneioDoCellVivo t, ServerPlayer cell, ServerPlayer? d, string como)
	{
		if (t.Assinatura.Length > 0) t.JaLutaram.Add(t.Assinatura);
		if (d != null || _players.TryGetValue(t.Desafiante, out d))
		{
			d.Combate.Letal = t.LetalDoDesafiante;
			if (LutadorDoCellOk(t, d))
			{
				CurarPraLuta(d);
				Transportar(d, t.Zona, PontoForaDaArena(t, d.Pos));
			}
		}
		DizerDoTorneioDoCell(TorneioDoCell.CellVence(cell.Name, t.NomeDoDesafiante, como));

		cell.Combate.Letal = true;
		CurarPraLuta(cell);
		if (cell.Voando) AlternarVoo(cell);
		Transportar(cell, t.Zona, t.Arena.Centro);
		SegurarOCell(cell);
		t.Desafiante = 0;
		t.Assinatura = t.NomeDoDesafiante = "";
		t.FilaVazia = 0;
		t.AvisouOUltimoMinuto = false;
		t.Prazo = TorneioDoCell.SegundosEntreLutas;
		t.Fase = FaseDoTorneioDoCell.Intervalo;
	}

	/// <summary>
	/// 4. NOCAUTEADO NO X1, O CELL "MORRE NA LUTA" -- o dono: *"o jogo vai falar q ele foi morto na luta e volta super perfeito"*. A morte e a
	/// da saga (`MorreuOChefe`): morto na Perfeita (o `ressurgeNoDegrau`), ele volta Super Perfeito em 1 min -- e a abertura dele, com a marca
	/// `NoTorneio` ainda acesa, o poe na arena e encerra o torneio (`AbrirOTorneioDoCell`). (2026-10-28) A volta e ao lado de quem o matou.
	/// </summary>
	private void CellMortoNaLuta(TorneioDoCellVivo t, ServerPlayer cell, ServerPlayer d)
	{
		d.Combate.Letal = t.LetalDoDesafiante;
		cell.Papel!.PresaDoRoteiro = 0;
		cell.UltimoAgressor = d.Id;
		DizerDoTorneioDoCell(TorneioDoCell.MortoNaLuta(cell.Name, d.Name));
		t.Fase = FaseDoTorneioDoCell.Acabou;
		cell.Combate.Morrer(ignorarSeguro: true);
	}

	/// <summary>
	/// 5. A FILA VAZIA: o Kamehameha na Terra. (2026-10-19) Ate as fotos de hoje era a esfera do ultimato (`ComecarASupernova`) com um desenho
	/// proprio; o dono pediu o raio da tecnica, 2x, e a disputa de ki contra ele -- ver o cabecalho de `Core/Npc/TorneioDoCell.cs`.
	/// </summary>
	private void Kamehameha(TorneioDoCellVivo t, ServerPlayer cell)
	{
		t.Fase = FaseDoTorneioDoCell.Kamehameha;
		ComecarOKamehamehaDoCell(t.Elo, cell, TorneioDoCell.AnuncioDoKamehameha(cell.Name), TorneioDoCell.FalaDoKamehameha);
	}

	/// <summary>
	/// O KAMEHAMEHA COMECA -- a porta comum do fim do torneio e do Super Perfeito perdendo: o estado dele, o que o mundo ouve, o que o Cell diz, e
	/// a carga (`CarregarOKamehamehaDoCell`).
	/// </summary>
	private void ComecarOKamehamehaDoCell(string elo, ServerPlayer cell, string anuncio, string fala)
	{
		_kamehamehaDoCell = new KamehamehaDoCellVivo { Elo = elo, Cell = cell.Id };
		DizerDoTorneioDoCell(anuncio);
		Falar(cell, Protocol.Fala.Diz, fala);
		CarregarOKamehamehaDoCell(cell);
	}

	/// <summary>
	/// 6. (2026-10-20) O SUPER PERFEITO PERDENDO SOLTA O KAMEHAMEHA -- o dono: *"Ele no super perfeito tb usaria o kamehameha q destrói planeta se
	/// ele estiver perdendo (hp do torso ou cabeça abaixo de 25%)"*. A cada tique, sem Kamehameha nem torneio: o Super Perfeito da saga
	/// (`SuperPerfeitoNoMundo`) com o tronco ou a cabeca abaixo de 25% (`TorneioDoCell.Perdendo`) -- ou CAIDO, que o golpe alto de uma vez pula a
	/// janela dos 25% (*"Caso ele seja nocauteado por sofrer um alto dano de uma vez, ele começa a usar o kamehameha"*), e ele levanta pra isso --
	/// para de lutar e carrega o MESMO Kamehameha (o raio 2x, intocavel na carga, o embate). Dali em diante ou ele morre no embate, ou a Terra
	/// explode (o empate o faz carregar outro, como no torneio). O golpe que o mataria vira o Kamehameha tambem: `NegarAMorteDoSuperPerfeito`.
	/// </summary>
	private void VigiarOSuperPerfeito()
	{
		if (_torneioDoCell != null || SuperPerfeitoNaoSoltaDeTeste || SuperPerfeitoNoMundo() is not { } sp) return;
		ServerPlayer cell = sp.Corpo;
		bool caido = cell.Ficha.KO && !SuperPerfeitoCaidoNaoSoltaDeTeste;
		if (!caido && (cell.Ficha.KO || !TorneioDoCell.Perdendo(cell.Combate.Corpo))) return;
		if (caido) cell.Combate.Levantar();   // caido ninguem carrega nada (`RaioNomeadoG6` recusa): ele levanta pra isso
		ComecarOKamehamehaDoCell(sp.Elo, cell, caido ? TorneioDoCell.AnuncioDoKamehamehaDoSuperPerfeitoCaido(cell.Name)
												 : TorneioDoCell.AnuncioDoKamehamehaDoSuperPerfeito(cell.Name),
								 TorneioDoCell.FalaDoKamehamehaDoSuperPerfeito);
		GD.Print($"[server] SAGA '{sp.Elo}': {cell.Name} SUPER PERFEITO {(caido ? "caido" : "perdendo")} -- solta o Kamehameha que explode a Terra");
	}

	/// <summary>
	/// O SUPER PERFEITO DA SAGA, se ha um no mundo: o chefe da saga do torneio (a ficha com `torneioNoDegrau`), no ULTIMO degrau e vivo. O vigia
	/// e a morte negada perguntam a mesma coisa.
	/// </summary>
	private (string Elo, ServerPlayer Corpo)? SuperPerfeitoNoMundo()
	{
		foreach (EloDaSaga elo in _cadeia)
		{
			if (_sagas.Do(elo.Id) is not { Fase: FaseDaSaga.EmCurso } est) continue;
			foreach (EstadoDoChefe ec in est.Chefes)
			{
				if (ec.Fase != FaseDoChefe.NoMundo || !_players.TryGetValue(ec.Corpo, out ServerPlayer? cell) || cell.Papel == null) continue;
				if (Array.Find(elo.Chefes, c => c.Molde == ec.Molde) is not { TorneioNoDegrau: > 0 }) continue;
				if (cell.Papel.Estagio != cell.Papel.Molde.Estagios.Length - 1) continue;
				if (cell.Ficha.dead) continue;
				return (elo.Id, cell);
			}
		}
		return null;
	}

	/// <summary>
	/// (2026-10-20) O GOLPE QUE MATARIA O SUPER PERFEITO VIRA O KAMEHAMEHA -- o dono: *"basicamente deveria ser impossível ele nao jogar o kamehameha
	/// no final"*. O primeiro elo do `NegarMorte` (pelo `NegarMorteDoChefe`, antes do nucleo do bio-androide e das raciais): fora da disputa o Super
	/// Perfeito nao morre -- o membro vital arrancado volta, ele levanta por um fio (`Levantar`) e carrega, intocavel; ja carregando, so fica de pe.
	/// O que o mata e perder a disputa (`KamehamehaDoCellPerdeu`) e o admin: os dois passam por cima do seguro (`ignorarSeguro`).
	/// </summary>
	private bool NegarAMorteDoSuperPerfeito(ServerPlayer npc)
	{
		if (SuperPerfeitoMorreDeTeste || npc.Papel == null || SuperPerfeitoNoMundo() is not { } sp || sp.Corpo != npc) return false;
		foreach (BodyPart p in npc.Combate.Corpo.Partes.Where(q => q.Decepado && q.Papel == Vitalidade.Nucleo).ToList()) npc.Combate.Corpo.Regenerar(p);
		npc.Combate.Levantar();
		if (_kamehamehaDoCell == null)
		{
			ComecarOKamehamehaDoCell(sp.Elo, npc, TorneioDoCell.AnuncioDoKamehamehaDoSuperPerfeitoCaido(npc.Name), TorneioDoCell.FalaDoKamehamehaDoSuperPerfeito);
			GD.Print($"[server] SAGA '{sp.Elo}': o golpe que mataria {npc.Name} virou o Kamehameha que explode a Terra");
		}
		return true;
	}

	/// <summary>
	/// O KAMEHAMEHA NA MAO: o VERB DA TECNICA (`RaioNomeadoG6` -- o canal, a receita, a arte do Kamehameha dele e o desenho da carga, os de
	/// qualquer Kamehameha do jogo), e no canal aberto duas coisas a mais: a `EscalaVisual` de <see cref="TorneioDoCell.EscalaDoKamehameha"/>
	/// vezes a do raio comum (o `wavemult` da receita), e a carga de <see cref="TorneioDoCell.SegundosDeCargaDoKamehameha"/>. Ele segue
	/// intocavel -- o golpe nao o tira dali; o que para o Kamehameha e outro raio. O Ki vai cheio: o fim da Terra nao falha por falta dele.
	/// </summary>
	private void CarregarOKamehamehaDoCell(ServerPlayer cell)
	{
		cell.Combate.Carencia = CarenciaDeEspera;
		cell.Ficha.Ki = cell.Ficha.MaxKi;
		MirarOKamehameha(cell);
		RaioNomeadoG6(cell, "Kamehameha");
		if (!_canais.TryGetValue(cell.Id, out CanalDeKi? c) || c.Verbo != "Kamehameha") return;   // nao abriu: o tique tenta de novo
		c.Receita.EscalaVisual = c.Receita.MultDeOnda * (KamehamehaDoTamanhoNormalDeTeste ? 1 : TorneioDoCell.EscalaDoKamehameha);
		c.CargaRestante = TorneioDoCell.SegundosDeCargaDoKamehameha;
	}

	/// <summary>
	/// PRA ONDE O KAMEHAMEHA VAI: o jogador vivo mais perto, na zona dele -- e com ele que se disputa (*"se outro jogador jogar um beam contra
	/// esse kamehameha"*). A marca e o olhar: o raio nasce pela mira de sempre (`RumoDoTiro`), que cai no olhar quando o marcado esta
	/// intocavel. Ninguem na zona: ele segue olhando pra onde olhava.
	/// </summary>
	private void MirarOKamehameha(ServerPlayer cell)
	{
		ServerPlayer? alvo = null;
		float melhor = float.MaxValue;
		foreach (ServerPlayer p in ZoneList(cell.Zone.Hash))
		{
			if (!EhJogador(p) || EhCadaver(p) || p.Ficha.dead) continue;
			float d = (p.Pos - cell.Pos).LengthSquared;
			if (d < melhor) { melhor = d; alvo = p; }
		}
		if (alvo == null) return;
		cell.AlvoId = alvo.Id;
		cell.Facing = MoveRules.FacingFrom(alvo.Pos - cell.Pos, cell.Facing);
	}

	/// <summary>
	/// O KAMEHAMEHA, a cada tique: intocavel e mirando ate o raio sair; o raio que saiu fica guardado. O RAIO ACABOU sem levar o Cell junto --
	/// chegou ao fim do alcance, bateu no cenario ou em alguem --: a Terra explode (o gancho das sagas). EMPATOU: ele carrega outro. O CANAL CAIU
	/// antes de o raio sair: carrega de novo. (Perdeu a disputa: o Cell ja morreu e o torneio acabou -- `KamehamehaDoCellPerdeu`.)
	/// </summary>
	private void TickDoKamehamehaDoCell(KamehamehaDoCellVivo k)
	{
		// o corpo saiu do mundo (o admin, a limpeza, o reset da saga) ou morreu por outro caminho: nao ha mais Kamehameha
		if (!_players.TryGetValue(k.Cell, out ServerPlayer? cell) || cell.Papel == null || cell.Ficha.dead) { _kamehamehaDoCell = null; return; }
		cell.Combate.Carencia = CarenciaDeEspera;
		CanalDeKi? c = _canais.GetValueOrDefault(cell.Id);
		if (c?.Raio is { } raio) k.Raio = raio;
		else if (c != null) MirarOKamehameha(cell);

		if (k.Raio is { Vivo: false })
		{
			if (k.Empatou && !EmpateExplodeATerraDeTeste)
			{
				k.Empatou = false;
				k.Raio = null;
				DizerDoTorneioDoCell(TorneioDoCell.KamehamehaEmpatou(cell.Name));
				CarregarOKamehamehaDoCell(cell);
				return;
			}
			ExplodirATerraPeloCell(k, cell);
			return;
		}
		if (c == null && k.Raio == null) CarregarOKamehamehaDoCell(cell);
	}

	/// <summary>O Kamehameha chegou: o torneio (se havia) acaba e a Terra comeca a explodir (o gancho de destruicao das sagas, o do ultimato).</summary>
	private void ExplodirATerraPeloCell(KamehamehaDoCellVivo k, ServerPlayer cell)
	{
		_kamehamehaDoCell = null;
		EloDaSaga? elo = Array.Find(_cadeia, e => e.Id == k.Elo);
		EstadoDoElo? est = elo == null ? null : _sagas.Do(elo.Id);
		EstadoDoChefe? ec = est?.Chefes.Find(x => x.Corpo == cell.Id);
		EncerrarOTorneioDoCell();
		if (elo != null && est != null && ec != null)
		{
			ec.NoTorneio = false;
			GanchoDeDestruicao(elo, est, ec, cell);
		}
	}

	/// <summary>
	/// (2026-10-19) O KAMEHAMEHA PERDEU A DISPUTA DE KI -- o dono: *"se o jogador vencer essa colisao o cell morre na hora (mesma coisa q no anime
	/// com gohan)"*. Chamado pelo `Resolver` do embate, depois do desfecho de sempre (o raio do vencedor ja avanca nele). O Cell MORRE ali, e de
	/// vez: a volta Super Perfeita e da morte na luta, nao desta (`MorreuSemVolta`). O credito e de quem venceu -- e o heroi da saga, pelo funil
	/// de sempre (`MorreuOChefe` -> `PagarOHeroi`) --, a Terra fica, e o torneio (se havia) acaba. (2026-10-20) Vale pro Super Perfeito tambem.
	/// </summary>
	private void KamehamehaDoCellPerdeu(ServerPlayer perdedor, ServerPlayer vencedor)
	{
		if (CellSobreviveAoEmbateDeTeste || _kamehamehaDoCell is not { } k || perdedor.Id != k.Cell || perdedor.Ficha.dead) return;
		_kamehamehaDoCell = null;
		_cellSemVolta.Add(perdedor.Id);
		perdedor.UltimoAgressor = vencedor.Id;
		if (_sagas.Do(k.Elo)?.Chefes.Find(x => x.Corpo == perdedor.Id) is { } ec) ec.NoTorneio = false;
		DizerDoTorneioDoCell(TorneioDoCell.CellDesintegrado(perdedor.Name, vencedor.Name));
		EncerrarOTorneioDoCell();
		perdedor.Combate.Morrer(ignorarSeguro: true);
		GD.Print($"[server] TORNEIO DO CELL: o Kamehameha perdeu a disputa pra {vencedor.Name} -- o Cell morreu de vez e a Terra fica");
	}

	/// <summary>(2026-10-19) A disputa EMPATOU: era o Kamehameha do Cell? Entao ele carrega outro (`TickDoKamehamehaDoCell`). Chamado pelo `Empatar`.</summary>
	private void KamehamehaDoCellEmpatou(ServerPlayer a, ServerPlayer b)
	{
		if (_kamehamehaDoCell is { } k && (a.Id == k.Cell || b.Id == k.Cell)) k.Empatou = true;
	}

	// =====================================================================
	// OS CELL JR (dono, 2026-10-19)
	// =====================================================================

	/// <summary>
	/// O CELL CAIU DA ARENA: alem de derrubar a regra, ele solta os sete Cell Jr -- em roda, a dois tiles dele -- e cada um mira um jogador de
	/// fora da luta (`MirarOsCellJr`). Cada um e um CLONE dele (*"os cell jr vao ser literalmente um clone do cell original mas com 90% do bp
	/// dele"*): o molde `cell_jr` e o do Cell (a raca, o sangue e as seis skills dele, as tecnicas dadas, o cerebro), o livro e o DELE (as mesmas
	/// skills, nos mesmos niveis -- `NascerUmCellJr`), o BP e 90% do dele agora, e a folha e a do Cell Jr. O x1 continua: o golpe deles no desafiante e desfeito como o de qualquer um, e o Cell segue intocavel pra plateia.
	/// </summary>
	private void SoltarOsCellJr(TorneioDoCellVivo t, ServerPlayer cell)
	{
		if (t.CellJrSoltos || SemCellJrDeTeste) return;
		t.CellJrSoltos = true;
		_eloDosCellJr = t.Elo;
		Falar(cell, Protocol.Fala.Diz, TorneioDoCell.FalaDosCellJr);
		EscutaDoTorneioDoCell?.Add(TorneioDoCell.FalaDosCellJr);
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(t.Zona);
		int nasceram = 0;
		for (int i = 0; i < TorneioDoCell.QuantosCellJr; i++)
		{
			double angulo = 2 * Math.PI * i / TorneioDoCell.QuantosCellJr;
			Vec2 alvo = cell.Pos + new Vec2((float)Math.Cos(angulo), (float)Math.Sin(angulo)) * (2 * ZoneCollision.TileSize);
			if (NascerUmCellJr(t, cell, mapa?.PontoLivrePerto(alvo) ?? alvo) is not { } jr) continue;
			_cellJrs.Add(jr.Id);
			nasceram++;
		}
		DizerDoTorneioDoCell(TorneioDoCell.AnuncioDosCellJr(cell.Name, nasceram));
		GD.Print($"[server] TORNEIO DO CELL: {nasceram} Cell Jr soltos, BP {cell.Ficha.BP * TorneioDoCell.FracaoDoBpDoCellJr:N0} cada, "
				 + $"{cell.Livro?.Aprendidas.Count ?? 0} skills (as do Cell)");
		MirarOsCellJr();
	}

	/// <summary>
	/// UM CELL JR, CLONE DO CELL: o molde `cell_jr` (a raca, o sangue e as seis skills dele, as tecnicas dadas, o cerebro), o LIVRO do Cell (as
	/// mesmas skills, nos mesmos niveis), a folha do Cell Jr e 90% do BP dele agora. Nulo se o molde nao nasceu.
	/// </summary>
	private ServerPlayer? NascerUmCellJr(TorneioDoCellVivo t, ServerPlayer cell, Vec2 onde)
	{
		ServerPlayer? jr = NascerNpc(TorneioDoCell.MoldeDoCellJr, t.Zona, onde, ++_lugarDosCellJr);
		if (jr?.Papel == null) return null;
		jr.Visual.Corpo = TorneioDoCell.CorpoDoCellJr;
		TrocarAparencias(jr);
		// (2026-10-19) O LIVRO E O DO CELL -- *"literalmente um clone do cell original"*. O sorteio do molde compra as skills pelo BP de
		// NASCIMENTO (`SorteioDeNpc.MarcosDe`), e o Cell Jr nasce com o BP do molde: na rodada d3 eram 12 skills contra as 22 do Cell. Aqui ele
		// passa a saber o que o Cell sabe, no nivel do Cell; a que so o sorteio dele deu cai pro nivel 0 antes, e os dois aplicadores (os efeitos
		// e os niveis) refazem a ficha pelos razoes do proprio Cell Jr -- eles poem exatamente o que o livro novo da, nem mais nem de novo.
		if (!CellJrSemOLivroDoCellDeTeste && _skills != null && jr.Livro != null && cell.Livro != null)
		{
			foreach (string p in jr.Livro.Aprendidas.ToList())
				if (!cell.Livro.Sabe(p)) jr.Niveis.Por(p, 0);
			jr.Livro.Carregar(cell.Livro.Aprendidas);
			foreach (string p in cell.Livro.Aprendidas) jr.Niveis.Por(p, cell.Niveis.Nivel(p));
			Jandirus.Core.Skills.EfeitosDeSkill.Aplicar(jr.Ficha, _skills, jr.Livro.Aprendidas, jr.Livro.Escolhas);
			jr.Niveis.Aplicar(jr.Ficha);
		}
		jr.Ficha.BP = cell.Ficha.BP * TorneioDoCell.FracaoDoBpDoCellJr;
		// (o fecho da ficha do sorteio, refeito com o BP e o livro novos: o Ki e o folego cheios, a raiva calma)
		SorteioDeNpc.Normalizar(jr.Ficha);
		jr.SigAtributos = "";
		return jr;
	}

	/// <summary>
	/// QUEM CADA CELL JR CACA: com o torneio de pe, um jogador da PLATEIA (de fora da luta, perto da arena), espalhados na ordem -- quem ja tem
	/// um alvo valido fica com ele; sem plateia, ele espera parado (`CellForaDaVez`). Sem torneio, o alvo imposto cai e a IA hostil dele escolhe
	/// quem estiver perto: a luta e livre. Uma vez por segundo, e quando eles nascem e quando o torneio acaba.
	/// </summary>
	private void MirarOsCellJr()
	{
		_cellJrs.RemoveAll(id => !_players.TryGetValue(id, out ServerPlayer? j) || j.Ficha.dead || j.Papel == null);
		if (_cellJrs.Count == 0) return;
		List<ServerPlayer> plateia = _torneioDoCell is { } t ? Plateia(t) : [];
		int proximo = 0;
		foreach (int id in _cellJrs)
		{
			PapelDeNpc papel = _players[id].Papel!;
			if (_torneioDoCell == null) { papel.PresaDoRoteiro = 0; continue; }
			if (plateia.Exists(p => p.Id == papel.PresaDoRoteiro)) continue;
			papel.PresaDoRoteiro = plateia.Count == 0 ? 0 : plateia[proximo++ % plateia.Count].Id;
		}
	}

	/// <summary>A PLATEIA: jogador vivo e de pe, na Terra, a ate `TilesDaPlateia` do meio da arena -- menos o desafiante da vez.</summary>
	private List<ServerPlayer> Plateia(TorneioDoCellVivo t)
	{
		float raio = TorneioDoCell.TilesDaPlateia * ZoneCollision.TileSize;
		var l = new List<ServerPlayer>();
		foreach (ServerPlayer p in ZoneList(t.Zona.Hash))
			if (EhJogador(p) && p.Id != t.Desafiante && !p.Ficha.dead && !p.Ficha.KO && (p.Pos - t.Arena.Centro).LengthSquared <= raio * raio)
				l.Add(p);
		l.Sort((a, b) => a.Id.CompareTo(b.Id));
		return l;
	}

	/// <summary>Acabada a saga deles (vencida, consumada ou resetada), os Cell Jr que sobraram vao embora com ela.</summary>
	private void CellJrVaoEmbora()
	{
		if (_cellJrs.Count == 0 || _torneioDoCell != null) return;
		if (_sagas.Do(_eloDosCellJr) is { Fase: FaseDaSaga.EmCurso }) return;
		foreach (int id in _cellJrs.ToList())
			if (_players.TryGetValue(id, out ServerPlayer? jr)) RemoverNpc(jr);
		_cellJrs.Clear();
	}

	// =====================================================================
	// A ARENA: os lugares, e ela so dos dois
	// =====================================================================

	/// <summary>Frente a frente na linha do centro, de 3 a 10 celulas do meio, sempre DENTRO -- o `fighter_spot` do torneio de artes marciais.</summary>
	private Vec2 PontoDoLadoDaArena(TorneioDoCellVivo t, int lado)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(t.Zona);
		Arena a = t.Arena;
		int acx = (a.X1 + a.X2) / 2, acy = (a.Y1 + a.Y2) / 2;
		for (int off = 3; off <= 10; off++)
		{
			int tx = lado == 1 ? acx - off : acx + off;
			if (tx < a.X1 + 1 || tx > a.X2 - 1) break;
			if (mapa == null || !mapa.BlockedCell(tx, acy)) return new Vec2(tx + 0.5f, acy + 0.5f) * ZoneCollision.TileSize;
		}
		return a.Centro;
	}

	/// <summary>
	/// PRA FORA DA ARENA, pelo lado mais perto de onde o corpo esta: tres celulas alem da beirada, na mesma linha (ou coluna), num chao que
	/// serve. Sem chao ali, o ponto livre mais perto.
	/// </summary>
	private Vec2 PontoForaDaArena(TorneioDoCellVivo t, Vec2 de)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(t.Zona);
		Arena a = t.Arena;
		int cx = Math.Clamp((int)MathF.Floor(de.X / ZoneCollision.TileSize), a.X1, a.X2);
		int cy = Math.Clamp((int)MathF.Floor(de.Y / ZoneCollision.TileSize), a.Y1, a.Y2);
		var lados = new List<(int Dist, int X, int Y)>
		{
			(cx - a.X1, a.X1 - 3, cy), (a.X2 - cx, a.X2 + 3, cy), (cy - a.Y1, cx, a.Y1 - 3), (a.Y2 - cy, cx, a.Y2 + 3),
		};
		lados.Sort((p, q) => p.Dist.CompareTo(q.Dist));
		foreach ((_, int x, int y) in lados)
			if (mapa == null || mapa.ServeDeChao(x, y)) return new Vec2(x + 0.5f, y + 0.5f) * ZoneCollision.TileSize;
		Vec2 alvo = new Vec2(lados[0].X + 0.5f, lados[0].Y + 0.5f) * ZoneCollision.TileSize;
		return mapa?.PontoLivrePerto(alvo) ?? alvo;
	}

	/// <summary>A arena e so dos dois durante a contagem e a luta: quem mais pisa nela e posto pra fora (o x1 e x1).</summary>
	private void EsvaziarAArena(TorneioDoCellVivo t)
	{
		if (ArenaNaoEsvaziaDeTeste) return;
		foreach (ServerPlayer p in ZoneList(t.Zona.Hash).ToList())
		{
			if (p.Id == t.Cell || p.Id == t.Desafiante || !EhJogador(p) || p.Ficha.dead || !t.Arena.ContemEm(p.Pos)) continue;
			Avisar(p, TorneioDoCell.ArenaOcupada(t.NomeDoDesafiante));
			Transportar(p, t.Zona, PontoForaDaArena(t, p.Pos));
		}
	}

	// =====================================================================
	// O QUE SE DIZ
	// =====================================================================

	/// <summary>Pro mundo inteiro (o anuncio de saga) -- e pra escuta da bancada.</summary>
	private void DizerDoTorneioDoCell(string texto)
	{
		AnunciarNoMundo(texto);
		EscutaDoTorneioDoCell?.Add(texto);
	}

	/// <summary>So pra quem esta na Terra perto da arena (a contagem, o "COMECEM!", o ring-out) -- e pra escuta da bancada.</summary>
	private void DizerNaArena(TorneioDoCellVivo t, string texto)
	{
		float raio = 40 * ZoneCollision.TileSize;
		foreach (ServerPlayer p in ZoneList(t.Zona.Hash))
			if (p.Peer != null && (p.Pos - t.Arena.Centro).LengthSquared <= raio * raio) Avisar(p, texto);
		EscutaDoTorneioDoCell?.Add(texto);
	}
}
