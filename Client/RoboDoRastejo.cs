using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O RASTEJO NA TELA (`--diagrastejo`) -- quem cai ou fica preso pela gravidade se arrasta a 1/4 (dono, 2026-10-23; a regra em
/// `GameServer.Rastejo.cs`, a conta na `--rastejoteste`). Aqui o que o jogador ve e o que a tecla faz, pelo caminho inteiro: a tecla de
/// andar, a previsao do cliente pelo `SpeedStat` da ficha, o pacote, o `AplicarInput` -- e nenhuma correcao do servidor no meio.
///   A. PRESO DE PE (a camara a 7x, razao 8): a ficha diz que ele so se arrasta; a tecla anda, no passo da ficha, com o andar de sempre;
///      o SHIFT nao corre.
///   B. DESMAIADO PELA GRAVIDADE (a camara a 1x, razao 2, e o corpo na beira do desmaio): o tique da gravidade derruba, a ficha diz que
///      ele se arrasta, o chat diz como; a tecla o leva DEITADO pra fora do campo -- o quadro de nocaute, sem virar -- e la fora ele
///      continua se arrastando.
/// Os defeitos injetados do cliente (`LocalPlayer.RastejoIgnoradoDeTeste`, `RastejoCorreDeTeste`, `DeitadoViraNoRastejoDeTeste`) reprovam
/// as reguas deles.
///
///     Godot --path . --janela --resolution 1280x720 --host --rede 10851 --horateste 0.5 --campoteste 9 --semfauna --diagrastejo \
///           --raca Human --conta bancada_rastejo --nome Rastejo
///
/// Fotos: `user://rastejo-1-preso.png`, `rastejo-2-preso-andando.png`, `rastejo-3-desmaiado.png`, `rastejo-4-arrastando.png`,
/// `rastejo-5-fora-do-campo.png`. Sem janela (`--headless`) as reguas rodam iguais e as fotos ficam de fora.
/// </summary>
public partial class RoboDoRastejo : Node
{
	private int _ok, _falha, _injOk, _injFalha, _correcoes;
	private readonly List<string> _reprovadas = [];
	private readonly List<string> _ouvido = [];

	private static void Nota(string s) => GD.Print("[drastejo] " + s);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK     " : "  FALHA  ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private void Injeta(string oque, bool ficouVermelha, string detalhe = "")
	{
		Nota((ficouVermelha ? "  pegou  " : "  FALHA  ") + "(defeito injetado) " + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ficouVermelha) _injOk++;
		else { _injFalha++; _reprovadas.Add("(defeito injetado) " + oque); }
	}

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;
	private LocalPlayer? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as LocalPlayer;
	private static CharacterVisual? Visual => World.Instancia?.VisualLocalDeTeste;

	public override void _Ready()
	{
		// PENSA ANTES DO CORPO: a tecla apertada aqui vale no MESMO quadro do passo dele (a licao do `RoboDoTranse`).
		ProcessPriority = -100;
		ProcessMode = ProcessModeEnum.Always;
		_ = Rodar();
	}

	public override void _ExitTree() => Desligar();

	private void AoCorrigir(Vec2 _) => _correcoes++;

	private void AoOuvir(Protocol.Fala canal, string autor, string texto)
	{
		if (canal == Protocol.Fala.Sistema) _ouvido.Add(texto);
	}

	/// <summary>Nenhum botao de defeito, nenhuma tecla e nenhum ouvido sobrevivem ao roteiro.</summary>
	private void Desligar()
	{
		LocalPlayer.RastejoIgnoradoDeTeste = LocalPlayer.RastejoCorreDeTeste = LocalPlayer.DeitadoViraNoRastejoDeTeste = false;
		foreach (string acao in new[] { "move_up", "move_down", "move_left", "move_right", "run" }) Input.ActionRelease(Teclas.NomeNoMotor(acao));
		if (C is { } c) { c.Corrected -= AoCorrigir; c.Falou -= AoOuvir; }
	}

	/// <summary>Uma excecao no roteiro e um vermelho com placar, e nao uma bancada muda ate o prazo.</summary>
	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Checa("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			Nota(e.ToString());
		}
		Encerrar();
	}

	private void Encerrar()
	{
		Desligar();
		if (C is { LocalId: not 0 } c) S?.LimparOPalcoDoRastejo(c.LocalId);
		Nota("==================================================================================");
		Nota($" PLACAR: {_ok} OK, {_falha} FALHA(S)   |   DEFEITOS INJETADOS: {_injOk} pegos, {_injFalha} passaram batido");
		foreach (string r in _reprovadas) Nota("   FALHA  " + r);
		int vermelhas = _falha + _injFalha;
		Nota(vermelhas == 0 ? "===== TUDO OK =====" : $"===== {vermelhas} FALHA(S) =====");
		GetTree().Quit(vermelhas == 0 ? 0 : 1);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Tarefa Percurso()
	{
		Nota("==================================================================================");
		Nota(" O RASTEJO NA TELA -- quem cai ou fica preso pela gravidade se arrasta a 1/4 (dono, 2026-10-23)");
		Nota("==================================================================================");
		bool pronto = await Ate(() => C is { Connected: true, LocalId: not 0 } c && c.Sheet.SpeedStat > 0 && Corpo != null && Visual != null
								   && S != null, 40);
		Checa("(montagem) o mundo chegou: conexao, ficha, o corpo desenhado e o servidor deste processo (--host)", pronto);
		if (!pronto || C is not { } cli || S is not { } srv || Corpo is not { } corpo) return;
		bool dePe = await Ate(() => !cli.Sheet.KO && !cli.Sheet.Morto, 25);
		Checa("(montagem) o corpo entrou de pe", dePe);
		if (!dePe) return;
		cli.Corrected += AoCorrigir;
		cli.Falou += AoOuvir;
		int id = cli.LocalId;

		bool praca = srv.AssentarNaPracaDasMaquinas(id, 6);
		Checa("(montagem) achei um quadrado de chao comum com seis tiles livres pra cada lado", praca);
		if (!praca) return;
		Nota($"habitantes tirados da cena (a 12 tiles): {srv.SemHabitantesNaCenaDoRastejo(id, 12)}");
		await Segundos(1.2);   // a posicao cravada chega ao cliente (a correcao do teleporte)
		int maq = srv.MaquinaDaFoto(id, "Gravity", -1, 0);   // a camara UM TILE A OESTE: o caminho pra fora do campo e o leste
		Checa("(montagem) a camara de gravidade assentou ao lado, pelo funil de producao", maq != 0);
		if (maq == 0) return;
		await Segundos(0.6);
		float semGravidade = cli.Sheet.SpeedStat;
		Nota($"o passo de fora do campo, de pe e inteiro: SpeedStat {semGravidade:0.###}");

		await OPresoDePe(cli, srv, corpo, id, maq, semGravidade);
		await ODesmaiadoPelaGravidade(cli, srv, corpo, id, maq);
	}

	// ---------------------------------------------------------------- A
	private async Tarefa OPresoDePe(GameClient cli, Jandirus.Server.GameServer srv, LocalPlayer corpo, int id, int maq, float semGravidade)
	{
		Nota("--- A: o preso de pe (a camara a 7x: razao 8) ---");
		bool ligou = srv.CampoDoRastejo(id, maq, 7, 4);
		bool soube = await Ate(() => cli.Sheet.SoRasteja && !cli.Sheet.KO, 4);
		var s = srv.RastejoNaFoto(id);
		Checa("A0. **A FICHA DIZ QUE SO A GRAVIDADE ME SEGURA**: no campo de razao 8 o corpo de pe esta preso, e o bit da ficha chega",
			ligou && soube && s.Razao >= 4, $"campo {s.Campo:0.#}x; razao {s.Razao:0.##}; bit {cli.Sheet.SoRasteja}");
		Checa("A0b. o passo da ficha e mais ou menos um quarto do de fora do campo (a conta exata e a da `--rastejoteste`; aqui a vida ja comecou a cair)",
			cli.Sheet.SpeedStat > 0.18f * semGravidade && cli.Sheet.SpeedStat <= 0.2501f * semGravidade,
			$"fora {semGravidade:0.###}; preso {cli.Sheet.SpeedStat:0.###}; razao {cli.Sheet.SpeedStat / semGravidade:0.###}");
		// o aviso do esmagamento sai no tique de 1 Hz -- e a foto espera ele estar no chat
		bool avisou = await Ate(() => Ouvi("mal consegue se arrastar"), 3);
		Checa("A0c. o aviso no chat diz que ele mal consegue se arrastar", avisou, Ultimas());
		await Segundos(0.3);
		GuardarATela("rastejo-1-preso.png", "1. preso de pe no campo de razao 8, com o aviso no chat");

		Medida norte = await Segurar("move_up", 1.2, foto: ("rastejo-2-preso-andando.png", "2. o preso de pe andando, devagar"));
		var depois = srv.RastejoNaFoto(id);
		Checa("A1. **PRESO DE PE, ANDA**: a tecla de andar o leva, no passo que a ficha diz (a previsao do cliente)",
			norte.Andou > 4f && norte.NoPasso, norte.ToString());
		Checa("A2. **...e o servidor concorda**: nenhuma correcao, e o corpo dele esta onde o meu esta",
			norte.Correcoes == 0 && (depois.Pos - corpo.PosicaoExataDeTeste).Length < 3f,
			$"correcoes {norte.Correcoes}; servidor {depois.Pos.X:0.0},{depois.Pos.Y:0.0} x cliente {corpo.PosicaoExataDeTeste.X:0.0},{corpo.PosicaoExataDeTeste.Y:0.0}");
		Checa("A3. **...com o andar de sempre** (de pe, a perna mexendo no ritmo do passo pesado)", norte.Andando,
			$"andando no meio da tecla {norte.Andando}; ritmo {Visual?.RitmoDoPasso:0.00}");

		Medida sul = await Segurar("move_down", 1.0, correndo: true);
		Checa("A4. **O SHIFT NAO CORRE**: com a tecla de correr segurada o preso anda no mesmo passo -- e o servidor nao concede corrida",
			sul.Andou > 4f && sul.NoPasso && !srv.RastejoNaFoto(id).Correndo && sul.Correcoes == 0, sul.ToString());

		LocalPlayer.RastejoIgnoradoDeTeste = true;
		Medida parado;
		try { parado = await Segurar("move_up", 0.6); }
		finally { LocalPlayer.RastejoIgnoradoDeTeste = false; }
		Injeta("(o cliente ignora o bit: o jogo de antes) a regua A1 REPROVA -- o preso nao sai do lugar e o motivo e 'prensado'",
			parado.Andou < 0.5f && parado.Motivo.StartsWith("prensado", StringComparison.Ordinal), parado.ToString());

		LocalPlayer.RastejoCorreDeTeste = true;
		Medida correu;
		try { correu = await Segurar("move_down", 0.6, correndo: true); }
		finally { LocalPlayer.RastejoCorreDeTeste = false; }
		Injeta("(quem se arrasta pede a corrida) a regua A4 REPROVA -- o cliente sai no passo de corrida, alem do que a ficha da",
			correu.Andou > 1.5f * correu.Esperado, correu.ToString());
		await Segundos(0.5);   // as correcoes desse contra-exemplo nao contam pra frente
	}

	// ---------------------------------------------------------------- B
	private async Tarefa ODesmaiadoPelaGravidade(GameClient cli, Jandirus.Server.GameServer srv, LocalPlayer corpo, int id, int maq)
	{
		Nota("--- B: o desmaiado pela gravidade (a camara a 1x: razao 2) se arrasta pra fora do campo ---");
		srv.CampoDoRastejo(id, maq, 1, 4);
		bool solto = await Ate(() => !cli.Sheet.SoRasteja && !cli.Sheet.KO, 4);
		Checa("(montagem) na razao 2 o corpo de pe nao esta preso (o bit apaga)", solto, $"razao {srv.RastejoNaFoto(id).Razao:0.##}");
		_ouvido.Clear();
		Checa("(montagem) o corpo na beira do desmaio: o proximo tique da gravidade o derruba", srv.NaBeiraDoDesmaioNaFoto(id));
		bool caiu = await Ate(() => cli.Sheet.KO && cli.Sheet.SoRasteja, 4);
		var s = srv.RastejoNaFoto(id);
		Checa("B0. **A GRAVIDADE DERRUBA, E A FICHA DIZ QUE ELE SE ARRASTA**: o tique de 1 Hz desmaiou o corpo, o servidor marcou o desmaio, e o bit chegou",
			caiu && s.Ko && s.DaGravidade, $"ko {s.Ko}; marca {s.DaGravidade}; bit {cli.Sheet.SoRasteja}");
		bool disse = await Ate(() => Ouvi("ainda consegue se arrastar"), 2);
		Checa("B0b. o chat diz que ele ainda se arrasta", disse, Ultimas());
		await Segundos(0.8);   // o corpo deita na tela
		float giro = Visual?.RotationDegrees ?? float.NaN;
		Facing olhar = corpo.OlharDeTeste;
		Checa("(montagem) deitado na tela: o quadro de nocaute", Visual?.AnimacaoDeTeste.StartsWith("ko", StringComparison.Ordinal) == true,
			$"animacao {Visual?.AnimacaoDeTeste}; giro {giro:0}");
		GuardarATela("rastejo-3-desmaiado.png", "3. desmaiado pela gravidade no campo, com a frase no chat");

		// pro leste, ate o servidor dizer que o campo nao o alcanca mais
		Medida pra = await Segurar("move_right", 12, ate: () => srv.RastejoNaFoto(id).Campo <= 0,
			foto: ("rastejo-4-arrastando.png", "4. arrastando-se deitado pra fora do campo"), fotoEm: 0.7);
		var fora = srv.RastejoNaFoto(id);
		Checa("B1. **O DESMAIADO SE ARRASTA PRA FORA DO CAMPO**: a tecla de andar o leva, deitado, ate o campo nao o alcancar mais",
			fora.Campo <= 0 && fora.Ko && pra.Andou > 8f, $"campo {fora.Campo:0.#}x; ko {fora.Ko}; {pra}");
		Checa("B2. **A PREVISAO DO CLIENTE E O PASSO DO SERVIDOR CONCORDAM**: no passo da ficha, sem correcao nenhuma, e o corpo do servidor esta onde o meu esta",
			pra.NoPasso && pra.Correcoes == 0 && (fora.Pos - corpo.PosicaoExataDeTeste).Length < 3f,
			$"{pra}; servidor {fora.Pos.X:0.0},{fora.Pos.Y:0.0} x cliente {corpo.PosicaoExataDeTeste.X:0.0},{corpo.PosicaoExataDeTeste.Y:0.0}");
		Checa("B3. **DESLIZA COMO CAIU**: o quadro de nocaute, o giro do corpo deitado e o olhar nao mudaram no caminho",
			pra.SempreDeitado && Visual != null && Mathf.IsEqualApprox(Visual.RotationDegrees, giro) && corpo.OlharDeTeste == olhar,
			$"sempre deitado {pra.SempreDeitado}; giro {giro:0} -> {Visual?.RotationDegrees:0}; olhar {olhar} -> {corpo.OlharDeTeste}");
		await Segundos(0.3);
		GuardarATela("rastejo-5-fora-do-campo.png", "5. fora do campo, ainda desmaiado");

		Medida laFora = await Segurar("move_right", 0.8);
		Checa("B4. **FORA DO CAMPO, AINDA DESMAIADO, CONTINUA SE ARRASTANDO** (o desmaio e o mesmo ate levantar)",
			srv.RastejoNaFoto(id).Ko && laFora.Andou > 1f && laFora.NoPasso && laFora.Correcoes == 0, laFora.ToString());

		LocalPlayer.RastejoIgnoradoDeTeste = true;
		Medida parado;
		try { parado = await Segurar("move_right", 0.6); }
		finally { LocalPlayer.RastejoIgnoradoDeTeste = false; }
		Injeta("(o cliente ignora o bit: o jogo de antes) a regua B1 REPROVA -- caido, o corpo nao sai do lugar e o motivo e 'caido'",
			parado.Andou < 0.5f && parado.Motivo.StartsWith("caido", StringComparison.Ordinal), parado.ToString());

		LocalPlayer.DeitadoViraNoRastejoDeTeste = true;
		try { await Segurar("move_up", 0.4); }
		finally { LocalPlayer.DeitadoViraNoRastejoDeTeste = false; }
		Injeta("(o deitado vira pro passo) a regua B3 REPROVA -- o olhar segue a tecla", corpo.OlharDeTeste != olhar,
			$"olhar {olhar} -> {corpo.OlharDeTeste}");
	}

	// =====================================================================
	// A MEDIDA DA TECLA
	// =====================================================================
	/// <summary>O que uma tecla segurada fez: quanto o corpo andou NA TELA (a posicao exata que o cliente previu), quanto devia andar no
	/// passo da ficha (somado quadro a quadro), as correcoes que chegaram nesse tempo, se a pose de andar apareceu, se o corpo ficou
	/// deitado o caminho inteiro, e o porque de nao andar (se houve).</summary>
	private readonly record struct Medida(float Andou, double Esperado, int Correcoes, bool Andando, bool SempreDeitado, string Motivo)
	{
		/// <summary>Andou no passo da ficha (a folga e a do quadro em que a tecla desce e o do teto do quadro longo).</summary>
		public bool NoPasso => Esperado > 0 && Andou >= 0.8 * Esperado && Andou <= 1.15 * Esperado;

		public override string ToString() =>
			$"andou {Andou:0.0} px de {Esperado:0.0} no passo da ficha; correcoes {Correcoes}; pose de andar {Andando}; deitado {SempreDeitado}; motivo '{Motivo}'";
	}

	/// <summary>
	/// SEGURA A TECLA `acao` por ate `segundos` (com a de correr junto, se pedido), ou ate `ate` responder sim, e mede (ver <see cref="Medida"/>).
	/// A foto, se pedida, sai `fotoEm` segundos depois de a tecla descer.
	/// </summary>
	private async System.Threading.Tasks.Task<Medida> Segurar(string acao, double segundos, bool correndo = false, Func<bool>? ate = null,
		(string Nome, string Legenda)? foto = null, double fotoEm = 0.6)
	{
		LocalPlayer corpo = Corpo!;
		Vec2 de = corpo.PosicaoExataDeTeste;
		int correcoes = _correcoes;
		double esperado = 0;
		bool andando = false, sempreDeitado = true, fotografou = foto == null;
		string motivo = "";
		ulong t0 = Time.GetTicksMsec(), antes = t0;
		Input.ActionPress(Teclas.NomeNoMotor(acao));
		if (correndo) Input.ActionPress(Teclas.NomeNoMotor("run"));
		try
		{
			while ((Time.GetTicksMsec() - t0) / 1000.0 < segundos && !(ate?.Invoke() ?? false))
			{
				await Quadros(1);
				ulong agora = Time.GetTicksMsec();
				esperado += MoveRules.SpeedPx(corpo.SpeedStat) * Math.Min((agora - antes) / 1000.0, MoveRules.TetoDoQuadroDoCliente);
				antes = agora;
				andando |= Visual?.AndandoDeTeste == true;
				if (corpo.CaidoDeTeste) sempreDeitado &= Visual?.AnimacaoDeTeste.StartsWith("ko", StringComparison.Ordinal) == true;
				if (motivo.Length == 0) motivo = corpo.PorQueNaoAnda;
				if (!fotografou && (agora - t0) / 1000.0 >= fotoEm)
				{
					GuardarATela(foto!.Value.Nome, foto.Value.Legenda);
					fotografou = true;
				}
			}
			// (a tecla acabou antes da hora da foto -- o `ate` chegou cedo: a foto sai agora, ainda com a tecla descida)
			if (!fotografou) GuardarATela(foto!.Value.Nome, foto.Value.Legenda);
		}
		finally
		{
			Input.ActionRelease(Teclas.NomeNoMotor(acao));
			if (correndo) Input.ActionRelease(Teclas.NomeNoMotor("run"));
		}
		await Segundos(0.25);   // os ultimos pacotes chegam (e uma correcao atrasada, se houver, tambem)
		return new Medida((corpo.PosicaoExataDeTeste - de).Length, esperado, _correcoes - correcoes, andando, sempreDeitado, motivo);
	}

	private bool Ouvi(string trecho) => _ouvido.Exists(t => t.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private string Ultimas() => string.Join(" | ", _ouvido.TakeLast(4));

	private void GuardarATela(string nome, string legenda)
	{
		Image? tela = GetViewport()?.GetTexture()?.GetImage();
		if (tela == null || tela.IsEmpty()) { Nota($"  --     {legenda}: sem janela, sem foto"); return; }
		tela.Convert(Image.Format.Rgba8);
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + nome);
			tela.SavePng(caminho);
			Nota($"  foto   {legenda}: {caminho}");
		}
		catch (Exception e) { Nota($"  --     {legenda}: sem foto: {e.Message}"); }
	}

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
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
}
