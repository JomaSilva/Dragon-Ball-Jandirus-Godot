using System.Reflection;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Skills;

/// <summary>
/// O QUE AS SKILLS FAZEM NO CORPO -- a parte passiva, que e a maioria.
///
/// SAO QUATRO CANAIS, e descobrir isso custou uma varredura inteira do DM. A primeira versao daqui
/// so conhecia o primeiro e dava 200 skills como "sem efeito"; eram 180, e a diferenca eram skills
/// que faziam efeito por um caminho que ninguem tinha olhado:
///
///   1. ADITIVO      `savant.physoffBuff += 0.4`   -- 87 skills. O canal obvio.
///   2. MULTIPLICATIVO `savant.MedMod *= 2`        -- 6 skills. Campos que nascem em 1; somar 2 num
///                                                    deles daria o numero certo so por acidente.
///   3. GENETICO     `savant.genome.add_to_stat("Energy Level", 0.1)` -- 21 skills. NAO passa pelos
///                                                    campos `*Buff`: mexe no modificador da raca.
///   4. ATRIBUICAO   `savant.can_sup_el = 1`       -- 34 skills. Flags de capacidade e CONTADORES
///                                                    de arvore (`bodyskill`, `bodyreadiness`).
///
/// O NOME DO CAMPO DO DM E O NOME DO CAMPO DAQUI nos canais 1, 2 e 4, e por isso a entrega e por
/// REFLEXAO em vez de um switch de sessenta casos: a cadeia de stats foi portada 1:1 de proposito,
/// entao o switch seria uma segunda copia da mesma tabela -- e tabela duplicada e tabela que
/// diverge. Reflexao aqui e barata: isto roda ao APRENDER e ao ENTRAR, nunca no tick.
///
/// O CANAL 3 PRECISA DE TRADUCAO (<see cref="DeGene"/>) porque o DM indexa a genetica por texto
/// ("Energy Level") e o port ja resolveu isso em campo na hora do nascimento (`KiMod`).
///
/// E O CAMPO QUE NAO EXISTE VIRA RELATORIO, nao silencio. <see cref="Desconhecidos"/> junta todo
/// nome que o DM buffa e que o port ainda nao tem. Sem isso, uma skill que promete "+regeneracao"
/// e nao faz nada parece funcionar -- ninguem descobre pela tela, so pela planilha, anos depois.
///
/// APLICAR E IDEMPOTENTE. Guarda o que aplicou da ultima vez e desfaz antes de aplicar de novo,
/// entao aprender uma skill, deslogar, entrar e reaplicar tudo NAO empilha o buff duas vezes. Um
/// sistema de efeito permanente que soma no login e o jeito classico de um personagem virar um
/// deus depois de vinte relogs.
/// </summary>
public static class EfeitosDeSkill
{
	/// <summary>Campos que o DM buffa e que este port ainda nao tem. Diagnostico, nao erro.</summary>
	public static readonly SortedSet<string> Desconhecidos = new(StringComparer.Ordinal);

	private static readonly Dictionary<string, FieldInfo?> Cache = new(StringComparer.Ordinal);

	/// <summary>
	/// A TRADUCAO DO CANAL GENETICO: o nome que o DM usa como indice do `modifiers[]` da genetica,
	/// e o campo do <see cref="Fighter"/> que o port ja resolveu pra ele no nascimento
	/// (ver <c>Fighter.FromGenome</c> -- e de la que estes pares saem, nao de adivinhacao).
	///
	/// O que nao esta aqui e porque o port ainda nao tem o conceito, e vai sair no relatorio em vez
	/// de virar um `+=` num campo parecido. "Regeneration" nao e `HPregen`; chutar seria pior que
	/// admitir.
	/// </summary>
	private static readonly Dictionary<string, string> DeGene = new(StringComparer.Ordinal)
	{
		["Energy Level"] = "KiMod",
		["Battle Power"] = "BPMod",
		["Tech Modifier"] = "techmod",
		["Ascension Mod"] = "ascBPmod",
		// `misc_stats["Regeneration"]` (Genetic_Datum.dm:247-267): a RACA poe o dela pelo
		// `races.json`, e as skills SOMAM por cima (`add_to_stat("Regeneration", 10)`,
		// spirit-doll.dm:33; `5`, alien.dm:65; `1`, Bodybuilding.dm:254). O campo do lutador guarda
		// so a parte das skills, e quem junta as duas e o `GameServer.EixoDeRegen`.
		["Regeneration"] = "RegenerationDeSkill",
	};

