using Godot;
using Jandirus.Core;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A VIGIA DOS INTERIORES (`--interioresteste`, servidor sem login) -- a regra do dono de 2026-10-11:
/// **ESTADO QUE MUDA DURANTE O JOGO NUNCA E DIVIDIDO ENTRE DUAS INSTANCIAS DE UM INTERIOR.**
///
/// O QUE ELA FAZ, pra CADA tipo que o funil conhece (<see cref="TiposDeInterior"/> -- a lista sai de la, e nao
/// daqui): fabrica DUAS instancias, muda uma pelos caminhos de producao (a celula que cai pelo `DerrubarCelula`, a
/// maquina pelo `AplicarColisaoDasObras`) e confere que a outra, a planta e o retrato que vai pro cliente ficaram
/// como estavam. Depois solta as copias e confere que renascem vestidas com o que era delas.
///
/// TIPO NOVO SEM LINHA AQUI E VERMELHO (<see cref="LinhasDaVigia"/>), e fabrica de interior nova no codigo -- um
/// `ZoneKey.Interior(` fora da lista <see cref="FabricasConhecidas"/> -- tambem. A linha diz o que pode mudar la
/// dentro (parede que cai, chao que racha, o que cega), e a vigia MEDE isso na planta: linha velha e vermelho.
///
/// O CONTRA-EXEMPLO de cada regra e o jogo de antes: `EstadoDeZonaPorNomeDeTeste` devolve a planta a toda instancia
/// e guarda por NOME o que caiu e o que abriu. Com ele ligado, cada prova de cima tem de reprovar.
/// </summary>
public sealed partial class GameServer
{
	private int _intrOk, _intrFalhou, _intrPegos, _intrBatido;

