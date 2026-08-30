namespace Jandirus.Core.World;

/// <summary>
/// ============================ O CORPO COMO OBSTACULO -- **NINGUEM ATRAVESSA NINGUEM** ============================
/// *"faca com q personagens N CONSIGAM PASSAR DENTRO DO OUTRO andando ou por KNOCK BACK ou por ser
/// JOGADO pelo grab"* -- o pedido do dono.
///
/// ============================ E ISTO **NAO** E UMA SEGUNDA NOCAO DE "DA PRA PASSAR AQUI" ============================
/// A pergunta continua sendo UMA, e ela mora onde sempre morou: <see cref="MoveRules.Advance"/>. O que
/// muda e que agora ela consulta DOIS planos em vez de um -- o estatico (parede pelo `.col`, agua pelo
/// `.agua`) e o dinamico (os corpos, por <see cref="GradeDeCorpos"/>) -- e **com o mesmo
/// <see cref="ModoDeTravessia"/>**, exatamente como a agua ja e consultada. Ninguem fora do `MoveRules`
/// pergunta "tem corpo aqui?" pra decidir passo.
///
/// A prova de que e o mesmo funil: a CAIXA e a mesma. Corpo contra corpo usa
/// <see cref="MoveRules.BodyHalfW"/> / <see cref="MoveRules.BodyHalfH"/> -- a caixa dos PES, a mesma
/// com que o jogo ja responde "cabe um corpo nesta celula?". Uma caixa propria (o raio de 32 px que o
/// arremesso usava a mao, por exemplo) criaria justamente o segundo dono da pergunta: existiria um
/// ponto onde a parede diz que cabe e a gente diz que nao, e o jogador nao teria como saber qual das
/// duas o parou.
/// ================================================================================================================
///
/// ============================ A REGRA E LITERAL DO DM, E SAO CINCO LINHAS ============================
/// <code>
/// mob/Cross(atom/movable/O)                 // CombatMovement.dm:52-57
///     if(ismob(O))
///         var/mob/M = O
///         if(M.flying)
///             return TRUE                   // quem VOA atravessa
///     ..()                                  // o resto esbarra (mob e denso por padrao)
/// </code>
/// Ou seja: **corpo e denso pra corpo, e a unica excecao e quem esta voando** -- e quem decide e o
/// modo de QUEM ENTRA, nao de quem esta parado. E a mesma forma da agua (`testWaters()`).
///
/// **A EXCECAO DO VOO GANHOU UMA EXCECAO**, e ela e a unica coisa deste arquivo que nao esta no DM:
/// o corpo OCUPADO para tambem quem voa. O motivo esta inteiro no <see cref="Bloqueia"/> -- em
/// resumo: la voar e um booleano no mesmo `z`, aqui e o jeito normal de se mover numa luta, e sem
/// esta segunda excecao o pedido do dono (*"n de pra empurar... enquando eles batem"*) valia em todo
/// lugar menos dentro da briga. Por isso <see cref="Bloqueia"/> tem DOIS argumentos: o modo de quem
/// entra e a <see cref="Ocupacao"/> de quem esta la.
///
/// **O DM NAO TEM EXCECAO PRA NOCAUTE NEM PRA MORTE.** `density` nunca e desligado por KO nem por
/// morrer -- as unicas coisas que desligam sao o `effect/undense` (uma skill de espada,
/// `Sword Skills.dm:21`) e obj/turf, que nao sao corpo. Ver <see cref="Bloqueia"/> pra a lista completa
/// de quem NAO bloqueia neste port, e por que cada um.
/// ====================================================================================================
/// </summary>
public static class ClasseDeCorpo
{
	/// <summary>
	/// ============================ ESTE MODO DE TRAVESSIA ESBARRA EM CORPO? ============================
	/// Literal do `mob/Cross` acima: **so quem voa passa**. Os outros tres esbarram, e cada um por uma
	/// razao que o proprio pedido do dono nomeia:
	///
	///   * <see cref="ModoDeTravessia.APe"/>       -- *"andando"*. O caso principal.
	///   * <see cref="ModoDeTravessia.Arremessado"/> -- *"por KNOCK BACK ou por ser JOGADO pelo grab"*.
	///     E o `M.KB`, e o DM concorda em DOIS lugares: o `Cross` nao o excepciona, e o proprio efeito
	///     de knockback varre `for(var/mob/M in get_step(target,dir))` pra machucar os dois e parar o
	///     voo (`Movement Effects.dm:77-81`). Ver `GameServer.Empurrao.TickDoEmpurrao`.
	///   * <see cref="ModoDeTravessia.Nadando"/>   -- o `swim` **restaura** `density = 1` (`Swim.dm:16`)
	///     e o `Cross` so abre pra `flying`. Dois nadadores esbarram; e como nao ha agua e chao no mesmo
	///     ponto, um nadador e alguem a pe so se encontram na beira -- onde esbarrar e o certo.
	///
	/// ============================ E QUEM VOA ATRAVESSA O QUE ESTA **LIVRE**, E SO ISSO ============================
	/// Quem voa atravessar parede e agua e coerente com o que o port ja faz: acima de
	/// <see cref="Voo.AlturaQueAtravessa"/> o chamador manda `mapa = null` (o `isflying` do original).
	/// Atravessar GENTE era a mesma linha, e ela virou o buraco por onde o pedido novo do dono escapava:
	///
	///   *"ao estar lutando e andando, vc consgue empurrar o inimigo... faca com q n de pra empurar npcs
	///    ou outros players ao andar contra eles enquando eles batem ou fazem outra coisa."*
	///
	/// **Numa luta de DBZ todo mundo voa.** Com a linha antiga, a regra de "ninguem atravessa ninguem"
	/// valia em todo lugar MENOS no unico lugar em que o dono a estava pedindo -- os dois no ar, um
	/// socando e o outro andando por dentro dele. Nao era uma excecao rara: era a luta inteira.
	///
	/// Entao a resposta agora tem dois termos, e o novo e o do OUTRO corpo:
	///
	///   * o corpo <see cref="Ocupacao.Livre"/> segue a regra literal do `mob/Cross` -- so quem voa passa;
	///   * o corpo OCUPADO (batendo, guardando, carregando, canalizando, agarrando, caido, em cena,
	///     treinando, meditando -- ver <see cref="Ocupacao"/>) **para todo mundo, voando inclusive**.
	///
	/// O ANDAR CONTINUA MANDANDO ANTES DISTO (<see cref="MesmoAndar"/>): quem paira dois tiles acima nao
	/// vira poste invisivel pra ninguem, ocupado ou nao. O que muda e so o encontro no MESMO andar.
	///
	/// ============================ E ISSO E **ASSIMETRICO**, DE PROPOSITO ============================
	/// Quem decide continua sendo o modo de QUEM ENTRA -- e o `mob/Cross` literal --, agora cruzado com o
	/// estado de QUEM ESTA LA. Consequencia dita em voz alta: dois voando no mesmo andar, um ocupado e o
	/// outro livre, e o LIVRE que para. O ocupado, se andar, passa por dentro do livre.
	///
	/// Nao e descuido, e nao prende ninguem: os dois terminariam sobrepostos, e sobreposto **ninguem se
	/// barra** (o `jaSobrepondo` do <see cref="GradeDeCorpos.Quem"/>). E na pratica quase todo estado
	/// desta lista ja finca o corpo pelo outro lado (`PodeMexerOCorpo`), entao o "ocupado que anda" e
	/// so o que soca, guarda ou agarra -- que sao justamente os tres que o dono NAO pediu pra parar.
	/// ==========================================================================================
	/// </summary>
	/// <param name="modo">Como QUEM ANDA esta atravessando o mundo.</param>
	/// <param name="ocupacao">O que O OUTRO CORPO esta fazendo. Ver <see cref="Ocupacao"/>.</param>
	public static bool Bloqueia(ModoDeTravessia modo, Ocupacao ocupacao)
		=> CorpoOcupado.Ocupado(ocupacao) || modo != ModoDeTravessia.Voando;

