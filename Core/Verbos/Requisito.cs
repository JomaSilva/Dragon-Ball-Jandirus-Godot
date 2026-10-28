namespace Jandirus.Core.Verbos;

/// <summary>
/// OS FATOS SOBRE UM PERSONAGEM QUE SO O SERVIDOR SABE, e que decidem se um verb e DELE.
///
/// ============================ POR QUE ISTO VIAJA, E POR QUE EM BITS ============================
/// Raca, classe, skills, mochila e cargos o cliente ja recebe. O que ele nao recebe e o resto do que um
/// verb pode exigir: se este androide saiu do pod de absorcao, se ha um convite de fusao na mesa, se alguem
/// e aluno deste corpo. Sem isso o menu so tinha duas saidas -- mostrar o botao pra todo mundo e deixar o
/// servidor recusar (o que o dono chamou de poluido, 2026-10-10: *"verbs q sao exclusivas de racas e
/// deveriam so aparecer se o player tem os requisitos"*) ou chutar.
///
/// Sao bits e nao um pacote por sistema porque a pergunta e sempre a mesma ("isto e verdade pra mim
/// agora?") e a resposta muda devagar: um `ulong` que sai so quando muda (`S2C.Fatos`, ver
/// `GameServer.Fatos.cs`). Fato novo = um bit aqui, uma linha la, uma linha na tabela de requisitos.
///
/// O QUE **NAO** E FATO: circunstancia. "Estou perto de um banco", "tenho Ki", "marquei um alvo" mudam a
/// cada passo e nao tiram o verb de ninguem -- o botao aparece e o servidor diz o que falta.
/// ============================================================================================
/// </summary>
[Flags]
public enum Fatos : ulong
{
	Nenhum = 0,

	// ---- o que o corpo E, e a ficha lenta nao conta ----
	/// <summary>Androide convertido no pod de ABSORCAO (`Fighter.AndroideAbsorcao`): tem coletores de energia.</summary>
	AndroideDeAbsorcao = 1u << 0,
	/// <summary>O ki divino esta desperto (`hasgodki`): pode desafiar o Deus da Destruicao.</summary>
	GodKiDesperto = 1u << 1,
	/// <summary>
	/// (2026-10-19) A raca deste corpo tem o vigor infinito (`Stats.Nutricao.VigorInfinitoDaRaca`: o morto comum nao conta). Nao abre
	/// verb: quem le e a mesa de tecnicas, que nao lhe vende folego (`Skills.TecnicaCustomizada.VendeFolego`).
	/// </summary>
	VigorInfinito = 1u << 30,

	// ---- o que este personagem TEM no mundo ----
	/// <summary>Tem um mestre (o discipulado): pode deixa-lo.</summary>
	TemMestre = 1u << 2,
	/// <summary>Guia ao menos um aluno: pode ensina-lo e dispensa-lo.</summary>
	TemAluno = 1u << 3,
	/// <summary>A bandeira de conquista DESTE planeta e dele.</summary>
	DominaEstePlaneta = 1u << 4,
	/// <summary>Domina ao menos um planeta, em qualquer lugar.</summary>
	DominaAlgum = 1u << 5,
	/// <summary>Ergueu a Estatua do Dragao DESTE mundo (so o criador a desperta e a reconfigura).</summary>
	CriouAEstatuaDaqui = 1u << 6,
	/// <summary>Esta fundido e dirige o corpo: pode passar o volante.</summary>
	DirigeUmaFusao = 1u << 7,
	/// <summary>Tem copias do Split Form de pe -- inclusive as que ficaram de um verb emprestado.</summary>
	TemCopias = 1u << 8,

	// ---- o que esta NA MESA esperando resposta dele ----
	/// <summary>Convite de mestre, ou ensinamento de forma, pendente.</summary>
	ConviteDeMestre = 1u << 9,
	/// <summary>Convite de fusao (Danca ou Potara) pendente.</summary>
	ConviteDeFusao = 1u << 10,
	/// <summary>Convite de ABSORCAO Namekuseijin pendente -- o que custa o personagem.</summary>
	ConviteDeAbsorcao = 1u << 11,
	/// <summary>Alguem esta lhe ensinando uma skill agora.</summary>
	LicaoOferecida = 1u << 12,
	/// <summary>Oferta de juventude (`Restore_Youth`) pendente.</summary>
	OfertaDeJuventude = 1u << 13,
	/// <summary>Oferta de despertar o potencial (`Unlock_Potential`) pendente.</summary>
	OfertaDePotencial = 1u << 14,
	/// <summary>Uma Genkidama esta pedindo a energia dele.</summary>
	PedidoDeGenkidama = 1u << 15,
	/// <summary>O Grande Anciao lhe ofereceu um assento de Anciao.</summary>
	OfertaDeAssento = 1u << 16,
	/// <summary>Ele carrega o titulo de Deus da Destruicao e ha um desafio formal esperando a resposta dele.</summary>
	DesafioAoTitulo = 1u << 17,

