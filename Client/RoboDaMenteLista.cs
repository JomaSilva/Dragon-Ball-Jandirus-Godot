using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A LISTA DA MENTE NA TELA (`--diagmentelista`, 2026-10-27, o pedido 124c) ============================
/// A `--mentelistateste` prova as regras pelo servidor e nao atravessa a porta da mente (ela exige um cliente). Este robo poe o host na
/// propria mente pela porta de producao (`EntrarNaMente`, sem a onda) e confere pela tela de verdade (`TelaDaMente`):
///   1. a LISTA abre, sem reflexo automatico -- e com a foto de cada um (voce, um habitante e o Freeza de Namek semeados na memoria);
///   2. o "Enfrentar" do reflexo fecha a lista e ele nasce;
///   3. derrubado o reflexo, a VITORIA devolve a lista;
///   4. o Freeza, com duas formas vistas, PERGUNTA A FORMA na propria lista (antes so a aba Learning do menu P a desenhava);
///   5. a forma escolhida nasce, e a lista fecha;
///   6. o DONO derrubado com o Freeza de pe e DERROTA pelo caminho de jogo: a borda do corpo largado o tira da mente, e o foco abalado o
///      barra de mergulhar (a marca do `SairDaMente`, que a bancada de servidor so alcanca pela porta da fila).
///
/// COMO RODAR -- com JANELA (no headless o `GetImage` volta vazio):
///
///     Godot --path . --host --rede 11882 --semfauna --diagmentelista --resolution 1280x720 --raca Human --conta bancada_dmentelista --nome Sonhador
/// </summary>
public partial class RoboDaMenteLista : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	private const double Paciencia = 120;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private double _t, _vida;
	private int _passo;

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		_linhas.Add((ok ? "  ok     " : "  FALHA  ") + oque + (detalhe.Length == 0 ? "" : $"   [{detalhe}]"));
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --     " + oque);

	public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		_vida += delta;
		_t += delta;
		if (_vida > Paciencia) { Conferir(false, $"a bancada coube na paciencia ({Paciencia:0} s; parou no passo {_passo})"); Fechar(); return; }
		if (S is not { } srv) { if (_vida > 20) { Nota("sem servidor no processo (`--diagmentelista` precisa de `--host`)"); Fechar(); } return; }
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo || !IsInstanceValid(mundo)) return;
		if (TelaDaMente.Instancia is not { } tela) return;

		switch (_passo)
		{
			case 0:
				if (_t < 4) return;   // o mundo monta e o chat assenta
				srv.SemearMemoriaDaMenteDeTeste(cli.LocalId);
				srv.EntrarNaMenteDeTeste(cli.LocalId);
				Ir(1);
				break;

			case 1:
				if (!tela.NaTela && _t < 6) return;
				if (_t < 2.5) return;   // o mundo da mente monta por tras
				Fotografar("mentelista-1-lista.png", "1. a lista da mente, com a foto de cada um");
				var linhas = tela.LinhasDeTeste();
				(int lembranca, bool naMente, _) = srv.MenteDeTeste(cli.LocalId);
				Conferir(tela.NaTela && naMente && lembranca == 0, "entrar na mente ABRE A LISTA, sem reflexo automatico",
						 $"na tela {tela.NaTela}, na mente {naMente}, lembranca {lembranca}");
				Conferir(linhas.Count >= 3 && linhas.All(l => l.ComFoto), "...com a FOTO de cada um (voce, o habitante e o chefe)",
						 string.Join(" | ", linhas.Select(l => $"{l.Nome}{(l.ComFoto ? "+foto" : "")}")));
				Conferir(tela.EnfrentarDeTeste("O seu reflexo"), "PRECONDICAO: o reflexo esta na lista");
				Ir(2);
				break;

			case 2:
				if (_t < 1.5) return;
				Fotografar("mentelista-2-reflexo.png", "2. o reflexo escolhido");
				(int reflexo, _, _) = srv.MenteDeTeste(cli.LocalId);
				Conferir(!tela.NaTela && reflexo != 0, "o \"Enfrentar\" do reflexo FECHA a lista e ele nasce", $"na tela {tela.NaTela}, lembranca {reflexo}");
				srv.DerrubarALembrancaDeTeste(cli.LocalId);
				Ir(3);
				break;

			case 3:
				if (!tela.NaTela && _t < 5) return;
				if (_t < 1.0) return;
				Fotografar("mentelista-3-vitoria.png", "3. a vitoria devolve a lista");
				(int depois, bool aindaNaMente, _) = srv.MenteDeTeste(cli.LocalId);
				Conferir(tela.NaTela && depois == 0 && aindaNaMente, "a VITORIA devolve a lista (o reflexo nao se reergue)",
						 $"na tela {tela.NaTela}, lembranca {depois}, na mente {aindaNaMente}");
				Conferir(tela.EnfrentarDeTeste("Freeza (Namek)"), "PRECONDICAO: o Freeza de Namek esta na lista");
				Ir(4);
				break;

			case 4:
				if (tela.EtapasDeTeste().Count == 0 && _t < 5) return;
				if (_t < 1.0) return;
				Fotografar("mentelista-4-forma.png", "4. a forma do Freeza, perguntada na propria lista");
				List<string> formas = tela.EtapasDeTeste();
				Conferir(tela.NaTela && formas.Count == 2 && formas.Contains("Forma 1") && formas.Contains("Forma Final"),
						 "o Freeza com duas formas vistas PERGUNTA A FORMA na propria lista (so as vistas)", string.Join(" | ", formas));
				Conferir(tela.EscolherEtapaDeTeste("Forma Final"), "PRECONDICAO: a Forma Final esta na pergunta");
				Ir(5);
				break;

			case 5:
				// O CHEFE TEM PODER DE SOBRA pra derrubar este corpo novo: a medida e no primeiro quadro em que ele existe, e nao 1 s depois.
				if (!srv.PalcoMcf_Estado(cli.LocalId).StartsWith("freeza_namek", StringComparison.Ordinal) && _t < 5) return;
				if (_t < 0.3) return;
				Fotografar("mentelista-5-freeza.png", "5. a Forma Final escolhida, diante de voce");
				string estado = srv.PalcoMcf_Estado(cli.LocalId);
				Conferir(!tela.NaTela && estado.StartsWith("freeza_namek|3|", StringComparison.Ordinal), "a forma escolhida NASCE, e a lista fecha",
						 $"na tela {tela.NaTela}, {estado}");
				srv.DerrubarODonoNaMenteDeTeste(cli.LocalId);
				Ir(6);
				break;

			case 6:
				if (srv.MenteDeTeste(cli.LocalId).NaMente && _t < 6) return;
				if (_t < 1.5) return;   // a volta pro mundo assenta na tela
				Fotografar("mentelista-6-derrota.png", "6. a derrota: de volta ao corpo, com o foco abalado");
				(int lembranca6, bool naMente6, bool barrado) = srv.MenteDeTeste(cli.LocalId);
				Conferir(!naMente6 && lembranca6 == 0 && barrado && !tela.NaTela,
						 "cair com a lembranca de pe e DERROTA pelo caminho de jogo: a borda do corpo largado tira da mente, e o foco abalado barra o mergulho",
						 $"na mente {naMente6}, lembranca {lembranca6}, barrado {barrado}, lista {tela.NaTela}");
				Fechar();
				break;
		}
	}

	private void Ir(int proximo) { _passo = proximo; _t = 0; }

	private void Fotografar(string nome, string rotulo)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"{rotulo}: a janela nao devolveu imagem"); return; }
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + nome);
			img.SavePng(caminho);
			_linhas.Add($"  foto   {rotulo}: {caminho}");
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	private void Fechar()
	{
		if (_acabou) return;
		_acabou = true;
		GD.Print("\n[dmentelista] ===== A LISTA DA MENTE NA TELA (o pedido 124c) =====");
		foreach (string l in _linhas) GD.Print("[dmentelista] " + l);
		GD.Print(_falhas.Count == 0
			? "[dmentelista] ===== TUDO OK ====="
			: $"[dmentelista] ===== {_falhas.Count} FALHA(S) =====\n[dmentelista]   " + string.Join("\n[dmentelista]   ", _falhas));
		GetTree().Quit();
	}
}
