using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Economia;
using Jandirus.Core.Items;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// O QUE ESTA LARGADO NO CHAO -- o encanamento do <see cref="Chao"/> (a regra, e o que o original faz, estao la).
///
/// Dono, 2026-10-13: *"poder dropar itens e dinheiro ou recurso"*.
///
/// TRES VERBOS, pelo canal que ja existia (`C2S.Verbo`):
///   * `item_largar &lt;id&gt;[/quantos]` -- a acao "largar" da mochila, que ate a v0.2.0 APAGAVA o item (o comentario
///     dela dizia: *"o item cair no chao pede um sistema inteiro"* -- e este arquivo). Entra pelo `ComandoDeItem`;
///   * `chao_largar zeni/500` e `chao_largar recursos/tudo` -- a carteira;
///   * `chao_pegar` (o mais perto) ou `chao_pegar #12` (aquela coisa).
///
/// QUEM VE: a zona inteira recebe a lista do chao dela (`S2C.Chao`) toda vez que ela muda, e quem chega a recebe na
/// entrada -- o mesmo desenho das Esferas do Dragao (`MandarEsferas`), que sao o outro "objeto que se pega do chao"
/// deste port. A lista e POR ZONA, e a zona de um interior instanciado e so dele: o que se larga dentro de uma nave
/// nao aparece na nave do vizinho.
///
/// NADA DISTO VAI PRO DISCO -- ver o cabecalho do <see cref="Chao"/>.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O que esta no chao de cada zona, pelo hash dela.</summary>
	private readonly Dictionary<ulong, List<Largado>> _largados = [];

	private int _proximoLargado = 1;

	private List<Largado> LargadosDe(ulong zona)
	{
		if (!_largados.TryGetValue(zona, out List<Largado>? lista))
			_largados[zona] = lista = [];
		return lista;
	}

	private bool ComandoDoChao(ServerPlayer pl, string cmd, string arg)
	{
		if (cmd == Chao.VerboPegar) { PegarDoChao(pl, arg.Trim()); return true; }
		if (cmd == Chao.VerboLargar) { LargarDaCarteira(pl, arg.Trim()); return true; }
		return false;
	}

	// =====================================================================
	// LARGAR
	// =====================================================================
	/// <summary>
	/// DA PRA LARGAR AGORA? Caido nao larga, a fera nao larga, e dentro de uma mente nada e real: o que se largasse
	/// la sairia da mochila DE VERDADE (a mochila de quem esta na mente e a de fora -- ver `Saquear`) pra um chao que
	/// some com o transe.
	/// </summary>
	private bool PodeLargar(ServerPlayer pl)
	{
		if (pl.Ficha.KO || pl.Ficha.dead || EhCadaver(pl)) { Avisar(pl, "caído, você não larga nada."); return false; }
		if (SemAsRedeas(pl)) { Avisar(pl, "a fera não larga nada."); return false; }
		if (NaMente(pl)) { Avisar(pl, "aqui dentro nada é real: não há onde largar."); return false; }
		return true;
	}

	/// <summary>
	/// ONDE CAI O QUE SE LARGA: um passo a frente -- `loc = TargetMob.loc` e `step(src, TargetMob.dir)`
	/// (`Grabbing.dm:291-292`). O `step` do BYOND nao entra em parede, e aqui a agua conta junto (nao se pisa nela: o
	/// que caisse la ficaria fora do alcance de quem nao nada). Sem passo livre, cai aos pes de quem largou.
	/// </summary>
	private Vec2 OndeCaiOQueSeLarga(ServerPlayer pl) => UmPassoOuAosPes(pl, pl.Facing);

	/// <summary>
	/// UM PASSO PRA ESTE LADO, se ha chao livre la; senao aos pes do corpo. E a regra de onde cai o que se larga
	/// (um passo a frente) e de onde brota a Semente Senzu (um passo ao sul, `Food.dm:10`).
	/// </summary>
	private Vec2 UmPassoOuAosPes(ServerPlayer pl, Facing lado)
	{
		const int t = ZoneCollision.TileSize;
		Vec2 la = pl.Pos + MeleeArea.Frente(lado) * t;
		(int cx, int cy) = ((int)MathF.Floor(la.X / t), (int)MathF.Floor(la.Y / t));
		bool livre = MapaDaZonaOuCatalogo(pl.Zone) is not { } mapa
					 || (cx >= 0 && cy >= 0 && cx < mapa.Width && cy < mapa.Height && !mapa.BlockedCell(cx, cy) && !mapa.EhAgua(cx, cy));
		return livre ? la : pl.Pos;
	}

	/// <summary>
	/// A ACAO "LARGAR" DA MOCHILA -- `obj/items/verb/Drop(n)` (`Grabbing.dm:240-261`). <paramref name="quantos"/> e o
	/// que veio depois da barra no verbo: vazio larga UM, um numero larga esse tanto, `tudo` larga a pilha inteira (o
	/// `Drop_All()`).
	///
	/// O QUE ESTA EM USO NAO SAI: *"You must unequip it first"* (`DropMe`, `:286-288`). E a mesma pergunta que protege
	/// o scouter ligado e os pesos vestidos do saque (`Saque.EmUso`).
	/// </summary>
	private void LargarItem(ServerPlayer pl, ItemDef def, string quantos)
	{
		if (!PodeLargar(pl)) return;

		if (Saque.EmUso(def.Id, (pl.PoderesConcedidos & Protocol.Poder.Scouter) != 0, pl.Ficha.Weighted > 0))
		{ Avisar(pl, $"você está usando {def.Nome}: tire antes de largar."); return; }

		int n = (int)Chao.Quanto(quantos, pl.Mochila.Quantos(def.Id));
		if (n <= 0) { Avisar(pl, "quantos? Diga um número (ou \"tudo\")."); return; }

		int saiu = pl.Mochila.Tirar(def.Id, n);
		MandarMochila(pl);
		if (saiu <= 0) return;

		if (Chao.LargarApagaDeTeste) { Avisar(pl, $"você joga fora {def.Nome}."); return; }

		// (2026-10-15) OS PESOS CAEM COM A ARTE QUE O DONO ESCOLHEU (o item nao a carrega: ela mora na ficha). Os pesos nao
		// empilham, entao o monte e so deles. Ver `Largado.Arte`.
		string arte = def.Id == CatalogoDeItens.Pesos && !PesoDeTreino.ArteSoNoCorpoDeTeste && pl.Ficha.pesoArte.Length > 0
			? PesoDeTreino.Folha(pl.Ficha.pesoArte, _visual) ?? "" : "";
		PorNoChao(pl.Zone, OndeCaiOQueSeLarga(pl), TipoDeLargado.Item, def.Id, saiu, arte);
		// TODO MUNDO A VISTA FICA SABENDO: `to_chat(view(TargetMob), "[TargetMob] drops [src].")` (`:294`).
		Falar(pl, Protocol.Fala.Emote, $"larga {Chao.Nome(TipoDeLargado.Item, def.Id, saiu)} no chão.");
	}

	/// <summary>
	/// `chao_largar zeni/500` -- o `obj/Zenni/verb/Drop()` (`Inventory.dm:124-142`): *"Drop how much zenni?"*, nunca
	/// mais do que ha na carteira, e so quantia inteira (`zenni=round(zenni)`, `if(zenni>=1)`). O recurso e o mesmo
	/// gesto com o outro saldo. Sem quantia o verbo PERGUNTA em vez de adivinhar: largar a carteira inteira por um
	/// argumento esquecido seria o pior acidente deste arquivo.
	/// </summary>
	private void LargarDaCarteira(ServerPlayer pl, string arg)
	{
		int barra = arg.IndexOf('/');
		string oQue = barra >= 0 ? arg[..barra] : arg, quanto = barra >= 0 ? arg[(barra + 1)..] : "";

		bool recursos = string.Equals(oQue, Recurso.PalavraDeRecursos, StringComparison.OrdinalIgnoreCase);
		if (!recursos && !string.Equals(oQue, Recurso.PalavraDeZeni, StringComparison.OrdinalIgnoreCase))
		{ Avisar(pl, "largar o quê? Zeni ou recursos."); return; }
		if (!PodeLargar(pl)) return;

		double tem = Math.Floor(recursos ? pl.Ficha.Recursos : pl.Ficha.Zeni);
		double n = Chao.Quanto(quanto, tem, semPedido: 0);
		if (n < 1)
		{
			Avisar(pl, tem < 1 ? $"você não tem {(recursos ? "recursos" : "zeni")} pra largar."
							   : "quanto? Diga um número (ou \"tudo\").");
			return;
		}

		if (recursos) pl.Ficha.Recursos -= n; else pl.Ficha.Zeni -= n;
		MandarCarteira(pl);

		TipoDeLargado tipo = recursos ? TipoDeLargado.Recursos : TipoDeLargado.Zeni;
		PorNoChao(pl.Zone, OndeCaiOQueSeLarga(pl), tipo, "", n);
		// `to_chat(view(usr), "[usr] drops [num2text(zenni,20)] zenni.")` (`:139`).
		Falar(pl, Protocol.Fala.Emote, $"larga {Chao.Nome(tipo, "", n)} no chão.");
		GD.Print($"[server] {pl.Name} LARGOU {Chao.Nome(tipo, "", n)} em {pl.Zone.Name}");
	}

	/// <summary>
	/// POE UMA COISA NO CHAO -- o funil de tudo o que cai: o que o jogador larga, e o que uma tecnica cria no chao.
	/// A posicao vai pro CENTRO da celula (e onde o cliente a desenha); o que e do mesmo monte se junta ao que ja
	/// estava ali (`Chao.Junta`); e a zona que passa do teto perde a coisa mais velha. Avisa a zona.
	/// </summary>
	/// <param name="arte">(2026-10-15) a folha que a tela desenha no lugar da do item (`Largado.Arte`); vazia = a do catalogo.</param>
	internal Largado PorNoChao(ZoneKey zona, Vec2 onde, TipoDeLargado tipo, string item, double quantidade, string arte = "")
	{
		const int t = ZoneCollision.TileSize;
		float x = MathF.Floor(onde.X / t) * t + t / 2f, y = MathF.Floor(onde.Y / t) * t + t / 2f;
		List<Largado> lista = LargadosDe(zona.Hash);
		long some = NowMs() + (long)(Chao.SegundosNoChao * 1000);

		Largado? monte = lista.Find(l => l.X == x && l.Y == y && Chao.Junta(l, tipo, item));
		if (monte != null)
		{
			monte.Quantidade += quantidade;
			monte.SomeMs = some;
		}
		else
		{
			monte = new Largado { Id = _proximoLargado++, Tipo = tipo, Item = item, Quantidade = quantidade, X = x, Y = y, SomeMs = some, Arte = arte };
			lista.Add(monte);
			// O TETO DA ZONA: a lista esta na ordem em que as coisas cairam, entao a mais velha e a primeira.
			while (lista.Count > Chao.MaximoPorZona) lista.RemoveAt(0);
		}

		MandarOChao(zona);
		return monte;
	}

	// =====================================================================
	// PEGAR
	// =====================================================================
	private static bool AoAlcanceDoChao(ServerPlayer pl, Largado l) =>
		Math.Abs(l.X - pl.Pos.X) <= Chao.Alcance && Math.Abs(l.Y - pl.Pos.Y) <= Chao.Alcance;

	/// <summary>A coisa largada mais perto deste corpo, dentro do alcance -- a mesma caixa por eixo da tecla E.</summary>
	private Largado? MaisPertoNoChao(ServerPlayer pl)
	{
		Largado? melhor = null;
		float melhorDist = float.MaxValue;
		foreach (Largado l in LargadosDe(pl.Zone.Hash))
		{
			if (!AoAlcanceDoChao(pl, l)) continue;
			float d = (l.X - pl.Pos.X) * (l.X - pl.Pos.X) + (l.Y - pl.Pos.Y) * (l.Y - pl.Pos.Y);
			if (d >= melhorDist) continue;
			melhorDist = d;
			melhor = l;
		}
		return melhor;
	}

	/// <summary>
	/// `chao_pegar` -- o `Get()` dos dois objetos do original (`Grabbing.dm:221-235` e `Inventory.dm:144-151`): de pe
	/// (*"You cant, you are knocked out"*), ao alcance, e com vaga na mochila quando e item (*"You have no space in
	/// your inventory"*). O que nao coube FICA no chao -- a pilha de dez macas numa mochila com uma vaga... cabe
	/// inteira (pilha e um slot); a de tres maquinas numa mochila com uma vaga leva uma e deixa duas.
	///
	/// DO ALTO NAO SE ALCANCA O CHAO: quem voa acima do cenario (`Voo.AtravessaCenario`) pousa antes. O DM nao tem
	/// altura; a regra e a mesma que ja separa o soco de quem esta la em cima.
	/// </summary>
	private void PegarDoChao(ServerPlayer pl, string arg)
	{
		if (pl.Ficha.KO || pl.Ficha.dead || EhCadaver(pl)) { Avisar(pl, "caído, você não pega nada."); return; }
		if (SemAsRedeas(pl)) { Avisar(pl, "a fera não cata nada do chão."); return; }
		if (Voo.AtravessaCenario(pl.Altitude)) { Avisar(pl, "daqui de cima você não alcança o chão: desça."); return; }

		List<Largado> lista = LargadosDe(pl.Zone.Hash);
		Largado? alvo = arg.StartsWith('#') && int.TryParse(arg.AsSpan(1), out int id) ? lista.Find(l => l.Id == id)
					  : arg.Length == 0 ? MaisPertoNoChao(pl)
					  : null;
		if (alvo == null || !AoAlcanceDoChao(pl, alvo)) { Avisar(pl, "não há nada no chão ao seu alcance."); return; }

		string nome;
		switch (alvo.Tipo)
		{
			case TipoDeLargado.Zeni:
				nome = Chao.Nome(alvo.Tipo, "", alvo.Quantidade);
				pl.Ficha.Zeni += alvo.Quantidade;
				MandarCarteira(pl);
				lista.Remove(alvo);
				break;

			case TipoDeLargado.Recursos:
				nome = Chao.Nome(alvo.Tipo, "", alvo.Quantidade);
				DarRecursos(pl, alvo.Quantidade);
				lista.Remove(alvo);
				break;

			default:
				int havia = (int)alvo.Quantidade;
				int sobrou = pl.Mochila.Guardar(alvo.Item, havia);
				if (sobrou >= havia) { Avisar(pl, $"sua mochila está cheia ({Inventario.Slots} espaços)."); return; }
				nome = Chao.Nome(alvo.Tipo, alvo.Item, havia - sobrou);
				alvo.Quantidade = sobrou;
				if (sobrou <= 0) lista.Remove(alvo);
				MandarMochila(pl);
				break;
		}

		MandarOChao(pl.Zone);
		// `to_chat(view(TargetMob), "[TargetMob] picks up [src].")` (`Grabbing.dm:273`).
		Falar(pl, Protocol.Fala.Emote, $"pega {nome} do chão.");
		if (alvo.Tipo != TipoDeLargado.Item) GD.Print($"[server] {pl.Name} PEGOU {nome} em {pl.Zone.Name}");
	}

	// =====================================================================
	// O RELOGIO E A REDE
	// =====================================================================
	/// <summary>O que ninguem pegou some depois de `Chao.SegundosNoChao`. Roda uma vez por segundo.</summary>
	private void TickDoChao()
	{
		long agora = NowMs();
		foreach ((ulong hash, List<Largado> lista) in _largados)
		{
			if (lista.Count == 0 || lista.RemoveAll(l => agora >= l.SomeMs) == 0) continue;
			MandarOChaoPorHash(hash);
		}
	}

	/// <summary>A lista do chao desta zona, no fio: `ushort n`, e por coisa o id, o tipo, o item, a quantidade e a posicao.</summary>
	private NetDataWriter PacoteDoChao(ulong zona)
	{
		List<Largado> lista = LargadosDe(zona);
		var w = Protocol.Begin(Protocol.S2C.Chao);
		w.Put((ushort)lista.Count);
		foreach (Largado l in lista)
		{
			w.Put(l.Id);
			w.Put((byte)l.Tipo);
			w.Put(l.Item);
			w.Put(l.Quantidade);
			w.Put(l.X);
			w.Put(l.Y);
			w.Put(l.Arte);   // (2026-10-15) a folha no lugar da do item (os pesos com arte); vazia no resto
		}
		return w;
	}

	/// <summary>Manda o chao de uma zona a todo mundo que esta nela. So quando muda.</summary>
	private void MandarOChao(ZoneKey zona) => MandarOChaoPorHash(zona.Hash);

	private void MandarOChaoPorHash(ulong hash)
	{
		NetDataWriter? w = null;
		foreach (ServerPlayer pl in _players.Values)
		{
			if (pl.Zone.Hash != hash || pl.Peer is not { } tela) continue;
			w ??= PacoteDoChao(hash);
			tela.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}
	}

	/// <summary>
	/// Manda o chao da zona em que este corpo esta, so pra ele -- na entrada (login e troca de zona). MESMO VAZIO: a
	/// lista vazia e o que apaga da tela o chao da zona de onde ele veio.
	/// </summary>
	private void MandarOChaoPara(ServerPlayer pl) =>
		pl.Peer?.Send(PacoteDoChao(pl.Zone.Hash), Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
}