	// ---- (o que o corpo E, de novo: os bits acima ja tinham lugar no fio) ----
	/// <summary>
	/// Abriu a escada do Super Saiyajin SEM ser Saiyajin (`Fighter.canSSJ`: o bio-androide de DNA Saiyajin) --
	/// e quem tem a escada tem os graus dela pra ligar e desligar.
	/// </summary>
	EscadaSuperSaiyajin = 1u << 18,

	/// <summary>
	/// E o Deus da Destruicao E trilha o Ultra Instinto: o titulo lhe EMPRESTA a Aura of Destruction e a Hakai
	/// Infusion (`god_apply_powers`, `GodOfDestruction.dm:134-138`). Cai com o titulo.
	/// </summary>
	KitDaDestruicaoEmprestado = 1u << 19,

	// ---- (v0.2.1) o que este personagem CARREGA, e a mochila sozinha nao diz ----
	/// <summary>
	/// Carrega uma arma de fogo (ou o atordoador): o gatilho de tecla e dele. E FATO e nao `Item(...)` porque o id de
	/// uma arma leva o estado dela (`Handgun|28|30|...`) e a pergunta da mochila e por id inteiro.
	/// </summary>
	TemArmaDeFogo = 1u << 20,

	// ---- (2026-10-17) a familia ----
	/// <summary>Declarou AMOR a alguem que esta no jogo e foi correspondido: o verb de ter um filho e dele.</summary>
	TemUmAmor = 1u << 21,
	/// <summary>Alguem pediu pra ter um filho com ele e espera a resposta.</summary>
	PedidoDeFilho = 1u << 22,
	/// <summary>A raca poe ovo e o corpo ja tem idade pra gerar (`Filhos.IdadeParaGerar`): o verb de por um ovo e dele.</summary>
	PoeOvo = 1u << 23,

	// ---- (2026-10-17) o bio-androide de laboratorio e a larva ----
	/// <summary>Nasceu no laboratorio de DNA (`Fighter.bio_lab_born`): veste a forma de larva quando quiser.</summary>
	BioDeLaboratorio = 1u << 24,
	/// <summary>E uma LARVA agora (`BioAndroids.EmLarva`): so ela se enterra -- e enterrada 5 min, amadurece.</summary>
	EmLarva = 1u << 25,
	/// <summary>Bio de laboratorio que ainda nao escolheu onde mora o NUCLEO: os cinco botoes da escolha sao dele (uma vez, pra sempre).</summary>
	NucleoPorEscolher = 1u << 26,

	// ---- (2026-10-17) as skills do DNA do bio-androide ----
	/// <summary>
	/// Bio-androide com VAGA no DNA: o sangue ainda tem skill de raca pra dar e ele escolheu menos de seis -- os botoes da escolha sao dele
	/// (`Races.SkillsRaciais`).
	/// </summary>
	SkillDoDnaPorEscolher = 1u << 27,

	// ---- (2026-10-18) o Neo Tuffle ----
	/// <summary>Passou pela maquina do Neo Tuffle Lab (`Fighter.NeoTuffle`), e esta no proprio corpo: o verb de possuir um nocauteado e dele.</summary>
	NeoTuffle = 1u << 28,

	// ---- (2026-10-19) o King Yemma ----
	/// <summary>
	/// Esta VIVO no Outro Mundo com a volta do juiz guardada (`Fighter.VoltaDoJuizZona`): o botao da travessia e dele mesmo sem o
	/// trono -- quem perdeu o cargo do lado de la nao fica preso entre os mortos. Ver `GameServer.TravessiaDoJuiz.cs`.
	/// </summary>
	VoltaDoOutroMundo = 1u << 29,

