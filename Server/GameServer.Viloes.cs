using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// OS VILOES AVULSOS -- o encanamento do <see cref="VilaoAvulso"/> (o pedido, os numeros e o que nao e daqui
/// estao la). Item 12 do playtest de 2026-10-10.
///
/// ============================ O CICLO DE UM EVENTO ============================
///   1. O RELOGIO e por PLANETA COM JOGADOR: a primeira vez que alguem pisa num mundo o intervalo e sorteado,
///      e a cada evento que acaba ele e sorteado de novo. Planeta vazio nao anda relogio nenhum.
///   2. NASCE um NPC do molde `vilao_avulso` numa VAGA DO HABITAT -- a mesma fonte de "terra firme" do
///      povoamento e das sagas (`Habitat`, que so conhece celula navegavel) --, escolhida entre as que ficam a
///      uma distancia jogavel de alguem. Ele nasce HOSTIL sem uma linha a mais: a IA caca jogador pra todo
///      papel que nao e pacifico (`PresaDoHostil`). (2026-10-23) A FORCA e a media de BP de quem esta VIVO NAQUELE planeta
///      (<see cref="MediaDoPlaneta"/>), e nao a do servidor inteiro.
///   3. O PLANETA E AVISADO (`AnunciarNoPlaneta`), com o nome dele e pra que lado de cada um ele esta.
///   4. ACABA de um de tres jeitos: alguem o DERROTA (nocaute ou morte -- o karma vai pra esse alguem, uma
///      vez), o PRAZO vence (ele vai embora, sem karma pra ninguem), ou o corpo some por outra porta.
/// ==============================================================================
///
/// SEPARADO DAS SAGAS de ponta a ponta: lista propria, relogio proprio, molde de tipo proprio, e o unico ponto
/// de contato com o resto do jogo e o funil da derrota (<see cref="AoPerderALuta"/>), que chama
/// <see cref="VilaoFoiDerrotado"/>.
/// </summary>
public sealed partial class GameServer
{
	private sealed class EventoDeVilao
	{
		public int Npc;
		public ZoneKey Zona;
		public string Nome = "";
		public long Ate;

		/// <summary>Alguem ja o derrotou: o karma foi pago, e o tique so falta tirar o corpo. Ver <see cref="VilaoFoiDerrotado"/>.</summary>
		public bool Derrotado;
	}

	private readonly List<EventoDeVilao> _viloes = [];

	/// <summary>Por zona (o hash): a partir de quando o proximo vilao pode nascer ali.</summary>
	private readonly Dictionary<ulong, long> _proximoVilao = [];

	/// <summary>
	/// A faixa PROPRIA de "lugar" dos viloes -- o numero que semeia o sorteio do NPC (`SementeDe`). Longe das
	/// faixas do povoamento e das sagas, pra um vilao nunca sair com a ficha de um habitante.
	/// </summary>
	private ulong _lugarDosViloes = 0x7110_0000UL;

	/// <summary>A MOEDA DA BANCADA: um sorteio fixo no lugar do `_rng`. Nulo em jogo.</summary>
	internal double? SorteioDoVilaoDeTeste;

	private double SorteioDoVilao() => SorteioDoVilaoDeTeste ?? _rng.NextDouble();

	/// <summary>
	/// ESTE CHAO RECEBE VILAO? Planeta de verdade -- pre-feito COM POVO (a linha de povoamento, a mesma
	/// pergunta da conquista) ou gerado -- e vivo. Ficam de fora o espaco, os interiores (nave, mente, o
	/// dentro de um Majin), o Outro Mundo e os lugares sem povo (o Mirante, a Sala do Tempo).
	/// </summary>
	private bool RecebeVilao(ZoneKey zona)
	{
		if (zona.Kind == ZoneKey.KindProcedural) return !ZonaMorta(zona);
		if (zona.Kind != ZoneKey.KindPremade) return false;
		return TemPovo(zona.Name) && !Alem.EhOAlem(zona) && !ZonaMorta(zona);
	}

