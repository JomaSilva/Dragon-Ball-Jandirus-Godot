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
/// </summary>
public static class DmTechScanner
{
	private static readonly Regex RxNum = new(@"^-?[0-9.]+$", RegexOptions.Compiled);
	private static readonly Regex RxStr = new("\"(?<s>[^\"]*)\"", RegexOptions.Compiled);

	/// <summary>No DM o .dmi vai entre ASPAS SIMPLES (`icon = 'ResearchBench.dmi'`).</summary>
	private static readonly Regex RxIcone = new("'(?<s>[^']*)'", RegexOptions.Compiled);

	public static List<ConstrucaoDef> Scan(string pastaCode)
	{
		var todas = new List<ConstrucaoDef>();
		foreach (string arq in Directory.GetFiles(pastaCode, "*.dm", SearchOption.AllDirectories))
			Ler(arq, todas);

		// SO INTERESSA O QUE TEM PRECO OU REQUISITO. `obj/Creatables` tambem e usado como base
		// abstrata e por blocos que so declaram icone; sem preco nem requisito nao ha o que vender.
		var lista = todas.Where(c => c.Custo > 0 || c.TechNecessario > 0).ToList();
		lista.AddRange(MobiliaDeMapa());
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
	];

	private static void Ler(string arq, List<ConstrucaoDef> saida)
	{
		string[] linhas = File.ReadAllLines(arq);
		int indRaiz = -1;                 // indentacao da linha `obj/Creatables`
		ConstrucaoDef? atual = null;
		int indAtual = -1;

		for (int i = 0; i < linhas.Length; i++)
		{
			string linha = linhas[i].TrimEnd();
			if (linha.Length == 0) continue;

			int ind = 0;
			while (ind < linha.Length && linha[ind] == '\t') ind++;
			string corpo = linha[ind..];
			if (corpo.StartsWith("//", StringComparison.Ordinal)) continue;

			// abriu um bloco de construiveis?
			if (corpo is "obj/Creatables" or "/obj/Creatables") { indRaiz = ind; atual = null; continue; }
			if (indRaiz < 0) continue;
			if (ind <= indRaiz) { indRaiz = -1; atual = null; continue; }

			// uma entrada nova: identificador sozinho, um nivel dentro do bloco
			if (ind == indRaiz + 1 && corpo.All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
			{
				atual = new ConstrucaoDef { Id = corpo, Nome = corpo.Replace('_', ' '), Arquivo = Path.GetFileName(arq) };
				saida.Add(atual);
				indAtual = ind;
				continue;
			}

			if (atual == null || ind <= indAtual) continue;

			int igual = corpo.IndexOf('=');
			if (igual <= 0) continue;
			string chave = corpo[..igual].Trim();
			string valor = corpo[(igual + 1)..].Trim();
			int comentario = valor.IndexOf("//", StringComparison.Ordinal);
			if (comentario >= 0) valor = valor[..comentario].Trim();

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
				case "allowedRaces":
					foreach (Match m in RxStr.Matches(valor)) atual.Racas.Add(m.Groups["s"].Value); break;
				// o typepath do que vai pro chao. Sem aspas no DM: `create_type = /obj/Technology/X`
				case "create_type":
					atual.CreateType = valor.Trim(); break;
				// o icone do PROPRIO Creatable serve de reserva: nem toda entrada tem create_type
				// (varias sao `/obj/buildables`, que se ergue como si mesmo).
				case "icon":
					if (RxIcone.Match(valor) is { Success: true } mi) atual.Icone ??= mi.Groups["s"].Value; break;
				case "icon_state":
					if (RxStr.Match(valor) is { Success: true } mis) atual.Estado ??= mis.Groups["s"].Value; break;
			}
		}
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
	public static (int comArte, List<string> semArte) Resolver(
		IEnumerable<ConstrucaoDef> defs, Dictionary<string, TurfDef> arvore, Dictionary<string, string> sprites)
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

			d.Arte = DmAppearanceScanner.Resolver(sprites, d.Icone) ?? "";
			if (d.Arte.Length > 0) ok++;
			else faltando.Add($"{d.Id} -> {d.Icone ?? "(sem icone)"}");
		}
		return (ok, faltando);
	}

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
			sb.Append($"\"tipo\": {J(d.CreateType)}");
			sb.Append(" }");
		}
		return sb.Append("\n]\n").ToString();
	}

	private static string J(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
