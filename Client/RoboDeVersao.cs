using System.Collections;
using System.Text.RegularExpressions;
using Godot;
using Jandirus.Core;
using Jandirus.Net;
using Jandirus.Server;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA VERSAO, DO LADO DO JOGADOR (`--diagversao`, e `--versaodupla` pros dois processos).
///
/// O dono (2026-10-11): *"o jogo poderia ter no menu mostrando qual versao ele ta, e ao entrar em um server o
/// server checa a versao do cliente / cliente checa a versao do server, e se n bater o jogo manda pro menu
/// avisando q o cliente ta em alguma versao na frente ou atras do server"*.
///
/// ============================ O QUE ELA MEDE, E POR ONDE ============================
/// TUDO PELA TELA DE VERDADE: o robo preenche o formulario do menu principal e aperta "Entrar" / "Hospedar
/// partida", le o painel de aviso e a linha de estado que o jogador leria, e aperta o OK. Quem responde do
/// outro lado e a porta de producao do servidor, pelo fio (UDP em 127.0.0.1).
///
/// `--diagversao` (um processo; o servidor sobe NESTE processo, pelo botao "Hospedar"):
///   1. o SELO na primeira tela: o texto, a quina de baixo, fora do formulario -- e ancorado (muda a tela, ele
///      continua la); o titulo da janela;
///   2. o selo na PAUSA/OPCOES;
///   3. quem HOSPEDA passa direto (versao igual entra, e o login so chega ao servidor depois da versao);
///   4. servidor NA FRENTE -> o jogo volta ao menu com "ATRASADO"; 5. servidor ATRAS -> "ADIANTADO";
///   6. mesma versao, outro protocolo -> "builds diferentes" (e o resumo na linha de estado quebra em linhas:
///      o formulario do menu nao alarga);
///   7. o servidor que aceita qualquer um: quem barra e o CLIENTE, sozinho;
///   8. o servidor ANTIGO (nunca diz a versao): o cliente percebe pelo prazo e avisa;
///   9. carga de recusa vazia, truncada e de lixo: aviso generico, sem excecao;
///  10. no fim, versao igual entra de novo.
/// Cada regra do cliente tem o seu defeito injetado (`GameClient.NaoConfereOServidorDeTeste`,
/// `GameClient.SemPrazoDaVersaoDeTeste`, `SeloDeVersao.PosicaoCravadaDeTeste`, `Boot.ResumoSemQuebraDeTeste`) e as do servidor entram como
/// palco (`VersaoDeTeste`, `AceitaQualquerVersaoDeTeste`, `PortaAntigaDeTeste`, `CargaDaRecusaDeTeste`).
///
/// `--versaodupla &lt;atrasado|adiantado|outrobuild|semversao|igual&gt;` (DOIS processos de verdade, pelo
/// `duas.py`): este processo e o JOGADOR, e nao hospeda nada -- ele disca, pelo formulario, num servidor que
/// e outro processo (com a versao forjada na linha de comando dele) e cobra o desfecho esperado.
///
/// ELE SE MUDA PRA RAIZ DA ARVORE, como o `RoboDeQueda`: a volta ao menu derruba os filhos do `Boot`.
///
/// O QUE ELE NAO PROVA: dois BINARIOS diferentes de verdade (aqui e sempre o mesmo, com a versao forjada).
/// O passo a passo pra testar com dois builds esta no relatorio.
///
///     Godot --path . --position 1920,0 --resolution 1280x720 --rede 8281 --diagversao --conta bancada_versao --pasta &lt;fotos&gt;
/// </summary>
public partial class RoboDeVersao : Node
{
	/// <summary>Ja ha um robo vivo? O `Boot.MostrarAVersao` passa por aqui a cada volta ao menu.</summary>
	public static bool Vivo { get; private set; }

	private Boot? _boot;
	private int _ok, _falhou, _naoMedidos, _quedasOuvidas, _pacotesNoServidor;
	private readonly List<string> _vermelhas = [], _fotos = [];
	private IEnumerator? _roteiro;
	private double _espera = 1.0;
	private bool _acabou, _espiando;
	private string _pasta = "", _papel = "", _conta = "bancada_versao";
	private Vector2I? _baseDaTela;

	private static GameClient? C => GameClient.Instance;
	private static GameServer? S => GameServer.Instance;

