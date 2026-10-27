using Godot;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA AGUA ULTRA DIVINA (`--aguateste`) -- o item 11 do playtest de 2026-10-10: *"Ultra Divine Water
/// no Lookout: existia no BYOND e falta aqui. Porte o comportamento original (efeito, requisitos, limite de
/// uso, riscos)."* A regra portada e o `/obj/Sacred_Water` (`EarthRanks.dm:181-231`); ver `Core/Tech/
/// AguaSagrada.cs`.
///
/// No boot, como as outras de servidor: o jarro e mobilia de MAPA, entao a primeira pergunta e se ele esta de
/// pe no Mirante. Os corpos sao forjados (sem `Peer`) e o gole entra pelo `Verbo(...)` -- a porta do
/// `C2S.Verbo`, que passa pelo despacho da tecla E. O relogio das fases e adiantado pela bancada
/// (`AdiantarAAguaDeTeste`) e quem as anda e o `TickDaAguaSagrada` de producao.
///
///   1. O JARRO ESTA NO MIRANTE, na celula do original, e responde aos dois verbos.
///   2. A REGRA: a resistencia garante; sem ela, a moeda.
///   3. LONGE DO JARRO nada acontece.
///   4. QUEM NAO AGUENTA morre no terceiro passo -- e a marca nao e gasta.
///   5. QUEM AGUENTA: as falas, o nocaute de 30 s, o vigor x1,2 e o BP pelo `capcheck`.
///   6. UMA VEZ NA VIDA: o segundo gole e so agua.
///   7. QUEM SAI NO MEIO leva o premio (o desvio declarado do DM, onde ele se perdia).
///
/// O defeito injetado (`AguaSagrada.SemGarantiaDeTeste`) tira a garantia da resistencia: o corpo resistente
/// passa a depender da moeda, e morre com ela contra.
///
///     Godot --headless -- --server --port 7998 --aguateste
/// </summary>
public partial class GameServer
{
	private int _agOk, _agFalhou;

