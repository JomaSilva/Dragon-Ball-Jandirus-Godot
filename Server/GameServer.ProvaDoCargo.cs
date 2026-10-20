using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A PROVA DO ESPIRITO DO CARGO (2026-10-19, pedido do dono) -- o lado do servidor. A regra (os numeros, o juiz, as frases) mora em
/// `Core/Ranks/ProvaDoCargo.cs`; aqui ficam:
///   * a PORTA DE PRODUCAO: o pacote `C2S.Cargo` com uma chave chama <see cref="PedirAProvaDoCargo"/>, e nao mais o
///     `ReivindicarCargo` -- que continua sendo a CONCESSAO (as portas, os requisitos, o trono, o kit, a campanha e o anuncio), chamada
///     uma vez pra conferir antes da prova e outra no fim dela;
///   * o CORPO do espirito: fora do catalogo e do povoamento (como a simulacao do Simulador), com o tempero dele, os atributos de
///     quem o invocou, o poder EXPRESSO cravado em 1,15x o dele e morto DE PASSAGEM -- de pe, meio transparente, de aureola;
///   * o GUIA dele no tique dos corpos (o ramo dele no `TicarUmCorpo`) e o JUIZ de 1 Hz (no `TickDosCargos`).
/// </summary>
public partial class GameServer
{
	/// <summary>Uma prova em curso. Uma por cargo (`rq_trial_busy`), indexada pela chave do cargo e pelo id do espirito.</summary>
	private sealed class ProvaEmCurso
	{
		public required string Cargo;
		public required int Desafiante;
		public required string Assinatura;
		public required int Espirito;
		public required long AcabaEm;

		/// <summary>O poder EXPRESSO cravado no espirito: o de quem o invocou, NA HORA, vezes 1,15 (`:346`).</summary>
		public required double ExpressoDoEspirito;

		/// <summary>O espirito "morreu de novo" -- o gancho `AoMorrerDeNovo` dele (o golpe letal de quem o testa).</summary>
		public bool CaiuDeVez;
	}

	private readonly Dictionary<string, ProvaEmCurso> _provasDoCargo = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<int, ProvaEmCurso> _espiritosDoCargo = [];

	/// <summary>A VERGONHA: assinatura -> quando ela pode tentar de novo (`rq_claim_cd`). Por assinatura, e nao no corpo: o relog nao a apaga.</summary>
	private readonly Dictionary<string, long> _vergonhaDoCargo = [];

	// =====================================================================
	// A PORTA DE PRODUCAO
	// =====================================================================

	/// <summary>
	/// O "REIVINDICAR" DA ABA CARGOS (`mob/verb/Reivindicar_Rank`, `RankQuests.dm:269-327`): a vergonha e a trava primeiro, depois as
	/// portas e os requisitos do `ReivindicarCargo` (com as mesmas mensagens) -- e, passando tudo, o espirito se ergue.
	/// </summary>
	private void PedirAProvaDoCargo(ServerPlayer pl, string chave)
	{
		if (ProvaDoCargo.ProvaPuladaDeTeste)
		{
			ReivindicarCargo(pl, chave);
			return;
		}
		if (_provasDoCargo.Values.Any(p => p.Desafiante == pl.Id)) { Avisar(pl, "você já está no meio de uma prova."); return; }

		long agora = NowMs();
		if (pl.Assinatura.Length > 0 && _vergonhaDoCargo.TryGetValue(pl.Assinatura, out long ate) && ate > agora)
		{
			Avisar(pl, $"o espírito do cargo ainda zomba da sua última derrota ({ProvaDoCargo.MinutosAte(ate, agora)} min).");
			return;
		}
		if (Cargos.Get(chave) is { } ocupado && _provasDoCargo.ContainsKey(ocupado.Chave) && !ProvaDoCargo.SemTravaDeTeste)
		{
			Avisar(pl, "outra alma já enfrenta o espírito deste cargo AGORA.");
			return;
		}
		if (!ReivindicarCargo(pl, chave, conceder: false) || Cargos.Get(chave) is not { } r) return;
		if (!CabeMaisUmCorpo(pl.Zone, Povoamento.MaxPorZona, Povoamento.MaxNoServidor, out string porque))
		{
			Avisar(pl, $"o espírito do cargo não tem onde se erguer aqui ({porque}).");
			return;
		}

		ServerPlayer espirito = NascerOEspirito(pl, r, out double expresso);
		var prova = new ProvaEmCurso
		{
			Cargo = r.Chave, Desafiante = pl.Id, Assinatura = pl.Assinatura, Espirito = espirito.Id,
			AcabaEm = agora + ProvaDoCargo.MsDaProva, ExpressoDoEspirito = expresso,
		};
		_provasDoCargo[r.Chave] = prova;
		_espiritosDoCargo[espirito.Id] = prova;
		AvisarPertoG3(pl, RaioDaVista, $"O ESPÍRITO DE {r.Nome.ToUpperInvariant()} se materializa diante de {pl.Name}! \"Prove que merece o manto.\"");
		GD.Print($"[server] {pl.Name} ({pl.Conta}) enfrenta o Espirito do Cargo '{r.Chave}' (expresso {expresso:0})");
	}

