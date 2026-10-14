using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DE ONDE O PODER DA LUTA VEM (`--ganhoteste`) -- as regras do dono (2026-10-14) que moram no
/// `GameServer.Ganhos.cs`. Dois clientes crus entram pela porta e viram JOGADORES de verdade (a troca de golpes so
/// existe entre jogadores); o NPC e um corpo forjado. Tudo pelo caminho de producao: o soco e o `Atacar`, o tiro e o
/// `BasicBlast`, e o voo dele e o `TickDosProjeteis`.
///
///   1. O SOCO: no ar nao rende; numa parede de verdade do mapa rende 0,1 do soco num NPC.
///   2. JOGADOR CONTRA JOGADOR: sem troca nao rende, pra quem bate nem pra quem apanha; com troca rende 2x o do NPC,
///      pros dois lados; a troca passa depois de 10 s; e apanhar de NPC continua rendendo.
///   3. O KI: no vento nao rende (nem no disparo, nem depois); acertando um NPC rende e paga o pacote do gesto; numa
///      parede paga o peso de objeto; num jogador fora da troca nao rende; e o tiro atirado num jogador marcado,
///      mesmo errando, abre a troca pra ele.
///   4. OS CONTRA-EXEMPLOS: `SocoNoArRendeDeTeste`, `KiRendeNoDisparoDeTeste` e `PvpSemTrocaDeTeste` poem cada regra
///      antiga de volta, e a prova de cada uma sabe reprovar.
///
/// AS RAZOES SAO DO SOCO, E NAO DO TIRO: o `Attack_Gain` e linear no multiplicador com o estado zerado (o mesmo
/// preparo da `GanhoDeUmSoco`), e por isso 0,1 e 2 se medem como razao. O `Blast_Gain` tem janela de tiro e sorteio
/// proprios, e o tiro se mede como zero ou mais que zero.
///
///     Godot --headless --path . --server --port 8290 --ganhoteste
/// </summary>
public partial class GameServer
{
	private int _gnOk, _gnFalhou;
	private bool _ganhoRodando, _ganhoJaRodou;
	private static readonly string[] ContasDaBancadaDeGanho = ["bancada_ganho_a", "bancada_ganho_b"];

