using Godot;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A SUPERNOVA DO CHEFE (v0.3.0, item 22) -- o ultimato de destruicao com corpo na tela. Os prazos e o porque estao em
/// <see cref="Jandirus.Core.Npc.Supernova"/>; aqui mora o que o SERVIDOR faz com o chefe enquanto ela cresce e cai.
///
/// ============================ O CHEFE FICA IMUNE E PARADO PELA PORTA QUE JA EXISTIA ============================
/// "Nesse momento ele fica imune aos ataques": e o que a cinematica de transformacao ja faz com quem se transforma -- o
/// `CenaSegundos` (lido pelo `EmCena`) prende o corpo (`PodeMexerOCorpo`) e o torna intocavel (`CombatState.EmCinematica`,
/// pendurado no `PrepararCombate`). A Supernova escreve o MESMO prazo, do primeiro brilho ate a explosao: nenhum golpe,
/// tiro ou tecnica o alcanca, e ele nao anda nem soca enquanto o sol esta na mao.
/// ==========================================================================================================
///
/// QUEM CHAMA E O ULTIMATO DA SAGA (`GameServer.Sagas.CorrerOUltimato`): ele comeca a Supernova no lugar da carga de 30 s
/// do DM, segura o prazo ate a explosao e so entao acende a destruicao (`GanchoDeDestruicao`), que tira o chefe do mundo
/// -- e e a saida dele que a tela le como "o sol bateu no chao".
/// </summary>
public sealed partial class GameServer
{
	/// <summary>DEFEITO INJETADO (`--v030teste`): a Supernova nao protege o chefe -- ele concentra o sol tomando golpe, como qualquer um.</summary>
	internal static bool SupernovaSemImunidadeDeTeste;

	/// <summary>
	/// O CHEFE ERGUE A SUPERNOVA: o corpo passa a `Ocupacao.Supernova` (e o que a tela le pra desenhar o sol), e fica
	/// parado e intocavel ate a explosao. Sem efeito em quem ja esta concentrando.
	/// </summary>
	internal void ComecarASupernova(ServerPlayer chefe)
	{
		if (chefe.SupernovaDesde != 0) return;
		chefe.SupernovaDesde = NowMs();
		chefe.Moving = false;
		// O MESMO PRAZO DA CENA, ate a explosao e um segundo de folga: o chefe sai do mundo NA explosao (ver o cabecalho),
		// e um prazo que vencesse antes soltaria o corpo no ultimo instante da queda.
		if (!SupernovaSemImunidadeDeTeste)
			chefe.CenaSegundos = Math.Max(chefe.CenaSegundos, Jandirus.Core.Npc.Supernova.SegundosAteAExplosao + 1);
		Falar(chefe, Protocol.Fala.Emote, "ergue um dedo ao céu -- e um sol começa a nascer sobre ele!");
		GD.Print($"[server] {chefe.Name} ergueu a SUPERNOVA ({Jandirus.Core.Npc.Supernova.SegundosAteAExplosao:0} s ate a explosao)");
	}

	/// <summary>
	/// A SUPERNOVA SE DESFAZ ANTES DA EXPLOSAO -- o servidor esvaziou no meio da carga (o ultimato espera gente pra reagir)
	/// ou o chefe caiu por um caminho que a imunidade nao cobre (o verb de admin). O corpo volta a ser livre; a tela apaga o
	/// sol sem estouro.
	/// </summary>
	private void AcabarASupernova(ServerPlayer chefe)
	{
		if (chefe.SupernovaDesde == 0) return;
		chefe.SupernovaDesde = 0;
		chefe.CenaSegundos = 0;
		GD.Print($"[server] a Supernova de {chefe.Name} se desfez antes da explosao");
	}
}
