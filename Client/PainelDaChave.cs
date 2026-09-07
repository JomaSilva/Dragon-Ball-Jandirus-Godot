using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O CHAVEAMENTO NA TELA -- o pedido do dono (2026-09-07): *"ao comecar um torneio o jogo deveria
/// colocar na minha tela o chaveamento bonitinho com cada nome, e um contorno que fica com um brilho
/// pulsando lentamente na minha luta pra eu saber qual vai ser"*.
///
/// ============================ O SERVIDOR MANDA O RETRATO, O CLIENTE SO DESENHA ============================
/// Tudo o que esta aqui vem do `S2C.Chave` (<see cref="ChaveNaTela"/>): os nomes, as rodadas, quem
/// venceu, onde a chave esta e qual e a minha chave. O painel nao deduz nada -- ele nao sabe montar
/// uma chave, nao sabe quem avanca, nao guarda estado entre pacotes alem do ultimo recebido. Assim o
/// que o dono ve e o que o servidor diz, e uma chave desenhada errada e um pacote errado, nunca uma
/// conta feita duas vezes.
///
/// ABRE SOZINHO quando a chave nasce (o primeiro pacote com rodadas) e nao volta a abrir por conta
/// propria: a cada luta o retrato so se atualiza se o painel estiver aberto. Quem fechou reabre pelo
/// botao "Chaveamento" do canto ou pelo verb da aba Other.
/// ==========================================================================================================
/// </summary>
public partial class PainelDaChave : PanelContainer
{
	private Label _titulo = null!, _rodape = null!;
	private Button _fechar = null!;
	private ScrollContainer _rolagem = null!;
	private DesenhoDaChave _desenho = null!;
	private ChaveNaTela? _chave;
	private bool _abriuEstaChave;

	public bool VisivelDeTeste => Visible;
	public bool TemChave => _chave != null;
	public int CompetidoresDeTeste => _chave?.Competidores.Count ?? 0;
	public int MinhaChaveDeTeste => _chave?.MinhaChave ?? -1;
	public int MinhaLutaDeTeste => _desenho.MinhaLuta;
	public float PulsoDeTeste => _desenho.Pulso;
	public int QuadrosDesenhadosDeTeste => _desenho.QuadrosDesenhadosDeTeste;
	public string TituloDeTeste => _titulo.Text;

	public override void _Ready()
	{
		AnchorLeft = 0.5f; AnchorRight = 0.5f; AnchorTop = 0.5f; AnchorBottom = 0.5f;
		GrowHorizontal = GrowDirection.Both;
		GrowVertical = GrowDirection.Both;
		Tema.Aplicar(this);
		var v = new VBoxContainer();
		v.AddThemeConstantOverride("separation", 6);
		AddChild(v);
		var topo = new HBoxContainer();
		topo.AddThemeConstantOverride("separation", 10);
		v.AddChild(topo);
		_titulo = Tema.Legenda("", Tema.Destaque, 15);
		_titulo.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		topo.AddChild(_titulo);
		_fechar = new Button { Text = "Fechar" };
		_fechar.Pressed += Fechar;
		topo.AddChild(_fechar);
		_rolagem = new ScrollContainer
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
		};
		v.AddChild(_rolagem);
		_desenho = new DesenhoDaChave { Name = "Desenho" };
		_rolagem.AddChild(_desenho);
		_rodape = Tema.Legenda("", Tema.TextoFraco, 12);
		v.AddChild(_rodape);
		Visible = false;
	}

	/// <summary>O retrato chegou (`S2C.Chave`). Aviso 2 = o torneio acabou: fecha e esquece.</summary>
	public void Receber(ChaveNaTela c, int localId)
	{
		if (c.Aviso == 2)
		{
			_chave = null;
			_abriuEstaChave = false;
			Visible = false;
			return;
		}
		_chave = c;
		_titulo.Text = (c.Tipo == 2 ? "TORNEIO DO OUTRO MUNDO" : "TORNEIO DE ARTES MARCIAIS") + " -- chaveamento";
		_desenho.Definir(c);
		// A ROLAGEM CABE NA TELA: o desenho pede o tamanho da chave inteira (32 nomes em 5 colunas), e a
		// janela pode ser menor que isso -- a rolagem e o que sobra.
		Vector2 tela = GetViewportRect().Size;
		_rolagem.CustomMinimumSize = new Vector2(
			Mathf.Min(_desenho.CustomMinimumSize.X, Mathf.Max(200, tela.X - 120)),
			Mathf.Min(_desenho.CustomMinimumSize.Y, Mathf.Max(160, tela.Y - 200)));
		_rodape.Text = Rodape(c);
		if (!_abriuEstaChave && c.Rodadas.Count > 0)
		{
			_abriuEstaChave = true;
			Visible = true;
		}
	}

	private static string Rodape(ChaveNaTela c)
	{
		string fase = c.Fase switch
		{
			0 => "inscricoes abertas",
			1 => "preparando a proxima luta",
			2 => "contagem",
			3 => "luta em andamento",
			_ => "intervalo",
		};
		string minha = c.MinhaChave < 0 ? "voce nao esta na chave"
			: c.Competidores.Count > c.MinhaChave ? $"voce e {c.Competidores[c.MinhaChave].Nome}: sua proxima luta pulsa" : "";
		return $"{fase}  |  {minha}  |  o botao Chaveamento (canto inferior esquerdo) reabre isto";
	}

	public void Fechar() => Visible = false;

	public void Alternar() => Visible = !Visible && _chave != null;
}

