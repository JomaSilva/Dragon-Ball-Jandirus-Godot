using Godot;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A NUVEM COM UM CORPO EM CIMA DELA (`--nuvemviva`) -- e ela existe porque a outra bancada da nuvem
/// **nao pode** responder isto.
///
/// ============================ O QUE A `nuvem-prova` MEDE, E O QUE ELA SE RECUSA A MEDIR ============================
/// A `dotnet run --project Tools/AssetPipeline -- nuvem-prova` tem 62 provas verdes e nenhuma delas
/// e sobre um jogador. Ela le os `.nuvem` do disco, conta celula, confere destino declarado, cruza
/// nuvem com agua e exercita a funcao pura (`ClasseDeNuvem.Travessia`). Isso e o DADO e a PLANTA.
///
/// O pedido do dono, porem, e um acontecimento: *"se um jogador ir nelas SEM ESTAR COM FLY ATIVADO,
/// ele vai automaticamente ser JOGADO NO MAPA DO INFERNO"*. Entre a planta e o acontecimento moram
/// seis coisas que nenhuma prova offline alcanca -- o laco do tique, a guarda de KO, a carencia
/// compartilhada com as passagens, o funil `ModoDeTravessiaDe`, o `MoveToZone` e o
/// `PontoLivrePerto` da zona de CHEGADA. Qualquer uma delas quebrada deixa as 62 provas VERDES e o
/// jogador andando por cima do ceu.
///
/// Esta bancada poe um corpo em cima de uma celula de nuvem DE VERDADE, roda o
/// <see cref="TickDasNuvens"/> DE PRODUCAO, e pergunta em que zona ele acordou.
/// ================================================================================================================
///
/// ============================ AS DUAS METADES, SEMPRE JUNTAS ============================
/// Cada familia de queda tem duas provas que se seguram: **cai a pe** e **NAO cai voando**. Sozinha,
/// a primeira fica verde num jogo em que a nuvem derruba todo mundo (inclusive quem voa, que e o
/// oposto do pedido); sozinha, a segunda fica verde num jogo em que a nuvem nao derruba ninguem.
/// Elas so significam alguma coisa uma ao lado da outra, e por isso nenhuma familia aqui tem so uma.
///
/// E o DESTINO e conferido pelo nome: "saiu do Caminho da Serpente" nao e a mesma frase que "chegou
/// no Inferno", e ha um defeito injetado (`Destino` trocado de zona) que separa exatamente as duas.
/// =========================================================================================
///
/// ============================ COMO ELA REPROVA ============================
/// Do jeito desta casa: rodando as MESMAS provas contra o codigo mutante, pelas
/// <see cref="SondasDaNuvem"/> (o mesmo desenho do `SondasDoVacuo`). Sao onze defeitos, e cada um
/// deles e uma frase que ja foi bug em algum sistema daqui:
///
///   * *"a metade do voo caiu"* -- a `Travessia` passa a derrubar todo mundo;
///   * *"a nuvem parou de derrubar"* -- ela passa a so bloquear;
///   * *"o funil do modo travou em VOANDO"* -- ninguem cai, e por outro caminho;
///   * *"o destino sumiu"* e *"o destino trocou de zona"* -- o Caminho da Serpente cospe na Terra;
///   * *"o funil de pouso saiu do caminho"* -- o corpo chega dentro de parede.
///
/// Um defeito que nao deixe a familia vermelha e reportado como **[CEGA]**, com nome e tudo.
/// ==========================================================================
/// </summary>
public partial class GameServer
{
	private bool _nuvemVivaDeTeste;

	/// <summary>Faixa de id propria, longe de qualquer jogador. Mesma pratica da bancada do vacuo.</summary>
	private const int IdBaseDaNuvemDeTeste = 941_000;

	private int _nuvProximoId;

	private readonly List<ServerPlayer> _corposDaNuvem = [];

	// =====================================================================
	// O MOTOR -- placar, familias e injecao (irmao do `PlacarDoVacuo`)
	// =====================================================================
	private sealed class PlacarDaNuvem
	{
		public int Ok, Falhas;
		public bool Mudo;
		public readonly List<string> Vermelhas = [];
		public readonly List<string> SemCobertura = [];

		public void Prova(string oQue, bool passou, string detalhe = "")
		{
			if (passou) Ok++; else { Falhas++; Vermelhas.Add(oQue); }
			if (Mudo) return;
			if (passou) GD.Print($"[nuvem]   ok    {oQue}   {detalhe}");
			else GD.PrintErr($"[nuvem]   FALHA {oQue}   {detalhe}");
		}

