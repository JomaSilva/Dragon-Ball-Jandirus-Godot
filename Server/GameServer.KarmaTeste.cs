using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Ranks;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DO KARMA -- roda dentro do `--formasteste`.
///
/// ============================ POR QUE ELA NAO E UMA BANCADA DE BOOT ============================
/// Porque o karma so acontece pra quem <see cref="Gente.EhJogador"/> diz que e jogador, e esse
/// predicado pede `Peer != null`. Uma bancada de boot forja corpos com `Peer` nulo -- e foi
/// exatamente assim que a `--cargoportas` passou a medir a dadiva sem nunca provar que ela chega em
/// alguem: **104 checagens verdes sobre corpos que o proprio jogo nao considera gente**.
///
/// Aqui os corpos forjados EMPRESTAM o `Peer` do host, como a secao 5 da bancada de formas ja faz.
/// E o unico jeito de a bancada atravessar a mesma porta que o jogador atravessa.
/// ==========================================================================================
///
/// ============================ E POR QUE ELA MEDE A CONSEQUENCIA, E NAO O NUMERO ============================
/// A aritmetica do karma e de tres linhas e se prova de olho. O que precisava de prova e a
/// CORRENTE, porque o defeito que este arquivo conserta nao era uma conta errada -- era uma conta
/// que **nao tinha quem a fizesse**:
///
///     matar -> karma mudar -> ir pro disco -> voltar do disco -> UM CARGO ABRIR
///
/// A ultima secao e a que importa. Sem ela esta bancada mediria de novo a propria intencao: "somei
/// 50 e o campo tem 50". Com ela, o que fica provado e que o Guardiao da Terra deixou de ser
/// inalcancavel -- que e o motivo de o arquivo existir.
/// ========================================================================================================
/// </summary>
public partial class GameServer
{
	private const int IdBaseDoKarma = 90_900;

