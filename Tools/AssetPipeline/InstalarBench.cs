using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// ============================ A BANCADA DO CATALOGO E DO LUGAR (`instalar-prova`) ============================
/// A metade SEM JOGO do pedido do dono. Ela responde duas perguntas que nao precisam de rede, de
/// zona nem de login -- e que, se estiverem erradas, fazem TODAS as familias da `--diaginstalar`
/// reprovarem em cascata sem apontar a causa:
///
///   A) **QUEM GANHA "INSTALAR NO CHAO"** (regra 2). Le o `construcoes.json` de VERDADE com o mesmo
///      parser do jogo, liga-o no `CatalogoDeItens` como o `Boot` liga, e entao pergunta -- item por
///      item -- a MESMA lista de acoes que o menu do inventario percorre pra montar os botoes.
///
///   B) **ONDE DA PRA ASSENTAR** (regra 4). Monta um mapa de mentira com parede, agua e beirada e
///      cobra do <see cref="Assentamento.DoLugar"/> as cinco recusas. E a funcao que o servidor e o
///      fantasma chamam JUNTOS, entao um erro aqui aparece nos dois lados de uma vez.
///
/// ============================ POR QUE ELA NAO E "SO CONTAR" ============================
/// Contar quantos itens sao instalaveis fica verde com a regra invertida (os numeros so trocam de
/// lado). Entao toda familia aqui afirma os DOIS sentidos, e as afirmacoes por NOME sao os itens que
/// o dono citou na queixa -- Armadura, Scouter, Pesos -- mais os que estavam errados junto com eles.
///
/// E no fim vem a INJECAO: as mesmas regras, com o catalogo deliberadamente estragado, obrigadas a
/// ficar vermelhas. Uma regra que nao reprova o defeito que existe pra pegar e uma linha verde que
/// nao significa nada.
/// ====================================================================================
/// </summary>
public static class InstalarBench
{
	private static int _ok, _falhou;
	private static readonly List<string> Vermelhas = [];

