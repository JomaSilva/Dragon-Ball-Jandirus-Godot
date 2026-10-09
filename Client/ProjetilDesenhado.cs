using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using static Jandirus.Client.ArteDeKiNoCliente;

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
/// anda a 640 px/s da saltos de 21 px, e o olho pega. O ponto que ACERTA e sempre o do servidor: o
/// LUGAR da explosao vem no pacote de morte, e nunca de onde este node estiver. So o INSTANTE dela
/// espera o desenho -- este node anda atras do servidor, e o que o servidor anuncia de um tiro so
/// aparece quando o desenho chega la (ver <see cref="AnuncioSemEsperaDeTeste"/>).
/// ====================================================================================
///
/// ============================ O DESENHO E SHADER, E NAO MAIS A FOLHA DO BYOND ============================
/// Ate 2026-10-07 este node era o TREM do `objects.dm`: tres folhas (`origin` na mao, `tail`
/// ladrilhada no meio, `head` na ponta) carimbadas ao longo do eixo, escolhidas entre oito (ou
/// quatro) direcoes pelo nome. As folhas nunca giravam -- e fora dos eixos de uma folha cada carimbo
/// escorregava de lado em relacao ao anterior. O dono viu no trailer e descreveu o defeito e a
/// saida na mesma frase:
///
///   *"a colisao de ki entre npcs, parece q eles as vezes se mexem durante a colisao ou fazem colisao
///   na diagonal e o beam fica todo torto, acho q uma solucao melhor pros beams seria ao inves de usar
///   sprite pra cada beam como era no byond, fazer eles por efeitos, noise e shaders do proprio godot,
///   assim em colisao eles podem ter o efeito q to mandando nas imagens, q o beam se deforma ao se
///   chocar... alem de beam na diagonal tb n ficar aquela coisa picotada e poder ser continuo em todas
///   as direcoes e poder ate dar curva. Pra outros ataques de ki isso tb seria interessante trocar
///   sprite por puro efeito, noise e shaders em esferas como genkidama, super nova, blast etc"*
///
/// Entao o raio hoje e UMA FITA da mao ate a ponta, pintada pelo `FeixeDeKi.gdshader`, e a bola e UM
/// QUADRO pintado pelo `EsferaDeKi.gdshader`. O que saiu junto com as folhas: a medida do passo do
/// trem, a sombra de alpha de cada arte, os sufixos de direcao e a primitiva de emergencia -- quatro
/// mecanismos que so existiam porque uma folha tem tamanho, direcao e pode faltar no disco.
///
/// O QUE NAO MUDOU e o formato do dado: cabeca + cauda, como sempre. E a ARTE continua valendo -- ela
/// deixou de escolher um arquivo e passou a escolher um estilo (ver <see cref="ArteDeKiNoCliente"/>).
/// ========================================================================================================
///
/// ============================ O QUE SE VE E O QUE ACERTA, E ISSO E MEDIDO ============================
/// Duas medidas do desenho sao REGRA do servidor, e este node as le do mesmo lugar que ele
/// (<see cref="PintorDeKi.Medir"/>): a ponta do raio fica a `Feixe.AlcanceDaCabeca` da posicao do
/// tiro, e o tronco tem `ArteDeProjetil.MeiaEspessuraDoTronco` de cada lado do eixo. O servidor planta
/// a cabeca encostada em quem ela empurra e corta o tronco na beirada POR ESSES NUMEROS -- e a
/// bancada `--diagartedeki` (familia 6) confere, na FOTO e arte por arte, que a ponta e o tronco
/// desenhados estao onde as duas tabelas dizem.
/// ====================================================================================================
/// </summary>
public partial class ProjetilDesenhado : Node2D
{
	/// <summary>
	/// Quanto do erro se fecha por segundo. Alto o bastante pra nao "arrastar" atras do servidor
	/// (o tiro e rapido e o atraso apareceria como o efeito nascendo longe da mao), baixo o
	/// bastante pra tirar o serrilhado dos 30 Hz.
	/// </summary>
	private const float Suavizacao = 22f;

