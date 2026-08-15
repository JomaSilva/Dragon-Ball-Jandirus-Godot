using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// UM ATAQUE DE KI NA TELA -- e ele NAO decide nada.
///
/// ============================ ELE E UM ESPELHO, E SO ISSO ============================
/// Nao integra posicao, nao testa colisao, nao sabe quanto doeu e nao adivinha aonde o tiro vai
/// parar. Recebe a posicao da CABECA (e, no raio, o fim do rastro) do servidor a 30 Hz e desliza
/// ate ela. Se o servidor calar, ele para -- que e a leitura honesta de "perdi o pacote".
///
/// A INTERPOLACAO E DE DESENHO, NAO DE VERDADE: ela existe porque 30 Hz de posicao num objeto que
/// anda a 320 px/s da saltos de 10 px, e o olho pega. O ponto que ACERTA e sempre o do servidor;
/// este node pode estar meio quadro atras dele, e e por isso que a explosao vem no pacote de morte
/// (com a posicao certa) em vez de sair daqui quando o sprite "chega".
/// ====================================================================================
///
/// ============================ AGORA ELE TEM A ARTE DO BYOND, E A PRIMITIVA VIROU A REDE ============================
/// O cabecalho antigo dizia: *"a bola e um circulo com halo e o raio e uma faixa com nucleo claro,
/// porque isso escala pra QUALQUER comprimento de rastro sem esticar arte. Quando houver folha
/// propria, ela entra por cima -- o formato do dado (cabeca + cauda) nao muda."* Foi exatamente
/// isso: as folhas ja estavam convertidas no disco (229 `.tres`, com `origin`/`tail`/`head`/`end`/
/// `struggle` por direcao) e nenhuma linha de C# as citava. **Vinte e quatro tecnicas desenhavam
/// DUAS formas.**
///
/// O rastro continua sendo cabeca + cauda -- e por isso o comprimento continua livre: o corpo do
/// raio e a folha `tail` LADRILHADA entre as duas, do jeito que o DM ladrilha os segmentos do trem.
/// A primitiva nao foi apagada: ela e o desenho de quando a folha falta (arte desconhecida, `.tres`
/// que nao importou). Um tiro sem arte tem que aparecer -- invisivel e indistinguivel de "a tecnica
/// nao funciona".
/// ==============================================================================================================
///
/// ============================ O VOCABULARIO E O DO `objects.dm`, E ELE ENCAIXOU SOZINHO ============================
/// O beam do BYOND e um TREM de segmentos e cada um sabe que parte do raio ele e (`objects.dm`):
///
///     `origin`   -- o segmento colado na mao de quem canaliza (`:126`, `:261`)
///     `tail`     -- o corpo, todo segmento do meio (`:128`, `:266`)
///     `head`     -- a PONTA, o unico denso, o unico que acerta (`:162`, `:232`)
///     `end`      -- com que estado um segmento NASCE, e o de quem nao tem ninguem atras (`:73`, `:222`)
///     `struggle` -- quando a ponta esta empurrando contra um corpo (`:287`)
///
/// O port representa o mesmo trem como UM objeto com duas pontas, e as tres primeiras caem
/// exatamente onde estao: `head` na cabeca, `origin` na cauda, `tail` no meio. **O que NAO se
/// desenha e declarado**: `end` e `struggle` dependem de estado que nao viaja no snapshot
/// (`Esvaziando` e `Encostado` sao do servidor). Nao ha invencao aqui -- ha dois estados de arte
/// esperando o dia em que alguem decidir que valem um bit.
/// ============================================================================================================
///
/// ============================ O TREM DO BYOND MORA NA GRADE, E FOI ISSO QUE PICOTOU ============================
/// O dono relatou *"o beam ta PICOTADO quando ele e lancado"*. A fresta foi MEDIDA em foto pela
/// familia 4 do <see cref="RoboDeArteDeKi"/>: 13 px num rastro de 205 e 25 px num de 249, sempre
/// **colada na mao**, nunca no meio. A lei era exata: `fresta = comprimento mod passo`.
///
/// A causa e que no BYOND nao existe "comprimento de raio". O beam e um trem de `/obj/attack/blast`,
/// **um por TILE**: cada segmento que troca de tile deixa um clone no `oldloc` (`objects.dm:112-148`)
/// e o `origin` e um tile como qualquer outro (`:118-126`). Todo mundo mora na grade, os centros ficam
/// a um tile um do outro, e la e geometricamente impossivel abrir fresta.
///
/// Aqui a cauda e a MAO do dono, que anda em float. O desenho antigo ladrilhava o corpo numa grade
/// ancorada na CABECA (`i * passo`) e cravava o `origin` no float exato da cauda -- mas PARAVA UM
/// PEDACO ANTES (`for(i=1; i&lt;quantos)`), reservando aquele lugar pra mao. So que a mao nao esta
/// naquele lugar: ela esta no float, mais adiante, e o que ficava entre os dois era o resto da
/// divisao. Pior no lancamento -- entre um e dois passos o laco nao rodava nenhuma vez e sobravam
/// cabeca e mao soltas, que e o instante que o dono fotografou.
///
/// O conserto e deixar a grade IR ATE A MAO: os corpos vao de `passo` em `passo` ate o ultimo que
/// ainda cabe, e a mao fecha o vao que sobra -- que agora e MENOR que um passo por construcao, e por
/// isso nao pode abrir fresta. Ver <see cref="MontarTrem"/>.
///
/// E o passo continua sendo passo FIXO, e nao "comprimento dividido por n": um passo que dependesse
/// do comprimento faria o corpo inteiro escorregar enquanto o raio cresce (o comprimento muda 3,3
/// vezes por segundo durante a canalizacao), e o feixe ficaria fervendo. Na grade fixa so o ULTIMO
/// pedaco se mexe -- e no BYOND nem esse, porque la a mao tambem esta na grade.
/// ==========================================================================================================
///
/// ============================ E O PASSO SAI DA MEDIDA DA FOLHA, NAO DE UM 32 CRAVADO ============================
/// Duas medidas mandam no passo, e a menor vence (<see cref="MedirTrem"/>):
///
///   1. **O passo do DM** -- um TILE, projetado no eixo do feixe: `32*(|ux|+|uy|)`. Da 32 px nas
///      cardeais e 45,25 nas diagonais, que e a distancia real entre centros de tiles vizinhos na
///      diagonal -- o mesmo √2 que o `dir_matrix` (`objects.dm:52-54`) embute na matriz das quatro
///      entradas diagonais. O `wavemult` **nao entra**: no DM ele so engorda o sprite
///      (`A.transform *= wavemult`, `beams.dm:149`) e o espacamento do trem continua sendo um tile --
///      e por isso que o Final Flash e um MURO (sprite 4x sobreposto) e nao um trem esparramado.
///
///   2. **O que a arte desta folha PINTA de fato**, medido no alpha (<see cref="SombraDaArte"/>) e no
///      mesmo eixo. Isto nao e zelo: das 16 folhas de raio portadas, as cardeais pintam a celula
///      inteira, mas a diagonal da `Beam3` -- a folha PADRAO do `beams.dm:4` -- pinta **34,0 px**
///      continuos contra os 45,25 do tile. Andar o passo do tile ali abre 11 px de fresta em CADA
///      junta de todo raio diagonal. E as celulas tambem nao sao todas de 32: a `Beam - Big Fire` e
///      64 e a `Beam14` e 96 -- um "32" cravado no codigo nao saberia de nenhum dos dois.
///
/// As tres juntas do trem (cabeca-corpo, corpo-corpo, corpo-mao) entram na conta separadas, porque as
/// tres artes tem alcances proprios: quem manda e a junta mais curta.
/// ==============================================================================================================
/// </summary>
public partial class ProjetilDesenhado : Node2D
{
	/// <summary>Raio da bola, em pixels. Meio tile -- o mesmo <c>Projetil.RaioDeImpacto</c>.</summary>
	private const float RaioDaBola = Projetil.RaioDeImpacto;

