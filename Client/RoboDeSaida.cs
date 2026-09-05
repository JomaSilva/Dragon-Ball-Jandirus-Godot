using System.Linq;
using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A SAIDA PELO LOBBY (`--diagsaida`) ============================
/// O relato do dono (2026-09-04): *"por algum motivo fechar o jogo no menu/lobby o jogo crasha ao
/// inves de fechar"*. O log dele termina em `[saida] fechando o jogo (botão Fechar o jogo)`,
/// `[server] parado`, `[client] desconectado` -- e nada depois: o que quebra vem DEPOIS da limpeza,
/// no `Quit()` e na descida da arvore, que nenhum log de dentro do jogo ve.
///
/// Por isso o JUIZ desta bancada e de fora: o CODIGO DE SAIDA do processo e o que sobrou no stderr
/// (`rodar-saida.ps1`). Um jogo que fecha bem sai com 0 e sem `Unhandled`/`crash` no stderr; e so
/// isso que se mede. O robo faz o caminho do dono, com os botoes de verdade (nao ha atalho por
/// dentro: `Saida.Encerrar` e o que o botao chama, entao apertar o botao e chamar a funcao sao a
/// mesma coisa -- o que importa e o ESTADO em que ela e chamada, e esse so o caminho inteiro monta):
///
///   1. hospeda e entra (o `--host --nome` do wrapper);
///   2. joga por dois segundos (o mundo inteiro de pe: corpo, som, voz, clima);
///   3. ESC -> "Desconectar" (duas vezes, porque hospeda) -> lobby;
///   4. no lobby, "Fechar o jogo" -- pelo menu de pausa se ele existir la, senao pelo mesmo
///      `Saida.Encerrar` que o botao chama.
///
/// Sobrevive a volta ao lobby pela mesma receita do `RoboDeDecalque`: se muda pra raiz da arvore
/// antes de apertar "Desconectar" (o `Boot.VoltarAoLogin` derruba os filhos dele).
/// ==================================================================================
/// </summary>
public partial class RoboDeSaida : Node
{
	private enum Fase { Entrando, Jogando, AbrirPausa, EsperarLobby, Fechar, Fim }

	private Fase _fase = Fase.Entrando;
	private double _t, _relogio;
	private int _tentativas;
	private bool _falou;
	private Node? _lobby;

	/// <summary>Quantos segundos jogar antes de sair -- `--saidajogar N` (3 por padrao; 14 no cenario com um segundo cliente).</summary>
	private double _segundosDeJogo = 3.0;

	/// <summary>
	/// `--saidadireta`: fecha DE DENTRO DO JOGO, pelo "Fechar o jogo" do menu de pausa, sem passar pelo
	/// lobby. E o caminho do log do dono: nao ha "Fulano saiu" antes do `[saida]` -- o mundo inteiro
	/// (corpo, microfone, vozes, decalques, cadaveres) ainda esta de pe quando o `Quit()` desce a arvore.
	/// </summary>
	private bool _direta;

	/// <summary>
	/// `--saidamatar <nome>`: aos 6 s de jogo o host (admin) MATA o outro corpo pelo nome -- o cadaver
	/// fica no chao, a alma viaja aos 2 s e o Outro Mundo abre. E o estado do log do dono na hora do
	/// "Fechar o jogo": um morto que viajou, um cadaver, pecas no chao.
	/// </summary>
	private string _matar = "";
	private bool _matei;

	/// <summary>
	/// `--saidalobby <s>`: NAO entra no jogo. Fica no lobby e fecha pelo X (`Saida.Encerrar` com o mesmo
	/// motivo que o `Boot._Notification` usa) `s` segundos depois de subir -- 0,5 s pega o meio do
	/// `[aquece]`, que e onde os tres logs curtos do dono (17:51:04, :29, :43) pararam: o X foi apertado
	/// antes de o aquecimento terminar, e o processo nunca mais respondeu (AppHangB1 no Event Log).
	/// </summary>
	private double _lobbyEm = -1;

