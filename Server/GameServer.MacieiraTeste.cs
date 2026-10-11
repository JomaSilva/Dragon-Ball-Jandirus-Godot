using System.Text.Json;
using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA SEMENTE DE MACIEIRA (`--macieirateste`).
///
/// O dono (2026-10-10): *"faça com quem coma maça tenha uma chance de spawnar uma semente de arvore de
/// maça no inventario q serve pra plantar uma nova arvore de maça onde quiser (pra n passar fome em
/// planetas procedurais)"*. E o `Seed`/`Plant()` do original (`Stamina/Food.dm:140-143`,
/// `Turfs/Plants.dm:909-953`), portado.
///
/// No boot, como as outras de servidor: precisa das zonas com colisao e de ninguem logado. O corpo e
/// forjado (sem `Peer`) e TUDO passa pelo funil de producao -- comer e o `item_comer` do canal de itens,
/// plantar e o `posicionar` que a previa do cliente manda, e colher e o verbo da tecla E.
///
/// TRES PERGUNTAS:
///   1. A REGRA: a fronteira do `prob(25)`, quem deixa semente e quem nao, e o que a semente vira no chao.
///   2. COMER: a semente cai na mochila quando o sorteio manda, e a refeicao recusada nao rende nada.
///   3. PLANTAR: sai a SEMENTE da mochila e nasce a ARVORE -- a mesma do mapa, que da maca e vai pro disco.
///
/// O defeito injetado (`Plantio.FrutaSemSementeDeTeste`) e o jogo de antes do pedido: a fruta nunca deixa
/// semente. A regua de comer tem de reprovar com ele.
///
///     Godot --headless -- --server --port 7980 --macieirateste
/// </summary>
public partial class GameServer
{
	private int _mcOk, _mcFalhou;

