using System.Diagnostics;
using Jandirus.Core.Magic;
using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DO MAPA DA ABA WORLD E DO DISCO DO RADAR -- as regras puras (`MapaDoLugar`, `CoresDeTiles`,
/// `RadarDeEsferas`), sem abrir o jogo.
///
///   1. a cor de um tile e a media dos pixels opacos dele, e a cobertura e quanto do quadro eles ocupam;
///   2. o arquivo de cores vai e volta, e folha com outra grade fica SEM cor (nao com a cor errada);
///   3. o mapa desenhado a mao: o chao da a cor, o que esta em cima mistura, o vazio fica vazio, e tile sem
///      cor cai na reserva pelo tipo de chao (agua / parede / chao);
///   4. o planeta gerado: classe de terreno -> tile -> cor, com a tinta do bioma;
///   5. lugar maior que a imagem vira blocos (1000x1000 -> 500x500);
///   6. o marcador e as coordenadas: o centro, os quatro cantos, e alem da borda (preso, e dizendo que esta fora);
///   7. o disco do radar: os quatro quadrantes, e o sinal preso na borda alem do alcance;
///   8. COM OS ARQUIVOS DO JOGO: toda zona do manifesto vira mapa sem nenhuma celula na reserva, e quanto
///      custa montar cada um -- inclusive um planeta gerado de 1000x1000.
///
/// Cada regra tem o defeito dela ligado no meio (`...DeTeste`) e tem de reprovar com ele.
///
///     dotnet run --project Tools/AssetPipeline -- mapa [raizDoProjeto] [tiles-cores.json]
/// </summary>
public static class MapaBench
{
	private static int _ok, _falhou;

	private static void Checa(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; Console.WriteLine($"  OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; Console.WriteLine($"  FALHA {nome}   [{detalhe}]"); }
	}

	/// <summary>A mesma checagem com o defeito ligado: tem de REPROVAR, e voltar a passar com ele desligado.</summary>
	private static void ComDefeito(string defeito, Action<bool> liga, Func<bool> checagem)
	{
		bool reprovou;
		liga(true);
		try { reprovou = !checagem(); }
		finally { liga(false); }
		Checa($"(defeito injetado: {defeito}) a checagem REPROVA, e volta a passar sem ele", reprovou && checagem());
	}

	public static int Run(string? raiz, string? caminhoDasCores)
	{
		_ok = _falhou = 0;
		try
		{
			Cores();
			PreFeito();
			Gerado();
			Blocos();
			Marcador();
			Radar();
			if (raiz != null) ComOsArquivosDoJogo(raiz, caminhoDasCores);
			else Console.WriteLine("(sem a raiz do projeto: a secao dos arquivos do jogo nao rodou)");
		}
		finally
		{
			Console.WriteLine($"[mapa] ================ {_ok} OK, {_falhou} FALHA(S) ================");
		}
		return _falhou;
	}

	// =====================================================================
	// 1 e 2. A COR DE UM TILE, E O ARQUIVO
	// =====================================================================
	/// <summary>Uma folha de 2x2 quadros de 2x2 px: vermelho cheio, azul meio coberto, vazio, e verde meio transparente.</summary>
	private static byte[] FolhaDeProva()
	{
		var px = new byte[4 * 4 * 4];
		void P(int x, int y, byte r, byte g, byte b, byte a) { int i = (y * 4 + x) * 4; px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = a; }
		for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++) P(x, y, 200, 10, 10, 255);      // (0,0) vermelho
		P(2, 0, 10, 10, 200, 255); P(3, 1, 10, 10, 200, 255);                                   // (1,0) azul em 2 de 4
		for (int y = 2; y < 4; y++) for (int x = 2; x < 4; x++) P(x, y, 10, 200, 10, 128);      // (1,1) verde a meio alfa
		return px;
	}

	private static void Cores()
	{
		Console.WriteLine("--- 1. a cor de um tile ---");
		byte[] folha = FolhaDeProva();
		CorDeTile vermelho = CoresDeTiles.Media(folha, 4, 0, 0, 2, 2);
		CorDeTile azul = CoresDeTiles.Media(folha, 4, 2, 0, 2, 2);
		CorDeTile vazio = CoresDeTiles.Media(folha, 4, 0, 2, 2, 2);
		CorDeTile verde = CoresDeTiles.Media(folha, 4, 2, 2, 2, 2);
		Checa("quadro cheio: a cor dos pixels, cobertura 255", vermelho == new CorDeTile(200, 10, 10, 255), vermelho.ToString());
		Checa("quadro meio coberto: a cor so dos pixels OPACOS (o transparente nao escurece), cobertura ~metade",
			  azul is { R: 10, G: 10, B: 200 } && azul.Cobertura is >= 126 and <= 129, azul.ToString());
		Checa("quadro vazio: cobertura 0", vazio.Cobertura == 0, vazio.ToString());
		Checa("pixel meio transparente pesa meio na cobertura", verde is { G: 200 } && verde.Cobertura is >= 126 and <= 129, verde.ToString());

		Console.WriteLine("--- 2. o arquivo de cores ---");
		var f = new FolhaDeCores { Atlas = "Prova \"1\"", Colunas = 2, Linhas = 2, Cores = [vermelho, azul, vazio, verde] };
		CoresDeTiles lido = CoresDeTiles.Parse(CoresDeTiles.ParaJson([f]));
		Checa("vai e volta: as quatro cores e o nome (com aspas) saem iguais do arquivo",
			  lido.Total == 1 && lido.De("Prova \"1\"", 1, 0) == azul && lido.De("prova \"1\"", 1, 1) == verde && lido.De("Prova \"1\"", 2, 0) == null,
			  $"{lido.Total} folha(s)");
		Checa("arquivo ilegivel devolve catalogo VAZIO (o mapa cai na reserva, nao estoura)", CoresDeTiles.Parse("{ torto").Total == 0);

		CatalogoDeTiles tiles = CatalogoDeTiles.Parse(
			"[ { \"atlas\": \"Prova \\\"1\\\"\", \"fonte\": 3, \"colunas\": 2, \"iw\": 2, \"ih\": 2, \"res\": \"\", \"estados\": [\"a=0,0\"] },"
			+ "  { \"atlas\": \"Outra\", \"fonte\": 5, \"colunas\": 4, \"iw\": 2, \"ih\": 2, \"res\": \"\", \"estados\": [\"a=0,0\"] } ]");
		var outraGrade = new FolhaDeCores { Atlas = "Outra", Colunas = 2, Linhas = 1, Cores = [vermelho, azul] };
		CoresPorFonte porFonte = CoresPorFonte.Montar(tiles, CoresDeTiles.Parse(CoresDeTiles.ParaJson([f, outraGrade])));
		Checa("por id de fonte: a fonte 3 acha a cor do quadro (1,0)", porFonte.Achar(3, 1, 0, out CorDeTile c3) && c3 == azul);
		Checa("folha medida com OUTRA grade fica SEM cor (cor errada seria pior que reserva)",
			  !porFonte.Achar(5, 0, 0, out _) && porFonte.SemCor.Contains("Outra"), string.Join(",", porFonte.SemCor));
	}

	// =====================================================================
	// 3. O MAPA DESENHADO A MAO
	// =====================================================================
	private static void PreFeito()
	{
		Console.WriteLine("--- 3. o mapa desenhado a mao ---");
		CatalogoDeTiles tiles = CatalogoDeTiles.Parse(
			"[ { \"atlas\": \"Chaos\", \"fonte\": 1, \"colunas\": 2, \"iw\": 32, \"ih\": 32, \"res\": \"\", \"estados\": [\"grama=0,0\", \"areia=1,0\"] },"
			+ "  { \"atlas\": \"Coisas\", \"fonte\": 2, \"colunas\": 2, \"iw\": 32, \"ih\": 32, \"res\": \"\", \"estados\": [\"arvore=0,0\", \"casa=1,0\"] },"
			+ "  { \"atlas\": \"SemCor\", \"fonte\": 9, \"colunas\": 1, \"iw\": 32, \"ih\": 32, \"res\": \"\", \"estados\": [\"x=0,0\"] } ]");
		var grama = new CorDeTile(40, 160, 40, 255);
		var areia = new CorDeTile(220, 200, 120, 255);
		var arvore = new CorDeTile(10, 60, 10, 80);     // copa rala: 31% do quadro
		var casa = new CorDeTile(150, 60, 40, 255);     // telhado cheio
		CoresPorFonte cores = CoresPorFonte.Montar(tiles, CoresDeTiles.Parse(CoresDeTiles.ParaJson(
		[
			new FolhaDeCores { Atlas = "Chaos", Colunas = 2, Linhas = 1, Cores = [grama, areia] },
			new FolhaDeCores { Atlas = "Coisas", Colunas = 2, Linhas = 1, Cores = [arvore, casa] },
		])));

		// UM MAPA DE 8x4: grama a esquerda, areia a direita, a coluna 7 sem chao (o vazio), uma arvore em
		// (1,1), uma casa em (2,2), uma ponte sobre o vazio em (7,3) e tres celulas de folha SEM cor.
		var chao = new List<CelulaDePedaco>();
		for (short y = 0; y < 4; y++)
			for (short x = 0; x < 7; x++)
				chao.Add(new CelulaDePedaco(x, y, 1, (ushort)(x < 4 ? 0 : 1), 0));
		chao.RemoveAll(c => c.Y == 0 && c.X is 4 or 5 or 6);
		chao.Add(new CelulaDePedaco(4, 0, 9, 0, 0));   // sem cor, e e AGUA no plano
		chao.Add(new CelulaDePedaco(5, 0, 9, 0, 0));   // sem cor, e e PAREDE no arquivo
		chao.Add(new CelulaDePedaco(6, 0, 9, 0, 0));   // sem cor, chao comum
		var objetos = new List<CelulaDePedaco> { new(1, 1, 2, 0, 0), new(2, 2, 2, 1, 0), new(7, 3, 2, 1, 0), new(3, 3, 9, 0, 0) };

		string tmp = Path.Combine(Path.GetTempPath(), $"mapa-bench-{Environment.ProcessId}.pedacos");
		PedacosDoMapa dados;
		try
		{
			PedacosDoMapa.Escrever(tmp, 64, ["Chao", "Decor", "Objetos"], [chao, [], objetos]);
			dados = PedacosDoMapa.Ler(File.ReadAllBytes(tmp))!;
		}
		finally { File.Delete(tmp); }

		var bits = new byte[(8 * 4 + 7) / 8];
		var agua = new byte[(8 * 4 + 7) / 8];
		bits[5 >> 3] |= 1 << (5 & 7);
		agua[4 >> 3] |= 1 << (4 & 7);
		ZoneCollision planos = ZoneCollision.Montar(8, 4, bits);
		planos.DefinirAgua(agua);

		ImagemDoLugar Montar() => MapaDoLugar.DoPreFeito(dados, 8, 4, cores, planos);
		ImagemDoLugar img = Montar();

		Checa("um pixel por tile: a imagem tem o tamanho do mapa", img is { Largura: 8, Altura: 4, Bloco: 1, LarguraEmTiles: 8 });
		Checa("grama em (0,0) e areia em (5,2): cada chao com a cor do tile dele",
			  img.Em(0, 0) == (40, 160, 40, 255) && img.Em(5, 2) == (220, 200, 120, 255), $"{img.Em(0, 0)} {img.Em(5, 2)}");
		Checa("onde nao ha chao o mapa e VAZIO (alfa 0)", img.Em(7, 0).A == 0 && img.Vazias == 3, $"{img.Vazias} vazias");

		bool ArvoreMistura()
		{
			(byte r, byte g, byte b, byte a) = Montar().Em(1, 1);
			// cobertura 80/255 x 1,6 = 0,50: metade arvore, metade grama
			return a == 255 && Math.Abs(r - 25) <= 2 && Math.Abs(g - 110) <= 2 && Math.Abs(b - 25) <= 2;
		}
		Checa("arvore de copa rala: metade arvore, metade grama (a floresta aparece sem virar tinta)", ArvoreMistura(), img.Em(1, 1).ToString());
		ComDefeito("so o chao entra na conta", v => MapaDoLugar.SoOChaoDeTeste = v, ArvoreMistura);
		Checa("telhado cheio cobre a celula inteira", img.Em(2, 2) == (150, 60, 40, 255), img.Em(2, 2).ToString());
		Checa("o que esta em cima do VAZIO e o chao dali (a ponte)", img.Em(7, 3) == (150, 60, 40, 255), img.Em(7, 3).ToString());
		Checa("enfeite de folha sem cor nao pinta nada por cima", img.Em(3, 3) == (40, 160, 40, 255), img.Em(3, 3).ToString());

		bool ReservaPeloTipo()
		{
			ImagemDoLugar m = Montar();
			CorDeTile a = MapaDoLugar.ReservaDaAgua, p = MapaDoLugar.ReservaDaParede, c = MapaDoLugar.ReservaDoChao;
			return m.NaReserva == 3 && m.Em(4, 0) == (a.R, a.G, a.B, 255) && m.Em(5, 0) == (p.R, p.G, p.B, 255) && m.Em(6, 0) == (c.R, c.G, c.B, 255);
		}
		Checa("tile SEM cor cai na reserva pelo TIPO de chao: agua, parede e chao, cada um com a sua (e a conta diz 3)",
			  ReservaPeloTipo(), $"{img.NaReserva} na reserva: {img.Em(4, 0)} {img.Em(5, 0)} {img.Em(6, 0)}");
		ComDefeito("sem reserva, o tile sem cor vira buraco", v => MapaDoLugar.SemReservaDeTeste = v, ReservaPeloTipo);
		Checa("as tres cores de reserva sao diferentes entre si",
			  MapaDoLugar.Reserva(true, true) == MapaDoLugar.ReservaDaAgua && MapaDoLugar.Reserva(false, true) == MapaDoLugar.ReservaDaParede
			  && MapaDoLugar.Reserva(false, false) == MapaDoLugar.ReservaDoChao
			  && MapaDoLugar.ReservaDaAgua != MapaDoLugar.ReservaDaParede && MapaDoLugar.ReservaDaParede != MapaDoLugar.ReservaDoChao);
	}

	// =====================================================================
	// 4. O PLANETA GERADO
	// =====================================================================
	private static void Gerado()
	{
		Console.WriteLine("--- 4. o planeta gerado ---");
		TerrenoGerado t = GeradorDeTerreno.Gerar(new ParametrosDeTerreno { Seed = 0xD0C0FFEE, Largura = 96, Altura = 96, Bioma = BiomaDeTerreno.Deserto });

		// UMA FOLHA POR NOME DE ATLAS QUE O TERRENO PEDE (as cinco classes e toda cobertura que ele tem), com um
		// quadro de cor conhecida por estado. Perguntado ao proprio terreno, como o mapa faz.
		var pedidos = new List<TileVisual>();
		for (int c = 0; c < 5; c++) pedidos.Add(t.VisualDaClasse((ClasseDeTerreno)c));
		for (int n = 0; n < t.Cobertura.Length; n++)
			if (t.Cobertura[n] != 0 && t.VisualDaCobertura(n % 96, n / 96) is { } cobertura && !pedidos.Contains(cobertura)) pedidos.Add(cobertura);
		var porAtlas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (TileVisual v in pedidos)
		{
			if (!porAtlas.TryGetValue(v.Atlas, out List<string>? l)) porAtlas[v.Atlas] = l = [];
			if (!l.Contains(v.Estado)) l.Add(v.Estado);
		}
		var json = new System.Text.StringBuilder("[");
		var folhas = new List<FolhaDeCores>();
		int fonte = 1;
		foreach ((string atlas, List<string> estados) in porAtlas)
		{
			json.Append(fonte > 1 ? "," : "").Append($" {{ \"atlas\": \"{atlas}\", \"fonte\": {fonte}, \"colunas\": {estados.Count}, \"iw\": 32, \"ih\": 32, \"res\": \"\", \"estados\": [")
				.Append(string.Join(", ", estados.Select((e, i) => $"\"{e}={i},0\""))).Append("] }");
			folhas.Add(new FolhaDeCores
			{
				Atlas = atlas, Colunas = estados.Count, Linhas = 1,
				Cores = [.. estados.Select((_, i) => new CorDeTile((byte)(200 - fonte * 7), (byte)(100 + i * 30), (byte)(40 + fonte * 9), 255))],
			});
			fonte++;
		}
		CatalogoDeTiles tiles = CatalogoDeTiles.Parse(json.Append(" ]").ToString());
		CoresDeTiles cores = CoresDeTiles.Parse(CoresDeTiles.ParaJson(folhas));

		ImagemDoLugar img = MapaDoLugar.DoTerreno(t, tiles, cores);
		Checa("o mapa do gerado tem o tamanho do terreno e nenhuma celula vazia nem na reserva",
			  img is { Largura: 96, Altura: 96, Vazias: 0, NaReserva: 0 }, $"{img.Largura}x{img.Altura}, {img.Vazias} vazias, {img.NaReserva} na reserva");

		// CADA CLASSE COM A COR DO TILE DELA VEZES A TINTA (o Deserto pinta as cinco). Uma celula SEM cobertura de cada classe.
		int certas = 0, vistas = 0;
		TileVisual[] visuais = [.. Enumerable.Range(0, 5).Select(c => t.VisualDaClasse((ClasseDeTerreno)c))];
		for (int classe = 0; classe < 5; classe++)
		{
			int n = Array.FindIndex(t.Chao, 0, c => c == classe);
			while (n >= 0 && t.Cobertura[n] != 0) n = Array.FindIndex(t.Chao, n + 1, c => c == classe);
			if (n < 0) continue;
			vistas++;
			CorDeTile? esperada = MapaDoLugar.CorDoVisual(visuais[classe], tiles, cores);
			(byte r, byte g, byte b, byte _) = img.Em(n % 96, n / 96);
			if (esperada is { } e && (r, g, b) == (e.R, e.G, e.B)) certas++;
		}
		Checa("cada classe de terreno sai com a cor do tile que a paleta do bioma manda desenhar", vistas >= 4 && certas == vistas, $"{certas} de {vistas} classes");

		// A TINTA: a primeira classe do Deserto que a paleta pinta (hoje, todas as cinco).
		TileVisual pintado = visuais.FirstOrDefault(v => v.Tinta is { Length: 7 });
		if (pintado.Tinta is { } hexa)
		{
			int tinta = Convert.ToInt32(hexa[1..], 16);
			CorDeTile semTinta = cores.De(tiles.Atlas(pintado.Atlas)!.Nome, tiles.Achar(pintado)!.Value.X, 0)!.Value;
			CorDeTile comTinta = MapaDoLugar.CorDoVisual(pintado, tiles, cores)!.Value;
			Checa("a TINTA do bioma multiplica a cor canal a canal (o chao do Deserto e a arte da Terra pintada de areia)",
				  comTinta.R == semTinta.R * ((tinta >> 16) & 255) / 255 && comTinta.G == semTinta.G * ((tinta >> 8) & 255) / 255
				  && comTinta.B == semTinta.B * (tinta & 255) / 255 && comTinta != semTinta, $"{hexa}: {semTinta} -> {comTinta}");
		}
		else Checa("a paleta do Deserto pinta alguma classe de terreno (senao nao ha tinta pra medir)", false);

		ImagemDoLugar semCores = MapaDoLugar.DoTerreno(t, tiles, CoresDeTiles.Parse(""));
		int agua = Array.IndexOf(t.Chao, (byte)ClasseDeTerreno.Agua), montanha = Array.IndexOf(t.Chao, (byte)ClasseDeTerreno.Montanha);
		Checa("sem arquivo de cores o gerado cai INTEIRO na reserva pelo tipo: agua azul, montanha cinza",
			  semCores.NaReserva == 96 * 96 && agua >= 0 && montanha >= 0
			  && semCores.Em(agua % 96, agua / 96) == (MapaDoLugar.ReservaDaAgua.R, MapaDoLugar.ReservaDaAgua.G, MapaDoLugar.ReservaDaAgua.B, 255)
			  && semCores.Em(montanha % 96, montanha / 96) == (MapaDoLugar.ReservaDaParede.R, MapaDoLugar.ReservaDaParede.G, MapaDoLugar.ReservaDaParede.B, 255),
			  $"{semCores.NaReserva} na reserva");
	}

	// =====================================================================
	// 5. LUGAR MAIOR QUE A IMAGEM
	// =====================================================================
	private static void Blocos()
	{
		Console.WriteLine("--- 5. lugar maior que a imagem ---");
		Checa("ate 512 tiles e um pixel por tile; 513..1024 sao blocos de 2; 1025 ja sao de 3",
			  MapaDoLugar.BlocoPara(192, 192) == 1 && MapaDoLugar.BlocoPara(500, 500) == 1 && MapaDoLugar.BlocoPara(512, 100) == 1
			  && MapaDoLugar.BlocoPara(513, 513) == 2 && MapaDoLugar.BlocoPara(1000, 1000) == 2 && MapaDoLugar.BlocoPara(40, 1025) == 3);

		TerrenoGerado t = GeradorDeTerreno.Gerar(new ParametrosDeTerreno { Seed = 7, Largura = 1000, Altura = 1000, Bioma = BiomaDeTerreno.Jardim });
		ImagemDoLugar img = MapaDoLugar.DoTerreno(t, CatalogoDeTiles.Parse(""), CoresDeTiles.Parse(""));
		Checa("um mundo de 1000x1000 vira uma imagem de 500x500 (blocos de 2x2), que continua dizendo 1000 tiles",
			  img is { Largura: 500, Altura: 500, Bloco: 2, LarguraEmTiles: 1000, AlturaEmTiles: 1000 } && img.Rgba.Length == 500 * 500 * 4,
			  $"{img.Largura}x{img.Altura}, bloco {img.Bloco}");

		// O PIXEL E A MEDIA DO BLOCO: um bloco todo agua e a cor da agua; um misto fica entre as duas.
		int puro = -1, misto = -1;
		for (int py = 0; py < 500 && (puro < 0 || misto < 0); py++)
			for (int px = 0; px < 500 && (puro < 0 || misto < 0); px++)
			{
				int aguas = 0;
				for (int k = 0; k < 4; k++)
					if (t.Chao[(py * 2 + k / 2) * 1000 + px * 2 + k % 2] == (byte)ClasseDeTerreno.Agua) aguas++;
				bool temMontanha = false;
				for (int k = 0; k < 4; k++) temMontanha |= t.Chao[(py * 2 + k / 2) * 1000 + px * 2 + k % 2] == (byte)ClasseDeTerreno.Montanha;
				if (aguas == 4 && puro < 0) puro = py * 500 + px;
				if (aguas == 2 && !temMontanha && misto < 0) misto = py * 500 + px;
			}
		CorDeTile a = MapaDoLugar.ReservaDaAgua, c = MapaDoLugar.ReservaDoChao;
		Checa("bloco todo agua tem a cor da agua; bloco meio agua, meio chao fica na MEDIA das duas",
			  puro >= 0 && misto >= 0 && img.Em(puro % 500, puro / 500) == (a.R, a.G, a.B, 255)
			  && img.Em(misto % 500, misto / 500) == ((byte)((a.R * 2 + c.R * 2) / 4), (byte)((a.G * 2 + c.G * 2) / 4), (byte)((a.B * 2 + c.B * 2) / 4), 255),
			  puro < 0 || misto < 0 ? "nao achei os dois blocos" : $"{img.Em(puro % 500, puro / 500)} {img.Em(misto % 500, misto / 500)}");
	}

	// =====================================================================
	// 6. ONDE EU ESTOU
	// =====================================================================
	private static void Marcador()
	{
		Console.WriteLine("--- 6. o marcador e as coordenadas ---");
		const int T = ZoneCollision.TileSize;
		Vec2 Centro(float cx, float cy) => new(cx * T, cy * T - MoveRules.FeetOffsetY);   // os PES caem em (cx, cy) tiles
		static bool Perto(float a, float b) => Math.Abs(a - b) < 0.002f;

		(float u, float v, bool fora) = MapaDoLugar.Marcador(Centro(250, 250), 500, 500);
		Checa("no meio do planeta o marcador fica no meio do mapa", Perto(u, 0.5f) && Perto(v, 0.5f) && !fora, $"{u:0.000},{v:0.000}");
		(u, v, fora) = MapaDoLugar.Marcador(Centro(125, 400), 500, 500);
		Checa("um quarto pro leste e quatro quintos pro sul: (0,25; 0,80) -- X e Y nao estao trocados", Perto(u, 0.25f) && Perto(v, 0.8f) && !fora, $"{u:0.000},{v:0.000}");

		bool NosCantos()
		{
			(float u0, float v0, bool f0) = MapaDoLugar.Marcador(Centro(0, 0), 500, 500);
			(float u1, float v1, bool f1) = MapaDoLugar.Marcador(Centro(500, 500), 500, 500);
			(float u2, float v2, bool f2) = MapaDoLugar.Marcador(Centro(500, 0), 192, 192);
			return Perto(u0, 0) && Perto(v0, 0) && !f0 && Perto(u1, 1) && Perto(v1, 1) && !f1 && u2 == 1f && Perto(v2, 0) && f2;
		}
		Checa("nos cantos o marcador encosta na borda; num mundo de 192, quem esta no tile 500 fica PRESO na borda leste", NosCantos());

		bool PresoForaDaBorda()
		{
			(float uu, float vv, bool f) = MapaDoLugar.Marcador(Centro(-40, 900), 500, 500);
			return uu == 0f && vv == 1f && f;
		}
		Checa("alem da borda (a Sala do Tempo nao tem beirada): preso no canto certo, e dizendo que esta FORA", PresoForaDaBorda());
		ComDefeito("o marcador nao e preso ao retangulo", x => MapaDoLugar.MarcadorSoltoDeTeste = x, PresoForaDaBorda);

		// A VISTA DE PERTO: a janela de 128 tiles em volta do corpo, presa dentro do mapa.
		(float jx, float jy, float jl, float ju, float jv) = MapaDoLugar.VistaDePerto(Centro(500, 500), 1000, 1000);
		Checa("vista de perto no meio de um mundo de 1000: a janela de 128 tiles fica centrada no corpo, e o marcador no meio dela",
			  jl == MapaDoLugar.TilesDaVistaDePerto && Perto(jx, 436) && Perto(jy, 436) && Perto(ju, 0.5f) && Perto(jv, 0.5f), $"{jx},{jy} lado {jl} marcador {ju:0.00},{jv:0.00}");
		bool JanelaPresa()
		{
			(float x0, float y0, float l, float uu, float vv) = MapaDoLugar.VistaDePerto(Centro(10, 990), 1000, 1000);
			return x0 == 0f && y0 == 1000 - l && Perto(uu, 10f / l) && Perto(vv, (990f - (1000 - l)) / l);
		}
		Checa("...junto do canto (10, 990) a janela PARA na borda do mapa, e e o marcador que sai do centro", JanelaPresa());
		ComDefeito("a janela da vista de perto nao e presa dentro do mapa", x => MapaDoLugar.JanelaSoltaDeTeste = x, JanelaPresa);
		(jx, jy, jl, ju, jv) = MapaDoLugar.VistaDePerto(Centro(20, 30), 40, 100);
		Checa("...e num lugar menor que a janela (40 x 100) ela tem o lado do lugar", jl == 40 && jx == 0 && Perto(ju, 0.5f) && jy == 10, $"{jx},{jy} lado {jl}");

		Checa("as coordenadas sao a celula dos PES, em tiles", MapaDoLugar.Coordenadas(Centro(312.5f, 87.5f)) == "X 312 · Y 87"
			  && MapaDoLugar.Celula(Centro(0.5f, 499.5f)) == (0, 499), MapaDoLugar.Coordenadas(Centro(312.5f, 87.5f)));
		Checa("...e continuam as de verdade alem da borda (negativas a oeste do mapa)", MapaDoLugar.Coordenadas(Centro(-39.5f, 900.5f)) == "X -40 · Y 900",
			  MapaDoLugar.Coordenadas(Centro(-39.5f, 900.5f)));
	}

	// =====================================================================
	// 7. O DISCO DO RADAR
	// =====================================================================
	private static void Radar()
	{
		Console.WriteLine("--- 7. o disco do radar ---");
		float alcance = RadarDeEsferas.AlcanceEmPx(RadarDeEsferas.AlcanceDeFabrica);
		Checa("o alcance de fabrica e o do meio da tabela, em pixels; o indice da a volta",
			  alcance == RadarDeEsferas.AlcancesEmTiles[1] * 32f && RadarDeEsferas.AlcanceEmPx(3) == RadarDeEsferas.AlcanceEmPx(0)
			  && RadarDeEsferas.AlcancesEmTiles[0] < RadarDeEsferas.AlcancesEmTiles[1] && RadarDeEsferas.AlcancesEmTiles[1] < RadarDeEsferas.AlcancesEmTiles[2]);

		bool NosQuatroQuadrantes()
		{
			float m = alcance / 2;
			(float x1, float y1, bool b1) = RadarDeEsferas.NoDisco(m, -m, alcance);    // nordeste
			(float x2, float y2, bool b2) = RadarDeEsferas.NoDisco(-m, -m, alcance);   // noroeste
			(float x3, float y3, bool b3) = RadarDeEsferas.NoDisco(-m, m, alcance);    // sudoeste
			(float x4, float y4, bool b4) = RadarDeEsferas.NoDisco(m, m, alcance);     // sudeste
			return !b1 && !b2 && !b3 && !b4
				&& x1 == 0.5f && y1 == -0.5f && x2 == -0.5f && y2 == -0.5f && x3 == -0.5f && y3 == 0.5f && x4 == 0.5f && y4 == 0.5f;
		}
		Checa("nos quatro quadrantes: leste a direita, NORTE EM CIMA, a meio alcance no meio do raio", NosQuatroQuadrantes());
		ComDefeito("o eixo Y do disco invertido", v => RadarDeEsferas.NorteParaBaixoDeTeste = v, NosQuatroQuadrantes);

		bool PresoNaBorda()
		{
			(float x, float y, bool b) = RadarDeEsferas.NoDisco(alcance * 3, -alcance * 4, alcance);   // 5 alcances, a nordeste
			(float xo, float yo, bool bo) = RadarDeEsferas.NoDisco(-alcance * 10, 0, alcance);         // 10 alcances, a oeste
			return b && bo && Math.Abs(x - 0.6f) < 1e-4 && Math.Abs(y + 0.8f) < 1e-4 && Math.Abs(MathF.Sqrt(x * x + y * y) - 1f) < 1e-4
				&& xo == -1f && yo == 0f;
		}
		Checa("alem do alcance o sinal fica PRESO NA BORDA do disco, na direcao certa", PresoNaBorda());
		ComDefeito("o sinal alem do alcance nao e preso", v => RadarDeEsferas.SemPrenderNaBordaDeTeste = v, PresoNaBorda);

		(float cx, float cy, bool cb) = RadarDeEsferas.NoDisco(0, 0, alcance);
		Checa("esfera em cima de mim cai no centro, e exatamente no alcance ainda e DENTRO", cx == 0 && cy == 0 && !cb && !RadarDeEsferas.NoDisco(alcance, 0, alcance).NaBorda);

		Vec2 q = RadarDeEsferas.PosicaoDaCarregada(new Vec2(1000.9f, 47f));
		Checa("a esfera carregada viaja no centro do tile de quem a carrega (andar dentro do tile nao muda o pacote)",
			  q.Equals(new Vec2(1008f, 48f)) && RadarDeEsferas.PosicaoDaCarregada(new Vec2(1023f, 33f)).Equals(q)
			  && !RadarDeEsferas.PosicaoDaCarregada(new Vec2(1025f, 33f)).Equals(q), $"{q.X},{q.Y}");
	}

	// =====================================================================
	// 8. COM OS ARQUIVOS DO JOGO
	// =====================================================================
	private static void ComOsArquivosDoJogo(string raiz, string? caminhoDasCores)
	{
		Console.WriteLine("--- 8. com os arquivos do jogo ---");
		string Disco(string res) => Path.Combine(raiz, res.Replace("res://", "").Replace('/', Path.DirectorySeparatorChar));
		string tilesJson = Path.Combine(raiz, "Assets", "Data", "tiles.json");
		caminhoDasCores ??= Path.Combine(raiz, "Assets", "Data", "tiles-cores.json");
		if (!File.Exists(tilesJson) || !File.Exists(caminhoDasCores))
		{
			Checa("o tiles.json e o tiles-cores.json existem", false, $"{tilesJson} | {caminhoDasCores} (rode o comando 'cores')");
			return;
		}

		var relogio = Stopwatch.StartNew();
		CatalogoDeTiles tiles = CatalogoDeTiles.Parse(File.ReadAllText(tilesJson));
		double msTiles = relogio.Elapsed.TotalMilliseconds;
		relogio.Restart();
		CoresDeTiles cores = CoresDeTiles.Parse(File.ReadAllText(caminhoDasCores));
		double msCores = relogio.Elapsed.TotalMilliseconds;
		relogio.Restart();
		CoresPorFonte porFonte = CoresPorFonte.Montar(tiles, cores);
		Console.WriteLine($"    ler os catalogos: tiles.json {msTiles:0.0} ms, tiles-cores.json {msCores:0.0} ms, cruzar {relogio.Elapsed.TotalMilliseconds:0.0} ms "
						  + $"({cores.Total} folhas, {cores.TotalDeQuadros} quadros)");
		Checa("toda folha do tileset tem cor publicada com a MESMA grade (senao: rode o comando 'cores')",
			  porFonte.SemCor.Count == 0 && cores.Total > 0, porFonte.SemCor.Count == 0 ? $"{cores.Total} folhas" : string.Join(", ", porFonte.SemCor.Take(8)));

		ZoneCatalog zonas = ZoneCatalog.Parse(File.ReadAllText(Path.Combine(raiz, "Assets", "Maps", "manifest.json")));
		int comMapa = 0, naReserva = 0;
		double pior = 0;
		string qualPior = "";
		Console.WriteLine($"    {"zona",-28} {"tiles",9} {"imagem",9} {"montar",9} {"vazias",8}");
		foreach (ZoneEntry e in zonas.Entradas)
		{
			if (!File.Exists(Disco(e.Pedacos)) || PedacosDoMapa.Ler(File.ReadAllBytes(Disco(e.Pedacos))) is not { } dados) continue;
			ZoneCollision? planos = File.Exists(Disco(e.Colisao)) ? ZoneCollision.Load(File.ReadAllBytes(Disco(e.Colisao))) : null;
			if (planos != null && File.Exists(Disco(e.CaminhoDaAgua))) planos.CarregarAgua(File.ReadAllBytes(Disco(e.CaminhoDaAgua)));

			MapaDoLugar.DoPreFeito(dados, e.W, e.H, porFonte, planos);   // aquece o JIT fora da conta
			relogio.Restart();
			ImagemDoLugar img = MapaDoLugar.DoPreFeito(dados, e.W, e.H, porFonte, planos);
			double ms = relogio.Elapsed.TotalMilliseconds;
			if (dados.TotalDeCelulas == 0) continue;

			comMapa++;
			naReserva += img.NaReserva;
			if (ms > pior) { pior = ms; qualPior = e.Zona; }
			if (comMapa <= 6 || img.NaReserva > 0)
				Console.WriteLine($"    {e.Zona,-28} {e.W + "x" + e.H,9} {img.Largura + "x" + img.Altura,9} {ms,6:0.0} ms {img.Vazias,8}"
								  + (img.NaReserva > 0 ? $"   <-- {img.NaReserva} celula(s) na RESERVA" : ""));
		}
		Console.WriteLine($"    ... {comMapa} zonas com chao; a mais cara: {qualPior}, {pior:0.0} ms");
		Checa("toda zona pre-feita vira mapa sem NENHUMA celula na cor de reserva (as cores estao em dia com os mapas)",
			  comMapa >= 20 && naReserva == 0, $"{comMapa} zonas, {naReserva} celulas na reserva");

		// A TERRA TEM MAR, GRAMA E NEVE -- e cada um sai com uma cor que se distingue. E o mapa "com a cara do planeta".
		if (zonas.Get("Earth") is { } terra && PedacosDoMapa.Ler(File.ReadAllBytes(Disco(terra.Pedacos))) is { } daTerra)
		{
			ImagemDoLugar img = MapaDoLugar.DoPreFeito(daTerra, terra.W, terra.H, porFonte, null);
			var conta = new Dictionary<int, int>();
			for (int i = 0; i < img.Rgba.Length; i += 4)
			{
				int chave = (img.Rgba[i] >> 5 << 6) | (img.Rgba[i + 1] >> 5 << 3) | (img.Rgba[i + 2] >> 5);
				conta[chave] = conta.GetValueOrDefault(chave) + 1;
			}
			int azuis = 0, verdes = 0;
			for (int i = 0; i < img.Rgba.Length; i += 4)
			{
				if (img.Rgba[i + 2] > img.Rgba[i] + 40 && img.Rgba[i + 2] > img.Rgba[i + 1]) azuis++;
				if (img.Rgba[i + 1] > img.Rgba[i] + 25 && img.Rgba[i + 1] > img.Rgba[i + 2] + 25) verdes++;
			}
			Checa("o mapa da Terra tem mar AZUL (mais de um terco) e terra VERDE (mais de um decimo), em varias cores",
				  azuis > 250000 / 3 && verdes > 25000 && conta.Count >= 12, $"{azuis} azuis, {verdes} verdes, {conta.Count} faixas de cor");
		}

		// O CASO CARO: um planeta gerado de 1000x1000 (o maior que a gravidade sorteia).
		Console.WriteLine($"    {"gerado",-28} {"tiles",9} {"imagem",9} {"montar",9}   (o terreno ja existe no cliente: o custo e so o do mapa)");
		foreach (int lado in new[] { 192, 500, 1000 })
		{
			TerrenoGerado t = GeradorDeTerreno.Gerar(new ParametrosDeTerreno { Seed = 0xABCDEF, Largura = lado, Altura = lado, Bioma = BiomaDeTerreno.Jardim });
			MapaDoLugar.DoTerreno(t, tiles, cores);
			relogio.Restart();
			ImagemDoLugar img = MapaDoLugar.DoTerreno(t, tiles, cores);
			double ms = relogio.Elapsed.TotalMilliseconds;
			Console.WriteLine($"    {"Jardim " + lado,-28} {lado + "x" + lado,9} {img.Largura + "x" + img.Altura,9} {ms,6:0.0} ms");
			if (lado == 1000)
				Checa("um planeta gerado de 1000x1000 vira mapa sem nenhuma celula na reserva, numa imagem de 500x500",
					  img is { NaReserva: 0, Vazias: 0, Largura: 500, Bloco: 2 }, $"{img.NaReserva} na reserva, {ms:0.0} ms");
		}

		int biomasEmDia = 0;
		foreach (BiomaDeTerreno b in Enum.GetValues<BiomaDeTerreno>())
		{
			TerrenoGerado t = GeradorDeTerreno.Gerar(new ParametrosDeTerreno { Seed = 99, Largura = 128, Altura = 128, Bioma = b });
			if (MapaDoLugar.DoTerreno(t, tiles, cores).NaReserva == 0) biomasEmDia++;
			else Console.WriteLine($"    bioma {b}: ha classe de terreno sem cor");
		}
		Checa("os seis biomas do gerador tem cor pra todas as classes de terreno", biomasEmDia == 6, $"{biomasEmDia} de 6");
	}
}