	/// <summary>
	/// ============================ ANDARES DIFERENTES NAO SE ESBARRAM ============================
	/// **Esta e a unica regra deste arquivo que NAO vem do DM, e ela vem porque o DM nao tem altura:**
	/// la voar e um booleano no mesmo `z`, e por isso o `Cross` so precisa perguntar `flying`. Aqui a
	/// altitude existe, e ela ja decide quem soca quem (<see cref="Voo.PodeAcertar"/>) e quem ve quem
	/// (<see cref="Voo.Enxerga"/>). Nao decidir tambem quem esbarra em quem faria um corpo pairando a
	/// 1,5 tile virar um poste invisivel pra quem anda embaixo -- exatamente o *"o jogador vai achar
	/// que travou"*, e com o agravante de o sprite ser desenhado deslocado pra cima
	/// (<see cref="Voo.EscalaNaTela"/>): a parede estaria num lugar e o desenho noutro.
	///
	/// **E SIMETRICA DE PROPOSITO**, ao contrario do <see cref="Voo.PodeAcertar"/>. Colisao assimetrica
	/// nao e "vantagem de quem paira": e A empurrando B pra dentro de si mesmo, e os dois terminando
	/// sobrepostos -- que e o estado que o pedido 6 do dono (*"nao pode prender"*) proibe. Soco pode ser
	/// assimetrico porque ele nao move ninguem; obstaculo nao pode.
	///
	/// **O ANDAR e simetrico; o <see cref="Bloqueia"/> nao**, e as duas frases convivem: aqui se
	/// responde *"nos estamos no mesmo pedaco de ceu?"* -- uma pergunta sobre o par, que nao teria
	/// sentido ter duas respostas. La se responde *"este corpo, do jeito que ele esta, me para?"* --
	/// uma pergunta sobre UM lado, que e o `mob/Cross` do DM e sempre foi assimetrica (quem voa passa
	/// por quem esta parado, e nao o contrario). Ver la o que a assimetria custa, e por que ela nao
	/// prende ninguem.
	/// ==========================================================================================
	/// </summary>
	public static bool MesmoAndar(int andarA, int andarB) => andarA == andarB;

