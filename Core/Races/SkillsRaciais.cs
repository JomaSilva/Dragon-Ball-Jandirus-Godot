using System.Numerics;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Races;

/// <summary>
/// (2026-10-17) AS SKILLS DOS CINCO KITS DE RACA, UMA POR BIT. O kit inteiro continua vindo com a raca -- o Saiyajin tem as cinco dele, o
/// Humano as quatro --; o bit existe pro BIO-ANDROIDE, que ESCOLHE. O adendo do dono: *"o bio androide pode escolher dentre as raças de
/// seu dna quais skills (passivas e ativas) ele quer, com limite de 6 skills"*. Ver <see cref="SkillsRaciais"/>.
///
/// SO OS KITS DE RACA TEM BIT (Saiyajin, meio-Saiyajin, Humano, Namekuseijin, Frost Demon). Os de CLASSE -- Primal, Lendario, Lendario
/// Primal, mutante -- continuam inteiros pelo <see cref="KitRacial"/>: o sangue do bio guarda a RACA do doador e nao a linhagem, entao um
/// Lendario doa o Saiyajin.
///
/// OS BITS SAO DO SAVE (`Fighter.bio_skills`) E DO FIO (`AtributosState.SkillsRaciais`): NUNCA RENUMERAR. Skill nova entra no fim.
/// </summary>
[Flags]
public enum SkillRacial : uint
{
	Nenhuma = 0,

	// ---- Saiyajin ----
	/// <summary>Seasoned Warrior: +10% de BP por forma veterana.</summary>
	GuerreiroVeterano = 1u << 0,
	/// <summary>Warrior Race: com o tronco ou a cabeca abaixo de 20%, ataque e defesa fisicos.</summary>
	RacaGuerreira = 1u << 1,
	/// <summary>Saiyan's Resolve: o golpe do mesmo tipo em fila doi menos.</summary>
	Resolucao = 1u << 2,
	/// <summary>Prideful: a barra baixa rende regen e fisico.</summary>
	Orgulho = 1u << 3,
	/// <summary>False Advantage: com um recurso abaixo da metade, a forma mortal soma.</summary>
	FalsaVantagem = 1u << 4,

	// ---- meio-Saiyajin ----
	/// <summary>Prodigy: forma nova com 50% de maestria; dominada, soma.</summary>
	Prodigio = 1u << 5,
	/// <summary>Immeasurable Potential: o teto da carga de Ki a 225%.</summary>
	Potencial = 1u << 6,
	/// <summary>Hidden Talent: a tecnica dominada rende mais.</summary>
	Talento = 1u << 7,
	/// <summary>Blazing Spirit: o membro que cai abaixo da metade acende a defesa.</summary>
	Espirito = 1u << 8,

	// ---- Humano ----
	/// <summary>Persistence: o treino seguido rende mais.</summary>
	Persistencia = 1u << 9,
	/// <summary>Second Wind (ativa).</summary>
	SegundoFolego = 1u << 10,
	/// <summary>Sheer Willpower (ativa).</summary>
	ForcaDeVontade = 1u << 11,
	/// <summary>Max Power (ativa, liga e desliga).</summary>
	PoderMaximo = 1u << 12,

	// ---- Namekuseijin ----
	/// <summary>Namekian Resilience: o nocaute na metade do tempo.</summary>
	Resiliencia = 1u << 13,
	/// <summary>Spirit Disruption (ativa).</summary>
	Disrupcao = 1u << 14,
	/// <summary>Warrior's Vitality: os membros feridos pesam a metade.</summary>
	Vitalidade = 1u << 15,

	// ---- Frost Demon ----
	/// <summary>Perfect Control: a carga de tecnica de ki e o dano de ki.</summary>
	Controle = 1u << 16,
	/// <summary>Extreme Tenacity: o nocaute na metade e o membro fraco apanha menos.</summary>
	Tenacidade = 1u << 17,
	/// <summary>Unrestrained Wrath (ativa).</summary>
	Ira = 1u << 18,
	/// <summary>Revitalizing Metamorphosis: transformar com o tronco ferido cura.</summary>
	Metamorfose = 1u << 19,
}

