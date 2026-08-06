using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// Outro jogador. O servidor manda posicao 30x por segundo; desenhar isso cru daria um
/// personagem andando aos saltos.
///
/// ============================ POR QUE A PRIMEIRA VERSAO ANDAVA "POR TILE" ============================
/// Ela guardava duas amostras e interpolava entre elas ao longo do intervalo do pacote ANTERIOR,
/// supondo que o proximo chegaria depois do mesmo tempo. Rede nao faz isso. Quando um pacote
/// atrasava, o lerp terminava antes e o corpo FICAVA PARADO ate o proximo; quando adiantava, o
/// lerp era cortado no meio e o corpo SALTAVA. Anda-para-anda-para -- que e exatamente a leitura
/// de "movimento por tile" que o dono descreveu, e a mesma causa do arremesso sem fluidez.
///
/// Agora ha um BUFFER com carimbo de tempo e um ATRASO FIXO: desenha-se sempre o estado do mundo
/// como ele era ha <see cref="Atraso"/> segundos, interpolando entre as duas amostras que cercam
/// esse instante. O relogio do desenho e o do CLIENTE, contínuo, e nao a chegada dos pacotes --
/// entao jitter deixa de virar movimento. Um pacote perdido tambem some: as amostras vizinhas
/// cobrem o buraco, porque a que falta nao e mais o unico caminho entre duas posicoes.
///
/// E o esquema padrao de interpolacao de estado (o mesmo do Source e de qualquer coisa em rede);
/// o preco e ver o outro corpo dois quadros no passado, que a 30 Hz sao 66 ms.
/// ======================================================================================================
/// </summary>
public partial class RemotePlayer : Node2D
{
	/// <summary>
	/// QUANTO DO PASSADO SE DESENHA, em segundos.
	///
	/// Dois tiques (66 ms). Um so nao deixa folga: qualquer pacote que chegue um milissegundo
	/// atrasado ja obriga a extrapolar, e volta o engasgo. Tres seriam 100 ms de atraso visual,
	/// que num jogo de troca de socos ja se sente na hora de reagir.
	/// </summary>
	private const double Atraso = 2 * Jandirus.Net.Protocol.TickSeconds;

	/// <summary>
	/// Quantas amostras guardar. Oito cobrem um quarto de segundo -- muito mais que o atraso, e o
	/// bastante pra atravessar uma rajada de pacotes perdidos sem ficar sem par pra interpolar.
	/// </summary>
	private const int Amostras = 8;

	private CharacterVisual _visual = null!;
	private readonly List<(double T, Vector2 Pos)> _linha = [];
	private double _relogio;
	private Facing _facing = Facing.South;
	private bool _moving;
	private Jandirus.Net.Protocol.Pose _pose = Jandirus.Net.Protocol.Pose.Normal;

	public override void _Ready()
	{
		_visual = GetNode<CharacterVisual>("Visual");
	}

	/// <summary>
	/// Acima de quantos pixels num intervalo de snapshot o deslocamento SO pode ser teleporte.
	///
	/// Tres tiles. A caminhada base e 160 px/s (5 tiles/s) e correr multiplica por pouco mais de
	/// um; o arremesso, que e o movimento mais rapido do jogo, faz 640 px/s -- 21 px por intervalo
	/// de 33 ms. Tres tiles (96 px) fica MUITO acima de qualquer coisa legitima e MUITO abaixo do
	/// menor teleporte do jogo (o cruzamento do embate salta ~6 tiles).
	/// </summary>
	private const float LimiteDeSalto = 3 * Jandirus.Core.World.ZoneCollision.TileSize;

	/// <summary>
	/// CRAVA O CORPO num ponto, sem suavizar.
	///
	/// A interpolacao existe pra encobrir o vao entre dois snapshots de um corpo que ANDA -- ela
	/// NAO serve pra suavizar um teleporte de servidor. Quando o servidor diz "este corpo esta
	/// AQUI, e o golpe saiu daqui", deslizar ate la e mostrar um passado que ja acabou.
	/// </summary>
	public void Cravar(Vector2 onde)
	{
		Position = onde;
		// A LINHA DO TEMPO INTEIRA MORRE. Deixar amostras velhas faria o corpo deslizar de volta
		// pro caminho de onde ele acabou de ser arrancado, no quadro seguinte.
		_linha.Clear();
		_linha.Add((_relogio, onde));
	}

