namespace Jandirus.Core.World;

/// <summary>
/// RUIDO DE PERLIN CLASSICO (gradient noise), 2D, escrito a mao.
///
/// POR QUE ELE SUBSTITUIU O VALUE NOISE: ate aqui o relevo saia do `pspace_noise_at` do DM
/// (`ProceduralSpace.dm:1069-1082`), que e VALUE NOISE -- sorteia um numero em cada no da grade e
/// interpola. O defeito do value noise nao e a distribuicao, e a GEOMETRIA: o extremo de cada
/// mancha cai exatamente EM CIMA de um no, entao picos e fundos se alinham numa grade de 16 em 16
/// tiles e o olho enxerga o quadriculado no mapa. Perlin nao sorteia altura no no, sorteia uma
/// DIRECAO (o gradiente) e le a altura pelo produto escalar; o valor no proprio no e sempre zero, e
/// os extremos caem no MEIO das celulas, em posicoes que a grade nao dita. Foi o pedido explicito
/// do dono do projeto.
///
/// O QUE ISSO CUSTOU: a distribuicao mudou, e MUITO. Value noise devolvia algo proximo de uniforme
/// esticado; Perlin e uma soma de produtos escalares e cai numa curva tipo sino apertada em volta
/// de 0,5. Todos os limiares de bioma tiveram de ser remedidos -- ver a nota de calibracao em
/// `GeradorDeTerreno.Paletas`.
///
/// DETERMINISMO -- as mesmas tres regras do resto do Core:
///   * a tabela de permutacao sai do <see cref="Espaco.Misturar"/> (o mesmo hash que decide onde ha
///     planeta no universo), nunca de `Random`;
///   * a conta e +, -, * e / em `double` mais `Math.Floor`. Nao ha `Math.Pow`, `Sin` nem `Sqrt`
///     em lugar nenhum: as constantes irracionais que o algoritmo precisa estao CRAVADAS como
///     literais, porque literal e exato e chamada de libm nao e igual bit a bit entre plataformas;
///   * zero estado mutavel. A tabela e `readonly`, montada no construtor e nunca tocada depois.
///
/// COMO SE USA: uma instancia por (seed, sal). Montar a tabela custa 255 embaralhamentos, entao
/// NAO se cria um objeto destes por tile -- cria-se uma vez por camada de relevo e chama-se
/// <see cref="Em"/> milhares de vezes. E o que o <see cref="GeradorDeTerreno.Gerar"/> faz.
/// </summary>
public sealed class RuidoPerlin
{
	/// <summary>
	/// 1/raiz(2), cravado. E o comprimento das componentes dos quatro gradientes diagonais: sem ele
	/// a diagonal seria raiz(2) vezes mais longa que o eixo e o ruido teria direcoes preferidas
	/// (manchas esticadas nas diagonais). Literal, e nao `Math.Sqrt(2)`, por determinismo.
	/// </summary>
	private const double InvRaiz2 = 0.70710678118654752440;

	/// <summary>
	/// raiz(2), cravado: o fator que abre o resultado cru pra -1..1.
	///
	/// O maximo teorico do Perlin 2D com gradientes UNITARIOS e raiz(2)/2 = 0,7071 (o caso em que
	/// os quatro gradientes apontam todos pro ponto avaliado). Multiplicar por raiz(2) leva esse
	/// maximo a 1. Na pratica esse extremo quase nunca acontece -- e exatamente por isso que a
	/// calibracao dos limiares nao pode ser adivinhada, tem de ser medida.
	/// </summary>
	private const double Abertura = 1.41421356237309504880;

	/// <summary>
	/// Sal do embaralhamento. Precisa ser diferente dos sais das camadas de relevo, senao dois
	/// passos do Fisher-Yates de camadas diferentes cairiam no mesmo hash. Nao pode ser
	/// 0x9E3779B97F4A7C15 (a proporcao aurea): o <see cref="Espaco.Misturar"/> ja faz
	/// `seed ^ 0x9E37...` na primeira linha, e repetir a constante desfaria o XOR.
	/// </summary>
	private const ulong SalDoEmbaralho = 0xCA9E3B7F2D51A6C3UL;

	/// <summary>
	/// A permutacao de 0..255 REPETIDA (512 posicoes). A repeticao nao e desperdicio: e o truque
	/// classico do Perlin pra somar indices sem `%` -- `_p[_p[xi] + yi]` chega no maximo a 510, e
	/// a copia de tras garante que o indice continue valido sem um teste por acesso.
	/// </summary>
	private readonly byte[] _p;

	/// <summary>A seed do planeta. Guardada so pra log e pra bancada conferir de onde veio.</summary>
	public ulong Seed { get; }

	/// <summary>O sal da camada (relevo grosso, relevo fino...). Idem.</summary>
	public ulong Sal { get; }

	/// <summary>
	/// Monta a tabela de permutacao a partir de (seed, sal).
	///
	/// Fisher-Yates de tras pra frente, com o indice sorteado saindo do <see cref="Espaco.Misturar"/>.
	/// Cada passo e um hash INDEPENDENTE de (raiz, i) e nao uma corrente -- assim o resultado nao
	/// depende de o laco ter comecado no mesmo lugar, so de (seed, sal).
	/// </summary>
	public RuidoPerlin(ulong seed, ulong sal)
	{
		Seed = seed;
		Sal = sal;

		var tab = new byte[512];
		for (int i = 0; i < 256; i++) tab[i] = (byte)i;

		ulong raiz = seed ^ sal;
		for (int i = 255; i > 0; i--)
		{
			ulong h = Espaco.Misturar(raiz, (ulong)i, SalDoEmbaralho);
			int j = (int)(h % (ulong)(i + 1));
			(tab[i], tab[j]) = (tab[j], tab[i]);
		}

		for (int i = 0; i < 256; i++) tab[256 + i] = tab[i];
		_p = tab;
	}