	private void OKarmaAoVivo(ServerPlayer host, Action<string, bool, string> Checa)
	{
		ZoneKey zona = ZoneKey.Premade(
			Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		// O `Peer` EMPRESTADO E O PONTO DA BANCADA -- ver o cabecalho. Sem ele `EhJogador` devolve
		// falso, `SomarKarma` sai na primeira linha e TUDO aqui embaixo passaria por ausencia.
		ServerPlayer Forjar(int i, string nome)
		{
			var novo = new ServerPlayer
			{
				Id = IdBaseDoKarma + i,
				Peer = host.Peer,
				Name = nome,
				Race = "Human",
				Genero = "Male",
				Idade = 25,
				Zone = zona,
				Pos = new Vec2(i * ZoneCollision.TileSize, 0),
				Conta = $"bancada_karma_{i}",
				Slot = 0,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 5_000_000 },
			};
			novo.Ficha.Class = "Normal";
			PorNoMundo(novo);
			novo.Ficha.Ki = novo.Ficha.MaxKi;
			return novo;
		}

		ServerPlayer algoz = Forjar(1, "bancada karma: o algoz");
		ServerPlayer inocente = Forjar(2, "bancada karma: o inocente");
		ServerPlayer vilao = Forjar(3, "bancada karma: o vilao");

		try
		{
			// ============================ 1. A ENTRADA NO ESTADO ============================
			// Pelo FUNIL DE PRODUCAO (`AoPerderALuta`), e nao pelo `SomarKarma`. E a diferenca entre
			// "a soma funciona" e "matar alguem soma" -- e era a segunda que faltava.
			algoz.Karma = Karma.Neutro;
			inocente.KarmaDaMorteContado = false;
			AoPerderALuta(inocente, algoz, morreu: true);
			Checa("MATAR UM INOCENTE suja a alma pelo funil de derrota (o `killer_stuff` do DM)",
				  algoz.Karma == -Karma.PorMatarJogador, $"{algoz.Karma}");

			// O VILAO E RECONHECIDO PELO KARMA DELE, e nao por selo de admin -- as duas metades do
			// `||` do DM, e esta e a que se ganha jogando.
			vilao.Karma = -40;
			vilao.KarmaDaMorteContado = false;
			AoPerderALuta(vilao, algoz, morreu: true);
			Checa("...e ABATER UM ASSASSINO (karma negativo) limpa de volta, na mesma medida",
				  algoz.Karma == Karma.Neutro, $"{algoz.Karma}");

			// A OUTRA METADE DO `||`: o selo do admin, pro vilao de roteiro que ainda nao sujou a
			// ficha. Sem ela, cacar um vilao selado e de ficha limpa puniria o cacador.
			vilao.Karma = Karma.Neutro;
			vilao.Ficha.isVillain = true;
			vilao.KarmaDaMorteContado = false;
			AoPerderALuta(vilao, algoz, morreu: true);
			Checa("...e o SELO DE VILAO conta mesmo com a ficha dele limpa (a 2a metade do `||`)",
				  algoz.Karma == Karma.PorMatarJogador, $"{algoz.Karma}");
			vilao.Ficha.isVillain = false;

			// ============================ 2. UMA MORTE, UMA CONTA ============================
			// O `pk_karma_taken`. A trava e da VITIMA, e ela existe porque `AoPerderALuta(morreu:
			// true)` tem dois chamadores (o golpe e a absorcao) -- um Majin que absorve quem acabou
			// de matar pagaria 40 por uma morte so.
			algoz.Karma = Karma.Neutro;
			inocente.KarmaDaMorteContado = false;
			AoPerderALuta(inocente, algoz, morreu: true);
			AoPerderALuta(inocente, algoz, morreu: true);
			Checa("A MESMA MORTE NAO CONTA DUAS VEZES (o `pk_karma_taken` do DM)",
				  algoz.Karma == -Karma.PorMatarJogador, $"{algoz.Karma}");

			// ...E A TRAVA CAI NO FUNIL DA MORTE, nao no revive. Sem este rearme, assassinar a mesma
			// pessoa uma segunda vez sairia de graca pra sempre.
			AMorteAconteceu(inocente);
			AoPerderALuta(inocente, algoz, morreu: true);
			Checa("...mas a MORTE SEGUINTE conta de novo (o rearme no `AMorteAconteceu`)",
				  algoz.Karma == -2 * Karma.PorMatarJogador, $"{algoz.Karma}");

			// ============================ 3. NOCAUTE NAO E ASSASSINATO ============================
			// O DM pendura o karma dentro do `killer_stuff`, que so a morte chama. Sem esta regra,
			// um treino entre amigos seria a fabrica de karma do servidor.
			algoz.Karma = Karma.Neutro;
			inocente.KarmaDaMorteContado = false;
			AoPerderALuta(inocente, algoz, morreu: false);
			Checa("DERRUBAR SEM MATAR nao mexe no karma (o karma mora no `killer_stuff`)",
				  algoz.Karma == Karma.Neutro, $"{algoz.Karma}");

			// ============================ 4. NINGUEM SE PUNE POR MORRER ============================
			// A Explosao Final e o Kaio-ken que estoura passam pelo funil com autor == vitima.
			algoz.KarmaDaMorteContado = false;
			AoPerderALuta(algoz, algoz, morreu: true);
			Checa("MORRER DA PROPRIA EXPLOSAO nao suja a alma de ninguem (`victim == src`)",
				  algoz.Karma == Karma.Neutro, $"{algoz.Karma}");

			// ============================ 5. O PISO E O TETO SEGURAM ============================
			// Nove mortes de inocente sao 180 pontos numa escala que so tem 100.
			algoz.Karma = Karma.Neutro;
			for (int i = 0; i < 9; i++)
			{
				inocente.KarmaDaMorteContado = false;
				AoPerderALuta(inocente, algoz, morreu: true);
			}
			Checa("NOVE ASSASSINATOS param no PISO da escala (-100), e nao em -180",
				  algoz.Karma == Karma.Piso, $"{algoz.Karma}");

			for (int i = 0; i < 12; i++)
			{
				vilao.Karma = -40;
				vilao.KarmaDaMorteContado = false;
				AoPerderALuta(vilao, algoz, morreu: true);
			}
			Checa("...e doze cacadas param no TETO (+100), do outro lado",
				  algoz.Karma == Karma.Teto, $"{algoz.Karma}");

			// ============================ 6. CORPO SEM DONO NAO TEM MORAL ============================
			// Um habitante que mata outro habitante nao pode encher o save de ninguem com um numero
			// que nada consulta -- e matar um habitante tem conta PROPRIA (-5), que nao e esta.
			ServerPlayer bicho = Forjar(4, "bancada karma: o habitante");
			MoldeDeNpc? molde = _moldes?.Get("cidadao");
			if (molde != null) bicho.Papel = new PapelDeNpc(molde, 1);
			bicho.Karma = Karma.Neutro;
			algoz.Karma = Karma.Neutro;
			inocente.KarmaDaMorteContado = false;
			AoPerderALuta(inocente, bicho, morreu: true);
			Checa("UM NPC QUE MATA NAO GANHA KARMA (o `src.Player` do DM)",
				  molde == null || bicho.Karma == Karma.Neutro, $"{bicho.Karma}");

			bicho.KarmaDaMorteContado = false;
			AoPerderALuta(bicho, algoz, morreu: true);
			Checa("...e MATAR UM NPC nao passa pela conta de PK (o `victim.Player`): ela e outra, e vale -5",
				  molde == null || algoz.Karma == Karma.Neutro, $"{algoz.Karma}");
			_players.Remove(bicho.Id);
			ZoneList(bicho.Zone.Hash).Remove(bicho);

			// ============================ 7. AS DUAS FONTES QUE NAO SAO PK ============================
			// Os dois metodos que os ganchos de reputacao chamam. Eles nao passam pelo funil de
			// derrota -- moram no `MorreuUmCorpoSemDono` e no `PagarOHeroi` --, entao sao chamados
			// aqui do mesmo jeito que la.
			algoz.Karma = Karma.Neutro;
			KarmaPorMatarHabitante(algoz);
			Checa("CEIFAR UM HABITANTE custa 5 (`KARMA_NPC_INNOCENT_LOSS`), um quarto de um jogador",
				  algoz.Karma == -Karma.PorMatarInocente, $"{algoz.Karma}");

			algoz.Karma = Karma.Neutro;
			KarmaPorDerrotarChefe(algoz);
			Checa("DERRUBAR UM CHEFE DE SAGA vale +30 (`KARMA_BOSS_GAIN`) -- o unico caminho LIMPO",
				  algoz.Karma == Karma.PorDerrotarChefe, $"{algoz.Karma}");

			// ============================ 8. O DISCO ============================
			// A metade do buraco que ninguem via. O campo nao estava no `CharacterSave`: tudo isto
			// acima acontecia e morria no logout, calado.
			algoz.Karma = 73;
			CharacterSave foto = AccountStore.DeJogador(algoz, NowMs());
			Checa("o karma ENTRA no save (o campo nao existia -- ele morria no logout)",
				  foto.Karma == 73, $"{foto.Karma}");

			algoz.Karma = 0;
			AccountStore.ParaJogador(foto, algoz);
			Checa("...e VOLTA do save igual",
				  algoz.Karma == 73, $"{algoz.Karma}");

			foto.Karma = 5_000;   // save adulterado, ou de uma versao com outra escala
			AccountStore.ParaJogador(foto, algoz);
			Checa("...e um save FORA DA ESCALA e aparado na carga, e nao vira um karma impossivel",
				  algoz.Karma == Karma.Teto, $"{algoz.Karma}");

			// ============================ 9. A CONSEQUENCIA: O CARGO ABRE ============================
			// **A SECAO QUE JUSTIFICA O ARQUIVO.** Com o unico produtor que existia (`+5 por tarefa
			// de cargo`, que so quem JA TEM cargo recebe) o karma era inalcancavel -- e com o campo
			// fora do disco, ele nem se acumulava. Nove cargos pediam karma e nenhum abria.
			//
			// A conta e feita pelo `OqueFalta` DE PRODUCAO, o mesmo que o painel de cargos desenha.
			//
			// ============================ POR QUE O EREMITA TARTARUGA, E NAO O GUARDIAO ============================
			// **A PRIMEIRA VERSAO DESTA SECAO USAVA O GUARDIAO DA TERRA E REPROVOU** -- e a reprovacao
			// foi honesta e ensinou: o `OqueFalta` devolve **a PRIMEIRA regra que falha**, e a primeira
			// do Guardiao e *"ser um Namekuseijin do Cla do Dragao"*. O karma dele nunca chegava a ser
			// mencionado, entao a checagem media a frase errada.
			//
			// O Eremita Tartaruga pede duas coisas e so duas -- karma 25+ e 1M de BP base --, e o corpo
			// desta bancada tem 5M. Ou seja: **o karma e o unico ferrolho**, e e exatamente isso que
			// esta secao precisa que ele seja pra a prova valer.
			// ================================================================================================
			RankDef? tartaruga = Cargos.Get("turtle");
			if (tartaruga != null)
			{
				algoz.Karma = Karma.Neutro;
				string faltaAntes = Cargos.OqueFalta(tartaruga, Ficha(algoz)) ?? "";
				Checa("com karma NEUTRO o Eremita Tartaruga TRANCA (era isto que travava nove cargos)",
					  faltaAntes.Contains("karma", StringComparison.OrdinalIgnoreCase), faltaAntes);

				// UMA SAGA DERRUBADA: +30, acima dos 25 que o cargo pede. Pelo METODO DE PRODUCAO, e
				// nao escrevendo 30 no campo -- o caminho tem que existir de verdade.
				KarmaPorDerrotarChefe(algoz);
				string faltaDepois = Cargos.OqueFalta(tartaruga, Ficha(algoz)) ?? "";
				Checa("UMA SAGA DERRUBADA (+30) ABRE o Eremita Tartaruga -- o circulo esta quebrado",
					  algoz.Karma == Karma.PorDerrotarChefe && faltaDepois.Length == 0,
					  $"karma {algoz.Karma}; falta: '{faltaDepois}'");
			}

			// E O LADO PODRE DA ESCALA, que tinha os mesmos dois cargos inalcancaveis (-50). O Lorde
			// Demonio pede karma -50 e 5M de BP, e o corpo tem os 5M -- de novo, o karma e o ferrolho.
			RankDef? lorde = Cargos.Get("demonlord");
			if (lorde != null)
			{
				algoz.Karma = Karma.Neutro;
				bool cobravaAntes = (Cargos.OqueFalta(lorde, Ficha(algoz)) ?? "")
					.Contains("karma", StringComparison.OrdinalIgnoreCase);

				for (int i = 0; i < 3; i++)   // tres inocentes = -60, abaixo dos -50 que ele pede
				{
					inocente.KarmaDaMorteContado = false;
					AoPerderALuta(inocente, algoz, morreu: true);
				}
				string faltaLorde = Cargos.OqueFalta(lorde, Ficha(algoz)) ?? "";
				Checa("e TRES ASSASSINATOS (-60) ABREM o Lorde Demonio, que pedia -50 e era inalcancavel",
					  cobravaAntes && faltaLorde.Length == 0 && algoz.Karma == -60,
					  $"karma {algoz.Karma}; falta: '{faltaLorde}'");
			}
		}
		finally
		{
			foreach (ServerPlayer p in new[] { algoz, inocente, vilao })
			{
				_players.Remove(p.Id);
				ZoneList(p.Zone.Hash).Remove(p);
			}
			GD.Print("[karma] os corpos da bancada sairam do mundo.");
		}
	}

