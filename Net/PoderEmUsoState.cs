using LiteNetLib.Utils;

namespace Jandirus.Net;

/// <summary>Os bits do pacote <see cref="Protocol.S2C.PoderEmUso"/>.</summary>
[Flags]
public enum BitsDoPoderEmUso : byte
{
	Nenhum = 0,

	/// <summary>O poder expresso esta travado pelo sigilo AGORA (o efeito: `Fighter.isconcealed`).</summary>
	Escondido = 1,

	/// <summary>O `Conceal_Power` esta ligado por vontade de quem joga (e nao so uma tecnica de sumir).</summary>
	PorVontade = 2,

	/// <summary>Os cinco segundos entre ligar e desligar o sigilo ainda correm (`Power Control.dm:78`).</summary>
	SigiloEmEspera = 4,

	/// <summary>Relaxado: o poder em uso esta no piso de 1% (`Power Control.dm:48`).</summary>
	Relaxado = 8,
}

/// <summary>
/// QUANTO DO PROPRIO PODER O MEU CORPO ESTA USANDO, e se ele o esconde -- o corpo do pacote
/// <see cref="Protocol.S2C.PoderEmUso"/>: `byte porcento` (1 a 100) + `byte bits`.
///
/// E o que a tela precisa pra dizer "usando 40% do poder", "relaxado", "escondendo o poder" -- e mais nada.
/// **NAO LEVA NUMERO DE PODER**: a porcentagem e do PROPRIO poder em uso (a regra principal do BP deixa: e
/// razao sobre si mesmo, e sem scouter os dois lados dela nao chegam ao cliente por caminho nenhum). A regra
/// mora em `Core.Stats.PoderEmUso`; quem monta isto e o `GameServer.PoderEmUso.cs`.
/// </summary>
public readonly record struct PoderEmUsoState(byte Porcento, BitsDoPoderEmUso Bits)
{
	/// <summary>O estado de quem nunca mexeu em nada: o poder inteiro, a vista.</summary>
	public static readonly PoderEmUsoState Cheio = new(100, BitsDoPoderEmUso.Nenhum);

	public bool Escondido => (Bits & BitsDoPoderEmUso.Escondido) != 0;
	public bool PorVontade => (Bits & BitsDoPoderEmUso.PorVontade) != 0;
	public bool SigiloEmEspera => (Bits & BitsDoPoderEmUso.SigiloEmEspera) != 0;
	public bool Relaxado => (Bits & BitsDoPoderEmUso.Relaxado) != 0;

	/// <summary>Nada fora do padrao: 100% e a vista. E quando o HUD nao escreve linha nenhuma.</summary>
	public bool Padrao => Porcento >= 100 && !Escondido;

	public void Write(NetDataWriter w)
	{
		w.Put(Porcento);
		w.Put((byte)Bits);
	}

	public static PoderEmUsoState Read(NetDataReader r) => new(r.GetByte(), (BitsDoPoderEmUso)r.GetByte());
}
