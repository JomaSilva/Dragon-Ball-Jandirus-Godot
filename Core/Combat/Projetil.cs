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

	/// <summary>
	/// `speed` 1..5 -- o numero do DM, como a tecnica o escreve. Ver <see cref="Projetil.SegundosPorTile"/>:
	/// ele vira ATRASO, nao velocidade, e a conta que o converte (<see cref="Projetil.AtrasoDeBola"/>,
	/// <see cref="Projetil.AtrasoDeRaio"/>) e onde mora a pressa que o dono pediu.
	/// </summary>
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
	/// NASCE PERSEGUINDO o alvo marcado de quem atira, dentro da curva do <see cref="Combat.Teleguiado"/> (dono,
	/// 2026-10-09). E o que a mesa de tecnicas liga num RAIO (`TecnicaCustomizada.Teleguiado`). A bola teleguiada
	/// nao passa por aqui: ela e um TIPO (<see cref="TipoDeProjetil.Guided"/>), e a tecnica que a atira escreve o
	/// alvo por conta propria. Sem ninguem marcado o tiro sai reto, como qualquer outro.
	/// </summary>
	public bool Teleguiado;

	/// <summary>
	/// ESTE RAIO ESTOURA NO PRIMEIRO CORPO QUE ACERTA, como uma bola: UM golpe, e acabou.
	///
	/// DIVERGENCIA DECLARADA DO DM (dono, 2026-10-08, sobre o Death Beam: *"ao se chocar ele diferente dos
	/// outros beams ele explode igual um blast causando 1 hit apenas"*). No original o Death Beam e um
	/// `WaveAttack` como os outros cinco raios nomeados (`beams/DeathBeam.dm:29-57`): a cabeca fica presa no
	/// corpo e o trem de segmentos bate de novo a cada `sleep(2)` (`beams.dm:205`). Com isto ligado ele nao se
	/// planta, nao moi, nao carrega ninguem e nao vira embate contra a guarda -- o impacto e o de uma bola
	/// (<see cref="Projetil.EstouraComoBola"/>). So tem efeito num `Beam`: a bola ja estoura.
	/// </summary>
	public bool EstouraNoImpacto;

	/// <summary>
	/// QUANTAS VEZES MAIS RAPIDO QUE NO DM este tiro voa, quando isso NAO sai da regra do tipo (0 = sai:
	/// <see cref="Projetil.PressaDeRaio"/> no raio, o `lag` na bola). So a tecnica cuja velocidade foi posta
	/// fora da conta comum a declara -- hoje o Death Beam. Ver <see cref="Projetil.Pressa"/>.
	/// </summary>
	public double PressaSobreODm;

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
	/// ESTE TIRO ARREMESSA QUEM ACERTA? -- e a resposta do DM e "quase sempre, MENOS a paralisia".
	///
	/// ============================ O DM SO EMPURRA POR DOIS CAMINHOS, E ELES SE EXCLUEM ============================
	/// `objects.dm:450-460`, o `Bump` de um blast, inteiro:
	///
	///     if(WaveAttack)                                    // 1) e um RAIO
	///         if(maxdistance-distance&lt;=2 &amp;&amp; dmg*BPModulus&gt;0.25 &amp;&amp; !M.KB) Knockback(M, ...)
	///         else if(maxdistance-distance&lt;=4 &amp;&amp; dmg*BPModulus&gt;0.5 &amp;&amp; !M.KB) Knockback(M, 0.5*...)
	///         spawn(1) MiniStun(M)
	///     else if(kiforceful &amp;&amp; dmg*BPModulus&gt;0.5 &amp;&amp; ...) // 2) e uma BOLA marcada `kiforceful`
	///         Knockback(M, 0.75*...)
	///
	/// Ou seja: uma bola que **nao** e `kiforceful` NAO empurra ninguem, nunca. E a Paralysis
	/// (`Ki2.0/Debuffs.dm:3-40`) e exatamente isso -- ela nao e `WaveAttack` e **nao escreve
	/// `kiforceful`**, ao contrario da irma Stunlock (`meta.dm:59`), que escreve.
	///
	/// ============================ QUEM ACHOU FOI A `--arsenalteste`, E ELA ESTAVA CERTA ============================
	/// A bancada afirma *"quem esta paralisado CONTINUA podendo atacar (nao virou stun)"* -- que e o
	/// ponto inteiro da tecnica -- e reprovava. A causa nao era a paralisia: era o ARREMESSO. O tiro
	/// batia, `Arremessar` escrevia `TiquesDeVoo`, e `CombatState.PodeAtacar()` recusa quem esta sendo
	/// arremessado. A tecnica que existe pra tirar as pernas do inimigo estava tirando os bracos junto,
	/// por dois segundos, atraves de um empurrao que o original nao da.
	///
	/// ============================ E POR QUE O PADRAO E `true` ============================
	/// Porque o port nao tem o campo `kiforceful`, e trocar o padrao aqui apagaria o empurrao de TODA
	/// bola do jogo (bola basica, tiro carregado, barragens, Kienzan) numa passada de madrugada, sem o
	/// dono ter pedido. **Isso e uma divergencia conhecida e ela fica anotada aqui**: hoje este port
	/// empurra com bola onde o DM so empurra com raio ou com bola `kiforceful`. Auditar as ~20 receitas
	/// contra o `kiforceful` do DM e uma passada propria, com bancada propria -- e quando ela vier, o
	/// lugar de mexer e este campo, uma receita por vez.
	/// ==========================================================================================================
	/// </summary>
	public bool Empurra = true;

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

	/// <summary>
	/// A FOLHA DE ARTE, quando a tecnica JA sabe qual e -- e ela quase sempre nao sabe.
	///
	/// ============================ QUEM DECIDE E A TABELA, NAO O VERB ============================
	/// <see cref="ArteDeKi.Nenhuma"/> (o padrao) quer dizer *"pergunte ao
	/// <see cref="ArteDeProjetil"/>"*, e e assim que as vinte e quatro tecnicas portadas funcionam:
	/// elas dizem QUAL VERB estao atirando e a arte sai da tabela, que e o unico lugar que decide.
	/// Se cada receita escrevesse a propria folha, a tabela viraria enfeite e a pergunta "que arte
	/// tem o Masenko?" teria duas respostas.
	///
	/// O CAMPO EXISTE PARA UMA COISA SO: a tecnica CUSTOMIZADA, onde a arte e escolha do jogador e
	/// portanto nao pode sair de tabela nenhuma -- ela sai do save dele
	/// (`TecnicaCustomizada.Arte`). E o mesmo desenho do `if (S.attackicon != null)` do DM
	/// (`customattacks.dm:437-440`): o que a tecnica declarou vence; sem declaracao, cai no padrao.
	/// =====================================================================================
	/// </summary>
	public ArteDeKi Arte = ArteDeKi.Nenhuma;

	/// <summary>
	/// A ESCALA DO DESENHO PEDIDA PELA TECNICA (0 = a regra de sempre: `MultDeOnda` no raio, 1 na bola).
	///
	/// Existe pelas bolas que CRESCEM enquanto sao formadas -- a Death Ball (`DeathBall.dm:81`,
	/// `nA.Scale(1+movestrength/4, ...)`) e a Genkidama (`SpiritBomb.dm:119-121`, `prevscale += 0.1`).
	/// La a `transform` e reescrita na bola viva; aqui a escala viaja UMA vez, no `Nasceu`, entao a bola
	/// que cresce RENASCE com a escala nova -- e a receita e a unica porta por onde ela entra. Ver o
	/// lote G12.
	/// </summary>
	public double EscalaVisual;

	/// <summary>
	/// NASCE INVISIVEL -- o `A.invisibility = 1` da lamina de ar do Kiai (`Ki2.0/Kiai.dm:40`).
	///
	/// E DADO DA RECEITA, e nao da arte: a folha diz COMO o tiro se desenha, isto diz PRA QUEM. O
	/// servidor nao filtra nada (o tiro continua acertando, explodindo e sendo defletido como qualquer
	/// outro -- no DM tambem: `invisibility` so mexe em quem VE); quem decide se desenha e o cliente,
	/// pela raca de quem olha (`VisaoDoInvisivel`).
	/// </summary>
	public bool Invisivel;
}

