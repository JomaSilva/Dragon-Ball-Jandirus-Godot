namespace Jandirus.Core.World;

/// <summary>
/// A CHAVE DE UM PLANETA -- e ela **nao e o nome**.
///
/// ============================ POR QUE O DM NAO SERVE DE MODELO AQUI ============================
/// No original a lista de mortos e `PlanetDisableList += P.planetType` (`Area_Death.dm:144`), ou
/// seja: uma lista de STRINGS. Isso funciona la porque todo planeta do DM tem mapa proprio e nome
/// unico -- sao sete.
///
/// Aqui o nome de um planeta procedural sai de
/// `$"{NomeDeBioma(seed)}-{|Sx| % 1000}{|Sy| % 1000}{k}"` (<see cref="SistemaSolar.Planeta"/>), e
/// ele **nao e unico** por duas razoes independentes: o `% 1000` colide a cada mil celulas, e a
/// concatenacao e ambigua (`Sx=1,Sy=234` e `Sx=12,Sy=34` produzem a mesma string). Copiar a lista
/// por nome mataria planetas inteiros do outro lado da galaxia, calado -- exatamente a familia de
/// defeito que a regra 0.2 existe pra evitar.
///
/// Entao a chave e a SEED do planeta (`PlanetaNoEspaco.Seed`, derivada de
/// `Misturar(H ^ SalDoPlaneta, k, 0)` -- uma por orbita, unica por construcao). Pre-feito continua
/// por nome, que la e unico e legivel no save.
/// ==========================================================================================
/// </summary>
public readonly record struct ChaveDePlaneta(bool PreFeito, string Nome, ulong Seed)
{
	/// <summary>A chave de um corpo do mapa do universo.</summary>
	public static ChaveDePlaneta De(PlanetaNoEspaco p) =>
		p.Premade ? new ChaveDePlaneta(true, p.Nome, 0) : new ChaveDePlaneta(false, p.Nome, p.Seed);

	/// <summary>
	/// A chave da SUPERFICIE em que alguem esta -- nulo quando o lugar nao e um planeta.
	///
	/// O crivo e o <see cref="Espaco.EhPlaneta"/>, que ja e a definicao unica de "planeta" das duas
	/// pontas: o espaco, a Sala do Tempo, o Inferno e o interior de uma nave nao morrem.
	/// </summary>
	public static ChaveDePlaneta? Da(ZoneKey z)
	{
		if (!Espaco.EhPlaneta(z)) return null;
		return z.Kind == ZoneKey.KindPremade
			? new ChaveDePlaneta(true, z.Name, 0)
			: new ChaveDePlaneta(false, z.Name, z.Seed);
	}

	/// <summary>
	/// A chave em TEXTO -- e o que vira campo do JSON e chave do dicionario.
	///
	/// O `#` na frente do procedural nao e enfeite: sem ele um pre-feito que alguem chamasse de
	/// "1234" colidiria com a seed 1234. O prefixo faz os dois espacos de nome nunca se tocarem.
	/// </summary>
	public string Texto => PreFeito ? Nome : $"#{Seed}";

	/// <summary>O caminho de volta, pra ler o save. Devolve falso pra texto que nao e chave.</summary>
	public static bool Ler(string texto, string nome, out ChaveDePlaneta chave)
	{
		if (texto.Length > 1 && texto[0] == '#' && ulong.TryParse(texto[1..], out ulong s))
		{
			chave = new ChaveDePlaneta(false, nome, s);
			return true;
		}
		if (texto.Length > 0 && texto[0] != '#')
		{
			chave = new ChaveDePlaneta(true, texto, 0);
			return true;
		}
		chave = default;
		return false;
	}

	/// <summary>
	/// A ZONA a partir da chave em texto mais o nome legivel -- o caminho de volta completo.
	///
	/// Ele mora aqui, e nao dentro de quem guarda a chave, porque **ha mais de um guardiao**: o
	/// <see cref="EstadoDaMorte"/> (a condenacao, que persiste) e a <see cref="FeridaDeMundo"/> (o
	/// dano de ki num mundo vivo, que nao persiste). Escrever as duas linhas nos dois lugares seria
	/// ter duas nocoes do endereco de um planeta -- que e exatamente a coisa que a
	/// <see cref="ChaveDePlaneta"/> existe pra nao deixar acontecer.
	///
	/// O nome vem de fora porque a chave de um mundo gerado e a SEED: ela identifica, mas nao
	/// enderece sozinha (a `ZoneKey` dele e o par nome+seed).
	/// </summary>
	public static ZoneKey ZonaDe(string chave, string nome) =>
		chave.Length > 1 && chave[0] == '#' && ulong.TryParse(chave[1..], out ulong s)
			? ZoneKey.Procedural(nome, s)
			: ZoneKey.Premade(nome);
}

/// <summary>
/// EM QUE PONTO DA MORTE UM PLANETA ESTA.
///
/// O DM guarda isto em TRES vars de area (`Area_Death.dm:5-7`) -- `death_proc_running` (tmp),
/// `planet_dying` (persistente) e `planet_death_stage` (tmp) --, e e dessa separacao que nasce a
/// armadilha registrada na memoria do projeto: `planet_dying` volta do disco e o estagio nao, entao
/// o tique de `Weather.dm:72-74` **retoma a morte lenta do estagio 0 a cada boot**. O planeta ficava
/// preso num pavio eterno.
///
/// Aqui e UM enum e UM registro, gravados juntos. Ver <see cref="EstadoDaMorte"/>.
/// </summary>
public enum FaseDaMorte : byte
{
	/// <summary>Vivo. Nao aparece no registro -- ausencia e a resposta.</summary>
	Vivo = 0,

	/// <summary>O pavio lento: os quatro estagios do `Planet_Death`.</summary>
	Morrendo = 1,

	/// <summary>Os ~5 min de tremor e explosao do `DestroyPlanet`, antes do commit.</summary>
	Explodindo = 2,

	/// <summary>Morto. `isDestroyed = 1` + entrada na `PlanetDisableList`.</summary>
	Destruido = 3,
}

