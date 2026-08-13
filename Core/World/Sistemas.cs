namespace Jandirus.Core.World;

/// <summary>
/// A CLASSE DE UMA ESTRELA. E um SIMBOLO, nao um caminho de arquivo.
///
/// O Core nao conhece o Godot (regra 0.1 da especificacao), entao quem traduz isto em `res://` e o
/// cliente -- mesma fronteira de <see cref="Jandirus.Core.Forms.FolhaColada"/> e da
/// `Client.ColadasDeForma.CaminhoDa`. Aqui so se DECIDE qual estrela e; la se descobre onde a arte
/// mora.
///
/// ============================ AS PASTAS MENTEM, E O CODIGO NAO PODE ACREDITAR NELAS ============================
/// A arte veio em `Assets/Sprites/Stars/Giant/` e `.../Main sequence/`, e as pastas estao erradas:
/// `Giant/` guarda `star_blue` e `star_orange`, que sao de sequencia principal, e `Main sequence/`
/// guarda `star_white_giant`, que e gigante. Quem classifica e o NOME DO ARQUIVO, nunca a pasta --
/// e por isso a enumeracao abaixo e a autoridade, e nao a arvore de diretorios.
/// ==========================================================================================================
///
/// A ordem e a fisica de verdade (do mais quente ao mais frio) com as tres gigantes por cima. Sao
/// exatamente as 8 famílias que existem em arte, com 4 variantes numeradas cada -- ou seja, a tabela
/// cobre 100% dos sorteios sem pedir um pixel novo.
/// </summary>
public enum ClasseDeEstrela : byte
{
	/// <summary>`star_red` -- ana vermelha. A mais comum (30%), e a menor.</summary>
	AnaVermelha = 0,
	/// <summary>`star_orange`.</summary>
	Laranja = 1,
	/// <summary>`star_yellow` -- a classe do Sol. E a estrela da Terra, por regra escrita.</summary>
	Amarela = 2,
	/// <summary>`star_white`.</summary>
	Branca = 3,
	/// <summary>`star_blue`.</summary>
	Azul = 4,
	/// <summary>`star_white_giant`.</summary>
	GiganteBranca = 5,
	/// <summary>`star_red_giant` -- no teto do raio letal.</summary>
	GiganteVermelha = 6,
	/// <summary>`star_blue_giant` -- a mais rara (2%), tambem no teto.</summary>
	GiganteAzul = 7,
}

/// <summary>
/// UMA ESTRELA NO ESPACO. Um corpo, com posicao e raio -- e o raio E LETAL, nao decorativo.
///
/// Nao herda de <see cref="PlanetaNoEspaco"/> de proposito: um planeta e um lugar onde se pousa e
/// uma estrela e um lugar onde se morre, e misturar os dois num tipo so faria o pouso ter que
/// perguntar "mas e planeta mesmo?" em todo lugar que ja recebe um planeta hoje.
/// </summary>
public readonly struct Estrela
{
	public ClasseDeEstrela Classe { get; init; }

	/// <summary>0..3 -- as quatro folhas numeradas de cada familia (`star_red01`..`04`).</summary>
	public int Variante { get; init; }

	public Vec2 Pos { get; init; }

	/// <summary>
	/// RAIO LETAL, em pixels de mundo. Nao e o raio da folha de arte, e nao e o raio do brilho:
	/// e a distancia a partir da qual o corpo COMECA A QUEIMAR.
	///
	/// ============================ AGORA ELE ESTA LIGADO -- E NAO MATA NA HORA ============================
	/// A Fase 3 fechou com este campo escrito e sem consumidor nenhum ("DEVE, e nao morre"). Ele agora
	/// e lido pelo <see cref="Sistemas.EstrelaPerto"/>, que o `TickDoSol` do servidor consulta a cada
	/// tique -- e o efeito e DANO POR SEGUNDO (<see cref="CalorDaEstrela"/>), nao morte instantanea.
	///
	/// A troca e do dono e ela e de desenho, nao de implementacao: *"se vc cair la comeca a tomar
	/// MUITO dano"*. Dano por segundo deixa o corpo TENTAR SAIR, e faz o forte durar mais que o
	/// fraco -- as duas coisas que a morte instantanea apagaria de uma vez.
	/// ==================================================================================================
	///
	/// O desenho tem que se ajustar A ESTE numero (o nucleo opaco de cada folha vale entre 0,569 e
	/// 0,698 do meio-lado, medido nas 32 -- ver `Client.ArteDeEstrela`) -- e nao o contrario. Vale
	/// aqui o mesmo aviso que o `PlanetaDesenhado` ja carrega, e agora ele custa vida: se o desenho
	/// nao casar com o raio, o jogador queima "no vazio" ao lado de uma estrela que parecia longe.
	/// </summary>
	public float Raio { get; init; }

	/// <summary>Seed da estrela. Deriva dela tudo que for proprio da estrela (cor, cintilancia).</summary>
	public ulong Seed { get; init; }
}

/// <summary>
/// COORDENADA DE CELULA DE SISTEMA. Duas celulas vizinhas distam <see cref="Sistemas.CelulaPx"/>.
///
/// Irma de <see cref="ChunkId"/> e pelo mesmo motivo: o universo nao tem fim nem centro, entao o
/// endereco de qualquer coisa e um par de inteiros e nao um indice numa lista.
/// </summary>
public readonly record struct SistemaId(int Sx, int Sy)
{
	public static SistemaId De(Vec2 p) => new(
		(int)Math.Floor(p.X / Sistemas.CelulaPx),
		(int)Math.Floor(p.Y / Sistemas.CelulaPx));

	/// <summary>Distancia em celulas (Chebyshev).</summary>
	public int Longe(SistemaId o) => Math.Max(Math.Abs(Sx - o.Sx), Math.Abs(Sy - o.Sy));

	public override string ToString() => $"[{Sx}:{Sy}]";
}