	/// <summary>
	/// O NOME DO CAMPO DO LUTADOR pra um stat do genoma -- a mesma tabela, pra quem aplica gene por
	/// DEGRAU (<see cref="NiveisDeSkill.Aplicar"/>). Uma tabela so: a segunda copia e a que envelhece.
	/// Devolve nulo (e anota no relatorio) pro stat que o port ainda nao tem.
	/// </summary>
	internal static string? TraduzirGene(string stat)
	{
		if (DeGene.TryGetValue(stat, out string? campo)) return campo;
		Desconhecidos.Add($"gene:{stat}");
		return null;
	}

	/// <summary>
	/// O que a soma de todas as skills aprendidas da, canal a canal.
	/// Aditivo e genetico SOMAM; multiplicativo COMPOE (duas skills que dobram dao 4x).
	/// </summary>
	public static (Dictionary<string, double> Soma, Dictionary<string, double> Fator, Dictionary<string, double> Set)
		Totalizar(SkillCatalog cat, IEnumerable<string> aprendidas,
				  IReadOnlyDictionary<string, int>? escolhas = null, string? raca = null)
	{
		var soma = new Dictionary<string, double>(StringComparer.Ordinal);
		var fator = new Dictionary<string, double>(StringComparer.Ordinal);
		var set = new Dictionary<string, double>(StringComparer.Ordinal);

		void Somar1(Dictionary<string, double> buffs, Dictionary<string, double> genes,
					Dictionary<string, double> mults, Dictionary<string, double> flags)
		{
			foreach ((string campo, double v) in buffs)
				soma[campo] = soma.GetValueOrDefault(campo) + v;

			// o canal genetico cai no MESMO balde do aditivo depois de traduzido -- os dois
			// terminam somando num campo do lutador, e separa-los aqui so criaria dois caminhos
			// pra desfazer no relog
			foreach ((string stat, double v) in genes)
			{
				if (!DeGene.TryGetValue(stat, out string? campo)) { Desconhecidos.Add($"gene:{stat}"); continue; }
				soma[campo] = soma.GetValueOrDefault(campo) + v;
			}

			foreach ((string campo, double v) in mults)
				fator[campo] = fator.GetValueOrDefault(campo, 1) * v;

			// ATRIBUICAO: o ULTIMO vence, e nao ha ordem definida entre skills. Nao ha caso real
			// de duas skills setarem a mesma flag com valores diferentes -- se houvesse, o DM
			// tambem nao teria ordem. Fica o maior, que e o unico criterio estavel.
			foreach ((string campo, double v) in flags)
				set[campo] = Math.Max(set.GetValueOrDefault(campo), v);
		}

		foreach (string path in aprendidas)
		{
			Skill? s = cat.Get(path);
			if (s == null) continue;

			Somar1(s.Buffs, s.Genes, s.Mults, s.Flags);

			// ============================ O EFEITO CONDICIONADO A RACA ============================
			// `if(savant.Race=="Yardrat") savant.teleskill=70` (yardrat.dm:85-87). A Instant
			// Transmission e ENSINAVEL a qualquer raca, e so o Yardrat nasce com a pericia 70 -- os
			// outros comecam em 1 (`mob/var/teleskill=1`, :68) e pagam o salto com a metade do Ki.
			// O extrator emitia a linha como flag INCONDICIONAL e todo aprendiz virava Yardrat por
			// dentro. Agora ela vem em `porraca` (rotulo = a raca) e so entra pra quem E daquela raca.
			// Sem raca informada (censo, bancada de mesa) nenhuma entra -- que e o lado seguro.
			// ====================================================================================
			foreach (Escolha porRaca in s.PorRaca)
				if (raca != null && string.Equals(porRaca.Rotulo, raca, StringComparison.OrdinalIgnoreCase))
					Somar1(porRaca.Buffs, porRaca.Genes, porRaca.Mults, porRaca.Flags);

			// ============================ A ESCOLHA UNICA ============================
			// Uma skill no jogo tem casas EXCLUSIVAS (`Great Robotic Alliance`, meta.dm:104-125).
			// Enquanto o dono nao escolheu, ela nao rende NADA -- e assim no DM tambem: os buffs
			// moram dentro do `switch(input(...))`, e sem resposta o switch nao entra em casa
			// nenhuma. Somar as tres "pra nao ficar de graca" daria ao Metamoriano os tres
			// conjuntos de uma vez, que e o unico erro pior que nao dar nada.
			//
			// O indice e 1-based porque e o que o DM guarda em `chosen` (1, 2, 3); 0 = ainda nao
			// escolheu.
			// ======================================================================
			if (s.Escolhas.Length == 0 || escolhas == null) continue;
			int qual = CasaEscolhida(cat, s, escolhas);
			if (qual < 1 || qual > s.Escolhas.Length) continue;
			Escolha e = s.Escolhas[qual - 1];
			Somar1(e.Buffs, e.Genes, e.Mults, e.Flags);
		}
		return (soma, fator, set);
	}

