using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Items;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--potarateste` (2026-10-23) -- O PAR DE BRINCOS POTARA QUE O KAIOSHIN DA. O dono, depois do relato dos jogadores (o
/// "esquerdo" e o "direito" feitos com a Materialize nao fundiam, e a Potara de verdade sumia em 1 s da mao de quem a recebia): *"Kaioshin
/// entrega o par"*. A regra mora em `Core/Items/ParDePotara.cs` e `GameServer.ParDePotara.cs`; o par chega pelo `ReconciliarOsBrincos`.
///
///     Godot --headless --server --port 11000 --potarateste
///
/// Quatro clientes crus entram pela porta -- o KAIOSHIN (o trono vai pra conta dele), QUEM RECEBE, o PARCEIRO da fusao e o de CONTROLE,
/// que nunca recebe nada. Tudo pelos funis de producao: o tique de 1 Hz dos cargos (`TickDosCargos`), os botoes da mochila (`item_dar`,
/// `item_jogar`, `item_largar`, pelo `Verbo`), o aceite (`fus_sim`) e a cena da fusao com o relogio adiantado (o `FundirDeVerdade` da
/// `--fusaoduplateste`). Os codigos ("P3a.") sao o que a contraprova por mutacao procura:
///   P1. o par chega com o cargo no tique, e so pro Kaioshin;
///   P2. o Kaioshin DA o par: sem alvo, longe ou com a mochila do outro cheia nada sai; com o alvo a 2 passos o par muda de mao;
///   P3. quem recebeu fica com ele -- CONTRA-EXEMPLO `ParVoltaAoCofreDeTeste`, o sumico de 1 s de antes;
///   P4. sem fabrica: o cargo so repoe 24 h depois do ultimo par, e o par que ele ja tinha conta como o de hoje -- CONTRA-EXEMPLO
///       `ParDePotara.CargoRepoeNaHoraDeTeste`;
///   P5. quem nunca recebeu continua sem, e nao convida ninguem;
///   P6. o par recebido FUNDE (jogar no alvo marcado, o aceite, a cena), a fusao veste o brinco e o par SE GASTA; o do Kaioshin nao se
///       gasta -- CONTRA-EXEMPLO `ParDePotara.ParNaoSeGastaDeTeste`;
///   P7. um par, uma fusao: o aceite confere o par (`AceiteSemOsBrincosDeTeste`) e ele nao sai da mao no meio da cena
///       (`BrincosSaemNaFusaoDeTeste`);
///   P8. a roupa potara saiu do guarda-roupa: o catalogo, a Materialize e a peca guardada -- CONTRA-EXEMPLO: o catalogo de antes, com os
///       quatro brincos; e quem a vestia a perde no login -- CONTRA-EXEMPLO `ParDePotara.BrincoDeRoupaFicaNoCorpoDeTeste`.
/// </summary>
public partial class GameServer
{
	private const string ContaDoKaioPtr = "bancada_ptr_kaio";
	private const string ContaDeQuemRecebePtr = "bancada_ptr_recebe";
	private const string ContaDoParceiroPtr = "bancada_ptr_par";
	private const string ContaDoControlePtr = "bancada_ptr_nada";

	private int _ptrOk, _ptrFalhou;

	private void AfirmarPtr(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ptrOk++; GD.Print($"[potara]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _ptrFalhou++; GD.PrintErr($"[potara]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoParDePotara(int porta)
	{
		_ptrOk = _ptrFalhou = 0;
		GD.Print("[potara] ================ O PAR DE BRINCOS POTARA QUE O KAIOSHIN DA (dono, 2026-10-23) ================");
		var tronosReais = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		_tronos.Clear();
		var cliK = new ClienteCruDeTeste();
		var cliR = new ClienteCruDeTeste();
		var cliA = new ClienteCruDeTeste();
		var cliN = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cliK, cliR, cliA, cliN);
		try
		{
			ServerPlayer? k = mesa.EntraNoMundo(cliK, ContaDoKaioPtr, "Kibitoshin");
			ServerPlayer? r = mesa.EntraNoMundo(cliR, ContaDeQuemRecebePtr, "Recebedor");
			ServerPlayer? a = mesa.EntraNoMundo(cliA, ContaDoParceiroPtr, "Parceiro");
			ServerPlayer? n = mesa.EntraNoMundo(cliN, ContaDoControlePtr, "Controle");
			AfirmarPtr("(montagem) os quatro clientes entram pela porta e sao jogadores",
					   k != null && r != null && a != null && n != null && EhJogador(k) && EhJogador(r) && EhJogador(a) && EhJogador(n));
			if (k == null || r == null || a == null || n == null) return;
			foreach (ServerPlayer p in new[] { r, a, n })
				if (!p.Zone.Equals(k.Zone)) MoveToZone(p.Id, k.Zone, k.Pos);
			foreach (ServerPlayer p in new[] { k, r, a, n })
			{
				TirarOsBrincosPtr(p);
				p.Combate.Carencia = 0;   // a carencia do login deixa o corpo intocavel, e o `Marcado` nao acharia ninguem
			}

			OParChegaComOCargoPtr(k, n);
			OKaioshinDaOParPtr(k, r);
			QuemRecebeuFicaComElePtr(r);
			SemFabricaPtr(k, n);
			QuemNaoRecebeuPtr(n, a);
			ORecebidoFundeESeGastaPtr(k, r, a);
			UmParUmaFusaoPtr(r, a, n);
			ARoupaPotaraSaiuPtr();
		}
		catch (Exception e) { AfirmarPtr("a bancada rodou ate o fim", false, e.ToString()); }
		finally
		{
			ParVoltaAoCofreDeTeste = false;
			AceiteSemOsBrincosDeTeste = false;
			BrincosSaemNaFusaoDeTeste = false;
			ParDePotara.CargoRepoeNaHoraDeTeste = false;
			ParDePotara.ParNaoSeGastaDeTeste = false;
			ParDePotara.BrincoDeRoupaFicaNoCorpoDeTeste = false;
			foreach (FusaoAtiva f in _fusoes.ToList()) Separar(f, "fim da bancada da Potara");
			foreach (CenaDeFusao c in _cenasDeFusao.ToList()) SoltarDaCenaDeFusao(c);
			_pedidosDeFusao.Clear();
			mesa.Fecha();
			_tronos.Clear();
			foreach ((string vaga, string conta) in tronosReais) _tronos[vaga] = conta;
			SalvarCargos();
			if (_store != null)
				foreach (string conta in new[] { ContaDoKaioPtr, ContaDeQuemRecebePtr, ContaDoParceiroPtr, ContaDoControlePtr })
					try
					{
						string caminho = System.IO.Path.Combine(_store.Pasta, conta + ".json");
						if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
					}
					catch (Exception e) { GD.PushWarning($"[potara] nao apaguei '{conta}': {e.Message}"); }
			GD.Print($"[potara] ================ {_ptrOk} OK, {_ptrFalhou} FALHA(S) ================");
		}
	}

	// ---- as maos ----

	private static int BrincosPtr(ServerPlayer p) => p.Mochila.Quantos(CatalogoDeItens.BrincosPotara);

	private static void TirarOsBrincosPtr(ServerPlayer p) => p.Mochila.Tirar(CatalogoDeItens.BrincosPotara, BrincosPtr(p));

	/// <summary>Poe <paramref name="quem"/> a <paramref name="tiles"/> passos de <paramref name="de"/>, na mesma zona.</summary>
	private void JuntarPtr(ServerPlayer de, ServerPlayer quem, int tiles)
	{
		Vec2 la = de.Pos + new Vec2(tiles * ZoneCollision.TileSize, 0);
		if (!quem.Zone.Equals(de.Zone)) MoveToZone(quem.Id, de.Zone, la);
		quem.Pos = la;
	}

	/// <summary>
	/// JOGA OS BRINCOS NO ALVO, O ALVO ACEITA E A CENA RODA -- o botao "Jogar no alvo marcado" (`item_jogar`), o `fus_sim` e a cena com o
	/// relogio adiantado, como o `FundirDeVerdade`. `convidou`: o convite chegou na mesa do alvo, da Potara, de quem jogou, e marcado como
	/// dos brincos.
	/// </summary>
	private FusaoAtiva? FundirPelosBrincosPtr(ServerPlayer quem, ServerPlayer alvo, out bool convidou)
	{
		JuntarPtr(quem, alvo, 1);
		quem.Ficha.fusion_cooldown_until = 0;
		alvo.Ficha.fusion_cooldown_until = 0;
		quem.AlvoId = alvo.Id;
		_pedidosDeFusao.Remove(alvo.Id);
		Verbo(quem, "item_jogar", CatalogoDeItens.BrincosPotara);
		convidou = _pedidosDeFusao.TryGetValue(alvo.Id, out PedidoDeFusao? p)
				   && p.DeQuem == quem.Id && p.Tipo == TipoDeFusao.Potara && p.PelosBrincos;
		UsarHabilidade(alvo, "fus_sim");
		AdiantarACenaPtr(quem);
		return FusaoDe(quem.Id);
	}

	/// <summary>A cena da fusao, com a virada e o fim no passado -- os dois tiques sao os do jogo.</summary>
	private void AdiantarACenaPtr(ServerPlayer quem)
	{
		if (_emCenaDeFusao.GetValueOrDefault(quem.Id) is not { } c) return;
		c.Funde = NowMs() - 1;
		TickDaCenaDeFusao();
		c.Acaba = NowMs() - 1;
		TickDaCenaDeFusao();
	}

	/// <summary>
	/// Desfaz a fusao (o `Separar` de producao) e apaga a recarga e os convites dos dois, pra a proxima medida partir do zero. E A PRISAO DA
	/// CENA TAMBEM: a cena adiantada deixa `CenaSegundos` e `Stun` com os ~7 s que o tique do jogo contaria pra baixo -- e esta bancada nao
	/// tica. Sem isto os dois ficavam INTOCAVEIS, o `Marcado` nao os achava, e o segundo convite da Potara nao saia (a r1: so a primeira
	/// fusao fundiu).
	/// </summary>
	private void DesfazerPtr(FusaoAtiva? f, ServerPlayer a, ServerPlayer b)
	{
		if (f != null) Separar(f, "bancada da Potara");
		foreach (ServerPlayer p in new[] { a, b })
		{
			p.Ficha.fusion_cooldown_until = 0;
			_pedidosDeFusao.Remove(p.Id);
			p.CenaSegundos = 0;
			p.Combate.Stun = 0;
			p.Combate.Carencia = 0;
		}
	}

	// =====================================================================
	// P1. O PAR CHEGA COM O CARGO
	// =====================================================================
	private void OParChegaComOCargoPtr(ServerPlayer k, ServerPlayer n)
	{
		GD.Print("[potara] -- P1) o par chega com o cargo, no tique dos cargos --");
		k.Ficha.ParDePotaraEm = 0;
		TickDosCargos();
		AfirmarPtr("P1a. (montagem) sem trono, o tique dos cargos nao da par a ninguem", BrincosPtr(k) == 0 && BrincosPtr(n) == 0,
				   $"Kaioshin {BrincosPtr(k)}, controle {BrincosPtr(n)}");

		_tronos["kaioshin"] = k.Conta;
		long antes = NowMs();
		TickDosCargos();
		AfirmarPtr("P1b. COM O TRONO, O TIQUE DOS CARGOS (1 Hz) ENTREGA UM PAR AO KAIOSHIN, e carimba o dia da entrega",
				   BrincosPtr(k) == 1 && k.Ficha.ParDePotaraEm >= antes, $"{BrincosPtr(k)} par(es), carimbo {k.Ficha.ParDePotaraEm - antes} ms");
		AfirmarPtr("P1c. ...e quem nao e Kaioshin continua sem par", BrincosPtr(n) == 0, $"{BrincosPtr(n)} par(es)");
	}

	// =====================================================================
	// P2. O KAIOSHIN DA O PAR
	// =====================================================================
	private void OKaioshinDaOParPtr(ServerPlayer k, ServerPlayer r)
	{
		GD.Print("[potara] -- P2) o Kaioshin da o par pelo botao da mochila --");
		ItemDef? def = CatalogoDeItens.Get(CatalogoDeItens.BrincosPotara);
		AfirmarPtr("P2a. os brincos tem as duas acoes na mochila: jogar no alvo marcado e DAR ao alvo marcado",
				   def != null && def.AcoesDoItem.Contains("jogar") && def.AcoesDoItem.Contains(ParDePotara.AcaoDar),
				   def == null ? "(sem ficha)" : string.Join(", ", def.AcoesDoItem));

		JuntarPtr(k, r, 2);
		k.AlvoId = 0;
		List<string> semAlvo = Ouvir(() => Verbo(k, "item_dar", CatalogoDeItens.BrincosPotara));
		AfirmarPtr("P2b. sem ninguem marcado o par nao sai, e o Kaioshin ouve por que",
				   BrincosPtr(k) == 1 && BrincosPtr(r) == 0 && Disse(semAlvo, "marque quem vai receber"), Ultimos(semAlvo));

		JuntarPtr(k, r, ParDePotara.TilesPraDar + 5);
		k.AlvoId = r.Id;
		List<string> longe = Ouvir(() => Verbo(k, "item_dar", CatalogoDeItens.BrincosPotara));
		AfirmarPtr($"P2c. com o alvo a {ParDePotara.TilesPraDar + 5} passos (o alcance e o de jogar, {ParDePotara.TilesPraDar}) o par nao sai",
				   BrincosPtr(k) == 1 && BrincosPtr(r) == 0 && Disse(longe, "longe demais"), Ultimos(longe));

		JuntarPtr(k, r, 2);
		int enchidos = 0;
		while (!r.Mochila.Cheio && enchidos < Inventario.Slots) { r.Mochila.Guardar(CatalogoDeItens.Scouter); enchidos++; }
		List<string> cheia = Ouvir(() => Verbo(k, "item_dar", CatalogoDeItens.BrincosPotara));
		AfirmarPtr("P2d. com a mochila de quem recebe cheia, o par nao sai da do Kaioshin",
				   BrincosPtr(k) == 1 && BrincosPtr(r) == 0 && Disse(cheia, "está cheia"), Ultimos(cheia));
		r.Mochila.Tirar(CatalogoDeItens.Scouter, enchidos);

		List<string> deu = Ouvir(() => Verbo(k, "item_dar", CatalogoDeItens.BrincosPotara));
		AfirmarPtr("P2e. COM O ALVO MARCADO A 2 PASSOS, O PAR MUDA DE MAO: sai da mochila do Kaioshin e entra na de quem ele marcou",
				   BrincosPtr(k) == 0 && BrincosPtr(r) == 1, $"Kaioshin {BrincosPtr(k)}, quem recebe {BrincosPtr(r)}");
		AfirmarPtr("P2f. ...e os dois ouvem: quem deu (com a hora do proximo par do cargo) e quem recebeu (com o como usar)",
				   Disse(deu, "você dá um par de brincos Potara") && Disse(deu, "o cargo te entrega outro par em")
				   && Disse(deu, "te dá um par de brincos Potara"), Ultimos(deu));
	}

	// =====================================================================
	// P3. QUEM RECEBEU FICA COM ELE
	// =====================================================================
	private void QuemRecebeuFicaComElePtr(ServerPlayer r)
	{
		GD.Print("[potara] -- P3) quem recebeu fica com o par --");
		for (int i = 0; i < 5; i++) TickDosCargos();
		AfirmarPtr("P3a. CINCO TIQUES DOS CARGOS DEPOIS, O PAR CONTINUA COM QUEM O RECEBEU (antes ele voltava pro cofre em 1 s)",
				   BrincosPtr(r) == 1, $"{BrincosPtr(r)} par(es)");

		int comDefeito;
		ParVoltaAoCofreDeTeste = true;
		try
		{
			TickDosCargos();
			comDefeito = BrincosPtr(r);
		}
		finally { ParVoltaAoCofreDeTeste = false; }
		AfirmarPtr("(defeito injetado: o par volta pro cofre em 1 s, o jogo de antes) a mesma regua de P3a REPROVA",
				   comDefeito == 0, $"{comDefeito} par(es) depois de um tique");
		if (BrincosPtr(r) == 0) r.Mochila.Guardar(CatalogoDeItens.BrincosPotara);
	}

	// =====================================================================
	// P4. SEM FABRICA
	// =====================================================================
	private void SemFabricaPtr(ServerPlayer k, ServerPlayer n)
	{
		GD.Print("[potara] -- P4) sem fabrica: o cargo so repoe no dia seguinte --");
		for (int i = 0; i < 5; i++) TickDosCargos();
		AfirmarPtr("P4a. O KAIOSHIN QUE DEU O PAR FICA SEM: cinco tiques dos cargos depois, o cargo nao repos",
				   BrincosPtr(k) == 0, $"{BrincosPtr(k)} par(es)");

		int comDefeito;
		ParDePotara.CargoRepoeNaHoraDeTeste = true;
		try
		{
			TickDosCargos();
			comDefeito = BrincosPtr(k);
		}
		finally { ParDePotara.CargoRepoeNaHoraDeTeste = false; }
		AfirmarPtr("(defeito injetado: o cargo repoe na hora -- a fabrica de Potara) a mesma regua de P4a REPROVA",
				   comDefeito == 1, $"{comDefeito} par(es) depois de um tique");
		// (cada medida daqui pra baixo parte do Kaioshin SEM par: uma que falhasse nao deixa a seguinte medir o resto dela)
		TirarOsBrincosPtr(k);
		k.Ficha.ParDePotaraEm = NowMs() - ParDePotara.MsEntreParesDoCargo + 60_000;
		TickDosCargos();
		AfirmarPtr("P4b. 23 h 59 min depois do ultimo par, o cargo ainda nao repoe", BrincosPtr(k) == 0, $"{BrincosPtr(k)} par(es)");

		TirarOsBrincosPtr(k);
		k.Ficha.ParDePotaraEm = NowMs() - ParDePotara.MsEntreParesDoCargo - 1_000;
		long antes = NowMs();
		TickDosCargos();
		AfirmarPtr("P4c. 24 h depois, o cargo entrega o par seguinte (e carimba o dia de novo)",
				   BrincosPtr(k) == 1 && k.Ficha.ParDePotaraEm >= antes, $"{BrincosPtr(k)} par(es), carimbo {k.Ficha.ParDePotaraEm - antes} ms");

		// O KAIOSHIN DE ANTES DESTA REGRA: com o par na mao e sem carimbo nenhum. O tique o carimba, e o par dado nao volta na hora.
		k.Ficha.ParDePotaraEm = 0;
		TickDosCargos();
		long carimbo = k.Ficha.ParDePotaraEm;
		JuntarPtr(k, n, 2);
		k.AlvoId = n.Id;
		Verbo(k, "item_dar", CatalogoDeItens.BrincosPotara);
		bool deu = BrincosPtr(n) == 1 && BrincosPtr(k) == 0;
		TickDosCargos();
		AfirmarPtr("P4d. O PAR QUE O KAIOSHIN JA TINHA CONTA COMO O DE HOJE: sem carimbo ele ganha o carimbo, e dado o par, o cargo nao repoe na hora",
				   carimbo > 0 && deu && BrincosPtr(k) == 0, $"carimbo {carimbo}, deu {deu}, Kaioshin com {BrincosPtr(k)}");
		TirarOsBrincosPtr(n);

		// O PAR DO KAIOSHIN DE VOLTA pra P6 (o dele nao se gasta): o dia seguinte, de novo -- e um so.
		TirarOsBrincosPtr(k);
		k.Ficha.ParDePotaraEm = NowMs() - ParDePotara.MsEntreParesDoCargo - 1_000;
		TickDosCargos();
	}

	// =====================================================================
	// P5. QUEM NUNCA RECEBEU
	// =====================================================================
	private void QuemNaoRecebeuPtr(ServerPlayer n, ServerPlayer a)
	{
		GD.Print("[potara] -- P5) quem nunca recebeu continua sem --");
		TirarOsBrincosPtr(n);
		JuntarPtr(n, a, 1);
		n.AlvoId = a.Id;
		_pedidosDeFusao.Remove(a.Id);
		List<string> falas = Ouvir(() => Verbo(n, "item_jogar", CatalogoDeItens.BrincosPotara));
		AfirmarPtr("P5a. quem nunca recebeu um par nao tem o que jogar: nenhum convite chega ao alvo dele",
				   BrincosPtr(n) == 0 && !_pedidosDeFusao.ContainsKey(a.Id) && Disse(falas, "você não tem"), Ultimos(falas));
	}

	// =====================================================================
	// P6. O PAR RECEBIDO FUNDE, E SE GASTA
	// =====================================================================
	private void ORecebidoFundeESeGastaPtr(ServerPlayer k, ServerPlayer r, ServerPlayer a)
	{
		GD.Print("[potara] -- P6) o par recebido funde, e se gasta --");
		// (o par de quem recebe e posto aqui, e nao herdado da P2: uma familia vermelha la em cima nao derruba esta em cascata)
		TirarOsBrincosPtr(r);
		r.Mochila.Guardar(CatalogoDeItens.BrincosPotara);
		AfirmarPtr("(montagem) quem recebe esta com um par e o Kaioshin com o do dia seguinte", BrincosPtr(r) == 1 && BrincosPtr(k) == 1,
				   $"quem recebe {BrincosPtr(r)}, Kaioshin {BrincosPtr(k)}");

		FusaoAtiva? f = FundirPelosBrincosPtr(r, a, out bool convidou);
		AfirmarPtr("P6a. quem recebeu joga os brincos no alvo marcado: o convite da Potara chega na mesa do parceiro, marcado como dos brincos",
				   convidou);
		AfirmarPtr("P6b. O PARCEIRO ACEITA E OS DOIS SE FUNDEM pela Potara, com quem jogou no controle",
				   f is { Tipo: TipoDeFusao.Potara } && f.Dono == r, f == null ? "(sem fusao)" : $"{f.Tipo}, dono {f.Dono.Name}");
		AfirmarPtr("P6c. O PAR DE QUEM NAO E KAIOSHIN SE GASTOU NA FUSAO", BrincosPtr(r) == 0, $"{BrincosPtr(r)} par(es)");
		AfirmarPtr("P6e. ...e a fusao veste o brinco potara (fora do guarda-roupa, pela segunda porta do `CaminhoDaPecaDeFusao`)",
				   r.LookDeFusao?.Roupa.Exists(p => p.Caminho.EndsWith("/potara.tres", StringComparison.Ordinal)) == true,
				   string.Join(", ", r.LookDeFusao?.Roupa.Select(p => p.Caminho) ?? []));
		DesfazerPtr(f, r, a);

		r.Mochila.Guardar(CatalogoDeItens.BrincosPotara);
		bool fundiu;
		int ficou;
		ParDePotara.ParNaoSeGastaDeTeste = true;
		try
		{
			FusaoAtiva? g = FundirPelosBrincosPtr(r, a, out _);
			fundiu = g != null;
			ficou = BrincosPtr(r);
			DesfazerPtr(g, r, a);
		}
		finally { ParDePotara.ParNaoSeGastaDeTeste = false; }
		AfirmarPtr("(defeito injetado: o par nao se gasta -- cada par dado vira uma Potara pra sempre) a mesma regua de P6c REPROVA",
				   fundiu && ficou == 1, $"fundiu {fundiu}, ficou {ficou} par(es)");
		TirarOsBrincosPtr(r);

		FusaoAtiva? h = FundirPelosBrincosPtr(k, a, out _);
		AfirmarPtr("P6d. O PAR DO KAIOSHIN NAO SE GASTA: ele funde com o dele e continua com o par", h != null && BrincosPtr(k) == 1,
				   $"fundiu {h != null}, {BrincosPtr(k)} par(es)");
		DesfazerPtr(h, k, a);
	}

	// =====================================================================
	// P7. UM PAR, UMA FUSAO
	// =====================================================================
	private void UmParUmaFusaoPtr(ServerPlayer r, ServerPlayer a, ServerPlayer n)
	{
		GD.Print("[potara] -- P7) um par, uma fusao --");
		(bool fundiu, bool ouviu, int comOOutro) = JogarEDarPtr(r, a, n);
		AfirmarPtr("P7a. QUEM JOGOU O PAR E DEPOIS O DEU A OUTRO NAO FUNDE: o aceite confere os brincos, ninguem se funde, e os dois ouvem por que",
				   !fundiu && ouviu && comOOutro == 1, $"fundiu {fundiu}, ouviu {ouviu}, o outro com {comOOutro}");
		DesfazerPtr(FusaoDe(r.Id), r, a);
		TirarOsBrincosPtr(n);
		TirarOsBrincosPtr(r);

		bool fundiuComDefeito;
		AceiteSemOsBrincosDeTeste = true;
		try { (fundiuComDefeito, _, _) = JogarEDarPtr(r, a, n); }
		finally { AceiteSemOsBrincosDeTeste = false; }
		AfirmarPtr("(defeito injetado: o aceite nao confere os brincos -- um par, duas fusoes) a mesma regua de P7a REPROVA", fundiuComDefeito);
		DesfazerPtr(FusaoDe(r.Id), r, a);
		TirarOsBrincosPtr(n);
		TirarOsBrincosPtr(r);

		(bool largou, bool emCena, bool fundiu2, int sobrou, bool noChao) = LargarNaCenaPtr(r, a);
		AfirmarPtr("P7b. NO MEIO DA CENA DA FUSAO OS BRINCOS NAO SAEM DA MAO: o largar e recusado", emCena && !largou,
				   $"em cena {emCena}, largou {largou}");
		AfirmarPtr("P7c. ...e a fusao acontece e GASTA o par -- nada ficou no chao pra outra fusao", fundiu2 && sobrou == 0 && !noChao,
				   $"fundiu {fundiu2}, sobrou {sobrou}, no chao {noChao}");
		DesfazerPtr(FusaoDe(r.Id), r, a);
		TirarDoChaoPtr(r);

		bool largouComDefeito, noChaoComDefeito;
		BrincosSaemNaFusaoDeTeste = true;
		try { (largouComDefeito, _, _, _, noChaoComDefeito) = LargarNaCenaPtr(r, a); }
		finally { BrincosSaemNaFusaoDeTeste = false; }
		AfirmarPtr("(defeito injetado: os brincos saem da mao no meio da cena) a mesma regua de P7b e P7c REPROVA: o par cai no chao e a fusao nao gasta nada",
				   largouComDefeito && noChaoComDefeito, $"largou {largouComDefeito}, no chao {noChaoComDefeito}");
		DesfazerPtr(FusaoDe(r.Id), r, a);
		TirarDoChaoPtr(r);
		TirarOsBrincosPtr(r);
	}

	/// <summary>Joga o par no alvo, DA o par a um terceiro e o alvo aceita: (fundiu, ouviram o porque, quantos pares o terceiro tem).</summary>
	private (bool Fundiu, bool Ouviu, int ComOOutro) JogarEDarPtr(ServerPlayer r, ServerPlayer a, ServerPlayer n)
	{
		TirarOsBrincosPtr(r);
		r.Mochila.Guardar(CatalogoDeItens.BrincosPotara);
		JuntarPtr(r, a, 1);
		JuntarPtr(r, n, 2);
		r.Ficha.fusion_cooldown_until = 0;
		a.Ficha.fusion_cooldown_until = 0;
		r.AlvoId = a.Id;
		_pedidosDeFusao.Remove(a.Id);
		Verbo(r, "item_jogar", CatalogoDeItens.BrincosPotara);
		r.AlvoId = n.Id;
		Verbo(r, "item_dar", CatalogoDeItens.BrincosPotara);
		List<string> falas = Ouvir(() => UsarHabilidade(a, "fus_sim"));
		AdiantarACenaPtr(r);
		return (EstaFundido(r.Id), Disse(falas, "não está mais com os brincos Potara"), BrincosPtr(n));
	}

	/// <summary>Joga o par, o alvo aceita, e NO MEIO DA CENA quem jogou aperta "Largar no chão"; depois a cena acaba.</summary>
	private (bool Largou, bool EmCena, bool Fundiu, int Sobrou, bool NoChao) LargarNaCenaPtr(ServerPlayer r, ServerPlayer a)
	{
		TirarOsBrincosPtr(r);
		r.Mochila.Guardar(CatalogoDeItens.BrincosPotara);
		JuntarPtr(r, a, 1);
		r.Ficha.fusion_cooldown_until = 0;
		a.Ficha.fusion_cooldown_until = 0;
		r.AlvoId = a.Id;
		_pedidosDeFusao.Remove(a.Id);
		Verbo(r, "item_jogar", CatalogoDeItens.BrincosPotara);
		UsarHabilidade(a, "fus_sim");
		bool emCena = _emCenaDeFusao.ContainsKey(r.Id);
		Verbo(r, "item_largar", CatalogoDeItens.BrincosPotara);
		bool largou = BrincosPtr(r) == 0;
		AdiantarACenaPtr(r);
		bool noChao = LargadosDe(r.Zone.Hash).Exists(l => l.Item == CatalogoDeItens.BrincosPotara);
		return (largou, emCena, EstaFundido(r.Id), BrincosPtr(r), noChao);
	}

	private void TirarDoChaoPtr(ServerPlayer perto) =>
		LargadosDe(perto.Zone.Hash).RemoveAll(l => l.Item == CatalogoDeItens.BrincosPotara);

	// =====================================================================
	// P8. A ROUPA POTARA SAIU DO GUARDA-ROUPA
	// =====================================================================
	private void ARoupaPotaraSaiuPtr()
	{
		GD.Print("[potara] -- P8) os brincos de roupa sairam do guarda-roupa e da Materialize --");
		if (_visual is not { } cat) { AfirmarPtr("(montagem) o servidor tem o catalogo de aparencia", false); return; }

		AfirmarPtr("P8a. O GUARDA-ROUPA (a grade da criacao e a da Materialize) NAO TEM NENHUMA FOLHA POTARA",
				   !PotaraNoGuardaRoupaPtr(cat), string.Join(", ", cat.Roupas.Where(c => c.Contains("otara"))));
		AfirmarPtr("P8b. A MATERIALIZE RECUSA os quatro brincos de roupa (potara, Potara-Equipped, potaraleft, potararight)",
				   !MaterializaUmBrincoPtr(cat));

		var mochila = new Inventario();
		mochila.Pilhas.Add(new Pilha($"{RoupaGuardada.Prefixo}potaraleft|", 1));
		mochila.Pilhas.Add(new Pilha(CatalogoDeItens.Maca, 1));
		mochila.Sanear();
		AfirmarPtr("P8c. A PECA DE ROUPA GUARDADA DE ANTES (`Roupa|potaraleft|`) nao e mais item: a limpeza da mochila do login a tira (e o resto fica)",
				   !BrincoDeRoupaEhItemPtr(cat) && mochila.Pilhas.Count == 1 && mochila.Quantos(CatalogoDeItens.Maca) == 1,
				   string.Join(", ", mochila.Pilhas.Select(p => p.Id)));

		// QUEM VESTIA UM BRINCO DE ROUPA O PERDE NO LOGIN, pela carga de producao (`AccountStore.ParaJogador`); a outra peca fica.
		(int Brincos, int Outras, string Erro) Logar()
		{
			var save = new CharacterSave
			{
				Nome = "Brincalhao", Raca = "Human", Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human" },
				Visual = new Jandirus.Core.Appearance.Appearance
				{
					Roupa = [new PecaDeRoupa("res://Assets/Sprites/Clothes/potararight.tres"), new PecaDeRoupa("res://Assets/Sprites/Clothes/Halo.tres")],
				},
			};
			var corpo = new ServerPlayer { Id = 99_871, Name = "Brincalhao", Ficha = new Jandirus.Core.Stats.Fighter(), Livro = new Jandirus.Core.Skills.SkillBook() };
			try { AccountStore.ParaJogador(save, corpo); }
			catch (Exception e) { return (-1, -1, e.GetType().Name + ": " + e.Message); }
			return (corpo.Visual.Roupa.Count(p => ParDePotara.EhBrincoDeRoupa(p.Caminho)), corpo.Visual.Roupa.Count(p => !ParDePotara.EhBrincoDeRoupa(p.Caminho)), "");
		}
		(int brincos, int outras, string erro) = Logar();
		AfirmarPtr("P8d. QUEM VESTIA UM BRINCO DE ROUPA O PERDE NO LOGIN (a carga de producao), e a outra peca fica no corpo",
				   brincos == 0 && outras == 1, $"brincos {brincos}, outras {outras} {erro}");
		int comDefeito;
		ParDePotara.BrincoDeRoupaFicaNoCorpoDeTeste = true;
		try { comDefeito = Logar().Brincos; }
		finally { ParDePotara.BrincoDeRoupaFicaNoCorpoDeTeste = false; }
		AfirmarPtr("(defeito injetado: o brinco vestido fica no corpo, o jogo de antes) a mesma regua de P8d REPROVA", comDefeito == 1,
				   $"brincos {comDefeito}");

		// O CATALOGO DE ANTES: o mesmo arquivo, com os quatro brincos de volta na lista -- a regua tem de saber dizer a diferenca.
		VisualCatalog antes = VisualCatalog.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Data/visual.json"));
		foreach (string nome in BrincosForaDoGuardaRoupa) antes.Roupas.Add($"res://Assets/Sprites/Clothes/{nome}.tres");
		AfirmarPtr("(defeito injetado: o guarda-roupa de antes, com os quatro brincos) a mesma regua de P8a, P8b e P8c REPROVA",
				   PotaraNoGuardaRoupaPtr(antes) && MaterializaUmBrincoPtr(antes) && BrincoDeRoupaEhItemPtr(antes));
	}

	/// <summary>Alguma folha potara na grade (a lista que a criacao e a Materialize oferecem, `PesoDeTreino.Escolhiveis`)?</summary>
	private static bool PotaraNoGuardaRoupaPtr(VisualCatalog cat) =>
		PesoDeTreino.Escolhiveis(cat).Exists(e => e.Nome.Contains("potara", StringComparison.OrdinalIgnoreCase));

	/// <summary>A Materialize faria algum dos quatro brincos? (A conta do servidor, `Materializacao.Item`.)</summary>
	private static bool MaterializaUmBrincoPtr(VisualCatalog cat) =>
		BrincosForaDoGuardaRoupa.Any(n => Materializacao.Item($"{Materializacao.DeRoupa}/{n}", cat, out _) != null);

	/// <summary>A peca guardada de algum dos quatro ainda seria item (a ficha que a mochila e a tela leem)?</summary>
	private static bool BrincoDeRoupaEhItemPtr(VisualCatalog cat) =>
		BrincosForaDoGuardaRoupa.Any(n => RoupaGuardada.Ler($"{RoupaGuardada.Prefixo}{n}|")?.Ficha(cat) != null);
}
