using System.Text.Json;
using Jandirus.Core.Npc;

namespace Jandirus.Core.Ai;

/// <summary>As frases curtas da vida diaria -- a secao `frases` do `rotina.json`.</summary>
public sealed class FrasesDaRotina
{
	// Os padroes existem pra um servidor SEM o arquivo continuar falando; o arquivo e quem manda.
	public string[] Conversa = ["Belo dia, nao?", "Ouviu falar do torneio?", "Meu treino esta rendendo."];
	public string[] Convite = ["Quer treinar comigo? So um spar."];
	public string[] Aceite = ["Vamos la!"];
	public string[] Recusa = ["Hoje nao, obrigado."];
	public string[] FimDoSpar = ["Boa luta!"];
	public string[] Treino = ["Hora de treinar."];
}

/// <summary>
/// AS MANIVELAS DA ROTINA -- `Assets/Data/rotina.json`, lido no boot. Dado, nao codigo: o dono pediu
/// *"duracoes/probabilidades configuraveis em um arquivo de config"*, e cada numero da vida diaria,
/// do espalhamento e da consciencia de arena mora aqui com o nome que tem no arquivo.
///
/// Os dois numeros de arena vem do original e sao citados no lugar: `TRN_AI_EDGE_MARGIN 2` e
/// `TRN_AI_FLY_CHANCE 60` (`Tournament.dm:35-36`), mais o `m >= 5` do pouso (`Tournament.dm:289`).
/// O resto nao existe no DM (ele nao tem vida diaria) e foi escolhido pra ser VISIVEL num teste de
/// mesa: conversa de 8-16 s, spar de ate 60 s, descanso de 20-40 s.
/// </summary>
public sealed class RotinaConfig
{
	public Espalhamento Espalhamento = new();

	// --- ocio e passeio -------------------------------------------------------
	/// <summary>Quanto tempo parado antes de decidir o proximo afazer (sorteado na faixa).</summary>
	public double OciosoMin = 4, OciosoMax = 12;
	/// <summary>Quanto tempo, no maximo, um passeio dura antes de desistir do destino.</summary>
	public double PasseioMin = 3, PasseioMax = 8;
	/// <summary>Ate quantos tiles de distancia o destino de um passeio e sorteado.</summary>
	public int PasseioTiles = 6;

	// --- treino sozinho -------------------------------------------------------
	public double ChanceDeTreinar = 0.25;
	public double TreinoMin = 15, TreinoMax = 40;

	// --- conversa -------------------------------------------------------------
	/// <summary>Chance de puxar conversa QUANDO ha um habitante livre ao alcance.</summary>
	public double ChanceDeConversar = 0.5;
	public int RaioDeConversaTiles = 6;
	public double ConversaMin = 8, ConversaMax = 16;
	/// <summary>Intervalo entre uma fala e a proxima (os dois alternam).</summary>
	public double SegundosEntreFalas = 2.5;

	// --- convite pro spar -----------------------------------------------------
	/// <summary>Chance de quem puxou a conversa convidar pro spar no fim dela.</summary>
	public double ChanceDeConvidar = 0.3;
	public double ChanceDeAceitar = 0.6;
	/// <summary>
	/// A "inteligencia minima" do convidado: recusa se um dos dois for mais de N vezes mais forte
	/// que o outro (poder expresso). Ninguem chama pra spar quem o esmagaria.
	/// </summary>
	public double RazaoDePoderQueAssusta = 3;

	// --- o spar ---------------------------------------------------------------
	public double SparMax = 60;
	/// <summary>O spar acaba quando um dos dois cai a esta fracao de vida (ou no tempo maximo).</summary>
	public double VidaQueEncerraOSpar = 0.35;
	public double DescansoMin = 20, DescansoMax = 40;

	// --- o tique barato -------------------------------------------------------
	/// <summary>Habitante a mais de N tiles de TODO jogador da zona pensa em ritmo reduzido.</summary>
	public int RaioDeAtencaoTiles = 40;
	/// <summary>...um tique a cada N (o relogio da mente anda os N de uma vez; o passo, nao).</summary>
	public int DivisorDeLonge = 10;

