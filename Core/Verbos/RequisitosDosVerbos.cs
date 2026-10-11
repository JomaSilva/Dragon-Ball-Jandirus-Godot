using static Jandirus.Core.Verbos.Requisito;

namespace Jandirus.Core.Verbos;

/// <summary>
/// UMA LINHA DA TABELA: um verb que so existe pra quem TEM alguma coisa.
/// </summary>
/// <param name="Chave">
/// A identidade do verb. Tres formas, e as tres ja existiam no cliente antes desta tabela:
///   * `hab:&lt;id&gt;`            -- canal de habilidade (`SendHabilidade`); e a propria `Verbo.Chave`;
///   * `verbo:&lt;cmd&gt;[ &lt;arg&gt;]` -- canal de verbo (`SendVerbo`), com o argumento quando ele muda o gesto;
///   * `linha:&lt;nome&gt;`        -- nao e botao: uma linha de leitura que tambem depende de posse (o radar).
/// </param>
/// <param name="Rotulo">O que o jogador le no botao. So pro relatorio e pras provas -- nao e chave de nada.</param>
/// <param name="Onde">Em que aba (e cartao) ele aparece.</param>
/// <param name="Requisito">O que o personagem precisa ter.</param>
/// <param name="Recusa">
/// Um pedaco da frase com que o SERVIDOR recusa quem nao cumpre o requisito (alternativas separadas por
/// `|`). E o que a bancada `--verbsteste` cobra: ninguem que a tabela esconde consegue usar o verb por fora
/// do menu. Vazio = o servidor nao recusa (o requisito e so de LUGAR ou de sentido, e esta dito na linha).
/// </param>
public sealed record VerbDeclarado(string Chave, string Rotulo, string Onde, Requisito Requisito, string Recusa = "")
{
	public bool EhHabilidade => Chave.StartsWith(RequisitosDosVerbos.PrefixoDeHabilidade, StringComparison.Ordinal);
	public bool EhVerbo => Chave.StartsWith(RequisitosDosVerbos.PrefixoDeVerbo, StringComparison.Ordinal);

	/// <summary>O comando que viaja (sem o prefixo do canal e sem o argumento).</summary>
	public string Comando
	{
		get
		{
			string resto = Chave[(Chave.IndexOf(':') + 1)..];
			int espaco = EhVerbo ? resto.IndexOf(' ') : -1;
			return espaco < 0 ? resto : resto[..espaco];
		}
	}

	/// <summary>O argumento que viaja com ele ("" quase sempre).</summary>
	public string Arg
	{
		get
		{
			if (!EhVerbo) return "";
			string resto = Chave[(Chave.IndexOf(':') + 1)..];
			int espaco = resto.IndexOf(' ');
			return espaco < 0 ? "" : resto[(espaco + 1)..];
		}
	}
}

/// <summary>
/// **QUEM VE QUE VERB** -- a tabela unica de requisitos do menu.
///
/// ============================ O PEDIDO, E O CRITERIO ============================
/// O dono (2026-10-10), com a foto da aba World de um Saiyajin cheia de botoes do Cla do Dragao: *"seria
/// melhor dar uma limpa em verbs q sao exclusivas de raças e deveriam so aparecer se o player tem os
/// requisitos se nao fica mt poluido, como esses verbs de clan dragao, split form (coisa q n tenho mas tem
/// os verbs), verb de fusao namek etc."*
///
/// O criterio que separa as linhas daqui do resto do menu:
///
///   * **REQUISITO** -- o que o personagem precisa TER ou SER pra algum dia usar o verb: raca, classe,
///     skill ou tecnica, item, cargo, um fato do corpo (androide de absorcao, God Ki), uma posse no mundo
///     (um planeta dominado, um aluno, uma estatua erguida) ou uma OFERTA na mesa (um botao de "aceitar"
///     nao tem o que aceitar sem ela). Sem o requisito o botao NAO EXISTE.
///   * **CIRCUNSTANCIA** -- onde ele esta e o que esta fazendo agora: perto de um banco, com alvo marcado,
///     com Ki, de pe, dentro da mente. Isso nao tira o verb de ninguem: o botao aparece (as vezes apagado)
///     e o servidor diz o que falta.
///
/// VERB QUE NAO ESTA AQUI NAO TEM REQUISITO e aparece pra todo mundo -- e o que deixa quem registra um verb
/// novo (o "Minerar" da economia, por exemplo) sem precisar saber que esta tabela existe.
///
/// ============================ POR QUE MORA NO CORE ============================
/// Tres leitores: o cliente (o menu P, a busca, a tela de teclas, os botoes da aba World e o menu da tecla E), a bancada do
/// servidor (`--verbsteste`, que aperta cada linha com quem NAO cumpre o requisito e cobra a recusa) e o
/// relatorio (a tabela verb -> requisito -> onde aparece sai DAQUI, impressa). Uma copia por leitor seria a
/// lista que envelhece calada.
///
/// ESCONDER NAO E PERMISSAO: o servidor continua conferindo tudo. A coluna `Recusa` e a prova disso.
/// ==============================================================================
/// </summary>
public static class RequisitosDosVerbos
{
	public const string PrefixoDeHabilidade = "hab:";
	public const string PrefixoDeVerbo = "verbo:";

