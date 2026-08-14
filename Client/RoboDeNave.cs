using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO INTERIOR DE NAVE (`--diagnave`). Sem rede, sem servidor, sem janela.
///
/// ============================ O QUE SO ELA RESPONDE ============================
/// A bancada `nave` do AssetPipeline prova que a PLANTA existe: que o casco fecha, que o console e
/// parede, que ha um vao pra ponte. A `--naveteste` prova que o SERVIDOR obedece essa planta. Nenhuma
/// das duas prova a unica coisa que o jogador enxerga: **que a sala e DESENHADA**.
///
/// E esse e o erro que este projeto ja cometeu e anotou na memoria: *"uniform escrito nao e pixel
/// desenhado"* -- quatro mil checagens verdes com quatro bugs visuais em pe. Aqui a armadilha e
/// concreta e tem nome: `TileMapLayer.SetCell` num tile que NAO EXISTE no `tileset.tres` **nao
/// desenha e nao reclama**. A planta pede `Space`/`bottom` e `Space`/`top`; se o pipeline nunca tiver
/// convertido o `Space.dmi`, a nave inteira sobe como um retangulo preto e todos os outros testes
/// continuam verdes.
///
/// As perguntas, e todas sao NUMERO DE CELULAS PINTADAS:
///   * o conves aparece? (uma celula do meio da sala tem tile)
///   * o casco aparece? (uma celula da borda tem tile)
///   * a sala inteira e coberta -- nao ha buraco entre chao e parede?
///   * e o cenario entra POR PEDACO (regra 0.6), e nao de uma vez?
/// ==============================================================================
///
/// ============================ ELA EXERCITA O CODIGO DE PRODUCAO ============================
/// A planta e a `NaveGrande.Planta()` de verdade; o node e o `PlanetaProcedural` de verdade, com o
/// `TerrenoPronto` que o `World.CarregarZona` usa; a pintura e o `PintorDePedacos` do jogo. O que a
/// bancada faz de proprio e so montar o viewport e mirar a camera -- que e o que a arvore faria.
/// ==========================================================================================
///
/// COMO RODAR:
///     Godot --path . --headless --diagnave
/// </summary>
public partial class RoboDeNave : Node
{
	private const int T = ZoneCollision.TileSize;

	private readonly List<string> _falhas = [];
	private int _ok;

	private void Conferir(bool passou, string oque, string detalhe = "")
	{
		if (passou) { _ok++; GD.Print($"[nave]   OK    {oque}"); return; }
		_falhas.Add(oque);
		GD.PrintErr($"[nave]   FALHA {oque}   {detalhe}");
	}

