using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO VAZIO DA SALA DO TEMPO (`--diagvazio`). Sem rede, sem servidor, sem janela.
///
/// ============================ O QUE SO ELA RESPONDE ============================
/// O vazio procedural e um sistema que, quando FUNCIONA, e indistinguivel de nada: um campo
/// branco liso. E quando ele quebra, quebra igual -- um campo branco liso onde o chao parou de
/// vir, ou onde o corpo bateu numa parede que ninguem desenhou. Olhar nao responde.
///
/// As quatro perguntas da 13.7, e as quatro sao numero:
///   * o CHAO continua vindo a mil tiles do quarto desenhado?
///   * a COLISAO deixa passar, ou a beirada do bitset ainda e o fim do mundo?
///   * a VOLTA DO PLANETA nao dispara (ela e o oposto exato de se perder)?
///   * o numero de PEDACOS VIVOS para de crescer -- e o descarte chega a DISPARAR?
///
/// A ultima e a que a PARTE 0.7 obriga a existir: um teto que nunca e atingido e indistinguivel
/// de teto nenhum. Foi exatamente isso que aconteceu com a folga de descarte do pintor em 160.
/// ==============================================================================
///
/// ============================ ELA EXERCITA O CODIGO DE PRODUCAO ============================
/// Nada aqui e uma copia do jogo: a cena e a `z13_...scn` de verdade, o cenario entra pelo
/// `PlanetaPreFeito.Semear` (o mesmo que o `World` chama, com o mesmo argumento), o passo e o
/// `MoveRules.Advance` das duas pontas, a colisao e o `.col` do disco com o `SemBorda` ligado pelo
/// MESMO `SalaDoTempo` que o servidor consulta, e a sombra e a `Visao` do jogo.
///
/// O que a bancada faz de proprio e so ANDAR: ela move a camera e chama o `_Process` do planeta,
/// que e o que a arvore faria. Uma bancada que pintasse por um caminho proprio mediria o caminho
/// proprio.
/// ==========================================================================================
///
/// ============================ COMO CADA FAMILIA REPROVA (medido, injetando o defeito) ============================
///   defeito injetado                                          | reprovam
///   ----------------------------------------------------------|----------
///   `SalaDoTempo.SemBorda` devolvendo `false`                  | 15
///   `FonteComVazio.Faixa` devolvendo a faixa do ARQUIVO        | 3
///   o tile do vazio CRAVADO (`31,4,1` em vez da moda)          | 3  (as TRES sao novas)
///   `Espaco.EhPlaneta` aceitando o z13 (a volta ligada)        | 1
///   `FolgaDeDescarte = 4000` (o descarte que nunca dispara)    | 2  (uma delas e nova: o SALTO)
///
/// A do tile cravado e a que justifica esta rodada: sem ela, a Sala do Tempo podia estar cercada de
/// PEDRA -- desenhando, sem erro e sem log -- com as 28 checagens antigas todas verdes.
/// ==============================================================================================================
///
/// COMO RODAR:
///     Godot --path . --headless --diagvazio
/// </summary>
public partial class RoboDeVazio : Node
{
	private const string Zona = SalaDoTempo.Zona;
	private const string Cena = "res://Assets/Maps/z13_Hyperbolic_Time_Chamber.scn";
	private const string Pedacos = "res://Assets/Maps/z13_Hyperbolic_Time_Chamber.pedacos";
	private const string Colisao = "res://Assets/Maps/z13_Hyperbolic_Time_Chamber.col";
	private const string Visao = "res://Assets/Maps/z13_Hyperbolic_Time_Chamber.vis";

	private const int T = ZoneCollision.TileSize;

	/// <summary>O mapa desenhado a mao, em tiles. E o que o manifesto diz do z13.</summary>
	private const int Larg = 500, Alt = 500;

	/// <summary>
	/// Quantos tiles andar PRA FORA do quarto. Mil e o numero da 13.7 -- e ele importa: a 500 o
	/// caminhante ainda estaria dentro do bitset, e o teste nao teria saido do mapa.
	/// </summary>
	private const int TilesDeCaminhada = 1000;

	/// <summary>De quantos em quantos tiles a bancada para pra conferir e deixar o pintor trabalhar.</summary>
	private const int PassoDaConferencia = 8;

	private readonly List<string> _falhas = [];
	private int _ok;

	/// <summary>
	/// O CHAO QUE O ARQUIVO DESENHA -- lido na COSTURA (ver <see cref="MedirACostura"/>) e comparado
	/// com o que o vazio pinta la fora, tanto na caminhada quanto no quadrante negativo.
	///
	/// NAO E O TILE DA ENTRADA. A celula (145,340), onde a porta larga o corpo, e `31[4,1]` -- os
	/// aposentos tem piso proprio. O "chao da Sala" e o branco do campo (`102[0,0]`, 99,6% das
	/// celulas), e usar a entrada como referencia reprovaria o sistema certo.
	/// </summary>
	private int _fonteDoChao = -1;
	private Vector2I _atlasDoChao = new(-1, -1);

