using Jandirus.Core.World;

namespace Jandirus.Core.Magic;

/// <summary>
/// **AS SUPER ESFERAS DO DRAGAO** -- onde nascem, quanto custa tomar uma, e a que distancia o radar
/// dourado as sente. Porte de `Code/Modules/Tech/ProceduralSpace.dm:1409-1728`.
///
/// ============================ ELAS NAO SAO AS OUTRAS SETE, E A DIFERENCA E ESTRUTURAL ============================
/// A esfera comum e um objeto de 32 px que se pega do chao e se carrega na mao. A Super Esfera tem o
/// **tamanho de um planetoide**: o DM a marca `SaveItem = 0` e escreve na descricao dela *"impossivel
/// de carregar. Reivindique-a (claim)"*. Ela nao entra em mochila nenhuma -- o que se disputa e a
/// POSSE, num canal de tempo, exatamente como uma bandeira de conquista.
///
/// E e por isso que este arquivo nao herda nada do <see cref="Esferas"/>: as duas compartilham o
/// numero sete e mais nada. Um tipo-base comum obrigaria as duas a explicarem, em todo lugar, se
/// aquele campo vale pra elas.
/// ============================================================================================================
///
/// ============================ O SETOR DO DM VIROU A CELULA DE SISTEMA ============================
/// O DM enderecava por SETOR (`sx`,`sy`) porque cada setor era um `z` do BYOND. Aqui nao ha setor: o
/// espaco e um plano continuo, e a unidade de navegacao que sobrou -- a que a carta estelar ja usa
/// como bloco e a que <see cref="Sistemas"/> usa como celula -- e o <see cref="SistemaId"/>.
///
/// Traduzir "setor" por "celula de sistema" mantem as tres constantes do DM com o mesmo SIGNIFICADO
/// (perto/longe/no alcance do radar) em vez de com o mesmo numero: 2 celulas de distancia sao
/// 131.072 px, que e uma viagem de verdade -- que e o que `SDB_MIN_DIST` sempre quis dizer.
/// ==========================================================================================
/// </summary>
public static class SuperEsferas
{
	/// <summary>Sete, como as outras -- e o registro `pspace_superdb` tem sete entradas (:54).</summary>
	public const int Total = 7;

	// =====================================================================
	// ONDE NASCEM -- `pspace_sdb_scatter()` (:1422-1437)
	// =====================================================================
	/// <summary>`SDB_MAX_DIST 8` (:40): a celula sorteada fica em `rand(-8,8)` nos dois eixos.</summary>
	public const int CelulasNoMaximo = 8;

	/// <summary>
	/// `SDB_MIN_DIST 2` (:41): o sorteio REJEITA `|sx| + |sy| &lt; 2`.
	///
	/// Existe pra forcar exploracao -- nenhuma Super Esfera nasce em casa. E a rejeicao e literal do
	/// DM (soma dos modulos, nao Chebyshev): so as cinco celulas do losango central sao proibidas.
	/// </summary>
	public const int CelulasNoMinimo = 2;

	/// <summary>`SDB_HINT_RANGE 2` (:42): a que distancia o radar do painel Nav sente o sinal dourado.</summary>
	public const int CelulasDeSinal = 2;

	// =====================================================================
	// A DISPUTA -- `sdb_claim_channel` / `sdb_contest_channel`
	// =====================================================================
	/// <summary>`SDB_CLAIM_TICKS 100` (:44) = 10 s. Esfera SEM dono e um canal curto.</summary>
	public const double SegundosDeClaim = 10;

	/// <summary>
	/// `SDB_CONTEST_TICKS 3000` (:45) = 5 min. Tomar a esfera DE OUTRO custa trinta vezes mais.
	///
	/// A razao 30:1 e o desenho inteiro: achar uma esfera livre e uma tarefa de exploracao, e
	/// tomar uma esfera de alguem e um EVENTO -- o dono e avisado e tem cinco minutos pra chegar.
	/// </summary>
	public const double SegundosDeDisputa = 300;

	/// <summary>
	/// `SDB_CONTEST_RANGE 6` (:46), em tiles: sair desse raio derruba o canal.
	///
	/// ============================ E ISTO E A REGRA DE DEFESA INTEIRA ============================
	/// O defensor **nao precisa vencer o ladrao**. Basta nocautea-lo, mata-lo, ou empurra-lo pra fora
	/// de seis tiles -- e o logout dele tambem cancela. Defender uma Super Esfera e um problema de
	/// CONTROLE DE AREA, e nao de dano, e essa foi uma decisao do original que vale ouro no port.
	/// ======================================================================================
	/// </summary>
	public const float AlcanceDaDisputa = 6 * ZoneCollision.TileSize;

