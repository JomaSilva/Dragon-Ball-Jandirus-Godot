using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// BANCO DE PROVA DAS PORTAS. Roda contra os arquivos DE VERDADE (`.portas`, `.col`, `.vis` da
/// pasta Assets/Maps), nao contra um mapa inventado.
///
/// ============================ O QUE ELE PEGA ============================
/// A porta e um caso onde "parece funcionar" e barato e "funciona" e caro, porque as tres coisas
/// que podem quebrar sao INVISIVEIS numa olhada:
///
///   1. a porta fechada tem que BLOQUEAR e CEGAR (ate hoje ela nao fazia nem um nem outro: o
///      conversor a tirava da colisao, e ela ficava aberta pra sempre);
///   2. o gatilho tem que pegar de UMA celula de distancia e nao de duas -- longe demais e a porta
///      abre sozinha do outro lado da rua, perto demais e ela nunca abre, porque ela BLOQUEIA e
///      ninguem chega a ocupar a celula dela;
///   3. abrir nao pode ser permanente. O `.col` e o `.vis` sao cacheados por sessao, entao uma
///      abertura escrita no lugar errado sobrevive a viagem pro outro planeta.
/// ========================================================================
/// </summary>
public static class PortaBench
{
	public static void Run(string pastaMaps)
	{
		Console.WriteLine("=== PORTAS ===\n");

		string manifesto = Path.Combine(pastaMaps, "manifest.json");
		if (!File.Exists(manifesto)) { Console.WriteLine($"sem {manifesto} -- rode o comando 'maps' antes."); return; }

		ZoneCatalog cat = ZoneCatalog.Parse(File.ReadAllText(manifesto));

		int zonas = 0, total = 0, falhas = 0;
		foreach (ZoneEntry e in cat.Todas)
		{
			string arq = Local(pastaMaps, e.Portas);
			if (arq.Length == 0 || !File.Exists(arq)) continue;

			List<PortaDoMapa> portas = PortasDaZona.Parse(File.ReadAllText(arq));
			if (portas.Count == 0) continue;

			ZoneCollision? col = Carregar(Local(pastaMaps, e.Colisao));
			ZoneCollision? vis = Carregar(Local(pastaMaps, e.Visao));
			if (col == null || vis == null) { Console.WriteLine($"  {e.Zona}: sem .col/.vis"); continue; }

			zonas++;
			total += portas.Count;

			int naoBloqueia = 0, naoCega = 0, semArte = 0;
			foreach (PortaDoMapa p in portas)
			{
				if (!col.BlockedCell(p.X, p.Y)) naoBloqueia++;
				if (!vis.BlockedCell(p.X, p.Y)) naoCega++;
				if (!p.Arte.EndsWith(".tres", StringComparison.OrdinalIgnoreCase)) semArte++;
			}

			// ---- abrir e fechar de novo, na porta de verdade ----
			PortaDoMapa alvo = portas[0];
			col.Abrir(alvo.X, alvo.Y);
			vis.Abrir(alvo.X, alvo.Y);
			bool abriuCol = !col.BlockedCell(alvo.X, alvo.Y);
			bool abriuVis = !vis.BlockedCell(alvo.X, alvo.Y);

			// ...e o `FecharTudo` que roda ao entrar na zona tem que devolver TUDO ao arquivo
			col.FecharTudo();
			vis.FecharTudo();
			bool voltou = col.BlockedCell(alvo.X, alvo.Y) && vis.BlockedCell(alvo.X, alvo.Y);

			int ruim = naoBloqueia + naoCega + semArte
					 + (abriuCol ? 0 : 1) + (abriuVis ? 0 : 1) + (voltou ? 0 : 1);
			falhas += ruim;

			Console.WriteLine($"  {e.Zona,-22} {portas.Count,3} porta(s)"
				+ (naoBloqueia > 0 ? $" | {naoBloqueia} NAO BLOQUEIAM" : "")
				+ (naoCega > 0 ? $" | {naoCega} NAO CEGAM" : "")
				+ (semArte > 0 ? $" | {semArte} SEM ARTE" : "")
				+ (abriuCol && abriuVis ? "" : " | ABRIR NAO SURTIU EFEITO")
				+ (voltou ? "" : " | FECHAR TUDO NAO RESTAUROU")
				+ (ruim == 0 ? "  ok" : ""));
		}

		Console.WriteLine($"\n{total} porta(s) em {zonas} zona(s) no mapa\n");

		// =================================================================
		// O GATILHO: de que distancia a porta abre
		// =================================================================
		// O ESPERADO ESTA NA TABELA. Sem ele isto seria um despejo de booleanos que "parece certo"
		// -- e foi exatamente assim que a primeira versao passou por certa enquanto NUNCA abria.
		Console.WriteLine("--- o gatilho (porta na celula 10,10) ---");
		const int T = ZoneCollision.TileSize;
		const int cx = 10, cy = 10;

		int erros = 0;
		foreach ((string caso, float dx, float dy, Facing olhar, bool esperado) in
			new (string, float, float, Facing, bool)[]
		{
			// de frente, a uma celula: e o passo do BYOND caindo na porta
			("ao sul,   olhando pro norte ", 0, +T, Facing.North, true),
			("ao norte, olhando pro sul   ", 0, -T, Facing.South, true),
			("a oeste,  olhando pro leste ", -T, 0, Facing.East, true),
			("a leste,  olhando pro oeste ", +T, 0, Facing.West, true),
			// encostado na porta (a colisao para o corpo aqui)
			("encostado, olhando pro norte", 0, +T / 2f + MoveRules.BodyHalfH, Facing.North, true),
			// ao lado, olhando PRA OUTRO LADO: e passar rente a parede, nao entrar
			("ao sul,   olhando pro leste ", 0, +T, Facing.East, false),
			("ao sul,   olhando pro sul   ", 0, +T, Facing.South, false),
			// longe demais
			("a duas celulas, de frente   ", 0, +2 * T, Facing.North, false),
			("a tres celulas, de frente   ", 0, +3 * T, Facing.North, false),
		})
		{
			var pos = new Vec2(cx * T + T / 2f + dx, cy * T + T / 2f + dy - MoveRules.FeetOffsetY);
			bool abre = PortasDaZona.VaiEntrar(pos, olhar, cx, cy);
			if (abre != esperado) erros++;
			Console.WriteLine($"  {caso}: {(abre ? "ABRE    " : "nao abre")}"
							  + (abre == esperado ? "  ok" : $"  ERRADO (esperado {(esperado ? "ABRE" : "nao abre")})"));
		}
		falhas += erros;

		// =================================================================
		// ATRAVESSAR: fechada barra o passo, aberta deixa passar
		// =================================================================
		Console.WriteLine("\n--- atravessar (parede de 1 celula com porta no meio) ---");
		ZoneCollision corredor = Corredor(cx, cy);

		var antes = new Vec2(cx * T + T / 2f, (cy + 1) * T + T / 2f - MoveRules.FeetOffsetY);
		var depois = new Vec2(cx * T + T / 2f, (cy - 1) * T + T / 2f - MoveRules.FeetOffsetY);

		bool f1 = MoveRules.PathOccupied(corredor, antes, depois);
		corredor.Abrir(cx, cy);
		bool a1 = MoveRules.PathOccupied(corredor, antes, depois);
		corredor.Fechar(cx, cy);
		bool f2 = MoveRules.PathOccupied(corredor, antes, depois);

		Console.WriteLine($"  fechada: {(f1 ? "BARRA  ok" : "passa  ERRADO")}");
		Console.WriteLine($"  aberta : {(a1 ? "BARRA  ERRADO" : "passa  ok")}");
		Console.WriteLine($"  fechou : {(f2 ? "BARRA  ok" : "passa  ERRADO")}");
		if (!f1 || a1 || !f2) falhas++;

		Console.WriteLine($"\n  fecha sozinha em {PortasDaZona.SegundosAberta:0.#}s (o spawn(50) do DM)");

		falhas += Obras(pastaMaps);
		Console.WriteLine(falhas == 0 ? "\nTUDO OK\n" : $"\n{falhas} FALHA(S)\n");
	}

