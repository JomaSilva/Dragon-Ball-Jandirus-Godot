using Godot;
using Jandirus.Core.Social;

namespace Jandirus.Client;

/// <summary>
/// ============================ A VOZ DE RADIO: BANDA ESTREITA, SATURADA, COM CHIADO ============================
/// *"...podem escutar sua voz no ouvido esquerdo deles com um EFEITO DE RADIO"* -- o dono, 2026-10-10.
///
/// O que faz uma voz soar "de radio" sao tres coisas, e as tres sao contas simples sobre o PCM ja
/// descomprimido (48 kHz, mono):
///
///   | o que          | como                                                                           |
///   |----------------|--------------------------------------------------------------------------------|
///   | banda estreita | passa-alta em <see cref="GraveDaBanda"/> e passa-baixa em <see cref="AgudoDaBanda"/> (a banda do telefone), 24 dB por oitava de cada lado |
///   | saturacao      | o sinal e empurrado contra um teto macio (`tanh`): a voz fica comprimida e "rachada" nos picos |
///   | chiado         | um fio de ruido branco por baixo, que passa pela MESMA banda -- o chiado tambem e de radio |
///
/// E dois estalos curtos de estatica, um ao ABRIR e um ao FECHAR a transmissao (<see cref="Abrir"/> e
/// <see cref="Fechar"/>): e por eles que se sabe, sem olhar a tela, que alguem pegou e soltou o canal.
///
/// ============================ POR QUE NO SINAL, E NAO NUM BARRAMENTO DO MOTOR ============================
/// Pelo argumento do <see cref="VozOuvida.FiltroDeParede"/>: efeito de barramento e GLOBAL, e ha ate quatro
/// vozes de radio ao mesmo tempo (cada uma com a memoria do proprio filtro); e so no sinal a bancada
/// (`--diagradio`) consegue MEDIR o que sai -- energia em 1 kHz contra energia em 100 Hz e em 9 kHz --, em
/// vez de conferir que um efeito "foi ligado". Esta classe nao sabe o que e rede nem alto-falante.
///
/// O OUVIDO ESQUERDO e a outra metade do pedido, e mora em <see cref="ParaOOuvidoEsquerdo"/>.
/// ===========================================================================================================
/// </summary>
public sealed class EfeitoDeRadio
{
	/// <summary>Abaixo disto o radio nao passa: 300 Hz, o piso da banda de voz do telefone. E o que tira o "peito" da voz.</summary>
	public const float GraveDaBanda = 300f;

	/// <summary>Acima disto tambem nao: 3,4 kHz, o teto da mesma banda. As consoantes ficam, o "ar" some.</summary>
	public const float AgudoDaBanda = 3400f;

	/// <summary>
	/// QUANTO O SINAL E EMPURRADO CONTRA O TETO antes do `tanh`. Com 3 a fala normal (pico ~0,3 depois da
	/// banda) encosta em 0,7 e os picos altos racham -- radio de mao, e nao distorcao de guitarra.
	/// </summary>
	public const float Saturacao = 3f;

	/// <summary>O volume de saida, depois de saturar. A voz chega num ouvido so, e por isso um pouco mais alta que a de perto.</summary>
	public const float Volume = 0.8f;

	/// <summary>A amplitude do ruido branco ANTES da banda. Depois dela e da saturacao sobra um chiado a uns -38 dB: ouve-se, nao atrapalha.</summary>
	public const float NivelDoChiado = 0.025f;

	/// <summary>A amplitude de pico dos dois estalos de estatica, antes da banda.</summary>
	public const float NivelDoEstalo = 0.3f;

	/// <summary>Quanto dura o estalo de ABRIR, em ms. Ele vai por cima do comeco da primeira palavra.</summary>
	public const int MsDoEstaloDeAbrir = 25;

	/// <summary>Quanto dura o estalo de FECHAR, em ms: o "kshh" de quem soltou o botao.</summary>
	public const int MsDoEstaloDeFechar = 60;

	/// <summary>O estalo de fechar em amostras de saida (2.880 a 48 kHz): o tamanho do buffer que <see cref="Fechar"/> enche.</summary>
	public const int AmostrasDoEstaloDeFechar = VozLocal.TaxaDeSaida * MsDoEstaloDeFechar / 1000;

	/// <summary>DEFEITO INJETADO (bancada `--diagradio`): a banda nao e cortada -- a voz passa inteira, so saturada. Lido por <see cref="Processar"/>.</summary>
	public static bool SemBandaDeTeste;

	/// <summary>DEFEITO INJETADO (bancadas `--diagradio` e `--radiovivo`): a voz de radio sai nos DOIS ouvidos. Lido por <see cref="ParaOOuvidoEsquerdo"/>.</summary>
	public static bool NosDoisOuvidosDeTeste;

	/// <summary>Um filtro de segunda ordem (a "biquad" do livro de receitas de audio do Bristow-Johnson), com a memoria dele.</summary>
	private struct Secao
	{
		private double _b0, _b1, _b2, _a1, _a2, _z1, _z2;