	private void AfirmarAg(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _agOk++; GD.Print($"[agua]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _agFalhou++; GD.PrintErr($"[agua]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDaAguaSagrada()
	{
		_agOk = _agFalhou = 0;
		GD.Print("[agua] ================ A AGUA ULTRA DIVINA DO MIRANTE (playtest, 2026-10-10) ================");

		ZoneKey mirante = ZoneKey.Premade("Lookout");
		var forjados = new List<ServerPlayer>();

		ServerPlayer Forjar(string nome, Vec2 onde, double resistencia)
		{
			var c = new ServerPlayer
			{
				Id = _nextId++, Peer = null, Name = nome, Race = "Human", Genero = "Male", Idade = 25,
				Zone = mirante, Pos = onde, LastInputMs = NowMs(), Conta = "bancada_agua", Slot = forjados.Count,
				Ficha = new Fighter
				{
					Name = nome, Race = "Human", BP = 1000, Idade = 25,
					physoff = 5, physdef = 5, technique = 5, kioff = 5, kidef = 5,
					kiskill = 5, speed = 5, magiskill = 5, maxstamina = 100, stamina = 100,
				},
				Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			c.Ficha.Class = "Normal";
			PorNoMundo(c);
			c.Ficha.PowerLevel();
			c.Ficha.Ephysdef = resistencia;
			forjados.Add(c);
			return c;
		}

		// As fases de um gole, uma por uma, pelo tique de producao: adianta o relogio e tica.
		void Passar(long ms)
		{
			AdiantarAAguaDeTeste(ms);
			TickDaAguaSagrada();
		}

		try
		{
			// ---------------------------------------------------------------- 1. o jarro
			GD.Print("[agua] -- 1) o jarro no mapa --");
			Obra? jarro = _noChao.FirstOrDefault(o => o.Tipo == AguaSagrada.Tipo && o.Zona.Equals(mirante));
			AfirmarAg("**O JARRO ESTA NO MIRANTE** como mobilia de mapa (uma obra de pe, vinda do `.objetos`)", jarro is { DoMapa: true });
			if (jarro == null) return;
			(int cx, int cy) = ((int)(jarro.X / ZoneCollision.TileSize), (int)((jarro.Y + MoveRules.FeetOffsetY) / ZoneCollision.TileSize));
			AfirmarAg("...na celula do original: 142,62 do z 12 do `.dmm`, que aqui e (141, 438)", (cx, cy) == (141, 438), $"({cx}, {cy})");
			AfirmarAg("...e ele responde aos dois verbs do DM (`Drink` e `Description`) pela tecla E",
					  Interacoes.Aceita(AguaSagrada.Tipo, AguaSagrada.VerboBeber) && Interacoes.Aceita(AguaSagrada.Tipo, AguaSagrada.VerboSobre)
					  && Interacoes.Interativo(AguaSagrada.Tipo));
			var aoLado = new Vec2(jarro.X + ZoneCollision.TileSize, jarro.Y);

			// ---------------------------------------------------------------- 2. a regra
			GD.Print("[agua] -- 2) a regra: `prob(50) || Ephysdef >= 1` --");
			AfirmarAg("a RESISTENCIA garante: com 1 ou mais, aguenta com qualquer moeda", AguaSagrada.Aguenta(1, 0.999) && AguaSagrada.Aguenta(3, 0.5));
			AfirmarAg("sem ela, e a moeda: abaixo de 0,5 aguenta, de 0,5 pra cima nao",
					  AguaSagrada.Aguenta(0.9, 0.499) && !AguaSagrada.Aguenta(0.9, 0.5) && !AguaSagrada.Aguenta(0, 0.999));

			// ---------------------------------------------------------------- 3. longe
			GD.Print("[agua] -- 3) longe do jarro --");
			ServerPlayer longe = Forjar("Longe", new Vec2(jarro.X + ZoneCollision.TileSize * 12, jarro.Y), 5);
			List<string> falas = Ouvir(() => Verbo(longe, AguaSagrada.VerboBeber, ""));
			AfirmarAg("a 12 tiles o verbo e recusado: sem gole, sem marca", longe.Ficha.sacredwater == 0 && _golesDeAgua.Count == 0 && Disse(falas, "por perto"), Ultimos(falas));

			// ---------------------------------------------------------------- 4. quem nao aguenta
			GD.Print("[agua] -- 4) quem nao aguenta --");
			ServerPlayer fraco = Forjar("Fraco", aoLado, 0.5);
			SorteioDaAguaDeTeste = 0.9;   // a moeda contra
			falas = Ouvir(() => Verbo(fraco, AguaSagrada.VerboBeber, ""));
			AfirmarAg("o gole comeca (a primeira fala sai na hora)", _golesDeAgua.Count == 1 && Disse(falas, "você bebe"), Ultimos(falas));
			falas = Ouvir(() => Verbo(fraco, AguaSagrada.VerboBeber, ""));
			AfirmarAg("um gole de cada vez: beber de novo no meio nao abre um segundo", _golesDeAgua.Count == 1 && Disse(falas, "na boca"), Ultimos(falas));
			falas = Ouvir(() => { Passar(3000); Passar(3000); });
			AfirmarAg("duas falas, com 3 s entre elas, e ninguem morreu ainda", !fraco.Ficha.dead && Disse(falas, "acontecendo") && Disse(falas, "não foi nada"), Ultimos(falas));
			falas = Ouvir(() => Passar(3000));
			AfirmarAg("**SEM A RESISTENCIA E COM A MOEDA CONTRA, A AGUA MATA** (o `usr.Death()`, pela porta unica da morte)",
					  fraco.Ficha.dead && _golesDeAgua.Count == 0 && Disse(falas, "MATA"), Ultimos(falas));
			AfirmarAg("...e a marca NAO e gasta: quem morreu pode voltar e tentar de novo", fraco.Ficha.sacredwater == 0);

			// ---------------------------------------------------------------- 5. quem aguenta
			GD.Print("[agua] -- 5) quem aguenta --");
			ServerPlayer forte = Forjar("Forte", aoLado, 1);
			forte.Ficha.BP = forte.Ficha.relBPmax * 0.5;
			// (2026-10-26) O BANCO DO POTENCIAL (o pedido 95): mil guardados, que a agua solta 1x
			forte.Ficha.hiddenpotential = 1000;
			double vigorAntes = forte.Ficha.maxstamina, bpAntes = forte.Ficha.BP;
			SorteioDaAguaDeTeste = 0.9;   // a moeda contra, de novo: quem salva e a resistencia
			Ouvir(() => Verbo(forte, AguaSagrada.VerboBeber, ""));
			AfirmarAg("**COM A RESISTENCIA EM 1 A AGUA NAO MATA**, com a moeda contra -- e o gole comeca",
					  _golesDeAgua.Count == 1 && !forte.Ficha.dead);
			falas = Ouvir(() => { Passar(3000); Passar(3000); Passar(3000); });
			AfirmarAg("no terceiro passo o corpo DESABA (o `KO` do original)", forte.Ficha.KO && !forte.Ficha.dead && Disse(falas, "desaba"), Ultimos(falas));
			falas = Ouvir(() => Passar(29_000));
			AfirmarAg("...e fica 30 s no chao: aos 29 ainda nao acordou nem ganhou nada",
					  forte.Ficha.KO && forte.Ficha.maxstamina == vigorAntes && falas.Count == 0, Ultimos(falas));
			falas = Ouvir(() => Passar(1000));
			AfirmarAg("aos 30 s ele ACORDA (o `Un_KO`)", !forte.Ficha.KO && Disse(falas, "acorda"), Ultimos(falas));
			falas = Ouvir(() => Passar(3000));
			AfirmarAg("**O VIGOR MAXIMO SOBE 20%** (`maxstamina *= 1.2`)", Math.Abs(forte.Ficha.maxstamina - vigorAntes * 1.2) < 1e-6,
					  $"{vigorAntes:0.##} -> {forte.Ficha.maxstamina:0.##}");
			// (2026-10-26) O BANCO, COMO NO DU (`Skills.dm:2770-2773`; o dono, o pedido 95): solta o banco 1x e poe nele 10% do BP, minguando
			AfirmarAg("**A AGUA SOLTA O BANCO DO POTENCIAL 1x** (o dono, 2026-10-25): os mil guardados viram mil de BP base -- e nao mais um quinto do teto pessoal",
					  Math.Abs(forte.Ficha.BP - (bpAntes + 1000)) < 1e-6 && forte.Ficha.unlockPotential >= 1,
					  $"{bpAntes:0.#} -> {forte.Ficha.BP:0.#}");
			AfirmarAg("**...E POE NO BANCO 10% DO BP** (o primeiro gole: `/ (0 + 1)`), conta o gole e comeca a hora de recarga",
					  Math.Abs(forte.Ficha.hiddenpotential - forte.Ficha.BP * 0.1) < 1e-6 && forte.Ficha.golesDaAgua == 1 && forte.Ficha.sacredwater == 1
					  && forte.Ficha.liberarPotencialEm > NowMs() + Jandirus.Core.Stats.GainKnobs.RecargaDaLiberacaoMs - 60_000,
					  $"banco {forte.Ficha.hiddenpotential:0.##} (BP {forte.Ficha.BP:0.##}), goles {forte.Ficha.golesDaAgua}");

			// (2026-10-16) O DONO: *"Agua sagrada do korin mostra o bp mesmo sem scouter"*. Sem medidor o ganho se SENTE (porcentagem) e o
			// poder de luta nao e citado; o numero so com scouter (`PoderNaFrase`). O corpo da bancada nao tem scouter.
			AfirmarAg("**SEM SCOUTER A AGUA NAO FALA DO PODER DE LUTA**: so a sensacao (\"cerca de N% mais forte\"), sem numero nenhum",
					  Disse(falas, "mais forte") && !Disse(falas, "PODER DE LUTA") && !TrazOPoder(falas, forte.Ficha.BP) && !TrazOPoder(falas, bpAntes),
					  Ultimos(falas));
			AfirmarAg("...e o gole acabou", _golesDeAgua.Count == 0);

			// ---------------------------------------------------------------- 6. de hora em hora (2026-10-26; era uma vez na vida)
			GD.Print("[agua] -- 6) de hora em hora, o banco minguando --");
			double vigorDepois = forte.Ficha.maxstamina, bpDepois = forte.Ficha.BP, bancoDepois = forte.Ficha.hiddenpotential;
			falas = Ouvir(() => Verbo(forte, AguaSagrada.VerboBeber, ""));
			AfirmarAg("**DENTRO DA HORA O SEGUNDO GOLE NAO FAZ NADA** (o dono: \"repetiveis, 1h de recarga\"): o potencial \"ainda se assenta\", e nenhum gole novo comeca",
					  _golesDeAgua.Count == 0 && forte.Ficha.maxstamina == vigorDepois && forte.Ficha.BP == bpDepois && Disse(falas, "ainda se assenta"),
					  Ultimos(falas));
			AdiantoDoRelogioDeTeste += Jandirus.Core.Stats.GainKnobs.RecargaDaLiberacaoMs;
			try
			{
				SorteioDaAguaDeTeste = 0.9;
				falas = Ouvir(() => { Verbo(forte, AguaSagrada.VerboBeber, ""); Passar(3000); Passar(3000); Passar(3000); Passar(30_000); Passar(3000); });
			}
			finally { AdiantoDoRelogioDeTeste -= Jandirus.Core.Stats.GainKnobs.RecargaDaLiberacaoMs; }
			AfirmarAg("**PASSADA A HORA, BEBE DE NOVO**: o banco que a primeira deixou sai 1x, e o vigor NAO sobe outra vez (so na primeira)",
					  Math.Abs(forte.Ficha.BP - (bpDepois + bancoDepois)) < 1e-6 && forte.Ficha.maxstamina == vigorDepois,
					  $"BP {bpDepois:0.##} -> {forte.Ficha.BP:0.##} (banco {bancoDepois:0.##}); vigor {forte.Ficha.maxstamina:0.##}");
			AfirmarAg("**...E O QUE ELA POE MINGUA**: no segundo gole, 10% do BP dividido por 2 (o `sacredWaterUses + 1` do DU)",
					  Math.Abs(forte.Ficha.hiddenpotential - forte.Ficha.BP * 0.1 / 2) < 1e-6 && forte.Ficha.golesDaAgua == 2,
					  $"banco {forte.Ficha.hiddenpotential:0.##} (BP {forte.Ficha.BP:0.##}), goles {forte.Ficha.golesDaAgua}");

			// O ANDROIDE DE ABSORCAO GUARDA, MAS NAO SOLTA NA AGUA (o dono: "so libera com tech q da um upgrade")
			ServerPlayer maquina = Forjar("Maquina", aoLado, 1);
			maquina.Ficha.AndroideAbsorcao = true;
			maquina.Ficha.hiddenpotential = 1000;
			double bpDaMaquina = maquina.Ficha.BP;
			falas = Ouvir(() => Verbo(maquina, AguaSagrada.VerboBeber, ""));
			AfirmarAg("**O ANDROIDE DE ABSORCAO NAO TIRA NADA DA AGUA**: ouve que so um upgrade de tecnologia solta o banco dele, e nenhum gole comeca",
					  _golesDeAgua.Count == 0 && maquina.Ficha.BP == bpDaMaquina && maquina.Ficha.hiddenpotential == 1000 && Disse(falas, "upgrade de tecnologia"),
					  Ultimos(falas));

			falas = Ouvir(() => Verbo(forte, AguaSagrada.VerboSobre, ""));
			AfirmarAg("o `Description()` diz o que ela da e o que ela cobra -- e a hora entre um gole e outro",
					  Disse(falas, "resistência") && Disse(falas, "potencial") && Disse(falas, "uma hora"), Ultimos(falas));

			// ---------------------------------------------------------------- 6b. o medidor
			GD.Print("[agua] -- 6b) com scouter o numero; sem ele, nunca --");
			List<string> GoleInteiro(ServerPlayer quem)
			{
				quem.Ficha.BP = quem.Ficha.relBPmax * 0.5;
				quem.Ficha.hiddenpotential = 1000;   // (2026-10-26) o banco que a agua solta
				SorteioDaAguaDeTeste = 0.9;   // quem salva e a resistencia (o corpo nasce com 1)
				return Ouvir(() => { Verbo(quem, AguaSagrada.VerboBeber, ""); Passar(3000); Passar(3000); Passar(3000); Passar(30_000); Passar(3000); });
			}

			ServerPlayer medido = Forjar("Medido", aoLado, 1);
			ScouterDeBancada(medido, true);
			falas = GoleInteiro(medido);
			AfirmarAg("COM SCOUTER a fala traz o poder de luta em numero (o aparelho e a porta de leitura)",
					  Disse(falas, "PODER DE LUTA") && TrazOPoder(falas, medido.Ficha.BP), Ultimos(falas));

			Jandirus.Core.Stats.PoderNaFrase.NumeroSemMedidorDeTeste = true;
			try
			{
				ServerPlayer espiado = Forjar("Espiado", aoLado, 1);
				falas = GoleInteiro(espiado);
				AfirmarAg("(defeito injetado: o numero sem medidor) a fala traz o poder de luta a quem nao tem scouter -- a regua de cima REPROVA",
						  Disse(falas, "PODER DE LUTA") && TrazOPoder(falas, espiado.Ficha.BP), Ultimos(falas));
			}
			finally { Jandirus.Core.Stats.PoderNaFrase.NumeroSemMedidorDeTeste = false; }

			// ---------------------------------------------------------------- 7. quem sai no meio
			GD.Print("[agua] -- 7) quem sai no meio do gole --");
			ServerPlayer apressado = Forjar("Apressado", aoLado, 1);
			double vigorDoApressado = apressado.Ficha.maxstamina;
			Ouvir(() => { Verbo(apressado, AguaSagrada.VerboBeber, ""); Passar(3000); });
			FecharOGoleDeQuemSai(apressado);
			AfirmarAg("quem desloga com a agua na boca LEVA o premio antes do save (no DM o proc morria e a agua unica se perdia)",
					  apressado.Ficha.sacredwater == 1 && Math.Abs(apressado.Ficha.maxstamina - vigorDoApressado * 1.2) < 1e-6 && _golesDeAgua.Count == 0);

			// ---------------------------------------------------------------- o defeito
			AguaSagrada.SemGarantiaDeTeste = true;
			try
			{
				ServerPlayer semSorte = Forjar("SemSorte", aoLado, 5);
				SorteioDaAguaDeTeste = 0.9;
				Ouvir(() => { Verbo(semSorte, AguaSagrada.VerboBeber, ""); Passar(3000); Passar(3000); Passar(3000); });
				AfirmarAg("(defeito injetado: a resistencia nao garante) o corpo de resistencia 5 MORRE com a moeda contra -- a regua sabe reprovar",
						  semSorte.Ficha.dead && semSorte.Ficha.sacredwater == 0);
			}
			finally { AguaSagrada.SemGarantiaDeTeste = false; }
		}
		catch (Exception e)
		{
			AfirmarAg("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			AguaSagrada.SemGarantiaDeTeste = false;
			Jandirus.Core.Stats.PoderNaFrase.NumeroSemMedidorDeTeste = false;
			SorteioDaAguaDeTeste = null;
			_golesDeAgua.Clear();
			foreach (ServerPlayer c in forjados) RemoverNpc(c);

			// O PLACAR SAI DAQUI: a bancada tem uma saida no meio (o jarro que nao esta no mapa), e sem placar
			// o lancador fica esperando o prazo inteiro.
			GD.Print($"[agua] ================ {_agOk} OK, {_agFalhou} FALHA(S) ================");
		}
	}
}