	/// <summary>Meia largura do raio. Mais fino que a bola: um beam corta, uma bola estoura.</summary>
	private const float MeiaLarguraDoRaio = 9f;

	/// <summary>
	/// Quanto do erro se fecha por segundo. Alto o bastante pra nao "arrastar" atras do servidor
	/// (o tiro e rapido e o atraso apareceria como o efeito nascendo longe da mao), baixo o
	/// bastante pra tirar o serrilhado dos 30 Hz.
	/// </summary>
	private const float Suavizacao = 22f;

	public TipoDeProjetil Tipo;

	/// <summary>A cor do ki de QUEM ATIROU -- ver <see cref="Aura.CorDaFicha"/>. Nao vem no pacote.</summary>
	public Color Cor = Aura.CorDoKiCru;

	/// <summary>
	/// QUAL ARTE ESTE TIRO USA -- ela veio no pacote de nascimento. Guardada porque o nome do estado
	/// de uma bola depende dela (`ArteDeProjetil.EstadoDeBola`), e nao so a folha.
	/// </summary>
	public ArteDeKi Arte;

	/// <summary>
	/// A FOLHA DESTE TIRO, ja carregada. Nula = desenhe pela primitiva (ver o cabecalho).
	///
	/// E resolvida UMA VEZ, no nascimento (`World.AoNascerTiro`), e nao a cada `_Draw`: a arte de um
	/// tiro nao muda depois que ele sai da mao, e `ResourceLoader.Load` por quadro por tiro seria o
	/// mesmo erro que o cache do <see cref="ArteDeKiNoCliente"/> existe pra evitar.
	/// </summary>
	public SpriteFrames? Folha;

	/// <summary>
	/// O TAMANHO DO SPRITE -- o `A.transform *= wavemult` do DM (`beams.dm:149`), que e o que faz o
	/// Final Flash ser um muro e o Ki Wave um fio. Ver `Core.Combat.Projetil.EscalaVisual`.
	/// </summary>
	public float Escala = 1f;

	/// <summary>
	/// A QUE ALTURA ELE VOA, em pixels de MUNDO -- a do dono no instante do disparo, vinda no pacote
	/// de nascimento (<see cref="Jandirus.Net.NascimentoDeProjetil.Altitude"/>).
	///
	/// ============================ ELA LEVANTA O DESENHO, E SO O DESENHO ============================
	/// A `Position` deste node continua sendo a que o servidor mandou. Ela e lida por coisas que
	/// moram no CHAO -- a onda da agua (`World.TickDaAguaDosTiros` pergunta "que celula e esta?"), o
	/// Y-sort dos atores, o efeito de morte --, e subir o node moveria todas elas junto. E a mesma
	/// disciplina do corpo, que deixa o node no chao e sobe os FILHOS (ver `SubirComOVoo.Aplicar`, e
	/// a nota dele sobre a queixa da "hitbox que pega longe").
	///
	/// SEM ISTO o feixe de quem voa era desenhado no plano do chao enquanto o corpo estava ate 160 px
	/// acima (`Voo.AlturaMaxima` 640 x `Voo.EscalaNaTela` 0,25): o tiro saia da SOMBRA do atirador. O
	/// node do tiro nao e filho do corpo (ele mora direto no `Atores`, porque ele sobrevive ao dono e
	/// anda sozinho), entao a subida dos filhos nunca o alcancou.
	/// =========================================================================================
	/// </summary>
	public float Altitude;

	/// <summary>Quanto o desenho sobe, em pixel de TELA -- a mesma conta do corpo que voa.</summary>
	private Vector2 SubidaNaTela => new(0, -Altitude * Voo.EscalaNaTela);

	/// <summary>
	/// SEGUNDOS DESDE QUE ELE NASCEU -- e so isso que a animacao da folha precisa.
	///
	/// Um `AnimatedSprite2D` por segmento daria dezenas de nodes por raio (o corpo de um beam de 30
	/// tiles sao 30 ladrilhos), cada um com o proprio relogio e o proprio quadro. Um relogio so, e o
	/// quadro calculado na hora de desenhar, deixa o trem inteiro em FASE -- que e como o BYOND o
	/// mostra, ja que la todos os segmentos nascem do mesmo `.dmi` andando junto.
	/// </summary>
	private double _idade;

	private Vector2 _cabecaAlvo, _caudaAlvo;
	private Vector2 _cabeca, _cauda;
	private bool _primeiro = true;

	/// <summary>Pra onde ele vai, normalizado. Ver <see cref="Mirar"/> -- ele NAO vem no pacote.</summary>
	private Vector2 _rumo = Vector2.Down;

	/// <summary>
	/// PRA ONDE ESTE TIRO ESTA INDO, no vocabulario de quatro direcoes do BYOND.
	///
	/// ============================ POR QUE UM TIRO PRECISA RESPONDER ISTO ============================
	/// A onda da agua (`KiWater`) tem DOIS recortes -- "ns" e "ew" --, e quem escolhe entre eles e o
	/// `Decalques.Escolher`, a partir de um `Facing`. Ate aqui so CORPOS abriam onda, e o
	/// `World.DirecaoDe` tinha duas respostas: o corpo local e o remoto. Um raio nao e corpo: ele
	/// caia no `_ => South` e desenharia SEMPRE o eixo norte-sul -- que e literalmente o defeito que
	/// o dono ja fotografou uma vez (com o corpo do jogador, e por isso aquele switch existe).
	///
	/// O nome e o MESMO que os dois corpos publicam de proposito: quem le a direcao de uma coisa na
	/// tela le a mesma coisa nas tres.
	/// ==============================================================================================
	/// </summary>
	public Facing OlharDeTeste => MoveRules.FacingFrom(new Vec2(_rumo.X, _rumo.Y), Facing.South);

	/// <summary>
	/// A ULTIMA CELULA EM QUE ELE JA MOLHOU o chao -- a cota deste node dentro do tique dos
	/// decalques. Ver <see cref="EntrouNaCelula"/>.
	/// </summary>
	private Vector2I _ultimaCelula = new(int.MinValue, int.MinValue);

	/// <summary>
	/// ELE ACABOU DE ENTRAR NESTA CELULA? Devolve verdadeiro UMA vez por celula nova.
	///
	/// ============================ ISTO E O TETO DO EFEITO, E E POR ISSO QUE ELE E BARATO ============================
	/// Sem esta guarda, o tique dos decalques perguntaria "esta celula e agua?" uma vez por tiro POR
	/// QUADRO -- e `World.EhAgua` varre as camadas do tilemap. Com ela, um tiro que nao trocou de
	/// celula custa a comparacao de dois inteiros, e o pior caso vira "quantas celulas o tiro cruzou
	/// neste quadro", que a 60 Hz e no maximo duas ate pro raio mais rapido do jogo.
	///
	/// A celula e consumida mesmo FORA da agua, e e o certo: entrar numa celula de grama tambem e
	/// entrar numa celula, e a pergunta seguinte ("ja molhei esta?") so faz sentido pra celula nova.
	/// ==============================================================================================================
	/// </summary>
	public bool EntrouNaCelula(Vector2I c)
	{
		if (c == _ultimaCelula) return false;
		_ultimaCelula = c;
		return true;
	}