/// <summary>
/// UM PROJETIL VIVO -- a primeira coisa deste jogo que sai de um corpo, anda entre tiques e
/// acerta outro.
///
/// ============================ NAO HAVIA O QUE REUSAR ============================
/// As ~33 tecnicas ja portadas sao TODAS instantaneas: escolhem alvo por raio
/// (`AlvoDeTecnica`) e aplicam o dano no mesmo instante, pelo funil do soco. Nenhuma tem
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
/// (Isto e a LETRA do DM. O que o BYOND faz com ela foi MEDIDO em 2026-10-08 e esta no bloco "A VELOCIDADE",
/// la embaixo: a bola some no primeiro passo, o lag vale em tiques inteiros e o `Burnout()` nao corre.)
///
/// Aqui o passo continua sendo por tile e com o mesmo atraso -- o que muda e a REPRESENTACAO do
/// beam: um objeto so, com <see cref="Pos"/> na cabeca e <see cref="Cauda"/> no fim do rastro.
/// Enquanto o dono canaliza, a cauda e a mao dele (o trem esta sendo alimentado); quando ele
/// solta, a cauda passa a andar tambem e o rastro se esvazia ate sumir. O desenho e identico e o
/// custo de tique cai de N segmentos pra 1 -- e o custo de tique e a regra 0.4 da casa.
///
/// O QUE SE PERDEU: no DM da pra encurvar um beam ja disparado (`beamturndelay`, `linear=0`), e
/// aqui nao -- o preco seria guardar a lista de segmentos de volta. O que o dono pediu depois
/// (2026-10-09) foi o raio TELEGUIADO, e ele cabe no objeto unico: o feixe gira inteiro em torno da
/// cauda, sem entortar. Ver <see cref="Teleguiado"/>.
/// =======================================================================================
/// </summary>
public sealed class Projetil
{
	public int Id;

	/// <summary>Quem atirou (`proprietor`). Zero = ninguem -- o tiro continua valendo.</summary>
	public int Dono;

	/// <summary>
	/// O corpo perseguido -- a bola <see cref="TipoDeProjetil.Guided"/> e o raio de receita teleguiada
	/// (<see cref="ReceitaDeProjetil.Teleguiado"/>). Zero = voa reto. Quanto ele vira atras do alvo e do
	/// <see cref="Teleguiado"/>.
	/// </summary>
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

	/// <summary>
	/// O RUMO EM QUE ELE SAIU -- a regua do leque do teleguiado (<see cref="Teleguiado.DesvioMaximoEmGraus"/>):
	/// por mais que o alvo ande, o tiro nunca aponta a mais do que isso deste rumo. Nulo = ele ainda nao saiu
	/// (a bola que nasce parada em volta do alvo), e o primeiro passo da caca o escreve. Quem REAPONTA o tiro
	/// (a bola devolvida pelo parry) reescreve.
	/// </summary>
	public Vec2 RumoDaSaida;

	/// <summary>`distance`: quantos tiles ainda pode andar. Zerou, acabou.</summary>
	public double Distancia;

	/// <summary>`maxdistance`: com quantos nasceu. Ver <see cref="ModsAgora"/> e o empurrao de perto.</summary>
	public double MaxDistancia;

	public double RangeMod = 1;

	/// <summary>`mods` no instante do disparo -- `Ekioff * Ekiskill` e amigos. Ver <see cref="ModsDoTiro"/>.</summary>
	public double ModsBase = 1;

	/// <summary>
	/// `BP`: o `expressedBP` de quem atirou vezes o <see cref="MultDeOnda"/>, como saiu da mao
	/// (`A.BP = expressedBP * wavemult`, `beams.dm:139`).
	///
	/// CONGELADO NO DISPARO -- **menos enquanto o feixe DISPUTA**. Numa colisao de ki ele acompanha o
	/// poder de AGORA do dono, tique a tique (ver <see cref="LerOPoderDoDono"/>): quem se transforma ou
	/// acende um aumento de poder no meio do encontro empurra com a forca nova.
	/// </summary>
	public double Bp;

	/// <summary>
	/// `wavemultipl`: por quanto esta tecnica multiplica o BP de quem atira (`N.wavemultipl = wavemult`,
	/// `beams.dm:139`). 1 em quase tudo; 4 no Final Flash, 2 no Massive Beam, 1,2 no Tiro Carregado.
	///
	/// VIAJA COM O TIRO porque o <see cref="Bp"/> deixou de ser escrito uma vez so: pra reler o poder
	/// do dono no meio de uma disputa e preciso refazer a conta do disparo, e sem este campo o Final
	/// Flash perderia os 4x dele no primeiro tique do encontro.
	/// </summary>
	public double MultDeOnda = 1;

	/// <summary>
	/// O PODER DE AGORA DO DONO ENTRA NO FEIXE -- a MESMA linha do disparo (`BP = expressedBP *
	/// wavemult`), com o `expressedBP` deste instante. Quem chama e a colisao de ki
	/// (`GameServer.LerOPoderDeAgora`), a cada tique da disputa.
	/// </summary>
	public void LerOPoderDoDono(double expressedBpDeAgora) => Bp = expressedBpDeAgora * MultDeOnda;

	public double BaseDano = 0.8, MaxDano;

	/// <summary>`murderToggle` de quem atirou: este tiro pode matar?</summary>
	public bool Letal;

	public bool Deflectivel = true, Piercer, Fisico;

	/// <summary>
	/// `ReceitaDeProjetil.EstouraNoImpacto`, copiado no disparo -- e pra quem nasce deste tiro (a parte de la de
	/// um corte, o trecho desviado): o pedaco de um raio que estoura tambem estoura.
	/// </summary>
	public bool EstouraNoImpacto;

	/// <summary>
	/// DEFEITO INJETADO (bancada): o raio que estoura no primeiro corpo volta a ser um raio como os outros -- a
	/// cabeca se planta em quem acertou e moi a cada 0,2 s. Era o Death Beam ate 2026-10-08.
	/// </summary>
	public static bool RaioQueEstouraMoiDeTeste;

	/// <summary>
	/// ESTE TIRO ACABA NO PRIMEIRO CORPO, COM UM GOLPE SO, mesmo sendo raio? E a pergunta que o impacto faz
	/// (`GameServer.Acertar`, `Matar`) -- ver `ReceitaDeProjetil.EstouraNoImpacto`.
	/// </summary>
	public bool EstouraComoBola => EstouraNoImpacto && !RaioQueEstouraMoiDeTeste;

	/// <summary>
	/// `paralysis` -- ver o mesmo campo na receita. O <see cref="MultDeOnda"/> NAO tem irmao aqui de
	/// proposito: ele ja foi consumido no disparo, dentro do <see cref="Bp"/>. Guardar os dois
	/// separados criaria a segunda resposta pra "qual e o poder deste tiro".
	/// </summary>
	public bool Paralisia;

	/// <summary>`kiforceful`/`WaveAttack` -- ver o mesmo campo na receita, onde a regra do DM esta escrita.</summary>
	public bool Empurra = true;

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
	/// A ULTIMA CELULA EM QUE ELE JA ARANHOU O CHAO -- a guarda que faz o sulco sair UMA VEZ por
	/// tile, e nao uma vez por sub-passo.
	///
	/// E o mesmo campo (e a mesma regra) que o corpo arremessado ja tem (`ServerPlayer.UltimoSulco`):
	/// o avanco e fatiado em sub-passos de meio tile, entao sem isto tres fatias na mesma celula
	/// dariam tres marcas sobrepostas -- que leem como mancha, nao como rastro. Ver
	/// `GameServer.Empurrao.CarimbarSulco`, que e quem consome os dois.
	///
	/// MORA AQUI E NAO NUM DICIONARIO DO SERVIDOR porque a alternativa seria uma busca por tiro por
	/// sub-passo no tique -- e o tique dos projeteis e o lugar onde este projeto mede custo.
	/// </summary>
	public Vec2 UltimoSulco;

	/// <summary>
	/// Quanto tempo, em segundos, pra andar UM tile.
	///
	/// No DM isto e o `lag` do `walk`, em tiques de 0,1 s, e ele e INVERSO da velocidade:
	///   * blast/guided -- `lag = max(1, round(4 - speed))` (`customattacks.dm:519`);
	///   * beam         -- `beamspeed = 1 / speed` (`customattacks.dm:452`).
	///
	/// AQUI ELE NAO E MAIS O DO DM (dono, 2026-10-08: os ataques de ki estavam lentos demais) -- a regra e a
	/// divergencia estao escritas junto de <see cref="AtrasoDeBola"/> e <see cref="AtrasoDeRaio"/>, que sao
	/// quem o calcula no disparo. Com `speed = 1` a bola leva 1/16 s por tile (512 px/s) e o raio 1/20 s
	/// (640 px/s): o raio continua na frente, e nenhum dos dois perde mais de quem corre.
	/// </summary>
	public double SegundosPorTile = 1.0 / 16;

