using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.Verbos;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ O RITUAL MISTICO DO VELHO KAIOHSHIN -- pedido do dono, 2026-10-23 ============================
/// O pedido palavra por palavra, as decisoes do dono e os numeros estao em <see cref="RitualDoKaioshin"/> (Core). Aqui fica a regra
/// viva, em quatro tempos:
///
///   1. A OFERTA: o Kaioshin no trono, com alguem NA FRENTE (o marcado, ao alcance do soco -- `QuemEstaNaFrente`), aperta o botao do
///      ritual (aba Skills). O convite vai pro MESMO cartao do Unlock Potential (`Fatos.OfertaDePotencial`): os botoes "Accept / Decline
///      Potential" e o cartao de pedido ja existiam, e o que o ritual da E o despertar do potencial -- mais a forma. UM convite por alvo:
///      o do ritual e o do Unlock de um Grand Kai se substituem (o mais novo fica, como dois Unlock ja faziam).
///   2. O ACEITE COMECA O RITUAL: os dois ficam TRAVADOS no lugar (`PodeMexerOCorpo`, mais o efeito `ritual_kaioshin` pro cliente nem
///      prever o passo), e o chat de cada um conta os minutos.
///   3. O QUE QUEBRA O RITUAL antes dos 10 minutos -- e NAO gasta o dia (o dono): o botao do ritual apertado por qualquer um dos dois (o
///      alvo o ve enquanto o ritual dura, `Fatos.RitualDoKaioshin`); um dos dois sair do lugar (arremesso, teleporte, agarrao), entrar
///      numa luta (um golpe dado ou levado), cair, sair do mundo ou ir pra outra zona; e o Kaioshin perder o trono.
///   4. O FIM: o potencial oculto do alvo desperta (`DespertarPotencialG11`, uma vez na vida -- quem ja despertou ganha so a forma), a
///      forma abre (`ConcederMistico` -- o Potential Unleashed) e o dia do Kaioshin se gasta (`Fighter.RitualDoKaioshinEm`).
///
/// AS DECISOES QUE O DONO NAO DITOU, e por que:
///   * o alvo e GENTE (`EhPessoa`, como o do Unlock) e nunca o proprio Kaioshin -- "na frente do outro";
///   * o alvo que ja tem as duas coisas (o potencial desperto e a forma) e recusado na OFERTA: dez minutos parado a toa nao e ritual;
///   * o ritual nao cobra Ki nem zeni: o preco e o dia e os dez minutos parado;
///   * golpe e qualquer golpe TROCADO por um dos dois, dado ou levado (a `BrigandoComAlguem` do combate) -- o dano do mundo (gravidade,
///     vacuo) nao conta;
///   * NENHUM VISUAL NOVO: os dois ficam de pe, parados, com o progresso no chat (pose e efeito sao do dono decidir).
/// ==========================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>DEFEITO INJETADO (bancada `--ritualkaiteste`): nenhum dos dois fica travado -- o ritual corre com os dois andando. Lido pelo <see cref="PresoNoRitual"/>. Falso em jogo, sempre.</summary>
	internal static bool RitualSemTravaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--ritualkaiteste`): o dia nao e conferido -- o Kaioshin faz um ritual atras do outro. Lido pelo <see cref="OferecerORitual"/>. Falso em jogo, sempre.</summary>
	internal static bool RitualSemDiaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--ritualkaiteste`): o dia se gasta no COMECO -- o ritual interrompido gasta o dia. Lido pelo <see cref="ComecarORitual"/> e pelo <see cref="TerminarORitual"/>. Falso em jogo, sempre.</summary>
	internal static bool DiaDoRitualNoComecoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--ritualkaiteste`): nada quebra o ritual -- nem sair do lugar, nem golpe, nem queda. Lido pelo <see cref="PorQueORitualQuebra"/>. Falso em jogo, sempre.</summary>
	internal static bool RitualNaoQuebraDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--ritualkaiteste`): o fim do ritual nao da nada -- nem o potencial, nem a forma. Lido pelo <see cref="TerminarORitual"/>. Falso em jogo, sempre.</summary>
	internal static bool RitualSemDomDeTeste;

	/// <summary>UM RITUAL EM CURSO: quem faz, quem recebe, onde cada um estava, e o relogio.</summary>
	private sealed class RitualEmCurso
	{
		public required ServerPlayer Kaioshin, Alvo;
		public required ZoneKey Zona;
		public Vec2 AncoraDoKaioshin, AncoraDoAlvo;
		public long ComecouEm, AcabaEm;

		/// <summary>Quantos minutos o chat ja contou (o aviso de cada minuto sai uma vez so).</summary>
		public int MinutosDitos;

		/// <summary>A briga com alguem de cada um na ultima olhada (`CombatState.BrigandoComAlguem`): ela so CRESCE num golpe trocado.</summary>
		public double BrigaDoKaioshin, BrigaDoAlvo;
	}

	/// <summary>Os rituais em curso -- raramente mais de um (cinco Kaioshins no maximo).</summary>
	private readonly List<RitualEmCurso> _rituais = [];

	/// <summary>Os convites na mesa, pela CONTA de quem recebe (como o do Unlock): de quem e, e ate quando.</summary>
	private readonly Dictionary<string, (string ContaDoKaioshin, string NomeDoKaioshin, long Ate)> _ofertasDeRitual =
		new(StringComparer.Ordinal);

	/// <summary>Quantos minutos um ritual tem (as marcas do chat).</summary>
	private const int MinutosDoRitual = (int)(RitualDoKaioshin.DuracaoMs / RitualDoKaioshin.AvisoACadaMs);

	/// <summary>O ritual em que este corpo esta, como o Kaioshin ou como o alvo -- pela REFERENCIA (id de jogador se reusa).</summary>
	private RitualEmCurso? RitualDe(ServerPlayer pl)
	{
		foreach (RitualEmCurso r in _rituais)
			if (r.Kaioshin == pl || r.Alvo == pl) return r;
		return null;
	}

	/// <summary>NENHUM DOS DOIS SE MOVE (o dono) -- a linha do `PodeMexerOCorpo`.</summary>
	private bool PresoNoRitual(ServerPlayer pl) => _rituais.Count > 0 && !RitualSemTravaDeTeste && RitualDe(pl) != null;

	/// <summary>Este corpo carrega o trono do Kaioshin agora? -- pela CONTA, como todo cargo, em qualquer uma das cinco vagas.</summary>
	private bool EhKaioshinNoTrono(ServerPlayer pl) =>
		pl.Conta.Length > 0 && string.Equals(CargoDe(pl.Conta), RitualDoKaioshin.Cargo, StringComparison.OrdinalIgnoreCase);

	/// <summary>O que o ritual ainda da a este corpo (ver <see cref="RitualDoKaioshin.OQueDa"/>).</summary>
	private static (bool Potencial, bool Forma) OQueORitualDa(ServerPlayer alvo) =>
		RitualDoKaioshin.OQueDa(alvo.Ficha.unlockPotential, alvo.Forma.Despertou(Jandirus.Core.Forms.Catalogo.IdMistico));

	/// <summary>O canal de habilidade do ritual. Devolve se o id era o dele.</summary>
	private bool UsarVerboDoRitual(ServerPlayer pl, string id)
	{
		if (!string.Equals(id, RitualDoKaioshin.Verbo, StringComparison.Ordinal)) return false;

		// NO MEIO DE UM RITUAL O BOTAO O INTERROMPE -- dos dois lados: e a saida de quem esta travado.
		if (RitualDe(pl) is { } emCurso)
		{
			InterromperORitual(emCurso, $"{pl.Name} interrompeu o ritual");
			return true;
		}
		OferecerORitual(pl);
		return true;
	}

	/// <summary>A OFERTA: as portas, e o convite no cartao de quem esta na frente.</summary>
	private void OferecerORitual(ServerPlayer pl)
	{
		if (!EhKaioshinNoTrono(pl))
		{
			Avisar(pl, $"só o Kaioshin no trono faz o {RitualDoKaioshin.Nome}.");
			return;
		}
		if (RecusarCaido(pl)) return;

		long agora = NowMs();
		long falta = RitualDoKaioshin.FaltaPraOProximo(pl.Ficha.RitualDoKaioshinEm, agora);
		if (falta > 0 && !RitualSemDiaDeTeste)
		{
			Avisar(pl, $"você já fez o Ritual Místico hoje: o próximo em {RitualDoKaioshin.HorasEMinutos(falta)} (tempo real).");
			return;
		}

		ServerPlayer? alvo = QuemEstaNaFrente(pl, o => EhPessoa(o) && o.Conta.Length > 0);
		if (alvo == null)
		{
			Avisar(pl, "fique na frente de quem vai receber o ritual (marque-o e encoste nele).");
			return;
		}
		if (RitualDe(alvo) != null)
		{
			Avisar(pl, $"{alvo.Name} já está no meio de um ritual.");
			return;
		}
		(bool potencial, bool forma) = OQueORitualDa(alvo);
		if (!potencial && !forma)
		{
			Avisar(pl, $"{alvo.Name} já tem o potencial desperto e o Potential Unleashed: o ritual não tem o que dar.");
			return;
		}

		// UM CONVITE POR ALVO: o do Unlock Potential usa o mesmo cartao, e o mais novo fica.
		_ofertasDePotencialG11.Remove(alvo.Conta);
		_ofertasDeRitual[alvo.Conta] = (pl.Conta, pl.Name, agora + RitualDoKaioshin.PrazoDaOfertaMs);
		Avisar(pl, $"você oferece a {alvo.Name} o {RitualDoKaioshin.Nome}. Agora é com {alvo.Name}.");
		Avisar(alvo, $"{pl.Name}, o Kaioshin, quer fazer em você o {RitualDoKaioshin.Nome}: {MinutosDoRitual} minutos parados, frente a "
					 + $"frente -- e o seu potencial desperta. (aceite ou recuse no cartão, em {RitualDoKaioshin.PrazoDaOfertaMs / 1000}s)");
		GD.Print($"[server] ritual: {pl.Name} ofereceu o Ritual Mistico a {alvo.Name}");
	}

	/// <summary>O cartao do convite -- chamado do `PedidosDe` (`GameServer.Pedidos.cs`), no mesmo tique dos fatos.</summary>
	private void PedidoDoRitual(ServerPlayer pl, List<PedidoNaTela> saida, long agora)
	{
		if (_ofertasDeRitual.TryGetValue(pl.Conta, out (string ContaDoKaioshin, string NomeDoKaioshin, long Ate) o) && agora <= o.Ate)
			saida.Add(new(Fatos.OfertaDePotencial,
						  $"{o.NomeDoKaioshin}, o Kaioshin, quer fazer em você o {RitualDoKaioshin.Nome}: {MinutosDoRitual} minutos parados, frente a frente.",
						  SegundosAte(o.Ate, agora)));
	}

	/// <summary>
	/// A RESPOSTA AO CONVITE -- chamada no topo do `ResponderPotencialG11` (os botoes sao os mesmos). Devolve se o convite era do ritual.
	/// </summary>
	private bool ResponderAoRitual(ServerPlayer pl, bool aceitou)
	{
		if (pl.Conta.Length == 0 || !_ofertasDeRitual.TryGetValue(pl.Conta, out (string ContaDoKaioshin, string NomeDoKaioshin, long Ate) o))
			return false;
		_ofertasDeRitual.Remove(pl.Conta);
		if (NowMs() > o.Ate)
		{
			Avisar(pl, "o convite do ritual já venceu.");
			return true;
		}

		ServerPlayer? kaioshin = OnlinePorConta(o.ContaDoKaioshin);
		if (!aceitou)
		{
			Avisar(pl, "você recusa o ritual.");
			if (kaioshin != null) Avisar(kaioshin, $"{pl.Name} recusou o ritual.");
			return true;
		}
		if (kaioshin == null)
		{
			Avisar(pl, "o Kaioshin que te ofereceu o ritual não está mais aqui.");
			return true;
		}
		ComecarORitual(kaioshin, pl);
		return true;
	}

	/// <summary>
	/// O ACEITE: as portas de novo (passou ate um minuto desde a oferta), e os dois ficam travados frente a frente. Devolve se comecou.
	/// </summary>
	private bool ComecarORitual(ServerPlayer kaioshin, ServerPlayer alvo)
	{
		string? porque =
			!EhKaioshinNoTrono(kaioshin) ? $"{kaioshin.Name} não é mais o Kaioshin"
			: RitualDe(kaioshin) != null || RitualDe(alvo) != null ? "um dos dois já está no meio de um ritual"
			: RitualDoKaioshin.FaltaPraOProximo(kaioshin.Ficha.RitualDoKaioshinEm, NowMs()) > 0 && !RitualSemDiaDeTeste
				? $"{kaioshin.Name} já fez o ritual hoje"
			: Caido(kaioshin) || Caido(alvo) ? "um dos dois está caído"
			: !kaioshin.Zone.Equals(alvo.Zone)
			  || Vec2.Distance(kaioshin.Pos, alvo.Pos) > RitualDoKaioshin.TilesDoAceite * Jandirus.Core.World.ZoneCollision.TileSize
				? $"{alvo.Name} precisa estar ao lado de {kaioshin.Name}"
			: OQueORitualDa(alvo) is (false, false) ? $"{alvo.Name} já tem tudo o que o ritual dá"
			: null;
		if (porque != null)
		{
			string recusa = $"o ritual não começa: {porque}.";
			Avisar(alvo, recusa);
			Avisar(kaioshin, recusa);
			return false;
		}

		long agora = NowMs();
		var r = new RitualEmCurso
		{
			Kaioshin = kaioshin, Alvo = alvo, Zona = kaioshin.Zone,
			AncoraDoKaioshin = kaioshin.Pos, AncoraDoAlvo = alvo.Pos,
			ComecouEm = agora, AcabaEm = agora + RitualDoKaioshin.DuracaoMs,
			BrigaDoKaioshin = kaioshin.Combate?.BrigandoComAlguem ?? 0, BrigaDoAlvo = alvo.Combate?.BrigandoComAlguem ?? 0,
		};
		_rituais.Add(r);
		if (DiaDoRitualNoComecoDeTeste) kaioshin.Ficha.RitualDoKaioshinEm = agora;

		MandarEfeito(kaioshin, RitualDoKaioshin.EfeitoDaTrava, -1);
		MandarEfeito(alvo, RitualDoKaioshin.EfeitoDaTrava, -1);
		AvisarPertoG3(kaioshin, RaioDaVista, $"{kaioshin.Name} começa o {RitualDoKaioshin.Nome} em {alvo.Name}.");
		string comecou = $"o ritual começou: {MinutosDoRitual} minutos frente a frente, e nenhum dos dois se move. O botão do ritual o "
						 + "interrompe (o dia do Kaioshin só se gasta no fim).";
		Avisar(kaioshin, comecou);
		Avisar(alvo, comecou);
		GD.Print($"[server] ritual: {kaioshin.Name} comecou o Ritual Mistico em {alvo.Name}");
		return true;
	}

	/// <summary>
	/// O TIQUE DOS RITUAIS -- 1 Hz, no `TickDosCargos`: o que quebra cada um, os minutos no chat e o fim. Dez minutos de ritual nao pedem
	/// mais que um segundo de resolucao, e a trava dos dois e por tique (o `PodeMexerOCorpo`).
	/// </summary>
	private void TickDosRituais()
	{
		long agora = NowMs();
		// OS CONVITES VENCIDOS SAEM (o cartao ja nao os mostrava: e so faxina).
		if (_ofertasDeRitual.Count > 0)
			foreach (string conta in _ofertasDeRitual.Where(kv => agora > kv.Value.Ate).Select(kv => kv.Key).ToList())
				_ofertasDeRitual.Remove(conta);
		if (_rituais.Count == 0) return;

		foreach (RitualEmCurso r in _rituais.ToList())
		{
			if (PorQueORitualQuebra(r) is { } motivo) { InterromperORitual(r, motivo); continue; }
			if (agora >= r.AcabaEm) { TerminarORitual(r); continue; }

			int minutos = (int)Math.Min((agora - r.ComecouEm) / RitualDoKaioshin.AvisoACadaMs, MinutosDoRitual - 1);
			if (minutos <= r.MinutosDitos) continue;
			r.MinutosDitos = minutos;
			string aviso = $"o Ritual Místico continua: {minutos} de {MinutosDoRitual} minutos. Fiquem parados.";
			Avisar(r.Kaioshin, aviso);
			Avisar(r.Alvo, aviso);
		}
	}

	/// <summary>
	/// O QUE QUEBRA ESTE RITUAL AGORA -- nulo = nada. A ORDEM e a da frase mais util: o trono, o mundo, a zona, a queda, o lugar e, por
	/// ultimo, a luta (a briga e LIDA e ANOTADA aqui, entao esta conta roda uma vez por tique).
	/// </summary>
	private string? PorQueORitualQuebra(RitualEmCurso r)
	{
		if (RitualNaoQuebraDeTeste) return null;
		if (!EhKaioshinNoTrono(r.Kaioshin)) return $"{r.Kaioshin.Name} não é mais o Kaioshin";
		foreach (ServerPlayer p in new[] { r.Kaioshin, r.Alvo })
		{
			if (!_players.TryGetValue(p.Id, out ServerPlayer? noMundo) || noMundo != p) return $"{p.Name} saiu do mundo";
			if (!p.Zone.Equals(r.Zona)) return $"{p.Name} foi pra outro lugar";
			if (Caido(p)) return $"{p.Name} caiu";
			Vec2 ancora = p == r.Kaioshin ? r.AncoraDoKaioshin : r.AncoraDoAlvo;
			if (Vec2.Distance(p.Pos, ancora) > RitualDoKaioshin.FolgaDaAncoraPx) return $"{p.Name} saiu do lugar";
		}

		// UM GOLPE DADO OU LEVADO: a briga com alguem so CRESCE num golpe trocado com outro corpo -- no resto do tempo ela escorre.
		double brigaK = r.Kaioshin.Combate?.BrigandoComAlguem ?? 0, brigaA = r.Alvo.Combate?.BrigandoComAlguem ?? 0;
		bool lutouK = brigaK > r.BrigaDoKaioshin + 1e-6, lutouA = brigaA > r.BrigaDoAlvo + 1e-6;
		r.BrigaDoKaioshin = brigaK;
		r.BrigaDoAlvo = brigaA;
		if (lutouK) return $"{r.Kaioshin.Name} entrou numa luta";
		if (lutouA) return $"{r.Alvo.Name} entrou numa luta";
		return null;
	}

	/// <summary>O RITUAL QUEBRA antes do fim: os dois se soltam, ouvem por que -- e o dia do Kaioshin NAO se gasta (o dono).</summary>
	private void InterromperORitual(RitualEmCurso r, string motivo)
	{
		_rituais.Remove(r);
		MandarEfeito(r.Kaioshin, RitualDoKaioshin.EfeitoDaTrava, 0);
		MandarEfeito(r.Alvo, RitualDoKaioshin.EfeitoDaTrava, 0);
		string aviso = $"o {RitualDoKaioshin.Nome} foi interrompido: {motivo}. O dia do Kaioshin não foi gasto.";
		Avisar(r.Kaioshin, aviso);
		Avisar(r.Alvo, aviso);
		GD.Print($"[server] ritual: o de {r.Kaioshin.Name} em {r.Alvo.Name} quebrou ({motivo})");
	}

	/// <summary>
	/// O FIM: o potencial do alvo desperta (uma vez na vida), a forma abre, e o dia do Kaioshin se gasta. Os dois se soltam.
	/// </summary>
	private void TerminarORitual(RitualEmCurso r)
	{
		_rituais.Remove(r);
		ServerPlayer kaioshin = r.Kaioshin, alvo = r.Alvo;
		MandarEfeito(kaioshin, RitualDoKaioshin.EfeitoDaTrava, 0);
		MandarEfeito(alvo, RitualDoKaioshin.EfeitoDaTrava, 0);

		// O DIA SE GASTA NO FIM -- o interrompido nao gasta (o dono).
		if (!DiaDoRitualNoComecoDeTeste) kaioshin.Ficha.RitualDoKaioshinEm = NowMs();

		AvisarPertoG3(kaioshin, RaioDaVista, $"{kaioshin.Name} termina o {RitualDoKaioshin.Nome} em {alvo.Name}.");
		if (!RitualSemDomDeTeste)
		{
			// O POTENCIAL OCULTO DESPERTA -- pela mesma porta do Unlock, do desejo e do Guru: uma vez na vida, e quem ja despertou ganha
			// so a forma (o dono).
			if (alvo.Ficha.unlockPotential < 1) DespertarPotencialG11(alvo, kaioshin);
			// ...E A FORMA ABRE: e um direito gravado (`Forma.Liberadas`), que ninguem tira por tique (`ReconciliarODomDoMistico`).
			ConcederMistico(alvo, $"o Ritual Mistico de {kaioshin.Name}");
		}
		HabilidadesMudaram(alvo);
		if (EhJogador(alvo)) Persistir(alvo);
		if (EhJogador(kaioshin)) Persistir(kaioshin);
		Avisar(kaioshin, $"o ritual terminou: o potencial de {alvo.Name} despertou. O seu próximo ritual em "
						 + $"{RitualDoKaioshin.HorasEMinutos(RitualDoKaioshin.RecargaMs)} (tempo real).");
		GD.Print($"[server] ritual: {kaioshin.Name} terminou o Ritual Mistico em {alvo.Name}");
	}
}
