using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ A TROPA DE UMA SAGA -- OS SOLDADOS DO FREEZA EM NAMEK (2026-10-18) ============================
/// O pedido do dono, em resumo: com o Freeza em Namek, muitos soldados NPC de raca alienigena pelo planeta, atacando os Namekuseijins e
/// pegando as Esferas do Dragao do chao -- que so voltam derrotando quem as pegou --, de armadura Saiyajin / do exercito do Freeza.
///
/// O DADO E O BLOCO `"tropa"` do elo (`Core/Npc/Sagas.cs`, `TropaDaSaga`); este arquivo e o que o poe de pe:
///   * QUANDO: enquanto um chefe do elo esta NO MUNDO, no planeta da saga. O desembarque poe `chegamJuntos` de uma vez (e o planeta ouve), e
///     depois chega um a cada `segundosEntre` ate `quantos` -- que e tambem a reposicao de quem caiu. Sem o chefe no planeta (caiu, o admin
///     resetou, a saga acabou), a tropa vai embora inteira, e o que ela carregava cai no chao (`RemoverNpc`).
///   * ONDE: pelo planeta inteiro, nas vagas de habitante (`PontoDeHabitante`) -- a tropa se ESPALHA, e nao desce num monte so.
///   * O QUE ELES FAZEM (`GuiarOSoldado`), nesta ordem:
///       1. revidam quem os atacou (jogador ou NPC), enquanto o rancor de 90 s durar;
///       2. vao pra cima de quem CARREGA esfera, a vinte tiles -- e o nocaute derruba a esfera (o funil das tres quedas do `TickDasEsferas`);
///       3. atacam o povo cacado (`caca`, o Namekuseijin -- jogador OU NPC) que esteja a vinte tiles;
///       4. vao ate a esfera do chao mais perto, no planeta inteiro (o radar do exercito), e a pegam;
///       5. sem esfera no chao, cacam o Namekuseijin mais perto do planeta;
///       6. e sem nada disso, vagam.
///   * A ESFERA NA MAO DO SOLDADO: so derrota-lo a solta -- o nocaute ou a morte a derrubam onde ele esta. O soldado derrotado, quando
///     levanta, FOGE (sai do mundo): sem isso ele voltaria pra tomar de volta a esfera que quem o derrubou acabou de pegar.
///
/// ============================ AS DIVERGENCIAS, DECLARADAS ============================
///   * O DM nao tem tropa nenhuma -- a saga de Namek dele e o Freeza sozinho (`spawn_freeza_namek`, BossEvents.dm).
///   * O "AI NAO ATACA AI" (`NPCAI.dm:439`) e a regra do HOSTIL comum (`PresaDoHostil` so ve jogador), e o soldado NAO a segue: a tropa
///     existe pra atacar o povo de Namek, que e NPC. Por isso ele tem ramo proprio no `TicarUmCorpo`, antes do habitante.
///   * O POVO NAO REVIDA o soldado (`MarcarAgressao`): o rancor de um agressor que nao e gente cai na busca alargada do `provoke()`, e o
///     Namekuseijin que apanhou sairia batendo no JOGADOR mais perto. Quem enfrenta a tropa sao os jogadores.
///   * O golpe do soldado e o NAO-LETAL de todo corpo sem dono: ele nocauteia o povo e quem carrega esfera, e segue. Quem o derruba pode
///     ser letal ou nao -- os dois soltam a esfera.
/// ===============================================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Um soldado de pe: de que elo ele e, e o que ele decidiu na ultima busca.</summary>
	private sealed class SoldadoDaTropa
	{
		public string Elo = "";

		/// <summary>Ele ja caiu (nocauteado): quando levantar, foge. Ver o cabecalho.</summary>
		public bool Caiu;

		/// <summary>Quando ele escolhe de novo, no relogio do mundo -- a busca e a 1 Hz, e nao a 30 (ela varre a zona).</summary>
		public double ProximaBusca;

		/// <summary>O corpo que ele persegue agora (0 = nenhum).</summary>
		public int Presa;

		/// <summary>Ate onde ele segue essa presa: os sessenta tiles do hostil, ou o planeta inteiro na caca do passo 5.</summary>
		public float Coleira;

		/// <summary>A esfera do chao pra onde ele vai agora (nula = nenhuma).</summary>
		public Esfera? Esfera;
	}

	/// <summary>Os soldados de pe, por id do corpo. Nao persiste: corpo nao atravessa reinicio, e a tropa volta com o chefe que volta.</summary>
	private readonly Dictionary<int, SoldadoDaTropa> _tropa = [];

	/// <summary>Por elo: quando o proximo soldado pode chegar, e se o desembarque desta passagem do chefe ja aconteceu.</summary>
	private readonly Dictionary<string, (double ProximoEm, bool Desembarcou)> _relogioDaTropa = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>O contador de lugares da tropa -- o terceiro argumento da semente, longe dos habitantes (1..) e dos chefes (5 milhoes..).</summary>
	private ulong _lugarDaTropa = 7_000_000;

	/// <summary>De quantos em quantos segundos (de jogo) o soldado escolhe o rumo de novo.</summary>
	private const double SegundosEntreBuscasDoSoldado = 1;

	// ---- os defeitos injetados (`--chefesteste`, familia T). Falsos em jogo, sempre. ----

	/// <summary>A tropa nao vem: o chefe chega sozinho -- o jogo de antes. Lido pelo <see cref="TickDaTropa"/>.</summary>
	internal static bool TropaNaoVemDeTeste;

	/// <summary>A tropa fica no planeta depois que o chefe dela cai. Lido pelo <see cref="TickDaTropa"/>.</summary>
	internal static bool TropaFicaSemOChefeDeTeste;

	/// <summary>
	/// O soldado cai no ramo do hostil comum (`PresaDoHostil`, so jogador) -- o "AI nao ataca AI" do DM: o povo nunca apanha e a esfera nunca e
	/// catada. Lido pelo `TicarUmCorpo`.
	/// </summary>
	internal static bool SoldadoComoHostilComumDeTeste;

	/// <summary>O soldado derrotado levanta e continua -- e volta pra tomar a esfera de quem o derrubou. Lido pelo <see cref="GuiarOSoldado"/>.</summary>
	internal static bool SoldadoDerrotadoVoltaDeTeste;

	/// <summary>O soldado ignora quem carrega esfera -- so cata as do chao. Lido pelo <see cref="EscolherOAlvoDoSoldado"/>.</summary>
	internal static bool SoldadoIgnoraQuemCarregaDeTeste;

	/// <summary>A esfera nao fica na mao do soldado: o funil das tres quedas a derruba no segundo seguinte. Lido pelo <see cref="CarregaComoSoldado"/>.</summary>
	internal static bool SoldadoNaoSeguraEsferaDeTeste;

	/// <summary>E UM SOLDADO DE SAGA? Pelo TIPO do molde, e nao pelo registro: o mesmo corpo nascido de um admin ou de uma bancada carrega esfera igual.</summary>
	private static bool EhSoldado(ServerPlayer p) => p.Papel is { Tipo: TipoDeNpc.Soldado };

	/// <summary>
	/// O SOLDADO SEGURA A ESFERA. O funil das tres quedas (`TickDasEsferas`) so deixava esfera na mao de JOGADOR (`Peer`), e o soldado nao e
	/// -- sem esta pergunta a esfera que ele pega cairia no chao no segundo seguinte.
	/// </summary>
	private static bool CarregaComoSoldado(ServerPlayer p) => !SoldadoNaoSeguraEsferaDeTeste && EhSoldado(p);

	// =====================================================================
	// O TIQUE DA TROPA (1 Hz, dentro do `TickDasSagas`)
	// =====================================================================
	private void TickDaTropa()
	{
		// QUEM SAIU DO MUNDO (o laco de combate tirou o morto, a bancada o removeu) sai do registro -- e a vaga dele volta pra reposicao.
		foreach (int id in _tropa.Keys.Where(id => !_players.ContainsKey(id)).ToList()) _tropa.Remove(id);

		foreach (EloDaSaga elo in _cadeia)
		{
			if (elo.Tropa is not { } tropa) continue;
			EstadoDoElo? est = _sagas.Do(elo.Id);
			ServerPlayer? chefe = est == null ? null : ChefeDoEloNoPlaneta(elo, est);
			List<ServerPlayer> deles = SoldadosDoElo(elo.Id);

			if (chefe == null)
			{
				if (TropaFicaSemOChefeDeTeste) continue;
				if (deles.Count > 0) RetirarATropa(elo, tropa, deles);
				_relogioDaTropa.Remove(elo.Id);
				continue;
			}
			if (TropaNaoVemDeTeste) continue;

			(double proximoEm, bool desembarcou) = _relogioDaTropa.GetValueOrDefault(elo.Id);
			if (!desembarcou)
			{
				// O DESEMBARQUE: `chegamJuntos` de uma vez, e o planeta ouve.
				int nasceram = 0;
				for (int i = deles.Count; i < tropa.ChegamJuntos; i++)
					if (NascerSoldado(elo, tropa, chefe.Zone) != null) nasceram++;
				if (nasceram > 0 && tropa.AnuncioDaChegada.Length > 0) AnunciarNoPlaneta(chefe.Zone, tropa.AnuncioDaChegada);
				_relogioDaTropa[elo.Id] = (_relogioDoMundo + tropa.SegundosEntre, true);
				continue;
			}

			// DEPOIS, UM A UM, ate o teto -- e a reposicao de quem caiu, no mesmo passo.
			if (deles.Count >= tropa.Quantos || _relogioDoMundo < proximoEm) continue;
			NascerSoldado(elo, tropa, chefe.Zone);
			_relogioDaTropa[elo.Id] = (_relogioDoMundo + tropa.SegundosEntre, true);
		}
	}

	/// <summary>O chefe deste elo de pe no planeta da saga -- nulo = nenhum (ainda nao chegou, caiu, ou a saga nao esta em curso).</summary>
	private ServerPlayer? ChefeDoEloNoPlaneta(EloDaSaga elo, EstadoDoElo est)
	{
		if (est.Fase != FaseDaSaga.EmCurso) return null;
		ulong planeta = ZoneKey.Premade(elo.Planeta).Hash;
		foreach (EstadoDoChefe ec in est.Chefes)
			if (ec.Fase == FaseDoChefe.NoMundo && ec.Corpo != 0 && _players.TryGetValue(ec.Corpo, out ServerPlayer? c)
				&& !c.Ficha.dead && c.Zone.Hash == planeta)
				return c;
		return null;
	}

	private List<ServerPlayer> SoldadosDoElo(string elo)
	{
		var l = new List<ServerPlayer>();
		foreach ((int id, SoldadoDaTropa s) in _tropa)
			if (string.Equals(s.Elo, elo, StringComparison.OrdinalIgnoreCase) && _players.TryGetValue(id, out ServerPlayer? p)) l.Add(p);
		return l;
	}

	/// <summary>
	/// UM SOLDADO, pela MESMA <see cref="NascerNpc"/> de todo mundo -- a raca, a ficha, a roupa (a armadura vem da raca Alien) e a mente saem do
	/// molde e da semente. O teto de lotacao e o do povoamento: a tropa nao entra num planeta cheio por uma porta lateral.
	/// </summary>
	private ServerPlayer? NascerSoldado(EloDaSaga elo, TropaDaSaga tropa, ZoneKey zona)
	{
		if (!CabeMaisUmCorpo(zona, Povoamento.MaxPorZona, Povoamento.MaxNoServidor, out string porque))
		{
			GD.Print($"[tropa] soldado da saga '{elo.Id}' nao nasceu: {porque}");
			return null;
		}
		ulong lugar = ++_lugarDaTropa;
		ServerPlayer? s = NascerNpc(tropa.Molde, zona, PontoDeHabitante(zona, lugar), lugar);
		if (s == null) return null;
		_tropa[s.Id] = new SoldadoDaTropa { Elo = elo.Id };
		return s;
	}

	/// <summary>A TROPA VAI EMBORA: o chefe dela nao esta mais no planeta. O que cada um carregava cai onde ele estava (`RemoverNpc`).</summary>
	private void RetirarATropa(EloDaSaga elo, TropaDaSaga tropa, List<ServerPlayer> deles)
	{
		foreach (ServerPlayer s in deles)
		{
			_tropa.Remove(s.Id);
			RemoverNpc(s);
		}
		if (tropa.AnuncioDaPartida.Length > 0) AnunciarNoPlaneta(ZoneKey.Premade(elo.Planeta), tropa.AnuncioDaPartida);
		GD.Print($"[tropa] a tropa da saga '{elo.Id}' foi embora ({deles.Count} soldado(s))");
	}

	// =====================================================================
	// A MENTE DO SOLDADO (30 Hz, dentro do `TicarUmCorpo`)
	// =====================================================================
	/// <summary>
	/// ATRAS DE QUEM (OU DE QUE ESFERA) O SOLDADO VAI. Falso = o corpo nao age neste tique (caido, morto, fugiu). A ordem das perguntas e a do
	/// cabecalho; a busca e a 1 Hz, e entre uma e outra o alvo escolhido so e revalidado.
	/// </summary>
	private bool GuiarOSoldado(ServerPlayer npc, out ServerPlayer? presa, out Vec2 destino)
	{
		presa = null;
		destino = npc.Pos;
		if (!_tropa.TryGetValue(npc.Id, out SoldadoDaTropa? s)) _tropa[npc.Id] = s = new SoldadoDaTropa();

		// O CADAVER NAO AGE: quem o tira do mundo e o laco de combate.
		if (npc.Ficha.dead && !npc.MortoDePe) { npc.Moving = false; return false; }

		// CAIDO, PARA -- e fica marcado. A esfera que ele carregava ja caiu pelo funil das tres quedas (`TickDasEsferas`).
		if (npc.Ficha.KO) { npc.Moving = false; s.Caiu = true; return false; }

		// O DERROTADO FOGE: levantou depois de cair, e sai do mundo -- ver o cabecalho.
		if (s.Caiu && !SoldadoDerrotadoVoltaDeTeste)
		{
			AvisarQuemVe(npc, $"{npc.Name} se levanta e foge, derrotado.");
			_tropa.Remove(npc.Id);
			RemoverNpc(npc);
			return false;
		}

		if (_relogioDoMundo >= s.ProximaBusca || !AlvoDoSoldadoValido(npc, s))
		{
			s.ProximaBusca = _relogioDoMundo + SegundosEntreBuscasDoSoldado;
			EscolherOAlvoDoSoldado(npc, s);
		}

		if (s.Presa != 0 && _players.TryGetValue(s.Presa, out ServerPlayer? alvo))
		{
			presa = alvo;
			destino = alvo.Pos;
			return true;
		}

		if (s.Esfera is { } e)
		{
			// UM PONTO E NAO ALGUEM: o cerebro anda ate ele (o plano Vagar), e a agua ele atravessa voando (`Travessia`).
			destino = new Vec2(e.X, e.Y);
			if (Math.Abs(e.X - npc.Pos.X) <= Esferas.AlcanceDePegar && Math.Abs(e.Y - npc.Pos.Y) <= Esferas.AlcanceDePegar)
				SoldadoPegaAEsfera(npc, s, e);
			return true;
		}

		destino = RumoDaFera(npc);
		return true;
	}

	/// <summary>O alvo de agora ainda vale? A presa de pe e dentro da coleira; a esfera ainda no chao deste mundo.</summary>
	private bool AlvoDoSoldadoValido(ServerPlayer npc, SoldadoDaTropa s)
	{
		if (s.Presa != 0) return _players.TryGetValue(s.Presa, out ServerPlayer? p) && PresaDoSoldado(npc, p, s.Coleira);
		if (s.Esfera is { } e) return e.Portador == 0 && e.Zona.Hash == npc.Zone.Hash;
		return true;   // vagando: a proxima busca decide
	}

	/// <summary>
	/// ESTE CORPO SERVE DE PRESA PRO SOLDADO? De pe, nao cadaver (o morto de pe do Outro Mundo conta), na mesma zona, dentro do raio -- e nao
	/// e da tropa nem o olho invisivel de quem observa uma nave pousada (`EhOlhoDeFora`).
	/// </summary>
	private static bool PresaDoSoldado(ServerPlayer npc, ServerPlayer o, float raio) =>
		o.Id != npc.Id && !EhSoldado(o) && !EhCadaver(o) && !o.Ficha.KO && !EhOlhoDeFora(o) && o.Zone.Hash == npc.Zone.Hash
		&& (raio >= float.MaxValue || (o.Pos - npc.Pos).LengthSquared <= raio * raio);

	/// <summary>A BUSCA, a 1 Hz. As cinco perguntas do cabecalho, na ordem; a primeira que responde fica.</summary>
	private void EscolherOAlvoDoSoldado(ServerPlayer npc, SoldadoDaTropa s)
	{
		s.Presa = 0;
		s.Esfera = null;
		s.Coleira = RaioDePerseguicao;
		string caca = Array.Find(_cadeia, e => string.Equals(e.Id, s.Elo, StringComparison.OrdinalIgnoreCase))?.Tropa?.Caca ?? "";

		// 1. QUEM O ATACOU, enquanto o rancor durar -- seja quem for (o rancor e o mesmo de todo corpo, `MarcarAgressao`).
		if (NowMs() < npc.RancorAte && _players.TryGetValue(npc.UltimoAgressor, out ServerPlayer? agressor)
			&& PresaDoSoldado(npc, agressor, RaioDePerseguicao))
		{
			s.Presa = agressor.Id;
			return;
		}

		// 2. QUEM CARREGA ESFERA, a vinte tiles: e ele que a tropa quer, e o nocaute a derruba.
		if (!SoldadoIgnoraQuemCarregaDeTeste)
		{
			ServerPlayer? portador = null;
			float perto = RaioDeAgressao * RaioDeAgressao;
			foreach (Esfera e in _esferas)
			{
				if (e.Portador == 0 || !_players.TryGetValue(e.Portador, out ServerPlayer? q) || !PresaDoSoldado(npc, q, RaioDeAgressao)) continue;
				float d2 = (q.Pos - npc.Pos).LengthSquared;
				if (d2 < perto) { perto = d2; portador = q; }
			}
			if (portador != null) { s.Presa = portador.Id; return; }
		}

		// 3. O POVO CACADO, a vinte tiles.
		if (caca.Length > 0 && OMaisPertoDoPovo(npc, caca, RaioDeAgressao) is { } vizinho) { s.Presa = vizinho.Id; return; }

		// 4. A ESFERA DO CHAO MAIS PERTO, no planeta inteiro -- o radar do exercito. A que espera assentar (mundo sem mapa) nao tem lugar certo.
		Esfera? alvo = null;
		float melhor = float.MaxValue;
		foreach (Esfera e in _esferas)
		{
			if (e.Portador != 0 || e.PrecisaAssentar || e.Zona.Hash != npc.Zone.Hash) continue;
			float d2 = (new Vec2(e.X, e.Y) - npc.Pos).LengthSquared;
			if (d2 < melhor) { melhor = d2; alvo = e; }
		}
		if (alvo != null) { s.Esfera = alvo; return; }

		// 5. SEM ESFERA NO CHAO, o povo cacado mais perto do planeta -- e a coleira e o planeta.
		if (caca.Length > 0 && OMaisPertoDoPovo(npc, caca, float.MaxValue) is { } longe)
		{
			s.Presa = longe.Id;
			s.Coleira = float.MaxValue;
		}
	}

	/// <summary>O corpo de pe da raca cacada mais perto, dentro do raio -- jogador OU NPC (a tropa existe pra atacar o povo).</summary>
	private ServerPlayer? OMaisPertoDoPovo(ServerPlayer npc, string caca, float raio)
	{
		ServerPlayer? melhor = null;
		float perto = raio >= float.MaxValue ? float.MaxValue : raio * raio;
		foreach (ServerPlayer o in ZoneList(npc.Zone.Hash))
		{
			if (!string.Equals(o.Race, caca, StringComparison.OrdinalIgnoreCase) || !PresaDoSoldado(npc, o, float.MaxValue)) continue;
			float d2 = (o.Pos - npc.Pos).LengthSquared;
			if (d2 >= perto) continue;
			perto = d2;
			melhor = o;
		}
		return melhor;
	}

	/// <summary>
	/// O SOLDADO PEGA A ESFERA do chao. Daqui ela viaja com ele (o <see cref="CarregaComoSoldado"/> a deixa na mao de quem nao e jogador), e o
	/// funil das tres quedas a derruba onde ele cair. O planeta ouve: e a noticia que manda os jogadores atras dele -- e o Dragon Radar mostra a
	/// esfera na mao de quem a carrega.
	/// </summary>
	private void SoldadoPegaAEsfera(ServerPlayer npc, SoldadoDaTropa s, Esfera e)
	{
		s.Esfera = null;
		s.ProximaBusca = 0;
		if (e.Portador != 0) return;
		e.Portador = npc.Id;
		SalvarEsferas();
		MandarEsferas(npc.Zone);
		AnunciarNoPlaneta(npc.Zone, $"{npc.Name} ({npc.Papel?.Molde.Nome ?? "soldado"}) pegou a {Esferas.NomeDaEsfera(e.Numero)}! Derrote-o pra recuperá-la.");
	}

	/// <summary>Uma linha pra quem esta perto (os vinte tiles de quem nota) -- a fuga de um soldado nao e noticia pro planeta inteiro.</summary>
	private void AvisarQuemVe(ServerPlayer npc, string texto)
	{
		foreach (ServerPlayer o in ZoneList(npc.Zone.Hash))
			if (o.Peer != null && (o.Pos - npc.Pos).LengthSquared <= RaioDeAgressao * RaioDeAgressao) Avisar(o, texto);
	}
}
