using Jandirus.Core.Ranks;

namespace Jandirus.Core.Skills;

/// <summary>
/// O CENSO DAS FOLHAS -- quantas skills fazem alguma coisa, quantas so dao um verbo, e quantas
/// continuam mudas. E o relatorio que a especificacao pediu por escrito, e ele existe em codigo
/// (e nao numa planilha) por um motivo so: **numero que nao roda envelhece**.
///
/// ============================ O QUE ESTE ARQUIVO IMPEDE ============================
/// Este projeto ja perdeu 60 verbos por meses porque ninguem tinha como perguntar "o que o jogo
/// promete e nao entrega?". A skill era comprada, o chat dizia *"voce pode usar X"*, e X nao
/// existia. Nao havia bug: havia AUSENCIA, que e o defeito que nao aparece em teste nenhum.
///
/// A regra que este arquivo cria e uma so:
///
///     TODO VERBO QUE UMA SKILL (OU UM DEGRAU) CONCEDE TEM QUE ESTAR EM UMA DE TRES LISTAS.
///
///       1. PORTADO      -- <see cref="Tecnicas"/> conhece o id e o modo nao e `NaoPortada`;
///       2. OUTRO CANAL  -- o port atende por outro caminho (o `Fly` do DM e a tecla de voo
///                          daqui; o `Create_Blast` e a mesa de tecnicas customizadas). A tabela
///                          guarda QUAL canal, e a bancada confere que o canal existe de verdade;
///       3. ESPERANDO    -- ainda nao ha efeito, e a tabela diz de que SISTEMA ele depende.
///
/// O que nao estiver em nenhuma sai como <see cref="Situacao.SemCobertura"/>, e a bancada REPROVA.
/// Nao e perfeccionismo: divida catalogada e trabalho; divida invisivel e surpresa.
/// ==================================================================================
///
/// ============================ POR QUE MORA NO CORE ============================
/// O relatorio e lido por dois programas diferentes: a bancada do servidor (`--censoteste`, que
/// sobe o jogo inteiro) e o console do extrator (`dotnet run -- efeitos`, que NAO carrega Godot).
/// Uma segunda copia da conta divergiria no dia em que alguem portasse uma tecnica -- e ja
/// divergiu uma vez, quando o console respondia "5 portadas" com 28 prontas.
/// ==============================================================================
/// </summary>
public static class CensoDeSkills
{
	public enum Situacao
	{
		/// <summary>O <see cref="Tecnicas"/> tem o id com modo diferente de `NaoPortada`.</summary>
		Portada,

		/// <summary>O port atende por outro caminho -- ver <see cref="PorOutroCanal"/>.</summary>
		OutroCanal,

		/// <summary>Sem efeito, e o sistema de que depende esta escrito.</summary>
		Esperando,

		/// <summary>NAO ESTA EM LISTA NENHUMA. E o unico estado que reprova a bancada.</summary>
		SemCobertura,
	}

	/// <summary>Uma linha do censo: o verb, onde ele esta e quem o concede.</summary>
	public sealed class LinhaDeVerbo
	{
		public string Verbo = "";
		public Situacao Situacao;

		/// <summary>O canal que o atende, ou o sistema que ele espera. Vazio se portado.</summary>
		public string Onde = "";

		/// <summary>Quantas skills concedem este verb, e quantas o concedem por DEGRAU de nivel.</summary>
		public int PorSkill, PorDegrau;

		/// <summary>Algum cargo entrega uma skill que concede este verb?</summary>
		public bool DeCargo;
	}