	/// <summary>
	/// PENDURA A ARTE -- e e AQUI que a tinta e ligada, nao no construtor do node.
	///
	/// ============================ O MATERIAL SO ENTRA SE HOUVER FOLHA ============================
	/// O `Ki.gdshader` SOMA a tinta em cima da textura, e num `DrawLine`/`DrawCircle` a "textura" do
	/// Godot e um pixel BRANCO. Com o material sempre ligado, a primitiva de emergencia sairia
	/// branca -- ou seja o tiro perderia a cor de quem atirou exatamente no caso em que ele ja perdeu
	/// a arte. Sem material, `DrawCircle` pinta com a cor que recebe, e e o que a primitiva sempre fez.
	///
	/// Duas maneiras de colorir para duas maneiras de desenhar; ligar as duas ao mesmo caminho e o
	/// que faria uma delas mentir.
	/// =======================================================================================
	/// </summary>
	public void Vestir(ArteDeKi arte, float escala)
	{
		Arte = arte;
		Folha = ArteDeKiNoCliente.Folha(arte);
		Escala = escala > 0 ? escala : 1f;
		_folhaEmCache = null;   // trocou de folha: os nomes de animacao guardados sao de outra arte
		if (Folha == null) { Material = null; return; }

		var sh = ResourceLoader.Load<Shader>(CaminhoDoShaderDeKi);
		if (sh == null) { Folha = null; return; }   // sem shader, a primitiva colorida e melhor

		var m = new ShaderMaterial { Shader = sh };
		// `icon += rgb(blastR,blastG,blastB)` (`beams.dm:132`). A folha e cinza; a cor e do dono.
		m.SetShaderParameter("tinta", new Vector3(Cor.R, Cor.G, Cor.B));
		Material = m;
	}

	private const string CaminhoDoShaderDeKi = "res://Assets/Shaders/Ki.gdshader";

	/// <summary>O servidor falou: e para AQUI que o tiro esta indo.</summary>
	public void Mirar(Vector2 cabeca, Vector2 cauda)
	{
		// ============================ O RUMO NAO VIAJA NO FIO, E NAO PRECISA ============================
		// O `ProjetilState` leva a cabeca e -- so no raio -- a CAUDA, que e o fim do rastro. O rumo do
		// raio e a subtracao das duas, exata e de graca: zero byte a mais no snapshot, que sai 30
		// vezes por segundo pra cada zona.
		//
		// PRA BOLA a cauda nao viaja (o `Read` faz `Cauda = Pos`), entao a subtracao da zero e o rumo
		// sai do PASSO: a cabeca de agora menos a cabeca do pacote anterior. E o dado do SERVIDOR e
		// nao a posicao interpolada -- ler `Position` daria um vetor tremido pelo `Lerp` perto do
		// alvo, e um recorte de onda que pisca entre "ns" e "ew" com o tiro andando reto.
		//
		// PARADO MANTEM O ULTIMO RUMO (a mina da `Ki_Bomb` nasce sem rumo nenhum): o campo so e
		// reescrito quando ha vetor, que e a mesma regra do `FacingFrom` pros corpos.
		// ==============================================================================================
		Vector2 rastro = cabeca - cauda;
		if (rastro.LengthSquared() > 1f) _rumo = rastro.Normalized();
		else if (!_primeiro && (cabeca - _cabecaAlvo).LengthSquared() > 1e-4f)
			_rumo = (cabeca - _cabecaAlvo).Normalized();

		_cabecaAlvo = cabeca;
		_caudaAlvo = cauda;

		// O PRIMEIRO PACOTE NAO SE INTERPOLA. Sem isto todo tiro nasceria na origem do mundo e
		// voaria ate a mao do dono -- um risco atravessando o mapa, uma vez por disparo.
		if (!_primeiro) return;
		_cabeca = cabeca;
		_cauda = cauda;
		_primeiro = false;
	}

	/// <summary>
	/// A LUZ QUE ESTE TIRO LANCA NO MUNDO -- o pedido do dono foi *"beams e ataque de ki deveriam ter
	/// LUZ PROPRIA"*. Quem sabe de luz e a <see cref="LuzDeKi"/>; aqui so se diz DE QUEM ela e.
	///
	/// ============================ AQUI, E NAO NO `_Process` ============================
	/// A luz e FILHA deste node, na posicao local zero -- ou seja, na CABECA, porque `Position` deste
	/// node e a cabeca (ver o `_Process`). Ela anda junto de graca: nenhuma linha por quadro copiando
	/// coordenada, e nenhuma chance de a luz ficar pra tras do sprite que ela acompanha. E ela morre
	/// junto pelo mesmo motivo, sem ninguem lembrar de apaga-la.
	///
	/// A CABECA E O UNICO PONTO ACESO, e nao o rastro inteiro. Um beam de trinta tiles com uma luz
	/// por pedaco seriam trinta luzes num tiro so -- e a cabeca e a parte densa (`KHH()`), a que
	/// acerta, a que o jogador esta olhando. Iluminar o rastro custaria uma ordem de grandeza pra
	/// acrescentar brilho onde a propria arte do feixe ja e a coisa mais clara da tela.
	///
	/// `_Ready` E O LUGAR CERTO: quando ele roda, `Cor`, `Tipo` e `Escala` ja estao escritos (o
	/// `World.AoNascerTiro` os poe no inicializador e chama `Vestir` ANTES do `AddChild`). Pendurar
	/// no construtor pegaria a cor padrao, que e o branco-azulado de quem nao tem ficha.
	/// ================================================================================
	/// </summary>
	public override void _Ready()
	{
		// ============================ O TIRO DESENHA NA FRENTE DO SPRITE, SEMPRE ============================
		// `#define KI_PLANE 6` + `obj/attack/blast { plane = KI_PLANE }` (`objects.dm:8`, `:65`), com
		// `A.layer = MOB_LAYER+2` por cima (`beams.dm:158`). Mob nenhum declara plane, ou seja todos
		// ficam no plano 0: **6 > 0 poe o tiro sobre TODO corpo, em qualquer direcao, sem condicao**.
		//
		// AQUI ISSO FALTAVA, e o defeito so aparecia pra um lado. Este node mora no `Atores`, que
		// ordena por Y, e a Y dele e a da CABECA do tiro -- que num raio pode estar trinta tiles
		// adiante. Tiro pra NORTE tem Y menor que a do dono e desenhava ATRAS do personagem; pra
		// LESTE/OESTE dava empate exato e quem ganhava era a ordem de irmao, ou seja o acaso.
		//
		// `ZIndex = 1` e a MESMA traducao que o resto da familia de ki ja usa -- `CargaDeRaioVisual`
		// (`plane = 7`) e `NaveDesenhada` (`plane = 6`). No Godot o `ZIndex` decide ANTES do Y-sort,
		// e o Y-sort continua valendo entre iguais: o cenario (ZIndex 0) segue ordenando com os
		// corpos como sempre, e quem esta atras do atirador continua vendo o feixe passar por cima
		// do chao. O unico que subiu foi o tiro.
		// ================================================================================================
		ZIndex = 1;

		LuzDeKi.Pendurar(this, Cor, Escala);

		// A LUZ SOBE COM O DESENHO. Ela e filha na posicao local zero (ver abaixo), que e a cabeca no
		// CHAO -- e o clarao de um raio disparado la em cima ficaria aceso no piso. A altura nao muda
		// depois do nascimento, entao isto se escreve uma vez e nao por quadro.
		if (Altitude > 0 && GetNodeOrNull<Node2D>(LuzDeKi.NomeDoNode) is { } luz)
			luz.Position = SubidaNaTela;
	}

	public override void _Process(double delta)
	{
		_idade += delta;
		float t = Mathf.Min(1f, (float)delta * Suavizacao);
		_cabeca = _cabeca.Lerp(_cabecaAlvo, t);
		_cauda = _cauda.Lerp(_caudaAlvo, t);

		// A POSICAO DO NODE E A CABECA, e o desenho e em coordenadas locais: assim o Y-sort do
		// `Atores` ordena o tiro pelo ponto que de fato importa (onde ele vai acertar), e nao pelo
		// meio de um rastro que pode ter trinta tiles.
		Position = _cabeca;
		QueueRedraw();
	}

