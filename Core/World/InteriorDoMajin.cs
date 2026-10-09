namespace Jandirus.Core.World;

/// <summary>
/// ============================ O INTERIOR DO MAJIN, COMO LUGAR ============================
/// Dono, 2026-10-09: *"cheque pra mim se a absorçao do majin ta funcionando e se o jogador absorvido vai
/// pro interior do corpo do majin enfrentar o clone do corpo original pra escapar (assim como era no DM)"*.
/// Nao estava: o port so tinha a absorcao do bio-androide, que CONSOME. Esta e a do Majin, que SELA.
///
/// Este arquivo e so a resposta a **"que lugar e este?"** -- o `build_majin_pocket()` do original
/// (`Magic/MajinSaga.dm:67-79`) e os dois turfs dele (`:50-65`). As regras de dentro (quem entra, o
/// guardiao, quem sai e como) moram em `Server/GameServer.AbsorcaoMajin.cs`; os numeros, em
/// <see cref="Jandirus.Core.Combat.AbsorcaoMajin"/>.
/// =======================================================================================
///
/// ============================ A CHAVE DA ZONA JA CARREGA O MAJIN ============================
/// O interior e `ZoneKey.Interior("InteriorDoMajin", id)`: o NOME diz ao cliente que cenario montar e a
/// SEED **e o id do Majin**. E o `majin_pocket_z` do DM (um z-level por Majin, `:28`), sem o z: dois
/// Majins sao duas chaves, e todo prisioneiro do mesmo Majin cai no MESMO bolsao (la tambem -- por isso o
/// sorteio da celula, "so multiple prisoners don't stack", `:176`).
///
/// E e por isso que nao ha campo de "estou absorvido por fulano" (o `absorbed_into` do DM, `:31`): quem
/// esta la dentro **esta la dentro**, e o dono do lugar sai da propria chave (<see cref="Anfitriao"/>). E o
/// mesmo desenho da mente (`DimensaoMental`), pelo mesmo motivo.
/// ==========================================================================================
///
/// ============================ UMA PLANTA SO, COMPARTILHADA ============================
/// Todo interior e igual (o `build_majin_pocket` nao le nada do Majin), entao <see cref="Planta"/> devolve
/// sempre a mesma instancia, como a da nave grande e a da mente. Compartilhar o `ZoneCollision` e seguro
/// aqui pelo motivo mais forte possivel: **nada neste mapa muda** -- o chao e a parede sao indestrutiveis
/// (`destroyable = 0` nos dois turfs), nao ha porta, e ninguem constroi dentro de um corpo.
/// ====================================================================================
/// </summary>
public static class InteriorDoMajin
{
	/// <summary>O NOME DA ZONA. So identidade: o lugar em si e a <see cref="Planta"/>.</summary>
	public const string Zona = "InteriorDoMajin";

	/// <summary>Esta zona e o interior de algum Majin?</summary>
	public static bool EhOInterior(ZoneKey z) =>
		z.Kind == ZoneKey.KindInterior && string.Equals(z.Name, Zona, StringComparison.Ordinal);

	/// <summary>DENTRO DE QUEM. Zero pra qualquer zona que nao seja um interior de Majin.</summary>
	public static int Anfitriao(ZoneKey z) => EhOInterior(z) ? (int)z.Seed : 0;

	/// <summary>O interior DESTE Majin. Um bolsao por Majin, e o mesmo em toda absorcao.</summary>
	public static ZoneKey De(int majinId) => ZoneKey.Interior(Zona, (ulong)majinId);

	/// <summary>`#define MAJIN_POCKET_SIZE 100` (`MajinSaga.dm:12`). Cem celulas de lado.</summary>
	public const int Lado = 100;

