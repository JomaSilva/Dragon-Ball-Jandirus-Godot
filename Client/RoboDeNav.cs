using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA CARTA ESTELAR (`--diagnav`).
///
/// ============================ O QUE SO UM TESTE RESPONDE ============================
/// Um mapa desenhado nao devolve nada: o `_Draw` pinta e acaba. Sem janela nao ha o que olhar, e
/// "o mapa mostra os planetas" continuaria sendo uma afirmacao minha ate alguem abrir o jogo.
///
/// As perguntas que importam e que so um numero responde:
///   * a aba Nav existe FORA do espaco? (ela passou a existir sempre, e isso e uma mudanca de regra)
///   * o cliente consegue enumerar planetas SOZINHO, sem pacote nenhum?
///   * os procedurais aparecem quando se aproxima -- e somem quando se afasta?
///   * quanto custa a varredura, de verdade, em milissegundos?
///   * clicar seleciona? viajar liga o piloto?
/// ====================================================================================
///
/// COMO RODAR:
///     Godot --headless --path . --host --diagnav --nome Piloto --conta piloto
/// </summary>
public partial class RoboDeNav : Node
{
	private double _t;
	private int _passo;
	private bool _acabou;

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private MapaEstelar? _mapa;
	private int _longe, _perto;
	private double _msDeVarredura;

	/// <summary>A camera antes de o tempo passar -- o teste de que a aba nao se remonta sozinha.</summary>
	private float _escalaAntes;
	private Vector2 _centroAntes;
	private MapaEstelar? _mapaAntes;

	private static GameClient? C => GameClient.Instance;

