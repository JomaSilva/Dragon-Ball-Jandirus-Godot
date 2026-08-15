using Jandirus.Core.Appearance;

namespace Jandirus.Core.Npc;

/// <summary>
/// O QUE ESTE CORPO VESTE -- a tabela de roupa por raca do original, portada linha a linha.
///
/// ============================ O PEDIDO DO DONO ============================
/// *"todo npc ta nascendo SEM ROUPAS, coloque roupas neles, mas claro ROUPA DE SAIYAJIN pra
/// saiyajins e ROUPAS COMUNS pra humanos, e ROUPAS DE NAMEK pra nameks (no byond ta tudo certinho
/// la qual sprite e de cada raca)"*.
///
/// Ele esta certo: a tabela existe no DM e e `PlanetPopulation.dm:359-415`. O que **nao** existe la
/// e uma linha pras outras nove racas que este port poe no mundo -- o DM povoa TRES planetas
/// (Terra, Vegeta, Namek) e este povoa SEIS, com a raca vindo do BERCO e nao da tabela. Onde falta
/// linha do DM, a escolha e MINHA e esta marcada como tal, uma por uma, no bloco `EscolhaMinha`.
/// ==========================================================================
///
/// ============================ POR QUE ISTO E CORE, E NAO SERVIDOR ============================
/// Porque e decisao, e decisao mora no Core. O que o Core NAO faz e conhecer o caminho de Godot: a
/// tabela cita o arquivo como o DM cita (`'Armor 8.dmi'`), e quem transforma nome em
/// `res://Assets/...` e o <see cref="VisualCatalog.Peca"/>, que leu o disco no passo de extracao.
/// Escrever `res://` aqui seria caminho de cliente dentro do Core e envelheceria calado.
/// ==========================================================================================
///
/// ============================ E POR QUE ELE NAO VESTE NINGUEM ============================
/// Este arquivo devolve uma LISTA DE PECAS. Quem veste e o funil unico da aparencia
/// (`GameServer.AparenciaDeNpc` -> `VisualCatalog.Sanear` -> `Protocol.PutAppearance` ->
/// `CharacterVisual.Vestir`), que e **o mesmo caminho do jogador**, camada por camada. Uma segunda
/// forma de vestir divergiria da primeira, e a que ninguem olha e sempre a que apodrece.
/// ======================================================================================
/// </summary>
public static class RoupaDeNpc
{
	// =====================================================================
	// OS LITERAIS DO DM
	// =====================================================================
	// Nomes de ARQUIVO, como o original os escreve. O `.dmi` fica de fora de proposito: o port tem
	// `.tres`, e a resolucao e por nome-base (ver `VisualCatalog.Peca`).

	/// <summary>`pick('Armor 8','Armor Bardock','Nappa Armor','RaditzArmorTobiUchiha')` -- PlanetPopulation.dm:368.</summary>
	private static readonly string[] ArmaduraComum =
		["Armor 8", "Armor Bardock", "Nappa Armor", "RaditzArmorTobiUchiha"];

	/// <summary>A UNICA que o original tinge: `npc_wear_armor_icon(M, armor, armor == 'Armor 8.dmi')` -- PlanetPopulation.dm:369.</summary>
	private const string ArmaduraTingida = "Armor 8";

	// O KIT DE ELITE do `make_saiyan_elite` (PlanetPopulation.dm:381-384 -- 'Armor_Elite' ou
	// 'Clothes_VegetaSaiyanSagaArmor' + luvas + botas + a capa do Rei) NAO tem constante aqui de
	// proposito: ele nao e sorteado por raca, e roupa DECLARADA do molde `guardiao_saiyajin` no
	// `npcs.json`. Uma constante nao lida seria a tabela existindo em dois lugares com um dos dois
	// morto, que e como uma delas comeca a mentir.