		/// <param name="passaAlta">Verdadeiro corta o que esta ABAIXO de <paramref name="hz"/>; falso, o que esta acima.</param>
		public static Secao Em(float hz, bool passaAlta)
		{
			// Q de 0,707 (Butterworth): a resposta mais plana dentro da banda. Duas secoes iguais em serie
			// caem 24 dB por oitava e custam 6 dB na propria quina -- que e a quina arredondada de um radio.
			double w = 2 * Math.PI * hz / VozLocal.TaxaDeSaida, cos = Math.Cos(w), alfa = Math.Sin(w) / (2 * 0.7071);
			double a0 = 1 + alfa;
			double b1 = passaAlta ? -(1 + cos) : 1 - cos;
			double b0 = Math.Abs(b1) / 2;
			return new Secao { _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b0 / a0, _a1 = -2 * cos / a0, _a2 = (1 - alfa) / a0 };
		}

		/// <summary>Uma amostra pelo filtro (forma direta II transposta: duas memorias, estavel em `double`).</summary>
		public double Passar(double x)
		{
			double y = _b0 * x + _z1;
			_z1 = _b1 * x - _a1 * y + _z2;
			_z2 = _b2 * x - _a2 * y;
			return y;
		}
	}

	private Secao _alta1 = Secao.Em(GraveDaBanda, passaAlta: true), _alta2 = Secao.Em(GraveDaBanda, passaAlta: true);
	private Secao _baixa1 = Secao.Em(AgudoDaBanda, passaAlta: false), _baixa2 = Secao.Em(AgudoDaBanda, passaAlta: false);

	/// <summary>Depois de saturar: o `tanh` cria harmonicos acima da banda, e um radio de verdade nao os toca.</summary>
	private Secao _depois = Secao.Em(AgudoDaBanda, passaAlta: false);

	private readonly bool _comChiado;
	private uint _sorteio;
	private int _estaloRestante;

	/// <param name="comChiado">A bancada desliga o chiado pra medir a banda sem ruido por cima. Em jogo, ligado.</param>
	/// <param name="semente">A semente do ruido. Fixa: o mesmo quadro de entrada da sempre o mesmo de saida.</param>
	public EfeitoDeRadio(bool comChiado = true, uint semente = 0x9E3779B9)
	{
		_comChiado = comChiado;
		_sorteio = semente == 0 ? 1 : semente;
	}

	/// <summary>Ruido branco entre -1 e 1 (xorshift de 32 bits: barato, e o bastante pra estatica).</summary>
	private float Ruido()
	{
		_sorteio ^= _sorteio << 13;
		_sorteio ^= _sorteio >> 17;
		_sorteio ^= _sorteio << 5;
		return (int)_sorteio / (float)int.MaxValue;
	}

	/// <summary>Uma amostra pela cadeia inteira: (ruido) -> banda -> teto macio -> banda de novo -> volume.</summary>
	private float Processar(double x)
	{
		if (!SemBandaDeTeste)
		{
			x = _alta2.Passar(_alta1.Passar(x));
			x = _baixa2.Passar(_baixa1.Passar(x));
		}
		x = Math.Tanh(x * Saturacao);
		if (!SemBandaDeTeste) x = _depois.Passar(x);
		return (float)(x * Volume);
	}

	/// <summary>A TRANSMISSAO ABRIU: o proximo quadro comeca com o estalo de estatica.</summary>
	public void Abrir() => _estaloRestante = VozLocal.TaxaDeSaida * MsDoEstaloDeAbrir / 1000;

	/// <summary>UM QUADRO DE VOZ VIRA VOZ DE RADIO, no lugar.</summary>
	public void Aplicar(short[] pcm, int amostras)
	{
		int doEstalo = VozLocal.TaxaDeSaida * MsDoEstaloDeAbrir / 1000;
		for (int i = 0; i < amostras; i++)
		{
			double x = pcm[i] / (double)short.MaxValue;
			if (_comChiado) x += Ruido() * NivelDoChiado;
			if (_estaloRestante > 0)
			{
				// O ESTALO CAI AO QUADRADO: comeca seco e some antes de a palavra acabar de entrar.
				float e = _estaloRestante-- / (float)doEstalo;
				x += Ruido() * NivelDoEstalo * e * e;
			}
			pcm[i] = (short)Math.Clamp((int)(Processar(x) * short.MaxValue), short.MinValue, short.MaxValue);
		}
	}

	/// <summary>
	/// A TRANSMISSAO FECHOU: enche <paramref name="destino"/> com o estalo de quem soltou o botao -- estatica que
	/// cai ate o silencio, pela MESMA banda (a memoria dos filtros segue de onde a voz parou, sem degrau).
	/// </summary>
	public void Fechar(short[] destino, int amostras)
	{
		_estaloRestante = 0;
		for (int i = 0; i < amostras; i++)
		{
			float e = 1f - i / (float)amostras;
			destino[i] = (short)Math.Clamp((int)(Processar(Ruido() * NivelDoEstalo * e * e) * short.MaxValue),
										   short.MinValue, short.MaxValue);
		}
	}

	/// <summary>
	/// **SO NO OUVIDO ESQUERDO.** O quadro mono vira o quadro estereo que o gerador toca: a voz no canal
	/// esquerdo (o `X` do `Vector2`) e ZERO no direito -- nao "baixinho", zero. E o pedido literal, e e o que
	/// separa de ouvido o radio (que vem de lugar nenhum, num lado so) da voz de perto (que tem posicao).
	/// </summary>
	public static void ParaOOuvidoEsquerdo(short[] mono, int amostras, Vector2[] estereo)
	{
		for (int i = 0; i < amostras; i++)
		{
			float v = mono[i] / (float)short.MaxValue;
			estereo[i] = new Vector2(v, NosDoisOuvidosDeTeste ? v : 0f);
		}
	}
}
