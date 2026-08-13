using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--diagberco`. Roda no boot, sem cliente, sem rede e sem tocar em ninguem.
///
/// ============================ ELA CHAMA O CODIGO DE PRODUCAO, E SO ELE ============================
/// Nada aqui recalcula regra: a bancada monta um <see cref="CharacterSave"/> igual ao que a criacao
/// grava e passa pelo mesmo <see cref="GameServer.BercoDe(CharacterSave)"/> e pelo mesmo
/// <see cref="GameServer.DestinoDoBerco"/> que o nascimento, a morte, o verb `spawn` e o admin usam.
/// Uma bancada com caminho proprio testaria o atalho (regra 0.7).
///
/// O que ela AFIRMA, e que nao dava pra afirmar por leitura:
///   1. todo berco tem destino que EXISTE -- zona no catalogo com colisao, ou um corpo no mapa do
///      universo pra pousar;
///   2. o ponto de chegada nao e PAREDE. E aqui que o defeito de Icer aparece: o (249,250) do BYOND
///      e o campo aberto da TERRA, e estava sendo usado como chegada de todo mapa pre-feito;
///   3. as duas excecoes do dono disparam, e so nelas -- teto que nunca dispara e teto nenhum;
///   4. nascimento e renascimento devolvem o MESMO lugar (o defeito que ninguem veria por semanas);
///   5. o personagem ANTIGO (sem `SeedDoBerco`) ganha um berco, e ganha sempre o mesmo.
/// ============================================================================================
/// </summary>
public partial class GameServer
{
	private int _bercoOk, _bercoFalhou;

	private void Conferir(string oque, bool passou)
	{
		if (passou) { _bercoOk++; return; }
		_bercoFalhou++;
		GD.PushWarning($"[diagberco] FALHOU: {oque}");
	}

	/// <summary>Uma ficha de disco igual a que o `CreateChar` grava -- e nada alem dela.</summary>
	private static CharacterSave Ficha(string raca, string classe, string linhagem,
									   bool pertoDeCasa, string nome = "Cobaia", long criadoEm = 1_700_000_000_000)
		=> new()
		{
			Nome = nome,
			Raca = raca,
			Linhagem = linhagem,
			CriadoEm = criadoEm,
			PertoDeCasa = pertoDeCasa,
			SeedDoBerco = Bercos.SementeDoBerco(nome, criadoEm),
			Ficha = new Jandirus.Core.Stats.Fighter { Name = nome, Race = raca, Class = classe },
		};

	public void RodarBancadaDeBerco()
	{
		_bercoOk = _bercoFalhou = 0;
		GD.Print("[diagberco] ================= O BERCO =================");

		// ---------------------------------------------------------------- 1. TODA RACA TEM CHAO
		// As 19 do menu de criacao + as 3 construidas + Halfbreed + uma que nao existe. O berco tem
		// que responder por TODAS: uma raca sem lugar e um corpo no vazio, que e o modo de falha que
		// esta regra existe pra impedir.
		string[] racas =
		[
			"Human", "Shapeshifter", "Demigod", "Majin", "Alien",
			"Saiyan", "Tsujin", "Saibaman", "Heran", "Meta", "Icer",
			"Namekian", "Arlian", "Makyo", "Gray", "Kanassa", "Yardrat",
			"Kai", "Demon",
			"Android", "BioAndroid", "SpiritDoll",
			"Halfbreed", "RacaQueNaoExiste",
		];

		GD.Print("[diagberco] -- cada raca no seu planeta (e o chao conferido contra a colisao)");
		foreach (string raca in racas)
		{
			Berco b = BercoDe(Ficha(raca, "Normal", "", false));
			(ZoneKey zona, Vec2 pos) = DestinoDoBerco(b);
			bool chao = ChaoDeVerdade(zona, pos, out string porque);

			Conferir($"{raca} nasce num lugar que existe ({porque})", chao);
			GD.Print($"[diagberco]    {raca,-18} -> {b.Planeta,-16} {(b.PreFeito ? "pre-feito" : "gerado   ")}"
				+ $" | zona {zona.Name} @ ({pos.X / ZoneCollision.TileSize:0},{pos.Y / ZoneCollision.TileSize:0}) | {porque}");
		}

		// ---------------------------------------------------------------- 2. O (249,250) NAO SERVE
		// A prova de que o `PontoLivrePerto` REJEITA -- um teto que nunca dispara e indistinguivel de
		// teto nenhum. Icer e o caso: o centro do mapa dela e rocha.
		GD.Print("[diagberco] -- o ponto de chegada de cada mundo pre-feito");
		int corrigidos = 0;
		foreach (string mundo in new[] { "Earth", "Vegeta", "Namek", "Icer", "Arconia", "Arlia", "Makyo_Star", "Heaven", "Hell" })
		{
			var z = ZoneKey.Premade(mundo);
			ZoneCollision? m = MapaDaZonaOuCatalogo(z);
			if (m == null) { GD.Print($"[diagberco]    {mundo,-12} sem colisao carregada"); continue; }

			bool centroEraParede = m.BlockedAt(SpawnPos);
			Vec2 p = PontoDeNascimento(z);
			Conferir($"o ponto de nascimento de {mundo} nao e parede", !m.BlockedAt(p));
			if (centroEraParede) corrigidos++;

			GD.Print($"[diagberco]    {mundo,-12} centro (249,250) {(centroEraParede ? "PAREDE -> desviou para" : "livre, ficou em    ")}"
				+ $" ({p.X / ZoneCollision.TileSize:0},{p.Y / ZoneCollision.TileSize:0})");
		}
		Conferir("o desvio de ponto de nascimento DISPARA em algum mapa (senao seria guarda morta)", corrigidos > 0);

		// ---------------------------------------------------------------- 3. AS DUAS EXCECOES
		GD.Print("[diagberco] -- as duas excecoes do dono");

		Berco baixa = BercoDe(Ficha("Saiyan", "Low-Class", "Saiyan", false));
		Conferir("o Saiyajin de classe baixa e despejado na Terra", baixa.Despejado && baixa.Planeta == "Earth");
		GD.Print($"[diagberco]    Saiyan/Low-Class      -> {baixa.Planeta} (despejado {baixa.Despejado})");

		Berco heran = BercoDe(Ficha("Heran", "Low-Class", "", false));
		Conferir("o HERAN de classe baixa NAO e despejado (a trava de raca)", !heran.Despejado && heran.Planeta == "Vegeta");
		GD.Print($"[diagberco]    Heran/Low-Class       -> {heran.Planeta} (despejado {heran.Despejado})");

		foreach (string classe in new[] { "Legendary", "Legendary Primal Saiyan" })
		{
			string lin = classe.Contains("Primal") ? "Primal Saiyan" : "Saiyan";
			Berco ex = BercoDe(Ficha("Saiyan", classe, lin, pertoDeCasa: true));
			(ZoneKey zona, Vec2 pos) = DestinoDoBerco(ex);

			Conferir($"{classe} e exilado", ex.Motivo == MotivoDoBerco.ExilioDoLendario);
			// O PEDIDO DO JOGADOR E IGNORADO NO EXILIO, e esta e a unica forma de provar isso: a
			// ficha pediu vizinho e o motivo saiu exilio, nao vizinhanca.
			Conferir($"{classe} ignora o pedido de nascer perto de casa", ex.Motivo != MotivoDoBerco.VizinhoDoNatal);
			Conferir($"{classe} nao cai no ultimo recurso", !ex.UltimoRecurso);
			Conferir($"{classe} nasce num lugar que existe", ChaoDeVerdade(zona, pos, out _));
			GD.Print($"[diagberco]    {classe,-22}-> {ex.Planeta} [{ex.Sx}:{ex.Sy}]k{ex.K} "
				+ $"grav {ex.Gravidade:0.#} | {ex.CelulasOlhadas} celula(s) olhada(s)");
		}

		Berco normalPrimal = BercoDe(Ficha("Saiyan", "Normal Primal Saiyan", "Primal Saiyan", false));
		Conferir("o Primal NORMAL nao e exilado", normalPrimal.Motivo == MotivoDoBerco.PlanetaNatal);

		// ---------------------------------------------------------------- 4. O PEDIDO DE VIZINHO
		GD.Print("[diagberco] -- nascer perto de casa");
		foreach (string raca in new[] { "Human", "Saiyan", "Namekian", "Icer", "Arlian", "Makyo", "Kai" })
		{
			string natal = Bercos.PlanetaNatal(raca);
			int irmas = Bercos.IrmasDoNatal(natal).Count;
			Berco v = BercoDe(Ficha(raca, "Normal", "", pertoDeCasa: true));
			(ZoneKey zona, Vec2 pos) = DestinoDoBerco(v);

			// A TELA E O MUNDO TEM QUE CONCORDAR. `IrmasDoNatal` e a MESMA funcao que o
			// `CreationScreen.AtualizarBerco` chama pra decidir se mostra a opcao; se ela listar
			// mundos e o berco devolver o natal, a tela promete uma escolha que o mundo ignora -- e
			// o jogador so descobre isso ao nascer no lugar errado.
			Conferir($"{raca}: a tela oferece vizinho exatamente quando o berco entrega um",
				(irmas > 0) == (v.Motivo == MotivoDoBerco.VizinhoDoNatal));
			Conferir($"{raca}: o vizinho existe", ChaoDeVerdade(zona, pos, out _));

			GD.Print($"[diagberco]    {raca,-10} natal {natal,-12} {irmas} irma(s) validas -> {v.Planeta} ({v.Motivo})");
		}

		// ---------------------------------------------------------------- 5. NASCER == RENASCER
		// O defeito que ninguem veria por semanas. Os dois caminhos passam pelo mesmo funil, entao a
		// prova e que o funil devolve o mesmo par pras mesmas entradas -- inclusive pro exilado.
		GD.Print("[diagberco] -- nascimento e renascimento sao o mesmo lugar");
		foreach (string classe in new[] { "Normal", "Low-Class", "Legendary" })
			foreach (bool perto in new[] { false, true })
			{
				CharacterSave c = Ficha("Saiyan", classe, "Saiyan", perto);
				(ZoneKey z1, Vec2 p1) = DestinoDoBerco(BercoDe(c));   // o que o `CreateChar` grava
				(ZoneKey z2, Vec2 p2) = DestinoDoBerco(BercoDe(c));   // o que o `Renascer` procura
				Conferir($"Saiyan/{classe} (perto={perto}): nascer e renascer caem no mesmo lugar",
					z1.Hash == z2.Hash && p1.X == p2.X && p1.Y == p2.Y);
			}

		// ---------------------------------------------------------------- 6. O PERSONAGEM ANTIGO
		// Save sem `SeedDoBerco` (zero). Ele tem que GANHAR um berco, e o mesmo em toda leitura --
		// senao ele renasceria num planeta diferente a cada login.
		GD.Print("[diagberco] -- o personagem que ja existe (save sem berco)");
		var velho = new CharacterSave
		{
			Nome = "Personagem Antigo", Raca = "Saiyan", Linhagem = "Saiyan",
			CriadoEm = 1_600_000_000_000, SeedDoBerco = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Name = "Personagem Antigo", Race = "Saiyan", Class = "Elite" },
			Zona = "Earth",
		};
		Berco d1 = BercoDe(velho);
		Berco d2 = BercoDe(velho);
		Conferir("o save sem berco DERIVA um", d1.Planeta is { Length: > 0 });
		Conferir("e a derivacao e estavel entre leituras", d1.Planeta == d2.Planeta && d1.Seed == d2.Seed);
		Conferir("a derivacao bate com a semente publicada",
			d1.Seed == BercoDe(Ficha("Saiyan", "Elite", "Saiyan", false, "Personagem Antigo", 1_600_000_000_000)).Seed);
		GD.Print($"[diagberco]    save de 2020 sem campo -> {d1.Planeta} ({d1.Motivo})");

		GD.Print($"[diagberco] ================= {_bercoOk} passaram, {_bercoFalhou} falharam =================");
	}

	/// <summary>
	/// ESTE PAR (zona, ponto) E UM LUGAR EM QUE DA PRA POR UM CORPO?
	///
	/// Tres desfechos legitimos, e nenhum "acho que sim": a zona e pre-feita e tem colisao carregada
	/// (e o ponto nao e parede); a zona e o ESPACO e o ponto esta sobre o disco de um planeta (e ai
	/// quem poe o corpo no chao e o `TickDoEspaco`, pelo caminho de verdade); ou a zona e um mundo
	/// gerado que ja esta vivo. Qualquer outra coisa e um corpo no vazio.
	/// </summary>
	private bool ChaoDeVerdade(ZoneKey zona, Vec2 pos, out string porque)
	{
		if (Espaco.EhEspaco(zona))
		{
			bool sobre = Espaco.PlanetaSob(SeedDoUniverso, pos) != null;
			porque = sobre ? "em orbita, sobre o disco" : "NO VAZIO -- sem planeta sob o corpo";
			return sobre;
		}

		ZoneCollision? m = MapaDaZonaOuCatalogo(zona);
		if (m == null) { porque = "zona sem colisao no catalogo"; return false; }

		bool livre = !m.BlockedAt(pos);
		porque = livre ? "chao livre" : "DENTRO DE PAREDE";
		return livre;
	}
}