	/// <summary>O tique, a 1 Hz: fecha os eventos que venceram e abre os que estao na hora.</summary>
	private void TickDosViloes()
	{
		long agora = NowMs();

		for (int i = _viloes.Count - 1; i >= 0; i--)
		{
			EventoDeVilao e = _viloes[i];
			bool dePe = _players.TryGetValue(e.Npc, out ServerPlayer? npc) && !npc.Ficha.dead;
			if (dePe && !e.Derrotado && agora < e.Ate) continue;

			if (dePe)
			{
				// O PRAZO VENCEU: ele vai embora, sem karma pra ninguem -- ninguem o derrotou. (O DERROTADO sai
				// calado: quem o derrubou ja foi anunciado na hora.)
				if (!e.Derrotado) AnunciarNoPlaneta(e.Zona, $"{e.Nome} cansou de esperar por um desafiante e foi embora.");
				RemoverNpc(npc!);
			}
			FecharEventoDeVilao(i, agora);
		}

		if (!VilaoAvulso.Ligado) return;

		// OS PLANETAS COM GENTE, um por vez. A lista sai dos jogadores e nao do `_zonasComGente`: aquele
		// guarda so o hash, e o nascimento precisa da zona inteira.
		_zonasDeVilao.Clear();
		foreach (ServerPlayer p in _players.Values)
			if (EhJogador(p) && !p.Ficha.dead && RecebeVilao(p.Zone)) _zonasDeVilao[p.Zone.Hash] = p.Zone;

		foreach ((ulong hash, ZoneKey zona) in _zonasDeVilao)
		{
			if (!_proximoVilao.TryGetValue(hash, out long quando))
			{
				// PRIMEIRA VEZ COM GENTE NESTE MUNDO: o relogio comeca agora. Ninguem e recebido por um vilao.
				_proximoVilao[hash] = agora + (long)(VilaoAvulso.ProximoIntervaloS(SorteioDoVilao()) * 1000);
				continue;
			}
			if (agora < quando) continue;
			if (!VilaoAvulso.Cabe(_viloes.Count, _viloes.Count(v => v.Zona.Hash == hash))) continue;

			// NASCEU OU NAO, o relogio anda: um planeta onde o nascimento falha (sem vaga a distancia jogavel)
			// nao pode tentar de novo a cada segundo.
			_proximoVilao[hash] = agora + (long)(VilaoAvulso.ProximoIntervaloS(SorteioDoVilao()) * 1000);
			NascerVilao(zona, agora);
		}
	}

	private readonly Dictionary<ulong, ZoneKey> _zonasDeVilao = [];

	private void FecharEventoDeVilao(int i, long agora)
	{
		EventoDeVilao e = _viloes[i];
		_viloes.RemoveAt(i);
		// O INTERVALO CONTA DO FIM do evento, e nao do comeco: derrotado no primeiro minuto ou ido embora no
		// decimo, o planeta tem o mesmo sossego ate o proximo.
		_proximoVilao[e.Zona.Hash] = agora + (long)(VilaoAvulso.ProximoIntervaloS(SorteioDoVilao()) * 1000);
	}

	/// <summary>
	/// ONDE O VILAO APARECE: uma vaga do habitat a distancia jogavel de algum jogador vivo daquele planeta.
	///
	/// TODAS AS VAGAS SAO OLHADAS, e nao um punhado sorteado. A primeira versao sorteava 48 e ficava com a
	/// primeira dentro da faixa: num mapa de 500 tiles a faixa de 10 a 30 em volta de alguem tem uns 2% das
	/// vagas, e em quatro de cada dez tentativas nenhuma das 48 caia la -- o evento simplesmente nao acontecia
	/// (a bancada pegou na primeira rodada). Sao alguns milhares de celulas, uma vez por evento.
	///
	/// DENTRO DA FAIXA, uma ao acaso. SEM NENHUMA na faixa (alguem sozinho num canto do mapa), a mais proxima
	/// que respeite o minimo: o aviso diz pra que lado e a quantos passos, entao longe ainda e achavel -- e
	/// "nunca em cima de ninguem" e a metade da regra que nao se afrouxa. Nulo so sem habitat ou sem gente.
	///
	/// (2026-10-16) <paramref name="perto"/>: de QUEM a faixa e medida. Nulo = dos jogadores vivos da zona (o vilao avulso); o vilao de
	/// saga que volta no Inferno passa os mortos de pe de la -- ou o portao, se ninguem esta la (`GameServer.ViloesDoInferno`).
	/// </summary>
	private Vec2? LugarDeVilao(ZoneKey zona, List<Vec2>? perto = null)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		Habitat? h = mapa == null ? null : HabitatDaZona(zona);
		if (mapa == null || h == null || h.Vagas == 0) return null;

