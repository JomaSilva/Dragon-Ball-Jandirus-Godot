using System.Collections;
using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Social;
using Jandirus.Net;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA ABA PEOPLE (`--diagconhecidos`) -- a aba redesenhada, andada inteira e FOTOGRAFADA.
///
/// ============================ O PEDIDO DO DONO (2026-10-10) ============================
/// *"tela de conhecidos (people) a galera ta se confundindo MUITO com ela, faca um revamp dela pra deixar mais
/// intuitivo ... seria melhor so deixar o player escolher oq aquela pessoa e pra ele e tirar isso de afinidade e
/// so deixar convivio e declaracao"*.
///
/// "Intuitivo" nao se mede; o que se mede e o CONTRATO que o redesenho promete, e a foto pra alguem discordar:
///
///   * cada cartao tem o convivio (UM numero, UMA barra) e a regua de nove respostas pra UMA pergunta -- e mais
///     nenhum rotulo de estado (nenhuma pilula, nenhuma segunda barra, nenhuma palavra da afinidade antiga);
///   * na regua, exatamente uma opcao esta acesa (a que eu declarei; o neutro pra quem nunca disse nada), as que
///     o convivio paga estao abertas e as outras apagadas com o convivio que pedem ESCRITO nelas;
///   * a linha de efeito e a que o `Core` responde pra escolha acesa;
///   * clicar numa opcao aberta muda a declaracao NO SERVIDOR, e a aba se refaz sozinha com a lista que voltou;
///   * o portao e do servidor: um pacote forjado pedindo uma opcao apagada e recusado.
///
/// E O SEGUNDO PEDIDO, DO MESMO DIA -- *"clicar em pessoas q vc conhece (tem q ter um certo nivel de convivio
/// minimo) vc pode ver mais detalhes da pessoa, como idade, a backstory dela"*:
///
///   * todo cartao tem o botao de detalhes; pra quem o convivio nao paga ele traz o preco e abre o cartao
///     TRANCADO, com a frase do que falta -- e a tela nem manda o pedido;
///   * pra quem paga, o clique manda UM verb `detalhes`, o servidor responde UM pacote, e o cartao abre no
///     lugar com a idade, o genero, o cargo, as duas datas e a historia;
///   * o portao e do servidor: o pedido forjado por cima do botao trancado nao rende pacote nenhum.
///
/// TUDO PELO CAMINHO DE PRODUCAO: a lista chega pelo fio (`S2C.Conhecidos`), o clique e o sinal do botao, o
/// verb e o `relacao` de sempre. So o ELENCO e plantado (no servidor, `PlantarConhecidosDeTeste`): convivio de
/// verdade leva dezenas de minutos ao lado de outro jogador, e a bancada nasce sozinha.
/// =========================================================================================
///
/// COMO RODAR -- um processo so (modo host: ele le o servidor do mesmo processo), com JANELA no segundo monitor
/// (no headless o `GetImage` volta vazio e as fotos sao puladas):
///
///     --host --rede 8171 --horateste 0.5 --diagconhecidos --raca Human --conta bancada_conhecidos_ag
///     --nome Goku --pasta &lt;pasta das fotos&gt;
///
/// As fotos saem em `--pasta` (ou em `user://`): `conhecidos-1-vazia`, `-2-lista`, `-3-declarou-amigo`,
/// `-4-fim-da-lista`, `-5-cartao` (o recorte de um cartao, ampliado), `-6-detalhes-trancados` e
/// `-7-detalhes-abertos` (cada uma com o recorte do cartao, `...-cartao`) e `-0-historia` (as telas lado a lado).
/// </summary>
public partial class RoboDeConhecidos : Node
{
	// =====================================================================
	// O CONTRATO DO CARTAO -- puro, pra bancada das abas (`--diagabas` F10) usar o mesmo
	// =====================================================================

	/// <summary>Uma opcao da regua, como esta na tela.</summary>
	internal readonly record struct OpcaoLida(Relacao Qual, string Texto, bool Apagada, bool Escolhida);

	/// <summary>
	/// Os DETALHES de um cartao, como estao na tela. <c>Estado</c>: "" (cartao fechado), "trancados", "buscando" ou
	/// "abertos" -- o metadado `detalhes` que a aba escreve. <c>Aviso</c> e a frase do estado trancado/buscando;
	/// <c>Fatos</c> sao as linhas rotulo/valor do estado aberto; <c>Historia</c> e o texto da caixa de historia
	/// (nulo = nao ha caixa) e <c>HistoriaEDela</c> diz se e a historia mesmo ou a frase de "ainda nao ouvi".
	/// </summary>
	internal sealed record DetalhesLidos(List<string> Botoes, string Estado, string Aviso,
										 List<(string Rotulo, string Valor)> Fatos, string? Historia, bool HistoriaEDela)
	{
		/// <summary>O que se le de um cartao FECHADO cujo convivio paga os detalhes: so o botao.</summary>
		internal static DetalhesLidos Fechados(string botao) => new([botao], "", "", [], null, false);
	}

	/// <summary>Um cartao de pessoa, como esta na tela -- o que a regra do contrato le.</summary>
	internal sealed record CartaoLido(string Nome, int Convivio, List<OpcaoLida> Opcoes, string Efeito, string Pergunta,
									  bool TemBoneco, bool SemFoto, int Pilulas, int Barras, DetalhesLidos Detalhes);

	/// <summary>
	/// Le um cartao de pessoa da aba People pelos metadados que a aba escreve (`declaracao`, `escolhida`, `efeito`,
	/// `linha`, `detalhes`, `historia`).
	/// </summary>
	internal static CartaoLido Ler(Control cartao)
	{
		string nome = cartao.HasMeta("titulo") ? cartao.GetMeta("titulo").AsString() : "";
		var opcoes = new List<OpcaoLida>();
		int convivio = -1, pilulas = 0, barras = 0;
		string efeito = "", pergunta = "", estado = "", aviso = "";
		bool boneco = false, semFoto = false, historiaEDela = false;
		var botoes = new List<string>();
		var fatos = new List<(string Rotulo, string Valor)>();
		string? historia = null;

		foreach (Node n in Todos(cartao))
		{
			switch (n)
			{
				case Button b when b.HasMeta("declaracao"):
					opcoes.Add(new OpcaoLida((Relacao)b.GetMeta("declaracao").AsInt32(), b.Text, b.Disabled,
											 b.HasMeta("escolhida") && b.GetMeta("escolhida").AsBool()));
					break;
				case Button b when b.HasMeta("detalhes"): botoes.Add(b.Text); break;
				case PanelContainer p when p.HasMeta("pilula"): pilulas++; break;
				case ProgressBar: barras++; break;
				case CharacterVisual: boneco = true; break;
				case Label l:
					if (l.Name.ToString() == "SemFoto") semFoto = true;
					if (l.HasMeta("efeito")) efeito = l.Text;
					if (l.Text == MenuJogo.PerguntaDaRegua.ToUpperInvariant()) pergunta = l.Text;
					if (l.HasMeta("detalhes")) { estado = l.GetMeta("detalhes").AsString(); aviso = l.Text; }
					if (l.HasMeta("historia")) { historia = l.Text; historiaEDela = l.GetMeta("historia").AsBool(); }
					break;
				case HBoxContainer h when h.HasMeta("linha") && h.GetMeta("linha").AsString() == MenuJogo.RotuloDoConvivio:
					if (h.GetChildCount() >= 2 && h.GetChild(1) is Label v && int.TryParse(v.Text, out int x)) convivio = x;
					break;
				case VBoxContainer f when f.HasMeta("detalhes"):
					estado = f.GetMeta("detalhes").AsString();
					foreach (HBoxContainer linha in Todos(f).OfType<HBoxContainer>().Where(h => h.HasMeta("linha")))
						fatos.Add((linha.GetMeta("linha").AsString(), linha.GetChildCount() >= 2 && linha.GetChild(1) is Label valor ? valor.Text : ""));
					break;
			}
		}
		return new CartaoLido(nome, convivio, opcoes, efeito, pergunta, boneco, semFoto, pilulas, barras,
							  new DetalhesLidos(botoes, estado, aviso, fatos, historia, historiaEDela));
	}

