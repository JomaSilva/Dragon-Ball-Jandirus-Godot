using Godot;
using Jandirus.Core.Races;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO FILHO (`--diagfilho`, 2026-10-17). O dono: *"quando alguem for ter um filho, o proximo jogador q for entrar criar um
/// personagem mesmo q seja no modo de raça random, vai aparecer uma mensagem perguntando se ele quer ser o filho do jogador x e y e
/// assim nascendo com 0 anos, ele claro escolhe o cabelo etc"*.
///
/// No molde da `--diagapagar`: o robo sobe o servidor deste processo, poe um bebe na fila (Goku e Chichi, um meio-Saiyajin -- a
/// concepcao tem a bancada dela, a `--filhosteste`), LOGA, monta a `CharacterSelect` de producao com a lista que chegou e APERTA os
/// botoes -- o mesmo `Pressed` do dedo do jogador:
///
///   1. a oferta chega no fim da lista de slots;
///   2. "Criar personagem" abre a PERGUNTA, e nao a criacao (foto `filho-1-pergunta.png`); "Criar outro personagem" leva a criacao
///      comum; sem bebe a caminho, "Criar personagem" vai direto (o controle);
///   2b. (a resposta do dono, 2026-10-17) O OVO: a pergunta de um ovo de Namekuseijin diz "UM OVO VAI CHOCAR" e quem o pos (foto
///      `filho-4-ovo.png`), e a criacao dele vem sem a etapa de linhagem (o cla de quem o pos, o unico) e com o anuncio do ovo (foto
///      `filho-5-ovo-identidade.png`) -- sem o parto, que e da `--filhosteste`;
///   3. "Nascer como filho(a)" abre a CRIACAO DO FILHO: sem mundo e sem raca, na linhagem (as tres do meio-Saiyajin; foto
///      `filho-2-linhagem.png`), com a idade em ZERO e travada (foto `filho-3-identidade.png`);
///   4. o "Entrar no mundo" da ultima etapa manda a ficha, e o servidor faz nascer o filho: Half-Saiyan, zero anos, a linhagem escolhida, o
///      potencial dos pais -- e o bebe sai da fila.
///
///     Godot --path . --diagfilho --position 1920,0 --resolution 1280x720 --rede 8449
/// Sem janela nao ha foto, mas as medidas saem.
/// </summary>
public partial class RoboDoFilho : Node
{
	private const string Conta = "diagfilho";
	private const string Senha = "diagfilho";

	private static GameClient? C => GameClient.Instance;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _fotos = [];