	/// <summary>
	/// AS DUAS CAIXAS DOS PES SE TOCAM? Uma sobreposicao de AABB e nada alem disso.
	///
	/// As meias-extensoes sao as do <see cref="MoveRules"/>, e a igualdade e o ponto (ver o cabecalho
	/// desta classe). Duas caixas de meia-largura 8 se cruzam quando os centros estao a menos de 16 px
	/// em X; de meia-altura 5, a menos de 10 px em Y.
	///
	/// ============================ POR QUE A CAIXA E "BAIXINHA", E POR QUE ISSO ESTA CERTO ============================
	/// 16x10 px parece pouco pra um sprite de 32x32, e a tentacao e engorda-la pro corpo "parecer"
	/// solido. Nao: num jogo visto de cima, a caixa dos pes descreve o **chao ocupado**, e chao visto em
	/// perspectiva e achatado. Estar 12 px "acima" de alguem nao e estar em cima dele -- e estar um terco
	/// de tile ATRAS dele, que e um pedaco de chao diferente, onde da pra ficar de pe.
	///
	/// E o custo de engordar seria imediato e pior: a caixa dos pes e a mesma que responde "cabe um corpo
	/// nesta celula?" pra parede, pouso, nascimento e construcao. Corpo mais gordo que parede criaria
	/// vaos por onde a geometria deixa passar e a gente nao.
	/// ==============================================================================================================
	/// </summary>
	public static bool CaixasSeTocam(Vec2 pesA, Vec2 pesB)
		=> MathF.Abs(pesA.X - pesB.X) < 2 * MoveRules.BodyHalfW
		   && MathF.Abs(pesA.Y - pesB.Y) < 2 * MoveRules.BodyHalfH;