	/// <summary>
	/// `--saidafoto <arquivo.png>`: aos 5 s de jogo tira uma FOTO da tela (`user://arquivo.png`) e
	/// segue o roteiro. Serve pra olhar um lugar com o olho -- com `--saidamatar <eu>` o proprio robo
	/// morre, viaja em 2 s e a foto sai do Outro Mundo, na mesa do Enma (a camada do juiz x o trono).
	/// </summary>
	private string _foto = "";
	private bool _fotografei, _diante;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		string[] args = OS.GetCmdlineArgs();
		int i = System.Array.IndexOf(args, "--saidajogar");
		if (i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out double sj) && sj > 0)
			_segundosDeJogo = sj;
		_direta = System.Array.IndexOf(args, "--saidadireta") >= 0;
		int m = System.Array.IndexOf(args, "--saidamatar");
		if (m >= 0 && m + 1 < args.Length) _matar = args[m + 1];
		int ft = System.Array.IndexOf(args, "--saidafoto");
		if (ft >= 0 && ft + 1 < args.Length) _foto = args[ft + 1];
		int lb = System.Array.IndexOf(args, "--saidalobby");
		if (lb >= 0 && lb + 1 < args.Length && double.TryParse(args[lb + 1], System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out double sl) && sl >= 0)
			_lobbyEm = sl;
		GD.Print($"[saida-robo] no ar: entrar, jogar {_segundosDeJogo:0.#} s, Desconectar, e Fechar o jogo no lobby");
		CallDeferred(nameof(Emancipar));
	}

	private void Emancipar()
	{
		if (GetParent() is not { } pai || GetTree() is not { } arv || pai == arv.Root) return;
		Node raiz = arv.Root;
		_lobby = pai;
		pai.RemoveChild(this);
		raiz.AddChild(this);
	}

	public override void _Process(double delta)
	{
		_relogio += delta;
		_t += delta;
		if (_lobbyEm < 0 && _t < 0.5) return;
		_t = 0;

		if (_relogio > 90 && _fase != Fase.Fim)
		{
			GD.PrintErr($"[saida-robo] FALHA: 90 s e a rodada nao chegou ao fim (fase {_fase})");
			_fase = Fase.Fim;
			Saida.Encerrar(GetTree(), "robo --diagsaida, por prazo");
			return;
		}

		if (_lobbyEm >= 0)
		{
			if (_fase == Fase.Fim || _relogio < _lobbyEm) return;
			_fase = Fase.Fim;
			GD.Print($"[saida-robo] t={_relogio:0.0}s  X NO LOBBY, sem entrar no jogo (aquecimento {(Aquecimento.Terminou ? "ja terminado" : "AINDA RODANDO")})");
			Saida.Encerrar(GetTree(), "o X da janela");
			return;
		}

		GameClient? cli = GameClient.Instance;
		switch (_fase)
		{
			case Fase.Entrando:
				if (World.Instancia == null || cli is not { Connected: true }) return;
				GD.Print($"[saida-robo] t={_relogio:0.0}s  no mundo (id {cli.LocalId})");
				_fase = Fase.Jogando;
				_relogio = 0;
				break;

			case Fase.Jogando:
				// FALA: segura a tecla de voz o jogo inteiro. O dono joga com voz, e o microfone aberto
				// (`AudioStreamMicrophone`) e o suspeito classico de travar a saida no Windows.
				if (!_falou) { _falou = true; Input.ActionPress("falar_voz"); GD.Print($"[saida-robo] t={_relogio:0.0}s  segurando a tecla de voz (microfone aberto)"); }
				if (_matar.Length > 0 && !_matei && _relogio >= 6.0)
				{
					_matei = true;
					cli.SendVerbo("admin_matar", _matar);
					GD.Print($"[saida-robo] t={_relogio:0.0}s  pedi `admin_matar {_matar}` (cadaver + viagem ao Outro Mundo antes de fechar)");
				}
				// Com `--saidamatar` a foto espera a VIAGEM (morte aos 6 s + 2 s de cadaver + a chegada) e
				// o corpo e posto DIANTE DO TRONO pelo servidor (a chegada e o checkpoint, 30 tiles longe
				// da cadeira -- andar ate la e o que uma pessoa faria, mas a foto quer o enquadramento).
				if (_foto.Length > 0 && _matar.Length > 0 && !_fotografei && !_diante && _relogio >= 10.0)
				{
					_diante = true;
					bool ok = Jandirus.Server.GameServer.Instance?.PorDianteDoEnmaDeTeste(cli.LocalId) == true;
					GD.Print($"[saida-robo] t={_relogio:0.0}s  diante do trono: {ok}");
				}
				if (_foto.Length > 0 && !_fotografei && _relogio >= (_matar.Length > 0 ? 12.0 : 5.0))
				{
					_fotografei = true;
					Godot.Image img = GetViewport().GetTexture().GetImage();
					string caminho = "user://" + _foto;
					img.SavePng(caminho);
					GD.Print($"[saida-robo] t={_relogio:0.0}s  FOTO {ProjectSettings.GlobalizePath(caminho)} ({img.GetWidth()}x{img.GetHeight()})");
				}
				if (_relogio < _segundosDeJogo) return;
				_fase = Fase.AbrirPausa;
				_tentativas = 0;
				break;

			case Fase.AbrirPausa:
				if (PauseMenu.Instancia is not { Aberto: true })
				{
					if (++_tentativas > 8) { GD.PrintErr("[saida-robo] FALHA: o ESC nunca abriu o menu de pausa"); _fase = Fase.Fechar; return; }
					Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
					Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = false });
					return;
				}
				if (_direta)
				{
					GD.Print($"[saida-robo] t={_relogio:0.0}s  FECHANDO O JOGO de dentro do mundo (menu de pausa)");
					_fase = Fase.Fim;
					if (!Apertar("Fechar o jogo")) { GD.PrintErr("[saida-robo] FALHA: o menu de pausa nao tem o botao Fechar o jogo"); Saida.Encerrar(GetTree(), "robo --saidadireta (sem botao)"); }
					return;
				}
				if (!Apertar("Desconectar")) { GD.PrintErr("[saida-robo] FALHA: o menu de pausa nao tem o botao Desconectar"); _fase = Fase.Fechar; return; }
				// quem hospeda clica duas vezes (o primeiro clique so avisa que derruba o servidor)
				if (Jandirus.Server.GameServer.Instance is { Running: true }) Apertar("Desconectar");
				GD.Print($"[saida-robo] t={_relogio:0.0}s  apertei Desconectar");
				_tentativas = 0;
				_fase = Fase.EsperarLobby;
				break;

			case Fase.EsperarLobby:
				if (World.Instancia != null || cli is { Connected: true })
				{
					if (++_tentativas > 12) { GD.PrintErr("[saida-robo] FALHA: o mundo nao caiu depois do Desconectar"); _fase = Fase.Fechar; }
					return;
				}
				GD.Print($"[saida-robo] t={_relogio:0.0}s  no lobby (World nulo, cliente desconectado)");
				_tentativas = 0;
				_fase = Fase.Fechar;
				break;

			case Fase.Fechar:
				// Um segundo no lobby, como uma pessoa faria; depois o botao -- ou a funcao que ele chama.
				if (++_tentativas < 2) return;
				GD.Print($"[saida-robo] t={_relogio:0.0}s  FECHANDO O JOGO pelo lobby");
				_fase = Fase.Fim;
				if (!Apertar("Fechar o jogo")) Saida.Encerrar(GetTree(), "robo --diagsaida (sem botao no lobby: a mesma funcao do botao)");
				break;
		}
	}

	private Button? Botao(string texto) =>
		Todos<Button>(_lobby ?? GetTree()?.Root).FirstOrDefault(b => b.Text == texto && b.IsVisibleInTree());

	private bool Apertar(string texto)
	{
		if (Botao(texto) is not { } b) return false;
		b.EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}

	private static System.Collections.Generic.IEnumerable<T> Todos<T>(Node? raiz) where T : Node
	{
		if (raiz == null) yield break;
		foreach (Node f in raiz.GetChildren())
		{
			if (f is T t) yield return t;
			foreach (T neto in Todos<T>(f)) yield return neto;
		}
	}
}
