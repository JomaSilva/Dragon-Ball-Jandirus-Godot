using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O personagem QUE VOCE controla. Movimento LIVRE em pixel, calculado localmente (por isso
/// nao ha latencia nenhuma no controle) e enviado ao servidor, que confere.
///
/// A COLISAO E A MESMA DAS DUAS PONTAS: cliente e servidor chamam <see cref="MoveRules"/>
/// sobre o mesmo <see cref="ZoneCollision"/>. Sem isso o cliente atravessa a parede, o
/// servidor recusa, manda correcao, o cliente empurra de novo -- e o sintoma que chega ao
/// jogador e o personagem TREMENDO no muro.
///
/// POSICAO EXATA x POSICAO DESENHADA. Sao duas, de proposito:
///
///   _pos      -- float cru, e a VERDADE da simulacao. Vai pro servidor e pro MoveRules.
///   Position  -- o mesmo valor CHAO, e so isso o motor desenha.
///
/// A razao e o `snap_2d_transforms_to_pixel`: ele arredonda a transformacao de cada objeto
/// na hora de desenhar, mas NAO arredonda a transformacao de canvas que a camera gera. Como
/// a camera e FILHA deste no e le a posicao fracionaria, o corpo pulava de pixel inteiro em
/// pixel inteiro enquanto o mundo rolava em subpixel: o boneco oscilava ate 1 px de mundo
/// (3 px de tela no zoom 3) a cada quadro. Escrevendo ja inteiro no no, a camera herda o
/// MESMO inteiro e nada mais se desloca em relacao a nada.
/// </summary>
public partial class LocalPlayer : Node2D
{
	[Export] public float SpeedStat = 1f;

	/// <summary>A geometria da zona. Nulo = zona sem colisao carregada (anda livre).</summary>
	public ZoneCollision? Mapa;

	private CharacterVisual _visual = null!;
	private Facing _facing = Facing.South;
	private Vec2 _pos;
	private double _sendAccumulator;
	private const double SendInterval = 1.0 / 30.0; // manda estado na mesma taxa do tick do servidor

	private Protocol.Activity _atividade = Protocol.Activity.Parado;
	private double _ataqueAte;
	private bool _guarda;

	/// <summary>
	/// SHIFT segurado. E um PEDIDO: o servidor concede enquanto houver Ki e cobra por segundo
	/// (ver `GameServer.PodeCorrer`). Andar mais rapido do que ele concedeu so gera correcao,
	/// entao quando o Ki acaba o cliente volta sozinho ao passo normal -- a ficha avisa.
	/// </summary>
	private bool _correndo;

	/// <summary>
	/// A CADENCIA DO SOCO, em segundos, dita pelo SERVIDOR (chega na ficha). Nao e uma
	/// constante porque nao e fixa: ela sai do `Eactspeed`, que cai quando o personagem
	/// carrega Ki -- carregar poder deixa a luta mais rapida, nao so mais forte. Usar o
	/// numero do servidor tambem garante que este cliente nunca tente um golpe que la vai
	/// ser recusado por recarga.
	/// </summary>
	private double _cadencia = Protocol.AttackPoseMs / 1000.0;

	public override void _Ready()
	{
		_visual = GetNode<CharacterVisual>("Visual");
		_pos = new Vec2(Position.X, Position.Y);   // o World ja nasceu com o spawn no construtor
		Desenhar();

		if (GameClient.Instance is not { } cli) return;

		cli.Corrected += OnCorrected;
		// A velocidade vem da FICHA que o servidor calculou. Andar mais rapido do que ele
		// concedeu so gera correcao, entao nao existe motivo pra divergir.
		cli.SheetUpdated += OnSheet;
		if (cli.Sheet.SpeedStat > 0) SpeedStat = cli.Sheet.SpeedStat;
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is not { } cli) return;
		cli.Corrected -= OnCorrected;
		cli.SheetUpdated -= OnSheet;
	}

	private void OnSheet(SheetState ficha)
	{
		if (ficha.SpeedStat > 0) SpeedStat = ficha.SpeedStat;
		if (ficha.SocoMs > 0) _cadencia = ficha.SocoMs / 1000.0;
		bool caiuAgora = ficha.Imobilizado && !_caido;
		_caido = ficha.Imobilizado;
		// DEITA PRA ONDE ESTAVA OLHANDO. So no instante da queda: girar todo pacote de ficha
		// deixaria o corpo caido acompanhando a direcao, que nao e o que um desmaio faz.
		if (caiuAgora) _visual.DeitarPor(_facing);
		else if (!ficha.Imobilizado && !ficha.Empurrado) _visual.GirarPara(default);
		_empurrado = ficha.Empurrado;   // o servidor esta dirigindo o corpo: ver _Process
		_visual.MostrarRabo(ficha.Rabo);
		// 3% de folga sobre o custo de um segundo de corrida: no fio do Ki, correr e desistir
		// a cada quadro daria um solavanco a cada passo
		_temKiPraCorrer = ficha.MaxKi <= 0 || ficha.Ki > ficha.MaxKi * 0.03;

		// A FICHA NAO ACENDE MAIS AURA NENHUMA.
		//
		// Ela acendia -- `Definir(_carregando || _sobrecarregado, ...)` -- e esse era o SEGUNDO
		// ponto do defeito que o dono viu: mesmo consertando a tecla, o proximo pacote de ficha
		// (varios por segundo) religava a luz que o servidor tinha negado. Quem sabe se a carga
		// esta acontecendo e o servidor, e ele ja avisa pelo canal de efeito.
	}

	/// <summary>Nocauteado ou morto: o servidor recusa qualquer passo, entao aqui nem se tenta.</summary>
	private bool _caido;

	/// <summary>Estou sendo ARREMESSADO -- quem move o corpo agora e o servidor.</summary>
	private bool _empurrado;

	/// <summary>Ainda ha Ki pro servidor conceder a corrida.</summary>
	private bool _temKiPraCorrer = true;

	/// <summary>
	/// PRA ONDE O PILOTO AUTOMATICO ESTA INDO (nulo = ninguem pilotando). E o nav system: o
	/// jogador escolhe um planeta na aba Nav e o corpo vai sozinho, no passo normal.
	/// </summary>
	public Vec2? Destino;

	/// <summary>SHIFT segurado AGORA -- o modificador do golpe, independente de estar andando.</summary>
	private bool _shift;

	/// <summary>
	/// Anda no QUADRO DE RENDER, nao no passo de fisica.
	///
	/// Este no nao usa fisica do Godot (a colisao e nossa, por mapa de bits), e integrar a
	/// 60 Hz fixo enquanto a tela desenha noutra cadencia faz alguns quadros receberem dois
	/// passos e outros nenhum -- tremor de amostragem, visivel como engasgo. Uma atualizacao
	/// por quadro desenhado resolve na raiz. O `Advance` ja e por dt, com teto pra alt-tab.
	/// </summary>
	public override void _Process(double delta)
	{
		// ESCREVENDO NO CHAT NAO SE JOGA. `Input.IsActionPressed` le a TECLA FISICA e nao sabe
		// que ha um campo de texto com foco -- sem esta guarda, escrever "sai da frente" faz o
		// personagem andar pra direita (D), treinar (T) e mirar na cabeca (1).
		// CARREGANDO NAO SE ANDA -- e nao e "rende menos", e nao anda.
		//
		// A versao anterior so SUSPENDIA o ganho em movimento, e o dono voltou: "ao apertar C o
		// personagem tem q ficar PARADO e n pode se mexer enquanto carrega". Ele esta certo e o
		// motivo e de leitura: um personagem que anda enquanto "carrega" nao mostra que a carga
		// parou -- ele parece estar carregando andando, que e justamente o que a regra proibe.
		// Travar o corpo faz a regra ser OBVIA sem uma linha de texto.
		var input = _caido || _carregando || Foco.Digitando
			? Vector2.Zero   // no chao nao se anda: ver OnSheet
			: new Vector2(
				Godot.Input.GetActionStrength("move_right") - Godot.Input.GetActionStrength("move_left"),
				Godot.Input.GetActionStrength("move_down") - Godot.Input.GetActionStrength("move_up"));

		var dir = new Vec2(input.X, input.Y);
		bool tentandoAndar = dir.LengthSquared > 1e-6f;

		// PILOTO AUTOMATICO (o nav system). NAO e teleporte nem velocidade extra: ele so
		// preenche a direcao que o jogador preencheria com as teclas, e o passo continua
		// passando pelo MoveRules e sendo conferido pelo servidor. Uma viagem de sete dias tem
		// que custar sete dias.
		if (!tentandoAndar && Destino is { } alvo)
		{
			Vec2 rumo = alvo - _pos;
			if (rumo.LengthSquared < 64 * 64) { Destino = null; Chat.Sistema("voce chegou."); }
			else { dir = rumo.Normalized(); tentandoAndar = true; }
		}
		else if (tentandoAndar && Destino != null)
		{
			// encostou numa tecla: assume o controle. Piloto que ignora o piloto e um sequestro.
			Destino = null;
			Chat.Sistema("piloto automatico desligado.");
		}

		// SHIFT FAZ DUAS COISAS, e elas NAO sao a mesma:
		//   * andando, ele CORRE (velocidade maior, cobrada em Ki pelo servidor);
		//   * socando, ele e o modificador do golpe PESADO / investida longa.
		// Se as duas dependessem de estar andando, segurar SHIFT parado e apertar espaco
		// daria um soco leve -- e o dono pediu justamente SHIFT+ESPACO como o golpe forte.
		_shift = !_caido && Godot.Input.IsActionPressed("run");

		// O C TEM PRIORIDADE SOBRE O SHIFT.
		//
		// O dono: "ao correr e segurar C ele ainda faz o som de carregar ki -- o certo seria ele
		// dar prioridade ao C e parar de correr e começar a carregar o ki parado". Faz sentido:
		// reunir energia exige pe plantado (o servidor ja nao rende nada em movimento), entao
		// deixar a corrida ligada punha o jogador num estado que nao produz nada e ainda gasta Ki.
		// Segurar C desliga a corrida; soltar devolve.
		bool querCorrer = _shift && tentandoAndar && _temKiPraCorrer && !_carregando;
		if (querCorrer && !_correndo) AudioDirector.EfeitoNoLugar(this, Trilha.Dash, 0.5f);
		_correndo = querCorrer;

		// ARREMESSADO NAO DIRIGE. Enquanto o servidor esta jogando o corpo, o cliente para de
		// integrar o input e so segue as correcoes -- senao os dois empurram o mesmo corpo em
		// direcoes diferentes, que e a briga que faz o personagem TREMER.
		// ARREMESSADO: o corpo ANDA sozinho na direcao do voo, no cliente, em vez de esperar a
		// correcao de cada tique.
		//
		// A primeira versao so travava o input e deixava o servidor teleportar o corpo a cada 0,1 s.
		// Funcionava e ficava HORRIVEL -- dez saltos de dois tiles em vez de um voo. O dono: "o
		// knock back n ta fluido, o personagem voa meio travado".
		//
		// Agora o cliente INTERPOLA: guarda o rumo que a ultima correcao revelou e desliza nele, e a
		// correcao seguinte so ajusta o erro. O servidor continua sendo o dono da posicao -- ele so
		// parou de ser o unico a mover o corpo.
		if (_empurrado)
		{
			_pos += _rumoDoVoo * (float)(delta * VelocidadeDoVoo);
			Desenhar();
			_visual.SetMotion(_facing, false);
			_visual.GirarPara(_rumoDoVoo);   // o corpo voa DEITADO na direcao do arremesso
			return;
		}
		_visual.GirarPara(default);   // fora do voo, o sprite volta ao prumo

		Vec2 antes = _pos;
		_pos = MoveRules.Advance(_pos, dir, (float)delta, SpeedStat, Mapa, out _, _correndo);
		Desenhar();

		// ANDANDO = saiu do lugar, nao = apertou a tecla. Empurrando a parede o personagem
		// fica parado de pe em vez de marchar sem sair do lugar.
		bool andando = (_pos - antes).LengthSquared > 0.01f;

		if (tentandoAndar) _facing = MoveRules.FacingFrom(dir, _facing);
		_visual.SetMotion(_facing, andando);

		// O BORRAO SEGUE A INTENCAO (`_correndo`), e nao o deslocamento quadro a quadro.
		//
		// ============================ POR QUE MUDOU ============================
		// A versao anterior usava `_correndo && andando`, com o argumento de que rastro saindo de
		// quem nao saiu do lugar nao faz sentido. O argumento e bom e o efeito colateral era pior:
		// `andando` e `(_pos - antes) > 0,01 px`, que PISCA -- um quadro curto, uma quina de
		// parede, um passo diagonal raspando. Cada piscada zerava o alvo do borrao e a subida/
		// descida (0,10 s / 0,22 s) transformava isso num pulso visivel. Foi o que o dono viu:
		// "ele da umas piscadinhas".
		//
		// `_correndo` ja exige tecla de direcao E Ki (`_shift && tentandoAndar && _temKiPraCorrer`),
		// entao empurrar parede segurando SHIFT ainda mostra rastro -- mas isso e um estado raro e
		// visivelmente travado, enquanto a piscada acontecia correndo em linha reta.
		// =======================================================================
		Vec2 desl = _pos - antes;
		_visual.Correr(_correndo, new Vector2(desl.X, desl.Y));

		// O RASTRO segue o deslocamento REAL: parado empurrando parede nao deixa rastro, porque
		// rastro e do corpo que passou por um lugar.
		if (GetNodeOrNull<RastroDeCorrida>("Rastro") is { } rastro) rastro.Definir(_correndo && andando);

		LerAcoes(tentandoAndar, delta);

		_sendAccumulator += delta;
		if (_sendAccumulator >= SendInterval)
		{
			_sendAccumulator -= SendInterval;
			GameClient.Instance?.SendState(_pos, _facing, andando, _correndo);   // o servidor recebe o EXATO
		}
	}

	/// <summary>O no fica sempre em pixel inteiro -- ver o cabecalho da classe.</summary>
	private void Desenhar() => Position = new Vector2(MathF.Floor(_pos.X), MathF.Floor(_pos.Y));

	/// <summary>
	/// T treina, M medita, ESPACO soca. O cliente so DECLARA -- quem soma BP (e mais tarde
	/// quem calcula o dano) e o servidor. A animacao roda na hora pra nao ter atraso no
	/// controle; o servidor confirma pros OUTROS verem.
	/// </summary>
	private void LerAcoes(bool andando, double delta)
	{
		if (_ataqueAte > 0) _ataqueAte -= delta;

		if (_caido)
		{
			// caido: a guarda cai junto, e a pose vem do servidor como qualquer outra
			if (_guarda) { _guarda = false; GameClient.Instance?.SendGuard(false); }
			_visual.SetState("ko");
			return;
		}

		// GUARDA. Segurar ALT ergue o braco; erguer a guarda no instante do golpe vira
		// contra-ataque, e por isso ela e um estado continuo e nao um toque.
		bool guardaAgora = Godot.Input.IsActionPressed("guard") && !andando && !Foco.Digitando;
		if (guardaAgora != _guarda)
		{
			_guarda = guardaAgora;
			GameClient.Instance?.SendGuard(_guarda);
		}

		if (!Foco.Digitando) LerMira();

		if (!Foco.Digitando) LerTeclaC(delta);
		else if (_carregando) { _carregando = false; GameClient.Instance?.SendCarregar(false); }

		if (!Foco.Digitando && Godot.Input.IsActionJustPressed("reverter"))
			GameClient.Instance?.SendTransformar(false);

		// UM soco por vez. Sem esta trava, martelar o espaco re-armava o cronometro a cada
		// tecla e o personagem ficava preso na pose de soco pra sempre -- e como todo estado
		// do .dmi tem loop, o ciclo se repetia sem nunca voltar a ficar de pe.
		if (!Foco.Digitando && Godot.Input.IsActionJustPressed("attack") && _ataqueAte <= 0)
		{
			// SHIFT + ESPACO = GOLPE PESADO, e com ele a investida longa. So ESPACO = golpe
			// leve, com um passo curto pra fechar o meio metro que falta. Nao ha tecla
			// separada de "soco forte": no original o golpe so ficava pesado quando saia em
			// dash (`1 + dash_delay` virava o `Type`), e SHIFT ja e essa escolha.
			// VIRA PRO ALVO MARCADO, na tela, ANTES de socar.
			//
			// O servidor ja girava o `Facing` pelo marcado (`GameServer.Atacar`) -- mas a direcao
			// que ele calcula so viaja pros OUTROS, no snapshot. O meu proprio sprite e desenhado
			// por mim, com a direcao que saiu do MEU movimento. Sem esta linha, marcar alguem que
			// esta atras e apertar espaco desenhava o soco pro lado errado enquanto o golpe, no
			// servidor, saia certo -- as duas pontas contando historias diferentes do mesmo golpe.
			if (World.Instancia?.PosicaoDoAlvo is { } alvo)
			{
				var pAlvo = new Vec2(alvo.X - _pos.X, alvo.Y - _pos.Y);
				_facing = MoveRules.FacingFrom(pAlvo, _facing);
				_visual.SetMotion(_facing, false);
			}

			Protocol.Golpe golpe = _shift ? Protocol.Golpe.Pesado : Protocol.Golpe.Leve;
			double dura = _cadencia * Protocol.PesoDoGolpe(golpe);

			// DE ONDE EU SAIRIA, guardado pro caso de ter havido investida.
			//
			// O VULTO NAO NASCE AQUI. Nascia -- e o dono viu o defeito: apertar SHIFT+ESPACO sem
			// ninguem por perto deixava miragem parado no lugar. O cliente nao tem como saber se a
			// investida ACONTECEU: quem escolhe o alvo, cobra o Ki e testa a parede no caminho e o
			// servidor. Ele responde isso no relato do golpe (`HitEvent.Zanzo`), e o vulto sai la --
			// neste ponto, que e onde o corpo estava quando a tecla foi apertada.
			//
			// ============================ VALE PROS DOIS GOLPES ============================
			// Isto so rodava no golpe PESADO, e estava errado: o servidor chama `Aproximar` nos DOIS
			// (`longo: golpe == Pesado` -- GameServer.Combat.cs), com 160px no pesado e 80px no leve.
			// Ou seja, o soco LEVE tambem investe, tambem marca `Zanzo`, e tambem faz o cliente
			// chamar `DeixarVulto()` -- so que com a posicao guardada no ULTIMO shift+espaco.
			//
			// O dono descreveu exatamente isso: "ao apertar espaco dentro do range do tp sem o shift,
			// o efeito do zanzoken acontece no ultimo local q usei o shift+espaco, ele n ta
			// atualizando com a posiçao atual do player". A miragem podia nascer do outro lado do
			// mapa, no lugar onde ele tinha investido minutos antes.
			// ==============================================================================
			_deOndeSai = new Vector2(_pos.X, _pos.Y);

			_ataqueAte = dura;
			// a animacao e ESTICADA pra caber no golpe: com a cadencia nova (~0,33 s) o ciclo
			// que veio do .dmi (~0,67 s) nao terminaria antes do proximo soco
			_visual.RestartState("attack", dura);
			GameClient.Instance?.SendAction(golpe);
		}

		Protocol.Activity nova = _atividade;
		if (Foco.Digitando) { }   // "treinar" e "meditar" sao T e M: no meio de uma frase, nao
		else if (Godot.Input.IsActionJustPressed("train"))
			nova = _atividade == Protocol.Activity.Treinando ? Protocol.Activity.Parado : Protocol.Activity.Treinando;
		else if (Godot.Input.IsActionJustPressed("meditate"))
			nova = _atividade == Protocol.Activity.Meditando ? Protocol.Activity.Parado : Protocol.Activity.Meditando;

		if (andando) nova = Protocol.Activity.Parado;   // nao se treina correndo

		if (nova != _atividade)
		{
			_atividade = nova;
			GameClient.Instance?.SendActivity(nova);
		}

		// a pose de soco tem prioridade enquanto dura
		if (_ataqueAte > 0) return;
		// NAO existe pose de guarda nos .dmi (o corpo tem meditate, train, attack, flight, ko e
		// mais nada) -- entao guardar mostra a pose parada, e quem avisa que a guarda esta
		// erguida e o HUD. Inventar arte aqui so daria um personagem em pose errada.
		_visual.SetState(_atividade switch
		{
			Protocol.Activity.Treinando => "train",
			Protocol.Activity.Meditando => "meditate",
			_ => "default",
		});
	}

	// =====================================================================
	// A TECLA C
	// =====================================================================
	/// <summary>Segurando C agora (o `is_drawing` do original).</summary>
	private bool _carregando;

	/// <summary>Quanto falta do prazo do toque duplo. 0 = nao ha toque pendente.</summary>
	private double _duploAte;

	/// <summary>
	/// A JANELA DO TOQUE DUPLO, em segundos. E o `spawn(10) dblclk=0` do DM -- dez decimos.
	///
	/// Um segundo parece largo pra duplo clique de mouse, mas isto NAO e duplo clique: e uma tecla
	/// que tambem tem funcao de segurar, e quem toca duas vezes esta alternando entre dois gestos
	/// com o mesmo dedo. Janela curta faria o jogador falhar a transformacao e sair carregando.
	/// </summary>
	private const double JanelaDoDuplo = 1.0;

	/// <summary>
	/// C FAZ TRES COISAS, e era isso que faltava: o dono avisou que "o C não é só pra transformar".
	///
	///   segurar          reune energia (o servidor decide o quanto, ver `CargaDeKi`)
	///   tocar duas vezes tenta subir a escada de transformacao
	///   soltar           para
	///
	/// A ORDEM IMPORTA: o toque e detectado ANTES de a carga comecar, e a carga comeca no mesmo
	/// quadro. Isso deixa o gesto natural -- tocar duas vezes carrega um tiquinho no meio, que e
	/// exatamente o que acontecia no original (`Draw_Energy` chama `Energy_Draw` E conta o clique
	/// na mesma passada).
	/// </summary>
	private void LerTeclaC(double delta)
	{
		if (_duploAte > 0) _duploAte -= delta;

		if (Godot.Input.IsActionJustPressed("transformar"))
		{
			if (_duploAte > 0) { _duploAte = 0; GameClient.Instance?.SendTransformar(true); }
			else _duploAte = JanelaDoDuplo;
		}

		// SEGURAR. Caido nao carrega -- o servidor recusa de qualquer jeito, e mandar mesmo assim
		// seria um pacote por quadro pra ouvir nao.
		bool quer = !_caido && Godot.Input.IsActionPressed("transformar");
		if (quer == _carregando) return;

		_carregando = quer;
		GameClient.Instance?.SendCarregar(quer);

		// E SO ISSO. A tecla PEDE; quem decide e o servidor, e quem acende e o `World` ao receber
		// o efeito de volta (ver World.AoCairEfeito).
		//
		// A VERSAO ANTERIOR ACENDIA AQUI MESMO, com o comentario "o meu corpo nao entra no
		// snapshot, entao tem que ser ligado aqui". O raciocinio estava certo sobre o snapshot e
		// errado sobre a conclusao: o corpo local realmente nao vem no snapshot, mas a resposta
		// nao era adivinhar -- era usar o canal de efeito, que o servidor ja mandava e ninguem
		// escutava. Sem isso, apertar C sem Ki Unlocked acendia aura pra um poder que o servidor
		// tinha recusado, e o jogador ficava com luz, som e Ki parado.
	}

	/// <summary>O typepath da skill que libera a imagem remanescente (`misc.dm:35`).</summary>
	private const string PathDoZanzoken = "/datum/skill/ki/Afterimage";

	/// <summary>
	/// Sei fazer o Zanzoken?
	///
	/// O CLIENTE PODE DECIDIR ISTO SOZINHO porque a resposta nao muda nada no mundo -- ela decide
	/// se um sprite semitransparente aparece por meio segundo. A lista de skills ja chega aqui
	/// (`S2C.Skills`), entao perguntar ao servidor seria uma ida e volta de rede pra desenhar.
	///
	/// Se algum dia o vulto virar mecanica (confundir a mira do adversario, como o
	/// `Zanzoken_Afterimage` de nivel 4 faz no original), a decisao muda de lado na hora: efeito
	/// que altera o resultado da luta e do servidor, sem excecao.
	/// </summary>
	private static bool TemZanzoken() =>
		GameClient.Instance?.SkillsAprendidas.Contains(PathDoZanzoken) == true;

	/// <summary>
	/// Onde o corpo estava quando o golpe saiu. O relato do servidor chega um RTT depois, e ate la
	/// o personagem JA investiu -- desenhar o vulto na posicao do momento o poria em cima do alvo,
	/// que e o oposto de "ficou pra tras".
	/// </summary>
	private Vector2 _deOndeSai;

	/// <summary>
	/// GRAVA DE ONDE EU SAIO, pra um gesto que ainda vai ser confirmado pelo servidor.
	///
	/// O soco ja fazia isso sozinho; a PISCADA por duplo clique precisava disto e nao tinha. Ver
	/// o comentario do <see cref="DeixarVulto"/>.
	/// </summary>
	public void MarcarSaida() => _deOndeSai = new Vector2(_pos.X, _pos.Y);

	/// <summary>
	/// O servidor confirmou o deslocamento: e aqui que o vulto nasce, na posicao que o CLIENTE
	/// guardou no instante do gesto.
	///
	/// ============================ POR QUE NAO USAR A POSICAO DO SERVIDOR ============================
	/// O pacote da piscada (`S2C.Zanzo`) traz a posicao de onde o corpo saiu, e a piscada usava ELA.
	/// Duas coisas dao errado nisso, e nenhuma acontece no caminho do soco:
	///
	///   1. E UMA POSICAO ATRASADA. `pl.Pos` no servidor e a ultima que CHEGOU por pacote de input.
	///      Entre o cliente mandar o duplo clique e o servidor atender, o corpo ja andou -- entao a
	///      miragem nasce alguns pixels atras de onde o jogador realmente estava.
	///
	///   2. OS DOIS PACOTES VEM POR CANAIS DIFERENTES. A `Correction` (que move o corpo) vai no canal
	///      CONFIAVEL e o `Zanzo` (que cria a miragem) no NAO CONFIAVEL -- e o LiteNetLib nao garante
	///      ordem ENTRE canais. Ou seja, a ordem em que o corpo salta e a miragem aparece muda de
	///      pacote pra pacote, e as vezes a miragem nasce em cima do corpo que ainda nao saiu.
	///
	/// O caminho do soco nunca teve nenhum dos dois problemas porque ele NAO PERGUNTA a posicao ao
	/// servidor: o cliente guarda onde estava no instante da tecla e desenha ali. O dono notou
	/// exatamente essa assimetria -- "o tp via dash nao causa o bug visual" --, e a correcao e fazer
	/// a piscada usar o mesmo caminho, nao inventar um terceiro.
	/// ================================================================================================
	/// </summary>
	public void DeixarVulto()
	{
		if (GetParent() is { } palco) Zanzoken.Deixar(palco, this, _deOndeSai);
	}

	/// <summary>
	/// Onde mirar. A zona nao GARANTE o membro -- so pesa o sorteio a favor dele -- mas e o
	/// que permite ir atras das pernas de quem foge ou da cabeca de quem esta quase caindo.
	/// </summary>
	private static void LerMira()
	{
		if (GameClient.Instance is not { } cli) return;

		byte? zona = null;
		if (Godot.Input.IsActionJustPressed("aim_none")) zona = 0;
		else if (Godot.Input.IsActionJustPressed("aim_head")) zona = 1;
		else if (Godot.Input.IsActionJustPressed("aim_torso")) zona = 2;
		else if (Godot.Input.IsActionJustPressed("aim_abdomen")) zona = 3;
		else if (Godot.Input.IsActionJustPressed("aim_arms")) zona = 4;
		else if (Godot.Input.IsActionJustPressed("aim_legs")) zona = 5;
		if (zona.HasValue) cli.SendAim(zona.Value);

		if (Godot.Input.IsActionJustPressed("lethal")) cli.SendLethal(!cli.Letal);
	}

	/// <summary>
	/// O servidor recusou o passo. Aqui NAO se discute: a posicao dele vale. Escreve na
	/// posicao EXATA, nao so no no -- senao o proximo quadro parte da posicao antiga e a
	/// correcao e engolida.
	/// </summary>
	private void OnCorrected(Vec2 pos)
	{
		// NO MEIO DO VOO a correcao tambem REVELA O RUMO: e a diferenca entre onde o servidor diz
		// que estou e onde eu estava. Sem isso o cliente nao teria como deslizar sozinho -- o rumo
		// do arremesso nunca viaja num campo proprio.
		if (_empurrado)
		{
			Vec2 d = pos - _pos;
			if (d.LengthSquared > 1f) _rumoDoVoo = d.Normalized();
		}
		_pos = pos;
		Desenhar();
	}

	/// <summary>Pra onde o corpo esta sendo arremessado. Sai da correcao -- ver OnCorrected.</summary>
	private Vec2 _rumoDoVoo;

	/// <summary>
	/// Velocidade do voo em px/s. E a mesma do servidor -- dois tiles a cada 0,1 s = 640 px/s --,
	/// entao o cliente chega no mesmo lugar e a correcao seguinte quase nao tem o que corrigir.
	/// </summary>
	private const double VelocidadeDoVoo =
		Jandirus.Core.Combat.Empurrao.TilesPorTique * ZoneCollision.TileSize / Jandirus.Core.Combat.Empurrao.SegundosPorTique;

	/// <summary>
	/// PARA ONDE O SERVIDOR ME MANDOU. Troca de zona, decolagem, pouso, saida da mente.
	///
	/// Escreve na posicao EXATA (`_pos`) e nao so no no. Escrever so no no e um bug silencioso:
	/// `_pos` e a verdade da simulacao -- e dela que sai o proximo passo e o que se manda pro
	/// servidor -- entao o corpo continuaria andando a partir do lugar ANTIGO e o servidor
	/// corrigiria pra sempre. Em terra isso passava batido (os spawns sao perto); no espaco a
	/// distancia e de milhoes de pixels e o defeito virou visivel na hora.
	/// </summary>
	public void Teleportar(Vec2 pos)
	{
		_pos = pos;
		Destino = null;   // chegou noutro lugar: o piloto anterior nao vale mais
		Desenhar();
	}
}
