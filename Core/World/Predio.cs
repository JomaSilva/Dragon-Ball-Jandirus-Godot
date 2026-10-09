namespace Jandirus.Core.World;

/// <summary>
/// A PAREDE DE PREDIO -- a unica coisa do mapa que para quem voa por cima do cenario.
///
/// ============================ O PEDIDO, E O QUE ELE MUDA ============================
/// Dono, 2026-10-08: "voar por cima de parede de base de player n deveria ser possivel".
///
/// Ate aqui a altura era tudo-ou-nada: acima de <see cref="Voo.AlturaQueAtravessa"/> o chamador
/// trocava o mapa por `null` e o passo nao perguntava mais nada a geometria -- montanha, arvore,
/// muro e CASA passavam por baixo. Dava pra pairar ate o meio do Banco e pousar la dentro.
///
/// Agora quem voa acima do cenario pergunta ao mapa com um modo proprio
/// (<see cref="ModoDeTravessia.PorCima"/>), e a resposta e esta classe: o que nao e predio continua
/// passando por baixo, e a parede de predio -- a celula que BARRA e esta SOB TETO -- para o corpo
/// em qualquer altura. A parede de uma casa vai ate o telhado dela.
/// ====================================================================================
///
/// ============================ NO DM ============================
/// La voar nao mexe em densidade (`flying.dm:107-114` so liga `flight` e `isflying`; e o proprio
/// `movement handler.dm:51` anota: "if(flight) density = 0 causes all sorts of wonky stuff, just
/// add special calls in the bump proc for walls"). Quem decide e o `Enter()` de cada turf:
///
///   * BARRAM quem voa: o telhado (`turf/Roof`, `Turfs.dm:1006-1014` -- so passaria com
///     `FlyOverAble`, que so e declarado, na linha 47, e nunca ligado; idem `NamekRoof` :723-732 e
///     `TempleRoof` :1208-1217) e a parede de castelo (`CastleWall`, `Turfs.dm:452-456`: "walls
///     block walking AND flight (was: flyers passed straight over them)" -- a mesma decisao do
///     dono, tomada antes no original);
///   * DEIXAM passar: a parede erguida por jogador (`turf/build/wall`, `buildturfs.dm:37-45`:
///     `if(M.flight) return 1`) e a `NamekWall` (`Turfs.dm:714-722`).
///
/// Onde o predio e fechado por telhado ou por parede de castelo, "sob teto" da o mesmo desfecho do
/// DM. O que MUDA em relacao a ele e o que o dono pediu: a parede de base de jogador, que la se
/// atravessava voando, aqui barra.
/// ===============================================================
///
/// ============================ DIVERGENCIAS DECLARADAS ============================
///   * E POR CELULA SOB TETO (<see cref="CelulaInterna"/>), e nao por tipo de turf: o conversor nao
///     guarda o `Enter()` de cada tile, e o plano do que e interno ja existe. A fachada de uma casa
///     de Namek (`NamekWall`), que no DM se atravessava voando pra dentro, aqui barra como o resto
///     do predio.
///   * A CAVERNA E INTERNA INTEIRA, entao la dentro nao se voa por cima da rocha -- que e o que o
///     DM faz com toda parede sem `Enter()` proprio, e o que "teto" quer dizer.
///   * A MOBILIA SOB TETO BARRA JUNTO (o `BlockedCell` soma a construcao densa): dentro de casa quem
///     voa esbarra no que quem anda esbarra.
///   * O PORT AINDA NAO ERGUE TILE (so objeto de uma celula): a regra vale hoje pros predios do
///     mapa. O tile erguido por jogador nasce interno no DM (`buildable.dm:447-452`) -- quando ele
///     existir aqui, entra no mesmo plano e herda esta regra sem uma linha a mais.
///   * SO O CORPO: o que voa, o que e arremessado no ar e o que um feixe arrasta. O ki disparado do
///     alto continua passando por cima do predio (`GameServer.Projeteis`, passo 6a) -- o pedido foi
///     voar.
/// =================================================================================
/// </summary>
public static class ClasseDePredio
{
	/// <summary>
	/// DEFEITO INJETADO (bancada): parede de predio nao para quem voa -- acima do cenario o corpo
	/// atravessa tudo, como antes de 2026-10-08. Falso em jogo, sempre.
	/// </summary>
	public static bool QuemVoaAtravessaDeTeste;

	/// <summary>
	/// ESTA CELULA PARA QUEM VOA ACIMA DO CENARIO? -- a que barra E esta sob teto.
	///
	/// `caiu: false` e o certo, e nao um atalho: a celula que caiu foi ABERTA (`ZoneCollision.Abrir`),
	/// entao o <see cref="ZoneCollision.BlockedCell"/> ja a tira daqui. A porta aberta sai pelo mesmo
	/// caminho -- quem voa passa pelo vao como quem anda.
	/// </summary>
	public static bool BarraQuemVoa(ZoneCollision mapa, int cx, int cy)
		=> !QuemVoaAtravessaDeTeste && CelulaInterna.SobTeto(mapa, cx, cy, caiu: false) && mapa.BlockedCell(cx, cy);
}
