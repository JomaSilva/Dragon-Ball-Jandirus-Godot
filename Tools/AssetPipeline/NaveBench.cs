using Jandirus.Core.Combat;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DAS CONTAS DA NAVE (`nave`).
///
/// ============================ O QUE ELA PROVA, E O QUE ELA NAO PODE PROVAR ============================
/// Ela prova os NUMEROS, e todos saem do Core de producao (<see cref="Naves"/> e <see cref="Espaco"/>):
/// a escada de preco do upgrade, a espera do lancamento, a viagem Terra→Namek com e sem nave, e o
/// TETO -- o passo por tique em que o pouso deixaria de disparar.
///
/// Ela nao prova a CORRENTE (que assentar cria a nave, que embarcar liga a velocidade, que lancar cai
/// no `Decolar` de producao, que pousar leva a nave junto). Aquilo e a bancada AO VIVO `--naveteste`,
/// e as duas existem pelo motivo de sempre neste projeto: ja aconteceu de as regras estarem certas e
/// nao haver chamador nenhum.
/// ==================================================================================================
///
/// ============================ O TETO TEM QUE DISPARAR (regra 0.7) ============================
/// "Um teto que nunca e atingido e indistinguivel de teto nenhum." Por isso a tabela de tunelamento
/// vai ALEM do teto do jogo: ela mostra o fator em que o passo por tique passa do diametro do menor
/// planeta, e afirma que o teto escolhido (39, o do DM) fica do lado seguro dessa linha.
/// ============================================================================================
///
///     dotnet run --project Tools/AssetPipeline -- nave
/// </summary>
public static class NaveBench
{
	private static int _falhas;

	private static void Afirmar(bool ok, string oque)
	{
		if (!ok) _falhas++;
		Console.WriteLine($"  [{(ok ? "OK  " : "FALHA")}] {oque}");
	}

	public static int Run()
	{
		_falhas = 0;

		AEscadaDoUpgrade();
		AEsperaDoLancamento();
		AViagem();
		OTunelamento();
		OFatorDoPiloto();
		ANaveGrande();
		APlantaDoInterior();
		OFoguete();
		OCascoEOSol();

		Console.WriteLine(_falhas == 0
			? "\n===== BANCADA DA NAVE: tudo OK ====="
			: $"\n===== BANCADA DA NAVE: {_falhas} FALHA(S) =====");
		return _falhas == 0 ? 0 : 1;
	}

	// =====================================================================
	private static void AEscadaDoUpgrade()
	{
		Console.WriteLine("\n-- o preco de cada degrau (`1000*Speed`, PlanetTech.dm:192) --");

		double total = 0;
		for (int v = Naves.VelocidadeInicial; v < Naves.VelocidadeMaxima; v++) total += Naves.CustoDoUpgrade(v);

		Console.WriteLine($"  degrau 1 -> 2   : {Naves.CustoDoUpgrade(1),12:N0} z");
		Console.WriteLine($"  degrau 19 -> 20 : {Naves.CustoDoUpgrade(19),12:N0} z");
		Console.WriteLine($"  degrau 38 -> 39 : {Naves.CustoDoUpgrade(38),12:N0} z");
		Console.WriteLine($"  do 1 ao {Naves.VelocidadeMaxima}     : {total,12:N0} z  (a Spacepod em si custa 100.000)");

		Afirmar(Math.Abs(Naves.CustoDoUpgrade(1) - 1000) < 1e-9, "o primeiro degrau custa 1.000 z");
		Afirmar(total > 700_000, "chegar ao teto custa mais que sete pods");
	}

	// =====================================================================
	private static void AEsperaDoLancamento()
	{
		Console.WriteLine("\n-- a espera do lancamento (`sleep(400/Speed)`, PlanetTech.dm:68) --");
		foreach (int v in new[] { 1, 5, 11, 20, 39 })
			Console.WriteLine($"  velocidade {v,2}x : {Naves.SegundosDeLancamento(v),6:0.0} s");

		// O DM ESCREVE A UNIDADE NO PROPRIO CHAT: `"ETA [((400/Speed)/10)] second(s)"` (:66). Sao
		// DECIMOS, e nao quadros -- a armadilha ja registrada na memoria deste projeto.
		Afirmar(Math.Abs(Naves.SegundosDeLancamento(1) - 40) < 1e-9, "no degrau 1 a espera e 40 s (400 decimos)");
		Afirmar(Naves.SegundosDeLancamento(39) < 1.1, "no teto ela cai pra ~1 s");
	}

