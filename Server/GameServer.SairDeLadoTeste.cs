using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// 14) SAIR DE LADO DA DISPUTA DE GUARDA (`--embatekiteste`) -- o pedido do dono de 2026-10-12: *"se o defensor for
/// mais forte ele pode tentar se mover pros lados e sair da frente do ataque de ki enquanto defende com a guarda e ele
/// n é arrastado pelo ki"*.
///
/// ============================ O QUE SE MEDE, E POR ONDE ============================
/// A disputa de guarda da familia 6 (o raio nasce pelo `Canalizar`, encontra a guarda pelo `Acertar`), e dentro dela
/// o PACOTE DE ANDAR de quem segura, pela porta do jogador: o `AplicarInput`, o mesmo metodo que o `Input` do fio
/// chama. A bancada nao fecha disputa nenhuma na mao -- se a porta da saida quebrar, a disputa fica de pe e as
/// reguas dizem isso. O que se le e estado de producao (a disputa, o dono e o canal do raio, a guarda, a posicao e a
/// vida de quem saiu) e o FIO (o fim de embate e o eixo, pelo `EscutaDeEmbates`; os golpes, pelo `EscutaDeGolpes`).
///
/// AS REGUAS:
///   * a regra pura (`KiContraCorpo.PassoAtravessado`, o prazo e a largura), contra as reguas do proprio raio;
///   * O MAIS FORTE SAI DE LADO ILESO: o passo atravessado fecha a disputa sem vencedor, a guarda continua erguida,
///     o raio segue viagem sem acerta-lo nem ser cortado por ele, e as duas telas ouvem o fim;
///   * O PASSO AO LONGO DO RAIO NAO SOLTA (o controle do rumo: a mesma disputa, o mesmo corpo);
///   * O MAIS FRACO NEM ENTRA NA DISPUTA (o controle da forca: ele e levado pelo raio, e levado nao escolhe o passo);
///   * quem para no meio do raio volta a ser alvo quando o prazo vence; o raio largo da mais prazo; a guarda que
///     vinha ganhando (a cabeca empurrada de volta) tambem sai ilesa; e quem nao andaria nao sai.
///
/// OS CONTRA-EXEMPLOS, um por metade da regra, cada um lido pela linha que o conserta:
///   * `KiContraCorpo.SemSaidaDeLadoDeTeste` -- o jogo de antes: a disputa planta, e o passo de lado nao a fecha;
///   * `KiContraCorpo.SaidaDeLadoSemPrazoDeTeste` -- a disputa fecha e o raio acerta quem saiu no mesmo tique;
///   * `KiContraCorpo.SaidaDeLadoPelaReguaDeTeste` -- o prazo trocado pela regua do raspao: a cabeca que a guarda
///     tinha empurrado de volta chega em quem ja foi esquecido.
///
/// A RODADA DE ANTES: `--embatekiteste --semsaidadelado` roda esta familia INTEIRA com a porta da saida fechada (o
/// primeiro botao ligado do comeco ao fim). E a rodada vermelha de proposito: as reguas da saida tem de reprovar.
/// ================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>O lado do campo da familia 14, em tiles, e a linha dele em que os dois ficam: sobra chao dos dois lados do raio.</summary>
	private const int LadoDoCampoDaSaida = 16, LinhaDaSaida = 8;

	/// <summary>Uma disputa de guarda de pe no campo da familia 14 (ou o que houve no lugar dela), pronta pra alguem sair.</summary>
	private sealed class CenaDeSaida
	{
		public required ServerPlayer Atirador, Guarda;
		public DisputaDeKi? Disputa;
		public Projetil? Raio;

		/// <summary>O rumo do raio (do atirador pra guarda) e onde a guarda estava quando o raio chegou nela.</summary>
		public Vec2 Eixo, Berco;
	}

	/// <summary>O que aconteceu com quem deu o passo e seguiu (ou parou), lido do estado de producao e do fio.</summary>
	private readonly record struct SaidaMedida(
		bool Fechou, bool PassoDado, bool RaioDeQuemAtirou, bool CanalDePe, bool GuardaErguida, double Prazo,
		double VidaPerdida, int GolpesNele, float AvancoDoRaio, double SaiuDeCimaEm, bool HouveCorte, float AndouDeLado);

	private void ASaidaDeLado()
	{
		GD.Print("[embateki] -- 14) SAIR DE LADO DA DISPUTA DE GUARDA: o passo atravessado ao raio fecha a disputa sem vencedor, com a guarda erguida");

		// RODA DEPOIS DA 13 E, COMO ELA, REUSA O COMECO DO MAPA: as outras familias ja tiraram os corpos delas, e cada
		// cena daqui limpa o que pos antes da seguinte. O campo e um quadrado -- o corredor das outras e uma linha, e
		// aqui se anda ATRAVESSADO a ele.
		LimparEmbatesDaBancada();
		_pjProximoCorredor = 8;
		Vec2 campo = QuadradoLivre(LadoDoCampoDaSaida);

		bool rodadaDeAntes = Array.IndexOf(OS.GetCmdlineArgs(), "--semsaidadelado") >= 0;
		if (rodadaDeAntes)
			GD.Print("[embateki]      (RODADA DE ANTES, `--semsaidadelado`: a porta da saida fica FECHADA a familia inteira -- as reguas da saida tem de reprovar)");
		KiContraCorpo.SemSaidaDeLadoDeTeste = rodadaDeAntes;

		try
		{
			ARegraDoPassoAtravessado();
			OMaisForteSaiDeLadoIleso(campo);
			OJogoDeAntesNaoTinhaSaida(campo);
			SemPrazoASaidaViraApanhar(campo);
			OPassoAoLongoDoRaioNaoSolta(campo);
			OMaisFracoNemEntraNaDisputa(campo);
			QuemParaNoMeioVoltaASerAlvo(campo);
			ORaioLargoDaMaisPrazo(campo);
			AGuardaQueVinhaGanhandoTambemSai(campo);
			QuemNaoAndariaNaoSai(campo);
		}
		finally
		{
			KiContraCorpo.SemSaidaDeLadoDeTeste = false;
			KiContraCorpo.SaidaDeLadoSemPrazoDeTeste = false;
			KiContraCorpo.SaidaDeLadoPelaReguaDeTeste = false;
			EscutaDeEmbates = null;
			EscutaDeGolpes = null;
			LimparEmbatesDaBancada();
		}
	}

	// =====================================================================
	// (a) A REGRA PURA
	// =====================================================================
	private void ARegraDoPassoAtravessado()
	{
		var leste = new Vec2(1, 0);
		bool atravessadas = KiContraCorpo.PassoAtravessado(new Vec2(0, -3), leste) && KiContraCorpo.PassoAtravessado(new Vec2(0, 3), leste);
		bool diagonais = KiContraCorpo.PassoAtravessado(new Vec2(2, -2), leste) && KiContraCorpo.PassoAtravessado(new Vec2(2, 2), leste)
						 && KiContraCorpo.PassoAtravessado(new Vec2(-2, -2), leste) && KiContraCorpo.PassoAtravessado(new Vec2(-2, 2), leste);
		bool aoLongo = KiContraCorpo.PassoAtravessado(new Vec2(3, 0), leste) || KiContraCorpo.PassoAtravessado(new Vec2(-3, 0), leste);
		AfirmarEk("A REGRA: contra um raio que vem reto, as duas teclas atravessadas e as quatro diagonais sao passo de saida; as duas ao longo dele, nao -- e passo nenhum nao e passo",
				  atravessadas && diagonais && !aoLongo && !KiContraCorpo.PassoAtravessado(Vec2.Zero, leste),
				  $"atravessadas {atravessadas}, diagonais {diagonais}, ao longo {aoLongo}");

		// A BEIRA: o seno do angulo com o eixo. Um grau pra ca nao sai, um grau pra la sai -- e o sentido do eixo nao importa.
		double graus = Math.Asin(KiContraCorpo.LadoDoPassoQueSai) * 180 / Math.PI;
		Vec2 A(double g) => new((float)Math.Cos(g * Math.PI / 180), (float)Math.Sin(g * Math.PI / 180));
		AfirmarEk($"...a beira e o angulo com o raio: a {graus - 1:0} graus do eixo o passo nao sai, a {graus + 1:0} sai (o seno de {graus:0} e o `LadoDoPassoQueSai`), pros dois sentidos do raio",
				  !KiContraCorpo.PassoAtravessado(A(graus - 1), leste) && KiContraCorpo.PassoAtravessado(A(graus + 1), leste)
				  && !KiContraCorpo.PassoAtravessado(A(graus - 1), leste * -1f) && KiContraCorpo.PassoAtravessado(A(graus + 1), leste * -1f));

		// O RAIO NAO VEM SEMPRE RETO (o eixo da disputa liga os dois corpos): em qualquer angulo tem de haver tecla.
		int piorCaso = 4;
		Vec2[] teclas = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
		for (int g = 0; g < 360; g++)
			piorCaso = Math.Min(piorCaso, teclas.Count(t => KiContraCorpo.PassoAtravessado(t, A(g))));
		AfirmarEk("...e com o raio vindo em QUALQUER angulo ha pelo menos duas das quatro teclas de andar que saem (ninguem fica sem saida por causa do angulo)",
				  piorCaso >= 2, $"no pior angulo, {piorCaso} tecla(s)");

		// O PRAZO: o tempo do proprio passo pra cruzar o raio, com a folga, entre o piso e o teto.
		double fino = KiContraCorpo.SegundosPraSairDeLado(24, 96), largo = KiContraCorpo.SegundosPraSairDeLado(136, 96);
		double ligeiro = KiContraCorpo.SegundosPraSairDeLado(136, 480);
		AfirmarEk($"O PRAZO e o tempo do passo pra cruzar o raio, com {(KiContraCorpo.FolgaDaSaidaDeLado - 1) * 100:0}% de folga: cresce com a largura, encolhe com o passo, "
				  + $"e fica entre {KiContraCorpo.PisoDaSaidaDeLado} e {KiContraCorpo.TetoDaSaidaDeLado} s",
				  Math.Abs(fino - 24.0 / 96 * KiContraCorpo.FolgaDaSaidaDeLado) < 1e-9 && largo > fino && ligeiro < largo
				  && Math.Abs(KiContraCorpo.SegundosPraSairDeLado(1, 1000) - KiContraCorpo.PisoDaSaidaDeLado) < 1e-9
				  && Math.Abs(KiContraCorpo.SegundosPraSairDeLado(1000, 1) - KiContraCorpo.TetoDaSaidaDeLado) < 1e-9
				  && Math.Abs(KiContraCorpo.SegundosPraSairDeLado(100, 0) - KiContraCorpo.TetoDaSaidaDeLado) < 1e-9,
				  $"fino {fino:0.###} s, largo {largo:0.###} s, largo e ligeiro {ligeiro:0.###} s");
	}

	// =====================================================================
	// (b) O MAIS FORTE SAI DE LADO ILESO
	// =====================================================================
	private void OMaisForteSaiDeLadoIleso(Vec2 campo)
	{
		var norte = new Vec2(0, -1);
		List<(int Para, byte[] Fio)> fio = [];
		EscutaDeEmbates = fio;
		List<string> ditos = [];
		EscutaDeAvisos = ditos;
		try
		{
			CenaDeSaida c = AbrirACenaDeSaida(campo, bpDoAtirador: 200_000, bpDaGuarda: 300_000);
			AfirmarEk("PREPARO: quem e MAIS FORTE que o raio (1,5x) e esta de guarda abre a disputa de guarda, do lado das maos, e fica plantado",
					  c.Disputa is { Tipo: Protocol.TipoDeEmbate.FeixeContraGuarda } && c.Disputa.B.Quem == c.Guarda && c.Disputa.B.DeMaos
					  && c.Raio != null && KiContraCorpo.Posto(KiContraCorpo.Razao(c.Guarda.Ficha.expressedBP, c.Raio.Bp)) == PostoContraKi.MaisForte
					  && !PodeMexerOCorpo(c.Guarda),
					  c.Raio == null ? "o raio nao nasceu" : $"razao {KiContraCorpo.Razao(c.Guarda.Ficha.expressedBP, c.Raio.Bp):0.##}, disputa {(c.Disputa == null ? "nenhuma" : c.Disputa.Tipo.ToString())}");
			if (c.Disputa == null || c.Raio == null) return;

			// AS REGUAS DO RAIO, contra a largura que a regra usa: meio pixel antes dela ainda se esta em cima de alguma
			// parte dele (a cabeca ou o tronco); um pixel alem, de nenhuma.
			float largura = KiContraCorpo.LarguraPraSairDeCima(c.Raio);
			Vec2 deLado = c.Eixo.Girado90(), meioDoTronco = (c.Raio.Cauda + c.Raio.Pos) * 0.5f;
			bool EmCima(float px) => Feixe.EmCimaDoTiro(c.Raio, c.Raio.Pos + c.Eixo * Feixe.ContatoNoCorpo(c.Raio) * 0.5f + deLado * px, 0)
									 || Feixe.EmCimaDoTiro(c.Raio, meioDoTronco + deLado * px, 0);
			AfirmarEk($"a LARGURA que a regra usa ({largura:0.#} px) e a das reguas do proprio raio: meio pixel antes dela ainda se esta em cima dele, um pixel alem nao",
					  EmCima(largura - 0.5f) && !EmCima(largura + 1f) && EmCima(-(largura - 0.5f)) && !EmCima(-(largura + 1f)),
					  $"em cima a {largura - 0.5f:0.#}: {EmCima(largura - 0.5f)}; a {largura + 1f:0.#}: {EmCima(largura + 1f)}");

			// O EIXO, NO FIO: so as maos o recebem, e ele e o da disputa.
			var eixos = fio.Select(f => (f.Para, Lido: LerEmbate(f.Fio))).Where(f => f.Lido.Momento == Protocol.ClashSub.SaidaDeLado).ToList();
			AfirmarEk("no COMECO as maos (e so elas) recebem o eixo do raio: unitario, o da disputa -- a licenca de dar o passo, pela mesma pergunta do servidor",
					  eixos.Count == 1 && eixos[0].Para == c.Guarda.Id && (eixos[0].Lido.Eixo - c.Eixo).Length < 1e-4f
					  && Math.Abs(c.Eixo.Length - 1f) < 1e-4f,
					  $"{eixos.Count} pacote(s) de eixo{(eixos.Count > 0 ? $", pra {eixos[0].Para} (as maos sao {c.Guarda.Id}), {eixos[0].Lido.Eixo} x {c.Eixo}" : "")}");

			// A DISPUTA CORRE UM POUCO SOZINHA (ninguem aperta nada), e ai as maos dao o passo.
			for (int i = 0; i < 10; i++) UmTiqueDoEncontro();
			AfirmarEk("PREPARO: a disputa continua de pe ate o passo", _emEmbateDeKi.ContainsKey(c.Guarda.Id));
			fio.Clear();

			SaidaMedida m = SairAndando(c, norte, segundos: 2.5);
			var fins = fio.Select(f => (f.Para, Lido: LerEmbate(f.Fio))).Where(f => f.Lido.Momento == Protocol.ClashSub.Acabou).ToList();

			AfirmarEk("o passo ATRAVESSADO ao raio FECHA a disputa na hora, pros dois lados",
					  m.Fechou && !_emEmbateDeKi.ContainsKey(c.Atirador.Id));
			AfirmarEk("...SEM VENCEDOR: o raio continua vivo e de quem atirou, solto da disputa, e o canal dele continua de pe",
					  m.RaioDeQuemAtirou && m.CanalDePe, $"raio de quem atirou e solto {m.RaioDeQuemAtirou}, canal de pe {m.CanalDePe}");
			AfirmarEk("...com a GUARDA ERGUIDA: quem saiu nao a baixou", m.GuardaErguida);
			AfirmarEk("...e o passo que fechou a disputa foi DADO: o corpo andou", m.PassoDado);
			AfirmarEk("...as DUAS telas ouvem o fim: sem vencedor, e com quem saiu no lugar do perdedor (a terceira leitura do `Acabou`)",
					  fins.Count == 2 && fins.Exists(f => f.Para == c.Guarda.Id) && fins.Exists(f => f.Para == c.Atirador.Id)
					  && fins.TrueForAll(f => f.Lido.Venc == 0 && f.Lido.Perd == c.Guarda.Id),
					  string.Join(" | ", fins.Select(f => $"pra {f.Para}: venc {f.Lido.Venc}, perd {f.Lido.Perd}")) + $" (saiu {c.Guarda.Id})");
			AfirmarEk("...e os dois leem no chat o que houve", Disse(ditos, "sai da frente do ataque") && Disse(ditos, "saiu da frente"), Ultimos(ditos));

			AfirmarEk($"quem saiu fica FORA DA LINHA do raio por um PRAZO: o tempo do passo dele pra cruzar a largura do raio, com a folga",
					  m.Prazo > 0 && Math.Abs(m.Prazo - KiContraCorpo.SegundosPraSairDeLado(largura, MoveRules.SpeedPx(c.Guarda.SpeedStat))) < 1e-6,
					  $"prazo {m.Prazo:0.###} s; largura {largura:0.#} px, passo {MoveRules.SpeedPx(c.Guarda.SpeedStat):0.#} px/s");
			AfirmarEk("ILESO: andando, ele sai de cima do raio antes de o prazo vencer -- a vida nao mexe e nenhum golpe o acerta",
					  m.VidaPerdida == 0 && m.GolpesNele == 0 && m.SaiuDeCimaEm > 0 && m.SaiuDeCimaEm <= m.Prazo,
					  $"vida perdida {m.VidaPerdida:0.##}, {m.GolpesNele} golpe(s) nele, saiu de cima em {m.SaiuDeCimaEm:0.###} s (prazo {m.Prazo:0.###} s)");
			AfirmarEk("...e o raio SEGUE VIAGEM: a cabeca passa de onde ele estava, sem ser cortada pelo corpo dele",
					  m.AvancoDoRaio > ZoneCollision.TileSize && !m.HouveCorte,
					  $"a cabeca foi ate {m.AvancoDoRaio:0} px alem de onde ele estava; houve corte: {m.HouveCorte}");
		}
		finally
		{
			EscutaDeEmbates = null;
			EscutaDeAvisos = null;
			LimparEmbatesDaBancada();
		}
	}

	// =====================================================================
	// (c) e (d) OS DOIS CONTRA-EXEMPLOS DA SAIDA
	// =====================================================================
	private void OJogoDeAntesNaoTinhaSaida(Vec2 campo)
	{
		bool ligado = KiContraCorpo.SemSaidaDeLadoDeTeste;
		KiContraCorpo.SemSaidaDeLadoDeTeste = true;
		try
		{
			CenaDeSaida c = AbrirACenaDeSaida(campo, 200_000, 300_000);
			if (c.Disputa == null || c.Raio == null) { AfirmarEk("(defeito injetado: o jogo de antes) a disputa de guarda abriu", false); return; }
			for (int i = 0; i < 10; i++) UmTiqueDoEncontro();

			SaidaMedida m = SairAndando(c, new Vec2(0, -1), segundos: 0.5);
			AfirmarEk("(defeito injetado: o jogo de antes, a disputa PLANTA quem segura) o mesmo passo de lado NAO fecha a disputa e o corpo nao sai do lugar "
					  + "-- a unica saida dele era baixar a guarda",
					  !m.Fechou && !m.PassoDado && m.AndouDeLado < 0.5f && _emEmbateDeKi.ContainsKey(c.Guarda.Id),
					  $"fechou {m.Fechou}, andou {m.AndouDeLado:0.#} px de lado");
		}
		finally
		{
			KiContraCorpo.SemSaidaDeLadoDeTeste = ligado;
			LimparEmbatesDaBancada();
		}
	}

	private void SemPrazoASaidaViraApanhar(Vec2 campo)
	{
		KiContraCorpo.SaidaDeLadoSemPrazoDeTeste = true;
		try
		{
			CenaDeSaida c = AbrirACenaDeSaida(campo, 200_000, 300_000);
			if (c.Disputa == null || c.Raio == null) { AfirmarEk("(defeito injetado: a saida sem prazo) a disputa de guarda abriu", false); return; }
			for (int i = 0; i < 10; i++) UmTiqueDoEncontro();

			SaidaMedida m = SairAndando(c, new Vec2(0, -1), segundos: 2.5);
			AfirmarEk("(defeito injetado: quem sai nao fica fora da linha do raio) a disputa fecha, e o raio solto ACERTA quem saiu ainda de saida "
					  + "-- sair vira apanhar",
					  m.Fechou && m.Prazo == 0 && (m.GolpesNele > 0 || m.VidaPerdida > 0),
					  $"fechou {m.Fechou}, prazo {m.Prazo:0.###} s, {m.GolpesNele} golpe(s) nele, vida perdida {m.VidaPerdida:0.##}");
		}
		finally
		{
			KiContraCorpo.SaidaDeLadoSemPrazoDeTeste = false;
			LimparEmbatesDaBancada();
		}
	}

	// =====================================================================
	// (e) O PASSO AO LONGO DO RAIO NAO SOLTA
	// =====================================================================
	private void OPassoAoLongoDoRaioNaoSolta(Vec2 campo)
	{
		CenaDeSaida c = AbrirACenaDeSaida(campo, 200_000, 300_000);
		if (c.Disputa == null || c.Raio == null) { AfirmarEk("(o passo ao longo do raio) a disputa de guarda abriu", false); LimparEmbatesDaBancada(); return; }
		for (int i = 0; i < 10; i++) UmTiqueDoEncontro();

		// PRA CIMA DE QUEM ATIRA e PRA TRAS, tres pacotes de cada, com o mundo andando entre eles.
		Vec2 antes = c.Guarda.Pos;
		foreach (Vec2 rumo in new[] { c.Eixo * -1f, c.Eixo })
			for (int i = 0; i < 3; i++)
			{
				PacoteDePasso(c.Guarda, rumo);
				UmTiqueDoEncontro();
			}
		AfirmarEk("o passo AO LONGO do raio (pra cima de quem atira, ou pra tras) NAO solta: a disputa continua de pe e o corpo nao sai do lugar",
				  _emEmbateDeKi.ContainsKey(c.Guarda.Id) && c.Raio.EmEmbate && (c.Guarda.Pos - antes).Length < 0.01f && c.Guarda.Combate.Bloqueando,
				  $"em disputa {_emEmbateDeKi.ContainsKey(c.Guarda.Id)}, andou {(c.Guarda.Pos - antes).Length:0.##} px");

		// O CONTROLE: a MESMA disputa, o MESMO corpo, o passo atravessado -- so o rumo mudou.
		PacoteDePasso(c.Guarda, new Vec2(0, 1));
		AfirmarEk("...e na MESMA disputa o passo atravessado (pro outro lado, desta vez) fecha na hora: quem decide e o rumo do passo",
				  !_emEmbateDeKi.ContainsKey(c.Guarda.Id) && DeLadoDoEixo(c.Guarda.Pos - antes, c.Eixo) > 0.5f);
		LimparEmbatesDaBancada();

		// A DIAGONAL CONTA COMO DE LADO (0,71 do passo e atravessado, acima do 0,5 da regra).
		CenaDeSaida diag = AbrirACenaDeSaida(campo, 200_000, 300_000);
		if (diag.Disputa != null)
		{
			Vec2 la = diag.Guarda.Pos;
			PacoteDePasso(diag.Guarda, new Vec2(1, -1));
			AfirmarEk("...e a DIAGONAL tambem fecha: o corpo anda nos dois eixos",
					  !_emEmbateDeKi.ContainsKey(diag.Guarda.Id) && DeLadoDoEixo(diag.Guarda.Pos - la, diag.Eixo) > 0.5f
					  && Math.Abs(NoEixo(diag.Guarda.Pos - la, diag.Eixo)) > 0.5f,
					  $"andou ({diag.Guarda.Pos.X - la.X:0.#}, {diag.Guarda.Pos.Y - la.Y:0.#})");
		}
		else AfirmarEk("(a diagonal) a disputa de guarda abriu", false);
		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// (f) O MAIS FRACO NEM ENTRA NA DISPUTA
	// =====================================================================
	private void OMaisFracoNemEntraNaDisputa(Vec2 campo)
	{
		// (mais perto de quem atira e mais a oeste no campo: quem e levado precisa de chao pra tras)
		CenaDeSaida c = AbrirACenaDeSaida(campo, bpDoAtirador: 200_000, bpDaGuarda: 100_000, tilesEntre: 4);
		AfirmarEk("O MAIS FRACO (metade do poder do raio), de guarda, NEM ENTRA na disputa: o raio o LEVA, com a guarda erguida",
				  c.Disputa == null && c.Raio is { Vivo: true } && c.Raio.Arrastando == c.Guarda.Id && c.Guarda.Combate.Bloqueando
				  && KiContraCorpo.Arrasta(KiContraCorpo.Razao(c.Guarda.Ficha.expressedBP, c.Raio.Bp)),
				  $"disputa {(c.Disputa == null ? "nenhuma" : "ABRIU")}, levando {c.Raio?.Arrastando ?? 0} (ele e {c.Guarda.Id})");
		if (c.Raio == null) { LimparEmbatesDaBancada(); return; }

		Vec2 antes = c.Guarda.Pos;
		for (int i = 0; i < 12; i++)
		{
			PacoteDePasso(c.Guarda, new Vec2(0, -1));
			UmTiqueDoEncontro();
		}
		AfirmarEk("...e o passo de lado DELE e recusado: quem e levado nao escolhe pra onde vai (ele segue raio afora, sem sair da linha)",
				  DeLadoDoEixo(c.Guarda.Pos - antes, c.Eixo) < 0.5f && NoEixo(c.Guarda.Pos - antes, c.Eixo) > 1f && !PodeMexerOCorpo(c.Guarda),
				  $"de lado {DeLadoDoEixo(c.Guarda.Pos - antes, c.Eixo):0.#} px, raio afora {NoEixo(c.Guarda.Pos - antes, c.Eixo):0.#} px");
		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// (g) QUEM PARA NO MEIO VOLTA A SER ALVO
	// =====================================================================
	private void QuemParaNoMeioVoltaASerAlvo(Vec2 campo)
	{
		// (o raio PESADO, na metade da velocidade: ele ainda esta atravessando o campo quando o prazo vence -- com o
		//  comum, a cabeca ja teria chegado ao fim do chao conhecido, e o que se mediria e o palco)
		CenaDeSaida c = AbrirACenaDeSaida(campo, 200_000, 300_000, velocidade: 0.5);
		if (c.Disputa == null || c.Raio == null) { AfirmarEk("(quem para no meio) a disputa de guarda abriu", false); LimparEmbatesDaBancada(); return; }
		for (int i = 0; i < 10; i++) UmTiqueDoEncontro();

		// UM PASSO SO, e ele fica: fora da disputa, de guarda erguida, a poucos pixels do eixo -- dentro do raio.
		double vida = c.Guarda.Combate.Corpo.Vida();
		PacoteDePasso(c.Guarda, new Vec2(0, -1));
		double prazo = c.Raio.SaiuDeLado == c.Guarda.Id ? c.Raio.FolgaDeQuemSaiuDeLado : 0;
		float largura = KiContraCorpo.LarguraPraSairDeCima(c.Raio);
		bool fechou = !_emEmbateDeKi.ContainsKey(c.Guarda.Id);

		// DENTRO DO PRAZO (ate um tique antes de ele vencer): nada o toca.
		int tiquesDoPrazo = (int)(prazo / Protocol.TickSeconds) - 1;
		for (int i = 0; i < tiquesDoPrazo; i++) UmTiqueDoEncontro();
		double vidaNoPrazo = c.Guarda.Combate.Corpo.Vida();
		bool aindaProtegido = c.Raio.SaiuDeLado == c.Guarda.Id;

		// E DEPOIS DELE (um segundo a mais): o raio volta a enxerga-lo.
		for (int i = 0; i < 30; i++) UmTiqueDoEncontro();
		double vidaDepois = c.Guarda.Combate.Corpo.Vida();

		AfirmarEk("PREPARO: ele da UM passo e para a poucos pixels do eixo -- fora da disputa, mas ainda em cima do raio",
				  fechou && prazo > 0 && DeLadoDoEixo(c.Guarda.Pos - c.Berco, c.Eixo) is > 0.5f and < 16f && DeLadoDoEixo(c.Guarda.Pos - c.Berco, c.Eixo) < largura,
				  $"fechou {fechou}, a {DeLadoDoEixo(c.Guarda.Pos - c.Berco, c.Eixo):0.#} px do eixo (o raio pega ate {largura:0.#})");
		AfirmarEk("QUEM PARA NO MEIO NAO FICA IMUNE: dentro do prazo nada o toca...",
				  aindaProtegido && vidaNoPrazo == vida, $"protegido {aindaProtegido}, vida {vida:0.##} -> {vidaNoPrazo:0.##}");
		AfirmarEk("...e vencido o prazo o raio volta a enxerga-lo: ele apanha, ali onde ficou",
				  c.Raio.SaiuDeLado == 0 && vidaDepois < vidaNoPrazo, $"vida {vidaNoPrazo:0.##} -> {vidaDepois:0.##}, prazo {prazo:0.###} s");
		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// (h) O RAIO LARGO DA MAIS PRAZO
	// =====================================================================
	private void ORaioLargoDaMaisPrazo(Vec2 campo)
	{
		CenaDeSaida fino = AbrirACenaDeSaida(campo, 200_000, 300_000);
		float larguraFina = fino.Raio != null ? KiContraCorpo.LarguraPraSairDeCima(fino.Raio) : 0;

		CenaDeSaida c = AbrirACenaDeSaida(campo, 200_000, 300_000, escala: 4);
		if (c.Disputa == null || c.Raio == null) { AfirmarEk("(o raio largo) a disputa de guarda abriu", false); LimparEmbatesDaBancada(); return; }
		float largura = KiContraCorpo.LarguraPraSairDeCima(c.Raio);
		for (int i = 0; i < 10; i++) UmTiqueDoEncontro();

		SaidaMedida m = SairAndando(c, new Vec2(0, -1), segundos: 4);
		double prazoFino = KiContraCorpo.SegundosPraSairDeLado(larguraFina, MoveRules.SpeedPx(c.Guarda.SpeedStat));
		AfirmarEk($"O RAIO LARGO (quatro vezes o desenho: {largura:0} px pra sair de cima, contra {larguraFina:0} do fino) da MAIS PRAZO -- o tempo e o do passo, e nao um numero fixo",
				  larguraFina > 0 && largura > larguraFina * 2 && m.Prazo > prazoFino * 1.5,
				  $"prazo {m.Prazo:0.###} s contra {prazoFino:0.###} s do fino");
		AfirmarEk("...e quem continua andando sai ILESO dele tambem, dentro do prazo, sem cortar o raio",
				  m.Fechou && m.VidaPerdida == 0 && m.GolpesNele == 0 && m.SaiuDeCimaEm > 0 && m.SaiuDeCimaEm <= m.Prazo && !m.HouveCorte,
				  $"vida perdida {m.VidaPerdida:0.##}, {m.GolpesNele} golpe(s), saiu de cima em {m.SaiuDeCimaEm:0.###} s (prazo {m.Prazo:0.###} s), corte {m.HouveCorte}");
		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// (i) A GUARDA QUE VINHA GANHANDO TAMBEM SAI
	// =====================================================================
	private void AGuardaQueVinhaGanhandoTambemSai(Vec2 campo)
	{
		(bool Abriu, float Recuo, SaidaMedida M) Medir()
		{
			CenaDeSaida c = AbrirACenaDeSaida(campo, 200_000, 300_000);
			if (c.Disputa == null || c.Raio == null) return (false, 0, default);

			// AS MAOS APERTAM ATE A CABECA DO RAIO RECUAR UM POUCO (meio tile): a disputa esta sendo ganha, e o raio ja
			// nao esta encostado nelas.
			float contato = Feixe.ContatoNoCorpo(c.Raio);
			float Recuo() => NoEixo(c.Berco - c.Raio.Pos, c.Eixo) - contato;
			for (int i = 0; i < 30 * 10 && _emEmbateDeKi.ContainsKey(c.Guarda.Id) && Recuo() < ZoneCollision.TileSize * 0.5f; i++)
			{
				if (c.Disputa.B.Letra != '\0') TeclaDeQualquerEmbate(c.Guarda, c.Disputa.B.Letra);
				UmTiqueDoEncontro();
			}
			float recuo = Recuo();
			if (!_emEmbateDeKi.ContainsKey(c.Guarda.Id)) return (false, recuo, default);
			return (true, recuo, SairAndando(c, new Vec2(0, -1), segundos: 2.5));
		}

		var vivo = Medir();
		AfirmarEk("PREPARO: a guarda vinha GANHANDO -- a cabeca do raio foi empurrada de volta e ja nao esta encostada nas maos (nem em cima de quem segura)",
				  vivo.Abriu && vivo.Recuo >= ZoneCollision.TileSize * 0.5f, $"recuo {vivo.Recuo:0.#} px");
		AfirmarEk("QUEM VINHA GANHANDO TAMBEM SAI ILESO: o raio volta a andar, chega onde ele estava e passa -- o prazo e do relogio, e ele ainda corre",
				  vivo.Abriu && vivo.M.Fechou && vivo.M.VidaPerdida == 0 && vivo.M.GolpesNele == 0 && vivo.M.AvancoDoRaio > ZoneCollision.TileSize && !vivo.M.HouveCorte,
				  $"fechou {vivo.M.Fechou}, vida perdida {vivo.M.VidaPerdida:0.##}, {vivo.M.GolpesNele} golpe(s), a cabeca foi ate {vivo.M.AvancoDoRaio:0} px alem dele");
		LimparEmbatesDaBancada();

		KiContraCorpo.SaidaDeLadoPelaReguaDeTeste = true;
		try
		{
			var pelaRegua = Medir();
			AfirmarEk("(defeito injetado: o prazo trocado pela regua do raspao, \"saiu de cima do tiro\") quem saiu e esquecido antes de a cabeca chegar, "
					  + "e ela o ACERTA ainda de saida",
					  pelaRegua.Abriu && pelaRegua.M.Fechou && (pelaRegua.M.GolpesNele > 0 || pelaRegua.M.VidaPerdida > 0),
					  $"fechou {pelaRegua.M.Fechou}, recuo {pelaRegua.Recuo:0.#} px, {pelaRegua.M.GolpesNele} golpe(s), vida perdida {pelaRegua.M.VidaPerdida:0.##}");
		}
		finally
		{
			KiContraCorpo.SaidaDeLadoPelaReguaDeTeste = false;
			LimparEmbatesDaBancada();
		}
	}

	// =====================================================================
	// (j) QUEM NAO ANDARIA NAO SAI
	// =====================================================================
	private void QuemNaoAndariaNaoSai(Vec2 campo)
	{
		CenaDeSaida c = AbrirACenaDeSaida(campo, 200_000, 300_000);
		if (c.Disputa == null || c.Raio == null) { AfirmarEk("(quem nao andaria) a disputa de guarda abriu", false); LimparEmbatesDaBancada(); return; }

		// FINCADO POR OUTRA COISA no meio da disputa: os coletores abertos do androide (`ki_absorb_stance`), uma das
		// recusas do `PodeMexerOCorpo` que nao sorteia nada. O raio ja esta preso na disputa, e nao e engolido por eles.
		Vec2 antes = c.Guarda.Pos;
		c.Guarda.Ficha.ki_absorb_stance = true;
		try
		{
			for (int i = 0; i < 3; i++)
			{
				PacoteDePasso(c.Guarda, new Vec2(0, -1));
				UmTiqueDoEncontro();
			}
			AfirmarEk("QUEM NAO ANDARIA NAO SAI: fincado por outra recusa do `PodeMexerOCorpo`, o passo de lado nao fecha a disputa (nem a tira do lugar) "
					  + "-- fechar por um passo que nao existe seria ganhar o prazo parado",
					  _emEmbateDeKi.GetValueOrDefault(c.Guarda.Id) == c.Disputa && _emEmbateDeKi.GetValueOrDefault(c.Atirador.Id) == c.Disputa
					  && c.Raio.EmEmbate && c.Raio.SaiuDeLado == 0 && (c.Guarda.Pos - antes).Length < 0.01f,
					  $"em disputa {_emEmbateDeKi.ContainsKey(c.Guarda.Id)}, andou {(c.Guarda.Pos - antes).Length:0.##} px");
		}
		finally { c.Guarda.Ficha.ki_absorb_stance = false; }

		PacoteDePasso(c.Guarda, new Vec2(0, -1));
		AfirmarEk("...e solto disso, o mesmo passo fecha", !_emEmbateDeKi.ContainsKey(c.Guarda.Id) && (c.Guarda.Pos - antes).Length > 0.5f);
		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// AS REGUAS E O PALCO DESTA FAMILIA
	// =====================================================================
	/// <summary>
	/// DOIS CORPOS NA LINHA DO MEIO DO CAMPO E UM RAIO QUE FERE ATRAVES DA GUARDA, ate o raio chegar em quem a segura:
	/// a disputa de guarda abre (quem nao e mais fraco que ele) ou ele e levado (quem e). Tudo por producao, como a
	/// familia 6: `Canalizar`, o tique fecha a carga e pare o raio, e o `Acertar` decide.
	///
	/// OS DOIS TEM TECLADO E NINGUEM APERTA NADA (so a deriva trabalha): o que se mede aqui e a SAIDA, e nao o cabo de
	/// guerra -- a cena que precisa das letras as aperta por conta propria.
	/// </summary>
	/// <param name="escala">A escala do desenho do raio (`ReceitaDeProjetil.EscalaVisual`): 4 e um raio quatro vezes mais largo.</param>
	/// <param name="velocidade">O `speed` do raio: 1 e o comum; 0,5 o pesado, que leva o dobro do tempo pra atravessar o campo.</param>
	private CenaDeSaida AbrirACenaDeSaida(Vec2 campo, double bpDoAtirador, double bpDaGuarda, double escala = 1, int tilesEntre = 8,
										  double velocidade = 1)
	{
		LimparEmbatesDaBancada();
		const int T = ZoneCollision.TileSize;

		var chao = new Vec2(campo.X + T, campo.Y + LinhaDaSaida * T);
		ServerPlayer atirador = Forjar("Agressor", chao, bp: bpDoAtirador);
		atirador.Facing = Facing.East;
		atirador.Ficha.Ki = atirador.Ficha.MaxKi;
		ServerPlayer guarda = Forjar("Muralha", new Vec2(chao.X + tilesEntre * T, chao.Y), bp: bpDaGuarda);
		guarda.Facing = Facing.West;
		guarda.Ficha.Ki = guarda.Ficha.MaxKi;
		SegurarAGuarda(guarda);
		_comTecladoDeTeste.Add(atirador.Id);
		_comTecladoDeTeste.Add(guarda.Id);

		ReceitaDeProjetil queFere = SemDeflexao();
		queFere.BaseDano = BaseQueFere(atirador, guarda);
		if (escala > 1) queFere.EscalaVisual = escala;
		queFere.Velocidade = velocidade;
		Canalizar(atirador, "Ki_Wave", 10 * atirador.Ficha.BaseDrain(), queFere);

		var cena = new CenaDeSaida { Atirador = atirador, Guarda = guarda, Berco = guarda.Pos, Eixo = new Vec2(1, 0) };
		for (int i = 0; i < 30 * 8 && cena.Disputa == null; i++)
		{
			UmTiqueDoEncontro();
			cena.Raio ??= _canais.GetValueOrDefault(atirador.Id)?.Raio;
			cena.Disputa = _emEmbateDeKi.GetValueOrDefault(guarda.Id);
			// (sem disputa: o raio chegou nele e o leva, ou parou nele sem ferir -- a cena e de quem chamou)
			if (cena.Raio is { } r && (r.Arrastando != 0 || r.BatendoSemFerir != 0 || !r.Vivo)) break;
		}
		if (cena.Disputa != null) cena.Eixo = cena.Disputa.Eixo;
		return cena;
	}

	/// <summary>
	/// UM PACOTE DE ANDAR, como o cliente o monta a 30 Hz: a posicao que o passo honesto deste corpo da num tique, no
	/// rumo pedido, entregue a porta de producao (`AplicarInput`). O relogio do servidor entre dois pacotes e escrito
	/// na mao, um tique atras -- a bancada roda mais depressa que o relogio de parede, e sem isto o passo nao teria
	/// orcamento (a mesma razao do `PacoteEmVoo` da `--arranqueteste`).
	/// </summary>
	private void PacoteDePasso(ServerPlayer pl, Vec2 rumo)
	{
		float passo = MoveRules.SpeedPx(pl.SpeedStat) * Protocol.TickSeconds;
		pl.LastInputMs = NowMs() - (long)Math.Round(Protocol.TickSeconds * 1000);
		byte flags = (byte)((byte)MoveRules.FacingFrom(rumo, pl.Facing) | Protocol.InputAndando);
		AplicarInput(pl, pl.SeqInput + 1, (uint)RelogioDeQuadrosMs(), pl.Pos + rumo.Normalized() * passo, flags);
	}

	/// <summary>
	/// O PASSO DE SAIDA E O QUE VEM DEPOIS: um pacote no rumo dado, e dali um por tique -- como quem segura a tecla --
	/// ate o corpo estar meio tile ALEM da largura do raio; o resto da cena ele espera parado, com o mundo andando.
	/// (Parar ali e do palco: o campo da familia tem chao conhecido so em volta da linha do raio.) Devolve o que se leu.
	/// </summary>
	private SaidaMedida SairAndando(CenaDeSaida c, Vec2 rumo, double segundos)
	{
		ServerPlayer g = c.Guarda;
		Projetil raio = c.Raio!;
		Vec2 antes = g.Pos;
		double vida = g.Combate.Corpo.Vida();
		float largura = KiContraCorpo.LarguraPraSairDeCima(raio);

		List<(bool Cheio, byte[] Fio)> golpes = [];
		EscutaDeGolpes = golpes;
		try
		{
			PacoteDePasso(g, rumo);
			bool fechou = !_emEmbateDeKi.ContainsKey(g.Id);
			bool passoDado = (g.Pos - antes).Length > 0.5f;
			bool deQuemAtirou = raio.Vivo && raio.Dono == c.Atirador.Id && !raio.EmEmbate;
			bool canalDePe = _canais.GetValueOrDefault(c.Atirador.Id)?.Raio == raio;
			bool guardaErguida = g.Combate.Bloqueando;
			double prazo = raio.SaiuDeLado == g.Id ? raio.FolgaDeQuemSaiuDeLado : 0;

			double saiuEm = -1;
			float avanco = NoEixo(raio.Pos - c.Berco, c.Eixo);
			bool houveCorte = false;
			int tiques = (int)Math.Round(segundos / Protocol.TickSeconds);
			for (int i = 0; i < tiques; i++)
			{
				UmTiqueDoEncontro();
				if (raio.Vivo) avanco = MathF.Max(avanco, NoEixo(raio.Pos - c.Berco, c.Eixo));
				foreach (Projetil p in ProjeteisDaZona(g.Zone.Hash)) houveCorte |= p.NascidoDoCorte != 0;

				float deLado = DeLadoDoEixo(g.Pos - c.Berco, c.Eixo);
				if (saiuEm < 0 && deLado > largura) saiuEm = (i + 1) * Protocol.TickSeconds;
				if (fechou && deLado <= largura + ZoneCollision.TileSize * 0.5f) PacoteDePasso(g, rumo);
			}

			int nele = golpes.Count(x => x.Cheio && LerGolpe(x.Fio).Alvo == g.Id);
			return new SaidaMedida(fechou, passoDado, deQuemAtirou, canalDePe, guardaErguida, prazo,
								   vida - g.Combate.Corpo.Vida(), nele, avanco, saiuEm, houveCorte,
								   DeLadoDoEixo(g.Pos - antes, c.Eixo));
		}
		finally { EscutaDeGolpes = null; }
	}

	/// <summary>Quanto deste vetor e ATRAVESSADO ao eixo, em pixels (sem sinal). O par do <see cref="NoEixo"/>.</summary>
	private static float DeLadoDoEixo(Vec2 v, Vec2 eixo) => MathF.Abs(v.X * eixo.Y - v.Y * eixo.X);

	/// <summary>
	/// O `S2C.Clash` lido byte a byte como o `GameClient` le, pros dois momentos que a saida de lado usa: o fim (quem
	/// venceu, quem perdeu) e o eixo.
	/// </summary>
	private static (Protocol.ClashSub Momento, int Venc, int Perd, Vec2 Eixo) LerEmbate(byte[] fio)
	{
		var r = new NetDataReader(fio);
		r.GetByte();   // o id do pacote (`Protocol.Begin`)
		var momento = (Protocol.ClashSub)r.GetByte();
		return momento switch
		{
			Protocol.ClashSub.Acabou => (momento, r.GetInt(), r.GetInt(), default),
			Protocol.ClashSub.SaidaDeLado => (momento, 0, 0, r.GetVec()),
			_ => (momento, 0, 0, default),
		};
	}
}
