using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.Social;
using Jandirus.Core.Verbos;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--filhosteste` -- OS FILHOS (adendo 42, 2026-10-17). O dono: *"faça com q seja possivel ter filhos no jogo, entao quando
/// um player chega em nivel relaçao "amor" com outro jogador do sexo oposto, eles possam ter filho"* (o pedido inteiro esta no
/// cabecalho de `GameServer.Filhos.cs`).
///
/// Os pais e os filhos sao JOGADORES que entram pela porta (clientes crus): o pedido e de jogador pra jogador, e o parto e o
/// `CreateChar` de producao.
///
///   A. AS REGRAS DO CORE: quem cruza, quem gera por gravidez, a classe mais rara (e a traducao entre as linhagens do Saiyajin), as
///      linhagens do filho, o BP do pai, e o recem-nascido que nao morre de velhice.
///   B. O PEDIDO: sem amor, nada; amor de um lado so, nada (o defeito `AmorDeUmLadoSoDeTeste` deixa passar); amor dos dois, o verb
///      acende, o pedido vira cartao na tela dela, o "nao" o tira e o "sim" concebe -- um meio-Saiyajin, com a foto do poder.
///   C. AS RECUSAS: mesmo sexo, a mae que ja espera, longe, morto.
///   D. A OFERTA: a conta nova recebe o bebe no fim da lista de slots (o leitor de producao).
///   E. O PARTO: a raca e a idade da ficha nao valem (Half-Saiyan, zero anos), a linhagem escolhida, do lado da mae, com o poder dos
///      pais -- e o bebe sai da fila. Quem chega depois com o mesmo bebe e recusado.
///   F. DOIS SAIYAJINS de linhagens diferentes: as duas linhagens no bebe; no MODO DE RACAS ALEATORIAS o filho nasce Saiyajin, com
///      a classe mais rara dos pais na linhagem que escolheu. E o CONTRA-EXEMPLO `FilhoSemHerancaDeTeste`.
///   G. A RESPOSTA DO DONO (2026-10-17): o Namekuseijin poe um OVO SOZINHO (jovem demais nao poe; um por vez; o Humano nao poe), a
///      oferta do ovo viaja e ele choca pela porta, com o cla de quem o pos, do lado dele; o recem-nascido ainda nao tem filho, e o
///      meio-Saiyajin crescido tem, com a Humana -- outro meio-Saiyajin; o androide de absorcao nao tem filho, e o de energia infinita,
///      com a Saiyajin, gera um meio-Saiyajin. Cada regra nova com o CONTRA-EXEMPLO `Filhos.FilhosComoAntesDeTeste` (as regras de antes).
///      E O OVO NO CHAO (o dono, 2026-10-17: *"tinha um sprite de ovo"*): a obra do ovo do lado de quem o pos, com a arte do DM e no
///      `mundo.json`; o filho nasce do lado do OVO (com quem o pos longe) e o ovo some; um soco quebra o ovo e o bebe se perde. Com o
///      CONTRA-EXEMPLO `Filhos.OvoSemCorpoDeTeste` (o ovo de antes, guardado como a gravidez).
///   H. O DISCO: o bebe a caminho volta igual do `bebes.json`.
/// </summary>
public sealed partial class GameServer
{
	private bool _filhosRodando, _filhosJaRodou;
	private int _flOk, _flFalhou;

	private static readonly string[] ContasDaBancadaDosFilhos =
	[
		"bancada_filhos_goku", "bancada_filhos_chichi", "bancada_filhos_bulma", "bancada_filhos_gohan", "bancada_filhos_atrasado",
		"bancada_filhos_vegeta", "bancada_filhos_gine", "bancada_filhos_tarble", "bancada_filhos_cru",
		"bancada_filhos_piccolo", "bancada_filhos_dende", "bancada_filhos_videl", "bancada_filhos_lapis",
	];

	/// <summary>
	/// O ROBO `--diagfilho` PRECISA DE UM BEBE NA FILA, e a concepcao tem a bancada dela (esta): o bebe entra direto, com a foto que o
	/// "sim" tiraria. Devolve o id.
	/// </summary>
	internal int PorUmBebeDeTeste(string pai, string mae, string racaDoPai, string racaDaMae, double bpPai, double bpMae)
	{
		string raca = Filhos.RacaDoFilho(racaDoPai, racaDaMae);
		string linhaPai = Filhos.LinhagemDe(racaDoPai, "Normal", "Saiyan"), linhaMae = Filhos.LinhagemDe(racaDaMae, "Normal", "Saiyan");
		var b = new BebeACaminho
		{
			Id = _bebes.Proximo++, ConcebidoEm = NowMs(), Pai = pai, Mae = mae, RacaDoPai = racaDoPai, RacaDaMae = racaDaMae,
			ClasseDoPai = "Normal", ClasseDaMae = "Normal", LinhagemDoPai = linhaPai, LinhagemDaMae = linhaMae,
			BpDoPai = Filhos.BpDoPai(bpPai), BpDaMae = bpMae, Raca = raca, Linhagens = Filhos.LinhagensDoFilho(raca, linhaPai, linhaMae),
		};
		_bebes.Bebes.Add(b);
		SalvarBebes();
		return b.Id;
	}

	/// <summary>O robo `--diagfilho` (a tela do OVO): um ovo na fila, com a foto que o "Pôr um ovo" tiraria. Devolve a oferta dele.</summary>
	internal OfertaDeBebe PorUmOvoDeTeste(string quem, string raca, string classe, double bp)
	{
		BebeACaminho ovo = Filhos.Ovo(_bebes.Proximo++, NowMs(), quem, "", raca, classe, "", bp);
		_bebes.Bebes.Add(ovo);
		SalvarBebes();
		return ovo.Oferta;
	}

	/// <summary>O robo `--diagovo`: o ovo que este corpo pos (o bebe a caminho dele), ou nulo.</summary>
	internal BebeACaminho? OvoDoCorpoDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? BebeDaMae(pl.Assinatura) : null;

	/// <summary>O robo `--diagovo`: a celula do ovo deste corpo no chao, e o id da obra -- ou nulo.</summary>
	internal (int Cx, int Cy, int Obra)? OvoNoChaoDeTeste(int id)
	{
		if (OvoDoCorpoDeTeste(id) is not { } b || OvoNoChao(b) is not { } o) return null;
		(int cx, int cy) = Jandirus.Core.Tech.CatalogoDeObras.Celula(o.X, o.Y);
		return (cx, cy, o.Id);
	}

	/// <summary>O robo `--diagovo`: um soco no ovo deste corpo, dado por ele mesmo, pela pancada de producao (`Estragar`) -- e o que se ouviu.</summary>
	internal List<string> QuebrarOOvoDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || OvoDoCorpoDeTeste(id) is not { } b || OvoNoChao(b) is not { } o) return [];
		return Ouvir(() => Estragar(o, 1e9, pl));
	}

	/// <summary>O robo `--diagovo`: o ovo do contra-exemplo (o sem corpo) sai da fila.</summary>
	internal void TirarOOvoDoCorpoDeTeste(int id)
	{
		if (OvoDoCorpoDeTeste(id) is { } b) TirarOBebeDaFila(b);
	}

	/// <summary>O robo `--diagfilho`: o ovo que so serviu pra tela sai da fila (o parto dele e da `--filhosteste`).</summary>
	internal void TirarOBebeDaFilaDeTeste(int id)
	{
		if (_bebes.Bebes.Find(b => b.Id == id) is { } b) TirarOBebeDaFila(b);
	}

	/// <summary>O robo `--diagfilho`: quantos bebes a caminho, e o que nasceu nesta conta (raca, idade, classe, potencial escondido).</summary>
	internal int BebesNaFilaDeTeste => _bebes.Bebes.Count;

	internal (string Raca, int Idade, string Classe, double Potencial)? NascidoDeTeste(string conta) =>
		Jogadores.FirstOrDefault(p => string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase)) is { } pl
			? (pl.Ficha.Race, pl.Idade, pl.Ficha.Class, pl.Ficha.hiddenpotential) : null;

	private void AfirmarFl(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _flOk++; GD.Print($"[filhos]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _flFalhou++; GD.PrintErr($"[filhos]   FALHA {nome}   [{detalhe}]"); }
	}

	private void ApagarContasDosFilhos()
	{
		if (_store == null) return;
		foreach (string conta in ContasDaBancadaDosFilhos)
			try
			{
				string caminho = System.IO.Path.Combine(_store.Pasta, conta + ".json");
				if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
			}
			catch (Exception e) { GD.PushWarning($"[filhos] nao apaguei '{conta}': {e.Message}"); }
	}

	public void RodarBancadaDosFilhos(int porta)
	{
		if (_filhosRodando || _filhosJaRodou) return;
		_filhosRodando = _filhosJaRodou = true;
		_flOk = _flFalhou = 0;
		GD.Print("[filhos] ================ OS FILHOS (dono, 2026-10-17) ================");

		const float T = ZoneCollision.TileSize;
		ApagarContasDosFilhos();
		_bebes = new BebesNoDisco();
		var c = new ClienteCruDeTeste[ContasDaBancadaDosFilhos.Length];
		for (int i = 0; i < c.Length; i++) c[i] = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, c);
		ModoDeRacas modoAntes = ModoDeRacas;
		try
		{
			// ------------------------------------------------------------ as pecas
			CharacterDraft Ficha(string nome, string raca, string genero, string linhagem = "") => new()
			{
				Name = nome, Race = raca, Planet = SorteioDeRaca.PlanetaDaRaca(raca), Gender = genero, Age = 18, ChosenClass = linhagem,
				Porte = "Medium", Backstory = "Personagem da bancada dos filhos, criado por um cliente cru.",
			};
			ServerPlayer? Entrar(int i, CharacterDraft ficha)
			{
				if (!mesa.Entra(c[i]) || !mesa.Loga(c[i], ContasDaBancadaDosFilhos[i])) return null;
				c[i].CriarPersonagem(0, ficha);
				return mesa.Bombeia(() => c[i].NoMundo || c[i].Recusa != null, 8000) && c[i].NoMundo ? mesa.CorpoDe(ContasDaBancadaDosFilhos[i]) : null;
			}
			// O AMOR PELA PORTA DE PRODUCAO: a ficha da pessoa com o convivio que paga a declaracao, e o `Declarar` de sempre.
			void Amar(ServerPlayer de, ServerPlayer por)
			{
				de.Social.Fotografar(por.Assinatura, por.Name, por.Race, por.Genero, por.Idade, NowMs());
				de.Social.Ficha(por.Assinatura)!.Familiaridade = Convivio.FamiliaridadeExigida(Relacao.Amor);
				de.Social.Declarar(por.Assinatura, Relacao.Amor);
			}
			// LADO A LADO E DE FRENTE: o pedido sai pra quem esta na frente (`QuemEstaNaFrente`, o alcance do soco).
			void LadoALado(ServerPlayer a, ServerPlayer b)
			{
				if (b.Zone.Hash != a.Zone.Hash) MoveToZone(b.Id, a.Zone, a.Pos + new Vec2(T, 0));
				b.Pos = a.Pos + new Vec2(T, 0);
				a.Facing = Facing.East;
				b.Facing = Facing.West;
				// MARCADOS UM NO OUTRO: o marcado vem primeiro em `QuemEstaNaFrente` -- as duas Humanas nascem no mesmo berco da Terra.
				a.AlvoId = b.Id;
				b.AlvoId = a.Id;
			}
			bool VeOVerb(ServerPlayer quem, string chave) => RequisitosDosVerbos.Linha(chave) is { } l && l.Requisito.Atende(PosseDe(quem));
			BebeACaminho? BebeDe(ServerPlayer mae) => _bebes.Bebes.Find(b => b.SigMae == mae.Assinatura);

			// ============================================================ A) AS REGRAS DO CORE
			GD.Print("[filhos] -- A) as regras: quem cruza, quem gera, a classe mais rara, a idade zero --");
			AfirmarFl("Humano com Saiyajin da o meio-Saiyajin, a mesma raca da a mesma raca, e o resto NAO cruza (o pedido do dono)",
				Filhos.RacaDoFilho("Human", "Saiyan") == Filhos.MeioSaiyajin && Filhos.RacaDoFilho("Saiyan", "Human") == Filhos.MeioSaiyajin
				&& Filhos.RacaDoFilho("Majin", "Majin") == "Majin" && Filhos.RacaDoFilho("Human", "Majin") == "" && Filhos.RacaDoFilho("Saiyan", "Icer") == "");
			if (_racas is not { } cat) { AfirmarFl("o catalogo de racas carregou", false); return; }
			AfirmarFl("COMO CADA RACA GERA (o 'Breed Type' do proto, e o dono por cima): Humano, Saiyajin e meio-Saiyajin com um par; Frost Demon e Namekuseijin sozinhos, pelo ovo; o Majin dos dois jeitos; o androide de energia infinita com um par, o de absorcao nao",
				Filhos.ComoGera(cat, "Human", false) == ModoDeGerar.Gravidez && Filhos.ComoGera(cat, "Saiyan", false) == ModoDeGerar.Gravidez
				&& Filhos.ComoGera(cat, Filhos.MeioSaiyajin, false) == ModoDeGerar.Gravidez
				&& Filhos.ComoGera(cat, "Icer", false) == ModoDeGerar.Ovo && Filhos.ComoGera(cat, "Namekian", false) == ModoDeGerar.Ovo
				&& Filhos.ComoGera(cat, "Majin", false) == (ModoDeGerar.Gravidez | ModoDeGerar.Ovo)
				&& Filhos.ComoGera(cat, "Android", true) == ModoDeGerar.Gravidez && Filhos.ComoGera(cat, "Android", false) == ModoDeGerar.Nenhum,
				$"{Filhos.PorQueNaoTemFilhoComPar(cat, "Icer", false)} | {Filhos.PorQueNaoTemFilhoComPar(cat, "Android", false)} | {Filhos.PorQueNaoPoeOvo(cat, "Human", false)}");
			BebeACaminho Par(string raca, string cPai, string lPai, string cMae, string lMae) => new()
				{ Raca = raca, ClasseDoPai = cPai, LinhagemDoPai = lPai, ClasseDaMae = cMae, LinhagemDaMae = lMae };
			var rng = new Random(7);
			string eliteLow = Filhos.ClasseDoFilho(cat, Par("Saiyan", "Elite", "Saiyan", "Low-Class", "Saiyan"), "Saiyan", rng);
			string lendPrimal = Filhos.ClasseDoFilho(cat, Par("Saiyan", "Legendary", "Saiyan", "Normal Primal Saiyan", "Primal Saiyan"), "Primal Saiyan", rng);
			string elitePrimal = Filhos.ClasseDoFilho(cat, Par("Saiyan", "Elite", "Saiyan", "Normal Primal Saiyan", "Primal Saiyan"), "Primal Saiyan", rng);
			string eremita = Filhos.ClasseDoFilho(cat, Par("Human", "Peak Human", "", "Ancient Hermit", ""), "", rng);
			string meio = Filhos.ClasseDoFilho(cat, Par(Filhos.MeioSaiyajin, "Elite", "Saiyan", "Normal", ""), "Prodigial", rng);
			AfirmarFl("A CLASSE MAIS RARA DOS PAIS: Elite x Low-Class = Elite; Lendario x Primal comum, na linhagem Primal = Lendario Primal; Elite x Primal comum, na Primal = Primal comum; Peak Human x Ancient Hermit = Ancient Hermit; o meio-Saiyajin nao herda (vazio: a linhagem dele e a classe)",
				eliteLow == "Elite" && lendPrimal == "Legendary Primal Saiyan" && elitePrimal == "Normal Primal Saiyan" && eremita == "Ancient Hermit" && meio == "",
				$"{eliteLow} | {lendPrimal} | {elitePrimal} | {eremita} | '{meio}'");
			AfirmarFl("AS LINHAGENS DO FILHO: as tres do meio-Saiyajin; a dos pais Saiyajins, ou as duas quando diferem; nenhuma no Humano",
				Filhos.LinhagensDoFilho(Filhos.MeioSaiyajin, "Saiyan", "").SequenceEqual(CharacterDraft.EscolhasDeClasse(Filhos.MeioSaiyajin))
				&& Filhos.LinhagensDoFilho("Saiyan", "Saiyan", "Saiyan").SequenceEqual(["Saiyan"])
				&& Filhos.LinhagensDoFilho("Saiyan", "Saiyan", "Primal Saiyan").SequenceEqual(["Saiyan", "Primal Saiyan"])
				&& Filhos.LinhagensDoFilho("Human", "", "").Length == 0);
			AfirmarFl("o BP do pai entra em um quarto, com teto de 10.000 (`Husband_BP`, `Mating.dm:195-196`)",
				Filhos.BpDoPai(400) == 100 && Filhos.BpDoPai(1e6) == Filhos.TetoDoBpDoPai);
			AfirmarFl("O RECEM-NASCIDO NAO MORRE DE VELHICE: com zero anos o corpo ainda cresce (a morte de velhice so depois do auge) -- e o velho de verdade continua morrendo",
				!Envelhecimento.MorreuDeVelhice("Human", 0) && !Envelhecimento.MorreuDeVelhice("Saiyan", 0) && Envelhecimento.MorreuDeVelhice("Human", 1000));
			var semZero = new CharacterDraft { Name = "Bebe", Race = "Human", Age = 0, Backstory = "uma historia de bebe" };
			var comZero = new CharacterDraft { Name = "Bebe", Race = "Human", Age = 0, Backstory = "uma historia de bebe", FilhoDe = 1 };
			AfirmarFl("a ficha so aceita zero anos de quem nasce de um bebe (`CharacterDraft.FilhoDe`)",
				semZero.Validar().Contains("idade") && comZero.Validar().Length == 0, $"'{semZero.Validar()}' / '{comZero.Validar()}'");

			// ============================================================ B) O PEDIDO
			GD.Print("[filhos] -- B) o pedido: o amor correspondido, o verb, o cartao e o sim --");
			ServerPlayer? goku = Entrar(0, Ficha("Goku", "Saiyan", "Male", "Saiyan"));
			ServerPlayer? chichi = Entrar(1, Ficha("Chichi", "Human", "Female"));
			ServerPlayer? bulma = Entrar(2, Ficha("Bulma", "Human", "Female"));
			AfirmarFl("(montagem) Goku (Saiyajin), Chichi e Bulma (Humanas) entram pela porta e sao JOGADORES",
				goku != null && chichi != null && bulma != null && EhJogador(goku) && EhJogador(chichi) && EhJogador(bulma),
				$"{c[0].Recusa} | {c[1].Recusa} | {c[2].Recusa}");
			if (goku == null || chichi == null || bulma == null) return;
			// A HERANCA TEM DE APARECER CONTRA A SEMENTE: o pai bem forte (o quarto dele bate no teto) e a mae de 24.000.
			goku.Ficha.BP = 400_000;
			chichi.Ficha.BP = 24_000;
			LadoALado(chichi, goku);

			List<string> semAmor = Ouvir(() => UsarHabilidade(goku, "filho_pedir"));
			AfirmarFl("SEM AMOR NAO HA PEDIDO: o verb nao aparece, e o pedido cru diz o que falta (declarar AMOR na aba People)",
				!VeOVerb(goku, "hab:filho_pedir") && chichi.PedidoDeFilho == null && semAmor.Any(x => x.Contains("declare AMOR")), Ultimos(semAmor));

			Amar(goku, chichi);
			List<string> umLado = Ouvir(() => UsarHabilidade(goku, "filho_pedir"));
			AfirmarFl("AMOR DE UM LADO SO NAO BASTA: Goku declarou, Chichi nao -- o pedido e recusado e o verb segue apagado",
				chichi.PedidoDeFilho == null && !VeOVerb(goku, "hab:filho_pedir") && umLado.Any(x => x.Contains("ainda não declarou amor")), Ultimos(umLado));
			AmorDeUmLadoSoDeTeste = true;
			try { UsarHabilidade(goku, "filho_pedir"); }
			finally { AmorDeUmLadoSoDeTeste = false; }
			AfirmarFl("CONTRA-EXEMPLO (defeito injetado: o amor de um lado so): o mesmo pedido PASSA -- a regra de cima REPROVA", chichi.PedidoDeFilho != null);
			chichi.PedidoDeFilho = null;

			Amar(chichi, goku);
			AfirmarFl("COM O AMOR CORRESPONDIDO o verb 'Ter um filho' acende pros dois (o fato `TemUmAmor`) -- e nao pra Bulma",
				VeOVerb(goku, "hab:filho_pedir") && VeOVerb(chichi, "hab:filho_pedir") && !VeOVerb(bulma, "hab:filho_pedir"));
			List<string> pediu = Ouvir(() => UsarHabilidade(goku, "filho_pedir"));
			var cartoes = new List<PedidoNaTela>();
			PedidosDe(chichi, cartoes);
			AfirmarFl("O PEDIDO vira cartao na tela da Chichi, com o nome de quem pede e o prazo, e acende 'Aceitar o filho' e 'Recusar o filho'",
				chichi.PedidoDeFilho != null && cartoes.Any(p => p.Fato == Fatos.PedidoDeFilho && p.Texto.Contains("Goku") && p.Segundos > 0)
				&& VeOVerb(chichi, "hab:filho_sim") && VeOVerb(chichi, "hab:filho_nao") && !VeOVerb(goku, "hab:filho_sim"), Ultimos(pediu));
			List<string> recusou = Ouvir(() => UsarHabilidade(chichi, "filho_nao"));
			AfirmarFl("o 'nao' tira o pedido da mesa, e bebe nenhum e concebido", chichi.PedidoDeFilho == null && _bebes.Bebes.Count == 0, Ultimos(recusou));

			UsarHabilidade(goku, "filho_pedir");
			List<string> aceitou = Ouvir(() => UsarHabilidade(chichi, "filho_sim"));
			BebeACaminho? bebe = BebeDe(chichi);
			AfirmarFl("O 'SIM' CONCEBE: um bebe a caminho -- Half-Saiyan (Humana com Saiyajin), pai Goku, mae Chichi, as tres linhagens do meio-Saiyajin",
				bebe is { Raca: Filhos.MeioSaiyajin, Pai: "Goku", Mae: "Chichi" } && bebe.SigPai == goku.Assinatura
				&& bebe.Linhagens.SequenceEqual(CharacterDraft.EscolhasDeClasse(Filhos.MeioSaiyajin)), Ultimos(aceitou));
			AfirmarFl("...com a foto do poder: o pai em um quarto com teto (400.000 -> 10.000), a mae inteira (24.000)",
				bebe is { BpDoPai: 10_000, BpDaMae: 24_000 }, $"{bebe?.BpDoPai:N0} / {bebe?.BpDaMae:N0}");
			AfirmarFl("...e a zona fica sabendo da gravidez (`view(9)`, `Mating.dm:198`)", aceitou.Any(x => x.Contains("está grávida")), Ultimos(aceitou));
			if (bebe == null) return;

			// ============================================================ C) AS RECUSAS
			GD.Print("[filhos] -- C) as recusas: mesmo sexo, a mae que ja espera, longe, morto --");
			Amar(chichi, bulma);
			Amar(bulma, chichi);
			LadoALado(bulma, chichi);
			List<string> mesmoSexo = Ouvir(() => UsarHabilidade(chichi, "filho_pedir"));
			AfirmarFl("MESMO SEXO NAO TEM FILHO, com amor correspondido e tudo (`try_have_child`)",
				bulma.PedidoDeFilho == null && mesmoSexo.Any(x => x.Contains("sexos opostos")), Ultimos(mesmoSexo));
			LadoALado(chichi, goku);
			List<string> jaEspera = Ouvir(() => UsarHabilidade(goku, "filho_pedir"));
			AfirmarFl("A MAE QUE JA ESPERA NAO CONCEBE OUTRO (o `Pregnant` do DM): um bebe por vez",
				chichi.PedidoDeFilho == null && jaEspera.Any(x => x.Contains("já espera um bebê")), Ultimos(jaEspera));
			Vec2 dela = chichi.Pos;
			goku.Pos = dela + new Vec2(12 * T, 0);
			string longe = PorQueNaoTemFilho(goku, chichi);
			goku.Pos = dela + new Vec2(T, 0);
			goku.Ficha.dead = true;
			string morto = PorQueNaoTemFilho(goku, chichi);
			goku.Ficha.dead = false;
			AfirmarFl("LONGE NAO, E MORTO NAO (`mate_pair_ok`: a regra e da morte, e nao de luta)",
				longe.Contains("do seu lado") && morto.Contains("mortos"), $"'{longe}' / '{morto}'");

			// ============================================================ D) A OFERTA
			GD.Print("[filhos] -- D) a oferta: o bebe no fim da lista de slots --");
			bool logou = mesa.Entra(c[3]) && mesa.Loga(c[3], ContasDaBancadaDosFilhos[3]);
			AfirmarFl("A OFERTA VIAJA: a conta nova recebe, no fim do `S2C.SlotList`, o bebe de Goku e Chichi -- id, pais, raca e as tres linhagens",
				logou && c[3].Bebe is { } oferta && oferta.Id == bebe.Id && oferta.Pai == "Goku" && oferta.Mae == "Chichi"
				&& oferta.Raca == Filhos.MeioSaiyajin && oferta.Linhagens.Length == 3,
				c[3].Bebe is { } o ? $"#{o.Id} {o.Pai} e {o.Mae}, {o.Raca}, {string.Join("/", o.Linhagens)}" : "sem oferta");

			// ============================================================ E) O PARTO
			GD.Print("[filhos] -- E) o parto: o CreateChar de quem aceitou nascer do bebe --");
			CharacterDraft doGohan = Ficha("Gohan", "Human", "Male", "Future Lineage");   // a raca e a idade da ficha NAO valem
			doGohan.FilhoDe = bebe.Id;
			c[3].CriarPersonagem(0, doGohan);
			bool nasceu = mesa.Bombeia(() => c[3].NoMundo || c[3].Recusa != null, 8000) && c[3].NoMundo;
			ServerPlayer? gohan = mesa.CorpoDe(ContasDaBancadaDosFilhos[3]);
			AfirmarFl("O PARTO PELA PORTA: nasce Half-Saiyan com ZERO anos (a ficha dizia Humano de 18) -- e vivo: a velhice nao o leva no login",
				nasceu && gohan != null && gohan.Ficha.Race == Filhos.MeioSaiyajin && gohan.Idade == 0 && gohan.Ficha.Idade == 0
				&& !gohan.Ficha.dead && !gohan.Ficha.aged_out, $"{c[3].Recusa} | {gohan?.Ficha.Race} de {gohan?.Idade}");
			AfirmarFl("...com a linhagem que escolheu entre as tres do meio-Saiyajin (Future Lineage) -- e ela e a classe dele",
				gohan?.Ficha.Class == "Future Lineage" && gohan.Linhagem == "Future Lineage", $"{gohan?.Ficha.Class} / {gohan?.Linhagem}");
			AfirmarFl("...DO LADO DA MAE (`SpawnPoints.dm:137`): na zona dela, colado nela",
				gohan != null && gohan.Zone.Hash == chichi.Zone.Hash && Vec2.Distance(gohan.Pos, chichi.Pos) <= 2 * T,
				gohan != null ? $"{Vec2.Distance(gohan.Pos, chichi.Pos) / T:0.#} tiles" : "");
			double minimo = (bebe.BpDoPai + bebe.BpDaMae) / 24;
			AfirmarFl("...COM O PODER DOS PAIS: o BP passa de 1/24 de cada um, e o potencial escondido ganha os dois inteiros (34.000)",
				gohan != null && gohan.Ficha.BP >= minimo - 2 && gohan.Ficha.hiddenpotential >= bebe.BpDoPai + bebe.BpDaMae,
				$"BP {gohan?.Ficha.BP:N0} (minimo {minimo:N0}), potencial {gohan?.Ficha.hiddenpotential:N0}");
			AfirmarFl("...e o bebe saiu da fila", BebeDe(chichi) == null && OfertaDaCriacao == null);

			bool logou2 = mesa.Entra(c[4]) && mesa.Loga(c[4], ContasDaBancadaDosFilhos[4]);
			CharacterDraft doAtrasado = Ficha("Goten", "Human", "Male");
			doAtrasado.FilhoDe = bebe.Id;
			c[4].CriarPersonagem(0, doAtrasado);
			mesa.Bombeia(() => c[4].NoMundo || c[4].Recusa != null, 4000);
			AfirmarFl("QUEM CHEGA DEPOIS NAO NASCE DO MESMO BEBE: a ficha e recusada ('ja nasceu') e nenhum corpo entra",
				logou2 && !c[4].NoMundo && c[4].Recusa?.Contains("já nasceu") == true && mesa.CorpoDe(ContasDaBancadaDosFilhos[4]) == null,
				c[4].Recusa ?? "sem recusa");

			// ============================================================ F) DOIS SAIYAJINS, O MODO ALEATORIO E O CONTRA-EXEMPLO
			GD.Print("[filhos] -- F) dois Saiyajins de linhagens diferentes, o modo de racas aleatorias e o filho sem heranca --");
			ServerPlayer? vegeta = Entrar(5, Ficha("Vegeta", "Saiyan", "Male", "Saiyan"));
			ServerPlayer? gine = Entrar(6, Ficha("Gine", "Saiyan", "Female", "Primal Saiyan"));
			AfirmarFl("(montagem) Vegeta (Saiyan) e Gine (Primal Saiyan) entram pela porta", vegeta != null && gine != null, $"{c[5].Recusa} | {c[6].Recusa}");
			if (vegeta == null || gine == null) return;
			// AS CLASSES CRAVADAS: o sorteio da criacao e o do DM, e a regua aqui e "a mais rara".
			vegeta.Ficha.Class = "Elite";
			vegeta.Ficha.SaiyanLineage = "Saiyan";
			gine.Ficha.Class = "Normal Primal Saiyan";
			gine.Ficha.SaiyanLineage = "Primal Saiyan";
			vegeta.Ficha.BP = 40_000;
			gine.Ficha.BP = 8_000;
			Amar(vegeta, gine);
			Amar(gine, vegeta);
			LadoALado(gine, vegeta);
			UsarHabilidade(vegeta, "filho_pedir");
			UsarHabilidade(gine, "filho_sim");
			BebeACaminho? saiyajin = BebeDe(gine);
			AfirmarFl("DOIS SAIYAJINS DE LINHAGENS DIFERENTES: o bebe e Saiyajin e oferece as DUAS linhagens -- o filho escolhe",
				saiyajin is { Raca: "Saiyan" } && saiyajin.Linhagens.SequenceEqual(["Saiyan", "Primal Saiyan"]),
				saiyajin != null ? string.Join("/", saiyajin.Linhagens) : "sem bebe");
			if (saiyajin == null) return;

			EscolherModoDeRacas(ModoDeRacas.Aleatorio, "bancada dos filhos");
			bool logou3 = mesa.Entra(c[7]) && mesa.Loga(c[7], ContasDaBancadaDosFilhos[7]);
			string sorteadaNaVaga = c[7].Slots is { Count: > 0 } vagas ? vagas[0].RacaSorteada ?? "" : "";
			CharacterDraft doTarble = Ficha("Tarble", "Human", "Male", "Saiyan");
			doTarble.FilhoDe = saiyajin.Id;
			c[7].CriarPersonagem(0, doTarble);
			mesa.Bombeia(() => c[7].NoMundo || c[7].Recusa != null, 8000);
			ServerPlayer? tarble = mesa.CorpoDe(ContasDaBancadaDosFilhos[7]);
			AfirmarFl("NO MODO DE RACAS ALEATORIAS o filho nasce da raca dos pais -- o sorteio da vaga nao vale pra ele (o dono: *\"mesmo q seja no modo de raça random\"*)",
				logou3 && sorteadaNaVaga.Length > 0 && tarble?.Ficha.Race == "Saiyan", $"a vaga sorteou '{sorteadaNaVaga}'; nasceu {tarble?.Ficha.Race}");
			AfirmarFl("A CLASSE MAIS RARA DOS PAIS na linhagem escolhida: Elite (o pai) contra a Primal comum da mae (que na linhagem Saiyan vira Normal) = Elite",
				tarble?.Ficha.Class == "Elite" && tarble.Ficha.SaiyanLineage == "Saiyan", $"{tarble?.Ficha.Class} / {tarble?.Ficha.SaiyanLineage}");

			UsarHabilidade(vegeta, "filho_pedir");
			UsarHabilidade(gine, "filho_sim");   // a mae ja pariu: pode conceber de novo
			BebeACaminho? segundo = BebeDe(gine);
			bool logou4 = segundo != null && mesa.Entra(c[8]) && mesa.Loga(c[8], ContasDaBancadaDosFilhos[8]);
			ServerPlayer? semHeranca = null;
			if (segundo != null)
			{
				CharacterDraft doCru = Ficha("Cru", "Human", "Male", "Primal Saiyan");
				doCru.FilhoDe = segundo.Id;
				FilhoSemHerancaDeTeste = true;
				try
				{
					c[8].CriarPersonagem(0, doCru);
					mesa.Bombeia(() => c[8].NoMundo || c[8].Recusa != null, 8000);
				}
				finally { FilhoSemHerancaDeTeste = false; }
				semHeranca = mesa.CorpoDe(ContasDaBancadaDosFilhos[8]);
			}
			AfirmarFl("CONTRA-EXEMPLO (defeito injetado: o filho sem heranca): nasce sem o potencial dos pais (18.000) -- a regua do parto REPROVA",
				logou4 && semHeranca != null && segundo != null && semHeranca.Ficha.hiddenpotential < segundo.BpDoPai + segundo.BpDaMae,
				$"potencial {semHeranca?.Ficha.hiddenpotential:N0}");
			EscolherModoDeRacas(modoAntes, "bancada dos filhos (volta)");

			// ============================================================ G) A RESPOSTA DO DONO: O OVO, O MEIO-SAIYAJIN E O ANDROIDE
			GD.Print("[filhos] -- G) a resposta do dono: as racas de ovo tem filho sozinhas, o meio-Saiyajin tem filho, o androide de energia infinita e humano --");
			AfirmarFl("O SANGUE: meio-Saiyajin com Humano, com Saiyajin ou com outro meio-Saiyajin da meio-Saiyajin; o androide conta como Humano -- com Saiyajin da meio-Saiyajin, com Humano (ou outro androide) da Humano; com o resto nao cruza",
				Filhos.RacaDoFilho(Filhos.MeioSaiyajin, "Human") == Filhos.MeioSaiyajin && Filhos.RacaDoFilho("Human", Filhos.MeioSaiyajin) == Filhos.MeioSaiyajin
				&& Filhos.RacaDoFilho(Filhos.MeioSaiyajin, "Saiyan") == Filhos.MeioSaiyajin && Filhos.RacaDoFilho(Filhos.MeioSaiyajin, Filhos.MeioSaiyajin) == Filhos.MeioSaiyajin
				&& Filhos.RacaDoFilho("Saiyan", "Android") == Filhos.MeioSaiyajin && Filhos.RacaDoFilho("Android", "Human") == "Human"
				&& Filhos.RacaDoFilho("Android", "Android") == "Human" && Filhos.RacaDoFilho("Android", "Majin") == "");
			AfirmarFl("A IDADE PRA GERAR: os 16 anos do DM (`Mature_Age`), ou o auge da raca se vier antes (o Saibaman, aos 10); o Majin, que nao envelhece, nos 16",
				Filhos.IdadeParaGerar("Human") == 16 && Filhos.IdadeParaGerar("Saibaman") == 10 && Filhos.IdadeParaGerar("Majin") == 16
				&& Filhos.IdadeParaGerar("Namekian") == 16,
				$"{Filhos.IdadeParaGerar("Human")} | {Filhos.IdadeParaGerar("Saibaman")} | {Filhos.IdadeParaGerar("Majin")} | {Filhos.IdadeParaGerar("Namekian")}");

			ServerPlayer? piccolo = Entrar(9, Ficha("Piccolo", "Namekian", "Male", "Warrior clan"));
			ServerPlayer? videl = Entrar(11, Ficha("Videl", "Human", "Female"));
			ServerPlayer? lapis = Entrar(12, Ficha("Lapis", "Human", "Male"));
			AfirmarFl("(montagem) Piccolo (Namekuseijin), Videl (Humana) e Lapis (Humano, que vai virar androide) entram pela porta",
				piccolo != null && videl != null && lapis != null, $"{c[9].Recusa} | {c[11].Recusa} | {c[12].Recusa}");
			if (piccolo == null || videl == null || lapis == null || gohan == null) return;

			// ---- o ovo: o Piccolo, sozinho ----
			piccolo.Ficha.BP = 400_000;
			piccolo.Ficha.Class = "Warrior clan";
			piccolo.Idade = 5;
			List<string> novo = Ouvir(() => UsarHabilidade(piccolo, "filho_ovo"));
			AfirmarFl("JOVEM DEMAIS NAO POE OVO: o Namekuseijin de 5 anos nao ve 'Pôr um ovo', e o aperto cru diz a idade (16)",
				!VeOVerb(piccolo, "hab:filho_ovo") && BebeDe(piccolo) == null && novo.Any(x => x.Contains("jovem demais")), Ultimos(novo));
			piccolo.Idade = 18;
			List<string> antesDoDono;
			Filhos.FilhosComoAntesDeTeste = true;
			try { antesDoDono = Ouvir(() => UsarHabilidade(piccolo, "filho_ovo")); }
			finally { Filhos.FilhosComoAntesDeTeste = false; }
			AfirmarFl("CONTRA-EXEMPLO (defeito injetado: as regras de antes da resposta, ovo nenhum): o mesmo aperto NAO poe o ovo -- a prova de baixo REPROVA",
				BebeDe(piccolo) == null && antesDoDono.Any(x => x.Contains("não põe ovo")), Ultimos(antesDoDono));
			List<string> semCorpo;
			Filhos.OvoSemCorpoDeTeste = true;
			try { semCorpo = Ouvir(() => UsarHabilidade(piccolo, "filho_ovo")); }
			finally { Filhos.OvoSemCorpoDeTeste = false; }
			BebeACaminho? ovoSemCorpo = BebeDe(piccolo);
			AfirmarFl("CONTRA-EXEMPLO (defeito injetado: o ovo de antes, guardado como a gravidez): o ovo entra na fila e NENHUMA obra cai no chao -- a prova do ovo no chao REPROVA",
				ovoSemCorpo is { DeOvo: true, Ovo: 0 } && !_noChao.Any(o => o.Tipo == Filhos.TipoDoOvo), Ultimos(semCorpo));
			if (ovoSemCorpo != null) TirarOBebeDaFila(ovoSemCorpo);   // o ovo de mentira sai da fila
			List<string> pos = Ouvir(() => UsarHabilidade(piccolo, "filho_ovo"));
			BebeACaminho? ovo = BebeDe(piccolo);
			AfirmarFl("AS RACAS DE OVO TEM FILHO SOZINHAS (o dono): o Namekuseijin adulto ve 'Pôr um ovo' -- e o Humano nao --, e o aperto poe o ovo: Namekuseijin, o cla dele (Warrior clan) como a unica linhagem, quem o pos no lugar da mae e o pai vazio",
				VeOVerb(piccolo, "hab:filho_ovo") && !VeOVerb(videl, "hab:filho_ovo") && ovo is { DeOvo: true, Raca: "Namekian", Mae: "Piccolo", Pai: "" }
				&& ovo.Linhagens.SequenceEqual(["Warrior clan"]) && pos.Any(x => x.Contains("põe um ovo")), Ultimos(pos));
			AfirmarFl("...com a heranca do `Lay_Egg`: um quarto do BP, SEM o teto do pai (400.000 -> 100.000)",
				ovo is { BpDaMae: 100_000, BpDoPai: 0 }, $"{ovo?.BpDaMae:N0} / {ovo?.BpDoPai:N0}");
			List<string> outroOvo = Ouvir(() => UsarHabilidade(piccolo, "filho_ovo"));
			List<string> humano = Ouvir(() => UsarHabilidade(videl, "filho_ovo"));
			AfirmarFl("UM OVO POR VEZ, e o Humano nao poe ovo (a raca dele tem filho com um par)",
				_bebes.Bebes.Count(b => b.SigMae == piccolo.Assinatura) == 1 && outroOvo.Any(x => x.Contains("já tem um ovo"))
				&& humano.Any(x => x.Contains("não põe ovo")), $"{Ultimos(outroOvo)} || {Ultimos(humano)}");
			if (ovo == null) return;

			// ---- O OVO NO CHAO (o dono, 2026-10-17: *"tinha um sprite de ovo"*) ----
			Obra? casca = OvoNoChao(ovo);
			(int ovoX, int ovoY) = casca != null ? Jandirus.Core.Tech.CatalogoDeObras.Celula(casca.X, casca.Y) : (-99, -99);
			(int picX, int picY) = Jandirus.Core.Tech.CatalogoDeObras.Celula(piccolo.Pos.X, piccolo.Pos.Y);
			bool noMundoJson = casca != null && System.IO.File.Exists(CaminhoDoMundo)
				&& System.IO.File.ReadAllText(CaminhoDoMundo).Contains($"\"Id\": {casca.Id},");
			AfirmarFl("O OVO FICA NO CHAO (o dono: *\"tinha um sprite de ovo\"*): a obra 'Egg' numa celula do lado do Piccolo, ligada ao ovo da fila, com a arte do `Egg.dmi` do DM -- e ela vai pro `mundo.json`",
				casca != null && casca.Zona.Equals(piccolo.Zone) && Math.Max(Math.Abs(ovoX - picX), Math.Abs(ovoY - picY)) == 1
				&& _obras?.Get(Filhos.TipoDoOvo)?.Arte == "res://Assets/Sprites/Misc/Egg.tres" && noMundoJson,
				casca != null ? $"obra #{casca.Id} em ({ovoX},{ovoY}), Piccolo em ({picX},{picY}), arte {_obras?.Get(Filhos.TipoDoOvo)?.Arte}, mundo.json {noMundoJson}" : "sem ovo no chao");

			bool logou5 = mesa.Entra(c[10]) && mesa.Loga(c[10], ContasDaBancadaDosFilhos[10]);
			AfirmarFl("A OFERTA DO OVO VIAJA: a conta nova recebe o ovo do Piccolo -- marcado como ovo, sem pai, com o cla",
				logou5 && c[10].Bebe is { DeOvo: true, Mae: "Piccolo", Pai: "" } ofOvo && ofOvo.Id == ovo.Id && ofOvo.Linhagens.SequenceEqual(["Warrior clan"]),
				c[10].Bebe is { } o2 ? $"#{o2.Id} ovo={o2.DeOvo} {o2.Mae}, {o2.Raca}, {string.Join("/", o2.Linhagens)}" : "sem oferta");
			CharacterDraft doDende = Ficha("Dende", "Human", "Male", "Dragon clan");   // a raca e o cla da ficha NAO valem: valem os do ovo
			doDende.FilhoDe = ovo.Id;
			// QUEM POS SE AFASTA ANTES DO PARTO: o filho tem de nascer do lado do OVO, e nao do lado do Piccolo.
			Vec2 ondeOOvoEsta = casca != null ? new Vec2(casca.X, casca.Y) : piccolo.Pos;
			piccolo.Pos += new Vec2(12 * T, 0);
			c[10].CriarPersonagem(0, doDende);
			bool chocou = mesa.Bombeia(() => c[10].NoMundo || c[10].Recusa != null, 8000) && c[10].NoMundo;
			ServerPlayer? dende = mesa.CorpoDe(ContasDaBancadaDosFilhos[10]);
			AfirmarFl("O OVO CHOCA PELA PORTA: nasce Namekuseijin de ZERO anos com o cla de quem o pos (Warrior clan, e nao o Dragon clan da ficha), do lado do OVO -- e nao do Piccolo, que se afastou -- e o ovo some do chao",
				chocou && dende is { Race: "Namekian", Idade: 0 } && dende.Ficha.Class == "Warrior clan"
				&& dende.Zone.Hash == piccolo.Zone.Hash && Vec2.Distance(dende.Pos, ondeOOvoEsta) <= 1.5f * T && Vec2.Distance(dende.Pos, piccolo.Pos) > 6 * T
				&& (casca == null || !_noChao.Contains(casca)),
				$"{c[10].Recusa} | {dende?.Race} {dende?.Ficha.Class} de {dende?.Idade}, a {(dende != null ? Vec2.Distance(dende.Pos, ondeOOvoEsta) / T : -1):0.0} tile(s) do ovo");
			piccolo.Pos -= new Vec2(12 * T, 0);
			AfirmarFl("O CALENDARIO CONTA A IDADE DO FILHO A PARTIR DO PARTO (2026-10-17): o mes do calendario vai no corpo dele, e os meses sao zero",
				dende is { MesesDaIdade: 0 } && dende.MesDaIdade == MesDoCalendario, $"mes {dende?.MesDaIdade}, calendario {MesDoCalendario}");
			AfirmarFl("...com o poder do ovo: o BP passa de 1/24 do quarto (100.000) e o potencial escondido ganha o quarto inteiro -- e o ovo sai da fila",
				dende != null && dende.Ficha.BP >= 100_000 / 24 - 2 && dende.Ficha.hiddenpotential >= 100_000 && BebeDe(piccolo) == null,
				$"BP {dende?.Ficha.BP:N0}, potencial {dende?.Ficha.hiddenpotential:N0}");

			// ---- O OVO QUEBRA: um soco no ovo, e o filho que ia nascer dele se perde ----
			List<string> doSegundoOvo = Ouvir(() => UsarHabilidade(piccolo, "filho_ovo"));
			BebeACaminho? ovo2 = BebeDe(piccolo);
			Obra? casca2 = ovo2 != null ? OvoNoChao(ovo2) : null;
			List<string> soco = casca2 != null ? Ouvir(() => Estragar(casca2, 1e9, videl)) : [];
			AfirmarFl("O OVO QUEBRA NUM SOCO (o `mob/Egg` era atacavel e caia num golpe): a obra sai do chao, o ovo sai da fila, e quem o pos ouve que o filho se perdeu",
				casca2 != null && !_noChao.Contains(casca2) && BebeDe(piccolo) == null && soco.Any(x => x.Contains("o seu ovo foi quebrado")),
				$"{Ultimos(doSegundoOvo)} || {Ultimos(soco)}");

			// ---- o meio-Saiyajin: o Gohan cresce e tem filho com a Videl ----
			Amar(gohan, videl);
			Amar(videl, gohan);
			LadoALado(videl, gohan);
			List<string> menino = Ouvir(() => UsarHabilidade(gohan, "filho_pedir"));
			AfirmarFl("O RECEM-NASCIDO AINDA NAO TEM FILHO: o Gohan de 0 anos, com o amor correspondido, ouve que e jovem demais",
				videl.PedidoDeFilho == null && menino.Any(x => x.Contains("jovem demais")), Ultimos(menino));
			gohan.Idade = 18;   // ele cresceu (a Sala do Tempo e o calendario deste port)
			gohan.Ficha.Idade = 18;
			List<string> esteril;
			Filhos.FilhosComoAntesDeTeste = true;
			try { esteril = Ouvir(() => UsarHabilidade(gohan, "filho_pedir")); }
			finally { Filhos.FilhosComoAntesDeTeste = false; }
			AfirmarFl("CONTRA-EXEMPLO (defeito injetado: o meio-Saiyajin esteril do DM): o pedido e recusado -- a prova de baixo REPROVA",
				videl.PedidoDeFilho == null && esteril.Any(x => x.Contains("não gera filhos")), Ultimos(esteril));
			UsarHabilidade(gohan, "filho_pedir");
			List<string> simDaVidel = Ouvir(() => UsarHabilidade(videl, "filho_sim"));
			BebeACaminho? pan = BebeDe(videl);
			AfirmarFl("O MEIO-SAIYAJIN TEM FILHO (o dono): o Gohan crescido com a Videl (Humana) concebe outro meio-Saiyajin -- que escolhe entre as tres linhagens",
				pan is { Raca: Filhos.MeioSaiyajin, Pai: "Gohan", Mae: "Videl", DeOvo: false }
				&& pan.Linhagens.SequenceEqual(CharacterDraft.EscolhasDeClasse(Filhos.MeioSaiyajin)), Ultimos(simDaVidel));

			// ---- o androide: o de absorcao nao tem filho; o de energia infinita e humano pro genoma ----
			if (gine == null) return;
			lapis.Race = "Android";   // o que o `VirarAndroide` de producao escreve
			lapis.Ficha.AndroideAbsorcao = true;
			lapis.Ficha.AndroideInfinito = false;
			Amar(lapis, gine);
			Amar(gine, lapis);
			LadoALado(gine, lapis);
			List<string> absorcao = Ouvir(() => UsarHabilidade(lapis, "filho_pedir"));
			AfirmarFl("O ANDROIDE DE ABSORCAO NAO TEM FILHO: o pedido dele e recusado",
				gine.PedidoDeFilho == null && absorcao.Any(x => x.Contains("absorção não gera")), Ultimos(absorcao));
			lapis.Ficha.AndroideAbsorcao = false;
			lapis.Ficha.AndroideInfinito = true;
			List<string> semFilho;
			Filhos.FilhosComoAntesDeTeste = true;
			try { semFilho = Ouvir(() => UsarHabilidade(lapis, "filho_pedir")); }
			finally { Filhos.FilhosComoAntesDeTeste = false; }
			AfirmarFl("CONTRA-EXEMPLO (defeito injetado: o androide sem filho do DM): o de energia infinita tambem e recusado -- a prova de baixo REPROVA",
				gine.PedidoDeFilho == null && semFilho.Any(x => x.Contains("não gera filhos")), Ultimos(semFilho));
			UsarHabilidade(lapis, "filho_pedir");
			List<string> simDaGine = Ouvir(() => UsarHabilidade(gine, "filho_sim"));
			BebeACaminho? doAndroide = BebeDe(gine);
			AfirmarFl("O ANDROIDE DE ENERGIA INFINITA E HUMANO PRO GENOMA (o dono: *\"saiyajin + androide de energia infinita = meio saiyajin\"*): com a Gine (Saiyajin) concebe um meio-Saiyajin",
				doAndroide is { Raca: Filhos.MeioSaiyajin, Pai: "Lapis", RacaDoPai: "Android" }, Ultimos(simDaGine));

			// ============================================================ H) O DISCO
			GD.Print("[filhos] -- H) o disco: o bebe a caminho volta igual do bebes.json --");
			LadoALado(chichi, goku);
			UsarHabilidade(goku, "filho_pedir");
			UsarHabilidade(chichi, "filho_sim");   // o Gohan ja nasceu: a Chichi pode de novo
			BebeACaminho? antes = BebeDe(chichi);
			int proximoAntes = _bebes.Proximo;
			_bebes = new BebesNoDisco();   // a memoria some...
			CarregarBebes();               // ...e volta do arquivo
			BebeACaminho? lido = BebeDe(chichi);
			AfirmarFl("O DISCO: o bebe a caminho volta do `bebes.json` igual -- pais, raca, linhagens e a foto do poder -- e o proximo numero tambem",
				antes != null && lido != null && lido.Id == antes.Id && lido.Pai == antes.Pai && lido.Raca == antes.Raca
				&& lido.Linhagens.SequenceEqual(antes.Linhagens) && lido.BpDoPai == antes.BpDoPai && lido.BpDaMae == antes.BpDaMae
				&& _bebes.Proximo == proximoAntes, lido != null ? $"#{lido.Id} {lido.Pai} e {lido.Mae}" : "nada voltou");
		}
		catch (Exception e)
		{
			AfirmarFl("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			AmorDeUmLadoSoDeTeste = false;
			FilhoSemHerancaDeTeste = false;
			Filhos.FilhosComoAntesDeTeste = false;
			if (ModoDeRacas != modoAntes) EscolherModoDeRacas(modoAntes, "bancada dos filhos (volta)");
			_bebes = new BebesNoDisco();
			SalvarBebes();
			mesa.Fecha();
			ApagarContasDosFilhos();
			_filhosRodando = false;
			GD.Print($"[filhos] ================ {_flOk} OK, {_flFalhou} FALHA(S) ================");
		}
	}
}