	// ---- (2026-10-19) a vaga reciclada ----
	/// <summary>
	/// A Estatua do Dragao DESTE mundo perdeu o criador (ele foi apagado ou reencarnou) e esta inerte: um Namekuseijin do Cla do Dragao a
	/// desperta e vira o criador dela. Ver `GameServer.VagaReciclada.cs`. Era O ULTIMO BIT de um `uint` -- o fio virou `ulong` com o fato
	/// seguinte (2026-10-19, o aprendiz de Kaioshin).
	/// </summary>
	EstatuaSemCriadorAqui = 1u << 31,

	// ---- (2026-10-19) o aprendiz de Kaioshin: o PRIMEIRO bit do `ulong` ----
	/// <summary>Um Kaioshin se ofereceu pra te tomar como aprendiz (`aprendiz_aceitar`/`aprendiz_recusar`). Ver `GameServer.AprendizDeKaioshin.cs`.</summary>
	OfertaDeAprendiz = 1ul << 32,

	// ---- (2026-10-23) o Ritual Mistico do Velho Kaiohshin ----
	/// <summary>
	/// Esta NO MEIO de um Ritual Mistico do Velho Kaiohshin -- como o Kaioshin ou como o alvo. Os dois estao travados no lugar, e o botao
	/// do ritual, que e do trono, passa a ser tambem de quem esta nele: e o que o INTERROMPE (o alvo nao fica preso dez minutos sem
	/// saida). Ver `GameServer.RitualDoKaioshin.cs`.
	/// </summary>
	RitualDoKaioshin = 1ul << 33,

	// ---- (2026-10-28, o pedido 144) o Neo Tuffle que sai quando quiser ----
	/// <summary>
	/// Esta DENTRO do corpo de outro jogador (a posse do Neo Tuffle): o botao "Sair do corpo" e dele. O irmao do <see cref="NeoTuffle"/>, que
	/// so acende no proprio corpo. Ver `GameServer.NeoTuffle.cs`.
	/// </summary>
	DentroDeOutroCorpo = 1ul << 34,
}

/// <summary>
/// ONDE O PERSONAGEM ESTA, no que importa pros botoes de LUGAR da aba World.
///
/// E recorte de lugar e nao de permissao, como ja era: reivindicar uma Super Esfera so acontece no espaco e
/// erguer estatua so em terra. O terceiro bit e novo e segue a mesma ideia -- um mundo sem Estatua do Dragao
/// nao tem esfera nenhuma (elas nascem da estatua e nunca saem do mundo dela), entao pegar, largar e invocar
/// ali sao botoes que nunca poderiam funcionar.
/// </summary>
[Flags]
public enum Lugar : byte
{
	Nenhum = 0,
	Planeta = 1 << 0,
	Espaco = 1 << 1,
	/// <summary>Este mundo tem uma Estatua do Dragao (e portanto um set de esferas).</summary>
	ComEstatua = 1 << 2,
}

/// <summary>
/// O QUE UM PERSONAGEM TEM -- as perguntas que um <see cref="Requisito"/> sabe fazer.
///
/// Interface e nao um retrato copiado: o cliente responde lendo o que o servidor ja lhe mandou (ficha lenta,
/// livro de skills, mochila, cargos, fatos) e o servidor responde lendo a propria verdade. As DUAS pontas
/// passam pelo mesmo `Requisito.Atende` -- e a bancada compara as duas respostas, que e o que prova que o
/// cliente sabe o bastante pra nao esconder o que e do jogador.
/// </summary>
public interface IPosse
{
	string Raca { get; }

	/// <summary>A linhagem/classe ("Dragon clan", "Elite"...).</summary>
	string Classe { get; }

	/// <summary>Um verb que o servidor ACEITA deste corpo (a lista do `SabeTecnica`; no cliente, `VerbosAtivos`).</summary>
	bool SabeTecnica(string verbo);

	/// <summary>Uma skill do livro, pelo typepath.</summary>
	bool SabeSkill(string typepath);

	/// <summary>Ao menos um deste item na mochila.</summary>
	bool TemItem(string id);

	/// <summary>Carrega este cargo (as chaves sao as de `Core/Ranks/Ranks.cs`).</summary>
	bool TemCargo(string chave);

	Fatos Fatos { get; }
	Lugar Lugar { get; }

