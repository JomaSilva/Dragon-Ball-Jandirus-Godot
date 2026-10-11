using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// A LENHA, como este cliente a conhece -- o lado de ca do `S2C.Lenha`: as arvores que REBROTARAM e os TOCOS das
/// que cairam.
///
/// A arvore que alguem derrubou e cenario caido como qualquer outro, e chega pelo pacote de cenario (terra batida
/// no lugar dela). O TOCO que fica ali e a arvore que VOLTA nao desfazem isso: sao coisas novas, que o servidor poe
/// por cima do mundo e manda como listas (celula + tile da arvore). Ver `GameServer.Lenha.cs`.
///
/// GUARDADAS AQUI, e nao so desenhadas, pelo motivo escrito no <see cref="CenarioCaido"/> e nos <see cref="Blocos"/>:
/// o retrato chega no instante em que o servidor aceita o jogador (antes de o `World` existir) e a cada troca de
/// zona (antes de o chao novo montar). Quem aplica sao as camadas `RebrotasNaTela` e `TocosNaTela`, e so quando a
/// lista e do chao que esta montado (<see cref="ZonaDaLenha"/>).
/// </summary>
public partial class GameClient
{
	/// <summary>As rebrotas da zona, por celula: o tile com que cada uma se desenha (o mesmo da arvore que caiu).</summary>
	public readonly Dictionary<(int X, int Y), TileDeArvore> Rebrotas = [];

	/// <summary>
	/// Os tocos a vista da zona, pela celula em que a arvore caiu: o tile DA ARVORE -- e dele que o `TocosNaTela`
	/// tira o desenho do toco (a base do tronco dela, cortada).
	/// </summary>
	public readonly Dictionary<(int X, int Y), TileDeArvore> Tocos = [];

	/// <summary>
	/// DE QUE ZONA SAO AS LISTAS <see cref="Rebrotas"/> e <see cref="Tocos"/>. Vem no retrato. Depois de um
	/// `ZoneChanged` ela continua dizendo a zona ANTERIOR ate o retrato da nova chegar (no mesmo lote de pacotes) --
	/// e e por isso que quem desenha compara com a zona do cliente e com o chao montado antes de aplicar qualquer coisa.
	/// </summary>
	public ulong ZonaDaLenha { get; private set; }

	/// <summary>O retrato da lenha de uma zona chegou: as duas listas foram trocadas (ver <see cref="ZonaDaLenha"/>).</summary>
	public event Action? LenhaChegou;

	/// <summary>Uma arvore voltou a ficar de pe nesta celula (ja esta em <see cref="Rebrotas"/>).</summary>
	public event Action<int, int>? RebrotaNasceu;

	/// <summary>A rebrota desta celula saiu. `true` = foi derrubada agora (quem ve solta a poeira); `false` = sumiu calada.</summary>
	public event Action<int, int, bool>? RebrotaCaiu;

	/// <summary>Ficou um toco nesta celula (ja esta em <see cref="Tocos"/>).</summary>
	public event Action<int, int>? TocoNasceu;

	/// <summary>O toco desta celula saiu: a arvore rebrotou, ou construiram em cima.</summary>
	public event Action<int, int>? TocoSumiu;

	/// <summary>O `S2C.Lenha`, nos cinco modos (ver <see cref="Protocol.LenhaRetrato"/>).</summary>
	private void LerLenha(NetDataReader reader)
	{
		byte modo = reader.GetByte();

		// A ORDEM DE LEITURA E A DE ESCRITA: celula, e so entao o tile (ver `GameServer.PorArvoreNoFio`).
		((int X, int Y) Onde, TileDeArvore Tile) Arvore()
		{
			int x = reader.GetUShort(), y = reader.GetUShort();
			ushort fonte = reader.GetUShort(), ax = reader.GetUShort(), ay = reader.GetUShort();
			return ((x, y), new TileDeArvore(fonte, ax, ay));
		}

		switch (modo)
		{
			case Protocol.LenhaRetrato:
			{
				// O RETRATO SUBSTITUI AS LISTAS: todas as rebrotas e todos os tocos da zona, com a zona de que ele e.
				ulong zona = reader.GetULong();
				Rebrotas.Clear();
				for (int n = reader.GetInt(); n > 0; n--)
				{
					((int X, int Y) onde, TileDeArvore tile) = Arvore();
					Rebrotas[onde] = tile;
				}
				Tocos.Clear();
				for (int n = reader.GetInt(); n > 0; n--)
				{
					((int X, int Y) onde, TileDeArvore tile) = Arvore();
					Tocos[onde] = tile;
				}
				ZonaDaLenha = zona;
				LenhaChegou?.Invoke();
				break;
			}

			case Protocol.LenhaBrotou:
			{
				((int X, int Y) onde, TileDeArvore tile) = Arvore();
				Rebrotas[onde] = tile;
				RebrotaNasceu?.Invoke(onde.X, onde.Y);
				break;
			}

			case Protocol.LenhaCaiu:
			{
				int x = reader.GetUShort(), y = reader.GetUShort();
				bool derrubada = reader.GetBool();
				Rebrotas.Remove((x, y));
				RebrotaCaiu?.Invoke(x, y, derrubada);
				break;
			}

			case Protocol.LenhaToco:
			{
				((int X, int Y) onde, TileDeArvore tile) = Arvore();
				Tocos[onde] = tile;
				TocoNasceu?.Invoke(onde.X, onde.Y);
				break;
			}

			case Protocol.LenhaSemToco:
			{
				int x = reader.GetUShort(), y = reader.GetUShort();
				Tocos.Remove((x, y));
				TocoSumiu?.Invoke(x, y);
				break;
			}
		}
	}
}