	// =====================================================================
	// TABELA 2 -- OS ATENDIDOS POR OUTRO CANAL
	// =====================================================================
	/// <summary>
	/// VERB DO DM -> O CANAL DO PORT QUE FAZ A MESMA COISA.
	///
	/// O VALOR NAO E DECORATIVO: a bancada procura cada um destes ids no roteador de verbos do
	/// servidor e reprova se ele sumiu. E o que impede esta tabela de virar uma lista de desculpas
	/// -- dizer "isso e atendido pelo voar" so vale enquanto o `voar` existir.
	///
	/// Cada linha diz por que o nome mudou, porque "mudou de nome" e a explicacao que mais some.
	/// </summary>
	public static readonly Dictionary<string, string> PorOutroCanal = new(StringComparer.OrdinalIgnoreCase)
	{
		// O voo do port nao e um verb comprado: e a tecla, e ela vale pra quem tem `flightability`.
		["Fly"] = "voar",

		// `Superflight` era um verb de MARCHA no DM. O dono cortou o botao: virou o SHIFT enquanto
		// voa, o mesmo gesto de correr no chao. Ver `GameServer.Voo.MarchaDeVoo`.
		["Superflight"] = "voar",

		["ApeshitRevert"] = "oozaru",
		["ApeshitSetting"] = "oozaru_olhar",

		// O Zanzoken do port e reflexo, e nao verb: chega pelo opcode proprio quando o jogador
		// aperta a esquiva. Ver `GameServer.Zanzoken.cs`.
		["Zanzoken_Dodge"] = "C2S.Zanzoken",

		// Os atalhos configuraveis do Kaio-ken viraram sete botoes de multiplo fixo (lote G2). O canal
		// declarado e o verb-mae (`Kaioken`) e nao um dos sete: os sete nascem de um laco
		// (`Kaioken_{n}`) e nenhum deles existe como literal no fonte -- apontar pra um seria apontar
		// pra uma string que a bancada nao consegue conferir.
		["Kaioken_Settings"] = "Kaioken",

		// A mesa de tecnicas customizadas (`GameServer.Customizadas.cs`) e o `Create_Blast` /
		// `Create_Beam` do DM, com o mesmo orcamento de pontos e mais tela.
		["Create_Blast"] = "ca_criar",
		["Create_Beam"] = "ca_criar",
		["Remove_Blast"] = "ca_esquecer",
		["Remove_Beam"] = "ca_esquecer",

		// Os tres verbs de cargo que ja tem corpo, so que com outro nome (camada das portas).
		["Appoint_Elder"] = "cargo_nomear",
		["RankChat"] = "cargo_falar",
		["Permission"] = "sala_autorizar",

		// AS DUAS FORMAS QUE SAO DEGRAU DA ESCADA, e nao tecnica: no DM cada forma tem o proprio
		// verb; aqui existe UM canal de transformacao e um catalogo de 33 formas. O verb sumiu
		// porque a escada o substituiu, e nao porque a forma falta.
		["Mystic"] = "forma:mistico",
		["Wrathful_State"] = "forma:wrathful",

		// ============================ A ABSORCAO DO BIO-ANDROIDE SAIU DA LISTA DE ESPERA ============================
		// `Bio_Absorb` (o verb do `obj/Bio_Absorb`, que a arvore racial concede) estava na tabela 3
		// dizendo que esperava "o sistema de absorcao de corpo". Ele nao espera mais: o sistema E o
		// `GameServer.Absorcao.cs`, e a vitima morre, o `AbsorbBP` cresce e a escada de degraus do bio
		// anda por ele.
		//
		// ENTRA AQUI E NAO NA TABELA 1 porque no port ele nao e uma TECNICA (nao esta no `Tecnicas`,
		// nao tem custo de Ki, nao tem projetil): e um verb do CORPO, pelo canal de habilidade -- a
		// mesma prateleira do `oozaru` e do `regenerar` logo acima.
		//
		// **AS TRES IRMAS FICARAM NA ESPERA, E DE PROPOSITO**: `Absorb_Android` (o `Energy_Drain` do
		// androide de raca nativa), `Buu_Absorb` (o selo do Majin, com dimensao interna e expel) e
		// `Soul_Absorb` sao mecanicas DIFERENTES -- o bio CONSOME, o Majin SELA --, e dar baixa nelas
		// aqui seria repetir o defeito que esta tabela existe pra impedir: dizer que um sistema
		// chegou quando chegou outro.
		["Bio_Absorb"] = "absorver",

		// O `Absorb_Ki` do androide de absorcao (`DNALabs.dm:213`). Ele nao vem de skill nenhuma no
		// DM (a CONVERSAO o concede com `assignverb`), entao ele so aparece neste censo se alguem o
		// pendurar numa arvore -- e a linha esta aqui pra esse dia, com o canal ja apontado.
		["Absorb_Ki"] = "absorver_ki",

		// ============================ A ESTATUA DO DRAGAO SAIU DA LISTA DE ESPERA ============================
		// `Create_Dragon_Statue` (namekian.dm:75-109) estava na tabela 3 dizendo que esperava
		// *"esferas do dragao: fragmento, radar e estatua"*. **Ele nao espera mais**: o motor e o
		// `GameServer.Esferas.cs` -- a estatua se ergue, as sete nascem, se espalham por semente, se
		// pegam do chao e o dragao sobe.
		//
		// ENTRA AQUI E NAO NA TABELA 1 pelo mesmo argumento do `Bio_Absorb` logo acima: no port ele nao
		// e uma TECNICA (nao esta no `Tecnicas`, nao custa Ki, nao tem projetil) -- e um verb do canal
		// de verbos, `db_estatua`.
		//
		// **O QUE FALTA NAO E ESTE VERB**: a skill `/datum/skill/rank/MakeDragonballs` da arvore racial
		// Namek ainda nao existe neste port, entao o portao hoje e RACA + CLASSE + territorio (as tres
		// perguntas que o proprio verb do DM faz). No dia em que a arvore fechar, o `effector()` dela
		// gateia pela MESMA `conq_owns_any` que o verb ja consulta -- nada aqui muda.
		["Create_Dragon_Statue"] = "db_estatua",
	};

