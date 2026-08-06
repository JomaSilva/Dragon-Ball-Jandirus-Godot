using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DE APAGAR PERSONAGEM (`--diagslot`).
///
/// ============================ POR QUE ISTO PRECISA DE BANCADA ============================
/// Apagar personagem e a unica coisa do jogo que NAO VOLTA. Se a conferencia do nome deixar passar
/// qualquer texto, ninguem descobre pelo caminho normal -- descobre no dia em que alguem digitar
/// errado e perder meses de treino, e ai nao ha o que fazer.
///
/// "O botao funcionou" nao e a pergunta interessante. As perguntas sao:
///   * o nome ERRADO e mesmo RECUSADO -- ou a trava e so enfeite de tela?
///   * o certo apaga de verdade, ou so some da lista ate o proximo login?
///   * apagar um slot mexe SO nele?
/// ========================================================================================
///
/// ============================ CRIAR PERSONAGEM JA ENTRA NO MUNDO ============================
/// Descoberto rodando: `CreateChar`, no servidor, cria E entra -- o peer sai de `_logados` na mesma
/// chamada. A primeira versao desta bancada criava dois personagens em sequencia e todo o resto
/// falhava com "faca login primeiro", porque depois do primeiro ela ja nao estava na tela de
/// selecao.
///
/// Entao o teste e feito de CICLOS: criar, cair fora, voltar. Sai mais lento e e o unico jeito
/// honesto -- e de quebra a volta e o que prova que o apagar foi pro DISCO, e nao so pra memoria.
/// ==========================================================================================
///
///     Godot --path . --diagslot
/// </summary>
public partial class RoboDeSlot : Node
{
	private const string Conta = "diagslot";
	private const string Senha = "diagslot";
	private const string NomeA = "Apagavel";
	private const string NomeB = "Sobrevivente";

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];
	private readonly List<string> _recusas = [];

	private bool _acabou;
	private int _passo;
	private double _t;
	private List<SlotInfo> _slots = [];
	private bool _naSelecao;
	private bool _pedindoConexao;

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Ready()
	{
		// O SERVIDOR SOBE AQUI. A bancada e registrada antes da tela de login (e onde ela vive), e
		// e a tela de login que normalmente chama `Hospedar()` -- sem esta linha o robo tentava
		// conectar num servidor que ninguem levantou ("desconectado: ConnectionFailed").
		if (Jandirus.Server.GameServer.Instance is { } srv && !srv.Running && !srv.Start())
		{
			GD.PushError("[slot] nao consegui abrir a porta -- ha outro servidor rodando?");
			return;
		}

		if (GameClient.Instance is not { } cli) return;
		cli.SlotsRecebidos += AoReceberSlots;
		cli.Rejected += AoRecusar;
		Reconectar();
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is not { } cli) return;
		cli.SlotsRecebidos -= AoReceberSlots;
		cli.Rejected -= AoRecusar;
	}

	private void AoRecusar(string motivo) => _recusas.Add(motivo);

	private void AoReceberSlots(List<SlotInfo> slots)
	{
		_slots = slots;
		_naSelecao = true;
		_pedindoConexao = false;
	}

	private void Reconectar()
	{
		_naSelecao = false;
		_pedindoConexao = true;
		GameClient.Instance?.Conectar("127.0.0.1", Protocol.DefaultPort, Conta, Senha);
	}

	private int Ocupados() => _slots.Count(s => s.Ocupado);
	private bool Tem(string nome) => _slots.Any(s => s.Ocupado && s.Nome == nome);

	public override void _Process(double delta)
	{
		if (_acabou) return;

		// FORA DA SELECAO NAO SE FAZ NADA -- e a espera E parte do teste. Depois de criar, o cliente
		// esta no MUNDO; depois de desconectar, em lugar nenhum. Os dois estados terminam num pedido
		// de conexao, e o passo so anda quando a lista de slots chega de volta.
		if (!_naSelecao)
		{
			if (_pedindoConexao) return;
			_t += delta;
			if (_t < 1.0) return;
			_t = 0;
			Reconectar();
			return;
		}

		_t += delta;
		if (_t < 0.7) return;
		_t = 0;

		GameClient? cli = GameClient.Instance;
		if (cli == null) return;

		switch (_passo++)
		{
			case 0:
				// TERRENO LIMPO: a conta e reusada entre rodadas. Sem isto a segunda execucao
				// comecaria com os slots cheios e o `CriarEm` seria recusado -- o teste falharia
				// por sujeira, e nao por defeito.
				for (int i = 0; i < _slots.Count; i++)
					if (_slots[i].Ocupado) cli.SendDeleteChar(i, _slots[i].Nome);
				break;

			case 1:
				Conferir(Ocupados() == 0, $"a conta comeca vazia ({Ocupados()} slot(s) ocupado(s))");
				CriarEm(0, NomeA);
				break;

			// ============================ NAO SE DESCONECTA NO MESMO QUADRO ============================
			// A primeira versao chamava `CriarEm` e `Sair()` na mesma linha, e o personagem nao existia
			// do outro lado: o `Desconectar` derruba a conexao antes de o pacote de criacao ser
			// entregue e o mundo carregado. O sintoma enganava -- o log do servidor mostrava um
			// personagem criado e apagado, e a bancada dizia "0 slots".
			//
			// Um passo de espera (0,7 s) entre criar e sair poe o teste no caminho de producao: o
			// jogador tambem nao cria e fecha o jogo no mesmo instante.
			// ==========================================================================================
			case 2:
				Sair();   // criar ENTRA no mundo: agora sim, sai e volta pra selecao
				break;

			case 3:
				Conferir(Tem(NomeA), $"'{NomeA}' foi criado no slot 1");
				CriarEm(1, NomeB);
				break;

			case 4:
				Sair();
				break;

			case 5:
				Conferir(Tem(NomeA) && Tem(NomeB), $"os dois personagens existem ({Ocupados()} slots)");

				// ---------- O NOME ERRADO TEM QUE SER RECUSADO ----------
				// Esta e a conferencia que justifica a bancada. Se ela passar, a trava e enfeite.
				_recusas.Clear();
				cli.SendDeleteChar(0, "nome que nao e o dele");
				break;

			case 6:
				Conferir(Tem(NomeA), $"nome ERRADO nao apagou nada -- '{NomeA}' continua la");
				Conferir(_recusas.Count > 0 && _recusas[0].Contains("nao confere"),
					_recusas.Count > 0 ? $"e o servidor disse por que: \"{_recusas[0]}\"" : "e o servidor EXPLICOU por que");
				_recusas.Clear();
				cli.SendDeleteChar(0, "");
				break;

			case 7:
				Conferir(Tem(NomeA), "nome VAZIO tambem nao apaga");
				cli.SendDeleteChar(0, NomeA);   // agora o certo
				break;

			case 8:
				Conferir(!Tem(NomeA), $"o nome CERTO apagou: '{NomeA}' saiu da lista");
				// ============================ SO O SLOT PEDIDO ============================
				// Um laco errado no servidor limparia o array inteiro, e com uma conta de um
				// personagem so ninguem notaria. E por isso que a bancada cria DOIS.
				// ==========================================================================
				Conferir(Tem(NomeB), $"e SO ele: '{NomeB}' continua intacto no outro slot");
				Conferir(Ocupados() == 1, $"sobrou exatamente 1 slot ocupado ({Ocupados()})");

				_recusas.Clear();
				cli.SendDeleteChar(0, NomeA);   // de novo, no slot ja vazio
				break;

			case 9:
				Conferir(_recusas.Count > 0, "apagar slot ja vazio e recusado, e nao ignorado em silencio");
				// FOI PRO DISCO? Sumir da lista nao e apagar -- o servidor podia ter mexido so na
				// memoria, e o personagem voltaria no proximo login.
				Sair();
				break;

			case 10:
				Conferir(!Tem(NomeA), $"depois de RELOGAR, '{NomeA}' continua apagado (foi pro disco)");
				Conferir(Tem(NomeB), $"e '{NomeB}' voltou inteiro");
				break;

			default:
				Terminar();
				break;
		}
	}

	/// <summary>Cai fora e volta pra selecao. O `_Process` reconecta sozinho.</summary>
	private void Sair()
	{
		_naSelecao = false;
		_pedindoConexao = false;
		_t = 0;
		GameClient.Instance?.Desconectar();
	}

	private static void CriarEm(int slot, string nome)
	{
		var ficha = new Jandirus.Core.Races.CharacterDraft
		{
			Name = nome, Race = "Human", Planet = "Earth", Gender = "Male",
			Backstory = "personagem de bancada, criado pra ser apagado.",
			Porte = "Medium",
		};
		GameClient.Instance?.CriarPersonagem(slot, ficha, new Jandirus.Core.Appearance.Appearance());
	}

	private void Terminar()
	{
		if (_acabou) return;
		_acabou = true;
		GD.Print("\n[slot] ===== BANCADA DE APAGAR PERSONAGEM =====");
		foreach (string l in _passos) GD.Print("[slot] " + l);
		GD.Print(_falhas.Count == 0
			? "[slot] ===== TUDO OK ====="
			: $"[slot] ===== {_falhas.Count} FALHA(S) =====\n[slot]   " + string.Join("\n[slot]   ", _falhas));
	}
}
