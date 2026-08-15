using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ BANCADA DA ARTE DOS ATAQUES DE KI ============================
/// O pedido do dono foi *"vc n ta utilizando os ICONES DE BEAM q era usado no byond pra dar mais
/// VARIEDADE nos ataques de ki"*, e a resposta so vale se TRES coisas forem verdade ao mesmo tempo:
/// a tabela escolhe folhas diferentes, as folhas EXISTEM no disco, e o pixel desenhado muda.
///
/// ============================ ELA MEDE PIXEL, E ISSO E O PONTO ============================
/// Este projeto tem registro de quatro defeitos visuais que passaram por quatro mil checagens verdes
/// porque a bancada media INTENCAO: uniform escrito nao e pixel desenhado, `Modulate` nao e tela, e
/// "as duas telas concordam" fica verde com as duas erradas igual. E tem registro de 35 atlas
/// ESCRITOS e nunca importados -- 178 animacoes mortas que nenhum teste de codigo notaria.
///
/// Entao a familia 3 aqui nao pergunta "que arte a tabela devolveu": ela DESENHA dois tiros com
/// artes diferentes, fotografa os dois e exige que as assinaturas de pixel sejam DIFERENTES. Uma
/// tabela perfeita com todas as folhas apontando pro mesmo `.tres` passaria na familia 1 e
/// REPROVARIA aqui -- que e exatamente o defeito que o dono relatou.
///
///     &lt;godot&gt; --path . --diagartedeki            (familias 1 e 2; roda no headless)
///     &lt;godot&gt; --path . --diagartedeki            (COM janela: a familia 3 precisa de pixel)
/// ======================================================================================
///
/// SEM REDE E SEM MUNDO, como a `--diagtinta`: arte de tiro nao depende de zona, de servidor nem de
/// login. O que ela toca e folha, shader e um quadro desenhado.
/// </summary>
public partial class RoboDeArteDeKi : Node2D
{
	private readonly List<string> _linhas = [];
	private int _falhas, _t;
	private ProjetilDesenhado? _a, _b;
	private ColorRect _fundo = null!;

	/// <summary>
	/// FUNDO VERDE PURO: nao existe em folha de ki nenhuma (elas sao cinzas tingidas de azul,
	/// vermelho ou branco), entao "nao e o fundo" separa o desenho do resto sem saber onde ele caiu.
	/// E o mesmo truque do magenta da `--diagtinta`, com outra cor porque aqui a tinta de teste E
	/// magenta.
	/// </summary>
	private static readonly Color Fundo = new(0f, 1f, 0f);

	private const int XA = 320, XB = 1200, YTiro = 400;

	private void Ok(string oque, bool passou)
	{
		_linhas.Add((passou ? "  OK   " : "  FALHA") + "  " + oque);
		if (!passou) _falhas++;
	}

	private void Nota(string t) => _linhas.Add("   --    " + t);

	public override void _Ready()
	{
		_fundo = new ColorRect
		{
			Color = Fundo, Size = new Vector2(4000, 4000),
			Position = new Vector2(-500, -500), ZIndex = -100,
		};
		AddChild(_fundo);

		Familia1();
		Familia2();
		MontarOsDoisTiros();
	}

	// =====================================================================
	// FAMILIA 1 -- A TABELA
	// =====================================================================
	/// <summary>
	/// OS VERBOS DE PRODUCAO, um por um. A lista e escrita A MAO e nao lida da propria tabela, e
	/// isso e deliberado: uma bancada que varre a tabela pra conferir a tabela sempre passa. O que
	/// esta escrito aqui e a lista dos verbos que o JOGO despacha (`UsarTecnicasDeProjetil`,
	/// `IdsG5`, o lote G6 e o G7), e ela reprova no dia em que alguem portar uma tecnica de tiro e
	/// esquecer a arte -- que e o unico defeito que esta familia existe pra pegar.
	/// </summary>
	private static readonly string[] VerbosDeProducao =
	[
		"Ki_Wave", "Basic_Blast", "Guided_Ball",
		"Masenko", "Makkankosappo", "Massive_Beam", "Final_Flash",
		"Charged_Shot", "KillDriver", "BusterShell", "Scattershot", "Energy_Barrage",
		"Ki_Bomb", "Hellzone_Grenade", "Kienzan", "Paralysis", "Stunlock",
		"Kamehameha", "GalicGun", "Death_Beam", "Dodompa", "Enkumei", "Boom_Wave", "Kikoho",
		"Scattering_Bullet", "Spirit_Gun",
	];

	private void Familia1()
	{
		_linhas.Add("=== FAMILIA 1: a tabela responde por todo verbo que atira ===");

		var vistas = new Dictionary<ArteDeKi, List<string>>();
		int semArte = 0;
		foreach (string v in VerbosDeProducao)
		{
			ArteDeKi a = ArteDeProjetil.De(v, "Human", "", 12345);
			if (a == ArteDeKi.Nenhuma) { Nota($"SEM ARTE: {v}"); semArte++; continue; }
			(vistas.TryGetValue(a, out List<string>? l) ? l : vistas[a] = []).Add(v);
		}
		Ok($"os {VerbosDeProducao.Length} verbos de producao tem arte declarada", semArte == 0);

		// ============================ A MEDIDA QUE RESPONDE O PEDIDO DO DONO ============================
		// "MAIS VARIEDADE" e um numero: quantas folhas DISTINTAS os verbos usam. Antes desta rodada
		// era UMA (nenhuma: o desenho era por primitiva, e eram duas formas pra tudo).
		//
		// O piso de 15 nao e chute -- e o que a leitura do DM produziu, com margem pra ninguem ter
		// que mexer aqui ao portar uma tecnica que compartilha folha (Paralysis e Stunlock dividem
		// a `KiHead`, e no DM elas dividem mesmo).
		// ==========================================================================================
		Ok($"os verbos usam {vistas.Count} folhas DISTINTAS (o pedido era variedade)", vistas.Count >= 15);
		foreach ((ArteDeKi a, List<string> quem) in vistas.OrderBy(p => (int)p.Key))
			Nota($"{ArteDeKiNoCliente.Rotulo(a),-34} <- {string.Join(", ", quem)}");

		// ---- as duas que o DM manda dividir, e nenhuma outra
		Ok("Paralysis e Stunlock dividem a MESMA folha (as duas leem `ParalysisIcon`)",
		   ArteDeProjetil.De("Paralysis", "Human", "", 1) == ArteDeProjetil.De("Stunlock", "Human", "", 1));

		// ---- a armadilha do nome: o verb `Makkankosappo` NAO usa a folha `Makkankosappo.dmi`
		Ok("o verb Makkankosappo usa BeamStaticBeam (`beams.dm:357`), e nao a folha de mesmo nome",
		   ArteDeProjetil.De("Makkankosappo", "Human", "", 1) == ArteDeKi.BeamStaticBeam);

		// ---- o Charged_Shot ignora a raca (`blasts.dm:103` escreve '20.dmi' na unha)
		Ok("o Tiro Carregado e igual pra toda raca (o DM escreve '20.dmi' literal)",
		   ArteDeProjetil.De("Charged_Shot", "Android", "", 1)
		   == ArteDeProjetil.De("Charged_Shot", "Tsujin", "", 1));

		// =====================================================================
		// A CAMADA RACIAL
		// =====================================================================
		_linhas.Add("=== FAMILIA 1b: a bola MUDA com a raca (`race.dm:62` + os quatro `stat*.dm`) ===");
		(string Raca, string Classe, ArteDeKi Esperada)[] racas =
		[
			("Human", "", ArteDeKi.Blast1),      // `race.dm:62`
			("Saiyan", "", ArteDeKi.Blast1),     // nao sobrescreve: fica no padrao
			("Android", "", ArteDeKi.Blast10),   // `statandroid.dm:9`
			("Tsujin", "", ArteDeKi.Blast5),     // `stattsujin.dm:6`
			("Yardrat", "", ArteDeKi.Blast7),    // `statyardrat.dm:7`
			("Demigod", "", ArteDeKi.Blast1),    // `statdemi.dm:13`
			("Demigod", "Ogre", ArteDeKi.Blast31),  // `statdemi.dm:29`
			("Demigod", "Genie", ArteDeKi.Blast19), // `statdemi.dm:45`
		];
		int erradas = 0;
		foreach ((string raca, string classe, ArteDeKi esp) in racas)
		{
			ArteDeKi teve = ArteDeProjetil.De("Basic_Blast", raca, classe, 1);
			if (teve != esp) { Nota($"{raca}/{classe}: esperava {esp}, veio {teve}"); erradas++; }
		}
		Ok("as oito linhas raciais de bola batem com o DM", erradas == 0);

		// A PROVA DE QUE A CAMADA VALE PRA VALER: quatro racas, quatro folhas diferentes na MESMA
		// tecnica. Uma tabela racial escrita e nunca consultada passaria nas oito linhas acima (se
		// todas devolvessem o padrao a comparacao acusaria) -- esta linha e a que garante que o
		// caminho de producao (`Basic_Blast`) chega mesmo ate ela.
		Ok("quatro racas dao quatro bolas diferentes no MESMO verb",
		   new[] { "Human", "Android", "Tsujin", "Yardrat" }
			   .Select(r => ArteDeProjetil.De("Basic_Blast", r, "", 1)).Distinct().Count() == 4);

		// ============================ AS TRES CAMADAS QUE NENHUM VERB PORTADO ALCANCA ============================
		// `WaveIcon`, `CBLASTICON` e `Makkankoicon` estao lidas do DM e nao tem valor de `Fonte` --
		// ver o bloco daquele enum. Elas sao MEDIDAS aqui mesmo assim, e por uma razao pratica: uma
		// leitura de original que ninguem exercita envelhece igual a codigo morto, e a parte cara
		// desta rodada foi justamente ler `race.dm` e os quatro `stat*.dm`. No dia em que o
		// Energy_Shot ou o Special Beam Cannon forem portados, estas tres linhas ja garantiram que a
		// resposta que eles vao consumir e a do jogo original.
		// ====================================================================================================
		Ok("o Makkanko racial tem as tres respostas do DM (padrao, Tsujin, Yardrat)",
		   ArteDeProjetil.Makkanko("Human") == ArteDeKi.Makkankosappo4
		   && ArteDeProjetil.Makkanko("Tsujin") == ArteDeKi.Makkankosappo3
		   && ArteDeProjetil.Makkanko("Yardrat") == ArteDeKi.Makkankosappo);

		// `WaveIcon` (`race.dm:57`): so o Demigod da linhagem Demigod troca (`statdemi.dm:9`).
		Ok("o `WaveIcon` racial: Beam3 pra todo mundo, Beam2 so pro Demigod-Demigod",
		   ArteDeProjetil.Onda("Human", "") == ArteDeKi.Beam3
		   && ArteDeProjetil.Onda("Demigod", "") == ArteDeKi.Beam2
		   && ArteDeProjetil.Onda("Demigod", "Ogre") == ArteDeKi.Beam3);

		// `CBLASTICON` (`race.dm:64`) -- e ele NAO e o do Tiro Carregado, que e literal.
		Ok("o `CBLASTICON` racial tem as cinco respostas do DM",
		   ArteDeProjetil.BolaCarregada("Human", "") == ArteDeKi.Blast18
		   && ArteDeProjetil.BolaCarregada("Android", "") == ArteDeKi.Blast11
		   && ArteDeProjetil.BolaCarregada("Tsujin", "") == ArteDeKi.Blast6
		   && ArteDeProjetil.BolaCarregada("Yardrat", "") == ArteDeKi.Blast8
		   && ArteDeProjetil.BolaCarregada("Demigod", "Ogre") == ArteDeKi.Blast35);

		// =====================================================================
		// O SORTEIO
		// =====================================================================
		_linhas.Add("=== FAMILIA 1c: o Kamehameha e SORTEADO por personagem (`Kamehameha.dm:14`) ===");

		var saiu = new HashSet<ArteDeKi>();
		for (int i = 0; i < 400; i++)
			saiu.Add(ArteDeProjetil.De("Kamehameha", "Human", "",
									   Jandirus.Core.Forms.LimiaresPessoais.SementeDe($"lutador{i}", 1700 + i)));

		Ok($"os SEIS Kamehamehas do `rand(1,6)` sao alcancaveis ({saiu.Count} de "
		   + $"{ArteDeProjetil.QuantosKamehamehas} em 400 personagens)",
		   saiu.Count == ArteDeProjetil.QuantosKamehamehas);

		// ============================ ESTAVEL E O OUTRO METADE DO PEDIDO ============================
		// No DM o sorteio e gravado numa `mob/var` salva: ele nao muda nunca mais. Aqui ele e uma
		// funcao PURA da identidade, e a prova de que isso equivale e esta: mil chamadas, mesma
		// resposta. Uma implementacao que sorteasse por disparo passaria na linha de cima (as seis
		// aparecem) e reprovaria aqui -- e o jogador veria o proprio Kamehameha trocar de arte a
		// cada tiro.
		// =======================================================================================
		ulong semente = Jandirus.Core.Forms.LimiaresPessoais.SementeDe("Goku", 1700);
		ArteDeKi primeiro = ArteDeProjetil.De("Kamehameha", "Human", "", semente);
		bool estavel = true;
		for (int i = 0; i < 1000; i++)
			if (ArteDeProjetil.De("Kamehameha", "Human", "", semente) != primeiro) estavel = false;
		Ok($"o mesmo personagem tira SEMPRE o mesmo ({primeiro}) -- 1000 chamadas", estavel);

		// E ele NAO pode andar junto da cor da aura: as duas partem da mesma semente de personagem, e
		// sem o sal por campo quem tem aura branca teria sempre Kamehameha1. Ver o `Hash64` no meio
		// do `SorteioDoKamehameha`.
		int mudou = 0;
		for (int i = 0; i < 200; i++)
		{
			ulong s = Jandirus.Core.Forms.LimiaresPessoais.SementeDe($"p{i}", 1700);
			if (ArteDeProjetil.De("Kamehameha", "Human", "", s) != primeiro) mudou++;
		}
		Ok($"personagens diferentes tiram Kamehamehas diferentes ({mudou} de 200 fugiram do {primeiro})",
		   mudou > 100);
	}

