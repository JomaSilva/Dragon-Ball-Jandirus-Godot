using Jandirus.Core.World;

namespace Jandirus.Core.Magic;

/// <summary>
/// **AS BLACK STAR DRAGON BALLS** -- um set global de sete, solto de zona (dono, 2026-10-21).
///
/// Nao e o Porunga nem um set de jogador: qualquer planeta vivo pode receber uma, da pra sair do
/// planeta carregando, e o desejo unico vale <see cref="MultiplicadorDaBlackStar"/> vezes o Shenron.
/// O sal de cada espalhada e sorteado no servidor e guardado no disco; daqui sai so o que e funcao
/// desse sal (o indice da vaga) e as constantes que o dono pode ajustar.
/// </summary>
public static class EsferasNegras
{
	/// <summary>
	/// Fora de 0..999 de proposito: o 0 e o Porunga, 1..999 sao sets de jogador
	/// (<see cref="Esferas.SetDeJogador"/>). Este nao entra nessa fila e nao ocupa zona.
	/// </summary>
	public const int IdDoSet = 1000;

	/// <summary>
	/// Quanto cada efeito NUMERICO do Shenron vale neste set. O dono (2026-10-21): "5x o Shenron".
	/// Desejo de uma vez por personagem continua uma vez -- o multiplicador nao abre a porta de novo.
	/// </summary>
	public const double MultiplicadorDaBlackStar = 5;

	/// <summary>
	/// Depois do desejo unico, pedra por um ano do jogo -- o mesmo prazo do set de jogador
	/// (<see cref="Esferas.OffTimeDeJogador"/>, 112 h). Knob so deste set.
	/// </summary>
	public const double OffTimeEmAnos = 1;

	public const int Desejos = 1;

	/// <summary>
	/// Poder do set alto o bastante pra a lista inteira do Shenron abrir (os patamares de
	/// <see cref="Desejos"/>). Nao e o BP de um criador: nao ha estatua nem criador.
	/// </summary>
	public const double PoderDoSet = Esferas.PoderDoEterno;

	/// <summary>
	/// Em volta da Terra, em chunks, de onde saem os planetas procedurais do sorteio. Os sete
	/// pre-feitos entram sempre. O mesmo recorte das Super Esferas (celulas perto da Terra), em
	/// planeta em vez de celula vazia do espaco.
	/// </summary>
	public const int RaioDeBuscaEmChunks = 8;

	public const string Folha = "negra";
	public const string Dragao = "shenlong";
	public const string NomeDoDragao = "SHENLONG";
	public const string Titulo = "O DRAGÃO DAS ESTRELAS NEGRAS";

	/// <summary>A folha 3x3 ja importada: "1".."7" e "default" na 8a celula (a pedra). Nao ha "inactive".</summary>
	public const string ArteDaEsfera = "res://Assets/Sprites/DU/Items/dragonball.tres";

	public const string EstadoDaPedra = "default";

	/// <summary>
	/// O png novo, sem .import ate a fila rodar `--import`. A altura do Shenron de hoje e 353 px
	/// (`Character Icons/Dragon.tres`); a arte do Shenlong e 990x878. A escala iguala as alturas.
	/// </summary>
	public const string ArteDoShenlong = "res://Assets/Sprites/Character Icons/Shenlong.tres";

	public const float AlturaDoShenronDeHoje = 353f;
	public const float AlturaDaArteDoShenlong = 878f;
	public const float EscalaDoShenlong = AlturaDoShenronDeHoje / AlturaDaArteDoShenlong;

	public static string EstadoDoSprite(int numero, bool pedra) =>
		pedra ? EstadoDaPedra : numero.ToString(System.Globalization.CultureInfo.InvariantCulture);

	public static string NomeDaEsfera(int numero) =>
		$"Esfera de {numero} estrela{(numero > 1 ? "s" : "")} negra{(numero > 1 ? "s" : "")}";

	/// <summary>
	/// Sal cravado do contra-exemplo: espalhar com ele cai no mesmo lugar. O sorteio de verdade
	/// nunca devolve este valor.
	/// </summary>
	public const ulong SalCravado = 0xA11CE00000000001UL;

	private const ulong SalDoIndice = 0xC2B2AE3D27D4EB4FUL;

	/// <summary>
	/// O lugar que <see cref="Jandirus.Core.Npc.Habitat.Vaga"/> recebe. Puro no sal: o mesmo sal
	/// devolve os mesmos sete lugares, e dois sais diferentes quase nunca devolvem o mesmo.
	/// O modulo e o de <c>Habitat.Vaga</c> (<c>(lugar + vagas - 1) % vagas</c>) -- os dois tem de
	/// andar juntos, senao a bancada e o servidor escolhem celulas diferentes.
	/// </summary>
	public static ulong[] LugaresDoSal(ulong sal, int vagas)
	{
		int n = Math.Max(vagas, 1);
		var usados = new HashSet<int>();
		var r = new ulong[Esferas.Total];
		for (int i = 0; i < Esferas.Total; i++)
		{
			ulong a = ((ulong)(uint)(i + 1) << 32) | (uint)((i + 1) * 397);
			ulong baseLugar = Espaco.Misturar(sal ^ SalDoIndice, a, (ulong)(uint)(i + 1));
			ulong lugar = baseLugar;
			for (int g = 0; g <= n; g++)
			{
				lugar = baseLugar + (ulong)g;
				int idx = (int)((lugar + (ulong)n - 1) % (ulong)n);
				if (usados.Add(idx) || g == n) break;
			}
			r[i] = lugar;
		}
		return r;
	}
}
