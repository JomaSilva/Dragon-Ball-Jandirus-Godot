using Jandirus.Core.Ranks;

namespace Jandirus.Core.Magic;

/// <summary>
/// **A LINGUA DOS DEUSES** -- o portao do Super Shenron. Porte de
/// `Code/Modules/Magic/WishTable.dm:22-43`.
///
/// ============================ ELA E UM BOOLEANO, E ISSO E O PORTE ============================
/// A tentacao aqui era desenhar: uma skill, um no de arvore, um campo do datum de raca, um bit do
/// cargo. O original **nao** faz nada disso -- `mob/var/godtongue = 0` (:23) e um booleano cru na
/// ficha, que persiste porque a ficha inteira persiste. Entao aqui ele e um `bool` na
/// <see cref="Stats.Fighter"/>, do lado do `aged_out`, e pelo mesmo motivo: e uma marca da ALMA.
///
/// **E ELA NUNCA SE ESQUECE.** `if(godtongue) return 1` (:38) e a PRIMEIRA linha do
/// `godtongue_check`, antes de qualquer reavaliacao -- perder o cargo divino NAO tira a lingua. Por
/// isso <see cref="Reavaliar"/> recebe o valor atual e so sabe LIGAR: nao existe caminho que desligue,
/// e essa ausencia e a regra.
/// ==========================================================================================
///
/// ============================ SAO **DOIS** EIXOS, E ESSA E A ARMADILHA NOMEADA ============================
/// A Fase 0 avisou por escrito que ligar um e esquecer o outro e o defeito que este repo mais paga
/// ("regra ligada em UM chamador e esquecida no outro"). Os dois:
///
///   * **CARGO** -- `divine_rank_now()` (:28-34), onze cargos comparados por identidade;
///   * **SANGUE** -- `godtongue_check()` (:39), `Race` OU `Parent_Race` igual a "Kai" ou "Demigod".
///
/// As duas metades estao neste arquivo, num metodo so (<see cref="Reavaliar"/>), justamente pra que
/// nao haja como chamar uma sem a outra. A bancada exige as duas separadamente.
/// ======================================================================================================
/// </summary>
public static class LinguaDosDeuses
{
	// =====================================================================
	// EIXO 1 -- O CARGO
	// =====================================================================
	/// <summary>
	/// OS ONZE CARGOS DIVINOS, **pelos nomes GLOBAIS DO DM** -- a lista literal do
	/// `divine_rank_now` (`WishTable.dm:30-33`), na ordem em que ele a escreve.
	///
	/// ============================ POR QUE O NOME DO DM, E NAO A CHAVE DO PORT ============================
	/// A tabela de cargos deste port (<see cref="Cargos"/>) guarda os dois: a `Chave` interna
	/// ("guardian", "nkai") e o `Global`, que e exatamente o nome da variavel global do BYOND. Escrever
	/// aqui a lista de CHAVES seria transcrever a traducao a mao e perder o vinculo com a fonte -- e no
	/// dia em que alguem renomeasse uma chave, esta lista continuaria compilando e o cargo sairia da
	/// lingua **calado**.
	///
	/// Escrevendo os GLOBAIS e traduzindo em <see cref="ChavesDivinas"/>, a fonte da verdade continua
	/// sendo o DM e a traducao vira uma consulta que **pode falhar em voz alta**: a bancada exige que os
	/// onze resolvam pra um cargo existente, e um global orfao aparece como falha em vez de silencio.
	/// ================================================================================================
	///
	/// QUEM FICA DE FORA, DE PROPOSITO: Namekian_Elder, Demon_Lord, King_Of_Hell, King_of_Vegeta,
	/// President, Turtle/Crane -- e o **Aprendiz de Kaioshin**, cuja exclusao esta escrita como decisao
	/// de projeto no proprio original (`KaioshinApprentice.dm:19-20`: *"De proposito FORA ... da Lingua
	/// dos Deuses -- godtongue e IRREVERSIVEL e so de rank divino"*).
	/// </summary>
	public static readonly string[] GlobaisDivinos =
	[
		"Earth_Guardian", "Assistant_Guardian",
		"North_Kai", "South_Kai", "East_Kai", "West_Kai",
		"Grand_Kai", "Supreme_Kai", "King_Yemma",
		"Angel_Rank", "God_Of_Destruction",
	];

	/// <summary>
	/// AS CHAVES DE CARGO DESTE PORT que correspondem aos onze globais acima.
	///
	/// Um global que nao existir na tabela de cargos **sai da lista** em vez de virar uma chave
	/// inventada -- e por isso <see cref="GlobaisSemCargo"/> existe: e a metade que reclama.
	/// </summary>
	public static IEnumerable<string> ChavesDivinas =>
		GlobaisDivinos
			.Select(g => Array.Find(Cargos.Todos,
				r => string.Equals(r.Global, g, StringComparison.OrdinalIgnoreCase)))
			.Where(r => r != null)
			.Select(r => r!.Chave);