	// =====================================================================
	// O CORPO DO ESPIRITO
	// =====================================================================

	/// <summary>
	/// O `mob/npc/Enemy/RankSpirit` (`RankQuests.dm:258-267`, `:340-348`): um corpo fora do catalogo e sem dono, ao lado de quem o invocou,
	/// com os atributos dele (ver o cabecalho do `Core/Ranks/ProvaDoCargo.cs`), o poder EXPRESSO cravado em 1,15x o dele e o golpe que
	/// nocauteia sem matar. Morto de passagem: de pe e de aureola entre os vivos.
	/// </summary>
	private ServerPlayer NascerOEspirito(ServerPlayer quem, RankDef r, out double expresso)
	{
		Fighter d = quem.Ficha;
		string raca = ProvaDoCargo.RacaDoEspirito(r.Chave);
		// O TEMPERO DO DM: `behavior_vals = list(90, 60, 0, 70) //corajoso, implacavel, sem piedade, taticamente sobrio` (`:267`) --
		// coragem, furia, bondade e logica (`NPCAI.dm:76`). A bondade nao tem manivela no port; as outras tres vao pelo molde, pela
		// mesma fabrica de todo NPC (`Temperamento.Montar`) -- e nao pelo cerebro de fabrica, que e o de quem nao declara tempero.
		var molde = new MoldeDeNpc { Id = "espirito_do_cargo", Racas = [raca], Classe = "Normal", Coragem = 90, Furia = 60, Inteligencia = 70 };
		ulong semente = SementeDoEspirito(r.Chave);
		var espirito = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,
			Name = ProvaDoCargo.NomeDoEspirito,
			Zone = quem.Zone,
			Pos = OndeOEspiritoSeErgue(quem),
			Facing = Facing.South,
			Race = raca,
			Class = "Normal",
			Genero = "Male",
			Planeta = quem.Planeta,
			Idade = 30,
			Visual = AparenciaDeNpc(molde, raca, "Male", semente),
			LastInputMs = NowMs(),
			Cerebro = Temperamento.Montar(molde, semente % 1000 / 1000.0, semente),
			Ficha = new Fighter(),
			Livro = new SkillBook(),
		};
		Fighter c = espirito.Ficha;
		c.Race = raca; c.Class = "Normal"; c.Idade = 30;
		c.kidef = d.Ekidef; c.physdef = d.Ephysdef + 1; c.speed = d.Espeed; c.physoff = d.Ephysoff + 1; c.technique = d.Etechnique;
		c.HP = 100;
		c.KO = false; c.dead = false;
		c.maxstamina = d.maxstamina; c.stamina = d.maxstamina;
		expresso = ProvaDoCargo.PoderDoEspirito(d.expressedBP);
		c.BP = expresso;
		c.Statify();
		c.Ki = c.MaxKi;
		c.PowerLevel();