/// <summary>
/// Uma skill de kit de raca: o bit, o nome do jogo (em ingles, como o das tecnicas), o kit que a da e, na ativa, o id dela no canal de
/// habilidade (vazio = passiva).
/// </summary>
public sealed record SkillDeRaca(SkillRacial Bit, string Nome, KitRacial Kit, string Ativa = "")
{
	public bool EhAtiva => Ativa.Length > 0;

	/// <summary>O id que viaja no canal de habilidade na ESCOLHA do bio: `dna:GuerreiroVeterano` (o nome do bit).</summary>
	public string IdDaEscolha => SkillsRaciais.PrefixoDaEscolha + Bit;
}

/// <summary>
/// ============================ O CATALOGO DAS SKILLS DE RACA, E A ESCOLHA DO BIO-ANDROIDE ============================
/// O adendo do dono (2026-10-17): *"o bio androide pode escolher dentre as raças de seu dna quais skills (passivas e ativas) ele quer, com
/// limite de 6 skills"*. Substitui a uniao automatica dos kits das doadoras que eu tinha comecado (desfeita no job 69).
///
///   * QUEM ESCOLHE: o bio-androide, entre as skills dos kits de raca das doadoras (<see cref="DoSangue"/>) -- ate
///     <see cref="LimiteDoBio"/>. A escolha e PERMANENTE (leitura minha: uma por vez, sem trocar; e um botao por skill na aba Skills,
///     que some com a escolha). O bio NPC (o Cell) escolhe sozinho, ao nascer (<see cref="EscolhaDoNpc"/>).
///   * O QUE VALE: o tique racial (`EstadoRacial.Calcular`) pergunta por BIT (`EntradaRacial.Tem`) e nao mais pelo kit -- quem nao e bio
///     recebe o kit inteiro da raca (<see cref="DoKit"/>), entao pra ele nada muda; o bio recebe so as escolhidas.
///   * AS ATIVAS: o verb, a IA e o botao perguntam <see cref="TemAtiva"/> -- as dos kits de raca pelo bit, as dos kits de classe pelo kit.
/// =================================================================================================================
/// </summary>
public static class SkillsRaciais
{
	/// <summary>O limite do bio -- o adendo: *"com limite de 6 skills"*.</summary>
	public const int LimiteDoBio = 6;

	/// <summary>O prefixo do canal de habilidade da escolha do bio: `dna:&lt;bit&gt;` (`GameServer.EscolherSkillDoDna`).</summary>
	public const string PrefixoDaEscolha = "dna:";

	/// <summary>
	/// AS VINTE, na ordem dos kits (Saiyajin, meio-Saiyajin, Humano, Namekuseijin, Frost Demon) e, dentro de cada kit, na da lista do amigo do
	/// dono (`PEDIDO-16.md`, itens 1-5, 19-26 e 30-36; as formas da Tensao, 27-29, sao FORMAS e nao skill: ficam com o Humano).
	/// </summary>
	public static readonly SkillDeRaca[] Todas =
	[
		new(SkillRacial.GuerreiroVeterano, "Seasoned Warrior", KitRacial.Saiyajin),
		new(SkillRacial.RacaGuerreira, "Warrior Race", KitRacial.Saiyajin),
		new(SkillRacial.Resolucao, "Saiyan's Resolve", KitRacial.Saiyajin),
		new(SkillRacial.Orgulho, "Prideful", KitRacial.Saiyajin),
		new(SkillRacial.FalsaVantagem, "False Advantage", KitRacial.Saiyajin),
		new(SkillRacial.Prodigio, "Prodigy", KitRacial.MeioSaiyajin),
		new(SkillRacial.Potencial, "Immeasurable Potential", KitRacial.MeioSaiyajin),
		new(SkillRacial.Talento, "Hidden Talent", KitRacial.MeioSaiyajin),
		new(SkillRacial.Espirito, "Blazing Spirit", KitRacial.MeioSaiyajin),
		new(SkillRacial.Persistencia, "Persistence", KitRacial.Humano),
		new(SkillRacial.SegundoFolego, "Second Wind", KitRacial.Humano, Passivas.SegundoFolego),
		new(SkillRacial.ForcaDeVontade, "Sheer Willpower", KitRacial.Humano, Passivas.ForcaDeVontade),
		new(SkillRacial.PoderMaximo, "Max Power", KitRacial.Humano, Passivas.PoderMaximo),
		new(SkillRacial.Resiliencia, "Namekian Resilience", KitRacial.Namekuseijin),
		new(SkillRacial.Disrupcao, "Spirit Disruption", KitRacial.Namekuseijin, Passivas.Disrupcao),
		new(SkillRacial.Vitalidade, "Warrior's Vitality", KitRacial.Namekuseijin),
		new(SkillRacial.Controle, "Perfect Control", KitRacial.FrostDemon),
		new(SkillRacial.Tenacidade, "Extreme Tenacity", KitRacial.FrostDemon),
		new(SkillRacial.Ira, "Unrestrained Wrath", KitRacial.FrostDemon, Passivas.Ira),
		new(SkillRacial.Metamorfose, "Revitalizing Metamorphosis", KitRacial.FrostDemon),
	];

