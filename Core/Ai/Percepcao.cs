using Jandirus.Core.World;

namespace Jandirus.Core.Ai;

/// <summary>
/// O MUNDO COMO ELE CABERIA NUMA TELA -- o que o NPC teria visto se tivesse olhos.
///
/// ============================ A REGRA DE ADMISSAO, E ELA E O ORCAMENTO ============================
/// **So entra aqui o que ja foi calculado por outro motivo.** Este struct e montado a cada tique,
/// por corpo; um campo novo que exija uma varredura de zona custa essa varredura 30 vezes por
/// segundo vezes o numero de NPCs, e o custo mora fora de qualquer medicao existente -- e
/// exatamente a forma do defeito do "cenario por pedaco", que so apareceu quando alguem cronometrou
/// o quadro certo.
///
/// Tudo o que esta aqui hoje ja existia: posicao, altitude, `Ki`, `HP`, `expressedBP` e `Voando`
/// sao campos do `ServerPlayer`/`Fighter`, e o alvo ja era escolhido pelo `PresaDaFera`. Nenhum
/// laco novo nasceu por causa desta camada.
/// ==============================================================================================
///
/// ============================ E NAO HA CLARIVIDENCIA AQUI DENTRO ============================
/// Note o que NAO ha: `alvo.Combate.Recarga`, `alvo.AtaqueAte`, `alvo.Combate.Bloqueando`, a lista
/// de skills do outro. Sao numeros que o servidor tem na mao e que um jogador nao ve -- ler
/// qualquer um deles daria um NPC que aparra ANTES do soco sair, e essa e a diferenca entre "ele e
/// bom" e "ele trapaceia". O que a IA sabe do ritmo do oponente ela aprende APANHANDO (ver
/// <see cref="Cerebro.ViuUmGolpe"/>), com atraso e com erro.
///
/// O <see cref="PoderDoAlvo"/> e a excecao consciente, e ela e legitima: e o `expressedBP`, o
/// numero que um scouter mostra e que o DM le cru na decisao da IA
/// (`target.expressedBP >= expressedBP * 1.5`, `NPCAI.dm:517`).
/// ========================================================================================
/// </summary>
public readonly struct Percepcao
{
	// --- eu ---
	public Vec2 Minha { get; init; }
	public float MinhaAltitude { get; init; }
	public bool EstouVoando { get; init; }

	/// <summary>Fracao de vida (0..1). E o `HP/100` que o `Cerebro` sempre recebeu.</summary>
	public double VidaFrac { get; init; }

	/// <summary>Fracao do tanque (0..1). Pode passar de 1: sobrecarga de Ki existe.</summary>
	public double KiFrac { get; init; }

	/// <summary>
	/// O Ki ABSOLUTO. Nao e redundante com <see cref="KiFrac"/>: o limiar que derruba do ceu
	/// (`Voo.KiQueDerruba`) e absoluto -- 5 de Ki, e nao 5% dele.
	/// </summary>
	public double Ki { get; init; }

	/// <summary>Fracao do folego (0..1). E o gate da carga: sem folego a carga PARA sozinha.</summary>
	public double FolegoFrac { get; init; }

	public bool Caido { get; init; }

	/// <summary>Atordoado agora (o `stagger` do DM). Guardar atordoado e a metade defensiva do `npc_defensive_check`.</summary>
	public bool Atordoado { get; init; }

	/// <summary>O `expressedBP` deste corpo -- o que o outro leria num scouter.</summary>
	public double MeuPoder { get; init; }

	// --- o alvo ---
	public bool TemAlvo { get; init; }
	public Vec2 DoAlvo { get; init; }
	public float AltitudeDoAlvo { get; init; }
	public bool AlvoVoando { get; init; }
	public bool AlvoCaido { get; init; }
	public double VidaDoAlvo { get; init; }
	public double PoderDoAlvo { get; init; }

	/// <summary>
	/// O ID DO ALVO -- pra MARCAR. E o mesmo numero que o `C2S.Alvo` do jogador carrega quando ele
	/// clica em alguem, e e assim que um ataque de longe vai saber pra onde ir: o alvo marcado
	/// (`GameServer.Marcado`) ja e a mira oficial do jogo, com validade, zona e regra de altura.
	///
	/// Zero quando nao ha alvo -- e zero, no funil de mira, quer dizer "limpa a mira".
	/// </summary>
	public int IdDoAlvo { get; init; }

	/// <summary>
	/// O ALVO ESTA ANDANDO? O maior fator do risco de errar de longe (ver <see cref="Arsenal.RiscoDeErrar"/>).
	/// E o `ServerPlayer.Moving`, que o passo ja escreve todo tique -- campo velho, custo zero.
	/// </summary>
	public bool AlvoSeMovendo { get; init; }

	/// <summary>
	/// TEM PAREDE ENTRE NOS? **Falso quando ninguem perguntou**, e isso e uma decisao e nao um
	/// descuido.
	///
	/// ============================ ELA E CARA, ENTAO ELA E PREGUICOSA -- E FALHA FECHADA ============================
	/// Responder isto e varrer o segmento ate o alvo meio tile por vez (`ZoneCollision.PathBlocked`).
	/// Posta na montagem da percepcao sem condicao, seria uma varredura 30 vezes por segundo por
	/// corpo, e ela apareceria como "o servidor ficou 3 ms mais caro com 20 NPCs" -- o custo que
	/// mora fora de qualquer medicao, que e a forma exata do defeito do "cenario por pedaco".
	///
	/// Entao o servidor so a traca quando <see cref="Capacidades.DeLonge"/> tem alguma coisa dentro
	/// -- ou seja, HOJE NUNCA. E o default e `false` = "nao sei" = **nao atira**: se um dia alguem
	/// esquecer de ligar a pergunta, o defeito e um NPC timido que nao usa o raio, e nunca um NPC
	/// que atravessa parede com ele. A direcao do erro foi escolhida.
	/// ==========================================================================================================
	/// </summary>
	public bool LinhaLivre { get; init; }

	// =====================================================================
	// DERIVADOS -- funcoes puras dos campos acima, e por isso nao sao campos
	// =====================================================================

	public Vec2 ParaOAlvo => DoAlvo - Minha;
	public float Distancia => TemAlvo ? ParaOAlvo.Length : float.MaxValue;

	public Vec2 Direcao
	{
		get
		{
			Vec2 d = ParaOAlvo;
			return d.LengthSquared > 1e-6f ? d.Normalized() : Vec2.Zero;
		}
	}

	/// <summary>Em que faixa de altura eu estou (0 = chao). Ver <see cref="Voo.Andar"/>.</summary>
	public int MeuAndar => Voo.Andar(MinhaAltitude);
	public int AndarDoAlvo => Voo.Andar(AltitudeDoAlvo);

	/// <summary>
	/// EU ENCOSTO NELE? A regra e ASSIMETRICA de proposito (`Voo.PodeAcertar`): quem paira no andar
	/// 1 bate em quem esta no chao **sem poder levar de volta**. E a unica vantagem mecanica que
	/// voar da numa briga, e e por ela que a receita de pairar rasante existe.
	/// </summary>
	public bool AlcancoPelaAltura => Voo.PodeAcertar(MeuAndar, AndarDoAlvo);

	/// <summary>E ELE ME ENCOSTA? A outra metade da assimetria.</summary>
	public bool EleMeAlcanca => Voo.PodeAcertar(AndarDoAlvo, MeuAndar);

	/// <summary>Quanto ele e mais forte que eu. 1 = parelho. E o `power_ratio` do `npc_combat_action`.</summary>
	public double RazaoDePoder =>
		MeuPoder > 0 && PoderDoAlvo > 0 ? PoderDoAlvo / MeuPoder : 1;
}