		/// <summary>Prova que nao deu pra fazer (falta o mapa, falta o plano). Nao e "ok".</summary>
		public void NaoDeu(string oQue)
		{
			SemCobertura.Add(oQue);
			if (!Mudo) GD.Print($"[nuvem]   --    {oQue}  (sem cobertura)");
		}
	}

	private sealed class FamiliaDaNuvem
	{
		public required string Nome { get; init; }
		public required string Frase { get; init; }
		public required Action<PlacarDaNuvem> Provas { get; init; }
		public required List<(string Nome, Action<SondasDaNuvem> Injetar)> Defeitos { get; init; }
	}

	/// <summary>
	/// RODA A BANCADA INTEIRA. Chamada no primeiro login (ver `GameServer.cs`), como a do vacuo, e
	/// pelo mesmo motivo: no boot as zonas do catalogo ainda podem nao ter mapa carregado, e a
	/// bancada mediria nuvem nenhuma.
	/// </summary>
	public void RodarBancadaDaNuvemViva(ServerPlayer host)
	{
		GD.Print("[nuvem] ============ A NUVEM COM UM CORPO EM CIMA -- as duas metades, sempre juntas ============");

		// ============================ O HOST NAO PODE ESTAR NUMA ZONA DE NUVEM ============================
		// O `TickDasNuvens` de producao varre `_players` INTEIRO, e os defeitos injetados aqui rodam
		// por baixo dele. O defeito *"a Travessia derruba todo mundo"* mandaria o personagem do dono
		// pro Inferno se ele estivesse no Caminho da Serpente -- e ele nao saberia por que.
		//
		// Isto e o achado da bancada do vacuo, repetido: *"uma bancada que estraga o mundo pra medir
		// o mundo e uma bancada que ninguem pode rodar duas vezes"*. Aqui o remedio e mais barato que
		// la (ninguem perde vida), mas o retrato existe do mesmo jeito -- ver `RetratarQuemNaoEMeu`.
		// ==================================================================================================
		if (MapaDaZonaOuCatalogo(host.Zone) is { TemNuvem: true, NuvemDerruba: true })
		{
			GD.PrintErr($"[nuvem] ABORTADA: o host esta em `{host.Zone.Name}`, que derruba -- "
					  + "um defeito injetado o jogaria de zona. Saia da nuvem antes.");
			return;
		}

		List<FamiliaDaNuvem> familias =
		[
			OCaminhoDaSerpente(),
			OTemploSagrado(),
			AChegadaEChaoLivre(),
			AsOutrasNuvensSoBarram(),
		];

		int provas = 0, falhas = 0, defeitos = 0, cegos = 0, semCobertura = 0;
		var buracos = new List<string>();

		foreach (FamiliaDaNuvem f in familias)
		{
			GD.Print($"[nuvem] === {f.Nome} ===");
			GD.Print($"[nuvem]     \"{f.Frase}\"");

			var sao = new PlacarDaNuvem();
			RodarProvasDaNuvem(f, sao);
			provas += sao.Ok + sao.Falhas;
			falhas += sao.Falhas;
			semCobertura += sao.SemCobertura.Count;
			foreach (string s in sao.SemCobertura) buracos.Add($"{f.Nome}: {s}");

			GD.Print("[nuvem]   -- e ela reprova assim:");
			foreach ((string nome, Action<SondasDaNuvem> injetar) in f.Defeitos)
			{
				defeitos++;
				var mutante = new SondasDaNuvem();
				injetar(mutante);

				var p = new PlacarDaNuvem { Mudo = true };
				_sondasDaNuvem = mutante;
				RodarProvasDaNuvem(f, p);

				if (p.Falhas == 0)
				{
					cegos++;
					GD.PrintErr($"[nuvem]      [CEGA] {nome}");
					GD.PrintErr("[nuvem]             ...a familia continuou VERDE com o defeito dentro.");
					buracos.Add($"{f.Nome}: cega para \"{nome}\"");
				}
				else
				{
					GD.Print($"[nuvem]      [pega] {nome}");
					GD.Print($"[nuvem]             -> {p.Falhas} prova(s) em vermelho, a 1a: \"{CurtoNuvem(p.Vermelhas[0])}\"");
				}
			}
		}

		GD.Print("[nuvem] ================ PLACAR ================");
		GD.Print($"[nuvem]   familias           : {familias.Count}");
		GD.Print($"[nuvem]   provas             : {provas}   ({provas - falhas} verdes, {falhas} vermelhas)");
		GD.Print($"[nuvem]   defeitos injetados : {defeitos}   ({defeitos - cegos} pegos, {cegos} passaram batido)");
		GD.Print($"[nuvem]   provas sem rodar   : {semCobertura}");
		foreach (string b in buracos) GD.PrintErr($"[nuvem]     - {b}");
		GD.Print(falhas == 0 && cegos == 0
			? "[nuvem] ================ OK -- toda familia esta verde E sabe ficar vermelha ================"
			: "[nuvem] ================ ATENCAO -- ha familia vermelha ou cega acima ================");
	}

