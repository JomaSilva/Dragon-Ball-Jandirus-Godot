using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ O PARRY CONTRA KI (dono, 2026-09-25) -- DIVERGENCIA DECLARADA ============================
/// O pedido: *"caso o jogador seja igual ou mais forte que o inimigo, e o inimigo use um ataque de ki, qualquer
/// um que seja, ao dar parry no ataque de ki, ao inves de so ser arrastado e levar parte do dano como seria com o
/// block comum, voce daria um deflect no ki, assim como no Sparking Zero e no Raging Blast 2: um beam que vem
/// bater em voce, com o parry, desvia pra um dos lados; uma bola ou esfera de ki volta pro jogador que atirou"*.
///
/// O DM NAO TEM ISTO. O parry dele e so de SOCO -- o `if(block_hold_time && M.block_hold_time <= 2 + dash_cool)
/// hit = 1` do `CombatMovement.dm:205`, que este port ja leu certo (o `Contra` do `MeleeResolver`) --, e o
/// `Bump` do tiro (`objects.dm:334-419`) so conhece sorteios: a guarda DOBRA a chance de deflexao e divide o dano
/// de novo, e um em cinco dos sorteios que acertam "reflete" (a bola anda na direcao pra onde o DEFENSOR olha,
/// sem trocar de dono). Aqui a devolucao e um GESTO, e nao um dado: acertar o tempo da guarda.
///
/// ---- AS QUATRO PORTAS, E DE ONDE CADA UMA VEM ----
///   1. O TEMPO: a mesma janela e o mesmo gatilho do parry de soco -- `CombatState.ContraPronto` (um por guarda
///      erguida, com a recarga `CombatKnobs.RecargaDoContra`) e `TempoDeGuarda <= MeleeResolver.JanelaContra`. E o
///      mesmo botao e o mesmo gesto; duas janelas diferentes seriam duas regras pra uma mao so.
///   2. A FORCA: *"igual ou mais forte"* -- o BP expresso de quem defende contra o `Bp` do tiro, que e o BP de
///      quem atirou congelado no disparo (`Projetil.Bp`). Mais fraco, o parry vira o bloqueio de sempre.
///   3. O CORPO: braco ou perna inteiro pra aparar (`TemComQueAparar`, a pergunta do `EscolherGuarda`) e o Ki da
///      guarda (`CombatKnobs.CustoKiDaGuarda`) -- as duas coisas sem as quais a guarda do soco cai.
///   4. O KI: *"qualquer um"* -- entao o `Deflectivel` da tecnica NAO conta (ele e do sorteio de deflexao); so o
///      tiro FISICO fica de fora, porque pedra arremessada nao e ki.
/// Sem sorteio nenhum: com as quatro portas abertas, o parry sai.
///
/// ---- O QUE ACONTECE ----
///   * BOLA (blast, teleguiada): troca de lado (`TrocarDeLado`, a metade do `Devolver` que nao e do embate) e
///     volta NA VELOCIDADE EM QUE VEIO, com o alcance e o prazo renovados -- a volta e uma viagem inteira. A
///     teleguiada passa a cacar quem a atirou.
///   * RAIO: a cabeca fica PLANTADA na frente de quem defendeu e a energia sai de la num RAMO, pro lado
///     (sorteado), com a cauda presa na DOBRA. Enquanto o atirador canaliza e o defensor segura a
///     guarda, o ramo CRESCE (o atirador continua alimentando -- e a curva do Sparking Zero); cada ciclo de moer
///     (0,2 s) cobra o Ki da guarda, como cada soco aparado cobra. Baixou a guarda, saiu da frente, caiu ou ficou
///     sem Ki: o desvio acaba, o ramo voa solto com o comprimento que tem, e a cabeca volta a ser um raio comum.
///
/// ---- A DOBRA E NA FRENTE DE QUEM DESVIOU, E O RAIO SAI DE LADO (dono, 2026-10-08) ----
/// A primeira versao punha a cauda do ramo ALEM do corpo de quem desviou, a 45 graus: o raio batia nele pela
/// frente e a energia reaparecia nas costas. O dono viu no trailer: *"o deflect ele deveria ao bater no jogador
/// e o jogador dar o deflect o beam dar curva pro lado e nao criar um novo beam atras do jogador"*. Entao:
///   * o ramo nasce na cabeca PLANTADA do raio (`Feixe.DobraDoDesvio`) -- o raio dobra onde bate, e o cliente
///     desenha os dois trechos como UMA fita em curva (`ProjetilState.Dobra`);
///   * e sai a 90 graus. Com a dobra na frente do corpo, um desvio de 45 passaria a 17 px do centro de quem
///     desviou -- por cima do sprite dele, que tem 16 de meia largura. De lado ele passa a um contato inteiro
///     (24 px num raio comum) e a leitura e a que foi pedida: bateu, virou pro lado.
/// ================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O angulo do ramo de um raio desviado, em graus -- pra um lado ou pro outro, sorteado.</summary>
	public const float AnguloDoDesvio = 90f;

	/// <summary>
	/// O TIRO ENCONTROU UM PARRY? Chamado do `Acertar`, antes do dano e dos sorteios de deflexao. Devolve
	/// verdadeiro quando o parry aconteceu -- e ai o tiro ja foi devolvido (bola) ou desviado (raio).
	/// </summary>
	private bool TentarParryDeKi(Projetil p, ServerPlayer alvo, ServerPlayer atirador)
	{
		CombatState cd = alvo.Combate!;
		if (p.Fisico || alvo.Ficha.KO || alvo.Ficha.dead || cd.Stun > 0) return false;
		if (!cd.Bloqueando || !cd.ContraPronto || cd.TempoDeGuarda > MeleeResolver.JanelaContra) return false;
		if (alvo.Ficha.expressedBP < p.Bp) return false;
		double custoKi = alvo.Ficha.MaxKi * CombatKnobs.CustoKiDaGuarda;
		if (alvo.Ficha.Ki < custoKi || !TemComQueAparar(alvo)) return false;

		alvo.Ficha.Ki -= custoKi;
		cd.ContraPronto = false;
		cd.RecargaContra = CombatKnobs.RecargaDoContra;

		// O PONTO DO IMPACTO ANTES DE O TIRO MUDAR DE LUGAR: e ali que a faisca do parry estoura.
		Vec2 impacto = Feixe.PontoDoImpacto(p, alvo.Pos);
		if (p.Tipo == TipoDeProjetil.Beam) DesviarRaio(p, alvo);
		else RebaterBola(p, alvo, atirador);

		AnunciarGolpe(atirador, alvo, new GolpeResultado { Desfecho = Desfecho.Rebateu, Membro = "" }, nivel: 2,
					  ponto: impacto);
		GD.Print($"[server] PARRY DE KI: {alvo.Name} {(p.Tipo == TipoDeProjetil.Beam ? "desviou" : "devolveu")} {p.Nome} de {atirador.Name}");
		return true;
	}

	/// <summary>
	/// A BOLA VOLTA PRA QUEM ATIROU -- do defensor, na velocidade em que veio. O alcance e o prazo recomecam:
	/// a volta e uma viagem nova, e uma bola que ja tinha gasto metade do alcance morreria no meio do caminho.
	/// </summary>
	private void RebaterBola(Projetil p, ServerPlayer defensor, ServerPlayer atirador)
	{
		TrocarDeLado(p, defensor, atirador);
		p.Distancia = p.MaxDistancia;
		p.VidaRestante = Math.Max(p.VidaRestante, Projetil.SegundosDeBurnout);
		if (p.Tipo == TipoDeProjetil.Guided)
		{
			p.Alvo = atirador.Id;
			p.EsperaDeCaca = 0;
		}
	}

	/// <summary>
	/// O RAIO DESVIA PRO LADO: a cabeca fica plantada na frente do defensor e nasce o RAMO, alimentado pelo
	/// mesmo canal. O ramo e um feixe de verdade (nasce pela lista da zona, pelo teto de tiros e pelo `Nasceu`
	/// no fio, como a parte de la de um corte); se a zona nao tiver vaga, ele nao existe -- a energia so para
	/// no parry, que e a unica saida em que o teto de tiros nao vira teto de corpos.
	/// </summary>
	private void DesviarRaio(Projetil p, ServerPlayer defensor)
	{
		PlantarACabecaNaFrenteDe(p, defensor);
		p.Encostado = true;
		p.AteMoerDeNovo = Projetil.SegundosPorCicloDeBeam;
		p.Arrastando = 0;
		p.DesviadoPor = defensor.Id;

		ulong zona = defensor.Zone.Hash;
		if (!_projeteis.TryGetValue(zona, out List<Projetil>? lista)) return;
		if (lista.Count >= MaxProjeteisPorZona || _projeteisVivos >= MaxProjeteisNoMundo) return;

		Vec2 rumo = Girar(p.Rumo, _rng.Next(2) == 0 ? AnguloDoDesvio : -AnguloDoDesvio);
		// A CAUDA DO RAMO E A DOBRA: a cabeca plantada do pai, na FRENTE de quem desviou -- ver o cabecalho e
		// `Feixe.DobraDoDesvio`. (O defeito injetado e o lugar de antes: alem do corpo dele, no rumo do desvio.)
		Vec2 ancora = Feixe.RamoNasceAlemDeTeste
			? defensor.Pos + rumo * (Feixe.MeioCorpo + Feixe.AlcanceDaCabeca(p))
			: Feixe.DobraDoDesvio(p);
		var ramo = new Projetil
		{
			Id = _proximoProjetil++,
			Dono = p.Dono, Tipo = p.Tipo, Pos = ancora + rumo, Cauda = ancora, Rumo = rumo,
			Distancia = p.Distancia, MaxDistancia = p.MaxDistancia, RangeMod = p.RangeMod,
			ModsBase = p.ModsBase, Bp = p.Bp, MultDeOnda = p.MultDeOnda, BaseDano = p.BaseDano, MaxDano = p.MaxDano,
			Letal = p.Letal, Deflectivel = p.Deflectivel, Piercer = p.Piercer, Fisico = p.Fisico,
			Paralisia = p.Paralisia, Empurra = p.Empurra, Altitude = p.Altitude, EstouraNoImpacto = p.EstouraNoImpacto,
			SegundosPorTile = p.SegundosPorTile, Pressa = p.Pressa, Acumulado = p.Acumulado, VidaRestante = p.VidaRestante,
			Canalizando = false, JaDisputou = true,
			Nome = $"{p.Nome} (desviado)", Arte = p.Arte, EscalaVisual = p.EscalaVisual, Invisivel = p.Invisivel,
			AlimentadoPor = p.Id, Desviador = defensor.Id, PontoDoDesvio = ancora,
		};
		lista.Add(ramo);
		_projeteisVivos++;
		AnunciarProjetil(zona, Protocol.ProjetilSub.Nasceu, ramo);
	}

	/// <summary>
	/// O CICLO DE MOER DE UM RAIO DESVIADO -- chamado do `Acertar` enquanto `DesviadoPor` estiver de pe.
	/// Verdadeiro = o desvio continua (a cabeca foi replantada, sem dano); falso = acabou, e o `Acertar`
	/// segue com o golpe comum neste mesmo corpo (quem baixou a guarda leva o raio).
	/// </summary>
	private bool SustentarDesvio(Projetil p, ServerPlayer alvo)
	{
		CombatState cd = alvo.Combate!;
		double custoKi = alvo.Ficha.MaxKi * CombatKnobs.CustoKiDaGuarda;
		bool segura = p.DesviadoPor == alvo.Id && cd.Bloqueando && !alvo.Ficha.KO && !alvo.Ficha.dead
					  && cd.Stun <= 0 && alvo.Ficha.Ki >= custoKi && TemComQueAparar(alvo);
		if (!segura)
		{
			p.DesviadoPor = 0;   // o ramo le isto no tique dele e passa a voar solto
			return false;
		}

		alvo.Ficha.Ki -= custoKi;
		PlantarACabecaNaFrenteDe(p, alvo);
		p.Encostado = true;
		p.AteMoerDeNovo = Projetil.SegundosPorCicloDeBeam;
		p.Arrastando = 0;
		return true;
	}

	/// <summary>
	/// O PAI QUE AINDA ALIMENTA ESTE RAMO, ou nulo. So enquanto o pai existir, estiver sendo canalizado e
	/// continuar desviado pelo MESMO corpo. Na primeira resposta "nao" o laco se desfaz de vez (`AlimentadoPor = 0`)
	/// e o ramo passa a ser um feixe solto -- nao ha volta: um desvio novo e um parry novo, com ramo novo.
	/// </summary>
	private static Projetil? PaiQueAlimenta(Projetil ramo, List<Projetil> lista)
	{
		foreach (Projetil q in lista)
		{
			if (q.Id != ramo.AlimentadoPor) continue;
			if (q.Vivo && q.Canalizando && !q.Esvaziando && q.DesviadoPor == ramo.Desviador) return q;
			break;
		}
		ramo.AlimentadoPor = 0;
		return null;
	}

	/// <summary>
	/// A CAUDA DO RAMO FICA NA DOBRA, E A DOBRA ANDA COM A CABECA DO PAI. Quem desvia pode recuar de guarda
	/// erguida: a cada ciclo de moer a cabeca do pai e replantada na frente dele (`SustentarDesvio`), e a dobra
	/// vai junto. O ramo INTEIRO se desloca o mesmo tanto -- mover so a cauda entortaria o feixe (o rumo dele
	/// deixaria de ser cabeca menos cauda, que e de onde o cliente tira a direcao do desenho).
	/// </summary>
	private static void PrenderNaDobra(Projetil ramo, Projetil pai)
	{
		if (!Feixe.RamoNasceAlemDeTeste)
		{
			Vec2 dobra = Feixe.DobraDoDesvio(pai);
			ramo.Pos += dobra - ramo.PontoDoDesvio;
			ramo.PontoDoDesvio = dobra;
		}
		ramo.Cauda = ramo.PontoDoDesvio;
	}

	private static Vec2 Girar(Vec2 v, float graus)
	{
		float r = graus * MathF.PI / 180f, c = MathF.Cos(r), s = MathF.Sin(r);
		return new Vec2(v.X * c - v.Y * s, v.X * s + v.Y * c);
	}
}
