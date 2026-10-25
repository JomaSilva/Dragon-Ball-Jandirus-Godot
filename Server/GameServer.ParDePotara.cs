using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-23) O PAR DE BRINCOS POTARA QUE SE DA -- o gesto de dar (o botao "Dar ao alvo marcado" da mochila), o par que se gasta na
/// fusao e as duas portas que fazem um par valer UMA fusao. A regra (quanto o cargo repoe, de quem o par se gasta) e o porque estao em
/// `Core/Items/ParDePotara.cs`; o par chega com o cargo pelo `ReconciliarOsBrincos` (`GameServer.Fusao.cs`), o funil de sempre.
///
/// O dono: *"Kaioshin entrega o par"* -- os jogadores reclamavam que a Potara feita com a Materialize nao fundia (era roupa) e que a de
/// verdade so funcionava na mao do Kaioshin (sumia em 1 s de quem a recebia).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--potarateste` e robo `--diagpotara`): o par volta pro cofre em 1 s da mochila de quem nao e Kaioshin -- o
	/// jogo de antes, o "sumiu da minha mao" do relato. Lido pelo `ReconciliarOsBrincos`. Falso em jogo, sempre.
	/// </summary>
	internal static bool ParVoltaAoCofreDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--potarateste`): o aceite de um convite que saiu dos brincos NAO confere se eles ainda estao com quem
	/// convidou -- jogar o par, da-lo a outro e fundir mesmo assim: um par, duas fusoes. Lido pela <see cref="ConviteDosBrincosSemOsBrincos"/>.
	/// </summary>
	internal static bool AceiteSemOsBrincosDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--potarateste`): os brincos saem da mao (largar, guardar, dar) no meio do puxao e da cena da fusao, e a
	/// fusao acontece sem gastar nada. Lido pela <see cref="BrincosPresosNaFusao"/>.
	/// </summary>
	internal static bool BrincosSaemNaFusaoDeTeste;

	/// <summary>
	/// O CARGO DESTE CORPO ENTREGA OS BRINCOS? (Hoje: e Kaioshin.) PELA TABELA DE INSIGNIAS e nao pelo kit de skills: o kit e uma lista de
	/// TYPEPATHS, e a bancada `--portasteste` afirma que todos existem no catalogo de skills -- um id de item enfiado la a deixaria vermelha
	/// por um motivo que nao tem nada a ver com ela.
	/// </summary>
	private bool CargoDaOsBrincos(ServerPlayer pl) =>
		Jandirus.Core.Ranks.DadivaDeCargo.ItensDe(CargoDe(pl.Conta))
			.Contains(CatalogoDeItens.BrincosPotara, StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// O BOTAO "DAR AO ALVO MARCADO" DOS BRINCOS (`item_dar Potara_Earrings`): o par sai da mochila de quem da e entra na de quem esta
	/// marcado (o duplo clique, como o "Jogar"). Quem da pode ser qualquer um que tenha o par -- o Kaioshin, ou quem o recebeu dele e quer
	/// passa-lo adiante.
	///
	/// AS RECUSAS, na ordem em que o jogador as entenderia: quem da esta caido, e a fera ou esta numa mente (as portas de largar um item,
	/// `PodeLargar`); ninguem marcado; o marcado nao e gente (NPC, clone, corpo largado -- a mesma pergunta da fusao, `EhPessoa`), esta caido,
	/// longe (o alcance de jogar, <see cref="ParDePotara.TilesPraDar"/>) ou de mochila cheia. Nada sai da mochila antes de o par caber na outra.
	/// </summary>
	private void DarOsBrincos(ServerPlayer pl)
	{
		if (pl.Ficha.KO || CaidoPelaMorte(pl)) { Avisar(pl, "caído, você não dá nada."); return; }
		if (SemAsRedeas(pl)) { Avisar(pl, "a fera não dá nada."); return; }
		if (NaMente(pl)) { Avisar(pl, "aqui dentro nada é real: não há o que dar."); return; }

		ServerPlayer? alvo = Marcado(pl);
		if (alvo == null || alvo == pl) { Avisar(pl, "marque quem vai receber os brincos (duplo clique na pessoa)."); return; }
		if (!EhPessoa(alvo)) { Avisar(pl, $"{alvo.Name} não é um jogador: os brincos Potara só vão pras mãos de gente de verdade."); return; }
		if (alvo.Ficha.KO || CaidoPelaMorte(alvo)) { Avisar(pl, $"{alvo.Name} está caído: espere ele se levantar."); return; }
		if (Fusao.DistanciaEmTilesDoDm(pl.Pos, alvo.Pos, ZoneCollision.TileSize) > ParDePotara.TilesPraDar)
		{
			Avisar(pl, $"{alvo.Name} está longe demais: os brincos se dão a até {ParDePotara.TilesPraDar} passos.");
			return;
		}
		if (pl.Mochila.Quantos(CatalogoDeItens.BrincosPotara) <= 0) return;
		if (alvo.Mochila.Guardar(CatalogoDeItens.BrincosPotara) > 0) { Avisar(pl, $"a mochila de {alvo.Name} está cheia."); return; }

		pl.Mochila.Tirar(CatalogoDeItens.BrincosPotara);
		MandarMochila(pl);
		MandarMochila(alvo);

		Avisar(pl, $"você dá um par de brincos Potara a {alvo.Name}.");
		// O KAIOSHIN FICA SABENDO QUANDO VEM O PROXIMO: sem a frase, o par que nao volta no tique seguinte pareceria defeito.
		if (CargoDaOsBrincos(pl) && pl.Mochila.Quantos(CatalogoDeItens.BrincosPotara) <= 0)
		{
			double horas = ParDePotara.HorasAteOProximo(pl.Ficha.ParDePotaraEm, NowMs());
			Avisar(pl, horas < 0.05 ? "o cargo te entrega outro par agora."
								   : $"o cargo te entrega outro par em {horas:0.#} h (um par por dia).");
		}
		Avisar(alvo, $"{pl.Name} te dá um par de brincos Potara! Marque alguém (duplo clique) e use \"Jogar no alvo marcado\" na mochila "
				   + "(tecla I) pra oferecer a fusão. O par se gasta quando a fusão acontece.");
		GD.Print($"[server] {pl.Name} DEU um par de brincos Potara a {alvo.Name}");
	}

	/// <summary>
	/// O PAR SE GASTA NA FUSAO QUE ACONTECEU -- chamado do `Fundir` (Potara), depois da ultima trava. So o de quem nao e Kaioshin
	/// (<see cref="ParDePotara.SeGastaNaFusao"/>), e so se ha um: as bancadas que fundem sem passar pela mochila nao tem par nenhum, e a
	/// fusao delas continua a mesma.
	/// </summary>
	private void GastarOsBrincosNaFusao(ServerPlayer dono)
	{
		if (!ParDePotara.SeGastaNaFusao(CargoDaOsBrincos(dono))) return;
		if (dono.Mochila.Tirar(CatalogoDeItens.BrincosPotara) <= 0) return;
		MandarMochila(dono);
		Avisar(dono, "o par de brincos Potara se gasta na fusão.");
	}

	/// <summary>
	/// OS BRINCOS NAO SAEM DA MAO A CAMINHO DA FUSAO: no puxao, na cena e enquanto ela dura, largar, guardar ou dar o par e recusado. E a
	/// metade que sobra do "um par, uma fusao": o aceite confere o par (<see cref="ConviteDosBrincosSemOsBrincos"/>), e entre o aceite e o
	/// `Fundir` (que o gasta) passam o puxao e a cena -- quem largasse o par ali fundiria e deixaria o par no chao pra outra fusao. "Jogar"
	/// passa: quem o recusa, com a frase de sempre, e o portao da fusao. Devolve se recusou.
	/// </summary>
	private bool BrincosPresosNaFusao(ServerPlayer pl, ItemDef def, string acao)
	{
		if (def.Id != CatalogoDeItens.BrincosPotara || acao == "jogar" || !OcupadoPorFusao(pl.Id) || BrincosSaemNaFusaoDeTeste) return false;
		Avisar(pl, "os brincos Potara estão em uso na fusão: espere ela acabar.");
		return true;
	}

	/// <summary>
	/// O CONVITE QUE SAIU DOS BRINCOS E QUEM CONVIDOU NAO ESTA MAIS COM ELES? -- a porta do aceite (`ResponderAoConvite`). So vale pro convite
	/// marcado no `OferecerOsBrincos`: as bancadas que convidam direto pela porta da fusao, sem item, continuam convidando.
	/// </summary>
	private static bool ConviteDosBrincosSemOsBrincos(PedidoDeFusao p, ServerPlayer quem) =>
		p.PelosBrincos && !AceiteSemOsBrincosDeTeste && quem.Mochila.Quantos(CatalogoDeItens.BrincosPotara) <= 0;
}