	/// <summary>
	/// Salva a tela em `user://mapa-estelar.png`, se houver renderizador.
	///
	/// Silenciosa no headless de proposito: la o `GetImage` devolve nada e isso e ESPERADO, nao um
	/// defeito. Falhar o teste por causa disso seria falhar por causa do modo de execucao.
	/// </summary>
	private void Fotografar()
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { _passos.Add("  --     sem foto (headless nao renderiza)"); return; }
			string caminho = ProjectSettings.GlobalizePath("user://mapa-estelar.png");
			img.SavePng(caminho);
			_passos.Add("  ok     foto do mapa salva em " + caminho);
		}
		catch (Exception e) { _passos.Add("  --     sem foto: " + e.Message); }
	}

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || MenuJogo.Instancia is not { } menu) return;

		_t += delta;
		if (_t < 0.5) return;
		_t = 0;

		switch (_passo++)
		{
			case 0:
				if (cli.Atributos.Raca is not { Length: > 0 }) { _passo = 0; return; }
				// A SEED CHEGA NO LOGIN, e nao so ao decolar -- e ela que faz a carta funcionar em
				// terra firme. Se voltar a chegar so no espaco, este passo cai.
				Conferir(cli.SeedDoUniverso != 0, $"a seed do universo chega no login ({cli.SeedDoUniverso})");
				Conferir(!Espaco.EhEspaco(cli.Zone), "estou em TERRA FIRME (e onde a aba nao existia antes)");
				break;

			case 1:
			{
				Conferir(Array.IndexOf(menu.AbasDeTeste, "Nav") >= 0,
					"a aba Nav existe fora do espaco (a carta se consulta antes de decolar)");

				// EM TERRA, A POSICAO DE GALAXIA E A DO PLANETA -- e nao a do corpo. O spawn da
				// Terra e (7984, 8016): usar isso no mapa punha o jogador a 11 mil px da origem,
				// fora do raio 220 da propria Terra, com "voce esta aqui" apontando pro vazio.
				Vector2? g = MapaEstelar.MinhaPosicaoNaGalaxia();
				Vector2? corpo = World.Instancia?.PosicaoLocal;
				Conferir(g is { } gg && gg.Length() < 1f,
					$"em terra, a posicao de galaxia e a do planeta ({g}), e nao a do corpo ({corpo})");
				break;
			}

			case 2:
				menu.Abrir();
				menu.IrPara("Nav");
				_mapa = menu.MapaDeTeste;
				Conferir(_mapa != null, "a aba Nav monta o mapa sem quebrar");
				break;

			case 3:
			{
				if (_mapa == null) { Conferir(false, "sem mapa: o resto nao da pra medir"); _passo = 99; break; }
				// LONGE: so o esqueleto (os sete com mapa proprio).
				_mapa.VerTudo();
				_longe = _mapa.PlanetasDeTeste().Count;
				Conferir(_longe >= 7, $"no zoom aberto aparecem os mundos pre-feitos ({_longe})");
				Conferir(!_mapa.VendoProcedurais, "no zoom aberto a varredura procedural fica DESLIGADA");
				break;
			}

			case 4:
			{
				if (_mapa == null) break;
				// PERTO: aproxima da Terra e conta de novo. O cliente faz isso SOZINHO -- nenhum
				// pacote de rede foi pedido entre um passo e outro.
				_mapa.IrPara(Vector2.Zero);
				for (int i = 0; i < 40 && !_mapa.VendoProcedurais; i++) _mapa.Zoom(1.25f);

				// A PRIMEIRA PASSADA ESQUENTA O CACHE e nao entra na conta: ela paga a varredura
				// inteira, e isso acontece uma vez. O que roda a 60 Hz e a SEGUNDA -- e e ela que
				// precisa caber num quadro. Medir as duas juntas seria reportar o pior caso como
				// se fosse o normal.
				_mapa.PlanetasDeTeste();
				ulong t0 = Time.GetTicksUsec();
				_perto = _mapa.PlanetasDeTeste().Count;
				_msDeVarredura = (Time.GetTicksUsec() - t0) / 1000.0;

				Conferir(_mapa.VendoProcedurais, $"aproximando, a varredura liga (escala {_mapa.EscalaDeTeste:0.0000})");
				Conferir(_perto > _longe,
					$"o cliente enumera planetas sozinho: {_longe} longe -> {_perto} perto, sem um byte de rede");
				Conferir(_msDeVarredura < 8, $"a varredura cabe num quadro ({_msDeVarredura:0.00} ms)");
				break;
			}

			case 5:
			{
				if (_mapa == null) break;
				// CLICAR: pega um planeta que esta na tela e clica onde ele esta desenhado.
				List<PlanetaNoEspaco> lista = _mapa.PlanetasDeTeste();
				PlanetaNoEspaco alvo = lista.Find(p => p.Premade && p.Nome == "Earth");
				Conferir(_mapa.ClicarEm(alvo), $"clicar no desenho de {alvo.Nome} seleciona ele");
				Conferir(_mapa.Selecionado?.Nome == alvo.Nome, "a selecao e a que foi clicada");
				break;
			}

			case 6:
			{
				// VIAJAR EM TERRA NAO LIGA O PILOTO. E a regra que o botao apagado promete.
				Conferir(World.Instancia?.DestinoDoPiloto == null,
					"em terra firme o piloto NAO liga (viajar e coisa do espaco)");

				// A CAMERA SOBREVIVE A ANDAR. A assinatura da aba carregava a direcao do corpo, que
				// e reescrita a cada pacote de input -- virar pra esquerda com o menu aberto
				// remontava a pagina e criava um mapa novo, jogando fora o zoom e o arrasto.
				_mapa!.IrPara(new Vector2(12345, -6789));
				_mapa.Zoom(1.6f);
				_escalaAntes = _mapa.EscalaDeTeste;
				_centroAntes = _mapa.CentroDeTeste;
				_mapaAntes = _mapa;
				break;
			}

			case 7:
				// Um quadro depois (varios pacotes de ficha ja chegaram, e o robo "andou"): a camera
				// tem que estar onde eu deixei, e tem que ser o MESMO no.
				Conferir(ReferenceEquals(menu.MapaDeTeste, _mapaAntes),
					"o mapa nao foi recriado por um pacote de ficha");
				Conferir(_mapa!.EscalaDeTeste == _escalaAntes && _mapa.CentroDeTeste == _centroAntes,
					"o zoom e o arrasto sobrevivem aos pacotes de ficha");

				// Sobe pro espaco pra provar o outro lado.
				C?.SendHabilidade("decolar");   // decolar e HABILIDADE, nao verb (GameServer.Raciais.cs:157)
				break;

			case 8:
				Conferir(Espaco.EhEspaco(cli.Zone), "decolei: estou no espaco");
				break;

			case 9:
			{
				if (_mapa == null) break;
				// O mapa foi remontado junto com a aba (a zona mudou, e a assinatura leva a zona).
				menu.IrPara("Nav");
				_mapa = menu.MapaDeTeste;
				if (_mapa == null) { Conferir(false, "o mapa sumiu depois de decolar"); break; }
				_mapa.VerMim();
				List<PlanetaNoEspaco> lista = _mapa.PlanetasDeTeste();
				PlanetaNoEspaco alvo = lista.Find(p => p.Nome == "Namek");
				_mapa.ClicarEm(alvo);
				World.Instancia?.Pilotar(new Vector2(alvo.Pos.X, alvo.Pos.Y));
				break;
			}

			case 10:
				Conferir(World.Instancia?.DestinoDoPiloto != null,
					"no espaco, viajar LIGA o piloto automatico");
				World.Instancia?.SoltarPiloto();
				Conferir(World.Instancia?.DestinoDoPiloto == null, "o botao Parar desliga o piloto");
				break;

			case 11:
				// UMA FOTO DO MAPA, quando ha renderizador de verdade.
				//
				// `--headless` usa o renderizador de mentira: `GetImage()` volta vazio ou nulo, e
				// por isso a foto e um EXTRA e nao um passo que pode falhar. Rodando com janela
				// (`--diagnav` sem `--headless`), ela e a unica prova visual que um teste consegue
				// produzir de um widget que so existe enquanto desenha.
				_mapa?.VerTudo();
				break;

			case 12:
				Fotografar();
				break;

			default:
				_acabou = true;
				menu.Fechar();
				GD.Print("\n[nav] ===== BANCADA DA CARTA ESTELAR =====");
				foreach (string l in _passos) GD.Print("[nav] " + l);
				GD.Print($"[nav]   planetas: {_longe} no zoom aberto, {_perto} aproximado "
						 + $"| varredura {_msDeVarredura:0.00} ms");
				GD.Print(_falhas.Count == 0
					? "[nav] ===== TUDO OK ====="
					: $"[nav] ===== {_falhas.Count} FALHA(S) =====\n[nav]   " + string.Join("\n[nav]   ", _falhas));
				break;
		}
	}
}
