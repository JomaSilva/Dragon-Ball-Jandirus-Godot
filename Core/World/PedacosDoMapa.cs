using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Jandirus.Core.World;

/// <summary>Uma celula de tilemap ja resolvida: onde fica e que quadro do atlas desenha.</summary>
public readonly record struct CelulaDePedaco(short X, short Y, ushort Fonte, ushort Ax, ushort Ay);

/// <summary>
/// O CHAO DE UM MAPA PRE-FEITO, GUARDADO EM PEDACOS DE 64x64 -- escrito pelo conversor, lido pelo
/// cliente.
///
/// ============================ POR QUE ISTO EXISTE ============================
/// As celulas moravam dentro do proprio `.tscn`, num `tile_map_data` por camada. Entrar na Terra
/// custava 659 ms de PARSE (9,6 MB de texto decimal) e mais 708 ms no PRIMEIRO QUADRO -- porque um
/// `TileMapLayer` nao monta o desenho ao entrar na arvore, ele monta quando o renderizador pede, e
/// ai sao ~2,7 us por celula vezes 266 mil celulas. A janela congelava por tres segundos e nenhuma
/// medicao que comecasse e terminasse dentro do carregamento via isso.
///
/// A prova de que a culpa era o TAMANHO e nao o carregamento: o ESPACO, que e maior que qualquer
/// planeta, entra instantaneo -- ele nao tem cena, desenha so o que a camera alcanca (ver
/// `CeuDoEspaco`). Um mapa pre-feito passa a fazer o mesmo, e por isso as celulas precisam sair da
/// cena: enquanto estiverem la, o Godot as monta todas antes de alguem poder escolher.
/// =============================================================================
///
/// POR QUE UM ARQUIVO SO, LIDO DOS DOIS LADOS: o formato tem que casar byte a byte entre quem
/// escreve (Tools/AssetPipeline) e quem le (Client). Duas descricoes do mesmo layout em projetos
/// diferentes divergem no primeiro campo que alguem acrescentar -- e o sintoma seria um mapa vazio
/// sem erro nenhum, que e o pior tipo de defeito que este pipeline ja produziu (ver a nota do
/// formato 0 do TileMapLayer no MapConverter).
///
/// O LAYOUT (tudo little-endian):
///
///     "JPD1"                             4 bytes
///     uint16  lado                       tiles por lado de pedaco (64)
///     uint8   camadas
///       por camada: uint8 tamanho + nome em UTF-8
///     uint32  entradas                   uma por (pedaco, camada) NAO VAZIO
///       por entrada: int16 cx | int16 cy | uint8 camada | uint32 celulas
///     corpo: as celulas de cada entrada, na ordem do indice
///       por celula: int16 x | int16 y | uint16 fonte | uint16 ax | uint16 ay
///
/// A ALTERNATIVA DO TILE NAO ENTRA. O conversor sempre escreve 0 nela (ver `MapConverter`), e dez
/// bytes por celula em vez de doze sao 500 KB a menos so na Terra.
///
/// PEDACO VAZIO NAO OCUPA ENTRADA: um mapa de 500x500 tem 64 pedacos, mas a camada de objetos so
/// desenha em alguns deles. Indexar os vazios seria guardar o nada e ainda pagar por procura-lo.
/// </summary>
public sealed class PedacosDoMapa
{
	/// <summary>Bytes por celula no corpo. Ver o layout acima.</summary>
	public const int BytesPorCelula = 10;

	private const uint Assinatura = 0x3144504A;   // "JPD1" lido em little-endian

	/// <summary>
	/// O LADO PADRAO DE UM PEDACO, em tiles. 64 tiles = 2048 px, mais que uma tela inteira em
	/// qualquer zoom jogavel -- entao a camera nunca toca mais de 2x2 pedacos de uma vez.
	///
	/// Nao e um numero magico solto: ele viaja DENTRO do arquivo. Quem le usa o que estiver
	/// gravado, e trocar esta constante so muda o que a proxima conversao escreve.
	/// </summary>
	public const int LadoPadrao = 64;

	private readonly byte[] _corpo;
	private readonly Dictionary<long, (int Inicio, int Celulas)> _indice;

	public int Lado { get; }

	/// <summary>Os nomes das camadas, na MESMA ordem em que a cena as declara (Chao, Decor, Objetos).</summary>
	public string[] Camadas { get; }