	public override void _Ready()
	{
		GD.Print("[nave] ================ O INTERIOR DA CAPITAL SHIP ================");

		TerrenoGerado planta = NaveGrande.Planta();
		GD.Print($"[nave] planta {planta.Largura}x{planta.Altura} | assinatura {planta.Assinatura():X16}");

		// ============================ 1) OS TILES QUE A PLANTA PEDE EXISTEM? ============================
		// A pergunta que precede todas as outras. Um `SetCell` num tile inexistente e o silencio que
		// esta bancada existe pra quebrar.
		var conjunto = ResourceLoader.Exists("res://Assets/Maps/tileset.tres")
			? ResourceLoader.Load<TileSet>("res://Assets/Maps/tileset.tres")
			: null;
		if (conjunto == null)
		{
			GD.PrintErr("[nave] sem 'res://Assets/Maps/tileset.tres' -- rode o Tools/AssetPipeline.");
			Sair(1);
			return;
		}

		// ============================ 2) O MUNDO DE VERDADE ============================
		var interior = new PlanetaProcedural
		{
			Name = "InteriorDeNave",
			TerrenoPronto = planta,
			NomeDoPlaneta = "Nave",
		};

		// A TELA E DA BANCADA e nao da maquina -- mesmo argumento (e mesmo tamanho) do `RoboDeVazio`:
		// quantos pedacos entram e funcao direta do viewport e do zoom, e o headless daria 64x64.
		var tela = new SubViewport
		{
			Size = new Vector2I(1152, 648),
			RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
		};
		AddChild(tela);

		var cam = new Camera2D
		{
			Enabled = true,
			PositionSmoothingEnabled = false,
			Zoom = new Vector2(Settings.ZoomMin, Settings.ZoomMin),
		};
		tela.AddChild(cam);
		cam.MakeCurrent();

		// ONDE O SERVIDOR LARGA O CORPO: um tile ao sul da plataforma (`NaveGrande.CelDaChegada`).
		Vec2 chegada = NaveGrande.PixelDe(NaveGrande.CelDaChegada);
		var entrada = new Vector2(chegada.X, chegada.Y);
		cam.Position = entrada;
		cam.ForceUpdateScroll();

		interior.CentroInicial = entrada;
		tela.AddChild(interior);

		ulong t0 = Time.GetTicksUsec();
		interior.Entrar(7);   // a seed e o ID DA NAVE: identidade, e nao geracao
		double ms = (Time.GetTicksUsec() - t0) / 1000.0;

		GD.Print($"[nave] entrada (a mesma chamada do `World`): {ms:0.0} ms, {interior.PedacosVivos} pedaco(s)");
		Conferir(ms < 33, "montar o interior cabe num quadro de 30 Hz", $"{ms:0.0} ms");

		Conferir(interior.Colisao != null && interior.Colisao.Width == NaveGrande.Lado,
				 "o node expoe a colisao da planta (e a mesma do servidor)");

		// ============================ 3) A ENTREGA E POR PEDACO (regra 0.6) ============================
		// Um `TileMapLayer` monta o desenho de TODAS as celulas que tiver no primeiro quadro. Uma sala
		// de 10.000 celulas entregue inteira e ~27 ms de travada invisivel em qualquer medicao que
		// comece e termine dentro da pintura -- exatamente o custo que este projeto perseguiu por dias.
		int cabem = (1152 / T / 64 + 2) * (648 / T / 64 + 2);
		Conferir(interior.PedacosVivos > 0 && interior.PedacosVivos <= cabem,
				 $"o cenario entra por PEDACO: {interior.PedacosVivos} vivo(s), e nao a sala inteira",
				 $"teto razoavel: {cabem}");

		// ============================ 4) O PIXEL, E NAO O UNIFORM ============================
		// Aqui esta a razao de esta bancada existir. `GetCellSourceId` devolve -1 quando a celula NAO
		// TEM tile -- que e exatamente o que acontece quando o atlas pedido nao existe no tileset.
		var camadas = new List<TileMapLayer>();
		foreach (Node f in interior.GetChildren())
			if (f is TileMapLayer c) camadas.Add(c);

		Conferir(camadas.Count > 0, "o interior montou pelo menos uma camada de tilemap",
				 $"{camadas.Count} camada(s)");

		bool TemTile(int cx, int cy)
		{
			foreach (TileMapLayer c in camadas)
				if (c.GetCellSourceId(new Vector2I(cx, cy)) >= 0) return true;
			return false;
		}

		Conferir(TemTile(NaveGrande.CelDaChegada.X, NaveGrande.CelDaChegada.Y),
				 "O CONVES E DESENHADO: a celula onde o corpo chega tem tile");
		Conferir(TemTile(NaveGrande.CelDaPlataforma.X, NaveGrande.CelDaPlataforma.Y),
				 "...e a celula da plataforma tambem (ela e chao, o sprite dela vem por outro canal)");

		// O CASCO. Ele so cai num pedaco perto da camera se a camera estiver perto dele -- entao a
		// bancada ANDA ate a borda em vez de afirmar que ela ja esta pintada. Andar e o que o jogador
		// faz, e o pintor segue a camera (`PintorDePedacos.CentroDaCamera`).
		var naBorda = new Vector2(2 * T + T / 2f, 2 * T + T / 2f);
		cam.Position = naBorda;
		cam.ForceUpdateScroll();
		for (int i = 0; i < 240 && !TemTile(1, 1); i++) interior._Process(1.0 / 60);

		Conferir(TemTile(1, 1), "O CASCO E DESENHADO: a celula da borda tem tile depois de a camera chegar la");
		Conferir(TemTile(NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y),
				 "...e a celula do console tambem (ela e casco: o sprite do computador vem por cima)");

		// NAO HA BURACO: num quadrado inteiro perto da camera, TODA celula tem tile. Um buraco seria
		// uma classe de terreno da planta sem pincel resolvido -- e ela nao reclama sozinha.
		int semTile = 0, olhadas = 0;
		for (int y = 0; y < 24; y++)
			for (int x = 0; x < 24; x++)
			{
				olhadas++;
				if (!TemTile(x, y)) semTile++;
			}
		Conferir(semTile == 0, $"nenhum buraco no quadrado 24x24 perto da camera ({olhadas} celulas)",
				 $"{semTile} sem tile");

		// ============================ 5) A SALA E A MESMA PRA TODA NAVE ============================
		// O cliente monta o interior a partir da SEED da zona, que aqui e o id da nave. Se a planta
		// dependesse dela, duas naves teriam interiores diferentes -- e o servidor, que usa uma planta
		// so, discordaria do cliente sobre onde ha parede.
		Conferir(ReferenceEquals(NaveGrande.Planta(), planta),
				 "a planta e a MESMA instancia em toda chamada -- as duas pontas nao tem como divergir");

		GD.Print($"[nave] ================ {_ok} passaram, {_falhas.Count} falharam ================");
		foreach (string f in _falhas) GD.PrintErr($"[nave]   -> {f}");
		Sair(_falhas.Count == 0 ? 0 : 1);
	}

	private void Sair(int codigo)
	{
		GetTree().Quit(codigo);
	}
}
