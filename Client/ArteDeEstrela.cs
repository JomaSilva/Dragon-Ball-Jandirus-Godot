using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ONDE A ARTE DE CADA ESTRELA MORA, E QUANTO DELA E A ESTRELA.
///
/// ============================ A FRONTEIRA QUE ESTA CLASSE E ============================
/// O `Core` decide QUAL estrela e (<see cref="ClasseDeEstrela"/>) e qual e o raio que MATA
/// (`Sistemas.RaioDaClasse`). Ele nao pode saber onde o `res://` fica -- e regra 0.1 da
/// especificacao, e e o mesmo corte que `Client.ColadasDeForma.CaminhoDa` ja faz pras formas.
/// Esta classe e o outro lado desse corte: recebe um simbolo, devolve pixel.
/// ======================================================================================
///
/// ============================ O NUMERO QUE FAZ O DESENHO NAO MENTIR ============================
/// Uma folha de estrela NAO e um disco que encosta na borda do quadro: o nucleo opaco ocupa uma
/// fracao do meio-lado e o resto e coroa e halo. Copiar o `Scale = Raio*2/lado` que o
/// `PlanetaDesenhado` usa (onde o disco realmente encosta) desenharia o nucleo MENOR do que o raio
/// que mata.
///
/// A fracao foi MEDIDA folha por folha (`dotnet run --project Tools/AssetPipeline -- estrelas`) e
/// nao e uma so: vai de **0,569 na ana vermelha a 0,698 na gigante vermelha**. Uma constante unica
/// erraria em ate 21% -- e o erro tem lado: com 0,57 aplicado a uma gigante, o nucleo desenhado
/// transborda o raio letal e o jogador acha que ja morreu antes de morrer; com 0,70 aplicado a uma
/// ana, ele morre com a estrela ainda visivelmente longe.
///
/// (O aviso herdado da Fase 2 dizia "0,57 em quase todas e **0,78 na amarela**". A medida nao
/// confirma: a amarela mede 0,569-0,577, igualzinho a vermelha e a laranja, e quem sobe sao as tres
/// GIGANTES. O numero de 0,78 nao existe em nenhuma das 32 folhas.)
/// ===========================================================================================
///
/// AS DUAS CORES, e por que a carta usa a de fora: o nucleo e branco-quente em quase toda familia
/// (a ana vermelha mede `f4bc92` no miolo, um pessego palido), entao pintar o pontinho da carta com
/// a cor do NUCLEO faria oito classes virarem tres tons de creme. Quem da cor a estrela nesta arte
/// e a coroa -- `ff3c03` na mesma ana. Ver o cabecalho de `Tools/AssetPipeline/EstrelasArte.cs`.
/// </summary>
public static class ArteDeEstrela
{
	private const string Arquivo = "res://Assets/Data/estrelas.json";

	private readonly record struct Folha(string Caminho, float Nucleo, float Brilho, Color Cor, Color Halo);

	/// <summary>Indexado por `classe * 4 + variante`. Nulo enquanto o arquivo nao foi lido.</summary>
	private static Folha[]? _folhas;
	private static readonly Dictionary<string, Texture2D> _texturas = [];

	/// <summary>
	/// A TABELA, LIDA UMA VEZ.
	///
	/// FALHA ALTO e nao em silencio (regra 0.5): sem o arquivo, o jogo desenharia estrelas sem
	/// nucleo e sem cor e ninguem saberia por que. O aviso diz o comando que conserta.
	/// </summary>
	private static Folha[] Tabela()
	{
		if (_folhas != null) return _folhas;

		var lidas = new Folha[EstrelasPorClasse * 8];
		if (!Godot.FileAccess.FileExists(Arquivo))
		{
			GD.PushWarning("[estrelas] sem estrelas.json -- rode: "
						   + "dotnet run --project Tools/AssetPipeline -- estrelas .");
			return _folhas = lidas;
		}

		var json = Json.ParseString(Godot.FileAccess.GetFileAsString(Arquivo));
		if (json.VariantType != Variant.Type.Dictionary)
		{
			GD.PushWarning("[estrelas] estrelas.json nao e um objeto -- a tela do sistema fica sem arte");
			return _folhas = lidas;
		}

		foreach (Variant v in (Godot.Collections.Array)((Godot.Collections.Dictionary)json)["folhas"])
		{
			var d = (Godot.Collections.Dictionary)v;
			if (!Enum.TryParse((string)d["classe"], out ClasseDeEstrela c)) continue;

			int variante = Math.Clamp((int)d["variante"], 0, EstrelasPorClasse - 1);
			lidas[(int)c * EstrelasPorClasse + variante] = new Folha(
				(string)d["arquivo"],
				(float)(double)d["nucleo"],
				(float)(double)d["brilho"],
				new Color((string)d["cor"]),
				new Color((string)d["halo"]));
		}

		return _folhas = lidas;
	}

