namespace Jandirus.Core.Forms;

/// <summary>As duas disciplinas divinas. Ver <see cref="Disciplinas"/> -- elas se EXCLUEM.</summary>
public enum TipoDeDisciplina
{
	UltraInstinct,
	PoderDaDestruicao,
}

/// <summary>Uma habilidade que uma faixa de maestria destrava.</summary>
/// <param name="Pct">Maestria REAL que abre.</param>
/// <param name="Nome">Como o jogador a conhece.</param>
/// <param name="Ativa">Ativa (o jogador aperta) ou passiva (acontece sozinha).</param>
/// <param name="Forma">Id no <see cref="Catalogo"/> quando o degrau E uma forma. Vazio = nao e.</param>
public readonly record struct Degrau(double Pct, string Nome, bool Ativa, string Desc, string Forma = "");

/// <summary>
/// A FICHA DE UMA DISCIPLINA -- o que muda entre o Ultra Instinct e o Poder da Destruicao.
///
/// O resto (as duas energias, o dreno, a regeneracao, o crescimento em combate, o ensino em
/// cadeia) e IDENTICO nos dois, e o proprio DM diz isso na primeira linha do `UltraEgo.dm`:
/// *"o espelho do Ultra Instinct, mesma logica, disciplina oposta"*. Por isso ha uma ficha e um
/// motor, e nao dois sistemas paralelos que vao divergir na primeira correcao.
/// </summary>
public sealed class DisciplinaDef
{
	public required TipoDeDisciplina Tipo;
	public required string Id;
	public required string Nome;

	/// <summary>
	/// A CHAVE do cargo que sempre possui a disciplina -- quem inicia a cadeia de ensino.
	///
	/// E a `Chave` do <c>Core/Ranks/Ranks.cs</c> ("angel", "godofdestruction"), nao o nome de tela:
	/// o nome muda com traducao, a chave nao.
	/// </summary>
	public required string RankQueEnsina;

	/// <summary>Nome do estado ligado -- o toggle da faixa 0%.</summary>
	public required string NomeDoToggle;

	/// <summary>As cinco faixas, em ordem.</summary>
	public required Degrau[] Degraus;

	/// <summary>Energia ATUAL perdida por segundo com o toggle ligado, FORA de forma.</summary>
	public double DrenoLigado = 0.5;

	/// <summary>Energia ATUAL perdida por segundo TRANSFORMADO. Menor que o do toggle, e do DM.</summary>
	public double DrenoNaForma = 0.35;

	/// <summary>Energia ATUAL recuperada por segundo com TUDO desligado e fora de combate.</summary>
	public double Regen = 0.25;

	/// <summary>Maestria REAL por segundo de uso EM COMBATE. 0,04 = ~40 min de luta pra 100%.</summary>
	public double RealPorSegundo = 0.04;

	/// <summary>Maestria REAL por gatilho da disciplina (esquiva do UI, proc da aura do UE).</summary>
	public double RealPorGatilho = 0.2;

	/// <summary>Energia ATUAL com que cada forma comeca. Transformar RENOVA a precisao.</summary>
	public double AtualAoEntrarNaPrimeiraForma = 80, AtualAoEntrarNaSegundaForma = 100;

	/// <summary>O maior degrau que esta maestria REAL destravou. Nulo = nenhum.</summary>
	public Degrau? Maior(double real)
	{
		Degrau? melhor = null;
		foreach (Degrau d in Degraus)
			if (real >= d.Pct && (melhor == null || d.Pct > melhor.Value.Pct)) melhor = d;
		return melhor;
	}

	/// <summary>Esta maestria REAL destrava aquele degrau?</summary>
	public bool Destravou(double real, string nome) =>
		Degraus.Any(d => d.Nome == nome && real >= d.Pct);

