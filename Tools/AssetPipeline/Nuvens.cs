namespace Jandirus.Tools;

/// <summary>
/// QUAL CELULA DO `.dmm` E NUVEM -- a quarta classe de celula, do lado do conversor.
///
/// ============================ ELE NAO TEM REGRA PROPRIA, E ESSE E O PONTO ============================
/// A leitura de "isto e ceu" ja existia e ja estava em producao: <see cref="Aguas.EhCeu"/>, escrita
/// quando a agua virou classe justamente pra TIRAR o ceu de lá. Ela le o nome curto do typepath e
/// pergunta se ele comeca com `Sky` -- e essa forma foi escolhida na epoca porque os quatro tipos
/// (`SkyHD` e `SkyHD2`, declarados em `/turf/HDTurfs` e de novo em `/turf/build`) tem o mesmo
/// `Enter()` e um quinto com o mesmo prefixo teria a mesma cara.
///
/// **Escrever uma segunda lista aqui seria o erro classico deste projeto**: duas derivacoes de "o que
/// e ceu" que concordam no dia em que sao escritas e divergem na primeira vez que alguem toca numa
/// so. E a divergencia seria calada nos dois sentidos -- uma celula que a agua exclui e o ceu nao
/// inclui vira chao comum (o bug de hoje), e uma que o ceu inclui e a agua nao exclui vira lago.
///
/// Entao esta classe existe pra dar NOME e LUGAR a pergunta do lado do ceu, e a resposta e delegada.
/// ==========================================================================================================
///
/// ============================ POR QUE A FLAG `Water` NAO SERVE DE CRIVO ============================
/// Ela mente nos DOIS sentidos, medido:
///
/// <code>
///   /turf/HDTurfs/SkyHD    Water = 1   e NAO e agua  (o `Enter()` so deixa voar)
///   /turf/HDTurfs/SkyHD2   Water = 1   e NAO e agua
///   /turf/Other/Sky1       (sem flag)  e E ceu       -- 207.915 celulas no Templo
/// </code>
///
/// O terceiro e o que importa pra esta tarefa: e o ceu do Lookout, que o dono citou pelo nome, e ele
/// escaparia inteiro de um crivo por flag.
/// ==================================================================================================
/// </summary>
public static class Nuvens
{
	/// <summary>
	/// Este typepath e uma celula da classe NUVEM?
	///
	/// <paramref name="td"/> entra na assinatura por SIMETRIA com o <see cref="Aguas.Eh"/> -- os dois
	/// sao chamados lado a lado pelo `MapConverter`, com os mesmos dois argumentos -- e hoje ele nao e
	/// consultado: o ceu se decide pelo NOME (ver o cabecalho). Deixa-lo de fora faria a chamada dos
	/// dois ficar diferente sem que nada na regra fosse diferente, e o dia em que o ceu precisar olhar
	/// uma propriedade herdada (um `destroyable` que alguem desligue, por exemplo) o parametro ja esta
	/// aqui em vez de mudar a assinatura e os dois chamadores junto.
	/// </summary>
	public static bool Eh(string basePath, TurfDef td) => Aguas.EhCeu(basePath);
}
