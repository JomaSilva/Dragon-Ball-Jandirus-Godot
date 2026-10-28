using Godot;
using Jandirus.Core.Tech;

namespace Jandirus.Client;

/// <summary>
/// ============================ OS MINIJOGOS DE ESTUDO NA TELA (`--diagestudo`, 2026-10-27, o pedido 120) ============================
/// A `--estudoteste` prova as regras pelo servidor e nao enxerga a tela. Esta bancada estuda numa Research Station de verdade (posta ao
/// lado pelas portas do servidor no mesmo processo, `GameServer.FotoDoEstudo.cs`) e fotografa cada minijogo pela tela de verdade
/// (`TelaDoEstudo`), respondendo pelo mesmo caminho do botao:
///   1. a CONTA aberta, e respondida certo -- "Certo!" e o bonus de +25%;
///   2. o SIMBOLO do elemento aberto, e respondido errado -- "Errado." e o bonus de volta a zero;
///   3. a MOLECULA (o CO2): as bolinhas na mesa, depois ligadas com duas duplas pelo clique do quadro, e o "Pronto" -- "Certo!".
/// (2026-10-27, o pedido 123) E antes de responder o SIMBOLO, as letras T, E, M e P -- cada uma com atalho no jogo (treinar, interagir,
/// meditar, menu) -- DIGITADAS com teclas de verdade: tem que ir pra resposta ("temp"), e nenhuma tela de atalho pode abrir.
///
/// COMO RODAR -- com JANELA (no headless o `GetImage` volta vazio):
///
///     Godot --path . --host --rede 11762 --semfauna --diagestudo --resolution 1280x720 --raca Human --conta bancada_destudo --nome Estudioso
/// </summary>
public partial class RoboDoEstudo : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	private const double Paciencia = 120;
	private const double Assentar = 1.5;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private double _t, _vida;
	private int _passo, _eu;

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		_linhas.Add((ok ? "  ok     " : "  FALHA  ") + oque + (detalhe.Length == 0 ? "" : $"   [{detalhe}]"));
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --     " + oque);

	public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		_vida += delta;
		_t += delta;
		if (_vida > Paciencia) { Conferir(false, $"a bancada coube na paciencia ({Paciencia:0} s; parou no passo {_passo})"); Fechar(); return; }
		if (S is not { } srv) { if (_vida > 20) { Nota("sem servidor no processo (`--diagestudo` precisa de `--host`)"); Fechar(); } return; }
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo || !IsInstanceValid(mundo)) return;
		if (TelaDoEstudo.Instancia is not { } tela) return;

		switch (_passo)
		{
			case 0:
				if (_t < 4) return;   // o mundo monta e o chat assenta
				_eu = cli.LocalId;
				srv.PrepararEstudoNaFoto(_eu);
				srv.AbrirMinijogoNaFoto(_eu, TipoDeMinijogo.Conta);
				Ir(1);
				break;

			case 1:
				if (_t < Assentar) return;
				Fotografar("estudo-1-conta.png", "1. a conta aberta");
				Conferir(tela.TipoNaTela == TipoDeMinijogo.Conta, "a tela abriu a CONTA", $"tela: {tela.TipoNaTela}; \"{tela.PerguntaDeTeste}\"");
				Conferir(cli.Estudo.Expressao.Length > 0 && tela.PerguntaDeTeste.Contains(cli.Estudo.Expressao),
						 "...com a conta do servidor no enunciado", cli.Estudo.Expressao);
				tela.ResponderDeTeste(srv.RespostaNaFoto(_eu));
				Ir(2);
				break;

			case 2:
				if (_t < 0.6) return;
				Fotografar("estudo-2-certo.png", "2. respondida certo: \"Certo!\" e o bonus de +25%");
				Conferir(srv.BonusNaFoto(_eu) == 1 && cli.Estudo.Bonus == 1 && cli.Estudo.Resultado == 1,
						 "o acerto deu UM bonus (+25%), e a tela sabe", $"servidor {srv.BonusNaFoto(_eu)}, tela {cli.Estudo.Bonus}");
				Ir(3);
				break;

			case 3:
				if (_t < 2.0) return;   // a janela do resultado some
				srv.AbrirMinijogoNaFoto(_eu, TipoDeMinijogo.Elemento);
				Ir(4);
				break;

			case 4:
				if (_t < Assentar) return;
				Fotografar("estudo-3-elemento.png", "3. o simbolo do elemento aberto");
				Conferir(tela.TipoNaTela == TipoDeMinijogo.Elemento, "a tela abriu o SIMBOLO", $"\"{tela.PerguntaDeTeste}\"");
				var e = Minijogos.Elemento(cli.Estudo.Indice);
				Conferir(tela.PerguntaDeTeste.Contains(e.Nome) || tela.PerguntaDeTeste.Contains(e.NomeEn), "...com o nome do elemento no enunciado", e.Nome);
				// (o pedido 123) AS LETRAS DE ATALHO DIGITADAS NA RESPOSTA, com teclas de verdade (ver `Tecla`)
				Conferir(tela.CampoComFocoDeTeste && Foco.Digitando, "o campo da resposta tem o foco, e o jogo sabe que se digita (`Foco.Digitando`)",
						 $"foco no campo {tela.CampoComFocoDeTeste}, Foco.Digitando {Foco.Digitando}");
				foreach (Key k in LetrasDeAtalho) Tecla(k, true);
				Ir(41);
				break;

			case 41:
				foreach (Key k in LetrasDeAtalho) Tecla(k, false);
				Ir(42);
				break;

			case 42:
				if (_t < 1.0) return;   // os atalhos que disparassem tinham dado as telas deles
				Fotografar("estudo-3b-digitado.png", "3b. as letras de atalho (T, E, M, P) digitadas na resposta");
				string abriu = string.Join(", ", new[]
				{
					TelaDeTreino.Instancia?.NaTela == true ? "o treino (T)" : null,
					MenuDeInteracao.Instancia?.NaTela == true ? "a interacao (E)" : null,
					TelaDeMeditacao.Instancia?.NaTela == true ? "a meditacao (M)" : null,
					MenuJogo.Instancia is { Visible: true } ? "o menu (P)" : null,
				}.Where(s => s != null));
				Conferir(tela.RespostaNoCampoDeTeste == "temp", "as letras de atalho foram pra RESPOSTA", $"\"{tela.RespostaNoCampoDeTeste}\"");
				Conferir(abriu.Length == 0, "...e nenhuma tela de atalho abriu", abriu.Length == 0 ? "nenhuma" : abriu);
				tela.ResponderDeTeste("Xx");
				Ir(5);
				break;

			case 5:
				if (_t < 0.6) return;
				Fotografar("estudo-4-errado.png", "4. respondido errado: \"Errado.\" e o bonus de volta a zero");
				Conferir(srv.BonusNaFoto(_eu) == 0 && cli.Estudo.Resultado == 2, "o erro tirou o bonus (zero valendo)", $"servidor {srv.BonusNaFoto(_eu)}");
				Ir(6);
				break;

			case 6:
				if (_t < 2.0) return;
				srv.AbrirMinijogoNaFoto(_eu, TipoDeMinijogo.Molecula, "CO2");
				Ir(7);
				break;

			case 7:
				if (_t < Assentar) return;
				Fotografar("estudo-5-molecula.png", "5. a molecula do CO2: as bolinhas soltas na mesa");
				Conferir(tela.TipoNaTela == TipoDeMinijogo.Molecula && tela.PerguntaDeTeste.Contains("CO2"), "a tela abriu a MOLECULA do CO2",
						 $"\"{tela.PerguntaDeTeste}\"");
				// as duas duplas pelo mesmo caminho do clique: o carbono (0) com cada oxigenio (1 e 2), duas vezes cada
				tela.QuadroDeTeste.Ligar(0, 1);
				tela.QuadroDeTeste.Ligar(0, 1);
				tela.QuadroDeTeste.Ligar(0, 2);
				tela.QuadroDeTeste.Ligar(0, 2);
				Ir(8);
				break;

			case 8:
				if (_t < 0.5) return;
				Fotografar("estudo-6-molecula-montada.png", "6. o CO2 montado: O=C=O, as duas duplas desenhadas");
				Conferir(tela.QuadroDeTeste.Ligacoes().Sum(l => l.Ordem) == 4, "o quadro tem as duas DUPLAS", Minijogos.EscreverLigacoes(tela.QuadroDeTeste.Ligacoes()));
				tela.ProntoDeTeste();
				Ir(9);
				break;

			case 9:
				if (_t < 0.6) return;
				Fotografar("estudo-7-molecula-certa.png", "7. o \"Pronto\": \"Certo!\"");
				Conferir(srv.BonusNaFoto(_eu) == 1 && cli.Estudo.Resultado == 1, "a molecula FECHOU: um bonus de novo", $"servidor {srv.BonusNaFoto(_eu)}");
				Fechar();
				break;
		}
	}

	private void Ir(int proximo) { _passo = proximo; _t = 0; }

	/// <summary>(o pedido 123) As letras que o robo digita na resposta: cada uma tem atalho no jogo -- treinar, interagir, meditar, menu.</summary>
	private static readonly Key[] LetrasDeAtalho = [Key.T, Key.E, Key.M, Key.P];

	/// <summary>
	/// Uma tecla de verdade, como o teclado manda: o `Unicode` e o que o campo escreve; o `PhysicalKeycode` e o que o `InputMap` dos
	/// atalhos casa, e o `Keycode` o que o chat e o menu leem (a nota da `--diagmudez`).
	/// </summary>
	private static void Tecla(Key k, bool apertada) =>
		Input.ParseInputEvent(new InputEventKey
		{
			Keycode = k, PhysicalKeycode = k, Pressed = apertada, Unicode = apertada ? char.ToLowerInvariant((char)k) : 0,
		});

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
		GD.Print("\n[destudo] ===== OS MINIJOGOS DE ESTUDO NA TELA (o pedido 120) =====");
		foreach (string l in _linhas) GD.Print("[destudo] " + l);
		GD.Print(_falhas.Count == 0
			? "[destudo] ===== TUDO OK ====="
			: $"[destudo] ===== {_falhas.Count} FALHA(S) =====\n[destudo]   " + string.Join("\n[destudo]   ", _falhas));
		GetTree().Quit();
	}
}