/// <summary>
/// UM SISTEMA SOLAR: uma estrela e de 1 a 10 planetas em orbitas circulares PARADAS.
///
/// E uma `struct` e nao uma classe porque <see cref="Espaco.PorPerto"/> a monta 9 vezes por
/// consulta, e a consulta acontece por tique por jogador -- alocar 9 objetos por tique por jogador
/// pra devolver 40 bytes de numero seria pagar coletor de lixo pelo resto do jogo. Os planetas nao
/// ficam guardados aqui: <see cref="Planeta"/> os calcula sob demanda, porque quase toda consulta
/// quer um punhado deles e nao os dez.
/// </summary>
public readonly struct SistemaSolar
{
	public SistemaId Id { get; init; }
	public Estrela Estrela { get; init; }

	/// <summary>
	/// Este sistema foi ANCORADO num planeta pre-feito -- a estrela nasceu a partir dele.
	/// Ver <see cref="Sistemas.Do"/>.
	/// </summary>
	public bool Ancorado { get; init; }

	/// <summary>Quantas orbitas o sistema tem. 1..10 nos gerados, sempre 4 nos ancorados.</summary>
	public int Orbitas { get; init; }

	/// <summary>Semieixo da orbita mais interna: raio da estrela + <see cref="Sistemas.FolgaDaCoroa"/>.</summary>
	public double A0 { get; init; }

	/// <summary>Distancia entre orbitas vizinhas. Constante dentro do sistema (ver <see cref="Sistemas"/>).</summary>
	public double Passo { get; init; }

	/// <summary>Hash da celula. E a raiz de tudo que este sistema sorteia.</summary>
	public ulong H { get; init; }

	/// <summary>
	/// Indice da orbita ocupada pelo planeta PRE-FEITO, ou -1 se o sistema e gerado. A orbita e
	/// devolvida LITERAL por <see cref="Planeta"/> -- e o que impede a Terra de andar.
	/// </summary>
	public int OrbitaPreFeita { get; init; }

	/// <summary>O pre-feito deste sistema (so vale quando <see cref="Ancorado"/>).</summary>
	public PlanetaNoEspaco PreFeito { get; init; }

	/// <summary>Semieixo maior da orbita k, em pixels.</summary>
	public double Semieixo(int k) => A0 + k * Passo;

	/// <summary>
	/// O ROTULO DA ESTRELA na carta e na tela do sistema.
	///
	/// E PROPRIEDADE E NAO CAMPO de proposito, e a bancada mediu o porque: montar a string dentro do
	/// <see cref="Sistemas.Do"/> custava mais que todo o resto da funcao junto (250 ns contra os 60
	/// que ela leva sem isso), e `Do` roda 9 vezes por consulta de vizinhanca, por tique, por
	/// jogador. Formatar texto de interface no caminho quente do universo e o tipo de custo que nao
	/// aparece em lugar nenhum ate a bancada perguntar.
	/// </summary>
	public string NomeDaEstrela => Ancorado ? $"Sistema de {PreFeito.Nome}" : $"Sistema {Id.Sx}:{Id.Sy}";

	/// <summary>
	/// RAIO DO SISTEMA: alcanca o corpo mais externo. E o que garante que dois sistemas nunca se
	/// interpenetram -- a estrela gerada nasce dentro de uma caixa livre de
	/// `CelulaPx - 2*RaioSistemaTeto`, logo duas estrelas geradas distam pelo menos `2*Teto`.
	/// </summary>
	public double RaioDoSistema { get; init; }

	/// <summary>
	/// ONDE O PILOTO AUTOMATICO LARGA QUEM VIAJOU "PRO SISTEMA" e nao pra um planeta.
	///
	/// Fica na orbita mais interna, no lado OPOSTO ao do planeta 0 -- determinístico e impossivel de
	/// coincidir com um corpo. Fora da coroa por construcao (A0 = raio + folga).
	/// </summary>
	public Vec2 PortoDeEntrada
	{
		get
		{
			(double dx, double dy) = Sistemas.DirecaoDaOrbita(H, 0);
			return new Vec2((float)(Estrela.Pos.X - dx * A0), (float)(Estrela.Pos.Y - dy * A0));
		}
	}

	/// <summary>Esta orbita cai na zona habitavel? Ver <see cref="Sistemas.ZonaHabitavelDe"/>.</summary>
	public bool Habitavel(int k)
	{
		(double dentro, double fora) = Sistemas.ZonaHabitavelDe(Estrela.Raio);
		double a = Semieixo(k);
		return a >= dentro && a <= fora;
	}

	/// <summary>
	/// O PLANETA DA ORBITA k. Funcao pura -- nada e guardado, nada e sorteado em runtime.
	///
	/// NA ORBITA DO PRE-FEITO ELE E DEVOLVIDO LITERAL, tal como <see cref="Espaco.PreFeitos"/> o
	/// escreveu. Isso nao e otimizacao: recalcular `estrela + direcao * semieixo` devolveria a
	/// posicao da Terra com erro de arredondamento de `float`, e a Terra teria ANDADO -- e com ela
	/// o zero do sistema de coordenadas do universo inteiro. Ver `Sistemas.Ancorar`.
	/// </summary>
	public PlanetaNoEspaco Planeta(int k)
	{
		if (k == OrbitaPreFeita) return PreFeito;

		ulong seed = Espaco.Misturar(H ^ Sistemas.SalDoPlaneta, (ulong)(uint)k, 0);
		(double dx, double dy) = Sistemas.DirecaoDaOrbita(H, k);
		double a = Semieixo(k);

		// O DISCO DIZ O TAMANHO DO MUNDO -- a mesma linha que o `Espaco` sempre teve. Um sorteio (a
		// gravidade) e tres consequencias: lado do mapa, raio do disco e custo de geracao. Duas
		// fontes de tamanho divergiriam no dia em que alguem mexesse numa delas.
		double g = MundoProcedural.GravidadeDaSeed(seed);
		double t = (g - MundoProcedural.GravidadeMinima)
				   / (MundoProcedural.GravidadeMaxima - MundoProcedural.GravidadeMinima);

		return new PlanetaNoEspaco
		{
			Nome = $"{Sistemas.NomeDeBioma(seed)}-{Math.Abs(Id.Sx) % 1000}{Math.Abs(Id.Sy) % 1000}{k}",
			Pos = new Vec2((float)(Estrela.Pos.X + dx * a), (float)(Estrela.Pos.Y + dy * a)),
			Raio = 110 + (float)(Math.Clamp(t, 0, 1) * 89),
			Seed = seed,
			Premade = false,
		};
	}

	/// <summary>Todos os planetas, da orbita interna pra externa. Pra carta e pra bancada.</summary>
	public IEnumerable<PlanetaNoEspaco> Planetas()
	{
		for (int k = 0; k < Orbitas; k++) yield return Planeta(k);
	}
}

/// <summary>
/// POR QUE UMA CELULA NAO TEM SISTEMA. Duas causas MUITO diferentes, e separa-las e o que mantem
/// uma guarda viva.
///
/// ============================ SEM ISTO A GUARDA DO ANCORADO MORRERIA CALADA ============================
/// A anulacao por ancorado e rara (nao dispara nenhuma vez na seed deste servidor) e a bancada
/// varre seeds ate faze-la disparar, porque guarda que nunca roda e guarda que nao existe. Quando o
/// <see cref="Sistemas.VaziosPor256"/> entrou, `Do` passou a devolver nulo em 37,5% das celulas --
/// e qualquer teste que contasse "quantos nulos" ficaria verde na primeira tentativa, provando o
/// sorteio e nao a guarda. Sao dois nulos com o mesmo tipo e significados opostos, entao eles tem
/// que ter nomes diferentes.
/// ====================================================================================================
/// </summary>
public enum CelulaVazia : byte
{
	/// <summary>A celula tem sistema.</summary>
	Nao = 0,
	/// <summary>Sorteada como vazia (<see cref="Sistemas.VaziosPor256"/>). E o vazio de projeto.</summary>
	Sorteada = 1,
	/// <summary>O sistema gerado se sobrepunha a um ancorado e foi recusado. E a guarda.</summary>
	AnuladaPorAncorado = 2,
}

