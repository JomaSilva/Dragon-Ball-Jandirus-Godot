namespace Jandirus.Client;

/// <summary>
/// O SORTEIO DO PYTHON, NUMERO A NUMERO -- pra a abertura do jogo sortear as MESMAS estrelas e as MESMAS faiscas do
/// titulo animado do trailer.
///
/// A receita (`Trailer/bruto/t2/titulo_anim.py`) faz `random.seed(7)` e tira dali as 260 estrelas do fundo e as 70
/// faiscas do impacto. O `random` do Python e um Mersenne Twister (MT19937) com tres portas que a receita usa:
/// `randrange`, `choice` e `uniform`. Esta classe e esse gerador e essas tres portas, com a mesma conta do CPython
/// (`Modules/_randommodule.c` e `Lib/random.py`) -- semeada com o mesmo 7, entrega a mesma fila de numeros, e a foto
/// da abertura cai em cima do quadro do trailer estrela por estrela.
///
/// CONFERIDO contra o Python da maquina (3.14): com a semente 7, as quatro primeiras palavras sao 1390851128,
/// 4071050724, 647892279 e 1695753998; a primeira estrela cai em (663, 308) com brilho 200; a primeira faisca sai com
/// angulo 2,6005071810704288, pressa 1024,168142750896 e tamanho 4,884329068297141. A bancada `--diagabertura` repete
/// a conferencia.
///
/// SEM GODOT de proposito: e conta pura, e assim se confere fora do jogo.
/// </summary>
public sealed class SorteioDaReceita
{
	private const int N = 624, M = 397;

	private readonly uint[] _mt = new uint[N];
	private int _i = N;

	/// <param name="semente">O inteiro do `random.seed(...)` (nao negativo, de ate 32 bits).</param>
	public SorteioDaReceita(uint semente)
	{
		// `random.seed(inteiro)` do CPython = `init_by_array` com o inteiro partido em palavras de 32 bits (aqui, uma).
		_mt[0] = 19650218u;
		for (int k = 1; k < N; k++) _mt[k] = 1812433253u * (_mt[k - 1] ^ (_mt[k - 1] >> 30)) + (uint)k;

		int i = 1;
		for (int k = N; k > 0; k--)
		{
			_mt[i] = (_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1664525u)) + semente;   // + chave[0] + 0
			if (++i >= N) { _mt[0] = _mt[N - 1]; i = 1; }
		}
		for (int k = N - 1; k > 0; k--)
		{
			_mt[i] = (_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1566083941u)) - (uint)i;
			if (++i >= N) { _mt[0] = _mt[N - 1]; i = 1; }
		}
		_mt[0] = 0x80000000u;
	}

	/// <summary>A proxima palavra de 32 bits do gerador (o `genrand_uint32` do CPython).</summary>
	public uint Palavra()
	{
		if (_i >= N) Girar();
		uint y = _mt[_i++];
		y ^= y >> 11;
		y ^= (y << 7) & 0x9d2c5680u;
		y ^= (y << 15) & 0xefc60000u;
		y ^= y >> 18;
		return y;
	}

	private void Girar()
	{
		for (int k = 0; k < N; k++)
		{
			uint y = (_mt[k] & 0x80000000u) | (_mt[(k + 1) % N] & 0x7fffffffu);
			_mt[k] = _mt[(k + M) % N] ^ (y >> 1) ^ ((y & 1u) != 0 ? 0x9908b0dfu : 0u);
		}
		_i = 0;
	}

	/// <summary>`random.random()`: uma fracao de 53 bits em [0, 1).</summary>
	public double Fracao()
	{
		uint a = Palavra() >> 5, b = Palavra() >> 6;
		return (a * 67108864.0 + b) * (1.0 / 9007199254740992.0);
	}

	/// <summary>`random.uniform(a, b)`.</summary>
	public double Entre(double a, double b) => a + (b - a) * Fracao();

	/// <summary>
	/// `random.randrange(n)` -- e o `random.choice` de uma lista de <paramref name="n"/> itens e o item de indice
	/// `Abaixo(n)`. Tira os bits que cabem `n` e joga fora o que passar, como o `_randbelow_with_getrandbits`.
	/// </summary>
	public int Abaixo(int n)
	{
		int bits = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)n);   // o `n.bit_length()`
		uint r;
		do r = Palavra() >> (32 - bits);
		while (r >= (uint)n);
		return (int)r;
	}
}