	// =====================================================================
	// TABELA 3 -- O QUE CADA VERB MUDO ESPERA
	// =====================================================================
	/// <summary>
	/// VERB -> SISTEMA DE QUE ELE DEPENDE. O agrupamento e o produto: dezenove folhas soltas nao
	/// dizem nada, mas *"vinte e nove verbos esperam o mesmo motor de combo"* diz onde esta o
	/// proximo lote inteiro.
	/// </summary>
	public static readonly Dictionary<string, string> Esperando = new(StringComparer.OrdinalIgnoreCase)
	{
		// ============================ ESTE GRUPO TINHA 29 E FICOU COM 15 ============================
		// O lote G7 fechou catorze deles (os tres combos de boxe, os tres chutes, as quatro artes
		// marciais, o Lariat e os dois do assassino) -- e as catorze linhas continuaram AQUI, dizendo
		// que eles esperavam um sistema que ja tinha chegado.
		//
		// A contradicao nao mudava nenhum numero do relatorio (o `Levantar` pergunta ao `Tecnicas`
		// PRIMEIRO, entao eles saiam como portados de qualquer jeito) -- e e exatamente por isso que
		// ela sobreviveu: uma tabela errada que nao muda nenhuma conta nao tem como ser notada. O que
		// ela estragava era a unica coisa que esta tabela serve pra dizer: *de que sistema o proximo
		// lote depende*. Quem lesse "29 verbos esperam o motor de combos" iria portar catorze verbos
		// ja portados.
		//
		// A `--catalogoteste` agora afirma que nenhum verb esta nas duas listas ao mesmo tempo. Ver
		// `Contradicoes`.
		// ==========================================================================================
		["Clench"] = SistCombo, ["Flip"] = SistCombo, ["Gigantic_Spike"] = SistCombo,
		["Hokuto_Hyakuretsu_Ken"] = SistCombo, ["Hold"] = SistCombo, ["Power_Drag"] = SistCombo,
		["Power_Slam"] = SistCombo, ["Precise_Explosion"] = SistCombo, ["Revenge_Demon"] = SistCombo,
		["Reverb"] = SistCombo, ["Seismic_Press"] = SistCombo, ["Shock"] = SistCombo,
		["Sneak"] = SistCombo, ["Suplex"] = SistCombo, ["Trip"] = SistCombo,

		// ---- 7 ----
		// O `Bio_Absorb` SAIU daqui pro `PorOutroCanal` -- ver a nota la. As tres irmas ficaram, e a
		// razao esta escrita junto: o bio CONSOME e o Majin SELA; sao motores diferentes.
		["Absorb_Android"] = SistAbsorcao, ["Buu_Absorb"] = SistAbsorcao,
		["Soul_Absorb"] = SistAbsorcao, ["Imitation"] = SistAbsorcao, ["Permanent_Imitation"] = SistAbsorcao,
		["BodyswapOBJ"] = SistAbsorcao,

		// ============================ ERAM 7, VIRARAM 2 -- E CINCO DELES NUNCA PRECISARAM DESTE SISTEMA ============================
		// O lote G8 portou `Dead`, `Keep_Body`, `Go_To_Heaven_Or_Hell`, `Restore_Youth` e
		// `Holy_Shortcut`, e a descoberta que os destravou foi ler o corpo dos verbs no DM em vez de
		// confiar nesta tabela: **nenhum dos cinco depende de "burocracia do alem"**.
		//
		//   * `Go_To_Heaven_Or_Hell` e um `switch` de dois destinos e as duas zonas ja existiam
		//     (`OtherworldRankSkills.dm:188-193`). A `desc` da skill `Judge` promete julgar alguem; o
		//     verb so teleporta o proprio Yemma;
		//   * `Holy_Shortcut` e um teleporte de ida e volta que cobra metade do Ki (`SpaceRanks.dm:59`);
		//   * `Dead` e um `for` que imprime quem esta morto (`:212-215`);
		//   * `Restore_Youth` escreve dois campos de idade depois de um "Sim" (`:163-174`);
		//   * `Keep_Body` liga um bit que este port ja citava em tres comentarios esperando o dia.
		//
		// **UMA ETIQUETA ERRADA E MAIS CARA QUE UMA PENDENCIA**, e este bloco e a prova: enquanto ela
		// dizia "falta o sistema X", ninguem tinha motivo pra abrir o arquivo do DM -- e os cinco
		// ficaram mudos por lotes inteiros ao lado de tecnicas muito mais caras que foram portadas.
		// Ver o cabecalho de `Server/GameServer.Tecnicas.G8.cs`.
		//
		// OS DOIS QUE FICAM PRECISAM MESMO: `Reincarnate_Mob` chama o `Reincarnate()` do original
		// (apaga a ficha e devolve a pessoa ao mundo dos vivos como outra pessoa) e `KaiPermission`
		// depende do Kai Loft / Hallowed Realm, que sao permissao de LUGAR e nao existem aqui.
		// ==========================================================================================================================
		["Reincarnate_Mob"] = SistAlem, ["KaiPermission"] = SistAlem,

		// ---- 7 ----
		["Word_Power"] = SistMagia, ["Watagashi"] = SistMagia, ["Void_Shout"] = SistMagia,
		["Psycho_Thread"] = SistMagia, ["Purification"] = SistMagia, ["Erasure"] = SistMagia,
		["Shackle"] = SistMagia,

		// ============================ ERAM 7 E VIRARAM 8, E O OITAVO E UMA DIVIDA QUE ATE ONTEM NAO EXISTIA ============================
		// O `Seal_Mob` (skill `Superior Seal`, do Guardiao da Terra, do Assistente e do Guardiao
		// Arconiano) NUNCA ESTEVE NESTA TABELA -- nao porque alguem o esqueceu, mas porque o extrator
		// perdia o `after_learn()` dele: a skill saia do `skills.json` com `verbos: []` e o censo a
		// classificava como "folha muda de nascenca". Ou seja, ela nao era divida NAO CATALOGADA, era
		// divida INVISIVEL, que e pior: o painel do cargo a anunciava como ENTREGUE.
		//
		// **E ele e o unico dos tres selos que NAO da pra portar hoje**, e a razao e concreta: os
		// outros dois chamam `SealMob()` direto, e este monta um `/obj/Ritual`, zera o `Magic` e o
		// `Ki` do lancador convertendo os dois em potencia de ritual, e delega a
		// `do_tietary("e_seal_s")`. A forca do selo la e `((Magic - alvo.Magic) * magnitude) + log(...)`
		// (`Rituals_Manipulation.dm:327-343`) -- uma disputa de MANA, nao de BP. Deste port o sistema
		// de magia tem so o TETO (`Fighter.MagicCap`): nao ha pool corrente, nao ha ritual, nao ha
		// palavra de poder. Ver o cabecalho de `Server/GameServer.Selo.cs`.
		// ==========================================================================================================================
		["Seal_Mob"] = SistMagia,

		// ---- 6 ----
		["Afterimage_Toggle"] = SistZanzo, ["Zanzoken_Afterimage"] = SistZanzo,
		["Zanzoken_Combo"] = SistZanzo, ["Zanzoken_Dash"] = SistZanzo,
		["Zanzoken_Rush"] = SistZanzo, ["Rapid_Movement"] = SistZanzo,

		// ---- 6 ----
		["Earth_Taxes"] = SistImposto, ["Vegeta_Taxes"] = SistImposto,
		["Collect_Earth_Taxes"] = SistImposto, ["Collect_Vegeta_Taxes"] = SistImposto,
		["Exempt_Earth_Taxes"] = SistImposto, ["Exempt_Vegeta_Taxes"] = SistImposto,

		// ---- 5 ----
		["Focus_Skill"] = SistEstudo, ["Study_Other"] = SistEstudo, ["Observe"] = SistEstudo,
		["Ki_Targets"] = SistEstudo, ["Write_Teachings"] = SistEstudo,

		// ---- 3 e 3 e 3 e 3 ----
		["Freeze"] = SistTempo, ["Stop"] = SistTempo, ["Tele_Stop"] = SistTempo,
		["SplitForm"] = SistDividido, ["Expand_Body"] = SistDividido, ["Devil_Bringer"] = SistDividido,
		["Equip_Tree"] = SistArvore, ["Throw_Tree"] = SistArvore, ["Pluck_Tree"] = SistArvore,
		["BusterBarrage"] = SistVolei, ["Continuous_Energy_Bullets"] = SistVolei, ["Spin_Blast"] = SistVolei,

		// ---- 2 e 2 e 2 e 2 e 2 ----
		["Instant_Transmission"] = SistTeleporte, ["Kai_Kai"] = SistTeleporte,

		// A `Scattering_Bullet` saiu daqui pelo lote G7 (a nuvem que converge de todos os angulos foi
		// portada como bolas de verdade). O `Energy_Wave_Volley` FICOU, e o motivo esta escrito no
		// proprio lote: sem o espalhamento de +-45 graus do `volley` ele vira um Ki Wave pior --
		// porta-lo assim seria entregar uma tecnica que nao e ela.
		["Energy_Wave_Volley"] = SistTrem,
		["Namekian_Fusion"] = SistFusao, ["Fusion_dance"] = SistFusao,

		// ============================ O `SistSigilo` FECHOU INTEIRO, E ERAM SO ESTES DOIS ============================
		// `Conceal_Power` e `Power_Control` MORAVAM AQUI, com a etiqueta *"sigilo de poder pelo lado
		// de quem ESCONDE (o port so tem o de quem LE)"* -- e ela estava certa, o que nesta tabela e
		// raro o bastante pra merecer nota. O lado de quem le ja existia inteiro (`isconcealed` trava
		// o BP expresso em 5, `powerMod` entra na conta do BP); faltavam os dois BOTOES que escrevem
		// esses campos, e eles sao um alterna-estado com trava de 5 s e uma atribuicao.
		//
		// O `Power_Control` ainda destravou um estagio INTEIRO da tecla C que era inalcancavel:
		// `EstagioDaCarga.Retomando` existe pra trazer o `powerMod` de volta a 1, e nada no port
		// jamais o baixava. Ver `Server/GameServer.Tecnicas.G9.cs`.
		//
		// A CONSTANTE `SistSigilo` FOI DELETADA junto, e nao esquecida aqui "por via das duvidas": um
		// grupo sem nenhum verb dentro sai do relatorio como uma linha de zero, que e ruido.
		// ==========================================================================================================
		["Nomear_Aprendiz"] = SistPatronato, ["Dispensar_Aprendiz"] = SistPatronato,
		// O `Detect_Shard` SAIU daqui pelo lote G8, e este e o caso mais gritante da tabela inteira:
		// ele estava marcado como dependente do sistema das esferas do dragao, e o corpo do verb no DM
		// e **uma linha de texto** -- *"The Master Emerald is no more; there is nothing to detect."*
		// (`ordered/SpaceRanks.dm:110-112`). Nao ha radar nenhum; a `desc` descreve um que o autor
		// nunca escreveu, e a piada e essa.
		// ============================ O `SistEsferas` FICOU SEM NINGUEM, E ISSO E O PONTO ============================
		// `Create_Dragon_Statue` era o UNICO verb desta linha, e ele mudou de tabela: o motor das
		// esferas existe (`GameServer.Esferas.cs`) e ele e atendido por `db_estatua`. Ver a entrada
		// nova no <see cref="PorOutroCanal"/>.
		//
		// A CONSTANTE FOI DELETADA JUNTO, e nao deixada "pro caso de". Uma etiqueta de sistema sem
		// nenhum verb dentro sai do relatorio como uma linha de zero -- que e o ruido que o comentario
		// vinte linhas acima ja proibe -- e, pior, faria o proximo leitor achar que ainda ha verb
		// esperando esferas. Regra da casa: codigo substituido se DELETA.
		// =========================================================================================================

		// ---- e os que sao um sistema cada, com o nome do sistema no lugar do grupo ----
		["Death_Ball"] = "carga de BOLA segurada (o `Canalizar` so carrega RAIO)",
		["Self_Destruct"] = "autodestruicao: o corpo vira a explosao, e o agarrao que a acompanha",
		["Give_Power"] = "doacao de BP entre dois corpos",
		["Majin"] = "majinizacao: a marca do M em outro jogador, com dono e reversao",
		["Golden_Form"] = "a camada Golden do Frost Demon (god ki temporario por cima da evolucao)",
		["Unlock_Potential"] = "despertar de potencial (o ritual do Ancia~o, uma vez por vida)",
		["Grow_Senzu_Bean"] = "plantio: semente que vira item com o tempo",
		["SpiritBomb"] = "genkidama: energia EMPRESTADA de quem esta em volta",
		["Narrate"] = "ferramentas de narrador (o verb e do `mob/Admin1`, nao do jogador)",
		["Acid_Spit"] = "veneno: dano ao longo do tempo carregado por um tiro",
	};

