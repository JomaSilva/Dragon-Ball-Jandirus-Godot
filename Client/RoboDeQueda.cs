using Godot;
using Jandirus.Core;
using LiteNetLib;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA QUEDA DE CONEXAO (`--diagqueda`).
///
/// O dono (2026-10-10): *"faça tb q caso tenha desconexao com o servidor o jogo te joga de volta pro menu
/// principal com a mensagem de desconectado do server"*.
///
/// ============================ O QUE ELA MEDE, E POR ONDE ============================
/// A QUEDA E DE VERDADE: o servidor (que mora neste processo, porque ela roda hospedando) derruba a conexao
/// do proprio robo pelo fio, do jeito que uma expulsao faz -- o cliente recebe a mesma despedida de um kick,
/// de um ban ou de um servidor que fechou. Nada aqui chama o tratador do `Boot` por dentro.
///
///   1. O MOTIVO sai na lingua do jogador, e nao no nome do enum do LiteNetLib.
///   2. (contraprova) Com o defeito injetado -- o jogo de antes do pedido -- a queda chega, fica PENDENTE,
///      e o mundo continua congelado na tela, sem aviso. A regua de baixo reprova.
///   3. Desligado o defeito, a MESMA queda pendente e tratada no quadro seguinte: o mundo cai, o menu
///      principal volta, e o aviso "DESCONECTADO DO SERVIDOR" esta na tela com o motivo. E a prova de que a
///      regra e por ESTADO -- nao houve um segundo evento pra ouvir.
///   4. O OK fecha o aviso, e a frase continua na linha de estado do formulario.
///   5. Da pra entrar de novo: o lobby remontado funciona.
///   6. SAIR NAO E CAIR: desconectar por vontade propria nao acende queda nem aviso.
///   7. A VERSAO QUE NAO BATE (dono, 2026-10-11) USA ESTE MESMO PAINEL: com o servidor uma versao na frente, o
///      robo disca de novo de DENTRO do mundo que ficou pra tras -- o mundo cai, o menu volta, e o no do aviso
///      e o mesmo, com o titulo e a frase de versao. O resto do aperto de mao e da `--diagversao`.
///
/// ELA SE MUDA PRA RAIZ DA ARVORE: o `Boot.VoltarAoLogin` derruba todos os filhos dele menos as telas de
/// maquina, e um robo que morresse na volta ao menu nao mediria a volta ao menu (a mesma licao do
/// `RoboDeCarga`).
///
///     Godot --headless --path . --host --rede 7981 --diagqueda --conta bancada_queda --nome Caido
/// </summary>
public partial class RoboDeQueda : Node
{
	/// <summary>Ja ha um robo vivo? A reentrada no mundo passa de novo pelo `MontarMundo`, que o criaria outra vez.</summary>
	public static bool Vivo { get; private set; }

	private Boot? _boot;
	private double _t;
	private int _passo, _esperas, _quedasOuvidas;
	private bool _acabou;

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private static GameClient? C => GameClient.Instance;

	public override void _EnterTree()
	{
		Vivo = true;
		if (GetParent() is Boot boot) _boot = boot;
	}

	public override void _Ready()
	{
		if (C is { } cli) cli.Caiu += AoCair;
		CallDeferred(nameof(Emancipar));
	}

	public override void _ExitTree()
	{
		// SO NA SAIDA DE VERDADE: a mudanca pra raiz tambem passa por aqui (sai de um pai, entra no outro).
		if (IsQueuedForDeletion())
		{
			Vivo = false;
			if (C is { } cli) cli.Caiu -= AoCair;
		}
	}

	private void AoCair(string _) => _quedasOuvidas++;

