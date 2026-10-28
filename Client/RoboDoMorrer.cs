using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ O BOTAO MORRER NA TELA (`--diagmorrer`, 2026-10-27, o pedido 124d) ============================
/// A `--snakewayteste` prova a morte pelo servidor e nao enxerga a tela. Este robo aciona o verbo "Morrer" do menu P > Other pelo mesmo
/// caminho do clique (o `Acionar` do verbo registrado) e confere, pela tela de verdade (`TelaDeMorrer`) e pelo servidor do mesmo processo:
///   1. a CONFIRMACAO abre, com o foco no "Cancelar" -- e o corpo segue vivo (foto);
///   2. o "Cancelar" fecha sem matar;
///   3. de novo, e o "Morrer": o servidor mata, e a caixa fecha (foto).
///
/// COMO RODAR -- com JANELA (no headless o `GetImage` volta vazio):
///
///     Godot --path . --host --rede 11852 --semfauna --diagmorrer --resolution 1280x720 --raca Human --conta bancada_dmorrer --nome Finado
/// </summary>
public partial class RoboDoMorrer : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	private const double Paciencia = 90;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private double _t, _vida;
	private int _passo;

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
		if (S is not { } srv) { if (_vida > 20) { Nota("sem servidor no processo (`--diagmorrer` precisa de `--host`)"); Fechar(); } return; }
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo || !IsInstanceValid(mundo)) return;
		if (TelaDeMorrer.Instancia is not { } tela) return;

		switch (_passo)
		{
			case 0:
				if (_t < 4) return;   // o mundo monta e o chat assenta
				Conferir(AcionarOVerbo(), "o verbo \"Morrer\" esta no menu P > Other (registrado)");
				Ir(1);
				break;

			case 1:
				if (_t < 0.8) return;
				Fotografar("morrer-1-confirmacao.png", "1. a confirmacao do Morrer aberta");
				Conferir(tela.NaTela && tela.CancelarComFocoDeTeste, "o verbo abre a CONFIRMACAO, com o foco no Cancelar",
						 $"na tela {tela.NaTela}, foco no Cancelar {tela.CancelarComFocoDeTeste}");
				Conferir(!srv.MortoDeTeste(cli.LocalId), "...e o corpo segue vivo");
				tela.CancelarDeTeste();
				Ir(2);
				break;

			case 2:
				if (_t < 0.8) return;
				Conferir(!tela.NaTela && !srv.MortoDeTeste(cli.LocalId), "o CANCELAR fecha sem matar");
				AcionarOVerbo();
				Ir(3);
				break;

			case 3:
				if (_t < 0.8) return;
				Conferir(tela.NaTela, "PRECONDICAO: a confirmacao abriu de novo");
				tela.MorrerDeTeste();
				Ir(4);
				break;

			case 4:
				if (_t < 1.0) return;
				Fotografar("morrer-2-morto.png", "2. o Morrer confirmado: o corpo cai");
				Conferir(!tela.NaTela && srv.MortoDeTeste(cli.LocalId), "o MORRER confirmado mata (pelo servidor) e a caixa fecha");
				Fechar();
				break;
		}
	}

	private static bool AcionarOVerbo()
	{
		Verbo? v = Verbos.Todos.FirstOrDefault(x => x.Nome == "Morrer" && x.Categoria == Verbos.Outros);
		v?.Acionar?.Invoke();
		return v != null;
	}

	private void Ir(int proximo) { _passo = proximo; _t = 0; }

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
		GD.Print("\n[dmorrer] ===== O BOTAO MORRER NA TELA (o pedido 124d) =====");
		foreach (string l in _linhas) GD.Print("[dmorrer] " + l);
		GD.Print(_falhas.Count == 0
			? "[dmorrer] ===== TUDO OK ====="
			: $"[dmorrer] ===== {_falhas.Count} FALHA(S) =====\n[dmorrer]   " + string.Join("\n[dmorrer]   ", _falhas));
		GetTree().Quit();
	}
}
