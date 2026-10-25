using System.Text.Json;

namespace Jandirus.Core.Npc;

/// <summary>
/// EM QUE PE ESTA UM ELO DA CADEIA. E o `s1_state`/`s2_state`/`s3_state`/`s4_state` do original
/// (BossEvents.dm:296-299), que la sao numeros soltos com a legenda escrita num comentario.
///
/// ============================ POR QUE UM SO, E NAO QUATRO CONJUNTOS DE NUMEROS ============================
/// O DM tem quatro maquinas de estado com significados PARECIDOS e nao iguais: `s1_state == 3` e
/// "Freeza derrotado", `s3_state == 3` e "Cell fora do mundo absorvendo", e `s3_state == 4` -- que
/// nas outras sagas quer dizer "o vilao venceu" -- ali quer dizer "o Cell foi destruido de vez".
/// A propria fonte precisa de tres procs (`saga1_over`, `saga2_over`, `saga3_over`) so pra
/// responder "acabou?", e o de numero 3 tem uma linha de comentario explicando por que ele e
/// diferente dos outros dois.
///
/// Aqui a pergunta "acabou?" e <see cref="EstadoDaSaga.Terminou"/> e vale pra qualquer elo, porque
/// o que muda entre uma saga e outra e o DADO (quantos chefes, quando chegam), nao o estado.
/// ======================================================================================================
/// </summary>
public enum FaseDaSaga
{
	/// <summary>Ninguem bateu o marco ainda. O elo nem foi anunciado.</summary>
	Adormecida,

	/// <summary>O marco bateu, o mundo foi avisado e a contagem em dias in-game esta correndo.</summary>
	Contando,

	/// <summary>Ha chefe (ou chefe por chegar) no mundo.</summary>
	EmCurso,

	/// <summary>Todos os chefes caíram. O elo seguinte pode comecar.</summary>
	Vencida,

	/// <summary>O vilao consumou o que veio fazer (hoje: so a destruicao do planeta, que e gancho).</summary>
	Consumada,

	/// <summary>O planeta alvo ja nao existia quando o marco bateu -- o `bev_planet_ok` recusando.</summary>
	Cancelada,
}

/// <summary>Em que pe esta UM chefe de um elo.</summary>
public enum FaseDoChefe
{
	/// <summary>Ainda nao chegou: a contagem em dias in-game dele esta correndo.</summary>
	ACaminho,

	/// <summary>Esta no mundo agora.</summary>
	NoMundo,

	/// <summary>Foi derrotado por alguem.</summary>
	Derrotado,

	/// <summary>Saiu do mundo sem morrer -- absorvido, ou o evento acabou por cima dele.</summary>
	Consumido,
}

/// <summary>
/// UM CHEFE DENTRO DE UM ELO -- o molde, quando ele chega, e o que a morte dele vale.
///
/// ============================ A ONDA E DADO, E ISSO APAGA A SAGA 3 DO CODIGO ============================
/// No original as quatro sagas tem quatro procs de spawn (`spawn_freeza_vegeta`, `spawn_freeza_namek`,
/// `spawn_androids`, `spawn_cell`, `spawn_boo`) e a de numero 3 e a unica com DOIS momentos: os
/// androides na hora, o Cell dois dias depois (`BEV_CELL_DELAY_DAYS`, BossEvents.dm:48). Isso vira, la,
/// um campo de contagem so dela (`cell_days`) e um ramo proprio no `day_tick`.
///
/// Aqui cada chefe tem a PROPRIA contagem em dias, entao "os androides na hora e o Cell dois dias
/// depois" e tres linhas de JSON e nenhuma de codigo -- e o dono acrescenta uma quarta onda a uma saga
/// existente sem ninguem recompilar nada.
/// ====================================================================================================
/// </summary>
public sealed class ChefeDaSaga
{
	/// <summary>Id do <see cref="MoldeDeNpc"/>. Ele TEM que ser do tipo <see cref="TipoDeNpc.Chefe"/>.</summary>
	public string Molde = "";

	/// <summary>Dias in-game entre o disparo do marco e a chegada dele. Faixa: sorteia no disparo.</summary>
	public int DiasMin, DiasMax;

	/// <summary>
	/// Reputacao de heroi que o matador ganha com o povo do planeta. `BEV_HERO_REP` = 100 pros
	/// chefes de verdade, `BEV_HERO_REP_ANDROID` = 50 por androide (BossEvents.dm:92-93).
	/// </summary>
	public double Recompensa = 100;

	/// <summary>
	/// ELE ABSORVE OS COMPANHEIROS DE ONDA CAIDOS? E o Cell (`cell_absorb_contact`, BossEvents.dm:680).
	///
	/// Encostar num chefe NOCAUTEADO do mesmo elo tira aquele corpo do mundo e faz ESTE subir um
	/// degrau da propria ficha -- que e o mesmo <c>AvancarEstagio</c> que o dano dispara, com outro
	/// gatilho. A evolucao do Cell nao e uma segunda mecanica: e a ficha pronta dele, movida por um
	/// evento em vez de por um limiar de membro.
	/// </summary>
	public bool Absorve;