	/// <summary>
	/// QUANTAS VEZES MAIS RAPIDO QUE NO DM ESTE TIRO VOA (1 = a velocidade do original). Escrita no disparo,
	/// junto do <see cref="SegundosPorTile"/>, e copiada pra quem nasce dele (a parte de la de um corte, o
	/// trecho desviado).
	///
	/// ELA NAO MOVE NADA: quem move e o `SegundosPorTile`. Ela existe pras regras que o DM mede em DISTANCIA
	/// e que sao, na verdade, tempo de aviso -- ver <see cref="AvisoDe"/> e o bloco da velocidade la embaixo.
	/// </summary>
	public double Pressa = 1;

	/// <summary>Segundos ja acumulados rumo ao proximo tile. E o que fatia o passo grosso do DM.</summary>
	public double Acumulado;

	/// <summary>`Burnout()`: 50 tiques = 5 s de vida, aconteca o que acontecer.</summary>
	public double VidaRestante = SegundosDeBurnout;

	/// <summary>O dono ainda esta segurando o raio. So o Beam usa.</summary>
	public bool Canalizando;

	/// <summary>
	/// AINDA ESTA SENDO FORMADA (ou e um alvo de treino): NAO anda, NAO colide, NAO gasta alcance --
	/// so o prazo corre. E a bola parada sobre a cabeca de quem carrega a Death Ball ou a Genkidama
	/// (`A.loc = locate(usr.x, usr.y+1, usr.z)` com `density = 1` e sem `walk`: no BYOND uma bola
	/// parada e um obstaculo, porque so o MOVEDOR chama `Bump`), e o `training_obj/Ki_Target`.
	/// Um `Projetil` comum com este bit, e nao um segundo tipo de entidade -- ver o lote G12.
	/// </summary>
	public bool Inerte;

	/// <summary>Ja acertou alguem e esta EMPURRANDO contra ele -- a cabeca para e continua moendo.</summary>
	public bool Encostado;

	/// <summary>
	/// O CORPO EM QUE ESTA CABECA ESTA BATENDO SEM FERIR (zero = ninguem) -- o ramo do corte dos fracos
	/// (`objects.dm:355-357`, ver <see cref="DanoDeKi.CorteDoFraco"/>): o raio fica parado na frente dele, e o corpo
	/// pode andar raio adentro (*"they should be able to walk through beams"*, `objects.dm:451`).
	///
	/// E MEMORIA DE UM CICLO: nasce na batida que nao feriu (`GameServer.EstourarSemFerir`) e cai quando o ciclo
	/// dela vence e a cabeca volta a testar o mundo (`GameServer.AndarProjetil`, passo 4). Enquanto vale, a cabeca
	/// RECUA na frente do corpo que avanca contra ela (`GameServer.RecuarNaFrenteDeQuemAvanca`) -- sem ela, quem
	/// entrasse no raio ficava com a cabeca desenhada em cima do corpo ate a batida seguinte.
	/// </summary>
	public int BatendoSemFerir;

	/// <summary>
	/// O CORPO QUE ESTA CABECA ESTA LEVANDO NA FRENTE. Zero = nao esta levando ninguem.
	///
	/// ============================ ISTO E PORTE, E A RECEITA E A DO DU ============================
	/// Pedido do dono: *"ao ACERTAREM alguem eles deveriam EMPURRAR A PESSOA JUNTO conforme o beam vai
	/// indo"*. Nao e desenho novo -- e a metade do `Bump` de beam que faltava, e as duas fontes do DM
	/// escrevem METADES DIFERENTES da mesma cena:
	///
	///   * Finale (`objects.dm:449-457`) -- so o IMPULSO de perto: `maxdistance-distance <= 2` joga com
	///     forca cheia, ate 4 tiles com metade, alem disso nada (*"harder to knock back at range"*).
	///     Ver <see cref="FatorDeEmpurrao"/>, que ja estava portado.
	///   * DU (`Projectile System/Projectiles.dm:573-591`) -- o mesmo corte em 4 tiles
	///     (`beam_stun_start = 4`, `death.dm:727`) e, do outro lado dele, o ARRASTO:
	///
	///         else
	///             if(getdist(Owner,P)&lt;10)
	///                 P.has_delay=0
	///                 step(P,dir,32)          // um tile, no rumo do raio, POR CICLO
	///                 P.has_delay=1
	///                 P.dir=turn(dir,180)
	///                 if(getdist(Owner,P)==10) BigCrater(...)
	///
	/// OS DOIS CONCORDAM NO NUMERO 4, e e por isso que os dois cabem juntos sem escolha arbitraria:
	/// perto o raio ARREMESSA (o funil do soco, <see cref="Empurrao"/>), longe ele CARREGA, e aos 10
	/// tiles larga com uma cratera.
	/// ============================================================================================
	///
	/// ============================ POR QUE `step(...,32)` VIROU "O MESMO DELTA DA CABECA" ============================
	/// La o passo e de um TILE inteiro e ele nao dispara mais rapido que o feixe porque e o proprio
	/// feixe que o dispara: o empurrao so acontece enquanto a vitima esta NA CELULA da cabeca, e um
	/// `step` a tira de la. Ou seja o DM ja resolve, por ocupacao de tile, a pergunta "a que velocidade
	/// ele e carregado" -- e a resposta e A DO FEIXE.
	///
	/// Aqui nao ha ocupacao de tile: a cabeca e um ponto e o avanco e fatiado em pixels. Copiar o
	/// `step` literal daria um tile por sub-passo -- ate quatro tiles por tique, dez vezes o proprio
	/// raio. Entao o que se porta e o EFEITO medido, que e o unico que o jogador ve: o corpo anda
	/// exatamente o que a cabeca andou, no mesmo sub-passo. Fica anotado como traducao, nao como
	/// mudanca de regra.
	/// =============================================================================================================
	/// </summary>
	public int Arrastando;

	/// <summary>
	/// A DISTANCIA JA VIAJADA, EM TILES -- `maxdistance - distance` do DM.
	///
	/// Ela e a mesma pergunta que o DU faz por `getdist(Owner, P)`: a cabeca do feixe nasce na mao do
	/// dono, entao "quanto ela andou" e "a que distancia do dono ela esta". Estava escrita a mao em
	/// tres lugares (<see cref="ModsAgora"/>, <see cref="FatorDeEmpurrao"/> e o arrasto) e virou nome
	/// quando o terceiro chegou.
	/// </summary>
	public double AndouTiles => MaxDistancia - Distancia;

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

	/// <summary>
	/// ESTA CABECA ESTA ESPERANDO O TRONCO DE OUTRO FEIXE SAIR DO CAMINHO (dono, 2026-09-07: dois
	/// feixes que se cruzam nao disputam; o que bate no tronco do outro fica parado). Reescrito a cada
	/// tique pelo avanco -- e leitura, nao estado; serve ao diagnostico e a bancada.
	/// </summary>
	public bool Esperando;

	/// <summary>O FEIXE DE QUE ESTE NASCEU POR CORTE (zero = nasceu de uma mao). Ver `GameServer.Feixe.cs`.</summary>
	public int NascidoDoCorte;

	// ============================ O RAIO DESVIADO PELO PARRY (dono, 2026-09-25) ============================
	// Um raio que encontra um parry nao some nem volta: a cabeca fica PLANTADA no defensor e a energia sai
	// de la pro lado, num RAMO que o atirador continua alimentando -- a curva do Sparking Zero. Sao dois
	// objetos pelo mesmo motivo do corte (`NascidoDoCorte`): o feixe e um segmento reto da mao ate a cabeca,
	// e um raio torto e dois segmentos. Os quatro campos abaixo sao as duas pontas desse laco. Ver
	// `GameServer.ParryDeKi.cs`.
	//
	// NA TELA ELES SAO UM RAIO SO (dono, 2026-10-08): o ramo sai da DOBRA -- a cabeca plantada do pai, na
	// frente de quem desviou -- e o cliente desenha os dois trechos como uma fita unica que faz a curva
	// (`ProjetilState.Dobra`, `ProjetilDesenhado.DesenharDobrado`).
	// ======================================================================================================

