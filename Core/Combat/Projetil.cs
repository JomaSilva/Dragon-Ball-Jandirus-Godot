using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Core.Combat;

/// <summary>
/// OS TRES TIPOS QUE ATIRAM. Os bytes sao os do DM (`customattacks.dm:104`: `attacktype` 0 Beam,
/// 1 Blast, 2 Guided, 3 Melee) -- e o Melee NAO esta aqui de proposito: no original ele tem tela
/// de montagem e NAO tem disparo (`CustomAttackHandler` desvia 1 e 2 e o resto do proc e o fluxo
/// de beam; nao ha ramo nenhum pro 3). Um quarto valor aqui seria um tipo que promete voar e nao
/// voa.
/// </summary>
public enum TipoDeProjetil : byte
{
	/// <summary>Raio CANALIZADO: nasce de uma carga, sai da mao enquanto o dono segurar.</summary>
	Beam = 0,

	/// <summary>Bola solta: sai e voa reto, o dono nao tem mais nada com ela.</summary>
	Blast = 1,

	/// <summary>Bola solta que PERSEGUE o alvo marcado. Sem alvo, vira um Blast e avisa.</summary>
	Guided = 2,
}

/// <summary>
/// O QUE UMA TECNICA PEDE PRA ATIRAR -- a costura entre "quem manda atirar" e "o que voa".
///
/// ============================ ELA E O ENCAIXE DA CAMADA 2 ============================
/// A tecnica customizada do jogador (`/datum/skill/CustomAttack`) vai preencher esta receita a
/// partir dos 5 pontos que ele gastou; uma tecnica portada a mao (Kamehameha, Death Beam)
/// preenche com os numeros do verb dela. Os dois caminhos entram pela MESMA porta, e por isso
/// existe UM projetil e nao dois -- que era o risco anotado no mapa desta camada.
///
/// Os padroes sao os do DM (`customattacks.dm:71-95`): dano 0,8; carga 1; Ki 20; velocidade 1;
/// alcance 20 tiles.
/// ====================================================================================
/// </summary>
public sealed class ReceitaDeProjetil
{
	public TipoDeProjetil Tipo = TipoDeProjetil.Blast;

	/// <summary>`base_damage`. Multiplica o dano cru inteiro -- ver <see cref="DanoDeKi.Bruto"/>.</summary>
	public double BaseDano = 0.8;

	/// <summary>`maxdamage` (0 = sem teto). Opcional no `DamageCalc` do DM, e por isso opcional aqui.</summary>
	public double MaxDano;

	/// <summary>`speed` 1..5. Ver <see cref="Projetil.SegundosPorTile"/> -- ela vira ATRASO, nao velocidade.</summary>
	public double Velocidade = 1;

	/// <summary>`range` em TILES (min. 5 na loja de pontos, padrao 20).</summary>
	public double AlcanceTiles = 20;

	/// <summary>`rangemodifier`: quanto a potencia muda a cada tile viajado. 1 = nao muda.</summary>
	public double RangeMod = 1;

	/// <summary>`minimum_chargetime`. So o Beam carrega; blast e guided saem na hora.</summary>
	public double CargaMinima = 1;

	/// <summary>`instantattack`: paga 2 pontos pra a carga nao prender ninguem.</summary>
	public bool Instantaneo;

	/// <summary>`physdamage`: troca a cadeia de ki pela fisica e o elemento pra "Physical".</summary>
	public bool Fisico;

	/// <summary>`deflectable`. Falso = nao ha o que defletir, so aguentar.</summary>
	public bool Deflectivel = true;

	/// <summary>`piercer`: atravessa quem acerta em vez de sumir.</summary>
	public bool Piercer;