	public override void _Draw()
	{
		// A SUBIDA DO VOO ENTRA COMO TRANSFORMA DE DESENHO, e nao somada em cada carimbo: sao tres
		// caminhos de desenho (a folha do trem, a folha da bola e a primitiva) e uma soma a mao em
		// cada um seria a quarta chance de esquecer um deles. Ver `Altitude` sobre por que ela nao
		// entra na `Position`.
		if (Altitude > 0) DrawSetTransform(SubidaNaTela);

		if (Folha != null && DesenharComFolha()) return;
		DesenharPrimitiva();
	}

	// =====================================================================
	// A ARTE DO BYOND
	// =====================================================================
	/// <summary>
	/// O TETO DE CORPOS de um trem, e ele e generoso: o raio mais longo do jogo tem 50 tiles (o Galick
	/// Ho no topo da pericia) e a folha mais miuda anda de 30 em 30 px, o que da 53 pedacos -- a
	/// varredura da familia 4d confirma que ate 1600 px o teto nunca morde.
	///
	/// Ele existe porque o comprimento e feito de posicao INTERPOLADA: um pacote fora de ordem pode,
	/// por um quadro, dar uma cauda absurda, e um laco sem teto ali seria um travamento de quadro por
	/// um erro de rede. Estourar o teto abre fresta (o passo passa a ser maior que a arte), e e a
	/// troca certa: um quadro feio e melhor que um quadro perdido.
	/// </summary>
	private const int TetoDePedacos = 96;

	/// <summary>
	/// DESENHA COM A FOLHA. Devolve falso quando a folha nao tem o estado pedido -- e ai o tiro cai
	/// na primitiva em vez de sumir.
	/// </summary>
	private bool DesenharComFolha()
	{
		SpriteFrames f = Folha!;
		string dir = SufixoDeDirecao(f);

		if (Tipo != TipoDeProjetil.Beam)
		{
			// A BOLA E UM DESENHO SO, e o nome do estado sai do Core (ver `ArteDeProjetil.EstadoDeBola`,
			// que explica por que ele nao e o `BLASTSTATE` literal do DM). `qualquerEstado` liga a
			// ultima tentativa da cadeia: numa folha de bola, um estado desconhecido ainda e a bola.
			Texture2D? bola = Quadro(f, ArteDeProjetil.EstadoDeBola(Arte), dir, qualquerEstado: true);
			if (bola == null) return false;
			Estampar(bola, Vector2.Zero);
			return true;
		}

		// ============================ O TREM, MONTADO COMO O DM MONTA ============================
		// Local: a CABECA e a origem do desenho e a mao esta em `fim`. O trem se preenche do fim pra a
		// cabeca -- ver o cabecalho sobre a grade do BYOND e sobre de onde sai o passo.
		AtualizarNomes(f, dir);
		Texture2D? cabeca = QuadroDaAnimacao(f, _animCabeca, _idade);
		if (cabeca == null) return false;

		Vector2 fim = _cauda - _cabeca;
		float comprimento = fim.Length();

		if (comprimento > 1f)
		{
			Vector2 paraTras = fim / comprimento;
			Texture2D? corpo = QuadroDaAnimacao(f, _animCorpo, _idade);

			// A MAO. `end` E A SEGUNDA ESCOLHA, e o DM concorda: e o estado com que todo segmento
			// nasce (`objects.dm:73`) e o que ele assume quando nao ha nada atras (`:222`). Folha sem
			// `origin` (algumas `Techniques` sao assim) fica com o fecho certo em vez de nenhum; folha
			// sem os dois fecha com o proprio corpo, que e melhor que fechar com nada.
			Texture2D? mao = QuadroDaAnimacao(f, _animMao, _idade) ?? corpo;

			MedidaDoTrem m = MedirTrem(f, paraTras, Escala,
									   _animCabeca, _animCorpo, _animMao ?? _animCorpo);

			// ENQUANTO A MAO ESTA DEBAIXO DA PROPRIA CABECA, o raio E a cabeca. Desenhar a mao aqui
			// empilharia duas artes quase no mesmo pixel -- que e o que o guarda antigo (`> passo`)
			// queria evitar, so que com um numero cravado em vez do alcance medido da arte.
			if (comprimento > m.AlcanceDaCabeca && mao != null)
			{
				TremDoRaio trem = MontarTrem(comprimento, m.Passo, m.SemCorpo);

				// A GRADE VAI ATE ONDE CABE; o que sobra e da mao, e sobra menos que um passo.
				if (corpo != null)
					for (int i = 1; i <= trem.Corpos; i++)
						Estampar(corpo, paraTras * (i * trem.Passo));

				// A MAO POR CIMA DO ULTIMO CORPO quando os dois calham no mesmo lugar (o comprimento
				// caiu quase em cima da grade): sao artes diferentes, e a de cima e a que fecha o raio.
				Estampar(mao, fim);
			}
		}

		// A CABECA POR ULTIMO: ela e a unica parte densa (`KHH()`), e desenhar por cima e o que
		// deixa claro, de longe, pra que lado o raio esta indo.
		Estampar(cabeca, Vector2.Zero);
		return true;
	}

	// =====================================================================
	// A GEOMETRIA DO TREM
	// =====================================================================
	/// <summary>Um trem montado: quantos CORPOS ele tem e a que passo, em pixel.</summary>
	/// <param name="Corpos">Quantos `tail`. Eles ficam em <c>i * Passo</c>, com <c>i</c> de 1 a este numero.</param>
	/// <param name="Passo">A distancia entre dois pedacos vizinhos. Nunca maior que o limite medido.</param>
	public readonly record struct TremDoRaio(int Corpos, float Passo);

	/// <summary>
	/// QUANTOS CORPOS, E A QUE PASSO -- a funcao que conserta o picote, e ela e pura de proposito (a
	/// bancada chama exatamente esta, e nao uma copia parecida).
	///
	/// ============================ O QUE MUDOU E UM `&lt;` QUE VIROU `&lt;=` ============================
	/// O laco antigo parava um pedaco antes do fim (`i &lt; comprimento/passo`) pra "deixar o lugar da
	/// mao". Mas a mao nao esta num lugar da GRADE: ela esta no float da cauda, mais adiante. Entre o
	/// ultimo corpo e ela sobrava `comprimento mod passo` -- os 13 px e os 25 px que a foto mediu,
	/// sempre colados na mao, nunca no meio.
	///
	/// Indo ate o ultimo corpo que ainda CABE (`piso(comprimento/passo)`), o que sobra pra a mao e por
	/// definicao menor que um passo, e um passo e por definicao coberto pela arte. A fresta deixa de
	/// ter onde existir sem que o passo precise mudar -- e o passo NAO muda, que e o ponto seguinte.
	///
	/// ============================ E O PASSO E FIXO, DE PROPOSITO ============================
	/// A outra saida seria dividir o comprimento em `n` vaos iguais (`passo = comprimento/n`). Fecha
	/// igual, mas o passo passa a depender do comprimento -- e o comprimento de um raio canalizando
	/// muda 3,3 vezes por segundo. O corpo inteiro escorregaria pra frente e pra tras enquanto o feixe
	/// cresce: trocar um buraco parado por trinta pedacos fervendo. Com a grade fixa, so o ULTIMO vao
	/// respira, e no BYOND nem esse (la a mao tambem mora na grade).
	/// ==================================================================================
	/// </summary>
	/// <param name="comprimento">Da cabeca ate a mao, em pixel.</param>
	/// <param name="passoLimite">O maior passo que nao abre fresta -- ver <see cref="MedirTrem"/>.</param>
	/// <param name="semCorpo">Ate onde cabeca e mao se encostam SOZINHAS, sem corpo no meio.</param>
	public static TremDoRaio MontarTrem(float comprimento, float passoLimite, float semCorpo)
	{
		float p = MathF.Max(1f, passoLimite);
		int k = Math.Clamp((int)MathF.Floor(comprimento / p), 0, TetoDePedacos);

		// O DEFEITO INJETADO, quando ha um. `Nenhum` em jogo, sempre -- ver `DefeitoDeTeste`.
		switch (DefeitoDeTeste)
		{
			case DefeitoDoTrem.UmPedacoAMenos: return new TremDoRaio(Math.Max(0, k - 1), p);
			case DefeitoDoTrem.SemLadrilho: return new TremDoRaio(0, p);
		}

		// O RAIO CURTO DEMAIS PRA UM CORPO INTEIRO, mas comprido demais pra cabeca e mao se encostarem
		// sozinhas: um corpo no meio do caminho fecha os dois vaos. E o mesmo defeito de novo (um vao
		// maior que a arte), so que dentro de um passo -- e sem esta linha ele apareceria justamente no
		// nascimento do tiro, que e o instante que o dono reclamou.
		if (k == 0 && comprimento > semCorpo) return new TremDoRaio(1, comprimento * 0.5f);

		return new TremDoRaio(k, p);
	}

