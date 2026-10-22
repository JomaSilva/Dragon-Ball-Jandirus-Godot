using LiteNetLib.Utils;

namespace Jandirus.Net;

/// <summary>Os bits do pacote <see cref="Protocol.S2C.PoderEmUso"/>.</summary>
[Flags]
public enum BitsDoPoderEmUso : byte
{
	Nenhum = 0,

	/// <summary>
	/// O poder expresso esta travado em 5 AGORA, e ninguem sente este corpo (`PoderEmUso.Escondido`): por estar
	/// relaxado, ou por uma tecnica de sumir (a invisibilidade, o Sneak, o embate de Zanzoken).
	/// </summary>
	Escondido = 1,

	/// <summary>Relaxado (`Fighter.relaxado`): o corpo soltou o proprio poder. Vem sempre junto do <see cref="Escondido"/>.</summary>
	Relaxado = 2,

	/// <summary>(2026-10-15) Com raiva: a raiva COMUM (`NivelDeRaiva.Comum`). O HUD escreve "COM RAIVA" embaixo da vida.</summary>
	ComRaiva = 4,

	/// <summary>(2026-10-15) Furioso: a raiva EXTREMA (`NivelDeRaiva.Extrema`). O HUD escreve "FURIOSO".</summary>
	Furioso = 8,

	/// <summary>(2026-10-21) Irritado: o degrau abaixo da raiva comum. O HUD escreve "IRRITADO".</summary>
	Irritado = 16,
}

/// <summary>
/// QUANTO DO PROPRIO PODER O MEU CORPO ESTA USANDO, e se ele esta relaxado ou escondido -- o corpo do pacote
/// <see cref="Protocol.S2C.PoderEmUso"/>: `byte porcento` (1 a 100, a barra) + `byte bits`.
///
/// E o que a tela precisa pra dizer "usando 40% do poder", "relaxado", "poder escondido" -- e mais nada.
/// **NAO LEVA NUMERO DE PODER**: a porcentagem e do PROPRIO poder em uso (a regra principal do BP deixa: e
/// razao sobre si mesmo, e sem scouter os dois lados dela nao chegam ao cliente por caminho nenhum). A regra
/// mora em `Core.Stats.PoderEmUso`; quem monta isto e o `GameServer.PoderEmUso.cs`.
/// </summary>
public readonly record struct PoderEmUsoState(byte Porcento, BitsDoPoderEmUso Bits)
{
	/// <summary>O estado de quem nunca mexeu em nada: o poder inteiro, a vista.</summary>
	public static readonly PoderEmUsoState Cheio = new(100, BitsDoPoderEmUso.Nenhum);

	public bool Escondido => (Bits & BitsDoPoderEmUso.Escondido) != 0;
	public bool Relaxado => (Bits & BitsDoPoderEmUso.Relaxado) != 0;
	public bool ComRaiva => (Bits & BitsDoPoderEmUso.ComRaiva) != 0;
	public bool Furioso => (Bits & BitsDoPoderEmUso.Furioso) != 0;
	public bool Irritado => (Bits & BitsDoPoderEmUso.Irritado) != 0;

	/// <summary>Nada fora do padrao: 100% e a vista. E quando o HUD nao escreve linha nenhuma.</summary>
	/// (A raiva nao conta: ela tem a linha dela no HUD, e um corpo com raiva usando o poder inteiro e a vista continua
	/// sem a linha do PODER.)
	public bool Padrao => Porcento >= 100 && (Bits & (BitsDoPoderEmUso.Escondido | BitsDoPoderEmUso.Relaxado)) == 0;

	public void Write(NetDataWriter w)
	{
		w.Put(Porcento);
		w.Put((byte)Bits);
	}

	public static PoderEmUsoState Read(NetDataReader r) => new(r.GetByte(), (BitsDoPoderEmUso)r.GetByte());
}