	// --- consciencia de arena (os numeros do original) ------------------------
	/// <summary>`TRN_AI_EDGE_MARGIN`: a quantas celulas da linha a IA reage.</summary>
	public int MargemDaArenaTiles = 2;
	/// <summary>O `m >= 5` do `trn_ai_assist`: voando, com o oponente no chao e esta folga, pousa.</summary>
	public int MargemSeguraParaPousarTiles = 5;
	/// <summary>`TRN_AI_FLY_CHANCE` (60%): a fracao dos NPCs de torneio que entram sabendo voar.</summary>
	public double ChanceDeVoarNoTorneio = 0.60;

	public FrasesDaRotina Frases = new();

	public static RotinaConfig Ler(string json)
	{
		var c = new RotinaConfig();
		using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
		JsonElement raiz = doc.RootElement;

		if (raiz.TryGetProperty("espalhamento", out JsonElement e) && e.ValueKind == JsonValueKind.Object)
		{
			c.Espalhamento.LadoDaRegiaoEmTiles = Inteiro(e, "ladoDaRegiaoEmTiles", c.Espalhamento.LadoDaRegiaoEmTiles);
			c.Espalhamento.DistanciaMinimaEmTiles = Inteiro(e, "distanciaMinimaEmTiles", c.Espalhamento.DistanciaMinimaEmTiles);
			c.Espalhamento.MinimoDeVagasPorRegiao = Inteiro(e, "minimoDeVagasPorRegiao", c.Espalhamento.MinimoDeVagasPorRegiao);
			c.Espalhamento.MinimoDeCelulasPorComponente = Inteiro(e, "minimoDeCelulasPorComponente", c.Espalhamento.MinimoDeCelulasPorComponente);
		}
		if (raiz.TryGetProperty("rotina", out JsonElement r) && r.ValueKind == JsonValueKind.Object)
		{
			(c.OciosoMin, c.OciosoMax) = Faixa(r, "ociosoSegundos", c.OciosoMin, c.OciosoMax);
			(c.PasseioMin, c.PasseioMax) = Faixa(r, "passeioSegundos", c.PasseioMin, c.PasseioMax);
			c.PasseioTiles = Inteiro(r, "passeioTiles", c.PasseioTiles);
			c.ChanceDeTreinar = Num(r, "chanceDeTreinar", c.ChanceDeTreinar);
			(c.TreinoMin, c.TreinoMax) = Faixa(r, "treinoSegundos", c.TreinoMin, c.TreinoMax);
			c.ChanceDeConversar = Num(r, "chanceDeConversar", c.ChanceDeConversar);
			c.RaioDeConversaTiles = Inteiro(r, "raioDeConversaTiles", c.RaioDeConversaTiles);
			(c.ConversaMin, c.ConversaMax) = Faixa(r, "conversaSegundos", c.ConversaMin, c.ConversaMax);
			c.SegundosEntreFalas = Num(r, "segundosEntreFalas", c.SegundosEntreFalas);
			c.ChanceDeConvidar = Num(r, "chanceDeConvidar", c.ChanceDeConvidar);
			c.ChanceDeAceitar = Num(r, "chanceDeAceitar", c.ChanceDeAceitar);
			c.RazaoDePoderQueAssusta = Num(r, "razaoDePoderQueAssusta", c.RazaoDePoderQueAssusta);
			c.SparMax = Num(r, "sparSegundosMax", c.SparMax);
			c.VidaQueEncerraOSpar = Num(r, "vidaQueEncerraOSpar", c.VidaQueEncerraOSpar);
			(c.DescansoMin, c.DescansoMax) = Faixa(r, "descansoSegundos", c.DescansoMin, c.DescansoMax);
			c.RaioDeAtencaoTiles = Inteiro(r, "raioDeAtencaoTiles", c.RaioDeAtencaoTiles);
			c.DivisorDeLonge = Inteiro(r, "divisorDeLonge", c.DivisorDeLonge);
		}
		if (raiz.TryGetProperty("combate", out JsonElement k) && k.ValueKind == JsonValueKind.Object)
		{
			c.MargemDaArenaTiles = Inteiro(k, "margemDaArenaTiles", c.MargemDaArenaTiles);
			c.MargemSeguraParaPousarTiles = Inteiro(k, "margemSeguraParaPousarTiles", c.MargemSeguraParaPousarTiles);
			c.ChanceDeVoarNoTorneio = Num(k, "chanceDeVoarNoTorneio", c.ChanceDeVoarNoTorneio);
		}
		if (raiz.TryGetProperty("frases", out JsonElement f) && f.ValueKind == JsonValueKind.Object)
		{
			c.Frases.Conversa = Lista(f, "conversa", c.Frases.Conversa);
			c.Frases.Convite = Lista(f, "convite", c.Frases.Convite);
			c.Frases.Aceite = Lista(f, "aceite", c.Frases.Aceite);
			c.Frases.Recusa = Lista(f, "recusa", c.Frases.Recusa);
			c.Frases.FimDoSpar = Lista(f, "fimDoSpar", c.Frases.FimDoSpar);
			c.Frases.Treino = Lista(f, "treino", c.Frases.Treino);
		}
		return c;
	}

