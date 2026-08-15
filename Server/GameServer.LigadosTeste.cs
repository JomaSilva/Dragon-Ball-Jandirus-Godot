using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DOS CINCO SISTEMAS LIGADOS -- `--ligadosteste`.
///
/// ============================ POR QUE ELA EXISTE ============================
/// Cinco regras do `Core/` estavam escritas, corretas e SEM NENHUM CHAMADOR de producao. Este
/// arquivo e a segunda metade do conserto, e e a metade que costuma faltar neste projeto: escrever
/// a regra e APLICAR a regra sao dois trabalhos, e so o primeiro tem quem o cobre.
///
/// Ja houve seis casos de dado extraido sem consumidor aqui, mais a API de sigilo do BP 100% orfa.
/// O padrao se repete porque nada REPROVA quando a chamada some. Entao cada conferencia daqui e
/// escrita pra falhar se alguem apagar a linha que liga o sistema -- nao pra confirmar que o
/// metodo do `Core` calcula certo (disso cuidam as bancadas de unidade).
///
/// CADA UMA ATRAVESSA O FUNIL DE PRODUCAO, e nao o metodo do Core direto. Uma conferencia que
/// chama `f.FlightGain()` na mao fica VERDE com a chamada do `TickDoVoo` apagada -- ela mediria o
/// Core, que nunca esteve quebrado. Por isso aqui se voa pelo `TickDoVoo`, se envelhece pelo
/// `EnvelhecerNaSala`, se atira pelo `BasicBlast`.
/// ===========================================================================
///
/// ============================ CADA FAMILIA TEM UM CONTRA-EXEMPLO ============================
/// "O metodo foi chamado" fica VERDE com a regra errada dentro. Uma regra que credita exp em TODA
/// arvore, uma que mata TODO corpo, uma que paga o ganho de voo a QUEM ESTA PARADO -- as tres
/// passariam nas conferencias positivas, porque o efeito medido acontece. O que separa "funciona"
/// de "sempre dispara" e a NEGACAO, e por isso cada familia daqui tem o par:
///
///   voo      voar sobe o BP        x  quem esta no chao nao sobe
///   nado     nadar sobe o BP       x  nadar em linha reta nao sobe
///   marco    BPBoost 5 da patamar  x  BPBoost 4 nao da
///   velhice  o corpo de 75 morre   x  o de 25 atravessa o mesmo funil e vive
///   exp      a bola sobe a arvore  x  as arvores de raio e de debuff ficam em ZERO
///   genoma   o filho herda dos 2   x  o neto nao colapsa em dois nomes
/// ==========================================================================================
///
/// ============================ A LINHA-SENTINELA DE CADA SISTEMA ============================
/// A conferencia que fica VERMELHA se a chamada de producao sumir -- provada por mutacao, uma de
/// cada vez, e nao por leitura:
///
///   `FlightGain`               "voar TREINA"                (`GameServer.Voo.cs`, TickDoVoo)
///   `SwimGain`                 "NADAR TREINA"               (`GameServer.Nado.cs`, TickDoNado)
///   `CheckAscensionMilestone`  "BPBoost 5 rompe o marco"    (`GameServer.cs`, Treinar)
///   `MorreuDeVelhice`          "o aniversario de 75 MATA"   (`GameServer.SalaSessao.cs`)
///   `Creditar` (contador)      "ATIRAR credita exp"         (`GameServer.Projeteis.cs`)
///   `Creditar` (Light Buster)  "o Light Buster credita 1"   (`GameServer.Tecnicas.G3.cs`)
///
/// `Genome.Child` NAO TEM LINHA DESSAS, e nao por descuido: nao existe gravidez neste port, entao
/// nao ha chamada de producao pra sumir. O que a familia dele guarda e a REGRA (herdar dos dois
/// lados, atravessar geracoes) -- e o dia em que a gravidez entrar, ela ja esta cobrada.
/// ========================================================================================
///
///     Godot --headless -- --server --ligadosteste
/// </summary>
public partial class GameServer
{
	private bool _ligadosDeTeste;

	/// <summary>
	/// UMA CELULA DE AGUA COM AGUA EM VOLTA -- onde da pra por um corpo pra nadar.
	///
	/// Os oito vizinhos entram na conta porque o <see cref="MoveRules.NaAgua"/> olha as QUATRO
	/// QUINAS da caixa do corpo, e nao o centro: numa poca de uma celula so, uma quina cairia no
	/// seco e o `TickDoNado` desligaria o nado no primeiro tique -- a bancada mediria o
	/// desligamento e chamaria isso de "o ganho nao veio".
	///
	/// Devolve nulo pra zona sem plano de agua, que e o caso da Sala do Tempo, do Alem e do
	/// interior de nave. Quem chama TRATA o nulo como falha, e nao como "pula esta".
	/// </summary>
	private static (int Cx, int Cy)? AguaLargaEm(ZoneCollision? m)
	{
		if (m == null || !m.TemAgua) return null;
		for (int cy = 1; cy < m.Height - 1; cy++)
			for (int cx = 1; cx < m.Width - 1; cx++)
			{
				if (!m.EhAgua(cx, cy)) continue;
				bool todas = true;
				for (int dy = -1; dy <= 1 && todas; dy++)
					for (int dx = -1; dx <= 1 && todas; dx++)
						if (!m.EhAgua(cx + dx, cy + dy)) todas = false;
				if (todas) return (cx, cy);
			}
		return null;
	}