	/// <summary>
	/// A ANCORA: o centro do sprite desce ate a caixa dos pes. A MESMA conta do
	/// <see cref="MoveRules.Occupied"/> -- e ela e escrita aqui e la porque a grade guarda pes e o
	/// `MoveRules` recebe centros. Ver a nota de 8 px do <see cref="MoveRules.NaAgua"/>: perguntar pelo
	/// centro numa ponta e pelos pes na outra abre uma faixa onde as duas discordam.
	/// </summary>
	public static Vec2 Pes(Vec2 centro) => new(centro.X, centro.Y + MoveRules.FeetOffsetY);
}

/// <summary>
/// ============================ A GRADE: COMO NAO PAGAR O(n²) ============================
/// A colisao de cenario custa O(1) -- e um bit numa celula. Corpo contra corpo e por PAR, e uma zona
/// povoada tem dezenas de corpos (o `Povoamento` chega a ~150 habitantes num planeta). Perguntar
/// "quem esta aqui?" varrendo a <c>ZoneList</c> custaria O(n) por pergunta, e o passo faz ATE TRES
/// perguntas (passo cheio + os dois deslizes de quina) por corpo por tique -- ou seja **O(n²) por
/// tique**, e o arremesso multiplica isso pelas amostras de meio tile do caminho.
///
/// Aqui a zona vira uma **tabela de dispersao espacial**: cada corpo entra no balde da celula de 32 px
/// em que os pes dele estao, e a consulta olha so os 3x3 baldes em volta do ponto. Como a caixa tem
/// meia-largura 8 (menor que meio tile), **nenhum corpo capaz de tocar o ponto pode estar a mais de um
/// balde de distancia** -- os 3x3 nao sao heuristica, sao a vizinhanca exata.
///
///   montar : O(n) por zona por tique
///   consultar : O(k), k = corpos nos 9 baldes -- 0 ou 1 no caso comum
///
/// ============================ E ELA NAO ALOCA NADA POR TIQUE ============================
/// Nada de `Dictionary` nem de `List` por balde: sao arrays fixos, e o "limpar" e um **selo de
/// geracao** (<see cref="_geracao"/>) em vez de zerar a tabela. Zerar 1024 baldes por zona por tique
/// seria trocar um O(n²) por um O(zonas x baldes) constante e igualmente inutil -- e este projeto ja
/// tem a regra escrita: nada pesado no tique.
///
/// O balde e um HASH, entao duas celulas distantes podem cair no mesmo. Isso nao afeta a resposta: a
/// consulta re-testa a caixa de verdade (<see cref="ClasseDeCorpo.CaixasSeTocam"/>) e o balde so
/// serve pra encurtar a lista de candidatos.
/// ======================================================================================
/// </summary>
public sealed class GradeDeCorpos
{
	/// <summary>O lado do balde. Um TILE, e nao um numero escolhido a esmo: e a mesma grade da colisao.</summary>
	public const float LadoDoBalde = ZoneCollision.TileSize;

	/// <summary>
	/// Quantos baldes. Potencia de dois pra a mascara substituir o modulo, e folgado o bastante pra que
	/// uma zona cheia (~150 corpos) espalhe sem cadeia longa.
	/// </summary>
	private const int Baldes = 1024;
	private const int Mascara = Baldes - 1;

	private readonly int[] _cabeca = new int[Baldes];
	private readonly int[] _selo = new int[Baldes];

