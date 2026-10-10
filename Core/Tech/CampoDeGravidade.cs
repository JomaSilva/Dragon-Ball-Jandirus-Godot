using Jandirus.Core.World;

namespace Jandirus.Core.Tech;

/// <summary>
/// ============================ O CAMPO DA MAQUINA DE GRAVIDADE, COMO GEOMETRIA ============================
/// Dono, 2026-10-09: *"verifique se a maquina de gravidade tb esta funcionando"*. Nao estava, e eram duas
/// coisas -- as duas moram aqui:
///
///   1. **QUEM ESTA NO CAMPO ERA DECIDIDO SO QUANDO ALGUEM MEXIA NA MAQUINA.** O port lia "quem esta dentro"
///      no instante de ajustar a gravidade, de comprar alcance ou de a bateria acabar -- e nunca mais. Quem
///      ENTRAVA andando num campo ligado nao sentia nada; quem SAIA (ate de planeta) levava a gravidade
///      junto. O DM rele a cada volta do laco de stats, de proposito e por escrito (`Gravity.dm:135-146`:
///      *"recalculado TODO tick ... Modelo SET = robusto"*).
///   2. **O TAMANHO DO CAMPO NAO ERA O DO ORIGINAL.** A maquina cria um `obj/bounding_box` com
///      `bounding_box_create(loc, list(Range, Range))` (`Gravity.dm:320`), e a caixa tem `32 * Range` px de
///      LADO, centrada no tile dela (`Procs/BoundingBoxes.dm:20-23`). O port usava `(Range + 1)` tiles de
///      RAIO -- mais que o dobro no alcance 10, e um campo de tres tiles numa maquina de alcance ZERO.
/// =========================================================================================================
///
/// ============================ A CAIXA: O TETO DO CAMPO ============================
/// `bounds(caixa)` devolve o que ENCOSTA na caixa, e um mob e um quadrado de um tile. Centro a centro, um
/// corpo esta na caixa quando `|dx| &lt; 16*Range + 16` nos dois eixos -- meio lado da caixa mais meio corpo.
///
///   * alcance 0 (a maquina de fabrica): so o proprio tile da maquina, que e DENSO. **Nao pega ninguem** --
///     e do original, e o servidor avisa quem liga uma maquina assim;
///   * alcance 1: quem esta encostado nela;
///   * alcance 2: os oito tiles em volta; alcance 10 (o teto): cinco tiles e meio pra cada lado.
/// ===================================================================================
///
/// ============================ O LIQUIDO: O CAMPO SE MOLDA AO LUGAR (dono, 2026-10-09) ============================
/// *"a area da gravidade deve se adaptar ao local q ta a maquina, tipo, se tiver dentro de uma base 4x4 mas a
/// area da gravidade for 5x5, ela diminui pra 4x4 pra n vazar pra fora do local, e se o local n for um
/// quadrado for um retangulo ou coisa do tipo, a area quadrada da gravidade vai preencher ate o maximo de
/// distancia (no nosso exemplo 5x e 5y) igual um liquido"*.
///
/// DIVERGENCIA DECLARADA (pedido do dono): no DM a caixa atravessa parede -- uma maquina num quarto
/// esmaga quem passa no corredor ao lado. Aqui a caixa e so o TETO: dentro dela o campo ESCORRE da maquina
/// pelos quatro lados, tile a tile, e para em parede (<see cref="Preencher"/>). Um corpo esta no campo
/// quando esta na caixa E num tile que o liquido alcancou.
///
/// PELOS QUATRO LADOS, e nao pelos oito: liquido que passasse na diagonal vazaria pela quina de duas
/// paredes que se encostam so pelo canto -- que e como toda sala e fechada.
/// =================================================================================================================
/// </summary>
public static class CampoDeGravidade
{
	/// <summary>
	/// DEFEITO INJETADO (bancada): o campo so e relido quando alguem mexe na maquina -- o estado de antes de
	/// 2026-10-09, em que entrar andando num campo ligado nao pesava e sair dele nao aliviava. Lido pela
	/// propria linha da releitura, em `GameServer.TickDosCampos`.
	/// </summary>
	public static bool SoMudaNaMaquinaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): o campo atravessa parede -- a caixa crua do DM, sem o liquido. Lido pela
	/// propria linha que pergunta pela parede, no <see cref="Preencher"/>.
	/// </summary>
	public static bool AtravessaParedeDeTeste;

	/// <summary>Do centro do tile da maquina ate onde a caixa alcanca um corpo, em px, por eixo.</summary>
	public static float MeioLado(int alcance) => ZoneCollision.TileSize * (alcance + 1) / 2f;

	/// <summary>Este deslocamento (corpo menos maquina, centro a centro) esta dentro da caixa?</summary>
	public static bool Dentro(float dx, float dy, int alcance)
	{
		float meio = MeioLado(alcance);
		return MathF.Abs(dx) < meio && MathF.Abs(dy) < meio;
	}

	/// <summary>
	/// ATE QUANTOS TILES DA MAQUINA A CAIXA CHEGA A ENCOSTAR, por eixo -- o quadrado em que o liquido pode
	/// escorrer. Um tile a `d` de distancia vai de `32d - 16` a `32d + 16` px, e ha ponto dele dentro da
	/// caixa quando `32d - 16 &lt; 16*alcance + 16`: `d &lt;= (alcance + 1) / 2`, na divisao inteira.
	/// </summary>
	public static int TilesDeRaio(int alcance) => (Math.Max(alcance, 0) + 1) / 2;

	/// <summary>A celula em que uma OBRA esta -- a do <see cref="CatalogoDeObras.Celula"/>, que e a que o servidor bloqueia.</summary>
	public static (int X, int Y) CelulaDaMaquina(double xDaObra, double yDaObra) => CatalogoDeObras.Celula(xDaObra, yDaObra);

	/// <summary>
	/// A celula em que um CORPO esta: a dos PES. A conta e a mesma da obra de proposito -- a obra guarda a
	/// posicao de corpo de quem a ergueu, e o deslocamento dos pes mora dentro do `Celula`.
	/// </summary>
	public static (int X, int Y) CelulaDoCorpo(Vec2 corpo) => CatalogoDeObras.Celula(corpo.X, corpo.Y);

	private static readonly (int X, int Y)[] Lados = [(0, 1), (1, 0), (-1, 0), (0, -1)];

	/// <summary>
	/// ============================ O LIQUIDO ============================
	/// Os tiles que o campo alcanca saindo da maquina sem atravessar parede, dentro do quadrado da caixa.
	/// O tile da maquina entra sempre (ela e a fonte, mesmo sendo densa).
	///
	/// QUEM DIZ O QUE E PAREDE E QUEM CHAMA (<paramref name="parede"/>): parede de mapa, parede e porta
	/// erguidas, porta de mapa. Mobilia NAO e parede -- uma bancada no meio da sala nao represa gravidade.
	/// ===================================================================
	/// </summary>
	public static HashSet<(int X, int Y)> Preencher((int X, int Y) maquina, int alcance, Func<int, int, bool> parede)
	{
		int raio = TilesDeRaio(alcance);
		var cheias = new HashSet<(int X, int Y)> { maquina };
		var fila = new Queue<(int X, int Y)>();
		fila.Enqueue(maquina);

		while (fila.Count > 0)
		{
			(int x, int y) = fila.Dequeue();
			foreach ((int dx, int dy) in Lados)
			{
				(int X, int Y) viz = (x + dx, y + dy);
				if (Math.Abs(viz.X - maquina.X) > raio || Math.Abs(viz.Y - maquina.Y) > raio) continue;
				if (cheias.Contains(viz)) continue;
				if (!AtravessaParedeDeTeste && parede(viz.X, viz.Y)) continue;
				cheias.Add(viz);
				fila.Enqueue(viz);
			}
		}
		return cheias;
	}

	/// <summary>
	/// ESTE CORPO ESTA NO CAMPO DESTA MAQUINA? Na caixa (em pixel, como no original) E num tile que o
	/// liquido alcancou. O ponto do corpo sao os PES (`MoveRules.FeetOffsetY`): e por eles que este port
	/// decide em que tile alguem esta, da colisao a celula da obra.
	/// </summary>
	public static bool Alcanca(double xDaObra, double yDaObra, int alcance, Vec2 corpo, HashSet<(int X, int Y)> cheias)
	{
		(int cx, int cy) = CelulaDaMaquina(xDaObra, yDaObra);
		float mx = (cx + 0.5f) * ZoneCollision.TileSize, my = (cy + 0.5f) * ZoneCollision.TileSize;
		if (!Dentro(corpo.X - mx, corpo.Y + MoveRules.FeetOffsetY - my, alcance)) return false;
		return cheias.Contains(CelulaDoCorpo(corpo));
	}

	/// <summary>
	/// QUANTO A MAQUINA ESPERA ENTRE O PEDIDO E A MUDANCA -- o `sleep(50)` de `Gravity.dm:306`, com o aviso
	/// *"Gravity changing in five seconds"*. E o tempo de quem nao quer ficar na sala sair dela, e o clique
	/// ja DESLIGA o campo antes de perguntar (`:291-302`, *"Gravity temporarily neutralized"*).
	/// </summary>
	public const double SegundosAteMudar = 50 / TempoDoDm.TiquesPorSegundo;

	/// <summary>De quanto em quanto se rele QUEM esta no campo: a volta do laco de stats (`Grav()` mora nele).</summary>
	public const double SegundosEntreLeituras = TempoDoDm.SegundosDoLacoStats;

	/// <summary>
	/// De quanto em quanto o LIQUIDO e refeito numa maquina ligada. Parede se ergue e cai com o campo de pe
	/// (a base cresce, alguem abre um buraco no soco), e o campo acompanha em ate um segundo.
	/// </summary>
	public const double SegundosEntreEnchentes = 1.0;
}
