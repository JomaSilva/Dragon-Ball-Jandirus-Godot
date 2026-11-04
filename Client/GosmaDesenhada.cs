using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A GOSMA DO MAJIN NA TELA (dono, 2026-11-01, o pedido 219) ============================
/// O corpo da gosma (um corpo do mundo, ver `GameServer.GosmaDoMajin.cs`) chega pelo snapshot como qualquer outro, e e pelo `Ocupacao.Gosma`
/// dele que esta tela troca o boneco pela gosma (`World.MarcarGosma`): a `regenerate_2` da `Majin1` -- o `icon_state = "Regenerate 2"` da
/// `obj/Majin_Goo` do DU --, em laco, na cor do Majin que a largou (`g.icon += base_icon_color`): a tinta e a pele do corpo que ela veste,
/// como os pedacos do Majin desfeito (<see cref="PedacosDoMajin"/>).
///
/// FILHA DO CORPO, como os pedacos: morre com ele, e devolve os irmaos que escondeu (o boneco, a aura, o balao...) ao sair da arvore. A
/// gosma escondida nem chega aqui: o corpo dela vem `Oculto`, e a tela apaga o corpo inteiro.
///
/// A COR CHEGA DEPOIS, as vezes: a aparencia do corpo viaja num pacote proprio, e o snapshot pode chegar antes dela. A tinta e lida do boneco
/// escondido ate ela existir.
/// </summary>
public partial class GosmaDesenhada : Node2D
{
	/// <summary>O nome do node. Uma casa so: quem cria e quem tira procuram por ele.</summary>
	public const string NomeDoNode = "GosmaDesenhada";

	/// <summary>A animacao da gosma na folha da `Majin1` (`PedacosDoMajin.FolhaDosPedacos`).</summary>
	public const string Animacao = "regenerate_2";

	private readonly List<CanvasItem> _escondidos = [];
	private AnimatedSprite2D? _gosma;
	private bool _pintada;

	/// <summary>A animacao que esta tocando ("" = nenhuma) -- so bancada.</summary>
	public string AnimacaoDeTeste => _gosma is { } g && IsInstanceValid(g) ? g.Animation.ToString() : "";

	/// <summary>A gosma ja tem a cor do Majin? -- so bancada.</summary>
	public bool PintadaDeTeste => _pintada;

	/// <summary>A tinta que a gosma recebeu (a cor e o modo do shader), ou nula sem tinta -- so bancada.</summary>
	public (Vector3 Cor, int Modo)? TintaDeTeste => _gosma is { } g && IsInstanceValid(g) ? CharacterVisual.TintaDoMaterial(g.Material) : null;

	public override void _Ready()
	{
		foreach (Node n in GetParent().GetChildren())
			if (n != this && n is not Camera2D && n is CanvasItem { Visible: true } irmao)
			{
				irmao.Visible = false;
				_escondidos.Add(irmao);
			}

		if (FolhasPresas.Carregar(PedacosDoMajin.FolhaDosPedacos) is not { } folha || !folha.HasAnimation(Animacao))
		{
			GD.PushWarning($"[visual] a gosma nao tem a `{Animacao}` em {PedacosDoMajin.FolhaDosPedacos}");
			return;
		}
		_gosma = new AnimatedSprite2D { SpriteFrames = folha, Animation = Animacao, Centered = true };
		AddChild(_gosma);
		_gosma.Play();
		Pintar();
	}

	public override void _Process(double delta)
	{
		if (!_pintada) Pintar();
	}

	/// <summary>A cor do Majin: a tinta e a pele do boneco escondido (o corpo da gosma veste a aparencia do dono).</summary>
	private void Pintar()
	{
		if (_gosma == null || GetParent()?.GetNodeOrNull<CharacterVisual>("Visual")?.CorpoParaOsPedacos() is not { } pele) return;
		_gosma.Material = pele.Tinta;
		_gosma.SelfModulate = pele.Pele;
		_pintada = true;
	}

	public override void _ExitTree()
	{
		foreach (CanvasItem c in _escondidos)
			if (IsInstanceValid(c)) c.Visible = true;
		_escondidos.Clear();
	}
}