	/// <summary>
	/// `wavemult` -- O TIRO CARREGA UM PODER MAIOR QUE O DO DONO.
	///
	/// No DM ele multiplica o `A.BP` do objeto (`A.BP = expressedBP*wavemult`, `beams.dm:139`) e,
	/// junto, a ESCALA do sprite -- por isso o Final Flash e um muro de energia e nao um fio. O
	/// comentario do proprio verb diz o que ele quer dizer: *"makes the FinalFlash BP 4x greater
	/// than your own. Oof."* (`beams/FinalFlash.dm:38`).
	///
	/// Ele NAO e o mesmo que <see cref="BaseDano"/> (o `powmod`): o `powmod` engorda o dano cru, e o
	/// `wavemult` engorda o BP -- que e o que decide o <see cref="DanoDeKi.ChanceDeDeflexao"/>, o
	/// `BPModulus` no fim da cadeia e o <see cref="PoderDeEmbate"/>. Uma tecnica com `wavemult` alto
	/// nao machuca mais um igual: ela e mais dificil de defletir e GANHA embate de quem e mais forte.
	///
	/// O QUE SE PERDEU: no DM o `wavemult` ainda CRESCE durante a carga
	/// (`wavemult *= 3**(-wavemult/log_10(chargedskill)) + 1`, `beams.dm:41`), porque la a carga e um
	/// laco que acumula. Aqui a carga e uma contagem regressiva (ver <see cref="SegundosDeCarga"/>) e
	/// o valor e o que a tecnica declara. Segurar a carga mais tempo nao engrossa o raio -- ficou
	/// anotado, e o preco de a carga nao ser um laco.
	/// </summary>
	public double MultDeOnda = 1;

	/// <summary>
	/// `paralysis` -- QUEM LEVA ESTE TIRO PARA DE ANDAR.
	///
	/// `objects.dm:347-350`: o `Bump` escreve `M.paralyzed = 1` e um prazo em SEGUNDOS. Nao e stun
	/// (o alvo continua podendo bater e defender): e a perna que nao obedece, e o DM deixa 1 chance
	/// em 12 por tique de o corpo escapar mesmo assim (`movement handler.dm:89`) -- ver
	/// <see cref="SegundosDeParalisia"/>.
	/// </summary>
	public bool Paralisia;

	/// <summary>
	/// O nome que aparece no relato. Pra tecnica customizada e o nome que o jogador deu.
	///
	/// A COR NAO ESTA AQUI, e isso e decisao: cada personagem ja tem a cor do proprio ki
	/// (`Appearance.CorAura`), o cliente ja a recebeu pelo `PeerLook` e ja a usa na aura e na carga.
	/// Mandar a cor junto do tiro criaria a segunda resposta pra "de que cor e o ki deste sujeito" --
	/// e o arquivo `Client/Aura.cs` inteiro e a historia de quando houve duas. O tiro leva o DONO;
	/// a cor sai dele.
	/// </summary>
	public string Nome = "ataque de ki";
}