	/// <summary>
	/// NO PAI: o corpo que esta desviando esta cabeca (zero = ninguem). Enquanto for de alguem, a cabeca
	/// fica plantada na frente dele e cada ciclo de moer vira "segurar o desvio" -- sem dano, sem arrasto.
	/// </summary>
	public int DesviadoPor;

	/// <summary>
	/// NO RAMO: o pai que o alimenta (zero = ramo solto, que voa com o comprimento que tem, como a parte
	/// de la de um corte). Enquanto o pai estiver sendo canalizado e desviado pelo mesmo corpo, a cauda do
	/// ramo fica presa no <see cref="PontoDoDesvio"/> e so a cabeca anda -- o ramo CRESCE.
	/// </summary>
	public int AlimentadoPor;

	/// <summary>
	/// NO RAMO: quem desviou. O ramo so continua alimentado enquanto o pai for desviado por ESTE corpo -- um
	/// segundo parry do mesmo raio, por outro corpo, e outro ramo.
	///
	/// E ESTE CORPO E CEGO PRO RAMO, PRA SEMPRE: a cabeca do ramo nao o acerta e ele nao corta o tronco
	/// (`Colidiu`, `CortarOndeEncostaram`). Ate 2026-10-08 isso era geometria -- a cauda nascia ALEM do corpo
	/// dele e nada do ramo ficava a frente --, e a exclusao escrita era letra morta. Com o ramo saindo da
	/// DOBRA, na frente de quem desviou e de lado, o corpo dele fica a um contato exato da cabeca recem-nascida
	/// (por construcao: a cabeca do pai e plantada a um contato dele): sem a regra, quem acerta o parry levaria
	/// o proprio desvio na cara. E o mesmo papel do `Dono`, que tambem nunca e alvo do proprio tiro.
	/// </summary>
	public int Desviador;

	/// <summary>
	/// NO RAMO: onde a cauda fica presa enquanto o pai o alimenta -- a DOBRA (<see cref="Feixe.DobraDoDesvio"/>),
	/// que acompanha a cabeca plantada do pai.
	/// </summary>
	public Vec2 PontoDoDesvio;

	// ============================ O RASPAO ENCERRA O ENCONTRO (2026-10-08) ============================
	// `prob(deflectchance/2)` no `Bump` (`objects.dm:358-363`): o corpo gira, da um passo de lado e o `Bump`
	// devolve sem dano. UM `Bump`, UM sorteio -- e o que vem depois depende de QUEM estava andando. MEDIDO no
	// BYOND 516, num mundo minimo com os dois ramos do `Move()` do tiro (`objects.dm:89-154`) copiados a letra:
	//
	//   * A BOLA QUE ANDA (reta ou `walk_towards`) SOME: o passo dela falhou, e o ramo `!WaveAttack` do `Move()`
	//     trata todo passo falhado como fim de alcance -- `if(!.) spawn explode(); src.loc=null` (`:92-99`), e o
	//     `explode()` adiado ja acha `loc` nula e nao explode nada. 1 `Bump`, nenhum dano, a bola fora do mundo;
	//   * A CABECA DO RAIO SEGUE VIAGEM: o ramo `WaveAttack` (`:100-113`) nao olha o que o passo devolveu, o corpo
	//     saiu do tile, e o `walk` seguinte passa. 1 `Bump`, e a cabeca foi ate a borda do mapa;
	//   * A BOLA PARADA (as do `Ki_Bomb`, sem `walk`) FICA: quem esbarra e o corpo (`mobBump.dm:57-58` chama o
	//     `Bump` dela na mao), nenhum `Move()` dela falhou, e o corpo e que e posto pra fora.
	// (AQUELE MUNDO MINIMO NAO TINHA o `obj/Move()` de `Code/Ki Attacks.dm:31-39`, que chama `..()` sem devolver o
	// valor. Com a cadeia inteira -- medida no mesmo dia -- TODO passo de bola chega "falhado" ao `Move()` do tiro:
	// ela some no primeiro, com raspao ou sem, e raspao de bola so existe a queima-roupa. A regra daqui nao muda; a
	// historia inteira esta no bloco "A VELOCIDADE", la embaixo.)
	//
	// O CORPO NAO DA O PASSO AQUI (dono, 2026-09-25: a esquiva e desenhada, as listras do Zanzoken) -- divergencia
	// declarada do `M.dir=pick(turn(M.dir,135),turn(M.dir,-135))` + `step(M,M.dir)` (`objects.dm:360-362`). Entao
	// "o corpo saiu da linha" vira MEMORIA DO TIRO: quem raspou nao e testado de novo (nem pela cabeca nem pelo
	// tronco) enquanto continuar em cima dele; saiu e voltou, e outro encontro -- o corpo do DM, um tile ao lado,
	// tambem so leva o raio de novo se pisar nele (`Crossed`, `objects.dm:156-171`).
	// (No DM, quem raspa CERCADO nao sai do tile, e a cabeca do raio da outro `Bump` a cada passo -- medido: 20 em
	// 20. Aqui nao ha passo que falhe.)
	// ==================================================================================================

	/// <summary>
	/// QUEM JA SAIU DA LINHA DESTE TIRO NUM RASPAO e continua em cima dele (ids de corpo; nulo = ninguem, que e
	/// o estado de quase todo tiro -- a lista so nasce no primeiro raspao). Quem a esvazia e o tique do servidor
	/// (`ReverQuemSaiuDaLinha`), pela regua de <see cref="Feixe.EmCimaDoTiro"/>.
	/// </summary>
	public List<int>? ForaDaLinha;

	/// <summary>Este corpo raspou neste tiro e ainda nao saiu de cima dele? Entao o tiro nao o enxerga.</summary>
	public bool EstaForaDaLinha(int corpo) => ForaDaLinha != null && ForaDaLinha.Contains(corpo);

	/// <summary>O corpo saiu da linha (raspao): o tiro deixa de enxerga-lo ate ele sair de cima e voltar.</summary>
	public void TirarDaLinha(int corpo)
	{
		ForaDaLinha ??= [];
		if (!ForaDaLinha.Contains(corpo)) ForaDaLinha.Add(corpo);
	}

	/// <summary>
	/// ESTE TIRO ACABA NO RASPAO? So a bola que esta ANDANDO -- e o passo DELA que falha (`objects.dm:92-99`). O
	/// raio segue e a bola parada fica; ver o bloco acima.
	/// </summary>
	public bool AcabaNoRaspao => Tipo != TipoDeProjetil.Beam && Rumo.LengthSquared >= 1e-6f;

	/// <summary>
	/// DEFEITO INJETADO (bancada): o raspao nao encerra o encontro -- a bola continua viva em cima do corpo, o
	/// raio nao lembra de quem saiu da linha, e o `Acertar` roda de novo a cada sub-passo (credito, treino e os
	/// DOIS sorteios outra vez). Era o jogo ate 2026-10-08: quem "esquivava" levava o tiro 16 px depois.
	/// </summary>
	public static bool RaspaoSorteiaDeNovoDeTeste;

	/// <summary>Como ele se chama no relato. A COR sai do dono -- ver o mesmo campo na receita.</summary>
	public string Nome = "ataque de ki";

	/// <summary>
	/// A ARTE COM QUE O CLIENTE VAI DESENHAR (ela escolhe o estilo -- ver <see cref="ArteDeKi"/>). Resolvida
	/// UMA VEZ, no disparo, pelo <see cref="ArteDeProjetil.De"/> -- e dali em diante ela e so um numero que viaja.
	/// </summary>
	public ArteDeKi Arte = ArteDeKi.Nenhuma;

