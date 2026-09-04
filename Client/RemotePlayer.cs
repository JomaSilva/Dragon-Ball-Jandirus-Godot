using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// Outro jogador. O servidor manda posicao 30x por segundo; desenhar isso cru daria um
/// personagem andando aos saltos.
///
/// ============================ POR QUE A PRIMEIRA VERSAO ANDAVA "POR TILE" ============================
/// Ela guardava duas amostras e interpolava entre elas ao longo do intervalo do pacote ANTERIOR,
/// supondo que o proximo chegaria depois do mesmo tempo. Rede nao faz isso. Quando um pacote
/// atrasava, o lerp terminava antes e o corpo FICAVA PARADO ate o proximo; quando adiantava, o
/// lerp era cortado no meio e o corpo SALTAVA.
///
/// ============================ E POR QUE A SEGUNDA DAVA MICRO TELEPORTES ============================
/// A segunda ja tinha um buffer com atraso fixo -- mas carimbava cada amostra com a hora de
/// CHEGADA. Tres coisas viravam movimento por causa disso:
///
///   1. O DEGRAU DO TIQUE. O servidor nao integra o jogador: `pl.Pos` so muda quando chega um
///      input, e o input vem a 30 Hz num relogio que NAO e o do tique. Ora zero inputs caem entre
///      dois tiques (a posicao REPETE -- carimbada como amostra nova, o corpo congela um tique) ora
///      dois (a posicao anda o DOBRO num tique -- o corpo salta). O salto cresce com a velocidade:
///      a 1760 px/s sao 59 px, quase dois tiles. Era o "micro teleporte" do relato.
///   2. DOIS PACOTES NO MESMO QUADRO recebiam o MESMO carimbo: o trecho entre eles tinha duracao
///      zero e era atravessado num quadro so.
///   3. O JITTER DO FIO (a thread do LiteNetLib acordando a cada 15 ms, o `PollEvents` por quadro)
///      entrava direto na duracao dos trechos.
///
/// ============================ A LINHA DO TEMPO AGORA E DO SERVIDOR ============================
/// O snapshot chega com HORA: `servidorMs` no cabecalho e, por corpo, a `IdadeMs` -- ha quantos ms
/// aquela posicao valia (ver `EntityState.IdadeMs`). A amostra entra na linha com
/// `T = servidorMs - idade`, que e a hora em que o corpo ESTAVA ali, no relogio de quem o move.
/// Uma posicao repetida em dois tiques traz a mesma T (descartada); uma que andou o dobro traz a T
/// certa (o trecho dura o dobro). A hora de chegada nao participa mais do desenho.
///
/// O desenho corre num relogio do servidor ESTIMADO (`RelogioDoServidor`: o meu relogio mais o
/// deslocamento medido no proprio snapshot), <see cref="_atraso"/> ms no passado. O atraso comeca
/// em tres tiques e sobe um tique a cada vez que a linha acaba antes da hora (inanicao), ate
/// <see cref="AtrasoTeto"/>; relaxa devagar. Faltando amostra, extrapola ate um tique com a ultima
/// velocidade e depois segura.
///
/// Salto de servidor continua NAO se interpolando -- so que o corte agora e por velocidade
/// implicita (<see cref="VelocidadeDeTeleporte"/>), nao por distancia fixa: a 1760 px/s um tique
/// legitimo ja passava dos 96 px que o corte antigo chamava de teleporte.
/// ======================================================================================================
/// </summary>
public partial class RemotePlayer : Node2D
{
	/// <summary>Um tique do servidor em ms -- a unidade de tudo nesta classe.</summary>
	private const double TickMs = Protocol.TickMs;

	/// <summary>
	/// QUANTO DO PASSADO SE DESENHA, em ms, no comeco. Tres tiques (100 ms).
	///
	/// Dois nao deixam folga: o snapshot que cobre o instante desenhado precisa ter CHEGADO, e entre
	/// a hora da amostra e a chegada ha a espera pelo tique (ate 33 ms), a ida e o jitter. Com dois
	/// tiques qualquer pacote um milissegundo atrasado ja obriga a extrapolar. Cem ms num jogo de
	/// troca de socos se sente -- e por isso que o valor nao e fixo: ele sobe so quando falta e volta
	/// quando sobra (ver <see cref="AjustarAtraso"/>).
	/// </summary>
	private const double AtrasoBase = 3 * TickMs;

	/// <summary>O teto do atraso adaptativo: 250 ms. Acima disso o corpo esta no passado demais pra reagir a ele.</summary>
	private const double AtrasoTeto = 250;

	/// <summary>Quanto o atraso sobe por inanicao e desce por relaxamento: um tique.</summary>
	private const double DegrauDoAtraso = TickMs;

	/// <summary>Dez segundos sem inanicao e o atraso desce um degrau.</summary>
	private const double RelaxaAposMs = 10_000;

	/// <summary>
	/// O atraso nao SALTA: ele desliza ate o alvo a 2% do tempo real. Mudar 33 ms de uma vez faria o
	/// corpo pular 33 ms pra tras (ou pra frente) num quadro -- que e o proprio defeito com outra
	/// causa. A 2%, o degrau leva ~1,7 s e o corpo anda 2% mais devagar nesse tempo, que ninguem ve.
	/// </summary>
	private const double DeslizeDoAtraso = 0.02;

