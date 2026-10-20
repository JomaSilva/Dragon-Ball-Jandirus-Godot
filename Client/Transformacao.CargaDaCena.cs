using Godot;

namespace Jandirus.Client;

// (2026-10-19) A AURA DE CARGA DURANTE A CINEMATICA DE FORMA. Arquivo proprio pra o `Transformacao.cs` so ganhar as chamadas: a do
// nascimento da cena (`_Ready`), a do fim dela (`_Process`) e a do `Soltar`.
public partial class Transformacao
{
	/// <summary>Esta cena calou a aura de carga do corpo e ainda nao a devolveu -- so quem calou devolve, e uma vez so.</summary>
	private bool _cargaCalada;

	/// <summary>
	/// A AURA DE CARGA DO CORPO SE CALA ENQUANTO A CENA DURA. O dono: *"durante cinematica de transformaçao tanto na primeira vez como
	/// quando vc n consegue se transformar instantaneamente (isso pra todas as formas) a aura de carga q vc tem caso esteja com ki acima
	/// de 100% é desativada ficando apenas a aura da cinematica, e dps q a cinematica acabar caso o ki ainda esteja acima de 100% a aura
	/// do ki acima de 100% volta normalmente"*.
	///
	/// SO EM CENA QUE CONTA UMA FORMA (`_forma != null`): a estreia e a encurtada de todas as formas, e o Oozaru, que tambem e forma --
	/// e cuja cena acende a aura base EMPRESTADA do node `Aura`: com a carga calada, e ela que aparece, que e a "aura da cinematica"
	/// dele. A FURIA FICA DE FORA, de proposito: em 2026-10-11 o dono tirou a chama vermelha dela pedindo *"apenas a aura normal por
	/// conta do ki acima de 100%"* -- a cena da raiva nao tem chama propria, e a aura que se ve nela E esta. As cenas do bio, da saga do
	/// Majin e da fusao tambem nao contam forma e continuam como estavam.
	///
	/// CALAR NAO E APAGAR: o estado da carga (o C, o excesso) continua sendo o que o servidor disse, guardado na `CargaVisual`; ela so
	/// deixa de DESENHAR enquanto houver cena por cima (`CargaVisual.CenaPorCima`). Por isso a volta nao pergunta nada a ninguem: se o
	/// Ki ainda estiver acima de 100% quando a cena acabar a chama volta sozinha, e se ele caiu no meio ela nao volta. O zumbido da
	/// carga tambem nao muda -- ele e do estado da tecla, e nao da aura.
	/// </summary>
	private void CalarACargaDoCorpo()
	{
		if (_forma == null || _cargaCalada) return;
		if (_alvo.GetNodeOrNull<CargaVisual>("Carga") is not { } carga) return;
		_cargaCalada = true;
		carga.CenaPorCima(true);
	}

	/// <summary>
	/// E A AURA DE CARGA VOLTA -- uma vez so, por quem calou. Sao duas portas, e as duas sao necessarias:
	///
	///   * o FIM DA CENA, no `_Process` (`FimDaCena.Sozinha`): a chama volta no MESMO quadro em que a cena acaba. Sem esta porta ela so
	///     voltaria no `_ExitTree` do `QueueFree` -- adiado pro fim do quadro --, e tudo o que olhasse o corpo nesse meio-tempo (o resto do
	///     quadro) veria um corpo com o Ki acima de 100% e sem aura, depois de a cena ja ter acabado;
	///   * o <see cref="Soltar"/>, ou seja TODO caminho de saida (o teto, o alvo sumindo, o `_ExitTree`), pela regra dos outros
	///     emprestimos da cena.
	/// </summary>
	private void DevolverACargaDoCorpo()
	{
		if (!_cargaCalada) return;
		_cargaCalada = false;
		if (IsInstanceValid(_alvo)) _alvo.GetNodeOrNull<CargaVisual>("Carga")?.CenaPorCima(false);
	}
}
