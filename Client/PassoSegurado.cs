namespace Jandirus.Client;

/// <summary>
/// **O PASSO QUE NAO SOME NUM TOQUE** -- a pose de andar dura pelo menos <see cref="MinimoDoPasso"/> depois de
/// comecar.
///
/// ============================ O CORPO DESLIZAVA ============================
/// O dono (2026-10-12): *"se o personagem anda mt devagar ou o player fica dando toquinhos nos botoes de andar, o
/// personagem ele desliza ao inves de fazer a animaçao correta de andar"*. A pose de andar era ligada e desligada
/// pelo deslocamento de CADA quadro: um toque de 50 ms na tecla ligava a animacao, mostrava o primeiro quadro
/// dela (que e o corpo de pe) e a desligava antes do segundo -- o corpo andava meio tile sem mexer a perna. Toques
/// em sequencia recomecavam a animacao do zero a cada um, e ela nunca saia do primeiro quadro.
///
/// A REGRA: quem comecou a andar mostra o passo por um minimo de tempo, contado do COMECO. Um toque isolado
/// mostra um passo; toques em sequencia emendam numa caminhada continua; e quem andou mais que o minimo para na
/// hora em que para (nao ha "marcha no lugar" depois de uma caminhada de verdade).
///
/// SO A POSE: o que vai no fio (`InputAndando`) continua sendo o deslocamento real -- o servidor le aquele bit
/// pra arremessar quem esta agarrado e pra saber quem esta parado.
/// ===========================================================================
/// </summary>
public struct PassoSegurado
{
	/// <summary>Quanto dura, no minimo, a pose de andar: o bastante pro segundo quadro da folha aparecer.</summary>
	public const double MinimoDoPasso = 0.30;

	/// <summary>DEFEITO INJETADO (bancada `--diagv020`): a pose volta a seguir o deslocamento quadro a quadro. Falso em jogo, sempre.</summary>
	public static bool SemSegurarDeTeste;

	private const double Parado = -1;

	/// <summary>Ha quanto tempo a pose de andar esta de pe. <see cref="Parado"/> = nao esta.</summary>
	private double _desdeOComeco;

	public PassoSegurado() => _desdeOComeco = Parado;

	/// <summary>A pose deste quadro: anda se o corpo se deslocou, ou se o passo que comecou ainda nao durou o minimo.</summary>
	public bool Segurar(bool andando, double delta)
	{
		if (andando)
		{
			_desdeOComeco = _desdeOComeco < 0 ? 0 : _desdeOComeco + delta;
			return true;
		}
		if (_desdeOComeco < 0) return false;

		_desdeOComeco += delta;
		if (_desdeOComeco >= MinimoDoPasso || SemSegurarDeTeste)
		{
			_desdeOComeco = Parado;
			return false;
		}
		return true;
	}

	/// <summary>Corta o passo agora (o corpo caiu, foi arremessado, entrou numa cena): nao ha minimo a cumprir.</summary>
	public void Soltar() => _desdeOComeco = Parado;
}
