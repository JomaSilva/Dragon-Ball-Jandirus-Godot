using Jandirus.Core.Forms;

namespace Jandirus.Core.Skills;

/// <summary>
/// O DISCIPULADO -- as regras puras do mestre e do aluno. Porte de
/// `Code/Modules/Players/MasterStudent.dm`.
///
/// ============================ O QUE E ISTO, E O QUE NAO E ============================
/// No original o vinculo de mestre e aluno faz TRES coisas, e so tres:
///
///   1. quem tem mestre GANHA MAIS treinando contra ele (ate 3x, piso 1,2x);
///   2. o mestre pode PROVOCAR/ENSINAR um despertar de forma, e nessa tentativa o requisito
///      PESSOAL de BP do aluno vale METADE (`MST_HALF`);
///   3. o vinculo e do MUNDO (savefile `MASTER`, por assinatura) -- ele atravessa logout, morte e
///      reboot.
///
/// Nada disso decide dano, posicao ou recompensa sozinho: o que mora aqui sao as PERGUNTAS
/// (pode vincular? esta forma se ensina? o mestre e forte o bastante?), e as respostas sao as
/// mesmas nas duas pontas. Quem guarda estado, escreve save e fala com o jogador e o
/// `Server/GameServer.Mestre.cs`.
/// ====================================================================================
///
/// ============================ AS EXCLUSOES SAO REGRA, E NAO ESQUECIMENTO ============================
/// O cabecalho do `MasterStudent.dm:220-227` diz o criterio em voz alta: *"so entram formas cujo
/// destravamento e um REQUISITO PESSOAL (poder, e as vezes raiva)"*. Ficam de fora, cada uma pelo
/// seu motivo:
///
///   * **Ultra Instinto e Poder da Destruicao** -- eles JA TEM ensino proprio, em cadeia, e a raiz
///     dele e o cargo (ver `GameServer.Disciplinas.EnsinarDisciplina`). Ensina-los aqui tambem
///     criaria uma SEGUNDA porta pra mesma coisa, e as duas envelheceriam separadas. **Isto esta
///     escrito pra ninguem "consertar" depois: a ausencia deles nesta lista e a decisao.**
///   * **skill comprada com Marcos** (Super Namek, as formas Alien) -- quem paga o marco ja pagou;
///   * **maestria ou lua** (SSJ4, Full Power, Limit Breaker) -- nao se ensina o que so se treina,
///     e ninguem ensina ninguem a ter lua cheia;
///   * **evento** (o Bio de laboratorio, a saga Majin) -- tem dono no roteiro, nao no discipulado;
///   * **concessao** (o Mistico) -- e dom de ritual de um Kaioshin, e ja tem seu proprio caminho.
///
/// E A LISTA E DERIVADA, NAO DIGITADA (ver <see cref="Ensinavel"/>). O DM enumera oito tags
/// (`"ssj","ssj2","ssj3","lssj1","heran1","heran2","frost6","frost7"`) porque la nao havia campo
/// pra perguntar; aqui ha. Uma lista de ids exigiria que toda forma nova lembrasse de se
/// inscrever -- e o dia em que a escada Heran ou a do Frost Demon forem portadas com
/// `PortaBp`+`ChaveDoLimiar`, elas entram sozinhas. E o mesmo argumento que o
/// `LimiaresPessoais.Porta` ja faz pra si mesmo.
/// ==============================================================================================
/// </summary>
public static class Discipulado
{
	// =====================================================================
	// OS NUMEROS DO DM
	// =====================================================================
	/// <summary>`MST_MAX_STUDENTS` (`MasterStudent.dm:28`). Um mestre, cinco alunos.</summary>
	public const int MaxAlunos = 5;

	/// <summary>
	/// `MST_BP_RATIO` (`1A Defines.dm:26`): o mestre precisa de 3x o BP do aluno.
	///
	/// E **BP BASE dos dois lados**, e o DM escreve o motivo em `mst_bp_ok` (`:88`): o poder
	/// EXPRESSO e distorcido por forma, raiva, supressao, KO e Ki, entao "tres vezes mais forte"
	/// medido nele seria uma porta que abre e fecha conforme o mestre respira. Ver tambem a
	/// PARTE 3 da spec: personagem novo expressa entre 2 e 21 com o BP bruto bem maior -- as duas
	/// escalas nao se comparam.
	/// </summary>
	public const double RazaoDeBp = 3;

