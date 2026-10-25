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

	/// <summary>A skill que ensina a fusao dos Namekuseijin (`/datum/skill/namek/fusion`, `namekian.dm:111`).</summary>
	public const string SkillDaFusaoNamek = "/datum/skill/namek/fusion";

	/// <summary>
	/// QUEM MOLDA A ESTATUA: o Namekuseijin do Cla do Dragao que APRENDEU a fazer isso -- a skill `Make Dragon
	/// Statue` da arvore racial (`namekian.dm:18`, e o verb sai do `after_learn()` dela). Ate 2026-10-13 a skill
	/// nao acendia neste port e o verb pedia so raca e classe; ver `AcendedoresDoPort`.
	/// </summary>
	public static readonly Requisito EscultorDaEstatua = ClaDoDragao.E(Skill(Skills.AcendedoresDoPort.EstatuaDoDragao));

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
	private static readonly Requisito PedidoDeFilhoNaMesa = Fato(Fatos.PedidoDeFilho, "pedido de filho pendente");
	private static readonly Requisito Licao = Fato(Fatos.LicaoOferecida, "lição oferecida");
	private static readonly Requisito Juventude = Fato(Fatos.OfertaDeJuventude, "oferta de juventude pendente");
	private static readonly Requisito Potencial = Fato(Fatos.OfertaDePotencial, "oferta de despertar o potencial pendente");
	private static readonly Requisito Genkidama = Fato(Fatos.PedidoDeGenkidama, "uma Genkidama pedindo a sua energia");
	private static readonly Requisito Assento = Fato(Fatos.OfertaDeAssento, "assento de Ancião oferecido");
	private static readonly Requisito Aprendizado = Fato(Fatos.OfertaDeAprendiz, "aprendizado de Kaioshin oferecido");
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

	/// <summary>(2026-10-17) Uma ativa de kit de RACA: o kit inteiro da raca, ou o bio-androide que a escolheu do sangue (`Races.SkillsRaciais`).</summary>
	private static Requisito AtivaDeRaca(Races.SkillRacial skill, string sangue) => DoKitDeRaca(skill, $"{sangue} (ou o bio-androide que a escolheu)");

	/// <summary>(2026-10-17) O bio-androide com vaga no DNA (menos de seis escolhidas, e o sangue ainda tem o que dar).</summary>
	private static readonly Requisito ComVagaNoDna = Fato(Fatos.SkillDoDnaPorEscolher, "bio-androide com vaga no DNA");

	/// <summary>(2026-10-17) Os pedacos das recusas da escolha do DNA (`Races.SkillsRaciais.PorQueNaoEscolhe`) -- o que a `--verbsteste` cobra.</summary>
	private const string RecusaDaEscolhaDoDna = "só o bio-androide|não vem de nenhuma raça do seu sangue|já tem essa skill|já escolheu as";

	/// <summary>
	/// (2026-10-17) AS VINTE LINHAS DA ESCOLHA DO DNA, uma por skill (`hab:dna:&lt;bit&gt;`): o botao aparece pro bio com vaga cujo sangue da a
	/// skill e que ainda nao a tem -- e some com a escolha.
	/// </summary>
	private static IEnumerable<VerbDeclarado> DaEscolhaDoDna() =>
		Races.SkillsRaciais.Todas.Select(s => new VerbDeclarado(
			PrefixoDeHabilidade + s.IdDaEscolha, $"DNA: {s.Nome}", AbaSkills,
			ComVagaNoDna.E(NoSangueSemEscolher(s.Bit, $"o sangue dá {s.Nome} e ele ainda não a tem")),
			RecusaDaEscolhaDoDna));

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

	/// <summary>(v0.4.0) O pedaco da recusa das ativas raciais (`GameServer.RecusaDoSangue`) -- o que a `--verbsteste` cobra.</summary>
	private const string RecusaDoSangue = "não é do seu sangue";

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
		new(ChaveDoVerbo("db_estatua"), "Erguer Estátua do Dragão", AbaWorldEsferas, EscultorDaEstatua.E(SemEstatua),
			"apenas Namekuseijins do Clã do Dragão|ainda não aprendeu a moldar a Estátua|só pode erguer uma Estátua|somente o Guardião da Terra"),
		// (2026-10-22) OS DOIS "Erguer + gravar o SUPREMO" SAIRAM: o "Erguer Estátua do Dragão" abre o menu da estatua
		// (`Client/JanelaDaEstatua.cs`), e o supremo -- em zeni ou em recursos -- e uma caixa dele. O verbo de antes (`db_estatua supremo
		// [recursos]`) continua valendo no servidor; so nao tem mais botao.
		// (2026-10-19) ...e a estatua que perdeu o criador (apagado ou reencarnado) e despertada por OUTRO do Cla do Dragao -- o dono.
		new(ChaveDoVerbo("db_reviver"), "Despertar as esferas", AbaWorldEsferas,
			Fato(Fatos.CriouAEstatuaDaqui, "ergueu a estátua deste mundo")
				.Ou(ClaDoDragao.E(Fato(Fatos.EstatuaSemCriadorAqui, "a estátua deste mundo perdeu o criador"))),
			"não há Estátua do Dragão neste mundo|só quem ergueu a estátua|só um Namekuseijin do Clã do Dragão"),
		// O RECORTE DE LUGAR QUE JA EXISTIA: as Super Esferas so se reivindicam (e so se invocam) no espaco. O
		// servidor cobra os dois: o dragao so e chamado AO LADO de uma delas (`set src in oview(2)`,
		// `ProceduralSpace.dm:1580`) -- ate 2026-10-10 o invocar era recorte so do menu, sem recusa nenhuma.
		new(ChaveDoVerbo("sdb_status"), "Super Esferas: placar", AbaWorldEsferas, NoEspaco),
		new(ChaveDoVerbo("sdb_reivindicar"), "Reivindicar Super Esfera", AbaWorldEsferas, NoEspaco,
			"as Super Esferas ficam no espaço"),
		new(ChaveDoVerbo("sdb_invocar"), "Chamar o Super Shenlong", AbaWorldEsferas, NoEspaco,
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
		// (2026-10-23) A FAMILIA DO C DUPLO so tem o que escolher em quem tem forma DIVINA a alcancar -- e forma divina e a que pede ki
		// divino desperto, de qualquer raca (o Ultra Instinct e de todas). Como os graus, o servidor guarda a de qualquer um: sem recusa.
		new(ChaveDoVerbo("divinas_c"), "Toggle C: Mortal/Divine Forms", AbaOther, Fato(Fatos.GodKiDesperto, "God Ki desperto")),

		// (2026-10-19) OS CORPOS DAS FORMAS DO FROST DEMON -- o `Icer_Form_Settings` do DM, de todo Frost Demon (`fd_login_check`,
		// `IcerTransform.dm:148`). Quem recusa quem nao e um e o servidor (`GameServer.Frost.ComandoDasFormasDoFrost`).
		new(ChaveDoVerbo("formas_frost"), "Frost Demon Form Settings", AbaOther, Raca(Races.FormasDeFrost.Raca), "só um Frost Demon"),

		new(ChaveDoVerbo("splitform_seguir"), "Split Form: Follow", AbaOther, Copias, "nenhuma copia de pe"),
		new(ChaveDoVerbo("splitform_parar"), "Split Form: Stop", AbaOther, Copias, "nenhuma copia de pe"),
		new(ChaveDoVerbo("splitform_alvo"), "Split Form: Attack Target", AbaOther, Copias, "nenhuma copia de pe"),
		new(ChaveDoVerbo("splitform_perto"), "Split Form: Attack Nearest", AbaOther, Copias, "nenhuma copia de pe"),
		new(ChaveDoVerbo("splitform_destruir"), "Split Form: Destroy", AbaOther, Copias, "nenhuma copia de pe"),

		// (v0.2.1) O GATILHO DAS ARMAS DE FOGO -- o `verb/fire()` do original (`Guns.dm:110-114`): so existe pra quem
		// carrega uma.
		new(ChaveDoVerbo(Tech.ArmasDeFogo.VerboAtirar), "Atirar (arma de fogo)", AbaOther,
			Fato(Fatos.TemArmaDeFogo, "carrega uma arma de fogo"), "não tem uma arma de fogo"),

		// AS RESPOSTAS A UMA OFERTA: sem oferta na mesa nao ha o que responder.
		new(ChaveDoVerbo("juventude_aceitar"), "Accept Youth", AbaOther, Juventude, "ninguem te ofereceu juventude"),
		new(ChaveDoVerbo("juventude_recusar"), "Decline Youth", AbaOther, Juventude, "ninguem te ofereceu juventude"),
		new(ChaveDoVerbo("potencial_aceitar"), "Accept Potential", AbaOther, Potencial, "ninguem te ofereceu despertar potencial"),
		new(ChaveDoVerbo("potencial_recusar"), "Decline Potential", AbaOther, Potencial, "ninguem te ofereceu despertar potencial"),
		new(ChaveDoVerbo("genkidama_doar"), "Give Power to Spirit Bomb", AbaOther, Genkidama, "ninguem esta pedindo a sua energia"),
		new(ChaveDoVerbo("genkidama_negar"), "Refuse Spirit Bomb", AbaOther, Genkidama, "ninguem esta pedindo a sua energia"),
		new(ChaveDoVerbo("cargo_nomear_aceitar"), "Accept Elder Seat", AbaOther, Assento, "ninguem te ofereceu um assento"),
		new(ChaveDoVerbo("cargo_nomear_recusar"), "Decline Elder Seat", AbaOther, Assento, "ninguem te ofereceu um assento"),
		new(ChaveDoVerbo("aprendiz_aceitar"), "Accept Apprenticeship", AbaOther, Aprendizado, "ninguém te ofereceu aprendizado"),
		new(ChaveDoVerbo("aprendiz_recusar"), "Decline Apprenticeship", AbaOther, Aprendizado, "ninguém te ofereceu aprendizado"),

		// OS DE CARGO. As chaves sao as de `Core/Ranks/Ranks.cs`.
		new(ChaveDoVerbo("cargo_nomear"), "Appoint Elder", AbaOther, Cargo("elder"),
			"marque o Namekuseijin|só o Grande Ancião"),
		// (2026-10-19) o patronato do Kaioshin: os dois verbos da skill `Kaioshin Apprenticeship`, que o censo tinha como mudos
		new(ChaveDoVerbo("aprendiz_nomear"), "Name Apprentice", AbaOther, Cargo("kaioshin"), "só o Kaioshin no trono"),
		new(ChaveDoVerbo("aprendiz_dispensar"), "Dismiss Apprentice", AbaOther, Cargo("kaioshin"), "só o Kaioshin no trono"),
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

		// (2026-10-17) OS FILHOS -- o `Have_Child` do "Other" do DM (`Mating.dm:139`). Pedir e de quem tem um amor correspondido no
		// jogo; responder, de quem foi pedido. E o `Lay_Egg` (`Mating.dm:222`): as racas de ovo, sozinhas, ja com idade pra gerar. Ver
		// `GameServer.Filhos.cs`.
		new("hab:filho_pedir", "Ter um filho", AbaOther, Fato(Fatos.TemUmAmor, "tem um amor correspondido"),
			"ninguém no jogo te ama de volta (os dois declaram AMOR na aba People)"),
		new("hab:filho_sim", "Aceitar o filho", AbaOther, PedidoDeFilhoNaMesa, "ninguém te pediu um filho"),
		new("hab:filho_nao", "Recusar o filho", AbaOther, PedidoDeFilhoNaMesa, "ninguém te pediu um filho"),
		new("hab:filho_ovo", "Pôr um ovo", AbaOther, Fato(Fatos.PoeOvo, "a raça põe ovo, e já tem idade pra gerar"),
			"não põe ovo|jovem demais"),

		// (2026-10-18) O NEO TUFFLE -- entrar no corpo de um jogador nocauteado. Ver `GameServer.NeoTuffle.cs`.
		new("hab:tuffle_possuir", "Possuir um nocauteado", AbaOther, Fato(Fatos.NeoTuffle, "é um Neo Tuffle, no próprio corpo"),
			"não é um Neo Tuffle"),

		// ---------------------------------------------------------- Learning
		new("hab:mst_aceitar", "Aceitar", AbaLearning, ConviteDeMestre, "ninguem te ofereceu nada"),
		new("hab:mst_recusar", "Recusar", AbaLearning, ConviteDeMestre, "ninguem te ofereceu nada"),
		new("hab:mst_ensinar", "Ajudar a transformar", AbaLearning, TemAluno, "fique de frente para um aluno seu"),
		new("hab:mst_dispensar", "Dispensar aluno", AbaLearning, TemAluno, "fique de frente para um aluno seu"),
		new("hab:mst_largar", "Deixar meu mestre", AbaLearning, Fato(Fatos.TemMestre, "tem um mestre"), "voce nao tem mestre"),

		// ---------------------------------------------------------- Alunos / Mestre (a aba do discipulado)
		// A PRIMEIRA LINHA E A ABA INTEIRA: o verb e o pedido do painel, e sem mestre nem aluno nao ha aba nem
		// painel (`GameServer.Alunos.cs`). As outras sao os gestos do cartao de UM aluno, pela assinatura dele.
		new(ChaveDoVerbo("alunos"), "(a aba inteira: o pedido do painel)", AbaDoDiscipulado,
			Fato(Fatos.TemMestre | Fatos.TemAluno, "tem um mestre ou um aluno"), "não tem mestre nem alunos"),
		new(ChaveDoVerbo("alunos_ensinar"), "Ajudar a transformar (no cartão do aluno)", AbaDoDiscipulado, TemAluno,
			"só vale pra um aluno seu"),
		new(ChaveDoVerbo("alunos_dispensar"), "Dispensar aluno (no cartão do aluno)", AbaDoDiscipulado, TemAluno,
			"só vale pra um aluno seu"),
		new(ChaveDoVerbo("alunos_licao"), "Ensinar uma skill sua (no cartão do aluno)", AbaDoDiscipulado, TemAluno,
			"só vale pra um aluno seu"),
		new(ChaveDoVerbo("alunos_tecnica"), "Ensinar uma técnica de ki sua (no cartão do aluno)", AbaDoDiscipulado, TemAluno,
			"só vale pra um aluno seu"),

		// (as duas perguntam "ha alguem na frente?" ANTES do requisito: sozinho, a recusa e a do alvo)
		new("hab:fus_danca", "Dança da Fusão", AbaLearning, Skill("/datum/skill/rank/Fusion_Dance"),
			"nao sabe a Danca da Fusao|nao ha ninguem na sua frente"),
		// (2026-10-13) PEDE A SKILL `Fusion- Namek Style`, e nao so a raca: no DM o verb nasce do `after_learn()` dela
		// (`namekian.dm:111-124`), e sem isto a skill era uma compra que nao dava nada.
		new("hab:fus_namek", "Fusão Namekuseijin (absorver)", AbaLearning, Raca("Namekian").E(Skill(SkillDaFusaoNamek)),
			"coisa de Namekuseijin|nao ha ninguem na sua frente|aprendeu a se fundir"),
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
		// (2026-10-24) O BIO COM SANGUE MAJIN TEM AS DUAS (o dono: *"Os dois, em botoes separados"*): o "Absorver" de cima bebe, e este sela
		// como o Majin. Quem tem: o bio que sabe Buu Absorb -- a arvore do Majin so abre pro sangue Majin, e o servidor confere o sangue
		// (`GameServer.BioMajin.cs`). O Majin de verdade sela pelo "Absorver" e nao o ve.
		new("hab:absorver_majin", "Absorver como Majin", AbaSkills,
			Raca(Races.BioAndroids.Raca, Races.BioAndroids.RacaDoDm).E(Tecnica(Combat.AbsorcaoMajin.Verbo)),
			"só o bio-androide com sangue Majin|ainda nao sabe absorver"),
		new("hab:expelir", "Expelir todos", AbaSkills, Tecnica(Combat.AbsorcaoMajin.Verbo), "nao ha ninguem dentro de voce"),
		// (2026-10-17) A LARVA DO BIO-ANDROIDE (o adendo do dono): a forma de barata que o bio de laboratorio veste quando quer, e o enterrar,
		// que e so dela -- e o unico jeito de sair dela (5 min enterrada). Ver `GameServer.Larva.cs`.
		new("hab:larva", "Forma de larva", AbaSkills, Fato(Fatos.BioDeLaboratorio, "bio-androide de laboratório"), "não tem forma de larva"),
		new("hab:enterrar", "Enterrar-se / Sair da terra", AbaSkills, Fato(Fatos.EmLarva, "na forma de larva"), "só a larva se enterra"),
		// (2026-10-17) O NUCLEO DO BIO (o adendo do dono): uma linha pra FAMILIA -- o menu tem um botao por lugar (bracos, pernas, cabeca), e os
		// cinco somem com a escolha, que e uma so. Ver `GameServer.Nucleo.cs`.
		new("hab:nucleo", "Núcleo: <membro>", AbaSkills, Fato(Fatos.NucleoPorEscolher, "bio-androide que ainda não escolheu onde mora o núcleo"),
			"não tem núcleo|já tem lugar"),
		// (2026-10-17) AS SKILLS DO DNA (o adendo do dono): um botao por skill que o sangue do bio da e que ele ainda nao tem, enquanto houver vaga
		// (ate seis). Uma por vez, pra sempre. Ver `Races.SkillsRaciais` e `GameServer.SkillsDoDna.cs`.
		.. DaEscolhaDoDna(),
		// `PodeRegenerar` (`GameServer.Raciais.cs`): o Namekuseijin pela raca, e quem COMPROU a skill `Regenerate`
		// (arvores de Alien, Android e Demonio) -- esse ja tem o botao da propria skill, com o mesmo nome.
		new("hab:regenerar", "Regenerar", AbaSkills, Raca("Namekian").Ou(Tecnica("Regenerate")), "seu corpo nao regenera assim"),

		// (v0.4.0) AS ATIVAS RACIAIS -- vem com a raca (dono, 2026-10-17), uma linha por ativa, pelo KIT da raca e da classe
		// (`Races.Passivas.KitDe`). Quem nao tem o sangue ouve a recusa do `GameServer.Passivas.UsarAtivaRacial`. (2026-10-17) As cinco dos kits
		// de RACA vao pela skill (`Races.SkillRacial`): o kit inteiro, ou a escolha do bio-androide.
		new($"hab:{Races.Passivas.Ferocidade}", "Unmatched Ferocity", AbaSkills, Kit(Races.KitRacial.Primal, "sangue Primal"), RecusaDoSangue),
		new($"hab:{Races.Passivas.Transbordar}", "Overflowing Power", AbaSkills, Kit(Races.KitRacial.Lendario, "o Saiyajin Lendário"), RecusaDoSangue),
		new($"hab:{Races.Passivas.ForcaImparavel}", "Unstoppable Force", AbaSkills, Kit(Races.KitRacial.Lendario, "o Saiyajin Lendário"), RecusaDoSangue),
		new($"hab:{Races.Passivas.TransbordarKi}", "Overflowing Ki", AbaSkills, Kit(Races.KitRacial.LendarioPrimal, "o Lendário Primal"), RecusaDoSangue),
		new($"hab:{Races.Passivas.SedeDeSangue}", "Primal Bloodlust", AbaSkills, Kit(Races.KitRacial.LendarioPrimal, "o Lendário Primal"), RecusaDoSangue),
		new($"hab:{Races.Passivas.Demonico}", "Demonic Saiyan", AbaSkills, Kit(Races.KitRacial.LendarioPrimal, "o Lendário Primal"), RecusaDoSangue),
		new($"hab:{Races.Passivas.SegundoFolego}", "Second Wind", AbaSkills, AtivaDeRaca(Races.SkillRacial.SegundoFolego, "sangue humano"), RecusaDoSangue),
		new($"hab:{Races.Passivas.ForcaDeVontade}", "Sheer Willpower", AbaSkills, AtivaDeRaca(Races.SkillRacial.ForcaDeVontade, "sangue humano"), RecusaDoSangue),
		new($"hab:{Races.Passivas.PoderMaximo}", "Max Power", AbaSkills, AtivaDeRaca(Races.SkillRacial.PoderMaximo, "sangue humano"), RecusaDoSangue),
		new($"hab:{Races.Passivas.Disrupcao}", "Spirit Disruption", AbaSkills, AtivaDeRaca(Races.SkillRacial.Disrupcao, "sangue Namekuseijin"), RecusaDoSangue),
		new($"hab:{Races.Passivas.Ira}", "Unrestrained Wrath", AbaSkills, AtivaDeRaca(Races.SkillRacial.Ira, "sangue Frost Demon"), RecusaDoSangue),
		new($"hab:{Races.Passivas.MenteCruel}", "Ruthless Mind", AbaSkills, Kit(Races.KitRacial.Mutante, "o Frost Demon mutante"), RecusaDoSangue),
		new($"hab:{Races.Passivas.Sadismo}", "Sadistic Nature", AbaSkills, Kit(Races.KitRacial.Mutante, "o Frost Demon mutante"), RecusaDoSangue),
		// RELAXAR PEDE CONTROLE DE KI (regra do dono, 2026-10-12: *"relaxar e o poder cair deveria ser somente pra
		// quem sabe controlar o proprio poder"*): o mesmo `Power_Control` do degrau 5 de Basic Ki Control que abre a
		// barra. No DM o `ReadyUp` e de todo corpo (`Power Control.dm:39`). Ver `GameServer.AlternarProntidao`.
		new("hab:prontidao", "Relaxar / ficar pronto", AbaSkills, Tecnica(Stats.PoderEmUso.VerboDoControle),
			"não sabe controlar o próprio poder"),

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

		// (2026-10-19) A TRAVESSIA DO KING YEMMA: ir ao Outro Mundo (a mesa do Enma, onde os mortos chegam) e voltar pro lugar EXATO de
		// onde saiu. E do TRONO, como o kit do Deus; e quem perdeu o trono do lado de la continua vendo o botao enquanto a volta guardada
		// for dele. Ver `GameServer.TravessiaDoJuiz.cs`.
		new(PrefixoDeHabilidade + World.TravessiaDoJuiz.Verbo, World.TravessiaDoJuiz.Rotulo, AbaSkills,
			Cargo(World.TravessiaDoJuiz.Cargo).Ou(Fato(Fatos.VoltaDoOutroMundo, "vivo no Outro Mundo, com a volta guardada")),
			"só o King Yemma"),

		// (2026-10-23) O RITUAL MISTICO DO VELHO KAIOHSHIN -- o que entrou no kit do Kaioshin no lugar da skill do Mistico (o dono). E do
		// TRONO, como a travessia logo acima; e quem esta NO MEIO de um ritual (o alvo tambem) ve o botao enquanto ele dura -- e o que o
		// interrompe. Ver `GameServer.RitualDoKaioshin.cs`.
		new(PrefixoDeHabilidade + Ranks.RitualDoKaioshin.Verbo, Ranks.RitualDoKaioshin.Nome, AbaSkills,
			Cargo(Ranks.RitualDoKaioshin.Cargo).Ou(Fato(Fatos.RitualDoKaioshin, "no meio de um Ritual Místico")),
			"só o Kaioshin"),

		// ---------------------------------------------------------- Tecla E: o mainframe de androides
		// A MAQUINA E DE HUMANO: instalar o laboratorio e converter o proprio corpo pedem a raca
		// (`InstalarLab` e `VirarAndroide`, `GameServer.Tech.cs`). As duas primeiras linhas so ABREM uma pagina
		// do menu -- nao viajam, e por isso nao ha recusa a cobrar delas; as quatro de dentro viajam.
		// (2026-10-18) A PAGINA DOS LABORATORIOS E TAMBEM DO TSUFURUJIN: o Neo Tuffle Lab e dele (`GameServer.NeoTuffle.cs`). Dentro dela cada
		// laboratorio continua pedindo a raca dele -- o humano nao ve o do Tsufurujin, e o Tsufurujin nao ve os dois do humano.
		new(ChaveDoVerbo("lab_menu", "labs"), "Instalar laboratório...", TeclaEMainframe, Raca("Human", "Tsujin")),
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
		// (2026-10-18) O NEO TUFFLE LAB E A CONVERSAO DENTRO DELE: do Tsufurujin (`InstalarLab` e `VirarNeoTuffle`, `GameServer.NeoTuffle.cs`).
		new(ChaveDoVerbo("lab_tuffle"), "Neo Tuffle Lab", TeclaEMainframe, Raca("Tsujin"),
			"perto de um mainframe|o Neo Tuffle Lab pede um Tsufurujin"),
		new(ChaveDoVerbo("tuffle_menu", "tuffle"), "Virar Neo Tuffle...", TeclaEMainframe, Raca("Tsujin")),
		new(ChaveDoVerbo("virar_tuffle"), "Neo Tuffle (1.000.000 zeni)", TeclaEMainframe, Raca("Tsujin"),
			"dentro de um Neo Tuffle Lab|corpo de um Tsufurujin"),
		new(ChaveDoVerbo("virar_tuffle", Economia.Recurso.PalavraDeRecursos), "Neo Tuffle (2.000.000 recursos)",
			TeclaEMainframe, Raca("Tsujin"), "dentro de um Neo Tuffle Lab|corpo de um Tsufurujin"),
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
