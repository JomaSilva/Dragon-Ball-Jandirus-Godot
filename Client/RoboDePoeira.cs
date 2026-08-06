using Godot;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA POEIRA (`--diagpoeira`).
///
/// ============================ O QUE SO O TESTE RESPONDE ============================
/// Poeira e um efeito de dois segundos: olhando, ve-se que "tem fumaca" e mais nada. O que a foto
/// nao diz:
///   * a cor saiu do TILE que caiu, ou e o marrom generico de sempre?
///   * o teto de efeitos vivos DISPARA? (regra da casa: teto que nunca e atingido e indistinguivel
///     de teto nenhum -- entao a bancada tem que estourar o limite de proposito)
///   * o node se apaga sozinho, ou cada parede derrubada deixa um emissor no mapa pra sempre?
/// ==================================================================================
///
/// COMO RODAR (com janela, pra a foto sair):
///     Godot --path . --host --quebrarteste 40 --diagpoeira --nome Poeira --conta poeira
///
/// O `--quebrarteste 40` derruba 40 celulas no nascimento -- MAIS que o teto de 24, que e o unico
/// jeito de provar que ele funciona.
/// </summary>
public partial class RoboDePoeira : Node
{
	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private bool _acabou;
	private int _passo;
	private double _t;
	private int _pico;

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Process(double delta)
	{
		if (_acabou || GameClient.Instance is not { Connected: true }) return;
		if (World.Instancia == null) return;

		// O PICO E LIDO TODO QUADRO, e nao no fim: a rajada acontece em um punhado de quadros e o
		// numero ja teria caido quando a bancada fosse olhar.
		_pico = Math.Max(_pico, PoeiraDeEstrago.VivosDeTeste);

		_t += delta;
		if (_t < 0.7) return;
		_t = 0;

		switch (_passo++)
		{
			case 0:
				// deixa a leva de `--quebrarteste` chegar e a poeira nascer
				break;

			case 1:
				Conferir(PoeiraDeEstrago.PedidosDeTeste > 0,
					$"a destruicao soltou poeira ({PoeiraDeEstrago.PedidosDeTeste} efeito(s) pedido(s))");

				// ============================ O TETO TEM QUE DISPARAR ============================
				// `PedidosDeTeste` conta quantos `Soltar` aconteceram e `_pico` conta quantos
				// chegaram a viver ao mesmo tempo. Se o teto funciona, o primeiro passa de 24 e o
				// segundo nao.
				// ================================================================================
				Conferir(PoeiraDeEstrago.PedidosDeTeste > PoeiraDeEstrago.MaxVivos,
					$"a bancada ESTOUROU o teto de proposito: {PoeiraDeEstrago.PedidosDeTeste} pedidos"
					+ $" contra teto {PoeiraDeEstrago.MaxVivos}");
				Conferir(_pico <= PoeiraDeEstrago.MaxVivos,
					$"e o teto SEGUROU: nunca passou de {_pico} efeito(s) vivos");
				break;

			case 2:
			{
				// A COR VEIO DO TILE. O padrao e um marrom escuro fixo (0.46, 0.36, 0.26); qualquer
				// coisa diferente disso prova que o pixel do cenario foi lido.
				Color? c = PoeiraDeEstrago.UltimaCorDeTeste;
				bool doTile = c is { } k
					&& (Mathf.Abs(k.R - 0.46f) > 0.02f || Mathf.Abs(k.G - 0.36f) > 0.02f
						|| Mathf.Abs(k.B - 0.26f) > 0.02f);
				Conferir(doTile, $"a cor da poeira saiu do TILE, e nao do padrao ({c})");
				Fotografar("user://poeira.png");
				break;
			}

			case 3:
			case 4:
				break;   // deixa a nuvem viver os ~2 s dela

			case 5:
				// SE APAGA SOZINHO. Sem isto cada parede derrubada deixaria um emissor parado.
				Conferir(PoeiraDeEstrago.VivosDeTeste == 0,
					$"passados ~3 s, nao sobrou emissor nenhum ({PoeiraDeEstrago.VivosDeTeste})");
				break;

			default:
				_acabou = true;
				GD.Print("\n[poeira] ===== BANCADA DA POEIRA =====");
				foreach (string l in _passos) GD.Print("[poeira] " + l);
				GD.Print(_falhas.Count == 0
					? "[poeira] ===== TUDO OK ====="
					: $"[poeira] ===== {_falhas.Count} FALHA(S) =====\n[poeira]   " + string.Join("\n[poeira]   ", _falhas));
				break;
		}
	}

	/// <summary>Salva a tela, se houver renderizador. No headless o `GetImage` volta vazio.</summary>
	private void Fotografar(string destino)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { _passos.Add("  --     sem foto (headless nao renderiza)"); return; }
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_passos.Add("  ok     foto salva em " + caminho);
		}
		catch (Exception e) { _passos.Add("  --     sem foto: " + e.Message); }
	}
}