	/// <summary>Quantas folhas numeradas cada classe tem. E o mesmo 4 que `Estrela.Variante` sorteia.</summary>
	public const int EstrelasPorClasse = 4;

	private static Folha Da(Estrela e) =>
		Tabela()[(int)e.Classe * EstrelasPorClasse + Math.Clamp(e.Variante, 0, EstrelasPorClasse - 1)];

	/// <summary>
	/// A textura da estrela, carregada na primeira vez que alguem olha pra ela.
	///
	/// PREGUICOSA de proposito: sao 32 folhas de 1 MB cada, e uma sessao inteira mostra um punhado.
	/// Carregar as 32 no boot seria 32 MB de textura pra desenhar uma.
	/// </summary>
	public static Texture2D? Textura(Estrela e)
	{
		string caminho = Da(e).Caminho;
		if (caminho.Length == 0) return null;
		if (_texturas.TryGetValue(caminho, out Texture2D? pronta)) return pronta;

		var t = ResourceLoader.Load<Texture2D>(caminho);
		if (t == null) GD.PushWarning($"[estrelas] {caminho} nao carregou -- falta importar?");
		else _texturas[caminho] = t;
		return t;
	}

	/// <summary>
	/// O LADO EM QUE A FOLHA TEM QUE SER DESENHADA pra o nucleo dela casar com <paramref name="raioNaTela"/>.
	///
	/// E a unica conta que importa neste arquivo: a folha transborda o nucleo, entao ela e desenhada
	/// MAIOR que o corpo. Ver o cabecalho.
	/// </summary>
	public static float LadoDaFolha(Estrela e, float raioNaTela)
	{
		float nucleo = Da(e).Nucleo;
		// Sem tabela lida, a folha nao existe e o desenho cai no circulo -- devolver 0 aqui e o que
		// deixa quem chama escolher, em vez de dividir por zero.
		return nucleo <= 0 ? 0 : raioNaTela * 2f / nucleo;
	}

	/// <summary>A cor da COROA. E com ela que a carta pinta o pontinho -- ver o cabecalho.</summary>
	public static Color Halo(Estrela e)
	{
		Color c = Da(e).Halo;
		return c.A == 0 ? Tema.Destaque : c;
	}

	/// <summary>
	/// O NOME DA CLASSE EM PORTUGUES, pra legenda e pro painel.
	///
	/// Aqui e nao no `Core`: o `ClasseDeEstrela` e um simbolo, e texto de tela e coisa de tela.
	/// </summary>
	public static string NomeDaClasse(ClasseDeEstrela c) => c switch
	{
		ClasseDeEstrela.AnaVermelha => "ana vermelha",
		ClasseDeEstrela.Laranja => "laranja",
		ClasseDeEstrela.Amarela => "amarela",
		ClasseDeEstrela.Branca => "branca",
		ClasseDeEstrela.Azul => "azul",
		ClasseDeEstrela.GiganteBranca => "gigante branca",
		ClasseDeEstrela.GiganteVermelha => "gigante vermelha",
		_ => "gigante azul",
	};

	/// <summary>Quantas folhas a tabela tem de verdade. SO PRA BANCADA.</summary>
	public static int FolhasLidas() => Tabela().Count(f => f.Caminho.Length > 0);

	/// <summary>A fracao medida do nucleo. SO PRA BANCADA -- e ela que prova que a medida chegou.</summary>
	public static float NucleoDeTeste(Estrela e) => Da(e).Nucleo;
}
