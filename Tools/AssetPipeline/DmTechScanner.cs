using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jandirus.Tools;

/// <summary>Uma coisa construivel, do jeito que o jogo precisa saber dela pra vender.</summary>
public sealed class ConstrucaoDef
{
	public string Id = "";          // "Research_Station"
	public string Nome = "";
	public string Desc = "";
	public double Custo = 0;        // `cost`, em zeni
	public double TechNecessario;   // `neededtech`
	public List<string> Racas = []; // `allowedRaces`: o mainframe de androide e so pra Humano
	public string Arquivo = "";     // de onde veio, pra conferir

	// ============================ O QUE APARECE NO CHAO ============================
	// A construcao tem DOIS typepaths no original: o `obj/Creatables/X` que voce compra (preco,
	// requisito, descricao) e o `create_type` que e ERGUIDO. Sao objetos diferentes, e a
	// diferenca importa: o Creatable de Research_Station tem `density = 0` -- e o
	// `/obj/Technology/Research_Station` que ele cria e que tem `density = 1`.
	//
	// Ler so o Creatable e o erro que deixava a bancada atravessavel: a MESMA maquina que o
	// mapa desenha (e que bloqueia, porque o conversor le a arvore certa) virava decoracao
	// quando um jogador a construia. O dono fotografou os dois lado a lado.
	// ===============================================================================
	public string CreateType = "";  // `create_type`: o typepath que vai pro chao
	public string? Icone;           // .dmi de quem foi erguido
	public string? Estado;          // icon_state dele
	public bool Densa;              // density DELE, nao do item de loja
	public double PixelX, PixelY;   // `pixel_x`/`pixel_y`: o desenho nao mora no canto do tile

	/// <summary>res:// do SpriteFrames convertido. Vazio = o .dmi nao existe no port.</summary>
	public string Arte = "";

	// ============================ AQUI MORAVA UM `TipoDoClick`, E ELE FOI DELETADO ============================
	// Era o typepath deduzido do `new/obj/items/Weapons/Axe(...)` que o `Click()` de sete entradas escreve no
	// lugar do `create_type`: as seis armas de `Modules/Tech/Weapons.dm` e o `Central_Computer` de `Tierless.dm`.
	// AS SETE ESTAO DENTRO DE `/* */` NO ORIGINAL -- o campo so existia pra classificar codigo que o jogo nao
	// compila. Com o leitor entendendo comentario de bloco (`DmFonte`), toda entrada viva tem `create_type`
	// (conferido contra a arvore de objetos do `dm.exe -o`: 91 de 91), e ele ficou sem leitor.
	// =========================================================================================================

	/// <summary>
	/// ISTO E DE USO PESSOAL (se carrega/veste) em vez de coisa que se assenta no chao?
	///
	/// A REGRA 2 DO PEDIDO DO DONO, extraida e nao digitada -- ver <see cref="DmVerbScanner"/> pro
	/// porque, e <see cref="Classificar"/> pro como. Vira o campo `pessoal` do `construcoes.json`,
	/// e o unico consumidor dele e o `CatalogoDeItens.Get`, que decide dali se o item ganha ou nao
	/// a acao "posicionar".
	/// </summary>
	public bool Pessoal;

	/// <summary>Qual dos quatro criterios decidiu -- so pro relatorio do pipeline.</summary>
	public string PorQue = "";
}

/// <summary>
/// Le as construcoes (`obj/Creatables`) direto do DM.
///
/// MESMO ARGUMENTO DAS SKILLS: sao 110 entradas com custo, requisito de tecnologia e restricao de
/// raca. Transcrever isso a mao erra um zero em algum custo e ninguem descobre ate um jogador
/// comprar um mainframe de 500.000 por 50.000.
///
/// A ARMADILHA AQUI E OUTRA, e vale anotar: em `obj/Creatables` o nome do bloco E o id do item, e
/// os blocos sao aninhados por indentacao SEM typepath (`Research_Station` solto, com as
/// propriedades embaixo). Nao da pra procurar por `/obj/Creatables/...`; o que marca o comeco de
/// uma entrada e "identificador sozinho na linha, um nivel dentro de um bloco Creatables".
///
/// ============================ O QUE ELE LE E O QUE O COMPILADOR LE (2026-10-10) ============================
/// Tres defeitos da mesma familia -- o extrator via codigo que o jogo original nao tem, ou via metade dele --,
/// achados comparando o catalogo com a arvore de objetos que o `dm.exe -o` imprime:
///
///   * COMENTARIO DE BLOCO. O leitor so pulava `//`, e o que esta dentro de `/* */` saia: seis armas a venda
///     que o original desativou, e os precos de dois itens vivos trocados pelos do bloco morto logo abaixo
///     deles. Quem le agora e o <see cref="DmFonte"/> (as regras estao la, medidas no compilador).
///   * O MESMO TIPO EM DOIS ARQUIVOS. No DM isso e UM tipo, e a propriedade escrita nos dois fica com o valor
///     do arquivo incluido depois. O extrator emitia DUAS entradas (o `PDA` repetido do `construcoes.json`) --
///     e o jogo ficava com a ultima do arquivo ordenado por tech e custo, que so por sorte era a certa.
///   * A FORMA DE UMA LINHA. `obj/Creatables/DNA_Container` (`Tech/DNALabs.dm:271`) nem era lida, e e ela que
///     vale: 50.000 zeni, tech 50, so Humano. O catalogo vendia a de `Cloning Machine.dm:170` (100.000, tech 60).
/// ===========================================================================================================
/// </summary>
public static class DmTechScanner
{
	private static readonly Regex RxNum = new(@"^-?[0-9.]+$", RegexOptions.Compiled);
	private static readonly Regex RxStr = new("\"(?<s>[^\"]*)\"", RegexOptions.Compiled);