	/// <summary>
	/// ONDE O PRISIONEIRO CAI -- `rand(8, psz - 8)` nos dois eixos (`MajinSaga.dm:179-180`), *"at a random
	/// interior spot (so multiple prisoners don't stack)"*.
	///
	/// O DM conta as celulas de 1 a 100 e aqui e de 0 a 99, entao o 8..92 de la e o 7..91 daqui. O eixo Y
	/// invertido (la cresce pra cima) nao muda nada: a faixa e simetrica.
	/// </summary>
	public static (int X, int Y) CelDoPrisioneiro(Random rng) =>
		(rng.Next(MenorCelula, MaiorCelula + 1), rng.Next(MenorCelula, MaiorCelula + 1));

	/// <summary>As pontas do sorteio, em celula de base zero. Publicas pra bancada conferir a faixa.</summary>
	public const int MenorCelula = 7, MaiorCelula = Lado - 9;

	/// <summary>
	/// ONDE O GUARDIAO NASCE -- `min(px + 3, psz - 2), py` (`MajinSaga.dm:218`): tres celulas ao lado do
	/// prisioneiro, sem nunca encostar na parede. Em base zero o `psz - 2` e o `Lado - 3`.
	/// </summary>
	public static (int X, int Y) CelDoGuardiao((int X, int Y) prisioneiro) =>
		(Math.Min(prisioneiro.X + 3, Lado - 3), prisioneiro.Y);

	/// <summary>O centro em pixels de uma celula da planta -- gemeo do `DimensaoMental.PixelDe`.</summary>
	public static Vec2 PixelDe((int X, int Y) cel) => new(
		(cel.X + 0.5f) * ZoneCollision.TileSize,
		(cel.Y + 0.5f) * ZoneCollision.TileSize);

	/// <summary>
	/// O PINCEL, e ele e UM so: `turf/MajinPocketFloor` e `turf/MajinPocketWall` declaram os dois
	/// `icon = 'Tiles 1.21.2011.dmi'` com `icon_state = "a 1"` (`MajinSaga.dm:52-53` e `:59-60`), e o
	/// comentario do autor avisa do espaco no nome: *"it is "a 1", not "a1""*. A parede e *"the SAME tile
	/// but solid + indestructible"* (`:48-49`).
	///
	/// O atlas ja estava convertido (`Assets/Data/tiles.json`, `Tiles 1.21.2011`, estado `a 1`): zero byte
	/// novo de asset.
	/// </summary>
	private static readonly TileVisual Carne = new("Tiles 1.21.2011", "a 1");

	/// <summary>
	/// A PALETA -- montada a mao, como a do casco da nave e a da mente, e pelo mesmo motivo: reusar o
	/// `TerrenoGerado` inteiro (o pintor por pedaco do cliente, a colisao do servidor). As dez entradas
	/// apontam pra mesma carne porque o `PlanetaProcedural.MontarCamadas` resolve os dez pinceis no
	/// nascimento, e um `TileVisual` vazio faria dez avisos a cada absorcao.
	/// </summary>
	private static readonly PaletaDeBioma PaletaDaCarne = new()
	{
		// SO UM ROTULO: a `TerrenoGerado` exige um bioma, e nenhum deles e "dentro de alguem".
		Bioma = BiomaDeTerreno.Morto,
		LimiarAgua = 0, LimiarPraia = 0, LimiarColina = 1, LimiarMontanha = 1,
		VegetacaoPct = 0, PlantaPct = 0, MinerioPct = 0,
		Agua = Carne, Praia = Carne, Planicie = Carne, Colina = Carne, Montanha = Carne,
		Arvore = Carne, ArvoreAcento = Carne, Planta = Carne, Minerio = Carne, Gema = Carne,
	};

	private static TerrenoGerado? _planta;

