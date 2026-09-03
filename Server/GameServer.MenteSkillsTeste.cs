using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--menteskills` -- AS DEZESSETE SKILLS DE "STRENGTH OF MIND", UMA A UMA.
///
/// ============================ AS DUAS COISAS QUE PARECEM UMA NA TELA DO JOGADOR ============================
/// O dono relatou *"as skills de strength of mind nao estao portadas, so o ki unlocked"*. Medindo,
/// eram DOIS defeitos diferentes com o mesmo sintoma, e separa-los e metade desta bancada:
///
///   (a) ALCANCE -- a skill aparece na aba e da pra comprar? As cinco `Basic_*` acendem por CONTADOR
///       (`kiawarenessskill>=1`, `Mind.dm:11-31`) e as dez `Advanced_*`/`Perfect_*` por DEGRAU (o
///       `enableskill` do nivel 100 da anterior, `:186`). As duas portas dependem de a skill anterior
///       SUBIR DE NIVEL;
///   (b) EFEITO -- comprar (ou subir) faz alguma coisa no corpo?
///
/// E o defeito de verdade era o elo entre as duas: **quinze das dezessete tinham ZERO fonte de exp
/// neste port**. As condicoes de ganho delas (`savant.studying`, `savant.kibuffon`, `savant.kiratio>1`,
/// `savant.Ki!=lastki&&diffki<0`, `(savant.Ki/savant.MaxKi)<0.9`, `else`...) caiam todas em "condicao
/// que o port nao sabe avaliar" no `RegrasDoDisco`, e regra descartada nao credita nada. Ou seja: as
/// cinco Basic eram compraveis e CONGELADAS no nivel 0, e como as Advanced/Perfect so acendem no
/// nivel 100 delas, as DEZ de cima eram inalcancaveis -- por defeito do port, nao por desenho do DM.
/// ========================================================================================================
///
/// ============================ O QUE ESTA BANCADA MEDE, E POR QUE CADA PARTE ============================
///   1. A TABELA DAS DEZESSETE, impressa: tier, custo, o que o DM faz, alcance e efeito. E o
///      relatorio -- ele nao afirma nada, mas e o unico jeito de alguem CONFERIR o resto;
///   2. AS SETE CONDICOES DE EXP novas, cada uma nas duas metades (sem o estado nao rende; com ele
///      rende, e o quanto o DM manda). Inclui a corrente `if/else`, que e o que impede o `else` de
///      creditar POR CIMA do ramo que ja rendeu;
///   3. O ALCANCE PELO FUNIL DE PRODUCAO -- `Aprender(pl, path)`, o mesmo que o `C2S.Aprender` chama:
///      um corpo recem-nascido so compra a Ki Unlocked; subindo ela, as cinco Basic acendem uma a uma
///      nos niveis do DM; subindo uma Basic ao 100, a Advanced dela acende; e assim ate a Perfect;
///   4. O EFEITO NOMEADO DE CADA UMA das dezessete, nas duas metades (antes de cruzar o degrau o
///      corpo nao tem; depois tem) -- buff, gene, chave, verb e skill concedida;
///   5. O SISTEMA DE ESTUDO (lote G13): estudar rende, focar decupla, o livro escreve e ensina;
///   6. O `kibuffon` e o `buffregen`: ligar o Foco muda a taxa de exp da Circulacao, e a Advanced no
///      nivel 30 passa a CURAR enquanto o buff estiver de pe.
/// ====================================================================================================
///
///     Godot --headless --path . --host --menteskills
/// </summary>
public partial class GameServer
{
	private int _mnOk, _mnFalhou;

	private void AfirmarMn(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _mnOk++; GD.Print($"[mente]   OK    {oque}"); return; }
		_mnFalhou++;
		GD.PrintErr($"[mente]   FALHA {oque}   {detalhe}");
	}

	private const string MnRaiz = "/datum/skill/mind/Ki_Unlocked";
	private const string MnArvore = "/datum/skill/tree/Mind";

	/// <summary>
	/// AS DEZESSETE, NA ORDEM EM QUE O DM AS DECLARA (`Mind.dm:6-15`) -- e nao em ordem alfabetica:
	/// a ordem do original e a ordem da PROGRESSAO (raiz, cinco Basic, cinco Advanced, a Targeted, as
	/// cinco Perfect), e ler a tabela na ordem da arvore e o que deixa a cadeia visivel.
	/// </summary>
	private static readonly string[] MnAsDezessete =
	[
		"Ki_Unlocked",
		"Basic_Ki_Awareness", "Basic_Ki_Circulation", "Basic_Ki_Control", "Basic_Ki_Efficiency", "Basic_Ki_Gathering",
		"Advanced_Ki_Awareness", "Advanced_Ki_Circulation", "Advanced_Ki_Control", "Advanced_Ki_Efficiency",
		"Advanced_Ki_Gathering", "Advanced_Targeted_Mastery",
		"Perfect_Ki_Awareness", "Perfect_Ki_Circulation", "Perfect_Ki_Control", "Perfect_Ki_Efficiency",
		"Perfect_Ki_Gathering",
	];

	private static string MnPath(string curto) => "/datum/skill/mind/" + curto;

	/// <summary>
	/// O TRIO DO LOGIN, e nao dois tercos dele: `Niveis.Aplicar` -> `AplicarPoderes` -> `AplicarEfeitos`
	/// (`GameServer.cs:3856-3859`).
	///
	/// ============================ SAO DOIS CANAIS, E O PRIMEIRO E O DOS DEGRAUS ============================
	/// `AplicarEfeitos` sozinho escreve so o que a COMPRA da (`EfeitosDeSkill`) e recalcula as arvores.
	/// O que o NIVEL da -- `kiawarenessskill += 1` a cada 5 niveis, o `canPower` do degrau 5, o
	/// `buffregen` do 30 -- e outro razao (`NiveisDeSkill.Aplicar`), e sem ele os contadores ficam em
	/// zero: nenhum acendedor da arvore da Mente dispara e nenhum buff de degrau chega ao corpo.
	///
	/// A primeira versao desta bancada chamava so o `AplicarEfeitos` e ficou vermelha em 36 linhas --
	/// medindo um corpo que o jogo nunca produz. Chamar o trio E o caminho de producao.
	/// ====================================================================================================
	/// </summary>
	private void MnLogin(ServerPlayer pl)
	{
		pl.Niveis.Aplicar(pl.Ficha);
		AplicarPoderes(pl);
		AplicarEfeitos(pl);
	}

	public void RodarBancadaDasSkillsDaMente()
	{
		_mnOk = _mnFalhou = 0;
		GD.Print("[mente] ================ AS DEZESSETE SKILLS DE 'STRENGTH OF MIND' ================");
		if (_skills == null) { AfirmarMn("o catalogo de skills carregou", false); return; }

		List<string>? escutaAnterior = EscutaDeAvisos;
		try
		{
			ATabelaDasDezessete();
			AsCondicoesDeExp();
			AsCorrentesDeElse();
			OAlcancePeloFunil();
			OEfeitoDeCadaUma();
			OSistemaDeEstudo();
			OBuffDeKiEACura();
		}
		catch (Exception e)
		{
			_mnFalhou++;
			GD.PrintErr($"[mente]   FALHA a bancada rodou inteira   {e}");
		}
		finally
		{
			EscutaDeAvisos = escutaAnterior;
			LimparTudoDaBancada();
		}

		GD.Print($"[mente] ================ {_mnOk} passaram, {_mnFalhou} falharam ================");
	}

	// =====================================================================
	// 1) A TABELA
	// =====================================================================
	/// <summary>
	/// O RELATORIO DAS DEZESSETE. Nao afirma -- IMPRIME. E o que responde "o que exatamente esta
	/// portado?" sem que ninguem precise ler cinco arquivos, e e a metade do pedido do dono que
	/// nenhuma asercao cobre.
	/// </summary>
	private void ATabelaDasDezessete()
	{
		GD.Print("[mente] -- 1) A TABELA DAS DEZESSETE (tier, custo, exp, degraus, verbos)");

		Skill? arv = _skills!.Get(MnArvore);
		AfirmarMn("a arvore 'Strength of Mind' existe no catalogo e tem 17 galhos",
				  arv is { Arvore: true } && arv.Galhos.Length == 17, $"{arv?.Galhos.Length}");
		AfirmarMn("...e ela e de TODO MUNDO (`skilltrees.json`: todos)",
				  _skills.ArvoresDe("Human", "None").Any(a => a.Path == MnArvore));

		GD.Print("[mente]    skill                       tier custo  exp  degraus  verbos concedidos");
		int semExp = 0;
		foreach (string curto in MnAsDezessete)
		{
			string path = MnPath(curto);
			Skill? s = _skills.Get(path);
			RegraDeNivel? r = RegrasDeNivel.Get(path);
			int fontes = (r?.PorEstado.Count ?? 0) + (r?.PorContador.Count ?? 0);
			if (fontes == 0) semExp++;
			var verbos = new List<string>();
			foreach (Degrau d in r?.Degraus ?? []) verbos.AddRange(d.Verbos);
			GD.Print($"[mente]    {s?.Nome,-27} {s?.Tier,3} {SkillCatalog.CustoDe(s!),5} {fontes,4} "
				   + $"{r?.Degraus.Length ?? 0,8}  {(verbos.Count > 0 ? string.Join(", ", verbos) : "-")}");
		}

		AfirmarMn("TODAS as dezessete tem pelo menos uma fonte de exp (antes deste lote eram 15 com ZERO)",
				  semExp == 0, $"{semExp} sem fonte");
	}

	// =====================================================================
	// 2) AS SETE CONDICOES DE EXP, NAS DUAS METADES
	// =====================================================================
	/// <summary>
	/// UM CORPO SO PRA MEDIR EXP: sem ficha o `KiSkillGains` nao aplica curva e as condicoes de Ki
	/// nao valem, entao a medida seria de outro motor. Este e o corpo de producao.
	/// </summary>
	private ServerPlayer MnCorpo(string nome, string[] skills, (string, int)[]? degraus = null)
	{
		ServerPlayer pl = ForjarComSkills(nome, CorredorLivre(4), bp: 50_000, skills: skills,
										  degraus: degraus, kiMin: 100_000);
		MnLogin(pl);   // o trio do login: o degrau escreve os contadores, a compra escreve os flags
		return pl;
	}

	/// <summary>Quanto de exp esta skill ganhou em <paramref name="tiques"/> tiques neste estado.</summary>
	private static double MnGanho(ServerPlayer pl, string path, NiveisDeSkill.EstadoDoCorpo corpo, int tiques = 10)
	{
		double antes = pl.Niveis.Exp(path);
		var rng = new Random(99);
		for (int i = 0; i < tiques; i++) pl.Niveis.Efetor(rng, _skillsEstatico!, pl.Livro!, corpo);
		return pl.Niveis.Exp(path) - antes;
	}

	/// <summary>O catalogo, pro <see cref="MnGanho"/> (estatico pra nao carregar o servidor inteiro).</summary>
	private static SkillCatalog? _skillsEstatico;

	private void AsCondicoesDeExp()
	{
		GD.Print("[mente] -- 2) AS SETE CONDICOES DE EXP NOVAS, cada uma nas DUAS metades");
		_skillsEstatico = _skills;

		// ---- studying: as tres de Percepcao (Mind.dm:189) ----
		{
			string path = MnPath("Basic_Ki_Awareness");
			ServerPlayer pl = MnCorpo("Percepcao", [MnRaiz, path], [(path, 20)]);
			double sem = MnGanho(pl, path, new NiveisDeSkill.EstadoDoCorpo(false, false, false, Ficha: pl.Ficha));
			double com = MnGanho(pl, path, new NiveisDeSkill.EstadoDoCorpo(false, false, false, Estudando: true, Ficha: pl.Ficha));
			AfirmarMn("ESTUDANDO (`savant.studying`) a Basic Ki Awareness rende MAIS que parada",
					  com > sem && sem > 0, $"parada {sem:0.##} -> estudando {com:0.##}");
		}

		// ---- observingnow: Advanced/Perfect de Percepcao (Mind.dm:461) ----
		{
			string path = MnPath("Advanced_Ki_Awareness");
			ServerPlayer pl = MnCorpo("Projetor", [MnRaiz, path], [(path, 20)]);
			double sem = MnGanho(pl, path, new NiveisDeSkill.EstadoDoCorpo(false, false, false, Ficha: pl.Ficha));
			double com = MnGanho(pl, path, new NiveisDeSkill.EstadoDoCorpo(false, false, false, Observando: true, Ficha: pl.Ficha));
			AfirmarMn("PROJETANDO A MENTE (`savant.observingnow`) a Advanced Ki Awareness rende 3x em vez de 1x",
					  Math.Abs(com - 3 * sem) < 1e-6 && sem > 0, $"parado {sem:0.##} -> observando {com:0.##}");
		}

		// ---- kibuffon: as tres de Circulacao (Mind.dm:243/513/726) ----
		{
			string path = MnPath("Advanced_Ki_Circulation");
			ServerPlayer pl = MnCorpo("Circulador", [MnRaiz, path], [(path, 20)]);
			double sem = MnGanho(pl, path, new NiveisDeSkill.EstadoDoCorpo(false, false, false, Ficha: pl.Ficha));
			double com = MnGanho(pl, path, new NiveisDeSkill.EstadoDoCorpo(false, false, false, ComBuffDeKi: true, Ficha: pl.Ficha));
			AfirmarMn("COM BUFF DE KI (`savant.kibuffon`) a Advanced Ki Circulation rende 2x em vez de 1x",
					  Math.Abs(com - 2 * sem) < 1e-6 && sem > 0, $"sem {sem:0.##} -> com {com:0.##}");
		}

		// ---- kiratio > 1: as tres de Controle, ESCALADAS pela propria razao (Mind.dm:296) ----
		{
			string path = MnPath("Advanced_Ki_Control");
			ServerPlayer pl = MnCorpo("Controlador", [MnRaiz, path], [(path, 20)]);
			pl.Ficha.kiratio = 1;
			double sem = MnGanho(pl, path, new NiveisDeSkill.EstadoDoCorpo(false, false, false, Ficha: pl.Ficha));
			pl.Ficha.kiratio = 4;
			double com = MnGanho(pl, path, new NiveisDeSkill.EstadoDoCorpo(false, false, false, Ficha: pl.Ficha));
			AfirmarMn("com o tanque ACIMA do cheio (`kiratio>1`) a Advanced Ki Control rende, e ESCALADA pela razao",
					  sem == 0 && com > 0, $"kiratio 1 -> {sem:0.##}; kiratio 4 -> {com:0.##}");

			// A ESCALA E O `1*savant.kiratio` do DM: o extrator so guarda o `1`, e a razao mora na condicao.
			pl.Ficha.kiratio = 8;
			double dobro = MnGanho(pl, path, new NiveisDeSkill.EstadoDoCorpo(false, false, false, Ficha: pl.Ficha));
			AfirmarMn("...e dobrar a razao DOBRA o ganho (`KiSkillGains(1*savant.kiratio)`)",
					  Math.Abs(dobro - 2 * com) < 1e-6, $"kiratio 4 -> {com:0.##}; kiratio 8 -> {dobro:0.##}");
		}

		// ---- Ki caiu desde o ultimo tique: as tres de Eficiencia (Mind.dm:352) ----
		{
			string path = MnPath("Basic_Ki_Efficiency");
			ServerPlayer pl = MnCorpo("Eficiente", [MnRaiz, path], [(path, 20)]);
			var corpo = new NiveisDeSkill.EstadoDoCorpo(false, false, false, Ficha: pl.Ficha);
			var rng = new Random(7);

			pl.Niveis.Efetor(rng, _skills!, pl.Livro!, corpo);   // marca o Ki de referencia
			double antes = pl.Niveis.Exp(path);
			pl.Niveis.Efetor(rng, _skills!, pl.Livro!, corpo);   // Ki nao mudou
			double parado = pl.Niveis.Exp(path) - antes;

			pl.Ficha.Ki -= 500;                                  // GASTOU
			antes = pl.Niveis.Exp(path);
			pl.Niveis.Efetor(rng, _skills!, pl.Livro!, corpo);
			double gastando = pl.Niveis.Exp(path) - antes;

			AfirmarMn("a Basic Ki Efficiency so rende no tique em que o Ki CAIU (`Ki!=lastki && diffki<0`)",
					  parado == 0 && gastando > 0, $"sem gastar {parado:0.##} -> gastando {gastando:0.##}");
		}

		// ---- tanque abaixo de 90%: as tres de Reserva, escaladas por (2 - fracao) (Mind.dm:412) ----
		{
			string path = MnPath("Basic_Ki_Gathering");
			ServerPlayer pl = MnCorpo("Reserva", [MnRaiz, path], [(path, 20)]);
			var corpo = new NiveisDeSkill.EstadoDoCorpo(false, false, false, Ficha: pl.Ficha);

			pl.Ficha.Ki = pl.Ficha.MaxKi;                      // 100% -- nao rende
			double cheio = MnGanho(pl, path, corpo);
			pl.Ficha.Ki = pl.Ficha.MaxKi * 0.5;                // 50%
			double meio = MnGanho(pl, path, corpo);
			pl.Ficha.Ki = 0;                                   // vazio
			double vazio = MnGanho(pl, path, corpo);

			AfirmarMn("a Basic Ki Gathering nao rende com o tanque CHEIO e rende com ele pela metade",
					  cheio == 0 && meio > 0, $"cheio {cheio:0.##} -> metade {meio:0.##}");
			AfirmarMn("...e quanto MAIS vazio, mais rende (`3*(2-fracao)`: 1,5x na metade, 2x vazio)",
					  Math.Abs(vazio / meio - 2 / 1.5) < 1e-3, $"metade {meio:0.##} -> vazio {vazio:0.##}");
		}

		// ---- deepmeditation: o ramo MORTO do DM, mantido a vista ----
		{
			string path = MnPath("Perfect_Ki_Gathering");
			RegraDeNivel? r = RegrasDeNivel.Get(path);
			AfirmarMn("o ramo `savant.deepmeditation` das tres de Reserva foi LIDO (nao caiu em 'condicao desconhecida')",
					  r != null && r.PorEstado.Exists(g => g.Quando == RegraDeNivel.Estado.MeditacaoProfunda));
			AfirmarMn("...e ele NUNCA vale, aqui como no DM (23 ocorrencias no `Code/`, nenhuma escreve 1)",
					  !MnDeepMeditationAlgumaVezLigada(), "o servidor passou `MeditacaoProfunda: true` pra alguem");
		}
	}

	/// <summary>
	/// O SERVIDOR LIGA `deepmeditation` EM ALGUM LUGAR? Le o proprio fonte -- o mesmo mecanismo com que
	/// a `--censoteste` afirma sobre codigo que nao da pra chamar. A resposta tem que ser NAO: no DM a
	/// reforma da meditacao deixou so os `deepmeditation = 0` (ver `RegraDeNivel.Estado.MeditacaoProfunda`).
	/// </summary>
	private static bool MnDeepMeditationAlgumaVezLigada()
	{
		string fonte = LerFonteDaBancada("Server/GameServer.Skills.cs");
		return fonte.Contains("MeditacaoProfunda: true", StringComparison.Ordinal);
	}

	// =====================================================================
	// 3) A CORRENTE `if / else if / else`
	// =====================================================================
	/// <summary>
	/// O `else` NAO PODE CREDITAR POR CIMA DO RAMO QUE JA RENDEU.
	///
	/// A Advanced Ki Circulation e `if(kibuffon) +2 else +1` (Mind.dm:513-519): sao ALTERNATIVAS. Sem
	/// a corrente, o `else` (que nao tem condicao) valeria sempre e o total com o buff seria 3 -- meio
	/// a mais, pra sempre e em silencio. E a Basic Ki Awareness prova o outro lado: la o `studying` e
	/// um `if` IRMAO (`:189`), fora da corrente do `else`, e os dois somam.
	/// </summary>
	private void AsCorrentesDeElse()
	{
		GD.Print("[mente] -- 3) A CORRENTE `if/else`: alternativas se excluem, `if` irmaos somam");

		string circ = MnPath("Advanced_Ki_Circulation");
		ServerPlayer pl = MnCorpo("Corrente", [MnRaiz, circ], [(circ, 20)]);
		double comBuff = MnGanho(pl, circ, new NiveisDeSkill.EstadoDoCorpo(false, false, false, ComBuffDeKi: true, Ficha: pl.Ficha));
		double sem = MnGanho(pl, circ, new NiveisDeSkill.EstadoDoCorpo(false, false, false, Ficha: pl.Ficha));
		AfirmarMn("com buff a Circulacao rende SO o ramo de cima (2), e nao 2 + o `else` (3)",
				  Math.Abs(comBuff / sem - 2) < 1e-6, $"com {comBuff:0.##} / sem {sem:0.##} = {comBuff / sem:0.###}");

		string perc = MnPath("Basic_Ki_Awareness");
		ServerPlayer p2 = MnCorpo("Irmaos", [MnRaiz, perc], [(perc, 20)]);
		// nivel 20 ja passou do `level<10`, entao a corrente da meditacao cai no `else` (1);
		// o `studying` e um `if` IRMAO e soma 2 por cima.
		double so = MnGanho(p2, perc, new NiveisDeSkill.EstadoDoCorpo(false, false, false, Ficha: p2.Ficha));
		double estudando = MnGanho(p2, perc, new NiveisDeSkill.EstadoDoCorpo(false, false, false, Estudando: true, Ficha: p2.Ficha));
		AfirmarMn("...e o `if(studying)` da Percepcao e IRMAO do `else`: os dois somam (1 -> 1+2)",
				  Math.Abs(estudando / so - 3) < 1e-6, $"so {so:0.##} -> estudando {estudando:0.##}");

		// O PORTAO DE NIVEL: `if(level<10 && med)` rende 2 ate o 9 e cai pro `else` (1) dali em diante.
		ServerPlayer novo = MnCorpo("Novato", [MnRaiz, perc], [(perc, 3)]);
		double cedo = MnGanho(novo, perc, new NiveisDeSkill.EstadoDoCorpo(Meditando: true, Voando: false, Treinando: false, Ficha: novo.Ficha));
		ServerPlayer velho = MnCorpo("Veterano", [MnRaiz, perc], [(perc, 30)]);
		double tarde = MnGanho(velho, perc, new NiveisDeSkill.EstadoDoCorpo(Meditando: true, Voando: false, Treinando: false, Ficha: velho.Ficha));
		AfirmarMn("o portao de NIVEL vale: meditando rende 2 abaixo do nivel 10 e 1 acima (`level<10&&med`)",
				  cedo > tarde, $"nivel 3 -> {cedo:0.##}; nivel 30 -> {tarde:0.##}");
	}

	// =====================================================================
	// 4) O ALCANCE PELO FUNIL DE PRODUCAO
	// =====================================================================
	/// <summary>
	/// A CADEIA INTEIRA, PELO `Aprender` -- o mesmo que o `C2S.Aprender` chama.
	///
	/// Nada aqui e escrito na mao: os niveis sobem pelo `Niveis.Por` + `AplicarEfeitos` (que e o que o
	/// login faz), e quem decide se da pra comprar e o `Livro.Avaliar` de producao.
	/// </summary>
	private void OAlcancePeloFunil()
	{
		GD.Print("[mente] -- 4) O ALCANCE: quem da pra comprar, e o que acende cada uma");

		ServerPlayer pl = ForjarComSkills("Aprendiz da Mente", CorredorLivre(4), bp: 50_000);
		// CINQUENTA MARCOS E O PRECO DAS DEZESSETE, e o numero e do catalogo: o custo cresce com o
		// tier (1 na raiz e nas cinco Basic, 3 nas Advanced, 4 na Targeted, 5 nas Perfect) --
		// 1 + 5 + 15 + 4 + 25 = 50. A primeira versao desta bancada concedia 40 e as tres ultimas
		// compras eram recusadas por FALTA DE MARCOS, o que na tela e indistinguivel de "nao acendeu".
		pl.Livro.Conceder(80);
		MnLogin(pl);

		bool Pode(string curto) =>
			pl.Livro.PodeAprender(_skills!, MnPath(curto), pl.Race, pl.Class, false) == Recusa.Pode;

		// ---- a raiz, e so ela ----
		AfirmarMn("um corpo recem-nascido PODE comprar a Ki Unlocked", Pode("Ki_Unlocked"));
		int trancadas = MnAsDezessete.Count(c => c != "Ki_Unlocked" && !Pode(c));
		AfirmarMn("...e as outras dezesseis estao trancadas (`enabled = 0` esperando acendedor)",
				  trancadas == 16, $"{trancadas} de 16");

		EscutaDeAvisos = [];
		Aprender(pl, MnRaiz);
		AfirmarMn("comprar a Ki Unlocked pelo FUNIL passa", pl.Livro.Sabe(MnRaiz), Ultimos(EscutaDeAvisos));
		EscutaDeAvisos = null;

		// ---- os cinco acendedores das Basic, cada um no nivel do DM ----
		// `Mind.dm:11-31`: kiawarenessskill>=1, kigatheringskill>=5, kicirculationskill>=1,
		// kicontrolskill>=5, kiefficiencyskill>=5 -- e todos vem dos degraus da PROPRIA Ki Unlocked.
		(int Nivel, string Curto, string Contador)[] degrausDaRaiz =
		[
			(5,   "Basic_Ki_Awareness",   "kiawarenessskill>=1"),
			(50,  "Basic_Ki_Gathering",   "kigatheringskill>=5"),
			(75,  "Basic_Ki_Circulation", "kicirculationskill>=1"),
			(100, "Basic_Ki_Control",     "kicontrolskill>=5"),
			(100, "Basic_Ki_Efficiency",  "kiefficiencyskill>=5"),
		];

		foreach ((int nivel, string curto, string cond) in degrausDaRaiz)
		{
			// A METADE DE ANTES: um nivel abaixo do degrau, a skill continua trancada.
			pl.Niveis.Por(MnRaiz, nivel - 1);
			MnLogin(pl);
			bool antes = Pode(curto);

			pl.Niveis.Por(MnRaiz, nivel);
			MnLogin(pl);
			bool depois = Pode(curto);

			AfirmarMn($"a Ki Unlocked no nivel {nivel} ACENDE a {curto.Replace('_', ' ')} (`{cond}`)",
					  !antes && depois, $"antes {antes}, depois {depois}");
		}

		// ---- a cadeia de tres degraus: Basic 100 -> Advanced, Advanced 100 -> Perfect ----
		(string Basica, string Avancada, string Perfeita)[] familias =
		[
			("Basic_Ki_Awareness", "Advanced_Ki_Awareness", "Perfect_Ki_Awareness"),
			("Basic_Ki_Circulation", "Advanced_Ki_Circulation", "Perfect_Ki_Circulation"),
			("Basic_Ki_Control", "Advanced_Ki_Control", "Perfect_Ki_Control"),
			("Basic_Ki_Efficiency", "Advanced_Ki_Efficiency", "Perfect_Ki_Efficiency"),
			("Basic_Ki_Gathering", "Advanced_Ki_Gathering", "Perfect_Ki_Gathering"),
		];

		foreach ((string basica, string avancada, string perfeita) in familias)
		{
			EscutaDeAvisos = [];
			Aprender(pl, MnPath(basica));
			EscutaDeAvisos = null;
			AfirmarMn($"comprar a {basica.Replace('_', ' ')} pelo funil passa", pl.Livro.Sabe(MnPath(basica)));

			pl.Niveis.Por(MnPath(basica), 99);
			MnLogin(pl);
			bool antes = Pode(avancada);
			pl.Niveis.Por(MnPath(basica), 100);
			MnLogin(pl);
			AfirmarMn($"...e no nivel 100 dela a {avancada.Replace('_', ' ')} ACENDE (o `enableskill` do degrau)",
					  !antes && Pode(avancada),
					  $"antes {antes}, depois {pl.Livro.PodeAprender(_skills!, MnPath(avancada), pl.Race, pl.Class, false)}");

			Aprender(pl, MnPath(avancada));
			pl.Niveis.Por(MnPath(avancada), 99);
			MnLogin(pl);
			bool antesP = Pode(perfeita);
			pl.Niveis.Por(MnPath(avancada), 100);
			MnLogin(pl);
			AfirmarMn($"...e no 100 da Advanced a {perfeita.Replace('_', ' ')} ACENDE (a cadeia inteira)",
					  !antesP && Pode(perfeita),
					  $"antes {antesP}, depois {pl.Livro.PodeAprender(_skills!, MnPath(perfeita), pl.Race, pl.Class, false)}");
			Aprender(pl, MnPath(perfeita));
		}

		int compradas = MnAsDezessete.Count(c => c != "Advanced_Targeted_Mastery" && pl.Livro.Sabe(MnPath(c)));
		AfirmarMn("as DEZESSEIS da cadeia de Ki foram compradas pelo funil (a 17a e a Targeted, ver abaixo)",
				  compradas == 16, $"{compradas} de 16");

		// ---- a decima setima: Advanced Targeted Mastery ----
		// Ela pende da Mind E da Effusive Specialty, e quem a acende e o nivel 100 da Basic Targeted
		// Mastery (`Effusion.dm:479`) -- que e galho de OUTRA arvore. A porta existe e a bancada a
		// atravessa: sem a skill anterior no 100 ela fica trancada; com ela, acende.
		const string basicTargeted = "/datum/skill/mind/Basic_Targeted_Mastery";
		bool antesT = Pode("Advanced_Targeted_Mastery");
		pl.Livro.Dar(basicTargeted);
		pl.Niveis.Por(basicTargeted, 100);
		MnLogin(pl);
		AfirmarMn("a Advanced Targeted Mastery acende no nivel 100 da Basic Targeted (galho de OUTRA arvore)",
				  !antesT && Pode("Advanced_Targeted_Mastery"),
				  $"antes {antesT}, depois {pl.Livro.PodeAprender(_skills!, MnPath("Advanced_Targeted_Mastery"), pl.Race, pl.Class, false)}");
		Aprender(pl, MnPath("Advanced_Targeted_Mastery"));
		AfirmarMn("...e as DEZESSETE estao no livro deste corpo, todas compradas pelo funil",
				  MnAsDezessete.All(c => pl.Livro.Sabe(MnPath(c))),
				  string.Join(",", MnAsDezessete.Where(c => !pl.Livro.Sabe(MnPath(c)))));
	}

	// =====================================================================
	// 5) O EFEITO NOMEADO DE CADA UMA
	// =====================================================================
	/// <summary>
	/// UMA PROVA POR SKILL, NAS DUAS METADES: um corpo SEM ela contra um corpo COM ela no nivel do
	/// degrau. O que se mede e o campo (ou o verb) que o DM escreve naquele degrau, pelo nome.
	///
	/// A TABELA E DO DM, LINHA A LINHA. Cada entrada cita o nivel e o que ele entrega; o corpo "com"
	/// e forjado com a skill no nivel exato e passa pelo `AplicarEfeitos`, que e o do login.
	/// </summary>
	private readonly record struct MnProva(string Curto, int Nivel, string Oque, string Campo, double Minimo, string Verb = "");

	private void OEfeitoDeCadaUma()
	{
		GD.Print("[mente] -- 5) O EFEITO NOMEADO DE CADA UMA DAS DEZESSETE (duas metades)");

		MnProva[] provas =
		[
			// A raiz: os dois flags vem da COMPRA (`after_learn`), o resto dos degraus.
			new("Ki_Unlocked", 100, "abre o Ki (`KiUnlockPercent=1`) e a regeneracao ao meditar", "MeditateGivesKiRegen", 1, "Kiai"),
			new("Basic_Ki_Awareness", 100, "+20 de percepcao de Ki (`kiawarenessskill += 1` a cada 5)", "kiawarenessskill", 20, "Study_Other"),
			new("Basic_Ki_Circulation", 100, "+23 de circulacao e o verb Foco (nivel 30)", "kicirculationskill", 23, "Focus"),
			new("Basic_Ki_Control", 100, "+25 de controle, e o `canPower` do nivel 5", "canPower", 1, "Power_Control"),
			new("Basic_Ki_Efficiency", 100, "+24 de eficiencia e +20 de armadura de Ki (`kiarmor`)", "kiarmor", 20, "Efficiency"),
			new("Basic_Ki_Gathering", 100, "+25 de acumulo de Ki", "kigatheringskill", 25),
			new("Advanced_Ki_Awareness", 100, "+50 de percepcao e a Telepatia (nivel 10)", "kiawarenessskill", 50, "Telepathy"),
			new("Advanced_Ki_Circulation", 100, "+50 de circulacao e a cura por buff (`buffregen`, nivel 30)", "buffregen", 1),
			new("Advanced_Ki_Control", 100, "+50 de controle de Ki", "kicontrolskill", 50),
			new("Advanced_Ki_Efficiency", 100, "+50 de eficiencia", "kiefficiencyskill", 50),
			new("Advanced_Ki_Gathering", 100, "+50 de acumulo", "kigatheringskill", 50),
			new("Advanced_Targeted_Mastery", 100, "+50 de pericia de ataque teleguiado por alvo", "targetedskill", 50),
			new("Perfect_Ki_Awareness", 100, "+25 de percepcao (o degrau periodico de 5)", "kiawarenessskill", 25),
			new("Perfect_Ki_Circulation", 100, "+25 de circulacao", "kicirculationskill", 25),
			new("Perfect_Ki_Control", 100, "+25 de controle", "kicontrolskill", 25),
			new("Perfect_Ki_Efficiency", 100, "+25 de eficiencia", "kiefficiencyskill", 25),
			new("Perfect_Ki_Gathering", 100, "+25 de acumulo", "kigatheringskill", 25),
		];

		foreach (MnProva p in provas)
		{
			string path = MnPath(p.Curto);

			// A METADE DE ANTES: o mesmo corpo, sem a skill.
			ServerPlayer sem = ForjarComSkills("SemA" + p.Curto, CorredorLivre(4), bp: 50_000);
			MnLogin(sem);
			double antes = MnCampo(sem, p.Campo);
			bool verbAntes = p.Verb.Length > 0 && SabeTecnica(sem, p.Verb);

			// A METADE DE DEPOIS: com ela, no nivel do degrau.
			ServerPlayer com = ForjarComSkills("ComA" + p.Curto, CorredorLivre(4), bp: 50_000,
											   skills: [path], degraus: [(path, p.Nivel)]);
			MnLogin(com);
			double depois = MnCampo(com, p.Campo);
			bool verbDepois = p.Verb.Length == 0 || SabeTecnica(com, p.Verb);

			AfirmarMn($"{_skills!.Get(path)?.Nome} no nivel {p.Nivel}: {p.Oque}",
					  antes < p.Minimo && depois >= p.Minimo - 1e-6 && !verbAntes && verbDepois,
					  $"{p.Campo}: {antes:0.##} -> {depois:0.##} (piso {p.Minimo})"
					  + (p.Verb.Length > 0 ? $"; verb {p.Verb}: {verbAntes} -> {verbDepois}" : ""));
		}

		// ---- as duas SKILLS que a raiz CONCEDE por degrau, e a que o dono RECUSOU ----
		{
			ServerPlayer pl = ForjarComSkills("Concedido", CorredorLivre(4), bp: 50_000,
											  skills: [MnRaiz], degraus: [(MnRaiz, 4)]);
			MnLogin(pl);
			bool antes = pl.Livro.Sabe("/datum/skill/sense");
			pl.Niveis.Por(MnRaiz, 5);
			MnLogin(pl);
			AfirmarMn("a Ki Unlocked no nivel 5 CONCEDE o Sense (`new/datum/skill/sense` + `learn`, Mind.dm:103)",
					  !antes && pl.Livro.Sabe("/datum/skill/sense"));

			pl.Niveis.Por(MnRaiz, 30);
			MnLogin(pl);
			AfirmarMn("...e o VOO do nivel 30 continua RECUSADO por decisao do dono (`NaoConcedidasPorDegrau`)",
					  !pl.Livro.Sabe(SkillDoVoo));
		}
	}

	private static double MnCampo(ServerPlayer pl, string campo)
	{
		System.Reflection.FieldInfo? fi = typeof(Jandirus.Core.Stats.Fighter)
			.GetField(campo, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
		return fi?.GetValue(pl.Ficha) is double d ? d : double.NaN;
	}

	// =====================================================================
	// 6) O SISTEMA DE ESTUDO (lote G13)
	// =====================================================================
	private void OSistemaDeEstudo()
	{
		GD.Print("[mente] -- 6) O SISTEMA DE ESTUDO: estudar, focar e escrever (lote G13)");

		const string perc = "/datum/skill/mind/Basic_Ki_Awareness";
		const string circ = "/datum/skill/mind/Basic_Ki_Circulation";
		Vec2 chao = CorredorLivre(6);

		// O MESTRE sabe as duas skills no nivel 60; o ALUNO nas mesmas no nivel 10.
		ServerPlayer mestre = ForjarComSkills("Mestre", chao + new Vec2(ZoneCollision.TileSize, 0), bp: 50_000,
											  skills: [MnRaiz, perc, circ], degraus: [(perc, 60), (circ, 60)], kiMin: 100_000);
		ServerPlayer aluno = ForjarComSkills("Aluno", chao, bp: 50_000,
											 skills: [MnRaiz, perc, circ], degraus: [(perc, 10), (circ, 10)], kiMin: 100_000);
		mestre.Peer = null;   // corpo forjado nao tem dono; o laco do DM tambem nao exige (`:71`)

		// ---- STUDY_OTHER ----
		double bancoAntes = aluno.Niveis.Buffer(perc);
		List<string> falas = ApertarEOuvir(aluno, "Study_Other:Mestre");
		AfirmarMn("Study_Other:<nome> comeca o estudo e marca `studying` na ficha",
				  aluno.Ficha.studying == 1 && Disse(falas, "estudar"), Ultimos(falas));
		AfirmarMn("...e antes de qualquer tique o banco de exp adiantado esta VAZIO",
				  bancoAntes == 0, $"{bancoAntes}");

		for (int i = 0; i < 6; i++) TickG13();   // 6 tiques do efetor = mais de um segundo do laco
		double bancoDepois = aluno.Niveis.Buffer(perc);
		AfirmarMn("um segundo de estudo DEPOSITA no banco da skill em que o mestre esta na frente",
				  bancoDepois > 0, $"{bancoAntes} -> {bancoDepois}");
		AfirmarMn("...e deposita nas DUAS skills em que ele esta na frente (estudo sem foco espalha)",
				  aluno.Niveis.Buffer(circ) > 0, $"{aluno.Niveis.Buffer(circ)}");

		// E O BANCO VIRA VELOCIDADE: o proximo ganho sai multiplicado (`KiSkillGains`, Mind.dm:864-870).
		double semBanco = MnGanho(aluno, "/datum/skill/mind/Advanced_Ki_Awareness",
								  new NiveisDeSkill.EstadoDoCorpo(false, false, false, Ficha: aluno.Ficha), 1);
		double expAntes = aluno.Niveis.Exp(perc);
		var rng = new Random(3);
		aluno.Niveis.Efetor(rng, _skills!, aluno.Livro!, new NiveisDeSkill.EstadoDoCorpo(false, false, false, Ficha: aluno.Ficha));
		double comBanco = aluno.Niveis.Exp(perc) - expAntes;
		AfirmarMn("...e o banco ACELERA o ganho seguinte, em vez de virar exp de uma vez",
				  comBanco > 0 && aluno.Niveis.Buffer(perc) < bancoDepois,
				  $"ganho {comBanco:0.##}, banco {bancoDepois:0.##} -> {aluno.Niveis.Buffer(perc):0.##} (ref {semBanco:0.##})");

		// PERDER O ALVO DE VISTA PARA O ESTUDO (`if(range<=0) studying=0`, `:82`).
		mestre.Pos = chao + new Vec2(ZoneCollision.TileSize * 40, 0);
		for (int i = 0; i < 6; i++) TickG13();
		AfirmarMn("o alvo se afasta alem de dez tiles e o estudo PARA sozinho",
				  aluno.Ficha.studying == 0);
		mestre.Pos = chao + new Vec2(ZoneCollision.TileSize, 0);

		// O APERTO NU ESTUDA O ALVO MARCADO -- e a unica porta que o CLIENTE tem (o painel de verbs
		// so manda o id, sem argumento). Sem ela o verb estaria portado e inalcancavel pela tela.
		aluno.AlvoId = mestre.Id;
		falas = ApertarEOuvir(aluno, "Study_Other");
		AfirmarMn("o aperto NU estuda quem esta MARCADO (duplo clique) -- a porta que o cliente tem",
				  aluno.Ficha.studying == 1 && Disse(falas, "Mestre"), Ultimos(falas));
		ApertarEOuvir(aluno, "Study_Other");   // desliga de novo

		// ---- FOCUS_SKILL: dez vezes, e SO na focada ----
		var alunoB = ForjarComSkills("AlunoB", chao + new Vec2(0, ZoneCollision.TileSize), bp: 50_000,
									 skills: [MnRaiz, perc, circ], degraus: [(perc, 10), (circ, 10)], kiMin: 100_000);
		falas = ApertarEOuvir(alunoB, "Focus_Skill:Basic Ki Awareness");
		AfirmarMn("Focus_Skill:<nome> escolhe a habilidade em foco",
				  Disse(falas, "Basic Ki Awareness"), Ultimos(falas));
		ApertarEOuvir(alunoB, "Study_Other:Mestre");
		for (int i = 0; i < 6; i++) TickG13();
		AfirmarMn("com foco, o estudo deposita SO na focada (o `else if(!focusskill)` do DM)",
				  alunoB.Niveis.Buffer(perc) > 0 && alunoB.Niveis.Buffer(circ) == 0,
				  $"focada {alunoB.Niveis.Buffer(perc):0.##}, outra {alunoB.Niveis.Buffer(circ):0.##}");
		AfirmarMn("...e o deposito focado e DEZ VEZES o sem foco (50x contra 5x)",
				  Math.Abs(alunoB.Niveis.Buffer(perc) / bancoDepois - 10) < 1e-6,
				  $"{alunoB.Niveis.Buffer(perc):0.##} contra {bancoDepois:0.##}");

		// ---- WRITE_TEACHINGS: escrever, e o livro na mochila ----
		int mochilaAntes = mestre.Mochila.Pilhas.Count;
		falas = ApertarEOuvir(mestre, "Write_Teachings:Basic Ki Awareness");
		AfirmarMn("Write_Teachings:<nome> comeca o livro e diz quanto tempo leva MEDITANDO",
				  Disse(falas, "escrever") && Disse(falas, "MEDITANDO"), Ultimos(falas));

		mestre.Ficha.med = false;
		for (int i = 0; i < 50; i++) TickG13();
		AfirmarMn("...e SEM meditar o livro nao anda (nenhum item novo)",
				  mestre.Mochila.Pilhas.Count == mochilaAntes);

		// `writetarget = level*300` tiques -- 60 no nivel 60 vezes 300 = 18.000. A bancada medita o
		// necessario; o numero e o do DM e esta na constante do lote.
		mestre.Ficha.med = true;
		for (int i = 0; i < 60 * 300 + 5; i++) TickG13();
		AfirmarMn("meditando o tempo do DM (`level*300` tiques), o LIVRO aparece na mochila",
				  mestre.Mochila.Pilhas.Count == mochilaAntes + 1,
				  string.Join(",", mestre.Mochila.Pilhas.Select(p => p.Id)));

		string idDoLivro = mestre.Mochila.Pilhas[^1].Id;
		LivroDeEnsinamentos? livro = LivroDeEnsinamentos.Ler(idDoLivro);
		AfirmarMn("...e ele ensina ate a METADE do nivel do autor (`writelevel = round(level/2)`)",
				  livro is { NivelDoAutor: 60, } && livro.NivelQueEnsina == 30, idDoLivro);

		// ---- LER O LIVRO ----
		mestre.Mochila.Tirar(idDoLivro);
		aluno.Mochila.Guardar(idDoLivro);
		double expDoAluno = aluno.Niveis.Exp(perc);
		falas = Ouvir(() => ComandoDeItem(aluno, "item_ler", idDoLivro));
		AfirmarMn("um leitor ABAIXO da metade do nivel do autor aprende com o livro",
				  aluno.Niveis.Exp(perc) > expDoAluno && Disse(falas, "experiencia"), Ultimos(falas));
		AfirmarMn("...e o livro SOME ao ser lido (`del(src)`)",
				  aluno.Mochila.Quantos(idDoLivro) == 0);

		// A METADE DE CIMA: quem ja passou da metade nao aprende nada.
		ServerPlayer adiantado = ForjarComSkills("Adiantado", chao + new Vec2(0, ZoneCollision.TileSize * 2), bp: 50_000,
												 skills: [MnRaiz, perc], degraus: [(perc, 55)], kiMin: 100_000);
		adiantado.Mochila.Guardar(idDoLivro);
		double expDoAdiantado = adiantado.Niveis.Exp(perc);
		falas = Ouvir(() => ComandoDeItem(adiantado, "item_ler", idDoLivro));
		AfirmarMn("um leitor ACIMA da metade do nivel do autor nao aprende nada, e o livro FICA",
				  adiantado.Niveis.Exp(perc) == expDoAdiantado && adiantado.Mochila.Quantos(idDoLivro) == 1
				  && Disse(falas, "nao consegue aprender"), Ultimos(falas));

		// E QUEM NAO SABE A SKILL ("You don't know this skill!", `:202`).
		ServerPlayer leigo = ForjarComSkills("Leigo", chao + new Vec2(0, ZoneCollision.TileSize * 3), bp: 50_000);
		leigo.Mochila.Guardar(idDoLivro);
		falas = Ouvir(() => ComandoDeItem(leigo, "item_ler", idDoLivro));
		AfirmarMn("...e quem NAO conhece a habilidade nao aprende nada com o livro",
				  Disse(falas, "nao conhece"), Ultimos(falas));

		// O LIVRO ATRAVESSA O FIO: o id carrega os dados dele, e o protocolo tem que caber.
		var w = new LiteNetLib.Utils.NetDataWriter();
		w.PutInventario(adiantado.Mochila);
		var r = new LiteNetLib.Utils.NetDataReader(w.CopyData());
		Inventario lido = r.GetInventario();
		AfirmarMn("o livro atravessa o pacote de inventario INTEIRO (o id dele passa de 32 letras)",
				  lido.Quantos(idDoLivro) == 1, string.Join(",", lido.Pilhas.Select(p => p.Id)));
	}

	// =====================================================================
	// 7) O BUFF DE KI E A CURA QUE ELE DESTRAVA
	// =====================================================================
	private void OBuffDeKiEACura()
	{
		GD.Print("[mente] -- 7) O `kibuffon` e a cura do `buffregen` (Advanced Ki Circulation nivel 30)");

		const string circ = "/datum/skill/mind/Advanced_Ki_Circulation";
		// A BASIC ENTRA JUNTO porque e ELA que concede o verb Foco, no degrau 30 (`Mind.dm:253`) -- a
		// Advanced so tem o `buffregen`. Sem as duas nao ha como LIGAR o buff que a cura exige.
		const string basica = "/datum/skill/mind/Basic_Ki_Circulation";
		ServerPlayer pl = ForjarComSkills("Circulante", CorredorLivre(4), bp: 50_000,
										  skills: [MnRaiz, basica, circ],
										  degraus: [(basica, 30), (circ, 30)], kiMin: 1_000_000);
		MnLogin(pl);

		AfirmarMn("o degrau 30 da Advanced Ki Circulation escreve `buffregen` na ficha",
				  pl.Ficha.buffregen == 1, $"{pl.Ficha.buffregen}");
		AfirmarMn("...e sem buff de Ki nenhum, `kibuffon` esta em zero", pl.Ficha.kibuffon == 0);

		// FERE UM MEMBRO pra a cura ter o que fazer, e mede as duas metades.
		Jandirus.Core.Combat.BodyPart membro = pl.Combate!.Corpo.Partes[0];
		membro.Vida = membro.VidaMax * 0.5;
		double vidaAntes = membro.Vida;
		for (int i = 0; i < 20; i++) TicarNiveisDe(pl);
		AfirmarMn("sem buff de Ki, o corpo NAO se cura pelo `buffregen`", membro.Vida == vidaAntes,
				  $"{vidaAntes:0.####} -> {membro.Vida:0.####}");

		List<string> falas = ApertarEOuvir(pl, "Focus");
		AfirmarMn("ligar o FOCO acende `kibuffon` (o `container.kibuffon=1` dos tres buffs de Ki)",
				  pl.Ficha.kibuffon == 1, Ultimos(falas));

		vidaAntes = membro.Vida;
		for (int i = 0; i < 20; i++) TicarNiveisDe(pl);
		AfirmarMn("...e AGORA o corpo se cura a cada tique (`SpreadHeal(0.01)`, Mind.dm:517)",
				  membro.Vida > vidaAntes, $"{vidaAntes:0.####} -> {membro.Vida:0.####}");

		ApertarEOuvir(pl, "Focus");
		AfirmarMn("desligar o Foco apaga `kibuffon` de novo", pl.Ficha.kibuffon == 0);

		// E O CONTADOR DA FAMILIA BUFF MASTERY (`Mind.dm:133`) -- ele existe mesmo que as tres skills
		// dela sejam mortas no DM (o `growbranches` delas foi escrito na arvore errada).
		const string buffMastery = "/datum/skill/mind/Basic_Buff_Mastery";
		pl.Livro!.Dar(buffMastery);
		pl.Niveis.Por(buffMastery, 1);
		double expAntes = pl.Niveis.Exp(buffMastery);
		ApertarEOuvir(pl, "Focus");
		for (int i = 0; i < 10; i++) TicarNiveisDe(pl);
		AfirmarMn("com um buff de Ki de pe, o `kibuffcounter` credita exp na familia Buff Mastery",
				  pl.Niveis.Exp(buffMastery) > expAntes,
				  $"{expAntes:0.##} -> {pl.Niveis.Exp(buffMastery):0.##}");
		ApertarEOuvir(pl, "Focus");
	}
}
