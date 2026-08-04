using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// Outro jogador. O servidor manda posicao 30x por segundo; desenhar isso cru daria um
/// personagem andando aos saltos.
///
/// Solucao: RENDERIZAR UM PASSO ATRAS. Guardamos a amostra anterior e a atual e interpolamos
/// entre as duas ao longo do intervalo do tick. O boneco fica ~33 ms no passado, e em troca
/// se move liso mesmo com jitter. Se um pacote se perde (o canal e sequenciado, sem reenvio),
/// a interpolacao simplesmente estica ate a proxima amostra.
/// </summary>
public partial class RemotePlayer : Node2D
{
	private CharacterVisual _visual = null!;
	private Vector2 _from, _to;
	private double _elapsed;
	private double _interval = Jandirus.Net.Protocol.TickSeconds;
	private Facing _facing = Facing.South;
	private bool _moving;
	private Jandirus.Net.Protocol.Pose _pose = Jandirus.Net.Protocol.Pose.Normal;

	public override void _Ready()
	{
		_visual = GetNode<CharacterVisual>("Visual");
		_from = _to = Position;
	}

	/// <summary>
	/// CRAVA O CORPO num ponto, sem suavizar.
	///
	/// A interpolacao existe pra encobrir os 33 ms entre dois snapshots -- ela NAO serve pra
	/// suavizar um teleporte de servidor. Quando o servidor diz "este corpo esta AQUI, e o golpe
	/// saiu daqui", deslizar ate la e mostrar um passado que ja acabou.
	///
	/// Zera o lerp inteiro (`_from`, `_to` e o relogio) pra o proximo snapshot partir DESTE ponto:
	/// deixar `_to` velho faria o corpo voltar meio caminho no quadro seguinte.
	/// </summary>
	/// <summary>
	/// Acima de quantos pixels num intervalo de snapshot o deslocamento SO pode ser teleporte.
	///
	/// Tres tiles. A caminhada base e 160 px/s (5 tiles/s) e correr multiplica por pouco mais de
	/// um; o arremesso, que e o movimento mais rapido do jogo, faz 640 px/s -- 21 px por intervalo
	/// de 33 ms. Tres tiles (96 px) fica MUITO acima de qualquer coisa legitima e MUITO abaixo do
	/// menor teleporte do jogo (o cruzamento do embate salta ~6 tiles).
	/// </summary>
	private const float LimiteDeSalto = 3 * Jandirus.Core.World.ZoneCollision.TileSize;

	public void Cravar(Vector2 onde)
	{
		Position = _from = _to = onde;
		_elapsed = 0;
	}

	public void Receive(Vec2 pos, Facing facing, bool moving, bool deitado, Jandirus.Net.Protocol.Pose pose,
						double sinceLast, bool rabo = false)
	{
		_visual.MostrarRabo(rabo);
		_from = Position;                 // parte de onde o boneco esta DESENHADO (nao de onde deveria)
		_to = new Vector2(pos.X, pos.Y);
		_interval = sinceLast > 0.001 ? sinceLast : Jandirus.Net.Protocol.TickSeconds;
		_elapsed = 0;

		// ============================ SALTO NAO SE SUAVIZA ============================
		// A interpolacao existe pra encobrir os 33 ms entre dois snapshots de um corpo que ANDA.
		// Quando o servidor TELEPORTA alguem -- a investida do soco, o Zanzoken, o cruzamento do
		// embate, o Light Buster -- a mesma suavizacao vira mentira: o boneco desliza por um caminho
		// que ninguem percorreu, e por um intervalo inteiro ele esta desenhado onde ja nao esta.
		//
		// Era a causa da queixa "a hitbox pega MUITO longe": a faisca nasce no meio dos corpos
		// DESENHADOS, e o atacante ainda estava a ate 128 px do lugar onde socou.
		//
		// O CORTE E FISICO, nao um palpite: corpo nenhum anda mais que `LimiteDeSalto` num intervalo
		// de snapshot -- o proprio servidor recusaria o passo (`MoveRules.ValidarPasso`). Acima
		// disso so ha uma explicacao possivel, e ela nao se interpola.
		if (_from.DistanceSquaredTo(_to) > LimiteDeSalto * LimiteDeSalto) Cravar(_to);

		_facing = facing;
		_moving = moving;
		_visual.SetMotion(facing, moving);

		// CORRENDO SE DEDUZ, nao se recebe. O snapshot nao carrega "este esta correndo" e nao
		// precisa: correr E andar mais rapido, e a velocidade esta ali, na distancia entre dois
		// pacotes dividida pelo tempo entre eles. Um bit a mais no pacote diria a mesma coisa que
		// a posicao ja diz -- e poderia DISCORDAR dela, que e o pior dos dois mundos.
		//
		// O CORTE EM 1,35x E A FOLGA DE VALIDACAO do proprio servidor (`MoveRules.SpeedTolerance`):
		// abaixo disso a diferenca cabe em jitter de rede, acima disso e corrida de verdade.
		float andou = _from.DistanceTo(_to);
		double vps = _interval > 0.001 ? andou / _interval : 0;
		bool correndo = moving && vps > MoveRules.BaseSpeedPx * MoveRules.SpeedTolerance;
		_visual.Correr(correndo, _to - _from);
		if (GetNodeOrNull<RastroDeCorrida>("Rastro") is { } rastro) rastro.Definir(correndo);

		// Socar REINICIA a animacao a cada vez que a pose (re)aparece -- e o que faz uma
		// sequencia de golpes parecer varios socos em vez de um ciclo continuo.
		// A animacao de soco e encaixada na duracao do golpe -- o mesmo que o LocalPlayer faz.
		// Daqui nao da pra saber a cadencia do OUTRO (ela sai do Eactspeed dele, que e ficha
		// privada), entao usa-se a de referencia; o que importa e nao ficar em camera lenta.
		if (pose == Jandirus.Net.Protocol.Pose.Atacando && _pose != pose)
			_visual.RestartState("attack", Jandirus.Net.Protocol.AttackPoseMs / 1000.0);
		else _visual.SetPose(pose);

		// O CORPO CAI (E VOA) PRO LADO CERTO, e AGORA os outros clientes tambem sabem disso.
		//
		// Antes o teste era `pose == Nocauteado`, e ele nao cobria o arremesso -- durante o voo a
		// pose e a normal, entao o corpo aparecia DE PE pra quem estava assistindo. E o `facing` do
		// pacote era a direcao do OLHAR, nao a da queda. Os dois furos viravam a mesma queixa.
		if (deitado)
		{
			// A POSE separa os dois: nocauteado usa o desenho deitado, voando usa o acordado -- e
			// cada um tem a sua tabela de rotacao (ver `CharacterVisual.VoarPara`).
			if (pose == Jandirus.Net.Protocol.Pose.Nocauteado) _visual.DeitarPor(facing);
			else _visual.VoarPara(facing);
		}
		else _visual.GirarPara(default);

		_pose = pose;
	}

	public override void _Process(double delta)
	{
		_elapsed += delta;
		float t = (float)Math.Clamp(_elapsed / _interval, 0.0, 1.0);
		Position = _from.Lerp(_to, t);
	}
}