	private static void Checa(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; Console.WriteLine($"  ok    {nome}"); }
		else { _falhou++; Vermelhas.Add(nome); Console.WriteLine($"  FALHA {nome}   {detalhe}"); }
	}

	// =====================================================================
	// AS REGRAS -- funcoes, pra a injecao chamar as MESMAS
	// =====================================================================
	/// <summary>
	/// ESTE ID OFERECE INSTALAR? Pela lista de acoes -- o mesmo array que `TelaDeInventario` percorre
	/// pra desenhar os botoes, e que `GameServer.Posicionar` consulta antes de plantar qualquer coisa.
	/// A bancada nao tem copia nenhuma da regra.
	/// </summary>
	private static bool OfereceInstalar(string id) =>
		CatalogoDeItens.Get(id) is { } d
		&& Array.IndexOf(d.AcoesDoItem, CatalogoDeItens.AcaoPosicionar) >= 0;

	/// <summary>
	/// AS DUAS PERGUNTAS SAO A MESMA PERGUNTA.
	///
	/// `PodeAssentarNoChao` e o que o servidor chama; `AcoesDoItem` e o que a tela desenha. A versao
	/// antiga disto tinha DUAS verdades (`EhConstrucao` dizia uma coisa e o ramo do `Get` decidia
	/// outra), e a que tinha nome era a que ninguem consultava. Esta linha cobra que nao volte.
	/// </summary>
	private static bool AsDuasConcordam(string id) =>
		CatalogoDeItens.PodeAssentarNoChao(id) == OfereceInstalar(id);

	/// <summary>
	/// ============================ O GABARITO: AS 97 COMPRAVEIS, UMA POR LINHA ============================
	/// A fase 1 afirmava 48 nomes escolhidos a dedo e CONTAVA o resto. Contar fica verde com a regra
	/// invertida em um item so -- e este projeto ja pagou por amostra: dez racas nasciam no planeta
	/// errado com a bancada inteira verde, porque a prova olhava tres racas.
	///
	/// AQUI ESTA O CATALOGO INTEIRO. Cada linha e uma afirmacao com nome proprio, e o comentario diz
	/// DE ONDE saiu a classificacao -- que e a unica coisa que torna o gabarito auditavel em vez de
	/// uma foto do que o codigo faz hoje:
	///
	///   `Bolt`    o verb `Bolt()` sem argumento: aparafusar so faz sentido em coisa assentada;
	///   `oview`   verb com `set src in oview(1)` -- `oview` EXCLUI o proprio, entao so alcanca no chao;
	///   `usr`     verb com `set src in usr` -- so alcanca de dentro da mochila;
	///   `palpite` o item nao tem verb nenhum e herdou o lugar dele na arvore do DM. **Nao e prova**,
	///             e por isso esta escrito: 29 das 103 entradas do extrator cairam aqui (21 dentro de
	///             `/obj/items` -> pessoal, 8 fora -> chao). Rode `AssetPipeline motivos <Code>` pra ver
	///             a lista atual;
	///   `tabela a mao` o id esta no `CatalogoDeItens.Tudo` e a ficha escrita a mao VENCE o catalogo de
	///             construcoes -- a classificacao do DM vai entre parenteses so pra conferir que as duas
	///             concordam.
	/// ==================================================================================================
	/// </summary>
	private static readonly (string Id, bool Chao)[] Gabarito =
	[
		("Android_Creation_Mainframe", true ),   // Bolt
		("Artificial_Moon",            true ),   // oview
		("Base_Camp",                  true ),   // oview
		("Bio_Field",                  true ),   // Bolt
		("Boat",                       true ),   // palpite: fora de /obj/items
		("Boat_Placed",                true ),   // palpite: fora de /obj/items
		("Capital_Ship",               true ),   // palpite: fora de /obj/items
		("Central_Computer",           true ),   // palpite: fora de /obj/items
		("Clone_Machine",              true ),   // oview
		("Crafting_Bench",             true ),   // Bolt
		("Crate",                      true ),   // palpite: fora de /obj/items
		("Emitter",                    true ),   // oview
		("Fridge",                     true ),   // Bolt
		("Gravity",                    true ),   // Bolt
		("Ingredient_Bag",             true ),   // oview
		("Ingredient_Crystal",         true ),   // oview
		("Magic_Sifter",               true ),   // oview
		("Medical_Scanner",            true ),   // Bolt
		("Personal_Spacepod",          true ),   // oview
		("Player_Fortress",            true ),   // oview
		("Power_Drill",                true ),   // palpite: fora de /obj/items
		("Punching_Bag",               true ),   // Bolt
		("Punching_Machine",           true ),   // Bolt
		("Recharge_Station",           true ),   // Bolt
		("Regenerator",                true ),   // Bolt
		("Research_Station",           true ),   // palpite: fora de /obj/items
		("Rocket_Ship",                true ),   // oview
		("Simulator",                  true ),   // Bolt
		("Spacepod",                   true ),   // oview
		("Spacepod_Placed",            true ),   // oview
		("Super_Computer",             true ),   // oview
		("Telepad",                    true ),   // Bolt
		("Virus_Synth",                true ),   // Bolt
		("Wall_Repair",                true ),   // Bolt
		("Wall_Upgrader",              true ),   // Bolt
		("Ammo",                       false),   // palpite: /obj/items
		("Armor",                      false),   // usr
		("Axe",                        false),   // usr
		("Bandages",                   false),   // tabela a mao (usr)
		("Blaster",                    false),   // usr
		("Blood_Bag",                  false),   // palpite: /obj/items
		("Bolter",                     false),   // usr
		("Books",                      false),   // palpite: /obj/items
		("Camera",                     false),   // palpite: /obj/items
		("Camera_Computer",            false),   // palpite: /obj/items
		("Chalk",                      false),   // usr
		("Club",                       false),   // usr
		("Communicator",               false),   // palpite: /obj/items
		("Cross",                      false),   // usr
		("DNA_Container",              false),   // palpite: /obj/items
		("Destructor",                 false),   // palpite: /obj/items
		("Detonator",                  false),   // usr
		("Doll_Core",                  false),   // usr
		("Dragon_Radar",               false),   // tabela a mao (palpite: /obj/items)
		("Dungeon_Needle",             false),   // palpite: /obj/items
		("Energy_Drain_Boots",         false),   // usr
		("Energy_Drain_Gloves",        false),   // usr
		("Fabricator",                 false),   // usr
		("First_Aid_Kit",              false),   // tabela a mao (usr)
		("Fishing_Pole",               false),   // usr
		("Forcefield",                 false),   // usr
		("G_Blaster",                  false),   // usr
		("Hammer",                     false),   // usr
		("Hand_Drill",                 false),   // tabela a mao (usr)
		("Handgun",                    false),   // usr
		("Intercepter_Core",           false),   // usr
		("Key",                        false),   // palpite: /obj/items
		("Mechanical_Kit",             false),   // palpite: /obj/items
		("Nav_System",                 false),   // tabela a mao (usr)
		("Nuke",                       false),   // palpite: /obj/items
		("Nutrient_Pill",              false),   // usr
		("Omniwatch",                  false),   // usr
		("PDA",                        false),   // palpite: /obj/items
		("Pet_Food",                   false),   // palpite: /obj/items
		("Pkball",                     false),   // usr
		("Portable_Repairer",          false),   // palpite: /obj/items
		("Predictor",                  false),   // usr
		("Rebreather",                 false),   // tabela a mao (usr)
		("Recreator",                  false),   // palpite: /obj/items
		("Repair_Kit",                 false),   // palpite: /obj/items
		("Rifle",                      false),   // usr
		("Rocket_Launcher",            false),   // usr
		("SMG",                        false),   // usr
		("Saibaman_Capsule",           false),   // usr
		("Scouter",                    false),   // tabela a mao (usr)
		("Sealing_Jar",                false),   // usr
		("Shotgun",                    false),   // usr
		("Shovel",                     false),   // tabela a mao (usr)
		("Smoke_Bomb",                 false),   // usr
		("Spacesuit",                  false),   // tabela a mao (usr)
		("Spear",                      false),   // usr
		("Staff",                      false),   // usr
		("Stungun",                    false),   // usr
		("Teleport_Nullifier",         false),   // palpite: /obj/items
		("Toxic_Waste",                false),   // palpite: /obj/items
		("Wand",                       false),   // usr
		("Weights",                    false),   // tabela a mao (usr)
	];

	/// <summary>
	/// AS 11 QUE NINGUEM COMPRA -- mobilia que ja esta nos `.dmm` (custo negativo, ver
	/// <see cref="Construcao.Construivel"/>). A classificacao delas nao governa nada, porque elas nunca
	/// chegam a uma mochila; a bancada as afirma mesmo assim, com a segunda metade junto: que o servidor
	/// nao as vende. Sem essa segunda metade, "e mobilia" seria uma desculpa e nao um fato.
	/// </summary>
	private static readonly (string Id, bool Chao)[] MobiliaDeMapa =
	[
		("AppleTree",         false),   // usr
		("Bank",              true ),   // oview
		("Cooked_Meat",       false),   // usr
		("Grave_1",           true ),   // --
		("Grave_2",           true ),   // --
		("Grave_3",           true ),   // --
		("Grave_4",           true ),   // --
		("Grave_5",           true ),   // --
		("Ship_Control",      true ),   // --
		("Ship_Pad",          true ),   // --
		("Time_Chamber_Door", true ),   // palpite: fora de /obj/items
	];

	public static int Run(string pastaAssets)
	{
		Console.WriteLine("=== INSTALAR: catalogo e lugar ===\n");

		string arq = Path.Combine(pastaAssets, "Data", "construcoes.json");
		if (!File.Exists(arq)) { Console.WriteLine($"nao achei {arq}"); return 1; }

		// O MESMO CAMINHO DO JOGO: parser de producao, ligado no catalogo de itens como o `Boot` liga.
		CatalogoDeObras cat = CatalogoDeObras.Parse(File.ReadAllText(arq));
		CatalogoDeItens.Obras = cat;

		Catalogo(cat);
		CatalogoInteiro(cat);
		Lugar();

		Console.WriteLine("\n--- injecao: as regras sabem reprovar? ---");
		Injecao(cat);

		Console.WriteLine($"\n===== {_ok} OK, {_falhou} FALHA(S) =====");
		if (_falhou > 0) Console.WriteLine("vermelhas: " + string.Join(" | ", Vermelhas));
		return _falhou > 0 ? 1 : 0;
	}

	// =====================================================================
	// A) O CATALOGO
	// =====================================================================
	private static void Catalogo(CatalogoDeObras cat)
	{
		Console.WriteLine("--- A: quem ganha \"Instalar no chão\" (regra 2) ---");

		var instalaveis = new List<string>();
		var pessoais = new List<string>();
		int discordam = 0;
		foreach (Construcao c in cat.Todas)
		{
			if (OfereceInstalar(c.Id)) instalaveis.Add(c.Id); else pessoais.Add(c.Id);
			if (!AsDuasConcordam(c.Id)) discordam++;
		}

		Console.WriteLine($"        {instalaveis.Count} instalaveis | {pessoais.Count} de uso pessoal "
						  + $"| {cat.Total} no catalogo");

		// OS DOIS GRUPOS PRECISAM EXISTIR, e nao "quase todos de um lado": uma regra que respondesse
		// sempre a mesma coisa passaria por metade das provas por nome logo abaixo.
		Checa("A.1 ha construcoes instalaveis", instalaveis.Count >= 20, $"{instalaveis.Count}");
		Checa("A.2 ha itens de uso pessoal", pessoais.Count >= 20, $"{pessoais.Count}");
		Checa("A.3 servidor e tela fazem a MESMA pergunta", discordam == 0,
			  $"{discordam} id(s) em que `PodeAssentarNoChao` e `AcoesDoItem` discordam");

		// ---- os NOMES: o lado de fora ----
		// Os tres primeiros sao os que o dono citou na queixa. Os outros sao os que estavam errados
		// junto com eles: as nove armas de fogo, o Nav System, as roupas e as luvas.
		foreach (string id in new[]
		{
			"Armor", "Scouter", "Weights",
			"Handgun", "SMG", "Rifle", "Rocket_Launcher", "Shotgun", "Blaster", "Bolter",
			"G_Blaster", "Nuke", "Nav_System", "Spacesuit", "Rebreather",
			"Energy_Drain_Gloves", "Energy_Drain_Boots", "Smoke_Bomb", "Sealing_Jar",
			"Shovel", "Hand_Drill", "Bandages", "First_Aid_Kit", "Dragon_Radar",
			"Axe", "Hammer", "Spear", "Club",
		})
			Checa($"A.4 {id} NAO oferece instalar", !OfereceInstalar(id),
				  Acoes(id));

		// ---- os NOMES: o lado de dentro ----
		foreach (string id in new[]
		{
			"Gravity", "Research_Station", "Punching_Bag", "Punching_Machine", "Telepad",
			"Fridge", "Clone_Machine", "Regenerator", "Simulator", "Android_Creation_Mainframe",
			"Bio_Field", "Wall_Repair", "Wall_Upgrader", "Crafting_Bench", "Recharge_Station",
			"Boat", "Base_Camp", "Player_Fortress", "Super_Computer", "Emitter",
		})
			Checa($"A.5 {id} oferece instalar", OfereceInstalar(id), Acoes(id));

		// ---- e o que NAO esta no catalogo nao vira item ----
		Checa("A.6 id inexistente nao vira item", CatalogoDeItens.Get("Nao_Existe_Isso") == null);
		Checa("A.7 e ele tambem nao pode ser assentado",
			  !CatalogoDeItens.PodeAssentarNoChao("Nao_Existe_Isso"));

		// ---- a maca continua sendo maca ----
		// A tabela escrita a mao vence o ramo de construcao, e isso nao pode ter sido quebrado pela
		// mudanca: a maca se come, e o scouter se equipa.
		Checa("A.8 a maca continua com 'comer'",
			  CatalogoDeItens.Get(CatalogoDeItens.Maca)?.AcoesDoItem.Contains("comer") == true,
			  Acoes(CatalogoDeItens.Maca));
		Checa("A.9 o scouter continua com 'equipar'",
			  CatalogoDeItens.Get(CatalogoDeItens.Scouter)?.AcoesDoItem.Contains("equipar") == true,
			  Acoes(CatalogoDeItens.Scouter));
	}

	private static string Acoes(string id) =>
		"acoes: [" + string.Join(", ", CatalogoDeItens.Get(id)?.AcoesDoItem ?? []) + "]";

	// =====================================================================
	// A2) O CATALOGO INTEIRO, ITEM POR ITEM
	// =====================================================================
	/// <summary>
	/// ============================ A VARREDURA NOMINAL, E POR QUE ELA SUBSTITUI A CONTAGEM ============================
	/// A familia A afirma 48 nomes e conta o resto (`{n} instalaveis | {m} de uso pessoal`). Duas coisas
	/// que a contagem NAO pega, e as duas ja aconteceram neste projeto:
	///
	///   * **um item so trocando de lado.** Os totais mudam de 44/64 pra 43/65 e nenhuma linha fica
	///     vermelha -- ninguem le totais como afirmacao. Foi assim que dez racas passaram a nascer no
	///     planeta errado com a bancada verde: a prova olhava tres racas das treze.
	///   * **um item NOVO entrando sem classificacao.** O `construcoes.json` cresce quando alguem anota
	///     mais uma maquina; sem <see cref="Gabarito"/>, ela entra com o lado que o extrator chutou e
	///     ninguem e obrigado a olhar.
	///
	/// Por isso a varredura cobra as DUAS pontas do casamento: todo id do arquivo tem que estar no
	/// gabarito (senao ha item sem dono) **e** todo id do gabarito tem que estar no arquivo (senao o
	/// gabarito envelheceu e passou a afirmar coisas sobre item que nao existe mais).
	/// ============================================================================================================
	/// </summary>
	private static void CatalogoInteiro(CatalogoDeObras cat)
	{
		Console.WriteLine("\n--- A2: o catalogo INTEIRO, item por item (regra 2, nominalmente) ---");

		var doGabarito = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach ((string id, bool chao) in Gabarito)
		{
			doGabarito.Add(id);
			Construcao? c = cat.Get(id);
			if (c == null) { Checa($"A2 {id} existe no catalogo", false, "o gabarito fala de um id que sumiu do construcoes.json"); continue; }

			Checa($"A2 {id} {(chao ? "OFERECE" : "nao oferece")} instalar",
				  OfereceInstalar(id) == chao, Acoes(id));
		}

		foreach ((string id, bool chao) in MobiliaDeMapa)
		{
			doGabarito.Add(id);
			Construcao? c = cat.Get(id);
			if (c == null) { Checa($"A2 {id} existe no catalogo", false, "gabarito de mobilia com id que sumiu"); continue; }

			Checa($"A2 {id} {(chao ? "OFERECE" : "nao oferece")} instalar (mobilia)",
				  OfereceInstalar(id) == chao, Acoes(id));

			// A SEGUNDA METADE: mobilia nao se compra. Sem esta linha, "a classificacao dela nao
			// importa porque ninguem a tem na mochila" seria uma afirmacao que ninguem conferiu -- e
			// bastaria um custo positivo escapar pro arquivo pra o Portao da Sala do Tempo virar item.
			Checa($"A2 {id} nao esta a venda", !c.Construivel, $"custo {c.Custo}");
		}

		// ---- e ninguem fica de fora ----
		var semDono = cat.Todas.Select(c => c.Id).Where(id => !doGabarito.Contains(id)).ToList();
		Checa($"A2.fim todo id do catalogo tem uma linha nominal ({cat.Total} ids)", semDono.Count == 0,
			  "sem afirmacao propria: " + string.Join(", ", semDono));
		Checa($"A2.fim o gabarito cobre {Gabarito.Length} compraveis + {MobiliaDeMapa.Length} de mobilia",
			  Gabarito.Length + MobiliaDeMapa.Length == cat.Total,
			  $"{Gabarito.Length + MobiliaDeMapa.Length} no gabarito, {cat.Total} no catalogo");

		// E O GABARITO NAO PODE SER DE UM LADO SO -- uma lista que dissesse "pessoal" pra tudo
		// passaria por metade das linhas acima com a regra invertida.
		int chaoNoGabarito = Gabarito.Count(g => g.Chao);
		Checa("A2.fim o gabarito tem os dois lados",
			  chaoNoGabarito >= 20 && Gabarito.Length - chaoNoGabarito >= 20,
			  $"{chaoNoGabarito} de chao, {Gabarito.Length - chaoNoGabarito} pessoais");
	}

	// =====================================================================
	// B) O LUGAR
	// =====================================================================
	/// <summary>
	/// UM MAPA DE MENTIRA, mas com as tres classes de celula que o jogo tem: chao, parede e agua.
	/// 20x20 tiles; a coluna 10 e parede e a coluna 12 e agua.
	/// </summary>
	private static ZoneCollision MapaDeProva()
	{
		const int w = 20, h = 20;
		var paredes = new byte[(w * h + 7) / 8];
		var aguas = new byte[(w * h + 7) / 8];
		for (int y = 0; y < h; y++)
		{
			Marca(paredes, y * w + 10);
			Marca(aguas, y * w + 12);
		}
		ZoneCollision m = ZoneCollision.Montar(w, h, paredes);
		m.DefinirAgua(aguas);
		return m;

		static void Marca(byte[] bits, int i) => bits[i >> 3] |= (byte)(1 << (i & 7));
	}

	private static void Lugar()
	{
		Console.WriteLine("\n--- B: onde da pra assentar (regra 4, a funcao das DUAS pontas) ---");

		ZoneCollision m = MapaDeProva();
		const int t = ZoneCollision.TileSize;

		// De pe no meio da celula (5,5). O alcance do Core sao 96 px = 3 tiles.
		var eu = new Vec2(5 * t + t / 2f, 5 * t + t / 2f);

		Checa("B.1 chao livre ao lado: pode",
			  Assentamento.DoLugar(m, eu, 6, 5) == RecusaDeAssento.Pode);
		Checa("B.2 tres tiles: ainda pode (o alcance e do Core)",
			  Assentamento.DoLugar(m, eu, 8, 5) == RecusaDeAssento.Pode);
		Checa("B.3 quatro tiles: longe demais",
			  Assentamento.DoLugar(m, eu, 9, 5) == RecusaDeAssento.LongeDemais,
			  $"{Assentamento.DoLugar(m, eu, 9, 5)}");

		// A parede e a agua ficam a 5 e 7 tiles do meu ponto -- fora do alcance. Ando pra perto
		// delas, que e o que o jogador faria.
		var pertoDaParede = new Vec2(9 * t + t / 2f, 5 * t + t / 2f);
		Checa("B.4 dentro de parede: recusa, e com a palavra 'parede'",
			  Assentamento.DoLugar(m, pertoDaParede, 10, 5) == RecusaDeAssento.DentroDeParede);

		var pertoDagua = new Vec2(13 * t + t / 2f, 5 * t + t / 2f);
		Checa("B.5 em cima d'agua: recusa, e NAO como parede",
			  Assentamento.DoLugar(m, pertoDagua, 12, 5) == RecusaDeAssento.EmCimaDagua,
			  $"{Assentamento.DoLugar(m, pertoDagua, 12, 5)}");

		// A BEIRADA: `MargemDaBorda = 2`, entao a celula 1 e beirada e a 2 nao.
		var pertoDaBorda = new Vec2(3 * t + t / 2f, 5 * t + t / 2f);
		Checa("B.6 beirada do mapa: recusa com a palavra dela",
			  Assentamento.DoLugar(m, pertoDaBorda, 1, 5) == RecusaDeAssento.BeiradaDoMapa,
			  $"{Assentamento.DoLugar(m, pertoDaBorda, 1, 5)}");
		Checa("B.7 e um tile pra dentro ja pode",
			  Assentamento.DoLugar(m, pertoDaBorda, 2, 5) == RecusaDeAssento.Pode);

		// SEM MAPA e "nao sei", e nao "nao pode": o fantasma nao pode ficar vermelho no mundo inteiro
		// no primeiro quadro depois de trocar de zona.
		Checa("B.8 sem mapa, perto: pode (e nao 'nao sei' virando 'nao')",
			  Assentamento.DoLugar(null, eu, 6, 5) == RecusaDeAssento.Pode);
		Checa("B.9 sem mapa, longe: ainda recusa por distancia",
			  Assentamento.DoLugar(null, eu, 60, 5) == RecusaDeAssento.LongeDemais);

		// A FOLGA ENTRE OBRAS -- o numero mora no Core porque as duas pontas o usam.
		Checa("B.10 coisa colada ocupa o ponto",
			  Assentamento.TemCoisaEm([new Vec2(100, 100)], new Vec2(100 + Assentamento.FolgaEntreObras - 1, 100)));
		Checa("B.11 coisa a uma folga inteira ja nao ocupa",
			  !Assentamento.TemCoisaEm([new Vec2(100, 100)], new Vec2(100 + Assentamento.FolgaEntreObras, 100)));
		Checa("B.12 lista vazia nunca ocupa", !Assentamento.TemCoisaEm([], new Vec2(0, 0)));

		// AS FRASES: cada recusa tem a SUA, e nenhuma repete a de outra. Era o defeito medido -- a
		// agua saia com a frase da parede.
		var frases = new List<string>();
		foreach (RecusaDeAssento r in Enum.GetValues<RecusaDeAssento>())
		{
			if (r == RecusaDeAssento.Pode) continue;
			frases.Add(Assentamento.Motivo(r, "Coisa"));
		}
		Checa("B.13 cada recusa tem frase propria", frases.Distinct().Count() == frases.Count,
			  string.Join(" | ", frases));
		Checa("B.14 a frase da agua fala de agua",
			  Assentamento.Motivo(RecusaDeAssento.EmCimaDagua, "X").Contains("água", StringComparison.Ordinal));
		Checa("B.15 a frase do item pessoal diz o NOME dele",
			  Assentamento.Motivo(RecusaDeAssento.NaoEDoChao, "Armadura")
						  .Contains("Armadura", StringComparison.Ordinal));
	}

	// =====================================================================
	// A INJECAO
	// =====================================================================
	private static void Injecao(CatalogoDeObras bom)
	{
		// ESTRAGA O CATALOGO DE VERDADE: liga o `Pessoal` na maquina de gravidade e desliga na
		// armadura, e exige que as duas regras notem. Nao e uma amostra inventada -- e o objeto do
		// catalogo, com o campo trocado, exatamente o defeito que a extracao pode cometer.
		Construcao? grav = bom.Get("Gravity");
		Construcao? armadura = bom.Get("Armor");
		if (grav == null || armadura == null) { Checa("I.0 achei Gravity e Armor no catalogo", false); return; }

		bool gravAntes = grav.Pessoal, armaduraAntes = armadura.Pessoal;

		grav.Pessoal = true;
		Checa("I.1 a regra pega maquina de gravidade marcada como pessoal", !OfereceInstalar("Gravity"));
		grav.Pessoal = gravAntes;
		Checa("I.2 ...e volta a aceitar quando o campo volta", OfereceInstalar("Gravity"));

		armadura.Pessoal = false;
		Checa("I.3 a regra pega armadura marcada como de chao", OfereceInstalar("Armor"));
		armadura.Pessoal = armaduraAntes;
		Checa("I.4 ...e volta a recusar quando o campo volta", !OfereceInstalar("Armor"));

		// E A LIGACAO PODE ESTAR SOLTA: sem catalogo de construcoes ligado, nenhuma construcao vira
		// item -- e uma bancada que nao notasse isso ficaria verde num jogo sem bancada de pesquisa.
		CatalogoDeObras? guardado = CatalogoDeItens.Obras;
		CatalogoDeItens.Obras = null;
		Checa("I.5 sem catalogo ligado, construcao nao vira item",
			  CatalogoDeItens.Get("Gravity") == null);
		Checa("I.6 ...mas a tabela escrita a mao continua de pe",
			  CatalogoDeItens.Get(CatalogoDeItens.Maca) != null);
		CatalogoDeItens.Obras = guardado;
		Checa("I.7 e religar devolve tudo", CatalogoDeItens.Get("Gravity") != null);
	}
}