	// =====================================================================
	// FAMILIA 2 -- AS FOLHAS EXISTEM DE VERDADE
	// =====================================================================
	/// <summary>
	/// ============================ A FAMILIA QUE PEGA OS "ATLAS MORTOS" ============================
	/// Este repo ja escreveu 35 atlas e nunca os importou -- 178 animacoes que existiam como arquivo
	/// e nao existiam pro jogo. Uma tabela de arte perfeita apontando pra `.tres` que nao carregam da
	/// exatamente o mesmo resultado na tela que nao ter tabela nenhuma: tiro desenhado por primitiva.
	///
	/// Entao aqui cada folha do enum e CARREGADA e interrogada sobre os estados que o desenho pede --
	/// `head`/`tail`/`origin` pra raio, `default` pra bola --, com a direcao resolvida como o
	/// `ProjetilDesenhado` resolve.
	/// ========================================================================================
	/// </summary>
	private void Familia2()
	{
		_linhas.Add("=== FAMILIA 2: as folhas carregam e tem os estados que o desenho pede ===");

		int total = 0, faltando = 0, semEstado = 0;
		foreach (ArteDeKi a in ArteDeProjetil.Todas)
		{
			total++;
			(string pasta, _) = ArteDeProjetil.Folha(a);
			if (pasta.Length == 0) { Nota($"{a}: SEM caminho declarado no `Folha()`"); faltando++; continue; }

			SpriteFrames? f = ArteDeKiNoCliente.Folha(a);
			if (f == null) { Nota($"{a}: {ArteDeKiNoCliente.Caminho(a)} NAO CARREGOU"); faltando++; continue; }

			// ============================ A PERGUNTA E FEITA PELO PROPRIO DESENHO ============================
			// `ProjetilDesenhado.QuadroDe` E `SufixoDeDirecao` sao os metodos que o `_Draw` chama --
			// nao uma copia parecida deles. Uma bancada com a propria busca ja reprovou uma folha
			// PERFEITA aqui (a `KiHead.dmi`, cujos estados se chamam `paralysis` e `1`..`9`): a copia
			// procurava `default`, o jogo procurava outra coisa, e nenhum dos dois estava medindo o
			// outro. Ver o cabecalho de `QuadroDe`.
			//
			// AS QUATRO DIRECOES sao varridas de proposito: uma folha pode ter `head_south` e nao ter
			// `head_north`, e o tiro sumiria so quando alguem atirasse pra cima.
			bool serve = true;
			foreach (Vec2 rumo in QuatroRumos)
			{
				string dir = ProjetilDesenhado.SufixoDeDirecao(f, rumo);
				bool comoRaio = ProjetilDesenhado.QuadroDe(f, "head", dir, 0) != null
								&& ProjetilDesenhado.QuadroDe(f, "tail", dir, 0) != null;
				bool comoBola = ProjetilDesenhado.QuadroDe(
					f, ArteDeProjetil.EstadoDeBola(a), dir, 0, qualquerEstado: true) != null;
				if (!comoRaio && !comoBola) serve = false;
			}
			if (!serve)
			{
				Nota($"{a}: carregou mas o desenho nao acha quadro nas 4 direcoes "
					 + $"(estados: {string.Join(",", f.GetAnimationNames())})");
				semEstado++;
			}
		}
		Ok($"as {total} folhas do catalogo carregam do disco", faltando == 0);
		Ok("todas tem os estados que o desenho pede", semEstado == 0);

		// ============================ E A PECA DA MAO, QUE O TREM PASSOU A EXIGIR ============================
		// O desenho fecha o raio com `origin` (o segmento colado na mao, `objects.dm:126`), caindo em
		// `end` (`:73`) e, em ultimo caso, no proprio `tail`. Enquanto o corpo parava um pedaco antes da
		// mao, uma folha sem essas duas so perdia o fecho; agora ela perde o PEDACO QUE FECHA O VAO, e
		// o vao e de ate um passo. Entao a pergunta passa a valer a pena ser feita em voz alta.
		// ================================================================================================
		// A PERGUNTA E FEITA A QUEM DESENHA COMO TREM. Uma folha sem `head`/`tail` nenhum nao monta trem
		// nenhum: ela cai na primitiva inteira (ver `DesenharComFolha`), que e a rede declarada e nao
		// um raio picotado. Quem tem as duas e nao tem o fecho e que ficaria com um vao de ate um passo.
		var semMao = new List<string>();
		var semTrem = new List<string>();
		foreach (ArteDeKi a in ArteDeProjetil.PermitidasPara(TipoDeProjetil.Beam))
		{
			SpriteFrames? f = ArteDeKiNoCliente.Folha(a);
			if (f == null) continue;
			foreach (Vec2 rumo in QuatroRumos)
			{
				string dir = ProjetilDesenhado.SufixoDeDirecao(f, rumo);
				bool trem = ProjetilDesenhado.NomeDaAnimacao(f, "head", dir) != null
							&& ProjetilDesenhado.NomeDaAnimacao(f, "tail", dir) != null;
				bool mao = ProjetilDesenhado.NomeDaAnimacao(f, "origin", dir) != null
						   || ProjetilDesenhado.NomeDaAnimacao(f, "end", dir) != null;

				if (!trem) semTrem.Add($"{ArteDeKiNoCliente.Rotulo(a)} ({dir})");
				else if (!mao) semMao.Add($"{ArteDeKiNoCliente.Rotulo(a)} ({dir})");
			}
		}
		foreach (string s in semTrem) Nota($"sem `head`/`tail` -- desenha pela primitiva: {s}");
		foreach (string s in semMao) Nota($"SEM PECA DE MAO (`origin`/`end`): {s}");
		Ok($"toda folha que monta TREM tem a peca que fecha na mao ({semMao.Count} sem)", semMao.Count == 0);

		// ============================ O RECORTE POR TIPO E DO DM, E ELE TEM QUE MORDER ============================
		// `custom_icon_folders` (`customattacks.dm:558-562`). Se as tres listas fossem iguais, a
		// tecnica customizada deixaria pendurar folha de raio numa bola -- e a bola sairia desenhada
		// com a cauda de um beam. Estas tres linhas sao o unico lugar que reprova isso.
		// ====================================================================================================
		int nRaio = ArteDeProjetil.PermitidasPara(TipoDeProjetil.Beam).Count();
		int nBola = ArteDeProjetil.PermitidasPara(TipoDeProjetil.Blast).Count();
		int nTele = ArteDeProjetil.PermitidasPara(TipoDeProjetil.Guided).Count();
		Ok($"o menu da tecnica inventada e RECORTADO pelo tipo (raio {nRaio}, bola {nBola}, teleguiado {nTele})",
		   nRaio > nBola && nRaio > nTele && nBola > 0 && nTele > 0);
		Ok("nenhuma folha de raio aparece no menu de bola",
		   !ArteDeProjetil.PermitidasPara(TipoDeProjetil.Blast).Contains(ArteDeKi.Beam3));

		// O padrao do custom e o do DM nos dois ramos (`:440` e `:500`), e ele tem que ESTAR no
		// catalogo -- um padrao que nao carrega e um tiro invisivel pra toda tecnica inventada.
		Ok("o padrao da tecnica inventada carrega nos dois tipos",
		   ArteDeKiNoCliente.Folha(ArteDeProjetil.PadraoDoCustom(TipoDeProjetil.Beam)) != null
		   && ArteDeKiNoCliente.Folha(ArteDeProjetil.PadraoDoCustom(TipoDeProjetil.Blast)) != null);
	}

	/// <summary>Os quatro rumos cardeais, em vetor -- `Y` cresce pra o sul, como no resto do projeto.</summary>
	private static readonly Vec2[] QuatroRumos =
	[
		new(0, 1), new(0, -1), new(1, 0), new(-1, 0),
	];

