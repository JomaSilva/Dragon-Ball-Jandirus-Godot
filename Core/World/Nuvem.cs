namespace Jandirus.Core.World;

/// <summary>
/// O QUE ACONTECE COM ESTE CORPO NESTA NUVEM -- as tres respostas possiveis, e sao TRES mesmo.
///
/// A agua so tem duas (<see cref="ClasseDeAgua.Bloqueia"/> devolve `bool`) porque ela so PARA. A
/// nuvem tambem DERRUBA, e nao da pra espremer isso num booleano sem perder a diferenca entre "voce
/// nao entra aqui" e "voce entra e cai em outro mapa" -- que sao, literalmente, os dois `Enter()`
/// distintos do original (ver <see cref="ClasseDeNuvem"/>).
/// </summary>
public enum TravessiaDaNuvem : byte
{
	/// <summary>Passa por cima. So quem voa -- o `M.isflying` do DM.</summary>
	Atravessa = 0,

	/// <summary>Nao entra. As nuvens do Ceu e do Reino dos Deuses (`SkyHD`, `Sky1` de fora do Templo).</summary>
	Bloqueia = 1,

	/// <summary>Entra e CAI num outro mapa. A nuvem do Caminho da Serpente (`SkyHD2`) e a do Templo.</summary>
	Derruba = 2,
}

