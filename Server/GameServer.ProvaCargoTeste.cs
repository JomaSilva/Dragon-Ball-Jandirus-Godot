using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--provacargoteste` (2026-10-19) -- A PROVA DO ESPIRITO DO CARGO e a campanha do Presidente (pedido do dono: *"conserte os
/// 3 e faça a prova do espirito"*). A regra mora em `Core/Ranks/ProvaDoCargo.cs`; o servidor, em `GameServer.ProvaDoCargo.cs`.
///
///     Godot --headless --server --port 9621 --provacargoteste
///
/// Um cliente cru entra pela porta (o "Reivindicar" e o pacote `C2S.Cargo` de verdade) e os outros corpos sao forjados. O juiz roda
/// pelo tique de 1 Hz dos cargos (`TickDosCargos`), e cada familia tem o defeito injetado que a mesma regua REPROVA:
///   P. A PORTA -- o pacote ergue o espirito (vago o trono, a ate 2 tiles, morto de passagem, 1,15x o expresso). `ProvaPuladaDeTeste`.
///   A. O ESPIRITO CACA quem o invocou (o tique da mente tira o plano dele do `Nada`).
///   B. O PODER FICA CRAVADO no tique da prova. `EspiritoSemCravoDeTeste`.
///   V. QUEM DERRUBA LEVA: o trono, o espirito fora do mundo, a trava solta.
///   I. O INTRUSO nao da o cargo nem vergonha. `IntrusoValeDeTeste`.
///   D. QUEM CAI perde e fica 30 min com vergonha. `SemVergonhaDeTeste`.
///   F. fugir, trocar de mapa e o tempo acabar tambem perdem.
///   T. UMA ALMA POR VEZ por cargo. `SemTravaDeTeste`.
///   M. o MORTO prova o cargo do Outro Mundo.
///   C. A CAMPANHA do Presidente e cobrada ao vencer -- e quem a gasta na prova nao leva. `CampanhaDeGracaDeTeste`.
///   S. SAIR DO JOGO no meio e derrota, e a vergonha sobrevive ao relog.
/// </summary>
public partial class GameServer
{
	private const string ContaDaBancadaDaProva = "bancada_prova_ag";
	private const int IdBaseDaProva = 99_700;

	private int _pcgOk, _pcgFalhou;
	private readonly List<ServerPlayer> _pcgForjados = [];

	private void AfirmarPcg(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _pcgOk++; GD.Print($"[provacargo]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _pcgFalhou++; GD.PrintErr($"[provacargo]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDaProvaDoCargo(int porta)
	{
		_pcgOk = _pcgFalhou = 0;
		GD.Print("[provacargo] ================ A PROVA DO ESPIRITO DO CARGO (dono, 2026-10-19) ================");
		var tronosReais = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		_tronos.Clear();
		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		try
		{
			ServerPlayer? pl = mesa.EntraNoMundo(cli, ContaDaBancadaDaProva, "Provado");
			AfirmarPcg("(montagem) o cliente entra pela porta e e jogador", pl != null && EhJogador(pl));
			if (pl == null) return;
			ApertoParaOEremita(pl);

			APortaPcg(mesa, cli, pl);
			OIntrusoPcg(pl);
			AQuedaEAVergonhaPcg(pl);
			AFugaPcg(pl);
			ATravaPcg(pl);
			OMortoDoAlemPcg();
			ACampanhaPcg(pl);
			ASaidaPcg(mesa, cli);
		}
		catch (Exception e) { AfirmarPcg("a bancada rodou ate o fim", false, e.ToString()); }
		finally
		{
			ProvaDoCargo.ProvaPuladaDeTeste = false;
			ProvaDoCargo.SemTravaDeTeste = false;
			ProvaDoCargo.SemVergonhaDeTeste = false;
			ProvaDoCargo.IntrusoValeDeTeste = false;
			ProvaDoCargo.EspiritoSemCravoDeTeste = false;
			ProvaDoCargo.CampanhaDeGracaDeTeste = false;
			LimparAsProvasPcg();
			foreach (ServerPlayer c in _pcgForjados)
			{
				ZoneList(c.Zone.Hash).Remove(c);
				_players.Remove(c.Id);
				foreach (Dictionary<string, double> livro in _reputacao.Values) livro.Remove(c.Conta);
			}
			_pcgForjados.Clear();
			mesa.Fecha();
			_tronos.Clear();
			foreach ((string k, string v) in tronosReais) _tronos[k] = v;
			SalvarCargos();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaBancadaDaProva + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch (Exception e) { GD.PushWarning($"[provacargo] nao apaguei '{ContaDaBancadaDaProva}': {e.Message}"); }
			GD.Print($"[provacargo] ================ {_pcgOk} OK, {_pcgFalhou} FALHA(S) ================");
		}
	}

	// ---- as maos ----

	/// <summary>O Eremita Tartaruga pede karma 25+ e 1M de BP base: o corpo de quem reivindica fica com isso.</summary>
	private static void ApertoParaOEremita(ServerPlayer pl)
	{
		pl.Ficha.BP = 1_000_000;
		pl.Karma = 30;
		pl.Ficha.KO = false;
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
	}

	private ServerPlayer ForjarPcg(int i, string nome, string raca, ZoneKey zona, Vec2 pos, double bp = 1_000_000, int karma = 30, double zeni = 0)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDaProva + i, Peer = null, Name = nome, Race = raca, Genero = "Male", Idade = 30,
			Zone = zona, Pos = pos, Conta = $"bancada_prova_{i}", Slot = 0,
			Ficha = new Fighter { Race = raca, BP = bp }, Livro = new SkillBook(), Karma = karma,
		};
		novo.Ficha.Class = "Normal";
		novo.Ficha.Zeni = zeni;
		PorNoMundo(novo);
		novo.Ficha.Statify();
		novo.Ficha.PowerLevel();
		_pcgForjados.Add(novo);
		return novo;
	}

	private ProvaEmCurso? ProvaPcg(string chave) => _provasDoCargo.GetValueOrDefault(chave);

	private ServerPlayer? EspiritoPcg(string chave) =>
		ProvaPcg(chave) is { } p && _players.TryGetValue(p.Espirito, out ServerPlayer? e) ? e : null;

	/// <summary>O "Reivindicar" pelo pacote de verdade: o cliente manda `C2S.Cargo` e o servidor o le no bombeio.</summary>
	private List<string> PedirPelaPortaPcg(MesaDeClientesCrus mesa, ClienteCruDeTeste cli, string chave) => Ouvir(() =>
	{
		var w = Protocol.Begin(Protocol.C2S.Cargo);
		w.Put(chave);
		cli.Mandar(w);
		mesa.Bombeia(() => _provasDoCargo.ContainsKey(chave) || _tronos.ContainsKey(chave), 3000);
	});

	/// <summary>
	/// A REGUA DA PORTA: a prova esta armada, o trono continua VAGO, e o espirito esta de pe no mesmo mapa, a ate 2 tiles (a diagonal
	/// conta), com o nome que o cliente desenha.
	/// </summary>
	private bool ErgueuOEspiritoPcg(ServerPlayer pl, string chave, out string detalhe)
	{
		ServerPlayer? e = EspiritoPcg(chave);
		double tiles = e != null && e.Zone.Hash == pl.Zone.Hash ? (e.Pos - pl.Pos).Length / ZoneCollision.TileSize : -1;
		detalhe = $"trono {_tronos.GetValueOrDefault(chave) ?? "(vago)"}; espirito {(e == null ? "(nenhum)" : $"'{e.Name}' a {tiles:0.0} tiles")}";
		return ProvaPcg(chave) != null && !_tronos.ContainsKey(chave) && e != null && tiles >= 0
			   && tiles <= ProvaDoCargo.TilesDoNascimento * 1.5 && e.Name == ProvaDoCargo.NomeDoEspirito;
	}

	/// <summary>Derruba o espirito "pelas maos" de alguem: a marca do agressor (a mesma do golpe) e o nocaute.</summary>
	private void DerrubarPcg(ServerPlayer espirito, ServerPlayer quem)
	{
		MarcarAgressao(espirito, quem);
		espirito.Combate.Nocautear(10);
	}

	/// <summary>Encerra a mao toda prova em curso (sem vencedor) e esquece a vergonha -- o comeco limpo de cada familia.</summary>
	private void LimparAsProvasPcg()
	{
		foreach (ProvaEmCurso p in _provasDoCargo.Values.ToList())
			if (_players.TryGetValue(p.Espirito, out ServerPlayer? e)) RemoverNpc(e);
		foreach (int id in _espiritosDoCargo.Keys.ToList())
			if (_players.TryGetValue(id, out ServerPlayer? e)) RemoverNpc(e);
		_provasDoCargo.Clear();
		_espiritosDoCargo.Clear();
		_vergonhaDoCargo.Clear();
	}

	private void DePe(ServerPlayer p)
	{
		if (p.Ficha.KO) p.Combate.Levantar();
		p.Ficha.KO = false;
	}

	// =====================================================================
	// P/A/B/V) A PORTA, A CACA, O CRAVO E A VITORIA
	// =====================================================================
	private void APortaPcg(MesaDeClientesCrus mesa, ClienteCruDeTeste cli, ServerPlayer pl)
	{
		GD.Print("[provacargo] -- P) a porta: o Reivindicar ergue o espirito --");

		// O DEFEITO PRIMEIRO: o jogo de antes, trono na hora
		ProvaDoCargo.ProvaPuladaDeTeste = true;
		try { PedirPelaPortaPcg(mesa, cli, "turtle"); }
		finally { ProvaDoCargo.ProvaPuladaDeTeste = false; }
		bool velho = ErgueuOEspiritoPcg(pl, "turtle", out string dVelho);
		AfirmarPcg("(defeito injetado: o Reivindicar sem prova, o jogo de antes) a mesma regua REPROVA -- o trono vem na hora e espirito nenhum",
				  !velho && _tronos.GetValueOrDefault("turtle") == pl.Conta, dVelho);
		_tronos.Remove("turtle");
		ReconciliarDadiva(pl);
		LimparAsProvasPcg();

		List<string> falas = PedirPelaPortaPcg(mesa, cli, "turtle");
		bool ergueu = ErgueuOEspiritoPcg(pl, "turtle", out string detalhe);
		AfirmarPcg("P. **O REIVINDICAR ERGUE O ESPIRITO**: o pacote `C2S.Cargo` deixa o trono VAGO e o Espirito do Cargo nasce a ate 2 tiles "
				  + "de quem reivindica, anunciado", ergueu && Disse(falas, "se materializa diante de"), $"{detalhe} | {Ultimos(falas)}");
		ServerPlayer? e = EspiritoPcg("turtle");
		ProvaEmCurso? prova = ProvaPcg("turtle");
		if (e == null || prova == null) return;

		AfirmarPcg("P. o espirito e um morto DE PASSAGEM: de pe, meio transparente e de aureola (o pacote da aureola diz as duas coisas)",
				  e.Ficha.dead && e.MorteJaViajou && e.EmPassagem && e.MortoDePe && Alem.TemAureola(e.Ficha.dead, e.MorteJaViajou) && !e.Deitado,
				  $"dead {e.Ficha.dead}; viajou {e.MorteJaViajou}; passagem {e.EmPassagem}; de pe {e.MortoDePe}");
		double alvo = ProvaDoCargo.PoderDoEspirito(pl.Ficha.expressedBP);
		AfirmarPcg("P. **O PODER DO ESPIRITO E 1,15x O EXPRESSO** de quem o invocou (na hora)",
				  Math.Abs(e.Ficha.expressedBP - alvo) <= alvo * 0.02, $"{e.Ficha.expressedBP:0} / {alvo:0}");

		// A) a caca, pelo tique de producao da mente. O cerebro novo fica PRESO no plano em que nasce por `Cerebro.TempoMinimoNoPlano`
		// (1,2 s, o compromisso -- a r1 deu so 1 s de tique e o plano ainda era `Nada`): ate 2,5 vezes isso, a 30 Hz.
		int tiques = (int)Math.Ceiling(Jandirus.Core.Ai.Cerebro.TempoMinimoNoPlano * 30 * 2.5);
		for (int i = 0; i < tiques && e.Cerebro!.Atual == Plano.Nada; i++) TicarUmCorpo(e, 1.0 / 30, 1.0 / 30);
		bool guia = GuiarOEspirito(e, prova, out ServerPlayer? presa, out _);
		AfirmarPcg("A. **O ESPIRITO CACA QUEM O INVOCOU**: o tique da mente tira o plano dele do `Nada`, com quem o invocou como presa",
				  e.Cerebro!.Atual != Plano.Nada && guia && presa == pl, $"plano {e.Cerebro.Atual}; presa {presa?.Name ?? "(nenhuma)"}");

		// B) o cravo
		e.Ficha.BP *= 3;
		e.Ficha.Statify();
		e.Ficha.PowerLevel();
		TickDosCargos();
		AfirmarPcg("B. **O PODER FICA CRAVADO**: o espirito que treinou (o BP triplicou) volta ao cravado no tique da prova",
				  Math.Abs(e.Ficha.expressedBP - prova.ExpressoDoEspirito) <= prova.ExpressoDoEspirito * 0.02,
				  $"{e.Ficha.expressedBP:0} / {prova.ExpressoDoEspirito:0}");
		ProvaDoCargo.EspiritoSemCravoDeTeste = true;
		try
		{
			e.Ficha.BP *= 3;
			e.Ficha.Statify();
			e.Ficha.PowerLevel();
			TickDosCargos();
		}
		finally { ProvaDoCargo.EspiritoSemCravoDeTeste = false; }
		AfirmarPcg("(defeito injetado: o espirito sem cravo) a mesma regua REPROVA -- o treino o leva pra cima do cravado",
				  Math.Abs(e.Ficha.expressedBP - prova.ExpressoDoEspirito) > prova.ExpressoDoEspirito * 0.02,
				  $"{e.Ficha.expressedBP:0} / {prova.ExpressoDoEspirito:0}");
		TickDosCargos();

		// V) a vitoria, pelo tique de 1 Hz dos cargos
		int idDoEspirito = e.Id;
		DerrubarPcg(e, pl);
		List<string> fim = Ouvir(TickDosCargos);
		AfirmarPcg("V. **QUEM DERRUBA O ESPIRITO LEVA O CARGO**: nocauteado por quem o invocou, o tique da prova da o trono, tira o espirito "
				  + "do mundo e solta a trava",
				  _tronos.GetValueOrDefault("turtle") == pl.Conta && !_players.ContainsKey(idDoEspirito) && !_provasDoCargo.ContainsKey("turtle")
				  && Disse(fim, "cai."), $"trono {_tronos.GetValueOrDefault("turtle") ?? "(vago)"} | {Ultimos(fim)}");
		_tronos.Remove("turtle");
		ReconciliarDadiva(pl);
		LimparAsProvasPcg();
	}

	// =====================================================================
	// I) O INTRUSO
	// =====================================================================
	private void OIntrusoPcg(ServerPlayer pl)
	{
		GD.Print("[provacargo] -- I) o intruso --");
		ServerPlayer intruso = ForjarPcg(1, "bancada: o intruso", "Human", pl.Zone, pl.Pos + new Vec2(-3 * ZoneCollision.TileSize, 0));

		bool Intromissao(out string detalhe)
		{
			LimparAsProvasPcg();
			PedirAProvaDoCargo(pl, "turtle");
			ServerPlayer? e = EspiritoPcg("turtle");
			if (e != null) DerrubarPcg(e, intruso);
			List<string> falas = Ouvir(TickDosCargos);
			detalhe = $"trono {_tronos.GetValueOrDefault("turtle") ?? "(vago)"}; vergonha {_vergonhaDoCargo.ContainsKey(pl.Assinatura)} | {Ultimos(falas)}";
			bool ok = e != null && !_tronos.ContainsKey("turtle") && !_vergonhaDoCargo.ContainsKey(pl.Assinatura) && Disse(falas, "a prova não vale");
			_tronos.Remove("turtle");
			return ok;
		}

		bool certo = Intromissao(out string d1);
		AfirmarPcg("I. **O INTRUSO NAO DA O CARGO**: o espirito derrubado por OUTRO nao vale -- e nao deixa vergonha", certo, d1);
		ProvaDoCargo.IntrusoValeDeTeste = true;
		bool comDefeito;
		string d2;
		try { comDefeito = Intromissao(out d2); }
		finally { ProvaDoCargo.IntrusoValeDeTeste = false; }
		AfirmarPcg("(defeito injetado: o intruso vale) a mesma regua REPROVA -- o trono vai pra quem nao derrubou nada", !comDefeito, d2);
		ReconciliarDadiva(pl);
		LimparAsProvasPcg();
	}

	// =====================================================================
	// D) A QUEDA E A VERGONHA
	// =====================================================================
	private void AQuedaEAVergonhaPcg(ServerPlayer pl)
	{
		GD.Print("[provacargo] -- D) a queda e a vergonha --");

		bool Queda(out string detalhe)
		{
			LimparAsProvasPcg();
			DePe(pl);
			PedirAProvaDoCargo(pl, "turtle");
			bool armou = ProvaPcg("turtle") != null;
			pl.Combate.Nocautear(10);
			List<string> falas = Ouvir(TickDosCargos);
			long agora = NowMs();
			bool vergonha = _vergonhaDoCargo.TryGetValue(pl.Assinatura, out long ate)
							&& Math.Abs(ate - (agora + ProvaDoCargo.MsDaVergonha)) <= 5_000;
			DePe(pl);
			List<string> denovo = Ouvir(() => PedirAProvaDoCargo(pl, "turtle"));
			detalhe = $"armou {armou}; trono {_tronos.GetValueOrDefault("turtle") ?? "(vago)"}; vergonha {vergonha} | {Ultimos(falas)} || {Ultimos(denovo)}";
			return armou && !_tronos.ContainsKey("turtle") && vergonha && Disse(falas, "você caiu") && Disse(denovo, "zomba")
				   && !_provasDoCargo.ContainsKey("turtle");
		}

		bool certo = Queda(out string d1);
		AfirmarPcg("D. **QUEM CAI PERDE E FICA 30 MIN COM VERGONHA**: nocauteado, sem trono; o Reivindicar seguinte ouve a zombaria", certo, d1);
		ProvaDoCargo.SemVergonhaDeTeste = true;
		bool comDefeito;
		string d2;
		try { comDefeito = Queda(out d2); }
		finally { ProvaDoCargo.SemVergonhaDeTeste = false; }
		AfirmarPcg("(defeito injetado: a derrota sem vergonha) a mesma regua REPROVA -- da pra tentar de novo na hora", !comDefeito, d2);
		LimparAsProvasPcg();
		DePe(pl);
	}

	// =====================================================================
	// F) FUGIR, TROCAR DE MAPA, O TEMPO
	// =====================================================================
	private void AFugaPcg(ServerPlayer pl)
	{
		GD.Print("[provacargo] -- F) fugir, trocar de mapa, o tempo --");
		ZoneKey casa = pl.Zone;
		Vec2 lugar = pl.Pos;

		string Rodar(Action jeito, string esperado, out bool ok)
		{
			LimparAsProvasPcg();
			DePe(pl);
			if (!pl.Zone.Equals(casa)) MoveToZone(pl.Id, casa, lugar);
			pl.Pos = lugar;
			PedirAProvaDoCargo(pl, "turtle");
			bool armou = ProvaPcg("turtle") != null;
			jeito();
			List<string> falas = Ouvir(TickDosCargos);
			ok = armou && !_tronos.ContainsKey("turtle") && _vergonhaDoCargo.ContainsKey(pl.Assinatura) && Disse(falas, esperado);
			return $"{esperado}: armou {armou}, vergonha {_vergonhaDoCargo.ContainsKey(pl.Assinatura)} | {Ultimos(falas)}";
		}

		string f1 = Rodar(() => pl.Pos = (EspiritoPcg("turtle")?.Pos ?? pl.Pos) + new Vec2(30 * ZoneCollision.TileSize, 0), "você fugiu da prova", out bool ok1);
		string f2 = Rodar(() => MoveToZone(pl.Id, ZoneKey.Premade("Namek"), new Vec2(10 * ZoneCollision.TileSize, 10 * ZoneCollision.TileSize)),
						  "você saiu do lugar da prova", out bool ok2);
		string f3 = Rodar(() => { if (ProvaPcg("turtle") is { } p) p.AcabaEm = NowMs() - 1; }, "o tempo da prova acabou", out bool ok3);
		AfirmarPcg("F. fugir (30 tiles), trocar de mapa e deixar o tempo acabar tambem perdem -- com vergonha", ok1 && ok2 && ok3,
				  $"{f1} || {f2} || {f3}");
		LimparAsProvasPcg();
		if (!pl.Zone.Equals(casa)) MoveToZone(pl.Id, casa, lugar);
		pl.Pos = lugar;
	}

	// =====================================================================
	// T) UMA ALMA POR VEZ
	// =====================================================================
	private void ATravaPcg(ServerPlayer pl)
	{
		GD.Print("[provacargo] -- T) uma alma por vez --");
		ServerPlayer outra = ForjarPcg(2, "bancada: a outra alma", "Human", pl.Zone, pl.Pos + new Vec2(0, 4 * ZoneCollision.TileSize));

		bool Trava(out string detalhe)
		{
			LimparAsProvasPcg();
			DePe(pl);
			PedirAProvaDoCargo(pl, "turtle");
			List<string> falas = Ouvir(() => PedirAProvaDoCargo(outra, "turtle"));
			detalhe = $"espiritos {_espiritosDoCargo.Count} | {Ultimos(falas)}";
			return _espiritosDoCargo.Count == 1 && Disse(falas, "outra alma já enfrenta");
		}

		bool certo = Trava(out string d1);
		string aba = ProvaQueOcupa(Cargos.Get("turtle")!, outra) ?? "(nada)";
		AfirmarPcg("T. **UMA ALMA POR VEZ**: com a prova do Eremita em curso, a segunda alma apta ouve que outra alma ja enfrenta o espirito "
				  + "-- e nenhum segundo espirito nasce", certo, d1);
		AfirmarPcg("T. a aba Cargos da segunda alma diz que o espirito esta sendo enfrentado (e o botao some)",
				  aba == "outra alma enfrenta o Espírito deste cargo agora", aba);
		ProvaDoCargo.SemTravaDeTeste = true;
		bool comDefeito;
		string d2;
		try { comDefeito = Trava(out d2); }
		finally { ProvaDoCargo.SemTravaDeTeste = false; }
		AfirmarPcg("(defeito injetado: sem a trava) a mesma regua REPROVA -- duas almas, dois espiritos", !comDefeito, d2);
		LimparAsProvasPcg();
	}

	// =====================================================================
	// M) O MORTO NO OUTRO MUNDO
	// =====================================================================
	private void OMortoDoAlemPcg()
	{
		GD.Print("[provacargo] -- M) o morto prova o cargo do Outro Mundo --");
		LimparAsProvasPcg();
		const float t = ZoneCollision.TileSize;
		ServerPlayer morto = ForjarPcg(3, "bancada: o Kai morto", "Kai", ZoneKey.Premade(Alem.ZonaDoOutroMundo),
									  new Vec2(176 * t + t / 2f, 20 * t + t / 2f), karma: 60);
		morto.Ficha.dead = true;
		morto.MorteJaViajou = true;
		List<string> pediu = Ouvir(() => PedirAProvaDoCargo(morto, "nkai"));
		bool armou = ProvaPcg("nkai") != null;
		TickDosCargos();
		bool continua = ProvaPcg("nkai") != null;
		if (EspiritoPcg("nkai") is { } e) DerrubarPcg(e, morto);
		TickDosCargos();
		AfirmarPcg("M. **O MORTO PROVA O CARGO DO OUTRO MUNDO**: morto no Alem, o Kaio do Norte se prova -- e a morte dele nao conta como derrota",
				  armou && continua && _tronos.GetValueOrDefault("nkai") == morto.Conta,
				  $"armou {armou}; continua {continua}; trono {_tronos.GetValueOrDefault("nkai") ?? "(vago)"} | {Ultimos(pediu)}");
		_tronos.Remove("nkai");
		LimparAsProvasPcg();
	}

	// =====================================================================
	// C) A CAMPANHA DO PRESIDENTE
	// =====================================================================
	private void ACampanhaPcg(ServerPlayer pl)
	{
		GD.Print("[provacargo] -- C) a campanha do Presidente --");
		ServerPlayer candidato = ForjarPcg(4, "bancada: o candidato", "Human", pl.Zone, pl.Pos + new Vec2(4 * ZoneCollision.TileSize, 0), karma: 10);
		if (!_reputacao.TryGetValue("Earth", out Dictionary<string, double>? livro))
			_reputacao["Earth"] = livro = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
		livro[candidato.Conta] = 20;

		bool Eleicao(double gastaNaProva, out string detalhe, out List<string> falas)
		{
			LimparAsProvasPcg();
			_tronos.Remove("president");
			candidato.Ficha.Zeni = 300_000;
			PedirAProvaDoCargo(candidato, "president");
			bool armou = ProvaPcg("president") != null;
			candidato.Ficha.Zeni -= gastaNaProva;
			if (EspiritoPcg("president") is { } e) DerrubarPcg(e, candidato);
			falas = Ouvir(TickDosCargos);
			detalhe = $"armou {armou}; trono {_tronos.GetValueOrDefault("president") ?? "(vago)"}; zeni {candidato.Ficha.Zeni:0} | {Ultimos(falas)}";
			return armou;
		}

		bool armou1 = Eleicao(0, out string d1, out List<string> f1);
		AfirmarPcg("C. **A CAMPANHA DO PRESIDENTE E COBRADA AO VENCER**: 300.000 no bolso, o trono e 50.000 depois dele",
				  armou1 && _tronos.GetValueOrDefault("president") == candidato.Conta && Math.Abs(candidato.Ficha.Zeni - 50_000) < 0.5
				  // o numero sai do `N0` da maquina, como a frase do servidor (`1.234.567` aqui; `1,234,567` noutra cultura)
				  && Disse(f1, $"a campanha custou {Cargos.CampanhaDoPresidente:N0} zeni"), d1);
		ProvaDoCargo.CampanhaDeGracaDeTeste = true;
		bool armou2;
		string d2;
		try { armou2 = Eleicao(0, out d2, out _); }
		finally { ProvaDoCargo.CampanhaDeGracaDeTeste = false; }
		AfirmarPcg("(defeito injetado: a campanha de graca, o jogo de antes) a mesma regua REPROVA -- o trono sai e o bolso fica cheio",
				  armou2 && _tronos.GetValueOrDefault("president") == candidato.Conta && Math.Abs(candidato.Ficha.Zeni - 300_000) < 0.5, d2);

		bool armou3 = Eleicao(200_000, out string d3, out List<string> f3);
		AfirmarPcg("C. quem gasta a campanha DURANTE a prova derruba o espirito e nao leva: o manto confere o bolso de novo",
				  armou3 && !_tronos.ContainsKey("president") && Math.Abs(candidato.Ficha.Zeni - 100_000) < 0.5 && Disse(f3, "250.000 zeni de campanha"),
				  d3);
		_tronos.Remove("president");
		LimparAsProvasPcg();
	}

	// =====================================================================
	// S) SAIR DO JOGO NO MEIO
	// =====================================================================
	private void ASaidaPcg(MesaDeClientesCrus mesa, ClienteCruDeTeste cli)
	{
		GD.Print("[provacargo] -- S) sair do jogo no meio --");
		LimparAsProvasPcg();
		ServerPlayer? pl = mesa.CorpoDe(ContaDaBancadaDaProva);
		if (pl == null) { AfirmarPcg("S. (montagem) o cliente cru ainda esta no jogo", false); return; }
		DePe(pl);
		string assinatura = pl.Assinatura;
		PedirAProvaDoCargo(pl, "turtle");
		bool armou = ProvaPcg("turtle") != null;
		bool saiu = mesa.SaiDoJogo(cli, ContaDaBancadaDaProva);
		TickDosCargos();
		bool vergonha = _vergonhaDoCargo.ContainsKey(assinatura) && !_provasDoCargo.ContainsKey("turtle");
		ServerPlayer? volta = mesa.EntraNoMundo(cli, ContaDaBancadaDaProva, "Provado");
		List<string> denovo = volta == null ? [] : Ouvir(() => PedirAProvaDoCargo(volta, "turtle"));
		AfirmarPcg("S. **SAIR DO JOGO NO MEIO E DERROTA**: a vergonha fica na assinatura, e o relog nao a apaga",
				  armou && saiu && vergonha && volta != null && volta.Assinatura == assinatura && Disse(denovo, "zomba")
				  && !_provasDoCargo.ContainsKey("turtle"),
				  $"armou {armou}; saiu {saiu}; vergonha {vergonha}; voltou {volta != null} | {Ultimos(denovo)}");
		LimparAsProvasPcg();
	}
}