	/// <summary>A linha "RADAR: um sinal dourado..." do cartao das esferas. Nao e botao, e depende de posse igual.</summary>
	public const string LinhaDoRadar = "linha:sinal_dourado";

	/// <summary>A chave de um botao do canal de verbos.</summary>
	public static string ChaveDoVerbo(string comando, string arg = "") =>
		arg.Length == 0 ? PrefixoDeVerbo + comando : $"{PrefixoDeVerbo}{comando} {arg}";

	// =====================================================================
	// OS REQUISITOS COM NOME -- os que mais de uma linha usa
	// =====================================================================
	/// <summary>`Race == "Namekian" &amp;&amp; Class == "Dragon clan"` -- o portao do `ErguerEstatua` (`namekian.dm:79`).</summary>
	public static readonly Requisito ClaDoDragao = Raca("Namekian").E(Classe("Dragon clan"));

	/// <summary>O Dragon Radar na mochila -- o que o `UsarORadar` cobra, e o que o sinal dourado passou a cobrar.</summary>
	public static readonly Requisito TemRadar = Item(Items.CatalogoDeItens.Radar);

	private static readonly Requisito EmPlaneta = Em(Lugar.Planeta, "num planeta");
	private static readonly Requisito NoEspaco = Em(Lugar.Espaco, "no espaço");

	/// <summary>Um mundo sem estatua nao tem esfera nenhuma: elas nascem dela e nunca saem do mundo dela.</summary>
	private static readonly Requisito ComEstatua = Em(Lugar.Planeta | Lugar.ComEstatua, "num mundo com Estátua do Dragão");
	private static readonly Requisito SemEstatua = EmPlaneta.E(ForaDe(Lugar.ComEstatua, "ainda sem Estátua do Dragão"));

	/// <summary>
	/// As ordens das copias: quem sabe dividir o corpo -- ou quem ainda tem copia de pe (o Majin que perdeu o
	/// verb emprestado ao expelir quem o sabia continua precisando de "Destroy").
	/// </summary>
	private static readonly Requisito Copias =
		Tecnica("SplitForm").Ou(Fato(Fatos.TemCopias, "cópias de pé"));

	private static readonly Requisito DonoDaqui = Fato(Fatos.DominaEstePlaneta, "domina este planeta");
	private static readonly Requisito TemAluno = Fato(Fatos.TemAluno, "tem um aluno");
	private static readonly Requisito ConviteDeMestre = Fato(Fatos.ConviteDeMestre, "convite (ou ensinamento) de mestre pendente");
	private static readonly Requisito ConviteDeFusao = Fato(Fatos.ConviteDeFusao, "convite de fusão pendente");
	private static readonly Requisito ConviteDeAbsorcao = Fato(Fatos.ConviteDeAbsorcao, "convite de absorção Namekuseijin pendente");
	private static readonly Requisito Licao = Fato(Fatos.LicaoOferecida, "lição oferecida");
	private static readonly Requisito Juventude = Fato(Fatos.OfertaDeJuventude, "oferta de juventude pendente");
	private static readonly Requisito Potencial = Fato(Fatos.OfertaDePotencial, "oferta de despertar o potencial pendente");
	private static readonly Requisito Genkidama = Fato(Fatos.PedidoDeGenkidama, "uma Genkidama pedindo a sua energia");
	private static readonly Requisito Assento = Fato(Fatos.OfertaDeAssento, "assento de Ancião oferecido");
	private static readonly Requisito Desafio = Fato(Fatos.DesafioAoTitulo, "desafio ao título pendente");