	/// <summary>`pick(TankTop, ShortSleeveShirt, LongSleeveShirt)` -- PlanetPopulation.dm:402. Typepath -> .dmi em Clothes.dm:105-124.</summary>
	private static readonly string[] RoupaComum =
		["Clothes_TankTop", "Clothes_ShortSleeveShirt", "Clothes_LongSleeveShirt"];

	private const string GiTop = "Clothes_GiTop";         // /obj/items/clothes/Gi_Top      -- Clothes.dm:59-60
	private const string GiBottom = "Clothes_GiBottom";   // /obj/items/clothes/Gi_Bottom   -- Clothes.dm:56-57
	private const string GiFemale = "ClothesGiFemale";    // /obj/items/clothes/Gifemale    -- Clothes.dm:289-290
	private const string NamekJacket = "ClothesNamekJacket";      // /obj/items/clothes/Namekjacket    -- Clothes.dm:310-311
	private const string NamekScarf = "Clothes_NamekianScarf";    // /obj/items/clothes/NamekianScarf  -- Clothes.dm:120-121
	private const string LuvasSaiyajin = "Clothes, Saiyan Gloves"; // /obj/items/clothes/SaiyanGloves  -- Clothes.dm:181-182
	private const string BotasSaiyajin = "Clothes, Saiyan Shoes";  // /obj/items/clothes/SaiyanShoes   -- Clothes.dm:184-185

	// --- pecas usadas so nas ESCOLHAS MINHAS (o DM nao declara roupa pra estas racas) ---
	private const string TrajeYardrat = "ClothesFullYardrat";  // /obj/items/clothes/Yardrat    -- Clothes.dm:292-293
	private const string TrajeKaio = "Clothes Elder Kaio";     // /obj/items/clothes/KaioSuit   -- Clothes.dm:126-127
	private const string TrajeMakyo = "PhoenixFullMakyo";      // /obj/items/clothes/Phoenixfullmakyo -- Clothes.dm:358-359
	private const string MantoDemonio = "Clothes Daimaou";     // /obj/items/clothes/Daimaou    -- Clothes.dm:157-158
	private const string CapaDemonio = "ClothesDaimaouCape";   // /obj/items/clothes/Dimeowcape -- Clothes.dm:286-287

	// =====================================================================
	// A COR DA ARMADURA
	// =====================================================================
	/// <summary>
	/// O `rgb(rand(0,255), rand(0,255), rand(0,255))` de `npc_wear_armor_icon`
	/// (PlanetPopulation.dm:250) -- **com a faixa apertada, e o motivo importa**.
	///
	/// La a cor e SOMADA (`C.icon += rgb(...)`, o ICON_ADD do BYOND): ela so CLAREIA, entao sortear
	/// 0..255 nunca escurece nada. Aqui a peca e tingida por MATIZ (`Tinta.DaFicha(cor, matiz: true)`,
	/// CharacterVisual.cs:757), e nessa rampa **o hexa escrito e o pixel mais ESCURO da peca**. Com
	/// 0..255 por canal, boa parte dos sorteios sairia armadura quase preta -- que e o defeito que ja
	/// custou duas reclamacoes do dono quando o Blue saiu marinho e o Rose saiu vinho.
	///
	/// A faixa <see cref="PisoDaCor"/>..(PisoDaCor+<see cref="FaixaDaCor"/>) mantem a peca legivel nas
	/// duas pontas da rampa sem tirar a variedade, que era o ponto do sorteio.
	/// </summary>
	private const int PisoDaCor = 56, FaixaDaCor = 152;

