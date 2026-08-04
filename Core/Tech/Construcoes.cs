using Jandirus.Core.Stats;

namespace Jandirus.Core.Tech;

/// <summary>Uma coisa que da pra construir no mundo.</summary>
public sealed class Construcao
{
	public string Id = "";
	public string Nome = "";
	public string Desc = "";
	public double Custo;
	public double Tech;
	public string[] Racas = [];

	/// <summary>res:// do SpriteFrames do que E ERGUIDO. Vazio = o .dmi nao existe no port.</summary>
	public string Arte = "";

	/// <summary>O `icon_state` dele. Vazio = a animacao "default" da folha.</summary>
	public string Estado = "";

	/// <summary>
	/// BLOQUEIA PASSAGEM. Sai do `create_type` -- o objeto que vai pro CHAO --, e nao do item de
	/// loja: o Creatable de Research_Station tem `density = 0` e o `/obj/Technology/Research_Station`
	/// que ele cria tem `density = 1`. Ler o errado deixava a bancada construida atravessavel ao
	/// lado da mesma maquina, vinda do mapa, que bloqueia.
	/// </summary>
	public bool Densa;

	/// <summary>`pixel_x`/`pixel_y`: a Research Bench e 96 px de largura com `pixel_x = -32`.</summary>
	public double PixelX, PixelY;

	/// <summary>
	/// O TYPEPATH DO QUE VAI PRO CHAO (`create_type`): `/obj/Technology/Research_Station`.
	///
	/// E a chave que liga o CATALOGO ao MAPA. Os `.dmm` do original ja tem bancadas, bancos e
	/// mainframes espalhados, e ate agora eles viravam celula de tilemap -- desenho parado, sem
	/// interacao nenhuma, ao lado de uma copia construida por jogador que funcionava. Com o
	/// typepath aqui, o conversor reconhece a maquina no mapa e a transforma na MESMA coisa.
	/// </summary>
	public string Tipo = "";

	/// <summary>
	/// DA PRA CONSTRUIR ISTO, ou e so mobilia do mapa?
	///
	/// O banco e a unica coisa deste catalogo que ninguem ergue: ele nao e um `Creatable` do DM,
	/// e um `/obj/Bank` posto a mao nos mapas. Ele entra aqui mesmo assim porque precisa de tudo
	/// o que uma construcao tem -- arte, densidade, deslocamento, alcance de uso -- e duplicar
	/// isso num segundo catalogo seria manter duas verdades sobre a mesma maquina.
	///
	/// Custo negativo e a marca. Ver `Ofertas`, que o esconde da loja.
	/// </summary>
	public bool Construivel => Custo >= 0;
}

/// <summary>Por que uma construcao foi recusada. Tipado pelo mesmo motivo das skills.</summary>
public enum RecusaObra
{
	Pode,
	NaoExiste,
	SemTech,
	SemZeni,
	RacaErrada,
	LugarOcupado,
	ZonaProibida,
}

/// <summary>
/// O CATALOGO DE CONSTRUCOES, extraido dos `obj/Creatables` do DM (comando `tech` do pipeline).
/// 99 entradas com preco em zeni e requisito de tecnologia.
///
/// E A SEGUNDA VIA DE PODER DO JOGO, e por isso ela tem uma moeda propria: o Saiyajin sobe socando
/// pedra, o humano sobe ESTUDANDO, e o que o estudo compra sao coisas no mundo. As duas escalas
/// tem que ser lidas juntas -- o mainframe que transforma um humano em androide pede tech 50 e
/// meio milhao de zeni, que e o equivalente, em horas, a subir uma transformacao.
/// </summary>
public sealed class CatalogoDeObras
{
	private readonly Dictionary<string, Construcao> _porId = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Construcao> _porTipo = new(StringComparer.Ordinal);

	public int Total => _porId.Count;
	public IEnumerable<Construcao> Todas => _porId.Values;
	public Construcao? Get(string id) => _porId.GetValueOrDefault(id);

	/// <summary>
	/// A CONSTRUCAO CUJO `create_type` E ESTE TYPEPATH -- a ponte entre o catalogo e os `.dmm`.
	///
	/// E o que o conversor de mapa usa pra decidir que aquela celula nao e cenario: ela e uma
	/// maquina que o jogo ja sabe desenhar, bloquear e usar. Ver `MapConverter`.
	/// </summary>
	public Construcao? PorTypepath(string typepath) =>
		typepath.Length == 0 ? null : _porTipo.GetValueOrDefault(typepath);

	/// <summary>
	/// O QUE ESTE PERSONAGEM CONSEGUE COMPRAR AGORA, e o motivo de cada nao.
	///
	/// Devolve TUDO que a raca permite, inclusive o que nao cabe no bolso -- ver a lista inteira
	/// com "faltam 40 de tecnologia" ao lado e o que transforma tecnologia numa meta. Uma lista
	/// que so mostra o comprável esconde o jogo.
	/// </summary>
	public List<(Construcao Obra, RecusaObra Motivo)> Ofertas(Fighter f, string raca)
	{
		var l = new List<(Construcao, RecusaObra)>();
		foreach (Construcao c in _porId.Values.OrderBy(c2 => c2.Tech).ThenBy(c2 => c2.Custo))
		{
			// MOBILIA DE MAPA NAO E OFERTA. O banco existe no catalogo pra ter arte e densidade,
			// nao pra ser vendido -- ver `Construcao.Construivel`.
			if (!c.Construivel) continue;
			RecusaObra r = Permitida(c, f, raca);
			if (r == RecusaObra.RacaErrada) continue;   // nem existe pra voce: nao vira linha
			l.Add((c, r));
		}
		return l;
	}