	private void Conferir(bool passou, string oque, string detalhe = "")
	{
		if (passou) { _ok++; GD.Print($"[vazio]   OK    {oque}"); return; }
		_falhas.Add(oque);
		GD.PrintErr($"[vazio]   FALHA {oque}   {detalhe}");
	}

	public override void _Ready()
	{
		GD.Print("[vazio] ================ O VAZIO DA SALA DO TEMPO ================");

		if (!Godot.FileAccess.FileExists(Cena) || !Godot.FileAccess.FileExists(Pedacos))
		{
			GD.PrintErr($"[vazio] sem '{Cena}' ou '{Pedacos}' -- rode o Tools/AssetPipeline.");
			Sair(1);
			return;
		}

		// ============================ 1) A REGRA, ANTES DO MUNDO ============================
		// As tres coisas que a 13.3 pediu que mudassem juntas comecam numa pergunta so, e ela tem
		// que responder o mesmo pras duas pontas. Se esta secao falhar, o resto e ruido.
		Conferir(SalaDoTempo.EhASala(Zona), "o `SalaDoTempo` reconhece a zona do z13");
		Conferir(SalaDoTempo.SemBorda(Zona), "...e diz que ela NAO TEM BEIRADA");
		Conferir(!SalaDoTempo.SemBorda("Earth"), "...e que a Terra tem (a excecao e de UM lugar)");

		// A VOLTA DO PLANETA, DESLIGADA AQUI. E o crivo do proprio servidor (`GameServer.Volta.cs`
		// abre com `if (!Espaco.EhPlaneta(...)) return false`), entao perguntar ao `Espaco` e
		// perguntar a mesma coisa que ele pergunta -- e nao uma imitacao da regra.
		Conferir(!Espaco.EhPlaneta(ZoneKey.Premade(Zona)),
				 "A VOLTA DO PLANETA NAO VALE NA SALA (o oposto de se perder)");
		Conferir(Espaco.EhPlaneta(ZoneKey.Premade("Earth")),
				 "...e continua valendo na Terra (o crivo nao virou 'nunca')");

		// ============================ 2) A COLISAO FORA DO BITSET ============================
		ZoneCollision? mapa = Godot.FileAccess.FileExists(Colisao)
			? ZoneCollision.Load(Godot.FileAccess.GetFileAsBytes(Colisao)) : null;
		if (mapa == null) { GD.PrintErr("[vazio] sem .col legivel"); Sair(1); return; }

		// SEM O BIT, PRIMEIRO: e o que prova que a mudanca e a mudanca, e nao o mapa ja ser assim.
		Conferir(mapa.BlockedCell(Larg + 40, 250), "SEM o bit, fora do bitset e PAREDE (como em todo mapa)");
		Conferir(mapa.NaBorda(1, 250), "SEM o bit, a beirada e beirada");

		mapa.SemBorda = SalaDoTempo.SemBorda(Zona);
		Conferir(mapa.SemBorda, "o `.col` da Sala sobe com `SemBorda` ligado");

		Conferir(!mapa.BlockedCell(Larg + 40, 250), "COM o bit, fora do bitset e CHAO LIVRE");
		Conferir(!mapa.BlockedCell(-40, -40), "...tambem do lado negativo (o vazio e nos quatro rumos)");
		Conferir(!mapa.NaBorda(1, 250), "...e nao ha mais beirada (nem chao indestrutivel de margem)");

		// O ANEL DE BORDA DO .dmm. Ele e a unica coluna densa fora do quarto, e sem esta clausula o
		// vazio teria uma parede invisivel de um tile so a leste.
		Conferir(!mapa.BlockedCell(Larg - 1, 250),
				 "o anel `/turf/Other/Blank` do mapa (a coluna 499) deixou de ser parede");

		// AS PAREDES DE VERDADE CONTINUAM DE PE. Uma zona "sem borda" que abrisse tudo teria passado
		// em todas as linhas acima e teria destruido o quarto desenhado a mao.
		int paredes = 0;
		for (int y = 337; y <= 362; y++)
			for (int x = 129; x <= 162; x++)
				if (mapa.BlockedCell(x, y)) paredes++;
		Conferir(paredes > 100, $"...E AS PAREDES DO QUARTO CONTINUAM DE PE ({paredes} celulas densas)",
				 $"{paredes}");

		// ============================ 3) O MUNDO DE VERDADE ============================
		var cena = ResourceLoader.Load<PackedScene>(Cena);
		if (cena?.Instantiate<Node2D>() is not PlanetaPreFeito planeta)
		{
			GD.PrintErr("[vazio] a cena do z13 nao e um PlanetaPreFeito");
			Sair(1);
			return;
		}
		// ============================ A TELA E DA BANCADA, E NAO DA MAQUINA ============================
		// O numero que esta bancada existe pra medir -- quantos pedacos ficam vivos -- e uma funcao
		// DIRETA do tamanho da tela e do zoom (`PintorDePedacos.PedacosPerto`). Rodando na janela do
		// processo, o headless da 64x64 px e o teto medido sairia ridiculo; noutra maquina, outro.
		//
		// Um `SubViewport` de tamanho fixo tranca as duas variaveis, e o ZOOM e o MINIMO que o jogo
		// permite (`Settings.ZoomMin`) de proposito: menos zoom = mais mundo na tela = mais pedacos.
		// O teto medido aqui e o PIOR caso que um jogador consegue pedir, e nao um caso tipico.
		// ==========================================================================================
		var tela = new SubViewport
		{
			Size = new Vector2I(1152, 648),
			RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
		};
		AddChild(tela);
		tela.AddChild(planeta);

		// A CAMERA E O QUE O PINTOR SEGUE (`PintorDePedacos.PedacosPerto`). Sem uma, ele cairia na
		// posicao do proprio node e a caminhada nao moveria nada.
		var cam = new Camera2D
		{
			Enabled = true,
			PositionSmoothingEnabled = false,
			Zoom = new Vector2(Settings.ZoomMin, Settings.ZoomMin),
		};
		tela.AddChild(cam);
		cam.MakeCurrent();

		// A BANCADA DIZ QUE CAMERA ELA ESTA DIRIGINDO. O pintor segue a camera do viewport
		// (`PintorDePedacos.CentroDaCamera`), e se ela nao for esta o teste andaria com o corpo e
		// com o cenario parado na origem -- passando ou falhando por um motivo que nao e o sistema.
		Conferir(planeta.GetViewport()?.GetCamera2D() == cam,
				 "o pintor esta seguindo a camera desta bancada",
				 $"{planeta.GetViewport()?.GetCamera2D()?.Name}");
		GD.Print($"[vazio] viewport {planeta.GetViewportRect().Size} | zoom {cam.Zoom.X}");

		// ONDE O DM DEIXA O CORPO: `HTC_ENTRY_LOC = locate(146,160,13)` -> celula (145,340).
		var entrada = new Vector2(145 * T + T / 2f, 340 * T + T / 2f);
		Mirar(cam, entrada);

		ulong t0 = Time.GetTicksUsec();
		planeta.Semear(Pedacos, entrada, new Vector2I(Larg, Alt));
		GD.Print($"[vazio] semeadura (a mesma chamada do `World`): "
				 + $"{(Time.GetTicksUsec() - t0) / 1000.0:0.0} ms, {planeta.PedacosVivos} pedaco(s)");

		TileMapLayer? chao = planeta.GetNodeOrNull<TileMapLayer>("Chao");
		Conferir(chao != null, "a cena tem a camada `Chao`");
		if (chao == null) { Sair(1); return; }

		Conferir(chao.GetCellSourceId(new Vector2I(145, 340)) >= 0,
				 "o quarto DESENHADO continua sendo desenhado (a celula da entrada tem tile)");

		// ============================ 3b) A COSTURA ============================
		// Onde o arquivo acaba e o vazio comeca, no MESMO pedaco. Ela mede o chao de referencia que
		// as duas checagens seguintes usam, entao vem antes das duas.
		MedirACostura(planeta, chao, cam);

		// E A CAMERA VOLTA PRA ENTRADA: a caminhada comeca de la, e o pedaco do quarto foi
		// descartado enquanto a camera estava na costura.
		Mirar(cam, entrada);
		for (int q = 0; q < 400; q++) planeta._Process(1.0 / 60.0);

		// ============================ 4) A CAMINHADA ============================
		Caminhar(planeta, chao, cam, mapa, entrada);

		// ============================ 4b) O OUTRO CANTO DO INFINITO ============================
		OVazioNoQuadranteNegativo(planeta, chao, cam, mapa);

		// ============================ 5) O VEU NUM CAMPO BRANCO INFINITO ============================
		OVeuNoVazio(mapa, entrada);

		GD.Print($"[vazio] ================ {_ok} passaram, {_falhas.Count} falharam ================");
		foreach (string f in _falhas) GD.PrintErr($"[vazio]   -> {f}");
		Sair(_falhas.Count == 0 ? 0 : 1);
	}