	/// <summary>O proximo degrau a partir desta maestria. Nulo = ja tem tudo.</summary>
	public Degrau? Proximo(double real)
	{
		Degrau? melhor = null;
		foreach (Degrau d in Degraus)
			if (real < d.Pct && (melhor == null || d.Pct < melhor.Value.Pct)) melhor = d;
		return melhor;
	}
}

/// <summary>
/// AS DUAS DISCIPLINAS DIVINAS, portadas de `Code/Modules/Skills/UltraInstinct.dm` e
/// `Code/Modules/Skills/UltraEgo.dm`.
///
/// ============================ O DESENHO, EM UMA IDEIA ============================
/// **Duas energias, e elas fazem coisas diferentes.** E o que separa este sistema de toda outra
/// progressao do jogo, e o cabecalho do DM gasta vinte linhas explicando:
///
///   * **REAL** (0-100) -- a maestria PERMANENTE. Cresce por USO EM COMBATE. Ela nao entra em
///     nenhuma conta de poder: ela so DESTRAVA as cinco faixas.
///   * **ATUAL** (0-100) -- a precisao do momento. E ela que ESCALA os bonus. DRENA enquanto a
///     disciplina esta ligada, e drena mais rapido ainda dentro das formas. **Nao recupera em
///     combate.**
///
/// Isso e o oposto de "quanto mais eu treino, mais forte eu fico o tempo todo": o instinto CANSA.
/// Uma luta longa deixa a esquiva pior, e a unica forma de renovar a precisao no meio da briga e
/// transformar -- que e o que o DM faz (Sign devolve 80%, Perfected devolve 100%).
///
/// **E as duas se excluem.** Ninguem tem as duas escolas; a memoria do projeto ja registrava isso
/// ("PATHS EXCLUSIVOS (UI xor UE)"), e o <see cref="Catalogo.LinhasAbertas"/> ja implementava a
/// metade das formas. Aqui ela vale pra skill inteira.
/// ==============================================================================
/// </summary>
public static class Disciplinas
{
	/// <summary>
	/// Maestria de God Ki pra APRENDER qualquer uma das duas. `GODKI_UIUE_LEARN_PCT 70`.
	///
	/// O DM diz "God Ki tier 1+", mas os tiers morreram num rework anterior deste projeto e viraram
	/// porcentagem -- o define de la ja e o numero novo.
	/// </summary>
	public const double GodKiParaAprender = Catalogo.GodkiUiUePct;

	// ==================================================================================
	// ULTRA INSTINCT -- a tecnica dos Anjos
	// ==================================================================================

	public static readonly DisciplinaDef UltraInstinct = new()
	{
		Tipo = TipoDeDisciplina.UltraInstinct,
		Id = "ultra_instinct",
		Nome = "Ultra Instinto",
		RankQueEnsina = "angel",
		NomeDoToggle = "Autonomous Evasion",
		DrenoLigado = 0.5,        // UI_DRAIN_ACTIVE
		DrenoNaForma = 0.35,      // UI_DRAIN_FORM
		Regen = 0.25,             // UI_REGEN_PROF
		RealPorSegundo = 0.04,    // UI_REAL_PER_SEC
		RealPorGatilho = 0.2,     // UI_REAL_PER_EVADE
		AtualAoEntrarNaPrimeiraForma = 80,   // UI_PROF_SIGN_START
		AtualAoEntrarNaSegundaForma = 100,   // UI_PROF_PERF_START
		Degraus =
		[
			new(0, "Autonomous Evasion", false,
				"O corpo desvia sozinho, sem a mente mandar. Chance de esquiva escala com a precisao ATUAL."),
			new(UiSign, "Ultra Instinct -Sign-", false,
				"A forma do instinto desperto: 60x. Entrar renova a precisao pra 80%.", "ui_sign"),
			new(UiAccel, "Accelerated Battle", false,
				"A esquiva passa a valer TAMBEM enquanto voce carrega Ki -- carregar deixa de ser se expor."),
			new(UiPerf, "Perfected Ultra Instinct", false,
				"O instinto completo: 66x, cabelo prateado. Entrar renova a precisao pra 100%.", "ui_perfected"),
			new(UiGd, "Godly Display", true,
				"Avanca marcando todos no caminho; o segundo toque em 5s despeja 10 golpes divididos entre os marcados."),
		],
	};