	/// <summary>
	/// Roda uma familia e LIMPA TUDO depois -- inclusive quando ela estoura. Copia deliberada do
	/// `RodarProvas` do vacuo: devolver `_sondasDaNuvem = null` remonta os padroes de producao, que e
	/// mais seguro que repor campo a campo (um campo esquecido ficaria mutante no servidor vivo).
	/// </summary>
	private void RodarProvasDaNuvem(FamiliaDaNuvem f, PlacarDaNuvem p)
	{
		try { RetratarQuemNaoEMeu(); f.Provas(p); }
		catch (Exception e)
		{
			// Estourar tambem e reprovar -- so nao pode passar calado.
			p.Prova($"estourou: {e.GetType().Name} {e.Message}", false);
		}
		finally
		{
			LimparAOficinaDaNuvem();
			_sondasDaNuvem = null;
			DevolverQuemNaoEMeu();
		}
	}

	/// <summary>
	/// ZONA E POSICAO DE QUEM NAO E DA BANCADA, guardadas antes de cada rodada.
	///
	/// O `TickDasNuvens` de producao varre `_players` inteiro, e um defeito injetado pode despachar
	/// um NPC que por acaso esteja numa zona de nuvem. Sem isto, a bancada mudaria o mundo de lugar
	/// pra medir o mundo -- e o rastro apareceria na familia seguinte, como ja apareceu na do vacuo.
	/// </summary>
	private readonly List<(ServerPlayer Pl, ZoneKey Zona, Vec2 Pos)> _retratoDaNuvem = [];

	private void RetratarQuemNaoEMeu()
	{
		_retratoDaNuvem.Clear();
		foreach (ServerPlayer pl in _players.Values) _retratoDaNuvem.Add((pl, pl.Zone, pl.Pos));
	}

	private void DevolverQuemNaoEMeu()
	{
		int mexidos = 0;
		foreach ((ServerPlayer pl, ZoneKey zona, Vec2 pos) in _retratoDaNuvem)
		{
			if (!_players.ContainsKey(pl.Id)) continue;          // corpo da bancada, ja removido
			if (pl.Zone.Hash == zona.Hash && pl.Pos.X == pos.X && pl.Pos.Y == pos.Y) continue;
			MoveToZone(pl.Id, zona, pos);
			mexidos++;
		}
		if (mexidos > 0)
			GD.Print($"[nuvem]       (o defeito injetado tinha mudado {mexidos} corpo(s) de FORA da bancada "
				   + "de lugar -- devolvido)");
		_retratoDaNuvem.Clear();
	}

	private static string CurtoNuvem(string s) => s.Length <= 80 ? s : s[..78] + "..";

	// =====================================================================
	// FAMILIA 1 -- O CAMINHO DA SERPENTE  ->  O INFERNO
	// =====================================================================
	/// <summary>
	/// *"as NUVENS q tem no CAMINHO DA SERPENTE no outro mundo, se um jogador ir nelas SEM ESTAR COM
	/// FLY ATIVADO, ele vai automaticamente ser JOGADO NO MAPA DO INFERNO"*.
	///
	/// Porte literal do `SkyHD2.Enter()` (`NewTurfs.dm:197-211`), e a prova checa o DESTINO pelo nome
	/// da zona -- nao basta "saiu de onde estava".
	/// </summary>
	private FamiliaDaNuvem OCaminhoDaSerpente() => new()
	{
		Nome = "1 -- o Caminho da Serpente derruba pro Inferno",
		Frase = "se um jogador ir nelas SEM ESTAR COM FLY ATIVADO, ele vai automaticamente ser "
			  + "JOGADO NO MAPA DO INFERNO",
		Provas = p => AQuedaDeUmaZona(p, Alem.ZonaDoOutroMundo, Alem.ZonaDoInferno),
		// O destino trocado e a TERRA -- que e o destino LEGITIMO da outra familia. Escolher uma zona
		// que o sistema ja usa e mais duro que inventar uma: prova que a prova le o nome certo, e nao
		// so "chegou em alguma zona que existe".
		Defeitos = DefeitosDaQueda(ClasseDeNuvem.ZonaDaTerra),
	};

