using Godot;
using Jandirus.Core.World;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DOS PEDACOS DO MAJIN (`--diagpedacos`, 2026-10-18): o desfeito INTEIRO, nos 30 s de verdade, na tela. O dono: *"os pedaços por
/// mais q separados se eu bater onde estava o corpo do majin eles viajam juntos [...] eles tinham q ser separados e imunes a golpes etc e
/// irem se movendo lentamente ate se encontrarem, e ao se encontrarem deveriam começar a animaçao a qual circulei no print e ao chegarem
/// nos naqueles 4 sprites finais da imagem ficaria em um loop desses 4 sprites por uns segundos (uns 5 segundos)"*.
///
/// Com janela e com `--host`, nascendo Humano no campo aberto da Terra, com um figurante Majin ao lado (o palco da v0.3.0). O figurante se
/// desfaz pela porta do Core (`CombatState.Desfazer`) e o relogio do servidor corre sozinho ate ele levantar:
///   1. os cinco pedacos espalhados em volta do lugar -- foto `pedacos-1-espalhados.png`;
///   2. o GOLPE no lugar, pela porta de producao do soco pesado: o servidor nao arremessa o corpo, e os pedacos nao saem do chao;
///   3. o corpo DESLOCADO no servidor (o "e se ele andasse?"): o node do corpo vai, e os pedacos ficam -- foto `pedacos-2-presos-ao-chao.png`;
///   4. no meio do caminho, os cinco mais perto do centro e ainda separados -- foto `pedacos-3-arrastando.png`;
///   5. o ENCONTRO, na hora marcada: a `regenerate` da folha do corpo comeca -- foto `pedacos-4-encontro.png`;
///   6. os QUATRO ULTIMOS QUADROS em laco -- foto `pedacos-5-laco.png` --, ate o corpo levantar nos 30 s, uns 5 s depois do laco comecar;
///   7. refeito -- foto `pedacos-6-refeito.png`;
///   8. CONTRA-EXEMPLOS: o corpo desfeito volta a voar com o golpe (`GameServer.DesfeitoVoaDeTeste`), e os pedacos voltam a andar com o
///      corpo (`PedacosDoMajin.PresosAoCorpoDeTeste`) -- foto `pedacos-7-defeito-presos.png`.
///
///     Godot --path . --host --rede 8462 --campoteste 12 --horateste 0.5 --semfauna --diagpedacos --raca Human --conta bancada_pedacos --nome Pedreiro
/// </summary>
public partial class RoboDosPedacos : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	private int _id;
	private RemotePlayer? Corpo() => World.Instancia?.FindChild($"Remoto{_id}", true, false) as RemotePlayer;
	private CharacterVisual? Boneco() => Corpo()?.GetNodeOrNull<CharacterVisual>("Visual");
	private PedacosDoMajin? Pedacos() => Corpo()?.GetNodeOrNull<PedacosDoMajin>(PedacosDoMajin.NomeDoNode);

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Fechar();
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo nasceu e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = cli.LocalId;
		await Segundos(1.5);   // o corpo assenta no campo
		GD.Print("[dpedacos] ===== O MAJIN DESFEITO, OS 30 S NA TELA (dono, 2026-10-18) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);

		_id = srv.PalcoV21_Figurante(eu, "Majin", "", 3);
		bool chegou = _id != 0 && await Ate(() => Boneco() is { Visible: true } b && b.CorpoParaOsPedacos() != null, 8);
		Conferir(chegou, $"(montagem) o figurante Majin apareceu ao lado, de pe e desenhado (id {_id})");
		if (!chegou) return;
		try
		{
			await OsTrintaSegundos(srv, eu);
			await OsContraExemplos(srv, eu);
		}
		finally
		{
			Jandirus.Server.GameServer.DesfeitoVoaDeTeste = false;
			PedacosDoMajin.PresosAoCorpoDeTeste = false;
			srv.PalcoV30_Refazer(_id);
			srv.PalcoV21_TirarOFigurante(_id);
		}
	}

	private async Tarefa OsTrintaSegundos(Jandirus.Server.GameServer srv, int eu)
	{
		SpriteFrames? esperada = PedacosDoMajin.FolhaDaReconstrucao(Boneco()?.CorpoParaOsPedacos()?.Folha);
		ulong t0 = Time.GetTicksMsec();
		double Desde() => (Time.GetTicksMsec() - t0) / 1000.0;

		// ---- 1. espalhados ----
		Conferir(srv.PalcoV30_Desfazer(_id), "o figurante se desfaz pela porta do Core (`CombatState.Desfazer`)");
		bool emPedacos = await Ate(() => Pedacos()?.PedacosDeTeste().Count == PedacosDoMajin.Quantos && Boneco() is { Visible: false }, 4);
		await Ate(() => Desde() >= 1.0, 3);
		PedacosDoMajin? p = Pedacos();
		Vector2 ancora = p?.AncoraDeTeste ?? default;
		List<Vector2> noComeco = p?.PosicoesDeTeste() ?? [];
		List<float> distanciaNoComeco = [.. noComeco.Select(x => x.DistanceTo(ancora))];
		Conferir(emPedacos && noComeco.Count == PedacosDoMajin.Quantos && distanciaNoComeco.All(d => d > 8)
				 && noComeco.Distinct().Count() == PedacosDoMajin.Quantos,
				 $"1. ESPALHADOS: o corpo some e os cinco pedacos caem SEPARADOS em volta do lugar (distancias ao centro: "
				 + $"{string.Join(", ", distanciaNoComeco.Select(d => d.ToString("0")))} px)");
		Fotografar("user://pedacos-1-espalhados.png");

		// ---- 2. o golpe no lugar ----
		(Vec2 antes, _) = srv.PalcoV30_OndeEsta(_id);
		List<Vector2> antesDoGolpe = Pedacos()?.PosicoesDeTeste() ?? [];
		bool voou = srv.PalcoV30_Arremessar(eu, _id);
		await Segundos(0.6);
		(Vec2 depois, bool voando) = srv.PalcoV30_OndeEsta(_id);
		List<Vector2> depoisDoGolpe = Pedacos()?.PosicoesDeTeste() ?? [];
		float maiorPasso = antesDoGolpe.Count == depoisDoGolpe.Count && antesDoGolpe.Count > 0
			? antesDoGolpe.Zip(depoisDoGolpe, (a, b) => a.DistanceTo(b)).Max() : float.MaxValue;
		Conferir(!voou && !voando && Vec2.Distance(antes, depois) < 0.5f && maiorPasso < 8,
				 $"2. O GOLPE NO LUGAR (a porta do soco pesado, `Arremessar`): o servidor NAO arremessa o corpo desfeito (andou "
				 + $"{Vec2.Distance(antes, depois):0.#} px) e nenhum pedaco sai do chao (o maior passo em 0,6 s foi {maiorPasso:0.#} px, so o arrasto)");

		// ---- 3. o corpo deslocado: os pedacos ficam ----
		float tile = ZoneCollision.TileSize;
		Vector2 corpoAntes = Corpo()?.GlobalPosition ?? default;
		List<Vector2> antesDoDesvio = Pedacos()?.PosicoesDeTeste() ?? [];
		srv.PalcoV30_Deslocar(_id, new Vec2(3 * tile, 0));
		await Ate(() => (Corpo()?.GlobalPosition ?? default).DistanceTo(corpoAntes) > 2 * tile, 3);
		await Segundos(0.3);
		float corpoAndou = (Corpo()?.GlobalPosition ?? default).DistanceTo(corpoAntes);
		List<Vector2> depoisDoDesvio = Pedacos()?.PosicoesDeTeste() ?? [];
		float maiorDesvio = antesDoDesvio.Count == depoisDoDesvio.Count && antesDoDesvio.Count > 0
			? antesDoDesvio.Zip(depoisDoDesvio, (a, b) => a.DistanceTo(b)).Max() : float.MaxValue;
		Conferir(corpoAndou > 2 * tile && maiorDesvio < 10 && (Pedacos()?.GlobalPosition ?? default).DistanceTo(ancora) < 1,
				 $"3. PRESOS AO CHAO: o corpo desfeito foi levado {corpoAndou:0} px pro leste no servidor, e os pedacos ficaram onde ele se desfez "
				 + $"(o maior passo deles foi {maiorDesvio:0.#} px, so o arrasto)");
		Fotografar("user://pedacos-2-presos-ao-chao.png");
		srv.PalcoV30_Deslocar(_id, new Vec2(-3 * tile, 0));
		await Ate(() => (Corpo()?.GlobalPosition ?? default).DistanceTo(corpoAntes) < tile, 3);

		// ---- 4. no meio do caminho ----
		await Ate(() => Desde() >= 12, 14);
		List<Vector2> noMeio = Pedacos()?.PosicoesDeTeste() ?? [];
		List<float> distanciaNoMeio = [.. noMeio.Select(x => x.DistanceTo(ancora))];
		bool maisPerto = distanciaNoMeio.Count == distanciaNoComeco.Count
						 && distanciaNoMeio.Zip(distanciaNoComeco, (m, c) => m < c * 0.8f).All(x => x);
		Conferir(maisPerto && noMeio.Distinct().Count() == PedacosDoMajin.Quantos && Pedacos()?.RefazendoDeTeste.Anim == "",
				 $"4. LENTAMENTE: aos 12 s os cinco estao mais perto do centro (agora {string.Join(", ", distanciaNoMeio.Select(d => d.ToString("0")))} px) "
				 + "e ainda separados -- e o encontro ainda nao aconteceu");
		Fotografar("user://pedacos-3-arrastando.png");

		// ---- 5. o encontro ----
		bool encontrou = await Ate(() => Pedacos()?.RefazendoDeTeste.Anim == PedacosDoMajin.AnimDaReconstrucao, 20);
		double horaDoEncontro = Desde();
		(double _, double marcado) = Pedacos()?.RelogioDeTeste ?? (0, 0);
		(SpriteFrames? folha, string anim) = Pedacos()?.RefazendoDeTeste ?? (null, "");
		Conferir(encontrou && Pedacos()?.PedacosDeTeste().Count == 0 && ReferenceEquals(folha, esperada) && esperada != null
				 && Math.Abs(horaDoEncontro - marcado) < 1.5 && horaDoEncontro > 15,
				 $"5. O ENCONTRO: aos {horaDoEncontro:0.0} s (marcado {marcado:0.0} s, pra fechar com os 30 do servidor) os pedacos se juntam e a "
				 + $"`regenerate` da folha do corpo comeca ({folha?.ResourcePath} '{anim}'; esperada {esperada?.ResourcePath})");
		Fotografar("user://pedacos-4-encontro.png");

		// ---- 6. o laco dos quatro ultimos ----
		bool noLaco = await Ate(() => Pedacos() is { } pp && pp.QuadroDeTeste.Total > 0
										 && pp.QuadroDeTeste.Quadro >= pp.QuadroDeTeste.Total - PedacosDoMajin.QuadrosDoLaco, 6);
		double inicioDoLaco = Desde();
		// O LACO NO RELOGIO DO SERVIDOR: a tela conta o desfeito no relogio de parede, e o servidor deste processo atrasa com a janela sem foco
		// (os 30 s dele viram 35 de parede). O que se afirma e o desenho: o laco comeca quando o servidor tem uns 5 s de desfeito pela frente.
		double restoNoLaco = srv.PalcoV30_RestoDoNocaute(_id);
		var vistos = new HashSet<int>();
		bool soOsQuatro = true;
		int total = Pedacos()?.QuadroDeTeste.Total ?? 0;
		ulong fimDaAmostra = Time.GetTicksMsec() + 1500;
		while (Time.GetTicksMsec() < fimDaAmostra && Pedacos() != null)
		{
			(int q, int n) = Pedacos()!.QuadroDeTeste;
			if (n > 0) { vistos.Add(q); soOsQuatro &= q >= n - PedacosDoMajin.QuadrosDoLaco; }
			await Quadros(1);
		}
		Conferir(noLaco && soOsQuatro && vistos.Count >= 2 && restoNoLaco is > 3.5 and < 6.5,
				 $"6. O LACO: a reconstrucao chega aos quatro ultimos quadros e fica neles (vistos em 1,5 s: {string.Join(", ", vistos.OrderBy(x => x))} "
				 + $"de {total}) -- e comeca com {restoNoLaco:0.0} s de desfeito pela frente no relogio do servidor: *\"uns 5 segundos\"*");
		Fotografar("user://pedacos-5-laco.png");

		// ---- 7. refeito ----
		bool refeito = await Ate(() => Pedacos() == null && Boneco() is { Visible: true }, 12);
		double fim = Desde();
		Conferir(refeito && srv.PalcoV30_RestoDoNocaute(_id) == 0,
				 $"7. REFEITO: o corpo levanta quando o desfeito do servidor acaba (aos {fim:0.0} s de parede, {fim - inicioDoLaco:0.0} s depois do laco "
				 + "comecar -- o relogio de parede deste processo anda na frente do servidor sem foco)");
		await Segundos(0.4);
		Fotografar("user://pedacos-6-refeito.png");
	}

	private async Tarefa OsContraExemplos(Jandirus.Server.GameServer srv, int eu)
	{
		float tile = ZoneCollision.TileSize;

		// ---- o corpo desfeito volta a voar ----
		Jandirus.Server.GameServer.DesfeitoVoaDeTeste = true;
		try
		{
			srv.PalcoV30_Desfazer(_id);
			await Ate(() => Pedacos()?.PedacosDeTeste().Count == PedacosDoMajin.Quantos, 4);
			bool voou = srv.PalcoV30_Arremessar(eu, _id);
			Conferir(voou, "CONTRA-EXEMPLO (defeito injetado: o desfeito volta a ser arremessado): o mesmo golpe do passo 2 ARREMESSA o corpo "
						   + "desfeito -- a regua do passo 2 reprovaria");
			await Ate(() => !srv.PalcoV30_OndeEsta(_id).Voando, 4);
		}
		finally { Jandirus.Server.GameServer.DesfeitoVoaDeTeste = false; }
		srv.PalcoV30_Refazer(_id);
		await Ate(() => Pedacos() == null && Boneco() is { Visible: true }, 6);
		await Segundos(0.5);

		// ---- os pedacos voltam a andar com o corpo ----
		PedacosDoMajin.PresosAoCorpoDeTeste = true;
		Vec2 volta = default;
		try
		{
			srv.PalcoV30_Desfazer(_id);
			await Ate(() => Pedacos()?.PedacosDeTeste().Count == PedacosDoMajin.Quantos, 4);
			await Segundos(1.0);
			Vector2 ancora = Pedacos()?.AncoraDeTeste ?? default;
			volta = new Vec2(-3 * tile, 0);
			srv.PalcoV30_Deslocar(_id, new Vec2(3 * tile, 0));
			bool foram = await Ate(() => (Pedacos()?.GlobalPosition ?? ancora).DistanceTo(ancora) > 2 * tile, 3);
			await Segundos(0.3);
			Conferir(foram, $"CONTRA-EXEMPLO (defeito injetado: os pedacos presos ao CORPO, o jogo de antes): o corpo deslocado leva os cinco junto "
							+ $"({(Pedacos()?.GlobalPosition ?? ancora).DistanceTo(ancora):0} px) -- a regua do passo 3 reprovaria");
			Fotografar("user://pedacos-7-defeito-presos.png");
		}
		finally
		{
			PedacosDoMajin.PresosAoCorpoDeTeste = false;
			if (volta.LengthSquared > 0) srv.PalcoV30_Deslocar(_id, volta);
		}
		srv.PalcoV30_Refazer(_id);
		await Ate(() => Pedacos() == null && Boneco() is { Visible: true }, 6);
	}

	private void Fotografar(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath(destino);
		img.SavePng(caminho);
		_linhas.Add("  --     foto salva em " + caminho);
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

	private void Fechar()
	{
		Jandirus.Server.GameServer.DesfeitoVoaDeTeste = false;
		PedacosDoMajin.PresosAoCorpoDeTeste = false;
		foreach (string l in _linhas) GD.Print("[dpedacos] " + l);
		GD.Print(_falhas.Count == 0 ? "[dpedacos] ===== TUDO OK =====" : $"[dpedacos] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dpedacos]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
