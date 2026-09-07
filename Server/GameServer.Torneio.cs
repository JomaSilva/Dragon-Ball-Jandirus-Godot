using System.Text.Json;
using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.Torneio;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// OS TORNEIOS -- o de Artes Marciais (Terra, vivos) e o do Outro Mundo (Afterlife, mortos). O porte
/// do `Tournament.dm` (`datum/tournament` e `datum/tournament/heaven`), com o que o dono pediu por
/// cima: 32 vagas, convite na tela com prazo, disputa do 3o lugar, W.O. por desconexao com prazo,
/// chave que sobrevive a reconexao e agenda em tempo real persistida.
///
/// ============================ O QUE E DO DM, LINHA A LINHA ============================
///   * inscricao aberta por `TRN_SIGNUP_TICKS`, convite so pra quem e elegivel (`eligible`: vivo na
///     Terra / morto no Alem), quem sumiu ate o fim das inscricoes e cortado (`still_eligible`);
///   * ninguem inscrito = cancelado; as vagas restantes viram NPC com BP na MEDIA dos jogadores
///     inscritos x rand(60..140)%, piso 500 (`trn_spawn_fighter`);
///   * todo mundo espera TRAVADO fora da arena (`apply_hold`: nao anda, nao bate, nao apanha);
///   * cada luta: cura total (`trn_heal`), os dois frente a frente (`fighter_spot`), contagem,
///     NPC engajado no oponente designado (`tourney_engage`), monitor: sumiu = W.O., nocaute =
///     derrota (duplo decide por vida), RING-OUT = pisar fora sem voar, tempo esgotado = vida;
///   * NPC de torneio VOA com `TRN_AI_FLY_CHANCE` so quando algum jogador inscrito voa (`anyfly`);
///   * premios 1.000.000 / 500.000 / 250.000 so pra quem tem cliente (`award`).
///
/// ============================ O QUE E DO PORT ============================
///   * A CHAVE E DO SERVIDOR e conhece as pessoas pela ASSINATURA (conta/slot): o id do corpo muda
///     a cada login, a assinatura nao. Quem desconecta no meio da luta tem `woSegundos` pra voltar
///     -- o corpo novo e reconhecido e posto de volta no lugar do lutador (`AoEntrarNoTorneio`).
///   * O W.O. so vence o relogio: o DM decidia no tique seguinte a desconexao.
///   * A 3a colocacao e DISPUTADA (o DM premiava os dois semifinalistas).
///   * Os golpes sao NAO-LETAIS pros dois enquanto a luta dura (o `murderToggle = 0` do
///     TourneyFighter, estendido ao jogador): torneio e esporte, ninguem morre nele.
///   * Sem camera de espectador (`Assistir Torneio`): quem quer ver vai ate a beirada da arena.
///   * Um reinicio do servidor no meio do torneio o cancela; a agenda ja foi gravada no comeco,
///     entao ele nao recomeca sozinho no boot.
/// =========================================================================================
/// </summary>
public sealed partial class GameServer
{
	private TorneioConfig _torneioCfg = new();
	private AgendaDeTorneios _agenda = new();
	private TorneioVivo? _torneio;
	private bool _torneioDeTeste;

	/// <summary>Faixa de lugares propria pros NPCs do torneio (longe dos habitantes, das sagas e das bancadas).</summary>
	private ulong _lugarDoTorneio = 7_000_000;

	/// <summary>Gancho de bancada: os anuncios do torneio, na ordem. Nulo fora de teste.</summary>
	internal static List<string>? EscutaDoTorneio;

	/// <summary>A carencia que faz o corpo INTOCAVEL na area de espera (o `attackable = 0`).</summary>
	private const double CarenciaDeEspera = 1e6;

	private const double MsPorDia = 86_400_000;

	private string CaminhoDaAgenda => System.IO.Path.Combine(_store?.Pasta ?? ".", "torneio.json");

	/// <summary>A agenda persistida (`torneio.json` da pasta de saves): quando e o proximo de cada um.</summary>
	public sealed class AgendaDeTorneios
	{
		public long ProximoDaTerraMs;
		public long ProximoDoAlemMs;
		public long UltimoDaTerraMs;
	}

	private enum FaseDoTorneio : byte { Inscricao, Preparando, Contagem, Luta, Intervalo }

	/// <summary>O torneio em andamento. Um so por vez: a Terra e o Alem nunca coincidem na agenda.</summary>
	private sealed class TorneioVivo
	{
		public TipoDeTorneio Tipo;
		public PalcoDeTorneio Palco = new();
		public ZoneKey Zona;
		public Arena Arena;
		public FaseDoTorneio Fase;
		public long ComecouMs, PrazoMs;
		public string Rotulo = "";
		public Random Sorteio = new();

		public readonly Dictionary<string, string> Inscritos = new();   // assinatura -> nome
		public readonly Dictionary<string, long> Convites = new();      // assinatura -> prazo do convite (ms)
		public readonly HashSet<string> Recusaram = new();
		public Chave Chave = new();
		public readonly Dictionary<string, int> CorpoDoNpc = new();      // chave npc:N -> id do corpo
		public readonly List<int> Npcs = new();
		public readonly HashSet<int> Presos = new();
		public readonly Dictionary<int, Vec2> LugarDeEspera = new();
		public readonly Dictionary<int, bool> LetalAntes = new();

		public string ChaveA = "", ChaveB = "";
		public long AusenteDesdeA, AusenteDesdeB;
		public double LutaSegundos, ReengajarEm;
		public int Contagem;
		public bool AlguemVoa;
		public int LutasDisputadas;
	}

	// =====================================================================
	// BOOT E AGENDA
	// =====================================================================