	/// <summary>
	/// `MST_HALF` (`1A Defines.dm:29`): na tentativa assistida o requisito PESSOAL de BP vale
	/// metade.
	///
	/// E o requisito **pessoal** -- o <see cref="LimiaresPessoais"/> sorteado no nascimento --, e
	/// nao o valor de fabrica da forma. Quem aplica e o passo 8 do
	/// <see cref="EstadoDeForma.Avaliar"/>, que e o funil por onde a tecla C, o `Proxima`, o
	/// `PorQueNao` e a aba Formas passam. Aplicar o corte DEPOIS, como desconto, faria a mensagem
	/// de recusa e o resultado discordarem.
	/// </summary>
	public const double FatorAssistido = 0.5;

	/// <summary>
	/// `MST_TEACH_COOLDOWN` (`MasterStudent.dm:30`): 3000 ticks do BYOND = **300 s**.
	///
	/// (Tick do BYOND e 1/10 s -- a mesma conversao que este port ja pagou caro pra aprender.)
	/// E POR MESTRE e nao por aluno: o que se esgota e o folego de quem ensina.
	/// </summary>
	public const double RecargaDeEnsinoSegundos = 300;

	/// <summary>`MST_RANGE` (`1A Defines.dm:25`): 4 tiles pra revalidar o convite.</summary>
	public const int TilesDoConvite = 4;

	/// <summary>
	/// `MST_WITNESS_RANGE` (`1A Defines.dm:30`): 8 tiles pra "eu VI essa forma".
	///
	/// O raio importa mais aqui do que no DM: la o `mst_note_form` usa `oview(8)` e o motor filtra;
	/// aqui o anuncio de forma varre a ZONA INTEIRA (`AnunciarForma`), entao sem conferir a
	/// distancia dentro do laco "eu vi" viraria "eu estava no mesmo planeta" -- e o pre-requisito
	/// nao gatearia nada. E o padrao "teto que nunca dispara" da PARTE 3 da spec.
	/// </summary>
	public const int TilesDeTestemunha = 8;

	/// <summary>
	/// QUANTO TEMPO UM CONVITE (ou uma oferta de despertar) FICA DE PE, em segundos.
	///
	/// NAO EXISTE NO DM, e nao existe porque la nao precisa: o `alert()` do BYOND CONGELA o aluno
	/// ate ele responder. Aqui nao ha caixa modal -- o molde e o `PedidoDeAmizade`
	/// (`GameServer.Convivio.cs`), um pendente vivo respondido por verb --, e um pendente sem prazo
	/// vira caixa de entrada: "aceitar" um convite de meia hora atras, do outro lado do mundo, nao
	/// quer dizer nada. Um minuto e o tempo de olhar pra tela.
	/// </summary>
	public const double PrazoDoConviteSegundos = 60;

	// O TETO E O PISO DO GANHO **NAO MORAM AQUI**: eles ja tinham casa em
	// `GainKnobs.MasterGainCap` (3) e `GainKnobs.MasterGainFloor` (1,2), lidos pelo
	// `Fighter.FightGainMult`, que e o porte literal do `fight_gain_mult`
	// (`combatgains.dm:88-100`). Repeti-los aqui seria a quarta copia do mesmo numero e a primeira
	// a divergir.

	// =====================================================================
	// O PORTAO DO MESTRE
	// =====================================================================
	/// <summary>
	/// `mst_bp_ok` (`MasterStudent.dm:88`): o mestre precisa de <see cref="RazaoDeBp"/> vezes o BP
	/// do aluno. **Os dois numeros sao BP BASE** -- ver <see cref="RazaoDeBp"/>.
	/// </summary>
	public static bool MestreForteOBastante(double bpMestre, double bpAluno) =>
		bpMestre >= bpAluno * RazaoDeBp;

	// O GANHO NAO TEM FUNCAO AQUI, e a ausencia e a regra: a conta inteira mora no
	// `Fighter.FightGainMult` (porte literal do `fight_gain_mult`, `combatgains.dm:88-100`), que ja
	// tem o ramo do mestre atras de um parametro. Um atalho `GanhoContraOMestre(a, b)` neste arquivo
	// seria uma segunda porta pra mesma conta -- e a primeira a envelhecer no dia em que o teto
	// mudar.

