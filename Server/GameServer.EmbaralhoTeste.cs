using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ A BANCADA DO "SEM PLATEIA, SEM MENTE" (`--embaralhoteste`) ============================
/// Mede as duas metades do pedido do dono, e elas sao opostas de proposito: a mente **para** quando
/// nao ha ninguem no planeta, e o mundo **nao para**.
///
///   Godot --headless --path . --host --rede 7972 --embaralhoteste --conta bancada_embaralho --nome Embaralho
///
/// ============================ O QUE ESTA BANCADA APRENDEU COM AS OUTRAS ============================
/// Tres cegos que este port ja pagou, e que esta bancada tenta nao repetir:
///
///   1. **"O NUMERO NAO MUDOU" NAO E MEDIDA.** Toda afirmacao de congelamento vem com o CONTROLE ao
///      lado -- o mesmo corpo, o mesmo numero de tiques, com um jogador na zona. Sem o par, "o NPC
///      nao andou" fica verde num codigo que nunca anda.
///   2. **RELOGIO DE PAREDE NAO GIRA NUM LACO.** O prazo do embaralho e `NowMs()`, e 3600 tiques de
///      bancada correm em muito menos de 5 minutos: esperar seria medir o nada. A bancada EMPURRA a
///      marca pra tras (ela e a unica dona de `_vazioDesde` aqui) e diz que empurrou.
///   3. **"NAO CAIU NA PAREDE" TEM QUE PODER FALHAR.** Medir isso no berco da TERRA nao prova nada:
///      o (249,250) foi escrito PRA ela, e e campo aberto. A familia 4 refaz a medida em **ICER**,
///      onde 309 das 441 celulas em volta do mesmo ponto sao rocha -- e conta as celulas densas do
///      raio ANTES, pra a afirmacao ter como ficar vermelha. Um teto que nunca dispara e
///      indistinguivel de teto nenhum.
/// ==============================================================================================
/// </summary>
public partial class GameServer
{
	private bool _embaralhoDeTeste;

	/// <summary>Faixa de lugares propria -- longe da dos habitantes, das sagas e da `--genteteste`.</summary>
	private ulong _lugarDaBancadaDeEmbaralho = 8_300_000;

	private void RodarBancadaDoEmbaralho(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA: SEM PLATEIA, SEM MENTE (congelamento + embaralho) =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		var forjados = new List<ServerPlayer>();

		try
		{
			// A zona da medida e a do jogador, e a de estacionamento e OUTRA: o congelamento so
			// existe quando nao ha ninguem, entao a bancada precisa de um lugar pra por o host.
			var palco = ZoneKey.Premade("Earth");

			// ============================ ONDE O HOST ESTACIONA -- E ISSO E UMA MEDIDA ============================
			// Namek seria o obvio, e seria ERRADO: ela tem 32 habitantes no plano, que ficariam
			// ACORDADOS junto do host. A familia 2 mede "quanto custa um servidor com todo mundo
			// congelado" -- com o host num planeta povoado ela mediria uma mistura, e o contraste entre
			// dormir e agir sairia menor do que e por um motivo que nao e o codigo.
			//
			// O Lookout nao esta no plano de povoamento (`npcs.json`: Earth, Vegeta, Namek, Icer,
			// Arlia, Makyo_Star), entao o host la deixa o mundo inteiro sem plateia.
			// ================================================================================================
			var longe = ZoneKey.Premade("Lookout");

			ZoneCollision? mapa = MapaDaZonaOuCatalogo(palco);
			if (mapa == null || _moldes == null)
			{
				Checa("PRECONDICAO: a Earth tem mapa de colisao e os moldes carregaram", false);
				return;
			}

			// ---------------------------------------------------------------
			// FAMILIA 1 -- A MENTE PARA SEM PLATEIA, E SO SEM PLATEIA
			// ---------------------------------------------------------------
			Vec2 berco = PontoDeHabitante(palco, ++_lugarDaBancadaDeEmbaralho);
			var vila = new List<ServerPlayer>();
			for (int i = 0; i < 8; i++)
			{
				ServerPlayer? c = NascerNpc("cidadao", palco,
					mapa.PontoLivrePerto(berco + new Vec2(i * 64, 0)), ++_lugarDaBancadaDeEmbaralho);
				if (c != null) { vila.Add(c); forjados.Add(c); }
			}
			Checa("PRECONDICAO: nasceram 8 habitantes na Earth", vila.Count == 8, $"{vila.Count}");
			if (vila.Count != 8) return;

			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));

