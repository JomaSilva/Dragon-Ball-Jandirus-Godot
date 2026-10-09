namespace Jandirus.Core.World;

/// <summary>
/// O QUE UM BLOCO E, PRA REGRA. Tres classes, e so tres -- o pedido do dono (2026-10-09): *"adicione
/// construcao de base (paredes, pisos e portas) no jogo"*.
/// </summary>
public enum ClasseDeBloco : byte
{
	/// <summary>Nao barra nem cega. Cobre: a celula passa a estar sob teto.</summary>
	Piso = 0,

	/// <summary>Barra o corpo (a pe e voando), cega e cobre.</summary>
	Parede = 1,

	/// <summary>Uma parede que abre: fechada e parede, aberta e piso.</summary>
	Porta = 2,
}

/// <summary>Uma linha do `blocos.json` -- um tile que se pode erguer.</summary>
public sealed class BlocoDef
{
	/// <summary>
	/// A POSICAO NO CATALOGO. E o que viaja no fio (as duas pontas leem o mesmo arquivo); o DISCO guarda
	/// o <see cref="Id"/>, que sobrevive a um catalogo regerado com outra ordem.
	/// </summary>
	public ushort Numero;

	/// <summary>A folha do typepath do DM, sem o `/turf/build/` ("brickwall", "Door/Door1").</summary>
	public string Id = "";

	public string Nome = "";
	public ClasseDeBloco Classe;

	/// <summary>O `.png` da folha (o `.dmi` convertido), e a celula do quadro dentro dela.</summary>
	public string Folha = "";
	public int X, Y;

	/// <summary>O lado do quadro na folha, em pixels. Todo bloco do catalogo e 32.</summary>
	public int Lado = ZoneCollision.TileSize;

	/// <summary>So a porta: o `SpriteFrames` com os quatro estados (`closed`, `opening`, `open`, `closing`).</summary>
	public string Arte = "";
}

/// <summary>
/// O CATALOGO DE BLOCOS -- o que a tecla M do BYOND listava, em dado.
///
/// ============================ DE ONDE VEM ============================
/// Do `/turf/build/*` do original (`Modules/Turfs/Building/buildturfs.dm`), pelo comando `blocos` do
/// pipeline (`Tools/AssetPipeline/BlocosScanner.cs`). La a janela lista TODO subtipo com icone
/// (`BuildWindow.dm:103-115`): 155 tiles numa grade so, sem aba e sem busca, na ordem de declaracao.
///
/// AQUI SAO TRES CLASSES, e quem decide a classe e a `density` do tipo: denso e parede, o resto e piso,
/// e o que mora em `/turf/build/Door/` e porta. O que o extrator deixa de fora, e por que, esta no
/// cabecalho dele (agua, lava e ceu de construir, os mosaicos HD, a porta de quatro direcoes).
/// =====================================================================
/// </summary>
public sealed class CatalogoDeBlocos
{
	private readonly List<BlocoDef> _todos = [];
	private readonly Dictionary<string, BlocoDef> _porId = new(StringComparer.Ordinal);

	public IReadOnlyList<BlocoDef> Todos => _todos;
	public int Total => _todos.Count;

	public BlocoDef? Get(string? id) => id != null && _porId.TryGetValue(id, out BlocoDef? b) ? b : null;

	public BlocoDef? PorNumero(int n) => n >= 0 && n < _todos.Count ? _todos[n] : null;