	/// <summary>
	/// (2026-10-17) As skills dos kits de RACA que valem pra este corpo (`Races.SkillRacial`): o kit inteiro da raca, ou as que o bio-androide
	/// escolheu do sangue. No cliente, a ficha lenta (`AtributosState.SkillsRaciais`).
	/// </summary>
	Races.SkillRacial SkillsRaciais { get; }

	/// <summary>(2026-10-17) As skills que o SANGUE do bio-androide da pra escolher (zero pra quem nao e bio).</summary>
	Races.SkillRacial SkillsDoSangue { get; }
}

/// <summary>Uma posse escrita a mao -- as bancadas montam personagens com ela, e o servidor tira o retrato de um corpo.</summary>
public sealed class PosseEscrita : IPosse
{
	public string Raca { get; set; } = "";
	public string Classe { get; set; } = "";
	public HashSet<string> Tecnicas { get; } = new(StringComparer.OrdinalIgnoreCase);
	public HashSet<string> Skills { get; } = new(StringComparer.OrdinalIgnoreCase);
	public HashSet<string> Itens { get; } = new(StringComparer.Ordinal);
	public HashSet<string> Cargos { get; } = new(StringComparer.OrdinalIgnoreCase);
	public Fatos Fatos { get; set; }
	public Lugar Lugar { get; set; }

	/// <summary>Escritas a mao; sem escrever, as do kit da raca e da classe -- o que o servidor da a quem nao e bio.</summary>
	public Races.SkillRacial SkillsRaciais
	{
		get => _skillsRaciais ?? Races.SkillsRaciais.DoKit(Races.Passivas.KitDe(Raca, Classe, ""));
		set => _skillsRaciais = value;
	}

	private Races.SkillRacial? _skillsRaciais;

	public Races.SkillRacial SkillsDoSangue { get; set; }

	public bool SabeTecnica(string verbo) => Tecnicas.Contains(verbo);
	public bool SabeSkill(string typepath) => Skills.Contains(typepath);
	public bool TemItem(string id) => Itens.Contains(id);
	public bool TemCargo(string chave) => Cargos.Contains(chave);
}

/// <summary>
/// **O REQUISITO DE UM VERB** -- o que o personagem precisa TER pra o botao existir pra ele.
///
/// ============================ UMA REGRA SO, E ELA E DADO ============================
/// Antes havia tres jeitos de um botao sumir: um `if (raca == ...)` em volta do registro, uma lambda
/// `Mostrar` no verb, e -- o mais comum -- jeito nenhum: o botao aparecia e o servidor recusava. Um
/// requisito e um valor: da pra escrever numa tabela, compor (`E`, `Ou`), imprimir (`Texto`, que vai
/// pro relatorio e pra bancada) e perguntar das duas pontas com a mesma funcao.
///
/// ESCONDER NAO E PERMISSAO. O servidor continua validando tudo; isto so decide o que APARECE.
/// ====================================================================================
/// </summary>
public sealed class Requisito
{
	/// <summary>
	/// DEFEITO INJETADO (bancadas `--verbsteste` e `--diagverbs`): todo requisito responde SIM -- o botao que
	/// mostra tudo, que e o menu de antes de 2026-10-10 (verb do Cla do Dragao na tela de um Saiyajin). Falso
	/// em jogo, sempre.
	/// </summary>
	public static bool MostrarTudoDeTeste;

	private readonly Func<IPosse, bool> _atende;

	/// <summary>Como se le: "Namekuseijin e classe Dragon clan". Vai pra tabela do relatorio.</summary>
	public string Texto { get; }

	/// <summary>Sem requisito nenhum? (O verb que ninguem declarou cai aqui, e aparece.)</summary>
	public bool Livre { get; }

	private Requisito(string texto, Func<IPosse, bool> atende, bool livre = false)
	{
		Texto = texto;
		_atende = atende;
		Livre = livre;
	}

	public bool Atende(IPosse posse) => MostrarTudoDeTeste || _atende(posse);

	public override string ToString() => Texto;

	/// <summary>Qualquer um. E o requisito de quem nao declarou nenhum.</summary>
	public static readonly Requisito Nenhum = new("qualquer um", _ => true, livre: true);

