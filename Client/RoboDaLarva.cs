using Godot;
using Jandirus.Core.Races;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA LARVA DO BIO-ANDROIDE (`--diaglarva`, 2026-10-17). O adendo do dono -- a forma de barata a qualquer hora, selada (sempre
/// relaxada, 5 de poder, fora do sense e dos rastreadores), voando, e enterrada 5 min pra amadurecer de volta ao degrau em que estava -- e
/// *"n esqueça de checar tudo com print de tudo isso q estamos fazendo"*.
///
/// Com janela e com `--host`, nascendo Humano no campo aberto da Terra. O servidor deste processo vira o corpo do robo um bio de
/// laboratorio PERFEITO (`VirarBioDeLaboratorioDeTeste`, a premissa) e o robo aperta os verbs pelo FIO do cliente, como o botao e a tecla:
///   0. o bio perfeito -- foto `larva-0-perfeito.png`;
///   1. `larva`: o corpo vira a larva, e o servidor a sela (5 de poder, fora do sense) -- foto `larva-1-larva.png`;
///   2. `voar`: a larva voa sem a skill -- foto `larva-2-voando.png` (e pousa);
///   3. CONTRA-EXEMPLO (`LocalPlayer.EnterradoSemSinalDeTeste`): enterrada, a tela do dono a mostra como na superficie -- foto
///      `larva-3a-sem-sinal.png` (e ela sai da terra);
///   4. `enterrar`: some dos outros (o `Oculto`), intocavel e plantada; na tela do dono, meio transparente sobre o montinho de terra -- foto
///      `larva-3-enterrada.png`;
///   5. "cinco minutos depois" (`EnvelhecerAEnterradaDeTeste`): a carapaca rompe e ela volta PERFEITA -- foto `larva-4-amadureceu.png`.
///
///     Godot --path . --host --rede 8459 --campoteste 12 --horateste 0.5 --semfauna --diaglarva --raca Human --conta bancada_dlarva --nome Casulo
/// </summary>
public partial class RoboDaLarva : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private int _passo;
	private double _t, _espera;

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

	/// <summary>O alfa do boneco do dono (a raiz do visual) e se o montinho de terra esta debaixo dele.</summary>
	private static (float Alfa, bool Monte) SinalDeEnterrado(World mundo, int eu)
	{
		Node2D? corpo = mundo.CorpoDeTeste(eu);
		float alfa = corpo?.GetNodeOrNull<CharacterVisual>("Visual")?.Modulate.A ?? 1f;
		return (alfa, corpo?.GetNodeOrNull<MonteDeTerra>(MonteDeTerra.NomeDoNode) != null);
	}

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
		Jandirus.Server.GameServer.RetratoDaLarva? r = srv.LarvaDeTeste(eu);
		switch (_passo)
		{
			case 0:
				if (_t < 1.5) return;   // o corpo assenta no campo
				GD.Print("[dlarva] ===== A LARVA DO BIO-ANDROIDE, NA TELA (dono, 2026-10-17) =====");
				srv.EmbateDeFoto_CeuLimpo(eu);
				srv.VirarBioDeLaboratorioDeTeste(eu, BioAndroids.Perfeito, 2);
				Passar();
				break;

			case 1:
				if (_t < 1.2) return;   // a aparencia nova chega
				Conferir(r is { Larva: false, Estagio: BioAndroids.Perfeito, Sentido: true } && r.Value.Corpo == BioAndroids.IndiceDoCorpo(BioAndroids.Perfeito),
							  "(premissa) o corpo do robo e um bio-androide de laboratorio PERFEITO, com o corpo do degrau, e e sentido");
				Fotografar("user://larva-0-perfeito.png");
				cli.SendHabilidade("larva");
				Passar();
				break;

			case 2:   // A LARVA
				if (_t < 1.2) return;
				Conferir(r is { Larva: true, Estagio: BioAndroids.Perfeito, Expresso: 5, Sentido: false, PodeVoar: true }
							  && r.Value.Corpo == BioAndroids.IndiceDoCorpo(BioAndroids.Larva),
							  $"`larva` (pelo fio): o corpo VIRA A LARVA por cima do degrau perfeito -- SELADA: {r?.Expresso:0} de poder, fora do sense e dos "
							  + "rastreadores, e voa sem a skill");
				Fotografar("user://larva-1-larva.png");
				cli.SendHabilidade("voar");
				Passar();
				break;

			case 3:   // VOANDO
				if (_t < 1.5) return;
				Conferir(r is { Voando: true, Larva: true }, "`voar` (pelo fio): a LARVA VOA");
				Fotografar("user://larva-2-voando.png");
				cli.SendHabilidade("voar");   // pousa
				Passar();
				break;

			case 4:   // o contra-exemplo: a tela do dono cega pro enterrado
				if (_t < 1.5) return;
				Conferir(r is { Voando: false }, "(palco) a larva pousou");
				LocalPlayer.EnterradoSemSinalDeTeste = true;
				cli.SendHabilidade("enterrar");
				Passar();
				break;

			case 5:
				if (_t < 0.8) return;
				{
					(float alfa, bool monte) = SinalDeEnterrado(mundo, eu);
					Conferir(r is { Enterrado: true } && !monte && alfa > 0.99f,
								  "CONTRA-EXEMPLO (defeito injetado: a tela do dono cega pro enterrado): o servidor a enterrou, e o dono se ve como na superficie -- "
								  + "opaca e sem o montinho");
					Fotografar("user://larva-3a-sem-sinal.png");
				}
				cli.SendHabilidade("enterrar");   // sai da terra
				Passar();
				break;

			case 6:
				if (_t < 0.8) return;
				Conferir(r is { Enterrado: false, Oculto: false, Intocavel: false, Anda: true },
							  "`enterrar` de novo (pelo fio): ela SAI da terra -- vista, alcancavel e andando");
				LocalPlayer.EnterradoSemSinalDeTeste = false;
				cli.SendHabilidade("enterrar");
				Passar();
				break;

			case 7:   // ENTERRADA
				if (_t < 0.8) return;
				{
					(float alfa, bool monte) = SinalDeEnterrado(mundo, eu);
					Conferir(r is { Enterrado: true, Oculto: true, Intocavel: true, Anda: false } && monte && alfa < 0.5f,
								  $"`enterrar` (pelo fio): a larva SOME dos outros (o `Oculto`), ninguem a alcanca e ela nao anda -- e o dono se ve meio "
								  + $"transparente (alfa {alfa:0.00}) sobre o montinho de terra");
					Fotografar("user://larva-3-enterrada.png");
				}
				srv.EnvelhecerAEnterradaDeTeste(eu, BioAndroids.SegundosEnterradoPraAmadurecer);
				Passar();
				break;

			case 8:   // espera o tique de producao amadurecer
				if (r is { Larva: true } && _t < 4) return;
				Passar();
				break;

			case 9:   // AMADURECEU
				if (_t < 1.2) return;   // o clarao da carapaca (0,6 s) e a aparencia nova
				{
					(float alfa, bool monte) = SinalDeEnterrado(mundo, eu);
					Conferir(r is { Larva: false, Enterrado: false, Estagio: BioAndroids.Perfeito, Sentido: true } && r.Value.Expresso > 5
								  && r.Value.Corpo == BioAndroids.IndiceDoCorpo(BioAndroids.Perfeito) && !monte && alfa > 0.99f,
								  "CINCO MINUTOS ENTERRADA (o tique de producao): a carapaca ROMPE e ela sai da terra PERFEITA de novo -- o poder inteiro, "
								  + "sentida, opaca e sem o montinho");
					Fotografar("user://larva-4-amadureceu.png");
				}
				Fechar();
				break;
		}
	}

	private void Fechar()
	{
		_acabou = true;
		LocalPlayer.EnterradoSemSinalDeTeste = false;
		foreach (string l in _linhas) GD.Print("[dlarva] " + l);
		GD.Print(_falhas.Count == 0 ? "[dlarva] ===== TUDO OK =====" : $"[dlarva] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dlarva]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