	/// <summary>Quantas celulas o arquivo inteiro guarda -- so pra diagnostico.</summary>
	public int TotalDeCelulas { get; }

	/// <summary>
	/// A faixa de pedacos que o mapa ocupa, em coordenadas de pedaco e com o fim EXCLUSIVO.
	///
	/// Sai do proprio indice e nao da largura declarada da zona: o que interessa e onde ha
	/// desenho. Um mapa cuja borda direita e so vazio nao tem por que ter um pedaco pra ela.
	/// </summary>
	public int Cx0 { get; }
	public int Cy0 { get; }
	public int Cx1 { get; }
	public int Cy1 { get; }

	private PedacosDoMapa(int lado, string[] camadas, byte[] corpo,
						  Dictionary<long, (int, int)> indice, int total,
						  int cx0, int cy0, int cx1, int cy1)
	{
		Lado = lado;
		Camadas = camadas;
		_corpo = corpo;
		_indice = indice;
		TotalDeCelulas = total;
		Cx0 = cx0;
		Cy0 = cy0;
		Cx1 = cx1;
		Cy1 = cy1;
	}

	/// <summary>
	/// A chave de um pedaco. `cx` e `cy` cabem em 16 bits com sobra (um mapa de 500 tiles tem 8
	/// pedacos de lado) e o deslocamento os deixa positivos, entao a chave e monotona e nao
	/// depende de como o C# trata negativo em bit a bit.
	/// </summary>
	private static long Chave(int cx, int cy, int camada) =>
		((long)(cx + 32768) << 24) | ((long)(cy + 32768) << 8) | (uint)(byte)camada;

	/// <summary>Onde as celulas de um pedaco comecam no corpo. Falso = pedaco vazio.</summary>
	public bool Achar(int cx, int cy, int camada, out int inicio, out int celulas)
	{
		if (_indice.TryGetValue(Chave(cx, cy, camada), out (int Inicio, int Celulas) e))
		{
			inicio = e.Inicio;
			celulas = e.Celulas;
			return true;
		}
		inicio = 0;
		celulas = 0;
		return false;
	}

	/// <summary>
	/// A celula de indice <paramref name="i"/> de um pedaco que comeca em <paramref name="inicio"/>.
	///
	/// Le direto do `byte[]` de proposito: transformar o arquivo inteiro num vetor de structs no
	/// carregamento seria alocar 266 mil objetos pra usar alguns milhares por vez.
	/// </summary>
	public CelulaDePedaco Celula(int inicio, int i)
	{
		int p = inicio + i * BytesPorCelula;
		return new CelulaDePedaco(
			unchecked((short)(_corpo[p] | (_corpo[p + 1] << 8))),
			unchecked((short)(_corpo[p + 2] | (_corpo[p + 3] << 8))),
			(ushort)(_corpo[p + 4] | (_corpo[p + 5] << 8)),
			(ushort)(_corpo[p + 6] | (_corpo[p + 7] << 8)),
			(ushort)(_corpo[p + 8] | (_corpo[p + 9] << 8)));
	}