	// =====================================================================
	// FAMILIA 2 -- O TEMPLO SAGRADO  ->  A TERRA
	// =====================================================================
	/// <summary>
	/// *"e no mapa do LOOKOUT se o jogador cair na nuvem do mapa sem fly, ele CAI DE VOLTA PRA
	/// TERRA"*.
	///
	/// Isto **nao** e porte: no DM o `Sky1.Enter()` (`Turfs.dm:81-84`) so BARRA. E desenho novo, e a
	/// coordenada de chegada e a que a escada do Templo ja usa (`fromeg`). Ver
	/// <see cref="ClasseDeNuvem.DestinoDaQueda"/>.
	/// </summary>
	private FamiliaDaNuvem OTemploSagrado() => new()
	{
		Nome = "2 -- o Templo derruba de volta pra Terra",
		Frase = "no mapa do LOOKOUT se o jogador cair na nuvem do mapa sem fly, ele CAI DE VOLTA PRA TERRA",
		Provas = p => AQuedaDeUmaZona(p, ClasseDeNuvem.ZonaDoTemplo, ClasseDeNuvem.ZonaDaTerra),
		Defeitos = DefeitosDaQueda(Alem.ZonaDoInferno),
	};

	/// <summary>
	/// OS CINCO DEFEITOS QUE AS DUAS FAMILIAS DE QUEDA COMPARTILHAM.
	///
	/// <paramref name="zonaErrada"/> e o destino trocado -- e ele PRECISA ser uma zona que exista e
	/// que nao seja a certa, senao o defeito viraria "a queda nao acontece" e ficaria
	/// indistinguivel do defeito do `Destino = null`. Duas injecoes que reprovam pela mesma linha
	/// nao provam duas coisas: provam uma, duas vezes.
	/// </summary>
	private static List<(string, Action<SondasDaNuvem>)> DefeitosDaQueda(string zonaErrada) =>
	[
		("a metade do voo caiu: a nuvem derruba TODO MUNDO, inclusive quem voa",
		 s => s.Travessia = (_, _) => TravessiaDaNuvem.Derruba),

		("a nuvem parou de derrubar: ela so bloqueia (o estado ANTERIOR a esta tarefa)",
		 s => s.Travessia = (_, _) => TravessiaDaNuvem.Bloqueia),

		("o funil do modo travou em VOANDO (ninguem cai, e por outro caminho)",
		 s => s.Modo = _ => ModoDeTravessia.Voando),

		("o destino sumiu: a zona derruba e nao sabe pra onde",
		 s => s.Destino = _ => null),

		($"o destino trocou de zona: a queda cospe em `{zonaErrada}`",
		 s => s.Destino = _ => (zonaErrada, 128, 162)),
	];

