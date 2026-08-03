using Jandirus.Core.Stats;

namespace Jandirus.Core.Combat;

/// <summary>Um estilo de luta: os "pontos" que o DM declara, mais o nome e o custo.</summary>
public sealed class Estilo
{
	public string Id = "";
	public string Nome = "";
	public string Desc = "";
	public double Custo;      // learncost, em marcos
	public double Pontos;     // allocatedpoints: a soma bruta, usada pela maestria
	public Dictionary<string, double> Defaults = new(StringComparer.Ordinal);
}

/// <summary>
/// ESTILOS DE LUTA -- o multiplicador que se escolhe, e o unico canal de poder que nao vem nem de
/// treino nem de raca.
///
/// O SOQUETE JA EXISTIA E ESTAVA VAZIO. Os dez campos `*Style` do <see cref="Fighter"/> ja
/// multiplicavam a cadeia inteira em <c>Fighter.Statify</c> -- ataque, defesa, tecnica, Ki,
/// velocidade, folego e regeneracao -- e nenhum deles era escrito por ninguem: todos parados em 1.
/// Portar estilo foi encher o soquete, nao construi-lo.
///
/// ================== A ARMADILHA QUE DEFINE ESTE ARQUIVO ==================
/// Os estilos do DM declaram SO os `default*`, e eles vao de 1 a 6. NAO SAO MULTIPLICADORES.
/// O multiplicador real nasce em `UpdateStyle()` (Style.dm:78-86):
///
///     mult = max(1, 1 + (default - 1) * mudanca)
///
/// Um `defaultphysoff = 6` do Saiyan Style e 1 + 5*0,02 = <b>1,10x</b>, nao 6x. Ler o numero cru
/// como multiplicador daria um estilo que sextuplica o ataque -- e como e um numero plausivel de
/// se ver num arquivo de configuracao, ninguem desconfiaria ate o jogo estar quebrado.
/// =========================================================================
///
/// TRES TAXAS DE MUDANCA, e a divisao e do original: o que e corpo anda o dobro do que e cabeca.
/// </summary>
public static class Estilos
{
	/// <summary>physoff, physdef, kioff, kidef, speed -- o corpo anda o dobro.</summary>
	public const double MudancaFisica = 0.02;

	/// <summary>technique, kiskill.</summary>
	public const double MudancaMental = 0.01;

	/// <summary>kiregen, staminamod.</summary>
	public const double MudancaAbstrata = 0.01;

	/// <summary>
	/// Que taxa cada stat usa, e em que campo do lutador ele aterrissa. Copiado linha a linha de
	/// `Style.dm:78-86` -- inclusive os nomes torto do DM: e `defaultechnique` (sem o "t") e
	/// `defaultsstaminamod` (com dois "s"). Nao sao erros de transcricao.
	/// </summary>
	private static readonly (string Default, string Campo, double Mudanca)[] Mapa =
	[
		("defaultphysoff", "physoffStyle", MudancaFisica),
		("defaultphysdef", "physdefStyle", MudancaFisica),
		("defaultechnique", "techniqueStyle", MudancaMental),
		("defaultkioff", "kioffStyle", MudancaFisica),
		("defaultkidef", "kidefStyle", MudancaFisica),
		("defaultkiskill", "kiskillStyle", MudancaMental),
		("defaultspeed", "speedStyle", MudancaFisica),
		("defaultkiregen", "kiregenStyle", MudancaAbstrata),
		("defaultsstaminamod", "staminadrainStyle", MudancaAbstrata),
	];

	private static readonly Dictionary<string, Estilo> Tudo = new(StringComparer.OrdinalIgnoreCase);

	public static int Total => Tudo.Count;
	public static IEnumerable<Estilo> Todos => Tudo.Values;
	public static Estilo? Get(string id) => id.Length == 0 ? null : Tudo.GetValueOrDefault(id);

	/// <summary>
	/// OS MULTIPLICADORES DE VERDADE: campo do lutador -> quanto multiplicar.
	///
	/// `magiStyle` NAO SAI DAQUI, e isso e fiel: no original nenhum estilo declara `defaultmagi`,
	/// `UpdateStyle()` nao toca em `magi` e a linha que o resetaria esta comentada (Style.dm:124).
	/// Ele fica em 1 pra sempre -- mas continua sendo LIDO na cadeia de stats. E um campo vivo que
	/// nunca muda, e inventar um valor pra ele aqui seria acrescentar poder que o jogo nao tem.
	/// </summary>
	public static Dictionary<string, double> Multiplicadores(Estilo e)
	{
		var m = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach ((string dflt, string campo, double mud) in Mapa)
		{
			double pontos = e.Defaults.GetValueOrDefault(dflt, 1);
			m[campo] = Math.Max(1, 1 + (pontos - 1) * mud);
		}

		// `staminagain = 1/staminamod` (Style.dm:67). No DM isso e calculado no TOPO do
		// UpdateStyle, com o valor do tique ANTERIOR -- fica um tique atrasado de graca. Aqui sai
		// do valor final, que e o que o proprio codigo pretendia.
		// (O `staminagainStyle` do DM e escrito e nunca lido; nao existe aqui de proposito.)
		return m;
	}

