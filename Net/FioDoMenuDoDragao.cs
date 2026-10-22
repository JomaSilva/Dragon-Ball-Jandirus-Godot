using Jandirus.Core.Magic;
using LiteNetLib.Utils;

namespace Jandirus.Net;

/// <summary>
/// O <see cref="Protocol.S2C.MenuDoDragao"/> NO FIO -- escrito e lido NESTE arquivo, pelas duas pontas.
///
/// AS DUAS METADES MORAM JUNTAS de proposito: pacote lido um byte alem (ou aquem) do que foi escrito estoura ou
/// le lixo, e a unica defesa que nao depende de alguem lembrar e a escrita e a leitura estarem a um palmo uma da
/// outra. A bancada `--menudodragaoteste` atravessa o menu por aqui (escreve, le, compara a assinatura).
///
/// TODO TEXTO VIAJA CORTADO NO TETO DELE. O `GetString(max)` do LiteNetLib nao corta: texto maior que o teto
/// volta VAZIO, sem erro. Por isso quem escreve corta no mesmo numero com que quem le pergunta.
/// </summary>
public static class FioDoMenuDoDragao
{
	// (2026-10-22) O `MaxPedido` e o teto do argumento do verbo (`Protocol.MaxArgDeVerbo`), e nao os 120 de antes: o "Configurar o dragao"
	// leva a configuracao do set inteira (`Core/Magic/ConfigDaEstatua.cs`, ate ~210), e cortada ela abriria o menu da estatua com desejos
	// religados -- que o "Refazer" mandaria de volta, calado.
	public const int MaxId = 48, MaxVerbo = 32, MaxPedido = Protocol.MaxArgDeVerbo, MaxNome = 96, MaxDesc = 260, MaxMotivo = 300,
					 MaxPergunta = 96, MaxRotulo = 64, MaxNota = 120, MaxEstado = 320, MaxTitulo = 64;

	/// <summary>O teto de linhas do menu. A tabela do Shenron tem 20 (`Desejos.Todos`), mais os gestos.</summary>
	public const int MaxEntradas = 48;

	/// <summary>
	/// O teto de respostas da segunda etapa. Passou disso (um servidor com mais de 60 mortos de uma vez), a
	/// lista viaja cortada e quem ficou de fora ainda pode ser pedido pelo alvo MARCADO, que vai na frente.
	/// </summary>
	public const int MaxEscolhas = 60;

	private const byte BitPode = 1, BitAcao = 2, BitPerigo = 4, BitDireto = 8;
	private const byte BitMarcado = 2;

	private static string Cortar(string? s, int max)
	{
		if (string.IsNullOrEmpty(s)) return "";
		if (s.Length <= max) return s;
		// SEM PARTIR UM PAR SUBSTITUTO no meio: meio caractere e um texto que o outro lado nao decodifica.
		return char.IsHighSurrogate(s[max - 1]) ? s[..(max - 1)] : s[..max];
	}

	public static void Escrever(NetDataWriter w, MenuDoDragao m)
	{
		w.Put(m.Fechar);
		w.Put((byte)m.Dragao);
		if (m.Fechar) return;

		w.Put(m.Abrir);
		w.Put(m.DePe);
		w.Put(Cortar(m.Nome, MaxTitulo));
		w.Put(Cortar(m.Titulo, MaxTitulo));
		w.Put(Cortar(m.Estado, MaxEstado));
		w.Put((byte)Math.Clamp(m.Restam, 0, byte.MaxValue));
		w.Put((byte)Math.Clamp(m.Total, 0, byte.MaxValue));
		w.Put((ushort)Math.Clamp(m.Segundos, 0, ushort.MaxValue));
		w.Put(Cortar(m.Foco, MaxId));

		int n = Math.Min(m.Entradas.Count, MaxEntradas);
		w.Put((byte)n);
		for (int i = 0; i < n; i++)
		{
			EntradaDoDragao e = m.Entradas[i];
			w.Put(Cortar(e.Id, MaxId));
			w.Put(Cortar(e.Verbo, MaxVerbo));
			w.Put(Cortar(e.Pedido, MaxPedido));
			w.Put(Cortar(e.Nome, MaxNome));
			w.Put(Cortar(e.Desc, MaxDesc));
			w.Put((byte)((e.Pode ? BitPode : 0) | (e.Acao ? BitAcao : 0) | (e.Perigo ? BitPerigo : 0) | (e.Direto ? BitDireto : 0)));
			w.Put(Cortar(e.Motivo, MaxMotivo));
			w.Put(Cortar(e.Pergunta, MaxPergunta));

			int k = Math.Min(e.Escolhas.Count, MaxEscolhas);
			w.Put((byte)k);
			for (int j = 0; j < k; j++)
			{
				EscolhaDoDragao c = e.Escolhas[j];
				w.Put(Cortar(c.Pedido, MaxPedido));
				w.Put(Cortar(c.Rotulo, MaxRotulo));
				w.Put(Cortar(c.Nota, MaxNota));
				w.Put((byte)((c.Pode ? BitPode : 0) | (c.Marcado ? BitMarcado : 0)));
			}
		}
	}

	public static MenuDoDragao Ler(NetDataReader r)
	{
		var m = new MenuDoDragao { Fechar = r.GetBool(), Dragao = (QualDragao)r.GetByte() };
		if (m.Fechar) return m;

		m.Abrir = r.GetBool();
		m.DePe = r.GetBool();
		m.Nome = r.GetString(MaxTitulo);
		m.Titulo = r.GetString(MaxTitulo);
		m.Estado = r.GetString(MaxEstado);
		m.Restam = r.GetByte();
		m.Total = r.GetByte();
		m.Segundos = r.GetUShort();
		m.Foco = r.GetString(MaxId);

		int n = r.GetByte();
		for (int i = 0; i < n; i++)
		{
			var e = new EntradaDoDragao
			{
				Id = r.GetString(MaxId),
				Verbo = r.GetString(MaxVerbo),
				Pedido = r.GetString(MaxPedido),
				Nome = r.GetString(MaxNome),
				Desc = r.GetString(MaxDesc),
			};
			byte bits = r.GetByte();
			e.Pode = (bits & BitPode) != 0;
			e.Acao = (bits & BitAcao) != 0;
			e.Perigo = (bits & BitPerigo) != 0;
			e.Direto = (bits & BitDireto) != 0;
			e.Motivo = r.GetString(MaxMotivo);
			e.Pergunta = r.GetString(MaxPergunta);

			int k = r.GetByte();
			for (int j = 0; j < k; j++)
			{
				var c = new EscolhaDoDragao
				{
					Pedido = r.GetString(MaxPedido),
					Rotulo = r.GetString(MaxRotulo),
					Nota = r.GetString(MaxNota),
				};
				byte b = r.GetByte();
				c.Pode = (b & BitPode) != 0;
				c.Marcado = (b & BitMarcado) != 0;
				e.Escolhas.Add(c);
			}
			m.Entradas.Add(e);
		}
		return m;
	}
}
