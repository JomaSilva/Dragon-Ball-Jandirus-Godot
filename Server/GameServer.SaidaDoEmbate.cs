namespace Jandirus.Server;

/// <summary>
/// SAIR DO JOGO NO MEIO DE UM ZANZOCLASH -- o que vai pro disco, e o que sobra pra quem fica.
///
/// ============================ O QUE FOI MEDIDO (2026-10-11, `--saidaembateteste`, o `Drop` de producao) ============================
/// O `Drop` gravava ANTES de soltar do embate, e soltava pela cena de fecho inteira:
///
///   * quem saia ia pro disco OCULTO (`Ficha.isconcealed`, que o embate liga nos dois). Nada no login desfazia:
///     ele voltava com a ficha dizendo "oculto" e o servidor sem o listar como invisivel -- todo mundo o via, e
///     a conta de poder travava o poder expresso dele em 5 (BP 32,5 -> expresso 5) com a recarga de Ki pela
///     metade, ate ele apertar o Ocultar Poder por conta propria;
///   * o golpe de saida ACONTECIA (o `Terminar` so desiste quando um dos dois ja saiu de `_players`, e o `Drop`
///     solta antes de tirar): quem ficava e estava vencendo era teleportado pra cima de quem saia e batia num
///     corpo que ia embora; quem saia vencendo era recolocado nas costas de quem ficava e batia nele na saida.
///     O comentario do menu de pausa dizia o contrario ("deslogar CANCELA a pancada").
///
/// ============================ A REGRA, E DE ONDE ELA VEM ============================
/// QUEM SAI DO JOGO DESFAZ O EMBATE SEM VENCEDOR, E ANTES DO SAVE. E o que o original faz: o laco do
/// `ZanzoClash` do DM para quando o rival some (`attack cmn.dm:22`, com o comentario "o rival podia ser
/// deletado no meio do clash"), os dois voltam a aparecer e mais nada acontece -- la nem existe golpe de
/// saida, que e deste port. E o que o proprio tique daqui ja dizia ("alguem ... saiu do mundo no meio: o
/// embate acaba onde esta") e o que o menu de pausa ja afirmava. Vale pro logout, pra queda e pra parada do
/// servidor, que passam todos pelo `Drop`.
///
/// E O OCULTO NAO ATRAVESSA O SAVE: no DM ele e `tmp` (`Power Control.dm:6`). Aqui a `Fighter` vai inteira pro
/// disco, entao o login o apaga (<see cref="AcordarVisivel"/>) -- o que cura tambem o save que JA estava
/// assim, e o que o save periodico grava quando cai no meio de um embate e o processo morre em seguida.
/// =======================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancadas `--saidaembateteste` e `--paradateste`): a saida de antes de 2026-10-11 -- quem
	/// sai e gravado PRIMEIRO (oculto e travado) e so entao o embate solta, com a cena de fecho inteira
	/// (vencedor recolocado, golpe de saida). Lido pela propria linha do conserto, em
	/// <see cref="SairDoEmbateAoSairDoJogo"/>.
	/// </summary>
	internal bool SaidaDoEmbateDeAntesDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--saidaembateteste`): o login volta a NAO apagar o oculto que veio do disco.
	/// Lido pela propria linha do conserto, em <see cref="AcordarVisivel"/>.
	/// </summary>
	internal bool LoginNaoCuraOOcultoDeTeste;

	/// <summary>
	/// ESTE JOGADOR ESTA SAINDO DO JOGO: se ele esta num ZanzoClash, o embate se desfaz agora, sem vencedor.
	/// Chamado pelo `Drop`, ANTES do `Persistir` -- o `Terminar` devolve a visibilidade e o controle aos DOIS, e
	/// e esse corpo, ja solto, que vai pro disco.
	/// </summary>
	private void SairDoEmbateAoSairDoJogo(ServerPlayer pl)
	{
		if (!_emEmbate.TryGetValue(pl.Id, out Embate? e)) return;

		if (SaidaDoEmbateDeAntesDeTeste)
		{
			Persistir(pl);                                  // a ordem antiga: grava primeiro, ainda oculto e travado...
			Terminar(e);                                    // ...e so entao solta, com a cena de fecho
			_embates.Remove(e);
			if (pl.Peer != null) _contas.Remove(pl.Peer);   // pro `Persistir` do `Drop`, logo abaixo, nao gravar de novo por cima
			return;
		}

		Terminar(e, semVencedor: true);
		_embates.Remove(e);
	}

	/// <summary>
	/// O OCULTO NAO VEM DO DISCO. Chamado no login, junto dos outros estados que nao atravessam a sessao
	/// (`ResgatarDeInteriorMorto`, `AcordarSemAbsorcao`). Quem diz que alguem esta invisivel e a lista
	/// `_invisiveis`, que nasce vazia a cada subida: um `isconcealed` verdadeiro vindo do save deixava o corpo
	/// A VISTA de todo mundo com o poder expresso travado em 5.
	/// </summary>
	private void AcordarVisivel(ServerPlayer pl)
	{
		if (LoginNaoCuraOOcultoDeTeste) return;
		pl.Ficha.isconcealed = false;
	}
}