	// =====================================================================
	// O DESENHO ANDA ATRAS -- e o que o servidor ANUNCIA espera por ele (2026-10-08)
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada): o que o servidor anuncia de um tiro aparece na CHEGADA do pacote, sem
	/// esperar o desenho -- o sulco nasce a frente da ponta do raio e a bola some antes do ponto em que
	/// estoura. E o jogo de antes de 2026-10-08. Sempre falso em jogo.
	///
	/// ============================ A RAIZ E UMA SO ============================
	/// O `Lerp` do `_Process` deixa a cabeca desenhada um TEMPO atras da do servidor, e esse tempo nao
	/// depende da velocidade do tiro -- a DISTANCIA e que cresce com ela. Quando os tiros ficaram mais
	/// rapidos (o bloco "A VELOCIDADE" de `Core.Combat.Projetil`), o atraso em pixel passou de 5 pra 23
	/// na bola comum e de 14 pra 29 no raio comum, quase um tile, e dois anuncios do canal confiavel
	/// passaram a chegar VISIVELMENTE antes de o desenho chegar la:
	///
	///   * a MARCA NO CHAO (`Protocol.Decal.Sulco`), carimbada debaixo da cabeca de verdade
	///     (`GameServer.MarcarSulcoDoTiro`): aparecia a frente da ponta enquanto o raio esticava;
	///   * a MORTE, que traz o ponto de verdade em que o tiro acabou. E o tiro morto nem viaja no
	///     snapshot do tique em que morre (`GameServer.TirosDaZona`), entao o ultimo alvo deste node
	///     ainda e o do tique ANTERIOR: a bola sumia quase um tile antes de onde o estouro aparecia.
	///
	/// A REGRA: o anuncio espera o desenho chegar la. A marca fica na fila do `World` enquanto estiver
	/// no vao entre as duas cabecas (<see cref="AindaNaoChegouEm"/>); a bola voa o ultimo trecho ate o
	/// ponto da morte, e so entao o `World` a recolhe e solta o estouro (<see cref="VoarAteOFim"/>).
	///
	/// ============================ O QUE NAO SE FEZ, E POR QUE ============================
	/// EXTRAPOLAR (andar a frente do ultimo pacote) fecharia o vao e quebraria a promessa do cabecalho:
	/// a cabeca do raio entraria no corpo de quem ela acerta. SUBIR a <see cref="Suavizacao"/> encurta o
	/// atraso e devolve o serrilhado dos 30 Hz que ela existe pra tirar. E um RELOGIO ("espere
	/// 1 / Suavizacao") erraria pros dois lados: o atraso de verdade muda com a taxa de quadros (a conta
	/// do `Lerp` atras de um alvo de 30 Hz da 35 ms a 60 quadros por segundo, uns 52 a 144 e 12 a 30), e
	/// a cabeca que LEVA alguem e desenhada ainda mais atras, na linha do tempo do corpo (a ancora do
	/// `_Process`).
	/// Quem sabe onde o desenho esta e o desenho -- entao a pergunta e feita a ele.
	/// ====================================================================================
	/// </summary>
	public static bool AnuncioSemEsperaDeTeste;

	/// <summary>Menos que isto nao se ve: e a folga de "chegou", em px.</summary>
	private const float MeioPixel = 0.5f;

	/// <summary>
	/// De lado do eixo, ate onde um ponto ainda e "do caminho deste raio", em px: um quarto de tile. As
	/// marcas do proprio raio nascem EM CIMA do eixo (`GameServer.CarimbarSulco` as interpola sobre a
	/// reta que a cabeca andou); a folga e pro rumo, que aqui sai da subtracao de dois pontos do fio.
	/// </summary>
	private const float FaixaDoCaminho = ZoneCollision.TileSize / 4f;

	/// <summary>
	/// A CABECA DESENHADA AINDA NAO CHEGOU NESTE PONTO DO CAMINHO, e a do servidor ja passou por ele?
	/// E a pergunta que a marca no chao faz antes de nascer (`World.TickDosSulcosEsperando`): verdadeiro
	/// quer dizer "voce esta no vao entre o que o servidor ja fez e o que a tela ja mostrou -- espere".
	///
	/// SO O RAIO RENTE AO CHAO responde sim: e so ele que risca o chao (`GameServer.RastroDoTiroVale`).
	/// E SO COM A CABECA DO SERVIDOR JA ADIANTE DO PONTO: uma marca a frente dela nao e deste raio (e o
	/// rastro de um corpo arremessado, ou a de um tique cujo snapshot se perdeu), e segura-la aqui
	/// atrasaria o que nao esta esperando ninguem.
	///
	/// O `World` roda ANTES deste node no quadro, entao a cabeca lida aqui e a do quadro que JA FOI
	/// desenhado: a marca nasce no quadro seguinte ao da passagem, e nunca no anterior.
	/// </summary>
	public bool AindaNaoChegouEm(Vector2 ponto)
	{
		if (Tipo != TipoDeProjetil.Beam || Altitude > 0f || !_temRumo) return false;

		Vector2 doServidor = ponto - _cabecaAlvo;
		if (doServidor.Dot(_rumo) > MeioPixel || Mathf.Abs(doServidor.Cross(_rumo)) > FaixaDoCaminho) return false;
		return (ponto - _cabeca).Dot(_rumo) > MeioPixel;
	}

	/// <summary>O ponto em que o servidor disse que este tiro acabou (nulo = ele ainda voa). Ver <see cref="VoarAteOFim"/>.</summary>
	private Vector2? _fim;

	/// <summary>A que velocidade o ultimo trecho e voado, em px/s: a que o fecha em 1 / <see cref="Suavizacao"/> segundos.</summary>
	private float _passoDoFim;

	/// <summary>
	/// O SERVIDOR DISSE ONDE ESTE TIRO ACABOU: ele voa em linha reta ate la e para. O `World` pergunta
	/// <see cref="ChegouAoFim"/> a cada quadro, e so entao recolhe o node e solta o efeito da morte.
	///
	/// ============================ EM LINHA RETA, E EM 1 / Suavizacao ============================
	/// O ponto da morte nao e mais um alvo de snapshot: e o FIM. Entregue ao `Lerp`, ele nunca seria
	/// alcancado -- o `Lerp` fecha uma FRACAO do que falta por quadro, e a bola frearia em cima do alvo
	/// sem encostar. Entao o trecho e andado em velocidade constante, a que o fecha em 1 / Suavizacao
	/// segundos: e o atraso com que o `Lerp` segue um alvo que anda, ou seja e quando a bola chegaria la
	/// se nada tivesse acontecido. O primeiro passo sai do tamanho do que o `Lerp` daria neste quadro
	/// (a mesma fracao do mesmo vao), entao a bola nao da tranco: ela so continua.
	///
	/// NAO E EXTRAPOLACAO: o ponto veio do servidor, e o node para NELE.
	///
	/// MENOS DE MEIO PIXEL NAO E TRECHO. E o caso de tudo que morre parado (a mina, a Death Ball ainda
	/// sobre a cabeca): ele ja chegou, e o `World` o recolhe no mesmo quadro, como sempre fez.
	/// ==========================================================================================
	/// </summary>
	public void VoarAteOFim(Vector2 fim)
	{
		float falta = _cabeca.DistanceTo(fim);
		if (falta <= MeioPixel) _cabeca = fim;
		_passoDoFim = falta * Suavizacao;
		_fim = fim;
	}

	/// <summary>Este tiro ja esta desenhado no ponto em que acabou? Falso enquanto o servidor nao disser que ele acabou.</summary>
	public bool ChegouAoFim => _fim is { } fim && _cabeca == fim;

	/// <summary>
	/// Meio tile: a distancia do centro de um corpo ate a frente dele. O raio nasce ali -- a cauda que o
	/// servidor manda e a BOCA, um tile a frente do corpo (`BocaDeCano.De`), e a mao fica no meio do
	/// caminho. Era o que a folha `origin` fazia, centrada na cauda e com meia celula pra tras.
	/// </summary>
	private const float MeiaCelula = ZoneCollision.TileSize / 2f;

	/// <summary>
	/// A que velocidade (px/s) o rastro de uma bola esta inteiro. Abaixo disso ele encolhe, e parada
	/// (a Death Ball crescendo sobre a cabeca, a mina) a bola nao tem rastro nenhum.
	/// </summary>
	private const float VelocidadeDoRastroCheio = 96f;

	public TipoDeProjetil Tipo;

	/// <summary>A cor do ki de QUEM ATIROU -- ver <see cref="World.CorDoKiDe"/>. Nao vem no pacote.</summary>
	public Color Cor = Aura.CorDoKiCru;

	/// <summary>QUAL ARTE ESTE TIRO USA -- ela veio no pacote de nascimento, e escolhe o estilo.</summary>
	public ArteDeKi Arte;

	/// <summary>
	/// O TAMANHO -- o `A.transform *= wavemult` do DM (`beams.dm:149`), que e o que faz o Final Flash
	/// ser um muro e o Ki Wave um fio. Ver `Core.Combat.Projetil.EscalaVisual`.
	/// </summary>
	public float Escala = 1f;

	/// <summary>
	/// A ESCALA QUE O SERVIDOR MANDOU NO ULTIMO SNAPSHOT (0 = a de nascenca; ver `ProjetilState.Escala`).
	///
	/// O RAIO ENGROSSA QUANDO QUEM O SEGURA SOBE DE PODER (dono, 2026-10-08: *"o beam vai ficar maior
	/// proporcionalmente com esse crescimento"* -- `Core.Combat.Projetil.CrescerComOPoder`). Quem decide o
	/// tamanho e o servidor, que ja encosta e disputa por ele; aqui o desenho ANDA ate la em vez de pular (ver
	/// <see cref="Reescalar"/>): o byte chega em degraus de 1/20, a 30 Hz, e escrito cru o feixe cresceria aos
	/// solavancos.
	/// </summary>
	public float EscalaDoServidor;

	/// <summary>A escala do `Nasceu`: pra onde o desenho volta quando o servidor para de mandar outra.</summary>
	private float _escalaDeNascenca = 1f;

	/// <summary>
	/// A QUE PASSO O TAMANHO DESENHADO ALCANCA O DO SERVIDOR, em tamanhos de nascenca por segundo. O dobro da
	/// rampa de la (`Projetil.CrescimentoPorSegundo`): acompanha sem ficar pra tras, e so alisa os degraus.
	/// </summary>
	private const float PassoDaEscala = 2f * (float)Projetil.CrescimentoPorSegundo;

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
	public Vector2 SubidaNaTela => new(0, -Altitude * Voo.EscalaNaTela);

	/// <summary>
	/// QUEM ATIROU (0 = ninguem que este cliente conheca). O raio SAI DA MAO dele: enquanto o dono o
	/// alimenta, a ponta de tras do desenho e o meio do caminho entre o corpo e a boca -- ver <see cref="AMao"/>.
	/// </summary>
	public int Dono;

	/// <summary>
	/// SEGUNDOS DESDE QUE ELE NASCEU. E o relogio do shader (`tempo`): um por tiro, pra que dois raios
	/// lado a lado nao pulsem juntos, e contado daqui pra que a foto de uma bancada dependa da idade
	/// do tiro e nao de ha quanto tempo o jogo esta aberto.
	/// </summary>
	private double _idade;

	private Vector2 _cabecaAlvo, _caudaAlvo;
	private Vector2 _cabeca, _cauda;
	private bool _primeiro = true;

	/// <summary>A luz do TRONCO (so raio, so de noite). Ver `LuzDeKi.Esticar` -- ela e reposicionada por quadro.</summary>
	private LuzDeKi? _luzDoTronco;

	/// <summary>Pra onde ele vai, normalizado. Ver <see cref="Mirar"/> -- ele NAO vem no pacote.</summary>
	private Vector2 _rumo = Vector2.Down;

	/// <summary>
	/// O RUMO JA E CONHECIDO? Um raio nasce com cabeca e cauda no MESMO ponto, e o rumo so existe quando
	/// o primeiro pacote as separa. A folha antiga desenhava esse instante com a arte "pra baixo" (o valor
	/// de nascenca do campo) -- um raio pro leste piscava pro sul por um quadro. Sem rumo nao se desenha
	/// raio: nao ha pra onde.
	/// </summary>
	private bool _temRumo;

	private EstiloDeFeixe _estiloDoFeixe = EstiloDoFeixe(ArteDeKi.Nenhuma);
	private EstiloDeBola _estiloDaBola = EstiloDaBola(ArteDeKi.Nenhuma);
	private PintorDeKi.MedidasDoFeixe _medidas;
	private float _raioDaBola;

	/// <summary>De 0 a 1: quanto a ponta de tras ja virou rabo. Anda atras de <see cref="Solto"/>.</summary>
	private float _soltura;

	/// <summary>De 0 a 1: a fracao do rastro cheio que esta bola merece pela velocidade dela.</summary>
	private float _veloz;
	private float _velocidade;
	private double _idadeDoAlvo;

	/// <summary>Os degraus da curva, quando o raio faz curva. Alocado na primeira vez -- quase nenhum raio precisa.</summary>
	private Vector2[]? _curva;

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
	/// NASCEU INVISIVEL -- o `A.invisibility = 1` da lamina de ar do Kiai (`Kiai.dm:40`). O no continua
	/// existindo (ele anda, explode e faz som como qualquer tiro); o que muda e se ESTE olho o desenha.
	/// </summary>
	public bool Invisivel { get; set; }

	/// <summary>
	/// MOSTRA OU ESCONDE conforme quem olha enxerga o invisivel (`Core.Combat.VisaoDoInvisivel`). Um
	/// tiro comum aparece pra todo mundo; o invisivel so pra quem tinha `see_invisible` no original.
	/// </summary>
	public void MostrarPara(bool veInvisivel) => Visible = !Invisivel || veInvisivel;

	/// <summary>
	/// NINGUEM ALIMENTA ESTE RAIO (ver `ProjetilState.Solto`): a mao so existe enquanto ha mao
	/// (`objects.dm:220-229`). Solto, a ponta de tras perde a bola da boca e afina em rabo de cometa.
	/// </summary>
	public bool Solto;

	/// <summary>
	/// O CORPO QUE ESTE RAIO ESTA LEVANDO (0 = ninguem) -- ver `ProjetilState.Arrasta`. Enquanto houver, a
	/// cabeca desenhada e PRESA na frente do corpo desenhado dele, a cada quadro (o `_Process`).
	/// </summary>
	public int ArrastaId;

	/// <summary>
	/// A PONTA ESTA PRENSADA numa disputa (ver `ProjetilState.Prensado`): contra outro feixe, ou contra
	/// as maos de quem segura o ataque na guarda. ESTE NODE NAO MUDA DE DESENHO POR ISSO -- quem le o bit e
	/// o `World.TickDosChoquesDeKi`, que poe a estrela do choque (<see cref="ChoqueDeKi"/>) em cima da ponta.
	/// O feixe ja se deformou aqui (um cone ate o encontro) e o dono dispensou: *"N precisa fazer a ponta do
	/// beam ficar maior na colisao, so a estrela no centro do choque basta"*.
	/// </summary>
	public bool Prensado;

	/// <summary>
	/// ONDE O NO DE UM CORPO ESTA DESENHADO, por id -- quem responde e o `World`. Nulo = sem mundo (a previa
	/// da mesa, as bancadas sem rede), e nem a ancora nem a mao perguntam por corpo nenhum.
	/// </summary>
	public static Func<int, Vector2?>? OndeEstaOCorpo;

	/// <summary>
	/// ESTE RAIO E A CONTINUACAO DE OUTRO (0 = nao e): o id do raio que o alimenta -- ver `ProjetilState.Dobra`.
	/// E o trecho que sai de um raio DESVIADO pelo parry, enquanto o desvio dura.
	///
	/// ============================ DOIS OBJETOS NO SERVIDOR, UM RAIO NA TELA (dono, 2026-10-08) ============================
	/// *"o deflect ele deveria ao bater no jogador e o jogador dar o deflect o beam dar curva pro lado e nao
	/// criar um novo beam atras do jogador"*. Desenhado sozinho, este trecho tem ponta de tras propria -- e o
	/// que se via era exatamente um segundo feixe. Entao, enquanto ele continua outro, ele NAO se desenha:
	/// apresenta-se ao pai a cada quadro (<see cref="_Process"/>), e e o PAI que pinta a fita inteira, da mao
	/// de quem atirou ate a ponta DESTE trecho, fazendo a curva na dobra (<see cref="DesenharDobrado"/>).
	///
	/// QUEM PINTA E O PAI de proposito: o material e o relogio sao dele, e o raio que vinha vindo nao muda de
	/// desenho no quadro em que e desviado -- a fita so passa a continuar pra outro lado.
	///
	/// Acabou o desvio (o servidor zera o campo), cada um volta a se desenhar: o pai como um raio comum, este
	/// como um raio solto que vai embora.
	/// ====================================================================================================================
	/// </summary>
	public int ContinuaDe;

	/// <summary>O NO DE UM TIRO, por id -- quem responde e o `World`. E por ele que o trecho desviado acha o pai.</summary>
	public static Func<int, ProjetilDesenhado?>? OndeEstaOTiro;

	/// <summary>O trecho desviado que me continua, e o quadro em que ele se apresentou pela ultima vez.</summary>
	private ProjetilDesenhado? _ramo;
	private long _ramoNoQuadro = -10;

	/// <summary>O quadro em que eu me apresentei a um pai -- enquanto for o de agora, quem me pinta e ele.</summary>
	private long _paiNoQuadro = -10;

	/// <summary>
	/// O quadro ainda e o de uma apresentacao? Com um de folga: o `_Process` de um e o `_Draw` do outro caem no
	/// mesmo quadro, mas a ordem entre irmaos nao e contrato -- e um quadro de fita emendada a mais nao se ve.
	/// </summary>
	private static bool Agora(long quadro) => (long)Engine.GetProcessFrames() - quadro <= 1;

	/// <summary>
	/// SEM LUZ -- a previa da mesa de tecnicas. Um tiro desenhado dentro de um painel de interface nao
	/// ilumina cenario nenhum, e pendurar uma `PointLight2D` ali gastaria uma vaga do teto de luzes de ki
	/// (`Settings.LuzesDeKi`) pra clarear um retangulo cinza. Falso em jogo, sempre.
	/// </summary>
	public bool SemLuz { get; set; }

	/// <summary>
	/// O RASTRO APARECE INTEIRO MESMO PARADO -- so a previa da mesa. Uma bola na vitrine nao anda, e sem
	/// isto ela seria mostrada sem a cauda que tem em voo: o jogador escolheria a arte por um desenho
	/// que nao e o que vai sair da mao dele.
	/// </summary>
	public bool SempreVoando { get; set; }

	/// <summary>Este node ja tem com que se desenhar (o `Vestir` rodou e os shaders carregaram)?</summary>
	public bool Vestido => Material is ShaderMaterial;

	// =====================================================================
	// A CONTRAPROVA DAS BANCADAS
	// =====================================================================
	/// <summary>
	/// DEFEITOS QUE AS BANCADAS LIGAM pra provar que as reguas delas enxergam -- uma regua que nunca
	/// reprovou nao mede nada. Sempre `Nenhum` em jogo.
	/// </summary>
	public enum DefeitoDoFeixe
	{
		Nenhum,

		/// <summary>O raio sai em PEDACOS com buraco entre eles: e o "picotado" que o dono viu.</summary>
		Picotado,

		/// <summary>
		/// A MAO do raio cai no eixo cardeal mais proximo da ponta, como a folha que nao girava: fora dos
		/// quatro eixos o feixe sai torto -- e em CURVA, porque a saida continua apontando pro rumo de verdade.
		/// </summary>
		SemGirar,

		/// <summary>
		/// A CABECA E PINTADA ADIANTE DA PONTA QUE O NODE ANUNCIA (<see cref="PontaDesenhada"/>), por
		/// <see cref="PixelsDaPontaAdiante"/> px: o shader recebe um comprimento maior que o da fita. Nenhuma
		/// leitura de CAMPO ve isto -- a ponta anunciada nao sai do lugar, e duas pontas numa disputa continuam
		/// "no mesmo ponto" com uma cabeca desenhada dentro da outra. Quem tem que reprovar e a foto
		/// (`--diagembateki`, as pontas no pixel).
		/// </summary>
		PontaAdiante,
	}

	public static DefeitoDoFeixe DefeitoDeTeste = DefeitoDoFeixe.Nenhum;

	/// <summary>
	/// Quanto a cabeca passa da ponta anunciada no <see cref="DefeitoDoFeixe.PontaAdiante"/>, em px. Cabe na
	/// fita de qualquer raio: ela sobra `PintorDeKi.FolgaNaPonta` alem da ponta, que nunca e menos de 9 px
	/// (o halo tem 7 de piso, e ha 2 de sobra).
	/// </summary>
	public const float PixelsDaPontaAdiante = 8f;

	/// <summary>
	/// BANCADA: o instante que TODO tiro escreve no shader, em vez da propria idade (nulo = a idade, que e
	/// o jogo). O desenho se mexe sozinho -- o ruido corre, a boca pisca, as descargas trocam de lugar --, e
	/// uma bancada que compara duas fotos precisa que o tempo nao seja uma das diferencas. Par do
	/// `PintorDeKi.SementeDeTeste`.
	/// </summary>
	public static double? TempoDeTeste;

	// =====================================================================
	// O QUE A ESTRELA DO EMBATE PRECISA SABER DESTE RAIO (ver `World.TickDosChoquesDeKi`)
	// =====================================================================
	/// <summary>
	/// ONDE A PONTA DESTE RAIO ESTA DESENHADA, em coordenada de mundo, ja com a subida do voo. E a
	/// mesma conta que o `_Draw` faz, e nao uma segunda: dois feixes numa disputa tem as pontas no
	/// MESMO ponto, e e ai que a estrela do choque e posta.
	/// </summary>
	public Vector2 PontaDesenhada => Position + _rumo * _medidas.Frente + SubidaNaTela;

	/// <summary>Pra onde ele aponta, normalizado -- a estrela do choque e uma lente atravessada neste eixo.</summary>
	public Vector2 Rumo => _rumo;

	/// <summary>As medidas com que este raio esta sendo desenhado: a estrela do choque sai do tamanho da cabeca dele.</summary>
	public PintorDeKi.MedidasDoFeixe Medidas => _medidas;

	/// <summary>
	/// O RAIO DO ESTOURO que este tiro merece ao acertar, em px: um pouco maior que a parte dele que bate (a
	/// cabeca do raio, a bola inteira). E o que faz uma Genkidama acabar numa explosao e um tiro de dedo num
	/// estalo -- ver <see cref="EstouroDeKi"/>.
	/// </summary>
	public float RaioDoEstouro => (Tipo == TipoDeProjetil.Beam ? _medidas.Cabeca * 1.3f : _raioDaBola * 1.5f) + 4f;

	/// <summary>A cor do MANTO deste tiro -- o tom do meio, ja com a tinta do ki. E a cor do estouro dele.</summary>
	public Color CorDoManto
	{
		get
		{
			Vector3 m = (Tipo == TipoDeProjetil.Beam ? _estiloDoFeixe.Tons : _estiloDaBola.Tons).Com(Cor).Manto;
			return new Color(m.X, m.Y, m.Z);
		}
	}

	/// <summary>
	/// A COR DO LADO DESTE TIRO NA ESTRELA DO EMBATE: entre a borda e o manto, mais pra borda. O manto
	/// sozinho e claro demais -- com ele a estrela saia quase toda branca, e sao as PONTAS coloridas que
	/// dizem de quem e cada metade (a de cima rosa, a de baixo azul, nas imagens do dono).
	/// </summary>
	public Color CorNoChoque
	{
		get
		{
			(Vector3 b, Vector3 m, _) = (Tipo == TipoDeProjetil.Beam ? _estiloDoFeixe.Tons : _estiloDaBola.Tons).Com(Cor);
			Vector3 c = b.Lerp(m, 0.4f);
			return new Color(c.X, c.Y, c.Z);
		}
	}

	/// <summary>
	/// TROCA A TINTA COM O NODE VIVO -- a previa da mesa acompanhando o seletor de cor. E a MESMA escrita
	/// do <see cref="Vestir"/> (as cores do shader: tres na bola, quatro no raio), so que sem refazer o
	/// material; a luz nao entra porque a previa nasce <see cref="SemLuz"/>.
	/// </summary>
	public void Tingir(Color cor)
	{
		Cor = cor;
		if (Material is ShaderMaterial m)
		{
			if (Tipo == TipoDeProjetil.Beam) PintorDeKi.TingirFeixe(m, _estiloDoFeixe.Tons, cor);
			else PintorDeKi.Tingir(m, _estiloDaBola.Tons, cor);
		}
		QueueRedraw();
	}

	/// <summary>
	/// VESTE A ARTE: escolhe o estilo, mede o tiro e monta o material. Chamado UMA vez, no nascimento
	/// (`World.AoNascerTiro`) -- a arte de um tiro nao muda depois que ele sai da mao.
	///
	/// A `Cor` e o `Tipo` tem que estar escritos ANTES: e deles que saem as tres cores e a escolha entre
	/// o shader do raio e o da bola.
	/// </summary>
	public void Vestir(ArteDeKi arte, float escala)
	{
		Arte = arte;
		Escala = escala > 0 ? escala : 1f;
		_escalaDeNascenca = Escala;

		if (Tipo == TipoDeProjetil.Beam)
		{
			_estiloDoFeixe = EstiloDoFeixe(arte);
			_medidas = PintorDeKi.Medir(arte, Escala);
			Material = PintorDeKi.MaterialDeFeixe(arte, Escala, Cor);
		}
		else
		{
			_estiloDaBola = EstiloDaBola(arte);
			_raioDaBola = _estiloDaBola.Raio * Escala;
			Material = PintorDeKi.MaterialDeBola(_estiloDaBola, _raioDaBola, Cor);
		}
	}

	/// <summary>
	/// O RAIO MUDA DE TAMANHO VIVO. Refaz as MEDIDAS -- que o desenho, a ancora de quem ele leva, a dobra e o
	/// estouro leem a cada quadro -- e escreve no material o que depende delas. O material, a semente do ruido e
	/// as cores sao os mesmos: o raio cresce, nao troca de desenho (`PintorDeKi.EscalarFeixe`).
	///
	/// A LUZ DO CHAO FICA A DE NASCENCA: ela e montada uma vez (`LuzDeKi.Nova`), ja com um alcance que passa do
	/// raio mesmo no teto do crescimento, e de dia nem existe.
	/// </summary>
	private void Reescalar(float nova)
	{
		Escala = nova;
		_medidas = PintorDeKi.Medir(Arte, Escala);
		if (Material is ShaderMaterial m) PintorDeKi.EscalarFeixe(m, _estiloDoFeixe, _medidas);
	}

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
		if (rastro.LengthSquared() > 1f) { _rumo = rastro.Normalized(); _temRumo = true; }
		else if (!_primeiro && (cabeca - _cabecaAlvo).LengthSquared() > 1e-4f)
		{
			_rumo = (cabeca - _cabecaAlvo).Normalized();
			_temRumo = true;
		}

		// A VELOCIDADE SAI DO MESMO PASSO, e so a bola a usa (o rastro dela encolhe quando ela para).
		if (!_primeiro)
		{
			double dt = _idade - _idadeDoAlvo;
			if (dt > 0.001) _velocidade = (cabeca - _cabecaAlvo).Length() / (float)dt;
		}
		_idadeDoAlvo = _idade;

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
	/// O SERVIDOR CRAVOU: esta ponta esta AQUI agora, sem interpolar -- o corte de um raio (ver
	/// `World.AoCortarTiro`). Nulo = essa ponta nao muda.
	/// </summary>
	public void Cravar(Vector2? cabeca, Vector2? cauda)
	{
		if (cabeca is { } c) { _cabeca = _cabecaAlvo = c; Position = c; }
		if (cauda is { } q) _cauda = _caudaAlvo = q;
		Vector2 rastro = _cabeca - _cauda;
		if (rastro.LengthSquared() > 1f) { _rumo = rastro.Normalized(); _temRumo = true; }
		_primeiro = false;
		_luzDoTronco?.Esticar(_cabeca, _cauda, SubidaNaTela);
		QueueRedraw();
	}

	/// <summary>
	/// A LUZ QUE ESTE TIRO LANCA NO MUNDO -- o pedido do dono foi *"beams e ataque de ki deveriam ter
	/// LUZ PROPRIA"*. Quem sabe de luz e a <see cref="LuzDeKi"/>; aqui so se diz DE QUEM ela e.
	///
	/// ============================ AQUI, E NAO NO `_Process` ============================
	/// A luz e FILHA deste node, na posicao local zero -- ou seja, na CABECA, porque `Position` deste
	/// node e a cabeca (ver o `_Process`). Ela anda junto de graca: nenhuma linha por quadro copiando
	/// coordenada, e nenhuma chance de a luz ficar pra tras do desenho que ela acompanha. E ela morre
	/// junto pelo mesmo motivo, sem ninguem lembrar de apaga-la.
	///
	/// E O TRONCO ACENDE JUNTO (dono, 2026-09-07: *"so a cabeca do beam tem brilho proprio, mas
	/// deveria ser o beam todo, a cabeca e o tronco"*) -- com UMA luz a mais, e nao uma por pedaco: a
	/// mesma textura radial ESTICADA da cabeca ate a cauda (`LuzDeKi.Esticar`), reposicionada por
	/// quadro. Trinta luzes num tiro so custariam uma ordem de grandeza; duas custam o dobro de uma.
	///
	/// A LUZ E DO CHAO, E NAO DO TIRO. O desenho e `unshaded` (ver o `FeixeDeKi.gdshader`): o raio tem
	/// brilho proprio e nao escurece de noite. O que estas duas luzes fazem e clarear o CENARIO em volta
	/// dele -- sem elas um feixe cruzaria a noite aceso sem iluminar nada, como um adesivo.
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
		// UM `ZIndex` ACIMA DE ZERO e a traducao que o resto da familia de ki ja usa -- `CargaDeRaioVisual`
		// (`plane = 7`) e `NaveDesenhada` (`plane = 6`). No Godot o `ZIndex` decide ANTES do Y-sort,
		// e o Y-sort continua valendo entre iguais: o cenario (ZIndex 0) segue ordenando com os
		// corpos como sempre, e quem esta atras do atirador continua vendo o feixe passar por cima
		// do chao. O unico que subiu foi o tiro.
		//
		// O numero e o `PintorDeKi.CamadaDoTiro`, e la esta por que ele e 2 e nao 1 (a poeira).
		// ================================================================================================
		ZIndex = PintorDeKi.CamadaDoTiro;

		// QUEM NAO FOI VESTIDO SE VESTE AQUI, com a arte que tiver (nenhuma = o estilo neutro). Um tiro
		// posto na arvore sem `Vestir` nao pode ser invisivel: o jogador nao distinguiria isso de "a
		// tecnica nao funciona". Era o papel da primitiva de emergencia do desenho antigo.
		if (!Vestido) Vestir(Arte, Escala);

		if (!SemLuz)
		{
			float tamanho = EscalaDaLuz();
			LuzDeKi.Pendurar(this, Cor, tamanho);
			if (Tipo == TipoDeProjetil.Beam) _luzDoTronco = LuzDeKi.PendurarNoTronco(this, Cor, tamanho);
		}

		// A LUZ SOBE COM O DESENHO. Ela e filha na posicao local zero (ver abaixo), que e a cabeca no
		// CHAO -- e o clarao de um raio disparado la em cima ficaria aceso no piso. A altura nao muda
		// depois do nascimento, entao isto se escreve uma vez e nao por quadro.
		if (Altitude > 0 && GetNodeOrNull<Node2D>(LuzDeKi.NomeDoNode) is { } luz)
			luz.Position = SubidaNaTela;
	}

	/// <summary>
	/// O TAMANHO DA LUZ deste tiro. Pro raio e a escala dele, como sempre foi. Pra bola entra tambem o
	/// RAIO do estilo: a escala de uma Genkidama comeca em 1 como a de um tiro comum, mas ela tem 96 px
	/// de raio contra 4,5 -- com a mesma luz a maior bola do jogo clarearia o chao como uma bolinha.
	/// </summary>
	private float EscalaDaLuz() =>
		Tipo == TipoDeProjetil.Beam ? Escala : Escala * Mathf.Max(1f, _estiloDaBola.Raio / Projetil.RaioDeImpacto);

	public override void _Process(double delta)
	{
		_idade += delta;

		// O RAIO QUE ENGROSSOU (ou murchou de volta) NO SERVIDOR: o desenho anda ate o tamanho de la. Ver
		// `EscalaDoServidor`. Antes de tudo, porque a ancora e o desenho deste quadro ja leem as medidas novas.
		if (Tipo == TipoDeProjetil.Beam)
		{
			float alvo = EscalaDoServidor > 0f ? EscalaDoServidor : _escalaDeNascenca;
			if (!Mathf.IsEqualApprox(Escala, alvo))
				Reescalar(Mathf.MoveToward(Escala, alvo, PassoDaEscala * _escalaDeNascenca * (float)delta));
		}

		float t = Mathf.Min(1f, (float)delta * Suavizacao);
		// O ULTIMO TRECHO E RETO E TEM FIM -- ver `VoarAteOFim`. Fora dele, o `Lerp` de sempre.
		_cabeca = _fim is { } fim ? _cabeca.MoveToward(fim, _passoDoFim * (float)delta) : _cabeca.Lerp(_cabecaAlvo, t);
		_cauda = _cauda.Lerp(_caudaAlvo, t);

		// A POSICAO DO NODE E A CABECA, e o desenho e em coordenadas locais: assim o Y-sort do
		// `Atores` ordena o tiro pelo ponto que de fato importa (onde ele vai acertar), e nao pelo
		// meio de um rastro que pode ter trinta tiles.
		// ============================ A CABECA QUE LEVA ALGUEM FICA NA FRENTE DELE, NO DESENHO (2026-09-23) ============================
		// O servidor ja a mantem a um contato do corpo (`ArrastarComOFeixe`). Mas o corpo remoto e desenhado na
		// linha do tempo dele (~100 ms no passado, ate 250 com a rede ruim) e este tiro com um `Lerp` de ~45 ms:
		// num arrasto a 320 px/s a cabeca desenhada corria 15 a 65 px a frente -- DENTRO de quem ela leva, que e a
		// queixa de sempre ("a cabeca fica sobre a pessoa"). Com a ancora, a cabeca e posta na frente do corpo
		// DESENHADO, no mesmo quadro, pela mesma conta do servidor (a frente da cabeca + meia largura do corpo).
		// ====================================================================================================================
		if (ArrastaId != 0 && Tipo == TipoDeProjetil.Beam && OndeEstaOCorpo?.Invoke(ArrastaId) is { } corpo)
			_cabeca = corpo - _rumo * (_medidas.Frente + Feixe.MeioCorpo);

		Position = _cabeca;
		_luzDoTronco?.Esticar(_cabeca, _cauda, SubidaNaTela);

		// O TRECHO DESVIADO SE APRESENTA AO PAI, a cada quadro -- ver `ContinuaDe`. Sem rumo ainda (o primeiro
		// pacote: cabeca e cauda no mesmo ponto) nao ha pra onde continuar, e o pai segue se desenhando sozinho.
		if (ContinuaDe != 0 && Tipo == TipoDeProjetil.Beam && _temRumo
			&& OndeEstaOTiro?.Invoke(ContinuaDe) is { Tipo: TipoDeProjetil.Beam, _temRumo: true, Vestido: true } pai
			&& pai != this)
		{
			long quadro = (long)Engine.GetProcessFrames();
			pai._ramo = this;
			pai._ramoNoQuadro = quadro;
			_paiNoQuadro = quadro;
		}

		// OS DOIS ESTADOS ANDAM, NAO PULAM. Chegam do fio como bit (solto) e como velocidade medida a 30 Hz;
		// escritos crus no shader, a boca do raio sumiria num quadro e o rastro da bola piscaria.
		_soltura = Mathf.MoveToward(_soltura, Solto ? 1f : 0f, (float)delta * 8f);
		_veloz = Mathf.Lerp(_veloz, Mathf.Clamp(_velocidade / VelocidadeDoRastroCheio, 0f, 1f), Mathf.Min(1f, (float)delta * 10f));

		QueueRedraw();
	}

	public override void _Draw()
	{
		if (Material is not ShaderMaterial mat) return;

		// A SUBIDA DO VOO ENTRA COMO TRANSFORMA DE DESENHO, e nao somada em cada vertice: sao dois
		// caminhos de desenho (a fita do raio e o quadro da bola) e uma soma a mao em cada um seria a
		// terceira chance de esquecer um deles. Ver `Altitude` sobre por que ela nao entra na `Position`.
		if (Altitude > 0) DrawSetTransform(SubidaNaTela);

		mat.SetShaderParameter("tempo", (float)(TempoDeTeste ?? _idade));
		if (Tipo == TipoDeProjetil.Beam) DesenharRaio(mat);
		else DesenharBola(mat);
	}

	// =====================================================================
	// O RAIO
	// =====================================================================
	/// <summary>
	/// A FITA, DA MAO ATE A PONTA. Em coordenada local, onde a origem e a CABECA (a `Position` do node).
	///
	/// A PONTA nao e a posicao do tiro: e `Frente` adiante dela -- a cabeca tem tamanho, e o servidor
	/// encosta a FRENTE dela nas coisas (ver o cabecalho, e `ArteDeProjetil.FrenteDaCabeca`).
	/// </summary>
	private void DesenharRaio(ShaderMaterial mat)
	{
		if (!_temRumo) return;

		// O RAIO DESVIADO E UMA FITA SO, e quem a pinta e o pai -- ver `ContinuaDe`.
		if (Agora(_paiNoQuadro) && !SemDobraDeTeste) return;
		if (_ramo is { } ramo && Agora(_ramoNoQuadro) && IsInstanceValid(ramo) && !SemDobraDeTeste)
		{
			DesenharDobrado(mat, ramo);
			return;
		}

		Vector2 ponta = _rumo * _medidas.Frente;
		(Vector2 mao, Vector2 saida) = AMao(_cauda - _cabeca);

		float meia = PintorDeKi.MeiaFita(_estiloDoFeixe, _medidas);
		float naMao = PintorDeKi.FolgaNaMao(_medidas);
		float naPonta = PintorDeKi.FolgaNaPonta(_medidas);

		mat.SetShaderParameter("solto", _soltura);

		if (DefeitoDeTeste == DefeitoDoFeixe.SemGirar)
		{
			// CONTRAPROVA: a MAO cai no eixo cardeal mais proximo da ponta, como a folha que nao girava. A
			// `saida` nao e mexida de proposito: com a mao fora da linha dela, o `Curva` logo abaixo dobra o
			// feixe ate a ponta -- e e nesse arco que a bancada confere, no pixel, que a fita curva sai inteira.
			Vector2 eixo = MoveRules.FacingFrom(new Vec2(_rumo.X, _rumo.Y), Facing.South) switch
			{
				Facing.North => Vector2.Up, Facing.East => Vector2.Right, Facing.West => Vector2.Left, _ => Vector2.Down,
			};
			mao = ponta - eixo * (ponta - mao).Length();
		}

		Vector2 corda = ponta - mao;
		float comprimento = corda.Length();
		if (comprimento < 0.5f) return;

		if (DefeitoDeTeste == DefeitoDoFeixe.Picotado)
		{
			// CONTRAPROVA: a mesma fita, cortada em pedacos de 12 px com 12 de buraco.
			Vector2 eixo = corda / comprimento;
			for (float de = 0f; de < comprimento; de += 24f)
				PintorDeKi.FitaReta(this, mao + eixo * de, mao + eixo * Mathf.Min(de + 12f, comprimento), meia, 0f, 0f);
			mat.SetShaderParameter("comprimento", 12f);
			return;
		}

		if (Curva(mao, saida, ponta) is { } degraus)
			comprimento = PintorDeKi.FitaCurva(this, degraus, DegrausDaCurva + 1, meia, naMao, naPonta);
		else
			PintorDeKi.FitaReta(this, mao, ponta, meia, naMao, naPonta);

		// CONTRAPROVA: a fita e a de sempre, e o shader pinta a cabeca adiante da ponta dela.
		if (DefeitoDeTeste == DefeitoDoFeixe.PontaAdiante) comprimento += PixelsDaPontaAdiante;

		mat.SetShaderParameter("comprimento", comprimento);
	}

	/// <summary>
	/// DE ONDE O RAIO SAI, e apontando pra onde -- em coordenada local. `cauda` e o fim do rastro que o
	/// servidor manda, tambem local.
	///
	/// ============================ ALIMENTADO, ELE SAI DA MAO DO DONO ============================
	/// A cauda de um raio alimentado e a BOCA, um tile a frente do centro do dono (`BocaDeCano.De`). O
	/// desenho comeca meio caminho antes: na frente do corpo, onde estao as maos. Havendo o corpo do
	/// dono na tela, a mao e literalmente o ponto medio entre ele e a boca -- e a direcao de saida e a do
	/// corpo pra boca, que e pra onde ele esta apontando. E essa direcao que deixa o raio FAZER CURVA
	/// (ver <see cref="Curva"/>) quando a ponta nao esta na frente das maos.
	///
	/// Sem o corpo (a previa da mesa, o dono fora de vista, uma boca que nao fica a um tile dele), a mao
	/// e a mesma meia celula pra tras da cauda, pelo eixo do proprio raio.
	///
	/// ============================ SOLTO, NAO HA MAO ============================
	/// O rabo acaba `Frente` atras da cauda. E a medida que o servidor ja reserva: a parte de la de um
	/// corte nasce com a cauda a `MeioCorpo + AlcanceDaCabeca` de quem cortou (`GameServer.Feixe`),
	/// justamente pra o desenho de tras nao cobrir o cortador.
	/// </summary>
	private (Vector2 Mao, Vector2 Saida) AMao(Vector2 cauda)
	{
		if (Solto) return (cauda - _rumo * _medidas.Frente, _rumo);

		if (Dono != 0 && OndeEstaOCorpo?.Invoke(Dono) is { } corpo)
		{
			Vector2 daBoca = _cauda - corpo;
			float d = daBoca.Length();
			// a boca fica a um tile do corpo (32 px nos eixos, 45 na diagonal). Fora disso este corpo nao
			// esta segurando esta cauda -- foi levado, foi teleportado -- e a mao dele nao e a do raio.
			if (d is > MeiaCelula and < ZoneCollision.TileSize * 2f)
				return ((corpo + _cauda) * 0.5f - _cabeca, daBoca / d);
		}

		return (cauda - _rumo * (MeiaCelula * (Mathf.Abs(_rumo.X) + Mathf.Abs(_rumo.Y))), _rumo);
	}

	/// <summary>Em quantos degraus a curva e cortada. Com 20, o maior desvio entre degraus fica abaixo de meio pixel.</summary>
	private const int DegrausDaCurva = 20;

	/// <summary>
	/// O CAMINHO CURVO, quando o raio precisa de um -- ou nulo, que e quase sempre.
	///
	/// ============================ QUANDO UM RAIO FAZ CURVA ============================
	/// O servidor so atira em linha reta, e a linha passa pelas maos de quem atira: num tiro comum a
	/// mao, a boca e a ponta estao alinhadas, e isto devolve nulo. A curva aparece nos instantes em que
	/// a PONTA sai da frente das maos sem o dono se virar -- a cabeca presa no corpo que ela arrasta (a
	/// ancora do `_Process`), um pacote que move uma ponta antes da outra. O desenho antigo mostrava
	/// isso como um cotovelo na mao; aqui o feixe SAI pra onde as maos apontam e se dobra ate a ponta,
	/// que e o que o dono chamou de *"poder ate dar curva"*.
	///
	/// A curva e uma Bezier de segundo grau com o ponto de controle a meia corda na frente das maos:
	/// sai tangente a direcao delas e chega na ponta sem laco, com raio de curvatura sempre muito maior
	/// que a largura do feixe (a fita nao se dobra sobre si mesma).
	/// ================================================================================
	/// </summary>
	private Vector2[]? Curva(Vector2 mao, Vector2 saida, Vector2 ponta)
	{
		if (Solto) return null;

		Vector2 corda = ponta - mao;
		float c = corda.Length();
		// menos de 2 graus de desvio e reta (o seno de 2 graus); curta demais nao ha onde dobrar; e uma
		// ponta ATRAS das maos nao e curva, e um pacote fora de ordem -- desenha reto e passa.
		if (c < 24f || saida.Dot(corda) <= 0f || Mathf.Abs(saida.Cross(corda)) < c * 0.035f) return null;

		_curva ??= new Vector2[DegrausDaCurva + 1];
		Vector2 controle = mao + saida * (c * 0.5f);
		for (int i = 0; i <= DegrausDaCurva; i++)
		{
			float t = i / (float)DegrausDaCurva, u = 1f - t;
			_curva[i] = mao * (u * u) + controle * (2f * u * t) + ponta * (t * t);
		}
		return _curva;
	}

	// =====================================================================
	// O RAIO DESVIADO -- uma fita, com a curva na dobra (dono, 2026-10-08)
	// =====================================================================
	/// <summary>
	/// BANCADA: desliga a emenda -- o raio desviado volta a ser desenhado como DOIS (o pai ate a cabeca
	/// plantada, o trecho desviado com ponta de tras propria), que e o desenho de antes de 2026-10-08. Sempre
	/// falso em jogo.
	/// </summary>
	public static bool SemDobraDeTeste;

	/// <summary>Em quantos degraus o arco da dobra e cortado. Num quarto de volta de 20 px de raio da 2,6 px por degrau.</summary>
	private const int PassosDaDobra = 12;

	/// <summary>O caminho da fita dobrada: a mao, o arco, a ponta. Alocado na primeira vez -- quase nenhum raio e desviado.</summary>
	private Vector2[]? _dobrado;

	/// <summary>
	/// O RAIO DESVIADO, INTEIRO, NUMA FITA SO: da mao de quem atirou ate a dobra, a curva, e dali ate a ponta
	/// do trecho desviado. Em coordenada local DESTE node (o pai), cuja origem e a cabeca plantada.
	///
	/// PRO SHADER NADA MUDOU: o `UV.x` e o comprimento percorrido (`PintorDeKi.FitaCurva`), entao ele pinta o
	/// mesmo feixe de sempre -- a boca na mao, a cabeca na ponta do trecho desviado, e as fitas que giram em
	/// volta do tronco seguindo a curva sem emenda.
	/// </summary>
	private void DesenharDobrado(ShaderMaterial mat, ProjetilDesenhado ramo)
	{
		(Vector2 mao, _) = AMao(_cauda - _cabeca);
		Vector2 dobra = ramo._cauda - _cabeca;
		Vector2 ponta = ramo._cabeca + ramo._rumo * ramo._medidas.Frente - _cabeca;

		mat.SetShaderParameter("solto", _soltura);

		int n = CaminhoDobrado(mao, dobra, ponta);
		float comprimento = n < 2 ? 0f : PintorDeKi.FitaCurva(this, _dobrado!, n, PintorDeKi.MeiaFita(_estiloDoFeixe, _medidas),
															  PintorDeKi.FolgaNaMao(_medidas), PintorDeKi.FolgaNaPonta(_medidas));
		mat.SetShaderParameter("comprimento", comprimento);
	}

	/// <summary>
	/// QUANTO DE CADA TRECHO O ARCO DA DOBRA COME, em pixel: a distancia da dobra ate onde a curva comeca
	/// (e, do outro lado, ate onde acaba).
	///
	/// A CURVA TEM QUE SER MAIS ABERTA QUE O RAIO E GORDO. A fita e desenhada com a normal de cada degrau, e
	/// do lado de DENTRO de uma curva ela se dobra sobre si mesma a partir de um raio de curvatura da largura
	/// dela. O que precisa caber inteiro ali e o que o shader pinta no meio do tronco -- o macico, as fitas e o
	/// halo, que ficam dentro de `Alcance + Halo` do eixo. (A fita e mais larga que isso, mas o resto dela so
	/// tem tinta nas duas pontas: os leques.) Num Kamehameha sao uns 20 px; num Final Flash, 200.
	/// </summary>
	private float TangenteDaDobra() => Mathf.Max(_medidas.Frente, _medidas.Alcance + _medidas.Halo);

	/// <summary>
	/// MONTA O CAMINHO da fita dobrada em <see cref="_dobrado"/> e devolve quantos pontos ele tem: a mao, um
	/// ARCO DE CIRCULO que sai tangente ao trecho que chega e entra tangente no que sai, e a ponta.
	///
	/// O arco encolhe pra caber: nunca come mais que 45% do trecho que chega (sobra raio reto saindo da mao)
	/// nem mais que o trecho que sai inteiro -- e por isso o ramo recem-nascido, com poucos pixels, comeca num
	/// canto vivo e a curva abre conforme ele cresce.
	/// </summary>
	private int CaminhoDobrado(Vector2 mao, Vector2 dobra, Vector2 ponta)
	{
		_dobrado ??= new Vector2[PassosDaDobra + 3];

		Vector2 chega = dobra - mao, sai = ponta - dobra;
		float c = chega.Length(), s = sai.Length();
		// sem um dos dois trechos nao ha o que dobrar: a fita vai reta da mao a ponta
		if (c < 1f || s < 1f)
		{
			_dobrado[0] = mao;
			_dobrado[1] = ponta;
			return (ponta - mao).LengthSquared() > 0.25f ? 2 : 0;
		}

		Vector2 e1 = chega / c, e2 = sai / s;
		float cos = Mathf.Clamp(e1.Dot(e2), -1f, 1f);
		float tg = Mathf.Tan(Mathf.Acos(cos) * 0.5f);            // a tangente de MEIA virada
		float t = Mathf.Min(TangenteDaDobra(), Mathf.Min(c * 0.45f, s));

		// quase reto (menos de ~3 graus), quase meia-volta, ou sem espaco: nao ha arco, so o canto
		if (tg < 0.03f || tg > 8f || t < 1f)
		{
			_dobrado[0] = mao;
			_dobrado[1] = dobra;
			_dobrado[2] = ponta;
			return 3;
		}

		float raio = t / tg;
		Vector2 a = dobra - e1 * t, b = dobra + e2 * t;
		// o centro fica do lado de DENTRO da virada: de `a`, perpendicular ao trecho que chega, pro lado do que sai
		Vector2 centro = a + (e2 - e1 * cos).Normalized() * raio;
		float de = (a - centro).Angle();
		float varre = Mathf.Wrap((b - centro).Angle() - de, -Mathf.Pi, Mathf.Pi);

		int k = 0;
		_dobrado[k++] = mao;
		for (int i = 0; i <= PassosDaDobra; i++)
		{
			float ang = de + varre * i / PassosDaDobra;
			_dobrado[k++] = centro + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * raio;
		}
		// com o trecho que sai inteiro dentro do arco, o fim do arco JA e a ponta (um ponto repetido deixaria
		// um degrau sem direcao)
		if (s - t > 0.5f) _dobrado[k++] = ponta;
		return k;
	}

	// =====================================================================
	// A BOLA
	// =====================================================================
	private void DesenharBola(ShaderMaterial mat)
	{
		float rastro = _estiloDaBola.Rastro * _raioDaBola * (SempreVoando ? 1f : _veloz);
		mat.SetShaderParameter("rastro", rastro);

		Vector2 eixo = _estiloDaBola.SemGiro || !_temRumo ? Vector2.Right : _rumo;
		PintorDeKi.Quadro(this, Vector2.Zero, eixo, PintorDeKi.MeiaDaBola(_estiloDaBola, _raioDaBola), rastro);
	}
}