	/// <summary>
	/// OS GLOBAIS DO DM QUE **NAO** ACHARAM CARGO NESTE PORT. Vazio e o estado saudavel.
	///
	/// Existe porque a alternativa (traduzir e seguir em frente) e exatamente a forma silenciosa desta
	/// regra falhar: o cargo continua no jogo, o jogador continua sendo Kaio do Norte, e a lingua
	/// simplesmente nao chega nele. A bancada le isto.
	/// </summary>
	public static IEnumerable<string> GlobaisSemCargo =>
		GlobaisDivinos.Where(g => !Array.Exists(Cargos.Todos,
			r => string.Equals(r.Global, g, StringComparison.OrdinalIgnoreCase)));

	/// <summary>ESTE CARGO E DIVINO? -- o `divine_rank_now` traduzido pra chave deste port.</summary>
	public static bool CargoEhDivino(string chave) =>
		chave.Length > 0 && ChavesDivinas.Contains(chave, StringComparer.OrdinalIgnoreCase);

	// =====================================================================
	// EIXO 2 -- O SANGUE
	// =====================================================================
	/// <summary>
	/// AS DUAS RACAS QUE JA NASCEM FALANDO -- `Race == "Kai" || Parent_Race == "Kai" || Race ==
	/// "Demigod" || Parent_Race == "Demigod"` (`WishTable.dm:39`).
	///
	/// Sao as duas, e nao mais: o teste do original e pela RACA e nunca pela classe, entao um Demigod
	/// de classe "Ogre" ou "Genie" fala a lingua igual a um de classe divina.
	/// </summary>
	public static readonly string[] RacasDivinas = ["Kai", "Demigod"];

	/// <summary>
	/// O SANGUE FALA? Pergunta a raca **e a raca-mae** (o `Parent_Race`), porque o original pergunta as
	/// duas -- ignorar a segunda metade fecharia a lingua pra um meio-Kai que o DM aceita.
	/// </summary>
	public static bool SangueDivino(string raca, string racaMae) =>
		RacasDivinas.Contains(raca, StringComparer.OrdinalIgnoreCase)
		|| RacasDivinas.Contains(racaMae, StringComparer.OrdinalIgnoreCase);

	// =====================================================================
	// OS DOIS JUNTOS
	// =====================================================================
	/// <summary>
	/// **`godtongue_check()`** (`WishTable.dm:37-43`) -- devolve se esta alma fala a lingua DEPOIS
	/// desta avaliacao.
	///
	/// A ORDEM E A DO DM e ela importa: primeiro `if(godtongue) return 1` (quem ja sabe, sabe -- nem se
	/// olha o cargo), depois os dois eixos. Sem essa primeira linha, um Kaio destituido perderia a
	/// lingua e o comentario do original (*"rank divino atual ou PASSADO"*) seria mentira.
	///
	/// **ELA NAO DESLIGA NUNCA.** Devolver `false` aqui significa "ainda nao aprendeu", jamais
	/// "esqueceu": quem chama escreve o resultado por cima do campo, e como `ja == true` sai `true`, o
	/// campo so anda num sentido. E o que faz este metodo poder ser chamado em todo login sem risco.
	/// </summary>
	/// <param name="ja">O valor atual do campo na ficha.</param>
	/// <param name="cargo">A chave do cargo que esta alma carrega AGORA ("" = nenhum).</param>
	public static bool Reavaliar(bool ja, string cargo, string raca, string racaMae) =>
		ja || CargoEhDivino(cargo) || SangueDivino(raca, racaMae);

	/// <summary>
	/// A FRASE DE QUANDO SE APRENDE -- `to_chat` de `WishTable.dm:41`, traduzida.
	///
	/// Ela e uma constante e nao um literal solto porque sai de dois lugares (o login e a coroacao) e
	/// duas copias de um texto sao duas copias que divergem.
	/// </summary>
	public const string FraseAoAprender =
		"Você compreende a LÍNGUA DOS DEUSES. O que se aprende do divino jamais se esquece.";

	/// <summary>
	/// A RECUSA -- `ProceduralSpace.dm:1588`, literal ate no fecho.
	///
	/// ============================ E A RECUSA QUE ENSINA O PROCURADOR ============================
	/// A ultima frase (*"Ou TRANSFIRA as esferas pra alguem que fale"*) e a UNICA porta pelo qual um
	/// jogador descobre que o procurador existe -- nao ha tutorial, nao ha aba. Por isso a ordem das
	/// guardas no `ChamarOSuperShenron` e a do DM: **as sete primeiro, a lingua depois**. Invertida,
	/// quem nao fala levaria "você tem 3 de 7" e nunca leria esta linha.
	/// ======================================================================================
	/// </summary>
	public const string FraseDaRecusa =
		"Você grita para os céus... e NADA acontece. Só a LÍNGUA DOS DEUSES desperta o Super Shenron "
		+ "-- cargos divinos (atuais ou passados) a conhecem, e o sangue Kai ou Demigod nasce com ela. "
		+ "Ou TRANSFIRA as sete a alguém que fale (verbo Transferir Super Esferas).";
}
