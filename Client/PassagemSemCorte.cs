using Godot;

namespace Jandirus.Client;

/// <summary>
/// (2026-11-01, o pedido 218) A PASSAGEM DO SUPER MAJIN SEM A METADE ROSA CORTADA -- o dono, vendo a divisao do Boo da saga: *"Parece q ta
/// cortado o sprite do fat com o evil"*.
///
/// ============================ O CORTE E DA ARTE, E O CONSERTO E NA TELA ============================
/// A `super_majin_transform` da `Majin - Form 2` (a passagem do Corrupted Majin jogador indo pra forma 3, e a da divisao do Boo) e um estado
/// de 32 px do DMI com DUAS figuras: o Majin cinza a direita e a metade rosa a esquerda -- e o desenhista fez a rosa ENTRAR pela borda do
/// quadro. O icone corta tudo o que passa da borda (no DM o `flick` dela nem tocava, ver `SagaDoMajin.PassagemPara`): aqui ela saia fatiada
/// por meio segundo, e a aura e a fumaca dela ficavam retas na borda depois.
///
/// Os quadros sao copiados num quadro de 64 px (16 de cada lado: centrado, o desenho fica onde estava) e tres trechos ganham o que faltava:
///   * 13-18, A ENTRADA: o corpo inteiro do quadro 19 desliza de fora, com a borda direita onde a do quadro esta;
///   * 39-52, A AURA: linha a linha, a espessura da aura do lado da frente repetida nas costas;
///   * 53-57, A FUMACA: as tres colunas da borda espelhadas pra fora.
/// Folha nova nao serve: imagem nova pede a importacao do editor, e o jogo sem importacao nao a carrega.
/// =================================================================================================================
/// </summary>
public static class PassagemSemCorte
{
	/// <summary>A unica passagem que este conserto conhece.</summary>
	public const string Animacao = "super_majin_transform";

	private const int Lado = 32, Pad = 16, Largura = Lado + 2 * Pad;

	/// <summary>As colunas da metade rosa: o Majin cinza comeca depois delas.</summary>
	private const int MetadeRosa = 16;

	/// <summary>Os trechos da arte (quadros da `super_majin_transform`, 99 ao todo).</summary>
	private const int EntradaDe = 13, EntradaAte = 18, Inteiro = 19, AuraDe = 39, AuraAte = 52, FumacaDe = 53, FumacaAte = 57;

	private static SpriteFrames? _pronta;
	private static bool _tentou;

	/// <summary>DEFEITO INJETADO (`--diagbuu`): a passagem volta a sair no quadro de 32 px, cortada. Falso em jogo, sempre.</summary>
	public static bool CortadaDeTeste;

	/// <summary>A folha a tocar: a alargada quando e a passagem do Super Majin, a propria em todo outro caso (e se a arte nao for a esperada).</summary>
	public static SpriteFrames Para(string folha, StringName anim, SpriteFrames original)
	{
		if (CortadaDeTeste || folha != Jandirus.Core.Races.SagaDoMajin.Corpos[1] || anim != Animacao) return original;
		if (!_tentou)
		{
			_tentou = true;
			_pronta = Montar(original, anim);
			if (_pronta == null) GD.PushWarning("[visual] a passagem do Super Majin nao alargou: a arte nao e a esperada; fica a de 32 px");
		}
		return _pronta ?? original;
	}

	private static SpriteFrames? Montar(SpriteFrames f, StringName anim)
	{
		int n = f.GetFrameCount(anim);
		if (n <= FumacaAte) return null;
		var q = new Image[n];
		for (int i = 0; i < n; i++)
		{
			if (f.GetFrameTexture(anim, i) is not { } t || t.GetImage() is not { } img) return null;
			if (img.IsCompressed()) img.Decompress();
			if (img.GetWidth() != Lado || img.GetHeight() != Lado) return null;
			img.Convert(Image.Format.Rgba8);
			q[i] = img;
		}

		var novos = new Image[n];
		for (int i = 0; i < n; i++)
		{
			novos[i] = Image.CreateEmpty(Largura, Lado, false, Image.Format.Rgba8);
			novos[i].BlitRect(q[i], new Rect2I(0, 0, Lado, Lado), new Vector2I(Pad, 0));
		}
		Entrada(q, novos);
		Aura(q, novos);
		Fumaca(q, novos);

		var sf = new SpriteFrames();
		if (sf.HasAnimation("default")) sf.RemoveAnimation("default");
		sf.AddAnimation(anim);
		sf.SetAnimationSpeed(anim, f.GetAnimationSpeed(anim));
		sf.SetAnimationLoopMode(anim, f.GetAnimationLoopMode(anim));
		for (int i = 0; i < n; i++) sf.AddFrame(anim, ImageTexture.CreateFromImage(novos[i]), f.GetFrameDuration(anim, i));
		return sf;
	}

