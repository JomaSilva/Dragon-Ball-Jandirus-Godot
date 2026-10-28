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
			AQuedaEVitoria(Forjar);
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

		// (2026-10-16) A MAESTRIA DE FORMA PARA LA DENTRO -- regra do dono; o original nao mexia nela (divergencia declarada no
		// `RitmoDaZona`).
		AfirmarMnt("**NA MENTE NAO SE MASTERIZA FORMA** (*\"faça q n é possivel masterizar forma na meditaçao profunda\"*): o ritmo de maestria da zona vai a 0",
				   Math.Abs(f.zoneMasteryMult) < 1e-9, $"{f.zoneMasteryMult}");
		RitmoDaZona.MenteMasterizaFormaDeTeste = true;
		var antes = new Fighter { Race = "Saiyan", BP = 1000 };
		try { RitmoDaZona.Aplicar(antes, DimensaoMental.De(1234)); }
		finally { RitmoDaZona.MenteMasterizaFormaDeTeste = false; }
		AfirmarMnt("(defeito injetado: a regra de antes) a mente volta a deixar a maestria em 1x -- a regua de cima REPROVA",
				   Math.Abs(antes.zoneMasteryMult - 1) < 1e-9, $"{antes.zoneMasteryMult}");

		// LIGAR E NAO DESLIGAR e o modo de falha nomeado no cabecalho do `RitmoDaZona`.
		RitmoDaZona.Aplicar(f, fora);
		AfirmarMnt("SAIR da mente devolve o ganho a 1x (nao ha 0,25 preso no bolso) -- e a maestria tambem",
				   Math.Abs(f.zoneGainMult - 1) < 1e-9 && Math.Abs(f.zoneMasteryMult - 1) < 1e-9, $"ganho {f.zoneGainMult}, maestria {f.zoneMasteryMult}");

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

		// (2026-10-16) E PELOS FUNIS DE PRODUCAO DA MAESTRIA: uma hora sustentando a forma -- a da escada (`SubirMaestriaDaZona`)
		// e a do ki divino (`TickDaMaestriaDivina`, num SSG com o ki divino desperto) --, dentro da mente e fora dela.
		(double Forma, double Divina) UmaHoraNaForma(ServerPlayer quem)
		{
			quem.Ficha.godki = new GodKiState { awakened = true, usage = true };
			quem.Forma.Entrar("ssg");
			SubirMaestriaDaZona(quem, "ssj", Jandirus.Core.Forms.Catalogo.MaestriaPorSegundo, 3600, out _);
			TickDaMaestriaDivina(quem, 3600);
			return (quem.Forma.Maestria.De("ssj"), quem.Ficha.godki.mastery);
		}
		var naMente = UmaHoraNaForma(dentro);
		var foraDela = UmaHoraNaForma(new ServerPlayer
		{
			Id = IdBaseDaMenteDeTeste + 91, Peer = null, Name = "bancada: fora da mente", Race = "Saiyan", Genero = "Male", Idade = 25,
			Ficha = new Fighter { Race = "Saiyan", BP = 1000 },
		});
		AfirmarMnt("**...E NADA SE MASTERIZA LA DENTRO**: uma hora sustentando a forma na mente nao sobe a maestria dela nem a do ki divino -- fora da mente, a mesma hora sobe as duas",
				   naMente.Forma == 0 && naMente.Divina == 0 && foraDela.Forma > 0 && foraDela.Divina > 0,
				   $"na mente: forma {naMente.Forma:0.###}, divina {naMente.Divina:0.###}; fora: forma {foraDela.Forma:0.###}, divina {foraDela.Divina:0.###}");
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

		// A TESTEMUNHA fica colada na vitima: e ela que provaria a raiva se o gate falhasse. DECLAROU a
		// vitima amiga (e a declaracao, e so ela, que a queda le -- `Convivio.LutoPorQueda`), e o algoz e
		// inimigo por PADRAO (quem a vitima nao declarou amigo ja e "inimigo aos olhos dela"), entao nao
		// ha nada a escrever daquele lado.
		ServerPlayer plateia = forjar(12, "bancada: quem assiste", mente, PosMental + new Vec2(32, 0), 1_000);
		DeclararDeTeste(plateia, vitima, Jandirus.Core.Social.Relacao.Bom);

		vitima.Ficha.zenkaiGuardado = 100_000;   // (2026-10-26) o banco do Zenkai que a derrota soltaria
		double bpAntes = vitima.Ficha.BP;

		AoPerderALuta(vitima, algoz, morreu: false);

		AfirmarMnt("DENTRO da mente, perder nao paga Zenkai",
				   Math.Abs(vitima.Ficha.BP - bpAntes) < 1e-6, $"{vitima.Ficha.BP} != {bpAntes}");
		// A RAIVA E A JANELA, e nao o `Anger`: o `Anger` e derivado e projetado por tique, a janela e
		// o que `AcenderJanelaDeRaiva` escreve. Medir o derivado deixaria a afirmacao a merce do
		// relogio do tique.
		AfirmarMnt("DENTRO da mente, ver um amigo cair nao acende raiva",
				   plateia.RaivaComumAte == 0, $"{plateia.RaivaComumAte}");

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
		vitima.Ficha.zenkaiGuardado = 100_000;   // (2026-10-26) o banco do Zenkai que a derrota soltaria
		bpAntes = vitima.Ficha.BP;

		AoPerderALuta(vitima, algoz, morreu: false);

		AfirmarMnt("FORA da mente a mesma cena paga Zenkai",
				   vitima.Ficha.BP > bpAntes + 1e-6, $"{vitima.Ficha.BP} == {bpAntes}");
		AfirmarMnt("FORA da mente a mesma cena acende raiva",
				   plateia.RaivaComumAte > 0, $"{plateia.RaivaComumAte}");
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

			// UM POR VEZ, E A LUTA NAO SE ENCERRA NO MEIO (2026-10-27, o pedido 124c): com a lembranca de pe, outra convocacao e RECUSADA.
			// Ate ali convocar de novo desfazia a anterior -- era a saida de uma luta perdida pela porta da lista.
			int antes = corpo.Id;
			ChamarChefe(dono, molde.Id);
			AfirmarMnt("com a lembranca de pe, convocar outra e RECUSADO -- a luta nao se encerra no meio (o pedido 124c)",
					   dono.CloneId == antes && _players.ContainsKey(antes), $"{dono.CloneId} / {antes}");
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
	// 6. A QUEDA E VITORIA -- o pedido 124c (2026-10-27), no lugar do `clone_watch()` do DM
	// =====================================================================
	/// <summary>
	/// *"Vitoria e escolhe outro"* -- a escolha do dono pro documento de um jogador. Ate 2026-10-27 esta familia media o `clone_watch()` do DM
	/// (`MindMeditate.dm:288-306`): o reflexo se reerguia 15 s depois de cada queda, inteiro, e so a MORTE encerrava o transe. Agora as duas
	/// quedas sao a mesma VITORIA (`VencerNaMente`): a lembranca se desfaz, o BP entra pela forca dela, a lista volta -- e o transe CONTINUA
	/// (sai-se pela porta, ou pela derrota). A lista inteira e da `--mentelistateste`; aqui fica o que esta familia media e mudou de lado:
	///   1. o NOCAUTE nao se reergue mais (o corpo sai no tique da queda);
	///   2. a MORTE tambem e vitoria, e nao encerra o transe -- nem depois da onda de 1,8 s que a volta antiga levava.
	///
	/// O RELOGIO E O DO MUNDO SIMULADO (os dois lacos de producao, na ordem do `Tick()`), salvo a espera da onda: o prazo dela e `NowMs()`, e
	/// so o relogio de verdade a vence.
	/// </summary>
	private void AQuedaEVitoria(Func<int, string, ZoneKey, Vec2, double, ServerPlayer> forjar)
	{
		GD.Print("[mente] --- 6. a queda e vitoria (o pedido 124c) ---");

		ZoneKey mente = DimensaoMental.De(IdBaseDaMenteDeTeste + 50);
		ServerPlayer dono = forjar(50, "bancada: quem medita", mente, PosMental, 100_000);
		// O TETO DE TREINO (`relBPmax`) que o tique de status escreve em jogo: o premio da vitoria e o ganho do T (`TrainGain`), que so rende
		// abaixo dele -- e o corpo forjado nascia com o teto em zero (a rodada r3 do agente `mentelista`: "BP 100.000 -> 100.000").
		dono.Ficha.PowerLevel();

		void UmTique()
		{
			TickCombate(Jandirus.Net.Protocol.TickSeconds);
			TickDosCorposSemDono(Jandirus.Net.Protocol.TickSeconds);
		}

		// PELO CAMINHO DE PRODUCAO: a escolha da lista, pelo canal de habilidade -- o reflexo nao nasce mais sozinho na porta.
		UsarHabilidade(dono, MenteOponentes.Pedido(MenteOponentes.Tipo.Reflexo, ""));
		ServerPlayer? reflexo = _players.GetValueOrDefault(dono.CloneId);
		AfirmarMnt("o reflexo nasce pela escolha da lista, de pe", reflexo is { Ficha.KO: false }, $"{dono.CloneId}");
		if (reflexo == null) return;

		// FRACO DE PROPOSITO, como antes: a medida e a queda dele, nao a troca de golpes.
		reflexo.Ficha.BP = 100;
		reflexo.Ficha.Statify();

		double antes = dono.Ficha.BP;
		int primeiro = reflexo.Id;
		reflexo.Combate.Nocautear(MeleeResolver.TetoDoNocaute);   // o funil comum, e nao `KO = true`
		UmTique();
		AfirmarMnt("o NOCAUTE e vitoria: a lembranca sai no tique da queda (nao se reergue mais), e o BP entra",
				   dono.CloneId == 0 && !_players.ContainsKey(primeiro) && dono.Ficha.BP > antes,
				   $"lembranca {dono.CloneId}, corpo {_players.ContainsKey(primeiro)}, BP {antes:N2} -> {dono.Ficha.BP:N2}");
		AfirmarMnt("...e o transe continua (escolhe-se o proximo)", NaMente(dono), $"{dono.Zone}");

		// A MORTE: a mesma vitoria. MATA PELO FUNIL DE VERDADE (`Morrer`), que arma o relogio da morte no gancho `CombatState.AoMorrer`.
		UsarHabilidade(dono, MenteOponentes.Pedido(MenteOponentes.Tipo.Reflexo, ""));
		ServerPlayer? segundo = _players.GetValueOrDefault(dono.CloneId);
		AfirmarMnt("a lista devolve outra lembranca (um corpo NOVO)", segundo != null && segundo.Id != primeiro, $"{dono.CloneId}");
		if (segundo == null) return;
		segundo.Ficha.BP = 100;
		segundo.Ficha.Statify();
		double antesDaMorte = dono.Ficha.BP;
		segundo.Combate.Morrer(ignorarSeguro: true);
		UmTique();
		// A VOLTA ANTIGA ONDULAVA 1,8 s de relogio REAL antes de tirar o dono da mente: o "continua" so vale depois desse prazo.
		long fimDaOnda = NowMs() + DimensaoMental.MsDaOnda + 700;
		while (NowMs() < fimDaOnda) UmTique();
		AfirmarMnt("a MORTE tambem e vitoria: o corpo saiu, o BP entrou -- e o transe NAO acabou (nem depois da onda de 1,8 s)",
				   NaMente(dono) && dono.CloneId == 0 && !_players.ContainsKey(segundo.Id) && dono.Ficha.BP > antesDaMorte,
				   $"{dono.Zone} / lembranca {dono.CloneId} / BP {antesDaMorte:N2} -> {dono.Ficha.BP:N2}");
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

		// ============================ (2026-10-27, o pedido 124c) A VOLTA QUE REANCORAVA SAIU JUNTO COM A VOLTA ============================
		// Aqui o dono crescia, o reflexo caia, e na volta (15 s) ele estava no poder do dono de AGORA -- e a lembranca de chefe se reerguia
		// com a ficha DELA. Derrubar a lembranca virou VITORIA (ela se desfaz, paga e a lista volta: a familia 6 e a `--mentelistateste`),
		// entao nao ha volta pra reancorar: a proxima escolha nasce no dono de agora, pelo mesmo `EspelharODono` medido acima.
		// ========================================================================================================================
	}
}