	// =====================================================================
	// QUEM JA NASCE VESTIDO
	// =====================================================================
	/// <summary>
	/// ============================ O SPRITE DESTE POVO JA E A ROUPA ============================
	/// Nao e buraco na tabela: e a resposta, e ela e "nada".
	///
	///   Frost Demon -- o corpo dele E a FORMA, um arquivo inteiro por degrau, e o bio-traje esta
	///                  desenhado nele (`VisualCatalog.CorpoSprite` nem procura corpo pra essa raca).
	///   Bio-Androide - uma casca: os quatro degraus sao arquivos inteiros (`Races.BioAndroids.Corpos`).
	///   Saibaman ----- criatura vegetal criada pronta; o sprite e o bicho todo.
	///   Dog / Boneca - idem, e nao ha peca desenhada que sirva nesses corpos.
	///
	/// O DM concorda por OMISSAO, e da pra apontar onde: as fabricas dos chefes que sao destas racas
	/// (Freeza, Cell, Boo -- BossEvents.dm) nao chamam `npc_wear_*` uma unica vez, enquanto as dos
	/// Androides 17 e 18, que sao de corpo humano, chamam (BossEvents.dm:499,508).
	///
	/// ============================ E POR QUE ELE E PUBLICO ============================
	/// Porque a bancada precisa da MESMA resposta. Ela afirma "todo cidadao nasce vestido", e sem
	/// esta funcao ela teria que carregar a propria lista de excecoes -- duas listas, e a segunda
	/// envelhece calada na primeira raca nova. Com uma casa so, acrescentar uma raca de sprite
	/// proprio muda a bancada junto.
	///
	/// O que a bancada continua tendo de dente: ela exige que quem NAO esta nesta lista esteja
	/// vestido. Uma raca posta aqui por engano vira NPC pelado em jogo, e isso o dono ve.
	/// ==============================================================================
	/// </summary>
	public static bool OCorpoJaVeste(string raca) =>
		Races.FormasDeFrost.EhFrost(raca) || Races.BioAndroids.EhBio(raca)
		|| raca is "Saibaman" or "Dog" or "SpiritDoll";