	/// <summary>
	/// AS DUAS METADES DE UMA ZONA QUE DERRUBA, medidas com o tique de producao.
	///
	/// ============================ POR QUE O CORPO E POSTO NO CENTRO DA CELULA ============================
	/// Porque o <see cref="MoveRules.NaNuvem"/> pergunta pelas QUATRO QUINAS da caixa dos pes, e uma
	/// quina fora da nuvem ja bastaria pro laco ignorar o corpo -- a prova ficaria vermelha por
	/// enquadramento e nao por defeito. A escolha da celula exige um bloco 3x3 inteiro de nuvem
	/// (<see cref="CelulaDeNuvemFolgada"/>), que e mais folga do que a caixa precisa.
	/// ====================================================================================================
	/// </summary>
	private void AQuedaDeUmaZona(PlacarDaNuvem p, string zonaOrigem, string zonaDestino)
	{
		ZoneEntry? origem = _catalogo?.Get(zonaOrigem);
		if (origem?.Mapa is not { } mapa)
		{
			p.NaoDeu($"a zona `{zonaOrigem}` nao tem mapa carregado");
			return;
		}

		// PRECONDICOES -- elas sao provas de verdade e nao enfeite: sem plano de nuvem, TUDO abaixo
		// ficaria "nao caiu", e "nao caiu" e o resultado esperado da metade do voo. Ou seja: um
		// `.nuvem` que nao carregasse deixaria metade da familia VERDE de graca.
		p.Prova($"{zonaOrigem}: o plano de nuvem esta carregado", mapa.TemNuvem);
		p.Prova($"{zonaOrigem}: esta zona DERRUBA (e nao so barra)", mapa.NuvemDerruba);
		if (!mapa.TemNuvem || !mapa.NuvemDerruba) return;

		if (CelulaDeNuvemFolgada(mapa) is not { } celula)
		{
			p.NaoDeu($"{zonaOrigem}: nao achei bloco 3x3 de nuvem pra pousar o corpo");
			return;
		}

		Vec2 emCima = mapa.CentroDaCelula(celula.X, celula.Y);
		p.Prova($"{zonaOrigem}: a celula escolhida ({celula.X},{celula.Y}) e nuvem pela caixa dos pes",
				MoveRules.NaNuvem(mapa, emCima));

		// ---------- METADE 1: A PE, CAI ----------
		ServerPlayer aPe = CorpoDeNuvem("bancada: o que anda", ZoneKey.Premade(zonaOrigem), emCima);
		aPe.Altitude = 0f;
		aPe.Nadando = false;
		aPe.TiquesDeVoo = 0;

		p.Prova($"{zonaOrigem}: antes do tique o corpo a pe esta NA nuvem",
				aPe.Zone.Name == zonaOrigem, $"em ({aPe.Pos.X:0},{aPe.Pos.Y:0})");

		TickDasNuvens();

		p.Prova($"{zonaOrigem}: quem anda a pe SAIU da nuvem", aPe.Zone.Name != zonaOrigem,
				$"acabou em `{aPe.Zone.Name}`");
		p.Prova($"{zonaOrigem}: e chegou em `{zonaDestino}` (o destino, pelo nome)",
				string.Equals(aPe.Zone.Name, zonaDestino, StringComparison.OrdinalIgnoreCase),
				$"acabou em `{aPe.Zone.Name}`");

		// ---------- METADE 2: VOANDO, NAO CAI ----------
		// A METADE QUE SEGURA A PRIMEIRA. Sem ela, uma nuvem que derrubasse todo mundo -- o oposto do
		// pedido -- passaria com nota cheia.
		ServerPlayer voando = CorpoDeNuvem("bancada: o que voa", ZoneKey.Premade(zonaOrigem), emCima);
		voando.Altitude = 64f;      // `ModoDeTravessiaDe` le ALTURA e nao a flag -- ver `GameServer.Nado.cs`
		voando.Nadando = false;
		voando.TiquesDeVoo = 0;

		p.Prova($"{zonaOrigem}: o corpo que voa e lido como VOANDO pelo funil do nado",
				ModoDeTravessiaDe(voando) == ModoDeTravessia.Voando);

		TickDasNuvens();

		p.Prova($"{zonaOrigem}: quem VOA nao cai -- continua na nuvem", voando.Zone.Name == zonaOrigem,
				$"acabou em `{voando.Zone.Name}`");

		// ---------- E A CHEGADA E CHAO LIVRE ----------
		// (a familia 3 mede isto contra um destino HOSTIL; aqui e contra o destino de verdade)
		if (_catalogo?.Get(zonaDestino)?.Mapa is { } destino)
		{
			int cx = (int)MathF.Floor(aPe.Pos.X / ZoneCollision.TileSize);
			int cy = (int)MathF.Floor(aPe.Pos.Y / ZoneCollision.TileSize);
			p.Prova($"{zonaOrigem}: quem caiu chegou em chao livre, nao dentro de parede",
					destino.ServeDeChao(cx, cy), $"celula ({cx},{cy}) de `{zonaDestino}`");
		}
		else p.NaoDeu($"a zona de chegada `{zonaDestino}` nao tem mapa pra conferir o pouso");
	}

	// =====================================================================
	// FAMILIA 3 -- QUEM CAI CHEGA EM CHAO LIVRE
	// =====================================================================
	/// <summary>
	/// *"Quem cai pela nuvem nao pode ficar preso nem cair dentro de parede -- use o funil de pouso
	/// que ja existe"*.
	///
	/// ============================ POR QUE O DESTINO E FORCADO PRA UM LUGAR RUIM ============================
	/// Porque a coordenada de verdade (`128,162` na Terra) **ja e chao livre**, e uma prova contra ela
	/// fica verde com o funil ligado E com o funil desligado -- ou seja nao prova o funil, prova a
	/// sorte da coordenada. Foi medido: sem esta familia, o defeito *"o funil de pouso saiu do
	/// caminho"* passaria batido.
	///
	/// Entao aqui o destino e apontado, de proposito, pra uma celula que o proprio mapa recusa
	/// (parede, borda, agua ou nuvem -- <see cref="ZoneCollision.ServeDeChao"/> diz nao). Com o funil
	/// no caminho o corpo tem que chegar em outro lugar, LIVRE; sem ele, chega dentro da pedra.
	/// ========================================================================================================
	/// </summary>
	private FamiliaDaNuvem AChegadaEChaoLivre() => new()
	{
		Nome = "3 -- quem cai chega em chao livre",
		Frase = "quem cai pela nuvem nao pode ficar preso nem cair dentro de parede",
		Provas = OPousoNaoEDentroDePedra,
		Defeitos =
		[
			("o funil de pouso saiu do caminho (o corpo chega na coordenada crua)",
			 s => s.UsarFunilDePouso = false),
		],
	};