	/// <summary>
	/// MORRER NESTE DEGRAU (1-based) NAO E MORRER: ele volta no degrau seguinte depois de
	/// <see cref="RessurgeEmSegundos"/>. E o Cell morto na forma perfeita voltando Super Perfeito
	/// (`s3_state = 5`, BossEvents.dm:760-763). Zero = a morte e definitiva em qualquer degrau.
	/// </summary>
	public int RessurgeNoDegrau;

	/// <summary>`BEV_CELL_REVIVE_DELAY` -- 600 decimos = 60 s.</summary>
	public double RessurgeEmSegundos = 60;

	/// <summary>
	/// (2026-10-19) CHEGANDO NESTE DEGRAU (1-based) ELE PARA A LUTA E MARCA O TORNEIO DELE -- o Cell Perfeito, o dono: *"se ele virar perfeito,
	/// ele avisa q em 5 dias tera um torneio do cell"*. Zero = sem torneio. Ver `Npc.TorneioDoCell` e `GameServer.TorneioDoCell.cs`.
	/// </summary>
	public int TorneioNoDegrau;

	/// <summary>(2026-10-19) Dias in-game entre o anuncio do torneio e a abertura dele na arena -- o dono: *"em 5 dias"*.</summary>
	public int DiasAteOTorneio = 5;

	/// <summary>O que a chegada dele anuncia. Vazio = chegada silenciosa (o Cell, no original).</summary>
	public string AnuncioDaChegada = "";

	/// <summary>O que a morte dele anuncia.</summary>
	public string AnuncioDaQueda = "";
}

/// <summary>
/// UM ELO DA CADEIA: um marco de BP, um planeta, e a onda de chefes que vem por causa dele.
///
/// **DADO, E NAO CODIGO.** Mora na secao `sagas` do `Assets/Data/npcs.json`, ao lado dos moldes que
/// ela cita. Acrescentar um elo -- e a especificacao aponta a lacuna entre Cell (5M) e Boo (1,5B)
/// como o lugar natural -- e acrescentar um objeto nessa lista.
/// </summary>
public sealed class EloDaSaga
{
	public string Id = "";
	public string Nome = "";

	/// <summary>Onde ela acontece. Tem que ser uma zona pre-feita do manifesto.</summary>
	public string Planeta = "";

	/// <summary>
	/// O MARCO, em BP **BASE** -- `BEV_M1_TRIGGER_BP` e irmaos (BossEvents.dm:39-42), e o
	/// `M.BP >= ...` do `check_triggers` (:375) le o `BP` cru e nao o `expressedBP`.
	///
	/// A armadilha 6 da PARTE 3 e exatamente esta: `expressedBP` nao e `BP`. Comparar 20 mil contra
	/// o expresso faria a saga 1 disparar no primeiro Saiyajin que carregasse Ki.
	/// </summary>
	public double GatilhoBp;

	/// <summary>
	/// SO ESTA RACA (ou linhagem) CONTA PRO MARCO. Vazio = qualquer um. E o `bev_is_saiyan`
	/// (BossEvents.dm:164), que a saga 1 exige e as outras tres nao.
	/// </summary>
	public string GatilhoRaca = "";

	public ChefeDaSaga[] Chefes = [];

	/// <summary>O aviso do marco: "Freeza se aproxima...". `{dias}` vira o numero sorteado.</summary>
	public string AnuncioDoMarco = "";

	/// <summary>O lembrete diario enquanto a contagem corre. Vazio = sem lembrete (o Cell).</summary>
	public string AnuncioDiario = "";

	/// <summary>O que se ouve quando o ultimo chefe do elo cai.</summary>
	public string AnuncioDaVitoria = "";

	// =====================================================================
	// O GANCHO DA DESTRUICAO (ver `GameServer.Sagas.cs`)
	// =====================================================================
	/// <summary>
	/// ============================ ESTE ELO DESTROI O PLANETA SE NINGUEM O IMPEDIR? ============================
	/// Declarado no dado porque e do EVENTO: no original a saga 1 e a 2 destroem
	/// (`cast_planet_destroy`, BossEvents.dm:780) e a 3 e a 4 nao.
	///
	/// **HOJE O GANCHO E INERTE, E DE PROPOSITO.** A destruicao de planeta e o bloco 10 da PARTE 2 e
	/// nao existe -- nao ha `DestroyPlanet`, nem `isDestroyed`, nem `planet_dying`. O que existe aqui
	/// e o RELOGIO e o ponto de chamada: o ultimato corre, vence, o mundo e avisado e o elo fica
	/// marcado como consumado. Nenhum tile some.
	///
	/// Foi construido assim em vez de "depois a gente ve" porque a metade cara e o relogio (a espera
	/// em dias, o adiamento com o servidor vazio, o KO que interrompe), e ela e testavel hoje. Quando
	/// a destruicao existir, o que muda e o corpo de uma funcao (`GanchoDeDestruicao`) -- nao a cadeia.
	/// =====================================================================================================
	/// </summary>
	public bool DestroiPlaneta;

	/// <summary>
	/// SEM NINGUEM ENFRENTANDO, quantos MINUTOS ate o ultimato. `BEV_FREEZA1_IDLE_TIMER` = 10800
	/// decimos = 18 min (BossEvents.dm:61).
	/// </summary>
	public double MinutosDeOcio = 18;

