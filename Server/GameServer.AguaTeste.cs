using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA AGUA: acha um lago estreito, poe quem entrar NELE e alguem do OUTRO LADO.
///
/// TRES BERCOS, UM LAGO SO -- `--aguateste` na margem, `--aguadentro` dentro, `--aguanoar` no ar.
/// Cada um mede o que os outros dois nao alcancam; ver os campos <see cref="_aguaDentro"/> e
/// <see cref="_aguaNoAr"/>, e o quadro em `GameServer.cs` onde as tres flags sao lidas.
///
/// ============================ POR QUE ISTO PRECISA EXISTIR ============================
/// A agua e a terceira classe de celula (ver <see cref="ClasseDeAgua"/>), e as cinco respostas dela
/// so aparecem na tela com um lago DE VERDADE embaixo do corpo. A Terra e 44,2% agua, mas o ponto
/// de nascimento nao e -- e mandar o robo procurar um lago andando seria uma bancada que gasta
/// minutos caminhando antes da primeira conferencia, que e o que ja aconteceu com a bancada da
/// volta do planeta (por isso o `--voltateste`).
///
/// A ESCOLHA DO LAGO E ESTREITA DE PROPOSITO (<see cref="LarguraMinima"/>..<see cref="LarguraMaxima"/>
/// tiles): a metade das perguntas do dono e sobre a FOTO -- "da pra ver que e agua?", "da pra ver
/// quem esta do outro lado?" --, e um oceano de 40 tiles poe a outra margem fora da tela. Um lago
/// que nao cabe no enquadramento nao reprova nada; ele so deixa a foto muda.
///
/// ELA NAO INVENTA COLISAO NENHUMA: o corpo e posto no chao SECO, um tile antes da beira, virado
/// pra agua. Quem decide se ele passa continua sendo o `MoveRules` -- a bancada so escolhe onde a
/// pergunta vai ser feita.
/// ======================================================================================
/// </summary>
public partial class GameServer
{
	private bool _aguaDeTeste;

	/// <summary>
	/// `--aguadentro`: o corpo nasce NO MEIO DO LAGO, a pe.
	///
	/// ============================ ISTO NAO E UM ESTADO INVENTADO ============================
	/// O proprio servidor diz que ele existe e o trata de proposito: `PodeComecarANadar` aceita quem
	/// ja esta em cima da agua justamente porque da pra chegar la sem nadar -- "deslogar dentro do
	/// lago, ser jogado la por um arremesso, nascer perto demais da beira". Esta flag e o quarto
	/// jeito, e serve pra bancada exercitar o nado sem depender de um arremesso encomendado.
	///
	/// Ela e SEPARADA da `--aguateste` porque as duas medem coisas opostas: uma quer o corpo no seco
	/// (a agua tem que BARRAR) e a outra o quer molhado (o nado tem que ANDAR). Uma flag so, com um
	/// argumento, faria a bancada ter que adivinhar qual dos dois roteiros rodar.
	/// ====================================================================================
	/// </summary>
	private bool _aguaDentro;

	/// <summary>
	/// `--aguanoar`: o corpo nasce VOANDO, por cima do meio do lago.
	///
	/// ============================ POR QUE O TERCEIRO BERCO ============================
	/// Sao DOIS os caminhos que um jogador tem pra comecar a nadar, e o segundo e o ar: voar ate a
	/// agua, largar o voo e cair nadando. Ele passa por um codigo que nenhum dos outros dois bercos
	/// toca -- o `DescerAte` do `GameServer.Voo.cs`, que decide se o chao debaixo serve --, e era ali
	/// que morava o defeito do `Occupied` sem modo: a pe a agua barra, entao o pouso do NADADOR caia
	/// no desvio do "pousou dentro da pedra", teleportava pra margem e o tique seguinte apagava o
	/// nado.
	///
	/// Nascer no ar e a unica forma honesta de exercitar isso sem depender do berco da margem: la o
	/// corpo teria que decolar, atravessar e so entao pousar -- tres gestos, e o que reprova fica
	/// ambiguo entre eles.
	///
	/// ELE NASCE COM `Voando` LIGADO E ALTURA DE PAIRAR, e nao com um `AlternarVoo` forjado: a flag
	/// e um BERCO, nao um roteiro. O custo de ligar o voo e o aviso de "voce comeca a pairar" sao do
	/// gesto do jogador, e a bancada nao esta medindo o gesto -- ela esta medindo o pouso.
	/// ==============================================================================
	/// </summary>
	private bool _aguaNoAr;

