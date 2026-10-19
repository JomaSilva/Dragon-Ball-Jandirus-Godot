using System.Text.Json;
using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// AS CAMADAS 9, 10 E 11 DA `--naveteste` (2026-10-18) -- o pedido do dono: *"enquanto vc pilota, deveria dar pra escutar e ver o chat q
/// rola dentro da nave igual como se vc n tivesse pilotando, outra coisa e a opcao de observe, deveria 'assistir' o exterior da nave sem pode
/// controla-la ... e a nave tanto pod quanto a capital ship deveria ter combustivel q gasta com a distancia andada"*. Pelas portas de
/// producao (o embarque, o leme, o console, o `TickDasNaves`, o `Falar`, o `TickDoVoo`), cada regra com o seu contra-exemplo:
///
///   9. O COMBUSTIVEL: a nave nasce cheia (e a do disco de antes tambem); gasta pela DISTANCIA (o relogio e o contra-exemplo); mais rapido =
///      mais por segundo, e a mesma distancia custa o mesmo tanque; o tanque seco para a nave de vez (no espaco e no chao, e nada lanca); o
///      abastecer cobra recursos de quem pede, enche o que o bolso paga, e vale ate pra quem vem trazer; so vai pra mochila cheia; ao leme o
///      Ki do piloto nao paga o voo; a nave grande sozinha gasta o passo e para seca.
///  10. O CHAT AO LEME: quem pilota a nave grande ouve quem fala la dentro, perto da ponte (e quem esta longe da ponte nao); e o que ele
///      fala sai la dentro tambem, sem chegar duas vezes nele.
///  11. O OBSERVAR: o corpo fica na ponte e o olho vai pra fora; ninguem o ve, ele nao anda, e anda com a nave (no espaco e na troca de
///      zona); a nave que passa por cima de um planeta nao o pousa la; ele ouve la dentro e nao la fora; apanhar no corpo o traz de volta;
///      o mesmo verbo o devolve; e a nave que explode o deixa do lado de fora, onde ela estava.
///
/// Elas emprestam o corpo do dono como as outras camadas, e devolvem tudo no fim.
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// 9. O COMBUSTIVEL
	// =====================================================================
	private void RodarBancadaDoCombustivel(ServerPlayer pl, Action<string, bool, string> checa)
	{
		GD.Print("  -- 9) o combustivel das naves (dono, 2026-10-18) --");
		const int t = ZoneCollision.TileSize;
		ZoneKey zonaAntes = pl.Zone;
		Vec2 posAntes = pl.Pos;
		double recursosAntes = pl.Ficha.Recursos, kiAntes = pl.Ficha.Ki;
		if (NaveDoPiloto(pl) is { } jaPilotava) Desembarcar(pl, jaPilotava);
		ZoneKey terra = ZoneKey.Premade("Earth");
		Vec2 berco = PontoDeNascimento(terra);
		PlanetaNoEspaco daTerra = Espaco.PreFeitos().First(p => string.Equals(p.Nome, "Earth", StringComparison.OrdinalIgnoreCase));
		// O ESPACO DA CENA: ao lado da decolagem da Terra, onde ela ja garante que nao ha disco nem estrela por baixo.
		Vec2 noEspaco = Espaco.PontoDeDecolagem(daTerra) + new Vec2(40 * t, 0);
		Nave? pod = null, grande = null;
		ServerPlayer? quemTraz = null;
		double tanque = Combustivel.Capacidade("Spacepod");

		// UM SEGUNDO DE VOO, medido: o passo por tique, trinta vezes, e a volta de um segundo fechada a forca (o relogio da bancada nao anda).
		double UmSegundo(Nave n, double passoPx)
		{
			_proximaVoltaDoTanque = 0;
			TickDasNaves();
			for (int i = 0; i < 30; i++) { pl.Pos += new Vec2((float)passoPx, 0); TickDasNaves(); }
			_proximaVoltaDoTanque = 0;
			TickDasNaves();
			return n.GastoPorSegundo;
		}
		double Andar(Nave n, double passoPx, int tiques)
		{
			double antes = NivelDoTanque(n);
			for (int i = 0; i < tiques; i++) { pl.Pos += new Vec2((float)passoPx, 0); TickDasNaves(); }
			return antes - NivelDoTanque(n);
		}

		try
		{
			// ---------------------------------------------------------------- a. a nave nasce cheia
			MoveToZone(pl.Id, terra, berco);
			pod = new Nave { Id = _proximaNaveId++, Tipo = "Spacepod", X = pl.Pos.X + 12, Y = pl.Pos.Y };
			pod.PorZona(terra);
			_naves.Add(pod);
			var opcoes = new JsonSerializerOptions { IncludeFields = true };
			Nave? doDisco = JsonSerializer.Deserialize<Nave>("{\"Id\":9999,\"Tipo\":\"Spacepod\"}", opcoes);
			checa("**A NAVE NASCE DE TANQUE CHEIO** -- a recem-assentada e a do disco de antes do combustivel (o campo nem estava no `naves.json`)",
				  Math.Abs(NivelDoTanque(pod) - tanque) < 1e-9 && doDisco != null && Math.Abs(NivelDoTanque(doDisco) - tanque) < 1e-9,
				  $"assentada {NivelDoTanque(pod):0.##}; do disco {(doDisco != null ? NivelDoTanque(doDisco) : -1):0.##} de {tanque:0}");

			UsarNave(pl);
			MoveToZone(pl.Id, ZonaDoEspaco, noEspaco);
			TickDasNaves();
			checa("(montagem) o dono embarca no pod e a cena vai pro espaco, com a nave junto",
				  pod.PilotoId == pl.Id && pod.Zona.Equals(ZonaDoEspaco), $"piloto {pod.PilotoId}; nave em {pod.Zona}");

			// ---------------------------------------------------------------- b. gasta pela distancia
			double gastou = Andar(pod, 5 * t, 40);
			double esperado = 200 * Combustivel.GastoPorTileDoPod;
			checa($"**O TANQUE PAGA A DISTANCIA ANDADA** (*\"combustivel q gasta com a distancia andada\"*): 200 tiles de pod gastam {esperado:0.##}",
				  Math.Abs(gastou - esperado) < 0.01, $"gastou {gastou:0.####}");
			NaveNaoGastaDeTeste = true;
			double semGasto;
			try { semGasto = Andar(pod, 5 * t, 40); }
			finally { NaveNaoGastaDeTeste = false; }
			checa("   DEFEITO INJETADO (o jogo de antes: a nave anda de graca): os mesmos 200 tiles nao gastam nada -- a regua de cima reprovaria",
				  semGasto == 0, $"gastou {semGasto:0.####}");

			// ---------------------------------------------------------------- c. mais rapido gasta mais por segundo
			double lento = UmSegundo(pod, Naves.PassoPorTique(1));
			double rapido = UmSegundo(pod, Naves.PassoPorTique(Naves.VelocidadeMaxima));
			double distanciaLenta = Andar(pod, Naves.PassoPorTique(1), 195);
			double distanciaRapida = Andar(pod, Naves.PassoPorTique(Naves.VelocidadeMaxima), 5);
			checa("**MAIS RAPIDO, MAIS POR SEGUNDO** (*\"se a nave viaja mt rapido, ela gasta mais combustivel por segundo ja q ela viaja uma maior distancia "
				  + "por segundo\"*): um segundo no degrau 1 gasta 0,25/s, no 39 gasta 9,75/s -- e a MESMA distancia custa o mesmo tanque nas duas",
				  Math.Abs(lento - 0.25) < 0.01 && Math.Abs(rapido - 9.75) < 0.05 && Math.Abs(distanciaLenta - distanciaRapida) < 0.01,
				  $"lento {lento:0.###}/s; rapido {rapido:0.###}/s; 1.040 px devagar {distanciaLenta:0.###}, depressa {distanciaRapida:0.###}");
			GastoPorTempoDeTeste = true;
			double lentoRelogio, rapidoRelogio;
			try { lentoRelogio = UmSegundo(pod, Naves.PassoPorTique(1)); rapidoRelogio = UmSegundo(pod, Naves.PassoPorTique(Naves.VelocidadeMaxima)); }
			finally { GastoPorTempoDeTeste = false; }
			checa("   DEFEITO INJETADO (o tanque como relogio, o mesmo por tique): a nave rapida gasta por segundo o mesmo que a lenta -- a regua de cima reprovaria",
				  Math.Abs(lentoRelogio - rapidoRelogio) < 0.01, $"lento {lentoRelogio:0.###}/s; rapido {rapidoRelogio:0.###}/s");

			// ---------------------------------------------------------------- d. o tanque seco para a nave de vez
			pod.Tanque = 0.001;
			List<string> secou = Ouvir(() => { pl.Pos += new Vec2(5 * t, 0); TickDasNaves(); });
			bool paraNoEspaco = TanqueVazio(pod) && !PodeMexerOCorpo(pl);
			checa("**O TANQUE SECO PARA A NAVE DE VEZ** (o dono escolheu *\"para de vez\"*): no espaco o piloto nao anda mais, e ouve o que houve",
				  paraNoEspaco && secou.Any(f => f.Contains("fica sem combustível e para de vez", StringComparison.Ordinal)),
				  $"tanque {NivelDoTanque(pod):0.###}; anda {PodeMexerOCorpo(pl)}; {Ultimos(secou)}");
			TanqueVazioAndaDeTeste = true;
			bool andaSeco;
			try { andaSeco = PodeMexerOCorpo(pl); }
			finally { TanqueVazioAndaDeTeste = false; }
			checa("   DEFEITO INJETADO (o tanque seco nao segura): o piloto anda com a nave seca -- a regua de cima reprovaria", andaSeco, "");

			// ---------------------------------------------------------------- e. abastecer
			pl.Ficha.Recursos = 0;
			List<string> semBolso = Ouvir(() => ComandoDeInteracao(pl, "nave_abastecer", ""));
			pl.Ficha.Recursos = 300;
			List<string> meio = Ouvir(() => ComandoDeInteracao(pl, "nave_abastecer", ""));
			double depoisDoMeio = NivelDoTanque(pod);
			pl.Ficha.Recursos = 2_000;
			ComandoDeInteracao(pl, "nave_abastecer", "");
			checa("**ABASTECER COBRA RECURSOS DE QUEM PEDE, AO LEME E NO ESPACO** (*\"pegar recursos e abastecer a nave\"*): sem nada no bolso ele ouve como "
				  + "conseguir; com 300, o tanque recebe 300 e o bolso esvazia; com 2.000, enche -- e sobram 1.300",
				  semBolso.Any(f => f.Contains("Minere num planeta", StringComparison.Ordinal))
				  && Math.Abs(depoisDoMeio - 300) < 1 && meio.Any(f => f.Contains("os recursos acabaram antes de encher", StringComparison.Ordinal))
				  && Math.Abs(NivelDoTanque(pod) - tanque) < 1e-6 && Math.Abs(pl.Ficha.Recursos - 1_300) < 1 && PodeMexerOCorpo(pl),
				  $"meio {depoisDoMeio:0.##}; cheio {NivelDoTanque(pod):0.##}; bolso {pl.Ficha.Recursos:0}; anda {PodeMexerOCorpo(pl)}; {Ultimos(semBolso)} | {Ultimos(meio)}");
			pod.Tanque = 0;
			pl.Ficha.Recursos = 5_000;
			AbastecerDeGracaDeTeste = true;
			try { ComandoDeInteracao(pl, "nave_abastecer", ""); }
			finally { AbastecerDeGracaDeTeste = false; }
			checa("   DEFEITO INJETADO (abastecer de graca): o tanque enche e o bolso nao mexe -- a regua de cima reprovaria",
				  NivelDoTanque(pod) >= tanque - 1 && Math.Abs(pl.Ficha.Recursos - 5_000) < 1e-9, $"tanque {NivelDoTanque(pod):0}; bolso {pl.Ficha.Recursos:0}");

			// "ATE ALGUEM TRAZER RECURSOS": o pod parado no espaco, seco, e outro corpo que chega com o bolso cheio
			Desembarcar(pl, pod);
			pod.Tanque = 0;
			quemTraz = Forjar("bancada: quem traz recursos", new Vec2(pod.X + t, pod.Y), ZonaDoEspaco, "bancada_quem_traz");
			quemTraz.Ficha.Recursos = 400;
			ComandoDeInteracao(quemTraz, "nave_abastecer", "");
			checa("**QUEM VEM TRAZER RECURSOS ABASTECE A NAVE A DERIVA** (*\"ate alguem trazer recursos\"*): o pod seco parado no espaco recebe 400, pagos "
				  + "pelo bolso de quem chegou", Math.Abs(NivelDoTanque(pod) - 400) < 1 && quemTraz.Ficha.Recursos < 1,
				  $"tanque {NivelDoTanque(pod):0}; bolso dele {quemTraz.Ficha.Recursos:0}");

			// ---------------------------------------------------------------- f. so vai pra mochila de tanque cheio
			MoveToZone(pl.Id, ZonaDoEspaco, new Vec2(pod.X - t, pod.Y));
			if (pl.Mochila.Cheio) pl.Mochila.Tirar(pl.Mochila.Pilhas[0].Id, pl.Mochila.Pilhas[0].Quantidade);
			List<string> recusa = Ouvir(() => ComandoDeInteracao(pl, "nave_pegar", ""));
			bool ficou = _naves.Contains(pod);
			checa("**SO VAI PRA MOCHILA DE TANQUE CHEIO**: recolher e assentar de novo seria abastecer de graca em qualquer lugar -- a nave de tanque pela "
				  + "metade fica, e ouve-se o que falta", ficou && recusa.Any(f => f.Contains("só vai pra mochila de tanque cheio", StringComparison.Ordinal)),
				  $"ainda no mundo {ficou}; {Ultimos(recusa)}");
			RecolheComTanqueFuradoDeTeste = true;
			try { ComandoDeInteracao(pl, "nave_pegar", ""); }
			finally { RecolheComTanqueFuradoDeTeste = false; }
			bool recolheu = !_naves.Contains(pod) && pl.Mochila.Quantos("Spacepod") > 0;
			checa("   DEFEITO INJETADO (recolhe de tanque furado): a nave vai pra mochila -- e voltaria cheia -- a regua de cima reprovaria", recolheu,
				  $"no mundo {_naves.Contains(pod)}; na mochila {pl.Mochila.Quantos("Spacepod")}");
			if (pl.Mochila.Quantos("Spacepod") > 0) pl.Mochila.Tirar("Spacepod", pl.Mochila.Quantos("Spacepod"));
			if (!_naves.Contains(pod)) { pod.Tanque = -1; pod.PorZona(ZonaDoEspaco); pod.X = pl.Pos.X + t; pod.Y = pl.Pos.Y; _naves.Add(pod); }

			// ---------------------------------------------------------------- g. ao leme o Ki nao paga o voo
			MoveToZone(pl.Id, terra, berco);
			pod.PorZona(terra);
			pod.X = berco.X + 12;
			pod.Y = berco.Y;
			pod.Tanque = -1;
			UsarNave(pl);
			pl.Voando = true;
			pl.Altitude = Voo.AlturaDePairar;
			double KiDepoisDeUmSegundo()
			{
				pl.Ficha.Ki = pl.Ficha.MaxKi * 0.5;
				double antes = pl.Ficha.Ki;
				for (int i = 0; i < 30; i++) TickDoVoo(pl, (float)Protocol.TickSeconds);
				return antes - pl.Ficha.Ki;
			}
			double kiAoLeme = KiDepoisDeUmSegundo();
			PilotoPagaOVooDeTeste = true;
			double kiDeAntes;
			try { kiDeAntes = KiDepoisDeUmSegundo(); }
			finally { PilotoPagaOVooDeTeste = false; }
			checa("**AO LEME QUEM PAGA O VOO E A NAVE** (o dono escolheu *\"so combustivel\"*): um segundo no ar ao leme nao tira Ki do piloto",
				  pl.Voando && Math.Abs(kiAoLeme) < 1e-9, $"voando {pl.Voando}; Ki gasto {kiAoLeme:0.###}");
			checa("   DEFEITO INJETADO (o Ki do voo ao leme, o jogo de antes): o mesmo segundo tira Ki do piloto -- a regua de cima reprovaria",
				  kiDeAntes > 0, $"Ki gasto {kiDeAntes:0.###}");

			// ---------------------------------------------------------------- h. seco num planeta: desce, pousa, e nao sobe nem lanca
			pod.Tanque = 0.001;
			pl.Pos += new Vec2(5 * t, 0);
			TickDasNaves();
			for (int i = 0; i < 90 && pl.Altitude > 0f; i++) TickDoVoo(pl, (float)Protocol.TickSeconds);
			pl.QuerSubir = true;
			pl.AvisoDeTetoAte = 0;
			List<string> naoSobe = Ouvir(() => TickDoVoo(pl, (float)Protocol.TickSeconds));
			pl.QuerSubir = false;
			List<string> naoLanca = Ouvir(() => ComandoDeInteracao(pl, "nave_lancar", ""));
			checa("**SECO NUM PLANETA, A NAVE DESCE E POUSA** -- e nao decola (a tecla de subir) nem lanca (o menu), e diz como abastecer",
				  !pl.Voando && pl.Altitude <= 0f && pod.LancaEm == 0 && naoSobe.Any(f => f.Contains("está sem combustível", StringComparison.Ordinal))
				  && naoLanca.Any(f => f.Contains("está sem combustível", StringComparison.Ordinal)),
				  $"voando {pl.Voando}; altura {pl.Altitude:0.#}; lanca {pod.LancaEm}; {Ultimos(naoSobe)} | {Ultimos(naoLanca)}");
			Desembarcar(pl, pod);

			// ---------------------------------------------------------------- i. a nave grande sozinha gasta o passo e para seca
			grande = new Nave
			{
				Id = _proximaNaveId++, Tipo = NaveGrande.Tipo, X = noEspaco.X, Y = noEspaco.Y + 30 * t, DonoConta = pl.Conta, DonoNome = pl.Name,
				ErguidaEm = NowMs(), ArmaduraMax = 1000, Armadura = 1000, TemRumo = true, RumoX = noEspaco.X + 60_000, RumoY = noEspaco.Y + 30 * t,
				RumoPor = pl.Conta,
			};
			grande.PorZona(ZonaDoEspaco);
			_naves.Add(grande);
			double antesDoPasso = NivelDoTanque(grande);
			TickDasNaves();
			double passo = Naves.PassoPorTique(grande.Velocidade);
			double doPasso = antesDoPasso - NivelDoTanque(grande);
			grande.Tanque = 0.0001;
			List<string> parou = Ouvir(() => { TickDasNaves(); TickDasNaves(); });
			checa("**A NAVE GRANDE SOZINHA GASTA O PASSO E PARA SECA**: o passo do rumo paga o tanque dela, e seca ela desliga o rumo onde estiver e avisa o dono",
				  Math.Abs(doPasso - passo / t * Combustivel.GastoPorTileDaNaveGrande) < 1e-6 && !grande.TemRumo
				  && parou.Any(f => f.Contains("fica sem combustível", StringComparison.Ordinal)),
				  $"o passo gastou {doPasso:0.######} (esperado {passo / t * Combustivel.GastoPorTileDaNaveGrande:0.######}); rumo {grande.TemRumo}; {Ultimos(parou)}");
		}
		finally
		{
			NaveNaoGastaDeTeste = TanqueVazioAndaDeTeste = RecolheComTanqueFuradoDeTeste = PilotoPagaOVooDeTeste = AbastecerDeGracaDeTeste = false;
			GastoPorTempoDeTeste = false;
			if (NaveDoPiloto(pl) is { } aoLeme) Desembarcar(pl, aoLeme);
			if (pod != null) _naves.Remove(pod);
			if (grande != null) _naves.Remove(grande);
			if (quemTraz != null) RemoverNpc(quemTraz);
			GravarNaves();
			pl.Ficha.Recursos = recursosAntes;
			pl.Ficha.Ki = kiAntes;
			pl.QuerSubir = pl.QuerDescer = false;
			if (!pl.Zone.Equals(zonaAntes)) MoveToZone(pl.Id, zonaAntes, posAntes);
			else pl.Pos = posAntes;
			pl.Voando = false;
			pl.Altitude = 0f;
			RecalcularVelocidade(pl);
			MandarFicha(pl);
		}
	}

	// =====================================================================
	// A NAVE GRANDE DAS CAMADAS 10 E 11: parada no espaco, o dono embarcado e de pe ao lado do console
	// =====================================================================
	private Nave NaveGrandeDeBancada(ServerPlayer dono, Vec2 onde)
	{
		var n = new Nave
		{
			Id = _proximaNaveId++, Tipo = NaveGrande.Tipo, X = onde.X, Y = onde.Y, DonoConta = dono.Conta, DonoNome = dono.Name,
			ErguidaEm = NowMs(), ArmaduraMax = 1000, Armadura = 1000,
		};
		n.PorZona(ZonaDoEspaco);
		_naves.Add(n);
		MoveToZone(dono.Id, ZonaDoEspaco, new Vec2(onde.X - ZoneCollision.TileSize, onde.Y));
		ComandoDeInteracao(dono, "nave_embarcar", "");
		dono.Pos = NaveGrande.PixelDe((NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y + 1));
		return n;
	}

	/// <summary>O que este corpo OUVIU durante o gesto (a `EscutaDeEntregas`): as linhas entregues a ele, com o autor.</summary>
	private static List<(string Autor, string Texto)> OuvidoPor(ServerPlayer quem, Action gesto)
	{
		List<(int Para, string Autor, string Texto)> todas = [];
		EscutaDeEntregas = todas;
		try { gesto(); }
		finally { EscutaDeEntregas = null; }
		return [.. todas.Where(e => e.Para == quem.Id).Select(e => (e.Autor, e.Texto))];
	}

	/// <summary>Fala pelo funil de producao, sem o freio de 400 ms entre duas falas (a bancada fala em sequencia).</summary>
	private void FalarNaBancada(ServerPlayer quem, Protocol.Fala canal, string texto)
	{
		_ultimaFala.Remove(quem.Id);
		Falar(quem, canal, texto);
	}

	// =====================================================================
	// 10. O CHAT AO LEME
	// =====================================================================
	private void RodarBancadaDaPonteQueOuve(ServerPlayer pl, Action<string, bool, string> checa)
	{
		GD.Print("  -- 10) quem pilota ouve a nave (dono, 2026-10-18) --");
		const int t = ZoneCollision.TileSize;
		ZoneKey zonaAntes = pl.Zone;
		Vec2 posAntes = pl.Pos;
		if (NaveDoPiloto(pl) is { } jaPilotava) Desembarcar(pl, jaPilotava);
		PlanetaNoEspaco daTerra = Espaco.PreFeitos().First(p => string.Equals(p.Nome, "Earth", StringComparison.OrdinalIgnoreCase));
		Nave? n = null;
		ServerPlayer? perto = null, longe = null;

		try
		{
			n = NaveGrandeDeBancada(pl, Espaco.PontoDeDecolagem(daTerra) + new Vec2(80 * t, 0));
			ZoneKey dentro = NaveGrande.ZonaDoInterior(n.Id);
			Vec2 console = NaveGrande.PixelDe(NaveGrande.CelDoConsole);
			perto = Forjar("bancada: o tripulante da ponte", console + new Vec2(2 * t, 3 * t), dentro, "bancada_tripulante_ponte");
			longe = Forjar("bancada: o tripulante do porao", console + new Vec2(40 * t, 40 * t), dentro, "bancada_tripulante_porao");
			ComandoDeInteracao(pl, "nave_pilotar", "");
			checa("(montagem) a nave grande no espaco: o dono ao leme, dois tripulantes la dentro (um perto da ponte, outro no fundo)",
				  n.PilotoId == pl.Id && pl.Zone.Equals(n.Zona) && perto.Zone.Equals(dentro) && longe.Zone.Equals(dentro) && pl.BonecoLargado != null,
				  $"piloto {n.PilotoId}; corpo do dono em {pl.Zone}");

			// ---------------------------------------------------------------- a. ele ouve quem esta perto da ponte
			var ouviu = OuvidoPor(pl, () => FalarNaBancada(perto, Protocol.Fala.Diz, "piloto, tudo certo ai?"));
			checa("**AO LEME SE OUVE A NAVE** (*\"enquanto vc pilota, deveria dar pra escutar e ver o chat q rola dentro da nave\"*): a fala do tripulante "
				  + "perto da ponte chega em quem pilota", ouviu.Any(e => e.Autor == perto.Name && e.Texto == "piloto, tudo certo ai?"),
				  string.Join(" | ", ouviu.Select(e => $"{e.Autor}: {e.Texto}")));
			PonteSurdaDeTeste = true;
			List<(string Autor, string Texto)> surdo;
			try { surdo = OuvidoPor(pl, () => FalarNaBancada(perto, Protocol.Fala.Diz, "piloto, tudo certo ai?")); }
			finally { PonteSurdaDeTeste = false; }
			checa("   DEFEITO INJETADO (a ponte surda, o jogo de antes): a mesma fala nao chega em quem pilota -- a regua de cima reprovaria",
				  surdo.Count == 0, string.Join(" | ", surdo.Select(e => $"{e.Autor}: {e.Texto}")));

			// ---------------------------------------------------------------- b. e como se ele estivesse na ponte: o fundo da nave nao chega
			var doFundo = OuvidoPor(pl, () => FalarNaBancada(longe, Protocol.Fala.Diz, "alguem me ouve aqui do fundo?"));
			checa("...**IGUAL COMO SE NAO ESTIVESSE PILOTANDO**: a fala de quem esta no fundo da nave, longe da ponte, nao chega -- a distancia e a do corpo "
				  + "que ficou no console", doFundo.Count == 0, string.Join(" | ", doFundo.Select(e => $"{e.Autor}: {e.Texto}")));

			// ---------------------------------------------------------------- c. o que ele fala sai la dentro, e uma vez so pra ele
			var noTripulante = OuvidoPor(perto, () => FalarNaBancada(pl, Protocol.Fala.Diz, "segurem-se, vamos acelerar"));
			var noPiloto = OuvidoPor(pl, () => FalarNaBancada(pl, Protocol.Fala.Diz, "segurem-se, vamos acelerar de novo"));
			checa("**E O QUE ELE FALA SAI LA DENTRO**, da boca do corpo da ponte -- e chega nele mesmo uma vez so (nao duas: a de fora e a de dentro)",
				  noTripulante.Any(e => e.Autor == pl.Name && e.Texto == "segurem-se, vamos acelerar")
				  && noPiloto.Count(e => e.Autor == pl.Name && e.Texto == "segurem-se, vamos acelerar de novo") == 1,
				  $"tripulante ouviu: {string.Join(" | ", noTripulante.Select(e => $"{e.Autor}: {e.Texto}"))}; o piloto ouviu a dele {noPiloto.Count} vez(es)");
			LemeMudoPraDentroDeTeste = true;
			List<(string Autor, string Texto)> mudo;
			try { mudo = OuvidoPor(perto, () => FalarNaBancada(pl, Protocol.Fala.Diz, "segurem-se, vamos acelerar")); }
			finally { LemeMudoPraDentroDeTeste = false; }
			checa("   DEFEITO INJETADO (a fala do leme so do lado de fora, o jogo de antes): o tripulante nao ouve o piloto -- a regua de cima reprovaria",
				  mudo.Count == 0, string.Join(" | ", mudo.Select(e => $"{e.Autor}: {e.Texto}")));
		}
		finally
		{
			PonteSurdaDeTeste = LemeMudoPraDentroDeTeste = false;
			if (n != null && NaveDoPiloto(pl) == n) PararDePilotar(pl, n);
			if (perto != null) RemoverNpc(perto);
			if (longe != null) RemoverNpc(longe);
			if (n != null) _naves.Remove(n);
			GravarNaves();
			if (!pl.Zone.Equals(zonaAntes)) MoveToZone(pl.Id, zonaAntes, posAntes);
			else pl.Pos = posAntes;
			pl.Voando = false;
			pl.Altitude = 0f;
			RecalcularVelocidade(pl);
			MandarFicha(pl);
		}
	}

	// =====================================================================
	// 11. O OBSERVAR
	// =====================================================================
	private void RodarBancadaDoObservar(ServerPlayer pl, Action<string, bool, string> checa)
	{
		GD.Print("  -- 11) observar a nave de fora (dono, 2026-10-18) --");
		const int t = ZoneCollision.TileSize;
		ZoneKey zonaAntes = pl.Zone;
		Vec2 posAntes = pl.Pos;
		if (NaveDoPiloto(pl) is { } jaPilotava) Desembarcar(pl, jaPilotava);
		PlanetaNoEspaco daTerra = Espaco.PreFeitos().First(p => string.Equals(p.Nome, "Earth", StringComparison.OrdinalIgnoreCase));
		PlanetaNoEspaco namek = Espaco.PreFeitos().First(p => string.Equals(p.Nome, "Namek", StringComparison.OrdinalIgnoreCase));
		Nave? n = null;
		ServerPlayer? tripulante = null, laFora = null;

		void Observar() { pl.Pos = NaveGrande.PixelDe((NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y + 1)); ComandoDeInteracao(pl, "nave_observar", ""); }
		bool OlhoNoCasco() => n != null && pl.Zone.Equals(n.Zona) && (pl.Pos - new Vec2(n.X, n.Y)).Length < 1;

		try
		{
			n = NaveGrandeDeBancada(pl, Espaco.PontoDeDecolagem(daTerra) + new Vec2(120 * t, 0));
			ZoneKey dentro = NaveGrande.ZonaDoInterior(n.Id);
			tripulante = Forjar("bancada: o tripulante", NaveGrande.PixelDe(NaveGrande.CelDoConsole) + new Vec2(2 * t, 3 * t), dentro, "bancada_tripulante_olho");

			// ---------------------------------------------------------------- a. o olho sai e o corpo fica
			Observar();
			ServerPlayer? corpo = pl.BonecoLargado;
			checa("**OBSERVAR LEVA O OLHO PRA FORA** (*\"deveria 'assistir' o exterior da nave sem pode controla-la\"*): o corpo fica de pe na ponte, e o olho "
				  + "vai pro lado de fora, no casco", pl.ObservaNave == n.Id && OlhoNoCasco() && corpo != null && corpo.Zone.Equals(dentro) && n.PilotoId == 0,
				  $"observa {pl.ObservaNave}; olho em {pl.Zone} ({pl.Pos.X:0},{pl.Pos.Y:0}), casco ({n.X:0},{n.Y:0}); corpo em {corpo?.Zone}; piloto {n.PilotoId}");

			// ---------------------------------------------------------------- b. ninguem o ve, ele nao esbarra e nao anda
			bool oculto = EstaOculto(pl.Id), naGrade = EntraNaGrade(pl), anda = PodeMexerOCorpo(pl);
			OlhoAVistaDeTeste = true;
			bool aVista;
			try { aVista = !EstaOculto(pl.Id); }
			finally { OlhoAVistaDeTeste = false; }
			checa("...o olho e SO UM OLHO: ninguem o ve (o `Oculto` do snapshot), ele nao entra na grade de corpos e nao anda -- *\"sem pode controla-la\"*",
				  oculto && !naGrade && !anda, $"oculto {oculto}; na grade {naGrade}; anda {anda}");
			checa("   DEFEITO INJETADO (o olho a vista): quem esta la fora o veria -- a regua de cima reprovaria", aVista, "");

			// ---------------------------------------------------------------- b2. o vazio nao sufoca o olho (o corpo esta abrigado na ponte)
			bool sufoca = SufocaAgora(pl);
			OlhoSufocaDeTeste = true;
			bool sufocariaAntes;
			try { sufocariaAntes = SufocaAgora(pl); }
			finally { OlhoSufocaDeTeste = false; }
			checa("...e o VAZIO nao sufoca o olho: o corpo esta abrigado na ponte, e a ficha e a mesma (a foto do `--diagcapital` mostrou a vida descendo "
				  + "enquanto se observava no espaco)", Espaco.EhEspaco(pl.Zone) && !sufoca, $"no espaco {Espaco.EhEspaco(pl.Zone)}; sufoca {sufoca}");
			checa("   DEFEITO INJETADO (o olho sufoca como corpo de fora): um Humano sem traje observando no espaco perderia a vida -- a regua de cima reprovaria",
				  sufocariaAntes, "");

			// ---------------------------------------------------------------- c. ele anda com a nave
			n.TemRumo = true;
			n.RumoX = n.X + 80_000;
			n.RumoY = n.Y;
			n.RumoPor = pl.Conta;
			Vec2 partida = new(n.X, n.Y);
			for (int i = 0; i < 10; i++) TickDasNaves();
			bool foiJunto = OlhoNoCasco() && (new Vec2(n.X, n.Y) - partida).Length > 1;
			OlhoParadoDeTeste = true;
			Vec2 olhoAntes = pl.Pos;
			try { for (int i = 0; i < 10; i++) TickDasNaves(); }
			finally { OlhoParadoDeTeste = false; }
			bool ficouPraTras = (pl.Pos - olhoAntes).Length < 1 && !OlhoNoCasco();
			TickDasNaves();
			checa("**O OLHO ANDA COM A NAVE** (*\"bom pros jogadores q querem ver a nave ir pelo espaco\"*): ela segue sozinha pelo nav system e o olho vai junto, "
				  + "tique a tique", foiJunto, $"nave andou {(new Vec2(n.X, n.Y) - partida).Length:0} px; olho no casco {OlhoNoCasco()}");
			checa("   DEFEITO INJETADO (o olho parado): a nave vai embora e o olho fica onde comecou -- a regua de cima reprovaria", ficouPraTras, "");

			// ---------------------------------------------------------------- d. passar por cima de um planeta nao o pousa la
			Vec2 sobreNamek = namek.Pos + new Vec2(0, (float)namek.Raio * 0.5f);
			n.TemRumo = false;
			n.X = sobreNamek.X;
			n.Y = sobreNamek.Y;
			TickDasNaves();
			TickDoEspaco(pl);
			bool ficouNoEspaco = Espaco.EhEspaco(pl.Zone) && OlhoNoCasco();
			OlhoPousaDeTeste = true;
			try { TickDoEspaco(pl); }
			finally { OlhoPousaDeTeste = false; }
			bool pousou = !Espaco.EhEspaco(pl.Zone);
			checa("...a nave que passa por cima de um planeta SEM POUSAR nao pousa o olho nele -- quem decide onde ele vai e a nave",
				  ficouNoEspaco, $"olho em {pl.Zone}");
			checa("   DEFEITO INJETADO (o olho como corpo comum pro espaco): o disco de Namek o pousa sozinho -- a regua de cima reprovaria", pousou, $"olho em {pl.Zone}");
			if (pousou) TickDasNaves();   // o tique dos olhos o devolve ao casco, no espaco

			// ---------------------------------------------------------------- e. ele troca de zona com a nave
			n.TemRumo = true;
			n.RumoX = namek.Pos.X;
			n.RumoY = namek.Pos.Y;
			for (int i = 0; i < 40 && n.TemRumo; i++) TickDasNaves();
			TickDasNaves();
			ZoneKey deNamek = ZoneKey.Premade(namek.Nome);
			checa("**E TROCA DE ZONA COM ELA**: a nave pousa sozinha em Namek e o olho desce junto, no casco", n.Zona.Equals(deNamek) && OlhoNoCasco(),
				  $"nave em {n.Zona}; olho em {pl.Zone} ({pl.Pos.X:0},{pl.Pos.Y:0})");

			// ---------------------------------------------------------------- f. ele ouve la dentro, e nao la fora
			laFora = Forjar("bancada: quem passa la fora", pl.Pos + new Vec2(2 * t, 0), n.Zona, "bancada_quem_passa");
			var deDentro = OuvidoPor(pl, () => FalarNaBancada(tripulante, Protocol.Fala.Diz, "ja chegamos?"));
			var deFora = OuvidoPor(pl, () => FalarNaBancada(laFora, Protocol.Fala.Diz, "que nave enorme!"));
			var oQueEleDiz = OuvidoPor(tripulante, () => FalarNaBancada(pl, Protocol.Fala.Diz, "chegamos em Namek"));
			var laForaOuviu = OuvidoPor(laFora, () => FalarNaBancada(pl, Protocol.Fala.Diz, "chegamos em Namek!"));
			checa("**QUEM OBSERVA OUVE A NAVE, E NAO A RUA**: a fala do tripulante chega, a de quem passa do lado de fora nao; o que ele fala sai la dentro e nao "
				  + "la fora (ninguem ve o olho)", deDentro.Any(e => e.Autor == tripulante.Name) && deFora.Count == 0
				  && oQueEleDiz.Any(e => e.Autor == pl.Name) && laForaOuviu.Count == 0,
				  $"de dentro {deDentro.Count}; de fora {deFora.Count}; o tripulante ouviu {oQueEleDiz.Count}; la fora ouviram {laForaOuviu.Count}");

			// ---------------------------------------------------------------- g. apanhar no corpo da ponte o traz de volta
			corpo = pl.BonecoLargado;
			if (corpo != null) MarcarAgressao(corpo, tripulante);
			TickDeQuemVolta();
			checa("**APANHAR NO CORPO O TRAZ DE VOLTA** (*\"caso alguem te bata vc volta pro corpo principal\"*): o olho volta pro corpo, na ponte",
				  pl.ObservaNave == 0 && pl.Zone.Equals(dentro) && pl.BonecoLargado == null && !_observadores.Contains(pl.Id),
				  $"observa {pl.ObservaNave}; em {pl.Zone}");

			// ---------------------------------------------------------------- h. o mesmo verbo devolve o olho
			Observar();
			bool saiu = pl.ObservaNave == n.Id && OlhoNoCasco();
			List<string> voltou = Ouvir(() => ComandoDeInteracao(pl, "nave_observar", ""));
			checa("...e o MESMO verbo (a tecla E de quem observa: *Voltar ao corpo*) devolve o olho ao corpo, de pe na ponte",
				  saiu && pl.ObservaNave == 0 && pl.Zone.Equals(dentro) && voltou.Any(f => f.Contains("você volta ao seu corpo", StringComparison.Ordinal)),
				  $"saiu {saiu}; observa {pl.ObservaNave}; em {pl.Zone}; {Ultimos(voltou)}");

			// ---------------------------------------------------------------- i. a nave que explode deixa o olho do lado de fora
			Observar();
			ZoneKey ondeEstava = n.Zona;
			Vec2 casco = new(n.X, n.Y);
			DestruirNave(n, null);
			_naves.Remove(n);
			checa("**A NAVE QUE EXPLODE DEIXA O OLHO ONDE ELA ESTAVA**: ele volta a si do lado de fora, no casco -- e nao no destino de emergencia (a Terra)",
				  pl.ObservaNave == 0 && pl.Zone.Equals(ondeEstava) && (pl.Pos - casco).Length < 2 * t && pl.BonecoLargado == null,
				  $"observa {pl.ObservaNave}; em {pl.Zone} a {(pl.Pos - casco).Length:0} px do casco");
			n = null;
		}
		finally
		{
			OlhoAVistaDeTeste = OlhoParadoDeTeste = OlhoPousaDeTeste = OlhoSufocaDeTeste = false;
			if (EhOlhoDeFora(pl)) PararDeObservar(pl, "");
			if (pl.BonecoLargado != null) VoltarProCorpo(pl, "");
			if (tripulante != null) RemoverNpc(tripulante);
			if (laFora != null) RemoverNpc(laFora);
			if (n != null) _naves.Remove(n);
			GravarNaves();
			if (!pl.Zone.Equals(zonaAntes)) MoveToZone(pl.Id, zonaAntes, posAntes);
			else pl.Pos = posAntes;
			pl.Voando = false;
			pl.Altitude = 0f;
			RecalcularVelocidade(pl);
			MandarFicha(pl);
		}
	}
}
