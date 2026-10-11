using Godot;
using Jandirus.Core.Items;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// O SAQUE DE QUEM ESTA NOCAUTEADO -- o encanamento do <see cref="Saque"/> (a regra e o que o original faz
/// estao la). Item 9b do playtest de 2026-10-10.
///
/// UM VERBO, DUAS PERGUNTAS, pelo canal de verbos que ja existia (`C2S.Verbo`):
///   * `saquear` (sem argumento) -- "o que da pra levar de quem esta caido aqui?". O servidor responde com a
///     lista (`S2C.Saque`), e o menu da tecla E do cliente a desenha;
///   * `saquear &lt;item&gt;` -- "levo este". O servidor confere TUDO de novo (o caido ainda esta caido e ao
///     alcance, o item ainda esta com ele e fora de uso, cabe na mochila do ladrao) e so entao troca de mao.
///
/// NADA E DECIDIDO NO CLIENTE: a lista que ele mostra e uma foto, e o clique num item que ja saiu so rende um
/// aviso e a lista nova.
///
/// O NPC CAI NA MESMA REGRA -- e nao tem o que levar: nenhum molde deste port nasce com item na mochila. Dar
/// inventario a NPC e um sistema de loot, e o pedido manda perguntar antes de criar um.
/// </summary>
public sealed partial class GameServer
{
	private bool ComandoDeSaque(ServerPlayer pl, string cmd, string arg)
	{
		if (cmd != Saque.Verbo) return false;
		Saquear(pl, arg.Trim());
		return true;
	}

	/// <summary>
	/// `M.KO` e mais nada (`Grabbing.dm:4`) -- nocauteado e VIVO. O cadaver tem o menu dele (enterrar), e o
	/// corpo largado carrega a ficha de alguem que esta noutro lugar (na mente, no leme): e a mesma fronteira
	/// da absorcao do Majin.
	///
	/// A CARCACA DE UM BICHO E A SEGUNDA COISA QUE SE REVISTA (2026-10-10: *"ao matar eles vc pode lootear eles
	/// e pegar carne"*). Ela e cadaver, e entra por aqui e nao pelo menu de enterrar: a carne esta na mochila
	/// dela, e sai pelo mesmo verbo, um pedaco por vez. Ver `AnimalVirouCarcaca` (`GameServer.Animais.cs`).
	/// </summary>
	private bool PodeSerSaqueado(ServerPlayer o) =>
		o.Combate != null
		&& (EhCarcaca(o) || (o.Ficha.KO && !o.Ficha.dead && !o.ECadaver && o.DonoDoCorpoLargado == 0));

	/// <summary>
	/// O CAIDO MAIS PERTO, dentro do alcance da tecla E -- a mesma caixa que o cliente usa pra oferecer o
	/// gesto (`MenuDeInteracao`) e que o cadaver ja usava (<see cref="CadaverPerto"/>): as duas pontas tem que
	/// concordar sobre "ao alcance", senao o menu abre e o verbo falha.
	/// </summary>
	private ServerPlayer? CaidoPerto(ServerPlayer pl)
	{
		ServerPlayer? melhor = null;
		float melhorDist = float.MaxValue;

		foreach (ServerPlayer c in ZoneList(pl.Zone.Hash))
		{
			if (c == pl || !PodeSerSaqueado(c)) continue;
			if (Math.Abs(c.Pos.X - pl.Pos.X) > Jandirus.Core.Tech.Interacoes.Alcance
				|| Math.Abs(c.Pos.Y - pl.Pos.Y) > Jandirus.Core.Tech.Interacoes.Alcance) continue;

			float d = (c.Pos - pl.Pos).LengthSquared;
			if (d >= melhorDist) continue;
			melhorDist = d;
			melhor = c;
		}
		return melhor;
	}

	/// <summary>O que da pra levar deste corpo agora: a mochila dele, menos o que esta em uso.</summary>
	private static List<Pilha> OQueDaPraLevarDe(ServerPlayer alvo) =>
		Saque.OQueDaPraLevar(alvo.Mochila,
							 scouterLigado: (alvo.PoderesConcedidos & Protocol.Poder.Scouter) != 0,
							 comPesos: alvo.Ficha.Weighted > 0);

	private static string NomeDoItemDeSaque(string id) => CatalogoDeItens.Get(id)?.Nome ?? id;

