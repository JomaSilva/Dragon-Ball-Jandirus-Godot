namespace Jandirus.Core.Combat;

/// <summary>O que acontece quando dois ataques de ki de donos diferentes se encostam. Ver <see cref="KiContraKi"/>.</summary>
public enum FimDoChoqueDeKi : byte
{
	/// <summary>Eu sigo, ele acaba.</summary>
	EuVenco,

	/// <summary>Ele segue, eu acabo.</summary>
	EleVence,

	/// <summary>Os dois acabam ali.</summary>
	OsDoisAcabam,
}

/// <summary>
/// **KI CONTRA KI** -- dono, 2026-10-13: *"permita a colisao de quaisquer ataques de ki, beam vs esferas gigantes,
/// beams vs esferas de ki etc"*.
///
/// ============================ O QUE EXISTIA ============================
/// So RAIO contra RAIO: a disputa de dois feixes alimentados, a espera no cruzamento e o empurrao da cabeca
/// solta (`GameServer.EmbateDeKi.cs`, `GameServer.Feixe.cs`). Uma BOLA atravessava tudo -- outra bola, a cabeca
/// de um Kamehameha, o tronco dele, uma Genkidama -- sem nada acontecer.
///
/// ============================ O QUE O DM FAZ ============================
/// O `Bump` de um ataque em outro (`Ki/tools/objects.dm:504-555`): donos diferentes, e a forca dos dois medida
/// por `BPModulus(BP*mods*basedamage, R.BP*R.mods*R.basedamage)` -- o mesmo numero que a disputa de feixes ja
/// usava aqui (<see cref="Projetil.PoderDeEmbate"/>):
///   * bola contra bola (`:538-554`): acima de 1,3 o outro some; abaixo de 0,7 some eu; no meio, OS DOIS somem;
///   * o tronco de um raio apaga o mais fraco que o cruza (`Crossed(obj/attack)`, `:173-185`).
///
/// ============================ O QUE ESTE PORT FAZ, E ONDE DIVERGE ============================
///   * BOLA x BOLA: a regra do DM, na letra (<see cref="Decidir"/>). A bola que acaba ESTOURA; a esfera grande
///     que acaba faz o estouro grande dela, com cratera e dano em area.
///   * RAIO x BOLA COMUM: a bola precisa de 1,3 vezes o poder do raio pra PARA-LO (e segue); com menos, ela estoura
///     na cabeca dele e o raio continua. **DIVERGENCIA DECLARADA**: no DM a faixa do meio (0,7 a 1,3) apagava os
///     dois -- uma bola de dedo de um igual desfazia um Kamehameha, e o raio (que planta o corpo e cobra por
///     segundo) perderia sempre pra rajada. O botao <see cref="RaioCedeNoEmpateDeTeste"/> devolve a regra de la.
///   * BOLA NO TRONCO de um raio: a mais fraca (ou igual) estoura nele; a 1,3 vezes mais forte atravessa.
///   * **RAIO ALIMENTADO x ESFERA GRANDE, e esfera grande x esfera grande, DE FRENTE: DISPUTA** -- o mesmo cabo
///     de guerra de dois feixes (letras, medidor, o encontro que caminha), com a esfera no lugar de um deles.
///     Quem a jogou fica plantado empurrando. A esfera vencida VOLTA pra quem a jogou e estoura nele; a
///     vencedora engole o raio e segue. NOVO: no DM a disputa exige `WaveAttack` dos dois lados
///     (`BeamClash.dm:70`). Quando nao da pra disputar (o raio ja solto, um dono preso noutro embate, a esfera
///     parada), vale a regra da forca.
/// =============================================================================================
/// </summary>
public static class KiContraKi
{
	/// <summary>Acima desta razao de forca, o outro acaba e eu sigo: `beam_strg_mod > 1.3` (`objects.dm:539`).</summary>
	public const double VezesPraVencer = 1.3;

	/// <summary>Abaixo desta razao, acabo eu: `beam_strg_mod &lt; 0.7` (`objects.dm:544`).</summary>
	public const double VezesPraPerder = 0.7;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): os ataques de ki voltam a se atravessar (so raio contra raio se
	/// enxerga) -- a v0.2.0. Lido pela propria porta do choque (`GameServer.AndarProjetil`). Falso em jogo, sempre.
	/// </summary>
	public static bool SemChoqueDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): na faixa do empate o raio acaba junto com a bola -- a regra do DM,
	/// em que a bola de um igual desfaz o raio. Lido pelo <see cref="Decidir"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool RaioCedeNoEmpateDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): o raio alimentado e a esfera grande nao disputam -- encostam e a
	/// forca decide na hora. Lido pela porta da disputa (`GameServer.TentarEmbateComEsfera`). Falso em jogo, sempre.
	/// </summary>
	public static bool EsferaNaoDisputaDeTeste;

	/// <summary>
	/// ATE ONDE UMA ESFERA PODE SER EMPURRADA DE VOLTA numa disputa, em tiles: ate quem a jogou, se ele esta atras
	/// dela, com este teto (a esfera lancada de muito longe nao atravessa o mapa de re).
	/// </summary>
	public const double TilesDeRecuoDaEsfera = 14;

	/// <summary>O recuo da esfera cujo dono NAO esta atras dela (ele andou, ela e teleguiada): um trecho fixo.</summary>
	public const double TilesDeRecuoSemDonoAtras = 6;

	/// <summary>
	/// O RAIO DE TOQUE de um ataque, em pixels: a bola pelo tamanho DESENHADO (o raio de impacto vezes a escala --
	/// uma Genkidama encosta de longe), o raio pela frente da cabeca dele, com o piso do raio de impacto.
	/// </summary>
	public static float RaioDeToque(Projetil p) =>
		p.Tipo == TipoDeProjetil.Beam
			? MathF.Max(Feixe.AlcanceDaCabeca(p), Projetil.RaioDeImpacto)
			: Projetil.RaioDeImpacto * (float)Math.Max(p.EscalaVisual, 1);

	/// <summary>
	/// QUEM ACABA quando o MEU ataque encosta no DELE, pela razao das duas forcas (`Projetil.PoderDeEmbate`).
	/// </summary>
	/// <param name="souRaio">O meu ataque e um raio (a cabeca dele).</param>
	/// <param name="eleERaio">O dele e um raio (a cabeca, ou o tronco).</param>
	public static FimDoChoqueDeKi Decidir(double meuPoder, double poderDele, bool souRaio, bool eleERaio)
	{
		double razao = poderDele <= 0 ? double.PositiveInfinity : Math.Max(meuPoder, 0) / poderDele;
		if (razao > VezesPraVencer) return FimDoChoqueDeKi.EuVenco;
		if (razao < VezesPraPerder) return FimDoChoqueDeKi.EleVence;

		// A FAIXA DO EMPATE. Entre duas bolas (ou duas cabecas soltas), os dois acabam; com um raio de um lado so,
		// quem cede e a bola -- ver o cabecalho.
		if (!RaioCedeNoEmpateDeTeste && souRaio != eleERaio)
			return souRaio ? FimDoChoqueDeKi.EuVenco : FimDoChoqueDeKi.EleVence;
		return FimDoChoqueDeKi.OsDoisAcabam;
	}
}
