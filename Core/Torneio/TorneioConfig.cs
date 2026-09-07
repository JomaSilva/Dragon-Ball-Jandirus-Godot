using System.Text.Json;
using Jandirus.Core.World;

namespace Jandirus.Core.Torneio;

/// <summary>Os dois torneios do original: o da Terra (vivos) e o do Outro Mundo (mortos).</summary>
public enum TipoDeTorneio : byte
{
	Terra = 1,
	Alem = 2,
}

/// <summary>Onde um torneio acontece: a zona e o retangulo da arena (`TRN_ARENA_*` / `TRNH_ARENA_*`).</summary>
public sealed class PalcoDeTorneio
{
	public string Zona = "";
	public Arena Arena;
}

/// <summary>
/// AS MANIVELAS DO TORNEIO -- `Assets/Data/torneio.json`, lido no boot. Cada numero do
/// `Tournament.dm` esta aqui com o define de origem ao lado; o que nao existe la (o prazo do
/// convite, o W.O. por desconexao, a agenda em dias, o 3o lugar disputado) e marcado como
/// decisao do port.
///
/// ============================ AS COORDENADAS DA ARENA ============================
/// O DM escreve `TRN_ARENA_X1 220, Y1 11, X2 276, Y2 68` em coordenadas BYOND (x e y a partir
/// de 1, y crescendo pra CIMA). O conversor de mapas poe a linha de y mais alto em cima, entao
/// a celula do port e `(x - 1, altura - y)`: a arena da Terra vira (219, 432)-(275, 489) num mapa
/// de 500, e a celeste (`TRNH 117..126 x 140..148`, z6 = Afterlife) vira (116, 352)-(125, 360).
/// A bancada confere que os dois retangulos sao chao livre no `.col` de verdade.
/// ==================================================================================
/// </summary>
public sealed class TorneioConfig
{
	/// <summary>Vagas da chave. O DM tinha 16 (`TRN_BRACKET`, oitavas); o dono pediu 32 (16-avos).</summary>
	public int Vagas = 32;

	/// <summary>`TRN_SIGNUP_TICKS 1500` = 150 s de inscricoes abertas.</summary>
	public double InscricaoSegundos = 150;

	/// <summary>Quanto tempo o convite na tela espera resposta (port; o `alert()` do DM nao vencia).</summary>
	public double ConviteSegundos = 90;

	/// <summary>`TRN_MATCH_MAX 2400` = 240 s: passou, decide por % de vida.</summary>
	public double LutaSegundosMax = 240;

	/// <summary>`TRN_COUNTDOWN 5`.</summary>
	public double ContagemSegundos = 5;

	/// <summary>`TRN_BREAK_TICKS 100` = 10 s entre lutas.</summary>
	public double IntervaloSegundos = 10;

	/// <summary>W.O. por desconexao: quanto tempo o lutador sumido tem pra voltar (decisao do port).</summary>
	public double WoSegundos = 30;

	/// <summary>`TRN_PRIZE_1/2/3`: campeao, vice e terceiro.</summary>
	public double Premio1 = 1_000_000, Premio2 = 500_000, Premio3 = 250_000;

	/// <summary>`TRN_NPC_BP_MIN/MAX`: o BP do NPC e a media dos jogadores x rand(60..140)%.</summary>
	public int NpcBpMinPct = 60, NpcBpMaxPct = 140;

	/// <summary>`TRN_NPC_BP_FLOOR 500`.</summary>
	public double NpcBpPiso = 500;

	/// <summary>O molde de NPC que preenche as vagas (npcs.json).</summary>
	public string MoldeDoNpc = "lutador_de_torneio";

	public PalcoDeTorneio Terra = new() { Zona = "Earth", Arena = new Arena(219, 432, 275, 489) };
	public PalcoDeTorneio Alem = new() { Zona = "Afterlife", Arena = new Arena(116, 352, 125, 360) };

	/// <summary>A agenda: de quantos em quantos dias (tempo real) a Terra tem torneio, e quando e o primeiro.</summary>
	public double IntervaloDaTerraDias = 30;
	public double PrimeiroDaTerraDias = 1;

	/// <summary>O do Outro Mundo acontece N dias depois de cada torneio da Terra.</summary>
	public double AlemDepoisDaTerraDias = 15;

	/// <summary>O campeao do Outro Mundo volta a vida? Decisao do port: nao -- so o dinheiro.</summary>
	public bool CampeaoDoAlemRevive;

	public PalcoDeTorneio Palco(TipoDeTorneio t) => t == TipoDeTorneio.Alem ? Alem : Terra;

	public double Premio(int lugar) => lugar switch { 1 => Premio1, 2 => Premio2, 3 => Premio3, _ => 0 };