/// <summary>
/// UM PROJETIL VIVO -- a primeira coisa deste jogo que sai de um corpo, anda entre tiques e
/// acerta outro.
///
/// ============================ NAO HAVIA O QUE REUSAR ============================
/// As ~33 tecnicas ja portadas sao TODAS instantaneas: escolhem alvo por raio
/// (`AlvoDeTecnicaG3`) e aplicam o dano no mesmo instante, pelo funil do soco. Nenhuma tem
/// entidade que viaje, nenhuma tem posicao propria, e o `EntityState` do snapshot so sabe
/// descrever CORPOS. Entao isto nao e "um segundo projetil": e o primeiro. As tecnicas
/// instantaneas continuam instantaneas -- elas nao sao raios mal-feitos, sao golpes de alcance,
/// e transformar `Light_Buster` num projetil mudaria a tecnica.
/// ===============================================================================
///
/// ============================ COMO O DM MOVE, E O QUE MUDOU ============================
/// La o passo e por TILE: `walk(A, dir, lag)` anda um tile a cada `lag` tiques de 0,1 s. O beam
/// nao e um objeto -- e um TREM: `ShootBeam` cria um segmento NOVO na mao do dono a cada 0,2 s e
/// todos andam juntos (`beams.dm:64-205`); so o da frente e denso (`objects.dm:190` `KHH()`).
///
/// Aqui o passo continua sendo por tile e com o mesmo atraso -- o que muda e a REPRESENTACAO do
/// beam: um objeto so, com <see cref="Pos"/> na cabeca e <see cref="Cauda"/> no fim do rastro.
/// Enquanto o dono canaliza, a cauda e a mao dele (o trem esta sendo alimentado); quando ele
/// solta, a cauda passa a andar tambem e o rastro se esvazia ate sumir. O desenho e identico e o
/// custo de tique cai de N segmentos pra 1 -- e o custo de tique e a regra 0.4 da casa.
///
/// O QUE SE PERDEU: no DM da pra encurvar um beam ja disparado (`beamturndelay`, `linear=0`), e
/// aqui nao. Ficou anotado; ninguem pediu, e o preco seria guardar a lista de segmentos de volta.
/// =======================================================================================
/// </summary>
public sealed class Projetil
{
	public int Id;

	/// <summary>Quem atirou (`proprietor`). Zero = ninguem -- o tiro continua valendo.</summary>
	public int Dono;

	/// <summary>O corpo perseguido, so no <see cref="TipoDeProjetil.Guided"/>. Zero = voa reto.</summary>
	public int Alvo;

	/// <summary>
	/// QUANTOS SEGUNDOS ELE ESPERA ANTES DE CACAR. Zero = ja sai perseguindo.
	///
	/// E o `spawn(10) A.blasthoming(usr.target)` da Hellzone Grenade (`blasts.dm:517`): as bolas
	/// nascem em volta do alvo e ficam PARADAS um segundo antes de convergir. A pausa e a tecnica --
	/// sem ela o cerco vira uma bola so chegando de sete lugares no mesmo instante, e a vitima nao
	/// tem o segundo em que ve o cerco fechado e ainda pode tentar sair. Enquanto a espera corre a
	/// bola nao anda (o <see cref="Rumo"/> dela nasce nulo).
	/// </summary>
	public double EsperaDeCaca;

	public TipoDeProjetil Tipo;

	/// <summary>A CABECA -- a unica parte densa, e por isso a unica que acerta.</summary>
	public Vec2 Pos;

	/// <summary>
	/// O FIM DO RASTRO, so pro Beam. Enquanto <see cref="Canalizando"/> ela e reescrita pra mao do
	/// dono a cada tique; depois ela anda sozinha ate alcancar a cabeca.
	/// </summary>
	public Vec2 Cauda;

	/// <summary>Pra onde anda, normalizado. `dir` do DM, sem os 8 setores.</summary>
	public Vec2 Rumo;

	/// <summary>`distance`: quantos tiles ainda pode andar. Zerou, acabou.</summary>
	public double Distancia;

	/// <summary>`maxdistance`: com quantos nasceu. Ver <see cref="ModsAgora"/> e o empurrao de perto.</summary>
	public double MaxDistancia;

	public double RangeMod = 1;

	/// <summary>`mods` no instante do disparo -- `Ekioff * Ekiskill` e amigos. Ver <see cref="ModsDoTiro"/>.</summary>
	public double ModsBase = 1;

	/// <summary>`BP`: o `expressedBP` de quem atirou, congelado no disparo.</summary>
	public double Bp;

	public double BaseDano = 0.8, MaxDano;

	/// <summary>`murderToggle` de quem atirou: este tiro pode matar?</summary>
	public bool Letal;

	public bool Deflectivel = true, Piercer, Fisico;