/// <summary>
/// A NUVEM COMO **QUARTA CLASSE DE CELULA** -- e a divida que a terceira deixou escrita.
///
/// ============================ ELA JA ESTAVA ANOTADA, COM ESTAS PALAVRAS ============================
/// Quando a agua virou classe, o passe registrou por que o ceu ficava de fora (`Tools/AssetPipeline/
/// Aguas.cs`): *"O CEU NAO GANHA CLASSE NESTE PASSE. Ele e uma QUARTA classe ('so quem voa') e o dono
/// nao pediu isso -- ele falou de agua. As celulas de ceu continuam exatamente como estao hoje: chao
/// comum, que se atravessa a pe. Fica anotado como divida, nao como bug novo."*
///
/// O dono pediu agora: *"as NUVENS q tem no CAMINHO DA SERPENTE no outro mundo, se um jogador ir
/// nelas SEM ESTAR COM FLY ATIVADO, ele vai automaticamente ser JOGADO NO MAPA DO INFERNO. e no mapa
/// do LOOKOUT se o jogador cair na nuvem do mapa sem fly, ele CAI DE VOLTA PRA TERRA"*.
///
/// Este arquivo e a divida paga, e ele e o IRMAO do `Agua.cs` de proposito -- mesmo desenho (um plano
/// de bits proprio, uma classe com as respostas juntas, um <see cref="ModoDeTravessia"/> de entrada),
/// pra que quem souber ler um saiba ler o outro.
/// ====================================================================================================
///
/// ============================ POR QUE "NUVEM" E NAO "CEU" ============================
/// Porque `Ceu` **ja existe neste namespace** e quer dizer outra coisa: o ciclo dia/noite, as fases da
/// lua e o relogio de cada planeta (`Core/World/Ceu.cs`). Duas ideias com o mesmo nome no mesmo lugar
/// e o comeco de um `Ceu.SegundosPorDia` sendo lido por quem procurava colisao. "Nuvem" tambem e a
/// palavra que o dono usou.
///
/// O UNICO LUGAR QUE CONTINUA DIZENDO "ceu" e a leitura do typepath no conversor
/// (`Aguas.EhCeu`), e de proposito: la o nome descreve o que esta escrito no `.dmm` (`SkyHD`, `Sky1`),
/// e renomear a funcao que ja estava em producao so pra combinar com esta seria mexer no que funciona.
/// =====================================================================================
///
/// ============================ QUANTAS CELULAS SAO, E A CONTA ANTIGA ESTAVA CURTA ============================
/// Contadas celula a celula nos `.dmm` (o ULTIMO turf de cada entrada, que e a regra que o
/// `MapConverter` ja usa):
///
/// <code>
///   z06 Afterlife  /turf/HDTurfs/SkyHD2   214.473   DERRUBA -> Inferno
///   z10 Heaven     /turf/HDTurfs/SkyHD     75.659   bloqueia
///   z12 Lookout    /turf/Other/Sky1       207.915   DERRUBA -> Terra   (pedido do dono; ver abaixo)
///   z30 Outside    /turf/Other/Sky1           672   bloqueia
///   z31 God_Realm  /turf/HDTurfs/SkyHD        322   bloqueia
///                                        ---------
///                                          499.041
/// </code>
///
/// A anotacao da agua dizia **295.251**. A diferenca nao e erro de contagem: aquela conta perguntava
/// *"quem tem `Water = 1` e nao e agua?"*, e o `Sky1` do Templo **nao tem a flag** -- ele nunca
/// apareceu naquela varredura. E justamente a nuvem que o dono citou pelo nome.
///
/// ============================ E POR ISSO A FLAG `Water` NAO PODE SER O CRIVO ============================
/// Ela mente **nos dois sentidos**: o `SkyHD`/`SkyHD2` a carregam de carona (e nao sao agua) e o
/// `Sky1` nao a carrega (e e nuvem). O que separa e o `Enter()`, e a leitura ja esta feita e em uso --
/// `Tools/AssetPipeline/Aguas.cs`, `Aguas.EhCeu`, que hoje serve pra EXCLUIR a nuvem da agua e passa
/// a servir tambem pra INCLUI-LA aqui. Uma leitura so, dois consumidores, sem uma segunda lista pra
/// envelhecer.
/// ==========================================================================================================
/// </summary>
public static class ClasseDeNuvem
{
	/// <summary>
	/// O QUE ESTA NUVEM FAZ COM ESTE CORPO.
	///
	/// ============================ A REGRA DA NUVEM NAO E A DA AGUA, E A DIFERENCA E MEDIDA ============================
	/// Parece a mesma e nao e. Os dois `Enter()`, lado a lado no original:
	///
	/// <code>
	///   agua   (`NewTurfs.dm:82-84`)    return testWaters(O)        // flight | swim | KB | boat
	///   nuvem  (`NewTurfs.dm:194-196`)  if(M.isflying|!M.density)   // SO flight
	/// </code>
	///
	/// Ou seja: **quem NADA nao entra na nuvem, e quem esta sendo ARREMESSADO tambem nao.** A agua
	/// deixa os dois passarem. Copiar `ClasseDeAgua.Bloqueia` pra ca -- a tentacao obvia, e sao duas
	/// linhas identicas na tela -- daria nuvem atravessavel a nado, que e o oposto de uma nuvem.
	///
	/// O `Arremessado` merece uma palavra porque a intuicao briga com o DM: um corpo voando de um soco
	/// ESTA no ar, e mesmo assim o `Enter()` da nuvem o recusa (o `M.KB` nao aparece na condicao). Nas
	/// nuvens que derrubam isso vira o desfecho certo pelo outro lado -- o arremessado cai no Inferno,
	/// que e a cena que o original produz e que o Caminho da Serpente existe pra produzir.
	///
	/// ============================ O `!M.density` NAO FOI PORTADO ============================
	/// O original tem um segundo portao: um mob SEM densidade tambem passa. Neste port nao ha corpo
	/// sem densidade -- nao ha fantasma, nao ha incorporeo, e o morto do Alem anda com colisao igual a
	/// de todo mundo (ver `Alem.MortoDePe`). Inventar um equivalente seria inventar um estado que o
	/// jogo nao tem. **Divergencia declarada**, e ela nao muda desfecho nenhum hoje.
	/// ==============================================================================================================
	/// </summary>
	/// <param name="zonaDerruba">
	/// Esta ZONA tem destino de queda? Ver <see cref="DestinoDaQueda"/> -- a pergunta e da zona e nao
	/// da celula, e o porque esta no cabecalho de la.
	/// </param>
	public static TravessiaDaNuvem Travessia(ModoDeTravessia modo, bool zonaDerruba)
	{
		// O UNICO QUE PASSA. Ver o quadro acima: `isflying` e a condicao inteira do `Enter()` da nuvem.
		if (modo == ModoDeTravessia.Voando) return TravessiaDaNuvem.Atravessa;
		return zonaDerruba ? TravessiaDaNuvem.Derruba : TravessiaDaNuvem.Bloqueia;
	}

	/// <summary>
	/// ESTA CELULA PARA ESTE CORPO? -- o mesmo formato do <see cref="ClasseDeAgua.Bloqueia"/>, pra o
	/// <see cref="ZoneCollision.Bloqueia"/> poder perguntar as duas coisas na mesma linha.
	///
	/// **NUMA NUVEM QUE DERRUBA A RESPOSTA E `false`, E ISSO E O PONTO.** Quem cai tem que ENTRAR na
	/// celula: e a entrada que dispara a queda (no DM, literalmente, o corpo do `Enter()`). Uma nuvem
	/// que bloqueasse E derrubasse nunca derrubaria nada -- o corpo pararia na beirada e o servidor
	/// nunca veria o pe dele em cima dela.
	/// </summary>
	public static bool Bloqueia(ModoDeTravessia modo, bool zonaDerruba) =>
		Travessia(modo, zonaDerruba) == TravessiaDaNuvem.Bloqueia;

