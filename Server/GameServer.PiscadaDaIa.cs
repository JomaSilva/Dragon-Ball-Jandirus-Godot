using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A PISCADA DA IA -- o NPC que sabe a Afterimage Technique passa a USA-LA na briga (dono, 2026-10-12: *"sinto q os
/// npcs em geral como os viloes aleatorios q dao karma e os npcs do mundo em si n estao com mt variedade de skills, n
/// vi nenhum com after image por exemplo"*).
///
/// ============================ ERA UMA DECISAO EM ABERTO, E O DONO A TOMOU ============================
/// O cabecalho do `GameServer.Ia.cs` dizia: *"`C2S.Zanzoken` -- a IA nao pisca"*, e deixava a escolha com o dono. Ha
/// duas metades nisso, e as duas estao resolvidas:
///   * SABER a tecnica: o molde passou a sortear talentos por corpo (`MoldeDeNpc.Talentos`), e a Afterimage e um deles;
///   * USAR: e este arquivo. O embate de Zanzoken (o quick time event) ja sabia responder pela maquina
///     (`ResponderPelaMaquina`, com o tempo de reacao e a inteligencia do cerebro), entao um NPC que pisca e troca
///     socos com quem tambem pisca ja disputa o embate como qualquer corpo.
///
/// ============================ O QUE ELE FAZ, E POR ONDE ============================
/// Perseguindo uma presa a MEIA DISTANCIA, o corpo some e reaparece NAS COSTAS dela -- a jogada que todo jogador com
/// a tecnica faz. Sai pela MESMA porta do duplo clique (`Zanzoken`): a skill, o Ki por tile, o alcance
/// (`zanzorange`, que cresce com a pericia: um corpo fraco nao alcanca nada), a parede no caminho e a miragem que
/// fica pra tras sao os do jogador. Nada aqui da ao NPC o que o funil do jogador negaria.
///
/// A RECARGA E SO DA IA: o jogador e limitado pelo Ki, e uma maquina que piscasse toda vez que o Ki deixasse viraria
/// um alvo que ninguem acerta. Entre uma piscada e outra passam de <see cref="SegundosEntrePiscadasMin"/> a
/// <see cref="SegundosEntrePiscadasMax"/> segundos, sorteados.
///
/// O QUE NAO VEIO: a piscada de ESQUIVA (sumir da frente de um raio). Ela pede que o cerebro saiba que ha um tiro
/// vindo, e a `Percepcao` nao tem esse campo.
/// =====================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Abaixo disto (em tiles) a presa esta colada, e colado se soca.</summary>
	private const float PiscadaDaIaDePertoTiles = 2.5f;

	private const double SegundosEntrePiscadasMin = 3, SegundosEntrePiscadasMax = 7;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v020teste`): a IA nao pisca -- o jogo de antes de 2026-10-12, em que o NPC com a
	/// tecnica a carregava sem nunca usar. Lido pela propria porta, em <see cref="PiscarPelaIa"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool IaNaoPiscaDeTeste;

	/// <summary>Quando cada corpo da IA pode piscar de novo (relogio do servidor, em ms).</summary>
	private readonly Dictionary<int, long> _proximaPiscadaDaIa = [];

	/// <summary>Quantas piscadas a IA deu desde o boot. Lido pela bancada.</summary>
	internal int PiscadasDaIaDeTeste;

	/// <summary>
	/// A IA PISCA PRAS COSTAS DA PRESA, se sabe, pode e esta na hora. Chamada a cada tique de corpo dirigido que tem
	/// presa; quase sempre sai na primeira linha (nao sabe a tecnica) ou na segunda (a recarga).
	/// </summary>
	private void PiscarPelaIa(ServerPlayer npc, ServerPlayer presa, Plano plano)
	{
		// (o `UsaAMiragem` e o `haszanzo` do DM: quem guardou a tecnica -- `Afterimage_Toggle` -- e perdeu as redeas nao pisca)
		if (IaNaoPiscaDeTeste || !UsaAMiragem(npc)) return;

		// O PERFIL CORTA POR CIMA, como corta o arsenal de longe e o sopro (`PerfilDeCombate.Filtrar`): o corpo que
		// sabe a tecnica e "so luta de corpo" nao a usa. A piscada e ki, e o habitante sorteia o perfil ao nascer.
		if (!npc.Perfil.UsaKi) return;

		long agora = NowMs();
		if (_proximaPiscadaDaIa.TryGetValue(npc.Id, out long quando) && agora < quando) return;

		// SO QUEM ESTA FECHANDO A DISTANCIA PRA BATER: quem foge, descansa ou esta plantado pra atirar nao pula pra
		// cima do alvo. E a presa tem de estar de pe -- em cima de um caido nao ha costas pra ganhar.
		if (plano != Plano.Pressionar || presa.Ficha.KO || EhCadaver(presa) || presa.Zone.Hash != npc.Zone.Hash) return;

		const float tile = ZoneCollision.TileSize;
		float distancia = Vec2.Distance(npc.Pos, presa.Pos);
		int alcance = ZanzorangeG10(npc.Ficha);
		if (distancia < PiscadaDaIaDePertoTiles * tile || distancia > alcance * tile) return;

		// DAQUI PRA BAIXO A TENTATIVA CONTA: a recarga e armada mesmo que a porta recuse (sem Ki, parede no caminho),
		// senao um corpo encurralado bateria nela trinta vezes por segundo.
		_proximaPiscadaDaIa[npc.Id] = agora + (long)((SegundosEntrePiscadasMin
			+ _rng.NextDouble() * (SegundosEntrePiscadasMax - SegundosEntrePiscadasMin)) * 1000);

		// AS COSTAS: um tile atras de onde a presa olha. Sem chao livre ali, um dos lados.
		Vec2 frente = MeleeArea.Frente(presa.Facing);
		var lado = new Vec2(-frente.Y, frente.X);
		if (PontoLivre(npc.Zone, presa.Pos - frente * tile, presa.Pos + lado * tile, presa.Pos - lado * tile) is not { } destino) return;

		Vec2 de = npc.Pos;
		Zanzoken(npc, destino);
		if (Vec2.Distance(npc.Pos, de) < 1) return;   // a porta recusou (Ki, parede): a recarga ja esta armada

		// CHEGA ENCARANDO A PRESA: o jogador vira o proprio boneco no golpe seguinte; a IA vira aqui.
		npc.Facing = MoveRules.FacingFrom(presa.Pos - npc.Pos, npc.Facing);
		PiscadasDaIaDeTeste++;
	}
}