	/// <summary>A etiqueta das linhas no log: `dversao` sozinho, `versaodupla` no papel de jogador de dois processos.</summary>
	private string Etiqueta => _papel.Length > 0 ? "versaodupla" : "dversao";

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[{Etiqueta}]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; _vermelhas.Add(nome); GD.PrintErr($"[{Etiqueta}]   FALHA {nome}   [{detalhe}]"); }
	}

	private void NaoMedido(string nome, string porque)
	{
		_naoMedidos++;
		GD.Print($"[{Etiqueta}]   --    NAO MEDIDO: {nome}   [{porque}]");
	}

	private void Nota(string s) => GD.Print($"[{Etiqueta}] " + s);

	private static string Arg(string chave)
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, chave);
		return i >= 0 && i + 1 < a.Length ? a[i + 1] : "";
	}

	public override void _EnterTree()
	{
		Vivo = true;
		if (GetParent() is Boot boot) _boot = boot;
	}

	public override void _Ready()
	{
		_papel = Arg("--versaodupla");
		if (Arg("--conta") is { Length: > 0 } conta) _conta = conta;
		if (C is { } cli) cli.Caiu += AoCair;
		CallDeferred(nameof(Emancipar));
	}

	public override void _ExitTree()
	{
		// SO NA SAIDA DE VERDADE: a mudanca pra raiz tambem passa por aqui (sai de um pai, entra no outro).
		if (!IsQueuedForDeletion()) return;
		Vivo = false;
		if (C is { } cli) cli.Caiu -= AoCair;
		PararDeEspiar();
	}

	private void AoCair(string _) => _quedasOuvidas++;

	/// <summary>Cada pacote que o SERVIDOR deste processo recebe, de quem for. O que se cobra dele e a contagem.</summary>
	private void Espiar(byte[] _) => _pacotesNoServidor++;

	private void PararDeEspiar()
	{
		if (!_espiando) return;
		_espiando = false;
		GameServer.EspiaoDeEntrada -= Espiar;
	}

	private void Emancipar()
	{
		if (GetParent() is not { } pai || GetTree() is not { } arv || pai == arv.Root) return;
		Node raiz = arv.Root;   // antes do `RemoveChild`: fora da arvore o `GetTree()` devolve nulo
		pai.RemoveChild(this);
		raiz.AddChild(this);
	}

	public override void _Process(double delta)
	{
		if (_acabou || _boot == null || C == null) return;

		_espera -= delta;
		if (_espera > 0) return;

		try
		{
			_roteiro ??= (_papel.Length > 0 ? RoteiroDeDois() : RoteiroSozinho()).GetEnumerator();
			if (!_roteiro.MoveNext()) { Fim(); return; }
			_espera = _roteiro.Current is double d ? d : 0;
		}
		catch (Exception e)
		{
			Afirmar("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
			Fim();
		}
	}

	private void Fim()
	{
		_acabou = true;
		// TODO DEFEITO E TODO PALCO DESLIGADOS, aconteca o que acontecer no roteiro.
		GameClient.NaoConfereOServidorDeTeste = GameClient.SemPrazoDaVersaoDeTeste = false;
		SeloDeVersao.PosicaoCravadaDeTeste = false;
		Boot.ResumoSemQuebraDeTeste = false;
		if (C is { } cli) cli.VersaoDeTeste = null;
		if (S is { } srv)
		{
			srv.VersaoDeTeste = null;
			srv.AceitaQualquerVersaoDeTeste = srv.PortaAntigaDeTeste = false;
			srv.CargaDaRecusaDeTeste = null;
		}
		PararDeEspiar();
		if (_baseDaTela is { } b0 && GetTree()?.Root is { } raiz) raiz.ContentScaleSize = b0;
		if (PauseMenu.Instancia is { Aberto: true } p) p.Fechar("bancada");

		foreach (string f in _fotos) Nota("foto " + f);
		if (_naoMedidos > 0) Nota($"{_naoMedidos} checagem(ns) NAO MEDIDA(S) (ver as linhas \"--\")");
		if (_falhou > 0) GD.PrintErr($"[{Etiqueta}] vermelhas: " + string.Join(" | ", _vermelhas));
		GD.Print(_falhou == 0 ? $"\n[{Etiqueta}] ===== TUDO OK ({_ok} OK) =====" : $"\n[{Etiqueta}] ===== {_falhou} FALHA(S), {_ok} OK =====");
	}

	// =====================================================================
	// FERRAMENTAS
	// =====================================================================
	/// <summary>Espera a condicao, ate o teto. Quem chama confere depois se ela veio.</summary>
	private static IEnumerable Ate(Func<bool> cond, double tetoSeg, double passo = 0.1)
	{
		ulong t0 = Time.GetTicksMsec();
		while (!cond() && (Time.GetTicksMsec() - t0) / 1000.0 < tetoSeg) yield return passo;
	}

	private Image? Fotografar(string nome)
	{
		// SEM JANELA NAO HA FOTO, e nem se pergunta: no `--headless` o `GetImage` imprime um erro do motor por chamada.
		if (DisplayServer.GetName() == "headless") return null;
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"sem foto pra {nome}"); return null; }
		string caminho = _pasta + nome + ".png";
		img.SavePng(caminho);
		_fotos.Add(caminho);
		return img;
	}

	private void PrepararAPasta()
	{
		_pasta = Arg("--pasta");
		if (_pasta.Length == 0) _pasta = ProjectSettings.GlobalizePath("user://");
		if (!_pasta.EndsWith('/') && !_pasta.EndsWith('\\')) _pasta += "/";
		DirAccess.MakeDirRecursiveAbsolute(_pasta);
	}

	/// <summary>O painel de aviso que esta na tela agora (titulo | frase), ou vazio.</summary>
	private string Aviso => _boot?.AvisoDeQuedaNaTela() ?? "";

	/// <summary>A linha de estado do formulario do menu principal.</summary>
	private string Estado => _boot?.EstadoDoMenuPrincipal ?? "";

	/// <summary>
	/// O FORMULARIO DO MENU PRINCIPAL, achado na tela como o jogador o acha: os tres campos (o da senha e o
	/// secreto; dos outros dois, o servidor vem antes da conta) e os dois botoes, pelo rotulo.
	/// </summary>
	private (LineEdit Servidor, LineEdit Conta, LineEdit Senha, Button Entrar, Button Hospedar)? Formulario()
	{
		if (_boot?.CamadaDaPrimeiraTela() is not { } camada) return null;
		List<LineEdit> campos = [.. camada.FindChildren("*", nameof(LineEdit), true, false).OfType<LineEdit>()];
		List<Button> botoes = [.. camada.FindChildren("*", nameof(Button), true, false).OfType<Button>()];
		LineEdit? senha = campos.FirstOrDefault(c => c.Secret);
		List<LineEdit> abertos = [.. campos.Where(c => !c.Secret)];
		Button? entrar = botoes.FirstOrDefault(b => b.Text == "Entrar");
		Button? hospedar = botoes.FirstOrDefault(b => b.Text.StartsWith("Hospedar", StringComparison.Ordinal));
		if (senha == null || abertos.Count < 2 || entrar == null || hospedar == null) return null;
		return (abertos[0], abertos[1], senha, entrar, hospedar);
	}

	/// <summary>(v0.3.0) Um botao da primeira tela pelo rotulo -- a tela de hospedar mora na mesma camada do formulario.</summary>
	private Button? BotaoDaPrimeiraTela(string texto) =>
		_boot?.CamadaDaPrimeiraTela()?.FindChildren("*", nameof(Button), true, false).OfType<Button>()
			.FirstOrDefault(b => b.Text == texto && b.IsVisibleInTree());

	/// <summary>Preenche o formulario e aperta "Entrar" (ou "Hospedar partida"), como o dedo do jogador.</summary>
	private bool ApertarEntrar(bool hospedar = false)
	{
		if (Formulario() is not { } f) return false;
		f.Servidor.Text = "127.0.0.1";
		f.Conta.Text = _conta;
		f.Senha.Text = "teste";
		(hospedar ? f.Hospedar : f.Entrar).EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}

	/// <summary>Aperta o "Trocar de servidor" da tela de selecao -- a saida por vontade propria.</summary>
	private bool SairDaSelecao()
	{
		if (_boot?.SelecaoNaTela is not { } selecao) return false;
		if (selecao.FindChildren("*", nameof(Button), true, false).OfType<Button>()
				   .FirstOrDefault(b => b.Text == "Trocar de servidor") is not { } sair) return false;
		sair.EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}

	/// <summary>O selo esta na quina de baixo, a direita, da tela de AGORA -- e inteiro dentro dela?</summary>
	private (bool NaQuina, bool Dentro, Rect2 Selo, Rect2 Tela) Quina(Control selo)
	{
		Rect2 tela = GetViewport().GetVisibleRect();
		Rect2 r = selo.GetGlobalRect();
		float m = SeloDeVersao.MargemDaQuina;
		bool naQuina = Math.Abs(r.End.X - (tela.End.X - m)) <= 1.5f && Math.Abs(r.End.Y - (tela.End.Y - m)) <= 1.5f;
		bool dentro = tela.Grow(0.75f).Encloses(r) && r.Size.X > 8 && r.Size.Y > 6;
		return (naQuina, dentro, r, tela);
	}

	// =====================================================================
	// O ROTEIRO DE UM PROCESSO SO (`--diagversao`)
	// =====================================================================
	private IEnumerable RoteiroSozinho()
	{
		PrepararAPasta();
		Boot boot = _boot!;
		GameClient cli = C!;

		foreach (object o in Ate(() => boot.MenuPrincipalNaTela, 30)) yield return o;
		yield return 0.6;

		IdentidadeDeBuild minha = cli.MinhaVersao;
		var naFrente = new IdentidadeDeBuild(minha.Numero + 1, minha.Digital);
		var atras = new IdentidadeDeBuild(minha.Numero - 1, minha.Digital);
		var outroBuild = new IdentidadeDeBuild(minha.Numero, minha.Digital ^ FioDaVersao.MascaraDaForja);

		Nota("==================================================================================");
		Nota(" A VERSAO DO JOGO: o selo no menu e o aperto de mao com o servidor (dono, 2026-10-11)");
		Nota("==================================================================================");
		Nota($"  este jogo  : versao {minha.Texto} (numero {minha.Numero}), protocolo {minha.Digital:x16}");
		Nota($"  fotos em   : {_pasta}");
		Nota($"  tela       : {GetViewport().GetVisibleRect().Size}  |  {DisplayServer.GetName()}");

		foreach (object o in F1_OSeloNaPrimeiraTela(boot, minha)) yield return o;
		foreach (object o in F2_OSeloNaPausa(minha)) yield return o;
		foreach (object o in F3_QuemHospedaPassaDireto(boot, cli)) yield return o;

		Nota("--- 4: o servidor NA FRENTE -- o jogo esta ATRASADO ---");
		foreach (object o in Barrado(boot, cli, "SERVIDOR NA FRENTE", naFrente, "VERSÃO DIFERENTE DA DO SERVIDOR",
			[$"Este servidor está na versão {naFrente.Texto} e o seu jogo na {minha.Texto}", "o seu jogo está ATRASADO — atualize o jogo."],
			$"seu jogo ({minha.Texto}) está ATRASADO: o servidor está na {naFrente.Texto}", "versao-3-atrasado")) yield return o;

		Nota("--- 5: o servidor ATRAS -- o jogo esta ADIANTADO ---");
		foreach (object o in Barrado(boot, cli, "SERVIDOR ATRAS", atras, "VERSÃO DIFERENTE DA DO SERVIDOR",
			[$"Este servidor está na versão {atras.Texto} e o seu jogo na {minha.Texto}", "o seu jogo está ADIANTADO — o servidor ainda não foi atualizado."],
			$"seu jogo ({minha.Texto}) está ADIANTADO: o servidor está na {atras.Texto}", "versao-4-adiantado")) yield return o;

		Nota("--- 6: a MESMA versao com outro protocolo -- builds diferentes ---");
		foreach (object o in Barrado(boot, cli, "MESMA VERSAO, OUTRO PROTOCOLO", outroBuild, "BUILD DIFERENTE DO DO SERVIDOR",
			[$"estão na mesma versão ({minha.Texto}), mas são builds diferentes (o protocolo mudou)", outroBuild.DigitalCurta, minha.DigitalCurta],
			$"mesma versão ({minha.Texto}), mas builds diferentes (o protocolo mudou)", "versao-5-outro-build")) yield return o;

		foreach (object o in F6b_OResumoNaoAlargaOFormulario(boot, outroBuild)) yield return o;
		foreach (object o in F7_OClienteConfereSozinho(boot, cli, minha, naFrente)) yield return o;
		foreach (object o in F8_OServidorAntigo(boot, cli, minha)) yield return o;
		foreach (object o in F9_ACargaTorta(boot, cli, naFrente)) yield return o;
		foreach (object o in F10_DeNovoIgual(boot, cli)) yield return o;
	}

	// ---------------------------------------------------------------------- 1. o selo na primeira tela
	private IEnumerable F1_OSeloNaPrimeiraTela(Boot boot, IdentidadeDeBuild minha)
	{
		Nota("--- 1: o selo da versao na PRIMEIRA TELA ---");
		Label? selo = boot.SeloDaPrimeiraTela();
		Afirmar("**A VERSAO ESTA NA PRIMEIRA TELA**: o selo diz \"v<versao>\" -- a mesma que vai no pedido de conexao",
				selo != null && selo.Text == "v" + minha.Texto && selo.Text == SeloDeVersao.Texto && selo.IsVisibleInTree(), selo?.Text ?? "(sem selo)");
		if (selo == null) yield break;

		var (naQuina, dentro, r, tela) = Quina(selo);
		Afirmar($"...ancorado na quina de BAIXO, a direita, a {SeloDeVersao.MargemDaQuina} px das duas bordas, inteiro dentro da tela",
				naQuina && dentro, $"selo {r} | tela {tela}");
		Rect2 formulario = boot.RetanguloDoFormulario() ?? default;
		Afirmar("...fora do caminho: nao encosta no formulario de login, fica na metade de baixo da tela e nao pega clique",
				formulario.Size.X > 0 && !formulario.Intersects(r) && r.Position.Y > tela.Size.Y / 2 && selo.MouseFilter == Control.MouseFilterEnum.Ignore,
				$"formulario {formulario}");
		Afirmar($"...discreto: letra {SeloDeVersao.TamanhoDaLetra}, menor que a da interface",
				selo.GetThemeFontSize("font_size") == SeloDeVersao.TamanhoDaLetra && SeloDeVersao.TamanhoDaLetra < Tema.Atual.DefaultFontSize);

		if (DisplayServer.GetName() != "headless")
			Afirmar("o TITULO DA JANELA tambem diz a versao", GetWindow().Title.Contains("v" + minha.Texto), GetWindow().Title);
		else NaoMedido("a versao no titulo da janela", "sem janela (headless)");
		yield return 0.3;
		Fotografar("versao-1-primeira-tela");

		// ANCORADO, E NAO POSICIONADO: a tela muda de tamanho e ele continua na quina.
		Window raiz = GetTree().Root;
		_baseDaTela = raiz.ContentScaleSize;
		bool comDefeito = false;
		foreach (Vector2I nova in new[] { new Vector2I(960, 540), new Vector2I(1600, 900) })
		{
			raiz.ContentScaleSize = nova;
			yield return 0.35;
			Rect2 agora = GetViewport().GetVisibleRect();
			if (agora.Size == tela.Size) { NaoMedido($"o selo na tela de {nova.X} x {nova.Y}", "a tela nao mudou de tamanho"); continue; }

			var q = Quina(selo);
			Afirmar($"MUDOU A TELA pra {agora.Size.X:0} x {agora.Size.Y:0}: o selo continua na quina de baixo, inteiro dentro dela",
					q.NaQuina && q.Dentro, $"selo {q.Selo} | tela {q.Tela}");

			if (comDefeito || boot.CamadaDaPrimeiraTela() is not { } camada) continue;
			comDefeito = true;
			SeloDeVersao.PosicaoCravadaDeTeste = true;
			Label cravado = SeloDeVersao.Novo();
			SeloDeVersao.PosicaoCravadaDeTeste = false;
			camada.AddChild(cravado);
			yield return 0.2;
			var qc = Quina(cravado);
			Afirmar("(defeito injetado: o selo numa posicao cravada, e nao ancorado) nesta tela ele fica FORA da quina -- a regua sabe reprovar",
					!(qc.NaQuina && qc.Dentro), $"selo {qc.Selo} | tela {qc.Tela}");
			camada.RemoveChild(cravado);
			cravado.QueueFree();
		}
		raiz.ContentScaleSize = _baseDaTela.Value;
		_baseDaTela = null;
		yield return 0.35;
		var deVolta = Quina(selo);
		Afirmar("de volta ao tamanho de antes, o selo esta na quina de novo", deVolta.NaQuina && deVolta.Dentro, $"selo {deVolta.Selo} | tela {deVolta.Tela}");
	}

	// ---------------------------------------------------------------------- 2. o selo na pausa
	private IEnumerable F2_OSeloNaPausa(IdentidadeDeBuild minha)
	{
		Nota("--- 2: o selo na PAUSA (no lobby ela e a tela de opcoes) ---");
		PauseMenu? pausa = PauseMenu.Instancia;
		Label? fechado = pausa?.FindChild(SeloDeVersao.Nome, true, false) as Label;
		Afirmar("(montagem) com a pausa FECHADA o selo dela nao aparece (ele mora no veu)", pausa != null && fechado != null && !fechado.IsVisibleInTree());
		pausa?.Abrir();
		yield return 0.4;
		Label? selo = pausa?.FindChild(SeloDeVersao.Nome, true, false) as Label;
		Afirmar("**A VERSAO ESTA NA PAUSA**: o mesmo selo, com o mesmo texto, a mostra",
				selo != null && selo.Text == "v" + minha.Texto && selo.IsVisibleInTree(), selo?.Text ?? "(sem selo)");
		if (selo != null)
		{
			var q = Quina(selo);
			Afirmar("...na mesma quina de baixo, a direita, dentro da tela", q.NaQuina && q.Dentro, $"selo {q.Selo} | tela {q.Tela}");
		}
		Fotografar("versao-2-pausa-opcoes");
		pausa?.Fechar("bancada");
		yield return 0.2;
	}

	// ---------------------------------------------------------------------- 3. quem hospeda passa direto
	private IEnumerable F3_QuemHospedaPassaDireto(Boot boot, GameClient cli)
	{
		Nota("--- 3: quem HOSPEDA passa direto (botao \"Hospedar partida\": servidor e cliente no mesmo processo) ---");
		Afirmar("(montagem) o formulario do menu principal esta inteiro: servidor, conta, senha, \"Entrar\" e \"Hospedar partida\"", Formulario() != null);
		Afirmar("(montagem) ainda nao ha servidor de pe neste processo", S is { Running: false });

		GameServer.EspiaoDeEntrada += Espiar;
		_espiando = true;
		int pacotes0 = _pacotesNoServidor;
		ApertarEntrar(hospedar: true);
		// (v0.3.0) A TELA DE HOSPEDAR: o servidor ainda nao subiu, e a pergunta do modo das racas esta na frente
		// (`Boot.ModoDeRacas.cs`). O robo responde como o dono responderia, no padrao.
		Afirmar("(v0.3.0) hospedar abre ANTES a pergunta do modo das racas: \"Modo padrão\" e \"Raças aleatórias\" na tela, e nada de servidor ainda",
				boot.EscolhaDoModoNaTela && BotaoDaPrimeiraTela("Modo padrão") != null && BotaoDaPrimeiraTela("Raças aleatórias") != null
				&& S is { Running: false });
		BotaoDaPrimeiraTela("Modo padrão")?.EmitSignal(BaseButton.SignalName.Pressed);
		foreach (object o in Ate(() => boot.SelecaoNaTela != null || Aviso.Length > 0, 60)) yield return o;
		yield return 0.3;

		Afirmar("**VERSAO IGUAL ENTRA**: o servidor sobe, a conta e aceita e a selecao de personagem aparece, sem aviso nenhum",
				S is { Running: true } && boot.SelecaoNaTela != null && Aviso.Length == 0 && cli.Connected && cli.QuedaPendente == null,
				$"aviso='{Aviso}' estado='{Estado}' conectado={cli.Connected}");
		Afirmar("...a porta anotou \"aceito\", o cliente leu o `S2C.Versao` sem sobrar nem faltar byte, e o LOGIN chegou ao servidor",
				S?.UltimoPedidoDeConexao is { } u && u.Juizo == FioDaVersao.JuizoDoPedido.Aceito && cli.PacotesMalLidos(Protocol.S2C.Versao) == 0
				&& _pacotesNoServidor > pacotes0, $"pacotes no servidor +{_pacotesNoServidor - pacotes0}");

		int quedas0 = _quedasOuvidas;
		Afirmar("(montagem) a selecao tem o botao \"Trocar de servidor\"", SairDaSelecao());
		yield return 0.6;
		Afirmar("sair da selecao por vontade nao e queda: o menu volta sem aviso, sem evento e sem nada pendente",
				boot.MenuPrincipalNaTela && boot.SelecaoNaTela == null && Aviso.Length == 0 && cli.QuedaPendente == null
				&& !cli.Connected && _quedasOuvidas == quedas0, $"aviso='{Aviso}' quedas +{_quedasOuvidas - quedas0}");
	}

	// ---------------------------------------------------------------------- 4/5/6. barrado pela versao
	/// <summary>
	/// O servidor se apresenta com <paramref name="doServidor"/>, o robo aperta "Entrar", e o jogo tem de voltar
	/// ao menu principal com o painel dizendo <paramref name="titulo"/> e todos os <paramref name="trechos"/>.
	/// Com <paramref name="servidorCego"/> o servidor ACEITA o pedido (nao compara), e quem barra e o cliente.
	/// </summary>
	private IEnumerable Barrado(Boot boot, GameClient cli, string cena, IdentidadeDeBuild doServidor, string titulo, string[] trechos,
								string resumo, string foto, bool servidorCego = false)
	{
		GameServer srv = S!;
		srv.VersaoDeTeste = doServidor;
		srv.AceitaQualquerVersaoDeTeste = servidorCego;
		int quedas0 = _quedasOuvidas, pacotes0 = _pacotesNoServidor, recusas0 = srv.RecusasPorVersao;
		float largura0 = boot.LarguraDaColunaDoEstado() ?? 0;

		Afirmar($"(montagem, {cena}) o menu principal esta livre pra apertar \"Entrar\"", boot.MenuPrincipalNaTela && Aviso.Length == 0 && ApertarEntrar());
		foreach (object o in Ate(() => Aviso.Length > 0 || boot.SelecaoNaTela != null, 10)) yield return o;
		yield return 0.3;   // um quadro desenhado com o painel no ar (a foto e dele)

		string aviso = Aviso;
		Afirmar($"**{cena}: O JOGO VOLTA AO MENU AVISANDO** -- o painel diz \"{titulo}\" e a frase com as duas versoes e a direcao",
				aviso.Contains(titulo, StringComparison.Ordinal) && trechos.All(t => aviso.Contains(t, StringComparison.Ordinal)), aviso);
		Afirmar("...no MENU PRINCIPAL: o formulario esta na tela, sem selecao e sem mundo, e a conexao foi desfeita",
				boot.MenuPrincipalNaTela && boot.SelecaoNaTela == null && !boot.MundoMontado && !cli.Connected);
		Afirmar("...pelo caminho da QUEDA: uma queda so, ja tratada, e a linha de estado guarda o resumo",
				_quedasOuvidas == quedas0 + 1 && cli.QuedaPendente == null && Estado == resumo + ".", $"quedas +{_quedasOuvidas - quedas0} | estado '{Estado}'");
		float largura = boot.LarguraDaColunaDoEstado() ?? 0;
		Afirmar("...e o resumo, mais comprido que a coluna, QUEBRA em linhas: a coluna do formulario nao alarga",
				largura0 > 0 && Math.Abs(largura - largura0) <= 1f, $"largura da coluna {largura0:0} -> {largura:0}");
		Afirmar(servidorCego
					? "...e quem barrou foi O CLIENTE: o servidor ACEITOU o pedido, e mesmo assim nenhum pacote meu (nem o login) chegou a ele"
					: "...e quem barrou foi O SERVIDOR, no pedido de conexao: nenhum pacote meu (nem o login) chegou a ele",
				_pacotesNoServidor == pacotes0 && srv.RecusasPorVersao == recusas0 + (servidorCego ? 0 : 1),
				$"pacotes no servidor +{_pacotesNoServidor - pacotes0} | recusas por versao +{srv.RecusasPorVersao - recusas0}");
		Fotografar(foto);

		Afirmar("o aviso tem um OK, e ele aceita o clique", boot.ApertarOkDoAvisoDeQueda());
		yield return 0.3;
		Afirmar("...o OK fecha o aviso, e o resumo continua na linha de estado do formulario", Aviso.Length == 0 && boot.MenuPrincipalNaTela && Estado == resumo + ".", Estado);

		srv.VersaoDeTeste = null;
		srv.AceitaQualquerVersaoDeTeste = false;
	}

	// ---------------------------------------------------------------------- 6b. o resumo nao alarga o formulario
	private IEnumerable F6b_OResumoNaoAlargaOFormulario(Boot boot, IdentidadeDeBuild outroBuild)
	{
		GameServer srv = S!;
		srv.VersaoDeTeste = outroBuild;
		Boot.ResumoSemQuebraDeTeste = true;
		float largura0 = boot.LarguraDaColunaDoEstado() ?? 0;
		ApertarEntrar();
		foreach (object o in Ate(() => Aviso.Length > 0, 10)) yield return o;
		yield return 0.3;
		float largura = boot.LarguraDaColunaDoEstado() ?? 0;
		Afirmar("(defeito injetado: o resumo sem quebra de linha) a coluna do formulario ALARGA pra caber a frase -- a regua sabe reprovar",
				Aviso.Length > 0 && largura > largura0 + 20, $"largura da coluna {largura0:0} -> {largura:0}");
		Boot.ResumoSemQuebraDeTeste = false;
		boot.ApertarOkDoAvisoDeQueda();
		yield return 0.3;

		// O MESMO AVISO, SEM O DEFEITO: a linha de estado volta a quebrar, e o formulario volta ao tamanho dele.
		ApertarEntrar();
		foreach (object o in Ate(() => Aviso.Length > 0, 10)) yield return o;
		yield return 0.3;
		float deVolta = boot.LarguraDaColunaDoEstado() ?? 0;
		Afirmar("desligado o defeito, o mesmo aviso volta a caber: a coluna do formulario tem a largura de antes",
				Aviso.Length > 0 && Math.Abs(deVolta - largura0) <= 1f, $"largura da coluna {largura:0} -> {deVolta:0} (antes {largura0:0})");
		srv.VersaoDeTeste = null;
		boot.ApertarOkDoAvisoDeQueda();
		yield return 0.3;
	}

	// ---------------------------------------------------------------------- 7. o cliente confere sozinho
	private IEnumerable F7_OClienteConfereSozinho(Boot boot, GameClient cli, IdentidadeDeBuild minha, IdentidadeDeBuild naFrente)
	{
		Nota("--- 7: o servidor que ACEITA QUALQUER UM -- quem barra e o cliente, pela versao do primeiro pacote ---");
		foreach (object o in Barrado(boot, cli, "SERVIDOR QUE NAO CONFERE, NA FRENTE", naFrente, "VERSÃO DIFERENTE DA DO SERVIDOR",
			[$"Este servidor está na versão {naFrente.Texto} e o seu jogo na {minha.Texto}", "o seu jogo está ATRASADO — atualize o jogo."],
			$"seu jogo ({minha.Texto}) está ATRASADO: o servidor está na {naFrente.Texto}", "versao-7-o-cliente-barra", servidorCego: true)) yield return o;

		GameServer srv = S!;
		srv.VersaoDeTeste = naFrente;
		srv.AceitaQualquerVersaoDeTeste = true;
		GameClient.NaoConfereOServidorDeTeste = true;
		ApertarEntrar();
		foreach (object o in Ate(() => boot.SelecaoNaTela != null || Aviso.Length > 0, 12)) yield return o;
		Afirmar("(defeito injetado: o cliente nao confere a versao que o servidor manda) ele ENTRA num servidor de outra versao -- a regua sabe reprovar",
				boot.SelecaoNaTela != null && Aviso.Length == 0 && cli.Connected, $"aviso='{Aviso}'");
		GameClient.NaoConfereOServidorDeTeste = false;
		srv.VersaoDeTeste = null;
		srv.AceitaQualquerVersaoDeTeste = false;
		SairDaSelecao();
		yield return 0.6;
	}

	// ---------------------------------------------------------------------- 8. o servidor antigo
	private IEnumerable F8_OServidorAntigo(Boot boot, GameClient cli, IdentidadeDeBuild minha)
	{
		Nota("--- 8: o servidor ANTIGO -- aceita so pela senha e nunca diz a versao ---");
		GameServer srv = S!;
		double prazo = GameClient.PrazoDaVersaoMs / 1000.0;
		string resumo = $"servidor sem versão (antigo); o seu jogo está na {minha.Texto}";
		srv.PortaAntigaDeTeste = true;

		// PRIMEIRO COM O DEFEITO: o cliente sem o prazo, que e o jogo que ficaria esperando pra sempre.
		GameClient.SemPrazoDaVersaoDeTeste = true;
		int pacotes0 = _pacotesNoServidor, quedas0 = _quedasOuvidas;
		Afirmar("(montagem) o menu principal esta livre pra apertar \"Entrar\"", boot.MenuPrincipalNaTela && Aviso.Length == 0 && ApertarEntrar());
		yield return prazo + 1.5;
		Afirmar("(defeito injetado: sem o prazo da versao) contra o servidor antigo o jogo fica em \"conectando...\": conectado, mudo, sem aviso -- a regua sabe reprovar",
				cli.Connected && Aviso.Length == 0 && Estado == "conectando..." && cli.QuedaPendente == null && _quedasOuvidas == quedas0,
				$"conectado={cli.Connected} estado='{Estado}' aviso='{Aviso}'");
		Afirmar("...e o LOGIN NAO SAIU: o servidor que nao disse a versao nao recebeu nenhum pacote meu", _pacotesNoServidor == pacotes0,
				$"pacotes no servidor +{_pacotesNoServidor - pacotes0}");

		GameClient.SemPrazoDaVersaoDeTeste = false;
		ulong desligou = Time.GetTicksMsec();
		foreach (object o in Ate(() => Aviso.Length > 0, 3)) yield return o;
		Afirmar("desligado o defeito, a MESMA espera vence no quadro seguinte (a regra e por estado): o aviso \"SERVIDOR SEM VERSÃO\" aparece",
				Aviso.Contains("SERVIDOR SEM VERSÃO", StringComparison.Ordinal) && Time.GetTicksMsec() - desligou < 1500, Aviso);
		boot.ApertarOkDoAvisoDeQueda();
		yield return 0.3;

		// AGORA O PRAZO DE VERDADE, cronometrado do clique ao aviso.
		pacotes0 = _pacotesNoServidor;
		quedas0 = _quedasOuvidas;
		ulong clique = Time.GetTicksMsec();
		ApertarEntrar();
		foreach (object o in Ate(() => Aviso.Length > 0, prazo + 5, 0.05)) yield return o;
		double levou = (Time.GetTicksMsec() - clique) / 1000.0;
		yield return 0.3;
		string aviso = Aviso;
		Afirmar($"**SERVIDOR ANTIGO: O CLIENTE PERCEBE SOZINHO** -- em {prazo:0.#} s (o prazo) ele desliga e volta ao menu com o aviso",
				aviso.Contains("SERVIDOR SEM VERSÃO", StringComparison.Ordinal) && aviso.Contains("não informou a versão", StringComparison.Ordinal)
				&& aviso.Contains("servidor antigo", StringComparison.Ordinal) && aviso.Contains(minha.Texto, StringComparison.Ordinal)
				&& levou >= prazo - 0.15 && levou <= prazo + 2.0, $"levou {levou:0.00} s | {aviso}");
		Afirmar("...no menu principal, pelo caminho da queda, com o resumo na linha de estado -- e o login nunca saiu",
				boot.MenuPrincipalNaTela && !cli.Connected && cli.QuedaPendente == null && _quedasOuvidas == quedas0 + 1
				&& Estado == resumo + "." && _pacotesNoServidor == pacotes0, $"estado '{Estado}' | pacotes no servidor +{_pacotesNoServidor - pacotes0}");
		Fotografar("versao-6-servidor-antigo");
		boot.ApertarOkDoAvisoDeQueda();
		yield return 0.3;
		srv.PortaAntigaDeTeste = false;
	}

	// ---------------------------------------------------------------------- 9. a carga torta
	private IEnumerable F9_ACargaTorta(Boot boot, GameClient cli, IdentidadeDeBuild naFrente)
	{
		Nota("--- 9: a carga da recusa vem torta -- aviso generico, sem excecao ---");
		GameServer srv = S!;
		var w = new NetDataWriter();
		FioDaVersao.EscreverBloco(w, naFrente);
		byte[] bloco = w.CopyData();
		byte[] lixo = [.. Enumerable.Range(0, FioDaVersao.BytesDoBloco).Select(i => (byte)(i * 53 + 7))];

		foreach ((string nome, byte[] carga) in new (string, byte[])[] { ("VAZIA", []), ("TRUNCADA (9 dos 16 bytes)", bloco[..9]), ("DE LIXO (16 bytes sem a marca)", lixo) })
		{
			srv.VersaoDeTeste = naFrente;
			srv.CargaDaRecusaDeTeste = carga;
			int quedas0 = _quedasOuvidas;
			ApertarEntrar();
			foreach (object o in Ate(() => _quedasOuvidas > quedas0 && cli.QuedaPendente == null, 8)) yield return o;
			yield return 0.2;
			Afirmar($"CARGA {nome}: o jogador le o aviso GENERICO na linha de estado, sem painel de versao, sem excecao e sem ficar em \"conectando...\"",
					_quedasOuvidas == quedas0 + 1 && Estado == "sem conexão: o servidor recusou a conexão." && Aviso.Length == 0
					&& boot.MenuPrincipalNaTela && !cli.Connected, $"quedas +{_quedasOuvidas - quedas0} | estado '{Estado}' | aviso '{Aviso}'");
		}
		srv.CargaDaRecusaDeTeste = null;
		srv.VersaoDeTeste = null;
	}

	// ---------------------------------------------------------------------- 10. de novo, igual
	private IEnumerable F10_DeNovoIgual(Boot boot, GameClient cli)
	{
		Nota("--- 10: tudo desligado -- a versao igual entra de novo ---");
		ApertarEntrar();
		foreach (object o in Ate(() => boot.SelecaoNaTela != null || Aviso.Length > 0, 12)) yield return o;
		Afirmar("depois de todas as recusas, a versao IGUAL entra de novo (a selecao aparece, sem aviso), e nenhum `S2C.Versao` foi mal lido na sessao",
				boot.SelecaoNaTela != null && Aviso.Length == 0 && cli.Connected && cli.PacotesMalLidos(Protocol.S2C.Versao) == 0, $"aviso='{Aviso}' estado='{Estado}'");
		SairDaSelecao();
		yield return 0.5;
	}

	// =====================================================================
	// O ROTEIRO DE DOIS PROCESSOS (`--versaodupla <esperado>`): este e o JOGADOR
	// =====================================================================
	/// <summary>Quanto o jogador insiste ate o outro processo abrir a porta (o `duas.py` o sobe 20 s depois deste).</summary>
	private const double TetoDeEsperaDoServidorSeg = 150;

	private IEnumerable RoteiroDeDois()
	{
		Boot boot = _boot!;
		GameClient cli = C!;
		foreach (object o in Ate(() => boot.MenuPrincipalNaTela, 30)) yield return o;
		yield return 0.5;

		IdentidadeDeBuild minha = cli.MinhaVersao;
		Nota("==================================================================================");
		Nota($" DOIS PROCESSOS: este e o JOGADOR (versao {minha.Texto}, protocolo {minha.DigitalCurta}); espero \"{_papel}\"");
		Nota("==================================================================================");
		Afirmar("(montagem) este processo NAO hospeda: o servidor e o outro processo", S is not { Running: true });
		Afirmar("(montagem) o desfecho esperado e um dos cinco conhecidos",
				_papel is "atrasado" or "adiantado" or "outrobuild" or "semversao" or "igual", _papel);
		Label? selo = boot.SeloDaPrimeiraTela();
		Afirmar("o selo da primeira tela diz a versao deste processo", selo != null && selo.Text == "v" + minha.Texto, selo?.Text ?? "(sem selo)");
		if (_papel == "igual") boot.LigarOModoAutomaticoDeTeste();

		// DISCA ATE O SERVIDOR ATENDER. Enquanto ele nao sobe, cada tentativa termina em "nao foi possivel alcancar o
		// servidor" na linha de estado (o aviso generico) -- e isso tambem e o caminho da queda funcionando.
		ulong comeco = Time.GetTicksMsec();
		int tentativas = 0, semServidor = 0;
		bool desfecho = false;
		while (!desfecho && (Time.GetTicksMsec() - comeco) / 1000.0 < TetoDeEsperaDoServidorSeg)
		{
			tentativas++;
			int quedas0 = _quedasOuvidas;
			if (!ApertarEntrar()) break;
			foreach (object o in Ate(() => Aviso.Length > 0 || boot.MundoMontado || boot.SelecaoNaTela != null
											|| (_quedasOuvidas > quedas0 && cli.QuedaPendente == null), 60)) yield return o;
			// CONECTADO E AINDA SEM DESFECHO (o mundo esta montando): espera mais, sem discar por cima.
			if (cli.Connected && Aviso.Length == 0 && !boot.MundoMontado && boot.SelecaoNaTela == null)
				foreach (object o in Ate(() => Aviso.Length > 0 || boot.MundoMontado || !cli.Connected, 90)) yield return o;

			desfecho = Aviso.Length > 0 || boot.MundoMontado || boot.SelecaoNaTela != null;
			if (desfecho) break;
			if (Estado.Contains("não foi possível alcançar o servidor", StringComparison.Ordinal)) semServidor++;
			yield return 1.0;
		}
		yield return 0.4;
		Nota($"  {tentativas} tentativa(s) em {(Time.GetTicksMsec() - comeco) / 1000.0:0} s; {semServidor} delas antes de o servidor abrir a porta (aviso generico na linha de estado)");
		Afirmar("o servidor do outro processo atendeu dentro do prazo da bancada", desfecho, $"estado '{Estado}'");

		string aviso = Aviso;
		Match noAviso = Regex.Match(aviso, @"Este servidor está na versão (\d+\.\d+\.\d+) e o seu jogo na (\d+\.\d+\.\d+)");
		int doServidor = noAviso.Success && Versao.TentarLer(noAviso.Groups[1].Value, out int ns) ? ns : -1;
		bool noMenu = boot.MenuPrincipalNaTela && !boot.MundoMontado && boot.SelecaoNaTela == null && !cli.Connected && cli.QuedaPendente == null;

		switch (_papel)
		{
			case "atrasado":
				Afirmar("**O SERVIDOR ESTA NA FRENTE: o jogo volta ao MENU com a frase certa** -- as duas versoes, \"ATRASADO\" e \"atualize o jogo\"",
						aviso.Contains("VERSÃO DIFERENTE DA DO SERVIDOR", StringComparison.Ordinal) && noAviso.Success && noAviso.Groups[2].Value == minha.Texto
						&& doServidor > minha.Numero && aviso.Contains("o seu jogo está ATRASADO — atualize o jogo.", StringComparison.Ordinal), aviso);
				Afirmar("...no menu principal, sem mundo, com o resumo na linha de estado", noMenu && Estado.Contains("ATRASADO", StringComparison.Ordinal), Estado);
				break;
			case "adiantado":
				Afirmar("**O SERVIDOR ESTA ATRAS: o jogo volta ao MENU com a frase certa** -- as duas versoes, \"ADIANTADO\" e \"o servidor ainda não foi atualizado\"",
						aviso.Contains("VERSÃO DIFERENTE DA DO SERVIDOR", StringComparison.Ordinal) && noAviso.Success && noAviso.Groups[2].Value == minha.Texto
						&& doServidor >= 0 && doServidor < minha.Numero
						&& aviso.Contains("o seu jogo está ADIANTADO — o servidor ainda não foi atualizado.", StringComparison.Ordinal), aviso);
				Afirmar("...no menu principal, sem mundo, com o resumo na linha de estado", noMenu && Estado.Contains("ADIANTADO", StringComparison.Ordinal), Estado);
				break;
			case "outrobuild":
				Afirmar("**MESMA VERSAO, OUTRO PROTOCOLO: o jogo volta ao MENU com \"builds diferentes\"** e as duas digitais",
						aviso.Contains("BUILD DIFERENTE DO DO SERVIDOR", StringComparison.Ordinal)
						&& aviso.Contains($"estão na mesma versão ({minha.Texto}), mas são builds diferentes (o protocolo mudou)", StringComparison.Ordinal)
						&& aviso.Contains(minha.DigitalCurta, StringComparison.Ordinal), aviso);
				Afirmar("...no menu principal, sem mundo, com o resumo na linha de estado", noMenu && Estado.Contains("builds diferentes", StringComparison.Ordinal), Estado);
				break;
			case "semversao":
				Afirmar("**SERVIDOR ANTIGO (nunca diz a versao): o jogo percebe sozinho e volta ao MENU** com \"servidor sem versão\"",
						aviso.Contains("SERVIDOR SEM VERSÃO", StringComparison.Ordinal) && aviso.Contains("servidor antigo", StringComparison.Ordinal)
						&& aviso.Contains(minha.Texto, StringComparison.Ordinal), aviso);
				Afirmar("...no menu principal, sem mundo, com o resumo na linha de estado", noMenu && Estado.Contains("servidor sem versão (antigo)", StringComparison.Ordinal), Estado);
				break;
			case "igual":
				Afirmar("**VERSOES IGUAIS: ENTRA NORMAL** -- o personagem esta no MUNDO do outro processo, sem aviso nenhum",
						boot.MundoMontado && cli.Connected && cli.LocalId != 0 && aviso.Length == 0 && cli.QuedaPendente == null,
						$"mundo={boot.MundoMontado} conectado={cli.Connected} id={cli.LocalId} aviso='{aviso}'");
				Afirmar("...e o `S2C.Versao` do servidor foi lido sem sobrar nem faltar byte", cli.PacotesMalLidos(Protocol.S2C.Versao) == 0);
				break;
		}

		if (_papel != "igual" && aviso.Length > 0)
		{
			Afirmar("o OK fecha o aviso e deixa o menu principal livre", boot.ApertarOkDoAvisoDeQueda());
			yield return 0.3;
			Afirmar("...e o formulario continua de pe, pronto pra tentar outro servidor", Aviso.Length == 0 && boot.MenuPrincipalNaTela && Formulario() != null);
		}
	}
}