			// O RANCOR TEM QUE ESTAR FRIO: um cidadao com agressor recente tem presa, e presa e a
			// unica coisa que atravessa o congelamento. Sem isto a medida diria "nao congelou" por um
			// motivo que nao e o defeito.
			foreach (ServerPlayer c in vila) { c.UltimoAgressor = 0; c.RancorAte = 0; }

			var antes = vila.Select(c => c.Pos).ToList();
			for (int t = 0; t < 900; t++) TickDosCorposSemDono(Protocol.TickSeconds);
			double andouVazio = vila.Select((c, i) => (c.Pos - antes[i]).Length).Max();

			Checa("planeta SEM ninguem: 900 tiques (30 s) e nenhum habitante deu um pixel",
				  andouVazio < 0.001, $"o que mais andou: {andouVazio:0.00} px");
			Checa("...e a zona nao conta como habitada (o `_zonasComGente` e quem decide)",
				  !_zonasComGente.Contains(palco.Hash));

			// ---- O CONTROLE: o mesmo laco, com o host na zona ----
			MoveToZone(pl.Id, palco, berco + new Vec2(4 * ZoneCollision.TileSize, 0));
			antes = vila.Select(c => c.Pos).ToList();
			for (int t = 0; t < 900; t++) TickDosCorposSemDono(Protocol.TickSeconds);
			double andouComGente = vila.Select((c, i) => (c.Pos - antes[i]).Length).Max();

			Checa("CONTROLE: com o HOST na zona, os mesmos 8 andam nos mesmos 900 tiques",
				  andouComGente > 0.001, "a linha de cima e decoracao -- ela nao sabe ficar vermelha");
			Checa("...e o host conta como plateia igual a um cliente (o dono pediu os dois)",
				  _zonasComGente.Contains(palco.Hash));

			// ---- MORRER NAO E SAIR DO PLANETA (o defeito de 15 s) ----
			// O corpo fica na zona ate a mesa do Enma, e a tela do dono continua mostrando a cidade.
			bool mortoGuardado = pl.Ficha.dead;
			pl.Ficha.dead = true;
			TickDosCorposSemDono(Protocol.TickSeconds);
			bool plateiaMorta = _zonasComGente.Contains(palco.Hash);
			pl.Ficha.dead = mortoGuardado;
			TickDosCorposSemDono(Protocol.TickSeconds);

			Checa("um jogador MORTO na zona ainda e plateia (senao a cidade vira estatua na frente dele)",
				  plateiaMorta);

			// ---------------------------------------------------------------
			// FAMILIA 2 -- O CUSTO: QUANTO CUSTA UM CORPO CONGELADO
			// ---------------------------------------------------------------
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			TickDosCorposSemDono(Protocol.TickSeconds);

			ulong t0 = Time.GetTicksUsec();
			for (int t = 0; t < 3000; t++) TickDosCorposSemDono(Protocol.TickSeconds);
			double usDormindo = (Time.GetTicksUsec() - t0) / 3000.0;

			MoveToZone(pl.Id, palco, berco + new Vec2(4 * ZoneCollision.TileSize, 0));
			TickDosCorposSemDono(Protocol.TickSeconds);
			t0 = Time.GetTicksUsec();
			for (int t = 0; t < 3000; t++) TickDosCorposSemDono(Protocol.TickSeconds);
			double usAcordado = (Time.GetTicksUsec() - t0) / 3000.0;

			GD.Print($"  MEDIDA  `TickDosCorposSemDono` com {CorposSemDonoNoServidor()} corpos: "
				   + $"planeta vazio {usDormindo:0.00} us/tique, com plateia {usAcordado:0.00} us/tique "
				   + $"({usDormindo / Protocol.TickSeconds / 10000:0.000}% x "
				   + $"{usAcordado / Protocol.TickSeconds / 10000:0.000}% do orcamento)");
			Checa("dormir e mais barato do que agir (senao o congelamento nao paga o proprio codigo)",
				  usDormindo < usAcordado, $"{usDormindo:0.00} x {usAcordado:0.00} us");

			// ---------------------------------------------------------------
			// FAMILIA 3 -- O EMBARALHO
			// ---------------------------------------------------------------
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			TickDosCorposSemDono(Protocol.TickSeconds);

			Checa("PRECONDICAO: a Earth ficou marcada como vazia no tique em que o host saiu",
				  _vazioDesde.ContainsKey(palco.Hash));