	/// <summary>
	/// MIL TILES PRA FORA, andando de verdade.
	///
	/// O passo e o `MoveRules.Advance` -- o mesmo do cliente e do servidor --, e nao um `pos.X +=`.
	/// A pergunta "a colisao barra?" so vale se quem responder for quem barra no jogo.
	///
	/// ============================ PRIMEIRO E PRECISO SAIR DO QUARTO ============================
	/// O quarto desenhado a mao e uma CAIXA FECHADA de 34x26 (x 129..162, y 337..362) com duas
	/// aberturas de quatro tiles, as duas na coluna x 144..147: a de cima e a passagem `fromhbtc`
	/// que volta pro Templo, e a de baixo e a que da no campo branco. E o desenho do anime -- os
	/// aposentos abrem pra a planicie -- e a primeira versao desta bancada saiu andando pro nordeste
	/// da entrada e bateu na parede norte 252 vezes achando que a colisao estava errada.
	///
	/// Entao a caminhada tem duas pernas: SUL pela abertura de baixo, e so entao a diagonal.
	/// =========================================================================================
	///
	/// A DIAGONAL E SUDESTE, e nao um eixo so: a beirada leste do z13 e a unica com o anel denso do
	/// `.dmm` (a coluna 499), e a quina e o unico lugar onde um pedaco tem as DUAS metades da fonte
	/// composta -- o arquivo e o vazio no mesmo bloco.
	/// </summary>
	private void Caminhar(PlanetaPreFeito planeta, TileMapLayer chao, Camera2D cam,
						  ZoneCollision mapa, Vector2 de)
	{
		var pos = new Vec2(de.X, de.Y);

		int fonteDoVazio = -1;
		var atlasDoVazio = new Vector2I(-1, -1);

		int barrados = 0, semChao = 0, foraDoMapa = 0;
		int pico = planeta.PedacosVivos, picoDepoisDeSair = 0;
		int amostras = 0;
		ulong custo = 0;

		// PERNA 1: sul, pela abertura de baixo, ate limpar o quarto.
		bool saiu = false;
		for (int n = 0; n < 40 && !saiu; n++)
		{
			Andar(new Vec2(0, 1), 1);
			saiu = Pe(pos).Y > 364 * T;
		}
		Conferir(saiu, $"o caminhante SAIU DO QUARTO pela abertura de baixo (y={Pe(pos).Y / T:0})",
				 $"{Pe(pos).X / T:0},{Pe(pos).Y / T:0}");
		barrados = 0;   // as paredes do quarto sao paredes de verdade; o que se mede e o vazio

		// PERNA 2: mil tiles na diagonal sudeste, conferindo a cada trecho.
		var rumo = new Vec2(1, 1);
		int passos = TilesDeCaminhada / PassoDaConferencia;
		for (int n = 0; n < passos; n++)
		{
			Andar(rumo, PassoDaConferencia);

			Mirar(cam, new Vector2(pos.X, pos.Y));

			// O QUE A ARVORE FARIA: alguns quadros de pintura. O orcamento do pintor e 4 ms por
			// quadro de proposito (ver `PintorDePedacos.OrcamentoUs`), entao um pedaco novo leva
			// alguns -- e a folga de carga de 24 tiles existe exatamente pra dar esse tempo.
			ulong tq = Time.GetTicksUsec();
			for (int q = 0; q < 6; q++) planeta._Process(1.0 / 60.0);
			custo += Time.GetTicksUsec() - tq;
			amostras++;

			Vec2 pe = Pe(pos);
			int cx = (int)MathF.Floor(pe.X / T), cy = (int)MathF.Floor(pe.Y / T);
			if (chao.GetCellSourceId(new Vector2I(cx, cy)) < 0) semChao++;
			if (mapa.BlockedCell(cx, cy)) barrados++;

			bool fora = cx < 0 || cy < 0 || cx >= Larg || cy >= Alt;
			if (fora) foraDoMapa++;
			if (fora && fonteDoVazio < 0)
			{
				fonteDoVazio = chao.GetCellSourceId(new Vector2I(cx, cy));
				atlasDoVazio = chao.GetCellAtlasCoords(new Vector2I(cx, cy));
			}

			pico = Math.Max(pico, planeta.PedacosVivos);
			// O PICO DEPOIS DE SAIR e o que responde "o teto e do vazio ou do mapa?": dentro do
			// bitset o pintor e recortado pela faixa do arquivo, e o numero nao significa nada.
			if (fora) picoDepoisDeSair = Math.Max(picoDepoisDeSair, planeta.PedacosVivos);
		}

		Vec2 fim = Pe(pos);
		int cxf = (int)MathF.Floor(fim.X / T), cyf = (int)MathF.Floor(fim.Y / T);
		GD.Print($"[vazio] parou em ({cxf},{cyf}) -- {foraDoMapa} de {amostras} amostras fora do mapa "
				 + $"de {Larg}x{Alt}; pintura {custo / 1000.0 / amostras:0.00} ms por amostra");

		Conferir(cxf >= Larg && cyf >= Alt,
				 $"A CAMINHADA SAIU DO MAPA DESENHADO pelos DOIS eixos (parou em {cxf},{cyf})",
				 $"{cxf},{cyf}");
		Conferir(barrados == 0, "A COLISAO NAO BARROU NENHUMA VEZ em mil tiles de vazio",
				 $"{barrados} barradas");
		Conferir(semChao == 0, "O CHAO CONTINUOU VINDO em todas as amostras",
				 $"{semChao} amostras sem tile embaixo do pe");

		// E E O MESMO CHAO A MIL TILES DE DISTANCIA -- a costura media logo acima prova a continuidade
		// no encosto; esta prova que ela nao se perde no caminho.
		Conferir(fonteDoVazio >= 0 && fonteDoVazio == _fonteDoChao && atlasDoVazio == _atlasDoChao,
				 $"...E E O MESMO TILE DO MAPA ({_fonteDoChao}[{_atlasDoChao.X},{_atlasDoChao.Y}]) "
				 + "la fora tambem -- e nao 'um tile qualquer'",
				 $"mapa {_fonteDoChao}[{_atlasDoChao.X},{_atlasDoChao.Y}] x "
				 + $"vazio {fonteDoVazio}[{atlasDoVazio.X},{atlasDoVazio.Y}]");

		// Um trecho de N tiles, em passos de velocidade de jogo.
		void Andar(Vec2 rumoLocal, int tiles)
		{
			float andar = tiles * T;
			while (andar > 0)
			{
				const float dt = 0.05f;
				Vec2 antes = pos;
				pos = MoveRules.Advance(pos, rumoLocal, dt, 1f, mapa, out bool barrado);
				if (barrado) barrados++;
				float deu = (pos - antes).Length;
				if (deu < 0.5f) { barrados++; return; }   // travou de frente
				andar -= deu;
			}
		}

		// ============================ O TETO, E ELE DISPARA ============================
		Conferir(picoDepoisDeSair > 0 && picoDepoisDeSair <= 25,
				 $"O NUMERO DE PEDACOS VIVOS NAO CRESCE: teto de {picoDepoisDeSair} no vazio "
				 + $"(pico geral {pico})", $"{picoDepoisDeSair}");

		// Mil tiles sao ~16 pedacos de lado. Se o descarte nao rodasse, o pintor teria dezenas de
		// pedacos vivos e nao um punhado -- e foi exatamente esse o defeito da folga em 160.
		Conferir(planeta.PedacosVivos <= picoDepoisDeSair,
				 $"...e no fim da caminhada restam {planeta.PedacosVivos} (o descarte DISPAROU)");
	}

