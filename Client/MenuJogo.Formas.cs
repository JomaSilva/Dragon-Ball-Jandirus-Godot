using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Social;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A ABA FORMS -- a forma de agora, o multiplicador, e as formas que este corpo ja despertou.
/// O `MultTexto` mora aqui porque e desta aba (a assinatura dela, em MenuJogo.cs, tambem o usa).
///
/// ============================ A LINGUA DA LEARNING, NESTA ABA ============================
/// Ela era uma coluna de "rotulo ..... valor" com dois muros de texto no meio (foto de antes:
/// `aba-04-forms.png`). Agora abre com a FAIXA da forma de agora (o nome grande; maestria e dreno
/// na legenda), um cartao "Agora" com as duas linhas de contrato -- multiplicador total e poder
/// efetivo, que a `--diagbancada` le pelo TEXTO --, e um CARTAO POR FORMA despertada, com a barra
/// de maestria e a pilula EM USO. A explicacao longa virou uma nota curta com o resto no tooltip.
/// O que a aba DIZ nao mudou; mudou a forma de dizer. As pecas sao as de `MenuJogo.Pecas.cs`.
/// ==========================================================================================
/// </summary>
public partial class MenuJogo
{
	/// <summary>
	/// O MULTIPLICADOR COMO SE LE. A escala deste jogo vai de 1x a milhoes, e "x345191,1" ocupa a
	/// linha inteira sem dizer mais do que "x345 mil".
	///
	/// ELE TAMBEM E A ASSINATURA DA ABA FORMS (ver <see cref="Assinatura"/>), e isso e de proposito:
	/// a pagina remonta exatamente quando o TEXTO muda, nem antes nem depois. Comparar o double cru
	/// remontaria a aba cinco vezes por segundo enquanto o Ki oscila, sem um pixel mudar.
	/// </summary>
	private static string MultTexto(double m) => m switch
	{
		>= 1e9 => $"×{m / 1e9:0.##} B",
		>= 1e6 => $"×{m / 1e6:0.##} M",
		>= 1000 => $"×{m / 1000:0.##} mil",
		>= 100 => $"×{m:0}",
		>= 10 => $"×{m:0.#}",
		_ => $"×{m:0.##}",
	};

