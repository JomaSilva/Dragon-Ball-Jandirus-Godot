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
}