	/// <summary>
	/// A COSTURA: o ultimo chao do arquivo e o primeiro chao do vazio, NO MESMO PEDACO.
	///
	/// ============================ O DEFEITO QUE SO ESTA MEDIDA VE ============================
	/// "Veio tile" e "veio O tile" sao afirmacoes diferentes, e a distancia entre as duas e o defeito
	/// que a `FonteComVazio` foi escrita pra evitar: o `102` e um INDICE no `tileset.tres`, gerado
	/// pelo pipeline na ordem em que os atlas apareceram, e a proxima conversao renumera. Com um
	/// numero cravado, o sintoma seria a Sala do Tempo cercada de PEDRA -- desenhando, sem erro, sem
	/// log, e passando em todas as outras checagens desta bancada.
	///
	/// A fonte descobre a MODA do chao lendo o proprio `.pedacos` justamente por isso, e esta e a
	/// linha que cobra a descoberta. A comparacao e de FONTE **e** de coordenada no atlas: so a
	/// fonte deixaria passar um vizinho dentro do mesmo tileset, que e o erro mais provavel de todos.
	/// =====================================================================================
	///
	/// ============================ POR QUE NO MESMO PEDACO, E POR QUE NAO NA ENTRADA ============================
	/// As duas celulas (490 e 502 da linha 250) caem no pedaco (7,3) -- o unico tipo de pedaco que
	/// tem as DUAS metades da fonte composta. Uma so pintura responde as duas perguntas, sem depender
	/// de o descarte ter poupado um pedaco de mil tiles atras.
	///
	/// E a referencia NAO e a celula da entrada: (145,340) e `31[4,1]`, porque os aposentos tem piso
	/// proprio. O "chao da Sala" e o branco do campo, e usar a entrada aqui reprovaria o sistema
	/// certo -- foi exatamente o que a primeira versao desta checagem fez.
	/// ========================================================================================================
	/// </summary>
	private void MedirACostura(PlanetaPreFeito planeta, TileMapLayer chao, Camera2D cam)
	{
		var dentro = new Vector2I(Larg - 10, 250);   // ultimo chao de ARQUIVO
		var fora = new Vector2I(Larg + 2, 250);      // primeiro chao de VAZIO

		Mirar(cam, new Vector2(Larg * T, 250 * T));
		for (int q = 0; q < 400; q++) planeta._Process(1.0 / 60.0);

		_fonteDoChao = chao.GetCellSourceId(dentro);
		_atlasDoChao = chao.GetCellAtlasCoords(dentro);
		int fonteFora = chao.GetCellSourceId(fora);
		Vector2I atlasFora = chao.GetCellAtlasCoords(fora);

		GD.Print($"[vazio] costura na linha 250: arquivo {_fonteDoChao}[{_atlasDoChao.X},"
				 + $"{_atlasDoChao.Y}] | vazio {fonteFora}[{atlasFora.X},{atlasFora.Y}]");

		Conferir(_fonteDoChao >= 0, $"o arquivo desenha ate a beirada (celula {dentro.X},{dentro.Y})");
		Conferir(fonteFora >= 0, $"e o vazio comeca no MESMO pedaco (celula {fora.X},{fora.Y})");
		Conferir(fonteFora == _fonteDoChao && atlasFora == _atlasDoChao,
				 "NAO HA COSTURA: o tile do vazio e o MESMO chao que o arquivo desenha "
				 + "(a moda achada no `.pedacos`, e nao um indice cravado)",
				 $"arquivo {_fonteDoChao}[{_atlasDoChao.X},{_atlasDoChao.Y}] x "
				 + $"vazio {fonteFora}[{atlasFora.X},{atlasFora.Y}]");
	}

