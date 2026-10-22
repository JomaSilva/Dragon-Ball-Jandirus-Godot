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
///     2 s de cadaver) ou DE PE (no alem, onde o DM faz `Un_KO()` antes de mover -- `Death.dm:89`);
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
	// O INFERNO -- o julgamento do Enma
	// =====================================================================
	/// <summary>O `locate(65,258,9)` do DM (`SkyNPCs.dm:182,236`): onde o condenado cai, e pra onde volta se tentar sair.</summary>
	public const int InfernoX = 65, InfernoY = 258;

	/// <summary>O portao do Inferno em pixels do z9 -- a mesma conversao BYOND -> Godot de <see cref="MesaDoEnma"/>.</summary>
	public static Vec2 PortaoDoInferno(int alturaDaZonaEmTiles)
	{
		int cx = Math.Clamp(InfernoX - 1, 0, int.MaxValue);
		int cy = Math.Clamp(alturaDaZonaEmTiles - InfernoY, 0, Math.Max(0, alturaDaZonaEmTiles - 1));
		const int t = ZoneCollision.TileSize;
		return new Vec2(cx * t + t / 2f, cy * t + t / 2f);
	}

	/// <summary>A pena maxima (`36000` ds = 1 h, `SkyNPCs.dm:178`) e o piso (`600` ds = 1 min).</summary>
	public const long MsMaxDePenaNoInferno = 3_600_000, MsMinDePenaNoInferno = 60_000;

	/// <summary>
	/// QUANTO TEMPO NO INFERNO -- `enma_judge_to_hell()` (`SkyNPCs.dm:176-182`): `frac = min(|karma|/100, 1)`,
	/// `lockdur = max(round(frac * 36000), 600)`. Karma -100 = 1 h, -50 = 30 min, -1 = 1 min (o piso). Zero
	/// pra quem nao tem coracao negro (karma >= 0): esse nem e julgado.
	/// </summary>
	public static long MsDePenaNoInferno(int karma)
	{
		if (karma >= 0) return 0;
		double frac = Math.Min(Math.Abs(karma) / 100.0, 1.0);
		return Math.Max((long)Math.Round(frac * MsMaxDePenaNoInferno), MsMinDePenaNoInferno);
	}

	// =====================================================================
	// 2. OS DOIS PRAZOS
	// =====================================================================
	/// <summary>
	/// ============================ QUANTO TEMPO O CORPO FICA CAIDO ANTES DE SUBIR ============================
	/// O `KO(-1)` + `sleep(20)` do `Death()` (`Death.dm:71-72`): 2,0 s (ver <see cref="TempoDoDm"/>) entre
	/// cair e o `loc = locate(187,104,6)`. E a janela em que o corpo fica no chao, na pose de nocaute e
	/// **sem aureola** (ver <see cref="TemAureola"/>: esta janela e o cadaver, e o cadaver do DM e
	/// fotografado antes de a aureola existir), antes do salto.
	///
	/// ============================ FORAM 15 s ATE 2026-09-04, DE PROPOSITO -- E O DONO DESFEZ ============================
	/// O port alargou a janela pra 15 s (o `MsAteRenascer` de antes do Outro Mundo) e escreveu aqui por
	/// que nao a devolvia ao DM: "quanto tempo voce olha o proprio corpo antes de ser puxado". O dono
	/// respondeu com a regra (2026-09-04): *"ao morrer demora muito pro jogador sair da tela do corpo
	/// morto e ir pro outro mundo, deveria ser no maximo 2 segundos apos a morte ja ir pro outro
	/// mundo"* -- que e exatamente o numero do original. O cadaver que fica pra tras (`DeixarOCadaver`)
	/// nasce no instante da viagem, entao ele continua sendo o corpo exato de quem caiu; so a espera
	/// encurtou. As bancadas que se ancoravam nos 15 s (`Client/RoboDeMorteVista.cs`) leem esta
	/// constante e foram reancoradas junto -- **uma linha, e as bancadas junto**.
	/// ============================================================================================================
	/// </summary>
	public const long MsNoChao = 2_000;

	/// <summary>
	/// O `KO(20)` do morto que "morre" DE NOVO no Outro Mundo (`Death.dm:113-118`, o `else if(dead)`):
	/// 20 ticks = 2,0 s de coma, depois de curado e devolvido a mesa do Enma. Ver `CombatState.AoMorrerDeNovo`.
	/// </summary>
	public const double SegundosDeComaDoMortoDeNovo = 2.0;

	// =====================================================================
	// 2b. O ENMA DAIOH -- a UNICA saida paga do Outro Mundo que este port tem
	// =====================================================================
	/// <summary>
	/// ============================ NAO HA VOLTA AUTOMATICA ============================
	/// Aqui morava `MsNoAlem = 60 s`, um andaime declarado: o port tinha a viagem e nenhuma das
	/// voltas do DM, e "prender todo mundo no Outro Mundo pra sempre" era pior. O andaime saiu no
	/// dia em que a primeira volta paga entrou (2026-09-04, pedido do dono: *"voce teria que ficar
	/// morto ate alguem te reviver com as esferas, ou juntar 1 milhao de zeni e pagar o Enma
	/// Daioh"*). Morreu, ficou morto: as esferas ("Revive"), a tecnica de reviver de quem esta vivo
	/// ao lado, ou o Enma. O `RelogioDaMorte` de quem ja viajou fica em `long.MaxValue` -- o tique
	/// nunca o examina de novo.
	///
	/// O DM tem ainda um `AutoRevive` GLOBAL de admin (30 min, `AutoRevive.dm:16-28`, ligado por
	/// padrao em `SettingsDatum.dm:297`). NAO foi portado, de proposito: e um interruptor de
	/// servidor, nao regra da morte, e o dono pediu o contrario dele.
	/// ==================================================================================
	/// </summary>
	public const string TipoDoEnma = "Enma_Daioh";

	/// <summary>O Enma Daioh do DM senta em (176,134,6) (`SkyNPCs.dm:59`), a 30 tiles da mesa onde os mortos chegam: e a fila.</summary>
	public const int EnmaDaiohX = 176, EnmaDaiohY = 134;

	/// <summary>`ZENI_REVIVE_COST = 1000000` (`SkyNPCs.dm:92`).</summary>
	public const double PrecoDoReviveDoEnma = 1_000_000;

	/// <summary>`zeni_revive_debuff_until = world.realtime + 36000` (`SkyNPCs.dm:194`): uma hora com o BP expresso em 25%.</summary>
	public const long MsDoDebuffDoEnma = 3_600_000;

	/// <summary>
	/// (v0.3.0) A FRACAO DO PODER QUE A REENCARNACAO LEVAVA: o `Reincarnate(0.1)` do Enma (`SkyNPCs.dm:204`) -- o proximo
	/// personagem da conta nascia com `round(oldBP * 0.1)` de BP base (`Reincarnation.dm:9`). Desde 2026-10-22 o BP inteiro vira
	/// potencial oculto (`GameServer.AplicarOEcoDaReencarnacao`); a fracao so vale no defeito injetado da regra de antes.
	/// </summary>
	public const double FracaoDaReencarnacao = 0.1;

	/// <summary>(v0.3.0) O passe pro mundo dos vivos: 100.000 zeni (o dono, 2026-10-15). Nao existe no DM.</summary>
	public const double PrecoDoPasseDoEnma = 100_000;

	/// <summary>(v0.3.0) Quantos dias o passe vale -- DIAS DO JOGO, o dia do ceu (`Espaco.SegundosPorDiaInGame`).</summary>
	public const int DiasDoPasseDoEnma = 7;

	/// <summary>O prazo do passe em ms de relogio: 7 dias de 24 min = 2 h 48 min.</summary>
	public const long MsDoPasseDoEnma = (long)(DiasDoPasseDoEnma * Espaco.SegundosPorDiaInGame * 1000);

	/// <summary>Onde o Enma senta, na grade do Godot -- a mesma conversao da <see cref="MesaDoEnma"/>.</summary>
	public static Vec2 CadeiraDoEnma(int alturaDaZonaEmTiles)
	{
		int cx = Math.Clamp(EnmaDaiohX - 1, 0, int.MaxValue);
		int cy = Math.Clamp(alturaDaZonaEmTiles - EnmaDaiohY, 0, Math.Max(0, alturaDaZonaEmTiles - 1));
		const int t = ZoneCollision.TileSize;
		return new Vec2(cx * t + t / 2f, cy * t + t / 2f);
	}

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
	///
	/// (v0.3.0) E A MENTE TAMBEM E LUGAR DE MORTO: o fantasma que mergulha na meditacao profunda fica de pe la dentro -- o
	/// dono: *"permita q morto possa fazer meditacao profunda [...] na interdimension e a excecao"*. E o unico lugar fora do
	/// Outro Mundo onde o morto sem o passe anda, luta e treina (ver `DimensaoMental.PorQueNaoMergulhar`).
	/// </summary>
	public static bool MortoDePe(bool morto, string nomeDaZona) =>
		morto && (EhOAlem(nomeDaZona) || (nomeDaZona == DimensaoMental.Zona && !MenteDeitaOMortoDeTeste));

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--transeteste` e `--diagv030`): a mente volta a nao ser lugar de morto -- o fantasma que
	/// mergulhou e o cadaver deitado la dentro. Lido por <see cref="MortoDePe"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool MenteDeitaOMortoDeTeste;

	/// <summary>
	/// (v0.3.0) O MORTO COM O PASSE DO ENMA, entre os vivos: de pe e de aureola FORA do Outro Mundo enquanto o prazo da
	/// ficha (`Fighter.passe_do_enma_ate`) estiver armado. E a outra metade do "este morto anda?": o lugar responde pelo
	/// Outro Mundo, o passe responde pelo resto. Quem desarma e o relogio do passe (`GameServer.CumprirOPasseDoEnma`) -- por
	/// isso a pergunta e "armado", e nao "dentro do prazo": entre o vencimento e o tique da volta (ate um quinto de
	/// segundo) o morto continua de pe, e nao cai como cadaver no meio do caminho.
	/// </summary>
	public static bool EmVisita(bool morto, long passeAte) => morto && passeAte != 0;

	/// <summary>
	/// (v0.3.2) A PASSAGEM DO MORTO PELA TRANSMISSAO: quantos segundos o morto que chegou aos vivos pela Transmissao Instantanea fica la
	/// -- meio transparente -- antes de a alma ser puxada de volta pro Outro Mundo. O dono, 2026-10-16: *"caso um player morto use
	/// instante transmission pra alguem q ta vivo, ele aparece meio transparente e fica 5 segundos no mundo dos vivos dps voltando pro
	/// outro mundo"*. Nao existe no DM: la a transmissao nao pergunta `dead` (`yardrat.dm:100-160`), e o morto chegava e ficava.
	/// </summary>
	public const double SegundosDaPassagem = 5;

	/// <summary>A passagem em milissegundos (`SegundosDaPassagem`).</summary>
	public const long MsDaPassagem = (long)(SegundosDaPassagem * 1000);

	// =====================================================================
	// AS ILHAS DO OUTRO MUNDO PUXAM 10x (dono, 2026-10-15)
	// =====================================================================
	// *"as ilhas q estao no outro mundo/ceu deveriam ter gravidade de 10x, assim como o planeta do sr kaioh era no anime,
	// porem, o resto do ceu como o caminho da serpente e o local do emna mantem gravidade em 1x"*.
	//
	// No DM so o planeta do Sr. Kaioh puxava 10x: o retangulo de `Gravity.dm:121-123` (x 29..49, y 273..350 contados de
	// baixo pra cima, no z6 -- a ilha do OESTE). Aqui TODA ILHA do mapa do Outro Mundo: a terra solta entre as nuvens, fora
	// do Caminho da Serpente (e do palacio da Princesa Serpente, que fica no laco dele) e do palacio do Enma. Os retangulos
	// sao do mapa `z06_Afterlife` (celulas, y de cima pra baixo), um por componente conexa de chao que nao toca o Caminho:
	// os quatro planetas dos Kaiohs (as quatro ilhas redondas com a mesma casa), a ilha das palmeiras, a do fragmento de
	// ressurreicao e a do peixe.
	//
	// O SR. KAIOH E O DO NORTE (dono, no mesmo pedido: *"coloca o sr kaioh na ilha mais ao norte"*) -- o DM o punha na do
	// oeste, e por isso o nome "o planeta do Sr. Kaioh" e o da ilha de cima.

	/// <summary>As ilhas do Outro Mundo: o nome que a frase usa e o retangulo, em celulas (inclusive).</summary>
	public static readonly (string Nome, int X0, int Y0, int X1, int Y1)[] IlhasDoOutroMundo =
	[
		("o planeta do Sr. Kaioh", 125, 0, 219, 40),
		("o planeta do Kaioh do Oeste", 15, 149, 49, 236),
		("o planeta do Kaioh do Leste", 444, 196, 478, 283),
		("o planeta do Kaioh do Sul", 270, 447, 364, 483),
		("a ilha das palmeiras", 291, 285, 331, 346),
		("a ilha do fragmento de ressurreição", 256, 210, 281, 233),
		("a ilha do peixe", 12, 377, 53, 413),
	];

	/// <summary>A gravidade das ilhas: a do planeta do Sr. Kaioh no anime (e no DM).</summary>
	public const double GravidadeDasIlhas = 10;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v030teste`): as ilhas voltam a puxar como o Caminho (1x) -- o jogo de antes. Lido por
	/// <see cref="IlhaEm"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool IlhasComoOCaminhoDeTeste;

	/// <summary>A ilha do Outro Mundo nesta celula (o nome da frase), ou nulo -- fora delas, ou noutra zona.</summary>
	public static string? IlhaEm(string nomeDaZona, int cx, int cy)
	{
		if (IlhasComoOCaminhoDeTeste || !string.Equals(nomeDaZona, ZonaDoOutroMundo, StringComparison.OrdinalIgnoreCase)) return null;
		foreach ((string nome, int x0, int y0, int x1, int y1) in IlhasDoOutroMundo)
			if (cx >= x0 && cx <= x1 && cy >= y0 && cy <= y1) return nome;
		return null;
	}

	/// <summary>
	/// ============================ A AUREOLA E DA VIAGEM, E NAO DA MORTE ============================
	/// **O QUE ESTAVA ESCRITO AQUI ERA "uma verdade so: a aureola *e* `Ficha.dead`", E ESSA FRASE
	/// VIROU O BUG QUE O DONO FOTOGRAFOU:** *"so tem um bug na morte, o personagem fica com AUREOLA
	/// NO CORPO MORTO dele, oq n deveria acontecer. o corpo q fica no MAPA DOS VIVOS deveria ser o
	/// EXATO CORPO DELE QUANDO MORRE, sem a aureola"*. A intencao do desenho antigo era boa (ver o
	/// item 3 abaixo, que sobreviveu inteiro), mas a premissa estava incompleta: **neste port o
	/// cadaver E o proprio corpo**, deitado por <see cref="MsNoChao"/> = 2 s no mundo dos vivos
	/// antes do salto. `dead` fica verdadeiro 2 s ANTES de o corpo sair -- e a aureola acendia em
	/// cima do cadaver.
	///
	/// ============================ NO DM ISSO NAO ACONTECE POR ORDEM, E A ORDEM ESTA MEDIDA ============================
	/// `Death()` fotografa o cadaver no passo 5 -- `GenerateCorpse()`, `Death.dm:64-67` --, e o
	/// cadaver e um **objeto separado** (`/obj/mobCorpse`, `Corpse.dm:75-85`) que copia os overlays
	/// DAQUELE instante. A aureola so e pendurada no passo 10 (`overlayList += 'Halo.dmi'`,
	/// `Death.dm:106-108`), no mob que na linha seguinte VIAJA (`:110`). **O cadaver do BYOND nunca
	/// ve a aureola porque ele foi fotografado antes de ela existir** -- que e, palavra por palavra,
	/// o que o dono descreveu.
	///
	/// Aqui nao ha `mobCorpse` (ver <see cref="MsNoChao"/>), entao a ordem que la e implicita tem que
	/// ser dita em voz alta: a aureola nao pertence ao instante da morte, pertence a VIAGEM.
	///
	/// ============================ O CRIVO E TEMPORAL, E NAO POR LUGAR -- ISTO NAO E DETALHE ============================
	/// A correcao obvia era copiar a forma do vizinho <see cref="MortoDePe"/> e escrever
	/// `morto && EhOAlem(zona)`. Resolveria a foto **e estaria errada**: no original um morto pode
	/// ANDAR NO MUNDO DOS VIVOS com `KeepsBody` (`OtherworldRankSkills.dm:195-202`, o verb
	/// `Keep_Body` de cargo), e ali a aureola e justamente o que denuncia que ele esta morto -- o
	/// jogo brinca com isso na cara do jogador (*"Or, you could, y'know, look at their goddamn
	/// Halo."*, `OtherworldRankSkills.dm:45`). Um crivo por LUGAR apagaria a aureola do visitante
	/// morto.
	///
	/// **`KeepsBody` ainda nao esta portado** (so e citado em comentario, no
	/// `GameServer.Esmagamento.cs`), entao hoje os dois crivos dariam a mesma resposta em jogo. A
	/// escolha e feita mesmo assim: o crivo por lugar plantaria o defeito pro dia em que ele for
	/// portado, e ninguem ia ligar as duas coisas. A pergunta certa e **quando**, nao **onde**: este
	/// corpo ainda e o CADAVER (a janela antes da viagem)? Se sim, sem aureola. Depois da viagem,
	/// aureola -- esteja ele onde estiver.
	///
	/// ============================ E A BOA PROPRIEDADE DO DESENHO ANTIGO CONTINUA VALENDO ============================
	/// A aureola e uma CONJUNCAO com `morto`, e nao um bit paralelo: apagar a morte apaga a aureola
	/// **sem que ninguem saiba que ela existe**. O revive, o restauro da mente e a volta no tempo do
	/// `Tecnicas.G4` continuam sem uma linha propria pra ela, exatamente como antes -- o
	/// `jaViajouProAlem` so e LIDO com `morto` ligado, e quem o rearma pra morte seguinte e o mesmo
	/// funil unico que arma o relogio (`GameServer.Alem.AMorteAconteceu`), e nao um chamador que
	/// precise lembrar.
	///
	/// ============================ ELA NAO E A MESMA PERGUNTA DO `MortoDePe`, E AS DUAS SE PARECEM ============================
	/// <see cref="MortoDePe"/> decide a POSE e pergunta ONDE o corpo esta (um morto num lugar de
	/// morto anda; qualquer outro morto e um cadaver no chao). Esta aqui decide o DESENHO e pergunta
	/// QUANDO. **Elas ja divergem hoje**, e num caso que existe: quem morre DENTRO do alem (o
	/// visitante vivo que apanha la) fica de pe na hora -- porque esta num lugar de morto -- e **sem
	/// aureola** pelos 2 s, porque ainda e o cadaver e nao viajou. As duas respostas estao certas, e
	/// nenhum dos dois crivos sabe da existencia do outro.
	///
	/// No dia do `KeepsBody` elas divergem de novo, pro outro lado: o morto que volta ao mundo dos
	/// vivos anda **e** tem aureola. **Nao funda os dois.**
	/// ====================================================================================================
	/// </summary>
	/// <param name="morto">`Ficha.dead`.</param>
	/// <param name="jaViajouProAlem">
	/// Esta morte ja passou pela viagem? Falso durante os 2 s de cadaver -- ver
	/// `ServerPlayer.MorteJaViajou`.
	/// </param>
	public static bool TemAureola(bool morto, bool jaViajouProAlem) => morto && jaViajouProAlem;
}