	public static TorneioConfig Ler(string json)
	{
		var c = new TorneioConfig();
		using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
		JsonElement r = doc.RootElement;
		c.Vagas = Inteiro(r, "vagas", c.Vagas);
		c.InscricaoSegundos = Num(r, "inscricaoSegundos", c.InscricaoSegundos);
		c.ConviteSegundos = Num(r, "conviteSegundos", c.ConviteSegundos);
		c.LutaSegundosMax = Num(r, "lutaSegundosMax", c.LutaSegundosMax);
		c.ContagemSegundos = Num(r, "contagemSegundos", c.ContagemSegundos);
		c.IntervaloSegundos = Num(r, "intervaloSegundos", c.IntervaloSegundos);
		c.WoSegundos = Num(r, "woSegundos", c.WoSegundos);
		if (r.TryGetProperty("premios", out JsonElement p) && p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 3)
		{
			c.Premio1 = p[0].GetDouble();
			c.Premio2 = p[1].GetDouble();
			c.Premio3 = p[2].GetDouble();
		}
		if (r.TryGetProperty("npc", out JsonElement n) && n.ValueKind == JsonValueKind.Object)
		{
			c.NpcBpMinPct = Inteiro(n, "bpMinPct", c.NpcBpMinPct);
			c.NpcBpMaxPct = Inteiro(n, "bpMaxPct", c.NpcBpMaxPct);
			c.NpcBpPiso = Num(n, "bpPiso", c.NpcBpPiso);
			c.MoldeDoNpc = Txt(n, "molde", c.MoldeDoNpc);
		}
		if (r.TryGetProperty("terra", out JsonElement t) && t.ValueKind == JsonValueKind.Object)
		{
			LerPalco(t, c.Terra);
			c.IntervaloDaTerraDias = Num(t, "intervaloDias", c.IntervaloDaTerraDias);
			c.PrimeiroDaTerraDias = Num(t, "primeiroEmDias", c.PrimeiroDaTerraDias);
		}
		if (r.TryGetProperty("alem", out JsonElement a) && a.ValueKind == JsonValueKind.Object)
		{
			LerPalco(a, c.Alem);
			c.AlemDepoisDaTerraDias = Num(a, "diasDepoisDaTerra", c.AlemDepoisDaTerraDias);
			c.CampeaoDoAlemRevive = Bit(a, "campeaoRevive", c.CampeaoDoAlemRevive);
		}
		return c;
	}

	private static void LerPalco(JsonElement e, PalcoDeTorneio palco)
	{
		palco.Zona = Txt(e, "zona", palco.Zona);
		if (e.TryGetProperty("arena", out JsonElement a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() == 4)
			palco.Arena = new Arena(a[0].GetInt32(), a[1].GetInt32(), a[2].GetInt32(), a[3].GetInt32());
	}

	public List<string> Problemas()
	{
		var p = new List<string>();
		if (Vagas < 2 || (Vagas & (Vagas - 1)) != 0) p.Add($"torneio.vagas = {Vagas}: precisa ser potencia de 2 (16, 32...)");
		if (InscricaoSegundos <= 0) p.Add("torneio.inscricaoSegundos precisa ser > 0");
		if (ConviteSegundos <= 0) p.Add("torneio.conviteSegundos precisa ser > 0");
		if (LutaSegundosMax <= 0) p.Add("torneio.lutaSegundosMax precisa ser > 0");
		if (ContagemSegundos < 0) p.Add("torneio.contagemSegundos precisa ser >= 0");
		if (IntervaloSegundos < 0) p.Add("torneio.intervaloSegundos precisa ser >= 0");
		if (WoSegundos < 0) p.Add("torneio.woSegundos precisa ser >= 0");
		if (Premio1 < Premio2 || Premio2 < Premio3 || Premio3 < 0) p.Add("torneio.premios precisa ser decrescente e >= 0");
		if (NpcBpMinPct <= 0 || NpcBpMaxPct < NpcBpMinPct) p.Add($"torneio.npc: bpMinPct {NpcBpMinPct} / bpMaxPct {NpcBpMaxPct} invalidos");
		if (NpcBpPiso <= 0) p.Add("torneio.npc.bpPiso precisa ser > 0");
		if (MoldeDoNpc.Length == 0) p.Add("torneio.npc.molde vazio");
		foreach ((string nome, PalcoDeTorneio palco) in new[] { ("terra", Terra), ("alem", Alem) })
		{
			if (palco.Zona.Length == 0) p.Add($"torneio.{nome}.zona vazia");
			if (palco.Arena.Largura < 3 || palco.Arena.Altura < 3) p.Add($"torneio.{nome}.arena pequena demais ({palco.Arena})");
		}
		if (IntervaloDaTerraDias <= 0) p.Add("torneio.terra.intervaloDias precisa ser > 0");
		if (PrimeiroDaTerraDias < 0) p.Add("torneio.terra.primeiroEmDias precisa ser >= 0");
		if (AlemDepoisDaTerraDias <= 0 || AlemDepoisDaTerraDias >= IntervaloDaTerraDias)
			p.Add("torneio.alem.diasDepoisDaTerra precisa ficar entre 0 e o intervalo da Terra");
		return p;
	}

	private static double Num(JsonElement e, string chave, double padrao) =>
		e.TryGetProperty(chave, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : padrao;

	private static int Inteiro(JsonElement e, string chave, int padrao) =>
		e.TryGetProperty(chave, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : padrao;

	private static string Txt(JsonElement e, string chave, string padrao) =>
		e.TryGetProperty(chave, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? padrao : padrao;

	private static bool Bit(JsonElement e, string chave, bool padrao) =>
		e.TryGetProperty(chave, out JsonElement v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : padrao;
}