	private void Saquear(ServerPlayer pl, string item)
	{
		// QUEM SAQUEIA ESTA DE PE E E DONO DE SI: `!KO && canfight && !Apeshit` (`Grabbing.dm:4`).
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "caído, você não revista ninguém."); return; }
		if (SemAsRedeas(pl)) { Avisar(pl, "a fera não revista bolsos."); return; }
		// DENTRO DE UMA MENTE NADA E REAL -- e a mochila de quem esta la e a de verdade. Sem esta linha um
		// treino na mente de alguem seria um jeito de levar os itens dele pra fora.
		if (NaMente(pl)) { Avisar(pl, "aqui dentro nada é real: não há o que levar."); return; }

		ServerPlayer? alvo = CaidoPerto(pl);
		if (alvo == null) { Avisar(pl, "não há ninguém nocauteado ao seu alcance."); return; }

		List<Pilha> leva = OQueDaPraLevarDe(alvo);

		// SEM ITEM: e a pergunta. A lista vai mesmo vazia -- o menu diz "nada" em vez de nao abrir.
		if (item.Length == 0)
		{
			MandarSaque(pl, alvo, leva);
			return;
		}

		long agora = NowMs();
		if (agora < pl.ProximoSaqueMs)
		{
			// CLIQUE EM CIMA DE CLIQUE: o respiro do `Saque.SegundosEntreSaques`. A lista volta junto -- o menu
			// do cliente fecha a cada escolha, e sem ela ele ficaria fechado sem ter levado nada.
			Avisar(pl, "um de cada vez.");
			MandarSaque(pl, alvo, leva);
			return;
		}

		if (!leva.Exists(p => p.Id == item))
		{
			Avisar(pl, "isso não está mais com ele.");
			MandarSaque(pl, alvo, leva);
			return;
		}

		// PRIMEIRO CABE, DEPOIS SAI: guardar antes de tirar e o que impede o item de sumir no ar quando a
		// mochila do ladrao esta cheia.
		if (pl.Mochila.Guardar(item, 1) > 0)
		{
			Avisar(pl, $"sua mochila está cheia ({Inventario.Slots} espaços).");
			return;
		}
		alvo.Mochila.Tirar(item, 1);
		pl.ProximoSaqueMs = agora + (long)(Saque.SegundosEntreSaques * 1000);

		MandarMochila(pl);
		MandarMochila(alvo);

		// TODO MUNDO A VISTA FICA SABENDO: `to_chat(view(usr), "[usr] steals [item] from [M]!")` (`:23`).
		string nome = NomeDoItemDeSaque(item);
		// (Da carcaca de um bicho nao se "rouba": tira-se a carne.)
		Falar(pl, Protocol.Fala.Emote, EhCarcaca(alvo) ? $"tira {nome} do {NomeVisivel(alvo)}." : $"rouba {nome} de {NomeVisivel(alvo)}!");
		GD.Print($"[server] {pl.Name} SAQUEOU {nome} de {alvo.Name}");

		// A CARCACA QUE ESVAZIOU SOME NA HORA, e sem lista nova: nao ha mais o que revistar ali.
		if (EhCarcaca(alvo) && alvo.Mochila.Ocupados == 0) { DesfazerOCadaver(alvo); return; }

		MandarSaque(pl, alvo, OQueDaPraLevarDe(alvo));
	}

	/// <summary>
	/// A LISTA, pro ladrao e so pra ele (`S2C.Saque`): quem e o caido, e o que da pra levar dele. A bancada a le
	/// pelo <see cref="EscutaDeSaque"/>, que recebe o mesmo que vai no fio.
	/// </summary>
	private void MandarSaque(ServerPlayer pl, ServerPlayer alvo, List<Pilha> leva)
	{
		EscutaDeSaque?.Invoke(pl.Id, alvo.Id, leva);
		if (pl.Peer == null) return;

		var w = Protocol.Begin(Protocol.S2C.Saque);
		w.Put(alvo.Id);
		w.Put(NomeVisivel(alvo));
		w.Put((byte)Math.Min(leva.Count, Inventario.Slots));
		for (int i = 0; i < leva.Count && i < Inventario.Slots; i++)
		{
			w.Put(leva[i].Id);
			w.Put((ushort)Math.Clamp(leva[i].Quantidade, 0, ushort.MaxValue));
		}
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>ESPIA DE BANCADA -- nulo em jogo. Recebe (ladrao, caido, lista) de cada `S2C.Saque` montado.</summary>
	internal Action<int, int, List<Pilha>>? EscutaDeSaque;
}