	/// <summary>
	/// `paralysis` -- ver o mesmo campo na receita. O <see cref="MultDeOnda"/> NAO tem irmao aqui de
	/// proposito: ele ja foi consumido no disparo, dentro do <see cref="Bp"/>. Guardar os dois
	/// separados criaria a segunda resposta pra "qual e o poder deste tiro".
	/// </summary>
	public bool Paralisia;

	/// <summary>
	/// A QUE ALTURA ELE VOA, em pixels -- a do atirador no instante do tiro, e ela nao muda.
	///
	/// O DM nao tem altitude (voo la e um bool no mesmo z), entao isto e do port. Vale duas coisas
	/// que o jogo ja decidiu pro corpo e nao podia decidir diferente pro tiro: acima do limiar o
	/// projetil ATRAVESSA cenario (`Voo.AtravessaCenario` -- e o que permite atirar por cima do
	/// muro), e ele so acerta quem esta ao alcance vertical (`Voo.PodeAcertar`). Sem isto um raio
	/// disparado do alto varreria quem estivesse no chao dez andares abaixo.
	/// </summary>
	public float Altitude;

	/// <summary>
	/// Quanto tempo, em segundos, pra andar UM tile.
	///
	/// No DM isto e o `lag` do `walk`, em tiques de 0,1 s, e ele e INVERSO da velocidade:
	///   * blast/guided -- `lag = max(1, round(4 - speed))` (`customattacks.dm:519`);
	///   * beam         -- `beamspeed = 1 / speed` (`customattacks.dm:452`).
	///
	/// Repare que os dois nao sao a mesma escala: com `speed = 1` a bola leva 0,3 s por tile
	/// (~107 px/s, mais devagar que alguem correndo) e o raio leva 0,1 s (320 px/s). E do jogo
	/// original, e e o que faz o raio ser a arma de longe e a bola ser o tiro de perto.
	/// </summary>
	public double SegundosPorTile = 0.3;

	/// <summary>Segundos ja acumulados rumo ao proximo tile. E o que fatia o passo grosso do DM.</summary>
	public double Acumulado;

	/// <summary>`Burnout()`: 50 tiques = 5 s de vida, aconteca o que acontecer.</summary>
	public double VidaRestante = SegundosDeBurnout;

	/// <summary>O dono ainda esta segurando o raio. So o Beam usa.</summary>
	public bool Canalizando;

	/// <summary>Ja acertou alguem e esta EMPURRANDO contra ele -- a cabeca para e continua moendo.</summary>
	public bool Encostado;

	/// <summary>
	/// A CABECA JA PAROU E O RASTRO ESTA SENDO ENGOLIDO -- so o Beam passa por aqui.
	///
	/// ============================ POR QUE UM RAIO NAO MORRE DE UMA VEZ ============================
	/// No DM o beam nao e um objeto: e um TREM de segmentos, cada um com o proprio `distance`. Todos
	/// andam na mesma velocidade, entao enquanto voam o rastro tem COMPRIMENTO CONSTANTE -- ele nao
	/// encolhe no ar, e a primeira versao disto errou justamente ai (fazia a cauda correr atras da
	/// cabeca na mesma velocidade, o que deixa a distancia entre as duas eternamente igual: um traco
	/// de 170 px voando pra sempre).
	///
	/// O rastro so se esvazia quando a CABECA PARA: ela e o segmento mais velho, entao e a primeira a
	/// esgotar o proprio alcance, e dali pra frente cada segmento de tras alcanca o ponto onde o da
	/// frente se apagou. Visto de fora: o raio avanca inteiro, encosta em alguma coisa (ou chega ao
	/// limite), e o rabo e sugado pra dentro do ponto de impacto.
	///
	/// Entao <see cref="Fim"/> e decidido na hora em que a cabeca para, e o projetil so SAI DA LISTA
	/// quando a cauda chega la. Matar na hora faria o rastro sumir de um quadro pro outro.
	/// ==============================================================================================
	/// </summary>
	public bool Esvaziando;

