using System.Diagnostics;
using System.Text;

namespace Jandirus.Tools;

/// <summary>
/// REEMPACOTA OS QUADROS ANIMADOS numa tira, pra que o Godot consiga anima-los.
///
/// ============================ O PROBLEMA ============================
/// Um tile animado do Godot le os quadros como um retangulo dentro do atlas, a partir da coordenada
/// base. Com `animation_columns = 0` eles tem que caber TODOS NA MESMA LINHA.
///
/// O atlas do jogo e uma COPIA CRUA do .dmi -- a grade e a do BYOND, onde os quadros correm em
/// sequencia linear e viram a linha quando acaba a largura. Um estado que comeca no meio da linha
/// simplesmente nao cabe.
///
/// Medido antes deste arquivo existir: 211 das 417 animacoes eram DESCARTADAS por isso, e 829.961
/// celulas pintadas (16% do mapa) ficavam congeladas no primeiro quadro -- o oceano, o ceu, a borda
/// das ilhas, a research table.
///
/// `animation_columns = N` faz os quadros virarem a linha, mas so acerta quando o estado comeca na
/// COLUNA ZERO (a formula e `base + (n % N, n / N)`, e ela nao sabe da posicao inicial). Os casos
/// grandes comecam no meio: o oceano na coluna 7 de 14, o ceu na coluna 3 de 13.
/// ====================================================================
///
/// ============================ A SOLUCAO ============================
/// Um atlas COMPANHEIRO por folha, com UM ESTADO POR LINHA, comecando sempre na coluna zero. Com
/// isso o `animation_columns = 0` que o gerador ja escrevia volta a valer -- nao ha caso especial
/// no leitor, nem aritmetica de wrap pra errar.
///
/// QUEM COMPOE A IMAGEM E O GODOT. O pipeline nao tem codificador de PNG (o .dmi e copiado cru), e
/// acrescentar um so pra isto seria uma dependencia nova. O Godot ja esta a mao -- e ja e chamado
/// assim pra converter as cenas em binario (ver <see cref="SceneBinary"/>) -- e tem `Image.blit_rect`
/// e `Image.save_png`. Mesmo padrao, mesma ferramenta.
/// ==================================================================
/// </summary>
public static class AtlasAnimado
{
	/// <summary>Uma tira: os quadros de UM estado, em sequencia, numa linha do atlas companheiro.</summary>
	public sealed class Tira
	{
		/// <summary>O arquivo de origem, no disco.</summary>
		public string Origem = "";

		/// <summary>Indices LINEARES do quadro na folha de origem, em ordem.</summary>
		public int[] Quadros = [];

		/// <summary>Em que linha do companheiro esta tira mora.</summary>
		public int Linha;
	}

