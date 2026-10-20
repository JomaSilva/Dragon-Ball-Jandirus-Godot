using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO BALAO DE FALA (`--diagbalao`).
///
/// ============================ O QUE SO O TESTE RESPONDE ============================
/// Olhando pra tela da pra ver que apareceu um texto sobre a cabeca. O que a foto NAO diz:
///   * o balao pega o canal CERTO? (OOC e do jogador, nao do personagem; o sussurro de longe
///     chega com o texto VAZIO -- os dois viram balao errado sem ninguem reclamar)
///   * a fala acha o CORPO CERTO? Desde 2026-10-19 o pacote traz o id da BOCA, porque o nome nao
///     separa corpos: dois homonimos em campo, a fusao que se apresenta com outro nome, quem fala
///     antes do proprio `PeerLook`. A busca pelo nome abria o balao no primeiro dono do nome da zona
///     -- o *"as vezes o balao nao aparece sobre eles mesmo eles falando"* do dono. Ver `OsHomonimos`.
///   * e a fala que passa pelo SERVIDOR chega com esse id? Ver `OFio`: e a unica medida que sai do
///     `SendChat` e volta pela rede, em vez de entrar pelo `World.AoFalar`.
///   * o NOME DERIVADO comprido ("corpo de X", "Bio-Androide de X") atravessa o fio, no `PeerLook` e de autor
///     no chat? O leitor do LiteNetLib devolve VAZIO acima do teto, e o teto era o 24 da criacao. Ver
///     `ONomeComprido`: corpos de verdade do servidor do `--host`, com o nome dado e lido pelas duas pontas.
///   * o corpo remoto nasce COM o balao? Sao duas linhas em dois lugares do `World` (a local e a
///     remota) e esquecer a segunda deixa metade do jogo mudo sem nenhum erro.
///   * uma frase de 400 letras QUEBRA, ou vira a faixa atravessando a tela?
///   * duas falas em meio segundo: a primeira e apagada antes de ser lida?
///   * ele SOME sozinho, ou fica pra sempre depois que a pessoa cala?
///   * ELE ANDA COM O CORPO? Hoje ele e FILHO e isso sai de graca -- mas "poe o texto numa camada
///     de tela pra fonte nao esticar no zoom" e um conserto plausivel, e ele quebra exatamente
///     isso: o balao fica parado no cenario enquanto o dono sai andando.
///   * ELE SOBE COM QUEM VOA? Esta e a pergunta cara: a lista de filhos que recebem a altitude ja
///     esqueceu a chama da carga uma vez, e o defeito so aparece com o corpo NO AR.
///   * a fala de CINEMATICA chega em quem ASSISTE? Era divida anotada ("o cliente nao tem mapa de
///     id -> nome") e o mapa passou a existir -- so o teste diz se ela foi mesmo paga.
/// ==================================================================================
///
/// ============================ NADA AQUI E MONTADO NA MAO ============================
/// A versao anterior desta bancada montava o corpo remoto com `new RemotePlayer` e pendurava o
/// balao nele ela mesma -- ou seja, testava um boneco que ela propria tinha construido certo. Se
/// o `World` esquecesse de dar balao a quem entra no campo de visao (`World.cs:1332`), o teste
/// continuaria verde e o jogo inteiro ficaria mudo menos o dono da tela.
///
/// Agora o corpo remoto nasce pelo `AoReceberSnapshot`, o nome dele chega pelo
/// `AoReceberAparencia` (o `PeerLook`), a fala entra pelo `AoFalar` e o passo pelo `Receive`.
/// Todos sao os metodos que a rede chama em jogo.
/// ==================================================================================
///
/// COMO RODAR (uma janela so; o servidor sobe junto pelo `--host`):
///     Godot --path . --host --diagbalao --raca Saiyan --nome Zx --conta &lt;NOVA&gt;
/// </summary>
public partial class RoboDeBalao : Node
{
	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private bool _acabou;
	private int _passo;
	private double _t;

	/// <summary>
	/// Quanto tempo se aceita esperar o cenario do teste (o corpo local com o filho `Balao`).
	///
	/// ============================ ESPERAR PRA SEMPRE NAO E REPROVAR ============================
	/// A guarda do `_Process` e um `return` -- e se o corpo local nascesse SEM balao (apagar uma
	/// linha do `World` basta), a bancada ficaria rodando calada ate alguem fechar a janela. Uma
	/// bancada que trava em vez de reprovar e pior que nenhuma: ela nao diz o que houve, e num
	/// script ela pendura a fila inteira.
	/// ====================================================================================
	/// </summary>
	private const double EsperaMaxima = 25.0;
	private double _espera;

	/// <summary>O corpo remoto do teste: um id que o servidor nunca vai usar nesta sessao.</summary>
	private const int IdDoRemoto = 4242;
	private const string NomeDoRemoto = "Kakaroto";

	/// <summary>
	/// Os corpos de `OsHomonimos`: quatro por medida, a partir daqui. Longe dos ids do servidor (que conta de 1
	/// em 1 a cada corpo que nasce) e do <see cref="IdDoRemoto"/>.
	/// </summary>
	private const int IdDosHomonimos = 900_000;

	/// <summary>
	/// O nome repetido. Nenhum NPC se chama assim: se um se chamasse, a busca pelo nome iria pra ELE e os dois
	/// homonimos ficariam mudos -- a medida com defeito continuaria reprovando, so que por outra porta.
	/// </summary>
	private const string NomeDosHomonimos = "Gemeo de Bancada";

	/// <summary>As frases de `OFio`: as que saem do MEU corpo pelo servidor de verdade -- uma de cada funil (`PorPerto`, `Sussurrar`).</summary>
	private const string FraseDoFio = "esta passou pelo servidor", SussurroDoFio = "e esta tambem";

	/// <summary>O id da boca que o `S2C.Chat` de cada frase do fio trouxe; a frase so entra quando ele chega.</summary>
	private readonly Dictionary<string, int> _bocasDoFio = [];

	/// <summary>A frase dos corpos de `ONomeComprido`, dita pelo servidor (`FalarPeloCorpoDeFoto`).</summary>
	private const string FraseDoNomeComprido = "meu nome nao cabia no fio";

	/// <summary>
	/// Quanto uma medida de `ONomeComprido` espera pelos dois pacotes dela (o `PeerLook` e a fala). Passado o prazo, o que
	/// faltou sai na regua como "nao chegou" -- e nao como "chegou vazio", que e o defeito.
	/// </summary>
	private const double PrazoDoNome = 5.0;

	/// <summary>
	/// UMA MEDIDA DE `ONomeComprido`: o corpo que o servidor forjou, o nome que ele deu, e o que o cliente LEU -- o nome no
	/// `PeerLook`, o autor no chat (nulo = o pacote nao chegou), e o que o mapa id -> nome do `World` responde depois.
	/// </summary>
	private sealed class MedidaDoNome
	{
		public int Id;
		public string Enviado = "";
		public string? Lido, Autor, NoMapa;
		public bool Falou;
	}

	private MedidaDoNome _medida = new();
	private MedidaDoNome? _comDefeitoNoCliente, _umDegrau, _alemDoTeto, _comDefeitoNoServidor;

	// O que o balao do LOCAL deve continuar mostrando quando a fala e de outra pessoa.
	private const string FraseDoLocal = "eu ando e falo";
	private const string FraseDoRemoto = "estou aqui do outro lado";
	private const string FraseNoAr = "estou aqui em cima";

	/// <summary>A frase da foto: comprida o bastante pra quebrar em duas linhas em qualquer zoom.</summary>
	private const string FraseDaFoto = "as letras deste balao precisam sair nitidas em qualquer zoom da camera";

	/// <summary>O zoom da foto. Quatro: fora do padrao (3), e blocos de 4x4 sao faceis de medir.</summary>
	private const int ZoomDaFoto = 4;

	/// <summary>A fracao de tracos claros com largura que NAO e multiplo do zoom, nitido e serrilhado; -1 = sem foto.</summary>
	private double _nitido = -1, _serrilhado = -1;

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _passos.Add("  --     " + oque);

