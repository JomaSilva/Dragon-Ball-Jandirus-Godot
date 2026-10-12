using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// 16) A PAREDE (`--projetilteste`) -- quem tem quatro vezes o poder do tiro nao o sente (`KiContraCorpo`, dono
/// 2026-10-12). O arquivo guarda o nome da regra que esta familia media ate aquele dia: o corte dos fracos do DM,
/// `if(...ResistCheck(dmg)*BPModulus(...) &lt; 10)` no `Bump` (`objects.dm:355-357`).
///
/// ============================ O QUE MUDOU DE DONO, E O QUE FICOU ============================
/// O RAMO e o mesmo -- o impacto que NAO E GOLPE (`GameServer.EstourarSemFerir`): a bola estoura no corpo sem
/// ferir, o raio para na frente dele e bate sem ferir numa cadencia propria, o corpo anda raio adentro, e nada e
/// sorteado, empurrado, atordoado ou derrubado. O que cada parte faz foi MEDIDO no BYOND 516 em 2026-10-08 (os
/// numeros estao no `DanoDeKi.CorteDoFraco` e no `EstourarSemFerir`) e continua valendo.
///
/// QUEM CAI NELE mudou. No DM era o tiro de dano final abaixo de 10, fosse de quem fosse -- e entre dois corpos de
/// poder igual quase todo raio caia ali: o vilao de mesmo poder atirava e nada acontecia (o playtest do dono).
/// Hoje e o corpo que e PAREDE pro tiro: `poder do corpo / poder do tiro &gt;= KiContraCorpo.VezesDaParede`. O
/// golpe mais pesado nao fere a parede, e o mais fraco fere quem nao e.
/// ==========================================================================================
///
/// ============================ O QUE SE MEDE ============================
/// O que UM impacto de ki faz a UM corpo-parede, lido do estado de producao (a vida, a pericia de defesa de ki, o
/// Ki, a posicao e a paralisia de quem leva; o `Fim` e a cabeca do tiro) e do FIO (`EscutaDeGolpes`: o impacto que
/// nao e golpe nao manda relato nenhum). Cada cena tem ao lado o CONTROLE: o MESMO tiro num corpo que nao e parede
/// (o igual, ou o que esta logo abaixo do degrau) -- a regua que diz "nao feriu" tem que saber dizer "feriu".
///
/// O DANO E CRAVADO NO TIRO (<see cref="CravarDanoFinal"/>) em 12 -- acima do antigo corte de 10, de proposito: o
/// que esta familia afirma e que NAO E MAIS O DANO que decide. A outra metade fecha a conta: o tiro de 5 de um
/// igual, que o corte antigo jogava fora, hoje e golpe.
///
/// O JOGO DE ANTES (o corte por dano, sem parede) fica no fim, como o defeito injetado
/// (`KiContraCorpo.RegraDeAntesDeTeste`); a cabeca que nao recua (`Feixe.CabecaNaoRecuaDeTeste`) tambem.
/// ======================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>O "tiro de 5": o golpe FRACO -- abaixo do antigo corte por dano (10). Entre iguais, hoje ele fere.</summary>
	private const double DanoFracoDoCorte = 5;

	/// <summary>
	/// O GOLPE DE BANCADA: 12 por impacto, pouco acima do antigo corte por dano. E o dano das cenas que medem o que um
	/// tiro faz a um corpo sem derrubar o boneco no meio da medida -- e, por passar de 10, e golpe tambem no jogo de
	/// antes (os contra-exemplos com o `KiContraCorpo.RegraDeAntesDeTeste` precisam disso).
	/// </summary>
	private const double DanoQueFereDoCorte = 12;

	/// <summary>
	/// Os dois lados do degrau da parede, em vezes o poder do tiro: logo ABAIXO (apanha e fica) e logo ACIMA (parede)
	/// de `KiContraCorpo.VezesDaParede`.
	/// </summary>
	private const double VezesLogoAbaixoDaParede = 3.9, VezesLogoAcimaDaParede = 4.1;

	private void AParedeNaoSenteOTiro()
	{
		GD.Print("[projetil] -- 16) A PAREDE: quem tem quatro vezes o poder do tiro nao o sente -- e o impacto que nao e golpe nao e sorteado, nao empurra e nao derruba nada");
		const int T = ZoneCollision.TileSize;
		Vec2 chao = CorredorLivre(24);
		Vec2 raia = CorredorSeco(30);

		// ---------------------------------------------------------------- (0) a pergunta, sozinha
		AfirmarPj("o degrau e 4 vezes o poder do tiro, e e INCLUSIVO: com 3,999999 o corpo apanha, com 4 cravado ja e parede (`KiContraCorpo.EhParede`)",
				  KiContraCorpo.VezesDaParede == 4 && !KiContraCorpo.EhParede(3.999999) && KiContraCorpo.EhParede(4)
				  && KiContraCorpo.EhParede(KiContraCorpo.Razao(4, 1)) && !KiContraCorpo.EhParede(KiContraCorpo.Razao(1, 1)),
				  $"degrau {KiContraCorpo.VezesDaParede}");

		// ---------------------------------------------------------------- (a) a bola na parede
		ImpactoDoCorte b = BolaDeDanoCravado(chao, BpDaParedeDaEscada, DanoQueFereDoCorte);
		GD.Print($"[projetil]      (medido) BOLA de {b.Dano:0.00} em quem tem {b.Vezes:0.##}x o poder dela: {b.Relatos.Count} relato(s), vida {b.Vida:+0.###;-0.###;0}, pericia +{b.Pericia:0.0}, "
				 + $"fim {b.Tiro.Fim} a {(b.Tiro.Pos - b.Alvo.Pos).Length:0.0} px do corpo");
		AfirmarPj("PREPARO: o corpo e PAREDE pro tiro (cinco vezes o poder dele) e a bola sai com o dano final cravado em 12 -- um golpe, pelo corte antigo",
				  KiContraCorpo.EhParede(b.Vezes) && Math.Abs(b.Dano - DanoQueFereDoCorte) < 1e-6 && !DanoDeKi.FracoDemais(b.Dano),
				  $"{b.Vezes:0.###}x o poder do tiro, dano final {b.Dano:0.######}");
		AfirmarPj("BOLA NA PAREDE: nao fere -- a vida de quem leva nao muda, por mais que o golpe valha",
				  Math.Abs(b.Vida) < 1e-9, $"vida {b.Vida:+0.###;-0.###;0}");
		AfirmarPj("BOLA NA PAREDE: nao e golpe -- nenhum relato sai pro fio (sem clarao, sem faisca, sem ferida)",
				  b.Relatos.Count == 0, $"{b.Relatos.Count} relato(s)");
		AfirmarPj("BOLA NA PAREDE: ESTOURA no corpo e acaba ali (`explodeme` -> `explode()`, `objects.dm:565-566`)",
				  !b.Tiro.Vivo && b.Tiro.Fim == FimDeProjetil.Acertou && (b.Tiro.Pos - b.Alvo.Pos).Length <= Projetil.RaioDeImpacto + 0.5f,
				  $"vivo {b.Tiro.Vivo}, fim {b.Tiro.Fim} a {(b.Tiro.Pos - b.Alvo.Pos).Length:0.0} px");
		AfirmarPj("BOLA NA PAREDE: o que o `Bump` faz a TODO tiro que encosta continua valendo -- o credito, a tag de combate e o treino de UM encontro (0,1)",
				  b.Alvo.UltimoAgressor == b.Atirador.Id && b.Alvo.Combate!.EmCombate > 0 && Math.Abs(b.Pericia - 0.1) < 1e-9,
				  $"agressor {b.Alvo.UltimoAgressor} (atirador {b.Atirador.Id}), tag {b.Alvo.Combate!.EmCombate:0.#} s, pericia +{b.Pericia:0.0}");
		AfirmarPj("BOLA NA PAREDE: nao empurra nem atordoa -- o corpo fica onde estava",
				  b.Andou < 0.01f && b.Alvo.TiquesDeVoo <= 0 && b.Alvo.Combate!.Stun <= 0,
				  $"andou {b.Andou:0.00} px, voo {b.Alvo.TiquesDeVoo}, stun {b.Alvo.Combate!.Stun:0.##}");

		ImpactoDoCorte c = BolaDeDanoCravado(chao, BpDoIgualDaEscada, DanoQueFereDoCorte);
		AfirmarPj("(controle) a MESMA bola de 12 num IGUAL fere e sai como golpe -- a regua enxerga dano",
				  c.Vida < -0.1 && c.Relatos.Count == 1 && (Desfecho)c.Relatos[0].Desfecho == Desfecho.Acertou && c.Tiro.Fim == FimDeProjetil.Acertou,
				  $"vida {c.Vida:+0.###;-0.###;0}, {c.Relatos.Count} relato(s), fim {c.Tiro.Fim}");

		// ---------------------------------------------------------------- (b) o degrau, no jogo
		ImpactoDoCorte abaixo = BolaDeDanoCravado(chao, BpDoFeixeDaEscada * VezesLogoAbaixoDaParede, DanoQueFereDoCorte);
		ImpactoDoCorte acima = BolaDeDanoCravado(chao, BpDoFeixeDaEscada * VezesLogoAcimaDaParede, DanoQueFereDoCorte);
		AfirmarPj("O DEGRAU NO JOGO: logo abaixo de quatro vezes o poder do tiro o corpo APANHA; logo acima, e parede -- o mesmo golpe de 12 nos dois",
				  abaixo.Vezes < KiContraCorpo.VezesDaParede && abaixo.Vida < -0.1 && abaixo.Relatos.Count == 1
				  && acima.Vezes >= KiContraCorpo.VezesDaParede && Math.Abs(acima.Vida) < 1e-9 && acima.Relatos.Count == 0,
				  $"{abaixo.Vezes:0.###}x: vida {abaixo.Vida:+0.###;-0.###;0}, {abaixo.Relatos.Count} relato(s) | {acima.Vezes:0.###}x: vida {acima.Vida:+0.###;-0.###;0}, {acima.Relatos.Count} relato(s)");

		// ---------------------------------------------------------------- (c) a parede vem ANTES do sorteio
		// O DADO LIGADO E O RASPAO CERTO (a chance cravada acima de 200%, como na familia 15): a parede nem e sorteada.
		ImpactoDoCorte s = BolaDeDanoCravado(chao, BpDaParedeDaEscada, 0, comDado: true, chance: ChanceDoRaspaoCerto);
		AfirmarPj("PREPARO: contra a parede a chance de deflexao da conta passa de 200% -- se ela fosse sorteada, o raspao seria CERTO",
				  KiContraCorpo.EhParede(s.Vezes) && s.Chance / 2 >= 100, $"{s.Vezes:0.###}x o poder do tiro, chance {s.Chance:0.#}%");
		AfirmarPj("A PAREDE NAO E SORTEADA: nem raspao nem deflexao -- so o treino de levar (0,1, e nao 0,5), e o Ki de quem leva intacto",
				  s.Relatos.Count == 0 && s.Tiro.Fim == FimDeProjetil.Acertou && Math.Abs(s.Pericia - 0.1) < 1e-9
				  && Math.Abs(s.Ki) < 1e-9 && Math.Abs(s.Vida) < 1e-9,
				  $"{s.Relatos.Count} relato(s), fim {s.Tiro.Fim}, pericia +{s.Pericia:0.0}, Ki {s.Ki:+0.###;-0.###;0}, vida {s.Vida:+0.###;-0.###;0}");
		ImpactoDoCorte sorteado = BolaDeDanoCravado(chao, BpDoFeixeDaEscada * VezesLogoAbaixoDaParede, 0, comDado: true, chance: ChanceDoRaspaoCerto);
		AfirmarPj("(controle) o MESMO dado em quem esta logo ABAIXO do degrau: e sorteado e RASPA (um relato de esquiva, 0,5 de treino, a bola se apaga)",
				  sorteado.Chance / 2 >= 100 && sorteado.Relatos.Count == 1 && (Desfecho)sorteado.Relatos[0].Desfecho == Desfecho.Esquivou
				  && Math.Abs(sorteado.Pericia - 0.5) < 1e-9 && sorteado.Tiro.Fim == FimDeProjetil.Apagou,
				  $"{sorteado.Vezes:0.###}x, chance {sorteado.Chance:0.#}%, {sorteado.Relatos.Count} relato(s), pericia +{sorteado.Pericia:0.0}, fim {sorteado.Tiro.Fim}");

		// ---------------------------------------------------------------- (d) a paralisia vem antes da parede
		ImpactoDoCorte par = BolaDeDanoCravado(chao, BpDaParedeDaEscada, DanoQueFereDoCorte, receita: r => r.Paralisia = true);
		bool trancou = ParalisiaAtiva(par.Alvo.Id);
		EsquecerParalisia(par.Alvo.Id);
		AfirmarPj("A PARALISIA VEM ANTES (`objects.dm:347-354`, a ordem do `Bump`): o tiro que a carrega tranca as pernas da parede, e nao a fere",
				  trancou && Math.Abs(par.Vida) < 1e-9 && par.Relatos.Count == 0,
				  $"paralisado {trancou}, vida {par.Vida:+0.###;-0.###;0}, {par.Relatos.Count} relato(s)");

		// ---------------------------------------------------------------- (e) a perfurante
		ImpactoDoCorte furo = BolaDeDanoCravado(chao, BpDaParedeDaEscada, DanoQueFereDoCorte, receita: r => r.Piercer = true);
		AfirmarPj("A PERFURANTE TAMBEM ACABA NA PAREDE: o `density = 0` que a deixa passar mora dentro do ramo do dano (`objects.dm:479-481`)",
				  !furo.Tiro.Vivo && furo.Tiro.Fim == FimDeProjetil.Acertou
				  && (furo.Tiro.Pos - furo.Alvo.Pos).Length <= Projetil.RaioDeImpacto + 0.5f && Math.Abs(furo.Vida) < 1e-9,
				  $"fim {furo.Tiro.Fim} a {(furo.Tiro.Pos - furo.Alvo.Pos).Length:0.0} px, vida {furo.Vida:+0.###;-0.###;0}");

		// ---------------------------------------------------------------- (f) a bola parada
		(Projetil Mina, double Vida, double Pericia, int Relatos) mina = MinaDeDanoCravado(chao, BpDaParedeDaEscada, DanoQueFereDoCorte);
		AfirmarPj("A MINA: a parede que pisa nela a estoura sem se ferir, num encontro so (`mobBump.dm:57-58` chama o `Bump` dela, e o `explode()` a tira do mundo)",
				  !mina.Mina.Vivo && mina.Mina.Fim == FimDeProjetil.Acertou && Math.Abs(mina.Vida) < 1e-9
				  && Math.Abs(mina.Pericia - 0.1) < 1e-9 && mina.Relatos == 0,
				  $"viva {mina.Mina.Vivo}, fim {mina.Mina.Fim}, vida {mina.Vida:+0.###;-0.###;0}, pericia +{mina.Pericia:0.0}, {mina.Relatos} relato(s)");

		// ---------------------------------------------------------------- (g) o raio na parede
		RaioDoCorte fr = RaioDeDanoCravado(raia, BpDaParedeDaEscada, DanoQueFereDoCorte, tiques: 75);
		GD.Print($"[projetil]      (medido) RAIO de {fr.Dano:0.00} por batida na parede, 75 tiques: {fr.Batidas.Count} batida(s) nos tiques [{string.Join(", ", fr.Batidas)}], "
				 + $"vida {fr.Vida:+0.###;-0.###;0}, {fr.Relatos} relato(s), corpo andou {fr.Andou:0.0} px, cabeca {fr.NaFrente:0.0} px atras dele");
		AfirmarPj("RAIO NA PAREDE: a cabeca PARA na frente do corpo e fica -- nao atravessa, nao se apaga, nao leva ninguem",
				  fr.Raio.Vivo && fr.Raio.Canalizando && fr.Raio.Encostado && fr.Raio.Arrastando == 0 && fr.Raio.BatendoSemFerir == fr.Alvo.Id
				  && Math.Abs(fr.NaFrente - Feixe.ContatoNoCorpo(fr.Raio)) < 1f && fr.PiorNaFrente < 1f,
				  $"vivo {fr.Raio.Vivo}, encostado {fr.Raio.Encostado}, levando {fr.Raio.Arrastando}, {fr.NaFrente:0.0} px a frente (contato {Feixe.ContatoNoCorpo(fr.Raio):0.0}), pior desvio {fr.PiorNaFrente:0.00} px");
		AfirmarPj("RAIO NA PAREDE: bate sem ferir -- vida intacta, nenhum relato, o corpo no lugar, de pe e sem o passo lento de quem apanha",
				  Math.Abs(fr.Vida) < 1e-9 && fr.Relatos == 0 && fr.Andou < 0.01f && fr.Alvo.Combate!.Stun <= 0 && fr.Alvo.TiquesDeVoo <= 0
				  && fr.Alvo.SobORaioAteMs == 0,
				  $"vida {fr.Vida:+0.###;-0.###;0}, {fr.Relatos} relato(s), andou {fr.Andou:0.00} px, stun {fr.Alvo.Combate!.Stun:0.##}, sob o raio ate {fr.Alvo.SobORaioAteMs}");
		AfirmarPj("RAIO NA PAREDE: uma batida a cada 5 tiques do DM (`Projetil.SegundosPorBatidaSemFerir`; no BYOND, 4,15 ds entre duas) -- e nao a cada ciclo de moer",
				  fr.Batidas.Count >= 4 && fr.MenorIntervalo >= 12 && fr.MaiorIntervalo <= 14,
				  $"{fr.Batidas.Count} batidas, intervalos de {fr.MenorIntervalo} a {fr.MaiorIntervalo} tiques de 1/30 s");

		RaioDoCorte fc = RaioDeDanoCravado(raia, BpDoIgualDaEscada, DanoQueFereDoCorte, tiques: 30);
		AfirmarPj("(controle) o MESMO raio de 12 num IGUAL fere, sai como golpe e moi a cada ciclo de 0,2 s",
				  fc.Vida < -0.1 && fc.Relatos >= 2 && fc.MenorIntervalo >= 5 && fc.MaiorIntervalo <= 8 && fc.Raio.BatendoSemFerir == 0,
				  $"vida {fc.Vida:+0.###;-0.###;0}, {fc.Relatos} relato(s), intervalos de {fc.MenorIntervalo} a {fc.MaiorIntervalo} tiques");

		// ---------------------------------------------------------------- (h) a parede anda raio adentro
		RaioDoCorte an = RaioQueAParedeAtravessa(raia);
		GD.Print($"[projetil]      (medido) A PAREDE ANDA 3 TILES CONTRA O RAIO: andou {an.Andou:0.0} px, vida {an.Vida:+0.###;-0.###;0}, "
				 + $"cabeca {an.NaFrente:0.0} px a frente no fim (pior desvio {an.PiorNaFrente:0.00} px), {an.Feixes} feixe(s), {an.Batidas.Count} batida(s)");
		AfirmarPj("A PAREDE ANDA RAIO ADENTRO (`objects.dm:451`: *they should be able to walk through beams*; o dono: *\"andar livremente como se ele fosse uma parede imparavel\"*): 3 tiles contra o raio, ilesa",
				  Math.Abs(an.Andou - 3 * T) < 0.5f && Math.Abs(an.Vida) < 1e-9 && an.Relatos == 0,
				  $"andou {an.Andou:0.0} px, vida {an.Vida:+0.###;-0.###;0}, {an.Relatos} relato(s)");
		AfirmarPj("...e a cabeca RECUA NA FRENTE dela, tique a tique: nunca em cima do corpo, e o raio continua um so, vivo e na mao do dono",
				  an.PiorNaFrente < 1f && Math.Abs(an.NaFrente - Feixe.ContatoNoCorpo(an.Raio)) < 1f
				  && an.Feixes == 1 && an.Raio.Vivo && an.Raio.Canalizando,
				  $"pior desvio {an.PiorNaFrente:0.00} px, {an.NaFrente:0.0} px a frente no fim, {an.Feixes} feixe(s), vivo {an.Raio.Vivo}");
		AfirmarPj("...e o alcance devolve o que a cabeca recuou (`AndouTiles` continua sendo da mao ate a cabeca)",
				  Math.Abs(an.Raio.AndouTiles - (an.Raio.Pos - an.Raio.Cauda).Length / T) < 0.6,
				  $"andou {an.Raio.AndouTiles:0.00} tiles, mao->cabeca {(an.Raio.Pos - an.Raio.Cauda).Length / T:0.00}");

		// ---------------------------------------------------------------- (i) quem vinha sendo LEVADO e vira parede
		// O raio PESADO (10 tiles por segundo) pega o MAIS FRACO a 6 tiles e vai levando; no tique em que o arrasto
		// comeca o poder do corpo salta pra cinco vezes o do tiro (o corpo que cresce no meio do raio -- uma forma, uma
		// explosao de raiva). Na batida seguinte ele e parede: nao ha golpe, e sem golpe nao ha empurrao.
		int cresceuEm = -1, soltouEm = -1;
		Vec2 ondeSoltou = default;
		double vidaAoSoltar = 0, andouAoSoltar = 0;
		RaioDoCorte lv = RaioDeDanoCravado(raia, BpDoMaisFracoDaEscada, DanoQueFereDoCorte, tiques: 50, receita: r => r.Velocidade = 0.5, aCadaTique: (i, alvo, raio) =>
		{
			if (cresceuEm < 0 && raio.Arrastando == alvo.Id)
			{
				alvo.Ficha.BP = BpDaParedeDaEscada;
				alvo.Ficha.Tick(agoraMs: NowMs());   // a conta de producao do poder expresso, como no `Forjar`
				cresceuEm = i;
			}
			else if (cresceuEm >= 0 && soltouEm < 0 && raio.Arrastando == 0)
			{
				soltouEm = i;
				ondeSoltou = alvo.Pos;
				vidaAoSoltar = alvo.Combate!.Corpo.Vida();
				andouAoSoltar = raio.AndouTiles;
			}
		});
		AfirmarPj("PREPARO: o raio pegou o mais fraco e vinha LEVANDO-o quando ele cresceu ate virar parede pro tiro",
				  cresceuEm >= 0 && KiContraCorpo.EhParede(VezesOPoderDoTiro(lv.Raio, lv.Alvo)),
				  $"arrasto no tique {cresceuEm}, agora com {VezesOPoderDoTiro(lv.Raio, lv.Alvo):0.###}x o poder do tiro");
		AfirmarPj("QUEM VINHA SENDO LEVADO E SOLTO na batida seguinte, longe do fim do alcance: sem golpe nao ha empurrao",
				  soltouEm > cresceuEm && soltouEm - cresceuEm <= 8 && andouAoSoltar < lv.Raio.MaxDistancia / 2,
				  $"cresceu no tique {cresceuEm}, soltou no {soltouEm}, com a cabeca a {andouAoSoltar:0.0} de {lv.Raio.MaxDistancia:0} tiles");
		AfirmarPj("...e dali em diante o corpo fica onde foi solto, nao perde mais vida, e o raio continua batendo nele sem ferir",
				  soltouEm >= 0 && (lv.Alvo.Pos - ondeSoltou).Length < 0.01f && Math.Abs(lv.Alvo.Combate!.Corpo.Vida() - vidaAoSoltar) < 1e-9
				  && lv.Raio.Vivo && lv.Raio.BatendoSemFerir == lv.Alvo.Id,
				  $"andou {(lv.Alvo.Pos - ondeSoltou).Length:0.00} px depois de solto, vida {vidaAoSoltar:0.###} -> {lv.Alvo.Combate!.Corpo.Vida():0.###}, batendo sem ferir em {lv.Raio.BatendoSemFerir}");
		RaioDoCorte semCrescer = RaioDeDanoCravado(raia, BpDoMaisFracoDaEscada, DanoQueFereDoCorte, tiques: 50, receita: r => r.Velocidade = 0.5);
		AfirmarPj("(controle) sem crescer, o MESMO corpo continua sendo levado pelo MESMO raio ate o fim da cena",
				  semCrescer.Raio.Arrastando == semCrescer.Alvo.Id && semCrescer.Andou > 4 * T,
				  $"levando {semCrescer.Raio.Arrastando}, o corpo andou {semCrescer.Andou / T:0.0} tiles");

		// ---------------------------------------------------------------- (j) o raio que estoura como bola
		RaioDoCorte db = RaioDeDanoCravado(raia, BpDaParedeDaEscada, DanoQueFereDoCorte, tiques: 30, receita: r => r.EstouraNoImpacto = true);
		AfirmarPj("O RAIO QUE ESTOURA COMO BOLA (o Death Beam), na parede: estoura no corpo e acaba de uma vez, sem ferir",
				  !db.Raio.Vivo && db.Raio.Fim == FimDeProjetil.Acertou && Math.Abs(db.Vida) < 1e-9 && db.Relatos == 0 && db.Batidas.Count == 1,
				  $"vivo {db.Raio.Vivo}, fim {db.Raio.Fim}, vida {db.Vida:+0.###;-0.###;0}, {db.Relatos} relato(s), {db.Batidas.Count} batida(s)");

		// ---------------------------------------------------------------- (k) a parede que pisa no tronco de um raio
		LimparTudoDaBancada();
		ServerPlayer deTronco = Forjar("FeixeDoTronco", raia, bp: BpDoFeixeDaEscada);
		deTronco.Facing = Facing.East;
		Projetil tronco = RaioDaBancada(deTronco, baseDano: 1);
		for (int i = 0; i < 25; i++) UmTiqueDeArrasto();
		List<Projetil> feixes = ProjeteisDaZona(deTronco.Zone.Hash);
		ServerPlayer pisou = Forjar("ParedeNoTronco", raia + new Vec2(4 * T, 0), bp: BpDaParedeDaEscada);
		CravarDanoFinal(tronco, pisou, DanoQueFereDoCorte);
		double vidaDoPisou = pisou.Combate!.Corpo.Vida();
		int relatosDoPisou = RelatosNoAlvo(pisou, UmTiqueDeArrasto).Count;
		AfirmarPj("A PAREDE QUE PISA NO TRONCO: o feixe e cortado e a cabeca nova para na frente dela (`Crossed`, `objects.dm:156-171`) -- sem ferir",
				  feixes.Count == 2 && tronco.Encostado && tronco.Arrastando == 0 && tronco.BatendoSemFerir == pisou.Id
				  && Math.Abs(NaFrenteDe(tronco, pisou) - Feixe.ContatoNoCorpo(tronco)) < 1f
				  && Math.Abs(pisou.Combate.Corpo.Vida() - vidaDoPisou) < 1e-9 && relatosDoPisou == 0,
				  $"{feixes.Count} feixe(s), encostado {tronco.Encostado}, {NaFrenteDe(tronco, pisou):0.0} px a frente, "
				  + $"vida {vidaDoPisou:0.###} -> {pisou.Combate.Corpo.Vida():0.###}, {relatosDoPisou} relato(s)");

		// ---------------------------------------------------------------- (m) o tiro nao quebra a concentracao da parede
		(bool Preparo, bool Caiu, FimDeProjetil Fim) emQuemCanaliza = BolaEmQuemCanaliza(raia, BpDaParedeDaEscada, DanoQueFereDoCorte);
		AfirmarPj("A BOLA NAO DERRUBA O RAIO DA PAREDE: o raio cai com golpe (`if(KB) stopbeaming()`, `beams.dm:73-74`), e isto nao e golpe",
				  emQuemCanaliza.Preparo && !emQuemCanaliza.Caiu && emQuemCanaliza.Fim == FimDeProjetil.Acertou,
				  $"canal de pe antes {emQuemCanaliza.Preparo}, caiu {emQuemCanaliza.Caiu}, fim da bola {emQuemCanaliza.Fim}");
		emQuemCanaliza = BolaEmQuemCanaliza(raia, BpDoIgualDaEscada, DanoQueFereDoCorte);
		AfirmarPj("(controle) a MESMA bola de 12 derruba o raio de um IGUAL",
				  emQuemCanaliza.Preparo && emQuemCanaliza.Caiu && emQuemCanaliza.Fim == FimDeProjetil.Acertou,
				  $"canal de pe antes {emQuemCanaliza.Preparo}, caiu {emQuemCanaliza.Caiu}, fim da bola {emQuemCanaliza.Fim}");

		// ---------------------------------------------------------------- (n) a guarda da parede
		// O EMBATE DE GUARDA (as maos segurando o raio; e deste port, o DM nao tem) VEM DEPOIS DA PAREDE: quem ergue a
		// guarda contra um raio que nao o feriria nao tem o que segurar. So ha disputa quando ha o que perder.
		(bool Embate, bool Plantou, double Vida, double VantagemDasMaos) naGuarda = RaioContraAGuardaSegurada(raia, BpDaParedeDaEscada);
		AfirmarPj("O RAIO NAO VIRA EMBATE DE GUARDA NA PAREDE: ele para na frente de quem defende, sem ferir e sem plantar ninguem numa disputa",
				  !naGuarda.Embate && naGuarda.Plantou && Math.Abs(naGuarda.Vida) < 1e-9,
				  $"embate {naGuarda.Embate}, cabeca batendo sem ferir {naGuarda.Plantou}, vida {naGuarda.Vida:+0.###;-0.###;0}");
		naGuarda = RaioContraAGuardaSegurada(raia, BpDoIgualDaEscada);
		AfirmarPj("(controle) o MESMO raio contra a guarda de um IGUAL vira embate: as maos o seguram",
				  naGuarda.Embate, $"embate {naGuarda.Embate}");
		// AS MAOS SE MEDEM PELO DANO (dono, 2026-10-08; `EmbateDeKi.PoderDeSegurar`): o raio de 12 fica ABAIXO do que
		// elas empatam (`EmbateDeKi.DanoQueAsMaosEmpatam`), e quem o segura entra na disputa na frente.
		AfirmarPj("...e seguram com VANTAGEM: a delas na disputa e o que as maos empatam sobre o dano do raio (o de 12 fica abaixo disso)",
				  Math.Abs(naGuarda.VantagemDasMaos - EmbateDeKi.DanoQueAsMaosEmpatam / DanoQueFereDoCorte) < 0.02,
				  $"vantagem das maos {naGuarda.VantagemDasMaos:0.##}, esperado {EmbateDeKi.DanoQueAsMaosEmpatam / DanoQueFereDoCorte:0.##}");

		// ---------------------------------------------------------------- (o) a outra metade: o tiro FRACO de um igual e golpe
		// O QUE O CORTE POR DANO JOGAVA FORA. O tiro de 5 de um igual estourava sem ferir (`objects.dm:355-357`) -- o
		// vilao de mesmo poder atirando sem efeito. Hoje quem nao e parede apanha, valha o golpe o que valer.
		ImpactoDoCorte fraca = BolaDeDanoCravado(chao, BpDoIgualDaEscada, DanoFracoDoCorte);
		AfirmarPj("PREPARO: a bola sai com o dano final cravado em 5 -- abaixo do antigo corte por dano -- contra um igual",
				  Math.Abs(fraca.Dano - DanoFracoDoCorte) < 1e-6 && DanoDeKi.FracoDemais(fraca.Dano) && Math.Abs(fraca.Vezes - 1) < 0.01,
				  $"dano final {fraca.Dano:0.######}, {fraca.Vezes:0.###}x o poder do tiro");
		AfirmarPj("O TIRO FRACO DE UM IGUAL E GOLPE: a bola de 5 fere e sai como golpe",
				  fraca.Vida < -0.1 && fraca.Relatos.Count == 1 && (Desfecho)fraca.Relatos[0].Desfecho == Desfecho.Acertou,
				  $"vida {fraca.Vida:+0.###;-0.###;0}, {fraca.Relatos.Count} relato(s)");
		RaioDoCorte fraco = RaioDeDanoCravado(raia, BpDoIgualDaEscada, DanoFracoDoCorte, tiques: 30);
		AfirmarPj("...e o raio de 5 por batida tambem: fere um igual a cada ciclo de moer, e nao na cadencia de quem bate sem ferir",
				  fraco.Vida < -0.1 && fraco.Relatos >= 2 && fraco.MenorIntervalo >= 5 && fraco.MaiorIntervalo <= 8 && fraco.Raio.BatendoSemFerir == 0,
				  $"vida {fraco.Vida:+0.###;-0.###;0}, {fraco.Relatos} relato(s), intervalos de {fraco.MenorIntervalo} a {fraco.MaiorIntervalo} tiques");

		// ---------------------------------------------------------------- (z) os defeitos injetados
		// O JOGO DE ANTES DE 2026-10-12: o corte por DANO, e nenhuma parede. As reguas de cima tem que ficar VERMELHAS
		// com ele -- pelos dois lados: o golpe de 12 fere quem hoje e parede, e o tiro de 5 nao fere quem hoje apanha.
		KiContraCorpo.RegraDeAntesDeTeste = true;
		try
		{
			b = BolaDeDanoCravado(chao, BpDaParedeDaEscada, DanoQueFereDoCorte);
			AfirmarPj("(defeito injetado: o jogo de antes) BOLA de 12 em quem hoje e parede: tira vida e sai como golpe",
					  b.Vida < -0.1 && b.Relatos.Count == 1 && (Desfecho)b.Relatos[0].Desfecho == Desfecho.Acertou,
					  $"vida {b.Vida:+0.###;-0.###;0}, {b.Relatos.Count} relato(s)");

			furo = BolaDeDanoCravado(chao, BpDaParedeDaEscada, DanoQueFereDoCorte, receita: r => r.Piercer = true);
			AfirmarPj("(defeito injetado: o jogo de antes) PERFURANTE de 12: fere quem hoje e parede e ATRAVESSA o corpo",
					  furo.Vida < -0.1 && furo.Tiro.Pos.X > furo.Alvo.Pos.X + T,
					  $"vida {furo.Vida:+0.###;-0.###;0}, bola {(furo.Tiro.Pos.X - furo.Alvo.Pos.X) / T:0.0} tiles alem do corpo");

			fr = RaioDeDanoCravado(raia, BpDaParedeDaEscada, DanoQueFereDoCorte, tiques: 30);
			AfirmarPj("(defeito injetado: o jogo de antes) RAIO de 12: fere quem hoje e parede a cada ciclo de moer -- e o LEVA",
					  fr.Vida < -0.1 && fr.Relatos >= 2 && fr.Andou > T && fr.MaiorIntervalo <= 8,
					  $"vida {fr.Vida:+0.###;-0.###;0}, {fr.Relatos} relato(s), andou {fr.Andou:0.0} px, intervalos ate {fr.MaiorIntervalo} tiques");

			fraca = BolaDeDanoCravado(chao, BpDoIgualDaEscada, DanoFracoDoCorte);
			AfirmarPj("(defeito injetado: o jogo de antes) BOLA de 5 num igual: estoura sem ferir e sem relato -- o vilao de mesmo poder atirando sem efeito",
					  Math.Abs(fraca.Vida) < 1e-9 && fraca.Relatos.Count == 0 && fraca.Tiro.Fim == FimDeProjetil.Acertou,
					  $"vida {fraca.Vida:+0.###;-0.###;0}, {fraca.Relatos.Count} relato(s), fim {fraca.Tiro.Fim}");

			fraco = RaioDeDanoCravado(raia, BpDoIgualDaEscada, DanoFracoDoCorte, tiques: 75);
			AfirmarPj("(defeito injetado: o jogo de antes) RAIO de 5 num igual: bate sem ferir, na cadencia de quem nao fere",
					  Math.Abs(fraco.Vida) < 1e-9 && fraco.Relatos == 0 && fraco.Batidas.Count >= 4 && fraco.MenorIntervalo >= 12,
					  $"vida {fraco.Vida:+0.###;-0.###;0}, {fraco.Relatos} relato(s), {fraco.Batidas.Count} batidas com intervalos de {fraco.MenorIntervalo} a {fraco.MaiorIntervalo} tiques");
		}
		finally { KiContraCorpo.RegraDeAntesDeTeste = false; }

		// A CABECA QUE NAO RECUA: o corpo entra no raio e a cabeca fica pra tras, desenhada em cima dele.
		Feixe.CabecaNaoRecuaDeTeste = true;
		try
		{
			an = RaioQueAParedeAtravessa(raia);
			AfirmarPj("(defeito injetado: a cabeca nao recua) A PAREDE ANDA RAIO ADENTRO e a cabeca fica EM CIMA dela entre uma batida e outra",
					  an.PiorNaFrente > Feixe.MeioCorpo, $"pior desvio {an.PiorNaFrente:0.0} px, {an.Feixes} feixe(s)");
		}
		finally { Feixe.CabecaNaoRecuaDeTeste = false; }

		LimparTudoDaBancada();
	}

	// =====================================================================
	// AS REGUAS DESTA FAMILIA
	// =====================================================================
	/// <summary>O dano final que o `Acertar` calcula AGORA pra este tiro neste corpo -- a mesma chamada, com os mesmos argumentos.</summary>
	private static double DanoFinalDe(Projetil p, ServerPlayer alvo)
		=> DanoDeKi.Final(p.ModsAgora(), p.BaseDano, p.MaxDano, p.Bp, alvo.Combate!, alvo.Combate!.Bloqueando, p.Fisico);

	/// <summary>
	/// CRAVA O DANO FINAL deste tiro neste corpo. A conta e a de producao e e LINEAR no `mods`: o `ModsBase` do tiro
	/// desta prova -- o unico lugar que a bancada alcanca sem mexer na receita nem na conta -- e escalado ate ela
	/// devolver o numero pedido. (A chance de deflexao anda junto, ao contrario: quem precisa do dado crava a chance,
	/// pelo mesmo lever -- `CravarAChance`, na familia 15.)
	/// </summary>
	private static void CravarDanoFinal(Projetil p, ServerPlayer alvo, double dano)
		=> p.ModsBase *= dano / DanoFinalDe(p, alvo);

	/// <summary>
	/// O `base_damage` COM QUE O TIRO DESTE CORPO FAZ O GOLPE DE BANCADA NUM IGUAL: dano final de 12 por impacto num
	/// corpo com a ficha e o poder dele mesmo, pela conta de producao (`Projetil.ModsDoTiro` e `DanoDeKi.Final`; a
	/// conta e QUADRATICA na base, porque o `Disparar` a multiplica no `mods` e o `Final` a usa de novo).
	///
	/// ============================ POR QUE 12, E NAO COCEGAS ============================
	/// As familias que medem GEOMETRIA (o arrasto, a cabeca na frente, o corte do tronco, a faisca) ja atiraram com
	/// `baseDano: 0.002`: um impacto que nao machuca o boneco no meio da medida. De 2026-10-08 a 2026-10-12 esse
	/// impacto nao era golpe (o corte por dano do DM, `objects.dm:355-357`), e o tiro dessas cenas subiu pra pouco
	/// acima de 10. O corte por dano saiu -- quem nao e ferido hoje e quem e PAREDE pro tiro (`KiContraCorpo`) --, mas
	/// o 12 ficou: e um dano que se le sem derrubar ninguem, e e golpe tambem no jogo de antes, que os contra-exemplos
	/// repoem. Os corpos de bancada sao todos a mesma ficha (`Forjar`), entao "um igual" e o alvo.
	/// ===================================================================================
	/// </summary>
	/// <param name="deGuarda">
	/// O igual esta de GUARDA ERGUIDA: a guarda divide o dano de novo (`dmg /= 2 * log_4(...)`, `objects.dm:342-344`), e
	/// o tiro que faz 12 em quem esta de maos abaixadas faz bem menos contra as maos erguidas.
	/// </param>
	private static double BaseQueFereUmIgual(ServerPlayer atira, TipoDeProjetil tipo = TipoDeProjetil.Beam, bool deGuarda = false)
	{
		double comBaseUm = DanoDeKi.Final(Projetil.ModsDoTiro(atira.Ficha, tipo), 1, 0, atira.Ficha.expressedBP, atira.Combate!, deGuarda);
		return Math.Sqrt(DanoQueFereDoCorte / comBaseUm);
	}

	/// <summary>
	/// O `base_damage` COM QUE O TIRO DESTE CORPO FAZ O GOLPE DE BANCADA NESTE ALVO (12 por impacto), com o poder, a
	/// ficha e a guarda que o alvo tem AGORA. E o <see cref="BaseQueFereUmIgual"/> pras cenas em que os dois nao sao
	/// iguais (o mais fraco que o raio leva, o fraco contra a muralha): la a razao dos poderes muda o dano, e a base
	/// tem que acompanhar.
	/// </summary>
	private static double BaseQueFere(ServerPlayer atira, ServerPlayer alvo, TipoDeProjetil tipo = TipoDeProjetil.Beam)
	{
		double comBaseUm = DanoDeKi.Final(Projetil.ModsDoTiro(atira.Ficha, tipo), 1, 0, atira.Ficha.expressedBP,
										  alvo.Combate!, alvo.Combate!.Bloqueando);
		return Math.Sqrt(DanoQueFereDoCorte / comBaseUm);
	}

	/// <summary>A MESMA RECEITA com a base do golpe de bancada num igual (<see cref="BaseQueFereUmIgual"/>). Devolve a propria.</summary>
	private static ReceitaDeProjetil QueFereUmIgual(ReceitaDeProjetil r, ServerPlayer atira, bool deGuarda = false)
	{
		r.BaseDano = BaseQueFereUmIgual(atira, r.Tipo, deGuarda);
		return r;
	}

	/// <summary>A que distancia A FRENTE da cabeca deste tiro o corpo esta, pelo eixo do tiro.</summary>
	private static float NaFrenteDe(Projetil p, ServerPlayer corpo)
		=> (corpo.Pos.X - p.Pos.X) * p.Rumo.X + (corpo.Pos.Y - p.Pos.Y) * p.Rumo.Y;

	/// <summary>
	/// O que UMA bola fez a UM corpo. `Vida`, `Pericia`, `Ki` e `Andou` sao a DIFERENCA entre depois e antes; `Vezes`
	/// e quantas vezes o poder do tiro o corpo tinha (a razao que a escada le).
	/// </summary>
	private readonly record struct ImpactoDoCorte(
		Projetil Tiro, ServerPlayer Atirador, ServerPlayer Alvo, double Dano, double Chance, double Vezes,
		List<Protocol.HitEvent> Relatos, double Vida, double Pericia, double Ki, float Andou);

	/// <summary>
	/// UMA BOLA de quem tem o poder do feixe da escada contra um corpo parado a sete tiles (ela chega com seis de
	/// viagem, alem dos quatro em que arremessa -- `Projetil.FatorDeEmpurrao`), ate ela acabar.
	/// </summary>
	/// <param name="bpDoAlvo">O poder de quem leva -- o DEGRAU da cena: parede, igual, logo abaixo ou logo acima do degrau.</param>
	/// <param name="dano">O dano final a cravar no tiro. Zero = nao cravar: vale o que a receita e os dois corpos derem.</param>
	/// <param name="comDado">Com o sorteio de deflexao ligado (`Deflectivel`). As cenas que nao sao sobre o dado o desligam.</param>
	/// <param name="chance">A chance de deflexao a cravar no tiro, em porcento (`CravarAChance`). Zero = nao cravar.</param>
	private ImpactoDoCorte BolaDeDanoCravado(Vec2 chao, double bpDoAlvo, double dano, bool comDado = false, double chance = 0,
											 Action<ReceitaDeProjetil>? receita = null)
	{
		LimparTudoDaBancada();
		ServerPlayer atira = Forjar("AtiraNoDegrau", chao, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		ServerPlayer alvo = Forjar("AlvoNoDegrau", chao + new Vec2(7 * ZoneCollision.TileSize, 0), bp: bpDoAlvo);

		var r = new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 20, Deflectivel = comDado, Nome = "bola do degrau",
		};
		receita?.Invoke(r);
		Projetil p = Disparar(atira, r);
		if (dano > 0) CravarDanoFinal(p, alvo, dano);
		if (chance > 0) CravarAChance(p, alvo, chance);

		double final = DanoFinalDe(p, alvo), chanceDaConta = ChanceDeDeflexaoDe(p, alvo), vezes = VezesOPoderDoTiro(p, alvo);
		double vida = alvo.Combate!.Corpo.Vida(), pericia = alvo.Ficha.kidefenseskill, ki = alvo.Ficha.Ki;
		Vec2 lugar = alvo.Pos;
		List<Protocol.HitEvent> relatos = RelatosNoAlvo(alvo, () =>
		{
			for (int i = 0; i < 30 * 8 && p.Vivo; i++) UmTiqueDeArrasto();
		});
		return new ImpactoDoCorte(p, atira, alvo, final, chanceDaConta, vezes, relatos, alvo.Combate.Corpo.Vida() - vida,
								  alvo.Ficha.kidefenseskill - pericia, alvo.Ficha.Ki - ki, (alvo.Pos - lugar).Length);
	}

	/// <summary>
	/// UMA BOLA PARADA (rumo nulo, como as do `Ki_Bomb`) a oito pixels de um corpo, por vinte tiques ou ate ela acabar.
	/// </summary>
	private (Projetil Mina, double Vida, double Pericia, int Relatos) MinaDeDanoCravado(Vec2 chao, double bpDeQuemPisa, double dano)
	{
		LimparTudoDaBancada();
		ServerPlayer dono = Forjar("DonoDaMinaNoDegrau", chao, bp: BpDoFeixeDaEscada);
		ServerPlayer pisa = Forjar("PisaNaMina", chao + new Vec2(6 * ZoneCollision.TileSize, 0), bp: bpDeQuemPisa);

		Projetil mina = Disparar(dono, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, AlcanceTiles = 20, Deflectivel = false, Nome = "mina do degrau",
		}, rumoDado: Vec2.Zero, deOnde: pisa.Pos + new Vec2(8, 0));
		CravarDanoFinal(mina, pisa, dano);

		double vida = pisa.Combate!.Corpo.Vida(), pericia = pisa.Ficha.kidefenseskill;
		int relatos = RelatosNoAlvo(pisa, () =>
		{
			for (int i = 0; i < 20 && mina.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
		}).Count;
		return (mina, pisa.Combate.Corpo.Vida() - vida, pisa.Ficha.kidefenseskill - pericia, relatos);
	}

	/// <summary>
	/// O que UM raio fez a UM corpo ao longo de uma cena. `Batidas` sao os tiques em que o `Acertar` rodou (cada um
	/// soma 0,1 na pericia de defesa de ki de quem leva, golpe ou nao); `PiorNaFrente` e o maior desvio, depois da
	/// primeira batida, entre onde a cabeca estava e onde ela devia estar -- o contato, na frente do corpo.
	/// </summary>
	private readonly record struct RaioDoCorte(
		Projetil Raio, ServerPlayer Atirador, ServerPlayer Alvo, double Dano, List<int> Batidas, int Relatos,
		double Vida, float Andou, float NaFrente, float PiorNaFrente, int Feixes)
	{
		public int MenorIntervalo => Intervalos().DefaultIfEmpty(0).Min();
		public int MaiorIntervalo => Intervalos().DefaultIfEmpty(int.MaxValue).Max();

		private IEnumerable<int> Intervalos()
		{
			for (int i = 1; i < Batidas.Count; i++) yield return Batidas[i] - Batidas[i - 1];
		}
	}

	/// <summary>
	/// UM RAIO NA MAO (como o <see cref="RaioDaBancada"/>: canalizando, sem o dado) de quem tem o poder do feixe da
	/// escada contra um corpo parado a seis tiles, com o dano final CHEIO cravado, por <paramref name="tiques"/>
	/// tiques do servidor.
	/// </summary>
	/// <param name="bpDoAlvo">O poder de quem leva -- o DEGRAU da cena.</param>
	/// <param name="aCadaTique">
	/// O que a cena faz DEPOIS de cada tique e da medida dele (andar com o corpo, faze-lo crescer): o tique, o corpo e
	/// o raio.
	/// </param>
	private RaioDoCorte RaioDeDanoCravado(Vec2 raia, double bpDoAlvo, double dano, int tiques, Action<int, ServerPlayer, Projetil>? aCadaTique = null,
										  Action<ReceitaDeProjetil>? receita = null)
	{
		LimparTudoDaBancada();
		ServerPlayer atira = Forjar("FeixeNoDegrau", raia, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		ServerPlayer alvo = Forjar("NaFrenteDoFeixe", raia + new Vec2(6 * ZoneCollision.TileSize, 0), bp: bpDoAlvo);

		var r = new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam, BaseDano = 1, Velocidade = 1, AlcanceTiles = 40, Deflectivel = false, Nome = "Onda de Ki",
		};
		receita?.Invoke(r);
		Projetil raio = Disparar(atira, r);
		raio.Canalizando = true;
		CravarDanoFinal(raio, alvo, dano);
		double final = DanoFinalDe(raio, alvo);

		double vida = alvo.Combate!.Corpo.Vida(), pericia = alvo.Ficha.kidefenseskill;
		Vec2 lugar = alvo.Pos;
		var batidas = new List<int>();
		float pior = 0;
		int relatos = RelatosNoAlvo(alvo, () =>
		{
			for (int i = 0; i < tiques; i++)
			{
				UmTiqueDeArrasto();
				if (alvo.Ficha.kidefenseskill > pericia + 1e-9)
				{
					batidas.Add(i);
					pericia = alvo.Ficha.kidefenseskill;
				}
				if (batidas.Count > 0 && raio.Vivo)
					pior = MathF.Max(pior, MathF.Abs(NaFrenteDe(raio, alvo) - Feixe.ContatoNoCorpo(raio)));
				aCadaTique?.Invoke(i, alvo, raio);
			}
		}).Count;
		return new RaioDoCorte(raio, atira, alvo, final, batidas, relatos, alvo.Combate.Corpo.Vida() - vida,
							   (alvo.Pos - lugar).Length, NaFrenteDe(raio, alvo), pior, ProjeteisDaZona(atira.Zone.Hash).Count);
	}

	/// <summary>
	/// O RAIO PLANTADO NUMA PAREDE QUE ANDA CONTRA ELE: do tique 20 ao 43 o corpo da 4 px por tique na direcao de
	/// quem atira -- tres tiles --, e a cena segue ate o tique 70 (mais de um ciclo de batida depois de ele parar).
	/// </summary>
	private RaioDoCorte RaioQueAParedeAtravessa(Vec2 raia)
		=> RaioDeDanoCravado(raia, BpDaParedeDaEscada, DanoQueFereDoCorte, tiques: 70, aCadaTique: (i, alvo, _) =>
		{
			if (i is >= 20 and < 44) alvo.Pos += new Vec2(-4, 0);
		});

	/// <summary>
	/// UM KI WAVE DE VERDADE (`Canalizar`, a carga, o canal) CONTRA UMA GUARDA SEGURADA ha mais que a janela do deflect
	/// (`SegurarAGuarda`), a oito tiles, por tres segundos ou ate o embate comecar. O raio e o MESMO nas duas cenas
	/// que o chamam: a base com que ele faz 12 ATRAVES da guarda de um igual (<see cref="QueFereUmIgual"/>). Devolve
	/// se virou embate, se a cabeca ficou batendo sem ferir em quem defende, a vida que ele perdeu e a vantagem com
	/// que as maos dele entraram na disputa (zero sem embate).
	/// </summary>
	/// <param name="bpDaGuarda">O poder de quem defende -- o DEGRAU da cena.</param>
	private (bool Embate, bool Plantou, double Vida, double VantagemDasMaos) RaioContraAGuardaSegurada(Vec2 raia, double bpDaGuarda)
	{
		LimparEmbatesDaBancada();
		ServerPlayer atira = Forjar("RaioNaGuarda", raia, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		atira.Ficha.Ki = atira.Ficha.MaxKi;
		ServerPlayer guarda = Forjar("GuardaSegurada", raia + new Vec2(8 * ZoneCollision.TileSize, 0), bp: bpDaGuarda);
		guarda.Ficha.Ki = guarda.Ficha.MaxKi;
		SegurarAGuarda(guarda);
		double vida = guarda.Combate!.Corpo.Vida();

		Canalizar(atira, "Ki_Wave", 10 * atira.Ficha.BaseDrain(), QueFereUmIgual(SemDeflexao(), atira, deGuarda: true));

		Projetil? raio = null;
		bool embate = false;
		for (int i = 0; i < 30 * 3 && !embate; i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
			TickDosEmbatesDeKi(Protocol.TickSeconds);
			raio ??= _canais.GetValueOrDefault(atira.Id)?.Raio;
			embate = _emEmbateDeKi.ContainsKey(guarda.Id);
		}
		bool plantou = raio is { Vivo: true, Encostado: true } && raio.BatendoSemFerir == guarda.Id;
		double perdeu = guarda.Combate.Corpo.Vida() - vida;
		double vantagemDasMaos = _emEmbateDeKi.TryGetValue(guarda.Id, out DisputaDeKi? d)
			? (d.A.Quem == guarda ? d.A : d.B).Vantagem : 0;
		LimparEmbatesDaBancada();
		return (embate, plantou, perdeu, vantagemDasMaos);
	}

	/// <summary>
	/// UMA BOLA DE DANO CRAVADO NAS COSTAS DE QUEM SEGURA UM RAIO (o preparo da familia 13). Devolve se o canal estava
	/// de pe antes dela, se caiu, e como a bola acabou.
	/// </summary>
	/// <param name="bpDeQuemCanaliza">O poder de quem leva a bola -- o DEGRAU da cena.</param>
	private (bool Preparo, bool Caiu, FimDeProjetil Fim) BolaEmQuemCanaliza(Vec2 raia, double bpDeQuemCanaliza, double dano)
	{
		LimparTudoDaBancada();
		ServerPlayer canal = Forjar("CanalNoDegrau", raia + new Vec2(4 * ZoneCollision.TileSize, 0), bp: bpDeQuemCanaliza);
		canal.Facing = Facing.East;
		canal.Ficha.Ki = canal.Ficha.MaxKi;
		ServerPlayer bolista = Forjar("BolistaNoDegrau", raia, bp: BpDoFeixeDaEscada);
		bolista.Facing = Facing.East;

		Canalizar(canal, "Ki_Wave", 10 * canal.Ficha.BaseDrain(), SemDeflexao());
		for (int i = 0; i < 40; i++) { TickDosCanaisDeKi(Protocol.TickSeconds); TickDosProjeteis(Protocol.TickSeconds); }
		bool preparo = _canais.GetValueOrDefault(canal.Id) is { Atirando: true, Raio.Vivo: true };

		Projetil bola = Disparar(bolista, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Deflectivel = false, Nome = "bola de bancada",
		});
		CravarDanoFinal(bola, canal, dano);
		for (int t = 0; t < 90 && bola.Vivo; t++) { TickDosCanaisDeKi(Protocol.TickSeconds); TickDosProjeteis(Protocol.TickSeconds); }
		return (preparo, !_canais.ContainsKey(canal.Id), bola.Fim);
	}
}