	/// <summary>
	/// `--aguaparede`: o corpo nasce NA AGUA, um tile antes de uma celula de agua colada num MURO,
	/// olhando pro muro.
	///
	/// ============================ POR QUE O QUARTO BERCO, E POR QUE NAO E O MESMO LAGO ============================
	/// O conserto que fez o jogador entrar no lago mandou o MODO junto do passo (`ValidateStep(...,
	/// ModoDeTravessiaDe(pl))`). O `ZoneCollision.Bloqueia` garante que parede para em todo modo -- mas
	/// nao e ai que mora o risco, e sim na SAIDA DE EMERGENCIA do `MoveRules.Advance`, que ANTES
	/// devolvia o passo CHEIO sem conferir nada. Com o modo `APe`, todo corpo em cima de agua caia nela
	/// -- e qualquer passo dele passava sem checagem, muro incluso. Com `Nadando` a agua deixa de o
	/// prender e a checagem volta a valer. Isso precisa de UM corpo nadando contra UM muro, na tela.
	///
	/// (A saida de emergencia deixou de aprovar tudo: hoje ela e `MoveRules.Escapar` -- nao existe pra
	/// quem esta parado pelo MODO, e so libera o passo que APROXIMA de um lugar valido. Foi ela que
	/// fechou o buraco que esta bancada tinha chamado de "oco". O que se mede aqui continua sendo outro
	/// eixo: parede parando quem nada.)
	///
	/// O LAGO DOS OUTROS TRES NAO SERVE, e isso foi medido: o muro mais proximo dele com agua encostada
	/// esta a 15 tiles. O corpo da bancada anda ~35 px/s e o nado cobra Ki por segundo, entao as duas
	/// tentativas de alcancar aquele muro terminaram na EXAUSTAO antes de o muro caber na foto.
	///
	/// UM TILE ANTES E NAO EM CIMA: o pedido e "ande CONTRA a parede", entao tem que haver passo. Quem
	/// nasce ja encostado nao anda contra nada.
	/// ============================================================================================================
	/// </summary>
	private bool _aguaParede;

	/// <summary>So o primeiro que entra ganha o vizinho do outro lado -- ver <see cref="PorNaBeiraDoLago"/>.</summary>
	private bool _vizinhoDoLagoNasceu;

	/// <summary>
	/// A FAIXA DE LARGURA QUE SERVE PRA UMA FOTO. Menos de 5 tiles nao prova travessia (o corpo
	/// atravessaria de um passo); mais de 12 poe a outra margem fora do enquadramento.
	/// </summary>
	private const int LarguraMinima = 5;
	private const int LarguraMaxima = 12;

	/// <summary>Onde o lago foi achado: a celula seca da margem, o rumo da travessia e a largura.</summary>
	private readonly record struct Lago(int Cx, int Cy, int Dx, int Dy, int Largura);

	/// <summary>
	/// PROCURA UMA TRAVESSIA: chao seco -> agua por N tiles -> chao seco de novo.
	///
	/// Busca por aneis a partir de onde o corpo esta, entao o lago escolhido e o MAIS PERTO --
	/// nao o maior, nao o mais bonito. Devolve nulo quando nao ha nenhum no raio, que e a resposta
	/// honesta em 21 dos 40 andares convertidos (eles nao tem `.agua` nenhum).
	/// </summary>
	private static Lago? AcharTravessia(ZoneCollision mapa, int cx0, int cy0, int raio = 120)
	{
		(int dx, int dy)[] rumos = [(1, 0), (-1, 0), (0, 1), (0, -1)];

		for (int r = 1; r <= raio; r++)
			for (int dy0 = -r; dy0 <= r; dy0++)
				for (int dx0 = -r; dx0 <= r; dx0++)
				{
					if (Math.Abs(dx0) != r && Math.Abs(dy0) != r) continue;   // so a casca do anel
					int cx = cx0 + dx0, cy = cy0 + dy0;
					if (!Seco(mapa, cx, cy)) continue;

					foreach ((int dx, int dy) in rumos)
					{
						// ATRAS DELE TEM QUE CABER UM PASSO: o robo nasce um tile PRA TRAS e anda
						// contra a agua. Sem esta folga ele nasceria dentro da margem e a caminhada
						// de controle ("a pe a agua barra") nao teria de onde comecar.
						if (!Seco(mapa, cx - dx, cy - dy)) continue;
						if (!mapa.EhAgua(cx + dx, cy + dy)) continue;

						int largura = 0;
						while (largura < LarguraMaxima + 1 && mapa.EhAgua(cx + dx * (largura + 1), cy + dy * (largura + 1)))
							largura++;
						if (largura < LarguraMinima || largura > LarguraMaxima) continue;

						// A OUTRA MARGEM PRECISA SER PISAVEL, e com um tile de sobra: e la que o
						// vizinho nasce, e la que o nado termina.
						int fx = cx + dx * (largura + 1), fy = cy + dy * (largura + 1);
						if (!Seco(mapa, fx, fy) || !Seco(mapa, fx + dx, fy + dy)) continue;

						return new Lago(cx, cy, dx, dy, largura);
					}
				}
		return null;
	}