/// <summary>
/// O DESENHO DA CHAVE: colunas por rodada, uma caixa por luta com os dois nomes, linhas ligando cada
/// luta a proxima, a luta de agora com a borda viva e a MINHA proxima luta com o contorno pulsando.
///
/// O TAMANHO E FUNCAO DA CHAVE: 16 lutas na primeira coluna dao a altura; as colunas sao as rodadas
/// que existem MAIS as que ainda vao existir (com caixas vazias), pra que a arvore inteira apareca
/// desde o primeiro pacote -- e o que faz "chaveamento" parecer chaveamento, e nao uma lista.
/// </summary>
public partial class DesenhoDaChave : Control
{
	private const float ColW = 172f, BoxW = 150f, BoxH = 30f, Passo = 36f, Margem = 10f, Cabeca = 24f;
	private const float PeriodoDoPulso = 2.2f;   // "brilho pulsando LENTAMENTE"
	private ChaveNaTela? _c;
	private Font _fonte = ThemeDB.FallbackFont;

	/// <summary>A minha proxima luta (indice na rodada dela), ou -1. So bancada.</summary>
	public int MinhaLuta { get; private set; } = -1;
	/// <summary>O brilho do pulso no ultimo quadro desenhado (0..1). So bancada.</summary>
	public float Pulso { get; private set; }
	/// <summary>Quantos quadros ja desenhou -- a prova de que o pulso e redesenhado por quadro. So bancada.</summary>
	public int QuadrosDesenhadosDeTeste { get; private set; }

	public void Definir(ChaveNaTela c)
	{
		_c = c;
		int n0 = c.Rodadas.Count > 0 ? c.Rodadas[0].Lutas.Count : 1;
		int colunas = Colunas(c);
		CustomMinimumSize = new Vector2(Margem * 2 + colunas * ColW, Cabeca + n0 * Passo + Passo * 2 + Margem);
		MinhaLuta = -1;
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		if (IsVisibleInTree() && _c != null) QueueRedraw();   // o pulso e do relogio, entao o desenho e por quadro
	}

	/// <summary>Quantas rodadas a arvore TEM ou VAI TER: 16 -> 8 -> 4 -> 2 -> final (o 3o lugar mora na coluna da final).</summary>
	private static int Colunas(ChaveNaTela c)
	{
		int n0 = c.Rodadas.Count > 0 ? c.Rodadas[0].Lutas.Count : 1;
		int colunas = 1;
		for (int n = n0; n > 1; n = (n + 1) / 2) colunas++;
		return Math.Max(colunas, Padrao(c).Count + (TemFinal(c) ? 1 : 0));
	}

	private static bool EhFinal(string nome) => nome == "final";
	private static bool EhTerceiro(string nome) => nome == "disputa do 3o lugar";
	private static bool TemFinal(ChaveNaTela c) => c.Rodadas.Exists(r => EhFinal(r.Nome));
	private static List<(string Nome, List<(short A, short B, short Vencedor)> Lutas)> Padrao(ChaveNaTela c)
		=> c.Rodadas.FindAll(r => !EhFinal(r.Nome) && !EhTerceiro(r.Nome));