	/// <summary>
	/// Le o `blocos.json`. Mesmo leitor a mao dos outros catalogos do Core (`CatalogoDeObras.Parse`):
	/// um objeto por linha, campos simples, sem aninhamento.
	/// </summary>
	public static CatalogoDeBlocos Parse(string json)
	{
		var cat = new CatalogoDeBlocos();
		if (string.IsNullOrWhiteSpace(json)) return cat;

		int i = 0;
		while (true)
		{
			int a = json.IndexOf('{', i);
			if (a < 0) break;
			int b = json.IndexOf('}', a);
			if (b < 0) break;
			string bloco = json[(a + 1)..b];
			i = b + 1;

			var d = new BlocoDef
			{
				Id = Str(bloco, "id"),
				Nome = Str(bloco, "nome"),
				Classe = Str(bloco, "classe") switch
				{
					"parede" => ClasseDeBloco.Parede,
					"porta" => ClasseDeBloco.Porta,
					_ => ClasseDeBloco.Piso,
				},
				Folha = Str(bloco, "folha"),
				X = Int(bloco, "x"),
				Y = Int(bloco, "y"),
				Arte = Str(bloco, "arte"),
			};
			int lado = Int(bloco, "lado");
			if (lado > 0) d.Lado = lado;
			if (d.Id.Length == 0 || cat._porId.ContainsKey(d.Id)) continue;

			d.Numero = (ushort)cat._todos.Count;
			cat._todos.Add(d);
			cat._porId[d.Id] = d;
		}
		return cat;
	}

	private static string Str(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return "";
		int dp = bloco.IndexOf(':', i);
		int a = bloco.IndexOf('"', dp + 1);
		int b = bloco.IndexOf('"', a + 1);
		return a < 0 || b < 0 ? "" : bloco[(a + 1)..b];
	}

	private static int Int(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return 0;
		int dp = bloco.IndexOf(':', i);
		int fim = dp + 1;
		while (fim < bloco.Length && (char.IsDigit(bloco[fim]) || bloco[fim] is ' ' or '-')) fim++;
		return int.TryParse(bloco[(dp + 1)..fim].Trim(), out int v) ? v : 0;
	}
}

/// <summary>Por que um bloco nao sobe (ou nao sai) de uma celula. `Pode` = sobe.</summary>
public enum RecusaDeBloco : byte
{
	Pode = 0,
	Longe,
	Beirada,
	Duro,
	Agua,
	Nuvem,
	Passagem,
	Predio,
	Cenario,
	DeOutro,
	Coisa,
	Corpo,
	Zona,
	Nada,
	Cheio,
}