	/// <summary>
	/// O TAMANHO DO DESENHO NA TELA -- e ele **nao** e poder, e por isso e um campo separado.
	///
	/// ============================ ISTO NAO CONTRADIZ O <see cref="Bp"/> ============================
	/// O `wavemult` do DM faz DUAS coisas na mesma linha e o port ja separou uma delas: o
	/// `A.BP = expressedBP * wavemult` (`beams.dm:139`) foi consumido dentro do <see cref="Bp"/> --
	/// e o comentario de <see cref="Paralisia"/> explica por que guardar o multiplicador ao lado do
	/// resultado criaria a segunda resposta pra "qual e o poder deste tiro".
	///
	/// A SEGUNDA coisa e `A.transform *= wavemult` (`beams.dm:149`): o desenho ENGORDA. E por isso
	/// que o Final Flash e um muro de energia e o Ki Wave e um fio -- e a diferenca some quando o
	/// desenho ignora a escala, que era o estado deste port (Final Flash e bola comum saiam com a
	/// mesma grossura).
	///
	/// Sao duas perguntas diferentes -- *quanto ele machuca* e *quantos pixels ele ocupa* -- e por
	/// isso sao dois campos. Ele sai do <c>MultDeOnda</c> da receita no nascimento e viaja no anuncio.
	///
	/// E ELE E LIDO PELA GEOMETRIA DO RAIO, dos dois lados: a frente da cabeca e a meia espessura do tronco
	/// sao a medida da arte VEZES esta escala (`Feixe.AlcanceDaCabeca`, `Feixe.MeiaEspessuraDoTronco`), e e
	/// por elas que o servidor encosta, disputa e corta -- e que o cliente desenha. (Aqui estava escrito
	/// "ninguem le este pra calcular nada"; deixou de ser verdade quando o Final Flash passou a encostar
	/// pela frente DESENHADA dele, em 2026-09-23.)
	/// ========================================================================================
	/// </summary>
	public double EscalaVisual = 1;

	// =====================================================================
	// O RAIO CRESCE COM O PODER DE QUEM O SEGURA (dono, 2026-10-08)
	// =====================================================================
	// O pedido: *"ao usar um beam e dar um power up (transformacao, kaioken ou oq for) q faca o bp subir o beam vai
	// ficar maior proporcionalmente com esse crescimento, entao se o cara vira um ssj nao masterizado (2x) no meio
	// de um clash, o beam vai aumentar o tamanho em 100%"* -- *"com limite de 3x o tamanho original, e o final flash
	// e o unico q isso n acontece"*.
	//
	// ============================ NAO HA NADA DISSO NO DM ============================
	// La o tamanho do raio e escrito uma vez (`A.transform *= wavemult`, `beams.dm:149`) e ninguem mais mexe nele;
	// o que a disputa rele a cada ciclo e o PODER (`BeamClash.dm:176-177`), e a tela nao mostrava a virada. Isto e
	// do port, por ordem do dono, e e so TAMANHO: o `Bp` do tiro continua com a regra dele (congelado
	// no disparo, vivo na disputa -- ver `LerOPoderDoDono`).
	//
	// ============================ "PROPORCIONAL AO CRESCIMENTO": CONTRA O QUE? ============================
	// Contra o MENOR poder que o dono expressou desde que o raio saiu (`PoderDeReferencia`), e nao
	// contra o do disparo. O `expressedBP` nao fica parado enquanto se segura um raio: ele cai com o Ki (o
	// `kiratio`, com piso de 0,6 -- `Fighter.Power.cs:40`), e o raio e a disputa cobram Ki o tempo todo. Medido
	// contra o disparo, quem vira Super Saiyajin com 70% do Ki veria o raio crescer 40% e nao os 100% do pedido:
	// o Ki ja gasto comeria o salto. Com a regua descendo junto, o salto de 2x vale 2x a qualquer momento; e
	// depois dele, se o poder escorre, o raio murcha na mesma proporcao. Nunca abaixo do tamanho com que nasceu.
	//
	// ============================ E O TAMANHO E REGRA, NAO SO DESENHO ============================
	// A frente da cabeca e a meia espessura do tronco saem da `EscalaVisual`
	// (`Feixe.AlcanceDaCabeca`, `Feixe.MeiaEspessuraDoTronco`): o raio que engrossa encosta, disputa e e cortado
	// pelo tamanho novo.
	// =====================================================================================================

	/// <summary>
	/// ESTE RAIO ENGROSSA COM O PODER DE QUEM O SEGURA? Todo raio que nasce de uma MAO (`GameServer.Disparar`),
	/// menos o do <see cref="VerboQueNaoCresce"/>. Quem nasce de outro tiro (a parte de la de um corte, o trecho
	/// desviado) nao tem mao: o trecho desviado copia o tamanho do pai, e o pedaco solto fica como estava.
	/// </summary>
	public bool CresceComOPoder;

	/// <summary>
	/// O UNICO RAIO QUE NAO CRESCE, pelo VERBO (dono, 2026-10-08: *"o final flash e o unico q isso n acontece"*)
	/// -- ele ja nasce um muro de quatro vezes. E pelo verbo, e nao por um campo da receita, de proposito: as
	/// cenas do trailer e as bancadas de foto disparam "Final_Flash" com a receita de bancada, e uma regra
	/// escrita na receita de producao nao valeria pra elas. O verbo e o que todos os caminhos tem em comum.
	/// </summary>
	public const string VerboQueNaoCresce = "Final_Flash";

	/// <summary>
	/// A ESCALA COM QUE ELE NASCEU: a que viajou no `Nasceu` (quem a escreve e o `GameServer.AnunciarProjetil`,
	/// junto do byte, pra as duas nunca discordarem). O crescimento e medido a partir dela, e o snapshot so
	/// carrega a escala de quem ja nao esta nela (`ProjetilState.Escala`).
	/// </summary>
	public double EscalaDeNascenca = 1;

	/// <summary>O MENOR `expressedBP` do dono desde o disparo: a regua do crescimento. Ver o bloco acima.</summary>
	public double PoderDeReferencia;

	/// <summary>O teto: tres vezes o tamanho com que o raio saiu da mao (o numero e do dono).</summary>
	public const double TetoDoCrescimento = 3;

	/// <summary>
	/// A QUE PASSO O TAMANHO ANDA ATE O ALVO, em vezes por segundo: o dobro em um quarto de segundo, o teto em
	/// meio. O poder da ficha muda em DEGRAU (o laco de fichas roda a 5 Hz e uma forma liga de uma vez), e um
	/// raio que dobrasse de um tique pro outro pularia de lugar junto -- a cabeca plantada num corpo e o ponto
	/// de encontro de uma disputa saem do tamanho dele.
	/// </summary>
	public const double CrescimentoPorSegundo = 4;

	/// <summary>
	/// O QUANTO O PODER TEM QUE SUBIR PRA O RAIO REAGIR: 5%, o passo em que a escala viaja no fio
	/// (`Protocol.EscalaDeProjetilEmByte`). Menos que isso e a ficha respirando -- o arredondamento do BP, a
	/// raiva mexendo um ponto --, e um raio que treme de tamanho a cada respiro seria ruido.
	/// </summary>
	public const double CrescimentoQueSeVe = 1.05;

	/// <summary>A maior escala que cabe no byte do fio (255 / 20). O crescimento para nela.</summary>
	public const double MaiorEscalaDoFio = 12.75;

	/// <summary>
	/// DEFEITO INJETADO (bancada): o raio fica do tamanho com que saiu da mao, suba o poder do dono quanto
	/// subir. Era o jogo ate 2026-10-08.
	/// </summary>
	public static bool RaioNaoCresceDeTeste;

	/// <summary>
	/// O PODER DE AGORA DE QUEM SEGURA ESTE RAIO -> O TAMANHO DELE. Um passo por tique do servidor, pro raio que
	/// uma mao ainda alimenta (`GameServer.AndarProjetil`).
	/// </summary>
	public void CrescerComOPoder(double expressedBpDeAgora, double dt)
	{
		if (!CresceComOPoder || RaioNaoCresceDeTeste || expressedBpDeAgora <= 0) return;

		// A REGUA SO DESCE: e o menor poder desde o disparo.
		if (PoderDeReferencia <= 0 || expressedBpDeAgora < PoderDeReferencia) PoderDeReferencia = expressedBpDeAgora;

		double alvo = Math.Min(expressedBpDeAgora / PoderDeReferencia, TetoDoCrescimento);
		if (alvo < CrescimentoQueSeVe) alvo = 1;

		double nascenca = EscalaDeNascenca > 0 ? EscalaDeNascenca : 1;
		double agora = EscalaVisual / nascenca, passo = CrescimentoPorSegundo * dt;
		agora = alvo > agora ? Math.Min(alvo, agora + passo) : Math.Max(alvo, agora - passo);
		EscalaVisual = Math.Min(nascenca * agora, Math.Max(MaiorEscalaDoFio, nascenca));
	}

	/// <summary>Copiado da receita no nascimento; viaja no anuncio. Ver `ReceitaDeProjetil.Invisivel`.</summary>
	public bool Invisivel;

	/// <summary>Morto: sai da lista no proximo tique. Ver o motivo em <see cref="Fim"/>.</summary>
	public bool Vivo = true;
	public FimDeProjetil Fim;