/// <summary>
/// OS DOIS BOTOES QUE DESENHAM A GRADE -- e o unico jeito honesto de a bancada medir o ONTEM.
///
/// ============================ POR QUE ISTO E CODIGO DE PRODUCAO E NAO DE TESTE ============================
/// A pergunta que a bancada tem que responder e comparativa: *"a reticula sumiu?"*. Uma bancada que
/// responde isso com LIMIAR CRAVADO ("o CV tem que passar de 0,20") envelhece sozinha -- no dia em que
/// a celula, o passo entre orbitas ou o numero de planetas mudarem, ela fica vermelha (ou verde) por
/// um motivo que nao tem nada a ver com a grade, e ninguem vai saber dizer qual dos dois.
///
/// A alternativa que este projeto ja usa e melhor: o "antes" nao e um numero guardado, e A MESMA
/// FUNCAO com os botoes NEUTROS. E o padrao dos raios de forma (`RoboDeForma`, "com os botoes NEUTROS
/// (o shader de antes)"): a comparacao e sempre entre duas rodadas da MESMA conta, e o que se afirma e
/// a RAZAO entre elas.
///
/// Escrever o universo velho DENTRO da bancada seria um caminho paralelo -- ele testaria a copia, e no
/// dia em que a formula do sorteio mudasse os dois lados da razao mediriam funcoes diferentes. Por isso
/// os dois botoes moram aqui, no caminho de producao, com o valor de hoje como padrao: o jogo chama
/// <see cref="Sistemas.Do(ulong,int,int)"/> e nunca ve esta struct.
///
/// CUSTO -- medido, e a medida tem uma armadilha que quase virou uma conclusao errada. O `Do` custava
/// 86,6 ns por celula na primeira rodada do dia e 105 a 108 ns depois desta struct entrar, o que
/// pareceu regressao de 20%. Nao e: refazendo o A/B na MESMA janela de tempo (as tres linhas do corpo
/// com os campos da struct contra as mesmas tres com as constantes de volta, e nada mais mudando) sai
///
///     com a struct   105,3 / 107,2 / 108,5 ns      media 107,0
///     sem a struct    99,7 / 110,6 / 105,4 ns      media 105,2
///
/// -- 1,8 ns de diferenca contra 10 ns de espalhamento dentro de cada trio. A struct e `readonly` e
/// vai por valor, entao o JIT le tres campos de um estatico e some com ela; o que mudou de verdade
/// entre as duas horas do dia foi a MAQUINA. Numero de bancada so se compara com numero medido do
/// lado, e nunca com numero de outra sessao.
/// ========================================================================================================
/// </summary>
/// <param name="MargemFixa">
/// Quanto a estrela tem que guardar de cada divisa da celula. **Zero (o valor de hoje) quer dizer "o
/// raio do PROPRIO sistema"** -- ver <see cref="Sistemas.Do(ulong,int,int,out CelulaVazia,RegraDaGrade)"/>.
/// Um numero positivo e a regra ANTIGA: a mesma margem pra todo mundo, que era o teto do maior sistema
/// possivel e desenhava as colunas da carta.
/// </param>
/// <param name="Folga">Quanto se exige ALEM da margem, pra que dois discos nunca fiquem tangentes.</param>
/// <param name="VaziosPor256">Quantas celulas em 256 nao tem sistema nenhum. Zero = toda celula tem.</param>
public readonly record struct RegraDaGrade(double MargemFixa, double Folga, int VaziosPor256)
{
	/// <summary>O universo que esta no ar. E o padrao de todas as sobrecargas curtas.</summary>
	public static readonly RegraDaGrade Hoje =
		new(0, Sistemas.FolgaEntreSistemas, Sistemas.VaziosPor256);

	/// <summary>
	/// O UNIVERSO DA RECLAMACAO -- margem = teto pra todos, folga nenhuma, nenhuma celula vazia.
	///
	/// E literalmente o codigo de antes da mudanca: a bancada roda a mesma `Do` com estes tres valores
	/// e tem que reencontrar os numeros que a Fase 0 publicou (CV 0,083, 96,6% de vies de eixo, 29,8%
	/// do lado da celula). E essa reproducao que autoriza usar o resultado como denominador -- se ela
	/// nao batesse, a razao estaria comparando hoje com uma invencao.
	/// </summary>
	public static readonly RegraDaGrade AntesDaReclamacao =
		new(Sistemas.RaioSistemaTeto, 0, 0);

	/// <summary>O mesmo universo com outra taxa de vazio -- e por onde a bancada injeta o defeito
	/// "esburacado demais", que tem que REPROVAR tanto quanto a reticula.</summary>
	public RegraDaGrade ComVazios(int quantos) => this with { VaziosPor256 = quantos };
}

/// <summary>
/// O UNIVERSO EM SISTEMAS SOLARES -- funcao pura de (seed, celula).
///
/// ============================ O QUE MUDOU, E POR QUE ============================
/// Antes: um planeta solto a cada 40 chunks, sorteado por `Espaco.PlanetaDaChunk`. Media medida de
/// 6.740 px entre um mundo e o vizinho, ou seja 42 s de voo -- o espaco NAO parecia vazio, apesar
/// de o comentario do `Espaco` dizer que era a distancia entre os mundos que dava peso a uma viagem
/// de sete dias. Havia um mundo a cada 42 segundos.
///
/// Agora: uma ESTRELA em 62,4% das celulas de 32x32 chunks, com 1 a 10 planetas em orbita.
/// Densidade global cai pra 1 planeta a cada 302 chunks (7,5x mais vazio que os planetas soltos),
/// mas AGRUPADO -- pular de um planeta pro vizinho custa 9 a 14 s, e cruzar de um sistema pro
/// proximo custa ~6,8 min. E exatamente a forma que o dono pediu: o pontinho da carta virou um
/// sistema. Os 37,6% de celulas sem nada sao a segunda rodada do mesmo pedido, e estao logo abaixo.
/// ==============================================================================
///
/// ============================ NEM TODA CELULA TEM SISTEMA, E A GRADE SUMIU ============================
/// Esta secao dizia "toda celula tem sistema" e o dono leu o resultado disso na carta estelar:
/// *"senti q os sistemas estao mt certinhos, deveriam ter posicoes mais RANDOMICAS e ter ESPACOS
/// VAZIOS entre eles"*. Ele estava certo, e eram DUAS coisas medidas:
///   * a estrela usava 29,8% do lado da celula pra se mexer (a margem era o teto do maior sistema
///     possivel, paga por todos) -- 96,6% dos vizinhos mais proximos caiam a menos de 15 graus de um
///     eixo, contra os 33,3% de um campo isotropico. Era literalmente uma reticula;
///   * densidade EXATAMENTE uniforme: 1 estrela por celula em todo lugar, sem excecao. O ponto mais
///     vazio do universo ficava a 54.713 px de uma estrela. Nao havia deserto.
///
/// As duas correcoes vivem em <see cref="Sistemas.Do"/> e sao independentes:
///   * a margem passou a ser o raio do PROPRIO sistema (mais <see cref="Sistemas.FolgaEntreSistemas"/>).
///     A garantia de nao-sobreposicao continua sendo por construcao e sem consultar vizinho, porque a
///     desigualdade real e `margemA + margemB >= rsysA + rsysB` -- e margem = raio a satisfaz exatamente;
///   * <see cref="Sistemas.VaziosPor256"/> celulas em 256 nao tem sistema nenhum. Jitter borra a
///     grade; so a rejeicao abre buraco.
///
/// O `% 40` do universo anterior a este nao dava garantia nenhuma: dois planetas em chunks vizinhas
/// podiam ficar a 868 px.
/// ====================================================================================================
///
/// DETERMINISMO -- a propriedade central:
///   * ZERO estado mutavel. Nao ha `Random`, nao ha cache, nao ha `GetHashCode()`;
///   * todo sorteio sai do <see cref="Espaco.Misturar"/>, o mesmo hash do terreno e do mapa;
///   * a aritmetica e `double` com +, -, * e /. **Nao ha `Math.Cos`/`Math.Sin` em lugar nenhum**
///     (ver <see cref="DirecaoDaOrbita"/>): essas variam de biblioteca pra biblioteca, e uma orbita
///     um ULP diferente no servidor poe o jogador pousando "ao lado" do planeta que ele ve;
///   * ordem de varredura fixa em <see cref="Assinatura"/> -- e ela e a prova.
/// </summary>
public static class Sistemas
{
	// =====================================================================
	// A GRADE
	// =====================================================================
	/// <summary>
	/// Lado da celula de sistema, em chunks.
	///
	/// 32 nao e numero redondo por acaso: e o MESMO `ChunksPorBloco` que a carta estelar
	/// (`Client.MapaEstelar`) ja usa como bloco de cache. Uma celula de sistema e exatamente um
	/// bloco da carta, entao a carta enumera 1 hash onde antes enumerava 1024.
	/// </summary>
	public const int ChunksPorSistema = 32;

