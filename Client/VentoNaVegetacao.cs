using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ O VENTO NA VEGETACAO, NA TELA (o pedido 226, 2026-11-02) ============================
/// O dono: *"vegetaçao dos planetas poderiam balançar (baseando-se no clima) pra simular vento, mas seria a parte superior deles pq se
/// balancar a arvore toda o tronco vai balancar e ficar restranho, entao mire mais na parte das folhas mesma ideia q o terraria usa"*.
///
/// UM MATERIAL POR QUADRO DE VEGETACAO, NO TILESET COMPARTILHADO. A arvore e um tile da camada de objetos (o pre-feito) ou da cobertura
/// (o mundo sorteado), com tudo o mais que mora nessas camadas -- um material na CAMADA balancaria a casa e a maquina junto. No tile
/// (`TileData.Material`) ele vale so pra arvore, em todo planeta, sem nenhuma camada saber. E cada quadro tem o SEU, porque cada um tem o
/// pe da copa numa altura (`Vento.MedirCopa`, lido na arte uma vez por processo).
///
/// O TILESET MUDA UMA VEZ SO, na entrada (`World._Ready`, antes de qualquer zona), e muda igual pra todo lugar: nao e o tileset que
/// "depende de onde o jogador esteve" que o `PlanetaProcedural` evita. Quem o segura vivo e o `World._tilesetVivo`.
///
/// A FORCA VEM DO CLIMA: o `ClimaNaTela.Aplicar` chama <see cref="Soprar"/> a cada quadro com o `Vento.DoClima`, e os materiais so sao
/// reescritos quando o numero muda.
/// ================================================================================================================
/// </summary>
public static class VentoNaVegetacao
{
	public const string CaminhoDoShader = "res://Assets/Shaders/VentoNaCopa.gdshader";

	public const string UniQuadros = "quadros", UniTopo = "topo", UniPivo = "pivo", UniPorte = "porte", UniAmplitude = "amplitude",
						UniEmpurrao = "empurrao";

	private static readonly List<ShaderMaterial> _materiais = [];
	private static TileSet? _preparado;
	private static double _ventoPosto = double.NaN;
	private static bool _paradoPosto;

	/// <summary>DEFEITO INJETADO (robo `--diagvento`): a vegetacao nao balanca -- o jogo de antes. Falso em jogo, sempre.</summary>
	public static bool ParadoDeTeste;

	/// <summary>
	/// OS OUTROS ROBOS RODAM SEM VENTO. Eles comparam fotos de momentos diferentes -- a copa da arvore que voltou na `--diaglenha` tem de
	/// ser 97% igual a de antes, e ha os que medem deslocamento na tela --, e uma arvore balancando no fundo seria uma diferenca que nao e
	/// deles. So o robo do vento (e o jogo) ve o vento.
	/// </summary>
	private static readonly bool _semVentoNaBancada = SemVentoNaBancada(OS.GetCmdlineArgs());

	/// <summary>A linha de comando e de um robo de tela que nao e o do vento?</summary>
	public static bool SemVentoNaBancada(string[] args) =>
		args.Any(a => (a.StartsWith("--diag", StringComparison.Ordinal) || a.StartsWith("--foto", StringComparison.Ordinal))
					  && a != "--diagvento");

	/// <summary>O vento (0 a 1) que esta nas copas agora. Pro robo.</summary>
	public static double VentoAgora => double.IsNaN(_ventoPosto) ? 0 : _ventoPosto;

	/// <summary>Quantos quadros de vegetacao ganharam o material. Pro robo.</summary>
	public static int QuadrosComVento => _materiais.Count;

	/// <summary>
	/// ESTA FOLHA E VEGETACAO? As arvores e plantas do jogo moram em `Sprites/Trees` (e umas poucas em `Sprites/Images/trees`); la tambem
	/// ha uma pedra (`bigrocktest`) e um cogumelo, que nao balancam.
	/// </summary>
	public static bool EhVegetacao(string caminho) =>
		(caminho.Contains("/Trees/", StringComparison.Ordinal) || caminho.Contains("/trees/", StringComparison.Ordinal))
		&& !caminho.Contains("rock", StringComparison.OrdinalIgnoreCase)
		&& !caminho.Contains("mushroom", StringComparison.OrdinalIgnoreCase);