	// =====================================================================
	// OS NUMEROS DO DM
	// =====================================================================
	/// <summary>
	/// `Burnout()` sem argumento = `burnouttime=50` tiques (`objects.dm:655`).
	///
	/// DIVERGENCIA DECLARADA: no DM esse prazo so corre com o TEMPO PARADO -- o laco de dentro e
	/// `while(!TimeStopped&amp;&amp;!CanMoveInFrozenTime) sleep(1)` (`objects.dm:658`), e em tempo normal ele nao sai
	/// nunca. MEDIDO no BYOND 516 (2026-10-08): bola parada com `Burnout()` e com `Burnout(40)` continuava no mapa
	/// 15 s depois; com `TimeStopped = 1` saiu em 4,2 s. Aqui todo prazo de tiro (este, os 4 s das minas, os 120 s
	/// da teleguiada) corre sempre, com o numero que o verb escreve.
	/// </summary>
	public const double SegundosDeBurnout = 5.0;

	/// <summary>
	/// A CADENCIA DO TREM DE BEAM: `sleep(2)` no fim do `ShootBeam` (`beams.dm:209`) = 0,2 s -- de quanto em quanto o
	/// dono paga o Ki do raio e um segmento novo sai da mao. Aqui e ela, tambem, que dita de quanto em quanto um beam
	/// encostado volta a machucar.
	///
	/// DIVERGENCIA DECLARADA (dono, 2026-10-08): no DM o raio encostado QUE FERE NAO espera este ciclo. A cabeca presa
	/// num corpo esbarra de novo a cada passo do `walk(src, src.dir, beamspeed)` (`objects.dm:241`), e cada `Bump` e
	/// um golpe inteiro, com `DamageLimb` e `MiniStun` (`:439`, `:457`). MEDIDO no BYOND 516 (`world.fps = 12`): 12
	/// batidas por segundo -- uma por tique, 110 vaos de 0,83 decimo em 110 -- com `beamspeed` 1, 0,6, 0,5, 0,4, 0,3
	/// e 0,2 (lag 2 da 6 por segundo, lag 3 da 4). La o numero e o tique do mundo, que o admin muda (`World.dm:30`);
	/// aqui o raio que fere bate 5 vezes por segundo, fixas, e o `MiniStun` de raio nao foi portado. (O raio que NAO
	/// fere tem a cadencia dele, que e a medida no DM: <see cref="SegundosPorBatidaSemFerir"/>.)
	///
	/// ELA ANDA JUNTO COM O SORTEIO DE MEMBRO, que tambem diverge (ver `MeleeResolver.AplicarDanoPronto`): la 22% das
	/// batidas nao acham membro e so 22% caem em nucleo. Medido no Core com um raio de 13,3 por batida segurado em
	/// cima de um corpo inteiro: 5,7 s ate o nocaute hoje; 2,4 s so com as 12 batidas; 13,0 s so com o sorteio do DM;
	/// 5,4 s com os dois. Portar um sem o outro muda a letalidade do raio umas 2,4 vezes.
	/// </summary>
	public const double SegundosPorCicloDeBeam = 0.2;

	/// <summary>
	/// A CADENCIA DA BATIDA QUE NAO FERE: cinco tiques do mundo de 12 fps do DM (`World.dm:5`).
	///
	/// No ramo do corte (`objects.dm:355-357`, ver <see cref="DanoDeKi.CorteDoFraco"/>) a cabeca do raio nao moi: ela
	/// e marcada (`stoopme`), some no `Move()` seguinte (`objects.dm:149-154`), e o segmento de tras so vira cabeca
	/// no laco do `KHH()` (`sleep(2)`, `:194`), anda um tile e bate de novo. MEDIDO no BYOND 516 (2026-10-08): uma
	/// batida a cada 5 tiques -- 4,15 decimos de segundo -- contra uma POR TIQUE quando o raio fere. Cada batida
	/// ainda treina os dois lados (`:306-319`), entao esta cadencia e a taxa desse treino.
	/// </summary>
	public const double SegundosPorBatidaSemFerir = 5.0 / 12;

	/// <summary>
	/// O RAIO DA CABECA, em pixels. Um tile e 32; meio tile e o que faz "encostou" querer dizer
	/// encostou, e nao "passou a um metro".
	/// </summary>
	public const float RaioDeImpacto = 16f;

	// =====================================================================
	// A VELOCIDADE -- DIVERGENCIA DECLARADA DO DM (dono, 2026-10-08)
	// =====================================================================
	// O pedido: *"deixe os ataques de ki mais rapidos, principalmente esferas, e projeteis como kienzan,
	// blast etc eles estao mt lentos"*.
	//
	// ============================ O QUE O DM ESCREVE, O QUE ELE FAZ, E POR QUE ERA LENTO AQUI ============================
	// A LETRA (e contra ela que a regra nova se mede):
	//   * bola e teleguiada CUSTOMIZADAS -- `walk(A, dir, lag)` com `lag = max(1, round(4 - speed))` decimos de
	//     segundo (`customattacks.dm:520-521`, `:531-537`): 0,3 / 0,2 / 0,1 s por tile = 3,3 / 5 / 10 tiles por
	//     segundo. Os verbs NOMEADOS escrevem o `walk` direto: sem lag nenhum (`walk(A,usr.dir)`, `blasts.dm:78`,
	//     o Basic_Blast) ou com lag 2 (`Ki2.0/Debuffs.dm:37`, a Paralysis). Este port mede toda bola pelo `speed`
	//     da customizada (`ReceitaDeProjetil.Velocidade`), e a bola comum saiu com `speed = 1` -- o lag 3;
	//   * raio -- `walk(src, src.dir, beamspeed)` (`objects.dm:241`), com `beamspeed = 1 / speed` na customizada
	//     (`customattacks.dm:445`): 0,1 s por tile = 10 tiles por segundo.
	//
	// O QUE O BYOND FAZ COM ELA -- MEDIDO em 2026-10-08 (516.1684, `world.fps = 12` de `World.dm:5`), num mundo
	// minimo com os trechos copiados por linha:
	//   * NENHUMA BOLA VOA. `obj/Move()` (`Code/Ki Attacks.dm:31-39`) chama `..()` sem `. = ..()` e devolve nulo; o
	//     `Move()` do tiro le isso como passo falhado e tira a bola do mundo (`objects.dm:92-99`). Ela entra no tile
	//     seguinte e some na mesma chamada, no PRIMEIRO passo do `walk`, com lag 0, 1, 2 ou 3 -- so ha acerto de
	//     bola a queima-roupa (o `Bump` desse passo). O raio voa: o ramo `WaveAttack` (`:100-113`) nao olha o que
	//     o passo devolveu;
	//   * O LAG VALE EM TIQUES INTEIROS do mundo (0,83 decimo a 12 fps). Tirando o `obj/Move()` da cadeia, a bola
	//     anda 12 tiles por segundo com lag 0 ou 1, 6 com lag 2 e 4 com lag 3. A cabeca do raio, que voa de
	//     verdade, anda um tile por tique com `beamspeed` 1, 0,6, 0,5, 0,4, 0,3 e 0,2: 12 tiles por segundo -- a
	//     escada de `beamspeed` dos raios nomeados nao muda a velocidade de nenhum;
	//   * O `Burnout()` NAO CORRE em tempo normal (`objects.dm:658`) -- ver <see cref="SegundosDeBurnout"/>.
	// ENTAO "a velocidade do DM", neste arquivo e nas bancadas (`VelocidadeDoDmDeTeste`, `LagDeBolaNoDm`,
	// `AtrasoDeRaioNoDm`, a `Pressa`), e a da LETRA -- lag e `beamspeed` em decimos de segundo --, que e o que este
	// port fazia ate 2026-10-08. Nao e o que o jogo antigo mostrava: la a bola nao saia do lugar.
	//
	// POR QUE ERA LENTO AQUI: neste port o corpo anda 5 tiles por segundo e CORRE a 11 (`MoveRules.BaseSpeedPx`,
	// `MultiplicadorCorrida`). A bola comum, no lag 3, era mais lenta que alguem ANDANDO, e ate o raio perdia de
	// quem corre. Um tiro de que se foge a pe nao e um tiro.
	//
	// ============================ A REGRA NOVA ============================
	//   * BOLA: `22 - 2 x lag` tiles por segundo -- 20 / 18 / 16 pro lag 1 / 2 / 3 do DM. A ordem do
	//     original fica (quem pagou por `speed` continua mais rapido), mas a escada ACHATA: quem mais
	//     ganha e a bola comum (4,8 vezes), que era a queixa; a mais rapida dobra.
	//   * RAIO: o dobro (`PressaDoRaio`). O raio comum continua empatado com a bola mais rapida, como na
	//     letra do DM (10 = 10 la, 20 = 20 aqui). Os raios NOMEADOS ficam um pouco acima dele, entre 24 e 30
	//     (<see cref="VelocidadeDeRaioNomeado"/>), e o Death Beam muito acima (66,7) -- ver o bloco do `beamspeed`.
	// O PRAZO DE 5 s DESTE PORT (`SegundosDeBurnout`) NAO MUDOU, e por isso o alcance agora manda: no passo antigo
	// a bola comum morria com 17 tiles andados (5 s a 3,3) e hoje chega aos 30 que a receita dela sempre disse
	// (`AlcanceTiles`).
	//
	// ============================ E AS REGRAS MEDIDAS EM DISTANCIA ANDAM JUNTO ============================
	// Tres defesas do jogo enxergam o tiro por DISTANCIA (`view(1)`, "a dois tiles", `BCL_NPC_DETECT 7`):
	// a Precognicao, os sopros de Kiai e o contra-feixe do NPC. Distancia sobre velocidade e TEMPO DE
	// AVISO -- e um tiro quase cinco vezes mais rapido deixaria essas tres com um quinto do aviso, sem
	// ninguem ter pedido isso. Entao cada tiro carrega a propria <see cref="Pressa"/> (quantas vezes mais
	// rapido que no DM ele voa) e essas distancias sao multiplicadas por ela NA DIRECAO EM QUE ELE VEM
	// (<see cref="VemPraCimaDe"/>): o aviso, em segundos, e o do original -- e a largura da regra tambem.
	// =======================================================================================================

