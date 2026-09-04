using Godot;

namespace Jandirus.Client;

/// <summary>
/// UMA ACAO QUE O JOGADOR PODE MANDAR FAZER. E o "verb" do BYOND.
///
/// No DreamMaker um verb era declarado junto da mecânica (`mob/verb/Kamehameha()`) e o motor
/// juntava tudo sozinho num painel, agrupado por `set category`. Aqui nao ha motor que faca
/// isso, entao o encontro acontece no <see cref="Verbos"/>: quem implementa a skill registra o
/// verb, e ele aparece no menu, na aba certa, e entra na busca -- sem que o menu precise
/// conhecer skill nenhuma.
/// </summary>
/// <param name="Nome">Como aparece no painel. E tambem o que a busca casa.</param>
/// <param name="Categoria">A ABA em que ele mora: "Skills", "Learning", "Other", "Admin".</param>
/// <param name="Descricao">Uma linha explicando. Vira a dica ao passar o mouse.</param>
/// <param name="Acionar">
/// O que fazer. Normalmente mandar um pacote pro servidor.
///
/// NULO E LEGITIMO e agora esta escrito no tipo: as faixas PASSIVAS de uma disciplina divina
/// entram no catalogo sem acao (`Habilidades.DasDisciplinas`) porque o jogador precisa saber que
/// as tem -- passiva que ninguem sabe que ganhou e indistinguivel de passiva quebrada. O tipo dizia
/// `Action` nao-anulavel e o `null` passava com aviso do compilador; quem clicasse numa dessas
/// linhas no menu levava uma `NullReferenceException`. Os dois unicos consumidores conferem agora.
/// </param>
/// <param name="Disponivel">
/// Quando o verb PODE ser usado agora (Ki suficiente, nao estar caido, cooldown). Nulo = sempre.
/// Um verb indisponivel continua APARECENDO, so que apagado -- saber que a tecnica existe e que
/// falta Ki e informacao; sumir com ela e esconder o jogo do jogador.
/// </param>
public sealed record Verbo(
	string Nome,
	string Categoria,
	string Descricao,
	Action? Acionar,
	Func<bool>? Disponivel = null)
{
	public bool PodeAgora => Disponivel == null || Disponivel();

	/// <summary>
	/// O ID ESTAVEL DESTE VERB -- o que uma tecla ligada a ele guarda no `config.json`.
	///
	/// ============================ POR QUE O NOME NAO SERVE ============================
	/// Porque o <see cref="Nome"/> MUDA COM O ESTADO. Sao quatro casos no `Habilidades.cs`, e todos
	/// acontecem no jogo normal:
	///
	///   * a disciplina divina vira `"Ultra Instinto  (ligada)"` quando ligada;
	///   * a faixa passiva carrega a porcentagem no nome (`"Sign  (20%)"`);
	///   * o estilo vira `"Turtle  (em uso)"` enquanto e o estilo atual;
	///   * a tecnica ainda nao portada vira `"Kamehameha (nao portada)"` -- e volta ao nome seco
	///     no dia em que o efeito sair.
	///
	/// Uma tecla gravada pelo nome se perderia no instante em que o jogador LIGASSE o Ultra
	/// Instinto. E o id estavel ja existia em toda fonte (`Tecnicas.Tecnica.Id`, `EstiloInfo.Id`,
	/// `TecnicaCustomizada.Verbo`, o `cmd` do `SendVerbo`) -- ele so era jogado fora ao montar o
	/// `Verbo`. Este campo e o lugar onde ele para de ser jogado fora.
	///
	/// VAZIO CAI NO NOME (ver <see cref="ChaveOuNome"/>), e e o certo pros 65 verbs fixos do
	/// `VerbosDoJogo`: o nome deles e literal no fonte e nao muda em tempo de execucao. Quem
	/// registra verb com nome MONTADO tem que preencher isto.
	/// =================================================================================
	/// </summary>
	public string Chave { get; init; } = "";

	/// <summary>Por onde uma tecla se lembra deste verb.</summary>
	public string ChaveOuNome => Chave.Length > 0 ? Chave : Nome;

	/// <summary>
	/// ESTE VERB APARECE PRA MIM? Nulo = sempre. E diferente de <see cref="Disponivel"/>: o
	/// indisponivel aparece APAGADO ("existe, e falta Ki"); o que nao se mostra NAO EXISTE pra este
	/// personagem -- o pedido do dono, literal: *"verbos de namek ... e alguns de rank deveriam
	/// aparecer somente se eu fosse namek ou tivesse o rank em questao"*. No original e assim que os
	/// verbs de cargo funcionam: eles entram em `verbs` quando o cargo e dado e saem quando ele se
	/// perde; ninguem ve "Fund Earth" sem ser o Presidente. Quem le isto e <see cref="Verbos.Visivel"/>.
	/// </summary>
	public Func<bool>? Mostrar { get; init; }
}