	// =====================================================================
	private static void AViagem()
	{
		// A DISTANCIA MEDIDA ENTRE OS DOIS CORPOS, e nao a constante que a derivou.
		//
		// Elas nao sao o mesmo numero: `DistanciaTerraNamek` e a ESCALA do universo (7 dias de voo),
		// e Namek foi posta em `(d*0,71 ; -d*0,70)`, cuja hipotenusa da 0,997 daquilo. Sao 4.765 px
		// de diferenca -- irrelevantes pro jogo e relevantes pra bancada: medir a constante em vez
		// da rota seria medir a intencao em vez do mundo.
		PlanetaNoEspaco terra = Espaco.PreFeitos().First(p => p.Nome == "Earth");
		PlanetaNoEspaco namek = Espaco.PreFeitos().First(p => p.Nome == "Namek");
		double d = (namek.Pos - terra.Pos).Length;

		Console.WriteLine($"\n-- Terra->Namek: {d:N0} px medidos entre os dois corpos "
						  + $"(a escala da constante e {Espaco.DistanciaTerraNamek:N0}) --");
		Console.WriteLine("  veloc     px/s        min reais      dias in-game");

		foreach (int v in new[] { 1, 2, 5, 11, 20, 39 })
			Console.WriteLine($"  {v,3}x   {MoveRules.BaseSpeedPx * Naves.Fator(v),8:N0}   "
							  + $"{Naves.SegundosDeViagem(d, v) / 60,12:0.0}   {Naves.DiasInGame(d, v),12:0.00}");

		// A LINHA DE BAIXO E A QUE IMPORTA: sem nave, a viagem e a que sempre foi. A conta tem que
		// bater com a do `Espaco`, senao a carta estelar e a nave estariam prometendo coisas
		// diferentes pra mesma travessia.
		double semNave = Espaco.SegundosDeViagem(d);
		Afirmar(Math.Abs(Naves.SegundosDeViagem(d, 1) - semNave) < 1e-6,
				"no degrau 1 a nave nao acelera nada -- e o pod do DM, que so carrega o piloto");
		// A FOLGA E DE 0,05 DIA porque a rota medida e 0,3% mais curta que a escala -- ver o
		// comentario da distancia acima. Exigir igualdade exata mediria a constante, nao a viagem.
		Afirmar(Math.Abs(Espaco.DiasInGame(d) - Espaco.DiasTerraNamek) < 0.05,
				$"a pe sao os {Espaco.DiasTerraNamek:0} dias do anime ({semNave / 60:0} min reais)");
		Afirmar(Naves.SegundosDeViagem(d, Naves.VelocidadeMaxima) / 60 < 5,
				"no teto a viagem cabe em menos de 5 minutos reais");
	}

