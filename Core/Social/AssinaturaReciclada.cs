namespace Jandirus.Core.Social;

/// <summary>
/// ============================ A FICHA QUE E DE OUTRA PESSOA -- pedido do dono, 2026-10-19 ============================
/// *"personagens q reencarnam no jogo n perdem a intimidade com os personagens q ele conhecia, personagens reencarnados ou
/// deletados deveriam resetar suas relacoes da aba people"*.
///
/// A ficha de quem eu conheco (<see cref="Conhecido"/>) e chaveada pela ASSINATURA da pessoa, e a assinatura do port e
/// `hash(conta, slot)` (`ServerPlayer.AssinaturaDe`): ela nao muda enquanto o personagem existe -- e VOLTA IGUAL quando a vaga
/// ganha outro personagem. Reencarnar (o Enma apaga e o jogador recria NA MESMA VAGA, `Boot.AoSerDespedido`) e apagar e criar
/// de novo davam ao personagem novo o convivio, a declaracao, a historia e o "desde quando" que os OUTROS tinham com o antigo
/// -- e a raiva, o filho, a Mente Implacavel e a Transmissao liam isso como se fosse dele. A ficha do proprio personagem novo ja
/// nascia vazia (`CreateChar` monta uma do zero); quem nao esquecia eram os outros.
///
/// Este arquivo e a pergunta pura que separa a ficha de AGORA da ficha de quem saiu da vaga. O servidor a faz no boot, pras
/// fichas que ja estavam nos saves antes deste conserto; na hora em que a vaga perde ou ganha um personagem ele nem precisa
/// perguntar -- ai TODA ficha daquela assinatura e de outra pessoa (`GameServer.ConvivioReciclado.cs`).
/// ======================================================================================================================
/// </summary>
public static class AssinaturaReciclada
{
	/// <summary>
	/// QUANDO O "DESDE QUANDO" PASSOU A SER GRAVADO EM TODA FICHA NOVA. O <see cref="Conhecido.DesdeMs"/> entrou em 2026-10-10 a
	/// noite (o revamp da aba People), e desde entao a unica porta que cria uma ficha -- o <see cref="Convivio.Fotografar"/> -- o
	/// escreve. A data tem UM DIA de folga (2026-10-12 00:00 UTC): um servidor atualizado com atraso ainda criou fichas sem ele.
	/// </summary>
	public const long DesdeQuandoGravadoDesdeMs = 1_791_763_200_000;

	/// <summary>
	/// ESTA FICHA E DE OUTRA PESSOA? -- de quem ocupava a vaga antes do personagem de agora, que nasceu em
	/// <paramref name="nascidoEm"/> (`CharacterSave.CriadoEm`; nulo = a vaga esta VAZIA).
	///
	///   * a vaga esta vazia: o personagem foi apagado, absorvido ou reencarnou e ainda nao foi recriado -- ninguem carrega mais a
	///     assinatura;
	///   * eu o conheco DESDE ANTES de ele nascer (`DesdeMs` menor que o nascimento): a ficha comecou com quem estava ali antes;
	///   * a ficha nao sabe desde quando (`DesdeMs` zero, de antes de 2026-10-10) e ele nasceu DEPOIS que todo servidor passou a
	///     gravar o "desde quando": entao ela tambem e de antes dele.
	///
	/// O personagem antigo (nascimento zero, de antes do campo) nunca e julgado pelas duas ultimas: sem a data dele nao ha como
	/// dizer quem veio primeiro, e na duvida a ficha fica.
	/// </summary>
	public static bool EDeOutraPessoa(Conhecido c, long? nascidoEm) =>
		nascidoEm is not { } nasceu
		|| (nasceu > 0 && c.DesdeMs > 0 && c.DesdeMs < nasceu)
		|| (nasceu >= DesdeQuandoGravadoDesdeMs && c.DesdeMs == 0);
}