	/// <summary>
	/// AS SKILLS QUE UM KIT DA: as dos kits de raca dentro dele (o Lendario carrega o Saiyajin; o mutante, o Frost Demon). Roda no tique
	/// racial de todo corpo: laco por indice, sem alocar.
	/// </summary>
	public static SkillRacial DoKit(KitRacial kit)
	{
		SkillRacial s = SkillRacial.Nenhuma;
		for (int i = 0; i < Todas.Length; i++)
			if ((kit & Todas[i].Kit) != 0) s |= Todas[i].Bit;
		return s;
	}

	/// <summary>A skill que da esta ATIVA racial -- Nenhuma pra ativa de kit de classe, ou pra id que nao e de ativa.</summary>
	public static SkillRacial DaAtiva(string id)
	{
		for (int i = 0; i < Todas.Length; i++)
			if (string.Equals(Todas[i].Ativa, id, StringComparison.Ordinal)) return Todas[i].Bit;
		return SkillRacial.Nenhuma;
	}

	/// <summary>
	/// ESTE CORPO TEM ESTA ATIVA RACIAL? As dos kits de RACA pelo bit (o kit inteiro, ou a escolha do bio); as dos kits de CLASSE pelo kit
	/// (`Passivas.KitDaAtiva`). A pergunta do verb (`GameServer.UsarAtivaRacial`), da IA (`GameServer.AtivasRaciaisNaMao`) e do botao.
	/// </summary>
	public static bool TemAtiva(KitRacial kit, SkillRacial skills, string id)
	{
		SkillRacial s = DaAtiva(id);
		return s != SkillRacial.Nenhuma ? (skills & s) != 0 : (kit & Passivas.KitDaAtiva(id)) != 0;
	}

	/// <summary>Quantas skills ha nesta mascara.</summary>
	public static int Quantas(SkillRacial s) => BitOperations.PopCount((uint)s);

	/// <summary>A skill pelo id da escolha (o nome do bit, `GuerreiroVeterano`) -- nula pra id que nao e de skill.</summary>
	public static SkillDeRaca? PorId(string id)
	{
		foreach (SkillDeRaca d in Todas)
			if (string.Equals(d.Bit.ToString(), id, StringComparison.Ordinal)) return d;
		return null;
	}

	/// <summary>Os nomes das skills de uma mascara, na ordem do catalogo ("Warrior Race, Second Wind").</summary>
	public static string Nomes(SkillRacial s) => string.Join(", ", Todas.Where(d => (s & d.Bit) != 0).Select(d => d.Nome));

