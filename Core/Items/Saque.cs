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
/// ============================ (2026-10-12) A ROUPA DO CORPO TAMBEM SAI -- REGRA DO DONO ============================
/// *"deveria dar pra pegar a roupa dos npcs e players q estao desmaiados"*, e a armadura junto: *"se o npc estiver
/// usando uma armadura (como a maioria dos saiyajins) ainda assim vai dar pra roubar se vc o nocautear"*.
///
/// No DM a roupa equipada fica (`suffix=="*Equipped*"`). Aqui ela entra na lista do saque DEPOIS da mochila, uma
/// linha por peca vestida, e sai do corpo do caido pra mochila do ladrao como a peca guardada de sempre
/// (`RoupaGuardada`). A linha vai MARCADA (<see cref="NoCorpo"/>) pra as duas pontas saberem que ela esta no corpo e
/// nao na mochila -- o caido pode ter a mesma peca nos dois lugares.
///
/// ============================ (2026-10-13) O ZENNI E O RECURSO SAEM -- REGRA DO DONO ============================
/// *"tb ter como roubar recursos e zeni de nocauteados (npc ou player)"*.
///
/// A lista do DM ja oferecia "Zenni", e o ramo que o levaria nunca disparou: compara a variavel errada (`else
/// if(choice == "Zenni")`, onde `choice` e o MOB escolhido um passo antes). Ninguem jamais roubou zenni naquele
/// jogo -- mas o que o ramo FARIA esta escrito, e e o que veio: **tudo o que o caido carrega**, de uma vez
/// (`zenni+=M.zenni`, `M.zenni=0`, `Grabbing.dm:25-28`), com o aviso pra quem esta a vista. O recurso e o mesmo
/// gesto com o outro saldo (no DM recurso era zeni). O que esta no BANCO nao esta com ele, e nao sai.
///
/// A carteira entra na lista por duas linhas proprias (<see cref="Zeni"/> e <see cref="Recursos"/>), que nao sao
/// item nenhum: a quantia viaja a parte, no fim do `S2C.Saque`. A carcaca de um bicho nao tem carteira.
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

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v020teste`): a roupa do corpo nao entra na lista -- o saque de antes de
	/// 2026-10-12. Lido pela propria <see cref="PecasQueDaPraLevar"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool RoupaDoCorpoFicaDeTeste;

	/// <summary>
	/// A MARCA, na frente do id, de que a peca esta NO CORPO do caido. O id que vem depois dela e o da peca
	/// guardada (`Roupa|...` ou `Armadura|...`), que e o que cai na mochila de quem leva.
	/// </summary>
	public const string NoCorpo = "@";

	/// <summary>
	/// AS DUAS LINHAS DA CARTEIRA do caido no verbo: `saquear $zeni` leva todo o zeni que ele carrega, e
	/// `saquear $recursos` todo o recurso. O `$` nao aparece em id de item nenhum.
	/// </summary>
	public const string Zeni = "$zeni", Recursos = "$recursos";

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--v021teste` e `--diagv021`): a carteira do caido fica com ele -- o saque de antes de
	/// 2026-10-13. Lido pela propria <see cref="CarteiraQueDaPraLevar"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool CarteiraFicaDeTeste;

	/// <summary>
	/// O QUE DA PRA ROUBAR DA CARTEIRA de quem esta caido: tudo o que ele carrega, em quantia inteira (`Grabbing.dm:27`).
	/// Zero nos dois pra carcaca de bicho.
	/// </summary>
	public static (double Zeni, double Recursos) CarteiraQueDaPraLevar(double zeni, double recursos, bool carcaca) =>
		carcaca || CarteiraFicaDeTeste ? (0, 0) : (Math.Max(Math.Floor(zeni), 0), Math.Max(Math.Floor(recursos), 0));

	/// <summary>A linha do saque de uma peca vestida.</summary>
	public static string IdDaVestida(Appearance.PecaDeRoupa peca) => NoCorpo + RoupaGuardada.De(peca).Id;

	/// <summary>Esta linha do saque e uma peca do corpo? Devolve o id da peca guardada que ela vira.</summary>
	public static bool EhDoCorpo(string idDoSaque, out string idDaPeca)
	{
		bool doCorpo = idDoSaque.StartsWith(NoCorpo, StringComparison.Ordinal);
		idDaPeca = doCorpo ? idDoSaque[NoCorpo.Length..] : idDoSaque;
		return doCorpo;
	}

	/// <summary>
	/// ESTA PECA FICA NO CORPO, mesmo com o dono caido? So a do Beerus: e a roupa do NPC Bills, desenhada pro corpo
	/// DELE (outra folha), e o dono a tirou do que o jogador veste.
	/// </summary>
	public static bool FicaNoCorpo(string nomeDoArquivo) =>
		nomeDoArquivo.Contains("Beerus", StringComparison.OrdinalIgnoreCase);

	/// <summary>AS PECAS DO CORPO QUE DA PRA LEVAR, na ordem em que estao vestidas -- uma linha marcada por peca.</summary>
	public static List<Pilha> PecasQueDaPraLevar(IReadOnlyList<Appearance.PecaDeRoupa> vestindo)
	{
		var leva = new List<Pilha>();
		if (RoupaDoCorpoFicaDeTeste) return leva;
		foreach (Appearance.PecaDeRoupa peca in vestindo)
			if (!FicaNoCorpo(Appearance.VisualCatalog.NomeDoArquivo(peca.Caminho)))
				leva.Add(new Pilha(IdDaVestida(peca), 1));
		return leva;
	}

	/// <summary>
	/// Este item esta em uso (e por isso fica com o dono)? (2026-10-18) O peso em uso e O VESTIDO (`Fighter.pesoVestido`, vazio = nenhum), e
	/// nao todo peso: cada um e um item com estado -- ver `PesoDeTreino`.
	/// </summary>
	public static bool EmUso(string id, bool scouterLigado, string pesoVestido) =>
		(scouterLigado && id == CatalogoDeItens.Scouter) || (pesoVestido.Length > 0 && id == pesoVestido);

	/// <summary>
	/// O QUE DA PRA LEVAR desta mochila: tudo, menos o que esta em uso. A ordem e a da mochila -- e a que o
	/// dono dela ve.
	/// </summary>
	public static List<Pilha> OQueDaPraLevar(Inventario mochila, bool scouterLigado, string pesoVestido)
	{
		var leva = new List<Pilha>();
		bool pesoFicou = false;
		foreach (Pilha p in mochila.Pilhas)
		{
			if (p.Quantidade <= 0) continue;
			if (!EquipadoTambemSaiDeTeste && EmUso(p.Id, scouterLigado, pesoVestido))
			{
				// O PESO VESTIDO E UMA UNIDADE: um gemeo dele (o mesmo id, noutro slot) pode sair. O scouter ligado fica inteiro, como sempre.
				if (p.Id != pesoVestido || pesoFicou) { if (p.Id == pesoVestido) leva.Add(p); continue; }
				pesoFicou = true;
				if (p.Quantidade > 1) leva.Add(p with { Quantidade = p.Quantidade - 1 });
				continue;
			}
			leva.Add(p);
		}
		return leva;
	}
}