	/// <summary>
	/// DEPOIS QUE A LUTA COMECA, quantos minutos ate o ultimato. `BEV_FREEZA1_FIGHT_TIMER` = 3600
	/// decimos = 6 min (:60). O `BEV_FREEZA2_FINAL_TIMER` (5 min) e o mesmo campo na saga 2 -- la ele
	/// so passa a correr na forma final, que aqui e "o ultimo degrau da ficha".
	/// </summary>
	public double MinutosDeLuta = 6;

	/// <summary>
	/// O ultimato so comeca a valer quando o chefe chega ao ULTIMO degrau da ficha? E a saga 2, em que
	/// o `s2_deadline` so e armado dentro do `freeza_transform` na forma final (BossEvents.dm:672-673).
	/// </summary>
	public bool UltimatoSoNoUltimoDegrau;

	public string AnuncioDoUltimato = "";

	/// <summary>
	/// (2026-10-17) A ESFERA DO ULTIMATO (`"ataqueDoUltimato"`): a Supernova (o padrao) ou a Death Ball. Os prazos e a regra sao os
	/// mesmos -- ver <see cref="Jandirus.Core.Npc.AtaqueDoUltimato"/>. Nulo = um nome que nao existe (acusado no boot).
	/// </summary>
	public AtaqueDoUltimato? AtaqueDoUltimato = Jandirus.Core.Npc.AtaqueDoUltimato.Supernova;

	/// <summary>
	/// (2026-10-18) A TROPA DESTE ELO (`"tropa"`) -- os soldados que o chefe espalha pelo planeta enquanto esta nele. Nulo = o elo nao traz
	/// ninguem (todos menos o do Freeza em Namek, hoje). Ver <see cref="TropaDaSaga"/>.
	/// </summary>
	public TropaDaSaga? Tropa;
}

/// <summary>
/// (2026-10-18) A TROPA QUE UM ELO TRAZ. O pedido do dono, em resumo: com o Freeza em Namek, muitos soldados NPC de raca alienigena pelo
/// planeta, atacando os Namekuseijins e pegando as Esferas do Dragao do chao -- que so voltam derrotando quem as pegou --, de armadura
/// Saiyajin / do exercito do Freeza.
///
/// DADO, E NAO CODIGO, como o resto da cadeia: o bloco `"tropa"` de um elo no `npcs.json`. Quem poe os soldados no mundo, os dirige e os
/// tira e o `GameServer.TropaDaSaga.cs`. A armadura nao mora aqui: o molde e da raca `Alien`, e a tabela de roupa ja veste essa raca com a
/// armadura de batalha do exercito (`RoupaDeNpc`, o `case "Alien"`).
///
/// DIVERGENCIA DECLARADA: o DM nao tem tropa -- la a saga de Namek e o Freeza sozinho (`spawn_freeza_namek`, BossEvents.dm).
/// </summary>
public sealed class TropaDaSaga
{
	/// <summary>O molde dos soldados. Tem que ser do tipo <see cref="TipoDeNpc.Soldado"/>.</summary>
	public string Molde = "";

	/// <summary>Quantos de pe ao mesmo tempo, no maximo, enquanto o chefe esta no planeta.</summary>
	public int Quantos = 12;

	/// <summary>Quantos desembarcam JUNTOS com o chefe; o resto chega um a um, a cada <see cref="SegundosEntre"/>.</summary>
	public int ChegamJuntos = 6;

	/// <summary>Segundos entre um soldado e o proximo depois do desembarque -- e a reposicao de quem caiu.</summary>
	public double SegundosEntre = 20;

	/// <summary>A RACA que eles cacam -- o povo do planeta ("Namekian"). Vazio = so as esferas e quem os ataca.</summary>
	public string Caca = "";

	/// <summary>O que o planeta ouve no desembarque. Vazio = calado.</summary>
	public string AnuncioDaChegada = "";

	/// <summary>O que o planeta ouve quando a tropa vai embora (o chefe caiu, a saga acabou). Vazio = calado.</summary>
	public string AnuncioDaPartida = "";
}

