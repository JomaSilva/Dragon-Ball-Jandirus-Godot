using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Items;
using Jandirus.Core.Magic;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--esferamochilateste` -- AS ESFERAS DO DRAGAO NA MOCHILA (dono, 2026-10-19). O pedido inteiro esta no cabecalho de
/// `GameServer.EsferasNaMochila.cs`; cada familia daqui mede uma frase dele, pela porta de PRODUCAO (o verbo, o tique, o
/// `MoveToZone`), e le o que o cliente leria: o `S2C.Inventario` que saiu pro `Peer`, pelo leitor do cliente (`GetInventario`).
///
///   A. PEGAR POE NA MOCHILA -- o slot chega NA HORA, com o sprite do chao, fora dos 30 e fora do save.
///   B. PEDRA OU LARANJA -- set apagado, pedra (`inactive`); o set que acorda com ela na mao troca pela laranja no tique.
///   C. LARGAR -- o slot larga AQUELA esfera aos pes (as outras ficam); o "Largar tudo" larga todas.
///   D. NOCAUTE E MORTE DERRUBAM -- e a mochila acompanha no mesmo tique.
///   E. DENTRO DO PLANETA -- a caverna e o Lookout sao da Terra pelo caminho das passagens; a esfera entra, fica na mao, segue o
///      corpo, e largada la dentro fica la.
///   F. AS PORTAS RECUSAM -- decolar, romper a atmosfera voando, o pod, a Capital Ship, o teleporte; e quem pilota nao pega.
///   G. A REDE -- a viagem que ninguem recusa (o admin, a ida pro Outro Mundo) deixa a esfera no chao, onde a pessoa estava.
///   H. A INVOCACAO TIRA DA MOCHILA -- as sete vao pro chao aos pes, o dragao sobe, o pedido as espalha; e fora do mundo do set
///      (na caverna) o dragao nao sobe.
///   I. O LOGOUT -- quem sai do jogo na caverna deixa a esfera na caverna, onde estava.
///
/// Toda regra tem o contra-exemplo "(defeito injetado: ...)": o botao `...DeTeste` lido pela propria linha, e a regua de cima
/// REPROVA com ele. A MORTE E O ESTADO (`Ficha.dead`), e nao o funil inteiro do Outro Mundo: o que derruba e o item 3 do tique, e
/// a viagem pro Alem e a rede da familia G.
/// </summary>
public sealed partial class GameServer
{
	private const string ContaDaBancadaDasEsferasNaMochila = "bancada_esferamochila_ag";

	private bool _emRodando, _emJaRodou;
	private int _emOk, _emFalhou;

	private void AfirmarEm(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _emOk++; GD.Print($"[esferamochila]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _emFalhou++; GD.PrintErr($"[esferamochila]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDasEsferasNaMochila(int porta)
	{
		if (_emRodando || _emJaRodou) return;
		_emRodando = _emJaRodou = true;
		_emOk = _emFalhou = 0;
		GD.Print("[esferamochila] ================ AS ESFERAS DO DRAGAO NA MOCHILA (dono, 2026-10-19) ================");

		var setsGuardados = new List<SetDeEsferas>(_sets);
		var esferasGuardadas = new List<Esfera>(_esferas);
		var navesDaBancada = new List<Nave>();
		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		EscutaDaMochila = [];
		try
		{
			ServerPlayer? pl = mesa.EntraNoMundo(cli, ContaDaBancadaDasEsferasNaMochila, "Carregador");
			AfirmarEm("(montagem) o cliente entra pela porta e e jogador", pl != null && EhJogador(pl));
			if (pl == null || pl.Combate == null) return;

			ZoneKey terra = ZoneKey.Premade("Earth");
			if (!pl.Zone.Equals(terra)) MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
			// O CRIADOR E OUTRO, e de proposito: o item 2 do tique inerta o set cujo criador ONLINE morre -- e a familia D mata o testador.
			var set = new SetDeEsferas
			{
				Id = ProximoIdDeSet(), Ex = pl.Pos.X, Ey = pl.Pos.Y, CriadorSig = "bancada-criador-ausente", CriadorConta = "bancada_criador",
				CriadorNome = "Criador Ausente", Poder = Math.Max(pl.Ficha.BP, 1), Desejos = 1,
			};
			set.PorZona(terra);
			_sets.Add(set);
			RefazerAsEsferas(set);   // as sete, pelo funil de producao
			set.AtivoEm = TempoDoMundo - 1;
			AfirmarEm("(montagem) um set de jogador na Terra, as sete no chao, acordado", pl.Zone.Equals(terra)
					  && _esferas.Count(e => e.Set == set.Id) == Esferas.Total && SetAtivo(set), $"zona {pl.Zone.Name}");

			EmA_PegarPoeNaMochila(pl, set);
			EmB_PedraOuLaranja(pl, set);
			EmC_Largar(pl, set);
			EmD_NocauteEMorte(pl, set);
			EmE_DentroDoPlaneta(pl, set);
			EmF_AsPortas(pl, set, navesDaBancada);
			EmG_ARede(pl, set);
			EmH_AInvocacao(pl, set);
			EmI_OLogout(pl, set, mesa, cli);
		}
		catch (Exception e)
		{
			AfirmarEm("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			EsferaForaDaMochilaDeTeste = EsferaSempreAcesaNaMochilaDeTeste = SlotDaEsferaNaoLargaDeTeste = false;
			MochilaNaoAcompanhaAsEsferasDeTeste = InteriorNaoEDoPlanetaDeTeste = EsferaNaoAcompanhaOPortadorDeTeste = false;
			EsferaNaoPrendeDeTeste = EsferaSaiDoPlanetaDeTeste = InvocacaoDeixaNaMochilaDeTeste = false;
			DragaoSobeForaDoSetDeTeste = PegaDeDentroDaNaveDeTeste = false;
			EscutaDaMochila = null;
			foreach (Nave n in navesDaBancada) _naves.Remove(n);
			_invocacoes.Clear();
			_sets.Clear(); _sets.AddRange(setsGuardados);
			_esferas.Clear(); _esferas.AddRange(esferasGuardadas);
			SalvarEsferas();
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaBancadaDasEsferasNaMochila + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch (Exception e) { GD.PushWarning($"[esferamochila] nao apaguei '{ContaDaBancadaDasEsferasNaMochila}': {e.Message}"); }
			_emRodando = false;
			GD.Print($"[esferamochila] ================ {_emOk} OK, {_emFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// AS PECAS DA BANCADA
	// =====================================================================
	/// <summary>
	/// O ULTIMO `S2C.Inventario` QUE SAIU PRA ESTE CORPO, lido pelo leitor do cliente: os itens e os slots de esfera (na ordem em que
	/// chegaram). Nulo se nenhum saiu desde o ultimo `Clear` da escuta.
	/// </summary>
	private (Inventario Mochila, List<EsferaNaMochila> Esferas)? UltimaMochilaQueSaiu(ServerPlayer pl)
	{
		if (EscutaDaMochila is not { } escuta) return null;
		for (int i = escuta.Count - 1; i >= 0; i--)
		{
			if (escuta[i].Para != pl.Id) continue;
			var r = new NetDataReader(escuta[i].Fio);
			r.GetByte();   // o opcode
			Inventario inv = r.GetInventario(new List<PecaDeRoupa>());
			return (inv, [.. inv.Pilhas.Select(p => EsferaNaMochila.Ler(p.Id)).OfType<EsferaNaMochila>()]);
		}
		return null;
	}

	/// <summary>O que o ultimo pacote mostrava: os numeros das esferas e se eram pedra -- o rodape das provas.</summary>
	private string ResumoDaMochila(ServerPlayer pl) =>
		UltimaMochilaQueSaiu(pl) is { } m
			? $"{m.Mochila.Pilhas.Count(p => EsferaNaMochila.Ler(p.Id) == null)} itens + esferas [{string.Join(",", m.Esferas.Select(e => $"{e.Numero}{(e.Apagada ? "p" : "a")}"))}]"
			: "nenhum pacote";

	private Esfera EsferaDoSet(SetDeEsferas s, int numero) => _esferas.First(e => e.Set == s.Id && e.Numero == numero);

	/// <summary>A esfera no chao, aos pes deste corpo -- pro verbo de pegar ter o que pegar.</summary>
	private static void PorAosPes(Esfera e, ServerPlayer pl)
	{
		e.Portador = 0;
		e.PorZona(pl.Zone);
		e.X = pl.Pos.X;
		e.Y = pl.Pos.Y;
	}

	/// <summary>
	/// TODAS NO CHAO DA TERRA, LONGE DO CORPO (fora do alcance de pegar e de invocar): cada cena poe aos pes so as que vai usar. A
	/// zona e a do set, entao a policia de planeta nao as mexe.
	/// </summary>
	private void Estacionar(SetDeEsferas s, ServerPlayer pl)
	{
		var longe = new Vec2(pl.Pos.X + 40 * ZoneCollision.TileSize, pl.Pos.Y);
		foreach (Esfera e in _esferas.Where(x => x.Set == s.Id))
		{
			e.Portador = 0;
			e.PorZona(s.Zona);
			e.X = longe.X;
			e.Y = longe.Y;
		}
	}

	private bool AosPes(Esfera e, ServerPlayer pl) =>
		e.Portador == 0 && e.Zona.Hash == pl.Zone.Hash && Math.Abs(e.X - pl.Pos.X) < 1 && Math.Abs(e.Y - pl.Pos.Y) < 1;

	private string OndeEsta(Esfera e) => $"esfera {e.Numero}: portador {e.Portador}, {e.Zona.Name} ({e.X:0},{e.Y:0})";

	// =====================================================================
	// A) PEGAR POE NA MOCHILA
	// =====================================================================
	private void EmA_PegarPoeNaMochila(ServerPlayer pl, SetDeEsferas set)
	{
		GD.Print("[esferamochila] -- A) pegar poe a esfera na mochila: um slot com o sprite do chao, fora dos 30 e fora do save --");
		Estacionar(set, pl);
		Esfera um = EsferaDoSet(set, 1);
		PorAosPes(um, pl);
		int itensAntes = pl.Mochila.Pilhas.Count;

		bool Pega(out List<string> disse)
		{
			EscutaDaMochila!.Clear();
			disse = Ouvir(() => Verbo(pl, "db_pegar", ""));
			return um.Portador == pl.Id && UltimaMochilaQueSaiu(pl) is { } m
				&& m.Esferas.Any(x => x.Set == set.Id && x.Numero == 1 && !x.Apagada);
		}

		bool pegou = Pega(out List<string> falas);
		AfirmarEm("A1. **PEGAR POE NA MOCHILA**: o verbo de pegar manda a mochila NA HORA, com o slot da esfera de 1 estrela (o set, o numero, acesa)",
				  pegou && Disse(falas, "mochila"), $"{OndeEsta(um)}; {ResumoDaMochila(pl)}; {Ultimos(falas)}");

		EsferaNaMochila? slot = UltimaMochilaQueSaiu(pl)?.Esferas.FirstOrDefault();
		ItemDef? ficha = slot == null ? null : CatalogoDeItens.Get(slot.Id);
		AfirmarEm("A2. ...e o slot se desenha como a esfera do chao: o nome, a folha `Dragonballs.dmi` (que existe no disco) e o estado \"1\", a laranja",
				  ficha is { Nome: "Esfera de 1 estrela", Estado: "1" } && ficha.Arte == EsferaNaMochila.ArteDaEsfera(false)
				  && ResourceLoader.Exists(ficha.Arte),
				  $"{ficha?.Nome} | {ficha?.Arte} | estado {ficha?.Estado}");
		AfirmarEm("A3. ...e ela NAO mora na mochila: as pilhas do servidor (o que vai pro save) nao a tem, e os itens do pacote sao os de antes",
				  pl.Mochila.Pilhas.Count == itensAntes && !pl.Mochila.Pilhas.Any(p => EsferaNaMochila.Ler(p.Id) != null)
				  && UltimaMochilaQueSaiu(pl)?.Mochila.Pilhas.Count(p => EsferaNaMochila.Ler(p.Id) == null) == itensAntes,
				  $"pilhas {pl.Mochila.Pilhas.Count} (eram {itensAntes}); {ResumoDaMochila(pl)}");

		PorAosPes(um, pl);
		EsferaForaDaMochilaDeTeste = true;
		bool comDefeito;
		try { comDefeito = Pega(out _); }
		finally { EsferaForaDaMochilaDeTeste = false; }
		AfirmarEm("CONTRA-EXEMPLO (defeito injetado: a esfera nao vai no pacote, o jogo de antes): o slot nao chega -- a regua de A1 REPROVA",
				  !comDefeito, ResumoDaMochila(pl));
	}

	// =====================================================================
	// B) PEDRA OU LARANJA
	// =====================================================================
	private void EmB_PedraOuLaranja(ServerPlayer pl, SetDeEsferas set)
	{
		GD.Print("[esferamochila] -- B) com o set apagado ela vai como pedra; o set que acorda troca pela laranja no tique --");
		Esfera um = EsferaDoSet(set, 1), dois = EsferaDoSet(set, 2);
		if (um.Portador != pl.Id) { PorAosPes(um, pl); Verbo(pl, "db_pegar", ""); }
		set.AtivoEm = TempoDoMundo + 999_999;
		PorAosPes(dois, pl);

		bool SoPedra()
		{
			if (UltimaMochilaQueSaiu(pl) is not { } m || m.Esferas.Count != 2) return false;
			return m.Esferas.All(x => x.Apagada && CatalogoDeItens.Get(x.Id) is { Estado: "inactive" } f && f.Arte == EsferaNaMochila.ArteDaEsfera(false));
		}

		EscutaDaMochila!.Clear();
		Verbo(pl, "db_pegar", "");
		AfirmarEm("B1. **SET APAGADO, PEDRA**: as duas na mao vao como `pedra`, e o desenho e o estado `inactive` da mesma folha",
				  dois.Portador == pl.Id && SoPedra(), ResumoDaMochila(pl));

		EsferaSempreAcesaNaMochilaDeTeste = true;
		bool pedraComDefeito;
		try
		{
			EscutaDaMochila.Clear();
			MandarMochila(pl, forcar: true);
			pedraComDefeito = SoPedra();
		}
		finally { EsferaSempreAcesaNaMochilaDeTeste = false; }
		AfirmarEm("CONTRA-EXEMPLO (defeito injetado: a esfera vai acesa mesmo com o set apagado): a pedra some -- a regua de B1 REPROVA",
				  !pedraComDefeito, ResumoDaMochila(pl));
		MandarMochila(pl, forcar: true);

		set.AtivoEm = TempoDoMundo - 1;
		EscutaDaMochila.Clear();
		TickDasEsferas();
		bool acesas = UltimaMochilaQueSaiu(pl) is { } m2 && m2.Esferas.Count == 2 && m2.Esferas.All(x => !x.Apagada)
					  && m2.Esferas.All(x => CatalogoDeItens.Get(x.Id)?.Estado == x.Numero.ToString());
		AfirmarEm("B2. **O SET ACORDA COM ELAS NA MAO**: o tique manda a mochila sem gesto nenhum, e a pedra vira a laranja (estados \"1\" e \"2\")",
				  acesas && um.Portador == pl.Id && dois.Portador == pl.Id, ResumoDaMochila(pl));
	}

	// =====================================================================
	// C) LARGAR
	// =====================================================================
	private void EmC_Largar(ServerPlayer pl, SetDeEsferas set)
	{
		GD.Print("[esferamochila] -- C) o slot larga AQUELA esfera aos pes; o \"Largar tudo\" larga todas --");
		Esfera um = EsferaDoSet(set, 1), dois = EsferaDoSet(set, 2);
		AfirmarEm("(montagem) as duas na mao, acesas", um.Portador == pl.Id && dois.Portador == pl.Id && SetAtivo(set), $"{OndeEsta(um)}; {OndeEsta(dois)}");
		string idDoDois = new EsferaNaMochila(set.Id, 2, Apagada: false, DeNamek: false).Id;

		bool LargaODois(out List<string> disse)
		{
			EscutaDaMochila!.Clear();
			disse = Ouvir(() => Verbo(pl, "item_largar", idDoDois));
			return AosPes(dois, pl) && um.Portador == pl.Id && UltimaMochilaQueSaiu(pl) is { } m
				&& m.Esferas.Count == 1 && m.Esferas[0].Numero == 1;
		}

		bool largou = LargaODois(out List<string> falas);
		AfirmarEm("C1. **O SLOT LARGA**: o \"Largar no chão\" da esfera de 2 estrelas a poe aos pes de quem a carregava; a de 1 fica, e a mochila vai so com ela",
				  largou && Disse(falas, "no chão"), $"{OndeEsta(dois)}; {OndeEsta(um)}; {ResumoDaMochila(pl)}; {Ultimos(falas)}");

		Verbo(pl, "db_pegar", "");
		SlotDaEsferaNaoLargaDeTeste = true;
		bool comDefeito;
		List<string> falasComDefeito;
		try { comDefeito = LargaODois(out falasComDefeito); }
		finally { SlotDaEsferaNaoLargaDeTeste = false; }
		AfirmarEm("CONTRA-EXEMPLO (defeito injetado: o slot cai no caminho dos itens, que nao a tem): ela fica na mao -- a regua de C1 REPROVA",
				  !comDefeito && dois.Portador == pl.Id, $"{OndeEsta(dois)}; {Ultimos(falasComDefeito)}");

		EscutaDaMochila!.Clear();
		Verbo(pl, "db_largar", "");
		AfirmarEm("C2. ...e o \"Largar tudo\" (aba World) poe as duas no chao, e a mochila vai sem esfera NA HORA",
				  AosPes(um, pl) && AosPes(dois, pl) && UltimaMochilaQueSaiu(pl) is { Esferas.Count: 0 }, $"{OndeEsta(um)}; {OndeEsta(dois)}; {ResumoDaMochila(pl)}");
	}

	// =====================================================================
	// D) NOCAUTE E MORTE DERRUBAM
	// =====================================================================
	private void EmD_NocauteEMorte(ServerPlayer pl, SetDeEsferas set)
	{
		GD.Print("[esferamochila] -- D) nocauteado ou morto, a esfera cai onde o corpo esta, e a mochila acompanha no tique --");
		Esfera um = EsferaDoSet(set, 1);
		Estacionar(set, pl);

		bool CaiENaoFicaNaMochila(Action derrubar, Action levantar)
		{
			PorAosPes(um, pl);
			Verbo(pl, "db_pegar", "");
			bool naMao = um.Portador == pl.Id;
			pl.Pos = new Vec2(pl.Pos.X + 2 * ZoneCollision.TileSize, pl.Pos.Y);   // longe do ponto em que a pegou
			EscutaDaMochila!.Clear();
			try
			{
				derrubar();
				TickDasEsferas();
				return naMao && AosPes(um, pl) && UltimaMochilaQueSaiu(pl) is { Esferas.Count: 0 };
			}
			finally { levantar(); }
		}

		void Nocauteia() => Nocautear(pl);
		void Levanta() { pl.Combate!.Levantar(); pl.Ficha.KO = false; }

		AfirmarEm("D1. **NOCAUTEADO, DERRUBA**: a esfera cai onde o corpo esta (e nao onde foi pega), e a mochila vai sem ela no mesmo tique",
				  CaiENaoFicaNaMochila(Nocauteia, Levanta), $"{OndeEsta(um)}; corpo ({pl.Pos.X:0},{pl.Pos.Y:0}); {ResumoDaMochila(pl)}");

		MochilaNaoAcompanhaAsEsferasDeTeste = true;
		bool comDefeito;
		try { comDefeito = CaiENaoFicaNaMochila(Nocauteia, Levanta); }
		finally { MochilaNaoAcompanhaAsEsferasDeTeste = false; }
		AfirmarEm("CONTRA-EXEMPLO (defeito injetado: o tique nao acerta a mochila de quem perdeu a esfera): o slot fica na tela -- a regua de D1 REPROVA",
				  !comDefeito, ResumoDaMochila(pl));
		MandarMochila(pl, forcar: true);

		AfirmarEm("D2. **MORTO, DERRUBA**: o mesmo funil -- a esfera cai onde o corpo esta, e a mochila vai sem ela",
				  CaiENaoFicaNaMochila(() => pl.Ficha.dead = true, () => pl.Ficha.dead = false), $"{OndeEsta(um)}; {ResumoDaMochila(pl)}");
	}

	// =====================================================================
	// E) DENTRO DO PLANETA
	// =====================================================================
	private void EmE_DentroDoPlaneta(ServerPlayer pl, SetDeEsferas set)
	{
		GD.Print("[esferamochila] -- E) a caverna e o Lookout sao da Terra: a esfera entra, segue o corpo e, largada la, fica la --");
		ZoneKey terra = set.Zona, caverna = ZoneKey.Premade("Earth_Cave"), lookout = ZoneKey.Premade("Lookout");

		HashSet<string> daTerra = InterioresDoPlaneta("Earth"), deVegeta = InterioresDoPlaneta("Vegeta"), deNamek = InterioresDoPlaneta("Namek");
		AfirmarEm("E0. o caminho das passagens: a caverna e o Lookout sao da Terra; a caverna de Vegeta e o mesmo Lookout (o castelo) sao de Vegeta; "
				+ "Namek nao tem interior; a Sala do Tempo e de ninguem",
				  daTerra.SetEquals(["Earth_Cave", "Lookout"]) && deVegeta.SetEquals(["Vegeta_Cave", "Lookout"]) && deNamek.Count == 0
				  && !daTerra.Contains("Hyperbolic_Time_Chamber"),
				  $"Terra [{string.Join(",", daTerra)}] Vegeta [{string.Join(",", deVegeta)}] Namek [{string.Join(",", deNamek)}]");

		Passagem? praCaverna = _passagens.GetValueOrDefault("Earth")?.Find(p => p.Zona == "Earth_Cave");
		Passagem? praTerra = _passagens.GetValueOrDefault("Earth_Cave")?.Find(p => p.Zona == "Earth");
		Passagem? proLookout = _passagens.GetValueOrDefault("Earth")?.Find(p => p.Zona == "Lookout");
		AfirmarEm("(montagem) a Terra tem boca pra caverna e pro Lookout, e a caverna tem boca pra Terra", praCaverna != null && praTerra != null && proLookout != null);
		if (praCaverna == null || praTerra == null || proLookout == null) return;
		Vec2 naCaverna = ChegadaDaPassagem("Earth", praCaverna), naTerra = pl.Pos, noLookout = ChegadaDaPassagem("Earth", proLookout);

		Esfera um = EsferaDoSet(set, 1);
		Estacionar(set, pl);
		PorAosPes(um, pl);
		Verbo(pl, "db_pegar", "");

		bool EntraNaCavernaComEla()
		{
			MoveToZone(pl.Id, caverna, naCaverna);   // o gesto da boca de passagem (`TickDasPassagens`)
			return pl.Zone.Equals(caverna) && um.Portador == pl.Id;
		}

		AfirmarEm("E1. **ENTRA NA CAVERNA COM ELA**: a passagem nao a derruba -- a caverna e da Terra", EntraNaCavernaComEla(), OndeEsta(um));

		TickDasEsferas();
		bool SegueOCorpo() => um.Portador == pl.Id && um.Zona.Equals(caverna) && Math.Abs(um.X - pl.Pos.X) < 1 && Math.Abs(um.Y - pl.Pos.Y) < 1;
		AfirmarEm("E2. ...e o tique a deixa na mao, com a zona e o ponto de quem a carrega (o radar a ve la dentro)", SegueOCorpo(),
				  $"{OndeEsta(um)}; corpo ({pl.Pos.X:0},{pl.Pos.Y:0})");

		pl.Pos = new Vec2(pl.Pos.X + 2 * ZoneCollision.TileSize, pl.Pos.Y);
		EsferaNaoAcompanhaOPortadorDeTeste = true;
		bool segueComDefeito;
		try { TickDasEsferas(); segueComDefeito = SegueOCorpo(); }
		finally { EsferaNaoAcompanhaOPortadorDeTeste = false; }
		AfirmarEm("CONTRA-EXEMPLO (defeito injetado: a esfera na mao nao segue o corpo): o ponto fica pra tras -- a regua de E2 REPROVA",
				  !segueComDefeito, OndeEsta(um));
		TickDasEsferas();

		string idDoUm = new EsferaNaMochila(set.Id, 1, Apagada: false, DeNamek: false).Id;
		Verbo(pl, "item_largar", idDoUm);
		Vec2 ondeCaiu = new(um.X, um.Y);
		bool FicaNaCaverna() => um.Portador == 0 && um.Zona.Equals(caverna) && Math.Abs(um.X - ondeCaiu.X) < 1 && Math.Abs(um.Y - ondeCaiu.Y) < 1;
		TickDasEsferas();
		AfirmarEm("E3. **LARGADA NA CAVERNA, FICA NA CAVERNA**: a policia de planeta nao a manda de volta pro ponto da semente", FicaNaCaverna(), OndeEsta(um));

		InteriorNaoEDoPlanetaDeTeste = true;
		bool ficaComDefeito;
		try { TickDasEsferas(); ficaComDefeito = FicaNaCaverna(); }
		finally { InteriorNaoEDoPlanetaDeTeste = false; }
		AfirmarEm("CONTRA-EXEMPLO (defeito injetado: caverna e Lookout nao sao do planeta, o jogo de antes): a policia a leva embora -- a regua de E3 REPROVA",
				  !ficaComDefeito && um.Zona.Equals(terra), OndeEsta(um));

		// de volta a mao, na caverna, e pelo Lookout
		PorAosPes(um, pl);
		Verbo(pl, "db_pegar", "");
		MoveToZone(pl.Id, terra, ChegadaDaPassagem("Earth_Cave", praTerra));
		bool voltou = pl.Zone.Equals(terra) && um.Portador == pl.Id;
		MoveToZone(pl.Id, lookout, noLookout);
		TickDasEsferas();
		AfirmarEm("E4. ...e o mesmo no LOOKOUT: volta da caverna e sobe ao Templo com ela, e o tique a deixa na mao, na zona do Templo",
				  voltou && pl.Zone.Equals(lookout) && um.Portador == pl.Id && um.Zona.Equals(lookout), OndeEsta(um));

		MoveToZone(pl.Id, terra, naTerra);
		InteriorNaoEDoPlanetaDeTeste = true;
		bool entrouComDefeito;
		try { entrouComDefeito = EntraNaCavernaComEla(); }
		finally { InteriorNaoEDoPlanetaDeTeste = false; }
		AfirmarEm("CONTRA-EXEMPLO (o mesmo defeito): a boca da caverna a derruba na Terra -- a regua de E1 REPROVA",
				  !entrouComDefeito && um.Portador == 0 && um.Zona.Equals(terra), OndeEsta(um));
		MoveToZone(pl.Id, terra, naTerra);
		Estacionar(set, pl);
	}

	// =====================================================================
	// F) AS PORTAS RECUSAM
	// =====================================================================
	private void EmF_AsPortas(ServerPlayer pl, SetDeEsferas set, List<Nave> navesDaBancada)
	{
		GD.Print("[esferamochila] -- F) decolar, romper a atmosfera, o pod, a Capital Ship e o teleporte recusam quem carrega; ao leme nao se pega --");
		const float T = ZoneCollision.TileSize;
		ZoneKey terra = set.Zona;
		Vec2 aqui = pl.Pos;
		Esfera um = EsferaDoSet(set, 1), dois = EsferaDoSet(set, 2);

		void NaMaoNaTerra()
		{
			if (!pl.Zone.Equals(terra) || (pl.Pos - aqui).Length > 1) MoveToZone(pl.Id, terra, aqui);
			pl.Voando = false;
			pl.Altitude = 0f;
			if (NaveDoPiloto(pl) is { } n) Desembarcar(pl, n);
			if (um.Portador != pl.Id) { PorAosPes(um, pl); Verbo(pl, "db_pegar", ""); }
		}

		// Um teste de porta: o gesto, e "ficou na Terra com ela na mao, e ouviu por que".
		void Porta(string rotulo, string defeito, Func<List<string>> gesto, Func<bool> ficou)
		{
			NaMaoNaTerra();
			List<string> disse = gesto();
			bool recusou = ficou() && um.Portador == pl.Id && Disse(disse, "não sai de");
			AfirmarEm(rotulo, recusou, $"zona {pl.Zone.Name}; voando {pl.Voando}; piloto {NaveDoPiloto(pl)?.Id}; {OndeEsta(um)}; {Ultimos(disse)}");

			NaMaoNaTerra();
			EsferaNaoPrendeDeTeste = true;
			bool comDefeito;
			try { List<string> d2 = gesto(); comDefeito = ficou() && um.Portador == pl.Id && Disse(d2, "não sai de"); }
			finally { EsferaNaoPrendeDeTeste = false; }
			AfirmarEm($"CONTRA-EXEMPLO (defeito injetado: nenhuma porta recusa quem carrega): {defeito} -- a regua de cima REPROVA", !comDefeito,
					  $"zona {pl.Zone.Name}; piloto {NaveDoPiloto(pl)?.Id}; {OndeEsta(um)}");
		}

		Estacionar(set, pl);
		Porta("F1. **DECOLAR, NAO**: a subida pro espaco recusa, e o corpo fica na Terra com a esfera", "o corpo sobe pro espaco",
			  () => Ouvir(() => Decolar(pl)), () => pl.Zone.Equals(terra));

		Porta("F2. **ROMPER A ATMOSFERA VOANDO, NAO**: no teto, com Ki, poder e voo, o corpo continua no ar sobre a Terra",
			  "o corpo rompe a atmosfera",
			  () =>
			  {
				  pl.Ficha.Ki = Math.Max(pl.Ficha.Ki, 10 * Voo.KiParaEspaco);
				  pl.Ficha.expressedBP = Math.Max(pl.Ficha.expressedBP, 10 * Voo.BpParaEspaco);
				  pl.Ficha.flightability = Math.Max(pl.Ficha.flightability, Voo.HabilidadeNivel1);
				  pl.Voando = true;
				  pl.Altitude = Voo.AlturaMaxima;
				  pl.AvisoDeTetoAte = 0;
				  return Ouvir(() => TentarRomperAAtmosfera(pl));
			  },
			  () => pl.Zone.Equals(terra) && pl.Voando && Voo.PodeRomperAAtmosfera(pl.Ficha.Ki, pl.Ficha.expressedBP, HabilidadeDeVoo(pl)));

		var pod = new Nave { Id = _proximaNaveId++, Tipo = "Spacepod", DonoConta = pl.Conta, DonoNome = pl.Name, ErguidaEm = NowMs() };
		pod.PorZona(terra);
		pod.X = aqui.X;
		pod.Y = aqui.Y;
		_naves.Add(pod);
		navesDaBancada.Add(pod);
		Porta("F3. **O POD, NAO**: o pod aos pes nao abre pra quem carrega (a tecla E)", "ele entra no pod",
			  () => Ouvir(() => ComandoDeInteracao(pl, "nave_usar", "")), () => NaveDoPiloto(pl) == null);

		// AO LEME NAO SE PEGA: quem pilota (sem esfera) nao cata a do chao.
		NaMaoNaTerra();
		PorAosPes(um, pl);
		um.X += 30 * T;   // fora do alcance
		ComandoDeInteracao(pl, "nave_usar", "");
		AfirmarEm("(montagem) sem esfera, o corpo entra no pod", NaveDoPiloto(pl) == pod, $"piloto {NaveDoPiloto(pl)?.Id}");
		bool PegaAoLeme(out List<string> disse)
		{
			PorAosPes(dois, pl);
			disse = Ouvir(() => Verbo(pl, "db_pegar", ""));
			return dois.Portador == 0 && Disse(disse, "de dentro de uma nave");
		}
		AfirmarEm("F4. **AO LEME, NAO SE PEGA**: a esfera aos pes de quem pilota o pod fica no chao", PegaAoLeme(out List<string> aoLeme), $"{OndeEsta(dois)}; {Ultimos(aoLeme)}");
		PegaDeDentroDaNaveDeTeste = true;
		bool pegaComDefeito;
		try { pegaComDefeito = PegaAoLeme(out _); }
		finally { PegaDeDentroDaNaveDeTeste = false; }
		AfirmarEm("CONTRA-EXEMPLO (defeito injetado: quem pilota pega): a esfera vai pro pod -- a regua de F4 REPROVA", !pegaComDefeito, OndeEsta(dois));
		if (NaveDoPiloto(pl) is { } ocupado) Desembarcar(pl, ocupado);
		_naves.Remove(pod);
		Estacionar(set, pl);

		var grande = new Nave
		{
			Id = _proximaNaveId++, Tipo = NaveGrande.Tipo, DonoConta = pl.Conta, DonoNome = pl.Name, ErguidaEm = NowMs(),
			ArmaduraMax = 1000, Armadura = 1000,
		};
		grande.PorZona(terra);
		grande.X = aqui.X + 2 * T;
		grande.Y = aqui.Y;
		_naves.Add(grande);
		navesDaBancada.Add(grande);
		Porta("F5. **A CAPITAL SHIP, NAO**: a porta da nave grande nao deixa embarcar quem carrega", "ele embarca no interior da nave",
			  () => Ouvir(() => ComandoDeInteracao(pl, "nave_embarcar", "")), () => pl.Zone.Equals(terra));
		_naves.Remove(grande);

		Porta("F6. **TELEPORTE, NAO**: a pergunta de todo teleporte (`PorQueNaoSaiDaqui`) diz que a esfera nao sai -- a Transmissao, o salto, o relogio, a carona",
			  "o teleporte passa", () => [PorQueNaoSaiDaqui(pl)], () => true);

		NaMaoNaTerra();
		Estacionar(set, pl);
		MoveToZone(pl.Id, terra, aqui);
	}

	// =====================================================================
	// G) A REDE
	// =====================================================================
	private void EmG_ARede(ServerPlayer pl, SetDeEsferas set)
	{
		GD.Print("[esferamochila] -- G) a viagem que nenhuma porta recusa (o admin, a ida pro Outro Mundo) deixa a esfera no chao, onde estava --");
		ZoneKey terra = set.Zona, alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		Vec2 aqui = pl.Pos;
		Esfera um = EsferaDoSet(set, 1);
		Vec2 noEspaco = CorpoDaZona(terra) is { } corpo ? Espaco.PontoDeDecolagem(corpo) : Vec2.Zero;

		bool FicaNoChao(ZoneKey destino, Vec2 chegada, out List<string> disse)
		{
			if (!pl.Zone.Equals(terra)) MoveToZone(pl.Id, terra, aqui);
			PorAosPes(um, pl);
			Verbo(pl, "db_pegar", "");
			EscutaDaMochila!.Clear();
			disse = Ouvir(() => MoveToZone(pl.Id, destino, chegada));
			bool ok = pl.Zone.Equals(destino) && um.Portador == 0 && um.Zona.Equals(terra)
				&& Math.Abs(um.X - aqui.X) < 1 && Math.Abs(um.Y - aqui.Y) < 1 && UltimaMochilaQueSaiu(pl) is { Esferas.Count: 0 };
			MoveToZone(pl.Id, terra, aqui);
			return ok;
		}

		AfirmarEm("G1. **O ADMIN QUE LEVA PRO ESPACO**: o corpo vai, a esfera fica no chao da Terra onde ele estava, e a mochila vai sem ela",
				  FicaNoChao(ZonaDoEspaco, noEspaco, out List<string> g1) && Disse(g1, "não sai de"), $"{OndeEsta(um)}; {Ultimos(g1)}");
		AfirmarEm("G2. **A IDA PRO OUTRO MUNDO** (a viagem da morte): o mesmo -- ela fica no planeta",
				  FicaNoChao(alem, MesaDoEnma(alem), out List<string> g2), $"{OndeEsta(um)}; {Ultimos(g2)}");

		EsferaSaiDoPlanetaDeTeste = true;
		bool comDefeito;
		try { comDefeito = FicaNoChao(ZonaDoEspaco, noEspaco, out _); }
		finally { EsferaSaiDoPlanetaDeTeste = false; }
		AfirmarEm("CONTRA-EXEMPLO (defeito injetado: a rede do `MoveToZone` solta): a esfera vai pro espaco na mao -- a regua de G1 REPROVA", !comDefeito, OndeEsta(um));
		TickDasEsferas();
		Estacionar(set, pl);
	}

	// =====================================================================
	// H) A INVOCACAO TIRA DA MOCHILA
	// =====================================================================
	private void EmH_AInvocacao(ServerPlayer pl, SetDeEsferas set)
	{
		GD.Print("[esferamochila] -- H) com as sete na mochila, INVOCAR as poe no chao aos pes, o dragao sobe e o pedido as espalha; na caverna, nao --");
		ZoneKey terra = set.Zona;
		Vec2 aqui = pl.Pos;
		List<Esfera> sete = [.. _esferas.Where(e => e.Set == set.Id).OrderBy(e => e.Numero)];

		void AsSeteNaMao()
		{
			FecharAInvocacao(set.Id, "");
			set.AtivoEm = TempoDoMundo - 1;
			set.Pedidos = 0;
			set.Desejos = 1;
			foreach (Esfera e in sete) PorAosPes(e, pl);
			for (int i = 0; i < Esferas.Total; i++) Verbo(pl, "db_pegar", "");
		}

		bool Invoca(out List<string> disse)
		{
			EscutaDaMochila!.Clear();
			disse = Ouvir(() => Verbo(pl, "db_invocar", ""));
			return _invocacoes.ContainsKey(set.Id) && QuantasCarrega(pl.Id) == 0 && sete.All(e => AosPes(e, pl))
				&& UltimaMochilaQueSaiu(pl) is { Esferas.Count: 0 };
		}

		AsSeteNaMao();
		AfirmarEm("(montagem) as sete na mochila, pelo verbo de pegar", QuantasCarrega(pl.Id) == Esferas.Total
				  && UltimaMochilaQueSaiu(pl) is { Esferas.Count: Esferas.Total }, ResumoDaMochila(pl));
		bool subiu = Invoca(out List<string> falas);
		AfirmarEm("H1. **INVOCAR TIRA AS SETE DA MOCHILA**: o dragao sobe, e as sete vao pro chao aos pes de quem invocou (a mochila vai sem elas)",
				  subiu,
				  $"dragao {_invocacoes.ContainsKey(set.Id)}; na mao {QuantasCarrega(pl.Id)}; {string.Join("; ", sete.Select(OndeEsta))}; {ResumoDaMochila(pl)}; {Ultimos(falas)}");

		int cicloAntes = set.Ciclo;
		ContarUmDesejo(set);
		int espalhadas = sete.Count(e => e.Portador == 0 && e.Zona.Equals(terra) && !AosPes(e, pl));
		AfirmarEm("H2. ...e o resto e o de sempre: o pedido tira o dragao, anda o ciclo e as espalha de onde estavam",
				  !_invocacoes.ContainsKey(set.Id) && set.Ciclo == cicloAntes + 1 && espalhadas >= Esferas.Total - 1,
				  $"ciclo {cicloAntes} -> {set.Ciclo}; {espalhadas} espalhadas");

		AsSeteNaMao();
		InvocacaoDeixaNaMochilaDeTeste = true;
		bool comDefeito;
		try { comDefeito = Invoca(out _); }
		finally { InvocacaoDeixaNaMochilaDeTeste = false; }
		AfirmarEm("CONTRA-EXEMPLO (defeito injetado: a invocacao deixa as sete na mochila, o jogo de antes): a regua de H1 REPROVA",
				  !comDefeito && QuantasCarrega(pl.Id) == Esferas.Total, $"na mao {QuantasCarrega(pl.Id)}");

		// NA CAVERNA, COM AS SETE: o dragao nao sobe.
		AsSeteNaMao();
		Passagem? praCaverna = _passagens.GetValueOrDefault("Earth")?.Find(p => p.Zona == "Earth_Cave");
		if (praCaverna == null) { AfirmarEm("(montagem) a boca da caverna pra H3", false); return; }
		MoveToZone(pl.Id, ZoneKey.Premade("Earth_Cave"), ChegadaDaPassagem("Earth", praCaverna));

		bool NaoSobeNaCaverna(out List<string> disse)
		{
			FecharAInvocacao(set.Id, "");
			disse = Ouvir(() => Verbo(pl, "db_invocar", ""));
			return !_invocacoes.ContainsKey(set.Id) && QuantasCarrega(pl.Id) == Esferas.Total && Disse(disse, "céu aberto");
		}

		AfirmarEm("H3. **NA CAVERNA, O DRAGAO NAO SOBE**: as sete juntas la dentro, e a invocacao recusa (ele so se ergue a ceu aberto, na Terra)",
				  NaoSobeNaCaverna(out List<string> h3), $"zona {pl.Zone.Name}; na mao {QuantasCarrega(pl.Id)}; {Ultimos(h3)}");
		DragaoSobeForaDoSetDeTeste = true;
		bool sobeComDefeito;
		try { sobeComDefeito = NaoSobeNaCaverna(out _); }
		finally { DragaoSobeForaDoSetDeTeste = false; }
		AfirmarEm("CONTRA-EXEMPLO (defeito injetado: o dragao sobe fora do mundo do set): ele sobe na caverna -- a regua de H3 REPROVA",
				  !sobeComDefeito, $"dragao {_invocacoes.ContainsKey(set.Id)}");

		FecharAInvocacao(set.Id, "");
		MoveToZone(pl.Id, terra, aqui);
		Estacionar(set, pl);
	}

	// =====================================================================
	// I) O LOGOUT
	// =====================================================================
	private void EmI_OLogout(ServerPlayer pl, SetDeEsferas set, MesaDeClientesCrus mesa, ClienteCruDeTeste cli)
	{
		GD.Print("[esferamochila] -- I) quem sai do jogo na caverna deixa a esfera na caverna, onde estava --");
		Esfera um = EsferaDoSet(set, 1);
		Passagem? praCaverna = _passagens.GetValueOrDefault("Earth")?.Find(p => p.Zona == "Earth_Cave");
		if (praCaverna == null) { AfirmarEm("(montagem) a boca da caverna pra I", false); return; }
		PorAosPes(um, pl);
		Verbo(pl, "db_pegar", "");
		MoveToZone(pl.Id, ZoneKey.Premade("Earth_Cave"), ChegadaDaPassagem("Earth", praCaverna));
		pl.Pos = new Vec2(pl.Pos.X + 2 * ZoneCollision.TileSize, pl.Pos.Y);
		TickDasEsferas();
		Vec2 ultimo = pl.Pos;

		bool saiu = mesa.SaiDoJogo(cli, ContaDaBancadaDasEsferasNaMochila);
		TickDasEsferas();
		AfirmarEm("I1. **O LOGOUT NA CAVERNA**: a conta sai, e a esfera fica no chao da caverna, no ultimo ponto do corpo",
				  saiu && um.Portador == 0 && um.Zona.Equals(ZoneKey.Premade("Earth_Cave"))
				  && Math.Abs(um.X - ultimo.X) < 1 && Math.Abs(um.Y - ultimo.Y) < 1, $"saiu {saiu}; {OndeEsta(um)}; ultimo ({ultimo.X:0},{ultimo.Y:0})");
	}
}