	/// <summary>
	/// Escreve o PNG companheiro de cada folha que precisou de tira.
	///
	/// <paramref name="porFolha"/> mapeia o PNG de origem -> (destino, largura do icone, altura,
	/// colunas da origem, tiras). Devolve quantos arquivos foram escritos.
	/// </summary>
	public static int Compor(Dictionary<string, (string Destino, int IconW, int IconH, int ColsOrigem, List<Tira> Tiras)> porFolha,
							 string raizDoProjeto, string? godot)
	{
		if (porFolha.Count == 0) return 0;

		godot ??= SceneBinary.AcharGodot();
		if (godot == null)
		{
			Console.WriteLine("AVISO: sem Godot -- os atlas de animacao NAO foram compostos.");
			Console.WriteLine("       As animacoes que nao cabiam numa linha continuam congeladas.");
			return 0;
		}

		string script = Path.Combine(raizDoProjeto, ".compor_animacoes.gd");
		File.WriteAllText(script, Gd(porFolha), new UTF8Encoding(false));

		try
		{
			var psi = new ProcessStartInfo(godot)
			{
				WorkingDirectory = raizDoProjeto,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			};
			psi.ArgumentList.Add("--headless");
			psi.ArgumentList.Add("--path");
			psi.ArgumentList.Add(raizDoProjeto);
			psi.ArgumentList.Add("--script");
			psi.ArgumentList.Add("res://.compor_animacoes.gd");

			using Process? p = Process.Start(psi);
			if (p == null) { Console.WriteLine("ERRO: nao consegui rodar o Godot"); return 0; }

			// OS DOIS FLUXOS DRENADOS AO MESMO TEMPO -- e a MESMA armadilha que o `SceneBinary` ja
			// documenta, e que eu reintroduzi aqui escrevendo este chamador do zero em vez de copiar
			// o padrao do irmao.
			//
			// `ReadToEnd()` no stdout e so DEPOIS esperar o processo trava quando o stderr enche o
			// buffer do sistema: o Godot fica bloqueado tentando escrever o erro e nos bloqueados
			// lendo a saida. Aqui enche facil -- ele reimporta centenas de PNG novos e comenta cada
			// um. Medido: vinte minutos com CPU zerada nos dois processos.
			var saida = new StringBuilder();
			p.OutputDataReceived += (_, e) => { if (e.Data != null) saida.AppendLine(e.Data); };
			// O STDERR E GUARDADO, nao descartado. O `SceneBinary` pode joga-lo fora porque o que
			// ele faz ou funciona ou aparece na contagem; aqui o script GDScript pode falhar por uma
			// API que mudou de nome, e a mensagem so existe nesse fluxo. Descartar custou uma rodada
			// inteira: o `Image.create` saiu no Godot 4.7 e a unica pista foi "0 arquivos".
			var erro = new StringBuilder();
			p.ErrorDataReceived += (_, e) => { if (e.Data != null) erro.AppendLine(e.Data); };
			p.BeginOutputReadLine();
			p.BeginErrorReadLine();

			if (!p.WaitForExit(10 * 60 * 1000))
			{
				p.Kill(entireProcessTree: true);
				Console.WriteLine("ERRO: o Godot passou de 10 minutos compondo -- abortado");
				return 0;
			}

			foreach (string l in saida.ToString().Split('\n'))
				if (l.StartsWith("[anim]", StringComparison.Ordinal)) Console.WriteLine("  " + l[6..].Trim());

			// nada saiu? entao a mensagem que importa esta no stderr
			if (!saida.ToString().Contains("[anim]", StringComparison.Ordinal) && erro.Length > 0)
				foreach (string l in erro.ToString().Split('\n').Take(6))
					if (l.Trim().Length > 0) Console.WriteLine("  [godot] " + l.Trim());
		}
		finally
		{
			if (File.Exists(script)) File.Delete(script);
		}

		int feitos = 0;
		foreach ((_, (string destino, _, _, _, _)) in porFolha)
			if (File.Exists(destino)) feitos++;

		if (feitos > 0) Importar(godot, raizDoProjeto, feitos);
		return feitos;
	}

	/// <summary>
	/// PEDE AO GODOT QUE IMPORTE OS PNG NOVOS.
	///
	/// ============================ SEM ISTO, NADA DISTO FUNCIONA ============================
	/// Um .png so vira `Texture2D` depois de importado -- o Godot escreve um `.png.import` ao lado
	/// e guarda o resultado em `.godot/imported`. O tileset gerado aqui aponta pros companheiros,
	/// e sem o import o motor responde "No loader found for resource" e a fonte inteira do atlas
	/// nao carrega. Ou seja: a animacao nao fica so parada, ela SOME.
	///
	/// E o pior modo de falha possivel, porque nada nesta ferramenta reclama: os 35 PNG estao no
	/// disco, o tileset esta certo, o relatorio diz "178 estados reempacotados". So o log do JOGO
	/// mostra o erro, e so quando alguem entra num planeta. Medido: 35 companheiros escritos e
	/// ZERO `.import` -- as 178 animacoes estavam todas mortas.
	///
	/// O caminho antigo era "abrir o editor uma vez", que nao e um passo: e uma coisa pra lembrar.
	/// =======================================================================================
	/// </summary>
	private static void Importar(string godot, string raizDoProjeto, int quantos)
	{
		Console.WriteLine($"  importando os {quantos} atlas no Godot...");
		try
		{
			var psi = new ProcessStartInfo(godot)
			{
				WorkingDirectory = raizDoProjeto,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			};
			psi.ArgumentList.Add("--headless");
			psi.ArgumentList.Add("--path");
			psi.ArgumentList.Add(raizDoProjeto);
			psi.ArgumentList.Add("--import");

			using Process? p = Process.Start(psi);
			if (p == null) { Console.WriteLine("  ERRO: nao consegui rodar o Godot pro import"); return; }

			// os dois fluxos drenados, pela mesma razao do `Compor` acima
			p.OutputDataReceived += (_, _) => { };
			p.ErrorDataReceived += (_, _) => { };
			p.BeginOutputReadLine();
			p.BeginErrorReadLine();

			if (!p.WaitForExit(10 * 60 * 1000))
			{
				p.Kill(entireProcessTree: true);
				Console.WriteLine("  ERRO: o import passou de 10 minutos -- abortado");
				return;
			}
		}
		catch (Exception e) { Console.WriteLine($"  ERRO no import: {e.Message}"); return; }

		// A CONFERENCIA E O QUE VALE. "Rodou sem erro" nao diz nada -- o que diz e o `.import`
		// existir ao lado de cada PNG.
		int semImport = 0;
		foreach (string png in Directory.GetFiles(raizDoProjeto, "*" + Sufixo + ".png", SearchOption.AllDirectories))
			if (!File.Exists(png + ".import")) semImport++;

		Console.WriteLine(semImport == 0
			? "  atlas importados: todos"
			: $"  AVISO: {semImport} atlas SEM .import -- as animacoes deles nao vao carregar");
	}