/// <summary>
/// (2026-10-24) OS CORPOS DA TROPA DO FREEZA -- o dono: *"os aliens soldados dele deveriam usar esses sprites base [...] sendo aleatorio o
/// sprite escolhido ao spawnar cada npcs soldado"*. Seis folhas da pasta dos aliens, e cada soldado com uma, SORTEADA quando ele nasce.
///
/// ============================ POR QUE UMA FAIXA RESERVADA, E NAO A LISTA DO ALIEN ============================
/// As seis nao estao na lista de corpos do Alien do catalogo (`visual.json`), e nao entram nela: aquela lista e a da TELA DE CRIACAO (o
/// jogador passaria a escolher a folha do soldado sem ninguem ter pedido) e tambem a do sorteio do POVO Alien (o cidadao comum nasceria com
/// a cara da tropa). Elas viajam numa faixa reservada do `Appearance.Corpo` (<see cref="PrimeiroCorpo"/>, 211 a 216), pelo caminho do Guru
/// (<see cref="Patriarca.Corpo"/>, 202) e da 18 (<see cref="Androides.CorpoDa18"/>, 203): o servidor escreve o indice depois do saneamento
/// (`GameServer.AparenciaDeNpc`), e o `VisualCatalog.CorpoSprite` o devolve como folha nos dois lados do fio.
///
/// QUEM GANHA: o molde que crava o corpo <see cref="NomeNoMolde"/> (o `"corpo"` do `soldado_do_freeza` no `npcs.json`) -- a regra e do
/// molde, como a folha da 18, e nao do tipo `soldado`. A raca continua `Alien` (a armadura do exercito vem dela, `RoupaDeNpc`), e os olhos
/// de gente continuam por cima: nas seis folhas eles caem em cima dos olhos desenhados, como no corpo do Konatsu (conferido quadro a quadro
/// em 2026-10-24).
///
/// AS FALTAS DE ARTE, CONHECIDAS: a `Alien 1` nao tem `blast_*` (o raio cai no soco, pela escada do `CharacterVisual`) e a `CavemanAlien`
/// nao tem `flight_mov_*` (o voo cai no `flight_*` dela). Nenhuma das seis tem `train_*` -- e soldado nao treina.
/// ===========================================================================================================
/// </summary>
public static class FolhasDaTropa
{
	/// <summary>O `"corpo"` que o molde crava pra ganhar uma das seis. Nao e o nome de folha nenhuma: e a chave da faixa.</summary>
	public const string NomeNoMolde = "Tropa do Freeza";

	/// <summary>
	/// 211 A 216: longe da saga do Majin (101 a 104), do Cell Jr (201), do Guru (202) e da 18 (203) -- um indice por folha, e nenhum com
	/// dois sentidos.
	/// </summary>
	public const int PrimeiroCorpo = 211;

	/// <summary>As seis folhas, na ordem em que o dono as listou.</summary>
	public static readonly string[] Folhas =
	[
		"res://Assets/Sprites/Character Icons/Aliens/Alien 9.tres",
		"res://Assets/Sprites/Character Icons/Aliens/Alien 4.tres",
		"res://Assets/Sprites/Character Icons/Aliens/Alien 8.tres",
		"res://Assets/Sprites/Character Icons/Aliens/Alien 1.tres",
		"res://Assets/Sprites/Character Icons/Aliens/Race Kui.tres",
		"res://Assets/Sprites/Character Icons/Aliens/CavemanAlien.tres",
	];

	/// <summary>Este indice de corpo e um dos da tropa?</summary>
	public static bool EhCorpoDaTropa(int corpo) => corpo >= PrimeiroCorpo && corpo < PrimeiroCorpo + Folhas.Length;

	/// <summary>A folha deste indice de corpo, ou nula quando ele nao e da tropa.</summary>
	public static string? FolhaDoCorpo(int corpo) => EhCorpoDaTropa(corpo) ? Folhas[corpo - PrimeiroCorpo] : null;

	/// <summary>O indice de UMA das seis, pelo sorteador que o nascimento passa (o da semente do lugar do soldado).</summary>
	public static int Sortear(Random r) => PrimeiroCorpo + r.Next(Folhas.Length);
}

/// <summary>O estado VIVO de um chefe dentro de um elo. Vai pro disco.</summary>
public sealed class EstadoDoChefe
{
	public string Molde = "";
	public FaseDoChefe Fase = FaseDoChefe.ACaminho;

	/// <summary>Dias in-game que faltam pra ele chegar. Zero ou menos = chega na proxima virada.</summary>
	public int DiasRestantes;

	/// <summary>
	/// ============================ O BP PINADO, DEGRAU A DEGRAU ============================
	/// Resolvido **uma vez, no disparo do marco**, e e este vetor -- e nao o `npcs.json` -- que o
	/// nascimento le, dias in-game depois, e que todo renascimento por reinicio le de novo.
	///
	/// E o `boss_seed_bp` do original (BossEvents.dm:186), com uma diferenca que o port precisa: la o
	/// numero e um `#define`, entao "pinado" so significa *nao deixar o `NPCTicker` empurrar durante a
	/// luta*. Aqui um molde pode declarar <see cref="MoldeDeNpc.BpRelativo"/> -- poder relativo a
	/// MEDIA de quem esta online --, e nesse caso "quando" a conta e feita e a diferenca entre um
	/// evento e uma piada: entre o anuncio e a chegada passam de 2 a 7 dias in-game, tempo de sobra
	/// pra o servidor inteiro treinar e pra tres pessoas mais fortes entrarem.
	///
	/// Vazio = o molde nao foi pinado (nao deveria acontecer; ver `Sagas.Pinar`).
	/// ==================================================================================
	/// </summary>
	public double[] Bps = [];

	/// <summary>Em que degrau da ficha ele esta. Sobrevive ao reinicio -- o `s2_form`/`s3_cell_form`.</summary>
	public int Degrau;

	/// <summary>Quando ele volta do alem, em segundos do relogio do mundo. Zero = nao esta voltando.</summary>
	public double RessurgeEm;

