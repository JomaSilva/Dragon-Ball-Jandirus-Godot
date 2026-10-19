using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--pesosteste` -- OS PESOS COM ESTADO E A GRAVIDADE COM PESO (dono, 2026-10-18):
///   * *"tem um bug q quando vc tem 2 itens do mesmo tipo no inventario o jogo acha q os dois sao o mesmo item"*, *"fiz um peso pra um amigo e
///     quando fui dropar veio com o mesmo peso do meu ate o icone, meio q clona o item"*, *"mas quando ele pega ele mostra 50kg"*;
///   * *"usar peso na gravidade aparentemente n ta deixando a gravidade ser masterizada, mas n temos certeza, investigue e resolva caso tenha
///     um problema real"*.
///
/// Um cliente cru entra pela porta (a maestria de gravidade e de JOGADOR), e cada familia mede pela porta de PRODUCAO, com o contra-exemplo:
///   I. O PESO E UM ITEM -- dois pesos na mesma mochila, um melhorado e com arte, o outro de fabrica: dois nomes, dois maximos, duas artes;
///      o "ajustar" veste o clicado; o largado cai com a cara DELE; o amigo pega o MESMO peso (o maximo e a arte vao junto). Contra:
///      `PesoDeTreino.EstadoForaDoIdDeTeste` (o item sem estado -- o jogo de antes, em que tudo morava na ficha).
///   M. A MIGRACAO -- o save de antes (o maximo e a arte na ficha) entra com os pesos de fabrica levando o que a ficha tinha, o vestido
///      continua vestido, e os campos da ficha zeram; o save sem nada fica como estava.
///   G. A GRAVIDADE COM PESO -- numa gravidade 10x acima da maestria, com os pesos que o corpo aguenta no maximo, o treino cansa em segundos
///      (o vigor pelo QUADRADO do peso, `Train.dm:88`) -- e a maestria sobe do mesmo jeito, a cada passe, como no DM. E parado tambem. Contra:
///      `MaestriaSoNoTreinoDeTeste` (a maestria so no passe do treino -- o jogo de antes, em que o peso a travava).
/// </summary>
public sealed partial class GameServer
{
	private const string ContaDaBancadaDosPesos = "bancada_pesos_ag";

	private bool _pesosRodando, _pesosJaRodou;
	private int _pesosOk, _pesosFalhou;

	private void AfirmarPesos(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _pesosOk++; GD.Print($"[pesos]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _pesosFalhou++; GD.PrintErr($"[pesos]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDosPesos(int porta)
	{
		if (_pesosRodando || _pesosJaRodou) return;
		_pesosRodando = _pesosJaRodou = true;
		_pesosOk = _pesosFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[pesos] ================ OS PESOS COM ESTADO E A GRAVIDADE COM PESO (dono, 2026-10-18) ================");

		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		List<Largado> chao = LargadosDe(ZonaDaBancadaDeProjetil.Hash);
		List<Largado> chaoDeAntes = [.. chao];
		try
		{
			ServerPlayer? pl = mesa.EntraNoMundo(cli, ContaDaBancadaDosPesos, "Halterofilista");
			AfirmarPesos("(montagem) o cliente entra pela porta e e jogador; a Terra tem mapa; ha catalogo de aparencia",
					  pl != null && EhJogador(pl) && _pjMapa != null && _visual != null);
			if (pl == null || _pjMapa == null || _visual == null) return;
			if (pl.Zone.Hash != ZonaDaBancadaDeProjetil.Hash) MoveToZone(pl.Id, ZonaDaBancadaDeProjetil, PontoDeNascimento(ZonaDaBancadaDeProjetil));

			OPesoEUmItem(chao);
			AMigracaoDosPesos();
			AGravidadeComPeso(pl);
		}
		catch (Exception e)
		{
			AfirmarPesos("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			PesoDeTreino.EstadoForaDoIdDeTeste = false;
			MaestriaSoNoTreinoDeTeste = false;
			chao.Clear();
			chao.AddRange(chaoDeAntes);
			LimparTudoDaBancada();
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaBancadaDosPesos + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch (Exception e) { GD.PushWarning($"[pesos] nao apaguei '{ContaDaBancadaDosPesos}': {e.Message}"); }
			_pesosRodando = false;
			GD.Print($"[pesos] ================ {_pesosOk} OK, {_pesosFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// I) O PESO E UM ITEM
	// =====================================================================
	private void OPesoEUmItem(List<Largado> chao)
	{
		GD.Print("[pesos] -- I) cada peso e um item: dois pesos, dois maximos, duas artes; o que cai e o que se pega e o MESMO peso --");
		string arte = PesoDeTreino.Escolhiveis(_visual!).Find(e => e.Nome != PesoDeTreino.ArtePadrao).Nome ?? "";

		// A CENA DO DONO: ele fabrica dois pesos, melhora o DELE duas vezes, da uma arte a ele e o veste; o outro (o do amigo) fica de fabrica
		// e e largado no chao; o amigo o pega. Tudo pela porta de producao (`item_*`, o `chao_pegar`).
		(string Meu, string DoAmigo, string NomeMeu, string NomeDoAmigo, string Vestido, string NoChao, string ArteNoChao, string Pegou) Cena()
		{
			chao.Clear();
			Vec2 aqui = TerraFirmeG12(6);
			ServerPlayer eu = ForjarV2("Ferreiro", aqui, 50_000);
			ServerPlayer amigo = ForjarV2("Amigo", aqui + new Vec2(ZoneCollision.TileSize, 0), 50_000);
			eu.Mochila.Guardar(CatalogoDeItens.Pesos, 2);
			eu.Ficha.Zeni = 1_000_000;
			string meu = eu.Mochila.Pilhas[0].Id;
			ComandoDeItem(eu, "item_melhorar", meu);
			meu = eu.Mochila.Pilhas[0].Id;
			ComandoDeItem(eu, "item_melhorar", meu);
			meu = eu.Mochila.Pilhas[0].Id;
			ComandoDeItem(eu, $"item_{PesoDeTreino.AcaoArte}", $"{meu}/{arte}");
			meu = eu.Mochila.Pilhas[0].Id;
			ComandoDeItem(eu, "item_ajustar", $"{meu}/50");
			string doAmigo = eu.Mochila.Pilhas[1].Id;
			string nomeMeu = CatalogoDeItens.Get(meu)?.Nome ?? "", nomeDoAmigo = CatalogoDeItens.Get(doAmigo)?.Nome ?? "";

			ComandoDeItem(eu, "item_largar", doAmigo);
			Largado? noChao = chao.FindLast(l => PesoDeTreino.EhPeso(l.Item));
			Verbo(amigo, Chao.VerboPegar, "");
			string pegou = amigo.Mochila.Pilhas.Find(p => PesoDeTreino.EhPeso(p.Id)).Id ?? "";
			var r = (meu, doAmigo, nomeMeu, nomeDoAmigo, eu.Ficha.pesoVestido, noChao?.Item ?? "", noChao?.Arte ?? "", pegou);
			RemoverNpc(eu);
			RemoverNpc(amigo);
			return r;
		}

		var c = Cena();
		AfirmarPesos("**DOIS PESOS, DOIS ITENS** (*\"quando vc tem 2 itens do mesmo tipo no inventario o jogo acha q os dois sao o mesmo item\"*): o "
				  + "melhorado leva o maximo dele (200 kg) e a arte dele no id, e o outro continua o de fabrica -- dois nomes na mochila",
				  c.Meu == PesoDeTreino.IdDe(200, arte) && c.DoAmigo == CatalogoDeItens.Pesos && c.NomeMeu != c.NomeDoAmigo
				  && c.NomeMeu.Contains("200 kg") && c.NomeDoAmigo.Contains("50 kg"),
				  $"o meu '{c.Meu}' ({c.NomeMeu}); o do amigo '{c.DoAmigo}' ({c.NomeDoAmigo})");
		AfirmarPesos("...o \"ajustar\" veste o CLICADO (o id dele vai pra ficha), e e o maximo dele que conta", c.Vestido == c.Meu, $"vestido '{c.Vestido}'");
		AfirmarPesos("**O PESO DO AMIGO CAI COM A CARA DELE** (*\"veio com o mesmo peso do meu ate o icone\"*): o largado e o de fabrica, sem a arte "
				  + "do meu -- e o amigo pega o MESMO peso (*\"quando ele pega ele mostra 50kg\"*: agora e porque ele E de 50 kg)",
				  c.NoChao == CatalogoDeItens.Pesos && c.ArteNoChao.Length == 0 && c.Pegou == CatalogoDeItens.Pesos,
				  $"no chao '{c.NoChao}' com arte '{c.ArteNoChao}'; o amigo pegou '{c.Pegou}'");

		// E O MELHORADO, dado de presente: vai com o maximo e a arte
		chao.Clear();
		Vec2 la = TerraFirmeG12(6);
		ServerPlayer doador = ForjarV2("Doador", la, 50_000);
		ServerPlayer ganhou = ForjarV2("Presenteado", la + new Vec2(ZoneCollision.TileSize, 0), 50_000);
		string presente = PesoDeTreino.IdDe(800, arte);
		doador.Mochila.Guardar(presente);
		ComandoDeItem(doador, "item_largar", presente);
		Largado? caiu = chao.FindLast(l => PesoDeTreino.EhPeso(l.Item));
		Verbo(ganhou, Chao.VerboPegar, "");
		string recebido = ganhou.Mochila.Pilhas.Find(p => PesoDeTreino.EhPeso(p.Id)).Id ?? "";
		AfirmarPesos("...e o peso MELHORADO dado de presente vai inteiro: cai com a arte DELE e chega com os 800 kg e a arte -- a melhoria paga "
				  + "nao fica pra tras", caiu?.Arte == PesoDeTreino.Folha(arte, _visual) && recebido == presente
				  && PesoDeTreino.MaximoDe(recebido) == 800 && PesoDeTreino.ArteDe(recebido) == arte,
				  $"no chao '{caiu?.Item}' ('{caiu?.Arte}'); recebeu '{recebido}'");
		RemoverNpc(doador);
		RemoverNpc(ganhou);

		PesoDeTreino.EstadoForaDoIdDeTeste = true;
		(string Meu, string DoAmigo, string NomeMeu, string NomeDoAmigo, string Vestido, string NoChao, string ArteNoChao, string Pegou) semEstado;
		try { semEstado = Cena(); }
		finally { PesoDeTreino.EstadoForaDoIdDeTeste = false; }
		AfirmarPesos("(defeito injetado: o item sem estado, o jogo de antes) os dois pesos sao o MESMO item -- o mesmo nome, e a melhoria nao "
				  + "fica em nenhum dos dois -- e a regua de cima REPROVA",
				  semEstado.Meu == CatalogoDeItens.Pesos && semEstado.DoAmigo == CatalogoDeItens.Pesos && semEstado.NomeMeu == semEstado.NomeDoAmigo,
				  $"o meu '{semEstado.Meu}', o do amigo '{semEstado.DoAmigo}'");
		chao.Clear();
	}

	// =====================================================================
	// M) A MIGRACAO DO SAVE DE ANTES
	// =====================================================================
	private void AMigracaoDosPesos()
	{
		GD.Print("[pesos] -- M) o save de antes: o maximo e a arte da ficha vao pros pesos de fabrica da mochila --");
		string arte = PesoDeTreino.Escolhiveis(_visual!).Find(e => e.Nome != PesoDeTreino.ArtePadrao).Nome ?? "";

		(List<string> Mochila, string Vestido, double MaxNaFicha, string ArteNaFicha, double Weighted) Carregar(double max, string arteDaFicha, double weighted)
		{
			var mochila = new Inventario();
			mochila.Guardar(CatalogoDeItens.Pesos, 2);
			mochila.Guardar(CatalogoDeItens.Maca, 3);
			var s = new CharacterSave
			{
				Nome = "Veterano", Raca = "Human", Mochila = mochila,
				Ficha = new Fighter { Race = "Human", BP = 10_000, pesoMaximoKg = max, pesoArte = arteDaFicha, Weighted = weighted },
			};
			var corpo = new ServerPlayer { Id = 0, Name = "Veterano" };
			AccountStore.ParaJogador(s, corpo);
			return ([.. corpo.Mochila.Pilhas.Select(p => p.Id)], corpo.Ficha.pesoVestido, corpo.Ficha.pesoMaximoKg, corpo.Ficha.pesoArte, corpo.Ficha.Weighted);
		}

		var velho = Carregar(400, arte, 120);
		string esperado = PesoDeTreino.IdDe(400, arte);
		AfirmarPesos("**O SAVE DE ANTES ENTRA COM OS PESOS DELE**: o maximo (400 kg) e a arte que moravam na ficha vao pros dois pesos de fabrica "
				  + "da mochila, o vestido continua vestido (com um deles), e os campos da ficha zeram -- a migracao roda uma vez",
				  velho.Mochila.Count(i => i == esperado) == 2 && velho.Vestido == esperado && velho.MaxNaFicha == 0 && velho.ArteNaFicha.Length == 0
				  && Math.Abs(velho.Weighted - 120) < 1e-9,
				  $"mochila [{string.Join(", ", velho.Mochila)}]; vestido '{velho.Vestido}'; ficha {velho.MaxNaFicha} / '{velho.ArteNaFicha}'");

		var nada = Carregar(0, "", 0);
		AfirmarPesos("...e o save sem nada pago fica como estava: os pesos de fabrica, nada vestido",
				  nada.Mochila.Count(i => i == CatalogoDeItens.Pesos) == 2 && nada.Vestido.Length == 0, $"mochila [{string.Join(", ", nada.Mochila)}]");
	}

	// =====================================================================
	// G) A GRAVIDADE COM PESO
	// =====================================================================
	private void AGravidadeComPeso(ServerPlayer pl)
	{
		GD.Print("[pesos] -- G) a gravidade com peso: o treino cansa em segundos, e a maestria sobe a cada passe do mesmo jeito --");
		Fighter f = pl.Ficha;
		double maestriaGuardada = f.GravMastered, gravGuardada = f.Planetgrav, gmultGuardado = f.gravmult;

		// UM PASSE DE FICHA (o `Treinar` de producao, 5 Hz) por vez, `passes` vezes, numa gravidade 10x com a maestria em 1. Devolve quanto
		// a maestria subiu, e em quantos passes o treino ainda estava ligado.
		(double Subiu, int Treinando) Passes(int passes, bool treinar, bool comPeso)
		{
			f.Planetgrav = 10;
			f.gravmult = 0;
			f.GravMastered = 1;
			pl.Combate.LutandoDeVerdade = pl.Combate.EmCombate = pl.Combate.BrigandoComAlguem = 0;
			f.IsInFight = false;
			f.Tick(agoraMs: NowMs());
			string pesos = PesoDeTreino.IdDe(f.weight_cap_hw * GainKnobs.WeightItemCapMult, "");
			pl.Mochila.Guardar(pesos);
			if (comPeso) AjustarPeso(pl, pesos, "100");
			f.stamina = f.maxstamina;
			f.train = treinar;
			f.med = false;
			double antes = f.GravMastered;
			int treinando = 0;
			for (int i = 0; i < passes; i++)
			{
				Treinar(pl);
				if (f.train) treinando++;
			}
			double subiu = f.GravMastered - antes;
			f.train = false;
			AjustarPeso(pl, pesos, "0");
			pl.Mochila.Tirar(pesos);
			return (subiu, treinando);
		}

		try
		{
			var comPeso = Passes(60, treinar: true, comPeso: true);
			var parado = Passes(60, treinar: false, comPeso: false);
			MaestriaSoNoTreinoDeTeste = true;
			(double Subiu, int Treinando) deAntes;
			try { deAntes = Passes(60, treinar: true, comPeso: true); }
			finally { MaestriaSoNoTreinoDeTeste = false; }

			AfirmarPesos("(montagem) com os pesos no maximo numa gravidade 10x, o treino CANSA em poucos passes (o vigor pelo quadrado do peso) -- o "
					  + "cenario do relato", comPeso.Treinando < 30, $"treinando em {comPeso.Treinando} de 60 passes");
			AfirmarPesos("**A MAESTRIA DE GRAVIDADE SOBE COM PESO** (*\"usar peso na gravidade aparentemente n ta deixando a gravidade ser "
					  + "masterizada\"*): os 60 passes (12 s) contam todos, com o treino parado de cansaco ou nao -- como no DM (`Gravity.dm:30-33`)",
					  comPeso.Subiu > 0 && deAntes.Subiu > 0 && comPeso.Subiu > deAntes.Subiu * 1.8,
					  $"subiu {comPeso.Subiu:0.#####}; no jogo de antes {deAntes.Subiu:0.#####}");
			AfirmarPesos("...e parado na gravidade tambem: o corpo se acostuma estando nela (o esmagamento e o preco)", parado.Subiu > 0,
					  $"subiu {parado.Subiu:0.#####}");
			AfirmarPesos("(defeito injetado: a maestria so no passe do treino, o jogo de antes) o treino com peso para de cansaco e a maestria para "
					  + "junto -- a regua de cima REPROVA", deAntes.Treinando < 30 && deAntes.Subiu < comPeso.Subiu * 0.6,
					  $"treinando {deAntes.Treinando} de 60; subiu {deAntes.Subiu:0.#####}");
		}
		finally
		{
			f.GravMastered = maestriaGuardada;
			f.Planetgrav = gravGuardada;
			f.gravmult = gmultGuardado;
			f.train = f.med = false;
		}
	}
}
