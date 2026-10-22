using Godot;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// FOTOS DO TREINO FOCADO (<c>--diagtreinofocado</c>). A telinha do T, o anel com a letra, um acerto,
/// um erro, a barra da respiracao e o bonus na tela. As teclas sao as de producao.
///
///     Godot --janela --resolution 1280x720 --host --rede 10151 --diagtreinofocado --raca Human --conta bancada_foco --nome Focado
/// </summary>
public partial class RoboDeTreinoFocado : Node
{
	private int _ok;
	private readonly List<string> _falhas = [];

	private static GameClient? C => GameClient.Instance;
	private LocalPlayer? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as LocalPlayer;

	private static void Nota(string linha) => GD.Print("[dtreinofocado] " + linha);

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		Nota((ok ? "  ok     " : "  FALHA  ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ok) _ok++;
		else _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree()
	{
		TreinoFocado.WasdAndaNoFocadoDeTeste = false;
		TelaDeTreino.TelasJuntasDeTeste = false;
	}

	private async System.Threading.Tasks.Task Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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

	private static void Acao(string nome, bool embaixo)
	{
		if (embaixo) Godot.Input.ActionPress(Teclas.NomeNoMotor(nome));
		else Godot.Input.ActionRelease(Teclas.NomeNoMotor(nome));
	}

	private static void Toque(Key k)
	{
		Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true, Echo = false });
		Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
	}

