namespace Jandirus.Tools;

/// <summary>O que os VERBS de um typepath dizem sobre onde aquele objeto e usado.</summary>
public sealed class VerbosDoTipo
{
	public string Path = "";

	/// <summary>Algum verb declarado AQUI abre com `set src in usr`.</summary>
	public bool EmUsr;

	/// <summary>Algum verb declarado AQUI abre com `set src in oview(...)`.</summary>
	public bool NoChao;

	/// <summary>Existe um verb chamado `Bolt` -- o "aparafusar no chao" do original.</summary>
	public bool TemBolt;
}

/// <summary>
/// LE O ESCOPO DOS VERBS de cada `/obj` do DM: `set src in usr`, `set src in oview(1)`, `view(1)`.
///
/// ============================ POR QUE ESSE DADO, E POR QUE ELE E O DADO CERTO ============================
/// A pergunta que este scanner existe pra responder e a REGRA 2 do pedido do dono: *"item que se poe
/// no chao ganha a acao INSTALAR; item de uso pessoal (scouter, armaduras, pesos) NAO ganha"*.
///
/// A tentacao era escrever a lista dos 99 ids a mao. Neste projeto lista a mao ja envelheceu tres
/// vezes -- e o proprio port ja tinha uma: o `CatalogoDeItens` classificava por AUSENCIA numa tabela
/// de nove linhas, o que dava "vai pro chao" pra **vinte e oito itens de uso pessoal**, a Armadura e
/// as nove armas de fogo entre eles. A Armadura e um dos tres exemplos que o dono citou pelo nome.
///
/// O ORIGINAL JA SABE A RESPOSTA, e ela esta no ESCOPO DO VERB -- que e como o BYOND decide de onde
/// um verb pode ser chamado:
///
///   * `set src in usr`      -> so aparece quando o objeto esta DENTRO de voce. E um item pessoal.
///   * `set src in oview(1)` -> `oview` **exclui o proprio**: so aparece quando o objeto esta no
///                              mundo, ao seu lado. E uma instalacao.
///   * `set src in view(1)`  -> `view` INCLUI o proprio: funciona nas duas situacoes. Nao separa
///                              nada, e por isso nao vota.
///
/// Ou seja: a resposta e um dado do DM, extraido, e nao uma opiniao digitada aqui.
/// ======================================================================================================
///
/// ============================ AS TRES ARMADILHAS QUE ELE PRECISOU TRATAR ============================
///   1. **O VERB TEM DUAS FORMAS DE DECLARACAO.** `verb/Bolt()` numa linha, e o bloco
///      `verb` seguido dos nomes indentados embaixo. Este projeto ja pagou por ver so uma das duas
///      (116 skills perdidas -- ver `dbclimax-port-extrator-buracos`), e aqui pagaria de novo: a
///      GELADEIRA declara `Deposit_Food`, `Withdraw_Food` e `Bolt` **na forma de bloco**, e lendo so
///      a forma de barra ela ficava sem verb nenhum e caia no lado errado.
///
///   2. **O BOLT MANDA MAIS QUE O `usr`.** O Telepad tem `Name()` em `set src in usr` (renomear
///      enquanto carrega) e `Set()`/`Bolt()` em `oview` -- ele e instalacao apesar do `usr`. As
///      armas de fogo sao o espelho: `fire()` em `usr` e `Info()` em `oview` -- e sao pessoais. O
///      que separa os dois casos e o `Bolt`, que no original **e** o gesto de fixar no chao (o port
///      ja o tem, em `GameServer.Tech.Aparafusar`).
///
///   3. **OS VERBS DA BASE `/obj/items` NAO CONTAM.** Ela declara `Get` (oview), `Drop` e `Drop_All`
///      (usr) e `Destroy` (view) -- o encanamento do inventario, herdado por TODOS os itens do jogo.
///      Herdar isso faria os 60 e poucos `/obj/items/*` votarem "usr" de uma vez e a regra devolveria
///      "pessoal" pra maquina de gravidade. Quem filtra e o <see cref="DmTechScanner"/>, que sobe a
///      arvore e para antes da base.
/// ==================================================================================================
/// </summary>
public static class DmVerbScanner
{
	/// <summary>
	/// A BASE DE TODO ITEM CARREGAVEL. Os verbs dela sao o encanamento do inventario e nao dizem
	/// nada sobre ESTE item -- ver a armadilha 3 no cabecalho.
	/// </summary>
	public const string BaseDosItens = "/obj/items";

	public static Dictionary<string, VerbosDoTipo> Scan(string codeRoot)
	{
		var defs = new Dictionary<string, VerbosDoTipo>(StringComparer.Ordinal);
		foreach (string arq in Directory.GetFiles(codeRoot, "*.dm", SearchOption.AllDirectories))
			Ler(arq, defs);
		return defs;
	}

	/// <summary>
	/// Um quadro da pilha de indentacao. `Verb` guarda de QUAL verb a linha atual faz parte -- sem
	/// ele o `set src in` seria anotado no tipo mas nao daria pra saber que o verb se chama `Bolt`.
	///
	/// `BlocoVerb` marca o cabecalho `verb` solto (a segunda forma de declaracao): o proximo nivel
	/// de indentacao ainda sao NOMES DE VERB, e nao subtipos.
	/// </summary>
	private readonly record struct Quadro(int Indent, string Path, bool EmProc, string? Verb, bool BlocoVerb);

