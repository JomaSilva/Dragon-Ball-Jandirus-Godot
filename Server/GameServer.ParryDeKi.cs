using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ O DEFLECT DE KI -- DIVERGENCIA DECLARADA, REGRA DO DONO ============================
/// O DM NAO TEM ISTO. O parry dele e so de SOCO (`CombatMovement.dm:205`, o `Contra` do `MeleeResolver`), e o
/// `Bump` do tiro (`objects.dm:334-419`) so conhece sorteios. Aqui defletir e um GESTO: erguer a guarda no tempo.
///
/// ---- A REGRA DE HOJE (dono, 2026-10-12) ----
/// *"deflect vc tem q ser pelomenos 2x a força do inimigo pra funcionar. deflect deveria fazer com q o ataque de ki
/// do inimigo seja cancelado, entao dar deflect em um beam ou esfera ou oq for deveria cancelar o ataque na hora,
/// assim como é no sparking zero"* -- e, no mesmo dia: *"todo ataque de ki como esfera grande e beam explodem ao
/// chegarem na distancia maxima, acertando ou nao o inimigo, defletidos ou nao"*.
///
/// Entao o deflect TIRA O ATAQUE DE QUEM ATIROU, na hora:
///   * RAIO: a mao de quem atirou FECHA (o canal acaba, ele nao alimenta mais nada) e o raio inteiro some dali -- o
///     que sobra e um pedaco solto que sai de LADO, da frente de quem defletiu, e vai estourar no fim do alcance;
///   * ESFERA (a bola e a teleguiada, grandes ou nao) VOLTA PRA QUEM ATIROU -- retorno do dono do mesmo dia, sobre a
///     primeira entrega (em que ela tambem saia de lado): *"deflect de esferas de ki (apenas blast) voltam pro
///     atacante original"*. Ela passa a ser de quem defletiu (dono, poder, letalidade), na velocidade em que veio e
///     com o alcance renovado -- <see cref="RebaterBola"/>.
/// O tiro defletido nunca mais acerta quem o defletiu (`Projetil.Desviador`; e a esfera devolvida e DELE).
///
/// Ela substitui a de 2026-09-25, em que bastava ser igual ou mais forte e o raio dobrava num trecho que o atirador
/// continuava alimentando. (A bola ja voltava pra quem atirou naquela regra: pra ela, o que mudou foi a porta.)
///
/// ---- AS QUATRO PORTAS ----
///   1. O TEMPO: a mesma janela e o mesmo gatilho do parry de soco -- `CombatState.ContraPronto` (um por guarda
///      erguida, com a recarga `CombatKnobs.RecargaDoContra`) e `TempoDeGuarda &lt;= MeleeResolver.JanelaContra`.
///   2. A FORCA: o DOBRO do poder do tiro (`KiContraCorpo.PodeDefletir`: o poder expresso de quem defende contra
///      o `Projetil.Bp`, o de quem atirou congelado no disparo). Com menos, a guarda e so guarda.
///   3. O CORPO: braco ou perna inteiro pra aparar (`TemComQueAparar`) e o Ki da guarda.
///   4. O KI: qualquer um -- o `Deflectivel` da tecnica nao conta (ele e do sorteio de deflexao); so o tiro
///      FISICO fica de fora, porque pedra arremessada nao e ki.
/// Sem sorteio nenhum: com as quatro portas abertas, o deflect sai. (O SORTEIO de deflexao do `Acertar` -- a sorte,
/// e nao o gesto -- pede o mesmo dobro e termina na mesma funcao: <see cref="DefletirOAtaque"/>.)
/// ================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Pra que lado o tiro defletido sai, em graus do rumo em que vinha -- de lado, pra um lado ou pro outro (sorteado).</summary>
	public const float AnguloDoDeflect = 90f;

	/// <summary>O comprimento maximo do pedaco de raio que sai voando de um deflect, em pixels (tres tiles).</summary>
	public const float ComprimentoDoRaioDefletido = 3 * ZoneCollision.TileSize;

	/// <summary>
	/// O TIRO ENCONTROU UM DEFLECT? Chamado do `Acertar`, antes do dano e dos sorteios. Verdadeiro = o ataque foi
	/// defletido (o tiro ja saiu dali).
	/// </summary>
	private bool TentarParryDeKi(Projetil p, ServerPlayer alvo, ServerPlayer atirador)
	{
		CombatState cd = alvo.Combate!;
		if (p.Fisico || alvo.Ficha.KO || EhCadaver(alvo) || cd.Stun > 0) return false;
		if (!cd.Bloqueando || !cd.ContraPronto || cd.TempoDeGuarda > MeleeResolver.JanelaContra) return false;

		// A FORCA: o dobro do poder do tiro. (O defeito injetado e a porta de antes: igual ou mais forte.)
		double razao = KiContraCorpo.Razao(alvo.Ficha.expressedBP, p.Bp);
		if (!(KiContraCorpo.RegraDeAntesDeTeste ? razao >= 1 : KiContraCorpo.PodeDefletir(razao))) return false;

		double custoKi = alvo.Ficha.MaxKi * CombatKnobs.CustoKiDaGuarda;
		if (alvo.Ficha.Ki < custoKi || !TemComQueAparar(alvo)) return false;

		alvo.Ficha.Ki -= custoKi;
		cd.ContraPronto = false;
		cd.RecargaContra = CombatKnobs.RecargaDoContra;

		// O PONTO DO IMPACTO ANTES DE O TIRO MUDAR DE LUGAR: e ali que a faisca do deflect estoura.
		Vec2 impacto = Feixe.PontoDoImpacto(p, alvo.Pos);
		DefletirOAtaque(p, alvo, atirador);

		AnunciarGolpe(atirador, alvo, new GolpeResultado { Desfecho = Desfecho.Rebateu, Membro = "" }, nivel: 2,
					  ponto: impacto);
		GD.Print($"[server] DEFLECT DE KI: {alvo.Name} defletiu {p.Nome} de {atirador.Name}");
		return true;
	}

	/// <summary>
	/// O ATAQUE SAI DAS MAOS DE QUEM ATIROU -- ver o cabecalho. O tiro continua VIVO: a esfera volta pra quem a atirou
	/// (<see cref="RebaterBola"/>) e o raio sai de lado ate o fim do alcance, onde estoura (`ExplodirNoFimDoAlcance`).
	/// Quem defletiu sai da lista de alvos dele (`Projetil.Desviador`, lido no `Colidiu`, no corte do tronco e no dano
	/// em area do estouro).
	/// </summary>
	private void DefletirOAtaque(Projetil p, ServerPlayer alvo, ServerPlayer atirador)
	{
		p.Arrastando = 0;
		p.Encostado = false;
		p.BatendoSemFerir = 0;
		p.Desviador = alvo.Id;

		if (p.Tipo != TipoDeProjetil.Beam && !EsferaDefletidaSaiDeLadoDeTeste)
		{
			RebaterBola(p, alvo, atirador);
			return;
		}

		Vec2 rumo = Girar(p.Rumo, _rng.Next(2) == 0 ? AnguloDoDeflect : -AnguloDoDeflect);
		if (p.Tipo != TipoDeProjetil.Beam)
		{
			// (o defeito injetado: a esfera de lado, sem dono novo -- a teleguiada perdia a caca)
			p.Rumo = rumo;
			if (p.Tipo == TipoDeProjetil.Guided) p.Alvo = 0;
			return;
		}

		// O RAIO: a mao de quem atirou fecha (o ataque DELE acabou) e o raio inteiro some de onde estava -- a cauda
		// pula pra frente de quem defletiu. Sobra um pedaco solto, que sai de lado.
		if (_canais.TryGetValue(atirador.Id, out CanalDeKi? canal) && canal.Raio == p)
			FecharCanal(atirador.Id, canal, "seu ataque foi defletido!");
		p.Canalizando = false;
		p.JaDisputou = true;

		float comprimento = MathF.Max(MathF.Min(p.Comprimento, ComprimentoDoRaioDefletido), 1f);
		Vec2 deOnde = p.Pos;
		p.Cauda = deOnde;
		p.Pos = deOnde + rumo * comprimento;
		p.Rumo = rumo;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--embatekiteste`): a esfera defletida volta a sair de LADO, sem trocar de dono -- a
	/// primeira entrega de 2026-10-12, que o dono reprovou. Lido pela propria linha da regra, em
	/// <see cref="DefletirOAtaque"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool EsferaDefletidaSaiDeLadoDeTeste;

	/// <summary>
	/// A ESFERA VOLTA PRA QUEM ATIROU -- de quem a defletiu, na velocidade em que veio (`TrocarDeLado`, a metade do
	/// `Devolver` que nao e do embate: dono, poder, letalidade e rumo). O alcance e o prazo recomecam: a volta e uma
	/// viagem nova, e uma esfera que ja tinha gasto metade do alcance morreria no meio do caminho. A teleguiada passa
	/// a cacar quem a atirou.
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

	private static Vec2 Girar(Vec2 v, float graus)
	{
		float r = graus * MathF.PI / 180f, c = MathF.Cos(r), s = MathF.Sin(r);
		return new Vec2(v.X * c - v.Y * s, v.X * s + v.Y * c);
	}
}