	private void Foto(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty())
		{
			Conferir(false, $"foto {nome}", "quadro vazio");
			return;
		}
		string caminho = $"user://{nome}.png";
		img.SavePng(caminho);
		Nota("foto " + ProjectSettings.GlobalizePath(caminho));
		Conferir(true, $"foto {nome}");
	}

	private async System.Threading.Tasks.Task Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Conferir(false, "o roteiro correu ate o fim sem lancar excecao", e.GetType().Name + ": " + e.Message);
		}
		finally
		{
			TreinoFocado.WasdAndaNoFocadoDeTeste = false;
			TelaDeTreino.TelasJuntasDeTeste = false;
			Acao("train", false);
			Acao("move_right", false);
			Acao("meditate", false);
			Nota(_falhas.Count == 0
				? "===== TUDO OK ====="
				: $"===== {_falhas.Count} FALHA(S), {_ok} ok =====");
		}
	}

	private async System.Threading.Tasks.Task Percurso()
	{
		Nota("===== BANCADA DO TREINO FOCADO, PELAS FOTOS =====");
		bool pronto = await Ate(() => C is { Connected: true, LocalId: not 0 } && Corpo != null
			&& TelaDeTreino.Instancia != null && TelaDeMeditacao.Instancia != null, 90);
		Conferir(pronto, "F1. o mundo e as duas telinhas estao montados");
		if (!pronto || C is not { } cli || Corpo is not { } corpo) return;
		await Quadros(20);

		Acao("train", true);
		await Quadros(3);
		Acao("train", false);
		await Quadros(2);
		TelaDeTreino? t = TelaDeTreino.Instancia;
		List<string> botoes = t?.BotoesDesenhados() ?? [];
		Conferir(t is { NaTela: true } && botoes.Contains("Treinar normal") && botoes.Contains("Treino focado"),
			"F2. o T abre a telinha com as duas opcoes", string.Join(" | ", botoes));
		Foto("treinofocado-1-telinha-t");

		bool apertou = t?.ApertarDesenhado(EscolhaDoTreino.Focado) == true;
		bool anel = apertou && await Ate(() => TelaDoTreinoFocado.Instancia is { AnelNaTela: true, LetraNaTela: not "" }, 4);
		Conferir(anel, "F3. o anel abre com uma letra", TelaDoTreinoFocado.Instancia?.LetraNaTela ?? "");
		Foto("treinofocado-2-anel");

		string letra = TelaDoTreinoFocado.Instancia?.LetraNaTela ?? "";
		if (letra.Length == 1) Toque((Key)letra[0]);
		bool subiu = await Ate(() => cli.FocoBonus > TreinoFocado.PisoDoBonus + 0.05, 2);
		await Quadros(2);
		Conferir(subiu && (TelaDoTreinoFocado.Instancia?.BonusNaTela.Contains("1,1") == true
				|| TelaDoTreinoFocado.Instancia?.BonusNaTela.Contains("1.1") == true),
			"F4. o acerto sobe o bonus na tela", TelaDoTreinoFocado.Instancia?.BonusNaTela ?? "");
		Foto("treinofocado-3-acerto");
		Foto("treinofocado-6-bonus");

		await Ate(() => cli.FocoLetra != '\0' && cli.FocoLetra.ToString() != letra, 2);
		char errada = cli.FocoLetra == 'W' ? 'A' : 'W';
		double bonus = cli.FocoBonus;
		Toque((Key)errada);
		bool caiu = await Ate(() => cli.FocoBonus < bonus - 0.05, 2);
		await Quadros(2);
		Conferir(caiu, "F5. o erro derruba o bonus", cli.FocoBonus.ToString("0.00"));
		Foto("treinofocado-4-erro");

		// (2026-10-21, r3) A MESMA REGUA NO F6 E NO F6b, com o D seguro por 30 quadros: com 12, o corpo do defeito andou 6,7 px numa
		// rodada lenta (o x9 da r2) e o F6b falhou sem motivo -- meio segundo anda bem mais que os 8 px da regua. Os 4 quadros depois
		// de soltar sao os da atividade que o servidor manda (andar tira do treino).
		async System.Threading.Tasks.Task<(bool parado, float andou)> FicaNoFocadoComOD()
		{
			Vec2 de = new(corpo.Position.X, corpo.Position.Y);
			Acao("move_right", true);
			await Quadros(30);
			Acao("move_right", false);
			await Quadros(4);
			float d = (float)Math.Sqrt(Math.Pow(corpo.Position.X - de.X, 2) + Math.Pow(corpo.Position.Y - de.Y, 2));
			return (d < 8f && corpo.AtividadeDeTeste == Protocol.Activity.Treinando, d);
		}
		var (parado, andou) = await FicaNoFocadoComOD();
		Conferir(parado, "F6. no focado o WASD nao anda e nao derruba o treino", $"andou {andou:0.#}");

		try
		{
			TreinoFocado.WasdAndaNoFocadoDeTeste = true;
			var (paradoComDefeito, comDefeito) = await FicaNoFocadoComOD();
			Conferir(!paradoComDefeito, "F6b. (defeito injetado: o WASD anda no focado) a mesma regua REPROVA",
				$"andou {comDefeito:0.#}; atividade {corpo.AtividadeDeTeste}");
		}
		finally { TreinoFocado.WasdAndaNoFocadoDeTeste = false; }

		// (2026-10-21, revisao) O F7 MEDE O T QUE PARA O FOCADO -- e nao o andar do F6b. O defeito do F6b anda o corpo, e andar ja tira do
		// treino: o T seguinte abria a telinha (em vez de parar) e o F7 passava pelo motivo errado. Aqui o focado volta antes.
		if (corpo.AtividadeDeTeste != Protocol.Activity.Treinando || cli.FocoModo != TreinoFocado.ModoTreino)
		{
			Acao("train", true);
			await Quadros(3);
			Acao("train", false);
			await Quadros(2);
			TelaDeTreino.Instancia?.ApertarDesenhado(EscolhaDoTreino.Focado);
			await Ate(() => cli.FocoModo == TreinoFocado.ModoTreino && corpo.AtividadeDeTeste == Protocol.Activity.Treinando, 4);
		}
		bool treinandoAntes = corpo.AtividadeDeTeste == Protocol.Activity.Treinando;
		Acao("train", true);
		await Quadros(3);
		Acao("train", false);
		await Quadros(4);
		Conferir(treinandoAntes && corpo.AtividadeDeTeste == Protocol.Activity.Parado && TelaDeTreino.Instancia is not { NaTela: true },
			"F7. o T de novo para o treino focado (e nao abre a telinha)", $"treinava {treinandoAntes}");

		// (2026-10-21, revisao) F8b. UMA TELINHA POR VEZ: com a do T aberta, o M a fecha e abre a dele -- na r1 as duas ficavam na
		// tela, e a barra da respiracao nascia por cima das opcoes do treino.
		async System.Threading.Tasks.Task<bool> UmaPorVez()
		{
			Acao("train", true);
			await Quadros(3);
			Acao("train", false);
			await Quadros(2);
			bool tAberta = TelaDeTreino.Instancia is { NaTela: true };
			Acao("meditate", true);
			await Quadros(3);
			Acao("meditate", false);
			await Quadros(2);
			bool so = tAberta && TelaDeTreino.Instancia is not { NaTela: true } && TelaDeMeditacao.Instancia is { NaTela: true };
			// E O CONTRARIO: com a do M aberta (a de agora), o T a fecha e abre a dele.
			Acao("train", true);
			await Quadros(3);
			Acao("train", false);
			await Quadros(2);
			bool volta = TelaDeTreino.Instancia is { NaTela: true } && TelaDeMeditacao.Instancia is not { NaTela: true };
			TelaDeTreino.Instancia?.Fechar();
			TelaDeMeditacao.Instancia?.Fechar();
			await Quadros(2);
			return so && volta;
		}
		Conferir(await UmaPorVez(), "F8b. uma telinha por vez: com a do T aberta o M a fecha e abre a dele, e com a do M o T faz o mesmo");
		bool juntas;
		try
		{
			TelaDeTreino.TelasJuntasDeTeste = true;
			juntas = !await UmaPorVez();
		}
		finally { TelaDeTreino.TelasJuntasDeTeste = false; }
		Conferir(juntas, "F8c. (defeito injetado: as telinhas abrem uma por cima da outra) a mesma regua REPROVA");

		Acao("meditate", true);
		await Quadros(3);
		Acao("meditate", false);
		await Quadros(2);
		TelaDeMeditacao? m = TelaDeMeditacao.Instancia;
		List<string> doM = m?.BotoesDesenhados() ?? [];
		Conferir(m is { NaTela: true } && doM.Contains("Meditar normal") && doM.Contains("Meditação profunda")
				&& doM.Contains("Meditação focada"),
			"F8. o M mostra a focada ao lado da normal e da profunda", string.Join(" | ", doM));
		bool focada = m?.ApertarFocada() == true;
		bool barra = focada && await Ate(() => TelaDoTreinoFocado.Instancia is { BarraNaTela: true }, 4);
		Conferir(barra, "F9. a meditacao focada abre a barra da respiracao",
			TelaDoTreinoFocado.Instancia?.TituloNaTela ?? "");
		Foto("treinofocado-5-respiracao");
	}
}