	/// <summary>O motivo que ja foi decidido e espera a cauda chegar. Ver <see cref="Esvaziando"/>.</summary>
	public FimDeProjetil FimPendente;

	/// <summary>Quanto falta pra o proximo tique de dano de um beam encostado.</summary>
	public double AteMoerDeNovo;

	/// <summary>
	/// ESTA CABECA ESTA PRESA NUMA DISPUTA -- o `in_beamclash` do DM (`BeamClash.dm:38`).
	///
	/// Enquanto for verdade a cabeca nao anda sozinha, nao colide e nao morre por alcance: quem manda
	/// na posicao dela e o embate (`GameServer.EmbateDeKi.cs`), que a poe no ponto de encontro a cada
	/// tique. E literalmente o mesmo papel do `if(!in_beamclash) walk(...)` do `objects.dm:241`.
	/// </summary>
	public bool EmEmbate;

	/// <summary>
	/// ESTA CABECA JA GANHOU (ou perdeu) UMA DISPUTA.
	///
	/// Sem isto o feixe vencedor, ao alcancar o perdedor que ainda esta de guarda erguida, abriria um
	/// embate NOVO contra a mesma pessoa -- e a cena que o dono pediu (o ki empurrando ate atingir)
	/// viraria um laco: ganha, avanca, disputa de novo, ganha, avanca. O DM nao precisa deste campo
	/// porque la o perdedor perde o canal e o vencedor so encontra corpo; aqui o embate de guarda
	/// (que e novo) criou o caso.
	/// </summary>
	public bool JaDisputou;

	/// <summary>Como ele se chama no relato. A COR sai do dono -- ver o mesmo campo na receita.</summary>
	public string Nome = "ataque de ki";

	/// <summary>Morto: sai da lista no proximo tique. Ver o motivo em <see cref="Fim"/>.</summary>
	public bool Vivo = true;
	public FimDeProjetil Fim;

	// =====================================================================
	// OS NUMEROS DO DM
	// =====================================================================
	/// <summary>`Burnout()` sem argumento = `burnouttime=50` tiques (`objects.dm:655`).</summary>
	public const double SegundosDeBurnout = 5.0;

	/// <summary>
	/// A CADENCIA DO TREM DE BEAM: `sleep(2)` no fim do `ShootBeam` (`beams.dm:205`) = 0,2 s.
	/// E ela que dita de quanto em quanto um beam encostado volta a machucar.
	/// </summary>
	public const double SegundosPorCicloDeBeam = 0.2;

	/// <summary>
	/// O RAIO DA CABECA, em pixels. Um tile e 32; meio tile e o que faz "encostou" querer dizer
	/// encostou, e nao "passou a um metro".
	/// </summary>
	public const float RaioDeImpacto = 16f;

	/// <summary>`lag = max(1, round(4 - speed))` tiques de 0,1 s (blast e guided).</summary>
	public static double AtrasoDeBola(double velocidade)
		=> Math.Max(1, Math.Round(4 - velocidade, MidpointRounding.AwayFromZero)) * 0.1;

	/// <summary>`beamspeed = 1 / speed` tiques de 0,1 s.</summary>
	public static double AtrasoDeRaio(double velocidade)
		=> 1.0 / Math.Max(velocidade, 0.01) * 0.1;