	// =====================================================================
	/// <summary>
	/// O TETO QUE A AMOSTRAGEM IMPOE. Ver <see cref="Naves.TetoSemTunelamento"/>.
	///
	/// O menor planeta deste universo tem raio 150 (`Sistemas`), ou seja 300 px de diametro. O pouso
	/// (`Espaco.PlanetaSob`) e a queimadura (`Sistemas.EstrelaPerto`) sao perguntas POR PONTO a 30 Hz:
	/// quem anda mais que o diametro entre dois tiques passa por dentro sem que a pergunta seja feita.
	/// </summary>
	private static void OTunelamento()
	{
		const double diametroDoMenorPlaneta = 300;   // R=150
		const double diametroDaMenorEstrela = 720;   // AnaVermelha, R=360

		Console.WriteLine($"\n-- o teto da amostragem (30 Hz; menor planeta {diametroDoMenorPlaneta:0} px "
						  + $"de diametro, menor estrela {diametroDaMenorEstrela:0}) --");
		Console.WriteLine("  fator    px/s     passo/tique   fura o planeta?");

		// VAI ALEM DO TETO DO JOGO de proposito: um limite que a bancada nunca alcanca nao prova nada.
		foreach (int v in new[] { 11, 39, 56, 100, 200 })
		{
			double passo = MoveRules.BaseSpeedPx * v / 30.0;   // CRU, sem o clamp -- e o ponto do teste
			Console.WriteLine($"  {v,4}x  {MoveRules.BaseSpeedPx * v,8:N0}   {passo,10:N0} px   "
							  + (passo > diametroDoMenorPlaneta ? "SIM" : "nao"));
		}

		Afirmar(Naves.PassoPorTique(Naves.VelocidadeMaxima) < diametroDoMenorPlaneta,
				$"no teto do jogo ({Naves.VelocidadeMaxima}x) o passo e "
				+ $"{Naves.PassoPorTique(Naves.VelocidadeMaxima):0} px -- cabe no menor planeta");
		Afirmar(MoveRules.BaseSpeedPx * 100 / 30.0 > diametroDoMenorPlaneta,
				"e a 100x ele NAO cabe: o teto existe, e a bancada o alcanca (regra 0.7)");

		// O CLAMP TEM QUE SER O DONO DO NUMERO, e nao um comentario: pedir 200x tem que devolver o
		// teto. Sem esta afirmacao, subir `VelocidadeMaxima` um dia furaria o mundo em silencio.
		Afirmar(Naves.Fator(9999) <= Naves.TetoSemTunelamento,
				$"o `Fator` apara qualquer degrau no teto medido ({Naves.TetoSemTunelamento}x)");
		Afirmar(MoveRules.BaseSpeedPx * Naves.TetoSemTunelamento / 30.0 <= diametroDoMenorPlaneta,
				"e o teto medido esta na borda do menor planeta, nao alem dela");
		Afirmar(MoveRules.BaseSpeedPx * Naves.TetoSemTunelamento / 30.0 < diametroDaMenorEstrela,
				"a estrela e larga: ela so seria furada muito acima disso");
	}

	// =====================================================================
	private static void OFatorDoPiloto()
	{
		Console.WriteLine("\n-- a nave SUBSTITUI a velocidade do corpo (nao multiplica) --");

		// O CORPO NO TETO DELE: `Espeed` satura perto de 11, e `SpeedStatFrom` divide por 2.
		float corpoNoTeto = MoveRules.SpeedStatFrom(11);
		Console.WriteLine($"  corpo no teto     : {corpoNoTeto:0.0}x");
		Console.WriteLine($"  nave 1x com ele   : {Naves.FatorDoPiloto(corpoNoTeto, 1):0.0}x  (embarcar nunca piora)");
		Console.WriteLine($"  nave 39x com ele  : {Naves.FatorDoPiloto(corpoNoTeto, 39):0.0}x");
		Console.WriteLine($"  se MULTIPLICASSE  : {corpoNoTeto * 39:0.0}x  -> passo de "
						  + $"{MoveRules.BaseSpeedPx * corpoNoTeto * 39 / 30.0:N0} px/tique (furaria tudo)");

		Afirmar(Naves.FatorDoPiloto(corpoNoTeto, 1) >= corpoNoTeto,
				"o pod no degrau 1 nao deixa ninguem mais lento que a pe");
		Afirmar(Naves.FatorDoPiloto(corpoNoTeto, 39) <= Naves.TetoSemTunelamento,
				"e com a nave no teto o piloto continua abaixo do limite da amostragem");
	}

