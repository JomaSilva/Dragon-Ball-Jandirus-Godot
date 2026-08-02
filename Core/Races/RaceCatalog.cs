using System.Text.Json;

namespace Jandirus.Core.Races;

/// <summary>
/// Carrega os prototipos raciais do `races.json` que o Tools/AssetPipeline extraiu do DM.
///
/// Usa System.Text.Json (que e da BCL do net8.0, entao o Core continua sem dependencia
/// externa). Cliente e servidor carregam o MESMO arquivo: um personagem gerado numa ponta
/// e reproduzivel na outra, que e o que a validacao de "cliente calcula, servidor valida"
/// precisa quando a criacao de personagem entrar na conta.
/// </summary>
public sealed class RaceCatalog
{
	private readonly Dictionary<string, RaceProto> _protos = new(StringComparer.OrdinalIgnoreCase);

	public IReadOnlyDictionary<string, RaceProto> Protos => _protos;
	public int Count => _protos.Count;

	public RaceProto? Get(string nome) => _protos.GetValueOrDefault(nome);

	public static RaceCatalog Parse(string json)
	{
		var cat = new RaceCatalog();
		using JsonDocument doc = JsonDocument.Parse(json);

		foreach (JsonElement e in doc.RootElement.EnumerateArray())
		{
			var p = new RaceProto { Name = e.GetProperty("nome").GetString() ?? "" };
			if (p.Name.Length == 0) continue;

			LerMapa(e, "stats", p.Stats);
			LerMapa(e, "misc", p.Misc);

			if (e.TryGetProperty("classes", out JsonElement classes))
				foreach (JsonProperty cls in classes.EnumerateObject())
				{
					var d = new Dictionary<string, double>(StringComparer.Ordinal);
					foreach (JsonProperty kv in cls.Value.EnumerateObject())
						d[kv.Name] = kv.Value.GetDouble();
					p.ClassStats[cls.Name] = d;
				}

			if (e.TryGetProperty("spread", out JsonElement spread))
				foreach (JsonElement par in spread.EnumerateArray())
				{
					if (par.GetArrayLength() < 2) continue;
					p.ClassSpread.Add((par[0].GetString() ?? "None", par[1].GetInt32()));
				}

			cat._protos[p.Name] = p;
		}

		return cat;
	}

	private static void LerMapa(JsonElement pai, string chave, Dictionary<string, double> destino)
	{
		if (!pai.TryGetProperty(chave, out JsonElement m)) return;
		foreach (JsonProperty kv in m.EnumerateObject()) destino[kv.Name] = kv.Value.GetDouble();
	}

	/// <summary>
	/// Cria um personagem de raca PURA: sorteia a classe pelo spread do proto e devolve o
	/// genoma pronto. Equivale ao caminho de menu do BYOND (peso 100 = neutro).
	/// </summary>
	public Genome NewPureCharacter(string raca, Random rng, string? classeForcada = null)
	{
		Genome g = Genome.Pure(raca);
		RaceProto? proto = Get(raca);
		g.Class = classeForcada ?? (proto != null ? proto.RollClass(rng) : "None");
		return g;
	}
}