	/// <summary>Lado da celula em pixels: 65.536.</summary>
	public const double CelulaPx = ChunksPorSistema * (double)Espaco.ChunkPx;

	/// <summary>
	/// Sal da camada de sistemas. Cada sorteio precisa do seu espaco, senao a classe da estrela
	/// sairia correlacionada com o que a mesma coordenada ja decidiu em outra camada.
	///
	/// Nao pode ser `0x9E3779B9...`: o <see cref="Espaco.Misturar"/> ja faz `seed ^ 0x9E37...` na
	/// primeira linha, e repetir a constante como sal desfaria o XOR e devolveria a seed crua.
	/// </summary>
	private const ulong SalDoSistema = 0xD6E8FEB86659FD93UL;

	/// <summary>Sal da SEED de cada planeta.</summary>
	internal const ulong SalDoPlaneta = 0x51ED270B4E1D8A73UL;

	/// <summary>Sal da DIRECAO de cada orbita (a fase orbital, fixa pra sempre).</summary>
	private const ulong SalDaOrbita = 0xA24BAED4963EE407UL;

	// =====================================================================
	// AS ESTRELAS
	// =====================================================================
	/// <summary>
	/// TETO DURO DO RAIO LETAL DE UMA ESTRELA -- e ele e uma consequencia medida, nao um gosto.
	///
	/// <see cref="Espaco.PlanetaSob"/> varre <see cref="Espaco.PorPerto"/> com
	/// <see cref="Espaco.RaioAtivo"/> = 1, ou seja o 3x3 de chunks em volta de QUEM TOCA. Um corpo so
	/// e detectavel se a chunk do CENTRO dele estiver a no maximo 1 chunk de quem encosta na borda.
	/// A bancada mediu o pior caso (centro colado na divisa, nao amostra aleatoria):
	///
	///     R = 2.047 px -> 1 chunk   OK
	///     R = 2.048 px -> 1 chunk   OK      &lt;-- exatamente ChunkPx
	///     R = 2.049 px -> 2 chunks  QUEBRA
	///
	/// Uma estrela de 2.049 px de raio seria ATRAVESSAVEL SEM MORRER dependendo de onde a divisa da
	/// chunk cai -- bug que aparece numa fracao fina do espaco, ou seja nunca em teste e sempre em
	/// producao. Subir `RaioAtivo` pra 2 custaria 25 hashes em vez de 9 em TODA vizinhanca de TODO
	/// jogador o tempo todo, por causa de 11% dos corpos. O teto sai mais barato.
	/// </summary>
	public const float RaioLetalMaximo = Espaco.ChunkPx;

	/// <summary>
	/// A TABELA DAS 8 CLASSES: raio letal e peso.
	///
	/// Escala media-alta a pedido do dono: 30% sao anas pequenas (360 px, ~2x o maior planeta) e 11%
	/// sao gigantes no teto (2.048 px, ~10x o maior planeta e 2 chunks de diametro). Uma gigante NAO
	/// le como um disco na tela de mundo (que mostra 384x216 px de mundo no zoom 3): ela le como um
	/// horizonte de luz que se atravessa em 25 s. E de proposito -- o lugar onde ela le como estrela
	/// e a carta e a tela do sistema.
	/// </summary>
	private static readonly (float raio, int peso)[] Tabela =
	[
		(360, 30),    // AnaVermelha
		(520, 18),    // Laranja
		(700, 16),    // Amarela
		(980, 12),    // Branca
		(1350, 8),    // Azul
		(1750, 5),    // GiganteBranca
		(2048, 9),    // GiganteVermelha
		(2048, 2),    // GiganteAzul
	];

	private const int PesoTotal = 100;   // 30+18+16+12+8+5+9+2

	/// <summary>O raio letal de uma classe, em pixels de mundo.</summary>
	public static float RaioDaClasse(ClasseDeEstrela c) => Tabela[(int)c].raio;

	private static ClasseDeEstrela ClasseDe(ulong h)
	{
		int r = (int)((h >> 8) % PesoTotal), soma = 0;
		for (int i = 0; i < Tabela.Length; i++)
		{
			soma += Tabela[i].peso;
			if (r < soma) return (ClasseDeEstrela)i;
		}
		return ClasseDeEstrela.AnaVermelha;
	}

	// =====================================================================
	// AS ORBITAS
	// =====================================================================
	/// <summary>
	/// Folga entre a superficie letal da estrela e a orbita mais interna.
	///
	/// 900 px sao 5,6 s de voo base -- perto o bastante pra a estrela dominar a tela do planeta mais
	/// interno. E 900 &gt; 199 (o maior raio de planeta que existe) com 701 px de sobra, entao NENHUM
	/// planeta pode nascer tocando a estrela, em nenhuma classe. E essa desigualdade que faz o teto
	/// do raio letal e a distancia intra-sistema caberem juntos.
	/// </summary>
	public const double FolgaDaCoroa = 900;

	/// <summary>
	/// Passo entre orbitas vizinhas: 9 a 14 s de voo base.
	///
	/// LINEAR E NAO GEOMETRICA, de proposito. Titius-Bode (`a0 * r^k`) poria o ultimo salto em
	/// ~11.500 px = 72 s de voo: o sistema viraria um caroco no meio e um deserto na borda, e
	/// "voar a mao ate o proximo planeta" quebraria na orbita 8. Perde-se realismo astronomico e
	/// ganha-se um sistema que se atravessa a mao inteiro.
	/// </summary>
	public const double PassoMinimo = 1400;
	public const double PassoMaximo = 2200;

	/// <summary>Quantos planetas um sistema gerado pode ter. Media medida: 5,5.</summary>
	public const int PlanetasMaximo = 10;

	/// <summary>Maior raio que um planeta gerado alcanca (ver `SistemaSolar.Planeta`).</summary>
	private const double RaioDePlanetaMaximo = 199;

	/// <summary>
	/// Teto do raio de um sistema gerado. O maior que o gerador consegue produzir mede
	/// `2048 + 900 + 9*2200 + 199` = 22.947 px; 23.000 e o teto com folga de 53 px.
	///
	/// ============================ ELE NAO E MAIS A MARGEM DO SORTEIO ============================
	/// Ele ERA: a estrela nascia a 23.000 px de cada divisa, o que deixava 19.536 px de caixa livre
	/// (29,8% do lado da celula) e desenhava uma RETICULA na carta -- 96,6% dos vizinhos mais
	/// proximos caiam a menos de 15 graus de um eixo, contra os 33,3% de um campo isotropico. Quem
	/// pagava a conta era o sistema PEQUENO, obrigado a guardar a margem do gigante.
	///
	/// Hoje a margem e o raio do PROPRIO sistema (ver <see cref="Do"/>), e este teto sobrou com os
	/// dois papeis que sempre foram dele de verdade:
	///   * o alcance que o <see cref="Espaco.PorPerto"/> usa pra saber quantas CELULAS varrer;
	///   * o limite que a bancada afirma -- nenhum sistema pode passar dele, senao a margem por
	///     celula poderia comer a celula inteira.
	/// ==========================================================================================
	/// </summary>
	public const double RaioSistemaTeto = 23_000;

