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
		_caido = ficha.Imobilizado;
		_visual.MostrarRabo(ficha.Rabo);
		// 3% de folga sobre o custo de um segundo de corrida: no fio do Ki, correr e desistir
		// a cada quadro daria um solavanco a cada passo
		_temKiPraCorrer = ficha.MaxKi <= 0 || ficha.Ki > ficha.MaxKi * 0.03;
	}

	/// <summary>Nocauteado ou morto: o servidor recusa qualquer passo, entao aqui nem se tenta.</summary>
	private bool _caido;

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
		var input = _caido || Foco.Digitando
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

		bool querCorrer = _shift && tentandoAndar && _temKiPraCorrer;
		if (querCorrer && !_correndo) AudioDirector.EfeitoNoLugar(this, Trilha.Dash, 0.5f);
		_correndo = querCorrer;

		Vec2 antes = _pos;
		_pos = MoveRules.Advance(_pos, dir, (float)delta, SpeedStat, Mapa, out _, _correndo);
		Desenhar();

		// ANDANDO = saiu do lugar, nao = apertou a tecla. Empurrando a parede o personagem
		// fica parado de pe em vez de marchar sem sair do lugar.
		bool andando = (_pos - antes).LengthSquared > 0.01f;

		if (tentandoAndar) _facing = MoveRules.FacingFrom(dir, _facing);
		_visual.SetMotion(_facing, andando);

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

		if (!Foco.Digitando)
		{
			if (Godot.Input.IsActionJustPressed("transformar")) GameClient.Instance?.SendTransformar(true);
			else if (Godot.Input.IsActionJustPressed("reverter")) GameClient.Instance?.SendTransformar(false);
		}

		// UM soco por vez. Sem esta trava, martelar o espaco re-armava o cronometro a cada
		// tecla e o personagem ficava preso na pose de soco pra sempre -- e como todo estado
		// do .dmi tem loop, o ciclo se repetia sem nunca voltar a ficar de pe.
		if (!Foco.Digitando && Godot.Input.IsActionJustPressed("attack") && _ataqueAte <= 0)
		{
			// SHIFT + ESPACO = GOLPE PESADO, e com ele a investida longa. So ESPACO = golpe
			// leve, com um passo curto pra fechar o meio metro que falta. Nao ha tecla
			// separada de "soco forte": no original o golpe so ficava pesado quando saia em
			// dash (`1 + dash_delay` virava o `Type`), e SHIFT ja e essa escolha.
			Protocol.Golpe golpe = _shift ? Protocol.Golpe.Pesado : Protocol.Golpe.Leve;
			double dura = _cadencia * Protocol.PesoDoGolpe(golpe);

			// o rasgo da investida sai NA HORA, sem esperar o servidor: e o feedback do
			// controle. O que o servidor decide e se ela acerta, nao se ela aconteceu.
			if (golpe == Protocol.Golpe.Pesado)
				AudioDirector.EfeitoNoLugar(this, Trilha.Dash, 0.7f);

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
		_pos = pos;
		Desenhar();
	}

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