	public override void _Process(double delta)
	{
		if (_acabou) return;

		if (GameClient.Instance is not { Connected: true } cli
		 || World.Instancia is not { } mundo
		 || GetTree().Root.FindChild("LocalPlayer", true, false) is not Node2D corpo
		 || corpo.GetNodeOrNull<BalaoDeFala>("Balao") is not { } balao)
		{
			// Ver `EsperaMaxima`: sem isto a falta do balao vira uma janela parada, e nao um placar.
			_espera += delta;
			if (_espera > EsperaMaxima)
			{
				Conferir(false, $"em {EsperaMaxima:0}s o corpo local nasceu com o filho `Balao` "
							  + "(o `World` cria um por corpo -- ver World.cs:1265)");
				Fechar();
			}
			return;
		}

		_espera = 0;
		_t += delta;

		switch (_passo)
		{
			case 0:
				GD.Print("[balao] ===== BANCADA DO BALAO DE FALA =====");
				Conferir(balao.GetParent() == corpo,
						 "o balao e FILHO do corpo (e nao um texto solto no mundo ou numa camada de tela)");
				OPortaoDeCanal();
				AQuebraDeLinha();
				Passar();
				break;

			// -------------------------------------------------------------
			// A FALA CHEGA NO CORPO -- pelo caminho do EVENTO, e nao pelo `Dizer` na mao
			// -------------------------------------------------------------
			case 1:
				mundo.AoFalar(Protocol.Fala.Diz, cli.LocalName, "sai da frente", cli.LocalId);
				Conferir(balao.Visible && balao.TextoDeTeste == "sai da frente",
						 $"a fala achou o meu corpo pelo id da boca e virou balao (\"{balao.TextoDeTeste}\")");

				mundo.AoFalar(Protocol.Fala.Ooc, cli.LocalName, "isto e fora do personagem", cli.LocalId);
				mundo.AoFalar(Protocol.Fala.Looc, cli.LocalName, "isto tambem", cli.LocalId);
				mundo.AoFalar(Protocol.Fala.Sistema, "", "zona carregada", 0);
				mundo.AoFalar(Protocol.Fala.Sussurro, cli.LocalName, "", cli.LocalId);
				Conferir(balao.TextoDeTeste == "sai da frente" && balao.NaFilaDeTeste == 0,
						 "OOC, LOOC, Sistema e o sussurro-sem-texto NAO viram balao (nem entram na fila)");

				mundo.AoFalar(Protocol.Fala.Diz, "Ninguem Com Esse Nome", "oi", 0);
				mundo.AoFalar(Protocol.Fala.Diz, "Ninguem Com Esse Nome", "oi", IdDosHomonimos - 1);
				Conferir(balao.TextoDeTeste == "sai da frente",
						 "fala sem boca (0) ou de uma boca que nao esta em campo nao encosta em corpo nenhum");

				// A SEGUNDA FRASE, DENTRO DO PISO: tem que ESPERAR, e nao apagar a primeira.
				mundo.AoFalar(Protocol.Fala.Diz, cli.LocalName, "vou te acertar", cli.LocalId);
				Conferir(balao.TextoDeTeste == "sai da frente" && balao.NaFilaDeTeste == 1,
						 "fala nova dentro do piso de leitura ENTRA NA FILA -- a primeira nao e apagada");
				Passar();
				break;

			// -------------------------------------------------------------
			// ...E ENTRA QUANDO O PISO PASSA
			// -------------------------------------------------------------
			case 2:
				if (_t < BalaoDeFala.PisoDeLeitura + 0.2) return;
				Conferir(balao.TextoDeTeste == "vou te acertar" && balao.NaFilaDeTeste == 0,
						 $"passado o piso ({BalaoDeFala.PisoDeLeitura:0.0}s) a da fila assume "
					   + $"(\"{balao.TextoDeTeste}\") -- SUBSTITUI, nao empilha");
				Passar();
				break;

			// -------------------------------------------------------------
			// SOME SOZINHO
			// -------------------------------------------------------------
			case 3:
				if (_t < BalaoDeFala.DuracaoMinima + 0.6) return;
				Conferir(!balao.Visible,
						 $"passados {BalaoDeFala.DuracaoMinima:0.0}s sem falar nada o balao some sozinho");
				Conferir(!balao.IsProcessing(),
						 "...e para de gastar quadro (corpo calado nao custa `_Process`)");

				// O ANDAR COMECA AGORA: fala, guarda onde estava e solta o piloto automatico.
				mundo.AoFalar(Protocol.Fala.Diz, cli.LocalName, FraseDoLocal, cli.LocalId);
				Comecar(corpo);
				mundo.AndarDeTeste(Vector2.Right);
				Passar();
				break;

			// -------------------------------------------------------------
			// O BALAO ANDA COM O CORPO LOCAL -- com as pernas dele, nao com um `Position` na mao
			// -------------------------------------------------------------
			case 4:
				Seguir(corpo, balao);
				// Meio caminho pra leste, meio pro sul: se uma das duas direcoes der numa parede,
				// a outra ainda anda -- e o teste mede o CAMINHO percorrido, nao o deslocamento
				// liquido, entao voltar pelo mesmo lugar tambem conta.
				if (_t > 0.7 && _t < 0.75) mundo.AndarDeTeste(Vector2.Down);
				if (_t < 1.4) return;

				mundo.PararDeTeste();
				Conferir(_andou > 24f,
						 $"o corpo local andou de verdade ({_andou:0} px de caminho) -- sem isto o "
					   + "teste do balao seria sobre um boneco parado");
				Conferir(_pior < 0.01f,
						 $"e o balao ficou COLADO nele o caminho inteiro (pior desvio {_pior:0.###} px) "
					   + "-- ele mora no corpo, e nao num lugar do cenario nem numa camada de tela");
				Conferir(balao.Visible, "...e continuou na tela enquanto o dono se mexia");

				// O MEU corpo tambem voa, e a lista de filhos que sobem e OUTRA (`LocalPlayer`).
				EuSubo(mundo, cli);
				Passar();
				break;

			// -------------------------------------------------------------
			// O MEU BALAO SOBE COM O MEU CORPO -- lista de filhos do `LocalPlayer`
			// -------------------------------------------------------------
			case 5:
				// REENVIADA TODO QUADRO: o servidor de verdade (o `--host` e este processo) manda a amostra
				// dele a 30 Hz com altitude 0, e a injetada perdia a corrida em metade das rodadas.
				EuSubo(mundo, cli);
				if (EuSubi(corpo, balao)) { Passar(); break; }
				if (_t < 1.0) return;
				// O QUE SE VIU, E NAO SO "nao subiu". Sao dois defeitos diferentes com a mesma cara:
				// a altura nao chegou no corpo (altitude 0 -- o snapshot injetado foi sobrescrito
				// pelo do servidor, que reafirma "no chao" 30x por segundo) ou chegou e nao virou
				// desenho (altitude viva e Y parado -- ai o defeito e a varredura dos filhos).
				Conferir(false, "em 1s o meu corpo chegou a ser DESENHADO no ar (a altura do "
							  + "snapshot chegou no `LocalPlayer.AplicarAltura`) -- desenho em "
							  + $"{corpo.GetNode<CharacterVisual>("Visual").Position.Y:0.##} px, "
							  + $"altitude {(corpo as LocalPlayer)?.Altitude ?? -1f:0.##}");
				Passar();
				break;

			// -------------------------------------------------------------
			// O CORPO REMOTO NASCE PELA REDE, e a fala tem que achar ELE (e nao o meu)
			// -------------------------------------------------------------
			case 6:
				OutroCorpoEntraEmCampo(mundo, balao);
				Passar();
				break;

			// -------------------------------------------------------------
			// E O BALAO DELE ANDA JUNTO -- pelos snapshots, que e como corpo remoto se mexe
			// -------------------------------------------------------------
			case 7:
				if (Remoto(mundo) is not { } andarilho) { Passar(); return; }
				Seguir(andarilho, andarilho.GetNode<BalaoDeFala>("Balao"));
				// Um passo por quadro, dentro do limite do que o servidor aceitaria (ver
				// `RemotePlayer.LimiteDeSalto`): acima dele o corpo CRAVA, e cravar e teleporte --
				// o que se quer medir aqui e o corpo deslizando.
				Snapshot(mundo, _ondeORemoto + new Vector2(12, 0), 0f, andando: true);
				// Os quatro de `OsHomonimos` ainda estao em campo: meio segundo de balao aberto e a foto.
				if (_homonimosNaTela.Count > 0 && _t >= 0.5) FotografarOsHomonimos(mundo);
				if (_t < 1.0) return;

				Conferir(_andou > 24f,
						 $"o corpo remoto andou pelos snapshots ({_andou:0} px de caminho)");
				Conferir(_pior < 0.01f,
						 $"e o balao dele foi junto (pior desvio {_pior:0.###} px)");
				// A SUBIDA COMECA: ele fala de novo, pra que haja texto na tela DURANTE o voo.
				mundo.AoFalar(Protocol.Fala.Diz, NomeDoRemoto, FraseNoAr, IdDoRemoto);
				Passar();
				break;

			// -------------------------------------------------------------
			// SOBE COM QUEM VOA -- pelo snapshot, que e por onde a altitude entra
			// -------------------------------------------------------------
			case 8:
				// A AMOSTRA DA SUBIDA E REENVIADA TODO QUADRO, como os passos do caso anterior: uma amostra
				// unica ficava presa na fila de jitter (o corpo no chao, 0 px de desenho) em metade das
				// rodadas, e a bancada reprovava uma subida que nunca chegou a ser tocada.
				Snapshot(mundo, _ondeORemoto, AlturaDoTeste, andando: false);
				if (_t < 1.5) return;
				OVoadorSubiu(mundo);
				Passar();
				break;

			// -------------------------------------------------------------
			// A FALA DE CINEMATICA -- a divida que dizia "o cliente nao tem nome"
			// -------------------------------------------------------------
			case 9:
				ACinematicaAlheia(corpo, balao);
				Passar();
				break;
			case 10:
				// NITIDO EM QUALQUER ZOOM (pedido do dono, 2026-09-06): primeiro por construcao (o no
				// encolhe por 1/zoom e a fonte cresce por zoom), depois na FOTO -- que e o que o dono ve.
				ONitidoPorConstrucao(mundo, balao);
				mundo.AplicarZoom(ZoomDaFoto);
				mundo.AoFalar(Protocol.Fala.Diz, cli.LocalName, FraseDaFoto, cli.LocalId);
				Passar();
				break;
			case 11:
				if (_t < 0.5) return;
				_nitido = MedirAFoto(balao, "balao-1-nitido");
				// O DEFEITO INJETADO: o mesmo balao, aberto, volta a desenhar em pixels de mundo -- e a
				// letra de 8 px e esticada pela camera. A troca acontece no `_Process` dele, sem fala nova.
				BalaoDeFala.NitidoDeTeste = false;
				Passar();
				break;
			case 12:
				if (_t < 0.5) return;
				_serrilhado = MedirAFoto(balao, "balao-2-serrilhado");
				BalaoDeFala.NitidoDeTeste = true;
				if (_nitido >= 0 && _serrilhado >= 0)
				{
					Conferir(_nitido > 0.30,
							 $"NA FOTO (zoom {ZoomDaFoto}): as letras NAO sao blocos de {ZoomDaFoto}x{ZoomDaFoto} -- "
						   + $"{_nitido * 100:0}% dos tracos claros tem largura que nao e multiplo do zoom "
						   + "(a fonte vetorial rasterizada no tamanho da tela)");
					Conferir(_serrilhado <= 0.02,
							 $"[injecao] em pixels de MUNDO, todo traco e multiplo do zoom ({_serrilhado * 100:0}% "
						   + "fora) -- a letra de 8 px esticada pela camera, que e o serrilhado que o dono viu");
					Conferir(_nitido > _serrilhado, "...e a foto nitida e mais nitida que a serrilhada pela mesma regua");
				}
				mundo.AplicarZoom(Math.Max(1, Boot.Config.Zoom));
				// O FIO: as duas ultimas falas saem pelo servidor de verdade (o `--host` e este processo).
				cli.FalouPelaBoca += OuvirOFio;
				cli.SendChat(Protocol.Fala.Diz, FraseDoFio);
				Passar();
				break;
			case 13:
				// 0,6 S ENTRE AS DUAS: o servidor joga fora a fala que chega antes dos 400 ms da anterior
				// (`GameServer.MsEntreFalas`), calado.
				if ((!_bocasDoFio.ContainsKey(FraseDoFio) || balao.TextoDeTeste != FraseDoFio || _t < 0.6) && _t < 5.0) return;
				OFio(cli, balao, FraseDoFio, "a fala");
				cli.SendChat(Protocol.Fala.Sussurro, SussurroDoFio);
				Passar();
				break;
			case 14:
				// O sussurro chega dentro do piso de leitura da fala de cima: espera a fila do balao andar.
				if ((!_bocasDoFio.ContainsKey(SussurroDoFio) || !balao.TextoDeTeste.Contains(SussurroDoFio)) && _t < 5.0) return;
				OFio(cli, balao, SussurroDoFio, "o sussurro");
				cli.FalouPelaBoca -= OuvirOFio;
				Passar();
				break;

			// -------------------------------------------------------------
			// O NOME COMPRIDO NO FIO (2026-10-19) -- quatro medidas em fila, cada uma esperando os PROPRIOS pacotes
			// -------------------------------------------------------------
			case 15:
				if (Jandirus.Server.GameServer.Instance is not { } srv)
				{
					Conferir(false, "o servidor do `--host` roda neste processo (sem ele ninguem escreve o nome comprido no fio)");
					Fechar();
					break;
				}
				AsContasDoNome();
				cli.PeerLooked += OuvirONome;
				cli.FalouPelaBoca += OuvirOAutor;
				// A PRIMEIRA E A DO DEFEITO DO CLIENTE: o botao fica ligado ate os DOIS pacotes da medida serem lidos.
				GameClient.NomeComTetoVelhoDeTeste = true;
				ComecarMedida(srv, cli, DerivadoDeUmDegrau('A'), 0);
				Passar();
				break;
			case 16:
				if (!MedidaAndou()) return;
				GameClient.NomeComTetoVelhoDeTeste = false;
				_comDefeitoNoCliente = FecharMedida(mundo);
				ComecarMedida(Jandirus.Server.GameServer.Instance!, cli, DerivadoDeUmDegrau('B'), 1);
				Passar();
				break;
			case 17:
				if (!MedidaAndou()) return;
				_umDegrau = FecharMedida(mundo);
				ComecarMedida(Jandirus.Server.GameServer.Instance!, cli, DerivadoAlemDoTeto('C'), 2);
				Passar();
				break;
			case 18:
				if (!MedidaAndou()) return;
				_alemDoTeto = FecharMedida(mundo);
				Passar();
				break;
			case 19:
				// Meio segundo pra o balao e a linha do chat da ultima fala serem DESENHADOS antes da foto.
				if (_t < 0.5) return;
				FotografarONomeComprido();
				// A ULTIMA E A DO DEFEITO DO SERVIDOR: ele escreve sem o corte, e o leitor (com o teto novo) devolve vazio.
				Protocol.NomeSemCorteDeTeste = true;
				ComecarMedida(Jandirus.Server.GameServer.Instance!, cli, DerivadoAlemDoTeto('D'), 3);
				Passar();
				break;
			case 20:
				if (!MedidaAndou()) return;
				Protocol.NomeSemCorteDeTeste = false;
				_comDefeitoNoServidor = FecharMedida(mundo);
				cli.PeerLooked -= OuvirONome;
				cli.FalouPelaBoca -= OuvirOAutor;
				Jandirus.Server.GameServer.Instance?.LimparAFoto();
				ONomeComprido();
				Fechar();
				break;
		}
	}

