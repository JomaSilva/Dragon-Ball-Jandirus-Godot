using Godot;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// O ZANZOKEN COMO DESLOCAMENTO -- piscar pra um ponto do chao, deixando a miragem pra tras.
///
/// ============================ DE ONDE VEM ============================
/// E o `turf/DblClick` do original (`click.dm:49-67`): quem tem a Afterimage Technique (`haszanzo`, que
/// o `after_learn` liga -- `misc.dm:80-82`, *"Try clicking a nearby tile"*) da duplo clique num tile e
/// aparece nele. O dono pediu exatamente esse gesto -- "faça com quem tenha after image ao dar double
/// click no chao ele de o teleporte e deixando a miragem a onde estava". (Ate 2026-09-24 este cabecalho
/// dizia que a fonte era o `Zanzoken_Dodge` -- `misc.dm:66-68`, o verb do nivel 3, que sorteia um tile em
/// volta --, e os tres numeros daqui tinham sido inventados em cima dessa leitura: alcance fixo de seis
/// tiles, 2,5% do Ki maximo e 900 ms de recarga. O `Zanzoken_Dodge` e o do lote G10.)
/// =====================================================================
///
/// ============================ OS TRES NUMEROS SAO OS DO DM, e o dono confirmou (2026-09-24) ============================
///   * ALCANCE -- `get_dist(usr,T) &lt;= usr.zanzorange` (:52), com `zanzorange = round(1.2*max(Ekiskill,
///     Etechnique)*Espeed)` (o efetor da Afterimage, `misc.dm:55`; ver <see cref="ZanzorangeG10"/>). Cresce
///     com o treino: 1 tile num corpo novo, ~11 com os E em 3, ~30 com os E em 5.
///   * CUSTO -- `kireq*get_dist(usr,T)` (:54 e :64), com `kireq = (6*BaseDrain)/(Ekiskill*(Espeed/2))` (:51).
///     Por TILE saltado, e barateia com a pericia de Ki e a velocidade.
///   * RECARGA -- nenhuma. O `DblClick` nao tem `sleep` nem prazo: quem trava a piscada seguinte e o Ki.
///
/// O `get_dist` e o do BYOND (<see cref="Jandirus.Core.Social.Fusao.DistanciaEmTilesDoDm"/>: tabuleiro, entre
/// as CELULAS dos centros) -- a diagonal de 3 tiles custa 3, e nao 4,2.
/// ================================================================================================================
///
/// ============================ O QUE DIVERGE, DECLARADO ============================
///   * LONGE DEMAIS ENCURTA, nao recusa. O DM ignora o clique alem do `zanzorange` (:52); aqui o destino
///     desce pelo segmento ate a borda do alcance (o quadrado de `get_dist`, e nao o circulo). Recusar
///     obrigaria o jogador a medir distancia com o mouse no meio de uma briga, num jogo sem grade.
///   * A PAREDE NO CAMINHO RECUSA. O DM confere so o tile de destino (`!T.density`, :52) -- o `Move()`
///     pra um turf distante e salto, nao passeio --, mas o jogador so da duplo clique no que VE, e a vista
///     do cliente do BYOND e cortada por parede opaca. O servidor daqui nao sabe o que o cliente ve e nao
///     tem opacidade; a parede no caminho (`MoveRules.PathOccupied`) e o que faz o papel da vista. Sem ela
///     o Zanzoken seria o jeito barato de entrar em qualquer casa fechada. A agua recusa pelo mesmo funil
///     (`MoveRules.Occupied`), como recusa o pe.
///   * O `telestopping`/`telestop()` (:55-57) e da Time Stop, que o port nao tem.
///   * O `zanzochange++` (:65) vira exp NA HORA -- ver <see cref="ExpDaPiscada"/>.
/// ==================================================================================
///
/// TUDO QUE IMPORTA E DECIDIDO AQUI. O cliente manda um PONTO e mais nada: se ele decidisse, a
/// tecnica seria teleporte livre pra qualquer cliente modificado -- que e a definicao de
/// atravessar parede de graca.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O jogador pediu pra piscar ate um ponto.
	///
	/// AS RECUSAS SAO MUDAS por escolha: elas acontecem no meio de uma luta, e encher o chat de
	/// "sem Ki" / "muito longe" a cada duplo clique tira a atencao de onde ela precisa estar. A
	/// unica que FALA e a de nao ter a skill -- essa e a que o jogador nao tem como deduzir. (No DM
	/// as recusas tambem sao mudas: e um `if` sem `else`.)
	/// </summary>
	private void Zanzoken(ServerPlayer pl, Vec2 destino)
	{
		// O FIO PSIQUICO DO HERAN (lote G11) usa o MESMO clique no chao: com o toggle ligado, o duplo
		// clique arma um fio de paralisia aos pes em vez de piscar. Ver `FioPsiquicoNoCliqueG11`.
		if (FioPsiquicoNoCliqueG11(pl, destino)) return;

		if (pl.Livro?.Sabe(PathDoZanzoken) != true)
		{
			Avisar(pl, "você não sabe se mover assim -- é o que a Afterimage Technique ensina.");
			return;
		}

		// ============================ `!usr.KO` -- E SO O KO (`click.dm:54`) ============================
		// A linha do DM nao le `dead`: o morto do Outro Mundo e um corpo de pe (o `Death()` escreve `move = 1`
		// e faz o `Un_KO`), anda, voa -- e pisca. Isto recusava `dead` cru, e o fantasma que o jogo inteiro
		// ja deixa andar (`PodeMexerOCorpo`) e voar (`AlternarVoo`) era o unico que nao piscava. O que
		// continua de fora e o CADAVER dos 15 s no chao dos vivos (`MortoDePe` falso): no DM ali ja nao ha
		// mob nenhum, so o `obj/mobCorpse` -- o mesmo corte do voo e do passo.
		// ==================================================================================================
		if (pl.Ficha.KO || (pl.Ficha.dead && !pl.MortoDePe)) return;

		// ============================ AS OUTRAS CINCO LETRAS DA LINHA DO DM -- `click.dm:54` ============================
		// `if(usr.move&&!usr.Apeshit&&!usr.KB&&!usr.beaming&&!usr.KO&&!usr.med&&!usr.train&&usr.Ki>=...)`. O `!usr.KO` e a
		// linha de cima e o `!usr.beaming` e o bloco de baixo; as outras cinco sao ESTAS, cada uma pelo estado que o port
		// guarda no lugar do bit do DM:
		//   * `usr.move` -- o `MoveZerado`, logo abaixo deste metodo, com a lista do que zera o bit no DM e do que o port
		//     trouxe de cada um. Sem esta letra a piscada saia de dentro de uma estreia de forma, da area de espera do
		//     torneio, de uma Final Explosion carregando -- e nas tecnicas que prendem por ANCORA (a carga da G3, a Death
		//     Ball, a Genkidama, a espera do torneio) o `Ancorar` do pulso seguinte puxava o corpo de volta: Ki cobrado,
		//     miragem deixada, e o corpo onde estava.
		//   * `!usr.Apeshit` -- o Oozaru, regular OU dourado: os dois passam pelo mesmo `obj/buff/Oozaru/Buff()`, que
		//     escreve `Apeshit = 1` (`Oozaru.dm:117`). Vale TAMBEM com as redeas na mao: a fera sem redeas nem chega
		//     aqui (o `ComandoDeCorpo` barra o pacote no despacho), mas o DM recusa pelo bit da FORMA, e nao pelo
		//     `ctrlParalysis`.
		//   * `!usr.KB` -- o arremesso (`TiquesDeVoo`, escrito pela porta unica `Arremessar`, que e o `Throw.dm:84` e o
		//     `/effect/knockback` deste port). DIVERGENCIA DECLARADA: a pergunta e o `DirigidoPeloServidor` inteiro, e
		//     nao so o arremesso -- entram tambem o ARRASTO do feixe (DU, `Projectiles.dm:585-590`) e o PUXAO da Potara
		//     (DU, `Potara_Fusion.dm:122-132`), que o Finale nao tem. Os tres sao "uma forca leva o corpo e escreve `Pos`
		//     a cada tique", e o arrasto mostra por que nao da pra deixa-lo de fora: quem piscasse pra fora do feixe
		//     continuaria sendo empurrado por ele A DISTANCIA -- o `ArrastarComOFeixe` soma o passo da cabeca a
		//     `alvo.Pos` sem medir onde o corpo esta.
		//   * `!usr.med` / `!usr.train` -- os MESMOS campos do DM (`Ficha.med`, `Ficha.train`), que o
		//     `case Protocol.C2S.Activity` escreve (teclas M e T). Dentro da DIMENSAO MENTAL o `med` do port continua
		//     ligado ate o primeiro passo (o DM o zera na entrada, `MindMeditate.dm:398`; o porque esta no cabecalho do
		//     `GameServer.Mente.cs`): quem acabou de mergulhar e ainda nao andou nao pisca -- e tambem nao atira, pela
		//     mesma leitura no `PodeAtirar`.
		//
		// MUDAS E DE GRACA, como as outras recusas daqui: vem antes do Ki. A `--kideponta` (familia 7) tem
		// uma linha por letra (o `move` tem duas: a carga da Final Explosion e a cena da estreia do SSJ), cada uma num
		// corpo proprio e posta no estado pelo caminho de producao, ao lado do controle.
		// ==========================================================================================================
		if (MoveZerado(pl) || pl.Oozaru != Jandirus.Core.Forms.FormaOozaru.Nao || pl.DirigidoPeloServidor
			|| pl.Ficha.med || pl.Ficha.train)
			return;

		// ============================ RAIO NA MAO NAO PISCA -- `!usr.beaming` (`click.dm:54`) ============================
		// O `turf/DblClick` do DM so deixa o corpo piscar com `usr.move && !usr.Apeshit && !usr.KB &&
		// !usr.beaming && !usr.KO && !usr.med && !usr.train` -- e o `beaming` e o raio SOLTO, sendo segurado
		// (`beams.dm:281`). Aqui a porta estava aberta: o corpo saltava seis tiles com o feixe na mao, a cauda
		// ia junto com a mao (`p.Cauda = BocaDeCano.De(dono.Pos, p.Rumo)`, no `TickDosProjeteis`) e a cabeca
		// seguia na linha velha -- um feixe TORTO, o mesmo motivo que ja tirou o salto do `Aproximar`.
		//
		// DIVERGENCIA DECLARADA: recusa o CANAL INTEIRO (`EnraizadoPorKi`: carregando OU segurando), e nao so
		// o `beaming`. No DM a CARGA (`charging = 1` com `canmove = 0`, `beams.dm:291-297`) NAO fecha esta
		// porta: o `DblClick` nao le `canmove` nem `charging`, e o `Move()` tambem nao (`Glidemove.dm:23-68`).
		// Aqui fecha, por uma diferenca real do porte:
		//   * no DM quem passa de `charging` a `beaming` e o JOGADOR, apertando o verb de novo
		//     (`beams.dm:279-284`); aqui a carga vira raio SOZINHA, no tique em que a `CargaRestante` zera
		//     (`TickDosCanaisDeKi`). Recusando so o raio solto, o mesmo duplo clique no fim da carga seria
		//     aceito ou recusado conforme o pacote chegasse antes ou depois daquele tique -- a latencia
		//     decidindo, com a MESMA pose na tela;
		//   * o combo que o DM permite ("carrega, pisca, solta") nao existe aqui do mesmo jeito: la a carga se
		//     SEGURA, engordando o `wavemult`, ate o jogador escolher soltar (`beams.dm:36-47`); aqui ela tem
		//     duracao fixa (`Projetil.SegundosDeCarga`) e dispara por conta propria;
		//   * e "raio na mao" ja quer dizer o canal inteiro no resto do port: a investida (`Aproximar`, pelo
		//     `PodeMexerOCorpo`), o escudo (`EscudoDeEnergiaG6`) e o Fio Psiquico -- que mora NESTE mesmo
		//     clique, logo acima (`FioPsiquicoNoCliqueG11`) -- recusam os dois estagios.
		//
		// E SO O CANAL, e nao o funil inteiro do `PodeMexerOCorpo` como no arranque: o `DblClick` do DM nao le
		// `canmove`, nem a paralisia (`paralyzed` so mora no laco de andar, `movement handler.dm:89`), nem o
		// agarrao (`grabParalysis`). Trancar a piscada por eles seria uma regra que o original nao escreveu
		// nesta linha, e ela nao cabe num conserto de raio. (As outras letras da linha tem o par delas no bloco
		// de cima -- e do funil de vetor so veio o que o DM escreve na linha: o `move`, e o `KB` com a divergencia
		// declarada la.)
		//
		// MUDA E DE GRACA, como as outras recusas daqui: vem ANTES do Ki, e quem esta com o raio
		// na mao ve a propria pose. A `--kideponta` (familia 7, ao lado da investida) mede as duas fases e o
		// controle.
		// ==========================================================================================================
		if (EnraizadoPorKi(pl.Id)) return;

		// ============================ A PERICIA E O PRECO POR TILE -- `click.dm:50-51` ============================
		// `if((usr.Ekiskill*(usr.Espeed/2)) && usr.haszanzo)` e o portao de divisao por zero da linha de baixo:
		// `kireq = (6*usr.BaseDrain)/(usr.Ekiskill*(usr.Espeed/2))`. O `BaseDrain` e o de toda tecnica (cresce com a
		// raiz do Ki maximo); a pericia de Ki e a velocidade barateiam.
		// =========================================================================================================
		Fighter f = pl.Ficha;
		double pericia = f.Ekiskill * (f.Espeed / 2);
		if (pericia <= 0) return;
		double kireq = 6 * f.BaseDrain() / pericia;

		// ============================ O ALCANCE E O `zanzorange` -- `click.dm:52` ============================
		// LONGE DEMAIS ENCURTA, nao recusa (declarado no cabecalho): o destino desce pelo segmento ate o maior
		// eixo caber no alcance. E o QUADRADO do `get_dist`, e nao o circulo -- a diagonal de N tiles e N tiles.
		// ======================================================================================================
		int alcance = ZanzorangeG10(f);
		Vec2 de = pl.Pos;
		Vec2 d = destino - de;
		float maiorEixo = MathF.Max(MathF.Abs(d.X), MathF.Abs(d.Y));
		float teto = alcance * ZoneCollision.TileSize;
		if (maiorEixo > teto) destino = de + d * (teto / maiorEixo);

		// `for(var/turf/A in view(0,usr)) if(A==src) return` (:53): o duplo clique no PROPRIO tile nao e piscada.
		int salto = Jandirus.Core.Social.Fusao.DistanciaEmTilesDoDm(de, destino, ZoneCollision.TileSize);
		if (salto == 0) return;

		// `usr.Ki >= (kireq*get_dist(usr,T))` (:54).
		double custo = kireq * salto;
		if (f.Ki < custo) return;

		// PAREDE MANDA MAIS QUE A TECNICA -- a mesma regra da investida do soco, e a divergencia declarada
		// no cabecalho (ela faz o papel da vista do cliente, que o servidor nao tem).
		ZoneCollision? mapa = _catalogo?.Get(pl.Zone)?.Mapa;
		if (mapa != null && (MoveRules.Occupied(mapa, destino) || MoveRules.PathOccupied(mapa, de, destino)))
			return;

		// `usr.Ki -= kireq*hopdist; if(usr.Ki<0) usr.Ki = 0` (:64, :66). O piso nao morde quem passou no
		// portao de cima, e fica pelo mesmo motivo do DM: a conta e a mesma, mas escrita duas vezes.
		f.Ki = Math.Max(0, f.Ki - custo);

		// ============================ PISCA SEM TIRAR OS OLHOS DE ONDE OLHAVA -- `formerdir` (`click.dm:61-63`) ============================
		// `var/formerdir=usr.dir; usr.Move(src); usr.dir=formerdir`: o `Move()` do BYOND vira o corpo pro rumo do passo, e o
		// DM desfaz isso DE PROPOSITO na linha seguinte -- quem pisca de lado continua encarando o inimigo. Aqui havia um
		// `pl.Facing = MoveRules.FacingFrom(d, pl.Facing)` virando o corpo pro destino, sem declaracao nenhuma, e ele nem
		// se sustentava: o cliente nao vira o proprio boneco na piscada (a `Correction` so traz posicao, e o `_facing` do
		// `LocalPlayer` so muda andando, socando o marcado ou quando o servidor crava a pose), e o proximo `InputState`
		// (um a cada `SendInterval`) reescreve `pl.Facing` com o olhar de ANTES (`pl.Facing = facing`, no fim do `Input`).
		// Quem assistia via o corpo virar pro destino num snapshot e desvirar no seguinte -- as duas pontas contando
		// historias diferentes da mesma piscada. Sem a linha as tres concordam: o DM, a tela de quem piscou e a de quem
		// assiste. (O `Facing` continua sendo de quem chama o `CravarPosicao` -- ver o cabecalho dele.)
		// A `--kideponta` (familia 7) cobra: o corpo que encarava o alvo pisca pro norte e chega encarando o alvo.
		// ====================================================================================================================
		CravarPosicao(pl, destino);

		AnunciarZanzo(pl, de);
		ExpDaPiscada(pl, alcance);
	}

	/// <summary>
	/// ============================ O `zanzochange++` -- A PISCADA TREINA A AFTERIMAGE ============================
	/// `usr.zanzochange++` (`click.dm:65`) e o efetor da skill (`misc.dm:56-59`, a 5 Hz):
	///
	///     if(level &lt; 4)
	///         if(savant.zanzorange &gt;= 3 &amp;&amp; savant.zanzochange)
	///             exp += savant.zanzochange   //"one zanzo = one exp"
	///             if(prob(20)) savant.zanzochange = 0
	///
	/// Ate aqui a piscada nao rendia nada: o extrator entregou esse bloco (`niveis.json`, com a condicao
	/// `savant.zanzorange>=3&amp;&amp;savant.zanzochange`) e o carregador o joga fora como condicao desconhecida --
	/// ninguem escrevia o contador. E o UNICO bloco de exp da Afterimage no `niveis.json`: sem ele, jogar nao dava
	/// exp nenhuma a ela, e os degraus dos niveis 2, 3 e 4 (`Zanzoken_Combo`, `Zanzoken_Dodge`,
	/// `Zanzoken_Afterimage`) nao se alcancavam piscando.
	///
	/// A CONTA: o contador so zera no `prob(20)` e, ate zerar, rende ELE INTEIRO a cada tique. Cada piscada
	/// fica, entao, um numero geometrico de tiques no contador -- em media 1/0,2 = 5 -- e rende 1 por tique:
	/// **5 de exp por piscada**, na media (o comentario do autor diz "one zanzo = one exp", e a conta dele da
	/// cinco). DIVERGENCIA DECLARADA: aqui os 5 saem NO EVENTO, e nao sorteados tique a tique -- a mesma decisao
	/// do <see cref="Jandirus.Core.Skills.NiveisDeSkill.CreditarPorContador"/> (sem contador vivo pra zerar no
	/// relog e sem estado pra gravar). O total esperado e o mesmo; a variancia some. E o portao `zanzorange >= 3`
	/// e lido no instante da piscada, e nao no tique seguinte.
	/// =========================================================================================================
	/// </summary>
	private void ExpDaPiscada(ServerPlayer pl, int alcance)
	{
		if (alcance < 3 || pl.Niveis.Nivel(PathDoZanzoken) >= 4) return;
		pl.Niveis.Creditar(PathDoZanzoken, ExpPorPiscada);
	}

	/// <summary>A esperanca do `zanzochange` por piscada: `1 / prob(20)`. Ver <see cref="ExpDaPiscada"/>.</summary>
	private const double ExpPorPiscada = 1 / 0.2;

	/// <summary>
	/// O `move = 0` DO DM, COMO ESTE PORT O GUARDA -- a primeira letra do `turf/DblClick` (`click.dm:54`).
	///
	/// ============================ NO DM E UM BIT; AQUI SAO OS ESTADOS DE CADA SISTEMA ============================
	/// O DM escreve `move = 0` em quase trinta lugares (o bit nasce 1, `mobvars.dm:4`, e fora daqui so o laco de andar o
	/// le, `movement handler.dm:131`). O port nao herdou o bit: cada sistema que prende o corpo guardou o estado DELE --
	/// um prazo, um dicionario, uma ancora. Esta funcao e a lista dos que vieram:
	///   * as CINEMATICAS -- `SSJCinematic.dm:4`, `SSJ2Cinematic.dm:4`, `perfecttrans.dm:4`, `lssjbuff.dm:219`,
	///     `IcerTransform.dm:199`, `UltraInstinct.dm:334`, `UltraEgo.dm:423`, `DNALabs.dm:554` e as irmas: o
	///     `CenaSegundos` que o `MarcarCena` e a evolucao do Bio escrevem (<see cref="EmCena"/>);
	///   * a AREA DE ESPERA do torneio -- `apply_hold`, `Tournament.dm:331` (<see cref="PresoNoTorneio"/>);
	///   * a DEATH BALL, da carga ao fim da guia -- `DeathBall.dm:33` ate `:111`;
	///   * a GENKIDAMA, ate 3 s depois do disparo -- `SpiritBomb.dm:43` ate `:176`;
	///   * a carga da FINAL EXPLOSION e a da AUTODESTRUICAO -- `misc.dm:355` e `:237` (as duas moram no `_cargaG3`);
	///   * a concentracao do INSTANT TRANSMISSION -- `yardrat.dm:134`. Sem esta, a piscada cancelava a concentracao
	///     pelo "voce se mexeu!" do `TickDaTransmissaoG11`; no DM ela nem sai, e a concentracao continua.
	///
	/// DIVERGENCIA DECLARADA: o <see cref="EmCena"/> conta tambem as cenas que SO o port tem -- a da Destroyer (o DM
	/// nao tem cinematica pra ela, `UltraEgo.dm:530`), a do Oozaru (o `Buff()` do DM ate escreve `move = 1`,
	/// `Oozaru.dm:134`) e a luz da fusao (`PrenderNaCenaDeFusao`). O port ja prende o corpo nelas pelo funil de vetor
	/// (<see cref="PodeMexerOCorpo"/>), e a piscada segue o mesmo corte pelo mesmo motivo: sem ele a cena continuaria
	/// tocando num ponto com o corpo em outro. (No Oozaru a letra `!usr.Apeshit` ja recusa de qualquer jeito.)
	///
	/// O NOCAUTE tambem zera o bit (`KO.dm:120`), mas tem letra propria (`!usr.KO`) e linha propria no `Zanzoken`. A
	/// MIND MEDITATE zera o do CORPO deixado pra tras (`MindMeditate.dm:132`), e aqui quem medita continua com o
	/// `Ficha.med` ligado -- cai na letra `!usr.med`.
	///
	/// O QUE NAO ESTA E PORQUE NAO VEIO: a Guided Ball (`GuidedBall.dm:34`) e o Kienzan (`discs.dm:48`) prendem o corpo
	/// no DM e aqui disparam e soltam -- nao ha estado nenhum pra ler; o `SkyNPCs.dm:13` e de NPC, que nao clica. Quem
	/// portar a trava de uma delas acrescenta a linha AQUI: e o unico lugar que a piscada le.
	///
	/// NAO E O <see cref="PodeMexerOCorpo"/>: aquele e o funil de VETOR e mistura o `move` com o `canmove` (a
	/// paralisia, os coletores do androide), o `grabParalysis` e o `gravParalysis` -- e o `DblClick` so le o `move`.
	/// Pelo mesmo motivo o <see cref="PresoPeloG12"/> inteiro nao serve: a rajada dele e `canmove = 0`
	/// (`blasts.dm:270`), e so a Death Ball e a Genkidama sao `move = 0`.
	/// ==========================================================================================================
	/// </summary>
	private bool MoveZerado(ServerPlayer pl) =>
		EmCena(pl) || PresoNoTorneio(pl.Id)
		|| _deathBallG12.ContainsKey(pl.Id) || _genkidamaG12.ContainsKey(pl.Id)
		|| _cargaG3.ContainsKey(pl.Id) || _transmissaoG11.ContainsKey(pl.Id);

	/// <summary>
	/// CRAVA O CORPO NUM PONTO -- o `loc = locate(...)` de toda tecnica que desloca sem o jogador
	/// andar: a piscada, a investida, o passo do Light Buster, a ancora de uma carga, o puxao da
	/// volta no tempo.
	///
	/// ============================ UM CAMINHO PRA "O SERVIDOR MOVEU VOCE" ============================
	/// Sao quatro linhas e um pacote, e eram copiadas em nove lugares com pequenas diferencas: uma
	/// copia sem zerar o `OrcamentoPx`, outra sem o carimbo de sequencia, outra que montava o pacote a
	/// mao (a chance de mandar so a posicao e o cliente ler a coordenada como sequencia). Aqui vale a
	/// versao inteira, sempre:
	///   * `LastInputMs`: o proximo pacote do cliente parte da posicao NOVA e nao conta como passo;
	///   * `CorrecaoEsperadaAte`: as correcoes desta janela sao a tecnica funcionando, e nao cliente
	///     desonesto -- nao entram no medidor (ver `GameServer.Input`);
	///   * `SeqDoTeleporte`: input montado ANTES deste instante nao opina sobre onde o corpo esta;
	///   * `OrcamentoPx = 0`: teleporte nao carrega credito de passo acumulado andando;
	///   * o pacote sai pelo <see cref="MandarCorrecaoG3"/>, o unico jeito certo de monta-lo.
	/// O `Facing` fica com quem chama: cada tecnica chega olhando pra um lado diferente.
	/// ================================================================================================
	/// </summary>
	/// <param name="janelaMs">Quanto tempo as correcoes seguintes contam como esperadas. Meio segundo
	/// basta pra quem se moveu por gesto proprio; quem foi puxado por OUTRA tecnica pede mais.</param>
	private void CravarPosicao(ServerPlayer pl, Vec2 destino, long janelaMs = 500)
	{
		long agora = NowMs();
		pl.Pos = destino;
		pl.LastInputMs = agora;
		pl.CorrecaoEsperadaAte = agora + janelaMs;
		pl.SeqDoTeleporte = pl.SeqInput;
		pl.OrcamentoPx = 0;
		MandarCorrecaoG3(pl);
	}

	/// <summary>
	/// A ANCORA: se o corpo saiu de onde a tecnica o prende (uma carga, um canal), volta pra la.
	/// Nao e cliente desonesto -- e a trava. Devolve se puxou; a folga de 4 px existe pra nao brigar
	/// com o arredondamento do cliente a cada tique.
	/// </summary>
	private bool Ancorar(ServerPlayer pl, Vec2 ancora, long janelaMs = 400)
	{
		if (Vec2.Distance(pl.Pos, ancora) <= 4) return false;
		CravarPosicao(pl, ancora, janelaMs);
		return true;
	}

	/// <summary>
	/// O PRIMEIRO PONTO LIVRE de uma lista de candidatos -- o `locate()` + `Enter()` de todo
	/// teleporte curto do DM (atras do alvo, ao lado dele, num vizinho diagonal), que aqui pergunta
	/// ao passo (<see cref="MoveRules.Occupied"/>: parede E agua). Nulo quando nenhum serve; sem
	/// mapa carregado o primeiro vale, porque nao ha parede pra conferir. Quatro tecnicas tinham
	/// cada uma a sua copia desta pergunta.
	/// </summary>
	private Vec2? PontoLivre(ZoneKey zona, params Vec2[] candidatos)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		if (mapa == null) return candidatos.Length > 0 ? candidatos[0] : null;
		foreach (Vec2 c in candidatos)
			if (!MoveRules.Occupied(mapa, c)) return c;
		return null;
	}

	/// <summary>
	/// Conta a piscada pra ZONA INTEIRA, com o ponto de PARTIDA.
	///
	/// A origem viaja no pacote porque e onde a miragem nasce, e quando ele chega o corpo ja esta
	/// no destino -- quem recebesse so o id desenharia o vulto em cima do jogador, que e o oposto
	/// de "ficou pra tras". Vale pra quem piscou tambem: e o mesmo pacote, e assim as duas pontas
	/// desenham a mesma coisa no mesmo lugar.
	///
	/// Canal NAO confiavel: perder um vulto custa meio segundo de efeito, e a piscada e uma coisa
	/// de combate, onde o trafego ja e o que mais aperta.
	///
	/// ============================ ESTE PACOTE VIROU "O CORPO SALTOU", NAO "O CORPO PISCOU" ============================
	/// O dono: *"npcs quando usam DASH n ficam com o EFEITO DE BLUR igual os jogadores"*.
	///
	/// O borrao (`Client/RastroDeCorrida.cs`) nunca foi um efeito de dash: e o rastro de CORRER, e o
	/// jogador so o via no arranque por COINCIDENCIA -- a mesma tecla SHIFT que faz o golpe ser Pesado
	/// (`LocalPlayer:957`) tambem liga o `_correndo` (`LocalPlayer:635`), que liga o rastro. Quem nao
	/// segura SHIFT (o soco leve, que TAMBEM investe) nunca borrou; e o NPC nunca borrou porque o
	/// cerebro so escreve `Comando.Correndo` na FUGA (`Cerebro:1831`) -- medido: 0 em 1000 comandos de
	/// perseguicao com o temperamento de fabrica.
	///
	/// Pendurar o borrao no INPUT foi o erro, e consertar isso no cliente exigiria um `if` de tipo de
	/// corpo em cada ponta. Entao ele desceu pro FUNIL DO MOVIMENTO: **quem sabe que houve deslocamento
	/// e o `Aproximar`, e ele ja anunciava por aqui.** Agora o anuncio sai de TODA investida que moveu
	/// o corpo -- jogador, NPC e corpo possuido pelo mesmo caminho, sem `if` nenhum -- e o
	/// <paramref name="vulto"/> diz se, POR CIMA do borrao, tambem nasce a miragem.
	///
	/// CUSTO: **um byte** (o `vulto`). O pacote vai de 13 pra 14 bytes -- opcode(1) + id(4) + Vec2(8).
	/// O que cresce de verdade e a FREQUENCIA: antes so quem sabia a Afterimage anunciava, agora
	/// qualquer corpo que arranque anuncia. Teto por corpo: 2 pacotes/s (`RecargaDashMs = 500`).
	/// ==============================================================================================================
	/// </summary>
	/// <param name="vulto">
	/// Nasce a IMAGEM REMANESCENTE junto? So quem sabe a Afterimage deixa vulto -- o borrao do
	/// deslocamento nao pede skill nenhuma, porque ele nao e tecnica: e o corpo tendo passado por ali.
	/// </param>
	private void AnunciarZanzo(ServerPlayer pl, Vec2 de, bool vulto = true)
	{
		// QUEM ESTA INVISIVEL NAO DEIXA VULTO.
		//
		// A miragem e uma FOTO do corpo, opaca, parada num ponto: um jogador escondido que piscasse
		// (ou investisse) entregava a propria posicao com ela. O sigilo do corpo nao pode depender
		// de o jogador lembrar de nao usar a tecnica.
		//
		// O corte e aqui e nao no cliente: mandar o pacote e depois pedir pra ele nao desenhar seria
		// entregar a posicao pra qualquer cliente modificado, que e a mesma regra do BP escondido.
		if (EstaOculto(pl.Id)) return;

		var w = Protocol.Begin(Protocol.S2C.Zanzo);
		w.Put(pl.Id);
		w.PutVec(de);
		w.Put(vulto);

		// ============================ O CARIMBO QUE A BANCADA LE (`--borraoteste`) ============================
		// Duas linhas, e elas medem o que REALMENTE saiu: o `w.Length` e o tamanho do pacote montado,
		// nao a soma dos `sizeof` que eu acharia que ele tem.
		//
		// SAO NECESSARIAS PORQUE O DESTINO DAQUI E UM `Peer`, e um NPC nao tem nenhum. Sem carimbo, a
		// unica coisa que a bancada poderia conferir seria uma COPIA da condicao do `Atacar` -- ou
		// seja, ela ficaria verde afirmando o que ela mesma escreveu. E o cego que este projeto ja
		// pagou ("uniform escrito != pixel desenhado"): aqui o `w` e o pixel.
		// ====================================================================================================
		_saltosAnunciados++;
		_ultimoSalto = (pl.Id, de, vulto, w.Length);

		// ============================ E O SALTO DE CADA CORPO, PORQUE UM QUADRO DEPOIS ELE JA NAO E EXATO ============================
		// A bancada de FOTO (`--diagborrao`) precisa do tamanho do salto do JOGADOR, e ela nao tem como
		// ler isso do estado: o corpo de quem tem cliente e RECONCILIADO nos quadros seguintes -- o
		// cliente ainda esta mandando input da posicao velha quando a `Correction` sai, e o servidor
		// acomoda alguns pixels disso dentro do `OrcamentoPx`.
		//
		// Medido: um salto de 268 px lido dois quadros depois dava 257. O numero nao estava errado, a
		// LEITURA estava tarde -- e a bancada acusava "o NPC salta mais que o jogador", que e o relato do
		// dono nascendo de um artefato de medicao. O corpo sem cliente nao sofre disso, e era so por isso
		// que os dois discordavam.
		//
		// Aqui, `pl.Pos` ainda e o destino que o `Aproximar` acabou de escrever. Uma escrita de duas
		// posicoes por anuncio, com teto de 2 anuncios/s por corpo.
		// ==========================================================================================================================
		_saltoDeCadaCorpo[pl.Id] = (de, pl.Pos);

		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelState, DeliveryMethod.Unreliable);
	}

	/// <summary>Quantos anuncios de salto sairam. Zerado e lido pela bancada `--borraoteste`.</summary>
	private int _saltosAnunciados;

	/// <summary>O ULTIMO anuncio de salto, como ele foi montado. Ver o carimbo em `AnunciarZanzo`.</summary>
	private (int Quem, Vec2 De, bool Vulto, int Bytes)? _ultimoSalto;

	/// <summary>
	/// DE ONDE ATE ONDE cada corpo saltou da ultima vez -- lido pela bancada `--diagborrao`. Ver o
	/// carimbo em <see cref="AnunciarZanzo"/>: um quadro depois esta distancia ja nao e exata pra quem
	/// tem cliente do outro lado.
	/// </summary>
	private readonly Dictionary<int, (Vec2 De, Vec2 Para)> _saltoDeCadaCorpo = [];
}
