using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA TECH PORTADA NA v0.2.1 (item 13 do pedido de 2026-10-13: *"tem muita tech q n foi portada, faça o port
/// delas todas"*) -- as familias X da `--v021teste`. Cada lote da tech ganha uma: o que o item FAZ, pelo canal de
/// producao (`ComandoDeItem`, `ComandoDeInteracao`), com o defeito injetado de cada regra.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Uma obra posta de pe pela bancada, ao lado de quem a usa. A bancada a tira no fim.</summary>
	private Obra ObraDeBancadaV21(string tipo, Vec2 onde, ServerPlayer dono, bool aparafusada = true)
	{
		var o = new Obra
		{
			Id = _proximaObraId++, Tipo = tipo, X = onde.X, Y = onde.Y,
			DonoConta = dono.Conta, DonoNome = dono.Name, Aparafusada = aparafusada, ErguidaEm = NowMs(),
		};
		o.PorZona(dono.Zone);
		_noChao.Add(o);
		return o;
	}

	// =====================================================================
	// X1) A TECH DE BOLSO E OS DEPOSITOS -- o lote T1
	// =====================================================================
	private void ATechDeBolsoV21()
	{
		GD.Print("[v021] -- X1) A TECH PORTADA, lote 1: os itens de bolso, a pesca, a chave, os depositos e o scanner (item 13)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		const float T = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(8);
		var obras = new List<Obra>();
		CofreDeTech cofreDeAntes = _cofreDeTech;
		_cofreDeTech = new CofreDeTech();
		ServerPlayer p = ForjarV2("Engenheiro", c, 50_000);
		p.Conta = "bancada_v021_engenheiro";
		ServerPlayer outro = ForjarV2("Visitante", c + new Vec2(T, 0), 50_000);
		outro.Conta = "bancada_v021_visitante";
		List<string> Fazer(ServerPlayer quem, string acao, string arg) => Ouvir(() => ComandoDeItem(quem, "item_" + acao, arg));
		bool Disse(List<string> falas, string trecho) => falas.Exists(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));

		try
		{
			// ---------------------------------------------------------------- as fichas
			string[] deBolso = [TechDeBolso.Pilula, TechDeBolso.BolsaDeSangue, TechDeBolso.Vara, TechDeBolso.Livro, TechDeBolso.Pda,
								TechDeBolso.Chave, TechDeBolso.Fabricador, TechDeBolso.Agulha, TechDeBolso.ContainerDeDna];
			List<string> semAcao = [.. deBolso.Where(id => CatalogoDeItens.Get(id) is not { AcoesDoItem.Length: > 0 })];
			List<string> semBotao = [.. deBolso.SelectMany(id => (CatalogoDeItens.Get(id)?.AcoesDoItem ?? []).Select(a => (id, a)))
											   .Where(x => x.a != "comer" && TechDeBolso.BotaoDa(x.a) == null).Select(x => $"{x.id}:{x.a}")];
			AfirmarV21($"**OS NOVE ITENS DE BOLSO DO LOTE TEM ACAO** (ate a v0.2.0 caiam na mochila sem botao nenhum), e toda acao tem o botao dela na tabela",
					   semAcao.Count == 0 && semBotao.Count == 0, $"sem acao: {string.Join(", ", semAcao)}; sem botao: {string.Join(", ", semBotao)}");
			string longo = TechDeBolso.Com(TechDeBolso.Chave, "", new string('a', 70));
			AfirmarV21("o estado mora no ID e o id tem teto: a chave com uma senha se le de volta, e o id que nao cabe no pacote da mochila nao vira item",
					   TechDeBolso.SenhasDaChave(TechDeBolso.ChaveCom(false, ["kame"])).SequenceEqual(["kame"])
					   && CatalogoDeItens.Get(TechDeBolso.ChaveCom(false, ["kame"])) is { } comSenha && comSenha.Nome.Contains("1 senha")
					   && longo.Length > TechDeBolso.MaxDoId && CatalogoDeItens.Get(longo) == null);

			// ---------------------------------------------------------------- a pilula
			p.Mochila.Guardar(TechDeBolso.Pilula, 2);
			p.Ficha.CurrentNutrition = 0;
			Fazer(p, "comer", TechDeBolso.Pilula);
			AfirmarV21("a PILULA DE NUTRIENTES se come: alimenta cinco (`nutrition = 5`) e a pilha cai de duas pra uma",
					   Math.Abs(p.Ficha.CurrentNutrition - 5) < 0.01 && p.Mochila.Quantos(TechDeBolso.Pilula) == 1,
					   $"nutricao {p.Ficha.CurrentNutrition:0.##}; na mochila {p.Mochila.Quantos(TechDeBolso.Pilula)}");

			// ---------------------------------------------------------------- a bolsa de sangue
			p.Mochila.Guardar(TechDeBolso.BolsaDeSangue);
			double vidaDeAntes = p.Ficha.HP;
			Fazer(p, TechDeBolso.AcaoDoar, TechDeBolso.BolsaDeSangue);
			string cheia = TechDeBolso.Com(TechDeBolso.BolsaDeSangue, "cheia");
			AfirmarV21("a BOLSA DE SANGUE: doar a enche (o estado vai pro id) e tira dez de vida (`SpreadDamage(10)`)",
					   p.Mochila.Quantos(cheia) == 1 && p.Mochila.Quantos(TechDeBolso.BolsaDeSangue) == 0
					   && vidaDeAntes - p.Ficha.HP is > 5 and < 15,
					   $"cheia {p.Mochila.Quantos(cheia)}; vida {vidaDeAntes:0.#} -> {p.Ficha.HP:0.#}");
			List<string> aoBeber = Fazer(p, TechDeBolso.AcaoBeber, cheia);
			AfirmarV21("...e beber e de vampiro (`if(usr.IsAVampire)`): quem nao e um ouve a recusa, e a bolsa continua cheia",
					   Disse(aoBeber, "vampiro") && p.Mochila.Quantos(cheia) == 1, string.Join(" | ", aoBeber));
			outro.Race = outro.Ficha.Race = "Android";
			outro.Mochila.Guardar(TechDeBolso.BolsaDeSangue);
			Fazer(outro, TechDeBolso.AcaoDoar, TechDeBolso.BolsaDeSangue);
			AfirmarV21("...e o androide nao doa: nao tem sangue (o aviso do proprio verb do original)",
					   outro.Mochila.Quantos(TechDeBolso.BolsaDeSangue) == 1 && outro.Mochila.Quantos(cheia) == 0);
			outro.Race = outro.Ficha.Race = "Human";

			// ---------------------------------------------------------------- a pesca
			AfirmarV21("a regra da PESCA (`Fishing.dm:33-34`): com pericia 1 o peixe morde no terceiro avanco da linha e nao no segundo; a truta pede 15 de pericia e o salmao 35",
					   Pesca.Mordeu(3, 0, 5, 1) && !Pesca.Mordeu(2, 0, 5, 1)
					   && Pesca.Peixe(1, () => 3, () => 0) == CatalogoDeItens.Peixe && Pesca.Peixe(15, () => 3, () => 0) == CatalogoDeItens.Truta
					   && Pesca.Peixe(35, () => 3, () => 0) == CatalogoDeItens.Salmao && Pesca.Peixe(35, () => 3, () => 99) == CatalogoDeItens.Peixe);
			p.Mochila.Guardar(TechDeBolso.Vara);
			// (o corredor da bancada so garante UMA linha de terra de cada lado, e a vara alcanca agua a dois tiles: o
			// chao seco se PROCURA, em vez de se supor -- o corredor muda de rodada pra rodada com as rochas de minerio)
			Vec2? seco = LongeDaAguaV21();
			if (seco is { } semAgua) p.Pos = semAgua;
			List<string> noSeco = Fazer(p, TechDeBolso.AcaoPescar, TechDeBolso.Vara);
			AfirmarV21("longe da agua a vara recusa: *\"You have to be near a body of water\"*",
					   seco != null && !_pescarias.ContainsKey(p.Id) && Disse(noSeco, "água"),
					   (seco == null ? "nao achei chao seco na zona da bancada | " : "") + string.Join(" | ", noSeco));
			_pescarias.Remove(p.Id);
			p.Pos = c;
			Vec2? beira = BeiraDaAguaV21();
			AfirmarV21("(montagem) a zona da bancada tem uma beira d'agua", beira != null);
			if (beira is { } margem)
			{
				p.Pos = margem;
				Fazer(p, TechDeBolso.AcaoPescar, TechDeBolso.Vara);
				bool lancou = _pescarias.TryGetValue(p.Id, out Pescaria? linha);
				if (linha != null) linha.Next = 3;
				TickDaPesca(Pesca.SegundosPorVolta);
				bool fisgou = linha is { FisgouAte: > 0 };
				double pericia = p.Ficha.FishingSkill;
				List<string> aoPuxar = Fazer(p, TechDeBolso.AcaoPescar, TechDeBolso.Vara);
				AfirmarV21("**A VARA PESCA**: na beira d'agua a linha e lancada, o peixe fisga, e usar a vara de novo dentro dos cinco segundos o PUXA -- ele cai na mochila e a pericia sobe um",
						   lancou && fisgou && p.Mochila.Quantos(CatalogoDeItens.Peixe) == 1 && Math.Abs(p.Ficha.FishingSkill - (pericia + 1)) < 0.01,
						   $"lancou {lancou}; fisgou {fisgou}; peixes {p.Mochila.Quantos(CatalogoDeItens.Peixe)}; pericia {pericia:0} -> {p.Ficha.FishingSkill:0} | {string.Join(" | ", aoPuxar)}");
				p.Pos = margem + new Vec2(0, 12);
				TickDaPesca(0.1);
				AfirmarV21("...e quem se mexe espanta os peixes: a pescaria acaba", !_pescarias.ContainsKey(p.Id));
				p.Pos = margem;

				Pesca.NuncaFisgaDeTeste = true;
				try
				{
					Fazer(p, TechDeBolso.AcaoPescar, TechDeBolso.Vara);
					if (_pescarias.TryGetValue(p.Id, out Pescaria? parada)) parada.Next = 3;
					TickDaPesca(Pesca.SegundosPorVolta);
					AfirmarV21("(defeito injetado: a linha nunca fisga) a mesma cena, e o peixe nao morde -- a vara da v0.2.0, um item parado na mochila",
							   _pescarias.TryGetValue(p.Id, out Pescaria? semPeixe) && semPeixe.FisgouAte == 0);
				}
				finally { Pesca.NuncaFisgaDeTeste = false; _pescarias.Remove(p.Id); }
				p.Pos = c;
			}

			// ---------------------------------------------------------------- o livro e o PDA
			p.Mochila.Guardar(TechDeBolso.Livro);
			Fazer(p, TechDeBolso.AcaoEscrever, $"{TechDeBolso.Livro}/Diario de bordo: dia 1/2, tudo em ordem");
			string livroEscrito = p.Mochila.Pilhas.Select(x => x.Id).FirstOrDefault(id => TechDeBolso.Base(id) == TechDeBolso.Livro) ?? "";
			List<string> lido = Fazer(p, TechDeBolso.AcaoFolhear, livroEscrito);
			AfirmarV21("o LIVRO se escreve e se le: o texto (com a barra que ele tem) mora no cofre do servidor e o id do livro leva so o numero",
					   TechDeBolso.Campo(livroEscrito, 0).Length > 0 && Disse(lido, "dia 1/2, tudo em ordem")
					   && CatalogoDeItens.Get(livroEscrito) is { } fichaDoLivro && fichaDoLivro.AcoesDoItem.Contains(TechDeBolso.AcaoFolhear)
					   && !CatalogoDeItens.Get(TechDeBolso.Livro)!.AcoesDoItem.Contains(TechDeBolso.AcaoFolhear),
					   $"id '{livroEscrito}' | {string.Join(" | ", lido)}");
			GravarOCofreDeTech();
			CofreDeTech escrito = _cofreDeTech;
			CarregarOCofreDeTech();
			AfirmarV21("...e o cofre vai pro disco e volta (`tech-itens.json`): depois de recarregar, o livro ainda diz o que foi escrito",
					   _cofreDeTech != escrito && Disse(Fazer(p, TechDeBolso.AcaoFolhear, livroEscrito), "dia 1/2"));
			p.Mochila.Guardar(TechDeBolso.Pda);
			Fazer(p, TechDeBolso.AcaoEscrever, $"{TechDeBolso.Pda}/senha do cofre: 1234");
			AfirmarV21("o PDA guarda notas do mesmo jeito, com o numero dele",
					   p.Mochila.Pilhas.Exists(x => TechDeBolso.Base(x.Id) == TechDeBolso.Pda && TechDeBolso.Campo(x.Id, 0).Length > 0
												   && TechDeBolso.Campo(x.Id, 0) != TechDeBolso.Campo(livroEscrito, 0)));

			// ---------------------------------------------------------------- a chave
			var porta = new BlocoDePe { Senha = "kame", DonoConta = "dono_da_porta" };
			bool semChave = PortaAbrePara(porta, p);
			p.Mochila.Guardar(TechDeBolso.Chave);
			Fazer(p, TechDeBolso.AcaoGravar, $"{TechDeBolso.Chave}/kame");
			string chaveGravada = TechDeBolso.ChaveCom(false, ["kame"]);
			bool comChave = PortaAbrePara(porta, p);
			bool outraPorta = PortaAbrePara(new BlocoDePe { Senha = "outra", DonoConta = "dono_da_porta" }, p);
			AfirmarV21("**A CHAVE ABRE A PORTA**: gravada a senha, quem CARREGA a chave passa pela porta trancada de outro sem digitar nada -- e so por ela (a porta de outra senha continua fechada)",
					   !semChave && p.Mochila.Quantos(chaveGravada) == 1 && comChave && !outraPorta,
					   $"sem chave {semChave}; com chave {comChave}; outra porta {outraPorta}; mochila [{string.Join(", ", p.Mochila.Pilhas.Select(x => x.Id))}]");
			ChaveNaoAbreDeTeste = true;
			try { AfirmarV21("(defeito injetado: a chave nao abre) com a mesma chave na mochila a porta fica fechada -- o item da v0.2.0", !PortaAbrePara(porta, p)); }
			finally { ChaveNaoAbreDeTeste = false; }
			p.Ficha.Zeni = 500;
			Fazer(p, TechDeBolso.AcaoReforcar, chaveGravada);
			bool semDinheiro = p.Mochila.Quantos(chaveGravada) == 1;
			p.Ficha.Zeni = 5000;
			Fazer(p, TechDeBolso.AcaoReforcar, chaveGravada);
			string reforcada = TechDeBolso.ChaveCom(true, ["kame"]);
			Fazer(p, TechDeBolso.AcaoGravar, $"{reforcada}/outra");
			string comDuas = TechDeBolso.ChaveCom(true, ["kame", "outra"]);
			bool duas = p.Mochila.Quantos(comDuas) == 1;
			Fazer(p, TechDeBolso.AcaoGravar, $"{comDuas}/kame");
			AfirmarV21("...e reforcada (mil zeni, cobrados) ela guarda varias senhas; gravar de novo uma que ja esta nela a apaga",
					   semDinheiro && Math.Abs(p.Ficha.Zeni - 4000) < 0.01 && duas && p.Mochila.Quantos(TechDeBolso.ChaveCom(true, ["outra"])) == 1,
					   $"sem dinheiro ficou igual {semDinheiro}; zeni {p.Ficha.Zeni:0}; duas {duas}; mochila [{string.Join(", ", p.Mochila.Pilhas.Select(x => x.Id))}]");

			// ---------------------------------------------------------------- o fabricador, a agulha e o recipiente
			p.Mochila.Guardar(TechDeBolso.Fabricador);
			p.Mochila.Guardar(TechDeBolso.Agulha);
			p.Mochila.Guardar(TechDeBolso.ContainerDeDna);
			List<string> aoFabricar = Fazer(p, TechDeBolso.AcaoFabricar, TechDeBolso.Fabricador);
			List<string> aoApontar = Fazer(p, TechDeBolso.AcaoApontar, TechDeBolso.Agulha);
			List<string> aoExtrair = Fazer(p, TechDeBolso.AcaoExtrair, TechDeBolso.ContainerDeDna);
			AfirmarV21("o FABRICADOR, a AGULHA e o RECIPIENTE respondem: nenhum cai no \"ainda nao faz nada\" -- a agulha diz que nao ha masmorra neste mundo, e o recipiente pede alguem caido ao lado",
					   !Disse(aoFabricar, "ainda não faz nada") && Disse(aoApontar, "masmorra") && Disse(aoExtrair, "caído"),
					   $"{string.Join(" | ", aoFabricar)} || {string.Join(" | ", aoApontar)} || {string.Join(" | ", aoExtrair)}");
			// (o doador do recipiente e GENTE -- `EhJogador`, o criterio do laboratorio --, e corpo de bancada de servidor
			// nao tem conexao: a amostra entra direto no cofre, e o que se mede e a viagem dela ate o tanque)
			int numeroDoRecipiente = _cofreDeTech.Proximo++;
			_cofreDeTech.Amostras[numeroDoRecipiente] = [new Amostra { Raca = "Saiyan", Doador = "Doador", Assinatura = "doador#0", Bp = 9000 }];
			string recipienteCheio = TechDeBolso.Com(TechDeBolso.ContainerDeDna, numeroDoRecipiente.ToString());
			TrocarItem(p, TechDeBolso.ContainerDeDna, recipienteCheio);
			Obra lab = ObraDeBancadaV21("Android_Creation_Mainframe", c + new Vec2(0, T), p);
			lab.Lab = 2;
			obras.Add(lab);
			List<string> amostras = Fazer(p, TechDeBolso.AcaoAmostras, recipienteCheio);
			ComandoDeTech(p, "colher_dna", "");
			AfirmarV21("**O RECIPIENTE DE DNA LEVA A AMOSTRA AO LABORATORIO**: ele lista o que guarda, e \"Colher DNA\" no Bio-Android Lab (sem ninguem caido por perto) despeja a amostra no tanque -- o recipiente volta vazio",
					   Disse(amostras, "Doador") && lab.Fornada is { Amostras.Count: 1 } && lab.Fornada.Amostras[0].Doador == "Doador"
					   && p.Mochila.Quantos(TechDeBolso.ContainerDeDna) == 1 && !_cofreDeTech.Amostras.ContainsKey(numeroDoRecipiente),
					   $"{string.Join(" | ", amostras)}; no tanque {lab.Fornada?.Amostras.Count}; mochila [{string.Join(", ", p.Mochila.Pilhas.Select(x => x.Id))}]");

			// ---------------------------------------------------------------- os depositos
			Obra bau = ObraDeBancadaV21(Deposito.Bau, c + new Vec2(T, T), p);
			obras.Add(bau);
			p.Pos = c + new Vec2(T, 0);
			outro.Pos = c + new Vec2(T, 0);
			p.Mochila.Guardar(CatalogoDeItens.Maca, 3);
			ComandoDeInteracao(p, Deposito.VerboAbrir, "");
			Fazer(p, TechDeBolso.AcaoGuardar, CatalogoDeItens.Maca);
			bool guardou = bau.Tech is { } noBau && noBau.Quantos(CatalogoDeItens.Maca) == 3 && p.Mochila.Quantos(CatalogoDeItens.Maca) == 0;
			ComandoDeInteracao(outro, Deposito.VerboAbrir, "");
			ComandoDeSaque(outro, Saque.Verbo, CatalogoDeItens.Maca);
			AfirmarV21("**O BAU GUARDA**: aberto pelo menu da tecla E, a pilha sai da mochila e entra nele; e QUALQUER UM tira (o `Loot()` do original nao pergunta de quem e)",
					   guardou && bau.Tech!.Quantos(CatalogoDeItens.Maca) == 2 && outro.Mochila.Quantos(CatalogoDeItens.Maca) == 1,
					   $"guardou {guardou}; no bau {bau.Tech?.Quantos(CatalogoDeItens.Maca)}; com o visitante {outro.Mochila.Quantos(CatalogoDeItens.Maca)}");
			List<string> aoRecolher = Ouvir(() => ComandoDeTech(p, "pegar", ""));
			AfirmarV21("...e com coisa dentro ele nao se recolhe: o que ele guarda nao some com a obra",
					   _noChao.Contains(bau) && Disse(aoRecolher, "esvazie"), string.Join(" | ", aoRecolher));
			_noChao.Remove(bau);

			Obra acampamento = ObraDeBancadaV21(Deposito.Base, c + new Vec2(T, T), p);
			obras.Add(acampamento);
			outro.Mochila.Guardar(CatalogoDeItens.Carne, 2);
			ComandoDeInteracao(outro, Deposito.VerboAbrir, "");
			Fazer(outro, TechDeBolso.AcaoGuardar, CatalogoDeItens.Carne);
			List<string> aoTirar = Ouvir(() => ComandoDeSaque(outro, Saque.Verbo, CatalogoDeItens.Carne));
			bool visitanteNaoTira = acampamento.Tech!.Quantos(CatalogoDeItens.Carne) == 2 && outro.Mochila.Quantos(CatalogoDeItens.Carne) == 0;
			ComandoDeInteracao(p, Deposito.VerboAbrir, "");
			ComandoDeSaque(p, Saque.Verbo, CatalogoDeItens.Carne);
			AfirmarV21("**NA BASE QUALQUER UM GUARDA E SO O DONO TIRA** (`PlanetTech.dm:489`): o visitante guarda a carne e nao a tira de volta; o dono tira",
					   visitanteNaoTira && Disse(aoTirar, "só o dono") && p.Mochila.Quantos(CatalogoDeItens.Carne) == 1,
					   $"visitante nao tira {visitanteNaoTira}; {string.Join(" | ", aoTirar)}; com o dono {p.Mochila.Quantos(CatalogoDeItens.Carne)}");
			Deposito.QualquerUmTiraDeTeste = true;
			try
			{
				ComandoDeInteracao(outro, Deposito.VerboAbrir, "");
				ComandoDeSaque(outro, Saque.Verbo, CatalogoDeItens.Carne);
				AfirmarV21("(defeito injetado: qualquer um tira da base) o visitante leva o que esta no deposito do dono", outro.Mochila.Quantos(CatalogoDeItens.Carne) == 1);
			}
			finally { Deposito.QualquerUmTiraDeTeste = false; }

			foreach (Jandirus.Core.Combat.BodyPart parte in p.Combate.Corpo.Partes) parte.Vida = parte.VidaMax * 0.5;
			p.Combate.SincronizarVida();
			p.Ficha.Ki = p.Ficha.MaxKi * 0.5;
			double vidaFerida = p.Ficha.HP, kiDeAntes = p.Ficha.Ki;
			ComandoDeInteracao(p, Deposito.VerboDescansar, "");
			AfirmarV21("...e o \"Rest\" da base cura os vitais e devolve 5% do Ki (`PlanetTech.dm:466-468`)",
					   p.Ficha.HP > vidaFerida && Math.Abs(p.Ficha.Ki - (kiDeAntes + p.Ficha.MaxKi * 0.05)) < p.Ficha.MaxKi * 0.001,
					   $"vida {vidaFerida:0.#} -> {p.Ficha.HP:0.#}; ki {kiDeAntes:0} -> {p.Ficha.Ki:0} de {p.Ficha.MaxKi:0}");
			p.Combate.Corpo.Restaurar();
			p.Combate.SincronizarVida();
			outro.Mochila.Guardar(CatalogoDeItens.Scouter);
			ComandoDeInteracao(outro, Deposito.VerboAbrir, "");
			Fazer(outro, TechDeBolso.AcaoGuardar, CatalogoDeItens.Scouter);
			int noChaoAntes = LargadosDe(p.Zone.Hash).Count;
			List<string> aoDesmontarDeFora = Ouvir(() => ComandoDeInteracao(outro, Deposito.VerboDesmontar, ""));
			bool visitanteNaoDesmonta = _noChao.Contains(acampamento);
			ComandoDeInteracao(p, Deposito.VerboDesmontar, "");
			AfirmarV21("...e so o dono DESMONTA -- e o que estava guardado cai no chao (`Dismantle()`, `:503-512`)",
					   visitanteNaoDesmonta && Disse(aoDesmontarDeFora, "só o dono") && !_noChao.Contains(acampamento) && LargadosDe(p.Zone.Hash).Count > noChaoAntes,
					   $"visitante nao desmonta {visitanteNaoDesmonta}; de pe {_noChao.Contains(acampamento)}; no chao {noChaoAntes} -> {LargadosDe(p.Zone.Hash).Count}");

			Obra geladeira = ObraDeBancadaV21(Deposito.Geladeira, c + new Vec2(T, T), p, aparafusada: false);
			obras.Add(geladeira);
			List<string> solta = Ouvir(() => ComandoDeInteracao(p, Deposito.VerboAbrir, ""));
			geladeira.Aparafusada = true;
			ComandoDeInteracao(p, Deposito.VerboAbrir, "");
			List<string> naoEComida = Fazer(p, TechDeBolso.AcaoGuardar, TechDeBolso.Fabricador);
			Fazer(p, TechDeBolso.AcaoGuardar, CatalogoDeItens.Carne);
			AfirmarV21("a GELADEIRA so funciona aparafusada e so guarda comida (`Food.dm:220-222`)",
					   Disse(solta, "aparafuse") && Disse(naoEComida, "só guarda comida") && geladeira.Tech is { } fria
					   && fria.Quantos(CatalogoDeItens.Carne) == 1 && fria.Quantos(TechDeBolso.Fabricador) == 0,
					   $"{string.Join(" | ", solta)} || {string.Join(" | ", naoEComida)}");
			_noChao.Remove(geladeira);

			// ---------------------------------------------------------------- o scanner
			Obra scanner = ObraDeBancadaV21(ScannerMedico.Tipo, c + new Vec2(T, T), p);
			obras.Add(scanner);
			List<string> exame = Ouvir(() => ComandoDeInteracao(p, ScannerMedico.VerboEu, ""));
			p.AlvoId = outro.Id;
			List<string> exameDoOutro = Ouvir(() => ComandoDeInteracao(p, ScannerMedico.VerboAlvo, ""));
			string poder = p.Ficha.expressedBP.ToString("N0");
			AfirmarV21("o SCANNER MEDICO examina quem clica e quem esta marcado a dois tiles: idade, raca, dano, energia e cada membro -- e NENHUM numero de poder",
					   Disse(exame, "idade") && Disse(exame, "raça") && Disse(exame, "dano") && exame.Count > 6
					   && Disse(exameDoOutro, "Visitante") && !exame.Concat(exameDoOutro).Any(f => f.Contains("BP") || f.Contains(poder)),
					   $"{exame.Count} linha(s) | {string.Join(" | ", exame.Take(4))}");
		}
		finally
		{
			Pesca.NuncaFisgaDeTeste = Deposito.QualquerUmTiraDeTeste = ChaveNaoAbreDeTeste = false;
			foreach (Obra o in obras) _noChao.Remove(o);
			_pescarias.Clear();
			_bauAberto.Clear();
			LargadosDe(p.Zone.Hash).Clear();
			_cofreDeTech = cofreDeAntes;
			GravarOCofreDeTech();
			GravarMundo();
			DesfazerV2(p, outro);
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// X2) AS ARMAS, AS BOMBAS E AS DEFESAS -- o lote T2
	// =====================================================================
	private void AsArmasEAsBombasV21()
	{
		GD.Print("[v021] -- X2) A TECH PORTADA, lote 2: as armas de fogo, o atordoador, o campo de forca, as bombas e o dreno (item 13)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		const float T = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(10);
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		int bombasDeAntes = BombasEstouradasDeTeste;
		CofreDeTech cofreDaX2 = _cofreDeTech;
		_cofreDeTech = new CofreDeTech();
		ServerPlayer p = ForjarV2("Atirador", c, 500);
		p.Conta = "bancada_v021_atirador";
		p.Facing = Facing.East;
		ServerPlayer fraco = ForjarV2("Fraco", c + new Vec2(3 * T, 0), 1);
		ServerPlayer forte = ForjarV2("Forte", c + new Vec2(3 * T, 4 * T), 50_000);
		// (sem Ki o alvo nao sorteia raspao nem deflexao: a bancada mede a regra, e nao o dado)
		fraco.Ficha.Ki = forte.Ficha.Ki = 0;

		List<string> Fazer(ServerPlayer quem, string acao, string arg) => Ouvir(() => ComandoDeItem(quem, "item_" + acao, arg));
		bool Disse(List<string> falas, string trecho) => falas.Exists(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));
		List<Projetil> NoAr() => [.. ProjeteisDaZona(p.Zone.Hash).Where(x => x.Vivo)];
		void Voar() { for (int i = 0; i < 120 && NoAr().Count > 0; i++) TickDosProjeteis(Protocol.TickSeconds); }
		// SO O AR: o `LimparTudoDaBancada` tira tambem os CORPOS do mundo, e o tiro seguinte sairia de um atirador que ja nao existe.
		void LimparOAr() { List<Projetil> ar = ProjeteisDaZona(p.Zone.Hash); _projeteisVivos -= ar.Count; ar.Clear(); }
		void Esfriar() => AdiantoDoRelogioDeTeste += 11_000;
		string ArmaNaMao(string @base) => p.Mochila.Pilhas.Find(x => TechDeBolso.Base(x.Id) == @base).Id ?? "";
		void SoIsto(params string[] itens) { p.Mochila.Pilhas.Clear(); foreach (string i in itens) p.Mochila.Guardar(i); }
		void Curar(ServerPlayer quem) { quem.Combate!.Corpo.Restaurar(); quem.Combate.SincronizarVida(); }

		try
		{
			// ---------------------------------------------------------------- as fichas
			string[] doLote = [.. ArmasDeFogo.Ids, Atordoador.Id, Bombas.Nuke, Bombas.LixoToxico, Bombas.Fumaca, Bombas.Detonador,
							   CampoDeForca.Id, DrenoDeEnergia.Luvas, DrenoDeEnergia.Botas];
			List<string> semAcao = [.. doLote.Where(id => CatalogoDeItens.Get(id) is not { AcoesDoItem.Length: > 0 })];
			List<string> semBotao = [.. doLote.SelectMany(id => (CatalogoDeItens.Get(id)?.AcoesDoItem ?? []).Select(a => (id, a)))
											  .Where(x => TechDeBolso.BotaoDa(x.a, x.id) == null).Select(x => $"{x.id}:{x.a}")];
			AfirmarV21($"**OS {doLote.Length} ITENS DO LOTE TEM ACAO** (as oito armas, o atordoador, as tres bombas, o detonador, o campo de forca e as duas pecas de dreno), e toda acao tem o botao dela; a caixa de municao existe e empilha",
					   semAcao.Count == 0 && semBotao.Count == 0 && CatalogoDeItens.Get(ArmasDeFogo.Municao) is { Empilhavel: true },
					   $"sem acao: {string.Join(", ", semAcao)}; sem botao: {string.Join(", ", semBotao)}");
			AfirmarV21("toda melhoria paga do lote oferece as DUAS moedas (`TechDeBolso.Botao.Paga`): a forca, o critico, os dois efeitos, a reserva, o reforco da bomba -- e a chave do lote 1",
					   new[] { ArmasDeFogo.AcaoForca, ArmasDeFogo.AcaoCritico, ArmasDeFogo.AcaoEmpurrao, ArmasDeFogo.AcaoAtordoar, ArmasDeFogo.AcaoReserva,
							   ArmasDeFogo.AcaoEncher, Bombas.AcaoReforcar, TechDeBolso.AcaoReforcar }
						   .All(a => TechDeBolso.BotaoDa(a, ArmasDeFogo.Blaster) is { Paga: true })
					   && TechDeBolso.BotaoDa(ArmasDeFogo.AcaoCritico, ArmasDeFogo.Pistola) is { } crit
					   && crit.RotuloEm(Jandirus.Core.Economia.Moeda.Zeni).Contains("500 zeni") && crit.RotuloEm(Jandirus.Core.Economia.Moeda.Recursos).Contains("1.000 recursos"));

			// ---------------------------------------------------------------- a pistola
			SoIsto(ArmasDeFogo.Pistola);
			Fazer(p, ArmasDeFogo.AcaoAtirar, ArmasDeFogo.Pistola);
			List<Projetil> tiro = NoAr();
			string pistola = ArmaNaMao(ArmasDeFogo.Pistola);
			AfirmarV21("**A PISTOLA ATIRA**: sai UMA bala, com o poder da ARMA (`A.BP = powerlevel` = 2) e nao o de quem atira (500), pela cadeia fisica (`physdamage`), com a arte e o som dela -- e o pente cai de 30 pra 29, escrito no id",
					   tiro.Count == 1 && Math.Abs(tiro[0].Bp - 2) < 1e-6 && Math.Abs(tiro[0].ModsBase - 1) < 1e-6 && tiro[0].Fisico && !tiro[0].Empurra
					   && tiro[0].Arte == ArteDeKi.Bala && tiro[0].Som == SomDeKi.Pistola && ArmasDeFogo.Ler(pistola).Carga == 29
					   && CatalogoDeItens.Get(pistola)?.Nome.Contains("29/30") == true,
					   tiro.Count == 0 ? "nenhum tiro" : $"tiros {tiro.Count}; bp {tiro[0].Bp:0.##}; mods {tiro[0].ModsBase:0.##}; fisico {tiro[0].Fisico}; arte {tiro[0].Arte}; som {tiro[0].Som}; id {pistola}");
			double vidaDoFraco = fraco.Ficha.HP;
			Voar();
			AfirmarV21("...e a bala FERE quem e fraco pra ela (o alvo de poder 1 perde vida)", fraco.Ficha.HP < vidaDoFraco - 0.01,
					   $"vida {vidaDoFraco:0.##} -> {fraco.Ficha.HP:0.##}");
			Fazer(p, ArmasDeFogo.AcaoAtirar, pistola);
			AfirmarV21("o intervalo entre dois tiros e o da arma (`spawn(refire) blasting=0`, um segundo): apertar de novo na hora nao atira nem gasta",
					   NoAr().Count == 0 && ArmasDeFogo.Ler(ArmaNaMao(ArmasDeFogo.Pistola)).Carga == 29);

			Esfriar();
			p.Pos = c + new Vec2(0, 4 * T);
			double vidaDoForte = forte.Ficha.HP;
			Fazer(p, ArmasDeFogo.AcaoAtirar, pistola);
			bool saiu = NoAr().Count == 1;
			Voar();
			AfirmarV21("...e contra quem tem quatro vezes o poder do tiro a bala e nada: o alvo de 50.000 nem sente (*\"for those who are Ki deficient\"* -- o tiro vale o que a arma vale)",
					   saiu && Math.Abs(forte.Ficha.HP - vidaDoForte) < 1e-6, $"saiu {saiu}; vida {vidaDoForte:0.##} -> {forte.Ficha.HP:0.##}");
			p.Pos = c;

			Esfriar();
			ArmasDeFogo.ForcaDeQuemAtiraDeTeste = true;
			try
			{
				Fazer(p, ArmasDeFogo.AcaoAtirar, ArmaNaMao(ArmasDeFogo.Pistola));
				List<Projetil> comDefeito = NoAr();
				AfirmarV21("(defeito injetado: o tiro sai com o poder de quem atira) a mesma pistola, e a bala leva os 500 do atirador -- vira uma bola de ki comum",
						   comDefeito.Count == 1 && comDefeito[0].Bp > 100, comDefeito.Count == 0 ? "nenhum tiro" : $"bp {comDefeito[0].Bp:0.##}");
			}
			finally { ArmasDeFogo.ForcaDeQuemAtiraDeTeste = false; LimparOAr(); }

			// ---------------------------------------------------------------- o pente e a recarga
			Esfriar();
			SoIsto(ArmasDeFogo.Escrever(ArmasDeFogo.Pistola, new ArmasDeFogo.Estado(0, 30, 1, false, false)));
			List<string> vazia = Fazer(p, ArmasDeFogo.AcaoAtirar, ArmaNaMao(ArmasDeFogo.Pistola));
			List<string> semCaixa = Fazer(p, ArmasDeFogo.AcaoRecarregar, ArmaNaMao(ArmasDeFogo.Pistola));
			p.Mochila.Guardar(ArmasDeFogo.Municao, 2);
			Fazer(p, ArmasDeFogo.AcaoRecarregar, ArmaNaMao(ArmasDeFogo.Pistola));
			List<string> cheia = Fazer(p, ArmasDeFogo.AcaoRecarregar, ArmaNaMao(ArmasDeFogo.Pistola));
			AfirmarV21("SEM MUNICAO a arma nao atira (`\"Out of ammo, reload.\"`); recarregar pede uma caixa, a GASTA e enche o pente (`Reload()`) -- e o pente cheio recusa antes de gastar outra",
					   NoAr().Count == 0 && Disse(vazia, "sem munição") && Disse(semCaixa, "não tem mais munição")
					   && ArmasDeFogo.Ler(ArmaNaMao(ArmasDeFogo.Pistola)).Carga == 30 && p.Mochila.Quantos(ArmasDeFogo.Municao) == 1 && Disse(cheia, "já está cheio"),
					   $"{string.Join(" | ", vazia)} || {string.Join(" | ", semCaixa)} || carga {ArmasDeFogo.Ler(ArmaNaMao(ArmasDeFogo.Pistola)).Carga}; caixas {p.Mochila.Quantos(ArmasDeFogo.Municao)}");
			ArmasDeFogo.PenteInfinitoDeTeste = true;
			try
			{
				Fazer(p, ArmasDeFogo.AcaoAtirar, ArmaNaMao(ArmasDeFogo.Pistola));
				AfirmarV21("(defeito injetado: municao infinita) o tiro sai e o pente continua em 30",
						   NoAr().Count == 1 && ArmasDeFogo.Ler(ArmaNaMao(ArmasDeFogo.Pistola)).Carga == 30);
			}
			finally { ArmasDeFogo.PenteInfinitoDeTeste = false; LimparOAr(); }

			// ---------------------------------------------------------------- a escopeta
			Esfriar();
			SoIsto(ArmasDeFogo.Escopeta);
			Fazer(p, ArmasDeFogo.AcaoAtirar, ArmasDeFogo.Escopeta);
			List<Projetil> chumbo = NoAr();
			AfirmarV21("a ESCOPETA solta TRES balas por aperto, lado a lado (um tile entre elas), e gasta UM cartucho (`Guns.dm:272-325`)",
					   chumbo.Count == 3 && chumbo.Select(x => MathF.Round(x.Pos.Y)).Distinct().Count() == 3
					   && chumbo.All(x => x.Arte == ArteDeKi.Chumbo && Math.Abs(x.Bp - 5) < 1e-6 && Math.Abs(x.ModsBase - 2) < 1e-6)
					   && ArmasDeFogo.Ler(ArmaNaMao(ArmasDeFogo.Escopeta)).Carga == 5,
					   $"balas {chumbo.Count}; alturas {string.Join("/", chumbo.Select(x => x.Pos.Y.ToString("0")))}; carga {ArmasDeFogo.Ler(ArmaNaMao(ArmasDeFogo.Escopeta)).Carga}");
			LimparOAr();

			// ---------------------------------------------------------------- as melhorias
			Esfriar();
			SoIsto(ArmasDeFogo.Pistola);
			p.Ficha.Zeni = 100_000;
			p.Ficha.Recursos = 100_000;
			double teto = Math.Floor(TetoDeTecnologia.De(p.Ficha.relBPmax, p.Ficha.techskill, p.Ficha.techmod));
			Fazer(p, ArmasDeFogo.AcaoForca, ArmasDeFogo.Pistola);
			bool forcaNoTeto = Math.Abs(ForcaDaArma(ArmaNaMao(ArmasDeFogo.Pistola)) - teto) < 1 && Math.Abs(p.Ficha.Zeni - 90_000) < 0.5;
			List<string> jaNoTeto = Fazer(p, ArmasDeFogo.AcaoForca, ArmaNaMao(ArmasDeFogo.Pistola));
			AfirmarV21("a melhoria FORCA leva o poder do tiro ao teto de tecnologia de quem paga (`powerlevel = usr.intBPcap`) pelos 10.000 zeni do rotulo -- e no teto recusa sem cobrar",
					   forcaNoTeto && Disse(jaNoTeto, "já está no teto") && Math.Abs(p.Ficha.Zeni - 90_000) < 0.5,
					   $"forca {ForcaDaArma(ArmaNaMao(ArmasDeFogo.Pistola)):0} (teto {teto:0}); zeni {p.Ficha.Zeni:0}");

			// A REGRA PRINCIPAL DO BP: o teto de tecnologia e funcao do poder de quem pagou -- nao viaja no id (pacote da
			// mochila) nem sai em numero na frase de quem nao tem medidor.
			string reforcada = ArmaNaMao(ArmasDeFogo.Pistola);
			string tetoEscrito = teto.ToString("N0"), tetoCru = teto.ToString("0");
			List<string> semMedidor = Fazer(p, ArmasDeFogo.AcaoInfo, reforcada);
			bool calado = !semMedidor.Exists(f => f.Contains(tetoEscrito) || f.Contains(tetoCru)) && Disse(semMedidor, "o seu");
			Jandirus.Core.Stats.PoderNaFrase.NumeroSemMedidorDeTeste = true;
			List<string> comONumero;
			try { comONumero = Fazer(p, ArmasDeFogo.AcaoInfo, reforcada); }
			finally { Jandirus.Core.Stats.PoderNaFrase.NumeroSemMedidorDeTeste = false; }
			AfirmarV21("**O TETO DE TECNOLOGIA NAO VAZA O PODER DE NINGUEM** (a regra principal do BP): a forca reforcada mora no cofre do servidor e o id da arma leva so a gaveta; sem medidor a frase a mede contra o poder de quem le -- (defeito injetado: o numero sai sem medidor) e com o defeito o numero aparece",
					   ArmasDeFogo.Ler(reforcada).Cofre > 0 && !reforcada.Contains(tetoCru) && calado && comONumero.Exists(f => f.Contains(tetoEscrito)),
					   $"id {reforcada}; sem medidor: {string.Join(" | ", semMedidor)}");
			Fazer(p, ArmasDeFogo.AcaoCritico, ArmaNaMao(ArmasDeFogo.Pistola) + "/recursos");
			Fazer(p, ArmasDeFogo.AcaoEmpurrao, ArmaNaMao(ArmasDeFogo.Pistola));
			Fazer(p, ArmasDeFogo.AcaoAtordoar, ArmaNaMao(ArmasDeFogo.Pistola));
			List<string> deNovo = Fazer(p, ArmasDeFogo.AcaoEmpurrao, ArmaNaMao(ArmasDeFogo.Pistola));
			ArmasDeFogo.Estado melhorada = ArmasDeFogo.Ler(ArmaNaMao(ArmasDeFogo.Pistola));
			Fazer(p, ArmasDeFogo.AcaoAtirar, ArmaNaMao(ArmasDeFogo.Pistola));
			List<Projetil> turbinado = NoAr();
			AfirmarV21("o CRITICO sobe um ponto por `500*critical` -- pago em RECURSOS, o dobro --, e os efeitos de ARREMESSO (2.000) e de PARALISIA (3.000) se instalam uma vez so; a bala seguinte carrega os dois",
					   melhorada.Critico == 2 && melhorada.Empurra && melhorada.Atordoa && Math.Abs(p.Ficha.Recursos - 99_000) < 0.5
					   && Math.Abs(p.Ficha.Zeni - 85_000) < 0.5 && deNovo.Count > 0 && turbinado.Count == 1 && turbinado[0].Empurra && turbinado[0].Paralisia,
					   $"crit {melhorada.Critico}; k {melhorada.Empurra}; s {melhorada.Atordoa}; recursos {p.Ficha.Recursos:0}; zeni {p.Ficha.Zeni:0}; {string.Join(" | ", deNovo)}");
			LimparOAr();

			// ---------------------------------------------------------------- as de energia e a de ki
			Esfriar();
			SoIsto(ArmasDeFogo.Blaster);
			p.Combate!.Letal = true;
			Fazer(p, ArmasDeFogo.AcaoAtirar, ArmasDeFogo.Blaster);
			List<Projetil> raio = NoAr();
			Fazer(p, ArmasDeFogo.AcaoReserva, ArmaNaMao(ArmasDeFogo.Blaster));
			ArmasDeFogo.Estado dobrada = ArmasDeFogo.Ler(ArmaNaMao(ArmasDeFogo.Blaster));
			List<string> jaCheia = Fazer(p, ArmasDeFogo.AcaoEncher, ArmaNaMao(ArmasDeFogo.Blaster));
			List<string> semCartucho = Fazer(p, ArmasDeFogo.AcaoRecarregar, ArmaNaMao(ArmasDeFogo.Blaster));
			AfirmarV21("o BLASTER atira energia: o tiro nao e fisico e NAO MATA mesmo com quem atira em modo letal (`A.murderToggle=0`), gasta a reserva (10 -> 9), e a reserva DOBRA e enche pagando (`10*maxreserve`); cheia, encher recusa -- e ele nao usa municao",
					   raio.Count == 1 && !raio[0].Fisico && !raio[0].Letal && Math.Abs(raio[0].Bp - 100) < 1e-6 && Math.Abs(raio[0].ModsBase - 2) < 1e-6
					   && raio[0].Som == SomDeKi.Blaster && dobrada is { Carga: 20, Pente: 20 } && Disse(jaCheia, "já está cheia")
					   && !CatalogoDeItens.Get(ArmasDeFogo.Blaster)!.AcoesDoItem.Contains(ArmasDeFogo.AcaoRecarregar) && Disse(semCartucho, "não dá pra"),
					   raio.Count == 0 ? "nenhum tiro" : $"fisico {raio[0].Fisico}; letal {raio[0].Letal}; bp {raio[0].Bp:0}; reserva {dobrada.Carga}/{dobrada.Pente}; {string.Join(" | ", jaCheia)}");
			p.Combate.Letal = false;
			LimparOAr();

			Esfriar();
			SoIsto(ArmasDeFogo.Gaster);
			p.Ficha.Ki = p.Ficha.MaxKi;
			double kiDeAntes = p.Ficha.Ki, custoDoGaster = ArmasDeFogo.KiDoGasterPorTiro * p.Ficha.BaseDrain();
			Fazer(p, ArmasDeFogo.AcaoAtirar, ArmasDeFogo.Gaster);
			List<Projetil> gaster = NoAr();
			double kiGasto = kiDeAntes - p.Ficha.Ki;
			LimparOAr();
			Esfriar();
			p.Ficha.Ki = 0;
			List<string> semKi = Fazer(p, ArmasDeFogo.AcaoAtirar, ArmasDeFogo.Gaster);
			AfirmarV21("o GASTER BLASTER nao tem pente: cada tiro cobra `10 * BaseDrain` do Ki de quem atira (poder do tiro 10.000), e sem Ki ele nao sai",
					   gaster.Count == 1 && Math.Abs(gaster[0].Bp - 10_000) < 1e-6 && Math.Abs(kiGasto - custoDoGaster) < 0.01 && NoAr().Count == 0 && semKi.Count > 0,
					   $"tiros {gaster.Count}; ki gasto {kiGasto:0.###} (esperado {custoDoGaster:0.###}); {string.Join(" | ", semKi)}");
			p.Ficha.Ki = p.Ficha.MaxKi;

			// ---------------------------------------------------------------- o gatilho de tecla
			Esfriar();
			SoIsto(ArmasDeFogo.Escrever(ArmasDeFogo.Pistola, new ArmasDeFogo.Estado(0, 30, 1, false, false)), ArmasDeFogo.Rifle);
			bool temFato = (FatosDe(p) & Jandirus.Core.Verbos.Fatos.TemArmaDeFogo) != 0;
			Ouvir(() => Verbo(p, ArmasDeFogo.VerboAtirar, ""));
			bool atirouComORifle = NoAr().Count == 1 && ArmasDeFogo.Ler(ArmaNaMao(ArmasDeFogo.Rifle)).Carga == 19;
			LimparOAr();
			SoIsto();
			List<string> desarmado = Ouvir(() => Verbo(p, ArmasDeFogo.VerboAtirar, ""));
			AfirmarV21("o VERB \"Atirar\" (o `verb/fire()` do original, pra por numa tecla) aperta o gatilho da primeira arma que TEM tiro -- a pistola vazia e pulada, o rifle atira --, so existe pra quem carrega uma arma (`Fatos.TemArmaDeFogo`) e recusa quem nao carrega",
					   temFato && atirouComORifle && (FatosDe(p) & Jandirus.Core.Verbos.Fatos.TemArmaDeFogo) == 0 && Disse(desarmado, "não tem uma arma de fogo"),
					   $"fato {temFato}; rifle {atirouComORifle}; {string.Join(" | ", desarmado)}");

			// ---------------------------------------------------------------- o atordoador
			Esfriar();
			ServerPlayer quase = ForjarV2("ForteDePe", c + new Vec2(3 * T, 0), 5_000);
			quase.Ficha.Ki = 0;
			double defesa = quase.Ficha.Ekidef * Math.Max(quase.Ficha.expressedBP, 1) * Math.Max(quase.Ficha.Ekiskill, quase.Ficha.Etechnique);
			AfirmarV21("a conta do ATORDOADOR (`objects.dm:351-354`, com a fracao desinvertida): o limiar de vida e 15 quando a forca do tiro alcanca a defesa do alvo e desce na proporcao quando nao alcanca (um quinto da defesa = limiar 3); quem tem menos de 100 de poder e de 3 de defesa cai sempre",
					   Math.Abs(Atordoador.LimiarDeVida(defesa * 10, quase.Ficha) - 15) < 1e-6
					   && Math.Abs(Atordoador.LimiarDeVida(defesa / 5, quase.Ficha) - 3) < 1e-6
					   && Atordoador.Derruba(100, fraco.Ficha) && !Atordoador.Derruba(100, quase.Ficha),
					   $"defesa {defesa:0.##}; limiar cheio {Atordoador.LimiarDeVida(defesa * 10, quase.Ficha):0.##}; a um quinto {Atordoador.LimiarDeVida(defesa / 5, quase.Ficha):0.##}");
			fraco.Pos = c + new Vec2(40 * T, 0);
			SoIsto(Atordoador.Id);
			Fazer(p, ArmasDeFogo.AcaoAtirar, Atordoador.Id);
			bool saiuOAtordoador = NoAr() is [{ Nocauteia: true, Paralisia: true, Letal: false }];
			Voar();
			string vazio = ArmaNaMao(Atordoador.Id);
			AfirmarV21("o ATORDOADOR contra quem esta de pe e forte: PARALISA (o `paralysis` do tiro) e nao derruba -- e fica VAZIO, um tiro so",
					   saiuOAtordoador && _paralisadoAte.ContainsKey(quase.Id) && !quase.Ficha.KO && !Atordoador.Carregado(vazio)
					   && CatalogoDeItens.Get(vazio)?.AcoesDoItem.Contains(ArmasDeFogo.AcaoAtirar) == false,
					   $"saiu {saiuOAtordoador}; paralisado {_paralisadoAte.ContainsKey(quase.Id)}; KO {quase.Ficha.KO}; id {vazio}");
			double custoDaRecarga = Atordoador.Custo(TetoDeTecnologia.De(p.Ficha.relBPmax, p.Ficha.techskill, p.Ficha.techmod));
			double zeniAntes = p.Ficha.Zeni;
			Fazer(p, ArmasDeFogo.AcaoRecarregar, vazio);
			AfirmarV21("...e recarregar DEVOLVE o tiro, por `100*log(intBPcap)` (no original o `Reload()` cobra e nunca zera o `used`: a arma morria no primeiro tiro)",
					   Atordoador.Carregado(ArmaNaMao(Atordoador.Id)) && Math.Abs(zeniAntes - p.Ficha.Zeni - custoDaRecarga) < 0.01,
					   $"carregado {Atordoador.Carregado(ArmaNaMao(Atordoador.Id))}; pagou {zeniAntes - p.Ficha.Zeni:0.##} (esperado {custoDaRecarga:0.##})");
			_paralisadoAte.Remove(quase.Id);
			DesfazerV2(quase);

			// O ALVO QUE CAI SEMPRE (menos de 100 de poder e de 3 de defesa), com a vida CHEIA: o tiro de fabrica nao o
			// derrubaria pelo dano -- quem derruba e a regra do atordoador.
			ServerPlayer mole = ForjarV2("Mole", c + new Vec2(3 * T, 0), 50);
			mole.Ficha.Ki = 0;
			Esfriar();
			Atordoador.NuncaDerrubaDeTeste = true;
			try
			{
				Fazer(p, ArmasDeFogo.AcaoAtirar, ArmaNaMao(Atordoador.Id));
				Voar();
				AfirmarV21("(defeito injetado: o tiro so paralisa) o alvo fraco leva o tiro, fica paralisado e continua de pe -- o atordoador vira a tecnica Paralysis",
						   !mole.Ficha.KO && _paralisadoAte.ContainsKey(mole.Id), $"vida {mole.Ficha.HP:0.#}; KO {mole.Ficha.KO}");
			}
			finally { Atordoador.NuncaDerrubaDeTeste = false; }
			Esfriar();
			_paralisadoAte.Remove(mole.Id);
			Curar(mole);
			SoIsto(Atordoador.Id);
			Fazer(p, ArmasDeFogo.AcaoAtirar, Atordoador.Id);
			Voar();
			AfirmarV21("**...e o mesmo tiro, valendo, DERRUBA NA HORA quem e fraco o bastante** (`spawn M.KO()`), com a vida que tiver", mole.Ficha.KO,
					   $"vida {mole.Ficha.HP:0.#}; KO {mole.Ficha.KO}");
			_paralisadoAte.Remove(mole.Id);
			DesfazerV2(mole);

			// ---------------------------------------------------------------- o campo de forca
			Esfriar();
			ServerPlayer protegido = ForjarV2("Protegido", c + new Vec2(3 * T, 0), 50);
			protegido.Ficha.Ki = 0;
			protegido.Mochila.Guardar(CampoDeForca.Id);
			Ouvir(() => ComandoDeItem(protegido, "item_" + CampoDeForca.AcaoLigar, CampoDeForca.Id));
			string ligado = protegido.Mochila.Pilhas.Find(x => TechDeBolso.Base(x.Id) == CampoDeForca.Id).Id ?? "";
			double tetoDoProtegido = TetoDeTecnologia.De(protegido.Ficha.relBPmax, protegido.Ficha.techskill, protegido.Ficha.techmod);
			AfirmarV21("o CAMPO DE FORCA liga pela mochila, e na primeira vez ganha a armadura dele: o teto de tecnologia de quem o liga (`A.maxarmor = usr.intBPcap`)",
					   CampoDeForca.Ligado(ligado) && Math.Abs(ArmaduraDoCampo(ligado).Maxima - tetoDoProtegido) < 1
					   && CampoLigadoDe(protegido) == ligado && !ligado.Contains(tetoDoProtegido.ToString("0")),
					   $"id {ligado}; teto {tetoDoProtegido:0}");
			AfirmarV21("a conta do campo (`takeDamage` com `superarmor = 0.05*maxarmor`): o tiro de poder ate 5% da armadura maxima nao gasta nada, e o resto gasta o que passa disso",
					   Math.Abs(CampoDeForca.Depois(150, 1000, 50) - 150) < 1e-6 && Math.Abs(CampoDeForca.Depois(150, 1000, 100) - 100) < 1e-6
					   && CampoDeForca.Depois(40, 1000, 100) == 0);

			// (a armadura escrita a mao, pra as contas da bancada serem as do paragrafo acima: 150 de 1.000, tiro de 100)
			protegido.Mochila.Pilhas.Clear();
			protegido.Mochila.Guardar(CampoDeForca.Escrever(EscreverNaGaveta(0, 150, 1000), true));
			SoIsto(ArmasDeFogo.Blaster);
			double vidaDoProtegido = protegido.Ficha.HP;
			var armaduras = new List<double>();
			for (int k = 0; k < 3; k++)
			{
				Esfriar();
				Fazer(p, ArmasDeFogo.AcaoAtirar, ArmaNaMao(ArmasDeFogo.Blaster));
				Voar();
				armaduras.Add(CampoLigadoDe(protegido) is { } id ? ArmaduraDoCampo(id).Armadura : -1);
			}
			bool ilesoComCampo = Math.Abs(protegido.Ficha.HP - vidaDoProtegido) < 1e-6;
			Esfriar();
			Fazer(p, ArmasDeFogo.AcaoAtirar, ArmaNaMao(ArmasDeFogo.Blaster));
			Voar();
			AfirmarV21("**O CAMPO LIGADO LEVA O TIRO NO LUGAR DO CORPO** (`objects.dm:421-423`): tres tiros de poder 100 gastam a armadura 150 -> 100 -> 50 -> estourou (o item some) sem o corpo perder vida -- e o quarto, sem campo, fere",
					   armaduras.SequenceEqual([100, 50, -1]) && ilesoComCampo && protegido.Mochila.Pilhas.Count == 0 && protegido.Ficha.HP < vidaDoProtegido - 0.01,
					   $"armaduras {string.Join(" -> ", armaduras)}; ileso {ilesoComCampo}; vida {vidaDoProtegido:0.##} -> {protegido.Ficha.HP:0.##}");

			Curar(protegido);
			protegido.Mochila.Guardar(CampoDeForca.Escrever(EscreverNaGaveta(0, 150, 1000), true));
			CampoDeForca.NaoAbsorveDeTeste = true;
			try
			{
				Esfriar();
				double antes = protegido.Ficha.HP;
				Fazer(p, ArmasDeFogo.AcaoAtirar, ArmaNaMao(ArmasDeFogo.Blaster));
				Voar();
				AfirmarV21("(defeito injetado: o campo nao segura nada) o mesmo tiro, com o campo ligado, fere o corpo -- o item sem acao da v0.2.0",
						   protegido.Ficha.HP < antes - 0.01, $"vida {antes:0.##} -> {protegido.Ficha.HP:0.##}");
			}
			finally { CampoDeForca.NaoAbsorveDeTeste = false; }

			Curar(protegido);
			Esfriar();
			SoIsto(Atordoador.Id);
			Fazer(p, ArmasDeFogo.AcaoAtirar, Atordoador.Id);
			Voar();
			AfirmarV21("...e o atordoador NAO derruba quem esta de campo ligado (`!M.hasForcefield`), mesmo sendo fraco o bastante pra cair sempre",
					   !protegido.Ficha.KO && Atordoador.Derruba(100, protegido.Ficha), $"KO {protegido.Ficha.KO}");
			_paralisadoAte.Remove(protegido.Id);
			DesfazerV2(protegido);

			// ---------------------------------------------------------------- as bombas
			ServerPlayer vitima = ForjarV2("Vitima", c + new Vec2(2 * T, 0), 50);
			ServerPlayer longe = ForjarV2("Longe", c + new Vec2((Bombas.RaioDaNuke + 5) * T, 0), 50);
			AfirmarV21("a conta da EXPLOSAO (`explosions.dm:154-177`, com o `basedamage` que la falta): `BPModulus(forca, poder) / ((Ephysoff + Etechnique) * degrau) * 40 / 1.5` -- 13,3 por membro na metade de dentro do raio contra um igual, a metade disso na de fora",
					   Math.Abs(Bombas.Dano(p.Ficha.expressedBP, p.Ficha, 1) - 40 / 1.5 / (p.Ficha.Ephysoff + p.Ficha.Etechnique)) < 1e-6
					   && Math.Abs(Bombas.Dano(p.Ficha.expressedBP, p.Ficha, 2) * 2 - Bombas.Dano(p.Ficha.expressedBP, p.Ficha, 1)) < 1e-6
					   && Bombas.Degrau(3, 10) == 1 && Bombas.Degrau(6, 10) == 2);

			SoIsto(Bombas.Nuke);
			p.Pos = c;
			Obra caixote = ObraDeBancadaV21(Deposito.Bau, c + new Vec2(T, 2 * T), p);
			List<string> contagemRuim = Fazer(p, Bombas.AcaoArmar, Bombas.Nuke + "/1");
			Fazer(p, Bombas.AcaoArmar, Bombas.Nuke + "/3");
			bool armada = p.Mochila.Pilhas.Count == 0 && _bombasArmadas.Count == 1
						  && LargadosDe(p.Zone.Hash).Exists(l => TechDeBolso.Base(l.Item) == Bombas.Nuke);
			p.Pos = c + new Vec2((Bombas.RaioDaNuke + 8) * T, 0);
			TickDasBombas();
			bool aindaNao = BombasEstouradasDeTeste == bombasDeAntes;
			AdiantoDoRelogioDeTeste += 3_100;
			double vidaDaVitima = vitima.Ficha.HP, vidaDeLonge = longe.Ficha.HP;
			TickDasBombas();
			AfirmarV21("**A NUKE ESTOURA**: armar a larga no chao ja contando (a contagem vai de 3 a 600 s), ela estoura no zero e nao antes, some do chao, FERE quem esta no raio de 35 tiles, envenena (a radioatividade deste port) e derruba a obra ao lado -- e quem esta alem do raio nao leva nada",
					   Disse(contagemRuim, "contagem") && armada && aindaNao && BombasEstouradasDeTeste == bombasDeAntes + 1 && _bombasArmadas.Count == 0
					   && !LargadosDe(vitima.Zone.Hash).Exists(l => TechDeBolso.Base(l.Item) == Bombas.Nuke)
					   && vitima.Ficha.HP < vidaDaVitima - 5 && _venenoG14.ContainsKey(vitima.Id)
					   && Math.Abs(longe.Ficha.HP - vidaDeLonge) < 1e-6 && !_venenoG14.ContainsKey(longe.Id) && !_noChao.Contains(caixote),
					   $"armada {armada}; esperou {aindaNao}; estouros {BombasEstouradasDeTeste - bombasDeAntes}; vitima {vidaDaVitima:0.#} -> {vitima.Ficha.HP:0.#}; veneno {_venenoG14.ContainsKey(vitima.Id)}; longe {vidaDeLonge:0.#} -> {longe.Ficha.HP:0.#}; o bau ao lado caiu {!_noChao.Contains(caixote)}");
			_noChao.Remove(caixote);

			Curar(vitima);
			_venenoG14.Remove(vitima.Id);
			p.Pos = c;
			SoIsto(Bombas.Nuke);
			Fazer(p, Bombas.AcaoArmar, Bombas.Nuke + "/3");
			Ouvir(() => Verbo(p, Chao.VerboPegar, ""));
			AdiantoDoRelogioDeTeste += 3_100;
			int estourosAntes = BombasEstouradasDeTeste;
			TickDasBombas();
			AfirmarV21("...e quem PEGA a bomba do chao antes do zero a DESARMA (o `Cancel()` de quem esta a um tile): ela volta pra mochila e nao estoura",
					   BombasEstouradasDeTeste == estourosAntes && _bombasArmadas.Count == 0 && p.Mochila.Quantos(Bombas.Nuke) == 1,
					   $"estouros {BombasEstouradasDeTeste - estourosAntes}; armadas {_bombasArmadas.Count}; na mochila {p.Mochila.Quantos(Bombas.Nuke)}");

			Bombas.ExplosaoSemDanoDeTeste = true;
			try
			{
				Fazer(p, Bombas.AcaoArmar, Bombas.Nuke + "/3");
				p.Pos = c + new Vec2((Bombas.RaioDaNuke + 8) * T, 0);
				AdiantoDoRelogioDeTeste += 3_100;
				double antes = vitima.Ficha.HP;
				TickDasBombas();
				AfirmarV21("(defeito injetado: a explosao e so o desenho) a nuke estoura ao lado da vitima e ela nao perde vida -- a bomba do original, com os tres defeitos dela",
						   Math.Abs(vitima.Ficha.HP - antes) < 1e-6 && !_venenoG14.ContainsKey(vitima.Id), $"vida {antes:0.#} -> {vitima.Ficha.HP:0.#}");
			}
			finally { Bombas.ExplosaoSemDanoDeTeste = false; }

			// ---------------------------------------------------------------- o detonador
			p.Pos = c + new Vec2((Bombas.RaioDaNuke + 8) * T, 0);
			SoIsto(Bombas.Nuke, Bombas.Detonador);
			Fazer(p, Bombas.AcaoCodigo, Bombas.Nuke + "/kame");
			string comCodigo = ArmaNaMao(Bombas.Nuke);
			List<string> semCodigo = Fazer(p, Bombas.AcaoDetonar, Bombas.Detonador + "/3");
			Fazer(p, Bombas.AcaoCodigo, Bombas.Detonador + "/outro");
			// A BOMBA LARGADA (e nao armada) ao lado da vitima, com o codigo "kame"; o detonador, com o codigo errado.
			p.Mochila.Tirar(comCodigo);
			PorNoChao(vitima.Zone, c, TipoDeLargado.Item, comCodigo, 1);
			Fazer(p, Bombas.AcaoDetonar, ArmaNaMao(Bombas.Detonador) + "/3");
			AdiantoDoRelogioDeTeste += 3_100;
			estourosAntes = BombasEstouradasDeTeste;
			TickDasBombas();
			bool codigoErradoNaoEstoura = BombasEstouradasDeTeste == estourosAntes && LargadosDe(vitima.Zone.Hash).Exists(l => l.Item == comCodigo);
			Bombas.DetonadorSemCodigoDeTeste = true;
			bool defeitoEstoura;
			try
			{
				Curar(vitima);
				Fazer(p, Bombas.AcaoDetonar, ArmaNaMao(Bombas.Detonador) + "/3");
				AdiantoDoRelogioDeTeste += 3_100;
				TickDasBombas();
				defeitoEstoura = BombasEstouradasDeTeste == estourosAntes + 1;
			}
			finally { Bombas.DetonadorSemCodigoDeTeste = false; }
			Curar(vitima);
			_venenoG14.Remove(vitima.Id);
			PorNoChao(vitima.Zone, c, TipoDeLargado.Item, comCodigo, 1);
			Fazer(p, Bombas.AcaoCodigo, ArmaNaMao(Bombas.Detonador) + "/kame");
			Fazer(p, Bombas.AcaoDetonar, ArmaNaMao(Bombas.Detonador) + "/3");
			AdiantoDoRelogioDeTeste += 3_100;
			estourosAntes = BombasEstouradasDeTeste;
			double vidaAntesDoDetonador = vitima.Ficha.HP;
			TickDasBombas();
			AfirmarV21("o DETONADOR estoura de longe toda bomba com o MESMO codigo (`if(B.code == code) B.explode()`): sem codigo gravado ele nao dispara, com o codigo errado nada acontece, e com o certo a bomba largada estoura onde esta",
					   Disse(semCodigo, "grave um código") && codigoErradoNaoEstoura && BombasEstouradasDeTeste == estourosAntes + 1
					   && !LargadosDe(vitima.Zone.Hash).Exists(l => l.Item == comCodigo) && vitima.Ficha.HP < vidaAntesDoDetonador - 5,
					   $"sem codigo: {string.Join(" | ", semCodigo)}; errado nao estoura {codigoErradoNaoEstoura}; estouros {BombasEstouradasDeTeste - estourosAntes}; vitima {vidaAntesDoDetonador:0.#} -> {vitima.Ficha.HP:0.#}");
			AfirmarV21("(defeito injetado: o detonador estoura toda bomba) o codigo errado estourou a bomba do vizinho", defeitoEstoura);

			// ---------------------------------------------------------------- o barril, a fumaca e a fabricacao
			Curar(vitima);
			_venenoG14.Remove(vitima.Id);
			p.Pos = c;
			Curar(p);
			SoIsto(Bombas.LixoToxico);
			double vidaAntesDoBarril = vitima.Ficha.HP;
			Fazer(p, Bombas.AcaoRomper, Bombas.LixoToxico);
			AfirmarV21("o LIXO TOXICO rompido solta a nuvem (`ToxicBoom`, raio 3): envenena quem esta perto -- inclusive quem o rompeu -- e NAO fere (`harmful = 0`)",
					   p.Mochila.Pilhas.Count == 0 && _venenoG14.ContainsKey(vitima.Id) && _venenoG14.ContainsKey(p.Id)
					   && Math.Abs(vitima.Ficha.HP - vidaAntesDoBarril) < 1e-6,
					   $"veneno na vitima {_venenoG14.ContainsKey(vitima.Id)}; em quem rompeu {_venenoG14.ContainsKey(p.Id)}; vida {vidaAntesDoBarril:0.#} -> {vitima.Ficha.HP:0.#}");
			_venenoG14.Remove(vitima.Id);
			_venenoG14.Remove(p.Id);

			SoIsto(Bombas.Fumaca);
			var decalques = new List<(ulong, Protocol.Decal, byte[])>();
			EscutaDeDecalques = decalques;
			try
			{
				Fazer(p, Bombas.AcaoSoltarFumaca, Bombas.Fumaca);
				for (int k = 0; k < 6; k++) { TickDasBombas(); AdiantoDoRelogioDeTeste += 600; }
				int tufos = decalques.Count(d => d.Item2 == Protocol.Decal.Fumaca);
				bool dePe = _fumacas.Count == 1;
				AdiantoDoRelogioDeTeste += 31_000;
				TickDasBombas();
				AfirmarV21("a BOMBA DE FUMACA se gasta e enche o lugar de fumaca por trinta segundos (`SmokeBoom`, `animlast = 300`): a nuvem solta um tufo a cada meio segundo, nao fere ninguem, e acaba sozinha",
						   p.Mochila.Pilhas.Count == 0 && dePe && tufos >= 5 && _fumacas.Count == 0, $"tufos {tufos}; de pe {dePe}; depois {_fumacas.Count}");
			}
			finally { EscutaDeDecalques = null; }

			SoIsto();
			Ouvir(() => AoFabricarTech(p, Bombas.Nuke));
			AfirmarV21("fabricar uma nuke fabrica junto um barril de lixo toxico (`do_toxic_waste = 1`)", p.Mochila.Quantos(Bombas.LixoToxico) == 1);

			// ---------------------------------------------------------------- as luvas e as botas de dreno
			SoIsto(DrenoDeEnergia.Luvas, DrenoDeEnergia.Luvas, DrenoDeEnergia.Botas);
			p.Ficha.Ki = p.Ficha.MaxKi;
			double kiModDeAntes = p.Ficha.KiMod;
			Fazer(p, DrenoDeEnergia.AcaoVestir, DrenoDeEnergia.Luvas);
			string vestidas = DrenoDeEnergia.Escrever(DrenoDeEnergia.Luvas, true);
			List<string> segundaLuva = Fazer(p, DrenoDeEnergia.AcaoVestir, DrenoDeEnergia.Luvas);
			double kiCheio = p.Ficha.Ki;
			TickDoDreno();
			double drenado = kiCheio - p.Ficha.Ki, esperado = DrenoDeEnergia.DrenoPorSegundo(p.Ficha, 1);
			AfirmarV21("**AS LUVAS DE DRENO SE VESTEM**: o estado vai no id, o `KiMod` sobe 0,05 (`add_to_stat(\"Energy Level\", 0.05)`), elas aparecem no corpo (o bit do `PeerLook`), drenam `2*BaseDrain + 0,0001*MaxKi` por segundo -- e a segunda luva recusa (`\"You already have one equipped.\"`)",
					   p.Mochila.Quantos(vestidas) == 1 && Math.Abs(p.Ficha.KiMod - (kiModDeAntes + 0.05)) < 1e-9 && p.Ficha.drenoVestido == 1
					   && EquipamentoNoCorpo.DrenoNoCorpo(p.Mochila) == (true, false) && Math.Abs(drenado - esperado) < 1e-6 && esperado > 0
					   && Disse(segundaLuva, "já veste"),
					   $"vestidas {p.Mochila.Quantos(vestidas)}; KiMod {kiModDeAntes:0.###} -> {p.Ficha.KiMod:0.###}; drenou {drenado:0.####} (esperado {esperado:0.####}); {string.Join(" | ", segundaLuva)}");
			Fazer(p, DrenoDeEnergia.AcaoVestir, DrenoDeEnergia.Botas);
			bool duasPecas = p.Ficha.drenoVestido == 2 && Math.Abs(p.Ficha.KiMod - (kiModDeAntes + 0.10)) < 1e-9;
			// A PECA VESTIDA SAI DA MOCHILA POR FORA (roubada, largada, guardada num bau): o bonus sai com ela no tique seguinte.
			p.Mochila.Tirar(vestidas);
			TickDoDreno();
			bool umaPeca = p.Ficha.drenoVestido == 1 && Math.Abs(p.Ficha.KiMod - (kiModDeAntes + 0.05)) < 1e-9;
			Fazer(p, DrenoDeEnergia.AcaoVestir, DrenoDeEnergia.Escrever(DrenoDeEnergia.Botas, true));
			AfirmarV21("...as botas somam outro tanto, e o bonus SEGUE o que esta vestido: a peca que sai da mochila por qualquer caminho leva o bonus embora, e tirar a ultima devolve o `KiMod` de antes -- sem sobra",
					   duasPecas && umaPeca && p.Ficha.drenoVestido == 0 && Math.Abs(p.Ficha.KiMod - kiModDeAntes) < 1e-9
					   && EquipamentoNoCorpo.DrenoNoCorpo(p.Mochila) == (false, false),
					   $"duas {duasPecas}; uma {umaPeca}; no fim {p.Ficha.drenoVestido} peca(s), KiMod {p.Ficha.KiMod:0.###}");

			DrenoDeEnergia.NaoFazNadaDeTeste = true;
			try
			{
				SoIsto(DrenoDeEnergia.Luvas);
				p.Ficha.Ki = p.Ficha.MaxKi;
				Fazer(p, DrenoDeEnergia.AcaoVestir, DrenoDeEnergia.Luvas);
				TickDoDreno();
				AfirmarV21("(defeito injetado: a peca vestida nao faz nada) vestir nao muda o `KiMod` e o segundo passa sem drenar -- a roupa sem acao da v0.2.0",
						   Math.Abs(p.Ficha.KiMod - kiModDeAntes) < 1e-9 && Math.Abs(p.Ficha.Ki - p.Ficha.MaxKi) < 1e-9);
			}
			finally { DrenoDeEnergia.NaoFazNadaDeTeste = false; }
			DesfazerV2(vitima, longe);
		}
		finally
		{
			ArmasDeFogo.ForcaDeQuemAtiraDeTeste = ArmasDeFogo.PenteInfinitoDeTeste = Atordoador.NuncaDerrubaDeTeste = false;
			CampoDeForca.NaoAbsorveDeTeste = Bombas.ExplosaoSemDanoDeTeste = Bombas.DetonadorSemCodigoDeTeste = DrenoDeEnergia.NaoFazNadaDeTeste = false;
			EscutaDeDecalques = null;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			_bombasArmadas.Clear();
			_detonadores.Clear();
			_fumacas.Clear();
			_armaProntaEm.Clear();
			_venenoG14.Clear();
			_paralisadoAte.Clear();
			LargadosDe(p.Zone.Hash).Clear();
			_cofreDeTech = cofreDaX2;
			_cofreSujo = false;
			DesfazerV2(p, fraco, forte);
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// X3) AS MAQUINAS DE CHAO E AS FERRAMENTAS DE PAREDE -- o lote T3
	// =====================================================================
	private void AsMaquinasDeChaoV21()
	{
		GD.Print("[v021] -- X3) A TECH PORTADA, lote 3: o reparador, o reforcador, o destruidor, a estacao de recarga, a lua artificial e o emissor (item 13)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		const float T = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(10);
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		double ceuDeAntes = _adiantoDoCeu;
		var obras = new List<Obra>();
		ServerPlayer p = ForjarV2("Engenheiro", c, 500);
		p.Conta = "bancada_v021_engenheiro";
		ServerPlayer vizinho = ForjarV2("Vizinho", c + new Vec2(6 * T, 6 * T), 500);
		vizinho.Conta = "bancada_v021_vizinho";
		p.Ficha.Zeni = p.Ficha.Recursos = 5_000_000;

		List<string> Fazer(ServerPlayer quem, string acao, string arg) => Ouvir(() => ComandoDeItem(quem, "item_" + acao, arg));
		bool Disse(List<string> falas, string trecho) => falas.Exists(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));
		Obra Por(string tipo, Vec2 onde, ServerPlayer dono, double armadura, double maxima, bool aparafusada = true)
		{
			Obra o = ObraDeBancadaV21(tipo, onde, dono, aparafusada);
			o.ArmaduraMax = maxima;
			o.Armadura = armadura;
			obras.Add(o);
			return o;
		}
		double teto = TetoDeTecnologia.De(p.Ficha.relBPmax, p.Ficha.techskill, p.Ficha.techmod);

		try
		{
			// ---------------------------------------------------------------- o reparador
			Obra antena = Por(MurosDeTech.Reparador, c + new Vec2(2 * T, 0), p, 100, 100);
			Obra ferida = Por(Deposito.Bau, c + new Vec2(5 * T, 0), p, 40, 100);
			Obra longe = Por(Deposito.Bau, c + new Vec2((MurosDeTech.RaioEmTiles + 6) * T, 0), p, 40, 100);
			MurosDeTech.NaoReparaDeTeste = true;
			try
			{
				RepararPelasAntenas();
				AfirmarV21("(defeito injetado: o reparador nao repara) a volta passa e a construcao ferida continua ferida -- a antena parada da v0.2.0",
						   Math.Abs(ferida.Armadura - 40) < 1e-6);
			}
			finally { MurosDeTech.NaoReparaDeTeste = false; }
			_relogioDosReparos = 0;
			TickDosReparadores(MurosDeTech.SegundosEntreReparos - 1);
			bool antesDaHora = Math.Abs(ferida.Armadura - 40) < 1e-6;
			TickDosReparadores(1.5);
			AfirmarV21("**O REPARADOR DE PAREDES REPARA**: de minuto em minuto (`sleep(600)`) cura a armadura de toda construcao a ate 15 tiles -- e nao a de quem esta alem",
					   antesDaHora && Math.Abs(ferida.Armadura - 100) < 1e-6 && Math.Abs(longe.Armadura - 40) < 1e-6,
					   $"antes da hora {antesDaHora}; perto {ferida.Armadura:0}; longe {longe.Armadura:0}");
			List<string> info = Ouvir(() => { p.Pos = c + new Vec2(T, 0); ComandoDeInteracao(p, MurosDeTech.VerboInfo, ""); });
			AfirmarV21("...e o menu da tecla E dele diz o que alcanca e quando cura de novo", Disse(info, "15 tiles") && Disse(info, "próxima volta"),
					   string.Join(" | ", info));
			_noChao.Remove(antena);

			// ---------------------------------------------------------------- o reforcador
			Jandirus.Core.World.BlocoDef? parede = _blocos?.Todos.FirstOrDefault(d => d.Classe == Jandirus.Core.World.ClasseDeBloco.Parede);
			(int px, int py) = CelulaDoPonto(c);
			p.Pos = c;
			// (DENTRO do corredor da bancada, que vai de py-1 a py+1, e a tres tiles de `c`: fora do alcance do destruidor)
			bool subiu = parede != null && ErguerBloco(p, parede.Numero, px + 3, py + 1, "");
			BlocoDePe? erguido = BlocoEm(p.Zone, px + 3, py + 1);
			if (erguido != null) erguido.Resistencia = 25;
			Obra reforcador = Por(MurosDeTech.Reforcador, c + new Vec2(-2 * T, 0), p, 100, 100);
			Obra fraca = Por(Deposito.Bau, c + new Vec2(3 * T, 2 * T), vizinho, 10, 50);
			Obra fortaleza = Por(Deposito.Bau, c + new Vec2(3 * T, -2 * T), vizinho, teto * 10, teto * 10);
			p.Pos = c + new Vec2(-T, 0);
			List<string> reforco = Ouvir(() => ComandoDeInteracao(p, MurosDeTech.VerboReforcar, ""));
			AfirmarV21("o REFORCADOR, acionado, leva a armadura maxima das construcoes e a resistencia das paredes erguidas a 15 tiles ao teto de tecnologia de quem o aciona (`M.maxarmor = usr.intBPcap`), e cura tudo -- e NAO rebaixa o que ja e mais forte",
					   subiu && erguido != null && Math.Abs(erguido.Resistencia - teto) < 1e-6
					   && Math.Abs(fraca.ArmaduraMax - teto) < 1e-6 && Math.Abs(fraca.Armadura - teto) < 1e-6
					   && Math.Abs(fortaleza.ArmaduraMax - teto * 10) < 1e-6 && Disse(reforco, "teto da sua tecnologia"),
					   $"parede subiu {subiu}, resistencia {erguido?.Resistencia:0} (teto {teto:0}); fraca {fraca.Armadura:0}/{fraca.ArmaduraMax:0}; fortaleza {fortaleza.ArmaduraMax:0} | {string.Join(" | ", reforco)}");
			_noChao.Remove(reforcador);
			_noChao.Remove(fraca);
			_noChao.Remove(fortaleza);

			// ---------------------------------------------------------------- o reparador portatil
			p.Pos = c;
			Obra aoLado = Por(Deposito.Bau, c + new Vec2(T, 0), p, 10, 100);
			ferida.Armadura = 10;
			p.Mochila.Pilhas.Clear();
			p.Mochila.Guardar(MurosDeTech.ReparadorPortatil);
			List<string> reparo = Fazer(p, MurosDeTech.AcaoReparar, MurosDeTech.ReparadorPortatil);
			AfirmarV21("o REPARADOR PORTATIL cura o que esta a um tile de quem o usa (`range(1, src)`) -- e so isso",
					   Math.Abs(aoLado.Armadura - 100) < 1e-6 && Math.Abs(ferida.Armadura - 10) < 1e-6 && Disse(reparo, "1 construção"),
					   $"ao lado {aoLado.Armadura:0}; a cinco tiles {ferida.Armadura:0} | {string.Join(" | ", reparo)}");

			// ---------------------------------------------------------------- o destruidor
			Obra doVizinhoForte = Por(Deposito.Bau, c + new Vec2(0, -T), vizinho, teto * 10, teto * 10);
			Obra minhaForte = Por(Deposito.Bau, c + new Vec2(-T, 0), p, teto * 10, teto * 10);
			if (erguido != null) erguido.Resistencia = teto * 10;
			bool subiuPerto = parede != null && ErguerBloco(p, parede.Numero, px - 1, py + 1, "");
			p.Mochila.Pilhas.Clear();
			p.Mochila.Guardar(MurosDeTech.Destruidor);
			double zeniAntes = p.Ficha.Zeni;
			List<string> destruicao = Fazer(p, MurosDeTech.AcaoDestruir, MurosDeTech.Destruidor);
			AfirmarV21("**O DESTRUIDOR DESTROI**: por 5.000 zeni cai o que ha a um tile e que a tecnologia de quem paga alcanca -- a construcao fraca e a parede erguida --, e o que e DELE mesmo sendo mais forte; o que e de outro e mais forte que a tecnologia dele fica de pe",
					   Math.Abs(zeniAntes - p.Ficha.Zeni - MurosDeTech.CustoDeDestruir) < 0.5 && !_noChao.Contains(aoLado) && !_noChao.Contains(minhaForte)
					   && _noChao.Contains(doVizinhoForte) && subiuPerto && BlocoEm(p.Zone, px - 1, py + 1) == null && _noChao.Contains(ferida),
					   $"pagou {zeniAntes - p.Ficha.Zeni:0}; fraca de pe {_noChao.Contains(aoLado)}; minha forte de pe {_noChao.Contains(minhaForte)}; do vizinho de pe {_noChao.Contains(doVizinhoForte)}; bloco {(BlocoEm(p.Zone, px - 1, py + 1) == null ? "caiu" : "de pe")} | {string.Join(" | ", destruicao)}");
			double recursosAntes = p.Ficha.Recursos;
			MurosDeTech.DestroiTudoDeTeste = true;
			try
			{
				Fazer(p, MurosDeTech.AcaoDestruir, MurosDeTech.Destruidor + "/recursos");
				AfirmarV21("(defeito injetado: o destruidor derruba tudo) o mesmo gesto -- pago em RECURSOS, o dobro -- leva tambem a construcao do vizinho, dez vezes mais forte que a tecnologia de quem o usa",
						   !_noChao.Contains(doVizinhoForte) && Math.Abs(recursosAntes - p.Ficha.Recursos - 2 * MurosDeTech.CustoDeDestruir) < 0.5,
						   $"do vizinho de pe {_noChao.Contains(doVizinhoForte)}; recursos gastos {recursosAntes - p.Ficha.Recursos:0}");
			}
			finally { MurosDeTech.DestroiTudoDeTeste = false; }
			if (erguido != null && BlocoEm(p.Zone, erguido.X, erguido.Y) != null) DerrubarBloco(erguido, p);

			// ---------------------------------------------------------------- a estacao de recarga
			Vec2 naEstacao = c + new Vec2(8 * T, 0);
			Obra estacao = Por(EstacaoDeRecarga.Tipo, naEstacao, p, 100, 100, aparafusada: false);
			ServerPlayer robo = ForjarV2("Robo", naEstacao, 500, "Android");
			robo.Ficha.stamina = 50;
			robo.Ficha.Ki = robo.Ficha.MaxKi / 2;
			double kiDoRobo = robo.Ficha.Ki;
			TickDasEstacoes();
			bool soltaNaoFunciona = Math.Abs(robo.Ficha.stamina - 50) < 1e-6;
			estacao.Aparafusada = true;
			TickDasEstacoes();
			double energia = estacao.Tech?.Num(EstacaoDeRecarga.ChaveEnergia) ?? -1;
			AfirmarV21("**A ESTACAO DE RECARGA RECARREGA O ANDROIDE QUE ESTA EM CIMA DELA**: por volta, `efficiency` de folego e `efficiency * MaxKi / 1000` de Ki, e cada um custa `5 / efficiency` da energia dela (100 -> 80) -- e solta ela nao funciona (`if(Bolted)`)",
					   soltaNaoFunciona && Math.Abs(robo.Ficha.stamina - 50.5) < 1e-6
					   && Math.Abs(robo.Ficha.Ki - (kiDoRobo + 0.5 * robo.Ficha.MaxKi / 1000)) < 1e-6 && Math.Abs(energia - 80) < 1e-6,
					   $"solta parada {soltaNaoFunciona}; folego {robo.Ficha.stamina:0.##}; ki +{robo.Ficha.Ki - kiDoRobo:0.####}; energia {energia:0.##}");
			ServerPlayer gente = ForjarV2("Gente", naEstacao, 500);
			robo.Pos = c;
			gente.Ficha.stamina = 50;
			TickDasEstacoes();
			bool humanoNao = Math.Abs(gente.Ficha.stamina - 50) < 1e-6;
			EstacaoDeRecarga.RecarregaQualquerUmDeTeste = true;
			try { TickDasEstacoes(); }
			finally { EstacaoDeRecarga.RecarregaQualquerUmDeTeste = false; }
			AfirmarV21("...e ela e dos androides (`M.Race==\"Android\"`): o humano em cima dela nao recebe nada -- (defeito injetado: recarrega qualquer um) e com o defeito recebe",
					   humanoNao && gente.Ficha.stamina > 50, $"humano parado {humanoNao}; com o defeito {gente.Ficha.stamina:0.##}");
			p.Pos = naEstacao + new Vec2(T, 0);
			double zeniDaEstacao = p.Ficha.Zeni;
			// (2026-10-19) as melhorias tem teto pela tecnologia de quem paga (`TetoPorTecnologia`, medido na `--melhoriasteste`):
			// aqui se mede a melhoria em si, com os 100 do jogo de antes -- e a tecnologia do engenheiro volta logo depois.
			double techDoEngenheiro = p.Ficha.techskill;
			p.Ficha.techskill = TetoPorTecnologia.TechSemTeto;
			Ouvir(() => ComandoDeInteracao(p, EstacaoDeRecarga.VerboMelhorar, EstacaoDeRecarga.Eficiencia));
			Ouvir(() => ComandoDeInteracao(p, EstacaoDeRecarga.VerboMelhorar, EstacaoDeRecarga.Nivel));
			Ouvir(() => ComandoDeInteracao(p, EstacaoDeRecarga.VerboMelhorar, EstacaoDeRecarga.Solar));
			List<string> jaTem = Ouvir(() => ComandoDeInteracao(p, EstacaoDeRecarga.VerboMelhorar, EstacaoDeRecarga.Solar));
			p.Ficha.techskill = techDoEngenheiro;
			double pagou = zeniDaEstacao - p.Ficha.Zeni;
			estacao.Tech!.N[EstacaoDeRecarga.ChaveEnergia] = 0;
			gente.Pos = c;
			TickDasEstacoes();
			AfirmarV21("as tres MELHORIAS da estacao: eficiencia (+0,5 por `20000 * efficiency`), nivel (`10000 * tier`: a capacidade vira `100 * tier**2`) e os paineis solares (100.000, uma vez so) -- e melhorada ela RENDE energia sozinha (`tier` dos paineis mais `tier` do nivel 2)",
					   Math.Abs(pagou - (10_000 + 10_000 + 100_000)) < 0.5 && Math.Abs(estacao.Tech.Num(EstacaoDeRecarga.ChaveEficiencia) - 1) < 1e-6
					   && (int)estacao.Tech.Num(EstacaoDeRecarga.ChaveNivel) == 2 && estacao.Tech.Ligado(EstacaoDeRecarga.ChaveSolar)
					   && Disse(jaTem, "já tem os painéis") && Math.Abs(estacao.Tech.Num(EstacaoDeRecarga.ChaveEnergia) - 4) < 1e-6
					   && Math.Abs(EstacaoDeRecarga.Capacidade(2) - 400) < 1e-6,
					   $"pagou {pagou:0}; eficiencia {estacao.Tech.Num(EstacaoDeRecarga.ChaveEficiencia):0.#}; nivel {estacao.Tech.Num(EstacaoDeRecarga.ChaveNivel):0}; rendeu {estacao.Tech.Num(EstacaoDeRecarga.ChaveEnergia):0.##}");
			DesfazerV2(robo, gente);

			// ---------------------------------------------------------------- a lua artificial e o emissor
			// (um ceu SEM lua cheia: a bancada mede a lua do chao, e nao a do ceu)
			ServerPlayer macaco = ForjarV2("Macaco", c + new Vec2(-6 * T, 0), 500, "Saiyan");
			for (int h = 0; h < 2000 && CeuDe(macaco) is { Cheia: true, LuaNoCeu: true }; h++) _adiantoDoCeu += 3600;
			bool ceuSemLua = CeuDe(macaco) is not { Cheia: true, LuaNoCeu: true };
			Obra lua = Por(LuaArtificial.Lua, c + new Vec2(-4 * T, 0), p, 100, 100);
			p.Pos = c + new Vec2(-3 * T, 0);
			bool rabo = TemRaboInteiro(macaco);
			bool antesDeAcender = !LuaArtificialAVista(macaco) && EstadoDaArte(lua, _obras?.Get(lua.Tipo)) == LuaArtificial.ArteApagada;
			Ouvir(() => ComandoDeInteracao(p, LuaArtificial.VerboAcender, ""));
			List<string> deNovo = Ouvir(() => ComandoDeInteracao(p, LuaArtificial.VerboAcender, ""));
			List<string> recolher = Ouvir(() => ComandoDeInteracao(p, "pegar", ""));
			TickDasLuas();
			AfirmarV21("**A LUA ARTIFICIAL ACESA E UMA LUA**: o Saiyajin de rabo que a ve vira Oozaru (`M.Apeshit()`), o desenho dela passa a ser o da lua acesa, e ela e a FONTE da fera -- acender de novo recusa (`\"It has already been activated...\"`), e acesa ela nao se recolhe",
					   ceuSemLua && rabo && antesDeAcender && macaco.Oozaru == Jandirus.Core.Forms.FormaOozaru.Regular && LuaArtificialAVista(macaco) && AFonteDaFera(macaco)
					   && EstadoDaArte(lua, _obras?.Get(lua.Tipo)) == LuaArtificial.ArteAcesa && Disse(deNovo, "já foi ativada")
					   && Disse(recolher, "está acesa") && _noChao.Contains(lua),
					   $"ceu sem lua {ceuSemLua}; rabo {rabo}; antes {antesDeAcender}; fera {macaco.Oozaru}; fonte {AFonteDaFera(macaco)} | {string.Join(" | ", deNovo)} | {string.Join(" | ", recolher)}");
			TickDoOozaru(macaco, 0.1);
			bool feraFica = macaco.Oozaru != Jandirus.Core.Forms.FormaOozaru.Nao;
			LuaArtificial.NaoEFonteDeTeste = true;
			bool semFonteComDefeito;
			try { semFonteComDefeito = !AFonteDaFera(macaco); }
			finally { LuaArtificial.NaoEFonteDeTeste = false; }
			AdiantoDoRelogioDeTeste += (long)(LuaArtificial.SegundosAcesa(LuaArtificial.Lua, false) * 1000) + 1000;
			macaco.OozaruAte = NowMs() + 600_000;   // (o prazo da propria fera fica de fora: o que a bancada mede e a FONTE)
			TickDasLuas();
			bool apagou = !_noChao.Contains(lua);
			TickDoOozaru(macaco, 0.1);
			AfirmarV21("...a fera FICA enquanto a lua arde, e quando a lua se apaga e some (ela dura o que o Oozaru dura) a fera se desfaz -- a regra do dono, *\"a forma do oozaru e desfeita no momento que a fonte (...) sumir\"*; (defeito injetado: a lua acesa nao e fonte) e sem a segunda fonte a fera nem teria de onde",
					   feraFica && semFonteComDefeito && apagou && macaco.Oozaru == Jandirus.Core.Forms.FormaOozaru.Nao,
					   $"fera fica {feraFica}; defeito tira a fonte {semFonteComDefeito}; lua apagou {apagou}; depois {macaco.Oozaru}");

			// O EMISSOR: devolve o rabo. (E o rabo perdido de vez -- a regra do dono -- nem ele refaz.)
			Jandirus.Core.Combat.BodyPart? oRabo = macaco.Combate!.Corpo.Achar("Rabo");
			if (oRabo != null) { oRabo.Decepado = true; oRabo.Perdido = false; }
			Obra emissor = Por(LuaArtificial.Emissor, c + new Vec2(-4 * T, 0), p, 100, 100);
			Ouvir(() => ComandoDeInteracao(p, LuaArtificial.VerboAcender, ""));
			TickDasLuas();
			bool raboVoltou = TemRaboInteiro(macaco), viraComOEmissor = macaco.Oozaru != Jandirus.Core.Forms.FormaOozaru.Nao;
			if (macaco.Oozaru != Jandirus.Core.Forms.FormaOozaru.Nao) DesfazerOozaru(macaco, "bancada");
			if (oRabo != null) { oRabo.Decepado = true; oRabo.Perdido = true; }
			_olhadosPelaLua.Remove(emissor.Id);
			TickDasLuas();
			AfirmarV21("o EMISSOR de ondas Blutz devolve o rabo do Saiyajin que o perdeu (`M.Tail_Grow()`) e o transforma -- menos o rabo PERDIDO DE VEZ (regra do dono, `RaboDoSaiyajin`): esse nem o emissor refaz",
					   raboVoltou && viraComOEmissor && !TemRaboInteiro(macaco) && macaco.Oozaru == Jandirus.Core.Forms.FormaOozaru.Nao,
					   $"rabo voltou {raboVoltou}; virou {viraComOEmissor}; perdido de vez continua sem {!TemRaboInteiro(macaco)}");
			_noChao.Remove(emissor);

			// A LUA NEGRA: a melhoria confere as DUAS coisas.
			Obra outraLua = Por(LuaArtificial.Lua, c + new Vec2(-4 * T, 0), p, 100, 100);
			p.Ficha.techskill = 10;
			List<string> semTecnologia = Ouvir(() => ComandoDeInteracao(p, LuaArtificial.VerboMelhorar, ""));
			p.Ficha.techskill = 60;
			double zeniDaLua = p.Ficha.Zeni;
			Ouvir(() => ComandoDeInteracao(p, LuaArtificial.VerboMelhorar, ""));
			AfirmarV21("a LUA NEGRA (`Upgrade_Moon`): um milhao de zeni E 50 de tecnologia -- sem a tecnologia a melhoria recusa (no original a recusa esta escrita com `&&` e quem nao tem a tecnologia passa)",
					   Disse(semTecnologia, "50 de tecnologia") && outraLua.Tech?.Ligado(LuaArtificial.ChaveNegra) == true
					   && Math.Abs(zeniDaLua - p.Ficha.Zeni - LuaArtificial.CustoDaLuaNegra) < 0.5
					   && LuaArtificial.PuxaODourado(LuaArtificial.Lua, true) && !LuaArtificial.PuxaODourado(LuaArtificial.Lua, false),
					   $"{string.Join(" | ", semTecnologia)}; negra {outraLua.Tech?.Ligado(LuaArtificial.ChaveNegra)}; pagou {zeniDaLua - p.Ficha.Zeni:0}");
			DesfazerV2(macaco);
		}
		finally
		{
			MurosDeTech.NaoReparaDeTeste = MurosDeTech.DestroiTudoDeTeste = EstacaoDeRecarga.RecarregaQualquerUmDeTeste = LuaArtificial.NaoEFonteDeTeste = false;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			_adiantoDoCeu = ceuDeAntes;
			foreach (Obra o in obras) _noChao.Remove(o);
			_olhadosPelaLua.Clear();
			if (_bases.TryGetValue(p.Zone.Hash, out BaseDaZona? zonaDaBancada))
				foreach (BlocoDePe b in zonaDaBancada.Celulas.Values.Where(b => b.DonoConta == p.Conta).ToList()) DerrubarBloco(b, p);
			LargadosDe(p.Zone.Hash).Clear();
			AplicarColisaoDasObras(p.Zone);
			GravarMundo();
			DesfazerV2(p, vizinho);
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// X4) O SIMULADOR, O RECRIADOR, A MAQUINA DE CLONAGEM E O BARCO -- a segunda metade do lote T3
	// =====================================================================
	private void AsMaquinasGrandesV21()
	{
		GD.Print("[v021] -- X4) A TECH PORTADA, lote 3 (2a metade): o simulador, o recriador, a maquina de clonagem e o barco (item 13)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		const float T = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(10);
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		CofreDeTech cofreDeAntes = _cofreDeTech;
		_cofreDeTech = new CofreDeTech();
		var obras = new List<Obra>();
		ServerPlayer p = ForjarV2("Piloto", c, 5_000);
		p.Conta = "bancada_v021_piloto";
		ServerPlayer vizinho = ForjarV2("Vizinho", c + new Vec2(8 * T, 8 * T), 500);
		vizinho.Conta = "bancada_v021_vizinho";
		p.Ficha.Zeni = p.Ficha.Recursos = 1_000_000;

		List<string> Fazer(ServerPlayer quem, string acao, string arg) => Ouvir(() => ComandoDeItem(quem, "item_" + acao, arg));
		List<string> Usar(ServerPlayer quem, string verbo, string arg = "") => Ouvir(() => ComandoDeInteracao(quem, verbo, arg));
		bool Disse(List<string> falas, string trecho) => falas.Exists(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));
		Obra Por(string tipo, Vec2 onde)
		{
			Obra o = ObraDeBancadaV21(tipo, onde, p);
			obras.Add(o);
			return o;
		}
		ServerPlayer? ASimulacao() => _simulacoes.Keys.Select(id => _players.GetValueOrDefault(id)).FirstOrDefault(s => s != null);

		try
		{
			// ---------------------------------------------------------------- o simulador
			Obra simulador = Por(Simulador.Tipo, c + new Vec2(2 * T, 0));
			p.Pos = c + new Vec2(T, 0);
			Usar(p, Simulador.VerboIniciar);
			ServerPlayer? sim = ASimulacao();
			bool caca = sim != null && GuiarASimulacao(sim, p.Id, out ServerPlayer? presa, out _) && presa == p;
			AfirmarV21("**O SIMULADOR ERGUE A SIMULACAO**: um corpo com o nome e a cara de quem pediu, o poder dele limitado ao TETO da maquina (`if(x.BP>maxbp) x.BP=maxbp`: 5.000 viram 500), que caca quem o pediu (`x.foundTarget(usr)`) e nao mata -- e a maquina gasta 0,1 de energia",
					   sim != null && sim.Name == "Piloto Simulation" && Math.Abs(sim.Ficha.BP - Simulador.TetoDeFabrica) < 1e-6 && caca
					   && sim.Combate is { Letal: false } && Math.Abs((simulador.Tech?.Num(Simulador.ChaveEnergia) ?? 0) - 0.9) < 1e-6,
					   sim == null ? "nenhuma simulacao" : $"nome {sim.Name}; BP {sim.Ficha.BP:0}; caca {caca}; energia {simulador.Tech?.Num(Simulador.ChaveEnergia):0.##}");
			int idDaSim = sim?.Id ?? 0;
			List<string> cancelou = Usar(p, Simulador.VerboIniciar);
			AfirmarV21("pedir de novo com uma de pe a CANCELA (`\"Simulations cancelled.\"`): o corpo sai do mundo e nenhuma energia e gasta",
					   _simulacoes.Count == 0 && !_players.ContainsKey(idDaSim) && Disse(cancelou, "cancelada")
					   && Math.Abs((simulador.Tech?.Num(Simulador.ChaveEnergia) ?? 0) - 0.9) < 1e-6, string.Join(" | ", cancelou));
			Simulador.SemTetoDeTeste = true;
			try
			{
				Usar(p, Simulador.VerboIniciar);
				AfirmarV21("(defeito injetado: a simulacao nasce sem teto) a mesma maquina ergue a copia com os 5.000 de quem pediu -- o teto nao valeu",
						   ASimulacao() is { } semTeto && semTeto.Ficha.BP > Simulador.TetoDeFabrica * 2);
			}
			finally { Simulador.SemTetoDeTeste = false; foreach (int id in _simulacoes.Keys.ToList()) DesfazerSimulacao(id, null); }

			double zeniDoSim = p.Ficha.Zeni;
			// (2026-10-19) as duas melhorias com os 100 de tecnologia do jogo de antes (o teto pela tecnologia, `TetoPorTecnologia`, e
			// medido na `--melhoriasteste`) -- e a do piloto volta logo depois.
			double techDoPiloto = p.Ficha.techskill;
			p.Ficha.techskill = TetoPorTecnologia.TechSemTeto;
			Usar(p, Simulador.VerboMelhorar, Simulador.Poder);
			Usar(p, Simulador.VerboMelhorar, Simulador.Energia);
			p.Ficha.techskill = techDoPiloto;
			Usar(p, Simulador.VerboIniciar);
			ServerPlayer? maisForte = ASimulacao();
			if (maisForte != null) maisForte.Ficha.KO = true;
			List<string> derrota = maisForte == null ? [] : Ouvir(() => GuiarASimulacao(maisForte, p.Id, out _, out _));
			simulador.Tech!.N[Simulador.ChaveEnergia] = 0.05;
			List<string> semEnergia = Usar(p, Simulador.VerboIniciar);
			List<string> infoDoSim = Usar(p, Simulador.VerboInfo);
			AfirmarV21("as MELHORIAS do simulador: o teto de poder DOBRA pelo proprio teto em zeni (`500*(maxbp/500)`) e a energia ganha um ponto e enche por `1000*MaxEnergy` -- a copia seguinte nasce com 1.000; derrotada, ela se desfaz; sem energia a maquina recusa; e o teto so sai em numero pra quem tem medidor",
					   Math.Abs(zeniDoSim - p.Ficha.Zeni - (500 + 1000)) < 0.5 && maisForte != null && Math.Abs(maisForte.Ficha.BP - 1000) < 1e-6
					   && Disse(derrota, "derrota a simulação") && _simulacoes.Count == 0 && Disse(semEnergia, "sem energia")
					   && !infoDoSim.Exists(f => f.Contains("1.000") && f.Contains("poder de até")),
					   $"pagou {zeniDoSim - p.Ficha.Zeni:0}; BP da 2a {maisForte?.Ficha.BP:0}; {string.Join(" | ", derrota)} | {string.Join(" | ", semEnergia)}");
			_noChao.Remove(simulador);

			// ---------------------------------------------------------------- o recriador
			Jandirus.Core.World.BlocoDef? parede = _blocos?.Todos.FirstOrDefault(d => d.Classe == Jandirus.Core.World.ClasseDeBloco.Parede);
			(int px, int py) = CelulaDoPonto(c);
			p.Pos = c;
			// (as duas DENTRO do corredor da bancada, que vai de px-1 a px+10 e de py-1 a py+1)
			(int ax, int ay) = (px + 3, py + 1);
			(int bx, int by) = (px - 1, py + 1);
			bool ergueu = false;
			List<string> aoErguer = Ouvir(() => ergueu = parede != null && ErguerBloco(p, parede.Numero, ax, ay, "") && ErguerBloco(p, parede.Numero, bx, by, ""));
			p.Mochila.Pilhas.Clear();
			p.Mochila.Guardar(Recriador.Id);
			List<string> semPlanta = Fazer(p, Recriador.AcaoRecriar, Recriador.Id);
			Fazer(p, Recriador.AcaoGravar, Recriador.Id);
			string gravado = p.Mochila.Pilhas.Find(x => TechDeBolso.Base(x.Id) == Recriador.Id).Id ?? "";
			bool temPlanta = PlantaDo(gravado) is { Pecas.Count: 2 };
			if (BlocoEm(p.Zone, ax, ay) is { } umaParede) DerrubarBloco(umaParede, null);
			bool caiu = BlocoEm(p.Zone, ax, ay) == null;
			double zeniDaPlanta = p.Ficha.Zeni;
			List<string> recriou = Fazer(p, Recriador.AcaoRecriar, gravado);
			bool voltou = BlocoEm(p.Zone, ax, ay) is { } deVolta && EDonoDoBloco(deVolta, p);
			double pagouPelaPlanta = zeniDaPlanta - p.Ficha.Zeni;
			List<string> inteira = Fazer(p, Recriador.AcaoRecriar, gravado);
			AfirmarV21("**O RECRIADOR RECRIA**: grava a planta dos blocos erguidos de quem o usa (o estado vai pro cofre, o id leva a gaveta) e, quando um cai, o ergue de novo no lugar por 10 zeni (`10 zenni per turf`) -- e com a planta inteira nao cobra nada",
					   ergueu && Disse(semPlanta, "não dá pra") && temPlanta && caiu && voltou && Math.Abs(pagouPelaPlanta - Recriador.CustoPorBloco) < 0.5
					   && Disse(inteira, "inteira") && Math.Abs(zeniDaPlanta - p.Ficha.Zeni - Recriador.CustoPorBloco) < 0.5,
					   $"ergueu {ergueu} ({string.Join(" | ", aoErguer)}); planta {temPlanta}; caiu {caiu}; voltou {voltou}; pagou {pagouPelaPlanta:0.##} | {string.Join(" | ", recriou)} | {string.Join(" | ", inteira)}");
			vizinho.Pos = c + new Vec2(T, 0);
			vizinho.Mochila.Guardar(gravado);
			if (BlocoEm(p.Zone, bx, by) is { } outraParede) DerrubarBloco(outraParede, null);
			List<string> deOutro = Fazer(vizinho, Recriador.AcaoRecriar, gravado);
			bool naoEDele = Disse(deOutro, "não é sua") && BlocoEm(p.Zone, bx, by) == null;
			Recriador.DeGracaDeTeste = true;
			double antesDoDefeito = p.Ficha.Zeni;
			try { Fazer(p, Recriador.AcaoRecriar, gravado + "/recursos"); }
			finally { Recriador.DeGracaDeTeste = false; }
			AfirmarV21("...a planta e de QUEM A GRAVOU (o vizinho com o mesmo recriador ouve a recusa); (defeito injetado: o recriador ergue de graca) e com o defeito o bloco volta sem custar nada",
					   naoEDele && BlocoEm(p.Zone, bx, by) != null && Math.Abs(antesDoDefeito - p.Ficha.Zeni) < 0.5 && p.Ficha.Recursos >= 1_000_000 - 0.5,
					   $"vizinho recusado {naoEDele}; bloco de pe {BlocoEm(p.Zone, bx, by) != null} | {string.Join(" | ", deOutro)}");
			vizinho.Pos = c + new Vec2(8 * T, 8 * T);

			// ---------------------------------------------------------------- a maquina de clonagem
			ClonagemSemPeerDeTeste = true;
			Obra tanque = Por(MaquinaDeClonagem.Tipo, c + new Vec2(-3 * T, 0));
			p.Pos = c + new Vec2(-2 * T, 0);
			Usar(p, MaquinaDeClonagem.VerboProgramar);
			double zeniDoTanque = p.Ficha.Zeni;
			p.Ficha.techskill = TetoPorTecnologia.TechSemTeto;   // (2026-10-19) idem, as melhorias da clonagem
			Usar(p, MaquinaDeClonagem.VerboMelhorar, MaquinaDeClonagem.Tempo);
			Usar(p, MaquinaDeClonagem.VerboMelhorar, MaquinaDeClonagem.Bateria);
			p.Ficha.techskill = techDoPiloto;
			Usar(p, MaquinaDeClonagem.VerboSenha, "kame");
			vizinho.Pos = p.Pos;
			List<string> negado = Usar(vizinho, MaquinaDeClonagem.VerboProgramar, "errada");
			vizinho.Pos = c + new Vec2(8 * T, 8 * T);
			EstadoDeTech et = tanque.Tech!;
			AfirmarV21("a MAQUINA DE CLONAGEM se programa (`Program()`), se melhora (a sessao mais curta por `1000*Speed`, a bateria por `1000*MaxEnergy`) e ganha senha (1.000): com a senha posta, quem nao a sabe ouve *\"Access denied.\"*",
					   et.Txt(MaquinaDeClonagem.ChaveConta) == p.Conta && Math.Abs(zeniDoTanque - p.Ficha.Zeni - (1000 + 2000 + 1000)) < 0.5
					   && Math.Abs(et.Num(MaquinaDeClonagem.ChaveVelocidade) - 2) < 1e-6 && Math.Abs(et.Num(MaquinaDeClonagem.ChaveEnergia) - 3) < 1e-6
					   && Disse(negado, "acesso negado") && Math.Abs(MaquinaDeClonagem.SegundosDaSessao(2) - 300) < 1e-6
					   && Math.Abs(MaquinaDeClonagem.ChanceDeFalhar(1) - 50) < 1e-6 && Math.Abs(MaquinaDeClonagem.ChanceDeFalhar(5) - 10) < 1e-6,
					   $"conta {et.Txt(MaquinaDeClonagem.ChaveConta)}; pagou {zeniDoTanque - p.Ficha.Zeni:0}; velocidade {et.Num(MaquinaDeClonagem.ChaveVelocidade):0}; energia {et.Num(MaquinaDeClonagem.ChaveEnergia):0} | {string.Join(" | ", negado)}");

			TickDasClonagens();
			bool vivoNaoAbre = et.Num(MaquinaDeClonagem.ChaveSessaoAte) <= 0;
			bool morreu = p.Combate!.Morrer() && p.Ficha.dead;
			MaquinaDeClonagem.NuncaReviveDeTeste = true;
			try { TickDasClonagens(); }
			finally { MaquinaDeClonagem.NuncaReviveDeTeste = false; }
			bool defeitoNaoAbre = et.Num(MaquinaDeClonagem.ChaveSessaoAte) <= 0;
			TickDasClonagens();
			bool abriu = et.Num(MaquinaDeClonagem.ChaveSessaoAte) > 0;
			AdiantoDoRelogioDeTeste += 200_000;
			TickDasClonagens();
			bool aindaMorto = p.Ficha.dead;
			et.N[MaquinaDeClonagem.ChaveFalha] = 1e12;   // (a chance de falhar vira zero: a bancada mede a volta, e nao o dado)
			AdiantoDoRelogioDeTeste += 110_000;
			p.Pos = c + new Vec2(20 * T, 20 * T);
			TickDasClonagens();
			AfirmarV21("**A MAQUINA DE CLONAGEM TRAZ DE VOLTA QUEM A PROGRAMOU**: com ele vivo nada acontece; morto, ela abre uma sessao (`10/Speed` minutos: cinco, com a melhoria), espera o prazo inteiro, e no fim ele volta a vida AO LADO DELA e ela gasta um ponto de energia (3 -> 2) -- (defeito injetado: a maquina nunca revive) e com o defeito nem a sessao abre",
					   vivoNaoAbre && morreu && defeitoNaoAbre && abriu && aindaMorto && !p.Ficha.dead
					   && Vec2.Distance(p.Pos, new Vec2(tanque.X, tanque.Y)) < 2.5f * T && Math.Abs(et.Num(MaquinaDeClonagem.ChaveEnergia) - 2) < 1e-6,
					   $"vivo nao abre {vivoNaoAbre}; morreu {morreu}; defeito nao abre {defeitoNaoAbre}; abriu {abriu}; no meio ainda morto {aindaMorto}; depois vivo {!p.Ficha.dead}; a {Vec2.Distance(p.Pos, new Vec2(tanque.X, tanque.Y)) / T:0.#} tiles; energia {et.Num(MaquinaDeClonagem.ChaveEnergia):0}");
			_noChao.Remove(tanque);

			// ---------------------------------------------------------------- o barco
			Vec2? beira = BeiraDaAguaV21();
			AfirmarV21("(montagem) a zona da bancada tem uma beira d'agua", beira != null);
			if (beira is { } margem)
			{
				p.Pos = c;
				p.Mochila.Pilhas.Clear();
				p.Mochila.Guardar(Barco.Id);
				List<string> noSeco = Fazer(p, Barco.AcaoVelejar, Barco.Id);
				p.Pos = margem;
				p.Facing = Facing.East;
				RecalcularVelocidade(p);
				float passoAPe = p.SpeedStat;
				List<string> zarpou = Fazer(p, Barco.AcaoVelejar, Barco.Id);
				bool velejando = p.Nadando && Velejando(p);
				bool passoIgual = Math.Abs(p.SpeedStat - passoAPe) < 1e-4f;
				p.Pos = margem + new Vec2(T, 0);
				p.Ficha.Ki = p.Ficha.MaxKi;
				TickDoNado(p, 1f);
				bool naoPaga = Math.Abs(p.Ficha.Ki - p.Ficha.MaxKi) < 1e-9 && p.Nadando;
				List<string> naAgua = Fazer(p, Barco.AcaoVelejar, Barco.Id);
				Barco.CobraComoNadoDeTeste = true;
				bool comDefeitoPaga, comDefeitoLento;
				try
				{
					TickDoNado(p, 1f);
					RecalcularVelocidade(p);
					comDefeitoPaga = p.Ficha.Ki < p.Ficha.MaxKi - 1e-9;
					comDefeitoLento = p.SpeedStat < passoAPe - 1e-4f;
				}
				finally { Barco.CobraComoNadoDeTeste = false; }
				p.Ficha.Ki = p.Ficha.MaxKi;
				p.Pos = margem;
				List<string> atracou = Ouvir(() => TickDoNado(p, 0.1f));
				AfirmarV21("**O BARCO VELEJA**: longe da agua recusa (`\"You can only sail in water!\"`); na margem, de frente pra ela, zarpa -- a agua deixa de parar o corpo, no passo de quem anda e SEM o Ki que o nado cobra; em cima da agua nao se atraca, e no seco atraca sozinho",
						   Disse(noSeco, "só dá pra velejar na água") && Disse(zarpou, "zarpa") && velejando && passoIgual && naoPaga
						   && Disse(naAgua, "em cima da água") && Disse(atracou, "atraca") && !p.Nadando && !Velejando(p),
						   $"{string.Join(" | ", noSeco)} | velejando {velejando}; passo igual {passoIgual}; nao paga {naoPaga} | {string.Join(" | ", naAgua)} | {string.Join(" | ", atracou)}");
				AfirmarV21("(defeito injetado: velejar cobra como nadar) o mesmo barco, e o segundo na agua gasta Ki e anda no passo lento do nado -- ele nao serviria pra nada",
						   comDefeitoPaga && comDefeitoLento, $"paga {comDefeitoPaga}; lento {comDefeitoLento}");
			}
		}
		finally
		{
			Simulador.SemTetoDeTeste = Recriador.DeGracaDeTeste = MaquinaDeClonagem.NuncaReviveDeTeste = Barco.CobraComoNadoDeTeste = false;
			ClonagemSemPeerDeTeste = false;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			foreach (int id in _simulacoes.Keys.ToList()) DesfazerSimulacao(id, null);
			foreach (Obra o in obras) _noChao.Remove(o);
			_velejando.Clear();
			if (_bases.TryGetValue(p.Zone.Hash, out BaseDaZona? zonaDaBancada))
				foreach (BlocoDePe b in zonaDaBancada.Celulas.Values.Where(b => b.DonoConta == p.Conta).ToList()) DerrubarBloco(b, p);
			LargadosDe(p.Zone.Hash).Clear();
			_cofreDeTech = cofreDeAntes;
			_cofreSujo = false;
			GravarOCofreDeTech();
			AplicarColisaoDasObras(p.Zone);
			GravarMundo();
			DesfazerV2(p, vizinho);
			LimparTudoDaBancada();
		}
		ATechDeQuemVirouBioV21();   // (2026-10-25) o pedido 74 -- logo abaixo
	}

	// =====================================================================
	// X4b) (2026-10-25) A TECH DE QUEM VIROU BIO, E A TECH LIBERADA -- o pedido 74 do dono
	// =====================================================================
	/// <summary>
	/// O dono: *"Faca q personagens q viraram neotuffle ou bio, consigam usar os itens tech do personagem anterior antes dele ser substituido,
	/// e permita com q o criador da tech libere ela quando quiser pra qualquer um mexer nela, desparafusar, pegar, etc, tudo q o dono pode
	/// fazer"* -- e o caso que o trouxe: *"meu amigo q virou bio n tava conseguindo usar o simulator dps q virou bio androide (simulator ele fez
	/// quando ainda era humano/androide)"*. O bio nasce pelo parto de PRODUCAO (`NascerBioAndroide`); o Neo Tuffle dentro do hospede e provado
	/// na `--tuffleteste` (a posse de producao mora la).
	/// </summary>
	private void ATechDeQuemVirouBioV21()
	{
		GD.Print("[v021] -- X4b) A TECH DE QUEM VIROU BIO, E A TECH LIBERADA (o pedido 74, 2026-10-25)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		const float T = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(10);
		var obras = new List<Obra>();
		var naves = new List<Nave>();
		ServerPlayer cientista = ForjarV2("Cientista", c, 5_000);
		cientista.Conta = "bancada_v021_cientista";
		ServerPlayer estranho = ForjarV2("Estranho", c + new Vec2(0, T), 500);
		estranho.Conta = "bancada_v021_estranho";

		List<string> Usar(ServerPlayer quem, string verbo, string arg = "") => Ouvir(() => ComandoDeInteracao(quem, verbo, arg));
		List<string> Tech(ServerPlayer quem, string cmd, string arg = "") => Ouvir(() => ComandoDeTech(quem, cmd, arg));
		bool Disse(List<string> falas, string trecho) => falas.Exists(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));
		ServerPlayer? ASimulacao() => _simulacoes.Keys.Select(id => _players.GetValueOrDefault(id)).FirstOrDefault(s => s != null);
		void SemSimulacao() { foreach (int id in _simulacoes.Keys.ToList()) DesfazerSimulacao(id, null); }
		bool Larva(ServerPlayer quem) => Jandirus.Core.Races.BioAndroids.EmLarva(quem.Ficha);
		// UMA OBRA DE CADA VEZ, um tile a leste do meio: a mais perto de quem esta no meio e sempre ela (o `ObraPerto` do parafuso e do pegar)
		Obra Por(string tipo, ServerPlayer dono)
		{
			Obra o = ObraDeBancadaV21(tipo, c + new Vec2(T, 0), dono);
			obras.Add(o);
			return o;
		}

		try
		{
			// ---------------------------------------------------------------- 1. o simulador do humano, e o parto de producao
			Obra simulador = Por(Simulador.Tipo, cientista);
			Obra lab = ObraDeBancadaV21("Android_Creation_Mainframe", c + new Vec2(6 * T, 0), cientista);
			obras.Add(lab);
			lab.Lab = 2;
			var fornada = new Gestacao
			{
				DonoConta = cientista.Conta, MaiorBp = 40_000, PrometidaEm = NowMs(),
				Amostras = { new Amostra { Raca = "Human", Doador = "Doador da Bancada", Bp = 40_000 } },
			};
			lab.Fornada = fornada;
			string antes = cientista.Name;
			NascerBioAndroide(cientista, lab, fornada);
			ServerPlayer bio = cientista;
			AfirmarV21("(montagem) o Cientista vira o bio pelo parto de PRODUCAO (`NascerBioAndroide`): o mesmo corpo e a mesma conta, outro nome -- e LARVA",
					   bio.Name == $"Bio-Androide de {antes}" && Larva(bio) && bio.Conta == "bancada_v021_cientista", $"{bio.Name}; larva {Larva(bio)}");

			// ---------------------------------------------------------------- 2. a larva liga o simulador que fez quando era humano
			bio.Pos = c;
			List<string> pediu = Usar(bio, Simulador.VerboIniciar);
			ServerPlayer? sim = ASimulacao();
			bool caca = sim != null && GuiarASimulacao(sim, bio.Id, out ServerPlayer? presa, out _) && presa == bio;
			bio.Ficha.PowerLevel();
			sim?.Ficha.PowerLevel();
			double dele = bio.Ficha.expressedBP, dela = sim?.Ficha.expressedBP ?? -1;
			AfirmarV21("**O BIO USA O SIMULADOR QUE FEZ QUANDO ERA HUMANO** (o amigo do dono): a larva liga a maquina e a simulacao nasce cacando ela -- e a "
					   + "COPIA DE UMA LARVA E UMA LARVA, selada como ela: o poder expresso da copia nao passa o dela (era o poder base inteiro, ate o teto "
					   + "da maquina, contra os 5 da carapaca -- o primeiro golpe nocauteava o bio e desfazia a simulacao)",
					   sim != null && caca && Larva(sim) && dela <= dele + 1e-6,
					   $"{Ultimos(pediu)} | {(sim == null ? "nenhuma simulacao" : $"{sim.Name}: expresso {dela:N0}, larva {Larva(sim)}")}; o bio expressa {dele:N0}");
			SemSimulacao();
			SimulacaoSemOSeloDaLarvaDeTeste = true;
			try
			{
				Usar(bio, Simulador.VerboIniciar);
				ServerPlayer? semSelo = ASimulacao();
				semSelo?.Ficha.PowerLevel();
				AfirmarV21("(defeito injetado: a copia sem o selo da larva, o jogo de antes) a mesma larva liga a mesma maquina e a copia nasce com o poder "
						   + "base dela, dezenas de vezes os 5 da carapaca -- a regua de cima REPROVA",
						   semSelo != null && semSelo.Ficha.expressedBP > dele * 10, $"copia {semSelo?.Ficha.expressedBP:N0}, o bio {dele:N0}");
			}
			finally { SimulacaoSemOSeloDaLarvaDeTeste = false; SemSimulacao(); }

			// ---------------------------------------------------------------- 3. maduro; o nome nas obras; o que e dele, ele solta e recolhe
			AmadurecerALarva(bio);
			List<string> maduro = Usar(bio, Simulador.VerboIniciar);
			ServerPlayer? simMaduro = ASimulacao();
			bool cacaMaduro = simMaduro != null && GuiarASimulacao(simMaduro, bio.Id, out ServerPlayer? presaMaduro, out _) && presaMaduro == bio;
			AfirmarV21("...e MADURO tambem: a simulacao do bio nasce e caca ele, sem selo nenhum (a copia de quem nao e larva e a de sempre)",
					   !Larva(bio) && cacaMaduro && !Larva(simMaduro!), Ultimos(maduro));
			SemSimulacao();
			AfirmarV21("**A TECH DO HUMANO PASSA PRO NOME DO BIO**: o simulador que o Cientista ergueu diz que e do Bio-Androide de Cientista -- a aba Tech "
					   + "(\"Suas construções\") lista pelo nome, e o deixava de fora",
					   simulador.DonoNome == bio.Name, simulador.DonoNome);
			List<string> soltou = Tech(bio, "aparafusar");
			List<string> recolheu = Tech(bio, "pegar");
			AfirmarV21("...e o bio SOLTA e RECOLHE o simulador do humano que ele era: a obra e da CONTA, e o bio e o mesmo save",
					   !_noChao.Contains(simulador) && !Disse(soltou, "nao e seu") && !Disse(recolheu, " é de "), $"{Ultimos(soltou)} | {Ultimos(recolheu)}");

			// ---------------------------------------------------------------- 4. LIBERAR PRA TODOS: o parafuso e o pegar
			Obra maquina = Por(Simulador.Tipo, bio);
			estranho.Pos = c;
			List<string> trancada = Tech(estranho, "aparafusar");
			List<string> estranhoLibera = Tech(estranho, "liberar", maquina.Id.ToString());
			List<string> liberou = Tech(bio, "liberar", maquina.Id.ToString());
			bool ficouLiberada = maquina.Liberada;
			List<string> comDefeito;
			LiberadaNaoValeDeTeste = true;
			try { comDefeito = Tech(estranho, "aparafusar"); }
			finally { LiberadaNaoValeDeTeste = false; }
			bool defeitoRecusou = maquina.Aparafusada && Disse(comDefeito, "nao e seu");
			List<string> estranhoSolta = Tech(estranho, "aparafusar");
			bool soltaPeloEstranho = !maquina.Aparafusada;
			List<string> estranhoPega = Tech(estranho, "pegar");
			AfirmarV21("**O DONO LIBERA A TECH PRA TODOS** (o dono: 'permita com q o criador da tech libere ela quando quiser pra qualquer um mexer nela, "
					   + "desparafusar, pegar, etc'): trancada, o estranho nao a solta nem a libera; o dono a libera pelo botao da aba Tech (`liberar` com o "
					   + "id) e o estranho a SOLTA e a RECOLHE pra mochila dele",
					   Disse(trancada, "nao e seu") && Disse(estranhoLibera, "só o dono libera") && !Disse(estranhoLibera, "está liberada")
					   && ficouLiberada && Disse(liberou, "está liberada") && soltaPeloEstranho && !_noChao.Contains(maquina),
					   $"{Ultimos(trancada)} | {Ultimos(estranhoLibera)} | {Ultimos(liberou)} | solta {soltaPeloEstranho} | {Ultimos(estranhoSolta)} | {Ultimos(estranhoPega)}");
			AfirmarV21("(defeito injetado: a liberada continua so do dono, o jogo de antes) com a maquina liberada o estranho ainda ouve 'isso nao e seu' "
					   + "-- a regua de cima REPROVA", defeitoRecusou, Ultimos(comDefeito));

			// ---------------------------------------------------------------- 5. as outras portas do mesmo funil, e o TRANCAR de volta
			Obra drill = Por(Drill.Tipo, bio);
			List<string> drillDeOutro = Usar(estranho, Drill.VerboColetar);
			Tech(bio, "liberar", drill.Id.ToString());
			List<string> drillLiberada = Usar(estranho, Drill.VerboColetar);
			List<string> trancou = Tech(bio, "liberar", drill.Id.ToString());
			List<string> drillTrancada = Usar(estranho, Drill.VerboColetar);
			_noChao.Remove(drill);
			Obra acampamento = Por(Deposito.Base, bio);
			List<string> baseDeOutro = Usar(estranho, Deposito.VerboDesmontar);
			Tech(bio, "liberar", acampamento.Id.ToString());
			List<string> baseLiberada = Usar(estranho, Deposito.VerboDesmontar);
			AfirmarV21("...a MESMA chave nas outras portas: a Drill de outro so o dono coleta, a liberada qualquer um, e TRANCADA de volta ('volta a ser "
					   + "só sua') de novo so o dono; a base de outro so o dono desmonta, a liberada qualquer um",
					   Disse(drillDeOutro, "só o dono coleta") && !Disse(drillLiberada, "só o dono") && Disse(trancou, "volta a ser só sua")
					   && Disse(drillTrancada, "só o dono coleta") && Disse(baseDeOutro, "só o dono desmonta") && !_noChao.Contains(acampamento),
					   $"{Ultimos(drillDeOutro)} | {Ultimos(drillLiberada)} | {Ultimos(trancou)} | {Ultimos(drillTrancada)} | {Ultimos(baseDeOutro)} | {Ultimos(baseLiberada)}");

			// ---------------------------------------------------------------- 6. a nave parada (o id negativo do pacote), e a trava do dono
			var pod = new Nave { Id = _proximaNaveId++, Tipo = "Spacepod", X = c.X + T, Y = c.Y, DonoConta = bio.Conta, DonoNome = bio.Name };
			pod.PorZona(bio.Zone);
			_naves.Add(pod);
			naves.Add(pod);
			List<string> podDeOutro = Usar(estranho, "nave_pegar");
			List<string> podLibera = Tech(bio, "liberar", (-pod.Id).ToString());
			List<string> podLiberado = Usar(estranho, "nave_pegar");
			Obra clonagem = Por(MaquinaDeClonagem.Tipo, bio);
			Tech(bio, "liberar", clonagem.Id.ToString());
			List<string> senhaDeOutro = Usar(estranho, MaquinaDeClonagem.VerboSenha, "1234");
			_noChao.Remove(clonagem);
			AfirmarV21("...a NAVE parada tambem (o botao manda o id negativo): a de outro nao se recolhe, a liberada sim -- e a SENHA da clonagem, trava "
					   + "permanente, continua so do dono de verdade com a maquina liberada (senao um estranho a trancaria pra sempre)",
					   Disse(podDeOutro, " é de ") && pod.Liberada && !_naves.Contains(pod) && Disse(senhaDeOutro, "só o dono põe senha"),
					   $"{Ultimos(podDeOutro)} | {Ultimos(podLibera)} | {Ultimos(podLiberado)} | {Ultimos(senhaDeOutro)}");

			// ---------------------------------------------------------------- 7. o ingles do que a tech liberada diz e mostra
			var ingles = Jandirus.Core.Lingua.Tradutor.DoJson(Godot.FileAccess.GetFileAsString("res://Assets/Data/lingua-en.json"));
			string[] novas =
			[
				FraseDaObraLonge, "só o dono libera Simulator.", "Simulator volta a ser só sua.",
				"Simulator está liberada: qualquer um mexe nela como você -- aparafusa, solta, pega, usa.",
				"Spacepod está liberada: qualquer um mexe nela como você -- recolhe, melhora, embarca.",
				"Liberar", "Trancar", "liberada",
				"libera esta construção pra qualquer um mexer nela como você: aparafusar, soltar, pegar, usar",
				"volta a ser só sua: ninguém mais mexe nela",
			];
			string[] semIngles = [.. novas.Where(s => ingles.Traduzir(s) == s)];
			AfirmarV21("...e o que a tech liberada diz e mostra tem ingles no dicionario do jogo", semIngles.Length == 0, string.Join(" | ", semIngles));
		}
		finally
		{
			SimulacaoSemOSeloDaLarvaDeTeste = LiberadaNaoValeDeTeste = false;
			SemSimulacao();
			foreach (Obra o in obras) _noChao.Remove(o);
			foreach (Nave n in naves) _naves.Remove(n);
			AplicarColisaoDasObras(cientista.Zone);
			GravarMundo();
			GravarNaves();
			DesfazerV2(cientista, estranho);
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// X5) O TELEPORTE E A VIGILANCIA -- o lote T4
	// =====================================================================
	private void OTeleporteEAVigilanciaV21()
	{
		GD.Print("[v021] -- X5) A TECH PORTADA, lote 4: o telepad, o relogio de teleporte, o anulador, as cameras, o PDA e o comunicador (item 13)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		const float T = ZoneCollision.TileSize;
		// (o corredor de terra firme vai de um tile a oeste ate dez a leste de `c`, com uma linha acima e uma abaixo:
		// tudo o que se ASSENTA pelo canal de producao fica dentro dele)
		Vec2 c = TerraFirmeG12(10);
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		CofreDeTech cofreDeAntes = _cofreDeTech;
		_cofreDeTech = new CofreDeTech();
		List<string>? sagasDeAntes = EscutaDasSagas;
		var obras = new List<Obra>();
		ServerPlayer p = ForjarV2("Viajante", c, 500);
		p.Conta = "bancada_v021_viajante";
		ServerPlayer amigo = ForjarV2("Amigo", c + new Vec2(4 * T, T), 500);
		amigo.Conta = "bancada_v021_amigo";
		ServerPlayer? comScouter = null;
		p.Ficha.Zeni = p.Ficha.Recursos = 1_000_000;
		RelogioSemPeerDeTeste = true;

		List<string> Fazer(ServerPlayer quem, string acao, string arg) => Ouvir(() => ComandoDeItem(quem, "item_" + acao, arg));
		List<string> Usar(ServerPlayer quem, string verbo, string arg = "") => Ouvir(() => ComandoDeInteracao(quem, verbo, arg));
		bool Disse(List<string> falas, string trecho) => falas.Exists(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));
		string Item(ServerPlayer quem, string @base) => quem.Mochila.Pilhas.Find(x => TechDeBolso.Base(x.Id) == @base).Id ?? "";
		Obra Por(string tipo, Vec2 onde)
		{
			Obra o = ObraDeBancadaV21(tipo, onde, p);
			obras.Add(o);
			return o;
		}
		void Viajar() { AdiantoDoRelogioDeTeste += (long)(Teleporte.SegundosDeViagem * 1000) + 100; TickDosRelogios(); }

		try
		{
			// ---------------------------------------------------------------- o telepad e o relogio
			Vec2 noPad = c + new Vec2(8 * T, 0);
			Obra pad = Por(Teleporte.Telepad, noPad);
			p.Pos = noPad + new Vec2(-T, 0);
			Usar(p, Teleporte.VerboCanalDoTelepad, "kame");
			double tetoDoP = TetoDeTecnologia.De(p.Ficha.relBPmax, p.Ficha.techskill, p.Ficha.techmod);
			Usar(p, Teleporte.VerboReforcarOTelepad);
			Obra padDeOutroCanal = Por(Teleporte.Telepad, c + new Vec2(8 * T, -T));
			(padDeOutroCanal.Tech ??= new EstadoDeTech()).S[Teleporte.ChaveCanal] = "outro";
			p.Pos = c;
			p.Mochila.Guardar(Teleporte.Relogio);
			List<string> semCanal = Fazer(p, Teleporte.AcaoDestinos, Teleporte.Relogio);
			Fazer(p, Teleporte.AcaoCanal, Teleporte.Relogio + "/kame");
			string relogio = Item(p, Teleporte.Relogio);
			List<string> lista = Fazer(p, Teleporte.AcaoDestinos, relogio);
			Fazer(p, Teleporte.AcaoIr, relogio + "/1");
			bool aindaAqui = Vec2.Distance(p.Pos, c) < 1;
			Viajar();
			float doPad = Vec2.Distance(p.Pos, noPad);
			AfirmarV21("**O RELOGIO TELEPORTA**: com um canal, ele lista os destinos do MESMO canal (o telepad aparafusado \"kame\", e nao o de outro canal) e leva quem o usa pra la DOIS segundos depois (`sleep(20)`) -- e o telepad se reforca ate o teto da tecnologia de quem clica",
					   Disse(semCanal, "não dá pra") && pad.Tech?.Txt(Teleporte.ChaveCanal) == "kame" && tetoDoP > 1 && Math.Abs(pad.ArmaduraMax - tetoDoP) < 1e-6
					   && lista.Count(f => f.Contains("telepad")) == 1 && Disse(lista, "\"kame\"") && aindaAqui && doPad < 1.5f * T,
					   $"{string.Join(" | ", semCanal)} || {string.Join(" | ", lista)}; antes {aindaAqui}; depois a {doPad / T:0.#} tiles do telepad; armadura {pad.ArmaduraMax:0} (teto {tetoDoP:0})");

			pad.Aparafusada = false;
			bool soltoNaoEDestino = DestinosDoRelogio(p, "kame").Count == 0;
			pad.Aparafusada = true;
			Teleporte.RelogioIgnoraOCanalDeTeste = true;
			int comDefeito;
			try { comDefeito = DestinosDoRelogio(p, "kame").Count; }
			finally { Teleporte.RelogioIgnoraOCanalDeTeste = false; }
			amigo.Mochila.Guardar(Teleporte.Escrever("kame", ""));
			int comOAmigo = DestinosDoRelogio(p, "kame").Count;
			Fazer(p, Teleporte.AcaoIr, relogio + "/2");
			// (o destino e QUEM carrega o relogio, e ele anda: a chegada o procura de novo)
			amigo.Pos = c + new Vec2(2 * T, T);
			Viajar();
			float doAmigo = Vec2.Distance(p.Pos, amigo.Pos);
			AfirmarV21("...o telepad SOLTO nao e destino (`nI.Bolted`); o OUTRO RELOGIO do canal e, e a viagem chega em quem o carrega ONDE ELE ESTIVER dois segundos depois (o `choice.loc` so se le na chegada); (defeito injetado: o relogio alcanca qualquer canal) e com o defeito o telepad de outro canal entra na lista",
					   soltoNaoEDestino && comDefeito == 2 && comOAmigo == 2 && doAmigo <= 1.5f * T,
					   $"solto {soltoNaoEDestino}; com defeito {comDefeito}; com o amigo {comOAmigo}; a {doAmigo / T:0.#} tiles dele");

			// ---------------------------------------------------------------- o anulador, e quem nao viaja
			AfirmarV21("a duracao do ANULADOR (o do original se desliga no tique seguinte: `telenuldur` nunca e escrito): um minuto, mais dez segundos por degrau de tecnologia, ate dois minutos",
					   Math.Abs(Teleporte.SegundosDoAnulador(1) - 60) < 1e-6 && Math.Abs(Teleporte.SegundosDoAnulador(8) - 70) < 1e-6
					   && Math.Abs(Teleporte.SegundosDoAnulador(1e9) - 120) < 1e-6);
			p.Mochila.Guardar(Teleporte.Anulador);
			Fazer(p, Teleporte.AcaoAnular, Teleporte.Anulador);
			List<string> anulado = Fazer(p, Teleporte.AcaoIr, relogio + "/1");
			bool barrou = _viagensDoRelogio.Count == 0 && Disse(anulado, "anulador");
			Teleporte.AnuladorNaoAnulaDeTeste = true;
			bool defeitoDeixa;
			try { Fazer(p, Teleporte.AcaoIr, relogio + "/1"); defeitoDeixa = _viagensDoRelogio.Count == 1; }
			finally { Teleporte.AnuladorNaoAnulaDeTeste = false; _viagensDoRelogio.Clear(); }
			Fazer(p, Teleporte.AcaoAnular, Teleporte.Anulador);
			Fazer(p, Teleporte.AcaoIr, relogio + "/1");
			bool desligadoDeixa = _viagensDoRelogio.Count == 1;
			_viagensDoRelogio.Clear();
			p.AgarradoPorId = amigo.Id;
			List<string> agarrado = Fazer(p, Teleporte.AcaoIr, relogio + "/1");
			p.AgarradoPorId = 0;
			bool agarradoFica = _viagensDoRelogio.Count == 0 && Disse(agarrado, "agarrão");
			AfirmarV21("**O ANULADOR LIGADO BARRA O TELEPORTE** dos relogios no lugar (`\"Teleport failed!\"`) -- o de quem o ligou tambem --, e desligado deixa; quem esta preso num agarrao nao viaja (`usr.grabParalysis`); (defeito injetado: o anulador nao anula) e com o defeito a viagem sai com ele ligado",
					   barrou && defeitoDeixa && desligadoDeixa && agarradoFica,
					   $"barrou {barrou}; com defeito {defeitoDeixa}; desligado {desligadoDeixa}; agarrado fica {agarradoFica} | {string.Join(" | ", anulado)} | {string.Join(" | ", agarrado)}");

			// ---------------------------------------------------------------- a fala, o capacete e a trava de DNA
			List<string> noCanal = Fazer(p, Teleporte.AcaoFalar, relogio + "/alo, canal");
			Fazer(p, Teleporte.AcaoHenshin, relogio);
			bool deCapacete = DeCapacete(p);
			Fazer(p, Teleporte.AcaoHenshin, relogio);
			Fazer(p, Teleporte.AcaoDna, relogio);
			string travado = Item(p, Teleporte.Relogio);
			amigo.Mochila.Pilhas.Clear();
			amigo.Mochila.Guardar(travado);
			List<string> naMaoErrada = Fazer(amigo, Teleporte.AcaoDestinos, travado);
			AfirmarV21("o relogio FALA no canal (`Speak`: ouve quem fala e quem carrega outro relogio do canal), o \"Henshin!\" veste e tira o capacete, e a TRAVA DE DNA faz o relogio EXPLODIR na mao de quem nao e o dono (`\"[src] explodes!\"`)",
					   noCanal.Count(f => f.Contains("(Relógio)") && f.Contains("alo, canal")) == 2 && deCapacete && !DeCapacete(p)
					   && Teleporte.Dna(travado) == Teleporte.Marca(p.Conta) && amigo.Mochila.Pilhas.Count == 0 && Disse(naMaoErrada, "explode"),
					   $"falas {noCanal.Count}; capacete {deCapacete}; id {travado}; mochila do amigo {amigo.Mochila.Pilhas.Count} | {string.Join(" | ", naMaoErrada)}");

			// ---------------------------------------------------------------- a camera e o computador de cameras
			p.Pos = c;
			amigo.Pos = c + new Vec2(4 * T, T);
			amigo.Ficha.train = true;
			p.Mochila.Pilhas.Clear();
			p.Mochila.Guardar(Vigilancia.Camera);
			Vec2 naCamera = c + new Vec2(2 * T, -T);
			List<string> aoAssentar = Ouvir(() => Posicionar(p, $"{Vigilancia.Camera}/{naCamera.X:0}/{naCamera.Y:0}"));
			Obra? camera = _noChao.FirstOrDefault(o => o.Tipo == Vigilancia.Camera && o.DonoConta == p.Conta);
			if (camera != null) obras.Add(camera);
			p.Pos = naCamera + new Vec2(0, T);
			Usar(p, Vigilancia.VerboFrequencia, "77");
			p.Mochila.Guardar(Vigilancia.Computador);
			Fazer(p, Vigilancia.AcaoFrequencia, Vigilancia.Computador + "/77");
			List<string> solta = Fazer(p, Vigilancia.AcaoVer, Item(p, Vigilancia.Computador));
			Usar(p, Interacoes.VerboDoParafuso);
			p.Pos = c + new Vec2(40 * T, 0);
			List<string> relato = Fazer(p, Vigilancia.AcaoVer, Item(p, Vigilancia.Computador));
			Fazer(p, Vigilancia.AcaoFrequencia, Item(p, Vigilancia.Computador) + "/12");
			List<string> outraFrequencia = Fazer(p, Vigilancia.AcaoVer, Item(p, Vigilancia.Computador));
			Vigilancia.CameraIgnoraAFrequenciaDeTeste = true;
			List<string> comODefeito;
			try { comODefeito = Fazer(p, Vigilancia.AcaoVer, Item(p, Vigilancia.Computador)); }
			finally { Vigilancia.CameraIgnoraAFrequenciaDeTeste = false; }
			string poderDoAmigo = amigo.Ficha.expressedBP.ToString("N0");
			AfirmarV21("**A CAMERA VIGIA**: assentada pela mochila (\"Instalar no chão\"), aparafusada e com uma frequencia, o computador de cameras da MESMA frequencia recebe, de longe, o relato do que ela ve -- quem esta a vista e o que faz (o Amigo, treinando) -- e NENHUM numero de poder; solta ela nao transmite, e de outra frequencia nao responde; (defeito injetado: a camera responde a qualquer frequencia) e com o defeito responde",
					   camera is { Aparafusada: true } && camera.Tech?.Txt(Vigilancia.ChaveFrequencia) == "77" && p.Mochila.Quantos(Vigilancia.Camera) == 0
					   && Disse(solta, "nenhuma câmera responde") && Disse(outraFrequencia, "nenhuma câmera responde") && Disse(comODefeito, "Amigo")
					   && Disse(relato, "Amigo (treinando)") && !relato.Exists(f => f.Contains(poderDoAmigo) || f.Contains("BP")),
					   $"camera {(camera == null ? "nao assentou: " + string.Join(" | ", aoAssentar) : camera.Tech?.Txt(Vigilancia.ChaveFrequencia) + (camera.Aparafusada ? ", aparafusada" : ", SOLTA"))} | {string.Join(" | ", solta)} | {string.Join(" | ", relato)} | {string.Join(" | ", outraFrequencia)}");

			// ---------------------------------------------------------------- o PDA: as frequencias, a tranca e a biblioteca
			p.Mochila.Pilhas.Clear();
			p.Mochila.Guardar(TechDeBolso.Pda);
			p.Mochila.Guardar(TechDeBolso.Livro);
			Fazer(p, Vigilancia.AcaoMaisFrequencia, TechDeBolso.Pda + "/77");
			Fazer(p, TechDeBolso.AcaoEscrever, Item(p, TechDeBolso.Pda) + "/nota do pda");
			string pda = Item(p, TechDeBolso.Pda);
			List<string> peloPda = Fazer(p, Vigilancia.AcaoVer, pda);
			Fazer(p, TechDeBolso.AcaoEscrever, TechDeBolso.Livro + "/A historia de um viajante");
			string livro = Item(p, TechDeBolso.Livro);
			List<string> semNumero = Fazer(p, Vigilancia.AcaoPublicar, pda + "/0");
			Fazer(p, Vigilancia.AcaoPublicar, pda + "/" + TechDeBolso.Campo(livro, 0));
			List<string> estante = Fazer(p, Vigilancia.AcaoBiblioteca, pda);
			Fazer(p, Vigilancia.AcaoImprimir, pda + "/1");
			int livros = p.Mochila.Pilhas.Count(x => TechDeBolso.Base(x.Id) == TechDeBolso.Livro);
			bool copiaTemOTexto = p.Mochila.Pilhas.Where(x => TechDeBolso.Base(x.Id) == TechDeBolso.Livro)
								   .All(x => _cofreDeTech.Textos.GetValueOrDefault(NumeroDoCofre(x.Id)) == "A historia de um viajante");
			Fazer(p, Vigilancia.AcaoDna, pda);
			string pdaTravado = Item(p, TechDeBolso.Pda);
			amigo.Mochila.Pilhas.Clear();
			amigo.Mochila.Guardar(pdaTravado);
			List<string> assinatura = Fazer(amigo, Vigilancia.AcaoVer, pdaTravado);
			amigo.Mochila.Pilhas.Clear();
			AfirmarV21("o PDA guarda a nota, as FREQUENCIAS e a tranca no id (escrever a nota nao apaga a frequencia), consulta as cameras delas, PUBLICA na biblioteca global o livro escolhido pelo numero e IMPRIME uma copia de la; e a TRAVA DE DNA recusa quem nao e o dono (`\"Wrong signature!\"` -- a do original, `if(!DNA == usr.signature)`, nunca recusa)",
					   Vigilancia.FrequenciasDoPda(pda).SequenceEqual(["77"]) && TechDeBolso.Campo(pda, 0).Length > 0 && Disse(peloPda, "Amigo")
					   && Disse(semNumero, "qual livro") && _cofreDeTech.Biblioteca.Count == 1 && Disse(estante, "A historia de um viajante")
					   && livros == 2 && copiaTemOTexto && Vigilancia.DnaDoPda(pdaTravado) == Teleporte.Marca(p.Conta) && Disse(assinatura, "assinatura errada"),
					   $"pda {pda} -> {pdaTravado}; biblioteca {_cofreDeTech.Biblioteca.Count}; livros {livros}; copia igual {copiaTemOTexto} | {string.Join(" | ", semNumero)} | {string.Join(" | ", assinatura)}");
			amigo.Ficha.train = false;

			// ---------------------------------------------------------------- o comunicador
			p.Pos = c + new Vec2(5 * T, 0);
			p.Mochila.Pilhas.Clear();
			p.Mochila.Guardar(Vigilancia.Comunicador);
			Vec2 noConsole = c + new Vec2(5 * T, -T);
			List<string> aoAssentarOConsole = Ouvir(() => Posicionar(p, $"{Vigilancia.Comunicador}/{noConsole.X:0}/{noConsole.Y:0}"));
			Obra? console = _noChao.FirstOrDefault(o => o.Tipo == Vigilancia.Comunicador && o.DonoConta == p.Conta);
			if (console != null) obras.Add(console);
			Obra outroConsole = Por(Vigilancia.Comunicador, c + new Vec2(9 * T, T));
			EstadoDeTech doOutro = outroConsole.Tech ??= new EstadoDeTech();
			doOutro.S[Vigilancia.ChaveFrequencias] = "kame";
			comScouter = ForjarV2("Patrulheiro", c + new Vec2(T, T), 500);
			comScouter.Poderes |= Protocol.Poder.Scouter;
			string doScouter = FrequenciaDe(comScouter).ToString();
			amigo.Mochila.Guardar(Teleporte.Escrever("kame", ""));
			List<string> semFrequencia = Usar(p, Vigilancia.VerboEnviar, "oi");
			Usar(p, Vigilancia.VerboMaisFrequencia, "kame");
			Usar(p, Vigilancia.VerboMaisFrequencia, doScouter);
			List<string> repetida = Usar(p, Vigilancia.VerboMaisFrequencia, "kame");
			List<string> envio = Usar(p, Vigilancia.VerboEnviar, "alguem na escuta?");
			int ouviram = envio.Count(f => f.Contains("(Comunicações)") && f.Contains("alguem na escuta?"));
			bool noHistorico = doOutro.Txt(Vigilancia.ChaveHistorico).Contains("alguem na escuta?");
			Vigilancia.EnvioParaNoPrimeiroDeTeste = true;
			int comOReturn;
			bool historicoParado;
			try
			{
				comOReturn = Usar(p, Vigilancia.VerboEnviar, "segunda").Count(f => f.Contains("(Comunicações)") && f.Contains("segunda"));
				historicoParado = !doOutro.Txt(Vigilancia.ChaveHistorico).Contains("segunda");
			}
			finally { Vigilancia.EnvioParaNoPrimeiroDeTeste = false; }
			doOutro.N[Vigilancia.ChaveDifusor] = 1;
			int comDifusor = Usar(p, Vigilancia.VerboEnviar, "terceira").Count(f => f.Contains("(Comunicações)") && f.Contains("terceira"));
			AfirmarV21("**O COMUNICADOR ENVIA**: assentado pela mochila, com as frequencias dele (ate oito, sem repetir), a frase chega a quem carrega um relogio do canal, a quem esta de scouter na frequencia e ao OUTRO comunicador que a escuta (que a guarda no historico, e com o DIFUSOR a diz em voz alta a quem esta perto) -- a TODOS; (defeito injetado: o envio para no primeiro) e com o `return` do original chega a um so",
					   console != null && Disse(semFrequencia, "frequência nenhuma") && Disse(repetida, "já escuta") && ouviram == 2 && noHistorico
					   && comOReturn == 1 && historicoParado && comDifusor > 2,
					   $"console {(console == null ? "nao assentou: " + string.Join(" | ", aoAssentarOConsole) : "ok")}; ouviram {ouviram}; no historico {noHistorico}; com o return {comOReturn} (historico parado {historicoParado}); com o difusor {comDifusor} | {string.Join(" | ", envio)}");

			List<string> semMegafone = Usar(p, Vigilancia.VerboMegafone, "ola, universo");
			p.Ficha.techskill = 10;
			List<string> semTecnologia = Usar(p, Vigilancia.VerboMelhorar, Vigilancia.Megafone);
			p.Ficha.techskill = 60;
			double zeniDoConsole = p.Ficha.Zeni, recursosDoConsole = p.Ficha.Recursos;
			Usar(p, Vigilancia.VerboMelhorar, Vigilancia.Megafone);
			Usar(p, Vigilancia.VerboMelhorar, Jandirus.Core.Economia.Recurso.PedidoPago(Vigilancia.OlhoDeCamera, Jandirus.Core.Economia.Moeda.Recursos));
			double pagouEmZeni = zeniDoConsole - p.Ficha.Zeni, pagouEmRecursos = recursosDoConsole - p.Ficha.Recursos;
			EscutaDasSagas = [];
			Usar(p, Vigilancia.VerboMegafone, "ola, universo");
			bool oMundoOuviu = EscutaDasSagas.Exists(f => f.Contains("(Megafone)") && f.Contains("ola, universo"));
			List<string> esfriando = Usar(p, Vigilancia.VerboMegafone, "de novo");
			AdiantoDoRelogioDeTeste += 61_000;
			Usar(p, Vigilancia.VerboMegafone, "terceira vez");
			double segundaEspera = (console?.Tech?.Num(Vigilancia.ChaveMegaAte) ?? 0) - NowMs();
			EscutaDasSagas = sagasDeAntes;
			Usar(p, Vigilancia.VerboMaisFrequencia, "77");
			List<string> pelasCameras = Usar(p, Vigilancia.VerboCameras);
			Usar(p, Vigilancia.VerboDna);
			amigo.Pos = p.Pos;
			List<string> historicoDoAmigo = Usar(amigo, Vigilancia.VerboHistorico);
			List<string> historico = Usar(p, Vigilancia.VerboHistorico);
			AfirmarV21("as MELHORIAS do comunicador pedem tecnologia (megafone 35, camera 45) e aceitam zeni ou recursos; o MEGAFONE fala com o mundo inteiro e esfria um minuto a mais a cada uso (`megaphonecooldown = 600 * cooldownstack`: a segunda espera e de dois); com a camera ele consulta as cameras das frequencias dele; o historico guarda o que passou por ele, e a TRANCA DE DNA so o abre pra quem trancou",
					   Disse(semMegafone, "precisa da melhoria") && Disse(semTecnologia, "35 de tecnologia")
					   && Math.Abs(pagouEmZeni - Vigilancia.CustoDoMegafone) < 0.5 && Math.Abs(pagouEmRecursos - 2 * Vigilancia.CustoDoOlho) < 0.5
					   && oMundoOuviu && Disse(esfriando, "ainda esfria") && segundaEspera is > 110_000 and <= 120_000
					   && Disse(pelasCameras, "Amigo") && Disse(historicoDoAmigo, "assinatura errada") && Disse(historico, "alguem na escuta?"),
					   $"{string.Join(" | ", semMegafone)} | {string.Join(" | ", semTecnologia)} | zeni {pagouEmZeni:0}; recursos {pagouEmRecursos:0}; o mundo ouviu {oMundoOuviu}; 2a espera {segundaEspera / 1000:0} s | {string.Join(" | ", pelasCameras)} | {string.Join(" | ", historicoDoAmigo)} | {string.Join(" | ", historico)}");
		}
		finally
		{
			Teleporte.RelogioIgnoraOCanalDeTeste = Teleporte.AnuladorNaoAnulaDeTeste = false;
			Vigilancia.CameraIgnoraAFrequenciaDeTeste = Vigilancia.EnvioParaNoPrimeiroDeTeste = false;
			RelogioSemPeerDeTeste = false;
			EscutaDasSagas = sagasDeAntes;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			foreach (Obra o in obras) _noChao.Remove(o);
			_viagensDoRelogio.Clear();
			_anuladorAte.Clear();
			_henshin.Clear();
			p.AgarradoPorId = 0;
			_cofreDeTech = cofreDeAntes;
			_cofreSujo = false;
			GravarOCofreDeTech();
			AplicarColisaoDasObras(p.Zone);
			GravarMundo();
			if (comScouter != null) DesfazerV2(comScouter);
			DesfazerV2(p, amigo);
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// X6) OS COMPANHEIROS -- o lote T5a
	// =====================================================================
	private void OsCompanheirosV21()
	{
		GD.Print("[v021] -- X6) A TECH PORTADA, lote 5: as capsulas de companheiro, a Pee-Kay Ball e a racao (item 13)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		const float T = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(10);
		CofreDeTech cofreDeAntes = _cofreDeTech;
		_cofreDeTech = new CofreDeTech();
		ServerPlayer p = ForjarV2("Treinador", c, 5_000);
		p.Conta = "bancada_v021_treinador";
		p.Facing = Facing.East;
		ServerPlayer rival = ForjarV2("Rival", c + new Vec2(4 * T, 0), 500);
		rival.Conta = "bancada_v021_rival";
		rival.Ficha.Ki = 0;
		p.Ficha.Zeni = p.Ficha.Recursos = 1_000_000;
		p.Ficha.techskill = 100;

		List<string> Fazer(ServerPlayer quem, string acao, string arg) => Ouvir(() => ComandoDeItem(quem, "item_" + acao, arg));
		bool Disse(List<string> falas, string trecho) => falas.Exists(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));
		string Item(ServerPlayer quem, string @base) => quem.Mochila.Pilhas.Find(x => TechDeBolso.Base(x.Id) == @base).Id ?? "";
		ServerPlayer? Solto(string capsula) => SoltoDaGaveta(Companheiros.Gaveta(capsula))?.Corpo;
		int SoltosDoP() => _companheiros.Values.Count(s => s.Dono == p.Id);
		void RecolherTodos() { foreach (int id in _companheiros.Keys.ToList()) RecolherCompanheiro(id, null); }
		List<Projetil> NoAr() => [.. ProjeteisDaZona(p.Zone.Hash).Where(x => x.Vivo)];
		void LimparOAr() { List<Projetil> ar = ProjeteisDaZona(p.Zone.Hash); _projeteisVivos -= ar.Count; ar.Clear(); }

		try
		{
			// ---------------------------------------------------------------- a fabricacao: a semente
			List<string> semSemente = Ouvir(() => Construir(p, Companheiros.CapsulaDeSaibaman));
			bool naoFabricou = p.Mochila.Quantos(Companheiros.CapsulaDeSaibaman) == 0;
			p.Mochila.Guardar(CatalogoDeItens.SementeDeMaca);
			double zeniDaCapsula = p.Ficha.Zeni;
			Ouvir(() => Construir(p, Companheiros.CapsulaDeSaibaman));
			AfirmarV21("a CAPSULA DE SAIBAMAN pede uma SEMENTE na mochila (`locate(/obj/items/food/Seed) in usr.contents`): sem ela nao se fabrica e nada e cobrado; com ela, a capsula sai e a semente se gasta",
					   Disse(semSemente, "semente") && naoFabricou && Math.Abs(zeniDaCapsula - 1_000_000) < 0.5
					   && p.Mochila.Quantos(Companheiros.CapsulaDeSaibaman) == 1 && p.Mochila.Quantos(CatalogoDeItens.SementeDeMaca) == 0
					   && p.Ficha.Zeni < zeniDaCapsula,
					   $"{string.Join(" | ", semSemente)}; capsulas {p.Mochila.Quantos(Companheiros.CapsulaDeSaibaman)}; sementes {p.Mochila.Quantos(CatalogoDeItens.SementeDeMaca)}; zeni {zeniDaCapsula:0} -> {p.Ficha.Zeni:0}");

			// ---------------------------------------------------------------- soltar e recolher
			List<string> aoSoltar = Fazer(p, Companheiros.AcaoSoltar, Companheiros.CapsulaDeSaibaman);
			string capsula = Item(p, Companheiros.CapsulaDeSaibaman);
			ServerPlayer? sai = Solto(capsula);
			Companheiro? criatura = _cofreDeTech.Companheiros.GetValueOrDefault(Companheiros.Gaveta(capsula));
			AfirmarV21("**A CAPSULA SOLTA A CRIATURA** (`\"[mob] appears!!\"`): um Saibaman de poder 1.000 e inteligencia 2 nasce a um passo do dono -- e o id da capsula leva so a gaveta e a inteligencia: o PODER mora no cofre do servidor",
					   sai is { Name: "Saibaman", Race: "Saibaman" } && Math.Abs(sai.Ficha.BP - 1000) < 1e-6 && criatura is { Inteligencia: 2 }
					   && capsula == Companheiros.Escrever(Companheiros.CapsulaDeSaibaman, Companheiros.Gaveta(capsula), 2) && !capsula.Contains("1000")
					   && Vec2.Distance(sai.Pos, p.Pos) <= 1.5f * T && Disse(aoSoltar, "aparece"),
					   $"capsula {capsula}; corpo {(sai == null ? "nenhum" : $"{sai.Name}/{sai.Race} BP {sai.Ficha.BP:0}")} | {string.Join(" | ", aoSoltar)}");
			Fazer(p, Companheiros.AcaoSoltar, capsula);
			bool recolheu = Solto(capsula) == null && sai != null && !_players.ContainsKey(sai.Id);

			p.Mochila.Guardar(Companheiros.NucleoDeBoneca);
			p.Mochila.Guardar(Companheiros.NucleoDoInterceptador);
			Fazer(p, Companheiros.AcaoSoltar, capsula);
			Fazer(p, Companheiros.AcaoSoltar, Companheiros.NucleoDeBoneca);
			ServerPlayer? boneca = Solto(Item(p, Companheiros.NucleoDeBoneca));
			List<string> terceira = Fazer(p, Companheiros.AcaoSoltar, Companheiros.NucleoDoInterceptador);
			int soltos = SoltosDoP();
			Companheiros.SemTetoDeSoltosDeTeste = true;
			int comDefeito;
			try { Fazer(p, Companheiros.AcaoSoltar, Item(p, Companheiros.NucleoDoInterceptador)); comDefeito = SoltosDoP(); }
			finally { Companheiros.SemTetoDeSoltosDeTeste = false; }
			AfirmarV21("usar a capsula de novo a RECOLHE (`\"[mob] disappears!\"`), e sao no maximo DUAS criaturas soltas por dono (`usr.out_mobs.len <= 1`): a terceira capsula nao abre -- a boneca nasce com inteligencia 8 e o dobro de ki; (defeito injetado: nao ha teto de soltas) e com o defeito a terceira abre",
					   recolheu && soltos == 2 && Disse(terceira, "recolha uma") && comDefeito == 3
					   && boneca is { Race: "SpiritDoll" } && Math.Abs(boneca.Ficha.KiMod - 2) < 1e-9 && Companheiros.Inteligencia(Item(p, Companheiros.NucleoDeBoneca)) == 8,
					   $"recolheu {recolheu}; soltos {soltos}; com defeito {comDefeito}; boneca {(boneca == null ? "nenhuma" : boneca.Race)} | {string.Join(" | ", terceira)}");
			RecolherTodos();

			// ---------------------------------------------------------------- seguir e ficar
			Fazer(p, Companheiros.AcaoSoltar, capsula);
			sai = Solto(capsula)!;
			CompanheiroSolto est = _companheiros[sai.Id];
			sai.Pos = p.Pos + new Vec2(2 * T, 0);
			GuiarOCompanheiro(sai, est, out ServerPlayer? semPresa, out Vec2 perto);
			sai.Pos = p.Pos + new Vec2(6 * T, 0);
			GuiarOCompanheiro(sai, est, out _, out Vec2 longe);
			Fazer(p, Companheiros.AcaoSeguir, capsula);
			GuiarOCompanheiro(sai, est, out _, out Vec2 ficando);
			Fazer(p, Companheiros.AcaoSeguir, capsula);
			AfirmarV21("solta, a criatura SEGUE o dono (`follow_state`): a ate tres tiles dele fica onde esta, alem disso vai ate ele; mandada FICAR (`\"You tell [src] to stay.\"`), nao sai do lugar",
					   semPresa == null && Vec2.Distance(perto, p.Pos + new Vec2(2 * T, 0)) < 1 && Vec2.Distance(longe, p.Pos) < 1
					   && Vec2.Distance(ficando, sai.Pos) < 1 && est.Ordem == OrdemDeCompanheiro.Seguir,
					   $"perto vai a {Vec2.Distance(perto, p.Pos) / T:0.#} tiles do dono; longe vai a {Vec2.Distance(longe, p.Pos) / T:0.#}; ficando {Vec2.Distance(ficando, sai.Pos) / T:0.#} do proprio lugar");

			// ---------------------------------------------------------------- atacar, matar, parar
			sai.Pos = p.Pos + new Vec2(T, T);
			p.AlvoId = 0;
			List<string> semAlvo = Fazer(p, Companheiros.AcaoAtacar, capsula);
			p.AlvoId = rival.Id;
			rival.Pos = p.Pos + new Vec2(4 * T, 0);
			Fazer(p, Companheiros.AcaoAtacar, capsula);
			bool cacando = GuiarOCompanheiro(sai, est, out ServerPlayer? presa, out _) && presa == rival;
			Companheiros.NaoObedeceDeTeste = true;
			bool defeitoCaca;
			try { GuiarOCompanheiro(sai, est, out ServerPlayer? presaDoDefeito, out _); defeitoCaca = presaDoDefeito == rival; }
			finally { Companheiros.NaoObedeceDeTeste = false; }
			rival.Ficha.KO = true;
			GuiarOCompanheiro(sai, est, out ServerPlayer? caido, out _);
			bool largou = caido == null && est.Presa == 0;
			Fazer(p, Companheiros.AcaoMatar, capsula);
			bool mata = sai.Combate.Letal && GuiarOCompanheiro(sai, est, out ServerPlayer? praMatar, out _) && praMatar == rival;
			Fazer(p, Companheiros.AcaoParar, capsula);
			bool parou = est.Presa == 0;
			rival.Ficha.KO = false;
			sai.Combate.Letal = false;
			AfirmarV21("**A CRIATURA OBEDECE**: sem alvo marcado a ordem e recusada; com o alvo a ate cinco tiles do dono ela o persegue (`foundTarget`); alvo caido, ela o larga (`if(src.target.KO && !src.murderToggle)`) -- e a ordem de MATAR liga o golpe letal e ela continua; PARAR a traz de volta; (defeito injetado: a criatura nao obedece) e com o defeito ela nao persegue ninguem",
					   Disse(semAlvo, "marque um alvo") && cacando && !defeitoCaca && largou && mata && parou,
					   $"sem alvo: {string.Join(" | ", semAlvo)}; cacando {cacando}; com defeito {defeitoCaca}; largou {largou}; mata {mata}; parou {parou}");

			// ---------------------------------------------------------------- a experiencia, o nivel e a melhoria
			List<string> semMelhoria = Fazer(p, Companheiros.AcaoMelhorar, capsula + "/1");
			Fazer(p, Companheiros.AcaoAtacar, capsula);
			sai.Combate.EmCombate = 5;
			TickDosCompanheiros(3.0);
			double expDeTresSegundos = criatura!.Exp;
			criatura.Exp = Companheiros.ExpDoNivel(1) - 1;
			List<string> aoSubir = Ouvir(() => TickDosCompanheiros(1.0));
			bool subiu = criatura.Nivel == 2 && criatura.Sobras == 1;
			Fazer(p, Companheiros.AcaoMelhorar, capsula + "/1");
			bool gastou = criatura.Sobras == 0 && criatura.Fisico == 1 && Math.Abs(sai.Ficha.physoff - 1.25) < 1e-9 && Math.Abs(sai.Ficha.physdef - 1.25) < 1e-9;
			Fazer(p, Companheiros.AcaoParar, capsula);
			sai.Combate.EmCombate = 0;
			AfirmarV21("LUTANDO a criatura ganha experiencia (um ponto por segundo de luta), cada nivel pede `10 * nivel`, e o nivel novo da uma MELHORIA: gasta nos fisicos, ataque e defesa sobem um quarto de ponto (`linked_phys_statboost / 4`) -- e sem melhoria por gastar o pedido e recusado",
					   Disse(semMelhoria, "não tem melhoria") && Math.Abs(expDeTresSegundos - 3) < 1e-6 && subiu && Disse(aoSubir, "nível 2") && gastou,
					   $"exp em 3 s {expDeTresSegundos:0.#}; subiu {subiu}; gastou {gastou} (physoff {sai.Ficha.physoff:0.##}) | {string.Join(" | ", aoSubir)}");

			// ---------------------------------------------------------------- o poder: a conta, a cobranca, e quem pode le-lo
			AfirmarV21("a conta do \"Update BP\" (`upgrade_linked_mob_bp`): a media do servidor, vezes a parte dela que o dono alcanca (de 10% a 100%), vezes `max(1, log2(nivel))`, vezes 2/5, vezes o modificador -- e nunca abaixo do que a criatura ja tem",
					   Math.Abs(Companheiros.Poder(10_000, 5_000, 4, 0.8, 1000) - 6400) < 1e-6 && Math.Abs(Companheiros.Poder(10_000, 100_000, 1, 1, 1) - 400) < 1e-6
					   && Math.Abs(Companheiros.Poder(100, 100, 1, 1, 1000) - 1000) < 1e-6);
			List<string> semComida = Fazer(p, Companheiros.AcaoPoder, capsula);
			p.Mochila.Guardar(CatalogoDeItens.Peixe);
			double zeniDoPoder = p.Ficha.Zeni;
			double esperado = Companheiros.Poder(MediaDoServidor(), p.Ficha.BP, criatura.Nivel, criatura.BpMod, criatura.Bp);
			Fazer(p, Companheiros.AcaoPoder, capsula);
			bool atualizou = Math.Abs(criatura.Bp - esperado) < 1e-6 && Math.Abs(zeniDoPoder - p.Ficha.Zeni - Companheiros.CustoDeAtualizar) < 0.5
							 && p.Mochila.Quantos(CatalogoDeItens.Peixe) == 0 && Math.Abs(sai.Ficha.BP - criatura.Bp) < 1e-6;
			string bpEscrito = criatura.Bp.ToString("N0"), bpCru = criatura.Bp.ToString("0");
			List<string> info = Fazer(p, Companheiros.AcaoInfo, capsula);
			bool calado = !info.Exists(f => f.Contains("Poder") && (f.Contains(bpEscrito) || f.Contains(bpCru))) && Disse(info, "o seu");
			Jandirus.Core.Stats.PoderNaFrase.NumeroSemMedidorDeTeste = true;
			List<string> comONumero;
			try { comONumero = Fazer(p, Companheiros.AcaoInfo, capsula); }
			finally { Jandirus.Core.Stats.PoderNaFrase.NumeroSemMedidorDeTeste = false; }
			AfirmarV21("\"Update BP\" pede 10.000 zeni E uma comida (`\"will require 10000 and one food item\"`), e a criatura solta recebe o poder novo; e **O PODER DA CRIATURA NAO SAI EM NUMERO** pra quem nao tem medidor (a regra principal do BP): o `Info` o mede contra o de quem le; (defeito injetado: o numero sai sem medidor) e com o defeito ele aparece",
					   Disse(semComida, "comida") && atualizou && calado && comONumero.Exists(f => f.Contains(bpEscrito) || f.Contains(bpCru)),
					   $"{string.Join(" | ", semComida)}; atualizou {atualizou} (BP {criatura.Bp:0}, esperado {esperado:0}); calado {calado} | {string.Join(" | ", info)}");

			// ---------------------------------------------------------------- a racao, e as ordens que pedem confianca
			p.Mochila.Guardar(Companheiros.Racao);
			sai.Pos = p.Pos + new Vec2(8 * T, 0);
			List<string> longeDaRacao = Fazer(p, Companheiros.AcaoRacao, Companheiros.Racao);
			sai.Pos = p.Pos + new Vec2(T, T);
			Companheiros.RacaoDoOriginalDeTeste = true;
			try { Fazer(p, Companheiros.AcaoRacao, Companheiros.Racao); }
			finally { Companheiros.RacaoDoOriginalDeTeste = false; }
			bool defeitoNaoFaz = criatura.Inteligencia == 2 && p.Mochila.Quantos(Companheiros.Racao) == 1;
			Fazer(p, Companheiros.AcaoRacao, Companheiros.Racao);
			string esperta = Item(p, Companheiros.CapsulaDeSaibaman);
			bool ordensNovas = Array.IndexOf(CatalogoDeItens.Get(esperta)?.AcoesDoItem ?? [], Companheiros.AcaoQuandoApanho) >= 0
							   && Array.IndexOf(CatalogoDeItens.Get(capsula)?.AcoesDoItem ?? [], Companheiros.AcaoQuandoApanho) < 0;
			capsula = esperta;
			List<string> semConfianca = Fazer(p, Companheiros.AcaoQuandoApanho, capsula);
			criatura.Relacao = Companheiros.RelacaoDeConfianca + 1;
			Fazer(p, Companheiros.AcaoQuandoApanho, capsula);
			p.UltimoAgressor = rival.Id;
			p.RancorAte = NowMs() + 5_000;
			bool defende = GuiarOCompanheiro(sai, est, out ServerPlayer? agressor, out _) && agressor == rival;
			p.UltimoAgressor = 0;
			p.RancorAte = 0;
			LargarAPresa(sai, est);
			AfirmarV21("a RACAO da tres de inteligencia a criatura do dono que esta ao lado (`M.techmod+=3`) e se gasta -- e a capsula passa a mostrar as ordens que a criatura agora entende; \"atacar quando me atacam\" pede inteligencia 4 E confianca (`relation > 20`), e ligada faz a criatura partir pra cima de quem bateu no dono; (defeito injetado: a racao compara o dono com ela mesma, como no original) e com o defeito ela nao faz nada",
					   Disse(longeDaRacao, "ao seu lado") && defeitoNaoFaz && criatura.Inteligencia == 5 && p.Mochila.Quantos(Companheiros.Racao) == 0
					   && Companheiros.Inteligencia(esperta) == 5 && ordensNovas && Disse(semConfianca, "não confia") && criatura.QuandoApanho && defende,
					   $"{string.Join(" | ", longeDaRacao)}; defeito nao faz {defeitoNaoFaz}; inteligencia {criatura.Inteligencia}; capsula {esperta}; ordens novas {ordensNovas}; defende {defende} | {string.Join(" | ", semConfianca)}");

			// ---------------------------------------------------------------- a Pee-Kay Ball
			sai.Pos = p.Pos + new Vec2(0, 3 * T);
			p.Mochila.Guardar(Companheiros.Bola);
			rival.Pos = p.Pos + new Vec2(T, 0);
			List<string> dePe = Fazer(p, Companheiros.AcaoCapturar, Companheiros.Bola);
			rival.Ficha.KO = true;
			Fazer(p, Companheiros.AcaoCapturar, Companheiros.Bola);
			rival.Ficha.KO = false;
			string bola = Item(p, Companheiros.Bola);
			Companheiro? capturado = _cofreDeTech.Companheiros.GetValueOrDefault(Companheiros.Gaveta(bola));
			rival.Pos = c + new Vec2(8 * T, 0);
			Fazer(p, Companheiros.AcaoSoltar, bola);
			ServerPlayer? forma = Solto(bola);
			AfirmarV21("**A PEE-KAY BALL CAPTURA** a criatura NOCAUTEADA a frente (`M.KO`; de pe, nao): guarda o nome, a cara e o poder dela -- o capturado continua no mundo --, e solta, a bola ergue uma criatura com esse nome e essa raca",
					   Disse(dePe, "nocauteada") && capturado is { Nome: "Rival", Especie: Companheiros.EspecieCapturada } && Math.Abs(capturado.Bp - rival.Ficha.BP) < 1e-6
					   && forma is { Name: "Rival" } && forma.Race == rival.Race && forma.Id != rival.Id && _players.ContainsKey(rival.Id),
					   $"{string.Join(" | ", dePe)}; bola {bola}; capturado {capturado?.Nome} BP {capturado?.Bp:0}; forma {(forma == null ? "nenhuma" : forma.Name)}");
			RecolherTodos();

			// ---------------------------------------------------------------- o interceptador
			string nucleo = Item(p, Companheiros.NucleoDoInterceptador);
			Fazer(p, Companheiros.AcaoSoltar, nucleo);
			ServerPlayer? drone = Solto(nucleo);
			rival.Pos = c + new Vec2(3 * T, T);
			rival.Facing = Facing.East;
			rival.Mochila.Guardar(ArmasDeFogo.Pistola);
			p.Mochila.Guardar(ArmasDeFogo.Pistola);
			LimparOAr();
			Fazer(rival, ArmasDeFogo.AcaoAtirar, ArmasDeFogo.Pistola);
			Fazer(p, ArmasDeFogo.AcaoAtirar, ArmasDeFogo.Pistola);
			int noArAntes = NoAr().Count;
			Companheiros.InterceptadorParadoDeTeste = true;
			int comOParado;
			try { TickDosCompanheiros(Companheiros.SegundosDoInterceptador + 0.1); comOParado = NoAr().Count; }
			finally { Companheiros.InterceptadorParadoDeTeste = false; }
			TickDosCompanheiros(Companheiros.SegundosDoInterceptador + 0.1);
			List<Projetil> sobrou = NoAr();
			AfirmarV21("**O INTERCEPTADOR DERRUBA OS DISPAROS** que nao sao do dono, a ate nove tiles, a cada segundo e meio (`InterceptorAI`): o tiro do rival cai, o do dono segue -- e o corpo dele e a folha do interceptador, sem cabelo nem rosto de gente; (defeito injetado: o interceptador nao derruba nada) e com o defeito os dois tiros continuam no ar",
					   drone is { Race: Companheiros.RacaDoInterceptador } && noArAntes == 2 && comOParado == 2 && sobrou.Count == 1 && sobrou[0].Dono == p.Id
					   && Companheiros.FolhaDoCorpo(drone.Race)?.EndsWith("Interceptor.tres") == true
					   && !Jandirus.Core.Appearance.VisualCatalog.TemCabelo(drone.Race),
					   $"drone {(drone == null ? "nenhum" : drone.Race)}; no ar {noArAntes} -> com o parado {comOParado} -> {sobrou.Count} (do dono: {sobrou.Count == 1 && sobrou[0].Dono == p.Id})");
			LimparOAr();

			// ---------------------------------------------------------------- a morte, e a saida do dono
			bool morreuCerto = false;
			List<string> morte = [];
			if (drone != null)
			{
				LargadosDe(p.Zone.Hash).Clear();
				drone.Ficha.dead = true;
				morte = Ouvir(() => TickDosCompanheiros(0.1));
				Largado? monte = LargadosDe(p.Zone.Hash).LastOrDefault(l => l.Tipo == TipoDeLargado.Zeni);
				morreuCerto = !_players.ContainsKey(drone.Id) && monte is { Quantidade: >= Companheiros.ZeniMinDaMorte and <= Companheiros.ZeniMaxDaMorte }
							  && _cofreDeTech.Companheiros.ContainsKey(Companheiros.Gaveta(nucleo));
			}
			Fazer(p, Companheiros.AcaoSoltar, nucleo);
			ServerPlayer? deNovo = Solto(nucleo);
			bool voltou = false;
			if (deNovo != null)
			{
				CompanheiroSolto doDeNovo = _companheiros[deNovo.Id];
				ZoneKey zonaDoDono = p.Zone;
				p.Zone = new ZoneKey(ZoneKey.KindPremade, "Namek");
				bool guiou;
				try { guiou = GuiarOCompanheiro(deNovo, doDeNovo, out _, out _); }
				finally { p.Zone = zonaDoDono; }
				voltou = !guiou && !_players.ContainsKey(deNovo.Id) && _cofreDeTech.Companheiros.ContainsKey(Companheiros.Gaveta(nucleo));
			}
			AfirmarV21("a criatura MORTA deixa um punhado de zeni no chao (`rand(100,4000)`) e some, e a capsula continua guardando a forma dela -- solta-se de novo; e quando o dono sai da zona, a criatura VOLTA PRA CAPSULA em vez de ficar pra tras",
					   morreuCerto && Disse(morte, "foi morta") && deNovo != null && voltou,
					   $"morreu certo {morreuCerto}; solta de novo {deNovo != null}; voltou pra capsula {voltou} | {string.Join(" | ", morte)}");
		}
		finally
		{
			Companheiros.SemTetoDeSoltosDeTeste = Companheiros.NaoObedeceDeTeste = Companheiros.InterceptadorParadoDeTeste = Companheiros.RacaoDoOriginalDeTeste = false;
			Jandirus.Core.Stats.PoderNaFrase.NumeroSemMedidorDeTeste = false;
			RecolherTodos();
			LimparOAr();
			LargadosDe(p.Zone.Hash).Clear();
			p.AlvoId = 0;
			_cofreDeTech = cofreDeAntes;
			_cofreSujo = false;
			GravarOCofreDeTech();
			DesfazerV2(p, rival);
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// X8) OS KITS DO ANDROIDE DE ABSORCAO, E O TANQUE QUE NAO O CURA -- o lote T5c (regra do dono, 2026-10-13); e (2026-10-26) o ANDROID
	// UPGRADE, que solta o banco de potencial dele (o pedido 95c)
	// =====================================================================
	private void OsKitsDeAndroideV21()
	{
		GD.Print("[v021] -- X8) A TECH PORTADA, lote 5 (3a parte): o kit de reparo e o kit mecanico, e o regenerador que nao cura o androide de absorcao (regra do dono de 2026-10-13)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		const float T = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(10);
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		var obras = new List<Obra>();
		var corpos = new List<ServerPlayer>();
		ServerPlayer Nasce(string nome, Vec2 onde, double bp)
		{
			ServerPlayer novo = ForjarV2(nome, onde, bp);
			novo.Conta = "bancada_v021_" + nome.ToLowerInvariant();
			corpos.Add(novo);
			return novo;
		}
		ServerPlayer absorvedor = Nasce("Absorvedor", c, 5_000);
		absorvedor.Ficha.AndroideAbsorcao = true;
		absorvedor.Facing = Facing.East;
		ServerPlayer infinito = Nasce("Infinito", c, 5_000);
		infinito.Ficha.AndroideInfinito = true;
		ServerPlayer humano = Nasce("Humano", c + new Vec2(4 * T, 0), 5_000);

		List<string> Fazer(ServerPlayer quem, string acao, string arg) => Ouvir(() => ComandoDeItem(quem, "item_" + acao, arg));
		bool Disse(List<string> falas, string trecho) => falas.Exists(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));
		void Passar(double segundos) { AdiantoDoRelogioDeTeste += (long)(segundos * 1000); TickDasEsperasParadas(); }
		bool Igual(double a, double b) => Math.Abs(a - b) < 1e-9;
		double Vida(ServerPlayer quem) => quem.Combate!.Corpo.Vida();
		void Ferir(ServerPlayer quem, double dano)
		{
			foreach (BodyPart parte in quem.Combate!.Corpo.Partes.ToList())
				if (!parte.Decepado && !parte.Aninhado) quem.Combate.Ferir(parte, dano, false);
			quem.Combate.SincronizarVida();
		}

		try
		{
			// ---------------------------------------------------------------- o tanque
			Obra tanque = ObraDeBancadaV21(Regenerador.Tipo, c, absorvedor);
			obras.Add(tanque);
			RegeneradorDaObra r = EstadoDoTanque(tanque);
			r.Ligado = true;
			r.Energia = r.EnergiaMax = 1;
			Ferir(absorvedor, 40);
			Ferir(infinito, 40);
			double vidaDoAbsorvedor = Vida(absorvedor), vidaDoInfinito = Vida(infinito), bateria = r.Energia;
			List<string> noTanque = Ouvir(PulsoDosTanques);
			bool absorvedorNaoCura = Igual(Vida(absorvedor), vidaDoAbsorvedor);
			bool infinitoCura = Vida(infinito) > vidaDoInfinito + 0.1;
			double gasto = bateria - r.Energia;
			List<string> semRepetir = Ouvir(PulsoDosTanques);
			KitsDeAndroide.AbsorcaoCuraNoTanqueDeTeste = true;
			bool comDefeitoCura;
			try { PulsoDosTanques(); comDefeitoCura = Vida(absorvedor) > vidaDoAbsorvedor + 0.1; }
			finally { KitsDeAndroide.AbsorcaoCuraNoTanqueDeTeste = false; }
			AfirmarV21("**O REGENERADOR NAO CURA O ANDROIDE DE ABSORCAO** (dono, 2026-10-13: *\"androides q absorvem energia nao curam na maquina de regeneração, androides de energia infinita q custa 2 milhoes regeneram normalmente\"*): no mesmo tanque ligado, o de energia infinita se cura e o de absorcao fica como estava -- sem gastar a bateria com ele --, e o tanque lhe diz como se consertar (uma vez, e nao a cada pulso); (defeito injetado: o de absorcao se cura no tanque) e com o defeito ele se cura",
					   absorvedorNaoCura && infinitoCura && Math.Abs(gasto - Regenerador.BateriaPorPulso(r.Eficiencia)) < 1e-9
					   && Disse(noTanque, "kit de reparo") && !Disse(semRepetir, "kit de reparo") && comDefeitoCura,
					   $"absorvedor {vidaDoAbsorvedor:0.##} -> nao cura {absorvedorNaoCura}; infinito {vidaDoInfinito:0.##} -> {Vida(infinito):0.##}; bateria gasta no 1o pulso {gasto:0.####}; com defeito cura {comDefeitoCura} | {string.Join(" | ", noTanque)}");
			_noChao.Remove(tanque);

			// ---------------------------------------------------------------- o kit de reparo
			Ferir(humano, 30);
			humano.Mochila.Guardar(KitsDeAndroide.Reparo);
			List<string> naoServe = Fazer(humano, KitsDeAndroide.AcaoReparar, KitsDeAndroide.Reparo);
			absorvedor.Mochila.Guardar(KitsDeAndroide.Reparo);
			double antesDoKit = Vida(absorvedor);
			BodyPart? umaParte = absorvedor.Combate!.Corpo.Partes.FirstOrDefault(x => !x.Decepado && !x.Aninhado && x.Vida < x.VidaMax);
			double vidaDaParte = umaParte?.Vida ?? 0;
			Fazer(absorvedor, KitsDeAndroide.AcaoReparar, KitsDeAndroide.Reparo);
			bool esperando = _esperasParadas.ContainsKey(absorvedor.Id) && absorvedor.Mochila.Quantos(KitsDeAndroide.Reparo) == 1;
			Passar(KitsDeAndroide.SegundosParado + 0.5);
			bool consertou = umaParte != null && Igual(umaParte.Vida, Math.Min(vidaDaParte + KitsDeAndroide.VidaPorKit, umaParte.VidaMax))
							 && Vida(absorvedor) > antesDoKit && absorvedor.Mochila.Quantos(KitsDeAndroide.Reparo) == 0;
			absorvedor.Mochila.Guardar(KitsDeAndroide.Reparo);
			Fazer(absorvedor, KitsDeAndroide.AcaoReparar, KitsDeAndroide.Reparo);
			absorvedor.Pos = c + new Vec2(0, T);
			List<string> mexeu = Ouvir(() => Passar(1));
			absorvedor.Pos = c;
			KitsDeAndroide.QualquerUmUsaDeTeste = true;
			bool defeitoServe;
			try { Fazer(humano, KitsDeAndroide.AcaoReparar, KitsDeAndroide.Reparo); defeitoServe = _esperasParadas.ContainsKey(humano.Id); }
			finally { KitsDeAndroide.QualquerUmUsaDeTeste = false; _esperasParadas.Remove(humano.Id); }
			AfirmarV21("**O KIT DE REPARO E A CURA DO ANDROIDE DE ABSORCAO**: dez segundos parado e sem apanhar (`\"This will take a moment, don't move or take further damage!\"`), cada parte ferida ganha dez de vida (`C.health += 10`) e o kit se gasta; quem se mexe perde a vez e fica com o kit; e quem nao e androide de absorcao ouve que o kit nao lhe serve; (defeito injetado: os kits servem a qualquer corpo) e com o defeito o humano comeca o conserto",
					   Disse(naoServe, "androide de absorção") && humano.Mochila.Quantos(KitsDeAndroide.Reparo) == 1 && esperando && consertou
					   && Disse(mexeu, "não saiu") && absorvedor.Mochila.Quantos(KitsDeAndroide.Reparo) == 1 && defeitoServe,
					   $"{string.Join(" | ", naoServe)}; esperando {esperando}; consertou {consertou} (parte {vidaDaParte:0.##} -> {umaParte?.Vida:0.##}; vida {antesDoKit:0.##} -> {Vida(absorvedor):0.##}); defeito serve {defeitoServe} | {string.Join(" | ", mexeu)}");

			// ---------------------------------------------------------------- o kit mecanico
			BodyPart? braco = absorvedor.Combate.Corpo.Achar("Braco esquerdo");
			if (braco != null) { braco.Decepado = true; braco.Perdido = false; }
			absorvedor.Mochila.Pilhas.Clear();
			absorvedor.Mochila.Guardar(KitsDeAndroide.Mecanico);
			Fazer(absorvedor, KitsDeAndroide.AcaoRefazer, KitsDeAndroide.Mecanico);
			Passar(KitsDeAndroide.SegundosParado + 0.5);
			bool refez = braco is { Decepado: false } && absorvedor.Mochila.Quantos(KitsDeAndroide.Mecanico) == 0;

			// (o pulso do tanque, la em cima, crava o olhar de quem ele cura pro sul -- o `CravarOlhar` do
			// `PulsoDosTanques` --, e o defeito injetado curou este corpo: sem virar de volta, o caido a leste nao estaria
			// "a frente" de ninguem, e o kit cairia em quem usa)
			absorvedor.Facing = Facing.East;
			ServerPlayer caido = Nasce("Caido", c + new Vec2(T, 0), 500);
			caido.Ficha.AndroideAbsorcao = true;
			BodyPart? perna = caido.Combate!.Corpo.Achar("Perna esquerda");
			if (perna != null) { perna.Decepado = true; perna.Perdido = false; }
			caido.Ficha.KO = true;
			absorvedor.Mochila.Guardar(KitsDeAndroide.Mecanico);
			List<string> noOutro = Fazer(absorvedor, KitsDeAndroide.AcaoRefazer, KitsDeAndroide.Mecanico);
			Passar(KitsDeAndroide.SegundosParado + 0.5);
			bool refezODoOutro = perna is { Decepado: false } && absorvedor.Mochila.Quantos(KitsDeAndroide.Mecanico) == 0;
			caido.Ficha.KO = false;

			absorvedor.Mochila.Guardar(KitsDeAndroide.Mecanico);
			List<string> nadaPraRefazer = Fazer(absorvedor, KitsDeAndroide.AcaoRefazer, KitsDeAndroide.Mecanico);
			if (braco != null) { braco.Decepado = true; braco.Perdido = true; }
			List<string> perdidoDeVez = Fazer(absorvedor, KitsDeAndroide.AcaoRefazer, KitsDeAndroide.Mecanico);
			if (braco != null) { braco.Decepado = false; braco.Perdido = false; }
			AfirmarV21("**O KIT MECANICO REFAZ UM MEMBRO PERDIDO** do androide de absorcao -- o de quem usa, ou o de um caido a frente --, com os mesmos dez segundos, e se gasta; sem membro perdido ele recusa, e o membro perdido DE VEZ nao volta nem por ele",
					   refez && refezODoOutro && Disse(noOutro, "Caido") && Disse(nadaPraRefazer, "não há membro perdido")
					   && Disse(perdidoDeVez, "não há membro perdido") && absorvedor.Mochila.Quantos(KitsDeAndroide.Mecanico) == 1,
					   $"refez o proprio {refez}; refez o do caido {refezODoOutro}; kits {absorvedor.Mochila.Quantos(KitsDeAndroide.Mecanico)} | {string.Join(" | ", noOutro)} | {string.Join(" | ", nadaPraRefazer)} | {string.Join(" | ", perdidoDeVez)}");

			// ---------------------------------------------------------------- (2026-10-26) o upgrade -- o pedido 95c do dono
			// O banco do androide de absorcao so se solta pelo Android Upgrade: dez segundos parado, 1x o banco, a hora de recarga dos
			// liberadores. O humano nao o usa (o potencial dele se solta pelos caminhos de sempre), e a segunda vez dentro da hora e recusada
			// sem gastar o item. Tres afirmacoes, uma por porta (o 95c-A, o B e o C), pra o mutante de duas trocas dizer qual caiu.
			var fa = absorvedor.Ficha;
			fa.hiddenpotential = 1_000;
			fa.liberarPotencialEm = 0;
			double bpAntesDoUpgrade = fa.BP;
			absorvedor.Mochila.Guardar(KitsDeAndroide.Upgrade);
			humano.Mochila.Guardar(KitsDeAndroide.Upgrade);
			humano.Ficha.hiddenpotential = 1_000;
			humano.Ficha.liberarPotencialEm = 0;
			List<string> humanoNao = Fazer(humano, KitsDeAndroide.AcaoUpgrade, KitsDeAndroide.Upgrade);
			Fazer(absorvedor, KitsDeAndroide.AcaoUpgrade, KitsDeAndroide.Upgrade);
			bool esperandoUpgrade = _esperasParadas.ContainsKey(absorvedor.Id) && absorvedor.Mochila.Quantos(KitsDeAndroide.Upgrade) == 1;
			Passar(KitsDeAndroide.SegundosParado + 0.5);
			bool soltou = Igual(fa.BP, bpAntesDoUpgrade + 1_000 * Jandirus.Core.Stats.GainKnobs.LiberaOUpgrade) && fa.hiddenpotential == 0
						  && absorvedor.Mochila.Quantos(KitsDeAndroide.Upgrade) == 0;
			AfirmarV21("**95c-A O ANDROID UPGRADE SOLTA O BANCO DO ANDROIDE DE ABSORCAO** (o pedido do dono: *\"so libera com tech q da um 'upgrade' neles\"*): dez segundos parado e sem apanhar, o banco inteiro (1x) vai pro BP base, e o item se gasta",
					   esperandoUpgrade && soltou,
					   $"esperando {esperandoUpgrade}; BP {bpAntesDoUpgrade:0} -> {fa.BP:0}, banco {fa.hiddenpotential:0}, itens {absorvedor.Mochila.Quantos(KitsDeAndroide.Upgrade)}");

			bool comRecarga = fa.liberarPotencialEm > NowMs();
			fa.hiddenpotential = 500;
			absorvedor.Mochila.Guardar(KitsDeAndroide.Upgrade);
			List<string> naHora = Fazer(absorvedor, KitsDeAndroide.AcaoUpgrade, KitsDeAndroide.Upgrade);
			bool recusouNaHora = !_esperasParadas.ContainsKey(absorvedor.Id) && absorvedor.Mochila.Quantos(KitsDeAndroide.Upgrade) == 1
								 && fa.hiddenpotential == 500 && Disse(naHora, "assenta");
			AfirmarV21("**95c-B ...com a HORA DE RECARGA dos liberadores**: a segunda vez dentro da hora e recusada, sem gastar o item nem o banco",
					   comRecarga && recusouNaHora, $"recarga marcada {comRecarga}; recusou {recusouNaHora} | {string.Join(" | ", naHora)}");

			AfirmarV21("**95c-C ...e SO O ANDROIDE DE ABSORCAO o usa**: o humano ouve que o potencial dele se solta pelos caminhos de sempre, e fica com o item e com o banco",
					   Disse(humanoNao, "androide de absorção") && humano.Mochila.Quantos(KitsDeAndroide.Upgrade) == 1 && humano.Ficha.hiddenpotential == 1_000,
					   $"{string.Join(" | ", humanoNao)}; item {humano.Mochila.Quantos(KitsDeAndroide.Upgrade)}, banco {humano.Ficha.hiddenpotential:0}");
		}
		finally
		{
			KitsDeAndroide.AbsorcaoCuraNoTanqueDeTeste = KitsDeAndroide.QualquerUmUsaDeTeste = false;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			foreach (Obra o in obras) _noChao.Remove(o);
			foreach (ServerPlayer corpo in corpos) { _esperasParadas.Remove(corpo.Id); _avisoDoTanqueEm.Remove(corpo.Id); }
			DesfazerV2([.. corpos]);
			LimparTudoDaBancada();
		}
	}

	/// <summary>Um ponto de chao firme SEM agua nenhuma ao alcance da vara de pescar, na zona da bancada. Nulo se nao ha.</summary>
	private Vec2? LongeDaAguaV21()
	{
		if (_pjMapa is not { } mapa) return null;
		const float T = ZoneCollision.TileSize;
		int r = Pesca.RaioDeAgua + 1;
		for (int y = 8; y < mapa.Height - 8; y++)
			for (int x = 8; x < mapa.Width - 8; x++)
			{
				if (mapa.BlockedCell(x, y) || mapa.EhAgua(x, y) || mapa.EhNuvem(x, y)) continue;
				bool semAgua = true;
				for (int dy = -r; dy <= r && semAgua; dy++)
					for (int dx = -r; dx <= r && semAgua; dx++)
						semAgua &= !mapa.EhAgua(x + dx, y + dy);
				if (semAgua) return new Vec2(x * T + T / 2f, y * T + T / 2f);
			}
		return null;
	}

	// =====================================================================
	// X9) O SUPER COMPUTADOR -- o lote T6
	// =====================================================================
	private void OSuperComputadorV21()
	{
		GD.Print("[v021] -- X9) A TECH PORTADA, lote 6: o super computador -- a chave de controle, os droides e as ordens deles, as melhorias e a ressurreicao de androide (item 13)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		const float T = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(10);
		var obras = new List<Obra>();
		var corpos = new List<ServerPlayer>();
		ServerPlayer Nasce(string nome, Vec2 onde, double bp)
		{
			ServerPlayer novo = ForjarV2(nome, onde, bp);
			novo.Conta = "bancada_v021_" + nome.ToLowerInvariant();
			novo.Ficha.Zeni = novo.Ficha.Recursos = 1_000_000;
			corpos.Add(novo);
			return novo;
		}
		ServerPlayer p = Nasce("Engenheiro", c, 5_000);
		p.Facing = Facing.East;
		ServerPlayer vizinho = Nasce("Vizinho", c, 500);
		ServerPlayer intruso = Nasce("Intruso", c, 500);
		ServerPlayer rival = Nasce("Rival", c + new Vec2(4 * T, 0), 500);

		List<string> Usar(ServerPlayer quem, string verbo, string arg = "") => Ouvir(() => ComandoDeInteracao(quem, verbo, arg));
		bool Disse(List<string> falas, string trecho) => falas.Exists(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));
		string Pago(string oQue, bool emRecursos = false) => Jandirus.Core.Economia.Recurso.PedidoPago(
			oQue, emRecursos ? Jandirus.Core.Economia.Moeda.Recursos : Jandirus.Core.Economia.Moeda.Zeni);
		double TetoDe(ServerPlayer quem) => TetoDeTecnologia.De(quem.Ficha.relBPmax, quem.Ficha.techskill, quem.Ficha.techmod);
		Obra Por(Vec2 onde, ServerPlayer dono)
		{
			Obra o = ObraDeBancadaV21(SuperComputador.Tipo, onde, dono);
			// (a armadura de uma obra posta em jogo e o teto de tecnologia de quem a ergueu -- `Posicionar`)
			o.ArmaduraMax = o.Armadura = TetoDe(dono);
			obras.Add(o);
			return o;
		}
		List<ServerPlayer> DroidesDoP() => [.. DroidesDe(p.Id).Where(_players.ContainsKey).Select(id => _players[id])];

		try
		{
			// ---------------------------------------------------------------- a chave de controle
			Obra pc = Por(c + new Vec2(0, -T), p);
			List<string> naoEDono = Usar(vizinho, SuperComputador.VerboCifrar, "roubo");
			List<string> vazia = Usar(p, SuperComputador.VerboCifrar, "   ");
			Usar(p, SuperComputador.VerboCifrar, "kame");
			EstadoDeTech e = pc.Tech ?? new EstadoDeTech();
			bool cifrado = e.Txt(SuperComputador.ChaveDaChave) == "kame" && e.Txt(SuperComputador.ChaveConta) == p.Conta;
			List<string> deNovo = Usar(p, SuperComputador.VerboCifrar, "outra");
			List<string> fechado = Usar(vizinho, SuperComputador.VerboDroide);
			bool vizinhoDeFora = DroidesDe(vizinho.Id).Count == 0;
			List<string> errada = Usar(vizinho, SuperComputador.VerboChave, "kami");
			List<string> certa = Usar(vizinho, SuperComputador.VerboChave, "kame");
			Usar(vizinho, SuperComputador.VerboDroide);
			bool vizinhoEntrou = DroidesDe(vizinho.Id).Count == 1;
			SuperComputador.AbreSemAChaveDeTeste = true;
			bool intrusoEntra;
			try { Usar(intruso, SuperComputador.VerboDroide); intrusoEntra = DroidesDe(intruso.Id).Count == 1; }
			finally { SuperComputador.AbreSemAChaveDeTeste = false; }
			AfirmarV21("**A CHAVE DE CONTROLE** (`Encrypt()`: *\"Put in the control key. Don't forget this!\"*): so o dono cifra, a chave vazia nao vale, e cifrado ele nao aceita outra (*\"has already been encrypted\"*); dai em diante quem nao digitou a chave fica de fora, a chave errada e recusada (*\"You do not know the encryption key.\"*) e a certa abre o computador pra sessao de quem a digita; (defeito injetado: o computador cifrado abre sem a chave) e com o defeito um intruso monta um droide nele",
					   Disse(naoEDono, "só o dono") && Disse(vazia, "vazia") && cifrado && Disse(deNovo, "já está cifrado") && e.Txt(SuperComputador.ChaveDaChave) == "kame"
					   && Disse(fechado, "cifrado") && vizinhoDeFora && Disse(errada, "não sabe") && Disse(certa, "aceita") && vizinhoEntrou && intrusoEntra,
					   $"{string.Join(" | ", naoEDono)} | {string.Join(" | ", vazia)} | cifrado {cifrado} | {string.Join(" | ", deNovo)} | {string.Join(" | ", fechado)} | {string.Join(" | ", errada)} | {string.Join(" | ", certa)} | vizinho entrou {vizinhoEntrou}; intruso (defeito) {intrusoEntra}");

			// QUEM SAI DO JOGO LEVA OS DROIDES DELE E A CHAVE QUE DIGITOU.
			int droideDoVizinho = DroidesDe(vizinho.Id).FirstOrDefault();
			EsquecerSuperComputador(vizinho.Id);
			EsquecerSuperComputador(intruso.Id);
			List<string> depoisDeSair = Usar(vizinho, SuperComputador.VerboDroide);
			AfirmarV21("...e o droide e a chave digitada sao da SESSAO: quem sai do mundo leva os droides dele (o corpo some) e, de volta, tem de digitar a chave de novo",
					   droideDoVizinho != 0 && !_players.ContainsKey(droideDoVizinho) && DroidesDe(vizinho.Id).Count == 0 && DroidesDe(intruso.Id).Count == 0
					   && Disse(depoisDeSair, "cifrado"),
					   $"droide do vizinho no mundo {_players.ContainsKey(droideDoVizinho)}; {string.Join(" | ", depoisDeSair)}");
			vizinho.Pos = intruso.Pos = c + new Vec2(9 * T, T);

			// ---------------------------------------------------------------- o droide
			Usar(p, SuperComputador.VerboDroide);
			ServerPlayer? droide = DroidesDoP().FirstOrDefault();
			AfirmarV21("**O COMPUTADOR MONTA UM DROIDE** (`makeCopy(4,\"Android\",\"None\",/mob/npc/Clone,FALSE)`): um corpo sem dono de tela chamado \"[dono] Android Body Model-#N\", de berco Android, com a folha do androide (uma folha so, sem cabelo nem rosto por cima), o BP de quem o pediu, o golpe nao-letal e o cerebro de fabrica -- a um passo do dono",
					   droide is { Peer: null, Cerebro: not null } && droide.Name.StartsWith("Engenheiro Android Body Model-#", StringComparison.Ordinal)
					   && droide.Race == SuperComputador.CorpoDoDroide && droide.Ficha.Race == SuperComputador.RacaDoDroide
					   && Companheiros.FolhaDoCorpo(droide.Race) == SuperComputador.FolhaDoDroide && Companheiros.EhDeFolha(droide.Race)
					   && Math.Abs(droide.Ficha.BP - p.Ficha.BP) < 1e-6 && !droide.Combate!.Letal
					   && Vec2.Distance(droide.Pos, p.Pos) < 2.5f * T,
					   droide == null ? "nao nasceu" : $"'{droide.Name}', corpo {droide.Race}, berco {droide.Ficha.Race}, folha {Companheiros.FolhaDoCorpo(droide.Race)}, BP {droide.Ficha.BP:0} de {p.Ficha.BP:0}, letal {droide.Combate!.Letal}, a {Vec2.Distance(droide.Pos, p.Pos) / T:0.#} tiles");
			if (droide == null) return;
			Droide est = _droides[droide.Id];

			droide.Pos = c + new Vec2(5 * T, 0);
			GuiarODroide(droide, est, out ServerPlayer? semPresa, out Vec2 longe);
			droide.Pos = c + new Vec2(T, 0);
			GuiarODroide(droide, est, out _, out Vec2 perto);
			for (int i = 1; i < SuperComputador.MaxDeDroides; i++) Usar(p, SuperComputador.VerboDroide);
			int noTetoAntes = DroidesDe(p.Id).Count;
			List<string> noTeto = Usar(p, SuperComputador.VerboDroide);
			int noTetoDepois = DroidesDe(p.Id).Count;
			SuperComputador.SemTetoDeDroidesDeTeste = true;
			int comDefeito;
			try { Usar(p, SuperComputador.VerboDroide); comDefeito = DroidesDe(p.Id).Count; }
			finally { SuperComputador.SemTetoDeDroidesDeTeste = false; }
			AfirmarV21("**O DROIDE SEGUE O DONO** (`if(d >= 2) step_towards(src,usr)`): a cinco tiles ele vai ate ele, a um fica onde esta, e nao persegue ninguem -- e ha TETO (quatro por dono): o quinto e recusado; (defeito injetado: nao ha teto de droides) e com o defeito o quinto nasce",
					   semPresa == null && Vec2.Distance(longe, p.Pos) < 1f && Vec2.Distance(perto, droide.Pos) < 1f
					   && noTetoAntes == SuperComputador.MaxDeDroides && noTetoDepois == SuperComputador.MaxDeDroides && Disse(noTeto, "já tem")
					   && comDefeito == SuperComputador.MaxDeDroides + 1,
					   $"presa {semPresa?.Name ?? "nenhuma"}; longe -> a {Vec2.Distance(longe, p.Pos) / T:0.#} do dono; perto -> a {Vec2.Distance(perto, droide.Pos) / T:0.#} de si; {noTetoAntes} -> {noTetoDepois} ({string.Join(" | ", noTeto)}); com defeito {comDefeito}");

			// ---------------------------------------------------------------- as ordens
			List<string> semAlvo = Usar(p, SuperComputador.VerboOrdem, SuperComputador.AtacarOAlvo);
			p.AlvoId = rival.Id;
			List<string> comAlvo = Usar(p, SuperComputador.VerboOrdem, SuperComputador.AtacarOAlvo);
			bool caca = GuiarODroide(droide, est, out ServerPlayer? presa, out _) && presa == rival;
			SuperComputador.DroideSurdoDeTeste = true;
			bool surdo;
			try { GuiarODroide(droide, est, out ServerPlayer? presaDoSurdo, out _); surdo = presaDoSurdo == null; }
			finally { SuperComputador.DroideSurdoDeTeste = false; }
			rival.Ficha.KO = true;
			GuiarODroide(droide, est, out ServerPlayer? caido, out _);
			rival.Ficha.KO = false;
			p.AlvoId = 0;

			// "Atacar o mais proximo": o droide vai pro fim do corredor, com um IRMAO colado nele -- o mais perto de todos e o
			// irmao (que ele pula), depois o vizinho e o intruso, e so entao o rival.
			Usar(p, SuperComputador.VerboOrdem, SuperComputador.AtacarOProximo);
			droide.Pos = c + new Vec2(8 * T, 0);
			ServerPlayer? irmao = DroidesDoP().FirstOrDefault(x => x != droide);
			if (irmao != null) irmao.Pos = droide.Pos + new Vec2(T / 2f, 0);
			GuiarODroide(droide, est, out ServerPlayer? oMaisPerto, out _);
			Usar(p, SuperComputador.VerboOrdem, SuperComputador.Seguir);
			GuiarODroide(droide, est, out ServerPlayer? depoisDeSeguir, out _);
			AfirmarV21("**AS ORDENS SAEM DO COMPUTADOR** (`Follow`, `Attack Target`, `Attack Nearest`): sem alvo marcado a ordem de atacar e recusada; com ele os droides o perseguem, e o largam caido; \"o mais proximo\" e quem esta mais perto do droide que nao seja o dono nem um droide irmao; e \"seguir\" os faz largar tudo; (defeito injetado: o droide nao obedece a ordem de atacar) e com o defeito ele nao persegue ninguem",
					   Disse(semAlvo, "marque um alvo") && Disse(comAlvo, "Rival") && caca && surdo && caido == null
					   && irmao != null && (oMaisPerto == vizinho || oMaisPerto == intruso) && depoisDeSeguir == null,
					   $"{string.Join(" | ", semAlvo)} | {string.Join(" | ", comAlvo)} | caca {caca}; surdo (defeito) {surdo}; caido {caido?.Name ?? "largou"}; o mais perto {oMaisPerto?.Name ?? "ninguem"}; depois de seguir {depoisDeSeguir?.Name ?? "ninguem"}");

			int quantos = DroidesDe(p.Id).Count;
			List<string> destruiu = Usar(p, SuperComputador.VerboOrdem, SuperComputador.Destruir);
			List<string> semNenhum = Usar(p, SuperComputador.VerboOrdem, SuperComputador.Seguir);
			AfirmarV21("...e \"destruir\" apaga os droides de QUEM MANDA (os corpos saem do mundo), e so os dele -- sem droide de pe, as ordens dizem que nao ha a quem dar",
					   quantos == SuperComputador.MaxDeDroides + 1 && DroidesDe(p.Id).Count == 0 && !_players.ContainsKey(droide.Id)
					   && Disse(destruiu, "destruídos") && Disse(semNenhum, "não tem droide"),
					   $"{quantos} -> {DroidesDe(p.Id).Count}; {string.Join(" | ", destruiu)} | {string.Join(" | ", semNenhum)}");

			// ---------------------------------------------------------------- as melhorias
			double zeni0 = p.Ficha.Zeni, recursos0 = p.Ficha.Recursos;
			pc.Armadura = pc.ArmaduraMax * 0.5;
			Usar(p, SuperComputador.VerboMelhorar, Pago(SuperComputador.Conserto));
			bool consertou = pc.Armadura >= pc.ArmaduraMax && Math.Abs(p.Ficha.Zeni - zeni0) < 0.5;
			double armaduraDeAntes = pc.ArmaduraMax;
			Usar(p, SuperComputador.VerboMelhorar, Pago(SuperComputador.Blindagem));
			bool blindou = Math.Abs(pc.ArmaduraMax - TetoDe(p) * SuperComputador.FatorDaBlindagem) <= 1e-9 * Math.Max(pc.ArmaduraMax, 1)
						   && pc.ArmaduraMax > armaduraDeAntes && Math.Abs(pc.Armadura - pc.ArmaduraMax) < 1e-9
						   && Math.Abs(zeni0 - p.Ficha.Zeni - SuperComputador.CustoDaBlindagem) < 0.5;
			List<string> jaBlindado = Usar(p, SuperComputador.VerboMelhorar, Pago(SuperComputador.Blindagem));
			bool naoCobrouDeNovo = Math.Abs(zeni0 - p.Ficha.Zeni - SuperComputador.CustoDaBlindagem) < 0.5;
			byte bitsDeAntes = MelhoriasFeitas(pc);
			Usar(p, SuperComputador.VerboMelhorar, Pago(SuperComputador.Ressurreicao, emRecursos: true));
			bool comprou = pc.Tech!.Ligado(SuperComputador.ChaveRessurreicao)
						   && Math.Abs(recursos0 - p.Ficha.Recursos - 2 * SuperComputador.CustoDaRessurreicao) < 0.5;
			List<string> jaTem = Usar(p, SuperComputador.VerboMelhorar, Pago(SuperComputador.Ressurreicao));
			Interacoes.Acao[] pagina = Interacoes.De(SuperComputador.Tipo + "/melhorias");
			bool saiDoMenu = bitsDeAntes == 0 && MelhoriasFeitas(pc) == Interacoes.MelhoriaUnicaFeita
							 && pagina.Any(a => a.Arg == "melhorias/ressurreicao")
							 && !pagina.Where(a => !Interacoes.SaiDoMenu(SuperComputador.Tipo, a, MelhoriasFeitas(pc))).Any(a => a.Arg == "melhorias/ressurreicao");
			AfirmarV21("**AS MELHORIAS** (`Upgrade`): o conserto e de graca (`healDamage(maxarmor)`); a blindagem custa 50.000 e leva a armadura a 1,2x o teto de tecnologia de quem paga (`maxarmor = usr.intBPcap*1.2`) -- e ja blindado nao cobra de novo; a ressurreicao de androide custa 75.000 (o dobro em recursos), e uma vez comprada sai do menu deste computador",
					   consertou && blindou && Disse(jaBlindado, "já está acima") && naoCobrouDeNovo && comprou && Disse(jaTem, "já tem")
					   && Math.Abs(zeni0 - p.Ficha.Zeni - SuperComputador.CustoDaBlindagem) < 0.5 && saiDoMenu,
					   $"consertou {consertou}; blindou {blindou} ({armaduraDeAntes:0.##} -> {pc.ArmaduraMax:0.##}, teto {TetoDe(p):0.##}); zeni {zeni0:0} -> {p.Ficha.Zeni:0}; recursos {recursos0:0} -> {p.Ficha.Recursos:0}; sai do menu {saiDoMenu} | {string.Join(" | ", jaBlindado)} | {string.Join(" | ", jaTem)}");

			// ---------------------------------------------------------------- a ressurreicao de androide
			VigiaDoComputadorSemPeerDeTeste = true;
			p.Ficha.AndroideInfinito = true;
			VigiarOsAndroides();
			bool vivoNadaMuda = !p.Ficha.dead;
			p.Pos = c + new Vec2(8 * T, 0);
			bool morreu = p.Combate!.Morrer() && p.Ficha.dead;
			List<string> aoVoltar = Ouvir(VigiarOsAndroides);
			bool voltou = !p.Ficha.dead && Vec2.Distance(p.Pos, new Vec2(pc.X, pc.Y)) < 2.5f * T;

			// O HUMANO QUE CIFROU O PROPRIO COMPUTADOR, com a melhoria comprada: ele nao e androide, e fica morto.
			Obra doVizinho = Por(c + new Vec2(6 * T, T), vizinho);
			vizinho.Pos = c + new Vec2(6 * T, 0);
			Usar(vizinho, SuperComputador.VerboCifrar, "bulma");
			Usar(vizinho, SuperComputador.VerboMelhorar, Pago(SuperComputador.Ressurreicao));
			bool humanoComprou = doVizinho.Tech?.Ligado(SuperComputador.ChaveRessurreicao) == true;
			bool humanoMorreu = vizinho.Combate!.Morrer();
			VigiarOsAndroides();
			bool humanoFicaMorto = humanoMorreu && vizinho.Ficha.dead;
			SuperComputador.RefazQualquerUmDeTeste = true;
			bool semComputadorNaoVolta, defeitoRefaz;
			try
			{
				// (o computador derrubado: sem ele de pe, nem o defeito traz ninguem)
				_noChao.Remove(doVizinho);
				VigiarOsAndroides();
				semComputadorNaoVolta = vizinho.Ficha.dead;
				_noChao.Add(doVizinho);
				VigiarOsAndroides();
				defeitoRefaz = !vizinho.Ficha.dead;
			}
			finally { SuperComputador.RefazQualquerUmDeTeste = false; }

			// DECIFRADO, o computador nao responde por ninguem.
			p.Pos = c;
			List<string> decifrou = Usar(p, SuperComputador.VerboDecifrar);
			bool semChave = pc.Tech!.Txt(SuperComputador.ChaveDaChave).Length == 0 && pc.Tech.Txt(SuperComputador.ChaveConta).Length == 0
							&& pc.Tech.Ligado(SuperComputador.ChaveRessurreicao);
			bool morreuDeNovo = p.Combate.Morrer();
			VigiarOsAndroides();
			bool decifradoNaoRefaz = morreuDeNovo && p.Ficha.dead;
			AfirmarV21("**A RESSURREICAO DE ANDROIDE** (`TestDeathRegen()`: *\"Your memories have been saved and implanted into a new body.\"*): com ele vivo nada acontece; morto, o computador cifrado por ele (e com a melhoria) o refaz AO LADO de si na vigia seguinte; o humano que cifrou e comprou a mesma melhoria fica morto (`if(Race==\"Android\")`); derrubado o computador ninguem volta; e decifrado (`controller=null`) ele deixa de responder pelo androide -- a melhoria continua comprada; (defeito injetado: o computador refaz quem nao e androide) e com o defeito o humano volta",
					   vivoNadaMuda && morreu && voltou && Disse(aoVoltar, "corpo novo") && humanoComprou && humanoFicaMorto
					   && semComputadorNaoVolta && defeitoRefaz && Disse(decifrou, "decifrado") && semChave && decifradoNaoRefaz,
					   $"vivo nada muda {vivoNadaMuda}; morreu {morreu}; voltou {voltou} | {string.Join(" | ", aoVoltar)} | humano comprou {humanoComprou}, fica morto {humanoFicaMorto}; sem computador {semComputadorNaoVolta}; defeito refaz {defeitoRefaz}; {string.Join(" | ", decifrou)}; sem chave {semChave}; decifrado nao refaz {decifradoNaoRefaz}");
		}
		finally
		{
			SuperComputador.AbreSemAChaveDeTeste = SuperComputador.SemTetoDeDroidesDeTeste = false;
			SuperComputador.RefazQualquerUmDeTeste = SuperComputador.DroideSurdoDeTeste = false;
			VigiaDoComputadorSemPeerDeTeste = false;
			foreach (int id in _droides.Keys.ToList()) DesfazerDroide(id, null);
			foreach (ServerPlayer corpo in corpos) _computadoresAbertos.Remove(corpo.Id);
			foreach (Obra o in obras) _noChao.Remove(o);
			GravarMundo();
			DesfazerV2([.. corpos]);
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// X10) A BANCADA DE CRIACAO -- o lote T7
	// =====================================================================
	private void ABancadaDeCriacaoV21()
	{
		GD.Print("[v021] -- X10) A TECH PORTADA, lote 7: a bancada de criacao -- a vitrine de roupas, o pente e a tinta de olhos (item 13)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		const float T = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(10);
		var obras = new List<Obra>();
		var corpos = new List<ServerPlayer>();
		ServerPlayer Nasce(string nome, Vec2 onde, string raca = "Human")
		{
			ServerPlayer novo = ForjarV2(nome, onde, 1_000, raca);
			novo.Conta = "bancada_v021_" + nome.ToLowerInvariant();
			novo.Ficha.Zeni = 10_000;
			corpos.Add(novo);
			return novo;
		}
		ServerPlayer p = Nasce("Costureiro", c);
		ServerPlayer saiyajin = Nasce("Saiyajin", c + new Vec2(3 * T, 0), "Saiyan");
		ServerPlayer namek = Nasce("Namek", c + new Vec2(4 * T, 0), "Namekian");

		List<string> Usar(ServerPlayer quem, string verbo, string arg = "") => Ouvir(() => ComandoDeInteracao(quem, verbo, arg));
		List<string> Fazer(ServerPlayer quem, string acao, string arg) => Ouvir(() => ComandoDeItem(quem, "item_" + acao, arg));
		bool Disse(List<string> falas, string trecho) => falas.Exists(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));
		const string gi = "Clothes_GiTop", luvas = "Clothes_Gloves", giVermelho = RoupaGuardada.Prefixo + gi + "|FF0000";

		try
		{
			AfirmarV21("(palco) a vitrine tem as 99 pecas do original com preco, o Gi Top custa 5 e a capa do Rei Vegeta 100.000 -- e as luvas (sem preco no `pickclothes()` de la) ficam de fora",
					   BancadaDeCriacao.Pecas == 99 && BancadaDeCriacao.PrecoDaRoupa(gi) == 5 && BancadaDeCriacao.PrecoDaRoupa("Clothes_KingVegetaCape") == 100_000
					   && BancadaDeCriacao.PrecoDaRoupa(luvas) == null && CatalogoDeItens.Visual?.Peca(luvas) != null,
					   $"{BancadaDeCriacao.Pecas} pecas; Gi Top {BancadaDeCriacao.PrecoDaRoupa(gi)}; luvas {BancadaDeCriacao.PrecoDaRoupa(luvas)?.ToString() ?? "sem preco"}");

			// ---------------------------------------------------------------- o parafuso
			Obra bancada = ObraDeBancadaV21(BancadaDeCriacao.Tipo, c + new Vec2(0, -T), p, aparafusada: false);
			obras.Add(bancada);
			List<string> solta = Usar(p, BancadaDeCriacao.VerboRoupa, gi);
			bool soltaNaoFaz = p.Mochila.Pilhas.Count(x => x.Quantidade > 0) == 0 && Math.Abs(p.Ficha.Zeni - 10_000) < 0.5;
			BancadaDeCriacao.TrabalhaSoltaDeTeste = true;
			bool defeitoFazSolta;
			try { Usar(p, BancadaDeCriacao.VerboRoupa, gi); defeitoFazSolta = p.Mochila.Quantos(RoupaGuardada.Prefixo + gi + "|") == 1; }
			finally { BancadaDeCriacao.TrabalhaSoltaDeTeste = false; }
			p.Mochila.Pilhas.Clear();
			p.Ficha.Zeni = 10_000;
			bancada.Aparafusada = true;

			// ---------------------------------------------------------------- a vitrine
			Usar(p, BancadaDeCriacao.VerboRoupa, gi + "/FF0000");
			bool fez = p.Mochila.Quantos(giVermelho) == 1 && Math.Abs(10_000 - p.Ficha.Zeni - 5) < 0.5
					   && CatalogoDeItens.Get(giVermelho)?.AcoesDoItem.Contains(RoupaGuardada.AcaoVestir) == true;
			double depoisDoGi = p.Ficha.Zeni;
			List<string> foraDaVitrine = Usar(p, BancadaDeCriacao.VerboRoupa, luvas);
			List<string> corErrada = Usar(p, BancadaDeCriacao.VerboRoupa, gi + "/XYZ");
			p.Ficha.Zeni = 3;
			List<string> semZeni = Usar(p, BancadaDeCriacao.VerboRoupa, gi);
			bool nadaMudou = p.Mochila.Pilhas.Count(x => x.Quantidade > 0) == 1 && Math.Abs(p.Ficha.Zeni - 3) < 0.5;
			BancadaDeCriacao.FazQualquerPecaDeGracaDeTeste = true;
			bool defeitoFazAsLuvas;
			try { Usar(p, BancadaDeCriacao.VerboRoupa, luvas); defeitoFazAsLuvas = p.Mochila.Quantos(RoupaGuardada.Prefixo + luvas + "|") == 1 && Math.Abs(p.Ficha.Zeni - 3) < 0.5; }
			finally { BancadaDeCriacao.FazQualquerPecaDeGracaDeTeste = false; }
			AfirmarV21("**A BANCADA FAZ ROUPA** (`Craft()` -> \"Clothes\": *\"This will cost [N] zenni, accept?\"*): solta ela recusa (*\"You need to bolt [name] before you can use it!\"*); aparafusada, a peca da vitrine sai na cor pedida, pelo preco de la, pra mochila -- e e roupa de vestir; a peca fora da vitrine, a cor que nao existe e o bolso vazio sao recusados sem cobrar nada; (defeitos injetados: a bancada trabalha solta / faz qualquer folha de graca) e com cada defeito a recusa correspondente some",
					   Disse(solta, "aparafuse") && soltaNaoFaz && defeitoFazSolta && fez && Disse(foraDaVitrine, "não sabe fazer") && Disse(corErrada, "seis dígitos")
					   && Math.Abs(depoisDoGi - 9_995) < 0.5 && semZeni.Count > 0 && nadaMudou && defeitoFazAsLuvas,
					   $"{string.Join(" | ", solta)}; solta nao faz {soltaNaoFaz}; defeito faz solta {defeitoFazSolta}; fez {fez} (zeni {depoisDoGi:0}) | {string.Join(" | ", foraDaVitrine)} | {string.Join(" | ", corErrada)} | {string.Join(" | ", semZeni)}; nada mudou {nadaMudou}; defeito faz as luvas {defeitoFazAsLuvas}");

			// ---------------------------------------------------------------- os itens
			p.Mochila.Pilhas.Clear();
			p.Ficha.Zeni = 1_000;
			Usar(p, BancadaDeCriacao.VerboItem, BancadaDeCriacao.Pente);
			Usar(p, BancadaDeCriacao.VerboItem, BancadaDeCriacao.TintaDeOlhos);
			List<string> naoFaz = Usar(p, BancadaDeCriacao.VerboItem, "Trash_Bin");
			AfirmarV21("...e faz o PENTE e a TINTA DE OLHOS, cem zeni cada (`if(usr.zenni>=100) usr.zenni-=100`), que vao pra mochila com a acao de cada um; o que ela nao faz (a lixeira, o tear) e recusado",
					   p.Mochila.Quantos(BancadaDeCriacao.Pente) == 1 && p.Mochila.Quantos(BancadaDeCriacao.TintaDeOlhos) == 1
					   && Math.Abs(1_000 - p.Ficha.Zeni - 2 * BancadaDeCriacao.CustoDoItem) < 0.5 && Disse(naoFaz, "não faz isso")
					   && CatalogoDeItens.Get(BancadaDeCriacao.Pente)?.AcoesDoItem.Contains(BancadaDeCriacao.AcaoPentear) == true
					   && CatalogoDeItens.Get(BancadaDeCriacao.TintaDeOlhos)?.AcoesDoItem.Contains(BancadaDeCriacao.AcaoTingirOsOlhos) == true,
					   $"pente {p.Mochila.Quantos(BancadaDeCriacao.Pente)}; tinta {p.Mochila.Quantos(BancadaDeCriacao.TintaDeOlhos)}; zeni {p.Ficha.Zeni:0} | {string.Join(" | ", naoFaz)}");

			// ---------------------------------------------------------------- o pente
			string cabeloDeAntes = p.Visual.Cabelo;
			List<string> naoExiste = Fazer(p, BancadaDeCriacao.AcaoPentear, BancadaDeCriacao.Pente + "/PenteadoQueNaoExiste");
			bool recusouOInexistente = p.Visual.Cabelo == cabeloDeAntes;
			Fazer(p, BancadaDeCriacao.AcaoPentear, BancadaDeCriacao.Pente + "/Goku/FF0000");
			bool penteou = p.Visual.Cabelo == "Goku" && p.Visual.CorCabelo is { R: 255, G: 0, B: 0 } && p.Mochila.Quantos(BancadaDeCriacao.Pente) == 1;
			Fazer(p, BancadaDeCriacao.AcaoPentear, BancadaDeCriacao.Pente + "/Vegeta");
			bool semCorMantem = p.Visual.Cabelo == "Vegeta" && p.Visual.CorCabelo is { R: 255, G: 0, B: 0 };

			saiyajin.Mochila.Guardar(BancadaDeCriacao.Pente);
			Fazer(saiyajin, BancadaDeCriacao.AcaoPentear, BancadaDeCriacao.Pente + "/Raditz/00FF00");
			bool saiyajinSoPenteia = saiyajin.Visual.Cabelo == "Raditz" && saiyajin.Visual.CorCabelo == null;
			BancadaDeCriacao.SaiyajinTingeDeTeste = true;
			bool defeitoTinge;
			try { Fazer(saiyajin, BancadaDeCriacao.AcaoPentear, BancadaDeCriacao.Pente + "/Raditz/00FF00"); defeitoTinge = saiyajin.Visual.CorCabelo is { G: 255 }; }
			finally { BancadaDeCriacao.SaiyajinTingeDeTeste = false; saiyajin.Visual.CorCabelo = null; }

			namek.Mochila.Guardar(BancadaDeCriacao.Pente);
			List<string> semCabelo = Fazer(namek, BancadaDeCriacao.AcaoPentear, BancadaDeCriacao.Pente + "/Goku");
			AfirmarV21("**O PENTE MUDA O PENTEADO E A COR** (`Change_Hair()` -> `Hair(1)`): o penteado que nao existe e recusado; o que existe troca o cabelo e a cor (sem cor no pedido, fica a que ja tinha), e o pente NAO se gasta; o Saiyajin troca o penteado mas nao pega tinta (*\"Saiyan hair created from here will always be black.\"*); e o corpo sem cabelo ouve que nao tem o que pentear; (defeito injetado: o pente tinge o cabelo do Saiyajin) e com o defeito a cor pega",
					   Disse(naoExiste, "não existe") && recusouOInexistente && penteou && semCorMantem && saiyajinSoPenteia && defeitoTinge
					   && Disse(semCabelo, "não tem cabelo") && namek.Visual.Cabelo != "Goku",
					   $"{string.Join(" | ", naoExiste)}; penteou {penteou} ({p.Visual.Cabelo}, {p.Visual.CorCabelo}); sem cor mantem {semCorMantem}; saiyajin so penteia {saiyajinSoPenteia}; defeito tinge {defeitoTinge} | {string.Join(" | ", semCabelo)}");

			// ---------------------------------------------------------------- a tinta de olhos
			List<string> corRuim = Fazer(p, BancadaDeCriacao.AcaoTingirOsOlhos, BancadaDeCriacao.TintaDeOlhos + "/verde");
			bool olhoDeAntes = p.Visual.CorOlho == null;
			Fazer(p, BancadaDeCriacao.AcaoTingirOsOlhos, BancadaDeCriacao.TintaDeOlhos + "/00FF00");
			AfirmarV21("**A TINTA MUDA A COR DOS OLHOS** (`Change_Eye_Color()`): a cor que nao existe e recusada, a que existe pega -- e a tinta nao se gasta",
					   Disse(corRuim, "seis dígitos") && olhoDeAntes && p.Visual.CorOlho is { R: 0, G: 255, B: 0 } && p.Mochila.Quantos(BancadaDeCriacao.TintaDeOlhos) == 1,
					   $"{string.Join(" | ", corRuim)}; olho {p.Visual.CorOlho}; tinta {p.Mochila.Quantos(BancadaDeCriacao.TintaDeOlhos)}");
		}
		finally
		{
			BancadaDeCriacao.TrabalhaSoltaDeTeste = BancadaDeCriacao.FazQualquerPecaDeGracaDeTeste = BancadaDeCriacao.SaiyajinTingeDeTeste = false;
			foreach (Obra o in obras) _noChao.Remove(o);
			DesfazerV2([.. corpos]);
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// X11) OS ITENS DE MAGIA FECHAM NA ABA TECH -- o lote T8
	// =====================================================================
	private void OsItensDeMagiaFechamV21()
	{
		GD.Print("[v021] -- X11) A TECH QUE AINDA NAO SE VENDE: os sete itens de magia do catalogo fecham, em vez de serem vendidos sem fazer nada (item 13)");
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		Vec2 c = TerraFirmeG12(6);
		ServerPlayer p = ForjarV2("Mago", c, 1_000);
		p.Conta = "bancada_v021_mago";
		try
		{
			AfirmarV21("(palco) ha catalogo de construcoes, e os sete itens de magia do `Magitech.dm` estao nele",
					   _obras != null && TechFechada.Todas.Length == 7 && TechFechada.Todas.All(id => _obras.Get(id) is { Construivel: true }),
					   _obras == null ? "sem catalogo" : string.Join(", ", TechFechada.Todas.Where(id => _obras.Get(id) == null)));
			if (_obras == null) return;

			p.Ficha.techskill = 100;
			p.Ficha.Zeni = p.Ficha.Recursos = 1_000_000;
			var recusas = new List<string>();
			foreach (string id in TechFechada.Todas)
			{
				List<string> falas = Ouvir(() => Construir(p, id));
				if (p.Mochila.Quantos(id) != 0 || !falas.Exists(f => f.Contains("ainda não existe neste jogo") && f.Contains("magia")))
					recusas.Add($"{id}: {string.Join(" | ", falas)}");
			}
			List<(Construcao Obra, RecusaObra Motivo)> ofertas = _obras.Ofertas(p.Ficha, p.Race);
			bool naLista = TechFechada.Todas.All(id => ofertas.Exists(o => o.Obra.Id == id && o.Motivo == RecusaObra.ForaDoPort));
			int fechadasNaLista = ofertas.Count(o => o.Motivo == RecusaObra.ForaDoPort);
			// (o controle: o que NAO esta fechado continua se fabricando -- a bancada de criacao, do lote T7)
			Ouvir(() => Construir(p, BancadaDeCriacao.Tipo));
			bool oRestoVende = p.Mochila.Quantos(BancadaDeCriacao.Tipo) == 1;
			double zeniDepois = p.Ficha.Zeni;
			AfirmarV21("**OS SETE ITENS DE MAGIA NAO SE VENDEM MAIS COMO ITEM QUE NAO FAZ NADA**: com tecnologia e zeni de sobra, fabricar o giz, a varinha, o cajado, o preditor, a peneira, o cristal e a bolsa e recusado dizendo que ainda nao existem neste jogo (pedem o sistema de magia), sem cobrar -- e os sete continuam NA LISTA da aba Tech, com esse motivo; o resto do catalogo continua se fabricando",
					   recusas.Count == 0 && naLista && fechadasNaLista == 7 && oRestoVende
					   && Math.Abs(1_000_000 - zeniDepois - (_obras.Get(BancadaDeCriacao.Tipo)?.Custo ?? 0)) < 0.5,
					   $"recusas erradas: {recusas.Count} {string.Join(" || ", recusas)}; na lista {naLista} ({fechadasNaLista} fechadas); o resto vende {oRestoVende}; zeni {zeniDepois:0}");

			TechFechada.TudoAbertoDeTeste = true;
			try
			{
				Ouvir(() => Construir(p, "Chalk"));
				AfirmarV21("(defeito injetado: nada esta fechado) o giz volta a ser fabricado -- e e um item de mochila sem acao nenhuma, que e o que se vendia",
						   p.Mochila.Quantos("Chalk") == 1 && p.Ficha.Zeni < zeniDepois && CatalogoDeItens.Get("Chalk") is { AcoesDoItem.Length: 0 },
						   $"giz {p.Mochila.Quantos("Chalk")}; zeni {zeniDepois:0} -> {p.Ficha.Zeni:0}; acoes [{string.Join(", ", CatalogoDeItens.Get("Chalk")?.AcoesDoItem ?? [])}]");
			}
			finally { TechFechada.TudoAbertoDeTeste = false; }
		}
		finally
		{
			TechFechada.TudoAbertoDeTeste = false;
			DesfazerV2(p);
			LimparTudoDaBancada();
		}
	}

	/// <summary>Um ponto de chao firme com agua no tile ao lado, na zona da bancada. Nulo se a zona nao tem agua.</summary>
	private Vec2? BeiraDaAguaV21()
	{
		if (_pjMapa is not { } mapa) return null;
		const float T = ZoneCollision.TileSize;
		for (int y = 8; y < mapa.Height - 8; y++)
			for (int x = 8; x < mapa.Width - 8; x++)
				if (!mapa.BlockedCell(x, y) && !mapa.EhAgua(x, y) && !mapa.EhNuvem(x, y) && mapa.EhAgua(x + 1, y))
					return new Vec2(x * T + T / 2f, y * T + T / 2f);
		return null;
	}
}