/// <summary>
/// AS REGRAS DE ERGUER -- as perguntas que o FANTASMA do cliente e o SERVIDOR fazem com a mesma funcao.
///
/// ============================ O QUE O ORIGINAL PERGUNTA ============================
/// `turf/Click` e `turf/MouseDrag` (`click.dm:44-48`, `:68-75`), com a janela de construir aberta:
/// <code>
/// if(usr.isbuilding &amp;&amp; usr.buildpath)
///     if(!usr.KO &amp;&amp; usr.move &amp;&amp; get_dist(usr,T) &lt;= 2)
///         if(!T.Exclusive &amp;&amp; T.destroyable &amp;&amp; (T.Free || T.proprietor == usr.ckey))
///             usr.BuildATileHere(locate(T.x,T.y,T.z))
/// </code>
/// e o `BuildATileHere` (`buildable.dm:379-461`) nao pergunta mais nada: nao ha custo (`buildcost` e
/// declarado e nunca lido), nem tempo, nem limite, nem requisito de tecnologia (`techlevel` nulo em
/// todo subtipo). O tile novo SUBSTITUI o turf, ganha `proprietor = usr.ckey` e
/// `Resistance = intBPcap` (`:404-405`, `:439`, `:453`).
/// ==================================================================================
///
/// ============================ O QUE FICOU IGUAL ============================
///   * de graca, na hora, sem requisito;
///   * so em celula que pode cair: o `T.destroyable` e o plano `.duro` (`Indestrutivel`), a beirada
///     do mapa, e a agua (`turf/Water/*` tem `destroyable = 0`, `Turfs.dm:1602-1605`);
///   * a boca de passagem nao aceita (`turf/Teleporters`, `destroyable = 0`, `Turfs.dm:101-103`);
///   * por cima do tile de OUTRO nao se constroi (`Free = 0` no `turf/build`, `buildturfs.dm:2`), e
///     por cima do proprio sim -- e assim que se troca um piso;
///   * por cima do que o MAPA ja trouxe erguido tambem nao (`/turf/build/*` de mapa tem `Free = 0` e
///     `proprietor` nulo: ninguem passa no teste).
/// ===========================================================================
///
/// ============================ DIVERGENCIAS DECLARADAS ============================
///   * O ALCANCE E 4 CELULAS, e nao 2. O dono pediu *"um estilo minecraft"*, e la se constroi a
///     distancia de braco esticado; com 2 a parede so sobe colada no corpo, e arrastar o mouse pra
///     pintar uma fileira para a cada passo.
///   * CENARIO DE PE NAO SE SUBSTITUI. No DM todo turf natural e `Free = 1`: erguer um piso em cima
///     de uma montanha troca a montanha pelo piso -- um tunel de graca por qualquer parede do mapa,
///     sem olhar a `Resistance` dela. Aqui a celula que barra tem de CAIR primeiro (na porrada, como
///     sempre caiu); depois de caida aceita bloco, e e assim que se cava uma base na rocha.
///   * O PREDIO DO MAPA E INTEIRO DO MAPA: toda celula que nasceu sob teto (`.dentro`) recusa, e nao
///     so as que eram `/turf/build`. O plano junta as duas coisas (ver `CelulaInterna`), e o berco da
///     Terra fica DENTRO do Banco -- uma parede ali trancaria o nascimento de todo mundo.
///   * PAREDE E PORTA NAO SOBEM EM CIMA DE NINGUEM, nem de uma maquina. O DM nao olha (`BuildATileHere`
///     nao testa mob nem obj), e o corpo fica preso dentro do tile denso.
///   * NAO HA PINCEL 3x3 (`BuildWindow.dm:194-204`): arrastar o mouse faz o papel dele, e o pincel
///     do original pulava todas as conferencias nas oito vizinhas (`buildable.dm:387-389`).
///   * NAO HA "OUTDOORS" (`BuildWindow.dm:206-216`). No DM o botao nao chega a valer: o `wasoutside`
///     so e escrito DEPOIS do `New()` que ja agendou a ida pra area interna (`buildable.dm:447-452`
///     contra `buildturfs.dm:4-22`). Todo bloco cobre a celula, que e o que la acontece de fato.
/// =================================================================================
/// </summary>
public static class RegrasDeBloco
{
	/// <summary>
	/// A QUE DISTANCIA SE CONSTROI, em celulas, contada como o `get_dist` do BYOND (a maior das duas
	/// diferencas): um quadrado de 9x9 em volta da celula dos pes. Ver a primeira divergencia do cabecalho.
	/// </summary>
	public const int Alcance = 4;

	/// <summary>
	/// O TETO DE BLOCOS POR CONTA. No DM nao ha limite (`builtobjects` so conta). Aqui ha um, largo, e
	/// ele nao e regra de jogo: e o que impede uma conta de cobrir um mapa de 250 mil celulas e fazer o
	/// retrato da zona (seis bytes por bloco, mandado a quem chega) pesar megabytes.
	/// </summary>
	public const int TetoPorConta = 6000;

	/// <summary>Quantas letras cabem na senha de uma porta.</summary>
	public const int MaxSenha = 16;

	/// <summary>
	/// NESTA ZONA SE CONSTROI? Planeta, de arquivo ou sorteado. Ficam de fora:
	///   * o ESPACO (nao ha chao);
	///   * todo INTERIOR (nave, Dimensao Mental): ele morre com quem o sustenta, e o que estivesse
	///     erguido la ficaria gravado numa zona onde ninguem mais entra -- o mesmo motivo de nao se
	///     assentar maquina dentro de nave (`GameServer.Posicionar`);
	///   * o OUTRO MUNDO: la o cenario nao se quebra, pra barreira do Enma valer (`DerrubarCelula`), e
	///     uma parede erguida na frente da mesa dele seria a mesma burla pelo avesso;
	///   * a SALA DO TEMPO, que nao tem borda e cujo chao e o vazio.
	/// </summary>
	public static bool ZonaAceita(ZoneKey z) =>
		z.Kind != ZoneKey.KindInterior && !Espaco.EhEspaco(z) && !Alem.EhOAlem(z) && !SalaDoTempo.SemBorda(z);