	/// <summary>
	/// O VAZIO NO QUADRANTE NEGATIVO -- o rumo que a caminhada NAO anda.
	///
	/// ============================ POR QUE UM SEGUNDO RUMO ============================
	/// A caminhada vai pro SUDESTE de propósito (a quina leste e o unico pedaco com as duas metades
	/// da fonte composta), e por isso ela so exercita coordenadas de pedaco POSITIVAS. O noroeste e
	/// aritmetica diferente, nao a mesma vista de outro angulo: `ForaDaFaixa` separa "antes do mapa"
	/// de "depois do mapa" em dois `Clamp` distintos, `Vazia` tem um ramo so pras colunas ANTES do
	/// mapa, e `PedacosDoMapa` indexa a chave do pedaco em `short` -- um `/` em vez de um
	/// `Math.Floor` na conta de pedaco arredonda pro ZERO no negativo e nao pra baixo, e o sintoma
	/// seria o chao sumindo so pra quem andasse pra tras.
	///
	/// A colisao ja respondia pelos dois lados (`BlockedCell(-40,-40)` la em cima); quem nunca foi
	/// perguntado foi o PINTOR. Aqui a camera pula pro canto oposto do mundo e a bancada cobra as
	/// tres mesmas coisas: chao, o MESMO chao, e o teto de pedacos vivos de pe depois do salto.
	/// ==============================================================================
	///
	/// O SALTO E DE PROPOSITO, e nao uma caminhada mais curta: ele e o pior caso do descarte (todo
	/// o conjunto vivo fica longe de uma vez), e e o que um teleporte de admin faz em jogo.
	/// </summary>
	private void OVazioNoQuadranteNegativo(PlanetaPreFeito planeta, TileMapLayer chao, Camera2D cam,
										   ZoneCollision mapa)
	{
		// MIL TILES PRA TRAS DA ORIGEM, nos dois eixos -- o espelho da caminhada.
		var celula = new Vector2I(-TilesDeCaminhada, -TilesDeCaminhada);
		Mirar(cam, new Vector2(celula.X * T + T / 2f, celula.Y * T + T / 2f));

		// QUADROS ATE O PINTOR ALCANCAR: o orcamento e 4 ms por quadro (`PintorDePedacos.OrcamentoUs`)
		// e o salto invalida o conjunto inteiro, entao aqui ele tem mais fila que na caminhada, onde
		// entra um pedaco de cada vez. O numero e folgado de proposito: o que se mede aqui e o
		// DESENHO, e nao quantos quadros ele levou.
		for (int q = 0; q < 400; q++) planeta._Process(1.0 / 60.0);

		int fonte = chao.GetCellSourceId(celula);
		Vector2I atlas = chao.GetCellAtlasCoords(celula);

		Conferir(fonte >= 0, $"O CHAO TAMBEM VEM NO RUMO CONTRARIO (celula {celula.X},{celula.Y})",
				 $"fonte {fonte}");
		Conferir(fonte == _fonteDoChao && atlas == _atlasDoChao,
				 "...e la e o MESMO tile do mapa (o ramo 'antes do mapa' do `Vazia`)",
				 $"mapa {_fonteDoChao}[{_atlasDoChao.X},{_atlasDoChao.Y}] x "
				 + $"vazio {fonte}[{atlas.X},{atlas.Y}]");
		Conferir(!mapa.BlockedCell(celula.X, celula.Y),
				 "...e a colisao continua livre no canto negativo");
		Conferir(planeta.PedacosVivos <= 25,
				 $"...e o SALTO nao acumulou pedacos ({planeta.PedacosVivos} vivos depois de pular "
				 + "de um canto do mundo pro outro)", $"{planeta.PedacosVivos}");
	}