	/// <summary>O `get_dist(usr, src) &lt;= 2` do `Click()` (:1510): de onde da pra ABRIR o canal.</summary>
	public const float AlcanceDeReivindicar = 2 * ZoneCollision.TileSize;

	/// <summary>
	/// De quanto em quanto tempo o ladrao ouve que a disputa anda -- `if(!(t % 600))` (:1563), 60 s.
	/// Sem isso, cinco minutos de canal sao cinco minutos de tela muda.
	/// </summary>
	public const double SegundosEntreAvisos = 60;

	// =====================================================================
	// A POSICAO -- funcao PURA de (seed, numero, ciclo)
	// =====================================================================
	/// <summary>Sal desta camada. Ver <see cref="Sistemas"/>: cada sorteio no seu espaco.</summary>
	private const ulong SalDaSuper = 0x8FB1A1D0C9E37B4DUL;

	/// <summary>
	/// Quanto ALEM do raio do sistema a esfera fica, quando o sorteio a jogou dentro de um.
	/// 15% de folga -- longe o bastante da orbita externa pra ninguem confundir a esfera com um mundo.
	/// </summary>
	private const double FolgaDoSistema = 1.15;

	/// <summary>
	/// **ONDE ESTA A SUPER ESFERA `n` NO CICLO `ciclo`.** Funcao pura -- nada guardado, nada sorteado
	/// em runtime.
	///
	/// ============================ POR QUE O CICLO, E NAO A POSICAO NO DISCO ============================
	/// O DM guarda `pspace_superdb` inteiro no savefile "Galaxy" (:117/:185) porque o `rand()` dele nao
	/// tem volta. Aqui a posicao e derivada, e o unico estado que sobra e um inteiro por set: **quantas
	/// vezes as sete ja foram consumidas por um desejo**. `pspace_sdb_scatter()` (:1642) e re-espalhar,
	/// e re-espalhar aqui e `ciclo + 1`.
	///
	/// Isso e o que fecha a tensao que a Fase 0 nomeou: *"as Super Esferas nao podem ser funcao pura da
	/// seed -- elas tem dono e mudam de lugar"*. Elas podem: o que tem dono e o CLAIM (estado
	/// autoritativo do servidor, como um dominio de conquista) e o que muda de lugar e o CICLO. A
	/// POSICAO continua saindo da semente, e os dois lados chegam nela sozinhos.
	/// ============================================================================================
	///
	/// ELA NUNCA CAI DENTRO DE UM SISTEMA: se o ponto sorteado ficar dentro do disco de um sistema, ele
	/// e empurrado radialmente pra fora dele. Sem isto, uma Super Esfera podia nascer dentro do raio
	/// LETAL de uma gigante azul -- e reivindicar exige dez segundos parado a dois tiles dela.
	/// </summary>
	public static Vec2 PosicaoDa(ulong seedDoUniverso, int numero, int ciclo)
	{
		(int sx, int sy) = CelulaDa(seedDoUniverso, numero, ciclo);

		ulong h = Espaco.Misturar(seedDoUniverso ^ SalDaSuper, (ulong)(uint)numero,
								  ((ulong)(uint)ciclo << 32) | 0xA5A5A5A5UL);

		// DENTRO DA CELULA, com margem de 20% de cada lado. E o `rand(20, PSPACE_SIZE-20)` do DM
		// (:1432) escrito em fracao em vez de em tiles: a celula deste port tem 65.536 px e nao 500.
		double fx = 0.20 + (h & 0xFFFFFF) / (double)0x1000000 * 0.60;
		double fy = 0.20 + ((h >> 32) & 0xFFFFFF) / (double)0x1000000 * 0.60;

		double px = sx * Sistemas.CelulaPx + fx * Sistemas.CelulaPx;
		double py = sy * Sistemas.CelulaPx + fy * Sistemas.CelulaPx;

		if (Sistemas.Do(seedDoUniverso, sx, sy) is { } s)
		{
			double dx = px - s.Estrela.Pos.X, dy = py - s.Estrela.Pos.Y;
			double d = Math.Sqrt(dx * dx + dy * dy);
			double minimo = s.RaioDoSistema * FolgaDoSistema;

			if (d < minimo)
			{
				// EMPATE EXATO NO CENTRO: sem esta saida a divisao seria 0/0 e a esfera nasceria em
				// NaN -- o tipo de valor que atravessa o jogo inteiro calado ate a tela sumir.
				if (d < 1e-6) { dx = 1; dy = 0; d = 1; }
				px = s.Estrela.Pos.X + dx / d * minimo;
				py = s.Estrela.Pos.Y + dy / d * minimo;
			}
		}

		return new Vec2((float)px, (float)py);
	}