			// ---- CHEGAR CEDO NAO EMBARALHA ----
			antes = vila.Select(c => c.Pos).ToList();
			MoveToZone(pl.Id, palco, berco);
			double andouCedo = vila.Select((c, i) => (c.Pos - antes[i]).Length).Max();
			Checa("voltar ANTES dos 5 minutos nao mexe ninguem de lugar",
				  andouCedo < 0.001, $"{andouCedo:0.00} px");

			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			TickDosCorposSemDono(Protocol.TickSeconds);

			// ---- E AGORA O RELOGIO E EMPURRADO PRA TRAS ----
			// Ver o cego 2 do cabecalho: `NowMs()` nao anda dentro de um laco de bancada.
			GD.Print($"  (a bancada empurra `_vazioDesde` da Earth {Povoamento.SegundosAteOEmbaralho + 60:0} s "
				   + "pra tras -- o prazo e de PAREDE e nao gira num laco)");
			_vazioDesde[palco.Hash] = NowMs() - (long)((Povoamento.SegundosAteOEmbaralho + 60) * 1000);

			antes = vila.Select(c => c.Pos).ToList();
			MoveToZone(pl.Id, palco, berco);

			int mudaram = 0, naParede = 0, longeDemais = 0;
			const int t2 = ZoneCollision.TileSize;
			double tetoEsperado = (Povoamento.TilesMaxDoEmbaralho + 8) * t2 * 1.5;
			for (int i = 0; i < vila.Count; i++)
			{
				double d = (vila[i].Pos - antes[i]).Length;
				if (d > 0.001) mudaram++;
				if (d > tetoEsperado) longeDemais++;
				if (mapa.BlockedAt(vila[i].Pos)) naParede++;
			}

			Checa("passados os 5 minutos, TODOS os habitantes mudaram de lugar na chegada do jogador",
				  mudaram == vila.Count, $"{mudaram}/{vila.Count}");
			Checa("...e NENHUM foi parar dentro de parede (o `PontoLivrePerto`, que o dono pediu em maiusculas)",
				  naParede == 0, $"{naParede} na pedra");
			Checa("...e nenhum atravessou o mapa: o passeio e PERTO de onde ele estava",
				  longeDemais == 0, $"{longeDemais} alem de {tetoEsperado:0} px");

			// QUANTO A LINHA ACIMA VALE NESTE MAPA. O berco da Terra e campo aberto de proposito
			// (o (249,250) do BYOND foi escrito PRA ela), entao "ninguem caiu na pedra" aqui pode ser
			// verdade por nao haver pedra. **Quem prova o funil e a familia 4, em Icer** -- este numero
			// so evita que a leitura desta secao seja mais confiante do que a medida.
			int cairiaNaPedra = 0;
			foreach (ServerPlayer c in vila)
				for (int dx = -Povoamento.TilesMaxDoEmbaralho; dx <= Povoamento.TilesMaxDoEmbaralho; dx++)
					for (int dy = -Povoamento.TilesMaxDoEmbaralho; dy <= Povoamento.TilesMaxDoEmbaralho; dy++)
						if (mapa.BlockedAt(c.Pos + new Vec2(dx * t2, dy * t2))) cairiaNaPedra++;
			GD.Print($"  (contexto: o raio do sorteio em volta dos 8 corpos da Terra tem {cairiaNaPedra} "
				   + "celulas densas -- a prova do funil e a familia 4, em Icer)");

			// ---- UMA VEZ SO ----
			antes = vila.Select(c => c.Pos).ToList();
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			MoveToZone(pl.Id, palco, berco);
			double andouDeNovo = vila.Select((c, i) => (c.Pos - antes[i]).Length).Max();
			Checa("sair e voltar na hora nao embaralha de novo (`SO BASTA ACONTECER 1 VEZ`)",
				  andouDeNovo < 0.001, $"{andouDeNovo:0.00} px");

			// ---- O CORPO DE UM JOGADOR NAO E EMBARALHADO ----
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			TickDosCorposSemDono(Protocol.TickSeconds);
			_vazioDesde[palco.Hash] = NowMs() - (long)((Povoamento.SegundosAteOEmbaralho + 60) * 1000);

			Vec2 ondeOHostChega = berco;
			MoveToZone(pl.Id, palco, ondeOHostChega);
			Checa("o corpo de quem CHEGA nao e embaralhado junto (o crivo e o `EhNpcDoMundo`)",
				  (pl.Pos - ondeOHostChega).Length < 0.001, $"{(pl.Pos - ondeOHostChega).Length:0.00} px");