	/// <summary>
	/// COMECA EM **1**, e nao em zero -- e isto e um defeito de verdade evitado, nao preciosismo.
	///
	/// `_selo` nasce zerado (todo array nasce). Com `_geracao = 0`, uma grade recem-criada teria TODOS
	/// os 1024 baldes lidos como validos (`_selo[b] == _geracao`) e `_cabeca[b] = 0`, ou seja: cada
	/// balde vazio apontaria pra a ENTRADA 0 -- o primeiro corpo inserido --, e ele apareceria como
	/// vizinho em todo canto do mapa. E o defeito nasce e morre no MESMO tique (o proximo `Recomecar`
	/// conserta), que e o pior tipo: intermitente, e so na primeira visita a uma zona.
	/// </summary>
	private int _geracao = 1;

	private int[] _prox = new int[64];
	private int[] _id = new int[64];
	private float[] _x = new float[64];
	private float[] _y = new float[64];
	private int[] _andar = new int[64];

	/// <summary>
	/// O QUE CADA CORPO ESTA FAZENDO. Um byte por corpo por tique, no MESMO array paralelo dos
	/// outros campos -- e nao um `Dictionary` consultado na hora da colisao, que alocaria e faria
	/// duas buscas por candidato no caminho mais quente do jogo.
	///
	/// Ele entra na grade porque e AQUI que a pergunta e feita: quem consulta e o passo de quem
	/// anda, e ele nao tem (nem deve ter) acesso a ficha alheia. Ver <see cref="Ocupacao"/>.
	/// </summary>
	private Ocupacao[] _ocupacao = new Ocupacao[64];
	private int _n;

	/// <summary>Quantos corpos esta grade descreve. So pra bancada e diagnostico.</summary>
	public int Quantos => _n;

	/// <summary>
	/// ESVAZIA -- em O(1). Nao apaga nada: incrementa o selo, e todo balde carimbado com um selo velho
	/// e lido como vazio. Ver o cabecalho.
	/// </summary>
	public void Recomecar()
	{
		_n = 0;
		// O selo estoura em 2^31 tiques (2,2 anos a 30 Hz). Quando estourar, ZERA a tabela de verdade --
		// senao um balde com selo antigo poderia coincidir com o novo e devolver corpos de outra era.
		if (++_geracao != int.MinValue) return;
		_geracao = 1;
		Array.Clear(_selo);
	}

	/// <summary>
	/// PoE UM CORPO NA GRADE. <paramref name="pes"/> e a ancora dos pes
	/// (<see cref="ClasseDeCorpo.Pes"/>), nao o centro do sprite.
	/// </summary>
	/// <param name="ocupacao">
	/// O QUE ESTE CORPO ESTA FAZENDO -- e o que decide se ele para tambem quem voa (ver
	/// <see cref="ClasseDeCorpo.Bloqueia"/>).
	///
	/// **O PADRAO E `Livre`**, e ele existe pra quem monta grade de corpo FORJADO -- as bancadas de
	/// geometria pura, que nao tem ficha nem servidor. Quem tem corpo de verdade (o
	/// `GameServer.MontarAsGrades` e o `World.MontarGradeDeCorpos`) passa o valor de verdade, e nos
	/// dois casos ele sai do MESMO lugar: o servidor calcula, o fio carrega.
	/// </param>
	public void Por(int id, Vec2 pes, int andar, Ocupacao ocupacao = Ocupacao.Livre)
	{
		if (_n == _id.Length) Crescer();

		int b = Balde(Celula(pes.X), Celula(pes.Y));
		// Balde de geracao anterior conta como vazio.
		int cabeca = _selo[b] == _geracao ? _cabeca[b] : -1;

		_id[_n] = id; _x[_n] = pes.X; _y[_n] = pes.Y; _andar[_n] = andar;
		_ocupacao[_n] = ocupacao;
		_prox[_n] = cabeca;
		_cabeca[b] = _n;
		_selo[b] = _geracao;
		_n++;
	}