	// =====================================================================
	// A PORTA
	// =====================================================================
	/// <summary>
	/// AS PECAS QUE ESTE CORPO VESTE. Deterministico pela semente DELE: o mesmo mundo devolve o mesmo
	/// NPC com a mesma roupa em qualquer maquina e em qualquer reinicio -- o NPC nao tem save
	/// (`GameServer.Npc.cs:234`), entao a roupa dele nao PERSISTE: ela e REDEDUZIDA, que e mais forte.
	/// Nao ha `Random` sem semente em lugar nenhum deste arquivo.
	/// </summary>
	/// <param name="cat">O catalogo lido do disco -- e quem sabe onde a arte esta.</param>
	/// <param name="raca">A raca do corpo (a do berco, no caso do cidadao).</param>
	/// <param name="genero">"Male" / "Female" -- so o Gi do DM olha pra isto (PlanetPopulation.dm:397).</param>
	/// <param name="semente">A semente do NPC (`SorteioDeNpc.SementeDe`), a MESMA do corpo e do cabelo.</param>
	/// <param name="declarada">
	/// A roupa CRAVADA no molde (`npcs.json`), que vence tudo. E por onde o chefe de saga se veste --
	/// ver o bloco em <see cref="MoldeDeNpc.Roupa"/>. Vazia = sorteio pela raca.
	/// </param>
	/// <param name="faltando">Recebe o nome de toda peca da tabela que NAO existe no port. Ver abaixo.</param>
	public static List<PecaDeRoupa> Vestir(
		VisualCatalog cat, string raca, string genero, ulong semente,
		string[]? declarada = null, List<string>? faltando = null)
	{
		var pecas = new List<PecaDeRoupa>();

		// ============================ O MOLDE VENCE A RACA ============================
		// E assim que "o chefe de saga NAO entra neste sorteio" deixa de ser uma regra escrita e
		// vira a forma do dado: o chefe nunca chega ao `switch` la embaixo (ver o chamador). Os
		// Androides 17 e 18 sao a prova de que isto e porte e nao invencao -- o original os veste a
		// mao, um a um, com roupa CASUAL e o comentario dizendo por que: *"nada de Namek/armadura
		// Saiyajin"* (BossEvents.dm:499 e :508).
		if (declarada is { Length: > 0 })
		{
			foreach (string nome in declarada) Por(pecas, cat, nome, null, faltando);
			return Aparar(pecas);
		}

		// QUEM NAO VESTE NADA -- ver <see cref="OCorpoJaVeste"/>, que e a UNICA casa dessa resposta.
		if (OCorpoJaVeste(raca)) return pecas;

		switch (raca)
		{
			// =========================================================================
			// SAIYAJIN -- `make_saiyan_commoner`, PlanetPopulation.dm:359-370
			// =========================================================================
			// var/armor = pick('Armor 8.dmi','Armor Bardock.dmi','Nappa Armor.dmi','RaditzArmorTobiUchiha.dmi')  :368
			// npc_wear_armor_icon(M, armor, armor == 'Armor 8.dmi')                                              :369
			//
			// O KIT DE ELITE (`make_saiyan_elite`, :372-385 -- armadura de elite + luvas + botas +
			// capa do Rei) NAO entra aqui: no DM ele e escolhido pelo PAPEL ("king"/"prince"), e o
			// povoamento deste port nao tem papel nenhum -- as linhas do `npcs.json` sao planeta,
			// molde e quantos. Ele existe e esta portado, so que como roupa DECLARADA do molde de
			// chefe (`guardiao_saiyajin`), que e o Saiyajin de elite que este port de fato poe no
			// mundo. Ver `Assets/Data/npcs.json`.
			case "Saiyan":
			// ESCOLHA MINHA: o meio-Saiyajin nao tem linha no DM. Ele vai com o Saiyajin e nao com o
			// Humano porque a armadura e o que marca a metade que o jogo mostra.
			//
			// ============================ O ROTULO FALTAVA, E SO A BANCADA VIU ============================
			// Este comentario existia desde o primeiro dia e o `case "Halfbreed":` NAO -- o meio-Saiyajin
			// caia no `default` la embaixo e nascia de regata, enquanto o comentario, o relatorio ao dono
			// e a tabela do porte diziam "armadura". O codigo nao contradizia o DM: ele contradizia a
			// propria intencao declarada uma linha acima, que e a classe de defeito mais dificil de ver
			// lendo -- os olhos leem a frase e nao a lista de rotulos.
			//
			// Quem pegou foi a `--mundoprova`, familia 5, porque o oraculo dela e uma SEGUNDA lista
			// escrita a mao: perguntar a este arquivo o que o Halfbreed veste e comparar com o que este
			// arquivo devolveu teria ficado verde pra sempre.
			// =========================================================================================
			case "Halfbreed":
				Armadura(pecas, cat, semente, ArmaduraComum, faltando);
				break;

			// =========================================================================
			// HUMANO -- `make_human`, PlanetPopulation.dm:387-403
			// =========================================================================
			// if(prob(50))                                                            :396
			//     if(g == "female") npc_wear_simple(M, /obj/items/clothes/Gifemale)    :397
			//     else  Gi_Top + Gi_Bottom                                             :399-400
			// else pick(TankTop, ShortSleeveShirt, LongSleeveShirt)                     :402
			case "Human":
			case "Android":     // roupa casual, e e LITERAL do DM: BossEvents.dm:499,508 (17 e 18)
				Humano(pecas, cat, genero, semente, faltando);
				break;

			// =========================================================================
			// NAMEKUSEIJIN -- `make_namekian`, PlanetPopulation.dm:405-415
			// =========================================================================
			// if(prob(60)) npc_wear_simple(M, /obj/items/clothes/Namekjacket)     :413
			// if(prob(50)) npc_wear_simple(M, /obj/items/clothes/NamekianScarf)   :414
			case "Namekian":
				if (Prob(semente, "roupa_namek_jaqueta", 60)) Por(pecas, cat, NamekJacket, null, faltando);
				if (Prob(semente, "roupa_namek_cachecol", 50)) Por(pecas, cat, NamekScarf, null, faltando);
				break;

			// =========================================================================
			// ESCOLHA MINHA -- o DM NAO TEM LINHA PRA NENHUMA DESTAS
			// =========================================================================
			// O original povoa Terra, Vegeta e Namek; este port povoa seis planetas e a raca sai do
			// berco, entao ele poe no mundo racas que a tabela do DM nunca precisou vestir. Cada
			// escolha abaixo esta declarada como escolha, com o porque -- e todas usam arte que o
			// PROPRIO jogo ja desenhou pra aquele povo, o que e o mais perto de "o DM decidiu" que
			// da pra chegar sem inventar.

			// Traje completo de Yardrat: o jogo tem UMA peca com esse nome (Clothes.dm:292-293),
			// desenhada pro povo que ensina a Teleport. Nao ha escolha mais obvia.
			case "Yardrat":
				Por(pecas, cat, TrajeYardrat, null, faltando);
				break;

			// O traje do Kaioh Ancestral (Clothes.dm:126-127) e a roupa dos deuses deste jogo.
			// Demigod e a raca dos Kaioshin neste port; Kai e a linhagem divina.
			case "Kai":
			case "Demigod":
				Por(pecas, cat, TrajeKaio, null, faltando);
				break;

			// O jogo desenhou uma peca com "Makyo" NO NOME (Clothes.dm:358-359). Se ela existe pra
			// alguem, e pra o povo da Estrela Makyo -- que e um dos seis planetas povoados aqui.
			case "Makyo":
				Por(pecas, cat, TrajeMakyo, null, faltando);
				break;

			// Manto do Rei Demonio (Clothes.dm:157-158) + a capa dele (Clothes.dm:286-287) em metade
			// dos casos, no mesmo formato "peca base + acessorio por probabilidade" que o DM usa no
			// Namekuseijin. A capa e por sorteio pra o povo nao sair uniformizado.
			case "Demon":
				Por(pecas, cat, MantoDemonio, null, faltando);
				if (Prob(semente, "roupa_demonio_capa", 50)) Por(pecas, cat, CapaDemonio, null, faltando);
				break;

			// A ARMADURA DE BATALHA E O UNIFORME DO EXERCITO, e nao roupa de Saiyajin: os Saiyajins
			// do DM a vestem porque SERVIAM ao Freeza. Alien e a raca de Zarbon, Dodoria, Cui e
			// Appule -- a mesma tropa, os mesmos nomes que o `npcs.json` ja da a eles --, e Heran e a
			// raca do Bojack, que o desenho tambem poe de armadura. Mesmo pool, mesma regra de
			// tintura da 'Armor 8'.
			case "Alien":
			case "Heran":
				Armadura(pecas, cat, semente, ArmaduraComum, faltando);
				break;

			// (Saibaman, Dog e SpiritDoll nao chegam aqui: saem no `OCorpoJaVeste` la em cima.)

			// TODO O RESTO: a roupa comum do DM (a mesma que o Humano ganha quando o Gi nao sai).
			// E o "mais proximo" honesto pra Majin, Shapeshifter, Gray, Kanassa, Arlian, Meta e
			// Tsujin: sao povos CIVIS, e a camisa/regata e o que um civil veste. Nenhum deles tem
			// arte propria no jogo -- procurei --, e inventar um traje seria pior que a camisa.
			// Quando o dono decidir o que um deles usa, e uma linha aqui ou um `roupa` no molde.
			default:
				Por(pecas, cat, Escolher(semente, "roupa_comum", RoupaComum), null, faltando);
				break;
		}

		return Aparar(pecas);
	}

