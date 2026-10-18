using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO OVO (`--diagovo`, 2026-10-17). O dono: *"tinha um sprite de ovo"* -- o `mob/Egg` do DM (`icon = 'Egg.dmi'`, `NPClist.dm:15-25`)
/// que o `Lay_Egg` poe no chao (`Mating.dm:230`) e de onde o filho nasce.
///
/// Com janela e com `--host`, nascendo NAMEKUSEIJIN (`--raca Namekian`, 18 anos: as racas de ovo poem ovo sozinhas). O robo aperta "Pôr um
/// ovo" pelo caminho do dedo (`GameClient.SendHabilidade("filho_ovo")`) e mede a TELA -- o ovo que o servidor deste processo poe no chao
/// tem que estar desenhado na celula dele (`World.ObraDeTeste`), com a arte do DM, e tem que APARECER NA FOTO:
///
///   1. CONTRA-EXEMPLO (`Filhos.OvoSemCorpoDeTeste`, o ovo de antes, guardado como a gravidez): o ovo entra na fila do servidor e a tela
///      nao desenha nada no chao -- a prova 2 reprovaria. Esse ovo sai da fila, e a tela de ANTES do ovo de verdade e fotografada
///      (`ovo-0-antes.png`);
///   2. o ovo de verdade: a obra `Egg` cai numa celula do lado do corpo, ligada ao ovo da fila, e a tela a desenha com a arte do
///      `Egg.dmi` (`Misc/Egg.tres`) -- foto `ovo-1-no-chao.png`. E A FOTO E A PROVA: na celula dele, a foto de depois tem pixels que a de
///      antes nao tinha (o chao parado nao muda; so o ovo entra). A primeira versao deste robo so perguntava ao node, e fotografava no
///      quadro em que ele nasceu -- passou com a foto sem ovo nenhum;
///   3. um soco (a pancada de producao, `Estragar`) quebra o ovo: ele some da tela, sai da fila, e quem o pos ouve que o filho se perdeu --
///      foto `ovo-2-quebrado.png`.
///
///     Godot --path . --host --rede 8455 --campoteste 8 --horateste 0.5 --semfauna --diagovo --raca Namekian --conta bancada_dovo --nome Ovoteiro
/// </summary>
public partial class RoboDoOvo : Node
{
	/// <summary>A arte que o catalogo da ao ovo -- a folha convertida do `Egg.dmi` do DM.</summary>
	private const string ArteDoOvo = "res://Assets/Sprites/Misc/Egg.tres";

	/// <summary>O lado do quadro do ovo na folha (32 px).</summary>
	private const float LadoDoOvo = 32f;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private int _passo;
	private double _t, _espera;
	private (int Cx, int Cy, int Obra)? _celula;
	private Image? _antes;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Passar() { _passo++; _t = 0; }