	/// <summary>
	/// Chega um snapshot. NAO recebe mais "quanto tempo desde o ultimo pacote": a interpolacao
	/// agora corre no relogio do cliente, com atraso fixo, e o intervalo entre chegadas -- que era
	/// o que fazia o corpo engasgar -- deixou de participar do desenho. Ver o cabecalho da classe.
	/// </summary>
	public void Receive(Vec2 pos, Facing facing, bool moving, bool deitado, Jandirus.Net.Protocol.Pose pose,
						bool correndo = false, bool rabo = false, float altitude = 0f)
	{
		_visual.MostrarRabo(rabo);
		_alturaDoPacote = altitude;
		var alvo = new Vector2(pos.X, pos.Y);
		Vector2 ultima = _linha.Count > 0 ? _linha[^1].Pos : alvo;

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
		if (ultima.DistanceSquaredTo(alvo) > LimiteDeSalto * LimiteDeSalto) Cravar(alvo);
		else
		{
			_linha.Add((_relogio, alvo));
			if (_linha.Count > Amostras) _linha.RemoveAt(0);
		}

		_facing = facing;
		_moving = moving;
		_visual.SetMotion(facing, moving);

		// CORRENDO VEM DO SERVIDOR, e nao da conta de velocidade que estava aqui. Ela comparava a
		// distancia entre dois pacotes com a velocidade BASE do jogo -- e a velocidade de andar de
		// cada personagem sai do `Espeed` dele, que cresce a vida inteira. Quem era rapido andando
		// passava do corte e deixava rastro de corrida sem apertar shift. Ver `EntityState.Correndo`.
		_visual.Correr(correndo, alvo - ultima);
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

	/// <summary>
	/// A ALTURA DE QUEM EU ESTOU VENDO. Mesma regra do corpo local (ver `LocalPlayer.AplicarAltura`):
	/// quem sobe e o DESENHO, nunca o node -- a posicao do node e onde o corpo esta pro alcance do
	/// soco, pro Y-sort e pra faisca de impacto. Um corpo desenhado 160 px acima de onde ele esta ja
	/// custou caro neste projeto (a queixa de "a hitbox pega MUITO longe"); repetir isso pelo lado do
	/// desenho seria o mesmo defeito com outra roupa.
	/// </summary>
	private void Altura(float altitude)
	{
		if (Mathf.IsEqualApprox(_altitude, altitude)) return;
		_altitude = altitude;

		var deslocamento = new Vector2(0, -altitude * Jandirus.Core.World.Voo.EscalaNaTela);
		_visual.Position = deslocamento;
		if (GetNodeOrNull<Aura>("Aura") is { } aura) aura.Position = deslocamento;
		if (GetNodeOrNull<HealthBar>("Vida") is { } vida) vida.Position = deslocamento;

		_sombra ??= CriarSombra();
		_sombra.Altura = altitude;
	}

	private float _altitude;
	private SombraDeVoo? _sombra;

	/// <summary>
	/// A altura que o ULTIMO PACOTE trouxe. O desenho a persegue por quadro -- ver `_Process`.
	///
	/// A POSICAO deste corpo ja e interpolada com atraso fixo (ver o cabecalho da classe), mas a
	/// altura vinha CRUA do pacote: horizontal macio e vertical aos degraus de 30 Hz no mesmo corpo.
	/// Medido no corpo LOCAL, que tem o mesmo cano: 81% dos quadros da subida sem mexer nada.
	/// </summary>
	private float _alturaDoPacote;

	/// <summary>A altura DESENHADA deste corpo, a vida e pra onde ele olha. Lidos pelos decalques.</summary>
	public float AlturaDeTeste => _altitude;
	public byte VidaDeTeste { get; private set; }
	public Facing OlharDeTeste => _facing;

	/// <summary>A vida deste corpo, em porcento. Vem do snapshot -- ver `World.AoReceberSnapshot`.</summary>
	public void GuardarVida(byte v) => VidaDeTeste = v;

	/// <summary>A mesma taxa do corpo local -- ver `LocalPlayer.PerseguicaoDaAltura`.</summary>
	private const float PerseguicaoDaAltura = 25f;

	/// <summary>
	/// A sombra so nasce QUANDO ALGUEM VOA. Corpo remoto e o que mais se instancia no jogo (todo
	/// mundo que entra no campo de visao vira um), e a maioria nunca sai do chao -- dar um node de
	/// desenho a mais pra cada um seria pagar por todos o preco de poucos.
	/// </summary>
	private SombraDeVoo CriarSombra()
	{
		var s = new SombraDeVoo { Name = "SombraDeVoo" };
		AddChild(s);
		return s;
	}

	public override void _Process(double delta)
	{
		// A ALTURA PERSEGUE O PACOTE, por quadro -- a posicao ja e interpolada com atraso fixo, e
		// deixar a vertical crua daria horizontal macio com vertical aos degraus no MESMO corpo.
		// Ver `_alturaDoPacote`.
		float falta = _alturaDoPacote - _altitude;
		if (MathF.Abs(falta) > 0.5f)
			Altura(_altitude + falta * Mathf.Min(1f, (float)delta * PerseguicaoDaAltura));
		else if (!Mathf.IsEqualApprox(_altitude, _alturaDoPacote))
			Altura(_alturaDoPacote);   // colado: crava, senao sobra rabo de meio pixel no pouso

		_relogio += delta;
		if (_linha.Count == 0) return;

		double quando = _relogio - Atraso;

		// AINDA NAO HA PASSADO SUFICIENTE (o corpo acabou de entrar em campo): fica na primeira
		// amostra em vez de deslizar de um lugar onde nunca esteve.
		if (quando <= _linha[0].T) { Position = _linha[0].Pos; return; }

		// O PAR QUE CERCA O INSTANTE. Varre de tras pra frente porque o que se procura esta quase
		// sempre no fim -- o buffer inteiro tem 8 amostras, mas o caso comum e a ultima ou a penultima.
		for (int i = _linha.Count - 1; i > 0; i--)
		{
			if (_linha[i - 1].T > quando) continue;

			(double t0, Vector2 p0) = _linha[i - 1];
			(double t1, Vector2 p1) = _linha[i];
			double vao = t1 - t0;
			Position = vao > 0.0001 ? p0.Lerp(p1, (float)Math.Clamp((quando - t0) / vao, 0, 1)) : p1;
			return;
		}

		// O PACOTE ATRASOU e o instante de desenho passou da ultima amostra. NAO SE EXTRAPOLA:
		// inventar posicao futura poe o corpo onde ele talvez nao va, e o preco do erro e um
		// puxao pra tras quando o pacote chega. Segurar no ultimo ponto conhecido custa alguns
		// milissegundos de corpo parado -- e a mentira menor das duas.
		Position = _linha[^1].Pos;
	}
}