	// =====================================================================
	// QUE FORMAS SE ENSINAM
	// =====================================================================
	/// <summary>
	/// ESTA FORMA SE ENSINA? A derivacao inteira, em campos do catalogo. Ver o cabecalho da classe
	/// pra o CRITERIO; aqui esta so a traducao dele.
	/// </summary>
	public static bool Ensinavel(FormaDef? d)
	{
		if (d == null || d.Id == Catalogo.IdBase) return false;

		// (1) HA UMA PORTA DE BP? E a coisa que o `MST_HALF` corta -- sem ela a transformacao
		//     assistida nao teria o que dar.
		//
		// ============================ ERA `PortaBp > 0 && ChaveDoLimiar != ""`, E O SEGUNDO SOBRAVA ============================
		// A segunda metade dizia "tem limiar PESSOAL", e era verdade pra todas as formas que existiam:
		// as vinte e duas com `PortaBp` tem `ChaveDoLimiar`, entao a condicao extra nunca recusou nada
		// -- ela so parecia estar trabalhando.
		//
		// O FROST DEMON MOSTROU QUE ELA ESTAVA ERRADA. As duas evolucoes dele (`frost6`, `frost7`) tem
		// porta de BP e NAO tem limiar pessoal: no original o `FD_FORM6_AT`/`FD_FORM7_AT` sao `#define`
		// (nao ha `RolarIcer` em lugar nenhum do `statsaiyan.dm`) -- e o mestre corta esses `#define`
		// pela metade do mesmo jeito, na cara do arquivo: `IcerTransform.dm:89` faz
		// `FD_FORM6_AT * mst_desc`, e `MasterStudent.dm:304-305` lista as duas em `mst_form_power_ok`.
		// Com a condicao antiga, o `mst_teachable` do port devolveria uma lista sem elas e o mestre de
		// Frost Demon nao teria o que ensinar -- **enquanto o DM ensina**.
		//
		// O que a metade removida protegia continua protegido pela metade que ficou: o corte do mestre
		// se aplica a `PortaBp` quando nao ha limiar pessoal (`EstadoDeForma.Avaliar`, passo 8, ja cai
		// no valor de fabrica quando `LimiaresPessoais.Porta` devolve 0).
		// ============================================================================================================
		if (d.PortaBp <= 0) return false;

		// ============================ (1b) O QUE SE **COMPRA** NAO SE DESPERTA ASSISTIDO ============================
		// O cabecalho desta classe sempre listou *"skill comprada com Marcos (Super Namek, as formas
		// Alien)"* entre as exclusoes -- e a exclusao **nao existia**. Quem a fazia era o passo (1), e
		// ele a fazia pelo motivo errado: o comentario dele dizia que aquelas formas "sao SKILL, e
		// skill nao tem porta de BP nenhuma". Tem. `snamek()` cobra `BP >= snamekat`
		// (`Super_Namek.dm:10`) e `Alien_Trans()` cobra `ayyform1at` / `ayyform2at`
		// (`Alien_Transformations.dm:9`, `:15`). Enquanto as tres nao existiam no catalogo, a frase
		// errada e a lista certa conviviam; no dia em que entraram, as tres cairiam na lista do mestre
		// -- e o DM nao ensina nenhuma delas (`mst_form_seal`, `MasterStudent.dm:393`, lista
		// exatamente `ssj, ssj2, ssj3, lssj1, heran1, heran2, frost6, frost7`).
		//
		// E O CRITERIO NAO E "O DM NAO LISTA", E SIM O QUE O SISTEMA FAZ: o discipulado existe pra
		// PROVOCAR um despertar (`mst_form_needs_rage`, o mestre batendo ate a forma sair). Numa forma
		// comprada nao ha nada a provocar -- a porta e o marco, e ela ja abriu ou nao. O `PedeFlag`
		// diz exatamente isso, e o Heran (que TAMBEM e racial e NAO se compra) continua ensinavel
		// sozinho, que e a prova de que a linha nao esta excluindo "raca nao-Saiyajin".
		// ======================================================================================================
		if (d.PedeFlag != null) return false;

		// (2) RAMO LATERAL NAO. Sao os grades (o USSJ), que o DM exclui pelo nome; aqui o campo
		//     `ForaDoTronco` ja diz que eles nao sao degrau de ninguem, e eles abrem com MAESTRIA
		//     no SSJ1 -- que e treino, e treino nao se ensina.
		if (d.ForaDoTronco) return false;

		// (3) MAESTRIA DE **OUTRA** LINHA NAO. A do degrau anterior da propria linha pode: e o caso
		//     do SSJ3, que pede 50% de SSJ2 e que o DM mantem ensinavel (o corte de la e no
		//     `ssj3LearnReq`, e a maestria fica INTEIRA -- `MasterStudent.dm:300`, *"maestria NAO e
		//     requisito de poder"*). O que sai por esta linha e o SSJ4 e o `primal_legendary4`, que
		//     pedem maestria no OOZARU DOURADO: literalmente "depende da lua".
		if (d.PedeMaestriaDe.Length > 0 && d.PedeMaestriaDe != Catalogo.IdAnterior(d)) return false;

		// (4) FORMA QUE PEDE O DEGRAU ANTERIOR **DOMINADO INTEIRO** e treino puro, nao ensino:
		//     Full Power e Limit Breaker.
		if (d.PedeMaestria >= 100) return false;

		// (5) AS LINHAS QUE TEM OUTRO DONO. O divino tem ensino PROPRIO (as disciplinas, em
		//     cadeia, com raiz no cargo); o Mistico e concessao de ritual; o Oozaru e a lua.
		if (d.Linha is LinhaDeForma.GodKi or LinhaDeForma.GodKiRose or LinhaDeForma.Mistico
					or LinhaDeForma.UltraInstinct or LinhaDeForma.UltraEgo or LinhaDeForma.Oozaru)
			return false;

		// (6) E O QUE SO VEM POR CONCESSAO nao vem por mestre.
		return !d.SoPorConcessao;
	}