	/// <summary>
	/// Quantas amostras guardar. Dezesseis cobrem meio segundo -- mais que o teto do atraso, e o
	/// bastante pra atravessar uma rajada de pacotes perdidos sem ficar sem par pra interpolar.
	/// </summary>
	private const int Amostras = 16;

	/// <summary>
	/// ACIMA DESTA VELOCIDADE IMPLICITA o deslocamento SO pode ser teleporte, em px/s.
	///
	/// O movimento mais rapido do jogo e correr com `SpeedStat` no teto: 160 x 2,2 x 5 = 1760 px/s,
	/// e a validacao do servidor tolera 1,35x disso (~2400). O Zanzoken pisca 5 tiles num tique
	/// (4800 px/s); a troca de zona, a borda do mundo e o Light Buster sao ordens de grandeza acima.
	/// Quatro mil fica com folga de cada lado.
	/// </summary>
	private const float VelocidadeDeTeleporte = 4000f;

	/// <summary>
	/// COM DUAS AMOSTRAS NA MESMA HORA (dt zero) nao ha velocidade pra medir: ai vale a distancia,
	/// e tres tiles e o corte antigo -- muito acima de qualquer passo e abaixo do menor teleporte.
	/// </summary>
	private const float SaltoParado = 3 * ZoneCollision.TileSize;

	/// <summary>
	/// O RELOGIO DO SERVIDOR, estimado -- um so pra todos os corpos, porque o deslocamento e da
	/// CONEXAO e nao do corpo. O `GameClient` o alimenta com o cabecalho de cada snapshot.
	/// </summary>
	public static readonly RelogioDoServidor Relogio = new();

	/// <summary>
	/// DEFEITO INJETADO (so bancada): volta a carimbar a amostra com a hora de CHEGADA, ignorando a
	/// hora que veio no pacote. E o comportamento anterior, e a bancada da fluidez exige que a MESMA
	/// alimentacao reprove com isto ligado -- senao ela nao estaria medindo nada.
	/// </summary>
	internal static bool DefeitoCarimboDeChegada;

	/// <summary>Uma amostra da linha do tempo. `T` e a hora no relogio do SERVIDOR, em ms.</summary>
	/// <param name="Saturada">
	/// A idade veio no teto (`EntityState.IdadeSaturada`): a posicao vale ha PELO MENOS isso, e a
	/// hora real e mais antiga do que `T` diz. Ver o bloco "amostra saturada" em <see cref="Receive"/>.
	/// </param>
	/// <param name="Olhar">Pra onde o corpo olhava NESTE instante -- ver <see cref="AplicarMovimento"/>.</param>
	/// <param name="Movendo">O `Moving` do mesmo instante.</param>
	/// <param name="Correndo">O `Correndo` do mesmo instante.</param>
	private readonly record struct Amostra(double T, Vector2 Pos, float Altura, bool Saturada,
										   Facing Olhar, bool Movendo, bool Correndo);

	private CharacterVisual _visual = null!;
	private readonly List<Amostra> _linha = [];
	private double _atraso = AtrasoBase;
	private double _atrasoAlvo = AtrasoBase;
	private double _semInanicaoHa;
	private int _inanicoes;
	private Vector2 _exata;
	private Facing _facing = Facing.South;
	private bool _moving, _correndo;
	private Protocol.Pose _pose = Protocol.Pose.Normal;

	// O QUE ESTA DESENHADO -- olhar, andar e corrida do instante que a tela mostra, e nao do ultimo
	// pacote. Ver `AplicarMovimento`.
	private Facing _olharDesenhado = Facing.South;
	private bool _movendoDesenhado, _correndoDesenhado, _movimentoAplicado;
	private Vector2 _rumoDoDesenho;

	/// <summary>
	/// DEFEITO INJETADO (`--diagfluidez`): o olhar e o andar aplicados NA CHEGADA do pacote, como era
	/// antes -- o corpo vira ~100 ms antes de o desenho sair na direcao nova, e desliza virado.
	/// </summary>
	internal static bool DefeitoViradaNaChegada;

	public override void _Ready()
	{
		_visual = GetNode<CharacterVisual>("Visual");
	}

	/// <summary>
	/// CRAVA O CORPO num ponto, sem suavizar.
	///
	/// A interpolacao existe pra encobrir o vao entre dois snapshots de um corpo que ANDA -- ela
	/// NAO serve pra suavizar um teleporte de servidor. Quando o servidor diz "este corpo esta
	/// AQUI, e o golpe saiu daqui", deslizar ate la e mostrar um passado que ja acabou.
	///
	/// ============================ E A LINHA RENASCE NA HORA DO DESENHO ============================
	/// A linha inteira morre (deixar amostras velhas faria o corpo deslizar de volta pro caminho de
	/// onde acabou de ser arrancado) e a semente nova entra com a hora que esta sendo DESENHADA
	/// agora, e nao com a hora do teleporte. A versao anterior semeava na hora de chegada e depois
	/// desenhava `Atraso` no passado: o corpo ficava cravado no destino por 66 ms, parado, antes de
	/// voltar a andar. Semeando no instante de desenho a proxima amostra (que vem com a hora real,
	/// ~`_atraso` a frente) forma um trecho um pouco mais longo que o normal -- o corpo sai do
	/// destino um pouco mais devagar por um tique em vez de ficar parado nele.
	/// ==========================================================================================
	/// </summary>
	public void Cravar(Vector2 onde) => Cravar(onde, _altitude);

