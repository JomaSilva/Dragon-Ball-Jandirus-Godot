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

		Acordo(mapa);
	}

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
