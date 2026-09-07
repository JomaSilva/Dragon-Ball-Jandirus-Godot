using Godot;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA SOMBRA NA TELA (`--diagsombra`). Roda com `--host` e JANELA: o `--diagvisao` prova a
/// geometria em numero, mas as duas queixas do dono de 2026-09-07 eram coisas VISTAS:
///
///   1. "sombra que sai do meio da parede / sombra que fica sobre tile e outras nao" -- a foto de um
///      portao num muro comprido, com mato atras. O robo vai ate um portao assim (o da Terra em
///      (248,122), o mesmo que o `--diagvisao` acha sozinho; `--sombralugar Zona,cx,cy` troca), tira
///      a foto e mede o que o veu esta desenhando ali: toda parada de raio numa fronteira
///      livre/cega, nenhuma parede escura com ponto interno visivel (a cunha), o leque sem dobra, a
///      fileira da frente clara;
///
///   2. "fica uma sombra na tela quando vai assistir o torneio" e "os npcs estao realmente
///      invisiveis" -- o robo abre um torneio de verdade, se inscreve, adianta ate a primeira luta
///      (verbos de admin), liga o Assistir e confere que o olho do veu foi com a camera (o
///      `EYE_PERSPECTIVE`), que o leque nao dobrou e que os DOIS lutadores estao desenhados, claros
///      e dentro da tela. E prova o contra-exemplo: com o olho de antes (o corpo na area de espera)
///      e a tela da camera, o leque dobra -- e o que o dono viu.
///
/// COMO RODAR (janela no segundo monitor):
///     Godot --path . --position 1920,0 --host --rede 7935 --diagsombra --semfoco --raca Human --conta &lt;NOVA&gt; --nome RoboDaSombra
/// </summary>
public partial class RoboDaSombra : Node
{
	/// <summary>`Zona,cx,cy` do portao. O padrao e o portao da Terra que o `--diagvisao` tambem usa.</summary>
	public string Lugar = "Earth,248,122";

