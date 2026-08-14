namespace Jandirus.Core.World;

/// <summary>
/// ============================ O OUTRO MUNDO, COMO LUGAR E COMO PRAZO ============================
/// *"falta fazer o personagem quando MORRER ir pro OUTRO MUNDO e a AUREOLA aparecer sobre a
/// cabeca"* -- o pedido do dono, e este arquivo e a metade dele que nao conhece Godot.
///
/// Porte de `Code/Modules/Death/Death.dm`, o funil unico da morte no original. La os doze passos
/// de `mob/proc/Death()` terminam em tres linhas que sao o pedido inteiro:
///
///     overlayList -= 'Halo.dmi'; overlayList += 'Halo.dmi'; overlaychanged = 1   // :106-108
///     loc = locate(187, 104, 6)                                                 // :110
///     SpreadHeal(100, 1, 1)                                                     // :111
///
/// O que mora AQUI e so o que as duas pontas precisam concordar sem se falar: **que lugar e o
/// alem**, **onde no alem se chega** e **quanto tempo cada etapa dura**. As regras de dentro (quem
/// viaja, quem nao viaja, o que se pode fazer morto) moram em `GameServer.Alem.cs`.
/// ==========================================================================================
///
/// ============================ POR QUE ELE E UM `static class` E NAO UM CAMPO DO SERVIDOR ============================
/// Tres consumidores que nao se conhecem leem a mesma frase:
///
///   * o SERVIDOR, pra saber pra onde mandar o morto e por quanto tempo;
///   * o `ServerPlayer.Pose`/`Deitado`, pra saber se este morto esta CAIDO (no mundo dos vivos, os
///     15 s de cadaver) ou DE PE (no alem, onde o DM faz `Un_KO()` antes de mover -- `Death.dm:89`);
///   * o CLIENTE, que desenha a auréola -- e ele nao pode perguntar ao servidor a cada quadro.
///
/// A alternativa era `nome == "Afterlife"` escrito em tres lugares -- e a primeira copia a
/// envelhecer seria a que ninguem le, que e exatamente o historico do `ZonaEhPlaneta` antes de
/// virar <see cref="Espaco.EhPlaneta"/>.
/// ================================================================================================================
/// </summary>
public static class Alem
{
	// =====================================================================
	// 1. QUE LUGAR E ESTE
	// =====================================================================
	/// <summary>
	/// O OUTRO MUNDO -- o z6 do original, a sala do Enma. **E ELE QUE E O LIMBO**: no DM nao ha uma
	/// quarta zona de espera, `Afterlife` E a espera (`SkyNPCs.dm:150-173`, o menu do Enma roda ali).
	/// </summary>
	public const string ZonaDoOutroMundo = "Afterlife";

	/// <summary>O Ceu -- z10. Destino de cargo e de passagem, nao de julgamento.</summary>
	public const string ZonaDoCeu = "Heaven";

	/// <summary>O Inferno -- z9. Destino do `enma_judge_to_hell()` (`SkyNPCs.dm:176-182`).</summary>
	public const string ZonaDoInferno = "Hell";

