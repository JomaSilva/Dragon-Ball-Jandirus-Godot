using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// O BILLS E O WHIS NO CLIENTE (dono, 2026-10-11) -- as duas coisas que so o servidor sabe deles:
///
///   * ONDE ELES MORAM (`S2C.CasaDeBills`). O endereco so viaja depois de revelado -- o Bills o conta quando e
///     derrubado na Terra. Antes disso o pacote e um `false`, e a carta estelar nao tem o que marcar.
///   * QUAL DOS DOIS ESTA AO ALCANCE DA MINHA TECLA E, e com que menu (`S2C.DivinoPerto`). O snapshot diz onde os
///     corpos estao, e nao qual deles tem menu: e o molde do cadaver ao alcance (`S2C.Cadaver`).
///
/// O QUE NAO MORA AQUI, e nao viaja: o PODER deles. O trilhao do Bills, os 15% da visita e o triplo do treino sao
/// regra do servidor -- nenhum dos dois pacotes leva numero de poder, e a bancada (`--billsteste`) cobra isso byte a
/// byte. Quem desenha o que esta aqui e a carta estelar (`MenuJogo.Bills.cs`) e o menu da tecla E (`DivinosNaTela`).
/// </summary>
public partial class GameClient
{
	/// <summary>A casa do Bills como o servidor contou por ultimo. <c>Revelada</c> falso = ninguem a conhece ainda.</summary>
	/// <param name="Sx">A celula do sistema na carta (o "Sistema Sx:Sy" que o Bills fala).</param>
	/// <param name="Orbita">O numero da orbita, o mesmo que a tela do sistema mostra.</param>
	/// <param name="Semente">A semente do mundo: e por ela que a carta reconhece o planeta.</param>
	public readonly record struct CasaDeBillsVista(bool Revelada, int Sx, int Sy, int Orbita, ulong Semente);

	public CasaDeBillsVista CasaDeBills { get; private set; }

	/// <summary>O corpo divino que a minha tecla E alcanca agora. Zero = nenhum.</summary>
	public int DivinoPerto { get; private set; }

	/// <summary>A chave do menu dele em `Core.Tech.Interacoes` (o convite, a saida do treino ou a do desafio).</summary>
	public string TipoDoDivinoPerto { get; private set; } = "";

	/// <summary>O nome que vai no alto do menu e na dica "[E] ...". Vem no pacote, como o do cadaver.</summary>
	public string NomeDoDivinoPerto { get; private set; } = "";

	/// <summary>`S2C.CasaDeBills`: `bool revelada` e, so se revelada, `int sx`, `int sy`, `byte orbita`, `ulong semente`.</summary>
	private void LerCasaDeBills(NetDataReader reader)
	{
		if (!reader.GetBool()) { CasaDeBills = default; return; }

		int sx = reader.GetInt();
		int sy = reader.GetInt();
		int orbita = reader.GetByte();
		ulong semente = reader.GetULong();
		CasaDeBills = new CasaDeBillsVista(true, sx, sy, orbita, semente);
	}

	/// <summary>`S2C.DivinoPerto`: `int corpo` (0 = nenhum), `string tipo`, `string nome`.</summary>
	private void LerDivinoPerto(NetDataReader reader)
	{
		DivinoPerto = reader.GetInt();
		TipoDoDivinoPerto = reader.GetString();
		NomeDoDivinoPerto = reader.GetString();
	}
}