	/// <summary>
	/// FOLGA ENTRE AS BORDAS DE DOIS SISTEMAS VIZINHOS -- o que impede que eles se TOQUEM.
	///
	/// A desigualdade da margem por celula (ver <see cref="Do"/>) e exata: com margem = raio, dois
	/// sistemas vizinhos podem ficar TANGENTES, e ai o planeta mais externo de um nasceria em cima
	/// do planeta mais externo do outro. 500 px de cada lado poem 1.000 px entre as bordas -- 6 s de
	/// voo base, mais que o dobro do maior planeta que existe (199 px de raio).
	///
	/// Ela custa 1.000 px de jitter no par, contra os 46.000 px que o teto cobrava. E o preco de
	/// nunca precisar escrever uma regra de rejeicao por sobreposicao entre gerados.
	/// </summary>
	public const double FolgaEntreSistemas = 500;

	/// <summary>
	/// QUANTAS CELULAS EM 256 NAO TEM SISTEMA NENHUM -- o vazio de verdade.
	///
	/// ============================ JITTER BORRA A GRADE; SO A REJEICAO ABRE BURACO ============================
	/// Espalhar a estrela dentro da celula tira as linhas e as colunas, mas nao muda a DENSIDADE: com
	/// uma estrela por celula, o ponto mais vazio do universo media 54.713 px ate a estrela mais
	/// proxima -- 5,7 min de voo base, em qualquer lugar, sempre. Nao existia deserto pra atravessar.
	///
	/// O sorteio e um `if` sobre bits que ninguem mais le, feito ANTES de o resto do sistema ser
	/// calculado: a celula vazia sai mais barata que a cheia, entao o universo com vazio custa MENOS
	/// por celula que o de antes.
	/// ======================================================================================================
	///
	/// ============================ A TAXA SAIU DE VARREDURA, E ELA TEM DOIS LADOS ============================
	/// Varredura de 10.000 celulas por taxa (seed 20260802), medindo o que o jogador sente: a
	/// distancia de um ponto QUALQUER ate a estrela mais proxima.
	///
	///     taxa      cheias   p99 ate a estrela   pior caso      pior caso a 11x   3x3 sem estrela
	///       0        100%         51.968 px      69.281 px          105 s              0,00%
	///      64 (25%)   75%         80.792 px     124.952 px          189 s              0,04%
	///      96 (37%)   62%         94.397 px     152.511 px          231 s              0,21%
	///     128 (50%)   50%        116.638 px     173.766 px          263 s              1,45%
	///     160 (62%)   37%        128.878 px     191.139 px          290 s              2,82%
	///
	/// 96 e onde os dois lados ainda cabem: o vazio quase TRIPLICA (o pior lugar do universo saiu de
	/// 69 mil px pra 152 mil) e mesmo assim a travessia mais longa custa 231 s no teto de velocidade,
	/// com 62% das celulas ainda habitadas -- a carta continua parecendo uma galaxia, e nao um campo
	/// de poeira. De 128 pra cima a conta vira "uma em cada setenta viagens comeca sem estrela
	/// nenhuma a vista", que e a viagem chata que o dono nao pediu.
	/// ====================================================================================================
	/// </summary>
	public const int VaziosPor256 = 96;

	/// <summary>
	/// A ZONA HABITAVEL de uma estrela: [3R, 7R].
	///
	/// Medido sobre 25.600 sistemas: 91% tem ao menos uma orbita na faixa, 60% tem exatamente uma, e
	/// 9% nao tem nenhuma. As alternativas testadas ([4R,9R] e [4R,12R]) deixavam 23% e 16% dos
	/// sistemas sem nenhuma -- uma faixa que quase sempre significa alguma coisa, e as vezes
	/// significa "este sistema e morto", e a que da informacao.
	/// </summary>
	public static (double dentro, double fora) ZonaHabitavelDe(double raioDaEstrela) =>
		(3 * raioDaEstrela, 7 * raioDaEstrela);

	/// <summary>
	/// A DIRECAO DA ORBITA k -- um vetor unitario EXATO, sem `Math.Cos` nem `Math.Sin`.
	///
	/// ============================ POR QUE NAO USAR COSSENO ============================
	/// O `GeradorDeTerreno` ja escreveu a regra e pagou por ela: `+ - * /` sao corretamente
	/// arredondados pelo IEEE-754 e dao bit a bit o mesmo resultado em qualquer maquina, enquanto
	/// `Cos`/`Sin`/`Pow` caem na biblioteca matematica da plataforma e podem diferir no ultimo bit
	/// entre Windows e Linux. O servidor pode nao rodar no mesmo sistema que o cliente, e uma orbita
	/// um ULP diferente e o jogador pousando "no vazio" ao lado do planeta que ele ve.
	///
	/// A saida e a parametrizacao RACIONAL do circulo (a formula do meio-angulo):
	///     cos = (1 - t^2)/(1 + t^2),  sin = 2t/(1 + t^2)
	/// que com `t` em [0,1] cobre o primeiro quadrante usando so as quatro operacoes exatas. Dois
	/// bits do hash escolhem o quadrante, e girar 90 graus e trocar e negar coordenada -- tambem
	/// exato. Cobre o circulo inteiro.
	///
	/// O QUE SE PERDE: a fase nao e uniforme em angulo dentro do quadrante (a densidade varia no
	/// maximo 2:1, porque `dθ/dt = 2/(1+t^2)`). Pra fase de orbita isso e invisivel -- o que
	/// importa e que dois planetas nao nascam colados e que os dois lados concordem. O que se ganha
	/// e a unica coisa que nao da pra recuperar depois: igualdade bit a bit entre maquinas.
	/// ================================================================================
	/// </summary>
	internal static (double x, double y) DirecaoDaOrbita(ulong hDoSistema, int k)
	{
		ulong h = Espaco.Misturar(hDoSistema ^ SalDaOrbita, (ulong)(uint)k, 0);

		double t = ((h >> 2) & 0xFFFF) / 65535.0;
		double d = 1 + t * t;
		double c = (1 - t * t) / d, s = 2 * t / d;

		return (h & 3) switch
		{
			0 => (c, s),
			1 => (-s, c),
			2 => (-c, -s),
			_ => (s, -c),
		};
	}

	/// <summary>
	/// Os biomas do `ProceduralSpace.dm`, na mesma ordem. Morou em `Espaco.NomeDeBioma` enquanto os
	/// planetas nasciam soltos por chunk; veio junto com eles.
	///
	/// Precisa concordar com <see cref="GeradorDeTerreno.BiomaDaSeed"/> (o mesmo `(seed >> 16) % 6`),
	/// senao um mundo chamado "Gelido-770" nasceria com areia.
	/// </summary>
	internal static string NomeDeBioma(ulong seed) => ((seed >> 16) % 6) switch
	{
		0 => "Verdejante",
		1 => "Gelido",
		2 => "Deserto",
		3 => "Vulcanico",
		4 => "Alienigena",
		_ => "Sombrio",
	};

	// =====================================================================
	// OS SISTEMAS ANCORADOS: A TERRA NAO SE MUDA, A ESTRELA DELA SIM
	// =====================================================================
	/// <summary>
	/// Quantas orbitas tem um sistema ancorado. Menor de proposito que um gerado (que vai a 10):
	/// um sistema ancorado nasce onde o pre-feito ja esta, sem poder escolher lugar, entao ele tem
	/// que caber sem empurrar vizinho. Com 4 orbitas o raio fica entre 7.900 e 10.300 px, contra os
	/// 22.947 do maior gerado.
	/// </summary>
	public const int OrbitasAncoradas = 4;

