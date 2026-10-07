using Godot;
using Jandirus.Core.Combat;
using static Jandirus.Client.ArteDeKiNoCliente;

namespace Jandirus.Client;

/// <summary>
/// UMA ARTE DE KI NUM QUADRADINHO DE INTERFACE -- a miniatura da mesa de tecnicas.
///
/// ============================ ELA ERA UM QUADRO DA FOLHA, E AGORA E O PROPRIO DESENHO ============================
/// A grade de artes mostrava o quadro zero de `head_east` (ou da bola) de cada folha num `TextureRect`.
/// Com o tiro desenhado por shader nao ha quadro pra mostrar -- e mostrar a folha antiga seria pior
/// que nao mostrar nada: o jogador escolheria a arte por um desenho que nao e o que sai da mao dele.
///
/// Entao a miniatura e o MESMO material e a MESMA geometria do tiro de verdade
/// (<see cref="PintorDeKi"/>), encolhidos pra caber: a ponta de um raio entrando pela esquerda, ou a
/// bola inteira. E como e o shader de verdade, ela se MEXE -- a grade deixa de ser um mostruario de
/// figurinhas e passa a mostrar o que cada estilo faz.
///
/// POR QUE NAO UM `ProjetilDesenhado` PEQUENO: ele e um `Node2D` que desliza atras de pacotes do
/// servidor, acende luz e mora no mundo. A previa grande da mesa usa um de proposito (ela e "como sai
/// da sua mao", em tamanho real); quarenta deles dentro de botoes seriam quarenta espelhos de servidor
/// sem servidor.
/// ================================================================================================================
/// </summary>
public partial class AmostraDeKi : Control
{
	public TipoDeProjetil Tipo;
	public ArteDeKi Arte;
	public Color Cor = Aura.CorDoKiCru;

	/// <summary>A folga entre o desenho e a borda do quadradinho, em px de tela.</summary>
	private const float Margem = 3f;

	/// <summary>O quanto uma arte miuda pode ser AMPLIADA pra encher o quadradinho. Acima disto o fio vira tubo.</summary>
	private const float AmpliacaoMaxima = 1.6f;

	private ShaderMaterial? _mat;
	private EstiloDeFeixe _feixe = EstiloDoFeixe(ArteDeKi.Nenhuma);
	private EstiloDeBola _bola = EstiloDaBola(ArteDeKi.Nenhuma);
	private PintorDeKi.MedidasDoFeixe _medidas;
	private double _idade;

	/// <summary>Ha o que desenhar aqui (o shader carregou)? E o que a grade publica como "tem miniatura".</summary>
	public bool Vestida => _mat != null;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;   // quem recebe o clique e o botao em volta
		ClipContents = true;                    // o tronco do raio entra por fora do quadradinho, e para na borda
		if (!Vestida) Vestir();
	}

	/// <summary>
	/// MONTA O MATERIAL do estilo desta arte. Pode ser chamado antes de o node entrar na arvore (a mesa
	/// pergunta <see cref="Vestida"/> enquanto ainda esta montando o botao); o `_Ready` so o chama se
	/// ninguem chamou.
	/// </summary>
	public void Vestir()
	{
		if (Tipo == TipoDeProjetil.Beam)
		{
			_feixe = EstiloDoFeixe(Arte);
			_medidas = PintorDeKi.Medir(Arte, 1f);
			_mat = PintorDeKi.MaterialDeFeixe(Arte, 1f, Cor);
		}
		else
		{
			_bola = EstiloDaBola(Arte);
			_mat = PintorDeKi.MaterialDeBola(_bola, _bola.Raio, Cor);
		}
		Material = _mat;
	}

	/// <summary>A cor do ki mudou no seletor: retinge sem refazer o material.</summary>
	public void Tingir(Color cor)
	{
		Cor = cor;
		if (_mat != null) PintorDeKi.Tingir(_mat, Tipo == TipoDeProjetil.Beam ? _feixe.Tons : _bola.Tons, cor);
	}

	public override void _Process(double delta)
	{
		_idade += delta;
		if (IsVisibleInTree()) QueueRedraw();
	}

	public override void _Draw()
	{
		if (_mat == null) return;
		_mat.SetShaderParameter("tempo", (float)_idade);

		Vector2 lado = Size;
		float meiaAltura = lado.Y * 0.5f - Margem;
		if (meiaAltura <= 1f) return;

		if (Tipo == TipoDeProjetil.Beam)
		{
			// O QUE TEM QUE CABER NA ALTURA e a cabeca com a chama dela; o halo pode cortar na borda.
			float vulto = _medidas.Cabeca * (1f + 0.5f * _feixe.Chama + 0.35f * _feixe.Coroa) + 1f;
			float tamanho = Mathf.Min(AmpliacaoMaxima, meiaAltura / vulto);

			// A PONTA ENCOSTA NA DIREITA e a mao fica bem fora, a esquerda: o que se ve e a cabeca com
			// um pedaco de tronco -- o mesmo recorte que a folha `head_east` mostrava.
			float comprimento = lado.X / tamanho + 60f;
			var ponta = new Vector2((lado.X - Margem) / tamanho, lado.Y * 0.5f / tamanho);
			var mao = ponta - new Vector2(comprimento, 0f);

			_mat.SetShaderParameter("comprimento", comprimento);
			DrawSetTransform(Vector2.Zero, 0f, Vector2.One * tamanho);
			PintorDeKi.FitaReta(this, mao, ponta, PintorDeKi.MeiaFita(_feixe, _medidas),
								PintorDeKi.FolgaNaMao(_medidas), PintorDeKi.FolgaNaPonta(_medidas));
			return;
		}

		// A BOLA INTEIRA, com o rastro que ela tem em voo. O centro anda pra direita o bastante pro
		// rastro caber atras, ate onde o quadradinho deixa.
		float rastro = _bola.Rastro * _bola.Raio;
		float vultoDaBola = _bola.Raio * (1.25f + 0.3f * _bola.Chama + (_bola.Pontilhado > 0f ? 0.7f : 0f) + (_bola.Aro > 0f ? 0.5f : 0f));
		float escala = Mathf.Min(AmpliacaoMaxima, Mathf.Min(meiaAltura / vultoDaBola, (lado.X - 2f * Margem) / (2f * vultoDaBola + rastro * 0.6f)));

		_mat.SetShaderParameter("rastro", rastro);
		var centro = new Vector2(lado.X * 0.5f + rastro * 0.3f * escala, lado.Y * 0.5f);
		DrawSetTransform(centro, 0f, Vector2.One * escala);
		PintorDeKi.Quadro(this, Vector2.Zero, Vector2.Right, PintorDeKi.MeiaDaBola(_bola, _bola.Raio), rastro);
	}
}
