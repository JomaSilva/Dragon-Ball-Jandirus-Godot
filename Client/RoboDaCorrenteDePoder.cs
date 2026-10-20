using Godot;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// A BANCADA DE TELA DA CORRENTE DE PODER (`--diagcorrente`) -- o efeito do DU quando alguem da poder a um amigo (dono, 2026-10-19). O
/// porque do desenho esta em <see cref="CorrenteDePoder"/>; o fio, em `GameServer.CorrenteDePoder.cs`.
///
/// O ROBO E QUEM ASSISTE: os dois que doam e recebem sao corpos de palco ao lado dele (`GameServer.CorrentePalco.cs`), e a doacao e a de
/// producao -- o `Give_Power` pelo canal de habilidade, as doses do efetor de 5 Hz e o folego a cada uma. Assistir e o ponto: o pacote
/// de antes (`MandarEfeito("dando_poder")`) ia so pra tela de quem DA, e quem estava em volta nunca via nada.
///
///   1. A CORRENTE NA TELA DE QUEM ASSISTE: os folegos chegam; as bolinhas voam em fluxo (varias no ar ao mesmo tempo, uma a cada decimo
///      de segundo), com a arte do DU, do doador pro amigo, por cima dos corpos.
///   2. O FIM: a doacao para, as ultimas bolinhas chegam e a corrente sai da arvore -- sem pacote de fim.
///   3. CONTRA-EXEMPLO DE SERVIDOR (`CorrenteSoProDoadorDeTeste`): o folego vai so pra tela de quem da -- quem assiste nao ve nada.
///   4. CONTRA-EXEMPLO DE CLIENTE (`CorrenteDePoder.ApagadaDeTeste`): o folego chega e nada e desenhado -- o jogo de antes.
///
/// COM JANELA ela fotografa a corrente (`user://corrente-NN-*.png`): a cor, a forma e o tamanho, que numero nenhum daqui mede.
///
///     Godot --path . --host --rede 8762 --diagcorrente --raca Human --conta bancada_dcorrente --nome Olheiro
/// </summary>
public partial class RoboDaCorrenteDePoder : Node
{
	private int _ok, _falha, _injOk, _injFalha;
	private readonly List<string> _reprovadas = [];

	/// <summary>Os folegos (`S2C.DandoPoder`) que chegaram a ESTE cliente, por doador.</summary>
	private readonly Dictionary<int, int> _folegos = [];

	private bool _escutando;
	private int _fotos;

	private static void Nota(string s) => GD.Print("[dcorrente] " + s);

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
	private static World? W => World.Instancia;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	public override void _Ready() => _ = Rodar();

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

	private void AoFolego(int doador, int alvo) => _folegos[doador] = _folegos.GetValueOrDefault(doador) + 1;