	/// <summary>
	/// A CURVA DE SUAVIZACAO QUINTICA de Perlin (2002): 6t^5 - 15t^4 + 10t^3.
	///
	/// A cubica antiga (`3t^2 - 2t^3`, a smoothstep que o value noise do DM usava) tem derivada
	/// PRIMEIRA zero nas pontas mas derivada SEGUNDA descontinua. Em ruido de gradiente isso
	/// aparece: a quebra de curvatura na divisa das celulas vira uma faixa visivel no relevo, um
	/// xadrez fantasma de 16 em 16 tiles. A quintica zera as duas derivadas nas pontas e a divisa
	/// some.
	///
	/// Escrito por Horner (`t*t*t*(t*(t*6-15)+10)`) de proposito: e a mesma conta em 4
	/// multiplicacoes e sem nenhuma `Math.Pow`, que e proibida aqui por determinismo.
	/// </summary>
	private static double Suavizar(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

	/// <summary>
	/// O produto escalar entre o gradiente do canto e o vetor CANTO -> PONTO.
	///
	/// Oito direcoes, todas de comprimento 1: os quatro eixos e as quatro diagonais. Perlin usava
	/// so as quatro diagonais no artigo de 2002; com quatro o ruido ganha um leve viezinho
	/// diagonal, e oito e o conjunto que a literatura assentou pro caso 2D. O `switch` substitui
	/// uma tabela de gradientes: sem array nao ha teste de limite nem campo estatico pra alguem
	/// escrever por engano.
	/// </summary>
	private static double Gradiente(int h, double x, double y) => (h & 7) switch
	{
		0 => x,
		1 => -x,
		2 => y,
		3 => -y,
		4 => (x + y) * InvRaiz2,
		5 => (-x + y) * InvRaiz2,
		6 => (x - y) * InvRaiz2,
		_ => (-x - y) * InvRaiz2,
	};

	/// <summary>Interpolacao linear. Nesta forma (a + t*(b-a)) t=0 devolve `a` EXATO.</summary>
	private static double Entre(double a, double b, double t) => a + (b - a) * t;

	/// <summary>
	/// O Perlin CRU, antes da abertura -- fica em [-0,7071 ; 0,7071]. Privado porque essa faixa
	/// meio arbitraria e detalhe do algoritmo e nao serve pra ninguem de fora.
	/// </summary>
	private double Cru(double x, double y)
	{
		// o canto inferior-esquerdo da celula. Math.Floor (e nao um cast) porque cast trunca em
		// direcao ao zero, e com coordenada negativa isso pularia uma celula inteira na origem.
		int cx = (int)Math.Floor(x);
		int cy = (int)Math.Floor(y);

		// a posicao DENTRO da celula, 0..1
		double fx = x - cx;
		double fy = y - cy;

		// `& 255` num int negativo funciona em complemento de dois e cai em 0..255 -- e o que
		// deixa o ruido cobrir coordenada negativa sem um `if` no caminho quente
		int xi = cx & 255;
		int yi = cy & 255;

		// a corrente classica de indices: a coluna x escolhe a linha, a linha soma y
		int a = _p[xi] + yi;
		int b = _p[xi + 1] + yi;

		// quatro gradientes, quatro vetores canto -> ponto. Note que o vetor MUDA por canto: pro
		// canto da direita o ponto esta a (fx - 1) dele, e e essa subtracao que faz o valor do
		// ruido ser zero em cima de cada no da grade -- a marca do gradient noise.
		double n00 = Gradiente(_p[a], fx, fy);
		double n10 = Gradiente(_p[b], fx - 1.0, fy);
		double n01 = Gradiente(_p[a + 1], fx, fy - 1.0);
		double n11 = Gradiente(_p[b + 1], fx - 1.0, fy - 1.0);

		double u = Suavizar(fx);
		double v = Suavizar(fy);

		return Entre(Entre(n00, n10, u), Entre(n01, n11, u), v);
	}

	/// <summary>
	/// O ruido em [-1, 1]. O `clamp` e cinto de seguranca contra o ultimo bit do ponto flutuante:
	/// o maximo teorico bate exatamente em 1 e nao se ganha nada deixando 1,0000000000000002 vazar
	/// pros limiares de bioma.
	/// </summary>
	public double Bruto(double x, double y)
	{
		double v = Cru(x, y) * Abertura;
		return v < -1.0 ? -1.0 : v > 1.0 ? 1.0 : v;
	}

	/// <summary>
	/// O ruido NORMALIZADO em [0, 1] -- e esta a porta que o gerador de terreno usa, porque os
	/// limiares de bioma todos vivem nessa faixa.
	///
	/// ATENCAO A QUEM FOR MEXER: 0..1 e a FAIXA, nao a distribuicao. Os valores se amontoam perto
	/// de 0,5 (medida real em `GeradorDeTerreno.Paletas`); tratar isso como uniforme e o jeito
	/// de fazer montanha virar raridade sem ninguem perceber.
	/// </summary>
	public double Em(double x, double y) => Bruto(x, y) * 0.5 + 0.5;
}