	// =====================================================================
	// FORMS -- a escada de transformacao
	// =====================================================================
	/// <summary>
	/// O QUE VOCE TEM: a forma de agora e as que ja despertaram. Mais nada.
	///
	/// ============================ A ESCADA FOI EMBORA ============================
	/// Esta aba listava TODOS os degraus, inclusive os travados, com uma faixa de distancia
	/// ("muito longe", "perto", "quase la", "no limiar"). O dono mandou tirar, e as duas razoes
	/// batem com o corte de sigilo que ele pediu na mensagem anterior:
	///
	///   1. A FAIXA ERA O BP DE VOLTA. Ela nascia de `BP / PortaBp`. Cinco faixas contra uma
	///      escada de degraus conhecidos deixam qualquer um binarizar o proprio poder em poucas
	///      sessoes de treino -- e o jogo inteiro acabou de ser arrumado pra que BP so vire
	///      numero com scouter. Esconder o digito e publicar a razao dele e esconder pela metade.
	///   2. LISTA DE DEGRAUS FUTUROS E TABELA DE PROGRESSAO. Saber de antemao que existem sete
	///      degraus acima transforma despertar -- que no anime e um acontecimento -- em barra de
	///      carregamento. O que o personagem sabe das proprias formas e o que ele ja viveu.
	/// =============================================================================
	///
	/// Quem quiser subir aperta C: a tentativa falhando E a informacao, como no original.
	/// </summary>
	private void AbaFormas(SheetState f)
	{
		Jandirus.Core.Forms.FormaDef? defAtual = Jandirus.Core.Forms.Catalogo.PorRede(_atributos.FormaAtual);
		if (defAtual is { Id: "base" }) defAtual = null;
		string atual = defAtual?.Id ?? Jandirus.Core.Forms.Catalogo.IdBase;

		// O LIVRO UMA VEZ SO, e nao um por linha: o `Livro()` remonta o dicionario inteiro a cada
		// chamada, e esta aba redesenha a cada quadro em que algo muda. Ele serve todas as leituras
		// de nome e de dreno daqui de baixo.
		Jandirus.Core.Forms.Maestrias livro = Livro();

		// ============================ A FAIXA: A FORMA DE AGORA ============================
		// O nome grande e, na legenda, o que se le de relance dela: maestria (ou a proficiencia da
		// skill, nas formas de disciplina) e o dreno. O ROTULO continua sendo "Forma", e a faixa
		// tambem e uma linha pra porta de bancada (`ValorDesenhado("Forms", "Forma")` le "normal ..."):
		// quem lia a linha antiga continua lendo.
		//
		// O NOME SAI DO CATALOGO E NAO DA ENTRADA: `Catalogo.NomeDe` e nao `defAtual.Nome`. O Super
		// Saiyajin a 100% de maestria se chama "Super Saiyajin Grade 4" e continua sendo a MESMA forma
		// (ver `Catalogo.DominouOSuperSaiyajin`). Aqui o cliente tem o livro de maestrias do proprio
		// dono da ficha, entao a pergunta se responde sem pedir nada ao servidor.
		//
		// NA BASE a legenda e o gesto: e a unica instrucao que esta aba precisa dar, e ela morava num
		// paragrafo de tres linhas no meio da pagina.
		// ==================================================================================
		string nome = defAtual != null ? Jandirus.Core.Forms.Catalogo.NomeDe(defAtual, livro) : "normal";
		Faixa("Forma", nome,
			  defAtual != null
				  ? FichaDaForma(atual, livro)
				  : "segure C pra reunir energia  ·  toque C duas vezes pra tentar subir  ·  X volta ao normal",
			  defAtual != null ? Tema.Destaque : Tema.Texto);

		// ============================ O CARTAO "AGORA": AS DUAS LINHAS DE CONTRATO ============================
		// O TOTAL SAI DA RAZAO `expressedBP / BP`, calculada no servidor, e NAO de multiplicar os
		// fatores um a um. Isso nao e preferencia de estilo: neste jogo os fatores tem tres FAMILIAS
		// (ver o cabecalho de `Fighter.Power.cs`). Medido: Kaio-ken 2x com Mistico 2x da 3x de
		// verdade, porque os dois SOMAM na base -- o produto ingenuo diria 4x, 33% a mais. Com forma,
		// raiva, Kaio-ken, Mistico e gravidade juntos o erro passa de 126%, e o corte de 25% do revive
		// por Zeni nao teria nem onde caber num produto de fatores.
		//
		// POR ISSO NAO HA QUEBRA POR FATOR AQUI. Uma lista "forma 56x · raiva 1,3x · ..." e ilustracao
		// legitima, mas ela so fecha com o total se for desenhada em DOIS blocos (o que soma na base e
		// o que multiplica depois) -- e uma fila de "x" que nao bate com o numero de cima ensina que a
		// tela mente. Enquanto os fatores nao viajarem no pacote, fica o total, que e o honesto.
		//
		// SEM SCOUTER ELES APARECEM DO MESMO JEITO: "x345" nao diz de QUE numero, e sem aparelho o BP e
		// o BP expresso nem chegam ao cliente. E o oposto da faixa de distancia que esta aba tinha, que
		// nascia de `BP / PortaBp` e por isso ENTREGAVA o absoluto contra a escada de degraus conhecida.
		//
		// AS DUAS SAO `Linha` E NAO FAIXA, de proposito: a `--diagbancada` desmonta "Multiplicador total"
		// com um parser que multiplica por mil/M/B quando o TEXTO contem " mil", " M" ou " B" -- uma faixa
		// entregaria a legenda junto do numero, e qualquer "M" maiusculo depois de espaco viraria um milhao.
		// Os TEXTOS ("×1,09", "98%") sao os de sempre, porque e assim que a bancada os desmonta.
		// ====================================================================================================
		VBoxContainer agora = Cartao("Agora");
		Linha("Multiplicador total", MultTexto(f.MultTotal),
			  f.MultTotal > 1.01 ? Tema.Destaque : Tema.Texto, agora);
		Linha("Poder efetivo", $"{f.Inteireza * 100:0.#}%",
			  f.Inteireza >= 0.9 ? Tema.Bom : f.Inteireza <= 0.5 ? Tema.Perigo : Tema.Texto, agora);
		NotaComDica(Nota("O multiplicador é o que o seu BP base virou agora; o poder efetivo é quanto dele o corpo "
						 + "consegue botar pra fora.", agora),
			"Transformar mexe no multiplicador e NÃO no poder efetivo: a forma multiplica os dois lados dessa conta.\n"
			+ "Quem mexe no poder efetivo é Ki, ferimento, peso, gravidade e idade.\n\n"
			+ "Maestria SÓ cresce dentro da forma, gastando Ki -- é o único eixo do jogo que não se compra.\n"
			+ "As formas de uma disciplina divina são exceção: elas não têm maestria própria -- usá-las sobe a "
			+ "proficiência da SKILL, e essa só cresce lutando.");

		// SO O QUE JA DESPERTOU. Maestria > 0 quer dizer que este corpo ja esteve nessa forma
		// alguma vez -- e o unico registro honesto de "eu sei fazer isto".
		//
		// NAS FORMAS DE DISCIPLINA O REGISTRO HONESTO E OUTRO: elas nao guardam maestria nenhuma, e
		// o que prova que o corpo as conhece e a FAIXA de proficiencia que as concedeu ter sido
		// cruzada (`Degrau.Pct`). Sem esta metade elas sumiriam da aba no instante em que o jogador
		// voltasse pra base -- a regra existiria e ninguem veria.
		var minhas = new List<Jandirus.Core.Forms.FormaDef>();
		foreach (Jandirus.Core.Forms.FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
			if (d.Id != Jandirus.Core.Forms.Catalogo.IdBase
				&& (d.Id == atual || Maestria(d.Id) > 0 || DespertouPelaDisciplina(d.Id)))
				minhas.Add(d);

		Secao("Formas que você desperta");

		if (minhas.Count == 0)
		{
			Aviso("Nenhuma, ainda. Nada garante que exista alguma -- e se existir, ela não vem por "
				+ "treino marcado: vem na hora em que vier.");
			return;
		}

		// UM CARTAO POR FORMA, numa coluna so: sao cartoes de ITEM (nome, barra, dreno), e a ordem e a
		// do catalogo -- a mesma ficha desenha a mesma fila, sempre.
		foreach (Jandirus.Core.Forms.FormaDef d in minhas) CartaoDeForma(d, d.Id == atual, livro);

		Aviso("O que vem depois -- se vier -- você descobre tentando.");
	}

	/// <summary>
	/// UM CARTAO DE FORMA DESPERTADA: o nome no cabecalho (com a pilula EM USO quando e a de agora), a
	/// barra de maestria -- ou de proficiencia da skill, nas formas de disciplina -- e o dreno.
	///
	/// MESMO FUNIL DE NOME DA FAIXA LA DE CIMA (`Catalogo.NomeDe`). Sao os dois lugares desta tela que
	/// escrevem o nome de uma forma, e escrever `d.Nome` num e `NomeDe` no outro faria a aba dizer
	/// "Grade 4" em cima e "Super Saiyajin" tres cartoes abaixo, sobre a mesma forma.
	///
	/// A MESMA TROCA DA FAIXA na barra: forma de disciplina relata a proficiencia da SKILL, e diz que e
	/// da skill -- "maestria 0,0%" ao lado de uma forma que o jogador acabou de usar leria como
	/// progresso perdido.
	///
	/// O metadado `cartao` = "forma" e o `titulo` = nome sao como a bancada (`--diagabas`, F7) conta os
	/// cartoes e casa cada um com a ficha (ver <see cref="MarcarCartaoDeItem"/>).
	/// </summary>
	private void CartaoDeForma(Jandirus.Core.Forms.FormaDef d, bool emUso, Jandirus.Core.Forms.Maestrias livro)
	{
		string nome = Jandirus.Core.Forms.Catalogo.NomeDe(d, livro);
		VBoxContainer corpo = Cartao("");
		MarcarCartaoDeItem(corpo, "forma", nome);

		corpo.AddChild(Cabecalho(nome, emUso ? Tema.Destaque : Tema.Texto, (emUso ? "EM USO" : "", Tema.Destaque)));

		if (ProficienciaDaForma(d.Id) is { } p)
			LinhaComBarra($"proficiência em {p.Nome}", $"{p.Pct:0.#}%", p.Pct / 100, Tema.Destaque, corpo);
		else
		{
			double m = Maestria(d.Id);
			LinhaComBarra("maestria", $"{m:0.#}%", m / 100, Tema.Destaque, corpo);
		}

		// O DRENO DESTA FORMA COM A MAESTRIA DE HOJE -- o mesmo `DrenoPorSegundo` que o servidor cobra.
		// "nenhum" e verde porque e o premio da maestria: dominar a forma e poder ANDAR nela.
		double dreno = Jandirus.Core.Forms.Catalogo.DrenoPorSegundo(d.Id, livro) * 100;
		Linha("dreno de Ki", dreno > 0 ? $"{dreno:0.##}% do Ki por segundo" : "nenhum: dá pra viver nela",
			  dreno > 0 ? Tema.Texto : Tema.Bom, corpo);
	}

	/// <summary>A legenda da faixa quando se esta numa forma: "maestria 37,2%  ·  dreno 0,5% do Ki/s".</summary>
	private string FichaDaForma(string id, Jandirus.Core.Forms.Maestrias livro)
	{
		string grau = ProficienciaDaForma(id) is { } p
			? $"proficiência em {p.Nome} {p.Pct:0.#}%"
			: $"maestria {Maestria(id):0.#}%";
		double dreno = Jandirus.Core.Forms.Catalogo.DrenoPorSegundo(id, livro) * 100;
		return grau + (dreno > 0 ? $"  ·  dreno {dreno:0.##}% do Ki/s" : "  ·  sem dreno");
	}

	/// <summary>
	/// A PROFICIENCIA QUE ESTA FORMA RELATA NO LUGAR DA MAESTRIA. Nulo = ela tem maestria propria.
	///
	/// Quem responde "esta forma e de uma disciplina?" e o <see cref="Jandirus.Core.Forms.Disciplinas.DaForma"/>,
	/// o mesmo funil que o servidor usa -- ver o cabecalho dele. Aqui so falta cruzar com a disciplina
	/// que ESTE corpo trilhou, que chega em byte na ficha lenta; os dois caminhos se excluem, entao
	/// uma forma da outra escola nunca casa e mostra 0 (que e a verdade: ela nao e alcancavel).
	/// </summary>
	private (string Nome, double Pct)? ProficienciaDaForma(string forma)
	{
		if (Jandirus.Core.Forms.Disciplinas.DaForma(forma) is not { } par) return null;
		bool minha = _atributos.Disciplina == Jandirus.Core.Forms.Disciplinas.Rede(par.Def.Tipo);
		return (par.Def.Nome, minha ? _atributos.DiscReal : 0);
	}

	/// <summary>
	/// ESTE CORPO JA DESPERTOU ESTA FORMA DE DISCIPLINA? A faixa que a concede foi cruzada.
	///
	/// E o substituto do "maestria > 0" pras quatro formas divinas: elas nao acumulam maestria, entao
	/// o registro de "eu sei fazer isto" e a proficiencia REAL ter passado do <see cref="Jandirus.Core.Forms.Degrau.Pct"/>
	/// da faixa que anuncia a forma (20% pro Sign/Destroyer, 60% pro Perfected/Ultra Ego).
	/// </summary>
	private bool DespertouPelaDisciplina(string forma) =>
		Jandirus.Core.Forms.Disciplinas.DaForma(forma) is { } par
		&& _atributos.Disciplina == Jandirus.Core.Forms.Disciplinas.Rede(par.Def.Tipo)
		&& _atributos.DiscReal >= par.Faixa.Pct;

	private double Maestria(string forma)
	{
		ushort alvo = Jandirus.Core.Forms.Catalogo.Rede(forma);
		foreach ((ushort id, float pct) in _atributos.Maestrias ?? [])
			if (id == alvo) return pct;
		return 0;
	}

	/// <summary>As maestrias num formato que o Core entende, pra calcular dreno e multiplicador.</summary>
	private Jandirus.Core.Forms.Maestrias Livro()
	{
		var m = new Jandirus.Core.Forms.Maestrias();
		foreach ((ushort id, float pct) in _atributos.Maestrias ?? [])
			if (Jandirus.Core.Forms.Catalogo.PorRede(id) is { } d) m.Por(d.Id, pct);
		return m;
	}

	// =====================================================================
	// PECAS DESTAS ABAS (Forms, Cargos, People, World, Nav) QUE DEVERIAM SUBIR PRA MenuJogo.Pecas.cs
	// =====================================================================
	// `MenuJogo.Pecas.cs` e compartilhado entre as frentes do redesenho e nao se edita daqui. As tres
	// pecas abaixo faltavam la e sao de uso geral; moram aqui ate subirem.

	/// <summary>
	/// MARCA UM CARTAO DE ITEM (forma, cargo, pessoa) pela bancada achar: troca o `cartao` = "secao" que
	/// o <see cref="Cartao"/> escreve pelo TIPO do item, e poe o nome no `titulo`. E o mesmo desenho dos
	/// cards da Learning ("arvore", "skill"): a identidade vai em metadado, e nao no `Name`, porque o
	/// Godot renomeia irmaos homonimos.
	/// </summary>
	private static void MarcarCartaoDeItem(VBoxContainer corpo, string tipo, string titulo)
	{
		if (corpo.GetParent() is not PanelContainer painel) return;
		painel.SetMeta("cartao", tipo);
		painel.SetMeta("titulo", titulo);
	}

	/// <summary>
	/// UMA NOTA COM O RESTO DA EXPLICACAO NO TOOLTIP. A regra das abas e "no maximo uma nota curta por
	/// cartao; o resto vai pro tooltip" -- e um `Label` nasce com `MouseFilter.Ignore`, ou seja, nunca
	/// recebe o mouse e nunca mostra tooltip nenhum. `Pass` e o filtro certo: mostra a dica e deixa o
	/// clique e a roda seguirem pra quem esta embaixo.
	/// </summary>
	private static Label NotaComDica(Label nota, string dica)
	{
		nota.TooltipText = dica;
		nota.MouseFilter = Control.MouseFilterEnum.Pass;
		return nota;
	}

	/// <summary>
	/// A LINHA DE TEXTO LONGO: o rotulo apagado a esquerda e um texto que QUEBRA LINHA ocupando o resto.
	/// E o irmao da <see cref="LinhaSolta"/> pra valores que nao cabem num numero -- o "dá: Kame Style,
	/// Turtle School - Kamehameha, ..." de um cargo tem ate 400 caracteres, e na linha de sempre ele
	/// sairia da pagina pela direita.
	/// </summary>
	private static void LinhaDeTextoLongo(string rotulo, string texto, Color? cor, Control pai)
	{
		var h = new HBoxContainer();
		h.AddThemeConstantOverride("separation", 8);
		// O ROTULO FICA NO ALTO da linha, ao lado da PRIMEIRA linha do texto: com o `Fill` de fabrica o
		// label ganhava a altura inteira e o "dá" caia no meio de tres linhas de dadiva.
		var a = new Label { Text = rotulo, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
		a.AddThemeColorOverride("font_color", Tema.TextoFraco);
		a.AddThemeFontSizeOverride("font_size", 12);
		h.AddChild(a);
		var b = new Label
		{
			Text = texto, AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		b.AddThemeColorOverride("font_color", cor ?? Tema.Texto);
		b.AddThemeFontSizeOverride("font_size", 12);
		h.AddChild(b);
		pai.AddChild(h);
	}

	/// <summary>
	/// O PEDACO DESTA ABA NA ASSINATURA DE CACHE (ver `MenuJogo.Assinatura`): tudo que ESTA aba
	/// desenha e que a assinatura basica (em MenuJogo.cs) nao cobre entra aqui, nos mesmos
	/// arredondamentos em que e desenhado.
	///
	/// VAZIO PORQUE A BASICA JA COBRE TUDO O QUE ESTA PAGINA ESCREVE, conferido peca a peca: o nome da
	/// faixa e a pilula EM USO saem de `FormaAtual`; as barras, o dreno de cada cartao e a legenda da
	/// faixa saem das `Maestrias` (a um decimo, que e como sao desenhadas) e de `Disciplina:DiscReal`;
	/// as duas linhas do cartao Agora entram como o TEXTO que viram (`MultTexto`, `Inteireza:0.#`). A
	/// legenda da base e uma frase fixa.
	/// </summary>
	private string ExtraDaAssinaturaDeFormas(SheetState f) => "";
}