	/// <summary>As faixas do Ultra Instinct. `UI_UNLOCK_*`.</summary>
	public const double UiSign = 20, UiAccel = 40, UiPerf = 60, UiGd = 80;

	/// <summary>
	/// CHANCE DE ESQUIVA AUTONOMA, em porcento. `UI_EVADE_MELEE_PCT 0.45` / `UI_EVADE_BLAST_PCT 0.30`.
	///
	/// Escala pela precisao **ATUAL**, nao pela REAL: com 100% de precisao sao 45% contra socos e 30%
	/// contra ki. E a precisao esta caindo o tempo todo -- o instinto no fim de uma luta longa e
	/// muito pior que no comeco, que e a ideia inteira.
	/// </summary>
	public const double EsquivaMelee = 0.45, EsquivaKi = 0.30;

	public static double ChanceDeEsquiva(double precisaoAtual, bool contraKi) =>
		Math.Clamp(precisaoAtual, 0, 100) * (contraKi ? EsquivaKi : EsquivaMelee);

	/// <summary>
	/// O BONUS POS-ESQUIVA: +5% de ataque fisico e de velocidade por 5s, ate 5 pilhas.
	/// `UI_EVADE_BONUS` / `UI_EVADE_BUFF_TICKS` / `UI_EVADE_STACK_MAX`.
	///
	/// Empilhar premia quem esta esquivando MUITO -- e o unico jeito de o instinto virar ofensiva.
	/// </summary>
	public const double EsquivaBonus = 0.05, EsquivaBonusSegundos = 5;
	public const int EsquivaPilhaMax = 5;

	/// <summary>O Godly Display. `UI_GD_*`.</summary>
	public const double GdAlcanceTiles = 10, GdJanelaSegundos = 5, GdRecargaSegundos = 60;
	public const int GdGolpes = 10;
	public const double GdDanoPorGolpe = 0.5, GdBloqueioMult = 0.35;
	public const double GdCustoKiPct = 8, GdCustoPrecisao = 10;

	// ==================================================================================
	// PODER DA DESTRUICAO -- a escola do Deus da Destruicao
	// ==================================================================================

	public static readonly DisciplinaDef PoderDaDestruicao = new()
	{
		Tipo = TipoDeDisciplina.PoderDaDestruicao,
		Id = "poder_destruicao",
		Nome = "Poder da Destruicao",
		RankQueEnsina = "godofdestruction",
		NomeDoToggle = "Aura of Destruction",
		DrenoLigado = 0.5,        // UE_DRAIN_ACTIVE
		DrenoNaForma = 0.35,      // UE_DRAIN_FORM
		Regen = 0.25,             // UE_REGEN
		RealPorSegundo = 0.04,    // UE_REAL_PER_SEC
		RealPorGatilho = 0.1,     // UE_REAL_PER_PROC -- METADE do UI, e do DM
		AtualAoEntrarNaPrimeiraForma = 80,   // UE_ENERGY_D_START
		AtualAoEntrarNaSegundaForma = 100,   // UE_ENERGY_U_START
		Degraus =
		[
			new(0, "Aura of Destruction", false,
				"Quem te acerta se machuca; ataques de ki perdem forca; voce sofre menos dano; e o "
				+ "golpe FATAL e negado uma vez por luta, com a aura estourando em area."),
			new(UeDestroyer, "Destroyer Form", false,
				"A forma da destruicao: 60x. Entrar renova a energia pra 80%.", "destroyer"),
			new(UeEgo, "Unbound Ego", false,
				"Cada membro FERIDO da ate +5% de BP -- quanto mais perto de quebrar, maior. Todos os "
				+ "seis no pico ao mesmo tempo: +20% e devolve 25% de Ki e vigor."),
			new(UeUltra, "Ultra Ego", false,
				"O ego que se alimenta da dor: 66x. Entrar renova a energia pra 100%.", "ultra_ego"),
			new(UeHakai, "Hakai Infusion", true,
				"Um minuto de ataques de ki infundidos de Hakai: penetram mais, o alvo defende pela "
				+ "metade, e num embate de beams o seu DEVORA o do rival a cada 5s."),
		],
	};