	/// <summary>
	/// ============================ QUEM ESTA OCUPANDO ESTE PONTO? -- devolve o id, ou 0 ============================
	/// Devolve o ID em vez de um `bool` porque **o arremesso precisa saber em quem bateu**: o dano vai
	/// nos DOIS (`Movement Effects.dm:77-81`), e uma resposta booleana obrigaria quem chama a varrer a
	/// zona de novo pra achar a vitima -- que e justamente o O(n) que esta grade existe pra tirar.
	///
	/// Zero nunca e id de corpo neste servidor, entao ele serve de "ninguem" sem um `int?` (que alocaria
	/// no caminho quente).
	/// ============================================================================================================
	/// </summary>
	/// <param name="pes">A ancora dos pes do corpo que quer ocupar o ponto.</param>
	/// <param name="andar">O andar de quem pergunta -- ver <see cref="ClasseDeCorpo.MesmoAndar"/>.</param>
	/// <param name="ignorarId">Quem pergunta. Um corpo nunca esbarra em si mesmo.</param>
	/// <param name="jaSobrepondo">
	/// ============================ O CORPO QUE EU **JA** ESTAVA ATRAVESSANDO NAO ME PARA ============================
	/// A ancora dos pes de ONDE EU ESTOU. Todo corpo que ja toca este ponto e ignorado nesta consulta --
	/// e e essa uma linha que responde inteiro o pedido 6 do dono, *"dois corpos que se encostem nao
	/// podem ficar travados um no outro"*.
	///
	/// Sobrepor acontece o tempo todo e **nao e sempre erro**: nasce-se no ponto de spawn onde alguem
	/// esta, chega-se por passagem, cai-se nocauteado em cima de quem estava colado, solta-se do colo
	/// (o carregado esta na posicao EXATA de quem carrega -- `LevarNoColo` escreve `d.Pos = a.Pos`), e o
	/// arremesso para dentro do alcance do corpo em que bateu. Se o corpo sobreposto bloqueasse, os dois
	/// ficariam com TODAS as direcoes recusadas: travados, sem nada na tela dizendo por que.
	///
	/// **E POR QUE NAO REUSAR A SAIDA DE EMERGENCIA DO `Advance`** (o *"ja preso dentro de parede: deixa
	/// sair"*, que devolve o passo CHEIO ignorando o mapa): porque ela e para quem nao tem NENHUM passo
	/// legal, e paga por isso atravessando parede por alguns quadros -- o defeito que a memoria da agua
	/// ja documenta. Quem esta dentro de um corpo tem passos legais de sobra; ele so precisa que AQUELE
	/// corpo pare de contar. Ignorar o vizinho e cirurgico: parede e agua continuam valendo, os OUTROS
	/// corpos continuam valendo, e assim que os dois se separam eles voltam a se barrar.
	/// ==========================================================================================================
	/// </param>
	/// <param name="modo">
	/// COMO QUEM PERGUNTA ESTA ATRAVESSANDO O MUNDO. Ele entrou nesta funcao (antes era decidido
	/// UMA vez, la fora, no <see cref="Vizinhanca.Barra"/>) porque a resposta deixou de ser do
	/// perguntador sozinho: quem voa atravessa quem esta LIVRE e para em quem esta OCUPADO, e isso
	/// so da pra decidir **por candidato** -- ver <see cref="ClasseDeCorpo.Bloqueia"/>.
	///
	/// O CUSTO ESTA MEDIDO E E ESTE: quem voa passou a andar os 9 baldes em vez de sair na primeira
	/// linha. Continua O(k) com k = corpos nos baldes (0 no caso comum), e o `_n == 0` logo abaixo
	/// continua cobrando zero na maior parte do mundo.
	/// </param>
	/// <param name="ocupacaoDele">
	/// O QUE O CORPO QUE ME PAROU ESTAVA FAZENDO -- <see cref="Ocupacao.Livre"/> quando ninguem
	/// parou. Existe pra a resposta ter NOME: "o corpo nao andou" nao e diagnostico, e a bancada
	/// (e um dia o log de quem estiver caçando um travamento) precisa poder dizer *"parou porque o
	/// outro estava de guarda"*. Ver <see cref="CorpoOcupado.Nome"/>.
	/// </param>
	public int Quem(Vec2 pes, int andar, int ignorarId, Vec2 jaSobrepondo, ModoDeTravessia modo,
					out Ocupacao ocupacaoDele)
	{
		ocupacaoDele = Ocupacao.Livre;

		// ZONA VAZIA (ou com so quem pergunta) NAO CUSTA NADA. E o caso comum -- na maior parte do
		// mundo nao ha ninguem por perto --, e sem esta linha ele pagaria os 9 baldes por consulta,
		// tres vezes por passo, por corpo. Medido na bancada: e o que faz a grade deixar de ser mais
		// LENTA que a varredura ingenua com n = 2.
		if (_n == 0) return 0;

		int cx = Celula(pes.X), cy = Celula(pes.Y);

		for (int dy = -1; dy <= 1; dy++)
			for (int dx = -1; dx <= 1; dx++)
			{
				int b = Balde(cx + dx, cy + dy);
				if (_selo[b] != _geracao) continue;

				for (int i = _cabeca[b]; i >= 0; i = _prox[i])
				{
					if (_id[i] == ignorarId) continue;
					if (!ClasseDeCorpo.MesmoAndar(andar, _andar[i])) continue;
					// ESTE corpo, do jeito que ele esta, me para? Ver `ClasseDeCorpo.Bloqueia`: o
					// livre segue o `mob/Cross` (so quem voa passa) e o ocupado para todo mundo.
					if (!ClasseDeCorpo.Bloqueia(modo, _ocupacao[i])) continue;

					var dele = new Vec2(_x[i], _y[i]);
					if (!ClasseDeCorpo.CaixasSeTocam(pes, dele)) continue;
					// ...e nao me para se eu JA estava por cima dele. Ver o parametro.
					//
					// **ESTA LINHA VALE PRO OCUPADO TAMBEM, E E DE PROPOSITO.** Ela e a unica coisa
					// que impede dois corpos no mesmo ponto de ficarem travados sem nada na tela
					// dizendo por que -- e o ponto comum acontece o tempo todo (colo solto, pouso de
					// arremesso, respawn, `LevarNoColo`). Se guardar ou socar desligasse o escape,
					// bastaria alguem erguer a guarda em cima de voce pra voce nao andar mais.
					if (ClasseDeCorpo.CaixasSeTocam(jaSobrepondo, dele)) continue;

					ocupacaoDele = _ocupacao[i];
					return _id[i];
				}
			}
		return 0;
	}