/// <summary>
/// O ESTADO DE MORTE DE UM PLANETA -- **e ele e UM registro, de proposito**.
///
/// ============================ O QUE PERSISTE E O QUE NAO PERSISTE, ESCRITO ============================
/// Tudo o que esta nesta classe vai pro disco JUNTO, num arquivo so, numa escrita so. Nao ha campo
/// "tmp" aqui, e essa e a correcao do defeito do original: la o "esta morrendo" persistia e o
/// "em que estagio" nao, e o boot seguinte reconstruia um pavio novo.
///
/// O que **nao** persiste, e nao esta aqui porque nao e estado:
///   * o efeito de clima (`ForcarClima` remonta a partir da fase);
///   * o relogio de 3-11 s entre um tremor e outro (`Area_Death.dm:96`) -- e cadencia visual, e um
///     tremor a mais ou a menos depois de um reinicio nao muda resultado de jogo nenhum;
///   * quem estava na zona (a evacuacao acontece no commit e le a zona de entao).
/// =============================================================================================
/// </summary>
public sealed class EstadoDaMorte
{
	/// <summary>A chave em texto -- ver <see cref="ChaveDePlaneta.Texto"/>.</summary>
	public string Chave = "";

	/// <summary>O nome legivel, so pro anuncio e pro log. NAO e a identidade.</summary>
	public string Nome = "";

	public FaseDaMorte Fase = FaseDaMorte.Vivo;

	/// <summary>0 a 3 -- o `planet_death_stage` do DM, e o que decide a chance do `limit_life()`.</summary>
	public int Estagio;

	/// <summary>
	/// QUANTO FALTA DO PASSO ATUAL, em segundos.
	///
	/// SEGUNDOS QUE FALTAM, e nao um instante absoluto do relogio do universo. A diferenca importa:
	/// o relogio do mundo anda com o servidor DESLIGADO (ele e o relogio de parede, ver
	/// `GameServer.TempoDoMundo`), e um prazo absoluto faria uma noite de servidor fora consumir o
	/// pavio inteiro -- o planeta explodiria sozinho, que e exatamente o que o original aprendeu a
	/// nao fazer no Freeza (`BossEvents.dm:562`). Guardando o que FALTA, o pavio retoma de onde
	/// parou, sem reiniciar (o bug do DM) e sem pular (o bug do relogio de parede).
	/// </summary>
	public double Faltam;

	/// <summary>
	/// O `mexpressedBP` -- o BP EXPRESSO de quem matou o planeta, fixado no comeco
	/// (`Planets.dm:342`). E o numero que decide quem sobrevive ao commit, e por isso ele persiste:
	/// se o servidor cair no meio dos cinco minutos, a explosao tem que voltar com a mesma forca.
	/// </summary>
	public double BpDoAlgoz;

	/// <summary>O id de rede de quem causou -- PISTA, nao identidade. Ver `GameServer.Destruicao`.</summary>
	public int IdDoAlgoz;

	/// <summary>Quem/o que causou, pro log e pro anuncio ("saga freeza_vegeta", "Planet Destroy de X").</summary>
	public string Motivo = "";

	/// <summary>
	/// A ZONA deste planeta, reconstruida do registro.
	///
	/// E por isso que <see cref="Nome"/> e gravado alem da <see cref="Chave"/>: a chave de um mundo
	/// procedural e a SEED (a unica identidade honesta, ver <see cref="ChaveDePlaneta"/>), mas a
	/// `ZoneKey` dele e `(nome, seed)` -- o nome nao identifica, e mesmo assim faz parte do endereco.
	/// </summary>
	public ZoneKey Zona() => ChaveDePlaneta.ZonaDe(Chave, Nome);
}

/// <summary>
/// OS NUMEROS DOS DOIS CAMINHOS DE MORTE DE UM PLANETA, tirados do DM linha a linha.
///
/// ============================ OS DOIS CAMINHOS ============================
///   (a) MORTE LENTA -- `area/proc/Planet_Death` (`Area_Death.dm:8-50`). Quatro estagios; a cada um
///       morre uma fracao dos habitantes, e a partir do 2 o chao comeca a se desfazer perto de quem
///       esta olhando. No fim, chama a destruicao.
///   (b) DESTRUICAO IMEDIATA -- o verb `Planet_Destroy` (`Planets.dm:318-370`) + o
///       `area/proc/DestroyPlanet` (`Area_Death.dm:75-147`): 30 s de carga, ~5 min de tremor, e o
///       commit (dano, morte, evacuacao, planeta na lista).
/// ==========================================================================
///
/// Tudo aqui e `const`/`static readonly` e nao ha estado: e o Core dizendo QUANTO, e o servidor
/// decidindo QUANDO.
/// </summary>
public static class MortePlanetaria
{
	// =====================================================================
	// (a) O PAVIO LENTO
	// =====================================================================
	/// <summary>
	/// QUANTO DURA CADA ESTAGIO, em segundos. Os quatro `sleep()` do `Planet_Death`
	/// (`Area_Death.dm:30, 34, 39, 44`): 2000, 3000, 3000 e 4000 DECIMOS.
	///
	/// **Decimos, e a unidade e a armadilha ja registrada na memoria deste projeto**: `sleep(N)` do
	/// BYOND e N/10 segundos, e nao N/`world.fps`. 2000 sao 200 s, nao 166.
	/// </summary>
	public static readonly double[] SegundosDoEstagio = [200, 300, 300, 400];

	/// <summary>O ultimo estagio antes do `goto destroy`. Quatro estagios: 0, 1, 2 e 3.</summary>
	public const int UltimoEstagio = 3;

	/// <summary>O pavio inteiro: 1200 decimos = **20 minutos**.</summary>
	public static double PavioInteiro
	{
		get
		{
			double t = 0;
			foreach (double s in SegundosDoEstagio) t += s;
			return t;
		}
	}

	/// <summary>
	/// A CHANCE DE UM HABITANTE MORRER NESTE ESTAGIO, em porcento -- `prob((planet_death_stage+1)*25)`
	/// (`Area_Death.dm:55`). Da 25 / 50 / 75 / 100: no estagio 3 nao sobra ninguem.
	/// </summary>
	public static double ChanceDeMorrerPct(int estagio) => Math.Clamp((estagio + 1) * 25.0, 0, 100);

	/// <summary>
	/// A PARTIR DE QUE ESTAGIO O CHAO COMECA A SE DESFAZER -- `while(planet_death_stage>=2 ...)`
	/// (`Area_Death.dm:62`).
	/// </summary>
	public const int EstagioQueQuebraOChao = 2;

