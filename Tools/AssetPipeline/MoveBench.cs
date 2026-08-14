using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// BANCO DE PROVA DO MOVIMENTO. Anda de verdade sobre o mapa de colisao de uma zona e
/// mostra o que acontece ao encostar num muro.
///
/// Existe porque "atravessa parede" e o tipo de defeito que so aparece jogando -- e jogando
/// ele se disfarca de outra coisa: o cliente passava pela parede, o servidor recusava e
/// mandava correcao, o cliente empurrava de novo, e o sintoma que chegava era "o personagem
/// treme". Aqui o problema aparece como numero.
/// </summary>
public static class MoveBench
{
	public static void Run(string caminhoCol)
	{
		ZoneCollision? mapa = ZoneCollision.Load(File.ReadAllBytes(caminhoCol));
		if (mapa == null) { Console.Error.WriteLine("colisao invalida"); return; }

		int bloqueadas = 0;
		for (int y = 0; y < mapa.Height; y++)
			for (int x = 0; x < mapa.Width; x++)
				if (mapa.BlockedCell(x, y)) bloqueadas++;

		Console.WriteLine($"{Path.GetFileName(caminhoCol)}: {mapa.Width}x{mapa.Height} | " +
						  $"bloqueadas {bloqueadas} ({100.0 * bloqueadas / (mapa.Width * mapa.Height):0.0}%)\n");

		// acha uma parede com chao livre a oeste dela: da pra andar ate encostar
		(int px, int py) = AcharParedeComAcesso(mapa);
		if (px < 0) { Console.WriteLine("nao achei parede com acesso livre nesta zona"); return; }
		Console.WriteLine($"parede escolhida: tile ({px},{py})\n");

		const float dt = 1f / 60;
		const float vel = 1f;

		// Comeca 4 tiles a oeste da parede e anda pra LESTE ate parar.
		var pos = new Vec2((px - 4) * 32 + 16, py * 32 + 16 - MoveRules.FeetOffsetY);
		Console.WriteLine("andando de frente contra a parede (leste):");
		Andar(mapa, ref pos, new Vec2(1, 0), dt, vel, 240);

		// DESLIZE, num muro sintetico: uma parede vertical de verdade, sem depender de onde
		// o mapa real coloca as coisas. Andando em diagonal contra ela, o eixo bloqueado zera
		// e o outro continua -- e o que faz correr rente a um muro nao travar.
		ZoneCollision muro = MuroVertical(40, 40, 20);
		var p2 = new Vec2(18 * 32 + 16, 5 * 32 + 16 - MoveRules.FeetOffsetY);
		Console.WriteLine("\nmuro sintetico, andando na diagonal contra ele (sudeste):");
		Andar(muro, ref p2, new Vec2(1, 1), dt, vel, 240);

		Console.WriteLine("\n  De frente o personagem PARA (deslocamento vai a zero).");
		Console.WriteLine("  Na diagonal ele DESLIZA: X zera no muro e Y continua andando.");

		Agua(caminhoCol, mapa);
		Nadar();
		Acordo(mapa);
	}

	/// <summary>
	/// A AGUA DESTA ZONA -- so o CENSO, e so isto.
	///
	/// ============================ AS PROVAS DA CLASSE MUDARAM DE ENDERECO ============================
	/// Elas moravam aqui (seis linhas sinteticas: a pe para, nadando nao, arremessado nao, nao e
	/// parede no `.col`, nao se nasce nela) e viraram um subconjunto estrito das familias 1, 2 e 3 do
	/// `agua-prova` (`AguaBench.cs`), que fazem as mesmas perguntas com contra-exemplo ao lado e com
	/// defeito injetado provando que sabem reprovar.
	///
	/// Ficaram no lugar novo e sairam daqui porque a mesma verdade em dois arquivos tem dois donos e
	/// envelhece em um so: o dia em que a regra mudar, uma das copias fica verde afirmando o passado.
	///
	/// O que sobra aqui e o que so esta bancada sabe -- ela recebe UMA zona por argumento, entao ela
	/// e o lugar de perguntar "e nesta zona, quanta agua ha?".
	/// ==============================================================================================
	/// </summary>
	private static void Agua(string caminhoCol, ZoneCollision zona)
	{
		Console.WriteLine("\n=== a agua desta zona (as regras da classe estao em `agua-prova`) ===");

		string arq = Path.ChangeExtension(caminhoCol, ".agua");
		if (!File.Exists(arq)) { Console.WriteLine($"  ({Path.GetFileName(arq)} nao existe: zona seca)"); return; }

		if (!zona.CarregarAgua(File.ReadAllBytes(arq)))
		{
			Console.WriteLine($"  FALHA: {Path.GetFileName(arq)} existe e o CarregarAgua RECUSOU");
			return;
		}

		int agua = 0, aguaEParede = 0;
		for (int y = 0; y < zona.Height; y++)
			for (int x = 0; x < zona.Width; x++)
				if (zona.EhAgua(x, y)) { agua++; if (zona.BlockedCell(x, y)) aguaEParede++; }

		Console.WriteLine($"  nesta zona: {agua} celulas de agua "
						  + $"({100.0 * agua / (zona.Width * zona.Height):0.0}% do mapa)"
						  + (aguaEParede > 0 ? $", {aguaEParede} delas tambem parede (ponte/cerca por cima)" : ""));
	}