	/// <summary>
	/// ============================ O BOLSAO, COMO FUNCAO ============================
	/// `build_majin_pocket()` (`MajinSaga.dm:67-79`): um quadrado de <see cref="Lado"/>, a borda de
	/// `MajinPocketWall` e o miolo de `MajinPocketFloor`. La e um z-level montado a mao; aqui e a receita do
	/// interior da nave grande (`Core/Tech/NaveGrande.Planta`) e da mente: o servidor tira dela a COLISAO e
	/// o cliente o DESENHO, sem um byte de mapa na rede.
	///
	/// ============================ AS DUAS REGRAS DOS TURFS, E ONDE CADA UMA FOI PARAR ============================
	///   * **`destroyable = 0`, no chao E na parede** (`:54`, `:62`): o plano do que nao se quebra
	///     (`ZoneCollision.DefinirDuro`) sobe INTEIRO em um. E ele que o `DerrubarCelula` do servidor
	///     pergunta -- o unico ponto por onde uma celula cai. Sem isto um prisioneiro cavava a saida.
	///   * **`Enter()` devolve 0 pra TODO mob** (`:63-65`, *"a plain density wall would let flyers pass"*):
	///     quem voa acima do cenario so para em parede de PREDIO -- a celula que barra e esta sob teto
	///     (`ClasseDePredio.BarraQuemVoa`). Entao o bolsao inteiro nasce SOB TETO (`CarregarDentro`, todo em
	///     um), que alias e a verdade: nao ha ceu dentro de um corpo. A parede passa a segurar quem voa.
	///
	/// DIVERGENCIA DECLARADA: por estar sob teto, o interior nao tem clima nem lua (`CelulaInterna`). No DM
	/// o bolsao e um z sem area propria; clima la tambem nao ha, e ninguem vira Oozaru dentro de um Majin.
	/// ==========================================================================================================
	/// </summary>
	public static TerrenoGerado Planta() => _planta ??= Montar();

	private static TerrenoGerado Montar()
	{
		const int n = Lado;
		var chao = new byte[n * n];
		var cobertura = new byte[n * n];
		var bits = new byte[(n * n + 7) / 8];
		var todas = new byte[(n * n + 7) / 8];

		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				int i = y * n + x;
				cobertura[i] = (byte)CoberturaDeTerreno.Nada;
				todas[i >> 3] |= (byte)(1 << (i & 7));

				// A PAREDE E A BORDA -- `if(xx == 1 || yy == 1 || xx == sz || yy == sz)` (`MajinSaga.dm:76`).
				if (x == 0 || y == 0 || x == n - 1 || y == n - 1)
				{
					chao[i] = (byte)ClasseDeTerreno.Montanha;
					bits[i >> 3] |= (byte)(1 << (i & 7));
				}
				else chao[i] = (byte)ClasseDeTerreno.Planicie;
			}

		byte[] jcol = GeradorDeTerreno.MontarJcol(n, n, bits);
		ZoneCollision colisao = ZoneCollision.Load(jcol)!;

		// AS DUAS REGRAS DOS TURFS -- ver o cabecalho. O mesmo bitset cheio serve as duas: o plano do teto
		// copia o dele do blob, o do duro guarda este, e nenhum dos dois e escrito depois.
		colisao.DefinirDuro(todas);
		colisao.CarregarDentro(GeradorDeTerreno.MontarJcol(n, n, todas));

		return new TerrenoGerado
		{
			Largura = n,
			Altura = n,
			Bioma = BiomaDeTerreno.Morto,
			Seed = 0,               // a planta e uma so; quem separa os Majins e a `ZoneKey`
			Paleta = PaletaDaCarne,
			Chao = chao,
			Cobertura = cobertura,
			BytesDeColisao = jcol,
			BytesDeAgua = new byte[(n * n + 7) / 8],   // nao ha agua dentro de um Majin
			Colisao = colisao,
			// O MEIO DO BOLSAO. Ninguem "pousa" aqui (quem poe o prisioneiro e o sorteio do servidor); o campo
			// e exigido, e o meio e onde um corpo trazido por outro caminho (um admin) deve aparecer.
			SpawnCelX = n / 2,
			SpawnCelY = n / 2,
			ClareiraEscavada = false,
		};
	}
}
