using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA SAIDA DA MEDITACAO PROFUNDA, PELO CLIENTE (`--diagtranse`).
///
/// O item 1a do playtest de 2026-10-10: *"adicionar um botao de saida (UI + tecla de atalho) que tire o jogador
/// da meditacao profunda a qualquer momento e devolva ao corpo com o estado limpo"*. A regra do servidor (o
/// corpo que volta como entrou, a faxina, os estados inconsistentes) esta na `--transeteste`; aqui se prova o
/// que so existe DESTE lado e PELO FIO:
///
///   1. Fora da mente a faixa do alto da tela nao existe.
///   2. O mergulho de verdade (os dois pacotes que a telinha do M manda) leva o cliente pra uma mente, e la a
///      faixa APARECE, com o botao dizendo a tecla que faz o mesmo.
///   3. O BOTAO acorda: o cliente volta pra zona de onde saiu, a faixa some, e a atividade local cai (o corpo
///      acorda parado -- sem isto o HUD ficava em "MEDITANDO" de um corpo que o servidor ja levantou).
///   4. (contraprova) Com o defeito injetado -- a tecla de meditar de antes --, apertar M la dentro NAO acorda.
///   5. Sem o defeito, a MESMA tecla acorda: e o "Meditate de novo" do original.
///
/// A TECLA E A DE VERDADE: `Input.ActionPress`, lida pelo `LocalPlayer.LerAtividade` de producao.
///
///     Godot --headless --path . --host --rede 7985 --diagtranse --raca Human --conta bancada_dtranse --nome Monge
/// </summary>
public partial class RoboDoTranse : Node
{
	private double _t;
	private int _passo, _esperas;
	private bool _acabou;
	private string _zonaDeFora = "";

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private static GameClient? C => GameClient.Instance;
	private LocalPlayer? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as LocalPlayer;

	public override void _Ready()
	{
		// ESTE ROBO PENSA ANTES DO CORPO (a licao do `RoboDeBorrao`): a tecla e lida com `IsActionJustPressed`,
		// que so vale no QUADRO em que ela desceu. Com a prioridade de fabrica o corpo processa antes deste no,
		// e a tecla descia depois de ele ja ter olhado.
		ProcessPriority = -100;
	}

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque + (ok || detalhe.Length == 0 ? "" : $"   [{detalhe}]"));
		if (!ok) _falhas.Add(oque);
	}

	/// <summary>Os dois pacotes que a telinha do M manda quando o jogador escolhe "Meditação profunda".</summary>
	private static void Mergulhar(GameClient cli)
	{
		cli.SendActivity(Protocol.Activity.Meditando);
		cli.SendHabilidade("mente");
	}

	/// <summary>A tecla de meditar, apertada e solta como um dedo: um quadro embaixo, e o proximo passo a solta.</summary>
	private static void ApertarATeclaDeMeditar() => Godot.Input.ActionPress(Teclas.NomeNoMotor("meditate"));
	private static void SoltarATeclaDeMeditar() => Godot.Input.ActionRelease(Teclas.NomeNoMotor("meditate"));

	public override void _Process(double delta)
	{
		if (_acabou || C is not { Connected: true } cli || SaidaDaMente.Instancia is not { } saida) return;

		_t += delta;
		if (_t < 0.5) return;
		_t = 0;

		switch (_passo++)
		{
			// -------------------------------------------------------------- fora da mente
			case 0:
				if (cli.LocalId == 0 || cli.Atributos.Raca is not { Length: > 0 } || Corpo == null) { _passo = 0; return; }
				_zonaDeFora = cli.Zone.Name;
				Conferir(!SaidaDaMente.NaMente && !saida.NaTela,
					"fora da mente a faixa de saida NAO esta na tela");
				_esperas = 0;
				Mergulhar(cli);
				break;

			// -------------------------------------------------------------- dentro: a faixa
			case 1:
				if (!SaidaDaMente.NaMente && ++_esperas < 30) { _passo = 1; return; }   // a onda leva 1,8 s
				Conferir(SaidaDaMente.NaMente, "o mergulho leva o cliente pra uma mente (a zona que chega pelo fio)", cli.Zone.Name);
				break;

			case 2:
				Conferir(saida.NaTela, "**DENTRO DA MENTE A FAIXA DE SAIDA ESTA NA TELA**");
				Conferir(saida.TextoDoBotao.StartsWith("Acordar", StringComparison.Ordinal)
					  && saida.TextoDoBotao.Contains(Teclas.Nome(Teclas.Teclado("meditate")[0]), StringComparison.Ordinal),
					"...e o botao diz a tecla que faz o mesmo (a de meditar)", saida.TextoDoBotao);
				_esperas = 0;
				saida.ApertarDeTeste();
				break;

			// -------------------------------------------------------------- o botao acorda
			case 3:
				if (SaidaDaMente.NaMente && ++_esperas < 20) { _passo = 3; return; }
				Conferir(!SaidaDaMente.NaMente && cli.Zone.Name == _zonaDeFora,
					"**O BOTAO ACORDA**: o cliente volta pra zona de onde saiu", cli.Zone.Name);
				break;

			case 4:
				Conferir(!saida.NaTela, "...a faixa some");
				Conferir(Corpo is { AtividadeDeTeste: Protocol.Activity.Parado },
					"...e a atividade local cai: o corpo acorda PARADO (o HUD nao fica em \"MEDITANDO\")",
					$"{Corpo?.AtividadeDeTeste}");
				_esperas = 0;
				Mergulhar(cli);
				break;

			// -------------------------------------------------------------- a tecla, com o defeito injetado
			case 5:
				if (!SaidaDaMente.NaMente && ++_esperas < 30) { _passo = 5; return; }
				Conferir(SaidaDaMente.NaMente, "(montagem) de volta dentro da mente pro teste da tecla");
				LocalPlayer.MNaoAcordaDeTeste = true;
				ApertarATeclaDeMeditar();
				break;

			case 6:
				SoltarATeclaDeMeditar();
				break;

			case 7:
				Conferir(SaidaDaMente.NaMente,
					"(defeito injetado: a tecla de meditar de antes) apertar M dentro da mente NAO acorda -- a regua sabe reprovar");
				LocalPlayer.MNaoAcordaDeTeste = false;
				_esperas = 0;
				ApertarATeclaDeMeditar();
				break;

			// -------------------------------------------------------------- a tecla acorda
			case 8:
				SoltarATeclaDeMeditar();
				break;

			case 9:
				if (SaidaDaMente.NaMente && ++_esperas < 20) { _passo = 9; return; }
				Conferir(!SaidaDaMente.NaMente && cli.Zone.Name == _zonaDeFora && !saida.NaTela,
					"**A TECLA DE MEDITAR ACORDA** (o \"Meditate de novo\" do original): de volta ao corpo, sem faixa", cli.Zone.Name);
				break;

			// -------------------------------------------------------------- relatorio
			default:
				_acabou = true;
				LocalPlayer.MNaoAcordaDeTeste = false;
				SoltarATeclaDeMeditar();
				GD.Print("\n[dtranse] ===== BANCADA DA SAIDA DA MEDITACAO PROFUNDA =====");
				foreach (string l in _passos) GD.Print("[dtranse] " + l);
				GD.Print(_falhas.Count == 0
					? "[dtranse] ===== TUDO OK ====="
					: $"[dtranse] ===== {_falhas.Count} FALHA(S) =====\n[dtranse]   " + string.Join("\n[dtranse]   ", _falhas));
				break;
		}
	}
}
