namespace Jandirus.Core.World;

/// <summary>Uma zona pre-feita, como o conversor de mapa a registrou no manifest.json.</summary>
public sealed class ZoneEntry
{
	public string Zona = "";      // nome curto ("Earth", "Namek", "Vegeta"...)
	public int Z;                 // o antigo andar do BYOND, so pra rastreabilidade
	public string Cena = "";      // res:// da cena que o CLIENTE instancia
	public string Colisao = "";   // res:// do bitset que o SERVIDOR le

	/// <summary>
	/// res:// das CELULAS do cenario, em pedacos de 64x64 (ver <see cref="PedacosDoMapa"/>).
	///
	/// Elas nao moram na cena porque um `TileMapLayer` monta o desenho de TODAS as celulas que
	/// tiver no primeiro quadro -- 708 ms na Terra. Fora da cena, o cliente entrega ao tilemap so
	/// os pedacos que a camera alcanca. O SERVIDOR nao le este arquivo: ele nao desenha nada, e o
	/// que ele precisa do cenario ja esta no `.col`.
	/// </summary>
	public string Pedacos = "";
	public string Visao = "";     // res:// do bitset do que CEGA (parede e porta; ver Visao.cs)

	/// <summary>
	/// res:// do bitset da AGUA -- a terceira classe de celula (ver <see cref="ClasseDeAgua"/>).
	///
	/// TEM PADRAO DERIVADO DO <see cref="Colisao"/>, e isso e de proposito: o `.agua` pode ser
	/// gerado pelo comando `agua` do pipeline, que NAO reescreve o manifesto (ele existe justamente
	/// pra nao reconverter os sprites). Sem o padrao, gerar os arquivos e nao ver agua nenhuma em
	/// jogo seria o desfecho silencioso -- e o pior tipo de defeito deste projeto e o calado.
	/// Quando a conversao cheia rodar, o manifesto traz a chave e ela vence.
	/// </summary>
	public string Agua = "";
	public string Luzes = "";     // res:// das fontes de luz do cenario (fogueira, tocha, lava)

	/// <summary>res:// das PORTAS da zona -- elas nao sao tile, sao entidade (ver MapConverter.EhPorta).</summary>
	public string Portas = "";

	/// <summary>
	/// res:// das MAQUINAS que o mapa ja traz (banco, bancada, gravidade, labs).
	///
	/// Pelo mesmo motivo das portas: sao coisas com que se INTERAGE, e interacao nao cabe numa
	/// celula de tilemap. O servidor as registra como construcoes -- ver `ObjetosDoMapa`.
	/// </summary>
	public string Objetos = "";

	/// <summary>
	/// res:// das PASSAGENS -- as celulas que levam a OUTRO mapa (caverna, escada do Templo).
	///
	/// Pelo mesmo motivo das portas e das maquinas: o que importa nelas e o DESTINO, e destino nao
	/// cabe num tile. Ver <see cref="Passagem"/>.
	/// </summary>
	public string PassagensArq = "";

	/// <summary>As passagens ja lidas. Carregadas junto com a colisao, no boot do servidor.</summary>
	public List<Passagem> Passagens = [];

	/// <summary>
	/// O caminho do `.agua`: o que o manifesto disser, ou o `.col` com a extensao trocada.
	/// Ver <see cref="Agua"/>.
	/// </summary>
	public string CaminhoDaAgua =>
		Agua.Length > 0 ? Agua
		: Colisao.EndsWith(".col", StringComparison.Ordinal) ? Colisao[..^4] + ".agua"
		: "";

	public int W, H;
	public ZoneCollision? Mapa;   // carregado sob demanda

	/// <summary>
	/// O BITSET DO QUE **CEGA** desta zona, ja lido. Nulo = zona sem `.vis` (as geradas por semente,
	/// os interiores de nave, a dimensao mental).
	///
	/// ============================ POR QUE O SERVIDOR PASSOU A CARREGAR ISTO ============================
	/// Por anos so o cliente leu o `.vis` -- ele desenha a sombra, e o servidor nao desenha nada. O
	/// campo <see cref="Visao"/> (o caminho `res://`) era parseado do manifesto e **nunca usado por
	/// ninguem**: API orfa, o mesmo padrao do sigilo do BP.
	///
	/// A VOZ LOCAL o acordou. "Ha parede entre estas duas pessoas?" e uma pergunta de SERVIDOR (e ele
	/// quem decide o que chega em quem), e a resposta certa tem que ser a MESMA que a vista da -- senao
	/// a voz e o olho discordam sobre o que e parede, e o jogador ouve alguem que ele nao ve com uma
	/// abafada que nao bate com nada na tela.
	///
	/// **E O `.vis` E NAO O `.col`, e a diferenca e o sistema inteiro**: porta CEGA e nao bloqueia;
	/// a beirada do mapa BLOQUEIA e nao cega. Usar o `.col` aqui daria a resposta errada nos dois
	/// casos que mais aparecem em jogo.
	/// ================================================================================================
	/// </summary>
	public ZoneCollision? Vista;
}

/// <summary>
/// Catalogo das zonas pre-feitas. Vem do manifest.json que o Tools/AssetPipeline gera junto
/// com as cenas, entao adicionar um planeta e reconverter, nao editar codigo.
///
/// O parser e escrito a mao porque o Core nao tem dependencia externa e o formato e conhecido
/// (uma lista de objetos plana, gerada por nos mesmos).
/// </summary>
public sealed class ZoneCatalog
{
	private readonly Dictionary<string, ZoneEntry> _porNome = new(StringComparer.OrdinalIgnoreCase);

	public IEnumerable<ZoneEntry> Todas => _porNome.Values;

	public ZoneEntry? Get(string zona) => _porNome.GetValueOrDefault(zona);
	public ZoneEntry? Get(ZoneKey k) => Get(k.Name);

	public static ZoneCatalog Parse(string json)
	{
		var cat = new ZoneCatalog();
		foreach (string bloco in Blocos(json))
		{
			var e = new ZoneEntry
			{
				Zona = Str(bloco, "zona"),
				Z = (int)Num(bloco, "z"),
				Cena = Str(bloco, "cena"),
				Pedacos = Str(bloco, "pedacos"),
				Colisao = Str(bloco, "colisao"),
				Visao = Str(bloco, "visao"),
				Agua = Str(bloco, "agua"),
				Luzes = Str(bloco, "luzes"),
				Portas = Str(bloco, "portas"),
				Objetos = Str(bloco, "objetos"),
				PassagensArq = Str(bloco, "passagens"),
				W = (int)Num(bloco, "w"),
				H = (int)Num(bloco, "h"),
			};
			// z01_Earth e z27_Earth podem coexistir: fica o de menor z (o canonico)
			if (e.Zona.Length > 0 && (!cat._porNome.TryGetValue(e.Zona, out ZoneEntry? antigo) || e.Z < antigo.Z))
				cat._porNome[e.Zona] = e;
		}
		return cat;
	}

	private static IEnumerable<string> Blocos(string s)
	{
		int i = 0;
		while (true)
		{
			int a = s.IndexOf('{', i);
			if (a < 0) yield break;
			int b = s.IndexOf('}', a);
			if (b < 0) yield break;
			yield return s[(a + 1)..b];
			i = b + 1;
		}
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

	private static double Num(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return 0;
		int dp = bloco.IndexOf(':', i);
		int fim = dp + 1;
		while (fim < bloco.Length && (char.IsDigit(bloco[fim]) || bloco[fim] is ' ' or '-' or '.')) fim++;
		return double.TryParse(bloco[(dp + 1)..fim].Trim(),
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
	}
}
