namespace Jandirus.Core.Torneio;

/// <summary>
/// Quem esta na chave. A <see cref="Chave"/> e a IDENTIDADE estavel: a assinatura (conta/slot)
/// de um jogador -- que sobrevive a desconexao e a troca de id de corpo -- ou `npc:N` pra quem foi
/// gerado na hora. O corpo de agora e coisa do servidor, que o acha pela chave a cada tique.
/// </summary>
public sealed class Competidor
{
	public string Chave = "";
	public string Nome = "";
	public bool Npc;
}

/// <summary>Uma luta da chave. `B` vazio = passa sem lutar (chave impar, o "bye" do `trn_run`).</summary>
public sealed class Luta
{
	public string A = "";
	public string B = "";
	public string Vencedor = "";
	public string Como = "";
	public bool Acabou => Vencedor.Length > 0;
	public string Perdedor => !Acabou || B.Length == 0 ? "" : Vencedor == A ? B : A;
}

public sealed class Rodada
{
	public string Nome = "";
	public List<Luta> Lutas = [];
	public bool Acabou => Lutas.TrueForAll(l => l.Acabou);
}

/// <summary>
/// A CHAVE DO TORNEIO -- eliminacao simples, como o `trn_run` (`Tournament.dm:398-470`), com o que o
/// dono pediu por cima: 32 vagas (16-avos, oitavas, quartas, semi), a DISPUTA DO 3o LUGAR antes da
/// final (o DM premiava os dois semifinalistas sem luta) e a ordem de lutas fixa.
///
/// E Core puro e serializavel por campos: o servidor a guarda, avanca com <see cref="Registrar"/> e
/// le <see cref="LutaAtual"/>. Nenhum corpo, nenhum relogio -- so nomes e resultados. A chave impar
/// (que so acontece se alguem sumir ANTES do sorteio, ja que as vagas sao sempre preenchidas com
/// NPC) da um "passa sem lutar" ao ultimo, como o `alive.len % 2` do original.
/// </summary>
public sealed class Chave
{
	public List<Competidor> Competidores = [];
	public List<Rodada> Rodadas = [];
	public int RodadaAtual;
	public string Campeao = "", Vice = "", Terceiro = "", Quarto = "";

	public const string NomeDoTerceiroLugar = "disputa do 3o lugar";
	public const string NomeDaFinal = "final";

	public bool Acabou => Campeao.Length > 0;

	/// <summary>A rodada em curso, ou nula quando a chave acabou.</summary>
	public Rodada? RodadaDeAgora => RodadaAtual < Rodadas.Count ? Rodadas[RodadaAtual] : null;

	/// <summary>A proxima luta sem vencedor, ou nula quando a chave acabou.</summary>
	public Luta? LutaAtual => RodadaDeAgora?.Lutas.Find(l => !l.Acabou);

	/// <summary>Quantas lutas ja tem resultado, contando as que passaram sem lutar.</summary>
	public int LutasDecididas => Rodadas.Sum(r => r.Lutas.Count(l => l.Acabou));

	public static string NomeDaRodada(int lutadores) => lutadores switch
	{
		32 => "16-avos de final",
		16 => "oitavas de final",
		8 => "quartas de final",
		4 => "semifinal",
		2 => NomeDaFinal,
		_ => $"rodada de {lutadores}",
	};

	/// <summary>
	/// MONTA A CHAVE: embaralha os competidores (o `shuffle(bracket)` do DM) e arma a primeira
	/// rodada. O `Random` vem de fora pra bancada repetir o sorteio.
	/// </summary>
	public static Chave Montar(IReadOnlyList<Competidor> inscritos, Random rng)
	{
		var c = new Chave { Competidores = [.. inscritos] };
		for (int i = c.Competidores.Count - 1; i > 0; i--)
		{
			int j = rng.Next(i + 1);
			(c.Competidores[i], c.Competidores[j]) = (c.Competidores[j], c.Competidores[i]);
		}
		c.Rodadas.Add(Emparelhar([.. c.Competidores.Select(x => x.Chave)], NomeDaRodada(c.Competidores.Count)));
		return c;
	}

	private static Rodada Emparelhar(List<string> vivos, string nome)
	{
		var r = new Rodada { Nome = nome };
		for (int i = 0; i + 1 < vivos.Count; i += 2) r.Lutas.Add(new Luta { A = vivos[i], B = vivos[i + 1] });
		// NUMERO IMPAR: o ultimo passa direto (`bye`), com o resultado ja escrito.
		if (vivos.Count % 2 == 1)
			r.Lutas.Add(new Luta { A = vivos[^1], B = "", Vencedor = vivos[^1], Como = "sem adversario (chave incompleta)" });
		return r;
	}