	// =====================================================================
	// FAMILIA 3 -- O PIXEL
	// =====================================================================
	/// <summary>
	/// Monta DOIS tiros lado a lado. O da esquerda e o Kamehameha1; o da direita e o Masenko. Sao os
	/// dois raios mais usados do jogo, e no DM eles sao folhas diferentes -- se o pixel deles for
	/// igual, a variedade nao chegou na tela por mais verde que a familia 1 esteja.
	/// </summary>
	private void MontarOsDoisTiros()
	{
		_a = Tiro(ArteDeKi.Kamehameha1, new Color(1f, 0.2f, 0.2f), XA);
		_b = Tiro(ArteDeKi.BeamMasenko, new Color(1f, 0.2f, 0.2f), XB);
	}

	private ProjetilDesenhado Tiro(ArteDeKi arte, Color cor, int x)
	{
		var no = new ProjetilDesenhado
		{
			Name = $"Tiro{arte}", Tipo = TipoDeProjetil.Beam, Cor = cor,
			Position = new Vector2(x, YTiro),
		};
		no.Vestir(arte, 1f);
		AddChild(no);
		// CABECA EM CIMA, CAUDA EMBAIXO: um rastro vertical de 6 tiles, que e o que faz o corpo
		// (`tail`) aparecer. Um raio de comprimento zero desenharia so a cabeca e as duas fotos
		// concordariam por nao terem nada pra discordar.
		no.Mirar(new Vector2(x, YTiro), new Vector2(x, YTiro + 32 * 6));
		return no;
	}

	public override void _Process(double delta)
	{
		switch (_t++)
		{
			case < 4: break;   // dois quadros pro `_Process` do node parar de interpolar

			case 4:
				_linhas.Add("=== FAMILIA 3: o PIXEL desenhado, e nao a intencao ===");
				Fotografar();
				break;

			case 5:
				// A MESMA ARTE, OUTRA COR DE KI. Se a tinta nao chegasse ao pixel, esta foto seria
				// identica a de cima -- e o jogo inteiro teria Kamehameha da mesma cor.
				_a!.Cor = new Color(0.2f, 0.4f, 1f);
				_a.Vestir(ArteDeKi.Kamehameha1, 1f);
				_a.QueueRedraw();
				break;

			case 6: FotografarTinta(); break;

			case 7:
				// A ESCALA. `A.transform *= wavemult` (`beams.dm:149`): o Final Flash e um MURO.
				_b!.Vestir(ArteDeKi.BeamMasenko, 3f);
				_b.QueueRedraw();
				break;

			case 8: FotografarEscala(); break;

			case 9:
				// ============================ A COR DE PERSONAGENS DE VERDADE ============================
				// Ate aqui a tinta foi uma cor de teste escolhida a mao. Esta ultima fase usa a cor que
				// o JOGO daria a dois personagens (`CorDoTiro.De`, o `blastR/G/B = rand(0,255)` do
				// `CharacterCreation.dm:28`) sobre a folha de raio PADRAO do jogo (`Beam3.dmi`, tom
				// dominante 192).
				//
				// E o desenho aqui tem historia: este metodo lia a cor da CHAMA (`Appearance.CorAura`),
				// que neste port carrega um `200 +` exigido pelo shader da aura. Somada numa folha de
				// 192, ela estoura os tres canais em 255 -- **o jogo inteiro atirava branco**, e as
				// tres fases acima ficavam TODAS VERDES assim mesmo, porque comparavam fotos entre si
				// e duas fotos brancas diferentes ainda sao diferentes.
				//
				// Esta fase e a unica que reprova aquilo, e por isso ela existe.
				// ====================================================================================
				_a!.Cor = CorDeTeste("Goku");
				_a.Vestir(ArteDeKi.Beam3, 1f);
				_a.QueueRedraw();
				_b!.Cor = CorDeTeste("Vegeta");
				_b.Vestir(ArteDeKi.Beam3, 1f);
				_b.QueueRedraw();
				break;

			case 10: FotografarCoresDeVerdade(); break;

			case 11:
				// ============================ AS BOLAS, E UMA DELAS E A ARMADILHA ============================
				// Ate aqui so raios foram desenhados. A esquerda vira a bola RACIAL padrao (`1.dmi`,
				// cujo estado o conversor batizou de `default`); a direita vira a `KiHead.dmi` da
				// Paralysis -- a unica folha do catalogo que NAO tem `default`: os estados dela se
				// chamam `paralysis`, `piercer` e `1`..`9` (`Ki2.0/Debuffs.dm:26` pede `"Paralysis"`).
				//
				// **Ela ja falhou uma vez nesta bancada** -- a Paralysis saia desenhada por primitiva,
				// e nada no codigo parecia errado. E o unico caso em que o `icon_state` do DM importa
				// (ver `ArteDeProjetil.EstadoDeBola`), e por isso ele e fotografado.
				// =======================================================================================
				VirarBola(_a!, ArteDeKi.Blast1, CorDeTeste("Goku"));
				VirarBola(_b!, ArteDeKi.KiHead, CorDeTeste("Vegeta"));
				break;

			case 12: FotografarBolas(); break;

			// ============================ FAMILIA 4 -- A CONTINUIDADE DO CORPO ============================
			// O dono relatou *"o beam ta PICOTADO quando ele e lancado"*, e a familia 3 acima nunca
			// poderia pegar isso: o unico comprimento que ela desenha e `32 * 6` (ver `Tiro`), um
			// MULTIPLO EXATO do ladrilho -- o caso em que o ultimo `tail` encosta no `origin` por acaso.
			//
			// Aqui o mesmo raio e desenhado em comprimentos que NAO sao multiplos, que e o que o jogo
			// produz o tempo todo (a cauda e a mao do dono e a cabeca anda em float, `Projetil.Cauda`).
			// Estas quatro fotos ja mediram 13 px e 25 px de fresta, e a lei era `comprimento mod passo`.
			// ==========================================================================================
			case 13:
				PorAOsDoisComoRaio(192, 205);   // esquerda: multiplo (controle) / direita: 192 + 13
				break;

			case 14: MedirAFrestaDosDois("6-fresta-192-e-205"); break;

			case 15:
				PorAOsDoisComoRaio(224, 249);   // outro par, pra a fresta nao ser um acaso do primeiro
				break;

			case 16: MedirAFrestaDosDois("7-fresta-224-e-249"); break;

			// ============================ FAMILIA 4b -- A DIAGONAL ============================
			// A diagonal e um caso PROPRIO e nao um comprimento a mais: no BYOND os centros de tiles
			// vizinhos na diagonal estao a 45,25 px (o √2 que o `dir_matrix` embute, `objects.dm:52`),
			// mas a arte diagonal da `Beam3` -- a folha PADRAO do `beams.dm:4` -- so pinta 39,6 px no
			// eixo. Um passo tirado do TILE abriria ~6 px de fresta em CADA junta aqui, e nenhuma foto
			// vertical do mundo mostraria isso.
			// =============================================================================
			case 17:
				PorOsDoisNaDiagonal(190, 253);
				break;

			case 18: MedirAFrestaDosDois("8-diagonal-190-e-253"); break;

			// ============================ FAMILIA 4c -- A ESCALA E A CELULA GRANDE ============================
			// Esquerda: o `wavemult` do Final Flash (`GameServer.Tecnicas.G5.cs:317` manda 4). No DM ele
			// so engorda o sprite (`A.transform *= wavemult`) e o trem continua andando de tile em tile --
			// e por isso que aquilo e um MURO. Um passo que multiplicasse pela escala andaria 128 px por
			// pedaco e abriria ate 127 px de buraco.
			//
			// Direita: a `Beam - Big Fire`, cuja celula e 64x64 e nao 32x32. Um "32" cravado no codigo
			// nao sabe disso; a medida da folha sabe.
			// ============================================================================================
			case 19:
				PorAEscalaEACelulaGrande(205, 205);
				break;

			case 20: MedirAFrestaDosDois("9-escala-4x-e-celula-64"); break;

			// ============================ FAMILIA 4d -- TODOS OS COMPRIMENTOS, E O CUSTO ============================
			case 21: VarrerTodosOsComprimentos(); MedirOCusto(); break;

			// ============================ FAMILIA 5 -- A FOTO DO DONO ============================
			case 22:
				_linhas.Add("=== FAMILIA 5: a foto do dono, reproduzida (o feixe que atravessa a tela) ===");
				PorAFotoDoDono();
				break;

			case 23: MedirAFrestaDosDois("10-a-foto-do-dono"); break;

			// ============================ FAMILIA 5b -- OS OITO RUMOS, LONGO E CURTO ============================
			case 24:
				_linhas.Add("=== FAMILIA 5b: os OITO rumos, o mesmo raio LONGO e CURTO ===");
				PorOsOitoRumos(CompDaGrade());
				break;

			case 25: MedirOsOito("11-oito-rumos-longo", $"LONGO ({CompDaGrade():0} px)"); break;

			// O CURTO E O CASO PROPRIO: com 44 px cabe UM corpo e sobram 13 -- e aqueles 13 px eram a
			// fresta inteira. Um passo errado aqui aparece como o raio inteiro em dois pedacos.
			case 26: PorOsOitoRumos(44f); break;

			case 27: MedirOsOito("12-oito-rumos-curto", "CURTO (44 px, cabe 1 corpo)"); break;

			// MAIS CURTO QUE UM PASSO: o instante do LANCAMENTO, o que o dono fotografou. Aqui o laco
			// antigo nao rodava NENHUMA vez e sobravam cabeca e mao soltas.
			case 28: PorOsOitoRumos(24f); break;

			case 29: MedirOsOito("12b-oito-rumos-lancamento", "NO LANCAMENTO (24 px, nem um passo)"); break;

			// ============================ FAMILIA 5c -- O FIO E O MURO ============================
			case 30:
				_linhas.Add("=== FAMILIA 5c: o FIO e o MURO -- a escala mudou junto? ===");
				PorOFioEOMuro();
				break;

			case 31: MedirOFioEOMuro(); break;

			// ============================ FAMILIA 5d -- O DEFEITO INJETADO ============================
			case 32:
				_linhas.Add("=== FAMILIA 5d: o defeito INJETADO -- a sonda sabe ficar vermelha? ===");
				EscolherOsComprimentosDaInjecao();
				ProjetilDesenhado.DefeitoDeTeste = ProjetilDesenhado.DefeitoDoTrem.UmPedacoAMenos;
				PorAOsDoisComoRaio(_compRuim, _compBom);
				break;

			case 33:
				MedirEsperandoFresta("14-defeito-um-pedaco-a-menos", "UmPedacoAMenos",
									 esperaNoRestoZero: false);
				break;

			case 34:
				// O CORPO DESLIGADO INTEIRO: aqui nem o multiplo exato salva, e por isso os DOIS
				// comprimentos tem que ficar vermelhos.
				ProjetilDesenhado.DefeitoDeTeste = ProjetilDesenhado.DefeitoDoTrem.SemLadrilho;
				PorAOsDoisComoRaio(_compRuim, _compBom);
				break;

			case 35:
				MedirEsperandoFresta("15-defeito-sem-ladrilho", "SemLadrilho", esperaNoRestoZero: true);
				break;

			case 36:
				// E DE VOLTA AO NORMAL, com o MESMO par: sem esta fase a familia acima provaria so que
				// a sonda fica vermelha, e nao que ela distingue.
				ProjetilDesenhado.DefeitoDeTeste = ProjetilDesenhado.DefeitoDoTrem.Nenhum;
				PorAOsDoisComoRaio(_compRuim, _compBom);
				break;

			case 37:
				_linhas.Add("=== FAMILIA 5d (fim): com o defeito DESLIGADO, o mesmo par volta ao verde ===");
				MedirAFrestaDosDois("16-defeito-desligado");
				break;

			// ============================ FAMILIA 5e -- A FASE DA DIAGONAL LONGA ============================
			// Um quadro desenha, o seguinte mede -- ver `PorADiagonalLonga` sobre por que precisam ser
			// doze raios longos de verdade e nao doze curtos lado a lado.
			case >= 38 and <= 38 + (QuantasDiagonais * 2) - 1:
			{
				if (_diagonal == 0)
					_linhas.Add("=== FAMILIA 5e: doze diagonais LONGAS -- a junta so abre em ALGUMAS fases ===");

				if ((_diagonal & 1) == 0) PorADiagonalLonga(_diagonal / 2);
				else MedirADiagonalLonga(_diagonal / 2);
				_diagonal++;
				break;
			}

			default:
				GD.Print("\n[artedeki] ===== BANCADA DA ARTE DOS ATAQUES DE KI =====");
				foreach (string l in _linhas) GD.Print("[artedeki] " + l);
				GD.Print(_falhas == 0
					? "[artedeki] ===== TUDO VERDE ====="
					: $"[artedeki] ===== {_falhas} FALHA(S) =====");
				GetTree().Quit(_falhas == 0 ? 0 : 1);
				break;
		}
	}