	// =====================================================================
	// LER
	// =====================================================================
	/// <summary>
	/// Le o arquivo inteiro. Devolve nulo quando o conteudo nao e um `.pedacos` -- quem chama
	/// decide se isso e um mapa sem chao ou um pipeline desatualizado.
	/// </summary>
	public static PedacosDoMapa? Ler(byte[] arquivo)
	{
		if (arquivo.Length < 11) return null;

		int p = 0;
		uint magica = (uint)(arquivo[0] | (arquivo[1] << 8) | (arquivo[2] << 16) | (arquivo[3] << 24));
		if (magica != Assinatura) return null;
		p = 4;

		int lado = arquivo[p] | (arquivo[p + 1] << 8);
		p += 2;
		if (lado <= 0) return null;

		int nCamadas = arquivo[p++];
		var nomes = new string[nCamadas];
		for (int i = 0; i < nCamadas; i++)
		{
			int len = arquivo[p++];
			nomes[i] = Encoding.UTF8.GetString(arquivo, p, len);
			p += len;
		}

		int entradas = arquivo[p] | (arquivo[p + 1] << 8) | (arquivo[p + 2] << 16) | (arquivo[p + 3] << 24);
		p += 4;

		var indice = new Dictionary<long, (int, int)>(entradas);
		var pares = new (long Chave, int Celulas)[entradas];
		int total = 0;
		int cx0 = int.MaxValue, cy0 = int.MaxValue, cx1 = int.MinValue, cy1 = int.MinValue;
		for (int i = 0; i < entradas; i++)
		{
			short cx = unchecked((short)(arquivo[p] | (arquivo[p + 1] << 8)));
			short cy = unchecked((short)(arquivo[p + 2] | (arquivo[p + 3] << 8)));
			byte camada = arquivo[p + 4];
			int celulas = arquivo[p + 5] | (arquivo[p + 6] << 8)
						  | (arquivo[p + 7] << 16) | (arquivo[p + 8] << 24);
			p += 9;
			pares[i] = (Chave(cx, cy, camada), celulas);
			total += celulas;
			if (cx < cx0) cx0 = cx;
			if (cy < cy0) cy0 = cy;
			if (cx > cx1) cx1 = cx;
			if (cy > cy1) cy1 = cy;
		}
		if (entradas == 0) { cx0 = cy0 = 0; cx1 = cy1 = -1; }

		// O CORPO E O RESTO DO ARQUIVO e os offsets saem da ORDEM do indice -- gravar cada offset
		// seria guardar uma soma que o leitor ja sabe fazer, e um numero a mais pra desalinhar.
		int corpo = p;
		int caminhando = corpo;
		foreach ((long chave, int celulas) in pares)
		{
			indice[chave] = (caminhando, celulas);
			caminhando += celulas * BytesPorCelula;
		}
		if (caminhando > arquivo.Length) return null;

		return new PedacosDoMapa(lado, nomes, arquivo, indice, total, cx0, cy0, cx1 + 1, cy1 + 1);
	}

	// =====================================================================
	// ESCREVER
	// =====================================================================
	/// <summary>
	/// Agrupa as celulas de cada camada em pedacos de <paramref name="lado"/> tiles e grava.
	///
	/// DENTRO DO PEDACO A ORDEM E POR LINHA (y, depois x). O pintor do cliente atravessa o pedaco
	/// aos poucos, parando quando o orcamento do quadro acaba, e retomando de um indice -- uma
	/// ordem estavel e o que faz o pedaco chegar de cima pra baixo em vez de salpicado.
	/// </summary>
	public static void Escrever(string caminho, int lado, IReadOnlyList<string> nomes,
								IReadOnlyList<List<CelulaDePedaco>> camadas)
	{
		var baldes = new Dictionary<long, List<CelulaDePedaco>>();
		var ordem = new List<(long Chave, int Cx, int Cy, int Camada)>();

		for (int c = 0; c < camadas.Count; c++)
			foreach (CelulaDePedaco cel in camadas[c])
			{
				int cx = Piso(cel.X, lado), cy = Piso(cel.Y, lado);
				long chave = Chave(cx, cy, c);
				if (!baldes.TryGetValue(chave, out List<CelulaDePedaco>? lista))
				{
					lista = [];
					baldes[chave] = lista;
					ordem.Add((chave, cx, cy, c));
				}
				lista.Add(cel);
			}

		foreach (List<CelulaDePedaco> lista in baldes.Values)
			lista.Sort((a, b) => a.Y != b.Y ? a.Y - b.Y : a.X - b.X);

		using var fs = new FileStream(caminho, FileMode.Create, FileAccess.Write);
		using var w = new BinaryWriter(fs, new UTF8Encoding(false));

		w.Write(Assinatura);
		w.Write((ushort)lado);
		w.Write((byte)nomes.Count);
		foreach (string n in nomes)
		{
			byte[] b = Encoding.UTF8.GetBytes(n);
			w.Write((byte)b.Length);
			w.Write(b);
		}

		w.Write((uint)ordem.Count);
		foreach ((long chave, int cx, int cy, int camada) in ordem)
		{
			w.Write((short)cx);
			w.Write((short)cy);
			w.Write((byte)camada);
			w.Write((uint)baldes[chave].Count);
		}

		foreach ((long chave, int _, int _, int _) in ordem)
			foreach (CelulaDePedaco cel in baldes[chave])
			{
				w.Write(cel.X);
				w.Write(cel.Y);
				w.Write(cel.Fonte);
				w.Write(cel.Ax);
				w.Write(cel.Ay);
			}
	}

	/// <summary>Divisao pra baixo -- `x / lado` em C# arredonda pro zero e erraria o lado negativo.</summary>
	private static int Piso(int v, int lado) => v >= 0 ? v / lado : (v - lado + 1) / lado;
}
