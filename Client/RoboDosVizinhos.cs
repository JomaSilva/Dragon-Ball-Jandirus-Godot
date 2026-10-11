using Godot;
using Jandirus.Core.Combat;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO QUE QUEM ESTA PERTO OUVE E SENTE (`--diagvizinho`) -- o item 5 do playtest de 2026-10-10:
///
///   a. *"Os sons de carregar ki (C) e de dash (Shift) so tocam para o proprio jogador. Devem tocar para os
///      jogadores proximos, com audio posicional, e o de carregar tem que parar para todos quando ele solta C."*
///   b. *"enquanto o jogador segura C com o ki acima de 120%, a tela dele e a dos jogadores ao redor tremem;
///      para quando ele solta C. Padrao: raio e intensidade configuraveis, tremor mais fraco quanto mais longe."*
///
/// O "OUTRO JOGADOR" E UM CORPO SEM DONO que o servidor poe ao lado do host (`GameServer.VizinhoTeste.cs`) e
/// manda pelos funis de producao -- o `Carregar` da tecla C e o tique da carga. O que chega aqui chega PELO
/// FIO (o snapshot de verdade, por um soquete de verdade), e o que se confere e o que este cliente fez com
/// ele: o player do zumbido de pe no corpo alheio, o estalo e o rasgo contados, a amplitude do tremor na
/// camera. Som nao se fotografa: o que se le e o no que toca, depois da carga do recurso.
///
/// CADA REGRA TEM O SEU DEFEITO INJETADO (as linhas "(defeito injetado: ...)"): o zumbido seguindo a aura, o
/// estalo engolido, o arranque mudo e o chao que nao treme sao, um a um, o jogo de antes -- e a regua tem que
/// reprovar todos.
///
///     Godot --headless --path . --host --rede 7994 --diagvizinho --raca Human --conta bancada_dvizinho --nome Ouvinte
/// </summary>
public partial class RoboDosVizinhos : Node
{
	private double _t;
	private int _passo, _esperas;
	private bool _acabou;

	/// <summary>Os corpos sem dono: o que reune e arranca, o que so e conhecido tarde, o que chega reunindo e o que chega correndo.</summary>
	private int _v1, _v2, _v3, _v4;
	private float _tremorDePerto;

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;
	private static World? M => World.Instancia;
	private LocalPlayer? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as LocalPlayer;

	private static RemotePlayer? Remoto(int id) => M?.FindChild($"Remoto{id}", true, false) as RemotePlayer;
	private static CargaVisual? CargaDe(int id) => Remoto(id)?.GetNodeOrNull<CargaVisual>("Carga");

	/// <summary>A distancia do vizinho de perto e a do de longe, em tiles: 3/4 e 1/4 do tremor (o raio e de 16).</summary>
	private const float Perto = 4f, Longe = 12f, ForaDoRaio = 20f;

