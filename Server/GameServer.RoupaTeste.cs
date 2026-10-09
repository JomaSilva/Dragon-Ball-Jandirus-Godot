using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Items;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--roupateste` -- A ROUPA QUE SE TIRA, SE GUARDA E SE TROCA (dono, 2026-10-08).
///
/// O pedido: *"a roupa q vc escolhe ao criar deveria aparecer no inventario pra ter a opcao de tirar ela e
/// trocar por outro caso queira"*.
///
/// ============================ O QUE ELA MEDE, E POR ONDE ============================
/// Tudo passa pelo `ComandoDeItem`, que e o funil do pacote que o botao da mochila manda (`C2S.Verbo` com
/// `item_vestir` / `item_despir`): a bancada nao chama `VestirRoupa` nem `TirarRoupa` direto, porque o que
/// estava em duvida e o caminho inteiro -- o id da peca atravessar o canal de itens (que parte o argumento
/// na primeira barra), o catalogo resolve-lo e a acao pertencer a ele.
///
/// A regra que cada familia segura e uma so: **a peca esta no corpo OU na mochila, nunca nos dois e nunca
/// em nenhum** (ver `RoupaGuardada`). O contra-exemplo e o jogo em que tirar a roupa so a apaga
/// (`RoupaGuardada.TiradaSomeDeTeste`): a peca da criacao se perde, que e o defeito que o pedido evita.
/// ================================================================================
/// </summary>
public sealed partial class GameServer
{
	private int _roupaOk, _roupaFalhou;

	private void AfirmarRoupa(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _roupaOk++; GD.Print($"[roupa]   OK    {oque}"); return; }
		_roupaFalhou++;
		GD.PrintErr($"[roupa]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaRoupa()
	{
		_roupaOk = _roupaFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[roupa] ================ A ROUPA NA MOCHILA: TIRAR, GUARDAR, TROCAR ================");

		bool ligado = _visual != null && ReferenceEquals(CatalogoDeItens.Visual, _visual) && _visual.Roupas.Count >= 6;
		AfirmarRoupa("o catalogo de aparencia esta LIGADO no de itens (sem ele a peca guardada nao tem ficha) e tem roupa pra medir",
					 ligado, $"{_visual?.Roupas.Count ?? 0} roupas");
		if (ligado)
		{
			try
			{
				OIdEAFicha();
				TirarEVestirDeVolta();
				TrocarPorOutra();
				AsRecusasDaRoupa();
				ARoupaNoFioENaCarga();
				ARoupaTiradaQueSome();
			}
			finally
			{
				RoupaGuardada.TiradaSomeDeTeste = false;
				LimparTudoDaBancada();
			}
		}

		GD.Print($"[roupa] ================ {_roupaOk} passaram, {_roupaFalhou} falharam ================");
	}

	/// <summary>A cor com que a bancada tinge uma das pecas -- pra a cor ter que sobreviver a cada ida e volta.</summary>
	private static readonly Rgb TintaDeBancada = new(0x2A, 0x6F, 0xD0);

	/// <summary>Um corpo de bancada vestindo estas pecas do catalogo (pelo indice na lista de roupas).</summary>
	private ServerPlayer VestidoDeBancada(string nome, params (int Indice, Rgb? Cor)[] pecas)
	{
		ServerPlayer pl = Forjar(nome, CorredorLivre(4), bp: 5_000);
		foreach ((int i, Rgb? cor) in pecas) pl.Visual.Roupa.Add(new PecaDeRoupa(_visual!.Roupas[i], cor));
		return pl;
	}

	private static string NoCorpo(ServerPlayer pl) =>
		string.Join(" + ", pl.Visual.Roupa.Select(p => RoupaGuardada.De(p).Id));

	// =====================================================================
	// 1) O ID E A FICHA
	// =====================================================================
	private void OIdEAFicha()
	{
		GD.Print("[roupa] -- 1) A PECA VIRA ID E O ID VIRA PECA");

		var tingida = new PecaDeRoupa(_visual!.Roupas[0], TintaDeBancada);
		var crua = new PecaDeRoupa(_visual.Roupas[1]);
		string idTingida = RoupaGuardada.De(tingida).Id, idCrua = RoupaGuardada.De(crua).Id;

		AfirmarRoupa("o id de uma peca volta a ser a MESMA peca: a folha e a cor (tingida e crua)",
					 RoupaGuardada.Ler(idTingida)?.Peca(_visual) == tingida && RoupaGuardada.Ler(idCrua)?.Peca(_visual) == crua,
					 $"{idTingida} | {idCrua}");
		AfirmarRoupa("...e o id nao tem BARRA (o canal de itens parte o argumento nela) e cabe nas 64 letras do pacote da mochila",
					 !idTingida.Contains('/') && _visual.Roupas.Concat(_visual.Armaduras)
						 .All(c => RoupaGuardada.De(new PecaDeRoupa(c, TintaDeBancada)).Id.Length <= 64),
					 $"o mais longo: {_visual.Roupas.Concat(_visual.Armaduras).Max(c => RoupaGuardada.De(new PecaDeRoupa(c, TintaDeBancada)).Id.Length)}");

		ItemDef? ficha = CatalogoDeItens.Get(idTingida);
		AfirmarRoupa("o catalogo de itens da a ficha dela: a ARTE e a folha da peca e a acao e `vestir`",
					 ficha != null && ficha.Arte == tingida.Caminho && ficha.AcoesDoItem.SequenceEqual([RoupaGuardada.AcaoVestir])
					 && !ficha.Empilhavel, $"{ficha?.Arte} / {string.Join(",", ficha?.AcoesDoItem ?? [])}");
		AfirmarRoupa("CONTRA-EXEMPLO: folha que nao esta no catalogo, ou cor que nao e cor, NAO vira item",
					 CatalogoDeItens.Get("Roupa|InventadaPelaBancada|") == null
					 && CatalogoDeItens.Get($"Roupa|{RoupaGuardada.De(crua).Nome}|azul") == null
					 && RoupaGuardada.Ler("Scouter") == null);
	}

	// =====================================================================
	// 2) TIRAR E VESTIR DE VOLTA
	// =====================================================================
	private void TirarEVestirDeVolta()
	{
		GD.Print("[roupa] -- 2) A ROUPA DA CRIACAO SAI DO CORPO, VAI PRA MOCHILA E VOLTA");

		ServerPlayer pl = VestidoDeBancada("Vestido", (0, TintaDeBancada), (1, null));
		string idA = RoupaGuardada.De(pl.Visual.Roupa[0]).Id, idB = RoupaGuardada.De(pl.Visual.Roupa[1]).Id;
		PecaDeRoupa pecaA = pl.Visual.Roupa[0];

		AfirmarRoupa("(preparo) o corpo nasce com as duas pecas da criacao e a mochila vazia",
					 pl.Visual.Roupa.Count == 2 && pl.Mochila.Ocupados == 0);

		List<string> falas = Ouvir(() => ComandoDeItem(pl, $"item_{RoupaGuardada.AcaoTirar}", idA));
		AfirmarRoupa("tirar a peca TINGIDA: ela sai do corpo, a outra fica, e a mochila ganha UM item com o id dela",
					 pl.Visual.Roupa.Count == 1 && RoupaGuardada.De(pl.Visual.Roupa[0]).Id == idB
					 && pl.Mochila.Quantos(idA) == 1 && pl.Mochila.Ocupados == 1,
					 $"corpo: {NoCorpo(pl)}; mochila: {string.Join(",", pl.Mochila.Pilhas.Select(p => p.Id))}; disse: {string.Join(" / ", falas)}");

		falas = Ouvir(() => ComandoDeItem(pl, $"item_{RoupaGuardada.AcaoVestir}", idA));
		AfirmarRoupa("vestir de volta: a peca sai da mochila e volta pro corpo com a MESMA cor",
					 pl.Mochila.Ocupados == 0 && pl.Visual.Roupa.Count == 2 && pl.Visual.Roupa.Contains(pecaA),
					 $"corpo: {NoCorpo(pl)}; disse: {string.Join(" / ", falas)}");

		ComandoDeItem(pl, $"item_{RoupaGuardada.AcaoTirar}", idA);
		ComandoDeItem(pl, $"item_{RoupaGuardada.AcaoTirar}", idB);
		AfirmarRoupa("da pra ficar SEM NADA: as duas saem, e cada uma ocupa o proprio espaco na mochila",
					 pl.Visual.Roupa.Count == 0 && pl.Mochila.Quantos(idA) == 1 && pl.Mochila.Quantos(idB) == 1);
	}

	// =====================================================================
	// 3) TROCAR POR OUTRA
	// =====================================================================
	private void TrocarPorOutra()
	{
		GD.Print("[roupa] -- 3) TROCAR: SAI UMA, ENTRA OUTRA QUE SE TEM");

		ServerPlayer pl = VestidoDeBancada("Trocador", (0, null));
		string idDaCriacao = RoupaGuardada.De(pl.Visual.Roupa[0]).Id;
		var outra = new PecaDeRoupa(_visual!.Roupas[2], TintaDeBancada);
		string idDaOutra = RoupaGuardada.De(outra).Id;
		pl.Mochila.Guardar(idDaOutra);

		ComandoDeItem(pl, $"item_{RoupaGuardada.AcaoTirar}", idDaCriacao);
		ComandoDeItem(pl, $"item_{RoupaGuardada.AcaoVestir}", idDaOutra);
		AfirmarRoupa("a peca da criacao vai pra mochila e a outra vai pro corpo -- nenhuma das duas se perde",
					 pl.Visual.Roupa.Count == 1 && pl.Visual.Roupa[0] == outra
					 && pl.Mochila.Quantos(idDaCriacao) == 1 && pl.Mochila.Quantos(idDaOutra) == 0,
					 $"corpo: {NoCorpo(pl)}; mochila: {string.Join(",", pl.Mochila.Pilhas.Select(p => p.Id))}");

		ComandoDeItem(pl, "item_largar", idDaCriacao);
		AfirmarRoupa("...e jogar fora a guardada e escolha de quem joga: `largar` vale pra ela como pra todo item",
					 pl.Mochila.Quantos(idDaCriacao) == 0 && pl.Visual.Roupa.Count == 1);
	}

	// =====================================================================
	// 4) AS RECUSAS
	// =====================================================================
	private void AsRecusasDaRoupa()
	{
		GD.Print("[roupa] -- 4) AS RECUSAS: QUATRO PECAS, FOLHA REPETIDA, MOCHILA CHEIA, CORPO QUE NAO E O SEU");

		ServerPlayer pl = VestidoDeBancada("Cheio", (0, null), (1, null), (2, null), (3, null));
		string idQuinta = RoupaGuardada.De(new PecaDeRoupa(_visual!.Roupas[4])).Id;
		pl.Mochila.Guardar(idQuinta);
		List<string> falas = Ouvir(() => ComandoDeItem(pl, $"item_{RoupaGuardada.AcaoVestir}", idQuinta));
		AfirmarRoupa($"com {Appearance.MaxRoupa} pecas no corpo a quinta e recusada, e continua na mochila",
					 pl.Visual.Roupa.Count == Appearance.MaxRoupa && pl.Mochila.Quantos(idQuinta) == 1 && Disse(falas, "tire uma"),
					 string.Join(" / ", falas));

		// A MESMA FOLHA DUAS VEZES: a segunda (de outra cor) fica na mochila.
		string idRepetida = RoupaGuardada.De(new PecaDeRoupa(_visual.Roupas[0], TintaDeBancada)).Id;
		ComandoDeItem(pl, $"item_{RoupaGuardada.AcaoTirar}", RoupaGuardada.De(pl.Visual.Roupa[3]).Id);
		pl.Mochila.Guardar(idRepetida);
		falas = Ouvir(() => ComandoDeItem(pl, $"item_{RoupaGuardada.AcaoVestir}", idRepetida));
		AfirmarRoupa("a MESMA folha nao entra duas vezes no corpo (seriam duas camadas do mesmo desenho, e o saneamento do login jogaria uma fora)",
					 pl.Visual.Roupa.Count == 3 && pl.Mochila.Quantos(idRepetida) == 1 && Disse(falas, "no corpo antes"),
					 string.Join(" / ", falas));

		// QUEM NAO VESTE NAO TIRA; e quem nao tem nao veste.
		falas = Ouvir(() => ComandoDeItem(pl, $"item_{RoupaGuardada.AcaoTirar}", idQuinta));
		AfirmarRoupa("tirar o que NAO esta no corpo e recusado, e a mochila nao ganha uma copia", Disse(falas, "vestindo isso")
					 && pl.Mochila.Quantos(idQuinta) == 1, string.Join(" / ", falas));
		ServerPlayer semNada = VestidoDeBancada("SemNada");
		falas = Ouvir(() => ComandoDeItem(semNada, $"item_{RoupaGuardada.AcaoVestir}", idQuinta));
		AfirmarRoupa("vestir o que NAO esta na mochila e recusado (o cliente nao inventa peca)",
					 semNada.Visual.Roupa.Count == 0 && falas.Count == 1 && Disse(falas, $"tem {RoupaGuardada.Ler(idQuinta)!.Nome}"),
					 string.Join(" / ", falas));

		// A MOCHILA CHEIA: a peca nao tem pra onde ir, entao fica no corpo.
		ServerPlayer lotado = VestidoDeBancada("Lotado", (0, null));
		lotado.Mochila.Guardar(CatalogoDeItens.Scouter, Inventario.Slots);
		string idNoCorpo = RoupaGuardada.De(lotado.Visual.Roupa[0]).Id;
		falas = Ouvir(() => ComandoDeItem(lotado, $"item_{RoupaGuardada.AcaoTirar}", idNoCorpo));
		AfirmarRoupa("(preparo) a mochila do terceiro corpo esta cheia", lotado.Mochila.Cheio, $"{lotado.Mochila.Ocupados}");
		AfirmarRoupa("com a mochila CHEIA a peca nao sai do corpo (ela nao teria onde ficar)",
					 lotado.Visual.Roupa.Count == 1 && lotado.Mochila.Quantos(idNoCorpo) == 0 && Disse(falas, "cheia"),
					 string.Join(" / ", falas));

		// FUNDIDO, O CORPO QUE O MUNDO VE NAO E O GUARDA-ROUPA DE QUEM JOGA.
		ServerPlayer fundido = VestidoDeBancada("Fundido", (0, null));
		fundido.LookDeFusao = new Appearance();
		falas = Ouvir(() => ComandoDeItem(fundido, $"item_{RoupaGuardada.AcaoTirar}", RoupaGuardada.De(fundido.Visual.Roupa[0]).Id));
		AfirmarRoupa("fundido, mexer na roupa e recusado: a peca de verdade nao sai debaixo da roupa da fusao",
					 fundido.Visual.Roupa.Count == 1 && fundido.Mochila.Ocupados == 0 && Disse(falas, "fus"),
					 string.Join(" / ", falas));
		fundido.LookDeFusao = null;
	}

	// =====================================================================
	// 5) NO FIO E NA CARGA
	// =====================================================================
	private void ARoupaNoFioENaCarga()
	{
		GD.Print("[roupa] -- 5) O PACOTE DA MOCHILA LEVA O QUE SE VESTE, E A CARGA DO SAVE GUARDA A PECA");

		ServerPlayer pl = VestidoDeBancada("NoFio", (0, TintaDeBancada), (1, null));
		string idGuardada = RoupaGuardada.De(new PecaDeRoupa(_visual!.Roupas[2], TintaDeBancada)).Id;
		pl.Mochila.Guardar(idGuardada);

		var w = new LiteNetLib.Utils.NetDataWriter();
		w.PutInventario(pl.Mochila, pl.Visual.Roupa);
		var vestindo = new List<PecaDeRoupa> { new("lixo de antes") };
		Inventario lido = new LiteNetLib.Utils.NetDataReader(w.CopyData()).GetInventario(vestindo);
		AfirmarRoupa("o pacote da mochila atravessa com a peca guardada E com as duas vestidas, na ordem e com a cor",
					 lido.Quantos(idGuardada) == 1 && vestindo.SequenceEqual(pl.Visual.Roupa),
					 $"mochila: {string.Join(",", lido.Pilhas.Select(p => p.Id))}; vestindo: {string.Join(" + ", vestindo.Select(p => RoupaGuardada.De(p).Id))}");

		// A ASSINATURA: tirar uma peca com a mochila ja lotada de assinatura igual nao pode ser engolido.
		MandarMochila(pl, forcar: true);
		string antes = pl.SigMochila;
		ComandoDeItem(pl, $"item_{RoupaGuardada.AcaoTirar}", RoupaGuardada.De(pl.Visual.Roupa[1]).Id);
		AfirmarRoupa("trocar de roupa MUDA a assinatura do pacote (senao a tela ficaria com a fileira velha)", pl.SigMochila != antes);

		// A CARGA: o `Sanear` da mochila roda em todo login e joga fora o id que o catalogo nao resolve.
		pl.Mochila.Pilhas.Add(new Pilha("Roupa|FolhaQueSumiuDoJogo|", 1));
		pl.Mochila.Sanear();
		AfirmarRoupa("o saneamento da carga GUARDA a peca de catalogo e varre a que nao existe mais",
					 pl.Mochila.Quantos(idGuardada) == 1 && pl.Mochila.Quantos("Roupa|FolhaQueSumiuDoJogo|") == 0,
					 string.Join(",", pl.Mochila.Pilhas.Select(p => p.Id)));
	}

	// =====================================================================
	// 6) O DEFEITO INJETADO
	// =====================================================================
	private void ARoupaTiradaQueSome()
	{
		GD.Print("[roupa] -- 6) O CONTRA-EXEMPLO: TIRAR QUE SO APAGA");

		ServerPlayer pl = VestidoDeBancada("Perdedor", (0, TintaDeBancada));
		string id = RoupaGuardada.De(pl.Visual.Roupa[0]).Id;

		RoupaGuardada.TiradaSomeDeTeste = true;
		try
		{
			ComandoDeItem(pl, $"item_{RoupaGuardada.AcaoTirar}", id);
			AfirmarRoupa("(defeito injetado: tirar so apaga a peca do corpo) a roupa da criacao SOME -- nem no corpo, nem na mochila",
						 pl.Visual.Roupa.Count == 0 && pl.Mochila.Quantos(id) == 0,
						 $"corpo: {NoCorpo(pl)}; mochila: {pl.Mochila.Quantos(id)}");
		}
		finally { RoupaGuardada.TiradaSomeDeTeste = false; }
	}
}