	/// <summary>
	/// ============================ OS DOIS JEITOS DE PICOTAR UM RAIO, INJETAVEIS ============================
	/// **`Nenhum` EM JOGO, SEMPRE** -- ninguem alem da bancada escreve aqui, e com `Nenhum` o `switch`
	/// do <see cref="MontarTrem"/> nao roda e a producao nao muda um bit.
	///
	/// Ele existe porque uma sonda de fresta que nunca viu fresta nao prova nada. A familia 4 desta
	/// casa mede "0 px" em oito fotos -- e mediria os mesmos 0 px se estivesse contando o fundo errado,
	/// se a faixa fosse larga demais pra cair no buraco, ou se o proprio laco de varredura tivesse
	/// parado no primeiro passo. **Injetar o defeito e a unica maneira de a sonda provar que sabe ficar
	/// vermelha**, e este projeto ja pagou por acreditar em verde de graca: quatro defeitos visuais
	/// passaram por quatro mil checagens porque a bancada media INTENCAO.
	///
	///   `UmPedacoAMenos` -- **o defeito que o dono fotografou**, exato. O laco antigo era
	///                       `for (i = 1; i &lt; comprimento/passo)`: parava um pedaco antes do fim
	///                       "reservando o lugar da mao", e sobrava `comprimento mod passo` colado nela.
	///                       Ele tambem nao tinha o resgate do raio curto, e por isso este ramo devolve
	///                       antes dele -- do lancamento (entre um e dois passos) sobravam cabeca e mao
	///                       soltas, que e o instante da foto.
	///   `SemLadrilho`    -- o corpo desligado inteiro: so cabeca e mao, o buraco do tamanho do raio.
	///                       Nao e um bug que existiu; e o CONTROLE, o defeito grande que a sonda tem
	///                       que enxergar mesmo se estiver cega pro pequeno.
	/// ====================================================================================================
	/// </summary>
	public enum DefeitoDoTrem
	{
		/// <summary>Producao.</summary>
		Nenhum,

		/// <summary>O `&lt;` que virou `&lt;=`, ao contrario: um `tail` a menos.</summary>
		UmPedacoAMenos,

		/// <summary>Sem corpo nenhum: cabeca e mao, e o vazio entre elas.</summary>
		SemLadrilho,
	}

	/// <summary>Ver <see cref="DefeitoDoTrem"/>. `Nenhum` em jogo, sempre.</summary>
	public static DefeitoDoTrem DefeitoDeTeste = DefeitoDoTrem.Nenhum;

	/// <summary>O que a arte desta folha permite: o passo, o alcance da cabeca e o vao sem corpo.</summary>
	public readonly record struct MedidaDoTrem(float Passo, float SemCorpo, float AlcanceDaCabeca);

	/// <summary>
	/// UM PIXEL DE TELA DE SOBREPOSICAO EM CADA JUNTA -- o imposto da grade de pixel, e ele foi MEDIDO.
	///
	/// Encostar exatamente (junta de largura zero) funciona quando o passo e INTEIRO: as duas estampas
	/// ficam com a mesma fase dentro do pixel e o rasterizador reparte a coluna limite entre elas sem
	/// sobrar nada. Na diagonal o passo nao e inteiro (34 px no eixo sao 24,04 px em x e em y), a fase
	/// escorrega de uma estampa pra a seguinte e a amostragem come a coluna da borda: a foto do raio
	/// diagonal de 253 px mediu **1 px de fresta a 17 px da cabeca**, que e exatamente a junta da
	/// cabeca com o primeiro corpo encostando sem sobrepor.
	///
	/// No BYOND o problema nao existe porque la tudo cai em pixel inteiro (`pixel_x` e int e o passo e
	/// um tile). Aqui a estampa nasce de posicao interpolada, entao a junta precisa de um pixel de
	/// folga -- e um pedaco a mais a cada 32 px de raio, que e o que o teste de custo mede.
	///
	/// ============================ E ELE E EM PIXEL DE TELA, NAO DE EIXO -- A FOTO LONGA COBROU ============================
	/// A primeira versao subtraia `1` do passo e pronto. So que o passo mora no EIXO DO FEIXE, e um
	/// pixel de eixo na diagonal e **0,707 px em x e 0,707 px em y** -- ou seja MENOS DE UM PIXEL de
	/// sobreposicao de verdade em qualquer das duas telas. Duas estampas vizinhas em fases diferentes
	/// dentro do pixel podem entao perder a coluna da borda as duas, e a junta abre.
	///
	/// Ele nao aparecia porque e RARO e depende da fase: com o passo diagonal em 33,0 px o deslocamento
	/// por pedaco e 23,33 px em cada tela, e so uma fase em tres perde a coluna. As fotos diagonais que
	/// esta bancada tinha (190 e 253 px, cinco e sete juntas) nunca calharam nela. A foto do dono
	/// reproduzida de verdade -- 1374 px, 42,9 tiles, **41 juntas** -- calhou: uma unica costura de
	/// 1 px a 49 px da cabeca, e ela some inteira na largura do feixe.
	///
	/// A conversao e exata e nao um chute: deslocar-se de `d` no eixo desloca `d*|ux|` em x e `d*|uy|`
	/// em y, entao pra garantir N pixels de tela no eixo dominante a margem no eixo tem que ser
	/// `N / max(|ux|, |uy|)`. Cardeal fica em N, diagonal em N*1,414.
	///
	/// ============================ E N E **DOIS**, PORQUE UM E EXATAMENTE O ORCAMENTO DO ARREDONDAMENTO ============================
	/// `project.godot` liga `snap_2d_vertices_to_pixel`: cada borda de estampa e arredondada pra o
	/// pixel mais proximo, e um arredondamento erra ate meio pixel. Sao DUAS bordas por junta (o fim de
	/// uma estampa e o comeco da seguinte) e elas podem errar pra lados opostos: **o arredondamento
	/// come ate um pixel inteiro**. Com a folga em 1 px, a junta no pior caso fica encostando em zero --
	/// e zero de folga e onde o defeito mora.
	///
	/// Isto nao e teoria: a folga de 1 px foi medida em DOZE diagonais longas (479 juntas) e deixou
	/// **7 costuras de 1 px**. Com 2 px as mesmas 479 juntas fecham. O preco e um pedaco a mais a cada
	/// 30 px de raio em vez de a cada 31 -- duas estampas a mais no raio mais longo do jogo (53 -> 55),
	/// que o teste de custo mede.
	/// ==========================================================================================================================
	/// </summary>
	private const float Margem = 2f;

	/// <summary>
	/// A <see cref="Margem"/> convertida pra o eixo deste rumo. Ver o bloco acima: a folga tem que
	/// valer um pixel de TELA, e no eixo isso custa mais quando o feixe anda na diagonal.
	/// </summary>
	private static float MargemNoEixo(Vector2 paraTras) =>
		Margem / MathF.Max(0.001f, MathF.Max(MathF.Abs(paraTras.X), MathF.Abs(paraTras.Y)));