	/// <summary>Carregar o titulo de Deus da Destruicao AGORA -- o `god_is_god(usr)` que abre todo verb do kit dele.</summary>
	private static readonly Requisito DeusDaDestruicao = Cargo(Ranks.KitDoDeus.Cargo);

	/// <summary>A chave de um verb do kit do Deus da Destruicao: o canal de habilidade mais o id de `Ranks.KitDoDeus`.</summary>
	private static string DoKit(string id) => PrefixoDeHabilidade + id;

	/// <summary>O pedaco da frase com que o servidor recusa qualquer verb do kit a quem nao carrega o titulo.</summary>
	private const string RecusaDoKit = "do Deus da Destruição";

	/// <summary>O Deus da Destruicao que trilha o Ultra Instinto: o titulo lhe empresta a aura e a infusao da Destruicao.</summary>
	private static readonly Requisito KitEmprestado =
		Fato(Fatos.KitDaDestruicaoEmprestado, "Deus da Destruição que trilha o Ultra Instinto");

	// =====================================================================
	// A TABELA
	// =====================================================================
	private const string AbaWorldEsferas = "World › Esferas do Dragão";
	private const string AbaWorldPedido = "World › O pedido ao dragão";
	private const string AbaWorldDominio = "World › Domínio planetário";

	/// <summary>
	/// A JANELA DE DESEJOS (`MenuDeDesejos`) nao e aba do menu P: as linhas dela vem prontas do SERVIDOR, que so
	/// manda cada uma a quem pode usa-la (`GameServer.MenuDoDragao.cs`). Uma linha de la entra nesta tabela
	/// pelo relatorio e pela recusa que a `--verbsteste` cobra -- quem a desenha nao pergunta nada aqui.
	/// </summary>
	public const string JanelaDeDesejos = "Janela de desejos";
	private const string AbaOther = "Other";
	private const string AbaLearning = "Learning";
	private const string AbaSkills = "Skills";

	/// <summary>
	/// A ABA DO DISCIPULADO do menu P ("Alunos" pra quem guia alguem, "Mestre" pra quem so e guiado --
	/// `MenuJogo.Alunos.cs`). Nao e uma das quatro abas de botoes de verb: a primeira linha dela nesta tabela e a
	/// PROPRIA ABA (o verb e o pedido do painel), e as outras sao os gestos do cartao de um aluno.
	/// </summary>
	public const string AbaDoDiscipulado = "Alunos / Mestre";
	private const string TeclaEMainframe = "Tecla E › Mainframe de androides";