	// =====================================================================
	// A CAMADA 2 -- A CAPITAL SHIP
	// =====================================================================
	private static void ANaveGrande()
	{
		Console.WriteLine("\n-- o casco da Capital Ship (`max(intBPcap*5, 1000)`, ShipVessel.dm:63) --");

		// `expressedBP` NAO E `BP` -- personagem novo expressa entre 2 e 21 enquanto o BP bruto e bem
		// maior (armadilha 6 da PARTE 3). E por isso que o PISO de 1000 existe: sem ele, a nave de
		// dois milhoes de zeni erguida por um novato nasceria com casco 105 e cairia sozinha.
		foreach (double bp in new[] { 0.0, 21, 200, 1_000, 50_000, 2_000_000 })
			Console.WriteLine($"  expressedBP {bp,11:N0} -> casco {NaveGrande.ArmaduraMaxima(bp),14:N0}"
							  + $"  (piso de 75%: {Armadura.Piso(NaveGrande.ArmaduraMaxima(bp)),12:N0})");

		Afirmar(Math.Abs(NaveGrande.ArmaduraMaxima(21) - 1000) < 1e-9,
				"o casco de um novato cai no PISO de 1000, e nao em 105");
		Afirmar(Math.Abs(NaveGrande.ArmaduraMaxima(50_000) - 250_000) < 1e-9,
				"acima do piso ele e cinco vezes o expressedBP de quem ergueu");

		// O QUE O PISO DE 1000 COMPRA, MEDIDO: quanto de golpe alguem precisa dar pra ARRANHAR a nave
		// mais fraca possivel. Sem este numero, "tanky" e um adjetivo.
		double piso = Armadura.Piso(NaveGrande.ArmaduraMaxima(0));
		Console.WriteLine($"  pra arranhar a nave mais fraca e preciso bater com mais de {piso:N0}");
		Afirmar(piso >= 750, "a superarmadura do piso ja exige 750 de golpe -- ninguem a derruba de passagem");

		Console.WriteLine($"\n  espera do lancamento: {NaveGrande.SegundosDeLancamento:0} s (fixa; "
						  + $"o pod no degrau 1 leva {Naves.SegundosDeLancamento(1):0})");
		Afirmar(Math.Abs(NaveGrande.SegundosDeLancamento - 10) < 1e-9,
				"`sleep(100)` = 10 s, e o proprio DM escreve a frase \"ETA 10 seconds\"");

		Afirmar(NaveGrande.EhNaveGrande("Capital_Ship") && Naves.EhNave("Capital_Ship"),
				"a Capital Ship e uma NAVE (chave de zona inteira) e nao uma Obra");
		Afirmar(!Naves.EhPod("Capital_Ship") && !Naves.EhFoguete("Capital_Ship"),
				"...e os tres crivos nao se sobrepoem");
	}