	/// <summary>
	/// ESTA ZONA E O ALEM? As tres do original, e a pergunta e por NOME e nao por `ZoneKey` porque
	/// quem mais a faz -- <see cref="MortoDePe"/> -- e lido de dentro do `ServerPlayer`, que tem a
	/// chave mas nao tem o catalogo.
	///
	/// AS TRES JUNTAS E NAO SO A PRIMEIRA: o `RevivalShards.dm:67` espalha os cacos em
	/// `Afterlife/Heaven/Hell` e o `:109` so os aceita nessas tres, e o `Gravity.dm:131-133` da o
	/// bonus racial do Kai no Ceu e o do Demonio no Inferno. Pro original as tres sao "estar morto
	/// num lugar de morto"; separa-las aqui faria o morto que atravessa a passagem pro Inferno
	/// (`CaveEntrance2`) deitar no chao de novo do outro lado.
	/// </summary>
	public static bool EhOAlem(string nomeDaZona) =>
		string.Equals(nomeDaZona, ZonaDoOutroMundo, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(nomeDaZona, ZonaDoCeu, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(nomeDaZona, ZonaDoInferno, StringComparison.OrdinalIgnoreCase);

	/// <summary>A mesma pergunta com a chave -- so `Premade` pode ser o alem (os tres sao mapas do .dmm).</summary>
	public static bool EhOAlem(ZoneKey z) => z.Kind == ZoneKey.KindPremade && EhOAlem(z.Name);

	/// <summary>
	/// ============================ A MESA DO ENMA, EM COORDENADA BYOND ============================
	/// `loc = locate(187, 104, 6)` -- `Death.dm:110`. Guardado como o DM escreve, e nao ja
	/// convertido, porque e assim que da pra conferir contra o original sem fazer conta de cabeca.
	///
	/// Ha UM ramo alternativo no DM que **nao foi portado**: quem morre com `Planet == "Sealed"`
	/// (o Boss Rush) cai em `(221,266,7)` -- `Death.dm:91-99`. O Boss Rush ficou de fora por
	/// decisao do dono (ver `PROXIMOS-SISTEMAS.md`), entao a coordenada dele nao existe aqui.
	/// ==========================================================================================
	/// </summary>
	public const int EnmaX = 187, EnmaY = 104;

	/// <summary>
	/// A MESA DO ENMA EM PIXEL, no sistema do port.
	///
	/// ============================ A CONTA DO Y E A INVERSAO DO BYOND ============================
	/// La o eixo cresce pra CIMA e aqui pra BAIXO: a linha `by` vira `altura - by`. E exatamente a
	/// mesma conta que o `MapConverter.Destinos` ja faz pras passagens (`MapConverter.cs:260-267`),
	/// e ela esta repetida aqui de proposito: aquela roda no CONVERSOR (uma vez, offline, com o
	/// `.dmm` na mao) e esta roda no SERVIDOR (a cada morte). O que nao pode divergir e a formula,
	/// e ela esta escrita nos dois lugares com a mesma frase pra que a divergencia salte aos olhos.
	///
	/// Errar isto poria a chegada ESPELHADA na vertical -- e sem sintoma nenhum a nao ser "a morte
	/// me cospe no lugar errado do Outro Mundo".
	/// ========================================================================================
	/// </summary>
	/// <param name="alturaDaZonaEmTiles">O `h` do manifesto de mapas (500 pro z6).</param>
	public static Vec2 MesaDoEnma(int alturaDaZonaEmTiles)
	{
		int cx = Math.Clamp(EnmaX - 1, 0, int.MaxValue);
		int cy = Math.Clamp(alturaDaZonaEmTiles - EnmaY, 0, Math.Max(0, alturaDaZonaEmTiles - 1));
		const int t = ZoneCollision.TileSize;
		return new Vec2(cx * t + t / 2f, cy * t + t / 2f);
	}

	// =====================================================================
	// 2. OS DOIS PRAZOS
	// =====================================================================
	/// <summary>
	/// ============================ QUANTO TEMPO O CORPO FICA CAIDO ANTES DE SUBIR ============================
	/// **Este numero ja era do port** -- era o `MsAteRenascer` do `GameServer.Combat.cs`, os 15 s
	/// entre morrer e reaparecer no berco. Ele mudou de significado, nao de valor: agora e a janela
	/// em que o corpo fica no chao, na pose de nocaute e com a aureola acesa, antes do salto.
	///
	/// ============================ NO DM SAO 2,0 s, E A DIFERENCA E DELIBERADA ============================
	/// O original faz `KO(-1)`, `sleep(20)` (= 2,0 s, ver <see cref="TempoDoDm"/>) e so entao
	/// `loc = locate(...)` -- `Death.dm:71-110`. Dois segundos bastam LA porque o que fica pra tras
	/// e um `/obj/mobCorpse` de verdade (`Corpse.dm:75-85`): um cadaver que se pode comer, enterrar,
	/// esfolar e destruir. **Este port nao tem cadaver largado** -- nao ha item-no-chao nenhum aqui
	/// (ver o comentario de `Protocol.Decal.Membro`), entao o corpo do jogador E o cadaver enquanto
	/// dura.
	///
	/// Encurtar pra 2 s sem ter o `mobCorpse` deixaria a morte ilegivel: o inimigo piscaria e nao
	/// estaria mais la. Os 15 s sao o cadaver que o port nao tem. Quando o `mobCorpse` for portado,
	/// este numero desce pros 2,0 s do DM e a diferenca desaparece.
	/// ====================================================================================================
	/// </summary>
	public const long MsNoChao = 15_000;

	/// <summary>
	/// ============================ QUANTO TEMPO SE FICA MORTO NO ALEM ============================
	/// **ESTE NUMERO E DO PORT E NAO DO DM, E E UM ANDAIME.** No original nao existe volta
	/// automatica nenhuma: morreu, ficou morto ate alguem pagar. Sao cinco caminhos e todos custam
	/// algo (`SkyNPCs.dm:185-204` o Enma por Zeni ou a reencarnacao, `OtherworldRankSkills.dm:217-267`
	/// o verb de cargo, `WishTable.dm:230` as esferas, `RevivalShards.dm` os cacos). **Nenhum dos
	/// cinco existe neste port ainda** -- nao ha Enma, nao ha Sr. Kaioh, nao ha caco, nao ha karma
	/// escrito por PK.
	///
	/// Portar a viagem sem portar nenhuma das voltas prenderia todo mundo no Outro Mundo pra sempre,
	/// e a instrucao do dono e explicita: *"prefira sempre o que NAO PRENDE NINGUEM"*. Entao o
	/// renascimento automatico que o port ja tinha (os 15 s) nao foi apagado -- ele foi EMPURRADO
	/// pra depois da viagem. O jogador morre, ve o proprio corpo cair, sobe pro Outro Mundo com a
	/// aureola, e um minuto depois volta a vida no berco.
	///
	/// ============================ O QUE ESTE NUMERO SUBSTITUI, PRA NAO SE PERDER ============================
	/// O DM tem um campo pra isto -- `reviveTime = 36000 * log(deathcounter)` (`Death.dm:109`) --,
	/// e ele **nao e um cronometro**: e o CUSTO em cacos de revivencia (`RevivalShards.dm:113`
	/// consome `1 x shardNumber` por decimo de segundo). Usa-lo como espera seria ler o campo
	/// errado, e ainda por cima daria ZERO na primeira morte (`log(1) == 0`, o quirk que o DM tem
	/// de verdade) -- ou seja, o jogador nunca chegaria a VER o Outro Mundo, que e o pedido.
	///
	/// **QUANDO O ENMA ENTRAR, ESTA CONSTANTE MORRE.** Ela e o unico lugar a apagar: o julgamento
	/// vira a saida, e o `TickDaMorte` deixa de ter a segunda etapa.
	/// ====================================================================================================
	/// </summary>
	public const long MsNoAlem = 60_000;

	// =====================================================================
	// 3. AS DUAS PERGUNTAS QUE O DESENHO FAZ
	// =====================================================================
	/// <summary>
	/// ESTE MORTO ESTA DE PE? -- e a pergunta que separa o CADAVER do FANTASMA.
	///
	/// ============================ POR QUE ELA E DERIVADA E NAO UM CAMPO ============================
	/// O DM levanta o morto ANTES de move-lo: `RegrowLimb` + `spawn Un_KO()` (`Death.dm:86-89`) e
	/// so depois `loc = locate(187,104,6)`. La dentro do Outro Mundo o morto **anda, voa, treina e
	/// luta** -- `move = 1` e escrito quatro vezes no proprio `Death()`, e o autor comenta que so
	/// nega Zenkai *"ja morto, ex: torneio do ceu"* (`combatgains.dm:28-33`).
	///
	/// Um campo `bool NoAlem` seria uma SEGUNDA VERDADE ao lado de `Ficha.dead` + `Zone`, e este
	/// port ja pagou essa conta (o `members` da sessao da mente, que virou a `ZoneList`; o
	/// `CloneId` do visitante, que virou a chave da zona). Aqui a resposta sai do estado real: um
	/// morto que esta num lugar de morto esta de pe; um morto em qualquer outro lugar e um corpo
	/// no chao esperando o salto.
	///
	/// De graca vem o caso que um campo erraria: o morto que atravessa a passagem pro Ceu ou pro
	/// Inferno continua de pe, sem ninguem ter que lembrar de reescrever o campo na travessia.
	/// ==========================================================================================
	/// </summary>
	public static bool MortoDePe(bool morto, string nomeDaZona) => morto && EhOAlem(nomeDaZona);

	/// <summary>
	/// ============================ A AUREOLA ACENDE COM A MORTE, E NAO COM A CHEGADA ============================
	/// **Divergencia de 15 s do DM, e ela e escolhida.** La a aureola e pendurada na linha 106, ou
	/// seja depois do `sleep(20)` -- o cadaver caido no chao nao tem aureola porque ele nem e mais
	/// o mob (virou `/obj/mobCorpse`).
	///
	/// Aqui o corpo caido E o jogador, e ate hoje **um morto era visualmente indistinguivel de um
	/// nocauteado**: os dois caem na `Protocol.Pose.Nocauteado` e a unica tela que sabia a diferenca
	/// era a linha "Condicao: MORTO" do menu do proprio morto. Quem matou nao tinha como saber se
	/// tinha matado. Acender a aureola no instante da morte conserta isso com o desenho que o autor
	/// ja fez -- e o `Halo.dmi` TEM um estado `ko`, um desenho da aureola ao lado de um corpo
	/// deitado, que so faz sentido pra ser usado assim.
	///
	/// UMA VERDADE SO: a aureola **e** `Ficha.dead`. Nao ha campo, nao ha etapa, nao ha um segundo
	/// bit pra sincronizar -- e por isso todo caminho que apaga a morte (o revive, o restauro da
	/// mente, a volta no tempo do `Tecnicas.G4`) ja apaga a aureola sem saber que existe uma.
	/// ========================================================================================================
	/// </summary>
	public static bool TemAureola(bool morto) => morto;
}