	/// <summary>
	/// A NOITE PERMANENTE comeca no estagio 1 (`Area_Death.dm:32`), e o efeito real mora no clima
	/// (`Weather.dm:166-167`: `planet_death_stage && < 4` forca `daylightcycle >= 6`).
	/// </summary>
	public const int EstagioDaNoiteEterna = 1;

	// =====================================================================
	// (b) A DESTRUICAO
	// =====================================================================
	/// <summary>`PDESTROY_KI_COST 1000` (`Planets.dm:315`) -- Ki FLAT, cobrado ANTES da confirmacao.</summary>
	public const double KiDaDestruicao = 1000;

	/// <summary>`PDESTROY_BP_PER_GRAV 10000` (`Planets.dm:316`) -- expressedBP exigido por 1x de gravidade.</summary>
	public const double BpPorGravidade = 10000;

	/// <summary>
	/// O BP EXPRESSO EXIGIDO NESTE CHAO -- `expressedBP >= PDESTROY_BP_PER_GRAV * Planetgrav`
	/// (`Planets.dm:326`).
	///
	/// ============================ POR QUE ISTO E UMA FUNCAO E NAO UMA LINHA NO VERB ============================
	/// Ela morava inline no `PlanetDestroy`, e por isso a regra so podia ser medida DE FORA, apertando o
	/// botao e vendo se ele recusa. Isso e caro (obriga a caçar por bisseccao o BP em que o verbo vira) e
	/// e ambiguo: gravidade maior tambem DERRUBA o `expressedBP` (`StatCurves.GravFelt`), entao "recusou
	/// num planeta pesado" nao prova que foi o limiar que subiu -- podia ser o poder que desceu.
	///
	/// Com a conta aqui, a bancada afirma as duas coisas separadas: que o limiar e `10000 x g` (aqui) e
	/// que o verbo obedece a ele (la). Uma casa so, no Core, como manda a regra 0.4.
	///
	/// O `Math.Max(g, 1)` e o piso: no DM a gravidade do espaco e 0 (`Planetgrav = 0`), e sem o piso o
	/// exigido seria ZERO -- ou seja, qualquer um destruiria qualquer coisa em gravidade baixa.
	/// ======================================================================================================
	/// </summary>
	public static double BpExigido(double planetgrav) => BpPorGravidade * Math.Max(planetgrav, 1);

	/// <summary>`sleep(300)` (`Planets.dm:355`) -- os 30 s de carga, a janela pra interromper.</summary>
	public const double SegundosDeCarga = 30;

	/// <summary>
	/// `sleep(3100)` (`Area_Death.dm:129`) -- o comentario do original diz *"five minutes lol"* e
	/// sao **310 s**. O numero literal vence o comentario.
	/// </summary>
	public const double SegundosDeExplosao = 310;

	/// <summary>`sleep(20 + rand(10,90))` (`Area_Death.dm:96`): de 3 a 11 s entre dois tremores.</summary>
	public const double TremorMin = 3, TremorMax = 11;

	/// <summary>A duracao total de (b), do "sim" ate o commit: carga + explosao.</summary>
	public static double DestruicaoInteira => SegundosDeCarga + SegundosDeExplosao;

	// =====================================================================
	// (c) A FURIA DE UM MUNDO -- **UMA formula, dois consumidores**
	// =====================================================================
	/// <summary>
	/// ============================ O PEDIDO DO DONO, LITERAL ============================
	/// *"todos q estao no planeta recebem um dano baseado na gravidade do planeta e tamanho dele"*
	/// e, sobre a vida do planeta sob fogo de ki, *"a 'vida' do planeta segue a mesma formula do dano
	/// dele quando explode, usando o tamanho e a gravidade, entao dano de quando ele explode = vida
	/// do planeta"*.
	///
	/// Sao DUAS regras e UM numero, e por isso ele e uma funcao so. Escrever duas -- uma no commit da
	/// explosao, outra no tiro que vem do espaco -- e o jeito conhecido de as duas divergirem no
	/// primeiro ajuste de balanceamento, e o dono nunca mais conseguir prever nenhuma das duas.
	///
	/// ============================ O QUE ISTO SUBSTITUI ============================
	/// O `DanoDoCommit = 99` do DM (`M.SpreadDamage(99)`, `Area_Death.dm:138`), que era **99 fixo em
	/// cada membro** com o crivo no BP DO ALGOZ. Ele foi DELETADO e nao aposentado: um numero que
	/// ninguem le e um numero que volta a ser lido por engano. Ver o que mudou de verdade no
	/// <see cref="DanoNoCorpo"/>.
	/// ==============================================================================
	///
	/// ============================ AS TRES ARMADILHAS DA ESCALA, E O QUE A FORMA FAZ COM CADA UMA ============================
	/// Medidas antes de a formula ser escrita, e sao elas que decidem a FORMA (nao o gosto):
	///
	///  1. **Nos pre-feitos o tamanho nao varia.** Os 7 mapas destrutiveis sao TODOS 500x500 tiles
	///     (medido no `manifest.json`), entao Earth, Namek, Arconia e Makyo_Star -- mesmo lado, mesma
	///     gravidade 1 -- tem a MESMA furia. Isso nao e defeito da formula: eles sao de fato o mesmo
	///     tamanho e o mesmo peso, e inventar um numero pra diferencia-los seria inventar dado.
	///
	///  2. **Nos gerados, tamanho e gravidade sao UM sorteio contado duas vezes.** O lado sai da
	///     gravidade por interpolacao direta (<see cref="MundoProcedural.LadoDaGravidade"/>), entao
	///     `lado x gravidade` eleva o mesmo sorteio ao quadrado: o leque iria de 192x1 a 1000x80, ou
	///     seja **417x**, contra os 15x dos pre-feitos. Por isso a gravidade entra pela RAIZ: o
	///     sorteio e contado UMA vez inteira, metade pelo lado que ele mesmo produz e metade pela
	///     raiz. O leque cai pra 46x, e as duas populacoes passam a caber na mesma regua.
	///
	///  3. **O lado entra LINEAR e nao ao quadrado.** Area seria mais "fisico", mas o lado ja e a
	///     terceira consequencia do mesmo sorteio de gravidade -- eleva-lo ao quadrado seria contar
	///     o sorteio uma terceira vez, e um mundo pesado viraria 243x a Terra.
	/// ======================================================================================================================
	///
	/// A TABELA QUE ISTO PRODUZ (com <see cref="FuriaBase"/> = 1500), pros planetas que existem hoje:
	///   Earth / Namek / Arconia / Makyo_Star (500, g1) ....  1.500
	///   Arlia (500, g2) ..................................   2.121
	///   Vegeta (500, g10) ................................   4.743
	///   Icer (500, g15) ..................................   5.809
	///   gerado mais leve (192, g1) .......................     576
	///   gerado g40 (591) .................................  11.214
	///   gerado mais pesado (1000, g80) ...................  26.833
	/// </summary>
	/// <param name="ladoEmTiles">O lado do mapa deste mundo, em tiles. Pre-feito le o manifesto; gerado, a <see cref="MundoProcedural.LadoDaGravidade"/>.</param>
	/// <param name="gravidade">O `Planetgrav`. Piso 1 pelo mesmo motivo do <see cref="BpExigido"/>.</param>
	public static double Furia(double ladoEmTiles, double gravidade) =>
		FuriaBase
		* (Math.Max(ladoEmTiles, 1) / LadoDeReferencia)
		* Math.Sqrt(Math.Max(gravidade, 1));