	// =====================================================================
	// O JULGAMENTO DO ENMA -- karma negro vai pro Inferno, e la fica
	// =====================================================================
	/// <summary>
	/// `enma_judge_to_hell()` + `afterlife_alignment_check()` (`SkyNPCs.dm:176-182, 228-236`), medidos no
	/// host: a pena pela formula, o corpo no portao, a passagem que devolve, a pena cumprida que limpa o
	/// karma, e o milhao que nao compra a volta de um coracao negro. Tudo fotografado e devolvido no `finally`.
	/// </summary>
	private void OJulgamentoDoEnma(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		int karmaAntes = pl.Karma;
		bool mortoAntes = pl.Ficha.dead, viajouAntes = pl.MorteJaViajou;
		long penaAntes = pl.Ficha.hell_lockout_until, relogioAntes = pl.RelogioDaMorte;
		double zeniAntes = pl.Ficha.Zeni;
		ZoneKey zonaAntes = pl.Zone;
		Vec2 posAntes = pl.Pos;
		try
		{
			Checa("A PENA E A DO DM: karma -100 = 1 h, -50 = 30 min, -1 = 1 min (piso), 0 = nada (`SkyNPCs.dm:177-178`)",
				  Alem.MsDePenaNoInferno(-100) == 3_600_000 && Alem.MsDePenaNoInferno(-50) == 1_800_000
				  && Alem.MsDePenaNoInferno(-1) == 60_000 && Alem.MsDePenaNoInferno(0) == 0 && Alem.MsDePenaNoInferno(80) == 0,
				  $"{Alem.MsDePenaNoInferno(-100)}/{Alem.MsDePenaNoInferno(-50)}/{Alem.MsDePenaNoInferno(-1)}/{Alem.MsDePenaNoInferno(0)}");

			ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo), inferno = ZoneKey.Premade(Alem.ZonaDoInferno);
			Obra? enma = _noChao.FirstOrDefault(o => o.Tipo == Alem.TipoDoEnma && o.Zona.Equals(alem));
			Checa("(montagem) o Enma esta na mesa dele", enma != null, "");
			if (enma == null) return;
			Vec2 naMesa = new(enma.X + ZoneCollision.TileSize, enma.Y);

			// morto de pe, na mesa, alma NEUTRA: fica
			pl.Ficha.dead = true; pl.MorteJaViajou = true; pl.RelogioDaMorte = long.MaxValue; pl.Ficha.hell_lockout_until = 0;
			MoveToZone(pl.Id, alem, naMesa);
			pl.Karma = Karma.Neutro;
			EnmaOuvir(pl);
			Checa("alma NEUTRA (karma 0): o Enma le a ficha e ela FICA no Outro Mundo, sem pena",
				  pl.Zone.Equals(alem) && pl.Ficha.hell_lockout_until == 0, $"zona {pl.Zone.Name}, pena {pl.Ficha.hell_lockout_until}");

			// coracao negro: Inferno, 30 min
			pl.Karma = -50;
			long t0 = NowMs();
			EnmaOuvir(pl);
			Vec2 portao = PortaoDoInferno(inferno);
			Checa("CORACAO NEGRO (karma -50): o Enma a manda pro INFERNO, no portao do DM (65,258), com 30 min de pena",
				  pl.Zone.Equals(inferno) && Vec2.Distance(pl.Pos, portao) < 1f
				  && pl.Ficha.hell_lockout_until >= t0 + 1_800_000 - 50 && pl.Ficha.hell_lockout_until <= t0 + 1_800_000 + 5_000,
				  $"zona {pl.Zone.Name}, pena em {(pl.Ficha.hell_lockout_until - t0) / 60_000.0:0.#} min");

			// tentar sair pela passagem: volta pro portao
			if (_passagens.TryGetValue(inferno.Name, out List<Passagem>? saidas) && saidas.Count > 0)
			{
				Passagem saida = saidas[0];
				const int T = ZoneCollision.TileSize;
				pl.Pos = new Vec2(saida.X * T + T / 2f, saida.Y * T + T / 2f - MoveRules.FeetOffsetY);
				_acabouDeAtravessar.Remove(pl.Id);
				TickDasPassagens();
				Checa("...e TENTAR SAIR pela passagem do Inferno a devolve ao portao (sempre que tentar sair, volta)",
					  pl.Zone.Equals(inferno) && Vec2.Distance(pl.Pos, portao) < 1f, $"zona {pl.Zone.Name} pos {pl.Pos}");
			}
			else Checa("(montagem) o Inferno tem a passagem de saida no mapa", false, "sem passagens em z09");

			// fora do Inferno por outro caminho (um admin, uma tecnica): o tique devolve
			MoveToZone(pl.Id, alem, naMesa);
			CumprirPenaNoInferno(pl);
			Checa("...e sair por QUALQUER caminho tambem devolve (o tique do `afterlife_alignment_check`)",
				  pl.Zone.Equals(inferno), pl.Zone.Name);

			// pena cumprida: alma limpa, de volta ao checkpoint
			pl.Ficha.hell_lockout_until = NowMs() - 1;
			CumprirPenaNoInferno(pl);
			Checa("PENA CUMPRIDA: a alma sai LIMPA (karma 0) e volta ao checkpoint do Outro Mundo (`SkyNPCs.dm:230-234`)",
				  pl.Ficha.hell_lockout_until == 0 && pl.Karma == Karma.Neutro && pl.Zone.Equals(alem),
				  $"pena {pl.Ficha.hell_lockout_until} karma {pl.Karma} zona {pl.Zone.Name}");

			// o milhao nao compra a volta de um coracao negro
			pl.Pos = naMesa;
			pl.Karma = -30;
			pl.Ficha.Zeni = 2_000_000;
			EnmaReviverPorZeni(pl);
			Checa("o MILHAO nao compra a volta de um coracao negro: o Enma julga antes de vender (continua morto, zeni intacto, no Inferno)",
				  pl.Ficha.dead && pl.Zone.Equals(inferno) && Math.Abs(pl.Ficha.Zeni - 2_000_000) < 1e-6,
				  $"morto={pl.Ficha.dead} zona={pl.Zone.Name} zeni={pl.Ficha.Zeni}");
		}
		finally
		{
			pl.Ficha.hell_lockout_until = penaAntes;
			pl.Karma = karmaAntes;
			pl.Ficha.dead = mortoAntes;
			pl.MorteJaViajou = viajouAntes;
			pl.RelogioDaMorte = relogioAntes;
			pl.Ficha.Zeni = zeniAntes;
			if (!pl.Zone.Equals(zonaAntes)) MoveToZone(pl.Id, zonaAntes, posAntes); else pl.Pos = posAntes;
		}
	}
}
