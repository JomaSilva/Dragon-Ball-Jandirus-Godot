using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// AS ROCHAS DE MINERIO, NO SERVIDOR -- quais estao de pe em cada planeta, onde nascem, quem as colhe e quem
/// fica sabendo. A regra e os numeros estao em <see cref="RochaDeMinerio"/>; o pedido do dono esta la.
///
/// ============================ POR QUE NAO SAO `Obra` ============================
/// A mobilia que o servidor semeia (o banco, a macieira, o jarro do Mirante) e `Obra`, e ganha de graca o
/// desenho, a colisao e a tecla E. A rocha nao entrou por ali, e a conta e de escala: sao CENTENAS por
/// planeta (uma a cada 20x20 tiles de terra: 316 na Terra, 379 em Vegeta, mais de mil num mundo gerado grande), e elas
/// mudam UMA de cada vez, o tempo todo. O caminho das obras refaz tudo a cada mudanca -- `MandarObras`
/// reenvia a lista inteira da zona (uns 130 bytes por obra, com o caminho da arte dentro), o cliente apaga e
/// recria um node por obra (`World.DesenharObras`) e o servidor refaz a camada de colisao
/// (`AplicarColisaoDasObras`). Com dezenas de obras isso e certo; com mil rochas cada coleta custaria 130 KB
/// e mil nodes por jogador do planeta. E `_noChao` e varrido por tique em mais de um lugar (o campo bio, o
/// saco de pancada), por jogador.
///
/// Entao a rocha tem o caminho dela, e ele e pequeno: um conjunto de celulas por planeta, uma camada propria
/// no mapa de colisao (`ZoneCollision.PorRocha`), um RETRATO pra quem chega (4 bytes por rocha) e depois so o
/// que muda (14 bytes). No cliente, um tile por rocha numa camada so. A tecla E a enxerga pela mesma tabela
/// de toda mobilia (`Interacoes.De`).
/// ================================================================================
///
/// ============================ O QUE NAO VAI PRO DISCO ============================
/// Nada daqui. Num reinicio as rochas renascem pela reserva do planeta, que e o que persiste (e quem a
/// persiste e o dono da economia). Planeta sem ninguem nao e simulado: o relogio de reposicao so anda com
/// gente olhando, e a conta do tempo que passou e acertada quando alguem chega.
/// =================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>As rochas de UM planeta.</summary>
	private sealed class CampoDeRochas
	{
		public required ZoneKey Zona;

		/// <summary>As celulas com rocha de pe.</summary>
		public readonly HashSet<(int X, int Y)> DePe = [];

		/// <summary>
		/// O MAPA EM QUE ELAS ESTAO ASSENTADAS. O de um mundo gerado e descartado quando o planeta esvazia e
		/// refeito na volta (`PodarZonasVazias`): a referencia e largada junto, e as rochas sao assentadas de
		/// novo no objeto novo -- as celulas sao as mesmas, porque o chao sai da mesma seed.
		/// </summary>
		public ZoneCollision? Mapa;

		/// <summary>O teto de rochas deste planeta, pela terra em que se anda. Ver <see cref="RochaDeMinerio.Teto"/>.</summary>
		public int Teto;

		/// <summary>A celula de chegada do planeta: em volta dela nao nasce rocha.</summary>
		public (int X, int Y) Chegada;

		/// <summary>A primeira leva (ate o alvo) ja nasceu.</summary>
		public bool Semeado;

		/// <summary>Ate que instante o relogio de reposicao ja foi contado, em ms.</summary>
		public long ContadoAte;

		/// <summary>A fracao de rocha que o relogio ja juntou.</summary>
		public double Credito;
	}

	/// <summary>As rochas de cada planeta, pela zona (<see cref="ZoneKey.Hash"/>).</summary>
	private readonly Dictionary<ulong, CampoDeRochas> _rochas = [];

	/// <summary>
	/// DE QUE ZONA E O RETRATO QUE CADA TELA TEM. A chave e o CORPO e nao o id: id de sessao se reusa, e um
	/// relog com o mesmo numero herdaria "ja mandei" de quem saiu.
	///
	/// E por aqui que o servidor sabe quem chegou: o tique compara a zona do corpo com a do retrato dele, e
	/// isso vale igual pro login, pra viagem, pra morte, pro admin e pro que mais mover alguem de planeta --
	/// nao ha uma linha de "mande as rochas" pra esquecer em cada porta de entrada.
	/// </summary>
	private readonly Dictionary<ServerPlayer, ulong> _rochasVistas = [];

	private long _proximaVoltaDasRochas;

	/// <summary>De quanto em quanto tempo o relogio de reposicao e olhado, em ms. O ritmo dele e do Core.</summary>
	private const long MsEntreVoltasDasRochas = 1000;

	/// <summary>A que distancia da celula de chegada do planeta uma rocha pode nascer, em tiles.</summary>
	private const int DistanciaDaChegada = 4;

	/// <summary>O SORTEIO DA BANCADA: o rendimento fixo no lugar do `_rng`. Nulo em jogo.</summary>
	internal double? RendimentoDaRochaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--rochasteste`): quem chega com os pes dentro de pedra fica la -- o corpo
	/// so sai andando, pelo escape do passo. Falso em jogo, sempre.
	/// </summary>
	internal bool PresoNaRochaDeTeste;

	/// <summary>A ESCUTA DA BANCADA: cada pacote de rocha que saiu, e pra quem. Nula em jogo.</summary>
	internal static List<(int Para, byte[] Fio)>? EscutaDeRochas;

	/// <summary>A conta dos corpos forjados da bancada: com a escuta ligada, eles contam como telas.</summary>
	internal const string ContaDaBancadaDeRochas = "bancada_rochas";

	/// <summary>Este corpo tem uma tela que desenha rochas? Jogador -- ou o corpo forjado da bancada, com a escuta ligada.</summary>
	private static bool VeRochas(ServerPlayer p) =>
		p.Peer != null || (EscutaDeRochas != null && p.Conta == ContaDaBancadaDeRochas);

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>
	/// DUAS COISAS, e a primeira e barata de proposito (roda no tique cheio):
	///   1. quem mudou de zona recebe o retrato da zona nova -- e, se chegou num planeta, o planeta acerta a
	///      conta do tempo em que ficou sem ninguem;
	///   2. uma vez por segundo, os planetas COM GENTE repoem o que o relogio deve.
	/// </summary>
	private void TickDasRochas()
	{
		long agora = NowMs();

		foreach (ServerPlayer pl in _players.Values)
		{
			if (!VeRochas(pl)) continue;
			ulong zona = pl.Zone.Hash;
			if (_rochasVistas.TryGetValue(pl, out ulong vista) && vista == zona) continue;
			if (ReceberNoPlaneta(pl, agora)) _rochasVistas[pl] = zona;
		}

		if (agora < _proximaVoltaDasRochas) return;
		_proximaVoltaDasRochas = agora + MsEntreVoltasDasRochas;

		// QUEM SAIU LEVA A MARCA DELE. `_players` devolve OUTRO corpo pro mesmo id depois de um relog.
		if (_rochasVistas.Count > 0)
		{
			List<ServerPlayer>? foram = null;
			foreach (ServerPlayer p in _rochasVistas.Keys)
				if (!_players.TryGetValue(p.Id, out ServerPlayer? vivo) || !ReferenceEquals(vivo, p)) (foram ??= []).Add(p);
			if (foram != null) foreach (ServerPlayer p in foram) _rochasVistas.Remove(p);
		}

		foreach (CampoDeRochas campo in _rochas.Values)
		{
			// O MUNDO GERADO FOI DESCARREGADO: larga o mapa dele (as celulas ficam). Ver `CampoDeRochas.Mapa`.
			if (campo.Mapa != null && EhZonaProcedural(campo.Zona) && !ReferenceEquals(MapaDaZona(campo.Zona), campo.Mapa))
				campo.Mapa = null;

			if (campo.Mapa == null || !TemTelaNoPlaneta(campo.Zona)) continue;   // sem ninguem, nao simula
			AcertarRochas(campo, agora, anunciar: true);
		}
	}

	private bool TemTelaNoPlaneta(ZoneKey zona)
	{
		if (!_zones.TryGetValue(zona.Hash, out List<ServerPlayer>? gente)) return false;
		foreach (ServerPlayer p in gente)
			if (VeRochas(p)) return true;
		return false;
	}

	/// <summary>
	/// ALGUEM CHEGOU NUMA ZONA. Devolve falso quando ainda nao da pra responder (o mapa do planeta nao
	/// existe neste tique): o tique pergunta de novo no seguinte.
	///
	/// FORA DE PLANETA O RETRATO SAI VAZIO, e sai sempre: e ele quem diz a tela "aqui nao ha rocha nenhuma",
	/// em vez de ela ficar com a lista do planeta de onde veio.
	/// </summary>
	private bool ReceberNoPlaneta(ServerPlayer pl, long agora)
	{
		CampoDeRochas? campo = null;
		if (Espaco.EhPlaneta(pl.Zone) && !ZonaMorta(pl.Zone))
		{
			if (MapaDaZonaOuCatalogo(pl.Zone) is not { } mapa) return false;
			campo = CampoDe(pl.Zone, mapa);
		}

		if (campo != null)
		{
			// A CONTA DO PLANETA ANTES DO RETRATO: quem chega ve o resultado, e nunca as rochas pipocando.
			AcertarRochas(campo, agora, anunciar: true);
			SoltarDaRocha(pl, campo.Mapa!);
		}

		MandarRochas(pl, campo);
		return true;
	}

	/// <summary>
	/// O CAMPO DE ROCHAS DE UM PLANETA, criado na primeira vez que alguem pisa nele. Nulo onde nao ha reserva
	/// -- e a reserva e quem diz o que e planeta (<see cref="ReservaDe"/>): o Mirante, o alem, a Sala do
	/// Tempo, interior de nave e o espaco ficam de fora por ela.
	/// </summary>
	private CampoDeRochas? CampoDe(ZoneKey zona, ZoneCollision mapa)
	{
		if (ReservaDe(zona) == null) return null;

		if (!_rochas.TryGetValue(zona.Hash, out CampoDeRochas? campo))
		{
			Habitat? terra = HabitatDaZona(zona);
			Vec2 chegada = ChegadaDaZonaGerada(zona) ?? PontoDeNascimento(zona);
			campo = new CampoDeRochas
			{
				Zona = zona,
				Teto = RochaDeMinerio.Teto(terra?.Celulas ?? 0),
				Chegada = ((int)MathF.Floor(chegada.X / ZoneCollision.TileSize),
						   (int)MathF.Floor(chegada.Y / ZoneCollision.TileSize)),
			};
			_rochas[zona.Hash] = campo;
		}

		if (!ReferenceEquals(campo.Mapa, mapa))
		{
			// MAPA NOVO (a primeira vez, ou o mundo gerado que renasceu): as rochas de pe voltam a barrar nele.
			campo.Mapa = mapa;
			foreach ((int x, int y) in campo.DePe) RochaDeMinerio.Assentar(mapa, x, y);
		}
		return campo;
	}

	// =====================================================================
	// QUANTAS, E QUANDO
	// =====================================================================
	/// <summary>
	/// POE O PLANETA EM DIA: quantas rochas a reserva sustenta agora, e quantas o tempo que passou deve.
	///
	/// A PRIMEIRA VEZ enche ate o alvo de uma vez -- o planeta sempre "teve" as rochas dele; ninguem pousa
	/// num mundo pelado pra ve-lo brotar. Dali em diante quem repoe e o relogio
	/// (<see cref="RochaDeMinerio.SegundosPorRocha"/>), contado pelo tempo de parede: o planeta que ficou uma
	/// hora sem ninguem recebe na chegada o que essa hora devia.
	///
	/// ACIMA DO ALVO NAO NASCE NADA, e nada some: se uma Drill secou o planeta, as rochas que ja estavam de
	/// pe ficam ate alguem colher (e rendem o que a reserva ainda tiver). O relogio nao junta credito
	/// enquanto o planeta esta cheio -- a espera comeca na coleta.
	/// </summary>
	private void AcertarRochas(CampoDeRochas campo, long agora, bool anunciar)
	{
		if (campo.Mapa == null || ReservaDe(campo.Zona) is not { } reserva) return;
		int alvo = RochaDeMinerio.Alvo(campo.Teto, reserva.Fracao);

		if (!campo.Semeado)
		{
			campo.Semeado = true;
			campo.ContadoAte = agora;
			// UM SORTEIO AZARADO NAO ENCERRA A LEVA: so desiste depois de varias rochas seguidas sem lugar
			// (planeta sem chao que sirva). Cada uma ja tentou `TentativasPorRocha` celulas.
			for (int semLugar = 0; campo.DePe.Count < alvo && semLugar < FalhasQueEncerramALeva;)
				semLugar = NascerRocha(campo, anunciar) ? 0 : semLugar + 1;
			GD.Print($"[server] rochas de minerio em {campo.Zona.Name}: {campo.DePe.Count} de pe "
				   + $"(teto {campo.Teto}, reserva {reserva.Fracao * 100:0}%)");
			return;
		}

		double segundos = Math.Max(0, agora - campo.ContadoAte) / 1000.0;
		campo.ContadoAte = agora;

		int falta = alvo - campo.DePe.Count;
		if (falta <= 0) { campo.Credito = 0; return; }

		campo.Credito = Math.Min(falta, campo.Credito + segundos / RochaDeMinerio.SegundosPorRocha(campo.Teto));
		// O CREDITO SO E GASTO PELA ROCHA QUE NASCEU: sem lugar nesta volta, ele fica pra proxima.
		while (campo.Credito >= 1 && campo.DePe.Count < alvo && NascerRocha(campo, anunciar)) campo.Credito -= 1;
	}

	/// <summary>Quantas rochas seguidas sem lugar encerram a primeira leva de um planeta.</summary>
	private const int FalhasQueEncerramALeva = 8;

	/// <summary>Uma rocha nasce num lugar sorteado. Falso quando nenhum dos lugares tentados serviu.</summary>
	private bool NascerRocha(CampoDeRochas campo, bool anunciar)
	{
		if (SortearLugarDeRocha(campo) is not { } c) return false;
		PorRocha(campo, c.X, c.Y, anunciar);
		return true;
	}

	private void PorRocha(CampoDeRochas campo, int cx, int cy, bool anunciar)
	{
		if (!campo.DePe.Add((cx, cy))) return;
		if (campo.Mapa != null) RochaDeMinerio.Assentar(campo.Mapa, cx, cy);
		if (anunciar) AnunciarRocha(campo, Protocol.RochasNasceu, cx, cy);
	}

	private void TirarRocha(CampoDeRochas campo, int cx, int cy)
	{
		if (!campo.DePe.Remove((cx, cy))) return;
		campo.Mapa?.TirarRocha(cx, cy);
		AnunciarRocha(campo, Protocol.RochasSumiu, cx, cy);
	}

	// =====================================================================
	// ONDE
	// =====================================================================
	/// <summary>
	/// UM LUGAR AO ACASO NO PLANETA INTEIRO, entre os que servem. Sorteia a celula e confere; nao enumera o
	/// planeta (sao ate um milhao de celulas) nem guarda lista de vagas -- a terra que serve e a maior parte
	/// do mapa, e errar o sorteio custa uma duzia de leituras de bit.
	/// </summary>
	private (int X, int Y)? SortearLugarDeRocha(CampoDeRochas campo)
	{
		if (campo.Mapa is not { } mapa) return null;
		Habitat? terra = HabitatDaZona(campo.Zona);
		_zones.TryGetValue(campo.Zona.Hash, out List<ServerPlayer>? gente);

		for (int t = 0; t < RochaDeMinerio.TentativasPorRocha; t++)
		{
			int cx = _rng.Next(mapa.Width), cy = _rng.Next(mapa.Height);
			if (LugarDeRochaServe(campo, mapa, terra, gente, cx, cy)) return (cx, cy);
		}
		return null;
	}

	/// <summary>
	/// ESTA CELULA PODE GANHAR UMA ROCHA AGORA? As recusas, da mais barata pra mais cara:
	///   * fora da terra em que se anda (o <see cref="Habitat"/>: ilhota, patio murado e telhado nao contam);
	///   * o chao e as oito celulas em volta (<see cref="RochaDeMinerio.ChaoServe"/>);
	///   * perto demais de outra rocha, de alguem, ou do ponto de chegada do planeta;
	///   * em cima de alguma coisa que esta no chao: obra solta, nave, piso ou parede de base, esfera.
	/// </summary>
	private bool LugarDeRochaServe(CampoDeRochas campo, ZoneCollision mapa, Habitat? terra,
								   List<ServerPlayer>? gente, int cx, int cy)
	{
		if (terra != null && !terra.Navegavel(cx, cy)) return false;
		if (!RochaDeMinerio.ChaoServe(mapa, cx, cy)) return false;

		if (Math.Max(Math.Abs(cx - campo.Chegada.X), Math.Abs(cy - campo.Chegada.Y)) < DistanciaDaChegada) return false;

		int d = RochaDeMinerio.DistanciaEntreRochas - 1;
		for (int dy = -d; dy <= d; dy++)
			for (int dx = -d; dx <= d; dx++)
				if (campo.DePe.Contains((cx + dx, cy + dy))) return false;

		if (gente != null)
			foreach (ServerPlayer o in gente)
			{
				(int px, int py) = CelulaInterna.CelulaDoCorpo(o.Pos);
				int limite = VeRochas(o) ? RochaDeMinerio.DistanciaDeJogador : RochaDeMinerio.DistanciaDeCorpo;
				if (Math.Max(Math.Abs(px - cx), Math.Abs(py - cy)) < limite) return false;
			}

		ZoneKey zona = campo.Zona;
		if (ObraNaCelula(zona, cx, cy) != null || NaveNaCelula(zona, cx, cy) != null || BlocoEm(zona, cx, cy) != null)
			return false;
		foreach (Esfera e in _esferas)
		{
			if (e.Portador != 0 || !e.Zona.Equals(zona)) continue;
			(int ex, int ey) = Jandirus.Core.Tech.CatalogoDeObras.Celula(e.X, e.Y);
			if (ex == cx && ey == cy) return false;
		}

		// O CANTEIRO DO POMAR FICA LIVRE, com a macieira de pe ou nao: a que cai volta a nascer na mesma
		// celula (`GameServer.Pomar.cs`), e uma rocha ali, ou colada ali, seguraria a arvore ou seria
		// encostada por ela.
		if (_pomares.TryGetValue(zona.Hash, out PomarDaZona? pomar))
			foreach ((int ax, int ay) in pomar.Celulas)
				if (Math.Max(Math.Abs(ax - cx), Math.Abs(ay - cy)) <= 1) return false;
		return true;
	}

	/// <summary>Ha uma rocha de minerio de pe nesta celula? Quem pergunta e o soco e o cenario que cai.</summary>
	private bool RochaNaCelula(ZoneKey zona, int cx, int cy) =>
		_rochas.TryGetValue(zona.Hash, out CampoDeRochas? campo) && campo.DePe.Contains((cx, cy));

	// =====================================================================
	// COLHER
	// =====================================================================
	/// <summary>
	/// COLHE A ROCHA AO ALCANCE -- o botao da tecla E. O sorteio diz quanto a rocha PEDE; quem diz quanto sai
	/// e a reserva do planeta (<see cref="ReservaDePlaneta.Tirar"/>, a porta unica): num planeta quase seco a
	/// rocha rende menos, e num planeta zerado ela era so casca. De um jeito ou de outro ela some -- uma
	/// coleta por rocha.
	/// </summary>
	private void ColetarRocha(ServerPlayer pl)
	{
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "você não está em condição de colher nada."); return; }

		// PERTO DE UMA ROCHA? Quem responde e o servidor: o menu do cliente le a mesma conta
		// (`RochaDeMinerio.AoAlcance`), mas esconder botao nunca foi permissao.
		if (!_rochas.TryGetValue(pl.Zone.Hash, out CampoDeRochas? campo) || RochaAoAlcance(campo, pl.Pos) is not { } c)
		{
			Avisar(pl, "não há rocha de minério por perto.");
			return;
		}

		double pedido = RochaDeMinerio.Rendimento(RendimentoDaRochaDeTeste ?? _rng.NextDouble());
		double saiu = ReservaDe(pl.Zone)?.Tirar(pedido) ?? 0;
		DarRecursos(pl, saiu);
		TirarRocha(campo, c.X, c.Y);

		Avisar(pl, saiu <= 0 ? "a rocha se desfaz em pó: este planeta está sem recursos."
				 : saiu < pedido ? $"você quebra a rocha e tira {saiu:0} de recursos -- era o que restava no planeta."
				 : $"você quebra a rocha e tira {saiu:0} de recursos.");
	}

	/// <summary>A rocha que este corpo alcanca, ou nulo. So pode haver uma: elas nascem a seis tiles umas das outras.</summary>
	private static (int X, int Y)? RochaAoAlcance(CampoDeRochas campo, Vec2 corpo)
	{
		const int t = ZoneCollision.TileSize;
		int raio = (int)MathF.Ceiling(Jandirus.Core.Tech.Interacoes.Alcance / t) + 1;
		int cx0 = (int)MathF.Floor(corpo.X / t), cy0 = (int)MathF.Floor(corpo.Y / t);

		(int X, int Y)? melhor = null;
		float menor = float.MaxValue;
		for (int dy = -raio; dy <= raio; dy++)
			for (int dx = -raio; dx <= raio; dx++)
			{
				(int X, int Y) c = (cx0 + dx, cy0 + dy);
				if (!campo.DePe.Contains(c) || !RochaDeMinerio.AoAlcance(corpo, c.X, c.Y)) continue;
				float dist = (RochaDeMinerio.Ponto(c.X, c.Y) - corpo).LengthSquared;
				if (dist >= menor) continue;
				menor = dist;
				melhor = c;
			}
		return melhor;
	}

	// =====================================================================
	// QUEM CHEGA COM OS PES NA PEDRA
	// =====================================================================
	/// <summary>
	/// QUEM CHEGA NUM PLANETA COM OS PES DENTRO DE ROCHA e posto no chao livre mais perto. Devolve se moveu.
	///
	/// O CASO REAL e o do personagem gravado num mundo gerado ANTES de as rochas grandes ganharem pegada
	/// (2026-10-10): ele volta pro mesmo ponto, e o ponto agora e pedra. O passo ja deixaria o corpo sair
	/// andando (`MoveRules.Escapar`, o escape de quem esta preso na geometria), mas ate ele dar esse passo o
	/// boneco estaria enfiado no desenho. Rocha de minerio nao cai em cima de ninguem (ver
	/// <see cref="LugarDeRochaServe"/>); a clausula dela existe pro corpo que foi POSTO ali por outro caminho.
	///
	/// "ROCHA" E SO ROCHA: a de minerio, e -- em mundo gerado -- a celula que barra e nao se quebra, que la
	/// e a pegada da rocha grande e mais nada. Montanha, arvore e parede de mapa desenhado nao entram: quem
	/// esta nelas chegou por outro defeito, e esconder defeito alheio nao e o servico daqui.
	/// </summary>
	private bool SoltarDaRocha(ServerPlayer pl, ZoneCollision mapa)
	{
		if (PresoNaRochaDeTeste) return false;
		if (Voo.ModoNaAltura(pl.Altitude, ModoDeTravessiaDe(pl)) == ModoDeTravessia.PorCima) return false;

		bool gerado = EhZonaProcedural(pl.Zone);
		bool EPedra(float x, float y)
		{
			int cx = (int)MathF.Floor(x / ZoneCollision.TileSize), cy = (int)MathF.Floor(y / ZoneCollision.TileSize);
			return mapa.TemRocha(cx, cy)
				   || (gerado && mapa.BloqueadaNoArquivo(cx, cy) && mapa.Indestrutivel(cx, cy) && !mapa.Aberta(cx, cy));
		}

		// A CAIXA DOS PES, a mesma do `MoveRules.Occupied`.
		float pes = pl.Pos.Y + MoveRules.FeetOffsetY;
		if (!EPedra(pl.Pos.X - MoveRules.BodyHalfW, pes - MoveRules.BodyHalfH)
			&& !EPedra(pl.Pos.X + MoveRules.BodyHalfW, pes - MoveRules.BodyHalfH)
			&& !EPedra(pl.Pos.X - MoveRules.BodyHalfW, pes + MoveRules.BodyHalfH)
			&& !EPedra(pl.Pos.X + MoveRules.BodyHalfW, pes + MoveRules.BodyHalfH))
			return false;

		Vec2 livre = MoveRules.RefugioPerto(mapa, pl.Pos, ModoDeTravessia.APe) ?? mapa.PontoLivrePerto(pl.Pos);
		GD.Print($"[server] {pl.Name} chegou com os pes dentro de rocha em {pl.Zone.Name} "
			   + $"({pl.Pos.X:0},{pl.Pos.Y:0}) -- posto em ({livre.X:0},{livre.Y:0})");
		CravarPosicao(pl, livre);
		Avisar(pl, "havia uma rocha onde você estava: você foi posto ao lado dela.");
		return true;
	}

	// =====================================================================
	// O FIO
	// =====================================================================
	private static void DespacharRochas(ServerPlayer para, NetDataWriter w)
	{
		EscutaDeRochas?.Add((para.Id, w.CopyData()));
		para.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O RETRATO pra UMA tela: a zona e todas as rochas de pe nela, num pacote. Vazio fora de planeta. Ver
	/// <see cref="Protocol.RochasRetrato"/>.
	/// </summary>
	private void MandarRochas(ServerPlayer pl, CampoDeRochas? campo)
	{
		NetDataWriter w = Protocol.Begin(Protocol.S2C.Rochas);
		w.Put(Protocol.RochasRetrato);
		w.Put(pl.Zone.Hash);
		w.Put((ushort)(campo?.DePe.Count ?? 0));
		if (campo != null)
			foreach ((int x, int y) in campo.DePe)
			{
				w.Put((ushort)x);
				w.Put((ushort)y);
			}
		DespacharRochas(pl, w);
	}

	/// <summary>
	/// UMA ROCHA NASCEU OU SUMIU: vai pra quem esta no planeta E ja tem o retrato dele. Quem acabou de chegar
	/// e ainda nao o recebeu fica de fora -- o retrato que vai sair pra ele ja traz esta mudanca.
	/// </summary>
	private void AnunciarRocha(CampoDeRochas campo, byte modo, int cx, int cy)
	{
		ulong zona = campo.Zona.Hash;
		if (!_zones.TryGetValue(zona, out List<ServerPlayer>? gente)) return;

		NetDataWriter? w = null;
		foreach (ServerPlayer o in gente)
		{
			if (!_rochasVistas.TryGetValue(o, out ulong vista) || vista != zona) continue;
			if (w == null)
			{
				w = Protocol.Begin(Protocol.S2C.Rochas);
				w.Put(modo);
				w.Put(zona);
				w.Put((ushort)cx);
				w.Put((ushort)cy);
			}
			DespacharRochas(o, w);
		}
	}

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--rochasteste`)
	// =====================================================================
	/// <summary>As rochas de pe num planeta, como o servidor as tem.</summary>
	internal IReadOnlyCollection<(int X, int Y)> RochasDeTeste(ZoneKey zona) =>
		_rochas.TryGetValue(zona.Hash, out CampoDeRochas? campo) ? campo.DePe : [];

	/// <summary>O teto de rochas que o servidor calculou pra este planeta (0 = ninguem pisou nele ainda).</summary>
	internal int TetoDeRochasDeTeste(ZoneKey zona) => _rochas.TryGetValue(zona.Hash, out CampoDeRochas? campo) ? campo.Teto : 0;

	/// <summary>O planeta volta a nunca ter sido visitado: as rochas saem do mapa e o campo e esquecido.</summary>
	internal void EsquecerRochasDeTeste(ZoneKey zona)
	{
		if (!_rochas.Remove(zona.Hash, out CampoDeRochas? campo)) return;
		if (campo.Mapa != null) foreach ((int x, int y) in campo.DePe) campo.Mapa.TirarRocha(x, y);
		foreach (ServerPlayer p in _rochasVistas.Keys.ToList())
			if (_rochasVistas[p] == zona.Hash) _rochasVistas.Remove(p);
	}

	/// <summary>Adianta o relogio de reposicao de um planeta. O tique de producao faz o resto.</summary>
	internal void AdiantarRochasDeTeste(ZoneKey zona, long ms)
	{
		if (_rochas.TryGetValue(zona.Hash, out CampoDeRochas? campo)) campo.ContadoAte -= ms;
		_proximaVoltaDasRochas = 0;
	}

	/// <summary>Poe uma rocha numa celula escolhida, pelo caminho de producao (conjunto, mapa e anuncio).</summary>
	internal bool PorRochaDeTeste(ZoneKey zona, int cx, int cy)
	{
		if (!_rochas.TryGetValue(zona.Hash, out CampoDeRochas? campo)) return false;
		PorRocha(campo, cx, cy, anunciar: true);
		return campo.DePe.Contains((cx, cy));
	}
}