	// OS NOMES DOS GRUPOS, em constante e nao em string solta: eles aparecem em ate vinte e nove
	// linhas cada, e um erro de digitacao criaria um grupo fantasma com um verb dentro.
	private const string SistCombo = "motor de combos de estilo marcial";
	private const string SistAbsorcao = "absorcao de corpo (comer alguem e ficar com o que era dele)";
	private const string SistAlem = "burocracia do alem (corpo guardado, julgamento, reencarnacao)";
	private const string SistMagia = "magia: mana, palavra de poder e ritual";
	private const string SistZanzo = "zanzoken avancado (o port so tem a esquiva reflexa)";
	private const string SistImposto = "impostos: cofre que TIRA dinheiro de quem mora no planeta";
	private const string SistEstudo = "percepcao e estudo de Ki (aprender vendo o outro usar)";
	private const string SistTempo = "parar o tempo";
	private const string SistDividido = "corpo dividido / mudado de tamanho";
	private const string SistArvore = "arvore-arma (arrancar cenario e usar como arma)";
	private const string SistVolei = "barragem SUSTENTADA (aperta, segura, jorra ate soltar)";
	private const string SistTrem = "raio em TREM DE SEGMENTOS (o port colapsou o raio num objeto so)";
	private const string SistTeleporte = "teleporte por assinatura de Ki";
	private const string SistFusao = "fusao de dois corpos num so";
	private const string SistPatronato = "patronato de Kaishin (aprendiz com varios donos)";

