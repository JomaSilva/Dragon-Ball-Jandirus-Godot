using Godot;
using Jandirus.Core.Races;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-19) A ESCOLHA DOS CORPOS DAS FORMAS DO FROST DEMON -- uma aba por degrau da escada e a grade com o catalogo inteiro.
///
/// ============================ UM COMPONENTE, PORQUE O DM TEM DUAS PORTAS PRA MESMA TELA ============================
/// O `formchoose()` (`body_custom.dm:111-256`) abre na CRIACAO (`IcerCustomization`) e de novo EM JOGO, pelo `Icer_Form_Settings`
/// (`icer.dm:77-83`). Aqui as duas portas sao a `CreationScreen` (a etapa "AS SUAS FORMAS") e a <see cref="TelaDeFormasDoFrost"/>; o
/// que elas tem em comum -- as abas, a grade, o aviso -- mora aqui uma vez, e o que muda e so quem guarda a lista e o que se faz com
/// ela no fim (o pacote de criacao, ou o verb `formas_frost`).
///
/// ============================ QUANTAS ABAS: A CLASSE DIZ ============================
/// Tres pro Frost Demon normal (a base e as duas evolucoes), SETE pro Mutante (as quatro supressoes, a base e as duas evolucoes) --
/// `FormasDeFrost.DegrausDe`, o `form_pick_slots` do DM (`body_custom.dm:197-203`). A lista que o componente edita tem exatamente um
/// corpo por degrau, na mesma ordem; quem a prepara e quem chama.
/// ===================================================================================
/// </summary>
public partial class EscolhaDeFormasDoFrost : VBoxContainer
{
	/// <summary>Um corpo trocou, ou a aba aberta trocou -- quem desenha a previa redesenha.</summary>
	public event Action? Mudou;

	private readonly Label _aviso;
	private readonly GridContainer _abas, _grade;

	/// <summary>Os botoes do catalogo, por corpo. Montados UMA vez -- ver <see cref="MarcarAGrade"/>.</summary>
	private readonly Dictionary<string, Button> _botoes = [];

	private List<string> _corpos = [];
	private int[] _degraus = [];

	/// <summary>O slot aberto: o indice, na lista, do degrau cujo corpo a grade esta escolhendo.</summary>
	public int SlotAberto { get; private set; }

	/// <summary>
	/// OS FILHOS NASCEM NO CONSTRUTOR, e nao no `_Ready`: a `CreationScreen` monta as paginas fora da arvore e ja abre esta escolha
	/// (a raca sorteada pode ser o Icer) antes de o painel dela entrar na tela.
	/// </summary>
	public EscolhaDeFormasDoFrost()
	{
		AddThemeConstantOverride("separation", 6);

		_aviso = new Label { AutowrapMode = TextServer.AutowrapMode.Word, HorizontalAlignment = HorizontalAlignment.Center };
		_aviso.AddThemeFontSizeOverride("font_size", 12);
		AddChild(_aviso);

		// QUATRO POR LINHA: as sete do Mutante nao cabem numa linha so do painel da criacao (470 px), e duas linhas de 4 + 3
		// leem como a escada que sao -- as cascas em cima, a base e as evolucoes embaixo.
		_abas = new GridContainer { Columns = 4 };
		_abas.AddThemeConstantOverride("h_separation", 6);
		_abas.AddThemeConstantOverride("v_separation", 4);
		AddChild(_abas);

		_grade = new GridContainer { Columns = 6 };
		_grade.AddThemeConstantOverride("h_separation", 6);
		_grade.AddThemeConstantOverride("v_separation", 6);
		AddChild(CreationScreen.Rolagem(_grade, 260));
	}

	/// <summary>
	/// ABRE A ESCOLHA pra esta CLASSE, editando ESTA lista no lugar (um corpo por degrau da classe). A aba aberta volta pra primeira.
	/// </summary>
	/// <param name="emJogo">A porta do `Icer_Form_Settings` -- o aviso de cima fala de trocar, e nao de nascer.</param>
	public void Abrir(string classe, List<string> corpos, bool emJogo)
	{
		_corpos = corpos;
		_degraus = FormasDeFrost.DegrausDe(classe);
		SlotAberto = 0;

		bool mutante = classe == FormasDeFrost.ClasseMutante;
		_aviso.Text = emJogo ? AvisoEmJogo : mutante ? AvisoDoMutante : AvisoDoNormal;
		_aviso.AddThemeColorOverride("font_color", mutante && !emJogo ? Tema.Perigo : Tema.TextoFraco);

		foreach (Node n in _abas.GetChildren())
		{
			_abas.RemoveChild(n);
			n.QueueFree();
		}
		for (int i = 0; i < _degraus.Length; i++)
		{
			int slot = i, degrau = _degraus[i];
			var b = new Button { Text = NomeDaAba(degrau), ToggleMode = true, TooltipText = DicaDaAba(classe, degrau) };
			b.AddThemeFontSizeOverride("font_size", 12);
			b.Pressed += () => { CreationScreen.MarcarSelecionado(_abas, b); SlotAberto = slot; MarcarAGrade(); Mudou?.Invoke(); };
			_abas.AddChild(b);
			if (i == 0) b.ButtonPressed = true;
		}

		MarcarAGrade();
		Mudou?.Invoke();
	}