	/// <summary>
	/// O LADO DE UM MAPA PRE-FEITO, e a regua da <see cref="Furia"/>.
	///
	/// **Medido, nao escolhido**: os 26 mapas principais do `Assets/Maps/manifest.json` sao 500x500,
	/// e os 7 destrutiveis estao entre eles. E a regua porque e o unico numero que as duas populacoes
	/// (pre-feita e gerada) compartilham -- com ele, "1,0" quer dizer "do tamanho da Terra".
	/// </summary>
	public const double LadoDeReferencia = 500;

	/// <summary>
	/// ============================ O BOTAO DE BALANCEAMENTO DA MORTE DE PLANETA ============================
	/// A furia de um mundo do tamanho e do peso da Terra. Tudo o mais e proporcao.
	///
	/// **O 1500 nao e um chute -- ele e a resposta a uma pergunta que da pra medir.** Um membro tem
	/// `Body.VidaMax = 100` e o corpo nocauteia com qualquer nucleo abaixo de `Regras.LimiarQuebra`
	/// (20%), ou seja **80 de dano em todos os membros ja nocauteia** -- e o commit mata quem esta
	/// nocauteado (`Area_Death.dm:140-142`). O piso do gap de poder e `CombatKnobs.BpModMin = 0,01`,
	/// entao o dano que um ser MUITO acima do planeta leva e exatamente `Furia / 100`. Com 1500:
	///   * Terra 15, Arlia 21, Vegeta 47, Icer 58 -- **os sete pre-feitos sao sobreviviveis** por quem
	///     esta muito alem deles, e o sobrevivente sai machucado e nao ileso;
	///   * gerado de gravidade ~30 pra cima passa de 80 e **nao sobra ninguem**.
	/// Subir este numero fecha a porta da sobrevivencia de baixo pra cima (o proximo a fechar e Icer);
	/// descer abre a de cima pra baixo. E o unico numero que precisa mudar.
	/// ====================================================================================================
	/// </summary>
	public const double FuriaBase = 1500;

	/// <summary>
	/// ============================ QUANTO A EXPLOSAO TIRA DE UM CORPO ============================
	/// A <see cref="Furia"/> nao entra crua no membro: ela passa pelo **gap de poder que o jogo
	/// inteiro ja usa** (<see cref="Combat.CombatMath.BpModulus"/>, o `BPModulus` do DM). Nenhuma
	/// curva nova foi inventada aqui -- e a mesma que decide o dano de um soco e o de um raio.
	///
	/// ============================ E ISTO SUBSTITUI O CRIVO DO ALGOZ. DE PROPOSITO ============================
	/// O DM decide quem morre por `if(M.expressedBP <= mexpressedBP)` (`Area_Death.dm:137`): o
	/// yardstick e o BP de **quem apertou o botao**, e o resultado e binario -- 99 em tudo, ou nada.
	/// O dono pediu dano *"baseado na gravidade do planeta e tamanho dele"*, e essas duas coisas nao
	/// convivem: se o dano e do PLANETA, o BP do algoz nao tem por que decidir quem sobrevive a ele.
	///
	/// O yardstick vira entao o do proprio mundo, e ele **ja existe e ja e usado**: o
	/// <see cref="BpExigido"/>, o `PDESTROY_BP_PER_GRAV * Planetgrav` que o verb cobra pra deixar
	/// alguem quebrar este planeta. A regra fica dizivel numa linha, e e a mesma dos dois lados:
	/// **quem e forte o bastante pra partir este mundo e forte o bastante pra sobreviver a ele.**
	///
	/// O que se perde: a fidelidade literal ao `<=` do original. O que se ganha: a mesma explosao
	/// mata as mesmas pessoas quer o planeta tenha caido por um jogador de BP 1e12, por uma saga ou
	/// pelo fim do pavio lento -- hoje os tres davam resultados diferentes pro mesmo mundo.
	/// ======================================================================================================
	/// </summary>
	public static double DanoNoCorpo(double furia, double gravidade, double expressedBP) =>
		furia * Combat.CombatMath.BpModulus(BpExigido(gravidade), expressedBP);

	// =====================================================================
	// (c-bis) O OUTRO CONSUMIDOR DA MESMA FURIA: A VIDA DO MUNDO SOB FOGO DE KI
	// =====================================================================
	// ============================ K3: O PEDIDO DO DONO, LITERAL ============================
	// *"a 'vida' do planeta segue a mesma formula do dano dele quando explode, usando o tamanho e a
	// gravidade, entao dano de quando ele explode = vida do planeta"*.
	//
	// **Nao ha funcao nova aqui de proposito, e esta ausencia e o ponto.** A vida de um mundo E a
	// `Furia` dele -- a mesma chamada, o mesmo numero, os mesmos dois argumentos. Uma
	// `VidaDoPlaneta(lado, g) => Furia(lado, g)` seria um APELIDO, e apelido e o primeiro passo pra
	// duas contas: bastaria alguem "corrigir" um dos dois lados um dia. Quem quer a vida chama
	// `Furia`, e quem le o codigo ve que e o mesmo numero sem precisar seguir um atalho.
	//
	// O que existe abaixo e o resto do pedido: o PORTAO (quem pode ferir) e o ESTRAGO (quanto cada
	// tiro arranca).
	// ====================================================================================

