using Godot;
using Jandirus.Core.Forms;
using Servidor = Jandirus.Server.GameServer;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO SUPER MAJIN (`--diagsupermajin`, 2026-10-23). O dono: *"dar a forma super majin melhorada pros majins q nao sao corrupted"*, com
/// a maestria travada pelo BP base (6x no nivel do SSJ1, 10x no do SSJ2, 100% = 16x no do SSJ3), e o visual escolhido por ele: *"Corpo de
/// sempre + aura rosa"* com as faiscas do `Electric_Majin`. Com janela e `--host`, um Majin; o palco (`GameServer.SuperMajinPalco.cs`) poe
/// a classe, o BP base, a chama pessoal e roda o TIQUE DE PRODUCAO da forma -- quem transforma e o C do proprio cliente, pelo fio.
///
///   S0. A PREMISSA: Majin de classe Majin no nivel do SSJ1 (2 M de BP base), na base -- foto `smajin-0-base`. E o CONTROLE da chama: com o Ki
///       acima de 100% a chama da base e a PESSOAL (cravada azul) -- foto `smajin-0b-base-chama`.
///   S1. O C, PELO FIO: o Super Majin. O CORPO DE SEMPRE (a mesma folha e a mesma tinta da base, sem corpo de forma por cima) e as FAISCAS --
///       (2026-10-28, o pedido 145: *"troque elas pf pra faiscas vermelhas de shaders (igual o do ssj4 fp)"*) os RAIOS DE SHADER, vermelhos
///       como os do Limit Breaker, e nenhuma camada colada (a `Electric_Majin` de sprite saiu) -- foto `smajin-1-super-majin` (com uma rajada).
///   S2. A AURA ROSA: com o Ki acima de 100% a chama do Super Majin e ROSA, no material e na foto -- foto `smajin-2-aura-rosa`.
///   S3. A ABA FORMS: o cartao "Super Majin" EM USO, a barra no comeco, o estagio x2 e a nota da trava -- foto `smajin-3-aba-forms`.
///   S4. O TETO DO NIVEL DO SSJ1: um tique de producao de 90 s a partir de 29,98% para a barra em 30% e o estagio em x6, e o servidor avisa --
///       fotos `smajin-4-teto-ssj1` (a aba) e `smajin-4b-aviso` (o chat). CONTRA-EXEMPLO `SuperMajin.SemTravaDeTeste`: o mesmo tique passa dos
///       30% e a aba mostra outro numero -- a regua REPROVA. (90 s e nao 60: sem a trava, 60 s levam a barra a 30,04%, que a aba escreve "30%"
///       -- o `0.#` dela -- e a metade da regua que le a TELA nao veria o defeito; e o dreno de 90 s, 72% do Ki, ainda nao derruba a forma.)
///   S5. O NIVEL DO SSJ2 (600 M): a barra vai a 60% (x10) -- foto `smajin-5-teto-ssj2`.
///   S6. O NIVEL DO SSJ3 (2,5 B): 100% = x16, com o "forma DOMINADA" -- foto `smajin-6-dominada`.
///
/// OS NUMEROS DO DONO (30%, 60%, 100%, x6, x10, x16) ESTAO ESCRITOS AQUI: uma regua que lesse as constantes do Core aprovaria o mutante.
///
///     Godot --janela --resolution 1280x720 --host --rede 10871 --campoteste 8 --horateste 0.5 --semfauna --diagsupermajin --raca Majin --conta bancada_smajin --nome Bubu
/// </summary>
public partial class RoboDoSuperMajin : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private int _ok;
	private int _eu;

	private const string Id = "super_majin";
	private const ushort Rede = 870;
	private const double BpSsj1 = 2_000_000, BpSsj2 = 600_000_000, BpSsj3 = 2_500_000_000;

	private static GameClient? C => GameClient.Instance;
	private static Servidor? S => Servidor.Instance;

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		_linhas.Add((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? "   " + detalhe : ""));
		if (passou) _ok++;
		else _falhas.Add(oque);
	}

	private void Nota(string texto) => _linhas.Add("  --     " + texto);

	public override void _Ready() => _ = Rodar();

	/// <summary>O defeito injetado nunca sobrevive ao robo, nem se ele cair no meio.</summary>
	public override void _ExitTree() => SuperMajin.SemTravaDeTeste = false;

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Checa("o roteiro correu ate o fim sem excecao", false, $"{e.GetType().Name}: {e.Message}"); GD.Print(e.ToString()); }
		SuperMajin.SemTravaDeTeste = false;
		Fechar();
	}

	private static CharacterVisual? VisualDe(int id) => World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	private static CargaVisual? CargaDe(int id) => World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<CargaVisual>("Carga");

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && S != null && World.Instancia != null && MenuJogo.Instancia != null, 90);
		Checa("(montagem) em 90 s o cliente conectou, o servidor e deste processo e o mundo e o menu P nasceram", pronto);
		if (!pronto || C is not { } cli || S is not { } srv || MenuJogo.Instancia is not { } menu) return;
		int eu = _eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dsupermajin] ===== O SUPER MAJIN (dono, 2026-10-23) =====");

		// ---------------------------------------------------------- S0. a premissa e o controle da chama
		Nota("palco: " + srv.PalcoSM_Preparar(eu, BpSsj1));
		bool base0 = await Ate(() => cli.Atributos.Classe == "Majin" && cli.Atributos.FormaAtual == 0 && VisualDe(eu) != null, 10);
		await Segundos(2.0);   // o ceu limpa e o corpo assenta
		CharacterVisual? vis = VisualDe(eu);
		string folhaDaBase = vis?.FolhaDoCorpoDeTeste ?? "";
		var tintaDaBase = vis?.TintaDoCorpoDeTeste;
		Checa("S0. (premissa) o robo e um Majin de classe Majin, na base, com o corpo dele na tela",
			  base0 && vis != null && folhaDaBase.Length > 0, $"[{srv.PalcoSM_Ficha(eu)} | corpo {folhaDaBase.GetFile()}]");
		if (!base0 || vis == null) return;
		await Fotografar("smajin-0-base");

		var (azulBase, rosaBase) = await AChamaAcesa("smajin-0b-base-chama", vis);
		Color? corDaBase = CargaDe(eu)?.DesenhoDeTeste.CorNoMaterialDeTeste;
		Checa("S0b. (controle) com o Ki acima de 100% a chama da BASE e a PESSOAL, azul -- no material e na foto",
			  corDaBase is { } cb && cb.B >= 0.85f && cb.R <= 0.45f && azulBase > rosaBase,
			  $"[material #{corDaBase?.ToHtml(false)} | pixels que mudaram: azulados {azulBase}, rosados {rosaBase}]");

		// ---------------------------------------------------------- S1. o C, pelo fio
		cli.SendTransformar(true);
		bool virou = await Ate(() => srv.FormaDoCorpoDeTeste(eu) == Id && !srv.EmCenaDeTeste(eu) && cli.Atributos.FormaAtual == Rede, 20);
		await Segundos(3.5);   // a poeira da cena assenta antes da foto
		vis = VisualDe(eu);
		(string Folha, Color Tinta)[] coladas = vis?.ColadasNoCorpoDeTeste ?? [];
		Checa("S1. **O C VIRA SUPER MAJIN** (pelo fio, no nivel do SSJ1)", virou, $"[{srv.PalcoSM_Ficha(eu)}]");
		Checa("S1c. **O CORPO DE SEMPRE**: a mesma folha e a mesma tinta da base, e nenhum corpo de forma por cima",
			  vis != null && vis.FolhaDoCorpoDeTeste == folhaDaBase && !vis.CorpoDaFormaDeTeste && Equals(vis.TintaDoCorpoDeTeste, tintaDaBase),
			  $"[folha {vis?.FolhaDoCorpoDeTeste.GetFile()} (base {folhaDaBase.GetFile()}), corpo de forma {vis?.CorpoDaFormaDeTeste}, tinta "
			  + $"{vis?.TintaDoCorpoDeTeste} (base {tintaDaBase})]");
		// (2026-10-28, o pedido 145) AS FAISCAS SAO OS RAIOS DE SHADER: o node `Raios` do corpo, aceso no CHEIO (2, o do Limit Breaker) e no
		// VERMELHO dele -- `#ff2d2f`, escrito aqui pelos canais e nao lido do Core (a regua que le a constante aprovaria o mutante) --, e
		// nenhuma camada colada no corpo: a `Electric_Majin` de sprite saiu. A cor e lida do UNIFORM do shader (`CorDeTeste`), que e o que pinta.
		RaiosDaForma? raios = World.Instancia?.CorpoDeTeste(eu)?.GetNodeOrNull<RaiosDaForma>("Raios");
		Color corDosRaios = raios?.CorDeTeste ?? default;
		bool vermelho = corDosRaios.R >= 0.95f && corDosRaios.G <= 0.25f && corDosRaios.B <= 0.25f;
		Checa("S1f. **AS FAISCAS SAO OS RAIOS DE SHADER, VERMELHOS** (*\"troque elas pf pra faiscas vermelhas de shaders (igual o do ssj4 fp)\"*): "
			  + "os raios acesos no cheio (2) e no vermelho #ff2d2f, e nenhuma camada colada no corpo (a `Electric_Majin` de sprite saiu)",
			  coladas.Length == 0 && raios is { IntensidadeDeTeste: 2 } && vermelho,
			  $"[coladas: {coladas.Length} ({string.Join(", ", coladas.Select(c => c.Folha.GetFile()))}); raios: "
			  + $"{(raios == null ? "sem node" : $"intensidade {raios.IntensidadeDeTeste}, cor #{corDosRaios.ToHtml(false)}")}]");
		if (!virou || vis == null || raios == null) return;

		// E ELES ESTALAM: o relogio do node solta uma rajada a cada 1,3-2,7 s, entao em 4 s sai pelo menos uma sozinha. A foto sai com uma
		// rajada forcada (`DispararDeTeste`) -- e o retrato pro dono, nao a regua.
		int rajadasAntes = raios.RajadasDeTeste;
		await Segundos(4.0);
		int rajadas = raios.RajadasDeTeste - rajadasAntes;
		raios.DispararDeTeste();
		await Quadros(2);
		await Fotografar("smajin-1-super-majin");
		Checa("S1t. ...e ESTALAM SOZINHOS: em 4 s o corpo solta rajadas de raio pelo relogio do node", rajadas >= 1, $"[{rajadas} rajada(s) em 4 s]");

		// ---------------------------------------------------------- S2. a aura rosa
		var (azulSm, rosaSm) = await AChamaAcesa("smajin-2-aura-rosa", vis);
		Color? corSm = CargaDe(eu)?.DesenhoDeTeste.CorNoMaterialDeTeste;
		Checa("S2. **A AURA ROSA**: com o Ki acima de 100% a chama do Super Majin e ROSA -- vermelho alto, azul no meio, verde embaixo -- no "
			  + "material e na foto (e nao a azul pessoal do S0b)",
			  corSm is { } cs && cs.R >= 0.85f && cs.B >= 0.6f && cs.G <= 0.55f && rosaSm > azulSm && rosaSm >= 200,
			  $"[material #{corSm?.ToHtml(false)} | pixels que mudaram: rosados {rosaSm}, azulados {azulSm}]");

		// ---------------------------------------------------------- S3. a aba Forms
		await AbrirAAbaForms(menu);
		(string maestria, string estagio, bool emUso, bool nota) = LerOCartao(menu);
		Checa("S3. **A ABA FORMS** mostra o cartao \"Super Majin\" EM USO, com a barra no comeco, o estagio x2 e a nota da trava pelo poder",
			  emUso && maestria == "0%" && estagio == "×2" && nota, $"[maestria \"{maestria}\", estagio \"{estagio}\", em uso {emUso}, nota {nota}]");
		await FotografarACarta(menu, "smajin-3-aba-forms");

		// ---------------------------------------------------------- S4. o teto do nivel do SSJ1
		var t1 = srv.PalcoSM_Tique(eu, 29.98, BpSsj1, 90);
		(maestria, estagio) = await AbaDepoisDoTique(cli, menu, t1.Maestria);
		Checa("S4. **NO NIVEL DO SSJ1 A BARRA PARA EM 30% (x6)**: um tique de producao de 90 s a partir de 29,98% (que passaria do teto) para "
			  + "cravado nela, e a aba mostra",
			  Math.Abs(t1.Maestria - 30) < 1e-9 && Math.Abs(t1.Mult - 6) < 1e-9 && maestria == "30%" && estagio == "×6",
			  $"[servidor {t1.Maestria:0.######}% x{t1.Mult:0.##} | aba \"{maestria}\" \"{estagio}\"]");
		Checa("S4a. ...e o servidor avisa o jogador que a maestria parou no teto do poder",
			  t1.Falas.Contains("Super Majin: a maestria parou em 30%", StringComparison.Ordinal), $"[{t1.Falas}]");
		await FotografarACarta(menu, "smajin-4-teto-ssj1");
		menu.Fechar();
		await Segundos(0.5);
		await Fotografar("smajin-4b-aviso");

		SuperMajin.SemTravaDeTeste = true;
		try
		{
			var solto = srv.PalcoSM_Tique(eu, 29.98, BpSsj1, 90);
			await AbrirAAbaForms(menu);
			(string m2, _) = await AbaDepoisDoTique(cli, menu, solto.Maestria);
			Checa("CONTRA-EXEMPLO (defeito injetado: a maestria sem o teto do poder): o mesmo tique passa dos 30% e a aba mostra outro numero -- a "
				  + "regua do S4 REPROVA", solto.Maestria > 30 + 1e-6 && m2 != "30%", $"[servidor {solto.Maestria:0.####}% | aba \"{m2}\"]");
		}
		finally { SuperMajin.SemTravaDeTeste = false; }

		// ---------------------------------------------------------- S5. o nivel do SSJ2
		var t2 = srv.PalcoSM_Tique(eu, 59.98, BpSsj2, 60);
		(maestria, estagio) = await AbaDepoisDoTique(cli, menu, t2.Maestria);
		Checa("S5. **NO NIVEL DO SSJ2 (600 M) ELA VAI A 60% (x10)** e para la",
			  Math.Abs(t2.Maestria - 60) < 1e-9 && Math.Abs(t2.Mult - 10) < 1e-9 && maestria == "60%" && estagio == "×10",
			  $"[servidor {t2.Maestria:0.######}% x{t2.Mult:0.##} | aba \"{maestria}\" \"{estagio}\" | {t2.Falas}]");
		await FotografarACarta(menu, "smajin-5-teto-ssj2");

		// ---------------------------------------------------------- S6. o nivel do SSJ3
		var t3 = srv.PalcoSM_Tique(eu, 99.98, BpSsj3, 60);
		(maestria, estagio) = await AbaDepoisDoTique(cli, menu, t3.Maestria);
		Checa("S6. **NO NIVEL DO SSJ3 (2,5 B) ELA CHEGA A 100% = x16**, com o \"forma DOMINADA\"",
			  t3.Maestria >= 100 && Math.Abs(t3.Mult - 16) < 1e-9 && maestria == "100%" && estagio == "×16" && t3.Falas.Contains("forma DOMINADA", StringComparison.Ordinal),
			  $"[servidor {t3.Maestria:0.####}% x{t3.Mult:0.##} | aba \"{maestria}\" \"{estagio}\" | {t3.Falas}]");
		await FotografarACarta(menu, "smajin-6-dominada");
		menu.Fechar();
	}

	// =====================================================================
	// A CHAMA
	// =====================================================================
	/// <summary>
	/// A CHAMA PELO KI ACIMA DE 100% (o caminho de producao: o servidor acende pelo tique da carga): uma foto apagada, a sobrecarga, a foto
	/// acesa, e o Ki de volta. Conta os pixels que MUDARAM em volta do corpo (fora da caixa dele, onde a faisca estala) e viraram azulados ou
	/// rosados -- a comparacao e entre as duas cores, nao contra um numero de exposicao.
	/// </summary>
	private async System.Threading.Tasks.Task<(int Azul, int Rosa)> AChamaAcesa(string foto, CharacterVisual vis)
	{
		S!.PalcoSM_Sobrecarga(_eu, false);
		await Ate(() => CargaDe(_eu) is { ExcessoDeTeste: false }, 5);
		await Segundos(1.0);
		Image? apagada = await Fotografar(foto + "-apagada");
		S.PalcoSM_Sobrecarga(_eu, true);
		bool acendeu = await Ate(() => CargaDe(_eu) is { ExcessoDeTeste: true }, 5);
		await Segundos(1.2);
		Image? acesa = await Fotografar(foto);
		Nota($"a chama acendeu pelo Ki acima de 100%: {acendeu}");
		int azul = 0, rosa = 0;
		if (apagada != null && acesa != null)
		{
			Rect2 fora = CaixaNaTela(vis, 70), dentro = CaixaNaTela(vis, 10);
			azul = Contar(acesa, apagada, fora, EhAzulado, dentro);
			rosa = Contar(acesa, apagada, fora, EhRosado, dentro);
		}
		S.PalcoSM_Sobrecarga(_eu, false);
		await Ate(() => CargaDe(_eu) is { ExcessoDeTeste: false }, 5);
		return (azul, rosa);
	}

	private static bool EhAzulado(Color c) => c.B - c.R >= 0.12f && c.B >= 0.45f;
	private static bool EhRosado(Color c) => c.R - c.G >= 0.12f && c.B - c.G >= -0.02f && c.R >= 0.45f;

	/// <summary>
	/// A CAIXA DO CORPO NA FOTO, alargada <paramref name="folga"/> pixels do QUADRO pra cada lado: o quadro do corpo vira tela pelo transform
	/// do proprio boneco (`GetGlobalTransformWithCanvas`, a conta da regua de camada).
	/// </summary>
	private static Rect2 CaixaNaTela(CharacterVisual vis, float folga)
	{
		Rect2 local = (vis.CaixaDoCorpoDeTeste ?? new Rect2(-8, -16, 16, 32)).Grow(folga);
		Transform2D t = vis.GetGlobalTransformWithCanvas();
		Vector2 a = t * local.Position, b = t * local.End;
		return new Rect2(new Vector2(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y)), (b - a).Abs());
	}

	/// <summary>
	/// QUANTOS PIXELS DA CAIXA passam no teste -- com uma foto de antes, so os que MUDARAM (algum canal andou 0,08 ou mais); com uma caixa
	/// <paramref name="semEsta"/>, fora dela.
	/// </summary>
	private static int Contar(Image foto, Image? antes, Rect2 caixa, Func<Color, bool> teste, Rect2? semEsta = null)
	{
		int x0 = Math.Max(0, (int)caixa.Position.X), y0 = Math.Max(0, (int)caixa.Position.Y);
		int x1 = Math.Min(foto.GetWidth(), (int)caixa.End.X), y1 = Math.Min(foto.GetHeight(), (int)caixa.End.Y);
		int n = 0;
		for (int y = y0; y < y1; y++)
			for (int x = x0; x < x1; x++)
			{
				if (semEsta is { } s && s.HasPoint(new Vector2(x, y))) continue;
				Color c = foto.GetPixel(x, y);
				if (antes != null)
				{
					Color a = antes.GetPixel(x, y);
					if (Mathf.Max(Mathf.Abs(c.R - a.R), Mathf.Max(Mathf.Abs(c.G - a.G), Mathf.Abs(c.B - a.B))) < 0.08f) continue;
				}
				if (teste(c)) n++;
			}
		return n;
	}

	// =====================================================================
	// A ABA FORMS
	// =====================================================================
	private async Tarefa AbrirAAbaForms(MenuJogo menu)
	{
		menu.Abrir();
		menu.IrPara("Forms");
		await Quadros(3);
		menu.ForcarRedesenho();
		await Quadros(3);
	}

	/// <summary>Espera a ficha lenta trazer a maestria do servidor e le o cartao de novo.</summary>
	private async System.Threading.Tasks.Task<(string Maestria, string Estagio)> AbaDepoisDoTique(GameClient cli, MenuJogo menu, double doServidor)
	{
		bool chegou = await Ate(() => (cli.Atributos.Maestrias ?? []).Any(m => m.Forma == Rede && Math.Abs(m.Pct - doServidor) < 1e-3), 6);
		if (!chegou) Nota($"a ficha lenta nao trouxe a maestria {doServidor:0.####}% em 6 s");
		await AbrirAAbaForms(menu);
		(string maestria, string estagio, _, _) = LerOCartao(menu);
		return (maestria, estagio);
	}

	/// <summary>O cartao "Super Majin" da aba Forms (o `MarcarCartaoDeItem` poe o nome no metadado `titulo`).</summary>
	private static PanelContainer? Cartao(MenuJogo menu) =>
		menu.PaginaDeTeste("Forms") is { } pg
			? Todos(pg).OfType<PanelContainer>().FirstOrDefault(c => c.HasMeta("titulo") && c.GetMeta("titulo").AsString() == "Super Majin"
																	  && c.IsVisibleInTree())
			: null;

	/// <summary>A maestria e o estagio (o valor ao lado de cada rotulo), a pilula EM USO e a nota da trava.</summary>
	private static (string Maestria, string Estagio, bool EmUso, bool Nota) LerOCartao(MenuJogo menu)
	{
		if (Cartao(menu) is not { } c) return ("(sem cartao)", "", false, false);
		string Valor(string rotulo) => Todos(c).OfType<HBoxContainer>()
			.Where(h => h.GetChildCount() >= 2 && h.GetChild(0) is Label { } a && a.Text == rotulo && h.GetChild(1) is Label)
			.Select(h => h.GetChild<Label>(1).Text).FirstOrDefault() ?? "";
		bool emUso = Todos(c).OfType<PanelContainer>().Any(p => p.HasMeta("pilula") && p.GetMeta("pilula").AsString() == "EM USO");
		bool nota = Todos(c).OfType<Label>().Any(l => l.Text == SuperMajin.NotaDaTrava);
		return (Valor("maestria"), Valor("estágio"), emUso, nota);
	}

	/// <summary>A arvore inteira, SEM os nos que ja vao embora (a pagina velha fica na arvore ate o fim do quadro da remontagem).</summary>
	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (f.IsQueuedForDeletion()) continue;
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	private async Tarefa FotografarACarta(MenuJogo menu, string nome)
	{
		if (Cartao(menu) is { } c && Ancestral<ScrollContainer>(c) is { } rol) rol.EnsureControlVisible(c);
		await Fotografar(nome);
	}

	// =====================================================================
	// A FOTO E O RELOGIO
	// =====================================================================
	private async System.Threading.Tasks.Task<Image?> Fotografar(string nome)
	{
		await Quadros(3);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"sem foto `{nome}` (headless nao renderiza)"); return null; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		Nota("foto salva em " + caminho);
		return img;
	}

	private static T? Ancestral<T>(Node n) where T : Node
	{
		for (Node? p = n.GetParent(); p != null; p = p.GetParent())
			if (p is T t) return t;
		return null;
	}

	private static double Agora => Time.GetTicksMsec() / 1000.0;

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Agora + s;
		while (Agora < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Agora + segundos;
		while (Agora < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}

	private void Fechar()
	{
		SuperMajin.SemTravaDeTeste = false;
		foreach (string l in _linhas) GD.Print("[dsupermajin] " + l);
		GD.Print($"[dsupermajin] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0 ? "[dsupermajin] ===== TUDO OK =====" : $"[dsupermajin] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dsupermajin]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