	private static readonly VerbDeclarado[] _todos =
	[
		// ---------------------------------------------------------- World: as esferas
		new(LinhaDoRadar, "RADAR: um sinal dourado a N células daqui.", AbaWorldEsferas, TemRadar),
		new(ChaveDoVerbo("db_ver"), "Ver as esferas daqui", AbaWorldEsferas, ComEstatua,
			"não há Estátua do Dragão neste mundo"),
		new(ChaveDoVerbo("db_radar"), "Radar", AbaWorldEsferas, TemRadar.E(EmPlaneta),
			"não tem um Dragon Radar"),
		new(ChaveDoVerbo("db_pegar"), "Pegar esfera", AbaWorldEsferas, ComEstatua,
			"não há nenhuma Esfera do Dragão ao seu alcance"),
		new(ChaveDoVerbo("db_largar"), "Largar tudo", AbaWorldEsferas, ComEstatua,
			"não está carregando nenhuma esfera"),
		new(ChaveDoVerbo("db_invocar"), "INVOCAR", AbaWorldEsferas, ComEstatua,
			"não há Esferas do Dragão aqui"),
		new(ChaveDoVerbo("db_estatua"), "Erguer Estátua do Dragão", AbaWorldEsferas, ClaDoDragao.E(SemEstatua),
			"apenas Namekuseijins do Clã do Dragão|só pode erguer uma Estátua|somente o Guardião da Terra"),
		new(ChaveDoVerbo("db_estatua", "supremo"), "Erguer + gravar o SUPREMO (2.000.000 zeni)", AbaWorldEsferas,
			ClaDoDragao.E(SemEstatua),
			"apenas Namekuseijins do Clã do Dragão|só pode erguer uma Estátua|somente o Guardião da Terra"),
		// O MESMO GESTO, PAGO EM RECURSOS (dono, 2026-10-10): a moeda vai no fim do argumento, e o requisito e o
		// da linha de cima -- e o mesmo botao pra mesma gente.
		new(ChaveDoVerbo("db_estatua", Economia.Recurso.PedidoPago("supremo", Economia.Moeda.Recursos)),
			"Erguer + gravar o SUPREMO (4.000.000 recursos)", AbaWorldEsferas,
			ClaDoDragao.E(SemEstatua),
			"apenas Namekuseijins do Clã do Dragão|só pode erguer uma Estátua|somente o Guardião da Terra"),
		new(ChaveDoVerbo("db_reviver"), "Despertar as esferas", AbaWorldEsferas,
			Fato(Fatos.CriouAEstatuaDaqui, "ergueu a estátua deste mundo"),
			"não há Estátua do Dragão neste mundo|só quem ergueu a estátua"),
		// O RECORTE DE LUGAR QUE JA EXISTIA: as Super Esferas so se reivindicam (e so se invocam) no espaco. O
		// servidor cobra os dois: o dragao so e chamado AO LADO de uma delas (`set src in oview(2)`,
		// `ProceduralSpace.dm:1580`) -- ate 2026-10-10 o invocar era recorte so do menu, sem recusa nenhuma.
		new(ChaveDoVerbo("sdb_status"), "Super Esferas: placar", AbaWorldEsferas, NoEspaco),
		new(ChaveDoVerbo("sdb_reivindicar"), "Reivindicar Super Esfera", AbaWorldEsferas, NoEspaco,
			"as Super Esferas ficam no espaço"),
		new(ChaveDoVerbo("sdb_invocar"), "Chamar o Super Shenron", AbaWorldEsferas, NoEspaco,
			"ao lado de uma delas"),

		// ---------------------------------------------------------- World: o pedido ao dragao
		// "Ver os desejos" e de todo mundo, em todo lugar (e a porta do Super Shenron pra quem esta num
		// planeta), e por isso nao esta aqui. "Derrubar a estátua" abre a janela de desejos ja na confirmacao
		// (`db_menu foco derrubar`): so existe onde ha uma estatua. Estar AO LADO dela e circunstancia -- longe,
		// a janela abre sem a linha e o servidor diz por que.
		new(ChaveDoVerbo("db_menu", "foco " + Magic.AcoesDoDragao.Derrubar), "Derrubar a estátua", AbaWorldPedido, ComEstatua,
			"não há Estátua do Dragão ao seu alcance"),
		// "Pedidos por invocação" era botao deste cartao e virou linha da janela de desejos: o servidor so a
		// manda a quem ergueu a estatua do mundo em que esta -- o MESMO fato de "Despertar as esferas".
		new(ChaveDoVerbo("db_refazer"), "Pedidos por invocação", JanelaDeDesejos,
			Fato(Fatos.CriouAEstatuaDaqui, "ergueu a estátua deste mundo"),
			"não há Estátua do Dragão neste mundo|só quem ergueu a estátua|a estátua ancestral não responde"),

		// ---------------------------------------------------------- World: o dominio
		// Ver, conquistar (em paz ou a forca) e arrancar a bandeira de um invasor sao de qualquer um e nao
		// estao aqui. Estes quatro sao de quem TEM o dominio.
		new(ChaveDoVerbo("conq_dominios"), "Meus domínios", AbaWorldDominio,
			Fato(Fatos.DominaAlgum, "domina algum planeta"), "(nenhum -- finque sua bandeira"),
		new(ChaveDoVerbo("conq_tributo"), "Coletar tributo", AbaWorldDominio, DonoDaqui,
			"não tem domínio neste planeta"),
		new(ChaveDoVerbo("conq_spawn"), "Renascer aqui", AbaWorldDominio, DonoDaqui,
			"não tem domínio neste planeta"),
		new(ChaveDoVerbo("conq_abandonar", "sim"), "Abandonar domínio", AbaWorldDominio, DonoDaqui,
			"não tem domínio neste planeta"),

		// ---------------------------------------------------------- Other
		// A PREFERENCIA DOS GRAUS so tem o que preferir em quem tem a escada do Super Saiyajin: o Saiyajin puro
		// e o meio (`Catalogo.EhSaiyajin`), e o corpo que a abriu por fora (`canSSJ`: o bio-androide de DNA
		// Saiyajin). O servidor guarda a preferencia de qualquer um -- ela nao faz nada no resto --, por isso
		// nao ha recusa a cobrar.
		new(ChaveDoVerbo("graus"), "Toggle SSJ Grades", AbaOther,
			Raca("Saiyan", "Halfbreed").Ou(Fato(Fatos.EscadaSuperSaiyajin, "escada do Super Saiyajin aberta (DNA Saiyajin)"))),

		new(ChaveDoVerbo("splitform_seguir"), "Split Form: Follow", AbaOther, Copias, "nenhuma copia de pe"),
		new(ChaveDoVerbo("splitform_parar"), "Split Form: Stop", AbaOther, Copias, "nenhuma copia de pe"),
		new(ChaveDoVerbo("splitform_alvo"), "Split Form: Attack Target", AbaOther, Copias, "nenhuma copia de pe"),
		new(ChaveDoVerbo("splitform_perto"), "Split Form: Attack Nearest", AbaOther, Copias, "nenhuma copia de pe"),
		new(ChaveDoVerbo("splitform_destruir"), "Split Form: Destroy", AbaOther, Copias, "nenhuma copia de pe"),

		// AS RESPOSTAS A UMA OFERTA: sem oferta na mesa nao ha o que responder.
		new(ChaveDoVerbo("juventude_aceitar"), "Accept Youth", AbaOther, Juventude, "ninguem te ofereceu juventude"),
		new(ChaveDoVerbo("juventude_recusar"), "Decline Youth", AbaOther, Juventude, "ninguem te ofereceu juventude"),
		new(ChaveDoVerbo("potencial_aceitar"), "Accept Potential", AbaOther, Potencial, "ninguem te ofereceu despertar potencial"),
		new(ChaveDoVerbo("potencial_recusar"), "Decline Potential", AbaOther, Potencial, "ninguem te ofereceu despertar potencial"),
		new(ChaveDoVerbo("genkidama_doar"), "Give Power to Spirit Bomb", AbaOther, Genkidama, "ninguem esta pedindo a sua energia"),
		new(ChaveDoVerbo("genkidama_negar"), "Refuse Spirit Bomb", AbaOther, Genkidama, "ninguem esta pedindo a sua energia"),
		new(ChaveDoVerbo("cargo_nomear_aceitar"), "Accept Elder Seat", AbaOther, Assento, "ninguem te ofereceu um assento"),
		new(ChaveDoVerbo("cargo_nomear_recusar"), "Decline Elder Seat", AbaOther, Assento, "ninguem te ofereceu um assento"),

		// OS DE CARGO. As chaves sao as de `Core/Ranks/Ranks.cs`.
		new(ChaveDoVerbo("cargo_nomear"), "Appoint Elder", AbaOther, Cargo("elder"),
			"marque o Namekuseijin|só o Grande Ancião"),
		new(ChaveDoVerbo("cargo_herdeiro"), "Name Heir", AbaOther, Cargo("kov"), "so o Rei de Vegeta"),
		new(ChaveDoVerbo("cargo_herdeiro_tirar"), "Remove Heir", AbaOther, Cargo("kov"), "so o Rei de Vegeta"),
		// A linha de sucessao e LEITURA: o servidor a mostra a quem pedir. Ela aparece pro Rei e pros
		// Saiyajins porque o trono de Vegeta e assunto deles (o recorte que o menu ja tinha).
		new(ChaveDoVerbo("cargo_herdeiros"), "Line of Succession", AbaOther, Cargo("kov").Ou(Raca("Saiyan"))),
		new(ChaveDoVerbo("cargo_desafiar"), "Challenge God of Destruction", AbaOther,
			Fato(Fatos.GodKiDesperto, "God Ki desperto"),
			"so quem despertou o God Ki|nao ha God of Destruction"),
		new(ChaveDoVerbo("cargo_duelo_aceitar"), "Accept Challenge", AbaOther, Desafio, "ninguem esta te desafiando"),
		new(ChaveDoVerbo("cargo_duelo_adiar"), "Postpone Challenge", AbaOther, Desafio, "ninguem esta te desafiando"),
		// Os DEVERES sao dos 17 cargos que recebem tarefa (`RankDef.TemDeveres`), e nao de todo trono: o
		// portador de um cargo sem deveres via o botao pra ouvir "nao e um cargo com deveres".
		new(ChaveDoVerbo("cargo_deveres"), "Rank Duty", AbaOther,
			CargoQue("um cargo com deveres", r => r.TemDeveres), "não carrega cargo nenhum|não é um cargo com deveres"),
		new(ChaveDoVerbo("cargo_verba"), "Fund Earth", AbaOther, Cargo("president"), "não tem uma tarefa de verba"),
		// A CHAVE DA SALA e uma SKILL (`/datum/skill/rank/Permission`), e nao o trono: o kit do Guardiao a da,
		// e o do Mestre Korin tambem -- que ate aqui tinha a skill, o servidor aceitando, e nenhum botao.
		new(ChaveDoVerbo("sala_autorizar"), "Time Chamber: Authorize", AbaOther,
			Skill("/datum/skill/rank/Permission"), "não sabe fazer a chave da Sala do Tempo"),
		new(ChaveDoVerbo("sala_soltar"), "Time Chamber: Release", AbaOther, Cargo("guardian"),
			"só o Guardião da Terra tira"),

		// ---------------------------------------------------------- Learning
		new("hab:mst_aceitar", "Aceitar", AbaLearning, ConviteDeMestre, "ninguem te ofereceu nada"),
		new("hab:mst_recusar", "Recusar", AbaLearning, ConviteDeMestre, "ninguem te ofereceu nada"),
		new("hab:mst_ensinar", "Ajudar a transformar", AbaLearning, TemAluno, "fique de frente para um aluno seu"),
		new("hab:mst_dispensar", "Dispensar aluno", AbaLearning, TemAluno, "fique de frente para um aluno seu"),
		new("hab:mst_largar", "Deixar meu mestre", AbaLearning, Fato(Fatos.TemMestre, "tem um mestre"), "voce nao tem mestre"),

		// ---------------------------------------------------------- Alunos / Mestre (a aba do discipulado)
		// A PRIMEIRA LINHA E A ABA INTEIRA: o verb e o pedido do painel, e sem mestre nem aluno nao ha aba nem
		// painel (`GameServer.Alunos.cs`). As outras duas sao os gestos do cartao de UM aluno, pela assinatura dele.
		new(ChaveDoVerbo("alunos"), "(a aba inteira: o pedido do painel)", AbaDoDiscipulado,
			Fato(Fatos.TemMestre | Fatos.TemAluno, "tem um mestre ou um aluno"), "não tem mestre nem alunos"),
		new(ChaveDoVerbo("alunos_ensinar"), "Ajudar a transformar (no cartão do aluno)", AbaDoDiscipulado, TemAluno,
			"só vale pra um aluno seu"),
		new(ChaveDoVerbo("alunos_dispensar"), "Dispensar aluno (no cartão do aluno)", AbaDoDiscipulado, TemAluno,
			"só vale pra um aluno seu"),

		// (as duas perguntam "ha alguem na frente?" ANTES do requisito: sozinho, a recusa e a do alvo)
		new("hab:fus_danca", "Dança da Fusão", AbaLearning, Skill("/datum/skill/rank/Fusion_Dance"),
			"nao sabe a Danca da Fusao|nao ha ninguem na sua frente"),
		new("hab:fus_namek", "Fusão Namekuseijin (absorver)", AbaLearning, Raca("Namekian"),
			"coisa de Namekuseijin|nao ha ninguem na sua frente"),
		new("hab:fus_sim", "Aceitar a fusão", AbaLearning, ConviteDeFusao,
			"ninguem te convidou pra fundir"),
		new("hab:fus_namek_sim", "Aceitar a ABSORÇÃO (perco o personagem)", AbaLearning, ConviteDeAbsorcao,
			"ninguem te ofereceu a fusao Namekuseijin"),
		new("hab:fus_nao", "Recusar a fusão", AbaLearning, ConviteDeFusao.Ou(ConviteDeAbsorcao),
			"ninguem te convidou pra fundir"),
		new("hab:fus_passar", "Passar o controle", AbaLearning,
			Fato(Fatos.DirigeUmaFusao, "fundido e no controle"), "voce nao esta fundido"),

		new("hab:ens_licao_sim", "Aceitar a licao", AbaLearning, Licao, "ninguem esta te ensinando nada"),
		new("hab:ens_licao_nao", "Recusar a licao", AbaLearning, Licao, "ninguem esta te ensinando nada"),

		// O ESTUDO DA MENTE: uma linha por FAMILIA (o menu tem um botao por skill da Mente; todos declaram
		// uma destas duas). O verb vem de um degrau de nivel, e nao da compra.
		new("hab:Focus_Skill", "Focar: <skill da Mente> / Focar: nenhuma", AbaLearning, Tecnica("Focus_Skill"),
			"voce nao sabe"),
		new("hab:Write_Teachings", "Escrever sobre: <skill da Mente> / Parar de escrever", AbaLearning,
			Tecnica("Write_Teachings"), "voce nao sabe"),

		// ---------------------------------------------------------- Skills
		new("hab:oozaru", "Voltar ao normal", AbaSkills, Raca("Saiyan", "Halfbreed"), "voce nao esta transformado"),
		new("hab:absorver_ki", "Abrir/fechar coletores", AbaSkills,
			Fato(Fatos.AndroideDeAbsorcao, "androide de absorção"), "nao tem coletores de energia"),
		// UM VERB, DOIS CORPOS: o bio-androide consome (pela raca, larva inclusive -- ou pela skill "Tail
		// Absorb", que o meio-sangue de pai bio compra) e o Majin sela (pela skill Buu Absorb da arvore dele).
		new("hab:absorver", "Absorver", AbaSkills,
			Raca(Races.BioAndroids.Raca, Races.BioAndroids.RacaDoDm).Ou(Tecnica("Bio_Absorb")).Ou(Tecnica(Combat.AbsorcaoMajin.Verbo)),
			"nao tem como absorver ninguem|ainda nao sabe absorver"),
		new("hab:expelir", "Expelir todos", AbaSkills, Tecnica(Combat.AbsorcaoMajin.Verbo), "nao ha ninguem dentro de voce"),
		// `PodeRegenerar` (`GameServer.Raciais.cs`): o Namekuseijin pela raca, e quem COMPROU a skill `Regenerate`
		// (arvores de Alien, Android e Demonio) -- esse ja tem o botao da propria skill, com o mesmo nome.
		new("hab:regenerar", "Regenerar", AbaSkills, Raca("Namekian").Ou(Tecnica("Regenerate")), "seu corpo nao regenera assim"),

		// O KIT DO DEUS DA DESTRUICAO (`GodOfDestruction.dm:119-133`, o `god_apply_powers`): os verbs sao do
		// TITULO -- aparecem pra quem carrega o cargo e somem com ele, na hora. O servidor confere o trono em cada
		// um (`GameServer.KitDoDeus.cs`), com a mesma frase.
		new(DoKit(Ranks.KitDoDeus.IdDaFury), "Fury", AbaSkills, DeusDaDestruicao, RecusaDoKit),
		new(DoKit(Ranks.KitDoDeus.VerbHakai), "Hakai", AbaSkills, DeusDaDestruicao, RecusaDoKit),
		new(DoKit(Ranks.KitDoDeus.VerbFlick), "Power Flick", AbaSkills, DeusDaDestruicao, RecusaDoKit),
		new(DoKit(Ranks.KitDoDeus.VerbFuria), "Destruction God's Fury", AbaSkills, DeusDaDestruicao, RecusaDoKit),
		new(DoKit(Ranks.KitDoDeus.VerbEsfera), "Destroyer Sphere", AbaSkills, DeusDaDestruicao, RecusaDoKit),
		new(DoKit(Ranks.KitDoDeus.VerbAnular), "Energy Nullification", AbaSkills, DeusDaDestruicao, RecusaDoKit),
		// OS DOIS EMPRESTADOS (`:134-138`): so pro Deus que trilha o INSTINTO. Quem trilha a Destruicao usa os da
		// disciplina dele, e nao ve estes.
		new(DoKit(Ranks.KitDoDeus.VerbAuraEmprestada), "Aura of Destruction (emprestada pelo título)", AbaSkills,
			KitEmprestado, RecusaDoKit + "|a quem trilha o Instinto"),
		new(DoKit(Ranks.KitDoDeus.VerbInfusaoEmprestada), "Hakai Infusion (emprestada pelo título)", AbaSkills,
			KitEmprestado, RecusaDoKit + "|a quem trilha o Instinto"),

		// ---------------------------------------------------------- Tecla E: o mainframe de androides
		// A MAQUINA E DE HUMANO: instalar o laboratorio e converter o proprio corpo pedem a raca
		// (`InstalarLab` e `VirarAndroide`, `GameServer.Tech.cs`). As duas primeiras linhas so ABREM uma pagina
		// do menu -- nao viajam, e por isso nao ha recusa a cobrar delas; as quatro de dentro viajam.
		new(ChaveDoVerbo("lab_menu", "labs"), "Instalar laboratório...", TeclaEMainframe, Raca("Human")),
		new(ChaveDoVerbo("and_menu", "androide"), "Converter meu corpo...", TeclaEMainframe, Raca("Human")),
		new(ChaveDoVerbo("lab_androide"), "Android Lab", TeclaEMainframe, Raca("Human"),
			"perto de um mainframe|a instalacao pede um humano"),
		new(ChaveDoVerbo("lab_bio"), "Bio-Android Lab", TeclaEMainframe, Raca("Human"),
			"perto de um mainframe|a instalacao pede um humano"),
		// Cada conversao tem dois botoes -- em zeni e, pelo dobro, em recursos (a moeda e o argumento) --, e os
		// dois pedem a mesma raca.
		new(ChaveDoVerbo("androide_absorcao"), "Androide de Absorção (1.000.000 zeni)", TeclaEMainframe, Raca("Human"),
			"dentro de um Android Lab|fisiologia humana"),
		new(ChaveDoVerbo("androide_absorcao", Economia.Recurso.PalavraDeRecursos), "Androide de Absorção (2.000.000 recursos)",
			TeclaEMainframe, Raca("Human"), "dentro de um Android Lab|fisiologia humana"),
		new(ChaveDoVerbo("androide_infinito"), "Androide de Energia Infinita (2.000.000 zeni)", TeclaEMainframe, Raca("Human"),
			"dentro de um Android Lab|fisiologia humana"),
		new(ChaveDoVerbo("androide_infinito", Economia.Recurso.PalavraDeRecursos), "Androide de Energia Infinita (4.000.000 recursos)",
			TeclaEMainframe, Raca("Human"), "dentro de um Android Lab|fisiologia humana"),
	];

