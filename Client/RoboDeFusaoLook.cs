using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Social;

namespace Jandirus.Client;

/// <summary>
/// ============================ BANCADA DE **QUEM A FUSAO E** (`--diagfusaolook`) ============================
/// A etapa anterior entregou o motor (chamar, aceitar, dancar, fundir). Esta responde a outra metade do
/// pedido do dono: **a fusao tem cara, nome, roupa e cabelo proprios**, e cada uma dessas quatro coisas
/// tem uma regra que ele ditou.
///
///     &lt;godot&gt; --headless --path . --diagfusaolook
///
/// ============================ O QUE ELA MEDE, E POR QUE NESTA ORDEM ============================
///   A. **A ENERGIA** -- os onze pontos da tabela que o dono mandou, minuto a minuto. E a unica parte
///      do pedido que veio com numeros conferiveis, entao ela e a primeira.
///   B. **O NOME** -- Metamoro e Potara dos MESMOS dois jogadores tem que dar nomes DIFERENTES, e os
///      dois tem que ser deterministicos (a mesma dupla, o mesmo nome, sempre).
///   C. **A ROUPA** -- a Danca SUBSTITUI o guarda-roupa pelo colete; a Potara SOMA o brinco.
///   D. **O CABELO** -- Goku + Vegeta da Vegito; o resto fica com o penteado de quem convidou.
///   E. **A ARTE EXISTE E RESOLVE** -- as cinco pecas, pelo banco de recursos IMPORTADOS e nao pela
///      pasta. Este projeto ja perdeu tempo duas vezes com arte que estava no disco e que o Godot
///      nunca importou.
///   F. **O SSJ4 DE TODA FUSAO** -- a folha do Gogeta pra QUALQUER penteado, homem e mulher, e ela tem
///      que ser DIFERENTE da que o mesmo corpo receberia sem estar fundido.
///   G. **O VERMELHO QUE CHEGA AO DESENHO, E SO NA DANCA** -- e este e o unico bloco que precisa de
///      explicacao. Depois da primeira versao desta bancada o dono corrigiu a regra: *"o ssj4 (e suas
///      variantes) quando esta na fusao potara, o cabelo nao fica vermelho e sim na cor normal de cabelo
///      q seria se n fosse uma fusao, so a fusao metamoro/danca q muda a cor do cabelo no ssj4"*. Por
///      isso todo par deste bloco e MEDIDO NOS DOIS TIPOS, no mesmo corpo -- ver a G17.
///
/// ============================ POR QUE O BLOCO G EXISTE, E O QUE ELE NAO PROVA ============================
/// A memoria deste projeto tem uma entrada inteira chamada *"a bancada mede INTENCAO"*: quatro mil
/// checagens verdes deixaram passar quatro defeitos visuais porque **uniform escrito nao e pixel
/// desenhado**. O vermelho do SSJ4 e exatamente esse tipo de risco, e de um jeito traicoeiro: a regra
/// derivada do cliente manda MATIZAR toda tinta que cai num sprite trazido pela forma, e matizar a folha
/// do SSJ4 (que e ESCURA, ao contrario da arte dourada de Super Saiyajin que aquela regra pressupoe)
/// desenharia **preto** em 47% do cabelo -- com o uniform da cor certinho escrito no material.
///
/// Entao este bloco faz duas coisas separadas, e diz qual e qual:
///   1. **le o uniform E o modo** (`tinta_modo`) que o caminho de producao escreveu -- e o modo e metade
///      da resposta;
///   2. **calcula o que aquele modo desenha**, aplicando as DUAS formulas do `Personagem.gdshader` sobre
///      os pixels REAIS de `Hair_SSJ4Gogeta.png`, lidos do arquivo aqui dentro.
///
/// **O passo 2 nao e uma foto da GPU** -- headless nao desenha --, e por isso ele nao vale sozinho: a
/// formula esta transcrita, e transcricao caduca. Por isso a prova G0 **abre o `.gdshader` e confere que
/// as duas linhas continuam sendo as que estao transcritas aqui**. Com ela vermelha, o resto do bloco G
/// nao quer dizer nada -- e e assim que se le.
/// ======================================================================================================
///
/// DEPOIS DA RODADA REAL ELA SE COBRA: cada regra recebe o defeito que ela existe pra pegar e TEM que
/// ficar vermelha (as linhas `[injecao]`). Regra que passa verde com o proprio defeito e falha DA
/// BANCADA -- e foi assim que se descobriu que o `matiz` precisava ser medido junto da cor.
/// </summary>
public partial class RoboDeFusaoLook : Node
{
	private int _ok, _falhou;

	private void Conferir(bool passou, string oque)
	{
		if (passou) { _ok++; GD.Print($"[fusaolook]  ok   {oque}"); }
		else { _falhou++; GD.Print($"[fusaolook]  FALHOU  {oque}"); }
	}

	public override void _Ready()
	{
		GD.Print("[fusaolook] ==== QUEM A FUSAO E: nome, roupa, cabelo e o vermelho do SSJ4 ====");

		VisualCatalog? cat = CarregarCatalogo();
		if (cat == null) { GD.Print("[fusaolook] sem visual.json -- nao da pra medir nada"); Sair(); return; }

		AEnergia();
		ONome();
		ARoupa(cat);
		OCabelo();
		AArteExiste(cat);
		OSsj4DeTodaFusao(cat);
		OVermelho();
		AInjecao(cat);

		GD.Print(_falhou == 0
			? $"[fusaolook] ===== TUDO OK ({_ok} provas) ====="
			: $"[fusaolook] ===== {_falhou} FALHA(S) em {_ok + _falhou} provas =====");
		Sair();
	}

	private void Sair() => GetTree().Quit(_falhou == 0 ? 0 : 1);

