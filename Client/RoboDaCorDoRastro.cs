using System.Threading.Tasks;
using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA COR DO RASTRO, DA MIRAGEM E DO RABO (`--diagcorrastro`) -- o item 6 do playtest de 2026-10-10:
///
///   *"o borrao do dash usa a cor base do personagem, entao em Majins que mudaram de cor o efeito fica na cor
///   antiga. O rabo de saiyajin tem o mesmo problema. Os dois devem usar a cor atual e acompanhar mudancas de
///   cor, em todos os clientes. Veja se outros elementos visuais leem a cor base do mesmo jeito."*
///
/// ============================ ELA MEDE PIXEL, E POR ISSO PRECISA DE JANELA ============================
/// A cor de um corpo nao esta na textura: esta nos uniforms de tinta do material de cada camada e no modulate
/// dela. Conferir que "o uniform foi copiado" seria medir a intencao -- este projeto ja catalogou esse cego
/// (uniform escrito nao e pixel desenhado). Aqui o corpo e desenhado num palco proprio (uma `SubViewport` com
/// mundo 2D proprio: sem chao, sem a luz do dia), fotografado, e a COPIA que o jogo larga -- pela mesma
/// `RastroDeCorrida.Largar` e pela mesma `Zanzoken.Deixar` -- e fotografada no mesmo lugar, com o corpo
/// escondido. As duas fotos tem que ser a mesma imagem.
///
/// Rumo ZERO na copia do rastro: as sete amostras do borrao caem no mesmo texel, e o que sobra do shader e so
/// a cor. E a arvore fica PAUSADA: nenhum quadro de animacao troca entre uma foto e a outra, e o esmaecer da
/// copia nao anda.
///
/// TRES CORPOS, um por canal de cor que a folha nao tem:
///   * o MAJIN AZUL de roupa tingida ...... tinta de SOMA no corpo, tinta de MATIZ na roupa
///   * o MESTICO de cabelo roxo, com rabo .. tinta de soma no cabelo, e a metade dela no rabo
///   * o NAMEKUSEIJIN claro ................ o modulate (o `Brilho` 1,18 do catalogo)
///
/// CADA REGRA TEM O SEU DEFEITO INJETADO: o rastro sem cor, a miragem sem cor e o rabo cru sao o jogo de
/// antes, e a regua tem que reprovar os tres.
///
///     Godot --path . --position 1920,0 --resolution 640x360 --semfoco --diagcorrastro
/// </summary>
public partial class RoboDaCorDoRastro : Node
{
	private const int Lado = 192;
	private const float Zoom = 4f;
	private static readonly Vector2 Centro = new(400, 300);
	private static readonly Color Cinza = new(0.5f, 0.5f, 0.5f);

	/// <summary>Niveis (de 255) que um canal pode variar entre duas fotos da MESMA coisa: o arredondamento de 8 bits.</summary>
	private const int Folga = 3;

	private SubViewport _tela = null!;
	private Node2D _palco = null!;
	private VisualCatalog _cat = null!;
	private int _ok, _falhou;

	private void Conferir(bool passou, string oque, string detalhe = "")
	{
		GD.Print($"[corrastro]  {(passou ? "ok   " : "FALHA")}  {oque}{(detalhe.Length == 0 ? "" : $"   [{detalhe}]")}");
		if (passou) _ok++; else _falhou++;
	}

	public override async void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		GD.Print("[corrastro] ==== A COR DO RASTRO, DA MIRAGEM E DO RABO ====");

		if (DisplayServer.GetName() == "headless")
		{
			GD.Print("[corrastro]  FALHA  esta bancada mede PIXEL e precisa de janela: headless nao desenha");
			GD.Print("[corrastro] ===== 1 FALHA(S) =====");
			GetTree().Quit(1);
			return;
		}
		if (VistosDeGente.Catalogo is not { } cat)
		{
			GD.Print("[corrastro]  FALHA  sem visual.json nao ha corpo pra vestir");
			GD.Print("[corrastro] ===== 1 FALHA(S) =====");
			GetTree().Quit(1);
			return;
		}
		_cat = cat;

