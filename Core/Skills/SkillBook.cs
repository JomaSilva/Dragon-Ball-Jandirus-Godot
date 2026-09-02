namespace Jandirus.Core.Skills;

/// <summary>
/// Por que uma skill nao pode ser aprendida agora. <see cref="Pode"/> = pode.
///
/// ============================ TRES MOTIVOS NOVOS, E O QUE CADA UM MANDA O JOGADOR FAZER ============================
/// A recusa e DADO pra tela, nao frase: "faltam marcos", "invista mais nesta arvore", "compre o
/// pre-requisito", "sua raca nunca vai poder" e "esta morta neste port" mandam a pessoa fazer coisas
/// diferentes, e o original engolia tudo num `enabled == 0` que sumia da vitrine.
///
///   * <see cref="TierTrancado"/>    -- a vitrine da arvore ainda nao chega neste tier (`HtmlUI.dm:820`):
///                                     falta INVESTIR na arvore. O veredito diz quanto.
///   * <see cref="AguardaAcendedor"/> -- nasce `enabled = 0`, nao tem pre-requisito, e uma regra de
///                                     `growbranches()` a acende quando uma condicao valer
///                                     (`Mind.dm:15`: `kiawarenessskill >= 1`). O veredito diz qual.
///   * <see cref="Apagada"/>          -- uma regra a APAGOU (`disableskill`): a especialidade de Ki que
///                                     voce nao escolheu, os raciais do Alien depois de 3 marcos.
///   * <see cref="Desligada"/>        -- nasce `enabled = 0`, sem pre-requisito, e NENHUMA regra de arvore
///                                     que a pendure a acende. No DM ela tambem nao acende (ou acende
///                                     por um canal que este port nao tem -- o censo diz qual).
/// ==================================================================================================================
/// </summary>
public enum Recusa
{
	Pode = 0,
	NaoExiste,
	JaSabe,
	Desligada,
	SoVilao,
	RacaOuClasse,
	SemArvore,
	FaltaPreRequisito,
	SemMarcos,
	TierTrancado,
	AguardaAcendedor,
	Apagada,
}

/// <summary>
/// A RESPOSTA COMPLETA de "posso aprender isto?" -- o motivo E os numeros que a tela precisa pra
/// dizer o que fazer. <see cref="SkillBook.PodeAprender"/> devolve so o <see cref="Motivo"/>.
/// </summary>
public sealed class Veredito
{
	public string Path = "";
	public Recusa Motivo;

	/// <summary>A arvore pela qual a resposta foi dada (a que aceita, ou a que esta mais perto de aceitar).</summary>
	public string Arvore = "";

	public int Custo;
	public int TierDaSkill;

	/// <summary>Em <see cref="Recusa.TierTrancado"/>: o tier que a arvore mostra hoje e quanto ja foi investido nela.</summary>
	public int TierDaArvore, Investido;

	/// <summary>
	/// Em <see cref="Recusa.TierTrancado"/>: quantos marcos ainda faltam investir NESTA arvore pra
	/// vitrine chegar no tier da skill. -1 = nenhum degrau de investimento chega la (o tier sobe por
	/// outra coisa, ou nao sobe).
	/// </summary>
	public int FaltaInvestir;

	/// <summary>Em <see cref="Recusa.FaltaPreRequisito"/>: os pre-requisitos que ainda nao sao seus.</summary>
	public string[] PreReqsFaltando = [];

	/// <summary>
	/// Em <see cref="Recusa.AguardaAcendedor"/> e <see cref="Recusa.Apagada"/>: a condicao da regra,
	/// como o extrator a escreveu (`kiawarenessskill>=1`, `effspec==0`). Texto do DM, nao frase.
	/// </summary>
	public string Acendedor = "";

	/// <summary>Em <see cref="Recusa.SemMarcos"/>: quantos faltam.</summary>
	public int FaltamMarcos;

	internal Veredito Com(Recusa r) { Motivo = r; return this; }
}

/// <summary>
/// O ESTADO DE UMA ARVORE PRA ESTE PERSONAGEM -- o que o `growbranches()` dela produziu.
///
/// E o que viaja no pacote `S2C.Skills`: o servidor calcula com os contadores da ficha na mao, o
/// cliente recebe e roda o MESMO <see cref="SkillBook.Avaliar"/> sobre isto. Mandar o RESULTADO, e
/// nao os contadores, e decisao (ver `GameServer.MandarSkills`).
/// </summary>
public sealed class EstadoDeArvore
{
	public string Path = "";

	/// <summary>O `allowedtier` de agora: a vitrine mostra skill de tier ate aqui.</summary>
	public int Tier;

	/// <summary>Marcos investidos nesta arvore (soma do custo das skills dela que sao suas e nao foram ensinadas).</summary>
	public int Investido;

	/// <summary>O proximo degrau de tier por investimento: investir ate X pra chegar no tier Y. 0 = nao ha.</summary>
	public int ProximoInvestir, ProximoTier;