	/// <summary>
	/// MEDE O TREM DESTA FOLHA NESTE RUMO -- o passo do DM contra o que a arte cobre, e o menor vence.
	/// Ver o bloco do cabecalho sobre as duas medidas.
	///
	/// AS TRES JUNTAS SAO CONTADAS SEPARADAS (cabeca-corpo, corpo-corpo, corpo-mao) porque as tres
	/// artes tem alcance proprio: uma cabeca redonda que so pega 24 px abriria fresta contra um corpo
	/// que pega 32, e um passo tirado so do corpo nao veria isso.
	/// </summary>
	public static MedidaDoTrem MedirTrem(SpriteFrames f, Vector2 paraTras, float escala,
										 string? animCabeca, string? animCorpo, string? animMao)
	{
		// O PASSO DO DM: um tile, projetado no eixo do feixe. Cardeal 32, diagonal 45,25 -- que e a
		// distancia real entre centros de tiles vizinhos, o √2 que o `dir_matrix` embute.
		float doTile = ZoneCollision.TileSize
					   * (MathF.Abs(paraTras.X) + MathF.Abs(paraTras.Y));

		(float aC, float bC) = Alcance(f, animCabeca, paraTras, escala);
		(float aT, float bT) = Alcance(f, animCorpo, paraTras, escala);
		(float aM, float _) = Alcance(f, animMao, paraTras, escala);

		// Uma junta entre um pedaco em `dA` e o seguinte em `dB` so nao tem fresta se
		// `dA + alcancePraTrasDeA >= dB + alcancePraFrenteDeB`, ou seja se o passo couber em `b - a`.
		float passo = MathF.Min(doTile, MathF.Min(bT - aT, MathF.Min(bC - aT, bT - aM)));

		float margem = MargemNoEixo(paraTras);
		return new MedidaDoTrem(MathF.Max(1f, passo - margem),
								MathF.Max(0f, bC - aM - margem), MathF.Max(0f, bC));
	}

	/// <summary>
	/// ATE ONDE ESTA ARTE CHEGA no eixo do feixe, em pixel e ja na escala do tiro: <c>a</c> e a borda
	/// da frente (negativa, em direcao a cabeca) e <c>b</c> a de tras (positiva, em direcao a mao).
	///
	/// A MEDIDA E RELATIVA AO CENTRO DA CELULA porque e assim que o <see cref="Estampar"/> poe o
	/// quadro -- e assim que o DM poe tambem (`pixel_x=-((W/2)-16)`, `objects.dm:85`).
	/// </summary>
	private static (float A, float B) Alcance(SpriteFrames f, string? anim, Vector2 paraTras, float escala)
	{
		if (anim == null) return (0f, 0f);

		(float a, float b) = SombraDaArte(f, anim, paraTras);
		return (a * escala, b * escala);
	}

	// =====================================================================
	// A MEDIDA DA FOLHA, NO PIXEL
	// =====================================================================
	/// <summary>
	/// A sombra da arte no eixo, por (folha, animacao, fatia de rumo). Ver <see cref="SombraDaArte"/>.
	/// </summary>
	private static readonly Dictionary<(ulong Folha, string Anim, int Fatia), (float A, float B)> Sombras = [];

	/// <summary>Em quantas fatias o rumo e arredondado pra guardar a medida. 64 = 5,6 graus.</summary>
	private const int FatiasDeRumo = 64;

	/// <summary>
	/// A SOMBRA DA TINTA NO EIXO DO FEIXE: de onde ate onde esta arte pinta SEM BURACO, medida no
	/// alpha do quadro. E este numero, e nao o tamanho da celula, que diz onde o proximo pedaco pode
	/// ficar sem abrir fresta.
	///
	/// ============================ POR QUE A CAIXA DA ARTE NAO SERVE, E A FOTO PROVOU ============================
	/// A primeira versao desta medida projetava a CAIXA (o `GetUsedRect`) no eixo. Nas cardeais da no
	/// mesmo; nas diagonais MENTE. A caixa da `tail_northwest` da `Beam3` e 28x28, e projetada na
	/// diagonal da 39,6 px -- mas a tinta la dentro e uma faixa que nao chega nos cantos da caixa: a
	/// sombra CONTINUA dela e 34,0 px. O trem andou 38 e a foto mediu **4 px de fresta**, exatamente a
	/// diferenca. Uma medida derivada da caixa fica verde com o desenho picotado.
	///
	/// COMO SE MEDE: cada pixel opaco e um quadradinho de lado 1; a sombra dele no eixo e um intervalo
	/// de `|ux|+|uy|` px (1 na cardeal, 1,41 na diagonal -- sem isso uma linha de pixels na diagonal
	/// pareceria uma fileira de furos). Marcam-se os bins de 1 px que cada quadradinho cobre e toma-se
	/// a MAIOR CORRIDA continua; entre os quadros da animacao vale a INTERSECCAO, que e o pedaco que
	/// todo quadro pinta -- assim o passo nao respira junto com a animacao.
	///
	/// CUSTA UMA VEZ por (folha, animacao, fatia de rumo), e o resultado fica no dicionario pra
	/// sempre. Ver o custo medido na familia 4d da `--diagartedeki`.
	/// ========================================================================================================
	/// </summary>
	public static (float A, float B) SombraDaArte(SpriteFrames f, string anim, Vector2 eixo)
	{
		float ang = MathF.Atan2(eixo.Y, eixo.X);
		int fatia = Mathf.PosMod((int)MathF.Floor((ang + MathF.PI) / (MathF.Tau / FatiasDeRumo)),
								 FatiasDeRumo);

		(ulong, string, int) chave = (f.GetInstanceId(), anim, fatia);
		if (Sombras.TryGetValue(chave, out (float A, float B) pronta)) return pronta;

		float meio = (MathF.Abs(eixo.X) + MathF.Abs(eixo.Y)) * 0.5f;
		float a = float.NegativeInfinity, b = float.PositiveInfinity;
		bool algum = false;
		Vector2 celula = Vector2.Zero;

		int n = f.GetFrameCount(anim);
		for (int i = 0; i < n; i++)
		{
			Texture2D? q = f.GetFrameTexture(anim, i);
			if (q == null) continue;
			if (celula == Vector2.Zero) celula = q.GetSize();

			(float ini, float fim)? corrida = CorridaDoQuadro(q, eixo, meio);
			if (corrida == null) continue;

			// INTERSECCAO ENTRE QUADROS: o pedaco que TODOS pintam.
			a = MathF.Max(a, corrida.Value.ini);
			b = MathF.Min(b, corrida.Value.fim);
			algum = true;
		}

		// A REDE: a celula inteira projetada no eixo. E o que vale quando o pixel nao pode ser lido, e
		// e o palpite do DM (o passo cai no do tile, que e o outro limite).
		if (!algum || b - a < 1f)
		{
			if (celula.X < 1f) celula = new Vector2(ZoneCollision.TileSize, ZoneCollision.TileSize);
			float m = (MathF.Abs(eixo.X) * celula.X + MathF.Abs(eixo.Y) * celula.Y) * 0.5f;
			(a, b) = (-m, m);
		}

		Sombras[chave] = (a, b);
		return (a, b);
	}