	/// <summary>
	/// DA PRA NASCER / POUSAR NUMA NUVEM? Nao -- nem nas que bloqueiam nem nas que derrubam.
	///
	/// Nas que bloqueiam e o mesmo motivo da agua (<see cref="ClasseDeAgua.ServeDeChao"/>): o corpo
	/// ficaria livre pela colisao e parado pela regra, ou seja PRESO. Nas que derrubam e pior e mais
	/// engracado: o corpo pousaria e seria cuspido pro Inferno no tique seguinte, sem ter feito nada.
	///
	/// E o pedido do dono cobre exatamente isto: *"Quem cai pela nuvem nao pode ficar preso nem cair
	/// dentro de parede -- use o funil de pouso que ja existe"*. O funil e o
	/// <see cref="ZoneCollision.PontoLivrePerto"/>, e ele so sabe da nuvem porque este campo existe.
	/// </summary>
	public const bool ServeDeChao = false;

	/// <summary>
	/// DA PRA SOCAR UMA NUVEM? Nao -- e nem precisou de regra nova.
	///
	/// Os tres turfs declaram `destroyable = 0` (`NewTurfs.dm:191`, `:201` e `Turfs.dm:80`), entao as
	/// 499 mil celulas ja entraram no `.duro` na passada do `Duros` e o punho ja nao as alcanca.
	/// Medido nos planos gravados: o `.duro` do z6 tem 214.872 celulas contra as 214.473 de nuvem, e o
	/// do z12 tem 223.349 contra 207.915. Este campo existe pra dizer que a coincidencia foi CONFERIDA
	/// e nao presumida.
	/// </summary>
	public const bool Destrutivel = false;

	/// <summary>
	/// A NUVEM ESCONDE QUEM ESTA ATRAS? Nao -- os tres turfs tem `density = 0`, entao nenhum entrou no
	/// `.vis` (que so recebe celula densa). Mesma nota do <see cref="ClasseDeAgua.Cega"/>, e ela existe
	/// pelo mesmo motivo: pra que "resolver" a nuvem marcando densidade tenha um lugar dizendo que
	/// aquilo poe leque preto de muro em meio milhao de celulas.
	/// </summary>
	public const bool Cega = false;

	// =====================================================================
	// PRA ONDE SE CAI -- e a pergunta e da ZONA, nao da celula
	// =====================================================================

	/// <summary>
	/// ============================ POR QUE O DESTINO E POR ZONA E NAO POR CELULA ============================
	/// A peca que ja existe pra "pisar aqui te leva ali" e a <see cref="Passagem"/>, e ela **nao serve
	/// aqui** -- por duas razoes medidas:
	///
	///   * ela e uma LISTA POR CELULA, varrida a cada tique (`GameServer.Passagens.cs`). Sao
	///     **422.388 celulas** de nuvem que derruba (z6 + z12). A lista de passagens do jogo inteiro
	///     tem algumas dezenas de entradas;
	///   * ela **nao tem campo de condicao** (`Passagem.cs`: X, Y, Zona, Dx, Dy, Nome). "So derruba
	///     quem nao voa" nao cabe nela sem inventar um campo que nenhuma outra passagem usa.
	///
	/// E o destino nao PRECISA ser por celula: conferido nos quatro `.dmm`, **nenhuma zona mistura dois
	/// tipos de nuvem** -- o z6 e todo `SkyHD2`, o z12 e todo `Sky1`, o z10/z30/z31 sao todos de
	/// bloqueio. Uma linha por zona diz a mesma coisa que 422 mil linhas por celula, e diz onde da pra
	/// ler.
	/// ========================================================================================================
	///
	/// ============================ METADE E PORTE E METADE E DESENHO NOVO ============================
	/// **O Caminho da Serpente e PORTE LITERAL.** O `SkyHD2.Enter()` (`NewTurfs.dm:197-211`) ja faz
	/// exatamente isto:
	///
	/// <code>
	///     if(M.isflying|!M.density) return ..()
	///     else
	///         to_chat(usr, "You fall through the clouds and land in Hell!")
	///         M.loc=locate(63,260,9)
	/// </code>
	///
	/// **O Templo NAO EXISTE NO DM.** La o `Sky1.Enter()` (`Turfs.dm:81-84`) so BARRA -- nao ha queda,
	/// nao ha destino, nao ha mensagem. A queda pra Terra e desenho novo, pedida pelo dono, e a
	/// coordenada dela **nao foi inventada**: e a mesma que a escada do Templo ja usa pra devolver
	/// alguem pra Terra (`/turf/Teleporters/fromeg`, `Turfs.dm:151-157` -> `locate(128,162,1)`), e ela
	/// ja estava transcrita no port em `Tools/AssetPipeline/Passagens.cs`. Cair da beirada do Templo e
	/// chegar onde a escada chega e a leitura que o proprio mapa oferece.
	///
	/// ============================ O QUE **NAO** FOI PORTADO, E DE PROPOSITO ============================
	/// O DM tem mais dois destinos de nuvem e os dois sao CODIGO MORTO -- declarados e nunca postos em
	/// mapa nenhum (0 celulas nos quatro `.dmm`, contado):
	///
	///   * `/turf/SnakeWay/Clouds` (`Turfs.dm:1092-1099`) cai em `(22,222,3)` = **Vegeta**;
	///   * `/turf/Other/Sky2` (`Turfs.dm:85-96`) cai no Inferno.
	///
	/// Porta-los seria porte de coisa nenhuma: nao ha celula onde eles rodem. Ficam anotados pra que a
	/// proxima pessoa que os encontrar no `Turfs.dm` saiba que eles ja foram olhados.
	/// ====================================================================================================
	/// </summary>
	/// <param name="nomeDaZona">O nome da <see cref="ZoneKey"/> -- e so `Premade` tem nuvem de mapa.</param>
	/// <returns>
	/// A zona e a coordenada BYOND do destino, ou nulo quando a nuvem desta zona so BLOQUEIA (o Ceu, o
	/// Reino dos Deuses e os `Outside`).
	/// </returns>
	public static (string Zona, int Bx, int By)? DestinoDaQueda(string nomeDaZona)
	{
		if (string.Equals(nomeDaZona, Alem.ZonaDoOutroMundo, StringComparison.OrdinalIgnoreCase))
			// `M.loc=locate(63,260,9)` -- `NewTurfs.dm:209`. O z9 e o Inferno.
			return (Alem.ZonaDoInferno, 63, 260);

		if (string.Equals(nomeDaZona, ZonaDoTemplo, StringComparison.OrdinalIgnoreCase))
			// `locate(128,162,1)` -- a chegada do `fromeg` (`Turfs.dm:156`). O z1 e a Terra.
			return (ZonaDaTerra, 128, 162);

		return null;
	}