	// =====================================================================
	// MAESTRIA
	// =====================================================================
	/// <summary>De quantos em quantos segundos a maestria rende (`sleep(15)` x 100 = 150 s).</summary>
	public const double SegundosPorGanho = 150;

	/// <summary>O teto duro do original.</summary>
	public const double MaestriaMaxima = 100;

	/// <summary>
	/// O TETO PESSOAL do estilo: `max(cap, min(5 + alloc + round(alloc + alloc/40.5), 100))`.
	/// So sobe. Um estilo de 8 pontos chega a um teto de ~21; e o que impede um estilo barato de
	/// valer tanto quanto um caro.
	/// </summary>
	public static double TetoDe(double tetoAtual, double pontos)
		=> Math.Max(tetoAtual, Math.Min(5 + pontos + Math.Floor(pontos + pontos / 40.5), MaestriaMaxima));

	/// <summary>
	/// QUANTO A MAESTRIA SOBE num ganho: `(pontos+1)/teto`, mais `(pontos+3)/teto` treinando e
	/// `(pontos+4)/teto` lutando -- e os tres SOMAM. Treinar e lutar ao mesmo tempo nao acontece,
	/// mas lutar treinando o estilo e o caminho rapido, e isso e intencional no original.
	/// </summary>
	public static double GanhoDeMaestria(double pontos, double teto, bool treinando, bool lutando)
	{
		if (teto <= 0) return 0;
		double g = (pontos + 1) / teto;
		if (treinando) g += (pontos + 3) / teto;
		if (lutando) g += (pontos + 4) / teto;
		return g;
	}

	/// <summary>Os estilos parados enferrujam: 4% de perda, com 10% de chance por ganho.</summary>
	public const double ChanceDeEnferrujar = 0.10;
	public const double PerdaAoEnferrujar = 1.0 / 25.0;

	/// <summary>
	/// O ESTILO VIRANDO DANO. E o `compareStyles()` do original, somado como dano PLANO em
	/// `calcs.dm:87` -- teto de `sqrt(100)` = 10 pontos de dano.
	///
	/// A conta e uma disputa de maestria: a diferenca entre a sua e a dele vira CHANCE, e so se a
	/// chance passar e que o bonus sai. Lutar contra alguem do MESMO estilo da +15 de chance
	/// (voce conhece os truques). Se a chance for negativa, ainda ha 60%+chance de ela ser
	/// resgatada pela sua tecnica; senao zera.
	///
	/// DIFERENCA CONSCIENTE: o original compara os estilos pelo NOME, e o jogador pode renomear o
	/// proprio estilo a vontade -- o que torna o +15 trivialmente manipulavel. Aqui a comparacao e
	/// pelo ID do estilo, que ninguem edita. E o mesmo bonus pela mesma razao de jogo, sem o
	/// buraco.
	/// </summary>
	public static double DanoDeEstilo(string meuId, double minhaMaestria,
									  string idDele, double maestriaDele,
									  double minhaEtechnique, Func<double> sorte)
	{
		if (meuId.Length == 0) return 0;
		if (idDele.Length == 0) return Math.Sqrt(Math.Max(minhaMaestria, 0));

		double chance = minhaMaestria - maestriaDele;
		if (string.Equals(meuId, idDele, StringComparison.OrdinalIgnoreCase)) chance += 15;

		if (chance < 0)
		{
			if (sorte() * 100 < 60 + chance) chance += 10 * minhaEtechnique;
			else chance = 0;
		}
		else chance += 10 * minhaEtechnique;

		return sorte() * 100 < chance ? Math.Sqrt(Math.Max(minhaMaestria, 0)) : 0;
	}

	// =====================================================================
	// LEITURA
	// =====================================================================
	public static void Carregar(string json)
	{
		Tudo.Clear();
		foreach (string bloco in Blocos(json))
		{
			var e = new Estilo
			{
				Id = Str(bloco, "id"),
				Nome = Str(bloco, "nome"),
				Desc = Str(bloco, "desc"),
				Custo = Num(bloco, "custo"),
				Pontos = Num(bloco, "pontos"),
			};
			foreach (string item in Lista(bloco, "defaults"))
			{
				int ig = item.IndexOf('=');
				if (ig <= 0) continue;
				if (double.TryParse(item[(ig + 1)..], System.Globalization.NumberStyles.Float,
						System.Globalization.CultureInfo.InvariantCulture, out double v))
					e.Defaults[item[..ig]] = v;
			}
			if (e.Id.Length > 0) Tudo[e.Id] = e;
		}
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