	private void Cravar(Vector2 onde, float altitude)
	{
		var semente = new Amostra(TempoDeDesenho(), onde, altitude, Saturada: false, _facing, _moving, _correndo);
		Mostrar(onde, altitude, semente, Vector2.Zero);
		_linha.Clear();
		_linha.Add(semente);
	}

	/// <summary>
	/// Chega um snapshot. A hora da amostra e <paramref name="servidorMs"/> menos
	/// <paramref name="idadeMs"/> -- ver o cabecalho da classe e `EntityState.IdadeMs`.
	/// </summary>
	/// <param name="servidorMs">O cabecalho do snapshot, ja desembrulhado (`GameClient.ServidorMsDoSnapshot`).</param>
	/// <param name="idadeMs">Ha quantos ms `pos` valia, contados de `servidorMs` (pode ser negativo -- ver `EntityState.IdadeMs`). `IdadeSaturada` = saturou.</param>
	/// <param name="canalAtirando">
	/// SO IMPORTA COM <see cref="Protocol.Pose.Canalizando"/>: o raio ja saiu da mao
	/// (pose `blast`) ou o corpo ainda esta reunindo energia (idle + o brilho da
	/// <see cref="CargaDeRaioVisual"/>)? Vem do byte opcional do snapshot -- ver `EntityState.Canal`.
	/// </param>
	/// <param name="ocupacao">
	/// O QUE ESTE CORPO ESTA FAZENDO, calculado pelo SERVIDOR e lido daqui sem refazer conta nenhuma.
	/// Ele nao desenha nada: ele existe pro passo do corpo local saber em quem nao pode entrar --
	/// `World.MontarGradeDeCorpos` o carrega pra dentro da grade de colisao. Ver
	/// `Core/World/Ocupacao.cs`.
	/// </param>
	public void Receive(long servidorMs, int idadeMs, Vec2 pos, Facing facing, bool moving, bool deitado,
						Protocol.Pose pose, bool correndo = false, bool rabo = false, float altitude = 0f,
						bool voando = false, bool canalAtirando = false, Ocupacao ocupacao = Ocupacao.Livre)
	{
		_ocupacao = ocupacao;
		_visual.MostrarRabo(rabo);
		OuvirODecolar(voando);
		var alvo = new Vector2(pos.X, pos.Y);
		Vector2 ultima = _linha.Count > 0 ? _linha[^1].Pos : alvo;
		_facing = facing;
		_moving = moving;
		_correndo = correndo;

		double t = DefeitoCarimboDeChegada ? Relogio.AgoraNoServidor() : servidorMs - idadeMs;
		bool saturada = idadeMs >= EntityState.IdadeSaturada;

		if (_linha.Count == 0) _linha.Add(new Amostra(t, alvo, altitude, saturada, facing, moving, correndo));
		else
		{
			Amostra u = _linha[^1];
			double dt = t - u.T;
			float dist = alvo.DistanceTo(u.Pos);

			// ============================ SALTO NAO SE SUAVIZA ============================
			// Quando o servidor TELEPORTA alguem -- a investida do soco, o Zanzoken, o cruzamento do
			// embate, o Light Buster, a borda do mundo -- a suavizacao vira mentira: o boneco desliza
			// por um caminho que ninguem percorreu, e por um intervalo inteiro ele esta desenhado onde
			// ja nao esta. Era a causa da queixa "a hitbox pega MUITO longe".
			//
			// O CORTE E POR VELOCIDADE, nao por distancia: corpo nenhum atravessa
			// `VelocidadeDeTeleporte` -- o proprio servidor recusaria o passo (`MoveRules.ValidateStep`).
			// Um corte em pixels por amostra (o antigo, 96 px) chamava de teleporte um tique legitimo
			// de quem corre no teto, e a cada um deles a linha morria e o corpo congelava.
			bool teleporte = dt <= 0 ? dist > SaltoParado : dist * 1000.0 / dt > VelocidadeDeTeleporte;
			if (teleporte) Cravar(alvo, altitude);
			else if (dt > 0)
			{
				// ============================ A AMOSTRA SATURADA ============================
				// A anterior chegou com a idade no teto: o corpo estava parado ha tanto tempo que a
				// hora real dela e MAIS ANTIGA do que a T guardada (`servidorMs - IdadeSaturada`).
				// Um corpo que ninguem moveu esta parado ate a vespera do proximo movimento, entao a
				// leitura mais fiel do vao e "saiu dali um tique antes de chegar aqui" -- e nao um
				// deslize de 255 ms ate a posicao nova, que e o que a T saturada produziria.
				if (u.Saturada && !saturada) _linha[^1] = u with { T = Math.Max(u.T, t - TickMs) };

				_linha.Add(new Amostra(t, alvo, altitude, saturada, facing, moving, correndo));
				if (_linha.Count > Amostras) _linha.RemoveAt(0);
			}
			// dt <= 0 e perto: e a MESMA amostra, repetida pelo tique que nao viu input novo (a hora
			// veio igual). Nao ha nada a acrescentar a linha -- e e exatamente esse descarte que
			// impede a repeticao de virar um tique de corpo parado. Se o OLHAR mudou sem input (o
			// servidor virou o corpo pra quem bateu), ele entra na amostra que ja esta la: e a que
			// esta sendo desenhada, entao vale no quadro seguinte.
			else if (u.Olhar != facing || u.Movendo != moving || u.Correndo != correndo)
				_linha[^1] = u with { Olhar = facing, Movendo = moving, Correndo = correndo };
		}

		// ============================ O OLHAR VIRA NA HORA DO DESENHO, NAO NA DA CHEGADA ============================
		// A posicao e desenhada ~`_atraso` no passado (a linha do tempo acima); o olhar, o `Moving` e a
		// corrida eram aplicados AQUI, no instante em que o pacote chegava -- ou seja ~100 ms ANTES do
		// trecho de caminho a que pertencem. Quem corria pra leste e virava pro norte aparecia virado
		// pro norte enquanto o desenho ainda percorria os ultimos 100 ms pra leste: "vira e desliza
		// virado antes de andar" (o dono, 2026-09-04). O corpo local nao faz isso porque vira e anda no
		// MESMO quadro. Agora os tres viajam DENTRO da amostra e sao aplicados por `AplicarMovimento`
		// quando o desenho chega ao trecho deles -- o mesmo relogio da posicao. A pose e a queda ficam
		// imediatas de proposito: sao eventos (o soco, o tombo) casados com o relato de golpe, que ja
		// crava o atacante na hora.
		// ==========================================================================================================
		if (DefeitoViradaNaChegada) AplicarMovimento(facing, moving, correndo, alvo - ultima);

		// Socar REINICIA a animacao a cada vez que a pose (re)aparece -- e o que faz uma
		// sequencia de golpes parecer varios socos em vez de um ciclo continuo.
		// A animacao de soco e encaixada na duracao do golpe -- o mesmo que o LocalPlayer faz.
		// Daqui nao da pra saber a cadencia do OUTRO (ela sai do Eactspeed dele, que e ficha
		// privada), entao usa-se a de referencia; o que importa e nao ficar em camera lenta.
		// ============================ E O RAIO NAO REINICIA, PORQUE ELE NAO E EVENTO ============================
		// O soco reinicia (a linha acima) porque ele e um GOLPE: cada um e um evento e o corpo tem que
		// mostrar a animacao de novo. O raio nao -- ele e um ESTADO que dura enquanto o canal viver, e
		// reiniciar a pose a cada snapshot travaria o desenho no primeiro quadro por segundos a fio.
		// Por isso ele cai no `SetPose`, que so trabalha quando o nome do estado muda.
		// ====================================================================================================
		if (pose == Protocol.Pose.Atacando && _pose != pose)
			_visual.RestartState("attack", Protocol.AttackPoseMs / 1000.0);
		else _visual.SetPose(pose, canalAtirando);

		// O CORPO CAI (E VOA) PRO LADO CERTO, e AGORA os outros clientes tambem sabem disso.
		//
		// Antes o teste era `pose == Nocauteado`, e ele nao cobria o arremesso -- durante o voo a
		// pose e a normal, entao o corpo aparecia DE PE pra quem estava assistindo. E o `facing` do
		// pacote era a direcao do OLHAR, nao a da queda. Os dois furos viravam a mesma queixa.
		if (deitado)
		{
			// A POSE separa os dois: nocauteado usa o desenho deitado, voando usa o acordado -- e
			// cada um tem a sua tabela de rotacao (ver `CharacterVisual.VoarPara`).
			if (pose == Protocol.Pose.Nocauteado) _visual.DeitarPor(facing);
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
	///
	/// ============================ ESTE METODO ERA A COPIA DA LISTA DE LA ============================
	/// Ele repetia, node por node, a mesma enumeracao a mao de `LocalPlayer.AplicarAltura` -- e essa
	/// duplicacao e o que fazia CADA esquecimento valer por dois: quem fosse deixado de fora aqui
	/// aparecia certo no proprio corpo e errado no de todo mundo, ou o contrario, e a queixa chegava
	/// como "so acontece com os outros". No corpo alheio isto ainda pesa MAIS: eu so me vejo voando,
	/// os outros eu vejo o tempo todo.
	///
	/// Agora as duas chamam a MESMA varredura, e nao ha mais duas listas pra manter de acordo.
	///
	/// ============================ E A ALTURA ANDA NA MESMA LINHA DO TEMPO DA POSICAO ============================
	/// Havia aqui um segundo suavizador (um lerp exponencial por quadro, `PerseguicaoDaAltura`) porque
	/// a posicao era interpolada e a altura chegava crua. Com a hora no pacote, a altura entra na
	/// MESMA amostra que a posicao e e interpolada pelo mesmo par -- uma mecanica so, e o corpo
	/// sobe e anda no mesmo relogio. O byte do fio (2,5 px por degrau) ainda deixa um vai-e-vem de
	/// ~20% na velocidade vertical entre amostras, mas a altura e desenhada a um quarto
	/// (`Voo.EscalaNaTela`): na tela isso e menos de um pixel.
	/// ==============================================================================================================
	/// </summary>
	private void Altura(float altitude)
	{
		if (Mathf.IsEqualApprox(_altitude, altitude)) return;
		_altitude = altitude;

		SubirComOVoo.Aplicar(this, new Vector2(0, -altitude * Voo.EscalaNaTela));

		// A SOMBRA FICA NO CHAO (ela declara `IFicaNoChao`) e recebe a altura por canal proprio -- e o
		// vao entre ela e o corpo que diz ao vizinho a que altura este sujeito esta.
		_sombra ??= CriarSombra();
		_sombra.Altura = altitude;
	}

	/// <summary>
	/// O BAQUE DE QUEM SAI DO CHAO E DE QUEM POUSA, no corpo alheio.
	///
	/// ============================ O MESMO BURACO DA CHAMA DA CARGA, NUM TERCEIRO SOM ============================
	/// `buku.wav` e `buku_land.wav` sao acesos pelo canal de EFEITO (`World.AoCairEfeito`, casos "voo"
	/// e "pouso"), e aquele canal e PESSOAL -- o servidor so o manda pra o dono do corpo. Resultado:
	/// decolar e pousar era mudo pra todo mundo menos pra quem decolava, exatamente como carregar Ki
	/// era antes de o `cg.Som(e.Carregando)` entrar no laco do snapshot.
	///
	/// No DM os dois saem no mundo como qualquer outro (`flying.dm:91`, `Stats.dm:422`), e o bit ja
	/// viajava: `EntityState.Voando` estava no snapshot e era o UNICO campo dele que o corpo remoto
	/// nao consumia -- a altura vinha, o estado de voo nao.
	///
	/// ============================ O PRIMEIRO PACOTE NAO E UMA DECOLAGEM ============================
	/// `null` ate o primeiro pacote, e nao `false`: quem entra no meu campo de visao JA voando nao
	/// decolou agora. Sem isto, atravessar um planeta cheio de gente no ar dispararia um estalo de
	/// decolagem por pessoa que aparecesse -- a mesma mentira que o `if (de != para)` da sincronia de
	/// forma existe pra evitar, so que no ouvido.
	/// =======================================================================================================
	/// </summary>
	private void OuvirODecolar(bool voando)
	{
		if (_voando == voando) return;
		bool primeiro = _voando == null;
		_voando = voando;
		if (primeiro) return;

		// POSICIONAL: o `EfeitoNoLugar` pendura o player NESTE corpo, entao o volume ja cai com a
		// distancia sem mais nada -- e as duas trilhas sao as mesmas que o corpo local usa.
		AudioDirector.EfeitoNoLugar(this, voando ? Trilha.Decolagem : Trilha.Pouso, 0.8f);
	}

	private bool? _voando;

	private float _altitude;
	private SombraDeVoo? _sombra;

	/// <summary>
	/// A altura DESENHADA deste corpo e pra onde ele olha. Lidos pelos decalques.
	///
	/// A VIDA NAO MORA MAIS AQUI: havia um `VidaDeTeste` alimentado pelo snapshot, e o campo de vida
	/// do snapshot saiu do jogo a pedido do dono (ver `EntityState`). Quem quer saber como este corpo
	/// esta pergunta as FERIDAS (`GameClient.Feridas`), que e grau e nao numero.
	/// </summary>
	public float AlturaDeTeste => _altitude;

	/// <summary>
	/// PRA ONDE O CORPO ESTA OLHANDO NA TELA -- o olhar do instante desenhado, e nao o do ultimo pacote
	/// (ver <see cref="AplicarMovimento"/>). Quem le isto quer a tela: o rastro na agua
	/// (`World.Decalques`), o espelho do olho, a bancada da fluidez.
	/// </summary>
	public Facing OlharDeTeste => _olharDesenhado;

	/// <summary>O olhar do ULTIMO pacote, ~`_atraso` a frente do desenhado. So bancada.</summary>
	public Facing OlharDoFioDeTeste => _facing;

	/// <summary>O `Moving` que o servidor mandou por ultimo. A bancada da fluidez so mede quadros em que ele vale.</summary>
	public bool MovendoDeTeste => _moving;

	/// <summary>O `Moving` do instante desenhado (as pernas andam quando a posicao anda). So bancada.</summary>
	public bool MovendoDesenhadoDeTeste => _movendoDesenhado;

	/// <summary>O atraso de desenho EM VIGOR, em ms (ver <see cref="AjustarAtraso"/>). So bancada.</summary>
	public double AtrasoDeTeste => _atraso;

	/// <summary>Quantas vezes a linha acabou antes da hora com o corpo andando. So bancada.</summary>
	public int InanicoesDeTeste => _inanicoes;

	/// <summary>A posicao interpolada ANTES de ir pra grade de desenho. So bancada.</summary>
	public Vector2 PosicaoExataDeTeste => _exata;

	/// <summary>
	/// O QUE ESTE CORPO ESTA FAZENDO -- a resposta do servidor, crua. Ver <see cref="_ocupacao"/>.
	/// </summary>
	public Ocupacao Ocupacao => _ocupacao;

	/// <summary>
	/// ============================ ELE NAO E DESENHO: E COLISAO ============================
	/// O ultimo valor que o snapshot trouxe. Ele nao muda um pixel deste boneco -- quem lê e a GRADE
	/// (`World.MontarGradeDeCorpos`), pra que o passo do corpo local pare neste corpo quando ele esta
	/// ocupado, inclusive voando (ver `ClasseDeCorpo.Bloqueia`).
	///
	/// **NAO E DEDUZIDO DA POSE.** A pose responde SEIS dos dez estados e nao tem como responder os
	/// outros quatro (guarda, agarrao, embate, cinematica). Deduzir aqui seria a lista escrita duas
	/// vezes, uma em cada ponta -- ver `EntityState.Ocupacao`.
	/// ==================================================================================
	/// </summary>
	private Ocupacao _ocupacao;

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

	/// <summary>A hora (do servidor) que esta sendo desenhada agora.</summary>
	private double TempoDeDesenho() => Relogio.AgoraNoServidor() - _atraso;

	/// <summary>
	/// O ATRASO ADAPTATIVO. O alvo sobe um tique a cada inanicao (a linha acabou com o corpo andando),
	/// ate o teto; desce um tique a cada <see cref="RelaxaAposMs"/> sem inanicao, ate a base. O valor
	/// em vigor desliza ate o alvo -- ver <see cref="DeslizeDoAtraso"/>.
	///
	/// SO CONTA INANICAO COM O CORPO ANDANDO E COM LINHA DE VERDADE (duas amostras ou mais): um corpo
	/// parado cujo cliente parou de mandar (nocaute) vive com a linha "acabada" e continua parado,
	/// que e o certo; e um corpo recem-cravado tem uma amostra so ate o proximo snapshot. Contar
	/// qualquer um dos dois subiria o atraso ate o teto por um corpo que nao precisava de atraso nenhum.
	/// </summary>
	private void AjustarAtraso(double deltaMs)
	{
		_semInanicaoHa += deltaMs;
		if (_semInanicaoHa >= RelaxaAposMs && _atrasoAlvo > AtrasoBase)
		{
			_atrasoAlvo = Math.Max(AtrasoBase, _atrasoAlvo - DegrauDoAtraso);
			_semInanicaoHa = 0;
		}
		double passo = deltaMs * DeslizeDoAtraso;
		_atraso += Math.Clamp(_atrasoAlvo - _atraso, -passo, passo);
	}

	private void Inanicao()
	{
		_inanicoes++;
		_semInanicaoHa = 0;
		_atrasoAlvo = Math.Min(AtrasoTeto, _atrasoAlvo + DegrauDoAtraso);
	}

	public override void _Process(double delta)
	{
		AjustarAtraso(delta * 1000.0);
		if (_linha.Count == 0) return;

		double quando = TempoDeDesenho();
		Amostra fim = _linha[^1];

		// AINDA NAO HA PASSADO SUFICIENTE (o corpo acabou de entrar em campo, ou acabou de ser
		// cravado): fica na primeira amostra em vez de deslizar de um lugar onde nunca esteve.
		if (quando <= _linha[0].T) { Mostrar(_linha[0].Pos, _linha[0].Altura, _linha[0], Vector2.Zero); return; }

		if (quando > fim.T)
		{
			// O PACOTE ATRASOU e o instante de desenho passou da ultima amostra. Extrapola ATE UM
			// TIQUE com a ultima velocidade -- e o tamanho de um pacote perdido, e o erro maximo que
			// se aceita corrigir depois -- e alem disso segura: inventar posicao mais longe poe o
			// corpo onde ele talvez nao va, e o preco do erro e um puxao pra tras quando o pacote
			// chega. Cada vez que isto acontece com o corpo andando o atraso sobe um degrau.
			if (_moving && _linha.Count >= 2)
			{
				Inanicao();
				Amostra a = _linha[^2];
				double vao = fim.T - a.T;
				if (vao > 0)
				{
					float dtx = (float)Math.Min(quando - fim.T, TickMs);
					Vector2 v = (fim.Pos - a.Pos) / (float)vao;
					Mostrar(fim.Pos + v * dtx, fim.Altura, fim, v);
					return;
				}
			}
			Mostrar(fim.Pos, fim.Altura, fim, Vector2.Zero);
			return;
		}

		// O PAR QUE CERCA O INSTANTE. Varre de tras pra frente porque o que se procura esta quase
		// sempre no fim -- o caso comum e a ultima ou a penultima.
		for (int i = _linha.Count - 1; i > 0; i--)
		{
			if (_linha[i - 1].T > quando) continue;

			Amostra a = _linha[i - 1], b = _linha[i];
			double vao = b.T - a.T;
			float f = vao > 0.0001 ? (float)Math.Clamp((quando - a.T) / vao, 0, 1) : 1f;
			// O OLHAR DO TRECHO [a, b] -- ver `OlharDoTrecho`. As pernas andam se QUALQUER ponta andava: o
			// ultimo trecho ate o ponto de parada ainda e percorrido, e o primeiro depois da partida tambem.
			Vector2 rumo = b.Pos - a.Pos;
			Amostra trecho = b with { Olhar = OlharDoTrecho(a, b, rumo), Movendo = a.Movendo || b.Movendo, Correndo = a.Correndo || b.Correndo };
			Mostrar(a.Pos.Lerp(b.Pos, f), Mathf.Lerp(a.Altura, b.Altura, f), trecho, rumo);
			return;
		}

		Mostrar(fim.Pos, fim.Altura, fim, Vector2.Zero);
	}

	/// <summary>
	/// PRA ONDE O CORPO OLHA ENQUANTO PERCORRE O TRECHO [a, b]. Quando as duas pontas concordam, e isso.
	/// Quando a VIRADA aconteceu dentro do trecho (o input mudou entre um envio e o outro), o desenho
	/// percorre uma reta que mistura os dois sentidos -- e o olhar que nao mente e o do EIXO DOMINANTE
	/// dessa reta: o corpo olha pra onde esta indo. Usar sempre o olhar de `b` fazia o corpo virar ate
	/// um tique antes de dobrar (58 px de deslize virado no supervoo); usar o de `a` o faria dobrar antes
	/// de virar. Se o eixo dominante nao casa com nenhuma das pontas (rumo em diagonal), vale `b`.
	/// </summary>
	private static Facing OlharDoTrecho(in Amostra a, in Amostra b, Vector2 rumo)
	{
		if (a.Olhar == b.Olhar || rumo == Vector2.Zero) return b.Olhar;
		Facing pelaMaioria = Math.Abs(rumo.X) >= Math.Abs(rumo.Y)
			? (rumo.X >= 0 ? Facing.East : Facing.West)
			: (rumo.Y >= 0 ? Facing.South : Facing.North);
		return pelaMaioria == a.Olhar ? a.Olhar : b.Olhar;
	}

	/// <summary>Posicao e altura interpoladas do MESMO par -- o corpo anda, sobe e VIRA no mesmo relogio.</summary>
	private void Mostrar(Vector2 onde, float altitude, in Amostra fonte, Vector2 rumo)
	{
		// (Com o defeito injetado o olhar ja foi aplicado na chegada; aplicar aqui de novo o desfaria e
		// a bancada mediria o conserto por baixo dos panos.)
		if (!DefeitoViradaNaChegada) AplicarMovimento(fonte.Olhar, fonte.Movendo, fonte.Correndo, rumo);
		_exata = onde;
		Desenhar(onde);
		Altura(altitude);
	}

	/// <summary>
	/// O OLHAR, O ANDAR E A CORRIDA DO INSTANTE DESENHADO -- so trabalha quando algo muda, porque o
	/// `SetMotion` troca de animacao e o rastro de corrida liga/desliga um efeito. `rumo` e o sentido do
	/// trecho desenhado (o borrao da corrida o le); zero quer dizer "parado, guarde o ultimo".
	/// </summary>
	private void AplicarMovimento(Facing olhar, bool movendo, bool correndo, Vector2 rumo)
	{
		if (rumo != Vector2.Zero) _rumoDoDesenho = rumo;
		bool estreia = !_movimentoAplicado;
		_movimentoAplicado = true;
		if (estreia || olhar != _olharDesenhado || movendo != _movendoDesenhado)
		{
			_olharDesenhado = olhar;
			_movendoDesenhado = movendo;
			_visual.SetMotion(olhar, movendo);
		}
		if (estreia || correndo != _correndoDesenhado)
		{
			_correndoDesenhado = correndo;
			// CORRENDO VEM DO SERVIDOR (`EntityState.Correndo`), nao de uma conta de velocidade: a
			// velocidade de andar de cada um sai do `Espeed` dele e quem era rapido andando passava
			// de qualquer corte fixo, deixando rastro de corrida sem apertar shift.
			_visual.Correr(correndo, _rumoDoDesenho);
			if (GetNodeOrNull<RastroDeCorrida>("Rastro") is { } rastro) rastro.Definir(correndo);
		}
	}

	/// <summary>
	/// PÕE O NO NA GRADE DE DESENHO -- a MESMA do corpo local (ver `LocalPlayer.NoPontoDaGrade`).
	///
	/// ============================ POR QUE ISTO NAO ERA PRECISO ANTES ============================
	/// O projeto ligava `snap_2d_transforms_to_pixel`, e o motor arredondava a posicao de todo
	/// objeto 2D na hora de desenhar. So que ele arredonda em pixel de MUNDO, e essa grade e
	/// `zoom` vezes mais grossa que a da TELA -- era ela que obrigava o cenario a rolar de 2 em 2
	/// px de tela no zoom 2 e produzia o tremor que o dono relatou. Com o arredondamento do motor
	/// desligado, quem nao se colocar na grade fica em subpixel, e arte de pixel em subpixel com
	/// filtro `Nearest` tem texel de largura irregular.
	///
	/// A posicao CRUA continua sendo a da interpolacao -- ela e recalculada do zero a cada quadro a
	/// partir da linha do tempo, entao arredondar aqui nao acumula erro nenhum.
	/// ==========================================================================================
	/// </summary>
	private void Desenhar(Vector2 onde) => Position = LocalPlayer.NoPontoDaGrade(onde, World.GradeDeDesenho);
}

/// <summary>
/// O RELOGIO DO SERVIDOR, VISTO DAQUI. `AgoraNoServidor() = meu relogio + deslocamento`, com o
/// deslocamento estimado pelo MAXIMO em janela de (servidorMs do snapshot - meu relogio na chegada):
/// a amostra do pacote que chegou mais rapido e a que mais se aproxima do deslocamento puro. Ver
/// <see cref="DeslocamentoDeRelogio"/> -- e o mesmo estimador que o servidor usa pro relogio de
/// cada cliente, com o extremo trocado.
///
/// O que ele entrega e um relogio que anda parelho com o do servidor e fica ATRAS dele pela menor
/// latencia de descida observada. Isso e proposital: quem desenha quer saber "que horas o servidor
/// marcava no pacote mais recente que pode ter chegado", e nao a hora absoluta.
/// </summary>
public sealed class RelogioDoServidor
{
	/// <summary>
	/// O MEU relogio, trocavel pela bancada. Nulo em producao (o relogio de quadro abaixo). A bancada
	/// da fluidez precisa avancar o tempo a mao, com quadros irregulares, e alimentar snapshots
	/// sinteticos em horas escolhidas -- nada disso e possivel contra o relogio de verdade.
	/// </summary>
	internal static Func<double>? RelogioDeTeste;

	/// <summary>
	/// O RELOGIO DE QUADRO DO CLIENTE: a soma dos `delta` do `_Process`, em ms. E o relogio em que o
	/// corpo local integra a posicao que manda (`MoveRules.Advance(_pos, dir, delta, ...)`) e o
	/// relogio em que a tela e desenhada -- ver "os relogios do fio sao relogios de quadro" em
	/// `Protocol`. O QPC diria a hora real; o que interessa aqui e a hora EM QUE A POSICAO FOI
	/// CALCULADA e a hora em que o quadro sera visto, e as duas sao esta.
	/// </summary>
	private static double _quadrosMs;

	/// <summary>
	/// Avanca o relogio de quadro. Quem chama e o `GameClient._Process` -- o primeiro autoload, logo o
	/// primeiro `_Process` de todo quadro, com ou sem mundo de pe. Nao pode viver nos corpos: eles nao
	/// processam enquanto a zona carrega, e um relogio parado enquanto snapshots chegam envenena o
	/// estimador por um minuto (ver o comentario la).
	/// </summary>
	public static void AvancarQuadro(double deltaSeconds) => _quadrosMs += deltaSeconds * 1000.0;

	public static double AgoraLocalMs() => RelogioDeTeste?.Invoke() ?? _quadrosMs;

	private readonly DeslocamentoDeRelogio _deslocamento = new(maximo: true, janelaMs: 2000);
	private long _ultimoServidorMs;
	private bool _temUltimo;

	/// <summary>
	/// O cabecalho de um snapshot acabou de chegar. Devolve o valor DESEMBRULHADO (o fio leva 32 bits,
	/// que dao a volta em 49 dias de servidor de pe) e alimenta o deslocamento.
	/// </summary>
	public long AoChegarSnapshot(uint servidorMs)
	{
		long s = Desembrulhar(servidorMs);
		long local = (long)Math.Round(AgoraLocalMs());
		_deslocamento.Amostrar(s - local, local);
		return s;
	}

	/// <summary>
	/// O relogio do servidor AGORA. Usa o deslocamento SUAVIZADO (`Deslizar`): o extremo cru flipa
	/// um quadro inteiro (17 ms) toda vez que uma janela vence numa rede local, e cada flip puxaria
	/// a hora de desenho de todo corpo em 17 ms de uma vez -- a 1760 px/s, 30 px num quadro.
	/// </summary>
	public double AgoraNoServidor()
	{
		double local = AgoraLocalMs();
		return local + _deslocamento.Deslizar((long)Math.Round(local));
	}

	/// <summary>Esquece tudo -- a bancada chama entre cenarios; em producao o proprio estimador se zera num salto.</summary>
	public void Zerar()
	{
		_deslocamento.Zerar();
		_temUltimo = false;
	}

	/// <summary>
	/// Os 32 bits do fio viram 64 pela DIFERENCA com sinal: `(int)(v - ultimo)` da o passo certo tanto
	/// na volta do contador quanto num pacote fora de ordem, e um servidor que reiniciou (o relogio
	/// dele volta perto de zero) vira um passo negativo grande, que o estimador reconhece como salto.
	/// </summary>
	private long Desembrulhar(uint v)
	{
		if (!_temUltimo)
		{
			_ultimoServidorMs = v;
			_temUltimo = true;
			return v;
		}
		int passo = unchecked((int)(v - (uint)_ultimoServidorMs));
		_ultimoServidorMs += passo;
		return _ultimoServidorMs;
	}
}