	// =====================================================================
	// AS TRES LISTAS NAO PODEM SE CONTRADIZER
	// =====================================================================
	/// <summary>
	/// UM VERB EM DUAS LISTAS AO MESMO TEMPO -- e o defeito que o `Levantar` NAO tem como acusar.
	///
	/// ============================ POR QUE ELE E INVISIVEL SEM ISTO ============================
	/// A classificacao do `Levantar` e uma ESCADA: pergunta ao `Tecnicas` primeiro, ao
	/// <see cref="PorOutroCanal"/> depois, ao <see cref="Esperando"/> por ultimo. Entao um verb
	/// portado que continue escrito no `Esperando` sai do relatorio como PORTADO -- todos os numeros
	/// batem, a soma fecha, a cobertura da zero, e a tabela mente em silencio.
	///
	/// Foi o que aconteceu com os dezesseis do lote G7: catorze deles ficaram uma sessao inteira
	/// declarando que esperavam *"o motor de combos de estilo marcial"*, que e a informacao com que
	/// alguem escolhe o proximo lote. Quem lesse o relatorio veria 29 verbos pedindo um motor que ja
	/// existia -- e portaria catorze verbos ja portados.
	///
	/// Por isso esta funcao e PURA e recebe as tres listas: e o que permite a bancada INJETAR a
	/// contradicao (chamar com um trio sintetico) em vez de so afirmar sobre o trio de verdade.
	/// ======================================================================================
	/// </summary>
	/// <returns>Uma linha por contradicao, ja legivel -- vazio quando as tres listas concordam.</returns>
	public static List<string> Contradicoes(
		IEnumerable<string> vivos,
		IReadOnlyDictionary<string, string> porCanal,
		IReadOnlyDictionary<string, string> esperando)
	{
		var v = new HashSet<string>(vivos, StringComparer.OrdinalIgnoreCase);
		var achados = new List<string>();

		foreach (string id in v)
		{
			if (esperando.ContainsKey(id))
				achados.Add($"{id}: TEM CORPO e mesmo assim consta como esperando '{esperando[id]}'");
			if (porCanal.ContainsKey(id))
				achados.Add($"{id}: TEM CORPO e mesmo assim consta como atendido por '{porCanal[id]}'");
		}

		foreach (string id in porCanal.Keys)
			if (esperando.ContainsKey(id))
				achados.Add($"{id}: esta no canal '{porCanal[id]}' E na fila de '{esperando[id]}'");

		achados.Sort(StringComparer.OrdinalIgnoreCase);
		return achados;
	}

