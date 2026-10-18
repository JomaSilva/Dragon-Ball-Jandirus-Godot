using Godot;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA EXECUCAO DO BIO-ANDROIDE (`--diagexecucao`, 2026-10-17). O adendo do dono: *"you can perform an execution by absorbing their
/// life essence, causing their body to dry up and vanish [...] this execution takes 5 seconds"* -- e *"n esqueça de checar tudo com print de
/// tudo isso q estamos fazendo"*.
///
/// Com janela e com `--host`, nascendo Humano no campo aberto da Terra. O servidor deste processo monta a cena
/// (`GameServer.MontarCenaDaExecucaoDeTeste`: o Cell da fabrica parado ao lado de um cidadao nocauteado) e o Cell aperta o `absorver` pelo
/// verb; o tique de producao conduz os 5 s. O robo fotografa:
///
///   0. CONTRA-EXEMPLO (`World.SecandoIgnoradoDeTeste`, a tela ignorando o estado): o servidor manda a vitima SECANDO e ela continua inteira
///      na tela -- foto `execucao-0-sem-secar.png`. Um passo do Cell desfaz a execucao, e a vitima continua viva;
///   1. a execucao de verdade, no comeco: o corpo comeca a secar e a essencia sai dele -- foto `execucao-1-comeco.png`;
///   2. na metade -- foto `execucao-2-metade.png`;
///   3. quase seco -- foto `execucao-3-seco.png`;
///   4. consumada: o corpo SOME do mundo e da tela sem cadaver, e no lugar dele sobe o PO -- foto `execucao-4-po.png`.
///
///     Godot --path . --host --rede 8458 --campoteste 12 --horateste 0.5 --semfauna --diagexecucao --raca Human --conta bancada_dexecucao --nome Plateia
/// </summary>
public partial class RoboDaExecucaoDoBio : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private int _passo;
	private double _t, _espera;

	/// <summary>
	/// QUANTO DA EXECUCAO JA CORREU, no relogio do SERVIDOR (que e quem a fecha, em 5 s reais). Nao a soma dos `delta` desta tela: com a janela
	/// sem foco ela anda quase pela metade do tempo real (job85: aos "2,6 s" dela a vitima ja estava 93% seca). -1 = nenhuma correndo.
	/// </summary>
	private static double JaCorreu(Jandirus.Server.GameServer srv, int bio) =>
		srv.FaltaDaExecucaoDeTeste(bio) is var falta and >= 0 ? Jandirus.Core.Races.BioAndroids.SegundosDaExecucao - falta : -1;
	private Jandirus.Server.GameServer.CenaDaExecucao? _cena;
	private int _cadaveresAntes;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Passar() { _passo++; _t = 0; }

	private void Fotografar(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath(destino);
		img.SavePng(caminho);
		_linhas.Add("  --     foto salva em " + caminho);
	}

	/// <summary>O corpo deste id esta DESENHADO na tela (o node existe, visivel, e cai dentro da janela)?</summary>
	private bool DesenhadoNaTela(World mundo, int id)
	{
		if (mundo.CorpoDeTeste(id) is not { } corpo || !corpo.IsVisibleInTree()) return false;
		Vector2 naTela = corpo.GetGlobalTransformWithCanvas().Origin;
		return GetViewport().GetVisibleRect().HasPoint(naTela);
	}

	private static CorpoSecando? Secando(World mundo, int id) =>
		mundo.CorpoDeTeste(id)?.GetNodeOrNull<CorpoSecando>(CorpoSecando.NomeDoNode);

	/// <summary>A cor do boneco inteiro (o RGB do `Modulate` da raiz do visual) -- branco e o corpo inteiro.</summary>
	private static Color CorDoBoneco(World mundo, int id) =>
		mundo.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual")?.Modulate ?? Colors.White;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (GameClient.Instance is not { Connected: true, LocalId: > 0 } cli || World.Instancia is not { } mundo
			|| Jandirus.Server.GameServer.Instance is not Jandirus.Server.GameServer srv)
		{
			_espera += delta;
			if (_espera > 30) { Conferir(false, "em 30 s o cliente conectou, o mundo nasceu e o servidor esta neste processo"); Fechar(); }
			return;
		}
		_t += delta;
		int eu = cli.LocalId;
		switch (_passo)
		{
			case 0:
				if (_t < 1.5) return;   // o corpo assenta no campo
				GD.Print("[dexecucao] ===== A ABSORCAO DE EXECUCAO DO BIO, NA TELA (dono, 2026-10-17) =====");
				srv.EmbateDeFoto_CeuLimpo(eu);   // a foto nao depende do clima do dia
				World.SecandoIgnoradoDeTeste = true;
				_cena = srv.MontarCenaDaExecucaoDeTeste(eu, 9301);
				Conferir(_cena != null, "(palco) o Cell nasce pela fabrica ao lado de um cidadao nocauteado");
				if (_cena == null) { Fechar(); return; }
				Passar();
				break;

			case 1:
				if (_t < 0.5) return;   // os dois chegam na tela
				Conferir(srv.ExecutarDeTeste(_cena!.Value.Bio), "(palco) o Cell aperta o `absorver` pelo verb e a execucao ABRE");
				Passar();
				break;

			case 2:   // O CONTRA-EXEMPLO: dois segundos e meio de execucao com a tela cega pro estado
				if (_t < 2.5) return;
				{
					int vitima = _cena!.Value.Vitima;
					Color cor = CorDoBoneco(mundo, vitima);
					Conferir(srv.SecandoDeTeste(vitima) && Secando(mundo, vitima) == null && cor.R > 0.99f && cor.B > 0.99f && DesenhadoNaTela(mundo, vitima),
								  $"CONTRA-EXEMPLO (defeito injetado: a tela ignora a `Ocupacao.Secando`): o servidor manda a vitima SECANDO e ela continua inteira na "
								  + $"tela (cor {cor.R:0.00}/{cor.G:0.00}/{cor.B:0.00})");
					Fotografar("user://execucao-0-sem-secar.png");
					srv.PassoDoBioDeTeste(_cena.Value.Bio);
				}
				Passar();
				break;

			case 3:   // o passo do Cell desfaz a execucao, no tique de producao
				if (srv.ExecutandoDeTeste(_cena!.Value.Bio) && _t < 2) return;
				Conferir(!srv.ExecutandoDeTeste(_cena.Value.Bio) && srv.NoMundoDeTeste(_cena.Value.Vitima) && !srv.SecandoDeTeste(_cena.Value.Vitima),
							  "...e UM PASSO do Cell desfaz a execucao: a vitima continua viva, e o snapshot para de manda-la secando");
				srv.DesmontarCenaDaExecucaoDeTeste(_cena.Value);
				World.SecandoIgnoradoDeTeste = false;
				_cena = srv.MontarCenaDaExecucaoDeTeste(eu, 9311);
				if (_cena == null) { Conferir(false, "(palco) o Cell e a vitima renascem pra execucao de verdade"); Fechar(); return; }
				Passar();
				break;

			case 4:
				if (_t < 0.5) return;
				_cadaveresAntes = srv.CadaveresNaZonaDeTeste(_cena!.Value.Bio);
				Conferir(srv.ExecutarDeTeste(_cena.Value.Bio), "(palco) a execucao de verdade abre");
				Passar();
				break;

			case 5:   // O COMECO
				if (JaCorreu(srv, _cena!.Value.Bio) is >= 0 and < 0.6) return;
				{
					int vitima = _cena!.Value.Vitima;
					CorpoSecando? s = Secando(mundo, vitima);
					Conferir(s is { Voltando: false } && s.Quanto is > 0.03f and < 0.3f && s.EssenciaDeTeste is { Emitting: true } && DesenhadoNaTela(mundo, vitima),
								  $"O COMECO: a vitima do Cell COMECA A SECAR na tela ({s?.Quanto:P0}) e a essencia sai dela em fiapos");
					Fotografar("user://execucao-1-comeco.png");
				}
				Passar();
				break;

			case 6:   // A METADE
				if (JaCorreu(srv, _cena!.Value.Bio) is >= 0 and < 2.6) return;
				{
					int vitima = _cena!.Value.Vitima;
					CorpoSecando? s = Secando(mundo, vitima);
					Color cor = CorDoBoneco(mundo, vitima);
					Conferir(s is { Voltando: false } && s.Quanto is > 0.4f and < 0.65f && cor.B < 0.85f && cor.B > CorpoSecando.Seco.B && srv.ExecutandoDeTeste(_cena.Value.Bio),
								  $"NA METADE ({s?.Quanto:P0}): o corpo inteiro ja amarelou (cor {cor.R:0.00}/{cor.G:0.00}/{cor.B:0.00}), e a execucao ainda corre");
					Fotografar("user://execucao-2-metade.png");
				}
				Passar();
				break;

			case 7:   // QUASE SECO: a tres decimos de fechar. A tela comeca a secar um tique de servidor e um quadro DEPOIS do servidor (ate meio
							  // segundo com a janela sem foco), entao aqui ela esta "quase" -- 80% ou mais; o limiar do po (85%) e provado no fim, quando o corpo sai.
				if (JaCorreu(srv, _cena!.Value.Bio) is >= 0 and < 4.7) return;
				{
					int vitima = _cena!.Value.Vitima;
					CorpoSecando? s = Secando(mundo, vitima);
					Color cor = CorDoBoneco(mundo, vitima);
					Conferir(s is { Voltando: false } && s.Quanto >= 0.8f && cor.B < CorpoSecando.Seco.B + 0.13f && srv.NoMundoDeTeste(vitima),
								  $"QUASE SECO ({s?.Quanto:P0}): o corpo e cor de palha (cor {cor.R:0.00}/{cor.G:0.00}/{cor.B:0.00}) -- e ele ainda esta no mundo");
					Fotografar("user://execucao-3-seco.png");
				}
				Passar();
				break;

			case 8:   // A EXECUCAO SE CONSUMA: espera o corpo sumir do servidor e da tela
				{
					int vitima = _cena!.Value.Vitima;
					if ((srv.NoMundoDeTeste(vitima) || mundo.CorpoDeTeste(vitima) != null) && _t < 4) return;
				}
				Passar();
				break;

			case 9:   // O PO
				if (_t < 0.2) return;   // a nuvem sobe
				{
					int vitima = _cena!.Value.Vitima;
					Conferir(!srv.NoMundoDeTeste(vitima) && mundo.CorpoDeTeste(vitima) == null && !srv.ExecutandoDeTeste(_cena.Value.Bio),
								  "CONSUMADA: aos 5 s a vitima SOME -- do mundo do servidor e da tela");
					Conferir(srv.CadaveresNaZonaDeTeste(_cena.Value.Bio) == _cadaveresAntes,
								  $"...SEM CADAVER: os cadaveres da zona continuam {_cadaveresAntes} (o corpo seco nao fica no chao)");
					Conferir(PoeiraDeEstrago.UltimaCorDeTeste == CorpoSecando.DoPo && PoeiraDeEstrago.VivosDeTeste > 0,
								  "...e no lugar dele sobe o PO, na cor do corpo seco (a poeira de estrago da tela)");
					Fotografar("user://execucao-4-po.png");
				}
				Fechar();
				break;
		}
	}

	private void Fechar()
	{
		_acabou = true;
		World.SecandoIgnoradoDeTeste = false;
		if (_cena is { } c && Jandirus.Server.GameServer.Instance is { } srv) srv.DesmontarCenaDaExecucaoDeTeste(c);
		foreach (string l in _linhas) GD.Print("[dexecucao] " + l);
		GD.Print(_falhas.Count == 0 ? "[dexecucao] ===== TUDO OK =====" : $"[dexecucao] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dexecucao]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