	/// <summary>O mesmo, pra quem so quer saber SE parou. Ver a versao com `out`.</summary>
	public int Quem(Vec2 pes, int andar, int ignorarId, Vec2 jaSobrepondo, ModoDeTravessia modo)
		=> Quem(pes, andar, ignorarId, jaSobrepondo, modo, out _);

	private static int Celula(float v) => (int)MathF.Floor(v / LadoDoBalde);

	/// <summary>
	/// Dispersao de duas coordenadas com sinal. Os primos sao os classicos de grade espacial; o que
	/// importa e so que celulas vizinhas caiam em baldes diferentes.
	///
	/// OS PARENTESES EM VOLTA DO `^` NAO SAO ENFEITE: em C# o `&amp;` liga MAIS FORTE que o `^`, entao
	/// `a ^ b &amp; M` seria `a ^ (b &amp; M)` -- um indice fora da tabela (e negativo em metade dos mapas,
	/// porque a coordenada de celula tem sinal).
	/// </summary>
	private static int Balde(int cx, int cy) => ((cx * 73856093) ^ (cy * 19349663)) & Mascara;

	private void Crescer()
	{
		int novo = _id.Length * 2;
		Array.Resize(ref _prox, novo); Array.Resize(ref _id, novo);
		Array.Resize(ref _x, novo); Array.Resize(ref _y, novo);
		Array.Resize(ref _andar, novo); Array.Resize(ref _ocupacao, novo);
	}
}