	private static readonly Dictionary<string, VerbDeclarado> _porChave =
		_todos.ToDictionary(v => v.Chave, StringComparer.Ordinal);

	/// <summary>Todas as linhas, na ordem das abas. E daqui que sai a tabela do relatorio.</summary>
	public static IReadOnlyList<VerbDeclarado> Todos => _todos;

	/// <summary>A linha de uma chave, ou nulo se o verb nao tem requisito declarado.</summary>
	public static VerbDeclarado? Linha(string chave) => _porChave.GetValueOrDefault(chave);

	/// <summary>O requisito de uma chave. Quem nao esta na tabela nao tem requisito: aparece.</summary>
	public static Requisito De(string chave) =>
		chave.Length > 0 && _porChave.TryGetValue(chave, out VerbDeclarado? v) ? v.Requisito : Nenhum;

	/// <summary>O requisito de um botao do canal de verbos, pelo comando (e o argumento) que ele manda.</summary>
	public static Requisito DoVerbo(string comando, string arg = "") => De(ChaveDoVerbo(comando, arg));

	/// <summary>
	/// QUAIS LINHAS ESTA POSSE CUMPRE, numa string de uns e zeros na ordem da tabela. E o pedaco que as abas
	/// poem na assinatura de cache: a pagina se refaz exatamente quando um botao passa a existir (ou deixa
	/// de), e nao a cada item que entra na mochila.
	/// </summary>
	public static string AssinaturaDe(IPosse posse)
	{
		Span<char> bits = stackalloc char[_todos.Length];
		for (int i = 0; i < _todos.Length; i++) bits[i] = _todos[i].Requisito.Atende(posse) ? '1' : '0';
		return new string(bits);
	}
}