	/// <summary>
	/// (2026-10-18) QUANTO FALTAVA PRA ELE VOLTAR DO ALEM quando o estado foi gravado, em segundos. O <see cref="RessurgeEm"/> e do relogio
	/// do mundo, que NAO atravessa o reinicio (ele recomeca do zero a cada subida do servidor): sem isto o Cell que voltaria em 60 s, gravado
	/// com o relogio em uma hora de jogo, so voltaria uma hora depois do boot. Escrito no `SalvarSagas`; lido no `RetomarSagas`.
	/// </summary>
	public double RessurgeFaltamS;

	/// <summary>
	/// (2026-10-19) ELE FOI PRO TORNEIO DELE (`ChefeDaSaga.TorneioNoDegrau`): a caminho, ele nasce NA ARENA da Terra e espera os desafiantes;
	/// voltando do alem, encerra o torneio e apaga a marca. Vai pro disco: o torneio marcado atravessa o reinicio.
	/// </summary>
	public bool NoTorneio;

	/// <summary>Id do corpo no `_players`. **NAO PERSISTE** -- corpo nao atravessa reinicio.</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public int Corpo;

	/// <summary>Ja entrou em combate alguma vez? Arma o prazo de luta. Nao persiste (o `s1_engaged` e `tmp`).</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public bool Engajou;

	/// <summary>Quando o ultimato vence, em segundos do relogio do mundo. Nao persiste.</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public double UltimatoEm;

	/// <summary>
	/// Esta na carga do ultimato -- no DM os 30 s em que nocautea-lo INTERROMPE (`BEV_PD_CHARGE`, BossEvents.dm:29); desde
	/// a v0.3.0, a SUPERNOVA (`Npc.Supernova`), com o chefe intocavel ate a explosao. Nao persiste: uma carga cortada por
	/// reinicio nao pode voltar pela metade.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public bool Carregando;
}

/// <summary>O estado VIVO de um elo. Vai pro disco.</summary>
public sealed class EstadoDoElo
{
	public string Id = "";

	/// <summary>
	/// O MARCO JA DISPAROU? Latch, e **um so por elo** -- o `m1_fired` do original. Ele e separado da
	/// <see cref="Fase"/> porque as duas respondem coisas diferentes: "alguem ja chegou naquele BP" e
	/// "a saga esta em que pe". Um servidor cujo elo 1 foi cancelado (planeta destruido) nao pode
	/// re-disparar o marco quando o proximo jogador passar de 20 mil.
	/// </summary>
	public bool Disparou;

	public FaseDaSaga Fase = FaseDaSaga.Adormecida;
	public List<EstadoDoChefe> Chefes = [];

	/// <summary>
	/// O PLANETA FOI CONDENADO pelo gancho inerte de destruicao. Persiste porque o dia em que a
	/// destruicao existir, ela vai querer saber quais planetas ja foram sentenciados -- e porque a
	/// bancada precisa poder afirmar "o gancho disparou" sem que nada no mundo tenha mudado.
	/// </summary>
	public bool Condenado;

	/// <summary>
	/// (2026-10-18) QUEM JA BATEU O MARCO DESTE ELO. Escrito na primeira vez que alguem passa do marco -- mesmo com a cadeia esperando a saga
	/// anterior -- e pela auditoria do boot, que procura no DISCO quem ja tinha passado dele (o jogo de antes so olhava quem estava online).
	/// Com ele o elo dispara NA VEZ DELE mesmo com essa pessoa fora do jogo. O dono: *"caso ... tenham cumprido o requisito dela ela
	/// inicie"*. Vazio = ninguem bateu ainda.
	/// </summary>
	public string MarcoBatidoPor = "";

	/// <summary>(2026-10-18) O BP de quem bateu o marco, no instante em que bateu -- so pro registro do console.</summary>
	public double MarcoBatidoComBp;
}

/// <summary>Tudo o que a cadeia guarda entre reinicios. E o savefile "BossEvents" do original.</summary>
public sealed class EstadoDasSagas
{
	public List<EstadoDoElo> Elos = [];

	/// <summary>
	/// O ULTIMO DIA IN-GAME QUE A CADEIA VIU. E o `last_day_seen` (BossEvents.dm:329), e ele PERSISTE
	/// aqui enquanto la e `tmp`.
	///
	/// Faz diferenca de verdade: o relogio do mundo deste port sai do UTC (ver `TempoDoMundo`), entao
	/// ele **anda com o servidor desligado**. Sem gravar o dia, um servidor que passa a noite fora
	/// voltaria com `last_day_seen = hoje` e engoliria as viradas que aconteceram -- a nave de Freeza
	/// nunca chegaria em servidor que reinicia todo dia. Com o dia gravado, as viradas perdidas sao
	/// aplicadas na volta, que e o que "contagem em dias in-game" quer dizer.
	/// </summary>
	public long UltimoDiaVisto = -1;

	/// <summary>
	/// (2026-10-18) O VILAO QUE ESTAVA NO INFERNO quando o estado foi gravado -- nulo = nenhum. Ele volta no boot com o tempo que lhe faltava
	/// (`GameServer.RetomarOVilaoDoInferno`). O dono: *"se o freeza boss spawnar mas fechar o server ele volta? se n ele deveria voltar na
	/// forma q ele estava (isso pra todos os chefes n so o freeza)"*.
	/// </summary>
	public VilaoGuardado? VilaoDoInferno;

