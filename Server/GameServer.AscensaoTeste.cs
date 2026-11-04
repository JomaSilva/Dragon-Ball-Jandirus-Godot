using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--ascensaoteste` -- A ASCENSAO DAS RACAS SEM TRANSFORMACAO (dono, 2026-10-23). A regra esta no cabecalho de
/// `Core/Races/Ascensao.cs`; o escritor, em `GameServer.Ascensao.cs`. Seis jogadores entram pela PORTA (clientes crus): tres de
/// racas que ascendem (Kai, Saibaman, Tsujin) e tres que nao (Saiyajin, Namekuseijin, Majin).
///
///   A. QUEM ASCENDE: o `races.json` inteiro, classe por classe, pela regra do catalogo -- e o resultado e a lista do dono, as 14.
///      CONTRA-EXEMPLOS `MajinAscendeDeTeste` (o Majin entra) e `EscadaNaoContaDeTeste` (o Saiyajin entra).
///   B. A RAMPA: os numeros do dono nas portas (2x, 4x, 16x, lidos do catalogo do Saiyajin), os valores do meio calculados a parte, e
///      nenhum salto nem descida de 1 mil a 100 bilhoes. CONTRA-EXEMPLO `DegrauSecoDeTeste` (o degrau que o dono nao quer).
///   C. OS SEIS JOGADORES nos BPs base 1 mi, 1,5 mi, 100 mi, 583 mi (a porta do SSJ2), 1 bi, 2 bi e 10 bi, pelo tique de fichas de
///      producao: o `BPBoost` de cada um e o poder expresso que ele multiplica.
///   D. O NPC NAO ASCENDE: um Kai sem dono, de 2 bilhoes, pelas duas portas do escritor. CONTRA-EXEMPLO `AscensaoProNpcDeTeste`.
///   E. A FORMA ABSOLUTA TOMA O LUGAR: o Kai de 2 bilhoes no Mistico expressa os 16x do Mistico, e nao 16 x 16 -- no MESMO instante da
///      troca, pelo `AplicarForma`. CONTRA-EXEMPLO `FormaAbsolutaSomaDeTeste`.
///   F. OS MARCOS DE GANHO nas portas: x2 na do SSJ1, x4 na do SSJ2, x8 na do SSJ3 (o 2^x do pedido 188), pelo `Treinar`. CONTRA-EXEMPLO
///      `MarcosNoDegrauDoDmDeTeste` (os 5/10/20 do DM).
///   G. O FIO: a ficha lenta (`AtributosState.Ascensao`) leva o bonus de cada um, lida pelo leitor de producao.
///
///     Godot --headless -- --server --port 10930 --ascensaoteste
/// </summary>
public sealed partial class GameServer
{
	private bool _ascensaoJaRodou;
	private int _ascOk, _ascFalhou;

	private static readonly string[] ContasDaAscensao =
		["bancada_asc_kai", "bancada_asc_saibaman", "bancada_asc_tsujin", "bancada_asc_saiyan", "bancada_asc_namek", "bancada_asc_majin"];

	/// <summary>As racas dos seis jogadores, na ordem das contas: as tres primeiras ascendem, as tres ultimas nao.</summary>
	private static readonly string[] RacasDaAscensao = ["Kai", "Saibaman", "Tsujin", "Saiyan", "Namekian", "Majin"];

	/// <summary>
	/// A LISTA DO DONO (o pedido e a decisao de 2026-10-23), escrita a mao DE PROPOSITO: e a regua contra a qual a regra do catalogo e
	/// conferida. Uma raca que ganhar escada sai da regra sozinha -- e esta lista fica vermelha pra alguem decidir se era isso.
	/// </summary>
	private static readonly string[] AsQuatorzeDoDono =
		["Android", "Arlian", "Demigod", "Demon", "Gray", "Kai", "Kanassa", "Makyo", "Meta", "Saibaman", "Shapeshifter", "SpiritDoll", "Tsujin", "Yardrat"];

	/// <summary>
	/// OS PONTOS DA RAMPA E O QUE SE ESPERA EM CADA UM, calculados FORA do jogo (linear no logaritmo do BP entre as ancoras 1 mi -> 1x,
	/// 1,5 mi -> 2x, 583,3 mi -> 4x, 2 bi -> 16x) e escritos aqui como numero -- uma bancada que chamasse a `Ascensao.Rampa` pra saber o
	/// esperado concordaria consigo mesma. A porta do SSJ2 entra pela constante: 583.000.000 redondos dariam 3,9999.
	/// </summary>
	private static readonly (double Bp, double Mult)[] PontosDaRampa =
	[
		(1_000_000, 1),
		(1_500_000, 2),
		(100_000_000, 3.408519),
		(Catalogo.PortaSsj2, 4),
		(1_000_000_000, 9.249354),
		(2_000_000_000, 16),
		(10_000_000_000, 16),
	];

	/// <summary>
	/// O PALCO DO ROBO `--diagascensao` (`Client/RoboDaAscensao.cs`): CRAVA o BP base do corpo e deixa o resto com o jogo -- o tique
	/// de fichas de producao poe o `BPBoost` (`AfirmarAAscensao`) e a ficha lenta o leva ao cliente. Nada daqui e chamado em jogo.
	/// </summary>
	internal bool CravarBpDaAscensaoDeTeste(int id, double bp)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.Ficha.BP = bp;
		pl.Ficha.KO = false;
		pl.Ficha.Statify();
		return true;
	}

	/// <summary>A verdade do servidor pro robo: a raca e a classe do corpo e o `BPBoost` dele agora, ou nulo se ele nao existe.</summary>
	internal (string Raca, string Classe, double BPBoost)? VerdadeDaAscensaoDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? (pl.Race, pl.Ficha.Class, pl.Ficha.BPBoost) : null;

	private void AfirmarAsc(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ascOk++; GD.Print($"[ascensao]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _ascFalhou++; GD.PrintErr($"[ascensao]   FALHA {nome}   [{detalhe}]"); }
	}

	private void ApagarContasDaAscensao()
	{
		if (_store == null) return;
		foreach (string conta in ContasDaAscensao)
			try
			{
				string caminho = System.IO.Path.Combine(_store.Pasta, conta + ".json");
				if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
			}
			catch (Exception e) { GD.PushWarning($"[ascensao] nao apaguei '{conta}': {e.Message}"); }
	}

	public void RodarBancadaDaAscensao(int porta)
	{
		if (_ascensaoJaRodou) return;
		_ascensaoJaRodou = true;
		_ascOk = _ascFalhou = 0;
		GD.Print("[ascensao] ================ A ASCENSAO DAS RACAS SEM TRANSFORMACAO (dono, 2026-10-23) ================");

		ApagarContasDaAscensao();
		var c = new ClienteCruDeTeste[ContasDaAscensao.Length];
		for (int i = 0; i < c.Length; i++) c[i] = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, c);
		double topoAntes = GainKnobs.TopBP;
		var forjados = new List<ServerPlayer>();
		try
		{
			// ------------------------------------------------------------ as pecas
			CharacterDraft Ficha(string nome, string raca)
			{
				var f = new CharacterDraft
				{
					Name = nome, Race = raca, Planet = SorteioDeRaca.PlanetaDaRaca(raca), Gender = "Male",
					ChosenClass = CharacterDraft.EscolhasDeClasse(raca) is { Length: > 0 } linhas ? linhas[0] : "",
					Porte = "Medium", Backstory = "Personagem da bancada da Ascensao, criado por um cliente cru.",
				};
				f.Age = Math.Min(18, f.IdadeMaxima);
				return f;
			}
			ServerPlayer? Entrar(int i)
			{
				if (!mesa.Entra(c[i]) || !mesa.Loga(c[i], ContasDaAscensao[i])) return null;
				c[i].CriarPersonagem(0, Ficha("Asc" + RacasDaAscensao[i], RacasDaAscensao[i]));
				return mesa.Bombeia(() => c[i].NoMundo || c[i].Recusa != null, 8000) && c[i].NoMundo ? mesa.CorpoDe(ContasDaAscensao[i]) : null;
			}
			// O BP BASE NOVO E O TIQUE DE FICHAS DE PRODUCAO (o `Treinar` no topo, o `Ficha.Tick` depois) -- a 5 Hz no jogo, uma vez aqui.
			void NoBp(IEnumerable<ServerPlayer> quem, double bp)
			{
				foreach (ServerPlayer p in quem)
				{
					p.Ficha.BP = bp;
					p.Ficha.KO = false;
					p.Ficha.Ki = p.Ficha.MaxKi;
				}
				TickFichas();
			}
			// QUANTO O `BPBoost` MULTIPLICA O PODER EXPRESSO DESTE CORPO AGORA: a mesma conta com ele em 1, e volta.
			double RazaoDoExpresso(ServerPlayer p)
			{
				double com = p.Ficha.expressedBP, guardado = p.Ficha.BPBoost;
				p.Ficha.BPBoost = 1;
				p.Ficha.PowerLevel(agoraMs: NowMs());
				double sem = p.Ficha.expressedBP;
				p.Ficha.BPBoost = guardado;
				p.Ficha.PowerLevel(agoraMs: NowMs());
				return sem > 0 ? com / sem : double.NaN;
			}
			bool Perto(double a, double b, double tol = 1e-4) => Math.Abs(a - b) <= tol * Math.Max(1, Math.Abs(b));

			// ============================================================ A) QUEM ASCENDE
			GD.Print("[ascensao] -- A) quem ascende: a regra do catalogo, raca por raca e classe por classe --");
			if (_racas == null) { AfirmarAsc("(montagem) o catalogo de racas carregou", false, "sem races.json"); return; }
			var ascendem = new SortedSet<string>(StringComparer.Ordinal);
			var classesQueDivergem = new List<string>();
			foreach (string raca in _racas.Protos.Keys.Append(Catalogo.RacaMeioSaiyajin).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				var classes = new List<string> { "" };
				if (_racas.Protos.TryGetValue(raca, out RaceProto? proto)) classes.AddRange(proto.ClassSpread.Select(x => x.Classe));
				classes.AddRange(CharacterDraft.EscolhasDeClasse(raca));
				bool semClasse = Ascensao.Ascende(raca, "");
				if (semClasse) ascendem.Add(raca);
				foreach (string classe in classes.Distinct(StringComparer.Ordinal))
					if (Ascensao.Ascende(raca, classe) != semClasse) classesQueDivergem.Add($"{raca}/{classe}");
			}
			AfirmarAsc("AS QUE ASCENDEM SAO AS 14 DO DONO, nem uma a mais nem uma a menos (o `races.json` inteiro pela regra do catalogo)",
				ascendem.SetEquals(AsQuatorzeDoDono),
				$"a regra: {string.Join(", ", ascendem)}");
			AfirmarAsc("...e a classe nao muda a resposta de raca nenhuma (Legendary, Corrupted, Golden Apple...)",
				classesQueDivergem.Count == 0, string.Join(", ", classesQueDivergem));
			AfirmarAsc("O MAJIN FICA DE FORA nas duas classes (o Super Majin e a saga sao dele) -- o Corrupted nem tem escada no catalogo (a Forma Pura e da saga)",
				!Ascensao.Ascende("Majin", "") && !Ascensao.Ascende("Majin", "Majin") && !Ascensao.Ascende("Majin", "Corrupted Majin"));
			AfirmarAsc("...e quem tem escada racial nao ascende: o Saiyajin, o meio-Saiyajin, o Humano (a Tensao), o Namekuseijin, o Frost Demon, o bio",
				new[] { "Saiyan", Catalogo.RacaMeioSaiyajin, Catalogo.RacaHumano, Catalogo.RacaNamekuseijin, FormasDeFrost.Raca, BioAndroids.Raca,
						Catalogo.RacaHeran, Catalogo.RacaAlien }.All(r => !Ascensao.Ascende(r, "")));
			// O CONTRA-EXEMPLO PERGUNTA PELO CORRUPTED, e so por ele: e a classe que nunca tem escada no catalogo (a Forma Pura e posta pela
			// saga). O Majin comum ganha a linha do Super Majin (agente `supermajin`), e com ela a regra do catalogo ja o tiraria sozinha --
			// perguntar por ele aqui deixaria o contra-exemplo vermelho no dia em que a linha entrasse, sem nada errado no jogo.
			Ascensao.MajinAscendeDeTeste = true;
			try
			{
				AfirmarAsc("CONTRA-EXEMPLO (defeito injetado: o Majin deixa de ficar de fora): o Corrupted, sem escada no catalogo, cai na regra -- a regua do Majin REPROVA",
					Ascensao.Ascende("Majin", "Corrupted Majin"));
			}
			finally { Ascensao.MajinAscendeDeTeste = false; }
			Ascensao.EscadaNaoContaDeTeste = true;
			try
			{
				AfirmarAsc("CONTRA-EXEMPLO (defeito injetado: a escada racial deixa de contar): o Saiyajin e o Namekuseijin ascendem -- a regua REPROVA",
					Ascensao.Ascende("Saiyan", "") && Ascensao.Ascende(Catalogo.RacaNamekuseijin, ""));
			}
			finally { Ascensao.EscadaNaoContaDeTeste = false; }

			// ============================================================ B) A RAMPA
			GD.Print("[ascensao] -- B) a rampa: as portas do Saiyajin, o meio, e nenhum degrau --");
			AfirmarAsc("OS NUMEROS DO DONO NAS PORTAS, lidos do catalogo do Saiyajin: 2x na do SSJ1, 4x na do SSJ2, 16x na do SSJ3",
				Ascensao.NaPortaDoSsj1 == 2 && Ascensao.NaPortaDoSsj2 == 4 && Ascensao.NaPortaDoSsj3 == 16,
				$"{Ascensao.NaPortaDoSsj1} / {Ascensao.NaPortaDoSsj2} / {Ascensao.NaPortaDoSsj3}");
			AfirmarAsc("...e as portas sao as do Saiyajin (1,5 mi; 3,5 bi / 6; 20 bi / 10), com a rampa comecando em 1 mi (`ascensioncontrols.dm:86`)",
				Ascensao.Ancoras.Length == 4 && Ascensao.Ancoras[0].Bp == 1_000_000 && Ascensao.Ancoras[1].Bp == 1_500_000
				&& Perto(Ascensao.Ancoras[2].Bp, 3.5e9 / 6, 1e-12) && Ascensao.Ancoras[3].Bp == 2e9,
				string.Join(" | ", Ascensao.Ancoras.Select(a => $"{a.Bp:N0} -> {a.Mult}")));
			foreach ((double bp, double mult) in PontosDaRampa)
				AfirmarAsc($"a rampa em {bp:N0} de BP base da x{mult:0.######}", Perto(Ascensao.Rampa(bp), mult, 1e-6), $"x{Ascensao.Rampa(bp):0.######}");
			AfirmarAsc("abaixo de 1 milhao nao ha Ascensao nenhuma (1x), e o BP zero ou invalido tambem nao",
				Ascensao.Rampa(999_999) == 1 && Ascensao.Rampa(5_000) == 1 && Ascensao.Rampa(0) == 1 && Ascensao.Rampa(double.NaN) == 1);

			// O SALTO E A DESCIDA, numa varredura de 2.000 pontos igualmente espacados no LOGARITMO, de mil a cem bilhoes. A inclinacao
			// mais forte da rampa e a do trecho do SSJ2 ao SSJ3 (12x em ln(3,43) = 1,23): 9,7x por unidade de log. Cada passo da
			// varredura anda ln(10^8)/2000 = 0,0092 -- no maximo 0,09x. Um degrau seco em qualquer porta passa disso de longe.
			(double maiorSalto, double emBp, bool desceu) Varrer()
			{
				const int n = 2000;
				double a = Math.Log(1e3), b = Math.Log(1e11), antes = Ascensao.Rampa(Math.Exp(a)), maior = 0, onde = 0;
				bool desce = false;
				for (int k = 1; k <= n; k++)
				{
					double bp = Math.Exp(a + (b - a) * k / n), agora = Ascensao.Rampa(bp);
					if (agora < antes - 1e-12) desce = true;
					if (agora - antes > maior) { maior = agora - antes; onde = bp; }
					antes = agora;
				}
				return (maior, onde, desce);
			}
			var v = Varrer();
			AfirmarAsc("SEM DEGRAU SECO: de mil a cem bilhoes de BP, nenhum passo de 1/2000 do caminho (no log) sobe mais que 0,1x, e nada desce",
				v.maiorSalto < 0.1 && !v.desceu, $"maior passo x{v.maiorSalto:0.####} em {v.emBp:N0}; desceu {v.desceu}");
			bool colado = Ascensao.Ancoras.All(a => Math.Abs(Ascensao.Rampa(a.Bp * (1 + 1e-9)) - Ascensao.Rampa(a.Bp * (1 - 1e-9))) < 1e-6);
			AfirmarAsc("...e de cada lado de cada porta, a um bilionesimo dela, o bonus e o mesmo", colado);
			Ascensao.DegrauSecoDeTeste = true;
			try
			{
				var vd = Varrer();
				AfirmarAsc("CONTRA-EXEMPLO (defeito injetado: a rampa vira degrau seco): o bonus pula na porta -- a regua do salto REPROVA",
					vd.maiorSalto >= 0.1, $"maior passo x{vd.maiorSalto:0.####} em {vd.emBp:N0}");
			}
			finally { Ascensao.DegrauSecoDeTeste = false; }

			// ============================================================ C) OS SEIS JOGADORES
			GD.Print("[ascensao] -- C) os seis jogadores pela porta, pelo tique de fichas de producao --");
			var corpos = new ServerPlayer[ContasDaAscensao.Length];
			for (int i = 0; i < corpos.Length; i++)
			{
				ServerPlayer? p = Entrar(i);
				AfirmarAsc($"(montagem) o {RacasDaAscensao[i]} entra pela porta", p != null && p.Race == RacasDaAscensao[i],
					$"{c[i].Recusa} {(p == null ? "" : p.Race)}");
				if (p == null) return;
				corpos[i] = p;
			}
			AfirmarAsc("(montagem) os seis sao JOGADORES (dono na tela) -- o Kai, o Saibaman e o Tsujin ascendem pela regra, os outros tres nao",
				corpos.All(EhJogador) && corpos.Take(3).All(p => Ascensao.Ascende(p.Race, p.Ficha.Class))
				&& corpos.Skip(3).All(p => !Ascensao.Ascende(p.Race, p.Ficha.Class)),
				string.Join(", ", corpos.Select(p => $"{p.Race}/{p.Ficha.Class}")));

			foreach ((double bp, double mult) in PontosDaRampa)
			{
				NoBp(corpos, bp);
				for (int i = 0; i < corpos.Length; i++)
				{
					ServerPlayer p = corpos[i];
					double esperado = i < 3 ? mult : 1, razao = RazaoDoExpresso(p);
					AfirmarAsc($"{p.Race} com {bp:N0} de BP base: BPBoost x{esperado:0.####}, e o poder expresso multiplicado por ele",
						Perto(p.Ficha.BPBoost, esperado, 1e-6) && Perto(razao, esperado, 2e-3),
						$"BPBoost {p.Ficha.BPBoost:0.######}, expresso x{razao:0.####} ({p.Ficha.expressedBP:N0})");
				}
			}

			// ============================================================ D) O NPC NAO ASCENDE
			GD.Print("[ascensao] -- D) o NPC fica como esta --");
			ServerPlayer kai = corpos[0];
			var fichaDoNpc = new Fighter
			{
				Name = "bancada: o Kai sem dono", Race = "Kai", BP = 2e9, physoff = 5, physdef = 5, technique = 5, kioff = 5, kidef = 5,
				kiskill = 5, speed = 5, magiskill = 5, Idade = 300, maxstamina = 100, stamina = 100,
			};
			fichaDoNpc.Statify();
			fichaDoNpc.Ki = fichaDoNpc.MaxKi;
			var npc = new ServerPlayer
			{
				Id = _nextId++, Peer = null, Name = fichaDoNpc.Name, Zone = kai.Zone, Pos = kai.Pos + new Vec2(3 * ZoneCollision.TileSize, 0),
				Race = "Kai", Class = "", Genero = "Male", Idade = 300, Conta = "", LastInputMs = NowMs(), Ficha = fichaDoNpc,
				Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			PorNoMundo(npc);
			forjados.Add(npc);
			TickFichas();
			AplicarForma(npc);
			AfirmarAsc("UM KAI SEM DONO DE 2 BILHOES NAO ASCENDE: o tique de fichas e o `AplicarForma` passam por ele e o BPBoost fica em 1",
				!EhJogador(npc) && npc.Ficha.BPBoost == 1, $"BPBoost {npc.Ficha.BPBoost}");
			AscensaoProNpcDeTeste = true;
			try
			{
				TickFichas();
				AfirmarAsc("CONTRA-EXEMPLO (defeito injetado: o NPC tambem ascende): ele vai a 16x -- a regua do NPC REPROVA",
					npc.Ficha.BPBoost == 16, $"BPBoost {npc.Ficha.BPBoost}");
			}
			finally { AscensaoProNpcDeTeste = false; npc.Ficha.BPBoost = 1; }

			// ============================================================ E) A FORMA ABSOLUTA
			GD.Print("[ascensao] -- E) a forma absoluta toma o lugar da Ascensao --");
			NoBp([kai], 2e9);
			double expressoNaBase = kai.Ficha.expressedBP;
			AfirmarAsc("(montagem) o Kai de 2 bilhoes esta na base, com os 16x", kai.Forma.NaBase && kai.Ficha.BPBoost == 16, $"{kai.Forma.Atual} x{kai.Ficha.BPBoost}");
			kai.Forma.Entrar(Catalogo.IdMistico);
			AplicarForma(kai);
			AfirmarAsc("NO MISTICO (16x, absoluta) O BONUS SAI NO MESMO INSTANTE DA TROCA -- o `AplicarForma`, sem esperar tique: BPBoost 1",
				kai.Ficha.BPBoost == 1 && Perto(kai.Ficha.ssjBuff, 16), $"BPBoost {kai.Ficha.BPBoost}, forma x{kai.Ficha.ssjBuff}");
			AfirmarAsc("...e o poder expresso no Mistico e o da base com os 16x (o Mistico substitui, como substitui a escada do Saiyajin)",
				Perto(kai.Ficha.expressedBP, expressoNaBase, 2e-3), $"{kai.Ficha.expressedBP:N0} contra {expressoNaBase:N0} na base");
			TickFichas();
			AfirmarAsc("...e o tique de fichas, dentro da forma, nao a devolve", kai.Ficha.BPBoost == 1, $"BPBoost {kai.Ficha.BPBoost}");
			Ascensao.FormaAbsolutaSomaDeTeste = true;
			try
			{
				AplicarForma(kai);
				AfirmarAsc("CONTRA-EXEMPLO (defeito injetado: a forma absoluta SOMA com a Ascensao): 16 x 16 no Mistico -- a regua REPROVA",
					kai.Ficha.BPBoost == 16 && kai.Ficha.expressedBP > expressoNaBase * 8, $"BPBoost {kai.Ficha.BPBoost}, {kai.Ficha.expressedBP:N0}");
			}
			finally { Ascensao.FormaAbsolutaSomaDeTeste = false; }
			kai.Forma.Entrar(Catalogo.IdBase);
			AplicarForma(kai);
			AfirmarAsc("DE VOLTA A BASE, os 16x voltam no mesmo instante", kai.Ficha.BPBoost == 16 && Perto(kai.Ficha.expressedBP, expressoNaBase, 2e-3),
				$"BPBoost {kai.Ficha.BPBoost}, {kai.Ficha.expressedBP:N0}");

			// ============================================================ F) OS MARCOS DE GANHO
			GD.Print("[ascensao] -- F) os marcos de ganho nas portas da rampa --");
			ServerPlayer tsujin = corpos[2];
			ServerPlayer saiyan = corpos[3];
			void SemMarcos(ServerPlayer p) { p.Ficha.bp_milestones_done.Clear(); p.Ficha.bp_milestone_mult = 1; }
			(double Mult, List<string> Ditos) Marco(ServerPlayer p, double bp)
			{
				List<string> ditos = Ouvir(() => NoBp([p], bp));
				return (p.Ficha.bp_milestone_mult, ditos.Where(x => x.Contains("MARCO DE PODER", StringComparison.Ordinal)).ToList());
			}
			SemMarcos(tsujin);
			SemMarcos(saiyan);
			var m0 = Marco(tsujin, 1_400_000);
			AfirmarAsc("abaixo da porta do SSJ1 (1,4 mi, BPBoost ~1,8) o Tsujin nao rompe marco nenhum", m0.Mult == 1 && m0.Ditos.Count == 0, $"x{m0.Mult}");
			var m1 = Marco(tsujin, Catalogo.PortaSsj1);
			AfirmarAsc("NA PORTA DO SSJ1 (BPBoost 2) o marco `asc5`: todo ganho x2, e o jogador ouve o MARCO DE PODER",
				m1.Mult == 2 && m1.Ditos.Count == 1 && tsujin.Ficha.bp_milestones_done.Contains("asc5"), $"x{m1.Mult} | {string.Join(" | ", m1.Ditos)}");
			var m2 = Marco(tsujin, Catalogo.PortaSsj2);
			AfirmarAsc("NA PORTA DO SSJ2 (BPBoost 4) o `asc10`: x4", m2.Mult == 4 && tsujin.Ficha.bp_milestones_done.Contains("asc10"), $"x{m2.Mult}");
			var m3 = Marco(tsujin, Catalogo.PortaSsj3);
			AfirmarAsc("NA PORTA DO SSJ3 (BPBoost 16) o `asc20`: x8 -- os marcos do Saiyajin nas mesmas portas (`ssj` x2, `ssj2` x4, `ssj3` x8)",
				m3.Mult == 8 && tsujin.Ficha.bp_milestones_done.Contains("asc20"), $"x{m3.Mult}");
			var ms = Marco(saiyan, Catalogo.PortaSsj3);
			AfirmarAsc("...e o Saiyajin de 2 bilhoes na base NAO ganha marco de Ascensao (o dele vem das formas)",
				ms.Mult == 1 && !saiyan.Ficha.bp_milestones_done.Any(t => t.StartsWith("asc", StringComparison.Ordinal)), $"x{ms.Mult}");
			SemMarcos(tsujin);
			Ascensao.MarcosNoDegrauDoDmDeTeste = true;
			try
			{
				NoBp([tsujin], 1_000);
				var md = Marco(tsujin, Catalogo.PortaSsj1);
				AfirmarAsc("CONTRA-EXEMPLO (defeito injetado: os marcos nos 5/10/20 do DM): na porta do SSJ1 nao vem marco nenhum -- a regua REPROVA",
					md.Mult == 1 && md.Ditos.Count == 0, $"x{md.Mult}");
			}
			finally { Ascensao.MarcosNoDegrauDoDmDeTeste = false; SemMarcos(tsujin); }

			// ============================================================ G) O FIO
			GD.Print("[ascensao] -- G) a ficha lenta leva o bonus --");
			NoBp(corpos, 1_000_000_000);
			for (int i = 0; i < corpos.Length; i++)
			{
				corpos[i].SigAtributos = "";
				MandarAtributos(corpos[i]);
			}
			bool chegou = mesa.Bombeia(() => Enumerable.Range(0, c.Length)
				.All(i => c[i].UltimaFicha is { } f && Math.Abs(f.Ascensao - (float)corpos[i].Ficha.BPBoost) < 1e-4), 3000);
			AfirmarAsc("A FICHA LENTA LEVA O BONUS: com 1 bilhao de BP, o Kai, o Saibaman e o Tsujin leem x9,25 no fio (o leitor de producao) e os outros x1",
				chegou && Enumerable.Range(0, 3).All(i => Math.Abs((c[i].UltimaFicha?.Ascensao ?? 0) - 9.249354f) < 1e-3)
				&& Enumerable.Range(3, 3).All(i => c[i].UltimaFicha?.Ascensao == 1f),
				string.Join(" | ", Enumerable.Range(0, c.Length).Select(i => $"{RacasDaAscensao[i]} x{c[i].UltimaFicha?.Ascensao:0.####}")));

			AAscensaoDivina(corpos[2], corpos[3], NoBp, RazaoDoExpresso, Perto);   // (2026-10-30, o pedido 183)
			OsMarcosDaAscensaoDivina(corpos[2], corpos[3], NoBp);   // (2026-10-30, o pedido 184)
		}
		catch (Exception e)
		{
			AfirmarAsc("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			Ascensao.DegrauSecoDeTeste = Ascensao.MajinAscendeDeTeste = Ascensao.EscadaNaoContaDeTeste = false;
			Ascensao.FormaAbsolutaSomaDeTeste = Ascensao.MarcosNoDegrauDoDmDeTeste = false;
			AscensaoProNpcDeTeste = false;
			foreach (ServerPlayer f in forjados) RemoverNpc(f);
			GainKnobs.TopBP = topoAntes;   // (o tique de fichas o sobe com o BP dos jogadores, e ele so SOBE: devolvido a mao)
			mesa.Fecha();
			ApagarContasDaAscensao();
			GD.Print($"[ascensao] ================ {_ascOk} OK, {_ascFalhou} FALHA(S) ================");
		}
	}

	// ============================================================ H) (2026-10-30, o pedido 183) A ASCENSAO DIVINA
	/// <summary>
	/// O PEDIDO 183, em tres mensagens do dono: *"ao pegarem god ki a ascensão delas sobe pra 22x (ssj god) e conforme vai subindo a maestria
	/// do god ki (a qual pra elas deve subir igual uma pessoa transformada) ai atingirem o god ki pra ter o blue, a ascensão sobe pro nivel
	/// blue [...] E com isso eles podem aprender ultra instinto ou ultra ego como raças q transformam"*; *"faça o limite do ascension ser nivel
	/// blue"*; *"limite o kaioken pra racas de ascension pra 20x em todas os niveis de ascension"*. O Tsujin (que ascende) e o Saiyajin (que
	/// nao), os dois jogadores da secao C, pelos funis de producao: o despertar (`DespertarOGodKi`), o tique de fichas (`NoBp`), o tique do
	/// ki divino (`TickDoGodKi`), o ensino (`PodeAprender`) e o botao do Kaio-ken.
	/// </summary>
	private void AAscensaoDivina(ServerPlayer tsu, ServerPlayer sai, Action<IEnumerable<ServerPlayer>, double> NoBp,
								 Func<ServerPlayer, double> RazaoDoExpresso, Func<double, double, double, bool> Perto)
	{
		GD.Print("[ascensao] -- H) (o pedido 183) a Ascensao divina: o despertar, o Blue, o teto, a maestria pelo tique, o ensino e o Kaio-ken x20 --");
		GodKiState? kiDoTsujin = tsu.Ficha.godki, kiDoSaiyan = sai.Ficha.godki;
		string classeDoTsujin = tsu.Ficha.Class;
		void KiDivino(ServerPlayer p, double maestria) => p.Ficha.godki = new GodKiState { awakened = true, mastery = maestria };
		void UmaHoraDeKiDivino() { for (int i = 0; i < 60; i++) TickDoGodKi(60); }
		try
		{
			// H1. O DESPERTAR, pelo funil de producao: o nivel do SSG com qualquer BP
			tsu.Ficha.godki = null;
			sai.Ficha.godki = null;
			if (tsu.Combate != null) tsu.Combate.EmCombate = 0;
			NoBp([tsu, sai], 1_000);
			double semKi = tsu.Ficha.BPBoost;
			List<string> ditos = Ouvir(() => DespertarOGodKi(tsu, FonteDaExposicao.Admin));
			KiDivino(sai, 0);
			NoBp([tsu, sai], 1_000);
			AfirmarAsc("(o pedido 183) **COM O KI DIVINO A ASCENSAO VAI AO NIVEL DO SSG**: o Tsujin de mil de BP, x1 sem ki divino, vai a x22 ao "
				+ "desperta-lo -- e o Saiyajin na base, com o ki divino, fica em x1",
				semKi == 1 && Perto(tsu.Ficha.BPBoost, 22, 1e-9) && sai.Ficha.BPBoost == 1,
				$"Tsujin x{semKi} -> x{tsu.Ficha.BPBoost}; Saiyajin x{sai.Ficha.BPBoost}");
			AfirmarAsc("(o pedido 183) **O DESPERTAR AVISA QUE A ASCENSAO SUBIU AO NIVEL DO SUPER SAIYAJIN GOD**",
				ditos.Any(x => x.Contains("Ascensão sobe ao nível do Super Saiyajin God", StringComparison.Ordinal)), string.Join(" | ", ditos));
			NoBp([tsu], 10_000_000_000);
			AfirmarAsc("...e com 10 bilhoes (a rampa mortal no teto, 16x) tambem: o maior dos dois, x22", Perto(tsu.Ficha.BPBoost, 22, 1e-9),
				$"x{tsu.Ficha.BPBoost}");

			// H2/H3. O BLUE E O TETO
			KiDivino(tsu, 32.9);
			NoBp([tsu], 1_000);
			double antesDoBlue = tsu.Ficha.BPBoost;
			KiDivino(tsu, 33);
			NoBp([tsu], 1_000);
			double noBlue = tsu.Ficha.BPBoost, expresso = RazaoDoExpresso(tsu);
			AfirmarAsc("(o pedido 183) **NA MAESTRIA DO BLUE (33%) A ASCENSAO VAI AO NIVEL DO BLUE**: x22 com 32,9%, x32 com 33% -- e o poder "
				+ "expresso multiplicado por ele",
				Perto(antesDoBlue, 22, 1e-9) && Perto(noBlue, 32, 1e-9) && Perto(expresso, 32, 2e-3), $"x{antesDoBlue} -> x{noBlue}, expresso x{expresso:0.###}");
			KiDivino(tsu, 50);
			NoBp([tsu], 1_000);
			double em50 = tsu.Ficha.BPBoost;
			KiDivino(tsu, 100);
			NoBp([tsu], 1_000);
			double em100 = tsu.Ficha.BPBoost;
			AfirmarAsc("(o pedido 183) **O TETO E O BLUE**: com 50% (o ki do Blue Evolution) e com 100% a Ascensao continua em x32",
				Perto(em50, 32, 1e-9) && Perto(em100, 32, 1e-9), $"50% x{em50}; 100% x{em100}");

			// H4. A MAESTRIA SOBE COMO A DE QUEM ESTA TRANSFORMADO
			KiDivino(tsu, 0);
			KiDivino(sai, 0);
			UmaHoraDeKiDivino();
			double hora = 100 / DespertarDivino.HorasDeUsoPraMaestriaCheia;
			AfirmarAsc($"(o pedido 183) **A MAESTRIA DIVINA DA ASCENSAO SOBE COMO A DE QUEM ESTA TRANSFORMADO**: uma hora na base e o Tsujin ganha "
				+ $"{hora:0.###}% (o que o SSG da); o Saiyajin na base continua em 0",
				Perto(tsu.Ficha.godki!.mastery, hora, 1e-6) && sai.Ficha.godki!.mastery == 0,
				$"Tsujin {tsu.Ficha.godki?.mastery:0.####}%; Saiyajin {sai.Ficha.godki?.mastery:0.####}%");

			// H5. OS 33% PELO TIQUE
			KiDivino(tsu, 32.9);
			ditos = Ouvir(UmaHoraDeKiDivino);
			NoBp([tsu], 1_000);
			AfirmarAsc("(o pedido 183) **OS 33% PELO TIQUE LEVAM A ASCENSAO AO BLUE**: o Tsujin cruza a maestria do Blue so com o ki divino aceso, e vai a x32",
				tsu.Ficha.godki!.mastery >= 33 && Perto(tsu.Ficha.BPBoost, 32, 1e-9), $"{tsu.Ficha.godki.mastery:0.##}% x{tsu.Ficha.BPBoost}");
			AfirmarAsc("(o pedido 183) **E O AVISO DOS 33% DIZ QUE A ASCENSAO SUBIU AO NIVEL DO BLUE** (e nao 'o degrau seguinte das formas divinas')",
				ditos.Any(x => x.Contains("Ascensão sobe ao nível do Blue", StringComparison.Ordinal)), string.Join(" | ", ditos));

			// H6. OS 50% -- na classe Normal, que ouviria a promessa do Kaio-ken por cima do Blue
			tsu.Ficha.Class = "Normal";
			KiDivino(tsu, 49.9);
			ditos = Ouvir(UmaHoraDeKiDivino);
			// (2026-10-30, o pedido 184) os 50% agora pagam o marco do Blue Kaio-ken a quem ascende (a familia I) -- o que esta regua barra e a
			// PROMESSA do Saiyajin ("aguenta o Kaio-ken por cima do Blue"), e nao toda frase com "Kaio-ken"
			AfirmarAsc("(o pedido 183) **OS 50% NAO PROMETEM O KAIO-KEN POR CIMA DO BLUE A QUEM ASCENDE** (a classe Normal ouviria): o aviso e o da maestria",
				tsu.Ficha.godki!.mastery >= 50 && ditos.Any(x => x.Contains("amadurece (50%)", StringComparison.Ordinal))
				&& !ditos.Any(x => x.Contains("por cima do Blue", StringComparison.Ordinal)),
				string.Join(" | ", ditos));

			// H7. OS 70% E O ENSINO
			KiDivino(tsu, 69.9);
			RecusaDeDisciplina antes70 = PodeAprender(tsu, TipoDeDisciplina.UltraInstinct);
			ditos = Ouvir(UmaHoraDeKiDivino);
			RecusaDeDisciplina depoisUi = PodeAprender(tsu, TipoDeDisciplina.UltraInstinct), depoisUe = PodeAprender(tsu, TipoDeDisciplina.PoderDaDestruicao);
			AfirmarAsc("(o pedido 183) **E COM ISSO ELA APRENDE O ULTRA INSTINTO E O ULTRA EGO**: o Tsujin cruza os 70% pelo tique, ouve dos segredos dos "
				+ "Anjos, e o ensino das duas deixa de recusar pelo ki divino",
				antes70 == RecusaDeDisciplina.SemGodKi && depoisUi == RecusaDeDisciplina.Pode && depoisUe == RecusaDeDisciplina.Pode
				&& ditos.Any(x => x.Contains("Ultra Instinct", StringComparison.Ordinal)),
				$"{antes70} -> UI {depoisUi}, UE {depoisUe} | {string.Join(" | ", ditos)}");

			// H8. O KAIO-KEN ATE x20 EM TODO NIVEL
			(bool Aceso, double Vezes, List<string> Falas) Kaio(ServerPlayer p, double pedido)
			{
				List<string> falas = Ouvir(() => Kaioken(p, pedido));
				bool aceso = TemBuff(p, BuffKaiokenG2);
				double vezes = p.Ficha.KaiokenVezes;
				if (aceso) Kaioken(p, 0);   // o mesmo botao apaga
				return (aceso, vezes, falas);
			}
			tsu.Ficha.godki = null;
			NoBp([tsu, sai], 1_000);
			var t1 = Kaio(tsu, 50);
			var s1 = Kaio(sai, 50);
			AfirmarAsc("(o pedido 183) **O KAIO-KEN DE QUEM ASCENDE VAI ATE x20 EM TODO NIVEL**: o Tsujin em x1 (mil de BP, sem ki divino) pede x50, "
				+ "acende x20 e ouve o teto; o Saiyajin na base acende os x50",
				t1.Aceso && t1.Vezes == 20 && t1.Falas.Any(x => x.Contains("vai até x20", StringComparison.Ordinal)) && s1.Aceso && s1.Vezes == 50,
				$"Tsujin {t1.Aceso}/x{t1.Vezes} | {string.Join(" | ", t1.Falas)}; Saiyajin {s1.Aceso}/x{s1.Vezes}");
			KiDivino(tsu, 100);
			NoBp([tsu], 1_000);
			var t2 = Kaio(tsu, 100);
			AfirmarAsc("...e no nivel do Blue (x32) tambem: x100 vira x20 -- o Blue com o Kaio-ken x20 do Saiyajin",
				Perto(tsu.Ficha.BPBoost, 32, 1e-9) && t2.Aceso && t2.Vezes == 20, $"x{tsu.Ficha.BPBoost}; {t2.Aceso}/x{t2.Vezes}");
		}
		finally
		{
			tsu.Ficha.godki = kiDoTsujin;
			sai.Ficha.godki = kiDoSaiyan;
			tsu.Ficha.Class = classeDoTsujin;
		}
	}

	// ============================================================ I) (2026-10-30, o pedido 184) OS MARCOS DA ASCENSAO DIVINA
	/// <summary>
	/// O PEDIDO 184 na Ascensao (*"todas as formas e ascenscoes desses niveis tb"*): no nivel do SSG (22x) o marco do ki divino (x16), no do Blue
	/// (32x) o do Blue (x32), e aos 50% de maestria divina o do Blue Kaio-ken (x64; os 2^x do pedido 188) -- a Ascensao no Blue com o Kaio-ken x20 e o mesmo 56x. O
	/// Tsujin sem marco nenhum e sem o `godki` do despertar (o ki divino posto direto), pra so a Ascensao pagar; o Saiyajin na base, com o ki
	/// divino, e o controle.
	/// </summary>
	private void OsMarcosDaAscensaoDivina(ServerPlayer tsu, ServerPlayer sai, Action<IEnumerable<ServerPlayer>, double> NoBp)
	{
		GD.Print("[ascensao] -- I) (o pedido 184) os marcos da Ascensao divina: x16 no SSG, x32 no Blue, x64 aos 50% --");
		GodKiState? kiDoTsujin = tsu.Ficha.godki, kiDoSaiyan = sai.Ficha.godki;
		void SemMarcos(ServerPlayer p) { p.Ficha.bp_milestones_done.Clear(); p.Ficha.bp_milestone_mult = 1; }
		try
		{
			SemMarcos(tsu);
			SemMarcos(sai);
			tsu.Ficha.godki = new GodKiState { awakened = true, mastery = 0 };
			sai.Ficha.godki = new GodKiState { awakened = true, mastery = 33 };
			NoBp([tsu, sai], 1_000);
			AfirmarAsc("(o pedido 184) **A ASCENSAO NO NIVEL DO SSG ROMPE O MARCO DO KI DIVINO**: o Tsujin a x22 ganha o `asc_deus`, x16 -- e o Saiyajin na base, nada",
				tsu.Ficha.bp_milestones_done.Contains("asc_deus") && tsu.Ficha.bp_milestone_mult == 16 && sai.Ficha.bp_milestones_done.Count == 0,
				$"Tsujin {string.Join(",", tsu.Ficha.bp_milestones_done)} x{tsu.Ficha.bp_milestone_mult}; Saiyajin {string.Join(",", sai.Ficha.bp_milestones_done)}");
			tsu.Ficha.godki = new GodKiState { awakened = true, mastery = 33 };
			NoBp([tsu], 1_000);
			AfirmarAsc("(o pedido 184) **A ASCENSAO NO NIVEL DO BLUE ROMPE O MARCO DO BLUE**: a x32 o `asc_blue`, x32",
				tsu.Ficha.bp_milestones_done.Contains("asc_blue") && tsu.Ficha.bp_milestone_mult == 32,
				$"{string.Join(",", tsu.Ficha.bp_milestones_done)} x{tsu.Ficha.bp_milestone_mult}");
			tsu.Ficha.godki = new GodKiState { awakened = true, mastery = 49.9 };
			List<string> ditos = Ouvir(() => { for (int i = 0; i < 60; i++) TickDoGodKi(60); });
			AfirmarAsc("(o pedido 184) **E AOS 50% A ASCENSAO GANHA O DO BLUE KAIO-KEN**: o Tsujin cruza os 50% pelo tique e vai a x64, com o aviso",
				tsu.Ficha.godki!.mastery >= 50 && tsu.Ficha.bp_milestones_done.Contains("blue_kaioken") && tsu.Ficha.bp_milestone_mult == 64
				&& ditos.Any(x => x.Contains("Blue com Kaio-ken", StringComparison.Ordinal)),
				$"{tsu.Ficha.godki?.mastery:0.##}% x{tsu.Ficha.bp_milestone_mult} | {string.Join(" | ", ditos)}");
		}
		finally
		{
			tsu.Ficha.godki = kiDoTsujin;
			sai.Ficha.godki = kiDoSaiyan;
		}
	}
}
