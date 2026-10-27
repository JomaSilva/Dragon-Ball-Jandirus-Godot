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
/// E UMA TERCEIRA, que nao e do pedido do instalar mas e sobre o mesmo arquivo que ela ja abre:
///
///   A3) **ONDE A MACIEIRA E DESENHADA.** O `px` dela no catalogo contra a conta que o `New()` do DM
///       faz em runtime e contra o que o extrator calcula -- ver <see cref="Macieira"/>.
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
	/// ============================ O GABARITO: AS 89 COMPRAVEIS, UMA POR LINHA ============================
	/// (Eram 97. Em 2026-10-10 sairam do catalogo as tres que pediam tecnologia acima do teto do DM e por isso
	/// nunca estiveram a venda la: `Boat_Placed` e `Spacepod_Placed`, de tech 999, e o `Central_Computer`, de
	/// tech 200 e comentado no original. Ver a familia A0 e `Construcao.AcimaDoTeto`. E, no mesmo dia, as cinco
	/// armas que so existiam dentro de um `/* */` do original -- `Axe`, `Club`, `Cross`, `Hammer` e `Spear`. A
	/// sexta, o `Staff`, dividia o id com o cajado magico de `Magitech.dm`, que e codigo vivo: a linha dele
	/// FICA, e agora fala do cajado. Ver a familia A0c.)
	///
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
	///             e por isso esta escrito: 25 das 94 entradas do extrator caem aqui (19 dentro de
	///             `/obj/items` -> pessoal, 6 fora -> chao). Rode `AssetPipeline motivos <Code>` pra ver
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
		("Camera",                     true ),   // ficha do port (`TechDeBolso`): a descricao manda *"Place it down, bolt it"*; os verbs dela sao `view(1)`, que nao votam (o extrator: palpite /obj/items)
		("Capital_Ship",               true ),   // palpite: fora de /obj/items
		("Clone_Machine",              true ),   // oview
		("Communicator",               true ),   // Bolt (`Communicator.dm:79`) -- que o extrator perde: a string de varias linhas do `messagelist` engole o resto do tipo. Assenta pela ficha do port (`TechDeBolso`)
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
		("Super_Computer",             true ),   // oview
		("Telepad",                    true ),   // Bolt
		("Wall_Repair",                true ),   // Bolt
		("Wall_Upgrader",              true ),   // Bolt
		("Ammo",                       false),   // palpite: /obj/items
		("Armor",                      false),   // usr
		("Bandages",                   false),   // tabela a mao (usr)
		("Blaster",                    false),   // usr
		("Blood_Bag",                  false),   // palpite: /obj/items
		("Bolter",                     false),   // usr
		("Books",                      false),   // palpite: /obj/items
		("Camera_Computer",            false),   // palpite: /obj/items
		("Chalk",                      false),   // usr
		("DNA_Container",              false),   // usr (o `Extract_DNA` de `DNALabs.dm`; era palpite enquanto o extrator lia o tipo errado)
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
		("Hand_Drill",                 false),   // tabela a mao (usr)
		("Handgun",                    false),   // usr
		("Intercepter_Core",           false),   // usr
		("Key",                        false),   // palpite: /obj/items
		("Mechanical_Kit",             false),   // palpite: /obj/items
		("Android_Upgrade",            false),   // so do port (2026-10-26, o pedido 95c): um item da mochila, que o androide usa em si
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
		("Staff",                      false),   // usr (o cajado magico de `Magitech.dm`: `Mana_Control`)
		("Stungun",                    false),   // usr
		("Teleport_Nullifier",         false),   // palpite: /obj/items
		("Toxic_Waste",                false),   // palpite: /obj/items
		("Wand",                       false),   // usr
		("Weights",                    false),   // tabela a mao (usr)
	];

	/// <summary>
	/// AS 13 QUE NINGUEM COMPRA -- mobilia que ja esta nos `.dmm` (custo negativo, ver
	/// <see cref="Construcao.Construivel"/>). A classificacao delas nao governa nada, porque elas nunca
	/// chegam a uma mochila; a bancada as afirma mesmo assim, com a segunda metade junto: que o servidor
	/// nao as vende. Sem essa segunda metade, "e mobilia" seria uma desculpa e nao um fato.
	///
	/// (O `Sacred_Water` entrou no `construcoes.json` a mao em 2026-10-10, com o jarro do Mirante, e ficou
	/// sem linha aqui: as duas cobrancas de fecho da A2 estavam vermelhas por ele.)
	///
	/// (O `Egg` repetiu a historia: entrou no extrator e no `construcoes.json` em 2026-10-17, com o ovo das racas
	/// que poem ovo (`Filhos.TipoDoOvo`), e a A2 ficou vermelha por ele ate 2026-10-19. Ele nao vem de mapa -- o
	/// servidor o poe no chao (`PorOOvoNoChao`, o `Lay_Egg` do DM) --, mas e mobilia no sentido que importa aqui:
	/// custo -1 e nenhum verbo que o leve pra uma mochila.)
	/// </summary>
	private static readonly (string Id, bool Chao)[] MobiliaDeMapa =
	[
		("AppleTree",         false),   // usr
		("Bank",              true ),   // oview
		("Cooked_Meat",       false),   // usr
		("Egg",               true ),   // -- (o `mob/Egg` de `NPClist.dm:15-25`: um mob no chao, sem verbo; o extrator o escreve a mao)
		("Grave_1",           true ),   // --
		("Grave_2",           true ),   // --
		("Grave_3",           true ),   // --
		("Grave_4",           true ),   // --
		("Grave_5",           true ),   // --
		("Sacred_Water",      true ),   // oview (`EarthRanks.dm:181-231`: o jarro se usa de perto, no chao)
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

		NadaAcimaDoTeto(cat);
		AsFerramentasNaoCavam(cat);
		SoOQueOOriginalCompila(cat);
		Catalogo(cat);
		CatalogoInteiro(cat);
		Macieira(cat, pastaAssets);
		Lugar();

		Console.WriteLine("\n--- injecao: as regras sabem reprovar? ---");
		Injecao(cat);

		Console.WriteLine($"\n===== {_ok} OK, {_falhou} FALHA(S) =====");
		if (_falhou > 0) Console.WriteLine("vermelhas: " + string.Join(" | ", Vermelhas));
		return _falhou > 0 ? 1 : 0;
	}

	// =====================================================================
	// A0) NADA A VENDA ACIMA DO TETO DE TECNOLOGIA -- o "Space Pod de tech 999"
	// =====================================================================
	/// <summary>
	/// ============================ O QUE O DONO VIU, E DE ONDE VINHA ============================
	/// *"remover space pod de tech 999 q aparece na aba tech"* (2026-10-10). O `Spacepod_Placed` e o `Boat_Placed`
	/// do DM sao balcoes postos no mapa, com `neededtech=999` justamente pra NUNCA entrarem na janela de
	/// tecnologia (ela apaga o que o jogador nao alcanca, e o teto de la e 100). O extrator os emitia como
	/// qualquer `Creatable`, e a aba do port -- que lista tudo com o motivo de cada nao -- os mostrava.
	///
	/// TRES PONTAS, e as tres tem de concordar:
	///   * o ARQUIVO publicado nao tem nenhuma compravel acima do teto (e os tres ids sumiram dele);
	///   * o JOGO nao oferece uma dessas mesmo que o arquivo a traga (`CatalogoDeObras.Ofertas` / `Permitida`);
	///   * o EXTRATOR nao a emite -- medido numa arvore DM de mentira com o defeito exato dentro (um
	///     `X_Placed` de tech 999 ao lado do `X` de verdade), e com o defeito religado (o extrator de antes).
	/// ==========================================================================================
	/// </summary>
	private static void NadaAcimaDoTeto(CatalogoDeObras cat)
	{
		Console.WriteLine("--- A0: nada a venda acima do teto de tecnologia do DM (o \"Space Pod de tech 999\") ---");

		// ---- o arquivo
		List<string> acima = [.. cat.AcimaDoTeto().Select(c => $"{c.Id} (tech {c.Tech:0})")];
		Checa($"A0.1 nenhuma compravel do construcoes.json pede tecnologia acima de {CatalogoDeObras.TechMaximoDoDm:0}",
			  acima.Count == 0, string.Join(", ", acima));
		foreach (string id in new[] { "Spacepod_Placed", "Boat_Placed", "Central_Computer" })
			Checa($"A0.2 {id} saiu do catalogo", cat.Get(id) == null);
		foreach ((string id, double tech) in new[] { ("Spacepod", 40.0), ("Personal_Spacepod", 35.0), ("Boat", 20.0) })
			Checa($"A0.3 {id} (a de verdade, tech {tech:0}) continua a venda",
				  cat.Get(id) is { Construivel: true } c && c.Tech == tech, $"tech {cat.Get(id)?.Tech}");
		Checa("A0.4 a nave pousada e o barco do MAPA continuam achando a entrada deles (`/obj/Spacepod`, `/obj/Boat`)",
			  cat.PorTypepath("/obj/Spacepod")?.Id == "Personal_Spacepod" && cat.PorTypepath("/obj/Boat")?.Id == "Boat",
			  $"{cat.PorTypepath("/obj/Spacepod")?.Id}, {cat.PorTypepath("/obj/Boat")?.Id}");

		// ---- o jogo, com um arquivo que TRAGA a marca de volta
		const string comAMarca =
			"""
			[
			  { "id": "Coisa", "nome": "Coisa", "custo": 100, "tech": 40, "racas": [], "tipo": "/obj/Coisa" },
			  { "id": "Coisa_Placed", "nome": "Coisa Placed", "custo": 100, "tech": 999, "racas": [], "tipo": "/obj/Coisa" }
			]
			""";
		CatalogoDeObras sujo = CatalogoDeObras.Parse(comAMarca);
		var sabio = new Jandirus.Core.Stats.Fighter { techskill = 100, Zeni = 1_000_000 };
		List<string> Oferecidas() => [.. sujo.Ofertas(sabio, "Human").Select(o => o.Obra.Id)];

		Checa("A0.5 um catalogo que traga a marca: a de tech 999 NAO vira oferta, e a de verdade vira",
			  Oferecidas().SequenceEqual(["Coisa"]) && sujo.AcimaDoTeto().Select(c => c.Id).SequenceEqual(["Coisa_Placed"]),
			  string.Join(", ", Oferecidas()));
		Checa("A0.6 ...e pedir pra fabrica-la direto e recusado como coisa que nao existe (esconder nao e permissao)",
			  CatalogoDeObras.Permitida(sujo.Get("Coisa_Placed")!, sabio, "Human") == RecusaObra.NaoExiste);

		CatalogoDeObras.AcimaDoTetoViraOfertaDeTeste = true;
		try
		{
			Checa("(defeito injetado: o que pede tecnologia acima do teto volta a ser oferta) a de tech 999 reaparece "
				  + "na lista -- a cobranca A0.5 acusa", Oferecidas().Contains("Coisa_Placed"), string.Join(", ", Oferecidas()));
		}
		finally { CatalogoDeObras.AcimaDoTetoViraOfertaDeTeste = false; }

		// ---- o extrator, numa arvore DM de mentira
		string dm = Path.Combine(Path.GetTempPath(), "jandirus-techprova-" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(dm);
			// O MESMO FORMATO DO ORIGINAL (`Tier3.dm:9-29`): tabs, o `Creatable` de verdade e o balcao de mapa.
			File.WriteAllText(Path.Combine(dm, "Tier3.dm"), string.Join("\n", new[]
			{
				"obj/Creatables",
				"\tCoisa",
				"\t\ticon='Coisa.dmi'",
				"\t\tcost=100000",
				"\t\tneededtech=40 //Deletes itself from contents if the usr doesnt have the needed tech",
				"\t\tcreate_type = /obj/Coisa",
				"\tCoisa_Placed",
				"\t\ticon='Coisa.dmi'",
				"\t\tcost=100000",
				"\t\tneededtech=999 //Deletes itself from contents if the usr doesnt have the needed tech",
				"\t\tvar/count=0",
				"\t\tSaveItem=0",
				"\t\tcreate_type = /obj/Coisa",
				"\tCoisa_No_Teto",
				"\t\tcost=5000000",
				"\t\tneededtech=100",
				"\t\tcreate_type = /obj/Coisa_No_Teto",
				// (2026-10-26) o sintetizador de virus, como o DM o declara -- e que saiu do jogo (o pedido 98): a A0.7b
				"\tVirus_Synth",
				"\t\tcost=75000",
				"\t\tneededtech=22",
				"\t\tcreate_type = /obj/items/Virus_Synth",
				"",
			}));

			List<string> Extraidas() => [.. DmTechScanner.Scan(dm).Where(d => d.Custo >= 0).Select(d => d.Id).Order(StringComparer.Ordinal)];

			List<string> hoje = Extraidas();
			Checa("A0.7 o extrator NAO emite o `Coisa_Placed` de tech 999, e emite a `Coisa` e a que esta NO teto (tech 100)",
				  hoje.SequenceEqual(["Coisa", "Coisa_No_Teto"]), string.Join(", ", hoje));
			Checa("A0.7b ...e NAO emite o `Virus_Synth`: o sintetizador de virus saiu do jogo (o pedido 98 do dono, 2026-10-26), e uma "
				  + "regeneracao do `construcoes.json` nao o traz de volta", !hoje.Contains(Jandirus.Core.Tech.Sintetizador.Tipo), string.Join(", ", hoje));

			DmTechScanner.AcimaDoTetoEntraDeTeste = true;
			try
			{
				List<string> antes = Extraidas();
				Checa("(defeito injetado: o extrator de antes do conserto) o `Coisa_Placed` volta a sair -- a cobranca A0.7 acusa",
					  antes.Contains("Coisa_Placed") && !antes.SequenceEqual(hoje), string.Join(", ", antes));
			}
			finally { DmTechScanner.AcimaDoTetoEntraDeTeste = false; }
		}
		catch (Exception e) { Checa("A0.7 montei a arvore DM de mentira do extrator", false, e.Message); }
		finally
		{
			DmTechScanner.AcimaDoTetoEntraDeTeste = false;
			try { Directory.Delete(dm, recursive: true); } catch { /* temporario */ }
		}

		Console.WriteLine();
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
	// A0b) A PA E A FURADEIRA NAO CAVAM MAIS -- na mochila, so aceleram o Minerar
	// =====================================================================
	/// <summary>
	/// *"tire a opcao de cavar da pa e furadeira pq e redundante"* (dono, 2026-10-10). O "cavar" era a acao das
	/// duas na mochila (zeni e uma chance de XP de tecnologia por cavada, o `Dig` de `Tier 1.dm:254-331`); o verb
	/// Minerar tomou o lugar, e elas ficaram sem acao nenhuma -- o que fazem e render mais por estarem na mochila.
	///
	/// O QUE SE COBRA AQUI E O QUE O JOGADOR VE E LE:
	///   * nenhuma das duas declara acao (a tela da mochila desenha um botao por acao declarada, e o servidor so
	///     aceita a acao que o item declara -- os dois leem `ItemDef.AcoesDoItem`);
	///   * a frase delas e UMA, em tres lugares: o item (`CatalogoDeItens`, que e tambem a dica do card na aba
	///     Tech), o `construcoes.json` publicado e o extrator que o regeneraria. A do DM ("Allows you to dig")
	///     nao pode voltar por nenhum dos tres.
	/// A recusa do comando antigo pelo fio e a aceleracao do minerar sao da `--recursosteste`.
	/// </summary>
	private static void AsFerramentasNaoCavam(CatalogoDeObras cat)
	{
		Console.WriteLine("--- A0b: a pa e a furadeira nao cavam mais (na mochila, aceleram o verb Minerar) ---");
		string[] ferramentas = [CatalogoDeItens.Pa, CatalogoDeItens.Furadeira];

		// ---- sem acao
		List<string> ComAcao() => [.. ferramentas.Where(id => CatalogoDeItens.Get(id) is not { AcoesDoItem.Length: 0 }).Select(id => $"{id} {Acoes(id)}")];
		Checa("A0b.1 a pa e a furadeira nao declaram acao NENHUMA (nem 'cavar'): o painel delas na mochila so tem o 'jogar fora' de todo item",
			  ComAcao().Count == 0, string.Join(" | ", ComAcao()));
		Checa("A0b.2 ...e continuam no catalogo, fabricaveis e de uso pessoal (nao se assentam no chao)",
			  ferramentas.All(id => cat.Get(id) is { Construivel: true, Pessoal: true } && !CatalogoDeItens.PodeAssentarNoChao(id)),
			  string.Join(", ", ferramentas.Select(id => $"{id}: construivel {cat.Get(id)?.Construivel}, pessoal {cat.Get(id)?.Pessoal}")));

		CatalogoDeItens.FerramentaVoltaACavarDeTeste = true;
		try
		{
			Checa("(defeito injetado: a pa e a furadeira voltam a declarar 'cavar' -- o botao volta) a cobranca A0b.1 acusa as DUAS",
				  ComAcao().Count == 2 && ComAcao().All(l => l.Contains("cavar")), string.Join(" | ", ComAcao()));
		}
		finally { CatalogoDeItens.FerramentaVoltaACavarDeTeste = false; }

		// ---- a frase: o item, o arquivo, e o que ela diz
		static bool FalaDeCavar(string s) =>
			s.Contains("cavar", StringComparison.OrdinalIgnoreCase) || s.Contains("cava ", StringComparison.OrdinalIgnoreCase)
			|| s.Contains("dig", StringComparison.OrdinalIgnoreCase);
		foreach (string id in ferramentas)
		{
			string doCore = Jandirus.Core.Economia.Recurso.DescricaoDeFerramenta(id) ?? "";
			Checa($"A0b.3 {id}: o Core tem a frase dela, e ela fala de MINERAR e do caminho do verb -- nao de cavar",
				  doCore.Contains("minerar mais rápido") && doCore.Contains("menu P > Other > Minerar") && !FalaDeCavar(doCore), doCore);
			Checa($"A0b.4 {id}: a descricao do ITEM (a dica do slot na mochila e do card na aba Tech) e essa frase",
				  CatalogoDeItens.Get(id)?.Descricao == doCore, CatalogoDeItens.Get(id)?.Descricao ?? "(sem item)");
			Checa($"A0b.5 {id}: o `desc` do construcoes.json publicado e a MESMA frase (e nao o \"Allows you to dig\" do DM)",
				  cat.Get(id)?.Desc == doCore, cat.Get(id)?.Desc ?? "(fora do catalogo)");
		}
		string daPa = Jandirus.Core.Economia.Recurso.DescricaoDeFerramenta(CatalogoDeItens.Pa) ?? "";
		string daFuradeira = Jandirus.Core.Economia.Recurso.DescricaoDeFerramenta(CatalogoDeItens.Furadeira) ?? "";
		Checa("A0b.6 os numeros da frase sao as taxas que o servidor paga: a pa diz a dela e a da mao, a furadeira a dela",
			  daPa.Contains($"+{Jandirus.Core.Economia.Recurso.PorSegundoComPa:0} recursos por segundo")
			  && daPa.Contains($"+{Jandirus.Core.Economia.Recurso.PorSegundoNaMao:0} com as mãos")
			  && daFuradeira.Contains($"+{Jandirus.Core.Economia.Recurso.PorSegundoComFuradeira:0} recursos por segundo"),
			  $"{daPa} | {daFuradeira}");
		Checa("A0b.7 so as duas tem frase de ferramenta: o Scouter, a maca e um id qualquer nao",
			  Jandirus.Core.Economia.Recurso.DescricaoDeFerramenta("Scouter") == null
			  && Jandirus.Core.Economia.Recurso.DescricaoDeFerramenta(CatalogoDeItens.Maca) == null
			  && Jandirus.Core.Economia.Recurso.DescricaoDeFerramenta("Nao_Existe_Isso") == null);

		// ---- o extrator, numa arvore DM de mentira com o `desc` de la
		string dm = Path.Combine(Path.GetTempPath(), "jandirus-ferramentaprova-" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(dm);
			// O MESMO FORMATO DO ORIGINAL (`Tier 1.dm`): o `Creatable`, o preco, o requisito e o `desc` em ingles.
			File.WriteAllText(Path.Combine(dm, "Tier 1.dm"), string.Join("\n", new[]
			{
				"obj/Creatables",
				"\tShovel",
				"\t\ticon='Shovel.dmi'",
				"\t\tcost=15",
				"\t\tneededtech=3",
				"\t\tdesc = \"Allows you to dig. Slowly...\"",
				"\t\tcreate_type = /obj/items/Shovel",
				"\tHand_Drill",
				"\t\ticon='Drill Hand.dmi'",
				"\t\tcost=250",
				"\t\tneededtech=15",
				"\t\tdesc = \"Allows you to dig a bit faster.\"",
				"\t\tcreate_type = /obj/items/Hand_Drill",
				"\tCoisa",
				"\t\tcost=100",
				"\t\tneededtech=5",
				"\t\tdesc = \"Uma coisa qualquer.\"",
				"\t\tcreate_type = /obj/Coisa",
				"",
			}));

			Dictionary<string, string> Descs() => DmTechScanner.Scan(dm).Where(d => d.Custo >= 0).ToDictionary(d => d.Id, d => d.Desc);

			Dictionary<string, string> hoje = Descs();
			Checa("A0b.8 o EXTRATOR emite a frase do port pra pa e pra furadeira (regenerar o json nao traz o ingles de volta), e deixa em paz o `desc` de quem nao e ferramenta",
				  hoje.GetValueOrDefault("Shovel") == daPa && hoje.GetValueOrDefault("Hand_Drill") == daFuradeira
				  && hoje.GetValueOrDefault("Coisa") == "Uma coisa qualquer.",
				  string.Join(" | ", hoje.Select(kv => $"{kv.Key}: {kv.Value}")));

			DmTechScanner.DescDoDmNasFerramentasDeTeste = true;
			try
			{
				Dictionary<string, string> antes = Descs();
				Checa("(defeito injetado: o extrator emite o `desc` do DM nas ferramentas) \"Allows you to dig\" volta a sair -- a cobranca A0b.8 acusa",
					  FalaDeCavar(antes.GetValueOrDefault("Shovel") ?? "") && FalaDeCavar(antes.GetValueOrDefault("Hand_Drill") ?? ""),
					  string.Join(" | ", antes.Select(kv => $"{kv.Key}: {kv.Value}")));
			}
			finally { DmTechScanner.DescDoDmNasFerramentasDeTeste = false; }
		}
		catch (Exception e) { Checa("A0b.8 montei a arvore DM de mentira das ferramentas", false, e.Message); }
		finally
		{
			DmTechScanner.DescDoDmNasFerramentasDeTeste = false;
			try { Directory.Delete(dm, recursive: true); } catch { /* temporario */ }
		}

		Console.WriteLine();
	}

	// =====================================================================
	// A0c) SO O QUE O ORIGINAL COMPILA -- as seis armas comentadas, o id repetido e o arquivo fora do .dme
	// =====================================================================
	/// <summary>
	/// *"o catalogo pode tirar essas armas comentadas"* (dono, 2026-10-10). Axe, Staff, Spear, Club, Hammer e Cross
	/// estavam na aba Tech sem existir no jogo original: o bloco delas (`Modules/Tech/Weapons.dm:2-113`) esta
	/// dentro de um `/* */`, e o extrator so pulava `//`.
	///
	/// QUATRO PONTAS, e as quatro tem de concordar:
	///   * o LEITOR (<see cref="DmFonte"/>) segue as regras de comentario que o `dm.exe` segue -- cada uma medida
	///     no BYOND 516.1684 e escrita aqui como uma linha de entrada e a saida esperada;
	///   * o EXTRATOR, numa arvore DM de mentira com os mesmos formatos do original, emite so o que o compilador
	///     veria: nada de dentro do bloco, cada tipo UMA vez (com a declaracao incluida por ultimo vencendo) e
	///     nada do arquivo que o `.dme` nao inclui -- com um botao de defeito por regra;
	///   * o ARQUIVO publicado nao tem as cinco armas, nem id repetido, e os quatro valores que vinham de codigo
	///     morto sao os do DM;
	///   * a LISTA do Core (<see cref="ItensAposentados"/>, a que tira o item da mochila no login e diz por que --
	///     sem zeni de volta: dono, 2026-10-11) fala dos mesmos ids.
	/// O que acontece com quem ja tinha uma das seis na mochila e da `--aposentadosteste` (servidor).
	/// </summary>
	private static void SoOQueOOriginalCompila(CatalogoDeObras cat)
	{
		Console.WriteLine("--- A0c: so o que o original COMPILA (as armas comentadas, o id repetido, o arquivo fora do .dme) ---");

		// ---- o leitor: as regras do compilador, uma por linha
		(string Regra, string[] Entra, string[] Sai)[] regras =
		[
			("o `//` mata o resto da linha, e so ele",
				["\tcost=10 // nota", "\tneededtech=5"], ["\tcost=10 ", "\tneededtech=5"]),
			("`/* */` atravessa linhas, e a linha que era so comentario sai vazia",
				["\t/*Sword", "\t\tcost=1000", "\t\tusr<<\"texto\"*/", "obj/var/weapon = 0"], ["\t", "", "", "obj/var/weapon = 0"]),
			("`/* */` ANINHA: so fecha no segundo `*/`",
				["/* a /* b */ c", "Morta", "*/ Viva"], ["", "", " Viva"]),
			("DENTRO do bloco o `//` esconde o resto da linha -- o `*/` inclusive",
				["/* x // y */", "Ainda_Morta", "*/", "Viva"], ["", "", "", "Viva"]),
			("...e um `/*` depois desse `//` tambem nao conta (a profundidade continua 1)",
				["/* a // /* b", "Morta", "*/", "Viva"], ["", "", "", "Viva"]),
			("STRING protege: `//` e `/*` dentro de aspas sao texto",
				["\tdesc=\"veja http://x /* y */ z\" // fim"], ["\tdesc=\"veja http://x /* y */ z\" "]),
			("...inclusive com aspa escapada no meio",
				["\tdesc=\"a \\\" // b\" // fim"], ["\tdesc=\"a \\\" // b\" "]),
			("a expressao embutida abre OUTRA string dentro da primeira",
				["\treturn \"a[x ? \"//\" : \"/*\"]b\" // fim"], ["\treturn \"a[x ? \"//\" : \"/*\"]b\" "]),
			("o documento `{\"...\"}` atravessa linhas, com aspas e barras dentro",
				["\tnotes={\"<a href=\"http://x\"> // nao e comentario", "/* nem isto", "fim\"} // agora sim"],
				["\tnotes={\"<a href=\"http://x\"> // nao e comentario", "/* nem isto", "fim\"} "]),
			("a string crua `@\"...\"` nao tem escape: a `\\` antes da aspa nao a segura aberta",
				["\tp = @\"c:\\dir\\\" // fim"], ["\tp = @\"c:\\dir\\\" "]),
			("DENTRO de comentario, aspa e apostrofo nao abrem nada",
				["/* it\"s don't", "Morta */ Viva"], ["", " Viva"]),
			("o `//` que acaba em `\\` engole a linha de baixo",
				["// nota \\", "Engolida", "Viva"], ["", "", "Viva"]),
			("o `/*/typepath` do DM e so um bloco que abre",
				["/*/obj/Creatables/Morta", "\tcost=2", "*/", "obj/Creatables/Viva"], ["", "", "", "obj/Creatables/Viva"]),
			("um `//` que traz `/*` nao abre bloco nenhum",
				["\t// nota /* isto nao abre", "\tViva"], ["\t", "\tViva"]),
		];
		foreach ((string regra, string[] entra, string[] sai) in regras)
		{
			string[] veio = DmFonte.SemComentarios(entra);
			Checa($"A0c.1 leitor: {regra}", veio.SequenceEqual(sai),
				  $"veio [{string.Join(" | ", veio)}], esperado [{string.Join(" | ", sai)}]");
		}

		// ---- o extrator, numa arvore DM de mentira com os formatos do original
		string raiz = Path.Combine(Path.GetTempPath(), "jandirus-comentadoprova-" + Guid.NewGuid().ToString("N"));
		string code = Path.Combine(raiz, "Code");
		try
		{
			Directory.CreateDirectory(Path.Combine(code, "Tech"));

			// `Modules/Tech/Weapons.dm:1-114`: o bloco abre COLADO no primeiro nome (`/*Sword`) e fecha depois de
			// uma string, na ultima linha do ultimo item.
			File.WriteAllText(Path.Combine(code, "Tech", "Weapons.dm"), string.Join("\n", new[]
			{
				"obj/Creatables",
				"\t/*Sword",
				"\t\ticon='Sword_Trunks.dmi'",
				"\t\tcost=1000",
				"\t\tneededtech=11 //Deletes itself from contents if the usr doesnt have the needed tech",
				"\t\tClick()",
				"\t\t\tif(usr.zenni>=cost)",
				"\t\t\t\tusr.zenni-=cost",
				"\t\t\t\tvar/obj/A=new/obj/items/Weapons/Sword(locate(usr.x,usr.y,usr.z))",
				"\t\t\telse to_chat(usr, \"You dont have enough money\")",
				"\tAxe",
				"\t\ticon='Axe.dmi'",
				"\t\ticon_state=\"Attack\"",
				"\t\tcost=1000",
				"\t\tneededtech=11 //Deletes itself from contents if the usr doesnt have the needed tech",
				"\t\tverb/Description()",
				"\t\t\tset category =null",
				"\t\t\tusr<<\"A weapon will commonly increase your attack while decreasing a stat in another area.\"*/",
				"obj/var/weapon = 0",
				"",
			}));

			// `Modules/Tech/Tier 1.dm:45-66`: um item vivo com um bloco comentado LOGO ABAIXO dele (o `Books` e o
			// `Saibaman_Seed`), mais os dois casos que o original nao tem e o compilador aceita.
			File.WriteAllText(Path.Combine(code, "Tech", "Tier 1.dm"), string.Join("\n", new[]
			{
				"obj/Creatables",
				"\tLivro",
				"\t\ticon='Books.dmi'",
				"\t\tcost=10",
				"\t\tneededtech=10 //Deletes itself from contents if the usr doesnt have the needed tech",
				"\t\tdesc=\"Livros: veja http://exemplo.com/livros /* isto e texto */ e leia.\"",
				"\t\tcreate_type = /obj/items/Livro",
				"",
				"\t/*Semente",
				"\t\ticon='Saiba Seed.dmi'",
				"\t\tcost=100",
				"\t\tneededtech=12 //Deletes itself from contents if the usr doesnt have the needed tech",
				"\t\tverb/Description()",
				"\t\t\tset category =null",
				"\t\t\tusr<<\"What's not to love?\"*/",
				"\tAgenda",
				"\t\tname=\"Agenda de Bolso\"",
				"\t\ticon='PDA.dmi'",
				"\t\tcost=400",
				"\t\tneededtech=15",
				"\t\tdesc=\"a de Tier 1\"",
				"\t\tcreate_type = /obj/items/Agenda",
				"\t/* um bloco com outro dentro /* aninhado */ que so fecha no segundo fecho",
				"\tMorta_No_Aninhado",
				"\t\tcost=7",
				"\t\tneededtech=7",
				"\t*/",
				"\tViva_Depois_Do_Aninhado",
				"\t\tcost=8",
				"\t\tneededtech=8",
				"\t\tcreate_type = /obj/items/Viva",
				"\t/* dentro do bloco o barra-barra esconde o fecho: // */",
				"\tMorta_Atras_Do_Fecho_Escondido",
				"\t\tcost=9",
				"\t\tneededtech=9",
				"\t*/",
				"\tViva_No_Fim",
				"\t\tcost=11",
				"\t\tneededtech=11",
				"\t\tcreate_type = /obj/items/Fim",
				"",
			}));

			// `Modules/Tech/Camera.dm:16-22`: o MESMO tipo declarado de novo (o `PDA`). No disco este arquivo vem
			// ANTES do `Tier 1.dm`; o `.dme` de baixo o inclui DEPOIS, e e a ordem do `.dme` que decide quem vence.
			File.WriteAllText(Path.Combine(code, "Tech", "Camera.dm"), string.Join("\n", new[]
			{
				"obj/Creatables",
				"\tAgenda",
				"\t\ticon='PDA.dmi'",
				"\t\tcost=400",
				"\t\tneededtech=15 //Deletes itself from contents if the usr doesnt have the needed tech",
				"\t\tdesc = \"a de Camera\"",
				"\t\tcreate_type = /obj/items/Agenda",
				"",
			}));

			// `Cloning Machine.dm:169-175` e `Modules/Tech/DNALabs.dm:271-286`: a primeira declaracao em bloco, a
			// segunda na forma de UMA linha -- seguida de um tipo que nao e Creatable e tem `cost` proprio.
			File.WriteAllText(Path.Combine(code, "Cloning Machine.dm"), string.Join("\n", new[]
			{
				"obj/Creatables",
				"\tRecipiente",
				"\t\tdesc=\"o velho\"",
				"\t\tcost=100000",
				"\t\tcreate_type = /obj/items/recipiente_velho",
				"\t\tneededtech=60",
				"",
			}));
			File.WriteAllText(Path.Combine(code, "Tech", "DNALabs.dm"), string.Join("\n", new[]
			{
				"obj/Creatables/Recipiente",
				"\ticon = 'props.dmi'",
				"\ticon_state = \"Closed\"",
				"\tcost = 50000",
				"\tneededtech = 50",
				"\tallowedRaces = list(\"Human\") //so HUMANOS veem na janela de tech",
				"\tdesc = \"Recipiente criogenico.\"",
				"\tcreate_type = /obj/items/Recipiente",
				"",
				"obj/items/Recipiente",
				"\tname = \"Recipiente\"",
				"\tcost = 1",
				"",
			}));

			// um `.dm` que nenhum `#include` cita: codigo que o jogo nao compila
			File.WriteAllText(Path.Combine(code, "Morto.dm"), string.Join("\n", new[]
			{
				"obj/Creatables",
				"\tFantasma_Fora_Do_Dme",
				"\t\tcost=5",
				"\t\tneededtech=5",
				"",
			}));

			File.WriteAllText(Path.Combine(raiz, "jogo.dme"), string.Join("\n", new[]
			{
				"// DM Environment file for jogo.dme.",
				"// BEGIN_INCLUDE",
				"#include \"Code\\Cloning Machine.dm\"",
				"#include \"Code\\Tech\\DNALabs.dm\"",
				"#include \"Code\\Tech\\Tier 1.dm\"",
				"#include \"Code\\Tech\\Camera.dm\"",
				"#include \"Code\\Tech\\Weapons.dm\"",
				"// END_INCLUDE",
				"",
			}));

			List<ConstrucaoDef> Extrair() => [.. DmTechScanner.Scan(code).Where(d => d.Custo >= 0)];
			static string Ids(List<ConstrucaoDef> l) => string.Join(", ", l.Select(d => d.Id).Order(StringComparer.Ordinal));
			static string Ficha(ConstrucaoDef? d) =>
				d == null ? "(nao saiu)" : $"{d.Id}: {d.Custo:0} zeni, tech {d.TechNecessario:0}, racas [{string.Join(",", d.Racas)}], tipo '{d.CreateType}', desc '{d.Desc}'";
			const string vivos = "Agenda, Livro, Recipiente, Viva_Depois_Do_Aninhado, Viva_No_Fim";
			const string descDoLivro = "Livros: veja http://exemplo.com/livros /* isto e texto */ e leia.";

			List<ConstrucaoDef> hoje = Extrair();
			ConstrucaoDef? De(List<ConstrucaoDef> l, string id) => l.Find(d => d.Id == id);

			bool SoOsVivos(List<ConstrucaoDef> l) => Ids(l) == vivos;
			bool LivroCerto(List<ConstrucaoDef> l) => De(l, "Livro") is { Custo: 10, TechNecessario: 10, CreateType: "/obj/items/Livro" } d && d.Desc == descDoLivro;
			bool AgendaCerta(List<ConstrucaoDef> l) =>
				l.Count(d => d.Id == "Agenda") == 1
				&& De(l, "Agenda") is { Custo: 400, TechNecessario: 15, Desc: "a de Camera", CreateType: "/obj/items/Agenda", Nome: "Agenda de Bolso" };
			bool RecipienteCerto(List<ConstrucaoDef> l) =>
				l.Count(d => d.Id == "Recipiente") == 1
				&& De(l, "Recipiente") is { Custo: 50000, TechNecessario: 50, Desc: "Recipiente criogenico.", CreateType: "/obj/items/Recipiente", Icone: "props.dmi", Estado: "Closed" } r
				&& r.Racas.SequenceEqual(["Human"]);

			Checa("A0c.2 extrator: emite SO o que o compilador ve -- nada de dentro dos tres blocos comentados, nada do arquivo fora do .dme",
				  SoOsVivos(hoje), Ids(hoje));
			Checa("A0c.3 extrator: o item vivo ACIMA de um bloco comentado fica com o preco e o requisito DELE (10 e 10, nao os 100 e 12 do bloco), e o `desc` com `//` e `/*` dentro sai inteiro",
				  LivroCerto(hoje), Ficha(De(hoje, "Livro")));
			Checa("A0c.4 extrator: o tipo declarado em dois arquivos sai UMA vez, com o valor do arquivo que o .dme inclui por ULTIMO (e o que so um escreve fica como esta)",
				  AgendaCerta(hoje), Ficha(De(hoje, "Agenda")));
			Checa("A0c.5 extrator: a declaracao de UMA linha (`obj/Creatables/X`) e lida e vence a de bloco incluida antes -- e o `cost` do tipo seguinte nao vaza pra ela",
				  RecipienteCerto(hoje), Ficha(De(hoje, "Recipiente")));

			DmFonte.BlocoNaoEComentarioDeTeste = true;
			try
			{
				List<ConstrucaoDef> antes = Extrair();
				Checa("(defeito injetado: `/*` volta a ser texto -- o leitor de antes) o Axe e as duas mortas VOLTAM a sair e o Livro fica com o preco do bloco de baixo -- as cobrancas A0c.2 e A0c.3 acusam",
					  !SoOsVivos(antes) && !LivroCerto(antes) && De(antes, "Axe") != null && De(antes, "Morta_No_Aninhado") != null
					  && De(antes, "Morta_Atras_Do_Fecho_Escondido") != null && De(antes, "Livro") is { Custo: 100, TechNecessario: 12 },
					  $"{Ids(antes)} | {Ficha(De(antes, "Livro"))}");
			}
			finally { DmFonte.BlocoNaoEComentarioDeTeste = false; }

			DmTechScanner.DuasDeclaracoesNaoSeFundemDeTeste = true;
			try
			{
				List<ConstrucaoDef> antes = Extrair();
				Checa("(defeito injetado: o tipo declarado duas vezes volta a ser dois) a Agenda sai REPETIDA e o Recipiente fica na declaracao velha -- as cobrancas A0c.4 e A0c.5 acusam",
					  !AgendaCerta(antes) && antes.Count(d => d.Id == "Agenda") == 2 && !RecipienteCerto(antes)
					  && De(antes, "Recipiente") is { Custo: 100000, TechNecessario: 60 },
					  $"{Ids(antes)} | {Ficha(De(antes, "Recipiente"))}");
			}
			finally { DmTechScanner.DuasDeclaracoesNaoSeFundemDeTeste = false; }

			DmFonte.SemDmeDeTeste = true;
			try
			{
				List<ConstrucaoDef> antes = Extrair();
				Checa("(defeito injetado: o .dme deixa de mandar) o arquivo que ninguem inclui volta a ser lido e a ordem do DISCO troca o vencedor da Agenda -- as cobrancas A0c.2 e A0c.4 acusam",
					  !SoOsVivos(antes) && De(antes, "Fantasma_Fora_Do_Dme") != null && !AgendaCerta(antes) && De(antes, "Agenda")?.Desc == "a de Tier 1",
					  $"{Ids(antes)} | {Ficha(De(antes, "Agenda"))}");
			}
			finally { DmFonte.SemDmeDeTeste = false; }
		}
		catch (Exception e) { Checa("A0c.2 montei a arvore DM de mentira do bloco comentado", false, e.Message); }
		finally
		{
			DmFonte.BlocoNaoEComentarioDeTeste = false;
			DmTechScanner.DuasDeclaracoesNaoSeFundemDeTeste = false;
			DmFonte.SemDmeDeTeste = false;
			try { Directory.Delete(raiz, recursive: true); } catch { /* temporario */ }
		}

		// ---- o arquivo publicado
		string[] cinco = ["Axe", "Spear", "Club", "Hammer", "Cross"];
		List<string> aindaLa = [.. cinco.Where(id => cat.Get(id) != null || CatalogoDeItens.Get(id) != null)];
		Checa("A0c.6 catalogo: Axe, Spear, Club, Hammer e Cross sairam do construcoes.json, e nenhum deles vira item",
			  aindaLa.Count == 0, string.Join(", ", aindaLa));

		Construcao? cajado = cat.Get("Staff");
		Checa("A0c.7 catalogo: o `Staff` que ficou e o cajado magico de `Magitech.dm:21-28` (tech 1, 1.000 zeni, com descricao e tipo) -- e nao a arma comentada (tech 11, sem tipo)",
			  cajado is { Tech: 1, Custo: 1000, Tipo: "/obj/items/Equipment/Weapon/Staff/Magic_Staff", Pessoal: true }
			  && cajado.Desc.Contains("magical tool") && cajado.Arte.EndsWith("magic_items.tres", StringComparison.Ordinal),
			  cajado == null ? "fora do catalogo" : $"tech {cajado.Tech:0}, custo {cajado.Custo:0}, tipo '{cajado.Tipo}', arte '{cajado.Arte}'");

		Checa("A0c.8 catalogo: nenhum id repetido no arquivo (duas linhas com o mesmo id viram UMA construcao, a ultima, e a outra some calada)",
			  cat.Repetidos.Count == 0, string.Join(", ", cat.Repetidos));
		const string comRepetido =
			"""
			[
			  { "id": "Coisa", "nome": "Coisa", "custo": 400, "tech": 15, "racas": [], "tipo": "/obj/Coisa" },
			  { "id": "Outra", "nome": "Outra", "custo": 1, "tech": 1, "racas": [], "tipo": "/obj/Outra" },
			  { "id": "coisa", "nome": "Coisa de novo", "custo": 400, "tech": 15, "racas": [], "tipo": "/obj/Coisa" }
			]
			""";
		CatalogoDeObras sujo = CatalogoDeObras.Parse(comRepetido);
		Checa("(defeito injetado: um arquivo com o mesmo id em duas linhas, uma delas com outra caixa) a cobranca A0c.8 acusa o id, e o catalogo fica com DUAS construcoes",
			  sujo.Repetidos.SequenceEqual(["coisa"]) && sujo.Total == 2 && sujo.Get("Coisa")?.Nome == "Coisa de novo",
			  $"repetidos [{string.Join(", ", sujo.Repetidos)}], total {sujo.Total}");

		// OS QUATRO VALORES QUE VINHAM DE CODIGO MORTO. As ancoras sao as do DM, escritas aqui e nao derivadas.
		Construcao? livros = cat.Get("Books"), furadeira = cat.Get(CatalogoDeItens.Furadeira), pda = cat.Get("PDA"), dna = cat.Get("DNA_Container");
		Checa("A0c.9 catalogo: o `Books` custa 10 zeni e pede tech 10 (`Tier 1.dm:45-50`) -- e nao os 100 e 12 do `Saibaman_Seed` comentado logo abaixo dele",
			  livros is { Custo: 10, Tech: 10 }, $"custo {livros?.Custo}, tech {livros?.Tech}");
		Checa("A0c.10 catalogo: a `Hand_Drill` pede tech 10 (`Tier 1.dm:26-31`, 250 zeni) -- e nao os 15 do `Shuriken` comentado logo abaixo dela",
			  furadeira is { Custo: 250, Tech: 10 }, $"custo {furadeira?.Custo}, tech {furadeira?.Tech}");
		Checa("A0c.11 catalogo: o `PDA` e UM, com o texto da declaracao que o DM compila por ultimo (`Tier 1.dm:66-72`, sem a frase das cameras de `Camera.dm:21`)",
			  pda is { Custo: 400, Tech: 15, Tipo: "/obj/items/PDA" } && pda.Desc.EndsWith("if you upgrade it.", StringComparison.Ordinal),
			  pda?.Desc ?? "(fora do catalogo)");
		Checa("A0c.12 catalogo: o `DNA_Container` e o de `DNALabs.dm:271-278` (50.000 zeni, tech 50, so Humano, `/obj/items/DNA_Container`) -- e nao o de `Cloning Machine.dm:170` (100.000, tech 60)",
			  dna is { Custo: 50000, Tech: 50, Tipo: "/obj/items/DNA_Container", Pessoal: true } && dna.Racas.SequenceEqual(["Human"]),
			  dna == null ? "fora do catalogo" : $"custo {dna.Custo:0}, tech {dna.Tech:0}, racas [{string.Join(",", dna.Racas)}], tipo '{dna.Tipo}'");

		// ---- a lista do Core que limpa a mochila no login
		// O QUE UMA LINHA DA LISTA TEM DE SER (nulo = esta certa; senao, o que ha de errado com ela).
		string? Desencontro(ItensAposentados.Aposentado a)
		{
			Construcao? noCatalogo = cat.Get(a.Id);
			// QUEM AINDA TEM LINHA NO CATALOGO so pode ser OUTRA coisa com o mesmo nome (o `Staff`): a arma
			// aposentada nao tinha `create_type`, e a que ficou tem.
			bool some = noCatalogo == null && CatalogoDeItens.Get(a.Id) == null;
			bool virouOutroItem = noCatalogo is { Tipo.Length: > 0 } && a.Id == "Staff";
			// O PORQUE E O QUE O JOGADOR LE, e nao promete dinheiro: item aposentado sai sem reembolso (dono, 2026-10-11).
			bool prometeDinheiro = a.Porque.Contains("zeni", StringComparison.OrdinalIgnoreCase);
			bool certa = (some || virouOutroItem) && a.Porque.Length > 0 && !prometeDinheiro
						 && a.SaiuNaVersao >= 1 && a.SaiuNaVersao <= ItensAposentados.VersaoAtual;
			return certa ? null : $"{a.Id} (versao {a.SaiuNaVersao}, porque '{a.Porque}', no catalogo: {(noCatalogo == null ? "nao" : $"sim, tipo '{noCatalogo.Tipo}'")})";
		}
		List<string> desencontros = [.. ItensAposentados.Todos.Select(Desencontro).OfType<string>()];
		// (2026-10-26) A VERSAO 2 da lista e o sintetizador de virus (a maquina e os dezenove produtos, o pedido 98 do dono): as seis
		// continuam sendo a versao 1, e os novos passam pelo mesmo `Desencontro` (fora do catalogo, com o porque, sem zeni).
		Checa("A0c.13 a lista do Core (`ItensAposentados`): as SEIS da versao 1, cada uma com o porque (que nao fala em zeni) -- cinco fora "
			  + "do catalogo, e o `Staff` la como OUTRO item; e a versao 2, o sintetizador de virus, tambem fora do catalogo",
			  ItensAposentados.Todos.Count(a => a.SaiuNaVersao == 1) == 6 && desencontros.Count == 0
			  && cinco.Append("Staff").All(id => ItensAposentados.Todos.Any(a => a.Id == id && a.SaiuNaVersao == 1))
			  && ItensAposentados.Todos.Any(a => a.Id == Jandirus.Core.Tech.Sintetizador.Tipo && a.SaiuNaVersao == 2),
			  string.Join(" | ", desencontros));
		Checa("A0c.13 (contra-exemplos da linha) um porque que promete zeni, um id que continua a venda e uma versao do futuro sao ACUSADOS",
			  Desencontro(new("Axe", "Axe", 1, "os 1.000 zeni que ele custava voltaram")) != null
			  && Desencontro(new("PDA", "PDA", 1, "saiu")) != null
			  && Desencontro(new("Axe", "Axe", ItensAposentados.VersaoAtual + 1, "saiu")) != null
			  && Desencontro(new("Axe", "Axe", 1, "saiu")) == null);

		Console.WriteLine();
	}

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
	// A3) ONDE A ARVORE E DESENHADA
	// =====================================================================
	/// <summary>
	/// A ANCORA, escrita aqui e nao derivada: a folha da macieira (`DecTrees.dmi`) tem 61 px de largura, e
	/// o `New()` de `/obj/Trees` (`Modules/Turfs/Plants.dm:31-35`) escreve `pixel_x = 16 - 61/2`.
	/// </summary>
	private const int LarguraDaMacieira = 61;
	private const double PxDaMacieira = -14.5;

	/// <summary>
	/// ============================ A MACIEIRA E CENTRADA, E ISSO NAO ESTA NA ARVORE DE TIPOS ============================
	/// O `pixel_x` de uma `/obj/Trees` nao e declarado: o `New()` da base o calcula da largura do icone. O
	/// extrator so lia o declarado, e toda macieira (as dos mapas e a que nasce da semente) saia com
	/// `px = 0` -- 14,5 px a direita de onde o BYOND a poe. O cliente desloca a obra pelo `px` DESTE catalogo
	/// (`Client/ObraDesenhada.cs`), entao o numero errado aqui e o desenho errado na tela.
	///
	/// A MEDIDA TEM TRES PONTAS, e a cobranca so fecha se as tres batem com a ancora:
	///   * a LARGURA, lida da folha pela propria bancada (o `.png` ao lado do `.tres` que o catalogo aponta);
	///   * o `px` do catalogo PUBLICADO -- o jogo nao roda o extrator, ele le o arquivo;
	///   * o `px` que o extrator calcula pra mesma folha (`DmTechScanner.PixelXDeArvore`, a linha do conserto).
	///
	/// O contra-exemplo liga o defeito no extrator e refaz a MESMA medida: a ponta do extrator tem que mudar
	/// de resposta. Se ficasse igual, a cobranca de cima nao estaria medindo o conserto.
	/// =================================================================================================================
	/// </summary>
	private static void Macieira(CatalogoDeObras cat, string pastaAssets)
	{
		Console.WriteLine("\n--- A3: a macieira e centrada no tile (o `pixel_x` que o `New()` do DM escreve) ---");

		Construcao? achada = cat.Get("AppleTree");
		string? caminho = achada == null ? null : NoDisco(pastaAssets, Path.ChangeExtension(achada.Arte, ".png"));
		if (achada is not { } arv || caminho is not { } folha || !File.Exists(folha))
		{
			Checa("A3 a macieira esta no catalogo e a folha dela esta no disco", false,
				  achada == null ? "sem `AppleTree` no construcoes.json"
								 : $"arte '{achada.Arte}' -> {caminho ?? "(arte sem `res://`, ou nenhum project.godot acima da pasta)"}");
			return;
		}

		(int Largura, double DoCatalogo, double? DoExtrator) Medir() =>
			(DmiFile.Read(folha)?.IconWidth ?? 0, arv.PixelX, DmTechScanner.PixelXDeArvore(arv.Tipo, folha));

		static bool Centrada((int Largura, double DoCatalogo, double? DoExtrator) m) =>
			m.Largura == LarguraDaMacieira && 16 - m.Largura / 2.0 == PxDaMacieira
			&& m.DoCatalogo == PxDaMacieira && m.DoExtrator == PxDaMacieira;

		static string Laudo((int Largura, double DoCatalogo, double? DoExtrator) m) =>
			$"folha de {m.Largura} px | catalogo px={N(m.DoCatalogo)} | "
			+ $"extrator px={(m.DoExtrator is { } e ? N(e) : "nulo (fica o declarado)")}";

		var medida = Medir();
		Checa($"A3 a macieira e centrada (px = 16 - largura/2 = {N(PxDaMacieira)}, folha de {LarguraDaMacieira} px): "
			  + "no catalogo publicado e no extrator", Centrada(medida), Laudo(medida));

		DmTechScanner.ArvoreSemCentroDeTeste = true;
		try
		{
			var comDefeito = Medir();
			Checa("(defeito injetado: a arvore volta ao `pixel_x` declarado do tipo) o extrator deixa de centrar "
				  + "a macieira -- a cobranca A3 acusa",
				  medida.DoExtrator == PxDaMacieira && comDefeito.DoExtrator != PxDaMacieira && !Centrada(comDefeito),
				  Laudo(comDefeito));
		}
		finally { DmTechScanner.ArvoreSemCentroDeTeste = false; }
	}

	/// <summary>O numero como o `construcoes.json` o escreve (ponto decimal, sem zero sobrando).</summary>
	private static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

	/// <summary>
	/// `res://Assets/Sprites/x.png` -> o arquivo de verdade. A raiz do `res://` e a pasta do `project.godot`,
	/// achada SUBINDO de <paramref name="pastaAssets"/> -- e nao "a pasta acima dela": assim a mesma bancada
	/// julga o catalogo de uma pasta de rascunho (que so traz o `Data/`) contra as folhas do projeto em que
	/// o rascunho mora.
	/// </summary>
	private static string? NoDisco(string pastaAssets, string res)
	{
		const string prefixo = "res://";
		if (!res.StartsWith(prefixo, StringComparison.Ordinal)) return null;
		for (var d = new DirectoryInfo(Path.GetFullPath(pastaAssets)); d != null; d = d.Parent)
			if (File.Exists(Path.Combine(d.FullName, "project.godot")))
				return Path.Combine(d.FullName, res[prefixo.Length..].Replace('/', Path.DirectorySeparatorChar));
		return null;
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