	/// <summary>As faixas do Poder da Destruicao. `UE_UNLOCK_*`.</summary>
	public const double UeDestroyer = 20, UeEgo = 40, UeUltra = 60, UeHakai = 80;

	/// <summary>
	/// ============================ O TITULO AMPLIFICA O CAMINHO: +25% ============================
	/// `GOD_PATH_BOOST 0.25` (`1A Defines.dm:47`): *"GoD que trilha a DESTRUICAO: todos os beneficios
	/// do Power of Destruction x (1 + isto)"*. Quem porta o trono E trilhou a disciplina tem as duas
	/// formas amplificadas -- `ue_form_mult()` (`UltraEgo.dm:544-547`) le exatamente estas duas
	/// condicoes: `M.ue_learned && god_is_god(M)`.
	///
	/// Na pratica: Destroyer 60x -> 75x, Ultra Ego 66x -> 82,5x, **enquanto o titulo estiver na mao**.
	/// O DM re-afirma isso no `Loop()` do buff (`:558`) justamente porque o titulo pode cair no meio da
	/// luta; aqui quem re-afirma e o `AplicarForma`, que roda no mesmo ritmo.
	///
	/// ============================ POR QUE A REGRA MORA AQUI E NAO NO CATALOGO ============================
	/// O `Catalogo` **nao conhece cargo**, e isso ja e decisao escrita (ver `VermelhoDoCabeloDivino`,
	/// onde as duas variantes de cabelo por cargo do DM ficaram de fora pelo mesmo motivo). A
	/// disciplina, sim: ela ja carrega o <see cref="DisciplinaDef.RankQueEnsina"/>. Entao o NUMERO e a
	/// regra ficam no Core, e quem sabe o cargo (o servidor, que le a conta) so responde as duas
	/// perguntas.
	/// ================================================================================================
	///
	/// FICA DE FORA, E ESTA ANOTADO: o `ue_kit_factor()` (`UltraEgo.dm:190-191`) espalha o MESMO 0,25
	/// por todo o resto do kit (recuo, ki devorado, reducao, explosao, Unbound Ego, Hakai) e ainda da
	/// `GOD_PATH_BORROW` a um Deus da Destruicao que trilhou o Ultra Instinct. Isso e sistema proprio e
	/// nao veio nesta passada -- o que veio e o multiplicador das duas FORMAS, que era o buraco medido.
	/// </summary>
	public const double AmpliacaoDoCargo = 0.25;

	/// <summary>
	/// O fator que multiplica as formas do Poder da Destruicao. Ver <see cref="AmpliacaoDoCargo"/>.
	/// </summary>
	/// <param name="portaOTitulo">E o Deus da Destruicao AGORA (o titulo, nao o passado).</param>
	/// <param name="trilhouADisciplina">`ue_learned`: aprendeu o Poder da Destruicao.</param>
	public static double FatorDoCargo(bool portaOTitulo, bool trilhouADisciplina) =>
		portaOTitulo && trilhouADisciplina ? 1 + AmpliacaoDoCargo : 1;

	// --- A AURA OF DESTRUCTION: quatro efeitos, todos escalados pela energia ATUAL ---
	//
	// Cada um e "base + por_energia * energia". Com energia cheia (100) os numeros do DM batem:
	// recuo 1+4 = 5%, ki devorado 20+15 = 35%, reducao 15+10 = 25%, explosao 50+50 = 100%.

