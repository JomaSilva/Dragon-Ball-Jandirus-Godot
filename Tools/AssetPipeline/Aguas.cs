namespace Jandirus.Tools;

/// <summary>
/// QUAL CELULA DO `.dmm` E AGUA -- a leitura do original, e o unico lugar que a faz.
///
/// ============================ POR QUE NAO BASTA `Water == 1` ============================
/// A flag `Water` do original (`Turfs.dm:41`) esta ligada em 38 typepaths, e QUATRO deles nao sao
/// agua: `SkyHD` e `SkyHD2` (em `/turf/HDTurfs` e de novo em `/turf/build`) sao as NUVENS do Ceu e
/// do Alem, e elas carregam a flag de carona. Da pra provar pelo `Enter()`, que e onde a regra
/// mora de verdade:
///
///     agua   (`NewTurfs.dm:82-84`)   ->  return testWaters(O)      // voa, NADA, barco, arremesso
///     ceu    (`NewTurfs.dm:194-196`) ->  if(M.isflying|!M.density) // SO voa -- nadar nao entra
///
/// Sao 295.251 celulas nos 26 andares -- quase tudo z6 (o Alem) e z10 (o Ceu). Varrer "todo turf
/// com `Water=1`" transformaria o ceu inteiro num oceano onde da pra nadar, e o Alem e um lugar
/// por onde os mortos ANDAM.
///
/// O CEU NAO GANHA CLASSE NESTE PASSE. Ele e uma QUARTA classe ("so quem voa") e o dono nao pediu
/// isso -- ele falou de agua. As celulas de ceu continuam exatamente como estao hoje: chao comum,
/// que se atravessa a pe. Fica anotado como divida, nao como bug novo.
///
/// A LAVA E AGUA, e isso nao e engano: `LavaHD` (`NewTurfs.dm:108-123`) e `/turf/Other/Lava`
/// chamam o `testWaters()` como qualquer lago -- nao se anda por cima, voa-se e nada-se por cima.
/// O `fire=1` que causa dano e OUTRO sistema, e nao esta sendo portado aqui.
/// =======================================================================================
/// </summary>
public static class Aguas
{
	/// <summary>
	/// Este typepath e uma celula da classe AGUA?
	///
	/// <paramref name="td"/> ja vem com a heranca resolvida pelo <see cref="DmTurfScanner"/> --
	/// e ela e essencial: `/turf/Water` liga a flag e os 15 filhos so trocam o icone.
	/// </summary>
	public static bool Eh(string basePath, TurfDef td)
	{
		if (!td.Water) return false;
		return !EhCeu(basePath);
	}

	/// <summary>
	/// O CEU DISFARCADO DE AGUA. Pelo NOME do tipo e nao por uma lista de quatro caminhos: os
	/// quatro sao `SkyHD` e `SkyHD2` declarados em duas arvores (`/turf/HDTurfs` e `/turf/build`),
	/// e um quinto que aparecesse com o mesmo prefixo teria a mesma cara e o mesmo `Enter()`.
	/// </summary>
	public static bool EhCeu(string basePath)
	{
		string curto = basePath[(basePath.LastIndexOf('/') + 1)..];
		return curto.StartsWith("Sky", StringComparison.Ordinal);
	}
}