			// ---------------------------------------------------------------
			// FAMILIA 4 -- A PAREDE, NO MAPA QUE TEM PAREDE
			// ---------------------------------------------------------------
			// ============================ POR QUE ICER E NAO A TERRA ============================
			// "Ninguem caiu na pedra" e uma frase que fica verde num campo aberto sem provar nada. O
			// ponto de chegada pre-feito (249,250) e o campo do meio da TERRA, e em **Icer ele e
			// PAREDE**: 309 das 441 celulas do quadrado 21x21 em volta sao densas (medido no proprio
			// `z04_Icer.col` quando o berco foi escrito). E o mapa onde o funil TEM que trabalhar.
			// ==============================================================================
			var pedra = ZoneKey.Premade("Icer");
			ZoneCollision? mapaPedra = MapaDaZonaOuCatalogo(pedra);
			if (mapaPedra == null) Checa("PRECONDICAO: Icer tem mapa de colisao", false);
			else
			{
				var presos = new List<ServerPlayer>();
				for (int i = 0; i < 8; i++)
				{
					ServerPlayer? c = NascerNpc("cidadao", pedra,
						PontoDeHabitante(pedra, ++_lugarDaBancadaDeEmbaralho),
						++_lugarDaBancadaDeEmbaralho);
					if (c != null) { presos.Add(c); forjados.Add(c); }
				}

				int densasEmVolta = 0;
				foreach (ServerPlayer c in presos)
					for (int dx = -Povoamento.TilesMaxDoEmbaralho; dx <= Povoamento.TilesMaxDoEmbaralho; dx++)
						for (int dy = -Povoamento.TilesMaxDoEmbaralho; dy <= Povoamento.TilesMaxDoEmbaralho; dy++)
							if (mapaPedra.BlockedAt(c.Pos + new Vec2(dx * t2, dy * t2))) densasEmVolta++;

				MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
				TickDosCorposSemDono(Protocol.TickSeconds);
				_vazioDesde[pedra.Hash] = NowMs() - (long)((Povoamento.SegundosAteOEmbaralho + 60) * 1000);
				MoveToZone(pl.Id, pedra, PontoDeHabitante(pedra, ++_lugarDaBancadaDeEmbaralho));

				int naPedra = presos.Count(c => mapaPedra.BlockedAt(c.Pos));
				Checa("PRECONDICAO (o defeito injetado, e ele e do MAPA): o raio do sorteio em volta dos "
					+ "8 corpos de Icer esta cheio de celula densa -- aqui um sorteio cru cairia na rocha",
					  densasEmVolta > 0, $"{densasEmVolta} celulas densas");
				Checa("...e mesmo assim NENHUM dos 8 embaralhou pra dentro da pedra em Icer",
					  naPedra == 0, $"{naPedra} na rocha");
			}

			// ---- PLANETA NUNCA VISITADO NAO EMBARALHA ----
			var virgem = ZoneKey.Premade("Arlia");
			ServerPlayer? arliano = NascerNpc("cidadao", virgem,
				PontoDeHabitante(virgem, ++_lugarDaBancadaDeEmbaralho), ++_lugarDaBancadaDeEmbaralho);
			if (arliano != null)
			{
				forjados.Add(arliano);
				Vec2 nasceuEm = arliano.Pos;
				_vazioDesde.Remove(virgem.Hash);   // ninguem nunca saiu de la
				MoveToZone(pl.Id, virgem, PontoDeHabitante(virgem, ++_lugarDaBancadaDeEmbaralho));
				Checa("planeta que jogador NENHUM visitou nao embaralha na primeira visita "
					+ "(o prazo e do ULTIMO que saiu, e ninguem saiu)",
					  (arliano.Pos - nasceuEm).Length < 0.001);
			}
		}
		catch (Exception ex)
		{
			// ABORTAR NO MEIO NAO PODE PARECER SUCESSO -- a licao da `--povoteste`.
			falhou++;
			GD.PrintErr($"  FALHA a bancada morreu no meio: {ex}");
		}
		finally
		{
			foreach (ServerPlayer c in forjados)
				if (_players.ContainsKey(c.Id)) RemoverNpc(c);

			MoveToZone(pl.Id, zonaGuardada, posGuardada);
			_vazioDesde.Clear();
			_embaralhosDaZona.Clear();

			GD.Print($"===== FIM: {ok} OK, {falhou} FALHA =====\n");
		}
	}
}
