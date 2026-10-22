using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO TREINO E DA MEDITACAO CONFERIDOS, PELO CLIENTE (`--diagatividade`).
///
/// O dono (2026-10-11), sobre a lista de validacoes fracas: *"resolva todos esses problemas"*. Treinar e meditar
/// eram so declarados pelo cliente; agora quem confere e o servidor (`GameServer.Atividade.cs`), e a regra dele
/// tem bancada propria (`--validacoesteste`, secao 10). O que so DAQUI se ve e a outra ponta: quando o servidor
/// recusa ou derruba a atividade, **o corpo local tem de largar a pose e o HUD** -- eles so conhecem o que este
/// cliente declarou, e sem a escuta (`LocalPlayer.OnAtividadeCaiu`) o jogador ficaria vendo "TREINANDO" num
/// corpo que ja nao ganha nada.
///
///   1. No chao, a tecla T liga o treino nas duas pontas.
///   2. DECOLAR derruba: o servidor para (`start_flying()` chama `Deoccupy()`, `flying.dm:107-108`), o cliente
///      larga a atividade, e o chat diz por que.
///   3. Pedir NO AR e recusado (`!flight`, `Train.dm:72`): o cliente, que liga a pose na hora, a larga em seguida.
///   4. Contraprova: com a escuta desligada o corpo local continua "treinando" depois de o servidor parar.
///   5. ANDAR derruba nas duas pontas (a regra que o cliente ja tinha, agora tambem do servidor).
///   6. Parar e apertar T no mesmo instante NAO e recusado: a carencia de chegada do servidor.
///
/// As teclas sao as de verdade (`Input.ActionPress`), lidas pelo `LocalPlayer` de producao; o voo e o verb de
/// producao (`voar`), com a skill dada pelo `--vooteste`. Hospedando: o que o servidor diz e lido dele.
///
///     Godot --headless --path . --host --rede 8267 --vooteste --bpteste 3000000 --diagatividade --raca Human --conta bancada_atividade --nome Aprendiz
/// </summary>
public partial class RoboDeAtividade : Node
{
	private int _ok;
	private readonly List<string> _falhas = [];

	/// <summary>O que o servidor disse a este jogador pelo chat, na ordem.</summary>
	private readonly List<string> _ouvido = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;
	private LocalPlayer? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as LocalPlayer;