	/// <summary>
	/// REGISTRA O VENCEDOR DA LUTA ATUAL e, se a rodada fechou, arma a seguinte. Depois da
	/// semifinal vem a disputa do 3o lugar (os dois perdedores) e so entao a final (os dois
	/// vencedores); a final coroa campeao e vice, e a do 3o lugar da o terceiro e o quarto.
	/// </summary>
	public void Registrar(string vencedor, string como)
	{
		Luta? l = LutaAtual;
		if (l == null) throw new InvalidOperationException("a chave ja acabou");
		if (vencedor != l.A && vencedor != l.B) throw new ArgumentException($"'{vencedor}' nao esta na luta {l.A} x {l.B}");
		l.Vencedor = vencedor;
		l.Como = como;
		Rodada r = Rodadas[RodadaAtual];
		if (!r.Acabou) return;

		if (r.Nome == NomeDaFinal)
		{
			Campeao = l.Vencedor;
			Vice = l.Perdedor;
			RodadaAtual++;
			return;
		}
		if (r.Nome == NomeDoTerceiroLugar)
		{
			Terceiro = l.Vencedor;
			Quarto = l.Perdedor;
			RodadaAtual++;
			return;
		}

		var vencedores = new List<string>();
		var perdedores = new List<string>();
		foreach (Luta x in r.Lutas)
		{
			vencedores.Add(x.Vencedor);
			if (x.Perdedor.Length > 0) perdedores.Add(x.Perdedor);
		}
		if (vencedores.Count == 1)
		{
			// Uma chave de um so (todo mundo sumiu): ele e o campeao por ausencia.
			Campeao = vencedores[0];
			RodadaAtual++;
			return;
		}
		if (vencedores.Count == 2 && perdedores.Count == 2)
		{
			Rodadas.Add(new Rodada { Nome = NomeDoTerceiroLugar, Lutas = [new Luta { A = perdedores[0], B = perdedores[1] }] });
			Rodadas.Add(new Rodada { Nome = NomeDaFinal, Lutas = [new Luta { A = vencedores[0], B = vencedores[1] }] });
		}
		else Rodadas.Add(Emparelhar(vencedores, NomeDaRodada(vencedores.Count)));
		RodadaAtual++;
	}

	/// <summary>1 = campeao, 2 = vice, 3 = terceiro, 0 = nenhum destes.</summary>
	public int Colocacao(string chave) =>
		chave.Length == 0 ? 0 : chave == Campeao ? 1 : chave == Vice ? 2 : chave == Terceiro ? 3 : 0;

	/// <summary>Quem ainda tem luta pela frente (ou esta lutando agora).</summary>
	public IEnumerable<string> AindaNaChave()
	{
		if (Acabou) yield break;
		var vivos = new HashSet<string>();
		foreach (Rodada r in Rodadas.Skip(RodadaAtual))
			foreach (Luta l in r.Lutas)
			{
				if (l.Acabou) { vivos.Add(l.Vencedor); continue; }
				vivos.Add(l.A);
				if (l.B.Length > 0) vivos.Add(l.B);
			}
		foreach (string v in vivos) yield return v;
	}

	public bool Eliminado(string chave) => Competidores.Exists(c => c.Chave == chave) && !AindaNaChave().Contains(chave);

	public Competidor? Quem(string chave) => Competidores.Find(c => c.Chave == chave);

	public string NomeDe(string chave) => Quem(chave)?.Nome ?? chave;

	/// <summary>A chave em uma linha por rodada -- pro anuncio e pro `trn_status`.</summary>
	public string Resumo()
	{
		var sb = new System.Text.StringBuilder();
		foreach (Rodada r in Rodadas)
		{
			sb.Append(r.Nome).Append(": ");
			sb.Append(string.Join(", ", r.Lutas.Select(l =>
				l.B.Length == 0 ? $"{NomeDe(l.A)} (passa)"
				: l.Acabou ? $"{NomeDe(l.Vencedor)} venceu {NomeDe(l.Perdedor)}"
				: $"{NomeDe(l.A)} x {NomeDe(l.B)}")));
			sb.Append(" | ");
		}
		return sb.ToString().TrimEnd(' ', '|');
	}
}