	/// <summary>
	/// O NADO -- as regras do `swim` medidas pelas funcoes que o jogo chama.
	///
	/// ============================ O QUE ESTAS PROVAS PEGAM QUE JOGAR NAO PEGA ============================
	/// Duas coisas, e as duas sao caras de descobrir na tela:
	///
	///   1. **A JANELA DA DECOLAGEM.** Entre soltar o chao e passar de `Voo.AlturaQueAtravessa` o
	///      corpo ainda consulta o mapa. Se o modo nesses ~170 ms fosse `APe`, decolar da beira do
	///      lago daria uma parede invisivel que some sozinha -- exatamente o tipo de defeito que o
	///      jogador descreve como "as vezes trava" e que ninguem consegue reproduzir.
	///   2. **O TETO DA MAESTRIA.** No original o mesmo `if` fecha o ganho E o custo: passando de
	///      0,99, nadar fica de graca. Parece bug, e nao e -- e escrever a prova e o jeito de o
	///      proximo leitor saber que isso foi decidido e nao esquecido.
	///
	/// TUDO AQUI E FUNCAO PURA, sem servidor e sem Godot: sao as MESMAS funcoes que o `TickDoNado` e
	/// o `RecalcularVelocidade` chamam.
	/// =================================================================================================
	/// </summary>
	private static void Nadar()
	{
		// SO AS FORMULAS. Quem passa por onde -- o modo, a agua que para so quem esta a pe, a parede
		// que continua parando quem nada -- e o `agua-prova` (`AguaBench.cs`, familias 1 e 2), com
		// contra-exemplo e defeito injetado. Aqui fica o que aquela bancada nao mede: os NUMEROS do
		// `swim` do DM, que sao funcao pura e nao dependem de mapa nenhum.
		Console.WriteLine("\n=== o nado: as formulas do `swim` do DM ===");

		// ---------------------------------------------------------------- o custo
		// `Ki -= max(1 + (MaxKi*0.001*(KiMod/(100*swimmastery))), 1)` por tique, 5 tiques/s.
		const double maxKi = 1000, kiMod = 1;
		double porTique = Nado.CustoPorTiqueDoDm(maxKi, kiMod, Nado.MaestriaInicial);
		double literal = Math.Max(1 + maxKi * 0.001 * (kiMod / (100 * Nado.MaestriaInicial)), 1);
		Prova($"custo por tique bate com a linha do DM ({porTique:0.###} = {literal:0.###})",
			  Math.Abs(porTique - literal) < 1e-9);
		Prova("5 tiques do DM por segundo (o `sleep(2)` do `Stats()`, 0,2 s -- NAO 1/12)",
			  Math.Abs(Nado.TiquesDoDmPorSegundo - 5) < 1e-9);

		// A maestria esta no DENOMINADOR: nadar fica MAIS BARATO com o tempo.
		double caro = Nado.CustoPorSegundo(maxKi, kiMod, Nado.MaestriaInicial);
		double barato = Nado.CustoPorSegundo(maxKi, kiMod, 0.5);
		Prova($"a maestria BARATEIA o nado ({caro:0.#}/s no comeco, {barato:0.#}/s na metade)",
			  barato < caro);

		// Save antigo chega com 0, e 0 no denominador seria divisao por zero.
		Prova("maestria ZERO (save antigo) cai no valor de nascenca em vez de estourar",
			  Math.Abs(Nado.CustoPorTiqueDoDm(maxKi, kiMod, 0) - porTique) < 1e-9);

		// ---------------------------------------------------------------- o teto que zera o custo
		double m = Nado.MaestriaInicial;
		double segundos = 0;
		while (m <= Nado.MaestriaMaxima && segundos < 600) { m = Nado.SubirMaestria(m, 1); segundos += 1; }
		Prova($"depois de ~{segundos:0} s de nado a maestria passa de 0,99 e o custo ZERA "
			  + "(o mesmo `if` do DM fecha o ganho e a cobranca)",
			  Nado.CustoPorTiqueDoDm(maxKi, kiMod, m) == 0);

		// ---------------------------------------------------------------- a velocidade
		// `mobTime -= 0.3` tira a parcela FIXA do passo: quem e lento perde muito mais.
		double lento = Nado.FatorDePasso(2), rapido = Nado.FatorDePasso(10);
		Prova($"nadar e mais devagar que andar (Epspeed 2: {lento:0.00}x)", lento is > 0 and < 1);
		Prova($"e quem e RAPIDO perde menos (Epspeed 10: {rapido:0.00}x)", rapido > lento);
	}

	private static void Prova(string oQue, bool passou) =>
		Console.WriteLine($"  [{(passou ? "ok  " : "FALHA")}] {oQue}");