	private void Encerrar(int codigo)
	{
		// O MUNDO VOLTA: nenhum defeito, nenhum corpo de palco e nenhum ouvido sobrevivem ao roteiro.
		S?.PalcoC_Limpar();
		CorrenteDePoder.ApagadaDeTeste = false;
		if (_escutando && C is { } cli) cli.DandoPoder -= AoFolego;
		CamadasDeTela(true);

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
		Nota("==================================================================================");
		Nota(" A CORRENTE DE PODER DO DU -- quem da poder a um amigo, visto por quem esta em volta");
		Nota("==================================================================================");

		bool pronto = await Ate(() => C is { Connected: true } c && c.LocalId != 0 && S != null && W?.DesenhadoDeTeste(c.LocalId) != null, 90);
		Checa("o mundo chegou (conexao, corpo na tela) e o servidor e deste processo (--host)", pronto);
		if (!pronto || C is not { } cli || S is not { } srv || W is not { } w) { Encerrar(2); return; }
		cli.DandoPoder += AoFolego;
		_escutando = true;
		int eu = cli.LocalId;

		// A CENA: o doador e o amigo, um de cada lado do robo e uma fileira abaixo -- a corrente passa rente aos pes de quem assiste. Acima
		// nao: o robo nasce numa sala com os moveis encostados na parede do fundo, e na primeira foto o amigo sumiu atras do computador
		// (a corrente chegava nele, mas a foto nao mostrava quem recebe). Os doadores dos contra-exemplos, que nao saem em foto, ficam la.
		int doador = srv.PalcoC_Corpo(eu, 4f, 1f, "Doador");
		int amigo = srv.PalcoC_Corpo(eu, -4f, 1f, "Amigo");
		bool veOsDois = await Ate(() => w.DesenhadoDeTeste(doador) != null && w.DesenhadoDeTeste(amigo) != null, 10);
		Checa("(montagem) o doador e o amigo estao na tela do robo, um de cada lado", veOsDois && doador != 0 && amigo != 0);
		if (!veOsDois) { Encerrar(1); return; }
		await Segundos(0.5);

		// ---------------------------------------------------------------- 1. a corrente na tela de quem assiste
		Nota("-- 1) o doador da poder ao amigo; o robo so assiste --");
		bool comecou = srv.PalcoC_DarPoder(doador, amigo);
		Checa("(montagem) o doador comecou a dar poder ao amigo pelo `Give_Power` de producao", comecou);
		bool chegou = await Ate(() => _folegos.GetValueOrDefault(doador) >= 3, 5);
		Checa("1. **O FOLEGO CHEGA A QUEM SO ASSISTE**: o robo nao da nem recebe, e mesmo assim recebe a corrente a cada dose (`S2C.DandoPoder`)",
			  chegou, $"{_folegos.GetValueOrDefault(doador)} folego(s)");

		bool fluxo = await Ate(() => w.CorrenteDeTeste(doador) is { BolinhasNoArDeTeste: >= 3 }, 3);
		CorrenteDePoder? corrente = w.CorrenteDeTeste(doador);
		Checa("...e as bolinhas voam EM FLUXO: tres ou mais no ar ao mesmo tempo", fluxo, $"no ar: {corrente?.BolinhasNoArDeTeste ?? 0}");

		AnimatedSprite2D? bolinha = corrente?.GetChildren().OfType<AnimatedSprite2D>().FirstOrDefault();
		Checa("...com a ARTE DO DU (`Give Power.dmi`, convertida) e por cima dos corpos (o `layer+1` de la)",
			  bolinha?.SpriteFrames?.ResourcePath == CorrenteDePoder.Folha && corrente!.ZIndex > 0,
			  $"{bolinha?.SpriteFrames?.ResourcePath} z={corrente?.ZIndex}");

		int antes = CorrenteDePoder.BolinhasSoltasDeTeste;
		await Segundos(1.0);
		int porSegundo = CorrenteDePoder.BolinhasSoltasDeTeste - antes;
		Checa("...uma bolinha a cada DECIMO de segundo (o `sleep(1)` do laco do DU): perto de dez por segundo", porSegundo is >= 7 and <= 13,
			  $"{porSegundo} em 1 s");

		Vector2? deOnde = w.DesenhadoDeTeste(doador), praOnde = w.DesenhadoDeTeste(amigo);
		var ultima = CorrenteDePoder.UltimaBolinhaDeTeste;
		Checa("...e cada uma SAI DO DOADOR e mira o AMIGO (os dois pelo desenho do corpo, com a altura do voo)",
			  ultima is { } u && deOnde is { } de && praOnde is { } para && u.De.DistanceTo(de) < 2f && u.Para.DistanceTo(para) < 2f,
			  $"saiu de {ultima?.De} (doador {deOnde}), rumo a {ultima?.Para} (amigo {praOnde})");

		AnimatedSprite2D? voando = w.CorrenteDeTeste(doador)?.GetChildren().OfType<AnimatedSprite2D>().LastOrDefault();
		float distAntes = voando != null && praOnde is { } p0 ? voando.Position.DistanceTo(p0) : -1f;
		await Segundos(0.1);
		float distDepois = voando != null && IsInstanceValid(voando) && praOnde is { } p1 ? voando.Position.DistanceTo(p1) : 0f;
		Checa("...e ela VOA pro amigo: um decimo de segundo depois esta mais perto dele", distAntes > 0 && distDepois < distAntes,
			  $"{distAntes:0.#} -> {distDepois:0.#} px");

		await FotoDoMundo("corrente-de-perto", 3);
		await FotoDoMundo("corrente", 2);

		// ---------------------------------------------------------------- 2. o fim
		Nota("-- 2) a doacao para --");
		srv.PalcoC_Parar(doador);
		bool parou = await Ate(() => !srv.PalcoC_Doando(doador), 2);
		bool sumiu = await Ate(() => w.CorrenteDeTeste(doador) == null, 2);
		int depoisDoFim = CorrenteDePoder.BolinhasSoltasDeTeste;
		await Segundos(0.5);
		Checa("2. **O FIM**: a doacao para, as ultimas bolinhas chegam e a corrente sai da arvore -- sem pacote de fim",
			  parou && sumiu && CorrenteDePoder.BolinhasSoltasDeTeste == depoisDoFim,
			  $"parou={parou} sumiu={sumiu} novas depois do fim={CorrenteDePoder.BolinhasSoltasDeTeste - depoisDoFim}");

		// ---------------------------------------------------------------- 3. contra-exemplo de servidor
		Nota("-- 3) contra-exemplo: o folego so pra tela de quem da --");
		int doador2 = srv.PalcoC_Corpo(eu, 4f, -1.5f, "DoadorDois");
		await Ate(() => w.DesenhadoDeTeste(doador2) != null, 5);
		srv.PalcoC_SoProDoador(true);
		try
		{
			bool doando = srv.PalcoC_DarPoder(doador2, amigo);
			await Segundos(1.5);
			Injeta("o folego vai so pra tela de quem da (o `MandarEfeito` de antes): quem assiste nao recebe folego nenhum e nao ve bolinha "
				   + "nenhuma -- a regua do 1 REPROVA", doando && _folegos.GetValueOrDefault(doador2) == 0 && w.CorrenteDeTeste(doador2) == null,
				   $"doando={doando} folegos={_folegos.GetValueOrDefault(doador2)}");
		}
		finally
		{
			srv.PalcoC_SoProDoador(false);
			srv.PalcoC_Parar(doador2);
		}

		// ---------------------------------------------------------------- 4. contra-exemplo de cliente
		Nota("-- 4) contra-exemplo: o folego chega e nada e desenhado --");
		int doador3 = srv.PalcoC_Corpo(eu, -4f, -1.5f, "DoadorTres");
		await Ate(() => w.DesenhadoDeTeste(doador3) != null, 5);
		CorrenteDePoder.ApagadaDeTeste = true;
		try
		{
			int soltasAntes = CorrenteDePoder.BolinhasSoltasDeTeste;
			bool doando = srv.PalcoC_DarPoder(doador3, amigo);
			bool folegos = await Ate(() => _folegos.GetValueOrDefault(doador3) >= 3, 5);
			Injeta("o folego CHEGA e nada e desenhado (o jogo de antes): nenhuma bolinha voou -- a regua do 1 REPROVA",
				   doando && folegos && CorrenteDePoder.BolinhasSoltasDeTeste == soltasAntes && w.CorrenteDeTeste(doador3) == null,
				   $"folegos={_folegos.GetValueOrDefault(doador3)} bolinhas={CorrenteDePoder.BolinhasSoltasDeTeste - soltasAntes}");
		}
		finally
		{
			CorrenteDePoder.ApagadaDeTeste = false;
			srv.PalcoC_Parar(doador3);
		}

		Encerrar(_falha + _injFalha == 0 ? 0 : 1);
	}

