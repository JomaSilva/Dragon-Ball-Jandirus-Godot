using Godot;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO ESPACO SO VOANDO (`--diagespacovoo`, 2026-10-20) -- a metade de ca do fio da regra do dono (*"n permita q de pra andar no
/// espaço so voar"*). A bancada de servidor (`--espacovooteste`) prova a regra; esta prova que o CLIENTE concorda com ela: o corpo local
/// parado no vazio nem tenta o passo (o servidor o devolveria a cada pacote -- o boneco dando tranco), e voando ele anda.
///
/// Roda com `--host` (o servidor e deste processo), headless, no campo aberto da bancada (`--campoteste`), sem fauna e com o voo
/// concedido (`--vooteste`). As teclas sao apertadas de verdade (`Input.ActionPress`, lidas pelo `LocalPlayer` no mesmo quadro -- o robo
/// processa antes dele), e o corpo vai e volta do vazio pelo palco (`GameServer.EspacoSoVoandoPalco.cs`, pelo `MoveToZone` de producao):
///   T. NA TERRA, A TECLA ANDA -- o contra-exemplo da zona;
///   E. NO VAZIO, A PE, A TECLA NAO TIRA O CORPO DO LUGAR NA TELA, o `PorQueNaoAnda` diz por que, e o chat diz a tecla do voo;
///   P. O PILOTO AUTOMATICO ESPERA no vazio sem voo -- nao empurra o corpo parado contra o servidor, diz por que, e segue viagem
///      quando o voo liga;
///   V. A TECLA DO VOO LIGA, E A TECLA DE ANDAR ANDA;
///   S. A TECLA DO VOO DE NOVO DESLIGA, E O CORPO PARA;
///   D. CONTRA-EXEMPLO (`LocalPlayer.APeNoEspacoAndaDeTeste`): o cliente volta a prever o passo de quem nao voa -- o corpo sai do lugar na
///      propria tela e e devolvido pelo servidor, o tranco que a linha do conserto evita;
///   L. AO LEME DO POD NO VAZIO (o voo do piloto apagado pela subida), A TECLA ANDA -- quem anda e a nave.
///
/// O QUE ELA MEDE E O DESLOCAMENTO NA TELA, e nao a contagem de correcoes: o servidor manda uma correcao por pacote a TODO corpo preso,
/// ande ele ou nao (`GameServer.AplicarInput`), e o tranco e o corpo que anda na tela e volta -- o maior afastamento visto num quadro.
///
///     Godot --path . --headless --host --rede 9961 --campoteste 8 --semfauna --vooteste --diagespacovoo --raca Human --conta bancada_espacovoo_r --nome Astronauta
/// </summary>
public partial class RoboDoEspacoSoVoando : Node
{
	private int _ok, _falha, _injOk, _injFalha;
	private readonly List<string> _reprovadas = [];

	private static void Nota(string s) => GD.Print("[despacovoo] " + s);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK     " : "  FALHA  ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private void Injeta(string oque, bool ficouVermelha, string detalhe = "")
	{
		Nota((ficouVermelha ? "  pegou  " : "  PASSOU ") + "(defeito injetado) " + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ficouVermelha) _injOk++;
		else { _injFalha++; _reprovadas.Add("(defeito injetado) " + oque); }
	}

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>O corpo local -- quem le o teclado e diz por que nao anda.</summary>
	private LocalPlayer? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as LocalPlayer;

	private static bool NoVazio(GameClient cli) => Jandirus.Core.World.Espaco.EhEspaco(cli.Zone);

	public override void _Ready()
	{
		// ANTES do `LocalPlayer` no quadro: o `IsActionJustPressed` da tecla do voo so vale no quadro do aperto (a licao do `RoboDeVoo`).
		ProcessPriority = -100;
		_ = Rodar();
	}

	/// <summary>Uma excecao no roteiro e um vermelho com placar, e nao uma bancada muda ate o prazo.</summary>
	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Checa("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			Nota(e.ToString());
			Encerrar(3);
		}
	}

	private void Encerrar(int codigo)
	{
		// O MUNDO VOLTA: nenhum botao de defeito nem tecla apertada sobrevive ao roteiro.
		LocalPlayer.APeNoEspacoAndaDeTeste = false;
		Input.ActionRelease(Teclas.NomeNoMotor("move_right"));
		Input.ActionRelease(Teclas.NomeNoMotor("voar"));
		if (S is { } srv && C is { LocalId: > 0 } cli)
		{
			srv.PalcoEv_SemPod(cli.LocalId);
			srv.PalcoEv_ATerra(cli.LocalId);
		}

		Nota("==================================================================================");
		Nota($" PLACAR: {_ok} OK, {_falha} FALHA(S)   |   DEFEITOS INJETADOS: {_injOk} pegos, {_injFalha} passaram batido");
		foreach (string r in _reprovadas) Nota("   FALHA  " + r);
		int vermelhas = _falha + _injFalha;
		Nota(vermelhas == 0 ? "===== TUDO OK =====" : $"===== {vermelhas} FALHA(S) =====");
		GetTree().Quit(codigo);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Tarefa Percurso()
	{
		Nota("===== NO ESPACO SO SE ANDA VOANDO, NA TELA (dono, 2026-10-20) =====");
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && S != null
									  && World.Instancia is { PosicaoLocalDeTeste: not null } && Corpo != null, 90);
		Checa("o mundo chegou (conexao, corpo local) e o servidor e deste processo (--host)", pronto);
		if (!pronto || C is not { } cli || S is not { } srv || World.Instancia is not { } mundo || Corpo is not { } corpo) { Encerrar(2); return; }
		await Segundos(1.0);
		string voar = Teclas.NomeNoMotor("voar");

		// ---- T: na Terra, a tecla anda ----
		var (_, tFim) = await Andar(mundo, 0.6);
		Checa("T. **NA TERRA, A TECLA ANDA**: no planeta, a pe, o corpo sai do lugar -- a regra nao vaza pro chao",
			  !NoVazio(cli) && tFim > 8f && corpo.PorQueNaoAnda.Length == 0,
			  $"zona {cli.Zone.Name}; andou {tFim:0.0} px; por que nao anda '{corpo.PorQueNaoAnda}'");

		// ---- E: no vazio, a pe ----
		bool foi = srv.PalcoEv_AoEspaco(cli.LocalId) && await Ate(() => NoVazio(cli) && mundo.PosicaoLocalDeTeste != null, 10);
		await Segundos(1.5);   // a tela de carregamento e os primeiros snapshots do vazio
		Checa("(montagem) o palco leva o corpo ao vazio pelo `MoveToZone` de producao, a pe", foi && !srv.PalcoEv_Estado(cli.LocalId).Voando,
			  $"zona {cli.Zone.Name}; servidor {srv.PalcoEv_Estado(cli.LocalId)}");
		if (!foi) { Encerrar(1); return; }

		int linhas = Chat.LinhasDeTeste().Count;
		var (eMaior, _) = await Andar(mundo, 1.0);
		string porQue = corpo.PorQueNaoAnda;
		Checa("E. **NO VAZIO, A PE, A TECLA NAO TIRA O CORPO DO LUGAR NA TELA**: o cliente nem tenta o passo (o servidor o devolveria a cada pacote), e o `PorQueNaoAnda` diz por que",
			  eMaior < 0.5f && porQue.StartsWith("no espaco", StringComparison.Ordinal), $"maior afastamento {eMaior:0.00} px; por que nao anda '{porQue}'");
		Checa("E2. ...e quem aperta a tecla le no chat que no espaco so se anda voando, com a tecla do voo",
			  DisseDesde(linhas, "anda voando: aperte"), UltimasDesde(linhas));

		// ---- P: o piloto automatico espera, e segue viagem quando o voo liga ----
		await Segundos(4.2);   // o relogio das frases de membro (4 s) vence: a frase do piloto nao cai na da tecla de agora ha pouco
		linhas = Chat.LinhasDeTeste().Count;
		Vector2 dePiloto = mundo.PosicaoLocalDeTeste ?? Vector2.Zero;
		float maiorPiloto = 0;
		mundo.AndarDeTeste(new Vector2(1, 0));
		for (int i = 0; i < 40; i++)
		{
			await Quadros(1);
			maiorPiloto = Mathf.Max(maiorPiloto, ((mundo.PosicaoLocalDeTeste ?? dePiloto) - dePiloto).Length());
		}
		Checa("P1. **O PILOTO AUTOMATICO ESPERA NO VAZIO SEM VOO**: ele continua ligado, mas nao empurra o corpo parado contra o servidor (sem tranco)",
			  corpo.Destino != null && maiorPiloto < 0.5f, $"destino {corpo.Destino}; maior afastamento {maiorPiloto:0.00} px");
		Checa("P2. ...e diz por que no chat, com a tecla do voo", DisseDesde(linhas, "espera: no espa"), UltimasDesde(linhas));

		await ToqueNaAcao(voar);
		bool voouComOPiloto = await Ate(() => srv.PalcoEv_Estado(cli.LocalId).Voando, 3);
		Vector2 deVoando = mundo.PosicaoLocalDeTeste ?? Vector2.Zero;
		await Segundos(0.8);
		float seguiu = ((mundo.PosicaoLocalDeTeste ?? deVoando) - deVoando).Length();
		Checa("P3. ...e quem liga o voo SEGUE VIAGEM pelo mesmo piloto, sem tocar em mais nada",
			  voouComOPiloto && corpo.Destino != null && seguiu > 8f, $"voando {voouComOPiloto}; destino {corpo.Destino}; andou {seguiu:0.0} px");
		mundo.PararDeTeste();
		await ToqueNaAcao(voar);   // de volta a pe, pra a tecla do voo ligar de novo logo abaixo
		await Ate(() => srv.PalcoEv_Estado(cli.LocalId) is { Voando: false, Altura: <= 0f }, 3);
		await Segundos(0.3);

		// ---- V: a tecla do voo liga, e a tecla de andar anda ----
		await ToqueNaAcao(voar);
		bool voa = await Ate(() => srv.PalcoEv_Estado(cli.LocalId).Voando && corpo.PorQueNaoAnda.Length == 0, 3);
		await Segundos(0.3);
		var (_, vFim) = await Andar(mundo, 0.8);
		Checa("V. **A TECLA DO VOO LIGA, E A TECLA DE ANDAR ANDA** no vazio",
			  voa && vFim > 8f, $"servidor {srv.PalcoEv_Estado(cli.LocalId)}; andou {vFim:0.0} px; por que nao anda '{corpo.PorQueNaoAnda}'");

		// ---- S: a tecla do voo de novo desliga, e o corpo para ----
		await ToqueNaAcao(voar);
		bool parou = await Ate(() => srv.PalcoEv_Estado(cli.LocalId) is { Voando: false, Altura: <= 0f }
									 && corpo.PorQueNaoAnda.StartsWith("no espaco", StringComparison.Ordinal), 3);
		await Segundos(0.3);
		var (sMaior, _) = await Andar(mundo, 0.8);
		Checa("S. **A TECLA DO VOO DE NOVO DESLIGA, E O CORPO PARA** no vazio -- sem tranco",
			  parou && sMaior < 0.5f, $"servidor {srv.PalcoEv_Estado(cli.LocalId)}; maior afastamento {sMaior:0.00} px; por que nao anda '{corpo.PorQueNaoAnda}'");

		// ---- D: o contra-exemplo do cliente ----
		LocalPlayer.APeNoEspacoAndaDeTeste = true;
		try
		{
			var (dMaior, dFim) = await Andar(mundo, 0.8);
			Injeta("(o cliente volta a prever o passo de quem nao voa) o corpo sai do lugar na propria tela e o servidor o devolve a cada pacote -- o tranco",
				   dMaior > 1f && dFim < dMaior, $"maior afastamento {dMaior:0.0} px; no fim {dFim:0.0} px");
		}
		finally { LocalPlayer.APeNoEspacoAndaDeTeste = false; }
		await Segundos(0.6);

		// ---- L: ao leme do pod, no vazio ----
		srv.PalcoEv_ATerra(cli.LocalId);
		bool naTerra = await Ate(() => !NoVazio(cli) && mundo.PosicaoLocalDeTeste != null, 10);
		await Segundos(1.5);
		bool embarcou = naTerra && srv.PalcoEv_EmbarcarNoPod(cli.LocalId) && await Ate(() => cli.VeiculoMontado.Length > 0, 5);
		await Segundos(0.5);
		bool subiu = embarcou && srv.PalcoEv_AoEspaco(cli.LocalId) && await Ate(() => NoVazio(cli) && mundo.PosicaoLocalDeTeste != null, 10);
		await Segundos(1.5);
		var estado = srv.PalcoEv_Estado(cli.LocalId);
		Checa("(montagem) o palco embarca o corpo num pod na Terra (a tecla E) e o sobe pelo `MoveToZone`: ao leme no vazio, com o voo apagado",
			  subiu && estado.AoLeme && !estado.Voando, $"na terra {naTerra}; embarcou {embarcou}; subiu {subiu}; servidor {estado}");
		if (subiu)
		{
			var (_, lFim) = await Andar(mundo, 0.8);
			Checa("L. **AO LEME DO POD, NO VAZIO, A TECLA ANDA** com o voo do piloto apagado -- quem anda e a nave, e o cliente concorda com o servidor",
				  lFim > 8f && corpo.PorQueNaoAnda.Length == 0, $"andou {lFim:0.0} px; por que nao anda '{corpo.PorQueNaoAnda}'; servidor {srv.PalcoEv_Estado(cli.LocalId)}");
		}

		Encerrar(_falha + _injFalha == 0 ? 0 : 1);
	}

	// =====================================================================
	// OS GESTOS E AS ESPERAS
	// =====================================================================
	/// <summary>
	/// SEGURA A TECLA DE ANDAR PRA DIREITA por alguns segundos, um quadro de cada vez: devolve o maior afastamento do corpo local visto num
	/// quadro (o tranco aparece nele mesmo quando o servidor devolve o corpo) e onde ele terminou, ja com a tecla solta.
	/// </summary>
	private async System.Threading.Tasks.Task<(float Maior, float NoFim)> Andar(World mundo, double segundos)
	{
		string tecla = Teclas.NomeNoMotor("move_right");
		Vector2 de = mundo.PosicaoLocalDeTeste ?? Vector2.Zero;
		float maior = 0;
		Input.ActionPress(tecla);
		try
		{
			double fim = Agora() + segundos;
			while (Agora() < fim)
			{
				await Quadros(1);
				maior = Mathf.Max(maior, ((mundo.PosicaoLocalDeTeste ?? de) - de).Length());
			}
		}
		finally { Input.ActionRelease(tecla); }
		await Segundos(0.3);
		return (maior, ((mundo.PosicaoLocalDeTeste ?? de) - de).Length());
	}

	private async Tarefa ToqueNaAcao(string acao)
	{
		Input.ActionPress(acao);
		await Segundos(0.1);
		Input.ActionRelease(acao);
		await Quadros(2);
	}

	private static bool DisseDesde(int linhas, string trecho) =>
		Chat.LinhasDeTeste().Skip(linhas).Any(l => l.Texto.Contains(trecho, StringComparison.Ordinal));

	private static string UltimasDesde(int linhas) =>
		string.Join(" | ", Chat.LinhasDeTeste().Skip(linhas).TakeLast(3).Select(l => l.Texto));

	private static double Agora() => Time.GetTicksMsec() / 1000.0;

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Agora() + s;
		while (Agora() < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Agora() + segundos;
		while (Agora() < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}
}