	// ============================ OS TRES TRECHOS ============================
	private static void Entrada(Image[] q, Image[] novos)
	{
		if (BordaDireitaDoRosa(q[Inteiro]) is not int ri) return;
		int corte = Math.Min(ri + 2, Lado);   // o contorno preto vai uma coluna alem do rosa
		for (int k = EntradaDe; k <= EntradaAte; k++)
		{
			if (BordaDireitaDoRosa(q[k]) is not int r) continue;
			Image c = Image.CreateEmpty(Largura, Lado, false, Image.Format.Rgba8);
			c.BlitRect(q[k], new Rect2I(MetadeRosa, 0, Lado - MetadeRosa, Lado), new Vector2I(Pad + MetadeRosa, 0));   // o cinza, como esta
			c.BlendRect(q[Inteiro], new Rect2I(0, 0, corte, Lado), new Vector2I(Pad + (r - ri), 0));
			novos[k] = c;
		}
	}

	private static void Aura(Image[] q, Image[] novos)
	{
		for (int k = AuraDe; k <= AuraAte; k++)
		{
			Image o = q[k], c = novos[k];
			for (int y = 0; y < Lado; y++)
			{
				if (!Magenta(o.GetPixel(0, y))) continue;
				int fim = -1;
				for (int x = 0; x < MetadeRosa + 2; x++) if (o.GetPixel(x, y).A > 0) fim = x;
				int t = 0;
				while (fim - t >= 0 && Magenta(o.GetPixel(fim - t, y))) t++;
				int corpo = -1;
				for (int x = 0; x < MetadeRosa; x++)
				{
					Color p = o.GetPixel(x, y);
					if (p.A > 0 && !Magenta(p)) { corpo = x; break; }
				}
				if (corpo < 0) continue;
				Color cor = o.GetPixel(0, y);
				for (int x = Math.Max(corpo - t, -Pad); x < 0; x++) c.SetPixel(Pad + x, y, cor);
			}
		}
	}

	private static void Fumaca(Image[] q, Image[] novos)
	{
		for (int k = FumacaDe; k <= FumacaAte; k++)
		{
			Image o = q[k], c = novos[k];
			for (int y = 0; y < Lado; y++)
			{
				Color borda = o.GetPixel(0, y);
				if (!DeFumaca(borda) && !Preto(borda)) continue;
				for (int d = 1; d <= 3; d++)
				{
					Color p = o.GetPixel(d, y);
					if (p.A > 0 && (DeFumaca(p) || Preto(p) || p.A < 1f)) c.SetPixel(Pad - d, y, p);
				}
			}
		}
	}

	// ============================ AS CORES DA ARTE ============================
	private static int? BordaDireitaDoRosa(Image q)
	{
		int? max = null;
		for (int x = 0; x < MetadeRosa; x++)
			for (int y = 0; y < Lado; y++)
				if (Rosa(q.GetPixel(x, y))) max = x;
		return max;
	}

	private static bool Rosa(Color p) => p.A > 0 && p.R > 180f / 255 && p.R > p.G + 40f / 255 && p.B > 100f / 255;
	private static bool Magenta(Color p) => p.A > 0 && p.R > 200f / 255 && p.G < 120f / 255 && p.B > 150f / 255;
	private static bool DeFumaca(Color p) => p.A > 0 && p.R > 150f / 255 && Math.Abs(p.R - p.G) < 25f / 255 && Math.Abs(p.G - p.B) < 25f / 255;
	private static bool Preto(Color p) => p.A > 0 && p.R < 0.01f && p.G < 0.01f && p.B < 0.01f;
}
