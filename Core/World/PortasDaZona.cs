namespace Jandirus.Core.World;

/// <summary>
/// Uma porta do mapa, do jeito que o `.portas` a descreve: onde ela esta e qual folha a desenha.
///
/// A porta NAO E TILE. No BYOND ela e um turf que abre no `Enter()` e fecha sozinha cinco segundos
/// depois -- densidade e opacidade que mudam em runtime. Tile do Godot nao faz isso, e pior: a
/// cena da zona fica cacheada entre visitas, entao qualquer coisa escrita nela sobrevive a saida
/// do planeta. Por isso o conversor de mapa NAO pinta a celula da porta e escreve esta lista no
/// lugar (ver `MapConverter.EhPorta`).
/// </summary>
public readonly record struct PortaDoMapa(int X, int Y, string Arte);

/// <summary>
/// Le o `zXX.portas` que o Tools/AssetPipeline gera ao lado do `.col` e do `.vis`.
///
/// Fica no Core porque as DUAS pontas precisam da mesma lista: o servidor pra saber onde estao as
/// portas (e decidir quem abre qual), o cliente pra instanciar os nodes que as desenham.
/// </summary>
public static class PortasDaZona
{
	/// <summary>
	/// Cinco segundos ate fechar sozinha -- o `spawn(50)` do `Open()` (Doors.dm:61), em decimos.
	/// </summary>
	public const double SegundosAberta = 5.0;

	/// <summary>Folga da caixa dos pes contra a celula. Cobre o passo que a colisao come.</summary>
	public const float Margem = 6f;

	/// <summary>
	/// O PROXIMO PASSO DESTE CORPO CAI NA CELULA DA PORTA? -- ou seja, o `Enter()` do BYOND.
	///
	/// ============================ POR QUE NAO E "ESTAR PERTO" ============================
	/// A primeira versao disto perguntava so distancia, e o banco de prova (`portas`) mostrou na
	/// primeira rodada que ela NUNCA abriria: a unica posicao que satisfazia era DENTRO da celula,
	/// e a porta BLOQUEIA -- ninguem chega la. Afrouxar a margem ate encostar tambem estava errado
	/// pelo outro lado: passar rente a uma parede abriria todas as portas dela de enfiada, e um
	/// corredor do Ceu tem vinte e uma.
	///
	/// O original nao pergunta distancia nenhuma. O `Enter(mob/M)` do BYOND dispara quando o passo
	/// tem a celula da porta como DESTINO -- e por isso empurrar contra a porta abre, e ficar
	/// parado ao lado dela nao. Aqui e a mesma pergunta: projeta-se o corpo um TILE a frente (o
	/// tamanho do passo no BYOND) na direcao pra qual ele olha, e ve-se se ele cairia na celula.
	/// =====================================================================================
	///
	/// Mora no Core, e nao no servidor, pelo mesmo motivo que o <see cref="MoveRules"/>: e uma
	/// REGRA DO JOGO, e regra com duas implementacoes e regra que diverge.
	/// </summary>
	public static bool VaiEntrar(Vec2 pos, Facing olhar, int cx, int cy)
	{
		const int T = ZoneCollision.TileSize;
		Vec2 passo = olhar switch
		{
			Facing.North => new Vec2(0, -T),
			Facing.South => new Vec2(0, T),
			Facing.East => new Vec2(T, 0),
			_ => new Vec2(-T, 0),
		};
		return Encosta(pos + passo, cx, cy) || Encosta(pos, cx, cy);
	}

	/// <summary>A caixa dos pes de um corpo em <paramref name="pos"/> toca a celula?</summary>
	private static bool Encosta(Vec2 pos, int cx, int cy)
	{
		float px0 = pos.X - MoveRules.BodyHalfW - Margem;
		float px1 = pos.X + MoveRules.BodyHalfW + Margem;
		float py0 = pos.Y + MoveRules.FeetOffsetY - MoveRules.BodyHalfH - Margem;
		float py1 = pos.Y + MoveRules.FeetOffsetY + MoveRules.BodyHalfH + Margem;

		const int T = ZoneCollision.TileSize;
		float tx0 = cx * T, ty0 = cy * T;
		return px1 >= tx0 && px0 <= tx0 + T && py1 >= ty0 && py0 <= ty0 + T;
	}

	public static List<PortaDoMapa> Parse(string json)
	{
		var lista = new List<PortaDoMapa>();
		if (string.IsNullOrWhiteSpace(json)) return lista;

		int i = 0;
		while (true)
		{
			int a = json.IndexOf('{', i);
			if (a < 0) break;
			int b = json.IndexOf('}', a);
			if (b < 0) break;

			string bloco = json[(a + 1)..b];
			string arte = Str(bloco, "arte");
			if (arte.Length > 0) lista.Add(new PortaDoMapa(Int(bloco, "x"), Int(bloco, "y"), arte));
			i = b + 1;
		}
		return lista;
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