	// =====================================================================
	// O LEVANTAMENTO
	// =====================================================================
	public sealed class Relatorio
	{
		/// <summary>
		/// QUANTAS TECNICAS TEM CORPO NESTE PORT -- o numero que ANDA, e o unico que responde a
		/// pergunta que abriu esta frente ("34 botoes de 94 faziam algo").
		///
		/// Nao confundir com <see cref="VerbosPortados"/>: aquele conta os verbos que **alguma skill
		/// concede** e que tem efeito, e depende de quem chamou passar (ou nao) os verbos de degrau --
		/// o console do extrator nao os passa, o servidor passa, e os dois numeros sao legitimamente
		/// diferentes. Este aqui e o mesmo nas duas bocas: e o tamanho da tabela de efeitos.
		/// </summary>
		public int ComCorpo;

		public int Folhas, ComEfeitoPassivo, SoVerbo, PassivoEVerbo, Mudas;
		public int VerbosTotal, VerbosPortados, VerbosPorCanal, VerbosEsperando, VerbosSemCobertura;
		public int VerbosDeCargo, VerbosDeCargoVivos;
		public int SkillsDeCargo;
		public List<LinhaDeVerbo> Verbos = [];

		/// <summary>Sistema -> quantos verbos o esperam. Ordenado do maior pro menor.</summary>
		public List<(string Sistema, int Quantos)> PorSistema = [];

		/// <summary>Os que nao estao em lista nenhuma. Vazio = a divida esta toda catalogada.</summary>
		public List<string> SemCobertura = [];
	}