	private void CarregarTorneio()
	{
		const string caminho = "res://Assets/Data/torneio.json";
		if (Godot.FileAccess.FileExists(caminho))
		{
			try { _torneioCfg = TorneioConfig.Ler(Godot.FileAccess.GetFileAsString(caminho)); }
			catch (Exception e) { GD.PushError($"[server] torneio.json ilegivel ({e.Message}) -- padroes do codigo"); _torneioCfg = new TorneioConfig(); }
		}
		else GD.PushWarning("[server] sem torneio.json -- os torneios usam os padroes do codigo");
		foreach (string p in _torneioCfg.Problemas()) GD.PushError($"[server] torneio.json: {p}");

		try
		{
			if (System.IO.File.Exists(CaminhoDaAgenda))
				_agenda = JsonSerializer.Deserialize<AgendaDeTorneios>(System.IO.File.ReadAllText(CaminhoDaAgenda),
					new JsonSerializerOptions { IncludeFields = true }) ?? new AgendaDeTorneios();
		}
		catch (Exception e) { GD.PushWarning($"[server] torneio.json (agenda) ilegivel: {e.Message}"); }

		// O PRIMEIRO TORNEIO DA TERRA e marcado no primeiro boot; dali em diante cada torneio marca o
		// seguinte ao COMECAR (assim um reinicio no meio nao o repete).
		if (_agenda.ProximoDaTerraMs == 0)
		{
			_agenda.ProximoDaTerraMs = NowMs() + (long)(_torneioCfg.PrimeiroDaTerraDias * MsPorDia);
			SalvarAgenda();
		}
		GD.Print($"[server] torneios: Terra em {QuandoEm(_agenda.ProximoDaTerraMs)}, Outro Mundo "
			   + (_agenda.ProximoDoAlemMs > 0 ? $"em {QuandoEm(_agenda.ProximoDoAlemMs)}" : "depois do proximo da Terra")
			   + $" | {_torneioCfg.Vagas} vagas, inscricao {_torneioCfg.InscricaoSegundos:0} s, luta ate {_torneioCfg.LutaSegundosMax:0} s, "
			   + $"arenas {_torneioCfg.Terra.Arena} ({_torneioCfg.Terra.Zona}) e {_torneioCfg.Alem.Arena} ({_torneioCfg.Alem.Zona})");
	}

	private string QuandoEm(long ms)
	{
		double dias = (ms - NowMs()) / MsPorDia;
		return dias >= 1 ? $"{dias:0.0} dia(s)" : dias >= 0 ? $"{dias * 24:0.0} h" : "atrasado (comeca no proximo tique)";
	}