	// =====================================================================
	private static void APlantaDoInterior()
	{
		Console.WriteLine("\n-- a planta do interior (`build_interior`, ShipVessel.dm:146-176) --");

		// O CUSTO E MEDIDO, NAO ESTIMADO (regra 0.5). Ela e paga UMA vez no processo inteiro, mas
		// essa unica vez acontece DENTRO de um tique -- e um tique parado e o servidor inteiro parado.
		var relogio = System.Diagnostics.Stopwatch.StartNew();
		TerrenoGerado t = NaveGrande.Planta();
		relogio.Stop();
		double ms = relogio.Elapsed.TotalMilliseconds;

		Console.WriteLine($"  {t.Largura}x{t.Altura} = {t.Largura * t.Altura:N0} celulas em {ms:0.00} ms "
						  + $"({ms * 1000 / (t.Largura * t.Altura):0.00} us por celula)");
		Afirmar(ms < 33, "ela cabe num tique de 30 Hz -- e so e paga uma vez no processo");

		// A SEGUNDA CHAMADA TEM QUE SER DE GRACA: a planta e compartilhada por todas as naves.
		var r2 = System.Diagnostics.Stopwatch.StartNew();
		TerrenoGerado t2 = NaveGrande.Planta();
		r2.Stop();
		Console.WriteLine($"  segunda chamada: {r2.Elapsed.TotalMilliseconds:0.000} ms (a planta e uma so)");
		Afirmar(ReferenceEquals(t, t2), "toda nave usa a MESMA planta -- quem separa as zonas e a ZoneKey");

		ZoneCollision c = t.Colisao;

		// O CASCO. `if(xx == 1 || yy == 1 || xx == sz || yy == sz)` (:156).
		int borda = 0;
		for (int i = 0; i < NaveGrande.Lado; i++)
		{
			if (c.BlockedCell(i, 0)) borda++;
			if (c.BlockedCell(i, NaveGrande.Lado - 1)) borda++;
			if (c.BlockedCell(0, i)) borda++;
			if (c.BlockedCell(NaveGrande.Lado - 1, i)) borda++;
		}
		Console.WriteLine($"  casco: {borda} de {4 * NaveGrande.Lado} celulas de borda sao parede");
		Afirmar(borda == 4 * NaveGrande.Lado, "o casco fecha a sala inteira -- nao ha buraco na borda");

		// O CONVES. Nao adianta o casco fechar se o meio nao for andavel.
		int livres = 0;
		for (int y = 1; y < NaveGrande.Lado - 1; y++)
			for (int x = 1; x < NaveGrande.Lado - 1; x++)
				if (!c.BlockedCell(x, y)) livres++;
		Console.WriteLine($"  conves: {livres:N0} celulas andaveis");
		Afirmar(livres > 9000, "o interior e uma SALA e nao um labirinto");

		// AS DUAS PECAS, e a densidade de cada uma. O console e parede assada na planta (`density=1`,
		// :290); a plataforma NAO e (`density=0`, :272) -- pisa-se nela pra sair.
		Afirmar(c.BlockedCell(NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y),
				"o console da ponte e parede, e a parede esta na PLANTA (nao na camada de obras)");
		Afirmar(!c.BlockedCell(NaveGrande.CelDaPlataforma.X, NaveGrande.CelDaPlataforma.Y),
				"a plataforma de saida nao bloqueia: e nela que se pisa");
		Afirmar(!c.BlockedCell(NaveGrande.CelDaChegada.X, NaveGrande.CelDaChegada.Y),
				"e o ponto de chegada e livre -- ninguem embarca dentro de uma parede");

		// O VAO DA SALA DE CONTROLE. Sem ele a ponte e inalcancavel e a nave vira um quarto vazio: e o
		// `if(xx == 8) new /turf/Door/Door2` (:168-169), aqui deixado ABERTO de proposito -- ver o
		// cabecalho de `NaveGrande` (porta e estado por zona, e a planta e compartilhada).
		Afirmar(!c.BlockedCell(NaveGrande.ColunaDoVao, NaveGrande.AlturaDaSala),
				"ha um vao na parede da sala de controle -- da pra chegar na ponte");
		Afirmar(c.BlockedCell(NaveGrande.ColunaDoVao + 1, NaveGrande.AlturaDaSala),
				"...e ele e um VAO e nao uma parede inteira faltando");

		// E A PONTE FICA MESMO DENTRO DA SALA -- do lado de dentro das duas paredes internas.
		Afirmar(NaveGrande.CelDoConsole.X < NaveGrande.ColunaDaParedeDaSala
				&& NaveGrande.CelDoConsole.Y < NaveGrande.AlturaDaSala,
				"o console esta DENTRO da sala de controle, e nao no meio do conves");

		// A CHAVE DE ZONA: duas naves, dois interiores. E a mesma prova que a camada 1 fez com dois
		// planetas gerados homonimos, agora com o nome IGUAL de proposito -- todo interior se chama
		// "Nave", e so a seed os separa. E exatamente o caso que a `Obra` (filtro por nome) juntaria.
		ZoneKey a = NaveGrande.ZonaDoInterior(7);
		ZoneKey b = NaveGrande.ZonaDoInterior(8);
		Afirmar(a.Name == b.Name && a.Hash != b.Hash,
				"dois interiores tem o MESMO nome e hashes diferentes -- quem separa e a seed");
		Afirmar(NaveGrande.EhInterior(a, out int id7) && id7 == 7,
				"e da pra voltar do interior pra nave que ele e");

		Console.WriteLine($"  assinatura da planta: {t.Assinatura():X16} (as duas pontas tem que imprimir esta)");
	}