	/// <summary>Roda uma vez, no primeiro login. MEXE no mundo (spawna e remove corpos).</summary>
	private void RodarBancadaDosLigados(ServerPlayer quem)
	{
		GD.Print("\n===== BANCADA DOS CINCO SISTEMAS LIGADOS =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// zona vazia: os anuncios de morte e de marco nao chegam na tela de ninguem
		ZoneKey zona = ZoneKey.Premade(
			Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		var nascidos = new List<ServerPlayer>();

		// ============================ O CORPO DE BANCADA E DE JOGADOR ============================
		// `Peer` nulo faria dele um NPC, e os dois funis que estamos medindo recusam NPC de
		// proposito (`TickDosNiveis` e o `CreditarContador` so valem pra quem joga). Um corpo sem
		// dono deixaria as conferencias de exp VERDES por nao creditarem nada -- que e o defeito
		// que esta bancada existe pra pegar. Ver `EhJogador`.
		//
		// Como nao ha peer de verdade num teste headless, o corpo entra no `_players` pela mesma
		// porta dos outros (`PorNoMundo`) e a bancada marca a conta como a do host.
		// =======================================================================================
		ServerPlayer Forjar(string nome, string raca, int idade, Vec2 pos, ZoneKey? emZona = null)
		{
			var f = new Jandirus.Core.Stats.Fighter
			{
				Name = nome, Race = raca, BP = 5000,
				physoff = 5, physdef = 5, technique = 5, kioff = 5, kidef = 5,
				kiskill = 5, speed = 5, magiskill = 5, Idade = idade,
				maxstamina = 100, stamina = 100,
			};
			f.Statify();
			f.PowerLevel();
			f.Ki = f.MaxKi;

			var c = new ServerPlayer
			{
				Id = _nextId++, Peer = quem.Peer, Name = nome, Zone = emZona ?? zona, Pos = pos,
				Race = raca, Class = "", Genero = "Male", Idade = idade,
				Conta = quem.Conta, LastInputMs = NowMs(), Ficha = f,
				Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			PorNoMundo(c);
			nascidos.Add(c);
			return c;
		}

		try
		{
			// =====================================================================
			// 1. FlightGain -- `Stats.dm:414`, ligado no `TickDoVoo`
			// =====================================================================
			{
				ServerPlayer v = Forjar("VoadorDeTeste", "Saiyan", 25, NoCentroDe(40, 40));
				v.Voando = true;
				v.Altitude = Jandirus.Core.World.Voo.AlturaDePairar;

				double antes = v.Ficha.BP, kiAntes = v.Ficha.baseKi;

				// ============ O TANQUE E REABASTECIDO A CADA TIQUE, E ISSO E DE PROPOSITO ============
				// `TickDoVoo` DERRUBA quem fica sem Ki (`Ki > KiQueDerruba`), e um corpo de bancada sem
				// a skill de voo tem `flightability` no piso, o que faz o custo por segundo ser enorme:
				// na primeira rodada desta bancada o voador caiu em meio segundo e a medicao virou
				// ruido (deu 0,05x do esperado). Quem mede a ECONOMIA DE KI do voo e a bancada do voo;
				// esta aqui mede a CADENCIA do ganho, e pra isso o corpo precisa continuar no ar.
				// ====================================================================================
				const float Passo = 1f / 30f;
				for (int i = 0; i < 300; i++) { v.Ficha.Ki = v.Ficha.MaxKi; TickDoVoo(v, Passo); }

				double ganho = v.Ficha.BP - antes;

				// SE A CHAMADA SUMIR, ISTO DA ZERO -- e a conferencia que o dono pediu.
				Checa("voar TREINA (o `FlightGain` tem chamador no `TickDoVoo`)",
					  ganho > 0, $"BP nao mexeu em 10 s de voo (antes {antes:0.####}, depois {v.Ficha.BP:0.####})");

				// ============ E A CADENCIA E A DO ORIGINAL, NAO A DO TIQUE CHEIO ============
				// `Flight_Gain()` e chamado do `Stats()`, que dorme `sleep_tiem = 2` -- 5 Hz. Dez
				// segundos sao 50 pagamentos, e nao 300. Sem o acumulador (`SegundosDeVooSemGanho`)
				// isto daria 6x, e a conferencia de cima ficaria verde igual -- por isso as duas.
				//
				// A COMPARACAO E CONTRA UM CONTROLE, E NAO CONTRA UMA FORMULA ESCRITA AQUI. O ganho
				// nao e `BpGainBase * BPTick / 24` limpo: ele passa pelo `CapCheck`, que tem estado
				// (`Gaintimer`, `Buffertimer`) e muda de valor conforme se paga. A primeira versao
				// desta conferencia previa o numero na mao e reprovou por isso, medindo o `CapCheck`
				// em vez da cadencia. Um corpo GEMEO levando 50 chamadas diretas carrega o mesmo
				// `CapCheck` nos dois lados, e ai o que sobra na diferenca e so a contagem.
				ServerPlayer gemeo = Forjar("GemeoDeTeste", "Saiyan", 25, NoCentroDe(38, 40));
				double gemeoAntes = gemeo.Ficha.BP;
				for (int i = 0; i < 50; i++) gemeo.Ficha.FlightGain();
				double esperado = gemeo.Ficha.BP - gemeoAntes;

				Checa("...e paga 50 vezes em 10 s (5 Hz, o `sleep_tiem` do `Stats()`), nao 300",
					  esperado > 0 && Math.Abs(ganho - esperado) <= esperado * 0.02,
					  $"o gemeo com 50 chamadas ganhou {esperado:0.######}, o voador ganhou {ganho:0.######}"
					  + $" -- razao {(esperado > 0 ? ganho / esperado : 0):0.##}x (6,0x = cadencia do tique cheio)");

				Checa("...e o `baseKi` sobe junto (a linha de Ki fica FORA do gate de BP, 1:1 com o DM)",
					  v.Ficha.baseKi > kiAntes, $"baseKi parado em {v.Ficha.baseKi:0.####}");

				// QUEM NAO VOA NAO PAGA: sem esta, um `FlightGain()` solto no tique passaria.
				ServerPlayer p = Forjar("ParadoDeTeste", "Saiyan", 25, NoCentroDe(42, 40));
				double bpParado = p.Ficha.BP;
				for (int i = 0; i < 300; i++) TickDoVoo(p, Passo);
				Checa("...e quem esta no chao NAO ganha nada com isso",
					  Math.Abs(p.Ficha.BP - bpParado) < 1e-9, $"BP mexeu sem voar: {p.Ficha.BP - bpParado:0.######}");

				// =====================================================================
				// 1b. O IRMAO: SwimGain -- `Movement.dm:1-4`, ligado no `TickDoNado`
				//
				// ============ POR QUE O IRMAO ENTROU NESTA BANCADA, E NAO NA DA AGUA ============
				// O `SwimGain` ganhou chamador na sessao da agua, mas NENHUMA bancada cobra o ganho:
				// o `agua-prova` e o `--diagagua` medem colisao, pose, sombra e custo de Ki, e um
				// grep por `SwimGain` nos dois nao acha nada. Ou seja, o irmao estava exatamente na
				// situacao que este arquivo existe pra fechar -- ligado e sem quem reprove.
				//
				// E OS DOIS PRECISAM SER MEDIDOS JUNTOS. Eles sao a mesma linha do DM com fracao
				// diferente, e "consertar" um sem o outro e o defeito novo que o dono citou.
				// =============================================================================
				{
					// A RAZAO EXATA, EM CORPOS GEMEOS. `Flight_Gain` paga `1/24` e `Swim_Gain` paga
					// `1/30` (`Movement.dm:1-11`) -- 30/24 = 1,25. Uma chamada em cada corpo NOVO:
					// o `CapCheck` tem estado (`Gaintimer`, `BPBuffer`), e na primeira chamada os
					// dois estao no mesmo ponto dele, entao o que sobra na razao e so a fracao.
					ServerPlayer iv = Forjar("IrmaoDoVooDeTeste", "Human", 25, NoCentroDe(56, 40));
					ServerPlayer inn = Forjar("IrmaoDoNadoDeTeste", "Human", 25, NoCentroDe(58, 40));
					double bv = iv.Ficha.BP, bn = inn.Ficha.BP;
					iv.Ficha.FlightGain();
					inn.Ficha.SwimGain();
					double gv = iv.Ficha.BP - bv, gn = inn.Ficha.BP - bn;

					Checa("voo e nado sao IRMAOS: 1/24 contra 1/30, razao 1,25 (`Movement.dm:1-11`)",
						  gn > 0 && Math.Abs(gv / gn - 30.0 / 24.0) < 1e-6,
						  $"voo {gv:0.######}, nado {gn:0.######}, razao {(gn > 0 ? gv / gn : 0):0.####}");

					// E OS DOIS PARAM NO MESMO TETO. `if(BP < relBPmax)` abre a linha de BP nos dois;
					// um irmao que perdesse esse gate viraria torneira aberta pra quem ja esta capado.
					iv.Ficha.BP = iv.Ficha.relBPmax;
					inn.Ficha.BP = inn.Ficha.relBPmax;
					double tv = iv.Ficha.BP, tn = inn.Ficha.BP;
					iv.Ficha.FlightGain();
					inn.Ficha.SwimGain();
					Checa("...e os DOIS param no `relBPmax` (nenhum vira torneira aberta no topo)",
						  Math.Abs(iv.Ficha.BP - tv) < 1e-9 && Math.Abs(inn.Ficha.BP - tn) < 1e-9,
						  $"voo mexeu {iv.Ficha.BP - tv:0.######}, nado mexeu {inn.Ficha.BP - tn:0.######}");

					// ============ AGORA PELO FUNIL, QUE E O QUE REPROVA SE A CHAMADA SUMIR ============
					// Nadar so acontece em cima de celula marcada como agua (`MoveRules.NaAgua`), e o
					// plano de agua e DADO do mapa: nao ha API de escrever uma celula, e trocar o
					// bitset da zona viva estragaria o mundo de quem esta nela. Entao a bancada
					// PROCURA um lago em vez de fabricar um.
					// ==============================================================================
					ZoneKey zonaDoLago = zona;
					(int Cx, int Cy)? lago = AguaLargaEm(MapaDaZonaOuCatalogo(zona));
					if (lago == null && AguaLargaEm(MapaDaZonaOuCatalogo(quem.Zone)) is { } naDoHost)
					{
						zonaDoLago = quem.Zone;
						lago = naDoHost;
					}
					if (lago == null)
						foreach (Jandirus.Core.World.PlanetaNoEspaco pl in Espaco.PreFeitos())
						{
							ZoneKey zk = ZoneKey.Premade(pl.Nome);
							if (AguaLargaEm(MapaDaZonaOuCatalogo(zk)) is not { } achado) continue;
							zonaDoLago = zk;
							lago = achado;
							break;
						}

					// FALHA, E NAO "PULADO": um teste que se desliga sozinho quando o cenario nao
					// coopera e a mesma doenca da regra sem chamador -- fica verde sem medir nada.
					Checa("ha lago no mundo pra medir o nado (o plano de agua carregou)",
						  lago != null, "nenhuma zona pre-feita tem `.agua` -- rode o AssetPipeline");

					if (lago is { } L)
					{
						ServerPlayer n = Forjar("NadadorDeTeste", "Human", 25,
												NoCentroDe(L.Cx, L.Cy), zonaDoLago);
						n.Nadando = true;
						n.NadoJaMolhou = true;   // ja dentro d'agua: a janela de ENTRADA nao e o que se mede aqui
						n.Facing = Jandirus.Core.World.Facing.East;
						n.UltimaDirDoNado = n.Facing;

						// EM LINHA RETA NAO PAGA, e esta e a diferenca FIEL entre os irmaos: o
						// `lastdir` do `Swim_Gain` e var de MOB (`Movement.dm:1-4`) e sobrevive entre
						// chamadas, enquanto o `lastloc` do `Flight_Gain` e LOCAL ao proc e nasce nulo
						// toda vez. O nado tem a guarda que o voo promete e nunca teve.
						double antesN = n.Ficha.BP;
						for (int i = 0; i < 60; i++) { n.Ficha.Ki = n.Ficha.MaxKi; TickDoNado(n, Passo); }
						Checa("...e nadar em LINHA RETA nao paga (o `lastdir` e var de mob, e sobrevive)",
							  n.Ficha.BP <= antesN + 1e-9 && n.Nadando,
							  $"BP mexeu {n.Ficha.BP - antesN:0.######} sem trocar de direcao (nadando={n.Nadando})");

						// TROCANDO DE DIRECAO, PAGA. SE A CHAMADA DO `TickDoNado` SUMIR, ISTO DA ZERO.
						for (int i = 0; i < 60; i++)
						{
							n.Ficha.Ki = n.Ficha.MaxKi;
							n.Facing = i % 2 == 0 ? Jandirus.Core.World.Facing.West : Jandirus.Core.World.Facing.East;
							TickDoNado(n, Passo);
						}
						Checa("NADAR TREINA (o `SwimGain` tem chamador no `TickDoNado`)",
							  n.Ficha.BP > antesN,
							  $"BP parado em {n.Ficha.BP:0.####} depois de 60 trocas de direcao dentro d'agua");
					}
				}
			}

			// =====================================================================
			// 2. CheckAscensionMilestone -- `ascensioncontrols.dm:83`, ligado no `Treinar`
			// =====================================================================
			{
				ServerPlayer a = Forjar("AscendidoDeTeste", "Human", 25, NoCentroDe(44, 40));

				Checa("um corpo novo nao tem marco de ascensao nenhum",
					  Math.Abs(a.Ficha.bp_milestone_mult - 1) < 1e-9,
					  $"bp_milestone_mult nasceu {a.Ficha.bp_milestone_mult}");

				// A RACA E HUMANA DE PROPOSITO. O marco de ascensao existe "pras racas que nao tem
				// forma" (`Fighter.Training.cs:40`): um Saiyajin tem a escada SSJ como outro caminho
				// pro patamar, e medir o degrau NELE nao responderia a pergunta do dono. Se um dia
				// esta raca ganhar escada, esta linha reprova antes de a prova mentir.
				Checa("...e o corpo da prova nao tem escada de forma (e a raca que o marco atende)",
					  !a.Ficha.canSSJ && a.Ficha.SaiyanLineage.Length == 0,
					  $"canSSJ={a.Ficha.canSSJ}, linhagem='{a.Ficha.SaiyanLineage}' -- a prova mediria outra coisa");

				// ============ ABAIXO DA LINHA NAO GANHA NADA, E SEM ISTO AS DUAS DE BAIXO SAO CEGAS ============
				// Um `CheckAscensionMilestone` que devolvesse patamar pra qualquer `BPBoost` -- ou um
				// `ReachMilestone` que aceitasse qualquer etiqueta -- deixaria as duas conferencias
				// positivas VERDES. O degrau tem que ser o `>= 5`, e nao "houve degrau".
				a.Ficha.BPBoost = 4;
				Treinar(a);
				Checa("...e BPBoost 4 (um abaixo do `asc5`) NAO da patamar nenhum",
					  Math.Abs(a.Ficha.bp_milestone_mult - 1) < 1e-9,
					  $"bp_milestone_mult = {a.Ficha.bp_milestone_mult} com BPBoost 4 (esperado 1)");

				// ============ O `BPBoost` E LEVANTADO NA MAO, E ISSO E O PONTO ============
				// A Ascensao (`Auto_Gain`) NAO esta portada: nenhum caminho de producao escreve
				// `BPBoost`, entao em jogo este marco esta DORMENTE hoje. A bancada faz o papel do
				// sistema que falta -- ela poe o `BPBoost` no valor que o `Auto_Gain` poria e
				// cobra o degrau. Assim a chamada fica guardada desde ja: se ela sumir, isto
				// reprova, mesmo que nada no jogo ainda a exercite.
				a.Ficha.BPBoost = 5;
				Treinar(a);
				Checa("BPBoost 5 rompe o marco `asc5` pelo funil do `Treinar` (x2 no ganho)",
					  Math.Abs(a.Ficha.bp_milestone_mult - 2) < 1e-9,
					  $"bp_milestone_mult = {a.Ficha.bp_milestone_mult} (esperado 2)");

				a.Ficha.BPBoost = 20;
				Treinar(a);
				Checa("...e BPBoost 20 sobe pro `asc20` (x4)",
					  Math.Abs(a.Ficha.bp_milestone_mult - 4) < 1e-9,
					  $"bp_milestone_mult = {a.Ficha.bp_milestone_mult} (esperado 4)");

				// O MARCO TEM QUE CHEGAR NA BASE DO GANHO, senao ele e so um numero guardado.
				Checa("...e o marco MULTIPLICA a base de ganho (`BpGainBase`)",
					  a.Ficha.BpGainBase() > 0
					  && Math.Abs(a.Ficha.BpGainBase()
								  - Jandirus.Core.Stats.GainKnobs.LinearGainBase * Math.Max(a.Ficha.BPMod, 0.1) * 4) < 1e-6,
					  $"BpGainBase = {a.Ficha.BpGainBase():0.####}");
			}

			// =====================================================================
			// 3. MorreuDeVelhice -- `Aging.dm:176-183`, ligado no `EnvelhecerNaSala`
			// =====================================================================
			{
				// UM ANO ANTES DA LINHA. O Humano tem auge 50 e morre em 75 (auge x 1,5); nascer em
				// 74 faz o proximo aniversario ser o que mata -- e assim quem mede e a TRAVESSIA da
				// linha, nao o corpo ja nascido do outro lado. Nascer morto testaria o `if`, e nao
				// o funil que leva ate ele.
				ServerPlayer velho = Forjar("VelhoDeTeste", "Human", 74, NoCentroDe(46, 40));

				Checa("aos 74 (auge 50, morte em 75) o Humano ainda esta vivo",
					  !velho.Ficha.dead && !velho.Ficha.aged_out,
					  $"morreu cedo demais aos {velho.Idade}");

				EnvelhecerNaSala(velho);   // o unico calendario que este port tem

				Checa("o aniversario de 75 MATA de velhice (o `MorreuDeVelhice` tem chamador)",
					  velho.Ficha.dead, $"idade {velho.Idade}, Body {Jandirus.Core.Races.Envelhecimento.IdadeDoCorpo("Human", velho.Idade):0.##}, dead={velho.Ficha.dead}");

				Checa("...e a morte fica marcada como `aged_out` (`Aging.dm:177`)",
					  velho.Ficha.aged_out, "aged_out ficou falso");

				// A MARCA TEM QUE VALER PRA ALGUMA COISA -- senao ela e outro campo escrito e orfao,
				// que e a familia de defeito que esta bancada inteira persegue.
				ServerPlayer curandeiro = Forjar("CuradorDeTeste", "Namekian", 30,
												 velho.Pos + new Vec2(ZoneCollision.TileSize * 0.5f, 0));
				RessuscitarG4(curandeiro);
				Checa("...e NENHUM revive traz de volta quem morreu de velhice (`Death.dm:144`)",
					  velho.Ficha.dead, "o Ressuscitar trouxe de volta um morto de velhice");

				// ============ CONTROLE: O MESMO REVIVE FUNCIONA NUMA MORTE COMUM ============
				// Sem ele, a conferencia de cima ficaria verde com o `RessuscitarG4` quebrado por
				// qualquer outro motivo -- ela nao distingue "recusou pela velhice" de "nao revive
				// mais ninguem".
				//
				// LONGE DO OUTRO CADAVER, e isto custou uma rodada vermelha: `AlvoPertoG4` pega o
				// morto MAIS PROXIMO dentro de um tile, e com o par de controle ao lado do morto de
				// velhice era o VELHO que ele achava de novo -- o revive recusava certo, pelo motivo
				// certo, e o controle acusava "parou de funcionar pra todo mundo". Um par novo, a
				// quatorze tiles dali, nao tem nenhum outro corpo no raio.
				ServerPlayer comum = Forjar("MortoComumDeTeste", "Human", 30, NoCentroDe(60, 40));
				ServerPlayer curandeiro2 = Forjar("CuradorDeTeste2", "Namekian", 30,
												  comum.Pos + new Vec2(ZoneCollision.TileSize * 0.5f, 0));
				comum.Combate?.Morrer(ignorarSeguro: true);
				Checa("um morto COMUM nao fica marcado como `aged_out`",
					  comum.Ficha.dead && !comum.Ficha.aged_out,
					  $"dead={comum.Ficha.dead}, aged_out={comum.Ficha.aged_out}");

				RessuscitarG4(curandeiro2);
				Checa("...mas ele AINDA funciona numa morte comum (o gate e so da velhice)",
					  !comum.Ficha.dead, "o Ressuscitar parou de funcionar pra todo mundo");

				// ============ O CONTRA-EXEMPLO, PELO MESMO FUNIL ============
				// "O velho morreu" fica VERDE tambem quando morre todo mundo -- um `MorreuDeVelhice`
				// que devolvesse verdadeiro sempre (ou um `IdadeDoCorpo` com a curva invertida)
				// passaria em todas as conferencias de cima. Quem separa "a linha esta no lugar" de
				// "a linha nao existe" e um corpo NOVO atravessando o MESMO `EnvelhecerNaSala`.
				ServerPlayer novo = Forjar("NovoDeTeste", "Human", 25, NoCentroDe(48, 40));
				EnvelhecerNaSala(novo);
				EnvelhecerNaSala(novo);
				Checa("...e o corpo NOVO atravessa o mesmo funil duas vezes e sai VIVO (aos 27)",
					  !novo.Ficha.dead && !novo.Ficha.aged_out && novo.Idade == 27,
					  $"idade {novo.Idade}, dead={novo.Ficha.dead}, aged_out={novo.Ficha.aged_out}");
			}

			// =====================================================================
			// 4. Creditar -- Light Buster (`yardrat.dm:245`) e os 30 contadores
			// =====================================================================
			{
				// -------- 4a. os 30 contadores, pelo funil do tiro --------
				ServerPlayer t = Forjar("AtiradorDeTeste", "Human", 25, NoCentroDe(50, 40));

				// ============ O TANQUE DELE E MAIOR QUE O DE FABRICA, E TEM QUE SER ============
				// O corpo de bancada nasce com `baseKi` 100, e o Solar Flare custa `100*BaseDrain()`
				// (`Tecnicas.cs:206`) -- ~115 de Ki. Ou seja: com o tanque de fabrica a tecnica sai
				// pela porta do "isso pede pelo menos X de energia" e NUNCA chega no credito. A
				// primeira rodada desta familia reprovou exatamente assim, e a linha vermelha dizia
				// "exp de debuff parado em 0" -- que e o mesmo texto que apareceria se o
				// `CreditarContador` do Solar Flare tivesse sumido. Um tanque que nao paga o gesto
				// transforma a conferencia num teste de economia de Ki disfarcado de teste de exp.
				// ============================================================================
				t.Ficha.baseKi = 5000;
				t.Ficha.Statify();
				t.Ficha.PowerLevel();
				t.Ficha.Ki = t.Ficha.MaxKi;

				const string Blast = "/datum/skill/mind/Basic_Blast_Mastery";

				Checa("a regra do `Basic_Blast_Mastery` existe (o `niveis.json` foi carregado)",
					  Jandirus.Core.Skills.RegrasDeNivel.Get(Blast) != null,
					  "sem regra -- rode o AssetPipeline (comando 'effector')");

				// ============ ELE APRENDE A FAMILIA DA BOLA **E** DUAS ARVORES QUE NAO TEM A VER ============
				// O contra-exemplo so existe se as outras arvores estiverem APRENDIDAS: o `Creditar`
				// recusa skill que a pessoa nao tem, entao um corpo que so sabe a bola ficaria com
				// zero nas outras por um motivo que nao e o que se quer medir. Aprendidas, a unica
				// coisa que as mantem em zero e o INDICE POR CONTADOR fazer o seu trabalho.
				// ==========================================================================================
				const string BlastAv = "/datum/skill/mind/Advanced_Blast_Mastery";
				const string BlastPf = "/datum/skill/mind/Perfect_Blast_Mastery";
				const string Beam = "/datum/skill/mind/Basic_Beam_Mastery";       // `beamcounter`
				const string Debuff = "/datum/skill/mind/Basic_Debuff_Mastery";   // `kidebuffcounter`
				foreach (string sk in (ReadOnlySpan<string>)[Blast, BlastAv, BlastPf, Beam, Debuff])
				{
					t.Livro.Dar(sk);
					t.Niveis.Por(sk, 0);
				}

				double expAntes = t.Niveis.Exp(Blast);
				BasicBlast(t);   // o verb de tiro, funil inteiro
				double expDepois = t.Niveis.Exp(Blast);

				Checa("ATIRAR credita exp na pericia de bola (os 30 contadores tem chamador)",
					  expDepois > expAntes,
					  $"exp parado em {expDepois:0.####} -- `blastcounter` nao chegou no `Creditar`");

				// O VALOR E O DO ORIGINAL: `KiSkillGains(10 * 1)` com `blastcounter++`
				// (`blasts.dm:54`). Sem esta, um credito de valor errado passaria despercebido.
				double esperado = Jandirus.Core.Skills.NiveisDeSkill.KiSkillGains(10 * 1, t.Ficha);
				Checa("...e credita o valor do `KiSkillGains(10*1)`, nao um numero qualquer",
					  Math.Abs((expDepois - expAntes) - esperado) < 1e-6,
					  $"veio {expDepois - expAntes:0.######}, esperado {esperado:0.######}");

				// A FAMILIA INTEIRA SOBE JUNTO. Os tres degraus (Basic/Advanced/Perfect) observam o
				// MESMO `blastcounter` -- e assim no DM, e e o que o indice por contador devolve. Se
				// ele passasse a devolver so o primeiro, as duas arvores de cima congelariam calado.
				Checa("...e os TRES degraus da familia sobem no mesmo tiro (Basic/Advanced/Perfect)",
					  t.Niveis.Exp(BlastAv) > 0 && t.Niveis.Exp(BlastPf) > 0,
					  $"Advanced {t.Niveis.Exp(BlastAv):0.####}, Perfect {t.Niveis.Exp(BlastPf):0.####}");

				// ============ E AS ARVORES QUE NAO TEM A VER FICAM EM ZERO ============
				// ESTA E A CONFERENCIA QUE O DONO PEDIU POR ESCRITO, e sem ela a familia inteira e
				// cega: um `CreditarPorContador` que ignorasse o nome do contador e creditasse TODA
				// arvore aprendida deixaria as quatro conferencias de cima verdes. "Subiu" nao e a
				// pergunta -- a pergunta e "subiu SO a certa".
				// ====================================================================
				Checa("...e a arvore de RAIO nao sobe com um tiro de bola (`beamcounter` != `blastcounter`)",
					  Math.Abs(t.Niveis.Exp(Beam)) < 1e-9, $"exp de raio: {t.Niveis.Exp(Beam):0.######}");
				Checa("...e a de DEBUFF tambem nao (creditar tudo em todo mundo reprova aqui)",
					  Math.Abs(t.Niveis.Exp(Debuff)) < 1e-9, $"exp de debuff: {t.Niveis.Exp(Debuff):0.######}");

				// ============ E O CRUZAMENTO, POR OUTRO FUNIL E NO SENTIDO CONTRARIO ============
				// O Solar Flare credita `kidebuffcounter += 4` (`misc.dm:14`) -- outro gesto, outro
				// arquivo, outra familia. As duas linhas juntas provam que o mapeamento golpe->arvore
				// e real nos DOIS sentidos: nao ha como passar nas quatro creditando sempre a mesma
				// arvore, nem creditando todas.
				double blastAntesDoFlare = t.Niveis.Exp(Blast);
				t.Ficha.Ki = t.Ficha.MaxKi;   // o tiro de cima gastou; o que se mede aqui nao e o tanque
				SolarFlare(t);
				Checa("o Solar Flare credita a arvore de DEBUFF (`misc.dm:14`, `kidebuffcounter += 4`)",
					  t.Niveis.Exp(Debuff) > 0,
					  $"exp de debuff parado em {t.Niveis.Exp(Debuff):0.######}"
					  + $" (Ki {t.Ficha.Ki:0}, a tecnica pede {Jandirus.Core.Skills.Tecnicas.SolarCustoKi(t.Ficha):0})");
				Checa("...e o MESMO gesto nao mexe na arvore de BOLA (cada contador tem a sua familia)",
					  Math.Abs(t.Niveis.Exp(Blast) - blastAntesDoFlare) < 1e-9,
					  $"a bola andou {t.Niveis.Exp(Blast) - blastAntesDoFlare:0.######} com um Solar Flare");

				// QUEM NAO TEM A SKILL NAO ACUMULA NADA -- sem progresso fantasma.
				ServerPlayer semSkill = Forjar("SemSkillDeTeste", "Human", 25, NoCentroDe(52, 40));
				BasicBlast(semSkill);
				Checa("...e quem NAO aprendeu a skill nao acumula progresso fantasma",
					  Math.Abs(semSkill.Niveis.Exp(Blast)) < 1e-9,
					  $"exp fantasma: {semSkill.Niveis.Exp(Blast):0.######}");

				// -------- 4b. o Light Buster, a unica `Skill_EXP_Add` do DM --------
				ServerPlayer y = Forjar("YardratDeTeste", "Yardrat", 25, NoCentroDe(54, 40));
				ServerPlayer alvo = Forjar("AlvoDeTeste", "Human", 25,
										   y.Pos + new Vec2(ZoneCollision.TileSize, 0));
				const string LB = "/datum/skill/yardrat/Light_Buster";
				y.Livro.Dar(LB);
				y.Niveis.Por(LB, 0);

				double lbAntes = y.Niveis.Exp(LB);
				ResolverBusterG3(y, new BusterG3 { Alvo = alvo.Id, QuandoMs = NowMs() });
				Checa("o Light Buster credita 1 de exp na propria skill (`yardrat.dm:245`)",
					  Math.Abs((y.Niveis.Exp(LB) - lbAntes) - 1) < 1e-9,
					  $"veio {y.Niveis.Exp(LB) - lbAntes:0.####}, esperado 1");
			}

			// =====================================================================
			// 5. Genome.Child -- `copy_races` (`Genetic_Sex.dm:23-70`)
			// =====================================================================
			{
				// O EXEMPLO ESTA NO COMENTARIO DO PROPRIO DM (`Genetic_Sex.dm:29`): meio-saiyajin
				// com um Icer da 25% Saiyan / 25% Human / 50% Icer. E o caso que a assinatura
				// antiga -- que recebia dois ROTULOS de raca -- nao tinha como produzir.
				var meio = new Jandirus.Core.Races.Genome();
				meio.Ancestry["Saiyan"] = 50;
				meio.Ancestry["Human"] = 50;
				meio.MajorityRace = "Saiyan";

				Jandirus.Core.Races.Genome icer = Jandirus.Core.Races.Genome.Pure("Icer");
				Jandirus.Core.Races.Genome cria = Jandirus.Core.Races.Genome.Child(meio, icer);

				Checa("filho de meio-saiyajin com Icer da 25 Saiyan / 25 Human / 50 Icer",
					  Math.Abs(cria.RacePercent("Saiyan") - 25) < 1e-9
					  && Math.Abs(cria.RacePercent("Human") - 25) < 1e-9
					  && Math.Abs(cria.RacePercent("Icer") - 50) < 1e-9,
					  $"Saiyan {cria.RacePercent("Saiyan")}, Human {cria.RacePercent("Human")}, Icer {cria.RacePercent("Icer")}");

				Checa("...e a AVO humana sobrevive a geracao (era o que a assinatura antiga perdia)",
					  cria.RacePercent("Human") > 0, "a ancestralidade humana sumiu");

				Checa("...e a maior fatia vira a raca majoritaria",
					  cria.MajorityRace == "Icer", $"MajorityRace = '{cria.MajorityRace}'");

				// ============ DUAS GERACOES, QUE E ONDE O DEFEITO ANTIGO SO APARECERIA ============
				// O neto de (meio-saiyajin x Icer) com uma Namekiana tem que carregar QUATRO
				// ancestrais: 12,5 Saiyan / 12,5 Human / 25 Icer / 50 Namekian. Com a assinatura
				// antiga -- dois ROTULOS de raca -- a arvore ja teria colapsado em dois nomes na
				// primeira geracao, e o neto nasceria 50/50: a avo humana e o bisavo saiyajin
				// sumindo calados, uma geracao por vez. Uma conferencia de UMA geracao so nao pega
				// isso, porque com rotulos a primeira ainda "parece" certa.
				Jandirus.Core.Races.Genome neto =
					Jandirus.Core.Races.Genome.Child(cria, Jandirus.Core.Races.Genome.Pure("Namekian"));

				Checa("...e a SEGUNDA geracao nao colapsa: o neto guarda os quatro ancestrais",
					  neto.Ancestry.Count == 4
					  && Math.Abs(neto.RacePercent("Saiyan") - 12.5) < 1e-9
					  && Math.Abs(neto.RacePercent("Human") - 12.5) < 1e-9
					  && Math.Abs(neto.RacePercent("Icer") - 25) < 1e-9
					  && Math.Abs(neto.RacePercent("Namekian") - 50) < 1e-9,
					  $"{neto.Ancestry.Count} ancestral(is): Saiyan {neto.RacePercent("Saiyan")},"
					  + $" Human {neto.RacePercent("Human")}, Icer {neto.RacePercent("Icer")},"
					  + $" Namekian {neto.RacePercent("Namekian")}");

				Checa("...e a ancestralidade do NETO tambem soma 100 (a arvore nao vaza nem infla)",
					  Math.Abs(neto.Ancestry.Values.Sum() - 100) < 1e-9,
					  $"somou {neto.Ancestry.Values.Sum()}");

				// RACAS IGUAIS CONTINUAM PURAS: 50 + 50 = 100, sem caso especial no codigo.
				Jandirus.Core.Races.Genome puro = Jandirus.Core.Races.Genome.Child(
					Jandirus.Core.Races.Genome.Pure("Saiyan"), Jandirus.Core.Races.Genome.Pure("Saiyan"));
				Checa("...e dois Saiyajins puros fazem um Saiyajin 100% (nao 50%)",
					  Math.Abs(puro.RacePercent("Saiyan") - 100) < 1e-9 && puro.Ancestry.Count == 1,
					  $"deu {puro.RacePercent("Saiyan")}% em {puro.Ancestry.Count} entrada(s)");

				// A soma nunca pode passar de 100 -- e o que garante que o `Build` pondere direito.
				double soma = cria.Ancestry.Values.Sum();
				Checa("...e a ancestralidade sempre soma 100",
					  Math.Abs(soma - 100) < 1e-9, $"somou {soma}");
			}
		}
		finally
		{
			foreach (ServerPlayer n in nascidos)
				if (_players.ContainsKey(n.Id)) RemoverNpc(n);

			GD.Print($"===== FIM: {ok} ok, {falhou} falha(s) =====\n");
			if (falhou > 0) GD.PushError($"[server] bancada dos ligados: {falhou} falha(s)");
			Avisar(quem, $"bancada dos ligados: {ok} ok, {falhou} falha(s) -- veja o console.");
		}
	}
}
