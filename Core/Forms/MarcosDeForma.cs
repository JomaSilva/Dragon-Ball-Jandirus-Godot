using Jandirus.Core.Stats;

namespace Jandirus.Core.Forms;

/// <summary>O que a conferencia dos marcos de forma fez numa ficha, pra quem chamou poder contar ao jogador.</summary>
/// <param name="Multiplicador">O novo multiplicador de ganho, ou 0 se ele nao subiu (nada era devido, ou o que era devido nao passa do que a ficha ja tem).</param>
/// <param name="PelaFormaDeAgora">
/// O marco que o levantou e o da forma em que o corpo ESTA -- a estreia que acabou de acontecer. Falso = divida
/// antiga: uma forma em que ele ja esteve e cujo marco a ficha nao tinha (o save de antes de os marcos serem ligados).
/// </param>
public readonly record struct MarcoConcedido(double Multiplicador, bool PelaFormaDeAgora);

/// <summary>
/// OS MARCOS DE GANHO DAS FORMAS -- quem os concede.
///
/// ============================ O ACHADO (2026-10-11) ============================
/// A tabela de marcos (`Stats.Milestones`) veio do DM inteira e as chamadas que a alimentam nao: virar
/// Super Saiyajin, Legendary ou Blue nunca subiu o ganho de ninguem neste port. O dono mandou ligar.
/// ===============================================================================
///
/// ============================ ONDE O ORIGINAL CONCEDE, E O QUE MUDA AQUI ============================
/// No DM o marco vem DENTRO da proc da transformacao, logo depois de a forma valer (`ssj=1` e, na linha de
/// baixo, `bp_milestone_reach("ssj")` -- `supersaiyanbuff.dm:340-341`). A chamada roda em TODA
/// transformacao e so a primeira faz diferenca: e o `if(tag in bp_milestones_done) return` do
/// `LinearGain.dm:40`. Ou seja o marco e de quem ENTROU na forma, e nao de quem a destravou.
///
/// Aqui a pergunta e a mesma, feita POR ESTADO: "este corpo esta nesta forma, ou ja esteve
/// (<see cref="EstadoDeForma.EstreiaVista"/>), e a ficha ainda nao tem o marco dela?". Quem pergunta e o
/// laco de fichas do servidor, cinco vezes por segundo (`GameServer.ConferirOsMarcosDeForma`).
///
/// POR ESTADO E NAO POR GANCHO pelo motivo que o `GameServer.ConferirMarcosDoBp` ja escreveu: ha seis
/// jeitos de um corpo entrar numa forma pela primeira vez (a tecla C, a tecla de forma, o despertar com o
/// mestre, o `admin_forma`, a queda do Oozaru Dourado em SSJ4 e a morte do bio), e um gancho em cada um
/// sao seis chances de esquecer o setimo. O DM faz por gancho, e esqueceu tres (ver `FormaDef.Marco`).
///
/// E O RETROATIVO SAI DE GRACA, pela mesma linha: quem ja tinha estreado uma forma antes desta data loga
/// com a estreia no save e sem o marco na ficha -- a primeira conferencia paga, uma vez.
///
/// A FORMA DE AGORA CONTA MESMO SEM ESTREIA, e o caso e um so: sair do Oozaru Dourado dominado poe o corpo
/// em SSJ4 SEM gastar a estreia (a cena fica devendo pro primeiro C). No DM ele entrou na forma, e o marco
/// e dele ali.
/// ====================================================================================================
/// </summary>
public static class MarcosDeForma
{
	/// <summary>
	/// DEFEITO INJETADO (bancada --marcosdeformateste): so a forma em que o corpo esta AGORA conta -- as
	/// estreias antigas nao. O personagem de antes dos marcos loga e fica sem o que ja tinha conquistado.
	/// </summary>
	public static bool SoAFormaDeAgoraDeTeste;

	/// <summary>
	/// AS FORMAS QUE CONCEDEM MARCO, tiradas do catalogo uma vez. E o catalogo quem diz
	/// (<see cref="FormaDef.Marco"/>); aqui nao ha lista de ids.
	/// </summary>
	public static readonly FormaDef[] ComMarco = [.. Catalogo.Todas.Where(d => d.Marco.Length > 0)];

	/// <summary>
	/// CONFERE E PAGA o que as formas devem a esta ficha. Idempotente: marco ja pago nao volta.
	///
	/// LACO CRU, SEM ALOCAR: isto roda no tique de fichas pra todo corpo do mundo. Quem nunca se
	/// transformou sai na primeira linha.
	/// </summary>
	public static MarcoConcedido Conferir(EstadoDeForma est, Fighter f)
	{
		if (est.NaBase && est.EstreiaVista.Count == 0) return default;

		double subiu = 0;
		bool deAgora = false;
		foreach (FormaDef d in ComMarco)
		{
			bool agora = est.Atual == d.Id;
			if (!agora && (SoAFormaDeAgoraDeTeste || !est.EstreiaVista.Contains(d.IdRede))) continue;
			if (f.bp_milestones_done.Contains(d.Marco)) continue;

			// O `ReachMilestone` devolve o multiplicador NOVO quando ele subiu, e 0 quando a etiqueta
			// entrou sem passar do que a ficha ja tinha (o SSJ de quem ja tem o `godki`).
			double m = f.ReachMilestone(d.Marco);
			if (m <= 0) continue;
			subiu = m;
			deAgora = agora;
		}
		return new MarcoConcedido(subiu, deAgora);
	}
}