	/// <summary>Chao onde da pra ficar de pe: nem parede, nem agua, nem beirada do mundo.</summary>
	private static bool Seco(ZoneCollision mapa, int cx, int cy)
		=> !mapa.BlockedCell(cx, cy) && !mapa.EhAgua(cx, cy) && !mapa.NaBorda(cx, cy);

	/// <summary>O centro de uma celula, ja descontado o pe do corpo (ver <see cref="MoveRules.FeetOffsetY"/>).</summary>
	private static Vec2 NoCentroDe(int cx, int cy)
		=> new(cx * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f,
			   cy * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f - MoveRules.FeetOffsetY);

	/// <summary>
	/// POE O CORPO NA BEIRA E O VIZINHO DO OUTRO LADO.
	///
	/// O VIZINHO E UM NPC COMUM (`cidadao`, pela mesma <see cref="NascerNpc"/> de todo mundo) e nao
	/// um corpo forjado a mao: a pergunta do dono e "da pra VER quem esta do outro lado do lago", e
	/// quem responde isso e o veu do cliente olhando um corpo que entrou no mundo pelo caminho de
	/// producao. Um fantasma injetado no cliente passaria no teste sem provar nada sobre o `.vis`.
	/// </summary>
	/// <summary>
	/// PROCURA AGUA COLADA NUM MURO: devolve a celula de agua VIZINHA do muro, o rumo que aponta pro
	/// muro, e exige que atras dela haja outra celula de agua (o tile de arranque).
	///
	/// Busca por aneis a partir de onde o corpo esta, entao o muro escolhido e o MAIS PERTO -- e o mais
	/// perto importa porque quem vai medir e uma foto: um muro a 40 tiles nao cabe no enquadramento.
	/// </summary>
	private static (int Ax, int Ay, int Dx, int Dy)? AcharAguaColadaEmMuro(ZoneCollision mapa, int cx0, int cy0, int raio = 120)
	{
		(int dx, int dy)[] rumos = [(1, 0), (-1, 0), (0, 1), (0, -1)];

		for (int r = 1; r <= raio; r++)
			for (int dy0 = -r; dy0 <= r; dy0++)
				for (int dx0 = -r; dx0 <= r; dx0++)
				{
					if (Math.Abs(dx0) != r && Math.Abs(dy0) != r) continue;   // so a casca do anel
					int ax = cx0 + dx0, ay = cy0 + dy0;
					if (!mapa.EhAgua(ax, ay) || mapa.NaBorda(ax, ay)) continue;

					foreach ((int dx, int dy) in rumos)
					{
						// O MURO A FRENTE...
						if (!mapa.BlockedCell(ax + dx, ay + dy)) continue;
						// ...E AGUA ATRAS, que e de onde o corpo vai andar contra ele. Sem este tile o
						// berco nasceria colado e nao haveria passo nenhum a medir.
						if (!mapa.EhAgua(ax - dx, ay - dy)) continue;
						return (ax, ay, dx, dy);
					}
				}
		return null;
	}

	private void PorNaBeiraDoLago(ServerPlayer pl)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		if (mapa == null) { GD.PushWarning("[server] BANCADA DA AGUA: sem mapa nesta zona"); return; }
		if (!mapa.TemAgua) { GD.PushWarning($"[server] BANCADA DA AGUA: a zona '{pl.Zone.Name}' nao tem plano de agua"); return; }