	public override void _ExitTree()
	{
		// O DEFEITO INJETADO NAO SOBREVIVE A BANCADA: `OsHomonimos` ja o desliga no `finally`, e este e o
		// segundo cinto (uma excecao fora dele, a janela fechada no meio).
		World.BalaoPeloNomeDeTeste = false;
		// Os dois de `ONomeComprido` ficam ligados ATRAVES de quadros (o pacote e lido depois), entao este cinto e o unico
		// que os pega quando a janela fecha no meio de uma medida.
		GameClient.NomeComTetoVelhoDeTeste = false;
		Protocol.NomeSemCorteDeTeste = false;
		if (GameClient.Instance is { } cli)
		{
			cli.FalouPelaBoca -= OuvirOFio;
			cli.PeerLooked -= OuvirONome;
			cli.FalouPelaBoca -= OuvirOAutor;
		}
	}

	private void Passar() { _passo++; _t = 0; }

	// =====================================================================
	// 1. O PORTAO DE CANAL
	// =====================================================================
	/// <summary>
	/// Funcao PURA -- nao precisa de rede nem de corpo. E o unico lugar do jogo que decide se uma
	/// linha de chat e voz de um personagem, e errar aqui poe na boca do boneco o que o JOGADOR
	/// disse (o OOC atravessa o planeta inteiro: seria balao em cima de gente do outro lado do mapa).
	/// </summary>
	private void OPortaoDeCanal()
	{
		Conferir(BalaoDeFala.EhDeCorpo(Protocol.Fala.Diz, "oi"), "`Diz` e do corpo");
		Conferir(BalaoDeFala.EhDeCorpo(Protocol.Fala.Emote, "cerra os punhos"), "`Emote` e do corpo");
		Conferir(BalaoDeFala.EhDeCorpo(Protocol.Fala.Pensa, "sera?"), "`Pensa` e do corpo");
		Conferir(BalaoDeFala.EhDeCorpo(Protocol.Fala.Sussurro, "psiu"), "`Sussurro` COM texto e do corpo");

		Conferir(!BalaoDeFala.EhDeCorpo(Protocol.Fala.Sussurro, ""),
				 "`Sussurro` VAZIO nao e (e o teaser de quem estava longe -- balao vazio)");
		Conferir(!BalaoDeFala.EhDeCorpo(Protocol.Fala.Ooc, "oi"), "`Ooc` NAO e do corpo (e do jogador)");
		Conferir(!BalaoDeFala.EhDeCorpo(Protocol.Fala.Looc, "oi"), "`Looc` NAO e do corpo");
		Conferir(!BalaoDeFala.EhDeCorpo(Protocol.Fala.Sistema, "zona carregada"),
				 "`Sistema` NAO e do corpo (nem autor tem)");
	}