	/// <summary>Abaixo disto a camera esta parada. O tremor cai a 8 px/s, entao meio segundo zera qualquer um.</summary>
	private const float Parada = 0.01f;

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque + (detalhe.Length == 0 ? "" : $"   [{detalhe}]"));
		if (!ok) _falhas.Add(oque);
	}

	private static float Esperado(float tiles)
		=> CargaDeKi.ForcaDoTremor * CargaDeKi.PesoDoTremor(tiles * Jandirus.Core.World.ZoneCollision.TileSize);

	public override void _Process(double delta)
	{
		if (_acabou || C is not { Connected: true } cli || S is not { } srv || M is not { } mundo) return;

		_t += delta;
		if (_t < 0.5) return;
		_t = 0;

		switch (_passo++)
		{
			// ============================================================== 5a: o zumbido de quem reune
			case 0:
				if (cli.LocalId == 0 || cli.Atributos.Raca is not { Length: > 0 } || Corpo == null) { _passo = 0; return; }
				_v1 = srv.MontarVizinhoDeBancada(cli.LocalId, Perto, comControle: false);
				Conferir(_v1 != 0, "(montagem) o servidor pos um corpo sem dono a 4 tiles do host");
				_esperas = 0;
				break;

			case 1:
			{
				if (CargaDe(_v1) == null && ++_esperas < 20) { _passo = 1; return; }
				CargaVisual? cg = CargaDe(_v1);
				Conferir(cg != null, "(montagem) o vizinho chegou pelo snapshot e tem o node da carga");
				if (cg == null) { _passo = 999; return; }
				Conferir(!cg.ZumbindoDeTeste && cg.EstalosDeTeste == 0, "parado, o vizinho nao zumbe nem estala");
				Conferir(srv.VizinhoSeguraOC(_v1, true), "(montagem) o servidor aceitou o C do vizinho");
				break;
			}

			case 2:
			{
				CargaVisual cg = CargaDe(_v1)!;
				(bool reunindo, bool aura, _) = srv.CargaDeBancada(_v1);
				Conferir(reunindo && !aura, "(montagem) ele reune SEM aura: so o Ki Unlocked, o caso de todo personagem novo");
				Conferir(cg.ZumbindoDeTeste,
					"**O VIZINHO QUE REUNE KI ZUMBE PRA QUEM ESTA PERTO**, com ou sem aura: o zumbido segue o estado da tecla (o bit `Reunindo`)");
				Conferir(cg.EstalosDeTeste == 1, "...e o ESTALO de inicio da primeira carga vista toca, uma vez", $"{cg.EstalosDeTeste}");
				Conferir(!cg.CarregandoDeTeste, "...com a chama apagada: sem controle de ki nao ha aura, e o SOM e que e de todos");
				srv.VizinhoSeguraOC(_v1, false);
				break;
			}

			case 3:
				Conferir(!CargaDe(_v1)!.ZumbindoDeTeste, "**SOLTOU O C, O ZUMBIDO PARA** pra quem ouvia");
				World.ZumbidoSegueAAuraDeTeste = true;
				srv.VizinhoSeguraOC(_v1, true);
				break;

			case 4:
				Conferir(!CargaDe(_v1)!.ZumbindoDeTeste && srv.CargaDeBancada(_v1).Reunindo,
					"(defeito injetado: o zumbido alheio segue o bit da aura) o vizinho sem aura reune em SILENCIO -- o relato do playtest, reproduzido");
				World.ZumbidoSegueAAuraDeTeste = false;
				break;

			case 5:
				Conferir(CargaDe(_v1)!.ZumbindoDeTeste,
					"desfeito o defeito, o zumbido entra no snapshot seguinte: e ESTADO, nao precisa de evento novo");
				srv.VizinhoSeguraOC(_v1, false);

				// QUEM CHEGA JA REUNINDO: montado e com o C em baixo ANTES de este cliente o ver pela primeira vez.
				_v3 = srv.MontarVizinhoDeBancada(cli.LocalId, 6f, comControle: false);
				srv.VizinhoSeguraOC(_v3, true);
				_esperas = 0;
				break;

			case 6:
			{
				if (CargaDe(_v3) == null && ++_esperas < 20) { _passo = 6; return; }
				CargaVisual? cg = CargaDe(_v3);
				Conferir(cg is { ZumbindoDeTeste: true, EstalosDeTeste: 0 },
					"**QUEM EU ENCONTRO JA REUNINDO ZUMBE, E SEM ESTALO**: o estado aparece pra quem chega depois, e o estalo e so de quem viu comecar",
					$"zumbindo={cg?.ZumbindoDeTeste} estalos={cg?.EstalosDeTeste}");
				srv.VizinhoSeguraOC(_v3, false);

				CargaVisual.EstaloEngolidoDeTeste = true;
				_v2 = srv.MontarVizinhoDeBancada(cli.LocalId, 5f, comControle: false);
				_esperas = 0;
				break;
			}

			case 7:
				if (CargaDe(_v2) == null && ++_esperas < 20) { _passo = 7; return; }
				Conferir(CargaDe(_v2) != null, "(montagem) um segundo vizinho, visto PARADO antes de reunir");
				srv.VizinhoSeguraOC(_v2, true);
				break;

			case 8:
			{
				CargaVisual? cg = CargaDe(_v2);
				Conferir(cg is { ZumbindoDeTeste: true, EstalosDeTeste: 0 },
					"(defeito injetado: o corpo so e dado por conhecido quando o estado muda) a primeira carga de um corpo que eu ja via sai SEM o estalo -- a regua sabe reprovar",
					$"zumbindo={cg?.ZumbindoDeTeste} estalos={cg?.EstalosDeTeste}");
				CargaVisual.EstaloEngolidoDeTeste = false;
				srv.VizinhoSeguraOC(_v2, false);
				break;
			}

			// ============================================================== 5a: o rasgo de quem arranca
			case 9:
				Conferir(Remoto(_v1) is { ArrancadasOuvidasDeTeste: 0 }, "(montagem) ninguem ouviu arrancada nenhuma do vizinho ate aqui");
				srv.VizinhoCorre(_v1, true);
				break;

			case 10:
				Conferir(Remoto(_v1) is { ArrancadasOuvidasDeTeste: 1 },
					"**O VIZINHO QUE ARRANCA E OUVIDO**: o rasgo do comeco da corrida toca no corpo dele, uma vez",
					$"{Remoto(_v1)?.ArrancadasOuvidasDeTeste}");
				srv.VizinhoCorre(_v1, false);
				break;

			case 11:
				RemotePlayer.ArranqueAlheioMudoDeTeste = true;
				srv.VizinhoCorre(_v1, true);
				break;

			case 12:
				Conferir(Remoto(_v1) is { ArrancadasOuvidasDeTeste: 1 },
					"(defeito injetado: o corpo alheio arranca em silencio) a segunda arrancada nao e ouvida -- a regua sabe reprovar",
					$"{Remoto(_v1)?.ArrancadasOuvidasDeTeste}");
				RemotePlayer.ArranqueAlheioMudoDeTeste = false;
				srv.VizinhoCorre(_v1, false);
				break;

			case 13:
				srv.VizinhoCorre(_v1, true);
				break;

			case 14:
				Conferir(Remoto(_v1) is { ArrancadasOuvidasDeTeste: 2 },
					"desfeito o defeito, a arrancada seguinte e ouvida de novo -- um rasgo por arrancada",
					$"{Remoto(_v1)?.ArrancadasOuvidasDeTeste}");
				srv.VizinhoCorre(_v1, false);

				_v4 = srv.MontarVizinhoDeBancada(cli.LocalId, 7f, comControle: false);
				srv.VizinhoCorre(_v4, true);
				_esperas = 0;
				break;

			case 15:
				if (Remoto(_v4) == null && ++_esperas < 20) { _passo = 15; return; }
				Conferir(Remoto(_v4) is { ArrancadasOuvidasDeTeste: 0 },
					"quem eu encontro JA correndo nao \"arrancou\" agora: sem rasgo na primeira vez que o vejo",
					$"{Remoto(_v4)?.ArrancadasOuvidasDeTeste}");
				srv.VizinhoCorre(_v4, false);

				// ---- daqui pra baixo, o tremor. O vizinho 1 ganha controle de ki e o tanque a 125%.
				Conferir(mundo.TremorDeTeste <= Parada, "(montagem) a camera esta parada antes de alguem passar dos 120%", $"{mundo.TremorDeTeste:0.00}");
				srv.LevarVizinhoDeBancada(_v1, cli.LocalId, Perto);
				srv.DarControleDeBancada(_v1, 1.25);
				srv.VizinhoSeguraOC(_v1, true);
				break;

			// ============================================================== 5b: o chao treme
			case 16:
			{
				(bool reunindo, bool aura, bool treme) = srv.CargaDeBancada(_v1);
				Conferir(reunindo && aura && treme, "(montagem) o servidor diz do vizinho: reunindo, com aura, acima de 120%",
					$"reunindo={reunindo} aura={aura} treme={treme}");
				Conferir(CargaDe(_v1) is { TremendoDeTeste: true }, "o bit `CargaAlta` chegou ao corpo dele pelo snapshot");
				_tremorDePerto = mundo.TremorDeTeste;
				Conferir(_tremorDePerto > Esperado(Perto) * 0.6f && _tremorDePerto <= Esperado(Perto) + 0.01f,
					"**A TELA DE QUEM ESTA PERTO TREME** enquanto o vizinho segura o C acima de 120%",
					$"{_tremorDePerto:0.00} px a 4 tiles (o cheio dali e {Esperado(Perto):0.00})");
				srv.LevarVizinhoDeBancada(_v1, cli.LocalId, Longe);
				break;
			}

			case 17:
			{
				float longe = mundo.TremorDeTeste;
				Conferir(longe > Esperado(Longe) * 0.5f && longe <= Esperado(Longe) + 0.01f && longe < _tremorDePerto * 0.6f,
					"**MAIS FRACO QUANTO MAIS LONGE**: a 12 tiles o mesmo tremor chega com um terco da forca de 4 tiles",
					$"{longe:0.00} px a 12 tiles (o cheio dali e {Esperado(Longe):0.00}), contra {_tremorDePerto:0.00} de perto");
				srv.LevarVizinhoDeBancada(_v1, cli.LocalId, ForaDoRaio);
				break;
			}

			case 18:
				Conferir(mundo.TremorDeTeste <= Parada && CargaDe(_v1) is { TremendoDeTeste: true },
					"fora do raio (20 tiles, o raio e de 16) a tela nao se mexe -- com o estado ainda ligado no corpo dele",
					$"{mundo.TremorDeTeste:0.00}");
				srv.LevarVizinhoDeBancada(_v1, cli.LocalId, Perto);
				break;

			case 19:
				Conferir(mundo.TremorDeTeste > Esperado(Perto) * 0.6f, "(montagem) de volta a 4 tiles, treme de novo", $"{mundo.TremorDeTeste:0.00}");
				srv.VizinhoSeguraOC(_v1, false);
				break;

			case 20:
				Conferir(mundo.TremorDeTeste <= Parada && CargaDe(_v1) is { TremendoDeTeste: false },
					"**SOLTOU O C, A TELA PARA** -- meio segundo depois nao sobra tremor nenhum", $"{mundo.TremorDeTeste:0.00}");
				CargaDeKi.ChaoNaoTremeDeTeste = true;
				srv.DarControleDeBancada(_v1, 1.25);
				srv.VizinhoSeguraOC(_v1, true);
				break;

			case 21:
				Conferir(mundo.TremorDeTeste <= Parada && srv.CargaDeBancada(_v1).Reunindo,
					"(defeito injetado: o chao nunca treme) o vizinho segura o C acima de 120% e a tela nao se mexe -- o jogo de antes, e a regua sabe reprovar",
					$"{mundo.TremorDeTeste:0.00}");
				CargaDeKi.ChaoNaoTremeDeTeste = false;
				break;

			case 22:
				Conferir(mundo.TremorDeTeste > Esperado(Perto) * 0.6f,
					"desfeito o defeito, o tremor entra no snapshot seguinte (estado, de novo)", $"{mundo.TremorDeTeste:0.00}");
				// ABAIXO DO LIMIAR: 110% e acima de 100 (a aura do excesso acende) e abaixo de 120.
				srv.DarControleDeBancada(_v1, 1.10);
				break;

			case 23:
				Conferir(mundo.TremorDeTeste <= Parada && srv.CargaDeBancada(_v1) is { Reunindo: true, Aura: true },
					"a 110% de Ki -- acima de 100, abaixo de 120 -- ele reune com a aura acesa e a tela NAO treme: o limiar e o do pedido",
					$"{mundo.TremorDeTeste:0.00}");
				srv.VizinhoSeguraOC(_v1, false);

				// O PROPRIO CORPO: o host ganha o controle, o tanque a 125%, e pede o C pelo canal de verdade.
				srv.DarControleDeBancada(cli.LocalId, 1.25);
				cli.SendCarregar(true);
				break;

			case 24:
			{
				float meu = mundo.TremorDeTeste;
				Conferir(Corpo?.GetNodeOrNull<CargaVisual>("Carga") is { TremendoDeTeste: true }
					  && meu > CargaDeKi.ForcaDoTremor * 0.6f && meu <= CargaDeKi.ForcaDoTremor + 0.01f,
					"**A TELA DE QUEM CARREGA TAMBEM TREME**, e inteira (distancia zero) -- pelo mesmo bit, e nao por um tremor local a parte",
					$"{meu:0.00} px (o cheio e {CargaDeKi.ForcaDoTremor:0.00})");
				cli.SendCarregar(false);
				break;
			}

			case 25:
				Conferir(mundo.TremorDeTeste <= Parada, "...e para quando ele solta o C", $"{mundo.TremorDeTeste:0.00}");
				break;

			// ============================================================== relatorio
			default:
				_acabou = true;
				World.ZumbidoSegueAAuraDeTeste = false;
				CargaVisual.EstaloEngolidoDeTeste = false;
				RemotePlayer.ArranqueAlheioMudoDeTeste = false;
				CargaDeKi.ChaoNaoTremeDeTeste = false;
				cli.SendCarregar(false);
				srv.DesmontarVizinhosDeBancada();
				GD.Print("\n[dvizinho] ===== BANCADA DO QUE QUEM ESTA PERTO OUVE E SENTE =====");
				foreach (string l in _passos) GD.Print("[dvizinho] " + l);
				GD.Print(_falhas.Count == 0
					? "[dvizinho] ===== TUDO OK ====="
					: $"[dvizinho] ===== {_falhas.Count} FALHA(S) =====\n[dvizinho]   " + string.Join("\n[dvizinho]   ", _falhas));
				break;
		}
	}
}
