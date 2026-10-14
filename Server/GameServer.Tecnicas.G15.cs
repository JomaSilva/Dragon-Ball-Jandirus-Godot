using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Stats;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// LOTE G15 (2026-10-13) -- AS SKILLS QUE CRIAM ITEM.
///
/// O dono: *"tem skills q n estao funcionando como materialize etc, essas skills q criam itens tem q ser quando a
/// implementaçao de dropar item tiver feita, pq por exemplo materialize criaria um item no chao (itens como peso,
/// roupa etc)"*. O chao chegou nesta versao (`GameServer.Chao.cs`), e com ele:
///
///     Materialize        `Magic_Tree.dm:243-262`, `Rituals_Manipulation.dm:521-570`     o item toma forma no chao
///     Grow_Senzu_Bean    `Stamina/Food.dm:2-13`                                         a semente BROTA no chao
///                        (o verb e do lote G12; o que mudou e onde ela nasce -- ver `TickDoSenzuG12`)
///
/// A regra da Materialize (o que se cria, quanto custa, e tudo o que diverge do ritual do DM) esta em
/// `Core/Items/Materializacao.cs`. Aqui fica so o gesto.
/// </summary>
public sealed partial class GameServer
{
	private void RegistrarTecnicasG15()
	{
		IniciarLote("G15");
		Vivo(Materializacao.Verbo, MaterializarG15);
	}

	/// <summary>
	/// O PEDIDO DA TELA, pelo canal de verbos (`materializar roupa/Clothes_GiTop/2A2A33`): o canal de habilidade nao
	/// comporta o nome de uma folha mais a cor (ver `Materializacao.VerboDeCanal`). Entra no MESMO funil do botao --
	/// a area de espera do torneio, o `SabeTecnica` --, e nao numa porta ao lado dele.
	/// </summary>
	private bool ComandoDeMaterializar(ServerPlayer pl, string cmd, string arg)
	{
		if (cmd != Materializacao.VerboDeCanal) return false;
		UsarHabilidade(pl, $"{Materializacao.Verbo}:{arg.Trim()}");
		return true;
	}

	/// <summary>
	/// MATERIALIZAR: o item pedido toma forma no chao, AOS PES de quem conjura -- o `locate(u.x,u.y,u.z)` da roupa do
	/// ritual (`Rituals_Manipulation.dm:530`) --, e o Ki do ritual sai (`Materializacao.Custo`).
	///
	/// QUEM NAO PODE: o caido, a fera e quem esta dentro de uma mente (as tres recusas de largar um item -- ver
	/// `PodeLargar` --, pela mesma razao: o que nascesse ali nasceria num chao que nao e de verdade, ou de um corpo
	/// que nao esta fazendo gesto nenhum), e quem medita ou treina.
	/// </summary>
	private void MaterializarG15(ServerPlayer pl, string pedido)
	{
		Fighter f = pl.Ficha;
		if (f.KO || f.dead || EhCadaver(pl)) { Avisar(pl, "caido, voce nao materializa nada."); return; }
		if (SemAsRedeas(pl)) { Avisar(pl, "a fera nao materializa nada."); return; }
		if (NaMente(pl)) { Avisar(pl, "aqui dentro nada e real: nao ha o que materializar."); return; }
		if (f.med || f.train) { Avisar(pl, "pare o que esta fazendo antes de materializar."); return; }
		if (pedido.Length == 0)
		{
			Avisar(pl, "materializar o que? Pesos de treino ou uma roupa -- escolha na tela que o botao abre.");
			return;
		}

		string? item = Materializacao.Item(pedido, CatalogoDeItens.Visual, out string porque);
		if (item == null) { Avisar(pl, porque); return; }

		double custo = Materializacao.Custo(f);
		if (f.Ki < custo) { Avisar(pl, $"materializar pede {custo:0} de energia (voce tem {f.Ki:0})."); return; }

		f.Ki -= custo;
		PorNoChao(pl.Zone, pl.Pos, TipoDeLargado.Item, item, 1);
		Falar(pl, Protocol.Fala.Emote, $"junta energia entre as mãos, e {Chao.Nome(TipoDeLargado.Item, item, 1)} toma forma no chão.");
		GD.Print($"[server] {pl.Name} MATERIALIZOU {item} em {pl.Zone.Name} (-{custo:0} de Ki)");
	}
}