	/// <summary>Recuo no atacante MELEE: % do dano que ele causou volta pra ele. `UE_RECOIL_*`.</summary>
	public const double RecuoBase = 1, RecuoPorEnergia = 0.04;

	/// <summary>Ataque de KI que te acerta perde esta % de forca. `UE_KIEAT_*`.</summary>
	public const double KiDevoradoBase = 20, KiDevoradoPorEnergia = 0.15;

	/// <summary>Reducao de dano geral, em %. `UE_DR_*`.</summary>
	public const double ReducaoBase = 15, ReducaoPorEnergia = 0.10;

	/// <summary>
	/// A DESTRUCTION EXPLOSION: % do ULTIMO golpe recebido, num raio de 3 tiles. `UE_EXP_*`.
	///
	/// Ela nao e um ataque que se usa: ela e o que acontece quando o golpe FATAL e negado -- uma vez
	/// por luta. O DM chama de death-save, e o custo de perde-la e a proxima morte ser real.
	/// </summary>
	public const double ExplosaoBase = 50, ExplosaoPorEnergia = 0.5, ExplosaoRaioTiles = 3;

	/// <summary>base + porEnergia * energia, limitado a [0, 100].</summary>
	public static double PelaEnergia(double baseValor, double porEnergia, double energia) =>
		baseValor + porEnergia * Math.Clamp(energia, 0, 100);

	public static double Recuo(double energia) => PelaEnergia(RecuoBase, RecuoPorEnergia, energia);
	public static double KiDevorado(double energia) => PelaEnergia(KiDevoradoBase, KiDevoradoPorEnergia, energia);
	public static double ReducaoDeDano(double energia) => PelaEnergia(ReducaoBase, ReducaoPorEnergia, energia);
	public static double Explosao(double energia) => PelaEnergia(ExplosaoBase, ExplosaoPorEnergia, energia);

	// --- O UNBOUND EGO ---

	/// <summary>Bonus MAXIMO de BP por membro ferido, em %. `UE_EGO_PART_MAX 5`.</summary>
	public const double EgoPorMembro = 5;

	/// <summary>Membro no "pico" = perdeu 90%+ da vida SEM quebrar. `UE_EGO_PEAK_MISSING 0.9`.</summary>
	public const double EgoPico = 0.9;

	/// <summary>Todos os membros no pico ao mesmo tempo. `UE_EGO_ALL_BONUS` / `UE_EGO_RESTORE`.</summary>
	public const double EgoTodosBonus = 20, EgoRestaura = 25;

	/// <summary>
	/// O MULTIPLICADOR DO UNBOUND EGO a partir das fracoes de vida de cada membro.
	///
	/// ============================ POR QUE MEMBRO QUEBRADO NAO CONTA ============================
	/// O bonus cresce conforme o membro se aproxima de quebrar e some quando ele quebra. Parece
	/// perverso e e o ponto: o Ultra Ego premia quem esta APANHANDO E DE PE, nao quem esta em
	/// pedacos. Quebrar o braco do inimigo deixa de ser so dano -- e tirar o combustivel dele.
	/// =====================================================================================
	/// </summary>
	/// <param name="fracoes">Fracao de vida de cada membro (0-1).</param>
	/// <param name="quebrados">Se cada membro esta quebrado ou decepado (nao rende bonus).</param>
	/// <param name="todosNoPico">Sai true quando TODOS os membros vivos estao no pico.</param>
	public static double UnboundEgo(IReadOnlyList<double> fracoes, IReadOnlyList<bool> quebrados,
									out bool todosNoPico)
	{
		double bonus = 0;
		int vivos = 0, noPico = 0;

		for (int i = 0; i < fracoes.Count; i++)
		{
			if (i < quebrados.Count && quebrados[i]) continue;
			vivos++;

			// "perdeu" e o quanto falta pra vida cheia. O bonus e proporcional a isso, ate o teto.
			double perdeu = Math.Clamp(1 - fracoes[i], 0, 1);
			bonus += EgoPorMembro * perdeu;
			if (perdeu >= EgoPico) noPico++;
		}

		todosNoPico = vivos > 0 && noPico == vivos;
		if (todosNoPico) bonus += EgoTodosBonus;
		return 1 + bonus / 100.0;
	}