	/// <summary>Quantas vezes mais rapido que no DM um raio voa. Ver o bloco acima.</summary>
	public const double PressaDoRaio = 2;

	/// <summary>
	/// DEFEITO INJETADO (bancada): todo tiro volta ao passo da LETRA do DM, que este port fazia ate 2026-10-08 -- a
	/// bola comum a 3,3 tiles por segundo, perdendo de quem ANDA; o raio a 10, perdendo de quem corre. Sempre falso
	/// em jogo.
	/// </summary>
	public static bool VelocidadeDoDmDeTeste;

	/// <summary>O `lag` de uma bola no DM, em tiques de 0,1 s: `max(1, round(4 - speed))` (`customattacks.dm:519`).</summary>
	public static double LagDeBolaNoDm(double velocidade)
		=> Math.Max(1, Math.Round(4 - velocidade, MidpointRounding.AwayFromZero));

	/// <summary>Segundos por tile de um raio NO DM: `beamspeed = 1 / speed` tiques de 0,1 s (`customattacks.dm:452`).</summary>
	public static double AtrasoDeRaioNoDm(double velocidade) => 1.0 / Math.Max(velocidade, 0.01) * 0.1;

	/// <summary>
	/// SEGUNDOS POR TILE de uma bola, a partir do `lag` dela no DM (em tiques; nem sempre inteiro -- a Death
	/// Ball anda um tile por pulso de `Eactspeed / 5` tiques). `22 - 2 x lag` tiles por segundo, e NUNCA
	/// menos que o dobro do original: a reta cruzaria o zero num lag de 11, e um lag grande assim (um corpo
	/// muito lento guiando a Death Ball) continua valendo o dobro do que valia.
	/// </summary>
	public static double AtrasoDeBolaPorLag(double lagNoDm)
	{
		double lag = Math.Max(lagNoDm, 1);
		return VelocidadeDoDmDeTeste ? lag * 0.1 : 1.0 / Math.Max(22 - 2 * lag, 20 / lag);
	}

	/// <summary>Segundos por tile de uma bola ou teleguiada com este `speed`. Ver <see cref="AtrasoDeBolaPorLag"/>.</summary>
	public static double AtrasoDeBola(double velocidade) => AtrasoDeBolaPorLag(LagDeBolaNoDm(velocidade));

	/// <summary>Segundos por tile de um raio com este `speed`: o do DM (<see cref="AtrasoDeRaioNoDm"/>), na <see cref="PressaDeRaio"/>.</summary>
	public static double AtrasoDeRaio(double velocidade) => AtrasoDeRaioNoDm(velocidade) / PressaDeRaio;

	/// <summary>A <see cref="Pressa"/> de uma bola que no DM tinha este `lag`, em tiques.</summary>
	public static double PressaDeBolaPorLag(double lagNoDm)
		=> Math.Max(lagNoDm, 1) * 0.1 / AtrasoDeBolaPorLag(lagNoDm);

	/// <summary>A <see cref="Pressa"/> de um raio: a <see cref="PressaDoRaio"/> -- ou 1, com o defeito injetado.</summary>
	public static double PressaDeRaio => VelocidadeDoDmDeTeste ? 1 : PressaDoRaio;

	// ============================ O `beamspeed` DOS RAIOS NOMEADOS E UM ATRASO (2026-10-08) ============================
	// Os verbs de raio do DM escrevem `beamspeed` direto (`beamspeed=0.3`, `beams/DeathBeam.dm:48`), e quem o
	// consome e `walk(src, src.dir, beamspeed)` (`objects.dm:241,278`): e o LAG do passo, em decimos de segundo --
	// MENOR = MAIS RAPIDO. E o mesmo numero que a tecnica customizada calcula como `beamspeed = 1 / S.speed`
	// (`customattacks.dm:445`), ou seja o `speed` equivalente e `1 / beamspeed`.
	//
	// Os lotes G5 e G6 deste port o entregam como `ReceitaDeProjetil.Velocidade`, que e `speed` (MAIOR = mais
	// rapido): a ordem dos raios nomeados saiu INVERTIDA. O Death Beam, que o proprio DM descreve como *"low
	// drain, high speed"* (0,3 -- o segundo menor atraso do jogo), voava a 6 tiles por segundo; o Ki Wave comum,
	// a 20.
	//
	// O DEATH BEAM FOI ACERTADO PELA LETRA (dono, 2026-10-08: *"death beam deveria ser mt rapido"*): 1 / 0,3 de
	// `speed`, que na pressa de todo raio da 66,7 tiles por segundo.
	//
	// OS OUTROS FORAM NIVELADOS ENTRE 24 E 30 (dono, no mesmo dia: *"pode nivelar entre 24 e 30"*). Antes voavam
	// Kamehameha 8, Galick Ho 6 a 8, Enkumei 4 a 6, Dodon Ray 12, Final Flash 12, Massive Beam 10 e Boom Wave 4 --
	// todos atras do Ki Wave comum (20), e alguns atras de quem ANDA. Lidos pela letra iriam de 33 a 100, e um
	// Kamehameha a 50 nao se desvia depois da carga. O que se escolheu e o que o jogo antigo fazia NA PRATICA,
	// medido no BYOND (ver o bloco "A VELOCIDADE"): la a escada de `beamspeed` nao mudava a velocidade de nenhum
	// -- todo raio andava um tile por tique. Aqui eles ficam um pouco acima do raio comum, com a ORDEM do verb
	// (menor atraso, mais rapido): 0,2 -> 30 | 0,3 -> 28,5 | 0,4 -> 27 | 0,5 -> 25,5 | 0,6 -> 24 tiles por segundo.
	// ===================================================================================================================

	/// <summary>
	/// O `speed` que corresponde a um `beamspeed` do DM: `1 / beamspeed` (`customattacks.dm:445`). Ver o bloco
	/// acima. Com o defeito injetado devolve o proprio `beamspeed` -- a leitura que deixava o raio lento.
	/// </summary>
	public static double VelocidadeDeBeamspeed(double beamspeed)
		=> BeamspeedLidoComoSpeedDeTeste ? beamspeed : 1 / Math.Max(beamspeed, 0.01);

	/// <summary>
	/// O `speed` DE UM RAIO NOMEADO, a partir do `beamspeed` do verb dele: de 1,2 vezes o raio comum (o atraso
	/// maior, 0,6 -- Final Flash, Dodon Ray) a 1,5 vezes (o menor, 0,2 -- Boom Wave), em linha reta entre os dois.
	/// Na pressa de todo raio sao 24 a 30 tiles por segundo. Ver o bloco acima: e ordem do dono, nao a letra do DM.
	/// Com o defeito injetado devolve o proprio `beamspeed` -- a leitura que os deixava atras do raio comum.
	/// </summary>
	public static double VelocidadeDeRaioNomeado(double beamspeed)
	{
		if (BeamspeedLidoComoSpeedDeTeste) return beamspeed;
		double t = Math.Clamp((BeamspeedMaisLento - beamspeed) / (BeamspeedMaisLento - BeamspeedMaisRapido), 0, 1);
		return RaioNomeadoMaisLento + t * (RaioNomeadoMaisRapido - RaioNomeadoMaisLento);
	}