	/// <summary>
	/// AS DUAS PONTAS CONCORDAM? Anda como o cliente anda e pergunta ao validador do servidor
	/// se cada passo passa. Toda reprovacao aqui e uma correcao que o jogador sentiria como o
	/// personagem sendo puxado de volta -- em jogo honesto o numero tem que ser ZERO.
	/// </summary>
	private static void Acordo(ZoneCollision mapa)
	{
		Console.WriteLine("\n== ACORDO CLIENTE x SERVIDOR ==\n");

		var rng = new Random(4242);
		const float dtCliente = 1f / 60;   // o cliente anda por quadro de render
		const int porPacote = 2;           // e manda a cada ~2 quadros (30 Hz)
		int recusas = 0, passos = 0;

		for (int tentativa = 0; tentativa < 200; tentativa++)
		{
			// larga em algum lugar livre e caminha em direcao aleatoria, trocando de rumo
			float orcamento = 0f;
			Vec2 pos = LugarLivre(mapa, rng);
			var dir = new Vec2(rng.Next(-1, 2), rng.Next(-1, 2));

			for (int t = 0; t < 300; t++)
			{
				if (t % 40 == 0) dir = new Vec2(rng.Next(-1, 2), rng.Next(-1, 2));

				Vec2 doPacote = pos;
				for (int f = 0; f < porPacote; f++)
					pos = MoveRules.Advance(pos, dir, dtCliente, 1f, mapa, out _);

				// o servidor mede o tempo ENTRE PACOTES; uso o mesmo intervalo do cliente
				passos++;
				// O ORCAMENTO E POR JOGADOR e persiste entre pacotes -- e justamente o acumulo
				// que absorve o jitter. Passar um zero novo a cada volta mediria a regra ANTIGA.
				if (!MoveRules.ValidateStep(doPacote, pos, dtCliente * porPacote, 1f, mapa, ref orcamento, out Vec2 corrigido))
				{
					recusas++;
					pos = corrigido;
				}
			}
		}

		Console.WriteLine($"  passos conferidos : {passos}");
		Console.WriteLine($"  recusados         : {recusas}");
		Console.WriteLine(recusas == 0
			? "  OK -- nenhuma correcao em jogo honesto."
			: "  ATENCAO -- o servidor esta puxando o cliente de volta: e isso que vira tremor na tela.");
	}

	private static Vec2 LugarLivre(ZoneCollision m, Random rng)
	{
		for (int i = 0; i < 500; i++)
		{
			var p = new Vec2(rng.Next(2, m.Width - 2) * 32 + 16, rng.Next(2, m.Height - 2) * 32 + 16);
			if (!MoveRules.Occupied(m, p)) return p;
		}
		return new Vec2(m.Width * 16, m.Height * 16);
	}

	/// <summary>Mapa de teste: uma coluna de parede em <paramref name="colX"/>.</summary>
	private static ZoneCollision MuroVertical(int w, int h, int colX)
	{
		var bytes = new byte[8 + (w * h + 7) / 8];
		bytes[0] = (byte)'J'; bytes[1] = (byte)'C'; bytes[2] = (byte)'O'; bytes[3] = (byte)'L';
		bytes[4] = (byte)(w & 0xFF); bytes[5] = (byte)(w >> 8);
		bytes[6] = (byte)(h & 0xFF); bytes[7] = (byte)(h >> 8);
		for (int y = 0; y < h; y++)
		{
			int i = y * w + colX;
			bytes[8 + (i >> 3)] |= (byte)(1 << (i & 7));
		}
		return ZoneCollision.Load(bytes)!;
	}

	private static void Andar(ZoneCollision mapa, ref Vec2 pos, Vec2 dir, float dt, float vel, int passos)
	{
		Vec2 inicio = pos;
		int primeiroBloqueio = -1;
		for (int i = 0; i < passos; i++)
		{
			Vec2 antes = pos;
			pos = MoveRules.Advance(pos, dir, dt, vel, mapa, out bool bloqueado);
			if (bloqueado && primeiroBloqueio < 0) primeiroBloqueio = i;
			if (i == passos - 1)
			{
				Vec2 d = pos - antes;
				Console.WriteLine($"  passo {i,3}: deslocamento do ultimo quadro = ({d.X:0.00}, {d.Y:0.00})");
			}
		}
		Vec2 total = pos - inicio;
		Console.WriteLine($"  encostou no passo {primeiroBloqueio} | andou no total ({total.X:0.0}, {total.Y:0.0}) px");
		Console.WriteLine($"  dentro de parede no fim? {(MoveRules.Occupied(mapa, pos) ? "SIM (BUG)" : "nao")}");
	}

	/// <summary>Uma parede com pelo menos 5 tiles livres a oeste e livre acima/abaixo.</summary>
	private static (int, int) AcharParedeComAcesso(ZoneCollision m)
	{
		for (int y = 2; y < m.Height - 2; y++)
			for (int x = 6; x < m.Width - 2; x++)
			{
				if (!m.BlockedCell(x, y)) continue;
				bool livre = true;
				for (int k = 1; k <= 5 && livre; k++)
					if (m.BlockedCell(x - k, y) || m.BlockedCell(x - k, y + 1)) livre = false;
				if (livre && !m.BlockedCell(x, y + 1)) return (x, y);
			}
		return (-1, -1);
	}
}
