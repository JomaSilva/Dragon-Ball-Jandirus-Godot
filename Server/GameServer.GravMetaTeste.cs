using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--gravmetateste` -- O PESO, A GRAVIDADE E O GANHO (dono, 2026-10-23): *"faça com q morto sofra dano de peso e gravidade, isso
/// virou meta do jogo, porem pra compensar suba o bp gain em 10x (tinha dividido por 20 antes entao agr seria so dividido por 2) , alem
/// disso o buff de bp do aclimado deveria ser no maximo 25% quando vc tem 500x gravidade masterizada e nao 100% , ta mt op"* e *"usar pesos
/// acima de 8x seu limite corporal, vc nem conseguiria treinar assim n valendo a pena a usar pesos mt mt mt altos"*.
///
/// Um cliente cru entra pela porta (o aviso de esmagamento, a recusa do treino e a ficha lenta sao de JOGADOR), e cada familia mede pela
/// porta de PRODUCAO -- o `PowerLevel`, o `Treinar` de 5 Hz, o `DefinirAtividade` do pacote, o `TickDoEsmagamento` de 1 Hz e o pacote da
/// ficha lenta --, com o contra-exemplo de cada regra:
///   A. A ACLIMATACAO TEM TETO: +25% com 500x dominada, a mesma curva abaixo (10x +3,4%, 100x +13,7%), nada a mais acima do teto, a
///      penalidade intacta. Contra: `StatCurves.AclimatacaoDoDmDeTeste` (a do DM, +96,6% a 500x).
///   P. PESO DEMAIS NAO TREINA: acima de 8x o limite do corpo o T, a meditacao, a luta e o ki nao rendem; o pedido de treinar (e o de
///      meditar) e recusado com o aviso, e o treino em curso cai; o painel diz 0x; o resto do peso fica (o `weight`, a prisao, o
///      esmagamento); gravidade sem peso continua sendo treino. Contra: `GainKnobs.PesoDemaisTreinaDeTeste`.
///   M. O MORTO SENTE O CHAO: o fantasma no Inferno (10x), numa ilha do Outro Mundo (10x), o do passe entre os vivos (Vegeta, 10x) e o
///      morto com peso sao prensados e feridos, com o aviso; o corpo em farrapos se desfaz e acorda na mesa do Enma; o cadaver dos 2 s
///      fica de fora, e quem ficou com o corpo continua como no DM; e a maestria sobe no Inferno (a saida). Contra:
///      `MortoSemPesoDeTeste` (o `!dead || KeepsBody` do DM).
/// O ganho x10 (`GainKnobs.BPTick`) e da `--ganhoteste` (familia 5), que mede o soco de producao contra a taxa do DM.
///
///     Godot --headless --path . --server --port 10910 --gravmetateste
/// </summary>
public sealed partial class GameServer
{
	private const string ContaDaBancadaGravMeta = "bancada_gravmeta";

	private bool _gmRodando, _gmJaRodou;
	private int _gmOk, _gmFalhou;

	private void AfirmarGm(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _gmOk++; GD.Print($"[gravmeta]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _gmFalhou++; GD.PrintErr($"[gravmeta]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaGravMeta(int porta)
	{
		if (_gmRodando || _gmJaRodou) return;
		_gmRodando = _gmJaRodou = true;
		_gmOk = _gmFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[gravmeta] ================ O PESO, A GRAVIDADE E O GANHO (dono, 2026-10-23) ================");

		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		ServerPlayer? pl = null;
		double maestriaGuardada = 1;
		try
		{
			pl = mesa.EntraNoMundo(cli, ContaDaBancadaGravMeta, "Pesado");
			AfirmarGm("(montagem) o cliente entra pela porta e e jogador; a Terra tem mapa",
					  pl != null && EhJogador(pl) && _pjMapa != null);
			if (pl == null || _pjMapa == null) return;
			if (pl.Zone.Hash != ZonaDaBancadaDeProjetil.Hash) MoveToZone(pl.Id, ZonaDaBancadaDeProjetil, PontoDeNascimento(ZonaDaBancadaDeProjetil));
			maestriaGuardada = pl.Ficha.GravMastered;

			AAclimatacaoTemTetoGm(pl);
			OPesoDemaisNaoTreinaGm(pl);
			OMortoSenteOChaoGm(pl);
		}
		catch (Exception e)
		{
			AfirmarGm("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			StatCurves.AclimatacaoDoDmDeTeste = false;
			GainKnobs.PesoDemaisTreinaDeTeste = false;
			MortoSemPesoDeTeste = false;
			if (pl != null)
			{
				// o corpo volta vivo, inteiro e sem peso pra Terra -- a saida pela porta grava o save, e a bancada apaga a conta logo abaixo
				pl.Combate.Reviver();
				pl.Ficha.KeepsBody = false;
				pl.Ficha.passe_do_enma_ate = 0;
				pl.MorteJaViajou = false;
				pl.Ficha.Weighted = 0;
				pl.Ficha.pesoVestido = "";
				pl.Ficha.train = pl.Ficha.med = false;
				pl.Ficha.GravMastered = maestriaGuardada;
				if (pl.Zone.Hash != ZonaDaBancadaDeProjetil.Hash) MoveToZone(pl.Id, ZonaDaBancadaDeProjetil, PontoDeNascimento(ZonaDaBancadaDeProjetil));
			}
			LimparTudoDaBancada();
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaBancadaGravMeta + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch (Exception e) { GD.PushWarning($"[gravmeta] nao apaguei '{ContaDaBancadaGravMeta}': {e.Message}"); }
			_gmRodando = false;
			GD.Print($"[gravmeta] ================ {_gmOk} OK, {_gmFalhou} FALHA(S) ================");
		}
	}

	/// <summary>
	/// O CORPO VIVO E PARADO NA TERRA, pronto pra medir: o BP dado, o teto pessoal fora da conta (o `UPMod` da `--ganhoteste`), sem briga,
	/// sem atividade, sem peso, o tanque e o folego cheios.
	/// </summary>
	private void PalcoGm(ServerPlayer pl, double bp = 1_000_000)
	{
		Fighter f = pl.Ficha;
		if (f.dead) pl.Combate.Reviver();
		pl.Combate.Curar();
		if (pl.Zone.Hash != ZonaDaBancadaDeProjetil.Hash) MoveToZone(pl.Id, ZonaDaBancadaDeProjetil, PontoDeNascimento(ZonaDaBancadaDeProjetil));
		pl.Combate.LutandoDeVerdade = pl.Combate.EmCombate = pl.Combate.BrigandoComAlguem = 0;
		f.IsInFight = false;
		f.train = f.med = false;
		DefinirAtividade(pl, Protocol.Activity.Parado);
		f.BP = bp;
		f.UPMod = 1e6;
		f.hiddenpotential = 0;
		f.BPBuffer = 0;
		f.Gaintimer = 0;
		f.tmp_activ_gains = 0;
		f.bp_milestone_mult = 1;
		f.Planetgrav = 1;
		f.gravmult = 0;
		f.GravMastered = 1;
		f.zoneGainMult = 1;
		f.Tick(agoraMs: NowMs());
		f.Ki = f.MaxKi;
		f.stamina = f.maxstamina;
		f.Statify();
		f.PowerLevel(agoraMs: NowMs());
	}

	/// <summary>A ficha lenta que sairia agora pra este corpo (o pacote de producao, pela escuta): a linha "Aclimatado" e o ganho do painel.</summary>
	private (float Aclimatacao, float GanhoDeTreino)? FichaLentaGm(ServerPlayer pl)
	{
		var ouvidos = new List<Protocol.AtributosState>();
		List<Protocol.AtributosState>? antes = EscutaDeAtributos;
		EscutaDeAtributos = ouvidos;
		try { pl.SigAtributos = ""; MandarAtributos(pl); }
		finally { EscutaDeAtributos = antes; }
		return ouvidos.Count > 0 ? (ouvidos[^1].Aclimatacao, ouvidos[^1].GanhoDeTreino) : null;
	}

	// =====================================================================
	// A) A ACLIMATACAO TEM TETO
	// =====================================================================
	private void AAclimatacaoTemTetoGm(ServerPlayer pl)
	{
		GD.Print("[gravmeta] -- A) a aclimatacao: no maximo +25% com 500x dominada, a mesma curva abaixo, nada a mais acima do teto --");
		PalcoGm(pl, 1_000);
		Fighter f = pl.Ficha;

		// O PODER DE QUEM DOMINA `maestria` NUM CHAO DE `chao` -- pelo `PowerLevel` de producao (o `gravFelt` que soma na base)
		(double Bonus, double Expresso) Dominando(double maestria, double chao = 1)
		{
			f.Planetgrav = chao;
			f.gravmult = 0;
			f.GravMastered = maestria;
			f.Statify();
			f.PowerLevel(agoraMs: NowMs());
			return (f.gravBuff, f.expressedBP);
		}

		var a500 = Dominando(500);
		var noFio = FichaLentaGm(pl);
		AfirmarGm("A1. **NO MAXIMO +25% COM 500x DOMINADA** (*\"deveria ser no maximo 25% quando vc tem 500x gravidade masterizada e nao 100%\"*): "
				  + "o `gravFelt` de quem domina 500x num chao de 1x e 1,25 -- e o pacote da aba de Formas leva o mesmo",
				  Math.Abs(a500.Bonus - 1.25) < 1e-9 && noFio is { } fio && Math.Abs(fio.Aclimatacao - 1.25f) < 1e-4,
				  $"gravBuff {a500.Bonus:0.######}; no fio {noFio?.Aclimatacao:0.####}");

		double a10 = Dominando(10).Bonus, a100 = Dominando(100).Bonus;
		AfirmarGm("A2. **A MESMA CURVA ABAIXO DO TETO** -- a do DM (`ln(maestria/gravidade)^2`), so o divisor mudou: 10x +3,4%, 100x +13,7% (eram +5,3% e +53%), "
				  + "e 100x rende quatro vezes o que rende 10x, como no DM",
				  Math.Abs(a10 - 1.0343) < 5e-4 && Math.Abs(a100 - 1.1372) < 5e-4 && Math.Abs((a100 - 1) / (a10 - 1) - 4) < 1e-6,
				  $"10x {(a10 - 1) * 100:0.###}%; 100x {(a100 - 1) * 100:0.###}%; razao {(a100 - 1) / (a10 - 1):0.######}");

		double a501 = Dominando(501).Bonus, a1000 = Dominando(1000).Bonus, a1000em2 = Dominando(1000, 2).Bonus;
		AfirmarGm("A3. **ACIMA DO TETO NAO CRESCE**: o planeta mais a maquina chegam a ~501x, e a maestria pela luta nao tem teto -- 501x e 1000x dominados "
				  + "num chao de 1x dao os mesmos +25%; e 1000x num chao de 2x (a razao 500) tambem",
				  Math.Abs(a501 - 1.25) < 1e-9 && Math.Abs(a1000 - 1.25) < 1e-9 && Math.Abs(a1000em2 - 1.25) < 1e-9,
				  $"501x {a501:0.######}; 1000x {a1000:0.######}; 1000x em 2x {a1000em2:0.######}");

		double semDominar = Dominando(1).Expresso, dominando500 = Dominando(500).Expresso;
		AfirmarGm("A4. **E PODER DE VERDADE**: o mesmo corpo, com o mesmo BP, expressa 1,25x dominando 500x (o bonus soma na base, `AdditiveBoost`)",
				  semDominar > 0 && Math.Abs(dominando500 / semDominar - 1.25) < 0.003,
				  $"sem dominar {semDominar:N0}; dominando 500x {dominando500:N0} ({dominando500 / Math.Max(semDominar, 1):0.####}x)");

		double penalidade = Dominando(1, 10).Bonus;
		double daPenalidade = 1 / (Math.Pow(Math.Log(10), 1.6) / 10 + 1);
		AfirmarGm("A5. a PENALIDADE (dominar menos que o chao) nao mudou: 1x num chao de 10x continua o `1/((-ln(g))^1.6/10+1)` do DM",
				  Math.Abs(penalidade - daPenalidade) < 1e-9, $"{penalidade:0.######} contra {daPenalidade:0.######}");

		StatCurves.AclimatacaoDoDmDeTeste = true;
		double dm500, dm1000;
		try { dm500 = Dominando(500).Bonus; dm1000 = Dominando(1000).Bonus; }
		finally { StatCurves.AclimatacaoDoDmDeTeste = false; }
		AfirmarGm("A6. (defeito injetado: a aclimatacao do DM, o jogo de antes) 500x dominada rende +96,6% e 1000x passa disso -- as reguas A1 e A3 REPROVAM",
				  Math.Abs(dm500 - 1.25) > 0.5 && dm1000 > dm500 + 0.1, $"500x {(dm500 - 1) * 100:0.#}%; 1000x {(dm1000 - 1) * 100:0.#}%");

		f.GravMastered = 1;
		f.Statify();
		f.PowerLevel(agoraMs: NowMs());
	}

	// =====================================================================
	// P) PESO DEMAIS NAO TREINA
	// =====================================================================
	private void OPesoDemaisNaoTreinaGm(ServerPlayer pl)
	{
		GD.Print("[gravmeta] -- P) peso acima de 8x o limite do corpo: o treino nao rende, o pedido e recusado, o painel diz 0x, o resto do peso fica --");
		PalcoGm(pl);
		Fighter f = pl.Ficha;
		// UM PESO QUE VAI A 20x O LIMITE DO CORPO num chao de 1x: a porcentagem do verbo vira a razao dividida por 5 (37% = 7,4x, 43% = 8,6x,
		// 100% = 20x). Os maximos se compram (`PesoDeTreino`): o verbo de ajustar nao tem teto de corpo.
		string pesos = PesoDeTreino.IdDe(f.weight_cap_hw * 20, "");
		pl.Mochila.Guardar(pesos);
		const string Abaixo = "37", Acima = "43", Muito = "100";

		// VESTE pelo verbo de producao (o "ajustar" da mochila, que ja roda o tique inteiro da ficha) e devolve o que o servidor disse
		List<string> Vestir(string pct)
		{
			List<string> disse = Ouvir(() => AjustarPeso(pl, pesos, pct));
			f.stamina = f.maxstamina;
			f.Ki = f.MaxKi;
			f.Statify();
			f.PowerLevel(agoraMs: NowMs());
			f.WeightTick();
			return disse;
		}

		// O QUE CADA ATIVIDADE DO CORE RENDE (BP, Ki base e folego maximo), chamada direto -- a regra mora nela, e o NPC e a tecnica que
		// treinam nao passam pelo pedido do jogador
		(double Bp, double Ki, double Folego) Rende(Action atividade)
		{
			f.BPBuffer = 0;
			f.Gaintimer = 0;
			f.tmp_activ_gains = 0;
			f.minuteshot = false;
			double bp0 = f.BP, ki0 = f.baseKi, st0 = f.maxstamina;
			for (int i = 0; i < 20; i++) atividade();
			return (f.BP - bp0, f.baseKi - ki0, f.maxstamina - st0);
		}
		var treino = new Random(7);
		(double Bp, double Ki, double Folego) T() => Rende(() => f.TrainGain(treino, 3));
		(double Bp, double Ki, double Folego) Med() => Rende(() => f.MedGain(treino));
		(double Bp, double Ki, double Folego) Luta() => Rende(() => f.AttackGain(treino));
		(double Bp, double Ki, double Folego) Ki() => Rende(() => f.BlastGain(treino, 1, ignoreMinuteshot: true));

		List<string> vestiuAbaixo = Vestir(Abaixo);
		double razaoAbaixo = f.weight_ratio;
		var tAbaixo = T();
		var medAbaixo = Med();
		var lutaAbaixo = Luta();
		var kiAbaixo = Ki();
		double painelAbaixo = f.MultiplicadorDeGanho();
		AfirmarGm("(montagem) a 7,4x o limite do corpo (abaixo dos 8x) o peso rende no teto (8x) -- o T, a meditacao, a luta e o ki pagam, e o painel diz 8x vezes o vigor",
				  Math.Abs(razaoAbaixo - 7.4) < 0.05 && f.weight == 8 && tAbaixo.Bp > 0 && medAbaixo.Bp > 0 && lutaAbaixo.Bp > 0 && kiAbaixo.Bp > 0
				  && Math.Abs(painelAbaixo - 8 * f.StamBPGainMod) < 1e-6,
				  $"razao {razaoAbaixo:0.###}; weight {f.weight}; T +{tAbaixo.Bp:0.###}, med +{medAbaixo.Bp:0.###}, luta +{lutaAbaixo.Bp:0.###}, "
				  + $"ki +{kiAbaixo.Bp:0.###}; painel {painelAbaixo:0.###}x");

		List<string> vestiuAcima = Vestir(Acima);
		double razaoAcima = f.weight_ratio;
		AfirmarGm("P0. **O AVISO AO VESTIR**: a 8,6x a frase de vestir diz que NADA rende (passa de 8x o que o corpo aguenta) -- e a 7,4x ela continua "
				  + "dizendo os 8x de ganho",
				  vestiuAcima.Any(s => s.Contains("e NADA de ganho no treino") && s.Contains("passa de 8x"))
				  && vestiuAbaixo.Any(s => s.Contains("e 8x de ganho no treino")) && !vestiuAbaixo.Any(s => s.Contains("NADA de ganho")),
				  $"a 8,6x: \"{string.Join(" | ", vestiuAcima)}\"; a 7,4x: \"{string.Join(" | ", vestiuAbaixo)}\"");
		var tAcima = T();
		var medAcima = Med();
		var lutaAcima = Luta();
		var kiAcima = Ki();
		AfirmarGm("P1. **A 8,6x O LIMITE DO CORPO O T NAO RENDE NADA** (*\"usar pesos acima de 8x seu limite corporal, vc nem conseguiria treinar assim\"*): nem BP, nem Ki, nem folego",
				  Math.Abs(razaoAcima - 8.6) < 0.05 && tAcima.Bp == 0 && tAcima.Ki == 0 && tAcima.Folego == 0,
				  $"razao {razaoAcima:0.###}; BP +{tAcima.Bp}, Ki +{tAcima.Ki}, folego +{tAcima.Folego}");
		AfirmarGm("P2. ...nem a MEDITACAO", medAcima.Bp == 0 && medAcima.Ki == 0, $"BP +{medAcima.Bp}, Ki +{medAcima.Ki}");
		AfirmarGm("P3. ...nem a LUTA (o soco e o golpe que se leva)", lutaAcima.Bp == 0 && lutaAcima.Folego == 0, $"BP +{lutaAcima.Bp}, folego +{lutaAcima.Folego}");
		AfirmarGm("P4. ...nem o KI (o tiro que acerta)", kiAcima.Bp == 0 && kiAcima.Ki == 0, $"BP +{kiAcima.Bp}, Ki +{kiAcima.Ki}");

		double painelAcima = f.MultiplicadorDeGanho();
		var fio = FichaLentaGm(pl);
		AfirmarGm("P5. **O PAINEL DIZ 0x**: o ganho do treino que a ficha lenta leva (a linha \"Ganho de BP\") e zero com o peso acima de 8x",
				  painelAcima == 0 && fio is { } lento && lento.GanhoDeTreino == 0, $"painel {painelAcima}; no fio {fio?.GanhoDeTreino}");

		// O PEDIDO DE PRODUCAO (o `C2S.Activity`, pelo `DefinirAtividade`): recusado, com o aviso, e o corpo fica parado
		List<string> pedidoT = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Treinando));
		bool treinaDepoisDoPedido = f.train;
		List<string> pedidoMed = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Meditando));
		bool meditaDepoisDoPedido = f.med;
		AfirmarGm("P6. **O PEDIDO DE TREINAR E RECUSADO, COM O AVISO** -- e o de meditar tambem: o corpo fica parado e o jogador le por que",
				  !treinaDepoisDoPedido && !meditaDepoisDoPedido
				  && pedidoT.Any(s => s.Contains("com esse peso você não consegue treinar") && s.Contains("8x"))
				  && pedidoMed.Any(s => s.Contains("com esse peso você não consegue meditar")),
				  $"treina {treinaDepoisDoPedido}, medita {meditaDepoisDoPedido}; disse \"{string.Join(" | ", pedidoT.Concat(pedidoMed))}\"");

		// O TREINO EM CURSO: comeca a 7,4x (aceito, e rende pelo `Treinar` de producao), o peso sobe a 8,6x, e o passe seguinte o derruba
		Vestir(Abaixo);
		DefinirAtividade(pl, Protocol.Activity.Treinando);
		bool comecou = f.train;
		double bpAntes = f.BP;
		for (int i = 0; i < 10; i++) { f.stamina = f.maxstamina; Treinar(pl); }
		double rendeuEmCurso = f.BP - bpAntes;
		Vestir(Acima);
		bpAntes = f.BP;
		List<string> caiu = Ouvir(() => Treinar(pl));
		double rendeuNoPasseQueCaiu = f.BP - bpAntes;
		AfirmarGm("P7. **O TREINO EM CURSO CAI QUANDO O PESO PASSA DE 8x**: a 7,4x o `Treinar` de producao rende; vestido mais, o passe seguinte derruba o "
				  + "treino com o aviso, sem render nada",
				  comecou && rendeuEmCurso > 0 && !f.train && rendeuNoPasseQueCaiu == 0
				  && caiu.Any(s => s.Contains("o peso passou de 8x o que o seu corpo aguenta") && s.Contains("você para de treinar")),
				  $"comecou {comecou}, rendeu +{rendeuEmCurso:0.###}; depois treina {f.train}, +{rendeuNoPasseQueCaiu}; disse \"{string.Join(" | ", caiu)}\"");
		DefinirAtividade(pl, Protocol.Activity.Parado);

		// O RESTO DO PESO FICA, a 20x: o `weight` no teto (ele divide o poder e cobra o vigor do treino), a prisao e o esmagamento pelo peso
		Vestir(Muito);
		AfirmarGm("P8. **O RESTO DO PESO FICA** (o dono so falou do treino): a 20x o `weight` continua 8 (o divisor do poder, `deBuff` 1/64), o corpo "
				  + "continua preso no chao e esmagado PELO PESO",
				  Math.Abs(f.weight_ratio - 20) < 0.1 && f.weight == 8 && f.BPrestriction == 8 && Math.Abs(f.deBuff - 1.0 / 64) < 1e-9
				  && PrensadoPelaGravidade(pl) && EsmagadoAgora(pl) && Esmagamento.PorPeso(f),
				  $"razao {f.weight_ratio:0.##}; weight {f.weight}; deBuff {f.deBuff:0.#####}; preso {PrensadoPelaGravidade(pl)}; esmagado {EsmagadoAgora(pl)}");

		// GRAVIDADE SEM PESO CONTINUA SENDO TREINO: 20x de gravidade dominando 1x (a razao 20 pela gravidade, nao pelo peso)
		Vestir("0");
		f.Planetgrav = 20;
		f.WeightTick();
		var tNaGravidade = T();
		AfirmarGm("P9. **SO O PESO CONTA**: 20x de gravidade sem peso nenhum (a razao do esmagamento 20, a do peso zero) continua sendo treino",
				  !f.PesoDemaisPraTreinar() && Esmagamento.Razao(f) >= 20 && tNaGravidade.Bp > 0,
				  $"razao do esmagamento {Esmagamento.Razao(f):0.#}; do peso {f.weight_ratio}; T +{tNaGravidade.Bp:0.###}");
		f.Planetgrav = 1;

		// O CONTRA-EXEMPLO: o peso demais volta a render (o jogo de antes)
		GainKnobs.PesoDemaisTreinaDeTeste = true;
		(double Bp, double Ki, double Folego) tDeAntes;
		double painelDeAntes;
		List<string> vestiuDeAntes, pedidoDeAntes;
		bool treinaDeAntes;
		try
		{
			vestiuDeAntes = Vestir(Muito);
			tDeAntes = T();
			painelDeAntes = f.MultiplicadorDeGanho();
			pedidoDeAntes = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Treinando));
			treinaDeAntes = f.train;
		}
		finally
		{
			GainKnobs.PesoDemaisTreinaDeTeste = false;
			DefinirAtividade(pl, Protocol.Activity.Parado);
		}
		AfirmarGm("P10. (defeito injetado: o peso demais que rende, o jogo de antes) a 20x a frase de vestir promete 8x de ganho, o T rende como a 4x, o "
				  + "painel diz 8x e o pedido de treinar passa -- as reguas P0, P1, P5 e P6 REPROVAM",
				  vestiuDeAntes.Any(s => s.Contains("e 8x de ganho no treino")) && tDeAntes.Bp > 0 && painelDeAntes > 1 && treinaDeAntes
				  && !pedidoDeAntes.Any(s => s.Contains("com esse peso")),
				  $"vestir: \"{string.Join(" | ", vestiuDeAntes)}\"; T +{tDeAntes.Bp:0.###}; painel {painelDeAntes:0.##}x; treina {treinaDeAntes}");

		Vestir("0");
		pl.Mochila.Tirar(pesos);
	}

	// =====================================================================
	// M) O MORTO SENTE O CHAO
	// =====================================================================
	private void OMortoSenteOChaoGm(ServerPlayer pl)
	{
		GD.Print("[gravmeta] -- M) o MORTO DE PE sente o peso e a gravidade como o vivo, em todo lugar; a morte dele e a mesa do Enma --");
		PalcoGm(pl, 50_000);
		Fighter f = pl.Ficha;
		const float T = ZoneCollision.TileSize;
		ZoneKey inferno = ZoneKey.Premade(Alem.ZonaDoInferno);
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		ZoneKey vegeta = ZoneKey.Premade("Vegeta");
		Vec2 portao = PortaoDoInferno(inferno);
		Vec2 mesa = MesaDoEnma(alem);
		Vec2 doKaioh = MapaDaZonaOuCatalogo(alem)?.PontoLivrePerto(new Vec2(150 * T + T / 2, 20 * T + T / 2)) ?? new Vec2(150 * T, 20 * T);
		Vec2 emVegeta = PontoDeNascimento(vegeta);
		string pesos = PesoDeTreino.IdDe(f.weight_cap_hw * 20, "");
		pl.Mochila.Guardar(pesos);

		// O CORPO NUM ESTADO DE MORTE, NUM LUGAR: inteiro e de pe, sem briga e sem peso, com a maestria de quem nunca treinou gravidade (1x)
		void Por(ZoneKey zona, Vec2 onde, bool viajou, bool manteveOCorpo = false, bool comPasse = false)
		{
			AjustarPeso(pl, pesos, "0");
			pl.Combate.Curar();
			f.dead = true;
			f.KO = false;
			pl.MorteJaViajou = viajou;
			pl.RelogioDaMorte = long.MaxValue;
			f.KeepsBody = manteveOCorpo;
			f.passe_do_enma_ate = comPasse ? NowMs() + 3_600_000 : 0;
			pl.PassagemAte = 0;
			f.hell_lockout_until = 0;
			pl.Combate.LutandoDeVerdade = pl.Combate.EmCombate = pl.Combate.BrigandoComAlguem = 0;
			MoveToZone(pl.Id, zona, onde);
			f.GravMastered = 1;
			f.Statify();
			f.PowerLevel(agoraMs: NowMs());
			f.WeightTick();
			_avisoDeEsmagamento.Remove(pl.Id);
		}
		void EmFarrapos()
		{
			foreach (Jandirus.Core.Combat.BodyPart p in pl.Combate.Corpo.Partes) p.Vida = p.VidaMax * 0.04;
			pl.Combate.SincronizarVida();
		}
		// UM SEGUNDO DE PRODUCAO (o `TickDoEsmagamento` do bloco de 1 Hz), com o que se pergunta antes e o que se ouviu
		(bool Preso, bool Anda, double HpAntes, double Hp, List<string> Disse, string Zona, bool Ko) UmSegundo()
		{
			bool preso = PrensadoPelaGravidade(pl), anda = PodeMexerOCorpo(pl);
			double hpAntes = f.HP;
			_avisoDeEsmagamento.Remove(pl.Id);
			List<string> disse = Ouvir(TickDoEsmagamento);
			return (preso, anda, hpAntes, f.HP, disse, pl.Zone.Name, f.KO);
		}
		static bool Disse(List<string> falas, string trecho) => falas.Any(s => s.Contains(trecho, StringComparison.OrdinalIgnoreCase));
		static string Falas(List<string> falas) => string.Join(" | ", falas);

		// M1. O FANTASMA NO INFERNO (10x), dominando 1x
		Por(inferno, portao, viajou: true);
		AfirmarGm("(montagem) o fantasma de pe no Inferno: morto, `MortoDePe`, o chao puxa 10x e ele domina 1x (a razao 10)",
				  f.dead && pl.MortoDePe && pl.Zone.Equals(inferno) && Esmagamento.Gravidade(f) == 10 && Math.Abs(Esmagamento.Razao(f) - 10) < 1e-9,
				  $"morto {f.dead}; de pe {pl.MortoDePe}; zona {pl.Zone.Name}; gravidade {Esmagamento.Gravidade(f)}; razao {Esmagamento.Razao(f):0.##}");
		var noInferno = UmSegundo();
		AfirmarGm("M1. **O MORTO SOFRE A GRAVIDADE** (*\"faça com q morto sofra dano de peso e gravidade\"*): no Inferno o fantasma fica PRESO no chao, "
				  + "perde vida no segundo de esmagamento e le o aviso -- como o vivo",
				  noInferno.Preso && !noInferno.Anda && noInferno.Hp < noInferno.HpAntes - 0.5 && Disse(noInferno.Disse, "ESMAGANDO SEU CORPO"),
				  $"preso {noInferno.Preso}; anda {noInferno.Anda}; vida {noInferno.HpAntes:0.#} -> {noInferno.Hp:0.#}; disse \"{Falas(noInferno.Disse)}\"");

		// M2. O CORPO EM FARRAPOS SE DESFAZ -- e a morte do morto e a de sempre: a mesa do Enma
		Por(inferno, portao, viajou: true);
		EmFarrapos();
		var desfeito = UmSegundo();
		AfirmarGm("M2. **O CORPO EM FARRAPOS SE DESFAZ, E O MORTO ACORDA NA MESA DO ENMA**: a 4% de vida e na razao 10, um segundo de esmagamento o desfaz "
				  + "(o `Body_Parts` do DM) -- e ele \"morre de novo\" como o morto morre: curado, na mesa do Enma, em coma curto",
				  desfeito.Zona == Alem.ZonaDoOutroMundo && Vec2.Distance(pl.Pos, mesa) < 4 * T && f.dead && desfeito.Ko && f.HP > 99
				  && Disse(desfeito.Disse, "vence o que restava do seu corpo") && Disse(desfeito.Disse, "acorda de novo na mesa do Enma"),
				  $"zona {desfeito.Zona}; a {Vec2.Distance(pl.Pos, mesa) / T:0.#} tiles da mesa; morto {f.dead}; coma {desfeito.Ko}; vida {f.HP:0.#}; "
				  + $"disse \"{Falas(desfeito.Disse)}\"");

		// M3. NUMA ILHA DO OUTRO MUNDO (o planeta do Sr. Kaioh, 10x)
		Por(alem, doKaioh, viajou: true);
		var naIlha = UmSegundo();
		AfirmarGm("M3. **...E NAS ILHAS DO OUTRO MUNDO** (\"Tudo, inclusive o 10x do Alem\"): no planeta do Sr. Kaioh o fantasma que domina 1x fica preso e se machuca",
				  pl.IlhaDoOutroMundo != null && Esmagamento.Gravidade(f) == 10 && naIlha.Preso && naIlha.Hp < naIlha.HpAntes - 0.5,
				  $"ilha '{pl.IlhaDoOutroMundo}'; gravidade {Esmagamento.Gravidade(f)}; preso {naIlha.Preso}; vida {naIlha.HpAntes:0.#} -> {naIlha.Hp:0.#}");

		// M4. O MORTO DO PASSE DO ENMA, ENTRE OS VIVOS (Vegeta, 10x)
		Por(vegeta, emVegeta, viajou: true, comPasse: true);
		var comPasse = UmSegundo();
		AfirmarGm("M4. **...E EM TODO LUGAR**: o morto com o passe do Enma em Vegeta (de pe entre os vivos) fica preso e se machuca como o vivo de la",
				  pl.MortoDePe && pl.EmVisita && Esmagamento.Gravidade(f) == 10 && comPasse.Preso && comPasse.Hp < comPasse.HpAntes - 0.5,
				  $"de pe {pl.MortoDePe}; passe {pl.EmVisita}; gravidade {Esmagamento.Gravidade(f)}; preso {comPasse.Preso}; vida {comPasse.HpAntes:0.#} -> {comPasse.Hp:0.#}");

		// M5. O PESO: o fantasma na mesa do Enma (1x), com o peso a 5x o limite do corpo. SO o verbo de producao (ele roda o tique inteiro): um
		// `WeightTick` solto depois dele, sem o `PowerLevel` no meio, inflava o recorde do corpo com o poder ainda sem o peso (a armadilha que o
		// `AjustarPeso` documenta) -- a r1 mediu a razao 2,6 em vez de 5.
		Por(alem, mesa, viajou: true);
		AjustarPeso(pl, pesos, "25");
		var comPeso = UmSegundo();
		AfirmarGm("M5. **...E O PESO TAMBEM**: na mesa do Enma (1x), com o peso a 5x o limite do corpo, o fantasma fica preso, se machuca e le que e o PESO",
				  Math.Abs(f.weight_ratio - 5) < 0.05 && Esmagamento.PorPeso(f) && comPeso.Preso && comPeso.Hp < comPeso.HpAntes - 0.5
				  && Disse(comPeso.Disse, "PESO ESTÁ ESMAGANDO"),
				  $"razao do peso {f.weight_ratio:0.##}; preso {comPeso.Preso}; vida {comPeso.HpAntes:0.#} -> {comPeso.Hp:0.#}; disse \"{Falas(comPeso.Disse)}\"");

		// M6. O CADAVER DOS 2 s (morto, antes da viagem) em Vegeta: fica de fora
		Por(vegeta, emVegeta, viajou: false);
		EmFarrapos();
		var cadaver = UmSegundo();
		AfirmarGm("M6. **O CADAVER DOS 2 s FICA DE FORA**: o corpo no chao antes da viagem, em Vegeta, nao e prensado nem ferido, nao se desfaz e nao ouve aviso",
				  EhCadaver(pl) && !cadaver.Preso && !EsmagadoAgora(pl) && Math.Abs(cadaver.Hp - cadaver.HpAntes) < 1e-9 && cadaver.Zona == "Vegeta"
				  && !Disse(cadaver.Disse, "ESMAGANDO") && !Disse(cadaver.Disse, "vence o que restava"),
				  $"cadaver {EhCadaver(pl)}; preso {cadaver.Preso}; vida {cadaver.HpAntes:0.#} -> {cadaver.Hp:0.#}; zona {cadaver.Zona}; disse \"{Falas(cadaver.Disse)}\"");

		// M7. QUEM FICOU COM O CORPO (`KeepsBody`) em Vegeta: como no DM -- esmagado (o aviso), mas cadaver: sem dano e sem se desfazer
		Por(vegeta, emVegeta, viajou: true, manteveOCorpo: true);
		EmFarrapos();
		var comCorpo = UmSegundo();
		AfirmarGm("M7. quem ficou com o corpo (`KeepsBody`) continua como no DM: e esmagado (le o aviso), mas e cadaver -- nao se fere e nao se desfaz",
				  EsmagadoAgora(pl) && Math.Abs(comCorpo.Hp - comCorpo.HpAntes) < 1e-9 && comCorpo.Zona == "Vegeta" && f.dead
				  && Disse(comCorpo.Disse, "ESMAGANDO") && !Disse(comCorpo.Disse, "vence o que restava"),
				  $"esmagado {EsmagadoAgora(pl)}; vida {comCorpo.HpAntes:0.#} -> {comCorpo.Hp:0.#}; zona {comCorpo.Zona}; disse \"{Falas(comCorpo.Disse)}\"");
		f.KeepsBody = false;

		// M8. A SAIDA: a maestria sobe estando no Inferno (o `GravGain` de cada passe de ficha, treinando ou nao) -- o laco preso -> morte ->
		// mesa -> volta (a pena puxa de volta ao portao) acaba quando ela alcanca a gravidade, ou quando a pena acaba (1 h no maximo)
		Por(inferno, portao, viajou: true);
		double maestria0 = f.GravMastered;
		for (int i = 0; i < 300; i++) Treinar(pl);
		double porMinuto = f.GravMastered - maestria0;
		double minAte4 = porMinuto > 0 ? (10.0 / Esmagamento.RazaoQuePrende - maestria0) / porMinuto : double.PositiveInfinity;
		double minAte1 = porMinuto > 0 ? (10.0 - maestria0) / porMinuto : double.PositiveInfinity;
		AfirmarGm("M8. **A SAIDA**: no Inferno a maestria do fantasma sobe a cada passe de ficha (um minuto de `Treinar` de producao, parado) -- o corpo "
				  + "preso se acostuma e deixa de ser preso (e ferido), entao o laco preso -> mesa do Enma -> portao nao trava ninguem pra sempre",
				  porMinuto > 0.2,
				  $"+{porMinuto:0.###}x por minuto: solta (razao abaixo de 4) em ~{minAte4:0.#} min e sem dano (razao 1) em ~{minAte1:0.#} min");

		// M9. O CONTRA-EXEMPLO: o morto de pe volta a nao sentir o chao (o `!dead || KeepsBody` do DM)
		MortoSemPesoDeTeste = true;
		try
		{
			Por(inferno, portao, viajou: true);
			var deAntes = UmSegundo();
			Por(inferno, portao, viajou: true);
			EmFarrapos();
			var farraposDeAntes = UmSegundo();
			AfirmarGm("M9. (defeito injetado: o morto isento do DM, o jogo de antes) no Inferno o fantasma anda solto, nao se fere nem le aviso, e em farrapos "
					  + "nao se desfaz -- as reguas M1 e M2 REPROVAM",
					  !deAntes.Preso && deAntes.Anda && Math.Abs(deAntes.Hp - deAntes.HpAntes) < 1e-9 && !Disse(deAntes.Disse, "ESMAGANDO")
					  && farraposDeAntes.Zona == Alem.ZonaDoInferno && !Disse(farraposDeAntes.Disse, "vence o que restava"),
					  $"preso {deAntes.Preso}; anda {deAntes.Anda}; vida {deAntes.HpAntes:0.#} -> {deAntes.Hp:0.#}; em farrapos ficou em {farraposDeAntes.Zona}");
		}
		finally { MortoSemPesoDeTeste = false; }

		AjustarPeso(pl, pesos, "0");
		pl.Mochila.Tirar(pesos);
	}
}
