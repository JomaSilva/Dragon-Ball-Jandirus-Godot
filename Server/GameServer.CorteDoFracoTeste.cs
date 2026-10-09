using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// 16) O CORTE DOS FRACOS (`--projetilteste`) -- o `if(...ResistCheck(dmg)*BPModulus(...) &lt; 10)` do `Bump`
/// (`objects.dm:355-357`).
///
/// ============================ O QUE SE MEDE ============================
/// O que UM impacto de ki faz a UM corpo quando o dano final dele fica abaixo de `DanoDeKi.CorteDoFraco`, lido do
/// estado de producao (a vida, a pericia de defesa de ki, o Ki, a posicao e a paralisia de quem leva; o `Fim` e a
/// cabeca do tiro) e do FIO (`EscutaDeGolpes`: o impacto que nao e golpe nao manda relato nenhum).
///
/// O DANO E CRAVADO NO TIRO (<see cref="CravarDanoFinal"/>): a receita e a conta sao as de producao, e o `ModsBase`
/// do tiro desta prova e escalado ate o `DanoDeKi.Final` -- a mesma chamada do `Acertar` -- devolver o numero
/// pedido. Cada cena tem ao lado o CONTROLE com o mesmo tiro acima do corte: a regua que diz "nao feriu" tem que
/// saber dizer "feriu".
///
/// ============================ DE ONDE VEM CADA AFIRMACAO ============================
/// Do DM MEDIDO no BYOND 516 em 2026-10-08 (mundo minimo com o `Bump`, os tres andares do `Move()`, o `explode()`
/// e o `DamageLimb` copiados a letra) -- os numeros estao no `DanoDeKi.CorteDoFraco` e no `EstourarSemFerir`. Ate
/// aquele dia o `Acertar` nao tinha o ramo: um tiro de 5 tirava 5 de um membro e era sorteado como qualquer outro.
/// Os contra-exemplos sao o `DanoDeKi.TiroFracoFereDeTeste` e o `Feixe.CabecaNaoRecuaDeTeste`, no fim da familia.
/// ===================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>O dano das cenas que ficam ABAIXO do corte: o "tiro de 5".</summary>
	private const double DanoFracoDoCorte = 5;

	/// <summary>O dano do CONTROLE: o mesmo tiro, acima do corte.</summary>
	private const double DanoQueFereDoCorte = 12;

	private void OCorteDosFracos()
	{
		GD.Print("[projetil] -- 16) O CORTE DOS FRACOS: o impacto de ki com dano final abaixo de 10 nao e golpe (`objects.dm:355-357`)");
		const int T = ZoneCollision.TileSize;
		Vec2 chao = CorredorLivre(24);
		Vec2 raia = CorredorSeco(30);

		// ---------------------------------------------------------------- (0) a pergunta, sozinha
		AfirmarPj("o limiar e 10 e e ESTRITO: 9,999999 e fraco demais, 10 cravado ja e golpe (`objects.dm:355`)",
				  DanoDeKi.CorteDoFraco == 10 && DanoDeKi.FracoDemais(0) && DanoDeKi.FracoDemais(9.999999) && !DanoDeKi.FracoDemais(10),
				  $"corte {DanoDeKi.CorteDoFraco}");

		// ---------------------------------------------------------------- (a) a bola fraca
		ImpactoDoCorte b = BolaDeDanoCravado(chao, DanoFracoDoCorte);
		GD.Print($"[projetil]      (medido) BOLA de {b.Dano:0.00}: {b.Relatos.Count} relato(s), vida {b.Vida:+0.###;-0.###;0}, pericia +{b.Pericia:0.0}, "
				 + $"fim {b.Tiro.Fim} a {(b.Tiro.Pos - b.Alvo.Pos).Length:0.0} px do corpo");
		AfirmarPj("PREPARO: a bola sai com o dano final cravado em 5 (a conta e a do `Acertar`)",
				  Math.Abs(b.Dano - DanoFracoDoCorte) < 1e-6, $"dano final {b.Dano:0.######}");
		AfirmarPj("BOLA FRACA: nao fere -- a vida de quem leva nao muda (`objects.dm:355-357`; no BYOND: 0 de 600)",
				  Math.Abs(b.Vida) < 1e-9, $"vida {b.Vida:+0.###;-0.###;0}");
		AfirmarPj("BOLA FRACA: nao e golpe -- nenhum relato sai pro fio (sem clarao, sem faisca, sem ferida)",
				  b.Relatos.Count == 0, $"{b.Relatos.Count} relato(s)");
		AfirmarPj("BOLA FRACA: ESTOURA no corpo e acaba ali (`explodeme` -> `explode()`, `objects.dm:565-566`)",
				  !b.Tiro.Vivo && b.Tiro.Fim == FimDeProjetil.Acertou && (b.Tiro.Pos - b.Alvo.Pos).Length <= Projetil.RaioDeImpacto + 0.5f,
				  $"vivo {b.Tiro.Vivo}, fim {b.Tiro.Fim} a {(b.Tiro.Pos - b.Alvo.Pos).Length:0.0} px");
		AfirmarPj("BOLA FRACA: o que o `Bump` faz ANTES da linha 355 continua valendo -- o credito, a tag de combate e o treino de UM encontro (0,1)",
				  b.Alvo.UltimoAgressor == b.Atirador.Id && b.Alvo.Combate!.EmCombate > 0 && Math.Abs(b.Pericia - 0.1) < 1e-9,
				  $"agressor {b.Alvo.UltimoAgressor} (atirador {b.Atirador.Id}), tag {b.Alvo.Combate!.EmCombate:0.#} s, pericia +{b.Pericia:0.0}");
		AfirmarPj("BOLA FRACA: nao empurra nem atordoa -- o corpo fica onde estava",
				  b.Andou < 0.01f && b.Alvo.TiquesDeVoo <= 0 && b.Alvo.Combate!.Stun <= 0,
				  $"andou {b.Andou:0.00} px, voo {b.Alvo.TiquesDeVoo}, stun {b.Alvo.Combate!.Stun:0.##}");

		ImpactoDoCorte c = BolaDeDanoCravado(chao, DanoQueFereDoCorte);
		AfirmarPj("(controle) a MESMA bola com dano final 12 fere e sai como golpe -- a regua enxerga dano",
				  c.Vida < -0.1 && c.Relatos.Count == 1 && (Desfecho)c.Relatos[0].Desfecho == Desfecho.Acertou && c.Tiro.Fim == FimDeProjetil.Acertou,
				  $"vida {c.Vida:+0.###;-0.###;0}, {c.Relatos.Count} relato(s), fim {c.Tiro.Fim}");

		// ---------------------------------------------------------------- (b) o limiar, no jogo
		ImpactoDoCorte abaixo = BolaDeDanoCravado(chao, 9.99);
		ImpactoDoCorte acima = BolaDeDanoCravado(chao, 10.01);
		AfirmarPj("O LIMIAR NO JOGO: 9,99 nao fere e 10,01 fere (no BYOND: 0 de 200 com 9,96 e 161 de 200 com 10,08)",
				  Math.Abs(abaixo.Vida) < 1e-9 && abaixo.Relatos.Count == 0 && acima.Vida < -0.1 && acima.Relatos.Count == 1,
				  $"9,99: vida {abaixo.Vida:+0.###;-0.###;0}, {abaixo.Relatos.Count} relato(s) | 10,01: vida {acima.Vida:+0.###;-0.###;0}, {acima.Relatos.Count} relato(s)");

		// ---------------------------------------------------------------- (c) o corte vem ANTES do sorteio
		// O PAR QUE A FAMILIA 15 USAVA ATE O CORTE: um fraco (500) atira num forte (500.000), com o dado ligado. A
		// chance de deflexao passa de 200% -- e o tiro nem chega a ser sorteado, porque o dano final dele e centesimos.
		ImpactoDoCorte s = BolaDeDanoCravado(chao, 0, bpDoAtirador: 500, bpDoAlvo: 500_000, comDado: true);
		AfirmarPj("PREPARO: fraco contra forte -- o dano final fica abaixo do corte e a chance de deflexao passa de 200% (o raspao seria CERTO)",
				  s.Dano < DanoDeKi.CorteDoFraco && s.Chance / 2 >= 100, $"dano final {s.Dano:0.####}, chance {s.Chance:0.#}%");
		AfirmarPj("SEM SORTEIO: nem raspao nem deflexao -- so o treino de levar (0,1, e nao 0,5), o Ki de quem leva intacto (no BYOND: 0 raspoes em 200)",
				  s.Relatos.Count == 0 && s.Tiro.Fim == FimDeProjetil.Acertou && Math.Abs(s.Pericia - 0.1) < 1e-9
				  && Math.Abs(s.Ki) < 1e-9 && Math.Abs(s.Vida) < 1e-9,
				  $"{s.Relatos.Count} relato(s), fim {s.Tiro.Fim}, pericia +{s.Pericia:0.0}, Ki {s.Ki:+0.###;-0.###;0}, vida {s.Vida:+0.###;-0.###;0}");

		// ---------------------------------------------------------------- (d) a paralisia vem antes do corte
		ImpactoDoCorte par = BolaDeDanoCravado(chao, DanoFracoDoCorte, receita: r => r.Paralisia = true);
		bool trancou = ParalisiaAtiva(par.Alvo.Id);
		EsquecerParalisia(par.Alvo.Id);
		AfirmarPj("A PARALISIA VEM ANTES DO CORTE (`objects.dm:347-354`): o tiro fraco que a carrega tranca as pernas e nao fere (no BYOND: 200 de 200)",
				  trancou && Math.Abs(par.Vida) < 1e-9 && par.Relatos.Count == 0,
				  $"paralisado {trancou}, vida {par.Vida:+0.###;-0.###;0}, {par.Relatos.Count} relato(s)");

		// ---------------------------------------------------------------- (e) a perfurante
		ImpactoDoCorte furo = BolaDeDanoCravado(chao, DanoFracoDoCorte, receita: r => r.Piercer = true);
		AfirmarPj("A PERFURANTE FRACA TAMBEM ACABA NO CORPO: o `density = 0` que a deixa passar mora dentro do ramo do dano (`objects.dm:479-481`)",
				  !furo.Tiro.Vivo && furo.Tiro.Fim == FimDeProjetil.Acertou
				  && (furo.Tiro.Pos - furo.Alvo.Pos).Length <= Projetil.RaioDeImpacto + 0.5f && Math.Abs(furo.Vida) < 1e-9,
				  $"fim {furo.Tiro.Fim} a {(furo.Tiro.Pos - furo.Alvo.Pos).Length:0.0} px, vida {furo.Vida:+0.###;-0.###;0}");

		// ---------------------------------------------------------------- (f) a bola parada
		(Projetil Mina, double Vida, double Pericia, int Relatos) mina = MinaDeDanoCravado(chao, DanoFracoDoCorte);
		AfirmarPj("A MINA FRACA: quem pisa nela a estoura sem se ferir, num encontro so (`mobBump.dm:57-58` chama o `Bump` dela, e o `explode()` a tira do mundo)",
				  !mina.Mina.Vivo && mina.Mina.Fim == FimDeProjetil.Acertou && Math.Abs(mina.Vida) < 1e-9
				  && Math.Abs(mina.Pericia - 0.1) < 1e-9 && mina.Relatos == 0,
				  $"viva {mina.Mina.Vivo}, fim {mina.Mina.Fim}, vida {mina.Vida:+0.###;-0.###;0}, pericia +{mina.Pericia:0.0}, {mina.Relatos} relato(s)");

		// ---------------------------------------------------------------- (g) o raio fraco
		RaioDoCorte fr = RaioDeDanoCravado(raia, DanoFracoDoCorte, tiques: 75);
		GD.Print($"[projetil]      (medido) RAIO de {fr.Dano:0.00} por batida, 75 tiques: {fr.Batidas.Count} batida(s) nos tiques [{string.Join(", ", fr.Batidas)}], "
				 + $"vida {fr.Vida:+0.###;-0.###;0}, {fr.Relatos} relato(s), corpo andou {fr.Andou:0.0} px, cabeca {fr.NaFrente:0.0} px atras dele");
		AfirmarPj("RAIO FRACO: a cabeca PARA na frente do corpo e fica -- nao atravessa, nao se apaga, nao leva ninguem",
				  fr.Raio.Vivo && fr.Raio.Canalizando && fr.Raio.Encostado && fr.Raio.Arrastando == 0
				  && Math.Abs(fr.NaFrente - Feixe.ContatoNoCorpo(fr.Raio)) < 1f && fr.PiorNaFrente < 1f,
				  $"vivo {fr.Raio.Vivo}, encostado {fr.Raio.Encostado}, levando {fr.Raio.Arrastando}, {fr.NaFrente:0.0} px a frente (contato {Feixe.ContatoNoCorpo(fr.Raio):0.0}), pior desvio {fr.PiorNaFrente:0.00} px");
		AfirmarPj("RAIO FRACO: bate sem ferir -- vida intacta, nenhum relato, o corpo no lugar e de pe",
				  Math.Abs(fr.Vida) < 1e-9 && fr.Relatos == 0 && fr.Andou < 0.01f && fr.Alvo.Combate!.Stun <= 0 && fr.Alvo.TiquesDeVoo <= 0,
				  $"vida {fr.Vida:+0.###;-0.###;0}, {fr.Relatos} relato(s), andou {fr.Andou:0.00} px, stun {fr.Alvo.Combate!.Stun:0.##}");
		AfirmarPj("RAIO FRACO: uma batida a cada 5 tiques do DM (`Projetil.SegundosPorBatidaSemFerir`; no BYOND, 4,15 ds entre duas) -- e nao a cada ciclo de moer",
				  fr.Batidas.Count >= 4 && fr.MenorIntervalo >= 12 && fr.MaiorIntervalo <= 14,
				  $"{fr.Batidas.Count} batidas, intervalos de {fr.MenorIntervalo} a {fr.MaiorIntervalo} tiques de 1/30 s");

		RaioDoCorte fc = RaioDeDanoCravado(raia, DanoQueFereDoCorte, tiques: 30);
		AfirmarPj("(controle) o MESMO raio com 12 por batida fere, sai como golpe, moi a cada ciclo de 0,2 s e LEVA o corpo",
				  fc.Vida < -0.1 && fc.Relatos >= 2 && fc.Andou > T && fc.MenorIntervalo >= 5 && fc.MaiorIntervalo <= 8,
				  $"vida {fc.Vida:+0.###;-0.###;0}, {fc.Relatos} relato(s), andou {fc.Andou:0.0} px, intervalos de {fc.MenorIntervalo} a {fc.MaiorIntervalo} tiques");

		// ---------------------------------------------------------------- (h) o forte anda raio adentro
		RaioDoCorte an = RaioQueOForteAtravessa(raia);
		GD.Print($"[projetil]      (medido) O FORTE ANDA 3 TILES CONTRA O RAIO: andou {an.Andou:0.0} px, vida {an.Vida:+0.###;-0.###;0}, "
				 + $"cabeca {an.NaFrente:0.0} px a frente no fim (pior desvio {an.PiorNaFrente:0.00} px), {an.Feixes} feixe(s), {an.Batidas.Count} batida(s)");
		AfirmarPj("O FORTE ANDA RAIO ADENTRO (`objects.dm:451`: *they should be able to walk through beams*): 3 tiles contra o raio, ileso",
				  Math.Abs(an.Andou - 3 * T) < 0.5f && Math.Abs(an.Vida) < 1e-9 && an.Relatos == 0,
				  $"andou {an.Andou:0.0} px, vida {an.Vida:+0.###;-0.###;0}, {an.Relatos} relato(s)");
		AfirmarPj("...e a cabeca RECUA NA FRENTE dele, tique a tique: nunca em cima do corpo, e o raio continua um so, vivo e na mao do dono",
				  an.PiorNaFrente < 1f && Math.Abs(an.NaFrente - Feixe.ContatoNoCorpo(an.Raio)) < 1f
				  && an.Feixes == 1 && an.Raio.Vivo && an.Raio.Canalizando,
				  $"pior desvio {an.PiorNaFrente:0.00} px, {an.NaFrente:0.0} px a frente no fim, {an.Feixes} feixe(s), vivo {an.Raio.Vivo}");
		AfirmarPj("...e o alcance devolve o que a cabeca recuou (`AndouTiles` continua sendo da mao ate a cabeca)",
				  Math.Abs(an.Raio.AndouTiles - (an.Raio.Pos - an.Raio.Cauda).Length / T) < 0.6,
				  $"andou {an.Raio.AndouTiles:0.00} tiles, mao->cabeca {(an.Raio.Pos - an.Raio.Cauda).Length / T:0.00}");

		// ---------------------------------------------------------------- (i) quem vinha sendo LEVADO e solto
		// O raio PESADO (10 tiles por segundo) pega o corpo a 6 tiles com 12 por batida e vai levando; no tique em que o
		// arrasto comeca o dano dele cai pra 5 (o `rangemod` que enfraquece no caminho, o alvo que cresce no meio do
		// raio). Na batida seguinte -- ainda longe do fim da corda, aos 10 tiles -- nao ha golpe, e sem golpe nao ha empurrao.
		int enfraqueceuEm = -1, soltouEm = -1;
		Vec2 ondeSoltou = default;
		double vidaAoSoltar = 0, andouAoSoltar = 0;
		RaioDoCorte lv = RaioDeDanoCravado(raia, DanoQueFereDoCorte, tiques: 50, receita: r => r.Velocidade = 0.5, aCadaTique: (i, alvo, raio) =>
		{
			if (enfraqueceuEm < 0 && raio.Arrastando == alvo.Id)
			{
				CravarDanoFinal(raio, alvo, DanoFracoDoCorte);
				enfraqueceuEm = i;
			}
			else if (enfraqueceuEm >= 0 && soltouEm < 0 && raio.Arrastando == 0)
			{
				soltouEm = i;
				ondeSoltou = alvo.Pos;
				vidaAoSoltar = alvo.Combate!.Corpo.Vida();
				andouAoSoltar = raio.AndouTiles;
			}
		});
		AfirmarPj("PREPARO: o raio de 12 pegou o corpo e vinha LEVANDO-o quando enfraqueceu pra 5",
				  enfraqueceuEm >= 0, $"arrasto no tique {enfraqueceuEm}");
		AfirmarPj("QUEM VINHA SENDO LEVADO E SOLTO na batida seguinte, antes do fim da corda (`Projetil.TilesDeArrasto`): sem golpe nao ha empurrao",
				  soltouEm > enfraqueceuEm && soltouEm - enfraqueceuEm <= 8 && andouAoSoltar < Projetil.TilesDeArrasto - 0.5,
				  $"enfraqueceu no tique {enfraqueceuEm}, soltou no {soltouEm}, a {andouAoSoltar:0.0} tiles da mao");
		AfirmarPj("...e dali em diante o corpo fica onde foi solto e nao perde mais vida",
				  soltouEm >= 0 && (lv.Alvo.Pos - ondeSoltou).Length < 0.01f && Math.Abs(lv.Alvo.Combate!.Corpo.Vida() - vidaAoSoltar) < 1e-9,
				  $"andou {(lv.Alvo.Pos - ondeSoltou).Length:0.00} px depois de solto, vida {vidaAoSoltar:0.###} -> {lv.Alvo.Combate!.Corpo.Vida():0.###}");

		// ---------------------------------------------------------------- (j) o raio que estoura como bola
		RaioDoCorte db = RaioDeDanoCravado(raia, DanoFracoDoCorte, tiques: 30, receita: r => r.EstouraNoImpacto = true);
		AfirmarPj("O RAIO QUE ESTOURA COMO BOLA (o Death Beam), fraco: estoura no corpo e acaba de uma vez, sem ferir",
				  !db.Raio.Vivo && db.Raio.Fim == FimDeProjetil.Acertou && Math.Abs(db.Vida) < 1e-9 && db.Relatos == 0 && db.Batidas.Count == 1,
				  $"vivo {db.Raio.Vivo}, fim {db.Raio.Fim}, vida {db.Vida:+0.###;-0.###;0}, {db.Relatos} relato(s), {db.Batidas.Count} batida(s)");

		// ---------------------------------------------------------------- (k) quem pisa no tronco de um raio fraco
		LimparTudoDaBancada();
		ServerPlayer deTronco = Forjar("FeixeDoTroncoFraco", raia, bp: 200_000);
		deTronco.Facing = Facing.East;
		Projetil tronco = RaioDaBancada(deTronco, baseDano: 1);
		for (int i = 0; i < 25; i++) UmTiqueDeArrasto();
		List<Projetil> feixes = ProjeteisDaZona(deTronco.Zone.Hash);
		ServerPlayer pisou = Forjar("PisouNoFraco", raia + new Vec2(4 * T, 0), bp: 200_000);
		CravarDanoFinal(tronco, pisou, DanoFracoDoCorte);
		double vidaDoPisou = pisou.Combate!.Corpo.Vida();
		int relatosDoPisou = RelatosNoAlvo(pisou, UmTiqueDeArrasto).Count;
		AfirmarPj("PISAR NO TRONCO DE UM RAIO FRACO: o feixe e cortado e a cabeca nova para na frente de quem pisou (`Crossed`, `objects.dm:156-171`) -- sem ferir",
				  feixes.Count == 2 && tronco.Encostado && tronco.Arrastando == 0
				  && Math.Abs(NaFrenteDe(tronco, pisou) - Feixe.ContatoNoCorpo(tronco)) < 1f
				  && Math.Abs(pisou.Combate.Corpo.Vida() - vidaDoPisou) < 1e-9 && relatosDoPisou == 0,
				  $"{feixes.Count} feixe(s), encostado {tronco.Encostado}, {NaFrenteDe(tronco, pisou):0.0} px a frente, "
				  + $"vida {vidaDoPisou:0.###} -> {pisou.Combate.Corpo.Vida():0.###}, {relatosDoPisou} relato(s)");

		// ---------------------------------------------------------------- (m) o tiro fraco nao quebra a concentracao de ninguem
		(bool Preparo, bool Caiu, FimDeProjetil Fim) emQuemCanaliza = BolaEmQuemCanaliza(raia, DanoFracoDoCorte);
		AfirmarPj("A BOLA FRACA NAO DERRUBA O RAIO DE QUEM A LEVA: o raio cai com golpe (`if(KB) stopbeaming()`, `beams.dm:73-74`), e isto nao e golpe",
				  emQuemCanaliza.Preparo && !emQuemCanaliza.Caiu && emQuemCanaliza.Fim == FimDeProjetil.Acertou,
				  $"canal de pe antes {emQuemCanaliza.Preparo}, caiu {emQuemCanaliza.Caiu}, fim da bola {emQuemCanaliza.Fim}");
		emQuemCanaliza = BolaEmQuemCanaliza(raia, DanoQueFereDoCorte);
		AfirmarPj("(controle) a MESMA bola com 12 derruba o raio dele",
				  emQuemCanaliza.Preparo && emQuemCanaliza.Caiu && emQuemCanaliza.Fim == FimDeProjetil.Acertou,
				  $"canal de pe antes {emQuemCanaliza.Preparo}, caiu {emQuemCanaliza.Caiu}, fim da bola {emQuemCanaliza.Fim}");

		// ---------------------------------------------------------------- (n) o raio que nao fere contra a guarda
		// O EMBATE DE GUARDA (as maos segurando o raio; e deste port, o DM nao tem) VEM DEPOIS DO CORTE: quem ergue a
		// guarda contra um raio que nao o feriria nao tem o que segurar. So ha disputa quando ha o que perder.
		(bool Embate, bool Plantou, double Vida, double VantagemDasMaos) naGuarda = RaioContraAGuardaSegurada(raia, fere: false);
		AfirmarPj("O RAIO QUE NAO FERE NAO VIRA EMBATE DE GUARDA: ele para na frente de quem defende, sem ferir e sem plantar ninguem numa disputa",
				  !naGuarda.Embate && naGuarda.Plantou && Math.Abs(naGuarda.Vida) < 1e-9,
				  $"embate {naGuarda.Embate}, cabeca batendo sem ferir {naGuarda.Plantou}, vida {naGuarda.Vida:+0.###;-0.###;0}");
		naGuarda = RaioContraAGuardaSegurada(raia, fere: true);
		AfirmarPj("(controle) o MESMO raio com a base que fere ATRAVES da guarda vira embate: as maos o seguram",
				  naGuarda.Embate, $"embate {naGuarda.Embate}");
		// AS MAOS SE MEDEM PELO DANO (dono, 2026-10-08; `EmbateDeKi.PoderDeSegurar`): o raio que passa do corte por
		// pouco fica ABAIXO do que elas empatam (`EmbateDeKi.DanoQueAsMaosEmpatam`), e quem o segura entra na disputa
		// na frente. Na escala de antes (o numerador da deflexao sobre 100) este mesmo raio ja nascia no teto contra elas.
		AfirmarPj("...e seguram com VANTAGEM: a delas na disputa e o que as maos empatam sobre o dano do raio (o de 12 fica abaixo disso)",
				  Math.Abs(naGuarda.VantagemDasMaos - EmbateDeKi.DanoQueAsMaosEmpatam / DanoQueFereDoCorte) < 0.02,
				  $"vantagem das maos {naGuarda.VantagemDasMaos:0.##}, esperado {EmbateDeKi.DanoQueAsMaosEmpatam / DanoQueFereDoCorte:0.##}");

		// ---------------------------------------------------------------- (z) os defeitos injetados
		// O JOGO DE ANTES: o `Acertar` sem o ramo de `objects.dm:355-357`. As reguas de cima tem que ficar VERMELHAS com ele.
		DanoDeKi.TiroFracoFereDeTeste = true;
		try
		{
			b = BolaDeDanoCravado(chao, DanoFracoDoCorte);
			AfirmarPj("(defeito injetado: o tiro fraco fere) BOLA de 5: tira vida e sai como golpe",
					  b.Vida < -0.1 && b.Relatos.Count == 1 && (Desfecho)b.Relatos[0].Desfecho == Desfecho.Acertou,
					  $"vida {b.Vida:+0.###;-0.###;0}, {b.Relatos.Count} relato(s)");

			s = BolaDeDanoCravado(chao, 0, bpDoAtirador: 500, bpDoAlvo: 500_000, comDado: true);
			AfirmarPj("(defeito injetado) FRACO CONTRA FORTE: o tiro e sorteado e o forte RASPA (um relato de esquiva, 0,5 de treino)",
					  s.Relatos.Count == 1 && (Desfecho)s.Relatos[0].Desfecho == Desfecho.Esquivou && Math.Abs(s.Pericia - 0.5) < 1e-9,
					  $"{s.Relatos.Count} relato(s), pericia +{s.Pericia:0.0}");

			furo = BolaDeDanoCravado(chao, DanoFracoDoCorte, receita: r => r.Piercer = true);
			AfirmarPj("(defeito injetado) PERFURANTE de 5: fere e ATRAVESSA o corpo",
					  furo.Vida < -0.1 && furo.Tiro.Pos.X > furo.Alvo.Pos.X + T,
					  $"vida {furo.Vida:+0.###;-0.###;0}, bola {(furo.Tiro.Pos.X - furo.Alvo.Pos.X) / T:0.0} tiles alem do corpo");

			fr = RaioDeDanoCravado(raia, DanoFracoDoCorte, tiques: 30);
			AfirmarPj("(defeito injetado) RAIO de 5: fere a cada ciclo de moer e LEVA o corpo",
					  fr.Vida < -0.1 && fr.Relatos >= 2 && fr.Andou > T && fr.MaiorIntervalo <= 8,
					  $"vida {fr.Vida:+0.###;-0.###;0}, {fr.Relatos} relato(s), andou {fr.Andou:0.0} px, intervalos ate {fr.MaiorIntervalo} tiques");
		}
		finally { DanoDeKi.TiroFracoFereDeTeste = false; }

		// A CABECA QUE NAO RECUA: o corpo entra no raio e a cabeca fica pra tras, desenhada em cima dele.
		Feixe.CabecaNaoRecuaDeTeste = true;
		try
		{
			an = RaioQueOForteAtravessa(raia);
			AfirmarPj("(defeito injetado: a cabeca nao recua) O FORTE ANDA RAIO ADENTRO e a cabeca fica EM CIMA dele entre uma batida e outra",
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
	/// devolver o numero pedido. (A chance de deflexao anda junto, ao contrario: quem precisa do dado crava a chance
	/// depois, no `Bp` do tiro, como a amostra da familia 15.)
	/// </summary>
	private static void CravarDanoFinal(Projetil p, ServerPlayer alvo, double dano)
		=> p.ModsBase *= dano / DanoFinalDe(p, alvo);

	/// <summary>
	/// O `base_damage` COM QUE O TIRO DESTE CORPO E UM GOLPE CONTRA UM IGUAL: dano final de 12 por impacto num corpo
	/// com a ficha e o poder dele mesmo, pela conta de producao (`Projetil.ModsDoTiro` e `DanoDeKi.Final`; a conta e
	/// QUADRATICA na base, porque o `Disparar` a multiplica no `mods` e o `Final` a usa de novo).
	///
	/// ============================ O RAIO DE BANCADA ERA DE COCEGAS, E COCEGAS NAO SAO MAIS GOLPE ============================
	/// As familias que medem GEOMETRIA (o arrasto, a cabeca na frente, o corte do tronco, a faisca) atiravam com
	/// `baseDano: 0.002`: um impacto que conta como golpe e nao machuca o boneco no meio da medida. Com o corte dos
	/// fracos (`DanoDeKi.CorteDoFraco`, `objects.dm:355-357`) esse impacto nao e golpe -- nao leva, nao arremessa,
	/// nao derruba o raio de ninguem e nao manda relato. O tiro dessas cenas passou a ser o de um golpe de verdade,
	/// pouco acima do corte; os corpos de bancada sao todos a mesma ficha (`Forjar`), entao "um igual" e o alvo.
	/// ========================================================================================================================
	/// </summary>
	/// <param name="deGuarda">
	/// O igual esta de GUARDA ERGUIDA: a guarda divide o dano de novo (`dmg /= 2 * log_4(...)`, `objects.dm:342-344`), e
	/// um tiro que so fere quem esta de maos abaixadas cai no corte contra as maos erguidas.
	/// </param>
	private static double BaseQueFereUmIgual(ServerPlayer atira, TipoDeProjetil tipo = TipoDeProjetil.Beam, bool deGuarda = false)
	{
		double comBaseUm = DanoDeKi.Final(Projetil.ModsDoTiro(atira.Ficha, tipo), 1, 0, atira.Ficha.expressedBP, atira.Combate!, deGuarda);
		return Math.Sqrt(DanoQueFereDoCorte / comBaseUm);
	}

	/// <summary>
	/// O `base_damage` COM QUE O TIRO DESTE CORPO E UM GOLPE CONTRA ESTE ALVO (12 por impacto), com o poder, a ficha e
	/// a guarda que o alvo tem AGORA. E o <see cref="BaseQueFereUmIgual"/> pras cenas em que os dois nao sao iguais
	/// (o fraco contra a muralha): la a razao dos poderes derruba o dano, e a base tem que subir junto.
	/// </summary>
	private static double BaseQueFere(ServerPlayer atira, ServerPlayer alvo, TipoDeProjetil tipo = TipoDeProjetil.Beam)
	{
		double comBaseUm = DanoDeKi.Final(Projetil.ModsDoTiro(atira.Ficha, tipo), 1, 0, atira.Ficha.expressedBP,
										  alvo.Combate!, alvo.Combate!.Bloqueando);
		return Math.Sqrt(DanoQueFereDoCorte / comBaseUm);
	}

	/// <summary>A MESMA RECEITA com a base de um golpe contra um igual (<see cref="BaseQueFereUmIgual"/>). Devolve a propria.</summary>
	private static ReceitaDeProjetil QueFereUmIgual(ReceitaDeProjetil r, ServerPlayer atira, bool deGuarda = false)
	{
		r.BaseDano = BaseQueFereUmIgual(atira, r.Tipo, deGuarda);
		return r;
	}

	/// <summary>A que distancia A FRENTE da cabeca deste tiro o corpo esta, pelo eixo do tiro.</summary>
	private static float NaFrenteDe(Projetil p, ServerPlayer corpo)
		=> (corpo.Pos.X - p.Pos.X) * p.Rumo.X + (corpo.Pos.Y - p.Pos.Y) * p.Rumo.Y;

	/// <summary>O que UMA bola fez a UM corpo. `Vida`, `Pericia`, `Ki` e `Andou` sao a DIFERENCA entre depois e antes.</summary>
	private readonly record struct ImpactoDoCorte(
		Projetil Tiro, ServerPlayer Atirador, ServerPlayer Alvo, double Dano, double Chance,
		List<Protocol.HitEvent> Relatos, double Vida, double Pericia, double Ki, float Andou);

	/// <summary>
	/// UMA BOLA contra um corpo parado a sete tiles (ela chega com seis de viagem, alem dos quatro em que arremessa
	/// -- `Projetil.FatorDeEmpurrao`), ate ela acabar.
	/// </summary>
	/// <param name="dano">O dano final a cravar no tiro. Zero = nao cravar: vale o que a receita e os dois corpos derem.</param>
	/// <param name="comDado">Com o sorteio de deflexao ligado (`Deflectivel`). As cenas que nao sao sobre o dado o desligam.</param>
	private ImpactoDoCorte BolaDeDanoCravado(Vec2 chao, double dano, double bpDoAtirador = 200_000, double bpDoAlvo = 200_000,
											 bool comDado = false, Action<ReceitaDeProjetil>? receita = null)
	{
		LimparTudoDaBancada();
		ServerPlayer atira = Forjar("AtiraNoCorte", chao, bp: bpDoAtirador);
		atira.Facing = Facing.East;
		ServerPlayer alvo = Forjar("AlvoDoCorte", chao + new Vec2(7 * ZoneCollision.TileSize, 0), bp: bpDoAlvo);

		var r = new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 20, Deflectivel = comDado, Nome = "bola do corte",
		};
		receita?.Invoke(r);
		Projetil p = Disparar(atira, r);
		if (dano > 0) CravarDanoFinal(p, alvo, dano);

		double final = DanoFinalDe(p, alvo), chance = ChanceDeDeflexaoDe(p, alvo);
		double vida = alvo.Combate!.Corpo.Vida(), pericia = alvo.Ficha.kidefenseskill, ki = alvo.Ficha.Ki;
		Vec2 lugar = alvo.Pos;
		List<Protocol.HitEvent> relatos = RelatosNoAlvo(alvo, () =>
		{
			for (int i = 0; i < 30 * 8 && p.Vivo; i++) UmTiqueDeArrasto();
		});
		return new ImpactoDoCorte(p, atira, alvo, final, chance, relatos, alvo.Combate.Corpo.Vida() - vida,
								  alvo.Ficha.kidefenseskill - pericia, alvo.Ficha.Ki - ki, (alvo.Pos - lugar).Length);
	}

	/// <summary>
	/// UMA BOLA PARADA (rumo nulo, como as do `Ki_Bomb`) a oito pixels de um corpo, por vinte tiques ou ate ela acabar.
	/// </summary>
	private (Projetil Mina, double Vida, double Pericia, int Relatos) MinaDeDanoCravado(Vec2 chao, double dano)
	{
		LimparTudoDaBancada();
		ServerPlayer dono = Forjar("DonoDaMinaFraca", chao, bp: 200_000);
		ServerPlayer pisa = Forjar("PisaNaMinaFraca", chao + new Vec2(6 * ZoneCollision.TileSize, 0), bp: 200_000);

		Projetil mina = Disparar(dono, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, AlcanceTiles = 20, Deflectivel = false, Nome = "mina do corte",
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
	/// UM RAIO NA MAO (como o <see cref="RaioDaBancada"/>: canalizando, sem o dado) contra um corpo parado a seis tiles,
	/// com o dano final cravado, por <paramref name="tiques"/> tiques do servidor.
	/// </summary>
	/// <param name="aCadaTique">
	/// O que a cena faz DEPOIS de cada tique e da medida dele (andar com o corpo, enfraquecer o raio): o tique, o
	/// corpo e o raio.
	/// </param>
	private RaioDoCorte RaioDeDanoCravado(Vec2 raia, double dano, int tiques, Action<int, ServerPlayer, Projetil>? aCadaTique = null,
										  Action<ReceitaDeProjetil>? receita = null)
	{
		LimparTudoDaBancada();
		ServerPlayer atira = Forjar("FeixeNoCorte", raia, bp: 200_000);
		atira.Facing = Facing.East;
		ServerPlayer alvo = Forjar("NaFrenteDoFeixe", raia + new Vec2(6 * ZoneCollision.TileSize, 0), bp: 200_000);

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
	/// O RAIO FRACO PLANTADO NUM CORPO QUE ANDA CONTRA ELE: do tique 20 ao 43 o corpo da 4 px por tique na direcao de
	/// quem atira -- tres tiles --, e a cena segue ate o tique 70 (mais de um ciclo de batida depois de ele parar).
	/// </summary>
	private RaioDoCorte RaioQueOForteAtravessa(Vec2 raia)
		=> RaioDeDanoCravado(raia, DanoFracoDoCorte, tiques: 70, aCadaTique: (i, alvo, _) =>
		{
			if (i is >= 20 and < 44) alvo.Pos += new Vec2(-4, 0);
		});

	/// <summary>
	/// UM KI WAVE DE VERDADE (`Canalizar`, a carga, o canal) CONTRA UM IGUAL DE GUARDA SEGURADA ha mais que a janela do
	/// parry (`SegurarAGuarda`), a oito tiles, por tres segundos ou ate o embate comecar. Com <paramref name="fere"/>
	/// a base sobe ate o raio ferir atraves da guarda (<see cref="BaseQueFere"/>); sem, e o Ki Wave de producao, que
	/// entre iguais faz ~2,7 com a guarda erguida. Devolve se virou embate, se a cabeca ficou batendo sem ferir em
	/// quem defende, a vida que ele perdeu e a vantagem com que as maos dele entraram na disputa (zero sem embate).
	/// </summary>
	private (bool Embate, bool Plantou, double Vida, double VantagemDasMaos) RaioContraAGuardaSegurada(Vec2 raia, bool fere)
	{
		LimparEmbatesDaBancada();
		ServerPlayer atira = Forjar("RaioNaGuarda", raia, bp: 200_000);
		atira.Facing = Facing.East;
		atira.Ficha.Ki = atira.Ficha.MaxKi;
		ServerPlayer guarda = Forjar("GuardaSegurada", raia + new Vec2(8 * ZoneCollision.TileSize, 0), bp: 200_000);
		guarda.Ficha.Ki = guarda.Ficha.MaxKi;
		SegurarAGuarda(guarda);
		double vida = guarda.Combate!.Corpo.Vida();

		ReceitaDeProjetil r = SemDeflexao();
		if (fere) r.BaseDano = BaseQueFere(atira, guarda);
		Canalizar(atira, "Ki_Wave", 10 * atira.Ficha.BaseDrain(), r);

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
	private (bool Preparo, bool Caiu, FimDeProjetil Fim) BolaEmQuemCanaliza(Vec2 raia, double dano)
	{
		LimparTudoDaBancada();
		ServerPlayer canal = Forjar("CanalNoCorte", raia + new Vec2(4 * ZoneCollision.TileSize, 0), bp: 200_000);
		canal.Facing = Facing.East;
		canal.Ficha.Ki = canal.Ficha.MaxKi;
		ServerPlayer bolista = Forjar("BolistaDoCorte", raia, bp: 200_000);
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
