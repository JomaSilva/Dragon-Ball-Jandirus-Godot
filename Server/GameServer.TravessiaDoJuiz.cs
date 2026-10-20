using Godot;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ O KING YEMMA VAI E VOLTA DO OUTRO MUNDO -- pedido do dono, 2026-10-19 ============================
/// *"faca com q king yemma possa ter uma skill q permite ir e voltar do outro mundo pra poder reviver as pessoas la, ao voltar do
/// outro mundo ele volta pro exato local onde estava no mundo dos vivos"*. Os nomes que as duas pontas dividem estao em
/// <see cref="TravessiaDoJuiz"/>, no Core.
///
/// UM BOTAO SO, E O LUGAR DIZ O SENTIDO: no mundo dos vivos ele LEVA o juiz pra mesa do Enma (onde os mortos chegam) e guarda na
/// ficha a zona e o ponto EXATOS de onde ele saiu (`Fighter.VoltaDoJuizZona`); no Outro Mundo (o Outro Mundo, o Ceu ou o Inferno)
/// ele o DEVOLVE la. A ficha vai pro disco na hora: o juiz que sai do jogo do lado de la volta pro mesmo lugar quando entrar e
/// apertar de novo.
///
/// O QUE ELE FAZ LA E O `Revive` DO KIT, que ja existia e pede um morto colado (`RessuscitarG4`). O que mudou por causa desta
/// travessia mora la: quem volta a vida DENTRO do Outro Mundo acorda no mundo dos vivos, no berco (escolha do dono), e nao ao lado
/// do juiz -- as passagens do Outro Mundo so levam ao Ceu e ao Inferno, e um vivo puxado pra perto dele ficaria preso.
///
/// AS DECISOES QUE O DONO NAO DITOU, e por que:
///   * NAO COBRA NADA (nem Ki, nem zeni): e o trabalho do cargo. A recarga e a de todo salto de planeta (`RecargaDoSaltoMs`), e as
///     portas tambem (`PodeSaltarDePlaneta`: caido, atordoado, treinando, em transe, agarrado, selado, com um raio na mao, preso na
///     Sala do Tempo, dentro de um Majin, esperando no torneio) -- e o morto, que sai do Outro Mundo pagando e nao por conta propria;
///   * DE ONDE NAO SE CHEGA POR TELEPORTE NAO SE SAI por ela (a Sala do Tempo, a mente, o interior do Majin, o bolso do selo): a
///     volta seria pra dentro deles, e a porta deles cobra o que um teleporte pularia (`ZonaFechadaAoTeleporte`);
///   * QUEM PILOTA DESEMBARCA antes, e quem esta fora do corpo (o leme) volta pro corpo: a nave segue o piloto pra qualquer zona, e
///     sem isto o pod iria junto pro Outro Mundo -- a mesma linha da volta do passe do Enma;
///   * O LUGAR QUE SUMIU (o planeta explodiu, a nave foi destruida, o mapa saiu do servidor) devolve o juiz ao berco, com a frase;
///   * QUEM PERDEU O TRONO DO LADO DE LA AINDA VOLTA: a volta guardada e dele (o fato `VoltaDoOutroMundo` mantem o botao). Ir de
///     novo, so com o trono;
///   * O CARIMBO DO JUIZ (`Go_To_Heaven_Or_Hell`) TAMBEM GUARDA A VOLTA quando sai do mundo dos vivos -- senao quem foi pelo carimbo
///     voltaria pro berco, e nao pro lugar de onde saiu.
/// ==========================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--juizteste`): a volta cai no berco, e nao no lugar exato de onde o juiz saiu -- o "volta pra
	/// casa" de toda saida paga do Outro Mundo. Lido pela <see cref="VoltarDoOutroMundoComoJuiz"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool VoltaDoJuizPeloBercoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--juizteste`): quem volta a vida DENTRO do Outro Mundo e puxado pra perto de quem o reviveu, como
	/// antes -- e fica vivo e preso la. Lido pelo `RessuscitarG4`. Falso em jogo, sempre.
	/// </summary>
	internal static bool RevividoFicaNoAlemDeTeste;

	/// <summary>O canal de habilidade da travessia. Devolve se o id era o dela.</summary>
	private bool UsarVerboDoJuiz(ServerPlayer pl, string id)
	{
		if (!string.Equals(id, TravessiaDoJuiz.Verbo, StringComparison.Ordinal)) return false;
		AtravessarOOutroMundo(pl);
		return true;
	}

	/// <summary>Este corpo carrega o trono do King Yemma agora? -- pela CONTA, como todo cargo (`GameServer.Ranks.cs`).</summary>
	private bool EhOJuiz(ServerPlayer pl) =>
		pl.Conta.Length > 0 && string.Equals(CargoDe(pl.Conta), TravessiaDoJuiz.Cargo, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// HA UMA VOLTA PRA ESTE CORPO AGORA: vivo, num lugar de morto, com o lugar guardado na ficha. E o fato que mantem o botao de quem
	/// perdeu o trono do lado de la (`Fatos.VoltaDoOutroMundo`), e a pergunta que a travessia faz antes de pedir o trono.
	/// </summary>
	internal static bool TemAVoltaDoJuiz(ServerPlayer pl) =>
		!pl.Ficha.dead && Alem.EhOAlem(pl.Zone) && pl.Ficha.VoltaDoJuizZona.Length > 0;

	/// <summary>O APERTO: no mundo dos vivos vai, no Outro Mundo volta.</summary>
	private void AtravessarOOutroMundo(ServerPlayer pl)
	{
		bool volta = TemAVoltaDoJuiz(pl);
		if (!volta && !EhOJuiz(pl))
		{
			Avisar(pl, "só o King Yemma atravessa por conta própria entre o mundo dos vivos e o Outro Mundo.");
			return;
		}
		if (!PodeSaltarDePlaneta(pl, out string porque)) { Avisar(pl, porque); return; }
		if (EmEspera(pl, _saltoPronto, "o espaco em volta de voce ainda esta se recompondo")) return;

		if (Alem.EhOAlem(pl.Zone)) VoltarDoOutroMundoComoJuiz(pl);
		else IrProOutroMundoComoJuiz(pl);
	}

	/// <summary>A IDA: guarda o lugar de agora e leva o juiz a mesa do Enma, onde os mortos chegam.</summary>
	private void IrProOutroMundoComoJuiz(ServerPlayer pl)
	{
		if (ZonaFechadaAoTeleporte(pl.Zone))
		{
			Avisar(pl, "daqui não: a volta pra cá não se faz por teleporte. Saia pela porta antes de atravessar.");
			return;
		}

		// QUEM ESTA FORA DO CORPO VOLTA PRO CORPO, E QUEM PILOTA DESEMBARCA -- as duas linhas da volta do passe do Enma: a nave segue
		// o piloto pra qualquer zona, e o pod iria junto pro Outro Mundo.
		if (pl.BonecoLargado != null) VoltarDeOndeEstiver(pl, "");
		if (NaveDoPiloto(pl) is { } nave) Desembarcar(pl, nave);

		GuardarAVoltaDoJuiz(pl);
		Falar(pl, Protocol.Fala.Emote, "fecha o livro dos vivos e atravessa pro Outro Mundo.");
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		MoveToZone(pl.Id, alem, MesaDoEnma(alem));
		_saltoPronto[pl.Id] = NowMs() + RecargaDoSaltoMs;
		Persistir(pl);

		Avisar(pl, "você atravessa pro Outro Mundo, na mesa do Enma, onde as almas chegam. Aperte de novo pra voltar "
				   + "exatamente pro lugar de onde saiu.");
		GD.Print($"[server] {pl.Name} (juiz) atravessou pro Outro Mundo; a volta e {pl.Ficha.VoltaDoJuizZona} "
				 + $"({pl.Ficha.VoltaDoJuizX:0},{pl.Ficha.VoltaDoJuizY:0})");
	}

	/// <summary>
	/// O LUGAR DE AGORA NA FICHA, nas tres partes da zona e no ponto -- chamado pela ida e pelo carimbo (`JulgarG8`) quando saem do
	/// mundo dos vivos. A ida seguinte por cima apaga a anterior: a volta e sempre pra ultima saida.
	/// </summary>
	private static void GuardarAVoltaDoJuiz(ServerPlayer pl)
	{
		pl.Ficha.VoltaDoJuizZona = pl.Zone.Name;
		pl.Ficha.VoltaDoJuizTipo = pl.Zone.Kind;
		pl.Ficha.VoltaDoJuizSeed = pl.Zone.Seed;
		pl.Ficha.VoltaDoJuizX = pl.Pos.X;
		pl.Ficha.VoltaDoJuizY = pl.Pos.Y;
	}

	/// <summary>
	/// A VOLTA: o lugar exato de onde ele saiu -- ou o berco, quando aquele lugar nao existe mais (ou quando ele foi pro Outro Mundo
	/// por outro caminho e nao ha volta guardada). A volta e GASTA: a ficha esquece o lugar.
	/// </summary>
	private void VoltarDoOutroMundoComoJuiz(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		var zona = new ZoneKey(f.VoltaDoJuizTipo, f.VoltaDoJuizZona, f.VoltaDoJuizSeed);
		bool guardada = f.VoltaDoJuizZona.Length > 0;
		f.VoltaDoJuizZona = "";

		Falar(pl, Protocol.Fala.Emote, "fecha o livro dos mortos e atravessa de volta pro mundo dos vivos.");
		if (guardada && !VoltaDoJuizPeloBercoDeTeste && LugarDaVoltaExiste(zona))
		{
			// O PONTO EXATO, e so ele: uma obra erguida ali enquanto o juiz estava fora o empurraria pro tile livre mais perto (a
			// mesma defesa da mesa do Enma).
			var ponto = new Vec2(f.VoltaDoJuizX, f.VoltaDoJuizY);
			ponto = MapaDaZonaOuCatalogo(zona)?.PontoLivrePerto(ponto) ?? ponto;
			MoveToZone(pl.Id, zona, ponto);
			// NO ESPACO o corpo precisa da vizinhanca de planetas do pedaco onde chegou (o `MandarProBerco` faz o mesmo).
			if (Espaco.EhEspaco(zona)) { pl.ChunkAtual = ChunkId.De(pl.Pos); MandarVizinhanca(pl); }
			Avisar(pl, "você volta pro mundo dos vivos, exatamente onde estava.");
		}
		else
		{
			MandarProBerco(pl);
			Avisar(pl, guardada
				? "o lugar de onde você saiu não existe mais: você volta pro mundo dos vivos em casa."
				: "você não guardou de onde saiu (não foi pela travessia): volta pro mundo dos vivos em casa.");
		}
		_saltoPronto[pl.Id] = NowMs() + RecargaDoSaltoMs;
		Persistir(pl);
		GD.Print($"[server] {pl.Name} (juiz) voltou do Outro Mundo pra {pl.Zone} ({pl.Pos.X:0},{pl.Pos.Y:0})");
	}

	/// <summary>
	/// O LUGAR GUARDADO AINDA RECEBE ALGUEM? A pergunta do login (`OndeEsteCorpoPodeAcordar` e `ResgatarDeInteriorMorto`) feita na
	/// hora da volta: o mapa ainda existe, o planeta nao explodiu, a nave nao foi destruida, o mundo sorteado e deste universo -- e
	/// nao e um lugar onde teleporte nao chega.
	/// </summary>
	private bool LugarDaVoltaExiste(ZoneKey zona)
	{
		if (zona.Name.Length == 0 || Alem.EhOAlem(zona) || ZonaFechadaAoTeleporte(zona) || ZonaMorta(zona)) return false;
		if (Espaco.EhEspaco(zona)) return zona.Equals(ZonaDoEspaco);
		if (NaveGrande.EhInterior(zona, out int nave)) return _naves.Any(n => n.Id == nave);
		if (EhZonaProcedural(zona)) return CorpoGerado.DaZona(SeedDoUniverso, zona) != null;
		if (zona.Kind == ZoneKey.KindPremade) return _catalogo?.Get(zona) != null;
		return zona.Kind == ZoneKey.KindInterior;   // a planta de um interior de arquivo nao some
	}
}