	// =====================================================================
	private static void OFoguete()
	{
		Console.WriteLine("\n-- o foguete de uso unico (`obj/Rocketship`, PlanetTech.dm:237) --");

		Console.WriteLine("  preco             : 200.000 z / tech 30  (a Spacepod: 100.000 / tech 40)");
		Console.WriteLine($"  janela em orbita  : {Naves.SegundosEmOrbita:0} s "
						  + $"(aviso de reentrada aos {Naves.SegundosAteAvisarAReentrada:0} s)");
		Console.WriteLine($"  recondicionar     : {Naves.CustoDeRecondicionar:N0} z");

		Afirmar(Naves.SegundosAteAvisarAReentrada < Naves.SegundosEmOrbita,
				"o aviso de reentrada sai ANTES da descida -- senao ele nao avisa nada");
		Afirmar(Naves.CustoDeRecondicionar < 200_000,
				"recondicionar custa menos que um foguete novo, senao ninguem recondicionaria");
		Afirmar(Naves.EhFoguete("Rocket_Ship") && Naves.EhNave("Rocket_Ship"),
				"o foguete tambem e uma NAVE -- ele se move entre mundos, e a `Obra` nao sabe disso");

		// QUANTO A JANELA VALE, MEDIDO: a que distancia um corpo chega em 25 s se ele soltar o
		// foguete assim que atingir a orbita. E o que separa o foguete de um brinquedo caro.
		double px = MoveRules.BaseSpeedPx * MoveRules.SpeedStatFrom(11) * Naves.SegundosEmOrbita;
		Console.WriteLine($"  quem voa no teto do corpo cobre {px:N0} px nessa janela "
						  + $"({px / Espaco.DistanciaTerraNamek * 100:0.00}% da viagem Terra->Namek)");
	}

