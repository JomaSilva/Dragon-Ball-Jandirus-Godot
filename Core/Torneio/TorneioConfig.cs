using System.Text.Json;
using Jandirus.Core.World;

namespace Jandirus.Core.Torneio;

/// <summary>Os dois torneios do original: o da Terra (vivos) e o do Outro Mundo (mortos).</summary>
public enum TipoDeTorneio : byte
{
	Terra = 1,
	Alem = 2,
}

/// <summary>Onde um torneio acontece: a zona, o retangulo da arena (`TRN_ARENA_*` / `TRNH_ARENA_*`) e as cadeiras de quem espera.</summary>
public sealed class PalcoDeTorneio
{
	public string Zona = "";
	public Arena Arena;

	/// <summary>
	/// AS CADEIRAS DA PLATEIA (`seats`, `Tournament.dm:219`), na ordem em que o `rest_place` as ocupa: quem espera senta na
	/// primeira livre. Vazia = a roda sorteada em volta da arena (`rest_turf`) -- a da Terra, onde o DM deixa `seats = null`.
	/// </summary>
	public List<Cadeira> Cadeiras = new();

	/// <summary>A cadeira desta celula, se houver uma.</summary>
	public Cadeira? CadeiraEm(int cx, int cy)
	{
		foreach (Cadeira c in Cadeiras)
			if (c.X == cx && c.Y == cy) return c;
		return null;
	}
}

/// <summary>Uma cadeira da plateia do torneio, em celulas do port.</summary>
public readonly record struct Cadeira(int X, int Y)
{
	/// <summary>Onde quem senta fica: o meio da celula, em pixels.</summary>
	public Vec2 Lugar => new Vec2(X + 0.5f, Y + 0.5f) * ZoneCollision.TileSize;

	/// <summary>
	/// PRA ONDE QUEM SENTA OLHA: o centro da arena (o `M.dir = S[3]` do `rest_place`). O DM escreve a direcao de cada fileira
	/// (`EAST`, `SOUTH`, `NORTH`, `WEST`), sempre a que encara o palco; aqui ela sai da conta, e a `--torneioteste` confere que as
	/// duas batem nas 32.
	/// </summary>
	public Facing OlharPara(Arena a) => MoveRules.FacingFrom(a.Centro - Lugar, Facing.South);
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
///
/// A CELESTE E O PALCO INTEIRO, E NAO O RETANGULO DO DM (2026-10-17, divergencia declarada). O piso claro do palco no mapa vai
/// de (110, 346) a (125, 360) -- 16 x 15 celulas --, e o retangulo do DM e so o canto de baixo-direita dele (10 x 9): divide o
/// canto e erra o tamanho. Com ele a roda de espera (2 a 5 celulas fora da linha) caia EM CIMA do palco pelos lados norte e
/// oeste, e o lutador levava ring-out pisando no piso claro. O dono: *"so os dois q estao lutando estao dentro dela"*.
///
/// AS CADEIRAS DO CEU (2026-10-17). O DM senta quem espera o torneio do Outro Mundo em 32 cadeiras fixas, uma a cada 2 celulas em
/// volta do palco, todas viradas pra arena (o `New()` do `datum/tournament/heaven`, `Tournament.dm:650-654`, e o `rest_place`,
/// `:256`); o port tinha so a roda sorteada, a da Terra, e o dono viu: *"coloque os participantes do torneio do outro mundo pra
/// esperarem onde tem os bancos ali"*. Pela mesma conta, a fileira esquerda do DM (x 109, y 154..140) e a coluna 108 de 346 a 360,
/// e assim por diante (`CadeirasDoCeu`) -- e as cadeiras do proprio DM cercam o palco INTEIRO, a 2 celulas da beirada dele: mais
/// uma prova de que o `TRNH_ARENA` e que estava errado.
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
	public PalcoDeTorneio Alem = new() { Zona = "Afterlife", Arena = new Arena(110, 346, 125, 360), Cadeiras = CadeirasDoCeu() };   // o palco inteiro (ver o cabecalho)

	/// <summary>
	/// AS 32 CADEIRAS DO CEU como o DM as monta (`Tournament.dm:650-654`), em coordenada BYOND e pela conta do cabecalho (`x - 1`,
	/// `500 - y`): as fileiras esquerda, de cima, de baixo e direita, nesta ordem -- a do `rest_place`.
	/// </summary>
	public static List<Cadeira> CadeirasDoCeu()
	{
		var c = new List<Cadeira>();
		for (int yy = 154; yy >= 140; yy -= 2) c.Add(DoByond(109, yy));   // fileira esquerda (EAST)
		for (int xx = 111; xx <= 125; xx += 2) c.Add(DoByond(xx, 156));   // fileira de cima (SOUTH)
		for (int xx = 111; xx <= 125; xx += 2) c.Add(DoByond(xx, 138));   // fileira de baixo (NORTH)
		for (int yy = 154; yy >= 140; yy -= 2) c.Add(DoByond(128, yy));   // fileira direita (WEST)
		return c;

		static Cadeira DoByond(int x, int y) => new(x - 1, 500 - y);
	}

	/// <summary>
	/// A agenda: de quantos em quantos DIAS DO JOGO a Terra tem torneio, e quando e o primeiro. O dia do jogo e o do ceu, 24 min
	/// de relogio (`Espaco.SegundosPorDiaInGame`): 30 dias sao 12 h reais. Ate 2026-10-17 era o dia de parede (o dono: *"o tempo
	/// de torneio n ta em dias do jogo ta em dias da vida real"*).
	/// </summary>
	public double IntervaloDaTerraDias = 30;
	public double PrimeiroDaTerraDias = 1;

	/// <summary>O do Outro Mundo acontece N dias DO JOGO depois de cada torneio da Terra.</summary>
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
		// [x, y] cada uma; uma mal escrita derruba a leitura inteira (o `torneio.json ilegivel` do boot), como a arena.
		if (e.TryGetProperty("cadeiras", out JsonElement cs) && cs.ValueKind == JsonValueKind.Array)
			palco.Cadeiras = [.. cs.EnumerateArray().Select(c => new Cadeira(c[0].GetInt32(), c[1].GetInt32()))];
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
			var vistas = new HashSet<Cadeira>();
			foreach (Cadeira c in palco.Cadeiras)
			{
				if (palco.Arena.Contem(c.X, c.Y)) p.Add($"torneio.{nome}.cadeiras: ({c.X},{c.Y}) fica DENTRO da arena -- quem espera estaria na luta");
				if (!vistas.Add(c)) p.Add($"torneio.{nome}.cadeiras: ({c.X},{c.Y}) repetida");
			}
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
