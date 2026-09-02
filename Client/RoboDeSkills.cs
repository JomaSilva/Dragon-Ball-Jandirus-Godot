using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// ============================ A BANCADA DA ABA DE SKILLS (`--diagskills`) ============================
/// O PEDIDO DO DONO, literal: *"na parte das skills onde vc escolhe a tree e compra a skills ta mt
/// ruim, mt simples e pouco intuitivo, deveria ser mais bonito e entendivel"*. Bonito nao se mede;
/// ENTENDIVEL se mede -- e o que se mede aqui e se cada estado que a aba promete esta na TELA, com
/// o motivo certo, e se a tela reage a cada coisa que muda.
///
/// ============================ ELA NASCE DENTRO DO MUNDO E APERTA O QUE O DEDO APERTARIA ============================
/// Como a `--diagadmin`: o host entra como jogador, e este node aperta P, clica na aba, clica no
/// card, clica em Comprar. Nada e reconstruido; os alvos sao achados VARRENDO A ARVORE DE CENA POR
/// TEXTO, como quem le a tela. Se alguem trocar um rotulo, a bancada fica vermelha -- que e o certo.
///
/// ============================ OS TRES CEGOS QUE ELA FOI FEITA PRA NAO TER ============================
///   * "apagado" nao e lido do `SelfModulate`, e lido da FOTO (luminancia do icone, cor da borda);
///   * "a ficha esta no tema" nao e lido do tipo do node, e lido da moda de pixel do painel contra a
///     PALETA do `Tema` -- e a injecao repoe o `AcceptDialog` de fabrica e cobra que a mesma conta
///     fique vermelha;
///   * "a pagina reage" nao e afirmado, e provado PECA A PECA da assinatura -- e a injecao tira uma
///     peca e reproduz o defeito de origem (a gaveta que nao abria): o dado muda, a tela nao.
///
/// Toda afirmacao vem com o contra-exemplo NA MESMA FOTO: "trancada esta apagada" so vale ao lado de
/// "compravel esta acesa".
///
/// COMO RODAR -- pelo `testar-a-aba-de-skills.bat`, que sobe DOIS PROCESSOS (janela no SEGUNDO
/// monitor -- o dono trabalha no principal; `--marcosteste 40` pra ter o que gastar; `--horateste
/// 0.5` pra ser dia na foto):
///     Godot --headless --path . --server --port 7982 --marcosteste 40 --horateste 0.5     (o servidor)
///     Godot --path . --connect 127.0.0.1 --rede 7982 --diagskills --semfoco                (este cliente)
///           --raca Saiyan --conta bancada_skills --nome Bancada
///           --pasta &lt;dir&gt; [--antes &lt;dir com as 12 fotos de antes&gt;] --position 1920,0 --resolution 1280x720
///
/// ============================ POR QUE DOIS PROCESSOS, E NAO `--host` ============================
/// Ate 2026-09-02 ela rodava com `--host` (servidor e cliente no mesmo processo). A F4c mede o que o
/// CLIENTE carrega do disco (o `niveis.json`, que da o texto "abre quando X chega ao nivel N"), e o
/// registro de niveis e ESTATICO: no `--host` o servidor o enche no boot e o cliente do mesmo
/// processo acerta o texto por tabela do vizinho -- a injecao "cliente sem niveis.json" nunca
/// ficaria vermelha. So um cliente que DISCA mede o cliente. As outras familias nao mudam: tudo que
/// elas apertam viaja por pacote, e 127.0.0.1 continua sendo admin pra F9.
/// ==================================================================================================
///
/// **PRECISA DE JANELA.** Sem foto as linhas de pixel sao PULADAS e entram no terceiro placar.
/// =====================================================================================================
/// </summary>
public partial class RoboDeSkills : Node
{
	// =====================================================================
	// PLACAR -- tres, como manda a casa: provas, injecoes, e o que NAO foi olhado
	// =====================================================================
	private int _ok, _falha, _pulados, _injOk, _injFalha;
	private readonly List<string> _reprovadas = [], _naoMedidos = [], _injPassouBatido = [];
	private readonly List<string> _fotosGravadas = [];

	private static void Nota(string linha) => GD.Print("[skills] " + linha);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private void ChecaNoPixel(string oque, bool temFoto, bool passou, string detalhe = "")
	{
		if (!temFoto)
		{
			Nota("  PULADA  " + oque + "   [sem janela: nao ha foto pra medir]");
			_pulados++;
			_naoMedidos.Add(oque);
			return;
		}
		Checa(oque, passou, detalhe);
	}

	private void Injeta(string oque, bool ficouVermelha, string detalhe = "")
	{
		Nota((ficouVermelha ? "  pegou " : "  PASSOU") + "  (injecao) " + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ficouVermelha) _injOk++;
		else { _injFalha++; _injPassouBatido.Add(oque); }
	}

	private void InjetaNoPixel(string oque, bool temFoto, bool ficouVermelha, string detalhe = "")
	{
		if (!temFoto)
		{
			Nota("  PULADA  (injecao) " + oque + "   [sem janela: nao ha foto pra medir]");
			_pulados++;
			_naoMedidos.Add("(injecao) " + oque);
			return;
		}
		Injeta(oque, ficouVermelha, detalhe);
	}

	// =====================================================================
	// ONDE GRAVAR, E CONTRA O QUE COMPARAR
	// =====================================================================
	private string _pasta = "", _antes = "";
	private readonly Dictionary<string, Image> _fotos = [];

