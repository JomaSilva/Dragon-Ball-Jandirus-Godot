using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.Verbos;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO APRENDIZ DE KAIOSHIN (`--diagaprendiz`, 2026-10-19) -- as fotos do pedido do dono (*"os verbos do Aprendiz de Kaioshin não
/// dizem nada -- conserte"*). A regra e da `--aprendizteste`; aqui e a TELA, com o robo no papel de discipulo e depois no de mestre:
///   R1. o Kaioshin (forjado, ao lado) convida: o cartao de pedido aparece -- o fato do 33o bit atravessou o fio -- foto `aprendiz-1-convite`;
///   R2. o "Aceitar" do cartao amarra o vinculo e a arvore "Aprendiz de Kaioshin" aparece na aba Learning, com a Cura -- foto
///       `aprendiz-2-arvore`;
///   R3. a Cura comprada pela arvore fica (a reconciliacao do kit de cargo nao a apaga);
///   R4. o trono do Kaioshin vaga: o aprendizado acaba, a Cura sai, os Marcos voltam e a arvore some -- foto `aprendiz-3-fim`;
///   R5. o robo vira o Kaioshin: o "Dismiss Apprentice" da aba Other lista o discipulo num cartao, e o botao dele o dispensa -- foto
///       `aprendiz-4-dispensar`.
///
///     Godot --path . --host --rede 9632 --horateste 0.5 --semfauna --diagaprendiz --raca Human --conta bancada_aprendiz --nome Novico
/// </summary>
public partial class RoboDoAprendiz : Node
{
	private const string Cura = "/datum/skill/ki/Heal";

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ditas = [];
	private int _ok;

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (ok) _ok++;
		else _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Fechar();
	}

	private void Ouvir(Protocol.Fala _, string __, string texto) => _ditas.Add(texto);

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null && MenuJogo.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu P nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || MenuJogo.Instancia is not { } menu) return;
		int eu = cli.LocalId;
		GD.Print("[daprendiz] ===== O APRENDIZ DE KAIOSHIN (2026-10-19) =====");
		cli.Falou += Ouvir;
		try
		{
			// ---- R1. o convite ----
			bool convidou = srv.PalcoApz_OKaioshinConvida(eu);
			bool chegou = await Ate(() => cli.Pedidos.Any(p => p.Fato == Fatos.OfertaDeAprendiz), 6);
			await Segundos(1.0);
			Conferir(convidou && chegou && cli.Pedidos.Any(p => p.Texto.Contains("Aprendiz de Kaioshin", StringComparison.Ordinal)),
					 $"R1. **o convite chega como cartao**: o fato do 33o bit atravessa o fio e o cartao diz quem convida [{string.Join(" | ", cli.Pedidos.Select(p => p.Texto))}]");
			Fotografar("user://aprendiz-1-convite.png");

			// ---- R2. o aceite, e a arvore ----
			_ditas.Clear();
			Button? aceitar = BotaoNoPainelDePedidos("Aceitar");
			aceitar?.EmitSignal(BaseButton.SignalName.Pressed);
			bool virou = await Ate(() => _ditas.Any(t => t.Contains("agora um Aprendiz de Kaioshin", StringComparison.Ordinal)), 5);
			await Aba(menu, "Learning");
			bool arvore = await Ate(() => CartaoDaArvore(menu, "Aprendiz de Kaioshin") != null, 5);
			if (CartaoDaArvore(menu, "Aprendiz de Kaioshin") is { } card && Todos(card).OfType<Button>().FirstOrDefault() is { } abrir)
				abrir.EmitSignal(BaseButton.SignalName.Pressed);
			bool cura = await Ate(() => menu.PaginaDeTeste("Learning") is { } pg && TemTexto(pg, "Healing Technique"), 4);
			await Segundos(0.6);
			Conferir(aceitar != null && virou && arvore && cura,
					 "R2. o \"Aceitar\" do cartao amarra o vinculo e a arvore \"Aprendiz de Kaioshin\" aparece na aba Learning, com a Cura nela");
			Fotografar("user://aprendiz-2-arvore.png");
			menu.Fechar();

			// ---- R3. a Cura comprada fica ----
			srv.PalcoApz_DarMarcos(eu, 3);
			await Segundos(0.5);
			cli.SendAprender(Cura);
			bool comprou = await Ate(() => cli.SkillsAprendidas.Contains(Cura), 5);
			await Segundos(2.5);   // dois tiques de 1 Hz dos cargos: a reconciliacao do kit passa por cima
			Conferir(comprou && cli.SkillsAprendidas.Contains(Cura) && srv.PalcoApz_Estado(eu).SabeACura,
					 "R3. a Cura comprada pela arvore do aprendiz fica -- a reconciliacao do kit de cargo nao a apaga");

			// ---- R4. o fim ----
			int marcosAntes = srv.PalcoApz_Estado(eu).Marcos;
			_ditas.Clear();
			srv.PalcoApz_OTronoVaga();
			bool acabou = await Ate(() => _ditas.Any(t => t.Contains("chegou ao fim", StringComparison.Ordinal)), 5);
			await Segundos(0.6);
			await Aba(menu, "Learning");
			await Segundos(0.6);
			bool semArvore = CartaoDaArvore(menu, "Aprendiz de Kaioshin") == null;
			var estado = srv.PalcoApz_Estado(eu);
			Conferir(acabou && !estado.Aprendiz && !estado.SabeACura && estado.Marcos > marcosAntes && semArvore,
					 $"R4. o trono do Kaioshin vaga: o aprendizado acaba, a Cura sai, os Marcos voltam ({marcosAntes} -> {estado.Marcos}) e a arvore some "
					 + $"[ouvi: {string.Join(" | ", _ditas.TakeLast(2))}]");
			Fotografar("user://aprendiz-3-fim.png");

			// (2026-10-20) o contra-exemplo da mesma regua: com o defeito da r1 injetado (a arvore anunciada a todos entre as que faltam
			// abrir), o cartao dela volta -- apagado. A pagina so remonta se a ASSINATURA dela mudar (o `Redesenhar` reaproveita a igual, e o
			// defeito nao entra nela -- a r2 voltou por isso): abrir uma arvore troca a assinatura guardada, e a volta pela outra aba remonta
			// a lista, ja com o defeito ligado.
			AprendizDeKaioshin.ArvoreAnunciadaDeTeste = true;
			bool anunciada;
			try
			{
				if (CartaoDaArvore(menu, "Strength of Body") is { } corpo && Todos(corpo).OfType<Button>().FirstOrDefault() is { } abrirCorpo)
					abrirCorpo.EmitSignal(BaseButton.SignalName.Pressed);
				await Quadros(6);
				await Aba(menu, "Skills");
				await Aba(menu, "Learning");
				await Segundos(0.3);
				anunciada = CartaoDaArvore(menu, "Aprendiz de Kaioshin") != null;
			}
			finally { AprendizDeKaioshin.ArvoreAnunciadaDeTeste = false; }
			Conferir(anunciada, "R4b. (defeito injetado: a arvore do aprendiz anunciada a todos, o jogo da r1) a mesma regua REPROVA -- o cartao apagado volta");
			menu.Fechar();

			// ---- R5. o robo e o mestre: dispensar ----
			srv.PalcoApz_ORoboEOMestre(eu);
			await Segundos(1.0);
			await Aba(menu, "Other");
			bool botao = await Ate(() => BotaoComTexto(menu, "Dismiss Apprentice") != null, 5);
			// O CARTAO NA CHEGADA DA LISTA (2026-10-20, a r4): todo verbo e seguido de um pacote de ficha, e a ficha remonta a aba Other por
			// conta propria -- com ela o cartao aparecia mesmo sem o ouvido (o mutante x11 passou na r3 e na r4, mesmo com o Kaioshin parado).
			// A prova do ouvido e a ORDEM: o robo assina o `EscolhasChegaram` DEPOIS dele, entao, quando a lista chega, o cartao ja tem de
			// estar na tela -- sem o ouvido ele nao esta, com ficha ou sem ficha.
			bool naChegada = false;
			void AoEscolhas(string verbo) { if (verbo == AprendizDeKaioshin.VerboDispensar) naChegada = BotaoDeEscolha(menu) != null; }
			cli.EscolhasChegaram += AoEscolhas;
			bool lista;
			try
			{
				BotaoComTexto(menu, "Dismiss Apprentice")?.EmitSignal(BaseButton.SignalName.Pressed);
				lista = await Ate(() => BotaoDeEscolha(menu) != null, 5);
			}
			finally { cli.EscolhasChegaram -= AoEscolhas; }
			// a foto mostra o cartao: ele mora no fim da aba Other, abaixo da dobra
			if (BotaoDeEscolha(menu) is { } escolha)
				for (Node? n = escolha.GetParent(); n != null; n = n.GetParent())
					if (n is ScrollContainer rolagem) { rolagem.EnsureControlVisible(escolha); break; }
			await Segundos(0.6);
			Fotografar("user://aprendiz-4-dispensar.png");
			_ditas.Clear();
			BotaoDeEscolha(menu)?.EmitSignal(BaseButton.SignalName.Pressed);
			bool dispensou = await Ate(() => _ditas.Any(t => t.Contains("você dispensou", StringComparison.Ordinal)), 5);
			Conferir(botao && lista && naChegada && dispensou && !srv.PalcoApz_ODiscipuloAindaEDele(eu),
					 $"R5. o \"Dismiss Apprentice\" da aba Other lista o discipulo num cartao -- ja na tela quando a lista chega (o ouvido) -- e "
					 + $"o botao dele o dispensa [na chegada: {naChegada}]");
			menu.Fechar();
		}
		finally
		{
			cli.Falou -= Ouvir;
			srv.PalcoApz_Limpar(eu);
		}
	}

	// =====================================================================
	// AS MAOS
	// =====================================================================
	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	private Button? BotaoNoPainelDePedidos(string texto) =>
		GetTree().Root.FindChild("Pedidos", true, false) is { } painel
			? Todos(painel).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && string.Equals(b.Text, texto, StringComparison.OrdinalIgnoreCase))
			: null;

	private static PanelContainer? CartaoDaArvore(MenuJogo menu, string nome) =>
		menu.PaginaDeTeste("Learning") is { } pg
			? Todos(pg).OfType<PanelContainer>().FirstOrDefault(x => !x.IsQueuedForDeletion() && x.HasMeta("cartao")
				  && x.GetMeta("cartao").AsString() == "arvore" && Todos(x).OfType<Label>().Any(l => l.Text == nome))
			: null;

	private static Button? BotaoComTexto(Node raiz, string texto) =>
		Todos(raiz).OfType<Button>().FirstOrDefault(b => !b.IsQueuedForDeletion() && b.IsVisibleInTree()
			&& string.Equals(b.Text, texto, StringComparison.OrdinalIgnoreCase));

	private static Button? BotaoDeEscolha(MenuJogo menu) =>
		menu.PaginaDeTeste("Other") is { } pg
			? Todos(pg).OfType<Button>().FirstOrDefault(b => !b.IsQueuedForDeletion() && b.IsVisibleInTree() && b.HasMeta("aprendiz_escolha"))
			: null;

	private static bool TemTexto(Node raiz, string trecho) =>
		Todos(raiz).OfType<Label>().Any(l => !l.IsQueuedForDeletion() && l.Text.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private async Tarefa Aba(MenuJogo menu, string aba)
	{
		if (!menu.Visible) menu.Abrir();
		await Quadros(3);
		Button? b = Todos(menu).OfType<Button>().FirstOrDefault(x => x.IsVisibleInTree() && string.Equals(x.Text, aba, StringComparison.OrdinalIgnoreCase));
		b?.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(6);
	}

	private void Fotografar(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath(destino);
		img.SavePng(caminho);
		_linhas.Add("  --     foto salva em " + caminho);
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

	private void Fechar()
	{
		foreach (string l in _linhas) GD.Print("[daprendiz] " + l);
		GD.Print($"[daprendiz] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0 ? "[daprendiz] ===== TUDO OK =====" : $"[daprendiz] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[daprendiz]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