	/// <summary>A Hakai Infusion. `UE_HKI_*`.</summary>
	public const double HakaiCustoKiPct = 5, HakaiDuracaoSegundos = 60, HakaiRecargaSegundos = 30;
	public const double HakaiPenetracao = 1.05, HakaiDeflectDoAlvo = 0.5;
	public const double HakaiEmbateKi = 0.05, HakaiEmbatePush = 8;

	// ==================================================================================
	// AS CONSULTAS
	// ==================================================================================

	public static readonly DisciplinaDef[] Todas = [UltraInstinct, PoderDaDestruicao];

	public static DisciplinaDef? Def(TipoDeDisciplina t) => Array.Find(Todas, d => d.Tipo == t);

	/// <summary>
	/// ESTA FORMA PERTENCE A UMA DISCIPLINA? Devolve a disciplina **e a faixa** que a concede.
	/// Nulo = a forma e da escada comum (SSJ, Legendary, Mistico...).
	///
	/// ============================ A UNICA DERIVACAO, PRAS QUATRO FORMAS ============================
	/// O dono: *"o ultra instinct e perfected ultra instinct estao ganhando maestria como
	/// transformacoes, ambas essas formas sao da skill de ultra instinct, entao usar elas aumenta a
	/// maestria na SKILL e nao nelas em si"* -- e depois *"sim pode mexer no ultra ego e destroyer tb
	/// e a mesma coisa"*. Quatro formas, DUAS disciplinas, e por isso a pergunta que este metodo faz
	/// NAO e "o id e `ui_sign` ou `ui_perfected`" (uma lista, que envelhece no proximo degrau) nem
	/// "a linha e UltraInstinct" (que precisaria de um segundo ramo pro Ultra Ego). E
	/// **"alguma disciplina reclama esta forma?"**, e a resposta ja estava no dado: cada
	/// <see cref="Degrau"/> carrega o <see cref="Degrau.Forma"/> que ele concede.
	///
	/// Consequencia: um degrau novo de qualquer disciplina -- inclusive de uma TERCEIRA que venha a
	/// existir -- nasce ja sem maestria propria e ja creditando a proficiencia certa, sem ninguem
	/// vir aqui escrever o nome dele. E cada disciplina credita a PROPRIA: quem devolve a ficha e o
	/// laco, entao a Destroyer nunca sobe o Instinto.
	///
	/// O `DisciplinasBench` confere que toda forma com porta de disciplina no catalogo
	/// (`PedeProficienciaUi`/`PedeEnergiaUe`) e reclamada aqui -- uma forma que passasse pela porta
	/// sem faixa acumularia maestria morta em silencio, que e o defeito que este rework remove.
	/// ==========================================================================================
	/// </summary>
	public static (DisciplinaDef Def, Degrau Faixa)? DaForma(string? idDaForma)
	{
		if (string.IsNullOrEmpty(idDaForma)) return null;

		foreach (DisciplinaDef def in Todas)
			foreach (Degrau g in def.Degraus)
				if (string.Equals(g.Forma, idDaForma, StringComparison.Ordinal)) return (def, g);
		return null;
	}

	/// <summary>
	/// O NUMERO DA DISCIPLINA NO FIO -- o mesmo do <c>Protocol.AtributosState.Disciplina</c>
	/// (0 = nenhuma, 1 = Ultra Instinto, 2 = Poder da Destruicao).
	///
	/// Existe porque o cliente precisa cruzar "de que disciplina e esta forma" (que sai do
	/// <see cref="DaForma"/>, em tipo) com "que disciplina este corpo trilhou" (que chega em byte).
	/// Sem um funil os dois lados escreveriam o mapeamento a mao, e um deles envelheceria calado no
	/// dia em que uma terceira disciplina existisse.
	/// </summary>
	public static byte Rede(TipoDeDisciplina t) =>
		t == TipoDeDisciplina.UltraInstinct ? (byte)1 : (byte)2;

