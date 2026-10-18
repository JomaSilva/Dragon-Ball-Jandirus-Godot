using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Races;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA IA RACIAL (`--diagiaraciais`, 2026-10-17). O dono: *"n esqueça de fazer as IAs saberem usar as passivas e ativas da raça, e os
/// boss de saga tb vao ter todas essas passivas e ativas a depender de sua raça"* -- e, em seguida, *"n esqueça de checar tudo com print de
/// tudo isso q estamos fazendo"*.
///
/// Com janela e com `--host`, nascendo Humano no campo aberto da Terra. O servidor deste processo monta as cenas
/// (`GameServer.MontarCenaDaIaRacialDeTeste`) e o laco de PRODUCAO dirige o corpo do mundo contra um alvo de pano; o robo espera a IA apertar
/// a ativa e fotografa:
///
///   0. CONTRA-EXEMPLO (`AtivasRaciaisNaIa.IaSemAtivasDeTeste`, a IA da v0.4.0): o Freeza da fabrica luta quatro segundos e NAO liga a Ira --
///      foto `iaracial-0-sem-ativas.png`;
///   1. o Freeza luta e LIGA a Unrestrained Wrath: o kiai dele grita "HAAAAAAA!!" -- foto `iaracial-1-ira.png`;
///   2. um Saiyajin Lendario com o Overflowing Power alem da folga DESABAFA: o GRITO de 3 s -- plantado dentro da esfera verde furiosa, com
///      os tiros saindo pra todo lado (o dono, 2026-10-17) -- fotos `iaracial-2-desabafo.png` (no meio) e `iaracial-2b-desabafo.png` (perto
///      do fim);
///   3. um Lendario Primal no LSSJ, com o alvo longe, carrega o DEMONIC SAIYAN parado: a esfera verde desenhada em volta dele -- foto
///      `iaracial-3-demonico.png`.
///
///     Godot --path . --host --rede 8457 --campoteste 12 --horateste 0.5 --semfauna --diagiaraciais --raca Human --conta bancada_diaraciais --nome Plateia
/// </summary>
public partial class RoboDaIaRacial : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private int _passo;
	private double _t, _espera;
	private Jandirus.Server.GameServer.CenaDaIaRacial? _cena;
	private Vector2 _ondeCarregou, _ondeGritou;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Passar() { _passo++; _t = 0; }

	private Image? Fotografar(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return null; }
		string caminho = ProjectSettings.GlobalizePath(destino);
		img.SavePng(caminho);
		_linhas.Add("  --     foto salva em " + caminho);
		return img;
	}

	/// <summary>O corpo deste id esta DESENHADO na tela (o node existe, visivel, e cai dentro da janela)?</summary>
	private bool DesenhadoNaTela(World mundo, int id)
	{
		if (mundo.CorpoDeTeste(id) is not { } corpo || !corpo.IsVisibleInTree()) return false;
		Vector2 naTela = corpo.GetGlobalTransformWithCanvas().Origin;
		Rect2 janela = GetViewport().GetVisibleRect();
		return janela.HasPoint(naTela);
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
		switch (_passo)
		{
			case 0:
				if (_t < 1.5) return;   // o corpo assenta no campo
				GD.Print("[diaraciais] ===== A IA COM AS ATIVAS DA RACA, NA TELA (dono, 2026-10-17) =====");
				srv.EmbateDeFoto_CeuLimpo(eu);   // a foto nao depende do clima do dia
				srv.EscutaDeTecnicasDaIa = [];
				Jandirus.Server.GameServer.EscutaDeFalas = [];
				AtivasRaciaisNaIa.IaSemAtivasDeTeste = true;
				_cena = srv.MontarCenaDaIaRacialDeTeste(eu, "ira");
				Conferir(_cena != null, "(palco) o Freeza nasce pela fabrica do lado do corpo, com um alvo de pano colado nele");
				if (_cena == null) { Fechar(); return; }
				Passar();
				break;

			case 1:   // O CONTRA-EXEMPLO: quatro segundos de luta sem a tabela das ativas
				if (_t < 4) return;
				{
					int npc = _cena!.Value.Npc;
					Conferir(!srv.IraDeTeste(npc) && !srv.IaApertouDeTeste(npc, Passivas.Ira) && DesenhadoNaTela(mundo, npc),
								  "CONTRA-EXEMPLO (defeito injetado: a IA da v0.4.0, sem as ativas da raca): o Freeza luta quatro segundos na tela e NAO liga a Ira");
					Fotografar("user://iaracial-0-sem-ativas.png");
				}
				srv.DesmontarCenaDaIaRacialDeTeste(_cena!.Value);
				AtivasRaciaisNaIa.IaSemAtivasDeTeste = false;
				_cena = srv.MontarCenaDaIaRacialDeTeste(eu, "ira");
				if (_cena == null) { Conferir(false, "(palco) o Freeza renasce pra cena de verdade"); Fechar(); return; }
				Passar();
				break;

			case 2:   // A IRA: espera a IA ligar
				if (!srv.IraDeTeste(_cena!.Value.Npc) && _t < 8) return;
				Passar();
				break;

			case 3:
				if (_t < 0.35) return;   // o kiai chega na tela
				{
					int npc = _cena!.Value.Npc;
					bool gritou = Jandirus.Server.GameServer.EscutaDeFalas?.Exists(f => f.Quem == npc && f.Texto.Contains("HAAAA")) == true;
					Conferir(srv.IraDeTeste(npc) && srv.IaApertouDeTeste(npc, Passivas.Ira) && gritou && DesenhadoNaTela(mundo, npc),
								  "O FREEZA DA FABRICA, na luta, LIGA a Unrestrained Wrath pela IA (o verb aceitou) e o kiai grita \"HAAAAAAA!!\" -- com ele desenhado na tela");
					Fotografar("user://iaracial-1-ira.png");
				}
				srv.DesmontarCenaDaIaRacialDeTeste(_cena!.Value);
				_cena = srv.MontarCenaDaIaRacialDeTeste(eu, "desabafo");
				if (_cena == null) { Conferir(false, "(palco) o Saiyajin Lendario nasce pra cena do desabafo"); Fechar(); return; }
				Passar();
				break;

			case 4:   // O DESABAFO: espera o transbordar apagar -- e o grito comecar
				if (srv.TransbordandoDeTeste(_cena!.Value.Npc) && _t < 6) return;
				_ondeGritou = srv.PosicaoDeTeste(_cena.Value.Npc) is { } g ? new Vector2(g.X, g.Y) : Vector2.Zero;
				Passar();
				break;

			case 5:   // NO MEIO DO GRITO
				if (_t < 1.2) return;
				FotoDoGrito(srv, mundo, "user://iaracial-2-desabafo.png", "NO MEIO DO GRITO");
				Passar();
				break;

			case 6:   // E PERTO DO FIM: outros tiros, em outros rumos
				if (_t < 1.1) return;
				FotoDoGrito(srv, mundo, "user://iaracial-2b-desabafo.png", "PERTO DO FIM DO GRITO");
				srv.DesmontarCenaDaIaRacialDeTeste(_cena!.Value);
				_cena = srv.MontarCenaDaIaRacialDeTeste(eu, "demonico");
				if (_cena == null) { Conferir(false, "(palco) o Lendario Primal nasce pra cena do Demonic"); Fechar(); return; }
				Passar();
				break;

			case 7:   // O DEMONIC: espera a IA comecar a carga
				if (!srv.CarregandoODemonicoDaIaDeTeste(_cena!.Value.Npc) && _t < 6) return;
				_ondeCarregou = srv.PosicaoDeTeste(_cena.Value.Npc) is { } p ? new Vector2(p.X, p.Y) : Vector2.Zero;
				Passar();
				break;

			case 8:
				if (_t < 1.5) return;   // a esfera acende devagar
				{
					int npc = _cena!.Value.Npc;
					EsferaDemoniaca? esfera = mundo.CorpoDeTeste(npc)?.GetNodeOrNull<EsferaDemoniaca>(EsferaDemoniaca.NomeDoNode);
					bool parado = srv.PosicaoDeTeste(npc) is { } agora && new Vector2(agora.X, agora.Y).DistanceTo(_ondeCarregou) < 1;
					Conferir(srv.CarregandoODemonicoDaIaDeTeste(npc) && srv.IaApertouDeTeste(npc, Passivas.Demonico) && parado
								  && esfera is { AlfaDeTeste: > 0.5 } && esfera.IsVisibleInTree() && DesenhadoNaTela(mundo, npc),
								  $"O LENDARIO PRIMAL, com o alvo longe, CARREGA O DEMONIC SAIYAN pela IA, PARADO -- e a esfera verde esta desenhada em volta dele (alfa {esfera?.AlfaDeTeste:0.00})");
					Fotografar("user://iaracial-3-demonico.png");
				}
				srv.DesmontarCenaDaIaRacialDeTeste(_cena!.Value);
				_cena = null;
				Fechar();
				break;
		}
	}

	/// <summary>
	/// O GRITO NA TELA (o dono, 2026-10-17): o Lendario ainda gritando, PLANTADO no ponto em que comecou, a esfera verde FURIOSA desenhada em
	/// volta dele e os tiros do grito desenhados -- e a foto.
	/// </summary>
	private void FotoDoGrito(Jandirus.Server.GameServer srv, World mundo, string foto, string quando)
	{
		int npc = _cena!.Value.Npc;
		List<int> tiros = srv.TirosVivosDeTeste(npc);
		int desenhados = tiros.Count(id => mundo.TiroDesenhadoDeTeste(id) is { } t && t.IsVisibleInTree());
		EsferaDemoniaca? esfera = mundo.CorpoDeTeste(npc)?.GetNodeOrNull<EsferaDemoniaca>(EsferaDemoniaca.NomeDoNode);
		bool parado = srv.PosicaoDeTeste(npc) is { } agora && new Vector2(agora.X, agora.Y).DistanceTo(_ondeGritou) < 1;
		Conferir(srv.GritandoDeTeste(npc) && srv.IaApertouDeTeste(npc, Passivas.Transbordar) && parado && esfera is { Furiosa: true, AlfaDeTeste: > 0.5 }
						  && desenhados >= 3 && DesenhadoNaTela(mundo, npc),
						  $"{quando}: o Saiyajin Lendario DESABAFA pela IA e GRITA -- plantado, dentro da esfera verde furiosa, com os tiros saindo pra todo "
						  + $"lado ({desenhados} de {tiros.Count} tiro(s) vivos na tela)");
		Fotografar(foto);
	}

	private void Fechar()
	{
		_acabou = true;
		AtivasRaciaisNaIa.IaSemAtivasDeTeste = false;
		if (Jandirus.Server.GameServer.Instance is { } srv)
		{
			if (_cena is { } c) srv.DesmontarCenaDaIaRacialDeTeste(c);
			srv.EscutaDeTecnicasDaIa = null;
		}
		Jandirus.Server.GameServer.EscutaDeFalas = null;
		foreach (string l in _linhas) GD.Print("[diaraciais] " + l);
		GD.Print(_falhas.Count == 0 ? "[diaraciais] ===== TUDO OK =====" : $"[diaraciais] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[diaraciais]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