	/// <summary>
	/// ============================ K5: "FORTE O SUFICIENTE" -- E A REGUA JA EXISTIA ============================
	/// *"o planeta tb pra sofrer dano de ataque de ki do espaco tem q vir de um jogador forte o
	/// suficiente, pessoas fracas receberiam um aviso q o ataque dela n fez nada ao planeta"*.
	///
	/// O limiar e o <see cref="BpExigido"/>, e ele **nao e um numero novo**: e o
	/// `PDESTROY_BP_PER_GRAV * Planetgrav` que o verb Planet Destroy ja cobra de quem esta PISANDO no
	/// planeta (`Planets.dm:326`), e que a <see cref="DanoNoCorpo"/> ja usa como regua de quem
	/// sobrevive a explosao. Com este terceiro consumidor a regra fica dizivel numa frase so, e ela
	/// vale nas tres direcoes:
	///   **e o mesmo poder que parte este mundo por dentro, que sobrevive a ele, e que o alcanca de
	///   fora.**
	///
	/// Um limiar proprio pra o tiro de orbita seria um quarto numero pra o dono equilibrar, e o
	/// primeiro a envelhecer sozinho.
	///
	/// ============================ E O AVISO NAO DIZ QUANTO FALTA -- ISSO E REGRA, NAO ESTILO ============================
	/// O dono foi explicito: *"nao e pra dizer o bp minimo ou outra coisa, so dizer q n e forte o
	/// suficiente"*. Esta funcao devolve um **bool** e nao a diferenca, nem a razao, nem o limiar --
	/// e isso e deliberado: com um `double` na mao, a tentacao de escrever "faltam X" no aviso e
	/// enorme, e o vazamento entra por descuido meses depois. Quem so pode responder sim/nao nao tem
	/// como vazar.
	///
	/// **O vazamento tambem tem lado de fora**: nao ha barra de vida de planeta, nao ha porcentagem
	/// no chat e o estado da ferida NAO viaja no `S2C.Mortos` (que so carrega o que ja e publico:
	/// fase, estagio e o prazo de uma agonia que todo mundo esta vendo). Ver
	/// `GameServer.Destruicao`.
	/// ==============================================================================================================
	/// </summary>
	public static bool ForteOBastantePraFerirOMundo(double bpDoTiro, double gravidade) =>
		bpDoTiro >= BpExigido(gravidade);

	/// <summary>
	/// ============================ K4: QUANTO UM TIRO ARRANCA DE UM MUNDO ============================
	/// *"pessoas MUITO fortes poderiam zerar a vida do planeta rapidamente, mas pessoas mais fracas
	/// demorariam mt mais tempo"*.
	///
	/// TRES FATORES, e nenhum deles e uma escala de poder nova:
	///
	///  1. **O DANO CRU DO PROPRIO GOLPE** (`bruto`), pela cadeia de ki de sempre -- o
	///     <see cref="Combat.DanoDeKi.BrutoContra"/>, com o divisor em zero porque um mundo nao tem
	///     `Ekidef` nem tecnica. E o que faz um Final Flash valer mais que uma bolinha, e faz a
	///     PERICIA de quem atira contar. Ele e limitado por construcao: os E-stats passam pelo
	///     `StatCurves.StatCap`, que satura perto de 10 -- ou seja este fator vai de ~12 (quem nunca
	///     treinou) a algumas dezenas de milhares, e **nao cresce com o BP**.
	///
	///  2. **O GAP DE PODER** (<see cref="Combat.CombatMath.BpModulus"/>), medido contra o
	///     <see cref="BpExigido"/> deste chao. E o unico fator que carrega o BP, e ele e linear e sem
	///     teto -- de proposito, e a mesma escolha que o resto do jogo faz. E ELE que responde o
	///     pedido: no limiar vale 1; cem vezes acima do limiar vale 100.
	///
	///     **Os dois nao contam o mesmo sorteio duas vezes**, que e a armadilha que a <see cref="Furia"/>
	///     ja teve que desviar: (1) e PERICIA (saturada), (2) e PODER (ilimitado). Sao eixos
	///     diferentes, e um lutador pode ser forte e desajeitado.
	///
	///  3. **A PONTE ENTRE AS DUAS ESCALAS** (<see cref="MundoPorPontoDeKi"/>). Ela e obrigatoria e
	///     e honesto dizer por que: a <see cref="Furia"/> nasceu calibrada pra MEMBRO (100 de vida,
	///     nocaute em 80), e o dono amarrou a vida do mundo ao mesmo numero. Um mundo com 1500 de
	///     vida e, na regua do corpo, quinze membros -- entao sem uma conversao qualquer tiro de
	///     meia-boca derrubaria a Terra no primeiro impacto. A ponte e UMA constante, derivada de uma
	///     ancora escrita (ver la), e e o unico botao deste sistema.
	/// ==========================================================================================
	/// </summary>
	/// <param name="bruto">O dano cru do tiro -- <see cref="Combat.DanoDeKi.BrutoContra"/> com `defesa: 0`.</param>
	/// <param name="gravidade">Do mundo, pro <see cref="BpExigido"/>. Piso 1, como em todo lugar.</param>
	/// <param name="bpDoTiro">O `Projetil.Bp` -- o `expressedBP` de quem atirou, fixado no disparo.</param>
	public static double DanoNoMundo(double bruto, double gravidade, double bpDoTiro)
	{
		// O PORTAO PRIMEIRO, E DENTRO DA FORMULA. Ele poderia morar so no chamador -- e ai o dia em
		// que aparecer um segundo chamador (a Final Explosion? uma nave?) sera o dia em que um
		// fracote derruba um mundo por uma linha esquecida. Ver `ForteOBastantePraFerirOMundo`.
		if (!ForteOBastantePraFerirOMundo(bpDoTiro, gravidade)) return 0;

		return Math.Max(bruto, 0)
			 * Combat.CombatMath.BpModulus(bpDoTiro, BpExigido(gravidade))
			 * MundoPorPontoDeKi;
	}