	private void OPousoNaoEDentroDePedra(PlacarDaNuvem p)
	{
		ZoneEntry? origem = _catalogo?.Get(Alem.ZonaDoOutroMundo);
		ZoneEntry? chegada = _catalogo?.Get(Alem.ZonaDoInferno);
		if (origem?.Mapa is not { } mapa || chegada?.Mapa is not { } mc)
		{
			p.NaoDeu("faltou mapa da origem ou da chegada");
			return;
		}

		if (CelulaDeNuvemFolgada(mapa) is not { } celula)
		{
			p.NaoDeu("nao achei bloco 3x3 de nuvem pra pousar o corpo");
			return;
		}

		// UMA CELULA QUE O MAPA RECUSA -- achada, nao digitada. Uma coordenada escrita a mao
		// envelheceria no dia em que alguem reconvertesse o mapa, e envelheceria CALADA: a prova
		// viraria "o funil consertou um ponto que ja estava bom", que e sempre verde.
		if (CelulaRuim(mc) is not { } ruim)
		{
			p.NaoDeu($"a zona `{Alem.ZonaDoInferno}` nao tem celula recusada pra mirar");
			return;
		}

		// A coordenada BYOND que produz aquela celula, pela formula do `ClasseDeNuvem.EmPixel`
		// (`cx = bx-1`, `cy = altura-by`) invertida. Ela vai pela sonda `Destino` -- que na rodada
		// SA continua sendo a de producao pra todo o resto.
		int bx = ruim.X + 1;
		int by = chegada.H - ruim.Y;

		// ============================ A MIRA E **ESCRITA POR CIMA**, NAO SUBSTITUIDA ============================
		// Isto era `_sondasDaNuvem = new SondasDaNuvem { Destino = ... }`, e a primeira rodada mostrou
		// o preco: o objeto novo APAGAVA o defeito injetado (`UsarFunilDePouso = false`) um instante
		// antes de a prova rodar, e a familia saiu **[CEGA]** -- verde com o funil desligado dentro.
		//
		// Escrever no objeto que ja esta la (que na rodada sa E o de producao, e na mutante e o
		// mutante) e o que faz a mira da bancada COMPOR com o defeito em vez de o atropelar.
		// =========================================================================================================
		SondasNuvem.Destino = _ => (Alem.ZonaDoInferno, bx, by);
		bool funilLigado = SondasNuvem.UsarFunilDePouso;

		p.Prova($"a mira e mesmo uma celula que o mapa RECUSA ({ruim.X},{ruim.Y})",
				!mc.ServeDeChao(ruim.X, ruim.Y));

		ServerPlayer quemCai = CorpoDeNuvem("bancada: o que mira na pedra",
											ZoneKey.Premade(Alem.ZonaDoOutroMundo),
											mapa.CentroDaCelula(celula.X, celula.Y));
		quemCai.Altitude = 0f;
		TickDasNuvens();

		p.Prova("o corpo caiu (senao nao ha pouso pra medir)",
				quemCai.Zone.Name == Alem.ZonaDoInferno, $"acabou em `{quemCai.Zone.Name}`");

		int px = (int)MathF.Floor(quemCai.Pos.X / ZoneCollision.TileSize);
		int py = (int)MathF.Floor(quemCai.Pos.Y / ZoneCollision.TileSize);

		p.Prova("quem cai chega em chao LIVRE mesmo mirando numa celula recusada",
				mc.ServeDeChao(px, py),
				$"funil={(funilLigado ? "ligado" : "DESLIGADO")}  mirou ({ruim.X},{ruim.Y}) "
			  + $"-> pousou ({px},{py})");

		p.Prova("e nao pousou em cima de nuvem (o `ServeDeChao` da nuvem e falso)",
				!mc.EhNuvem(px, py));
	}

