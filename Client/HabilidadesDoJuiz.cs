using Jandirus.Core.Verbos;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-19) O BOTAO DA TRAVESSIA DO KING YEMMA (aba Skills) -- ir ao Outro Mundo e voltar pro lugar exato de onde saiu
/// (`GameServer.TravessiaDoJuiz.cs`). Como todo o `Habilidades`, este arquivo so REGISTRA: quem decide se o botao existe pra este
/// personagem e a linha `hab:juiz_outro_mundo` da tabela (`RequisitosDosVerbos`: o trono do King Yemma, ou a volta guardada de quem
/// o perdeu do lado de la), lida ao vivo -- o botao nasce na coroacao e some na destituicao sem relogar. E ele nao e permissao: o
/// servidor confere o trono no aperto.
/// </summary>
public static class HabilidadesDoJuiz
{
	public static void Registrar() =>
		Verbos.Registrar(new Verbo(
			TravessiaDoJuiz.Rotulo,
			Verbos.Skills,
			"O caminho do juiz entre os dois mundos. No mundo dos vivos ele te leva ao Outro Mundo, na mesa do Enma, onde as almas "
			+ "chegam -- e lá o Revive traz de volta quem merecer (a alma acorda em casa). No Outro Mundo, o mesmo botão te devolve ao "
			+ "lugar EXATO de onde você saiu.\n\nNão gasta energia. Não atravessa caído, preso nem em transe; quem pilota desembarca antes.",
			() => GameClient.Instance?.SendHabilidade(TravessiaDoJuiz.Verbo))
			{ Chave = RequisitosDosVerbos.PrefixoDeHabilidade + TravessiaDoJuiz.Verbo });
}
