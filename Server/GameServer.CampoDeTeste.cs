using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// `--campoteste [N]` -- A BANCADA NASCE NO MEIO DE UM CAMPO ABERTO.
///
/// ============================ POR QUE ISTO EXISTE ============================
/// O berco de um personagem novo e o do BYOND (os `SpawnPoint` do manifesto), e o da Terra cai DENTRO
/// DO BANCO: uma sala de seis por dois tiles com uma porta. Quatro bancadas de ki nasceram quando o
/// berco era um descampado e passaram a reprovar por causa do PALCO, e nao do que medem:
///
///   * `--diagvariedade`: 24 tecnicas, 24 vezes "o tiro saiu e andou 4 tiles (achou=False)". O corpo
///     olhava pro sul com "vinte e dois tiles livres no mapa do servidor" -- e a dois tiles dele havia
///     a PORTA do banco, que e entidade e nao bit do mapa. Todo tiro morria nela;
///   * `--diagboca`: a praca de seis tiles achada colada no banco tinha os quatro BRACOS livres e a
///     quina noroeste de pedra. O corpo vira ANDANDO (uns 18 px por virada), saia do centro, e o tiro
///     pro oeste nascia de cara pra parede -- o `Disparar` o poe em cima do corpo e ele morre no muro;
///   * `--diagpose`: "achei rumo livre pro raio andar (0 dos 4)";
///   * `--diagki`: "andando honestamente... chego perto do boneco" -- com o boneco arremessado pra
///     fora do banco pela parede (ver o `ArmarAMetadeViva`, que conta o caso inteiro).
///
/// Nenhuma das quatro estava medindo ki. Foi a FOTO de cada uma que mostrou (o corpo dentro da sala, a
/// bola desenhada em cima do boneco) -- as mensagens diziam so que o tiro nao existia.
///
/// ============================ DUAS PORTAS PRO MESMO CAMPO ============================
///   * a FLAG (`--campoteste [N]`) vale pra TODA entrada no mundo, e e o que as tres bancadas de foto
///     querem: elas reusam o personagem entre rodadas e precisam dele no centro do campo toda vez;
///   * a `--kideponta` chama o <see cref="PorEmCampoAberto"/> ELA MESMA, e so no primeiro login. A
///     metade viva dela RELOGA pra medir o que o save devolve, e com a flag o corpo e REPOSTO no
///     campo tambem na volta (medido: um tile fora de onde deslogou) -- a conferencia "o corpo
///     acorda onde deslogou" passaria a medir esta funcao, e nao o save.
/// ====================================================================================
///
/// ============================ E O REMEDIO E O DAS OUTRAS BANCADAS DE PALCO ============================
/// `--voltateste` nasce na beirada, `--quebrarteste` encostado numa parede, `--aguateste` na margem de
/// um lago: a posicao e escrita NA ENTRADA, antes do primeiro pacote, e por isso o cliente ja nasce
/// la -- sem teleporte, sem correcao e sem a briga de "quem manda na posicao" que um `pl.Pos = ...`
/// escrito depois perde pro passo validado do cliente (foi exatamente o que desancorou a `--diagboca`).
///
/// O campo e o quadrado LIVRE mais perto do berco: `N` tiles pra cada lado do corpo, todos servindo de
/// chao (`ZoneCollision.ServeDeChao` -- nem parede, nem agua). Quadrado, e nao cruz: uma cruz promete
/// os quatro rumos e esquece as quinas, que e o defeito da praca acima.
/// ======================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Quantos tiles livres pra cada lado o campo da bancada tem (0 = a flag nao veio).</summary>
	private int _campoDeTeste;

	/// <summary>O padrao do `--campoteste` sem numero: cabe a praca de seis tiles da `--diagboca` com folga.</summary>
	private const int CampoPadrao = 8;

	/// <summary>Ate que distancia do berco (em tiles) se procura o campo. Alem disso e outro pedaco de mundo.</summary>
	private const int AlcanceDaBuscaDeCampo = 200;

	private void LerOCampoDeTeste(string[] args)
	{
		int i = Array.IndexOf(args, "--campoteste");
		if (i < 0) return;

		_campoDeTeste = i + 1 < args.Length && int.TryParse(args[i + 1], out int n) && n > 0 ? n : CampoPadrao;
		GD.Print($"[server] BANCADA: todo personagem nasce no meio de um campo aberto ({_campoDeTeste} tiles livres pra cada lado)");
	}

	/// <summary>
	/// POE O CORPO NO CENTRO DO CAMPO LIVRE MAIS PERTO. Chamado na entrada, antes de qualquer pacote.
	///
	/// A busca e por ANEIS a partir de onde ele nasceria (o primeiro achado e o mais perto que existe), e
	/// "o quadrado esta livre?" e respondido em tempo constante por uma tabela de somas -- sem ela, um
	/// campo de 23 tiles custaria 2209 leituras de mapa por candidato, e ha dezenas de milhares deles.
	/// </summary>
	/// <param name="n">Quantos tiles livres pra cada lado do corpo o campo precisa ter.</param>
	/// <returns>Falso quando nao ha campo desse tamanho por perto (ou a zona nao tem mapa): o corpo fica onde estava.</returns>
	private bool PorEmCampoAberto(ServerPlayer pl, int n)
	{
		if (MapaDaZonaOuCatalogo(pl.Zone) is not { } mapa)
		{
			GD.PushWarning("[server] BANCADA: campo aberto pedido numa zona sem mapa -- o corpo fica no berco");
			return false;
		}

		int w = mapa.Width, h = mapa.Height;

		// A TABELA DE SOMAS: `ruim[x, y]` = quantas celulas que NAO servem de chao ha em [0,x) x [0,y).
		var ruim = new int[(w + 1) * (h + 1)];
		for (int y = 0; y < h; y++)
		{
			int naLinha = 0;
			for (int x = 0; x < w; x++)
			{
				if (!mapa.ServeDeChao(x, y)) naLinha++;
				ruim[(y + 1) * (w + 1) + x + 1] = ruim[y * (w + 1) + x + 1] + naLinha;
			}
		}

		bool Livre(int cx, int cy)
		{
			int x0 = cx - n, y0 = cy - n, x1 = cx + n + 1, y1 = cy + n + 1;
			if (x0 < 0 || y0 < 0 || x1 > w || y1 > h) return false;
			return ruim[y1 * (w + 1) + x1] - ruim[y0 * (w + 1) + x1] - ruim[y1 * (w + 1) + x0] + ruim[y0 * (w + 1) + x0] == 0;
		}

		const int T = ZoneCollision.TileSize;
		int bx = (int)MathF.Floor(pl.Pos.X / T), by = (int)MathF.Floor(pl.Pos.Y / T);

		for (int r = 0; r <= AlcanceDaBuscaDeCampo; r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
				{
					// so a CASCA do quadrado de raio r: o miolo ja foi visto nas voltas anteriores
					if (r > 0 && Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
					if (!Livre(bx + dx, by + dy)) continue;

					pl.Pos = mapa.CentroDaCelula(bx + dx, by + dy);
					pl.Moving = false;
					GD.Print($"[server] BANCADA: {pl.Name} nasce no campo de ({bx + dx},{by + dy}), "
						   + $"a {Math.Max(Math.Abs(dx), Math.Abs(dy))} tiles do berco");
					return true;
				}

		GD.PushWarning($"[server] BANCADA: nenhum campo de {n} tiles livres pra cada lado a {AlcanceDaBuscaDeCampo} "
					 + "tiles do berco -- o corpo fica onde nasceu");
		return false;
	}
}