	private static void Nota(string linha) => GD.Print("[atividade] " + linha);

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		Nota((ok ? "  ok     " : "  FALHA  ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ok) _ok++;
		else _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree()
	{
		if (C is { } cli) cli.Falou -= AoOuvir;
	}

	private void AoOuvir(Protocol.Fala canal, string autor, string texto)
	{
		if (canal == Protocol.Fala.Sistema) _ouvido.Add(texto);
	}

	private bool Ouvi(string trecho) => _ouvido.Any(t => t.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	/// <summary>Uma excecao no roteiro e um vermelho, nao um silencio; o placar e a arrumacao saem no `finally`.</summary>
	private async System.Threading.Tasks.Task Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Conferir(false, "o roteiro correu ate o fim sem lancar excecao", $"{e.GetType().Name}: {e.Message}");
			Nota(e.ToString());
		}
		finally
		{
			LocalPlayer.AtividadeNaoCaiDeTeste = false;
			Tecla("train", false);
			Tecla("move_right", false);
			Nota(_falhas.Count == 0
				? $"===== TUDO OK ({_ok} afirmacoes) ====="
				: $"===== {_falhas.Count} FALHA(S), {_ok} ok =====\n[atividade]   " + string.Join("\n[atividade]   ", _falhas));
		}
	}

	// =====================================================================
	// OS GESTOS
	// =====================================================================
	private static void Tecla(string acao, bool embaixo)
	{
		if (embaixo) Godot.Input.ActionPress(Teclas.NomeNoMotor(acao));
		else Godot.Input.ActionRelease(Teclas.NomeNoMotor(acao));
	}

	private async System.Threading.Tasks.Task Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async System.Threading.Tasks.Task Segundos(double s)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + s;
		while (Time.GetTicksMsec() / 1000.0 < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
		while (Time.GetTicksMsec() / 1000.0 < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}

	/// <summary>A tecla T como um dedo: desce, o corpo a le no quadro em que desceu, e sobe.</summary>
	private async System.Threading.Tasks.Task ApertarT()
	{
		bool iaTreinar = Corpo?.AtividadeDeTeste == Protocol.Activity.Treinando;
		Tecla("train", true);
		await Quadros(2);
		Tecla("train", false);
		await Quadros(2);
		// (2026-10-21) O T abre a telinha. O "treinar" desta bancada e o botao normal -- o AFK de sempre.
		if (!iaTreinar && TelaDeTreino.Instancia is { NaTela: true } tela)
			tela.ApertarDesenhado(EscolhaDoTreino.Normal);
		await Quadros(1);
	}

	/// <summary>O verb de voar (a tecla V manda este mesmo), ate o servidor dizer que o corpo esta onde se pediu.</summary>
	private async System.Threading.Tasks.Task<bool> Voar(GameClient cli, Jandirus.Server.GameServer srv, int eu, bool noAr)
	{
		if (srv.AtividadeDeTeste(eu).NoAr == noAr) return true;
		cli.SendHabilidade("voar");
		return await Ate(() => srv.AtividadeDeTeste(eu).NoAr == noAr, 6);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async System.Threading.Tasks.Task Percurso()
	{
		Nota("===== BANCADA DO TREINO E DA MEDITACAO CONFERIDOS, PELO CLIENTE =====");

		bool pronto = await Ate(() => C is { Connected: true, LocalId: not 0 } c && c.Atributos.Raca is { Length: > 0 } && Corpo != null, 90);
		Conferir(pronto, "(montagem) o mundo chegou: conexao e corpo local", $"raca={C?.Atributos.Raca} corpo={Corpo != null}");
		if (!pronto || C is not { } cli || Corpo is not { } corpo) return;

		bool hospedando = Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0;
		Conferir(hospedando && S != null, "(montagem) o servidor mora neste processo (--host): o que ele diz e lido dele");
		if (!hospedando || S is not { } srv) return;

		cli.Falou += AoOuvir;
		await Segundos(1.5);
		int eu = cli.LocalId;
		bool Treina() => srv.AtividadeDeTeste(eu).Treina;
		string Estado() => $"corpo {corpo.AtividadeDeTeste}, cliente {cli.Atividade}, servidor treina={Treina()} noAr={srv.AtividadeDeTeste(eu).NoAr}";

		// ---------------------------------------------------------------------- 1. no chao, T treina
		Nota("--- 1: no chao, a tecla T liga o treino nas duas pontas ---");
		Conferir(await Voar(cli, srv, eu, noAr: false), "(montagem) o corpo esta no chao", Estado());
		await ApertarT();
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Treinando && Treina(), 3);
		Conferir(corpo.AtividadeDeTeste == Protocol.Activity.Treinando && cli.Atividade == Protocol.Activity.Treinando && Treina(),
			"no chao, parado, a tecla T liga o treino: o corpo local, o cliente e o SERVIDOR concordam", Estado());
		await Segundos(1.0);
		Conferir(corpo.AtividadeDeTeste == Protocol.Activity.Treinando && Treina() && !Ouvi("para de treinar"),
			"...e um segundo depois ele continua treinando (a conferencia do servidor, a 5 Hz, nao derruba quem esta parado)", Estado());

		// ---------------------------------------------------------------------- 2. decolar derruba
		Nota("--- 2: decolar derruba, e o cliente fica sabendo ---");
		bool subiu = await Voar(cli, srv, eu, noAr: true);
		Conferir(subiu, "(montagem) o corpo decolou pelo verb de producao (`voar`, com a skill do --vooteste)", Estado());
		await Ate(() => !Treina() && corpo.AtividadeDeTeste == Protocol.Activity.Parado, 3);
		Conferir(!Treina(), "**DECOLAR DERRUBA O TREINO NO SERVIDOR** (`start_flying()` chama `Deoccupy()`, `flying.dm:107-108`)", Estado());
		Conferir(corpo.AtividadeDeTeste == Protocol.Activity.Parado && cli.Atividade == Protocol.Activity.Parado,
			"**...E O CORPO LOCAL LARGA A ATIVIDADE**: a pose e o HUD deixam de dizer \"TREINANDO\" sem o jogador apertar nada", Estado());
		await Ate(() => Ouvi("no ar não dá"), 2);
		Conferir(Ouvi("no ar não dá"), "...e o chat diz por que", _ouvido.LastOrDefault() ?? "(nada)");

		// ---------------------------------------------------------------------- 3. pedir no ar e recusado
		Nota("--- 3: pedir no ar e recusado ---");
		_ouvido.Clear();
		await ApertarT();
		await Ate(() => Ouvi("pouse antes"), 3);
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Parado, 2);
		Conferir(Ouvi("pouse antes") && !Treina(),
			"**NO AR O PEDIDO E RECUSADO PELO SERVIDOR** (`!flight`, `Train.dm:72`), com a frase que diz o que fazer", $"{Estado()} | {_ouvido.LastOrDefault() ?? "(nada)"}");
		Conferir(corpo.AtividadeDeTeste == Protocol.Activity.Parado && cli.Atividade == Protocol.Activity.Parado,
			"...e o corpo local, que liga a pose na hora da tecla, a larga quando a recusa chega", Estado());

		// ---------------------------------------------------------------------- 4. a contraprova
		Nota("--- 4: contraprova -- sem a escuta, o corpo local segue \"treinando\" ---");
		Conferir(await Voar(cli, srv, eu, noAr: false), "(montagem) de volta ao chao", Estado());
		await ApertarT();
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Treinando && Treina(), 3);
		Conferir(corpo.AtividadeDeTeste == Protocol.Activity.Treinando && Treina(), "(montagem) treinando de novo, nas duas pontas", Estado());
		LocalPlayer.AtividadeNaoCaiDeTeste = true;
		await Voar(cli, srv, eu, noAr: true);
		await Ate(() => !Treina(), 3);
		await Segundos(1.0);
		Conferir(!Treina() && corpo.AtividadeDeTeste == Protocol.Activity.Treinando,
			"(defeito injetado: o cliente nao escuta o servidor derrubar a atividade) o servidor parou e o corpo local continua "
			+ "dizendo TREINANDO -- a regua sabe reprovar", Estado());
		LocalPlayer.AtividadeNaoCaiDeTeste = false;
		await ApertarT();   // a mesma tecla desfaz: o corpo local, que se dizia treinando, declara "parado"
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Parado, 2);
		Conferir(await Voar(cli, srv, eu, noAr: false) && corpo.AtividadeDeTeste == Protocol.Activity.Parado,
			"(montagem) no chao e parado, pras duas ultimas", Estado());