	public override void _Draw()
	{
		if (_c is not { } c || c.Rodadas.Count == 0) return;
		var padrao = Padrao(c);
		int n0 = padrao[0].Lutas.Count;
		int colunas = Colunas(c);
		float alturaDaArvore = n0 * Passo;
		Pulso = 0.5f + 0.5f * MathF.Sin((float)(Time.GetTicksMsec() / 1000.0) * MathF.Tau / PeriodoDoPulso);
		QuadrosDesenhadosDeTeste++;
		MinhaLuta = -1;

		// A LUTA DE AGORA: a primeira sem vencedor na rodada atual (a mesma pergunta do `Chave.LutaAtual`).
		int rodadaAtual = c.RodadaAtual;
		int lutaDeAgora = -1;
		if (rodadaAtual < c.Rodadas.Count) lutaDeAgora = c.Rodadas[rodadaAtual].Lutas.FindIndex(l => l.Vencedor < 0);
		// A MINHA PROXIMA LUTA: a primeira sem vencedor, de hoje pra frente, em que eu estou.
		(int rodada, int luta) minha = (-1, -1);
		if (c.MinhaChave >= 0)
			for (int r = Math.Max(0, rodadaAtual); r < c.Rodadas.Count && minha.rodada < 0; r++)
			{
				int i = c.Rodadas[r].Lutas.FindIndex(l => l.Vencedor < 0 && (l.A == c.MinhaChave || l.B == c.MinhaChave));
				if (i >= 0) minha = (r, i);
			}

		Vector2 CentroDaCaixa(int coluna, int i, int n) =>
			new(Margem + coluna * ColW + BoxW / 2f, Cabeca + (i + 0.5f) * (alturaDaArvore / Math.Max(1, n)));

		// AS LINHAS PRIMEIRO (ficam por baixo das caixas): de cada luta pra luta i/2 da coluna seguinte.
		var linha = Tema.Borda;
		for (int col = 0; col + 1 < colunas; col++)
		{
			int n = col < padrao.Count ? padrao[col].Lutas.Count : Math.Max(1, n0 >> col);
			int nProx = col + 1 < padrao.Count ? padrao[col + 1].Lutas.Count : Math.Max(1, n0 >> (col + 1));
			for (int i = 0; i < n; i++)
			{
				Vector2 de = CentroDaCaixa(col, i, n) + new Vector2(BoxW / 2f, 0);
				Vector2 para = (col + 1 == colunas - 1)
					? new Vector2(Margem + (col + 1) * ColW, Cabeca + alturaDaArvore / 2f - Passo / 2f)   // a FINAL
					: CentroDaCaixa(col + 1, i / 2, nProx) - new Vector2(BoxW / 2f, 0);
				float meio = (de.X + para.X) / 2f;
				DrawLine(de, new Vector2(meio, de.Y), linha, 1.5f);
				DrawLine(new Vector2(meio, de.Y), new Vector2(meio, para.Y), linha, 1.5f);
				DrawLine(new Vector2(meio, para.Y), para, linha, 1.5f);
			}
		}

		// AS COLUNAS PADRAO (16-avos ... semifinal), com as rodadas futuras em caixas vazias.
		for (int col = 0; col + 1 < colunas; col++)
		{
			bool existe = col < padrao.Count;
			int n = existe ? padrao[col].Lutas.Count : Math.Max(1, n0 >> col);
			// A RODADA QUE AINDA VAI EXISTIR ganha o nome que o servidor vai lhe dar (a mesma funcao).
			string nome = existe ? padrao[col].Nome : Jandirus.Core.Torneio.Chave.NomeDaRodada(n * 2);
			DrawString(_fonte, new Vector2(Margem + col * ColW, Cabeca - 8), nome.ToUpperInvariant(), HorizontalAlignment.Left, BoxW, 11, Tema.TextoFraco);
			for (int i = 0; i < n; i++)
			{
				Vector2 centro = CentroDaCaixa(col, i, n);
				var caixa = new Rect2(centro - new Vector2(BoxW / 2f, BoxH / 2f), new Vector2(BoxW, BoxH));
				int indiceDaRodada = existe ? c.Rodadas.IndexOf(padrao[col]) : -1;
				var luta = existe ? padrao[col].Lutas[i] : ((short)-1, (short)-1, (short)-1);
				bool agora = existe && indiceDaRodada == rodadaAtual && i == lutaDeAgora;
				bool eMinha = existe && indiceDaRodada == minha.rodada && i == minha.luta;
				if (eMinha) MinhaLuta = i;
				Caixa(c, caixa, luta, agora, eMinha);
			}
		}

		// A ULTIMA COLUNA: a FINAL em cima, a disputa do 3o lugar embaixo.
		{
			int col = colunas - 1;
			float x = Margem + col * ColW;
			DrawString(_fonte, new Vector2(x, Cabeca - 8), "FINAL", HorizontalAlignment.Left, BoxW, 11, Tema.TextoFraco);
			var final = c.Rodadas.Find(r => EhFinal(r.Nome));
			var terceiro = c.Rodadas.Find(r => EhTerceiro(r.Nome));
			var caixaFinal = new Rect2(x, Cabeca + alturaDaArvore / 2f - Passo / 2f - BoxH / 2f, BoxW, BoxH);
			var lutaFinal = final.Lutas is { Count: > 0 } lf ? lf[0] : ((short)-1, (short)-1, (short)-1);
			int iFinal = c.Rodadas.IndexOf(final);
			bool finalAgora = final.Lutas != null && iFinal == rodadaAtual && lutaDeAgora == 0;
			bool finalMinha = final.Lutas != null && iFinal == minha.rodada && minha.luta == 0;
			if (finalMinha) MinhaLuta = 0;
			Caixa(c, caixaFinal, lutaFinal, finalAgora, finalMinha);

			float yTerceiro = caixaFinal.Position.Y + BoxH + Passo * 0.9f;
			DrawString(_fonte, new Vector2(x, yTerceiro - 6), "3o LUGAR", HorizontalAlignment.Left, BoxW, 11, Tema.TextoFraco);
			var caixaTerceiro = new Rect2(x, yTerceiro, BoxW, BoxH);
			var lutaTerceiro = terceiro.Lutas is { Count: > 0 } lt ? lt[0] : ((short)-1, (short)-1, (short)-1);
			int iTerceiro = c.Rodadas.IndexOf(terceiro);
			bool terceiroAgora = terceiro.Lutas != null && iTerceiro == rodadaAtual && lutaDeAgora == 0;
			bool terceiroMinha = terceiro.Lutas != null && iTerceiro == minha.rodada && minha.luta == 0;
			if (terceiroMinha) MinhaLuta = 0;
			Caixa(c, caixaTerceiro, lutaTerceiro, terceiroAgora, terceiroMinha);
		}
	}