	/// <summary>
	/// A CELULA DE SISTEMA em que a esfera `n` nasce no ciclo dado -- o `rand(-8,8)` com rejeicao do
	/// `pspace_sdb_scatter` (:1425-1431), reescrito como sorteio deterministico.
	///
	/// A REJEICAO E LITERAL (`|sx| + |sy| &lt; 2`, e nao Chebyshev) e a tentativa entra no hash, que e
	/// o que faz o laco terminar sem `Random`: cada volta muda a mistura. A chance de rejeitar e
	/// 5/289, entao na pratica ele sai na primeira.
	/// </summary>
	public static (int Sx, int Sy) CelulaDa(ulong seedDoUniverso, int numero, int ciclo)
	{
		const int lado = 2 * CelulasNoMaximo + 1;

		for (int tentativa = 0; tentativa < 16; tentativa++)
		{
			ulong h = Espaco.Misturar(seedDoUniverso ^ SalDaSuper,
									  ((ulong)(uint)numero << 32) | (uint)tentativa,
									  (ulong)(uint)ciclo);

			// OS PARENTESES SAO OBRIGATORIOS: em C# o `%` tem precedencia MAIOR que o `>>`, entao
			// `h >> 32 % lado` deslocaria por `32 % lado` e os dois eixos sairiam correlacionados.
			int sx = (int)(h % (ulong)lado) - CelulasNoMaximo;
			int sy = (int)((h >> 32) % (ulong)lado) - CelulasNoMaximo;

			if (Math.Abs(sx) + Math.Abs(sy) >= CelulasNoMinimo) return (sx, sy);
		}

		// DEZESSEIS REJEICOES SEGUIDAS TEM CHANCE ~1e-20. A saida existe pra que a funcao seja TOTAL
		// (e nunca um laco infinito num servidor), e ela e deterministica como o resto.
		return (CelulasNoMaximo, numero % 3 - 1);
	}

	/// <summary>
	/// A DISTANCIA EM CELULAS entre dois pontos do espaco -- CHEBYSHEV, como o `max(abs(dx),abs(dy))`
	/// do `pspace_nav_header` (:1401).
	///
	/// Chebyshev e nao euclidiana porque a pergunta e de NAVEGACAO ("quantos saltos de celula"), e e a
	/// mesma metrica que <see cref="ChunkId.Longe"/> e <see cref="SistemaId.Longe"/> ja usam.
	/// </summary>
	public static int CelulasEntre(Vec2 a, Vec2 b) => SistemaId.De(a).Longe(SistemaId.De(b));

	/// <summary>
	/// A FRASE DO RADAR DOURADO -- `pspace_nav_header` (:1399-1405).
	///
	/// Vazia quando nao ha sinal: o painel Nav nao mostra linha nenhuma em vez de mostrar "nada por
	/// perto", que e ruido em toda tela que o jogador abrir longe de uma esfera.
	///
	/// NO ALCANCE ZERO A POSICAO SAI LITERAL, como no DM (*"NESTE setor, perto de (x,y)!"*): sem isso,
	/// achar a celula certa deixaria o jogador varrendo 65.536 px de vazio com um sinal que nao afina.
	/// </summary>
	public static string SinalDoRadar(int celulas, Vec2 ondeEla, int quantasSentidas)
	{
		if (celulas > CelulasDeSinal) return "";
		if (quantasSentidas <= 0) return "";

		string plural = quantasSentidas > 1 ? $"{quantasSentidas} sinais dourados" : "um sinal dourado";
		return celulas <= 0
			? $"RADAR: {plural} -- NESTA célula, perto de ({ondeEla.X:0}, {ondeEla.Y:0})!"
			: $"RADAR: {plural} a {celulas} célula{(celulas > 1 ? "s" : "")} daqui.";
	}
}