	/// <summary>
	/// FAZ A CONTA. <paramref name="verbosDeDegrau"/> sao os verbos que um DEGRAU de nivel concede
	/// (`niveis.json`) -- eles NAO estao no `Skill.Verbos` e ja sumiram do radar uma vez: dois dos
	/// tres verbos de tiro do jogo sao concedidos por degrau, e o menu inteiro os ignorava.
	/// </summary>
	public static Relatorio Levantar(SkillCatalog cat, IEnumerable<string>? verbosDeDegrau = null)
	{
		var r = new Relatorio { ComCorpo = Tecnicas.Portadas };
		var porVerbo = new Dictionary<string, LinhaDeVerbo>(StringComparer.OrdinalIgnoreCase);

		LinhaDeVerbo Linha(string v)
		{
			if (porVerbo.TryGetValue(v, out LinhaDeVerbo? l)) return l;
			return porVerbo[v] = new LinhaDeVerbo { Verbo = v };
		}

		// AS SKILLS QUE ALGUM CARGO ENTREGA -- o recorte que a camada das portas mediu e que este
		// censo mantem vivo: um kit de cargo cheio de verb mudo e uma promessa que o jogo faz.
		var deCargo = new HashSet<string>(DadivaDeCargo.Todas, StringComparer.OrdinalIgnoreCase);

		foreach (Skill s in cat.Todas)
		{
			if (s.Arvore) continue;
			r.Folhas++;

			// A ESCOLHA UNICA CONTA COMO EFEITO PASSIVO. O que este censo mede e se o PORT sabe o
			// que a skill faz -- nao se algum personagem ja decidiu. Enquanto ela nao contava, a
			// `Great Robotic Alliance` ficava na coluna das mudas junto com as skills cujo efeito
			// ninguem leu, e as duas coisas pedem trabalho oposto: uma precisa de sistema, a outra
			// so precisa que alguem responda a pergunta.
			bool passivo = s.Buffs.Count > 0 || s.Mults.Count > 0 || s.Genes.Count > 0
						   || s.Flags.Count > 0 || s.Estilo.Length > 0 || s.Escolhas.Length > 0;
			bool verbo = s.Verbos.Length > 0;

			if (passivo && verbo) r.PassivoEVerbo++;
			else if (passivo) r.ComEfeitoPassivo++;
			else if (verbo) r.SoVerbo++;
			else r.Mudas++;

			bool doCargo = deCargo.Contains(s.Path);
			if (doCargo) r.SkillsDeCargo++;

			foreach (string v in s.Verbos)
			{
				LinhaDeVerbo l = Linha(v);
				l.PorSkill++;
				l.DeCargo |= doCargo;
			}
		}

		foreach (string v in verbosDeDegrau ?? []) Linha(v).PorDegrau++;

		foreach (LinhaDeVerbo l in porVerbo.Values)
		{
			l.Situacao =
				Tecnicas.Get(l.Verbo) is { Modo: not Modo.NaoPortada } ? Situacao.Portada
				: PorOutroCanal.ContainsKey(l.Verbo) ? Situacao.OutroCanal
				: Esperando.ContainsKey(l.Verbo) ? Situacao.Esperando
				: Situacao.SemCobertura;

			l.Onde = l.Situacao switch
			{
				Situacao.OutroCanal => PorOutroCanal[l.Verbo],
				Situacao.Esperando => Esperando[l.Verbo],
				_ => "",
			};
		}

		r.Verbos = [.. porVerbo.Values.OrderBy(l => l.Verbo, StringComparer.OrdinalIgnoreCase)];
		r.VerbosTotal = r.Verbos.Count;
		r.VerbosPortados = r.Verbos.Count(l => l.Situacao == Situacao.Portada);
		r.VerbosPorCanal = r.Verbos.Count(l => l.Situacao == Situacao.OutroCanal);
		r.VerbosEsperando = r.Verbos.Count(l => l.Situacao == Situacao.Esperando);
		r.VerbosSemCobertura = r.Verbos.Count(l => l.Situacao == Situacao.SemCobertura);
		r.SemCobertura = [.. r.Verbos.Where(l => l.Situacao == Situacao.SemCobertura).Select(l => l.Verbo)];

		r.VerbosDeCargo = r.Verbos.Count(l => l.DeCargo);
		r.VerbosDeCargoVivos = r.Verbos.Count(
			l => l.DeCargo && l.Situacao is Situacao.Portada or Situacao.OutroCanal);

		r.PorSistema = [.. r.Verbos.Where(l => l.Situacao == Situacao.Esperando)
			.GroupBy(l => l.Onde)
			.Select(g => (g.Key, g.Count()))
			.OrderByDescending(t => t.Item2).ThenBy(t => t.Key, StringComparer.Ordinal)];

		return r;
	}