	// =====================================================================
	// A PERGUNTA QUE O DONO TEM QUE RESPONDER: O CASCO PROTEGE DO SOL?
	// =====================================================================
	/// <summary>
	/// ============================ ELA E MEDIDA AQUI, E NAO DECIDIDA ============================
	/// A camada 1 deixou a pergunta em aberto ("o casco protege do sol?") dizendo que ela so ficaria
	/// urgente na Capital Ship, porque a Spacepod nao tem interior. Ela tem interior agora.
	///
	/// O PORTE RESPONDE METADE SOZINHO, e sem uma linha escrita pra isso: quem esta DENTRO nao queima,
	/// porque o `TickDoSol` roda dentro do `TickDoEspaco` e este devolve na primeira linha pra quem nao
	/// esta no espaco -- e o interior e `ZoneKey.Interior`, nao espaco. A bancada `--naveteste` afirma
	/// exatamente isso.
	///
	/// A OUTRA METADE E DESIGN E NAO E MINHA: quem esta AO LEME tem o corpo do lado de fora do casco, na
	/// zona do espaco, e queima como qualquer um. O DM cobre esse caso com `usr.spacesuit = 1`
	/// (`ShipVessel.dm:318`, *"survive space while at the helm"*) -- so que la o `spacesuit` protege do
	/// VAZIO, e o vazio deste port nao machuca ninguem. Nao existe, no DM, uma regra sobre estrela: elas
	/// nao existiam la.
	///
	/// Entao o que esta secao faz e por os NUMEROS na mesa pro dono decidir.
	/// ======================================================================================
	/// </summary>
	private static void OCascoEOSol()
	{
		Console.WriteLine("\n-- o casco e o sol: os numeros da pergunta que fica pro dono --");

		// A ESTRELA MAIS COMUM E A MENOR: a ana vermelha. Ela e o caso BOM -- as outras sete classes
		// sao piores, e a tabela abaixo mostra as pontas.
		foreach (ClasseDeEstrela c in new[]
			{ ClasseDeEstrela.AnaVermelha, ClasseDeEstrela.Amarela, ClasseDeEstrela.GiganteAzul })
		{
			double raio = Sistemas.RaioDaClasse(c);
			var e = new Estrela { Classe = c, Raio = (float)raio, Pos = default, Seed = 1 };

			Console.WriteLine($"\n  {c}: raio letal {raio:N0} px, coroa a {raio * CalorDaEstrela.RazaoDaCoroa:N0} px");
			foreach (double bp in new[] { 1e3, 1e6, 1e9 })
			{
				double dps = CalorDaEstrela.DanoPorSegundo(e, bp);
				double ate = CalorDaEstrela.SegundosAteCeder(e, bp, 100, CombatKnobs.RegenPorSegundo);
				Console.WriteLine($"    expressedBP {bp,10:N0} -> {dps,7:0.0} de vida/s, "
								  + $"o corpo cede em {(double.IsInfinity(ate) ? "nunca" : $"{ate:0.0} s")}");
			}

			// QUANTO TEMPO A TRAVESSIA CUSTA, por velocidade de nave. E o numero que decide a pergunta:
			// se atravessar uma estrela a 39x leva menos tempo do que o corpo aguenta, o casco nao
			// precisa proteger de nada -- a VELOCIDADE ja e a protecao.
			double diametro = raio * 2;
			Console.Write("    travessia (diametro): ");
			foreach (int v in new[] { 1, 5, 39 })
				Console.Write($"{v}x = {Naves.SegundosDeViagem(diametro, v):0.0} s   ");
			Console.WriteLine();
		}

		// ============================ A CONTA QUE A PRIMEIRA VERSAO DESTA BANCADA ERROU ============================
		// Estava escrito aqui que "a pe nao da e de nave da" -- e a bancada REPROVOU a propria frase: numa
		// ANA VERMELHA (raio 360 px) a travessia a pe custa 4,5 s e um corpo de 1e6 aguenta 13,8 s, ou
		// seja da pra atravessar a menor estrela do universo andando. A frase era uma historia, e nao
		// uma medida.
		//
		// O QUE E VERDADE, medido: a proporcao entre "quanto tempo o corpo aguenta" e "quanto tempo a
		// travessia custa" e o que decide, e ela vira contra o corpo conforme a estrela cresce -- porque
		// a estrela maior e mais larga E queima mais forte, as duas coisas ao mesmo tempo. Na GIGANTE
		// AZUL nem 1e9 de BP expresso atravessa andando.
		// ======================================================================================================
		double rGig = Sistemas.RaioDaClasse(ClasseDeEstrela.GiganteAzul);
		var gig = new Estrela { Classe = ClasseDeEstrela.GiganteAzul, Raio = (float)rGig, Pos = default, Seed = 1 };
		double aguentaGig = CalorDaEstrela.SegundosAteCeder(gig, 1e9, 100, CombatKnobs.RegenPorSegundo);
		double aPeGig = Espaco.SegundosDeViagem(rGig * 2);
		double noTetoGig = Naves.SegundosDeViagem(rGig * 2, Naves.VelocidadeMaxima);

		Console.WriteLine($"\n  gigante azul, corpo de 1e9 de BP expresso: aguenta {aguentaGig:0.0} s la dentro");
		Console.WriteLine($"  atravessa-la custa {aPeGig:0.0} s a pe e {noTetoGig:0.0} s numa nave no teto");
		Afirmar(aPeGig > aguentaGig, "na maior estrela, nem 1e9 de BP atravessa ANDANDO");
		Afirmar(noTetoGig < aguentaGig, "...e no teto de velocidade a travessia cabe -- a nave E a diferenca");

		// E O CONTRAEXEMPLO, que e o que impede esta secao de virar propaganda: a MENOR estrela e
		// atravessavel a pe pelo mesmo corpo. "A nave protege do sol" e verdade nas grandes e falso nas
		// pequenas, e e essa a resposta -- nao um sim nem um nao.
		double rAna = Sistemas.RaioDaClasse(ClasseDeEstrela.AnaVermelha);
		var ana = new Estrela { Classe = ClasseDeEstrela.AnaVermelha, Raio = (float)rAna, Pos = default, Seed = 1 };
		double aguentaAna = CalorDaEstrela.SegundosAteCeder(ana, 1e6, 100, CombatKnobs.RegenPorSegundo);
		double aPeAna = Espaco.SegundosDeViagem(rAna * 2);
		Console.WriteLine($"  ana vermelha, corpo de 1e6: aguenta {aguentaAna:0.0} s e a travessia a pe custa {aPeAna:0.0} s");
		Afirmar(aPeAna < aguentaAna, "CONTRAEXEMPLO: a menor estrela se atravessa ANDANDO -- a nave nao e sempre necessaria");

		// E O TUNELAMENTO: a estrela e MUITO mais larga que o menor planeta, entao o teto de velocidade
		// nao passa por dentro dela sem que a amostragem perceba. Se passasse, a nave rapida seria
		// imune ao sol por acidente -- que e pior que qualquer resposta de design.
		double passo = Naves.PassoPorTique(Naves.VelocidadeMaxima);
		Console.WriteLine($"  passo por tique no teto: {passo:0} px, contra {rAna * 2:N0} px de diametro da estrela");
		Afirmar(passo < rAna, "no teto a nave nao ATRAVESSA a estrela entre dois tiques -- ela queima de verdade");
	}
}