	/// <summary>No DM o .dmi vai entre ASPAS SIMPLES (`icon = 'ResearchBench.dmi'`).</summary>
	private static readonly Regex RxIcone = new("'(?<s>[^']*)'", RegexOptions.Compiled);

	/// <summary>
	/// DEFEITO INJETADO (bancada `instalar-prova`): o extrator volta a emitir o que pede tecnologia acima do
	/// teto do DM -- o estado de antes de 2026-10-10, que punha o `Spacepod_Placed` de tech 999 na aba Tech.
	/// Falso na extracao, sempre. Lido pela propria linha do conserto, em <see cref="Scan"/>.
	/// </summary>
	public static bool AcimaDoTetoEntraDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `instalar-prova`): o extrator volta a emitir o `desc` do DM pra pa e pra
	/// furadeira ("Allows you to dig...") -- o que uma regeneracao do `construcoes.json` traria de volta. Falso na
	/// extracao, sempre. Lido pela propria linha do conserto, em <see cref="Scan"/>.
	/// </summary>
	public static bool DescDoDmNasFerramentasDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `instalar-prova`): o tipo declarado em dois lugares volta a ser dois -- a segunda
	/// declaracao em bloco sai como entrada REPETIDA (o `PDA` duplicado do `construcoes.json` de antes de
	/// 2026-10-10) e a de uma linha so (`obj/Creatables/X`) volta a nao ser lida. Lido pelas duas linhas do
	/// conserto, em <see cref="Ler"/>.
	/// </summary>
	public static bool DuasDeclaracoesNaoSeFundemDeTeste;

	public static List<ConstrucaoDef> Scan(string pastaCode)
	{
		// SO O QUE O `.dme` INCLUI, E NA ORDEM DELE: a ordem decide qual declaracao vence quando o mesmo tipo
		// esta em dois arquivos. Ver `DmFonte.Arquivos`.
		var todas = new List<ConstrucaoDef>();
		foreach (string arq in DmFonte.Arquivos(pastaCode))
			Ler(arq, todas);

		// SO INTERESSA O QUE TEM PRECO OU REQUISITO. `obj/Creatables` tambem e usado como base
		// abstrata e por blocos que so declaram icone; sem preco nem requisito nao ha o que vender.
		//
		// ============================ E O QUE O DM DEIXA ALGUEM COMPRAR ============================
		// A janela de tecnologia do original APAGA o que o jogador nao alcanca (`TechWindow.dm:48`:
		// `if(techskill<A.neededtech ...) deleteflag=1`) e o `techskill` para em 100 (`TechSupport.dm:27`).
		// Entao um `neededtech` acima disso e a forma de o DM dizer "isto nunca entra na janela": os dois
		// `*_Placed` de tech 999 (`Tier3.dm:16-29`, `Tier 1.5.dm:44-52`) sao balcoes que o mapa traz -- o
		// `/obj/Creatables/Boat_Placed` esta posto na agua de Arconia, `1to26.dmm:1369`, e vende UMA vez por
		// clique nele --, e nao linhas do catalogo. O port mostra a lista inteira com o motivo de cada nao,
		// entao la a marca virava um card que ninguem nunca destrava: o "Space Pod de tech 999" que o dono
		// mandou tirar (2026-10-10). O teto e o do Core (`CatalogoDeObras.TechMaximoDoDm`), que o jogo tambem le.
		//
		// O `Central_Computer` de tech 200 (`Tierless.dm:19-33`) cairia junto, e la o DM diz duas vezes: alem do
		// teto, o bloco inteiro esta dentro de um `/* */` -- hoje ele nem chega aqui, como as seis armas de
		// `Weapons.dm:2-113` (dono, 2026-10-10: *"o catalogo pode tirar essas armas comentadas"*). Ver `Ler`.
		// ===========================================================================================
		var lista = todas.Where(c => c.Custo > 0 || c.TechNecessario > 0)
						 .Where(c => AcimaDoTetoEntraDeTeste || c.TechNecessario <= Jandirus.Core.Tech.CatalogoDeObras.TechMaximoDoDm)
						 // (2026-10-26) O SINTETIZADOR DE VIRUS SAIU DO JOGO (o pedido 98 do dono): o DM ainda o declara, e uma regeneracao
						 // do `construcoes.json` o traria de volta. Ver `Jandirus.Core.Tech.Sintetizador`.
						 .Where(c => c.Id != Jandirus.Core.Tech.Sintetizador.Tipo)
						 .ToList();
		lista.AddRange(MobiliaDeMapa());

		// ============================ A PA E A FURADEIRA NAO CAVAM MAIS ============================
		// O `desc` do DM diz "Allows you to dig. Slowly..." e "Allows you to dig a bit faster." -- e era isso que
		// elas faziam la (`Tier 1.dm:254-331`). O port tirou o cavar (dono, 2026-10-10: *"tire a opcao de cavar
		// da pa e furadeira pq e redundante"*): na mochila, elas so aceleram o verb Minerar. O texto que vai pro
		// catalogo e o do Core (`Recurso.DescricaoDeFerramenta`), o MESMO que o item da mochila mostra -- e esta
		// volta existe pra uma regeneracao do `construcoes.json` nao trazer o ingles do DM de volta.
		// ==========================================================================================
		if (!DescDoDmNasFerramentasDeTeste)
			foreach (ConstrucaoDef c in lista)
				if (Jandirus.Core.Economia.Recurso.DescricaoDeFerramenta(c.Id) is { } doPort) c.Desc = doPort;

		return lista;
	}

	/// <summary>
	/// ============================ A MOBILIA QUE NAO SE CONSTROI ============================
	/// Maquinas que existem nos `.dmm` e NAO sao `Creatable` nenhum -- o dono nao as compra, elas
	/// ja estao lá. O banco e o caso: `obj/Bank` (`Modules/Tech/Bank.dm:25`), com tres verbos
	/// (`Bank`, `Deposit_Item`, `Retrieve_Item`) e `density = 1`.
	///
	/// ELAS ENTRAM NO MESMO CATALOGO das construidas, e nao num segundo arquivo, porque precisam
	/// exatamente das mesmas coisas: arte, `icon_state`, densidade, deslocamento e alcance de uso.
	/// Um catalogo separado seria uma segunda verdade sobre a mesma maquina, e a primeira coisa a
	/// divergir seria a densidade -- que foi justamente o defeito que ja aconteceu aqui entre o
	/// item de loja e o `create_type`.
	///
	/// CUSTO -1 E A MARCA de "nao se ergue" (ver `Construcao.Construivel`), e e ela que as tira da
	/// loja e faz o servidor recusar um pedido de construir vindo de cliente modificado.
	///
	/// LISTA DECLARADA, e nao varredura. Da pra achar todo `/obj/` com `verb/` no DM, mas isso
	/// traria arma, comida e remedio junto -- coisas que se CARREGAM, nao que ficam no chao. O que
	/// entra aqui e mobilia: fica parada num tile e se usa de perto.
	/// =======================================================================================
	/// </summary>
	private static IEnumerable<ConstrucaoDef> MobiliaDeMapa() =>
	[
		new()
		{
			Id = "Bank", Nome = "Bank", CreateType = "/obj/Bank",
			Desc = "Deposita e saca zeni. O que fica no banco nao se perde ao morrer.",
			Custo = -1, TechNecessario = -1,
		},

		// A ARVORE DE MACA -- `/obj/Trees/AppleTree` (`Modules/Turfs/Plants.dm:54-75`).
		//
		// ============================ POR QUE UMA ARVORE E "MOBILIA" ============================
		// Ela nao parece maquina, mas e exatamente a mesma coisa pro jogo: fica parada num tile,
		// bloqueia (`density = 1`), e se usa de perto. A alternativa era um segundo sistema de
		// "objetos do mundo que respondem" ao lado do de construcoes -- duas verdades sobre a mesma
		// pergunta ("o que tem perto de mim que da pra usar?").
		//
		// Entrando aqui, ela ganha de graca tudo o que o banco ja tem: sai do tilemap e vira node,
		// bloqueia passagem, aparece pra todo mundo pelo mesmo pacote, e o alcance de uso a alcanca.
		//
		// O `pixel_x` DELA NAO VEM DA ARVORE DE TIPOS: quem centra uma `/obj/Trees` e o `New()`, em
		// runtime, e o `Resolver` refaz a conta com a largura da folha -- ver `PixelXDeArvore`.
		// =======================================================================================
		new()
		{
			Id = "AppleTree", Nome = "Apple Tree", CreateType = "/obj/Trees/AppleTree",
			Desc = "Da macas. Dez por vez, e leva um tempo pra repor o que foi colhido.",
			Custo = -1, TechNecessario = -1,
		},

		// ============================ A PORTA DA SALA DO TEMPO ============================
		// `/turf/Teleporters/tohbtc` (`Code/Turfs.dm:157-165`), no Templo Sagrado (z12).
		//
		// ELA E A UNICA MOBILIA DESTE CATALOGO QUE E UM `/turf`, e isso e deliberado. No DM ela ja
		// NAO era uma passagem comum: o `Enter()` dela devolve **0** (nunca se atravessa a porta) e
		// chama `htc_try_enter()`, que confere autorizacao do Guardiao e a recarga de 24 h antes de
		// teleportar. Ou seja, no original ela ja e um OBJETO QUE RESPONDE com o desenho de uma
		// porta, e nao um chao que leva a algum lugar.
		//
		// Por isso o `Tools/AssetPipeline/Passagens.cs` a deixou de fora da tabela de passagens
		// automaticas -- exporta-la como "pisou, foi" entregaria de graca a coisa mais cara do jogo.
		// O que faltava era o SUBSTITUTO, e ele e este: virando mobilia, a porta ganha de graca o
		// mesmo caminho do banco e da macieira (sai do tilemap, vira node, bloqueia, aparece pra
		// todo mundo, e o menu da tecla E a alcanca), e o gate mora no servidor
		// (`GameServer.SalaDoTempo.cs`), que e quem decide.
		//
		// O ICONE E O ESTADO SAEM DA ARVORE DO DM (`Door6.dmi`, `icon_state = "Closed"`) pelo
		// `Resolver` abaixo, como os outros dois -- nao ha arte digitada aqui.
		// =================================================================================
		// ============================ A COMIDA DA SALA DO TEMPO ============================
		// `/obj/items/food/Cooked_Meat` (`Modules/Stamina/Food.dm:102-106`: `icon = 'food.dmi'`,
		// `icon_state = "meatcooked"`, `nutrition = 30`).
		//
		// ELA E O UNICO ITEM DESTA LISTA, e o cabecalho acima diz em voz alta que comida NAO entra
		// aqui ("o que entra e mobilia: fica parada num tile e se usa de perto"). A excecao tem
		// motivo, e ele e que **esta** comida e exatamente isso: as duas porcoes da Sala do Tempo
		// (regra do dono 13.6b) nascem no chao perto da porta, ficam paradas, se usam de perto e
		// **nao passam pela mochila** -- se passassem, o teto de duas porcoes viraria "duas por
		// viagem" e a dupla sairia de la com comida no bolso.
		//
		// CUSTO -1: ninguem constroi uma refeicao. Quem as poe no chao e o servidor, quando a
		// sessao comeca e quando alguem come uma (ver `GameServer.SalaSessao.cs`).
		// =================================================================================
		new()
		{
			Id = "Cooked_Meat", Nome = "Refeicao da Sala do Tempo",
			CreateType = "/obj/items/food/Cooked_Meat",
			Desc = "Comida quente das provisoes da Sala do Tempo. Enche o estomago de verdade -- "
				 + "e outra porcao aparece no lugar quando esta acabar.",
			Custo = -1, TechNecessario = -1,
		},

		new()
		{
			Id = "Time_Chamber_Door", Nome = "Porta da Sala do Tempo",
			CreateType = "/turf/Teleporters/tohbtc",
			Desc = "A porta da Sala do Tempo. Precisa da autorizacao do Guardiao da Terra, "
				 + "e o corpo so aguenta uma visita por dia.",
			Custo = -1, TechNecessario = -1,
		},

		// A AGUA ULTRA DIVINA DO MIRANTE -- `/obj/Sacred_Water` (`Modules/Ranks/ordered/EarthRanks.dm:181-231`),
		// um jarro so, na celula 142,62 do z 12. O `.objetos` do Mirante e a linha do `construcoes.json` foram
		// postos a mao em 2026-10-10 (rodar o conversor inteiro mexeria em todos os mapas por causa de um
		// jarro); esta entrada existe pra a proxima rodada dele gerar os dois de novo, iguais.
		new()
		{
			Id = "Sacred_Water", Nome = "Água Ultra Divina",
			CreateType = "/obj/Sacred_Water",
			Desc = "O jarro do Mirante. Aumenta o poder de luta e o vigor de quem aguenta -- e mata quem nao "
				 + "tem o corpo pra ela. So funciona uma vez na vida.",
			Custo = -1, TechNecessario = -1,
		},

		// ============================ O OVO (dono, 2026-10-17: *"tinha um sprite de ovo"*) ============================
		// O `mob/Egg` do DM (`NPClist.dm:15-25`: `icon = 'Egg.dmi'`), que o `Lay_Egg` poe no chao (`Mating.dm:230`) e de onde o filho nasce
		// (`SpawnPoints.dm:136-147`). No DM ele e um MOB, e nao um obj -- e a arvore de tipos deste extrator so le `/turf` e `/obj`
		// (`DmTurfScanner.Interessa`), entao o icone e a densidade vem escritos aqui: o `Egg.dmi` declarado, e a densidade de todo mob do
		// BYOND (1; o `mob/Egg` nao a muda). Ninguem o constroi (custo -1) e ele nao tem verbo: e um ovo no chao, e cai num soco.
		// =================================================================================================================
		new()
		{
			Id = "Egg", Nome = "Ovo",
			CreateType = "/mob/Egg",
			Desc = "O ovo de alguém de uma raça que põe ovo. Ele choca quando alguém criar um personagem novo e aceitar nascer dele -- "
				 + "o filho sai do lado dele. Um soco o quebra, e o filho se perde.",
			Custo = -1, TechNecessario = -1,
			Icone = "Egg.dmi", Densa = true,
		},
	];

	private static void Ler(string arq, List<ConstrucaoDef> saida)
	{
		// O ARQUIVO COMO O COMPILADOR O VE: sem comentario de linha NEM de bloco. Era aqui que morava um
		// `StartsWith("//")` sozinho, e por ele as seis armas de `Weapons.dm:2-113` entravam no catalogo.
		string[] linhas = DmFonte.Linhas(arq);
		string nomeDoArquivo = Path.GetFileName(arq);
		int indRaiz = -1;                 // indentacao da linha `obj/Creatables`
		ConstrucaoDef? atual = null;
		int indAtual = -1;
		bool solta = false;               // a entrada aberta veio na forma de UMA linha (`obj/Creatables/X`)
		bool iconeLido = false, estadoLido = false;   // o primeiro `icon`/`icon_state` DESTA declaracao e o que vale

		// ============================ O MESMO TIPO DECLARADO DE NOVO E O MESMO TIPO ============================
		// Medido no BYOND 516.1684: `obj/Creatables/PDA` escrito em dois arquivos da UM tipo, e a propriedade que
		// os dois escrevem fica com o valor do que foi compilado DEPOIS (a que so um escreve fica como esta). Entao
		// a segunda declaracao REABRE a entrada em vez de criar outra, e cada atribuicao dela passa por cima.
		// ======================================================================================================
		ConstrucaoDef Abrir(string id)
		{
			iconeLido = estadoLido = false;
			ConstrucaoDef? ja = DuasDeclaracoesNaoSeFundemDeTeste ? null : saida.Find(c => c.Id == id);
			if (ja != null)
			{
				if (!ja.Arquivo.Split(" + ").Contains(nomeDoArquivo)) ja.Arquivo += " + " + nomeDoArquivo;
				return ja;
			}
			var nova = new ConstrucaoDef { Id = id, Nome = id.Replace('_', ' '), Arquivo = nomeDoArquivo };
			saida.Add(nova);
			return nova;
		}

		for (int i = 0; i < linhas.Length; i++)
		{
			string linha = linhas[i].TrimEnd();
			if (linha.Length == 0) continue;

			int ind = 0;
			while (ind < linha.Length && linha[ind] == '\t') ind++;
			string corpo = linha[ind..];

			// a entrada de uma linha so acaba na primeira linha que volta ao nivel dela
			if (solta && ind <= indAtual) { solta = false; atual = null; }

			// abriu um bloco de construiveis?
			if (corpo is "obj/Creatables" or "/obj/Creatables") { indRaiz = ind; atual = null; solta = false; continue; }

			// A FORMA DE UMA LINHA: `obj/Creatables/DNA_Container`, com as propriedades embaixo. E a mesma
			// entrada, escrita com o caminho inteiro -- e o extrator so via a outra forma.
			if (IdDeUmaLinha(corpo) is { } idSolto && !DuasDeclaracoesNaoSeFundemDeTeste)
			{
				indRaiz = -1;
				solta = true;
				atual = Abrir(idSolto);
				indAtual = ind;
				continue;
			}

			if (!solta)
			{
				if (indRaiz < 0) continue;
				if (ind <= indRaiz) { indRaiz = -1; atual = null; continue; }

				// uma entrada nova: identificador sozinho, um nivel dentro do bloco
				if (ind == indRaiz + 1 && EhIdentificador(corpo))
				{
					atual = Abrir(corpo);
					indAtual = ind;
					continue;
				}
			}

			if (atual == null || ind <= indAtual) continue;

			int igual = corpo.IndexOf('=');
			if (igual <= 0) continue;
			string chave = corpo[..igual].Trim();
			string valor = corpo[(igual + 1)..].Trim();

			switch (chave)
			{
				case "cost" when RxNum.IsMatch(valor):
					atual.Custo = double.Parse(valor, CultureInfo.InvariantCulture); break;
				case "neededtech" when RxNum.IsMatch(valor):
					atual.TechNecessario = double.Parse(valor, CultureInfo.InvariantCulture); break;
				case "name":
					if (RxStr.Match(valor) is { Success: true } mn) atual.Nome = mn.Groups["s"].Value; break;
				case "desc":
					if (RxStr.Match(valor) is { Success: true } md) atual.Desc = md.Groups["s"].Value; break;
				// a lista e ATRIBUIDA, nao somada: a segunda declaracao do tipo troca a da primeira
				case "allowedRaces":
					atual.Racas = [.. RxStr.Matches(valor).Select(m => m.Groups["s"].Value)]; break;
				// o typepath do que vai pro chao. Sem aspas no DM: `create_type = /obj/Technology/X`
				case "create_type":
					atual.CreateType = valor.Trim(); break;
				// o icone do PROPRIO Creatable serve de reserva: nem toda entrada tem create_type
				// (varias sao `/obj/buildables`, que se ergue como si mesmo). O PRIMEIRO de cada declaracao
				// e o que vale -- os de baixo podem ser de corpo de proc (`icon_state = "Open"` num verb).
				case "icon":
					if (!iconeLido && RxIcone.Match(valor) is { Success: true } mi) { atual.Icone = mi.Groups["s"].Value; iconeLido = true; }
					break;
				case "icon_state":
					if (!estadoLido && RxStr.Match(valor) is { Success: true } mis) { atual.Estado = mis.Groups["s"].Value; estadoLido = true; }
					break;
			}
		}
	}

	private static bool EhIdentificador(string s) => s.Length > 0 && s.All(ch => char.IsLetterOrDigit(ch) || ch == '_');

	/// <summary>`obj/Creatables/DNA_Container` (com ou sem a barra da frente) -> `DNA_Container`. Nulo pra qualquer outra linha.</summary>
	private static string? IdDeUmaLinha(string corpo)
	{
		const string raiz = "obj/Creatables/";
		string s = corpo.StartsWith('/') ? corpo[1..] : corpo;
		if (!s.StartsWith(raiz, StringComparison.Ordinal)) return null;
		string id = s[raiz.Length..];
		return EhIdentificador(id) ? id : null;
	}

	/// <summary>
	/// PREENCHE O QUE VAI PRO CHAO: icone, estado, densidade e deslocamento de quem foi ERGUIDO.
	///
	/// Reusa o <see cref="DmTurfScanner"/> em vez de reler o DM: ele ja varre `/obj` inteiro com
	/// herança de `density` resolvida, e foi ele que descobriu que o Creatable e o objeto do chao
	/// tem densidades DIFERENTES. Um segundo leitor da mesma arvore seria uma segunda chance de
	/// divergir.
	///
	/// Devolve quantas ficaram sem arte -- e o numero que diz se o pipeline de sprites cobriu o
	/// que a tecnologia precisa.
	/// </summary>
	/// <summary>
	/// ============================ A REGRA 2, INTEIRA, NUM LUGAR SO ============================
	/// *"item que se poe no chao ganha a acao INSTALAR; item de uso pessoal (scouter, armaduras,
	/// pesos) NAO ganha -- ele e equipavel."*
	///
	/// Quatro criterios, nesta ordem, e a ordem E a regra:
	///
	///   1. **TEM `Bolt`** -> CHAO. `Bolt` e o verb do original pra fixar a coisa no chao (o port ja
	///      o chama de `Aparafusar`). Ele vem PRIMEIRO porque desempata os dois casos que o escopo
	///      sozinho erra: o Telepad tem `Name()` em `usr` e mesmo assim e instalacao; as armas de
	///      fogo tem `Info()` em `oview` e mesmo assim sao pessoais. So o Telepad tem Bolt.
	///
	///   2. **ALGUM VERB EM `set src in usr`** -> PESSOAL. So aparece com o objeto DENTRO de voce.
	///
	///   3. **ALGUM VERB EM `set src in oview`** -> CHAO. `oview` exclui o proprio: so aparece com o
	///      objeto no mundo, do seu lado.
	///
	///   4. **NENHUM DOS DOIS** -> decide a ARVORE: `/obj/items/*` e o que a mochila do original
	///      carrega (`Inventory.dm:165` conta `for(var/obj/items/o in src)`), entao dentro dela e
	///      pessoal e fora dela e chao. E o que separa a Munição e o Livro (que ninguem instala) do
	///      Barco, da Furadeira e do Pad de Nave (que ninguem veste).
	///
	/// A subida da arvore PARA ANTES DE `/obj/items` -- ver a armadilha 3 do <see cref="DmVerbScanner"/>.
	/// =========================================================================================
	/// </summary>
	public static (bool Pessoal, string PorQue) Classificar(
		string typepath, Dictionary<string, VerbosDoTipo> verbos)
	{
		if (typepath.Length == 0) return (true, "sem typepath: nao ha prova de que va no chao");

		bool bolt = false, usr = false, chao = false;
		string p = typepath;
		while (true)
		{
			// A BASE NAO VOTA: os verbs de `/obj/items` sao o encanamento do inventario (Get, Drop,
			// Drop_All, Destroy), herdados por todo item do jogo.
			if (!string.Equals(p, DmVerbScanner.BaseDosItens, StringComparison.Ordinal)
				&& verbos.TryGetValue(p, out VerbosDoTipo? v))
			{
				bolt |= v.TemBolt;
				usr |= v.EmUsr;
				chao |= v.NoChao;
			}
			int barra = p.LastIndexOf('/');
			if (barra <= 0) break;
			p = p[..barra];
		}

		if (bolt) return (false, "tem o verb Bolt");
		if (usr) return (true, "verb em `set src in usr`");
		if (chao) return (false, "verb em `set src in oview`");

		return typepath.StartsWith(DmVerbScanner.BaseDosItens + "/", StringComparison.Ordinal)
			? (true, "sem verb proprio, mas mora em /obj/items")
			: (false, "sem verb proprio e fora de /obj/items");
	}

	public static (int comArte, List<string> semArte) Resolver(
		IEnumerable<ConstrucaoDef> defs, Dictionary<string, TurfDef> arvore,
		Dictionary<string, string> sprites, Dictionary<string, VerbosDoTipo> verbos)
	{
		int ok = 0;
		var faltando = new List<string>();

		foreach (ConstrucaoDef d in defs)
		{
			// O QUE FOI ERGUIDO MANDA. O `create_type` e quem tem a densidade de verdade; o proprio
			// Creatable so entra quando ele nao existe (varios se erguem como si mesmos).
			if (d.CreateType.Length > 0 && arvore.TryGetValue(d.CreateType, out TurfDef? posto))
			{
				d.Icone = posto.Icon ?? d.Icone;
				d.Estado = posto.IconState ?? d.Estado;
				d.Densa = posto.Density;
				d.PixelX = posto.PixelX;
				d.PixelY = posto.PixelY;
			}
			else if (arvore.TryGetValue("/obj/Creatables/" + d.Id, out TurfDef? proprio))
			{
				d.Icone = proprio.Icon ?? d.Icone;
				d.Estado = proprio.IconState ?? d.Estado;
				d.Densa = proprio.Density;
				d.PixelX = proprio.PixelX;
				d.PixelY = proprio.PixelY;
			}

			(d.Pessoal, d.PorQue) = Classificar(d.CreateType, verbos);

			d.Arte = DmAppearanceScanner.Resolver(sprites, d.Icone) ?? "";

			// A ARVORE GANHA O `pixel_x` QUE O `New()` DELA ESCREVE EM RUNTIME; pros outros tipos a conta
			// devolve nulo e fica o declarado, que veio da arvore de tipos ali em cima. Mora DEPOIS da
			// arte porque a largura sai da folha que o catalogo aponta. Ver `PixelXDeArvore`.
			d.PixelX = PixelXDeArvore(d.CreateType, FolhaDaArte(d.Arte)) ?? d.PixelX;

			if (d.Arte.Length > 0) ok++;
			else faltando.Add($"{d.Id} -> {d.Icone ?? "(sem icone)"}");
		}
		return (ok, faltando);
	}

	/// <summary>A raiz das arvores do original (`Modules/Turfs/Plants.dm:15`).</summary>
	private const string BaseDasArvores = "/obj/Trees";

	/// <summary>A unica arvore que REESCREVE o `pixel_x` no proprio `New()` (`Plants.dm:182-190`).</summary>
	private const string PalmeiraDaEsquerda = "/obj/Trees/PalmTreeLeft";

	/// <summary>
	/// DEFEITO INJETADO (bancada): a arvore volta a sair com o `pixel_x` DECLARADO do tipo (zero), sem a
	/// conta que o `New()` do DM faz em runtime. Falso na extracao, sempre.
	/// </summary>
	public static bool ArvoreSemCentroDeTeste;

	/// <summary>
	/// O `pixel_x` QUE O `New()` DE UMA ARVORE ESCREVE EM RUNTIME, ou nulo quando o tipo nao e arvore
	/// (ai vale o `pixel_x` DECLARADO, que o <see cref="DmTurfScanner"/> ja leu) ou nao ha folha pra medir.
	///
	/// ============================ O DESLOCAMENTO QUE NAO ESTA NA ARVORE DE TIPOS ============================
	/// Toda `/obj/Trees` e CENTRADA no tile pelo `New()` da base (`Modules/Turfs/Plants.dm:31-35`):
	///
	///     var/icon/I = icon(icon,icon_state)
	///     trees_pixelx_cache[ck] = 16 - I.Width()/2
	///     pixel_x = trees_pixelx_cache[ck]
	///
	/// Isso mora num CORPO DE PROC, e o leitor da arvore de tipos pula corpo de proc de proposito (o
	/// `New()` costuma escrever valor dinamico, que nao e a aparencia do tipo). So que aqui o valor e
	/// funcao pura da largura do icone -- e sem ele a macieira (`AppleTree`, folha de 61 px) saia com
	/// `px = 0`: o canto esquerdo do desenho colado no canto esquerdo do tile, 14,5 px a DIREITA de onde
	/// o BYOND a poe. E o desenho de TODA macieira do jogo, as dos mapas inclusive: o cliente as desloca
	/// pelo `px` deste catalogo.
	///
	/// A CONTA E TRANSCRITA, e nao extraida: sao duas linhas do DM, e acha-las lendo corpo de proc seria
	/// um segundo interpretador de DM pra duas contas. Conferido em 2026-10-09: debaixo de `obj/Trees` o
	/// `pixel_x` do proprio objeto so e escrito nesses dois `New()` (`Plants.dm:35` e `:189`); os outros
	/// `New()` da familia (AppleTree, SmallPine, BigHousePlant, Oak, RoundTree, Tree, TallBush) chamam o
	/// `..()` e so penduram overlay.
	///
	/// QUEM REESCREVE O `New()` COM OUTRA CONTA: a `PalmTreeLeft` (`Plants.dm:186-190`) chama o `..()` e
	/// depois escreve `pixel_x = I.Width()/2 - 16` -- o sinal trocado. A conta DELA vence a da base.
	///
	/// O 16 e o meio do tile de 32, escrito como numero no DM. O MEIO PIXEL FICA: `/` no DM e divisao de
	/// ponto flutuante, e 61/2 da 30,5. Arredondar aqui seria decidir por quem desenha.
	///
	/// A LARGURA SAI DA FOLHA QUE O CATALOGO APONTA (<paramref name="folha"/>: o `.png` ao lado do `.tres`,
	/// que traz o `width` do icone no chunk `Description` do `.dmi`). DIVERGENCIA DECLARADA: o DM mede o
	/// `.dmi` DELE. Da no mesmo enquanto a folha for a copia desse `.dmi` -- e o caso da `DecTrees`, que nao
	/// tem homonima em `Assets/Sprites`. Se um dia a arte resolvida for a de outra pasta com o mesmo nome
	/// (`DmAppearanceScanner.IndiceDeSprites` fica com a primeira), a arvore sai centrada pelo desenho que
	/// o cliente MOSTRA, que e o que "centrada" quer dizer na tela.
	///
	/// FORA DAQUI, de proposito -- contas parecidas em tipo que nao e `/obj/Trees` e nao esta no catalogo:
	/// `/obj/Reincarnation_Tree` (`Modules/Magic/ReincarnationTree.dm:9-12`, a mesma `16 - largura/2`),
	/// `/obj/Raw_Material/Lumber_Tree` (`Modules/Crafting/Woodcutting.dm:23-24`, `round((32 - largura)/2,1)`)
	/// e o efeito `mshenshin` (`Modules/DLC/MasterS.dm:101-104`, idem, num `EffectStart()`). Quem puser um
	/// deles em <see cref="MobiliaDeMapa"/> traz a conta dele pra ca.
	/// =========================================================================================================
	/// </summary>
	public static double? PixelXDeArvore(string tipo, string folha)
	{
		if (ArvoreSemCentroDeTeste || !EhOuDescende(tipo, BaseDasArvores)) return null;
		if (!File.Exists(folha) || DmiFile.Read(folha) is not { } dmi) return null;
		return EhOuDescende(tipo, PalmeiraDaEsquerda) ? dmi.IconWidth / 2.0 - 16 : 16 - dmi.IconWidth / 2.0;
	}

	/// <summary>
	/// O tipo E a raiz, ou mora debaixo dela? Por SEGMENTO do typepath, que e como o DM herda o `New()`:
	/// `/obj/Trees2` nao mora em `/obj/Trees`.
	/// </summary>
	private static bool EhOuDescende(string tipo, string raiz) =>
		tipo == raiz || tipo.StartsWith(raiz + "/", StringComparison.Ordinal);

	/// <summary>
	/// `res://Assets/Sprites/Trees/DecTrees.tres` -> o `.png` da mesma folha. O `res://` do indice de
	/// sprites e o caminho RELATIVO A PASTA DE TRABALHO (`DmAppearanceScanner.IndiceDeSprites`), entao
	/// sem o prefixo ele e um caminho que este mesmo processo abre. Arte vazia = sem folha.
	/// </summary>
	private static string FolhaDaArte(string arte) =>
		arte.StartsWith("res://", StringComparison.Ordinal)
			? Path.ChangeExtension(arte["res://".Length..], ".png")
			: "";

	public static string ParaJson(IEnumerable<ConstrucaoDef> defs)
	{
		var sb = new StringBuilder("[\n");
		bool primeiro = true;
		foreach (ConstrucaoDef d in defs.OrderBy(c => c.TechNecessario).ThenBy(c => c.Custo))
		{
			if (!primeiro) sb.Append(",\n");
			primeiro = false;
			sb.Append("  { ");
			sb.Append($"\"id\": {J(d.Id)}, \"nome\": {J(d.Nome)}, \"desc\": {J(d.Desc)}, ");
			sb.Append($"\"custo\": {d.Custo.ToString("0.##", CultureInfo.InvariantCulture)}, ");
			sb.Append($"\"tech\": {d.TechNecessario.ToString("0.##", CultureInfo.InvariantCulture)}, ");
			sb.Append($"\"racas\": [{string.Join(", ", d.Racas.Select(J))}], ");
			sb.Append($"\"arte\": {J(d.Arte)}, \"estado\": {J(d.Estado ?? "")}, ");
			sb.Append($"\"densa\": {(d.Densa ? 1 : 0)}, ");
			sb.Append($"\"px\": {d.PixelX.ToString("0.##", CultureInfo.InvariantCulture)}, ");
			sb.Append($"\"py\": {d.PixelY.ToString("0.##", CultureInfo.InvariantCulture)}, ");
			// O TYPEPATH DO QUE VAI PRO CHAO. E a chave que o conversor de mapa usa pra reconhecer
			// a mesma maquina dentro de um `.dmm` -- ver `CatalogoDeObras.PorTypepath`.
			sb.Append($"\"tipo\": {J(d.CreateType)}, ");
			// DE USO PESSOAL? -- a regra 2 do pedido do dono, extraida do DM. Ver `Classificar`.
			sb.Append($"\"pessoal\": {(d.Pessoal ? 1 : 0)}");
			sb.Append(" }");
		}
		return sb.Append("\n]\n").ToString();
	}

	private static string J(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