	/// <summary>
	/// ESTA SKILL AINDA NAO FAZ NADA NESTE PORT? Devolve o SISTEMA que falta, ou `null` se ela faz.
	///
	/// ============================ POR QUE O CENSO PRECISAVA FALAR COM O JOGO ============================
	/// O censo nasceu como relatorio pra quem programa, e resolveu metade do problema: a divida
	/// virou lista. A outra metade e de quem JOGA -- e do lado dele nada mudou.
	///
	/// O caso e o do cargo. O Kaio do Norte assume, o mundo e avisado, o livro recebe a Genkidama...
	/// e nenhum botao aparece, porque o menu do cliente e desenhado a partir do
	/// <see cref="Tecnicas"/> e verbo mudo nao esta la. Nao ha erro, nao ha recusa, nao ha mensagem:
	/// ha uma promessa e o silencio. E o mesmo defeito que a regra 5 da casa nomeia ("silencio no
	/// lugar de erro"), so que na cara do jogador em vez de no log.
	///
	/// Uma skill so conta como muda quando **nenhum** dos verbos dela tem corpo E ela nao carrega
	/// numero nenhum: `Ritual_of_Might` nao da verb e mesmo assim faz efeito (e um buff), e chamar
	/// ela de muda seria mentir na outra direcao.
	/// ================================================================================================
	/// </summary>
	public static string? SistemaQueFalta(Skill s)
	{
		if (s.Arvore) return null;
		if (s.Buffs.Count > 0 || s.Mults.Count > 0 || s.Genes.Count > 0
			|| s.Flags.Count > 0 || s.Estilo.Length > 0 || s.Escolhas.Length > 0) return null;
		if (s.Verbos.Length == 0) return null;   // folha muda de nascenca: nao ha promessa a quebrar

		string? falta = null;
		foreach (string v in s.Verbos)
		{
			if (Tecnicas.Get(v) is { Modo: not Modo.NaoPortada }) return null;
			if (PorOutroCanal.ContainsKey(v)) return null;
			falta ??= Esperando.GetValueOrDefault(v);
		}
		return falta ?? "um sistema que este port ainda nao tem";
	}

	/// <summary>
	/// O RELATORIO EM TEXTO -- o mesmo em qualquer boca (bancada do servidor ou console do
	/// extrator). Uma linha por fato; nada aqui e calculado, so impresso.
	/// </summary>
	public static IEnumerable<string> Texto(Relatorio r)
	{
		// O NUMERO QUE ANDA, e ele vem PRIMEIRO: e a resposta a pergunta que abriu esta frente.
		// Escrito como marco -> hoje de proposito -- "85" sozinho nao diz se o jogo melhorou.
		int delta = r.ComCorpo - Tecnicas.MarcoDeVerbosComCorpo;
		yield return "================ O CATALOGO ANDANDO ================";
		yield return $"tecnicas COM CORPO: {Tecnicas.MarcoDeVerbosComCorpo} (marco) -> {r.ComCorpo} hoje "
				   + $"({(delta >= 0 ? "+" : "")}{delta})";
		yield return "";
		yield return "================ O CATALOGO DE SKILLS, POR EFEITO ================";
		yield return $"folhas (skills de verdade)              : {r.Folhas}";
		yield return $"  so numero no corpo (buff/gene/flag)   : {r.ComEfeitoPassivo}";
		yield return $"  numero E verbo                        : {r.PassivoEVerbo}";
		yield return $"  so destravam um verbo                 : {r.SoVerbo}";
		yield return $"  MUDAS (nada extraido do DM)           : {r.Mudas}";
		yield return "";
		yield return "================ OS VERBOS QUE O JOGO CONCEDE ================";
		yield return $"verbos distintos                        : {r.VerbosTotal}";
		yield return $"  com EFEITO portado                    : {r.VerbosPortados}";
		yield return $"  atendidos por OUTRO CANAL             : {r.VerbosPorCanal}";
		yield return $"  MUDOS, esperando um sistema           : {r.VerbosEsperando}";
		yield return $"  SEM COBERTURA (nem catalogados)       : {r.VerbosSemCobertura}";
		yield return "";
		yield return $"dos que algum CARGO entrega ({r.SkillsDeCargo} skills): "
				   + $"{r.VerbosDeCargo} verbos, {r.VerbosDeCargoVivos} com efeito";
		yield return "";
		yield return "================ O QUE FALTA, POR SISTEMA ================";
		foreach ((string sistema, int quantos) in r.PorSistema)
			yield return $"  {quantos,3}  {sistema}";

		if (r.SemCobertura.Count == 0) yield break;
		yield return "";
		yield return "!!! VERBOS SEM COBERTURA -- catalogue em CensoDeSkills.Esperando:";
		foreach (string v in r.SemCobertura) yield return $"    {v}";
	}
}