	/// <summary>
	/// O CONTRATO DOS DETALHES de um cartao (segundo pedido do dono, 2026-10-10): devolve o que esta ERRADO, dado o
	/// convivio que o servidor guarda. O botao existe SEMPRE (com o preco quando o convivio nao paga); abaixo do
	/// minimo o cartao aberto so pode estar TRANCADO, e nenhum fato nem historia aparecem; aberto, as linhas sao
	/// uma lista FECHADA -- uma linha de poder, de classe ou de lugar entrando ali reprova aqui.
	/// </summary>
	internal static List<string> ProblemasDosDetalhes(DetalhesLidos d, int convivio)
	{
		var ruim = new List<string>();
		int minimo = Convivio.ConvivioParaDetalhes;
		bool paga = convivio >= minimo;
		bool mostraAlgo = d.Fatos.Count > 0 || d.Historia != null;

		string botao = d.Estado.Length > 0 ? "Fechar detalhes" : paga ? "Ver detalhes" : $"Detalhes ({minimo})";
		if (d.Botoes.Count != 1 || d.Botoes[0] != botao)
			ruim.Add($"o botao de detalhes devia ser um so, '{botao}': [{string.Join("|", d.Botoes)}]");

		switch (d.Estado)
		{
			case "":
				if (mostraAlgo) ruim.Add("cartao FECHADO mostrando detalhe (fato ou historia)");
				break;
			case "trancados":
				if (paga) ruim.Add($"detalhes TRANCADOS com {convivio} de convivio (o minimo e {minimo})");
				if (mostraAlgo) ruim.Add("cartao TRANCADO mostrando detalhe (fato ou historia)");
				if (!d.Aviso.Contains($"{minimo} de convívio") || !d.Aviso.Contains($"têm {convivio}"))
					ruim.Add($"a frase do trancado nao diz o que falta (o minimo e o que ha): '{d.Aviso}'");
				break;
			case "buscando":
				if (!paga) ruim.Add($"buscando detalhes com {convivio} de convivio (o minimo e {minimo})");
				if (mostraAlgo) ruim.Add("cartao BUSCANDO ja mostrando detalhe");
				break;
			case "abertos":
				if (!paga) ruim.Add($"detalhes ABERTOS com {convivio} de convivio (o minimo e {minimo})");
				// A LISTA FECHADA, na ordem. Duas linhas podem FALTAR -- o genero (raca sem genero) e "voces se
				// conhecem" (ficha de antes de a data existir) --, e nenhuma outra pode ENTRAR.
				string[] lista = ["Idade", "Gênero", "Cargo", "Vocês se conhecem", "Visto por último"];
				string[] obrigatorias = ["Idade", "Cargo", "Visto por último"];
				List<string> rotulos = [.. d.Fatos.Select(f => f.Rotulo)];
				if (!rotulos.SequenceEqual(lista.Where(rotulos.Contains)) || obrigatorias.Except(rotulos).Any())
					ruim.Add("as linhas de detalhe nao sao a lista fechada (idade, genero, cargo, datas): " + string.Join(",", rotulos));
				if (d.Fatos.Any(f => f.Valor.Length == 0)) ruim.Add("linha de detalhe sem valor");
				if (d.Historia == null) ruim.Add("cartao aberto sem a caixa da historia");
				break;
			default:
				ruim.Add($"estado de detalhes desconhecido: '{d.Estado}'");
				break;
		}
		return ruim;
	}

	/// <summary>
	/// O CONTRATO DE UM CARTAO: devolve o que esta ERRADO nele (vazio = certo), dado o convivio e a declaracao
	/// que o servidor guarda. Pura -- as injecoes forjam um `CartaoLido` torto e cobram que ela reclame.
	/// </summary>
	internal static List<string> ProblemasDoCartao(CartaoLido c, int convivio, Relacao declarada)
	{
		var ruim = new List<string>();
		Relacao acesa = declarada == Relacao.Nenhuma ? Relacao.Neutro : declarada;

		if (c.Convivio != convivio) ruim.Add($"o convivio escrito e {c.Convivio}, o do servidor e {convivio}");
		if (c.Barras != 1) ruim.Add($"{c.Barras} barra(s) no cartao (so a do convivio)");
		if (c.Pilulas != 0) ruim.Add($"{c.Pilulas} pilula(s) de estado no cartao (o estado e a opcao acesa da regua, e so ela)");
		if (c.Pergunta.Length == 0) ruim.Add("a pergunta da regua nao esta escrita");
		if (c.TemBoneco == c.SemFoto) ruim.Add("o retrato tem que ser o boneco OU 'sem foto' (nem os dois, nem nenhum)");

		if (!c.Opcoes.Select(o => o.Qual).SequenceEqual(Convivio.Declaraveis))
			ruim.Add("a regua nao tem as 9 declaracoes na ordem do Core: " + string.Join(",", c.Opcoes.Select(o => o.Qual)));
		List<OpcaoLida> acesas = [.. c.Opcoes.Where(o => o.Escolhida)];
		if (acesas.Count != 1 || acesas[0].Qual != acesa)
			ruim.Add($"acesa(s): [{string.Join(",", acesas.Select(o => o.Qual))}], devia ser so {acesa}");

		foreach (OpcaoLida o in c.Opcoes)
		{
			int pede = Convivio.FamiliaridadeExigida(o.Qual);
			bool aberta = convivio >= pede;
			string nome = MenuJogo.NomeNaRegua(o.Qual);
			if (o.Escolhida)
			{
				if (!o.Apagada || o.Texto != nome) ruim.Add($"{o.Qual} (acesa): '{o.Texto}', clicavel={!o.Apagada}");
			}
			else if (aberta)
			{
				if (o.Apagada || o.Texto != nome) ruim.Add($"{o.Qual} (aberta com {convivio}): '{o.Texto}', clicavel={!o.Apagada}");
			}
			else if (!o.Apagada || o.Texto != $"{nome} ({pede})")
				ruim.Add($"{o.Qual} (fechada: pede {pede}, ha {convivio}): '{o.Texto}', clicavel={!o.Apagada}");
		}

		if (c.Efeito != MenuJogo.EfeitoDe(acesa)) ruim.Add($"a linha de efeito nao e a da opcao acesa: '{c.Efeito}'");
		if (c.Efeito.Contains("FÚRIA") != Convivio.EnfurecePorMorte(acesa))
			ruim.Add("a linha de efeito fala de furia sem a declaracao enfurecer (ou cala quando enfurece)");
		// OS DOIS DEGRAUS (dono, 2026-10-11): a QUEDA da pessoa da a raiva comum; a furia e so da morte e da absorcao.
		// Uma linha que prometa furia pela queda -- o texto de antes -- nao tem a palavra da raiva, e cai aqui.
		if (c.Efeito.Contains("RAIVA") != Convivio.EnfurecePorQueda(acesa))
			ruim.Add("a linha de efeito nao separa a RAIVA (da queda) da furia (da morte), ou fala de raiva sem a queda enfurecer");

		// E OS DETALHES: o botao (sempre), e o que o cartao aberto pode mostrar com este convivio.
		ruim.AddRange(ProblemasDosDetalhes(c.Detalhes, convivio));
		return ruim;
	}

	/// <summary>
	/// AS PALAVRAS QUE SO EXISTIAM NA AFINIDADE (os rotulos que o jogo deduzia, o odio, o pedido de amizade):
	/// devolve as que aparecem nestes textos. "Amigo", "Inimigo" e "Desafeto" NAO estao aqui -- hoje sao
	/// nomes de declaracao, escolhidos pelo jogador.
	/// </summary>
	internal static List<string> PalavrasDaAfinidade(IEnumerable<string> textos)
	{
		string[] proibidas =
		[
			"afinidade", "proximidade", "Ligado", "Familiar", "Mal conhecido", "Nemesis", "Rival odiado",
			"Antipatizado", "RIVAL", "Pedido de amizade", "quer ser seu amigo", "ódio", "libera até",
		];
		var achadas = new List<string>();
		foreach (string t in textos)
			foreach (string p in proibidas)
				// "RIVAL" (a pilula antiga) so em caixa alta e sozinha: "Rival amigo" e uma declaracao de hoje
				if (p == "RIVAL" ? t == p : t.Contains(p, StringComparison.OrdinalIgnoreCase))
					achadas.Add($"'{p}' em '{(t.Length > 50 ? t[..50] + "..." : t)}'");
		return achadas;
	}

