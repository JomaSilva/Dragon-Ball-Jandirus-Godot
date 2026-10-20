using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A ARTE DE CADA PLANETA -- o quadro de `Misc/Planets.tres` que o espaco desenha, e agora a carta estelar e a
/// tela do sistema tambem.
///
/// ============================ O PEDIDO, E POR QUE A ESCOLHA MORA AQUI ============================
/// O dono (2026-10-19): *"na aba de nav system, quando vc der zoom em um sistema ou abrir o mapa do sistema,
/// poderia usar o proprio sprite do planeta pra mostrar ele assim como ta feito com as estrelas, ao inves de
/// uma bola feita de ui do godot"*.
///
/// O icone ja existia e ja era escolhido -- pelo `PlanetaDesenhado`, o disco que se ve voando no espaco: a Terra
/// pelo nome, o mundo gerado pelo bioma. Esta classe e essa escolha TIRADA de la e posta num lugar so. O disco
/// do espaco, a carta e a tela do sistema perguntam aqui, entao a Terra da carta e a mesma Terra que se ve pela
/// janela da nave. Duas tabelas de icone (uma no espaco, outra no menu) discordariam no primeiro planeta novo
/// da folha, e ninguem perceberia ate alguem comparar as duas telas.
/// ================================================================================================
///
/// O QUADRO ENCOSTA NA BORDA: o disco da folha toca os quatro lados do quadro de 128 px -- e por isso o espaco
/// escala o sprite por `Raio*2/128` (o cabecalho de `ArteDeEstrela` explica por que a ESTRELA nao e assim: la o
/// nucleo e uma fracao do quadro). Entao desenhar um planeta de raio r na tela e desenhar o quadro inteiro num
/// quadrado de lado 2r, centrado nele.
/// </summary>
public static class ArteDePlaneta
{
	private const string Folha = "res://Assets/Sprites/Misc/Planets.tres";

	/// <summary>O lado de um quadro na folha (640x512, vinte quadros de 128x128). Converte raio em escala.</summary>
	public const float LadoDoQuadro = 128f;

	/// <summary>
	/// O MENOR RAIO DE TELA EM QUE O MUNDO E DESENHADO COM A ARTE; abaixo dele, o ponto colorido de sempre.
	///
	/// E o mesmo raciocinio da estrela da carta (que so ganha a folha a partir de 10 px): num disco de 2 ou 3 px a
	/// arte de 128 px vira um borrao de um pixel, e o ponto colorido diz mais -- "tem mapa proprio" (laranja) ou
	/// "e gerado" (azul-acinzentado). O planeta aguenta um degrau mais baixo que a estrela porque o desenho dele e
	/// um disco cheio ate a borda, sem coroa: com 5 px de raio ja se le a cor do mundo (o azul da Terra, o verde
	/// de Namek). Na tela do sistema o piso do modo legivel e 12 px (`TelaDoSistema.RaioLegivelMinimo`), entao la todo
	/// mundo sai com a arte -- e grande o bastante pra ela se ler.
	/// </summary>
	public const float RaioMinimoDaArte = 5f;

	/// <summary>
	/// O quadro de cada estado, pedido uma vez. A carta desenha o mesmo mundo a cada redesenho, e perguntar a
	/// folha de novo seria uma chamada ao motor por planeta por quadro. Guarda o NULO tambem: o estado que a folha
	/// nao tem avisa uma vez, e nao a cada desenho.
	/// </summary>
	private static readonly Dictionary<string, Texture2D?> _quadros = new(StringComparer.Ordinal);

	/// <summary>
	/// O estado da folha de um mundo PRE-FEITO: casa pelo nome (a Terra usa `earth`). Sem correspondencia sobra um
	/// mundo generico -- nunca um quadrado vazio.
	/// </summary>
	public static string EstadoDoPreFeito(string nome) =>
		nome.Trim().ToLowerInvariant().Replace(' ', '_') switch
		{
			"earth" => "earth",
			"namek" => "namek",
			"vegeta" => "vegeta",
			"icer_planet" or "icer" => "icer_planet",
			"arlia" => "arlia",
			"arconia" or "acronia" => "arconia",
			"hell" => "hell",
			"heaven" => "heaven",
			"big_gete_star" => "big_gete_star",
			"vampa" => "vampa",
			_ => "jungle",
		};

	/// <summary>
	/// O estado da folha de um mundo GERADO: o icone do BIOMA. A folha nao tem um icone por seed -- nem poderia --,
	/// entao o que da pra fazer e escolher o parente mais proximo pelo tipo: um Jardim sorteado se parece com
	/// selva porque ele E uma selva.
	/// </summary>
	public static string EstadoDoBioma(BiomaDeTerreno bioma) => bioma switch
	{
		BiomaDeTerreno.Jardim => "jungle",
		BiomaDeTerreno.Deserto => "desert",
		BiomaDeTerreno.Gelado => "icer_planet",
		BiomaDeTerreno.Vulcanico => "hell",
		BiomaDeTerreno.Morto => "big_gete_star",
		_ => "vegeta",   // Rochoso: o mundo de pedra do original
	};

	/// <summary>
	/// O estado da folha de um corpo da carta. O bioma sai da seed pela MESMA funcao do Core que o terreno usa
	/// (`GeradorDeTerreno.BiomaDaSeed`, que e o que o `MundoProcedural.DaSeed` poe no campo `Bioma`): as duas
	/// pontas e as tres telas derivam o mesmo icone sem trocar um byte.
	/// </summary>
	public static string EstadoDe(PlanetaNoEspaco p) =>
		p.Premade ? EstadoDoPreFeito(p.Nome) : EstadoDoBioma(GeradorDeTerreno.BiomaDaSeed(p.Seed));

	/// <summary>A arte de um corpo da carta, ou nulo se a folha nao carregou (quem desenha cai no disco).</summary>
	public static Texture2D? De(PlanetaNoEspaco p) => Quadro(EstadoDe(p));

	/// <summary>
	/// O quadro de um estado da folha. FALHA ALTO, uma vez: sem a folha, ou sem o estado, o planeta sairia como um
	/// disco liso e ninguem saberia por que -- o aviso diz o que faltou.
	/// </summary>
	public static Texture2D? Quadro(string estado)
	{
		if (_quadros.TryGetValue(estado, out Texture2D? pronto)) return pronto;

		Texture2D? quadro = null;
		SpriteFrames? frames = FolhasPresas.Carregar(Folha);
		if (frames == null) GD.PushWarning("[planeta] sem Planets.tres");
		else if (frames.HasAnimation(estado) && frames.GetFrameCount(estado) > 0)
			quadro = frames.GetFrameTexture(estado, 0);
		else
		{
			GD.PushWarning($"[planeta] a folha nao tem o estado '{estado}'");
			if (frames.HasAnimation("jungle") && frames.GetFrameCount("jungle") > 0)
				quadro = frames.GetFrameTexture("jungle", 0);
		}

		_quadros[estado] = quadro;
		return quadro;
	}
}