	/// <summary>
	/// AS FORMAS ENSINAVEIS, derivadas uma vez.
	///
	/// ============================ O QUE ESTA LISTA DEVOLVE HOJE, E ONDE ELA DIVERGE DO DM ============================
	/// `ssj1`, `ssj2`, `ssj3`, `wrathful` (o `Restrained_SSj`/`lssj1` do DM) -- os quatro que a spec
	/// pede e que existem no catalogo. Mais quatro que o DM nao tinha como ter:
	///
	///   * `future_ssj` -- **o DM concorda**: `mst_form_open("ssj")` (`:254`) nao exclui
	///     `FutureLineage`. O port so partiu o SSJ1 do Futuro em linha propria, e a regra o pega
	///     sozinha;
	///   * `c_type`, `legendary`, `primal_c_type`, `primal_legendary`, `primal_legendary2` --
	///     **aqui eu divirjo do original de olho aberto, e o motivo de la nao existe aqui**. No DM o
	///     Legendary Primal reaproveita a var `ssj` com outra semantica e o `mst_form_tag()`
	///     devolve `null` pra ele (`:198`); e o despertar do `Unrestrained_SSj` acontece SOZINHO
	///     dentro do laco de raiva (`supersaiyan.dm:75-83`), entao nao havia proc que um mestre
	///     pudesse disparar. Sao limitacoes de REPRESENTACAO, nao de desenho: neste port cada uma
	///     tem entrada, ordem, limiar pessoal e uma porta unica (`Avaliar`), e um mestre pode
	///     guia-las como guia o SSJ1.
	///
	/// **Ganho:** um degrau novo de qualquer linha entra sem tocar neste arquivo. **Perda:** um
	/// mestre pode apressar um degrau Legendary que no DM so vinha da furia sozinha. Se o dono
	/// quiser fidelidade estrita, o filtro extra e uma linha (`d.Ordem == 10` na linha Legendary) --
	/// e fica anotado aqui pra ser uma decisao, e nao um descuido.
	///
	/// **`frost6` e `frost7` ENTRARAM** -- as duas evolucoes do Frost Demon, que sao exatamente as
	/// duas tags que o `mst_teachable` do original ja listava (`MasterStudent.dm:390`). Elas passam
	/// pelo mesmo funil de todo mundo: tem porta de BP (`FD_FORM6_AT`/`FD_FORM7_AT`), nao sao ramo
	/// lateral, nao pedem maestria e a linha delas nao tem outro dono. As quatro SUPRESSOES ficam de
	/// fora sozinhas (`ForaDoTronco`), e a forma base tambem (`PortaBp` zero) -- que e o certo: nao
	/// se ensina alguem a estar no proprio corpo.
	///
	/// **`heran1` e `heran2` ENTRARAM**, e eram as duas ultimas tags do `mst_form_seal` que faltavam:
	/// com elas, o port passou a cobrir as OITO que o DM lista. Elas entram pelo mesmo funil de todo
	/// mundo (porta de BP com limiar pessoal, tronco, sem maestria de outra linha) e sem uma linha de
	/// codigo citando "Heran" -- que era a promessa que este arquivo fazia enquanto a escada nao
	/// existia ("o dia em que a escada Heran ou a do Frost Demon forem portadas, elas entram
	/// sozinhas").
	///
	/// **`snamek`, `alien1` e `alien2` NAO entram**, pelo passo (1b): elas se COMPRAM. O DM tambem
	/// nao as ensina. Ver la o porque de a exclusao ter passado anos escrita no cabecalho e nao
	/// existir no codigo.
	///
	/// UMA DIVERGENCIA MEDIDA NO HERAN, e ela e do proprio DM consigo mesmo: `mst_form_power_ok`
	/// cobra `ssj2at / 6` pro `heran2` (`MasterStudent.dm:303`) enquanto o gate real da forma cobra
	/// `ssj2at / 50` (`heran.dm:38`). O port usa o gate REAL nos dois caminhos -- o mestre nao pode
	/// cobrar oito vezes mais do que a forma cobra, senao a transformacao assistida seria MAIS DIFICIL
	/// que a sozinha, que e o oposto do que ela e.
	/// ==========================================================================================================
	/// </summary>
	public static IReadOnlyList<FormaDef> Ensinaveis => _ensinaveis;

