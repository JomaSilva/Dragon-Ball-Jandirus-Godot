using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS FAMILIAS P E T DA `--chefesteste` (2026-10-18): o admin que pula os dias de aviso da saga, e a TROPA do Freeza em Namek. O pedido do
/// dono, em resumo: o admin poder pular os dias de aviso do chefe de saga (ele nasce agora); e, com o Freeza em Namek, muitos soldados NPC de
/// raca alienigena pelo planeta, atacando os Namekuseijins e pegando as Esferas do Dragao do chao -- que so voltam derrotando quem as pegou
/// --, de armadura Saiyajin / do exercito do Freeza.
///   P.  PULAR OS DIAS -- `admin_saga_agora` poe no mundo, na hora, o Freeza que estava a caminho; a saga adormecida dispara e ele chega; um id
///       que nao existe e recusado. Contra: `AgoraEsperaAViradaDeTeste`.
///   T1. O DESEMBARQUE -- com o Freeza em Namek, a tropa chega (os numeros sao os do `npcs.json`: desde 2026-10-24, vinte de uma vez e um a
///       cada 5 s ate quarenta), soldados da raca Alien, de armadura, hostis e nao-letais, e o planeta ouve. Contra: `TropaNaoVemDeTeste`.
///   T1b. QUARENTA PELO MAPA TODO (2026-10-24) -- o teto e de quarenta ou mais, e no teto ha soldado nos quatro quadrantes de Namek. Contra:
///       a mutacao do `npcs.json` de volta ao teto de doze.
///   T1c. AS SEIS FOLHAS (2026-10-24) -- todo soldado veste uma das seis folhas que o dono escolheu, sorteada quando ele nasce (quatro ou mais
///       delas entre os do teto). Contra: `TropaComOCorpoDoPovoDeTeste`.
///   T2. A ESFERA DO CHAO -- pelo tique de producao o soldado vai ate ela e a pega, o planeta ouve, e ela fica na mao dele. Contra:
///       `SoldadoNaoSeguraEsferaDeTeste`.
///   T3. O POVO -- o soldado ataca o Namekuseijin NPC e deixa o vizinho Humano, mais perto; o Namekuseijin que apanhou nao vai pra cima do
///       jogador ao lado. Contra: `SoldadoComoHostilComumDeTeste`, `SoldadoProvocaOPovoDeTeste`.
///   T4. QUEM CARREGA -- o jogador com esfera, a vinte tiles, vira a presa. Contra: `SoldadoIgnoraQuemCarregaDeTeste`.
///   T5. DERROTADO -- de pe, ninguem tira a esfera dele; nocauteado, ela cai onde ele esta e o jogador a pega; ao levantar, ele foge. Contra:
///       `SoldadoDerrotadoVoltaDeTeste`.
///   T6. A TROPA VAI EMBORA COM O CHEFE -- o Freeza cai, os soldados saem, e a esfera de um deles fica no chao onde ele estava. Contra:
///       `TropaFicaSemOChefeDeTeste`, `EsferaSomeComOSoldadoDeTeste`.
/// O MUNDO VOLTA COMO ESTAVA: as esferas voltam pro lugar (e pro disco), os corpos da bancada saem e a testemunha volta pra onde estava.
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// P) PULAR OS DIAS DE AVISO DA SAGA
	// =====================================================================
	private void PularOsDiasDaSaga(ServerPlayer pl)
	{
		GD.Print("[chefes] -- P) o admin pula os dias de aviso da saga (dono, 2026-10-18) --");
		EloDaSaga? elo = Array.Find(_cadeia, e => e.Chefes.Any(c => c.Molde == "freeza_namek"));
		if (elo == null) { AfirmarCh("(montagem) a cadeia tem a saga do Freeza de Namek", false); return; }
		ulong planeta = ZoneKey.Premade(elo.Planeta).Hash;

		bool FreezaNoPlaneta() => _sagas.Do(elo.Id) is { Fase: FaseDaSaga.EmCurso } e
			&& e.Chefes.Any(c => c.Molde == "freeza_namek" && c.Fase == FaseDoChefe.NoMundo
								 && _players.TryGetValue(c.Corpo, out ServerPlayer? f) && f.Zone.Hash == planeta);
		string Estado() => _sagas.Do(elo.Id) is { } e
			? $"fase {e.Fase}; " + string.Join(", ", e.Chefes.Select(c => $"{c.Molde} {c.Fase} {c.DiasRestantes}d"))
			: "sem estado";

		// 1. A SAGA DISPARADA, CONTANDO OS SETE DIAS -- o botao poe o Freeza no mundo agora.
		SagasLimpas();
		DispararElo(elo, _sagas.Do(elo.Id)!, "bancada", 0);
		bool contava = _sagas.Do(elo.Id) is { Fase: FaseDaSaga.Contando } antes
					   && antes.Chefes.All(c => c.Fase == FaseDoChefe.ACaminho && c.DiasRestantes > 0);
		List<string> disse = Ouvir(() => VerboDeAdmin(pl, "admin_saga_agora", elo.Id));
		AfirmarCh("P. **O ADMIN PULA OS DIAS DE AVISO** (o pedido do dono: o chefe de saga nasce agora, sem esperar os dias): a saga do Freeza de "
				  + "Namek contando os sete dias -> o verb poe o Freeza em Namek na hora, com a saga em curso, e o admin le que ele esta chegando",
				  contava && FreezaNoPlaneta() && disse.Exists(f => f.Contains("chegando agora", StringComparison.Ordinal)),
				  $"contava {contava}; {Estado()}; {Ultimos(disse)}");

		// CONTRA-EXEMPLO: o verb que so zera a contagem e espera a virada do dia.
		SagasLimpas();
		DispararElo(elo, _sagas.Do(elo.Id)!, "bancada", 0);
		AgoraEsperaAViradaDeTeste = true;
		try { Ouvir(() => VerboDeAdmin(pl, "admin_saga_agora", elo.Id)); }
		finally { AgoraEsperaAViradaDeTeste = false; }
		AfirmarCh("CONTRA-EXEMPLO (defeito injetado: o verb zera a contagem e espera a virada do dia pra o chefe nascer): o Freeza nao esta no "
				  + "mundo -- a regua de cima REPROVA", !FreezaNoPlaneta(), Estado());

		// 2. A SAGA ADORMECIDA: o mesmo botao a dispara do marco, e o Freeza chega na hora.
		SagasLimpas();
		List<string> adormecida = Ouvir(() => VerboDeAdmin(pl, "admin_saga_agora", elo.Id));
		AfirmarCh("...e na saga ADORMECIDA (ninguem bateu o marco) o mesmo botao a dispara e o Freeza chega na hora",
				  _sagas.Do(elo.Id) is { Disparou: true } && FreezaNoPlaneta(), $"{Estado()}; {Ultimos(adormecida)}");

		// 3. O id que nao existe.
		List<string> errou = Ouvir(() => VerboDeAdmin(pl, "admin_saga_agora", "nao_existe"));
		AfirmarCh("...e um id que nao existe e recusado com a lista das sagas", errou.Exists(f => f.Contains(elo.Id, StringComparison.Ordinal)),
				  Ultimos(errou));
		SagasLimpas();
	}

	// =====================================================================
	// T) A TROPA DO FREEZA EM NAMEK
	// =====================================================================
	private void ATropaDoFreeza(ServerPlayer pl)
	{
		GD.Print("[chefes] -- T) a tropa do Freeza em Namek: os soldados, o povo e as esferas (dono, 2026-10-18) --");
		EloDaSaga? elo = Array.Find(_cadeia, e => e.Tropa != null && e.Chefes.Any(c => c.Molde == "freeza_namek"));
		ZoneKey namek = ZoneKey.Premade("Namek");
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(namek);
		MoldeDeNpc? molde = elo?.Tropa is { } t0 ? _moldes?.Get(t0.Molde) : null;
		Esfera? bola = _esferas.Find(e => e.Zona.Hash == namek.Hash);
		AfirmarCh("(montagem) a saga do Freeza de Namek traz tropa, o molde dela e do tipo soldado e da raca Alien, e Namek tem mapa e esfera",
				  elo?.Tropa is { Caca: "Namekian", Quantos: > 0 } && molde is { Tipo: TipoDeNpc.Soldado } && molde.Racas.Contains("Alien")
				  && mapa != null && bola != null,
				  $"tropa {elo?.Tropa?.Molde}; molde {molde?.Tipo} {string.Join("/", molde?.Racas ?? [])}; mapa {mapa != null}; "
				  + $"esferas em Namek {_esferas.Count(e => e.Zona.Hash == namek.Hash)}");
		if (elo?.Tropa is not { } tropa || molde == null || mapa == null || bola == null) return;

		// O MUNDO VOLTA COMO ESTAVA: as esferas e a testemunha.
		var esferasAntes = _esferas.Select(e => (E: e, e.Portador, e.X, e.Y, Zona: e.Zona, e.PrecisaAssentar)).ToList();
		ZoneKey zonaDaTestemunha = pl.Zone;
		Vec2 posDaTestemunha = pl.Pos;
		var daBancada = new List<ServerPlayer>();
		const float t = ZoneCollision.TileSize;
		try
		{
			// A TESTEMUNHA EM NAMEK: sem jogador no planeta a mente dos corpos de la dorme (`MenteDormindo`).
			if (CantoVazioDeNamek(mapa, namek, 4) is not { } cantoDaTestemunha)
			{
				AfirmarCh("(montagem) achei em Namek um canto de terra firme sem ninguem a trinta tiles", false);
				return;
			}
			MoveToZone(pl.Id, namek, cantoDaTestemunha);
			Vec2 ondeFica = pl.Pos;

			// ---------------------------------------------------------- T1. o desembarque
			SagasLimpas();
			EstadoDoChefe ec = SagaComOChefeNoMundo(elo.Id, "freeza_namek").Ec;
			bool freezaVeio = _players.TryGetValue(ec.Corpo, out ServerPlayer? freeza) && freeza.Zone.Hash == namek.Hash;
			List<string> desembarque = Ouvir(TickDaTropa);
			List<ServerPlayer> chegaram = SoldadosDoElo(elo.Id);
			static bool DeArmadura(ServerPlayer s) => s.Visual.Roupa.Exists(r => r.Caminho.Contains("Armor", StringComparison.OrdinalIgnoreCase));
			bool comoPedido = chegaram.Count > 0 && chegaram.All(s => s.Zone.Hash == namek.Hash && EhSoldado(s) && s.Race == "Alien"
																	   && s.Papel is { Pacifico: false } && !s.Combate.Letal && DeArmadura(s));
			AfirmarCh("T1. **O DESEMBARQUE** (o pedido do dono: com o Freeza em Namek, muitos soldados de raca alienigena, de armadura do exercito): "
					  + $"o Freeza em Namek e a tropa chega -- {tropa.ChegamJuntos} de uma vez, soldados da raca Alien, de armadura, hostis e "
					  + "nao-letais -- e o planeta ouve o aviso",
					  freezaVeio && chegaram.Count == tropa.ChegamJuntos && comoPedido && Disse(desembarque, tropa.AnuncioDaChegada),
					  $"freeza {freezaVeio}; {chegaram.Count}: " + string.Join(", ", chegaram.Select(s => $"{s.Name}[{s.Race} "
					  + string.Join("+", s.Visual.Roupa.Select(r => System.IO.Path.GetFileNameWithoutExtension(r.Caminho))) + "]"))
					  + $"; {Ultimos(desembarque)}");

			_relogioDoMundo += tropa.SegundosEntre;
			TickDaTropa();
			int depoisDeUm = SoldadosDoElo(elo.Id).Count;
			for (int i = 0; i < tropa.Quantos * 2; i++) { _relogioDoMundo += tropa.SegundosEntre; TickDaTropa(); }
			int noTeto = SoldadosDoElo(elo.Id).Count;
			AfirmarCh($"...e depois chega um a cada {tropa.SegundosEntre:0} s, ate o teto de {tropa.Quantos} -- e para ali",
					  depoisDeUm == tropa.ChegamJuntos + 1 && noTeto == tropa.Quantos, $"depois de um intervalo {depoisDeUm}; no fim {noTeto}");

			// ---------------------------------------------------------- T1b e T1c. os quarenta pelo mapa todo, cada um com a sua folha (2026-10-24)
			List<ServerPlayer> noTetoDaTropa = SoldadosDoElo(elo.Id);
			int quadrantes = noTetoDaTropa.Select(s => (s.Pos.X >= mapa.Width * t / 2 ? 1 : 0) + (s.Pos.Y >= mapa.Height * t / 2 ? 2 : 0))
										  .Distinct().Count();
			AfirmarCh($"T1b. **QUARENTA PELO MAPA TODO** (o pedido do dono: \"pelomenos uns 40 pelo mapa todo\"): o teto da tropa e {tropa.Quantos} -- "
					  + "quarenta ou mais -- e, no teto, ha soldado nos QUATRO quadrantes de Namek (eles nascem nas vagas de habitante, que cobrem o "
					  + "planeta)", tropa.Quantos >= 40 && noTetoDaTropa.Count == tropa.Quantos && quadrantes == 4,
					  $"teto {tropa.Quantos}; de pe {noTetoDaTropa.Count}; quadrantes com soldado {quadrantes}");

			string FolhaDe(ServerPlayer s) => _visual?.CorpoSprite(s.Visual, s.Race, s.Genero) ?? "";
			static string Contagem(List<string> l) =>
				string.Join(", ", l.GroupBy(System.IO.Path.GetFileNameWithoutExtension).OrderBy(g => g.Key).Select(g => $"{g.Key} x{g.Count()}"));
			List<string> folhas = noTetoDaTropa.Select(FolhaDe).ToList();
			int distintas = folhas.Distinct().Count();
			AfirmarCh("T1c. **CADA SOLDADO NASCE COM UMA DAS SEIS FOLHAS QUE O DONO ESCOLHEU** (Alien 9, Alien 4, Alien 8, Alien 1, Race Kui e "
					  + "CavemanAlien), SORTEADA quando ele nasce: todo soldado da tropa veste uma delas, e os do teto nao sao todos iguais -- quatro "
					  + "ou mais das seis aparecem",
					  folhas.Count > 0 && folhas.TrueForAll(f => FolhasDaTropa.Folhas.Contains(f)) && distintas >= 4, Contagem(folhas));

			// CONTRA-EXEMPLO: o soldado que nasce com um corpo da lista do povo Alien -- o jogo de antes.
			TirarATropaDaBancada();
			TropaComOCorpoDoPovoDeTeste = true;
			try { for (int i = 0; i < 6; i++) NascerSoldado(elo, tropa, namek); }
			finally { TropaComOCorpoDoPovoDeTeste = false; }
			List<string> doPovo = SoldadosDoElo(elo.Id).Select(FolhaDe).ToList();
			AfirmarCh("CONTRA-EXEMPLO (defeito injetado: o soldado nasce com um corpo da lista do povo Alien, o jogo de antes): nenhum veste uma das "
					  + "seis -- a regua de T1c REPROVA", doPovo.Count > 0 && !doPovo.Exists(f => FolhasDaTropa.Folhas.Contains(f)), Contagem(doPovo));

			TirarATropaDaBancada();
			TropaNaoVemDeTeste = true;
			try { TickDaTropa(); }
			finally { TropaNaoVemDeTeste = false; }
			AfirmarCh("CONTRA-EXEMPLO (defeito injetado: o chefe chega sozinho, o jogo de antes): nenhum soldado em Namek -- a regua de T1 REPROVA",
					  SoldadosDoElo(elo.Id).Count == 0, $"{SoldadosDoElo(elo.Id).Count} soldado(s)");

			// ---------------------------------------------------------- T6. a tropa vai embora com o chefe
			TickDaTropa();   // o desembarque de novo, com o Freeza ainda de pe
			List<ServerPlayer> comOChefe = SoldadosDoElo(elo.Id);
			ServerPlayer portador = comOChefe[0];
			Vec2 ondeEstava = portador.Pos;
			Vec2 ondePegou = portador.Pos + new Vec2(20 * t, 0);   // a esfera na mao guarda o lugar em que foi pega
			DarAEsfera(bola, portador, ondePegou, namek);
			ec.Fase = FaseDoChefe.Derrotado;
			if (freeza != null) RemoverNpc(freeza);
			List<string> partida = Ouvir(TickDaTropa);
			bool sairam = SoldadosDoElo(elo.Id).Count == 0 && comOChefe.TrueForAll(s => !_players.ContainsKey(s.Id));
			bool ondeEle = bola.Portador == 0 && Math.Abs(bola.X - ondeEstava.X) < 1 && Math.Abs(bola.Y - ondeEstava.Y) < 1;
			AfirmarCh("T6. **A TROPA VAI EMBORA COM O CHEFE**: o Freeza caiu, os soldados saem de Namek (o planeta ouve) e a esfera que um deles "
					  + "carregava fica no chao ONDE ELE ESTAVA -- nao some, nem volta pro lugar em que ele a pegou",
					  sairam && ondeEle && Disse(partida, tropa.AnuncioDaPartida),
					  $"sairam {sairam}; esfera portador {bola.Portador} em ({bola.X:0},{bola.Y:0}), ele estava em ({ondeEstava.X:0},{ondeEstava.Y:0}); {Ultimos(partida)}");

			SagasLimpas();
			EstadoDoChefe ec2 = SagaComOChefeNoMundo(elo.Id, "freeza_namek").Ec;
			TickDaTropa();
			ec2.Fase = FaseDoChefe.Derrotado;
			if (_players.TryGetValue(ec2.Corpo, out ServerPlayer? freeza2)) RemoverNpc(freeza2);
			TropaFicaSemOChefeDeTeste = true;
			try { TickDaTropa(); }
			finally { TropaFicaSemOChefeDeTeste = false; }
			int ficaram = SoldadosDoElo(elo.Id).Count;
			AfirmarCh("CONTRA-EXEMPLO (defeito injetado: a tropa fica no planeta sem o chefe): os soldados continuam em Namek -- a regua de T6 REPROVA",
					  ficaram > 0, $"{ficaram} soldado(s)");

			ServerPlayer leva = SoldadosDoElo(elo.Id)[0];
			Vec2 ondeLeva = leva.Pos;
			DarAEsfera(bola, leva, ondeLeva + new Vec2(20 * t, 0), namek);
			EsferaSomeComOSoldadoDeTeste = true;
			try { RemoverNpc(leva); }
			finally { EsferaSomeComOSoldadoDeTeste = false; }
			bool sumiuComEle = bola.Portador != 0 || Math.Abs(bola.X - ondeLeva.X) >= 1;
			AfirmarCh("CONTRA-EXEMPLO (defeito injetado: o soldado que sai do mundo leva a esfera pro lugar em que a pegou): a esfera nao esta no chao "
					  + "onde ele estava -- a regua de T6 REPROVA", sumiuComEle, $"portador {bola.Portador}; esfera em ({bola.X:0},{bola.Y:0})");
			bola.Portador = 0;
			TirarATropaDaBancada();
			SagasLimpas();

			// ---------------------------------------------------------- T2. a esfera do chao
			if (CantoVazioDeNamek(mapa, namek, 10) is not { } a2 || SoldadoNoPonto(elo, tropa, namek, a2) is not { } s2)
			{
				AfirmarCh("(montagem) um soldado num canto vazio de Namek, com uma esfera ao lado", false);
				return;
			}
			PorNoChao(bola, a2 + new Vec2(6 * t, 0), namek);
			List<string> pegando = Ouvir(() => TiquesAte(() => bola.Portador == s2.Id, 900));
			bool pegou = bola.Portador == s2.Id;
			TickDasEsferas();
			bool segura = bola.Portador == s2.Id;
			AfirmarCh("T2. **O SOLDADO PEGA A ESFERA DO CHAO** (o pedido do dono: pegando as esferas do chao): pelo tique de producao ele vai ate ela "
					  + "e a pega, o planeta ouve -- e ela FICA na mao dele no tique das esferas seguinte",
					  pegou && segura && Disse(pegando, "pegou a"),
					  $"pegou {pegou}; segura {segura}; ele em ({s2.Pos.X:0},{s2.Pos.Y:0}); {Ultimos(pegando)}");

			SoldadoNaoSeguraEsferaDeTeste = true;
			try
			{
				DarAEsfera(bola, s2, s2.Pos, namek);
				TickDasEsferas();
			}
			finally { SoldadoNaoSeguraEsferaDeTeste = false; }
			AfirmarCh("CONTRA-EXEMPLO (defeito injetado: so jogador segura esfera, a regra de antes): a esfera cai do soldado no tique seguinte -- a "
					  + "regua de T2 REPROVA", bola.Portador == 0, $"portador {bola.Portador}");
			// O SOLDADO DE T2 SAI, E A ESFERA VOLTA PRO LUGAR DELA: as familias de baixo nao podem ter uma esfera do chao no canto delas.
			RemoverNpc(s2);
			var deAntes = esferasAntes.Find(g => ReferenceEquals(g.E, bola));
			PorNoChao(bola, new Vec2(deAntes.X, deAntes.Y), deAntes.Zona);

			// ---------------------------------------------------------- T3. o povo
			(ServerPlayer? nk, ServerPlayer? vizinho, ServerPlayer? s3) = PovoESoldado(elo, tropa, namek, mapa, daBancada);
			if (nk == null || vizinho == null || s3 == null)
			{
				AfirmarCh("(montagem) um Namekuseijin de Namek (molde cidadao), um vizinho Humano e um soldado num canto vazio", false);
				return;
			}
			Ouvir(() => TiquesAte(() => nk.Ficha.KO, 600));
			bool apanhou = nk.Ficha.KO || nk.Ficha.HP < 99.9;
			bool poupado = !vizinho.Ficha.KO && vizinho.Ficha.HP >= 99.9;
			AfirmarCh("T3. **O SOLDADO ATACA O NAMEKUSEIJIN** (o pedido do dono: atacando qualquer namek) -- o NPC tambem, que o hostil comum nao "
					  + "enxerga --, e deixa o vizinho Humano, que estava MAIS PERTO dele",
					  apanhou && poupado, $"Namekuseijin {nk.Name}: vida {nk.Ficha.HP:0.0} KO {nk.Ficha.KO}; Humano: vida {vizinho.Ficha.HP:0.0} KO {vizinho.Ficha.KO}");

			(ServerPlayer? nk2, ServerPlayer? vizinho2, ServerPlayer? s3b) = PovoESoldado(elo, tropa, namek, mapa, daBancada);
			if (nk2 != null && s3b != null)
			{
				SoldadoComoHostilComumDeTeste = true;
				try { Ouvir(() => TiquesAte(() => nk2.Ficha.KO, 600)); }
				finally { SoldadoComoHostilComumDeTeste = false; }
				AfirmarCh("CONTRA-EXEMPLO (defeito injetado: o soldado no ramo do hostil comum, que so ve jogador -- o \"AI nao ataca AI\"): o "
						  + "Namekuseijin sai sem um arranhao -- a regua de T3 REPROVA",
						  !nk2.Ficha.KO && nk2.Ficha.HP >= 99.9, $"vida {nk2.Ficha.HP:0.0} KO {nk2.Ficha.KO}");
			}

			// O POVO NAO VAI PRA CIMA DO JOGADOR: o Namekuseijin que apanhou do soldado, com o jogador do lado.
			nk.Combate.Levantar();
			nk.RancorAte = 0;
			nk.UltimoAgressor = 0;
			pl.Pos = nk.Pos + new Vec2(3 * t, 0);
			MarcarAgressao(nk, s3);
			ServerPlayer? foiPra = PresaDoNpc(nk);
			AfirmarCh("...e o Namekuseijin que apanhou do soldado NAO sai batendo no jogador do lado (o rancor de um agressor que nao e gente "
					  + "mandaria o habitante atras do jogador mais perto)", foiPra == null, $"presa {foiPra?.Name ?? "nenhuma"}");
			nk.RancorAte = 0;
			nk.UltimoAgressor = 0;
			SoldadoProvocaOPovoDeTeste = true;
			try { MarcarAgressao(nk, s3); }
			finally { SoldadoProvocaOPovoDeTeste = false; }
			ServerPlayer? foiComODefeito = PresaDoNpc(nk);
			AfirmarCh("CONTRA-EXEMPLO (defeito injetado: o golpe do soldado marca rancor no habitante): o Namekuseijin vai pra cima do JOGADOR -- a "
					  + "regua de cima REPROVA", foiComODefeito?.Id == pl.Id, $"presa {foiComODefeito?.Name ?? "nenhuma"}");
			nk.RancorAte = 0;
			nk.UltimoAgressor = 0;
			pl.Pos = ondeFica;

			// ---------------------------------------------------------- T4. quem carrega esfera
			if (CantoVazioDeNamek(mapa, namek, 10) is { } a4 && SoldadoNoPonto(elo, tropa, namek, a4) is { } s4)
			{
				daBancada.Add(s4);
				pl.Pos = a4 + new Vec2(8 * t, 0);
				DarAEsfera(bola, pl, pl.Pos, namek);
				SoldadoDaTropa reg = _tropa[s4.Id];
				EscolherOAlvoDoSoldado(s4, reg);
				int presaCerta = reg.Presa;   // guardada: o contra-exemplo logo abaixo reescreve o mesmo campo
				bool cacaQuemCarrega = presaCerta == pl.Id;
				SoldadoIgnoraQuemCarregaDeTeste = true;
				try { EscolherOAlvoDoSoldado(s4, reg); }
				finally { SoldadoIgnoraQuemCarregaDeTeste = false; }
				bool ignorou = reg.Presa != pl.Id;
				AfirmarCh("T4. **QUEM CARREGA ESFERA VIRA A PRESA**: o jogador com uma esfera, a oito tiles do soldado, e o alvo dele -- e o nocaute "
						  + "derruba a esfera", cacaQuemCarrega, $"presa {presaCerta} (o jogador e {pl.Id})");
				AfirmarCh("CONTRA-EXEMPLO (defeito injetado: o soldado ignora quem carrega): o jogador deixa de ser a presa -- a regua de T4 REPROVA",
						  ignorou, $"presa {reg.Presa}");
				bola.Portador = 0;
				pl.Pos = ondeFica;
			}
			else AfirmarCh("(montagem) um soldado num canto vazio de Namek pra T4", false);

			// ---------------------------------------------------------- T5. derrotado
			if (CantoVazioDeNamek(mapa, namek, 10) is { } a5 && SoldadoNoPonto(elo, tropa, namek, a5) is { } s5)
			{
				daBancada.Add(s5);
				DarAEsfera(bola, s5, a5 + new Vec2(20 * t, 0), namek);
				pl.Pos = s5.Pos + new Vec2(0.5f * t, 0);
				TickDasEsferas();
				List<string> tentou = Ouvir(() => PegarEsfera(pl));
				bool ninguemTira = bola.Portador == s5.Id && Disse(tentou, "não há nenhuma Esfera");

				s5.Combate.Nocautear(30);
				List<string> caiu = Ouvir(TickDasEsferas);
				bool noChao = bola.Portador == 0 && Math.Abs(bola.X - s5.Pos.X) < 1 && Math.Abs(bola.Y - s5.Pos.Y) < 1;
				List<string> pegouDeVolta = Ouvir(() => PegarEsfera(pl));
				bool doJogador = bola.Portador == pl.Id;
				AfirmarCh("T5. **SO DERROTANDO-O** (o pedido do dono: as esferas so voltam derrotando quem as pegou): de pe, o jogador do lado nao "
						  + "tira a esfera dele; nocauteado, ela cai onde ele esta (o planeta ouve) e o jogador a pega",
						  ninguemTira && noChao && doJogador && Disse(caiu, "cai de"),
						  $"de pe {ninguemTira}; no chao {noChao}; com o jogador {doJogador}; {Ultimos(tentou)} | {Ultimos(caiu)} | {Ultimos(pegouDeVolta)}");
				bola.Portador = 0;

				TiqueDaLutaDeChefe(0);     // caido: o soldado fica marcado
				s5.Combate.Levantar();
				List<string> fuga = Ouvir(() => TiqueDaLutaDeChefe(1));
				bool fugiu = !_players.ContainsKey(s5.Id);
				AfirmarCh("...e quando levanta, ele FOGE (sai do mundo, quem estava perto ouve) -- sem voltar pra tomar a esfera de quem o derrubou",
						  fugiu && Disse(fuga, "foge"), $"no mundo {!fugiu}; {Ultimos(fuga)}");
			}
			else AfirmarCh("(montagem) um soldado num canto vazio de Namek pra T5", false);

			if (CantoVazioDeNamek(mapa, namek, 10) is { } a5b && SoldadoNoPonto(elo, tropa, namek, a5b) is { } s5b)
			{
				daBancada.Add(s5b);
				s5b.Combate.Nocautear(30);
				TiqueDaLutaDeChefe(2);
				s5b.Combate.Levantar();
				SoldadoDerrotadoVoltaDeTeste = true;
				try { TiqueDaLutaDeChefe(3); }
				finally { SoldadoDerrotadoVoltaDeTeste = false; }
				AfirmarCh("CONTRA-EXEMPLO (defeito injetado: o derrotado levanta e continua): ele segue no mundo -- a regua de T5 REPROVA",
						  _players.ContainsKey(s5b.Id));
			}
			pl.Pos = ondeFica;
		}
		finally
		{
			AgoraEsperaAViradaDeTeste = TropaNaoVemDeTeste = TropaFicaSemOChefeDeTeste = SoldadoComoHostilComumDeTeste = false;
			SoldadoDerrotadoVoltaDeTeste = SoldadoIgnoraQuemCarregaDeTeste = SoldadoNaoSeguraEsferaDeTeste = false;
			SoldadoProvocaOPovoDeTeste = EsferaSomeComOSoldadoDeTeste = false;
			foreach (ServerPlayer c in daBancada) if (_players.ContainsKey(c.Id)) RemoverNpc(c);
			TirarATropaDaBancada();
			foreach ((Esfera e, int portador, float x, float y, ZoneKey zona, bool assentar) in esferasAntes)
			{
				e.Portador = portador;
				e.X = x;
				e.Y = y;
				e.PorZona(zona);
				e.PrecisaAssentar = assentar;
			}
			SalvarEsferas();
			MandarEsferas(namek);
			if (pl.Zone.Hash != zonaDaTestemunha.Hash) MoveToZone(pl.Id, zonaDaTestemunha, posDaTestemunha);
			SagasLimpas();
		}
	}

	// =====================================================================
	// AS FERRAMENTAS DA FAMILIA T
	// =====================================================================
	/// <summary>Os soldados que a bancada pos de pe saem, e o relogio da tropa recomeca.</summary>
	private void TirarATropaDaBancada()
	{
		foreach (int id in _tropa.Keys.ToList())
			if (_players.TryGetValue(id, out ServerPlayer? s)) RemoverNpc(s);
		_tropa.Clear();
		_relogioDaTropa.Clear();
	}

	/// <summary>
	/// UM CANTO DE TERRA FIRME DE NAMEK: a celula e uma fileira de <paramref name="tiles"/> tiles livres (sem parede e sem agua -- o
	/// `PathOccupied` pergunta o passo), e nenhum corpo a trinta tiles -- o soldado da familia nao pode achar o povo do planeta no faro de
	/// vinte. Nulo = o mapa inteiro sem um canto assim.
	/// </summary>
	private Vec2? CantoVazioDeNamek(ZoneCollision mapa, ZoneKey namek, int tiles)
	{
		const float t = ZoneCollision.TileSize;
		const float longe = 30 * ZoneCollision.TileSize;
		for (int cy = 24; cy < mapa.Height - 24; cy += 7)
			for (int cx = 24; cx < mapa.Width - 24 - tiles; cx += 9)
			{
				var c = new Vec2(cx * t + t / 2f, cy * t + t / 2f);
				if (MoveRules.Occupied(mapa, c) || MoveRules.PathOccupied(mapa, c, c + new Vec2((tiles - 1) * t, 0))) continue;
				if (ZoneList(namek.Hash).Exists(o => (o.Pos - c).LengthSquared < longe * longe)) continue;
				return c;
			}
		return null;
	}

	/// <summary>Um soldado da tropa (pelo `NascerSoldado` de producao, registrado no elo) posto no ponto pedido.</summary>
	private ServerPlayer? SoldadoNoPonto(EloDaSaga elo, TropaDaSaga tropa, ZoneKey namek, Vec2 ponto)
	{
		ServerPlayer? s = NascerSoldado(elo, tropa, namek);
		if (s == null) return null;
		s.Pos = ponto;
		s.Moving = false;
		return s;
	}

	/// <summary>A esfera no chao, num ponto.</summary>
	private static void PorNoChao(Esfera e, Vec2 onde, ZoneKey zona)
	{
		e.Portador = 0;
		e.X = onde.X;
		e.Y = onde.Y;
		e.PorZona(zona);
		e.PrecisaAssentar = false;
	}

	/// <summary>A esfera na mao de alguem -- e o X/Y e o lugar em que ela foi pega (a esfera na mao nao anda com o corpo).</summary>
	private static void DarAEsfera(Esfera e, ServerPlayer quem, Vec2 ondeFoiPega, ZoneKey zona)
	{
		PorNoChao(e, ondeFoiPega, zona);
		e.Portador = quem.Id;
	}

	/// <summary>Tiques de producao (a ordem do `_Process`, com as esferas a 1 Hz) ate a condicao valer ou o teto vencer.</summary>
	private void TiquesAte(Func<bool> cond, int teto)
	{
		for (int i = 0; i < teto && !cond(); i++)
		{
			TiqueDaLutaDeChefe(i);
			if (i % 30 == 29) TickDasEsferas();
		}
	}

	/// <summary>
	/// O POVO DE NAMEK E UM SOLDADO, num canto vazio: um Namekuseijin nascido pelo molde `cidadao` de producao (a raca sai do berco do planeta,
	/// entao a bancada tenta lugares ate sair um), um vizinho Humano MAIS PERTO do soldado que ele, e o soldado. Os tres vao pra lista da bancada.
	/// </summary>
	private (ServerPlayer? Nk, ServerPlayer? Vizinho, ServerPlayer? Soldado) PovoESoldado(EloDaSaga elo, TropaDaSaga tropa, ZoneKey namek,
																						  ZoneCollision mapa, List<ServerPlayer> daBancada)
	{
		const float t = ZoneCollision.TileSize;
		if (CantoVazioDeNamek(mapa, namek, 12) is not { } c) return (null, null, null);
		ServerPlayer? nk = null;
		for (ulong lugar = 9_100_000; lugar < 9_100_060 && nk == null; lugar++)
		{
			ServerPlayer? n = NascerNpc("cidadao", namek, c + new Vec2(9 * t, 0), lugar);
			if (n == null) continue;
			if (Jandirus.Core.Social.Fusao.EhNamekuseijin(n.Race)) nk = n;
			else RemoverNpc(n);
		}
		if (nk == null) return (null, null, null);
		daBancada.Add(nk);
		nk.Pos = c + new Vec2(9 * t, 0);
		// O VIZINHO DO OUTRO LADO: mais perto do soldado que o Namekuseijin, mas fora da linha de tiro dele (um raio que errasse o alvo pegaria
		// quem estivesse no meio, e a bancada mediria o tiro perdido e nao a escolha da presa).
		ServerPlayer vizinho = Forjar("Vizinho Humano", c + new Vec2(-3 * t, 0), namek, "bancada_chefes_vizinho");
		daBancada.Add(vizinho);
		ServerPlayer? s = SoldadoNoPonto(elo, tropa, namek, c);
		if (s != null) daBancada.Add(s);
		return (nk, vizinho, s);
	}
}