	private void Caixa(ChaveNaTela c, Rect2 caixa, (short A, short B, short Vencedor) luta, bool agora, bool minha)
	{
		DrawRect(caixa, agora ? Tema.PainelAceso : Tema.PainelClaro);
		DrawRect(caixa, agora ? Tema.BordaViva : Tema.Borda, filled: false, width: agora ? 2f : 1f);
		if (minha)
		{
			// O CONTORNO QUE PULSA: dois aneis, o de dentro firme e o de fora esmaecendo com o pulso.
			var viva = Tema.BordaViva;
			DrawRect(caixa.Grow(2f), new Color(viva, 0.35f + 0.65f * Pulso), filled: false, width: 2f);
			DrawRect(caixa.Grow(5f), new Color(viva, 0.30f * Pulso), filled: false, width: 3f);
		}
		DrawLine(caixa.Position + new Vector2(6, BoxH / 2f), caixa.Position + new Vector2(BoxW - 6, BoxH / 2f), new Color(Tema.Borda, 0.6f), 1f);
		Nome(c, caixa.Position + new Vector2(6, 12), luta.A, luta.Vencedor);
		Nome(c, caixa.Position + new Vector2(6, 25), luta.B, luta.Vencedor);
	}

	private void Nome(ChaveNaTela c, Vector2 onde, short quem, short vencedor)
	{
		string texto = quem < 0 || quem >= c.Competidores.Count ? "?" : c.Competidores[quem].Nome;
		Color cor = quem >= 0 && quem == c.MinhaChave ? Tema.BordaViva
				  : vencedor < 0 ? Tema.Texto
				  : vencedor == quem ? Tema.Destaque
				  : Tema.TextoFraco;
		texto = Encurtar(texto, BoxW - 14);
		DrawString(_fonte, onde, texto, HorizontalAlignment.Left, BoxW - 12, 11, cor);
	}

	private string Encurtar(string s, float largMax)
	{
		while (s.Length > 1 && _fonte.GetStringSize(s, HorizontalAlignment.Left, -1, 11).X > largMax) s = s[..^1];
		return s;
	}
}