		PorNoMundo(espirito);
		espirito.Combate.Letal = false;
		// MORTO DE PASSAGEM: a aureola (`FazerMortoDoAlem`) e o prazo da passagem que nunca vence -- de pe e meio transparente entre os
		// vivos (`ServerPlayer.EmPassagem`, `TickDasAureolas`). No Outro Mundo ele ja esta de pe.
		FazerMortoDoAlem(espirito);
		espirito.PassagemAte = long.MaxValue;
		// O GOLPE LETAL de quem o testa nao o manda pra mesa do Enma (`MorrerDeNovoNoAlem`): ele so cai, e o juiz decide.
		int id = espirito.Id;
		espirito.Combate.AoMorrerDeNovo = _ => { if (_espiritosDoCargo.TryGetValue(id, out ProvaEmCurso? p)) p.CaiuDeVez = true; };
		CravarOEspirito(espirito, expresso);
		TrocarAparencias(espirito);
		return espirito;
	}

	/// <summary>
	/// A semente da cara do espirito: a mesma pra cada cargo, rodada apos rodada (FNV-1a da chave -- o `GetHashCode` de texto do .NET
	/// muda a cada processo, e a foto de uma rodada nao bateria com a da outra).
	/// </summary>
	private static ulong SementeDoEspirito(string chave)
	{
		ulong h = 14695981039346656037UL;
		foreach (char ch in chave.ToLowerInvariant()) h = (h ^ ch) * 1099511628211UL;
		return h;
	}

	/// <summary>Um tile livre a ate 2 de quem invocou (`rq_trial_turf`, `:329-333`) -- o mais perto primeiro. Sem nenhum, o lugar dele.</summary>
	private Vec2 OndeOEspiritoSeErgue(ServerPlayer quem)
	{
		const float t = ZoneCollision.TileSize;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(quem.Zone);
		for (int raio = 1; raio <= ProvaDoCargo.TilesDoNascimento; raio++)
			for (int dy = -raio; dy <= raio; dy++)
				for (int dx = -raio; dx <= raio; dx++)
				{
					if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != raio) continue;
					var onde = quem.Pos + new Vec2(dx * t, dy * t);
					if (mapa == null || !mapa.BlockedAt(onde + new Vec2(0, MoveRules.FeetOffsetY))) return onde;
				}
		return quem.Pos;
	}

	/// <summary>
	/// CRAVA o poder do espirito no EXPRESSO pedido (`conq_pin_bp`, re-cravado porque o tique de fichas so SOBE o BP de quem treina --
	/// `:348`): o base que, no estado de agora, da aquele expresso (`BillsEWhis.BaseParaOExpresso`, a conta do treino com o Bills).
	/// </summary>
	private static void CravarOEspirito(ServerPlayer espirito, double expresso) =>
		CravarOPoder(espirito, BillsEWhis.BaseParaOExpresso(expresso, espirito.Ficha.BP, espirito.Ficha.expressedBP));

	/// <summary>
	/// QUEM O ESPIRITO CACA (o ramo dele no `TicarUmCorpo`): quem o invocou, e so ele (`S.foundTarget(M)`, `:353`; ocioso, re-engaja,
	/// `:363-365`). Caido, ou com quem o invocou fora do mapa ou no chao, ele para -- o juiz de 1 Hz encerra a prova.
	/// </summary>
	private bool GuiarOEspirito(ServerPlayer espirito, ProvaEmCurso prova, out ServerPlayer? presa, out Vec2 destino)
	{
		presa = null;
		destino = espirito.Pos;
		if (espirito.Ficha.KO || prova.CaiuDeVez) return false;
		if (!_players.TryGetValue(prova.Desafiante, out ServerPlayer? alvo) || alvo.Zone.Hash != espirito.Zone.Hash || alvo.Ficha.KO) return false;
		presa = alvo;
		destino = alvo.Pos;
		return true;
	}

	// =====================================================================
	// O JUIZ (1 Hz, no `TickDosCargos`)
	// =====================================================================

	/// <summary>O laco da prova (`:356-365`): recrava o espirito e pergunta ao juiz se ela acabou.</summary>
	private void TickDasProvas()
	{
		if (_provasDoCargo.Count == 0) return;
		long agora = NowMs();
		foreach (ProvaEmCurso prova in _provasDoCargo.Values.ToList())
		{
			_players.TryGetValue(prova.Espirito, out ServerPlayer? espirito);
			_players.TryGetValue(prova.Desafiante, out ServerPlayer? quem);
			if (espirito != null && !espirito.Ficha.KO && !prova.CaiuDeVez && !ProvaDoCargo.EspiritoSemCravoDeTeste)
				CravarOEspirito(espirito, prova.ExpressoDoEspirito);

			bool mesmaZona = espirito != null && quem != null && quem.Zone.Hash == espirito.Zone.Hash;
			double tiles = mesmaZona ? (quem!.Pos - espirito!.Pos).Length / ZoneCollision.TileSize : 0;
			var quadro = new ProvaDoCargo.QuadroDaProva(
				EspiritoExiste: espirito != null,
				EspiritoCaido: espirito != null && (espirito.Ficha.KO || prova.CaiuDeVez),
				QuemDerrubou: espirito?.UltimoAgressor ?? 0,
				Desafiante: prova.Desafiante,
				DesafianteNoJogo: quem != null,
				DesafianteKo: quem?.Ficha.KO ?? false,
				DesafianteMorto: quem?.Ficha.dead ?? false,
				CargoDoAlem: Cargos.Get(prova.Cargo)?.DoOutroMundo ?? false,
				MesmaZona: mesmaZona,
				DistanciaEmTiles: tiles,
				AgoraMs: agora,
				AcabaEm: prova.AcabaEm);
			if (ProvaDoCargo.Julgar(quadro) is { } fim) EncerrarAProva(prova, fim, espirito, quem, agora);
		}
	}

	/// <summary>
	/// O FIM DA PROVA (`:366-393`): o espirito sai do mundo, a trava se solta, a derrota deixa a vergonha -- e a vitoria chama a
	/// CONCESSAO de verdade (`ReivindicarCargo`), que confere de novo as portas e os requisitos (alguem pode ter tomado o trono, o
	/// Presidente pode ter gastado a campanha) antes de dar o cargo. Derrubar o espirito nao mexe em karma nem em reputacao (ele nao e
	/// gente), entao a conferencia do fim responde o mesmo que a do comeco.
	/// </summary>
	private void EncerrarAProva(ProvaEmCurso prova, ProvaDoCargo.FimDaProva fim, ServerPlayer? espirito, ServerPlayer? quem, long agora)
	{
		_provasDoCargo.Remove(prova.Cargo);
		_espiritosDoCargo.Remove(prova.Espirito);
		if (espirito != null) RemoverNpc(espirito);
		string nome = Cargos.Get(prova.Cargo)?.Nome ?? prova.Cargo;
		GD.Print($"[server] a prova do cargo '{prova.Cargo}' acabou: {fim}");

		if (ProvaDoCargo.Envergonha(fim) && !ProvaDoCargo.SemVergonhaDeTeste && prova.Assinatura.Length > 0)
			_vergonhaDoCargo[prova.Assinatura] = agora + ProvaDoCargo.MsDaVergonha;
		if (quem == null) return;

		if (fim == ProvaDoCargo.FimDaProva.Venceu)
		{
			Avisar(quem, $"o Espírito de {nome} cai.");
			ReivindicarCargo(quem, prova.Cargo);
		}
		else Avisar(quem, ProvaDoCargo.FraseDoFim(fim, nome));
		MandarCargos(quem);
	}

	/// <summary>O que a aba Cargos diz de um cargo cujo espirito esta sendo enfrentado agora -- e some o botao "Reivindicar" dele.</summary>
	private string? ProvaQueOcupa(RankDef r, ServerPlayer pl) =>
		!_provasDoCargo.TryGetValue(r.Chave, out ProvaEmCurso? p) ? null
		: p.Desafiante == pl.Id ? "você está na prova deste cargo agora" : "outra alma enfrenta o Espírito deste cargo agora";
}
