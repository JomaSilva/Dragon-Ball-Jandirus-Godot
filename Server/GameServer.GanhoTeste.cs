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
///   6. (2026-10-16) O LUGAR MULTIPLICA O TIQUE: um minuto de `Treinar` de producao em 1x, 10x e 100x, com peso, meditando e
///      no espaco, e o soco de producao em 10x -- o BP medido segue o numero do painel (1x sem nada); a aclimatacao NAO mexe no
///      ganho (e PODER: o `gravFelt`, que o pacote da aba de Formas leva). Contra-exemplo: `GainKnobs.GravidadeNaoMultiplicaDeTeste`.
///   7. (2026-10-26) O BANCO DO POTENCIAL, COMO NO DU (o pedido 95): o potencial guardado nao pinga mais no BP -- no treino em 1x e a
///      500x com o peso no teto, meditando e no soco, o mesmo minuto com dez vezes o BP de potencial rende o mesmo que sem --, e cada
///      ganho guarda de 20 a 40% de si no banco (30 a 60% no Prodigial). O caso do relato de 2026-10-20 (uma hora a 500x, de 1B com
///      14B de potencial) sobe so o esforco. Contra-exemplo: `Fighter.PotencialSemBancoDeTeste`. (Era "o potencial pinga igual em
///      qualquer lugar", de 2026-10-20 a 2026-10-25.)
///   7c. (2026-10-26) O ESTOQUE ANTIGO SAI DO SAVE, UMA VEZ: pelo `AccountStore.ParaJogador`, um save de antes do banco chega com o
///      potencial oculto do sistema velho e sai com o banco zerado; um ja migrado guarda o banco. Contra-exemplo: o mutante do zerar.
///   8. (2026-10-20) O "VIEW SESSION" DIZ A MEDIA DE GANHO POR SEGUNDO, com o scouter em BP e sem ele em porcentagem. Contra-exemplo:
///      `SessaoSemMediaDeTeste`.
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
			// o vigor cheio: o `CapCheck` le o folego, e duas medidas com folegos diferentes nao dariam razao). O potencial
			// oculto e zero, menos na familia 7, que o mede.
			SocoMedido SocoEm(ServerPlayer quem, ServerPlayer alvo, double potencial = 0)
			{
				SemMarca();
				// O TERCEIRO SAI DA PISTA: quem socou antes ficou parado no comeco dela, e o soco acha o mais perto.
				foreach (ServerPlayer outro in new[] { a, b, n })
					if (outro != quem && outro != alvo) Estacionar(outro);
				PrepararParaMedir(quem, bp);
				quem.Ficha.hiddenpotential = potencial;
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

			// ---------------------------------------------------------------- 5. o ritmo do jogo
			// O dono (2026-10-16): *"diminuir os ganhos em 20x do jogo em si"*; e (2026-10-23), junto do morto que passou a sentir o peso e a
			// gravidade: *"suba o bp gain em 10x (tinha dividido por 20 antes entao agr seria so dividido por 2)"*. O MESMO soco de producao no
			// NPC, medido na taxa do jogo e na do DM (`BPTickDoDm`): a do DM rende 2x a de agora. O 2 e o do dono, escrito AQUI (a regua nao le o
			// `BPTick` que confere). Contra-exemplo: o ritmo de 2026-10-16 (`BPTickDoDm / 20`), posto no mesmo campo -- a mesma regua REPROVA.
			GD.Print("[ganho] -- 5) O RITMO DO JOGO: o ganho de BP a metade do do DM (dono, 2026-10-23; de 2026-10-16 a 2026-10-23, 20x menor) --");
			Estacionar(b);
			double ritmo = GainKnobs.BPTick;
			SocoMedido SocoNoRitmo(double taxa)
			{
				GainKnobs.BPTick = taxa;
				try { return SocoEm(a, n); }
				finally { GainKnobs.BPTick = ritmo; Estacionar(n); }
			}
			SocoMedido agora = SocoNoRitmo(ritmo);
			SocoMedido doDm = SocoNoRitmo(GainKnobs.BPTickDoDm);
			SocoMedido de1016 = SocoNoRitmo(GainKnobs.BPTickDoDm / 20);
			double vezes = agora.Ganho > 0 ? doDm.Ganho / agora.Ganho : 0;
			double vezesDeAntes = de1016.Ganho > 0 ? doDm.Ganho / de1016.Ganho : 0;
			AfirmarGn("**O GANHO DE BP E A METADE DO DO DM** (*\"tinha dividido por 20 antes entao agr seria so dividido por 2\"*): o mesmo soco no mesmo NPC "
					  + "rende metade do que rende com a taxa global do DM -- dez vezes o de 2026-10-16",
					  agora.Acertou && doDm.Acertou && agora.Ganho > 0 && Math.Abs(vezes - 2) < 0.05,
					  $"agora {agora.Ganho:0.##########} | na taxa do DM {doDm.Ganho:0.##########} | {vezes:0.###}x");
			AfirmarGn("(defeito injetado: o ritmo de 2026-10-16, a taxa do DM / 20) o mesmo soco rende um vinte avos do do DM -- a regua de cima REPROVA",
					  de1016.Acertou && Math.Abs(vezesDeAntes - 2) >= 0.05 && Math.Abs(vezesDeAntes - 20) < 0.5,
					  $"de 2026-10-16 {de1016.Ganho:0.##########} | {vezesDeAntes:0.###}x");

			// ---------------------------------------------------------------- 6. o lugar multiplica o tique
			// O dono (2026-10-16): *"em 1x gravidade diz 4,8x e em 10x diz 15,63x oq daria uma grande diferença, pq eu lembro q no DM esses
			// valores realmente multiplicavam o bp gain e aqui deveria ser assim tb"*. UM MINUTO de treino pelo `Treinar` de producao (300
			// passadas, o `TickFichas` a 5 Hz) com tudo que nao e o lugar congelado -- o preparo da `--salateste` (`OQuantoRendeDeVerdade`) --
			// e o potencial oculto em ZERO: desde 2026-10-20 ele pinga igual em qualquer lugar (a familia 7 o mede), e aqui se mede o que o
			// lugar multiplica, o esforco.
			GD.Print("[ganho] -- 6) A GRAVIDADE E O PESO MULTIPLICAM O TIQUE, E A ACLIMATACAO E PODER: o painel diz o que o treino paga (dono, 2026-10-16) --");
			Estacionar(b, n);
			// `pesoMaximoEmLimites`: o maximo do peso em vezes o limite do corpo NESTA gravidade (o "100" vira essa razao); zero = o dobro do
			// limite, a escala do verbo. (2026-10-23) A familia 7 o usa: a 500x o "100" do peso de sempre e 1000x o limite, e acima de 8x o
			// treino nao rende mais nada (`Fighter.PesoDemaisPraTreinar`) -- o "peso no teto" dela passou a ser o do teto do GANHO (8x).
			(double Bp, double Painel, double Banco) UmMinuto(double gravidade, double maestria, string peso = "0", bool meditando = false,
												double potencial = 0, double bpDePartida = 0, int passadas = 300, double pesoMaximoEmLimites = 0)
			{
				Fighter f = a.Ficha;
				PrepararParaMedir(a, bpDePartida > 0 ? bpDePartida : bp);
				// AS FAMILIAS DE CIMA SOCARAM: o corpo ainda esta "em briga", e a briga interrompe o treino (`PorQueNaoTreinaNemMedita`).
				a.Combate.LutandoDeVerdade = a.Combate.EmCombate = a.Combate.BrigandoComAlguem = 0;
				f.IsInFight = false;
				f.Tick(agoraMs: NowMs());          // o recorde do corpo pro peso (`weight_cap_hw`) sai do `WeightTick`
				f.hiddenpotential = potencial;
				f.UPMod = 1e6;                     // o teto pessoal fora da conta (ver a `--salateste`)
				f.bp_milestone_mult = 1;
				f.BPBuffer = 0;
				f.Gaintimer = 0;
				f.Planetgrav = gravidade;
				f.gravmult = 0;
				f.GravMastered = maestria;
				f.zoneGainMult = 1;
				// (2026-10-18) os pesos de bancada sao um ITEM, com o maximo no id (o dobro do limite do corpo -- a escala do verbo)
				string pesos = Jandirus.Core.Items.PesoDeTreino.IdDe(pesoMaximoEmLimites > 0
					? f.weight_cap_hw * pesoMaximoEmLimites / Math.Max(gravidade, 1)
					: f.weight_cap_hw * GainKnobs.WeightItemCapMult, "");
				a.Mochila.Guardar(pesos);
				AjustarPeso(a, pesos, peso);
				f.Statify();
				f.PowerLevel();                    // o teto pessoal com o UPMod novo, e o `WeightTick` com o peso novo
				f.stamina = f.maxstamina;
				f.Statify();                       // o `StamBPGainMod` no teto
				f.train = !meditando;
				f.med = meditando;
				double painel = f.MultiplicadorDeGanho();
				double antes = f.BP;
				for (int i = 0; i < passadas; i++)
				{
					f.stamina = f.maxstamina;      // o treino cansa (`PagarOVigorDoTreino`), e o folego nao e o assunto
					Treinar(a);
				}
				double ganho = f.BP - antes;
				f.train = f.med = false;
				AjustarPeso(a, pesos, "0");
				a.Mochila.Tirar(pesos);
				return (ganho, painel, f.hiddenpotential - potencial);   // (2026-10-26) o que o minuto guardou no banco
			}
			static double Vezes(double x, double y) => y > 0 ? x / y : 0;
			static bool Bate(double real, double esperado) => esperado > 0 && Math.Abs(real / esperado - 1) < 0.02;

			// (2026-10-21) O VIGOR ENTROU NO PAINEL (dono: *"coloque no bp gain no painel esse multiplicador de vigor, pros jogadores
			// saberem"*). Sem gravidade, sem peso e fora da Sala o painel diz SO o vigor: 1,25x de tanque cheio (o `UmMinuto` enche o
			// tanque) e 1x pela metade -- o "sem nada, 1x" de 2026-10-16. A raca (`Egains`) continua paga so no tique: e do corpo.
			double PainelComOTanqueEm(double fracao)
			{
				Fighter f = a.Ficha;
				double guardado = f.stamina;
				f.stamina = f.maxstamina * fracao;
				f.Statify();
				double p = f.MultiplicadorDeGanho();
				f.stamina = guardado;
				f.Statify();
				return p;
			}
			var em1 = UmMinuto(1, 1);
			double meioTanque = PainelComOTanqueEm(0.5);   // logo depois do de 1x: o corpo ainda esta sem gravidade, sem peso e fora da Sala
			var em10 = UmMinuto(10, 10);
			var em100 = UmMinuto(100, 100);
			AfirmarGn("**SEM NADA, O PAINEL DIZ SO O VIGOR** (dono, 2026-10-21: *\"coloque no bp gain no painel esse multiplicador de vigor\"*): de tanque cheio 1,25x, e pela metade 1x (*\"o bp gain mais baixo quando vc n tem aclimation nem nada deveria ser 1x\"*)",
					  Math.Abs(em1.Painel - 1.25) < 1e-9 && Math.Abs(meioTanque - 1) < 1e-9, $"cheio {em1.Painel:0.###}x; metade {meioTanque:0.###}x");
			AfirmarGn("**TREINAR EM 10x RENDE 10x O DE 1x** -- e o painel diz o mesmo: o numero da tela e o que o tique paga",
					  Bate(Vezes(em10.Bp, em1.Bp), 10) && Bate(Vezes(em10.Painel, em1.Painel), 10),
					  $"1x: {em1.Bp:0.####} BP/min, painel {em1.Painel:0.##}x | 10x: {em10.Bp:0.####} BP/min, painel {em10.Painel:0.##}x | {Vezes(em10.Bp, em1.Bp):0.###}x");
			AfirmarGn("...e em 100x, 100x", Bate(Vezes(em100.Bp, em1.Bp), 100) && Bate(Vezes(em100.Painel, em1.Painel), 100),
					  $"{Vezes(em100.Bp, em1.Bp):0.###}x; painel {Vezes(em100.Painel, em1.Painel):0.###}x");

			var aclimatado = UmMinuto(1, 10);
			// (2026-10-21) o painel de quem domina e o MESMO de quem nao domina (e nao mais "1x": o vigor cheio esta nos dois)
			AfirmarGn("**A ACLIMATACAO NAO MEXE NO GANHO** (*\"fora isso ele n deveria dar ganho de bp eterno\"*): dominando 10x, treinar em 1x rende o mesmo que sem dominar -- e o painel diz o mesmo",
					  Bate(Vezes(aclimatado.Bp, em1.Bp), 1) && Math.Abs(aclimatado.Painel - em1.Painel) < 1e-9,
					  $"{Vezes(aclimatado.Bp, em1.Bp):0.###}x; painel {aclimatado.Painel:0.###}x contra {em1.Painel:0.###}x");

			// ...ELA E PODER: o `gravFelt` do DM (soma na base), e o pacote da aba de Formas o leva (a linha "Aclimatado")
			double PoderAclimatado(double maestria)
			{
				a.Ficha.Planetgrav = 1;
				a.Ficha.gravmult = 0;
				a.Ficha.GravMastered = maestria;
				a.Ficha.Statify();
				a.Ficha.PowerLevel();
				var ouvidos = new List<Protocol.AtributosState>();
				List<Protocol.AtributosState>? escutaAntes = EscutaDeAtributos;
				EscutaDeAtributos = ouvidos;
				try { a.SigAtributos = ""; MandarAtributos(a); }
				finally { EscutaDeAtributos = escutaAntes; }
				return ouvidos.Count > 0 ? ouvidos[^1].Aclimatacao : -1;
			}
			double doGravFelt = Jandirus.Core.Stats.StatCurves.GravFelt(10, 1);
			double noFio = PoderAclimatado(10);
			AfirmarGn($"**...ELA E PODER**: dominando 10x num chao de 1x o corpo ganha +{(doGravFelt - 1) * 100:0}% na base (o `gravFelt`), e o pacote da aba de Formas leva o bonus",
					  Math.Abs(a.Ficha.gravBuff - doGravFelt) < 1e-6 && Math.Abs(noFio - doGravFelt) < 1e-3, $"gravBuff {a.Ficha.gravBuff:0.####}; no fio {noFio:0.####}");
			double semDominio = PoderAclimatado(1);
			AfirmarGn("...e sem dominar mais que o chao, o pacote leva 1 (a linha da aba nao existe)", Math.Abs(semDominio - 1) < 1e-6, $"{semDominio:0.####}");

			var comPeso = UmMinuto(1, 1, "50");
			AfirmarGn("**O PESO PAGA**: no limite do corpo (50% do peso maximo, razao 1) o treino rende 2x -- e o painel diz 2x",
					  Bate(Vezes(comPeso.Bp, em1.Bp), 2) && Bate(Vezes(comPeso.Painel, em1.Painel), 2),
					  $"{Vezes(comPeso.Bp, em1.Bp):0.###}x; painel {Vezes(comPeso.Painel, em1.Painel):0.###}x");

			var medita1 = UmMinuto(1, 1, meditando: true);
			var medita10 = UmMinuto(10, 10, meditando: true);
			AfirmarGn("**MEDITAR EM 10x RENDE 10x O DE 1x** (a gravidade multiplica a meditacao tambem; o painel e do treino, que leva ainda o peso e a zona)",
					  medita1.Bp > 0 && Bate(Vezes(medita10.Bp, medita1.Bp), 10), $"{medita1.Bp:0.####} -> {medita10.Bp:0.####} BP/min ({Vezes(medita10.Bp, medita1.Bp):0.###}x)");

			var noEspaco = UmMinuto(0, 1);
			AfirmarGn("no ESPACO (gravidade zero) o treino rende o de 1x: a gravidade nao ajuda, mas nao zera",
					  Bate(Vezes(noEspaco.Bp, em1.Bp), 1), $"{Vezes(noEspaco.Bp, em1.Bp):0.###}x");

			// A LUTA: o mesmo soco de producao no mesmo NPC, em 10x e em 1x (a bancada roda num quadro so: nada reescreve a gravidade no meio)
			a.Ficha.Planetgrav = 10;
			a.Ficha.GravMastered = 10;
			SocoMedido soco10 = SocoEm(a, n);
			Estacionar(n);
			a.Ficha.Planetgrav = 1;
			a.Ficha.GravMastered = 1;
			SocoMedido soco1 = SocoEm(a, n);
			Estacionar(n);
			AfirmarGn("**LUTAR EM 10x RENDE 10x**: o mesmo soco de producao no mesmo NPC",
					  soco10.Acertou && soco1.Acertou && Bate(Vezes(soco10.Ganho, soco1.Ganho), 10),
					  $"1x {soco1.Ganho:0.##########} | 10x {soco10.Ganho:0.##########} | {Vezes(soco10.Ganho, soco1.Ganho):0.###}x");

			GainKnobs.GravidadeNaoMultiplicaDeTeste = true;
			try
			{
				var comODefeito = UmMinuto(10, 10);
				AfirmarGn("(defeito injetado: a gravidade nao multiplica -- o jogo de antes) treinar em 10x rende o mesmo que em 1x: a queixa do dono, com o painel ainda dizendo 10x",
						  Bate(Vezes(comODefeito.Bp, em1.Bp), 1), $"{Vezes(comODefeito.Bp, em1.Bp):0.###}x");
			}
			finally { GainKnobs.GravidadeNaoMultiplicaDeTeste = false; }

			// ---------------------------------------------------------------- 7. o banco do potencial (2026-10-26)
			// O dono (o pedido 95, 2026-10-25): *"o potencial crescia quando vc treinava e ele n pingava no seu bp ele acumulava"* -- o DU. O
			// potencial guardado NAO PINGA MAIS: o mesmo minuto com dez vezes o BP de potencial rende o MESMO que sem nenhum, no treino, na
			// meditacao e no soco (de 2026-10-20 a 2026-10-25 ele pingava cru, sem o lugar; antes disso, com o lugar -- os 20 bilhoes). E
			// cada ganho GUARDA de 20 a 40% de si no banco (`Fighter.GanhoDeAtividade`), 30 a 60% na classe Prodigial (1,5x, o dono).
			GD.Print("[ganho] -- 7) O BANCO DO POTENCIAL: ele nao pinga mais, e todo ganho guarda de 20 a 40% de si (dono, 2026-10-25) --");
			const double potencialAlto = 10 * bp;
			// (2026-10-23) O PESO NO TETO E O DO GANHO, 8x: a 500x o "100" do peso de sempre e 1000x o limite do corpo, e acima de 8x o treino nao
			// rende nada (dono: *"vc nem conseguiria treinar assim"*). Aqui o "100" e 6x o limite -- o `weight` no teto (8x), e abaixo do corte.
			const double TetoDoGanho = 6;
			static bool Entre(double x, double de, double ate) => x >= de - 1e-9 && x <= ate + 1e-9;
			double PingoNoTreino(double gravidade, string peso, double limites = 0) =>
				UmMinuto(gravidade, gravidade, peso, potencial: potencialAlto, passadas: 100, pesoMaximoEmLimites: limites).Bp
				- UmMinuto(gravidade, gravidade, peso, passadas: 100, pesoMaximoEmLimites: limites).Bp;
			double PingoMeditando(double gravidade) =>
				UmMinuto(gravidade, gravidade, meditando: true, potencial: potencialAlto).Bp - UmMinuto(gravidade, gravidade, meditando: true).Bp;
			double PingoNoSoco(double gravidade)
			{
				a.Ficha.Planetgrav = gravidade;
				a.Ficha.GravMastered = gravidade;
				SocoMedido com = SocoEm(a, n, potencialAlto);
				Estacionar(n);
				SocoMedido sem = SocoEm(a, n);
				Estacionar(n);
				return com.Acertou && sem.Acertou ? com.Ganho - sem.Ganho : double.NaN;
			}
			// O CASO DO RELATO (2026-10-20), de ponta a ponta: 1B de BP com o potencial de quem comprou as tres skills com 1B (14x o BP),
			// UMA HORA de `Treinar` de producao (18.000 passadas) a 500x com o peso no teto e o folego sempre cheio (o do morto). Roda por
			// ULTIMO: o recorde do corpo pro peso (`weight_cap_hw`) nunca desce, e o de 1B ficaria nas medidas seguintes.
			double UmaHoraDoRelato() => UmMinuto(500, 500, "100", potencial: 14e9, bpDePartida: 1e9, passadas: 18_000, pesoMaximoEmLimites: TetoDoGanho).Bp;

			double treino1 = PingoNoTreino(1, "0"), treino500 = PingoNoTreino(500, "100", TetoDoGanho);
			double med1 = PingoMeditando(1), med500 = PingoMeditando(500), soco1Oculto = PingoNoSoco(1), soco500Oculto = PingoNoSoco(500);
			double semNada = UmMinuto(1, 1, passadas: 100).Bp;
			bool Zero(double x) => !double.IsNaN(x) && Math.Abs(x) <= 1e-6 * Math.Max(semNada, 1);
			AfirmarGn("**O POTENCIAL GUARDADO NAO PINGA MAIS NO BP** (o DU, o dono): no treino em 1x e a 500x com o peso no teto, meditando em 1x e a 500x, e no soco em 1x e a 500x, o mesmo minuto com dez vezes o BP de potencial rende O MESMO que sem nenhum",
					  Zero(treino1) && Zero(treino500) && Zero(med1) && Zero(med500) && Zero(soco1Oculto) && Zero(soco500Oculto),
					  $"treino {treino1:0.####}/{treino500:0.####}, meditando {med1:0.####}/{med500:0.####}, soco {soco1Oculto:0.####}/{soco500Oculto:0.####}");
			var esforco500 = UmMinuto(500, 500, "100", pesoMaximoEmLimites: TetoDoGanho);
			AfirmarGn("...e o ESFORCO, no mesmo lugar, continua pagando o que o painel diz: 500x de gravidade com o peso no teto (8x) = 4000x o de 1x",
					  Bate(Vezes(esforco500.Bp, em1.Bp), 4000) && Bate(Vezes(esforco500.Painel, em1.Painel), 4000),
					  $"{Vezes(esforco500.Bp, em1.Bp):0.#}x; painel {esforco500.Painel:0.##}x");

			var guardaTreino = UmMinuto(1, 1);
			var guardaMedita = UmMinuto(1, 1, meditando: true);
			SocoMedido socoGuarda = SocoEm(a, n);
			double bancoDoSoco = a.Ficha.hiddenpotential;
			Estacionar(n);
			AfirmarGn("**...E TODO GANHO GUARDA DE 20 A 40% DE SI NO BANCO** (`unusedPotential += gain * rand(0.2, 0.4)` do DU): um minuto de treino, um de meditacao e um soco",
					  guardaTreino.Bp > 0 && Entre(guardaTreino.Banco / guardaTreino.Bp, 0.2, 0.4)
					  && guardaMedita.Bp > 0 && Entre(guardaMedita.Banco / guardaMedita.Bp, 0.2, 0.4)
					  && socoGuarda.Acertou && socoGuarda.Ganho > 0 && Entre(bancoDoSoco / socoGuarda.Ganho, 0.2, 0.4),
					  $"treino {guardaTreino.Banco / Math.Max(guardaTreino.Bp, 1e-12):P1}, meditando {guardaMedita.Banco / Math.Max(guardaMedita.Bp, 1e-12):P1}, "
					  + $"soco {bancoDoSoco / Math.Max(socoGuarda.Ganho, 1e-12):P1}");
			string classeDeAntes = a.Ficha.Class;
			a.Ficha.Class = "Prodigial";
			(double Bp, double Painel, double Banco) doProdigial;
			try { doProdigial = UmMinuto(1, 1); }
			finally { a.Ficha.Class = classeDeAntes; }
			// A MEDIA, E NAO A FAIXA DE CADA GANHO: o minuto sao 300 ganhos, e a fracao guardada de cada um (30 a 60%) se MEDIA em ~45%. A
			// regua era "entre 30% e 60%", que a classe comum tambem passa -- a media dela e ~30%, na beirada (a contraprova de 2026-10-26
			// passou batido assim). 40 a 50% e o meio da do Prodigial, com folga de dez desvios da media de 300 sorteios.
			AfirmarGn("**...E O PRODIGIAL ENCHE 1,5x** (o dono, 2026-10-26: *\"faz ele encher o banco 1,5x\"*): cada ganho guarda de 30 a 60% de si, "
					  + "e o minuto inteiro (300 ganhos) guarda em media 45% -- 1,5x os 30% da classe comum",
					  doProdigial.Bp > 0 && Entre(doProdigial.Banco / doProdigial.Bp, 0.4, 0.5), $"{doProdigial.Banco / Math.Max(doProdigial.Bp, 1e-12):P1}");
			Fighter.PotencialSemBancoDeTeste = true;
			try
			{
				var semBanco = UmMinuto(1, 1);
				AfirmarGn("(defeito injetado: o ganho nao guarda) o minuto de treino nao poe nada no banco -- a regua de cima REPROVA",
						  semBanco.Bp > 0 && semBanco.Banco == 0, $"banco {semBanco.Banco:0.####}");
			}
			finally { Fighter.PotencialSemBancoDeTeste = false; }

			double hora = UmaHoraDoRelato();
			AfirmarGn("**O CASO DO RELATO** (2026-10-20): 1B com 14B de potencial guardado, uma hora de treino a 500x com o peso no teto e o folego do morto -- sobe so o esforco (menos de 500M), e nao os 13B de 2026-10-20",
					  hora > 0 && hora < 500e6, $"+{hora:N0} de BP na hora");
			AplicarGravidade(a);   // a gravidade do chao de volta

			// ---------------------------------------------------------------- 7c. o estoque antigo sai do save (2026-10-26)
			// O dono: *"n esqueça claro de remover todo codigo antigo do hidden potential antigo, so mantendo o atual"* -- e, perguntado,
			// zerar o que os saves de antes traziam. Pelo funil do login (`AccountStore.ParaJogador`), com uma ficha propria (o save
			// aponta pra ficha do corpo, e esta nao pode mexer nela): a de antes do banco (o campo novo falso) chega com 5B de estoque
			// velho; a ja migrada, com 300 no banco.
			{
				ServerPlayer Logar(double estoque, bool jaMigrado)
				{
					CharacterSave s = AccountStore.DeJogador(a, NowMs());
					s.Ficha = new Fighter { Name = a.Name, Race = a.Ficha.Race, BP = a.Ficha.BP, hiddenpotential = estoque, potencialAntigoZerado = jaMigrado };
					var volta = new ServerPlayer { Id = a.Id, Peer = null, Conta = a.Conta, Slot = a.Slot };
					AccountStore.ParaJogador(s, volta);
					return volta;
				}
				ServerPlayer deAntes = Logar(5e9, false), migrado = Logar(300, true);
				AfirmarGn("**O POTENCIAL OCULTO ANTIGO SAI DO SAVE, UMA VEZ** (o dono, 2026-10-26: zerar o estoque do sistema velho): o save de antes do banco chega com 5B e entra com o banco zerado (e marcado); o ja migrado entra com os 300 do banco",
						  deAntes.Ficha.hiddenpotential == 0 && deAntes.Ficha.potencialAntigoZerado && migrado.Ficha.hiddenpotential == 300,
						  $"de antes {deAntes.Ficha.hiddenpotential:0} (marcado {deAntes.Ficha.potencialAntigoZerado}); migrado {migrado.Ficha.hiddenpotential:0}");
			}

			// ---------------------------------------------------------------- 7b. sem o freio de 1 bi
			// O dono (2026-10-24): *"tira o debuff de bp gains acima de 1b"*. O `CapCheck` multiplicava todo ganho acima de 1B por
			// `1B / BP` (`Fighter.BpGainSoftcap`, na forca `BpGainSoftStrength` = 1): a 10B, um decimo. O treino deste port e LINEAR (a base
			// nao le o BP), entao o mesmo minuto a 2,5B e a 10B tem de render o MESMO -- e com o freio de volta, o de 10B rende um quarto do
			// de 2,5B. Um minuto de aquecimento no BP mais alto antes: os marcos de ascensao que o tique de producao cruza (`Treinar`) ficam
			// todos vistos e nao mexem nas duas medidas.
			GD.Print("[ganho] -- 7b) SEM O FREIO DE 1 BI: o mesmo treino rende o mesmo em qualquer BP (dono, 2026-10-24) --");
			UmMinuto(1, 1, bpDePartida: 1e10);
			var minutoEm2B5 = UmMinuto(1, 1, bpDePartida: 2.5e9);
			var minutoEm10B = UmMinuto(1, 1, bpDePartida: 1e10);
			AfirmarGn("**SEM O FREIO DE 1 BI** (dono, 2026-10-24: *\"tira o debuff de bp gains acima de 1b\"*): o mesmo minuto de treino rende o MESMO a 2,5B e a 10B -- e nao um quarto",
					  minutoEm2B5.Bp > 0 && Bate(minutoEm10B.Bp, minutoEm2B5.Bp),
					  $"2,5B: +{minutoEm2B5.Bp:N0} | 10B: +{minutoEm10B.Bp:N0} ({Vezes(minutoEm10B.Bp, minutoEm2B5.Bp):0.###}x)");
			double forcaDoFreio = Fighter.BpGainSoftStrength;
			Fighter.BpGainSoftStrength = 1;
			try
			{
				var freado10B = UmMinuto(1, 1, bpDePartida: 1e10);
				var freado2B5 = UmMinuto(1, 1, bpDePartida: 2.5e9);
				AfirmarGn("(defeito injetado: o freio de antes, `Fighter.BpGainSoftStrength` = 1) a 10B o minuto rende um quarto do de 2,5B -- a regua de cima REPROVA",
						  freado2B5.Bp > 0 && Bate(freado10B.Bp, freado2B5.Bp * 0.25),
						  $"2,5B: +{freado2B5.Bp:N0} | 10B: +{freado10B.Bp:N0} ({Vezes(freado10B.Bp, freado2B5.Bp):0.###}x)");
			}
			finally { Fighter.BpGainSoftStrength = forcaDoFreio; }
			AplicarGravidade(a);

			// ---------------------------------------------------------------- 8. a media da sessao de treino
			// O dono (2026-10-20): *"adicione com isso no training session ao clicar em view session a media de ganhos de bp por segundo"*.
			// Pelo verbo de producao (`VerboSessao`, o do "sessao_ver"): a sessao comecou ha 100 s e o BP subiu 500 -- com o scouter, 5 por
			// segundo; sem ele, 0,05% do BP do comeco em 100 s, 0,0005% por segundo.
			GD.Print("[ganho] -- 8) O VIEW SESSION DIZ HA QUANTO TEMPO A SESSAO CORRE E A MEDIA DE GANHO POR SEGUNDO (dono, 2026-10-20) --");
			Protocol.Poder poderesAntes = a.Poderes;
			try
			{
				string Relatorio() => string.Join(" | ", Ouvir(() => VerboSessao(a, nova: false)));
				PorBp(a, bp);
				VerboSessao(a, nova: true);
				a.SessaoDesdeMs = NowMs() - 100_000;
				a.Ficha.BP = bp + 500;
				a.Poderes |= Protocol.Poder.Scouter;
				string comScouter = Relatorio();
				string cincoPorSegundo = $"{NumeroDaMedia(5)} por segundo";
				AfirmarGn("**O VIEW SESSION DIZ A MEDIA POR SEGUNDO** (*\"a media de ganhos de bp por segundo\"*): +500 de BP em 1 min 40 s, com o scouter, e 5 por segundo",
						  comScouter.Contains(cincoPorSegundo) && comScouter.Contains("1 min 40 s") && comScouter.Contains("500"), comScouter);
				a.Poderes &= ~Protocol.Poder.Scouter;
				string semScouter = Relatorio();
				AfirmarGn("...e sem o scouter, em porcentagem do BP do comeco (o sigilo do resto da frase): 0,05% em 100 s, 0,0005% por segundo",
						  semScouter.Contains($"{NumeroDaMedia(0.0005)}% por segundo") && !semScouter.Contains(cincoPorSegundo), semScouter);

				a.Poderes |= Protocol.Poder.Scouter;
				SessaoSemMediaDeTeste = true;
				try
				{
					string deAntes = Relatorio();
					AfirmarGn("(defeito injetado: o View Session de antes) a frase diz o ganho e nao a media. A prova da media sabe reprovar",
							  deAntes.Contains("500") && !deAntes.Contains("por segundo"), deAntes);
				}
				finally { SessaoSemMediaDeTeste = false; }
			}
			finally
			{
				a.Poderes = poderesAntes;
				a.EmSessao = false;
			}
		}
		catch (Exception e)
		{
			AfirmarGn("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			GainKnobs.SocoNoArRendeDeTeste = GainKnobs.KiRendeNoDisparoDeTeste = GainKnobs.PvpSemTrocaDeTeste = false;
			GainKnobs.GravidadeNaoMultiplicaDeTeste = false;
			Fighter.PotencialSemBancoDeTeste = false;
			SessaoSemMediaDeTeste = false;
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