		(int cx0, int cy0) = CelulaDoPonto(pl.Pos);

		// ---------- O QUARTO BERCO PROCURA OUTRA COISA E SAI POR AQUI ----------
		// Ele nao quer travessia nenhuma: quer agua encostada em muro. Ver o campo `_aguaParede`.
		if (_aguaParede)
		{
			if (AcharAguaColadaEmMuro(mapa, cx0, cy0) is not { } muro)
			{
				GD.PushWarning("[server] BANCADA DA AGUA: nenhuma agua colada em muro num raio de 120 tiles");
				return;
			}

			// UM TILE ANTES, OLHANDO PRO MURO.
			pl.Pos = NoCentroDe(muro.Ax - muro.Dx, muro.Ay - muro.Dy);
			pl.Facing = (muro.Dx, muro.Dy) switch
			{
				(1, 0) => Facing.East,
				(-1, 0) => Facing.West,
				(0, 1) => Facing.South,
				_ => Facing.North,
			};
			GD.Print($"[server] BANCADA DA AGUA: {pl.Name} na AGUA em ({muro.Ax - muro.Dx},{muro.Ay - muro.Dy}),"
					 + $" rumo ({muro.Dx},{muro.Dy}), muro em ({muro.Ax + muro.Dx},{muro.Ay + muro.Dy}) em '{pl.Zone.Name}'");
			return;
		}

		if (AcharTravessia(mapa, cx0, cy0) is not { } lago)
		{
			GD.PushWarning($"[server] BANCADA DA AGUA: nenhuma travessia de {LarguraMinima}..{LarguraMaxima} tiles em 120 tiles");
			return;
		}

		// UM TILE ANTES DA MARGEM, OLHANDO PRA AGUA. E dai que sai a caminhada de controle do item 1.
		// Com `--aguadentro` e `--aguanoar`, no MEIO da agua -- ver os campos.
		bool noMeio = _aguaDentro || _aguaNoAr;
		pl.Pos = noMeio
			? NoCentroDe(lago.Cx + lago.Dx * (1 + lago.Largura / 2), lago.Cy + lago.Dy * (1 + lago.Largura / 2))
			: NoCentroDe(lago.Cx - lago.Dx, lago.Cy - lago.Dy);

		// ...E COM `--aguanoar`, TAMBEM NO AR. A altura e a de PAIRAR, que e onde o voo estabiliza
		// sozinho (`TickDoVoo`): mais alto exigiria segurar a descida, e a bancada mede o pouso, nao
		// a queda. Quem manda a altura pro cliente e o snapshot (`Voando = pl.Altitude > 0f`), entao
		// nao ha nada mais a avisar aqui.
		if (_aguaNoAr)
		{
			pl.Voando = true;
			pl.Altitude = Jandirus.Core.World.Voo.AlturaDePairar;
		}
		pl.Facing = (lago.Dx, lago.Dy) switch
		{
			(1, 0) => Facing.East,
			(-1, 0) => Facing.West,
			(0, 1) => Facing.South,
			_ => Facing.North,
		};

		GD.Print($"[server] BANCADA DA AGUA: {pl.Name} {(_aguaNoAr ? "NO AR sobre" : noMeio ? "DENTRO" : "na beira")}"
				 + $" de ({lago.Cx},{lago.Cy}) rumo ({lago.Dx},{lago.Dy}),"
				 + $" lago de {lago.Largura} tiles em '{pl.Zone.Name}'");

		if (_vizinhoDoLagoNasceu) return;
		_vizinhoDoLagoNasceu = true;

		// O VIZINHO FICA NA PRIMEIRA CELULA SECA DA OUTRA MARGEM -- colado na agua, e nao longe:
		// ele tem que caber na mesma tela que o lago.
		int fx = lago.Cx + lago.Dx * (lago.Largura + 1), fy = lago.Cy + lago.Dy * (lago.Largura + 1);
		ServerPlayer? vizinho = NascerNpc("cidadao", pl.Zone, NoCentroDe(fx, fy), 7980);
		if (vizinho == null) { GD.PushWarning("[server] BANCADA DA AGUA: o vizinho do outro lado nao nasceu"); return; }
		GD.Print($"[server] BANCADA DA AGUA: '{vizinho.Name}' (id {vizinho.Id}) do outro lado, em ({fx},{fy})");
	}
}