	private void Emancipar()
	{
		if (GetParent() is not { } pai || GetTree() is not { } arv || pai == arv.Root) return;
		Node raiz = arv.Root;   // antes do `RemoveChild`: fora da arvore o `GetTree()` devolve nulo
		pai.RemoveChild(this);
		raiz.AddChild(this);
	}

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque + (ok || detalhe.Length == 0 ? "" : $"   [{detalhe}]"));
		if (!ok) _falhas.Add(oque);
	}

	private bool NoMundo(GameClient cli) => cli.Connected && cli.LocalId != 0 && _boot is { MundoMontado: true };

	public override void _Process(double delta)
	{
		if (_acabou || _boot == null || C is not { } cli) return;

		_t += delta;
		if (_t < 0.5) return;   // meio segundo por passo: da tempo de o fio e de um quadro acontecerem
		_t = 0;

		Jandirus.Server.GameServer? srv = Jandirus.Server.GameServer.Instance;

		switch (_passo++)
		{
			// -------------------------------------------------------------- dentro do mundo
			case 0:
				if (!NoMundo(cli)) { _passo = 0; return; }
				Conferir(srv is { Running: true }, "(montagem) a bancada esta HOSPEDANDO: o servidor que derruba mora neste processo");
				Conferir(cli.QuedaPendente == null && _boot.AvisoDeQuedaNaTela() == null,
					"PREMISSA: dentro do mundo, sem queda pendente e sem aviso na tela");
				Conferir(GameClient.MotivoDaQueda(DisconnectReason.Timeout) == "o servidor parou de responder"
					  && GameClient.MotivoDaQueda(DisconnectReason.RemoteConnectionClose) == "o servidor encerrou a conexão"
					  && GameClient.MotivoDaQueda(DisconnectReason.ConnectionFailed) == "não foi possível alcançar o servidor",
					"REGRA PURA: o motivo da queda sai na lingua do jogador (e nao no nome do enum do LiteNetLib)");
				break;

			// -------------------------------------------------------------- a queda, com o defeito injetado
			case 1:
				Boot.QuedaNoMundoCongelaDeTeste = true;
				_esperas = 0;
				Conferir(srv != null && srv.DerrubarDeBancada(cli.LocalId),
					"(montagem) o servidor derrubou a MINHA conexao pelo fio (a despedida de um kick)");
				break;

			case 2:
				if (cli.QuedaPendente == null && ++_esperas < 12) { _passo = 2; return; }
				Conferir(!cli.Connected && cli.QuedaPendente == "o servidor encerrou a conexão" && _quedasOuvidas == 1,
					"a queda CHEGA ao cliente: sem conexao, com o motivo guardado, e o evento disparou uma vez",
					$"conectado={cli.Connected} pendente='{cli.QuedaPendente}' eventos={_quedasOuvidas}");
				Conferir(_boot.MundoMontado && _boot.AvisoDeQuedaNaTela() == null && !_boot.MenuPrincipalNaTela,
					"(defeito injetado: a queda dentro do mundo e ignorada) o mundo fica congelado na tela, sem menu e sem aviso -- a regua sabe reprovar",
					$"mundo={_boot.MundoMontado} aviso='{_boot.AvisoDeQuedaNaTela()}' menu={_boot.MenuPrincipalNaTela}");
				Boot.QuedaNoMundoCongelaDeTeste = false;
				break;

			// -------------------------------------------------------------- a MESMA queda, agora tratada
			case 3:
			{
				string aviso = _boot.AvisoDeQuedaNaTela() ?? "";
				Conferir(!_boot.MundoMontado, "**A QUEDA DERRUBA O MUNDO**: nao ha mais mundo congelado na tela");
				Conferir(_boot.MenuPrincipalNaTela, "...o MENU PRINCIPAL esta de volta");
				Conferir(aviso.Contains("DESCONECTADO DO SERVIDOR", StringComparison.Ordinal)
					  && aviso.Contains("servidor encerrou a conexão", StringComparison.OrdinalIgnoreCase),
					"...com o aviso \"DESCONECTADO DO SERVIDOR\" e o motivo, na tela", aviso);
				Conferir(cli.QuedaPendente == null && _quedasOuvidas == 1,
					"...e a queda foi TRATADA: nada pendente, e nenhum segundo evento foi preciso (a regra e por estado)",
					$"pendente='{cli.QuedaPendente}' eventos={_quedasOuvidas}");
				break;
			}

			case 4:
				Conferir(_boot.ApertarOkDoAvisoDeQueda(), "o aviso tem um OK, e ele aceita o clique");
				break;

			case 5:
				Conferir(_boot.AvisoDeQuedaNaTela() == null && _boot.MenuPrincipalNaTela,
					"o OK fecha o aviso e deixa o menu principal livre");
				Conferir(_boot.EstadoDoMenuPrincipal.Contains("desconectado do servidor", StringComparison.OrdinalIgnoreCase),
					"...e a frase continua na linha de estado do formulario", _boot.EstadoDoMenuPrincipal);

				// DE VOLTA PRA DENTRO, pelo mesmo caminho da primeira entrada.
				_esperas = 0;
				_boot.DiscarDeNovoDeTeste();
				break;

			// -------------------------------------------------------------- entrar de novo
			case 6:
				if (!NoMundo(cli) && ++_esperas < 80) { _passo = 6; return; }
				Conferir(NoMundo(cli) && _boot.AvisoDeQuedaNaTela() == null,
					"da pra ENTRAR DE NOVO depois da queda (o lobby remontado funciona, e o aviso nao vem junto)",
					$"conectado={cli.Connected} mundo={_boot.MundoMontado}");
				break;

			// -------------------------------------------------------------- sair nao e cair
			case 7:
				cli.Desconectar();
				break;

			case 8:
				break;   // um passo de folga: a despedida local e lida no proximo `PollEvents`

			case 9:
				Conferir(!cli.Connected && cli.QuedaPendente == null && _quedasOuvidas == 1 && _boot.AvisoDeQuedaNaTela() == null,
					"**SAIR NAO E CAIR**: desconectar por vontade propria nao acende queda, nao dispara o evento e nao mostra aviso",
					$"conectado={cli.Connected} pendente='{cli.QuedaPendente}' eventos={_quedasOuvidas}");
				break;

			// -------------------------------------------------------------- a versao que nao bate: o MESMO painel
			case 10:
				// O SERVIDOR UMA VERSAO NA FRENTE (o palco de bancada dele), e o robo disca de novo pelo caminho de
				// sempre. O mundo da ultima entrada ainda esta montado: sair por vontade nao o derruba.
				Conferir(srv != null && _boot.MundoMontado, "(montagem) o mundo de antes continua montado, e ha servidor pra por uma versao na frente");
				if (srv != null) srv.VersaoDeTeste = new IdentidadeDeBuild(cli.MinhaVersao.Numero + 1, cli.MinhaVersao.Digital);
				_esperas = 0;
				_boot.DiscarDeNovoDeTeste();
				break;

			case 11:
			{
				if (_boot.AvisoDeQuedaNaTela() == null && ++_esperas < 20) { _passo = 11; return; }
				string aviso = _boot.AvisoDeQuedaNaTela() ?? "";
				string daqui = cli.MinhaVersao.Texto, deLa = Versao.TextoDe(cli.MinhaVersao.Numero + 1);
				Conferir(aviso.Contains("VERSÃO DIFERENTE DA DO SERVIDOR", StringComparison.Ordinal)
					  && aviso.Contains($"Este servidor está na versão {deLa} e o seu jogo na {daqui}", StringComparison.Ordinal)
					  && aviso.Contains("o seu jogo está ATRASADO — atualize o jogo.", StringComparison.Ordinal)
					  && !aviso.Contains("DESCONECTADO DO SERVIDOR", StringComparison.Ordinal),
					"**A VERSAO QUE NAO BATE USA O MESMO PAINEL DA QUEDA**: o mesmo no de aviso, com o titulo e a frase de versao (as duas versoes e a direcao)", aviso);
				Conferir(!_boot.MundoMontado && _boot.MenuPrincipalNaTela && !cli.Connected && cli.QuedaPendente == null && _quedasOuvidas == 2,
					"...pelo MESMO caminho: a queda acendeu (o segundo evento da sessao), o mundo que ficou pra tras caiu, o menu principal voltou, e nada ficou pendente",
					$"mundo={_boot.MundoMontado} menu={_boot.MenuPrincipalNaTela} pendente='{cli.QuedaPendente}' eventos={_quedasOuvidas}");
				break;
			}

			case 12:
				Conferir(_boot.ApertarOkDoAvisoDeQueda(), "o MESMO OK fecha o aviso de versao");
				break;

			case 13:
				Conferir(_boot.AvisoDeQuedaNaTela() == null && _boot.MenuPrincipalNaTela
					  && _boot.EstadoDoMenuPrincipal.Contains("ATRASADO", StringComparison.Ordinal),
					"...e o resumo (com a direcao) continua na linha de estado do formulario", _boot.EstadoDoMenuPrincipal);
				if (srv != null) srv.VersaoDeTeste = null;
				break;

			// -------------------------------------------------------------- relatorio
			default:
				_acabou = true;
				Boot.QuedaNoMundoCongelaDeTeste = false;
				if (srv != null) srv.VersaoDeTeste = null;
				GD.Print("\n[queda] ===== BANCADA DA QUEDA DE CONEXAO =====");
				foreach (string l in _passos) GD.Print("[queda] " + l);
				GD.Print(_falhas.Count == 0
					? "[queda] ===== TUDO OK ====="
					: $"[queda] ===== {_falhas.Count} FALHA(S) =====\n[queda]   " + string.Join("\n[queda]   ", _falhas));
				break;
		}
	}
}