	/// <summary>
	/// QUANTO TEMPO DE CARGA, em segundos, pra o raio sair.
	///
	/// No DM a carga termina quando `accum >= 10*chargedelay/3`, e `accum` sobe uma vez por ciclo
	/// do `GlobalStats` (~0,2 s) -- ou seja `chargedelay * 2/3` segundos. Antes disso o proprio
	/// laco desconta a pericia: `chargedelay /= log_10(max(chargedskill+beamskill, 10))`
	/// (`beams.dm:33`), que e o que faz um veterano carregar um Kamehameha mais rapido.
	///
	/// COM PISO: soltar o botao antes da carga minima CANCELA (`stopcharging`), entao uma carga de
	/// zero segundos nao existiria como estado -- e o `instantattack` (que paga 2 pontos justamente
	/// pra pular a espera) deixaria de ter o que pular.
	/// </summary>
	public static double SegundosDeCarga(double cargaMinima, Fighter f)
	{
		// ============================ A PERICIA ERA OUTRA ============================
		// O DM escreve `chargedelay /= log_10(max(chargedskill+beamskill,10))` (`beams.dm:33`), e
		// esta linha somava `kieffusionskill`. A troca nao foi escolha: o campo `chargedskill` NAO
		// EXISTIA no `Fighter` quando o raio foi portado (ele caia em `EfeitosDeSkill.Desconhecidos`
		// junto com outras cinco), e a pericia de efusao foi posta no lugar por ser a vizinha.
		//
		// O erro e invisivel em personagem novo -- as duas valem zero e o `max(...,10)` iguala tudo --
		// e so aparece em quem treinou: quem subiu a arvore de EFUSAO carregava mais rapido sem ter
		// treinado CARGA, e quem subiu a de carga nao ganhava nada. Ver `Fighter.chargedskill`.
		// =============================================================================
		double div = Math.Log(Math.Max(f.chargedskill + f.beamskill, 10)) / Math.Log(10);
		return Math.Max(cargaMinima / Math.Max(div, 1) * 2.0 / 3.0, 0.05);
	}

	/// <summary>
	/// QUANTOS SEGUNDOS DE PARALISIA este tiro impoe:
	/// `min(max(5, Ekidef * max(Etechnique,Ekiskill) * BPModulus(BP_do_tiro, expressedBP_do_alvo)), 10)`
	/// (`objects.dm:349`).
	///
	/// REPARE QUE A CONTA E DO ALVO, E NAO DO ATIRADOR -- e ela e MAIOR quanto mais forte for quem
	/// leva. Lida de fora parece invertida, e nao e um erro do DM: o `BPModulus` do numerador cai
	/// quando o alvo e mais forte, e os dois efeitos brigam. Na pratica o piso de 5 s e o teto de
	/// 10 s comem quase toda a variacao -- a tecnica e um "cinco a dez segundos parado" com uma
	/// janela estreita no meio. Nao arredondei nem "consertei": o piso e o teto sao o que a tecnica
	/// promete, e mexer na conta do meio mudaria a unica coisa que ela faz.
	/// </summary>
	public static double SegundosDeParalisia(double bpDoTiro, Fighter alvo)
	{
		double bruto = alvo.Ekidef * Math.Max(alvo.Etechnique, alvo.Ekiskill)
					   * CombatMath.BpModulus(bpDoTiro, alvo.expressedBP);
		return Math.Min(Math.Max(5, bruto), 10);
	}

	/// <summary>
	/// `mods` NO INSTANTE DO DISPARO. Duas formulas, e a diferenca entre elas e por que existem
	/// duas pericias de tiro:
	///   * bola  -- `Ekioff * Ekiskill` (`customattacks.dm:509`);
	///   * raio  -- `Ekioff * Ekiskill * log_10(max(kieffusionskill,10)) * log_10(max(beamskill,10))`
	///              (`beams.dm:152`), vezes o `powmod`, que e o `base_damage` da tecnica.
	///
	/// Com as duas pericias zeradas os dois logs valem 1 e as formulas coincidem -- o que e o
	/// estado de um personagem novo, e e por isso que nao da pra "simplificar" uma na outra: elas
	/// so divergem depois de alguem treinar, que e exatamente quando a diferenca deve aparecer.
	/// </summary>
	public static double ModsDoTiro(Fighter f, TipoDeProjetil tipo)
	{
		double m = f.Ekioff * f.Ekiskill;
		if (tipo != TipoDeProjetil.Beam) return m;

		double efusao = Math.Log(Math.Max(f.kieffusionskill, 10)) / Math.Log(10);
		double raio = Math.Log(Math.Max(f.beamskill, 10)) / Math.Log(10);
		return m * efusao * raio;
	}

