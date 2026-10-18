using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ O OUTRO MUNDO POVOADO E O VILAO DO INFERNO, NA TELA (`--diaginferno`, 2026-10-16) ============================
/// O dono: *"faça com q viloes de saga derrotados as vezes apareçam no inferno no outro mundo como uma boss fight e quem vencer ganha
/// zenni e honra, alem de colocar npcs no outro mundo, npcs mortos com a aureula etc, no inferno tb"*.
///
/// A `--infernoteste` prova as regras pelo servidor e nao enxerga a tela. Esta bancada fotografa o que o morto VE: o corpo local morre
/// e vai ao Outro Mundo (ao lado da mesa do Enma), almas de aureola em volta; depois o Inferno, com as condenadas; depois o vilao de
/// saga que escapa (a cena de chegada de chefe, o vilao de aureola no Inferno), e a derrota dele pelo funil de producao, com o premio
/// no chat. Cada alma e o vilao tem a aureola cobrada NO BONECO da minha tela (`CharacterVisual.AureolaVisivelDeTeste`) e INTEIRA
/// dentro da janela (<see cref="AureolaNaTela"/>).
///
/// COMO RODAR -- com JANELA (no headless o `GetImage` volta vazio), no segundo monitor e mudo:
///
///     Godot --path . --host --rede 8438 --horateste 0.5 --semfauna --diaginferno --position 1920,0 --resolution 1280x720 \
///           --raca Human --conta bancada_dinferno --nome Defunto
/// </summary>
public partial class RoboDoInferno : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	private const double Paciencia = 180;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private double _t, _vida;
	private int _passo;
	private int _eu, _vilao;
	private List<int> _almas = [];

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		_linhas.Add((ok ? "  ok     " : "  FALHA  ") + oque + (ok || detalhe.Length == 0 ? "" : $"   [{detalhe}]"));
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --     " + oque);

	public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

	private const int PEntrar = 0, PAlem = 1, PFotoDoAlem = 2, PInferno = 3, PFotoDoInferno = 4, PChegada = 5, PVilao = 6, PFora = 7, PDerrota = 8,
		PFim = 9;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		_vida += delta;
		_t += delta;
		if (_vida > Paciencia) { Conferir(false, $"a bancada coube na paciencia ({Paciencia:0} s; parou no passo {_passo})"); Fechar(); return; }
		if (S is not { } srv) { if (_vida > 20) { Nota("sem servidor no processo (`--diaginferno` precisa de `--host`)"); Fechar(); } return; }
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo || !IsInstanceValid(mundo)) return;

		switch (_passo)
		{
			case PEntrar:
				if (_t < 3) return;
				_eu = cli.LocalId;
				Conferir(srv.MortoNaFotoDoInferno(_eu, Alem.ZonaDoOutroMundo), "o corpo local morreu e foi ao Outro Mundo, ao lado da mesa do Enma");
				Ir(PAlem);
				break;

			case PAlem:
				if (!cli.Zone.Equals(ZoneKey.Premade(Alem.ZonaDoOutroMundo)) && _t < 15) return;
				if (_t < 4) return;   // o mapa monta
				Conferir(cli.Zone.Equals(ZoneKey.Premade(Alem.ZonaDoOutroMundo)), "...e esta no Outro Mundo", cli.Zone.ToString());
				_almas = srv.AlmasNaFotoDoInferno(_eu, 6);
				Conferir(_almas.Count == 6, "seis ALMAS nasceram em volta, pela porta de producao (o molde 'alma')", $"{_almas.Count}");
				Ir(PFotoDoAlem);
				break;

			case PFotoDoAlem:
				if (_t < 2.5) return;   // a aparencia e a aureola atravessam o fio
				ConferirAsAlmas(mundo, "no Outro Mundo");
				Fotografar("dinferno-1-alem.png", "1. o Outro Mundo, ao lado da mesa do Enma: almas de aureola em volta");
				Conferir(srv.MortoNaFotoDoInferno(_eu, Alem.ZonaDoInferno), "o corpo local foi ao Inferno (o portao)");
				Ir(PInferno);
				break;

			case PInferno:
				if (!cli.Zone.Equals(ZoneKey.Premade(Alem.ZonaDoInferno)) && _t < 15) return;
				if (_t < 4) return;
				Conferir(cli.Zone.Equals(ZoneKey.Premade(Alem.ZonaDoInferno)), "...e esta no Inferno", cli.Zone.ToString());
				_almas = srv.AlmasNaFotoDoInferno(_eu, 6);
				Conferir(_almas.Count == 6, "seis ALMAS CONDENADAS nasceram em volta (o molde 'alma_condenada')", $"{_almas.Count}");
				Ir(PFotoDoInferno);
				break;

			case PFotoDoInferno:
				if (_t < 2.5) return;
				ConferirAsAlmas(mundo, "no Inferno");
				Fotografar("dinferno-2-inferno.png", "2. o Inferno: as almas condenadas, de aureola");
				// O FREEZA DE NAMEK NA FORMA FINAL (o 5o degrau) -- o vilao que o servidor derrotou, de volta.
				_vilao = srv.VilaoNaFotoDoInferno(_eu, "freeza_namek", 4);
				Conferir(_vilao != 0, "O VILAO DE SAGA ESCAPOU no Inferno (o Freeza de Namek, derrotado na forma final)");
				if (_vilao == 0) { Fechar(); return; }
				Ir(PChegada);
				break;

			case PChegada:
				if (_t < 2.0) return;
				Fotografar("dinferno-3-chegada.png", "3. a cena de chegada de chefe, pra quem esta no Outro Mundo");
				Ir(PVilao);
				break;

			case PVilao:
			{
				if (_t < 6.5) return;   // a cena acaba (6,5 s)
				CharacterVisual? v = mundo.CorpoDeTeste(_vilao)?.GetNodeOrNull<CharacterVisual>("Visual");
				Rect2? aureola = v != null ? AureolaNaTela(mundo, _vilao, v) : null;
				Conferir(v != null && v.IsVisibleInTree(), "o vilao esta no meu mundo, de pe no Inferno");
				Conferir(v?.AureolaVisivelDeTeste == true, "...COM A AUREOLA de todo morto -- ele volta do Inferno, nao da vida");
				Conferir(NaJanela(aureola), "...e a cabeca e a aureola dele estao DENTRO da janela (a foto mostra o vilao, e nao o pe dele)", $"{aureola}");
				Conferir(TemNaTela("ESCAPOU DAS PROFUNDEZAS"), "o chat avisou: o vilao escapou das profundezas do Inferno");
				Conferir(TemNaTela("zeni") && TemNaTela("karma"), "...com o premio (zeni) e a honra (karma) de quem o derrotar");
				Fotografar("dinferno-4-vilao.png", "4. o Freeza de volta no Inferno, de aureola");
				// O CONTROLE: a medida de cima tem de saber REPROVAR. Seis tiles ao norte passam da borda (a janela mostra 3,75 acima de mim).
				Conferir(srv.AfastarNaFotoDoInferno(6), "CONTROLE: o vilao foi posto seis tiles ao norte, fora do quadro");
				Ir(PFora);
				break;
			}

			case PFora:
			{
				if (_t < 1.0) return;   // o snapshot leva o corpo
				CharacterVisual? v = mundo.CorpoDeTeste(_vilao)?.GetNodeOrNull<CharacterVisual>("Visual");
				Rect2? aureola = v != null ? AureolaNaTela(mundo, _vilao, v) : null;
				Conferir(aureola != null && !NaJanela(aureola),
					"CONTROLE: seis tiles ao norte, a aureola dele SAI da janela -- a medida enxerga o corte da foto 4 das primeiras rodadas", $"{aureola}");
				var (derrotado, zeni, karma) = srv.DerrotarNaFotoDoInferno(_eu);
				Conferir(derrotado, "o vilao foi DERROTADO (nocaute, pelo funil de producao)");
				Nota($"depois da derrota: {zeni:N0} zeni no bolso, karma {karma}");
				Ir(PDerrota);
				break;
			}

			case PDerrota:
				if (_t < 1.0) return;
				Conferir(TemNaTela("derrotou") && TemNaTela("o prêmio é seu"), "o chat diz quem derrotou -- e o premio caiu na carteira");
				Fotografar("dinferno-5-derrota.png", "5. o vilao derrotado: o premio no chat");
				Ir(PFim);
				break;

			default:
				if (_t < 2.0) return;
				Conferir(mundo.CorpoDeTeste(_vilao) == null, "o vilao derrotado afundou de volta: saiu da minha tela no tique seguinte");
				Fechar();
				break;
		}
	}

	private void Ir(int proximo) { _passo = proximo; _t = 0; }

	/// <summary>Cada alma desta foto esta no meu mundo, de pe e COM A AUREOLA desenhada INTEIRA dentro da janela.</summary>
	private void ConferirAsAlmas(World mundo, string onde)
	{
		int vistas = 0, comAureola = 0;
		string fora = "";
		foreach (int id in _almas)
		{
			CharacterVisual? v = mundo.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");
			if (v == null || !v.IsVisibleInTree()) continue;
			vistas++;
			Rect2? aureola = AureolaNaTela(mundo, id, v);
			if (v.AureolaVisivelDeTeste && NaJanela(aureola)) comAureola++;
			else fora += $" #{id} {aureola}";
		}
		Conferir(vistas == _almas.Count && vistas > 0, $"{onde}: as almas estao no meu mundo, de pe", $"{vistas} de {_almas.Count}");
		Conferir(comAureola == vistas && vistas > 0, $"{onde}: TODA alma tem a AUREOLA desenhada, INTEIRA dentro da janela",
			$"{comAureola} de {vistas};{fora}");
	}

	/// <summary>
	/// ============================ ONDE, NA TELA, A AUREOLA DESTE CORPO ESTA DESENHADA ============================
	/// A caixa de pixel ACESO da aureola (`CharacterVisual.CaixaDaAureolaDeTeste`, em coordenadas do `Visual`) levada pra tela pela conta
	/// da CAMERA -- a do `RoboDeMacaco.NaTela`, que foi MEDIDA: o corpo local fica no centro da janela, e o resto e a distancia desenhada
	/// vezes o zoom (la, o `GetGlobalTransformWithCanvas` devolveu posicao de MUNDO). Nulo sem aureola ou sem corpo.
	///
	/// POR QUE ELA EXISTE: o "na minha tela" desta bancada era so o `IsVisibleInTree` -- o boneco existe e nao esta escondido. As duas
	/// primeiras rodadas passaram com o Freeza no alto da foto 4, a cabeca e a aureola FORA do quadro.
	/// </summary>
	private Rect2? AureolaNaTela(World mundo, int id, CharacterVisual v)
	{
		if (C is not { } cli || v.CaixaDaAureolaDeTeste is not { } caixa
			|| mundo.PosicaoDesenhadaDe(id) is not { } ele || mundo.PosicaoDesenhadaDe(cli.LocalId) is not { } eu) return null;
		float zoom = Math.Max(1, mundo.ZoomDeTeste);
		Vector2 origem = (ele - eu) * zoom + GetViewport().GetVisibleRect().Size / 2f;
		return new Rect2(origem + caixa.Position * v.Scale * zoom, caixa.Size * v.Scale * zoom).Abs();
	}

	/// <summary>A caixa cabe INTEIRA na janela? Nula nao cabe.</summary>
	private bool NaJanela(Rect2? caixa) => caixa is { } c && GetViewport().GetVisibleRect().Encloses(c);

	/// <summary>ALGUM TEXTO VISIVEL DA TELA (o chat inclusive) TRAZ ESTE TRECHO? Sem diferenca de caixa.</summary>
	private bool TemNaTela(string trecho)
	{
		bool achou = false;
		void Olhar(Node no)
		{
			foreach (Node f in no.GetChildren())
			{
				if (achou) return;
				if (f is CanvasItem ci && !ci.IsVisibleInTree()) continue;
				string? texto = f switch { RichTextLabel r => r.GetParsedText(), Label l => l.Text, Button b => b.Text, _ => null };
				if (texto != null && texto.Contains(trecho, StringComparison.OrdinalIgnoreCase)) { achou = true; return; }
				Olhar(f);
			}
		}
		Olhar(GetTree().Root);
		return achou;
	}

	private void Fotografar(string nome, string rotulo)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"{rotulo}: a janela nao devolveu imagem"); return; }
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + nome);
			img.SavePng(caminho);
			_linhas.Add($"  foto   {rotulo}: {caminho}");
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	private void Fechar()
	{
		if (_acabou) return;
		_acabou = true;
		if (S is { } srv) srv.LimparAFotoDoInferno(_eu);

		GD.Print("\n[dinferno] ===== O OUTRO MUNDO POVOADO E O VILAO DO INFERNO, NA TELA =====");
		foreach (string l in _linhas) GD.Print("[dinferno] " + l);
		GD.Print(_falhas.Count == 0
			? "[dinferno] ===== TUDO OK ====="
			: $"[dinferno] ===== {_falhas.Count} FALHA(S) =====\n[dinferno]   " + string.Join("\n[dinferno]   ", _falhas));
		GetTree().Quit();
	}
}