/// <summary>
/// O CATALOGO DE VERBS do personagem local.
///
/// Estatico e de propósito: e a lista do MEU personagem nesta sessao, do mesmo jeito que
/// `mob.verbs` era do mob. Quando uma skill e aprendida, ela registra; quando o personagem
/// troca (voltar pro menu de slots), <see cref="Limpar"/> zera tudo.
///
/// ESTA VAZIO AGORA, e isso e o estado correto: nenhuma skill foi portada ainda. O menu ja
/// mostra as abas Skills/Learning/Other com "nenhuma acao aqui" -- que e literalmente o que o
/// original escrevia ("No actions here.") quando a categoria estava vazia.
/// </summary>
public static class Verbos
{
	/// <summary>As categorias que viram aba, na ordem do BYOND (HtmlUI.dm:137).</summary>
	public const string Skills = "Skills";
	public const string Outros = "Other";
	public const string Aprendizado = "Learning";
	public const string Admin = "Admin";

	private static readonly List<Verbo> _todos = [];

	/// <summary>Avisa que a lista mudou -- o menu aberto se redesenha.</summary>
	public static event Action? Mudou;

	public static IReadOnlyList<Verbo> Todos => _todos;

	public static void Registrar(Verbo v)
	{
		// mesmo nome = mesmo verb. O original tambem deduplicava por nome (`if(vname in seen)`),
		// porque a mesma acao podia chegar pelo mob E por um objeto carregado.
		if (_todos.Any(x => x.Nome == v.Nome)) return;
		_todos.Add(v);
		Mudou?.Invoke();
	}

	public static void Esquecer(string nome)
	{
		if (_todos.RemoveAll(v => v.Nome == nome) > 0) Mudou?.Invoke();
	}

	public static void Limpar()
	{
		if (_todos.Count == 0) return;
		_todos.Clear();
		Mudou?.Invoke();
	}

	/// <summary>
	/// Os verbs de uma categoria QUE ESTE PERSONAGEM VE. O filtro de <see cref="Visivel"/> entra aqui,
	/// e nao so na busca: a aba Other e o mesmo balcao que a busca, e um verb de cargo alheio
	/// escondido de um lado e listado do outro seria a mesma porta com duas placas.
	/// </summary>
	public static IEnumerable<Verbo> Da(string categoria) =>
		_todos.Where(v => v.Categoria == categoria).Where(Visivel).OrderBy(v => v.Nome, StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// SOU ADMIN? Escrito de fora (pelo menu, quando a ficha lenta chega com o bit).
	///
	/// ============================ POR QUE A BUSCA PRECISA SABER ============================
	/// Esconder a ABA tira o atalho, e nao o acesso: a barra de busca varre TODAS as categorias de
	/// proposito ("ninguem lembra em que aba a tecnica mora"). Sem este filtro, qualquer jogador
	/// digitava "target" e recebia "Kill Target [Admin]", "Boot Target [Admin]", "Assess Target
	/// [Admin]" -- todos habilitados, porque `PodeAgora` so olha se ha alvo marcado.
	///
	/// O servidor recusa, claro. Mas o estrago ja esta feito antes do clique: a lista inteira dos
	/// poderes de administracao vira leitura publica, e o botao promete uma coisa que responde
	/// "isso e coisa de administrador" -- que e a definicao de botao que nao devia estar ali.
	/// =======================================================================================
	/// </summary>
	public static bool SouAdmin { get; private set; }

	public static void DefinirAdmin(bool sou)
	{
		if (SouAdmin == sou) return;
		SouAdmin = sou;
		Mudou?.Invoke();
	}

	/// <summary>Este verb pode sequer APARECER pra mim? Admin so pra admin; e o que o verb mesmo disser (<see cref="Verbo.Mostrar"/>).</summary>
	public static bool Visivel(Verbo v) => (SouAdmin || v.Categoria != Admin) && (v.Mostrar?.Invoke() ?? true);

	/// <summary>
	/// O verb que uma tecla guardou, ou nulo se este personagem nao o tem.
	///
	/// NULO E UM RESULTADO NORMAL e nao um erro: a ligacao e de MAQUINA (mora no `config.json`) e o
	/// catalogo e do PERSONAGEM. O Namekuseijin que virou Saiyajin continua com a tecla do
	/// "Regenerar" gravada -- e ela tem que dizer isso em vez de sumir. Ver `Atalhos.Disparar`.
	/// </summary>
	public static Verbo? PorChave(string chave) =>
		_todos.Find(v => string.Equals(v.ChaveOuNome, chave, StringComparison.Ordinal));

	/// <summary>
	/// Busca em TODAS as categorias. E o que o dono pediu ("barra de pesquisa pra procurar os
	/// verbs"): com o campo preenchido, a aba atual deixa de importar -- o que se procura e uma
	/// tecnica, e ninguem lembra em que aba ela mora.
	/// </summary>
	public static IEnumerable<Verbo> Buscar(string termo)
	{
		if (string.IsNullOrWhiteSpace(termo)) return [];
		string t = termo.Trim();
		return _todos
			.Where(Visivel)
			.Where(v => v.Nome.Contains(t, StringComparison.OrdinalIgnoreCase)
					 || v.Descricao.Contains(t, StringComparison.OrdinalIgnoreCase))
			.OrderBy(v => v.Nome.StartsWith(t, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
			.ThenBy(v => v.Nome, StringComparer.OrdinalIgnoreCase);
	}
}