	/// <summary>A celula sobre a qual o corpo esta: a dos PES, como em toda regra de lugar deste port.</summary>
	public static (int Cx, int Cy) CelulaDoCorpo(Vec2 centro) => CelulaInterna.CelulaDoCorpo(centro);

	/// <summary>Esta celula esta ao alcance de quem esta em <paramref name="corpo"/>?</summary>
	public static bool AoAlcance(Vec2 corpo, int cx, int cy)
	{
		(int px, int py) = CelulaDoCorpo(corpo);
		return Math.Max(Math.Abs(cx - px), Math.Abs(cy - py)) <= Alcance;
	}

	/// <summary>
	/// O QUE O TERRENO DIZ DESTA CELULA -- a parte da regra que so depende do mapa e de onde o corpo esta.
	/// O que depende de LISTA (o bloco que ja esta la, a maquina, o corpo em cima) e de quem chama, com a
	/// lista que cada ponta tem.
	/// </summary>
	/// <param name="caiu">O cenario desta celula ja caiu? Vem da lista de estrago de quem pergunta.</param>
	public static RecusaDeBloco DoTerreno(ZoneCollision? mapa, Vec2 corpo, int cx, int cy, bool caiu)
	{
		if (mapa == null) return RecusaDeBloco.Zona;
		if (!AoAlcance(corpo, cx, cy)) return RecusaDeBloco.Longe;
		if (cx < 0 || cy < 0 || cx >= mapa.Width || cy >= mapa.Height || mapa.NaBorda(cx, cy)) return RecusaDeBloco.Beirada;
		if (mapa.Selada(cx, cy)) return RecusaDeBloco.Passagem;

		// A AGUA E A NUVEM ANTES DO `.duro`: a agua do mapa TAMBEM e indestrutivel (`turf/Water/*`,
		// `destroyable = 0`), e o cliente nao le o plano do que nao se quebra -- com o duro na frente o
		// fantasma dizia "na agua" e o servidor respondia "este chao nao aceita". A mesma celula, a mesma frase.
		if (mapa.EhAgua(cx, cy)) return RecusaDeBloco.Agua;
		if (mapa.EhNuvem(cx, cy)) return RecusaDeBloco.Nuvem;
		if (mapa.Indestrutivel(cx, cy)) return RecusaDeBloco.Duro;
		if (mapa.NasceuDentro(cx, cy)) return RecusaDeBloco.Predio;

		// CENARIO DE PE: o bit do arquivo, que nao caiu. Cobre o muro, a arvore que e tile, a porta do
		// mapa (aberta ou fechada -- ela nao esta na lista de estrago) e a mobilia que o mapa trouxe.
		if (mapa.BloqueadaNoArquivo(cx, cy) && !caiu) return RecusaDeBloco.Cenario;
		return RecusaDeBloco.Pode;
	}

	/// <summary>A caixa dos pes de um corpo em <paramref name="centro"/> toca esta celula?</summary>
	public static bool CorpoNaCelula(Vec2 centro, int cx, int cy)
	{
		const float T = ZoneCollision.TileSize;
		float pes = centro.Y + MoveRules.FeetOffsetY;
		return centro.X + MoveRules.BodyHalfW > cx * T && centro.X - MoveRules.BodyHalfW < (cx + 1) * T
			   && pes + MoveRules.BodyHalfH > cy * T && pes - MoveRules.BodyHalfH < (cy + 1) * T;
	}