	/// <summary>O centro da arena do torneio da Terra (`TorneioConfig.Terra.Arena`): pra onde o corpo volta antes do torneio.</summary>
	private static readonly Vector2 Arena = new(247.5f * ZoneCollision.TileSize, 460.5f * ZoneCollision.TileSize);

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];
	private bool _acabou;
	private int _passo;
	private double _t, _espera, _ultimoAvanco;
	private const double EsperaMaxima = 30.0;
	private Vector2 _destino;
	private string _zona = "Earth";

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string s) => _passos.Add("  --     " + s);
	private void Passar() { _passo++; _t = 0; }

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (GameClient.Instance is not { Connected: true } cli
		 || Hud.Instancia is not { } hud
		 || World.Instancia is not { } mundo
		 || GameServer.Instance is not { } servidor)
		{
			_espera += delta;
			if (_espera > EsperaMaxima)
			{
				Conferir(false, $"em {EsperaMaxima:0}s o cliente conectou, o HUD e o mundo nasceram e o servidor esta neste processo");
				Fechar();
			}
			return;
		}
		_espera = 0;
		_t += delta;
		Visao veu = mundo.VeuDeTeste;

		switch (_passo)
		{
			case 0:
			{
				GD.Print("[diagsombra] ===== O PORTAO DA FOTO =====");
				string[] p = Lugar.Split(',');
				if (p.Length != 3 || !int.TryParse(p[1], out int cx) || !int.TryParse(p[2], out int cy))
				{ Conferir(false, $"--sombralugar `{Lugar}` nao e Zona,cx,cy"); Fechar(); return; }
				_zona = p[0];
				_destino = new Vector2((cx + 0.5f) * ZoneCollision.TileSize, (cy + 0.5f) * ZoneCollision.TileSize);
				// ZOOM 2, como o print do dono: em zoom 3 a tela tem 7 linhas de tile e o muro a quatro
				// linhas do corpo fica FORA dela -- a primeira foto desta bancada saiu so areia.
				mundo.AplicarZoom(2);
				// MEIO-DIA: de madrugada a tela inteira e escura e a foto nao separa sombra de noite.
				cli.SendVerbo("admin_meio_dia");
				servidor.MoveToZone(cli.LocalId, ZoneKey.Premade(_zona), new Vec2(_destino.X, _destino.Y));
				Passar();
				break;
			}
			case 1:
				// CHEGOU? O servidor mandou a zona e o corpo; o veu precisa do mapa dela e a cena de um
				// quadro pra pintar os pedacos antes da foto.
				if (((mundo.PosicaoLocalDeTeste ?? Vector2.Zero).DistanceTo(_destino) > 48f || veu.Mapa == null || _t < 2.5) && _t < 15) return;
				Conferir((mundo.PosicaoLocalDeTeste ?? Vector2.Zero).DistanceTo(_destino) <= 48f && veu.Mapa != null,
						 $"em {_t:0.0}s o corpo esta no portao de {_zona} ({(mundo.PosicaoLocalDeTeste ?? Vector2.Zero)}) e o veu tem o mapa da zona");
				Passar();
				break;
			case 2:
			{
				if (_t < 0.5) return;
				Fotografar("sombra-1-portao");
				(int paredes, int claras, int cunhas, int facesErradas) = MedirATela(veu);
				Nota($"na tela: {paredes} paredes, {claras} claras, {veu.QuantosRaios} raios, maior setor {veu.MaiorSetorGraus:0.0} graus");
				Conferir(veu.ParadasForaDeFace == 0, $"toda parada de raio e uma fronteira livre/cega ({veu.ParadasForaDeFace} fora de face)");
				Conferir(cunhas == 0, $"nenhuma parede escura tem ponto interno visivel -- sem cunha saindo do meio do muro ({cunhas})");
				Conferir(!veu.Dobrado, $"o leque nao dobra (maior setor {veu.MaiorSetorGraus:0.0} graus)");
				Conferir(claras > 0, $"ha parede clara na tela ({claras} de {paredes})");
				Conferir(facesErradas == 0, $"o veu e um raio direto concordam sobre que parede esta clara ({facesErradas} faces erradas)");
				Conferir(veu.OlhoEmprestado == null && veu.OlhoDeTeste.DistanceTo((mundo.PosicaoLocalDeTeste ?? Vector2.Zero) + new Vector2(0, MoveRules.FeetOffsetY)) < 1f,
						 "sem espectador o olho do veu sao os pes do corpo");
				GD.Print("[diagsombra] ===== O ESPECTADOR DO TORNEIO =====");
				// O TORNEIO DA TERRA CONVIDA QUEM ESTA NA TERRA (e vivo): o corpo volta pra arena antes.
				servidor.MoveToZone(cli.LocalId, ZoneKey.Premade("Earth"), new Vec2(Arena.X, Arena.Y));
				Passar();
				break;
			}
			case 3:
				if ((mundo.PosicaoLocalDeTeste ?? Vector2.Zero).DistanceTo(Arena) > 48f && _t < 15) return;
				Conferir((mundo.PosicaoLocalDeTeste ?? Vector2.Zero).DistanceTo(Arena) <= 48f, $"em {_t:0.0}s o corpo esta na arena da Terra");
				cli.SendVerbo("trn_iniciar", "terra");
				Passar();
				break;
			case 4:
			{
				PainelDoTorneio? convite = hud.FindChild("Torneio", true, false) as PainelDoTorneio;
				if (convite?.VisivelDeTeste != true && _t < 5) return;
				Conferir(convite?.VisivelDeTeste == true, "o convite do torneio chegou");
				convite?.ClicarParticiparDeTeste();
				Passar();
				break;
			}
			case 5:
				if (servidor.InscritosDeTeste == 0 && _t < 5) return;
				Conferir(servidor.InscritosDeTeste == 1, "o host esta inscrito");
				cli.SendVerbo("trn_avancar");
				_ultimoAvanco = 0;
				Passar();
				break;
			case 6:
				// ATE A PRIMEIRA LUTA: o admin adianta cada fase (inscricao -> preparo -> contagem -> luta).
				if (servidor.FaseDoTorneioDeTeste != "Luta")
				{
					if (_t - _ultimoAvanco > 1.0) { cli.SendVerbo("trn_avancar"); _ultimoAvanco = _t; }
					if (_t < 25) return;
				}
				Conferir(servidor.FaseDoTorneioDeTeste == "Luta", $"em {_t:0.0}s a primeira luta comecou (fase {servidor.FaseDoTorneioDeTeste})");
				Conferir(mundo.PorQueOCorpoNaoAnda == "na area de espera do torneio", $"o corpo local espera a vez dele (\"{mundo.PorQueOCorpoNaoAnda}\")");
				// A ZONA CHEIA CHEGA INTEIRA NO CLIENTE (dono: "os npcs estao realmente invisiveis"): com os
				// 31 NPCs o snapshot da Terra nao cabe num pacote e sai em partes -- ver `GameServer.Snapshot.cs`.
				(int partes, int maiorParte, int orcamento) = servidor.UltimoSnapshotDeTeste;
				Conferir(partes >= 2 && maiorParte <= orcamento, $"o snapshot da Terra sai em {partes} partes de ate {maiorParte} bytes (orcamento {orcamento}) -- antes era UM pacote grande demais, e nunca saia");
				Conferir(mundo.CorposRemotosDeTeste >= 32, $"o cliente tem os corpos da zona inteira ({mundo.CorposRemotosDeTeste} corpos remotos, 31 deles os NPCs do torneio)");
				Passar();
				break;
			case 7:
			{
				if (_t < 0.5) return;
				// A CHAVE ABRE SOZINHA e taparia a foto: fecha. O canto de assistir fica.
				if (hud.FindChild("Chave", true, false) is PainelDaChave chave && chave.VisivelDeTeste) hud.AlternarChave();
				PainelDeAssistir? assistir = hud.FindChild("Assistir", true, false) as PainelDeAssistir;
				Conferir(assistir?.VisivelDeTeste == true, "o canto 'Assistir torneio' esta na tela");
				assistir?.ClicarAssistirDeTeste();
				Conferir(mundo.AssistindoDeTeste, "o botao liga a camera do espectador");
				Passar();
				break;
			}
			case 8:
			{
				if (_t < 2.0) return;
				Fotografar("sombra-2-assistindo");
				Vector2 camera = mundo.CentroDaCameraDeTeste;
				Vector2 pes = (mundo.PosicaoLocalDeTeste ?? Vector2.Zero) + new Vector2(0, MoveRules.FeetOffsetY);
				Nota($"camera em {camera}, corpo em {pes} (deslocamento {mundo.DeslocamentoDaCameraDeTeste.Length():0} px)");
				Conferir(mundo.DeslocamentoDaCameraDeTeste.Length() > 20f, "a camera saiu de cima do corpo e foi pra arena");
				Conferir(veu.OlhoEmprestado != null && veu.OlhoDeTeste.DistanceTo(camera) < 2f,
						 $"o olho do veu FOI COM A CAMERA (olho {veu.OlhoDeTeste}, camera {camera}) -- o EYE_PERSPECTIVE do DM");
				Conferir(veu.TelaDeTeste.HasPoint(veu.OlhoDeTeste), "...e esta dentro da tela calculada");
				Conferir(!veu.Dobrado, $"o leque do espectador nao dobra (maior setor {veu.MaiorSetorGraus:0.0} graus)");
				Conferir(veu.ParadasForaDeFace == 0, $"toda parada e fronteira livre/cega tambem daqui ({veu.ParadasForaDeFace})");

				(int a, int b) = servidor.LutadoresDeTeste;
				foreach ((int id, string rotulo) in new[] { (a, "A"), (b, "B") })
				{
					Node2D? corpo = mundo.CorpoDeTeste(id);
					bool desenhado = corpo != null && corpo.Visible && corpo.IsVisibleInTree();
					bool naTela = corpo != null && veu.TelaDeTeste.HasPoint(corpo.GlobalPosition);
					bool claro = corpo != null && veu.Ve(corpo.GlobalPosition);
					Conferir(desenhado && naTela && claro,
							 $"o lutador {rotulo} (id {id}) esta DESENHADO, na tela e fora da sombra"
							 + (corpo == null ? " -- o corpo nem existe no cliente" : $" (visivel {desenhado}, na tela {naTela}, claro {claro}, em {corpo.GlobalPosition})"));
				}

				// O CONTRA-EXEMPLO: o olho de ANTES (o corpo na area de espera) com a tela da camera.
				Visao.TelaSemOlhoDeTeste = true;
				var telaDaCamera = new Rect2(camera - veu.TelaDeTeste.Size / 2f, veu.TelaDeTeste.Size);
				veu.Recalcular(pes, telaDaCamera);
				bool dobrava = veu.Dobrado;
				Visao.TelaSemOlhoDeTeste = false;
				veu.Invalidar();
				Conferir(dobrava || telaDaCamera.HasPoint(pes),
						 $"CONTRA-EXEMPLO: com o olho no corpo e a tela na arena o leque DOBRAVA (maior setor {veu.MaiorSetorGraus:0.0} graus) -- a 'sombra na tela' do dono");
				Passar();
				break;
			}
			case 9:
			{
				PainelDeAssistir? assistir = hud.FindChild("Assistir", true, false) as PainelDeAssistir;
				assistir?.ClicarAssistirDeTeste();
				Conferir(!mundo.AssistindoDeTeste, "apertar de novo desliga a camera do espectador");
				Passar();
				break;
			}
			case 10:
				if (_t < 2.0) return;
				Conferir(veu.OlhoEmprestado == null && veu.OlhoDeTeste.DistanceTo((mundo.PosicaoLocalDeTeste ?? Vector2.Zero) + new Vector2(0, MoveRules.FeetOffsetY)) < 1f,
						 "...e o olho do veu voltou pros pes do corpo");
				cli.SendVerbo("trn_cancelar");
				Passar();
				break;
			case 11:
				if (servidor.TorneioAtivo && _t < 5) return;
				Conferir(!servidor.TorneioAtivo, "o torneio foi cancelado");
				Fechar();
				break;
		}
	}

	/// <summary>
	/// O QUE O VEU ESTA DESENHANDO NA TELA, por parede: quantas, quantas claras, quantas escuras com
	/// ponto interno visivel (a cunha) e em quantas o furo discorda de um raio direto ate as mesmas
	/// amostras de face (a verdade da bancada sem janela, `VisaoDiag.FaceVistaPorRaio`).
	/// </summary>
	private static (int Paredes, int Claras, int Cunhas, int FacesErradas) MedirATela(Visao veu)
	{
		const int T = ZoneCollision.TileSize;
		ZoneCollision m = veu.Mapa!;
		Rect2 tela = veu.TelaDeTeste;
		int cx0 = (int)MathF.Floor(tela.Position.X / T), cy0 = (int)MathF.Floor(tela.Position.Y / T);
		int cx1 = (int)MathF.Floor(tela.End.X / T), cy1 = (int)MathF.Floor(tela.End.Y / T);
		int paredes = 0, claras = 0, cunhas = 0, facesErradas = 0;
		for (int cy = cy0; cy <= cy1; cy++)
			for (int cx = cx0; cx <= cx1; cx++)
			{
				if (!m.BlockedCell(cx, cy)) continue;
				paredes++;
				bool clara = veu.ParedeIluminada(cx, cy);
				// o anel de fora fica de fora da comparacao -- ver a mesma nota em `VisaoDiag.Medir`
				bool noAnel = cx == cx0 || cx == cx1 || cy == cy0 || cy == cy1;
				if (!noAnel && clara != VisaoDiag.FaceVistaPorRaio(m, veu, veu.OlhoDeTeste, cx, cy)) facesErradas++;
				if (clara) { claras++; continue; }
				float x0 = cx * T, y0 = cy * T;
				if (veu.Ve(new Vector2(x0 + T / 4f, y0 + T / 4f)) || veu.Ve(new Vector2(x0 + 3 * T / 4f, y0 + T / 4f))
				 || veu.Ve(new Vector2(x0 + T / 4f, y0 + 3 * T / 4f)) || veu.Ve(new Vector2(x0 + 3 * T / 4f, y0 + 3 * T / 4f))) cunhas++;
			}
		return (paredes, claras, cunhas, facesErradas);
	}

	private void Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota("sem foto (headless nao renderiza): rode com janela"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		Nota($"foto {caminho} ({img.GetWidth()}x{img.GetHeight()})");
	}

	private void Fechar()
	{
		_acabou = true;
		foreach (string p in _passos) GD.Print("[diagsombra] " + p);
		GD.Print(_falhas.Count == 0 ? "[diagsombra] ===== TUDO OK =====" : $"[diagsombra] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[diagsombra]   - " + f);
		GD.Print($"[diagsombra] fim: {_passos.Count(p => p.StartsWith("  ok"))} ok, {_falhas.Count} falha(s)");
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
