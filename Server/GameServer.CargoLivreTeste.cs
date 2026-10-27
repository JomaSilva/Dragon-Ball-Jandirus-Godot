using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--cargolivreteste` (2026-10-19) -- OS CARGOS SEM ADMIN. O dono: *"fazer com q todos os cargos do jogo tenham requisitos e
/// n precisem de adm (tirando o anjo)"*, com a tabela de requisitos que ele aprovou e a escolha "vagar com 7 dias".
///
///     Godot --headless --server --port 9421 --cargolivreteste
///
/// Corpos forjados, sem cliente (como a `--cargoportas`). Cinco familias, cada uma com o defeito injetado que a mesma regua REPROVA:
///   P. OS ONZE DA PORTA DO DONO -- os sete do espaco e os quatro Anciaos de Namek se reivindicam: apto com a ficha que cumpre tudo, e a
///      frase da regra que falta com a ficha sem ela; o Anjo continua so do admin. Contra-exemplo: `Cargos.PortasDoDonoFechadasDeTeste`.
///   R. A PORTA DE PRODUCAO -- o `ReivindicarCargo` de verdade: o pirata com a fortuna leva o capt, o pobre le o que falta, o dono de
///      Arconia leva o King of Acronia (o `Ficha` diz os planetas dominados) e quem nao domina nao, o Namekuseijin leva o assento.
///   V. VEGETA CONDENADO -- de pe, o trono do Rei so se toma matando; condenado (no palco de mortes, so na memoria), o Saiyajin de 5M o
///      reivindica, o humano e o fraco leem o que falta. Contra-exemplo: `Cargos.VegetaSemPortaDeTeste`.
///   T. OS TEXTOS DO TRONO VAGO E O FIO -- a frase do Deus da Destruicao e o status do titulo dizem o Bills; e toda frase de "o que
///      falta" cabe nos 160 caracteres do `GetString` do cliente. Contra-exemplo: `Cargos.DueloSemOBillsDeTeste`.
///   A. O TRONO DE QUEM SUMIU -- a volta de 10 min soma a ausencia de quem esta offline, zera a de quem esta online, vaga quem passou
///      dos 7 dias (so a vaga dele, no Kaioshin), poupa o Anjo, nao conta processo suspenso, e o livro vai e volta do disco.
///      Contra-exemplo: `AbandonoDeCargo.AbandonoIgnoradoDeTeste`.
///
/// O MUNDO DE VERDADE E FOTOGRAFADO: os tronos, as ausencias (e o `cargos-ausencia.json`), os dominios e o livro dos mortos (pelo
/// palco de mortes) voltam no `finally` como estavam.
/// </summary>
public partial class GameServer
{
	private const int IdBaseDoCargoLivre = 99_400;

	private int _clOk, _clFalhou;

	private void AfirmarCl(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _clOk++; GD.Print($"[cargolivre]   OK    {oque}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); return; }
		_clFalhou++;
		GD.PrintErr($"[cargolivre]   FALHA {oque}   [{detalhe}]");
	}

	/// <summary>O limite do fio: o cliente le o "o que falta" de cada cargo por `GetString(160)` (`GameClient.cs`, `S2C.Cargos`).</summary>
	private const int LetrasDoFalta = 160;

	private static bool CabeNoFio(string texto) => texto.Length <= LetrasDoFalta;

	public void RodarBancadaDosCargosSemAdmin()
	{
		_clOk = _clFalhou = 0;
		GD.Print("[cargolivre] ================ OS CARGOS SEM ADMIN (dono, 2026-10-19) ================");

		var tronosReais = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		var personagensReais = new Dictionary<string, (string Conta, int Slot, string Nome)>(_personagensDosTronos, StringComparer.OrdinalIgnoreCase);
		var ausenciasReais = _ausencias.ToDictionary(kv => kv.Key, kv => new Ausencia { Conta = kv.Value.Conta, Ms = kv.Value.Ms },
													 StringComparer.OrdinalIgnoreCase);
		long voltaReal = _voltaDoAbandono;
		string? arquivoDasAusencias = System.IO.File.Exists(CaminhoDasAusencias) ? System.IO.File.ReadAllText(CaminhoDasAusencias) : null;
		var dominiosReais = new List<Dominio>(_dominios);
		var forjados = new List<ServerPlayer>();
		_tronos.Clear();
		_ausencias.Clear();

		ZoneKey zona = ZoneKey.Premade("Namek");
		ServerPlayer Forjar(int i, string nome, string raca, double bp, int karma = 0, double zeni = 0)
		{
			var novo = new ServerPlayer
			{
				Id = IdBaseDoCargoLivre + i,
				Peer = null,
				Name = nome,
				Race = raca,
				Genero = "Male",
				Idade = 30,
				Zone = zona,
				Pos = new Vec2(i * 3f * ZoneCollision.TileSize, 0),
				Conta = $"bancada_livre_{i}",
				Slot = 0,
				Ficha = new Fighter { Race = raca, BP = bp },
				Livro = new SkillBook(),
				Karma = karma,
			};
			novo.Ficha.Class = "Normal";
			novo.Ficha.Zeni = zeni;
			PorNoMundo(novo);
			forjados.Add(novo);
			return novo;
		}

		try
		{
			AsOnzeDaPortaDoDono();
			APortaDeProducao(Forjar);
			VegetaCondenadoAbreOTrono(Forjar);
			OsTextosEOFio(Forjar);
			OTronoDeQuemSumiu(Forjar);
			OCargoEDoPersonagem(Forjar);   // (2026-10-26) o cargo ligado ao personagem, e nao a conta
		}
		catch (Exception e) { AfirmarCl("a bancada rodou inteira", false, e.ToString()); }
		finally
		{
			Cargos.PortasDoDonoFechadasDeTeste = false;
			Cargos.VegetaSemPortaDeTeste = false;
			Cargos.DueloSemOBillsDeTeste = false;
			AbandonoDeCargo.AbandonoIgnoradoDeTeste = false;
			foreach (ServerPlayer p in forjados)
			{
				ZoneList(p.Zone.Hash).Remove(p);
				_players.Remove(p.Id);
			}
			_dominios.Clear();
			_dominios.AddRange(dominiosReais);
			_tronos.Clear();
			foreach ((string k, string v) in tronosReais) _tronos[k] = v;
			_personagensDosTronos.Clear();
			foreach ((string k, (string Conta, int Slot, string Nome) v) in personagensReais) _personagensDosTronos[k] = v;
			SalvarCargos();
			_ausencias.Clear();
			foreach ((string k, Ausencia v) in ausenciasReais) _ausencias[k] = v;
			_voltaDoAbandono = voltaReal;
			try
			{
				if (arquivoDasAusencias != null) System.IO.File.WriteAllText(CaminhoDasAusencias, arquivoDasAusencias);
				else if (System.IO.File.Exists(CaminhoDasAusencias)) System.IO.File.Delete(CaminhoDasAusencias);
			}
			catch (Exception e) { GD.PushWarning($"[cargolivre] nao devolvi o cargos-ausencia.json: {e.Message}"); }
			GD.Print($"[cargolivre] ================ {_clOk} OK, {_clFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// P) OS ONZE DA PORTA DO DONO -- a regra pura
	// =====================================================================
	private void AsOnzeDaPortaDoDono()
	{
		GD.Print("[cargolivre] -- P) os onze da porta do dono --");
		var rep15 = new Dictionary<string, double> { ["Arlia"] = Jandirus.Core.Social.Reputacao.LimiarDeAmigavel };
		var semRep = new Dictionary<string, double>();
		// (cargo, a ficha que cumpre tudo, [(a ficha sem UMA regra, o comeco da frase que tem de sair)])
		(string Chave, FichaDeRank Apta, (FichaDeRank Ficha, string Frase)[] Faltas)[] casos =
		[
			("kingofacronia", new FichaDeRank { Raca = "Human", RacaMae = "Human", BpBase = 3_000_000, PlanetasQueDomina = ["Arconia"] },
			 [(new FichaDeRank { Raca = "Human", RacaMae = "Human", BpBase = 3_000_000, PlanetasQueDomina = ["Namek"] }, "dominar o planeta Arconia"),
			  (new FichaDeRank { Raca = "Human", RacaMae = "Human", BpBase = 3_000_000, PlanetasQueDomina = null }, "dominar o planeta Arconia"),
			  (new FichaDeRank { Raca = "Human", RacaMae = "Human", BpBase = 2_999_999, PlanetasQueDomina = ["Arconia"] }, "3M de BP base")]),
			("arconianguardian", new FichaDeRank { Raca = "Alien", RacaMae = "Alien", Karma = 50, BpBase = 2_000_000 },
			 [(new FichaDeRank { Raca = "Alien", RacaMae = "Alien", Karma = 49, BpBase = 2_000_000 }, "karma 50+"),
			  (new FichaDeRank { Raca = "Alien", RacaMae = "Alien", Karma = 50, BpBase = 1_999_999 }, "2M de BP base")]),
			("saibamenrougeleader", new FichaDeRank { Raca = "Saibaman", RacaMae = "Saibaman", Karma = 0, BpBase = 1_000_000 },
			 [(new FichaDeRank { Raca = "Human", RacaMae = "Human", Karma = 0, BpBase = 1_000_000 }, "sangue Saibaman"),
			  (new FichaDeRank { Raca = "Saibaman", RacaMae = "Saibaman", Karma = 1, BpBase = 1_000_000 }, "karma 0 ou menos"),
			  (new FichaDeRank { Raca = "Saibaman", RacaMae = "Saibaman", Karma = 0, BpBase = 999_999 }, "1M de BP base")]),
			("capt", new FichaDeRank { Raca = "Human", RacaMae = "Human", Karma = -25, BpBase = 3_000_000, Zeni = 1_000_000 },
			 [(new FichaDeRank { Raca = "Human", RacaMae = "Human", Karma = -24, BpBase = 3_000_000, Zeni = 1_000_000 }, "karma -25 ou menos"),
			  (new FichaDeRank { Raca = "Human", RacaMae = "Human", Karma = -25, BpBase = 2_999_999, Zeni = 1_000_000 }, "3M de BP base"),
			  (new FichaDeRank { Raca = "Human", RacaMae = "Human", Karma = -25, BpBase = 3_000_000, Zeni = 999_999 }, "1M de zeni no bolso")]),
			("geti", new FichaDeRank { Raca = "Meta", RacaMae = "Meta", BpBase = 3_000_000 },
			 [(new FichaDeRank { Raca = "Human", RacaMae = "Human", BpBase = 3_000_000 }, "sangue Meta"),
			  (new FichaDeRank { Raca = "Meta", RacaMae = "Meta", BpBase = 2_999_999 }, "3M de BP base")]),
			("mutany", new FichaDeRank { Raca = "Human", RacaMae = "Human", Karma = 0, BpBase = 2_000_000, PlanetasDominados = 1 },
			 [(new FichaDeRank { Raca = "Human", RacaMae = "Human", Karma = 0, BpBase = 2_000_000, PlanetasDominados = 0 }, "dominar um planeta"),
			  (new FichaDeRank { Raca = "Human", RacaMae = "Human", Karma = 1, BpBase = 2_000_000, PlanetasDominados = 1 }, "karma 0 ou menos"),
			  (new FichaDeRank { Raca = "Human", RacaMae = "Human", Karma = 0, BpBase = 1_999_999, PlanetasDominados = 1 }, "2M de BP base")]),
			("arlian", new FichaDeRank { Raca = "Arlian", RacaMae = "Arlian", BpBase = 1_000_000, ReputacaoDePlaneta = rep15 },
			 [(new FichaDeRank { Raca = "Human", RacaMae = "Human", BpBase = 1_000_000, ReputacaoDePlaneta = rep15 }, "sangue Arlian"),
			  (new FichaDeRank { Raca = "Arlian", RacaMae = "Arlian", BpBase = 1_000_000, ReputacaoDePlaneta = semRep }, "ser Amigável com o povo de Arlia"),
			  (new FichaDeRank { Raca = "Arlian", RacaMae = "Arlian", BpBase = 999_999, ReputacaoDePlaneta = rep15 }, "1M de BP base")]),
		];
		var anciao = new FichaDeRank { Raca = "Namekian", RacaMae = "Namekian", Karma = 10 };
		(FichaDeRank, string)[] faltasDoAnciao =
		[
			(new FichaDeRank { Raca = "Human", RacaMae = "Human", Karma = 10 }, "sangue Namekuseijin"),
			(new FichaDeRank { Raca = "Namekian", RacaMae = "Namekian", Karma = 9 }, "karma 10+"),
		];
		casos = [.. casos, .. Nomeacao.Assentos.Select(a => (a, anciao, faltasDoAnciao))];

		foreach ((string chave, FichaDeRank apta, (FichaDeRank Ficha, string Frase)[] faltas) in casos)
		{
			RankDef r = Cargos.Get(chave)!;
			string? falta = Cargos.OqueFalta(r, apta);
			AfirmarCl($"P. **{r.Nome} SE REIVINDICA**: porta de requisito aberta pelo dono, e a ficha que cumpre tudo e APTA",
					  r.Porta == PortaDoCargo.Prova && r.PortaDoDono && falta == null, $"porta {r.Porta}, falta \"{falta}\"");
			foreach ((FichaDeRank ficha, string frase) in faltas)
			{
				string sem = Cargos.OqueFalta(r, ficha) ?? "";
				AfirmarCl($"P. {r.Nome}: sem a regra, o que falta e \"{frase}...\"", sem.StartsWith(frase, StringComparison.Ordinal), sem);
			}
		}

		RankDef capt = Cargos.Get("capt")!;
		FichaDeRank pirata = casos.First(c => c.Chave == "capt").Apta;
		Cargos.PortasDoDonoFechadasDeTeste = true;
		string? fechada;
		try { fechada = Cargos.OqueFalta(capt, pirata); }
		finally { Cargos.PortasDoDonoFechadasDeTeste = false; }
		AfirmarCl("(defeito injetado: as portas do dono fechadas, o jogo de antes) a mesma regua REPROVA -- o pirata com tudo le \"dado por um "
				  + "administrador\"", fechada != null && fechada.Contains("administrador", StringComparison.Ordinal), fechada ?? "(apto)");

		RankDef anjo = Cargos.Get("angel")!;
		AfirmarCl("P. **O ANJO CONTINUA SO DO ADMIN** (*\"tirando o anjo\"*): porta de admin, sem a marca do dono, e o que falta diz o administrador",
				  anjo.Porta == PortaDoCargo.Admin && !anjo.PortaDoDono
				  && (Cargos.OqueFalta(anjo, pirata) ?? "").Contains("administrador", StringComparison.Ordinal));
		int reivindicaveis = Cargos.Reivindicaveis.Count();
		AfirmarCl("P. vinte e sete dos trinta se reivindicam (eram dezesseis); fora, so o duelo, a sucessao e o Anjo",
				  reivindicaveis == 27 && Cargos.Todos.Length == 30
				  && Cargos.Todos.Where(x => x.Porta != PortaDoCargo.Prova).Select(x => x.Chave).OrderBy(x => x)
							.SequenceEqual(["angel", "godofdestruction", "kov"]),
				  $"{reivindicaveis} de {Cargos.Todos.Length}");
	}

	// =====================================================================
	// R) A PORTA DE PRODUCAO
	// =====================================================================
	private void APortaDeProducao(Func<int, string, string, double, int, double, ServerPlayer> forjar)
	{
		GD.Print("[cargolivre] -- R) a porta de producao (ReivindicarCargo) --");
		ServerPlayer pirata = forjar(1, "bancada: o pirata", "Human", 3_000_000, -30, 1_000_000);
		ServerPlayer pobre = forjar(2, "bancada: o pirata pobre", "Human", 3_000_000, -30, 10);
		ServerPlayer acronia = forjar(3, "bancada: o dono de Arconia", "Human", 3_000_000, 0, 0);
		ServerPlayer semPlaneta = forjar(4, "bancada: o sem planeta", "Human", 3_000_000, 0, 0);
		ServerPlayer namekuseijin = forjar(5, "bancada: o Namekuseijin", "Namekian", 1_000, 10, 0);

		List<string> ouviu = Ouvir(() => ReivindicarCargo(pobre, "capt"));
		AfirmarCl("R. o pirata sem a fortuna le o que falta, e o trono continua vago",
				  !_tronos.ContainsKey("capt") && ouviu.Any(a => a.Contains("1M de zeni no bolso", StringComparison.Ordinal)), string.Join(" | ", ouviu));
		Cargos.PortasDoDonoFechadasDeTeste = true;
		try { ouviu = Ouvir(() => ReivindicarCargo(pirata, "capt")); }
		finally { Cargos.PortasDoDonoFechadasDeTeste = false; }
		AfirmarCl("(defeito injetado: as portas do dono fechadas) a mesma regua REPROVA -- o pirata com tudo e recusado pelo \"administrador\"",
				  !_tronos.ContainsKey("capt") && ouviu.Any(a => a.Contains("administrador", StringComparison.Ordinal)), string.Join(" | ", ouviu));
		ReivindicarCargo(pirata, "capt");
		AfirmarCl("R. **O PIRATA COM A FORTUNA LEVA O REI DOS PIRATAS** pelo `ReivindicarCargo` de verdade",
				  _tronos.TryGetValue("capt", out string? c1) && c1 == pirata.Conta, c1 ?? "(vago)");

		var arconia = new Dominio
		{
			PreFeito = true, Planeta = "Arconia", Assinatura = acronia.Assinatura, Conta = acronia.Conta, Nome = acronia.Name,
			Desde = TempoDoMundo, Coletado = TempoDoMundo, Visto = TempoDoMundo,
		};
		_dominios.Add(arconia);
		FichaDeRank f = Ficha(acronia);
		AfirmarCl("R. o `Ficha` de producao diz os planetas pre-feitos dominados (Arconia)",
				  f.PlanetasQueDomina != null && f.PlanetasQueDomina.Contains("Arconia"), string.Join(",", f.PlanetasQueDomina ?? []));
		ouviu = Ouvir(() => ReivindicarCargo(semPlaneta, "kingofacronia"));
		AfirmarCl("R. quem nao domina Arconia le o que falta",
				  !_tronos.ContainsKey("kingofacronia") && ouviu.Any(a => a.Contains("dominar o planeta Arconia", StringComparison.Ordinal)),
				  string.Join(" | ", ouviu));
		ReivindicarCargo(acronia, "kingofacronia");
		AfirmarCl("R. **O DONO DE ARCONIA LEVA O KING OF ACRONIA**",
				  _tronos.TryGetValue("kingofacronia", out string? c2) && c2 == acronia.Conta, c2 ?? "(vago)");

		ReivindicarCargo(namekuseijin, "eastelder");
		AfirmarCl("R. **O NAMEKUSEIJIN DE KARMA 10 LEVA UM ASSENTO DE ANCIAO** sem Grande Anciao nenhum",
				  _tronos.TryGetValue("eastelder", out string? c3) && c3 == namekuseijin.Conta, c3 ?? "(vago)");
	}

	// =====================================================================
	// V) VEGETA CONDENADO
	// =====================================================================
	private void VegetaCondenadoAbreOTrono(Func<int, string, string, double, int, double, ServerPlayer> forjar)
	{
		GD.Print("[cargolivre] -- V) o trono de Vegeta condenado --");
		ServerPlayer saiya = forjar(6, "bancada: o Saiyajin", "Saiyan", 5_000_000, 0, 0);
		ServerPlayer fraco = forjar(7, "bancada: o Saiyajin fraco", "Saiyan", 1_000_000, 0, 0);
		ServerPlayer humano = forjar(8, "bancada: o humano forte", "Human", 5_000_000, 0, 0);
		RankDef kov = Cargos.Get(Sucessao.ReiDeVegeta)!;
		_tronos.Remove(kov.Chave);

		string dePe = Cargos.OqueFalta(kov, Ficha(saiya)) ?? "";
		AfirmarCl("V. COM VEGETA DE PE o trono do Rei nao se reivindica: mata-se o Rei (o NPC, com ele vago)",
				  !Ficha(saiya).VegetaCondenado && dePe.Contains("mata-se o Rei", StringComparison.Ordinal), dePe);

		using (PalcoDeMortesDeBancada())
		{
			ZoneKey vegeta = ZoneKey.Premade("Vegeta");
			_mortos.Por(new EstadoDaMorte { Chave = ChaveDePlaneta.Da(vegeta)!.Value.Texto, Nome = "Vegeta", Fase = FaseDaMorte.Destruido });
			FichaDeRank f = Ficha(saiya);
			AfirmarCl("V. o `Ficha` de producao ve Vegeta condenado (o livro dos mortos)", f.VegetaCondenado);
			AfirmarCl("V. o humano le \"sangue Saiyajin\" e o Saiyajin de 1M le \"5M de BP base\"",
					  (Cargos.OqueFalta(kov, Ficha(humano)) ?? "").StartsWith("sangue Saiyajin", StringComparison.Ordinal)
					  && (Cargos.OqueFalta(kov, Ficha(fraco)) ?? "").StartsWith("5M de BP base", StringComparison.Ordinal),
					  $"{Cargos.OqueFalta(kov, Ficha(humano))} | {Cargos.OqueFalta(kov, Ficha(fraco))}");

			Cargos.VegetaSemPortaDeTeste = true;
			string? semPorta;
			try { semPorta = Cargos.OqueFalta(kov, f); }
			finally { Cargos.VegetaSemPortaDeTeste = false; }
			AfirmarCl("(defeito injetado: o trono de Vegeta condenado sem porta, o jogo de antes) a mesma regua REPROVA -- o Saiyajin de 5M le "
					  + "a sucessao", semPorta != null, semPorta ?? "(apto)");

			ReivindicarCargo(saiya, kov.Chave);
			AfirmarCl("V. **COM VEGETA CONDENADO O SAIYAJIN DE 5M REIVINDICA O TRONO VAGO** pelo `ReivindicarCargo` de verdade",
					  _tronos.TryGetValue(kov.Chave, out string? dono) && dono == saiya.Conta, dono ?? "(vago)");
			List<string> ouviu = Ouvir(() => ReivindicarCargo(humano, kov.Chave));
			// COM O TRONO VAGO (o mutante que fecha a porta de Vegeta) a regua le vazio e REPROVA -- o indexador estourava e derrubava a
			// bancada antes do T e do A (r1 de 2026-10-19: as contraprovas x1/x2 sem as linhas delas).
			AfirmarCl("V. com o trono ocupado o resto nao toma por requisito (\"ja tem dono\")",
					  _tronos.GetValueOrDefault(kov.Chave) == saiya.Conta && ouviu.Any(a => a.Contains("ja tem dono", StringComparison.Ordinal)), string.Join(" | ", ouviu));
			_tronos.Remove(kov.Chave);
		}
	}

	// =====================================================================
	// T) OS TEXTOS DO TRONO VAGO, E O FIO
	// =====================================================================
	private void OsTextosEOFio(Func<int, string, string, double, int, double, ServerPlayer> forjar)
	{
		GD.Print("[cargolivre] -- T) os textos do trono vago e o fio --");
		ServerPlayer leitor = forjar(9, "bancada: quem le", "Saiyan", 100, 0, 0);
		RankDef deus = Cargos.Get("godofdestruction")!;
		_tronos.Remove(deus.Chave);
		string frase = Cargos.OqueFalta(deus, Ficha(leitor)) ?? "";
		List<string> status = Ouvir(() => VerboStatusDoTitulo(leitor));
		AfirmarCl("T. **O TRONO VAGO DO DEUS DIZ O CAMINHO**: o que falta e o status do titulo falam do Bills (e nao da outorga)",
				  frase.Contains("Bills", StringComparison.Ordinal) && status.Any(a => a.Contains("Bills", StringComparison.Ordinal))
				  && !status.Any(a => a.Contains("outorga", StringComparison.Ordinal)), $"{frase} | {string.Join(" | ", status)}");

		Cargos.DueloSemOBillsDeTeste = true;
		string fraseVelha;
		List<string> statusVelho;
		try { fraseVelha = Cargos.OqueFalta(deus, Ficha(leitor)) ?? ""; statusVelho = Ouvir(() => VerboStatusDoTitulo(leitor)); }
		finally { Cargos.DueloSemOBillsDeTeste = false; }
		AfirmarCl("(defeito injetado: as frases de antes) a mesma regua REPROVA -- \"outorga\" e nenhum Bills",
				  !fraseVelha.Contains("Bills", StringComparison.Ordinal) && statusVelho.Any(a => a.Contains("outorga", StringComparison.Ordinal)),
				  $"{fraseVelha} | {string.Join(" | ", statusVelho)}");

		// TODA FRASE DE "O QUE FALTA" CABE NO FIO, pra toda ficha que esta bancada sabe montar -- inclusive com Vegeta condenado
		var fichas = new List<FichaDeRank>
		{
			new() { Raca = "Human", RacaMae = "Human" },
			new() { Raca = "Saiyan", RacaMae = "Saiyan", BpBase = 1, Karma = -100 },
			new() { Raca = "Namekian", RacaMae = "Namekian", Karma = 100, VegetaCondenado = true, ReputacaoDePlaneta = new Dictionary<string, double>() },
		};
		var grandes = new List<string>();
		foreach (RankDef r in Cargos.Todos)
			foreach (FichaDeRank f in fichas)
				if (Cargos.OqueFalta(r, f) is { } t && !CabeNoFio(t)) grandes.Add($"{r.Chave}: {t.Length} letras");
		AfirmarCl($"T. **TODA FRASE DE \"O QUE FALTA\" CABE NO FIO** (ate {LetrasDoFalta} letras: acima disso o cliente le vazio e mostra o cargo apto)",
				  grandes.Count == 0, string.Join(" | ", grandes));
		AfirmarCl("(regua do fio) uma frase de 161 letras NAO cabe -- a regua sabe reprovar", !CabeNoFio(new string('x', LetrasDoFalta + 1)));
	}

	// =====================================================================
	// A) O TRONO DE QUEM SUMIU
	// =====================================================================
	private void OTronoDeQuemSumiu(Func<int, string, string, double, int, double, ServerPlayer> forjar)
	{
		GD.Print("[cargolivre] -- A) o trono de quem sumiu --");
		ServerPlayer presente = forjar(10, "bancada: o que joga", "Human", 1_000_000, 0, 0);
		_tronos.Clear();
		_ausencias.Clear();
		_tronos["turtle"] = "bancada_livre_sumido";
		_tronos["kaioshin#2"] = "bancada_livre_sumido_k";
		_tronos["kaioshin"] = "bancada_livre_kaioshin_um";
		_tronos["crane"] = presente.Conta;
		_tronos["angel"] = "bancada_livre_anjo";

		long t0 = 1_800_000_000_000;
		_voltaDoAbandono = 0;
		VoltaDoAbandono(t0);
		AfirmarCl("A. a primeira volta depois do boot so ANCORA: abre o livro de quem esta offline com zero, e o Anjo nem entra",
				  _ausencias.TryGetValue("turtle", out Ausencia? at) && at.Ms == 0 && !_ausencias.ContainsKey("angel") && _tronos.Count == 5,
				  string.Join(", ", _ausencias.Select(kv => $"{kv.Key}={kv.Value.Ms}")));

		long quase = AbandonoDeCargo.MsSemEntrar - 60_000;
		_ausencias["turtle"].Ms = quase;
		_ausencias["kaioshin#2"].Ms = quase;
		_ausencias["crane"].Ms = quase;
		_ausencias["kaioshin"].Ms = 0;

		AbandonoDeCargo.AbandonoIgnoradoDeTeste = true;
		try { VoltaDoAbandono(t0 + AbandonoDeCargo.MsEntreVoltas); }
		finally { AbandonoDeCargo.AbandonoIgnoradoDeTeste = false; }
		AfirmarCl("(defeito injetado: a ausencia que nao vence, o jogo de antes) a mesma regua REPROVA -- o dono sumido ha 7 dias continua "
				  + "no trono", _tronos.ContainsKey("turtle") && _tronos.ContainsKey("kaioshin#2"));

		_ausencias["turtle"].Ms = quase;
		_ausencias["kaioshin#2"].Ms = quase;
		VoltaDoAbandono(t0 + 2 * AbandonoDeCargo.MsEntreVoltas);
		AfirmarCl("A. **SETE DIAS SEM ENTRAR, O CARGO VAGA**: o Eremita Tartaruga sumido perde o trono, e no Kaioshin so a VAGA dele",
				  !_tronos.ContainsKey("turtle") && !_tronos.ContainsKey("kaioshin#2") && _tronos.ContainsKey("kaioshin"),
				  string.Join(", ", _tronos.Keys));
		AfirmarCl("A. quem esta jogando zera a ausencia e fica; o Anjo sumido fica (*\"tirando o anjo\"*)",
				  _tronos.ContainsKey("crane") && _ausencias["crane"].Ms == 0 && _tronos.ContainsKey("angel"));

		// O PROCESSO PARADO NAO E O DONO SUMINDO: cinco horas entre duas voltas somam so o teto (duas voltas)
		_tronos["turtle"] = "bancada_livre_sumido";
		VoltaDoAbandono(t0 + 3 * AbandonoDeCargo.MsEntreVoltas);
		_ausencias["turtle"].Ms = AbandonoDeCargo.MsSemEntrar - AbandonoDeCargo.TetoDoPasso - 60_000;
		VoltaDoAbandono(t0 + 3 * AbandonoDeCargo.MsEntreVoltas + 5L * 3600 * 1000);
		// (sem o teto o trono VAGA aqui e o livro perde a linha: a regua le "(vagou)" e reprova, em vez de estourar)
		long? parado = _ausencias.GetValueOrDefault("turtle")?.Ms;
		AfirmarCl("A. **SERVIDOR PARADO NAO CONTA**: cinco horas entre duas voltas (a maquina dormiu) somam so o teto, e o trono fica",
				  _tronos.ContainsKey("turtle") && parado == AbandonoDeCargo.MsSemEntrar - 60_000,
				  $"{parado?.ToString() ?? "(vagou)"} de {AbandonoDeCargo.MsSemEntrar}");
		AfirmarCl("A. a primeira volta depois de um boot soma zero (o servidor desligado tambem nao conta)",
				  AbandonoDeCargo.Passo(0, t0) == 0 && AbandonoDeCargo.Passo(t0, t0 + 1000) == 1000);

		// O LIVRO VAI E VOLTA DO DISCO pela porta da subida do servidor (`CarregarCargos`, a do boot)
		SalvarAusencias();
		SalvarCargos();
		long antes = _ausencias.GetValueOrDefault("turtle")?.Ms ?? -1;
		_ausencias.Clear();
		CarregarCargos();
		AfirmarCl("A. o `cargos-ausencia.json` guarda a ausencia e a subida do servidor a le de volta (o desligado nao zera ninguem)",
				  _ausencias.TryGetValue("turtle", out Ausencia? lida) && lida.Ms == antes && lida.Conta == "bancada_livre_sumido" && _voltaDoAbandono == 0,
				  $"{lida?.Ms} / {antes}");

		// E A VOLTA SAI DO TIQUE DE 1 Hz DOS CARGOS -- a porta de producao, e nao a chamada direta das linhas de cima
		_voltaDoAbandono = 0;
		TickDosCargos();
		AfirmarCl("A. o tique dos cargos (1 Hz) faz a volta do abandono", _voltaDoAbandono > 0, $"{_voltaDoAbandono}");
	}

	// =====================================================================
	// B) O CARGO E DO PERSONAGEM, E NAO DA CONTA (2026-10-26)
	// =====================================================================
	/// <summary>
	/// O dono: *"faca os ranks serem ligados ao personagem e nao a conta assim n tem como ter varios personagens da mesma conta caindo no
	/// mesmo rank"*; e, perguntado, *"Sim, cada um o seu"* (personagens da mesma conta podem ter cargos DIFERENTES, nunca o mesmo) e *"O 1o
	/// que entrar com ele"* (o cargo que estava na conta fica com o primeiro personagem dela que entrar). Dois personagens de UMA conta (os
	/// slots 0 e 1), forjados como o resto da bancada, pelas portas de producao: o `Outorgar`, o `ReivindicarCargo`, o dono no jogo que o
	/// abandono e as missoes perguntam (`OnlineDoTrono`), a ida e volta do `cargos.txt` e o `PurgarAssinatura` de quem apaga um personagem.
	/// </summary>
	private void OCargoEDoPersonagem(Func<int, string, string, double, int, double, ServerPlayer> forjar)
	{
		GD.Print("[cargolivre] -- B) o cargo e do personagem, e nao da conta --");
		const string Conta = "bancada_livre_pessoal";
		ServerPlayer DaConta(int i, string nome, int slot)
		{
			ServerPlayer p = forjar(i, nome, "Human", 1_000_000, 0, 0);
			p.Conta = Conta;
			p.Slot = slot;
			return p;
		}
		_tronos.Clear();
		_personagensDosTronos.Clear();
		ServerPlayer a = DaConta(30, "bancada: o primeiro", 0), b = DaConta(31, "bancada: o irmao de conta", 1);
		RankDef kaioshin = Cargos.Get("kaioshin")!, guardiao = Cargos.Get("guardian")!;

		// 1. o cargo de um nao e do outro
		Outorgar(kaioshin, a);
		AfirmarCl("B. **O CARGO E DO PERSONAGEM** (*\"ligados ao personagem e nao a conta\"*): o Kaioshin dado a um personagem nao vale pro "
				  + "outro da MESMA conta",
				  CargoDe(a) == "kaioshin" && CargoDe(b) == "", $"o primeiro '{CargoDe(a)}', o irmao '{CargoDe(b)}'");

		// 2. a mesma conta nao poe dois personagens no mesmo cargo -- nem com as vagas do Kaioshin sobrando
		List<string> ouviu = Ouvir(() => ReivindicarCargo(b, "kaioshin"));
		AfirmarCl("B. **E A MESMA CONTA NAO POE DOIS PERSONAGENS NO MESMO CARGO**: com quatro vagas de Kaioshin sobrando, o irmao de conta e "
				  + "recusado, e pelo motivo",
				  CargoDe(b) == "" && ouviu.Any(f => f.Contains("outro personagem da sua conta")), string.Join(" | ", ouviu));

		// 3. cargos diferentes, cada um o seu
		Outorgar(guardiao, b);
		AfirmarCl("B. **CADA UM O SEU** (*\"Sim, cada um o seu\"*): o irmao de conta vira Guardiao, e o primeiro continua Kaioshin",
				  CargoDe(a) == "kaioshin" && CargoDe(b) == "guardian", $"'{CargoDe(a)}' e '{CargoDe(b)}'");

		// 4. o dono do trono no jogo e o PERSONAGEM: com o Kaioshin fora do mundo, o irmao de conta presente nao o representa
		string vagaDoKaioshin = VagaDe(a);
		ZoneList(a.Zone.Hash).Remove(a);
		_players.Remove(a.Id);
		ServerPlayer? noJogo = OnlineDoTrono(vagaDoKaioshin);
		bool oIrmaoEODele = OnlineDoTrono(VagaDe(b)) == b;
		PorNoMundo(a);
		AfirmarCl("B. **O DONO NO JOGO E O PERSONAGEM**: com o Kaioshin fora do jogo e o irmao de conta dentro, o trono do Kaioshin fica sem o "
				  + "dono no jogo (e o abandono conta o tempo dele) -- e o do Guardiao e do irmao",
				  vagaDoKaioshin.Length > 0 && noJogo == null && oIrmaoEODele,
				  $"vaga '{vagaDoKaioshin}', no jogo: {noJogo?.Name ?? "ninguem"}");

		// 5. o trono de antes, so da conta, fica com o PRIMEIRO personagem que entrar
		_tronos.Clear();
		_personagensDosTronos.Clear();
		_tronos["turtle"] = Conta;   // como o `cargos.txt` de antes guardava: so a conta
		string doUm = CargoDe(b), doZero = CargoDe(a);   // o slot 1 pergunta PRIMEIRO
		AfirmarCl("B. **O CARGO QUE ESTAVA NA CONTA FICA COM O PRIMEIRO QUE ENTRAR** (*\"O 1o que entrar com ele\"*): o slot 1 pergunta "
				  + "primeiro e o leva; o slot 0 fica sem",
				  doUm == "turtle" && doZero == "", $"slot 1 '{doUm}', slot 0 '{doZero}'");

		// ...e isso vai pro disco: depois do boot o trono continua do slot 1 -- perguntado agora pelo slot 0 PRIMEIRO, que e quem o levaria se
		// o arquivo tivesse perdido o personagem
		SalvarCargos();
		_tronos.Clear();
		_personagensDosTronos.Clear();
		CarregarCargos();
		string depoisZero = CargoDe(a), depoisUm = CargoDe(b);
		AfirmarCl("B. ...e o `cargos.txt` guarda o PERSONAGEM: depois do boot o Eremita continua do slot 1, mesmo com o slot 0 perguntando antes",
				  depoisUm == "turtle" && depoisZero == "", $"slot 1 '{depoisUm}', slot 0 '{depoisZero}'");

		// 6. o personagem apagado leva o cargo junto -- o slot que vagou nao passa o trono pro proximo personagem dele
		PurgarAssinatura(ServerPlayer.AssinaturaDe(Conta, 1));
		AfirmarCl("B. **O PERSONAGEM APAGADO DEIXA O TRONO VAGO**: o slot reciclado nao herda o cargo de quem saiu",
				  !_tronos.ContainsKey("turtle"), string.Join(", ", _tronos.Keys));

		_tronos.Clear();
		_personagensDosTronos.Clear();
	}
}