	private static void Ler(string arq, Dictionary<string, VerbosDoTipo> defs)
	{
		var pilha = new List<Quadro>();

		foreach (string bruta in File.ReadLines(arq))
		{
			string sem = bruta.TrimEnd();
			if (sem.Trim().Length == 0) continue;
			if (sem.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;

			int ind = 0;
			while (ind < sem.Length && (sem[ind] == '\t' || sem[ind] == ' ')) ind++;
			string corpo = sem[ind..];
			int c = corpo.IndexOf("//", StringComparison.Ordinal);
			if (c >= 0) corpo = corpo[..c].TrimEnd();
			if (corpo.Length == 0) continue;

			while (pilha.Count > 0 && pilha[^1].Indent >= ind) pilha.RemoveAt(pilha.Count - 1);

			// RAIZ EXPLICITA e nao `default`: o `Path` de um `record struct` zerado e **null**, e a
			// primeira linha de todo arquivo do DM cai nele.
			Quadro pai = pilha.Count > 0 ? pilha[^1] : new Quadro(-1, "", false, null, false);

			// --- dentro do corpo de um verb: e aqui que mora o `set src in` ---
			if (pai.EmProc)
			{
				if (pai.Verb != null && pai.Path.StartsWith("/obj", StringComparison.Ordinal)
					&& corpo.StartsWith("set src in ", StringComparison.Ordinal))
				{
					string escopo = corpo["set src in ".Length..].TrimStart();
					VerbosDoTipo d = Get(defs, pai.Path);
					// `usr` e `oview` separam; `view` serve pras duas situacoes e nao vota.
					if (escopo.StartsWith("usr", StringComparison.Ordinal)) d.EmUsr = true;
					else if (escopo.StartsWith("oview", StringComparison.Ordinal)) d.NoChao = true;
				}
				pilha.Add(pai with { Indent = ind });
				continue;
			}

			// --- cabecalho `verb` / `proc` solto: NAO e um subtipo (armadilha 1) ---
			string cru = corpo.Trim().TrimEnd('/');
			if (cru is "verb" or "proc")
			{
				pilha.Add(new Quadro(ind, pai.Path, false, null, cru == "verb"));
				continue;
			}

			bool ehProc = corpo.Contains('(')
					   || corpo.StartsWith("var", StringComparison.Ordinal)
					   || corpo.StartsWith("if", StringComparison.Ordinal)
					   || corpo.StartsWith("for", StringComparison.Ordinal)
					   || corpo.Contains('=');
			if (ehProc)
			{
				(string? nome, bool semArgs) = NomeDoVerb(corpo, pai.BlocoVerb);
				if (nome != null && pai.Path.StartsWith("/obj", StringComparison.Ordinal))
				{
					VerbosDoTipo d = Get(defs, pai.Path);

					// ============================ O BOLT E O DESEMPATE, MAS SO O BOLT SEM ARGUMENTO ============================
					// Quem tem `verb/Bolt()` e coisa que se fixa no chao -- e o desempate da armadilha 2.
					//
					// O BOLTER E O CONTRA-EXEMPLO, e ele custou uma classificacao errada: `verb/Bolt(var/obj/O
					// in oview(1))` (`buildable.dm:213`). E uma ARMA, e o Bolt dela aparafusa OUTRA coisa --
					// o alvo vem por parametro. Lendo so o nome, a pistola virava mobilia.
					//
					// A distincao e mecanica e nao de gosto: `Bolt()` sem parametro so pode falar de si
					// mesmo (`src`); `Bolt(alvo)` fala de um terceiro.
					// ======================================================================================================
					if (semArgs && nome.Equals("Bolt", StringComparison.Ordinal)) d.TemBolt = true;
				}
				pilha.Add(new Quadro(ind, pai.Path, true, nome, false));
				continue;
			}

			// --- fragmento de typepath ---
			string frag = cru;
			string full = frag.StartsWith('/') ? frag
						: (pai.Path.Length > 0 ? pai.Path + "/" + frag : "/" + frag);
			pilha.Add(new Quadro(ind, full, false, null, false));
		}
	}

	/// <summary>
	/// O nome do verb nas DUAS formas: `verb/Bolt()` (barra) e `Bolt()` embaixo de um `verb` solto.
	/// Devolve null quando a linha e um `proc`, um `New()` ou qualquer outro corpo.
	///
	/// `SemArgs` diz se a lista de parametros esta vazia -- ver o Bolter no chamador.
	/// </summary>
	private static (string? Nome, bool SemArgs) NomeDoVerb(string corpo, bool dentroDeBlocoVerb)
	{
		int par = corpo.IndexOf('(');
		if (par <= 0) return (null, false);
		string cabeca = corpo[..par].Trim();

		if (cabeca.StartsWith("verb/", StringComparison.Ordinal))
			cabeca = cabeca["verb/".Length..];
		else if (!dentroDeBlocoVerb) return (null, false);

		if (cabeca.Length == 0 || !cabeca.All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
			return (null, false);

		int fecha = corpo.IndexOf(')', par);
		return (cabeca, fecha == par + 1);
	}

	private static VerbosDoTipo Get(Dictionary<string, VerbosDoTipo> defs, string path)
	{
		if (!defs.TryGetValue(path, out VerbosDoTipo? d))
			defs[path] = d = new VerbosDoTipo { Path = path };
		return d;
	}
}
