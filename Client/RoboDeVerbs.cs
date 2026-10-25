using Godot;
using Jandirus.Core.Verbos;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// A BANCADA DE TELA DOS VERBS POR REQUISITO (`--diagverbs`) -- o que o JOGADOR ve, e nao o que a tabela diz.
///
/// O dono (2026-10-10), com a foto da aba World de um Saiyajin: *"tem um bug na terceira imagem q aparece em
/// world falando q te um sinal dourado 2 celulas dali mas nem tenho radar, e seria melhor dar uma limpa em
/// verbs q sao exclusivas de raças e deveriam so aparecer se o player tem os requisitos se nao fica mt
/// poluido, como esses verbs de clan dragao, split form (coisa q n tenho mas tem os verbs), verb de fusao
/// namek etc."*
///
/// A irma de servidor (`--verbsteste`) prova a tabela CONTRA O SERVIDOR: quem ela esconde, o servidor recusa.
/// Esta prova a outra metade -- que as quatro abas do menu P, a busca, as teclas e o menu da tecla E
/// DESENHAM o que a tabela manda. Le-se a arvore de botoes (o `Button` que estaria na tela), e nao a tabela:
///
///   F1  o novato de verdade (o que o servidor contou a ESTE cliente): os botoes da foto do dono sumiram;
///   F2  cada personagem (`PosseDoCliente.DeTeste`): Saiyajin, Namekuseijin com e sem Cla do Dragao, quem
///       tem Split Form, quem tem radar, os cargos, as ofertas -- botao na tela se e so se o requisito vale;
///   F3  a busca e a tecla ligada obedecem ao mesmo crivo;
///   F4  o radar dourado pelo FIO: sem o aparelho a frase nem chega, e o cliente nao a desenha nem que chegue;
///   F5  a tecnica e a oferta pelo FIO: o botao nasce sozinho quando o servidor conta, e some quando acaba;
///   F6  a tecla E no mainframe: as opcoes de humano so pra humano;
///   F7  o verb sem corpo nao vira botao;
///   F8  do cargo, so o ataque de ki vira "Ensinar" (dono, 2026-10-23: a chave da Sala do Tempo nao se ensina);
///   F9  sem ninguem marcado, a chave da Sala do Tempo e o destrancar vao no proprio id (dono, 2026-10-23);
///   F10 o clique numa copia minha do Split Form abre as ordens dela, e a ordem e so dela (dono, 2026-10-24).
///
/// O defeito injetado e o de sempre da regra: `Requisito.MostrarTudoDeTeste` (o menu de antes).
///
///     Godot --headless --path . --host --rede 8191 --diagverbs --raca Human --conta bancada_dverbs --nome Novato
/// </summary>
public partial class RoboDeVerbs : Node
{
	private int _ok, _falha, _injOk, _injFalha;
	private readonly List<string> _reprovadas = [];

	private static void Nota(string s) => GD.Print("[dverbs] " + s);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK     " : "  FALHA  ") + oque + (!passou && detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private void Injeta(string oque, bool ficouVermelha, string detalhe = "")
	{
		Nota((ficouVermelha ? "  pegou  " : "  PASSOU ") + "(defeito injetado) " + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ficouVermelha) _injOk++;
		else { _injFalha++; _reprovadas.Add("(defeito injetado) " + oque); }
	}

	private static GameClient? C => GameClient.Instance;
	private static MenuJogo? M => MenuJogo.Instancia;
	private static MenuDeInteracao? E => MenuDeInteracao.Instancia;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>As quatro abas do menu P que desenham verbs com requisito.</summary>
	private static readonly string[] AbasDeVerb = ["World", Verbos.Outros, Verbos.Aprendizado, Verbos.Skills];

	public override void _Ready() => _ = Rodar();

