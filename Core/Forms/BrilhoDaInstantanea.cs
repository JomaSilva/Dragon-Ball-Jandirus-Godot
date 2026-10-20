namespace Jandirus.Core.Forms;

/// <summary>
/// O BRILHO DA TRANSFORMACAO INSTANTANEA (2026-10-19) -- o que o terceiro degrau (`DegrauDeCena.Nenhuma`, maestria acima de
/// <see cref="Cinematicas.MaestriaQueDispensaCena"/>) ganhou no lugar da cena. O dono: *"todas as transformaçoes q o personagem
/// consegue se transformar instantaneamente (ssj com maestria acima de 50% por exemplo e outras formas) ao se transformar o personagem
/// da um pequeno brilho branco (parecido com o efeito de hit ao tomar dano de um golpe) mas o personagem da um brilho rapido e quando
/// o brilho sai ele ja ta transformado, pq atualmente virar instantaneamente ta muito seco"*.
///
/// O CANAL E O DO SOCO (`CharacterVisual.Banhar`, o `flash` do `Personagem.gdshader`): o corpo inteiro -- pele, roupa, cabelo, rabo,
/// colada -- lava junto e escoa LINEAR a partir do pico. A forma ja entra vestida no quadro do pico, por baixo do branco (ver
/// `World.BrilharNaInstantanea`): quando o brilho sai ele ja esta transformado, que e a frase do pedido. Sem o achatamento e sem o
/// empurrao do golpe -- virar nao e apanhar.
///
/// OS NUMEROS SAO MEUS, e o dono so disse "pequeno" e "rapido": o soco lava a 0,85 por 0,15 s; aqui o pico e o branco CHEIO (a
/// silhueta chapada esconde a troca do cabelo no quadro em que ela acontece) e o escoamento e o dobro do soco, pra ler como brilho e
/// nao como pancada. A COR e branca e nao a da forma: foi o pedido, e o banho NA COR da forma ja e outra coisa (o
/// <see cref="Efeito.BanhoDeCor"/> das cenas de surto).
///
/// ARQUIVO PROPRIO e nao tres constantes no `Cinematicas.cs`: quem le isto e codigo novo (o `World.BrilhoDaInstantanea.cs` e a bancada
/// `--diagbrilhoforma`), e arquivo novo que dependesse de linha nova num arquivo velho nao compilaria sozinho contra o repo de antes.
/// </summary>
public static class BrilhoDaInstantanea
{
	/// <summary>Quanto o branco leva pra escoar do pico ate o corpo limpo, em segundos.</summary>
	public const double Segundos = 0.3;

	/// <summary>Quanto o corpo se mistura no branco no pico, de 0 a 1 (1 = a silhueta chapada).</summary>
	public const float Lavagem = 1f;

	/// <summary>A cor do brilho, em hexa sem `#`: o cliente a converte (o Core nao conhece `Godot.Color`).</summary>
	public const string Cor = "ffffff";
}