	/// <summary>
	/// ============================ A ANCORA: QUANTOS TIROS NO LIMIAR ============================
	/// **Este e o botao de balanceamento do K**, o irmao do <see cref="FuriaBase"/>, e ele diz uma
	/// frase inteira:
	///
	///   *"um atacante EXATAMENTE no limiar deste mundo, atirando o tiro mais CRU do jogo, derruba a
	///     Terra em dez mil tiros"*.
	///
	/// Dez mil Basic Blasts (recarga de `max(Eactspeed/5, 3)` tiques, no minimo 0,3 s) sao cerca de
	/// cinquenta minutos de fogo ininterrupto.
	///
	/// ============================ E ISSO E O **PISO**, NAO O CASO TIPICO ============================
	/// O tiro de referencia e o de quem **nunca treinou pericia nenhuma** (ver
	/// <see cref="BrutoDeReferencia"/>) -- e ninguem chega ao limiar de um planeta sem pericia. O
	/// primeiro fator do <see cref="DanoNoMundo"/> (o dano cru) vai de 12 a algumas dezenas de
	/// milhares conforme a pericia de ki, entao um atacante REALISTA da faixa do limiar fica na casa
	/// dos minutos, e nao das dezenas deles. A bancada `--planetateste` imprime a superficie inteira
	/// (BP x pericia x planeta) exatamente pra que este numero nunca precise ser adivinhado.
	///
	/// ============================ POR QUE ELE E TAO ALTO ============================
	/// Porque a outra porta pro mesmo mundo e barata: o verb Planet Destroy cobra 1000 de Ki, o bit
	/// de vilao e **trinta segundos**. Se o bombardeio de orbita fosse mais rapido que isso, ele
	/// tornaria o verb decorativo e abriria a arma pra todo mundo de uma vez. Ele tem que ser a rota
	/// LENTA -- a que se paga com exposicao e paciencia em vez de com um bit de admin.
	///
	/// Dali pra cima o gap de poder faz o resto, e ele e ingreme porque o jogo e ingreme: cem vezes
	/// o limiar sao cem vezes menos tiros. Isso e o *"MUITO fortes zeram rapidamente"* do pedido, e
	/// e a mesma curva que ja decide um soco. **E o unico numero a mexer se o dono achar a rota
	/// lenta demais ou rapida demais.**
	/// ======================================================================================
	/// </summary>
	public const double TirosNoLimiar = 10_000;

	/// <summary>
	/// O DANO CRU DO TIRO DE REFERENCIA -- e ele e **medido, nao escolhido**.
	///
	/// E o Basic Blast de quem nunca treinou nada: com `Ekioff = Ekiskill = 1` e `blastskill` no piso
	/// de 10, o `basedamage` vale 1, o `mods` vale 1, e a cadeia de ki devolve
	/// `1 * 6 * DanoGlobalDeKi * 1`. Escrito como expressao e nao como o literal `12` de proposito:
	/// o `DanoGlobalDeKi` e um botao de balanceamento global (`objects.dm:56`), e se alguem o dobrar,
	/// a ancora acompanha em vez de mentir caladamente.
	/// </summary>
	public static double BrutoDeReferencia => 6 * Combat.DanoDeKi.DanoGlobalDeKi;

	/// <summary>
	/// A PONTE ENTRE A REGUA DO CORPO E A REGUA DO MUNDO -- derivada da ancora, nunca cravada.
	///
	/// Ela cai de <see cref="TirosNoLimiar"/>, <see cref="BrutoDeReferencia"/> e
	/// <see cref="FuriaBase"/> (a furia de um mundo do tamanho e do peso da Terra, que e a vida dele).
	/// Mexer em qualquer um dos tres move esta e mantem a frase da ancora verdadeira; cravar o
	/// resultado aqui faria a frase virar mentira no primeiro ajuste.
	/// </summary>
	public static double MundoPorPontoDeKi => FuriaBase / (TirosNoLimiar * BrutoDeReferencia);

	/// <summary>
	/// ============================ A FERIDA FECHA -- e a decisao esta escrita ============================
	/// O dono nao disse se a vida volta. **Volta**, e devagar: passado um minuto sem levar tiro
	/// (<see cref="SegundosDeCalmaAntesDeFechar"/>), um mundo fecha a ferida inteira em
	/// <see cref="PavioInteiro"/> -- os mesmos vinte minutos do pavio lento, o outro relogio longo
	/// deste arquivo.
	///
	/// AS DUAS RAZOES, e a primeira e de custo e nao de gosto:
	///   1. **O universo e infinito.** Sem cicatrizacao, um tiro de raspao em cada mundo gerado por
	///      onde alguem passasse deixaria uma entrada de estado viva pra sempre -- "dado orfao
	///      eterno" e uma familia de defeito que este repo ja pagou. Com ela, a ferida some sozinha e
	///      o registro se limpa sem ninguem varrer.
	///   2. **E ela e o que faz *"os fracos demoram muito mais"* ser verdade de verdade.** Sem
	///      cicatrizacao, mil pessoas fracas atirando uma vez por dia derrubariam a Terra num mes; o
	///      esforco tem que ser SUSTENTADO, e nao acumulado pra sempre.
	///
	/// ============================ E POR QUE HA UMA CARENCIA, EM VEZ DE CICATRIZAR SEMPRE ============================
	/// **Sem ela o portao viraria mentira.** A primeira versao cicatrizava continuamente, e a conta
	/// mostrou o buraco: o atacante mais fraco que o portao ACEITA tira `Furia/TirosNoLimiar` por
	/// tiro, e isso e MENOS do que `Furia/PavioInteiro` por segundo -- ou seja o jogo diria "voce e
	/// forte o suficiente", ele atiraria a tarde inteira e o planeta ficaria intacto, **sem uma linha
	/// explicando por que**. Um portao que deixa passar quem nao pode vencer e pior do que um portao
	/// mais alto.
	///
	/// Com a carencia, fogo SUSTENTADO sempre progride (por mais devagar que seja, que e exatamente o
	/// *"demorariam mt mais tempo"* do pedido) e o que a cicatrizacao desfaz e o cerco ABANDONADO --
	/// que e a unica coisa que ela precisava desfazer.
	///
	/// ============================ E POR ISSO ELA NAO VAI PRO DISCO ============================
	/// A ferida de um mundo VIVO nao persiste, e isso nao e a armadilha do DM (metade do estado
	/// sobrevivendo ao boot) -- e o contrario dela. O que o original errou foi guardar "esta
	/// morrendo" e esquecer "em que estagio", reconstruindo um pavio novo a cada boot; aqui, o que e
	/// permanente (a condenacao) persiste INTEIRO no `EstadoDaMorte`, e o que e transitorio se
	/// desfaz sozinho com o tempo. Um reinicio so faz de uma vez o que vinte minutos fariam devagar,
	/// e o irmao disso ja mora neste arquivo: o relogio de 3-11 s entre dois tremores tambem nao vai
	/// pro disco, e pela mesma razao.
	/// ====================================================================================
	/// </summary>
	public static double SegundosParaCicatrizar => PavioInteiro;

