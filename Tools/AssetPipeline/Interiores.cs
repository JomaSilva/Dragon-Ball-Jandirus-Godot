namespace Jandirus.Tools;

/// <summary>
/// QUAL CELULA DO `.dmm` NASCE SOB TETO -- a leitura do original, e o unico lugar que a faz.
///
/// O que "sob teto" tira de uma celula (o clima, a noite, a lua) esta escrito em
/// `Core/World/CelulaInterna.cs`, com as linhas do DM. Aqui mora so a pergunta do CONVERSOR: de
/// onde, no mapa, sai o bit.
///
/// ============================ TRES FONTES, E SO A PRIMEIRA ESTA PINTADA ============================
///   1. A AREA. O `.dmm` pinta o predio com `/area/Earth/Inside`, `/area/Namek/Inside`... -- toda
///      area de planeta tem a filha (`Areas.dm:125-415`), e o nome do ultimo segmento e o que a
///      identifica. Sao 20 andares e a quase totalidade das celulas.
///   2. O TURF ERGUIDO. Todo `/turf/build/*` chama `addtoinside()` no `New()` e se muda pra area
///      interna do MESMO planeta um tique depois do boot (`buildturfs.dm:4-22`) -- inclusive os que o
///      mapeador pos a mao num lugar pintado como externo. Medido em 2026-10-08: 36 celulas em
///      Arconia, 101 em Hera e 9 no Templo (cuja area tem `Planet = "Earth"` e por isso cai na
///      interna da Terra).
///   3. A CIDADE DE VEGETA, que nem esta no `.dmm`: o `veg_stamp` marca cada celula que carimba
///      (`VegetaCity.dm:42`). Quem a conhece aqui e o <see cref="CidadeDeVegeta.Planta"/>.
/// ===================================================================================================
///
/// ============================ A CONDICAO DO TURF ERGUIDO, E O QUE FOI ASSUMIDO ============================
/// O `addtoinside()` so muda o turf se existir uma area interna do mesmo planeta na
/// `area_inside_list` -- ou seja, instanciada em algum andar e sem `exclude`. Conferir isso pediria
/// extrair `Planet` e `exclude` de cada area do `Areas.dm`, pra tres areas. Em vez disso a condicao
/// foi conferida a mao pras tres (Arconia/Inside, Hera/Inside e Earth/Inside existem no mapa e
/// nenhuma e `exclude`), e a bancada `dentro-prova` crava as tres contagens: turf erguido novo em
/// outra area deixa a bancada vermelha, e quem ler vem conferir a condicao pra area nova.
///
/// VALE O ULTIMO TURF DA CHAVE, a mesma regra da agua e do duro. Duas celulas de Hera tem um
/// `/turf/build` POR BAIXO de outro turf (`/turf/build/GrassHD1,/turf/decor/Plant16`), e lendo o
/// mapa nao da pra saber se o `New()` do de baixo roda; ficaram de fora, e isso esta declarado no
/// cabecalho do `CelulaInterna`.
/// ==========================================================================================================
/// </summary>
public static class Interiores
{
	/// <summary>
	/// Esta area do DM e interna? Pelo ULTIMO segmento do typepath, e nao por uma lista: toda area
	/// de planeta de `Areas.dm` tem a filha `Inside` com o mesmo corpo, e uma nova teria a mesma cara.
	/// </summary>
	public static bool EhAreaInterna(string areaBasePath) =>
		areaBasePath.EndsWith("/Inside", StringComparison.Ordinal);

	/// <summary>Este turf e dos que se mudam pra area interna no boot? Ver a fonte 2 do cabecalho.</summary>
	public static bool EhTurfErguido(string turfBasePath) =>
		turfBasePath.StartsWith("/turf/build/", StringComparison.Ordinal);
}