	/// <summary>Skills desta arvore que nasceram `enabled = 0` e uma regra ACENDEU.</summary>
	public HashSet<string> Acesas { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Skills desta arvore que uma regra APAGOU (`disableskill`). Ganha da acesa.</summary>
	public HashSet<string> Apagadas { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// O QUE ESTE PERSONAGEM APRENDEU, e quanto ainda pode gastar.
///
/// Vive no CORE porque as duas pontas precisam da mesma resposta: o cliente pra pintar o botao
/// de "aprender" (e nao prometer o que o servidor vai recusar) e o servidor pra decidir de
/// verdade. Sao as mesmas regras, na mesma funcao -- e o que evita duas implementacoes
/// divergirem em silencio.
///
/// MARCOS SAO A MOEDA. O original chama de `skillpoints` e mantem DOIS contadores: quantos voce
/// ja ganhou na vida e quantos ainda tem pra gastar. Os dois importam -- o total e progressao
/// (nao volta atras), o saldo e escolha.
///
/// ============================ O ESTADO DAS ARVORES MORA AQUI TAMBEM ============================
/// Alem do que foi aprendido, o livro carrega o que o `growbranches()` de cada arvore produziu
/// (<see cref="Arvores"/>, <see cref="Destravadas"/>): o tier de vitrine, as skills acesas e apagadas,
/// as arvores que o progresso abriu. No servidor isso e CALCULADO (<see cref="Recalcular"/>, com os
/// contadores da ficha); no cliente e RECEBIDO (<see cref="CarregarEstado"/>, do pacote). O
/// <see cref="Avaliar"/> le esse estado e e o mesmo nas duas pontas.
/// ==============================================================================================
/// </summary>
public sealed class SkillBook
{
	private readonly HashSet<string> _aprendidas = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// O QUE ALGUEM ME ENSINOU (subconjunto de <see cref="_aprendidas"/>).
	///
	/// ============================ UM CONJUNTO SO PRA OS DOIS BITS DO DM ============================
	/// O original tem DUAS variaveis por skill, e elas so aparecem juntas, nas duas linhas seguidas
	/// que o `Study` escreve (`teachable.dm:53-54`):
	///
	///     nS.teacher   = FALSE   // o aluno NAO repassa
	///     nS.wastaught = TRUE    // e pode esquecer o que foi ensinado
	///
	/// Nunca uma sem a outra, em lugar nenhum do jogo. Sao portanto **a mesma informacao vista de
	/// dois lados**: "esta copia veio de um mestre". Guardar dois campos aqui seria a mesma verdade
	/// escrita duas vezes, com uma delas fadada a envelhecer -- e a pergunta que o jogo faz e sempre
	/// a mesma (`FoiEnsinada`), com o "nao repassa" derivado dela em
	/// <see cref="EnsinoDeSkill.PodeRepassar"/>.
	///
	/// **E ISTO E O QUE IMPEDE UMA SKILL RARA DE VIRAR CORRENTE.** Sem esta marca, o primeiro
	/// Kamehameha ensinado se espalharia pelo servidor inteiro em uma tarde.
	/// ============================================================================================
	/// </summary>
	private readonly HashSet<string> _ensinadas = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Quantos marcos este personagem JA GANHOU na vida. So sobe.</summary>
	public int MarcosTotais;

	/// <summary>Quantos ainda estao pra gastar.</summary>
	public int MarcosLivres;

	public IReadOnlyCollection<string> Aprendidas => _aprendidas;
	public bool Sabe(string path) => _aprendidas.Contains(path);

	public void Conceder(int marcos)
	{
		if (marcos <= 0) return;
		MarcosTotais += marcos;
		MarcosLivres += marcos;
	}

	/// <summary>Aprende sem cobrar nada. E o caminho de quem foi ENSINADO, nao de quem comprou.</summary>
	public void Dar(string path) { _aprendidas.Add(path); _versao++; }

	/// <summary>
	/// APRENDEU PORQUE ALGUEM ENSINOU -- as duas linhas do `Study` (`teachable.dm:53-54`) numa so.
	///
	/// Existe separada do <see cref="Dar"/> porque `Dar` tem outros seis chamadores (a concessao do
	/// admin, o Ki liberado, os moldes de NPC, a bancada) e nenhum deles e ensino: marcar todos
	/// eles como ensinados travaria o repasse de skill que ninguem ensinou a ninguem.
	/// </summary>
	public void DarComoEnsinada(string path)
	{
		_aprendidas.Add(path);
		_ensinadas.Add(path);
		_versao++;
	}

	/// <summary>Esta copia veio de um mestre? (o `wastaught` do DM.)</summary>
	public bool FoiEnsinada(string path) => _ensinadas.Contains(path);

	/// <summary>As que vieram de mestre. So o save le isto.</summary>
	public IReadOnlyCollection<string> Ensinadas => _ensinadas;

	/// <summary>
	/// ESQUECE. **Tira das duas listas** -- e a segunda linha nao e zelo: o DM DELETA o datum
	/// (`skill.dm:79`), e com ele o `wastaught`. Deixar a marca pra tras faria a skill voltar
	/// "ensinada" no dia em que a pessoa a comprasse de verdade com os proprios marcos, e ela
	/// continuaria sem poder repassar uma coisa que ninguem lhe deu.
	///
	/// NAO REEMBOLSA e NAO encolhe a arvore: e a metade "apagar do livro". A outra metade -- o
	/// `refund()` + `treeshrink()` do DM -- e <see cref="EsquecerEReembolsar"/>.
	/// </summary>
	public void Esquecer(string path)
	{
		_aprendidas.Remove(path);
		_ensinadas.Remove(path);
		// A CASA ESCOLHIDA MORRE JUNTO, pelo mesmo motivo do `wastaught`: guardar a escolha de uma
		// skill esquecida faria os buffs dela voltarem sozinhos no dia em que alguem a comprasse
		// de novo, sem passar pela pergunta.
		_escolhas.Remove(path);
		_versao++;
	}

	public void Carregar(IEnumerable<string> paths)
	{
		_aprendidas.Clear();
		foreach (string p in paths) _aprendidas.Add(p);
		_versao++;
	}

	// =====================================================================
	// A ESCOLHA UNICA
	// =====================================================================
	private readonly Dictionary<string, int> _escolhas = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// QUAL CASA o dono escolheu em cada skill de escolha unica (1-based, como o `chosen` do DM).
	///
	/// UMA SKILL NO JOGO usa isto -- a `Great Robotic Alliance` (`meta.dm:104-125`). Ela nao mora
	/// nas <see cref="Aprendidas"/> porque nao e "sabe / nao sabe": e um dado A MAIS sobre uma
	/// skill que ele ja sabe, do mesmo jeito que <see cref="Ensinadas"/> e.
	///
	/// SEM ESCOLHA REGISTRADA A SKILL NAO RENDE NADA, e isso e fiel: no DM os buffs moram dentro
	/// do `switch(input(...))` e sem resposta nenhuma casa entra.
	/// </summary>
	public IReadOnlyDictionary<string, int> Escolhas => _escolhas;

	/// <summary>Registra a casa escolhida. Devolve false se a skill nem e de escolha unica.</summary>
	public bool Escolher(SkillCatalog cat, string path, int casa)
	{
		Skill? s = cat.Get(path);
		if (s == null || s.Escolhas.Length == 0) return false;
		if (casa < 1 || casa > s.Escolhas.Length) return false;
		if (!_aprendidas.Contains(path)) return false;
		_escolhas[path] = casa;
		return true;
	}

	/// <summary>
	/// Le do save. **DEPOIS do <see cref="Carregar"/>**, pelo mesmo motivo do
	/// <see cref="CarregarEnsinadas"/>: escolha de skill que o livro nao tem mais e escolha orfa,
	/// e ela reapareceria como buff de uma skill esquecida.
	/// </summary>
	public void CarregarEscolhas(IEnumerable<KeyValuePair<string, int>> pares)
	{
		_escolhas.Clear();
		foreach ((string p, int c) in pares) if (_aprendidas.Contains(p)) _escolhas[p] = c;
	}

	// =====================================================================
	// O GANHO NA COMPRA -- o razao do que a compra somou na ficha
	// =====================================================================
	private readonly Dictionary<string, Dictionary<string, double>> _ganhos = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// O QUE CADA SKILL SOMOU NA FICHA AO SER COMPRADA (typepath -> campo -> quanto). E o `storedBP`
	/// / `hiddenpot` do datum do DM (Bodybuilding.dm:85-86), guardado aqui porque o datum morre no
	/// esquecimento e o numero tem que sobreviver ate la. Ver <see cref="GanhoNaCompra"/>.
	///
	/// PERSISTE (`CharacterSave.SkillsGanhos`), senao esquecer a skill depois de um relog devolveria
	/// zero -- ou, pior, o `Aplicar` somaria de novo por nao achar o registro.
	/// </summary>
	public IReadOnlyDictionary<string, Dictionary<string, double>> GanhosNaCompra => _ganhos;

	public void RegistrarGanho(string path, Dictionary<string, double> somou) =>
		_ganhos[path] = new Dictionary<string, double>(somou, StringComparer.Ordinal);

	public void EsquecerGanho(string path) => _ganhos.Remove(path);

	/// <summary>Le do save. DEPOIS do <see cref="Carregar"/>: ganho de skill que o livro nao tem e orfao.</summary>
	public void CarregarGanhos(IEnumerable<KeyValuePair<string, Dictionary<string, double>>> pares)
	{
		_ganhos.Clear();
		foreach ((string p, Dictionary<string, double> g) in pares)
			if (_aprendidas.Contains(p) && g != null) _ganhos[p] = new Dictionary<string, double>(g, StringComparer.Ordinal);
	}

	/// <summary>
	/// Le do save quais copias vieram de mestre. **Chamada DEPOIS do <see cref="Carregar"/>**, e o
	/// filtro existe porque um save pode ter perdido a skill sem perder a marca (skill removida do
	/// catalogo, admin que apagou): marca orfa faria o livro achar que ele "foi ensinado" numa
	/// coisa que ele nem sabe.
	/// </summary>
	public void CarregarEnsinadas(IEnumerable<string> paths)
	{
		_ensinadas.Clear();
		foreach (string p in paths) if (_aprendidas.Contains(p)) _ensinadas.Add(p);
		_versao++;
	}

	// =====================================================================
	// O ESTADO DAS ARVORES -- o `growbranches()` de cada uma, calculado ou recebido
	// =====================================================================
	/// <summary>
	/// ARVORES DESTRAVADAS PELO PROGRESSO -- as que nao vem da raca nem da classe, e sim de uma
	/// regra `enabletree()` de outra arvore (`Body.dm:25-31`, `Bodybuilding.dm:17`, `Mind.dm:17`...).
	///
	/// Sem isto o catalogo era so estatico: as arvores que voce tem no nascimento, e mais nada
	/// pra sempre. Metade da progressao fisica do jogo (Bodybuilding, Martial Skill, Wrestling,
	/// Assassain, Cultivation) e as tres arvores de Ki ficam ATRAS desta porta.
	/// </summary>
	public HashSet<string> Destravadas { get; } = new(StringComparer.OrdinalIgnoreCase);

	private readonly List<EstadoDeArvore> _arvores = [];

	/// <summary>O estado de cada arvore que este personagem possui, na ordem raca/classe e depois as destravadas.</summary>
	public IReadOnlyList<EstadoDeArvore> Arvores => _arvores;

	public EstadoDeArvore? Arvore(string path) =>
		_arvores.Find(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));

	// O estado envelhece quando o livro muda; `_versao` e o relogio, e `_versaoDoEstado` diz de
	// quando e o estado calculado. Estado que veio do SERVIDOR (cliente) nunca e recalculado aqui.
	private int _versao, _versaoDoEstado = -1;
	private string _chaveDoEstado = "";
	private bool _estadoDoServidor;

	/// <summary>
	/// O ULTIMO CONTEXTO usado: o leitor de contadores do servidor le a ficha AO VIVO (a lambda de
	/// `ContextoDeRegra.De`), entao reaproveita-lo num recalculo preguicoso ve os numeros de agora.
	/// Sem isto, um `Dar()` seguido de `PodeAprender()` recalcularia sem contador nenhum e fecharia
	/// arvore que estava aberta -- calado.
	/// </summary>
	private ContextoDeRegra? _contexto;

	/// <summary>
	/// RECALCULA O QUE AS REGRAS DAS ARVORES PRODUZEM -- o `testunlocks()` do original
	/// (`SkillTreesWindow.dm:306`: `for(T in possessed_trees) T.growbranches()`).
	///
	/// ============================ A ORDEM E UMA FILA, PORQUE ARVORE ABRE ARVORE ============================
	/// Body abre Bodybuilding, Bodybuilding abre Wrestling; Body abre Martial Skill, que abre
	/// Assassain; Mind abre Effusive Mastery, que abre Effusive Specialty. Uma passada so sobre as
	/// arvores de nascenca pararia no primeiro elo. A fila comeca com as da raca e da classe e
	/// cresce com o que cada `enabletree` abrir; `Destravadas` fica so com o que NAO era de nascenca.
	///
	/// Chamado depois de qualquer mudanca na ficha ou no livro (o servidor chama no
	/// `AplicarEfeitos`, DEPOIS de os contadores serem escritos -- a ordem importa, ver la).
	/// ===================================================================================================
	/// </summary>
	public void Recalcular(SkillCatalog cat, ContextoDeRegra ctx, string raca, string classe)
	{
		_contexto = ctx;
		_estadoDoServidor = false;
		Destravadas.Clear();
		_arvores.Clear();

		var fila = new Queue<Skill>(cat.ArvoresDe(raca, classe));
		var deNascenca = new HashSet<string>(fila.Select(a => a.Path), StringComparer.OrdinalIgnoreCase);
		var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var abertas = new List<string>();

		while (fila.Count > 0)
		{
			Skill arv = fila.Dequeue();
			if (!vistas.Add(arv.Path)) continue;
			abertas.Clear();
			_arvores.Add(AvaliarArvore(cat, arv, ctx, abertas));
			foreach (string p in abertas)
			{
				if (cat.Get(p) is not { Arvore: true } outra) continue;
				if (!deNascenca.Contains(p)) Destravadas.Add(p);
				fila.Enqueue(outra);
			}
		}

		_versaoDoEstado = _versao;
		_chaveDoEstado = raca + "|" + classe;
	}

	/// <summary>O `growbranches()` de UMA arvore, regra por regra, na ordem do DM.</summary>
	private EstadoDeArvore AvaliarArvore(SkillCatalog cat, Skill arv, ContextoDeRegra ctx, List<string> abertas)
	{
		var e = new EstadoDeArvore { Path = arv.Path, Investido = Investido(cat, arv), Tier = arv.TierInicial };
		ctx.Invested = e.Investido;

		foreach (RegraDeArvore r in arv.RegrasDeGalho)
		{
			if (r.Vale(ctx) != true) continue;
			switch (r.Tipo)
			{
				case TipoDeRegra.Tier: e.Tier = r.TierAlvo; break;
				case TipoDeRegra.AbreArvore: abertas.Add(r.Alvo); break;
				// `enableskill`/`disableskill` so alcancam GALHOS DESTA arvore (`trees.dm:172-181`
				// varre `constituentskills`): regra que aponta pra fora nao pega em nada -- e o
				// caso do growbranches da Ki Buff Mastery, escrito na arvore errada
				case TipoDeRegra.Acende:
					foreach (string g in Alvos(arv, r.Alvo)) { e.Acesas.Add(g); e.Apagadas.Remove(g); }
					break;
				case TipoDeRegra.Apaga:
					foreach (string g in Alvos(arv, r.Alvo)) { e.Apagadas.Add(g); e.Acesas.Remove(g); }
					break;
			}
		}

		// ============================ O `enableskill` DISPARADO POR DEGRAU ============================
		// `if(level == 100) enableskill(/datum/skill/mind/Advanced_Ki_Awareness)` (Mind.dm:186) e
		// o UNICO acendedor das Advanced_*/Perfect_* de Ki -- 55 folhas saiam do censo como "sem
		// acendedor (mortas neste port)" porque o degrau estava extraido (`destrava` no
		// `niveis.json`) e ninguem o lia. Entra DEPOIS das regras de arvore e NAO desapaga: no DM o
		// `enableskill` do effector roda uma vez, no tique da subida, e o `disableskill` do prune
		// roda a cada `testunlocks()` -- quem apaga depois ganha.
		//
		// DIVERGENCIA DECLARADA: no DM o `enableskill` de `/datum/skill/mind` procura o alvo na
		// arvore `ptree`, que e SEMPRE a Mind (Mind.dm:40 e ninguem sobrescreve). Pra Basic_Ki_Effusion
		// (arvore effusionmas) acender Advanced_Ki_Effusion o alvo teria que ser galho da MIND -- e
		// nao e, entao no original as cadeias de Effusion/Buff/Specialty nunca acendem (a da Mind e
		// a Advanced_Targeted, que por acaso pende da Mind, acendem). Este port acende o alvo em
		// QUALQUER arvore do personagem que o pendure: e o que a linha do DM claramente quis dizer,
		// e o defeito do `ptree` seria copiado sem que nenhuma tela o denunciasse.
		// ============================================================================================
		foreach (string p in ctx.DestravadasPorDegrau())
			foreach (string g in Alvos(arv, p))
				if (!e.Apagadas.Contains(g)) e.Acesas.Add(g);

		// O PROXIMO DEGRAU: o menor investimento que sobe o tier acima do de agora. E o que a tela
		// mostra como "invista mais N nesta arvore".
		foreach (RegraDeArvore r in arv.RegrasDeGalho)
		{
			if (r.Tipo != TipoDeRegra.Tier || r.TierAlvo <= e.Tier) continue;
			if (r.InvestidoMinimo is not { } m || m <= e.Investido) continue;
			if (e.ProximoInvestir == 0 || m < e.ProximoInvestir || (m == e.ProximoInvestir && r.TierAlvo > e.ProximoTier))
			{
				e.ProximoInvestir = m;
				e.ProximoTier = r.TierAlvo;
			}
		}
		return e;
	}

	private static IEnumerable<string> Alvos(Skill arv, string alvo)
	{
		if (alvo == "*") return arv.Galhos;
		return Array.Exists(arv.Galhos, g => string.Equals(g, alvo, StringComparison.OrdinalIgnoreCase)) ? [alvo] : [];
	}

	/// <summary>
	/// QUANTO FOI INVESTIDO NESTA ARVORE -- o `invested` do DM (`trees.dm:20-22`: a soma do
	/// `skillcost` das `investedskills`).
	///
	/// O DM sabe por qual arvore cada skill foi COMPRADA (`fund()` poe em `investedskills`). Este
	/// livro nao guarda isso, e guardar mudaria a forma do save -- entao a conta e: skill sua, que
	/// pende desta arvore, e que ninguem te ensinou (o `Study` nao passa pelo `fund`,
	/// `teachable.dm:53-56`). Duas divergencias, as duas pro lado de ABRIR mais:
	///   * skill pendurada em duas arvores suas conta nas duas;
	///   * skill dada por admin (ou pelo Ki liberado da bancada) conta como comprada.
	/// </summary>
	public int Investido(SkillCatalog cat, Skill arv)
	{
		int total = 0;
		foreach (string g in arv.Galhos)
		{
			if (!Sabe(g) || FoiEnsinada(g)) continue;
			if (cat.Get(g) is { Arvore: false } s) total += SkillCatalog.CustoDe(s);
		}
		return total;
	}

	/// <summary>
	/// O ESTADO VEIO DO SERVIDOR (o pacote `S2C.Skills`). A partir daqui este livro nao recalcula
	/// nada sozinho: o cliente nao tem os contadores da ficha, e o servidor e a verdade.
	/// </summary>
	public void CarregarEstado(IEnumerable<string> destravadas, IEnumerable<EstadoDeArvore> arvores)
	{
		Destravadas.Clear();
		foreach (string p in destravadas) Destravadas.Add(p);
		_arvores.Clear();
		_arvores.AddRange(arvores);
		_estadoDoServidor = true;
		_versaoDoEstado = _versao;
	}

	/// <summary>
	/// UMA LINHA QUE MUDA SEMPRE QUE O ESTADO DAS ARVORES MUDA -- a assinatura de cache das duas pontas.
	///
	/// O servidor a usa pra decidir se o pacote `S2C.Skills` precisa sair de novo, e o cliente pra
	/// decidir se a aba de aprendizado precisa ser remontada. E a MESMA pergunta ("o que as regras
	/// produziram mudou?"), e por isso e uma funcao so: a versao do servidor morava num `private
	/// static` dele, e a do cliente nasceria como segunda copia -- a que envelhece calada no dia em
	/// que o estado ganhar um campo novo.
	/// </summary>
	public string AssinaturaDasArvores()
	{
		var sb = new System.Text.StringBuilder();
		foreach (string p in Destravadas) sb.Append(p).Append(',');
		foreach (EstadoDeArvore e in _arvores)
			sb.Append(e.Path).Append(':').Append(e.Tier).Append('/').Append(e.Investido)
			  .Append('/').Append(e.Acesas.Count).Append('/').Append(e.Apagadas.Count).Append(';');
		return sb.ToString();
	}

	/// <summary>
	/// O estado esta em dia? Se nao, recalcula com o ultimo contexto (ou com nenhum -- e o caso
	/// das bancadas de mesa e do robo do cliente, que nao tem ficha: as arvores abrem so pelo
	/// investimento e o tier sobe, mas contador nenhum e lido).
	/// </summary>
	private void GarantirEstado(SkillCatalog cat, string raca, string classe)
	{
		if (_estadoDoServidor) return;
		if (_versaoDoEstado == _versao && _chaveDoEstado == raca + "|" + classe) return;
		Recalcular(cat, _contexto ?? ContextoDeRegra.Vazio(raca, classe), raca, classe);
	}

	// =====================================================================
	// O VEREDITO
	// =====================================================================
	/// <summary>
	/// Pode aprender ESTA skill AGORA? So o motivo -- ver <see cref="Avaliar"/> pros numeros.
	/// </summary>
	public Recusa PodeAprender(SkillCatalog cat, string path, string raca, string classe, bool vilao) =>
		Avaliar(cat, path, raca, classe, vilao).Motivo;

	/// <summary>
	/// A RESPOSTA COMPLETA. A ordem das checagens e a da vitrine do original (aprendida, vilao,
	/// raca/classe, arvore, tier, `enabled`, pre-requisito, custo), e ela importa pra mensagem:
	/// dizer "faltam marcos" pra uma skill que a raca dele nunca vai poder aprender manda a pessoa
	/// juntar marcos a toa.
	///
	/// ============================ O `enabled = 0` LIDO COMO O DM LE ============================
	/// `enabled = 0` NAO e "desligada" (`skill.dm:26`). Ha quatro jeitos de uma skill nascida
	/// apagada acender, e os quatro estao aqui:
	///   1. ela tem PRE-REQUISITO: o `testskillprereqs()` a acende quando ele entra (`trees.dm:28-36`).
	///      Aqui isso e so o proprio teste de pre-requisito;
	///   2. ela e SO-VILAO sem pre-requisito: acende com o bit de vilao (`trees.dm:37-43`);
	///   3. uma regra `enableskill` da arvore que a pendura valeu (<see cref="EstadoDeArvore.Acesas"/>);
	///   4. nada disso: e <see cref="Recusa.Desligada"/> de verdade, e o censo diz por que.
	/// E `disableskill` (<see cref="EstadoDeArvore.Apagadas"/>) ganha de todos.
	/// ==========================================================================================
	/// </summary>
	public Veredito Avaliar(SkillCatalog cat, string path, string raca, string classe, bool vilao)
	{
		var v = new Veredito { Path = path };
		Skill? s = cat.Get(path);
		if (s == null || s.Arvore) return v.Com(Recusa.NaoExiste);
		v.Custo = SkillCatalog.CustoDe(s);
		v.TierDaSkill = s.Tier;

		if (Sabe(path)) return v.Com(Recusa.JaSabe);
		if (s.SoVilao && !vilao) return v.Com(Recusa.SoVilao);
		if (!cat.Permitida(s, raca, classe, vilao)) return v.Com(Recusa.RacaOuClasse);

		// A ARVORE E O GATE DE VERDADE. No original a skill nao esta solta no mundo: ela pende
		// de uma arvore, e a arvore vem da raca (`generatetrees`) ou do progresso (`enabletree`).
		// Sem esta checagem, um humano compraria a regeneracao namekuseijin so por ela nao
		// declarar restricao de raca.
		GarantirEstado(cat, raca, classe);
		var donas = new List<EstadoDeArvore>();
		foreach (EstadoDeArvore e in _arvores)
			if (cat.Get(e.Path) is { } arv && Array.Exists(arv.Galhos, g => string.Equals(g, path, StringComparison.OrdinalIgnoreCase)))
				donas.Add(e);
		if (donas.Count == 0) return v.Com(Recusa.SemArvore);

		// UMA SKILL PODE PENDER DE DUAS ARVORES SUAS (Body Expansion: Body e a racial do Alien). A
		// vitrine do DM mostra a skill em cada arvore que a admite; basta UMA admitir. Quando
		// nenhuma admite, a recusa e a da arvore que esta mais perto de admitir.
		EstadoDeArvore? aceita = null;
		EstadoDeArvore? presaNoTier = null;
		int menorFalta = int.MaxValue;
		EstadoDeArvore? apagadaEm = null;
		EstadoDeArvore? semAcender = null;
		string acendedor = "";

		foreach (EstadoDeArvore dona in donas)
		{
			if (dona.Apagadas.Contains(path)) { apagadaEm ??= dona; continue; }

			if (s.Tier > dona.Tier)
			{
				int falta = FaltaInvestir(cat, dona, s.Tier);
				int chave = falta < 0 ? int.MaxValue - 1 : falta;
				if (chave < menorFalta) { menorFalta = chave; presaNoTier = dona; }
				continue;
			}

			bool acesa = s.Ligada || s.PreReqs.Length > 0 || (s.SoVilao && vilao) || dona.Acesas.Contains(path);
			if (!acesa)
			{
				if (semAcender == null || acendedor.Length == 0)
				{
					semAcender = dona;
					acendedor = AcendedorDe(cat, dona, path);
				}
				continue;
			}
			aceita = dona;
			break;
		}

		if (aceita == null)
		{
			if (semAcender != null)
			{
				v.Arvore = semAcender.Path;
				v.TierDaArvore = semAcender.Tier;
				v.Investido = semAcender.Investido;
				v.Acendedor = acendedor;
				return v.Com(acendedor.Length > 0 ? Recusa.AguardaAcendedor : Recusa.Desligada);
			}
			if (presaNoTier != null)
			{
				v.Arvore = presaNoTier.Path;
				v.TierDaArvore = presaNoTier.Tier;
				v.Investido = presaNoTier.Investido;
				v.FaltaInvestir = FaltaInvestir(cat, presaNoTier, s.Tier);
				return v.Com(Recusa.TierTrancado);
			}
			v.Arvore = apagadaEm!.Path;
			v.Acendedor = ApagadorDe(cat, apagadaEm, path);
			return v.Com(Recusa.Apagada);
		}

		v.Arvore = aceita.Path;
		v.TierDaArvore = aceita.Tier;
		v.Investido = aceita.Investido;

		if (!SkillCatalog.PreReqsOk(s, _aprendidas))
		{
			v.PreReqsFaltando = [.. s.PreReqs.Where(p => !_aprendidas.Contains(p))];
			return v.Com(Recusa.FaltaPreRequisito);
		}
		if (MarcosLivres < v.Custo)
		{
			v.FaltamMarcos = v.Custo - MarcosLivres;
			return v.Com(Recusa.SemMarcos);
		}
		return v.Com(Recusa.Pode);
	}

	/// <summary>
	/// Quantos marcos faltam investir na arvore pra vitrine chegar em <paramref name="tier"/>. -1 se
	/// nenhum degrau de investimento chega la.
	/// </summary>
	public static int FaltaInvestir(SkillCatalog cat, EstadoDeArvore dona, int tier)
	{
		if (cat.Get(dona.Path) is not { } arv) return -1;
		int? menor = null;
		foreach (RegraDeArvore r in arv.RegrasDeGalho)
		{
			if (r.Tipo != TipoDeRegra.Tier || r.TierAlvo < tier) continue;
			if (r.InvestidoMinimo is not { } m) continue;
			if (menor == null || m < menor) menor = m;
		}
		return menor is { } n ? Math.Max(1, n - dona.Investido) : -1;
	}

	/// <summary>
	/// As condicoes das regras `acende` desta arvore que apontam pra skill (ou pra `*`) -- e, quando
	/// um DEGRAU de outra skill a acende, a frase "'X' chega ao nivel N", pra que o veredito diga o
	/// que fazer em vez de "morta neste port".
	///
	/// O REGISTRO DE NIVEIS TEM QUE ESTAR CARREGADO NAS DUAS PONTAS (`RegrasDoDisco.Carregar` do
	/// `niveis.json`): o cliente le o RESULTADO (Acesas) e acerta a resposta, mas a FRASE sai daqui,
	/// e sem o registro ele dizia "sem acendedor" (Desligada) pra uma skill que abre no nivel 100 --
	/// ver `MenuJogo.CarregarNiveisNoCliente`.
	///
	/// A frase e escrita NA GRAMATICA DO EXTRATOR, que e o que o `NomesLegiveis.Condicao` traduz: o
	/// nome da skill vai entre aspas simples -- a string literal da gramatica (`Rank=='Demon Lord'`),
	/// copiada verbatim pela tela. Solto, cada palavra do nome passaria pelo tradutor de
	/// identificadores, e "Basic Debuff Mastery" sairia "Basic De Mastery" (o `Campo()` tira o sufixo
	/// `buff`). O servidor imprime a frase crua no chat, aspas e tudo.
	/// </summary>
	public static string AcendedorDe(SkillCatalog cat, EstadoDeArvore dona, string path)
	{
		string cond = CondicoesDe(cat, dona, path, TipoDeRegra.Acende);
		var partes = new List<string>();
		if (cond.Length > 0) partes.Add(cond);
		foreach (RegrasDeNivel.AcendedorPorDegrau a in RegrasDeNivel.DestravadaPor(path))
			partes.Add($"'{cat.Get(a.Path)?.Nome ?? a.Path}' chega ao nivel {a.Nivel}");
		return string.Join(" || ", partes);
	}

	public static string ApagadorDe(SkillCatalog cat, EstadoDeArvore dona, string path) =>
		CondicoesDe(cat, dona, path, TipoDeRegra.Apaga);

	private static string CondicoesDe(SkillCatalog cat, EstadoDeArvore dona, string path, TipoDeRegra tipo)
	{
		if (cat.Get(dona.Path) is not { } arv) return "";
		var conds = new List<string>();
		foreach (RegraDeArvore r in arv.RegrasDeGalho)
		{
			if (r.Tipo != tipo) continue;
			if (r.Alvo != "*" && !string.Equals(r.Alvo, path, StringComparison.OrdinalIgnoreCase)) continue;
			string c = r.Condicao.Length > 0 ? r.Condicao : "sempre";
			if (!conds.Contains(c)) conds.Add(c);
		}
		return string.Join(" || ", conds);
	}

	/// <summary>Compra a skill. Devolve o motivo da recusa quando nao da.</summary>
	public Recusa Aprender(SkillCatalog cat, string path, string raca, string classe, bool vilao)
	{
		Recusa r = PodeAprender(cat, path, raca, classe, vilao);
		if (r != Recusa.Pode) return r;
		MarcosLivres -= SkillCatalog.CustoDe(cat.Get(path)!);
		_aprendidas.Add(path);
		_versao++;
		return Recusa.Pode;
	}

	// =====================================================================
	// ESQUECER, REEMBOLSAR, ENCOLHER
	// =====================================================================
	/// <summary>
	/// ESQUECE E DEVOLVE OS MARCOS -- o `refund()` do DM (`trees.dm:93-98`: `invested -= skillcost`,
	/// `S.forget()` devolve os `skillpoints`, e `treeshrink()`), e devolve o que a arvore encolhendo
	/// levou junto.
	///
	/// So reembolsa o que foi COMPRADO: copia ensinada nao custou marco (`Study`, `teachable.dm:41`),
	/// e devolver marco por ela seria uma impressora -- ver <see cref="EnsinoDeSkill.Esquecer"/>.
	/// </summary>
	public List<string> EsquecerEReembolsar(SkillCatalog cat, string path, string raca, string classe)
	{
		if (!Sabe(path)) return [];
		bool comprada = !FoiEnsinada(path);
		Esquecer(path);
		if (comprada && cat.Get(path) is { Arvore: false } s) MarcosLivres += SkillCatalog.CustoDe(s);
		return Encolher(cat, raca, classe);
	}

	/// <summary>
	/// A ARVORE ENCOLHEU -- o `treeshrink()` do DM (`trees.dm:119-135`): com menos investido o tier
	/// cai, e toda skill sua que ficou ACIMA do tier da arvore (ou cujo pre-requisito saiu) e
	/// esquecida e reembolsada, em cascata, ate nada mais cair.
	///
	/// Skill pendurada em duas arvores so cai se NENHUMA das duas a admite mais; `can_forget = FALSE`
	/// nunca cai (`Spirit Unleashed`, as formas). Copia ensinada nao cai por tier -- nao foi
	/// investida em arvore nenhuma.
	///
	/// NAO RODA NO LOGIN, e isso e fiel: o DM so encolhe no `refund`. Um save antigo que comprou
	/// Afterimage no primeiro milissegundo (o defeito que este arquivo conserta) fica com ele.
	/// </summary>
	public List<string> Encolher(SkillCatalog cat, string raca, string classe)
	{
		var cascata = new List<string>();
		for (int volta = 0; volta < 64; volta++)
		{
			Recalcular(cat, _contexto ?? ContextoDeRegra.Vazio(raca, classe), raca, classe);
			string? cai = null;
			foreach (EstadoDeArvore e in _arvores)
			{
				if (cat.Get(e.Path) is not { } arv) continue;
				foreach (string g in arv.Galhos)
				{
					if (!Sabe(g) || FoiEnsinada(g)) continue;
					if (cat.Get(g) is not { Arvore: false, Esquecivel: true } s) continue;

					bool algumaAdmite = false;
					foreach (EstadoDeArvore d in _arvores)
						if (cat.Get(d.Path) is { } a2 && Array.Exists(a2.Galhos, x => string.Equals(x, g, StringComparison.OrdinalIgnoreCase))
							&& s.Tier <= d.Tier) { algumaAdmite = true; break; }

					if (!algumaAdmite || !SkillCatalog.PreReqsOk(s, _aprendidas)) { cai = g; break; }
				}
				if (cai != null) break;
			}
			if (cai == null) break;

			Esquecer(cai);
			if (cat.Get(cai) is { } caiu) MarcosLivres += SkillCatalog.CustoDe(caiu);
			cascata.Add(cai);
		}
		return cascata;
	}

	/// <summary>Esta skill pende de alguma arvore que este personagem possui?</summary>
	public bool PenduraEmArvoreDe(SkillCatalog cat, Skill s, string raca, string classe)
	{
		GarantirEstado(cat, raca, classe);
		foreach (EstadoDeArvore e in _arvores)
			if (cat.Get(e.Path) is { } arv && Array.IndexOf(arv.Galhos, s.Path) >= 0) return true;

		// Skill SOLTA (nao pendurada em arvore nenhuma do catalogo) e ensinada, nao comprada:
		// Kaio-ken vem do Senhor Kaioh, a Genkidama tambem. Se ninguem a pendurou, so o
		// caminho do ensino a concede -- e `Dar()` nao passa por aqui.
		return false;
	}

	/// <summary>O que da pra comprar agora, em ordem de arvore e tier. E o que a aba mostra.</summary>
	public IEnumerable<(Skill Skill, Recusa Estado)> Ofertas(SkillCatalog cat, string raca, string classe, bool vilao)
	{
		GarantirEstado(cat, raca, classe);
		var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		// materializa: quem consome `Ofertas` compra no meio da iteracao, e comprar recalcula `_arvores`
		var arvores = new List<Skill>();
		foreach (EstadoDeArvore e in _arvores) if (cat.Get(e.Path) is { } a) arvores.Add(a);

		foreach (Skill arv in arvores)
			foreach (string p in arv.Galhos)
			{
				Skill? s = cat.Get(p);
				if (s == null || s.Nome.Length == 0 || !vistas.Add(p)) continue;
				yield return (s, PodeAprender(cat, p, raca, classe, vilao));
			}
	}
}