	private List<SlotInfo> _slots = [];
	private bool _naSelecao, _noMundo;
	private readonly List<string> _recusas = [];
	private CharacterSelect? _tela;
	private CreationScreen? _criacao;
	private int? _criarComum;
	private (int Slot, OfertaDeBebe Bebe)? _comoFilho;
	private IEnumerator<double>? _roteiro;
	private double _espera = 0.5;

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		_linhas.Add((passou ? "  ok    " : "  FALHA ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (!passou) _falhas.Add(oque);
	}

	public override void _Ready()
	{
		if (Jandirus.Server.GameServer.Instance is { } srv && !srv.Running && !srv.Start(Porta()))
		{
			GD.PushError("[dfilho] nao consegui abrir a porta -- ha outra bancada viva?");
			return;
		}
		if (C is not { } cli) return;
		cli.SlotsRecebidos += AoReceberSlots;
		cli.Rejected += motivo => _recusas.Add(motivo);
		cli.Joined += (_, _, _, _) => _noMundo = true;
		_roteiro = Roteiro().GetEnumerator();
	}

	public override void _ExitTree()
	{
		if (C is { } cli) cli.SlotsRecebidos -= AoReceberSlots;
	}

	private static int Porta()
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, "--rede");
		return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out int p) && p > 0 ? p : Protocol.DefaultPort;
	}

	private void AoReceberSlots(List<SlotInfo> slots)
	{
		_slots = slots;
		_naSelecao = true;
	}

	public override void _Process(double delta)
	{
		if (_roteiro == null) return;
		_espera -= delta;
		if (_espera > 0) return;
		if (!_roteiro.MoveNext()) { _roteiro = null; return; }
		_espera = _roteiro.Current;
	}

	private static IEnumerable<T> Todos<T>(Node raiz) where T : Node
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (f is T t) yield return t;
			foreach (T n in Todos<T>(f)) yield return n;
		}
	}

	private static Button? Botao(Node raiz, string texto) => Todos<Button>(raiz).FirstOrDefault(b => b.Text == texto && b.IsVisibleInTree());

	/// <summary>O cartao largo da criacao (`CartaoLargo`): o texto dele e "titulo\ndescricao", e ele se acha pelo titulo.</summary>
	private static Button? Cartao(Node raiz, string titulo) =>
		Todos<Button>(raiz).FirstOrDefault(b => b.Text.Split('\n')[0] == titulo && b.IsVisibleInTree());

	private static void Apertar(Button? b) => b?.EmitSignal(BaseButton.SignalName.Pressed);

	private void Fotografar(string nome)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
			string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
			img.SavePng(caminho);
			_fotos.Add(caminho);
		}
		catch (Exception e) { _linhas.Add("  --     sem foto: " + e.Message); }
	}

	private IEnumerable<double> Conectar()
	{
		_naSelecao = false;
		C?.Conectar("127.0.0.1", Porta(), Conta, Senha);
		for (int i = 0; i < 300 && !_naSelecao; i++) yield return 0.05;
	}

	private IEnumerable<double> Roteiro()
	{
		GD.Print("[dfilho] ===== A PERGUNTA DO BEBE E A CRIACAO DO FILHO (dono, 2026-10-17) =====");
		if (Jandirus.Server.GameServer.Instance is not { } srv) { Checa("o servidor deste processo existe", false); foreach (double d in Fim()) yield return d; yield break; }

		// ---- 0) O BEBE NA FILA, e a conta limpa ----
		int id = srv.PorUmBebeDeTeste("Goku", "Chichi", "Saiyan", "Human", 400_000, 24_000);
		foreach (double d in Conectar()) yield return d;
		Checa("a conta abriu e a lista de slots chegou", _naSelecao);
		for (int i = 0; i < _slots.Count; i++)
			if (_slots[i].Ocupado) C?.SendDeleteChar(i, _slots[i].Nome);
		yield return 0.8;
		Checa("a conta comeca vazia", _slots.All(s => !s.Ocupado), $"{_slots.Count(s => s.Ocupado)} ocupado(s)");
		Checa("1. A OFERTA CHEGA no fim da lista de slots: o bebe de Goku e Chichi, Half-Saiyan, com as tres linhagens",
			  C?.BebeACaminho is { } of && of.Id == id && of.Pai == "Goku" && of.Mae == "Chichi" && of.Raca == Filhos.MeioSaiyajin && of.Linhagens.Length == 3,
			  C?.BebeACaminho is { } o ? $"#{o.Id} {o.Pai} e {o.Mae}, {o.Raca}" : "sem oferta");

		// ---- 2) A SELECAO DE PRODUCAO, com e sem bebe ----
		_tela = new CharacterSelect { Name = "SelecaoDoFilho" };
		GetTree().Root.AddChild(_tela);
		_tela.Criar += slot => _criarComum = slot;
		_tela.CriarComoFilho += (slot, bebe) => _comoFilho = (slot, bebe);
		_tela.Bebe = null;
		_tela.Mostrar(_slots);
		yield return 0.5;
		Apertar(Botao(_tela, "Criar personagem"));
		yield return 0.3;
		Checa("(controle) SEM BEBE A CAMINHO, 'Criar personagem' vai direto pra criacao -- pergunta nenhuma",
			  _criarComum == 0 && !_tela.PerguntaDoBebeAbertaDeTeste, $"criar comum: {_criarComum?.ToString() ?? "nada"}");

		_criarComum = null;
		_tela.Bebe = C?.BebeACaminho;
		_tela.Mostrar(_slots);
		yield return 0.4;
		Apertar(Botao(_tela, "Criar personagem"));
		yield return 0.5;
		Checa("2. COM O BEBE, 'Criar personagem' abre a PERGUNTA -- e nao a criacao",
			  _tela.PerguntaDoBebeAbertaDeTeste && _criarComum == null && _comoFilho == null);
		Fotografar("filho-1-pergunta");
		Apertar(_tela.BotaoDaPerguntaDeTeste("Criar outro personagem"));
		yield return 0.3;
		Checa("...'Criar outro personagem' fecha a pergunta e leva a criacao COMUM", _criarComum == 0 && !_tela.PerguntaDoBebeAbertaDeTeste && _comoFilho == null);

		// ---- 2b) O OVO (a resposta do dono, 2026-10-17): a pergunta e a criacao de quem nasce de um ovo -- sem o parto ----
		// A TELA DE CRIACAO E UMA SO, a de producao reaberta: o "Entrar no mundo" manda a ficha pela vaga da escolha da vez.
		_criacao = new CreationScreen { Name = "CriacaoDoFilho", Visible = false };
		GetTree().Root.AddChild(_criacao);
		_criacao.Pronto += (ficha, visual) => { if (_comoFilho is { } e) C?.CriarPersonagem(e.Slot, ficha, visual); };
		yield return 0.4;
		OfertaDeBebe ovo = srv.PorUmOvoDeTeste("Piccolo", "Namekian", "Warrior clan", 400_000);
		_comoFilho = null;
		_tela.Bebe = ovo;
		_tela.Mostrar(_slots);
		yield return 0.4;
		Apertar(Botao(_tela, "Criar personagem"));
		yield return 0.5;
		string pergunta = _tela.TextoDaPerguntaDeTeste;
		Checa("2b. O OVO: a pergunta diz 'UM OVO VAI CHOCAR', quem o pos (Piccolo, sozinho) e que ele PÔS um ovo",
			  pergunta.Contains("UM OVO VAI CHOCAR") && pergunta.Contains("Piccolo") && !pergunta.Contains(" e Piccolo") && pergunta.Contains("pôs um ovo"),
			  pergunta.Replace("\n", " | "));
		Fotografar("filho-4-ovo");
		Apertar(_tela.BotaoDaPerguntaDeTeste("Nascer como filho(a)"));
		yield return 0.3;
		_tela.Visible = false;
		if (_comoFilho is { } doOvo) _criacao.ReabrirComoFilho(doOvo.Bebe);
		yield return 0.5;
		Checa("...e a criacao de quem nasce do ovo: Namekuseijin, o cla de quem o pos ja na ficha (Warrior clan) -- sem a etapa de linhagem, que nao tem escolha --, e o anuncio fala do ovo",
			  _comoFilho is { } x && x.Bebe.Id == ovo.Id && _criacao.FichaDeTeste.Raca == "Namekian" && _criacao.FilhoDeTeste.Linhagem == "Warrior clan"
			  && _criacao.TituloDaEtapaDeTeste != "LINHAGEM" && _criacao.AnuncioDeTeste.Contains("do ovo de Piccolo"),
			  $"etapa '{_criacao.TituloDaEtapaDeTeste}', {_criacao.FichaDeTeste}, '{_criacao.AnuncioDeTeste}'");
		for (int i = 0; i < 14 && _criacao.TituloDaEtapaDeTeste != "QUEM É VOCÊ?"; i++)
		{
			Apertar(Botao(_criacao, "Avançar"));
			yield return 0.25;
		}
		Checa("...ate 'QUEM É VOCÊ?', com a idade ZERO travada e a ficha levando o ovo",
			  _criacao.TituloDaEtapaDeTeste == "QUEM É VOCÊ?" && _criacao.FilhoDeTeste is { Idade: 0, IdadeTravada: true } t0 && t0.FilhoDe == ovo.Id,
			  $"etapa '{_criacao.TituloDaEtapaDeTeste}', {_criacao.FilhoDeTeste}");
		Fotografar("filho-5-ovo-identidade");
		_criacao.Visible = false;
		srv.TirarOBebeDaFilaDeTeste(ovo.Id);   // o parto do ovo e da bancada de servidor (`--filhosteste` G); aqui so a tela
		_comoFilho = null;
		_tela.Bebe = C?.BebeACaminho;
		_tela.Visible = true;
		_tela.Mostrar(_slots);
		yield return 0.4;

		Apertar(Botao(_tela, "Criar personagem"));
		yield return 0.4;
		Apertar(_tela.BotaoDaPerguntaDeTeste("Nascer como filho(a)"));
		yield return 0.3;
		Checa("3. 'Nascer como filho(a)' fecha a pergunta e pede a criacao DO FILHO, no slot certo e com o bebe certo",
			  _comoFilho is { Slot: 0 } f && f.Bebe.Id == id && !_tela.PerguntaDoBebeAbertaDeTeste);
		if (_comoFilho is not { } escolha) { foreach (double d in Fim()) yield return d; yield break; }

		// ---- 3) A CRIACAO DO FILHO ----
		_tela.Visible = false;
		_criacao.ReabrirComoFilho(escolha.Bebe);
		yield return 0.5;
		Checa("...ela abre na LINHAGEM -- sem as etapas de mundo e de raca -- e a ficha ja e Half-Saiyan",
			  _criacao.TituloDaEtapaDeTeste == "LINHAGEM" && _criacao.FichaDeTeste.Raca == Filhos.MeioSaiyajin,
			  $"etapa '{_criacao.TituloDaEtapaDeTeste}', raca {_criacao.FichaDeTeste.Raca}");
		Checa("...com as TRES linhagens do meio-Saiyajin na tela", escolha.Bebe.Linhagens.All(l => Cartao(_criacao, l) != null),
			  string.Join("/", escolha.Bebe.Linhagens.Where(l => Cartao(_criacao, l) == null)));
		Apertar(Cartao(_criacao, "Future Lineage"));
		yield return 0.3;
		Fotografar("filho-2-linhagem");
		Checa("...e o cartao escolhido vira a linhagem da ficha (Future Lineage)", _criacao.FilhoDeTeste.Linhagem == "Future Lineage", _criacao.FilhoDeTeste.Linhagem);

		for (int i = 0; i < 14 && _criacao.TituloDaEtapaDeTeste != "QUEM É VOCÊ?"; i++)
		{
			Apertar(Botao(_criacao, "Avançar"));
			yield return 0.25;
		}
		Checa("...o caminho ate 'QUEM É VOCÊ?' sem o berco (o filho nasce do lado da mae)",
			  _criacao.TituloDaEtapaDeTeste == "QUEM É VOCÊ?", $"parou em '{_criacao.TituloDaEtapaDeTeste}'");
		Checa("...e la a idade e ZERO, TRAVADA, e a ficha leva o bebe", _criacao.FilhoDeTeste is { Idade: 0, IdadeTravada: true } t && t.FilhoDe == id,
			  $"{_criacao.FilhoDeTeste}");
		if (Todos<LineEdit>(_criacao).FirstOrDefault(l => l.PlaceholderText == "nome do personagem") is { } nome) nome.Text = "Gohan";
		if (Todos<TextEdit>(_criacao).FirstOrDefault() is { } historia)
		{
			historia.Text = "O filho de Goku e Chichi, nascido de uma bancada de robo.";
			historia.EmitSignal(TextEdit.SignalName.TextChanged);   // o `Text` escrito por codigo nao avisa -- e a contagem da foto ficava em 0
		}
		yield return 0.3;
		Fotografar("filho-3-identidade");

		// ---- 4) O PARTO ----
		_noMundo = false;
		Apertar(Botao(_criacao, "Entrar no mundo"));
		for (int i = 0; i < 200 && !_noMundo && _recusas.Count == 0; i++) yield return 0.05;
		yield return 0.5;
		(string Raca, int Idade, string Classe, double Potencial)? nasceu = srv.NascidoDeTeste(Conta);
		Checa("4. O FILHO NASCEU pelo caminho da tela: Half-Saiyan, ZERO anos, a linhagem escolhida (Future Lineage)",
			  _noMundo && nasceu is { Raca: Filhos.MeioSaiyajin, Idade: 0, Classe: "Future Lineage" },
			  _recusas.Count > 0 ? $"recusado: {_recusas[^1]}" : _criacao.ErroDeTeste.Length > 0 ? $"a tela recusou: {_criacao.ErroDeTeste}" : $"{nasceu}");
		Checa("...com o potencial dos pais (10.000 + 24.000), e o bebe saiu da fila",
			  nasceu is { Potencial: >= 34_000 } && srv.BebesNaFilaDeTeste == 0, $"potencial {nasceu?.Potencial:N0}, fila {srv.BebesNaFilaDeTeste}");

		foreach (double d in Fim()) yield return d;
	}

	private IEnumerable<double> Fim()
	{
		foreach (string l in _linhas) GD.Print("[dfilho] " + l);
		foreach (string f in _fotos) GD.Print("[dfilho]   foto " + f);
		GD.Print(_falhas.Count == 0 ? "[dfilho] ===== TUDO OK =====" : $"[dfilho] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dfilho]   - " + f);
		yield return 0.3;
		GetTree()?.Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