	private static readonly FormaDef[] _ensinaveis =
		[.. Catalogo.Todas.Where(Ensinavel).OrderBy(d => d.Ordem)];

	/// <summary>Esta forma, por id, se ensina?</summary>
	public static bool EhEnsinavel(string id) => _ensinaveis.Any(d => d.Id == id);

	// =====================================================================
	// AS DUAS PERGUNTAS DO VINCULO
	// =====================================================================
	/// <summary>Por que o vinculo nao rolou. <see cref="Pode"/> = rolou.</summary>
	public enum RecusaDeVinculo
	{
		Pode = 0,
		SemAssinatura,   // corpo sem dono (NPC, clone) -- nao entra em lista social nenhuma
		EleMesmo,
		JaTemMestre,     // a chave do `mst_list` e o ALUNO: um aluno, um mestre
		Lotado,          // MST_MAX_STUDENTS
		MestreFraco,     // mst_bp_ok
		Caido,
		Longe,
	}

	/// <summary>
	/// TUDO O QUE O VINCULO PERGUNTA, sem nada do servidor dentro. O `Convidar_Aluno`
	/// (`MasterStudent.dm:423`) e o `mst_ask_student` (`:452`) fazem exatamente estas checagens --
	/// o segundo REVALIDA todas depois da caixa de dialogo, e por isso elas moram numa funcao so.
	/// </summary>
	/// <param name="distanciaTiles">
	/// Negativo = nao conferir (e a chamada de quem ja sabe que os dois estao de frente um pro
	/// outro; o DM confere o tile da frente no convite e a distancia de 4 na revalidacao).
	/// </param>
	public static RecusaDeVinculo AvaliarVinculo(bool mestreTemAssinatura, bool alunoTemAssinatura,
												 bool mesmaPessoa, bool alunoJaTemMestre,
												 int alunosDoMestre, double bpMestre, double bpAluno,
												 bool algumCaido, double distanciaTiles)
	{
		if (!mestreTemAssinatura || !alunoTemAssinatura) return RecusaDeVinculo.SemAssinatura;
		if (mesmaPessoa) return RecusaDeVinculo.EleMesmo;
		if (alunoJaTemMestre) return RecusaDeVinculo.JaTemMestre;
		if (alunosDoMestre >= MaxAlunos) return RecusaDeVinculo.Lotado;
		if (algumCaido) return RecusaDeVinculo.Caido;
		if (!MestreForteOBastante(bpMestre, bpAluno)) return RecusaDeVinculo.MestreFraco;
		if (distanciaTiles >= 0 && distanciaTiles > TilesDoConvite) return RecusaDeVinculo.Longe;
		return RecusaDeVinculo.Pode;
	}
}