	// =====================================================================
	// AS TRES RECEITAS
	// =====================================================================
	private static void Armadura(List<PecaDeRoupa> pecas, VisualCatalog cat, ulong semente,
								 string[] pool, List<string>? faltando)
	{
		string armadura = Escolher(semente, "roupa_armadura", pool);

		// `armor == 'Armor 8.dmi'` -- PlanetPopulation.dm:369. So esta tem cor sorteada; as outras
		// tres ja vem pintadas pelo artista, e tingi-las apagaria o desenho delas.
		Rgb? cor = armadura == ArmaduraTingida ? CorDaArmadura(semente) : null;
		Por(pecas, cat, armadura, cor, faltando);
	}

	private static void Humano(List<PecaDeRoupa> pecas, VisualCatalog cat, string genero,
							   ulong semente, List<string>? faltando)
	{
		if (Prob(semente, "roupa_gi", 50))                        // if(prob(50))  -- :396
		{
			if (genero == "Female") Por(pecas, cat, GiFemale, null, faltando);   // :397
			else                                                                  // :399-400
			{
				Por(pecas, cat, GiTop, null, faltando);
				Por(pecas, cat, GiBottom, null, faltando);
			}
			return;
		}
		Por(pecas, cat, Escolher(semente, "roupa_comum", RoupaComum), null, faltando);   // :402
	}