		// ---------------------------------------------------------------------- 5. andar derruba
		Nota("--- 5: andar derruba, nas duas pontas ---");
		await ApertarT();
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Treinando && Treina(), 3);
		Tecla("move_right", true);
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Parado && !Treina(), 3);
		Conferir(corpo.AtividadeDeTeste == Protocol.Activity.Parado && !Treina(),
			"**ANDAR DERRUBA NAS DUAS PONTAS** (`if(curdir) src.Deoccupy()`, `movement handler.dm:217-219`): a regra que so o cliente tinha", Estado());

		// ---------------------------------------------------------------------- 6. a carencia de chegada
		Nota("--- 6: parar e apertar T no mesmo instante ---");
		await Segundos(0.4);                 // andando de verdade
		_ouvido.Clear();
		Tecla("move_right", false);          // para...
		await Quadros(1);
		await ApertarT();                    // ...e aperta T em seguida: o pedido pode chegar antes do ultimo passo
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Treinando && Treina(), 3);
		await Segundos(1.5);                 // bem depois da carencia do servidor
		Conferir(corpo.AtividadeDeTeste == Protocol.Activity.Treinando && Treina() && !Ouvi("para de treinar") && !Ouvi("pare antes"),
			"**PARAR E APERTAR T EM SEGUIDA NAO E RECUSADO**: o servidor espera o \"parei\" chegar (a carencia de chegada) em vez de "
			+ "derrubar quem acabou de parar", $"{Estado()} | {_ouvido.LastOrDefault() ?? "(nada)"}");

		await ApertarT();
		await Ate(() => corpo.AtividadeDeTeste == Protocol.Activity.Parado && !Treina(), 2);
		Conferir(corpo.AtividadeDeTeste == Protocol.Activity.Parado && !Treina(), "...e a tecla T de novo para, nas duas pontas", Estado());
	}
}
