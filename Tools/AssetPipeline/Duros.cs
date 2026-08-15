namespace Jandirus.Tools;

/// <summary>
/// QUAL CELULA DO `.dmm` NAO SE QUEBRA -- a leitura do original, e o unico lugar que a faz.
///
/// ============================ A QUEIXA QUE A TROUXE ============================
/// *"em VARIOS MAPAS PRE-FEITOS tem VARIOS TILES INVISIVEIS COM COLISAO... ai quando eu soco ele
/// QUEBRA e faz TODOS OS EFEITOS mas N TINHA NADA LA, so colisao"*.
///
/// O censo mediu e a hipotese obvia estava errada: nao ha mesa muda nenhuma sobre piso pintado --
/// **zero** celulas nessa forma nos 40 andares. Todo bloqueador de todo mapa ou desenha, ou e
/// `/turf/Other/Blank`, que **nao tem arte pra recuperar**: ele nao declara `icon` no DM e no BYOND
/// o jogador esbarra exatamente nas mesmas celulas. Nao e buraco de ARTE.
///
/// O que o port fazia de diferente era DESTRUIR. No original o vazio nao cai, porque
/// `turf/proc/Destroy()` (`Modules/Turfs/NewTurfs.dm:2-4`) abre com `if(src.destroyable)` e o Blank
/// declara `destroyable = 0` (`Turfs.dm:72`). Aqui o campo nunca foi extraido: ele era aproximado
/// pelo anel de 2 celulas do `ZoneCollision.NaBorda`, e as costuras do Templo ficam a centenas de
/// tiles da beirada -- 1.690 das 1.702 alcancaveis passavam pela guarda e caiam.
/// ==============================================================================
///
/// ============================ O SOM E A POEIRA CONTINUAM ============================
/// E de proposito, e e o original: em `attack_proc.dm:99-105` os sons de soco e o `createDust` vem
/// ANTES do `T.Destroy()`. Bater numa parede que aguenta faz barulho nos dois. O que muda e o
/// desfecho -- a celula nao vira terra batida e a colisao nao abre.
/// ===================================================================================
/// </summary>
public static class Duros
{
	/// <summary>
	/// ESTE TURF E INDESTRUTIVEL?
	///
	/// <paramref name="td"/> ja vem com a heranca resolvida pelo <see cref="DmTurfScanner"/> -- e ela
	/// e essencial aqui: `turf/Teleporters` declara `destroyable = 0` uma vez e mais de vinte filhos
	/// so trocam o icone.
	/// </summary>
	public static bool Eh(TurfDef td) => !td.Destroyable;

	/// <summary>
	/// ESTE OBJ SOBREVIVE A QUEDA DO CHAO DEBAIXO DELE?
	///
	/// ============================ POR QUE OBJ ENTRA NUMA REGRA DE TURF ============================
	/// Porque no original `Destroy()` e um proc de TURF: derrubar o chao sob uma `/obj/barrier` troca
	/// o turf por `/turf/Ground/Ground8` e **a barreira continua la**, bloqueando pelo
	/// `selectivecollide` (`barrier.dm:17-23`), que nao depende de turf nenhum.
	///
	/// No port a barreira nao e entidade: ela virou celula solida do `.col` (ver `CensoMudoBench.Travas`,
	/// que a conta como bloqueador). Entao derrubar a celula apagava a barreira inteira -- um soco
	/// abria de vez o que no original nao abre nunca. O caso que doi e o `obj/barrier/kaio_gate`
	/// (`barrier.dm:63-72`): ele e `fragile = 0`, tem `icon = null` de proposito (INVISIVEL) e e o
	/// unico tranco entre um morto e o Sr. Kaioh.
	/// ==============================================================================================
	/// </summary>
	public static bool EhObjDuro(string basePath) =>
		basePath.StartsWith("/obj/barrier/", StringComparison.Ordinal)
		|| basePath.Equals("/obj/barrier", StringComparison.Ordinal);
}