/// <summary>
/// ============================ QUEM ESTA PERGUNTANDO -- o contexto de UMA consulta ============================
/// Um `struct` de tres campos em vez de tres parametros a mais em cada assinatura do
/// <see cref="MoveRules"/>. Nao e enfeite: `Advance` ja recebe sete argumentos, e `Occupied`,
/// `PathOccupied` e `ValidateStep` teriam de recebe-los tambem. Passa-los soltos garantiria que um dia
/// alguem chamasse um deles com o andar de outro corpo.
///
/// **O PADRAO E VAZIO** (`Grade` nulo) e isso significa *"nao consulte corpos"* -- e o que faz todo
/// chamador antigo continuar valendo sem uma linha de mudanca, e o que deixa o
/// <see cref="MoveRules.ValidateStep"/> explicitamente de fora (ver la o porque).
/// ==========================================================================================================
/// </summary>
public readonly struct Vizinhanca
{
	/// <summary>Os corpos da zona neste tique. Nulo = ninguem consulta corpo nenhum.</summary>
	public readonly GradeDeCorpos? Grade;

	/// <summary>O id de quem anda -- pra ele nao esbarrar em si mesmo.</summary>
	public readonly int Id;

	/// <summary>O andar de quem anda (<see cref="Voo.Andar"/>).</summary>
	public readonly int Andar;

	public Vizinhanca(GradeDeCorpos? grade, int id, int andar)
	{
		Grade = grade; Id = id; Andar = andar;
	}

	/// <summary>
	/// UM CORPO ME PARA INDO DE <paramref name="de"/> PRA <paramref name="para"/>? Devolve o id de quem
	/// para, ou 0.
	///
	/// Os dois pontos sao CENTROS de sprite (e nao pes): a conversao mora num lugar so
	/// (<see cref="ClasseDeCorpo.Pes"/>), do mesmo jeito que o `MoveRules.Occupied` faz a dele.
	///
	/// ============================ O ATALHO DO VOO SAIU DAQUI ============================
	/// Havia aqui um `|| !ClasseDeCorpo.Bloqueia(modo)` que devolvia zero **sem olhar corpo nenhum**
	/// quando quem andava estava voando. Ele era barato e estava errado desde que o corpo OCUPADO
	/// passou a barrar todo mundo: com ele, o voo continuava atravessando quem esta socando -- que e
	/// exatamente o caso do pedido do dono, e o mais comum de todos numa luta.
	///
	/// A pergunta desceu pra dentro do <see cref="GradeDeCorpos.Quem"/>, que a faz POR CANDIDATO. O
	/// atalho barato que sobrou e o certo: `_n == 0` (zona sem ninguem), que continua saindo na
	/// primeira linha e cobre a maior parte do mundo.
	/// ==================================================================================
	/// </summary>
	public int Barra(Vec2 de, Vec2 para, ModoDeTravessia modo)
		=> Barra(de, para, modo, out _);

	/// <summary>O mesmo, e diz O QUE o corpo que parou estava fazendo. Ver <see cref="GradeDeCorpos.Quem"/>.</summary>
	public int Barra(Vec2 de, Vec2 para, ModoDeTravessia modo, out Ocupacao ocupacaoDele)
	{
		ocupacaoDele = Ocupacao.Livre;
		if (Grade == null) return 0;
		return Grade.Quem(ClasseDeCorpo.Pes(para), Andar, Id, ClasseDeCorpo.Pes(de), modo, out ocupacaoDele);
	}
}