	/// <summary>
	/// QUANTO TEMPO SEM LEVAR TIRO antes de a ferida comecar a fechar. Ver
	/// <see cref="SegundosParaCicatrizar"/>, onde esta escrito o buraco que ela tapa.
	///
	/// Um minuto porque ele tem que ser FOLGADO em relacao ao intervalo entre dois tiros de qualquer
	/// arma do jogo -- inclusive a Esfera Teleguiada, que custa `600 * BaseDrain` e nao da pra
	/// repetir a cada segundo. Curto demais e a carencia deixaria de valer justamente pra quem atira
	/// devagar, que e quem ela existe pra proteger.
	/// </summary>
	public const double SegundosDeCalmaAntesDeFechar = 60;

	/// <summary>
	/// Um passo de cicatrizacao. Ver <see cref="SegundosParaCicatrizar"/>.
	/// </summary>
	/// <param name="semLevarTiro">
	/// Ha quantos segundos este mundo nao leva um tiro. Abaixo da
	/// <see cref="SegundosDeCalmaAntesDeFechar"/> nada fecha -- a ferida so comeca a se refazer
	/// depois que o fogo para.
	/// </param>
	public static double Cicatrizar(double dano, double furia, double dt, double semLevarTiro)
	{
		if (semLevarTiro < SegundosDeCalmaAntesDeFechar) return dano;
		return Math.Max(dano - furia * dt / SegundosParaCicatrizar, 0);
	}

	// =====================================================================
	// (d) A RAMPA -- **UMA fracao, um lugar so**
	// =====================================================================
	/// <summary>
	/// ============================ A AGONIA, DE 0 A 1 ============================
	/// *"quanto mais perto ta de explodir, mais intenso esses efeitos ficam"*. Esta e a fracao, e ela
	/// e **a unica** do sistema: tremor, ceu, chao caindo, cratera, pedra levitando e a crosta de
	/// magma vista do espaco leem daqui. Um efeito com nocao propria de intensidade divergiria de
	/// todos os outros no primeiro ajuste -- e ninguem descobre isso olhando, porque cada um estaria
	/// "certo" sozinho.
	///
	/// **E ela e do Core porque os dois lados a consomem**: o servidor (cadencia do tremor, forca do
	/// clima, celulas de chao por volta) e o cliente (o shader do planeta no espaco). Duas contas com
	/// o mesmo nome em dois arquivos seriam duas rampas.
	///
	/// ============================ A CURVA, E POR QUE ELA NAO E RETA ============================
	/// `t^1,5`. A derivada cresce com `t`, entao a coisa ACELERA -- que e o que "mais perto, mais
	/// intenso" quer dizer de verdade. Reta daria a mesma quantidade de piora por segundo do comeco
	/// ao fim, e o ultimo minuto (o unico que o jogador vai lembrar) leria igual ao primeiro.
	/// Na metade do prazo a agonia esta em 0,43; faltando um minuto, em 0,80.
	///
	/// ============================ E ELA NAO COMECA EM ZERO ============================
	/// <see cref="PisoDaAgonia"/>. No segundo zero da explosao o planeta ja tem que estar tremendo e
	/// o ceu ja tem que ser o do fim do mundo -- e o `Quake()` + `currentWeather = "Destruction"` que
	/// o `Area_Death.dm:86-95` dispara ANTES do laco. Comecar em zero daria meio minuto de silencio
	/// depois do anuncio de que o mundo vai acabar.
	///
	/// O PAVIO LENTO SOBE ATE ESSE MESMO PISO, em degraus de estagio, e por isso a passagem do pavio
	/// pra explosao e continua: a agonia nunca DESCE. Fosse o piso menor que o topo do pavio, o
	/// planeta ficaria visivelmente mais calmo no instante em que a conta regressiva comeca.
	/// =========================================================================
	/// </summary>
	public static double Intensidade(FaseDaMorte fase, int estagio, double faltam) => fase switch
	{
		FaseDaMorte.Morrendo =>
			PisoDaAgonia * Math.Clamp(estagio + 1, 0, UltimoEstagio + 1) / (UltimoEstagio + 1.0),

		FaseDaMorte.Explodindo =>
			PisoDaAgonia + (1 - PisoDaAgonia) * Math.Pow(
				Math.Clamp(1 - faltam / SegundosDeExplosao, 0, 1), ExpoenteDaAgonia),

		// Vivo nao tem agonia; DESTRUIDO tambem nao -- o mundo ja acabou, e o que sobra e a explosao,
		// que e um instante e nao um estado.
		_ => 0,
	};

	/// <summary>A agonia no segundo zero da explosao, e o topo do pavio lento. Ver <see cref="Intensidade"/>.</summary>
	public const double PisoDaAgonia = 0.12;

	/// <summary>O expoente da rampa. Acima de 1 = acelera. Ver <see cref="Intensidade"/>.</summary>
	public const double ExpoenteDaAgonia = 1.5;

	/// <summary>
	/// QUANTO DURA A MEGA EXPLOSAO, em segundos -- o instante em que o mundo some.
	///
	/// Mora no Core porque os DOIS lados precisam do mesmo numero e por razoes diferentes: o
	/// servidor manda o efeito de tela com esta duracao pra quem esta no chao, e o cliente segura o
	/// disco do planeta desenhado por exatamente este tempo depois do prazo vencer, pra a explosao
	/// ter onde acontecer. Se os dois numeros divergissem, o planeta sumiria no meio do proprio
	/// estouro (ou ficaria um disco morto na tela depois dele).
	///
	/// **Nao e um numero do DM**: la o `spawnExplosion` do fim (`Area_Death.dm:147`) e instantaneo e
	/// o planeta simplesmente sai da lista. O desfecho visual e pedido novo do dono.
	/// </summary>
	public const double SegundosDoEstouro = 2.2;

	/// <summary>A mesma pergunta com o registro na mao -- o jeito que os dois lados chamam.</summary>
	public static double Intensidade(EstadoDaMorte e) => Intensidade(e.Fase, e.Estagio, e.Faltam);