	public static DisciplinaDef? Def(string id) =>
		Array.Find(Todas, d => string.Equals(d.Id, id, StringComparison.Ordinal));

	/// <summary>
	/// A OUTRA disciplina -- a que aprender esta te fecha.
	///
	/// Existe pra a mensagem de recusa poder DIZER o que aconteceu. "Voce nao pode aprender isso"
	/// manda a pessoa procurar um requisito que ela nunca vai achar; "voce ja trilhou o Ultra
	/// Instinto, e as duas disciplinas se excluem" encerra a duvida.
	/// </summary>
	public static TipoDeDisciplina Oposta(TipoDeDisciplina t) =>
		t == TipoDeDisciplina.UltraInstinct ? TipoDeDisciplina.PoderDaDestruicao
											: TipoDeDisciplina.UltraInstinct;
}

/// <summary>
/// O ESTADO DE UMA DISCIPLINA NUM PERSONAGEM. Vai pro save inteiro.
///
/// As formas NAO moram aqui -- elas sao entradas do <see cref="Catalogo"/> e o
/// <see cref="EstadoDeForma"/> as guarda, como todas as outras. O que mora aqui e o que o catalogo
/// nao sabe: se aprendeu, quanto domina, quanta precisao resta e se o toggle esta ligado.
/// </summary>
public sealed class EstadoDeDisciplina
{
	public bool Aprendida;

	/// <summary>Maestria REAL 0-100. Destrava faixas; NAO entra em conta de poder.</summary>
	public double Real;

	/// <summary>Precisao/energia ATUAL 0-100. Escala os bonus; drena com o uso.</summary>
	public double Atual;

	/// <summary>O toggle da faixa 0% (Autonomous Evasion / Aura of Destruction).</summary>
	public bool Ligada;

	/// <summary>
	/// SOBE A MAESTRIA REAL e devolve o degrau CRUZADO, se cruzou algum.
	///
	/// Devolver o degrau e o que permite o servidor anunciar. Sem anuncio, uma habilidade nova
	/// aparece na aba sem ninguem saber que ganhou -- e passivas que ninguem sabe que tem sao
	/// indistinguiveis de passivas que nao funcionam.
	/// </summary>
	public Degrau? SubirReal(DisciplinaDef def, double quanto)
	{
		if (quanto <= 0 || Real >= 100) return null;
		double antes = Real;
		Real = Math.Clamp(Real + quanto, 0, 100);

		foreach (Degrau d in def.Degraus)
			if (antes < d.Pct && Real >= d.Pct && d.Pct > 0) return d;
		return null;
	}

	/// <summary>
	/// O TIQUE DA ENERGIA ATUAL: drena com uso, recupera parada.
	///
	/// A REGENERACAO NAO ACONTECE EM COMBATE, e isso e do DM. Sem essa trava a disciplina viraria
	/// um recurso que se recarrega no meio da briga -- e o cansaco, que e a mecanica inteira,
	/// deixaria de existir.
	///
	/// E o teto da recuperacao e a maestria REAL: quem domina 40% nunca passa de 40% de precisao.
	/// E o que faz treinar valer a pena mesmo depois de todas as faixas abertas.
	/// </summary>
	public void TickAtual(DisciplinaDef def, double dt, bool emForma, bool emCombate)
	{
		if (!Aprendida) return;

		if (emForma) Atual -= def.DrenoNaForma * dt;
		else if (Ligada) Atual -= def.DrenoLigado * dt;
		else if (!emCombate) Atual += def.Regen * dt;

		Atual = Math.Clamp(Atual, 0, Real);
	}
}
