using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA DIMENSAO MENTAL -- `--menteteste`. Porte de
/// `Code/Modules/Stats/Training/MindMeditate.dm` (Camada 2).
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// As tres regras do pedido do dono (*"os FERIMENTOS NAO SAO TRANSFERIDOS pro corpo real, NAO TEM
/// COMO TER ZENKAI e o GANHO DE BP E REDUZIDO"*) sao cobradas em **tres funis diferentes**, e nenhum
/// deles fica no arquivo da mente:
///
///   * a ferida -- `RestaurarDaMente`, na saida;
///   * o Zenkai (e a RAIVA) -- o gate do `AoPerderALuta`, em `GameServer.Convivio.cs`;
///   * o ganho -- `Core/World/RitmoDaZona.cs`, escrito pelo `AplicarGravidade`.
///
/// Cada um deles pode estar certo sozinho e desligado do outro -- que e o modo de falha mais
/// repetido deste port ("a regra escrita de um lado do fio e sem chamador do outro"). O que esta
/// bancada mede e a CORRENTE: um corpo posto DENTRO de uma mente, pelos caminhos de producao, e a
/// pergunta feita a cada funil.
///
/// E ela mede pelos **dois lados**: toda afirmacao de "nao acontece na mente" tem uma irma que prova
/// que a mesma chamada, FORA da mente, acontece. Sem essa metade, um `return` no lugar errado
/// deixaria a bancada verde medindo o nada.
/// ==============================================================================
///
/// ============================ O QUE ESTA BANCADA **NAO** MEDE, E POR QUE ============================
/// A PORTA (`EntrarNaMente` -> `LargarOCorpo`). O mecanismo do corpo largado exige `Peer != null` --
/// e a recusa e deliberada, esta escrita la ("corpo sem dono nao tem atencao pra mandar embora") e
/// nao vai ser afrouxada por causa de um teste. Corpos forjados nao tem `Peer`.
///
/// Quem exercita a porta e o robo: `--socar --mente N` (`Client/RoboDeSoco.cs`) entra na mente por
/// um cliente de verdade. O que ESTA bancada faz e assumir o corpo ja la dentro -- que e exatamente
/// o estado que a porta produz -- e cobrar tudo o que vem depois.
/// ================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Faixa de ids de bancada -- bem longe do `_nextId`, e das outras bancadas.</summary>
	private const int IdBaseDaMenteDeTeste = 90_700;

	private int _mntOk, _mntFalhou;

	private void AfirmarMnt(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _mntOk++; GD.Print($"[mente]   OK    {oque}"); return; }
		_mntFalhou++;
		GD.PrintErr($"[mente]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaMente()
	{
		_mntOk = _mntFalhou = 0;
		GD.Print("[mente] ================ A DIMENSAO MENTAL ================");

		// A zona "de fora" e um pre-feito onde nao ha ninguem conectado -- mesmo cuidado das outras
		// bancadas que forjam corpo: nada do que acontece aqui pode cair na tela de alguem.
		ZoneKey fora = ZoneKey.Premade(
			Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		var forjados = new List<ServerPlayer>();

		ServerPlayer Forjar(int i, string nome, ZoneKey zona, Vec2 pos, double bp)
		{
			var novo = new ServerPlayer
			{
				Id = IdBaseDaMenteDeTeste + i,
				Peer = null,
				Name = nome,
				Race = "Saiyan",           // Zenkai exige DNA Saiyajin: sem isto a regra 2 daria verde por ausencia
				Genero = "Male",
				Idade = 25,
				Zone = zona,
				Pos = pos,
				// CONTA E SLOT dao ASSINATURA, e sem assinatura o `LutoNaVizinhanca` sai na primeira
				// linha (`EhPessoa`) -- a regra 5 mediria o nada.
				Conta = $"bancada_mente_{i}",
				Slot = 0,
				Ficha = new Fighter { Race = "Saiyan", BP = bp },
			};
			novo.Ficha.Class = "Normal";
			PorNoMundo(novo);
			novo.Ficha.Ki = novo.Ficha.MaxKi;
			forjados.Add(novo);
			return novo;
		}

		try
		{
			ORitmoDaZona(fora);
			AFotoEORestauro(Forjar(1, "bancada: o meditador", fora, new Vec2(0, 0), 100_000));
			SemZenkaiESemRaivaNaMente(Forjar, fora);
			OVisitanteAchaAMenteAoLado(Forjar, fora);
			OChefeJaVisto(Forjar, fora);
			OReflexoSeReergue(Forjar);
			OReflexoEODonoDeAgora(Forjar);
		}
		catch (Exception e)
		{
			AfirmarMnt($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			foreach (ServerPlayer p in forjados)
			{
				ZoneList(p.Zone.Hash).Remove(p);
				_players.Remove(p.Id);
				// O corpo que a mente dele tenha erguido sai junto -- senao a bancada deixa um NPC
				// dirigido parado num bolso pra sempre.
				if (p.CloneId != 0 && _players.TryGetValue(p.CloneId, out ServerPlayer? c)) RemoverNpc(c);
			}
		}

		GD.Print($"[mente] ================ {_mntOk} OK, {_mntFalhou} FALHA(S) ================");
	}

	// =====================================================================
	// 1. REGRA 3 -- O GANHO REDUZIDO, NO FUNIL DO RITMO DE ZONA
	// =====================================================================
	/// <summary>
	/// A CONSTANTE ESTAVA DECLARADA E ORFA. `GainKnobs.MindDimensionMult = 0.25` existia desde que o
	/// clone foi portado e **nao tinha um unico leitor** -- o "escrever a regra e nao aplica-la" que
	/// este projeto ja pagou com a API de sigilo de BP inteira.
	///
	/// As tres afirmacoes daqui sao as tres maneiras de isso dar errado: nao ligar, ligar e nao
	/// desligar, e ligar por cima da Sala do Tempo.
	/// </summary>
	private void ORitmoDaZona(ZoneKey fora)
	{
		GD.Print("[mente] --- 1. o ritmo da zona (regra 3: ganho reduzido) ---");
		var f = new Fighter { Race = "Saiyan", BP = 1000 };

		RitmoDaZona.Aplicar(f, DimensaoMental.De(1234));
		AfirmarMnt($"DENTRO da mente o ganho e {GainKnobs.MindDimensionMult}x",
				   Math.Abs(f.zoneGainMult - GainKnobs.MindDimensionMult) < 1e-9, $"{f.zoneGainMult}");

		// A MAESTRIA **NAO** MUDA la dentro (o `mind_gain_mult` do DM so entra nos `*_Gain`).
		AfirmarMnt("a maestria de forma nao e tocada pela mente",
				   Math.Abs(f.zoneMasteryMult - 1) < 1e-9, $"{f.zoneMasteryMult}");

		// LIGAR E NAO DESLIGAR e o modo de falha nomeado no cabecalho do `RitmoDaZona`.
		RitmoDaZona.Aplicar(f, fora);
		AfirmarMnt("SAIR da mente devolve o ganho a 1x (nao ha 0,25 preso no bolso)",
				   Math.Abs(f.zoneGainMult - 1) < 1e-9, $"{f.zoneGainMult}");

		// E A SALA CONTINUA VALENDO 280 -- a excecao da mente nao pode ter comido a da Sala.
		RitmoDaZona.Aplicar(f, ZoneKey.Premade(SalaDoTempo.Zona));
		AfirmarMnt($"a Sala do Tempo continua em {GainKnobs.TimeChamberMult}x",
				   Math.Abs(f.zoneGainMult - GainKnobs.TimeChamberMult) < 1e-9, $"{f.zoneGainMult}");

		// E O CAMINHO DE PRODUCAO: quem NASCE numa mente ja sai daqui com 0,25, porque o
		// `AplicarGravidade` (o funil de toda troca de zona) e quem chama o `Aplicar`.
		var dentro = new ServerPlayer
		{
			Id = IdBaseDaMenteDeTeste + 90, Peer = null, Name = "bancada: nascido na mente",
			Race = "Saiyan", Genero = "Male", Idade = 25,
			Zone = DimensaoMental.De(4321), Pos = PosMental,
			Ficha = new Fighter { Race = "Saiyan", BP = 1000 },
		};
		PorNoMundo(dentro);
		AfirmarMnt("o funil de producao (`AplicarGravidade`) escreve o 0,25 sozinho",
				   Math.Abs(dentro.Ficha.zoneGainMult - GainKnobs.MindDimensionMult) < 1e-9,
				   $"{dentro.Ficha.zoneGainMult}");
		ZoneList(dentro.Zone.Hash).Remove(dentro);
		_players.Remove(dentro.Id);
	}

	// =====================================================================
	// 2. REGRA 1 -- A FERIDA NAO VOLTA
	// =====================================================================
	private void AFotoEORestauro(ServerPlayer pl)
	{
		GD.Print("[mente] --- 2. a foto e o restauro (regra 1: ferida nao volta) ---");

		// O CORPO ENTRA JA MACHUCADO, e isso e o ponto: restaurar nao pode ser "curar". Se o restauro
		// chamasse `Levantar()`/`Reviver()` -- que curam --, esta afirmacao e a unica que pegaria.
		BodyPart braco = pl.Combate.Corpo.Partes.First(p => p.Nome != "Rabo");
		braco.Vida = braco.VidaMax * 0.6;
		pl.Combate.SincronizarVida();
		pl.Ficha.Ki = pl.Ficha.MaxKi * 0.5;
		pl.Ficha.stamina = pl.Ficha.maxstamina * 0.5;

		double vidaAntes = braco.Vida, kiAntes = pl.Ficha.Ki, hpAntes = pl.Ficha.HP;
		FotografarParaAMente(pl);

		// A LUTA NA MENTE: outro membro quebra, um e DECEPADO, o Ki vai a zero e o corpo cai.
		braco.Vida = braco.VidaMax * 0.05;
		BodyPart perna = pl.Combate.Corpo.Partes.Last(p => p.Nome != "Rabo" && p != braco);
		perna.Decepado = true;
		pl.Ficha.Ki = 0;
		pl.Ficha.stamina = 0;
		pl.Combate.Nocautear(30);
		pl.Combate.SincronizarVida();

		RestaurarDaMente(pl);

		AfirmarMnt("o membro ferido volta ao valor da ENTRADA (e nao curado)",
				   Math.Abs(braco.Vida - vidaAntes) < 1e-6, $"{braco.Vida} != {vidaAntes}");
		AfirmarMnt("o membro DECEPADO na mente volta inteiro", !perna.Decepado);
		AfirmarMnt("o Ki volta ao da entrada", Math.Abs(pl.Ficha.Ki - kiAntes) < 1e-6,
				   $"{pl.Ficha.Ki} != {kiAntes}");
		AfirmarMnt("o folego volta ao da entrada",
				   Math.Abs(pl.Ficha.stamina - pl.Ficha.maxstamina * 0.5) < 1e-6, $"{pl.Ficha.stamina}");
		AfirmarMnt("o nocaute sofrido na mente nao atravessa a saida", !pl.Ficha.KO);
		AfirmarMnt("o cronometro do nocaute desfeito tambem zera",
				   pl.Combate.NocauteRestante <= 0, $"{pl.Combate.NocauteRestante}");

		// O HP **NAO** ESTA NA FOTO: ele e derivado dos membros. Esta linha e a prova de que a
		// derivacao funciona -- se alguem "consertar" isso guardando o HP, os dois podem divergir.
		AfirmarMnt("o HP volta sozinho (derivado dos membros, sem estar na foto)",
				   Math.Abs(pl.Ficha.HP - hpAntes) < 1e-6, $"{pl.Ficha.HP} != {hpAntes}");
		AfirmarMnt("a foto e consumida na saida (nao restaura duas vezes)", pl.FotoDaMente == null);

		// E MORRER NA MENTE NAO E MORRER -- `mind_monitor` (`:448-451`).
		FotografarParaAMente(pl);
		pl.Ficha.dead = true;
		RestaurarDaMente(pl);
		AfirmarMnt("morrer na mente nao e morrer", !pl.Ficha.dead);
	}

	// =====================================================================
	// 3. REGRAS 2 E 5 -- SEM ZENKAI, E SEM RAIVA DE VERDADE
	// =====================================================================
	/// <summary>
	/// O gate mora no <see cref="AoPerderALuta"/> -- o funil unico da derrota --, e por isso as duas
	/// regras sao provadas pela MESMA chamada.
	///
	/// AS DUAS METADES: dentro da mente nada acontece; **na mesma cena, do lado de fora, tudo
	/// acontece**. Sem a segunda, um erro de sinal no `if` daria verde.
	/// </summary>
	private void SemZenkaiESemRaivaNaMente(
		Func<int, string, ZoneKey, Vec2, double, ServerPlayer> forjar, ZoneKey fora)
	{
		GD.Print("[mente] --- 3. sem Zenkai e sem raiva (regras 2 e 5) ---");

		ZoneKey mente = DimensaoMental.De(IdBaseDaMenteDeTeste + 10);
		ServerPlayer vitima = forjar(10, "bancada: quem perde na mente", mente, PosMental, 100_000);
		ServerPlayer algoz = forjar(11, "bancada: o reflexo", mente, PosMental + new Vec2(64, 0), 5_000_000);

		// A TESTEMUNHA fica colada na vitima: e ela que provaria a raiva se o gate falhasse. AMIGA
		// dela (a queda so move quem era muito proximo -- `Convivio.LutoPorQueda`), e o algoz e
		// inimigo por PADRAO (quem nao e amigo nem tem relacao declarada ja e "inimigo aos olhos da
		// vitima"), entao nao ha nada a escrever daquele lado.
		ServerPlayer plateia = forjar(12, "bancada: quem assiste", mente, PosMental + new Vec2(32, 0), 1_000);
		plateia.Social.Amizade[vitima.Assinatura] = Jandirus.Core.Social.Convivio.ExigenciaDeAmigo;

		vitima.Ficha.zenkaiReady = 0;
		double bpAntes = vitima.Ficha.BP;

		AoPerderALuta(vitima, algoz, morreu: false);

		AfirmarMnt("DENTRO da mente, perder nao paga Zenkai",
				   Math.Abs(vitima.Ficha.BP - bpAntes) < 1e-6, $"{vitima.Ficha.BP} != {bpAntes}");
		// A RAIVA E A JANELA, e nao o `Anger`: o `Anger` e derivado e projetado por tique, a janela e
		// o que `AcenderJanelaDeRaiva` escreve. Medir o derivado deixaria a afirmacao a merce do
		// relogio do tique.
		AfirmarMnt("DENTRO da mente, ver um amigo cair nao acende raiva",
				   plateia.RaivaLendariaAte == 0, $"{plateia.RaivaLendariaAte}");

		// ============================ A OUTRA METADE: A MESMA CENA, LA FORA ============================
		// Os mesmos tres corpos, os mesmos vinculos, a mesma chamada -- so o LUGAR muda. Se esta
		// metade nao acender, o verde de cima nao vale nada: significaria que a cena nunca produziria
		// Zenkai nem raiva em lugar nenhum.
		// ==========================================================================================
		foreach (ServerPlayer p in new[] { vitima, algoz, plateia })
		{
			ZoneList(p.Zone.Hash).Remove(p);
			p.Zone = fora;
			ZoneList(fora.Hash).Add(p);
		}
		vitima.Ficha.zenkaiReady = 0;
		bpAntes = vitima.Ficha.BP;

		AoPerderALuta(vitima, algoz, morreu: false);

		AfirmarMnt("FORA da mente a mesma cena paga Zenkai",
				   vitima.Ficha.BP > bpAntes + 1e-6, $"{vitima.Ficha.BP} == {bpAntes}");
		AfirmarMnt("FORA da mente a mesma cena acende raiva",
				   plateia.RaivaLendariaAte > 0, $"{plateia.RaivaLendariaAte}");
	}

	// =====================================================================
	// 4. O VISITANTE
	// =====================================================================
	/// <summary>
	/// *"se um player meditasse AO SEU LADO ele entraria na sua mente"*.
	///
	/// O que se mede aqui e a PERGUNTA (`MenteAoLado`), que e a parte derivada e portanto a parte que
	/// pode estar errada em silencio: ela tem que achar o corpo largado de quem esta em transe,
	/// **ignorar** o corpo largado de quem esta pilotando (o mesmo boneco, outro sistema) e respeitar
	/// o raio do `oview(1)`.
	/// </summary>
	private void OVisitanteAchaAMenteAoLado(
		Func<int, string, ZoneKey, Vec2, double, ServerPlayer> forjar, ZoneKey fora)
	{
		GD.Print("[mente] --- 4. o visitante acha a mente ao lado ---");

		// O ANFITRIAO ja esta em transe: o corpo dele esta LA FORA e ele esta na propria mente.
		ServerPlayer anfitriao = forjar(20, "bancada: o anfitriao", DimensaoMental.De(IdBaseDaMenteDeTeste + 20),
										PosMental, 100_000);
		ServerPlayer boneco = forjar(21, "bancada: o corpo do anfitriao", fora, new Vec2(500, 500), 1);
		boneco.DonoDoCorpoLargado = anfitriao.Id;

		ServerPlayer visitante = forjar(22, "bancada: o visitante", fora, new Vec2(500 + 32, 500), 100_000);

		AfirmarMnt("colado no corpo de quem medita, a mente encontrada e a DELE",
				   MenteAoLado(visitante) == anfitriao);

		// O RAIO: um tile e meio. A quatro tiles nao ha vizinho nenhum.
		visitante.Pos = new Vec2(500 + 4 * 32, 500);
		AfirmarMnt("longe do corpo, nao ha mente ao lado", MenteAoLado(visitante) == null);
		visitante.Pos = new Vec2(500 + 32, 500);

		// O MESMO BONECO SERVE AO LEME DA NAVE, e ali nao ha mente pra visitar -- e o `wake_mode == 2`
		// do DM, que aqui e derivado de ONDE o dono esta.
		ZoneList(anfitriao.Zone.Hash).Remove(anfitriao);
		anfitriao.Zone = fora;
		ZoneList(fora.Hash).Add(anfitriao);
		AfirmarMnt("corpo largado por quem NAO esta na mente (o leme) nao e uma porta",
				   MenteAoLado(visitante) == null);

		// E A ZONA DA MENTE E A DO ANFITRIAO -- a chave carrega o dono, entao os dois cabem no mesmo
		// bolso sem nenhum codigo de instanciamento.
		ZoneKey dele = DimensaoMental.De(anfitriao.Id);
		AfirmarMnt("a mente do anfitriao aponta pra ele (o dono sai da propria chave)",
				   DimensaoMental.Anfitriao(dele) == anfitriao.Id, $"{DimensaoMental.Anfitriao(dele)}");
		AfirmarMnt("uma zona comum nao e mente nenhuma",
				   !DimensaoMental.EhAMente(fora) && DimensaoMental.Anfitriao(fora) == 0);
	}

	// =====================================================================
	// 5. O CHEFE JA VISTO
	// =====================================================================
	private void OChefeJaVisto(Func<int, string, ZoneKey, Vec2, double, ServerPlayer> forjar, ZoneKey fora)
	{
		GD.Print("[mente] --- 5. o chefe ja visto ---");

		MoldeDeNpc? molde = _moldes?.Todos.FirstOrDefault(m => m.EhChefe);
		if (molde == null)
		{
			AfirmarMnt("ha algum molde de chefe no npcs.json pra medir", false, "nenhum");
			return;
		}

		// ============================ A TESTEMUNHA: O FUNIL E O `TickDoRoteiro` ============================
		// Nasce um chefe de verdade, pelo caminho de producao, e o laco do roteiro (nao o monitor da
		// saga) e quem anota quem o viu. Um corpo colado ve; um a 40 tiles nao ve.
		// ==============================================================================================
		ulong lugar = 990_001;
		ServerPlayer? chefe = NascerNpc(molde.Id, fora, new Vec2(2000, 2000), lugar);
		if (chefe == null) { AfirmarMnt($"o chefe '{molde.Id}' nasceu pra bancada", false); return; }

		ServerPlayer perto = forjar(30, "bancada: quem viu o chefe", fora, new Vec2(2000 + 96, 2000), 1_000);
		ServerPlayer longe = forjar(31, "bancada: quem estava longe", fora, new Vec2(2000 + 40 * 32, 2000), 1_000);

		TickDoRoteiro();

		AfirmarMnt("quem estava perto do chefe passa a te-lo visto",
				   perto.ChefesVistos.Contains(molde.Id), string.Join(",", perto.ChefesVistos));
		AfirmarMnt("quem estava a 40 tiles NAO o viu", !longe.ChefesVistos.Contains(molde.Id));

		// UMA LEMBRANCA NAO VIRA LEMBRANCA DE SI MESMA: chefe dentro de uma mente nao conta.
		ZoneList(chefe.Zone.Hash).Remove(chefe);
		chefe.Zone = DimensaoMental.De(IdBaseDaMenteDeTeste + 40);
		ZoneList(chefe.Zone.Hash).Add(chefe);
		ServerPlayer dentro = forjar(32, "bancada: quem esta na mente", chefe.Zone, chefe.Pos, 1_000);
		TickDoRoteiro();
		AfirmarMnt("chefe DENTRO de uma mente nao vira lembranca nova",
				   !dentro.ChefesVistos.Contains(molde.Id));
		RemoverNpc(chefe);

		// ============================ A CONVOCACAO ============================
		ZoneKey mente = DimensaoMental.De(IdBaseDaMenteDeTeste + 33);
		ServerPlayer dono = forjar(33, "bancada: o dono da mente", mente, PosMental, 100_000);

		ChamarChefe(dono, molde.Id);
		AfirmarMnt("sem ter visto, a lembranca nao se ergue", dono.CloneId == 0);

		dono.ChefesVistos.Add(molde.Id);
		ChamarChefe(dono, molde.Id);
		AfirmarMnt("tendo visto, a lembranca se ergue na mente",
				   dono.CloneId != 0 && _players.ContainsKey(dono.CloneId), $"{dono.CloneId}");

		if (_players.TryGetValue(dono.CloneId, out ServerPlayer? corpo))
		{
			AfirmarMnt("ela nasce com a FICHA PRONTA do chefe (degraus do npcs.json)",
					   corpo.Papel is { EhChefe: true },
					   $"{corpo.Papel?.Molde.Id ?? "sem papel"}");
			AfirmarMnt("ela e do MESMO ramo de IA do reflexo (vem atras de voce)",
					   corpo.DonoDoClone == dono.Id, $"{corpo.DonoDoClone}");
			AfirmarMnt("ela nao decepa nem mata (`Letal = false`)", !corpo.Combate.Letal);
			AfirmarMnt("ela esta DENTRO da mente e nao no mundo", corpo.Zone.Hash == mente.Hash);
			AfirmarMnt("o BP dela esta pinado no vetor do papel",
					   corpo.Papel!.BpsPinados.Length > 0, "vetor vazio");

			// UM POR VEZ: convocar de novo desfaz o anterior.
			int antes = corpo.Id;
			ChamarChefe(dono, molde.Id);
			AfirmarMnt("convocar de novo desfaz a lembranca anterior (um corpo por mente)",
					   dono.CloneId != antes && !_players.ContainsKey(antes), $"{dono.CloneId} / {antes}");
		}

		// **SO NA PROPRIA MENTE**: um visitante nao chama lembranca na cabeca dos outros.
		ServerPlayer visita = forjar(34, "bancada: a visita", mente, PosMental + new Vec2(64, 0), 1_000);
		visita.ChefesVistos.Add(molde.Id);
		ChamarChefe(visita, molde.Id);
		AfirmarMnt("o visitante nao ergue lembranca na mente alheia", visita.CloneId == 0);

		// E COM VISITA NA MENTE, nem o dono ergue -- o `add_member` do DM apaga o NPC quando entra o
		// segundo, e nunca mais o recria enquanto houver dois.
		int tinha = dono.CloneId;
		ChamarChefe(dono, molde.Id);
		AfirmarMnt("com visita na mente, nem o dono ergue lembranca", dono.CloneId == tinha);

		// E FORA DE QUALQUER MENTE, ninguem ergue nada.
		ZoneList(dono.Zone.Hash).Remove(dono);
		dono.Zone = fora;
		ZoneList(fora.Hash).Add(dono);
		DesfazerOOponente(dono, "");
		ChamarChefe(dono, molde.Id);
		AfirmarMnt("fora da mente a convocacao e recusada", dono.CloneId == 0);
	}

	// =====================================================================
	// 6. O REFLEXO SE REERGUE -- o `clone_watch()` (`MindMeditate.dm:288-306`)
	// =====================================================================
	/// <summary>
	/// *"o NPC MENTAL VOLTANDO DPS DE UM TEMPO"* -- o pedido do dono.
	///
	/// ============================ AS QUATRO MANEIRAS DE ISTO DAR ERRADO ============================
	///   1. **nao voltar** -- a queda encerra o transe, que era o estado anterior deste port;
	///   2. **voltar na hora ERRADA** -- e o modo silencioso, e o unico que so uma medida pega. Ja foi
	///      "cedo demais" (o nocaute comum eram 12 s e o reflexo, 15); hoje o nocaute comum e o COMA do
	///      DM, com teto de 225 s (`MeleeResolver.TetoDoNocaute`), entao o risco virou **TARDE demais**:
	///      sem a REESCRITA daquele relogio o reflexo ficaria quase quatro minutos no chao. A bancada
	///      cerca os dois lados -- caido aos 13 s, de pe aos 16;
	///   3. **voltar MACHUCADO** -- o `Levantar()` quase nao cura (`SpreadHeal(25,1,1)` do `Un_KO`: 25
	///      pontos, so nos vitais abaixo de 70%). Um parceiro de treino que volta a 8% de vida cai no
	///      primeiro soco, e em tres rodadas o treino infinito virou saco de pancada;
	///   4. **voltar e nao encerrar nunca** -- se a MORTE tambem se reerguesse, nao haveria saida por
	///      vitoria nenhuma.
	///
	/// O RELOGIO E O DO MUNDO SIMULADO: a bancada chama os DOIS lacos de producao, na ordem em que o
	/// `Tick()` os chama (`TickCombate` conta o nocaute; `TickDosCorposSemDono` decide). Nao ha
	/// `Task.Delay` nem relogio de parede -- um `sleep` aqui mediria o agendador do sistema.
	/// ==========================================================================================
	/// </summary>
	private void OReflexoSeReergue(Func<int, string, ZoneKey, Vec2, double, ServerPlayer> forjar)
	{
		GD.Print("[mente] --- 6. o reflexo se reergue (o `clone_watch`) ---");

		ZoneKey mente = DimensaoMental.De(IdBaseDaMenteDeTeste + 50);
		ServerPlayer dono = forjar(50, "bancada: quem medita", mente, PosMental, 100_000);

		// PELO CAMINHO DE PRODUCAO: o mesmo `CriarClone` que a porta da mente chama.
		ServerPlayer reflexo = CriarClone(dono, mente);
		dono.CloneId = reflexo.Id;

		// O REFLEXO FICA FRACO DE PROPOSITO. Ele copia a ficha do dono, entao com o BP original os dois
		// se derrubariam durante os 15 s de medida -- e o que estaria sendo medido seria a borda de KO
		// do jogador (familia de outra bancada), nao a volta do reflexo.
		reflexo.Ficha.BP = 100;
		reflexo.Ficha.Statify();

		AfirmarMnt("o reflexo nasce de pe e sem queda pendente",
				   !reflexo.Ficha.KO && !reflexo.CaiuNaMente);

		// O ESTRAGO ANTES DA QUEDA: e ele que prova a CURA la embaixo. Um membro quase zerado e o Ki
		// no chao -- exatamente o estado em que um reflexo derrotado fica.
		BodyPart membro = reflexo.Combate.Corpo.Partes.First(p => p.Nome != "Rabo");
		membro.Vida = membro.VidaMax * 0.05;
		reflexo.Ficha.Ki = 0;
		reflexo.Combate.SincronizarVida();

		// A QUEDA VEM PELO FUNIL COMUM (12 s), e nao escrevendo `KO = true`: e justamente esse relogio
		// que o mecanismo tem que reescrever.
		reflexo.Combate.Nocautear(MeleeResolver.TetoDoNocaute);

		void UmTique()
		{
			TickCombate(Jandirus.Net.Protocol.TickSeconds);
			TickDosCorposSemDono(Jandirus.Net.Protocol.TickSeconds);
		}

		void Rodar(double segundos)
		{
			int n = (int)Math.Round(segundos / Jandirus.Net.Protocol.TickSeconds);
			for (int i = 0; i < n; i++) UmTique();
		}

		UmTique();
		AfirmarMnt("no primeiro tique caido o mecanismo assume a queda",
				   reflexo.CaiuNaMente, "o bit nao foi escrito");
		// ============================ A COMPARACAO VIROU DO AVESSO, E O MOTIVO E O NOCAUTE NOVO ============================
		// Isto afirmava `NocauteRestante > MeleeResolver.TetoDoNocaute`, quando aquele numero era 12 e o
		// reflexo levava 15: o reflexo ficava MAIS tempo no chao que o padrao. Agora o nocaute comum e o
		// COMA do DM com teto de 225 s, entao o reflexo fica MENOS -- e a mesma pergunta ("o relogio foi
		// reescrito?") tem que ser feita contra o numero do reflexo, e nao contra uma desigualdade cujo
		// lado forte mudou de mao. Confere as duas pontas: e o prazo dele, e nao e o teto comum.
		// ============================================================================================================
		AfirmarMnt($"...e o relogio do nocaute foi REESCRITO pro prazo do DM ({SegundosAteOReflexoSeReerguer:0}s)",
				   reflexo.Combate.NocauteRestante <= SegundosAteOReflexoSeReerguer
				   && reflexo.Combate.NocauteRestante > SegundosAteOReflexoSeReerguer - 1
				   && !reflexo.Combate.NocautePorVital,
				   $"{reflexo.Combate.NocauteRestante:0.00} (teto comum {MeleeResolver.TetoDoNocaute}, "
				   + $"porVital={reflexo.Combate.NocautePorVital})");
		AfirmarMnt("...e a queda NAO encerrou o transe (era o comportamento antigo)",
				   NaMente(dono) && dono.CloneId == reflexo.Id, $"{dono.Zone} / {dono.CloneId}");

		// AOS 13 S ELE AINDA TEM QUE ESTAR NO CHAO -- e agora quem discrimina e o Rodar(3) logo abaixo.
		// Enquanto o nocaute comum eram 12 s, este 13 era A afirmacao da familia. Com o teto em 225 s a
		// pergunta inverteu: ficar caido aos 13 e o que qualquer corpo faria; o que so o reflexo faz e
		// ESTAR DE PE aos 16 -- duzentos e nove segundos antes do teto. Os dois continuam aqui porque
		// juntos cercam o prazo pelos dois lados.
		Rodar(13);
		AfirmarMnt("aos 13 s ele CONTINUA no chao (o prazo dele nao venceu antes)",
				   reflexo.Ficha.KO && reflexo.CaiuNaMente, $"KO={reflexo.Ficha.KO}");
		AfirmarMnt("...e continua ferido (o `Levantar` nao cura, e ninguem curou por fora)",
				   membro.Vida < membro.VidaMax, $"{membro.Vida}/{membro.VidaMax}");

		Rodar(3);
		AfirmarMnt($"passados os 15 s ele esta de PE de novo -- {MeleeResolver.TetoDoNocaute - 16:0} s "
				   + "antes do teto do nocaute comum, que e o que prova a reescrita",
				   !reflexo.Ficha.KO);
		AfirmarMnt("...e o corpo e o MESMO (ele nao foi refeito, ele se reergueu)",
				   _players.ContainsKey(reflexo.Id) && dono.CloneId == reflexo.Id, $"{dono.CloneId}");
		AfirmarMnt("...e a queda foi dada por encerrada (o bit nao fica preso pro proximo KO)",
				   !reflexo.CaiuNaMente);
		AfirmarMnt("...INTEIRO: o membro esmagado voltou cheio (`SpreadHeal(150,1,1)`)",
				   Math.Abs(membro.Vida - membro.VidaMax) < 1e-6, $"{membro.Vida}/{membro.VidaMax}");
		AfirmarMnt("...e com o Ki cheio (`Ki = MaxKi`)",
				   reflexo.Ficha.MaxKi > 0 && Math.Abs(reflexo.Ficha.Ki - reflexo.Ficha.MaxKi) < 1e-6,
				   $"{reflexo.Ficha.Ki}/{reflexo.Ficha.MaxKi}");
		AfirmarMnt("...e o transe continua aberto (parceiro de treino infinito)",
				   NaMente(dono), $"{dono.Zone}");

		// ============================ A SEGUNDA QUEDA, pra provar que o ciclo se repete ============================
		// Um bit que nao se limpasse daria verde na primeira volta e travaria o reflexo no chao pra
		// sempre na segunda -- e a bancada que so medisse um ciclo nao veria.
		// ======================================================================================================
		reflexo.Combate.Nocautear(MeleeResolver.TetoDoNocaute);
		UmTique();
		AfirmarMnt("uma SEGUNDA queda reabre o ciclo", reflexo.CaiuNaMente);
		Rodar(16);
		AfirmarMnt("...e ele se reergue de novo", !reflexo.Ficha.KO && !reflexo.CaiuNaMente);

		// ============================ A OUTRA METADE: MORRER ENCERRA ============================
		// Sem ela, "ele sempre volta" nao teria saida por vitoria -- e a bancada de cima estaria
		// medindo um transe do qual nao se sai lutando.
		// ===================================================================================
		// MATA PELO FUNIL DE VERDADE, e nao escrevendo `dead = true` na ficha: quem arma o relogio da
		// morte e o gancho `CombatState.AoMorrer` (ver `GameServer.Alem.cs`), e um corpo com prazo
		// ZERO seria despachado pelo `TickCombate` -- que roda ANTES do laco dos corpos dirigidos --
		// antes de a mente sequer perceber a morte. A bancada mediria um estado que o jogo nao produz.
		// `ignorarSeguro: true` porque o reflexo nao tem por que negociar com nenhum seguro.
		reflexo.Combate.Morrer(ignorarSeguro: true);
		UmTique();

		// ============================ A ONDA PRECISA DRENAR, E ELA LEVA 1,8 s ============================
		// **VERMELHA ENCONTRADA, E ELA NAO E DA CURA.** Este ponto media a saida UM tique depois da
		// morte, e a saida deixou de ser seca: `ComecarAVoltaDaMente` (`GameServer.Mente.cs:516`) zera
		// o `CloneId` na hora mas **enfileira** a viagem -- *"a tela ondula 1,8 s e SO ENTAO o mundo
		// real volta"*. Quem drena a fila e o `TickDaOndaDaMente`, de dentro do `TickCombate`, no tique
		// SEGUINTE. Ou seja: a prova pedia o resultado antes de a onda existir, e ficava vermelha com o
		// sistema certo -- o detalhe que ela imprimia (`CloneId` ja em 0, zona ainda na mente) e
		// exatamente a assinatura disso.
		//
		// E A ESPERA TEM QUE SER NO RELOGIO DE VERDADE, e nao em tiques. Foi a segunda tentativa aqui:
		// `Rodar(2)` roda 60 tiques em microssegundos de relogio real, e o prazo da onda e
		// `NowMs()`-- ou seja **nenhum tempo de parede passa** e a fila nunca vence. Toda bancada de
		// tique simulado que encostar num sistema de relogio real cai nisso; a saida e esperar pelo
		// MESMO relogio que o sistema le, com um teto pra nao travar a rodada.
		// ============================================================================================
		long limiteDaOnda = NowMs() + 3000;
		while (NaMente(dono) && NowMs() < limiteDaOnda) UmTique();
		AfirmarMnt("MORRER (golpe letal) encerra o transe -- e a unica vitoria que termina a sessao",
				   !NaMente(dono) && dono.CloneId == 0, $"{dono.Zone} / {dono.CloneId}");
		AfirmarMnt("...e o corpo do reflexo saiu do mundo junto",
				   !_players.ContainsKey(reflexo.Id));
	}

	// =====================================================================
	// 7. O REFLEXO E O DONO DE AGORA -- a ancora (`EspelharODono`) e o pino
	// =====================================================================
	/// <summary>
	/// *"ele volta INTEIRO: ficha recopiada do dono no momento (se o dono ficou mais forte treinando,
	/// o clone acompanha). E BP pinado como o DM faz (`ai_no_powerup = 1`), senao o motor de NPC
	/// infla ele."* -- o pedido do dono, e sao DUAS afirmacoes opostas que precisam valer juntas:
	///
	///   * o BP do reflexo **segue o dono** -- e ele nao seguia: `ClonarFicha` era chamado no
	///     nascimento e mais nunca;
	///   * o BP do reflexo **nao anda sozinho** -- ninguem alem do espelho escreve nele.
	///
	/// A segunda e a mais dificil de medir porque hoje ela e verdade POR AUSENCIA: o surto de BP do
	/// original (`NPCAscension`/`BPBoost`) nao esta portado, e o reflexo nao treina (`Treinar` cai no
	/// `BufferTick` sem `train` nem `med`) nem ganha Zenkai (o gate do `AoPerderALuta`). Uma verdade
	/// por ausencia e exatamente a que morre calada quando alguem portar o surto -- entao ela vira
	/// medida aqui, rodando os lacos de producao com o reflexo de pe.
	///
	/// ============================ E A ANCORA E O **EXPRESSO**, NAO O BASE ============================
	/// A afirmacao que separa as duas coisas usa um dono com `BPBoost` -- ou seja, base e expresso
	/// diferentes por construcao. Com a conta antiga (`BP = f.BP`) ela fica vermelha; com a do DM
	/// (`mind_seed_bp = round(M.expressedBP)`) ela fecha. Sem esse degrau, medir "o reflexo tem o BP do
	/// dono" daria verde nos dois casos e nao mediria nada.
	/// ============================================================================================
	/// </summary>
	private void OReflexoEODonoDeAgora(Func<int, string, ZoneKey, Vec2, double, ServerPlayer> forjar)
	{
		GD.Print("[mente] --- 7. o reflexo e o dono de AGORA (ancora + pino) ---");

		ZoneKey mente = DimensaoMental.De(IdBaseDaMenteDeTeste + 60);
		ServerPlayer dono = forjar(60, "bancada: o dono que cresce", mente, PosMental, 50_000);

		// O DEGRAU QUE SEPARA BASE DE EXPRESSO. `BPBoost` e familia 1 (multiplica) no `powerlevel()`:
		// com ele o dono expressa 4x o proprio base, e as duas leituras deixam de coincidir.
		dono.Ficha.BPBoost = 4;
		dono.Ficha.Idade = 61;
		dono.Ficha.Statify();
		dono.Ficha.PowerLevel();

		ServerPlayer reflexo = CriarClone(dono, mente);
		dono.CloneId = reflexo.Id;

		// ============================ FORA DO ALCANCE, DE PROPOSITO ============================
		// Esta familia mede a ANCORA, e nao a luta -- e o reflexo espelhado tem exatamente o poder do
		// dono, entao quinze segundos de troca de golpes o nocauteariam. Dono caido tem `expressedBP`
		// de 10% (corpo exposto) e o espelho recusa a ancora ali por desenho: a bancada mediria a
		// briga e chamaria de erro a guarda que esta certa.
		//
		// 5000 px = 156 tiles. O reflexo passa a maior parte destes segundos no CHAO (15 dos 16 de
		// cada ciclo) e o resto andando; ele nunca chega. O caminho de producao e o mesmo -- ele
		// pensa, decide e anda pelas mesmas funcoes.
		// ==================================================================================
		reflexo.Pos = dono.Pos + new Vec2(5000, 0);

		AfirmarMnt("o reflexo nasce com o BP **EXPRESSO** do dono (forma atual inclusa)",
				   Math.Abs(reflexo.Ficha.BP - dono.Ficha.expressedBP) < 1,
				   $"reflexo {reflexo.Ficha.BP:N0} x expresso do dono {dono.Ficha.expressedBP:N0} "
				 + $"(base {dono.Ficha.BP:N0})");
		AfirmarMnt("...e nao o BP base (a conta antiga daria isto)",
				   Math.Abs(reflexo.Ficha.BP - dono.Ficha.BP) > 1, $"{reflexo.Ficha.BP:N0}");
		AfirmarMnt("...com a RACA e a IDADE do dono (entram no `AgeDiv` do `powerlevel`)",
				   reflexo.Ficha.Race == dono.Ficha.Race
				   && Math.Abs(reflexo.Ficha.Idade - dono.Ficha.Idade) < 1e-9,
				   $"'{reflexo.Ficha.Race}' / {reflexo.Ficha.Idade}");
		AfirmarMnt("...e inteiro: Ki cheio, folego cheio, de pe",
				   reflexo.Ficha.Ki > 0 && Math.Abs(reflexo.Ficha.Ki - reflexo.Ficha.MaxKi) < 1e-6
				   && !reflexo.Ficha.KO && !reflexo.Ficha.dead);

		// ============================ O TIQUE E O DE PRODUCAO, NA CADENCIA DE PRODUCAO ============================
		// `TickFichas` roda a 5 Hz no servidor (`_tickCount % TicksPorFicha`), e chamar a 30 Hz aqui
		// nao seria "medir mais": seria acelerar seis vezes a fome, a regeneracao e o treino de TODO
		// mundo que estiver online enquanto a bancada roda. Ele entra porque e nele que o motor
		// inflaria (`Treinar`, `Ficha.Tick`, o topo do servidor) -- mas na cadencia certa.
		// ====================================================================================================
		int passo = 0;
		void UmTique()
		{
			TickCombate(Jandirus.Net.Protocol.TickSeconds);
			TickDosCorposSemDono(Jandirus.Net.Protocol.TickSeconds);
			if (++passo % TicksPorFicha == 0) TickFichas();
		}

		void Rodar(double segundos)
		{
			int n = (int)Math.Round(segundos / Jandirus.Net.Protocol.TickSeconds);
			for (int i = 0; i < n; i++) UmTique();
		}

		// ============================ O PINO, MEDIDO NO MECANISMO E NAO NO RELOGIO ============================
		// A fonte real de deriva e o `Fighter.FightGain` (`Fighter.Training.cs:139`): o reflexo esta no
		// `_players`, entao lutar rende BP pra ele como pra qualquer um. So que rende em FRACOES --
		// dois segundos de troca de golpes movem menos de um ponto --, e uma bancada que apenas
		// ESPERASSE ficaria verde com o pino arrancado. Seria a medida por ausencia de sempre.
		//
		// Entao a deriva e ESCRITA, do tamanho de uma sessao longa, e o que se cobra e o tique
		// DESFAZE-LA. As duas afirmacoes sao os dois lados da tolerancia de 0,5 -- a mesma do pino de
		// chefe do `TickDoRoteiro`, e ela existe pra nao pagar `Statify` + `PowerLevel` a 30 Hz por
		// causa de um milesimo.
		// ==================================================================================================
		double pinado = reflexo.Ficha.BP;

		reflexo.Ficha.BP = pinado * 1.5;
		UmTique();
		AfirmarMnt("o tique DESFAZ o BP que o reflexo ganhou lutando (o `NPCTicker` do `MindClone`)",
				   Math.Abs(reflexo.Ficha.BP - pinado) <= 0.5,
				   $"{reflexo.Ficha.BP:N3} x {pinado:N3}");

		reflexo.Ficha.BP = pinado + 0.2;
		UmTique();
		AfirmarMnt("...e uma deriva menor que a tolerancia nao paga `Statify` a toa",
				   Math.Abs(reflexo.Ficha.BP - (pinado + 0.2)) < 1e-9, $"{reflexo.Ficha.BP:N3}");
		reflexo.Ficha.BP = pinado;

		Rodar(2);
		AfirmarMnt("e de pe, com os lacos de producao rodando, ele nao sai do lugar",
				   Math.Abs(reflexo.Ficha.BP - pinado) <= 0.5,
				   $"{reflexo.Ficha.BP:N3} -> saiu de {pinado:N3}");

		// ============================ E ELE NAO EMPURRA O TOPO DO SERVIDOR ============================
		// O BP base do reflexo e o EXPRESSO de outra pessoa -- nao e o BP base de ninguem. `TopBP` so
		// SOBE, entao um emprestimo desses ficaria no servidor pra sempre, escalando o ganho de todo
		// mundo. Ver a guarda no `TickFichas`.
		//
		// SO O `TickFichas`, sem tique de combate: com 1e15 de BP um unico golpe do reflexo quebraria
		// um nucleo vital do dono e a bancada passaria a medir o proprio estrago.
		// ==========================================================================================
		double topoAntes = GainKnobs.TopBP;
		reflexo.Ficha.BP = 1e15;
		TickFichas();
		AfirmarMnt("o BP do reflexo NAO entra no topo do servidor (`GainKnobs.TopBP`)",
				   GainKnobs.TopBP <= Math.Max(topoAntes, 1e14), $"{GainKnobs.TopBP:N0}");
		reflexo.Ficha.BP = pinado;

		// ============================ O DONO CRESCE, E A VOLTA O ALCANCA ============================
		// A ordem e a do jogo: o dono treina DENTRO da mente (a um quarto), o reflexo cai, e quando ele
		// se reergue e o dono de agora que esta na frente. Sem a reancoragem, quinze minutos de treino
		// deixariam um saco de pancada no lugar do oponente.
		// ========================================================================================
		dono.Ficha.BP *= 3;
		dono.Ficha.Statify();
		dono.Ficha.PowerLevel();
		double alvo = dono.Ficha.expressedBP;

		reflexo.Combate.Nocautear(MeleeResolver.TetoDoNocaute);
		UmTique();
		AfirmarMnt("caido, o reflexo AINDA nao acompanhou (a reancoragem e na VOLTA, nao na queda)",
				   Math.Abs(reflexo.Ficha.BP - alvo) > 1, $"{reflexo.Ficha.BP:N0}");

		Rodar(16);

		// ============================ A BANDA DE 1% E DELIBERADA ============================
		// O poder expresso do dono nao e uma constante nem com ele parado: folego, barriga e Ki mexem
		// no `expressedBP` a cada tique de ficha, e a ancora e um INSTANTANEO do momento da volta --
		// nao um fio ligado. Exigir igualdade exata mediria o relogio, nao a reancoragem.
		//
		// O que a afirmacao cobra e a unica coisa que importa: o reflexo esta no poder de AGORA (1%) e
		// nao no de quinze segundos atras, que era TRES VEZES menor. Nenhum erro plausivel deste
		// mecanismo cai dentro dessa banda.
		// ================================================================================
		double agoraDoDono = dono.Ficha.expressedBP;
		double banda = Math.Max(1, agoraDoDono * 0.01);
		AfirmarMnt("ao se reerguer, o reflexo esta no poder do dono de AGORA",
				   !reflexo.Ficha.KO && reflexo.Ficha.BP > pinado * 2.5
				   && Math.Abs(reflexo.Ficha.BP - agoraDoDono) <= banda,
				   $"{reflexo.Ficha.BP:N0} x {agoraDoDono:N0} (era {pinado:N0}, alvo na queda {alvo:N0})");
		AfirmarMnt("...e o PINO acompanhou junto (senao o tique o puxaria de volta pro poder antigo)",
				   Math.Abs(reflexo.BpDaMente - reflexo.Ficha.BP) <= 0.5,
				   $"{reflexo.BpDaMente:N0} x {reflexo.Ficha.BP:N0}");

		// ============================ A LEMBRANCA DE CHEFE **NAO** E REESCRITA ============================
		// O mesmo ramo de IA ergue os dois corpos, e o mesmo `ReerguerOOponente` os levanta -- mas a
		// ficha do chefe e DELE (degraus, gatilho, BP pinado pelo `TickDoRoteiro`). Reancora-la no dono
		// apagaria o Freeza e poria voce no lugar dele, com o nome dele. A pergunta que separa os dois e
		// a mesma que ja escolhe a frase do anuncio.
		// ============================================================================================
		MoldeDeNpc? moldeChefe = _moldes?.Todos.FirstOrDefault(m => m.EhChefe);
		if (moldeChefe == null) AfirmarMnt("ha molde de chefe pra medir a lembranca", false, "nenhum");
		else
		{
			// O ESTADO DE PRODUCAO DE UMA LEMBRANCA: ficha de chefe e `BpDaMente` ZERO -- o pino dela e
			// o `Papel.BpsPinados`, do `TickDoRoteiro`. Deixar o pino do reflexo ligado aqui faria a
			// afirmacao passar pelo motivo errado (o BP ficaria parado por causa do pino velho, e nao
			// porque o espelho recusou reescreve-lo).
			reflexo.Papel = new PapelDeNpc(moldeChefe, 1UL);
			reflexo.BpDaMente = 0;
			double bpDaLembranca = reflexo.Ficha.BP;
			dono.Ficha.BP *= 7;
			dono.Ficha.Statify();
			dono.Ficha.PowerLevel();

			reflexo.Combate.Nocautear(MeleeResolver.TetoDoNocaute);
			UmTique();
			Rodar(16);
			AfirmarMnt("a lembranca de CHEFE se reergue, mas com a ficha DELA (nao a sua)",
					   !reflexo.Ficha.KO && Math.Abs(reflexo.Ficha.BP - bpDaLembranca) < 1,
					   $"{reflexo.Ficha.BP:N0} x {bpDaLembranca:N0}");
			reflexo.Papel = null;
		}
	}
}