	// =====================================================================
	// AJUDANTES
	// =====================================================================
	/// <summary>Sem janela nao ha o que fotografar -- e pedir a imagem ao renderizador de mentira so suja o log com um erro por foto.</summary>
	private static bool SemJanela => DisplayServer.GetName() == "headless";

	/// <summary>A tela inteira em `user://corrente-NN-nome.png`, com a camera no zoom pedido e sem HUD nem chat. SO COM JANELA.</summary>
	private async Tarefa FotoDoMundo(string nome, int zoom)
	{
		if (SemJanela || W is not { } w) return;
		int antes = (int)MathF.Round(w.ZoomDaCameraDeTeste);
		CamadasDeTela(false);
		w.AplicarZoom(zoom);
		try
		{
			await Quadros(3);
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img != null && !img.IsEmpty())
			{
				string caminho = ProjectSettings.GlobalizePath($"user://corrente-{++_fotos:00}-{nome}.png");
				img.SavePng(caminho);
				Nota($"  foto: {caminho}");
			}
		}
		catch (Exception e) { Nota($"  (sem foto de '{nome}': {e.Message})"); }
		finally
		{
			if (antes > 0) w.AplicarZoom(antes);
			CamadasDeTela(true);
		}
	}

	private static void CamadasDeTela(bool aVista)
	{
		if (Hud.Instancia is { } hud) hud.Visible = aVista;
		if (Chat.Instancia is { } chat) chat.Visible = aVista;
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