	/// <summary>A cor sorteada da 'Armor 8'. Ver <see cref="PisoDaCor"/> pra a faixa e o motivo dela.</summary>
	private static Rgb CorDaArmadura(ulong semente)
	{
		Random r = SorteioDeNpc.Sorteador(semente, "roupa_armadura_cor");
		return new Rgb(Canal(r), Canal(r), Canal(r));
	}

	private static byte Canal(Random r) => (byte)(PisoDaCor + r.Next(0, FaixaDaCor + 1));

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// PENDURA UMA PECA -- ou DENUNCIA que a arte nao existe.
	///
	/// O `faltando` e o pedido literal do dono pra esta tarefa (*"Se faltar arte, PARE e RELATE
	/// aquela peca; entregue as outras racas"*), e ele e por RACA: um Namekuseijin sem cachecol nao
	/// pode impedir o Saiyajin de vestir a armadura. Quem le a lista e a bancada e o log do servidor.
	/// </summary>
	private static void Por(List<PecaDeRoupa> pecas, VisualCatalog cat, string nome, Rgb? cor,
							List<string>? faltando)
	{
		string? caminho = cat.Peca(nome);
		if (caminho == null) { faltando?.Add(nome); return; }
		pecas.Add(new PecaDeRoupa(caminho, cor));
	}

	/// <summary>
	/// O TETO DO GUARDA-ROUPA vale pro NPC tambem -- `Appearance.MaxRoupa`, que e o mesmo 4 do
	/// jogador e o mesmo que o `Protocol.PutAppearance` escreve no fio. O pior caso da tabela e
	/// exatamente 4 (o kit de elite: armadura + luvas + botas + capa), entao cortar aqui nao tira
	/// nada hoje; ela existe pra o dia em que alguem acrescentar a quinta linha no `npcs.json` --
	/// senao o `Sanear` cortaria calado e o dono nao saberia por que a capa sumiu.
	/// </summary>
	private static List<PecaDeRoupa> Aparar(List<PecaDeRoupa> pecas) =>
		pecas.Count <= Appearance.Appearance.MaxRoupa
			? pecas
			: pecas.GetRange(0, Appearance.Appearance.MaxRoupa);

	/// <summary>
	/// O `prob(N)` do BYOND, deterministico. UM GERADOR POR CAMPO -- ver
	/// <see cref="SorteioDeNpc.Sorteador"/>: sem isso, a jaqueta e o cachecol do Namekuseijin sairiam
	/// juntos pra sempre (o mesmo gerador, a mesma primeira tirada), e todo Namekuseijin de jaqueta
	/// teria cachecol.
	/// </summary>
	private static bool Prob(ulong semente, string campo, int pct) =>
		SorteioDeNpc.Sorteador(semente, campo).Next(100) < pct;

	/// <summary>O `pick(...)` do BYOND, deterministico e por campo.</summary>
	private static string Escolher(ulong semente, string campo, string[] pool) =>
		pool[SorteioDeNpc.Sorteador(semente, campo).Next(pool.Length)];
}