	/// <summary>A frase de cada recusa. Uma so, pras duas pontas: o fantasma diz o que o servidor diria.</summary>
	public static string Motivo(RecusaDeBloco r) => r switch
	{
		RecusaDeBloco.Longe => $"longe demais: você constrói a até {Alcance} quadros de onde está.",
		RecusaDeBloco.Beirada => "isto é o limite do mundo -- aqui não se constrói.",
		RecusaDeBloco.Duro => "este chão não aceita construção.",
		RecusaDeBloco.Agua => "não dá pra construir na água.",
		RecusaDeBloco.Nuvem => "não dá pra construir em cima de nuvem.",
		RecusaDeBloco.Passagem => "isto é uma passagem -- não dá pra tapar.",
		RecusaDeBloco.Predio => "isto é um interior do próprio mapa (prédio ou caverna): aqui não se constrói.",
		RecusaDeBloco.Cenario => "tem cenário de pé aqui. Derrube primeiro, depois construa.",
		RecusaDeBloco.DeOutro => "isto é de outra pessoa.",
		RecusaDeBloco.Coisa => "tem uma máquina aqui: parede e porta não sobem em cima dela.",
		RecusaDeBloco.Corpo => "tem alguém em cima: parede e porta não sobem em cima de gente.",
		RecusaDeBloco.Zona => "aqui não se constrói.",
		RecusaDeBloco.Nada => "não há nada seu aqui pra desmanchar.",
		RecusaDeBloco.Cheio => $"você já ergueu {TetoPorConta} blocos, que é o teto. Desmanche algum pra continuar.",
		_ => "",
	};

	/// <summary>
	/// A SENHA COMO ELA FICA GUARDADA: sem espaco nas pontas, sem quebra de linha, cortada no teto.
	/// Vazia quer dizer "sem senha". Quem limpa e o servidor; a caixa do cliente so limita o comprimento.
	/// </summary>
	public static string SenhaLimpa(string? crua)
	{
		if (string.IsNullOrWhiteSpace(crua)) return "";
		var sb = new System.Text.StringBuilder(crua.Length);
		foreach (char c in crua.Trim())
			if (!char.IsControl(c)) sb.Append(c);
		string s = sb.ToString();
		return s.Length > MaxSenha ? s[..MaxSenha] : s;
	}
}

/// <summary>
/// A FORCA DO QUE SE CONSTROI -- o `intBPcap` do original.
///
/// `mob/proc/Check_Tech()` (`Modules/Tech/TechSupport.dm:14-26`), recalculado a cada tique de stats:
/// <code>
/// if(techskill&lt;8) intBPcap = (relBPmax * techmod) / max((11/max(log(1.1,max(techskill,1)),1))*techmod,1)
/// else            intBPcap = (max(log(8,max(techskill,1)),1) * relBPmax * techmod) / max(log(max(techskill*techmod,1)),1)
/// intBPcap = max(relBPmax * 8, intBPcap)
/// </code>
/// O tile erguido leva esse numero como `Resistance` no instante em que sobe (`buildable.dm:405`,
/// `:453`), e ele nao muda mais -- e uma foto. O piso de `relBPmax * 8` domina em quase toda ficha: a
/// parede de alguem aguenta oito vezes o teto de treino dele, e so cai pra quem EXPRESSA mais que isso.
///
/// DIVERGENCIA DECLARADA: a linha seguinte do DM multiplica pelo `intAsc` da Ascensao
/// (`TechSupport.dm:24-26`, que so vale com `AscensionStarted`). A Ascensao nao esta portada; aqui o
/// fator e 1, que e o que o DM usa com ela desligada.
/// </summary>
public static class TetoDeTecnologia
{
	public static double De(double relBPmax, double techskill, double techmod)
	{
		double cap = techskill < 8
			? relBPmax * techmod / Math.Max(11 / Math.Max(Math.Log(Math.Max(techskill, 1), 1.1), 1) * techmod, 1)
			: Math.Max(Math.Log(Math.Max(techskill, 1), 8), 1) * relBPmax * techmod
			  / Math.Max(Math.Log(Math.Max(techskill * techmod, 1)), 1);
		return Math.Max(relBPmax * 8, cap);
	}
}