	// =====================================================================
	// 2. A QUEBRA DE LINHA
	// =====================================================================
	/// <summary>
	/// A FRASE LONGA NAO PODE VIRAR FAIXA. Medido no balao de verdade (um solto, sem corpo), lendo
	/// a largura que ele mesmo calculou pra desenhar -- e nao remontando a conta aqui.
	/// </summary>
	private void AQuebraDeLinha()
	{
		// ============================ UM BALAO POR FRASE, E ISSO E A REGRA FUNCIONANDO ============================
		// A primeira versao desta bancada reusava UM balao pras quatro frases e reprovou tres delas
		// medindo sempre a mesma coisa: a segunda `Dizer` caiu na FILA (a primeira ainda estava dentro
		// do piso de leitura) e nunca chegou a ser quebrada em linhas. O erro era do teste; o que ele
		// acusou sem querer foi que a fila funciona.
		// ====================================================================================================
		var soltos = new List<BalaoDeFala>();

		BalaoDeFala Medir(string texto)
		{
			var b = new BalaoDeFala { Name = "BalaoSolto" };
			AddChild(b);
			soltos.Add(b);
			b.Dizer(Protocol.Fala.Diz, texto);
			return b;
		}

		BalaoDeFala curta = Medir("curta");
		Conferir(curta.LinhasDeTeste == 1, $"frase curta cabe em UMA linha ({curta.LarguraDeTeste:0} px)");

		BalaoDeFala media = Medir(string.Join(' ', Enumerable.Repeat("palavra", 12)));
		Conferir(media.LinhasDeTeste is > 1 and <= BalaoDeFala.MaxLinhas
			  && media.LarguraDeTeste <= BalaoDeFala.LarguraMaxima,
				 $"frase media quebra em {media.LinhasDeTeste} linhas e nao passa de "
			   + $"{BalaoDeFala.LarguraMaxima:0} px ({media.LarguraDeTeste:0} px)");

		// PASSA MUITO DO TETO: tem que parar em MaxLinhas e avisar com reticencia.
		BalaoDeFala enorme = Medir(string.Join(' ', Enumerable.Repeat("palavra", 200)));
		Conferir(enorme.LinhasDeTeste == BalaoDeFala.MaxLinhas,
				 $"frase enorme para em {BalaoDeFala.MaxLinhas} linhas (nao vira uma torre)");
		Conferir(enorme.LarguraDeTeste <= BalaoDeFala.LarguraMaxima,
				 $"...e continua dentro da largura ({enorme.LarguraDeTeste:0} px)");
		Conferir(enorme.TextoDeTeste.EndsWith('…'),
				 "...e o corte e ANUNCIADO com reticencia (o resto esta no chat)");

		// UMA PALAVRA SO, gigante: nao ha espaco onde quebrar, e mesmo assim nao pode vazar.
		BalaoDeFala colada = Medir(new string('A', 400));
		Conferir(colada.LarguraDeTeste <= BalaoDeFala.LarguraMaxima
			  && colada.LinhasDeTeste <= BalaoDeFala.MaxLinhas,
				 $"palavra unica de 400 letras e cortada na forca ({colada.LinhasDeTeste} linhas, "
			   + $"{colada.LarguraDeTeste:0} px) -- sem espaco onde quebrar, ela vazaria a tela");

		Nota($"a fonte do mundo mede \"palavra\" em {ThemeDB.FallbackFont.GetStringSize("palavra", HorizontalAlignment.Left, -1, 8).X:0} px "
		   + $"e a linha tem {ThemeDB.FallbackFont.GetHeight(8):0} px de altura -- {BalaoDeFala.MaxLinhas} linhas "
		   + $"sao {ThemeDB.FallbackFont.GetHeight(8) * BalaoDeFala.MaxLinhas:0} px sobre a cabeca");

		foreach (BalaoDeFala b in soltos) b.QueueFree();
	}

	// =====================================================================
	// 3. O BALAO SEGUE O CORPO
	// =====================================================================
	/// <summary>O maior desvio visto entre o balao e a cabeca do dono, e o caminho que o dono andou.</summary>
	private float _pior, _andou;
	private Vector2 _antes;

	private void Comecar(Node2D corpo)
	{
		_pior = 0;
		_andou = 0;
		_antes = corpo.GlobalPosition;
	}

	/// <summary>
	/// UM QUADRO DE PERSEGUICAO: quanto o corpo andou, e quanto o balao ficou pra tras.
	///
	/// ============================ POR QUE MEDIR O QUE E "DE GRACA" ============================
	/// Hoje o balao e FILHO do corpo, entao a posicao dele sai da transformacao do pai e este
	/// numero e zero por construcao. O teste nao existe pra duvidar do Godot -- existe pra
	/// reprovar a MUDANCA: a fonte do balao e desenhada em pixels de mundo e cresce com o zoom, e
	/// "poe o texto numa CanvasLayer pra ele nao esticar" e um conserto que alguem vai propor. No
	/// dia em que isso acontecer, o balao para de andar com o dono e este numero deixa de ser
	/// zero -- num corpo parado (que e como quase todo teste olha pra ele) ninguem notaria.
	/// ==================================================================================
	/// </summary>
	private void Seguir(Node2D corpo, BalaoDeFala balao)
	{
		Vector2 esperado = corpo.GlobalPosition + new Vector2(0, BalaoDeFala.AlturaBase);
		_pior = MathF.Max(_pior, balao.GlobalPosition.DistanceTo(esperado));
		_andou += corpo.GlobalPosition.DistanceTo(_antes);
		_antes = corpo.GlobalPosition;
	}

	// =====================================================================
	// 3b. O MEU PROPRIO VOO
	// =====================================================================
	/// <summary>
	/// A ALTURA DO CORPO LOCAL ENTRA POR OUTRA PORTA. E o mesmo `AoReceberSnapshot`, mas o ramo do
	/// proprio id: ele chama `LocalPlayer.ReceberAltura`, e quem empurra os filhos la e o
	/// `AplicarAltura` -- uma SEGUNDA lista, com os mesmos nomes escritos de novo. Duas listas sao
	/// dois lugares onde esquecer o balao, e o corpo remoto nao cobre o meu.
	/// </summary>
	private void EuSubo(World mundo, GameClient cli)
		=> mundo.AoReceberSnapshot([new EntityState
		{
			Id = cli.LocalId,
			Facing = (byte)Jandirus.Core.World.Facing.South,
			Pose = Protocol.Pose.Normal,
			Voando = true,
			Altitude = AlturaDoTeste,
		}]);

	/// <summary>
	/// ============================ NAO SE MEDE A ALTURA, SE MEDE O ACORDO ============================
	/// A altura injetada nao se sustenta, e nem precisa: o servidor reafirma "altitude 0" trinta
	/// vezes por segundo e o desenho ja comeca a descer no quadro seguinte. Por isso o teste nao
	/// pergunta QUANTO o corpo subiu -- pergunta se os dois desenhos (corpo e balao) receberam o
	/// MESMO empurrao, que e verdade em qualquer altura da subida ou da descida.
	///
	/// ERAM TRES: a barra de vida sobre a cabeca era o outro `ISobeComOCorpo` e era conferida aqui.
	/// Ela foi DELETADA a pedido do dono (ver `EntityState`), e com ela o balao passou a ser o unico
	/// node com altura propria -- entao esta bancada e agora a UNICA guarda da regra "quem tem altura
	/// propria SOMA o deslocamento, nao o substitui".
	/// ==========================================================================================
	/// </summary>
	private bool EuSubi(Node2D corpo, BalaoDeFala balao)
	{
		float visual = corpo.GetNode<CharacterVisual>("Visual").Position.Y;
		if (visual > -1f) return false;   // ainda nao saiu do chao neste quadro

		Conferir(Mathf.Abs(balao.Position.Y - (BalaoDeFala.AlturaBase + visual)) < 0.01f,
				 $"o MEU balao recebeu o mesmo empurrao do meu desenho ({visual:0.#} px) e manteve a "
			   + $"altura propria ({balao.Position.Y:0.#} px) -- e outra lista de filhos, a do `LocalPlayer`");
		return true;
	}

	// =====================================================================
	// 4. O OUTRO CORPO
	// =====================================================================
	/// <summary>Uns quatro andares -- alto o bastante pra que um filho esquecido apareca na hora.</summary>
	private const float AlturaDoTeste = 200f;

	private Vector2 _ondeORemoto;

	private static RemotePlayer? Remoto(World mundo) => mundo.CorpoDeTeste(IdDoRemoto) as RemotePlayer;

	/// <summary>
	/// UM SNAPSHOT, como o servidor manda. E o unico caminho pelo qual um corpo remoto nasce, anda
	/// e sobe -- montar o `RemotePlayer` na mao testaria o boneco que a bancada construiu, e nao o
	/// que o `World` constroi (que e onde mora a linha que pendura o balao).
	/// </summary>
	private void Snapshot(World mundo, Vector2 onde, float altitude, bool andando)
	{
		_ondeORemoto = onde;
		mundo.AoReceberSnapshot([new EntityState
		{
			Id = IdDoRemoto,
			Pos = new Vec2(onde.X, onde.Y),
			Facing = (byte)Jandirus.Core.World.Facing.South,
			Moving = andando,
			Pose = Protocol.Pose.Normal,
			Altitude = altitude,
			Voando = altitude > 0f,
		}]);
	}