	public EstadoDoElo? Do(string id) =>
		Elos.Find(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// (2026-10-18) O VILAO DO INFERNO GRAVADO: o que ele precisa pra voltar IGUAL depois de um reinicio -- o molde, o elo de onde veio, o
/// degrau (a forma em que caiu), o BP pinado no nascimento (a media de AGORA, no boot, e outra -- quase sempre a de ninguem online), o premio
/// na bolsa e o tempo que lhe faltava pra afundar.
/// </summary>
public sealed class VilaoGuardado
{
	public string Molde = "", Elo = "";
	public int Degrau;
	public double[] Bps = [];
	public double Zeni;
	public long RestanteMs;
}

/// <summary>
/// A CADEIA DE SAGAS: os marcos, a ordem, e o BP que nao se mexe depois de decidido.
///
/// ============================ AS DUAS REGRAS QUE IMPEDEM O EVENTO DE VIRAR PIADA ============================
/// A especificacao nomeia as duas, e as duas moram aqui:
///
///   1. **O BP DO CHEFE E FIXADO QUANDO A SAGA COMECA** -- <see cref="Pinar"/>. Quem chegar depois nao
///      empurra a dificuldade pra cima; o numero anunciado e o numero que pousa.
///   2. **A SAGA SO COMECA QUANDO A ANTERIOR TERMINOU** -- <see cref="PodeComecar"/>. O marco de BP
///      continua valendo e ESPERA a vez; ele nao se perde.
/// ========================================================================================================
///
/// ============================ O QUE FICA DE FORA, POR DECISAO DO DONO ============================
/// O *Hell Boss Rush* (`HellBossRush.dm`) e as masmorras (`dungeon_obj.dm`). Existem no original,
/// funcionavam mal la, e nao entram -- nem os arquivos nem os itens de catalogo que os acompanham.
/// "Mais chefes" recai inteiramente sobre esta cadeia, e por isso ela e dado: acrescentar chefe e
/// acrescentar ELO, e nao inventar um segundo sistema.
/// ============================================================================================
/// </summary>
public static class Sagas
{
	/// <summary>
	/// LE A SECAO `sagas` do `npcs.json`. Ausente = cadeia vazia (nenhum evento), e nao uma cadeia
	/// embutida no codigo -- mesma disciplina do <see cref="Povoamento.Ler"/>: um padrao escondido
	/// faria o dono apagar a secao e continuar vendo Freeza cair do ceu sem entender de onde vem.
	/// </summary>
	public static EloDaSaga[] Ler(JsonDocument doc)
	{
		if (!doc.RootElement.TryGetProperty("sagas", out JsonElement lista)
			|| lista.ValueKind != JsonValueKind.Array) return [];

		var l = new List<EloDaSaga>();
		foreach (JsonElement e in lista.EnumerateArray())
		{
			var elo = new EloDaSaga
			{
				Id = Txt(e, "id"),
				Nome = Txt(e, "nome"),
				Planeta = Txt(e, "planeta"),
				GatilhoBp = Num(e, "gatilhoBp", 0),
				GatilhoRaca = Txt(e, "gatilhoRaca"),
				AnuncioDoMarco = Txt(e, "anuncioDoMarco"),
				AnuncioDiario = Txt(e, "anuncioDiario"),
				AnuncioDaVitoria = Txt(e, "anuncioDaVitoria"),
				AnuncioDoUltimato = Txt(e, "anuncioDoUltimato"),
				DestroiPlaneta = Bit(e, "destroiPlaneta", false),
				MinutosDeOcio = Num(e, "minutosDeOcio", 18),
				MinutosDeLuta = Num(e, "minutosDeLuta", 6),
				UltimatoSoNoUltimoDegrau = Bit(e, "ultimatoSoNoUltimoDegrau", false),
				AtaqueDoUltimato = Supernova.AtaquePorNome(Txt(e, "ataqueDoUltimato")),
			};

			if (e.TryGetProperty("chefes", out JsonElement chefes) && chefes.ValueKind == JsonValueKind.Array)
			{
				var cl = new List<ChefeDaSaga>();
				foreach (JsonElement c in chefes.EnumerateArray())
					cl.Add(new ChefeDaSaga
					{
						Molde = Txt(c, "molde"),
						DiasMin = (int)Num(c, "diasMin", 0),
						DiasMax = (int)Num(c, "diasMax", Num(c, "diasMin", 0)),
						Recompensa = Num(c, "recompensa", 100),
						Absorve = Bit(c, "absorve", false),
						RessurgeNoDegrau = (int)Num(c, "ressurgeNoDegrau", 0),
						RessurgeEmSegundos = Num(c, "ressurgeEmSegundos", 60),
						TorneioNoDegrau = (int)Num(c, "torneioNoDegrau", 0),
						DiasAteOTorneio = (int)Num(c, "diasAteOTorneio", 5),
						AnuncioDaChegada = Txt(c, "anuncioDaChegada"),
						AnuncioDaQueda = Txt(c, "anuncioDaQueda"),
					});
				elo.Chefes = [.. cl];
			}

			// (2026-10-18) A TROPA -- ver `TropaDaSaga`. Ausente = o elo nao traz soldado nenhum.
			if (e.TryGetProperty("tropa", out JsonElement tr) && tr.ValueKind == JsonValueKind.Object)
				elo.Tropa = new TropaDaSaga
				{
					Molde = Txt(tr, "molde"),
					Quantos = (int)Num(tr, "quantos", 12),
					ChegamJuntos = (int)Num(tr, "chegamJuntos", 6),
					SegundosEntre = Num(tr, "segundosEntre", 20),
					Caca = Txt(tr, "caca"),
					AnuncioDaChegada = Txt(tr, "anuncioDaChegada"),
					AnuncioDaPartida = Txt(tr, "anuncioDaPartida"),
				};

			if (elo.Id.Length > 0) l.Add(elo);
		}
		return [.. l];
	}

	/// <summary>
	/// ============================ ESTE ELO PODE COMECAR? ============================
	/// **A CADEIA E SEQUENCIAL.** O `check_triggers` do original escreve isso como uma conjuncao por
	/// linha (`if(!m2_fired && saga1_over() && ...)`, BossEvents.dm:376-378), com um proc `sagaN_over`
	/// pra cada elo. Aqui e uma funcao sobre o INDICE, e por isso um elo novo no meio da lista herda a
	/// regra sem ninguem escrever a linha dele.
	///
	/// O marco de BP nao se perde enquanto espera: ele continua sendo conferido a cada volta e dispara
	/// no instante em que o elo anterior fecha.
	/// ============================================================================
	/// </summary>
	public static bool PodeComecar(EloDaSaga[] cadeia, EstadoDasSagas estado, int indice)
	{
		if (indice <= 0) return true;
		EstadoDoElo? anterior = estado.Do(cadeia[indice - 1].Id);
		return anterior != null && Terminou(anterior.Fase);
	}

	/// <summary>Terminou = o vilao caiu, o vilao venceu, ou o elo foi cancelado. Os tres liberam o proximo.</summary>
	public static bool Terminou(FaseDaSaga f) =>
		f is FaseDaSaga.Vencida or FaseDaSaga.Consumada or FaseDaSaga.Cancelada;

	/// <summary>
	/// ============================ O BP FIXADO, E ESTE E O UNICO LUGAR QUE O DECIDE ============================
	/// Chamada UMA VEZ, no disparo do marco, com a media do servidor DAQUELE instante. O vetor que sai
	/// daqui e gravado no save e e o que o nascimento le -- dias in-game depois, e de novo a cada
	/// reinicio.
	///
	/// Pra ficha de BP constante (os quatro chefes do DM) o resultado e o proprio numero da ficha, e
	/// pinar parece nao fazer nada. Pra ficha com <see cref="MoldeDeNpc.BpRelativo"/> -- poder relativo
	/// a quem esta online -- e a diferenca entre "Freeza chega com 530 mil, como foi anunciado" e
	/// "Freeza chega com o que a media do servidor virou depois de cinco dias de treino de todo mundo".
	///
	/// A bancada prova pelo segundo caso, ligando `bpRelativo` no molde de producao e multiplicando a
	/// media entre o disparo e a chegada: o corpo pousa com o numero PINADO. Sem essa metade, "o BP e
	/// fixado" seria um comentario -- que e a armadilha 1 da PARTE 3.
	/// ======================================================================================================
	/// </summary>
	public static double[] Pinar(MoldeDeNpc molde, double mediaDoServidor)
	{
		var bps = new double[Math.Max(molde.Estagios.Length, 1)];
		for (int i = 0; i < molde.Estagios.Length; i++)
		{
			double fixo = molde.Estagios[i].Bp;

			// O `NPCTicker` base (NPClist.dm:72) escala o bicho pela media dos jogadores, e o
			// `Bosses.dm:19` faz o mesmo pra cima. Quem quer isso num chefe declara `bpRelativo`; o
			// piso continua sendo o degrau escrito, pra um servidor vazio nao produzir um Freeza de 0.
			double relativo = molde.BpRelativo > 0 ? mediaDoServidor * molde.BpRelativo : 0;
			bps[i] = Math.Max(fixo, relativo);
		}
		if (molde.Estagios.Length == 0) bps[0] = Math.Max(molde.BpMax, molde.BpPiso);
		return bps;
	}

	/// <summary>
	/// O QUE HA DE CONTRADITORIO NA CADEIA. Mesma disciplina do <see cref="MoldeDeNpc.Problemas"/> e
	/// do <see cref="Povoamento.Problemas"/>: o arquivo e editado a mao, e uma cadeia incoerente falha
	/// CALADA -- o servidor roda anos e a saga simplesmente nunca acontece.
	/// </summary>
	public static List<string> Problemas(EloDaSaga[] cadeia, CatalogoDeMoldes moldes)
	{
		var p = new List<string>();
		var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (EloDaSaga e in cadeia)
		{
			if (!vistos.Add(e.Id)) p.Add($"saga '{e.Id}': id repetido -- o estado das duas colidiria no save");
			if (e.Planeta.Length == 0) p.Add($"saga '{e.Id}': sem planeta");
			if (e.GatilhoBp <= 0) p.Add($"saga '{e.Id}': sem gatilhoBp -- ela dispararia com qualquer BP");
			if (e.Chefes.Length == 0) p.Add($"saga '{e.Id}': nenhum chefe -- ela comecaria e terminaria no mesmo tique");

			foreach (ChefeDaSaga c in e.Chefes)
			{
				MoldeDeNpc? m = moldes.Get(c.Molde);
				if (m == null) { p.Add($"saga '{e.Id}': molde '{c.Molde}' nao existe"); continue; }

				// O RECORTE DO DONO CORTA PELO TIPO, e uma saga que citasse um molde de cidadao faria
				// o povo do planeta virar chefe de evento. Chefe e quem tem ficha pronta.
				if (m.Tipo != TipoDeNpc.Chefe)
					p.Add($"saga '{e.Id}': o molde '{c.Molde}' e do tipo '{m.Tipo}' -- chefe de saga "
						+ "precisa de ficha pronta (`estagios`), senao o BP dele seria sorteado.");

				if (c.DiasMin < 0 || c.DiasMax < c.DiasMin)
					p.Add($"saga '{e.Id}' / '{c.Molde}': faixa de dias invalida ({c.DiasMin}..{c.DiasMax})");

				if (c.RessurgeNoDegrau > m.Estagios.Length)
					p.Add($"saga '{e.Id}' / '{c.Molde}': ressurgeNoDegrau {c.RessurgeNoDegrau} passa dos "
						+ $"{m.Estagios.Length} degraus da ficha -- ele nunca voltaria.");

				// (2026-10-19) O TORNEIO PRECISA DE DEGRAU PRA ACONTECER, e a volta Super Perfeita e o que o encerra: sem ela o torneio nunca acabaria
				// pela vitoria de alguem (so pelo Kamehameha).
				if (c.TorneioNoDegrau > m.Estagios.Length)
					p.Add($"saga '{e.Id}' / '{c.Molde}': torneioNoDegrau {c.TorneioNoDegrau} passa dos {m.Estagios.Length} degraus da ficha");
				if (c.TorneioNoDegrau > 0 && c.RessurgeNoDegrau != c.TorneioNoDegrau)
					p.Add($"saga '{e.Id}' / '{c.Molde}': torneioNoDegrau {c.TorneioNoDegrau} sem ressurgeNoDegrau no mesmo degrau -- nocauteado no x1 "
						+ "ele morreria de vez, sem a volta que encerra o torneio");
				if (c.TorneioNoDegrau > 0 && c.DiasAteOTorneio < 1)
					p.Add($"saga '{e.Id}' / '{c.Molde}': diasAteOTorneio {c.DiasAteOTorneio} -- o torneio e marcado pra pelo menos um dia depois");

				// ABSORVER PRECISA DE QUEM ABSORVER, e de degrau pra onde subir.
				if (c.Absorve && e.Chefes.Length < 2)
					p.Add($"saga '{e.Id}' / '{c.Molde}': absorve=true e ele e o unico chefe do elo");
				if (c.Absorve && m.Estagios.Length < 2)
					p.Add($"saga '{e.Id}' / '{c.Molde}': absorve=true e a ficha tem um degrau so");
			}

			// O ULTIMATO SO EXISTE PRA QUEM DESTROI. Um prazo sem consequencia venceria calado.
			if (!e.DestroiPlaneta && e.AnuncioDoUltimato.Length > 0)
				p.Add($"saga '{e.Id}': tem anuncio de ultimato e destroiPlaneta=false -- o prazo venceria calado");
			if (e.AtaqueDoUltimato == null)
				p.Add($"saga '{e.Id}': ataqueDoUltimato desconhecido -- vale 'supernova' ou 'deathball'");

			// (2026-10-18) A TROPA CITA UM MOLDE DE SOLDADO. Um molde de cidadao aqui poria o povo do planeta pra cacar o proprio povo; um
			// que nao existe faria a saga chegar sem ninguem, calada.
			if (e.Tropa is { } t)
			{
				MoldeDeNpc? mt = moldes.Get(t.Molde);
				if (mt == null) p.Add($"saga '{e.Id}': a tropa cita o molde '{t.Molde}', que nao existe");
				else if (mt.Tipo != TipoDeNpc.Soldado)
					p.Add($"saga '{e.Id}': o molde da tropa '{t.Molde}' e do tipo '{mt.Tipo}' -- tropa e do tipo 'soldado'");
				if (t.Quantos <= 0 || t.ChegamJuntos < 0 || t.ChegamJuntos > t.Quantos)
					p.Add($"saga '{e.Id}': tropa com quantos={t.Quantos} e chegamJuntos={t.ChegamJuntos} -- precisa de 0 <= chegamJuntos <= quantos e quantos > 0");
				if (t.SegundosEntre <= 0) p.Add($"saga '{e.Id}': tropa com segundosEntre={t.SegundosEntre} -- precisa ser positivo");
			}
		}
		return p;
	}

	private static string Txt(JsonElement e, string chave) =>
		e.TryGetProperty(chave, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

	private static double Num(JsonElement e, string chave, double padrao) =>
		e.TryGetProperty(chave, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : padrao;

	private static bool Bit(JsonElement e, string chave, bool padrao) =>
		e.TryGetProperty(chave, out JsonElement v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
			? v.GetBoolean() : padrao;
}