	/// <summary>
	/// O VEU NUM CAMPO BRANCO INFINITO -- a pergunta 5 da 13.3.
	///
	/// ============================ NAO FICA ESTRANHO; FICARIA CARO ============================
	/// A resposta MEDIDA, e ela nao e a que se espera. O `Visao.Cega` e o `BlockedCell` do mapa de
	/// visao, e a regra antiga dizia que fora do bitset tudo bloqueia -- entao a intuicao e que o
	/// vazio apagaria a tela. Nao apaga: o `Marchar` atravessa qualquer sequencia de celulas cegas e
	/// so para ao pisar numa LIVRE depois do bloco, e fora do mapa nunca ha uma livre. O raio entra
	/// na parede fantasma e vai ate a borda da tela por dentro dela. Nenhuma sombra, nenhum leque
	/// cheio (44 raios nos dois casos: os 4 cantos da tela e os 40 soltos, sem quina nenhuma).
	///
	/// O preco esta escondido em dois lugares, e sao esses os dois que a secao mede:
	///   * cada raio gasta o TETO de passos do DDA (256) em vez dos ~40 que cabem na tela;
	///   * e `RepintarParedes` varre a tela perguntando `Cega` por celula -- com o vazio inteiro
	///     cego, as 984 celulas da tela viram candidatas a repinte, cada uma com uma chamada de
	///     `Ve`, contra ZERO com o bit ligado.
	///
	/// Ou seja: sem o bit o veu ficaria pagando por uma parede que ninguem ve, pra sempre, no lugar
	/// onde o jogador vai passar 48 minutos. Com ele, o vazio simplesmente nao tem o que sombrear e
	/// custa menos que o quarto desenhado.
	/// ======================================================================================
	/// </summary>
	private void OVeuNoVazio(ZoneCollision colisao, Vector2 entrada)
	{
		ZoneCollision? vis = Godot.FileAccess.FileExists(Visao)
			? ZoneCollision.Load(Godot.FileAccess.GetFileAsBytes(Visao)) : null;
		if (vis == null) { Conferir(false, "o `.vis` da Sala abre"); return; }
		vis.SemBorda = SalaDoTempo.SemBorda(Zona);

		var veu = new Visao { Mapa = vis, Colisao = colisao };
		AddChild(veu);

		var tela = new Rect2(new Vector2(-640, -360), new Vector2(1280, 720));

		(int raios, double ms) Medir(Vector2 olho)
		{
			ulong t = Time.GetTicksUsec();
			for (int i = 0; i < 10; i++) veu.Recalcular(olho, new Rect2(olho + tela.Position, tela.Size));
			return (veu.QuantosRaios, (Time.GetTicksUsec() - t) / 10000.0);
		}

		(int raiosQuarto, double msQuarto) = Medir(entrada);
		var longe = new Vector2((Larg + 700) * T, -(300 * T));
		(int raiosVazio, double msVazio) = Medir(longe);

		GD.Print($"[vazio] veu: quarto {raiosQuarto} raios em {msQuarto:0.00} ms | "
				 + $"vazio {raiosVazio} raios em {msVazio:0.00} ms");

		Conferir(!colisao.BlockedAt(new Vec2(longe.X, longe.Y)),
				 "no vazio o guarda do `_Draw` nao dispara (o olho nao esta 'dentro de rocha')");
		Conferir(raiosVazio < raiosQuarto,
				 $"o leque no vazio e MENOR que no quarto ({raiosVazio} contra {raiosQuarto} raios)",
				 $"{raiosVazio} vs {raiosQuarto}");
		Conferir(msVazio <= Math.Max(msQuarto, 1.0),
				 $"...e nao custa mais caro ({msVazio:0.00} ms contra {msQuarto:0.00} ms)",
				 $"{msVazio:0.00} vs {msQuarto:0.00}");
		// TODA A TELA VISIVEL, e nao dois pontos escolhidos a dedo: uma grade dentro do retangulo,
		// que e ate onde o leque vai (`ateABorda` e o "infinito util" deste sistema).
		int escuros = 0, olhados = 0;
		for (float dx = -600; dx <= 600; dx += 60)
			for (float dy = -320; dy <= 320; dy += 40)
			{
				olhados++;
				if (!veu.Ve(longe + new Vector2(dx, dy))) escuros++;
			}
		Conferir(escuros == 0,
				 $"...e o campo branco fica TODO visivel ({olhados} pontos, nenhum na sombra)",
				 $"{escuros} escuros");

		// ============================ E A PROVA DE QUE O BIT E QUEM PAGA A CONTA ============================
		// Aqui esta a parte contraintuitiva, e ela merece ficar escrita: o veu SEM o bit tambem nao
		// escurece o vazio. O `Marchar` atravessa qualquer sequencia de celulas cegas e so para ao
		// pisar numa livre depois do bloco -- e fora do bitset nunca ha uma livre. Ou seja, a parede
		// fantasma infinita nao projeta sombra nenhuma: ela e um muro em que o raio ENTRA e do qual
		// nao sai, ate o teto de 256 passos do DDA.
		//
		// Ela nao fica ESTRANHA. Ela fica CARA, e por dois caminhos:
		//   * cada raio do leque passa a gastar o teto de passos do DDA em vez dos ~40 da tela;
		//   * e `RepintarParedes` varre a tela perguntando `Cega` -- e com todo o vazio cego, TODA
		//     celula da tela vira candidata a ser repintada por cima da sombra, uma chamada de `Ve`
		//     por celula, contra zero.
		// As duas medidas abaixo sao esses dois caminhos, e sao o unico lugar da bancada em que
		// "sem o bit" e "com o bit" respondem coisas diferentes no vazio.
		// ================================================================================================
		int CegasNaTela(Vector2 olho)
		{
			int n = 0;
			for (int cy = (int)((olho.Y - 360) / T); cy <= (int)((olho.Y + 360) / T); cy++)
				for (int cx = (int)((olho.X - 640) / T); cx <= (int)((olho.X + 640) / T); cx++)
					if (vis.BlockedCell(cx, cy)) n++;
			return n;
		}

		vis.SemBorda = false;
		(int _, double msSemBit) = Medir(longe);
		int cegasSemBit = CegasNaTela(longe);
		vis.SemBorda = true;
		int cegasComBit = CegasNaTela(longe);

		GD.Print($"[vazio] veu no vazio: sem o bit {msSemBit:0.00} ms e {cegasSemBit} celulas cegas "
				 + $"na tela | com o bit {msVazio:0.00} ms e {cegasComBit} cegas");

		Conferir(cegasComBit == 0 && cegasSemBit > 500,
				 $"SEM o bit a tela inteira e parede ({cegasSemBit} celulas cegas); com ele, nenhuma",
				 $"{cegasSemBit} -> {cegasComBit}");
		Conferir(msVazio < msSemBit,
				 $"...e o leque fica MAIS BARATO com o bit ({msVazio:0.00} contra {msSemBit:0.00} ms)",
				 $"{msVazio:0.00} vs {msSemBit:0.00}");

		veu.QueueFree();
	}