	/// <summary>O que esta errado no arquivo -- o servidor imprime e segue com o valor que leu.</summary>
	public List<string> Problemas()
	{
		var p = Espalhamento.Problemas();
		void Chance(string nome, double v) { if (v < 0 || v > 1) p.Add($"rotina.{nome} = {v}: chance fora de [0, 1]"); }
		void Ordem(string nome, double min, double max) { if (min < 0 || max < min) p.Add($"rotina.{nome} = [{min}, {max}]: faixa invalida"); }
		Ordem("ociosoSegundos", OciosoMin, OciosoMax);
		Ordem("passeioSegundos", PasseioMin, PasseioMax);
		Ordem("treinoSegundos", TreinoMin, TreinoMax);
		Ordem("conversaSegundos", ConversaMin, ConversaMax);
		Ordem("descansoSegundos", DescansoMin, DescansoMax);
		Chance("chanceDeTreinar", ChanceDeTreinar);
		Chance("chanceDeConversar", ChanceDeConversar);
		Chance("chanceDeConvidar", ChanceDeConvidar);
		Chance("chanceDeAceitar", ChanceDeAceitar);
		Chance("vidaQueEncerraOSpar", VidaQueEncerraOSpar);
		Chance("combate.chanceDeVoarNoTorneio", ChanceDeVoarNoTorneio);
		if (RazaoDePoderQueAssusta < 1) p.Add($"rotina.razaoDePoderQueAssusta = {RazaoDePoderQueAssusta}: precisa ser >= 1");
		if (SegundosEntreFalas <= 0) p.Add("rotina.segundosEntreFalas precisa ser > 0");
		if (SparMax <= 0) p.Add("rotina.sparSegundosMax precisa ser > 0");
		if (RaioDeConversaTiles < 1) p.Add("rotina.raioDeConversaTiles precisa ser >= 1");
		if (DivisorDeLonge < 1) p.Add("rotina.divisorDeLonge precisa ser >= 1");
		if (RaioDeAtencaoTiles < 1) p.Add("rotina.raioDeAtencaoTiles precisa ser >= 1");
		if (MargemDaArenaTiles < 0) p.Add("combate.margemDaArenaTiles precisa ser >= 0");
		if (Frases.Conversa.Length == 0) p.Add("frases.conversa esta vazia: a conversa seria muda");
		return p;
	}

	private static double Num(JsonElement e, string chave, double padrao) =>
		e.TryGetProperty(chave, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : padrao;

	private static int Inteiro(JsonElement e, string chave, int padrao) =>
		e.TryGetProperty(chave, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : padrao;

	/// <summary>`"chave": [min, max]`. Um numero so vale como faixa de largura zero.</summary>
	private static (double, double) Faixa(JsonElement e, string chave, double min, double max)
	{
		if (!e.TryGetProperty(chave, out JsonElement v)) return (min, max);
		if (v.ValueKind == JsonValueKind.Number) { double n = v.GetDouble(); return (n, n); }
		if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 2) return (min, max);
		return (v[0].GetDouble(), v[1].GetDouble());
	}

	private static string[] Lista(JsonElement e, string chave, string[] padrao)
	{
		if (!e.TryGetProperty(chave, out JsonElement v) || v.ValueKind != JsonValueKind.Array) return padrao;
		var l = new List<string>();
		foreach (JsonElement x in v.EnumerateArray())
			if (x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 } s) l.Add(s);
		return l.Count > 0 ? [.. l] : padrao;
	}
}
