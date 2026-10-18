using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA TRAVA DE ALVO (`--diagtrava`, 2026-10-17). O dono: *"adicione um botao q ao apertar vc da lock in no player ou npc
/// mais perto de vc, bom pra enfrentar boss q se move mt"*.
///
/// Roda com `--host` (o servidor e deste processo), headless, no campo aberto da bancada (`--campoteste`) e sem fauna. Tres corpos
/// de pe nascem ao lado do host pelo gancho da foto dos corpos -- a 5, 3,5 e 2 tiles, nesta ordem --, e a TECLA e apertada de
/// verdade: `Input.ActionPress` da acao `travar_alvo`, lida pelo `LocalPlayer` no MESMO quadro (o robo processa antes dele, a
/// licao do `RoboDeVoo`). O alvo e conferido nas duas pontas: o `GameClient.AlvoId` e o do servidor.
///
///   1. a tecla de fabrica e o L, e ela esta no `InputMap`;
///   2. o 1o toque trava no MAIS PERTO; o 2o, no seguinte; o 3o, no mais longe; o 4o da a volta;
///   3. o mais perto MORRE (o cadaver fica deitado no lugar, com outro id): o toque pula o caido e trava no de pe mais perto;
///   4. CONTRA-EXEMPLO (`World.TravaDoMaisLongeDeTeste`): com a fila ao contrario, o primeiro toque pega o mais longe.
///
///     Godot --path . --headless --host --rede 8447 --campoteste 8 --semfauna --diagtrava --raca Human --conta bancada_dtrava --nome Travador
/// </summary>
public partial class RoboDaTrava : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou, _soltar;
	private int _passo;
	private double _t, _espera;
	private int _longe, _meio, _perto, _obrasAntes = -1;

	/// <summary>ANTES do `LocalPlayer` no quadro: o `IsActionJustPressed` so vale no quadro do aperto, e um robo que processa
	/// depois do corpo aperta uma tecla que ninguem le (ver `RoboDeVoo`).</summary>
	public override void _Ready() => ProcessPriority = -100;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Passar() { _passo++; _t = 0; }

	private void Apertar()
	{
		Input.ActionPress(Teclas.NomeNoMotor("travar_alvo"));
		_soltar = true;
	}

	/// <summary>O pacote do alvo vai pelo fio ate o servidor deste processo: um pouco de folga antes de perguntar pra ele.</summary>
	private bool Esperou() => _t >= 0.4;

	private void ConferirAlvo(GameClient cli, Jandirus.Server.GameServer srv, int esperado, string oque)
	{
		int noServidor = srv.AlvoNaFotoDeCorpos(cli.LocalId);
		Conferir(cli.AlvoId == esperado && noServidor == esperado, $"{oque} (cliente {cli.AlvoId}, servidor {noServidor}, esperado {esperado})");
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		// A TECLA SOLTA NO QUADRO SEGUINTE ao aperto.
		if (_soltar) { Input.ActionRelease(Teclas.NomeNoMotor("travar_alvo")); _soltar = false; }
		if (GameClient.Instance is not { Connected: true, LocalId: > 0 } cli || World.Instancia is not { PosicaoLocalDeTeste: not null } mundo
			|| Jandirus.Server.GameServer.Instance is not Jandirus.Server.GameServer srv)
		{
			_espera += delta;
			if (_espera > 30) { Conferir(false, "em 30 s o cliente conectou, o mundo nasceu e o servidor esta neste processo"); Fechar(null); }
			return;
		}
		_t += delta;
		const float T = ZoneCollision.TileSize;
		switch (_passo)
		{
			case 0:
				GD.Print("[dtrava] ===== A TECLA DE TRAVAR O ALVO NO MAIS PERTO (dono, 2026-10-17) =====");
				Conferir(Teclas.NomeDaAcao("travar_alvo") == "L", $"a tecla de fabrica e o L (\"{Teclas.NomeDaAcao("travar_alvo")}\")");
				Conferir(InputMap.HasAction(Teclas.NomeNoMotor("travar_alvo")), "...e ela esta no `InputMap` (e de la que o `LocalPlayer` a le)");
				_obrasAntes = srv.ObrasAgoraNaFotoDeCorpos();
				_longe = srv.ForjarParaAFotoDeCorpos(cli.LocalId, new Vec2(5 * T, 0), "Longe", 50_000);
				_meio = srv.ForjarParaAFotoDeCorpos(cli.LocalId, new Vec2(0, 3.5f * T), "Meio", 50_000);
				_perto = srv.ForjarParaAFotoDeCorpos(cli.LocalId, new Vec2(-2 * T, 0), "Perto", 50_000);
				Conferir(_longe != 0 && _meio != 0 && _perto != 0, "tres corpos de pe ao meu lado: a 5, 3,5 e 2 tiles");
				int outros = srv.OutrosPertoNaFotoDeCorpos(cli.LocalId, 6, _longe, _meio, _perto);
				Conferir(outros == 0, $"PRECONDICAO: mais ninguem de pe a menos de 6 tiles (o mais perto tem que ser um dos tres) -- {outros} de fora");
				Passar();
				break;
			case 1:
				// OS TRES DESENHADOS NA MINHA TELA: a trava escolhe pelo que o CLIENTE ve.
				bool tres = mundo.CorpoDeTeste(_longe) != null && mundo.CorpoDeTeste(_meio) != null && mundo.CorpoDeTeste(_perto) != null;
				if ((!tres && _t < 10) || _t < 1) return;
				Conferir(tres, $"em {_t:0.0}s os tres chegaram pela rede e estao desenhados");
				Apertar();
				Passar();
				break;
			case 2:
				if (!Esperou()) return;
				ConferirAlvo(cli, srv, _perto, "o 1o toque na TECLA trava no MAIS PERTO (o de 2 tiles)");
				Apertar();
				Passar();
				break;
			case 3:
				if (!Esperou()) return;
				ConferirAlvo(cli, srv, _meio, "o 2o toque passa pro seguinte (o de 3,5 tiles)");
				Apertar();
				Passar();
				break;
			case 4:
				if (!Esperou()) return;
				ConferirAlvo(cli, srv, _longe, "o 3o toque passa pro mais longe (o de 5 tiles)");
				Apertar();
				Passar();
				break;
			case 5:
				if (!Esperou()) return;
				ConferirAlvo(cli, srv, _perto, "o 4o toque da a volta: o mais perto de novo");
				Conferir(srv.MatarNaFotoDeCorpos(_perto), "o mais perto MORRE pelo funil de producao (o cadaver fica deitado no lugar)");
				Passar();
				break;
			case 6:
				// O CADAVER CHEGA DEITADO na minha tela antes da pergunta.
				if (_t < 2) return;
				Apertar();
				Passar();
				break;
			case 7:
				if (!Esperou()) return;
				ConferirAlvo(cli, srv, _meio, "com o mais perto CAIDO, o toque pula o cadaver e trava no de pe mais perto (o de 3,5 tiles)");
				cli.SendAlvo(0);
				World.TravaDoMaisLongeDeTeste = true;
				Apertar();
				Passar();
				break;
			case 8:
				if (!Esperou()) return;
				World.TravaDoMaisLongeDeTeste = false;
				Conferir(cli.AlvoId == _longe, $"CONTRA-EXEMPLO (defeito injetado: a fila ao contrario): o primeiro toque pega o MAIS LONGE (alvo {cli.AlvoId}, o longe e {_longe})");
				Fechar(srv);
				break;
		}
	}

	private void Fechar(Jandirus.Server.GameServer? srv)
	{
		_acabou = true;
		World.TravaDoMaisLongeDeTeste = false;
		if (srv != null && _obrasAntes >= 0) srv.LimparAFotoDeCorpos(_obrasAntes);
		foreach (string l in _linhas) GD.Print("[dtrava] " + l);
		GD.Print(_falhas.Count == 0 ? "[dtrava] ===== TUDO OK =====" : $"[dtrava] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dtrava]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
