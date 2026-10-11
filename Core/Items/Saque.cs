namespace Jandirus.Core.Items;

/// <summary>
/// O SAQUE DE QUEM ESTA NOCAUTEADO -- o `mob/verb/Steal()` do original (`Code/Grabbing.dm:1-29`), pedido no
/// item 9b do playtest de 2026-10-10: *"jogadores podem pegar itens de personagens nocauteados (jogadores e
/// NPCs) interagindo com o corpo. Padrao: siga o BYOND se ele tinha isso; senao, so itens do inventario, nao
/// os equipados."*
///
/// ============================ O QUE O ORIGINAL FAZ, E O QUE VEIO ============================
///   * O ALVO: um mob NOCAUTEADO a um passo (`get_step(src,dir)`), com o ladrao de pe (`!KO`), podendo lutar
///     (`canfight`) e fora da fera (`!Apeshit`). Aqui o passo e o alcance da tecla E
///     (`Tech.Interacoes.Alcance`), que e por onde o gesto entra.
///   * O QUE SAI: os `obj/items` do alvo que NAO estao equipados (`suffix=="*Equipped*"` fica). UM por vez.
///     Aqui "equipado" e o que esta EM USO sem sair da mochila -- o scouter ligado e os pesos no corpo. A
///     roupa vestida nunca esteve na mochila (ver `RoupaGuardada`), entao ela ja fica de fora sozinha.
///   * QUEM VE: todo mundo a vista (`to_chat(view(usr), "[usr] steals [item] from [M]!")`). Aqui e um emote.
///
/// ============================ O ZENNI NAO VEIO, E ISSO E O ORIGINAL ============================
/// A lista do DM oferece "Zenni", mas o ramo que o levaria compara a variavel errada (`else if(choice ==
/// "Zenni")`, onde `choice` e o MOB escolhido um passo antes): ele nunca dispara, e ninguem jamais roubou
/// zenni naquele jogo. O que se porta e o que o original FAZ. Se o dono quiser o roubo de zenni, e uma
/// decisao de jogo (quanto, de quem, com que limite) e nao um conserto.
/// </summary>
public static class Saque
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--saqueteste`): o que esta em uso tambem sai -- o scouter ligado e os pesos
	/// no corpo entram na lista. Lido pela propria <see cref="OQueDaPraLevar"/>.
	/// </summary>
	public static bool EquipadoTambemSaiDeTeste;

	/// <summary>O verbo, nas duas pontas. Sem argumento ele pede a lista; com o id de um item, leva um.</summary>
	public const string Verbo = "saquear";

	/// <summary>
	/// Segundos entre dois itens levados pelo MESMO ladrao. No original cada roubo sao duas caixas de dialogo
	/// (quem, o que); aqui e um clique, e sem este respiro uma mochila inteira sairia num segundo.
	/// </summary>
	public const double SegundosEntreSaques = 1.0;

	/// <summary>Este item esta em uso (e por isso fica com o dono)?</summary>
	public static bool EmUso(string id, bool scouterLigado, bool comPesos) =>
		(scouterLigado && id == CatalogoDeItens.Scouter) || (comPesos && id == CatalogoDeItens.Pesos);

	/// <summary>
	/// O QUE DA PRA LEVAR desta mochila: tudo, menos o que esta em uso. A ordem e a da mochila -- e a que o
	/// dono dela ve.
	/// </summary>
	public static List<Pilha> OQueDaPraLevar(Inventario mochila, bool scouterLigado, bool comPesos)
	{
		var leva = new List<Pilha>();
		foreach (Pilha p in mochila.Pilhas)
		{
			if (p.Quantidade <= 0) continue;
			if (!EquipadoTambemSaiDeTeste && EmUso(p.Id, scouterLigado, comPesos)) continue;
			leva.Add(p);
		}
		return leva;
	}
}