	/// <summary>A assinatura de pixel de uma metade da tela: quantos pixels desenhados e que tons.</summary>
	private (int Pixels, int Tons, long Hash) Assinatura(Image img, int x0, int x1)
	{
		var tons = new HashSet<int>();
		int n = 0;
		long hash = 17;
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = x0; x < x1; x++)
			{
				Color c = img.GetPixel(x, y);
				// "e o fundo?" pelo VERDE cheio com os outros dois zerados -- e a unica combinacao
				// que nenhuma folha de ki tem depois de tingida.
				if (c.G > 0.9f && c.R < 0.1f && c.B < 0.1f) continue;
				int r = (int)(c.R * 255 + 0.5f), g = (int)(c.G * 255 + 0.5f), b = (int)(c.B * 255 + 0.5f);
				tons.Add((r << 16) | (g << 8) | b);
				// A POSICAO ENTRA NO HASH de proposito: duas folhas podem ter a mesma paleta e
				// desenhos completamente diferentes. Sem o `x,y` a assinatura mediria so a paleta.
				hash = hash * 31 + ((long)x * 7919 + y) * (r + g * 3 + b * 7);
				n++;
			}
		return (n, tons.Count, hash);
	}

	/// <summary>
	/// Fotografa e GRAVA a imagem em `user://artedeki-*.png`.
	///
	/// A gravacao nao e enfeite: os numeros abaixo dizem que os dois desenhos sao DIFERENTES, e nao
	/// que os dois estao CERTOS. Duas artes trocadas entre si, um raio desenhado ao contrario ou uma
	/// cauda colada por cima da cabeca passam por qualquer contagem de pixel -- e este projeto ja
	/// tem duas correcoes visuais que so a FOTO mostrou. O olho e a ultima familia, e ela nao cabe
	/// num `if`.
	/// </summary>
	private Image? Foto(string nome = "")
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty())
		{
			Nota("SEM FOTO (headless nao renderiza) -- as familias 1 e 2 valem, esta nao roda");
			return null;
		}
		if (nome.Length > 0)
		{
			string caminho = $"user://artedeki-{nome}.png";
			img.SavePng(caminho);
			Nota($"foto: {ProjectSettings.GlobalizePath(caminho)}");
		}
		return img;
	}

	private (int Pixels, int Tons, long Hash) _assA, _assB;

	private void Fotografar()
	{
		Image? img = Foto("1-duas-artes");
		if (img == null) { _t = 99; return; }

		int meio = img.GetWidth() / 2;
		_assA = Assinatura(img, 0, meio);
		_assB = Assinatura(img, meio, img.GetWidth());

		Nota($"Kamehameha1: {_assA.Pixels}px, {_assA.Tons} tons");
		Nota($"Masenko    : {_assB.Pixels}px, {_assB.Tons} tons");

		Ok("o Kamehameha DESENHOU alguma coisa (a folha chegou na tela)", _assA.Pixels > 100);
		Ok("o Masenko DESENHOU alguma coisa", _assB.Pixels > 100);

		// ============================ A LINHA QUE RESPONDE O DONO ============================
		// Duas tecnicas, duas folhas do BYOND, dois desenhos DIFERENTES. Era exatamente isto que
		// nao acontecia antes: as vinte e quatro tecnicas caiam em duas primitivas, e um Kamehameha
		// e um Masenko eram o mesmo risco com a mesma grossura.
		// ================================================================================
		Ok("Kamehameha e Masenko desenham PIXEIS DIFERENTES (a variedade chegou na tela)",
		   _assA.Hash != _assB.Hash && _assA.Pixels > 100 && _assB.Pixels > 100);
	}

	private void FotografarTinta()
	{
		Image? img = Foto("2-tinta-azul");
		if (img == null) { _t = 99; return; }

		(int Pixels, int Tons, long Hash) azul = Assinatura(img, 0, img.GetWidth() / 2);
		Nota($"Kamehameha1 tingido de AZUL: {azul.Pixels}px, {azul.Tons} tons");

		// O `icon += rgb(...)` do DM chegando ao pixel. Sem isto o ki de todo mundo teria a cor
		// crua da folha -- e a folha e CINZA.
		Ok("trocar a cor do ki muda o PIXEL (o `icon += rgb()` do BYOND funciona)",
		   azul.Hash != _assA.Hash && azul.Pixels > 100);

		// E o DESENHO tem que sobreviver a tinta: somar clareia, mas nao pode achatar tudo num tom
		// so. Um `Modulate` no lugar da soma daria justamente isso em folha escura.
		Ok($"o desenho sobreviveu a tinta ({azul.Tons} tons, mais de um)", azul.Tons > 1);
	}

	/// <summary>A cor que o jogo daria ao tiro deste personagem. Ver `Appearance.CorDoTiro`.</summary>
	private static Color CorDeTeste(string nome)
	{
		Jandirus.Core.Appearance.Rgb c = Jandirus.Core.Appearance.CorDoTiro.De(nome, 1700);
		return new Color(c.R / 255f, c.G / 255f, c.B / 255f);
	}

	private void FotografarCoresDeVerdade()
	{
		Image? img = Foto("4-cores-de-personagem");
		if (img == null) { _t = 99; return; }

		int meio = img.GetWidth() / 2;
		(int px, int brancos) goku = ContarBrancos(img, 0, meio);
		(int px, int brancos) vegeta = ContarBrancos(img, meio, img.GetWidth());

		Nota($"Goku   ({CorDeTeste("Goku").ToHtml(false)}): {goku.px}px, {goku.brancos} brancos puros");
		Nota($"Vegeta ({CorDeTeste("Vegeta").ToHtml(false)}): {vegeta.px}px, {vegeta.brancos} brancos puros");

		// ============================ A LINHA QUE PEGA A SATURACAO ============================
		// Com a cor da CHAMA (media 247) sobre a `Beam3` (192), 100% dos pixels saiam `#ffffff`.
		// Com o sorteio cru do DM, a maior parte do desenho guarda a cor. O limiar e generoso (mais
		// da metade tem que sobreviver) justamente pra nao reprovar um personagem de sorteio alto --
		// o BYOND tambem satura as vezes, e isso e fidelidade e nao defeito.
		// =================================================================================
		Ok("a cor do tiro NAO satura tudo em branco (Goku)", goku.px > 100 && goku.brancos < goku.px / 2);
		Ok("a cor do tiro NAO satura tudo em branco (Vegeta)", vegeta.px > 100 && vegeta.brancos < vegeta.px / 2);

		// DOIS PERSONAGENS, A MESMA FOLHA, DESENHOS DIFERENTES. E o `rand(0,255)` do DM chegando na
		// tela: o Ki Wave de um nao se parece com o do outro, e nenhuma arte nova foi precisa pra
		// isso -- e a mesma `Beam3.dmi` nos dois lados.
		Ok("dois personagens atiram a MESMA folha em cores diferentes",
		   Assinatura(img, 0, meio).Hash != Assinatura(img, meio, img.GetWidth()).Hash);
	}

	// =====================================================================
	// FAMILIA 4 -- A CONTINUIDADE DO CORPO (so mede)
	// =====================================================================
	/// <summary>Onde esta a MAO de cada um dos dois tiros, e como cada um se chama na foto.</summary>
	private Vector2 _maoA, _maoB;
	private string _rotuloA = "", _rotuloB = "";

	/// <summary>
	/// UM RAIO NOVO, e nao o de antes remirado -- e isso importa.
	///
	/// `Mirar` so CRAVA a posicao no primeiro pacote; do segundo em diante ela vira alvo de
	/// interpolacao (`Suavizacao`, 22 por segundo). Remirar um node vivo e fotografar no quadro
	/// seguinte mede um raio de comprimento DESCONHECIDO -- que so nao estragou a medida da rodada
	/// passada porque a foto e tao lenta que o `delta` fecha o `Lerp` de uma vez. Nascer de novo e a
	/// mesma coisa que o jogo faz (o tiro nasce, ai interpola) e da o comprimento pedido, exato.
	/// </summary>
	private ProjetilDesenhado NovoRaio(ArteDeKi arte, float escala, Vector2 cabeca, Vector2 mao)
	{
		var no = new ProjetilDesenhado
		{
			Name = $"Raio{_t}_{Mathf.RoundToInt(cabeca.X)}",
			Tipo = TipoDeProjetil.Beam,
			Cor = new Color(1f, 0.2f, 0.2f),
			Position = cabeca,
		};
		no.Vestir(arte, escala);
		no.Mirar(cabeca, mao);
		AddChild(no);
		return no;
	}

	/// <summary>Troca os dois tiros por dois raios novos, e guarda onde cada mao ficou.</summary>
	private void PorOsDoisRaios(ArteDeKi arteA, float escA, Vector2 maoA, string rotA,
								ArteDeKi arteB, float escB, Vector2 maoB, string rotB) =>
		PorOsDoisLivres(arteA, escA, new Vector2(XA, YTiro), maoA, rotA,
						arteB, escB, new Vector2(XB, YTiro), maoB, rotB);

	/// <summary>
	/// A MESMA COISA, com as duas CABECAS livres -- as familias 5 em diante precisam do palco inteiro
	/// (um feixe de trinta tiles nao cabe pendurado num Y fixo) e o berco delas e calculado a partir do
	/// tamanho da janela, nao cravado. Ver <see cref="Tela"/>.
	/// </summary>
	private void PorOsDoisLivres(ArteDeKi arteA, float escA, Vector2 cabA, Vector2 maoA, string rotA,
								 ArteDeKi arteB, float escB, Vector2 cabB, Vector2 maoB, string rotB)
	{
		LimparOPalco();
		_a = NovoRaio(arteA, escA, cabA, maoA);
		_b = NovoRaio(arteB, escB, cabB, maoB);
		_maoA = maoA; _maoB = maoB;
		_rotuloA = rotA; _rotuloB = rotB;
	}

	/// <summary>Tira TODO raio da tela -- os dois nomeados e os oito da grade dos rumos.</summary>
	private void LimparOPalco()
	{
		_a?.QueueFree();
		_b?.QueueFree();
		_a = _b = null;
		foreach ((ProjetilDesenhado no, Vector2 _, string _) in _oito) no.QueueFree();
		_oito.Clear();
	}

	/// <summary>
	/// O TAMANHO DA JANELA, e todo palco das familias 5 sai dele.
	///
	/// A `testar-variedade.bat` abre 1280x720 e esta sessao mediu em 1600x900; um berco cravado em
	/// pixel sairia da tela num dos dois, e um raio fora da tela mede fresta ENORME sem ter fresta
	/// nenhuma (a sonda le "fora da imagem" como "sem tinta"). Berco calculado nao tem esse jeito de
	/// mentir.
	/// </summary>
	private Vector2 Tela => GetViewportRect().Size;

	/// <summary>
	/// Os dois como RAIO VERTICAL da mesma folha (`Beam3.dmi`, a `beamicon` padrao do `beams.dm:4`),
	/// na mesma cor e escala, mudando SO o comprimento. Dois desenhos que so diferem no comprimento e
	/// a unica maneira de a fresta ser atribuivel ao comprimento.
	/// </summary>
	private void PorAOsDoisComoRaio(int compA, int compB) =>
		PorOsDoisRaios(ArteDeKi.Beam3, 1f, new Vector2(XA, YTiro + compA), $"vertical de {compA} px",
					   ArteDeKi.Beam3, 1f, new Vector2(XB, YTiro + compB), $"vertical de {compB} px");

	/// <summary>
	/// Os dois na DIAGONAL, um pra cada lado (a esquerda desce pra a direita, a direita desce pra a
	/// esquerda) -- assim os dois cabem na tela e nenhum encosta no outro.
	/// </summary>
	private void PorOsDoisNaDiagonal(int compA, int compB)
	{
		const float Meio = 0.70710678f;
		PorOsDoisRaios(
			ArteDeKi.Beam3, 1f, new Vector2(XA + compA * Meio, YTiro + compA * Meio),
			$"diagonal de {compA} px", ArteDeKi.Beam3, 1f,
			new Vector2(XB - compB * Meio, YTiro + compB * Meio), $"diagonal de {compB} px");
	}

	/// <summary>Esquerda: a escala 4 do Final Flash. Direita: a folha de celula 64 (`Beam - Big Fire`).</summary>
	private void PorAEscalaEACelulaGrande(int compA, int compB) =>
		PorOsDoisRaios(ArteDeKi.Beam3, 4f, new Vector2(XA, YTiro + compA), $"escala 4x, {compA} px",
					   ArteDeKi.BeamBigFire, 1f, new Vector2(XB, YTiro + compB),
					   $"celula 64, {compB} px");

	/// <summary>
	/// A MAIOR FRESTA DE CADA LADO, em pixel, medida NA FOTO -- e nao na conta.
	///
	/// Anda no EIXO DO FEIXE (que pode ser diagonal) da cabeca ate a mao e conta a maior corrida de
	/// passos em que a faixa inteira e fundo. Faixa e nao um ponto so: o corpo do `Beam3` tem oito
	/// pixels de largura e uma sonda de um pixel erraria o desenho e chamaria o raio inteiro de fresta.
	/// </summary>
	private void MedirAFrestaDosDois(string nome)
	{
		Image? img = Foto(nome);
		if (img == null) { _t = 99; return; }

		MedirUmRaio(img, _a!, _maoA, _rotuloA);
		MedirUmRaio(img, _b!, _maoB, _rotuloB);
	}

	private void MedirUmRaio(Image img, ProjetilDesenhado no, Vector2 mao, string rotulo)
	{
		Vector2 cabeca = no.Position;
		if ((mao - cabeca).Length() < 1f) { Nota($"{rotulo}: rastro de zero -- nada a medir"); return; }

		Fresta faixa = Varrer(img, cabeca, mao, MeiaFaixa);
		Fresta centro = Varrer(img, cabeca, mao, MeiaLinha);

		Nota($"{rotulo}: maior fresta {faixa.Maior} px, a {faixa.Onde} px da cabeca "
			 + $"-- na LINHA CENTRAL {centro.Maior} px de corrida, {centro.Total} px de fundo no total");
		Ok($"{rotulo}: o corpo do raio nao tem fresta ({faixa.Maior} px medidos na foto)", faixa.Maior == 0);
		Ok($"{rotulo}: a LINHA CENTRAL nao tem um so pixel de fundo entre o punho e a ponta "
		   + $"({centro.Total} px)", centro.Total == 0);
	}

	// =====================================================================
	// A SONDA DE FRESTA
	// =====================================================================
	/// <summary>Meia largura da FAIXA: 20 px pra cada lado do eixo. Ver <see cref="Varrer"/>.</summary>
	private const int MeiaFaixa = 20;

	/// <summary>
	/// Meia largura da LINHA CENTRAL. Nao e zero, e nao pode ser: o eixo de um feixe diagonal cai
	/// entre pixels, e uma sonda de largura zero leria o serrilhado da propria diagonal como buraco.
	/// Dois pixels e menos de um terco do nucleo do `Beam3` (8 px), entao ela continua sendo uma sonda
	/// do MIOLO -- ela nao alcanca a borda de coisa nenhuma.
	/// </summary>
	private const int MeiaLinha = 2;

	/// <summary>Uma fresta medida: a maior corrida, o total de passos vazios e onde a pior comeca.</summary>
	private readonly record struct Fresta(int Maior, int Total, int Onde);

	/// <summary>
	/// ANDA NO EIXO DO FEIXE E CONTA O FUNDO -- da cabeca ate a mao, um passo de 1 px por vez.
	///
	/// ============================ DUAS LARGURAS, PORQUE SAO DUAS PERGUNTAS ============================
	/// Com <see cref="MeiaFaixa"/> a pergunta e *"o raio se interrompe?"*: 20 px pra cada lado cobrem o
	/// corpo do `Beam3` inteiro e a cabeca, entao um passo sem tinta nenhuma na faixa e um pedaco do
	/// trem que faltou -- e literalmente o buraco que o dono fotografou.
	///
	/// Com <see cref="MeiaLinha"/> a pergunta e a que o dono fez com essas palavras: *"varra a linha
	/// central do feixe e conte pixels transparentes entre o punho e a ponta"*. E a mais dura das duas,
	/// e ela pega uma coisa que a faixa nao pega: um trem que fecha pelas BORDAS da arte (duas pontas
	/// tocando de canto) e continua com o miolo furado. A faixa acharia tinta e diria que esta inteiro.
	/// ==============================================================================================
	///
	/// O `Total` existe pelo mesmo motivo que a `Maior`: um raio com dez furos de 1 px nao tem "maior
	/// fresta" nenhuma que impressione, e ainda assim esta picotado dez vezes.
	/// </summary>
	private static Fresta Varrer(Image img, Vector2 cabeca, Vector2 mao, int meia)
	{
		Vector2 d = mao - cabeca;
		float comp = d.Length();
		if (comp < 1f) return new Fresta(0, 0, 0);

		Vector2 u = d / comp;
		var perp = new Vector2(-u.Y, u.X);

		int maior = 0, corrida = 0, ondeMaior = 0, total = 0;
		for (int s = 0; s <= (int)comp; s++)
		{
			bool temTinta = false;
			for (int t = -meia; t <= meia && !temTinta; t++)
			{
				Vector2 p = cabeca + u * s + perp * t;
				int px = Mathf.RoundToInt(p.X), py = Mathf.RoundToInt(p.Y);
				if (px < 0 || px >= img.GetWidth() || py < 0 || py >= img.GetHeight()) continue;
				Color c = img.GetPixel(px, py);
				if (!(c.G > 0.9f && c.R < 0.1f && c.B < 0.1f)) temTinta = true;
			}
			if (temTinta) { corrida = 0; continue; }

			total++;
			if (++corrida > maior) { maior = corrida; ondeMaior = s - corrida + 1; }
		}

		return new Fresta(maior, total, ondeMaior);
	}

	// =====================================================================
	// FAMILIA 4d -- TODOS OS COMPRIMENTOS, E O CUSTO
	// =====================================================================
	/// <summary>
	/// A FOTO MOSTRA SEIS COMPRIMENTOS; ESTA VARRE TODOS -- e as duas medem a mesma coisa por dois
	/// caminhos, que e o unico jeito de uma delas poder estar errada e alguem notar.
	///
	/// A regra que a foto mede em pixel ("nao ha faixa de fundo dentro do rastro") e a mesma que aqui
	/// vira geometria: nenhuma junta do trem pode ser maior que o alcance medido da arte. Se ela vale
	/// pra todo comprimento de 2 a 1600 px, nas 8 direcoes, em tres folhas e duas escalas, entao a
	/// fresta nao depende do comprimento -- que era exatamente a lei do defeito.
	/// </summary>
	private void VarrerTodosOsComprimentos()
	{
		_linhas.Add("=== FAMILIA 4d: a continuidade em TODO comprimento, e o custo ===");

		ArteDeKi[] folhas = [ArteDeKi.Beam3, ArteDeKi.Beam4, ArteDeKi.BeamBigFire];
		float[] escalas = [1f, 4f];

		int casos = 0, frouxas = 0, passoMudou = 0, naoFecha = 0, teto = 0;
		float maiorSobra = 0f, sobra = 0f;
		string ondeMaiorSobra = "";

		foreach (ArteDeKi arte in folhas)
		{
			SpriteFrames? f = ArteDeKiNoCliente.Folha(arte);
			if (f == null) { Nota($"{arte}: nao carregou -- fora da varredura"); continue; }

			foreach (Vec2 rumo in OitoRumos)
			{
				string dir = ProjetilDesenhado.SufixoDeDirecao(f, rumo);
				string? aC = ProjetilDesenhado.NomeDaAnimacao(f, "head", dir);
				string? aT = ProjetilDesenhado.NomeDaAnimacao(f, "tail", dir);
				string? aM = ProjetilDesenhado.NomeDaAnimacao(f, "origin", dir)
							 ?? ProjetilDesenhado.NomeDaAnimacao(f, "end", dir) ?? aT;
				if (aC == null || aT == null) { Nota($"{arte}/{dir}: SEM `head` ou `tail`"); continue; }

				// `paraTras` e o vetor da cabeca pra a mao -- o contrario de pra onde o tiro voa.
				var paraTras = new Vector2(-rumo.X, -rumo.Y);

				foreach (float esc in escalas)
				{
					ProjetilDesenhado.MedidaDoTrem m =
						ProjetilDesenhado.MedirTrem(f, paraTras, esc, aC, aT, aM);

					for (int comp = 2; comp <= 1600; comp++)
					{
						casos++;
						ProjetilDesenhado.TremDoRaio t =
							ProjetilDesenhado.MontarTrem(comp, m.Passo, m.SemCorpo);

						// A conta das juntas do trem: cabeca -> corpos -> mao.
						float ultimoCorpo = t.Corpos * t.Passo;
						float vaoDaMao = comp - ultimoCorpo;
						bool noTeto = t.Corpos >= 96;

						// 1) NENHUMA JUNTA MAIOR QUE O ALCANCE DA ARTE -- e a fresta, em geometria.
						//    Sao tres: a da cabeca (o primeiro passo), as do meio (o passo) e a da mao.
						if (t.Passo > m.Passo + 0.01f || vaoDaMao > m.Passo + 0.01f
							|| (t.Corpos == 0 && comp > m.SemCorpo + 0.01f))
						{
							if (noTeto) teto++;   // o teto mordeu: e a troca declarada
							else frouxas++;
						}

						// 2) O VAO DA MAO E O QUE SOBRA DA GRADE, e ele nunca pode ser negativo (corpo
						//    passando da mao) nem chegar a um passo inteiro (era ai que morava a fresta).
						if (vaoDaMao < -0.01f || (t.Corpos > 0 && vaoDaMao > t.Passo + 0.01f)) naoFecha++;

						// 3) O PASSO NAO DEPENDE DO COMPRIMENTO -- e a propriedade que impede o corpo
						//    do raio de escorregar enquanto ele cresce. So o trem de um corpo so (o raio
						//    curto) usa outro passo, e por isso ele esta de fora.
						if (t.Corpos > 1 && Mathf.Abs(t.Passo - m.Passo) > 0.01f) passoMudou++;

						if (!noTeto && vaoDaMao > maiorSobra)
						{
							maiorSobra = vaoDaMao;
							ondeMaiorSobra = $"{arte}/{dir} escala {esc}, {comp} px";
						}
						if (noTeto && sobra < 1f) sobra = comp;
					}
				}
			}
		}

		Ok($"nenhuma junta passa do alcance da arte em {casos} comprimentos ({frouxas} frouxas)",
		   frouxas == 0 && casos > 10000);
		Ok($"o vao da mao e sempre menor que um passo ({naoFecha} fora da regra)", naoFecha == 0);
		Ok($"o passo NAO depende do comprimento ({passoMudou} casos em que ele mudou)", passoMudou == 0);
		Nota($"o maior vao que sobrou pra a mao foi {maiorSobra:0.0} px ({ondeMaiorSobra})");
		Nota(teto == 0
			 ? "o teto de 96 pedacos nunca mordeu ate 1600 px (50 tiles)"
			 : $"o teto de 96 pedacos morde a partir de {sobra:0} px, em {teto} casos");
	}

	/// <summary>Os oito rumos do BYOND, em vetor -- as diagonais e que fazem o passo mudar.</summary>
	private static readonly Vec2[] OitoRumos =
	[
		new(0, 1), new(0, -1), new(1, 0), new(-1, 0),
		new(0.7071f, 0.7071f), new(-0.7071f, 0.7071f),
		new(0.7071f, -0.7071f), new(-0.7071f, -0.7071f),
	];

	/// <summary>O nome de cada um dos <see cref="OitoRumos"/>, na mesma ordem -- pra a foto ser lida.</summary>
	private static readonly string[] NomesDosOito =
	[
		"sul", "norte", "leste", "oeste", "sudeste", "sudoeste", "nordeste", "noroeste",
	];

	// =====================================================================
	// FAMILIA 5 -- A FOTO DO DONO, REPRODUZIDA
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ELA EXISTE DEPOIS DA 4 ============================
	/// A familia 4 mede a fresta em raios de 190 a 253 px -- de seis a oito tiles. O dono fotografou
	/// **um feixe atravessando o mapa**: corpo no canto de cima e o raio descendo a tela inteira. Sao
	/// trinta tiles, e trinta tiles nao sao "o mesmo teste com outro numero": e onde o teto de pedacos
	/// mora, onde a interpolacao tem mais caminho pra escorregar, e onde um erro de meio pixel por
	/// junta vira meio raio de deriva.
	///
	/// Entao esta familia repete a foto DELE, e nas duas leituras: o feixe vertical descendo e o
	/// DIAGONAL do print (o corpo em cima a direita, o raio descendo pra a esquerda). O comprimento sai
	/// da janela e nao de um numero cravado -- ver <see cref="Tela"/>.
	/// ====================================================================================
	/// </summary>
	private void PorAFotoDoDono()
	{
		Vector2 t = Tela;

		// O VERTICAL: mao la em cima, cabeca embaixo -- o raio DESCE, como no print.
		var maoV = new Vector2(t.X * 0.16f, t.Y * 0.06f);
		var cabV = new Vector2(t.X * 0.16f, t.Y * 0.95f);

		// O DIAGONAL DO PRINT: a mao no canto superior direito, o feixe descendo pra o sudoeste. O
		// comprimento e o maior que cabe sem encostar no vertical nem sair da tela.
		const float Meio = 0.70710678f;
		var maoD = new Vector2(t.X * 0.95f, t.Y * 0.05f);
		float compD = MathF.Floor(MathF.Min((maoD.X - t.X * 0.36f) / Meio, (t.Y * 0.95f - maoD.Y) / Meio));
		Vector2 cabD = maoD + new Vector2(-Meio, Meio) * compD;

		PorOsDoisLivres(ArteDeKi.Beam3, 1f, cabV, maoV,
						$"A FOTO DO DONO -- vertical descendo, {cabV.Y - maoV.Y:0} px "
						+ $"({(cabV.Y - maoV.Y) / 32f:0.0} tiles)",
						ArteDeKi.Beam3, 1f, cabD, maoD,
						$"A FOTO DO DONO -- diagonal do print (sudoeste), {compD:0} px "
						+ $"({compD / 32f:0.0} tiles)");
	}

	// =====================================================================
	// FAMILIA 5b -- OS OITO RUMOS NA MESMA FOTO
	// =====================================================================
	/// <summary>Os oito raios da grade, e onde a mao de cada um ficou.</summary>
	private readonly List<(ProjetilDesenhado No, Vector2 Mao, string Rotulo)> _oito = [];

	/// <summary>
	/// OS OITO RUMOS, UM EM CADA CELULA DE UMA GRADE 4x2.
	///
	/// ============================ POR QUE EM CELULAS SEPARADAS, E NAO EM ESTRELA ============================
	/// O desenho obvio seria uma estrela: oito feixes saindo do mesmo punho. Ele ficaria bonito e
	/// mediria MENOS: a fresta deste defeito nascia colada na MAO, e numa estrela e justamente ali que
	/// os oito se cruzam -- a tinta de um tapa o buraco do vizinho e a sonda acha o raio inteiro. Uma
	/// foto que so pode dar verde nao e uma medida.
	///
	/// Em celulas separadas cada raio esta sozinho com o fundo, e a sonda so pode achar a tinta DELE.
	/// ====================================================================================================
	///
	/// AS DIAGONAIS SAO A METADE QUE IMPORTA: la o passo do tile e 45,25 px e a arte da `Beam3` so pinta
	/// 34, e um passo tirado do tile abriria 11 px em CADA junta. Nenhuma foto vertical mostraria isso.
	/// </summary>
	private void PorOsOitoRumos(float comp)
	{
		LimparOPalco();

		Vector2 t = Tela;
		float cw = t.X / 4f, ch = t.Y / 2f;

		for (int i = 0; i < OitoRumos.Length; i++)
		{
			Vec2 r = OitoRumos[i];
			var u = new Vector2(r.X, r.Y).Normalized();
			var centro = new Vector2(cw * (i % 4) + cw * 0.5f, ch * (i / 4) + ch * 0.5f);

			// CENTRADO NA CELULA: assim a celula inteira e a margem, e o mesmo comprimento serve pros
			// oito rumos sem um deles vazar pro vizinho.
			Vector2 mao = centro - u * (comp * 0.5f);
			Vector2 cabeca = centro + u * (comp * 0.5f);

			_oito.Add((NovoRaio(ArteDeKi.Beam3, 1f, cabeca, mao), mao, NomesDosOito[i]));
		}
	}

	/// <summary>
	/// O COMPRIMENTO DA GRADE, tirado da janela -- e de proposito NAO um multiplo de 32.
	///
	/// O comprimento que a familia 3 desenhava era `32 * 6`, um multiplo exato do ladrilho: o caso em
	/// que o ultimo corpo encosta na mao por ACASO. E por isso que ela nunca poderia ter pego o defeito
	/// do dono. Aqui o numero e torto de proposito, e o `- 3` no fim e o que garante isso em qualquer
	/// tamanho de janela.
	/// </summary>
	private float CompDaGrade() => MathF.Floor(MathF.Min(Tela.X / 4f, Tela.Y / 2f) * 0.78f) - 3f;

	private void MedirOsOito(string nome, string titulo)
	{
		Image? img = Foto(nome);
		if (img == null) { _t = 99; return; }

		int pior = 0, piorCentro = 0;
		string ondePior = "", ondePiorCentro = "";
		var partes = new List<string>();

		foreach ((ProjetilDesenhado no, Vector2 mao, string rotulo) in _oito)
		{
			Fresta faixa = Varrer(img, no.Position, mao, MeiaFaixa);
			Fresta centro = Varrer(img, no.Position, mao, MeiaLinha);
			partes.Add($"{rotulo} {faixa.Maior}/{centro.Total}");

			if (faixa.Maior > pior) { pior = faixa.Maior; ondePior = rotulo; }
			if (centro.Total > piorCentro) { piorCentro = centro.Total; ondePiorCentro = rotulo; }
		}

		Nota($"{titulo}: por rumo, fresta na faixa / fundo na linha central, em px -- "
			 + string.Join("   ", partes));
		Ok($"{titulo}: nenhum dos 8 rumos tem fresta no corpo"
		   + (pior > 0 ? $" (pior: {pior} px no {ondePior})" : ""), pior == 0);
		Ok($"{titulo}: nenhum dos 8 tem pixel de fundo na LINHA CENTRAL"
		   + (piorCentro > 0 ? $" (pior: {piorCentro} px no {ondePiorCentro})" : ""), piorCentro == 0);
	}

	// =====================================================================
	// FAMILIA 5c -- O FIO E O MURO
	// =====================================================================
	/// <summary>
	/// AS DUAS TECNICAS QUE O DM POE NOS EXTREMOS, com a arte e a escala que o jogo manda de verdade:
	///
	///   **Onda de Ki** -- `Beam3.dmi` (`ArteDeKi.cs:263`), `wavemult` 1. O fio.
	///   **Final Flash** -- `Beam - Big Fire.dmi` (`ArteDeKi.cs:284`), `wavemult` 4
	///                      (`GameServer.Tecnicas.G5.cs`). O muro.
	///
	/// ============================ A PERGUNTA E "A ESCALA MUDOU JUNTO?" ============================
	/// No DM o `wavemult` entra em `A.transform *= wavemult` (`beams.dm:149`) -- ele engorda o SPRITE.
	/// O espacamento do trem continua sendo um tile, porque la os segmentos sao objetos em tiles e
	/// nenhuma matriz de escala muda em que tile um objeto esta. **E por isso que o Final Flash e um
	/// muro e nao um trem esparramado.**
	///
	/// Um port que multiplicasse o PASSO pela escala tambem daria um feixe mais grosso -- e abriria ate
	/// 127 px de buraco a 4x. As duas metades desta familia separam as duas coisas: a foto mede a
	/// LARGURA (tem que crescer) e a `MedirTrem` responde o PASSO (nao pode mudar).
	/// ==========================================================================================
	/// </summary>
	private void PorOFioEOMuro()
	{
		Vector2 t = Tela;
		float comp = MathF.Floor(t.Y * 0.72f) - 5f;   // torto de proposito -- ver `CompDaGrade`
		float y0 = t.Y * 0.12f;

		PorOsDoisLivres(
			ArteDeKi.Beam3, 1f, new Vector2(t.X * 0.25f, y0 + comp), new Vector2(t.X * 0.25f, y0),
			$"O FIO (Onda de Ki: Beam3, wavemult 1), {comp:0} px",
			ArteDeKi.BeamBigFire, 4f, new Vector2(t.X * 0.72f, y0 + comp), new Vector2(t.X * 0.72f, y0),
			$"O MURO (Final Flash: Big Fire, wavemult 4), {comp:0} px");
	}

	private void MedirOFioEOMuro()
	{
		Image? img = Foto("13-o-fio-e-o-muro");
		if (img == null) { _t = 99; return; }

		MedirUmRaio(img, _a!, _maoA, _rotuloA);
		MedirUmRaio(img, _b!, _maoB, _rotuloB);

		int larguraFio = LarguraNaFoto(img, _a!.Position, _maoA);
		int larguraMuro = LarguraNaFoto(img, _b!.Position, _maoB);
		Nota($"largura medida no meio do feixe: fio {larguraFio} px, muro {larguraMuro} px "
			 + $"({(larguraFio > 0 ? (float)larguraMuro / larguraFio : 0f):0.0}x)");
		Ok($"o MURO e muito mais grosso que o FIO na foto ({larguraMuro} px contra {larguraFio})",
		   larguraFio > 0 && larguraMuro > larguraFio * 2);

		// ============================ E O PASSO, QUE NAO PODE TER CRESCIDO JUNTO ============================
		// A MESMA folha em duas escalas: se o passo dependesse do `wavemult`, este par seria 31 e 124.
		// Perguntado a `MedirTrem`, que e a funcao que o `_Draw` chama -- e nao a uma copia.
		// ================================================================================================
		SpriteFrames? f = ArteDeKiNoCliente.Folha(ArteDeKi.Beam3);
		if (f == null) { Nota("Beam3 nao carregou -- o passo nao foi medido"); return; }

		string dir = ProjetilDesenhado.SufixoDeDirecao(f, new Vec2(0, 1));
		string? aC = ProjetilDesenhado.NomeDaAnimacao(f, "head", dir);
		string? aT = ProjetilDesenhado.NomeDaAnimacao(f, "tail", dir);
		string? aM = ProjetilDesenhado.NomeDaAnimacao(f, "origin", dir) ?? aT;
		var paraTras = new Vector2(0, -1);

		float p1 = ProjetilDesenhado.MedirTrem(f, paraTras, 1f, aC, aT, aM).Passo;
		float p4 = ProjetilDesenhado.MedirTrem(f, paraTras, 4f, aC, aT, aM).Passo;
		Nota($"a MESMA folha a 1x e a 4x: passo {p1:0.0} px e {p4:0.0} px "
			 + $"(um passo que escalasse junto daria {p1 * 4:0.0})");
		Ok($"a escala engorda o SPRITE e nao espalha o TREM -- o passo nao mudou ({p1:0.0} = {p4:0.0})",
		   Mathf.Abs(p1 - p4) < 0.01f);
	}

	/// <summary>
	/// QUANTOS PIXELS DE TINTA ATRAVESSADOS no meio do feixe, na perpendicular. E a largura DESENHADA,
	/// que e o que o `wavemult` deveria mexer -- e ela e contada na foto e nao no tamanho da textura,
	/// porque textura grande com desenho pequeno dentro e exatamente o jeito de mentir aqui.
	/// </summary>
	private static int LarguraNaFoto(Image img, Vector2 cabeca, Vector2 mao)
	{
		Vector2 d = mao - cabeca;
		float comp = d.Length();
		if (comp < 1f) return 0;

		Vector2 u = d / comp;
		var perp = new Vector2(-u.Y, u.X);
		Vector2 meio = cabeca + u * (comp * 0.5f);

		int n = 0;
		for (int t = -400; t <= 400; t++)
		{
			Vector2 p = meio + perp * t;
			int px = Mathf.RoundToInt(p.X), py = Mathf.RoundToInt(p.Y);
			if (px < 0 || px >= img.GetWidth() || py < 0 || py >= img.GetHeight()) continue;
			Color c = img.GetPixel(px, py);
			if (!(c.G > 0.9f && c.R < 0.1f && c.B < 0.1f)) n++;
		}
		return n;
	}

	// =====================================================================
	// FAMILIA 5d -- O DEFEITO INJETADO
	// =====================================================================
	/// <summary>
	/// A SONDA TEM QUE SABER FICAR VERMELHA, e as oito fotos verdes acima nao provam isso.
	///
	/// Ver <see cref="ProjetilDesenhado.DefeitoDoTrem"/>: aqui o defeito que o dono fotografou e
	/// religado no caminho de desenho de PRODUCAO (nao numa copia) e as MESMAS duas sondas medem o
	/// MESMO par de comprimentos da familia 4 -- os 205 e os 249 px que ja tinham dado 13 e 25 px de
	/// fresta antes do conserto. Elas tem que reprovar.
	///
	/// E a ultima fase desliga tudo e mede de novo: uma sonda que ficasse vermelha pra sempre depois de
	/// ver um defeito nao valeria mais que uma que fica verde sempre.
	/// </summary>
	private void MedirEsperandoFresta(string nome, string oque, bool esperaNoRestoZero)
	{
		Image? img = Foto(nome);
		if (img == null) { _t = 99; return; }

		Injetado(img, _a!, _maoA, _rotuloA, oque, true);
		Injetado(img, _b!, _maoB, _rotuloB, oque, esperaNoRestoZero);
	}

	private void Injetado(Image img, ProjetilDesenhado no, Vector2 mao, string rotulo, string oque,
						  bool esperaFresta)
	{
		Fresta faixa = Varrer(img, no.Position, mao, MeiaFaixa);
		Fresta centro = Varrer(img, no.Position, mao, MeiaLinha);
		Nota($"[injecao] {rotulo}: fresta {faixa.Maior} px a {faixa.Onde} px da cabeca; "
			 + $"linha central {centro.Total} px de fundo");

		if (esperaFresta)
		{
			Ok($"[injecao] com `{oque}` a sonda REPROVA o {rotulo} ({faixa.Maior} px de fresta)",
			   faixa.Maior > 0);
			Ok($"[injecao] ...e a LINHA CENTRAL tambem ({centro.Total} px de fundo)", centro.Total > 0);
			return;
		}

		// ============================ E AQUI O DEFEITO E INVISIVEL, DE PROPOSITO ============================
		// A lei do bug antigo era `fresta = comprimento mod passo`: no MULTIPLO EXATO do passo ele
		// sumia, porque o pedaco que faltava era coberto pela metade da cabeca e pela metade da mao.
		// Esta linha e a prova de que a bancada entendeu a lei em vez de so ter visto o buraco -- e ela
		// explica o "quando ele e LANCADO" do relato: o dono nao via o raio picotado sempre, via nos
		// comprimentos errados, e um raio que cresce 3,3 vezes por segundo passa por todos eles.
		// ================================================================================================
		Ok($"[injecao] ...e no MULTIPLO EXATO do passo o mesmo defeito e INVISIVEL ({rotulo}, "
		   + $"{faixa.Maior} px) -- era essa a lei do `comprimento mod passo`", faixa.Maior == 0);
	}

	// =====================================================================
	// FAMILIA 5e -- A FASE DA DIAGONAL LONGA
	// =====================================================================
	/// <summary>
	/// ============================ ELA NASCEU DE UMA FALHA DESTA MESMA RODADA ============================
	/// A familia 5 reproduziu a foto do dono de verdade -- 1374 px de diagonal, 42,9 tiles -- e mediu
	/// **1 px de fresta a 49 px da cabeca**, numa junta so, com as outras quarenta inteiras. As fotos
	/// diagonais que esta bancada ja tinha (190 e 253 px) nunca poderiam ter pego isso: sao cinco e
	/// sete juntas, e o defeito aparece em uma fase em tres.
	///
	/// ============================ E A FASE ANDA SOZINHA AO LONGO DO FEIXE ============================
	/// `project.godot` liga `snap_2d_vertices_to_pixel`: cada estampa e ARREDONDADA pra o pixel na hora
	/// de desenhar. O deslocamento entre dois corpos vizinhos na diagonal e `passo * 0,7071` -- 23,33 px
	/// -- e o `,33` se acumula: a fase de cada junta e diferente da anterior e o feixe percorre TODAS
	/// elas se for comprido o bastante. Um raio curto so ve as primeiras.
	///
	/// **E por isso que esta familia precisa de raios LONGOS DE VERDADE, um por foto**, e nao de doze
	/// pedacinhos lado a lado numa grade: doze raios de trinta juntas varrem 360 fases; doze de cinco
	/// varreriam sessenta, e sempre as mesmas sessenta.
	/// ================================================================================================
	/// </summary>
	private const int QuantasDiagonais = 12;

	private int _diagonal, _piorDaFase, _fundoDasFases, _juntasVarridas;
	private string _ondePiorDaFase = "";

	/// <summary>
	/// UMA diagonal longa, no rumo do print do dono (sudoeste), com um comprimento diferente a cada
	/// chamada -- e nenhum deles multiplo do passo, que e onde o defeito se esconde.
	/// </summary>
	private void PorADiagonalLonga(int i)
	{
		if (i >= QuantasDiagonais) return;

		Vector2 t = Tela;
		const float Meio = 0.70710678f;
		var mao = new Vector2(t.X * 0.95f, t.Y * 0.05f);
		float maior = MathF.Floor(MathF.Min((mao.X - t.X * 0.04f) / Meio, (t.Y * 0.95f - mao.Y) / Meio));

		// O PASSO DE 7,3 px NAO E BONITO DE PROPOSITO: 7,3 nao divide o passo do trem (33 px) nem o
		// tile (45,25 na diagonal), entao os doze comprimentos caem em doze restos diferentes.
		float comp = maior - (i * 7.3f);
		Vector2 cab = mao + new Vector2(-Meio, Meio) * comp;

		LimparOPalco();
		_a = NovoRaio(ArteDeKi.Beam3, 1f, cab, mao);
		_maoA = mao;
		_rotuloA = $"diagonal de {comp:0} px ({comp / 32f:0.0} tiles)";
	}

	private void MedirADiagonalLonga(int i)
	{
		// SO A PRIMEIRA VIRA ARQUIVO. As doze medem igual; doze PNGs de 1920x1080 pra mostrar a mesma
		// coisa e peso sem resposta nova.
		Image? img = Foto(i == 0 ? "17-diagonal-longa-como-no-print" : "");
		if (img == null) { _t = 99; return; }

		Fresta faixa = Varrer(img, _a!.Position, _maoA, MeiaFaixa);
		Fresta centro = Varrer(img, _a!.Position, _maoA, MeiaLinha);

		_fundoDasFases += centro.Total;
		_juntasVarridas += (int)((_maoA - _a!.Position).Length() / 33f);
		if (faixa.Maior > _piorDaFase)
		{
			_piorDaFase = faixa.Maior;
			_ondePiorDaFase = $"{_rotuloA}, a {faixa.Onde} px da cabeca";
		}

		if (i < QuantasDiagonais - 1) return;

		Nota($"{QuantasDiagonais} diagonais longas varridas -- cerca de {_juntasVarridas} juntas, "
			 + $"{_fundoDasFases} px de fundo na linha central no total");
		Ok($"nenhuma das ~{_juntasVarridas} juntas da diagonal LONGA abre"
		   + (_piorDaFase > 0 ? $" (pior: {_piorDaFase} px em {_ondePiorDaFase})" : ""),
		   _piorDaFase == 0);
		Ok($"e nenhuma delas tem um so pixel de fundo na LINHA CENTRAL ({_fundoDasFases} px)",
		   _fundoDasFases == 0);
	}

	/// <summary>O passo que o DESENHO usaria pra esta folha neste rumo -- pelo mesmo `MedirTrem`.</summary>
	private static float PassoDe(ArteDeKi arte, Vector2 paraTras, float escala)
	{
		SpriteFrames? f = ArteDeKiNoCliente.Folha(arte);
		if (f == null) return 31f;

		string dir = ProjetilDesenhado.SufixoDeDirecao(f, new Vec2(-paraTras.X, -paraTras.Y));
		string? aC = ProjetilDesenhado.NomeDaAnimacao(f, "head", dir);
		string? aT = ProjetilDesenhado.NomeDaAnimacao(f, "tail", dir);
		string? aM = ProjetilDesenhado.NomeDaAnimacao(f, "origin", dir)
					 ?? ProjetilDesenhado.NomeDaAnimacao(f, "end", dir) ?? aT;
		return ProjetilDesenhado.MedirTrem(f, paraTras, escala, aC, aT, aM).Passo;
	}

	/// <summary>Onde o defeito antigo aparecia inteiro, e onde ele sumia. Ver <see cref="Injetado"/>.</summary>
	private int _compRuim = 205, _compBom = 248;

	/// <summary>
	/// OS DOIS COMPRIMENTOS DA INJECAO, CALCULADOS DO PASSO MEDIDO -- e nao escritos a mao.
	///
	/// A primeira versao desta familia usou os 205 e 249 px que a rodada anterior tinha fotografado, e
	/// o 249 ficou VERMELHO sem defeito nenhum: com o passo em 31 px, 249 e quase multiplo exato e o
	/// bug antigo desaparece ali. Um teste que exige ver o defeito num comprimento onde o defeito nao
	/// existe reprova o codigo certo -- foi a bancada que estava errada, e o conserto e perguntar ao
	/// passo em vez de lembrar de um numero.
	/// </summary>
	private void EscolherOsComprimentosDaInjecao()
	{
		// `paraTras` do raio que desce e pra CIMA -- a mao fica acima da cabeca.
		float p = PassoDe(ArteDeKi.Beam3, new Vector2(0, -1), 1f);
		_compRuim = (int)MathF.Round(6 * p + (p - 1));   // resto MAXIMO: o defeito inteiro
		_compBom = (int)MathF.Round(8 * p);              // resto ZERO: o defeito invisivel
		Nota($"passo medido da `Beam3` ao sul: {p:0.0} px -- resto maximo em {_compRuim} px, "
			 + $"resto zero em {_compBom} px");
	}

	/// <summary>
	/// O CUSTO, MEDIDO -- porque pode haver varios feixes no ar, cada um com dezenas de pedacos.
	///
	/// Sao tres numeros diferentes e vale nao confundi-los: a medida da folha (uma vez por folha e
	/// direcao, na vida do processo), a montagem do trem (uma vez por tiro por quadro) e as estampas
	/// (o que de fato vai pra a placa de video).
	/// </summary>
	private void MedirOCusto()
	{
		// Uma folha que NENHUMA familia acima mediu (a varredura mediu Beam3, Beam4 e Big Fire): assim
		// a primeira chamada e mesmo a FRIA, com o atlas ainda por descomprimir.
		SpriteFrames? f = ArteDeKiNoCliente.Folha(ArteDeKi.Beam2);
		if (f == null) { Nota("Beam2 nao carregou -- custo nao medido"); return; }

		string dir = ProjetilDesenhado.SufixoDeDirecao(f, new Vec2(0, 1));
		string? aC = ProjetilDesenhado.NomeDaAnimacao(f, "head", dir);
		string? aT = ProjetilDesenhado.NomeDaAnimacao(f, "tail", dir);
		string? aM = ProjetilDesenhado.NomeDaAnimacao(f, "origin", dir) ?? aT;
		var paraTras = new Vector2(0, -1);

		var relogio = System.Diagnostics.Stopwatch.StartNew();
		ProjetilDesenhado.MedidaDoTrem m = ProjetilDesenhado.MedirTrem(f, paraTras, 1f, aC, aT, aM);
		double frio = relogio.Elapsed.TotalMilliseconds;

		const int N = 200000;
		relogio.Restart();
		double soma = 0;
		for (int i = 0; i < N; i++)
		{
			ProjetilDesenhado.MedidaDoTrem mm = ProjetilDesenhado.MedirTrem(f, paraTras, 1f, aC, aT, aM);
			ProjetilDesenhado.TremDoRaio t = ProjetilDesenhado.MontarTrem(500 + (i % 97), mm.Passo, mm.SemCorpo);
			soma += t.Passo;   // pra o compilador nao apagar o laco
		}
		double porChamada = relogio.Elapsed.TotalMilliseconds * 1000.0 / N;

		// O raio mais longo do jogo: 30 tiles de Ki Wave (`beams.dm:294`) e 50 no topo da pericia.
		ProjetilDesenhado.TremDoRaio longo = ProjetilDesenhado.MontarTrem(50 * 32, m.Passo, m.SemCorpo);

		Nota($"medir a folha (uma vez por folha+animacao+fatia de rumo, pra sempre): {frio:0.00} ms "
			 + $"-- soma {soma:0}");
		Nota($"montar o trem (uma vez por tiro por quadro): {porChamada:0.00} us");
		Nota($"um raio de 50 tiles = {longo.Corpos + 2} estampas por quadro "
			 + $"(passo {longo.Passo:0.0} px; o desenho antigo dava {50 * 32 / 32} e deixava a fresta)");

		// O CUSTO NAO PODE CRESCER com o conserto: quem consertaria a fresta instanciando um node por
		// pedaco pagaria 52 nodes por raio. Aqui o trem inteiro continua sendo UM node e N estampas --
		// uma a mais que antes, que e literalmente o pedaco que faltava.
		Ok($"montar o trem custa menos de 1 us ({porChamada:0.00} us)", porChamada < 1.0);
		Ok($"o raio mais longo do jogo cabe em menos de 60 estampas ({longo.Corpos + 2})",
		   longo.Corpos + 2 < 60);
	}

	/// <summary>
	/// Vira um dos dois tiros numa BOLA. A cauda vai pro mesmo ponto da cabeca de proposito: bola
	/// nao tem rastro (`Projetil.Comprimento` devolve zero pra ela), e deixar a cauda velha faria a
	/// bancada medir um raio disfarçado.
	/// </summary>
	private static void VirarBola(ProjetilDesenhado no, ArteDeKi arte, Color cor)
	{
		no.Tipo = TipoDeProjetil.Blast;
		no.Cor = cor;
		no.Vestir(arte, 1f);
		no.Mirar(no.Position, no.Position);
		no.QueueRedraw();
	}

	private void FotografarBolas()
	{
		Image? img = Foto("5-bolas");
		if (img == null) { _t = 99; return; }

		int meio = img.GetWidth() / 2;
		(int Pixels, int Tons, long Hash) racial = Assinatura(img, 0, meio);
		(int Pixels, int Tons, long Hash) paralisia = Assinatura(img, meio, img.GetWidth());

		Nota($"bola racial (1.dmi): {racial.Pixels}px, {racial.Tons} tons");
		Nota($"Paralysis (KiHead.dmi, estado `paralysis`): {paralisia.Pixels}px, {paralisia.Tons} tons");

		Ok("a bola RACIAL desenha", racial.Pixels > 20);
		Ok("a Paralysis desenha -- a folha sem `default`, pelo `icon_state` do DM", paralisia.Pixels > 20);
		Ok("as duas bolas sao desenhos diferentes", racial.Hash != paralisia.Hash);
	}

	/// <summary>Quantos pixels desenhados, e quantos deles sao branco puro (a assinatura da saturacao).</summary>
	private static (int Pixels, int Brancos) ContarBrancos(Image img, int x0, int x1)
	{
		int n = 0, brancos = 0;
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = x0; x < x1; x++)
			{
				Color c = img.GetPixel(x, y);
				if (c.G > 0.9f && c.R < 0.1f && c.B < 0.1f) continue;   // o fundo
				n++;
				if (c.R > 0.98f && c.G > 0.98f && c.B > 0.98f) brancos++;
			}
		return (n, brancos);
	}

	private void FotografarEscala()
	{
		Image? img = Foto("3-escala-3x");
		if (img == null) { _t = 99; return; }

		(int Pixels, int Tons, long Hash) grande = Assinatura(img, img.GetWidth() / 2, img.GetWidth());
		Nota($"Masenko a 3x: {grande.Pixels}px (era {_assB.Pixels}px a 1x)");

		// `A.transform *= wavemult` (`beams.dm:149`). O `wavemult` do Final Flash e 4 e o do Ki Wave
		// e 1 -- sem esta linha os dois sairiam do mesmo tamanho, que e como estavam.
		Ok("a escala do `wavemult` chega ao pixel (3x desenha bem mais que 1x)",
		   grande.Pixels > _assB.Pixels * 2);
	}
}