	/// <summary>Uma destas racas (a raca do corpo, como o servidor compara).</summary>
	public static Requisito Raca(params string[] racas) => new(
		"raça " + string.Join(" ou ", racas),
		p => racas.Any(r => string.Equals(p.Raca, r, StringComparison.OrdinalIgnoreCase)));

	public static Requisito Classe(string classe) => new(
		$"classe {classe}",
		p => string.Equals(p.Classe, classe, StringComparison.OrdinalIgnoreCase));

	/// <summary>Sabe ao menos um destes verbs de tecnica (skill comprada, degrau de nivel ou emprestado).</summary>
	public static Requisito Tecnica(params string[] verbos) => new(
		"técnica " + string.Join(" ou ", verbos),
		p => verbos.Any(p.SabeTecnica));

	/// <summary>Tem ao menos uma destas skills no livro (typepath).</summary>
	public static Requisito Skill(params string[] typepaths) => new(
		"skill " + string.Join(" ou ", typepaths.Select(t => t[(t.LastIndexOf('/') + 1)..])),
		p => typepaths.Any(p.SabeSkill));

	public static Requisito Item(params string[] ids) => new(
		"item " + string.Join(" ou ", ids),
		p => ids.Any(p.TemItem));

	public static Requisito Cargo(params string[] chaves) => new(
		"cargo " + string.Join(" ou ", chaves),
		p => chaves.Any(p.TemCargo));

	/// <summary>Carrega um dos cargos que passam neste crivo -- pra quando a lista sai de uma tabela (os cargos com deveres).</summary>
	public static Requisito CargoQue(string texto, Func<Ranks.RankDef, bool> crivo) => new(
		texto,
		p =>
		{
			foreach (Ranks.RankDef r in Ranks.Cargos.Todos)
				if (crivo(r) && p.TemCargo(r.Chave)) return true;
			return false;
		});

	/// <summary>Ao menos um destes fatos e verdade agora (ver <see cref="Fatos"/>).</summary>
	public static Requisito Fato(Fatos algum, string texto) => new(texto, p => (p.Fatos & algum) != 0);

	/// <summary>(v0.4.0) O KIT RACIAL que a raca e a classe dao (`Races.Passivas.KitDe`) -- as ativas dos kits de CLASSE.</summary>
	public static Requisito Kit(Races.KitRacial kit, string texto) =>
		new(texto, p => (Races.Passivas.KitDe(p.Raca, p.Classe, "") & kit) != 0);

	/// <summary>
	/// (2026-10-17) Esta skill de kit de RACA vale pra este corpo (`Races.SkillRacial`): o kit inteiro da raca, ou a escolha do bio-androide --
	/// as ativas dos cinco kits de raca (Second Wind, Sheer Willpower, Max Power, Spirit Disruption, Unrestrained Wrath).
	/// </summary>
	public static Requisito DoKitDeRaca(Races.SkillRacial skill, string texto) => new(texto, p => (p.SkillsRaciais & skill) != 0);

	/// <summary>(2026-10-17) O sangue do bio-androide da esta skill e ele ainda nao a tem -- um botao da escolha do DNA.</summary>
	public static Requisito NoSangueSemEscolher(Races.SkillRacial skill, string texto) =>
		new(texto, p => (p.SkillsDoSangue & skill) != 0 && (p.SkillsRaciais & skill) == 0);

	/// <summary>Esta num lugar que tem TODOS estes tracos.</summary>
	public static Requisito Em(Lugar todos, string texto) => new(texto, p => (p.Lugar & todos) == todos);

	/// <summary>Esta num lugar que NAO tem nenhum destes tracos.</summary>
	public static Requisito ForaDe(Lugar nenhum, string texto) => new(texto, p => (p.Lugar & nenhum) == 0);

	public Requisito E(Requisito outro) =>
		Livre ? outro : outro.Livre ? this
		: new($"{Entre(this)} e {Entre(outro)}", p => _atende(p) && outro._atende(p));

	public Requisito Ou(Requisito outro) =>
		Livre || outro.Livre ? Nenhum
		: new($"{Texto} ou {outro.Texto}", p => _atende(p) || outro._atende(p)) { _ehOu = true };

	/// <summary>So pro texto: um "ou" dentro de um "e" vai entre parenteses, senao a frase mente a precedencia.</summary>
	private bool _ehOu;

	private static string Entre(Requisito r) => r._ehOu ? $"({r.Texto})" : r.Texto;
}