	// =====================================================================
	// AS PERGUNTAS QUE O RESTO DO JOGO FAZ
	// =====================================================================
	/// <summary>Este estado impede pousar, povoar, nascer e viajar? Morrendo AINDA nao -- so morto.</summary>
	public static bool EstaMorto(FaseDaMorte f) => f == FaseDaMorte.Destruido;

	/// <summary>O ceu esta sob o efeito da morte? Vale do estagio da noite eterna ate o commit.</summary>
	public static bool CeuCondenado(EstadoDaMorte e) =>
		e.Fase == FaseDaMorte.Explodindo
		|| (e.Fase == FaseDaMorte.Morrendo && e.Estagio >= EstagioDaNoiteEterna);
}

/// <summary>
/// ============================ A FERIDA DE UM MUNDO VIVO -- e ela NAO e uma fase da morte ============================
/// O estado de um planeta que esta levando tiro de ki do espaco e **ainda esta vivo**. Ele mora
/// FORA do <see cref="RegistroDeMortos"/>, e essa separacao e a coisa mais importante deste tipo:
///
///   * `RegistroDeMortos.Condenado(z)` responde *"ha morte em curso ou consumada aqui"* -- e essa
///     resposta desliga o povoamento, o berco, a invasao, o dominio e o pouso. Um planeta que levou
///     UM tiro de raspao **nao pode** acender nada disso. Enfiar a ferida no registro (como uma fase
///     nova, ou como um `EstadoDaMorte` com `Fase = Vivo`) faria um unico tiro na Terra parar de
///     repovoar os 40 cidadaos dela, calado. Foi por um triz;
///   * e o registro de mortos PERSISTE. A ferida nao (ver
///     <see cref="MortePlanetaria.SegundosParaCicatrizar"/>): ela se desfaz sozinha com o tempo, e
///     um arquivo cheio de arranhoes de mundos gerados seria estado orfao eterno.
///
/// Quando a ferida chega na <see cref="MortePlanetaria.Furia"/> do mundo, ela **entrega o planeta
/// pela porta unica** (`ComecarDestruicao`, a mesma do Planet Destroy) e deixa de existir: dali pra
/// frente quem manda e o `EstadoDaMorte`, que persiste inteiro.
/// ==============================================================================================================
/// </summary>
public sealed class FeridaDeMundo
{
	/// <summary>A chave do planeta -- <see cref="ChaveDePlaneta.Texto"/>.</summary>
	public string Chave = "";

	/// <summary>O nome legivel, pro aviso e pro log. NAO e a identidade.</summary>
	public string Nome = "";

	/// <summary>Quanto ja foi arrancado. Chegou na furia do mundo, ele esta condenado.</summary>
	public double Dano;

	/// <summary>
	/// Segundos desde o ultimo AVISO a quem esta no chao (K2). O aviso e por planeta e nao por tiro:
	/// uma barragem de tres tiros por segundo viraria trinta linhas de chat em dez segundos, e o
	/// jogador pararia de ler exatamente a linha que ele precisa ler.
	/// </summary>
	public double DesdeOAviso = double.MaxValue;

	/// <summary>
	/// Ha quantos segundos este mundo nao leva um tiro. E o relogio da CARENCIA da cicatrizacao --
	/// ver <see cref="MortePlanetaria.SegundosDeCalmaAntesDeFechar"/>: sob fogo o mundo nao se
	/// refaz, e o cerco abandonado e o unico que a cicatrizacao desfaz.
	/// </summary>
	public double SemLevarTiro;

	/// <summary>A zona deste mundo, pela mesma volta que a condenacao usa. Ver <see cref="ChaveDePlaneta.ZonaDe"/>.</summary>
	public ZoneKey Zona() => ChaveDePlaneta.ZonaDe(Chave, Nome);
}

/// <summary>
/// O LIVRO DOS PLANETAS MORTOS -- a `PlanetDisableList` do original, com chave honesta.
///
/// ============================ AS DUAS PONTAS TEM UM ============================
/// O servidor tem o dele (autoridade, persistido em `planetas-mortos.json`); o cliente tem uma copia
/// que chega por pacote (`S2C.Mortos`). O cliente precisa porque **ele enumera planetas sozinho**:
/// a carta estelar chama `Espaco.PreFeitos()` e `Sistemas.Do` direto (`Client/MapaEstelar.cs`), e
/// sem esta copia ela desenharia um planeta destruido com um botao "Viajar" em cima. Um bit no
/// `S2C.Vizinhanca` nao cobriria a carta -- ela desenha o que esta a anos-luz de distancia.
///
/// A classe e a MESMA nas duas pontas justamente pra nao haver duas nocoes de "morto" (regra 0.4).
/// ==========================================================================
/// </summary>
public sealed class RegistroDeMortos
{
	private readonly Dictionary<string, EstadoDaMorte> _porChave = [];

	public int Quantos => _porChave.Count;
	public IEnumerable<EstadoDaMorte> Todos => _porChave.Values;

	public EstadoDaMorte? De(ChaveDePlaneta c) =>
		_porChave.TryGetValue(c.Texto, out EstadoDaMorte? e) ? e : null;

	public EstadoDaMorte? De(ZoneKey z) =>
		ChaveDePlaneta.Da(z) is { } c ? De(c) : null;

	/// <summary>Este planeta esta DESTRUIDO? A pergunta do `if(A.planetType in PlanetDisableList)`.</summary>
	public bool Morto(ChaveDePlaneta c) => De(c) is { } e && MortePlanetaria.EstaMorto(e.Fase);

	public bool Morto(ZoneKey z) => De(z) is { } e && MortePlanetaria.EstaMorto(e.Fase);

	public bool Morto(PlanetaNoEspaco p) => Morto(ChaveDePlaneta.De(p));

	/// <summary>Ha alguma morte em curso ou consumada aqui? (o `planet_dying` do DM).</summary>
	public bool Condenado(ZoneKey z) => De(z) != null;

	public void Por(EstadoDaMorte e) => _porChave[e.Chave] = e;

	public bool Tirar(ChaveDePlaneta c) => _porChave.Remove(c.Texto);

	public void Limpar() => _porChave.Clear();

	/// <summary>Troca o conteudo inteiro de uma vez. E o que o cliente faz ao receber o pacote.</summary>
	public void Substituir(IEnumerable<EstadoDaMorte> novos)
	{
		_porChave.Clear();
		foreach (EstadoDaMorte e in novos) _porChave[e.Chave] = e;
	}
}