	/// <summary>Uma excecao no roteiro e um vermelho com placar, e nao uma bancada muda ate o prazo -- ver `RoboDasAbas.Rodar`.</summary>
	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Checa("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			Nota(e.ToString());
			Encerrar(3);
		}
	}

	private void Encerrar(int codigo)
	{
		// O MUNDO VOLTA: nenhum botao de defeito nem personagem postico sobrevive ao roteiro.
		Requisito.MostrarTudoDeTeste = false;
		PosseDoCliente.DeTeste = null;
		Jandirus.Core.Skills.EnsinoDeSkill.CargoSeEnsinaComoAntesDeTeste = false;
		Jandirus.Core.Skills.EnsinoDeSkill.MisticoSeEnsinaComoAntesDeTeste = false;
		VerbosDoJogo.SoNoAlvoMarcadoDeTeste = false;
		if (S is { } srv) srv.SinalSemRadarDeTeste = false;

		Nota("==================================================================================");
		Nota($" PLACAR: {_ok} OK, {_falha} FALHA(S)   |   DEFEITOS INJETADOS: {_injOk} pegos, {_injFalha} passaram batido");
		foreach (string r in _reprovadas) Nota("   FALHA  " + r);
		int vermelhas = _falha + _injFalha;
		Nota(vermelhas == 0 ? "===== TUDO OK =====" : $"===== {vermelhas} FALHA(S) =====");
		GetTree().Quit(codigo);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Tarefa Percurso()
	{
		Nota("==================================================================================");
		Nota(" QUEM VE QUE VERB, NA TELA -- menu P (World, Other, Learning, Skills), busca, teclas e tecla E");
		Nota("==================================================================================");

		bool pronto = await Ate(() => C is { Connected: true } c && c.Atributos.Raca is { Length: > 0 } && M != null && S != null, 90);
		Checa("o mundo chegou (conexao, raca na ficha lenta, menu de pe) e o servidor e deste processo (--host)", pronto,
			  $"raca={C?.Atributos.Raca} menu={(M != null)} servidor={(S != null)}");
		if (!pronto || M is not { } menu || C is not { } cli || S is not { } srv) { Encerrar(2); return; }
		await Segundos(1.5);

		Checa("PRECONDICAO: a bancada e um HUMANO, num PLANETA (a Terra), sem cargo, sem radar e sem tecnica nenhuma",
			  cli.Atributos.Raca == "Human" && Jandirus.Core.World.Espaco.EhPlaneta(cli.Zone)
			  && cli.Mochila.Quantos(Jandirus.Core.Items.CatalogoDeItens.Radar) == 0 && !cli.VerbosAtivos.Contains("SplitForm"),
			  $"raca={cli.Atributos.Raca} zona={cli.Zone.Name}");

		menu.Abrir();
		await Quadros(3);

		List<(VerbDeclarado Linha, string Aba, string Texto)> linhas = LinhasDeTela();
		await F1_ONovatoDeVerdade(menu, cli, linhas);
		await F2_CadaPersonagem(menu, linhas);
		F3_ABuscaEATecla();
		await F4_ORadarPeloFio(menu, cli, srv);
		await F5_ATecnicaEAOfertaPeloFio(menu, cli, srv);
		await F6_ATeclaE(menu, cli, srv);
		await F7_OVerbSemCorpoNaoEBotao(menu, cli);
		await F8_OEnsinarDoCargo(menu, cli);
		F9_AChaveEmMim(cli);
		await F10_OCliqueNaCopia(menu, cli, srv);

		menu.Fechar();
		Encerrar(_falha == 0 && _injFalha == 0 ? 0 : 1);
	}

	// =====================================================================
	// A TABELA, VISTA DA TELA
	// =====================================================================
	/// <summary>
	/// AS LINHAS DA TABELA QUE TEM BOTAO NO MENU P DESTE CLIENTE, com a aba e o TEXTO do botao.
	///
	/// O texto de um verb de habilidade sai do REGISTRO (o `Nome` de quem carrega aquela chave), e nao do rotulo
	/// da tabela: e o que o `BotaoDe` escreve. O de um verb de canal e o rotulo -- e por isso o rotulo da tabela
	/// tem que ser o nome do botao, o que a F2 cobra (toda linha tem plateia). Ficam de fora as que nao sao
	/// botao do menu P (a linha do radar, as da tecla E, as da janela de desejos -- que o servidor manda prontas,
	/// e quem as mede e a `--menudodragaoteste`), as da aba do discipulado (a aba inteira e os botoes dos cartoes de
	/// aluno: quem as mede e a `--diagalunos` e a familia F17 da `--diagabas`) e as que este cliente nao registra (o
	/// "Regenerar" racial so se registra pro Namekuseijin de verdade; o estudo da Mente tem um botao por skill sabida).
	/// </summary>
	private List<(VerbDeclarado Linha, string Aba, string Texto)> LinhasDeTela()
	{
		var r = new List<(VerbDeclarado, string, string)>();
		var fora = new List<string>();
		foreach (VerbDeclarado v in RequisitosDosVerbos.Todos)
		{
			string aba = v.Onde.StartsWith("World", StringComparison.Ordinal) ? "World"
					   : v.Onde == "Other" ? Verbos.Outros
					   : v.Onde == "Learning" ? Verbos.Aprendizado
					   : v.Onde == "Skills" ? Verbos.Skills : "";
			string texto = !v.EhHabilidade ? v.Rotulo
						 : Verbos.Todos.FirstOrDefault(x => x.Chave == v.Chave)?.Nome ?? "";
			if (aba.Length == 0 || texto.Length == 0 || v.Chave == RequisitosDosVerbos.LinhaDoRadar) { fora.Add(v.Chave); continue; }
			r.Add((v, aba, texto));
		}
		Nota($"  linhas da tabela com botao no menu P: {r.Count} de {RequisitosDosVerbos.Todos.Count}   (fora: {string.Join(", ", fora)})");
		Checa("a tabela tem botao no menu P pra medir (40+ linhas), e as que ficaram de fora sao a do radar, as da tecla E, "
			  + "as da janela de desejos, as da aba do discipulado, o Regenerar racial e as duas familias do estudo da Mente",
			  r.Count >= 40 && fora.All(c => c == RequisitosDosVerbos.LinhaDoRadar || c == "hab:regenerar"
										  || c is "hab:Focus_Skill" or "hab:Write_Teachings"
										  || RequisitosDosVerbos.Linha(c)!.Onde.StartsWith("Tecla E", StringComparison.Ordinal)
										  || RequisitosDosVerbos.Linha(c)!.Onde == RequisitosDosVerbos.JanelaDeDesejos
										  || RequisitosDosVerbos.Linha(c)!.Onde == RequisitosDosVerbos.AbaDoDiscipulado),
			  string.Join(", ", fora));
		return r;
	}

	/// <summary>Os textos dos botoes VISIVEIS de cada uma das quatro abas, desenhadas pra este personagem (nulo = o de verdade).</summary>
	private async System.Threading.Tasks.Task<Dictionary<string, HashSet<string>>> Tela(MenuJogo menu, IPosse? quem)
	{
		PosseDoCliente.DeTeste = quem;
		var r = new Dictionary<string, HashSet<string>>();
		foreach (string aba in AbasDeVerb)
		{
			menu.IrPara(aba);
			menu.ForcarRedesenho();
			await Quadros(2);
			r[aba] = BotoesDa(menu, aba);
		}
		return r;
	}

	private static HashSet<string> BotoesDa(MenuJogo menu, string aba) =>
		menu.PaginaDeTeste(aba) is { } pg
			? [.. Todos(pg).OfType<Button>().Where(b => b.IsVisibleInTree()).Select(b => b.Text)]
			: [];

	private static List<string> RotulosDa(MenuJogo menu, string aba) =>
		menu.PaginaDeTeste(aba) is { } pg
			? [.. Todos(pg).OfType<Label>().Where(l => l.IsVisibleInTree()).Select(l => l.Text)]
			: [];

	// =====================================================================
	// F1 -- O NOVATO DE VERDADE
	// =====================================================================
	private async Tarefa F1_ONovatoDeVerdade(MenuJogo menu, GameClient cli, List<(VerbDeclarado Linha, string Aba, string Texto)> linhas)
	{
		Nota("--- F1: o humano novato DE VERDADE (o que o servidor contou a este cliente) ---");
		Dictionary<string, HashSet<string>> tela = await Tela(menu, null);

		// OS ROTULOS SO SE LEEM COM A ABA NA TELA (a `Tela` termina na Skills, e rotulo de pagina escondida nao
		// conta): a primeira rodada lia a World escondida, e "a linha do RADAR nao esta la" passava sem olhar nada.
		menu.IrPara("World");
		menu.ForcarRedesenho();
		await Quadros(2);
		List<string> rotulosDaWorld = RotulosDa(menu, "World");

		// OS BOTOES DA FOTO DO DONO, pelo texto LITERAL (e nao tirado da tabela: e a tabela que esta em prova).
		string[] daFoto =
		[
			"Erguer Estátua do Dragão", "Despertar as esferas", "Radar",   // (2026-10-22) o "Erguer + SUPREMO" virou caixa do menu da estatua
			"Ver as esferas daqui", "Pegar esfera", "Largar tudo", "INVOCAR",
			"Super Esferas: placar", "Reivindicar Super Esfera", "Chamar o Super Shenlong",
			"Meus domínios", "Coletar tributo", "Renascer aqui", "Abandonar domínio",
		];
		string[] deOutros =
		[
			"Split Form: Follow", "Split Form: Stop", "Split Form: Attack Target", "Split Form: Attack Nearest", "Split Form: Destroy",
			"Accept Youth", "Decline Youth", "Accept Potential", "Decline Potential",
			"Give Power to Spirit Bomb", "Refuse Spirit Bomb", "Accept Elder Seat", "Decline Elder Seat",
			"Appoint Elder", "Name Heir", "Remove Heir", "Line of Succession", "Challenge God of Destruction",
			"Accept Challenge", "Postpone Challenge", "Rank Duty", "Fund Earth",
			"Time Chamber: Authorize", "Time Chamber: Release", "Toggle SSJ Grades",
		];
		string[] deAprender =
		[
			"Fusão Namekuseijin (absorver)", "Dança da Fusão", "Aceitar a fusão", "Aceitar a ABSORÇÃO (perco o personagem)",
			"Recusar a fusão", "Passar o controle", "Aceitar", "Recusar", "Ajudar a transformar", "Dispensar aluno",
			"Deixar meu mestre", "Aceitar a licao", "Recusar a licao",
		];
		string[] deSkills = ["Voltar ao normal", "Abrir/fechar coletores", "Absorver", "Expelir todos"];

		// UM LITERAL ERRADO PASSARIA CALADO num "nao esta na tela": cada um tem que ser o texto de um botao da tabela.
		HashSet<string> textosDaTabela = [.. linhas.Select(l => l.Texto)];
		List<string> orfaos = [.. daFoto.Concat(deOutros).Concat(deAprender).Concat(deSkills).Where(t => !textosDaTabela.Contains(t))];
		Checa("(regua) cada texto literal desta familia e o texto de um botao que a tabela conhece (um literal errado passaria calado)",
			  orfaos.Count == 0, string.Join(" | ", orfaos));

		List<string> naWorld = [.. daFoto.Where(tela["World"].Contains)];
		Checa("**A FOTO DO DONO**: na aba World nao ha Erguer Estatua, Erguer + SUPREMO, Despertar, Radar -- nem os quatro das esferas "
			  + "(a Terra nao tem estatua), nem os tres das Super (em terra), nem os quatro de quem tem dominio",
			  naWorld.Count == 0, string.Join(" | ", naWorld));
		Checa("...e a linha \"RADAR: um sinal dourado...\" nao esta la (nem tenho radar)",
			  !rotulosDaWorld.Any(t => t.StartsWith("RADAR:", StringComparison.Ordinal)) && cli.SinalDourado.Length == 0,
			  $"sinal no cliente: '{cli.SinalDourado}'");
		Checa("...no lugar dos botoes das esferas ha a frase que diz por que nao ha nenhuma aqui (\"Este mundo não tem Estátua do Dragão\")",
			  rotulosDaWorld.Any(t => t.StartsWith("Este mundo não tem Estátua do Dragão", StringComparison.Ordinal)),
			  $"{rotulosDaWorld.Count} rotulo(s) visiveis na World");
		Checa("...e o que e de todo mundo continua: Ver este planeta, Conquistar planeta, ...à força e Arrancar bandeira",
			  new[] { "Ver este planeta", "Conquistar planeta", "...à força", "Arrancar bandeira" }.All(tela["World"].Contains),
			  string.Join(" | ", tela["World"]));

		List<string> naOther = [.. deOutros.Where(tela[Verbos.Outros].Contains)];
		Checa("**SPLIT FORM (coisa que nao tenho)**: as cinco ordens das copias nao estao na Other -- nem as respostas a ofertas que "
			  + "ninguem fez, nem os verbs de cargo, nem o desafio ao Deus, nem os graus do Super Saiyajin",
			  naOther.Count == 0, string.Join(" | ", naOther));
		Checa("...e 'Toggle Knockback' e 'Title Status' continuam la (verb sem requisito aparece pra todo mundo)",
			  tela[Verbos.Outros].Contains("Toggle Knockback") && tela[Verbos.Outros].Contains("Title Status"));

		List<string> naLearning = [.. deAprender.Where(tela[Verbos.Aprendizado].Contains)];
		Checa("**VERB DE FUSAO NAMEK**: a Fusao Namekuseijin nao esta na Learning de um humano -- nem a Danca (nao a aprendi), "
			  + "nem os aceites de convite que ninguem fez, nem os verbs de mestre de quem nao tem aluno nem mestre",
			  naLearning.Count == 0, string.Join(" | ", naLearning));
		string convidar = Verbos.Todos.FirstOrDefault(v => v.Chave == "hab:mst_convidar")?.Nome ?? "";
		Checa($"...e '{convidar}' continua la (convidar um aluno e de qualquer um)",
			  convidar.Length > 0 && tela[Verbos.Aprendizado].Contains(convidar));

		List<string> naSkills = [.. deSkills.Where(tela[Verbos.Skills].Contains)];
		Checa("NA SKILLS: Voltar ao normal (Oozaru), os coletores do androide, Absorver e Expelir nao aparecem pra um humano",
			  naSkills.Count == 0, string.Join(" | ", naSkills));
		string nadar = Verbos.Todos.FirstOrDefault(v => v.Chave == "hab:nadar")?.Nome ?? "";
		Checa($"...e '{nadar}' continua la", nadar.Length > 0 && tela[Verbos.Skills].Contains(nadar));

		// ---- o botao que mostra tudo: o menu de antes ----
		Requisito.MostrarTudoDeTeste = true;
		Dictionary<string, HashSet<string>> antes = await Tela(menu, null);
		Requisito.MostrarTudoDeTeste = false;
		int voltaram = linhas.Count(l => antes[l.Aba].Contains(l.Texto));
		Injeta($"com o botao que mostra tudo o novato ve {voltaram} dos {linhas.Count} botoes com requisito -- o menu de antes, e as provas de cima REPROVAM",
			   voltaram == linhas.Count && daFoto.All(antes["World"].Contains) && deOutros.All(antes[Verbos.Outros].Contains),
			   string.Join(" | ", linhas.Where(l => !antes[l.Aba].Contains(l.Texto)).Select(l => l.Texto)));
		await Tela(menu, null);
	}

	// =====================================================================
	// F2 -- CADA PERSONAGEM
	// =====================================================================
	private async Tarefa F2_CadaPersonagem(MenuJogo menu, List<(VerbDeclarado Linha, string Aba, string Texto)> linhas)
	{
		Nota("--- F2: cada personagem: botao na tela se e so se o requisito vale ---");
		const Lugar terra = Lugar.Planeta;

		static PosseEscrita Gente(string raca, string classe, Lugar lugar, Fatos fatos = Fatos.Nenhum,
								  string[]? tecnicas = null, string[]? skills = null, string[]? itens = null, string[]? cargos = null)
		{
			var p = new PosseEscrita { Raca = raca, Classe = classe, Lugar = lugar, Fatos = fatos };
			foreach (string t in tecnicas ?? []) p.Tecnicas.Add(t);
			foreach (string s in skills ?? []) p.Skills.Add(s);
			foreach (string i in itens ?? []) p.Itens.Add(i);
			foreach (string c in cargos ?? []) p.Cargos.Add(c);
			return p;
		}

		// (2026-10-17) o bio da escolha do DNA: o sangue que ele carrega e as skills que ja escolheu (`Races.SkillsRaciais`)
		static PosseEscrita ComSangue(PosseEscrita p, Jandirus.Core.Races.SkillRacial sangue, Jandirus.Core.Races.SkillRacial escolhidas)
		{
			p.SkillsDoSangue = sangue;
			p.SkillsRaciais = escolhidas;
			return p;
		}
		Jandirus.Core.Races.SkillRacial sangueInteiro = Jandirus.Core.Races.SkillsRaciais.Todas.Aggregate(
			Jandirus.Core.Races.SkillRacial.Nenhuma, (s, d) => s | d.Bit);

		// (2026-10-23) ...e o convite de APRENDIZ DE KAIOSHIN, cujos dois botoes ficaram sem plateia desde 2026-10-20 (a F2 reprovava
		// "110 de 114" por eles e pelo Name/Dismiss Apprentice, que o Kaioshin do elenco desenha agora).
		const Fatos ofertas = Fatos.ConviteDeMestre | Fatos.ConviteDeFusao | Fatos.ConviteDeAbsorcao | Fatos.LicaoOferecida
							| Fatos.OfertaDeJuventude | Fatos.OfertaDePotencial | Fatos.PedidoDeGenkidama | Fatos.OfertaDeAssento
							| Fatos.DesafioAoTitulo | Fatos.PedidoDeFilho | Fatos.OfertaDeAprendiz;
		(string Nome, PosseEscrita Quem)[] elenco =
		[
			("humano novato", Gente("Human", "Normal", terra)),
			("Saiyajin novato", Gente("Saiyan", "Low-Class", terra)),
			// (v0.4.0) OS SANGUES COM ATIVA RACIAL -- as treze linhas da aba Skills tem quem as desenhe (o humano e o Namekuseijin ja estao aqui).
			("Saiyajin Lendário", Gente("Saiyan", "Legendary", terra)),
			("Saiyajin Primal", Gente("Saiyan", "Normal Primal Saiyan", terra)),
			("Lendário Primal", Gente("Saiyan", "Legendary Primal Saiyan", terra)),
			("Frost Demon", Gente("Frost Demon", "Frost Demon", terra)),
			("Frost Demon mutante", Gente("Frost Demon", "Mutant Frost Demon", terra)),
			// (2026-10-13) A FUSAO NAMEKUSEIJIN PEDE A SKILL DELA (`Fusion- Namek Style`): o guerreiro e o do Cla a
			// aprenderam; o "Cla do Dragao que nao aprendeu a estatua" e o Namekuseijin que so tem o que e da raca.
			// (2026-10-17) E ELE E ADULTO: poe ovo (`Fatos.PoeOvo`) -- as racas de ovo tem filho sozinhas.
			("Namekuseijin guerreiro", Gente("Namekian", "Warrior clan", terra, Fatos.PoeOvo, skills: [RequisitosDosVerbos.SkillDaFusaoNamek])),
			// (2026-10-13) A ESTATUA PEDE A SKILL DELA (`Make Dragon Statue`): o do Cla que a aprendeu, e o que ainda nao.
			("Namekuseijin do Clã do Dragão", Gente("Namekian", "Dragon clan", terra,
				skills: [Jandirus.Core.Skills.AcendedoresDoPort.EstatuaDoDragao, RequisitosDosVerbos.SkillDaFusaoNamek])),
			("Clã do Dragão que não aprendeu a estátua", Gente("Namekian", "Dragon clan", terra)),
			("Clã do Dragão que ergueu a estátua daqui", Gente("Namekian", "Dragon clan", terra | Lugar.ComEstatua, Fatos.CriouAEstatuaDaqui)),
			("humano com Split Form", Gente("Human", "Normal", terra, tecnicas: ["SplitForm"])),
			("humano com Dragon Radar", Gente("Human", "Normal", terra, itens: [Jandirus.Core.Items.CatalogoDeItens.Radar])),
			("humano que sabe a Dança", Gente("Human", "Normal", terra, skills: ["/datum/skill/rank/Fusion_Dance"])),
			// (o `Power_Control` do degrau 5 de Basic Ki Control: e ele que abre a barra de poder e o relaxar)
			("humano com controle de ki", Gente("Human", "Normal", terra, tecnicas: [Jandirus.Core.Stats.PoderEmUso.VerboDoControle])),
			("Majin com Buu Absorb", Gente("Majin", "Normal", terra, tecnicas: [Jandirus.Core.Combat.AbsorcaoMajin.Verbo])),
			("bio-androide", Gente(Jandirus.Core.Races.BioAndroids.Raca, "Normal", terra)),
			// (2026-10-24) O BIO DE SANGUE MAJIN QUE COMPROU BUU ABSORB desenha o "Absorver como Majin" ao lado do "Absorver" (`ag/biomajin`).
			("bio-androide de sangue Majin com Buu Absorb", Gente(Jandirus.Core.Races.BioAndroids.Raca, "Normal", terra,
				tecnicas: [Jandirus.Core.Combat.AbsorcaoMajin.Verbo])),
			("bio-androide de DNA Saiyajin", Gente(Jandirus.Core.Races.BioAndroids.Raca, "Normal", terra, Fatos.EscadaSuperSaiyajin)),
			// (2026-10-17) O BIO QUE SAIU DE UM TANQUE veste a larva (`Fatos.BioDeLaboratorio`), e so a LARVA se enterra (`Fatos.EmLarva`).
			("bio-androide de laboratório", Gente(Jandirus.Core.Races.BioAndroids.Raca, "Normal", terra, Fatos.BioDeLaboratorio | Fatos.NucleoPorEscolher)),
			("larva de bio-androide", Gente(Jandirus.Core.Races.BioAndroids.Raca, "Normal", terra, Fatos.BioDeLaboratorio | Fatos.EmLarva)),
			// (2026-10-17) AS SKILLS DO DNA (`Fatos.SkillDoDnaPorEscolher`): o bio com vaga e o sangue das cinco racas de kit (os vinte botoes da
			// escolha), e o que ja escolheu Warrior Race e Second Wind de um sangue Saiyajin + Humano (sete botoes, e o da ativa escolhida).
			("bio-androide com o DNA por escolher", ComSangue(Gente(Jandirus.Core.Races.BioAndroids.Raca, "Normal", terra, Fatos.SkillDoDnaPorEscolher),
				sangueInteiro, Jandirus.Core.Races.SkillRacial.Nenhuma)),
			("bio-androide que já escolheu Warrior Race e Second Wind",
				ComSangue(Gente(Jandirus.Core.Races.BioAndroids.Raca, "Normal", terra, Fatos.SkillDoDnaPorEscolher),
					Jandirus.Core.Races.SkillsRaciais.DoKit(Jandirus.Core.Races.KitRacial.Saiyajin | Jandirus.Core.Races.KitRacial.Humano),
					Jandirus.Core.Races.SkillRacial.RacaGuerreira | Jandirus.Core.Races.SkillRacial.SegundoFolego)),
			("androide de absorção", Gente("Android", "Normal", terra, Fatos.AndroideDeAbsorcao)),
			// (2026-10-18) O NEO TUFFLE: o Tsufurujin que passou pela maquina ve o "Possuir um nocauteado" (`Fatos.NeoTuffle`).
			("Neo Tuffle", Gente("Tsujin", "Normal", terra, Fatos.NeoTuffle)),
			("humano no espaço", Gente("Human", "Normal", Lugar.Espaco)),
			("humano com tudo na mesa (ofertas, mestre, aluno, fusão, cópias, God Ki, arma de fogo, um amor)",
				Gente("Human", "Normal", terra, ofertas | Fatos.TemMestre | Fatos.TemAluno | Fatos.DirigeUmaFusao | Fatos.TemCopias | Fatos.GodKiDesperto
											 | Fatos.TemArmaDeFogo | Fatos.TemUmAmor)),
			("dono deste planeta", Gente("Human", "Normal", terra, Fatos.DominaEstePlaneta | Fatos.DominaAlgum)),
			("Grande Ancião de Namek", Gente("Namekian", "Dragon clan", terra, cargos: ["elder"])),
			("Rei de Vegeta", Gente("Saiyan", "Elite", terra, cargos: ["kov"])),
			("Presidente da Terra", Gente("Human", "Normal", terra, cargos: ["president"])),
			("Guardião da Terra", Gente("Namekian", "Dragon clan", terra, skills: ["/datum/skill/rank/Permission"], cargos: ["guardian"])),
			// (o kit do titulo: os verbs do `god_apply_powers`, `GodOfDestruction.dm:119-133`)
			("Deus da Destruição", Gente("Human", "Normal", terra, cargos: [Jandirus.Core.Ranks.KitDoDeus.Cargo])),
			("Deus da Destruição que trilha o Instinto",
				Gente("Human", "Normal", terra, Fatos.KitDaDestruicaoEmprestado, cargos: [Jandirus.Core.Ranks.KitDoDeus.Cargo])),
			// (2026-10-19) O KING YEMMA: a travessia pro Outro Mundo e do trono dele -- e quem perdeu o trono la dentro guarda a volta
			// (`Fatos.VoltaDoOutroMundo`, `GameServer.TravessiaDoJuiz.cs`).
			("King Yemma", Gente("Human", "Normal", terra, cargos: [Jandirus.Core.World.TravessiaDoJuiz.Cargo])),
			("ex-King Yemma vivo no Outro Mundo, com a volta guardada", Gente("Human", "Normal", terra, Fatos.VoltaDoOutroMundo)),
			// (2026-10-23) O KAIOSHIN: o Ritual Mistico do Velho Kaiohshin e do trono dele (e o Name/Dismiss Apprentice) -- e quem
			// RECEBE o ritual ve o mesmo botao enquanto ele dura, pra interromper (`Fatos.RitualDoKaioshin`, `GameServer.RitualDoKaioshin.cs`).
			("Kaioshin", Gente("Kai", "Normal", terra, cargos: [Jandirus.Core.Ranks.RitualDoKaioshin.Cargo])),
			("humano no meio de um Ritual Místico", Gente("Human", "Normal", terra, Fatos.RitualDoKaioshin)),
		];

		var telas = new Dictionary<string, Dictionary<string, HashSet<string>>>();
		var plateia = new HashSet<string>(StringComparer.Ordinal);
		foreach ((string nome, PosseEscrita quem) in elenco)
		{
			Dictionary<string, HashSet<string>> tela = await Tela(menu, quem);
			telas[nome] = tela;

			var errados = new List<string>();
			int ve = 0;
			foreach ((VerbDeclarado linha, string aba, string texto) in linhas)
			{
				bool devia = linha.Requisito.Atende(quem);
				bool tem = tela[aba].Contains(texto);
				if (tem) { ve++; plateia.Add(linha.Chave); }
				if (tem != devia) errados.Add($"{texto} ({(tem ? "na tela sem requisito" : "com requisito e fora da tela")})");
			}
			Checa($"{nome}: nas quatro abas, botao na tela SE E SO SE o requisito vale ({ve} dos {linhas.Count})",
				  errados.Count == 0, string.Join(" | ", errados));
		}
		PosseDoCliente.DeTeste = null;

		List<string> semPlateia = [.. linhas.Where(l => !plateia.Contains(l.Linha.Chave)).Select(l => $"{l.Texto} [{l.Linha.Chave}]")];
		Checa($"TODA linha foi DESENHADA pra pelo menos um personagem ({plateia.Count} de {linhas.Count}) -- nenhum rotulo da tabela e de um botao que nao existe",
			  semPlateia.Count == 0, string.Join(" | ", semPlateia));

		// ---- os casos do dono, pelo texto literal ----
		bool Tem(string quem, string aba, params string[] textos) => textos.All(telas[quem][aba].Contains);
		bool NaoTem(string quem, string aba, params string[] textos) => !textos.Any(telas[quem][aba].Contains);
		// (2026-10-22) o supremo mora no menu da estatua: o botao do Cla do Dragao e um so
		string[] doCla = ["Erguer Estátua do Dragão"];
		string[] doSplit = ["Split Form: Follow", "Split Form: Stop", "Split Form: Attack Target", "Split Form: Attack Nearest", "Split Form: Destroy"];

		Checa("VERBS DE CLA DO DRAGAO: o Namekuseijin do Cla QUE APRENDEU A ESTATUA ve Erguer Estatua e Erguer + SUPREMO; o do Cla que nao aprendeu, o guerreiro de Namek, o Saiyajin e o humano nao",
			  Tem("Namekuseijin do Clã do Dragão", "World", doCla) && NaoTem("Clã do Dragão que não aprendeu a estátua", "World", doCla)
			  && NaoTem("Namekuseijin guerreiro", "World", doCla)
			  && NaoTem("Saiyajin novato", "World", doCla) && NaoTem("humano novato", "World", doCla));
		Checa("...quem ERGUEU a estatua daqui ve Despertar as esferas e Ver/Pegar/Largar/INVOCAR -- e deixa de ver Erguer (ja ha uma)",
			  Tem("Clã do Dragão que ergueu a estátua daqui", "World", "Despertar as esferas", "Ver as esferas daqui", "Pegar esfera", "Largar tudo", "INVOCAR")
			  && NaoTem("Clã do Dragão que ergueu a estátua daqui", "World", doCla));
		Checa("VERB DE FUSAO NAMEK: os dois Namekuseijins QUE APRENDERAM a Fusion- Namek Style a veem na Learning; o Namekuseijin que nao a aprendeu, o Saiyajin e o humano nao",
			  Tem("Namekuseijin guerreiro", Verbos.Aprendizado, "Fusão Namekuseijin (absorver)")
			  && Tem("Namekuseijin do Clã do Dragão", Verbos.Aprendizado, "Fusão Namekuseijin (absorver)")
			  && NaoTem("Clã do Dragão que não aprendeu a estátua", Verbos.Aprendizado, "Fusão Namekuseijin (absorver)")
			  && NaoTem("Saiyajin novato", Verbos.Aprendizado, "Fusão Namekuseijin (absorver)")
			  && NaoTem("humano novato", Verbos.Aprendizado, "Fusão Namekuseijin (absorver)"));
		Checa("SPLIT FORM: quem tem a tecnica ve as cinco ordens na Other -- e, do elenco, so ele e quem tem copias de pe",
			  Tem("humano com Split Form", Verbos.Outros, doSplit)
			  && elenco.Where(e => !e.Quem.SabeTecnica("SplitForm") && (e.Quem.Fatos & Fatos.TemCopias) == 0)
					   .All(e => NaoTem(e.Nome, Verbos.Outros, doSplit)));
		Checa("RELAXAR so pra quem sabe controlar o proprio poder (dono, 2026-10-12; no DM e de todo corpo): quem tem o `Power_Control` "
			  + "ve \"Relaxar / ficar pronto\" na Skills, e do elenco so ele",
			  Tem("humano com controle de ki", Verbos.Skills, "Relaxar / ficar pronto")
			  && elenco.Where(e => e.Nome != "humano com controle de ki").All(e => NaoTem(e.Nome, Verbos.Skills, "Relaxar / ficar pronto")));
		Checa("O RADAR: so quem tem o Dragon Radar ve o botao Radar",
			  Tem("humano com Dragon Radar", "World", "Radar") && elenco.Where(e => e.Nome != "humano com Dragon Radar").All(e => NaoTem(e.Nome, "World", "Radar")));
		Checa("O SAIYAJIN ve o que e dele: Toggle SSJ Grades e Line of Succession na Other, Voltar ao normal na Skills",
			  Tem("Saiyajin novato", Verbos.Outros, "Toggle SSJ Grades", "Line of Succession") && Tem("Saiyajin novato", Verbos.Skills, "Voltar ao normal"));
		Checa("...e os graus sao de quem tem a ESCADA: o bio-androide de DNA Saiyajin ve Toggle SSJ Grades; o bio comum e o Namekuseijin nao",
			  Tem("bio-androide de DNA Saiyajin", Verbos.Outros, "Toggle SSJ Grades") && NaoTem("bio-androide", Verbos.Outros, "Toggle SSJ Grades")
			  && NaoTem("Namekuseijin guerreiro", Verbos.Outros, "Toggle SSJ Grades"));
		string[] doDna = [.. Jandirus.Core.Races.SkillsRaciais.Todas.Select(s => $"DNA: {s.Nome}")];
		const string jaEscolheu = "bio-androide que já escolheu Warrior Race e Second Wind";
		Checa("AS SKILLS DO DNA (o adendo do dono): o bio com vaga e o sangue das cinco racas de kit ve os VINTE botoes da escolha; o que ja escolheu "
			  + "Warrior Race e Second Wind (sangue Saiyajin + Humano) ve os SETE que sobram do sangue dele e o botao da ativa escolhida -- e nao o "
			  + "Max Power, que nao escolheu; ninguem mais do elenco ve botao de DNA",
			  Tem("bio-androide com o DNA por escolher", Verbos.Skills, doDna)
			  && telas[jaEscolheu][Verbos.Skills].Count(t => t.StartsWith("DNA: ", StringComparison.Ordinal)) == 7
			  && Tem(jaEscolheu, Verbos.Skills, "Second Wind") && NaoTem(jaEscolheu, Verbos.Skills, "Max Power", "DNA: Warrior Race", "DNA: Second Wind",
																										   "DNA: Perfect Control")
			  && elenco.Where(e => !e.Nome.StartsWith("bio-androide com o DNA", StringComparison.Ordinal) && e.Nome != jaEscolheu)
						   .All(e => NaoTem(e.Nome, Verbos.Skills, doDna)),
			  string.Join(" | ", telas[jaEscolheu][Verbos.Skills].Where(t => t.StartsWith("DNA: ", StringComparison.Ordinal))));
		Checa("AS SUPER ESFERAS: os tres botoes so pra quem esta no espaco",
			  Tem("humano no espaço", "World", "Super Esferas: placar", "Reivindicar Super Esfera", "Chamar o Super Shenlong")
			  && NaoTem("humano novato", "World", "Super Esferas: placar", "Reivindicar Super Esfera", "Chamar o Super Shenlong"));
		Checa("OS CARGOS: o Anciao nomeia, o Rei escolhe herdeiro, o Presidente deposita a verba, o Guardiao autoriza e solta -- cada um so o seu",
			  Tem("Grande Ancião de Namek", Verbos.Outros, "Appoint Elder") && NaoTem("Rei de Vegeta", Verbos.Outros, "Appoint Elder")
			  && Tem("Rei de Vegeta", Verbos.Outros, "Name Heir", "Remove Heir") && NaoTem("Presidente da Terra", Verbos.Outros, "Name Heir")
			  && Tem("Presidente da Terra", Verbos.Outros, "Fund Earth", "Rank Duty") && NaoTem("Grande Ancião de Namek", Verbos.Outros, "Fund Earth")
			  && Tem("Guardião da Terra", Verbos.Outros, "Time Chamber: Authorize", "Time Chamber: Release"));
		await Tela(menu, null);
	}

	// =====================================================================
	// F7 -- O VERB SEM CORPO NAO E BOTAO
	// =====================================================================
	/// <summary>
	/// O dono, 2026-10-13: *"tem mts verbs mortos q poderiam ser tirados"*. O verb que nao tem corpo neste jogo virava
	/// um botao cinza escrito "(nao portada)"; agora nao vira botao nenhum. Medido na TELA, com duas skills postas no
	/// livro do cliente: a Tree Mastery (o unico verb dela, `Pluck_Tree`, nao tem corpo) e a Afterimage Technique (o
	/// `Afterimage_Toggle` ganhou corpo no lote G14).
	/// </summary>
	private async Tarefa F7_OVerbSemCorpoNaoEBotao(MenuJogo menu, GameClient cli)
	{
		Nota("--- F7: o verb sem corpo nao vira botao (o \"(nao portada)\" cinza saiu do menu) ---");
		// (a F6 fechou o menu P pra apertar E no mundo: com ele fechado nenhum botao esta "na tela", e as duas reguas
		// daqui mediriam uma pagina vazia)
		if (E is { NaTela: true }) { ApertarE(); await Quadros(2); }
		menu.Abrir();
		await Quadros(3);
		string[] skills = ["/datum/skill/Tree_Mastery", "/datum/skill/ki/Afterimage"];
		List<string> postas = [.. skills.Where(cli.SkillsAprendidas.Add)];
		static List<string> Cinzas(Dictionary<string, HashSet<string>> tela) =>
			[.. tela.Values.SelectMany(b => b).Where(t => t.Contains("(nao portada)", StringComparison.Ordinal))];
		try
		{
			Habilidades.Montar(cli.Atributos.Raca ?? "");
			Dictionary<string, HashSet<string>> tela = await Tela(menu, null);
			string miragem = Jandirus.Core.Skills.Tecnicas.Get("Afterimage_Toggle")?.Nome ?? "";
			Checa("**NENHUM BOTAO \"(nao portada)\"** nas quatro abas de quem sabe uma skill cujo verb nao tem corpo (a Tree Mastery) -- e o verb que ganhou corpo esta la: \"Miragem: ligar / desligar\"",
				  Cinzas(tela).Count == 0 && miragem.Length > 0 && tela.Values.Any(b => b.Contains(miragem)),
				  $"cinzas: {string.Join(" | ", Cinzas(tela))}; miragem '{miragem}' na tela: {tela.Values.Any(b => b.Contains(miragem))}");

			Habilidades.BotaoCinzaDeTeste = true;
			Habilidades.Montar(cli.Atributos.Raca ?? "");
			Dictionary<string, HashSet<string>> antes = await Tela(menu, null);
			Injeta("com o botao cinza de volta, o verb morto da Tree Mastery reaparece escrito \"(nao portada)\" -- a fileira da v0.2.0",
				   Cinzas(antes).Count > 0, string.Join(" | ", Cinzas(antes)));
		}
		finally
		{
			Habilidades.BotaoCinzaDeTeste = false;
			foreach (string p in postas) cli.SkillsAprendidas.Remove(p);
			Habilidades.Montar(cli.Atributos.Raca ?? "");
			await Tela(menu, null);
		}
	}

	// =====================================================================
	// F8 -- DO CARGO, SO O ATAQUE DE KI VIRA "ENSINAR"
	// =====================================================================
	/// <summary>
	/// O dono, 2026-10-23: *"ta dando pra ensinar skills de entrar na sala do tempo pros outros, unica skill de cargo q pode ensinar
	/// é a skill de ataques de ki"*. Medido na TELA (aba Learning), como a F7, com tres skills de cargo postas no livro do cliente: a
	/// chave da Sala do Tempo (`rank/Permission`, que nao se ensina mais), o Makankosappo (ataque de ki) e a Fusion Dance (que se
	/// ensina por escolha do dono). O "Esquecer a licao" da chave fica: ele e de quem a aprendeu, e nao de quem a ensinaria.
	/// O defeito injetado e o da regra: `EnsinoDeSkill.CargoSeEnsinaComoAntesDeTeste` (o kit inteiro ensinavel, como antes).
	/// </summary>
	private async Tarefa F8_OEnsinarDoCargo(MenuJogo menu, GameClient cli)
	{
		Nota("--- F8: do cargo, so o ataque de ki vira \"Ensinar\" (a chave da Sala do Tempo nao) ---");
		menu.Abrir();
		await Quadros(3);
		Jandirus.Core.Skills.SkillCatalog? cat = MenuJogo.CatalogoPublico();
		const string chave = "/datum/skill/rank/Permission", tiro = "/datum/skill/rank/Makkankosappo", danca = "/datum/skill/rank/Fusion_Dance";
		// (2026-10-23) ...e o Potential Unleashed (a skill `kai/Mystic`), que tambem deixou de se ensinar: so o ritual o da (F8m).
		const string mistico = Jandirus.Core.Ranks.RitualDoKaioshin.SkillDoMistico;
		string nChave = cat?.Get(chave)?.Nome ?? "", nTiro = cat?.Get(tiro)?.Nome ?? "", nDanca = cat?.Get(danca)?.Nome ?? "";
		string nMistico = cat?.Get(mistico)?.Nome ?? "";
		Checa("F8. PRECONDICAO: o catalogo do cliente tem as quatro (a chave da Sala, o Makankosappo, a Fusion Dance e o Potential Unleashed)",
			  nChave.Length > 0 && nTiro.Length > 0 && nDanca.Length > 0 && nMistico.Length > 0, $"'{nChave}' '{nTiro}' '{nDanca}' '{nMistico}'");
		List<string> postas = [.. new[] { chave, tiro, danca, mistico }.Where(cli.SkillsAprendidas.Add)];
		static bool NaAprendizado(Dictionary<string, HashSet<string>> tela, string texto) =>
			tela.TryGetValue(Verbos.Aprendizado, out HashSet<string>? b) && b.Contains(texto);
		try
		{
			Habilidades.Montar(cli.Atributos.Raca ?? "");
			Dictionary<string, HashSet<string>> tela = await Tela(menu, null);
			string doLearning = string.Join(" | ", tela.GetValueOrDefault(Verbos.Aprendizado) ?? []);
			Checa($"F8a. quem sabe a chave da Sala do Tempo NAO ve \"Ensinar: {nChave}\" na aba Learning -- e o \"Esquecer a licao: {nChave}\" continua la",
				  !NaAprendizado(tela, $"Ensinar: {nChave}") && NaAprendizado(tela, $"Esquecer a licao: {nChave}"), doLearning);
			Checa($"F8b. ...e ve \"Ensinar: {nTiro}\" (ataque de ki do cargo) e \"Ensinar: {nDanca}\" (escolha do dono)",
				  NaAprendizado(tela, $"Ensinar: {nTiro}") && NaAprendizado(tela, $"Ensinar: {nDanca}"), doLearning);
			Checa($"F8m. quem tem o Potential Unleashed NAO ve \"Ensinar: {nMistico}\" (so o ritual o da, 2026-10-23) -- e o \"Esquecer a licao: {nMistico}\" continua la",
				  !NaAprendizado(tela, $"Ensinar: {nMistico}") && NaAprendizado(tela, $"Esquecer a licao: {nMistico}"), doLearning);

			Jandirus.Core.Skills.EnsinoDeSkill.CargoSeEnsinaComoAntesDeTeste = true;
			Habilidades.Montar(cli.Atributos.Raca ?? "");
			Dictionary<string, HashSet<string>> antes = await Tela(menu, null);
			Injeta($"F8x. com o kit de cargo ensinavel de novo, o \"Ensinar: {nChave}\" volta pra aba Learning -- o botao de antes de 2026-10-23",
				   NaAprendizado(antes, $"Ensinar: {nChave}"), string.Join(" | ", antes.GetValueOrDefault(Verbos.Aprendizado) ?? []));
			Jandirus.Core.Skills.EnsinoDeSkill.CargoSeEnsinaComoAntesDeTeste = false;

			Jandirus.Core.Skills.EnsinoDeSkill.MisticoSeEnsinaComoAntesDeTeste = true;
			Habilidades.Montar(cli.Atributos.Raca ?? "");
			Dictionary<string, HashSet<string>> comMistico = await Tela(menu, null);
			Injeta($"F8mx. com o Potential Unleashed ensinavel de novo, o \"Ensinar: {nMistico}\" volta pra aba Learning -- o botao de antes de 2026-10-23",
				   NaAprendizado(comMistico, $"Ensinar: {nMistico}"), string.Join(" | ", comMistico.GetValueOrDefault(Verbos.Aprendizado) ?? []));
		}
		finally
		{
			Jandirus.Core.Skills.EnsinoDeSkill.CargoSeEnsinaComoAntesDeTeste = false;
			Jandirus.Core.Skills.EnsinoDeSkill.MisticoSeEnsinaComoAntesDeTeste = false;
			foreach (string p in postas) cli.SkillsAprendidas.Remove(p);
			Habilidades.Montar(cli.Atributos.Raca ?? "");
			await Tela(menu, null);
		}
	}

	// =====================================================================
	// F9 -- SEM NINGUEM MARCADO, A CHAVE E O DESTRANCAR VAO EM MIM
	// =====================================================================
	/// <summary>
	/// O dono, 2026-10-23: *"permita o proprio guardiao poder se autorizar a entrar na sala do tempo"*. O proprio corpo nao se marca (o
	/// duplo clique nele SOLTA o alvo), entao sem ninguem marcado os dois botoes do Guardiao vao no proprio id. Medido no FIO: o espiao de
	/// verbos do cliente (`GameClient.EspiaoDeVerbos`) pega o que o botao manda e o engole -- nada chega ao servidor, e o novato da bancada
	/// nem precisa saber fazer a chave. O que o servidor faz com o id e a `--salateste` que prova (secao 19).
	/// </summary>
	private void F9_AChaveEmMim(GameClient cli)
	{
		Nota("--- F9: sem ninguem marcado, a chave da Sala do Tempo e o destrancar vao no proprio id ---");
		Verbo? autorizar = Verbos.Todos.FirstOrDefault(v => v.Nome == "Time Chamber: Authorize");
		Verbo? soltar = Verbos.Todos.FirstOrDefault(v => v.Nome == "Time Chamber: Release");
		Checa("F9. PRECONDICAO: os dois botoes estao no registro, e ninguem esta marcado",
			  autorizar?.Acionar != null && soltar?.Acionar != null && cli.AlvoId == 0, $"alvo {cli.AlvoId}");
		if (autorizar?.Acionar is not { } aAutorizar || soltar?.Acionar is not { } aSoltar) return;

		List<(string Cmd, string Arg)> saiu = [];
		string Fio() => string.Join(" | ", saiu.Select(s => $"{s.Cmd}:{s.Arg}"));
		Func<string, string, bool>? espiaoDeAntes = GameClient.EspiaoDeVerbos;
		GameClient.EspiaoDeVerbos = (cmd, arg) => { saiu.Add((cmd, arg)); return false; };
		try
		{
			string eu = cli.LocalId.ToString();
			aAutorizar();
			Checa($"F9a. \"Time Chamber: Authorize\" sem ninguem marcado manda a chave pro PROPRIO id ({eu})",
				  saiu.Count == 1 && saiu[0] == ("sala_autorizar", eu), Fio());
			saiu.Clear();
			aSoltar();
			Checa("F9b. ...e \"Time Chamber: Release\" tambem: o Guardiao preso se destranca por dentro",
				  saiu.Count == 1 && saiu[0] == ("sala_soltar", eu), Fio());
			Checa("F9c. ...e com alguem marcado o gesto continua indo NELE (a conta do botao, sem mexer na mira de verdade)",
				  VerbosDoJogo.AlvoOuEu(7, 5) == 7 && VerbosDoJogo.AlvoOuEu(0, 5) == 5);

			saiu.Clear();
			VerbosDoJogo.SoNoAlvoMarcadoDeTeste = true;
			aAutorizar();
			Injeta("F9x. com a chave pedindo alvo marcado de novo, sem ninguem marcado o botao nao manda nada -- o Guardiao nao se autoriza, como antes",
				   saiu.Count == 0, Fio());
		}
		finally
		{
			VerbosDoJogo.SoNoAlvoMarcadoDeTeste = false;
			GameClient.EspiaoDeVerbos = espiaoDeAntes;
		}
	}

	// =====================================================================
	// F10 -- O CLIQUE NA COPIA (dono, 2026-10-24)
	// =====================================================================
	/// <summary>
	/// O dono, 2026-10-24: *"Eu clicava na copia como no DM e nao aparecia nada; nao achei onde dar as ordens"*. O clique DE VERDADE (o
	/// mouse aperta e solta, empurrado no viewport na coordenada de tela da copia) numa copia minha abre o menu do `Click()` do DM, e a
	/// ordem sai com o id DELA -- medida no servidor deste processo (`--host`): so a clicada obedece. As copias vem da divisao de producao
	/// (`GameServer.CopiasDeTeste`), e o cliente as conhece pelo `S2C.MinhasCopias`.
	/// </summary>
	private async Tarefa F10_OCliqueNaCopia(MenuJogo menu, GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F10: o clique na copia abre as ordens dela, e a ordem e so dela (dono, 2026-10-24) ---");
		string[] ordens = ["Follow", "Stop", "Attack Target", "Attack Nearest", "Destroy Splitforms"];
		menu.Fechar();
		if (E is { NaTela: true }) { ApertarE(); await Quadros(2); }
		Vector2? NoMundo(int id) => World.Instancia?.PosicaoDesenhadaDe(id);
		bool Afastada(int id) => NoMundo(id) is { } p && NoMundo(cli.LocalId) is { } eu && p.DistanceTo(eu) > 48;
		List<int> copias = srv.CopiasDeTeste(cli.LocalId, 2);
		Func<string, string, bool>? espiaoDeAntes = GameClient.EspiaoDeVerbos;
		try
		{
			bool chegaram = await Ate(() => copias.Count == 2 && copias.All(cli.MinhasCopias.Contains)
										&& (cli.Fatos & Fatos.TemCopias) != 0 && copias.All(Afastada), 6);
			await Quadros(10);
			Checa("F10. (palco) duas copias minhas de pe, uma de cada lado: o servidor as conta pro cliente (`S2C.MinhasCopias`) e elas estao desenhadas",
				  chegaram, $"copias {string.Join(",", copias)}; minhas {string.Join(",", cli.MinhasCopias)}; fatos {cli.Fatos}");
			if (!chegaram) return;
			int a = copias[0], b = copias[1];

			// ---- 1. o clique de verdade na A ----
			await ClicarNoMundo(NoMundo(a)!.Value);
			List<string> botoes = E is { NaTela: true } e1 ? e1.BotoesDesenhados() : [];
			Checa("F10a. **O CLIQUE NA COPIA ABRE AS ORDENS DELA** (o `Click()` do DM): Follow, Stop, Attack Target, Attack Nearest e Destroy Splitforms -- e o menu e o DELA",
				  botoes.SequenceEqual(ordens) && E?.CopiaAbertaDeTeste == a, $"{string.Join(" | ", botoes)}; copia aberta {E?.CopiaAbertaDeTeste}");

			// ---- 2. Follow: so a A ----
			List<(string Cmd, string Arg)> saiu = [];
			GameClient.EspiaoDeVerbos = (cmd, arg) => { saiu.Add((cmd, arg)); return true; };
			E?.ApertarDesenhado("Follow");
			GameClient.EspiaoDeVerbos = espiaoDeAntes;
			bool soA = await Ate(() => srv.OrdemDaCopiaDeTeste(a) == "Seguir" && srv.OrdemDaCopiaDeTeste(b) == "Parado", 3);
			Checa("F10b. **...E A ORDEM E SO DELA**: 'Follow' manda `splitform_seguir` com o id da A, a A segue e a B continua parada -- e o menu fecha",
				  soA && saiu.Count == 1 && saiu[0] == ("splitform_seguir", a.ToString()) && E is { NaTela: false },
				  $"fio {string.Join(" | ", saiu.Select(s => s.Cmd + ":" + s.Arg))}; A {srv.OrdemDaCopiaDeTeste(a)}, B {srv.OrdemDaCopiaDeTeste(b)}");

			// ---- o contra-exemplo: a ordem do clique pra todas (o servidor de antes) ----
			cli.SendVerbo("splitform_parar", "");
			await Ate(() => srv.OrdemDaCopiaDeTeste(a) == "Parado" && srv.OrdemDaCopiaDeTeste(b) == "Parado", 3);
			Jandirus.Server.GameServer.OrdemDaCopiaPraTodasDeTeste = true;
			try
			{
				await ClicarNoMundo(NoMundo(a)!.Value);
				E?.ApertarDesenhado("Follow");
				bool asDuas = await Ate(() => srv.OrdemDaCopiaDeTeste(a) == "Seguir" && srv.OrdemDaCopiaDeTeste(b) == "Seguir", 3);
				Injeta("F10x. com a ordem do clique valendo pra todas (o jogo de antes), a B segue junto -- a regua F10b REPROVA", asDuas,
					   $"A {srv.OrdemDaCopiaDeTeste(a)}, B {srv.OrdemDaCopiaDeTeste(b)}");
			}
			finally { Jandirus.Server.GameServer.OrdemDaCopiaPraTodasDeTeste = false; }
			if (E is { NaTela: true }) { ApertarE(); await Quadros(2); }
			cli.SendVerbo("splitform_parar", "");
			await Ate(() => srv.OrdemDaCopiaDeTeste(a) == "Parado" && srv.OrdemDaCopiaDeTeste(b) == "Parado", 3);
			await Segundos(0.5);

			// ---- 3. o clique em mim e no chao vazio: nada ----
			Vector2 eu = NoMundo(cli.LocalId)!.Value;
			await ClicarNoMundo(eu);
			bool emMim = E is { NaTela: true };
			if (emMim) { ApertarE(); await Quadros(2); }
			await ClicarNoMundo(eu + new Vector2(0, 5 * 32));
			bool noChao = E is { NaTela: true };
			if (noChao) { ApertarE(); await Quadros(2); }
			Checa("F10c. CONTRA-EXEMPLO: o clique em MIM e no chao vazio nao abre menu nenhum", !emMim && !noChao, $"em mim {emMim}, no chao {noChao}");

			// ---- 4. o duplo clique na copia: o menu que o primeiro clique abriu fecha, e o segundo clique nao vira ordem ----
			async System.Threading.Tasks.Task<(bool Aberto, int Ordens)> Duplo()
			{
				List<(string Cmd, string Arg)> noDuplo = [];
				GameClient.EspiaoDeVerbos = (cmd, arg) => { noDuplo.Add((cmd, arg)); return true; };
				try { await ClicarNoMundo(NoMundo(b)!.Value, duplo: true); }
				finally { GameClient.EspiaoDeVerbos = espiaoDeAntes; }
				bool aberto = E is { NaTela: true };
				if (aberto) { ApertarE(); await Quadros(2); }
				return (aberto, noDuplo.Count(v => v.Cmd.StartsWith("splitform_", StringComparison.Ordinal)));
			}
			(bool Aberto, int Ordens) duplo = await Duplo();
			Checa("F10d. O DUPLO CLIQUE NA COPIA nao deixa o menu dela aberto, nem vira ordem: o primeiro clique o abre, o segundo o fecha e segue pro mundo (o gesto de marcar alvo)",
				  !duplo.Aberto && duplo.Ordens == 0, $"aberto {duplo.Aberto}, ordens {duplo.Ordens}");
			MenuDeInteracao.DuploDeixaOMenuDaCopiaDeTeste = true;
			try
			{
				(bool Aberto, int Ordens) comDefeito = await Duplo();
				Injeta("F10y. com o duplo clique deixando o menu da copia aberto, o segundo clique cai no menu -- a regua F10d REPROVA",
					   comDefeito.Aberto || comDefeito.Ordens > 0, $"aberto {comDefeito.Aberto}, ordens {comDefeito.Ordens}");
			}
			finally { MenuDeInteracao.DuploDeixaOMenuDaCopiaDeTeste = false; }
			cli.SendAlvo(0);
			cli.SendVerbo("splitform_parar", "");
			await Ate(() => srv.OrdemDaCopiaDeTeste(a) == "Parado" && srv.OrdemDaCopiaDeTeste(b) == "Parado", 3);

			// ---- 5. Destroy Splitforms pelo menu de uma: as duas se desfazem, a lista chega vazia, e o clique ali nao abre mais nada ----
			Vector2 ondeEstavaA = NoMundo(a)!.Value;
			await ClicarNoMundo(ondeEstavaA);
			E?.ApertarDesenhado("Destroy Splitforms");
			bool sumiram = await Ate(() => cli.MinhasCopias.Count == 0 && srv.OrdemDaCopiaDeTeste(a) == null && srv.OrdemDaCopiaDeTeste(b) == null, 4);
			await Quadros(5);
			await ClicarNoMundo(ondeEstavaA);
			Checa("F10e. 'Destroy Splitforms' (o do DM: todas): as duas se desfazem, o cliente fica sem copia nenhuma -- e o clique onde a A estava nao abre nada",
				  sumiram && E is { NaTela: false }, $"minhas {cli.MinhasCopias.Count}; A {srv.OrdemDaCopiaDeTeste(a)}, B {srv.OrdemDaCopiaDeTeste(b)}");
			if (E is { NaTela: true }) { ApertarE(); await Quadros(2); }

			// ---- o contra-exemplo do pacote: calado, o cliente nao sabe da copia, e o clique nela nao abre nada ----
			Jandirus.Server.GameServer.CopiasCaladasDeTeste = true;
			try
			{
				List<int> calada = srv.CopiasDeTeste(cli.LocalId, 1);
				bool desenhada = await Ate(() => calada.Count == 1 && Afastada(calada[0]), 4);
				await Quadros(10);
				if (desenhada) await ClicarNoMundo(NoMundo(calada[0])!.Value);
				Injeta("F10z. com o pacote das copias calado, o cliente nao sabe que a copia e dele e o clique nela NAO abre o menu -- a regua F10a REPROVA",
					   desenhada && E is { NaTela: false }, $"desenhada {desenhada}, menu {E?.NaTela}");
			}
			finally { Jandirus.Server.GameServer.CopiasCaladasDeTeste = false; }
		}
		finally
		{
			GameClient.EspiaoDeVerbos = espiaoDeAntes;
			MenuDeInteracao.DuploDeixaOMenuDaCopiaDeTeste = false;
			Jandirus.Server.GameServer.OrdemDaCopiaPraTodasDeTeste = false;
			Jandirus.Server.GameServer.CopiasCaladasDeTeste = false;
			if (E is { NaTela: true }) { ApertarE(); await Quadros(2); }
			srv.DesfazerCopiasDeTeste(cli.LocalId);
		}
	}

	/// <summary>
	/// UM CLIQUE DE VERDADE NUM PONTO DO MUNDO: o mouse chega, aperta e solta (e, `duplo`, aperta de novo como duplo clique) -- empurrado
	/// no viewport na coordenada de TELA desse ponto (o caminho inverso do que o `World` faz com o clique), e passa por quem estiver em
	/// cima, como o mouse do jogador.
	/// </summary>
	private async Tarefa ClicarNoMundo(Vector2 mundo, bool duplo = false)
	{
		Vector2 p = (World.Instancia?.GetCanvasTransform() ?? GetViewport().GetCanvasTransform()) * mundo;
		void Botao(bool desce, bool dobra) =>
			GetViewport().PushInput(new InputEventMouseButton
			{
				ButtonIndex = MouseButton.Left, Pressed = desce, DoubleClick = dobra, Position = p, GlobalPosition = p,
				ButtonMask = desce ? MouseButtonMask.Left : 0,
			}, true);
		GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true);
		Botao(true, false);
		Botao(false, false);
		if (duplo)
		{
			await Quadros(1);
			Botao(true, true);
			Botao(false, false);
		}
		await Quadros(3);
	}

	// =====================================================================
	// F3 -- A BUSCA E A TECLA LIGADA
	// =====================================================================
	private void F3_ABuscaEATecla()
	{
		Nota("--- F3: a busca e a tecla ligada passam pelo mesmo crivo ---");
		var comSplit = new PosseEscrita { Raca = "Human", Classe = "Normal", Lugar = Lugar.Planeta };
		comSplit.Tecnicas.Add("SplitForm");
		var namek = new PosseEscrita { Raca = "Namekian", Classe = "Warrior clan", Lugar = Lugar.Planeta };
		namek.Skills.Add(RequisitosDosVerbos.SkillDaFusaoNamek);
		var namekSemASkill = new PosseEscrita { Raca = "Namekian", Classe = "Warrior clan", Lugar = Lugar.Planeta };

		// (so as ORDENS -- a busca tambem casa a descricao, e outro verb pode citar o Split Form na frase dele)
		static int Ordens() => Verbos.Buscar("Split Form").Count(v => v.Nome.StartsWith("Split Form:", StringComparison.Ordinal));

		PosseDoCliente.DeTeste = null;
		int doNovato = Ordens();
		bool teclaDoNovato = Verbos.PorChave("hab:fus_namek") != null;
		PosseDoCliente.DeTeste = comSplit;
		int deQuemTem = Ordens();
		PosseDoCliente.DeTeste = namek;
		bool teclaDoNamek = Verbos.PorChave("hab:fus_namek") != null;
		PosseDoCliente.DeTeste = namekSemASkill;
		bool teclaDoNamekSemASkill = Verbos.PorChave("hab:fus_namek") != null;
		Requisito.MostrarTudoDeTeste = true;
		PosseDoCliente.DeTeste = null;
		int comOBotao = Ordens();
		Requisito.MostrarTudoDeTeste = false;

		Checa("A BUSCA: \"Split Form\" nao acha nada pra quem nao tem a tecnica, e acha as cinco ordens pra quem tem",
			  doNovato == 0 && deQuemTem == 5, $"novato {doNovato}, quem tem {deQuemTem}");
		Checa("A TECLA: uma tecla ligada a Fusao Namekuseijin nao acha o verb num humano (\"este personagem nao tem essa acao\") nem num Namekuseijin que nao aprendeu a skill dela, e acha no que aprendeu",
			  !teclaDoNovato && teclaDoNamek && !teclaDoNamekSemASkill, $"humano {teclaDoNovato}, namek {teclaDoNamek}, namek sem a skill {teclaDoNamekSemASkill}");
		Injeta("com o botao que mostra tudo a busca do novato acha as cinco ordens do Split Form", comOBotao == 5, $"{comOBotao}");
	}

	// =====================================================================
	// F4 -- O RADAR DOURADO, PELO FIO
	// =====================================================================
	private async Tarefa F4_ORadarPeloFio(MenuJogo menu, GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F4: o radar dourado pelo fio (sem o aparelho a frase nem chega) ---");
		string radar = Jandirus.Core.Items.CatalogoDeItens.Radar;
		bool LinhaNaTela() => RotulosDa(menu, "World").Any(t => t.StartsWith("RADAR:", StringComparison.Ordinal));

		// A SEMENTE DO MUNDO E SORTEADA: o palco e um ciclo das Super em que alguma esta ao alcance de quem esta na Terra.
		int cicloDeAntes = srv.SinalNaTerraDeTeste();
		Checa("(palco) ha um ciclo das Super Esferas com uma delas a ate 2 celulas da Terra", cicloDeAntes >= 0);
		if (cicloDeAntes < 0) return;
		try
		{
			menu.IrPara("World");
			await Segundos(2.2);   // dois tiques do radar (1 Hz)
			Checa("**SEM O DRAGON RADAR, COM UMA SUPER ESFERA AO ALCANCE, A FRASE NAO CHEGA AO CLIENTE** -- e a linha nao esta na World",
				  cli.SinalDourado.Length == 0 && !LinhaNaTela(), $"chegou '{cli.SinalDourado}'");

			// O servidor de ANTES (mandava pra todo mundo): a frase chega -- e o cliente, sozinho, ainda a segura.
			srv.SinalSemRadarDeTeste = true;
			bool vazou = await Ate(() => cli.SinalDourado.StartsWith("RADAR:", StringComparison.Ordinal), 4);
			await Quadros(3);
			Injeta("com o servidor mandando o sinal pra quem nao tem radar, a frase CHEGA ao cliente (e a que o dono leu)", vazou, cli.SinalDourado);
			Checa("...e mesmo chegando o CLIENTE nao a desenha: a linha tambem pede o Dragon Radar na mochila (dois portoes, e o de baixo e o que conta)",
				  vazou && !LinhaNaTela());
			Requisito.MostrarTudoDeTeste = true;
			menu.ForcarRedesenho();
			await Quadros(3);
			Injeta("...e com o botao que mostra tudo a linha do RADAR volta pra tela de quem nao tem radar", LinhaNaTela());
			Requisito.MostrarTudoDeTeste = false;
			srv.SinalSemRadarDeTeste = false;
			bool calou = await Ate(() => cli.SinalDourado.Length == 0, 4);
			menu.ForcarRedesenho();
			await Quadros(3);
			Checa("desligado o defeito, o tique seguinte esvazia a frase no cliente e a linha some", calou && !LinhaNaTela(), cli.SinalDourado);

			// ---- o aparelho chega ----
			srv.RadarNaMochilaDeTeste(cli.LocalId, dar: true);
			bool chegou = await Ate(() => cli.Mochila.Quantos(radar) > 0 && cli.SinalDourado.StartsWith("RADAR:", StringComparison.Ordinal), 5);
			bool apareceu = await Ate(() => LinhaNaTela() && BotoesDa(menu, "World").Contains("Radar"), 3);
			Checa("**COM O DRAGON RADAR NA MOCHILA** a frase chega em ate um tique, e a linha E o botao Radar NASCEM na World sem reabrir o menu",
				  chegou && apareceu, $"mochila {cli.Mochila.Quantos(radar)}, sinal '{cli.SinalDourado}', linha {LinhaNaTela()}, botao {BotoesDa(menu, "World").Contains("Radar")}");

			srv.RadarNaMochilaDeTeste(cli.LocalId, dar: false);
			bool sumiu = await Ate(() => cli.Mochila.Quantos(radar) == 0 && !LinhaNaTela() && !BotoesDa(menu, "World").Contains("Radar"), 3);
			bool esvaziou = await Ate(() => cli.SinalDourado.Length == 0, 4);
			Checa("...o radar SAI da mochila: a linha e o botao somem, e a frase esvazia no cliente no tique seguinte", sumiu && esvaziou,
				  $"mochila {cli.Mochila.Quantos(radar)}, sinal '{cli.SinalDourado}'");
		}
		finally
		{
			srv.SinalSemRadarDeTeste = false;
			Requisito.MostrarTudoDeTeste = false;
			srv.RadarNaMochilaDeTeste(cli.LocalId, dar: false);
			srv.CicloDasSupersDeTeste(cicloDeAntes);
		}
	}

	// =====================================================================
	// F5 -- A TECNICA E A OFERTA, PELO FIO
	// =====================================================================
	private async Tarefa F5_ATecnicaEAOfertaPeloFio(MenuJogo menu, GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F5: a tecnica e a oferta pelo fio (o botao nasce quando o servidor conta) ---");
		string[] doSplit = ["Split Form: Follow", "Split Form: Stop", "Split Form: Attack Target", "Split Form: Attack Nearest", "Split Form: Destroy"];
		const string skill = "/datum/skill/general/splitform";

		menu.IrPara(Verbos.Outros);
		await Quadros(3);
		Checa("PRECONDICAO: o host e admin (o Split Form chega por `admin_skill_dar`) e ainda nao tem as ordens na Other",
			  Verbos.SouAdmin && !doSplit.Any(BotoesDa(menu, Verbos.Outros).Contains));

		cli.SendVerbo("admin_skill_dar", $"{cli.LocalId}|{skill}");
		bool aprendeu = await Ate(() => cli.VerbosAtivos.Contains("SplitForm"), 8);
		bool nasceram = await Ate(() => doSplit.All(BotoesDa(menu, Verbos.Outros).Contains), 3);
		Checa("**GANHEI O SPLIT FORM**: o livro chega com o verb e as CINCO ordens nascem na Other sem reabrir o menu",
			  aprendeu && nasceram, $"verbos ativos: {string.Join(",", cli.VerbosAtivos)}");

		cli.SendVerbo("admin_skill_tirar", $"{cli.LocalId}|{skill}");
		bool esqueceu = await Ate(() => !cli.VerbosAtivos.Contains("SplitForm"), 8);
		bool sumiram = await Ate(() => !doSplit.Any(BotoesDa(menu, Verbos.Outros).Contains), 3);
		Checa("...perdi a tecnica: as cinco somem de novo", esqueceu && sumiram);

		// ---- um FATO: a oferta de juventude ----
		Checa("(antes) sem oferta na mesa, 'Accept Youth' e 'Decline Youth' nao estao na Other, e o fato esta apagado",
			  (cli.Fatos & Fatos.OfertaDeJuventude) == 0 && !BotoesDa(menu, Verbos.Outros).Contains("Accept Youth"));
		srv.OfertaDeJuventudeDeTeste(cli.LocalId);
		bool contou = await Ate(() => (cli.Fatos & Fatos.OfertaDeJuventude) != 0, 3);
		bool ofereceu = await Ate(() => BotoesDa(menu, Verbos.Outros).Contains("Accept Youth") && BotoesDa(menu, Verbos.Outros).Contains("Decline Youth"), 3);
		Checa("**UMA OFERTA CAI NA MESA**: o servidor conta (o pacote dos fatos, em ate 0,2 s) e os dois botoes de resposta nascem na Other",
			  contou && ofereceu, $"fatos {cli.Fatos}");

		Button? recusar = menu.PaginaDeTeste(Verbos.Outros) is { } pg
			? Todos(pg).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.Text == "Decline Youth") : null;
		recusar?.EmitSignal(BaseButton.SignalName.Pressed);
		bool respondeu = await Ate(() => (cli.Fatos & Fatos.OfertaDeJuventude) == 0, 4);
		bool foramSe = await Ate(() => !BotoesDa(menu, Verbos.Outros).Contains("Accept Youth") && !BotoesDa(menu, Verbos.Outros).Contains("Decline Youth"), 3);
		Checa("...apertei 'Decline Youth' (o botao de verdade): a oferta sai da mesa no servidor, o fato apaga e os dois botoes somem",
			  recusar != null && respondeu && foramSe, $"fatos {cli.Fatos}");
	}

	// =====================================================================
	// F6 -- A TECLA E, NO MAINFRAME
	// =====================================================================
	private async Tarefa F6_ATeclaE(MenuJogo menu, GameClient cli, Jandirus.Server.GameServer srv)
	{
		Nota("--- F6: a tecla E no mainframe de androides (as opcoes de humano so pra humano) ---");
		string[] deHumano = ["Instalar laboratório...", "Converter meu corpo..."];
		string[] deTodos = ["Aparafusar / soltar", "Colher DNA de um nocauteado", "Gestar bio-androide"];
		menu.Fechar();
		await Quadros(2);

		srv.MainframeDeTeste(cli.LocalId, por: true);
		try
		{
			bool chegou = await Ate(() => cli.Obras.Any(o => o.Tipo == "Android_Creation_Mainframe"), 5);
			await Quadros(5);
			Checa("(palco) o mainframe chegou na lista de construcoes do cliente, aos pes dele", chegou);
			if (!chegou) return;

			List<string> humano = await AbrirOMenuE(null);
			Checa("UM HUMANO aperta E no mainframe: o menu abre com Instalar laboratório... e Converter meu corpo... (a maquina e dele)",
				  deHumano.All(humano.Contains) && deTodos.All(humano.Contains), string.Join(" | ", humano));

			var saiyajin = new PosseEscrita { Raca = "Saiyan", Classe = "Low-Class", Lugar = Lugar.Planeta };
			List<string> outro = await AbrirOMenuE(saiyajin);
			Checa("UM SAIYAJIN aperta E no mesmo mainframe: as duas opcoes de humano NAO estao -- e as que nao pedem raca continuam",
				  !deHumano.Any(outro.Contains) && deTodos.All(outro.Contains), string.Join(" | ", outro));

			Requisito.MostrarTudoDeTeste = true;
			List<string> antes = await AbrirOMenuE(saiyajin);
			Requisito.MostrarTudoDeTeste = false;
			Injeta("com o botao que mostra tudo o Saiyajin ve as duas opcoes de humano de volta", deHumano.All(antes.Contains), string.Join(" | ", antes));

			// ---- e dentro do submenu, pro humano, as duas instalacoes ----
			await AbrirOMenuE(null, fechar: false);
			bool entrou = E?.ApertarDesenhado("Instalar laboratório...") == true;
			await Quadros(3);
			List<string> labs = E?.BotoesDesenhados() ?? [];
			Checa("...e 'Instalar laboratório...' abre a pagina com Android Lab e Bio-Android Lab (a tabela nao comeu o submenu de quem pode)",
				  entrou && labs.Contains("Android Lab") && labs.Contains("Bio-Android Lab"), string.Join(" | ", labs));

			// ---- e a pagina da CONVERSAO: as duas, cada uma em zeni e em recursos (dono, 2026-10-10) -- e as QUATRO so pra humano ----
			string[] conversoes =
			[
				"Androide de Absorção (1.000.000 zeni)", "Androide de Absorção (2.000.000 recursos)",
				"Androide de Energia Infinita (2.000.000 zeni)", "Androide de Energia Infinita (4.000.000 recursos)",
			];
			// A RAIZ E DESENHADA PRO HUMANO (senao o botao da pagina nem existe) e a PAGINA pra `quem`: e a pagina que se mede.
			async System.Threading.Tasks.Task<List<string>> APaginaDeConverter(IPosse? quem)
			{
				await AbrirOMenuE(null, fechar: false);
				PosseDoCliente.DeTeste = quem;
				bool abriu = E?.ApertarDesenhado("Converter meu corpo...") == true;
				await Quadros(3);
				List<string> botoes = abriu && E is { NaTela: true } e ? e.BotoesDesenhados() : [];
				if (E is { NaTela: true }) { ApertarE(); await Quadros(2); }
				PosseDoCliente.DeTeste = null;
				return botoes;
			}

			List<string> doHumano = await APaginaDeConverter(null);
			Checa("...e 'Converter meu corpo...' mostra ao humano as QUATRO formas: as duas conversoes, cada uma em zeni e em recursos (o dobro)",
				  doHumano.SequenceEqual(["< Voltar", .. conversoes]), string.Join(" | ", doHumano));
			List<string> doSaiyajin = await APaginaDeConverter(saiyajin);
			Checa("...e as quatro pedem humano -- a de RECURSOS herdou o requisito da de zeni: desenhada pra um Saiyajin, a pagina nao tem nenhuma",
				  doSaiyajin.SequenceEqual(["< Voltar"]), string.Join(" | ", doSaiyajin));
			Requisito.MostrarTudoDeTeste = true;
			List<string> semCrivo = await APaginaDeConverter(saiyajin);
			Requisito.MostrarTudoDeTeste = false;
			Injeta("com o botao que mostra tudo o Saiyajin ve as quatro conversoes de volta", conversoes.All(semCrivo.Contains), string.Join(" | ", semCrivo));

			if (E is { NaTela: true }) ApertarE();
			await Quadros(2);
		}
		finally
		{
			Requisito.MostrarTudoDeTeste = false;
			PosseDoCliente.DeTeste = null;
			srv.MainframeDeTeste(cli.LocalId, por: false);
		}
	}

	/// <summary>Abre o menu da tecla E (pela tecla de verdade) como este personagem e devolve os botoes desenhados.</summary>
	private async System.Threading.Tasks.Task<List<string>> AbrirOMenuE(IPosse? quem, bool fechar = true)
	{
		if (E is { NaTela: true }) { ApertarE(); await Quadros(2); }
		PosseDoCliente.DeTeste = quem;
		ApertarE();
		await Quadros(3);
		List<string> botoes = E is { NaTela: true } e ? e.BotoesDesenhados() : [];
		if (fechar)
		{
			if (E is { NaTela: true }) { ApertarE(); await Quadros(2); }
			PosseDoCliente.DeTeste = null;
		}
		return botoes;
	}

	/// <summary>A tecla E de verdade: um evento de teclado empurrado no viewport (o caminho do `Teclas.Bate`).</summary>
	private void ApertarE()
	{
		var ev = new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = true };
		GetViewport().PushInput(ev);
	}

	// =====================================================================
	// LER A TELA, E ESPERAR
	// =====================================================================
	private static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node n in Todos(f)) yield return n;
	}

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + s;
		while (Time.GetTicksMsec() / 1000.0 < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
		while (Time.GetTicksMsec() / 1000.0 < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}
}