	/// <summary>Poe o material do vento em todo quadro de vegetacao deste tileset. Uma vez por tileset; chamar de novo nao faz nada.</summary>
	public static void Preparar(TileSet? ts)
	{
		if (ts == null || ReferenceEquals(ts, _preparado)) return;
		if (ResourceLoader.Load<Shader>(CaminhoDoShader) is not { } shader)
		{
			GD.PushWarning($"[vento] sem '{CaminhoDoShader}': a vegetacao fica parada");
			return;
		}
		_preparado = ts;
		_materiais.Clear();

		for (int i = 0; i < ts.GetSourceCount(); i++)
		{
			int id = ts.GetSourceId(i);
			if (ts.GetSource(id) is not TileSetAtlasSource atlas || atlas.Texture is not { } tex || !EhVegetacao(tex.ResourcePath)) continue;

			Vector2I regiao = atlas.TextureRegionSize;
			var quadros = new Vector2(tex.GetWidth() / (float)regiao.X, tex.GetHeight() / (float)regiao.Y);
			(byte[] Px, int L, int A)? arte = Arte(tex);

			for (int t = 0; t < atlas.GetTilesCount(); t++)
			{
				Vector2I q = atlas.GetTileId(t);
				if (atlas.GetTileData(q, 0) is not { } dado) continue;

				// O PE DA COPA DESTE QUADRO. Sem a arte (textura que nao se le), a arvore inteira verga pela raiz -- melhor que nada parado.
				(int Topo, int Pivo, int Base)? copa = arte is { } a
					? Vento.MedirCopa((x, y) => Opaco(a, q.X * regiao.X + x, q.Y * regiao.Y + y), regiao.X, regiao.Y)
					: null;
				if (copa is not { } c) c = (0, regiao.Y - 1, regiao.Y - 1);

				var m = new ShaderMaterial { Shader = shader };
				m.SetShaderParameter(UniQuadros, quadros);
				m.SetShaderParameter(UniTopo, (c.Topo + 0.5f) / regiao.Y);
				m.SetShaderParameter(UniPivo, (c.Pivo + 0.5f) / regiao.Y);
				m.SetShaderParameter(UniPorte, (float)Vento.Porte(c.Pivo - c.Topo));
				dado.Material = m;
				_materiais.Add(m);
			}
		}

		_ventoPosto = double.NaN;
		Soprar(Vento.Brisa);
		GD.Print($"[vento] {_materiais.Count} quadro(s) de vegetacao com o vento na copa");
	}

	/// <summary>Os pixels RGBA8 da folha, lidos uma vez -- `GetPixel` um a um seria uma chamada por pixel, e sao centenas de milhares.</summary>
	private static (byte[] Px, int L, int A)? Arte(Texture2D tex)
	{
		if (tex.GetImage() is not { } img || img.IsEmpty()) return null;
		if (img.IsCompressed() && img.Decompress() != Error.Ok) return null;
		if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
		return (img.GetData(), img.GetWidth(), img.GetHeight());
	}

	private static bool Opaco((byte[] Px, int L, int A) a, int x, int y) =>
		x < a.L && y < a.A && a.Px[(y * a.L + x) * 4 + 3] > Vento.AlfaOpaco;

	/// <summary>
	/// O VENTO DE AGORA (0 a 1) NAS COPAS. Chamado a cada quadro pelo `ClimaNaTela`; so reescreve os materiais quando o numero muda.
	/// </summary>
	public static void Soprar(double vento)
	{
		bool parado = ParadoDeTeste || _semVentoNaBancada;
		if (!double.IsNaN(_ventoPosto) && Math.Abs(vento - _ventoPosto) < 0.005 && _paradoPosto == parado) return;
		_ventoPosto = vento;
		_paradoPosto = parado;
		float amp = parado ? 0f : (float)Vento.AmplitudePx(vento);
		float emp = parado ? 0f : (float)Vento.EmpurraoPx(vento);
		foreach (ShaderMaterial m in _materiais)
		{
			m.SetShaderParameter(UniAmplitude, amp);
			m.SetShaderParameter(UniEmpurrao, emp);
		}
	}
}