	private Image? Fotografar(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return null; }
		string caminho = ProjectSettings.GlobalizePath(destino);
		img.SavePng(caminho);
		_linhas.Add("  --     foto salva em " + caminho);
		return img;
	}

	/// <summary>Ha um ovo desenhado em alguma celula em volta do meu corpo? (O contra-exemplo pergunta isto sem saber a celula.)</summary>
	private static bool OvoDesenhadoPerto(World mundo, Vector2 meu)
	{
		(int cx, int cy) = Jandirus.Core.Tech.CatalogoDeObras.Celula(meu.X, meu.Y);
		for (int dy = -2; dy <= 2; dy++)
			for (int dx = -2; dx <= 2; dx++)
				if (mundo.ObraDeTeste(cx + dx, cy + dy) is { Tipo: Filhos.TipoDoOvo }) return true;
		return false;
	}

	/// <summary>
	/// O QUADRO DO OVO NA TELA: o node da obra fica na base da celula (canto de baixo, a esquerda) e o desenho sobe dali (`ObraDesenhada`),
	/// na escala da camera. Encolhido um decimo de cada lado: a borda da celula nao conta, e o corpo ao lado nao entra.
	/// </summary>
	private static Rect2 QuadroNaTela(Node2D obra)
	{
		Transform2D tr = obra.GetGlobalTransformWithCanvas();
		float s = tr.X.Length() * LadoDoOvo;
		var quadro = new Rect2(tr.Origin.X, tr.Origin.Y - s, s, s);
		return quadro.Grow(-s * 0.1f);
	}

	/// <summary>
	/// QUANTOS PIXELS DE UM QUADRO MUDARAM entre duas fotos (mais de 0,2 num canal). O ovo do `Egg.dmi` e pequeno -- 122 pixels opacos no
	/// quadro de 32 -- e verde-claro com pintas marrons: nao ha cor que o separe do chao sozinha, mas o chao parado e o mesmo nas duas fotos.
	/// </summary>
	private static int PixelsQueMudaram(Image antes, Image depois, Rect2 r)
	{
		int n = 0;
		int altura = Math.Min(antes.GetHeight(), depois.GetHeight()), largura = Math.Min(antes.GetWidth(), depois.GetWidth());
		for (int y = Math.Max(0, (int)r.Position.Y); y < Math.Min(altura, (int)r.End.Y); y++)
			for (int x = Math.Max(0, (int)r.Position.X); x < Math.Min(largura, (int)r.End.X); x++)
			{
				Color a = antes.GetPixel(x, y), b = depois.GetPixel(x, y);
				if (Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))) > 0.2f) n++;
			}
		return n;
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (GameClient.Instance is not { Connected: true, LocalId: > 0 } cli || World.Instancia is not { PosicaoLocalDeTeste: { } pos } mundo
			|| Jandirus.Server.GameServer.Instance is not Jandirus.Server.GameServer srv)
		{
			_espera += delta;
			if (_espera > 30) { Conferir(false, "em 30 s o cliente conectou, o mundo nasceu e o servidor esta neste processo"); Fechar(); }
			return;
		}
		_t += delta;
		int eu = cli.LocalId;
		Vector2 meu = new(pos.X, pos.Y);
		switch (_passo)
		{
			case 0:
				if (_t < 1.5) return;   // o corpo assenta no campo
				GD.Print("[dovo] ===== O OVO NO CHAO (dono, 2026-10-17) =====");
				srv.EmbateDeFoto_CeuLimpo(eu);   // a foto nao depende do clima do dia
				Filhos.OvoSemCorpoDeTeste = true;
				cli.SendHabilidade("filho_ovo");
				Passar();
				break;
			case 1:
				if (_t < 1.5) return;
				bool naFila = srv.OvoDoCorpoDeTeste(eu) is { DeOvo: true, Ovo: 0 };
				Conferir(naFila && !OvoDesenhadoPerto(mundo, meu),
						 "CONTRA-EXEMPLO (defeito injetado: o ovo de antes, guardado como a gravidez): o ovo entra na fila do servidor e a tela NAO desenha "
						 + "ovo nenhum no chao -- a prova do ovo desenhado reprovaria");
				Filhos.OvoSemCorpoDeTeste = false;
				srv.TirarOOvoDoCorpoDeTeste(eu);
				Passar();
				break;
			case 2:
				if (_t < 0.6) return;
				_antes = Fotografar("user://ovo-0-antes.png");
				cli.SendHabilidade("filho_ovo");
				Passar();
				break;
			case 3:
				// O OVO NASCE NA TELA -- e so no passo seguinte ele e fotografado: a foto devolve o ultimo quadro DESENHADO, e o quadro em que
				// o node nasce ainda nao o tem.
				_celula = srv.OvoNoChaoDeTeste(eu);
				if ((_celula is not { } achou || mundo.ObraDeTeste(achou.Cx, achou.Cy) == null) && _t < 5) return;
				Passar();
				break;
			case 4:
			{
				if (_t < 0.6) return;
				ObraDesenhada? desenho = _celula is { } c ? mundo.ObraDeTeste(c.Cx, c.Cy) : null;
				(int mx, int my) = Jandirus.Core.Tech.CatalogoDeObras.Celula(meu.X, meu.Y);
				Conferir(_celula is { } cel && Math.Max(Math.Abs(cel.Cx - mx), Math.Abs(cel.Cy - my)) == 1 && srv.OvoDoCorpoDeTeste(eu)?.Ovo == cel.Obra,
						 $"o \"Pôr um ovo\" poe o OVO NO CHAO, numa celula do lado do corpo, ligado ao ovo da fila (celula {_celula?.Cx},{_celula?.Cy}; corpo {mx},{my})");
				Conferir(desenho is { Tipo: Filhos.TipoDoOvo, Arte: ArteDoOvo } && desenho.IsVisibleInTree(),
						 $"...e a TELA o desenha ali, com a arte do `Egg.dmi` do DM ({desenho?.Arte ?? "nada desenhado"})");
				Image? foto = Fotografar("user://ovo-1-no-chao.png");
				if (foto != null && _antes != null && desenho != null)
				{
					Rect2 quadro = QuadroNaTela(desenho);
					int mudaram = PixelsQueMudaram(_antes, foto, quadro);
					Conferir(mudaram >= 0.05f * quadro.Area,
							 $"...e ELE APARECE NA FOTO: na celula dele, {mudaram} pixels mudaram desde a foto de antes do ovo "
							 + $"({mudaram / Math.Max(quadro.Area, 1) * 100:0}% do quadro de {quadro.Size.X:0}x{quadro.Size.Y:0} px; o ovo do DM ocupa 12% do quadro dele)");
				}
				else Conferir(false, "a prova do pixel precisa das duas fotos e do ovo desenhado -- a bancada e de JANELA");
				List<string> ouvido = srv.QuebrarOOvoDeTeste(eu);
				Conferir(ouvido.Any(x => x.Contains("o seu ovo foi quebrado")),
						 "um SOCO no ovo (a pancada de producao) o quebra, e quem o pos ouve que o filho se perdeu");
				Passar();
				break;
			}
			case 5:
				if (_t < 1) return;
				Conferir(_celula is { } c2 && mundo.ObraDeTeste(c2.Cx, c2.Cy) == null && srv.OvoDoCorpoDeTeste(eu) == null,
						 "...e o ovo quebrado SOME da tela e sai da fila");
				Fotografar("user://ovo-2-quebrado.png");
				Fechar();
				break;
		}
	}

	private void Fechar()
	{
		_acabou = true;
		Filhos.OvoSemCorpoDeTeste = false;
		foreach (string l in _linhas) GD.Print("[dovo] " + l);
		GD.Print(_falhas.Count == 0 ? "[dovo] ===== TUDO OK =====" : $"[dovo] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dovo]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