	private void AfirmarIntr(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _intrOk++; GD.Print($"[interiores]   OK     {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _intrFalhou++; GD.PrintErr($"[interiores]   FALHA  {nome}   [{detalhe}]"); }
	}

	/// <summary>Com o defeito ligado a prova tem de REPROVAR: `reprovou` falso e defeito que passou batido.</summary>
	private void DefeitoIntr(string nome, bool reprovou, string detalhe = "")
	{
		if (reprovou) { _intrPegos++; GD.Print($"[interiores]   pegou  (defeito injetado) {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _intrBatido++; _intrFalhou++; GD.PrintErr($"[interiores]   FALHA  (defeito injetado PASSOU BATIDO) {nome}   [{detalhe}]"); }
	}

	/// <summary>A LINHA DA VIGIA de um tipo de interior: como se fabrica uma instancia dele, e o que pode mudar la em jogo.</summary>
	private sealed record LinhaDaVigia(Func<int, ZoneKey> Instancia, bool ParedeCai, bool ChaoRacha, bool Cega, bool AceitaObra);

	/// <summary>
	/// UMA LINHA POR TIPO DE <see cref="TiposDeInterior"/>. Tipo novo la sem linha aqui = VERMELHO.
	///   * mente / selo: o quarto branco nao tem uma celula densa -- nada barra, nada cega; o CHAO racha.
	///   * majin: o bolsao e inteiro duro (`DefinirDuro`): nada cai, nem parede nem chao. A borda cega.
	///   * nave: as paredes da sala de controle e o console caem; o casco (a beirada) nao.
	///   * arquivo: a Sala do Tempo de um mundo (o z13) -- 168 celulas que barram, todas podem cair.
	/// `AceitaObra`: a Sala do Tempo e a nave (desde 2026-10-18: o fim dela tira o que esta dentro -- `TirarAsObrasDaNave`). Nos outros nao
/// se constroi nada (`LugarDeObra`): morrem com quem os sustenta.
	/// </summary>
	private static readonly Dictionary<string, LinhaDaVigia> LinhasDaVigia = new()
	{
		["mente"] = new(DimensaoMental.De, ParedeCai: false, ChaoRacha: true, Cega: false, AceitaObra: false),
		["selo"] = new(ZonaDoSelo, ParedeCai: false, ChaoRacha: true, Cega: false, AceitaObra: false),
		["majin"] = new(InteriorDoMajin.De, ParedeCai: false, ChaoRacha: false, Cega: true, AceitaObra: false),
		["nave"] = new(NaveGrande.ZonaDoInterior, ParedeCai: true, ChaoRacha: true, Cega: true, AceitaObra: true),
		[TipoDeArquivo] = new(id => SalaDePlaneta.Interior((ulong)id), ParedeCai: true, ChaoRacha: true, Cega: true, AceitaObra: true),
	};

	/// <summary>
	/// AS FABRICAS DE INTERIOR DO CODIGO: o arquivo, e um trecho da linha que fabrica. Um `ZoneKey.Interior(` fora
	/// desta lista (bancada e robo ficam de fora da varredura) e interior novo -- e interior novo pede um tipo no
	/// funil e uma linha em <see cref="LinhasDaVigia"/>.
	/// </summary>
	private static readonly (string Arquivo, string Trecho, string Tipo)[] FabricasConhecidas =
	[
		("Core/World/ZoneKey.cs", "new(KindInterior, name, id)", "(a fabrica de todas)"),
		("Core/Tech/NaveGrande.cs", "ZoneKey.Interior(NomeDaZonaInterna", "nave"),
		("Core/World/DimensaoMental.cs", "ZoneKey.Interior(Zona", "mente"),
		("Core/World/InteriorDoMajin.cs", "ZoneKey.Interior(Zona", "majin"),
		("Core/World/SalaDePlaneta.cs", "ZoneKey.Interior(SalaDoTempo.Zona", TipoDeArquivo),
		("Server/GameServer.Fusao.cs", "ZoneKey.Interior(NomeDoSelo", "selo"),
		("Server/GameServer.Selo.cs", "ZoneKey.Interior(NomeDoSelo", "selo"),
	];

	private const int IdBaseDaVigia = 987_000;

	/// <summary>O id da MAQUINA DE GRAVIDADE no catalogo de construcoes (`construcoes.json`).</summary>
	private const string MaquinaDeGravidadeDaVigia = "Gravity";

	public void RodarAVigiaDosInteriores()
	{
		_intrOk = _intrFalhou = _intrPegos = _intrBatido = 0;
		GD.Print("[interiores] ================ A VIGIA DOS INTERIORES: o que muda em jogo e de UMA instancia (dono, 2026-10-11) ================");
		try
		{
			IntrDosTipos();
			IntrDasPortas();
			IntrDaObraDaSala();
			IntrDosHomonimos();
			IntrDaFonte();
			IntrDaMemoria();
		}
		catch (Exception e)
		{
			AfirmarIntr($"a vigia rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			EstadoDeZonaPorNomeDeTeste = false;
			LugarDeObra.InterioresDeSessaoAceitamDeTeste = false;
			_tiposDeInterior = null;
			SoltarInterioresVazios();
			GD.Print($"[interiores] ================ {_intrOk} OK, {_intrFalhou} FALHA(S) ================   "
					 + $"(defeitos injetados: {_intrPegos} pegos, {_intrBatido} passaram batido)");
		}
	}

	// =====================================================================
	// 1) CADA TIPO DO FUNIL: duas instancias, muda-se uma
	// =====================================================================

	/// <summary>Os tipos do funil que nao tem linha na vigia.</summary>
	private List<string> IntrTiposSemLinha() =>
		TiposDeInterior.Where(t => !LinhasDaVigia.ContainsKey(t.Nome)).Select(t => t.Nome).ToList();

	private void IntrDosTipos()
	{
		GD.Print("[interiores] -- 1) cada tipo de interior do funil: duas instancias, muda-se uma, a outra fica como estava --");
		List<string> semLinha = IntrTiposSemLinha();
		AfirmarIntr("**TODO TIPO DO FUNIL TEM LINHA NA VIGIA** (`TiposDeInterior` x `LinhasDaVigia`)", semLinha.Count == 0,
					semLinha.Count == 0 ? $"{TiposDeInterior.Length} tipo(s): {string.Join(", ", TiposDeInterior.Select(t => t.Nome))}"
										: $"SEM LINHA: {string.Join(", ", semLinha)}");

		// o contra-exemplo: um tipo inventado no funil, sem linha aqui
		TipoDeInterior[] deVerdade = TiposDeInterior;
		_tiposDeInterior = [new("inventado", z => z.Name == "Inventado", _ => DaPlanta(NaveGrande.Planta())), .. deVerdade];
		DefeitoIntr("um tipo novo no funil sem linha na vigia: a prova de cima REPROVA", IntrTiposSemLinha().Contains("inventado"));
		_tiposDeInterior = deVerdade;

		int n = 0;
		foreach (TipoDeInterior tipo in TiposDeInterior)
		{
			if (!LinhasDaVigia.TryGetValue(tipo.Nome, out LinhaDaVigia? linha)) continue;
			int idA = IdBaseDaVigia + n * 10 + 1, idB = idA + 1;
			n++;
			IntrDoTipo(tipo, linha, idA, idB);
		}
	}

	/// <summary>O que uma cena de duas instancias mostrou.</summary>
	private sealed record ObservadoIntr(
		bool Montou, bool CopiasProprias, bool TemAlvo, bool CaiuEmA, bool BIntacta, bool RetratoDeASoDeA,
		bool RetratoDeBVazio, bool TemObra, bool ObraBarraEmA, bool ObraNaoBarraEmB, bool ObraDeASobreviveAB,
		bool PlantaIntacta, string Detalhe);

	private void IntrDoTipo(TipoDeInterior tipo, LinhaDaVigia linha, int idA, int idB)
	{
		ZoneKey a = linha.Instancia(idA), b = linha.Instancia(idB);
		(ZoneCollision? pc, ZoneCollision? pv) = tipo.Planta(a);
		string nome = tipo.Nome;

		AfirmarIntr($"[{nome}] (montagem) a fabrica da linha da um interior DESTE tipo, duas instancias de mesmo NOME e chaves diferentes, e a planta existe",
					a.Kind == ZoneKey.KindInterior && TipoDoInterior(a).Nome == nome && TipoDoInterior(b).Nome == nome
					&& a.Name == b.Name && a.Hash != b.Hash && pc != null, $"{a} e {b}");
		if (pc == null) return;

		// ---- A TABELA: o que pode mudar aqui dentro, MEDIDO na planta
		int barram = 0, caem = 0, racham = 0, cegam = 0;
		for (int y = 0; y < pc.Height; y++)
			for (int x = 0; x < pc.Width; x++)
			{
				bool parede = pc.BloqueadaNoArquivo(x, y);
				bool cede = !pc.Indestrutivel(x, y) && !pc.NaBorda(x, y);
				if (parede) barram++;
				if (parede && cede) caem++;
				if (!parede && cede) racham++;
				if (pv != null && pv.BloqueadaNoArquivo(x, y)) cegam++;
			}
		bool aceitaBloco = RegrasDeBloco.ZonaAceita(a);
		GD.Print($"[interiores]   TABELA [{nome}] {a.Name}#id: planta {pc.Width}x{pc.Height}; {barram} celula(s) que barram, {caem} podem cair; "
				 + $"{racham} de chao que racha; {cegam} que cegam; bloco de base: {(aceitaBloco ? "SIM" : "nao")}");
		AfirmarIntr($"[{nome}] a linha da vigia diz a verdade sobre a planta (parede que cai, chao que racha, o que cega)",
					linha.ParedeCai == (caem > 0) && linha.ChaoRacha == (racham > 0) && linha.Cega == (cegam > 0),
					$"linha: cai {linha.ParedeCai}, racha {linha.ChaoRacha}, cega {linha.Cega}; planta: {caem}, {racham}, {cegam}");
		AfirmarIntr($"[{nome}] bloco de base NAO se ergue aqui (`RegrasDeBloco.ZonaAceita`): as camadas `_erguidas` e `_cobertas` nao tem como mudar "
					+ "-- se um dia tiverem, a copia que renasce precisa vesti-las", !aceitaBloco);

		// ---- "AQUI SE CONSTROI?" -- pelo verbo de producao (`Posicionar`) e pela cova (`ErguerALapide`)
		AfirmarIntr($"[{nome}] a linha da vigia diz a verdade sobre construir aqui (`LugarDeObra`)", linha.AceitaObra == LugarDeObra.Aceita(a),
					$"linha: {linha.AceitaObra}; Core: {LugarDeObra.PorQueNaoSeConstroi(a) ?? "aqui se constroi"}");
		if (!linha.AceitaObra && IntrChaoLivre(pc, null) is { } ondeAssentar)
		{
			(Obra? posta, string disse) = IntrAssentar(a, ondeAssentar);
			AfirmarIntr($"[{nome}] **AQUI NAO SE CONSTROI**: assentar a maquina pelo `Posicionar` e recusado com a frase do lugar, e nada vai pro chao",
						posta == null && disse.Contains("não dá pra construir", StringComparison.Ordinal), disse);
			if (posta != null) IntrTirarObra(posta);
			(bool abriu, string disseNaCova) = IntrCova(a, ondeAssentar);
			AfirmarIntr($"[{nome}] ...e nao se abre cova aqui (a lapide e obra gravada)",
						!abriu && disseNaCova.Contains("não dá pra abrir uma cova", StringComparison.Ordinal), disseNaCova);

			LugarDeObra.InterioresDeSessaoAceitamDeTeste = true;
			try
			{
				(Obra? deAntes, string disseAntes) = IntrAssentar(a, ondeAssentar);
				DefeitoIntr($"[{nome}] os interiores de sessao aceitam (o jogo de antes de 2026-10-11): a maquina e ACEITA e fica gravada num lugar que morre com o dono",
							deAntes != null && deAntes.Zona.Equals(a), disseAntes);
				if (deAntes != null) IntrTirarObra(deAntes);
			}
			finally { LugarDeObra.InterioresDeSessaoAceitamDeTeste = false; }
		}

		if (!linha.ParedeCai && !linha.ChaoRacha)
		{
			// ONDE NADA PODE CAIR, PROVA-SE: a planta inteira, celula a celula, pelo caminho de producao.
			int cairam = 0;
			for (int y = 0; y < pc.Height; y++)
				for (int x = 0; x < pc.Width; x++)
					if (DerrubarCelula(a, x, y)) cairam++;
			AfirmarIntr($"[{nome}] **NADA CAI AQUI**: o `DerrubarCelula` recusa as {pc.Width * pc.Height} celulas da planta, uma a uma",
						cairam == 0 && !_cenarioCaido.ContainsKey(ChaveDeEstado(a)), $"{cairam} cairam");
		}

		// ---- A CENA, no jogo de hoje
		ObservadoIntr o = IntrCena(tipo, linha, a, b, renascer: true);
		AfirmarIntr($"[{nome}] **CADA INSTANCIA TEM A COPIA DELA** dos dois mapas: A e B sao objetos diferentes, nenhum e a planta, e os dois sairam dela",
					o.Montou && o.CopiasProprias, o.Detalhe);
		if (o.TemAlvo)
		{
			AfirmarIntr($"[{nome}] **A CELULA QUE CAI EM A NAO CAI EM B**: caiu em A pelo `DerrubarCelula`; o mapa e a vista de B nao mudaram",
						o.CaiuEmA && o.BIntacta, o.Detalhe);
			AfirmarIntr($"[{nome}] **QUEM ENTRA RECEBE AS CAIDAS DA INSTANCIA DELE**: o retrato de A leva a celula (e a zona de A); o de B vai vazio",
						o.RetratoDeASoDeA && o.RetratoDeBVazio, o.Detalhe);
		}
		if (o.TemObra)
			AfirmarIntr($"[{nome}] **A MAQUINA ASSENTADA EM A SO BARRA EM A** (`AplicarColisaoDasObras`), e refazer a camada de B nao apaga a de A",
						o.ObraBarraEmA && o.ObraNaoBarraEmB && o.ObraDeASobreviveAB,
						$"barra em A {o.ObraBarraEmA}, livre em B {o.ObraNaoBarraEmB}, A depois de B {o.ObraDeASobreviveAB}");
		AfirmarIntr($"[{nome}] **A PLANTA NAO MUDOU**: ninguem escreveu nela", o.PlantaIntacta, o.Detalhe);

		// ---- O CONTRA-EXEMPLO: o jogo de antes (a planta pra todos, o registro por nome)
		EstadoDeZonaPorNomeDeTeste = true;
		ObservadoIntr antes;
		try { antes = IntrCena(tipo, linha, a, b, renascer: false); }
		finally
		{
			EstadoDeZonaPorNomeDeTeste = false;
			// a camada de obras do homonimo de arquivo (a Sala da Terra divide a planta com as de planeta) volta ao que e dele
			if (_catalogo?.Get(a.Name) != null) AplicarColisaoDasObras(ZoneKey.Premade(a.Name));
		}
		DefeitoIntr($"[{nome}] a planta pra todos: as duas instancias recebem o MESMO objeto", !antes.CopiasProprias);
		if (antes.TemAlvo)
		{
			DefeitoIntr($"[{nome}] a celula que cai em A aparece caida em B (o mapa de B mudou)", !antes.BIntacta, antes.Detalhe);
			DefeitoIntr($"[{nome}] o registro por NOME: quem entra em B recebe o retrato com a celula que caiu em A", !antes.RetratoDeBVazio, antes.Detalhe);
		}
		if (antes.TemObra)
			DefeitoIntr($"[{nome}] a maquina de A barra em B, e refazer a camada de B apaga a de A",
						!antes.ObraNaoBarraEmB && !antes.ObraDeASobreviveAB,
						$"livre em B {antes.ObraNaoBarraEmB}, A depois de B {antes.ObraDeASobreviveAB}");
		if (antes.TemAlvo || antes.TemObra) DefeitoIntr($"[{nome}] ...e a PLANTA foi escrita", !antes.PlantaIntacta || !antes.CopiasProprias, antes.Detalhe);
	}

	/// <summary>A primeira celula que o `DerrubarCelula` aceita: uma parede que cai; se nao houver, um chao que racha.</summary>
	private static (int X, int Y)? IntrAlvoQueCai(ZoneCollision planta)
	{
		(int X, int Y)? chao = null;
		for (int y = 0; y < planta.Height; y++)
			for (int x = 0; x < planta.Width; x++)
			{
				if (planta.Indestrutivel(x, y) || planta.NaBorda(x, y)) continue;
				if (planta.BloqueadaNoArquivo(x, y)) return (x, y);
				chao ??= (x, y);
			}
		return chao;
	}

	/// <summary>Um chao livre da planta (nem parede, nem agua, nem beirada), longe do alvo da queda.</summary>
	private static (int X, int Y)? IntrChaoLivre(ZoneCollision planta, (int X, int Y)? evitando)
	{
		for (int y = planta.Height / 2; y < planta.Height; y++)
			for (int x = planta.Width / 2; x < planta.Width; x++)
				if (!planta.BloqueadaNoArquivo(x, y) && !planta.EhAgua(x, y) && !planta.NaBorda(x, y) && !planta.BlockedCell(x, y)
					&& (evitando is not { } e || Math.Abs(e.X - x) + Math.Abs(e.Y - y) > 2))
					return (x, y);
		return null;
	}

	/// <summary>
	/// A CENA: derruba uma celula em A e assenta uma maquina em A, pelos caminhos de producao, e olha B, a planta e os
	/// retratos. Devolve o mundo como pegou. `renascer`: solta as copias no fim e confere que voltam vestidas.
	/// </summary>
	private ObservadoIntr IntrCena(TipoDeInterior tipo, LinhaDaVigia linha, ZoneKey a, ZoneKey b, bool renascer)
	{
		const int T = ZoneCollision.TileSize;
		string nome = tipo.Nome;
		(ZoneCollision? pc, ZoneCollision? pv) = tipo.Planta(a);
		ZoneCollision? ma = MapaDaZonaOuCatalogo(a), mb = MapaDaZonaOuCatalogo(b);
		ZoneCollision? va = MapaDaVista(a), vb = MapaDaVista(b);
		if (pc == null || ma == null || mb == null)
			return new ObservadoIntr(false, false, false, false, false, false, false, false, false, false, false, false, "sem mapa");

		int plantaAntes = pc.CelulasMudadas + (pv?.CelulasMudadas ?? 0);
		bool copias = !ReferenceEquals(ma, mb) && !ReferenceEquals(ma, pc) && !ReferenceEquals(mb, pc) && ma.SaiuDe(pc) && mb.SaiuDe(pc)
					  && (pv == null ? va == null && vb == null
									 : va != null && vb != null && !ReferenceEquals(va, vb) && !ReferenceEquals(va, pv)
									   && !ReferenceEquals(vb, pv) && va.SaiuDe(pv) && vb.SaiuDe(pv));

		// ---- 1) a celula que cai
		(int X, int Y)? alvo = linha.ParedeCai || linha.ChaoRacha ? IntrAlvoQueCai(pc) : null;
		bool eraParede = alvo is { } q && pc.BloqueadaNoArquivo(q.X, q.Y);
		bool caiuEmA = false, bIntacta = true, retratoA = true, retratoB = true;
		string chaveDeA = ChaveDeEstado(a);
		bool haviaRegistro = _cenarioCaido.ContainsKey(chaveDeA);
		string detalhe = "";
		if (alvo is { } c)
		{
			bool caiu = DerrubarCelula(a, c.X, c.Y);
			HashSet<(int X, int Y)>? doA = _cenarioCaido.GetValueOrDefault(chaveDeA);
			caiuEmA = caiu && doA != null && doA.Contains(c) && (!eraParede || !ma.BlockedCell(c.X, c.Y));
			(_, ulong zonaDoA, List<(int X, int Y)> rA) = LerPacoteDeCenario(PacoteDeRetratoDeCenario(a).CopyData());
			(_, ulong zonaDoB, List<(int X, int Y)> rB) = LerPacoteDeCenario(PacoteDeRetratoDeCenario(b).CopyData());
			bIntacta = mb.CelulasMudadas == 0 && (vb?.CelulasMudadas ?? 0) == 0 && (!eraParede || mb.BlockedCell(c.X, c.Y));
			retratoA = zonaDoA == a.Hash && rA.Count == 1 && rA[0].X == c.X && rA[0].Y == c.Y;
			retratoB = zonaDoB == b.Hash && rB.Count == 0;
			detalhe = $"{(eraParede ? "parede" : "chao")} ({c.X},{c.Y}): caiu {caiu}; A barra ali {ma.BlockedCell(c.X, c.Y)}, B barra ali {mb.BlockedCell(c.X, c.Y)}; "
					  + $"B mudou {mb.CelulasMudadas} celula(s); retrato de A {rA.Count}, de B {rB.Count}";
		}

		// ---- 2) a maquina
		Construcao? densa = _obras?.Todas.FirstOrDefault(k => k.Densa);
		(int X, int Y)? chao = IntrChaoLivre(pc, alvo);
		bool obraEmA = false, obraLivreEmB = true, sobrevive = true;
		Obra? forjada = null;
		if (densa != null && chao is { } f)
		{
			forjada = new Obra
			{
				Id = int.MaxValue - 7, Tipo = densa.Id, ZonaTipo = a.Kind, ZonaNome = a.Name, ZonaSeed = a.Seed,
				X = f.X * T + T / 2f, Y = f.Y * T + T / 2f - MoveRules.FeetOffsetY, DonoConta = "bancada_interiores",
			};
			_noChao.Add(forjada);
			AplicarColisaoDasObras(a);
			obraEmA = MapaDaZonaOuCatalogo(a)!.BlockedCell(f.X, f.Y);
			obraLivreEmB = !MapaDaZonaOuCatalogo(b)!.BlockedCell(f.X, f.Y);
			AplicarColisaoDasObras(b);
			sobrevive = MapaDaZonaOuCatalogo(a)!.BlockedCell(f.X, f.Y);
		}

		bool plantaIntacta = pc.CelulasMudadas + (pv?.CelulasMudadas ?? 0) == plantaAntes
							 && (alvo is not { } p || !eraParede || pc.BlockedCell(p.X, p.Y));

		// ---- 3) a copia morre vazia e renasce vestida; com alguem dentro, fica
		if (renascer)
		{
			int soltas = SoltarInterioresVazios();
			ZoneCollision? novaDoA = MapaDaZonaOuCatalogo(a), novaDoB = MapaDaZonaOuCatalogo(b);
			AfirmarIntr($"[{nome}] **A COPIA MORRE COM A INSTANCIA VAZIA E RENASCE VESTIDA**: outro objeto, com a celula caida aberta e a maquina "
						+ "barrando em A, e B de novo igual a planta",
						soltas >= 2 && novaDoA != null && novaDoB != null && !ReferenceEquals(novaDoA, ma) && !ReferenceEquals(novaDoB, mb)
						&& (alvo is not { } r || !eraParede || !novaDoA.BlockedCell(r.X, r.Y))
						&& (forjada == null || chao is not { } g || novaDoA.BlockedCell(g.X, g.Y))
						&& novaDoB.CelulasMudadas == 0,
						$"{soltas} solta(s); A renasceu com {novaDoA?.CelulasMudadas} celula(s) mudada(s), B com {novaDoB?.CelulasMudadas}");

			var corpo = new ServerPlayer { Id = IdBaseDaVigia + 900, Zone = a, Ficha = new Fighter() };
			ZoneList(a.Hash).Add(corpo);
			SoltarInterioresVazios();
			AfirmarIntr($"[{nome}] ...e COM ALGUEM DENTRO a copia fica: a de A e o mesmo objeto, a de B (vazia) foi solta",
						ReferenceEquals(MapaDaZonaOuCatalogo(a), novaDoA) && !ReferenceEquals(MapaDaZonaOuCatalogo(b), novaDoB));
			ZoneList(a.Hash).Remove(corpo);
		}

		// ---- devolve o mundo como pegou
		if (forjada != null)
		{
			_noChao.Remove(forjada);
			AplicarColisaoDasObras(a);
		}
		if (alvo is { } d)
		{
			if (_cenarioCaido.TryGetValue(chaveDeA, out HashSet<(int X, int Y)>? caidas))
			{
				caidas.Remove(d);
				if (!haviaRegistro && caidas.Count == 0) _cenarioCaido.Remove(chaveDeA);
			}
			MapaDaZonaOuCatalogo(a)?.Fechar(d.X, d.Y);
			MapaDaVista(a)?.Fechar(d.X, d.Y);
		}
		SoltarInterioresVazios();

		return new ObservadoIntr(true, copias, alvo != null, caiuEmA, bIntacta, retratoA, retratoB, forjada != null,
								 obraEmA, obraLivreEmB, sobrevive, plantaIntacta, detalhe);
	}

	// =====================================================================
	// 1b) CONSTRUIR: onde se recusa, e a obra da Sala do Tempo
	// =====================================================================

	/// <summary>O item de MAQUINA que a bancada assenta: denso, que sai da mochila pro chao, e que nao e nave.</summary>
	private string? IntrItemDeMaquina() =>
		_obras?.Todas.Where(k => k.Densa && !Naves.EhNave(k.Id)
								 && Jandirus.Core.Items.CatalogoDeItens.Get(k.Id) != null
								 && Jandirus.Core.Items.CatalogoDeItens.PodeAssentarNoChao(k.Id)
								 && Jandirus.Core.Items.CatalogoDeItens.OQueAssenta(k.Id) == k.Id)
			   .Select(k => k.Id).OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();

	/// <summary>
	/// Um corpo com a maquina na mochila tenta ASSENTA-LA nesta zona, na celula em que esta, pelo verbo de producao
	/// (`Posicionar`). Devolve a obra que ficou de pe (ou nulo) e o que ele ouviu.
	/// </summary>
	private (Obra? Posta, string Disse) IntrAssentar(ZoneKey zona, (int X, int Y) onde, string? qual = null)
	{
		const int T = ZoneCollision.TileSize;
		if ((qual ?? IntrItemDeMaquina()) is not { } item) return (null, "(o catalogo nao tem maquina de assentar)");
		var pl = new ServerPlayer
		{
			Id = IdBaseDaVigia + 950, Name = "bancada: o construtor", Conta = "bancada_interiores", Zone = zona,
			Pos = new Vec2(onde.X * T + T / 2f, onde.Y * T + T / 2f - MoveRules.FeetOffsetY), Ficha = new Fighter(),
		};
		pl.Mochila.Guardar(item, 1);
		int antes = _noChao.Count;
		List<string> disse = Ouvir(() => Posicionar(pl, $"{item}/{(int)pl.Pos.X}/{(int)pl.Pos.Y}"));
		return (_noChao.Count > antes ? _noChao[^1] : null, string.Join(" | ", disse));
	}

	/// <summary>Alguem tenta abrir uma cova pra um corpo nesta zona, pela rotina de producao (`ErguerALapide`).</summary>
	private (bool Abriu, string Disse) IntrCova(ZoneKey zona, (int X, int Y) onde)
	{
		const int T = ZoneCollision.TileSize;
		var lugar = new Vec2(onde.X * T + T / 2f, onde.Y * T + T / 2f - MoveRules.FeetOffsetY);
		var coveiro = new ServerPlayer { Id = IdBaseDaVigia + 951, Name = "bancada: o coveiro", Conta = "bancada_interiores", Zone = zona, Pos = lugar, Ficha = new Fighter() };
		var corpo = new ServerPlayer { Id = IdBaseDaVigia + 952, Name = "bancada: o morto", Zone = zona, Pos = lugar, Ficha = new Fighter() };
		bool abriu = false;
		List<string> disse = Ouvir(() => abriu = ErguerALapide(coveiro, corpo, "aqui jaz a bancada"));
		return (abriu, string.Join(" | ", disse));
	}

	/// <summary>Tira do mundo uma obra que a bancada pos, e regrava o `mundo.json` da bancada sem ela.</summary>
	private void IntrTirarObra(Obra o)
	{
		_noChao.Remove(o);
		GravarMundo();
		AplicarColisaoDasObras(o.Zona);
	}

	private void IntrDaObraDaSala()
	{
		GD.Print("[interiores] -- 1b) na Sala do Tempo a obra FICA, e e DAQUELA sala: a do planeta X nao aparece na da Terra nem na do planeta Y --");
		ZoneKey x = SalaDePlaneta.Interior((ulong)(IdBaseDaVigia + 601)), y = SalaDePlaneta.Interior((ulong)(IdBaseDaVigia + 602));
		ZoneKey terra = ZoneKey.Premade(SalaDoTempo.Zona), mente = DimensaoMental.De(5);
		ZoneCollision? planta = _catalogo?.Get(terra)?.Mapa;
		(int X, int Y)? chao = planta == null ? null : IntrChaoLivre(planta, null);
		AfirmarIntr("(montagem) o mapa da Sala esta no catalogo, tem chao livre, e o catalogo tem a maquina de gravidade e o regenerador",
					planta != null && chao != null && _obras?.Get(MaquinaDeGravidadeDaVigia) != null && _obras?.Get(Regenerador.Tipo) != null);
		if (planta == null || chao is not { } c || _obras == null) return;

		int ObrasEm(ZoneKey z) => _noChao.Count(o => !o.DoMapa && o.Zona.Equals(z));
		bool Barra(ZoneKey z) => MapaDaZonaOuCatalogo(z)!.BlockedCell(c.X, c.Y);
		string caminho = System.IO.Path.Combine(OS.GetUserDataDir(), "mundo_da_vigia_dos_interiores.json");
		List<Obra> noChaoDeAntes = [.. _noChao], semLugarDeAntes = [.. _obrasSemLugar];
		int idDeAntes = _proximaObraId;
		try
		{
			// ---- O QUE DA PRA ASSENTAR NUMA SALA: cada item de assentar do catalogo, pelo verbo de producao (nave fica
			// de fora da volta: ela vai pra outra lista, e o `AssentarNave` nao pergunta nada a zona -- tambem entra).
			List<string> aceitos = [], recusados = [];
			foreach (Construcao k in _obras.Todas.OrderBy(k => k.Id, StringComparer.Ordinal))
			{
				if (Naves.EhNave(k.Id) || Jandirus.Core.Items.CatalogoDeItens.Get(k.Id) == null
					|| !Jandirus.Core.Items.CatalogoDeItens.PodeAssentarNoChao(k.Id)) continue;
				(Obra? posta, string disse) = IntrAssentar(x, c, k.Id);
				if (posta != null) { aceitos.Add(k.Id); IntrTirarObra(posta); }
				else recusados.Add($"{k.Id} ({disse})");
			}
			GD.Print($"[interiores]   NA SALA DO TEMPO DA PRA ASSENTAR {aceitos.Count} coisa(s): {string.Join(", ", aceitos)}");
			if (recusados.Count > 0) GD.Print($"[interiores]   ...e {recusados.Count} o `Posicionar` recusou la: {string.Join("; ", recusados)}");
			AfirmarIntr("**O QUE SE ASSENTA NUMA SALA DO TEMPO**: a maquina de gravidade e o regenerador estao na lista do que o `Posicionar` aceita la",
						aceitos.Contains(MaquinaDeGravidadeDaVigia) && aceitos.Contains(Regenerador.Tipo), $"{aceitos.Count} aceito(s), {recusados.Count} recusado(s)");

			foreach (string maquina in new[] { MaquinaDeGravidadeDaVigia, Regenerador.Tipo })
			{
				bool densa = _obras.Get(maquina) is { Densa: true };
				string m = $"[{maquina}]";

				// ---- O QUE ACONTECIA ANTES: a LISTA ja era pela chave inteira; a CAMADA de colisao era a da planta
				if (densa)
				{
					EstadoDeZonaPorNomeDeTeste = true;
					try
					{
						(Obra? deAntes, _) = IntrAssentar(x, c, maquina);
						DefeitoIntr($"{m} a camada de obras na planta (o jogo de antes): assentada na Sala do planeta X, ela vira PAREDE SEM DESENHO na Sala da Terra -- nao "
									+ "esta na lista de la (a lista e o `mundo.json` ja eram pela chave inteira), mas a celula barra",
									deAntes != null && ObrasEm(terra) == 0 && Barra(terra), $"obras na da Terra: {ObrasEm(terra)}; a celula barra la: {Barra(terra)}");
						if (deAntes != null) IntrTirarObra(deAntes);
					}
					finally
					{
						EstadoDeZonaPorNomeDeTeste = false;
						AplicarColisaoDasObras(terra);
					}
				}

				// ---- a obra da Sala do planeta X
				(Obra? naX, string disseX) = IntrAssentar(x, c, maquina);
				AfirmarIntr($"{m} **ASSENTADA NA SALA DO PLANETA X, E DAQUELA SALA**: aceita pelo `Posicionar`, gravada com a chave INTEIRA (tipo, nome e a seed do mundo) -- e nao "
							+ "existe nem barra na Sala da Terra nem na do planeta Y",
							naX != null && naX.Tipo == maquina && naX.Zona.Equals(x) && naX.ZonaSeed == x.Seed && ObrasEm(x) == 1 && ObrasEm(y) == 0 && ObrasEm(terra) == 0
							&& Barra(x) == densa && !Barra(y) && !Barra(terra), disseX);

				(Obra? naTerra, string disseNaTerra) = IntrAssentar(terra, c, maquina);
				AfirmarIntr($"{m} **A DA TERRA IDEM**: assentada na Sala da Terra, e so dela -- a do planeta Y continua vazia e livre, e a de X continua com a UMA dela",
							naTerra != null && naTerra.Zona.Equals(terra) && ObrasEm(terra) == 1 && ObrasEm(x) == 1 && ObrasEm(y) == 0 && Barra(terra) == densa && !Barra(y),
							disseNaTerra);

				// ---- sai todo mundo, a copia morre, alguem volta
				ZoneCollision? copiaVelha = MapaDaZonaOuCatalogo(x);
				int soltas = SoltarInterioresVazios();
				AfirmarIntr($"{m} **SAI TODO MUNDO, A COPIA DA SALA MORRE, ALGUEM VOLTA: ELA ESTA LA** -- outro objeto de mapa, a mesma maquina na lista, no mesmo lugar",
							soltas >= 1 && !ReferenceEquals(MapaDaZonaOuCatalogo(x), copiaVelha) && ObrasEm(x) == 1 && Barra(x) == densa && !Barra(y), $"{soltas} copia(s) solta(s)");

				// ---- a parada: o estrago da sessao e esquecido, a obra e dado de disco (a parada de VERDADE esta na `--paradateste`)
				EsquecerOEstragoDaSessao();
				AfirmarIntr($"{m} **A PARADA NAO A LEVA** (`EsquecerOEstragoDaSessao`, o que o `Stop` chama): continua na lista das duas Salas e no lugar",
							ObrasEm(x) == 1 && ObrasEm(terra) == 1 && ObrasEm(y) == 0 && Barra(x) == densa && Barra(terra) == densa && !Barra(y));

				// ---- um processo novo: o mundo relido do disco
				GravarMundoEm(caminho);
				_noChao.RemoveAll(o => !o.DoMapa);
				SoltarInterioresVazios();
				CarregarMundoDeDisco(caminho);
				AplicarColisaoDasObras(terra);
				Obra? relidaX = _noChao.FirstOrDefault(o => !o.DoMapa && o.Zona.Equals(x)), relidaT = _noChao.FirstOrDefault(o => !o.DoMapa && o.Zona.Equals(terra));
				AfirmarIntr($"{m} **RELIDO DO DISCO (como um processo novo), CADA UMA VOLTA PRA SALA DELA**: uma na do planeta X, uma na da Terra, nenhuma na do planeta Y",
							relidaX != null && relidaT != null && !ReferenceEquals(relidaX, naX) && relidaX.Tipo == maquina && relidaT.Tipo == maquina
							&& ObrasEm(x) == 1 && ObrasEm(terra) == 1 && ObrasEm(y) == 0 && Barra(x) == densa && Barra(terra) == densa && !Barra(y),
							$"X {ObrasEm(x)}, Terra {ObrasEm(terra)}, Y {ObrasEm(y)}");

				_noChao.RemoveAll(o => !o.DoMapa);
				AplicarColisaoDasObras(terra);
				SoltarInterioresVazios();
			}

			// ---- a obra orfa: gravada, antes da recusa, num lugar em que nao se constroi mais
			string denso = IntrItemDeMaquina() ?? Regenerador.Tipo;
			var orfa = new Obra
			{
				Id = _proximaObraId++, Tipo = denso, ZonaTipo = mente.Kind, ZonaNome = mente.Name, ZonaSeed = mente.Seed,
				X = 50 * 32 + 16, Y = 50 * 32 + 8, DonoConta = "bancada_interiores", DonoNome = "bancada: o de antes",
			};
			_noChao.Add(orfa);
			GravarMundoEm(caminho);
			_noChao.RemoveAll(o => !o.DoMapa);
			SoltarInterioresVazios();
			CarregarMundoDeDisco(caminho);
			bool foraDoMundo = ObrasEm(mente) == 0 && _obrasSemLugar.Count(o => o.Zona.Equals(mente)) == 1 && !MapaDaZonaOuCatalogo(mente)!.BlockedCell(50, 50);
			GravarMundoEm(caminho);
			_noChao.RemoveAll(o => !o.DoMapa);
			_obrasSemLugar.Clear();
			CarregarMundoDeDisco(caminho);
			AfirmarIntr("**A OBRA ORFA** (gravada na mente #5 antes da recusa): ao carregar fica FORA do mundo -- nao esta na lista e nao barra na mente de quem for o #5 agora --, e "
						+ "CONTINUA no arquivo depois de o servidor regravar o mundo (ninguem apaga nada do arquivo de ninguem)",
						foraDoMundo && _obrasSemLugar.Count(o => o.Zona.Equals(mente)) == 1 && ObrasEm(mente) == 0,
						$"fora do mundo {foraDoMundo}; guardadas a parte {_obrasSemLugar.Count}");

			LugarDeObra.InterioresDeSessaoAceitamDeTeste = true;
			_noChao.RemoveAll(o => !o.DoMapa);
			_obrasSemLugar.Clear();
			SoltarInterioresVazios();
			CarregarMundoDeDisco(caminho);
			DefeitoIntr("os interiores de sessao aceitam (o jogo de antes de 2026-10-11): a obra gravada na mente #5 volta pro mundo -- esta na lista e BARRA na mente de quem for o #5 depois do reinicio",
						ObrasEm(mente) == 1 && MapaDaZonaOuCatalogo(mente)!.BlockedCell(50, 50));
			LugarDeObra.InterioresDeSessaoAceitamDeTeste = false;
		}
		finally
		{
			LugarDeObra.InterioresDeSessaoAceitamDeTeste = false;
			EstadoDeZonaPorNomeDeTeste = false;
			_noChao.Clear();
			_noChao.AddRange(noChaoDeAntes);
			_obrasSemLugar.Clear();
			_obrasSemLugar.AddRange(semLugarDeAntes);
			_proximaObraId = idDeAntes;
			GravarMundo();
			SoltarInterioresVazios();
			AplicarColisaoDasObras(terra);
		}
	}

	// =====================================================================
	// 2) AS PORTAS: um interior feito de um mapa de arquivo COM porta
	// =====================================================================
	private void IntrDasPortas()
	{
		GD.Print("[interiores] -- 2) a porta aberta numa instancia nao abre na outra (um interior feito de um mapa de arquivo com porta) --");
		(string? zona, PortaDoMapa porta) = _portasDoMapa
			.Where(kv => kv.Value.Count > 0 && _catalogo?.Get(kv.Key)?.Mapa != null)
			.OrderBy(kv => kv.Key, StringComparer.Ordinal)
			.Select(kv => ((string?)kv.Key, kv.Value[0])).FirstOrDefault();
		AfirmarIntr("(montagem) ha um mapa de arquivo com porta pra servir de planta (nenhum interior de hoje tem porta: o `.portas` do z13 "
					+ "e do z24 e vazio, e as plantas do Core nao tem nenhuma)", zona != null, zona ?? "nenhum");
		if (zona == null) return;

		ZoneKey a = ZoneKey.Interior(zona, IdBaseDaVigia + 701), b = ZoneKey.Interior(zona, IdBaseDaVigia + 702), deArquivo = ZoneKey.Premade(zona);
		ZoneCollision planta = _catalogo!.Get(zona)!.Mapa!;
		long relogio = AdiantoDoRelogioDeTeste;
		try
		{
			bool Aberta(ZoneKey z) => _portasAbertas.TryGetValue(ChaveDeEstado(z), out Dictionary<(int X, int Y), long>? ab) && ab.ContainsKey((porta.X, porta.Y));
			bool Barra(ZoneKey z) => MapaDaZonaOuCatalogo(z)!.BlockedCell(porta.X, porta.Y);

			AfirmarIntr($"(montagem) a porta ({porta.X},{porta.Y}) de {zona} nasce fechada nas duas instancias e no planeta de arquivo",
						Barra(a) && Barra(b) && Barra(deArquivo) && !Aberta(a) && !Aberta(b) && !Aberta(deArquivo));

			AbrirPorta(ChaveDeEstado(a), porta.X, porta.Y, NowMs());
			AfirmarIntr("**A PORTA ABERTA EM A SO ABRE EM A**: passa-se por ela em A; em B e no planeta de arquivo ela continua fechada -- e a lista de "
						+ "portas abertas que quem entra recebe (`MandarPortas`) e a da instancia dele",
						!Barra(a) && Barra(b) && Barra(deArquivo) && Aberta(a) && !Aberta(b) && !Aberta(deArquivo) && planta.BlockedCell(porta.X, porta.Y),
						$"A barra {Barra(a)}, B barra {Barra(b)}, {zona} barra {Barra(deArquivo)}");

			AdiantoDoRelogioDeTeste += PortaFechaEmMs + 1;
			TickDasPortas();
			AfirmarIntr("...e o relogio a fecha em A, pela chave de estado (o tique so tem a chave na mao: `MapasDoEstado`)", Barra(a) && !Aberta(a));
			AdiantoDoRelogioDeTeste = relogio;

			EstadoDeZonaPorNomeDeTeste = true;
			AbrirPorta(ChaveDeEstado(a), porta.X, porta.Y, NowMs());
			DefeitoIntr("as portas por NOME e a planta pra todos: abrir em A abre em B e no planeta de arquivo", !Barra(b) && !Barra(deArquivo) && Aberta(b),
						$"B barra {Barra(b)}, {zona} barra {Barra(deArquivo)}");
			AdiantoDoRelogioDeTeste += PortaFechaEmMs + 1;
			TickDasPortas();
			EstadoDeZonaPorNomeDeTeste = false;
			AfirmarIntr("(limpeza) a porta do planeta de arquivo voltou a fechar", planta.BlockedCell(porta.X, porta.Y) && !Aberta(deArquivo));
		}
		finally
		{
			EstadoDeZonaPorNomeDeTeste = false;
			AdiantoDoRelogioDeTeste = relogio;
			SoltarInterioresVazios();
		}
	}

	// =====================================================================
	// 3) OS MUNDOS SORTEADOS HOMONIMOS
	// =====================================================================
	private void IntrDosHomonimos()
	{
		GD.Print("[interiores] -- 3) o estrago de um mundo sorteado nao aparece no homonimo dele (o nome de mundo gerado NAO e unico) --");

		// QUANTOS HA: os mundos sorteados a ate `raio` celulas de sistema da Terra, contados por nome.
		const int raio = 60;
		var porNome = new Dictionary<string, HashSet<ulong>>(StringComparer.Ordinal);
		int mundos = 0;
		for (int sy = -raio; sy <= raio; sy++)
			for (int sx = -raio; sx <= raio; sx++)
			{
				if (Sistemas.Do(SeedDoUniverso, sx, sy) is not { } s) continue;
				for (int k = 0; k < s.Orbitas; k++)
				{
					if (k == s.OrbitaPreFeita) continue;
					PlanetaNoEspaco p = s.Planeta(k);
					mundos++;
					if (!porNome.TryGetValue(p.Nome, out HashSet<ulong>? seeds)) porNome[p.Nome] = seeds = [];
					seeds.Add(p.Seed);
				}
			}
		int repetidos = porNome.Values.Where(v => v.Count > 1).Sum(v => v.Count);
		KeyValuePair<string, HashSet<ulong>> exemplo = porNome.FirstOrDefault(kv => kv.Value.Count > 1);
		GD.Print($"[interiores]   MEDIDO: {mundos} mundos sorteados a ate {raio} celulas da Terra, {porNome.Count} nomes; "
				 + $"{repetidos} mundos ({100.0 * repetidos / Math.Max(1, mundos):0.0}%) dividem o nome com outro"
				 + (exemplo.Key != null ? $" -- p.ex. '{exemplo.Key}' x{exemplo.Value.Count}" : ""));

		ZoneKey m1 = ZoneKey.Procedural("Gemeo-da-vigia", 111), m2 = ZoneKey.Procedural("Gemeo-da-vigia", 222);
		try
		{
			AfirmarIntr("(montagem) dois mundos sorteados de MESMO nome e seeds diferentes sao zonas diferentes", m1.Name == m2.Name && m1.Hash != m2.Hash);
			_cenarioCaido[ChaveDeEstado(m1)] = [(3, 4)];
			List<(int X, int Y)> r1 = LerPacoteDeCenario(PacoteDeRetratoDeCenario(m1).CopyData()).Celulas;
			List<(int X, int Y)> r2 = LerPacoteDeCenario(PacoteDeRetratoDeCenario(m2).CopyData()).Celulas;
			AfirmarIntr("**O BURACO DE UM MUNDO NAO APARECE NO HOMONIMO**: a chave de estado de mundo sorteado e `nome#seed`; o retrato de um leva a "
						+ "celula, o do outro vai vazio", ChaveDeEstado(m1) != ChaveDeEstado(m2) && r1.Count == 1 && r2.Count == 0,
						$"{ChaveDeEstado(m1)} / {ChaveDeEstado(m2)}: retratos {r1.Count} e {r2.Count}");
			_cenarioCaido.Remove(ChaveDeEstado(m1));

			EstadoDeZonaPorNomeDeTeste = true;
			_cenarioCaido[ChaveDeEstado(m1)] = [(3, 4)];
			int doOutro = LerPacoteDeCenario(PacoteDeRetratoDeCenario(m2).CopyData()).Celulas.Count;
			DefeitoIntr("o estrago por NOME: o homonimo recebe o retrato com o buraco do outro mundo", doOutro == 1, $"{doOutro} celula(s)");
			_cenarioCaido.Remove(ChaveDeEstado(m1));
		}
		finally
		{
			EstadoDeZonaPorNomeDeTeste = false;
			_cenarioCaido.Remove(m1.Name);
			_cenarioCaido.Remove(m1.ToString());
			_chavesDeMundoSorteado.Remove(m1.Hash);
			_chavesDeMundoSorteado.Remove(m2.Hash);
		}
	}

	// =====================================================================
	// 4) A VIGIA DE FONTE: fabrica de interior nova no codigo
	// =====================================================================

	/// <summary>Esta linha de codigo fabrica uma chave de interior?</summary>
	private static bool IntrFabricaInterior(string linha)
	{
		string l = linha.TrimStart();
		if (l.StartsWith("//", StringComparison.Ordinal)) return false;
		return l.Contains("ZoneKey.Interior(", StringComparison.Ordinal) || l.Contains("new(KindInterior,", StringComparison.Ordinal)
			   || l.Contains("new ZoneKey(ZoneKey.KindInterior,", StringComparison.Ordinal) || l.Contains("new ZoneKey(KindInterior,", StringComparison.Ordinal);
	}

	/// <summary>Esta fabrica (arquivo relativo, linha de codigo) esta na lista da vigia?</summary>
	private static bool IntrFabricaConhecida(string arquivo, string linha) =>
		FabricasConhecidas.Any(f => string.Equals(f.Arquivo, arquivo, StringComparison.OrdinalIgnoreCase) && linha.Contains(f.Trecho, StringComparison.Ordinal));

	private void IntrDaFonte()
	{
		GD.Print("[interiores] -- 4) a vigia de fonte: todo `ZoneKey.Interior(` do jogo esta na lista (bancada e robo ficam de fora) --");
		string raiz = ProjectSettings.GlobalizePath("res://");
		var achadas = new List<(string Arquivo, int Linha, string Texto)>();
		int arquivos = 0;
		foreach (string pasta in new[] { "Core", "Server", "Client", "Net" })
		{
			string dir = System.IO.Path.Combine(raiz, pasta);
			if (!System.IO.Directory.Exists(dir)) continue;
			foreach (string arq in System.IO.Directory.EnumerateFiles(dir, "*.cs", System.IO.SearchOption.AllDirectories))
			{
				string nomeDoArquivo = System.IO.Path.GetFileName(arq);
				if (nomeDoArquivo.EndsWith("Teste.cs", StringComparison.Ordinal) || nomeDoArquivo.EndsWith("Prova.cs", StringComparison.Ordinal)
					|| nomeDoArquivo.EndsWith("Palco.cs", StringComparison.Ordinal) || nomeDoArquivo.StartsWith("Robo", StringComparison.Ordinal)) continue;
				arquivos++;
				string relativo = System.IO.Path.GetRelativePath(raiz, arq).Replace('\\', '/');
				int n = 0;
				foreach (string linha in System.IO.File.ReadLines(arq))
				{
					n++;
					if (IntrFabricaInterior(linha)) achadas.Add((relativo, n, linha.Trim()));
				}
			}
		}

		AfirmarIntr("(montagem) a vigia leu o codigo do jogo", arquivos > 100, $"{arquivos} arquivo(s) em {raiz}");
		if (arquivos == 0) return;

		List<(string Arquivo, int Linha, string Texto)> novas = achadas.Where(f => !IntrFabricaConhecida(f.Arquivo, f.Texto)).ToList();
		AfirmarIntr("**NENHUMA FABRICA DE INTERIOR FORA DA LISTA**: todo `ZoneKey.Interior(` do jogo e de um tipo que o funil conhece e que a vigia roda",
					novas.Count == 0,
					novas.Count == 0 ? $"{achadas.Count} fabrica(s): {string.Join("; ", achadas.Select(f => $"{f.Arquivo}:{f.Linha}"))}"
									 : "NOVA(S): " + string.Join("; ", novas.Select(f => $"{f.Arquivo}:{f.Linha} `{f.Texto}`"))
									   + " -- de a ela um tipo em `TiposDeInterior`, uma linha em `LinhasDaVigia` e uma entrada em `FabricasConhecidas`");
		List<(string Arquivo, string Trecho, string Tipo)> sumidas =
			FabricasConhecidas.Where(f => !achadas.Any(x => string.Equals(x.Arquivo, f.Arquivo, StringComparison.OrdinalIgnoreCase)
															&& x.Texto.Contains(f.Trecho, StringComparison.Ordinal))).ToList();
		AfirmarIntr("...e toda fabrica da lista ainda existe no codigo (lista velha tambem e vermelho)", sumidas.Count == 0,
					string.Join("; ", sumidas.Select(f => $"{f.Arquivo} `{f.Trecho}`")));
		AfirmarIntr("...e todo tipo da lista de fabricas e um tipo do funil",
					FabricasConhecidas.Skip(1).All(f => TiposDeInterior.Any(t => t.Nome == f.Tipo)));

		DefeitoIntr("uma fabrica nova (`ZoneKey.Interior(\"Casa\", id)` num arquivo qualquer do servidor): a prova de cima REPROVA",
					IntrFabricaInterior("\t\tZoneKey casa = ZoneKey.Interior(\"Casa\", (ulong)id);")
					&& !IntrFabricaConhecida("Server/GameServer.Casas.cs", "ZoneKey casa = ZoneKey.Interior(\"Casa\", (ulong)id);"));
		AfirmarIntr("...e comentario que cita a fabrica nao conta", !IntrFabricaInterior("\t/// O interior e `ZoneKey.Interior(\"Nave\", id)`."));
	}

	// =====================================================================
	// 5) O CUSTO DA COPIA
	// =====================================================================
	private void IntrDaMemoria()
	{
		GD.Print("[interiores] -- 5) quanto custa a copia de uma instancia, e a memoria que volta quando ela morre --");
		const int n = 2000;
		SoltarInterioresVazios();
		int vivasAntes = _interiores.Count;
		long antes = GC.GetTotalMemory(true);
		for (int i = 0; i < n; i++) MapaDaVista(NaveGrande.ZonaDoInterior(IdBaseDaVigia + 2_000 + i));
		long com = GC.GetTotalMemory(true);
		int vivas = _interiores.Count - vivasAntes;
		int soltas = SoltarInterioresVazios();
		long depois = GC.GetTotalMemory(true);

		double porInstancia = (com - antes) / (double)n;
		ZoneCollision planta = NaveGrande.Planta().Colisao;
		int bitset = (planta.Width * planta.Height + 7) / 8;
		GD.Print($"[interiores]   MEDIDO: {n} interiores de nave vivos = {(com - antes) / 1024.0:0} KB ({porInstancia:0} bytes cada, com os dois mapas, o "
				 + $"registro e a chave); copiar os bitsets custaria {2 * bitset} bytes cada. Soltos, voltam {(com - depois) / 1024.0:0} KB.");
		AfirmarIntr($"**A COPIA E BARATA**: uma instancia viva custa menos de 1 KB (os planos do arquivo nao sao copiados: seriam {2 * bitset} bytes)",
					vivas == n && porInstancia > 0 && porInstancia < 1024, $"{porInstancia:0} bytes por instancia");
		AfirmarIntr("**A COPIA MORRE**: vazias, as instancias sao soltas e a memoria volta",
					soltas >= n && _interiores.Count == vivasAntes && com - depois > (com - antes) / 2,
					$"{soltas} solta(s); {(com - depois) / 1024.0:0} KB de volta de {(com - antes) / 1024.0:0}");
	}
}