		List<Vec2> gente = perto ?? [];
		if (perto == null)
			foreach (ServerPlayer p in ZoneList(zona.Hash))
				if (EhJogador(p) && !p.Ficha.dead) gente.Add(p.Pos);
		if (gente.Count == 0) return null;

		float min = VilaoAvulso.DistanciaMinTiles * ZoneCollision.TileSize, max = VilaoAvulso.DistanciaMaxTiles * ZoneCollision.TileSize;
		_vagasDeVilao.Clear();
		Vec2? reserva = null;
		float distDaReserva = float.MaxValue;

		for (ulong lugar = 1; lugar <= (ulong)h.Vagas; lugar++)
		{
			(int cx, int cy) = h.Vaga(lugar);
			Vec2 centro = mapa.CentroDaCelula(cx, cy);

			float maisPerto = float.MaxValue;
			foreach (Vec2 g in gente) maisPerto = Math.Min(maisPerto, (g - centro).Length);
			if (maisPerto < min) continue;
			if (maisPerto <= max) _vagasDeVilao.Add(centro);
			else if (maisPerto < distDaReserva) { distDaReserva = maisPerto; reserva = centro; }
		}

		Vec2? escolhida = _vagasDeVilao.Count > 0 ? _vagasDeVilao[_rng.Next(_vagasDeVilao.Count)] : reserva;
		return escolhida is { } e ? mapa.PontoLivrePerto(e) : null;
	}

	/// <summary>A lista reusada das vagas dentro da faixa -- ver <see cref="LugarDeVilao"/>.</summary>
	private readonly List<Vec2> _vagasDeVilao = [];

	/// <summary>
	/// ============================ A FORCA DO VILAO E A DO PLANETA (dono, 2026-10-23) ============================
	/// *"os viloes aleatorios q aparecem deveriam escalar com o poder do planeta atual e nao do server, galera novata tava apanhando
	/// mt dos villoes"*. Perguntado, o dono escolheu: a media do BP BASE dos jogadores VIVOS naquele planeta (a zona) no instante em que
	/// o vilao nasce -- o mesmo sorteio de 85-115% e o mesmo piso do molde por cima. Ate aqui o vilao nascia pela media do SERVIDOR
	/// inteiro (`MediaDoServidor`, que conta toda zona e os mortos): um veterano no Inferno fazia o vilao da Terra nascer pra ele, e quem
	/// apanhava era o novato que estava la.
	///
	/// QUEM CONTA e o mesmo conjunto do <see cref="LugarDeVilao"/> (os jogadores vivos da lista da zona, perto de quem ele aparece) --
	/// pelo funil do `EhJogador` (o clone e o boneco do corpo largado nao contam duas vezes, ver o `MediaDoServidor`) e com BP de
	/// verdade. Ninguem vivo no planeta = zero (o piso do molde segura), e o nascimento nem chega aqui assim: o tique so visita zona com
	/// jogador vivo, e o lugar sem ninguem por perto e nulo.
	/// ========================================================================================================
	/// </summary>
	private double MediaDoPlaneta(ZoneKey zona)
	{
		double soma = 0;
		int n = 0;
		foreach (ServerPlayer p in ZoneList(zona.Hash))
			if (EhJogador(p) && !p.Ficha.dead && p.Ficha is { BP: > 0 }) { soma += p.Ficha.BP; n++; }
		return n == 0 ? 0 : soma / n;
	}

	/// <summary>Poe um vilao naquele planeta e avisa quem esta la. Devolve o corpo, ou nulo se nao nasceu.</summary>
	private ServerPlayer? NascerVilao(ZoneKey zona, long agora)
	{
		if (LugarDeVilao(zona) is not { } onde) return null;

		ulong lugar = ++_lugarDosViloes;
		// (2026-10-23) A FORCA E A DO PLANETA -- ver `MediaDoPlaneta`. O sorteio de 85-115% e o piso do molde continuam os mesmos.
		double media = VilaoPelaMediaDoServidorDeTeste ? MediaDoServidor() : MediaDoPlaneta(zona);
		ServerPlayer? npc = NascerNpc(VilaoAvulso.Molde, zona, onde, lugar, media);
		if (npc?.Papel == null) return null;

		var e = new EventoDeVilao
		{
			Npc = npc.Id, Zona = zona, Nome = npc.Name,
			Ate = agora + (long)(VilaoAvulso.DuracaoS * 1000),
		};
		_viloes.Add(e);

		// O AVISO, UM POR JOGADOR, porque "pra que lado" depende de onde cada um esta.
		foreach (ServerPlayer p in ZoneList(zona.Hash))
		{
			if (p.Peer == null) continue;
			Avisar(p, $"UM VILÃO APARECEU: {e.Nome} está espalhando o terror {RumoDe(p.Pos, onde)}. "
					+ $"Quem o derrotar ganha karma (+{VilaoAvulso.KarmaPorDerrotar}) e reputação com o planeta (+{VilaoAvulso.ReputacaoPorDerrotar:0}). "
					+ $"Ele vai embora em {VilaoAvulso.DuracaoS / 60:0} minutos.");
		}
		GD.Print($"[viloes] {e.Nome} ({npc.Race}, BP {npc.Ficha.BP:N0}, media do planeta {media:N0}) apareceu em {zona.Name} "
			   + $"em ({onde.X:0}, {onde.Y:0}); {_viloes.Count} evento(s) de pe");
		return npc;
	}

	/// <summary>"a uns 14 passos a nordeste de voce" -- a distancia em tiles e um dos oito rumos.</summary>
	private static string RumoDe(Vec2 de, Vec2 para)
	{
		Vec2 d = para - de;
		int tiles = (int)MathF.Round(d.Length / ZoneCollision.TileSize);
		if (tiles <= 1) return "bem do seu lado";

		// O Y DA TELA CRESCE PRA BAIXO: norte e Y negativo.
		double graus = Math.Atan2(-d.Y, d.X) * 180 / Math.PI;
		string[] rumos = ["leste", "nordeste", "norte", "noroeste", "oeste", "sudoeste", "sul", "sudeste"];
		int i = (int)Math.Round(((graus % 360) + 360) % 360 / 45) % 8;
		return $"a uns {tiles} passos a {rumos[i]} de você";
	}

	/// <summary>
	/// O FUNIL DA DERROTA PASSOU POR UM VILAO: nocauteado OU morto por um JOGADOR, o karma vai pra esse jogador
	/// -- *"o karma vai para quem derrotou ou matou"* --, uma vez, e o evento acaba ali.
	///
	/// O VILAO NOCAUTEADO VAI EMBORA em vez de ficar no chao: derrotado, o evento acabou, e um corpo caido que
	/// levantasse dali a pouco seria um segundo premio (ou um vilao de pe sem evento nenhum por tras). O morto
	/// sai pelo caminho de todo NPC que morre.
	///
	/// QUEM TIRA O CORPO E O TIQUE, no segundo seguinte, e nao esta funcao: ela roda DENTRO da resolucao de um
	/// golpe, com alguem percorrendo a lista da zona -- tirar um corpo dali e o "Collection was modified" que
	/// este servidor ja pagou. Aqui so se marca (<see cref="EventoDeVilao.Derrotado"/>), e a marca tambem e o
	/// que impede o segundo premio de quem MATA o vilao que ja estava no chao.
	/// </summary>
	private void VilaoFoiDerrotado(ServerPlayer vitima, ServerPlayer algoz, bool morreu)
	{
		int i = _viloes.FindIndex(v => v.Npc == vitima.Id);
		if (i < 0) return;
		EventoDeVilao e = _viloes[i];

		// QUEM DERROTOU. O algoz pode ser o clone ou a imagem de alguem: o merito e de quem manda nele.
		ServerPlayer? heroi = EhJogador(algoz) ? algoz
			: algoz.DonoDoClone != 0 && _players.TryGetValue(algoz.DonoDoClone, out ServerPlayer? dono) && EhJogador(dono) ? dono
			: null;

		if (e.Derrotado && !VilaoPagaDeNovoDeTeste) return;
		e.Derrotado = true;

		if (heroi != null)
		{
			SomarKarma(heroi, VilaoAvulso.KarmaPorDerrotar, $"você {(morreu ? "acabou com" : "derrotou")} o vilão {e.Nome}.");

			// (2026-10-25) ...E O PLANETA FICA DEVENDO -- o dono: *"faca vencer viloes q aparecem no planeta ao serem derrotados alem de
			// karma ganha reputacao com o planeta"*. A reputacao com o planeta em que o vilao apareceu (`e.Zona`), pelo funil de sempre
			// (`SomarReputacao`, que avisa quem cruza limiar), na mesma vez unica do karma -- a guarda do `Derrotado` logo acima.
			if (!VilaoNaoDaReputacaoDeTeste)
				SomarReputacao(e.Zona.Name, heroi, VilaoAvulso.ReputacaoPorDerrotar, "derrotou-vilao");

			// ============================ ...E FICA COM O ZENI DELE (dono, 2026-10-13) ============================
			// *"matar vilao deveria dar zenni como reward"*. O premio e a BOLSA que ele carrega desde que nasceu
			// (`BolsaDeNpc`), e ela vai direto pra carteira de quem o derrotou -- como o karma da linha de cima, e nao
			// pro chao: e premio, e premio que cai no mato (ou na agua) onde o vilao caiu e de quem passar primeiro.
			//
			// NA DERROTA, E NAO SO NA MORTE, pelo motivo escrito no cabecalho: o vilao NOCAUTEADO vai embora no
			// segundo seguinte -- nao ha como mata-lo depois --, entao cobrar a morte deixaria sem premio quase toda
			// luta, que acaba em nocaute. Uma vez so: a guarda do `Derrotado` logo acima e a mesma do karma.
			// =====================================================================================================
			if (vitima.Ficha.Zeni >= 1 && !VilaoNaoPagaZeniDeTeste)
			{
				double premio = Math.Floor(vitima.Ficha.Zeni);
				vitima.Ficha.Zeni = 0;
				heroi.Ficha.Zeni += premio;
				MandarCarteira(heroi);
				Avisar(heroi, $"você fica com o que o vilão carregava: {premio:N0} zeni.");
				GD.Print($"[viloes] {heroi.Name} derrotou {e.Nome} e ficou com {premio:N0} zeni");
			}
			AnunciarNoPlaneta(e.Zona, $"{NomeVisivel(heroi)} {(morreu ? "acabou com" : "derrotou")} o vilão {e.Nome}!");
		}
		else AnunciarNoPlaneta(e.Zona, $"o vilão {e.Nome} caiu.");
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--vilaoteste`): o premio e pago a cada queda do MESMO vilao -- nocautear e
	/// depois matar pagaria duas vezes. Lido pela propria guarda, em <see cref="VilaoFoiDerrotado"/>.
	/// </summary>
	internal bool VilaoPagaDeNovoDeTeste;

	/// <summary>
	/// (2026-10-25) DEFEITO INJETADO (bancada `--vilaoteste`): derrotar o vilao volta a nao dar reputacao com o planeta -- so o karma e o
	/// zeni, o jogo de antes. Lido pela propria linha que soma, em <see cref="VilaoFoiDerrotado"/>.
	/// </summary>
	internal bool VilaoNaoDaReputacaoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): derrotar o vilao volta a nao render zeni nenhum -- a v0.2.0, em que o
	/// premio era so o karma. Lido pela propria linha que paga, em <see cref="VilaoFoiDerrotado"/>.
	/// </summary>
	internal bool VilaoNaoPagaZeniDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--vilaoteste`): o vilao volta a nascer pela media do SERVIDOR inteiro -- o jogo de antes de 2026-10-23,
	/// em que o veterano de outro planeta fazia o vilao do novato. Lido pela propria linha que escolhe a media, em <see cref="NascerVilao"/>.
	/// </summary>
	internal bool VilaoPelaMediaDoServidorDeTeste;

	/// <summary>Quantos eventos estao de pe (no servidor, e naquele planeta). SO PRA BANCADA.</summary>
	internal (int NoServidor, int NoPlaneta) ViloesDeTeste(ZoneKey zona) =>
		(_viloes.Count, _viloes.Count(v => v.Zona.Hash == zona.Hash));
}