	private static VisualCatalog? CarregarCatalogo()
	{
		const string dados = "res://Assets/Data/visual.json";
		return Godot.FileAccess.FileExists(dados)
			? VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados))
			: null;
	}

	// =====================================================================
	// A. A ENERGIA -- a tabela do dono, os onze pontos
	// =====================================================================
	/// <summary>
	/// A TABELA QUE O DONO MANDOU, copiada dele: multiplicador da forma -> dreno por segundo. Ela e a
	/// prova de que `1 + mult/50` (`Fusion.dm:349`) e a formula certa e nao uma parecida.
	///
	/// OS DOIS PONTOS DE CHECAGEM que o pedido nomeia estao aqui dentro: **50x = 2,0/s = 15 min de
	/// Potara** (e 7,5 da Danca), e **2x = 1,04/s = 28,85 min de Potara** (14,42 da Danca).
	/// </summary>
	private static readonly (double Mult, double Dreno)[] TabelaDoDono =
	[
		(1, 1.00), (2, 1.04), (4, 1.08), (8, 1.16), (12, 1.24), (14, 1.28),
		(24, 1.48), (42, 1.84), (45, 1.90), (50, 2.00), (55, 2.10),
	];

	private void AEnergia()
	{
		GD.Print("[fusaolook] -- A. a energia de fusao --");

		Conferir(Fusao.EnergiaMaxima(TipoDeFusao.Danca) == 900, "A1 Danca nasce com 900 de energia (Fusion.dm:4)");
		Conferir(Fusao.EnergiaMaxima(TipoDeFusao.Potara) == 1800, "A2 Potara nasce com 1800 (Fusion.dm:5)");
		Conferir(Fusao.EnergiaMaxima(TipoDeFusao.Namek) == 0, "A3 Namekuseijin e permanente (energia 0)");

		foreach ((double mult, double dreno) in TabelaDoDono)
		{
			double meu = Fusao.DrenoPorSegundo(mult);
			Conferir(Math.Abs(meu - dreno) < 1e-9, $"A4 {mult}x drena {dreno:0.00}/s (deu {meu:0.0000})");
		}

		// OS DOIS NUMEROS DE CHECAGEM DO PEDIDO, EM MINUTOS -- e nao em dreno, porque foi assim que o
		// dono escreveu a tabela. Uma formula errada por pouco passa na coluna do dreno e cai aqui.
		double potara50 = Fusao.EnergiaDaPotara / Fusao.DrenoPorSegundo(50) / 60;
		double danca50 = Fusao.EnergiaDaDanca / Fusao.DrenoPorSegundo(50) / 60;
		Conferir(Math.Abs(potara50 - 15.0) < 0.01, $"A5 a 50x a Potara dura 15,00 min (deu {potara50:0.00})");
		Conferir(Math.Abs(danca50 - 7.5) < 0.01, $"A6 a 50x a Danca dura 7,50 min (deu {danca50:0.00})");

		double potara2 = Fusao.EnergiaDaPotara / Fusao.DrenoPorSegundo(2) / 60;
		double danca2 = Fusao.EnergiaDaDanca / Fusao.DrenoPorSegundo(2) / 60;
		Conferir(Math.Abs(potara2 - 28.85) < 0.01, $"A7 a 2x a Potara dura 28,85 min (deu {potara2:0.00})");
		Conferir(Math.Abs(danca2 - 14.42) < 0.01, $"A8 a 2x a Danca dura 14,42 min (deu {danca2:0.00})");

		// A BASE, que e o teto da tabela: 900/15 e 1800/30, o pedido ao pe da letra.
		Conferir(Math.Abs(Fusao.EnergiaDaDanca / 1.0 / 60 - 15) < 1e-9, "A9 sem forma, a Danca dura 15 min");
		Conferir(Math.Abs(Fusao.EnergiaDaPotara / 1.0 / 60 - 30) < 1e-9, "A10 sem forma, a Potara dura 30 min");

		// FORMA MENOR QUE 1 NAO ACELERA O DRENO (o `fm > 1 ?` do `Fusion.dm:349`). Sem esta guarda um
		// multiplicador de 0,5 daria 1,01 -- e, pior, um de 0 daria 1,0 por acidente e nao por regra.
		Conferir(Fusao.DrenoPorSegundo(0.5) == 1.0, "A11 forma abaixo de 1x nao mexe no dreno");
	}

	// =====================================================================
	// B. O NOME
	// =====================================================================
	private void ONome()
	{
		GD.Print("[fusaolook] -- B. o nome --");

		string danca = Fusao.NomeDaFusao(TipoDeFusao.Danca, "Goku", "Vegeta");
		string potara = Fusao.NomeDaFusao(TipoDeFusao.Potara, "Goku", "Vegeta");

		// A REGRA DO DONO, LITERAL: *"a metamoro pega a 1a metade do 1o + a 2a metade do 2o, e a potara
		// o inverso"*. Goku + Vegeta -> "Goeta" e "Vegku".
		Conferir(danca == "Goeta", $"B1 Metamoro de Goku+Vegeta = Goeta (deu {danca})");
		Conferir(potara == "Vegku", $"B2 Potara de Goku+Vegeta = Vegku (deu {potara})");

		// ============================ E ESTA E A PROVA QUE O PEDIDO PEDE ============================
		// *"Potara e Metamoro dos MESMOS jogadores tem nomes DIFERENTES"*. O DM tem uma formula so pros
		// dois tipos (`Fusion.dm:176-180`), entao esta linha ficaria VERMELHA com o porte literal.
		// ======================================================================================
		Conferir(danca != potara, "B3 os dois tipos dao nomes DIFERENTES pra mesma dupla");

		// DETERMINISTICO: mesma dupla, mesmo nome, sempre. Nao ha `Random` no caminho -- e uma bancada
		// que rodasse duas vezes e comparasse seria mais fraca que esta, que compara na mesma rodada.
		for (int i = 0; i < 50; i++)
			if (Fusao.NomeDaFusao(TipoDeFusao.Danca, "Goku", "Vegeta") != danca)
			{ Conferir(false, "B4 o nome e deterministico"); return; }
		Conferir(true, "B4 o nome e deterministico (50 chamadas, o mesmo nome)");

		// A ORDEM IMPORTA, e ela e "quem convidou primeiro". Sem isso a fusao de A com B teria o mesmo
		// nome da de B com A -- e o dono foi explicito em que quem convida e quem manda.
		Conferir(Fusao.NomeDaFusao(TipoDeFusao.Danca, "Vegeta", "Goku") != danca,
				 "B5 trocar quem convidou troca o nome");

		// NOMES DE UMA LETRA nao produzem string vazia (a rede de seguranca do `Fusion.dm:181-184`).
		Conferir(Fusao.NomeDaFusao(TipoDeFusao.Danca, "A", "B").Length > 0, "B6 nome de 1 letra nao some");
		Conferir(Fusao.NomeDaFusao(TipoDeFusao.Potara, "", "").Length > 0, "B7 nome vazio nao estoura");

		// MAIUSCULA NA PRIMEIRA LETRA -- e uma pessoa, nao um identificador.
		Conferir(char.IsUpper(danca[0]) && char.IsUpper(potara[0]), "B8 o nome comeca com maiuscula");
	}

	// =====================================================================
	// C. A ROUPA
	// =====================================================================
	private void ARoupa(VisualCatalog cat)
	{
		GD.Print("[fusaolook] -- C. a roupa --");

		string colete = cat.Peca(Fusao.PecaDoColeteMetamoran) ?? "";
		const string brinco = "res://Assets/Sprites/Clothes/potara.tres";

		var doConvidador = new List<PecaDeRoupa>
		{
			new("res://Assets/Sprites/Clothes/Clothes_GiTop.tres", new Rgb(60, 110, 220)),
			new("res://Assets/Sprites/Clothes/Clothes_GiBottom.tres"),
		};

		List<PecaDeRoupa> daDanca = Fusao.RoupaDaFusao(TipoDeFusao.Danca, doConvidador, colete);
		Conferir(daDanca.Count == 1 && daDanca[0].Caminho == colete,
				 $"C1 a Metamoro veste SO o colete metamoriano ({daDanca.Count} peca(s))");

		List<PecaDeRoupa> daPotara = Fusao.RoupaDaFusao(TipoDeFusao.Potara, doConvidador, brinco);
		Conferir(daPotara.Count == 3 && daPotara[0].Caminho == brinco,
				 $"C2 a Potara veste o brinco MAIS a roupa de quem convidou ({daPotara.Count} pecas)");

		// A COR DA ROUPA HERDADA SOBREVIVE. A peca e um `record struct` com a cor dentro, e um `RoupaDaFusao`
		// que remontasse a lista so com o caminho perderia a tintura de quem convidou -- o mesmo defeito
		// que o `VisualCatalog.Sanear` ja documenta ter tido uma vez.
		Conferir(daPotara.Any(p => p.Cor is { R: 60, G: 110, B: 220 }),
				 "C3 a cor que o convidador escolheu vai junto");

		// O TETO DO GUARDA-ROUPA, e a peca da fusao entra PRIMEIRO. Com quatro pecas vestidas, a Potara
		// tem que sair com o brinco + 3, e nao com 4 + brinco descartado.
		var cheio = new List<PecaDeRoupa>
		{
			new("res://a.tres"), new("res://b.tres"), new("res://c.tres"), new("res://d.tres"),
		};
		List<PecaDeRoupa> lotada = Fusao.RoupaDaFusao(TipoDeFusao.Potara, cheio, brinco);
		Conferir(lotada.Count == Appearance.MaxRoupa && lotada[0].Caminho == brinco,
				 $"C4 com o guarda-roupa cheio, o brinco entra e o excedente sai ({lotada.Count} pecas)");

		// ARTE QUE NAO RESOLVEU NAO DEIXA NINGUEM PELADO. A Danca cai na roupa de quem convidou -- e a
		// alternativa (lista vazia) seria um personagem sem roupa que ninguem saberia explicar.
		List<PecaDeRoupa> semArte = Fusao.RoupaDaFusao(TipoDeFusao.Danca, doConvidador, null);
		Conferir(semArte.Count == doConvidador.Count, "C5 sem a arte do colete, a Danca herda a roupa em vez de ficar pelada");

		// A NAMEKUSEIJIN NAO VESTE NADA POR CIMA -- `Fusion.dm:271` nao tem ramo de roupa pra ela.
		Conferir(Fusao.PecaDe(TipoDeFusao.Namek).Length == 0, "C6 a fusao Namekuseijin nao tem peca propria");

		// ============================ E AGORA A ROUPA VESTIDA NUM CORPO DE VERDADE ============================
		// Tudo acima e a REGRA. Isto e o resultado dela passando pelo `CharacterVisual.Vestir` de
		// producao -- e a diferenca nao e cerimonia: as camadas de roupa sao criadas a partir dos
		// caminhos, e um `.tres` que nao carregue produz uma camada VAZIA em vez de um erro. A folha do
		// brinco, alias, tem so 8 animacoes contra as 23 de uma roupa normal (ela e um acessorio, nao
		// veste o corpo todo) -- e e por isso que se confere que a camada nasceu com folha.
		// ================================================================================================
		var corpo = new CharacterVisual { Name = "AlvoDaRoupa" };
		AddChild(corpo);

		corpo.Vestir(cat, new Appearance { Cabelo = "Goku", Roupa = daPotara }, "Saiyan", "Male");
		List<string> naPotara = corpo.RoupasNoCorpoDeTeste();
		Conferir(naPotara.Count == 3 && naPotara[0].Contains("potara"),
				 $"C7 as tres camadas da Potara nascem no corpo, o brinco na primeira ({naPotara.Count})");

		corpo.Vestir(cat, new Appearance { Cabelo = "Goku", Roupa = daDanca }, "Saiyan", "Male");
		List<string> naDanca = corpo.RoupasNoCorpoDeTeste();
		Conferir(naDanca.Count == 1 && naDanca[0].Contains("Metamoran"),
				 $"C8 a Metamoro nasce com UMA camada, e ela e o colete ({string.Join(",", naDanca)})");

		// ============================ E TROCAR A APARENCIA TIRA A CAMADA QUE SOBROU ============================
		// A prova de que a fusao se DESFAZ no desenho. `Vestir` remonta as camadas, e o caminho de
		// producao (`Separar` -> `TrocarAparencias`) e exatamente esta chamada com a aparencia de
		// verdade: se ela nao derrubasse as camadas antigas, o jogador ficaria com o colete metamoriano
		// por cima da propria roupa depois que a fusao acabasse.
		// =================================================================================================
		corpo.Vestir(cat, new Appearance { Cabelo = "Goku", Roupa = doConvidador }, "Saiyan", "Male");
		List<string> devolta = corpo.RoupasNoCorpoDeTeste();
		Conferir(devolta.Count == 2 && !devolta.Any(p => p.Contains("Metamoran") || p.Contains("potara")),
				 $"C9 desfeita a fusao, as pecas dela somem do corpo ({devolta.Count} camadas)");

		corpo.QueueFree();
	}

	// =====================================================================
	// D. O CABELO
	// =====================================================================
	private void OCabelo()
	{
		GD.Print("[fusaolook] -- D. o cabelo --");

		Conferir(Fusao.CabeloDaFusao("Goku", "Vegeta") == Fusao.EstiloDoVegito,
				 "D1 Goku (convidou) + Vegeta = Vegito");
		Conferir(Fusao.CabeloDaFusao("Vegeta", "Goku") == Fusao.EstiloDoVegito,
				 "D2 Vegeta (convidou) + Goku = Vegito (a regra e simetrica)");

		// A FAMILIA DO VEGETA E MAIOR QUE O NOME EXATO -- o catalogo tem `GT Vegeta`, e o resolvedor de
		// cabelo do cliente ja trata os dois como o mesmo widow's peak.
		Conferir(Fusao.CabeloDaFusao("Goku", "GT Vegeta") == Fusao.EstiloDoVegito,
				 "D3 GT Vegeta conta como cabelo de Vegeta");

		// FORA DA EXCECAO, O CABELO E DE QUEM CONVIDOU -- a mesma regra da raca e das transformacoes.
		Conferir(Fusao.CabeloDaFusao("Mohawk", "Afro") == "Mohawk", "D4 fora do par, vale o cabelo de quem convidou");
		Conferir(Fusao.CabeloDaFusao("Goku", "Goku") == "Goku", "D5 dois Gokus nao viram Vegito");
		Conferir(Fusao.CabeloDaFusao("Vegeta", "Vegeta") == "Vegeta", "D6 dois Vegetas nao viram Vegito");
		Conferir(Fusao.CabeloDaFusao("Bald", "Vegeta") == "Bald", "D7 careca + Vegeta continua careca");
	}

	// =====================================================================
	// E. A ARTE EXISTE -- e "existe" quer dizer IMPORTADA
	// =====================================================================
	private void AArteExiste(VisualCatalog cat)
	{
		GD.Print("[fusaolook] -- E. as cinco pecas de arte --");

		// PELO BANCO DE RECURSOS e nao pela pasta: `ResourceLoader.Exists` e a unica pergunta que
		// corresponde ao que o jogo vai conseguir carregar. Ver `ColadasDeForma.Existe`.
		Conferir(ResourceLoader.Exists("res://Assets/Sprites/Clothes/Metamoran Vest.tres"),
				 "E1 `Metamoran Vest` importado");
		Conferir(ResourceLoader.Exists("res://Assets/Sprites/Clothes/potara.tres"), "E2 `potara` importado");
		Conferir(ResourceLoader.Exists("res://Assets/Sprites/Hair/VegitoHairPVP.tres"),
				 "E3 `VegitoHairPVP` importado");
		Conferir(ResourceLoader.Exists(CabelosDeForma.FolhaDoSsj4DaFusao), "E4 `Hair_SSJ4Gogeta` importado");

		// O COLETE ESTA NO CATALOGO -- e e por ele que o servidor o acha, por NOME, pra a pasta poder
		// mudar sem quebrar nada.
		Conferir(cat.Peca(Fusao.PecaDoColeteMetamoran) != null,
				 "E5 o colete resolve pelo catalogo (por nome, nao por caminho)");

		// ============================ E O BRINCO **NAO** ESTA, E ISSO E UMA PROVA E NAO UM BURACO ============================
		// O extrator o recusa por nome (`DmAppearanceScanner.Varrer`, a lista `fora`) porque a pasta
		// `Clothes/` do jogo e o deposito de todo overlay de corpo -- varre-la inteira poria "olhos" na
		// grade de camisas da criacao. Esta linha existe pra que o dia em que alguem "consertar" isso
		// jogando o brinco no guarda-roupa fique VERMELHO: a regra de la esta certa, e e por ela que a
		// aparencia da fusao nao passa pelo `Sanear`.
		// ============================================================================================================
		Conferir(cat.Peca(Fusao.PecaDosBrincosPotara) == null,
				 "E6 o brinco NAO esta no guarda-roupa (e nao deve estar) -- ele e overlay de fusao");

		// O CABELO DO VEGITO E UM ESTILO DO CATALOGO, e nao um arquivo solto. Se ele nao fosse, o
		// `Sanear` trocaria o penteado da fusao por `Bald` -- a fusao sairia CARECA.
		Conferir(cat.TemCabeloChamado(Fusao.EstiloDoVegito),
				 "E7 `Vegito` e um estilo do catalogo (senao o saneamento deixaria a fusao careca)");
		Conferir(cat.SpriteDoCabelo(Fusao.EstiloDoVegito)?.Contains("VegitoHairPVP") == true,
				 "E8 o estilo `Vegito` aponta pra folha `VegitoHairPVP`");
	}

	// =====================================================================
	// F. O SSJ4 DE TODA FUSAO
	// =====================================================================
	private void OSsj4DeTodaFusao(VisualCatalog cat)
	{
		GD.Print("[fusaolook] -- F. o SSJ4 de toda fusao --");

		// ============================ TODO PENTEADO JOGAVEL, E NAO UMA AMOSTRA ============================
		// *"TODA fusao tem cabelo vermelho no SSJ4, tendo ou nao o cabelo do Vegito"* -- entao a prova
		// tem que varrer o catalogo inteiro. Uma amostra de tres penteados passaria verde com um `if`
		// que so pegasse os tres.
		// ==========================================================================================
		int comFolha = 0, diferente = 0, penteados = 0;
		foreach ((string nome, string? sprite) in cat.Cabelos)
		{
			if (sprite == null) continue;   // `Bald` nao tem folha
			penteados++;

			string? daFusao = CabelosDeForma.De(sprite, Fusao.SufixoDoSsj4, feminino: false, fusao: true);
			string? normal = CabelosDeForma.De(sprite, Fusao.SufixoDoSsj4, feminino: false, fusao: false);

			if (daFusao == CabelosDeForma.FolhaDoSsj4DaFusao) comFolha++;
			if (daFusao != normal) diferente++;
			else GD.Print($"[fusaolook]    (o penteado '{nome}' recebe a MESMA folha fundido e solto)");
		}

		Conferir(penteados > 50, $"F1 o catalogo tem penteado pra varrer ({penteados} com folha)");
		Conferir(comFolha == penteados, $"F2 os {penteados} penteados recebem a folha do Gogeta no SSJ4 ({comFolha})");

		// ============================ E ELA TEM QUE SER OUTRA ============================
		// Esta e a prova que separa "a regra existe" de "a regra faz alguma coisa". Sem ela, um bug que
		// devolvesse a folha comum nos dois casos deixaria a F2 verde se a folha comum FOSSE a do Gogeta.
		// ============================================================================
		Conferir(diferente == penteados,
				 $"F3 e ela e DIFERENTE da que o mesmo corpo receberia solto ({diferente}/{penteados})");

		// O FEMININO NAO DESVIA. Sem fusao, mulher recebe `Hair_SSJ4Female`; com fusao, a mesma folha do
		// Gogeta -- porque a fusao ja e um corpo terceiro, e o dono disse "TODA".
		string? fem = CabelosDeForma.De("res://Assets/Sprites/Hair/Hair_FemaleLong.tres",
										Fusao.SufixoDoSsj4, feminino: true, fusao: true);
		Conferir(fem == CabelosDeForma.FolhaDoSsj4DaFusao, $"F4 corpo feminino fundido tambem usa a do Gogeta (deu {fem})");

		// O VEGETA TAMBEM NAO. Fora da fusao ele tem folha propria de SSJ4 (`Hair VegetaSSJ4`, o widow's
		// peak que sobrevive a transformacao) -- e essa e a excecao mais provavel de sobreviver por engano.
		string? veg = CabelosDeForma.De("res://Assets/Sprites/Hair/Hair_Vegeta.tres",
										Fusao.SufixoDoSsj4, feminino: false, fusao: true);
		Conferir(veg == CabelosDeForma.FolhaDoSsj4DaFusao, $"F5 nem o cabelo de Vegeta escapa da folha do Gogeta (deu {veg})");

		// ============================ E A FUSAO NAO MUDA MAIS NENHUMA FORMA ============================
		// O bit e do CORPO e vale pra sempre; se ele vazasse pros outros degraus, uma fusao veria o
		// cabelo do Gogeta no SSJ1 tambem. Varrido nos sufixos que a escada usa.
		// ========================================================================================
		foreach (string s in new[] { "SSj", "SSj2", "SSj3", "USSj", "LSSj", "SSjFP" })
		{
			string? comF = CabelosDeForma.De("res://Assets/Sprites/Hair/Hair_Goku.tres", s, false, fusao: true);
			string? semF = CabelosDeForma.De("res://Assets/Sprites/Hair/Hair_Goku.tres", s, false, fusao: false);
			Conferir(comF == semF, $"F6 no `{s}` a fusao nao muda a folha (fundido {comF ?? "nada"})");
		}

		// ============================ O `Hair_VegitoSSj` DEIXOU DE SER FOLHA MORTA ============================
		// Achado nesta etapa: o penteado se chama `VegitoHairPVP` (com "Hair" no MEIO) e as variantes tem
		// DOIS nomes -- `VegitoHairPVPSSj2`/`VegitoHairPVPSSjFP` e `Hair_VegitoSSj`. O SSJ1 nao casava com
		// nenhum padrao, entao o Vegito virava Super Saiyajin com o cabelo PRETO da base, com a folha
		// certa parada na pasta. Isto deixou de ser canto no dia em que `Vegito` virou o cabelo de toda
		// fusao de Goku com Vegeta.
		// ================================================================================================
		string? vegitoSsj = CabelosDeForma.De("res://Assets/Sprites/Hair/VegitoHairPVP.tres", "SSj");
		Conferir(vegitoSsj != null && vegitoSsj.Contains("VegitoSSj"),
				 $"F7 o Vegito acha a folha de SSJ1 (`Hair_VegitoSSj`) -- deu {vegitoSsj ?? "nada"}");
		Conferir(CabelosDeForma.De("res://Assets/Sprites/Hair/VegitoHairPVP.tres", "SSj2") != null,
				 "F8 e continua achando a de SSJ2 pelo padrao antigo");
	}

	// =====================================================================
	// G. O VERMELHO QUE CHEGA AO DESENHO
	// =====================================================================
	/// <summary>
	/// As DUAS linhas do `fragment` do `Personagem.gdshader`, transcritas. A prova G0 confere que elas
	/// continuam la -- ver o cabecalho desta classe pro porque isso e obrigatorio.
	/// </summary>
	private const string LinhaDoMatiz = "c.rgb = clamp(tinta * (luz * 2.0), 0.0, 1.0);";
	private const string LinhaDaSoma = "c.rgb = clamp(c.rgb + tinta, 0.0, 1.0);";
	private const string LinhaDaLuz = "float luz = dot(c.rgb, vec3(0.299, 0.587, 0.114));";

	private void OVermelho()
	{
		GD.Print("[fusaolook] -- G. o vermelho que chega ao desenho --");

		// ---- G0: a formula transcrita ainda e a do shader ----
		const string shader = "res://Assets/Shaders/Personagem.gdshader";
		string fonte = Godot.FileAccess.FileExists(shader) ? Godot.FileAccess.GetFileAsString(shader) : "";
		bool transcricaoValida = fonte.Contains(LinhaDoMatiz) && fonte.Contains(LinhaDaSoma) && fonte.Contains(LinhaDaLuz);
		Conferir(transcricaoValida,
				 "G0 as duas formulas transcritas aqui ainda sao as do `Personagem.gdshader` "
			   + "(com esta vermelha, o resto do bloco G nao quer dizer nada)");

		// ---- G1: o Core devolve tinta so no SSJ4, e so na DANCA ----
		// ============================ A CORRECAO DO DONO MORA AQUI, E ELA E UM PAR ============================
		// *"o ssj4 (e suas variantes) quando esta na fusao potara, o cabelo nao fica vermelho e sim na cor
		// normal de cabelo q seria se n fosse uma fusao, so a fusao metamoro/danca q muda a cor do cabelo
		// no ssj4"*.
		//
		// UMA PROVA SO NAO SERVE. "A Danca pinta" fica VERDE numa implementacao que pinte as tres -- que
		// e exatamente a regra velha, a que este passe veio corrigir. A prova e o PAR: uma afirma a cor,
		// a outra afirma a ausencia dela, e nenhuma das duas sozinha distingue as duas regras.
		// ==================================================================================================
		Conferir(Fusao.TintaDoCabeloDaFusao(TipoDeFusao.Danca, Fusao.SufixoDoSsj4) == Fusao.VermelhoDoCabeloDaFusao,
				 "G1 a DANCA (Metamoro) pinta o SSJ4 de vermelho");
		Conferir(Fusao.TintaDoCabeloDaFusao(TipoDeFusao.Potara, Fusao.SufixoDoSsj4) == null,
				 "G1b a POTARA NAO pinta o SSJ4 -- cor normal de cabelo (correcao do dono)");
		Conferir(Fusao.TintaDoCabeloDaFusao(TipoDeFusao.Namek, Fusao.SufixoDoSsj4) == null,
				 "G1c e a NAMEKUSEIJIN tambem nao (os dois lados dela sao Namekuseijin -- nao ha SSJ4 la)");

		foreach (TipoDeFusao t in new[] { TipoDeFusao.Danca, TipoDeFusao.Potara, TipoDeFusao.Namek })
			foreach (string s in new[] { "SSj", "SSj2", "SSj3", "USSj", "LSSj", "SSjFP", "" })
				Conferir(Fusao.TintaDoCabeloDaFusao(t, s) == null, $"G2 a fusao {t} NAO pinta o `{s}`");

		// ---- G3: o que as duas operacoes desenham na folha REAL ----
		var folha = new Image();
		string png = "res://Assets/Sprites/Hair/SSJ Hairs/Hair_SSJ4Gogeta.png";
		Image? img = Godot.FileAccess.FileExists(png) ? Image.LoadFromFile(png) : null;
		if (img == null) { Conferir(false, "G3 a folha do SSJ4 do Gogeta abre pra ser medida"); return; }
		folha = img;

		// OS TONS DA FOLHA, contados. Uma folha de pixel art tem poucos tons e eles SAO o relevo.
		var tons = new Dictionary<uint, int>();
		for (int y = 0; y < folha.GetHeight(); y++)
			for (int x = 0; x < folha.GetWidth(); x++)
			{
				Color c = folha.GetPixel(x, y);
				if (c.A < 0.5f) continue;
				uint k = ((uint)c.R8 << 16) | ((uint)c.G8 << 8) | (uint)c.B8;
				tons[k] = tons.GetValueOrDefault(k) + 1;
			}

		List<(uint Tom, int Quantos)> ordenados = [.. tons.Select(kv => (kv.Key, kv.Value)).OrderByDescending(t => t.Value)];
		Conferir(ordenados.Count >= 4, $"G3 a folha tem relevo pra medir ({ordenados.Count} tons opacos)");
		if (ordenados.Count < 4) return;

		var tinta = new Color(Fusao.VermelhoDoCabeloDaFusao);
		int opacos = ordenados.Sum(t => t.Quantos);

		// ============================ O PISO E O QUE DECIDE, E ELE E QUASE METADE DO CABELO ============================
		// A licao da rampa de matiz deste projeto, ao pe da letra: *"o hexa escrito e o pixel mais
		// ESCURO"*. Calibrar so pelo topo ja deixou o Blue marinho e o Rose vinho, e o dono reclamou
		// duas vezes. Aqui o tom mais comum da folha e o mais escuro dela -- entao ele e a prova.
		// ========================================================================================================
		(uint tomPiso, int quantosNoPiso) = ordenados[0];
		double fracaoDoPiso = (double)quantosNoPiso / opacos;
		Conferir(fracaoDoPiso > 0.30,
				 $"G4 o tom mais comum da folha e {fracaoDoPiso * 100:0.0}% dela -- e ele que decide a leitura");

		var desenhadosSoma = new List<Color>();
		var desenhadosMatiz = new List<Color>();
		foreach ((uint tom, int _) in ordenados.Take(4))
		{
			Color c = DeHex(tom);
			desenhadosSoma.Add(Soma(c, tinta));
			desenhadosMatiz.Add(Matiz(c, tinta));
		}

		GD.Print($"[fusaolook]    folha  : {string.Join(" ", ordenados.Take(4).Select(t => "#" + t.Tom.ToString("x6")))}");
		GD.Print($"[fusaolook]    SOMA   : {string.Join(" ", desenhadosSoma.Select(Hex))}");
		GD.Print($"[fusaolook]    MATIZ  : {string.Join(" ", desenhadosMatiz.Select(Hex))}");

		// ---- a soma desenha VERMELHO, com relevo ----
		Color pisoSoma = Soma(DeHex(tomPiso), tinta);
		Conferir(EhVermelho(pisoSoma), $"G5 SOMA: o piso do cabelo sai VERMELHO ({Hex(pisoSoma)})");
		Conferir(desenhadosSoma.Select(Hex).Distinct().Count() == 4,
				 "G6 SOMA: os quatro tons continuam distintos (o relevo do desenho sobrevive)");
		Conferir(desenhadosSoma.All(c => c.R > c.G && c.R > c.B),
				 "G7 SOMA: o vermelho domina em todos os quatro tons");

		// ---- e o CONTRA-EXEMPLO: o matiz desenharia preto ----
		// Esta prova nao mede o codigo de producao: ela mede a DECISAO. Ela e a razao de o
		// `VestirCabeloDaForma` ter um ramo proprio pra fusao em vez de cair na regra derivada
		// (`matiz: trocou`), e sem ela essa escolha seria opiniao.
		Color pisoMatiz = Matiz(DeHex(tomPiso), tinta);
		Conferir(Luma(pisoMatiz) < 0.10,
				 $"G8 MATIZ desenharia o piso quase PRETO ({Hex(pisoMatiz)}) -- e por isso a tinta SOMA");

		// ---- G9: o caminho de PRODUCAO escreve a cor E o modo ----
		// Ate aqui tudo foi conta. Esta prova pergunta ao material o que o `VestirCabeloDaForma` de
		// verdade escreveu -- que e a metade que a memoria deste projeto manda nunca supor.
		MedirOMaterial();
	}

	/// <summary>
	/// O QUE O CAMINHO DE PRODUCAO ESCREVEU NO MATERIAL do cabelo. Monta um corpo de verdade, veste a
	/// ficha de verdade e chama o `VestirCabeloDaForma` de verdade -- nada aqui e simulado.
	/// </summary>
	private void MedirOMaterial()
	{
		VisualCatalog? cat = CarregarCatalogo();
		if (cat == null) return;

		Jandirus.Core.Forms.FormaDef? ssj4 = PrimeiraFormaComSufixo(Fusao.SufixoDoSsj4);
		if (ssj4 == null) { Conferir(false, "G9 o catalogo de formas tem um SSJ4 pra vestir"); return; }

		var vis = new CharacterVisual { Name = "AlvoDaFusao" };
		AddChild(vis);
		vis.Vestir(cat, new Appearance { Cabelo = Fusao.EstiloDoVegito }, "Saiyan", "Male");

		// ---- sem fusao: o SSJ4 nao pinta cabelo de ninguem ----
		vis.MarcarFusao(null);
		vis.VestirCabeloDaForma(ssj4);
		(Vector3 Tinta, int Modo)? solto = vis.TintaDoCabeloDeTeste;
		Conferir(solto is { } s && s.Tinta.LengthSquared() < 1e-6f,
				 $"G9 SEM fusao o SSJ4 nao poe tinta nenhuma no cabelo (deu {solto?.Tinta.ToString() ?? "nada"})");
		string folhaSolto = vis.CabeloDeTeste;

		// ---- na DANCA: a cor certa, no modo certo, na folha certa ----
		vis.MarcarFusao(TipoDeFusao.Danca);
		vis.VestirCabeloDaForma(ssj4);
		(Vector3 Tinta, int Modo)? fundido = vis.TintaDoCabeloDeTeste;

		var esperada = new Color(Fusao.VermelhoDoCabeloDaFusao);
		bool corBate = fundido is { } f
					&& Math.Abs(f.Tinta.X - esperada.R) < 0.01f
					&& Math.Abs(f.Tinta.Y - esperada.G) < 0.01f
					&& Math.Abs(f.Tinta.Z - esperada.B) < 0.01f;
		Conferir(corBate, $"G10 na DANCA o material recebe o vermelho `{Fusao.VermelhoDoCabeloDaFusao}` "
						+ $"(deu {fundido?.Tinta.ToString() ?? "nada"})");

		// ============================ O MODO E METADE DA RESPOSTA ============================
		// Esta e a linha que a bancada existe pra ter. Com `tinta_modo = 1` a G10 acima ficaria VERDE e o
		// cabelo desenharia preto -- exatamente o modo de falha que a memoria do projeto registra
		// ("uniform escrito nao e pixel desenhado").
		// ================================================================================
		Conferir(fundido is { Modo: 0 }, $"G11 e ela e SOMA e nao matiz (tinta_modo = {fundido?.Modo.ToString() ?? "?"})");

		Conferir(vis.CabeloDeTeste == CabelosDeForma.FolhaDoSsj4DaFusao,
				 $"G12 e a folha na cabeca e a do Gogeta (deu {vis.CabeloDeTeste})");
		Conferir(vis.CabeloDeTeste != folhaSolto, "G13 e ela e outra folha que a de quem nao esta fundido");

		// ============================ E AGORA A POTARA, NO MESMO CORPO -- A CORRECAO DO DONO NO PIXEL ============================
		// **AS DUAS TELAS TEM QUE DISCORDAR.** A memoria deste projeto guarda a licao ao pe da letra:
		// *"as duas telas concordam" fica verde com as duas erradas igual*. Medir so a Danca deixaria
		// passar uma implementacao que ignorasse o tipo e pintasse tudo de vermelho -- que e a regra
		// VELHA. Entao o MESMO corpo veste a Potara e as tres perguntas se repetem, com a resposta
		// invertida em UMA delas (a tinta) e IGUAL na outra (a folha).
		//
		// A G16 e a que registra a leitura que eu fiz do pedido: o dono falou de COR nas duas metades da
		// frase (*"nao fica vermelho e sim na COR normal"*, *"so a danca q muda a COR"*), entao a CABECA
		// da fusao continua sendo a do Gogeta na Potara. Se ele quiser que a folha volte tambem, e esta
		// linha que muda -- e nao um `if` novo escondido no cliente.
		// =============================================================================================================
		vis.MarcarFusao(TipoDeFusao.Potara);
		vis.VestirCabeloDaForma(ssj4);
		(Vector3 Tinta, int Modo)? potara = vis.TintaDoCabeloDeTeste;
		Conferir(potara is { } pt && pt.Tinta.LengthSquared() < 1e-6f,
				 $"G15 na POTARA o SSJ4 fica SEM tinta -- a cor normal do cabelo "
			   + $"(deu {potara?.Tinta.ToString() ?? "nada"})");
		Conferir(vis.CabeloDeTeste == CabelosDeForma.FolhaDoSsj4DaFusao,
				 $"G16 ...mas a CABECA continua a do Gogeta -- o dono falou de COR (deu {vis.CabeloDeTeste})");
		Conferir(potara?.Tinta != fundido?.Tinta,
				 "G17 e as duas fusoes DISCORDAM no mesmo corpo (Danca pinta, Potara nao)");

		// ---- e SAIR da fusao devolve o cabelo ----
		// Nada aqui e permanente: a fusao acaba, e o corpo tem que voltar. O tombo do `ussj_saved_icon`
		// do DM foi exatamente este, com o corpo do USSJ.
		vis.MarcarFusao(null);
		vis.VestirCabeloDaForma(null);
		(Vector3 Tinta, int Modo)? voltou = vis.TintaDoCabeloDeTeste;
		Conferir(voltou is { } v && v.Tinta.LengthSquared() < 1e-6f,
				 $"G14 desfeita a fusao, o vermelho SAI do cabelo (deu {voltou?.Tinta.ToString() ?? "nada"})");

		vis.QueueFree();
	}

	private static Jandirus.Core.Forms.FormaDef? PrimeiraFormaComSufixo(string sufixo) =>
		Jandirus.Core.Forms.Catalogo.Todas.FirstOrDefault(
			d => string.Equals(d.SufixoDoCabelo, sufixo, StringComparison.OrdinalIgnoreCase));

	// =====================================================================
	// A INJECAO -- a bancada se cobra
	// =====================================================================
	/// <summary>
	/// CADA REGRA RECEBE O DEFEITO QUE ELA EXISTE PRA PEGAR, e TEM que ficar vermelha. Regra que passa
	/// verde com o proprio defeito e falha DA BANCADA -- e nao do codigo.
	///
	/// Aqui a injecao e feita nos VALORES (nao da pra reescrever constante em tempo de execucao), e cada
	/// uma reproduz um erro que este porte de fato quase cometeu.
	/// </summary>
	private void AInjecao(VisualCatalog cat)
	{
		GD.Print("[fusaolook] -- injecao: cada regra tem que ficar vermelha com o proprio defeito --");

		// 1. A FORMULA DA ENERGIA "quase certa" (`/40` em vez de `/50`) passaria no ponto base e cairia
		//    nos outros dez. E o erro mais provavel de todos: um decimo trocado.
		double erradaA = 1 + 50 / 40.0;
		Injetar(Math.Abs(erradaA - 2.00) >= 1e-9, "A4 pega um dreno com o divisor trocado (/40)");

		// 2. A FORMULA UNICA DE NOME dos dois tipos -- que e literalmente o que o DM faz.
		string umSo = Fusao.PrimeiraMetade("Goku") + Fusao.SegundaMetade("Vegeta");
		Injetar(umSo == Fusao.NomeDaFusao(TipoDeFusao.Danca, "Goku", "Vegeta")
			 && umSo != Fusao.NomeDaFusao(TipoDeFusao.Potara, "Goku", "Vegeta"),
				"B3 pega o dia em que os dois tipos voltarem a ter a mesma formula");

		// 3. A METAMORO HERDANDO ROUPA -- o defeito seria trocar `RoupaDaFusao` por "sempre soma".
		var doConvidador = new List<PecaDeRoupa> { new("res://x.tres") };
		List<PecaDeRoupa> somandoErrado = Fusao.RoupaDaFusao(TipoDeFusao.Potara, doConvidador, "res://colete.tres");
		Injetar(somandoErrado.Count != 1, "C1 pega a Metamoro herdando roupa (a regra da Potara na Danca)");

		// 4. O CABELO DE QUEM CONVIDOU no lugar do Vegito.
		Injetar(Fusao.CabeloDaFusao("Mohawk", "Vegeta") != Fusao.EstiloDoVegito,
				"D1 pega o par que NAO e Goku+Vegeta caindo no Vegito");

		// 5. A FOLHA COMUM DE SSJ4 no lugar da do Gogeta -- o defeito de esquecer o bit da fusao.
		string? comum = CabelosDeForma.De("res://Assets/Sprites/Hair/Hair_Goku.tres",
										  Fusao.SufixoDoSsj4, false, fusao: false);
		Injetar(comum != CabelosDeForma.FolhaDoSsj4DaFusao,
				"F2 pega o corpo fundido recebendo a folha comum de SSJ4");

		// 6. O MATIZ NO LUGAR DA SOMA -- o defeito que a G11 existe pra pegar, e o unico dos seis que
		//    deixaria TODAS as outras provas verdes.
		var tinta = new Color(Fusao.VermelhoDoCabeloDaFusao);
		Color pisoDaFolha = new(8 / 255f, 8 / 255f, 8 / 255f);   // `#080808`, 47% da folha
		Injetar(Luma(Matiz(pisoDaFolha, tinta)) < Luma(Soma(pisoDaFolha, tinta)) / 4,
				"G11 pega o matiz -- ele desenha menos de um quarto do brilho da soma no piso");

		// 7. O BRINCO NO GUARDA-ROUPA, que quebraria a razao de a aparencia da fusao ser transiente.
		Injetar(!cat.Roupas.Any(r => r.Contains("potara", StringComparison.OrdinalIgnoreCase)),
				"E6 pega o dia em que o brinco entrar no guarda-roupa da criacao");

		// 8. A REGRA VELHA DE VOLTA -- *"TODA fusao tem cabelo vermelho no SSJ4"*, que era o que estava
		//    escrito aqui ate o dono corrigir. Ela deixaria a G1, a G10, a G11 e a G12 VERDES: e o unico
		//    defeito deste arquivo que so o PAR (G1/G1b, G10/G15) pega. Esta injecao e o que prova que o
		//    par existe -- se alguem apagar a metade negativa, esta linha cai junto.
		Injetar(Fusao.TintaDoCabeloDaFusao(TipoDeFusao.Danca, Fusao.SufixoDoSsj4)
			 != Fusao.TintaDoCabeloDaFusao(TipoDeFusao.Potara, Fusao.SufixoDoSsj4),
				"G1b pega o dia em que a Potara voltar a pintar junto com a Danca");
	}

	private void Injetar(bool ficouVermelha, string oque) =>
		Conferir(ficouVermelha, $"[injecao] {oque}");

	// =====================================================================
	// As duas contas do shader, e as ferramentas de cor
	// =====================================================================
	/// <summary>`c.rgb = clamp(c.rgb + tinta, 0, 1)` -- o `else` do `fragment`, e o `ICON_ADD` do BYOND.</summary>
	private static Color Soma(Color c, Color tinta) => new(
		Math.Clamp(c.R + tinta.R, 0f, 1f),
		Math.Clamp(c.G + tinta.G, 0f, 1f),
		Math.Clamp(c.B + tinta.B, 0f, 1f));

	/// <summary>`c.rgb = clamp(tinta * (luz * 2.0), 0, 1)` -- o `tinta_modo == 1`.</summary>
	private static Color Matiz(Color c, Color tinta)
	{
		float luz = c.R * 0.299f + c.G * 0.587f + c.B * 0.114f;
		return new Color(
			Math.Clamp(tinta.R * luz * 2f, 0f, 1f),
			Math.Clamp(tinta.G * luz * 2f, 0f, 1f),
			Math.Clamp(tinta.B * luz * 2f, 0f, 1f));
	}

	private static Color DeHex(uint rgb) =>
		new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

	private static string Hex(Color c) => $"#{c.R8:x2}{c.G8:x2}{c.B8:x2}";

	private static double Luma(Color c) => c.R * 0.299 + c.G * 0.587 + c.B * 0.114;

	/// <summary>
	/// ESTE PIXEL LE COMO VERMELHO? Nao e "o canal R e o maior": um cinza claro tambem passaria nisso
	/// se os tres fossem parecidos. Vermelho e R alto **com** distancia dos outros dois.
	/// </summary>
	private static bool EhVermelho(Color c) => c.R > 0.55f && c.R - Math.Max(c.G, c.B) > 0.35f;
}
