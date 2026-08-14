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
///   * a fala acha o CORPO CERTO? O pacote traz NOME e o cliente so tem id -- a busca reversa e
///     uma regra inteira, e ela so e exercitada de verdade por um corpo que NAO E O MEU (o meu
///     tem atalho pelo `LocalName` e nunca encosta no mapa `_nomes`).
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

	// O que o balao do LOCAL deve continuar mostrando quando a fala e de outra pessoa.
	private const string FraseDoLocal = "eu ando e falo";
	private const string FraseDoRemoto = "estou aqui do outro lado";
	private const string FraseNoAr = "estou aqui em cima";

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
				mundo.AoFalar(Protocol.Fala.Diz, cli.LocalName, "sai da frente");
				Conferir(balao.Visible && balao.TextoDeTeste == "sai da frente",
						 $"a fala achou o meu corpo pelo NOME e virou balao (\"{balao.TextoDeTeste}\")");

				mundo.AoFalar(Protocol.Fala.Ooc, cli.LocalName, "isto e fora do personagem");
				mundo.AoFalar(Protocol.Fala.Looc, cli.LocalName, "isto tambem");
				mundo.AoFalar(Protocol.Fala.Sistema, "", "zona carregada");
				mundo.AoFalar(Protocol.Fala.Sussurro, cli.LocalName, "");
				Conferir(balao.TextoDeTeste == "sai da frente" && balao.NaFilaDeTeste == 0,
						 "OOC, LOOC, Sistema e o sussurro-sem-texto NAO viram balao (nem entram na fila)");

				mundo.AoFalar(Protocol.Fala.Diz, "Ninguem Com Esse Nome", "oi");
				Conferir(balao.TextoDeTeste == "sai da frente",
						 "fala de nome desconhecido nao encosta em corpo nenhum");

				// A SEGUNDA FRASE, DENTRO DO PISO: tem que ESPERAR, e nao apagar a primeira.
				mundo.AoFalar(Protocol.Fala.Diz, cli.LocalName, "vou te acertar");
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
				mundo.AoFalar(Protocol.Fala.Diz, cli.LocalName, FraseDoLocal);
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
				if (_t < 1.0) return;

				Conferir(_andou > 24f,
						 $"o corpo remoto andou pelos snapshots ({_andou:0} px de caminho)");
				Conferir(_pior < 0.01f,
						 $"e o balao dele foi junto (pior desvio {_pior:0.###} px)");
				// A SUBIDA COMECA: ele fala de novo, pra que haja texto na tela DURANTE o voo.
				mundo.AoFalar(Protocol.Fala.Diz, NomeDoRemoto, FraseNoAr);
				Snapshot(mundo, _ondeORemoto, AlturaDoTeste, andando: false);
				Passar();
				break;

			// -------------------------------------------------------------
			// SOBE COM QUEM VOA -- pelo snapshot, que e por onde a altitude entra
			// -------------------------------------------------------------
			case 8:
				if (_t < 1.5) return;
				OVoadorSubiu(mundo);
				Passar();
				break;

			// -------------------------------------------------------------
			// A FALA DE CINEMATICA -- a divida que dizia "o cliente nao tem nome"
			// -------------------------------------------------------------
			case 9:
				ACinematicaAlheia(corpo, balao);
				Fechar();
				break;
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
	/// Este e o caso que o balao existe pra resolver -- e o unico que exercita a busca reversa
	/// nome -> id: o meu proprio nome tem atalho (`LocalName`) e nunca chega a olhar o mapa.
	///
	/// A ORDEM E DE PROPOSITO: o corpo entra em campo pelo snapshot ANTES do `PeerLook`, que e o
	/// que acontece em jogo (o snapshot e por tique, a aparencia chega uma vez). Falar nesse vao
	/// nao pode achar corpo nenhum -- se achasse, seria sinal de que a busca esta pegando "o
	/// primeiro remoto que houver" em vez do dono do nome.
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

		mundo.AoFalar(Protocol.Fala.Diz, NomeDoRemoto, "ainda nao me apresentei");
		Conferir(!dele.Visible,
				 "antes do `PeerLook` o nome nao existe e a fala nao acha corpo nenhum "
			   + "(a busca e pelo NOME, e nao \"o primeiro remoto que houver\")");

		// O PeerLook: e ele que escreve o mapa id -> nome de que a busca reversa vive.
		mundo.AoReceberAparencia(IdDoRemoto, NomeDoRemoto, "Saiyan", "Male",
								 new Jandirus.Core.Appearance.Appearance());

		mundo.AoFalar(Protocol.Fala.Diz, NomeDoRemoto, FraseDoRemoto);
		Conferir(dele.Visible && dele.TextoDeTeste == FraseDoRemoto,
				 $"depois do `PeerLook` a fala dele acha o corpo DELE (\"{dele.TextoDeTeste}\")");
		Conferir(meuBalao.TextoDeTeste != FraseDoRemoto,
				 "...e nao caiu no MEU balao (dois corpos em campo, cada frase no dono certo)");

		Comecar(r);
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
		mundo.AoFalar(Protocol.Fala.Diz, NomeDoRemoto, "ainda estou aqui?");
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
