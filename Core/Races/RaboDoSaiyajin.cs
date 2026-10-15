using Jandirus.Core.Forms;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Races;

/// <summary>
/// **O RABO QUE NAO VOLTA** -- a perda definitiva do rabo do Saiyajin que nao e da linhagem Primal.
///
/// ============================ NAO E PORTE: E REGRA DO DONO (2026-10-12) ============================
/// *"saiyajins nao primal, quando poder passa de 20k e eles perdem o rabo, o rabo nunca mais cresce, mesmo em
/// maquina de regeneraçao, nao ter rabo tem suas vantagens"*. No DM o rabo arrancado volta por qualquer
/// `RegrowLimb` (`mobparts.dm:316-319`, o `Grow_Tail()` de `:108`): a regeneracao da raca, a morte, o tanque.
///
/// A LEITURA (minha, avisada ao dono): sao DUAS condicoes -- o poder de BASE ja passou de
/// <see cref="PoderEmQueORaboNaoVolta"/> E o rabo foi perdido. Abaixo disso o rabo arrancado volta como sempre
/// (o Goku crianca); acima, quem o perde fica sem ele. A linhagem Primal (a do Oozaru e do SSJ4, que PRECISA do
/// rabo) nunca perde de vez.
///
/// AS VANTAGENS DE NAO TER RABO ja estavam portadas e continuam valendo: sem rabo o Saiyajin treina a 1,25 em vez
/// de 0,5 (`tailgain`, `statsaiyan.dm:267-290` -- duas vezes e meia mais rapido), a lua cheia nao lhe tira as
/// redeas, e ninguem o pega pelo rabo (<see cref="Combat.RaboPreso"/>). O preco e o de la: sem Oozaru.
/// ====================================================================================================
///
/// A MARCA MORA NO MEMBRO (`BodyPart.Perdido`), e e ela que as duas portas de devolver membro consultam
/// (`Body.Regenerar` e `Body.Restaurar`): nao ha lista de "quem pode devolver o rabo" pra alguem esquecer de
/// atualizar quando nascer uma cura nova.
/// </summary>
public static class RaboDoSaiyajin
{
	/// <summary>O poder de BASE a partir do qual o rabo perdido nao volta.</summary>
	public const double PoderEmQueORaboNaoVolta = 20_000;

	/// <summary>O nome do membro, como o `Body.Novo` o cria.</summary>
	public const string Membro = "Rabo";

	/// <summary>DEFEITO INJETADO (bancada `--v020teste`): o rabo perdido volta sempre, como no DM. Falso em jogo, sempre.</summary>
	public static bool PerdaDefinitivaDesligadaDeTeste;

	/// <summary>
	/// PERDER O RABO AGORA E PRA SEMPRE? Saiyajin de linhagem nao-Primal com o poder de base no limiar ou acima.
	/// Quem nao e Saiyajin puro (o meio-sangue, o NPC de outra raca que o roteiro pos com rabo) tem a linhagem
	/// vazia e cai na mesma regra: so a linhagem Primal fica de fora.
	/// </summary>
	public static bool PerdaEhDefinitiva(Fighter f) =>
		!PerdaDefinitivaDesligadaDeTeste
		&& f.SaiyanLineage != Oozaru.LinhagemPrimal
		&& f.BP >= PoderEmQueORaboNaoVolta;

	/// <summary>O que o dono do corpo le quando o rabo se vai de vez.</summary>
	public const string AvisoDaPerda =
		"o seu rabo não volta mais: o corpo já é forte demais pra refazê-lo. Sem ele você treina bem mais "
		+ "rápido, a lua cheia não o domina e ninguém o agarra pelo rabo -- mas o Oozaru ficou pra trás.";

	/// <summary>
	/// (2026-10-15) QUANTO TEMPO O TOCO DO RABO PERDIDO DE VEZ FICA COMO FERIDA: *"so durante uns minutos apos ter o rabo
	/// arrancado q esse efeito de ferimento fica, passou de uns 10 minutos (...) ele deve sumir o efeito de ferido"*.
	/// Dez minutos de relogio, contados pelo servidor (`GameServer.TickDaFeridaDoRabo`).
	/// </summary>
	public const double SegundosDaFerida = 600;

	/// <summary>O que o dono do corpo le quando o toco cicatriza.</summary>
	public const string AvisoDaCicatriz = "o toco do seu rabo cicatriza -- ele não volta, mas também não sangra mais.";
}