	/// <summary>
	/// A REGRA QUE INVERTE A DEPENDENCIA.
	///
	/// A celula que contem um planeta pre-feito nao gera sistema proprio: ela vira um SISTEMA
	/// ANCORADO, e **a estrela nasce a partir do pre-feito, nao o contrario**. O pre-feito ocupa uma
	/// das <see cref="OrbitasAncoradas"/> orbitas e a estrela e posta no ponto que poe aquela orbita
	/// exatamente em cima dele.
	///
	/// ============================ E A TERRA CONTINUA EM (0,0) ============================
	/// As sete posicoes seguem literais em <see cref="Espaco.PreFeitos"/>, e este arquivo nunca as
	/// recalcula: <see cref="SistemaSolar.Planeta"/> DEVOLVE o pre-feito tal como ele foi escrito
	/// quando `k == OrbitaPreFeita`. Se ele recalculasse `estrela + direcao * semieixo`, a Terra
	/// voltaria com erro de arredondamento de `float` -- e a Terra e o zero do sistema de
	/// coordenadas do universo. Andar 0,001 px seria mover o universo inteiro em relacao a ela.
	/// ===================================================================================
	///
	/// A TERRA ORBITA UM SOL AMARELO POR REGRA ESCRITA, e nao por hash. As outras seis estrelas saem
	/// do `Hash64` do nome do planeta -- deixar o hash decidir a estrela da Terra seria deixar um
	/// sorteio decidir uma coisa que o canon ja decidiu.
	/// </summary>
	private static SistemaSolar Ancorar(PlanetaNoEspaco p)
	{
		ulong h = Espaco.Hash64(p.Nome) ^ SalDoSistema;

		ClasseDeEstrela classe = p.Nome == "Earth" ? ClasseDeEstrela.Amarela : ClasseDe(h);
		float raio = RaioDaClasse(classe);
		double passo = PassoMinimo + (h >> 33) % 1024 / 1024.0 * (PassoMaximo - PassoMinimo);
		int orbita = (int)((h >> 9) % OrbitasAncoradas);
		double a = raio + FolgaDaCoroa + orbita * passo;

		(double dx, double dy) = DirecaoDaOrbita(h, orbita);
		var estrela = new Vec2((float)(p.Pos.X - dx * a), (float)(p.Pos.Y - dy * a));

		// O raio do sistema tem que alcancar o corpo mais externo, e o pre-feito e MAIOR que
		// qualquer planeta gerado (a Terra tem 220 px de disco contra os 199 do teto gerado).
		double raioDeCorpo = Math.Max(RaioDePlanetaMaximo, p.Raio);

		return new SistemaSolar
		{
			Id = SistemaId.De(estrela),
			Ancorado = true,
			Orbitas = OrbitasAncoradas,
			A0 = raio + FolgaDaCoroa,
			Passo = passo,
			H = h,
			OrbitaPreFeita = orbita,
			PreFeito = p,
			RaioDoSistema = raio + FolgaDaCoroa + (OrbitasAncoradas - 1) * passo + raioDeCorpo,
			Estrela = new Estrela
			{
				Classe = classe,
				Variante = (int)((h >> 17) % 4),
				Pos = estrela,
				Raio = raio,
				Seed = h,
			},
		};
	}

	/// <summary>
	/// Os sete sistemas ancorados, montados uma vez.
	///
	/// Campo estatico `readonly` e nao um `yield`: e funcao pura de sete literais, entao guardar o
	/// resultado nao introduz estado -- e <see cref="Do"/> consulta esta lista em TODA celula, o que
	/// aconteceria por tique por jogador se cada consulta remontasse os sete.
	/// </summary>
	private static readonly SistemaSolar[] Ancorados = Espaco.PreFeitos().Select(Ancorar).ToArray();

	/// <summary>
	/// OS MESMOS SETE, SO OS NUMEROS QUE <see cref="Do"/> CONSULTA.
	///
	/// <see cref="SistemaSolar"/> e uma struct grande (estrela, pre-feito, oito campos): percorre-la
	/// duas vezes por celula copiava 14 vezes dezenas de bytes pra ler quatro `double`. Esta tabela
	/// magra e o que mantem o custo de uma celula na casa das dezenas de nanossegundos, que e o
	/// numero de que a carta estelar depende pra desenhar a galaxia inteira num quadro.
	/// </summary>
	private static readonly (int Sx, int Sy, double X, double Y, double RaioDoSistema)[] AncorasMagras =
		Ancorados.Select(a => (a.Id.Sx, a.Id.Sy, (double)a.Estrela.Pos.X, (double)a.Estrela.Pos.Y, a.RaioDoSistema))
				 .ToArray();

	/// <summary>Os sistemas com planeta pre-feito. Pra carta, pra tela do sistema e pra bancada.</summary>
	public static IReadOnlyList<SistemaSolar> ComPreFeito => Ancorados;

	// =====================================================================
	// A FUNCAO
	// =====================================================================
	/// <summary>
	/// O QUE EXISTE NESTA CELULA. **Esta e a funcao.** Tudo o mais neste arquivo a serve.
	///
	/// Tres desfechos, nesta ordem:
	///   1. a celula e a de um sistema ANCORADO -> devolve ele (a estrela de um pre-feito manda);
	///   2. o sistema gerado se sobrepoe a um ancorado -> devolve NULO (celula anulada). A bancada
	///      mede quantas: sao poucas, e o numero e afirmado la justamente porque um teto que nunca
	///      dispara e indistinguivel de teto nenhum;
	///   3. senao, o sistema gerado.
	///
	/// O cheque do passo 2 e O(7) sobre <see cref="Ancorados"/> e so precisa olhar os ancorados:
	/// dois sistemas GERADOS nao podem se sobrepor por construcao (ver o cabecalho da classe).
	/// </summary>
	public static SistemaSolar? Do(ulong seedDoMundo, int sx, int sy) => Do(seedDoMundo, sx, sy, out _);

	/// <summary>
	/// A MESMA FUNCAO, dizendo POR QUE a celula ficou vazia. Ver <see cref="CelulaVazia"/>: o vazio
	/// de projeto e a guarda do ancorado sao o mesmo `null` e coisas opostas, e so a bancada precisa
	/// distinguir os dois -- por isso o jogo chama a sobrecarga curta.
	/// </summary>
	public static SistemaSolar? Do(ulong seedDoMundo, int sx, int sy, out CelulaVazia porque) =>
		Do(seedDoMundo, sx, sy, out porque, RegraDaGrade.Hoje);