	private void AfirmarGn(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _gnOk++; GD.Print($"[ganho]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _gnFalhou++; GD.PrintErr($"[ganho]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDeGanho(int porta)
	{
		if (_ganhoRodando || _ganhoJaRodou) return;
		_ganhoRodando = _ganhoJaRodou = true;
		_gnOk = _gnFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[ganho] ================ DE ONDE O PODER DA LUTA VEM (dono, 2026-10-14) ================");

		const float T = ZoneCollision.TileSize;
		const double bp = 1_000_000;
		var ca = new ClienteCruDeTeste();
		var cb = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, ca, cb);
		try
		{
			ServerPlayer? a = mesa.EntraNoMundo(ca, ContasDaBancadaDeGanho[0], "Lutador");
			ServerPlayer? b = mesa.EntraNoMundo(cb, ContasDaBancadaDeGanho[1], "Rival");
			AfirmarGn("(montagem) os dois clientes entram pela porta e sao JOGADORES -- a troca so existe entre jogadores; a Terra tem mapa",
					  a != null && b != null && EhJogador(a) && EhJogador(b) && _pjMapa != null);
			if (a == null || b == null || _pjMapa == null) return;

			ZoneKey terra = ZonaDaBancadaDeProjetil;
			foreach (ServerPlayer p in new[] { a, b })
				if (p.Zone.Hash != terra.Hash) MoveToZone(p.Id, terra, PontoDeNascimento(terra));
			Vec2 pista = TerraFirmeG12(40);                  // o corredor livre do soco e do tiro
			Vec2 longe = pista + new Vec2(0, 45 * T);        // onde fica quem nao esta na medida
			ServerPlayer n = ForjarV2("SacoDePancada", longe, bp);
			AfirmarGn("(montagem) o NPC e um corpo sem dono na tela: NAO e jogador, e contra ele a luta rende 1",
					  !EhJogador(n) && FatorDaLuta(a, n) == 1 && FatorDaLuta(n, a) == 1 && a.Zone.Hash == terra.Hash && b.Zone.Hash == terra.Hash);

			void Estacionar(params ServerPlayer[] quem)
			{
				for (int i = 0; i < quem.Length; i++) quem[i].Pos = longe + new Vec2(6 * T * (i + 1), 0);
			}
			void SemMarca() { a.AlvoId = b.AlvoId = 0; }
			void EsquecerAsTrocas()
			{
				_ultimoAtaque.Remove((a.Id, b.Id));
				_ultimoAtaque.Remove((b.Id, a.Id));
			}
			// UM SOCO DE PRODUCAO EM ALGUEM, com o preparo de todas as medidas desta bancada (o da `GanhoDeUmSoco`, mais
			// o vigor cheio: o `CapCheck` le o folego, e duas medidas com folegos diferentes nao dariam razao).
			SocoMedido SocoEm(ServerPlayer quem, ServerPlayer alvo)
			{
				SemMarca();
				// O TERCEIRO SAI DA PISTA: quem socou antes ficou parado no comeco dela, e o soco acha o mais perto.
				foreach (ServerPlayer outro in new[] { a, b, n })
					if (outro != quem && outro != alvo) Estacionar(outro);
				PrepararParaMedir(quem, bp);
				alvo.Altitude = 0;
				alvo.Ficha.expressedBP = 1_000;
				double mult = quem.Ficha.FightGainMult(alvo.Ficha, EhMeuMestre(quem, alvo), EhColegaDeMestre(quem, alvo));
				quem.Pos = pista;
				Encostar(quem, alvo, tiles: 1.0);
				string estado = EstadoDeQuemMede(quem);
				Atacar(quem, Protocol.Golpe.Leve);
				return new SocoMedido(quem.Ficha.BP - bp, mult, quem.Combate.Recarga > 0, quem.UltimoAlvo == alvo.Id, estado);
			}

			// ---------------------------------------------------------------- 1. o soco
			GD.Print("[ganho] -- 1) O SOCO: no ar nada, numa parede 0,1 do soco num NPC --");
			SemMarca();
			Estacionar(b, n);
			(double noAr, bool saiuNoAr) = GanhoDoSocoSemAlvo(a, pista, Facing.East, bp);
			AfirmarGn("**SOCAR O AR NAO RENDE BP**: o soco sai (o mesmo `Atacar` do pacote), nao acha ninguem nem nada, e o BP fica onde estava",
					  saiuNoAr && noAr == 0, $"saiu: {saiuNoAr}; ganho {noAr:0.##########}");

			SocoMedido noNpcA = SocoEm(a, n);
			Estacionar(n);
			SocoMedido noNpcB = SocoEm(b, n);
			Estacionar(n);
			AfirmarGn("(montagem) o soco no NPC acerta quem devia e rende BP -- e a regua das razoes abaixo",
					  noNpcA.Acertou && noNpcA.Ganho > 0 && noNpcB.Acertou && noNpcB.Ganho > 0 && Math.Abs(noNpcA.Mult - 1) < 1e-9,
					  $"A: {noNpcA} | B: {noNpcB}");

			Vec2? muro = DeFrenteProMuro(_pjMapa);
			(double naParede, bool saiuNaParede) = muro is { } m1 ? GanhoDoSocoSemAlvo(a, m1, Facing.North, bp) : (0, false);
			double razaoObjeto = noNpcA.Ganho > 0 ? naParede / noNpcA.Ganho : 0;
			AfirmarGn($"**SOCAR UM OBJETO RENDE {GainKnobs.GanhoDeObjeto:0.##} DO SOCO NUM NPC** -- o que menos rende (uma parede de verdade do mapa da Terra)",
					  muro != null && saiuNaParede && Math.Abs(razaoObjeto - GainKnobs.GanhoDeObjeto) < 0.003,
					  $"parede {(muro == null ? "nao achada" : $"{naParede:0.##########}")}; razao {razaoObjeto:0.####}");

			// ---------------------------------------------------------------- 2. jogador contra jogador
			GD.Print("[ganho] -- 2) JOGADOR CONTRA JOGADOR: so numa troca de golpes, e ai rende o dobro --");
			EsquecerAsTrocas();
			SocoMedido semTroca = SocoEm(a, b);
			AfirmarGn("**BATER EM QUEM NAO TE ATACOU NAO RENDE**: o soco no outro jogador sai e chega nele, e o BP de quem bateu fica onde estava",
					  semTroca.Acertou && semTroca.Ganho == 0, $"acertou: {semTroca.Acertou}; ganho {semTroca.Ganho:0.##########}");
			(bool apanhouSemTroca, double ganhoDeQuemApanhouSemTroca) = GanhoDeQuemApanha(a, b, pista, bp);
			AfirmarGn("**APANHAR SEM REVIDAR TAMBEM NAO RENDE**: o golpe ENCOSTA no outro jogador (o corpo dele muda) e ele nao ganha nada",
					  apanhouSemTroca && ganhoDeQuemApanhouSemTroca == 0, $"encostou: {apanhouSemTroca}; ganho {ganhoDeQuemApanhouSemTroca:0.##########}");

			SocoMedido revide = SocoEm(b, a);
			double razaoRevide = noNpcB.Ganho > 0 ? revide.Ganho / noNpcB.Ganho : 0;
			AfirmarGn($"**QUEM REVIDA ESTA NA TROCA, E ELA E O MAIOR GANHO: {GainKnobs.GanhoDaTrocaPvp:0.#}x O SOCO NUM NPC** -- o outro o atacou ha um instante",
					  revide.Acertou && Math.Abs(razaoRevide - GainKnobs.GanhoDaTrocaPvp) < 0.06, $"razao {razaoRevide:0.###}; {revide}");
			SocoMedido naTroca = SocoEm(a, b);
			double razaoTroca = noNpcA.Ganho > 0 ? naTroca.Ganho / noNpcA.Ganho : 0;
			AfirmarGn("...e o primeiro, agora que apanhou de volta, tambem: a troca rende pros dois",
					  naTroca.Acertou && Math.Abs(razaoTroca - GainKnobs.GanhoDaTrocaPvp) < 0.06, $"razao {razaoTroca:0.###}; {naTroca}");
			(bool apanhouNaTroca, double ganhoDeQuemApanhouNaTroca) = GanhoDeQuemApanha(a, b, pista, bp);
			AfirmarGn("...e na troca quem APANHA tambem ganha", apanhouNaTroca && ganhoDeQuemApanhouNaTroca > 0,
					  $"encostou: {apanhouNaTroca}; ganho {ganhoDeQuemApanhouNaTroca:0.##########}");

			_ultimoAtaque[(b.Id, a.Id)] = NowMs() - JanelaDaTrocaMs - 500;
			SocoMedido passou = SocoEm(a, b);
			_ultimoAtaque[(b.Id, a.Id)] = NowMs() - JanelaDaTrocaMs + 500;
			SocoMedido aindaDentro = SocoEm(a, b);
			AfirmarGn($"**A TROCA DURA {GainKnobs.SegundosDaTroca:0} s**: o ataque do outro de {GainKnobs.SegundosDaTroca:0},5 s atras ja nao abre nada; o de {GainKnobs.SegundosDaTroca - 0.5:0.#} s atras ainda abre",
					  passou.Acertou && passou.Ganho == 0 && aindaDentro.Acertou && aindaDentro.Ganho > 0,
					  $"fora: {passou} | dentro: {aindaDentro}");

			Estacionar(b);
			(bool apanhouDoNpc, double ganhoDeQuemApanhouDoNpc) = GanhoDeQuemApanha(n, a, pista, bp);
			AfirmarGn("**APANHAR DE NPC CONTINUA RENDENDO**: o golpe do NPC encosta no jogador, e ele ganha", apanhouDoNpc && ganhoDeQuemApanhouDoNpc > 0,
					  $"encostou: {apanhouDoNpc}; ganho {ganhoDeQuemApanhouDoNpc:0.##########}");
			Estacionar(n);

			// ---------------------------------------------------------------- 3. o ki
			GD.Print("[ganho] -- 3) O KI: so encostando em alguem ou em algo --");
			SemMarca();
			TiroMedido aoVento = UmTiro(a, pista, Facing.East, bp);
			AfirmarGn("**O KI NO VENTO NAO RENDE**: a bola sai, voa o alcance inteiro sem encostar em nada, e o BP nao se mexe -- nem no disparo, nem depois",
					  aoVento.Saiu && aoVento.Morreu && aoVento.NoDisparo == 0 && aoVento.NoFim == 0 && aoVento.Pendentes > 0,
					  aoVento.ToString());

			n.Pos = pista + new Vec2(6 * T, 0);
			n.Facing = Facing.West;
			TiroMedido noNpc = UmTiro(a, pista, Facing.East, bp);
			Estacionar(n);
			AfirmarGn("**O KI QUE ACERTA UM NPC RENDE**, e o ganho do gesto (o `Blast_Gain` do disparo) sai no acerto, com o peso dele",
					  noNpc.Saiu && noNpc.Fim == FimDeProjetil.Acertou && noNpc.NoDisparo == 0 && noNpc.NoFim > 0 && noNpc.Pendentes == 0 && noNpc.Fator == 1,
					  noNpc.ToString());

			Vec2? muro2 = DeFrenteProMuro(_pjMapa);
			TiroMedido naParedeKi = muro2 is { } m2 ? UmTiro(a, m2, Facing.North, bp) : default;
			AfirmarGn($"**O KI NUMA PAREDE RENDE COMO OBJETO**: o pacote do gesto e pago com o peso {GainKnobs.GanhoDeObjeto:0.##}",
					  muro2 != null && naParedeKi.Saiu && naParedeKi.Fim == FimDeProjetil.Cenario && naParedeKi.NoFim > 0
					  && naParedeKi.Pendentes == 0 && Math.Abs(naParedeKi.Fator - GainKnobs.GanhoDeObjeto) < 1e-9,
					  muro2 == null ? "parede nao achada" : naParedeKi.ToString());

			EsquecerAsTrocas();
			b.Pos = pista + new Vec2(6 * T, 0);
			b.Facing = Facing.West;
			a.AlvoId = b.Id;
			TiroMedido noJogador = UmTiro(a, pista, Facing.East, bp, manterMarca: true);
			AfirmarGn("**O KI NUM JOGADOR FORA DA TROCA NAO RENDE**: a bola acerta, e nem o acerto nem o gesto pagam",
					  noJogador.Saiu && noJogador.Fim == FimDeProjetil.Acertou && noJogador.NoFim == 0 && noJogador.Pendentes > 0,
					  noJogador.ToString());

			EsquecerAsTrocas();
			Estacionar(b);
			a.AlvoId = b.Id;
			TiroMedido errou = UmTiro(a, pista, Facing.East, bp, manterMarca: true);
			bool marcadoLonge = Marcado(a) == b && Vec2.Distance(a.Pos, b.Pos) > 40 * T;
			SocoMedido depoisDoTiro = SocoEm(b, a);
			double razaoDepoisDoTiro = noNpcB.Ganho > 0 ? depoisDoTiro.Ganho / noNpcB.Ganho : 0;
			AfirmarGn("**O TIRO ATIRADO NUM JOGADOR MARCADO E ATAQUE, MESMO ERRANDO**: ele nao chegou (o outro estava longe demais), e o soco de volta ja rende o dobro",
					  marcadoLonge && errou.Saiu && errou.Fim != FimDeProjetil.Acertou && depoisDoTiro.Acertou && Math.Abs(razaoDepoisDoTiro - GainKnobs.GanhoDaTrocaPvp) < 0.06,
					  $"{errou}; razao do soco de volta {razaoDepoisDoTiro:0.###}");

			// ---------------------------------------------------------------- 4. os contra-exemplos
			GD.Print("[ganho] -- 4) OS CONTRA-EXEMPLOS: cada regra antiga de volta --");
			SemMarca();
			Estacionar(b, n);
			GainKnobs.SocoNoArRendeDeTeste = true;
			(double noArDeAntes, bool saiuNoArDeAntes) = GanhoDoSocoSemAlvo(a, pista, Facing.East, bp);
			GainKnobs.SocoNoArRendeDeTeste = false;
			AfirmarGn("(defeito injetado: o soco no ar de antes) **SOCAR O VENTO RENDE BP**. A prova do soco no ar sabe reprovar",
					  saiuNoArDeAntes && noArDeAntes > 0, $"saiu: {saiuNoArDeAntes}; ganho {noArDeAntes:0.##########}; {EstadoDeQuemMede(a)}");

			GainKnobs.KiRendeNoDisparoDeTeste = true;
			TiroMedido aoVentoDeAntes = UmTiro(a, pista, Facing.East, bp);
			GainKnobs.KiRendeNoDisparoDeTeste = false;
			AfirmarGn("(defeito injetado: o ki de antes) **A BOLA QUE NAO ACERTA NADA RENDE NO DISPARO**. A prova do ki no vento sabe reprovar",
					  aoVentoDeAntes.Saiu && aoVentoDeAntes.NoDisparo > 0, aoVentoDeAntes.ToString());

			EsquecerAsTrocas();
			GainKnobs.PvpSemTrocaDeTeste = true;
			SocoMedido semTrocaDeAntes = SocoEm(a, b);
			GainKnobs.PvpSemTrocaDeTeste = false;
			double razaoDeAntes = noNpcA.Ganho > 0 ? semTrocaDeAntes.Ganho / noNpcA.Ganho : 0;
			AfirmarGn("(defeito injetado: o PvP de antes) **BATER EM QUEM NAO REVIDA RENDE COMO NUM NPC**. A prova da troca sabe reprovar",
					  semTrocaDeAntes.Acertou && Math.Abs(razaoDeAntes - 1) < 0.03, $"razao {razaoDeAntes:0.###}; {semTrocaDeAntes}");
		}
		catch (Exception e)
		{
			AfirmarGn("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			GainKnobs.SocoNoArRendeDeTeste = GainKnobs.KiRendeNoDisparoDeTeste = GainKnobs.PvpSemTrocaDeTeste = false;
			LimparTudoDaBancada();
			mesa.Fecha();
			ApagarContasDaBancadaDeGanho();
			_ganhoRodando = false;
			GD.Print($"[ganho] ================ {_gnOk} OK, {_gnFalhou} FALHA(S) ================");
		}
	}

	/// <summary>O que um soco de bancada rendeu, se ele saiu, se chegou no alvo, e o estado de quem bateu (pro relato).</summary>
	private readonly record struct SocoMedido(double Ganho, double Mult, bool Saiu, bool Acertou, string Estado)
	{
		public override string ToString() => $"saiu {Saiu}, acertou {Acertou}, ganho {Ganho:0.##########} (x{Mult:0.##}); {Estado}";
	}

	private static string EstadoDeQuemMede(ServerPlayer q) =>
		$"teto {q.Ficha.relBPmax / Math.Max(q.Ficha.BP, 1):0.###}x o BP, KO {q.Ficha.KO}, atordoado {q.Combate.Stun:0.##}, voo {q.TiquesDeVoo}, zona x{q.Ficha.zoneGainMult:0.##}";

	/// <summary>
	/// O estado de quem bate, como o da `GanhoDeUmSoco`: o BP dado, o vigor cheio, e nada que carregue de uma medida pra
	/// outra -- nem o atordoamento e o voo de um golpe que ele levou antes, que no jogo passariam com os tiques (a
	/// bancada roda tudo num quadro so).
	/// </summary>
	private static void PrepararParaMedir(ServerPlayer quem, double bp)
	{
		quem.Combate.Stun = 0;
		quem.TiquesDeVoo = 0;
		quem.Ficha.stamina = quem.Ficha.maxstamina;
		PorBp(quem, bp);
		quem.Ficha.expressedBP = 1_000;
		quem.Ficha.hiddenpotential = 0;
		quem.Ficha.tmp_activ_gains = 0;
		quem.Ficha.BPBuffer = 0;
		quem.Combate.Recarga = 0;
		quem.AtaqueAte = 0;
		quem.UltimoAlvo = 0;
		quem.Ficha.Ki = quem.Ficha.MaxKi;
		quem.Altitude = 0;
	}

	/// <summary>Um soco de producao sem ninguem na frente (no ar, ou numa parede): o BP que rendeu, e se o soco saiu.</summary>
	private (double Ganho, bool Saiu) GanhoDoSocoSemAlvo(ServerPlayer quem, Vec2 onde, Facing rumo, double bp)
	{
		PrepararParaMedir(quem, bp);
		quem.Pos = onde;
		quem.Facing = rumo;
		Atacar(quem, Protocol.Golpe.Leve);
		return (quem.Ficha.BP - bp, quem.Combate.Recarga > 0);
	}

	/// <summary>
	/// QUEM APANHA: `quemBate` soca `quemApanha` ate um golpe ENCOSTAR (o corpo dele muda), no maximo 30 vezes -- o
	/// esquivo e o aparo nao contam. Devolve se encostou e o BP que `quemApanha` ganhou com esse golpe.
	/// </summary>
	private (bool Encostou, double Ganho) GanhoDeQuemApanha(ServerPlayer quemBate, ServerPlayer quemApanha, Vec2 onde, double bp)
	{
		for (int i = 0; i < 30; i++)
		{
			quemBate.AlvoId = quemApanha.AlvoId = 0;
			PrepararParaMedir(quemBate, bp);
			PrepararParaMedir(quemApanha, bp);
			quemApanha.Combate.Guardar(false);
			quemBate.Pos = onde;
			Encostar(quemBate, quemApanha, tiles: 1.0);
			Dictionary<string, double[]> antes = FotografarCorpo(quemApanha.Combate);
			Atacar(quemBate, Protocol.Golpe.Leve);
			if (!MesmosMembrosDeTeste(antes, FotografarCorpo(quemApanha.Combate))) return (true, quemApanha.Ficha.BP - bp);
		}
		return (false, 0);
	}

	/// <summary>O que um tiro de bancada deixou: o BP no disparo e no fim, como acabou, e o pacote do gesto.</summary>
	private readonly record struct TiroMedido(bool Saiu, bool Morreu, FimDeProjetil Fim, double NoDisparo, double NoFim, int Pendentes, double Fator)
	{
		public override string ToString() =>
			$"saiu {Saiu}, acabou {(Morreu ? Fim.ToString() : "VIVO")}; BP no disparo +{NoDisparo:0.##########}, no fim +{NoFim:0.##########}; pacote: {Pendentes} pendente(s), peso {Fator:0.##}";
	}

	/// <summary>
	/// UMA BOLA DE KI DE PRODUCAO (`BasicBlast`, o verbo do botao), voando pelos tiques de producao ate acabar. Cada tiro
	/// e um gesto proprio: no jogo eles caem em quadros diferentes, e a bancada, que roda tudo num quadro so, fecha o
	/// pacote do gesto anterior antes de atirar.
	/// </summary>
	private TiroMedido UmTiro(ServerPlayer quem, Vec2 onde, Facing rumo, double bp, bool manterMarca = false)
	{
		int marca = quem.AlvoId;
		PrepararParaMedir(quem, bp);
		quem.AlvoId = manterMarca ? marca : 0;
		quem.Pos = onde;
		quem.Facing = rumo;
		_blastPronto.Remove(quem.Id);
		_gestoDeKi.Remove(quem.Id);
		List<Projetil> lista = ProjeteisDaZona(quem.Zone.Hash);
		int antes = lista.Count;
		BasicBlast(quem);
		double noDisparo = quem.Ficha.BP - bp;
		Projetil? tiro = lista.Count > antes ? lista[^1] : null;
		if (tiro == null || tiro.Dono != quem.Id) return new TiroMedido(false, false, FimDeProjetil.Nenhum, noDisparo, noDisparo, 0, 0);
		for (int i = 0; i < 600 && tiro.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
		_pacoteDoTiro.TryGetValue(tiro, out PacoteDeKi? pacote);
		return new TiroMedido(true, !tiro.Vivo, tiro.Fim, noDisparo, quem.Ficha.BP - bp, pacote?.Ganhos.Count ?? -1, pacote?.Fator ?? -1);
	}

	/// <summary>Uma celula de parede que o punho alcanca, com chao livre ao sul: o ponto de onde socar (ou atirar) olhando pro norte.</summary>
	private static Vec2? DeFrenteProMuro(ZoneCollision mapa)
	{
		const int t = ZoneCollision.TileSize;
		for (int y = 10; y < 240; y++)
			for (int x = 10; x < 240; x++)
				if (ClasseDeAgua.SocoAlcanca(mapa, x, y) && !mapa.Indestrutivel(x, y) && !mapa.NaBorda(x, y)
					&& !mapa.BlockedCell(x, y + 1) && !mapa.EhAgua(x, y + 1) && !mapa.NaBorda(x, y + 1))
					return new Vec2(x * t + t / 2f, (y + 1) * t + t / 2f);
		return null;
	}

	/// <summary>Apaga os arquivos das contas da bancada: conta reaproveitada traria o personagem de ontem.</summary>
	private void ApagarContasDaBancadaDeGanho()
	{
		if (_store == null) return;
		foreach (string conta in ContasDaBancadaDeGanho)
			try
			{
				string caminho = System.IO.Path.Combine(_store.Pasta, conta + ".json");
				if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
			}
			catch (Exception e) { GD.PushWarning($"[ganho] nao apaguei '{conta}': {e.Message}"); }
	}
}