	/// <summary>
	/// ALGUEM ENTRA NO MEU CAMPO DE VISAO E FALA.
	///
	/// Este e o caso que o balao existe pra resolver. O corpo entra em campo pelo snapshot e o nome
	/// pelo `PeerLook`, como em jogo; a fala traz o id da boca -- e o que nao se separa pelo nome (os
	/// homonimos, a fusao, quem fala antes do proprio `PeerLook`) e medido em `OsHomonimos`.
	/// </summary>
	private void OutroCorpoEntraEmCampo(World mundo, BalaoDeFala meuBalao)
	{
		Vector2 aqui = World.Instancia?.PosicaoLocal ?? Vector2.Zero;
		Snapshot(mundo, aqui + new Vector2(96, 0), 0f, andando: false);

		if (Remoto(mundo) is not { } r)
		{
			Conferir(false, "o snapshot fez nascer o corpo remoto");
			return;
		}

		BalaoDeFala? dele = r.GetNodeOrNull<BalaoDeFala>("Balao");
		Conferir(dele != null,
				 "quem entra no meu campo de visao nasce COM balao (a segunda das duas linhas do "
			   + "`World` -- esquecer ela deixaria mudo todo mundo menos eu)");
		if (dele == null) return;

		mundo.AoReceberAparencia(IdDoRemoto, NomeDoRemoto, "Saiyan", "Male",
								 new Jandirus.Core.Appearance.Appearance());

		mundo.AoFalar(Protocol.Fala.Diz, NomeDoRemoto, FraseDoRemoto, IdDoRemoto);
		Conferir(dele.Visible && dele.TextoDeTeste == FraseDoRemoto,
				 $"a fala dele acha o corpo DELE (\"{dele.TextoDeTeste}\")");
		Conferir(meuBalao.TextoDeTeste != FraseDoRemoto,
				 "...e nao caiu no MEU balao (dois corpos em campo, cada frase no dono certo)");

		OsHomonimos(mundo);

		Comecar(r);
	}

	// =====================================================================
	// 4b. O QUE O NOME NAO SEPARA (2026-10-19)
	// =====================================================================
	/// <summary>
	/// O *"as vezes o balao de fala dos personagens nao aparece sobre eles mesmo eles falando"* do dono. O
	/// balao achava o corpo pelo NOME do autor, pegando o primeiro dono do nome na zona -- e ha tres jeitos de
	/// jogo comum em que o nome nao aponta quem falou. Um corpo pra cada, cada um com a sua frase:
	///   * A e B, HOMONIMOS em campo (os habitantes saem de listas curtas de nome por raca, todo bicho tem o
	///     nome da especie). Pelo nome as duas frases iam pro mesmo corpo e o outro ficava MUDO -- qualquer
	///     que seja a ordem do mapa, e por isso a medida nao depende dela;
	///   * C, a FUSAO: o `PeerLook` traz o nome visivel e o chat o do personagem;
	///   * D, quem fala ANTES do proprio `PeerLook` (snapshot e aparencia viajam em canais sem ordem entre si).
	///
	/// MEDIDO DUAS VEZES, com corpos novos em cada: com o defeito injetado (a busca pelo nome de antes,
	/// `World.BalaoPeloNomeDeTeste`), em que a MESMA regua tem que reprovar as tres, e limpo. Os corpos da
	/// medida com defeito saem de cena na hora; os da limpa ficam pra foto (`FotografarOsHomonimos`).
	/// </summary>
	private void OsHomonimos(World mundo)
	{
		(bool Homonimos, bool Fusao, bool AntesDoNome) comDefeito;
		string comoComDefeito;
		World.BalaoPeloNomeDeTeste = true;
		try { comDefeito = MedirAsBocas(mundo, IdDosHomonimos + 10, out comoComDefeito); }
		finally { World.BalaoPeloNomeDeTeste = false; }
		for (int id = IdDosHomonimos + 10; id < IdDosHomonimos + 14; id++) mundo.AoSair(id);

		var limpo = MedirAsBocas(mundo, IdDosHomonimos, out string comoLimpo);
		for (int id = IdDosHomonimos; id < IdDosHomonimos + 4; id++) _homonimosNaTela.Add(id);

		Conferir(limpo.Homonimos,
				 $"dois HOMONIMOS (\"{NomeDosHomonimos}\") em campo, cada um com a sua frase: cada frase abre no balao de "
			   + $"QUEM FALOU, pelo id da boca ({comoLimpo})");
		Conferir(limpo.Fusao, "a FUSAO (`PeerLook` \"Gogeta\", autor \"Goku\" no chat) fala e o balao abre sobre ela");
		Conferir(limpo.AntesDoNome, "quem fala ANTES do proprio `PeerLook` ganha o balao -- o id basta, o nome nem chegou");
		Conferir(!comDefeito.Homonimos,
				 $"(defeito injetado: o balao acha o corpo pelo NOME, o codigo de antes) a mesma regua REPROVA os "
			   + $"homonimos -- um deles fica mudo ({comoComDefeito})");
		Conferir(!comDefeito.Fusao, "(defeito injetado) ...a fusao fica sem balao");
		Conferir(!comDefeito.AntesDoNome, "(defeito injetado) ...e quem fala antes do `PeerLook` tambem");
	}

	/// <summary>
	/// UMA MEDIDA: os quatro corpos de `OsHomonimos` nascem a partir de <paramref name="id0"/> e falam.
	/// <paramref name="como"/> diz o que os balaoes de A e B mostraram. Quem chama os tira de cena.
	/// </summary>
	private (bool Homonimos, bool Fusao, bool AntesDoNome) MedirAsBocas(World mundo, int id0, out string como)
	{
		const string FraseDeA = "eu sou o primeiro", FraseDeB = "eu sou o outro", FraseDaFusao = "duas vozes numa so",
					 FraseDeD = "ainda nao me apresentei";
		int a = id0, b = id0 + 1, c = id0 + 2, d = id0 + 3;
		Vector2 aqui = World.Instancia?.PosicaoLocal ?? Vector2.Zero;

		SnapshotDe(mundo, a, aqui + new Vector2(-96, 0));
		SnapshotDe(mundo, b, aqui + new Vector2(-96, 64));
		SnapshotDe(mundo, c, aqui + new Vector2(0, 96));
		SnapshotDe(mundo, d, aqui + new Vector2(0, -96));
		mundo.AoReceberAparencia(a, NomeDosHomonimos, "Saiyan", "Male", new Jandirus.Core.Appearance.Appearance());
		mundo.AoReceberAparencia(b, NomeDosHomonimos, "Saiyan", "Male", new Jandirus.Core.Appearance.Appearance());
		mundo.AoReceberAparencia(c, "Gogeta", "Saiyan", "Male", new Jandirus.Core.Appearance.Appearance());

		mundo.AoFalar(Protocol.Fala.Diz, NomeDosHomonimos, FraseDeA, a);
		mundo.AoFalar(Protocol.Fala.Diz, NomeDosHomonimos, FraseDeB, b);
		mundo.AoFalar(Protocol.Fala.Diz, "Goku", FraseDaFusao, c);
		mundo.AoFalar(Protocol.Fala.Diz, "Recem Chegado", FraseDeD, d);

		BalaoDeFala? BalaoDe(int id) => mundo.CorpoDeTeste(id)?.GetNodeOrNull<BalaoDeFala>("Balao");
		bool Disse(int id, string frase) => BalaoDe(id) is { Visible: true } bl && bl.TextoDeTeste == frase;
		string Mostra(int id) => BalaoDe(id) is { Visible: true } bl
			? $"\"{bl.TextoDeTeste}\"{(bl.NaFilaDeTeste > 0 ? $" +{bl.NaFilaDeTeste} na fila" : "")}"
			: "mudo";

		como = $"A {Mostra(a)}, B {Mostra(b)}";
		return (Disse(a, FraseDeA) && Disse(b, FraseDeB), Disse(c, FraseDaFusao), Disse(d, FraseDeD));
	}

	/// <summary>Os corpos da medida limpa de `OsHomonimos`, ainda em campo esperando a foto.</summary>
	private readonly List<int> _homonimosNaTela = [];