	/// <summary>
	/// A MESMA FUNCAO COM OS DOIS BOTOES A MOSTRA -- ver <see cref="RegraDaGrade"/>. **Nenhum caminho
	/// do jogo chama esta sobrecarga**: ela existe pra a bancada poder medir o universo de ONTEM com a
	/// funcao de HOJE, e assim afirmar uma RAZAO em vez de um limiar cravado.
	/// </summary>
	public static SistemaSolar? Do(ulong seedDoMundo, int sx, int sy, out CelulaVazia porque, RegraDaGrade regra)
	{
		porque = CelulaVazia.Nao;

		for (int i = 0; i < AncorasMagras.Length; i++)
			if (AncorasMagras[i].Sx == sx && AncorasMagras[i].Sy == sy) return Ancorados[i];

		ulong h = Espaco.Misturar(seedDoMundo ^ SalDoSistema, (ulong)(uint)sx, (ulong)(uint)sy);

		// ============================ O VAZIO, E ELE VEM PRIMEIRO ============================
		// Os bits 25..32 sao os UNICOS oito que ninguem mais le neste `h` (a posicao come 4..19 e
		// 44..59, a classe 8..14, a variante 17..18, o numero de orbitas 21..24, o passo 33..42).
		// Usar bits ja gastos correlacionaria "ser vazio" com "ser grande"; misturar um hash novo so
		// pra isto custaria mais que a funcao inteira. Oito bits dao passo de 0,39%, de sobra.
		//
		// O `if` vem ANTES de classe/orbitas/passo/posicao de proposito: a celula vazia sai por 6
		// operacoes, e e por isso que 37,5% de vazio deixa `Do` mais BARATO e nao mais caro.
		// ==================================================================================
		if ((int)((h >> 25) & 0xFF) < regra.VaziosPor256) { porque = CelulaVazia.Sorteada; return null; }

		ClasseDeEstrela classe = ClasseDe(h);
		float raio = RaioDaClasse(classe);
		int n = 1 + (int)((h >> 21) % PlanetasMaximo);
		double passo = PassoMinimo + (h >> 33) % 1024 / 1024.0 * (PassoMaximo - PassoMinimo);
		double a0 = raio + FolgaDaCoroa;
		double rsys = a0 + (n - 1) * passo + RaioDePlanetaMaximo;

		// ============================ A MARGEM E O RAIO DESTE SISTEMA, NAO O TETO DE TODOS ============================
		// A margem ERA <see cref="RaioSistemaTeto"/> (23.000 px), o teto do MAIOR sistema que o
		// gerador consegue produzir. Como o sistema mediano mede ~9.900 px de raio, o pequeno pagava
		// a margem do gigante: sobravam 19.536 px de caixa livre -- 29,8% do lado da celula -- e o
		// olho lia a RETICULA na carta.
		//
		// A desigualdade de verdade nunca foi o teto: ela e por PAR. Duas estrelas de celulas
		// vizinhas distam pelo menos `margemA + margemB`, e a conta e direta -- a de tras esta a no
		// maximo `margemA + livreA` da divisa (ou seja, `CelulaPx - margemA` do inicio da celula
		// dela) e a da frente a pelo menos `margemB` depois da divisa. Basta entao
		//
		//     margemA + margemB >= rsysA + rsysB
		//
		// e a margem de CADA celula ser o raio do PROPRIO sistema (mais a folga) satisfaz isso
		// exatamente, celula a celula, sem nenhuma consulta ao vizinho. Uma ana vermelha com uma
		// orbita mede 1.459 px de raio e ganha 95,4% da celula pra se mexer.
		//
		// O 3x3 do <see cref="PorPerto"/> continua valendo, e por uma conta que nem depende disto:
		// um sistema a DUAS celulas tem a estrela a pelo menos `CelulaPx + margem` = 65.536+ px do
		// ponto consultado, contra os 22.947 px do maior raio possivel. Ele tinha 65.596 px de folga
		// e passa a ter 66.995 no pior caso -- ver a secao "A GRADE" da bancada `sistemas`.
		// ==========================================================================================================
		// `MargemFixa > 0` e a regra ANTIGA (a mesma margem pra todo mundo, que era o teto). Zero -- o
		// valor de hoje -- quer dizer "o raio do proprio sistema", que e a desigualdade por par escrita
		// acima. Ver <see cref="RegraDaGrade"/>: quem passa outro valor e so a bancada.
		double margem = (regra.MargemFixa > 0 ? regra.MargemFixa : rsys) + regra.Folga;
		double livre = CelulaPx - 2 * margem;
		double x = sx * CelulaPx + margem + ((h >> 4) & 0xFFFF) / 65536.0 * livre;
		double y = sy * CelulaPx + margem + ((h >> 44) & 0xFFFF) / 65536.0 * livre;

		// A CELULA ANULADA. A caixa livre so garante que dois sistemas GERADOS nunca se tocam; o
		// ancorado nasce onde o pre-feito esta e pode encostar na divisa da celula dele, entao esta e
		// a unica sobreposicao que sobra pra conferir. A mesma <see cref="FolgaEntreSistemas"/> entra
		// aqui pra que a regra seja UMA so: nenhum par de sistemas encosta, venha ele de onde vier.
		for (int i = 0; i < AncorasMagras.Length; i++)
		{
			double dx = x - AncorasMagras[i].X, dy = y - AncorasMagras[i].Y;
			double limite = rsys + AncorasMagras[i].RaioDoSistema + regra.Folga;
			if (dx * dx + dy * dy < limite * limite) { porque = CelulaVazia.AnuladaPorAncorado; return null; }
		}

		return new SistemaSolar
		{
			Id = new SistemaId(sx, sy),
			Ancorado = false,
			Orbitas = n,
			A0 = a0,
			Passo = passo,
			H = h,
			OrbitaPreFeita = -1,
			RaioDoSistema = rsys,
			Estrela = new Estrela
			{
				Classe = classe,
				Variante = (int)((h >> 17) % 4),
				Pos = new Vec2((float)x, (float)y),
				Raio = raio,
				Seed = h,
			},
		};
	}

	/// <summary>Atalho por posicao de mundo.</summary>
	public static SistemaSolar? Em(ulong seedDoMundo, Vec2 pos)
	{
		SistemaId c = SistemaId.De(pos);
		return Do(seedDoMundo, c.Sx, c.Sy);
	}

	/// <summary>
	/// OS SISTEMAS QUE ALCANCAM ESTE PONTO -- o 3x3 de celulas em volta dele.
	///
	/// 3x3 basta e a conta e fechada, e ela NAO depende da margem do sorteio: nenhum sistema passa de
	/// <see cref="RaioSistemaTeto"/> = 23.000 px de raio, e a celula mede 65.536 px. Um sistema a duas
	/// celulas de distancia tem a estrela a pelo menos `CelulaPx + margem` do ponto consultado, ou
	/// seja mais de 65.536 px -- quase tres vezes o maior raio possivel.
	///
	/// **E por isso que alargar o jitter nao obrigou a alargar a busca.** O que amarrava a margem era
	/// a nao-sobreposicao entre sistemas vizinhos, nao esta varredura: ela tinha 65.596 px de folga
	/// com a margem antiga e tem 66.995 px no pior caso com a nova. Medido (varredura de 50.000
	/// pontos contra o anel de 3 celulas, zero casos) na secao "A GRADE" da bancada `sistemas`.
	/// </summary>
	public static void PorPerto(ulong seedDoMundo, Vec2 pos, List<SistemaSolar> saida)
	{
		SistemaId c = SistemaId.De(pos);
		for (int dy = -1; dy <= 1; dy++)
			for (int dx = -1; dx <= 1; dx++)
				if (Do(seedDoMundo, c.Sx + dx, c.Sy + dy) is { } s) saida.Add(s);
	}

