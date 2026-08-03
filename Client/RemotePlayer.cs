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

	public void Receive(Vec2 pos, Facing facing, bool moving, Jandirus.Net.Protocol.Pose pose,
						double sinceLast, bool rabo = false)
	{
		_visual.MostrarRabo(rabo);
		_from = Position;                 // parte de onde o boneco esta DESENHADO (nao de onde deveria)
		_to = new Vector2(pos.X, pos.Y);
		_interval = sinceLast > 0.001 ? sinceLast : Jandirus.Net.Protocol.TickSeconds;
		_elapsed = 0;

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

		// O CORPO CAI PRO LADO CERTO. O `.dmi` so tem UM desenho de nocaute (deitado pra direita),
		// entao quem estava olhando pra outro lado caia errado -- e o dono viu. Girar resolve, e e
		// o que o proprio BYOND fazia com `transform`.
		if (pose == Jandirus.Net.Protocol.Pose.Nocauteado) _visual.DeitarPor(facing);
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