	private static string Arg(string chave)
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, chave);
		return i >= 0 && i + 1 < a.Length ? a[i + 1] : "";
	}

	private static string Hex(Color c) =>
		$"#{(int)(c.R * 255 + 0.5f):x2}{(int)(c.G * 255 + 0.5f):x2}{(int)(c.B * 255 + 0.5f):x2}";

	private static GameClient? C => GameClient.Instance;
	private static MenuJogo? M => MenuJogo.Instancia;

	public override void _Ready() => _ = Rodar();

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async System.Threading.Tasks.Task Rodar()
	{
		_pasta = Arg("--pasta");
		_antes = Arg("--antes");
		if (_pasta.Length == 0) _pasta = ProjectSettings.GlobalizePath("user://");
		if (!_pasta.EndsWith('/') && !_pasta.EndsWith('\\')) _pasta += "/";
		DirAccess.MakeDirRecursiveAbsolute(_pasta);

		Nota("==================================================================================");
		Nota(" A ABA DE SKILLS DO MENU P -- cards, linhas por tier, a ficha no tema, os marcos");
		Nota("==================================================================================");
		Nota($"  fotos em           : {_pasta}");
		Nota($"  fotos de ANTES em  : {(_antes.Length == 0 ? "(nenhuma -- sem tira antes x depois)" : _antes)}");
		Nota($"  janela             : {DisplayServer.WindowGetSize()} em {DisplayServer.WindowGetPosition()}");

		// ---- o mundo: conectado, com a raca na ficha lenta e o estado das arvores no pacote ----
		bool pronto = await Ate(() => C is { Connected: true } c && c.Atributos.Raca is { Length: > 0 }
										&& c.SkillsArvores.Count > 0 && M != null, 90);
		Checa("o mundo chegou (conexao, raca na ficha lenta, estado das arvores no pacote de skills)", pronto,
			  $"raca={C?.Atributos.Raca} arvores={C?.SkillsArvores.Count} marcos={C?.MarcosLivres}");
		if (!pronto || M is not { } menu || C is not { } cli) { Placar(); GetTree().Quit(2); return; }
		await Segundos(1.0);   // os verbs de aprendizado registram na chegada da raca; da tempo

		// RODADA LIMPA: o personagem da bancada nasce sem skill. Um save de rodada anterior (a pasta de
		// usuario desviada persiste entre rodadas se ninguem a apagar) deixaria "Basic Training" ja SUA
		// no primeiro quadro e toda a F2 mediria outro estado -- foi assim que uma rodada saiu com 17
		// falhas que nao eram da tela.
		Checa("RODADA LIMPA: o personagem comeca sem skill nenhuma e com os marcos de teste", cli.SkillsAprendidas.Count == 0 && cli.MarcosTotais == cli.MarcosLivres,
			  $"{cli.SkillsAprendidas.Count} skills, {cli.MarcosLivres}/{cli.MarcosTotais} marcos");

		SkillCatalog? cat = MenuJogo.CatalogoPublico();
		Checa("o catalogo de skills carregou no cliente", cat != null, $"{cat?.Total} skills");
		if (cat == null) { Placar(); GetTree().Quit(2); return; }

		await F0_AbreOMenuNaAbaLearning(menu, cli);
		await F1_AListaDeArvores(menu, cli, cat);
		await F2_AArvoreEmLinhasPorTier(menu, cli, cat);
		await F3_AFichaEACompra(menu, cli, cat);
		await F4_OsQuatroEstadosNaMesmaFoto(menu, cli, cat);
		await F4b_OMouseEmCima(menu, cli, cat);
		await F4c_TrancadaPorDegrau(menu, cli, cat);
		await F5_Esquecer(menu, cli, cat);
		await F6_ALinhaDoTierPersiste(menu, cli, cat);
		await F7_ABuscaAchaSkill(menu, cli, cat);
		await F8_AAbaSkills(menu, cli, cat);
		await F9_AAssinaturaPecaAPeca(menu, cli, cat);
		await F10_OTemaDaFicha(menu, cli, cat);
		await F11_Determinismo(menu, cli, cat);
		F12_ATiraAntesXDepois();

		menu.Fechar();
		Placar();
		GetTree().Quit(_falha == 0 && _injFalha == 0 ? 0 : 1);
	}

	// =====================================================================
	// F0 -- O MENU ABRE COM P E A ABA LEARNING ABRE PELO BOTAO
	// =====================================================================
	private async System.Threading.Tasks.Task F0_AbreOMenuNaAbaLearning(MenuJogo menu, GameClient cli)
	{
		Nota("--- F0: P abre o menu, o botao abre a aba Learning ---");

		// A TECLA DE VERDADE, empurrada pela porta de entrada do motor: e o `_Input` do menu que a le.
		var p = new InputEventKey { Keycode = Key.P, PhysicalKeycode = Key.P, Unicode = 'p', Pressed = true };
		GetViewport().PushInput(p);
		await Quadros(3);
		Checa("apertar P abre o menu", menu.Visible && MenuJogo.Aberto);
		if (!menu.Visible) menu.Abrir();

		Button? aba = Botao(menu, "Learning");
		Checa("a aba 'Learning' existe na barra", aba != null);
		if (aba != null) await Clicar(aba);
		Checa("a aba Learning e a que esta na tela", menu.AbaDeTeste == "Learning", menu.AbaDeTeste);
	}

	// =====================================================================
	// F1 -- NIVEL 1: AS ARVORES EM CARDS, AS QUE VAO ABRIR APAGADAS, OS MARCOS GRANDES, OS VERBS
	// =====================================================================
	private async System.Threading.Tasks.Task F1_AListaDeArvores(MenuJogo menu, GameClient cli, SkillCatalog cat)
	{
		Nota("--- F1: a lista de arvores ---");
		Control? pg = menu.PaginaDeTeste("Learning");
		Checa("a pagina de Learning existe e esta visivel", pg is { Visible: true });
		if (pg == null) return;

		string[] minhas = ["Strength of Body", "Strength of Mind", "Strength of Spirit", "Saiyan Racials"];
		foreach (string nome in minhas)
		{
			PanelContainer? card = CartaoDeArvore(pg, nome);
			Skill? arv = cat.Arvores.FirstOrDefault(a => a.Nome == nome);
			Checa($"'{nome}' e um CARD (painel com botao), e nao um botao de largura total", card != null && card.GetChild(0) is Button);
			if (card == null || arv == null) continue;
			Checa($"'{nome}': a DESCRICAO esta VISIVEL no card (nao em tooltip)",
				  Rotulos(card).Any(l => l.Text == arv.Desc), arv.Desc);
			string contadores = Rotulos(card).Select(l => l.Text).FirstOrDefault(t => t.Contains("suas")) ?? "";
			Checa($"'{nome}': o card diz o tier atual de maximo e X/Y suas", contadores.Contains("tier ") && contadores.Contains(" de ") && contadores.Contains("/"), contadores);
		}

		// ---- as arvores que o progresso ainda vai abrir: APAGADAS, com o que falta escrito ----
		PanelContainer? body = CartaoDeArvore(pg, "Strength of Body");
		PanelContainer? porAbrir = CartaoDeArvore(pg, "Advanced Bodybuilding") ?? CartaoDeArvore(pg, "Martial Arts");
		Checa("uma arvore que o Body ainda vai abrir (Bodybuilding/Martial Arts) APARECE na lista, apagada",
			  porAbrir != null && porAbrir.GetChild(0) is not Button, porAbrir == null ? "nao achei" : "achei, sem botao");
		if (porAbrir != null)
		{
			string cond = Rotulos(porAbrir).Select(l => l.Text).FirstOrDefault(t => t.StartsWith("abre com")) ?? "";
			Checa("...e ela diz o que a abre, em portugues (a condicao do growbranches traduzida)",
				  cond.Contains("abre com:") && (cond.Contains("domínio corporal") || cond.Contains("prontidão corporal")), cond);
		}

		await Rolar(pg, body);
		Image? foto = await Foto();
		await Guardar("depois-01-arvores", foto);

		if (body != null && porAbrir != null)
		{
			await Rolar(pg, porAbrir);
			Image? f2 = await Foto();
			(Color cBody, _) = Moda(f2, Caixa(body.GetGlobalRect(), 4));
			(Color cApagada, _) = Moda(f2, Caixa(porAbrir.GetGlobalRect(), 4));
			ChecaNoPixel("CONTRA-EXEMPLO NA MESMA FOTO: a chapa da arvore SUA e a clara e a da POR-ABRIR e a apagada",
						 f2 != null, Perto(cBody, Tema.PainelClaro) && Perto(cApagada, Tema.PainelApagado),
						 $"sua {Hex(cBody)} (paleta {Hex(Tema.PainelClaro)}) / por abrir {Hex(cApagada)} (paleta {Hex(Tema.PainelApagado)})");
		}

		// ---- o mouse num card de ARVORE: o mesmo hover do tema (o botao do card e Flat: quem acende e o painel) ----
		if (body?.GetChild(0) is Button bBody)
		{
			await Rolar(pg, body);
			await SobreOControle(bBody);
			Image? f4 = await Foto();
			ChecaNoPixel("o mouse num card de ARVORE acende a chapa de hover do tema (Tema.PainelAceso) e a borda laranja", f4 != null,
						 Perto(Chapa(f4, body), Tema.PainelAceso) && MaisPerto(Borda(f4, body)) == "BordaViva", $"chapa {Hex(Chapa(f4, body))} (paleta {Hex(Tema.PainelAceso)}) borda {Hex(Borda(f4, body))}");
			await ForaDoAlcance();
			Image? f5 = await Foto();
			ChecaNoPixel("...e volta ao repouso quando o mouse sai (chapa clara, borda cinza)", f5 != null,
						 Perto(Chapa(f5, body), Tema.PainelClaro) && MaisPerto(Borda(f5, body)) == "apagada", $"chapa {Hex(Chapa(f5, body))} borda {Hex(Borda(f5, body))}");
		}

		// ---- a faixa de marcos: fixa, grande, e com o numero do cliente ----
		Label? grande = Achar<Label>(menu, l => l.IsVisibleInTree() && l.Text == cli.MarcosLivres.ToString() && Ancestral<PanelContainer>(l)?.Name == "FaixaDeMarcos");
		Checa("a faixa de MARCOS mostra o saldo do cliente", grande != null, $"cliente diz {cli.MarcosLivres}");
		if (grande != null)
		{
			Checa("...e ela esta FORA da rolagem (nao e filha da pagina)", !pg.IsAncestorOf(grande));
			Image? f3 = await Foto();
			int altura = AlturaDoGlifo(f3, grande.GetGlobalRect());
			ChecaNoPixel("...e o numero e GRANDE na foto (glifo de 16+ px; o rotulo antigo tinha 11 px de fonte)",
						 f3 != null, altura >= 16, $"glifo com {altura} px de altura no retangulo {grande.GetGlobalRect()}, foto {f3?.GetWidth()}x{f3?.GetHeight()}");
		}

		// ---- os verbs de aprendizado (defeito D) ----
		var verbos = Verbos.Da(Verbos.Aprendizado).ToList();
		int naTela = verbos.Count(v => Botao(pg, v.Nome) != null);
		Checa("os verbs de LEARNING tem lugar na aba (Convidar aluno, Danca da Fusao, Inventar tecnicas...)",
			  naTela >= 10 && naTela == verbos.Count, $"{naTela} de {verbos.Count} na tela");
		Checa("CONTRA-EXEMPLO: eles NAO estao na pagina de Other", menu.PaginaDeTeste(Verbos.Outros) is not { } outra || Botao(outra, "Convidar aluno", soVisivel: false) == null);
	}

	// =====================================================================
	// F2 -- NIVEL 2: LINHAS POR TIER, A ORDEM DO CATALOGO, OS ESTADOS
	// =====================================================================
	private async System.Threading.Tasks.Task F2_AArvoreEmLinhasPorTier(MenuJogo menu, GameClient cli, SkillCatalog cat)
	{
		Nota("--- F2: a arvore Strength of Body em linhas por tier ---");
		Control? pg = menu.PaginaDeTeste("Learning");
		if (pg == null) return;

		PanelContainer? body = CartaoDeArvore(pg, "Strength of Body");
		if (body?.GetChild(0) is not Button abrir) { Checa("achei o card do Body pra clicar", false); return; }
		await Clicar(abrir);
		Checa("clicar no card abre a arvore (o botao de voltar esta na tela)", Botao(pg, "‹  todas as árvores") != null);

		foreach (int t in new[] { 1, 2, 3 })
			Checa($"a linha 'TIER {t}' existe", Rotulo(pg, $"TIER {t}") != null);

		string tier2 = LinhaDoTier(pg, 2), tier3 = LinhaDoTier(pg, 3), tier1 = LinhaDoTier(pg, 1);
		Checa("TIER 1 esta aberto ('0 de 11 suas')", tier1.Contains("0 de 11 suas"), tier1);
		Checa("TIER 2 esta TRANCADO e diz quanto investir (4 marcos: a regra `invested>=4` do Body.dm)", tier2.Contains("TRANCADO") && tier2.Contains("invista 4 marcos"), tier2);
		Checa("TIER 3 esta TRANCADO e diz 7 (a regra `invested>=7`)", tier3.Contains("TRANCADO") && tier3.Contains("invista 7 marcos"), tier3);

		// ---- a ordem dos cards do tier 1 e a do catalogo ----
		Skill arv = cat.Arvores.First(a => a.Nome == "Strength of Body");
		List<string> esperada = arv.Galhos.Select(cat.Get).Where(s => s is { Arvore: false } && s.Nome.Length > 0 && s.Tier == 1).Select(s => s!.Nome).ToList();
		List<string> naTela = NomesDaLinha(pg, 1);
		Checa("os 11 cards do tier 1 estao NA ORDEM DO CATALOGO (a dos galhos no json, nao a de um dicionario)",
			  naTela.SequenceEqual(esperada), string.Join(" > ", naTela));

		// ---- os estados, nada comprado ainda ----
		Checa("'Basic Training' (ligada, sem pre-requisito) e COMPRAVEL: o card diz o preco", EstadoDo(pg, "Basic Training") == "1 marco", EstadoDo(pg, "Basic Training"));
		Checa("'Evasion Training' esta trancada POR PRE-REQUISITO, com o nome dele", EstadoDo(pg, "Evasion Training") == "depois de Basic Training", EstadoDo(pg, "Evasion Training"));
		Checa("'Rapid Movement' (enabled=0, sem regra que a acenda) diz 'sem acendedor'", EstadoDo(pg, "Rapid Movement") == "sem acendedor", EstadoDo(pg, "Rapid Movement"));
		Checa("'Afterimage Technique' (tier 2) esta trancada POR TIER", EstadoDo(pg, "Afterimage Technique") == "tier trancado", EstadoDo(pg, "Afterimage Technique"));
		Checa("'Planet Destroy' diz 'so pra vilao'", EstadoDo(pg, "Planet Destroy") == "só pra vilão", EstadoDo(pg, "Planet Destroy"));
		Checa("nao existe mais a gaveta 'nao estao a venda'", Botao(pg, "▸") == null && Rotulos(pg).All(l => !l.Text.Contains("não estão à venda")));

		// ---- a foto do topo: tier 1 inteiro e o cabecalho do tier 2 ----
		await Rolar(pg, Cartao(pg, "Basic Training"));
		await Guardar("depois-02-body-3-tiers", await Foto());
	}

	// =====================================================================
	// F3 -- A FICHA NO TEMA, E COMPRAR POR ELA MUDA OS MARCOS E O CARD
	// =====================================================================
	private async System.Threading.Tasks.Task F3_AFichaEACompra(MenuJogo menu, GameClient cli, SkillCatalog cat)
	{
		Nota("--- F3: a ficha e a compra ---");
		Control? pg = menu.PaginaDeTeste("Learning");
		if (pg == null) return;
		Skill? bt = cat.Todas.FirstOrDefault(s => s.Nome == "Basic Training");

		PanelContainer? card = Cartao(pg, "Basic Training");
		if (card?.GetChild(0) is not Button b) { Checa("achei o card de Basic Training", false); return; }
		int marcosAntes = cli.MarcosLivres;
		await Clicar(b);

		PanelContainer? ficha = menu.FichaDeTeste;
		Checa("clicar no card abre a FICHA (um painel, nao uma compra)", ficha != null && cli.MarcosLivres == marcosAntes);
		if (ficha == null) return;
		Checa("a ficha NAO e um AcceptDialog/Window: nenhuma janela de fabrica esta aberta", !Todos(GetTree().Root).OfType<Window>().Any(w => w != GetTree().Root && w.Visible));
		Checa("a ficha tem o nome", Rotulo(ficha, "Basic Training") != null);
		Checa("a ficha mostra a descricao do catalogo (em ingles, como esta -- divida registrada)", bt != null && Rotulo(ficha, bt.Desc) != null, bt?.Desc ?? "");
		Checa("a ficha lista os EFEITOS (linhas com '•')", Rotulos(ficha).Any(l => l.Text.StartsWith("• ")));
		Button? comprar = Botao(ficha, "Comprar");
		Checa("o botao diz 'Comprar · 1 marco (voce tem N)' com o saldo de verdade", comprar != null && comprar.Text.Contains("1 marco") && comprar.Text.Contains($"(você tem {marcosAntes})"), comprar?.Text ?? "");
		Checa("a ficha tem 'Cancelar'", Botao(ficha, "Cancelar") != null);
		Checa("CONTRA-EXEMPLO: a ficha de uma skill que NAO e sua nao tem 'Esquecer'", Botao(ficha, "Esquecer") == null);

		Image? foto = await Foto();
		await Guardar("depois-03-ficha", foto);
		(Color cor, float frac) = Moda(foto, Caixa(ficha.GetGlobalRect(), 3));
		ChecaNoPixel("O PAINEL DA FICHA E PINTADO COM A PALETA DO TEMA (moda de pixel do retangulo dela)",
					 foto != null, NaPaleta(cor), $"moda {Hex(cor)} ({frac * 100:0}% do painel), paleta {string.Join(" ", Paleta().Select(Hex))}");
		Checa("a ficha cabe inteira na tela", GetViewport().GetVisibleRect().Encloses(ficha.GetGlobalRect()), $"{ficha.GetGlobalRect()}");

		// ---- comprar ----
		if (comprar == null) return;
		await Clicar(comprar);
		bool chegou = await Ate(() => cli.SkillsAprendidas.Contains(bt!.Path), 5);
		await Quadros(4);
		Checa("Comprar manda o pedido e o servidor responde com a skill aprendida", chegou);
		Checa("a ficha fechou sozinha depois de comprar", menu.FichaDeTeste == null);
		Checa("o CLIENTE tem um marco a menos (40 -> 39: numero absoluto, nao so 'mudou')", cli.MarcosLivres == marcosAntes - 1, $"{marcosAntes} -> {cli.MarcosLivres}");
		Label? faixa = Achar<Label>(menu, l => l.IsVisibleInTree() && Ancestral<PanelContainer>(l)?.Name == "FaixaDeMarcos" && int.TryParse(l.Text, out _));
		Checa("a FAIXA de marcos mudou na hora (as duas telas concordam E com o numero certo)", faixa != null && faixa.Text == (marcosAntes - 1).ToString(), faixa?.Text ?? "(sem faixa)");
		Checa("o card de Basic Training passou a dizer SUA", EstadoDo(pg, "Basic Training") == "SUA", EstadoDo(pg, "Basic Training"));
		Checa("...e Evasion Training, que pedia Basic Training, virou COMPRAVEL (o pre-requisito entrou)", EstadoDo(pg, "Evasion Training") == "1 marco", EstadoDo(pg, "Evasion Training"));
		await Rolar(pg, Cartao(pg, "Basic Training"));
		await Guardar("depois-04-depois-de-comprar", await Foto());
	}

	// =====================================================================
	// F4 -- OS QUATRO ESTADOS, DESENHADOS DIFERENTES, NA MESMA FOTO
	// =====================================================================
	private async System.Threading.Tasks.Task F4_OsQuatroEstadosNaMesmaFoto(MenuJogo menu, GameClient cli, SkillCatalog cat)
	{
		Nota("--- F4: sua / compravel / trancada por pre-requisito / trancada por tier ---");
		Control? pg = menu.PaginaDeTeste("Learning");
		if (pg == null) return;

		PanelContainer? sua = Cartao(pg, "Basic Training");
		PanelContainer? compravel = Cartao(pg, "Evasion Training");
		PanelContainer? porPreReq = Cartao(pg, "Speed Drills");
		PanelContainer? porTier = Cartao(pg, "Afterimage Technique");
		Checa("os quatro cards existem", sua != null && compravel != null && porPreReq != null && porTier != null);
		if (sua == null || compravel == null || porPreReq == null || porTier == null) return;

		Checa("os quatro dizem motivos DIFERENTES: SUA / 1 marco / depois de Evasion Training / tier trancado",
			  Estado(sua) == "SUA" && Estado(compravel) == "1 marco" && Estado(porPreReq) == "depois de Evasion Training" && Estado(porTier) == "tier trancado",
			  $"{Estado(sua)} | {Estado(compravel)} | {Estado(porPreReq)} | {Estado(porTier)}");

		// A FOTO: rola ate o tier 2 aparecer junto do fim do tier 1 -- os quatro no mesmo quadro
		await Rolar(pg, porTier);
		Image? foto = await Foto();
		await Guardar("depois-05-quatro-estados", foto);
		bool osQuatroNaFoto = foto != null && new[] { sua, compravel, porPreReq, porTier }.All(c => GetViewport().GetVisibleRect().Encloses(c.GetGlobalRect()));
		if (!osQuatroNaFoto)
		{
			// a tela e menor do que a soma das duas linhas do tier 1 mais a do tier 2: mede cada um
			// no seu proprio quadro, e diz isso
			Nota("  (os quatro nao cabem num quadro so nesta resolucao; cada borda e lida no proprio quadro)");
		}

		Color bSua = await CorDaBorda(pg, sua), bComp = await CorDaBorda(pg, compravel), bPre = await CorDaBorda(pg, porPreReq), bTier = await CorDaBorda(pg, porTier);
		ChecaNoPixel("a borda da SUA e VERDE (Tema.Bom) no pixel", foto != null, MaisPerto(bSua) == "Bom", $"{Hex(bSua)} -> {MaisPerto(bSua)}");
		ChecaNoPixel("a borda da COMPRAVEL e LARANJA (Tema.BordaViva) no pixel -- o contra-exemplo da verde", foto != null, MaisPerto(bComp) == "BordaViva", $"{Hex(bComp)} -> {MaisPerto(bComp)}");
		ChecaNoPixel("a borda da trancada POR PRE-REQUISITO e APAGADA (cinza escuro do tema)", foto != null, MaisPerto(bPre) == "apagada", $"{Hex(bPre)} -> {MaisPerto(bPre)}");
		ChecaNoPixel("a borda da trancada POR TIER e apagada tambem", foto != null, MaisPerto(bTier) == "apagada", $"{Hex(bTier)} -> {MaisPerto(bTier)}");

		float lComp = await LumDoIcone(pg, compravel), lPre = await LumDoIcone(pg, porPreReq);
		ChecaNoPixel("o ICONE da trancada esta apagado no pixel (menos de 60% da luminancia do icone da compravel, mesma arte)",
					 foto != null, lPre < lComp * 0.6f && lComp > 0.05f, $"compravel {lComp:0.000} / trancada {lPre:0.000}");

	}

	// =====================================================================
	// F4b -- O MOUSE EM CIMA: o card APONTADO e o mais forte; o pre-requisito e uma ETIQUETA
	// =====================================================================
	/// <summary>
	/// O RELATO DO DONO, com foto: *"o highlight dos icones quando coloca o mouse em cima ta todo
	/// errado, coloco o mouse em um o outro q acende"*. Na foto o mouse estava em 'Speed Drills' e a
	/// borda branca estava em 'Evasion Training'. O branco era a ligacao de pre-requisito, feita de
	/// proposito -- mas o card apontado nao acendia NADA: o `Button` embaixo do card e `Flat`, e um
	/// botao Flat nao desenha stylebox de estado nenhum (normal, hover, pressed), so o de foco -- o
	/// hover do tema nunca chegou ao card. Um realce que precisa de explicacao falhou como realce.
	///
	/// O que se cobra aqui, NO PIXEL, no mesmo gesto do dono (mouse em 'Speed Drills'):
	///   (a) o card SOB O MOUSE tem a borda e a chapa de HOVER do tema, e um brilho por fora;
	///   (b) o pre-requisito ('Evasion Training') NAO ganha borda nem chapa de hover: ganha uma
	///       ETIQUETA "requisito" -- outra natureza, e nao a mesma borda noutra cor;
	///   (c) sem mouse, nenhum dos dois tem marca; e o INVERSO: apontar o pre-requisito nao acende
	///       nada em quem depende dele.
	/// </summary>
	private async System.Threading.Tasks.Task F4b_OMouseEmCima(MenuJogo menu, GameClient cli, SkillCatalog cat)
	{
		Nota("--- F4b: o mouse em cima -- o card apontado e o mais forte, o pre-requisito e uma etiqueta ---");
		Control? pg = menu.PaginaDeTeste("Learning");
		if (pg == null) return;
		PanelContainer? alvo = Cartao(pg, "Speed Drills");        // trancada: "depois de Evasion Training"
		PanelContainer? antes = Cartao(pg, "Evasion Training");   // o pre-requisito dela, compravel
		PanelContainer? alheio = Cartao(pg, "Basic Training");    // sua; nao entra na historia
		Checa("os tres cards existem (o apontado, o pre-requisito dele, e um alheio)", alvo != null && antes != null && alheio != null);
		if (alvo == null || antes == null || alheio == null) return;
		if (alvo.GetChild(0) is not Button bAlvo || antes.GetChild(0) is not Button bAntes) { Checa("os dois cards tem o botao embaixo", false); return; }

		// A LINHA INTEIRA NO QUADRO, com o cabecalho do tier acima dela: no topo da rolagem a primeira
		// linha do tier 1 cabe inteira (a foto da F2 mostra). `EnsureControlVisible` encostaria a linha
		// no teto da rolagem, e o pixel de FORA do card cairia na area cortada.
		if (Ancestral<ScrollContainer>(pg) is { } rol) rol.ScrollVertical = 0;
		await Quadros(3);
		if (!GetViewport().GetVisibleRect().Encloses(alvo.GetGlobalRect())) await Rolar(pg, alvo);
		await ForaDoAlcance();

		// ---- (c) primeiro o repouso: sem mouse, nenhum dos dois tem marca ----
		Image? f0 = await Foto();
		ChecaNoPixel("SEM MOUSE: 'Speed Drills' esta no repouso de trancada -- borda e chapa apagadas", f0 != null,
					 MaisPerto(Borda(f0, alvo)) == "apagada" && Perto(Chapa(f0, alvo), Tema.PainelApagado), $"borda {Hex(Borda(f0, alvo))} chapa {Hex(Chapa(f0, alvo))}");
		ChecaNoPixel("SEM MOUSE: 'Evasion Training' esta no repouso de compravel -- borda laranja, chapa clara", f0 != null,
					 MaisPerto(Borda(f0, antes)) == "BordaViva" && Perto(Chapa(f0, antes), Tema.PainelClaro), $"borda {Hex(Borda(f0, antes))} chapa {Hex(Chapa(f0, antes))}");
		Checa("SEM MOUSE: nenhuma etiqueta 'requisito' visivel na pagina", EtiquetasVisiveis(pg) == 0, $"{EtiquetasVisiveis(pg)} visiveis");

		// ---- O GESTO DO DONO: o mouse em 'Speed Drills' ----
		await SobreOControle(bAlvo);
		Image? f1 = await Foto();
		await Guardar("depois-10-mouse-no-speed-drills", f1);
		Nota($"  mouse em {MeioDe(bAlvo)}; 'Speed Drills' em {alvo.GetGlobalRect()}; 'Evasion Training' em {antes.GetGlobalRect()}");
		Color bA = Borda(f1, alvo), cA = Chapa(f1, alvo), bP = Borda(f1, antes, 0.2f), cP = Chapa(f1, antes);
		ChecaNoPixel("(a) o card SOB O MOUSE tem a borda de HOVER do tema (laranja, Tema.BordaViva) -- no repouso era apagada", f1 != null, MaisPerto(bA) == "BordaViva", $"{Hex(bA)} -> {MaisPerto(bA)}");
		ChecaNoPixel("(a) ...e a CHAPA de hover do tema (Tema.PainelAceso), a mesma que todo botao ganha embaixo do mouse", f1 != null, Perto(cA, Tema.PainelAceso), $"{Hex(cA)} (paleta {Hex(Tema.PainelAceso)})");
		Color foraA = ForaDoCard(f1, alvo), foraP = ForaDoCard(f1, antes);
		ChecaNoPixel("(a) ...e um BRILHO por fora: 2 px a esquerda dele o pixel e QUENTE (R > B); 2 px a esquerda do pre-requisito e frio", f1 != null,
					 Quente(foraA) && !Quente(foraP) && (foraA.R - foraA.B) > (foraP.R - foraP.B) + 0.06f, $"fora do apontado {Hex(foraA)} / fora do pre-requisito {Hex(foraP)}");
		ChecaNoPixel("(b) o PRE-REQUISITO nao ganha a marca do hover: a borda (lida fora da pilula) continua a laranja de repouso, nao e branca; a chapa continua a clara, nao e a de hover", f1 != null,
					 MaisPerto(bP) == "BordaViva" && Perto(cP, Tema.PainelClaro) && !Perto(cP, Tema.PainelAceso), $"borda {Hex(bP)} chapa {Hex(cP)}");
		Label? etq = Etiqueta(antes);
		Checa("(b) ...ele ganha a ETIQUETA 'requisito', visivel", etq is { Visible: true } && etq.Text.Equals("requisito", StringComparison.OrdinalIgnoreCase), etq == null ? "sem etiqueta no card" : $"'{etq.Text}' visivel={etq.Visible}");
		Checa("(b) ...e e a UNICA etiqueta visivel na pagina", EtiquetasVisiveis(pg) == 1, $"{EtiquetasVisiveis(pg)} visiveis");
		if (etq != null)
		{
			Rect2I re = Caixa(etq.GetGlobalRect(), 1);
			(Color pilula, float fr) = Moda(f1, re);
			int escuras = LinhasComPixelEscuro(f1, re);
			ChecaNoPixel("(b) ...e a etiqueta e uma PILULA BRANCA (moda de pixel = Tema.Texto) com letras escuras dentro -- outra natureza que uma borda", f1 != null,
						 Perto(pilula, Tema.Texto) && escuras >= 4, $"moda {Hex(pilula)} em {fr * 100:0}% do retangulo {re}, {escuras} linhas com pixel escuro");
		}
		ChecaNoPixel("CONTRA-EXEMPLO NA MESMA FOTO: o card alheio ('Basic Training', sua) nao muda: borda verde, chapa clara", f1 != null,
					 MaisPerto(Borda(f1, alheio)) == "Bom" && Perto(Chapa(f1, alheio), Tema.PainelClaro), $"borda {Hex(Borda(f1, alheio))} chapa {Hex(Chapa(f1, alheio))}");
		Checa("o botao embaixo do card continua Flat: o realce e do PAINEL do card, nao dele (nao-flat desenharia uma segunda moldura 6 px por dentro)", bAlvo.Flat);

		// ---- (c) o mouse sai: tudo volta ----
		await ForaDoAlcance();
		Image? f2 = await Foto();
		ChecaNoPixel("(c) o mouse saiu: 'Speed Drills' volta ao repouso (borda e chapa apagadas)", f2 != null,
					 MaisPerto(Borda(f2, alvo)) == "apagada" && Perto(Chapa(f2, alvo), Tema.PainelApagado), $"borda {Hex(Borda(f2, alvo))} chapa {Hex(Chapa(f2, alvo))}");
		Checa("(c) ...e a etiqueta some", EtiquetasVisiveis(pg) == 0 && etq is not { Visible: true }, $"{EtiquetasVisiveis(pg)} visiveis");
		if (etq != null)
		{
			(Color semPilula, _) = Moda(f2, Caixa(etq.GetGlobalRect(), 1));
			ChecaNoPixel("(c) ...no pixel: onde a pilula estava nao ha mais branco", f2 != null, !Perto(semPilula, Tema.Texto), Hex(semPilula));
		}

		// ---- o INVERSO: o mouse no pre-requisito nao acende nada em quem depende dele ----
		await SobreOControle(bAntes);
		Image? f3 = await Foto();
		ChecaNoPixel("INVERSO: com o mouse em 'Evasion Training', E ELE que acende (chapa de hover)", f3 != null, Perto(Chapa(f3, antes), Tema.PainelAceso), Hex(Chapa(f3, antes)));
		ChecaNoPixel("INVERSO: ...e 'Speed Drills' (que depende dele) nao acende nada: borda e chapa apagadas", f3 != null,
					 MaisPerto(Borda(f3, alvo)) == "apagada" && Perto(Chapa(f3, alvo), Tema.PainelApagado), $"borda {Hex(Borda(f3, alvo))} chapa {Hex(Chapa(f3, alvo))}");
		// 'Evasion Training' tem pre-requisito proprio (Basic Training): a etiqueta que aparece agora e a
		// DELE, nunca a de quem depende dele. A ligacao anda num sentido so: de quem depende pra quem vem antes.
		Skill? skAntes = cat.Get(antes.GetMeta("path").AsString());
		string[] preDoAntes = skAntes?.PreReqs.Select(p => cat.Get(p)?.Nome ?? p).ToArray() ?? [];
		int esperadas = preDoAntes.Count(n => Cartao(pg, n) != null);
		Checa($"INVERSO: ...e a etiqueta de 'Speed Drills' fica ESCONDIDA; as visiveis sao so os pre-requisitos de 'Evasion Training' ({string.Join(", ", preDoAntes)})",
			  Etiqueta(alvo) is not { Visible: true } && EtiquetasVisiveis(pg) == esperadas && preDoAntes.All(n => Cartao(pg, n) is not { } c || Etiqueta(c) is { Visible: true }),
			  $"{EtiquetasVisiveis(pg)} visiveis, esperadas {esperadas}; etiqueta de 'Speed Drills' visivel={Etiqueta(alvo)?.Visible}");
		await ForaDoAlcance();
	}

	// =====================================================================
	// F4c -- TRANCADA POR DEGRAU: o card diz QUEM acende e EM QUE NIVEL
	// =====================================================================
	/// <summary>
	/// O BURACO RELATADO PELO LOTE DOS DEGRAUS (2026-09-02): um DEGRAU de outra skill acende uma skill
	/// -- Basic Ki Awareness no nivel 100 acende Advanced Ki Awareness, e 32 skills sao assim (as
	/// Advanced_* e Perfect_* da Mente e das maestrias de Ki). A DECISAO (acesa ou nao) chega certa
	/// pelo pacote `S2C.Skills` (`Acesas`), mas o TEXTO do veredito e montado no cliente por
	/// `SkillBook.AcendedorDe`, que le o registro de niveis (`RegrasDeNivel.DestravadaPor`) -- e o
	/// cliente nao carregava o `niveis.json`. Resultado: "sem acendedor" (Desligada) num card que o
	/// servidor sabe que abre no nivel 100.
	///
	/// ============================ SO SE MEDE COM DOIS PROCESSOS ============================
	/// No `--host` o servidor mora no MESMO processo e o registro de niveis e ESTATICO: o cliente
	/// "acertava" por tabela do vizinho, e a injecao (cliente sem `niveis.json`) nunca ficaria
	/// vermelha. Por isso o `testar-a-aba-de-skills.bat` sobe um servidor DEDICADO e este cliente
	/// disca (`--connect`) -- e a primeira linha desta familia cobra que e assim que se esta rodando.
	/// =======================================================================================
	///
	/// O que se cobra, lido no TEXTO do card e da ficha (com o contra-exemplo na mesma arvore):
	///   (a) 'Advanced Ki Awareness' diz "abre quando Basic Ki Awareness chega ao nivel 100";
	///   (b) 'Basic Ki Awareness' -- trancada por CONDICAO de arvore (`kiawarenessskill>=1`), nao por
	///       degrau -- continua dizendo a condicao traduzida; 'Ki Unlocked' continua compravel;
	///   (c) na arvore do Body, a trancada por PRE-REQUISITO continua "depois de X" e a por TIER
	///       continua "tier trancado" com a linha do tier dizendo "invista N marcos";
	///   (d) a ficha diz a mesma frase, e NAO inventa quanto falta: o nivel atual da skill nao viaja
	///       no pacote (decisao de 2026-09-01), entao o cliente sabe QUAL degrau acende e nao QUANTO
	///       falta -- e mostra so o que sabe.
	/// </summary>
	private async System.Threading.Tasks.Task F4c_TrancadaPorDegrau(MenuJogo menu, GameClient cli, SkillCatalog cat)
	{
		Nota("--- F4c: trancada por DEGRAU -- o card diz quem acende e em que nivel ---");
		Control? pg = menu.PaginaDeTeste("Learning");
		if (pg == null) return;

		// ---- a medicao so vale fora do host: o registro de niveis e estatico e o servidor o encheria ----
		bool doisProcessos = Array.IndexOf(OS.GetCmdlineArgs(), "--host") < 0
						  && Jandirus.Server.GameServer.Instance is not { Running: true };
		Checa("a medicao e de CLIENTE PURO: o servidor mora noutro processo (sem --host; o registro de niveis "
			  + "e estatico e o do servidor mascararia o do cliente)", doisProcessos,
			  doisProcessos ? "--connect" : "--host: o servidor deste processo carregou o niveis.json e o cliente le por tabela");

		// ---- O DADO: o registro de degraus DESTE processo (o niveis.json que o cliente carregou) ----
		int porDegrau = RegrasDeNivel.DestravadasPorDegrau.Count;
		Checa("o cliente carregou o niveis.json: o registro tem 32 skills acesas por DEGRAU (as Advanced_* e Perfect_*)",
			  porDegrau == 32, $"{porDegrau} no registro; {MenuJogo.NiveisNoClienteDeTeste} regras carregadas pelo cliente em {MenuJogo.MsDoNiveisDeTeste:0.0} ms");
		Checa("...e carregar custou pouco (menos de 60 ms, na mesma chamada que ja le o skills.json -- fora do quadro do mundo)",
			  MenuJogo.MsDoNiveisDeTeste < 60, $"skills.json {MenuJogo.MsDoCatalogoDeTeste:0.0} ms, niveis.json {MenuJogo.MsDoNiveisDeTeste:0.0} ms");

		// ---- O TEXTO, DO CORE ATE A TELA: a frase do `AcendedorDe` atravessa o `NomesLegiveis.Condicao` ----
		// O veredito escreve o acendedor por degrau NA GRAMATICA DO EXTRATOR (nome entre aspas simples,
		// a string literal), e a tela traduz tudo com o mesmo `Condicao` das condicoes de arvore. As
		// duas coisas que ja quebraram isso: o `Condicao` DESCARTAVA espacos ("chegaaonivel100"), e um
		// nome solto passa pelo tradutor de identificadores, que come o sufixo `buff` de "Debuff".
		Checa("o `Condicao` copia a frase do degrau inteira: \"'Basic Ki Awareness' chega ao nivel 100\" -> \"Basic Ki Awareness chega ao nivel 100\"",
			  NomesLegiveis.Condicao("'Basic Ki Awareness' chega ao nivel 100") == "Basic Ki Awareness chega ao nivel 100",
			  NomesLegiveis.Condicao("'Basic Ki Awareness' chega ao nivel 100"));
		Checa("CONTRA-EXEMPLO: a condicao de arvore continua traduzida igual: `kiawarenessskill>=1` -> 'percepção de Ki ≥ 1'; `invested>=4&&Class!='None'` -> 'marcos investidos ≥ 4 e classe ≠ None'",
			  NomesLegiveis.Condicao("kiawarenessskill>=1") == "percepção de Ki ≥ 1"
			  && NomesLegiveis.Condicao("invested>=4&&Class!='None'") == "marcos investidos ≥ 4 e classe ≠ None",
			  NomesLegiveis.Condicao("kiawarenessskill>=1") + " | " + NomesLegiveis.Condicao("invested>=4&&Class!='None'"));
		Checa("CONTRA-EXEMPLO (por que o nome vai entre aspas): solto, 'Basic Debuff Mastery' vira 'Basic De Mastery' no tradutor de identificadores",
			  NomesLegiveis.Condicao("Basic Debuff Mastery chega ao nivel 100").Contains("Basic De Mastery"),
			  NomesLegiveis.Condicao("Basic Debuff Mastery chega ao nivel 100"));

		// AS 32, uma a uma, pelo mesmo caminho da tela (`SkillBook.AcendedorDe` -> `Condicao`): cada
		// alvo de degrau, na arvore que o pendura, rende exatamente "<nome do acendedor> chega ao nivel N".
		int rendemCerto = 0;
		var erradas = new List<string>();
		foreach ((string alvo, RegrasDeNivel.AcendedorPorDegrau quem) in RegrasDeNivel.DestravadasPorDegrau)
		{
			Skill? dona = cat.Arvores.FirstOrDefault(a => a.Galhos.Contains(alvo, StringComparer.OrdinalIgnoreCase));
			if (dona == null) { erradas.Add($"{alvo}: nenhuma arvore o pendura"); continue; }
			string frase = NomesLegiveis.Condicao(SkillBook.AcendedorDe(cat, new EstadoDeArvore { Path = dona.Path }, alvo));
			string esperada = $"{cat.Get(quem.Path)?.Nome ?? quem.Path} chega ao nivel {quem.Nivel}";
			if (frase == esperada) rendemCerto++; else erradas.Add($"{cat.Get(alvo)?.Nome ?? alvo}: '{frase}' (esperava '{esperada}')");
		}
		Checa("as 32 acesas por degrau rendem, pelo caminho da tela, exatamente '<acendedor> chega ao nivel 100' -- com o nome do catalogo inteiro (Debuff, Kiai, Volley...)",
			  porDegrau == 32 && rendemCerto == 32, $"{rendemCerto} de {porDegrau}" + (erradas.Count > 0 ? "; " + string.Join(" | ", erradas.Take(3)) : ""));

		// ---- (c) os contra-exemplos na arvore do Body, que esta aberta desde a F2 ----
		string preReq = EstadoDo(pg, "Speed Drills"), porTier = EstadoDo(pg, "Afterimage Technique"), tier2 = LinhaDoTier(pg, 2);
		Checa("CONTRA-EXEMPLO (pre-requisito): 'Speed Drills' continua dizendo 'depois de Evasion Training'", preReq == "depois de Evasion Training", preReq);
		// 3 e nao 4: a F3 comprou Basic Training (1 marco investido), e a regra `invested>=4` do Body.dm
		// passa a pedir os 3 que faltam -- e o mesmo `FaltaInvestir` que a F2 mediu em 4 com nada comprado
		Checa("CONTRA-EXEMPLO (tier): 'Afterimage Technique' continua 'tier trancado' e a linha do TIER 2 continua 'invista 3 marcos ... (você investiu 1)'",
			  porTier == "tier trancado" && tier2.Contains("TRANCADO") && tier2.Contains("invista 3 marcos") && tier2.Contains("(você investiu 1)"), $"{porTier} | {tier2}");

		// ---- a arvore da Mente: e onde moram as 32 ----
		if (Botao(pg, "‹  todas as árvores") is { } voltar) await Clicar(voltar);
		if (CartaoDeArvore(pg, "Strength of Mind")?.GetChild(0) is not Button mind) { Checa("achei o card da Strength of Mind pra clicar", false); return; }
		await Clicar(mind);
		Checa("clicar no card abre a Strength of Mind", Rotulo(pg, "Strength of Mind") != null && Botao(pg, "‹  todas as árvores") != null);

		const string fraseAdv = "abre quando Basic Ki Awareness chega ao nivel 100";
		const string frasePerf = "abre quando Advanced Ki Awareness chega ao nivel 100";
		string adv = EstadoDo(pg, "Advanced Ki Awareness"), perf = EstadoDo(pg, "Perfect Ki Awareness");
		Checa($"(a) 'Advanced Ki Awareness' (enabled=0, sem pre-requisito, sem regra de arvore: so o DEGRAU 100 da Basic a acende) diz QUEM e EM QUE NIVEL: '{fraseAdv}'",
			  adv == fraseAdv, adv);
		Checa($"(a) 'Perfect Ki Awareness' idem, pelo degrau 100 da Advanced: '{frasePerf}'", perf == frasePerf, perf);
		Checa("(a) ...e nenhum dos dois diz 'sem acendedor' (era o texto de antes: a resposta certa com o motivo errado)",
			  adv != "sem acendedor" && perf != "sem acendedor");

		string basic = EstadoDo(pg, "Basic Ki Awareness"), ku = EstadoDo(pg, "Ki Unlocked");
		Checa("(b) CONTRA-EXEMPLO NA MESMA ARVORE: 'Basic Ki Awareness' e trancada por CONDICAO de arvore (kiawarenessskill>=1), nao por degrau: 'abre quando percepção de Ki ≥ 1'",
			  basic == "abre quando percepção de Ki ≥ 1", basic);
		Checa("(b) CONTRA-EXEMPLO: 'Ki Unlocked' (ligada, tier 0) continua compravel: '1 marco'", ku == "1 marco", ku);

		// TODAS as folhas da Mente tem acendedor (condicao ou degrau): "sem acendedor" nao cabe em nenhuma
		var semAcendedor = new List<string>();
		foreach (int t in new[] { 1, 3, 4, 5 })
			foreach (string nome in NomesDaLinha(pg, t))
				if (EstadoDo(pg, nome) == "sem acendedor") semAcendedor.Add(nome);
		Checa("nenhum card da Mente diz 'sem acendedor' (as 15 Basic/Advanced/Perfect tem acendedor: condicao de arvore ou degrau)",
			  semAcendedor.Count == 0, semAcendedor.Count == 0 ? "" : string.Join(", ", semAcendedor));

		// ---- a foto do card, com o compravel ao lado como contra-exemplo de pixel ----
		PanelContainer? card = Cartao(pg, "Advanced Ki Awareness");
		PanelContainer? cardKu = Cartao(pg, "Ki Unlocked");
		if (Ancestral<ScrollContainer>(pg) is { } rol) rol.ScrollVertical = 0;
		await Quadros(2);
		await Rolar(pg, card);
		Image? foto = await Foto();
		await Guardar("depois-11-mente-trancada-por-degrau", foto);
		if (card != null && cardKu != null)
		{
			Color bAdv = Borda(foto, card), bKu = await CorDaBorda(pg, cardKu);
			ChecaNoPixel("no pixel, o card por degrau e TRANCADO (borda apagada) e o 'Ki Unlocked' compravel e laranja -- o texto muda, a cara de trancado nao",
						 foto != null, MaisPerto(bAdv) == "apagada" && MaisPerto(bKu) == "BordaViva", $"{Hex(bAdv)} -> {MaisPerto(bAdv)} / {Hex(bKu)} -> {MaisPerto(bKu)}");
		}

		// ---- (d) a ficha: a mesma frase, por extenso, e nada inventado sobre o quanto falta ----
		if (card?.GetChild(0) is Button bCard)
		{
			await Rolar(pg, card);
			await Clicar(bCard);
			PanelContainer? ficha = menu.FichaDeTeste;
			string motivo = ficha == null ? "(sem ficha)" : Rotulos(ficha).Select(l => l.Text).FirstOrDefault(t => t.StartsWith("Abre sozinha quando") || t.StartsWith("Nenhuma regra") || t.StartsWith("Marco nenhum")) ?? "(sem motivo)";
			Checa("(d) a ficha de 'Advanced Ki Awareness' diz 'Abre sozinha quando: Basic Ki Awareness chega ao nivel 100.'",
				  ficha != null && motivo == "Abre sozinha quando: Basic Ki Awareness chega ao nivel 100.", motivo);
			Checa("(d) ...e NAO inventa o quanto falta (o nivel atual nao viaja no pacote): nenhum rotulo da ficha diz 'falta', 'nível atual' ou 'está no nível'",
				  ficha != null && !Rotulos(ficha).Any(l => l.Text.Contains("falta", StringComparison.OrdinalIgnoreCase) || l.Text.Contains("nível atual") || l.Text.Contains("está no nível")));
			Checa("(d) CONTRA-EXEMPLO: a ficha de uma trancada nao tem 'Comprar' -- so 'Fechar'", ficha != null && Botao(ficha, "Comprar") == null && Botao(ficha, "Fechar") != null);
			await Guardar("depois-12-mente-ficha-por-degrau", await Foto());
			if (ficha != null && Botao(ficha, "Fechar") is { } fechar) await Clicar(fechar);
		}

		// ---- de volta ao Body: as familias seguintes (F5, F6) contam com ele aberto ----
		if (Botao(pg, "‹  todas as árvores") is { } voltar2) await Clicar(voltar2);
		if (CartaoDeArvore(pg, "Strength of Body")?.GetChild(0) is Button body) await Clicar(body);
		Checa("de volta a Strength of Body pras familias seguintes", Cartao(pg, "Basic Training") != null);
	}

	// =====================================================================
	// F5 -- ESQUECER DEVOLVE, EM DOIS CLIQUES
	// =====================================================================
	private async System.Threading.Tasks.Task F5_Esquecer(MenuJogo menu, GameClient cli, SkillCatalog cat)
	{
		Nota("--- F5: esquecer (dois cliques) devolve o marco ---");
		Control? pg = menu.PaginaDeTeste("Learning");
		if (pg == null) return;
		Skill bt = cat.Todas.First(s => s.Nome == "Basic Training");

		if (Cartao(pg, "Basic Training")?.GetChild(0) is not Button b) { Checa("achei o card", false); return; }
		int antes = cli.MarcosLivres;
		await Clicar(b);
		PanelContainer? ficha = menu.FichaDeTeste;
		Button? esquecer = ficha == null ? null : Botao(ficha, "Esquecer");
		Checa("a ficha de uma skill SUA e esquecivel tem 'Esquecer · reembolsa 1'", esquecer != null && esquecer.Text.Contains("reembolsa 1"), esquecer?.Text ?? "");
		Checa("CONTRA-EXEMPLO: ela nao tem 'Comprar'", ficha != null && Botao(ficha, "Comprar") == null);
		if (esquecer == null) return;

		await Clicar(esquecer);
		await Quadros(2);
		Checa("o PRIMEIRO clique so arma ('tem certeza?') e nao manda nada", esquecer.Text.Contains("tem certeza") && cli.SkillsAprendidas.Contains(bt.Path), esquecer.Text);
		await Clicar(esquecer);
		bool saiu = await Ate(() => !cli.SkillsAprendidas.Contains(bt.Path), 5);
		await Quadros(4);
		Checa("o SEGUNDO clique esquece de verdade (o servidor tirou a skill)", saiu);
		Checa("o marco VOLTOU (numero absoluto)", cli.MarcosLivres == antes + 1, $"{antes} -> {cli.MarcosLivres}");
		Checa("o card voltou a dizer o preco", EstadoDo(pg, "Basic Training") == "1 marco", EstadoDo(pg, "Basic Training"));
		Checa("e Evasion Training voltou a pedir Basic Training", EstadoDo(pg, "Evasion Training") == "depois de Basic Training", EstadoDo(pg, "Evasion Training"));
	}

	// =====================================================================
	// F6 -- COMPRAR O TIER 1 INTEIRO: A LINHA DO TIER CONTINUA (o defeito G)
	// =====================================================================
	private async System.Threading.Tasks.Task F6_ALinhaDoTierPersiste(MenuJogo menu, GameClient cli, SkillCatalog cat)
	{
		Nota("--- F6: comprar tudo do tier 1 pela tela -- a linha do tier persiste ---");
		Control? pg = menu.PaginaDeTeste("Learning");
		if (pg == null) return;

		// na ordem dos pre-requisitos: cada uma so vira compravel depois da anterior
		string[] ordem = ["Basic Training", "Evasion Training", "Muscle Training", "Speed Drills", "Light Skill", "Bulk",
						  "Punch Training", "Muscular Precision", "Preparedness", "Basic Blocking"];
		int compradas = 0;
		foreach (string nome in ordem)
		{
			Skill s = cat.Todas.First(x => x.Nome == nome);
			if (Cartao(pg, nome)?.GetChild(0) is not Button b) { Nota($"  nao achei o card de {nome}"); continue; }
			await Rolar(pg, Cartao(pg, nome));
			await Clicar(b);
			Button? comprar = menu.FichaDeTeste == null ? null : Botao(menu.FichaDeTeste, "Comprar");
			if (comprar == null) { Nota($"  {nome}: sem botao de comprar (estado '{EstadoDo(pg, nome)}')"); menu.Fechar(); menu.Abrir(); continue; }
			await Clicar(comprar);
			if (await Ate(() => cli.SkillsAprendidas.Contains(s.Path), 5)) compradas++;
			await Quadros(3);
		}
		Checa("as 10 skills compraveis do tier 1 foram compradas PELA TELA, na ordem dos pre-requisitos", compradas == 10, $"{compradas} de 10");

		string tier1 = LinhaDoTier(pg, 1);
		Checa("DEFEITO G MORTO: a linha 'TIER 1' continua existindo com tudo comprado", Rotulo(pg, "TIER 1") != null && tier1.Contains("10 de 11 suas"), tier1);
		Checa("Rapid Movement continua no lugar dela, apagada, dizendo por que", EstadoDo(pg, "Rapid Movement") == "sem acendedor", EstadoDo(pg, "Rapid Movement"));
		string tier2 = LinhaDoTier(pg, 2), tier3 = LinhaDoTier(pg, 3);
		Checa("com 10 marcos investidos, TIER 2 e TIER 3 ABRIRAM (as regras >=4 e >=7 do Body.dm), e a linha diz isso",
			  !tier2.Contains("TRANCADO") && !tier3.Contains("TRANCADO"), $"{tier2} | {tier3}");
		// 2 marcos, e nao 1: o custo de uma skill sem `fixedcost` e o TIER dela (`CustoDe`)
		Checa("'Afterimage Technique' (tier 2) virou COMPRAVEL: 'tier trancado' sumiu e o preco e o do tier (2)", EstadoDo(pg, "Afterimage Technique") == "2 marcos", EstadoDo(pg, "Afterimage Technique"));
		Checa("'Force of Will' (tier 2, pede Punch Training, que agora e sua) virou compravel", EstadoDo(pg, "Force of Will") == "2 marcos", EstadoDo(pg, "Force of Will"));
		await Rolar(pg, Cartao(pg, "Basic Training"));
		await Guardar("depois-08-tier-1-todo-comprado", await Foto());
	}

	// =====================================================================
	// F7 -- A BUSCA ACHA SKILL E LEVA ATE ELA (o defeito E)
	// =====================================================================
	private async System.Threading.Tasks.Task F7_ABuscaAchaSkill(MenuJogo menu, GameClient cli, SkillCatalog cat)
	{
		Nota("--- F7: a busca acha skill pelo nome e abre a arvore certa ---");
		LineEdit? busca = Achar<LineEdit>(menu, l => l.PlaceholderText.StartsWith("procurar"));
		Checa("achei o campo de busca do topo", busca != null);
		if (busca == null) return;

		Digitar(busca, "afterimage");
		await Quadros(3);
		Control? pg = menu.PaginaDeTeste("Learning");
		Button? achado = pg == null ? null : Botao(pg, "Afterimage Technique");
		Checa("digitar 'afterimage' lista a SKILL 'Afterimage Technique' (e nao so verbs)", achado != null);
		Checa("...com a arvore e o tier no rotulo", achado != null && achado.Text.Contains("[Strength of Body · tier 2]"), achado?.Text ?? "");
		Checa("CONTRA-EXEMPLO: um nome que nao existe nao acha nada", Prova(() => { Digitar(busca, "xyzzy"); return Rotulos(pg!).Any(l => l.Text.StartsWith("Nenhuma acao nem habilidade")); }));
		Digitar(busca, "afterimage");
		await Quadros(3);
		await Guardar("depois-06-busca", await Foto());

		achado = pg == null ? null : Botao(pg, "Afterimage Technique");
		if (achado == null) return;
		await Clicar(achado);
		Checa("clicar no achado LIMPA a busca e abre a arvore certa (Strength of Body)", busca.Text.Length == 0 && Botao(pg!, "‹  todas as árvores") != null && Rotulo(pg!, "Strength of Body") != null);
		Checa("...e abre a FICHA da skill", menu.FichaDeTeste != null && Rotulo(menu.FichaDeTeste!, "Afterimage Technique") != null);
		menu.Fechar();
		await Quadros(2);
		menu.Abrir();
		await Quadros(2);
		Checa("fechar e abrir o menu fecha a ficha", menu.FichaDeTeste == null);
	}

	private bool Prova(Func<bool> f) { try { return f(); } catch (Exception e) { Nota("  excecao: " + e.Message); return false; } }

	// =====================================================================
	// F8 -- A ABA SKILLS: nome + arvore/tier + o que faz (o defeito H)
	// =====================================================================
	private async System.Threading.Tasks.Task F8_AAbaSkills(MenuJogo menu, GameClient cli, SkillCatalog cat)
	{
		Nota("--- F8: a aba Skills (o que ja sei) ---");
		Button? aba = Botao(menu, "Skills");
		if (aba == null) { Checa("achei a aba Skills", false); return; }
		await Clicar(aba);
		Control? pg = menu.PaginaDeTeste("Skills");
		Checa("a aba Skills esta na tela", pg is { Visible: true } && menu.AbaDeTeste == "Skills");
		if (pg == null) return;

		Checa("as compradas aparecem agrupadas pela arvore ('STRENGTH OF BODY (10)')", Rotulos(pg).Any(l => l.Text.StartsWith("STRENGTH OF BODY") && l.Text.Contains("(10)")));
		string? valor = menu.ValorDesenhado("Skills", "Basic Training");
		Checa("a coluna da direita diz o TIER e a categoria limpa ('tier 1 · buff'), nao o `type` cru", valor != null && valor.Contains("tier 1") && valor.Contains("buff"), valor ?? "(nao achei a linha)");
		Checa("DEFEITO H MORTO: nenhum rotulo mostra o `type` sujo do DM ('Body Buff', 'Sprit Buff', 'misc')",
			  !Rotulos(pg).Any(l => l.Text is "Body Buff" or "Sprit Buff" or "misc" or "Misc" or "Physical"));
		Checa("cada linha diz O QUE A SKILL FAZ (os efeitos extraidos, em portugues)", Rotulos(pg).Any(l => l.Text.Contains("ataque físico +0,1") || l.Text.Contains("ataque físico +0.1")));
		await Guardar("depois-07-aba-skills", await Foto());

		Button? voltar = Botao(menu, "Learning");
		if (voltar != null) await Clicar(voltar);
	}

	// =====================================================================
	// F9 -- A ASSINATURA, PECA A PECA (e a injecao que reproduz o defeito A)
	// =====================================================================
	private async System.Threading.Tasks.Task F9_AAssinaturaPecaAPeca(MenuJogo menu, GameClient cli, SkillCatalog cat)
	{
		Nota("--- F9: toda variavel que muda a tela entra na assinatura ---");
		var nomes = menu.PecasDaAssinaturaDeTeste("Learning").Select(p => p.Nome).ToList();
		Checa("a assinatura de Learning tem as pecas: busca, arvore, aprendidas, marcos, vilao, arvores, ficha, verbos",
			  new[] { "busca", "arvore", "aprendidas", "marcos", "vilao", "arvores", "ficha", "verbos" }.All(nomes.Contains), string.Join(",", nomes));

		string Peca(string n) => menu.PecasDaAssinaturaDeTeste("Learning").First(p => p.Nome == n).Valor;
		Control? pg = menu.PaginaDeTeste("Learning");
		if (pg == null) return;

		// ---- QUIETUDE: sem nada acontecer, nenhuma peca muda e a pagina NAO remonta ----
		// E o outro lado da mesma moeda: uma peca volatil (que mude a cada pacote) remontaria a
		// pagina cinco vezes por segundo e mataria o botao debaixo do dedo -- foi o que a rajada do
		// catalogo de verbs fazia antes do redesenho adiado.
		{
			var antes = menu.PecasDaAssinaturaDeTeste("Learning").ToList();
			int rq = menu.RemontagensDeTeste;
			await Segundos(1.5);
			var depois = menu.PecasDaAssinaturaDeTeste("Learning").ToList();
			var mudaram = antes.Where(a => depois.First(d => d.Nome == a.Nome).Valor != a.Valor).Select(a => a.Nome).ToList();
			Checa("QUIETUDE: em 1,5 s sem acao nenhuma, nenhuma peca muda e a pagina nao e remontada",
				  mudaram.Count == 0 && menu.RemontagensDeTeste == rq,
				  $"pecas que mudaram: [{string.Join(",", mudaram)}], remontagens {rq} -> {menu.RemontagensDeTeste}");
		}

		// ---- verbos: registrar um verb fantasma remonta a pagina e ele aparece ----
		int r0 = menu.RemontagensDeTeste;
		string v0 = Peca("verbos");
		Verbos.Registrar(new Verbo("Bancada: verbo fantasma", Verbos.Aprendizado, "so pra bancada", null));
		await Quadros(3);
		Checa("peca 'verbos': registrar um verb de Learning muda a peca, REMONTA a pagina e o botao aparece",
			  Peca("verbos") != v0 && menu.RemontagensDeTeste > r0 && Botao(pg, "Bancada: verbo fantasma") != null,
			  $"remontagens {r0} -> {menu.RemontagensDeTeste}");
		Verbos.Esquecer("Bancada: verbo fantasma");
		await Quadros(3);

		// ---- arvore: abrir uma arvore remonta ----
		r0 = menu.RemontagensDeTeste;
		if (CartaoDeArvore(pg, "Strength of Mind")?.GetChild(0) is Button mind) await Clicar(mind);
		Checa("peca 'arvore': abrir uma arvore muda a peca e remonta", Peca("arvore").EndsWith("/Mind") && menu.RemontagensDeTeste > r0, Peca("arvore"));
		if (Botao(pg, "‹  todas as árvores") is { } voltar) await Clicar(voltar);

		// ---- vilao: o bit vem do servidor e troca o card do Planet Destroy ----
		if (CartaoDeArvore(pg, "Strength of Body")?.GetChild(0) is Button body) await Clicar(body);
		r0 = menu.RemontagensDeTeste;
		string antesVilao = EstadoDo(pg, "Planet Destroy");
		cli.SendVerbo("admin_vilao", "");
		bool virou = await Ate(() => cli.SouVilao, 4);
		await Quadros(3);
		Checa("peca 'vilao': virar vilao (admin_vilao) remonta e o card do Planet Destroy deixa de dizer 'so pra vilao'",
			  virou && menu.RemontagensDeTeste > r0 && antesVilao == "só pra vilão" && EstadoDo(pg, "Planet Destroy") != "só pra vilão",
			  $"'{antesVilao}' -> '{EstadoDo(pg, "Planet Destroy")}'");
		cli.SendVerbo("admin_vilao", "");
		await Ate(() => !cli.SouVilao, 4);
		await Quadros(3);

		// ---- marcos: um marco do admin muda a faixa ----
		r0 = menu.RemontagensDeTeste;
		int m0 = cli.MarcosLivres;
		cli.SendVerbo("admin_marco", "|1");
		bool veio = await Ate(() => cli.MarcosLivres == m0 + 1, 4);
		await Quadros(3);
		Checa("peca 'marcos': um marco chegando do servidor remonta e a faixa mostra o novo numero",
			  veio && menu.RemontagensDeTeste > r0 && TextoDaFaixa(menu) == (m0 + 1).ToString(), $"faixa diz {TextoDaFaixa(menu)}, cliente {cli.MarcosLivres}");

		// ---- busca: digitar remonta (a pagina inteira vira os achados) ----
		if (Achar<LineEdit>(menu, l => l.PlaceholderText.StartsWith("procurar")) is { } busca)
		{
			r0 = menu.RemontagensDeTeste;
			Digitar(busca, "ki");
			await Quadros(2);
			Checa("peca 'busca': digitar muda a peca e remonta", Peca("busca") == "ki" && menu.RemontagensDeTeste > r0);
			Digitar(busca, "");
			await Quadros(2);
		}

		// ============================ A INJECAO: O DEFEITO A, REPRODUZIDO ============================
		// Tira a peca "marcos" da assinatura e manda um marco. O dado muda no cliente, a pagina NAO
		// remonta e a faixa fica com o numero velho -- e exatamente a gaveta que nao abria.
		MenuJogo.PedacoIgnoradoDeTeste = "marcos";
		// A ASSINATURA GUARDADA TEM QUE SER DA FORMA NOVA antes do marco chegar: tirar uma peca muda a
		// propria string, e a primeira comparacao remontaria por isso -- e nao pelo marco.
		menu.ForcarRedesenho();
		await Quadros(2);
		r0 = menu.RemontagensDeTeste;
		m0 = cli.MarcosLivres;
		string faixaAntes = TextoDaFaixa(menu);
		var pecasAntes = menu.PecasDaAssinaturaDeTeste("Learning").ToList();
		cli.SendVerbo("admin_marco", "|1");
		veio = await Ate(() => cli.MarcosLivres == m0 + 1, 4);
		await Segundos(0.8);
		var pecasDepois = menu.PecasDaAssinaturaDeTeste("Learning").ToList();
		foreach ((string n, string v) in pecasAntes)
			if (pecasDepois.First(d => d.Nome == n).Valor != v) Nota($"    peca que mudou com o marco: {n}: '{v}' -> '{pecasDepois.First(d => d.Nome == n).Valor}'");
		Injeta("com a peca 'marcos' FORA da assinatura, o marco chega no cliente mas a pagina NAO remonta e a faixa fica velha (defeito A)",
			   veio && menu.RemontagensDeTeste == r0 && TextoDaFaixa(menu) == faixaAntes && faixaAntes != cli.MarcosLivres.ToString(),
			   $"cliente {m0} -> {cli.MarcosLivres}, faixa '{faixaAntes}' -> '{TextoDaFaixa(menu)}', remontagens {r0} -> {menu.RemontagensDeTeste}");
		MenuJogo.PedacoIgnoradoDeTeste = "";
		menu.ForcarRedesenho();
		await Quadros(2);
		Checa("devolvida a peca, a faixa volta a concordar com o cliente", TextoDaFaixa(menu) == cli.MarcosLivres.ToString(), TextoDaFaixa(menu));
	}

	// =====================================================================
	// F10 -- O TEMA DA FICHA: a injecao do AcceptDialog de fabrica
	// =====================================================================
	private async System.Threading.Tasks.Task F10_OTemaDaFicha(MenuJogo menu, GameClient cli, SkillCatalog cat)
	{
		Nota("--- F10: a paleta -- e o AcceptDialog de fabrica reposto ---");

		// O CODIGO ANTIGO, literal: o que a foto 06 de antes mostrava, cinza de fabrica sobre um menu
		// pintado pela casa. Se a conta de paleta nao ficar vermelha aqui, ela nao vale nada verde na F3.
		var velha = new AcceptDialog { Title = "Basic Training", MinSize = new Vector2I(440, 0), OkButtonText = "Comprar  ·  1 marco" };
		velha.AddCancelButton("Cancelar");
		var caixa = new VBoxContainer();
		velha.AddChild(caixa);
		caixa.AddChild(new Label { Text = "The user hones their body. P.Off+, P.Def+, Tech+", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 0) });
		caixa.AddChild(new HSeparator());
		caixa.AddChild(new Label { Text = "• ataque físico +0,1" });
		AddChild(velha);
		velha.PopupCentered();
		await Quadros(6);

		Image? foto = await Foto();
		await Guardar("depois-09-INJETADO-acceptdialog", foto);
		var r = new Rect2I(velha.Position, velha.Size);
		(Color cor, float frac) = Moda(foto, r);
		// SO VALE SE O DIALOGO ESTA NA FOTO: a primeira rodada mediu a grama do mundo no retangulo
		// dele (o menu tinha sido fechado por tecla de fora) e "pegou" pelo motivo errado. Um dialogo
		// de verdade cobre o proprio retangulo com uma cor so em mais de 40% dos pixels.
		bool naFoto = foto != null && velha.Visible && frac > 0.4f;
		if (foto != null && !naFoto) { Nota($"  PULADA  (injecao) o AcceptDialog nao esta na foto (moda {Hex(cor)} em {frac * 100:0}% do retangulo {r})"); _pulados++; _naoMedidos.Add("(injecao) AcceptDialog fora da foto"); }
		InjetaNoPixel("com a ficha de volta ao AcceptDialog de fabrica, a conta de paleta ACUSA cor fora do tema",
					  naFoto, !NaPaleta(cor), $"moda {Hex(cor)} ({frac * 100:0}%), retangulo {r}");
		velha.QueueFree();
		await Quadros(3);
	}

	// =====================================================================
	// F11 -- DETERMINISMO: a mesma arvore no mesmo estado desenha igual
	// =====================================================================
	private async System.Threading.Tasks.Task F11_Determinismo(MenuJogo menu, GameClient cli, SkillCatalog cat)
	{
		Nota("--- F11: determinismo visual ---");
		Control? pg = menu.PaginaDeTeste("Learning");
		if (pg == null) return;
		if (Botao(pg, "‹  todas as árvores") == null && CartaoDeArvore(pg, "Strength of Body")?.GetChild(0) is Button body) await Clicar(body);

		List<string> a = [.. NomesDaLinha(pg, 1), "|", .. NomesDaLinha(pg, 2), "|", .. NomesDaLinha(pg, 3)];
		menu.ForcarRedesenho();
		await Quadros(3);
		List<string> b = [.. NomesDaLinha(pg, 1), "|", .. NomesDaLinha(pg, 2), "|", .. NomesDaLinha(pg, 3)];
		Checa("duas montagens da mesma arvore no mesmo estado dao a MESMA ordem de cards", a.SequenceEqual(b) && a.Count > 5, $"{a.Count} entradas");

		Skill arv = cat.Arvores.First(x => x.Nome == "Strength of Body");
		List<string> catalogo = [];
		foreach (int t in new[] { 1, 2, 3 })
		{
			if (t > 1) catalogo.Add("|");
			catalogo.AddRange(arv.Galhos.Select(cat.Get).Where(s => s is { Arvore: false } && s.Nome.Length > 0 && s.Tier == t).Select(s => s!.Nome));
		}
		Checa("...e e a ordem do CATALOGO nos tres tiers", b.SequenceEqual(catalogo), string.Join(" ", b.Take(6)) + " ...");
	}

	// =====================================================================
	// F12 -- A TIRA ANTES x DEPOIS
	// =====================================================================
	private void F12_ATiraAntesXDepois()
	{
		Nota("--- F12: a tira antes x depois ---");
		if (_antes.Length == 0) { Nota("  PULADA  tira antes x depois   [sem --antes]"); _pulados++; _naoMedidos.Add("tira antes x depois (sem --antes)"); return; }
		string ant = _antes.EndsWith('/') || _antes.EndsWith('\\') ? _antes : _antes + "/";

		(string Antes, string Depois, string Legenda)[] pares =
		[
			("02-learning-lista-de-arvores", "depois-01-arvores", "1 ARVORES"),
			("03-arvore-aberta-topo", "depois-02-body-3-tiers", "2 A ARVORE"),
			("06-ficha-da-skill-selecionada", "depois-03-ficha", "3 A FICHA"),
			("07-depois-de-comprar", "depois-04-depois-de-comprar", "4 COMPROU"),
			("09-aba-skills-sabidas", "depois-07-aba-skills", "5 ABA SKILLS"),
			("12-busca-ki", "depois-06-busca", "6 A BUSCA"),
		];
		var quadros = new List<TiraDeFotos.Quadro>();
		foreach ((string antes, string depois, string legenda) in pares)
		{
			Image? a = Image.LoadFromFile(ant + antes + ".png");
			if (a == null || a.IsEmpty() || !_fotos.TryGetValue(depois, out Image? d))
			{
				Nota($"  PULADA  par {legenda}   [falta {(a == null || a.IsEmpty() ? antes : depois)}]");
				_pulados++; _naoMedidos.Add($"tira: par {legenda}");
				continue;
			}
			quadros.Add(new TiraDeFotos.Quadro(TiraDeFotos.Empilhar(Metade(a), Metade(d)), legenda));
		}
		if (quadros.Count == 0) return;
		string caminho = _pasta + "TIRA-ANTES-x-DEPOIS.png";
		double pintada = TiraDeFotos.Montar(quadros, caminho);
		Checa("a tira ANTES (em cima) x DEPOIS (embaixo) saiu pintada", pintada > 0.5, $"{pintada * 100:0}% da tira e imagem");
		Checa("...e cada quadro dela tem a legenda desenhada", TiraDeFotos.QuadrosComRotulo(caminho, quadros) == quadros.Count, $"{TiraDeFotos.QuadrosComRotulo(caminho, quadros)} de {quadros.Count}");
		_fotosGravadas.Add(caminho);
		Nota($"  tira: {caminho}");
	}

	private static Image Metade(Image img)
	{
		var c = (Image)img.Duplicate();
		c.Convert(Image.Format.Rgba8);
		c.Resize(Math.Max(1, img.GetWidth() / 2), Math.Max(1, img.GetHeight() / 2), Image.Interpolation.Bilinear);
		return c;
	}

	// =====================================================================
	// LER A TELA
	// =====================================================================
	private static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node n in Todos(f)) yield return n;
	}

	private static T? Achar<T>(Node raiz, Func<T, bool> quer) where T : Node =>
		Todos(raiz).OfType<T>().FirstOrDefault(quer);

	private static T? Ancestral<T>(Node n) where T : Node
	{
		for (Node? p = n.GetParent(); p != null; p = p.GetParent())
			if (p is T t) return t;
		return null;
	}

	/// <summary>Um botao pelo TEXTO, exato primeiro e prefixo depois. So conta se estiver visivel.</summary>
	private static Button? Botao(Node raiz, string texto, bool soVisivel = true)
	{
		List<Button> vistos = Todos(raiz).OfType<Button>().Where(b => !soVisivel || b.IsVisibleInTree()).ToList();
		return vistos.FirstOrDefault(b => string.Equals(b.Text, texto, StringComparison.OrdinalIgnoreCase))
			?? vistos.FirstOrDefault(b => b.Text.StartsWith(texto, StringComparison.OrdinalIgnoreCase));
	}

	private static IEnumerable<Label> Rotulos(Node raiz) => Todos(raiz).OfType<Label>().Where(l => l.IsVisibleInTree());

	private static Label? Rotulo(Node raiz, string texto) => Rotulos(raiz).FirstOrDefault(l => l.Text == texto);

	/// <summary>O card de SKILL cujo rotulo de nome e este. Achado pelo texto, como quem le a tela.</summary>
	private static PanelContainer? Cartao(Node raiz, string nome)
	{
		foreach (Label l in Rotulos(raiz))
		{
			if (l.Name != "Nome" || l.Text != nome) continue;
			// PELO METADADO, e nao pelo `Name`: o Godot renomeia irmaos homonimos pra "@PanelContainer@736"
			// (a CLASSE, nao o nome dado) -- so o primeiro card da grade guardava "CartaoDeSkill", e a
			// primeira rodada desta bancada achou so ele.
			if (Ancestral<PanelContainer>(l) is { } p && EhCartao(p, "skill")) return p;
		}
		// DIAGNOSTICO: por que nao achei -- os rotulos com este texto, e onde eles estao pendurados
		foreach (Label l in Todos(raiz).OfType<Label>().Where(l => l.Text == nome))
		{
			var cadeia = new List<string>();
			for (Node? n = l; n != null && cadeia.Count < 5; n = n.GetParent()) cadeia.Add($"{n.GetType().Name}:{n.Name}");
			Nota($"    (Cartao '{nome}' nao achado) rotulo Name='{l.Name}' visivel={l.IsVisibleInTree()} cadeia={string.Join(" < ", cadeia)}");
		}
		return null;
	}

	/// <summary>O card de ARVORE cujo titulo e este.</summary>
	private static PanelContainer? CartaoDeArvore(Node raiz, string nome)
	{
		foreach (Label l in Rotulos(raiz))
		{
			if (l.Text != nome) continue;
			if (Ancestral<PanelContainer>(l) is { } p && EhCartao(p, "arvore")) return p;
		}
		return null;
	}

	private static bool EhCartao(Node n, string tipo) => n.HasMeta("cartao") && n.GetMeta("cartao").AsString() == tipo;

	private static string Estado(PanelContainer card) => Achar<Label>(card, l => l.Name == "Estado")?.Text ?? "(sem estado)";

	private static string EstadoDo(Node raiz, string nome) => Cartao(raiz, nome) is { } c ? Estado(c) : "(sem card)";

	/// <summary>O texto inteiro da linha "TIER N ...": o rotulo e o detalhe ao lado dele.</summary>
	private static string LinhaDoTier(Node raiz, int tier)
	{
		Label? r = Rotulo(raiz, $"TIER {tier}");
		if (r?.GetParent() is not HBoxContainer h) return "(sem linha)";
		return string.Join("  ", h.GetChildren().OfType<Label>().Select(l => l.Text));
	}

	/// <summary>Os nomes dos cards da grade que vem logo depois da linha do tier, na ordem em que estao.</summary>
	private static List<string> NomesDaLinha(Node raiz, int tier)
	{
		Label? r = Rotulo(raiz, $"TIER {tier}");
		if (r?.GetParent() is not HBoxContainer h || h.GetParent() is not Control pai) return [];
		int i = h.GetIndex();
		for (int k = i + 1; k < pai.GetChildCount(); k++)
			if (pai.GetChild(k) is HFlowContainer grade)
				return grade.GetChildren().OfType<PanelContainer>().Select(c => Achar<Label>(c, l => l.Name == "Nome")?.Text ?? "?").ToList();
		return [];
	}

	private static string TextoDaFaixa(MenuJogo menu) =>
		Achar<Label>(menu, l => Ancestral<PanelContainer>(l)?.Name == "FaixaDeMarcos" && int.TryParse(l.Text, out _))?.Text ?? "(sem faixa)";

	private static void Digitar(LineEdit campo, string texto)
	{
		campo.Text = texto;
		campo.EmitSignal(LineEdit.SignalName.TextChanged, texto);
	}

	// =====================================================================
	// O MOUSE, O CLIQUE, A ESPERA
	// =====================================================================
	private async System.Threading.Tasks.Task Clicar(Button b)
	{
		b.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(3);
	}

	private async System.Threading.Tasks.Task Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async System.Threading.Tasks.Task Segundos(double s)
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

	private void Mouse(Vector2 onde)
	{
		var m = new InputEventMouseMotion { Position = onde, GlobalPosition = onde, Relative = new Vector2(1, 1) };
		GetViewport().PushInput(m, true);
	}

	private async System.Threading.Tasks.Task SobreOControle(Control c)
	{
		Rect2 r = c.GetGlobalRect();
		Mouse(r.Position + r.Size / 2);
		await Quadros(3);
	}

	private async System.Threading.Tasks.Task ForaDoAlcance()
	{
		Mouse(new Vector2(2, 2));
		await Quadros(3);
	}

	private async System.Threading.Tasks.Task Rolar(Control pagina, Control? alvo)
	{
		if (alvo != null && Ancestral<ScrollContainer>(pagina) is { } rol) rol.EnsureControlVisible(alvo);
		await Quadros(3);
	}

	// =====================================================================
	// A FOTO E O PIXEL
	// =====================================================================
	/// <summary>Quantas vezes o menu de PAUSA foi achado aberto antes de uma foto -- tecla de fora chegando na janela.</summary>
	private int _intrusoes;

	private async System.Threading.Tasks.Task<Image?> Foto()
	{
		// ============================ A PAUSA ABERTA E TECLA DE FORA, E ELA CEGA A FOTO ============================
		// O menu de pausa e um veu preto de 72% na camada 20: com ele aberto TODA leitura de pixel
		// desta bancada mede o veu (o "43" laranja saiu #432d12 na primeira rodada). Nada no roteiro
		// aperta ESC, entao pausa aberta e teclado do dono chegando aqui -- ver `--semfoco` no Boot.
		// Fecha, conta, e o placar diz quantas vezes aconteceu: uma rodada com intrusao nao e limpa.
		// ==========================================================================================
		if (PauseMenu.Instancia is { Aberto: true } pausa)
		{
			_intrusoes++;
			Nota("  AVISO  o menu de PAUSA estava aberto (tecla de fora chegou na janela) -- fechando pra fotografar");
			pausa.Fechar("bancada fechou a pausa aberta por tecla de fora");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		return img == null || img.IsEmpty() ? null : img;
	}

	private async System.Threading.Tasks.Task Guardar(string nome, Image? foto)
	{
		if (foto == null) { _pulados++; _naoMedidos.Add($"foto {nome}"); Nota($"  --     sem foto pra {nome} (headless?)"); return; }
		string caminho = _pasta + nome + ".png";
		foto.SavePng(caminho);
		_fotos[nome] = foto;
		_fotosGravadas.Add(caminho);
		Nota("  foto   " + caminho);
		await System.Threading.Tasks.Task.CompletedTask;
	}

	private static Rect2I Caixa(Rect2 r, int folga) => new(
		(int)r.Position.X + folga, (int)r.Position.Y + folga,
		Math.Max(1, (int)r.Size.X - folga * 2), Math.Max(1, (int)r.Size.Y - folga * 2));

	/// <summary>A cor mais frequente de um retangulo da foto, e que fracao dele ela ocupa.</summary>
	private static (Color, float) Moda(Image? img, Rect2I r)
	{
		if (img == null) return (new Color(0, 0, 0), 0);
		var conta = new Dictionary<uint, int>();
		int total = 0;
		int x1 = Math.Min(img.GetWidth(), r.Position.X + r.Size.X);
		int y1 = Math.Min(img.GetHeight(), r.Position.Y + r.Size.Y);
		for (int y = Math.Max(0, r.Position.Y); y < y1; y++)
			for (int x = Math.Max(0, r.Position.X); x < x1; x++)
			{
				uint k = Chave(img.GetPixel(x, y));
				conta[k] = conta.GetValueOrDefault(k) + 1;
				total++;
			}
		if (total == 0) return (new Color(0, 0, 0), 0);
		KeyValuePair<uint, int> top = conta.OrderByDescending(kv => kv.Value).First();
		return (Cor(top.Key), top.Value / (float)total);
	}

	private static uint Chave(Color c) =>
		((uint)(c.R * 255 + 0.5f) << 16) | ((uint)(c.G * 255 + 0.5f) << 8) | (uint)(c.B * 255 + 0.5f);

	private static Color Cor(uint k) => Color.Color8((byte)(k >> 16), (byte)((k >> 8) & 0xff), (byte)(k & 0xff));

	/// <summary>Quantas linhas do retangulo tem um pixel claro: a altura do glifo desenhado ali.</summary>
	private static int AlturaDoGlifo(Image? img, Rect2 r)
	{
		if (img == null) return 0;
		int linhas = 0;
		for (int y = (int)r.Position.Y; y < (int)(r.Position.Y + r.Size.Y) && y < img.GetHeight(); y++)
		{
			bool clara = false;
			for (int x = (int)r.Position.X; x < (int)(r.Position.X + r.Size.X) && x < img.GetWidth(); x++)
				if (img.GetPixel(x, y).Luminance > 0.5f) { clara = true; break; }
			if (clara) linhas++;
		}
		return linhas;
	}

	/// <summary>
	/// A COR DA BORDA DE UM CARD, lida na foto: o pixel do meio da aresta de CIMA, um pixel pra dentro
	/// (a borda tem 1 ou 2 px; o canto e arredondado, o meio da aresta e reto).
	/// </summary>
	private async System.Threading.Tasks.Task<Color> CorDaBorda(Control pagina, Control card, bool rolar = true)
	{
		if (rolar) await Rolar(pagina, card);
		return Borda(await Foto(), card);
	}

	/// <summary>
	/// A mesma leitura numa foto JA TIRADA -- pra cobrar dois cards na MESMA foto. `ondeNoTopo` e a
	/// fracao da largura onde ler (0,5 = o meio): a etiqueta "REQUISITO" pendura no MEIO da aresta de
	/// cima, entao a borda de um pre-requisito marcado se le a 0,2 -- ja passou o canto arredondado
	/// (raio 6) e ainda nao chegou na pilula.
	/// </summary>
	private static Color Borda(Image? img, Control card, float ondeNoTopo = 0.5f)
	{
		if (img == null) return new Color(0, 0, 0);
		Rect2 r = card.GetGlobalRect();
		return MaisAcesa(img, (int)(r.Position.X + r.Size.X * ondeNoTopo), (int)r.Position.Y);
	}

	/// <summary>
	/// O pixel mais ACESO de tres linhas a partir de y0. A BORDA TEM SERRILHADO: a primeira linha e meio
	/// misturada com o que ha atras (o `StyleBoxFlat` suaviza a aresta de fora), e uma borda de 1 px pode
	/// ser inteira mistura. Das tres primeiras linhas, a mais acesa e a que carrega a cor da borda; as
	/// apagadas continuam apagadas.
	/// </summary>
	private static Color MaisAcesa(Image img, int x, int y0)
	{
		if (x < 0 || y0 < 0 || x >= img.GetWidth() || y0 + 2 >= img.GetHeight()) return new Color(0, 0, 0);
		Color melhor = img.GetPixel(x, y0);
		for (int dy = 1; dy <= 2; dy++)
		{
			Color c = img.GetPixel(x, y0 + dy);
			if (Math.Max(c.R, Math.Max(c.G, c.B)) > Math.Max(melhor.R, Math.Max(melhor.G, melhor.B))) melhor = c;
		}
		return melhor;
	}

	/// <summary>
	/// A CHAPA do card nesta foto: o pixel 4 px pra dentro da aresta ESQUERDA, no meio da altura -- ja
	/// passou a borda (1 ou 2 px, mais o serrilhado) e ainda esta na margem de conteudo, onde nao ha texto.
	/// </summary>
	private static Color Chapa(Image? img, Control card)
	{
		if (img == null) return new Color(0, 0, 0);
		Rect2 r = card.GetGlobalRect();
		int x = (int)r.Position.X + 4, y = (int)(r.Position.Y + r.Size.Y / 2);
		return x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight() ? new Color(0, 0, 0) : img.GetPixel(x, y);
	}

	/// <summary>
	/// O pixel 2 px FORA da aresta esquerda do card, no meio da altura: onde um brilho apareceria. A
	/// esquerda e nao acima, porque a etiqueta de pre-requisito pendura na aresta de CIMA do card.
	/// </summary>
	private static Color ForaDoCard(Image? img, Control card)
	{
		if (img == null) return new Color(0, 0, 0);
		Rect2 r = card.GetGlobalRect();
		int x = (int)r.Position.X - 2, y = (int)(r.Position.Y + r.Size.Y / 2);
		return x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight() ? new Color(0, 0, 0) : img.GetPixel(x, y);
	}

	/// <summary>QUENTE = puxa pro laranja (R acima de B). O fundo do menu e frio (cinza-azulado); o brilho do hover e laranja.</summary>
	private static bool Quente(Color c) => c.R - c.B > 0.06f;

	/// <summary>Quantas linhas do retangulo tem um pixel ESCURO: as letras dentro de uma pilula clara.</summary>
	private static int LinhasComPixelEscuro(Image? img, Rect2I r)
	{
		if (img == null) return 0;
		int linhas = 0;
		for (int y = Math.Max(0, r.Position.Y); y < r.Position.Y + r.Size.Y && y < img.GetHeight(); y++)
		{
			bool escura = false;
			for (int x = Math.Max(0, r.Position.X); x < r.Position.X + r.Size.X && x < img.GetWidth(); x++)
				if (img.GetPixel(x, y).Luminance < 0.3f) { escura = true; break; }
			if (escura) linhas++;
		}
		return linhas;
	}

	/// <summary>A etiqueta "requisito" de um card (o rotulo de nome "Requisito"), exista ou nao.</summary>
	private static Label? Etiqueta(Node card) => Achar<Label>(card, l => l.Name == "Requisito");

	private static int EtiquetasVisiveis(Node raiz) => Todos(raiz).OfType<Label>().Count(l => l.Name == "Requisito" && l.IsVisibleInTree());

	private static Vector2 MeioDe(Control c) => c.GetGlobalRect().GetCenter();

	/// <summary>A luminancia media do icone do card, na foto.</summary>
	private async System.Threading.Tasks.Task<float> LumDoIcone(Control pagina, Control card)
	{
		await Rolar(pagina, card);
		Image? img = await Foto();
		TextureRect? icone = Achar<TextureRect>(card, t => t.Name == "Icone");
		if (img == null || icone == null) return 0;
		Rect2I r = Caixa(icone.GetGlobalRect(), 2);
		float soma = 0; int n = 0;
		for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y && y < img.GetHeight(); y++)
			for (int x = r.Position.X; x < r.Position.X + r.Size.X && x < img.GetWidth(); x++)
			{ soma += img.GetPixel(x, y).Luminance; n++; }
		return n == 0 ? 0 : soma / n;
	}

	// =====================================================================
	// A PALETA, perguntada ao Tema por reflexao -- nunca uma lista escrita aqui
	// =====================================================================
	private static IEnumerable<Color> Paleta() =>
		typeof(Tema).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
			.Where(f => f.FieldType == typeof(Color))
			.Select(f => (Color)f.GetValue(null)!);

	/// <summary>Perto o bastante pra ser a mesma cor com a mistura do veu por baixo (o painel tem alfa 0,9).</summary>
	private static bool Perto(Color a, Color b) =>
		Math.Abs(a.R - b.R) <= 0.035f && Math.Abs(a.G - b.G) <= 0.035f && Math.Abs(a.B - b.B) <= 0.035f;

	private static bool NaPaleta(Color c) => Paleta().Any(p => Perto(c, p));

	/// <summary>
	/// QUE BORDA E ESTA: "Bom" (verde, sua), "BordaViva" (laranja: compravel, e tambem o HOVER do tema),
	/// "branca" (Tema.Texto -- ja foi a borda do pre-requisito aceso; hoje so serve pra provar que borda
	/// nenhuma e branca) ou "apagada" (qualquer cinza escuro do tema).
	///
	/// POR DIRECAO, e nao por distancia: o serrilhado mistura a borda com o fundo escuro e ESCURECE a
	/// cor sem trocar o matiz -- o verde lido saiu #386d41, que por distancia absoluta cai no cinza
	/// `Borda` (#39405a) e por direcao cai no `Bom` (#5fc46a). As cinzas do tema tem todas a mesma
	/// direcao (azulada) e a mesma escuridao; nao ha o que separar entre elas, e nem precisa.
	/// </summary>
	private static string MaisPerto(Color c)
	{
		float topo = Math.Max(c.R, Math.Max(c.G, c.B));
		if (topo < 0.28f) return "apagada";
		Vector3 d = new Vector3(c.R, c.G, c.B) / topo;
		static Vector3 Dir(Color k) => new Vector3(k.R, k.G, k.B) / Math.Max(k.R, Math.Max(k.G, k.B));
		(string Nome, Color Cor)[] candidatas = [("Bom", Tema.Bom), ("BordaViva", Tema.BordaViva), ("branca", Tema.Texto), ("apagada", Tema.BordaApagada)];
		return candidatas.OrderBy(k => (Dir(k.Cor) - d).Length()).First().Nome;
	}

	// =====================================================================
	// O PLACAR
	// =====================================================================
	private void Placar()
	{
		Nota("==================================================================================");
		Nota($" PLACAR: {_ok} OK, {_falha} FALHA, {_pulados} NAO MEDIDO");
		Nota($" INJECAO: {_injOk} pegou, {_injFalha} PASSOU BATIDO");
		Nota($" INTRUSOES DE TECLADO (pausa aberta por tecla de fora): {_intrusoes}");
		Nota("==================================================================================");
		foreach (string r in _reprovadas) Nota("  reprovada: " + r);
		foreach (string r in _injPassouBatido) Nota("  passou batido: " + r);
		foreach (string r in _naoMedidos) Nota("  nao medido: " + r);
		if (_fotosGravadas.Count > 0) { Nota("FOTOS:"); foreach (string f in _fotosGravadas) Nota("   " + f); }
	}
}