	/// <summary>
	/// QUAL CASA VALE pra esta skill: a escolhida nela mesma, ou -- quando ela SEGUE outra -- a casa
	/// de mesmo rotulo escolhida na lider.
	///
	/// ============================ A ESCOLHA QUE SE HERDA ============================
	/// `Grace` nao pergunta nada: o `after_learn` dela abre `switch(savant.trinitytype)`
	/// (Bodybuilding.dm:243) e entra na casa que `TheHolyTrinity` escolheu. So que no DM o
	/// `trinitytype` do mob e escrito no `login()` da Trinity (:167) -- comprar as duas na mesma
	/// sessao deixava a Grace sem casa nenhuma pra sempre, porque o `after_learn` roda uma vez. O
	/// port herda a casa pelo ROTULO (as tres sao "Van-sama"/"Ricardo"/"Aniki" nas duas skills), e
	/// nao pelo instante do login: quem escolheu na Trinity tem a Grace correspondente.
	/// ==============================================================================
	/// </summary>
	public static int CasaEscolhida(SkillCatalog cat, Skill s, IReadOnlyDictionary<string, int> escolhas)
	{
		if (escolhas.TryGetValue(s.Path, out int propria)) return propria;
		if (s.EscolhaSegue.Length == 0) return 0;
		if (cat.Get(s.EscolhaSegue) is not { } lider || !escolhas.TryGetValue(lider.Path, out int daLider)) return 0;
		if (daLider < 1 || daLider > lider.Escolhas.Length) return 0;
		string rotulo = lider.Escolhas[daLider - 1].Rotulo;
		for (int i = 0; i < s.Escolhas.Length; i++)
			if (string.Equals(s.Escolhas[i].Rotulo, rotulo, StringComparison.OrdinalIgnoreCase)) return i + 1;
		return 0;
	}

	/// <summary>
	/// O ROTULO DA CASA ESCOLHIDA numa skill de escolha unica ("Van-sama"), ou nulo sem escolha (ou
	/// pra skill que nao e de escolha). E o que o <see cref="NiveisDeSkill.VerbosAtivos"/> pergunta pra
	/// decidir qual verb por casa vale -- a MESMA resolucao (propria ou herdada da lider) do
	/// <see cref="CasaEscolhida"/>, pra que a Grace e o degrau 2 da Trindade nunca discordem sobre em
	/// que casa o jogador esta.
	/// </summary>
	public static string? RotuloDaCasa(SkillCatalog cat, IReadOnlyDictionary<string, int> escolhas, string path)
	{
		if (cat.Get(path) is not { } s || s.Escolhas.Length == 0) return null;
		int qual = CasaEscolhida(cat, s, escolhas);
		return qual >= 1 && qual <= s.Escolhas.Length ? s.Escolhas[qual - 1].Rotulo : null;
	}