	/// <summary>
	/// O PONTO QUE OCUPA CHAO -- os pes, e nao o centro do sprite.
	///
	/// E o mesmo <see cref="MoveRules.FeetOffsetY"/> que a colisao usa. Perguntar "que celula eu
	/// piso?" pelo centro erraria por 8 px, que e um quarto de tile -- perto o bastante pra o teste
	/// ficar verde no lugar errado, e e exatamente o tipo de defeito que so a bancada esconde.
	/// </summary>
	private static Vec2 Pe(Vec2 centro) => new(centro.X, centro.Y + MoveRules.FeetOffsetY);

	/// <summary>
	/// POE A CAMERA NUM PONTO, E FAZ O VIEWPORT ACREDITAR.
	///
	/// ============================ O `ForceUpdateScroll` NAO E ENFEITE ============================
	/// A `Camera2D` so empurra a posicao dela pro viewport no processamento do quadro, e esta
	/// bancada roda inteira DENTRO do `_Ready` -- nenhum quadro passa. Sem esta chamada,
	/// `GetScreenCenterPosition()` continua devolvendo a origem, o pintor pinta em volta do (0,0) e
	/// a caminhada anda com o cenario parado.
	///
	/// O sintoma era exatamente esse na primeira rodada: "soltei 4, ficam 0" no primeiro passo, e
	/// 125 amostras sem chao. Nada disso era do sistema medido.
	/// ==========================================================================================
	/// </summary>
	private static void Mirar(Camera2D cam, Vector2 onde)
	{
		cam.GlobalPosition = onde;
		cam.ForceUpdateScroll();
	}

	private static void Sair(int codigo)
	{
		if (Engine.GetMainLoop() is SceneTree t) t.Quit(codigo);
	}
}