	/// <summary>
	/// A FOTO DOS QUATRO, cada balao sobre quem falou (so com janela: headless nao renderiza) -- e depois eles saem de
	/// cena pelo `AoSair`, o caminho da rede: o andar e o voo que vem em seguida sao do remoto do teste.
	/// </summary>
	private void FotografarOsHomonimos(World mundo)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img != null && !img.IsEmpty())
		{
			string caminho = ProjectSettings.GlobalizePath("user://balao-3-homonimos.png");
			img.SavePng(caminho);
			Nota($"foto {caminho}: os dois homonimos, a fusao e quem falou antes do `PeerLook`, cada um com o seu balao");
		}
		foreach (int id in _homonimosNaTela) mundo.AoSair(id);
		_homonimosNaTela.Clear();
	}

	/// <summary>Um corpo parado em <paramref name="onde"/>, pelo snapshot -- o caminho por que todo corpo remoto nasce.</summary>
	private static void SnapshotDe(World mundo, int id, Vector2 onde)
		=> mundo.AoReceberSnapshot([new EntityState
		{
			Id = id,
			Pos = new Vec2(onde.X, onde.Y),
			Facing = (byte)Jandirus.Core.World.Facing.South,
			Pose = Protocol.Pose.Normal,
		}]);

	// =====================================================================
	// 11. O FIO (a fala que passa pelo servidor)
	// =====================================================================
	private void OuvirOFio(Protocol.Fala canal, string autor, string texto, int boca)
	{
		if (texto is FraseDoFio or SussurroDoFio) _bocasDoFio[texto] = boca;
	}

	/// <summary>
	/// A FALA DE VERDADE: sai pelo `SendChat`, passa pelo `GameServer.Falar` e volta no `S2C.Chat`. Todo o resto
	/// da bancada entra pelo `World.AoFalar` com o id na mao -- e o que so esta medida prova e que o servidor
	/// ESCREVE o id da boca e o cliente o LE no lugar certo (um campo lido fora de ordem ou esquecido num dos
	/// dois funis do `GameServer.Chat.cs` volta como 0 ou lixo, e o balao nao abre em ninguem).
	/// </summary>
	private void OFio(GameClient cli, BalaoDeFala balao, string frase, string oque)
	{
		int? boca = _bocasDoFio.TryGetValue(frase, out int b) ? b : null;
		Conferir(boca == cli.LocalId,
				 $"{oque} que passou pelo SERVIDOR voltou no `S2C.Chat` com o id da MINHA boca "
			   + $"({(boca is { } x ? x.ToString() : "nao chegou")}, eu sou {cli.LocalId})");
		Conferir(balao.Visible && balao.TextoDeTeste.Contains(frase),
				 $"...e {oque} virou balao no meu corpo pelo caminho do jogo (\"{balao.TextoDeTeste}\")");
	}

	// =====================================================================
	// 12. O NOME COMPRIDO NO FIO (2026-10-19)
	// =====================================================================
	/// <summary>
	/// O NOME DE 24 LETRAS, o maior que a criacao aceita (`CreationScreen`, `MaxLength = 24`): a raiz dos derivados. A letra
	/// do fim separa as medidas -- os corpos de cada uma continuam em campo ate o fim.
	/// </summary>
	private static string NomeDeCriacao(char qual) => "Fulano de Tal Comprido " + qual;

	/// <summary>
	/// O DERIVADO MAIS COMPRIDO DE UM DEGRAU, pelas receitas do jogo: o corpo (`Cadaver.NomeDo`) do droide
	/// (`SuperComputador.NomeDoDroide`, modelo de 4 algarismos) de um Bio-Androide (o batismo do `NascerBioAndroide`, que
	/// nao e funcao). Tem 74 letras: passa dos dois tetos de antes (24 e 32) e cabe no novo.
	/// </summary>
	private static string DerivadoDeUmDegrau(char qual) =>
		Cadaver.NomeDo(Jandirus.Core.Tech.SuperComputador.NomeDoDroide($"Bio-Androide de {NomeDeCriacao(qual)}",
																	   Jandirus.Core.Tech.SuperComputador.MaxDoModelo));

	/// <summary>A cadeia mais funda que o teto: tres Bio-Androides em fila (106 letras). Tem que chegar CORTADA, nunca vazia.</summary>
	private static string DerivadoAlemDoTeto(char qual) =>
		Cadaver.NomeDo(Jandirus.Core.Tech.SuperComputador.NomeDoDroide(
			$"Bio-Androide de Bio-Androide de Bio-Androide de {NomeDeCriacao(qual)}", Jandirus.Core.Tech.SuperComputador.MaxDoModelo));

	/// <summary>
	/// AS CONTAS, antes do fio: os dois derivados tem o tamanho que a regua precisa (um passa dos tetos de antes e cabe no
	/// novo; o outro passa do novo), e o corte nao parte um caractere de dois pedacos -- um emoji na beirada do teto.
	/// </summary>
	private void AsContasDoNome()
	{
		string um = DerivadoDeUmDegrau('A'), alem = DerivadoAlemDoTeto('C');
		Conferir(NomeDeCriacao('A').Length == 24, $"a raiz tem 24 letras, o maximo da criacao (\"{NomeDeCriacao('A')}\")");
		Conferir(um.Length > 32 && um.Length <= Protocol.MaxNomeNoFio,
				 $"o derivado de um degrau tem {um.Length} letras: passa dos dois tetos de antes (24 e 32) e cabe no novo "
			   + $"({Protocol.MaxNomeNoFio})");
		Conferir(alem.Length > Protocol.MaxNomeNoFio, $"a cadeia funda tem {alem.Length} letras: passa do teto novo");

		string comEmoji = new string('x', Protocol.MaxNomeNoFio - 1) + "\U0001F409" + " fim";
		string cortado = Protocol.NomeNoFio(comEmoji);
		Conferir(cortado.Length == Protocol.MaxNomeNoFio - 1 && !char.IsHighSurrogate(cortado[^1]),
				 $"o corte nao parte o emoji que cai na beirada do teto: fica com {cortado.Length} letras, e nao com meio caractere");
	}

	/// <summary>
	/// O SERVIDOR FORJA UM CORPO com este nome ao meu lado (`ForjarCorpoDeFoto`: o mundo de verdade, apresentado pela
	/// `TrocarAparencias` -- o `PeerLook` de producao). A fala vem depois, quando o nome ja chegou (`MedidaAndou`).
	/// </summary>
	private void ComecarMedida(Jandirus.Server.GameServer srv, GameClient cli, string nome, int i)
	{
		_medida = new MedidaDoNome { Enviado = nome };
		// Cada corpo num tile, a poucos tiles de mim: dentro do alcance da fala (22 tiles), e na foto.
		_medida.Id = srv.ForjarCorpoDeFoto(cli.LocalId,
										   new Vec2((2 + 2 * i) * ZoneCollision.TileSize, 2 * ZoneCollision.TileSize),
										   nome, bp: 1000, comEscada: false);
	}

	/// <summary>
	/// ANDA A MEDIDA: o `PeerLook` do corpo chegou? entao ele fala (`FalarPeloCorpoDeFoto`, o funil de toda fala de corpo).
	/// A fala chegou? acabou. O prazo fecha a medida com o que faltou como "nao chegou".
	/// </summary>
	private bool MedidaAndou()
	{
		if (!_medida.Falou && _medida.Lido != null)
		{
			Jandirus.Server.GameServer.Instance?.FalarPeloCorpoDeFoto(_medida.Id, FraseDoNomeComprido);
			_medida.Falou = true;
		}
		return (_medida.Falou && _medida.Autor != null) || _t > PrazoDoNome;
	}

	private void OuvirONome(int quem, string nome, string raca, string genero, Jandirus.Core.Appearance.Appearance ap,
							Jandirus.Core.Social.TipoDeFusao? fusao)
	{
		if (quem != 0 && quem == _medida.Id) _medida.Lido = nome;
	}

	private void OuvirOAutor(Protocol.Fala canal, string autor, string texto, int boca)
	{
		if (boca != 0 && boca == _medida.Id && texto == FraseDoNomeComprido) _medida.Autor = autor;
	}

	/// <summary>
	/// Fecha a medida com o que o mapa id -> nome do `World` responde agora: o `NomeDe`, pela porta do radio do scouter
	/// (`RadioOuvido.QuemE`) -- o mesmo mapa que o rotulo do alvo e a aba People leem.
	/// </summary>
	private MedidaDoNome FecharMedida(World mundo)
	{
		MedidaDoNome m = _medida;
		m.NoMapa = mundo.GetNodeOrNull<RadioOuvido>("RadioOuvido")?.QuemE?.Invoke(m.Id);
		_medida = new MedidaDoNome();
		return m;
	}

	/// <summary>
	/// A FOTO DO NOME COMPRIDO (so com janela): os corpos falando, com o balao, e as linhas do chat -- a da medida com o
	/// defeito do cliente sem autor, a do derivado de um degrau com o nome inteiro, a da cadeia funda com o nome cortado.
	/// </summary>
	private void FotografarONomeComprido()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty())
		{
			Nota("sem foto do nome comprido (headless nao renderiza)");
			return;
		}
		string caminho = ProjectSettings.GlobalizePath("user://balao-4-nome-comprido.png");
		img.SavePng(caminho);
		Nota($"foto {caminho}: as falas dos corpos de nome comprido e as linhas do chat com o autor de cada uma");
	}

	/// <summary>
	/// O NOME DERIVADO NO FIO. A criacao limita o nome a 24 letras, e o cliente lia o `PeerLook` com 24 e o autor do chat
	/// com 32 -- mas o servidor batiza corpos derivados ("corpo de X", "Bio-Androide de X", o droide) que passam disso, e o
	/// `GetString(max)` do LiteNetLib devolve VAZIO acima do teto. Hoje as duas pontas usam o `Protocol.MaxNomeNoFio`: quem
	/// le pergunta com ele, quem escreve corta nele.
	///
	/// Corpos DE VERDADE do servidor do `--host`, apresentados pelo `PeerLook` de producao e falando pelo funil de toda fala
	/// de corpo: os dois pacotes saem do servidor e sao lidos pelo `GameClient`, como em jogo. Confere-se o que o leitor
	/// entregou (`PeerLooked`, `FalouPelaBoca`) e o que o mapa id -> nome do `World` responde depois.
	///
	/// Cada defeito tem a SUA medida, com corpo e pacotes proprios -- o do cliente (os tetos de antes) e o do servidor (sem o
	/// corte) --, e nas duas a regua da medida limpa tem que reprovar pelo motivo certo: o pacote CHEGOU, e chegou vazio.
	/// </summary>
	private void ONomeComprido()
	{
		static string Mostrar(string? s) => s == null ? "nao chegou" : s.Length == 0 ? "VAZIO" : $"\"{s}\" ({s.Length} letras)";
		MedidaDoNome um = _umDegrau ?? new(), alem = _alemDoTeto ?? new();
		MedidaDoNome noCliente = _comDefeitoNoCliente ?? new(), noServidor = _comDefeitoNoServidor ?? new();
		string comeco = Protocol.NomeNoFio(alem.Enviado);

		Conferir(um.Lido == um.Enviado,
				 $"o nome DERIVADO de {um.Enviado.Length} letras chega INTEIRO no `PeerLook` (lido: {Mostrar(um.Lido)})");
		Conferir(um.NoMapa == um.Enviado,
				 "...e o mapa id -> nome do `World` o guarda: o rotulo do alvo, a aba People e o radio do scouter dizem o nome, "
			   + $"e nao \"#id\" nem \"alguem\" (o mapa responde {Mostrar(um.NoMapa)})");
		Conferir(um.Autor == um.Enviado, $"...e a fala dele chega no chat COM o autor (autor lido: {Mostrar(um.Autor)})");
		Conferir(alem.Lido == comeco && comeco.Length == Protocol.MaxNomeNoFio,
				 $"o nome de {alem.Enviado.Length} letras, alem do teto, chega CORTADO nele -- as {Protocol.MaxNomeNoFio} primeiras "
			   + $"letras --, e nao vazio (lido: {Mostrar(alem.Lido)})");
		Conferir(alem.Autor == comeco, $"...e o autor da fala dele tambem (autor lido: {Mostrar(alem.Autor)})");

		Conferir(noCliente.Lido == "",
				 "(defeito injetado: o cliente le com os tetos de antes, 24 e 32) a mesma regua REPROVA: o nome de "
			   + $"{noCliente.Enviado.Length} letras chega VAZIO no `PeerLook` (lido: {Mostrar(noCliente.Lido)})");
		Conferir(noCliente.NoMapa == "",
				 $"(defeito injetado) ...o mapa do `World` fica sem nome -- o \"#id\", o \"alguem\" (responde {Mostrar(noCliente.NoMapa)})");
		Conferir(noCliente.Autor == "",
				 $"(defeito injetado) ...e a linha do chat sai sem autor (autor lido: {Mostrar(noCliente.Autor)})");
		Conferir(noServidor.Lido == "",
				 "(defeito injetado: o servidor escreve sem o corte) a regua do nome alem do teto REPROVA: ele chega VAZIO "
			   + $"(lido: {Mostrar(noServidor.Lido)})");
		Conferir(noServidor.Autor == "",
				 $"(defeito injetado) ...e o autor da fala tambem (autor lido: {Mostrar(noServidor.Autor)})");
	}

	private void OVoadorSubiu(World mundo)
	{
		if (Remoto(mundo) is not { } r)
		{
			Conferir(false, "o corpo remoto continua em campo pra medir a subida");
			return;
		}

		float esperado = -AlturaDoTeste * Voo.EscalaNaTela;
		float visual = r.GetNode<CharacterVisual>("Visual").Position.Y;
		BalaoDeFala dele = r.GetNode<BalaoDeFala>("Balao");

		Conferir(Mathf.Abs(visual - esperado) < 1f,
				 $"o corpo remoto subiu {AlturaDoTeste:0} de altitude ({visual:0.#} px de desenho)");

		// ============================ A SOMA, E NAO A SUBSTITUICAO ============================
		// Este e o teste que o defeito antigo teria reprovado: o balao TEM altura propria sobre a
		// cabeca, e quem escrevia `Position = deslocamento` cru a apagava -- ele ia parar no umbigo
		// de quem voa, exatamente onde o corpo NAO esta.
		// ==================================================================================
		Conferir(Mathf.Abs(dele.Position.Y - (BalaoDeFala.AlturaBase + esperado)) < 1f,
				 $"o BALAO subiu junto E manteve a altura propria ({dele.Position.Y:0.#} px, esperado "
			   + $"{BalaoDeFala.AlturaBase + esperado:0.#})");

		Conferir(dele.Visible && dele.IsVisibleInTree(),
				 $"e o texto do voador continua na tela durante a subida (\"{dele.TextoDeTeste}\")");
		Nota($"{AlturaDoTeste:0} px de altitude sao o andar {Voo.Andar(AlturaDoTeste)} de {Voo.Andares}, "
		   + "que quem esta no chao ainda enxerga");

		// ============================ QUEM VOA ALTO SOME -- E O TEXTO VAI JUNTO ============================
		// A regra e do `World.AoReceberSnapshot` e vale pro CORPO; o balao herda por ser filho. Vale
		// conferir porque a alternativa (balao numa camada de tela, ver `Seguir`) daria justamente
		// isto: um retangulo de texto pairando sozinho sobre o nada, com o dono invisivel.
		// ==========================================================================================
		Snapshot(mundo, _ondeORemoto, Voo.AlturaMaxima * 0.9f, andando: false);
		Conferir(!r.Visible && !dele.IsVisibleInTree(),
				 $"subindo pro andar {Voo.Andar(Voo.AlturaMaxima * 0.9f)} ele some de vista -- e o "
			   + "balao some junto (nao fica texto orfao pairando)");

		// ============================ E QUANDO ELE SAI DE CENA ============================
		// `AoSair` e o par do snapshot: apaga o corpo e o nome. Falar em nome de quem ja saiu nao
		// pode cair em ninguem -- e o caso que uma busca "pelo primeiro corpo" transformaria em
		// balao na cabeca do vizinho.
		// ============================================================================
		mundo.AoSair(IdDoRemoto);
		mundo.AoFalar(Protocol.Fala.Diz, NomeDoRemoto, "ainda estou aqui?", IdDoRemoto);
		Conferir(Remoto(mundo) == null, "quem sai de cena leva o corpo (e o balao) junto");
	}

	// =====================================================================
	// 5. A CINEMATICA DE OUTRA PESSOA
	// =====================================================================
	/// <summary>
	/// A DIVIDA ANOTADA: as falas da cinematica so chegavam a QUEM SE TRANSFORMA, porque "o cliente
	/// nao tem mapa de id -> nome". O mapa existe (`World._nomes`, escrito pelo `PeerLook`), e o
	/// balao nem precisa dele -- ele mora no corpo.
	///
	/// Rodada com `souEu: false` de proposito: e o caso do ESPECTADOR, o unico que estava quebrado.
	/// O relogio e adiantado na mao ate o primeiro beat que fala; esperar os segundos de verdade
	/// custaria meio minuto de bancada pra medir a mesma coisa.
	/// </summary>
	private void ACinematicaAlheia(Node2D corpo, BalaoDeFala balao)
	{
		// O SSJ3, e nao o SSJ1: a cena do SSJ1 nao tem UMA fala sequer (e so efeito), e foi ela que a
		// primeira rodada desta bancada escolheu -- reprovando por escolher a cena errada. As falas
		// canonicas ("AINDA MAIS ALEM!") moram na do SSJ3.
		if (Jandirus.Core.Forms.Catalogo.Def("ssj3") is not { } def)
		{
			Conferir(false, "achei a forma `ssj3` no catalogo");
			return;
		}

		Jandirus.Core.Forms.Cinematica cena = Jandirus.Core.Forms.Cinematicas.Ssj3;
		int i = Array.FindIndex(cena.Beats, b => b.Fala.Length > 0);
		if (i < 0) { Conferir(false, "a cena do SSJ3 tem alguma fala"); return; }

		Jandirus.Core.Forms.Beat fala = cena.Beats[i];
		Transformacao t = Transformacao.Rodar(corpo.GetParent(), corpo, def, cena,
											  souEu: false, nome: "Fulano");
		t._Process(fala.Em + 0.05);

		// COMECO, e nao a frase inteira: a primeira fala do SSJ3 tem 88 letras e o balao para em
		// tres linhas -- ele MOSTRA o comeco e reticencia, e a frase inteira sai no chat. Exigir o
		// texto completo aqui reprovaria o teto de linhas, que e o que impede a torre sobre a
		// cabeca. (Foi o que a primeira rodada fez.)
		string comeco = fala.Fala[..Math.Min(30, fala.Fala.Length)];
		Conferir(balao.Visible && balao.TextoDeTeste.StartsWith(comeco),
				 $"a fala da cinematica ALHEIA virou balao pra quem assiste (\"{balao.TextoDeTeste}\") "
			   + $"-- o beat de {fala.Em:0.#}s comeca com \"{comeco}\"");
		Conferir(balao.TextoDeTeste.Length < fala.Fala.Length && balao.LinhasDeTeste == BalaoDeFala.MaxLinhas,
				 "...e a fala longa da cena obedece o MESMO teto de linhas que a do chat");

		Nota($"a cena tem {Array.FindAll(cena.Beats, b => b.Fala.Length > 0).Length} falas e "
		   + $"{Array.FindAll(cena.Beats, b => b.Narra.Length > 0).Length} narracoes; so as falas viram "
		   + "balao (narracao descreve o MUNDO, nao a boca de ninguem)");

		t.Free();
	}

	// =====================================================================
	// =====================================================================
	// 10. NITIDO EM QUALQUER ZOOM
	// =====================================================================
	/// <summary>
	/// POR CONSTRUCAO: em cada zoom o no encolhe por 1/zoom e a fonte cresce por zoom -- um pixel
	/// local e um pixel da tela --, a caixa fica dentro da mesma largura de MUNDO de sempre, a frase
	/// media continua quebrando, e um balao ja aberto acompanha a troca de zoom sem fala nova.
	/// Medido em baloes soltos, como a quebra de linha, e no zoom que a camera esta usando de verdade.
	/// </summary>
	private void ONitidoPorConstrucao(World mundo, BalaoDeFala doCorpo)
	{
		var soltos = new List<BalaoDeFala>();
		float larguraNoZoom2 = 0;
		foreach (int z in new[] { 2, 3, 6 })
		{
			mundo.AplicarZoom(z);
			var b = new BalaoDeFala { Name = $"BalaoSolto{z}" };
			AddChild(b);
			soltos.Add(b);
			b.Dizer(Protocol.Fala.Diz, FraseDaFoto);
			Conferir(b.ZoomDeTeste == z && Mathf.IsEqualApprox(b.EscalaDeTeste * z, 1f) && b.TamanhoDaFonteDeTeste == 8 * z,
					 $"zoom {z}: o balao encolhe por 1/{z} e escreve com a fonte em {8 * z} px -- um pixel do no e um pixel da tela "
				   + $"(escala {b.EscalaDeTeste:0.###}, fonte {b.TamanhoDaFonteDeTeste})");
			Conferir(b.LarguraDeTeste <= BalaoDeFala.LarguraMaxima && b.LinhasDeTeste >= 2,
					 $"zoom {z}: a frase quebra em {b.LinhasDeTeste} linhas dentro dos {BalaoDeFala.LarguraMaxima:0} px de mundo "
				   + $"({b.LarguraDeTeste:0} px de mundo = {b.CaixaNaTelaDeTeste.X:0} px de tela)");
			if (z == 2) larguraNoZoom2 = b.CaixaNaTelaDeTeste.X;
			else Conferir(Mathf.Abs(b.CaixaNaTelaDeTeste.X / larguraNoZoom2 - z / 2f) < 0.2f,
						  $"zoom {z}: a caixa na tela cresce com o zoom ({b.CaixaNaTelaDeTeste.X:0} px contra {larguraNoZoom2:0} no zoom 2, "
						+ $"razao {b.CaixaNaTelaDeTeste.X / larguraNoZoom2:0.00} de {z / 2f:0.00})");
		}
		// UM BALAO ABERTO ACOMPANHA A TROCA DE ZOOM: o do zoom 6 continua na tela; a camera vai pra 2.
		BalaoDeFala aberto = soltos[^1];
		mundo.AplicarZoom(2);
		aberto._Process(0.001);
		Conferir(aberto.ZoomDeTeste == 2 && aberto.TamanhoDaFonteDeTeste == 16,
				 $"um balao ja aberto acompanha a troca de zoom no quadro seguinte, sem fala nova (agora zoom {aberto.ZoomDeTeste}, fonte {aberto.TamanhoDaFonteDeTeste})");
		Conferir(Mathf.IsEqualApprox(doCorpo.Position.X, 0) && doCorpo.Position.Y <= BalaoDeFala.AlturaBase + 0.01f,
				 "o balao do corpo continua na altura propria sobre a cabeca (a escala nao mexe na posicao)");
		BalaoDeFala.NitidoDeTeste = false;
		aberto._Process(0.001);
		Conferir(aberto.ZoomDeTeste == 1 && Mathf.IsEqualApprox(aberto.EscalaDeTeste, 1f) && aberto.TamanhoDaFonteDeTeste == 8,
				 "[injecao] com o interruptor desligado o balao volta aos pixels de mundo (escala 1, fonte de 8 px esticada pela camera)");
		BalaoDeFala.NitidoDeTeste = true;
		foreach (BalaoDeFala b in soltos) b.QueueFree();
	}

	/// <summary>
	/// NA FOTO: o retangulo do texto do balao e recortado da tela e cada linha e varrida em busca de
	/// TRACOS CLAROS (a tinta e branca; a moldura e o contorno sao escuros). Uma letra de 8 px esticada
	/// pela camera com o filtro nearest so tem tracos de largura multipla do zoom -- cada pixel do
	/// glifo vira um bloco de zoom x zoom. A letra rasterizada no tamanho da tela tem tracos de toda
	/// largura. A regua e a fracao de tracos cuja largura NAO e multiplo do zoom; -1 = sem foto
	/// (headless nao renderiza). A foto fica em `user://` pro olho do dono.
	/// </summary>
	private double MedirAFoto(BalaoDeFala balao, string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty())
		{
			Nota("sem foto (headless nao renderiza): a nitidez das letras fica pro olho do dono -- rode com janela");
			return -1;
		}
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		Transform2D t = balao.GetGlobalTransformWithCanvas();
		Rect2 r = balao.TextoNaTelaDeTeste;
		Vector2 canto = t.Origin + t.BasisXform(r.Position);
		Vector2 tam = t.BasisXform(r.Size);
		int x0 = Mathf.RoundToInt(canto.X), y0 = Mathf.RoundToInt(canto.Y);
		int w = Mathf.RoundToInt(tam.X), h = Mathf.RoundToInt(tam.Y);
		int bloco = Math.Max(1, Mathf.RoundToInt(World.GradeDeDesenho));
		int xa = Math.Max(0, x0), xb = Math.Min(img.GetWidth(), x0 + w);
		int ya = Math.Max(0, y0), yb = Math.Min(img.GetHeight(), y0 + h);

		// A REGUA E RELATIVA: o ceu tinge o mundo inteiro (de dia o branco do texto sai rosado, de
		// madrugada azul-escuro), entao "claro" e o que esta acima de 60% do caminho entre o pixel
		// mais escuro (a moldura) e o mais claro (o miolo da letra) DENTRO do retangulo do texto.
		float menor = 1f, maior = 0f;
		for (int y = ya; y < yb; y++)
			for (int x = xa; x < xb; x++)
			{
				float lum = Luminancia(img.GetPixel(x, y));
				if (lum < menor) menor = lum;
				if (lum > maior) maior = lum;
			}
		float corte = menor + 0.6f * (maior - menor);

		int tracos = 0, foraDoBloco = 0;
		for (int y = ya; y < yb; y++)
		{
			int run = 0;
			for (int x = xa; x <= xb; x++)
			{
				bool claro = x < xb && Luminancia(img.GetPixel(x, y)) > corte;
				if (claro) { run++; continue; }
				if (run > 0) { tracos++; if (run % bloco != 0) foraDoBloco++; run = 0; }
			}
		}
		Nota($"foto {caminho}: texto em ({x0},{y0}) {w}x{h} px de tela ({img.GetWidth()}x{img.GetHeight()}), "
		   + $"luminancia de {menor:0.00} a {maior:0.00} (corte {corte:0.00}), {tracos} tracos claros, {foraDoBloco} fora do bloco de {bloco}");
		return tracos == 0 || maior - menor < 0.08f ? -1 : foraDoBloco / (double)tracos;
	}

	private static float Luminancia(Color c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

	private void Fechar()
	{
		_acabou = true;
		foreach (string p in _passos) GD.Print("[balao] " + p);
		if (_falhas.Count == 0) GD.Print("[balao] ===== TUDO OK =====");
		else
		{
			GD.Print($"[balao] ===== {_falhas.Count} FALHA(S) =====");
			foreach (string f in _falhas) GD.Print("[balao]   - " + f);
		}
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