	/// <summary>
	/// A maior corrida CONTINUA de tinta de um quadro no eixo, relativa ao centro da celula. Nulo = o
	/// pixel deste quadro nao deu pra ler.
	/// </summary>
	private static (float Ini, float Fim)? CorridaDoQuadro(Texture2D q, Vector2 eixo, float meio)
	{
		Image? img = ImagemDoQuadro(q);
		if (img == null) return null;

		Vector2I tam = img.GetSize();
		if (tam.X <= 0 || tam.Y <= 0) return null;

		// UMA COPIA EM RGBA8 e uma leitura de byte: `GetPixel` por pixel atravessaria a fronteira do
		// motor umas dez mil vezes por quadro de folha.
		if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
		byte[] dados = img.GetData();
		if (dados.Length < tam.X * tam.Y * 4) return null;

		int alcance = (int)MathF.Ceiling(MathF.Abs(eixo.X) * tam.X + MathF.Abs(eixo.Y) * tam.Y) + 4;
		var tem = new bool[2 * alcance + 1];
		int desloc = alcance;
		int menor = int.MaxValue, maior = int.MinValue;

		for (int y = 0; y < tam.Y; y++)
			for (int x = 0; x < tam.X; x++)
			{
				if (dados[((y * tam.X) + x) * 4 + 3] == 0) continue;

				float s = (x - tam.X * 0.5f + 0.5f) * eixo.X + (y - tam.Y * 0.5f + 0.5f) * eixo.Y;
				int k0 = (int)MathF.Floor(s - meio), k1 = (int)MathF.Floor(s + meio);
				for (int k = k0; k <= k1; k++)
				{
					int j = k + desloc;
					if (j < 0 || j >= tem.Length) continue;
					tem[j] = true;
					if (k < menor) menor = k;
					if (k > maior) maior = k;
				}
			}

		if (menor > maior) return null;

		int melhorIni = menor, melhorFim = menor, ini = menor;
		for (int k = menor; k <= maior + 1; k++)
		{
			if (k <= maior && tem[k + desloc]) continue;
			if (k - 1 - ini > melhorFim - melhorIni) { melhorIni = ini; melhorFim = k - 1; }
			ini = k + 1;
		}

		// O bin `k` cobre `[k, k+1)`: a corrida vai da borda de tras do primeiro ate a da frente do
		// ultimo.
		return (melhorIni, melhorFim + 1);
	}

	/// <summary>O ultimo atlas aberto. Ver <see cref="ImagemDoQuadro"/>.</summary>
	private static ulong _atlasAberto;
	private static Image? _imagemDoAtlas;

	/// <summary>
	/// A imagem de UM quadro. Nulo = nao deu pra ler.
	///
	/// ============================ O ATLAS E ABERTO UMA VEZ POR ANIMACAO ============================
	/// As folhas de ki sao `SpriteFrames` de `AtlasTexture`: todo quadro e um recorte da MESMA imagem.
	/// `GetImage()` num recorte desses descomprime a imagem inteira, e medir oito quadros seguidos
	/// descomprimiria a mesma coisa oito vezes. A memoria de um atlas so (o ultimo) resolve, porque os
	/// quadros de uma animacao sao sempre lidos em sequencia.
	/// ==========================================================================================
	/// </summary>
	private static Image? ImagemDoQuadro(Texture2D q)
	{
		if (q is not AtlasTexture at || at.Atlas == null)
		{
			Image? direta = q.GetImage();
			return direta == null || direta.IsEmpty() ? null : direta;
		}

		ulong id = at.Atlas.GetInstanceId();
		if (id != _atlasAberto || _imagemDoAtlas == null)
		{
			_imagemDoAtlas = at.Atlas.GetImage();
			_atlasAberto = id;
		}
		if (_imagemDoAtlas == null || _imagemDoAtlas.IsEmpty()) return null;

		var r = new Rect2I((Vector2I)at.Region.Position, (Vector2I)at.Region.Size);
		r = r.Intersection(new Rect2I(Vector2I.Zero, _imagemDoAtlas.GetSize()));
		if (r.Size.X <= 0 || r.Size.Y <= 0) return null;

		return _imagemDoAtlas.GetRegion(r);
	}

	// =====================================================================
	// OS NOMES, RESOLVIDOS UMA VEZ POR RUMO
	// =====================================================================
	/// <summary>
	/// AS TRES ANIMACOES DESTE TIRO NESTE RUMO, ja resolvidas.
	///
	/// Elas mudam quando o tiro vira (o rumo troca de fatia) e quando a folha troca -- e nao a cada
	/// quadro. Sem esta memoria, cada `_Draw` de cada tiro montaria seis strings (`$"{estado}_{dir}"`
	/// duas vezes por estado) so pra chegar no mesmo nome de sempre; com varios feixes no ar isso e
	/// lixo por quadro que nao paga nada.
	/// </summary>
	private string? _animCabeca, _animCorpo, _animMao;
	private string _dirEmCache = " ";
	private SpriteFrames? _folhaEmCache;

	private void AtualizarNomes(SpriteFrames f, string dir)
	{
		if (dir == _dirEmCache && ReferenceEquals(f, _folhaEmCache)) return;

		_animCabeca = Achar(f, "head", dir);
		_animCorpo = Achar(f, "tail", dir);
		_animMao = Achar(f, "origin", dir) ?? Achar(f, "end", dir);
		_dirEmCache = dir;
		_folhaEmCache = f;
	}

	/// <summary>
	/// Poe um quadro CENTRADO em <paramref name="onde"/>, ja na escala do tiro.
	///
	/// CENTRADO e nao ancorado nos pes: o DM faz `pixel_x = -((W/2)-16)` no nascimento do segmento
	/// (`objects.dm:~122`), ou seja centra a folha larga sobre o tile. Um raio nao pisa no chao.
	/// </summary>
	private void Estampar(Texture2D tex, Vector2 onde)
	{
		Vector2 t = tex.GetSize() * Escala;
		DrawTextureRect(tex, new Rect2(onde - t * 0.5f, t), false);
	}

	/// <summary>
	/// O SUFIXO DE DIRECAO QUE ESTA FOLHA TEM. Oito quando ela tem oito, quatro quando tem quatro,
	/// vazio quando a folha e de uma direcao so (`Kienzan.dmi` e `28.dmi` sao assim).
	///
	/// A PERGUNTA E FEITA A FOLHA e nao a uma tabela, porque a resposta varia POR ARQUIVO: das folhas
	/// desta tabela, umas tem 8 direcoes, outras 4 e duas nenhuma. Uma constante daria estado
	/// inexistente em um terco delas -- e o `HasAnimation` falharia calado, apagando o tiro.
	/// </summary>
	private string SufixoDeDirecao(SpriteFrames f) =>
		SufixoDeDirecao(f, new Vec2(_rumo.X, _rumo.Y));

	/// <summary>
	/// A PERGUNTA E FEITA AOS NOMES DA FOLHA, e nao a um estado especifico.
	///
	/// A primeira versao disto perguntava `head_&lt;dir&gt;` ou `default_&lt;dir&gt;`, e falhava na
	/// `KiHead.dmi` -- que tem oito... nao, QUATRO direcoes, mas em estados chamados `paralysis` e
	/// `1`..`9`. A resposta era "esta folha nao tem direcao", e a busca seguinte procurava
	/// `paralysis` sem sufixo, que tambem nao existe: o tiro sumia. Perguntar pelo SUFIXO -- que e o
	/// que de fato se quer saber -- vale pra qualquer folha, tenha ela o estado que tiver.
	/// </summary>
	public static string SufixoDeDirecao(SpriteFrames f, Vec2 rumo)
	{
		string[] nomes = f.GetAnimationNames();
		string oito = Oito(rumo);
		if (TemSufixo(nomes, oito)) return oito;

		string quatro = MoveRules.FacingSuffix(MoveRules.FacingFrom(rumo, Facing.South));
		return TemSufixo(nomes, quatro) ? quatro : "";
	}

	private static bool TemSufixo(string[] nomes, string dir)
	{
		foreach (string n in nomes)
			if (n.EndsWith($"_{dir}", StringComparison.Ordinal)) return true;
		return false;
	}