	/// <summary>
	/// A GRADE DE CORPOS. Montada uma vez; trocar de aba so remarca qual esta aceso. (Ela era reconstruida a cada aba na criacao, e o
	/// dono sentiu: *"ao clicar em frost demon o jogo da uma travadinha"* -- sao 54 miniaturas lidas do disco.)
	/// </summary>
	private void MarcarAGrade()
	{
		if (_botoes.Count == 0)
			foreach (string corpo in FormasDeFrost.Catalogo)
			{
				string alvo = corpo;
				Button b = CreationScreen.BotaoIcone(CreationScreen.Miniatura(FormasDeFrost.Caminho(corpo)), corpo);
				b.Pressed += () =>
				{
					if (SlotAberto >= _corpos.Count) return;
					_corpos[SlotAberto] = alvo;
					CreationScreen.MarcarSelecionado(_grade, b);
					Mudou?.Invoke();
				};
				_grade.AddChild(b);
				_botoes[corpo] = b;
			}

		string atual = SlotAberto < _corpos.Count ? _corpos[SlotAberto] : "";
		foreach ((string corpo, Button b) in _botoes) b.ButtonPressed = corpo == atual;
	}

	/// <summary>O rotulo curto da aba: as cascas com a porcentagem (os chips "SUPRESSAO 1..4" do DM), o resto pelo nome do degrau.</summary>
	private static string NomeDaAba(int degrau) => degrau <= FormasDeFrost.UltimaSupressao
		? $"{degrau}ª Forma · {FormasDeFrost.Multiplicador(degrau) * 100:0}%"
		: FormasDeFrost.Nome(degrau);

	/// <summary>
	/// O QUE O DEGRAU FAZ, na dica da aba. O multiplicador sai do `FormasDeFrost.Multiplicador`, que e o mesmo que o combate paga. Era
	/// `{mult:0}` -- e as supressoes saiam "multiplica o seu poder por 0".
	/// </summary>
	private static string DicaDaAba(string classe, int degrau)
	{
		string ondeComeca = degrau == FormasDeFrost.FormaInicial(classe) ? " -- é onde você começa" : "";
		return degrau <= FormasDeFrost.UltimaSupressao
			? $"supressão: {FormasDeFrost.Multiplicador(degrau) * 100:0}% do seu poder{ondeComeca}"
			: degrau == FormasDeFrost.Base
				? $"1x: o corpo sem casca nenhuma{ondeComeca}"
				: $"multiplica o seu poder por {FormasDeFrost.Multiplicador(degrau):0}";
	}

	/// <summary>
	/// O AVISO DO MUTANTE NA CRIACAO -- o *"You were born a Mutant Frost Demon."* do `statfrost()` (`staticer.dm:7`) e o
	/// *"(MUTANTE: voce comeca na SUPRESSAO 1 -- 25% do seu poder.)"* da tela de formas do DM (`body_custom.dm:307`).
	/// </summary>
	public const string AvisoDoMutante =
		"VOCÊ NASCEU UM FROST DEMON MUTANTE. São sete corpos: as quatro supressões, a base e as duas evoluções. Você começa na "
		+ "1ª Forma, com 25% do seu poder -- e só segura as de cima pra sempre masterizando a forma base.";

	/// <summary>O do normal: *"(Voce comeca na forma BASE; as evolucoes destravam com BP.)"* (`body_custom.dm:307`).</summary>
	public const string AvisoDoNormal = "Você começa na forma base; as duas evoluções se abrem quando o seu poder chegar lá.";

	/// <summary>O da porta em jogo: o `formchoose` re-aplica e devolve o corpo a forma inicial (`icer.dm:80-83`).</summary>
	public const string AvisoEmJogo = "Escolha o corpo de cada degrau. Trocar os corpos devolve você à forma inicial.";

	// =====================================================================
	// AS PORTAS DE BANCADA (`--diagmutante`)
	// =====================================================================
	/// <summary>Quantas abas a escolha tem agora. Pra bancada.</summary>
	public int AbasDeTeste => _abas.GetChildCount();

	/// <summary>O aviso de cima. Pra bancada.</summary>
	public string AvisoDeTeste => _aviso.Text;

	/// <summary>Abre a aba deste slot como o clique nela abriria. Pra bancada (o robo fotografa a escolha de cada degrau).</summary>
	public void AbrirAbaDeTeste(int slot)
	{
		if (slot < 0 || slot >= _abas.GetChildCount() || _abas.GetChild(slot) is not Button b) return;
		b.ButtonPressed = true;
		b.EmitSignal(BaseButton.SignalName.Pressed);
	}

	/// <summary>Escolhe este corpo na aba aberta como o clique na grade escolheria. Pra bancada.</summary>
	public void EscolherDeTeste(string corpo)
	{
		if (_botoes.TryGetValue(corpo, out Button? b)) b.EmitSignal(BaseButton.SignalName.Pressed);
	}
}
