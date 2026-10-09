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
///   3. (2026-10-08) "a sombra deveria ser totalmente escura vendo de fora pra dentro de uma casa" --
///      o robo para dois tiles ao sul da porta do Banco da Terra, poe um corpo la dentro e mede o
///      PIXEL do piso do Banco na foto: preto, parado e PAIRANDO (era pairando que o veu inteiro
///      sumia). O corpo de dentro e o balcao nao sao desenhados; a fachada continua clara e a sombra
///      do campo aberto continua penumbra. O contra-exemplo e o veu de antes
///      (`Visao.InteriorSemBreuDeTeste`), nas duas fotos: o piso volta a aparecer.
///      A `--diagvisao` prova a mesma regra no byte da mascara; aqui e a cor que chega na tela.
///      E, ainda pairando, a CEGUEIRA: a cortina do Solar Flare sumia com a altura pelo mesmo caminho
///      que abria a sombra. O chao que a foto anterior mostra claro tem que sair preto com ela ligada,
///      e voltar sozinho quando o prazo acaba, com o corpo parado.
///
/// COMO RODAR (janela no segundo monitor; `--vooteste` e o que da a skill de voo ao corpo da bancada):
///     Godot --path . --position 1920,0 --host --rede 7935 --vooteste --diagsombra --semfoco --raca Human --conta &lt;NOVA&gt; --nome RoboDaSombra
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
				_passo = 20;   // o Banco visto de fora; de la a bancada segue pro torneio (passo 3)
				_t = 0;
				break;
			}
			// =====================================================================
			// O BANCO VISTO DE FORA (2026-10-08) -- passos 20 a 29, e de volta pro 3
			// =====================================================================
			case 20:
				GD.Print("[diagsombra] ===== O BANCO VISTO DE FORA =====");
				_destino = Meio(PortaX, PortaY + 2);
				servidor.MoveToZone(cli.LocalId, ZoneKey.Premade("Earth"), new Vec2(_destino.X, _destino.Y));
				// CEU ABERTO: o veu do clima e uma camada de tela por cima de tudo, e a neblina poria cinza
				// em cima do preto que esta foto mede.
				servidor.EmbateDeFoto_CeuLimpo(cli.LocalId);
				Passar();
				break;
			case 21:
			{
				Vector2 eu = mundo.PosicaoLocalDeTeste ?? Vector2.Zero;
				if ((eu.DistanceTo(_destino) > 48f || veu.Mapa == null || _t < 2.5) && _t < 15) return;
				_piso = PisoDoBanco(mundo, veu);
				Conferir(eu.DistanceTo(_destino) <= 48f && _piso.Count == 30,
						 $"em {_t:0.0}s o corpo esta dois tiles ao sul da porta do Banco, e o Banco tem 30 celulas de piso sob teto ({_piso.Count})");
				// UM CORPO LA DENTRO, no meio do piso -- o que o dono nao quer que se veja de fora.
				_deDentro = servidor.ForjarCorpoDeFoto(cli.LocalId, new Vec2(-1 * T, -4 * T), "DeDentro", 1000, comEscada: false);
				// O HUD E UMA CAMADA DE TELA: fica fora das fotos que medem cor.
				hud.Visible = false;
				Passar();
				break;
			}
			case 22:
			{
				if (_t < 1.2) return;
				Image? foto = Fotografar("sombra-3-banco-de-fora");
				if (foto == null || _piso.Count == 0)
				{
					Conferir(false, "a bancada do Banco precisa de JANELA (pra medir a cor) e do piso do Banco no mapa");
					_passo = 29;
					_t = 0;
					return;
				}

				int piso = _piso.Max(c => Brilho(foto, c.X, c.Y));
				Conferir(piso <= PretoAte, $"de FORA, parado: o piso do Banco e PRETO na tela (o maior canal das 30 celulas e {piso} de 255; preto e ate {PretoAte})");

				int fachada = 0;
				for (int cx = BancoX0; cx <= BancoX1; cx++)
					if (veu.ParedeIluminada(cx, PortaY)) fachada = Math.Max(fachada, Brilho(foto, cx, PortaY));
				Conferir(fachada > ClaroDesde, $"...a fachada que da pro olho continua CLARA ({fachada} de 255)");

				_naPenumbra = CelulaNaPenumbra(mundo, veu, foto);
				_penumbra = _naPenumbra.X < 0 ? -1 : Brilho(foto, _naPenumbra.X, _naPenumbra.Y);
				Conferir(_penumbra > PretoAte, $"...e a sombra do campo aberto continua PENUMBRA, nao preto ({_penumbra} de 255 em {_naPenumbra.X},{_naPenumbra.Y})");

				Node2D? corpo = mundo.CorpoDeTeste(_deDentro);
				Conferir(corpo != null && !corpo.Visible, $"o corpo que esta dentro do Banco NAO e desenhado (existe {corpo != null}, visivel {corpo?.Visible})");
				(int sobTeto, int escondidas) = mundo.ObrasNoBreuDeTeste;
				Conferir(sobTeto > 0 && escondidas == sobTeto, $"...nem a mobilia de la de dentro ({escondidas} de {sobTeto} construcoes sob teto escondidas)");

				Visao.InteriorSemBreuDeTeste = true;
				veu.InvalidarOComodo();
				Passar();
				break;
			}
			case 23:
			{
				if (_t < 0.8) return;
				Image? foto = Fotografar("sombra-3b-banco-de-fora-DEFEITO");
				int piso = foto == null ? -1 : _piso.Max(c => Brilho(foto, c.X, c.Y));
				Node2D? corpo = mundo.CorpoDeTeste(_deDentro);
				bool corpoVisivel = corpo != null && corpo.Visible;
				Visao.InteriorSemBreuDeTeste = false;
				veu.InvalidarOComodo();
				Conferir(piso > PretoAte && corpoVisivel,
						 $"(defeito injetado: o interior que nao se ve nao e breu) o piso do Banco volta a aparecer ({piso} de 255) e o corpo la dentro volta a ser desenhado ({corpoVisivel})");
				cli.SendHabilidade("voar");
				Passar();
				break;
			}
			case 24:
			{
				bool noAr = mundo.AlturaDeTeste >= Voo.AlturaQueAtravessa && veu.Abertura >= 0.99f;
				if (!noAr && _t < 8) return;
				// um segundo depois de chegar la em cima: o veu redesenha e a camera assenta antes da foto
				if (noAr && _noArDesde <= 0) _noArDesde = _t;
				if (noAr && _t < _noArDesde + 1.0) return;
				Conferir(noAr, $"o corpo esta PAIRANDO acima do cenario ({mundo.AlturaDeTeste:0} px; o cenario passa por baixo a partir de {Voo.AlturaQueAtravessa:0}) e o veu abriu (abertura {veu.Abertura:0.00})");
				Image? foto = Fotografar("sombra-4-banco-pairando");
				if (foto != null)
				{
					int piso = _piso.Max(c => Brilho(foto, c.X, c.Y));
					Conferir(piso <= PretoAte, $"de FORA, PAIRANDO: o piso do Banco continua PRETO na tela ({piso} de 255; preto e ate {PretoAte})");
					int aberta = _naPenumbra.X < 0 ? -1 : Brilho(foto, _naPenumbra.X, _naPenumbra.Y);
					Conferir(aberta > _penumbra + 20, $"...e a sombra do campo aberto ABRIU com a altura, como sempre abriu ({_penumbra} -> {aberta} de 255)");
					_chaoClaro = Brilho(foto, PortaX, PortaY + 4);
				}
				// A CEGUEIRA, PAIRANDO. A cortina do Solar Flare e deste mesmo veu, e sumia com a altura pelo mesmo
				// `Modulate` que abria a sombra: quem pairava nao ficava cego. O controle e a foto de cima -- o chao a
				// vista, claro, com o corpo no ar -- e a medida e a mesma celula com a cegueira ligada.
				veu.CegoAte = Time.GetTicksMsec() + MsDeCegueira;
				Passar();
				break;
			}
			case 25:
			{
				if (_t < 0.3) return;
				Image? foto = Fotografar("sombra-5-cego-pairando");
				int cego = foto == null ? -1 : Brilho(foto, PortaX, PortaY + 4);
				Conferir(_chaoClaro > ClaroDesde && cego >= 0 && cego <= PretoAte,
						 $"CEGO e pairando, a tela e PRETA: o chao que estava claro ({_chaoClaro} de 255) saiu {cego} -- a altura nao abre a cortina da cegueira");
				Passar();
				break;
			}
			case 26:
			{
				// passado o prazo, SEM o corpo se mexer: a cortina tem que sair sozinha
				if (_t < MsDeCegueira / 1000.0 + 0.4) return;
				Image? foto = Fotografar("sombra-5b-depois-da-cegueira");
				int depois = foto == null ? -1 : Brilho(foto, PortaX, PortaY + 4);
				Conferir(depois > ClaroDesde, $"...e acabado o prazo a tela VOLTA com o corpo parado no ar ({depois} de 255)");
				// o contra-exemplo: a mesma cegueira, sem ninguem mandar redesenhar no fim dela
				Visao.CortinaDaCegueiraFicaDeTeste = true;
				veu.CegoAte = Time.GetTicksMsec() + MsDeCegueira;
				Passar();
				break;
			}
			case 27:
			{
				if (_t < MsDeCegueira / 1000.0 + 0.7) return;
				Image? foto = Fotografar("sombra-5c-depois-da-cegueira-DEFEITO");
				int presa = foto == null ? -1 : Brilho(foto, PortaX, PortaY + 4);
				Visao.CortinaDaCegueiraFicaDeTeste = false;
				veu.Invalidar();
				Conferir(presa >= 0 && presa <= PretoAte,
						 $"(defeito injetado: acabada a cegueira ninguem manda redesenhar) a tela continua PRETA com o corpo parado ({presa} de 255)");
				Visao.InteriorSemBreuDeTeste = true;
				veu.InvalidarOComodo();
				Passar();
				break;
			}
			case 28:
			{
				if (_t < 0.8) return;
				Image? foto = Fotografar("sombra-4b-banco-pairando-DEFEITO");
				int piso = foto == null ? -1 : _piso.Max(c => Brilho(foto, c.X, c.Y));
				Visao.InteriorSemBreuDeTeste = false;
				veu.InvalidarOComodo();
				Conferir(piso > ClaroDesde, $"(defeito injetado: o interior que nao se ve nao e breu) PAIRANDO se ve o piso do Banco inteiro ({piso} de 255) -- era o que bastava pra olhar dentro de qualquer casa");
				cli.SendHabilidade("voar");   // o mesmo botao, desligando
				Passar();
				break;
			}
			case 29:
				if (mundo.AlturaDeTeste > 0.01f && _t < 8) return;
				hud.Visible = true;
				servidor.LimparAFoto();
				GD.Print("[diagsombra] ===== O ESPECTADOR DO TORNEIO =====");
				// O TORNEIO DA TERRA CONVIDA QUEM ESTA NA TERRA (e vivo): o corpo volta pra arena antes.
				servidor.MoveToZone(cli.LocalId, ZoneKey.Premade("Earth"), new Vec2(Arena.X, Arena.Y));
				_passo = 3;
				_t = 0;
				break;
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

	private Image? Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota("sem foto (headless nao renderiza): rode com janela"); return null; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		Nota($"foto {caminho} ({img.GetWidth()}x{img.GetHeight()})");
		return img;
	}

	// =====================================================================
	// O BANCO DA TERRA, e a cor dele na foto
	// =====================================================================
	private const int T = ZoneCollision.TileSize;

	/// <summary>O retangulo do Banco e a porta dele, em celulas -- os mesmos da `--diagvisao`.</summary>
	private const int BancoX0 = 67, BancoX1 = 78, BancoY0 = 257, BancoY1 = 262, PortaX = 73, PortaY = 262;

	/// <summary>
	/// "PRETO" NA FOTO: o maior dos tres canais, de 0 a 255. Nao e zero porque a nevoa de altitude e
	/// uma camada de tela por cima de tudo (uns poucos pontos quando se paira) -- e esta longe do que
	/// o veu de antes deixava passar, que e o que o defeito injetado mostra na mesma bancada.
	/// </summary>
	private const int PretoAte = 12;

	/// <summary>"CLARO" na foto: a parede iluminada, o piso a vista.</summary>
	private const int ClaroDesde = 40;

	private List<(int X, int Y)> _piso = [];
	private (int X, int Y) _naPenumbra = (-1, -1);
	private int _penumbra, _deDentro, _chaoClaro;
	private double _noArDesde;

	/// <summary>Quanto dura a cegueira que a bancada liga (o mesmo campo que o efeito do Solar Flare escreve).</summary>
	private const ulong MsDeCegueira = 900;

	private static Vector2 Meio(int cx, int cy) => new((cx + 0.5f) * T, (cy + 0.5f) * T);

	/// <summary>O piso do Banco: sob teto e sem cegar -- contado no mapa que o cliente carregou.</summary>
	private static List<(int X, int Y)> PisoDoBanco(World mundo, Visao veu)
	{
		var piso = new List<(int X, int Y)>();
		if (veu.Mapa is not { } vis) return piso;
		for (int cy = BancoY0; cy <= BancoY1; cy++)
			for (int cx = BancoX0; cx <= BancoX1; cx++)
				if (mundo.CelulaSobTeto(cx, cy) && !vis.BlockedCell(cx, cy)) piso.Add((cx, cy));
		return piso;
	}

	/// <summary>
	/// A COR DE UMA CELULA NA FOTO: o maior canal entre nove amostras dela (a 1/4, 1/2 e 3/4 de cada
	/// lado), de 0 a 255; -1 se a celula esta fora da foto. Nove e nao uma porque a pergunta e "ha
	/// ALGUMA coisa aparecendo aqui?", e um pixel so pode cair numa junta escura do piso.
	///
	/// A foto sai no tamanho da JANELA, e no modo `canvas_items` do projeto o caminho do mundo ate o
	/// pixel passa pelas duas transformacoes do viewport -- a mesma conta da foto do embate.
	/// </summary>
	private int Brilho(Image foto, int cx, int cy)
	{
		Viewport? v = GetViewport();
		if (v == null) return -1;
		int maior = -1;
		for (int iy = 1; iy <= 3; iy++)
			for (int ix = 1; ix <= 3; ix++)
			{
				var noMundo = new Vector2((cx + ix / 4f) * T, (cy + iy / 4f) * T);
				Vector2 p = v.GetFinalTransform() * (v.CanvasTransform * noMundo);
				int x = (int)p.X, y = (int)p.Y;
				if (x < 0 || y < 0 || x >= foto.GetWidth() || y >= foto.GetHeight()) continue;
				Color c = foto.GetPixel(x, y);
				maior = Math.Max(maior, (int)MathF.Round(MathF.Max(c.R, MathF.Max(c.G, c.B)) * 255f));
			}
		return maior;
	}

	/// <summary>
	/// UMA CELULA DE CHAO NA SOMBRA COMUM, dentro da foto: ao ar livre, sem cegar, fora do leque -- e
	/// com as oito vizinhas no mesmo estado, pra nenhuma das nove amostras cair na borda da sombra.
	/// </summary>
	private (int X, int Y) CelulaNaPenumbra(World mundo, Visao veu, Image foto)
	{
		if (veu.Mapa is not { } vis) return (-1, -1);
		Rect2 tela = veu.TelaDeTeste;
		int cx0 = (int)MathF.Floor(tela.Position.X / T), cy0 = (int)MathF.Floor(tela.Position.Y / T);
		int cx1 = (int)MathF.Floor(tela.End.X / T), cy1 = (int)MathF.Floor(tela.End.Y / T);
		for (int cy = cy0 + 1; cy < cy1; cy++)
			for (int cx = cx0 + 1; cx < cx1; cx++)
			{
				bool serve = Brilho(foto, cx, cy) >= 0;
				for (int dy = -1; dy <= 1 && serve; dy++)
					for (int dx = -1; dx <= 1 && serve; dx++)
						serve = !mundo.CelulaSobTeto(cx + dx, cy + dy) && !vis.BlockedCell(cx + dx, cy + dy)
								&& !veu.Ve(Meio(cx + dx, cy + dy));
				if (serve) return (cx, cy);
			}
		return (-1, -1);
	}

	private void Fechar()
	{
		_acabou = true;
		Visao.InteriorSemBreuDeTeste = false;   // os defeitos nao sobrevivem a uma bancada que parou no meio
		Visao.CortinaDaCegueiraFicaDeTeste = false;
		foreach (string p in _passos) GD.Print("[diagsombra] " + p);
		GD.Print(_falhas.Count == 0 ? "[diagsombra] ===== TUDO OK =====" : $"[diagsombra] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[diagsombra]   - " + f);
		GD.Print($"[diagsombra] fim: {_passos.Count(p => p.StartsWith("  ok"))} ok, {_falhas.Count} falha(s)");
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