	/// <summary>
	/// A MESMA PERGUNTA EM `bool`, pra quem so precisa saber se a nuvem para ou deixa cair.
	///
	/// Ela e quem alimenta o `zonaDerruba` do <see cref="Travessia"/>, e existe pra que a resposta
	/// venha do MESMO lugar que o destino: um segundo lugar dizendo "esta zona derruba" poderia
	/// concordar hoje e discordar no dia em que uma zona ganhasse destino -- e o sintoma seria uma
	/// nuvem que deixa entrar e nao leva a lugar nenhum, ou seja o jogador ANDANDO NO CEU.
	/// </summary>
	public static bool Derruba(string nomeDaZona) => DestinoDaQueda(nomeDaZona) != null;

	/// <summary>O Templo Sagrado -- z12. E o `Lookout` que o dono citou pelo nome.</summary>
	public const string ZonaDoTemplo = "Lookout";

	/// <summary>A Terra -- z1.</summary>
	public const string ZonaDaTerra = "Earth";

	/// <summary>
	/// A COORDENADA BYOND DO DESTINO, EM PIXEL DO PORT.
	///
	/// A conta e a mesma do <see cref="Alem.MesaDoEnma"/> e do `MapConverter.Destinos` --
	/// `cx = bx - 1`, `cy = altura - by`, centro da celula --, e ela esta escrita aqui pelo mesmo
	/// motivo que esta escrita la: uma roda no CONVERSOR (offline) e outra roda no SERVIDOR (a cada
	/// queda), e o que nao pode divergir e a formula.
	///
	/// **ESTE NAO E O PONTO FINAL.** Ele e o ponto DESEJADO; quem decide onde o corpo encosta e o
	/// <see cref="ZoneCollision.PontoLivrePerto"/> da zona de destino -- ver <see cref="ServeDeChao"/>.
	/// </summary>
	public static Vec2 EmPixel(int bx, int by, int alturaDaZonaEmTiles)
	{
		int cx = Math.Clamp(bx - 1, 0, int.MaxValue);
		int cy = Math.Clamp(alturaDaZonaEmTiles - by, 0, Math.Max(0, alturaDaZonaEmTiles - 1));
		const int t = ZoneCollision.TileSize;
		return new Vec2(cx * t + t / 2f, cy * t + t / 2f);
	}

	/// <summary>
	/// O AVISO QUE O JOGADOR LE AO CAIR.
	///
	/// O do Caminho da Serpente e a traducao do original (`NewTurfs.dm:208`, *"You fall through the
	/// clouds and land in Hell!"*). O do Templo nao tem original pra traduzir -- ver
	/// <see cref="DestinoDaQueda"/> -- e segue a mesma voz.
	/// </summary>
	public static string AvisoDaQueda(string nomeDaZonaDeOrigem) =>
		string.Equals(nomeDaZonaDeOrigem, Alem.ZonaDoOutroMundo, StringComparison.OrdinalIgnoreCase)
			? "você atravessa as nuvens e cai no Inferno!"
			: "você atravessa as nuvens e cai de volta na Terra!";
}