	/// <summary>O maior e o menor `beamspeed` que um verb de raio nomeado escreve (`FinalFlash.dm:45`, `beams.dm:490`).</summary>
	public const double BeamspeedMaisLento = 0.6, BeamspeedMaisRapido = 0.2;

	/// <summary>
	/// Os dois extremos do raio nomeado, em `speed` (vezes o raio comum): 24 e 30 tiles por segundo na
	/// <see cref="PressaDoRaio"/>. Os numeros sao do dono.
	/// </summary>
	public const double RaioNomeadoMaisLento = 1.2, RaioNomeadoMaisRapido = 1.5;

	/// <summary>
	/// DEFEITO INJETADO (bancada): o `beamspeed` dos raios nomeados volta a ser lido como `speed` -- o Death Beam a
	/// 6 tiles por segundo, o Kamehameha a 8, o Boom Wave a 4: todos atras do raio comum, e alguns atras de quem
	/// corre. Era o jogo ate 2026-10-08.
	/// </summary>
	public static bool BeamspeedLidoComoSpeedDeTeste;

	/// <summary>
	/// A <see cref="Pressa"/> de um raio que voa a este `speed`, medida contra o que um raio ANDAVA no DM: um
	/// tile por decimo de segundo (<see cref="AtrasoDeRaioNoDm"/> de 1 -- abaixo disso o `walk` nao passava do
	/// tique do mundo). E a que a tecnica mais rapida que a regra declara (`ReceitaDeProjetil.PressaSobreODm`):
	/// com ela as distancias de aviso continuam valendo o mesmo TEMPO que valiam no original.
	/// </summary>
	public static double PressaSobreORaioDoDm(double velocidade) => AtrasoDeRaioNoDm(1) / AtrasoDeRaio(velocidade);

	/// <summary>
	/// DEFEITO INJETADO (bancada): o tiro devolvido pela Deflexao volta no passo do EMPURRAO de uma disputa (o
	/// que o `Devolver` deixa nele) -- 10 tiles por segundo, mais devagar que qualquer bola e que quem corre.
	/// </summary>
	public static bool DeflexaoNoPassoDoEmpurraoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): toda distancia de AVISO volta a ser a do DM, sem a <see cref="Pressa"/> do
	/// tiro -- o jogo em que so a velocidade mudou, e a Precognicao, os sopros e o contra-feixe ficaram com
	/// uma fracao do tempo que tinham.
	/// </summary>
	public static bool AvisoSemPressaDeTeste;

	/// <summary>
	/// UMA DISTANCIA DE AVISO DO DM, NA PRESSA DESTE TIRO: quantos pixels antes ele tem que ser visto pra que
	/// o tempo ate chegar seja o do original. Ver o bloco "AS REGRAS MEDIDAS EM DISTANCIA".
	/// </summary>
	public float AvisoDe(float distanciaNoDm) => AvisoSemPressaDeTeste ? distanciaNoDm : distanciaNoDm * (float)Pressa;

	/// <summary>
	/// ESTE TIRO VEM PRA CIMA DESTE CORPO e chega no tempo que o DM dava a quem o via a `distanciaNoDm`? O corpo
	/// A FRENTE dele, no rumo, ate a distancia de aviso (<see cref="AvisoDe"/>) -- e numa faixa da LARGURA do
	/// original, nao mais.
	///
	/// SO PRA FRENTE, de proposito. Esticar a regra pra todo lado (um raio de dois tiles virando um de nove)
	/// faria um sopro apagar tiros que passam longe e quem ve o futuro pular de lado por causa de uma bola do
	/// outro lado da tela. O que a pressa encurtou foi o tempo de quem ESTA NA LINHA do tiro; e so esse que volta.
	/// </summary>
	public bool VemPraCimaDe(Vec2 corpo, float distanciaNoDm)
	{
		if (Rumo.LengthSquared < 1e-6f) return false;
		Vec2 d = corpo - Pos;
		float frente = d.X * Rumo.X + d.Y * Rumo.Y;
		return frente > 0 && frente <= AvisoDe(distanciaNoDm) && MathF.Abs(d.X * Rumo.Y - d.Y * Rumo.X) <= distanciaNoDm;
	}

	/// <summary>
	/// ESTE TIRO ESTA "A `distanciaNoDm`" DESTE CORPO, pra uma regra de alcance do DM? Dentro da distancia do
	/// original, como sempre -- ou vindo pra cima dele dentro do aviso (<see cref="VemPraCimaDe"/>).
	/// </summary>
	public bool AoAlcanceDe(Vec2 corpo, float distanciaNoDm)
		=> (corpo - Pos).LengthSquared <= distanciaNoDm * distanciaNoDm || VemPraCimaDe(corpo, distanciaNoDm);

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
		double andou = AndouTiles;
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
		double andou = AndouTiles;
		if (andou <= 2) return 1.0;
		if (andou <= 4) return 0.5;
		return 0;
	}

	/// <summary>
	/// ESTE TIRO CARREGA QUEM ACERTOU? -- o `else` do DU (`Projectiles.dm:583-591`), que e o outro
	/// lado do <see cref="FatorDeEmpurrao"/>.
	///
	/// Perto (ate 4 tiles) o feixe ARREMESSA e nao carrega: quem esta em cima do cano leva o impulso
	/// inteiro e sai voando pelo funil de sempre. Passando disso, e ate <see cref="TilesDeArrasto"/>,
	/// ele CARREGA. Nao ha faixa em que os dois valham -- e a mesma condicao com o sinal trocado, e e
	/// assim no DU (um `if`/`else`, nao dois `if`).
	///
	/// SO O RAIO. `WaveAttack` no Finale, `Beam` no DU: uma bola nao tem em que empurrar: ela toca e
	/// estoura. E a `Ki_Bomb`, que pare sete bolas PARADAS em volta do alvo, empurraria a vitima em
	/// sete rumos ao mesmo tempo.
	/// </summary>
	public bool PodeArrastar()
		=> Tipo == TipoDeProjetil.Beam && !EstouraComoBola && FatorDeEmpurrao() <= 0 && AndouTiles < TilesDeArrasto;

	/// <summary>
	/// ATE ONDE O FEIXE CARREGA, em tiles a partir da mao do dono: `if(getdist(Owner,P) &lt; 10)`
	/// (`Projectiles.dm:584`). Chegando la, o DU larga o corpo e abre um `BigCrater` no chao
	/// (`:589-591`) -- o fim do arrasto e um lugar, e nao um relogio.
	/// </summary>
	public const double TilesDeArrasto = 10;

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

	/// <summary>
	/// ============================ ENCOSTOU NUM PLANETA, VISTO DO ESPACO ============================
	/// O cenario do espaco -- ver `GameServer.Projeteis`, passo 6a-quater, e
	/// `GameServer.Destruicao.AtingirMundoComKi`.
	///
	/// **Ele existe por causa da ESCALA, e nao da regra.** A regra e a mesma do
	/// <see cref="Cenario"/> (o tiro acaba ali, e o servidor decide o que aquilo custou), e a primeira
	/// versao reusava aquele valor -- o que estava certo do lado do servidor e errado na tela: o
	/// desenho do `Cenario` e um estouro de 0,9 de escala, dimensionado pra a quina de um muro de
	/// 32 px. Um disco de planeta tem **220 a 440 px de diametro**, e o mesmo efeito em cima dele nao
	/// se ve: o jogador atira num mundo e a tela nao responde.
	///
	/// Um byte no enum (que ja viaja no pacote de morte) e um `case` no cliente compram um estouro na
	/// escala do alvo, pelas mesmas pecas que o resto do jogo usa (`CombatFx.Impacto` + `Onda`, que
	/// escala de 32 a 512 px sem serrilhar). Sem ele, K1 ficava certo no mundo e mudo na tela -- que e
	/// a familia de defeito que este projeto passa o tempo todo tentando nao repetir.
	/// ==========================================================================================
	/// </summary>
	Mundo = 6,
}