	/// <summary>
	/// AS CONSTRUCOES: a outra metade do mesmo problema.
	///
	/// A queixa do dono foi "o banco n tem fisica, eu atravesso ele". O banco do MAPA foi
	/// consertado na arvore de tipos do DM. A construcao ERGUIDA por um jogador era outra coisa:
	/// nunca teve fisica em lugar nenhum, e a MESMA Research Station bloqueava quando vinha do
	/// `.dmm` e nao bloqueava quando alguem a construia.
	///
	/// Aqui se confere o que o catalogo diz DEPOIS de extraido -- porque o defeito nao era falta
	/// de codigo, era ler o typepath errado: o `obj/Creatables/Research_Station` tem `density = 0`
	/// e o `/obj/Technology/Research_Station` que ele CRIA tem `density = 1`.
	/// </summary>
	private static int Obras(string pastaMaps)
	{
		Console.WriteLine("\n=== CONSTRUCOES ===\n");

		// o catalogo mora ao lado dos mapas: Assets/Maps -> Assets/Data
		string assets = Path.GetDirectoryName(pastaMaps.TrimEnd('/', '\\'))!;
		string cj = Path.Combine(assets, "Data", "construcoes.json");
		if (!File.Exists(cj)) { Console.WriteLine($"sem {cj} -- rode o comando 'tech' antes."); return 1; }

		Jandirus.Core.Tech.CatalogoDeObras cat = Jandirus.Core.Tech.CatalogoDeObras.Parse(File.ReadAllText(cj));
		int comArte = cat.Todas.Count(c => c.Arte.Length > 0);
		int densas = cat.Todas.Count(c => c.Densa);
		Console.WriteLine($"  {cat.Total} no catalogo | {comArte} com sprite | {densas} bloqueiam");

		int erros = 0;

		// A que o dono fotografou. Ela tem que bloquear, ter arte, e a arte tem que ser ANIMADA.
		Jandirus.Core.Tech.Construcao? rs = cat.Get("Research_Station");
		if (rs == null) { Console.WriteLine("  Research_Station NAO ESTA no catalogo  ERRADO"); erros++; }
		else
		{
			Console.WriteLine($"  Research_Station: densa={(rs.Densa ? "sim  ok" : "NAO  ERRADO")}"
							  + $" | px={rs.PixelX:0} | arte={(rs.Arte.Length > 0 ? "sim  ok" : "NAO  ERRADO")}");
			if (!rs.Densa) erros++;
			if (rs.Arte.Length == 0) erros++;
			else
			{
				// `res://` E A RAIZ DO PROJETO, que aqui e a pasta acima de Assets/
				string tres = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(assets)!,
					rs.Arte.Replace("res://", "")));
				int quadros = File.Exists(tres)
					? System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(tres), "\"duration\"").Count
					: -1;
				Console.WriteLine($"    {Path.GetFileName(tres)}: {(quadros < 0 ? "NAO EXISTE  ERRADO" : quadros + " quadro(s)")}"
								  + (quadros > 1 ? "  ok (anima)" : quadros == 1 ? "  parada" : ""));
				if (quadros <= 1) erros++;
			}
		}

		// A CELULA e a mesma conta nas duas pontas -- o servidor bloqueia e o cliente desenha por
		// ela. Aqui so se confere que ela cai onde os PES estao, e nao onde o centro do corpo esta.
		const int T = ZoneCollision.TileSize;
		var mapa = ZoneCollision.Montar(32, 32, new byte[(32 * 32 + 7) / 8]);
		(int cx, int cy) = Jandirus.Core.Tech.CatalogoDeObras.Celula(5 * T + 16, 5 * T + 16 - MoveRules.FeetOffsetY);
		bool celulaCerta = cx == 5 && cy == 5;
		Console.WriteLine($"  celula de quem esta no meio do tile (5,5): ({cx},{cy})  {(celulaCerta ? "ok" : "ERRADO")}");
		if (!celulaCerta) erros++;

		mapa.Bloquear(cx, cy);
		bool barrou = mapa.BlockedCell(cx, cy);
		mapa.LimparObras();
		bool soltou = !mapa.BlockedCell(cx, cy);
		Console.WriteLine($"  erguer bloqueia: {(barrou ? "sim  ok" : "NAO  ERRADO")}"
						  + $" | derrubar libera: {(soltou ? "sim  ok" : "NAO  ERRADO")}");
		if (!barrou || !soltou) erros++;

		return erros;
	}

	/// <summary>Uma parede horizontal de um tile de altura, com um buraco de porta no meio.</summary>
	private static ZoneCollision Corredor(int cx, int cy)
	{
		const int w = 32, h = 32;
		var bits = new byte[(w * h + 7) / 8];
		for (int x = 0; x < w; x++)
		{
			int i = cy * w + x;
			bits[i >> 3] |= (byte)(1 << (i & 7));
		}
		return ZoneCollision.Montar(w, h, bits);
	}

	private static ZoneCollision? Carregar(string caminho) =>
		File.Exists(caminho) ? ZoneCollision.Load(File.ReadAllBytes(caminho)) : null;

	/// <summary>`res://Assets/Maps/x.col` -> o arquivo real dentro da pasta que o bench recebeu.</summary>
	private static string Local(string pastaMaps, string res) =>
		res.Length == 0 ? "" : Path.Combine(pastaMaps, Path.GetFileName(res));
}