	/// <summary>Marca no nome do arquivo companheiro. Quem gera o nome e o <see cref="MapConverter"/>.</summary>
	public const string Sufixo = "__anim";

	/// <summary>
	/// O script que o Godot roda. Le cada folha de origem, recorta quadro por quadro e cola numa
	/// linha do companheiro.
	///
	/// `FORMAT_RGBA8` FORCADO na imagem nova: o `blit_rect` exige que origem e destino tenham o
	/// MESMO formato, e os .dmi vem em variantes (paleta, RGB). Converter a origem uma vez e mais
	/// barato e mais previsivel do que criar o destino no formato de cada uma.
	/// </summary>
	private static string Gd(Dictionary<string, (string Destino, int IconW, int IconH, int ColsOrigem, List<Tira> Tiras)> porFolha)
	{
		var sb = new StringBuilder();
		sb.Append("extends SceneTree\n\nfunc _init():\n");
		sb.Append("\tvar feitos := 0\n");

		// UM NOME POR FOLHA. Todas as folhas caem no MESMO escopo de `_init()`, e o GDScript recusa
		// redeclarar: "There is already a variable named src". Foi o erro que a primeira rodada
		// escondeu, porque o stderr estava sendo descartado.
		int n = 0;
		foreach ((string origem, (string destino, int iw, int ih, int cols, List<Tira> tiras) v) in porFolha)
		{
			string src = "src" + n, dst = "dst" + n;
			n++;
			int largura = 0;
			foreach (Tira t in v.tiras) largura = Math.Max(largura, t.Quadros.Length);

			sb.Append($"\tvar {src} = Image.load_from_file(\"").Append(Esc(origem)).Append("\")\n");
			sb.Append($"\tif {src} != null:\n");
			sb.Append($"\t\t{src}.convert(Image.FORMAT_RGBA8)\n");
			sb.Append($"\t\tvar {dst} = Image.create_empty({largura * v.iw}, {v.tiras.Count * v.ih}, false, Image.FORMAT_RGBA8)\n");

			foreach (Tira t in v.tiras)
				for (int q = 0; q < t.Quadros.Length; q++)
				{
					int sx = t.Quadros[q] % v.cols * v.iw;
					int sy = t.Quadros[q] / v.cols * v.ih;
					sb.Append($"\t\t{dst}.blit_rect({src}, Rect2i({sx}, {sy}, {v.iw}, {v.ih}), Vector2i({q * v.iw}, {t.Linha * v.ih}))\n");
				}

			sb.Append($"\t\tif {dst}.save_png(\"").Append(Esc(v.destino)).Append("\") == OK:\n");
			sb.Append("\t\t\tfeitos += 1\n");
		}

		sb.Append("\tprint(\"[anim] \", feitos, \" atlas de animacao compostos\")\n");
		sb.Append("\tquit()\n");
		return sb.ToString();
	}

	private static string Esc(string caminho) => caminho.Replace("\\", "/").Replace("\"", "\\\"");
}