	// =====================================================================
	// O BIO-ANDROIDE ESCOLHE
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada `--bioteste`): o bio volta a ganhar o kit INTEIRO das racas do sangue -- a uniao automatica que o adendo
	/// trocou pela escolha. Lido por <see cref="DoCorpoDoBio"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool BioComOSangueInteiroDeTeste;

	/// <summary>
	/// AS SKILLS QUE O SANGUE DO BIO DA PRA ESCOLHER: as dos kits de raca das doadoras (`SangueDoBio.Doadoras`), cada uma pela raca SEM
	/// classe (`Passivas.KitDe(raca, "", "")`). Zero pra quem nao e bio. Sem alocar: roda nos fatos, cinco vezes por segundo por jogador.
	/// </summary>
	public static SkillRacial DoSangue(Fighter f)
	{
		IReadOnlyList<string> doadoras = SangueDoBio.Doadoras(f);
		SkillRacial s = SkillRacial.Nenhuma;
		for (int i = 0; i < doadoras.Count; i++) s |= DoKit(Passivas.KitDe(doadoras[i], "", ""));
		return s;
	}

	/// <summary>AS QUE ESTE BIO ESCOLHEU (`Fighter.bio_skills`), cortadas pelo sangue: o que o sangue nao da nao vale.</summary>
	public static SkillRacial EscolhidasDoBio(Fighter f) => (SkillRacial)f.bio_skills & DoSangue(f);

	/// <summary>As skills que VALEM no corpo do bio: as escolhidas (ou, com o defeito injetado, o sangue inteiro).</summary>
	public static SkillRacial DoCorpoDoBio(Fighter f) => BioComOSangueInteiroDeTeste ? DoSangue(f) : EscolhidasDoBio(f);

	/// <summary>Quantas este bio ainda pode escolher: as vagas que sobram das seis, e nao mais do que o sangue ainda tem pra dar.</summary>
	public static int AindaEscolhe(Fighter f)
	{
		SkillRacial escolhidas = EscolhidasDoBio(f);
		return Math.Min(LimiteDoBio - Quantas(escolhidas), Quantas(DoSangue(f) & ~escolhidas));
	}

	/// <summary>A recusa de quem nao e bio -- e o pedaco que a tabela de verbs cobra.</summary>
	public const string RecusaSemDna = "só o bio-androide escolhe skills do sangue.";

	/// <summary>
	/// POR QUE ESTE CORPO NAO ESCOLHE ESTA SKILL (vazio = escolhe). As frases sao as que a tabela de verbs cobra (`RequisitosDosVerbos`, as
	/// linhas `hab:dna:*`).
	/// </summary>
	public static string PorQueNaoEscolhe(Fighter f, SkillRacial bit)
	{
		if (!SangueDoBio.EhBio(f)) return RecusaSemDna;
		if ((DoSangue(f) & bit) == 0) return "essa skill não vem de nenhuma raça do seu sangue.";
		SkillRacial escolhidas = EscolhidasDoBio(f);
		if ((escolhidas & bit) != 0) return "você já tem essa skill.";
		if (Quantas(escolhidas) >= LimiteDoBio) return $"você já escolheu as {LimiteDoBio} skills do seu DNA.";
		return "";
	}

	/// <summary>
	/// A ESCOLHA DO BIO NPC (o Cell): as racas do sangue EM RODIZIO, na ordem das amostras, e dentro de cada uma as ATIVAS primeiro (a IA as
	/// aperta -- `GameServer.AtivasRaciaisNaMao`) e depois as passivas, na ordem do catalogo, ate <see cref="LimiteDoBio"/>. ESCOLHA minha,
	/// pra o chefe sair com um pouco de cada sangue. O Cell (Saiyajin, Namekuseijin, Icer -- o sangue que o dono deu a ele em 2026-10-19) sai
	/// com Seasoned Warrior, Spirit Disruption, Unrestrained Wrath, Warrior Race, Namekian Resilience e Perfect Control; o Cell Jr, clone
	/// dele, com as mesmas.
	/// </summary>
	public static SkillRacial EscolhaDoNpc(IReadOnlyList<string> sangue)
	{
		var filas = new List<List<SkillDeRaca>>();
		foreach (string raca in sangue)
		{
			KitRacial kit = Passivas.KitDe(raca, "", "");
			if (kit == KitRacial.Nenhum) continue;
			filas.Add([.. Todas.Where(d => (kit & d.Kit) != 0).OrderBy(d => d.EhAtiva ? 0 : 1)]);
		}
		SkillRacial s = SkillRacial.Nenhuma;
		for (int volta = 0; Quantas(s) < LimiteDoBio; volta++)
		{
			bool algum = false;
			foreach (List<SkillDeRaca> fila in filas)
			{
				if (volta >= fila.Count || Quantas(s) >= LimiteDoBio) continue;
				s |= fila[volta].Bit;
				algum = true;
			}
			if (!algum) break;
		}
		return s;
	}
}