	/// <summary>
	/// A ESTRELA MAIS PROXIMA DESTE PONTO, e a distancia ate o CENTRO dela. Falso quando nenhuma das
	/// nove celulas em volta tem sistema -- e desde o <see cref="VaziosPor256"/> isso e COMUM, nao
	/// mais um caso de canto: 0,4% dos pontos do universo estao num 3x3 inteiro vazio. Quem chama tem
	/// que tratar o falso como "nao ha estrela por perto" e nao como falha (o `TickDoSol` ja trata).
	///
	/// ============================ ELA NAO PASSA PELO `Espaco.PorPerto` -- DE PROPOSITO ============================
	/// A Fase 2 anotou a razao e ela continua valendo: `Espaco.PorPerto` devolve
	/// <see cref="PlanetaNoEspaco"/>, e e por essa lista que o <see cref="Espaco.PlanetaSob"/> decide
	/// ONDE SE POUSA. Uma estrela ali dentro nao seria "um corpo a mais": seria o jogador POUSANDO NO
	/// SOL, calado, e caindo numa zona de superficie que nao existe.
	///
	/// Entao a estrela tem o caminho dela, e ele e mais barato e mais exato que o dos planetas: nove
	/// hashes de celula, sem lista e sem alocacao, e o alcance e fechado por aritmetica -- a estrela
	/// nasce a pelo menos o RAIO DO PROPRIO SISTEMA de cada divisa da celula (era
	/// <see cref="RaioSistemaTeto"/> pra todo mundo; ver <see cref="Do"/>), logo uma estrela de duas
	/// celulas de distancia esta a mais de 65.536 px e nao alcanca nada aqui -- o maior raio que
	/// existe e 22.947.
	///
	/// **E por isso que o <see cref="RaioLetalMaximo"/> nao limita esta funcao.** Aquele teto nasceu do
	/// `RaioAtivo=1` do pouso (3x3 de CHUNKS de 2.048 px); aqui a varredura e de 3x3 CELULAS de 65.536,
	/// e uma estrela de qualquer raio ate meia celula seria achada. O teto fica onde estava porque ele
	/// governa a arte e a geometria do sistema -- nao porque a deteccao precise dele.
	/// ==========================================================================================================
	///
	/// A raiz quadrada no fim NAO quebra o determinismo: `sqrt` e uma das operacoes que o IEEE-754
	/// obriga a ser corretamente arredondada, exatamente como `+ - * /` -- e diferente de
	/// `Cos`/`Sin`/`Pow`, que sao da libm da plataforma e por isso estao banidas deste arquivo (ver
	/// <see cref="DirecaoDaOrbita"/>). Dentro do laco ela nem aparece: compara-se o QUADRADO.
	/// </summary>
	public static bool EstrelaPerto(ulong seedDoMundo, Vec2 pos, out Estrela estrela, out double distancia)
	{
		estrela = default;
		distancia = double.MaxValue;

		SistemaId c = SistemaId.De(pos);
		double melhor = double.MaxValue;
		bool achou = false;

		for (int dy = -1; dy <= 1; dy++)
			for (int dx = -1; dx <= 1; dx++)
			{
				if (Do(seedDoMundo, c.Sx + dx, c.Sy + dy) is not { } s) continue;

				double ex = s.Estrela.Pos.X - (double)pos.X;
				double ey = s.Estrela.Pos.Y - (double)pos.Y;
				double d2 = ex * ex + ey * ey;
				if (d2 >= melhor) continue;

				melhor = d2;
				estrela = s.Estrela;
				achou = true;
			}

		if (achou) distancia = Math.Sqrt(melhor);
		return achou;
	}

	// =====================================================================
	// A ASSINATURA
	// =====================================================================
	/// <summary>
	/// A ASSINATURA DE UMA REGIAO DO UNIVERSO. E o que prova que as duas pontas concordam.
	///
	/// Mesmo molde e mesmas constantes de <see cref="TerrenoGerado.Assinatura"/> (FNV-1a de 64 bits),
	/// pelo mesmo motivo escrito la: e estavel entre execucoes e entre maquinas, enquanto
	/// `GetHashCode()` o .NET randomiza por processo -- um hash que muda a cada boot nao prova nada.
	///
    /// ============================ DUAS REGRAS QUE PARECEM DETALHE E NAO SAO ============================
	/// 1. Ela come a ENUMERACAO INTEIRA em ordem fixa, nunca um resumo. Uma assinatura de "quantos
	///    planetas ha" fecharia verde com os planetas nos lugares errados.
	/// 2. **Nenhum `float` entra aqui.** Posicoes vao arredondadas pra inteiro e raios tambem. Float
	///    na assinatura e divergencia garantida entre maquinas -- e a divergencia apareceria como um
	///    numero diferente sem nada de errado no universo, que e pior que nao ter assinatura.
	/// ==============================================================================================
	///
	/// Custo medido: 32x32 celulas (1.024 sistemas, 5.560 planetas) em 5,2 ms -- abaixo do tique de
	/// 33 ms, entao nao precisa do `Encomendar`/`TickDasGeracoes`. Quase todo esse tempo e a MONTAGEM
	/// DOS NOMES dos planetas (uma string por corpo), e nao o hash: enumerar os mesmos 1.024 sistemas
	/// sem tocar nos planetas custa 0,09 ms. Regiao grande precisaria da encomenda.
	/// </summary>
	public static ulong Assinatura(ulong seedDoMundo, int sx0, int sy0, int lado) =>
		Assinatura(seedDoMundo, sx0, sy0, lado, RegraDaGrade.Hoje);

	/// <summary>
	/// A MESMA ASSINATURA, COM OS BOTOES DA GRADE A MOSTRA -- ver <see cref="RegraDaGrade"/>.
	///
	/// ============================ E COM ELA A `--diaguniverso` PEGA UM DEFEITO NOVO ============================
	/// A familia 1 daquela bancada compara a assinatura que o CLIENTE calcula com a que o SERVIDOR
	/// devolve pelo fio. Enquanto os dois rodam o mesmo build, ela prova determinismo -- mas nao prova
	/// que o universo e o de HOJE: dois processos no build velho concordariam entre si, verdinhos, com
	/// a reticula de volta na carta.
	///
	/// Com esta sobrecarga o cliente calcula tambem a assinatura do universo ANTIGO e exige que ela
	/// **nao** bata com a do servidor. E o mesmo hash, a mesma regiao e os mesmos dois processos; o que
	/// muda e que agora "as duas pontas concordam" e "elas concordam no universo certo" sao duas
	/// perguntas separadas, e cada uma reprova sozinha.
	/// =========================================================================================================
	/// </summary>
	public static ulong Assinatura(ulong seedDoMundo, int sx0, int sy0, int lado, RegraDaGrade regra)
	{
		const ulong offset = 14695981039346656037UL;
		const ulong prime = 1099511628211UL;
		ulong h = offset;

		void Somar(byte b) => h = (h ^ b) * prime;
		void Longo(ulong v) { for (int i = 0; i < 8; i++) Somar((byte)((v >> (i * 8)) & 0xFF)); }
		void Inteiro(long v) => Longo((ulong)v);
		void Texto(string s) { foreach (char ch in s) { Somar((byte)(ch & 0xFF)); Somar((byte)(ch >> 8)); } Somar(0); }

		Longo(seedDoMundo);
		Inteiro(sx0); Inteiro(sy0); Inteiro(lado);

		for (int sy = sy0; sy < sy0 + lado; sy++)
			for (int sx = sx0; sx < sx0 + lado; sx++)
			{
				Inteiro(sx); Inteiro(sy);

				// A CELULA ANULADA TAMBEM ASSINA. Pular a celula vazia deixaria a assinatura cega
				// justamente pra a regra dos ancorados -- que e a que tem mais chance de divergir.
				if (Do(seedDoMundo, sx, sy, out _, regra) is not { } s) { Somar(0); continue; }
				Somar(1);

				Somar((byte)s.Estrela.Classe);
				Somar((byte)s.Estrela.Variante);
				Somar((byte)(s.Ancorado ? 1 : 0));
				Inteiro((long)s.Estrela.Raio);
				Inteiro((long)Math.Round(s.Estrela.Pos.X));
				Inteiro((long)Math.Round(s.Estrela.Pos.Y));
				Texto(s.NomeDaEstrela);
				Inteiro(s.Orbitas);
				Inteiro(s.OrbitaPreFeita);

				for (int k = 0; k < s.Orbitas; k++)
				{
					PlanetaNoEspaco p = s.Planeta(k);
					Inteiro(k);
					Inteiro((long)Math.Round(s.Semieixo(k)));
					Inteiro((long)Math.Round(p.Pos.X));
					Inteiro((long)Math.Round(p.Pos.Y));
					Inteiro((long)Math.Round(p.Raio));
					Longo(p.Seed);
					Somar((byte)(p.Premade ? 1 : 0));
					Texto(p.Nome);
				}
			}

		return h;
	}
}
