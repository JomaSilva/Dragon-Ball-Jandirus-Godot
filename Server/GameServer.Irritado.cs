using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// O IRRITADO no servidor (dono, 2026-10-21): apanhar de um INIMIGO numa luta de verdade acende o degrau
/// abaixo da raiva comum. A pergunta do corpo e do Core (<see cref="GatilhoDoIrritado"/>); aqui fica quem
/// pode acender, o aviso, e a estreia da forma pelo mesmo <see cref="DespertarPelaRaiva"/> da raiva comum.
///
/// NAO CONTA, e isto fica anotado pro dono confirmar (sobretudo o membro perdido por autodano): treino e spar
/// (amigo declarado, ou habitante pacifico), a mente, o treino do Bills/Whis, bicho, e autodano (gravidade,
/// Kaio-ken, sobrecarga, estrela -- esses caminhos nem chamam <see cref="TalvezIrritar"/>). NPC e chefe nao
/// ficam irritados: eles tem as etapas deles. So jogador. E nem o cadaver (2026-10-23): o golpe que acabou de matar
/// nao irrita o morto.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (<c>--irritadoteste</c>): treino, spar, mente, Bills e bicho TAMBEM irritam -- a exclusao
	/// sumiu. Lido por <see cref="PorQueEsteGolpeNaoIrrita"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool IrritadoContaOQueNaoELutaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (<c>--irritadoteste</c>): o prazo do irritado nao reinicia no golpe seguinte. Lido por
	/// <see cref="AcenderOIrritado"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool IrritadoNaoRenovaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (<c>--irritadoteste</c> e <c>--diagirritado</c>): ficar irritado nao avisa no chat. Lido por
	/// <see cref="AcenderOIrritado"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool IrritadoMudoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (<c>--raivateste</c>, familia 14): o cadaver tambem se irrita -- o golpe que acaba de matar acende o
	/// irritado no morto. Lido por <see cref="PorQueEsteGolpeNaoIrrita"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool CadaverIrritadoDeTeste;

	/// <summary>
	/// UM GOLPE DE OUTRO CORPO JA FERIO ESTE. Chamado depois do dano, nos funis que tem algoz (soco, tiro, explosao,
	/// tecnica). Autodano nao passa por aqui.
	/// </summary>
	internal void TalvezIrritar(ServerPlayer vitima, ServerPlayer agressor, bool perdeuMembro)
	{
		if (vitima.Combate == null) return;
		if (!GatilhoDoIrritado.Acende(vitima.Combate.Corpo, vitima.Ficha.HP, perdeuMembro)) return;
		if (PorQueEsteGolpeNaoIrrita(vitima, agressor) is { } motivo)
		{
			GD.Print($"[server] {vitima.Name}: o irritado nao acendeu ({motivo})");
			return;
		}
		AcenderOIrritado(vitima);
	}

	/// <summary>
	/// Por que este golpe NAO e luta de verdade, ou nulo se e. A exclusao inteira mora nesta funcao pra a bancada
	/// (e a mutacao) pegarem a linha, e nao um <c>if</c> espalhado em cada funil de dano.
	/// </summary>
	internal string? PorQueEsteGolpeNaoIrrita(ServerPlayer vitima, ServerPlayer agressor)
	{
		if (IrritadoContaOQueNaoELutaDeTeste) return null;
		if (vitima.Peer == null) return "npc";
		// O CADAVER: desde 2026-10-23 o golpe que acabou de matar segue pelo `ResolverDesfecho` (a morte dele e contada la), e
		// sem esta linha o morto ainda acenderia o irritado -- o aviso e o despertar de forma num corpo caido no chao.
		if (EhCadaver(vitima) && !CadaverIrritadoDeTeste) return "cadaver";
		if (ReferenceEquals(agressor, vitima) || agressor.Id == vitima.Id) return "autodano";
		if (NaMente(vitima)) return "mente";
		if (TreinoDivinoEntre(vitima, agressor)) return "bills";
		if (EhAnimal(vitima) || EhAnimal(agressor)) return "bicho";
		// Habitante pacifico socando no spar da rotina nao e briga. Anotado: o jogo nao tem um modo "spar" de jogador
		// alem da declaracao de amigo (a mesma porta da raiva, Convivio.AlgozEhInimigo).
		if (agressor.Papel is { Pacifico: true }) return "spar";
		if (agressor.Assinatura.Length > 0 && !vitima.Social.AlgozEhInimigo(agressor.Assinatura)) return "treino";
		return null;
	}

	/// <summary>
	/// Acende a janela do irritado (120 s, reiniciada a cada golpe que acende) pela mao unica
	/// <see cref="AcenderJanelaDeRaiva"/>. Nao rebaixa quem ja esta com raiva ou furia: a janela de cima continua
	/// mandando. O aviso e a estreia so saem na ERUPCAO (de calmo pra irritado).
	/// </summary>
	private void AcenderOIrritado(ServerPlayer pl)
	{
		NivelDeRaiva antes = NivelDaRaivaDe(pl);
		if (IrritadoNaoRenovaDeTeste && antes != NivelDeRaiva.Nenhuma) return;
		AcenderJanelaDeRaiva(pl, NivelDeRaiva.Irritado, NowMs() + (long)(SegundosDeRaiva * 1000));
		GD.Print($"[server] {pl.Name}: IRRITADO" + (antes == NivelDeRaiva.Nenhuma ? "" : "  <- prolongado"));
		if (antes != NivelDeRaiva.Nenhuma) return;
		if (!IrritadoMudoDeTeste)
			Avisar(pl, "você está irritado. Mais um pouco e a raiva toma conta.", Protocol.Fala.AvisoDeRaiva);
		// A estreia e a da raiva comum: o mesmo plano, a mesma cena de forma. Sem salto de Ki e sem cena de furia.
		DespertarPelaRaiva(pl);
	}

	/// <summary>Quantos membros de fora ja estao decepados -- o antes/depois de um funil que pode arrancar mais de um.</summary>
	private static int MembrosDeForaPerdidos(CombatState c) =>
		c.Corpo.Partes.Count(p => GatilhoDoIrritado.EhMembroDeFora(p) && p.Decepado);

	/// <summary>
	/// PALCO DO ROBO <c>--diagirritado</c>: o host (Saiyajin) fica irritado pela mao de producao e estreia o SSJ1 se o
	/// poder pagar a porta x2,5. Devolve o degrau e a forma em que ele ficou.
	/// </summary>
	internal (NivelDeRaiva Raiva, string Forma) PalcoDoIrritado(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return (NivelDeRaiva.Nenhuma, "");
		// Acima de qualquer limiar pessoal do SSJ1 vezes 2,5 (a porta de fabrica e 1,5 mi; o sorteio nao passa de ~1,3x).
		pl.Ficha.BP = 20_000_000;
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		AcenderOIrritado(pl);
		MandarPoderEmUso(pl);
		MandarFicha(pl);
		return (NivelDaRaivaDe(pl), pl.Forma.Atual);
	}

	/// <summary>A forma em que este corpo esta agora -- o robo espera o SSJ1 aparecer.</summary>
	internal string PalcoDoIrritadoForma(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? pl.Forma.Atual : "";
}