	// =====================================================================
	// FAMILIA 4 -- O CONTRA-EXEMPLO: AS OUTRAS NUVENS SO BARRAM
	// =====================================================================
	/// <summary>
	/// AS TRES ZONAS QUE **NAO** DERRUBAM -- Ceu (z10), Outside (z30) e Reino dos Deuses (z31).
	///
	/// ============================ SEM ISTO, "DERRUBAR TUDO" PASSARIA VERDE ============================
	/// As familias 1 e 2 ficariam com nota cheia num jogo em que TODA nuvem cospe o jogador pra outro
	/// mapa. Esta familia e o contra-exemplo do sistema inteiro: no DM aquelas tres so tem o `Enter()`
	/// que BARRA (`NewTurfs.dm:194-196` e `Turfs.dm:81-84`), sem queda, sem destino e sem mensagem.
	///
	/// E ela mede as duas pontas da mesma afirmacao: o Core nao declara destino, e o corpo posto em
	/// cima nao sai do lugar quando o tique de producao roda.
	/// ==================================================================================================
	/// </summary>
	private FamiliaDaNuvem AsOutrasNuvensSoBarram() => new()
	{
		Nome = "4 -- as outras nuvens so BARRAM (o contra-exemplo)",
		Frase = "o Ceu e o Reino dos Deuses nao derrubam ninguem -- la o Enter() do original so recusa",
		Provas = p =>
		{
			int olhadas = 0;
			foreach (ZoneEntry e in _catalogo?.Entradas ?? [])
			{
				if (e.Mapa is not { TemNuvem: true } mapa) continue;
				if (ClasseDeNuvem.Derruba(e.Zona)) continue;   // as duas de cima tem familia propria
				olhadas++;

				// ============================ PELA SONDA, E NAO PELA FUNCAO DIRETA ============================
				// Escrito `ClasseDeNuvem.DestinoDaQueda(e.Zona)` na mao, este `Prova` media uma funcao
				// que o servidor talvez nem chame -- e foi assim que a familia saiu **[CEGA]** na
				// primeira rodada: o defeito *"toda nuvem passou a derrubar"* troca a sonda, a prova
				// olhava para o outro lado, e o contra-exemplo ficou verde com o defeito dentro.
				//
				// Na rodada sa `SondasNuvem.Destino` **e** `ClasseDeNuvem.DestinoDaQueda` (ver
				// `SondasDaNuvem`), entao a afirmacao nao mudou: mudou de onde ela e lida.
				// ==============================================================================================
				p.Prova($"{e.Zona}: o destino que o SERVIDOR consulta nao existe pra esta zona",
						SondasNuvem.Destino(e.Zona) == null);
				p.Prova($"{e.Zona}: o plano gravado concorda -- a zona nao derruba",
						!SondasNuvem.ZonaDerruba(mapa));
				p.Prova($"{e.Zona}: e a nuvem BARRA quem esta a pe",
						ClasseDeNuvem.Bloqueia(ModoDeTravessia.APe, zonaDerruba: false));

				if (CelulaDeNuvemFolgada(mapa) is not { } c) continue;
				ServerPlayer parado = CorpoDeNuvem($"bancada: parado em {e.Zona}",
												   ZoneKey.Premade(e.Zona),
												   mapa.CentroDaCelula(c.X, c.Y));
				parado.Altitude = 0f;
				TickDasNuvens();
				p.Prova($"{e.Zona}: o corpo a pe em cima dela NAO muda de zona",
						parado.Zone.Name == e.Zona, $"acabou em `{parado.Zone.Name}`");
			}

			// SE NAO HOUVER NENHUMA, A FAMILIA NAO E VERDE -- e "sem cobertura". Uma varredura vazia
			// passa em todo `foreach` que existe.
			if (olhadas == 0) p.NaoDeu("nenhuma zona de nuvem que so barra foi encontrada no catalogo");
			else p.Prova($"ha zona de nuvem que so barra pra servir de contra-exemplo ({olhadas})", true);
		},
		Defeitos =
		[
			// O defeito que esta familia existe pra pegar: dar destino a QUEM NAO TEM. Ele deixaria as
			// familias 1 e 2 verdes e mandaria pro Inferno todo mundo que encostasse no Ceu.
			("toda nuvem ganhou destino: o Ceu passou a apontar pro Inferno",
			 s => s.Destino = _ => (Alem.ZonaDoInferno, 63, 260)),

			// E O SEGUNDO, QUE E O QUE CHEGA NO TIQUE. Sao DOIS campos porque o laco de producao tem
			// duas guardas em serie: sem a `ZonaDerruba` o corpo nem e visitado, e sem o `Destino` o
			// `Cair` desiste na primeira linha. Injetar so um deixaria o corpo parado -- e "parado" e
			// o resultado ESPERADO aqui, ou seja o defeito passaria batido.
			("a guarda que protege o Ceu caiu: toda zona de nuvem passou a derrubar",
			 s => { s.ZonaDerruba = _ => true; s.Destino = _ => (Alem.ZonaDoInferno, 63, 260); }),
		],
	};

