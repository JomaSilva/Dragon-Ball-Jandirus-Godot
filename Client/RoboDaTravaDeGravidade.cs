using Godot;
using Jandirus.Core.Tech;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A TRAVA DE SEGURANCA NA TELA (`--diaggravtrava`).
///
/// O menu da camara com a melhoria nova, e o aviso no chat depois que alguem desmaia no campo.
/// A conta da maestria e da trava ja e a `--gravtravateste`. Aqui so o que o jogador ve.
///
///     Godot --path . --host --rede 10111 --horateste 0.5 --campoteste 9 --semfauna --diaggravtrava \
///           --position 1920,0 --resolution 1280x720 --raca Human --conta bancada_gravtrava --nome Fulano
///
/// Fotos: `user://gravtrava-1-menu.png`, `user://gravtrava-2-aviso.png`.
/// </summary>
public partial class RoboDaTravaDeGravidade : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;
	private static MenuDeInteracao? E => MenuDeInteracao.Instancia;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ouvido = [];
	private bool _acabou, _ouvindo;
	private double _t, _vida;
	private int _passo, _sub, _maquina;

	public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

	public override void _ExitTree()
	{
		if (_ouvindo && C is { } cli) cli.Falou -= AoOuvir;
	}

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok     " : "  FALHA  ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --     " + oque);

	private void AoOuvir(Protocol.Fala canal, string autor, string texto)
	{
		if (canal == Protocol.Fala.Sistema) _ouvido.Add(texto);
	}

	private bool Ouvi(string trecho) => _ouvido.Exists(t => t.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private void Ir(int passo) { _passo = passo; _sub = 0; _t = 0; }

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli) return;
		if (S is not { } srv) { Conferir(false, "sem servidor no processo (`--diaggravtrava` precisa de `--host`)"); Fechar(); return; }

		_vida += delta;
		if (_vida > 90) { Conferir(false, $"a bancada coube em 90 s (parou no passo {_passo}.{_sub})"); Fechar(); return; }
		_t += delta;

		switch (_passo)
		{
			case 0: Assentar(srv, cli); break;
			case 1: AbrirOMenu(); break;
			case 2: ComprarEDesmaiar(srv, cli); break;
			default: Fechar(); break;
		}
	}

	private void Assentar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_sub == 0)
		{
			if (_t < 3) return;
			(bool ko, bool morto, _, _, _) = srv.EstadoDaVariedade(cli.LocalId);
			if ((ko || morto) && _t < 25) return;
			Conferir(!ko && !morto, "o corpo entrou em pe");
			bool praca = srv.AssentarNaPracaDasMaquinas(cli.LocalId, 4);
			Conferir(praca, "achei chao livre pra camara");
			if (!praca) { Fechar(); return; }
			srv.MaestriaDeGravidadeNaFotoDasMaquinas(cli.LocalId, 80);
			srv.ZeniDaFotoDaTrava(cli.LocalId, 2_000_000, 0);
			_sub = 1;
			_t = 0;
			return;
		}
		if (_t < 1.2) return;
		_maquina = srv.MaquinaDaFoto(cli.LocalId, "Gravity", 1, 0);
		Conferir(_maquina != 0, "a camara assentou ao lado");
		if (_maquina == 0) { Fechar(); return; }
		if (!_ouvindo) { cli.Falou += AoOuvir; _ouvindo = true; }
		Ir(1);
	}

	private void ApertarE()
	{
		var ev = new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = true };
		GetViewport().PushInput(ev);
	}

	private bool Apertar(string rotulo) => E?.ApertarDesenhado(rotulo) == true;

	private void AbrirOMenu()
	{
		if (_sub == 0)
		{
			if (E is not { NaTela: true }) { if (_t > 0.4) { ApertarE(); _t = 0; } return; }
			if (!E.BotoesDesenhados().Contains("Melhorar..."))
			{
				if (_t > 6) { Conferir(false, $"o menu da camara nao abriu: {string.Join(" | ", E.BotoesDesenhados())}"); Fechar(); }
				return;
			}
			Apertar("Melhorar...");
			_sub = 1;
			_t = 0;
			return;
		}
		if (E is not { NaTela: true }) { if (_t > 4) { Conferir(false, "Melhorar... fechou o menu"); Fechar(); } return; }
		bool tem = E.BotoesDesenhados().Contains("Trava de segurança");
		// a foto espera o preco chegar ao chat e o menu ser desenhado (a imagem e a do ultimo quadro pronto)
		bool preco = Ouvi("trava de segurança: ");
		if ((!tem || !preco || _t < 0.5) && _t < 3) return;
		Conferir(tem, $"Melhorar... lista a Trava de segurança: {string.Join(" | ", E.BotoesDesenhados())}");
		Conferir(preco, $"...e o chat diz o preco dela nas duas moedas: {string.Join(" | ", _ouvido)}");
		GuardarATela("gravtrava-1-menu.png", "1. o menu da camara com a trava de seguranca");
		if (!tem) { Fechar(); return; }
		Apertar("Trava de segurança");
		Ir(2);
	}

	private void ComprarEDesmaiar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_sub == 0)
		{
			if (E is { NaTela: true } && E.BotoesDesenhados().Contains("Trava de segurança: pagar com zeni"))
			{
				// (o chat ja tem "trava de seguranca" da lista de precos: a compra se reconhece pela fala dela, ouvida do zero)
				_ouvido.Clear();
				Apertar("Trava de segurança: pagar com zeni");
				_sub = 1;
				_t = 0;
			}
			else if (_t > 4) { Conferir(false, "a pagina de pagar a trava nao abriu"); Fechar(); }
			return;
		}
		if (_sub == 1)
		{
			// A COMPRA CHEGA AO CLIENTE PELO PACOTE DE CONSTRUCOES (o byte da maquina): so depois dele o desmaio prova a trava.
			bool comprou = Ouvi("trava de segurança instalada")
						   && cli.Obras.Any(o => o.Id == _maquina && (o.Feitas & Interacoes.TravaDeSegurancaFeita) != 0);
			if (!comprou && _t < 4) return;
			Conferir(comprou, $"a compra saiu pelo menu (1.000.000 de zeni), e o cliente sabe que ESTA camara tem a trava: {string.Join(" | ", _ouvido)}");
			if (!comprou) { Fechar(); return; }
			bool ligou = srv.CampoLigadoNaFotoDaTrava(_maquina, 10);
			Conferir(ligou, "o campo ligou em 10x, com a trava ja comprada");
			_sub = 2;
			_t = 0;
			return;
		}
		if (_sub == 2)
		{
			if (_t < 1) return;   // o "campo te pressiona" no chat, antes do desmaio
			_ouvido.Clear();
			Conferir(srv.DesmaiarNaFotoDaTrava(cli.LocalId), "desmaiei dentro do campo (o funil do nocaute)");
			_sub = 3;
			_t = 0;
			return;
		}
		if (_sub == 3)
		{
			if (!Ouvi("desligou a gravidade") && _t < 3) return;
			(_, _, double grav, _, _, _, _) = srv.CampoNaFoto(_maquina);
			Conferir(grav == 0, $"a gravidade da camara foi a 0 (esta em {grav:0.##})");
			Conferir(Ouvi("A trava de segurança desligou a gravidade: Fulano desmaiou."), $"o aviso no chat: {string.Join(" | ", _ouvido)}");
			_sub = 4;
			_t = 0;
			return;
		}
		if (_t < 0.6) return;   // a linha do chat desenhada antes da foto
		GuardarATela("gravtrava-2-aviso.png", "2. o aviso no chat depois do desmaio");
		Fechar();
	}

	private void GuardarATela(string nome, string legenda)
	{
		Image? tela = GetViewport()?.GetTexture()?.GetImage();
		if (tela == null || tela.IsEmpty()) { Nota($"{legenda}: a janela nao devolveu imagem"); return; }
		tela.Convert(Image.Format.Rgba8);
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + nome);
			tela.SavePng(caminho);
			_linhas.Add($"  foto   {legenda}: {caminho}");
		}
		catch (Exception e) { Nota($"{legenda}: sem foto: {e.Message}"); }
	}

	private void Fechar()
	{
		if (_acabou) return;
		_acabou = true;
		if (_ouvindo && C is { } cli) cli.Falou -= AoOuvir;
		if (E is { NaTela: true }) ApertarE();
		S?.LimparAFotoDasMaquinas();
		GD.Print("\n[dgravtrava] ===== A TRAVA DE SEGURANCA NA TELA =====");
		foreach (string l in _linhas) GD.Print("[dgravtrava] " + l);
		GD.Print(_falhas.Count == 0
			? "[dgravtrava] ===== TUDO OK ====="
			: $"[dgravtrava] ===== {_falhas.Count} FALHA(S) =====");
		GetTree().Quit();
	}
}