	internal static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node n in Todos(f)) yield return n;
	}

	/// <summary>Todo texto que a pagina mostra: rotulos e botoes visiveis.</summary>
	internal static List<string> TextosDe(Node raiz) =>
	[
		.. Todos(raiz).OfType<Label>().Where(l => l.IsVisibleInTree()).Select(l => l.Text),
		.. Todos(raiz).OfType<Button>().Where(b => b.IsVisibleInTree()).Select(b => b.Text),
	];

	private static IEnumerable<PanelContainer> CartoesDePessoa(Node pagina) =>
		Todos(pagina).OfType<PanelContainer>()
			.Where(p => p.IsVisibleInTree() && p.HasMeta("cartao") && p.GetMeta("cartao").AsString() == "pessoa");

	private static PanelContainer? CartaoDe(Node pagina, string nome) =>
		CartoesDePessoa(pagina).FirstOrDefault(p => p.GetMeta("titulo").AsString() == nome);

	private static PanelContainer? Secao(Node pagina, string titulo) =>
		Todos(pagina).OfType<PanelContainer>().FirstOrDefault(p =>
			p.IsVisibleInTree() && p.HasMeta("cartao") && p.GetMeta("cartao").AsString() == "secao"
			&& p.HasMeta("titulo") && p.GetMeta("titulo").AsString() == titulo);

	private static List<string> PilulasEm(Node raiz) =>
		[.. Todos(raiz).OfType<PanelContainer>().Where(p => p.HasMeta("pilula")).Select(p => p.GetMeta("pilula").AsString())];

	/// <summary>O que se le de um cartao que nao esta na tela: nada bate, e a checagem que o usar reprova dizendo isso.</summary>
	private static readonly CartaoLido SemCartao =
		new("(sem cartao)", -1, [], "", "", false, false, 0, 0, new DetalhesLidos([], "", "", [], null, false));

	private static Button? Opcao(Node cartao, Relacao qual) =>
		Todos(cartao).OfType<Button>().FirstOrDefault(b => b.HasMeta("declaracao") && b.GetMeta("declaracao").AsInt32() == (int)qual);

	/// <summary>O botao que abre e fecha os detalhes de um cartao (o metadado `detalhes` leva a assinatura da pessoa).</summary>
	private static Button? BotaoDeDetalhes(Node cartao) =>
		Todos(cartao).OfType<Button>().FirstOrDefault(b => b.HasMeta("detalhes"));

	/// <summary>A moldura do retrato de um cartao (ela cresce quando o cartao abre).</summary>
	private static Panel? Moldura(Node cartao) =>
		Todos(cartao).OfType<Panel>().FirstOrDefault(p => p.Name.ToString() == "Moldura");

	/// <summary>O valor escrito numa linha de detalhe ("" = a linha nao esta la).</summary>
	private static string Fato(CartaoLido c, string rotulo) =>
		c.Detalhes.Fatos.FirstOrDefault(f => f.Rotulo == rotulo).Valor ?? "";

	// =====================================================================
	// O ELENCO -- sete com cartao (dois deles sem foto) e dois "so de vista"
	// =====================================================================
	/// <param name="Historia">O que EU ja ouvi dela: um texto, "" (ouvi, e ela nao escreveu nada) ou nulo (ainda nao ouvi).</param>
	private readonly record struct Papel(string Assinatura, string Nome, string Raca, int Convivio,
										 Relacao Declaracao, string Cabelo, string Roupa,
										 int Idade = 25, string Cargo = "", string? Historia = null, int ConheceHaDias = 0);

	/// <summary>
	/// O KURIRIN E O CASO DO CLIQUE: 62 de convivio (o "amigo" ja esta aberto, o "melhor amigo" ainda nao) e
	/// nenhuma declaracao -- e e dele o cartao que se ABRE (31 anos, um cargo, uma historia de dois paragrafos,
	/// conhecido ha 3 dias). A BULMA e a que quase nao conviveu (18: tudo de 20 pra cima fechado, os detalhes
	/// TRANCADOS) e que eu nunca vi com os meus olhos (sem foto). Cabelo vazio = sem retrato guardado.
	///
	/// AS TRES HISTORIAS QUE NAO SAO TEXTO: o VEGETA eu ainda nao ouvi (a ficha dele e de antes dos detalhes), o
	/// RADITZ eu ouvi e ele nao escreveu nenhuma, e a Bulma nao tem como ter (o convivio nao paga).
	/// </summary>
	private static readonly Papel[] Elenco =
	[
		new("9000000001", "Gohan", "Halfbreed", 214, Relacao.Amor, "Kid Gohan", "Clothes_GohanGi",
			11, "", "Filho do Goku. Eu só queria estudar, mas a Terra continua precisando de mim.", 400),
		new("9000000002", "Kuririn", "Human", 62, Relacao.Nenhuma, "Bald", "Clothes_TurtleSuit",
			31, "Turtle Hermit", HistoriaDoKuririn, 3),
		new("9000000003", "Vegeta", "Saiyan", 150, Relacao.RivalBom, "Vegeta", "Clothes_SaiyanSuit+Armor_Elite",
			33, "", null, 30),
		new("9000000004", "Bulma", "Human", 18, Relacao.Nenhuma, "", "", 28),
		new("9000000005", "Raditz", "Saiyan", 60, Relacao.Odio, "Raditz", "RaditzArmorTobiUchiha", 35, "", "", 2),
		new("9000000006", "Yamcha", "Human", 104, Relacao.MuitoBom, "Yamcha", "Clothes_GiTop+Clothes_GiBottom",
			29, "", "Ex-bandido do deserto. Hoje luto ao lado de quem um dia tentei assaltar.", 60),
		new("9000000007", "Tenshinhan", "Human", 0, Relacao.Nenhuma, "", ""),
		new("9000000008", "Chaos", "Human", 0, Relacao.Nenhuma, "", ""),
		// O PICCOLO e de uma raca SEM GENERO (`CharacterDraft.SemGenero`): o cartao aberto dele nao tem a linha.
		new("9000000009", SemGenero, "Namekian", 30, Relacao.Nenhuma, "", "",
			24, "", "Nasci de um ovo, já sabendo quem eu devia odiar. Um garoto me ensinou outra coisa.", 8),
	];

	private const string DoClique = "Kuririn", ATrancada = "Bulma", SemGenero = "Piccolo";

	/// <summary>
	/// Os cartoes na ordem em que o servidor os manda (`Convivio.EmOrdem`): quem eu classifiquei, do amor ao
	/// inimigo mortal, e depois os neutros pelo degrau do convivio (Kuririn 62, Piccolo 30, Bulma 18).
	/// </summary>
	private static readonly string[] OrdemAoChegar = ["Gohan", "Yamcha", "Vegeta", "Raditz", "Kuririn", "Piccolo", "Bulma"];

	/// <summary>...e depois que eu declaro o Kuririn amigo: ele sobe pro lugar dele na regua (depois do melhor amigo).</summary>
	private static readonly string[] OrdemDepoisDoClique = ["Gohan", "Yamcha", "Kuririn", "Vegeta", "Raditz", "Piccolo", "Bulma"];

	private const string HistoriaDoKuririn =
		"Cresci no templo Orin e fugi de lá pra aprender com o Mestre Kame.\n\n"
		+ "Não sou o mais forte, mas nunca deixei um amigo pra trás.";

	// =====================================================================
	// O ANDAIME
	// =====================================================================
	private int _ok, _falhou, _injOk, _injFalhou;
	private readonly List<string> _vermelhas = [];
	private readonly List<TiraDeFotos.Quadro> _historia = [];
	private readonly List<string> _fotos = [];
	private readonly List<string> _doServidor = [];
	/// <summary>Os verbs que a TELA tentou mandar enquanto o espiao esta ligado (`cmd:arg`). Ver a secao 7 do roteiro.</summary>
	private readonly List<string> _verbos = [];
	/// <summary>TRUE = o espiao ENGOLE o verb `detalhes`: o pedido sai da tela e a resposta nunca chega (o estado "buscando").</summary>
	private bool _engolirDetalhes;
	private IEnumerator? _roteiro;
	private double _espera = 2.5;
	private bool _acabou, _ouvindo;
	private string _pasta = "";

	private static GameClient? C => GameClient.Instance;
	private static GameServer? S => GameServer.Instance;
	private static MenuJogo? M => MenuJogo.Instancia;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[dconhecidos]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; _vermelhas.Add(nome); GD.PrintErr($"[dconhecidos]   FALHA {nome}   [{detalhe}]"); }
	}

	private void Injeta(string nome, bool ficouVermelha, string detalhe = "")
	{
		if (ficouVermelha) { _injOk++; GD.Print($"[dconhecidos]   pegou  (injecao) {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _injFalhou++; _vermelhas.Add("(injecao) " + nome); GD.PrintErr($"[dconhecidos]   FALHA (injecao que PASSOU) {nome}   [{detalhe}]"); }
	}

	private static void Nota(string s) => GD.Print("[dconhecidos] " + s);

	private static string Arg(string chave)
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, chave);
		return i >= 0 && i + 1 < a.Length ? a[i + 1] : "";
	}

	/// <summary>O que o servidor me DIZ (canal de sistema): a confirmacao e a recusa de uma declaracao sao frases.</summary>
	private void AoFalar(Protocol.Fala canal, string autor, string texto)
	{
		if (canal == Protocol.Fala.Sistema) _doServidor.Add(texto);
	}

	public override void _ExitTree()
	{
		if (_ouvindo && C is { } cli) cli.Falou -= AoFalar;
		DesligarOsBotoesDeBancada();
	}

	/// <summary>
	/// Os defeitos injetados, a escuta dos pacotes de detalhes e o espiao dos verbs sao ESTATICOS: nenhum pode
	/// sobrar ligado quando o roteiro acaba (ou estoura no meio) -- o processo e host, e o servidor continua
	/// servindo com eles.
	/// </summary>
	private static void DesligarOsBotoesDeBancada()
	{
		Convivio.DeclararSemConvivioDeTeste = false;
		Convivio.DetalhesSemConvivioDeTeste = false;
		GameServer.EscutaDeDetalhes = null;
		GameClient.EspiaoDeVerbos = null;
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia?.PosicaoLocal == null || M == null) return;
		if (!_ouvindo) { cli.Falou += AoFalar; _ouvindo = true; }

		_espera -= delta;
		if (_espera > 0) return;

		try
		{
			_roteiro ??= Roteiro().GetEnumerator();
			if (!_roteiro.MoveNext()) { Fim(); return; }
			_espera = _roteiro.Current is double d ? d : 0;
		}
		catch (Exception e)
		{
			// UMA EXCECAO NO ROTEIRO E UM VERMELHO, NAO UM SILENCIO: sem isto o processo ficaria vivo, sem placar,
			// ate o prazo do lancador.
			Afirmar("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
			Fim();
		}
	}

	private void Fim()
	{
		_acabou = true;
		DesligarOsBotoesDeBancada();
		VistosDeGente.EsquecerDeTeste();
		foreach (string f in _fotos) Nota("foto  " + f);
		Nota($"injecoes: {_injOk} pegas, {_injFalhou} passaram batido");
		if (_falhou + _injFalhou > 0) GD.PrintErr("[dconhecidos] vermelhas: " + string.Join(" | ", _vermelhas));
		GD.Print(_falhou + _injFalhou == 0
			? $"\n[dconhecidos] ===== TUDO OK ({_ok} ok, {_injOk} injecoes pegas) ====="
			: $"\n[dconhecidos] ===== {_falhou + _injFalhou} FALHA(S) ({_ok} ok) =====");
	}

	/// <summary>Espera ate a condicao valer (ou o prazo vencer), sem travar o quadro.</summary>
	private static IEnumerable<double> Ate(Func<bool> cond, double segundos)
	{
		for (double t = 0; t < segundos && !cond(); t += 0.1) yield return 0.1;
	}

	private Image? Fotografar(string nome, string legenda)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"sem foto pra {nome} (headless nao renderiza): rode com janela"); return null; }
		string caminho = _pasta + nome + ".png";
		img.SavePng(caminho);
		_fotos.Add(caminho);

		// A HISTORIA vai em meia escala: quatro telas inteiras lado a lado dariam uma tira de 5 mil px.
		var pequena = (Image)img.Duplicate();
		pequena.Resize(img.GetWidth() / 2, img.GetHeight() / 2, Image.Interpolation.Lanczos);
		_historia.Add(new TiraDeFotos.Quadro(pequena, legenda));
		return img;
	}

	/// <summary>O recorte de UM cartao de uma foto ja tirada, ampliado duas vezes (sem borrar o pixel art do retrato).</summary>
	private void Recortar(Image foto, Control cartao, string nome)
	{
		Rect2 r = cartao.GetGlobalRect();
		Rect2I area = new Rect2I(Math.Max(0, (int)r.Position.X - 6), Math.Max(0, (int)r.Position.Y - 6),
								 (int)r.Size.X + 12, (int)r.Size.Y + 12)
			.Intersection(new Rect2I(0, 0, foto.GetWidth(), foto.GetHeight()));
		if (area.Size.X <= 8 || area.Size.Y <= 8) return;

		Image recorte = foto.GetRegion(area);
		recorte.Resize(area.Size.X * 2, area.Size.Y * 2, Image.Interpolation.Nearest);
		string caminho = _pasta + nome + ".png";
		recorte.SavePng(caminho);
		_fotos.Add(caminho);
	}

	/// <summary>Rola a pagina ate este cartao caber na janela (pra foto pegar o cartao inteiro).</summary>
	private static void TrazerPraTela(Control? cartao)
	{
		if (cartao != null && Rolagem(cartao) is { } rolagem) rolagem.EnsureControlVisible(cartao);
	}

	private static ScrollContainer? Rolagem(Node n)
	{
		for (Node? p = n; p != null; p = p.GetParent())
			if (p is ScrollContainer s) return s;
		return null;
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private IEnumerable<double> Roteiro()
	{
		GameClient cli = C!;
		MenuJogo menu = M!;

		_pasta = Arg("--pasta");
		if (_pasta.Length == 0) _pasta = ProjectSettings.GlobalizePath("user://");
		_pasta = _pasta.Replace('\\', '/');
		if (!_pasta.EndsWith('/')) _pasta += "/";
		DirAccess.MakeDirRecursiveAbsolute(_pasta);

		Nota("================ A ABA PEOPLE: CONVIVIO E DECLARACAO ================");
		Nota($"fotos em {_pasta}; janela {DisplayServer.WindowGetSize()}");

		GameServer? srv = S;
		Afirmar("ha servidor no processo (`--diagconhecidos` precisa de `--host`)", srv != null);
		if (srv == null) yield break;

		// MEIO-DIA E CEU LIMPO: o menu e de vidro, e a foto sai com a mesma luz atras em toda rodada.
		srv.CravarMeioDiaDaVariedade(cli.LocalId);
		yield return 1.0;

		// ---------------------------------------------------------------- 1. A ABA VAZIA
		Nota("--- 1. a aba de quem nao conhece ninguem ---");
		menu.Abrir();
		menu.IrPara("People");
		yield return 0.6;
		Control? pg = menu.PaginaDeTeste("People");
		Afirmar("a pagina de People existe e esta na tela", pg is { Visible: true });
		if (pg == null) yield break;

		Afirmar("PRECONDICAO: a bancada nasce sem conhecer ninguem", cli.Conhecidos.Count == 0, $"{cli.Conhecidos.Count}");
		string? faixa = menu.ValorDesenhado("People", "Conhecidos");
		Afirmar("a faixa 'Conhecidos' diz 0 e explica como alguem entra na lista",
				faixa != null && faixa.StartsWith("0", StringComparison.Ordinal) && faixa.Contains("perto de você"), faixa ?? "(nula)");
		PanelContainer? como = Secao(pg, "Como funciona");
		List<string> legenda = como != null ? TextosDe(como) : [];
		Afirmar("a aba abre com 'Como funciona': as DUAS coisas que ela tem, e quem escreve cada uma (o jogo conta / você escolhe)",
				legenda.Contains(MenuJogo.TituloDoConvivio) && legenda.Any(t => t.StartsWith("o jogo conta", StringComparison.Ordinal))
				&& legenda.Contains(MenuJogo.TituloDaEscolha) && legenda.Any(t => t.StartsWith("você escolhe", StringComparison.Ordinal)),
				string.Join(" | ", legenda.Select(t => t.Length > 40 ? t[..40] + "..." : t)));
		Afirmar("sem conviver com ninguem: a frase de vazio diz o que fazer, e nao ha cartao nem regua",
				TextosDe(pg).Any(t => t.StartsWith("Você ainda não conviveu com ninguém", StringComparison.Ordinal))
				&& !CartoesDePessoa(pg).Any() && !Todos(pg).OfType<Button>().Any(b => b.HasMeta("declaracao")));
		List<string> velhas = PalavrasDaAfinidade(TextosDe(pg));
		Afirmar("NADA da afinidade antiga na pagina (nem a palavra, nem os rotulos que o jogo deduzia, nem o pedido de amizade)",
				velhas.Count == 0, string.Join("; ", velhas));
		Afirmar("os verbs 'Request Friendship' e 'Declare Rival' sairam do catalogo (amigo e rival sao escolhas desta aba); "
				+ "o 'Remember Person' continua",
				Verbos.PorChave("Request Friendship") == null && Verbos.PorChave("Declare Rival") == null
				&& Verbos.PorChave("Remember Person") != null);
		Fotografar("conhecidos-1-vazia", "1 sem conhecer ninguem");

		// ---------------------------------------------------------------- 2. O ELENCO CHEGA PELO FIO
		Nota("--- 2. o elenco, plantado no servidor, chega pelo fio e a aba se refaz sozinha ---");
		VisualCatalog? cat = VistosDeGente.Catalogo;
		foreach (Papel p in Elenco)
		{
			if (p.Cabelo.Length == 0 || cat == null) continue;
			Appearance ap = cli.Visual.Copiar();
			ap.Cabelo = p.Cabelo;
			ap.CorCabelo = null;
			ap.Roupa = [.. p.Roupa.Split('+', StringSplitOptions.RemoveEmptyEntries)
							.Select(n => cat.Peca(n)).Where(c => c != null).Select(c => new PecaDeRoupa(c!, null))];
			VistosDeGente.AnotarDeTeste(p.Nome, p.Raca, "Male", ap);
		}

		// A FICHA QUE O SERVIDOR GUARDA DE CADA UM, como o convivio de verdade a deixaria: a idade e o genero da
		// foto, desde quando eu o conheco e -- so pra quem eu ja OUVI -- a historia e o cargo. O relogio e o do
		// servidor (o mesmo processo): e dele que saem o "ha 3 dias" e o "agora ha pouco" do cartao aberto.
		long agoraMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		bool plantou = srv.PlantarConhecidosDeTeste(cli.LocalId, Elenco.Select(p => new Conhecido
		{
			Assinatura = p.Assinatura, Nome = p.Nome, Raca = p.Raca, Genero = "Male", Idade = p.Idade,
			Familiaridade = p.Convivio, Relacao = p.Declaracao,
			DesdeMs = agoraMs - p.ConheceHaDias * 86_400_000L,
			Titulo = p.Cargo, Historia = p.Historia ?? "", DetalhesEm = p.Historia != null ? agoraMs : 0,
		}));
		Afirmar("o servidor plantou o elenco na lista deste jogador", plantou);
		foreach (double s in Ate(() => cli.Conhecidos.Count == Elenco.Length, 6)) yield return s;
		Afirmar($"a lista chegou pelo fio com os {Elenco.Length}", cli.Conhecidos.Count == Elenco.Length, $"{cli.Conhecidos.Count}");
		yield return 0.4;

		// SEM `ForcarRedesenho`: a lista nova tem que mudar a assinatura da aba e refazer a pagina sozinha.
		List<string> comCartao = [.. CartoesDePessoa(pg).Select(c => c.GetMeta("titulo").AsString())];
		Afirmar($"**a aba se refez sozinha**: {OrdemAoChegar.Length} cartoes, na ordem que o servidor mandou -- quem eu classifiquei "
				+ "primeiro (do amor ao inimigo mortal), depois os neutros por convivio",
				comCartao.SequenceEqual(OrdemAoChegar),
				string.Join(",", comCartao));
		Afirmar("...e a ordem da tela e a ordem do fio", comCartao.SequenceEqual(cli.Conhecidos.Take(OrdemAoChegar.Length).Select(k => k.Nome)),
				string.Join(",", cli.Conhecidos.Select(k => k.Nome)));
		PanelContainer? vista = Secao(pg, "Só de vista");
		Afirmar("quem eu so vi passar (convivio 0, nada declarado) NAO ganha cartao: vai por nome no cartao 'Só de vista'",
				vista != null && PilulasEm(vista).Order().SequenceEqual(["Chaos", "Tenshinhan"])
				&& CartaoDe(pg, "Chaos") == null && CartaoDe(pg, "Tenshinhan") == null,
				vista == null ? "sem o cartao" : string.Join(",", PilulasEm(vista)));
		faixa = menu.ValorDesenhado("People", "Conhecidos");
		Afirmar($"a faixa conta os {Elenco.Length} e diz de quem e a escolha",
				faixa != null && faixa.StartsWith(Elenco.Length.ToString(), StringComparison.Ordinal) && faixa.Contains("VOCÊ quem diz"), faixa ?? "(nula)");

		// O QUE SE VE AO ABRIR, sem rolar nada. A primeira versao desta aba tinha uma explicacao de dois
		// paragrafos e o primeiro cartao ficava cortado no pe da pagina: quem abria via texto, e nao gente.
		PanelContainer? primeiro = CartoesDePessoa(pg).FirstOrDefault();
		ScrollContainer? janela = Rolagem(pg);
		Rect2 rCartao = primeiro?.GetGlobalRect() ?? default, rJanela = janela?.GetGlobalRect() ?? default;
		Afirmar("**ao abrir a aba, o primeiro cartao de pessoa cabe INTEIRO na tela, sem rolar** (a explicacao e curta)",
				primeiro != null && janela != null && janela.ScrollVertical == 0
				&& rCartao.Position.Y >= rJanela.Position.Y && rCartao.End.Y <= rJanela.End.Y + 0.5f,
				$"cartao y {rCartao.Position.Y:0}..{rCartao.End.Y:0}, janela y {rJanela.Position.Y:0}..{rJanela.End.Y:0}");
		// E A REGUA SE LE COMO REGUA: com todas as opcoes abertas (o Gohan, 214 de convivio), as nove cabem
		// numa linha so. (Com opcao fechada o preco entra no texto e a fila pode quebrar -- e o que o
		// `HFlowContainer` existe pra fazer.)
		List<float> alturas = primeiro != null
			? [.. Todos(primeiro).OfType<Button>().Where(b => b.HasMeta("declaracao")).Select(b => b.GetGlobalRect().Position.Y)]
			: [];
		Afirmar("...e as nove opcoes da regua, todas abertas, cabem em UMA linha",
				alturas.Count == 9 && alturas.Max() - alturas.Min() < 1f,
				$"{alturas.Count} opcoes em y [{string.Join(",", alturas.Select(y => y.ToString("0")).Distinct())}]");
		Fotografar("conhecidos-2-lista", "2 a lista");

		// ---------------------------------------------------------------- 3. O CONTRATO, CARTAO A CARTAO
		Nota("--- 3. cada cartao: UM numero de convivio, UMA regua, UMA linha de efeito ---");
		foreach (Papel p in Elenco.Where(p => p.Convivio > 0))
		{
			PanelContainer? cartao = CartaoDe(pg, p.Nome);
			(int Convivio, Relacao Declaracao)? verdade = srv.ConhecidoDeTeste(cli.LocalId, p.Assinatura);
			if (cartao == null || verdade is not { } v) { Afirmar($"[{p.Nome}] ha cartao e ha ficha no servidor", false); continue; }
			CartaoLido lido = Ler(cartao);
			List<string> ruim = ProblemasDoCartao(lido, v.Convivio, v.Declaracao);
			Afirmar($"[{p.Nome}] convivio {v.Convivio}, declarou {v.Declaracao}: o cartao diz exatamente isso "
					+ $"(acesa: {MenuJogo.NomeNaRegua(v.Declaracao == Relacao.Nenhuma ? Relacao.Neutro : v.Declaracao)}; "
					+ $"{lido.Opcoes.Count(o => !o.Apagada)} aberta(s), {lido.Opcoes.Count(o => o.Apagada && !o.Escolhida)} fechada(s))",
					ruim.Count == 0, string.Join("; ", ruim));
			Afirmar($"[{p.Nome}] o retrato: {(p.Cabelo.Length > 0 ? "o boneco que eu vi por ultimo" : "'sem foto' (nunca vi com os meus olhos)")}",
					p.Cabelo.Length > 0 ? lido.TemBoneco && !lido.SemFoto : lido.SemFoto && !lido.TemBoneco);
		}

		// Tres leituras a olho nu, por nome -- o que a tabela acima cobra em geral, dito no caso concreto.
		CartaoLido bulma = CartaoDe(pg, "Bulma") is { } cb ? Ler(cb) : SemCartao;
		Afirmar("a Bulma (18 de convivio, nunca declarei nada): o NEUTRO esta aceso, e 'Amigo (50)' esta fechado com o preco escrito",
				bulma.Opcoes.Any(o => o.Qual == Relacao.Neutro && o.Escolhida)
				&& bulma.Opcoes.Any(o => o.Qual == Relacao.Bom && o.Apagada && o.Texto == "Amigo (50)")
				&& bulma.Opcoes.Any(o => o.Qual == Relacao.Ruim && !o.Apagada && o.Texto == "Desafeto"),
				string.Join(" | ", bulma.Opcoes.Select(o => o.Texto + (o.Escolhida ? "*" : o.Apagada ? "(x)" : ""))));
		Afirmar("...e a linha dela diz que essa escolha nao muda regra nenhuma", bulma.Efeito.Contains("não muda nenhuma regra"), bulma.Efeito);
		CartaoLido gohan = CartaoDe(pg, "Gohan") is { } cg ? Ler(cg) : bulma;
		Afirmar("o Gohan (amor): a linha diz que ve-lo CAIR me da RAIVA, que ve-lo morrer ou ser absorvido me poe em FÚRIA, e que lutar com ele e treino",
				gohan.Efeito.StartsWith("Amor:", StringComparison.Ordinal)
				&& gohan.Efeito.Contains("RAIVA se vir essa pessoa cair") && gohan.Efeito.Contains("FÚRIA se a vir morrer ou ser absorvida")
				&& gohan.Efeito.Contains("treino"), gohan.Efeito);
		CartaoLido vegeta = CartaoDe(pg, "Vegeta") is { } cv ? Ler(cv) : bulma;
		Afirmar("o Vegeta (rival amigo): so a MORTE dele enfurece (o texto nao promete a queda)",
				vegeta.Efeito.Contains("morrer") && !vegeta.Efeito.Contains("cair") && vegeta.Efeito.Contains("FÚRIA"), vegeta.Efeito);

		velhas = PalavrasDaAfinidade(TextosDe(pg));
		Afirmar("com a lista cheia: NADA da afinidade antiga na pagina", velhas.Count == 0, string.Join("; ", velhas));

		// ---------------------------------------------------------------- 4. O CLIQUE
		Nota($"--- 4. eu digo que o {DoClique} e meu AMIGO, pelo botao da regua ---");
		Papel k = Elenco.First(p => p.Nome == DoClique);
		PanelContainer? doK = CartaoDe(pg, DoClique);
		Button? amigo = doK != null ? Opcao(doK, Relacao.Bom) : null;
		Afirmar($"a opcao 'Amigo' do {DoClique} esta ABERTA (62 de convivio pagam os 50)", amigo is { Disabled: false, Text: "Amigo" },
				amigo == null ? "nao achei" : $"'{amigo.Text}' Disabled={amigo.Disabled}");
		Afirmar($"...e 'Melhor amigo (100)' esta FECHADA", doK != null && Opcao(doK, Relacao.MuitoBom) is { Disabled: true, Text: "Melhor amigo (100)" });
		if (amigo == null) yield break;

		_doServidor.Clear();
		amigo.EmitSignal(BaseButton.SignalName.Pressed);
		foreach (double s in Ate(() => srv.ConhecidoDeTeste(cli.LocalId, k.Assinatura)?.Declaracao == Relacao.Bom
									   && cli.Conhecidos.Any(c => c.Nome == DoClique && c.Relacao == (byte)Relacao.Bom), 5)) yield return s;
		yield return 0.4;

		Afirmar("**o clique mudou a declaracao NO SERVIDOR** (o verb `relacao`, com o portao dele)",
				srv.ConhecidoDeTeste(cli.LocalId, k.Assinatura)?.Declaracao == Relacao.Bom,
				srv.ConhecidoDeTeste(cli.LocalId, k.Assinatura)?.Declaracao.ToString() ?? "sem ficha");
		Afirmar("...o servidor confirmou em palavras", _doServidor.Any(t => t.Contains($"{DoClique} é 'amigo' pra você")),
				string.Join(" | ", _doServidor));
		doK = CartaoDe(pg, DoClique);
		CartaoLido depois = doK != null ? Ler(doK) : bulma;
		List<string> ruimDepois = ProblemasDoCartao(depois, k.Convivio, Relacao.Bom);
		Afirmar("...e a aba se refez sozinha: 'Amigo' aceso, e a linha de efeito agora fala de FÚRIA",
				ruimDepois.Count == 0 && depois.Efeito.Contains("FÚRIA"), string.Join("; ", ruimDepois) + " | " + depois.Efeito);
		comCartao = [.. CartoesDePessoa(pg).Select(c => c.GetMeta("titulo").AsString())];
		Afirmar($"...e o {DoClique} subiu pra junto de quem eu classifiquei, no lugar dele na regua (depois do melhor amigo, antes do rival)",
				comCartao.SequenceEqual(OrdemDepoisDoClique), string.Join(",", comCartao));
		if (doK != null && Rolagem(pg) is { } rolK) rolK.EnsureControlVisible(doK);
		yield return 0.3;
		Image? fotoDoAmigo = Fotografar("conhecidos-3-declarou-amigo", "3 declarei o Kuririn amigo");

		// O RECORTE DE UM CARTAO, ampliado: e a peca que o redesenho inteiro repete.
		if (fotoDoAmigo != null && doK != null) Recortar(fotoDoAmigo, doK, "conhecidos-5-cartao");

		// ---------------------------------------------------------------- 5. O PORTAO E DO SERVIDOR
		Nota("--- 5. o pacote forjado: 'amor' (200) pra quem eu so tenho 62 de convivio ---");
		bool ORecusa()
		{
			(int Convivio, Relacao Declaracao)? v = srv.ConhecidoDeTeste(cli.LocalId, k.Assinatura);
			return v is { Declaracao: Relacao.Bom };
		}
		_doServidor.Clear();
		cli.SendVerbo("relacao", $"{k.Assinatura}|amor");
		foreach (double s in Ate(() => _doServidor.Any(t => t.Contains("conviveu o bastante")), 4)) yield return s;
		Afirmar("**o servidor RECUSA o pacote forjado** (apagar o botao nunca foi permissao): a declaracao continua 'amigo'",
				ORecusa(), srv.ConhecidoDeTeste(cli.LocalId, k.Assinatura)?.Declaracao.ToString() ?? "sem ficha");
		Afirmar("...e diz por que, com os dois numeros (pede 200, ha 62)",
				_doServidor.Any(t => t.Contains("conviveu o bastante") && t.Contains("200") && t.Contains("62")),
				string.Join(" | ", _doServidor));

		// O MESMO PACOTE COM O DEFEITO INJETADO no portao do Core: a declaracao passa, e a checagem de cima reprova.
		try
		{
			Convivio.DeclararSemConvivioDeTeste = true;
			cli.SendVerbo("relacao", $"{k.Assinatura}|amor");
			foreach (double s in Ate(() => !ORecusa(), 4)) yield return s;
			Injeta("(defeito injetado: declarar nao cobra convivio) o MESMO pacote passa, e a checagem da recusa REPROVA",
				   !ORecusa(), srv.ConhecidoDeTeste(cli.LocalId, k.Assinatura)?.Declaracao.ToString() ?? "sem ficha");
		}
		finally { Convivio.DeclararSemConvivioDeTeste = false; }
		cli.SendVerbo("relacao", $"{k.Assinatura}|amigo");
		foreach (double s in Ate(ORecusa, 4)) yield return s;
		Afirmar("...e desfeito o defeito (e a declaracao), ela volta a valer", ORecusa());
		yield return 0.4;

		// ---------------------------------------------------------------- 6. O FIM DA LISTA
		Nota("--- 6. o fim da lista: 'Só de vista' e 'Quem está por perto' ---");
		// A PAGINA E REFEITA NO MESMO QUADRO DA LEITURA: os nomes visiveis mudam quando um NPC entra na tela, e a
		// aba os segue no proximo pacote de ficha (ate 200 ms) -- ler os dois em quadros diferentes acusaria uma
		// diferenca que e so de relogio.
		menu.ForcarRedesenho();
		PanelContainer? perto = Secao(pg, "Quem está por perto");
		List<string> visiveis = World.Instancia?.NomesVisiveis() ?? [];
		Afirmar("o cartao 'Quem está por perto' continua la, com exatamente os nomes visiveis do mundo",
				perto != null && (visiveis.Count == 0
					? TextosDe(perto).Contains("Ninguém no seu campo de visão.")
					: PilulasEm(perto).SequenceEqual(visiveis)),
				perto == null ? "sem o cartao" : $"pilulas [{string.Join("|", PilulasEm(perto))}] visiveis [{string.Join("|", visiveis)}]");
		if (perto != null && Rolagem(pg) is { } rol) rol.ScrollVertical = (int)rol.GetVScrollBar().MaxValue;
		yield return 0.4;
		Fotografar("conhecidos-4-fim-da-lista", "4 o fim da lista");

		// ---------------------------------------------------------------- 7. OS DETALHES: TRANCADOS
		// O segundo pedido do dono: *"clicar em pessoas q vc conhece (tem q ter um certo nivel de convivio minimo)
		// vc pode ver mais detalhes da pessoa, como idade, a backstory dela"*. Tres testemunhas, e nenhuma e a
		// tela: o ESPIAO dos verbs (o que o cliente tentou mandar), a ESCUTA do servidor (os pacotes de detalhes
		// que foram pro fio, no mesmo processo) e o que o cliente guarda (`DetalhesDe`).
		Nota($"--- 7. clico na {ATrancada} (18 de convivio): os detalhes dela estao TRANCADOS ---");
		Papel trancada = Elenco.First(p => p.Nome == ATrancada);
		int minimo = Convivio.ConvivioParaDetalhes;
		var saidos = new List<(int Para, byte[] Pacote)>();
		GameServer.EscutaDeDetalhes = saidos;
		GameClient.EspiaoDeVerbos = (cmd, arg) =>
		{
			_verbos.Add($"{cmd}:{arg}");
			return !(_engolirDetalhes && cmd == "detalhes");
		};
		int PedidosDeDetalhes() => _verbos.Count(v => v.StartsWith("detalhes:", StringComparison.Ordinal));

		if (Rolagem(pg) is { } praCima) praCima.ScrollVertical = 0;
		PanelContainer? cartaoT = CartaoDe(pg, ATrancada);
		Button? botaoT = cartaoT != null ? BotaoDeDetalhes(cartaoT) : null;
		Afirmar($"o botao de detalhes da {ATrancada} traz o PRECO, como as opcoes fechadas da regua ('Detalhes ({minimo})') -- e da pra clicar",
				botaoT is { Disabled: false } && botaoT.Text == $"Detalhes ({minimo})", botaoT == null ? "nao achei" : $"'{botaoT.Text}'");
		if (botaoT == null) yield break;

		_doServidor.Clear();
		_verbos.Clear();
		botaoT.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.8;
		cartaoT = CartaoDe(pg, ATrancada);
		CartaoLido lidoT = cartaoT != null ? Ler(cartaoT) : SemCartao;
		Afirmar($"**o clique abre o cartao TRANCADO, e a frase diz o que falta** ({minimo} de convivio; voces tem {trancada.Convivio})",
				lidoT.Detalhes.Estado == "trancados" && lidoT.Detalhes.Aviso.Contains("ainda não conviveram o bastante")
				&& lidoT.Detalhes.Aviso.Contains($"{minimo} de convívio") && lidoT.Detalhes.Aviso.Contains($"têm {trancada.Convivio}"),
				$"estado '{lidoT.Detalhes.Estado}': {lidoT.Detalhes.Aviso}");
		Afirmar("...NADA dela aparece (nem idade, nem cargo, nem historia), e o resto do cartao continua no contrato",
				lidoT.Detalhes.Fatos.Count == 0 && lidoT.Detalhes.Historia == null
				&& ProblemasDoCartao(lidoT, trancada.Convivio, trancada.Declaracao).Count == 0,
				string.Join("; ", ProblemasDoCartao(lidoT, trancada.Convivio, trancada.Declaracao)));
		Afirmar("**...e o pedido NEM SAIU do cliente** (a tela ja sabe a resposta): nenhum verb `detalhes`, nenhum pacote, nenhuma recusa",
				PedidosDeDetalhes() == 0 && saidos.Count == 0 && !_doServidor.Any(t => t.Contains("conviveram o bastante")),
				$"verbs [{string.Join(",", _verbos)}]; {saidos.Count} pacote(s)");
		TrazerPraTela(cartaoT);
		yield return 0.3;
		Image? fotoTrancada = Fotografar("conhecidos-6-detalhes-trancados", "6 detalhes trancados");
		cartaoT = CartaoDe(pg, ATrancada);
		if (fotoTrancada != null && cartaoT != null) Recortar(fotoTrancada, cartaoT, "conhecidos-6-detalhes-trancados-cartao");

		// O PEDIDO FORJADO: o verb `detalhes` na mao, por cima do botao trancado.
		bool OServidorNaoContou() => saidos.Count == 0 && cli.DetalhesDe(trancada.Assinatura) == null;
		_doServidor.Clear();
		cli.PedirDetalhes(trancada.Assinatura);
		foreach (double s in Ate(() => _doServidor.Any(t => t.Contains("conviveram o bastante")), 4)) yield return s;
		Afirmar("**o servidor RECUSA o pedido forjado**: pacote nenhum de detalhes sai, e o cliente continua sem nada dela",
				OServidorNaoContou(), $"{saidos.Count} pacote(s)");
		Afirmar($"...e diz por que, com os dois numeros (pede {minimo}, ha {trancada.Convivio})",
				_doServidor.Any(t => t.Contains("conviveram o bastante") && t.Contains($"{minimo} de convívio")
									 && t.Contains($"têm {trancada.Convivio}")),
				string.Join(" | ", _doServidor));

		// O MESMO PEDIDO COM O DEFEITO INJETADO no portao do Core: o pacote sai, e a checagem de cima reprova.
		try
		{
			Convivio.DetalhesSemConvivioDeTeste = true;
			cli.PedirDetalhes(trancada.Assinatura);
			foreach (double s in Ate(() => !OServidorNaoContou(), 4)) yield return s;
			Injeta("(defeito injetado: os detalhes saem com qualquer convivio) o MESMO pedido passa, e a checagem da recusa REPROVA",
				   !OServidorNaoContou(), $"{saidos.Count} pacote(s); idade que chegou: {cli.DetalhesDe(trancada.Assinatura)?.Idade}");
		}
		finally { Convivio.DetalhesSemConvivioDeTeste = false; }
		yield return 0.3;
		cartaoT = CartaoDe(pg, ATrancada);
		Afirmar("...e mesmo com os detalhes VAZADOS no cliente (pelo defeito), a tela nao os mostra: o cartao continua trancado",
				cli.DetalhesDe(trancada.Assinatura) != null && cartaoT != null
				&& Ler(cartaoT).Detalhes is { Estado: "trancados", Fatos.Count: 0, Historia: null });
		saidos.Clear();
		cli.SendVerbo("conhecidos");
		foreach (double s in Ate(() => cli.DetalhesDe(trancada.Assinatura) == null, 4)) yield return s;
		Afirmar("...e desfeito o defeito, a proxima lista que chega LIMPA do cliente o que ele nao podia guardar",
				OServidorNaoContou());

		// ---------------------------------------------------------------- 8. OS DETALHES: ABERTOS
		Nota($"--- 8. clico no {DoClique} (62 de convivio): os detalhes dele ABREM ---");
		PanelContainer? cartaoK = CartaoDe(pg, DoClique);
		Button? botaoK = cartaoK != null ? BotaoDeDetalhes(cartaoK) : null;
		float molduraFechada = cartaoK != null && Moldura(cartaoK) is { } mf ? mf.CustomMinimumSize.X : -1;
		Afirmar($"o botao do {DoClique} diz 'Ver detalhes' (o convivio dele paga os {minimo})",
				botaoK is { Text: "Ver detalhes", Disabled: false }, botaoK == null ? "nao achei" : $"'{botaoK.Text}'");
		if (botaoK == null) yield break;

		_doServidor.Clear();
		_verbos.Clear();
		botaoK.EmitSignal(BaseButton.SignalName.Pressed);
		foreach (double s in Ate(() => cli.DetalhesDe(k.Assinatura) != null, 5)) yield return s;
		yield return 0.5;

		Afirmar("**o clique PEDIU (um verb `detalhes`) e o servidor MANDOU**: um pacote, pra mim, e nenhuma recusa",
				PedidosDeDetalhes() == 1 && saidos.Count == 1 && saidos[0].Para == cli.LocalId
				&& !_doServidor.Any(t => t.Contains("conviveram o bastante")),
				$"verbs [{string.Join(",", _verbos)}]; {saidos.Count} pacote(s); {string.Join(" | ", _doServidor)}");
		DetalhesDeConhecido? dK = cli.DetalhesDe(k.Assinatura);
		Afirmar("...com o que a ficha do servidor guarda dele: 31 anos, o cargo e a historia que ele escreveu",
				dK is { Idade: 31, Genero: "Male", Titulo: "Turtle Hermit", OuviuAHistoria: true, Historia: HistoriaDoKuririn },
				dK is { } chegou ? $"{chegou.Idade} / '{chegou.Titulo}' / {chegou.Historia.Length} chars" : "nada chegou");

		cartaoK = CartaoDe(pg, DoClique);
		CartaoLido lidoK = cartaoK != null ? Ler(cartaoK) : SemCartao;
		Afirmar("**o cartao ABRIU no lugar**: idade (com a fase da vida), genero, cargo, ha quanto tempo nos conhecemos e quando o vi",
				lidoK.Detalhes.Estado == "abertos" && Fato(lidoK, "Idade").StartsWith("31 anos", StringComparison.Ordinal)
				&& Fato(lidoK, "Gênero") == "masculino" && Fato(lidoK, "Cargo") == "Turtle Hermit"
				&& Fato(lidoK, "Vocês se conhecem") == "há 3 dias"
				&& dK is { } visto && Fato(lidoK, "Visto por último") == MenuJogo.HaQuantoTempo(visto.VistoHaSegundos),
				string.Join(" | ", lidoK.Detalhes.Fatos.Select(f => $"{f.Rotulo}: {f.Valor}")));
		Afirmar("**...e a HISTORIA que ele escreveu, inteira, com os dois paragrafos**",
				lidoK.Detalhes is { HistoriaEDela: true, Historia: HistoriaDoKuririn }, lidoK.Detalhes.Historia ?? "(sem caixa)");
		Afirmar("...a regua e a linha de efeito continuam no cartao aberto (e ali que se decide o que ele e, depois de ler quem ele e)",
				ProblemasDoCartao(lidoK, k.Convivio, Relacao.Bom).Count == 0,
				string.Join("; ", ProblemasDoCartao(lidoK, k.Convivio, Relacao.Bom)));
		float molduraAberta = cartaoK != null && Moldura(cartaoK) is { } ma ? ma.CustomMinimumSize.X : -1;
		Afirmar("...o retrato cresce no cartao aberto", molduraFechada > 0 && molduraAberta > molduraFechada,
				$"{molduraFechada:0} -> {molduraAberta:0} px");
		cartaoT = CartaoDe(pg, ATrancada);
		Afirmar($"...e so UM cartao fica aberto por vez: o da {ATrancada} fechou, e o botao dela voltou a mostrar o preco",
				cartaoT != null && Ler(cartaoT).Detalhes is { Estado: "" } fechadaT && fechadaT.Botoes.SequenceEqual([$"Detalhes ({minimo})"]));

		TrazerPraTela(cartaoK);
		yield return 0.3;
		Image? fotoAberta = Fotografar("conhecidos-7-detalhes-abertos", "7 detalhes abertos");
		cartaoK = CartaoDe(pg, DoClique);
		if (fotoAberta != null && cartaoK != null) Recortar(fotoAberta, cartaoK, "conhecidos-7-detalhes-abertos-cartao");

		// FECHAR, pelo mesmo botao.
		Button? fechar = cartaoK != null ? BotaoDeDetalhes(cartaoK) : null;
		Afirmar("com o cartao aberto o botao vira 'Fechar detalhes'", fechar is { Text: "Fechar detalhes" });
		fechar?.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.4;
		cartaoK = CartaoDe(pg, DoClique);
		Afirmar("...e fecha: o cartao volta ao contrato de sempre, sem fato nem historia na tela",
				cartaoK != null && Ler(cartaoK) is { Detalhes.Estado: "" } fechadoK
				&& ProblemasDoCartao(fechadoK, k.Convivio, Relacao.Bom).Count == 0);

		// O ESTADO DO MEIO ("buscando"): o que a tela mostra entre o pedido e a resposta. Em host a resposta chega
		// no quadro seguinte e nao se deixa medir -- entao o cliente esquece o que tinha e o espiao ENGOLE o
		// verb: o pedido sai da tela e a resposta nunca vem.
		cli.EsquecerDetalhes();
		_engolirDetalhes = true;
		_verbos.Clear();
		saidos.Clear();
		(cartaoK != null ? BotaoDeDetalhes(cartaoK) : null)?.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.5;
		cartaoK = CartaoDe(pg, DoClique);
		CartaoLido buscando = cartaoK != null ? Ler(cartaoK) : SemCartao;
		Afirmar("enquanto a resposta nao chega o cartao diz que esta BUSCANDO, e nao mostra fato nenhum",
				buscando.Detalhes is { Estado: "buscando", Fatos.Count: 0, Historia: null }
				&& ProblemasDoCartao(buscando, k.Convivio, Relacao.Bom).Count == 0, $"estado '{buscando.Detalhes.Estado}'");
		menu.ForcarRedesenho();
		yield return 0.3;
		Afirmar("...e a pagina redesenhada NAO repete o pedido (um por abertura, e nao um por redesenho)",
				PedidosDeDetalhes() == 1 && saidos.Count == 0, $"{PedidosDeDetalhes()} pedido(s), {saidos.Count} pacote(s)");
		_engolirDetalhes = false;

		// AS OUTRAS DUAS CAIXAS DE HISTORIA, e o clique no RETRATO (que abre igual ao botao).
		foreach ((string nome, string frase, string porQue) in new[]
		{
			("Vegeta", "Você ainda não ouviu a história dessa pessoa", "a ficha dele e de antes dos detalhes: a historia entra no proximo encontro"),
			("Raditz", "Essa pessoa não escreveu uma história.", "eu ja o ouvi, e ele nao escreveu nenhuma"),
		})
		{
			Papel papel = Elenco.First(p => p.Nome == nome);
			PanelContainer? cartao = CartaoDe(pg, nome);
			Panel? retrato = cartao != null ? Moldura(cartao) : null;
			retrato?.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
			foreach (double s in Ate(() => cli.DetalhesDe(papel.Assinatura) != null, 5)) yield return s;
			yield return 0.4;
			cartao = CartaoDe(pg, nome);
			CartaoLido lido = cartao != null ? Ler(cartao) : SemCartao;
			Afirmar($"[{nome}] clicar no RETRATO abre os detalhes; a caixa da historia diz '{frase}' ({porQue})",
					lido.Detalhes is { Estado: "abertos", HistoriaEDela: false } && (lido.Detalhes.Historia ?? "").StartsWith(frase, StringComparison.Ordinal)
					&& Fato(lido, "Idade").StartsWith($"{papel.Idade} anos", StringComparison.Ordinal)
					&& Fato(lido, "Cargo") == "nenhum que você saiba"
					&& ProblemasDoCartao(lido, papel.Convivio, papel.Declaracao).Count == 0,
					$"estado '{lido.Detalhes.Estado}', historia '{lido.Detalhes.Historia}', "
					+ string.Join("; ", ProblemasDoCartao(lido, papel.Convivio, papel.Declaracao)));
		}
		// A RACA SEM GENERO: a ficha de um Namekuseijin carrega um genero de fabrica, e o cartao nao o escreve.
		Papel namek = Elenco.First(p => p.Nome == SemGenero);
		(CartaoDe(pg, SemGenero) is { } fechadoN ? BotaoDeDetalhes(fechadoN) : null)?.EmitSignal(BaseButton.SignalName.Pressed);
		foreach (double s in Ate(() => cli.DetalhesDe(namek.Assinatura) != null, 5)) yield return s;
		yield return 0.4;
		CartaoLido lidoN = CartaoDe(pg, SemGenero) is { } abertoN ? Ler(abertoN) : SemCartao;
		Afirmar($"[{SemGenero}] raca SEM genero: o cartao aberto traz a idade, o cargo, as datas e a historia -- e nao inventa a linha 'Gênero'",
				lidoN.Detalhes is { Estado: "abertos", HistoriaEDela: true } && Fato(lidoN, "Gênero").Length == 0
				&& Fato(lidoN, "Idade").StartsWith($"{namek.Idade} anos", StringComparison.Ordinal)
				&& Fato(lidoN, "Vocês se conhecem") == "há 8 dias"
				&& ProblemasDoCartao(lidoN, namek.Convivio, namek.Declaracao).Count == 0,
				string.Join(" | ", lidoN.Detalhes.Fatos.Select(f => $"{f.Rotulo}: {f.Valor}")) + " ; "
				+ string.Join("; ", ProblemasDoCartao(lidoN, namek.Convivio, namek.Declaracao)));

		// (o ultimo aberto fecha: as injecoes abaixo leem o cartao do Kuririn como ele estava na secao 4)
		if (CartaoDe(pg, SemGenero) is { } ultimo) BotaoDeDetalhes(ultimo)?.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.3;
		GameClient.EspiaoDeVerbos = null;
		GameServer.EscutaDeDetalhes = null;

		// ---------------------------------------------------------------- 9. AS REGRAS SABEM REPROVAR
		Nota("--- 9. o contrato sabe ficar vermelho ---");
		CartaoLido certo = depois;
		Afirmar("CONTROLE: o cartao certo nao tem problema nenhum (a regra nao reprova tudo)",
				ProblemasDoCartao(certo, k.Convivio, Relacao.Bom).Count == 0,
				string.Join("; ", ProblemasDoCartao(certo, k.Convivio, Relacao.Bom)));
		Injeta("...pega a PILULA de estado de volta no cartao (o desenho antigo: 'Inimigo mortal · neutro · RIVAL')",
			   ProblemasDoCartao(certo with { Pilulas = 3 }, k.Convivio, Relacao.Bom).Count > 0);
		Injeta("...pega a SEGUNDA BARRA (a de afinidade, ao lado da de convivio)",
			   ProblemasDoCartao(certo with { Barras = 2 }, k.Convivio, Relacao.Bom).Count > 0);
		Injeta("...pega DUAS opcoes acesas",
			   ProblemasDoCartao(certo with { Opcoes = [.. certo.Opcoes.Select(o => o.Qual == Relacao.Neutro ? o with { Escolhida = true } : o)] },
								 k.Convivio, Relacao.Bom).Count > 0);
		Injeta("...pega uma opcao FECHADA que da pra clicar ('Amor' aberto com 62 de convivio)",
			   ProblemasDoCartao(certo with { Opcoes = [.. certo.Opcoes.Select(o => o.Qual == Relacao.Amor ? o with { Apagada = false, Texto = "Amor" } : o)] },
								 k.Convivio, Relacao.Bom).Count > 0);
		Injeta("...pega a opcao fechada SEM o preco escrito",
			   ProblemasDoCartao(certo with { Opcoes = [.. certo.Opcoes.Select(o => o.Qual == Relacao.Amor ? o with { Texto = "Amor" } : o)] },
								 k.Convivio, Relacao.Bom).Count > 0);
		Injeta("...pega o convivio escrito diferente do do servidor",
			   ProblemasDoCartao(certo with { Convivio = k.Convivio + 1 }, k.Convivio, Relacao.Bom).Count > 0);
		Injeta("...pega a linha de efeito de OUTRA opcao (a do neutro num cartao de amigo)",
			   ProblemasDoCartao(certo with { Efeito = MenuJogo.EfeitoDe(Relacao.Neutro) }, k.Convivio, Relacao.Bom).Count > 0);
		// O TEXTO DE UM DEGRAU SO, com o defeito ligado no proprio `EfeitoDe`: a tela e a regra leem a MESMA funcao,
		// entao a comparacao de cima nao ve nada -- quem reprova e a regra dos dois degraus.
		try
		{
			MenuJogo.EfeitoEmUmDegrauSoDeTeste = true;
			Injeta("(defeito injetado: a linha de efeito com UM degrau so) a queda volta pra debaixo da furia, e a regra dos dois degraus REPROVA",
				   ProblemasDoCartao(certo with { Efeito = MenuJogo.EfeitoDe(Relacao.Bom) }, k.Convivio, Relacao.Bom).Count > 0,
				   MenuJogo.EfeitoDe(Relacao.Bom));
		}
		finally { MenuJogo.EfeitoEmUmDegrauSoDeTeste = false; }
		Injeta("a regra das palavras pega 'afinidade (automática)', 'Mal conhecido (0)', a pilula 'RIVAL' e o pedido de amizade",
			   PalavrasDaAfinidade(["afinidade (automática)"]).Count == 1 && PalavrasDaAfinidade(["Mal conhecido (0)"]).Count == 1
			   && PalavrasDaAfinidade(["RIVAL"]).Count == 1 && PalavrasDaAfinidade(["Fulano quer ser seu amigo"]).Count == 1);
		Injeta("...e NAO confunde as declaracoes de hoje com os rotulos de ontem ('Rival amigo', 'Inimigo mortal', 'Amigo')",
			   PalavrasDaAfinidade(["Rival amigo", "Inimigo mortal", "Amigo", "Desafeto", "Rival inimigo (15)"]).Count == 0,
			   string.Join("; ", PalavrasDaAfinidade(["Rival amigo", "Inimigo mortal", "Amigo", "Desafeto", "Rival inimigo (15)"])));

		// E O CONTRATO DOS DETALHES, sobre os dois cartoes lidos nas secoes 7 e 8 (o trancado e o aberto).
		DetalhesLidos doAberto = lidoK.Detalhes, doTrancado = lidoT.Detalhes;
		Afirmar("CONTROLE: os detalhes lidos (o aberto do Kuririn, o trancado da Bulma) nao tem problema nenhum",
				ProblemasDosDetalhes(doAberto, k.Convivio).Count == 0 && ProblemasDosDetalhes(doTrancado, trancada.Convivio).Count == 0,
				string.Join("; ", ProblemasDosDetalhes(doAberto, k.Convivio).Concat(ProblemasDosDetalhes(doTrancado, trancada.Convivio))));
		Injeta("o contrato dos detalhes pega o cartao ABERTO com convivio abaixo do minimo (os fatos na tela de quem nao podia ler)",
			   ProblemasDosDetalhes(doAberto, minimo - 1).Count > 0);
		Injeta("...pega o cartao TRANCADO que mostra a idade assim mesmo",
			   ProblemasDosDetalhes(doTrancado with { Fatos = [("Idade", "28 anos")] }, trancada.Convivio).Count > 0);
		Injeta("...pega o cartao TRANCADO que mostra a historia",
			   ProblemasDosDetalhes(doTrancado with { Historia = "uma historia", HistoriaEDela = true }, trancada.Convivio).Count > 0);
		Injeta("...pega a frase do trancado que nao diz o que falta",
			   ProblemasDosDetalhes(doTrancado with { Aviso = "Detalhes indisponíveis." }, trancada.Convivio).Count > 0);
		Injeta("...pega uma linha FORA da lista fechada no cartao aberto ('Poder de luta')",
			   ProblemasDosDetalhes(doAberto with { Fatos = [.. doAberto.Fatos, ("Poder de luta", "3.000.000")] }, k.Convivio).Count > 0);
		Injeta("...pega a CLASSE entrando como linha de detalhe",
			   ProblemasDosDetalhes(doAberto with { Fatos = [("Classe", "Legendary"), .. doAberto.Fatos] }, k.Convivio).Count > 0);
		Afirmar("CONTROLE: a lista fechada aceita as duas linhas que podem FALTAR (o genero, em raca sem genero; a data, em ficha antiga)",
				ProblemasDosDetalhes(doAberto with { Fatos = [.. doAberto.Fatos.Where(f => f.Rotulo is not ("Gênero" or "Vocês se conhecem"))] },
									 k.Convivio).Count == 0);
		Injeta("...mas pega o cartao aberto sem a IDADE, e as linhas fora de ordem",
			   ProblemasDosDetalhes(doAberto with { Fatos = [.. doAberto.Fatos.Where(f => f.Rotulo != "Idade")] }, k.Convivio).Count > 0
			   && ProblemasDosDetalhes(doAberto with { Fatos = [.. doAberto.Fatos.AsEnumerable().Reverse()] }, k.Convivio).Count > 0);
		Injeta("...pega o cartao aberto sem a caixa da historia", ProblemasDosDetalhes(doAberto with { Historia = null }, k.Convivio).Count > 0);
		Injeta("...pega o cartao SEM botao de detalhes, e o botao sem o preco em quem o convivio nao paga",
			   ProblemasDosDetalhes(DetalhesLidos.Fechados("Ver detalhes") with { Botoes = [] }, k.Convivio).Count > 0
			   && ProblemasDosDetalhes(DetalhesLidos.Fechados("Ver detalhes"), trancada.Convivio).Count > 0
			   && ProblemasDosDetalhes(DetalhesLidos.Fechados("Ver detalhes"), k.Convivio).Count == 0);

		// ---------------------------------------------------------------- A HISTORIA
		if (_historia.Count > 0)
		{
			string tira = _pasta + "conhecidos-0-historia.png";
			double pintada = TiraDeFotos.Montar(_historia, tira);
			_fotos.Add(tira);
			Afirmar($"a tira da historia saiu pintada (as {_historia.Count} telas lado a lado)", pintada > 0.5, $"{pintada:P0} pintada");
		}
		else Nota("sem janela: nenhuma foto foi tirada (as checagens de arvore valem; as fotos nao)");

		menu.Fechar();
	}
}