	/// <summary>
	/// Poe no lutador exatamente os efeitos das skills que ele sabe -- nem mais, nem de novo.
	/// Devolve quantos campos foram efetivamente mexidos.
	/// </summary>
	public static int Aplicar(Fighter f, SkillCatalog cat, IEnumerable<string> aprendidas,
							  IReadOnlyDictionary<string, int>? escolhas = null)
	{
		(Dictionary<string, double> soma, Dictionary<string, double> fator, Dictionary<string, double> set)
			= Totalizar(cat, aprendidas, escolhas, f.Race);   // a raca do corpo decide o `porraca`
		int mexidos = 0;

		// --- ADITIVO: desfaz o que saiu, ajusta o que mudou ---
		foreach ((string campo, double antigo) in f.BuffsDeSkill)
			if (!soma.ContainsKey(campo)) mexidos += Somar(f, campo, -antigo) ? 1 : 0;

		foreach ((string campo, double v) in soma)
		{
			double delta = v - f.BuffsDeSkill.GetValueOrDefault(campo);
			if (Math.Abs(delta) > 1e-9 && Somar(f, campo, delta)) mexidos++;
		}

		// --- MULTIPLICATIVO: DIVIDE o fator antigo antes de multiplicar o novo ---
		foreach ((string campo, double antigo) in f.MultsDeSkill)
			if (!fator.ContainsKey(campo) && antigo != 0) mexidos += Multiplicar(f, campo, 1 / antigo) ? 1 : 0;

		foreach ((string campo, double v) in fator)
		{
			double antigo = f.MultsDeSkill.GetValueOrDefault(campo, 1);
			if (antigo == 0) antigo = 1;
			double raz = v / antigo;
			if (Math.Abs(raz - 1) > 1e-9 && Multiplicar(f, campo, raz)) mexidos++;
		}

		// --- ATRIBUICAO: o que saiu volta a zero (o default do DM pra flag e contador) ---
		foreach ((string campo, double _) in f.FlagsDeSkill)
			if (!set.ContainsKey(campo)) mexidos += Escrever(f, campo, 0) ? 1 : 0;

		// CHAVE SE ESCREVE, SEMPRE -- e nao "so se o razao diz que mudou". Escrever o mesmo valor duas
		// vezes e inofensivo, e e o que dispensa o razao das flags de ser filtrado (ver abaixo): um save
		// que trouxe `pitted=1` como aplicado antes de o campo `pitted` existir recebe o 1 no dia em que o
		// campo nasce, porque a escrita nao pergunta ao razao.
		foreach ((string campo, double v) in set)
			if (Escrever(f, campo, v)) mexidos++;

		// ============================ O RAZAO SO GUARDA O QUE PEGOU (buffs e mults) ============================
		// Antes ele guardava o total INTEIRO, campo existente ou nao. Num save isso e uma armadilha
		// armada: `KaiokenMastery=3` ficava registrado como aplicado num lutador que nao tinha o
		// campo, e no dia em que o campo nascesse o delta seria zero -- o buff nunca chegaria em
		// quem ja tinha a skill. Tres campos daquele lote (`pitted`, `HPregenbuff`, `KaiokenMastery`)
		// nasceram exatamente nessa situacao. Ver `NiveisDeSkill.SoOsQueExistem`.
		//
		// ============================ AS FLAGS FICAM INTEIRAS, E ISSO NAO E DESCUIDO ============================
		// `FlagsDeSkill` NAO e so razao: e o ARMAZEM das chaves que o `Fighter` nao tem como campo e que
		// o catalogo de formas le por nome (`Formas.PedeFlag` -> `ContextoDeForma.Flag`): o `snamek=1` da
		// Super Namek e o `hasayyform=2` da transformacao Alien so existem AQUI. Filtra-los "porque o campo
		// nao existe" apagou as duas escadas: a skill era comprada, a tecla C ficava parada na base e a
		// `--formasteste` ficou vermelha em seis linhas (2026-09-02). A armadilha do save que motivou o
		// filtro nao alcanca as flags, porque a escrita acima nao consulta o razao.
		// ====================================================================================================
		f.BuffsDeSkill = NiveisDeSkill.SoOsQueExistem(soma);
		f.MultsDeSkill = NiveisDeSkill.SoOsQueExistem(fator);
		f.FlagsDeSkill = set;
		return mexidos;
	}

	/// <summary>O cache UNICO de reflexao sobre o Fighter. `internal` pra que o motor de
	/// niveis use este e nao escreva um segundo -- ver NiveisDeSkill.Somar.</summary>
	internal static FieldInfo? Campo(string nome)
	{
		FieldInfo? fi = Resolver(nome);
		// ANOTA A CADA APLICACAO, e nao so na primeira resolucao: perguntar (`CampoExiste`, a ficha)
		// nao e aplicar, e um nome que a ficha perguntou antes de alguem aplicar continua entrando
		// no relatorio de desconhecidos quando a aplicacao chegar
		if (fi == null) Desconhecidos.Add(nome);
		return fi;
	}

	/// <summary>
	/// O `Fighter` TEM este campo? A MESMA resolucao do <see cref="Campo"/> (publico, `double`), sem
	/// o efeito colateral do relatorio -- e o que a ficha (<see cref="FichaDeSkill"/>) pergunta pra
	/// marcar "ainda sem efeito neste port" na linha de um campo que o aplicador descartaria calado.
	/// </summary>
	public static bool CampoExiste(string nome) => Resolver(nome) != null;

	/// <summary>O port ja resolveu este stat do genoma num campo? (A tabela <c>DeGene</c>, sem anotar.)</summary>
	public static bool GeneExiste(string stat) => DeGene.ContainsKey(stat);

	private static FieldInfo? Resolver(string nome)
	{
		if (Cache.TryGetValue(nome, out FieldInfo? fi)) return fi;
		fi = typeof(Fighter).GetField(nome, BindingFlags.Public | BindingFlags.Instance);
		if (fi != null && fi.FieldType != typeof(double)) fi = null;
		Cache[nome] = fi;
		return fi;
	}

	private static bool Somar(Fighter f, string campo, double delta)
	{
		FieldInfo? fi = Campo(campo);
		if (fi == null) return false;
		fi.SetValue(f, (double)fi.GetValue(f)! + delta);
		return true;
	}

	private static bool Multiplicar(Fighter f, string campo, double razao)
	{
		FieldInfo? fi = Campo(campo);
		if (fi == null) return false;
		fi.SetValue(f, (double)fi.GetValue(f)! * razao);
		return true;
	}

	private static bool Escrever(Fighter f, string campo, double valor)
	{
		FieldInfo? fi = Campo(campo);
		if (fi == null) return false;
		fi.SetValue(f, valor);
		return true;
	}
}