	// =====================================================================
	// A OFICINA
	// =====================================================================
	/// <summary>
	/// UM CORPO SEM DONO em cima da celula pedida. `Peer = null`, ou seja um NPC pra todos os efeitos
	/// -- e isso responde de graca se o laco alcanca NPC (alcanca).
	/// </summary>
	private ServerPlayer CorpoDeNuvem(string rotulo, ZoneKey zona, Vec2 onde)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDaNuvemDeTeste + (++_nuvProximoId),
			Peer = null,
			Name = rotulo,
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = zona,
			Pos = onde,
			Conta = "bancada_nuvem",
			Slot = 0,
			Ficha = new Fighter { Race = "Human", BP = 100_000 },
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.ChunkAtual = ChunkId.De(novo.Pos);

		// `PorNoMundo` chama `Statify` e nao `PowerLevel` -- mesma linha das outras bancadas. Aqui nao
		// muda desfecho nenhum, e esta escrita pra que o proximo corpo forjado nasca igual aos outros.
		novo.Ficha.Tick(agoraMs: NowMs());

		// O `PorNoMundo` pode ter reposicionado o corpo (berço/pouso). A nuvem so mede o que esta EM
		// CIMA dela, entao a posicao e reafirmada -- e reafirmar e honesto porque a pergunta desta
		// bancada e "e daqui que se cai?", nao "da pra nascer aqui?" (nao da: `ServeDeChao` e falso).
		novo.Pos = onde;
		novo.ChunkAtual = ChunkId.De(novo.Pos);

		_corposDaNuvem.Add(novo);
		return novo;
	}

	/// <summary>Os corpos da bancada saem do mundo e das listas de zona -- inclusive da de DESTINO.</summary>
	private void LimparAOficinaDaNuvem()
	{
		foreach (ServerPlayer pl in _corposDaNuvem)
		{
			_players.Remove(pl.Id);
			ZoneList(pl.Zone.Hash).Remove(pl);

			// A CARENCIA TAMBEM SAI. Ela e indexada por id e um id de bancada deixado la ficaria pra
			// sempre (o laco so visita quem esta em `_players`) -- e, pior, a proxima rodada herdaria
			// a carencia do id reciclado e mediria "nao caiu" com o relogio de outra familia.
			_acabouDeAtravessar.Remove(pl.Id);
		}
		_corposDaNuvem.Clear();
	}

	/// <summary>
	/// UMA CELULA DE NUVEM COM FOLGA DE 3x3 em volta -- ver o cabecalho do <see cref="AQuedaDeUmaZona"/>.
	///
	/// A varredura e por passo largo de proposito: sao mapas de 500x500 e qualquer celula serve, entao
	/// vale mais achar rapido do que achar a primeira. Ela desiste devolvendo nulo, e ai a prova diz
	/// "sem cobertura" em vez de passar.
	/// </summary>
	private static (int X, int Y)? CelulaDeNuvemFolgada(ZoneCollision m)
	{
		for (int y = 1; y < m.Height - 1; y++)
			for (int x = 1; x < m.Width - 1; x++)
			{
				if (!m.EhNuvem(x, y)) continue;
				bool folgada = true;
				for (int dy = -1; dy <= 1 && folgada; dy++)
					for (int dx = -1; dx <= 1; dx++)
						if (!m.EhNuvem(x + dx, y + dy)) { folgada = false; break; }
				if (folgada) return (x, y);
			}
		return null;
	}

	/// <summary>
	/// UMA CELULA QUE O MAPA RECUSA -- parede, borda, agua ou nuvem. Achada e nao digitada; ver o
	/// cabecalho do <see cref="AChegadaEChaoLivre"/>.
	///
	/// Ela e procurada LONGE da borda (a borda tambem e recusada, mas mirar nela mediria o clamp da
	/// conversao de coordenada em vez do funil de pouso).
	/// </summary>
	private static (int X, int Y)? CelulaRuim(ZoneCollision m)
	{
		for (int y = 8; y < m.Height - 8; y++)
			for (int x = 8; x < m.Width - 8; x++)
				if (!m.ServeDeChao(x, y)) return (x, y);
		return null;
	}
}