	private void SalvarAgenda()
	{
		if (_store == null) return;
		try
		{
			string tmp = CaminhoDaAgenda + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(_agenda,
				new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDaAgenda, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra salvar torneio.json: {e.Message}"); }
	}

	// --- ganchos de bancada ---------------------------------------------------
	internal TorneioConfig TorneioDeTeste { get => _torneioCfg; set => _torneioCfg = value; }
	internal AgendaDeTorneios AgendaDeTeste => _agenda;
	internal bool TorneioAtivo => _torneio != null;
	internal string FaseDoTorneioDeTeste => _torneio?.Fase.ToString() ?? "nenhum";
	internal string TipoDoTorneioDeTeste => _torneio?.Tipo.ToString() ?? "nenhum";
	internal Chave? ChaveDeTeste => _torneio?.Chave;
	internal int InscritosDeTeste => _torneio?.Inscritos.Count ?? 0;
	internal (int A, int B) LutadoresDeTeste => _torneio == null ? (0, 0) : (CorpoDaChave(_torneio.ChaveA)?.Id ?? 0, CorpoDaChave(_torneio.ChaveB)?.Id ?? 0);
	internal IReadOnlyCollection<int> PresosDeTeste => _torneio?.Presos ?? (IReadOnlyCollection<int>)Array.Empty<int>();
	internal IReadOnlyList<int> NpcsDoTorneioDeTeste => _torneio?.Npcs ?? (IReadOnlyList<int>)Array.Empty<int>();
	internal void RecarregarAgendaDeTeste()
	{
		_agenda = new AgendaDeTorneios();
		if (System.IO.File.Exists(CaminhoDaAgenda))
			_agenda = JsonSerializer.Deserialize<AgendaDeTorneios>(System.IO.File.ReadAllText(CaminhoDaAgenda),
				new JsonSerializerOptions { IncludeFields = true }) ?? new AgendaDeTorneios();
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================

	private void TickDoTorneio(double dt)
	{
		if (_torneio == null)
		{
			long agora = NowMs();
			if (_agenda.ProximoDaTerraMs > 0 && agora >= _agenda.ProximoDaTerraMs) ComecarTorneio(TipoDeTorneio.Terra, "a agenda");
			else if (_agenda.ProximoDoAlemMs > 0 && agora >= _agenda.ProximoDoAlemMs) ComecarTorneio(TipoDeTorneio.Alem, "a agenda");
			return;
		}
		TorneioVivo t = _torneio;
		if (t.Fase != FaseDoTorneio.Inscricao) SegurarPresos(t);
		switch (t.Fase)
		{
			case FaseDoTorneio.Inscricao:
				if (NowMs() >= t.PrazoMs) FecharInscricoes(t);
				break;
			case FaseDoTorneio.Preparando:
			case FaseDoTorneio.Intervalo:
				if (NowMs() >= t.PrazoMs) ProximaLuta(t);
				break;
			case FaseDoTorneio.Contagem:
				if (ConferirAusencias(t)) break;
				if (NowMs() >= t.PrazoMs)
				{
					t.Contagem--;
					if (t.Contagem > 0) { DizerAosPresentes(t, $"{t.Contagem}..."); t.PrazoMs = NowMs() + 1000; }
					else ComecarALuta(t);
				}
				break;
			case FaseDoTorneio.Luta:
				t.LutaSegundos += dt;
				Juiz(t, dt);
				break;
		}
	}

	// =====================================================================
	// COMECO E INSCRICOES
	// =====================================================================

	private void ComecarTorneio(TipoDeTorneio tipo, string porQuem)
	{
		if (_torneio != null) return;
		PalcoDeTorneio palco = _torneioCfg.Palco(tipo);
		var zona = ZoneKey.Premade(palco.Zona);
		if (MapaDaZonaOuCatalogo(zona) == null)
		{
			GD.PushError($"[server] torneio: a zona '{palco.Zona}' nao tem mapa -- cancelado");
			Reagendar(tipo, NowMs());
			return;
		}
		long agora = NowMs();
		var t = new TorneioVivo
		{
			Tipo = tipo, Palco = palco, Zona = zona, Arena = palco.Arena,
			Fase = FaseDoTorneio.Inscricao, ComecouMs = agora,
			PrazoMs = agora + (long)(_torneioCfg.InscricaoSegundos * 1000),
			Rotulo = tipo == TipoDeTorneio.Alem ? "TORNEIO DO OUTRO MUNDO" : "TORNEIO",
			Sorteio = new Random(unchecked((int)(agora ^ (agora >> 32)))),
		};
		_torneio = t;

		// A AGENDA E MARCADA NO COMECO (ver o cabecalho): um reinicio no meio nao repete o torneio.
		Reagendar(tipo, agora);

		AnunciarTorneio(t, tipo == TipoDeTorneio.Alem
			? $"O Torneio de Artes Marciais DO OUTRO MUNDO abriu as inscricoes! Almas do alem: respondam ao convite. (fecha em {_torneioCfg.InscricaoSegundos / 60:0.#} min)"
			: $"O Torneio de Artes Marciais comecou as inscricoes! Jogadores na Terra: respondam ao convite. (fecha em {_torneioCfg.InscricaoSegundos / 60:0.#} min)");
		GD.Print($"[server] torneio: {t.Rotulo} aberto por {porQuem}");
		foreach (ServerPlayer p in Jogadores.ToList())
			if (Elegivel(p, tipo)) Convidar(t, p);
	}

	/// <summary>Marca o proximo da agenda a partir do comeco deste (e grava).</summary>
	private void Reagendar(TipoDeTorneio tipo, long comecouMs)
	{
		if (tipo == TipoDeTorneio.Terra)
		{
			_agenda.UltimoDaTerraMs = comecouMs;
			_agenda.ProximoDaTerraMs = comecouMs + (long)(_torneioCfg.IntervaloDaTerraDias * MsPorDia);
			_agenda.ProximoDoAlemMs = comecouMs + (long)(_torneioCfg.AlemDepoisDaTerraDias * MsPorDia);
		}
		else _agenda.ProximoDoAlemMs = 0;   // o proximo do Alem e o proximo da Terra quem marca
		SalvarAgenda();
	}

	/// <summary>`eligible`: Terra = vivo e na Terra; Alem = morto e no Outro Mundo (qualquer zona do alem).</summary>
	private bool Elegivel(ServerPlayer p, TipoDeTorneio tipo) =>
		EhJogador(p) && (tipo == TipoDeTorneio.Alem
			? p.Ficha.dead && Alem.EhOAlem(p.Zone)
			: !p.Ficha.dead && string.Equals(p.Zone.Name, _torneioCfg.Terra.Zona, StringComparison.OrdinalIgnoreCase));

	/// <summary>`still_eligible` / `player_ok`: na Terra continua vivo; no Alem continua morto (reviveu = fora).</summary>
	private static bool AindaElegivel(ServerPlayer p, TipoDeTorneio tipo) =>
		tipo == TipoDeTorneio.Alem ? p.Ficha.dead : !p.Ficha.dead;

	private void Convidar(TorneioVivo t, ServerPlayer p)
	{
		string chave = p.Assinatura;
		if (chave.Length == 0 || t.Convites.ContainsKey(chave) || t.Inscritos.ContainsKey(chave)) return;
		long prazo = Math.Min(t.PrazoMs, NowMs() + (long)(_torneioCfg.ConviteSegundos * 1000));
		t.Convites[chave] = prazo;
		int segundos = (int)Math.Max(1, (prazo - NowMs()) / 1000);
		MandarAvisoDoTorneio(p, 1, t.Tipo, segundos, t.Tipo == TipoDeTorneio.Alem
			? "O TORNEIO DO OUTRO MUNDO vai comecar! So os mortos podem lutar."
			: "O TORNEIO DE ARTES MARCIAIS vai comecar! Lutas 1v1 ate a final.");
		Avisar(p, $"{t.Rotulo}: convite recebido -- premios {_torneioCfg.Premio1:N0} / {_torneioCfg.Premio2:N0} / {_torneioCfg.Premio3:N0} zeni. "
				+ "Responda no painel (Participar / Recusar) ou pelos verbos da aba Other.");
	}

	private void MandarAvisoDoTorneio(ServerPlayer p, byte aviso, TipoDeTorneio tipo, int segundos, string titulo)
	{
		var w = Protocol.Begin(Protocol.S2C.Torneio);
		w.Put(aviso);
		w.Put((byte)tipo);
		w.Put(segundos);
		w.Put(titulo);
		p.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Os verbos: `trn_participar`, `trn_recusar`, `trn_status`; e os de admin `trn_iniciar terra|alem`, `trn_cancelar`.</summary>
	private bool ComandoDeTorneio(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "trn_participar": Inscrever(pl); return true;
			case "trn_recusar": Recusar(pl); return true;
			case "trn_status": Avisar(pl, SituacaoDoTorneio()); return true;
			case "trn_iniciar":
				if (!EhAdmin(pl)) { Avisar(pl, "isso e coisa de administrador."); return true; }
				if (_torneio != null) { Avisar(pl, $"ja ha um torneio em andamento ({_torneio.Rotulo}, {_torneio.Fase})."); return true; }
				ComecarTorneio(string.Equals(arg, "alem", StringComparison.OrdinalIgnoreCase) ? TipoDeTorneio.Alem : TipoDeTorneio.Terra, $"o admin {pl.Name}");
				Avisar(pl, "torneio iniciado na marra.");
				return true;
			case "trn_cancelar":
				if (!EhAdmin(pl)) { Avisar(pl, "isso e coisa de administrador."); return true; }
				if (_torneio == null) { Avisar(pl, "nao ha torneio rolando."); return true; }
				AnunciarTorneio(_torneio, _torneio.Tipo == TipoDeTorneio.Alem ? "O torneio foi CANCELADO pelo proprio Enma!" : "O torneio foi CANCELADO pela organizacao!");
				EncerrarTorneio(_torneio);
				return true;

			// OS TRES DE TESTAR EM JOGO (dono, 2026-09-07: "verbs pra adm que permita eu forcar torneios
			// etc pra eu poder testar ingame"): pular a espera da fase, marcar a agenda pra daqui a
			// pouco, e inscrever alguem sem o clique dele. Todos passam pelos MESMOS caminhos que o
			// relogio e o jogador usam -- nao ha um "modo teste" do torneio.
			case "trn_avancar":
				if (!EhAdmin(pl)) { Avisar(pl, "isso e coisa de administrador."); return true; }
				if (_torneio is not { } vivo) { Avisar(pl, "nao ha torneio rolando."); return true; }
				AvancarTorneio(vivo, pl);
				return true;
			case "trn_agenda":
				if (!EhAdmin(pl)) { Avisar(pl, "isso e coisa de administrador."); return true; }
				AgendarTorneio(pl, arg);
				return true;
			case "trn_inscrever":
				if (!EhAdmin(pl)) { Avisar(pl, "isso e coisa de administrador."); return true; }
				if (PorNome(arg) is not { } outro) { Avisar(pl, "marque alguem antes (ou de o nome dele)."); return true; }
				Inscrever(outro);
				if (outro != pl) Avisar(pl, $"{outro.Name}: inscricao pedida em nome dele (a resposta foi pra ele).");
				return true;
			default: return false;
		}
	}

	/// <summary>
	/// PULAR A ESPERA DA FASE ATUAL (admin, `trn_avancar`) -- pra testar em jogo sem esperar os 150 s
	/// de inscricao, os 5 da contagem, os 10 do intervalo ou os 240 de uma luta. Cada fase pula pelo
	/// MESMO caminho que o relogio usaria: o prazo vai pro passado e o `TickDoTorneio` faz o resto.
	/// A luta e a excecao -- ela nao tem prazo, tem juiz --, entao acaba como acabaria com o tempo
	/// esgotado: por pontos, vence quem tem mais vida.
	/// </summary>
	private void AvancarTorneio(TorneioVivo t, ServerPlayer adm)
	{
		switch (t.Fase)
		{
			case FaseDoTorneio.Inscricao:
				AnunciarTorneio(t, "As inscricoes foram ENCERRADAS pela organizacao!");
				FecharInscricoes(t);
				break;
			case FaseDoTorneio.Preparando:
			case FaseDoTorneio.Intervalo:
				t.PrazoMs = NowMs();
				break;
			case FaseDoTorneio.Contagem:
				t.Contagem = 1;
				t.PrazoMs = NowMs();
				break;
			case FaseDoTorneio.Luta:
				ServerPlayer? a = CorpoDaChave(t.ChaveA), b = CorpoDaChave(t.ChaveB);
				double vidaA = a?.Ficha.HP ?? 0, vidaB = b?.Ficha.HP ?? 0;
				DizerAosPresentes(t, "A organizacao ENCERRA a luta! Os juizes decidem por pontos...");
				TerminarLuta(t, vidaA >= vidaB ? t.ChaveA : t.ChaveB, "encerrada pelo admin: decide a vida");
				break;
		}
		Avisar(adm, $"torneio adiantado: {t.Rotulo}, fase {t.Fase}.");
	}

	/// <summary>
	/// A AGENDA NA MARRA (admin, `trn_agenda`): sem argumento mostra; `terra N` ou `alem N` marcam o
	/// proximo pra daqui a N minutos -- e o CAMINHO AUTOMATICO quem abre (o `TickDoTorneio`), com o
	/// convite e tudo, que e o que se quer testar em jogo. Gravado no `torneio.json` como qualquer
	/// agenda: sobrevive ao reinicio.
	/// </summary>
	private void AgendarTorneio(ServerPlayer adm, string arg)
	{
		string[] partes = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (partes.Length >= 2 && double.TryParse(partes[1], System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out double minutos) && minutos >= 0)
		{
			bool alem = string.Equals(partes[0], "alem", StringComparison.OrdinalIgnoreCase);
			long quando = NowMs() + (long)(minutos * 60_000);
			if (alem) _agenda.ProximoDoAlemMs = quando; else _agenda.ProximoDaTerraMs = quando;
			SalvarAgenda();
			Avisar(adm, $"agenda: {(alem ? "Outro Mundo" : "Terra")} marcado pra daqui a {minutos:0.#} min.");
		}
		Avisar(adm, $"agenda: Terra {Daqui(_agenda.ProximoDaTerraMs)}; Outro Mundo {Daqui(_agenda.ProximoDoAlemMs)}"
				 + (_torneio != null ? $"; agora: {_torneio.Rotulo} em {_torneio.Fase}." : "; nenhum torneio rolando."));
	}

	private string Daqui(long ms)
	{
		if (ms <= 0) return "sem data";
		double s = (ms - NowMs()) / 1000.0;
		if (s <= 0) return "agora";
		if (s < 120) return $"em {s:0} s";
		if (s < 7200) return $"em {s / 60:0} min";
		if (s < 172_800) return $"em {s / 3600:0.#} h";
		return $"em {s / 86_400:0.#} dias";
	}

	private void Inscrever(ServerPlayer pl)
	{
		if (_torneio is not { } t || t.Fase != FaseDoTorneio.Inscricao)
		{
			Avisar(pl, _torneio == null ? "nao ha torneio com inscricoes abertas." : "as inscricoes do torneio ja fecharam.");
			return;
		}
		string chave = pl.Assinatura;
		if (chave.Length == 0 || !EhJogador(pl)) return;
		if (t.Inscritos.ContainsKey(chave)) { Avisar(pl, "voce ja esta inscrito."); return; }
		if (!Elegivel(pl, t.Tipo))
		{
			Avisar(pl, t.Tipo == TipoDeTorneio.Alem ? "so os mortos do Outro Mundo podem se inscrever." : "so quem esta vivo e na Terra pode se inscrever.");
			return;
		}
		if (t.Inscritos.Count >= _torneioCfg.Vagas) { Avisar(pl, $"as {_torneioCfg.Vagas} vagas do torneio ja foram preenchidas!"); return; }
		t.Inscritos[chave] = pl.Name;
		t.Convites.Remove(chave);
		MandarAvisoDoTorneio(pl, 2, t.Tipo, 0, "");
		Avisar(pl, "Inscricao confirmada! Voce sera levado a arena quando o torneio comecar.");
		AnunciarTorneio(t, $"{pl.Name} se inscreveu! ({t.Inscritos.Count}/{_torneioCfg.Vagas})");
	}

	private void Recusar(ServerPlayer pl)
	{
		if (_torneio is not { } t) { Avisar(pl, "nao ha torneio com inscricoes abertas."); return; }
		string chave = pl.Assinatura;
		if (t.Inscritos.Remove(chave)) AnunciarTorneio(t, $"{pl.Name} desistiu da inscricao. ({t.Inscritos.Count}/{_torneioCfg.Vagas})");
		t.Convites.Remove(chave);
		t.Recusaram.Add(chave);
		MandarAvisoDoTorneio(pl, 2, t.Tipo, 0, "");
		Avisar(pl, "voce fica de fora do torneio desta vez.");
	}

	private string SituacaoDoTorneio()
	{
		if (_torneio is not { } t)
		{
			return $"nenhum torneio agora. Terra em {QuandoEm(_agenda.ProximoDaTerraMs)}"
				 + (_agenda.ProximoDoAlemMs > 0 ? $"; Outro Mundo em {QuandoEm(_agenda.ProximoDoAlemMs)}." : ".");
		}
		return t.Fase switch
		{
			FaseDoTorneio.Inscricao => $"{t.Rotulo}: inscricoes abertas ({t.Inscritos.Count}/{_torneioCfg.Vagas}), fecham em {(t.PrazoMs - NowMs()) / 1000} s.",
			FaseDoTorneio.Luta => $"{t.Rotulo}: {t.Chave.RodadaDeAgora?.Nome} -- {t.Chave.NomeDe(t.ChaveA)} x {t.Chave.NomeDe(t.ChaveB)} ({t.LutaSegundos:0} s). Chave: {t.Chave.Resumo()}",
			_ => $"{t.Rotulo}: {t.Fase}. Chave: {t.Chave.Resumo()}",
		};
	}

	/// <summary>
	/// FECHA AS INSCRICOES: corta quem sumiu, preenche com NPC, sorteia a chave e leva todo mundo
	/// pra area de espera, travado. O `trn_run` do DM, passos 1 a 3.
	/// </summary>
	private void FecharInscricoes(TorneioVivo t)
	{
		var validos = new List<ServerPlayer>();
		foreach ((string chave, _) in t.Inscritos.ToList())
		{
			ServerPlayer? p = CorpoDaAssinatura(chave);
			if (p == null || !AindaElegivel(p, t.Tipo)) { t.Inscritos.Remove(chave); continue; }
			validos.Add(p);
		}
		foreach (ServerPlayer p in Jogadores.ToList())
			if (t.Convites.ContainsKey(p.Assinatura)) MandarAvisoDoTorneio(p, 2, t.Tipo, 0, "");
		t.Convites.Clear();

		if (validos.Count == 0)
		{
			AnunciarTorneio(t, "Nenhum lutador se inscreveu... o torneio deste mes foi CANCELADO.");
			EncerrarTorneio(t);
			return;
		}

		// A MEDIA DE BP DOS INSCRITOS (expressedBP, como o DM) manda no poder dos NPCs.
		double media = Math.Max(validos.Average(p => Finito(p.Ficha.expressedBP)), _torneioCfg.NpcBpPiso);
		t.AlguemVoa = validos.Any(PodeVoar);

		var competidores = new List<Competidor>();
		foreach (ServerPlayer p in validos) competidores.Add(new Competidor { Chave = p.Assinatura, Nome = p.Name, Npc = false });
		while (competidores.Count < _torneioCfg.Vagas)
		{
			ServerPlayer? npc = NascerLutador(t, media);
			if (npc == null) break;
			string chave = $"npc:{npc.Id}";
			t.CorpoDoNpc[chave] = npc.Id;
			t.Npcs.Add(npc.Id);
			competidores.Add(new Competidor { Chave = chave, Nome = npc.Name, Npc = true });
		}
		t.Chave = Chave.Montar(competidores, t.Sorteio);

		AnunciarTorneio(t, $"As inscricoes fecharam! {validos.Count} jogador(es) e {t.Npcs.Count} convidado(s) disputam o titulo!");
		Rodada? primeira = t.Chave.RodadaDeAgora;
		if (primeira != null)
			DizerAosPresentes(t, $"Chaveamento ({primeira.Nome}): " + string.Join(", ", t.Chave.Competidores.Select(c => c.Nome)));

		foreach (Competidor c in t.Chave.Competidores)
			if (CorpoDaChave(c.Chave) is { } corpo) Segurar(t, corpo);

		t.Fase = FaseDoTorneio.Preparando;
		t.PrazoMs = NowMs() + (long)(Math.Max(1, _torneioCfg.IntervaloSegundos) * 1000);
	}

	/// <summary>
	/// O NPC LUTADOR (`trn_spawn_fighter`): nasce pelo funil do molde, ganha o BP da media x sorteio,
	/// o perfil de voo (so se algum inscrito voa) e, no Outro Mundo, nasce MORTO de pe com aureola.
	/// </summary>
	private ServerPlayer? NascerLutador(TorneioVivo t, double mediaDeBp)
	{
		Vec2 onde = PontoDeEspera(t);
		ServerPlayer? npc = NascerNpc(_torneioCfg.MoldeDoNpc, t.Zona, onde, ++_lugarDoTorneio);
		if (npc == null) return null;

		double pct = t.Sorteio.Next(_torneioCfg.NpcBpMinPct, _torneioCfg.NpcBpMaxPct + 1) / 100.0;
		npc.Ficha.BP = Math.Max(Math.Round(mediaDeBp * pct), _torneioCfg.NpcBpPiso);
		npc.Ficha.Statify();
		npc.Ficha.PowerLevel();
		npc.Ficha.Ki = npc.Ficha.MaxKi;
		npc.Ficha.stamina = npc.Ficha.maxstamina;
		npc.Ficha.HP = 100;
		npc.SigAtributos = "";

		bool voa = t.AlguemVoa && t.Sorteio.NextDouble() < _rotina.ChanceDeVoarNoTorneio;
		if (voa)
		{
			npc.Livro.Dar(SkillDoKi);
			npc.Niveis.Por(SkillDoKi, MaestriaQueDestravaVoo);
		}
		npc.Perfil = new PerfilDeCombate(Voa: voa, UsaKi: true);
		npc.Arena = t.Arena;

		if (t.Tipo == TipoDeTorneio.Alem)
		{
			// NASCE MORTO, de pe e com aureola: e o que todo participante do torneio celeste e.
			npc.Ficha.dead = true;
			npc.MorteJaViajou = true;
			npc.RelogioDaMorte = long.MaxValue;
		}
		GD.Print($"[server] torneio: lutador '{npc.Name}' ({npc.Race}) BP {npc.Ficha.BP:N0} ({pct * 100:0}% da media {mediaDeBp:N0}), perfil {npc.Perfil}");
		return npc;
	}

	// =====================================================================
	// A AREA DE ESPERA (`apply_hold` / `release_hold` / `rest_turf`)
	// =====================================================================

	/// <summary>`rest_turf`: uma celula livre na BEIRADA (2 a 5 celulas fora do retangulo), 40 tentativas.</summary>
	private Vec2 PontoDeEspera(TorneioVivo t)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(t.Zona);
		Arena a = t.Arena;
		for (int tentativa = 0; tentativa < 40; tentativa++)
		{
			int cx, cy;
			switch (t.Sorteio.Next(4))
			{
				case 0: cx = t.Sorteio.Next(a.X1, a.X2 + 1); cy = a.Y1 - t.Sorteio.Next(2, 6); break;   // norte (y menor)
				case 1: cx = t.Sorteio.Next(a.X1, a.X2 + 1); cy = a.Y2 + t.Sorteio.Next(2, 6); break;   // sul
				case 2: cx = a.X2 + t.Sorteio.Next(2, 6); cy = t.Sorteio.Next(a.Y1, a.Y2 + 1); break;   // leste
				default: cx = a.X1 - t.Sorteio.Next(2, 6); cy = t.Sorteio.Next(a.Y1, a.Y2 + 1); break;  // oeste
			}
			if (mapa == null || mapa.ServeDeChao(cx, cy)) return new Vec2(cx + 0.5f, cy + 0.5f) * ZoneCollision.TileSize;
		}
		Vec2 acima = new((a.X1 + a.X2 + 1) * ZoneCollision.TileSize / 2f, (a.Y1 - 3) * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f);
		return mapa?.PontoLivrePerto(acima) ?? acima;
	}

	/// <summary>`fighter_spot`: frente a frente na linha do centro, de 3 a 10 celulas do meio, sempre DENTRO.</summary>
	private Vec2 PontoDoLutador(TorneioVivo t, int lado)
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
		return new Vec2(acx + 0.5f, acy + 0.5f) * ZoneCollision.TileSize;
	}

	private void Segurar(TorneioVivo t, ServerPlayer corpo)
	{
		if (!t.LugarDeEspera.TryGetValue(corpo.Id, out Vec2 lugar))
		{
			lugar = PontoDeEspera(t);
			t.LugarDeEspera[corpo.Id] = lugar;
		}
		bool novo = t.Presos.Add(corpo.Id);
		Transportar(corpo, t.Zona, lugar);
		corpo.Combate.Carencia = CarenciaDeEspera;
		if (EhJogador(corpo))
		{
			corpo.Moving = false;
			if (novo) Avisar(corpo, "voce esta na AREA DE ESPERA do torneio: imovel e intocavel ate a sua vez. Assista daqui.");
		}
		else corpo.Moving = false;
	}

	private void Soltar(TorneioVivo t, ServerPlayer corpo)
	{
		if (!t.Presos.Remove(corpo.Id)) return;
		if (corpo.Combate.Carencia >= CarenciaDeEspera) corpo.Combate.Carencia = 0;
	}

	/// <summary>
	/// A RE-TRAVA de cada tique (o `hold_watch`): o jogador travado e ancorado no lugar de espera
	/// (o cliente que anda recebe correcao) e continua intocavel; o NPC travado nem e ticado.
	/// </summary>
	private void SegurarPresos(TorneioVivo t)
	{
		foreach (int id in t.Presos.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? c)) { t.Presos.Remove(id); continue; }
			if (c.Combate.Carencia < CarenciaDeEspera) c.Combate.Carencia = CarenciaDeEspera;
			if (!EhJogador(c)) continue;
			if (t.LugarDeEspera.TryGetValue(id, out Vec2 lugar) && c.Zone.Hash == t.Zona.Hash) Ancorar(c, lugar);
		}
	}

	private void Transportar(ServerPlayer c, ZoneKey zona, Vec2 pos)
	{
		if (c.Zone.Hash != zona.Hash) { MoveToZone(c.Id, zona, pos); return; }
		if (EhJogador(c)) CravarPosicao(c, pos);
		else c.Pos = pos;
		c.Moving = false;
	}

	internal bool PresoNoTorneio(int id) => _torneio?.Presos.Contains(id) == true;

	// =====================================================================
	// AS LUTAS (`run_match`)
	// =====================================================================

	// `CorpoDaAssinatura(sig)` mora em `GameServer.Mestre.cs`: o mesmo crivo (pessoa + assinatura) que o discipulado usa.

	private ServerPlayer? CorpoDaChave(string chave)
	{
		if (_torneio is not { } t || chave.Length == 0) return null;
		if (t.CorpoDoNpc.TryGetValue(chave, out int id)) return _players.TryGetValue(id, out ServerPlayer? npc) ? npc : null;
		return CorpoDaAssinatura(chave);
	}

	/// <summary>`fighter_ok`: existe, esta no palco e continua na condicao do torneio (vivo / morto de pe).</summary>
	private bool LutadorOk(TorneioVivo t, ServerPlayer? c)
	{
		if (c == null || c.Zone.Hash != t.Zona.Hash) return false;
		if (t.Tipo == TipoDeTorneio.Alem) return c.Ficha.dead && c.MortoDePe;
		return !c.Ficha.dead;
	}

	private void ProximaLuta(TorneioVivo t)
	{
		Luta? l = t.Chave.LutaAtual;
		if (l == null) { Premiar(t); return; }
		if (l.B.Length == 0)
		{
			// Passa sem lutar (chave impar). O resultado ja esta escrito; so anunciar e seguir.
			DizerAosPresentes(t, $"{t.Chave.NomeDe(l.A)} avanca sem lutar (chave incompleta).");
			t.Chave.Registrar(l.A, l.Como);
			ProximaLuta(t);
			return;
		}
		t.ChaveA = l.A; t.ChaveB = l.B;
		t.AusenteDesdeA = 0; t.AusenteDesdeB = 0;
		t.LutaSegundos = 0;
		ServerPlayer? a = CorpoDaChave(l.A), b = CorpoDaChave(l.B);
		foreach ((ServerPlayer? c, int lado) in new[] { (a, 1), (b, 2) })
		{
			if (c == null) continue;
			Soltar(t, c);
			CurarPraLuta(c);
			Transportar(c, t.Zona, PontoDoLutador(t, lado));
			c.Facing = lado == 1 ? Facing.East : Facing.West;
			if (EhJogador(c)) { t.LetalAntes[c.Id] = c.Combate.Letal; c.Combate.Letal = false; }
			if (c.Arena == null && !EhJogador(c)) c.Arena = t.Arena;
		}
		AnunciarTorneio(t, $"{t.Chave.RodadaDeAgora?.Nome} - luta {t.LutasDisputadas + 1}: {t.Chave.NomeDe(l.A)} VS {t.Chave.NomeDe(l.B)}!");
		t.Contagem = (int)Math.Ceiling(_torneioCfg.ContagemSegundos);
		t.Fase = FaseDoTorneio.Contagem;
		if (t.Contagem <= 0) { ComecarALuta(t); return; }
		DizerAosPresentes(t, $"{t.Contagem}...");
		t.PrazoMs = NowMs() + 1000;
	}

	private void ComecarALuta(TorneioVivo t)
	{
		t.Fase = FaseDoTorneio.Luta;
		t.LutaSegundos = 0;
		t.ReengajarEm = 0;
		DizerAosPresentes(t, "COMECEM!");
		Engajar(t);
	}

	/// <summary>`tourney_engage`: o NPC recebe o oponente como presa IMPOSTA (a mesma do roteiro de chefe).</summary>
	private void Engajar(TorneioVivo t)
	{
		ServerPlayer? a = CorpoDaChave(t.ChaveA), b = CorpoDaChave(t.ChaveB);
		if (a?.Papel != null && b != null) a.Papel.PresaDoRoteiro = b.Id;
		if (b?.Papel != null && a != null) b.Papel.PresaDoRoteiro = a.Id;
	}

	private static void Desengajar(ServerPlayer? c)
	{
		if (c?.Papel != null) c.Papel.PresaDoRoteiro = 0;
	}

	/// <summary>
	/// O W.O.: quem sumiu (deslogou, saiu do palco, morreu na Terra, reviveu no Alem) tem `woSegundos`
	/// pra voltar; os dois sumidos por esse prazo = o primeiro avanca (`run_match`). Devolve true se a
	/// luta acabou por ausencia.
	/// </summary>
	private bool ConferirAusencias(TorneioVivo t)
	{
		long agora = NowMs();
		long limite = (long)(_torneioCfg.WoSegundos * 1000);
		bool aOk = LutadorOk(t, CorpoDaChave(t.ChaveA)), bOk = LutadorOk(t, CorpoDaChave(t.ChaveB));
		t.AusenteDesdeA = aOk ? 0 : t.AusenteDesdeA == 0 ? agora : t.AusenteDesdeA;
		t.AusenteDesdeB = bOk ? 0 : t.AusenteDesdeB == 0 ? agora : t.AusenteDesdeB;
		bool aFora = !aOk && agora - t.AusenteDesdeA >= limite;
		bool bFora = !bOk && agora - t.AusenteDesdeB >= limite;
		if (aFora && bFora) { TerminarLuta(t, t.ChaveA, "dupla ausencia: o primeiro da chave avanca"); return true; }
		if (aFora) { TerminarLuta(t, t.ChaveB, "W.O."); return true; }
		if (bFora) { TerminarLuta(t, t.ChaveA, "W.O."); return true; }
		return false;
	}

	/// <summary>O MONITOR DA LUTA: ausencia, nocaute, ring-out, tempo. A cada tique, na ordem do DM.</summary>
	private void Juiz(TorneioVivo t, double dt)
	{
		if (ConferirAusencias(t)) return;
		ServerPlayer? a = CorpoDaChave(t.ChaveA), b = CorpoDaChave(t.ChaveB);
		if (a == null || b == null) return;   // ausente dentro do prazo: a luta espera

		if (a.Ficha.KO && b.Ficha.KO) { TerminarLuta(t, a.Ficha.HP >= b.Ficha.HP ? t.ChaveA : t.ChaveB, "nocaute duplo: decide a vida"); return; }
		if (a.Ficha.KO) { TerminarLuta(t, t.ChaveB, "nocaute"); return; }
		if (b.Ficha.KO) { TerminarLuta(t, t.ChaveA, "nocaute"); return; }

		// RING-OUT: fora do retangulo SEM voar = derrota na hora (`!A.flight && !in_arena(A)`).
		if (!a.Voando && !t.Arena.ContemEm(a.Pos)) { DizerAosPresentes(t, $"{a.Name} pisou fora da arena! RING-OUT!"); TerminarLuta(t, t.ChaveB, "ring-out"); return; }
		if (!b.Voando && !t.Arena.ContemEm(b.Pos)) { DizerAosPresentes(t, $"{b.Name} pisou fora da arena! RING-OUT!"); TerminarLuta(t, t.ChaveA, "ring-out"); return; }

		if (t.LutaSegundos >= _torneioCfg.LutaSegundosMax)
		{
			DizerAosPresentes(t, "TEMPO ESGOTADO! Os juizes decidem por pontos...");
			TerminarLuta(t, a.Ficha.HP >= b.Ficha.HP ? t.ChaveA : t.ChaveB, "tempo esgotado: decide a vida");
			return;
		}

		// RE-ENGAJA o NPC que perdeu o alvo, a cada ~3 s (o `t % 30 == 0` do DM).
		t.ReengajarEm -= dt;
		if (t.ReengajarEm <= 0) { t.ReengajarEm = 3; Engajar(t); }
	}

	private void TerminarLuta(TorneioVivo t, string vencedor, string como)
	{
		string perdedor = vencedor == t.ChaveA ? t.ChaveB : t.ChaveA;
		t.Chave.Registrar(vencedor, como);
		t.LutasDisputadas++;
		AnunciarTorneio(t, $"{t.Chave.NomeDe(vencedor)} vence a luta contra {t.Chave.NomeDe(perdedor)} ({como})!");

		foreach (string chave in new[] { t.ChaveA, t.ChaveB })
		{
			ServerPlayer? c = CorpoDaChave(chave);
			if (c == null) continue;
			Desengajar(c);
			if (!EhJogador(c) && c.Voando) AlternarVoo(c);   // `tourney_standdown`: pousa, o voo era ferramenta da luta
			if (EhJogador(c) && t.LetalAntes.Remove(c.Id, out bool letal)) c.Combate.Letal = letal;
			if (!LutadorOk(t, c)) continue;
			CurarPraLuta(c);
			Segurar(t, c);
			if (chave == perdedor && EhJogador(c))
				Avisar(c, "voce foi ELIMINADO! Aguarde o encerramento na area de espera (imovel e intocavel).");
		}
		t.ChaveA = ""; t.ChaveB = "";
		t.Fase = FaseDoTorneio.Intervalo;
		t.PrazoMs = NowMs() + (long)(_torneioCfg.IntervaloSegundos * 1000);
	}

	/// <summary>`trn_heal`: levanta do nocaute, membros nao decepados inteiros, Ki e folego cheios.</summary>
	private void CurarPraLuta(ServerPlayer c)
	{
		if (c.Ficha.KO) c.Combate.Levantar();
		foreach (BodyPart membro in c.Combate.Corpo.Partes)
			if (!membro.Decepado) membro.Vida = membro.VidaMax;
		c.Combate.SincronizarVida();
		c.Combate.Stun = 0;
		c.Ficha.Ki = c.Ficha.MaxKi;
		c.Ficha.stamina = c.Ficha.maxstamina;
		c.SigAtributos = "";
		MandarFicha(c);
	}

	// =====================================================================
	// PREMIACAO E FIM
	// =====================================================================

	private void Premiar(TorneioVivo t)
	{
		Chave k = t.Chave;
		if (k.Campeao.Length > 0) AnunciarTorneio(t, $"O CAMPEAO do {t.Rotulo} e... {k.NomeDe(k.Campeao)}!!!");
		foreach ((string chave, int lugar, string titulo) in new[] { (k.Campeao, 1, "CAMPEAO"), (k.Vice, 2, "vice-campeao"), (k.Terceiro, 3, "3o lugar") })
		{
			if (chave.Length == 0) continue;
			double premio = _torneioCfg.Premio(lugar);
			AnunciarTorneio(t, $"{k.NomeDe(chave)} ({titulo}) leva {premio:N0} zeni!");
			ServerPlayer? p = CorpoDaAssinatura(chave);
			if (p == null) continue;   // NPC, ou jogador que sumiu: o `if (M.client)` do `award`
			p.Ficha.Zeni += premio;
			Avisar(p, $"Voce recebeu {premio:N0} zeni de premio do torneio!");
			if (lugar == 1 && t.Tipo == TipoDeTorneio.Alem && _torneioCfg.CampeaoDoAlemRevive && p.Ficha.dead)
			{
				Avisar(p, "Enma Daioh carimba a sua ficha: o campeao do Outro Mundo volta a terra dos vivos!");
				Renascer(p);
			}
			MandarFicha(p);
			Persistir(p);
		}
		EncerrarTorneio(t);
	}

	/// <summary>`cleanup`: solta todo mundo, remove os NPCs, e o torneio deixa de existir.</summary>
	private void EncerrarTorneio(TorneioVivo t)
	{
		foreach (int id in t.Presos.ToList())
			if (_players.TryGetValue(id, out ServerPlayer? c)) Soltar(t, c);
		foreach ((int id, bool letal) in t.LetalAntes)
			if (_players.TryGetValue(id, out ServerPlayer? c)) c.Combate.Letal = letal;
		foreach (int id in t.Npcs)
			if (_players.TryGetValue(id, out ServerPlayer? npc)) { Desengajar(npc); RemoverNpc(npc); }
		foreach (ServerPlayer p in Jogadores.ToList())
			if (t.Convites.ContainsKey(p.Assinatura)) MandarAvisoDoTorneio(p, 2, t.Tipo, 0, "");
		GD.Print($"[server] torneio: {t.Rotulo} encerrado ({t.LutasDisputadas} luta(s); campeao '{t.Chave.NomeDe(t.Chave.Campeao)}')");
		_torneio = null;
	}

	/// <summary>
	/// QUEM ENTRA NO SERVIDOR com torneio rolando: convite atrasado nas inscricoes; de volta a area de
	/// espera se estava na chave; de volta ao seu lugar de lutador se a luta dele esta em curso.
	/// </summary>
	private void AoEntrarNoTorneio(ServerPlayer pl)
	{
		if (_torneio is not { } t || !EhJogador(pl)) return;
		string chave = pl.Assinatura;
		if (t.Fase == FaseDoTorneio.Inscricao)
		{
			if (Elegivel(pl, t.Tipo) && !t.Recusaram.Contains(chave)) Convidar(t, pl);
			return;
		}
		if (t.Chave.Quem(chave) == null) return;
		if (chave == t.ChaveA || chave == t.ChaveB)
		{
			Transportar(pl, t.Zona, PontoDoLutador(t, chave == t.ChaveA ? 1 : 2));
			t.LetalAntes[pl.Id] = pl.Combate.Letal;
			pl.Combate.Letal = false;
			Avisar(pl, "voce voltou no meio da sua luta do torneio -- continue!");
			Engajar(t);
			return;
		}
		Segurar(t, pl);
	}

	private void AnunciarTorneio(TorneioVivo t, string texto)
	{
		EscutaDoTorneio?.Add(texto);
		Anunciar($"{t.Rotulo}: {texto}");
	}

	/// <summary>`tell`: so quem esta no palco (participantes e quem foi assistir).</summary>
	private void DizerAosPresentes(TorneioVivo t, string texto)
	{
		EscutaDoTorneio?.Add(texto);
		foreach (ServerPlayer p in ZoneList(t.Zona.Hash).ToList())
			if (EhJogador(p)) Avisar(p, $"{t.Rotulo}: {texto}");
	}
}