		try
		{
			MontarOPalco();
			GetTree().Paused = true;
			await Quadros(3);
			await OMajinAzul();
			await OMestico();
			await ONamekuseijin();
		}
		catch (Exception e)
		{
			Conferir(false, "a bancada rodou ate o fim", e.ToString());
		}
		finally
		{
			BorraoDirecional.SemACorDoCorpoDeTeste = false;
			Zanzoken.MiragemSemACorDoCorpoDeTeste = false;
			CharacterVisual.RaboSemACorDoCabeloDeTeste = false;
			GetTree().Paused = false;
		}

		GD.Print(_falhou == 0
			? $"[corrastro] ===== TUDO OK ({_ok} provas) ====="
			: $"[corrastro] ===== {_falhou} FALHA(S) em {_ok + _falhou} provas =====");
		GetTree().Quit(_falhou == 0 ? 0 : 1);
	}

	// =====================================================================
	// O PALCO
	// =====================================================================
	private void MontarOPalco()
	{
		// MUNDO PROPRIO: sem ele a copia entraria no canvas do jogo e herdaria o que estiver la. Aqui a unica
		// coisa que mexe no pixel e o shader de cada camada.
		_tela = new SubViewport { Name = "PalcoDaCor", World2D = new World2D() };
		AddChild(_tela);
		// AS PROPRIEDADES DEPOIS DE ENTRAR NA ARVORE: fora de um `SubViewportContainer` o modo padrao
		// (`WhenVisible`) quer dizer NUNCA (a licao do `RoboDeForma`).
		_tela.Size = new Vector2I(Lado, Lado);
		// O FILTRO DO JOGO, e ele nao vem sozinho: uma `SubViewport` nasce em LINEAR, e a janela do jogo usa o do
		// projeto (`default_texture_filter=0`, nearest). As camadas do corpo vivo HERDAM o filtro do viewport e
		// as copias o cravam em nearest -- no palco linear o corpo saia interpolado e a copia nao, e a primeira
		// rodada desta bancada reprovou 80% dos pixels de um rastro que estava certo.
		_tela.CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest;
		_tela.TransparentBg = false;
		_tela.Disable3D = true;
		_tela.RenderTargetClearMode = SubViewport.ClearMode.Always;
		_tela.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;

		// O FUNDO E UM CINZA MEDIO OPACO: nem preto (o cabelo), nem branco (o realce), e sem alfa pra a
		// leitura da foto nao depender de como o viewport guarda transparencia.
		_palco = new Node2D { Name = "Palco" };
		_tela.AddChild(_palco);
		_palco.AddChild(new ColorRect
		{
			Name = "Fundo", Color = Cinza, Position = Centro - new Vector2(200, 200), Size = new Vector2(400, 400), ZIndex = -100,
		});
		_tela.AddChild(new Camera2D { Name = "Camera", Position = Centro, Zoom = new Vector2(Zoom, Zoom) });
	}

	private (Node2D Corpo, CharacterVisual Visual, RastroDeCorrida Rastro) NovoCorpo(Appearance ap, string raca, bool comRabo, Facing olhar)
	{
		var corpo = new Node2D { Name = "Corpo", Position = Centro };
		var vis = new CharacterVisual { Name = "Visual" };
		var rastro = new RastroDeCorrida { Name = "Rastro" };
		corpo.AddChild(vis);
		corpo.AddChild(rastro);
		_palco.AddChild(corpo);
		vis.Vestir(_cat, ap, raca, "Male");
		if (comRabo) vis.MostrarRabo(true);
		vis.SetMotion(olhar, false);
		return (corpo, vis, rastro);
	}

	private async Task Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
	}

	private async Task<Image> Foto()
	{
		await Quadros(3);
		Image img = _tela.GetTexture().GetImage();
		img.Convert(Image.Format.Rgba8);
		return img;
	}

	private void TirarAsCopias()
	{
		foreach (Node n in GetTree().GetNodesInGroup(RastroDeCorrida.GrupoDoRastro)) n.Free();
		foreach (Node n in _palco.GetChildren())
			if (n is Zanzoken) n.Free();
	}

	// =====================================================================
	// A REGUA
	// =====================================================================
	private static int C8(float v) => Mathf.RoundToInt(Mathf.Clamp(v, 0f, 1f) * 255f);

	private static bool EhFundo(Color c)
		=> Math.Abs(C8(c.R) - C8(Cinza.R)) <= Folga && Math.Abs(C8(c.G) - C8(Cinza.G)) <= Folga && Math.Abs(C8(c.B) - C8(Cinza.B)) <= Folga;

	/// <summary>
	/// DUAS FOTOS, PIXEL A PIXEL. `Desenho` e quantos pixels nao sao fundo em pelo menos uma das duas (o que ha
	/// pra comparar); `Diferentes`, em quantos deles algum canal passa da folga; `Medio`, o desvio medio de
	/// canal (em niveis de 255) dentro do desenho.
	/// </summary>
	private static (int Desenho, int Diferentes, float Medio) Comparar(Image a, Image b)
	{
		int desenho = 0, diferentes = 0;
		double soma = 0;
		for (int y = 0; y < a.GetHeight(); y++)
			for (int x = 0; x < a.GetWidth(); x++)
			{
				Color pa = a.GetPixel(x, y), pb = b.GetPixel(x, y);
				if (EhFundo(pa) && EhFundo(pb)) continue;
				desenho++;
				int d = Math.Max(Math.Abs(C8(pa.R) - C8(pb.R)), Math.Max(Math.Abs(C8(pa.G) - C8(pb.G)), Math.Abs(C8(pa.B) - C8(pb.B))));
				soma += (Math.Abs(C8(pa.R) - C8(pb.R)) + Math.Abs(C8(pa.G) - C8(pb.G)) + Math.Abs(C8(pa.B) - C8(pb.B))) / 3.0;
				if (d > Folga) diferentes++;
			}
		return (desenho, diferentes, desenho == 0 ? 0f : (float)(soma / desenho));
	}

	private static string Ler((int Desenho, int Diferentes, float Medio) m)
		=> $"{m.Diferentes} de {m.Desenho} px fora da folga, desvio medio {m.Medio:0.0}/255";

	/// <summary>A cor media do que nao e fundo, em niveis de 255, e quantos pixels entraram nela.</summary>
	private static (Vector3 Cor, int Pixels) MediaDe(Image img)
	{
		double r = 0, g = 0, b = 0;
		int n = 0;
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
			{
				Color c = img.GetPixel(x, y);
				if (EhFundo(c)) continue;
				r += C8(c.R); g += C8(c.G); b += C8(c.B); n++;
			}
		return n == 0 ? (Vector3.Zero, 0) : (new Vector3((float)(r / n), (float)(g / n), (float)(b / n)), n);
	}

	private static string Media(Image img)
	{
		(Vector3 c, int n) = MediaDe(img);
		return n == 0 ? "(vazia)" : $"({c.X:0} {c.Y:0} {c.Z:0}) em {n} px";
	}

	/// <summary>A distancia entre as cores medias de duas fotos, em niveis de 255.</summary>
	private static float Distancia(Image a, Image b) => MediaDe(a).Cor.DistanceTo(MediaDe(b).Cor);

	/// <summary>
	/// A PROVA DE UM CORPO: o rastro e a miragem dele saem na cor que ele tem, e saem na cor da FOLHA quando o
	/// defeito de cada um volta.
	/// </summary>
	private async Task ORastroEAMiragem(string quem, Node2D corpo, CharacterVisual vis, RastroDeCorrida rastro, bool aCorMudaAFolha = true)
	{
		Image vivo = await Foto();
		int pixels = Comparar(vivo, await FotoSoDoFundo(vis)).Desenho;
		Conferir(pixels > 400, $"(montagem) {quem} esta desenhado no palco", $"{pixels} px de corpo");

		// ---- o rastro, pela `Largar` do jogo
		vis.Visible = false;
		rastro.LargarDeTeste(vis.GlobalPosition, Vector2.Zero, 1f, 600);
		Image copia = await Foto();
		(int Desenho, int Diferentes, float Medio) certo = Comparar(vivo, copia);
		Conferir(certo.Desenho > 400 && certo.Diferentes == 0,
			$"**O RASTRO DE {quem} SAI NA COR QUE ELE TEM**: a copia da corrida e o corpo, pixel a pixel", Ler(certo));
		TirarAsCopias();

		BorraoDirecional.SemACorDoCorpoDeTeste = true;
		rastro.LargarDeTeste(vis.GlobalPosition, Vector2.Zero, 1f, 600);
		Image crua = await Foto();
		BorraoDirecional.SemACorDoCorpoDeTeste = false;
		(int Desenho, int Diferentes, float Medio) errado = Comparar(vivo, crua);
		if (aCorMudaAFolha)
			Conferir(errado.Diferentes > 100,
				$"(defeito injetado: o borrao sem a cor do corpo) o rastro de {quem} sai na cor da FOLHA -- o relato do playtest, reproduzido", Ler(errado));
		TirarAsCopias();

		// ---- a miragem do Zanzoken, pela `Deixar` do jogo
		Zanzoken.Deixar(_palco, corpo);
		Image miragem = await Foto();
		(int Desenho, int Diferentes, float Medio) mCerta = Comparar(vivo, miragem);
		TirarAsCopias();

		Zanzoken.MiragemSemACorDoCorpoDeTeste = true;
		Zanzoken.Deixar(_palco, corpo);
		Image miragemCrua = await Foto();
		Zanzoken.MiragemSemACorDoCorpoDeTeste = false;
		(int Desenho, int Diferentes, float Medio) mErrada = Comparar(vivo, miragemCrua);
		TirarAsCopias();

		GD.Print($"[corrastro]  --     miragem de {quem}: com a cor {Ler(mCerta)} | sem a cor {Ler(mErrada)}");
		GD.Print($"[corrastro]  --     cores medias de {quem}: corpo {Media(vivo)} | rastro {Media(copia)} | rastro cru {Media(crua)} "
			   + $"| miragem {Media(miragem)} | miragem crua {Media(miragemCrua)}");
		// A MIRAGEM NAO E UMA COPIA PARADA: a silhueta ondula e se desfaz (o `Zanzoken.gdshader`), entao ela
		// nunca bate pixel a pixel com o corpo -- a primeira rodada mostrou ~18% dos pixels fora, com a cor
		// certa em todos. O que se cobra dela e a COR: a media da miragem tem que ser a do corpo, e a da
		// miragem crua (o defeito de antes) nao.
		float certa = Distancia(miragem, vivo), errada = Distancia(miragemCrua, vivo);
		if (aCorMudaAFolha)
			Conferir(mCerta.Desenho > 400 && certa <= 2.5f && errada > 5f,
				$"**A MIRAGEM DE {quem} NASCE NA COR DELE** (o vulto do Zanzoken lia a folha do mesmo jeito): a cor media dela e a do corpo",
				$"a {certa:0.0}/255 da cor do corpo; a miragem crua (defeito injetado) fica a {errada:0.0}/255");

		vis.Visible = true;
	}

	private async Task<Image> FotoSoDoFundo(CharacterVisual vis)
	{
		vis.Visible = false;
		Image img = await Foto();
		vis.Visible = true;
		return img;
	}

	// =====================================================================
	// OS TRES CORPOS
	// =====================================================================
	private async Task OMajinAzul()
	{
		GD.Print("[corrastro] -- o Majin que mudou de cor, de roupa tingida --");
		string roupa = _cat.Roupas.Find(r => r.Contains("Blue suit", StringComparison.Ordinal)) ?? _cat.Roupas[0];
		var ap = new Appearance { CorPele = new Rgb(40, 110, 255) };
		ap.Roupa.Add(new PecaDeRoupa(roupa, new Rgb(230, 40, 40)));
		Conferir(_cat.CorposDe("Majin").CorLivre, "(montagem) o Majin e raca de cor livre: a cor dele e TINTA, nao arquivo");

		(Node2D corpo, CharacterVisual vis, RastroDeCorrida rastro) = NovoCorpo(ap, "Majin", comRabo: false, Facing.South);
		await ORastroEAMiragem("UM MAJIN AZUL", corpo, vis, rastro);

		// ============================ UMA MEDIDA, E NAO UMA COBRANCA: O ALFA DA COPIA ============================
		// O rastro larga cada copia com `Modulate.a` baixo (0,26 na corrida) e um `Tween` que o leva a zero. O
		// `Borrao.gdshader` escreve o `COLOR` sem le-lo, e neste motor quem faz isso joga o modulate fora (o
		// `Personagem.gdshader` documenta o mesmo tombo). Aqui a copia sai com alfa 0,3 sobre o cinza: se a cor
		// media dela for a do corpo, o alfa NAO chegou ao pixel. Fica dito no log e no relatorio -- nao e o
		// pedido deste item (a cor), e ligar o alfa mudaria a cara do rastro que o dono ja conhece.
		// =========================================================================================================
		{
			Image corpoInteiro = await Foto();
			vis.Visible = false;
			rastro.LargarDeTeste(vis.GlobalPosition, Vector2.Zero, 0.3f, 600);
			Image translucida = await Foto();
			vis.Visible = true;
			TirarAsCopias();
			float d = Distancia(translucida, corpoInteiro);
			GD.Print($"[corrastro]  --     o alfa da copia do rastro chega ao pixel? {(d < 3f ? "NAO" : "SIM")}: pedida com alfa 0,30, "
				   + $"ela sai a {d:0.0}/255 da cor do corpo opaco -- corpo {Media(corpoInteiro)}, copia {Media(translucida)}");
		}

		// "ACOMPANHAR MUDANCAS DE COR": a ficha muda (o guarda-roupa, o pacote de aparencia), e a proxima
		// copia ja sai na cor nova -- nao ha cor guardada em lugar nenhum pra ficar velha.
		ap.CorPele = new Rgb(60, 220, 90);
		vis.Vestir(_cat, ap, "Majin", "Male");
		Image verde = await Foto();
		vis.Visible = false;
		rastro.LargarDeTeste(vis.GlobalPosition, Vector2.Zero, 1f, 600);
		Image copiaVerde = await Foto();
		vis.Visible = true;
		TirarAsCopias();
		(int Desenho, int Diferentes, float Medio) m = Comparar(verde, copiaVerde);
		Conferir(m.Desenho > 400 && m.Diferentes == 0,
			"**MUDOU DE COR, O RASTRO MUDA JUNTO**: o Majin passa de azul a verde e a copia seguinte ja e verde", Ler(m));

		corpo.Free();
	}

	private async Task OMestico()
	{
		GD.Print("[corrastro] -- o mestico de cabelo roxo, com rabo --");
		var roxo = new Rgb(170, 60, 210);
		var ap = new Appearance { Cabelo = "Goku", CorCabelo = roxo };
		(Node2D corpo, CharacterVisual vis, RastroDeCorrida rastro) = NovoCorpo(ap, "Halfbreed", comRabo: true, Facing.East);

		Image comCor = await Foto();
		CharacterVisual.RaboSemACorDoCabeloDeTeste = true;
		vis.Vestir(_cat, ap, "Halfbreed", "Male");
		Image cru = await Foto();
		CharacterVisual.RaboSemACorDoCabeloDeTeste = false;
		vis.Vestir(_cat, ap, "Halfbreed", "Male");

		// O RABO E A UNICA COISA QUE MUDA ENTRE AS DUAS FOTOS (o cabelo tem a mesma tinta nas duas), entao os
		// pixels que diferem SAO o rabo -- e em cada um deles a foto com cor tem que ser a crua MAIS metade
		// da cor do cabelo, canal a canal: o `icon += rgb(HairR/2, HairG/2, HairB/2)` do original.
		(int rabo, int certos) = ContarSoma(cru, comCor, roxo);
		Conferir(rabo > 30 && certos == rabo,
			"**O RABO TEM METADE DA COR DO CABELO**, somada (`SaiyanObjects.dm:99`): em cada pixel do rabo, cru + metade do roxo",
			$"{certos} de {rabo} px do rabo batem a conta");
		Conferir(rabo > 30,
			"(defeito injetado: o rabo e o sprite cru) o rabo do mestico de cabelo roxo sai no marrom do arquivo -- o jogo de antes",
			$"{rabo} px mudam entre o rabo cru e o tingido");

		// TROCOU A COR DO CABELO (o guarda-roupa): o rabo que JA EXISTIA tem que acompanhar.
		var loiro = new Rgb(230, 200, 40);
		ap.CorCabelo = loiro;
		vis.Vestir(_cat, ap, "Halfbreed", "Male");
		Image deLoiro = await Foto();
		CharacterVisual.RaboSemACorDoCabeloDeTeste = true;
		vis.Vestir(_cat, ap, "Halfbreed", "Male");
		Image cruDeLoiro = await Foto();
		CharacterVisual.RaboSemACorDoCabeloDeTeste = false;
		vis.Vestir(_cat, ap, "Halfbreed", "Male");
		(int rabo2, int certos2) = ContarSoma(cruDeLoiro, deLoiro, loiro);
		Conferir(rabo2 > 30 && certos2 == rabo2,
			"**MUDOU A COR DO CABELO, O RABO MUDA JUNTO**: de roxo a loiro, o rabo que ja existia e repintado",
			$"{certos2} de {rabo2} px do rabo batem a conta com o loiro");

		await ORastroEAMiragem("UM MESTICO DE CABELO LOIRO E RABO", corpo, vis, rastro);
		corpo.Free();
	}

	/// <summary>
	/// Nos pixels em que as duas fotos diferem: quantos sao, e em quantos a foto tingida e a crua MAIS metade de
	/// <paramref name="cabelo"/>, canal a canal (com o teto de 255 da soma).
	/// </summary>
	private static (int Mudaram, int BatemAConta) ContarSoma(Image crua, Image tingida, Rgb cabelo)
	{
		int mudaram = 0, batem = 0;
		for (int y = 0; y < crua.GetHeight(); y++)
			for (int x = 0; x < crua.GetWidth(); x++)
			{
				Color a = crua.GetPixel(x, y), b = tingida.GetPixel(x, y);
				int dr = C8(b.R) - C8(a.R), dg = C8(b.G) - C8(a.G), db = C8(b.B) - C8(a.B);
				if (Math.Abs(dr) <= Folga && Math.Abs(dg) <= Folga && Math.Abs(db) <= Folga) continue;
				mudaram++;
				bool ok = Math.Abs(Math.Min(C8(a.R) + cabelo.R / 2, 255) - C8(b.R)) <= Folga
					   && Math.Abs(Math.Min(C8(a.G) + cabelo.G / 2, 255) - C8(b.G)) <= Folga
					   && Math.Abs(Math.Min(C8(a.B) + cabelo.B / 2, 255) - C8(b.B)) <= Folga;
				if (ok) batem++;
			}
		return (mudaram, batem);
	}

	private async Task ONamekuseijin()
	{
		GD.Print("[corrastro] -- o Namekuseijin claro (a cor mora no modulate) --");
		var ap = new Appearance { Tom = 0 };
		(_, float brilho) = _cat.TintaDoCorpo(ap, "Namekian");
		Conferir(Math.Abs(brilho - 1f) > 0.05f, "(montagem) o tom claro do Namekuseijin e um brilho diferente de 1", $"{brilho:0.00}");

		(Node2D corpo, CharacterVisual vis, RastroDeCorrida rastro) = NovoCorpo(ap, "Namekian", comRabo: false, Facing.South);
		await ORastroEAMiragem("UM NAMEKUSEIJIN CLARO", corpo, vis, rastro);
		corpo.Free();
	}
}
