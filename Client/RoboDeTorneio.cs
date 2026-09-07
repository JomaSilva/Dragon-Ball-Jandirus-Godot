using Godot;
using Jandirus.Net;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO CONVITE DO TORNEIO (`--diagtorneio`).
///
/// O pedido do dono (2026-09-06): *"notificacao de convite (canto inferior direito) com Participar/
/// Recusar e tempo limite"*. Roda com `--host`: o servidor esta NESTE processo, entao o robo abre um
/// torneio de verdade pelo verbo de admin (`trn_iniciar`), o convite chega pelo pacote de rede
/// (`S2C.Torneio`) como chegaria a qualquer jogador, o painel aparece, o botao manda o verbo, e o
/// servidor -- lido pelo gancho `InscritosDeTeste` -- confirma que a inscricao entrou. So a contagem
/// vencida usa o convite injetado (um torneio de verdade levaria 90 s).
///
/// COMO RODAR (uma janela so, ou headless):
///     Godot --path . --host --rede 7932 --diagtorneio --raca Human --conta &lt;NOVA&gt; --nome Zx
/// </summary>
public partial class RoboDeTorneio : Node
{
	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];
	private bool _acabou;
	private int _passo;
	private double _t, _espera;
	private const double EsperaMaxima = 30.0;
	private string? _ultimoVerbo;

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (GameClient.Instance is not { Connected: true } cli
		 || Hud.Instancia is not { } hud
		 || hud.FindChild("Torneio", true, false) is not PainelDoTorneio painel
		 || GameServer.Instance is not { } servidor)
		{
			_espera += delta;
			if (_espera > EsperaMaxima)
			{
				Conferir(false, $"em {EsperaMaxima:0}s o cliente conectou, o HUD nasceu com o painel `Torneio` e o servidor esta neste processo");
				Fechar();
			}
			return;
		}
		_espera = 0;
		_t += delta;
		switch (_passo)
		{
			case 0:
				GD.Print("[diagtorneio] ===== O CONVITE DO TORNEIO NA TELA =====");
				Conferir(!painel.VisivelDeTeste, "sem torneio o painel do convite fica escondido");
				Conferir(painel.AnchorLeft == 1 && painel.AnchorTop == 1 && painel.OffsetRight < 0 && painel.OffsetBottom < 0,
						 "o painel ancora no canto INFERIOR DIREITO da tela");
				GameClient.EspiaoDeVerbos = (cmd, arg) => { _ultimoVerbo = cmd; return true; };
				cli.SendVerbo("trn_iniciar", "terra");
				Passar();
				break;
			case 1:
				// O CONVITE VEM PELA REDE: o servidor abriu o torneio e mandou o `S2C.Torneio` pro host.
				if (!painel.VisivelDeTeste && _t < 5) return;
				Conferir(painel.VisivelDeTeste, $"em {_t:0.0}s o convite do torneio aberto pelo servidor chegou pela REDE e o painel apareceu");
				Conferir(painel.TituloDeTeste.Contains("TORNEIO", StringComparison.OrdinalIgnoreCase) && painel.TipoDeTeste == 1,
						 $"o painel diz que e o torneio da Terra (\"{painel.TituloDeTeste}\")");
				Conferir(painel.SegundosDeTeste > 0 && painel.SegundosDeTeste <= 91,
						 $"o prazo de resposta veio do servidor ({painel.SegundosDeTeste:0} s, o `conviteSegundos`)");
				Conferir(servidor.TorneioAtivo && servidor.InscritosDeTeste == 0, "o servidor tem o torneio aberto e ninguem inscrito ainda");
				Passar();
				break;
			case 2:
				// UM QUADRO DEPOIS: o painel ficou visivel no quadro do pacote, mas so e DESENHADO no
				// seguinte -- a foto tirada no mesmo quadro saia sem ele.
				if (_t < 0.3) return;
				Fotografar("torneio-1-convite");
				painel.ClicarParticiparDeTeste();
				Conferir(_ultimoVerbo == "trn_participar" && !painel.VisivelDeTeste, "o botao Participar manda `trn_participar` e o painel fecha");
				Passar();
				break;
			case 3:
				if (servidor.InscritosDeTeste == 0 && _t < 5) return;
				Conferir(servidor.InscritosDeTeste == 1, $"em {_t:0.0}s o servidor recebeu a inscricao pela rede (inscritos: {servidor.InscritosDeTeste})");
				cli.SendVerbo("trn_recusar");
				Passar();
				break;
			case 4:
				if (servidor.InscritosDeTeste == 1 && _t < 5) return;
				Conferir(servidor.InscritosDeTeste == 0, "`trn_recusar` pela rede tira a inscricao");
				cli.SendVerbo("trn_cancelar");
				Passar();
				break;
			case 5:
				if (servidor.TorneioAtivo && _t < 5) return;
				Conferir(!servidor.TorneioAtivo, "`trn_cancelar` (admin) encerra o torneio");
				Conferir(!painel.VisivelDeTeste, "...e o painel continua fechado");
				// A CONTAGEM VENCIDA: convite injetado de 2 s -- some sozinho, sem resposta (= recusa).
				_ultimoVerbo = null;
				painel.Convidar(2, 2, "");
				Conferir(painel.VisivelDeTeste && painel.TituloDeTeste.Contains("OUTRO MUNDO"), "um convite do Outro Mundo sem titulo ganha o titulo padrao");
				Passar();
				break;
			case 6:
				if (_t < 2.6) return;
				Conferir(!painel.VisivelDeTeste, "vencido o prazo (2 s) o painel some sozinho");
				Conferir(_ultimoVerbo == null, "...sem mandar verbo nenhum (silencio e nao)");
				painel.Convidar(1, 30, "x");
				painel.ClicarRecusarDeTeste();
				Conferir(_ultimoVerbo == "trn_recusar" && !painel.VisivelDeTeste, "o botao Recusar manda `trn_recusar` e fecha");
				painel.Convidar(1, 30, "x");
				painel.Fechar();
				Conferir(!painel.VisivelDeTeste, "o aviso 2 do servidor (convite fechado) esconde o painel");
				GameClient.EspiaoDeVerbos = null;
				Passar();
				break;

			// =====================================================================
			// O CHAVEAMENTO NA TELA, O CANTO "ASSISTIR" E A TRAVA DA ESPERA (dono, 2026-09-07)
			// =====================================================================
			case 7:
				GD.Print("[diagtorneio] ===== O CHAVEAMENTO, O ASSISTIR E A AREA DE ESPERA =====");
				cli.SendVerbo("trn_iniciar", "terra");
				Passar();
				break;
			case 8:
				if (!painel.VisivelDeTeste && _t < 5) return;
				Conferir(painel.VisivelDeTeste, "o convite do segundo torneio chegou");
				painel.ClicarParticiparDeTeste();
				Passar();
				break;
			case 9:
				if (servidor.InscritosDeTeste == 0 && _t < 5) return;
				Conferir(servidor.InscritosDeTeste == 1, "o host esta inscrito");
				cli.SendVerbo("trn_avancar");   // fecha as inscricoes JA (verb de admin): a chave nasce
				Passar();
				break;
			case 10:
			{
				PainelDaChave? chave = hud.FindChild("Chave", true, false) as PainelDaChave;
				PainelDeAssistir? assistir = hud.FindChild("Assistir", true, false) as PainelDeAssistir;
				if ((chave == null || !chave.VisivelDeTeste) && _t < 6) return;
				Conferir(chave != null && assistir != null, "o HUD tem o painel da chave e o canto de assistir");
				if (chave == null || assistir == null) { Fechar(); return; }
				Conferir(chave.VisivelDeTeste, $"em {_t:0.0}s a chave nasceu no servidor, veio pela REDE (S2C.Chave) e o chaveamento ABRIU sozinho");
				Conferir(chave.CompetidoresDeTeste == 32, $"o chaveamento tem os 32 nomes ({chave.CompetidoresDeTeste})");
				Conferir(chave.MinhaChaveDeTeste >= 0, $"...e sabe qual sou eu (indice {chave.MinhaChaveDeTeste})");
				Conferir(chave.TituloDeTeste.Contains("chaveamento", StringComparison.OrdinalIgnoreCase), $"o titulo diz o que e (\"{chave.TituloDeTeste}\")");
				Conferir(assistir.VisivelDeTeste, "o canto 'Assistir torneio' apareceu (eu espero a minha vez)");
				Conferir(assistir.AnchorLeft == 0 && assistir.AnchorTop == 1 && assistir.OffsetLeft > 0 && assistir.OffsetBottom < 0,
						 "...no canto INFERIOR ESQUERDO da tela (acima do chat, que mora no canto)");
				Conferir(World.Instancia?.PorQueOCorpoNaoAnda == "na area de espera do torneio",
						 $"o corpo local esta TRAVADO pela espera -- nem tenta andar (\"{World.Instancia?.PorQueOCorpoNaoAnda}\")");
				Passar();
				break;
			}
			case 11:
			{
				// UM QUADRO DEPOIS (o desenho e por quadro): a minha luta esta marcada e o pulso tem valor.
				if (_t < 0.7) return;
				PainelDaChave? chave = hud.FindChild("Chave", true, false) as PainelDaChave;
				if (chave == null) { Fechar(); return; }
				Conferir(chave.MinhaLutaDeTeste >= 0, $"a MINHA luta esta marcada pra pulsar (luta {chave.MinhaLutaDeTeste} da rodada)");
				_pulso = chave.PulsoDeTeste;
				_pulsoMin = _pulsoMax = _pulso;
				_quadros = chave.QuadrosDesenhadosDeTeste;
				Passar();
				break;
			}
			case 12:
			{
				// O PULSO: amostras por 0,8 s -- o brilho tem que variar de verdade, e o desenho tem que ser por quadro.
				PainelDaChave? chave = hud.FindChild("Chave", true, false) as PainelDaChave;
				PainelDeAssistir? assistir = hud.FindChild("Assistir", true, false) as PainelDeAssistir;
				if (chave == null || assistir == null) { Fechar(); return; }
				_pulsoMin = Math.Min(_pulsoMin, chave.PulsoDeTeste);
				_pulsoMax = Math.Max(_pulsoMax, chave.PulsoDeTeste);
				if (_t < 0.8) return;
				Conferir(chave.QuadrosDesenhadosDeTeste >= _quadros + 10, $"o chaveamento e redesenhado a cada quadro ({chave.QuadrosDesenhadosDeTeste - _quadros} quadros em 0,8 s)");
				Conferir(_pulsoMax - _pulsoMin > 0.15f, $"o contorno da minha luta PULSA (brilho de {_pulsoMin:0.00} a {_pulsoMax:0.00} em 0,8 s)");
				Fotografar("torneio-2-chaveamento");
				assistir.ClicarAssistirDeTeste();
				Conferir(assistir.AssistindoDeTeste && World.Instancia?.AssistindoDeTeste == true, "o botao Assistir liga a camera do espectador");
				Passar();
				break;
			}
			case 13:
				if (_t < 0.8) return;
				Conferir((World.Instancia?.DeslocamentoDaCameraDeTeste.Length() ?? 0f) > 20f,
						 $"a camera saiu de cima de mim e foi pra arena (deslocamento {World.Instancia?.DeslocamentoDaCameraDeTeste.Length():0} px)");
				Fotografar("torneio-3-assistindo");
				cli.SendVerbo("trn_cancelar");
				Passar();
				break;
			case 14:
			{
				PainelDaChave? chave = hud.FindChild("Chave", true, false) as PainelDaChave;
				PainelDeAssistir? assistir = hud.FindChild("Assistir", true, false) as PainelDeAssistir;
				if ((servidor.TorneioAtivo || assistir?.VisivelDeTeste == true) && _t < 5) return;
				Conferir(!servidor.TorneioAtivo, "o torneio foi cancelado");
				Conferir(chave?.VisivelDeTeste == false && assistir?.VisivelDeTeste == false, "...o chaveamento e o canto de assistir fecharam (aviso 2)");
				Conferir(World.Instancia?.AssistindoDeTeste == false, "...a camera parou de assistir sozinha");
				Conferir(World.Instancia?.PorQueOCorpoNaoAnda == "", $"...e o corpo local esta livre de novo (\"{World.Instancia?.PorQueOCorpoNaoAnda}\")");
				Passar();
				break;
			}
			case 15:
				if (_t < 1.2) return;
				Conferir((World.Instancia?.DeslocamentoDaCameraDeTeste.Length() ?? 999f) < 20f,
						 $"...e a camera voltou pra cima de mim (deslocamento {World.Instancia?.DeslocamentoDaCameraDeTeste.Length():0} px)");
				Fechar();
				break;
		}
	}

	private float _pulso, _pulsoMin, _pulsoMax;
	private int _quadros;

	private void Passar() { _passo++; _t = 0; }

	/// <summary>A foto do painel pro olho do dono; headless nao renderiza e a nota diz isso.</summary>
	private void Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _passos.Add("  --     sem foto (headless nao renderiza): o painel fica pro olho do dono -- rode com janela"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		_passos.Add($"  --     foto {caminho} ({img.GetWidth()}x{img.GetHeight()})");
	}

	private void Fechar()
	{
		_acabou = true;
		foreach (string p in _passos) GD.Print("[diagtorneio] " + p);
		GD.Print(_falhas.Count == 0 ? "[diagtorneio] ===== TUDO OK =====" : $"[diagtorneio] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[diagtorneio]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