	/// <summary>
	/// O `mods` DE AGORA -- e ele muda com a distancia ja viajada.
	///
	/// `mods = proprietor.beammods * (rangemod ** (maxdistance - distance))` (`objects.dm:275`).
	/// Com `rangemod` menor que 1 o raio MORRE andando (o Masenko do DM e assim); maior que 1 ele
	/// engrossa. E o unico jeito de uma tecnica dizer "sou boa de perto" ou "sou boa de longe" sem
	/// mudar o alcance.
	/// </summary>
	public double ModsAgora()
	{
		double andou = MaxDistancia - Distancia;
		if (Math.Abs(RangeMod - 1) < 1e-9 || andou <= 0) return ModsBase;

		// MULTIPLICACAO REPETIDA e nao `Math.Pow`, pela mesma razao do `NiveisDeSkill.BarreiraEm`:
		// as duas pontas tem que chegar no mesmo numero, e `Pow` nao garante isso entre libms. O
		// expoente e o alcance em tiles, no maximo algumas dezenas.
		double m = ModsBase;
		int n = (int)Math.Min(andou, 512);
		for (int i = 0; i < n; i++) m *= RangeMod;
		return m;
	}

	/// <summary>
	/// O PODER DESTE TIRO NUM EMBATE: `BP * mods * basedamage` (`objects.dm:503`).
	///
	/// Usa o <see cref="ModsAgora"/> e nao o <see cref="ModsBase"/> de proposito: uma tecnica com
	/// `rangemod` menor que 1 chega FRACA no encontro se o encontro foi longe, e uma que engrossa com
	/// a distancia chega forte. Congelar o poder no disparo apagaria o unico jeito que uma tecnica
	/// tem de dizer "sou boa de perto".
	/// </summary>
	public double PoderDeEmbate() => EmbateDeKi.PoderDoFeixe(Bp, ModsAgora(), BaseDano);

	/// <summary>
	/// ESTE TIRO EMPURRA? `maxdistance - distance <= 2` empurra com forca cheia; ate 4 tiles,
	/// metade; alem disso nao empurra (`objects.dm:449-455`: *"harder to knock back at range"*).
	///
	/// Devolve o MULTIPLICADOR da forca, 0 quando nao empurra.
	/// </summary>
	public double FatorDeEmpurrao()
	{
		double andou = MaxDistancia - Distancia;
		if (andou <= 2) return 1.0;
		if (andou <= 4) return 0.5;
		return 0;
	}

	/// <summary>O comprimento do rastro, em pixels. Zero pra bola -- ela nao tem rastro.</summary>
	public float Comprimento => Tipo == TipoDeProjetil.Beam ? (Pos - Cauda).Length : 0;
}

/// <summary>
/// POR QUE ESTE TIRO ACABOU. Vai pro cliente junto do aviso de morte porque cada um desenha
/// diferente: bateu em corpo estoura, bateu em parede lasca, venceu o prazo apaga.
/// </summary>
public enum FimDeProjetil : byte
{
	/// <summary>Ainda esta vivo (valor de quem nunca morreu).</summary>
	Nenhum = 0,

	/// <summary>Acertou um corpo.</summary>
	Acertou = 1,

	/// <summary>Bateu no cenario.</summary>
	Cenario = 2,

	/// <summary>Acabou o alcance (`distance` zerou) ou o `Burnout` de 5 s venceu.</summary>
	Apagou = 3,

	/// <summary>O alvo defletiu ou absorveu.</summary>
	Defletido = 4,

	/// <summary>O dono soltou (ou nao aguentou) o raio, e o rastro terminou de se esvaziar.</summary>
	Cessou = 5,
}