	/// <summary>
	/// A CELULA QUE UMA CONSTRUCAO OCUPA.
	///
	/// UMA celula, mesmo quando o desenho tem tres tiles de largura -- e a mesma regra que o
	/// conversor de mapa aplica a arvore: no BYOND densidade e do OBJETO, e um objeto mora num
	/// tile. Usar a extensao do icone faria a bancada barrar o corredor inteiro.
	///
	/// A ANCORA SAO OS PES, nao o centro do corpo: a construcao nasce onde o jogador ESTAVA, e a
	/// posicao guardada e a do no dele (o meio do sprite). Sem o deslocamento, construir encostado
	/// na parede de baixo poria a maquina uma celula acima de onde o jogador a viu nascer.
	///
	/// Mora no Core porque as DUAS pontas precisam da mesma conta: o servidor pra bloquear, o
	/// cliente pra desenhar. Se divergirem, a maquina aparece num lugar e barra noutro.
	/// </summary>
	public static (int X, int Y) Celula(double x, double y)
	{
		const int t = Jandirus.Core.World.ZoneCollision.TileSize;
		return ((int)Math.Floor(x / t),
				(int)Math.Floor((y + Jandirus.Core.World.MoveRules.FeetOffsetY) / t));
	}

	public static RecusaObra Permitida(Construcao c, Fighter f, string raca)
	{
		// NAO SE CONSTROI MOBILIA DE MAPA. A guarda mora aqui e nao so no `Ofertas` porque o
		// pedido de construir chega do CLIENTE, e esconder da lista nunca foi permissao.
		if (!c.Construivel) return RecusaObra.NaoExiste;
		if (c.Racas.Length > 0 && !c.Racas.Contains(raca, StringComparer.OrdinalIgnoreCase))
			return RecusaObra.RacaErrada;
		if (f.techskill < c.Tech) return RecusaObra.SemTech;
		if (f.Zeni < c.Custo) return RecusaObra.SemZeni;
		return RecusaObra.Pode;
	}

	public static CatalogoDeObras Parse(string json)
	{
		var cat = new CatalogoDeObras();
		foreach (string bloco in Blocos(json))
		{
			var c = new Construcao
			{
				Id = Str(bloco, "id"),
				Nome = Str(bloco, "nome"),
				Desc = Str(bloco, "desc"),
				Custo = Num(bloco, "custo"),
				Tech = Num(bloco, "tech"),
				Racas = Lista(bloco, "racas"),
				Arte = Str(bloco, "arte"),
				Estado = Str(bloco, "estado"),
				Densa = Num(bloco, "densa") > 0,
				PixelX = Num(bloco, "px"),
				PixelY = Num(bloco, "py"),
				Tipo = Str(bloco, "tipo"),
			};
			if (c.Id.Length == 0) continue;
			cat._porId[c.Id] = c;

			// A PRIMEIRA VENCE. Duas entradas podem apontar pro mesmo `create_type` (o Creatable e
			// uma variante dele); a ordem do arquivo e por tech e custo, entao a primeira e a mais
			// barata -- que e a que faz sentido um mapa ter espalhado.
			if (c.Tipo.Length > 0) cat._porTipo.TryAdd(c.Tipo, c);
		}
		return cat;
	}

	// ---- o mesmo leitor de uma pagina do catalogo de skills ----
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
		int a = bloco.IndexOf('"', bloco.IndexOf(':', i) + 1);
		if (a < 0) return "";
		var sb = new System.Text.StringBuilder();
		for (int k = a + 1; k < bloco.Length; k++)
		{
			if (bloco[k] == '\\' && k + 1 < bloco.Length) { sb.Append(bloco[++k]); continue; }
			if (bloco[k] == '"') break;
			sb.Append(bloco[k]);
		}
		return sb.ToString();
	}

	private static double Num(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return 0;
		int a = bloco.IndexOf(':', i) + 1;
		int b = a;
		while (b < bloco.Length && (char.IsDigit(bloco[b]) || bloco[b] is '.' or '-' or ' ')) b++;
		return double.TryParse(bloco[a..b].Trim(), System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
	}

	private static string[] Lista(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return [];
		int a = bloco.IndexOf('[', i);
		int b = bloco.IndexOf(']', a + 1);
		if (a < 0 || b < 0) return [];
		var l = new List<string>();
		string dentro = bloco[(a + 1)..b];
		int k = 0;
		while (true)
		{
			int q1 = dentro.IndexOf('"', k);
			if (q1 < 0) break;
			int q2 = dentro.IndexOf('"', q1 + 1);
			if (q2 < 0) break;
			l.Add(dentro[(q1 + 1)..q2]);
			k = q2 + 1;
		}
		return [.. l];
	}
}