	/// <summary>
	/// AS OITO DIRECOES DO BYOND. `Y` cresce pra o SUL, que e a mesma convencao do
	/// <see cref="MoveRules.FacingFrom"/> -- e a razao de o eixo vertical parecer invertido aqui e
	/// no resto do projeto: em tela o Y cresce pra baixo, e pra baixo e o sul.
	///
	/// O CORTE E EM 22,5 GRAUS (tan de 67,5 = 2,414): dentro dessa fatia o vetor conta como puro
	/// norte/sul/leste/oeste; fora dela, diagonal. E o mesmo corte que o BYOND faz ao converter um
	/// angulo em `dir`, e usar 45 graus (o corte "obvio") daria diagonal pra quase todo tiro que sai
	/// um pixel fora do eixo.
	/// </summary>
	private static string Oito(Vec2 d)
	{
		float ax = MathF.Abs(d.X), ay = MathF.Abs(d.Y);
		if (ax < 1e-6f && ay < 1e-6f) return "south";

		const float Fatia = 2.414213562f;   // tan(67,5 graus)
		bool soX = ay <= ax / Fatia;
		bool soY = ax <= ay / Fatia;

		if (soX) return d.X >= 0 ? "east" : "west";
		if (soY) return d.Y >= 0 ? "south" : "north";
		return d.Y >= 0
			? (d.X >= 0 ? "southeast" : "southwest")
			: (d.X >= 0 ? "northeast" : "northwest");
	}

	/// <summary>
	/// UM QUADRO DE UM ESTADO, no instante certo da animacao. Nulo = esta folha nao tem esse estado.
	///
	/// A ANIMACAO E DO RELOGIO DO TIRO (<see cref="_idade"/>) e nao de um `AnimatedSprite2D`: ver o
	/// comentario daquele campo. `GetAnimationSpeed` e a cadencia que o conversor gravou; o piso de
	/// 1 fps existe porque uma folha com velocidade zero congelaria numa divisao por zero.
	/// </summary>
	private Texture2D? Quadro(SpriteFrames f, string estado, string dir, bool qualquerEstado = false) =>
		QuadroDe(f, estado, dir, _idade, qualquerEstado);

	/// <summary>
	/// A MESMA BUSCA, ESTATICA -- e ela e publica pra a bancada poder chamar EXATAMENTE o que o
	/// desenho chama.
	///
	/// ============================ UMA BANCADA QUE PERGUNTA OUTRA COISA NAO PROVA NADA ============================
	/// A primeira versao desta bancada tinha a propria copia da busca ("a folha tem `head` ou
	/// `default` em alguma direcao?"), e por isso ela reprovou a `KiHead.dmi` com um motivo QUASE
	/// certo -- a folha era usavel, so nao pelo nome que a copia procurava. Duas buscas parecidas sao
	/// duas chances de o teste ficar verde com o jogo errado (e de ficar vermelho com o jogo certo,
	/// que foi o caso). Agora ha uma so.
	/// ========================================================================================================
	///
	/// A CADEIA DE TENTATIVAS, nesta ordem:
	///   1. `estado_direcao`  -- o caso normal;
	///   2. `estado`          -- folha de uma direcao so (`Kienzan.dmi`, `28.dmi`);
	///   3. `default_direcao` -- o nome que o conversor da ao estado ANONIMO de um `.dmi`;
	///   4. `default`;
	///   5. QUALQUER animacao (so quando <paramref name="qualquerEstado"/>).
	///
	/// O PASSO 5 E SO PRA BOLA, e a restricao e o que impede um estrago sutil: numa folha de RAIO o
	/// primeiro estado alfabetico e `bend_left_*` (a curva), e cair nele desenharia o corpo do feixe
	/// com o quadro da curva -- um raio torto e inteiro, que ninguem leria como defeito de arte.
	/// Numa folha de BOLA nao existe essa armadilha: seja qual for o estado, o desenho e a bola.
	/// </summary>
	public static Texture2D? QuadroDe(SpriteFrames f, string estado, string dir, double idade,
									  bool qualquerEstado = false) =>
		QuadroDaAnimacao(f, NomeDaAnimacao(f, estado, dir, qualquerEstado), idade);

	/// <summary>
	/// O NOME DA ANIMACAO que serve pra este estado neste rumo -- a cadeia de tentativas descrita em
	/// <see cref="QuadroDe"/>, separada do quadro porque o nome muda com o RUMO e o quadro muda com o
	/// RELOGIO. Ver <see cref="AtualizarNomes"/>: o desenho resolve o nome uma vez por rumo.
	/// </summary>
	public static string? NomeDaAnimacao(SpriteFrames f, string estado, string dir,
										 bool qualquerEstado = false) =>
		Achar(f, estado, dir)
		?? Achar(f, "default", dir)
		?? (qualquerEstado ? PrimeiroCom(f, dir) : null);

	/// <summary>O quadro desta animacao no instante certo. Nulo = nao ha animacao ou ela esta vazia.</summary>
	public static Texture2D? QuadroDaAnimacao(SpriteFrames f, string? anim, double idade)
	{
		if (anim == null) return null;

		int n = f.GetFrameCount(anim);
		if (n <= 0) return null;
		if (n == 1) return f.GetFrameTexture(anim, 0);

		// `GetAnimationSpeed` e a cadencia que o conversor gravou. O piso de 1 fps existe porque uma
		// folha com velocidade zero congelaria numa divisao por zero.
		double fps = Math.Max(f.GetAnimationSpeed(anim), 1);
		return f.GetFrameTexture(anim, (int)(idade * fps) % n);
	}

	private static string? Achar(SpriteFrames f, string estado, string dir)
	{
		if (dir.Length > 0 && f.HasAnimation($"{estado}_{dir}")) return $"{estado}_{dir}";
		return f.HasAnimation(estado) ? estado : null;
	}

	/// <summary>
	/// A PRIMEIRA animacao que serve pra esta direcao. Prefere a que TEM o sufixo pedido -- sem isso
	/// uma folha de oito direcoes desenharia sempre o mesmo lado.
	/// </summary>
	private static string? PrimeiroCom(SpriteFrames f, string dir)
	{
		string[] nomes = f.GetAnimationNames();
		if (dir.Length > 0)
			foreach (string n in nomes)
				if (n.EndsWith($"_{dir}", StringComparison.Ordinal)) return n;
		return nomes.Length > 0 ? nomes[0] : null;
	}

	// =====================================================================
	// O DESENHO DE QUANDO A FOLHA FALTA
	// =====================================================================
	/// <summary>
	/// A PRIMITIVA -- o desenho que este arquivo sempre teve, agora como REDE e nao como regra.
	///
	/// Ela cobre tres casos, e nenhum deles pode virar tiro invisivel: arte que este cliente nao
	/// conhece (numero de enum de uma versao mais nova), `.tres` que nao importou, e a lamina de ar
	/// do Kiai -- que **no proprio DM sai sem folha nenhuma** (ver a nota final da
	/// `ArteDeProjetil`). Nos tres o jogador ve o tiro.
	/// </summary>
	private void DesenharPrimitiva()
	{
		Color nucleo = Cor.Lightened(0.55f);
		Color halo = Cor with { A = 0.35f };

		if (Tipo == TipoDeProjetil.Beam)
		{
			Vector2 fim = _cauda - _cabeca;   // local: a cabeca e a origem
			DrawLine(Vector2.Zero, fim, halo, MeiaLarguraDoRaio * 2.6f);
			DrawLine(Vector2.Zero, fim, Cor, MeiaLarguraDoRaio * 1.6f);
			DrawLine(Vector2.Zero, fim, nucleo, MeiaLarguraDoRaio * 0.7f);

			// A PONTA E MAIS GORDA que o corpo do raio -- e a "cabeca" do `KHH()`, e e o que deixa
			// claro, olhando de longe, para que lado o beam esta indo.
			DrawCircle(Vector2.Zero, MeiaLarguraDoRaio * 1.5f, halo);
			DrawCircle(Vector2.Zero, MeiaLarguraDoRaio, nucleo);
			return;
		}

		DrawCircle(Vector2.Zero, RaioDaBola * 1.7f, halo);
		DrawCircle(Vector2.Zero, RaioDaBola, Cor);
		DrawCircle(Vector2.Zero, RaioDaBola * 0.45f, nucleo);
	}
}
