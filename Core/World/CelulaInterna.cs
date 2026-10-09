namespace Jandirus.Core.World;

/// <summary>
/// A CELULA SOB TETO -- a area `Inside` do original, e o que ela tira de quem esta nela.
///
/// ============================ NO DM A NOITE E O CLIMA SAO O MESMO DESENHO ============================
/// Toda area do BYOND carrega um icone (`icon='Weather.dmi'`, `Areas.dm:34`) num plano acima de chao,
/// objeto e mob (`plane = WEATHER_LAYER`, `Areas.dm:60`; o valor e 10, `Overlays.dm:11`), e o motor o
/// desenha em CADA turf dela. O `area/Ticker()` so troca o estado desse icone (`Weather.dm:84-129`):
///
///     com clima   "Rain", "Night Rain", "Fog", "Blizzard"...
///     sem clima   "Dark" (noite), "Sunset", "Sunrise", ou "" de dia
///
/// Nao ha outra fonte de escuro no original: nenhum `client.color`, nenhum `luminosity`, nenhum plane
/// master, e `TurfOnNew.dm:1` diz que o sistema de luz nao e compilado. Medido no `.dmi`: "Dark" e uma
/// chapa (35,35,35) com alfa 125/255 cobrindo o tile inteiro, e "Night Rain" e a mesma chapa com os
/// riscos por cima.
///
/// A AREA INTERNA (`/area/X/Inside`, `Areas.dm:134-137`) NUNCA RECEBE ESTADO NENHUM:
///   * ela fica fora da lista de quem tem ceu (`Areas.dm:98-102`), e so essa lista liga o clima
///     (`Areas.dm:100`, `WorldClock.dm:110`) e acerta a hora (`Weather.dm:156`);
///   * e o `Ticker()` ainda zera `daylightcycle`, `mooncycle`, `HasWeather` e `HasMoon` a cada volta,
///     ANTES de escolher o estado (`Weather.dm:76-80`).
/// O estado fica "", que nao existe em nenhum dos dois icones, e nada e desenhado: nem particula, nem
/// escuro, nem amanhecer. O `IndoorsWeather.dmi` ate tem "Dark", "Sunrise" e "Sunset" (identicos aos
/// de fora, medido), mas nenhum caminho do codigo chega neles.
/// ======================================================================================================
///
/// ============================ O QUE NASCE INTERNO ============================
/// Tres fontes, e o conversor (`Tools/AssetPipeline/Interiores.cs`) junta as tres no `.dentro`:
///   1. o que o `.dmm` pinta com uma area `.../Inside` (20 andares; na Terra, 1.164 celulas);
///   2. todo `/turf/build/*` do mapa, que se muda pra area interna do planeta um tique depois do
///      boot (`buildturfs.dm:11-22`);
///   3. as 854 celulas da cidade de Vegeta, que o boot carimba e marca (`VegetaCity.dm:42`).
/// O predio entra INTEIRO: o Banco da Terra e um retangulo 12x6 com o anel de telhado, a fileira de
/// parede, o piso e a porta, e os quatro sao internos do mesmo jeito.
/// ===========================================================================
///
/// ============================ E O QUE DEIXA DE SER ============================
/// Celula interna DESTRUIDA volta pro lado de fora: `turf/proc/Destroy()` poe o chao novo na area
/// externa do planeta (`NewTurfs.dm:13-17`). Por isso a pergunta do jogo recebe um `caiu`, e quem o
/// responde e a lista de estrago que cada ponta ja guarda (`_cenarioCaido` no servidor,
/// `CenarioCaido` no cliente). Nao ha estado novo pra sair de sincronia.
/// ==============================================================================
///
/// ============================ DIVERGENCIAS DECLARADAS ============================
///   * A UNIDADE E A CELULA DOS PES, e nao o tile do mob: este port anda em pixel, e "onde o corpo
///     esta" ja e a caixa dos pes pra colisao e pra agua (<see cref="ClasseDeCorpo.Pes"/>).
///   * TILE ERGUIDO POR JOGADOR nasce interno no DM (`buildturfs.dm:4-22`). Aqui ele entra por uma
///     camada de runtime do mapa (<see cref="ZoneCollision.Coberta"/>), e nao pelo `.dentro`: o
///     arquivo e do que o mapa trouxe. Quem cobre e descobre e a construcao de base (`Blocos`).
///   * DUAS CELULAS de Hera tem um `/turf/build` POR BAIXO de outro turf na mesma chave do `.dmm`.
///     Nao da pra saber, lendo, se o `New()` do de baixo roda; elas ficaram de fora.
///   * A LUZ E SO DE DENTRO. No DM a celula interna nunca escurece, pra ninguem. Aqui ela fica no
///     tom de dia enquanto o corpo LOCAL esta sob teto; vista da rua, escurece com a rua. Escolha do
///     dono por foto (2026-10-08): a noite daqui e bem mais escura que a de la, e o predio claro
///     visto de fora virava um retangulo aceso no breu. Quem faz e o cliente
///     (`Client/Iluminacao.cs`, `ALuzDeDentro`).
///   * OS ASTROS DO MAKYO NAO PERGUNTAM O TETO. No DM o bonus de Sol/Lua le a hora do mob, que
///     dentro de casa vale 0 e CONGELA o ultimo valor (`makyo.dm:78` e `:121`, um `switch` sem ramo
///     pro zero), e o Above All perde o x5 de treino (`:162-165`). Aqui eles seguem o ceu da zona,
///     dentro e fora. Decisao do dono (2026-10-08): e balanco de raca, fica como esta.
/// =================================================================================
/// </summary>
public static class CelulaInterna
{
	/// <summary>
	/// DEFEITO INJETADO (bancada): o plano do `.dentro` nao vale -- nenhuma celula esta sob teto.
	/// E o mundo de ANTES do conserto, no mesmo binario: volta a nevar dentro do Banco e a lua volta
	/// a nascer la. Falso em jogo, sempre.
	/// </summary>
	public static bool SemPlanoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): a celula que caiu continua interna. E o que aconteceria se alguem
	/// lesse o bit cru do mapa (<see cref="ZoneCollision.NasceuDentro"/>) no lugar desta classe.
	/// Falso em jogo, sempre.
	/// </summary>
	public static bool CaidaContinuaInternaDeTeste;

	/// <summary>
	/// ESTA CELULA ESTA SOB TETO AGORA? -- o plano do mapa, menos o que caiu, mais o que alguem ergueu.
	///
	/// O ERGUIDO NAO PERGUNTA O `caiu`: a lista de estrago fala do CENARIO, e um piso assentado em cima
	/// de chao rachado esta de pe. Quando ele e demolido, sai da camada (`ZoneCollision.Descobrir`).
	/// </summary>
	/// <param name="caiu">A celula do cenario foi destruida? Vem da lista de estrago de quem pergunta.</param>
	public static bool SobTeto(ZoneCollision? mapa, int cx, int cy, bool caiu) =>
		!SemPlanoDeTeste && mapa != null
		&& (mapa.Coberta(cx, cy)
			|| (mapa.NasceuDentro(cx, cy) && (!caiu || CaidaContinuaInternaDeTeste)));

	/// <summary>
	/// A CELULA EM QUE UM CORPO ESTA, pra esta pergunta: a dos PES. No DM o mob tem um tile
	/// (`current_area = GetArea()`, `Stats.dm:158`); aqui o equivalente e o ponto que a colisao ja
	/// garante fora de parede. Ver a primeira divergencia do cabecalho.
	/// </summary>
	public static (int Cx, int Cy) CelulaDoCorpo(Vec2 centro)
	{
		Vec2 pes = ClasseDeCorpo.Pes(centro);
		return ((int)MathF.Floor(pes.X / ZoneCollision.TileSize),
				(int)MathF.Floor(pes.Y / ZoneCollision.TileSize));
	}

	/// <summary>
	/// ESTE CLIMA FICA DE FORA DO INTERIOR? Todos, menos a DESTRUICAO.
	///
	/// A morte do planeta e a unica coisa que o DM desenha dentro de casa: o `DestroyPlanet` roda
	/// pra TODA area do planeta, interna inclusive (`Area_Death.dm:81-95`), e "Rising Rocks" e o
	/// unico estado de clima que o `IndoorsWeather.dmi` tem. O laco dela tambem alcanca quem esta
	/// dentro (`Area_Death.dm:97-98` so pula quem saiu da area ou morreu), entao teto nenhum salva
	/// de um mundo acabando.
	/// </summary>
	public static bool TiraOClima(TipoDeClima t) => t != TipoDeClima.Destruicao;
}