	private void AfirmarMc(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _mcOk++; GD.Print($"[macieira]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _mcFalhou++; GD.PrintErr($"[macieira]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDaMacieira()
	{
		_mcOk = _mcFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[macieira] ================ A SEMENTE DE MACIEIRA (dono, 2026-10-10) ================");

		ZoneKey terra = ZonaDaBancadaDeProjetil;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(terra);
		int obrasAntes = _noChao.Count;

		try
		{
			AfirmarMc("(montagem) a Terra tem mapa, e o catalogo de obras tem a macieira",
					  mapa != null && _obras?.Get(CatalogoDeItens.ArvoreDeMaca) != null);
			if (mapa == null || _obras?.Get(CatalogoDeItens.ArvoreDeMaca) == null) return;

			(int px, int py) = PracaDaBancada(mapa);
			McDaRegra();
			McDeComer(px, py);
			LimparTudoDaBancada();
			McDePlantar(terra, mapa, px, py);
		}
		finally
		{
			Plantio.FrutaSemSementeDeTeste = false;
			SorteioDeSementeDeTeste = null;

			// O MUNDO VOLTA: as arvores da bancada saem do chao, do disco e da colisao.
			if (_noChao.Count > obrasAntes) _noChao.RemoveRange(obrasAntes, _noChao.Count - obrasAntes);
			GravarMundo();
			AplicarColisaoDasObras(terra);
			LimparTudoDaBancada();
		}

		GD.Print($"[macieira] ================ {_mcOk} OK, {_mcFalhou} FALHA(S) ================");
	}

	// =====================================================================
	// 1) A REGRA
	// =====================================================================
	private void McDaRegra()
	{
		GD.Print("[macieira] -- 1) a regra: o `prob(25)` do DM, e o que a semente e --");

		const string maca = CatalogoDeItens.Maca, semente = CatalogoDeItens.SementeDeMaca;

		AfirmarMc("a chance e a do original: um quarto (`prob(25)`, Food.dm:140)", Plantio.ChanceDeSemente == 0.25);
		AfirmarMc("a FRONTEIRA do sorteio: 0 e 0,2499 dao semente; 0,25 e 0,99 nao dao",
				  Plantio.SaiSemente(maca, 0) == semente && Plantio.SaiSemente(maca, 0.2499) == semente
				  && Plantio.SaiSemente(maca, 0.25) == null && Plantio.SaiSemente(maca, 0.99) == null);
		AfirmarMc("so fruta DE PLANTAR deixa semente: a Senzu (que tambem e comida) nao deixa nem com o sorteio no zero",
				  Plantio.SaiSemente(CatalogoDeItens.Senzu, 0) == null && Plantio.SementeDe(CatalogoDeItens.Senzu) == null);
		AfirmarMc("a semente CARREGA o tipo da fruta (`A.planttype = planttype`): come-la tambem pode devolver outra",
				  Plantio.SaiSemente(semente, 0) == semente);

		ItemDef? def = CatalogoDeItens.Get(semente);
		AfirmarMc("a semente esta no catalogo: empilha, PLANTAR vem antes de comer, e alimenta 2 (o padrao do `food`)",
				  def is { Empilhavel: true, Nutricao: 2 } && def.AcoesDoItem.SequenceEqual([CatalogoDeItens.AcaoPlantar, "comer"]),
				  def == null ? "sem ficha" : string.Join(",", def.AcoesDoItem));
		AfirmarMc("...e a arte dela existe no disco (o `Saiba Seed.dmi` do original)",
				  def != null && ResourceLoader.Exists(def.Arte), def?.Arte ?? "");

		AfirmarMc("O QUE ELA VIRA NO CHAO e a macieira; todo o resto assenta como ele mesmo",
				  CatalogoDeItens.OQueAssenta(semente) == CatalogoDeItens.ArvoreDeMaca
				  && CatalogoDeItens.OQueAssenta("Gravity") == "Gravity" && CatalogoDeItens.OQueAssenta(maca) == maca);
		AfirmarMc("a semente SE ASSENTA (plantar e a mesma previa de instalar), e a maca nao",
				  CatalogoDeItens.PodeAssentarNoChao(semente) && !CatalogoDeItens.PodeAssentarNoChao(maca));
		AfirmarMc("...e a ARVORE em si continua sem ser item de assentar: a unica porta pra uma macieira nova e a semente",
				  !CatalogoDeItens.PodeAssentarNoChao(CatalogoDeItens.ArvoreDeMaca));
	}

	// =====================================================================
	// 2) COMER
	// =====================================================================
	private void McDeComer(int px, int py)
	{
		GD.Print("[macieira] -- 2) comer: a semente cai na mochila quando o sorteio manda --");

		const int T = ZoneCollision.TileSize;
		const string maca = CatalogoDeItens.Maca, semente = CatalogoDeItens.SementeDeMaca;
		ServerPlayer pl = Forjar("Comilao", new Vec2(px * T + T / 2f, py * T + T / 2f - MoveRules.FeetOffsetY), 1000);

		// COM FOME A CADA MORDIDA: o corpo cheio recusa a refeicao, e o que se mede aqui e a semente.
		List<string> Morder(string id)
		{
			pl.Ficha.CurrentNutrition = 0;
			return Ouvir(() => ComandoDeItem(pl, "item_comer", id));
		}

		pl.Mochila.Guardar(maca, 4);

		SorteioDeSementeDeTeste = 0.10;
		List<string> falas = Morder(maca);
		AfirmarMc("com o sorteio DENTRO do quarto, comer a maca deixa UMA semente na mochila -- e o jogador fica sabendo",
				  pl.Mochila.Quantos(maca) == 3 && pl.Mochila.Quantos(semente) == 1 && Disse(falas, "sobrou uma semente"),
				  $"macas={pl.Mochila.Quantos(maca)} sementes={pl.Mochila.Quantos(semente)} | {Ultimos(falas)}");

		SorteioDeSementeDeTeste = 0.60;
		falas = Morder(maca);
		AfirmarMc("com o sorteio FORA, a maca e comida e nao sobra nada (nem aviso de semente)",
				  pl.Mochila.Quantos(maca) == 2 && pl.Mochila.Quantos(semente) == 1 && !Disse(falas, "semente"),
				  $"macas={pl.Mochila.Quantos(maca)} sementes={pl.Mochila.Quantos(semente)} | {Ultimos(falas)}");

		// ---- A CONTRAPROVA: o jogo de antes do pedido ----
		SorteioDeSementeDeTeste = 0.10;
		Plantio.FrutaSemSementeDeTeste = true;
		try { falas = Morder(maca); }
		finally { Plantio.FrutaSemSementeDeTeste = false; }
		AfirmarMc("(defeito injetado: a fruta nunca deixa semente) a MESMA mordida, com o MESMO sorteio, nao rende semente",
				  pl.Mochila.Quantos(maca) == 1 && pl.Mochila.Quantos(semente) == 1 && !Disse(falas, "semente"),
				  $"macas={pl.Mochila.Quantos(maca)} sementes={pl.Mochila.Quantos(semente)} | {Ultimos(falas)}");

		// ---- A REFEICAO RECUSADA NAO RENDE ----
		pl.Ficha.CurrentNutrition = Nutricao.Tanque(pl.Ficha.Metabolism);
		falas = Ouvir(() => ComandoDeItem(pl, "item_comer", maca));
		AfirmarMc("de barriga CHEIA a maca nao e comida -- e maca que nao foi comida nao deixa semente",
				  pl.Mochila.Quantos(maca) == 1 && pl.Mochila.Quantos(semente) == 1,
				  $"macas={pl.Mochila.Quantos(maca)} sementes={pl.Mochila.Quantos(semente)} | {Ultimos(falas)}");

		// ---- A SEMENTE TAMBEM E COMIDA ----
		SorteioDeSementeDeTeste = 0.60;
		double antes = 0;
		falas = Morder(semente);
		AfirmarMc("a semente se come (2 de nutricao), e com o sorteio fora ela so some",
				  pl.Mochila.Quantos(semente) == 0 && pl.Ficha.CurrentNutrition == antes + 2,
				  $"sementes={pl.Mochila.Quantos(semente)} nutricao={pl.Ficha.CurrentNutrition}");

		// ---- A MOCHILA CHEIA ----
		// 29 espacos tomados por coisa que nao empilha + a pilha de macas no ultimo: a semente so cabe se a
		// maca comida for a ULTIMA da pilha (o slot dela fica livre antes de a semente chegar).
		for (int i = 0; i < Inventario.Slots - 1; i++) pl.Mochila.Guardar(CatalogoDeItens.Scouter);
		pl.Mochila.Guardar(maca);   // agora sao 2 macas no trigesimo espaco
		SorteioDeSementeDeTeste = 0.10;
		falas = Morder(maca);
		AfirmarMc("MOCHILA CHEIA (a pilha de macas ainda ocupa o espaco): a semente se perde, e o jogador fica sabendo",
				  pl.Mochila.Quantos(maca) == 1 && pl.Mochila.Quantos(semente) == 0 && Disse(falas, "se perdeu"),
				  $"macas={pl.Mochila.Quantos(maca)} sementes={pl.Mochila.Quantos(semente)} | {Ultimos(falas)}");
		falas = Morder(maca);
		AfirmarMc("...mas a ULTIMA maca da pilha libera o espaco, e a semente cabe nele",
				  pl.Mochila.Quantos(maca) == 0 && pl.Mochila.Quantos(semente) == 1,
				  $"macas={pl.Mochila.Quantos(maca)} sementes={pl.Mochila.Quantos(semente)} | {Ultimos(falas)}");

		// ---- O SORTEIO DE VERDADE ----
		// Sem cravar nada: 400 macas pelo `_rng` do servidor. A media e 100 e o desvio 8,7; a folga de 65 a
		// 135 sao quatro desvios -- reprova um quarto que virou um decimo ou uma metade, e nao a sorte.
		pl.Mochila.Tirar(CatalogoDeItens.Scouter, Inventario.Slots);
		pl.Mochila.Tirar(semente, pl.Mochila.Quantos(semente));
		SorteioDeSementeDeTeste = null;
		for (int i = 0; i < 400; i++)
		{
			pl.Mochila.Guardar(maca);
			pl.Ficha.CurrentNutrition = 0;
			ComandoDeItem(pl, "item_comer", maca);
		}
		int renderam = pl.Mochila.Quantos(semente);
		AfirmarMc("pelo sorteio DO SERVIDOR, 400 macas rendem perto de 100 sementes (um quarto, com folga de quatro desvios)",
				  renderam is >= 65 and <= 135 && pl.Mochila.Quantos(maca) == 0, $"{renderam} sementes em 400 macas");
	}

	// =====================================================================
	// 3) PLANTAR
	// =====================================================================
	private void McDePlantar(ZoneKey terra, ZoneCollision mapa, int px, int py)
	{
		GD.Print("[macieira] -- 3) plantar: sai a semente, nasce a arvore --");

		const int T = ZoneCollision.TileSize;
		const string maca = CatalogoDeItens.Maca, semente = CatalogoDeItens.SementeDeMaca;
		int cx = px + 2, cy = py + 2;
		ServerPlayer pl = Forjar("Lavrador", new Vec2(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY), 1000);

		// INTEIROS de proposito, como na bancada das maquinas: o `posicionar` le com `float.TryParse`.
		string Ponto(int x, int y) => $"{x * T + T / 2}/{y * T + T / 2 - (int)MoveRules.FeetOffsetY}";
		List<string> Plantar(string item, int x, int y) => Ouvir(() => ComandoDeTech(pl, "posicionar", $"{item}/{Ponto(x, y)}"));

		// ---- SEM SEMENTE NAO SE PLANTA ----
		int antes = _noChao.Count;
		List<string> falas = Plantar(semente, cx + 1, cy);
		AfirmarMc("SEM a semente na mochila o servidor nao planta nada (o item e conferido pelo nome DELE, e nao pelo da arvore)",
				  _noChao.Count == antes && Disse(falas, "você não tem Semente de Macieira"), Ultimos(falas));

		// ---- A SEMENTE VIRA A ARVORE ----
		pl.Mochila.Guardar(semente, 2);
		falas = Plantar(semente, cx + 1, cy);
		Obra? arvore = _noChao.Count > antes ? _noChao[^1] : null;
		AfirmarMc("**PLANTAR**: a semente sai da mochila e nasce uma MACIEIRA no tile escolhido, com o nome de quem plantou",
				  arvore is { Tipo: CatalogoDeItens.ArvoreDeMaca, DoMapa: false } && arvore.DonoNome == pl.Name
				  && arvore.Zona.Equals(terra) && CatalogoDeObras.Celula((float)arvore.X, (float)arvore.Y) == (cx + 1, cy)
				  && pl.Mochila.Quantos(semente) == 1 && Disse(falas, "você planta a semente"),
				  $"obra={arvore?.Tipo} dono={arvore?.DonoNome} sementes={pl.Mochila.Quantos(semente)} | {Ultimos(falas)}");
		if (arvore == null) return;

		// ---- ELA E UMA MACIEIRA DE VERDADE ----
		falas = Ouvir(() => ComandoDeInteracao(pl, "colher", ""));
		AfirmarMc("a arvore plantada nasce CHEIA e da maca pelo mesmo verbo da tecla E (`appleCount = 10`, Plants.dm:60)",
				  pl.Mochila.Quantos(maca) == 1 && MacasDe(arvore.Id) == 9, $"macas={pl.Mochila.Quantos(maca)} no pe={MacasDe(arvore.Id)} | {Ultimos(falas)}");

		// ---- E VAI PRO DISCO (`A.SaveItem = 1`, Plants.dm:952) ----
		string copia = System.IO.Path.Combine(_store?.Pasta ?? ".", "mundo.da-bancada-da-semente.json");
		try
		{
			GravarMundoEm(copia);
			List<Obra>? gravadas = System.IO.File.Exists(copia)
				? JsonSerializer.Deserialize<List<Obra>>(System.IO.File.ReadAllText(copia), new JsonSerializerOptions { IncludeFields = true })
				: null;
			AfirmarMc("a arvore plantada entra no `mundo.json` (a do MAPA nao entra: ela renasce do `.objetos`)",
					  gravadas != null && gravadas.Exists(o => o.Id == arvore.Id && o.Tipo == CatalogoDeItens.ArvoreDeMaca)
					  && !gravadas.Exists(o => o.DoMapa), $"{gravadas?.Count} obra(s) no arquivo");
		}
		finally { if (System.IO.File.Exists(copia)) System.IO.File.Delete(copia); }

		// ---- AS GUARDAS DE LUGAR SAO AS DE TODA CONSTRUCAO, E A SEMENTE NAO SE PERDE NELAS ----
		falas = Plantar(semente, cx + 1, cy);
		AfirmarMc("em cima de OUTRA arvore nao se planta, e a semente continua na mochila",
				  _noChao.Count == antes + 1 && pl.Mochila.Quantos(semente) == 1, Ultimos(falas));
		falas = Plantar(semente, cx + 40, cy);
		AfirmarMc("LONGE DEMAIS nao se planta, e a semente continua na mochila",
				  _noChao.Count == antes + 1 && pl.Mochila.Quantos(semente) == 1, Ultimos(falas));

		if (AguaPerto(mapa, cx, cy) is { } agua)
		{
			// O corpo vai pra BEIRA dessa agua: o alcance e medido a partir dele.
			pl.Pos = new Vec2(agua.X * T + T / 2f, (agua.Y + 1) * T + T / 2f - MoveRules.FeetOffsetY);
			falas = Plantar(semente, agua.X, agua.Y);
			AfirmarMc("NA AGUA nao se planta -- e aqui a semente NAO afunda (no DM ela se perdia, Plants.dm:922-924; desvio declarado)",
					  _noChao.Count == antes + 1 && pl.Mochila.Quantos(semente) == 1 && Disse(falas, "água"), Ultimos(falas));
			pl.Pos = new Vec2(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY);
		}
		else AfirmarMc("(montagem) ha agua na Terra pra medir a recusa", false);

		// ---- A ARVORE EM SI NAO E ITEM DE ASSENTAR ----
		pl.Mochila.Guardar(CatalogoDeItens.ArvoreDeMaca);
		falas = Plantar(CatalogoDeItens.ArvoreDeMaca, cx + 2, cy + 1);
		AfirmarMc("uma `AppleTree` forjada na mochila NAO se assenta: a troca item -> arvore nao abriu porta pra mobilia do mapa",
				  _noChao.Count == antes + 1 && pl.Mochila.Quantos(CatalogoDeItens.ArvoreDeMaca) == 1, Ultimos(falas));
		pl.Mochila.Tirar(CatalogoDeItens.ArvoreDeMaca);

		// ---- E O RESTO DO FUNIL NAO MUDOU ----
		Obra? maquina = AssentarDeTeste(pl, "Gravity", cx - 1, cy);
		AfirmarMc("uma construcao comum continua assentando como ela mesma pelo mesmo funil (a maquina de gravidade)",
				  maquina is { Tipo: "Gravity" } && pl.Mochila.Quantos("Gravity") == 0, $"obra={maquina?.Tipo}");
	}
}
