using Godot;
using Jandirus.Core.Social;

namespace Jandirus.Server;

/// <summary>
/// ============================ QUEM SAIU DA VAGA SAI DA ABA PEOPLE DE TODO MUNDO -- pedido do dono, 2026-10-19 ============================
/// *"personagens q reencarnam no jogo n perdem a intimidade com os personagens q ele conhecia, personagens reencarnados ou
/// deletados deveriam resetar suas relacoes da aba people"*.
///
/// A CAUSA: a lista de conhecidos (`Convivio.Conhecidos`) e chaveada pela ASSINATURA, e a assinatura e `hash(conta, slot)`
/// (`ServerPlayer.AssinaturaDe`) -- o personagem que nasce numa vaga herda a assinatura de quem estava nela. O apagamento so
/// limpava o discipulado (`PurgarAssinatura`); as fichas que os OUTROS tinham do antigo ficavam pra sempre, e o novo personagem
/// da vaga (o reencarnado volta SEMPRE na mesma, `Boot.AoSerDespedido`) as herdava inteiras: o convivio, a declaracao, a historia,
/// o "desde quando". A primeira foto dele so trocava o nome e a raca (`Convivio.Fotografar` guarda o resto, de proposito) -- e a
/// raiva, o filho, a Mente Implacavel e a Transmissao passavam a ler o recem-nascido como o amigo (ou o inimigo) antigo. A lista
/// do proprio personagem novo ja nascia vazia (`CreateChar`).
///
/// O CONSERTO SAO DUAS PORTAS, e cada uma tem o seu defeito injetado:
///   * A HORA DA RECICLAGEM -- <see cref="EsquecerAAssinaturaNoConvivio"/>, chamado do `PurgarAssinatura` (apagar, reencarnar, ser
///     absorvido e CRIAR na vaga): toda ficha daquela assinatura e de outra pessoa, e sai de todas as listas;
///   * O BOOT -- <see cref="VarrerOConvivioDeQuemNaoExisteMais"/>: as fichas que ja estavam nos saves antes deste conserto (o
///     personagem que reencarnou ontem) saem pela pergunta pura do Core (`AssinaturaReciclada.EDeOutraPessoa`).
///
/// O CLIENTE NAO PRECISA DE NADA: a aba desenha a lista que o servidor manda (`MandarConhecidos`), e os detalhes guardados de quem
/// saiu da lista se podam sozinhos (`GameClient.PodarDetalhes`). O retrato (`VistosDeGente`) e por NOME e se refaz na primeira
/// vez que o novo personagem e visto.
/// ======================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--recicladoteste`): a vaga reciclada nao tira a assinatura de lista nenhuma -- quem conhecia o
	/// personagem apagado continua o conhecendo, e o proximo da vaga herda tudo. Lido pelo <see cref="EsquecerAAssinaturaNoConvivio"/>.
	/// Falso em jogo, sempre.
	/// </summary>
	internal static bool ConvivioHerdaAAssinaturaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--recicladoteste`): a varredura do boot nao tira ficha nenhuma -- o lixo de antes do conserto
	/// fica nos saves. Lido pelo <see cref="VarrerOConvivioDeQuemNaoExisteMais"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool ConvivioVelhoFicaDeTeste;

	/// <summary>
	/// A ULTIMA VARREDURA: se rodou, quantas contas leu, quantas fichas tirou e quantas contas regravou. A bancada confere que ela
	/// RODOU NO BOOT deste processo -- e o que prova que a linha do boot esta la.
	/// </summary>
	internal (bool Rodou, int Contas, int Fichas, int Gravadas) VarreduraDoConvivio;

	/// <summary>
	/// A VAGA PERDEU (OU GANHOU) UM PERSONAGEM: ninguem mais conhece quem estava nela. Tira a assinatura de TODA lista de
	/// conhecidos, nas tres casas de uma lista -- a casa esquecida regravaria a ficha por cima das outras:
	///   1. o corpo de quem esta no mundo (`ServerPlayer.Social`): e dele que o `Persistir` remonta o slot, e a aba se refaz na hora;
	///   2. as copias de conta na memoria (`_contas`, `_logados`): a da tela de selecao regravaria o arquivo inteiro a partir dela;
	///   3. o disco, conta por conta: quem esta fora do jogo acha a lista ja limpa quando entrar.
	/// </summary>
	private void EsquecerAAssinaturaNoConvivio(string sig)
	{
		if (sig.Length == 0 || ConvivioHerdaAAssinaturaDeTeste) return;
		int fichas = 0, gravadas = 0;

		foreach (ServerPlayer pl in _players.Values)
			if (pl.Social.Conhecidos.Remove(sig)) { fichas++; MandarConhecidos(pl); }

		foreach (AccountSave viva in _contas.Values.Concat(_logados.Values))
			fichas += TirarDasListas(viva, sig);

		if (_store != null)
			foreach (AccountSave acc in _store.Todas())
			{
				int n = TirarDasListas(acc, sig);
				if (n == 0) continue;
				_store.Gravar(acc);
				fichas += n;
				gravadas++;
			}

		if (fichas > 0)
			GD.Print($"[server] convivio: a assinatura {sig} foi reciclada -- {fichas} ficha(s) esquecida(s), {gravadas} conta(s) regravada(s)");
	}

	/// <summary>Tira a assinatura da lista de cada personagem da conta. Devolve quantas fichas sairam.</summary>
	private static int TirarDasListas(AccountSave acc, string sig)
	{
		int n = 0;
		foreach (CharacterSave? c in acc.Slots)
			if (c?.Social is { } social && social.Conhecidos.Remove(sig)) n++;
		return n;
	}

	/// <summary>
	/// A FAXINA DO BOOT: le todas as contas do disco, anota quando nasceu o personagem de cada vaga (ou que ela esta vazia) e tira
	/// de cada lista as fichas que sao de outra pessoa (`AssinaturaReciclada.EDeOutraPessoa`). Ficha de assinatura que nenhuma vaga
	/// deste servidor produz FICA: sem saber quem ela e, nao ha como dizer que nao e mais.
	///
	/// NO BOOT, e nao no login de cada um: uma vez por subida a pasta inteira, contra uma vez por login. E ninguem esta no mundo
	/// ainda -- o disco e a unica casa das listas.
	/// </summary>
	private void VarrerOConvivioDeQuemNaoExisteMais()
	{
		VarreduraDoConvivio = (true, 0, 0, 0);
		if (_store == null || ConvivioVelhoFicaDeTeste) return;

		List<AccountSave> contas = _store.Todas();
		var nascidos = new Dictionary<string, long?>(StringComparer.Ordinal);
		foreach (AccountSave a in contas)
			for (int s = 0; s < a.Slots.Length; s++)
				nascidos[ServerPlayer.AssinaturaDe(a.Conta, s)] = a.Slots[s]?.CriadoEm;

		int fichas = 0, gravadas = 0;
		var deOutra = new List<string>();
		foreach (AccountSave a in contas)
		{
			int n = 0;
			foreach (CharacterSave? c in a.Slots)
			{
				if (c?.Social is not { } social) continue;
				deOutra.Clear();
				foreach ((string sig, Conhecido ficha) in social.Conhecidos)
					if (nascidos.TryGetValue(sig, out long? nasceu) && AssinaturaReciclada.EDeOutraPessoa(ficha, nasceu))
						deOutra.Add(sig);
				foreach (string sig in deOutra) social.Conhecidos.Remove(sig);
				n += deOutra.Count;
			}
			if (n == 0) continue;
			_store.Gravar(a);
			fichas += n;
			gravadas++;
		}

		VarreduraDoConvivio = (true, contas.Count, fichas, gravadas);
		if (fichas > 0)
			GD.Print($"[server] convivio: a varredura do boot tirou {fichas} ficha(s) de quem nao existe mais, em {gravadas} de {contas.Count} conta(s)");
	}
}
