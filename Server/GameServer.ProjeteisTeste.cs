using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DOS ATAQUES DE KI (`--projetilteste`) -- ela roda no BOOT e nao precisa de ninguem em
/// jogo.
///
///     Godot --headless --path . --server --port 7961 --projetilteste
///
/// ============================ ELA CHAMA O CODIGO DE PRODUCAO, E SO ELE ============================
/// Nao ha um `PassoDeTeste`, nao ha uma cadeia de dano paralela e nao ha um tique proprio. Os tiros
/// nascem por <c>Disparar</c>, andam por <c>TickDosProjeteis</c>, acertam por <c>Acertar</c>, ferem
/// pelo <c>MeleeResolver.AplicarDanoPronto</c> e o raio e conduzido pelo <c>TickDosCanaisDeKi</c> --
/// os mesmos metodos que o `C2S.Habilidade` de um jogador de verdade aciona.
///
/// O UNICO PRIVILEGIO e a CADENCIA: o laco chama os dois tiques na mao em vez de esperar o
/// `_Process`. Sem isso a bancada nao rodaria no boot, e uma bancada que precisa de duas janelas
/// abertas nao e rodada.
/// =============================================================================================
///
/// ============================ O QUE ELA AFIRMA ============================
///  1. OS TRES TIPOS voam do jeito que cada um promete -- a bola reto, o teleguiado atras de um alvo
///     que FOGE, e o raio saindo de uma carga com rastro que cresce, voa inteiro e ESTOURA no fim do
///     alcance (o rastro engolido ficou pro raio que para numa parede).
///  2. QUEM VALIDA E O SERVIDOR: o alvo perde vida, entra em combate e ganha pericia de defesa de
///     ki, tudo por chamada de producao -- e a cadeia de dano de ki e a de KI (`Ekidef` ao
///     quadrado), nao a do soco.
///  3. A CARGA PRENDE, e prende pelo funil certo: `PodeMexerOCorpo` -- o MESMO que a tecla C e o
///     embate usam, e o mesmo que a IA obedece.
///  4. O CENARIO PARA O TIRO, e a altura o deixa passar por cima.
///  5. O TETO DISPARA. Nao "existe": dispara. Ver a familia 6.
///  6. O CUSTO, MEDIDO: quanto custa o tique com a zona lotada, e a afirmacao de que ele cabe no
///     orcamento de 33 ms do servidor a 30 Hz.
///  7. O RAIO ARA O CHAO por onde passa -- lido nos BYTES que sairiam pro cliente, com os quatro
///     contra-exemplos que importam (a bola, o disparo do alto, o vacuo e a agua, que ganha ONDA e
///     nao sulco) e o custo disso medido.
///  8. O RAIO LEVA QUEM E MAIS FRACO QUE ELE, na velocidade que a diferenca de forca da, ate o fim do
///     alcance -- onde estoura. Com o igual (que fica), o muro, a agua, a queima-roupa, o boneco
///     largado, a disputa de ki e quem cresce no meio do caminho (que e solto) como contra-exemplos.
///  9. E ELA SABE REPROVAR (familia 10): cinco defeitos plantados no rastro medido, cada um obrigado
///     a deixar VERMELHA a regra que leva o nome dele, mais duas injecoes no estado de PRODUCAO do
///     arrasto. Sem esta familia, as oito de cima sao afirmacoes sobre si mesmas.
/// =========================================================================
///
/// ============================ O IMPACTO E O DA ESCADA (dono, 2026-10-12) ============================
/// O que um tiro faz ao corpo que acerta e a regra do `KiContraCorpo`: quem e mais fraco que o tiro e
/// levado, quem tem de uma a quatro vezes o poder dele apanha e fica, quem tem quatro vezes ou mais e
/// parede, defletir pede o dobro, e todo raio estoura no fim do alcance. Os corpos de cada cena dizem
/// em que degrau ela esta (<see cref="BpDoFeixeDaEscada"/> e vizinhos). O jogo de antes -- o corte por
/// dano, o empurrao de perto, o arrasto de dez tiles, o raio que se apagava -- so aparece aqui como
/// CONTRA-EXEMPLO, com o `KiContraCorpo.RegraDeAntesDeTeste` ligado dentro de um `try/finally` na
/// propria cena.
/// ====================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Faixa de ids desta bancada -- longe do `_nextId`, do convivio (90.100) e do sol (90.400).</summary>
	private const int IdBaseDeProjetil = 90_800;

	/// <summary>A zona da bancada: a Terra, porque ela tem colisao de verdade no catalogo.</summary>
	private static readonly ZoneKey ZonaDaBancadaDeProjetil = new(ZoneKey.KindPremade, "Earth");

	private int _pjOk, _pjFalhou, _pjCorpos;

	// =====================================================================
	// OS CORPOS DA ESCADA (`KiContraCorpo`, dono 2026-10-12)
	// =====================================================================
	/// <summary>
	/// O PODER DE QUEM ATIRA nas cenas de impacto -- e, com ele, o do tiro (`Projetil.Bp` e o poder expresso de
	/// quem atirou, congelado no disparo). Os outros corpos dessas cenas se escrevem EM RELACAO a ele, que e como
	/// a escada os le: a razao `poder do corpo / poder do tiro`.
	/// </summary>
	private const double BpDoFeixeDaEscada = 200_000;

	/// <summary>
	/// O MAIS FRACO: metade do poder do tiro. E quem o raio LEVA -- e a metade deixa o fator do arrasto
	/// (`KiContraCorpo.FatorDoArrasto`: 0,6) longe das duas pontas, pra a medida de velocidade separar "na do
	/// feixe" de "na do arrasto".
	/// </summary>
	private const double BpDoMaisFracoDaEscada = BpDoFeixeDaEscada / 2;

	/// <summary>O IGUAL (razao 1): apanha e FICA -- o primeiro degrau de quem nao e levado.</summary>
	private const double BpDoIgualDaEscada = BpDoFeixeDaEscada;

	/// <summary>A PAREDE: cinco vezes o poder do tiro (o degrau comeca em `KiContraCorpo.VezesDaParede`, quatro).</summary>
	private const double BpDaParedeDaEscada = BpDoFeixeDaEscada * 5;

	/// <summary>
	/// O MAPA DA ZONA DA BANCADA, lido uma vez. Ele NAO e enfeite: a Terra e um planeta com pedras,
	/// casas e arvores, e a primeira versao desta bancada largava os corpos em coordenadas redondas
	/// (1000, 2000, 3000) que calharam de cair DENTRO de cenario. O tiro batia numa parede a dois
	/// tiles do atirador e quatro familias reprovavam por um motivo que nao era o delas.
	///
	/// Ou seja: a colisao ja funcionava tao bem que quebrou o teste. Por isso os corpos agora nascem
	/// em <see cref="CorredorLivre"/>, e nao em numeros escolhidos por serem bonitos.
	/// </summary>
	private ZoneCollision? _pjMapa;

	/// <summary>
	/// UM CORREDOR HORIZONTAL LIVRE de <paramref name="tiles"/> celulas, achado no mapa de verdade.
	/// Deterministico (varredura em ordem), e cada chamada continua de onde a anterior parou pra que
	/// duas familias nao briguem pelo mesmo pedaco de chao.
	/// </summary>
	private Vec2 CorredorLivre(int tiles)
	{
		ZoneCollision? mapa = _pjMapa;
		if (mapa == null) return new Vec2(_pjProximoCorredor * 64 + 32, 32);

		for (int y = _pjProximoCorredor; y < 250; y++)
		{
			for (int x = 4; x + tiles < 250; x++)
			{
				bool livre = true;
				for (int d = 0; d <= tiles && livre; d++) livre &= !mapa.BlockedCell(x + d, y);
				if (!livre) continue;

				// PULA UMA FAIXA: a familia seguinte nao pode pousar em cima desta, senao os corpos
				// de uma viram alvo da outra e o teste mede a bancada.
				_pjProximoCorredor = y + 3;
				return new Vec2(x * ZoneCollision.TileSize + 16, y * ZoneCollision.TileSize + 16);
			}
		}

		AfirmarPj($"achei um corredor livre de {tiles} tiles no mapa da bancada", false, "varredura falhou");
		return new Vec2(64, 64);
	}

	private int _pjProximoCorredor = 8;

	/// <summary>
	/// UMA PRACA 3x3 LIVRE -- pra quem precisa dos QUATRO lados, e nao de uma pista.
	///
	/// O <see cref="CorredorLivre"/> so promete chao livre PRA DIREITA: a familia da boca do cano
	/// atira tambem pro norte, pro sul e pro oeste, e a primeira versao dela reprovou duas vezes por
	/// isso -- a celula acima e a de tras eram pedra. Reprovar por causa do mapa e a mesma armadilha
	/// que o cabecalho do <see cref="_pjMapa"/> ja registra, agora pelo outro eixo.
	/// </summary>
	private Vec2 PracaLivre()
	{
		ZoneCollision? mapa = _pjMapa;
		if (mapa == null) return new Vec2(_pjProximoCorredor * 64 + 32, 32);

		for (int y = _pjProximoCorredor; y < 250; y++)
			for (int x = 4; x < 250; x++)
			{
				bool livre = true;
				for (int dy = -1; dy <= 1 && livre; dy++)
					for (int dx = -1; dx <= 1 && livre; dx++)
						livre &= !mapa.BlockedCell(x + dx, y + dy);
				if (!livre) continue;

				_pjProximoCorredor = y + 3;
				return new Vec2(x * ZoneCollision.TileSize + 16, y * ZoneCollision.TileSize + 16);
			}

		AfirmarPj("achei uma praca 3x3 livre no mapa da bancada", false, "varredura falhou");
		return new Vec2(64, 64);
	}

	private void AfirmarPj(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _pjOk++; GD.Print($"[projetil]   OK    {oque}"); return; }
		_pjFalhou++;
		GD.PrintErr($"[projetil]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDeProjeteis()
	{
		_pjOk = _pjFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[projetil] ================ OS ATAQUES DE KI QUE VIAJAM ================");

		// SEM MAPA A BANCADA NAO MEDE NADA, e isso e uma FALHA e nao um aviso: quatro das sete
		// familias dependem de saber onde ha chao livre.
		AfirmarPj("a zona da bancada tem colisao carregada", _pjMapa != null);

		// (O `KiContraCorpo.RegraDeAntesDeTeste` NAO e ligado aqui: ate 2026-10-12 esta bancada rodava inteira com
		//  ele, medindo o jogo de antes. Cada familia mede hoje a escada, e o botao so sobe dentro do contra-exemplo
		//  que o nomeia -- ver o cabecalho da classe.)
		try
		{
			ABolaVoaReto();
			ABocaDoCanoFicaNaFrente();
			ABocaComDefeitoInjetado();
			OTeleguiadoPerseguemQuemFoge();
			ORaioCarregaCanalizaEDesfaz();
			ACargaPrendeOCorpo();
			APoseSegueOCanal();
			AsTresSaidasComDefeitoInjetado();
			OCenarioParaOTiroEAAlturaNao();
			OTetoDispara();
			OCustoDoTique();
			ORaioAraOChaoPorOndePassa();
			ORaioLevaQuemAcerta();
			ACabecaFicaNaFrenteDeQuemAcerta();
			OTroncoSeCortaOndeAlguemEncosta();
			ApanharDerrubaORaio();
			AFaiscaEstouraNaCabeca();
			UmEncontroUmSorteio();
			AParedeNaoSenteOTiro();
			ABancadaSeCobra();
		}
		finally
		{
			LimparTudoDaBancada();
		}

		// QUANTO CHAO A BANCADA GASTOU. Cada cena pede uma faixa nova do mapa (`CorredorLivre` pula tres linhas por
		// pedido) e a varredura acaba na linha 250: quem acrescentar cena le aqui quanto sobra antes de pedir.
		GD.Print($"[projetil]      (chao) os corredores desta rodada foram ate a linha {_pjProximoCorredor} do mapa (a varredura vai ate a 250)");
		GD.Print($"[projetil] ================ {_pjOk} passaram, {_pjFalhou} falharam ================");
	}

	// =====================================================================
	// 1) A BOLA
	// =====================================================================
	/// <summary>
	/// BLAST: sai reto na direcao do olhar, anda na velocidade do DM, acerta quem estiver na frente
	/// e MORRE no impacto (ela nao e `piercer`).
	///
	/// A VELOCIDADE E CONFERIDA CONTRA A FORMULA, e nao contra um numero digitado aqui: `lag =
	/// max(1, round(4-speed))` tiques de 0,1 s. Repetir o "0,3 s por tile" nesta bancada criaria a
	/// segunda copia do numero -- exatamente o defeito que a regra 4 da casa proibe.
	/// </summary>
	private void ABolaVoaReto()
	{
		GD.Print("[projetil] -- 1) A BOLA VOA RETO E MORRE EM QUEM ACERTA");

		Vec2 chao = CorredorLivre(24);
		ServerPlayer atirador = Forjar("Atirador", chao, bp: 5_000);
		atirador.Facing = Facing.East;

		// `Deflectivel = false` PELO MESMO MOTIVO DO `RaioDaBancada`: esta familia mede "a bola anda,
		// encosta e machuca", e o `Acertar` sorteia o RASPAO em todo impacto contra quem esta de pe e nao e
		// parede pro tiro (a deflexao cheia nem entra neste par: ela pede o dobro do poder do tiro,
		// `KiContraCorpo.PodeDefletir`, e a vitima tem um decimo). Com atirador 5.000, vitima 500 e
		// `base_damage` 1 a chance MEDIDA e 0,0999% por impacto, e o raspao e a metade dela: uma rodada em
		// duas mil terminaria com `fim = Apagou` sem nada errado no jogo. E o defeito que a bancada da
		// tecnica customizada pegou em 2026-09-02, e ele mora aqui igual. (Quem mede o dado e a familia 15.)
		Projetil p = Disparar(atirador, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 20,
			Deflectivel = false,
		});
		AfirmarPj("PREPARO: o voo medido mais o trecho ate a vitima cabem no alcance da bola (senao ela se apaga antes de chegar)",
				  0.5 / p.SegundosPorTile + 200.0 / ZoneCollision.TileSize + 1 < p.MaxDistancia,
				  $"{0.5 / p.SegundosPorTile:0.#} + {200.0 / ZoneCollision.TileSize:0.#} tiles de {p.MaxDistancia:0}");

		// A BOCA DO CANO, E NAO O UMBIGO. Ate aqui esta linha cobrava `p.Pos == atirador.Pos`, e era
		// ela que ficava verde com o defeito que o dono fotografou: o quadro de 32x32 da cabeca,
		// carimbado CENTRADO no centro do sprite, tapava o personagem inteiro. O DM da `step(A,A.dir)`
		// no mesmo tique do nascimento -- ver `BocaDeCano`.
		AfirmarPj("a bola nasceu viva e na BOCA DO CANO -- um tile a frente, nunca em cima do corpo",
				  p.Vivo && p.Pos.Equals(BocaDeCano.De(atirador.Pos, MeleeArea.Frente(Facing.East))),
				  $"{p.Pos} vs corpo em {atirador.Pos}");

		double esperadoPorTile = Projetil.AtrasoDeBola(1);
		AfirmarPj("o atraso por tile e o da regra (`Projetil.AtrasoDeBola`: o `lag` do DM, na pressa que o dono pediu)",
				  Math.Abs(p.SegundosPorTile - esperadoPorTile) < 1e-9,
				  $"{p.SegundosPorTile:0.###} vs {esperadoPorTile:0.###}");

		// MEIO SEGUNDO DE VOO. A distancia esperada sai da MESMA constante, nunca de um literal. (Era um segundo
		// inteiro, quando a bola andava 3,3 tiles por segundo; a 16, um segundo de voo mais os 200 px ate a vitima
		// passavam dos 20 tiles de alcance, e a bola se apagava no ar antes de encostar em alguem.)
		float antes = p.Pos.X;
		for (int i = 0; i < 15; i++) TickDosProjeteis(Protocol.TickSeconds);
		float andou = p.Pos.X - antes;
		float previsto = (float)(ZoneCollision.TileSize / p.SegundosPorTile * 0.5);

		AfirmarPj("em meio segundo ela anda o que a regra manda",
				  Math.Abs(andou - previsto) < 2f, $"{andou:0.#} px vs {previsto:0.#} px");
		AfirmarPj("e ela andou SO no eixo do olhar (nada de deriva)",
				  Math.Abs(p.Pos.Y - chao.Y) < 0.01f, $"y = {p.Pos.Y:0.##}");

		// AGORA COM ALGUEM NO CAMINHO. A vitima nasce a frente, mais fraca, pra o dano ser visivel.
		ServerPlayer vitima = Forjar("Vitima", new Vec2(p.Pos.X + 200, chao.Y), bp: 500);
		double vidaAntes = vitima.Combate.Corpo.Vida();
		double periciaAntes = vitima.Ficha.kidefenseskill;

		for (int i = 0; i < 300 && p.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);

		AfirmarPj("a bola morreu ao encostar num corpo", !p.Vivo && p.Fim == FimDeProjetil.Acertou,
				  $"fim = {p.Fim}");
		AfirmarPj("...e QUEM DECIDIU FOI O SERVIDOR: a vitima perdeu vida",
				  vitima.Combate.Corpo.Vida() < vidaAntes,
				  $"{vidaAntes:0.##} -> {vitima.Combate.Corpo.Vida():0.##}");
		AfirmarPj("...a vitima entrou em combate pelo funil normal",
				  vitima.Combate.EmCombate > 0 && vitima.UltimoAgressor == atirador.Id);
		AfirmarPj("...e APANHAR DE KI TREINA defesa de ki (`kidefensecounter`)",
				  vitima.Ficha.kidefenseskill > periciaAntes,
				  $"{periciaAntes:0.###} -> {vitima.Ficha.kidefenseskill:0.###}");

		// A CADEIA E A DE KI, E NAO A DO SOCO. As duas contas so coincidem por acaso; o que as
		// separa e o `Ekidef` AO QUADRADO. Dobrar a defesa de ki tem que derrubar o dano do raio a
		// aproximadamente um quarto -- e nao mexer no dano do soco.
		var molde = new Fighter { Race = "Human", BP = 500 };
		molde.Statify();
		double d1 = DanoDeKi.Bruto(1, 1, 0, molde);
		molde.Ekidef *= 2;
		double d2 = DanoDeKi.Bruto(1, 1, 0, molde);
		AfirmarPj("dobrar `Ekidef` divide o dano de ki por ~4 (o quadrado do DM)",
				  Math.Abs(d1 / d2 - 4) < 0.01, $"razao {d1 / d2:0.###}");
	}

	// =====================================================================
	// 1-bis) A BOCA DO CANO
	// =====================================================================
	/// <summary>
	/// DE ONDE O TIRO SAI -- a frente do corpo, nos QUATRO sentidos, e nunca em cima dele.
	///
	/// ============================ POR QUE ESTA FAMILIA EXISTE ============================
	/// O dono relatou *"os beams tao saindo DE CIMA do personagem, deveriam sair DA FRENTE dele"*, e
	/// nenhuma das dez familias desta bancada ficou vermelha -- a familia 1 CONFIRMAVA o defeito
	/// (`p.Pos == atirador.Pos`) e as outras nove mediam voo, dano e alcance, que continuavam certos.
	/// Um tiro que nasce no umbigo anda igualzinho a um que nasce na mao.
	///
	/// UM SENTIDO SO NAO PROVA NADA: o deslocamento MUDA com a direcao, e um numero fixo (ou um sinal
	/// trocado) acerta um lado e erra os outros tres. Por isso a volta e pelos quatro, e o eixo
	/// TRANSVERSAL e conferido junto -- um tiro pro leste que tambem desce dois pixels seria um bug
	/// invisivel em qualquer medida que so olhasse "andou pra frente".
	/// ==================================================================================
	/// </summary>
	private void ABocaDoCanoFicaNaFrente()
	{
		GD.Print("[projetil] -- 1-bis) O TIRO NASCE NA FRENTE DO CORPO, NOS QUATRO SENTIDOS");

		// UMA PRACA E NAO UMA PISTA: este teste olha pros QUATRO lados, e o `CorredorLivre` so
		// promete chao livre pra direita. Ver `PracaLivre` -- a primeira versao desta familia
		// reprovou duas vezes medindo a pedra que estava ao norte e a oeste do corredor.
		Vec2 praca = PracaLivre();
		ServerPlayer pl = Forjar("BocaDeCano", praca, bp: 5_000);

		foreach (Facing lado in (Facing[])[Facing.North, Facing.South, Facing.East, Facing.West])
		{
			pl.Facing = lado;
			Vec2 frente = MeleeArea.Frente(lado);

			// SE A VIZINHA ESTIVER BLOQUEADA, o `step()` do DM falha de proposito e o teste seria sobre
			// o mapa. Pula avisando -- calado, ele viraria uma familia que "passa" sem medir nada.
			if (_pjMapa != null && _pjMapa.BlockedAt(BocaDeCano.De(praca, frente)))
			{
				AfirmarPj($"o corredor da bancada tem chao livre pro lado {lado}", false, "celula bloqueada");
				continue;
			}

			Projetil p = Disparar(pl, new ReceitaDeProjetil
			{
				Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 4,
			});

			Vec2 vao = p.Pos - praca;
			float aoLongo = vao.X * frente.X + vao.Y * frente.Y;               // projecao no rumo
			float atravessado = vao.X * frente.Y - vao.Y * frente.X;           // o que sobrou de lado

			AfirmarPj($"{lado}: nasce UM TILE a frente ({ZoneCollision.TileSize} px)",
					  Math.Abs(aoLongo - ZoneCollision.TileSize) < 0.01f, $"{aoLongo:0.##} px");
			AfirmarPj($"{lado}: ...e nada de deriva pro lado",
					  Math.Abs(atravessado) < 0.01f, $"{atravessado:0.##} px");

			// A PROVA QUE O DONO FOTOGRAFOU: o quadro de 32x32 do tiro, carimbado CENTRADO na cabeca,
			// nao pode encostar no quadro de 32x32 do corpo. Encostando, ele tapa o personagem -- que e
			// literalmente "saindo de cima dele".
			AfirmarPj($"{lado}: ...e o quadro do tiro NAO cobre o do corpo",
					  vao.Length >= ZoneCollision.TileSize - 0.01f, $"vao de {vao.Length:0.##} px");

			// E A CAUDA NASCE JUNTO DA CABECA. Numa bola isso e o dado que o `ProjetilState` manda
			// (`Cauda = Pos`); num raio e o pedaco `origin`, o que fecha o feixe do lado da mao.
			AfirmarPj($"{lado}: ...e a cauda nasce na mesma boca", p.Cauda.Equals(p.Pos));

			LimparTudoDaBancada([pl]);
		}

		// A ALTURA VIAJA. O servidor JA usava o `Altitude` do tiro pra colisao (`AtravessaCenario`,
		// `PodeAcertar`), mas ela nunca chegava ao cliente -- e o feixe de quem voa era desenhado no
		// plano do chao, ate 160 px ABAIXO do corpo. Aqui se afirma que o tiro NASCE com a altura do
		// dono; quem levanta o desenho e o `ProjetilDesenhado.Altitude`, com a mesma `EscalaNaTela`.
		pl.Facing = Facing.East;
		pl.Altitude = Voo.AlturaQueAtravessa + 1;
		Projetil alto = Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 4,
		});
		AfirmarPj("o tiro de quem voa nasce COM a altura do dono",
				  Math.Abs(alto.Altitude - pl.Altitude) < 0.01f,
				  $"{alto.Altitude:0.#} vs {pl.Altitude:0.#}");
		pl.Altitude = 0;

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 1-ter) A BOCA DO CANO, COM O DEFEITO INJETADO
	// =====================================================================
	/// <summary>
	/// A FAMILIA 1-bis SABE FICAR VERMELHA? -- e a pergunta que ela nao pode responder sozinha.
	///
	/// ============================ POR QUE ELA PRECISA EXISTIR ============================
	/// A 1-bis mede "nasceu 32 px a frente, sem deriva, sem cobrir o corpo" e passa. Passaria os mesmos
	/// verdes se estivesse comparando o berco com ele mesmo, se o `Disparar` tivesse deixado de ser
	/// chamado, ou se `MeleeArea.Frente` devolvesse zero nos quatro lados. **Este projeto ja pagou por
	/// acreditar em verde de graca**: quatro defeitos visuais atravessaram quatro mil checagens porque
	/// a bancada media INTENCAO.
	///
	/// O defeito injetado aqui e LITERALMENTE o codigo de antes do conserto -- o tiro nascendo em
	/// `pl.Pos` --, e ele entra pelo `deOnde`, que e parametro de PRODUCAO (a Hellzone e o Ki Minefield
	/// nascem por ele, em volta do alvo). Nenhuma linha de producao ganhou um `if (bancada)`.
	/// ==================================================================================
	///
	/// E A SEGUNDA METADE E A COLISAO. O conserto criou um risco novo -- o tiro passou a nascer um tile
	/// a frente, e quem esta EXATAMENTE nesse tile? Se o nascimento pulasse por cima dele, teriamos
	/// trocado um defeito de desenho por um buraco de combate, e nenhuma das dez familias veria isso.
	/// A vitima nasce colada, no proprio tile da boca; o CONTROLE e um tiro nascido um tile ALEM dela,
	/// que TEM que errar -- senao "acertou" nao esta medindo nada.
	/// </summary>
	private void ABocaComDefeitoInjetado()
	{
		GD.Print("[projetil] -- 1-ter) O DEFEITO DA BOCA, INJETADO: as regras sabem reprovar?");

		Vec2 praca = PracaLivre();
		ServerPlayer pl = Forjar("BocaInjetada", praca, bp: 5_000);
		pl.Facing = Facing.East;
		Vec2 frente = MeleeArea.Frente(Facing.East);

		// ---------- O DEFEITO DO DONO: o tiro nasce no umbigo ----------
		Projetil doente = Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 4,
		}, rumoDado: frente, deOnde: pl.Pos);

		Vec2 vaoDoente = doente.Pos - praca;
		float aoLongoDoente = vaoDoente.X * frente.X + vaoDoente.Y * frente.Y;

		AfirmarPj("INJETADO: nascendo no umbigo, a regra 'um tile a frente' REPROVA",
				  Math.Abs(aoLongoDoente - ZoneCollision.TileSize) >= 0.01f, $"{aoLongoDoente:0.##} px");
		AfirmarPj("INJETADO: ...e a regra 'o quadro do tiro nao cobre o do corpo' REPROVA",
				  vaoDoente.Length < ZoneCollision.TileSize - 0.01f, $"vao de {vaoDoente.Length:0.##} px");

		// ---------- E O MESMO TIRO, SEM O DEFEITO, CONTINUA PASSANDO ----------
		// Sem esta metade a familia inteira ficaria verde com o `Disparar` quebrado: "reprovou" e
		// facil de conseguir estragando qualquer coisa.
		Projetil sao = Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 4,
		}, rumoDado: frente);
		AfirmarPj("...e o MESMO tiro sem a injecao continua nascendo na boca do cano",
				  sao.Pos.Equals(BocaDeCano.De(praca, frente)), $"{sao.Pos} vs corpo em {praca}");

		LimparTudoDaBancada();

		// ---------- A COLISAO: QUEM ESTA COLADO CONTINUA SENDO ACERTADO ----------
		Vec2 raia = CorredorLivre(10);
		ServerPlayer atirador = Forjar("Colado", raia, bp: 500_000);
		atirador.Facing = Facing.East;

		// EXATAMENTE NO TILE DA BOCA -- e nao "perto dela": o ponto todo e o tile em que o tiro nasce.
		ServerPlayer colada = Forjar("Vitima colada", BocaDeCano.De(raia, frente), bp: 500);
		double vidaAntes = colada.Combate.Corpo.Vida();

		// `Deflectivel = false` NAS DUAS RECEITAS DESTE PAR, e nas duas pela mesma razao do
		// `RaioDaBancada`: o que se mede e a COLISAO, e o raspao e um sorteio que acaba com o tiro antes
		// de ele virar dano (o passo 3 do `Acertar`). Aqui ele e raro -- a chance da conta e 0,001% por
		// impacto com 500.000 contra 500, e o raspao e a metade dela -- mas raro e o intervalo em que este
		// tipo de defeito mora, e o controle logo abaixo PRECISA nascer nas mesmas condicoes do tiro que
		// ele controla: se so um dos dois tivesse o dado ligado, a dupla deixaria de comparar a mesma coisa.
		Projetil tiro = Disparar(atirador, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 6,
			Deflectivel = false,
		}, rumoDado: frente);

		for (int i = 0; i < 60 && tiro.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);

		AfirmarPj("o tiro que nasce um tile a frente ACERTA quem esta colado NESSE tile",
				  colada.Combate.Corpo.Vida() < vidaAntes,
				  $"vida {vidaAntes:0.##} -> {colada.Combate.Corpo.Vida():0.##}, fim = {tiro.Fim}");

		// O CONTROLE: nascido um tile ALEM dela, o mesmo tiro tem que ERRAR.
		double vidaAntes2 = colada.Combate.Corpo.Vida();
		Projetil pulou = Disparar(atirador, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 6,
			Deflectivel = false,   // MESMAS condicoes do tiro de cima: so o ponto de nascimento muda
		}, rumoDado: frente, deOnde: BocaDeCano.De(BocaDeCano.De(raia, frente), frente));

		for (int i = 0; i < 60 && pulou.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);

		AfirmarPj("INJETADO: nascendo um tile ALEM dela, o mesmo tiro NAO a acerta -- a prova de "
				+ "colisao sabe reprovar",
				  colada.Combate.Corpo.Vida() >= vidaAntes2 - 1e-9,
				  $"vida {vidaAntes2:0.##} -> {colada.Combate.Corpo.Vida():0.##}");

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 2) O TELEGUIADO
	// =====================================================================
	/// <summary>
	/// GUIDED: persegue o alvo -- com a curva do `Core.Combat.Teleguiado` (dono, 2026-10-09; era o
	/// `walk_towards` do DM, sem limite de angulo). A prova nao pode ser "acertou um alvo parado" (uma
	/// bola reta faria isso): o alvo TEM que sair da linha, e o tiro tem que virar atras dele. Os limites
	/// da curva (quem corre escapa, quem vai pras costas e perdido) sao da `--miradekiteste`.
	/// </summary>
	private void OTeleguiadoPerseguemQuemFoge()
	{
		GD.Print("[projetil] -- 2) O TELEGUIADO VIRA ATRAS DE QUEM FOGE");

		// ============================ A PERSEGUICAO SE MEDE NO AR, E NAO E TRAPACA ============================
		// A fuga tem que ser LONGA e TRANSVERSAL pro tiro precisar virar de verdade -- e uma fuga longa
		// num planeta com pedras acaba dentro de uma pedra. A primeira versao media isso e reprovava
		// com `fim = Cenario`: o teleguiado nunca chegou a errar, ele bateu num morro no caminho.
		//
		// Entao os tres sobem acima do limiar de voo, onde o tiro atravessa cenario (`Voo.AtravessaCenario`,
		// a MESMA regra que a familia 5 usa e prova). O que se isola aqui e a CURVA; quem mede pedra e a
		// familia 5. Os dois ficam no MESMO andar, senao `Voo.PodeAcertar` recusaria o impacto e a bancada
		// mediria a regra de altura em vez da perseguicao.
		// ================================================================================================
		float noAr = Voo.AlturaQueAtravessa + 1;
		Vec2 pista = CorredorLivre(4);
		ServerPlayer atirador = Forjar("Cacador", pista, bp: 5_000);
		atirador.Facing = Facing.East;
		atirador.Altitude = noAr;
		ServerPlayer presa = Forjar("Presa", new Vec2(pista.X + 400, pista.Y), bp: 5_000);
		presa.Altitude = noAr;

		// `Deflectivel = false` NOS DOIS TIROS DESTA FAMILIA (aqui e no controle la embaixo), pela razao
		// do `RaioDaBancada`: o que se mede e a CURVA, e um raspao sorteado no fim da perseguicao (o passo 3
		// do `Acertar`) trocaria `Acertou` por `Apagou` sem o teleguiado ter errado nada. Entre iguais a
		// deflexao cheia nao sai (ela pede o dobro do poder do tiro), mas o raspao sai: com 5.000 contra
		// 5.000 e `base_damage` 15 a chance da conta e 0,0044% por impacto, e ele e a metade dela -- e o
		// controle depende de nascer nas MESMAS condicoes, senao "so o TIPO muda" deixa de ser verdade.
		Projetil p = Disparar(atirador, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Guided, BaseDano = 15, Velocidade = 1, AlcanceTiles = 60,
			Deflectivel = false,
		});
		p.Alvo = presa.Id;
		p.VidaRestante = 60;

		// A PRESA FOGE PRO NORTE. Uma bola reta passaria reto e nunca a alcancaria.
		for (int i = 0; i < 900 && p.Vivo; i++)
		{
			presa.Pos = new Vec2(presa.Pos.X, presa.Pos.Y + 3f);   // pro SUL: o mapa tem chao ali
			TickDosProjeteis(Protocol.TickSeconds);
		}

		AfirmarPj("o teleguiado alcancou a presa que fugia", !p.Vivo && p.Fim == FimDeProjetil.Acertou,
				  $"fim = {p.Fim}, tiro em {p.Pos}, presa em {presa.Pos}");

		// O CONTRA-EXEMPLO, e sem ele a afirmacao acima nao vale nada: a MESMA fuga contra uma BOLA
		// comum tem que terminar sem acerto. Sem esta metade, um bug que fizesse todo tiro
		// perseguir passaria verde.
		Vec2 pista2 = CorredorLivre(4);
		ServerPlayer atirador2 = Forjar("Cacador2", pista2, bp: 5_000);
		atirador2.Facing = Facing.East;
		atirador2.Altitude = noAr;   // MESMAS condicoes do teleguiado: so o TIPO muda
		ServerPlayer presa2 = Forjar("Presa2", new Vec2(pista2.X + 400, pista2.Y), bp: 5_000);
		presa2.Altitude = noAr;

		Projetil reto = Disparar(atirador2, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 15, Velocidade = 1, AlcanceTiles = 60,
			Deflectivel = false,   // MESMAS condicoes do teleguiado: so o TIPO muda
		});
		reto.VidaRestante = 60;

		for (int i = 0; i < 900 && reto.Vivo; i++)
		{
			presa2.Pos = new Vec2(presa2.Pos.X, presa2.Pos.Y + 3f);
			TickDosProjeteis(Protocol.TickSeconds);
		}
		AfirmarPj("...e a bola COMUM, na mesma fuga, NAO acerta", reto.Fim != FimDeProjetil.Acertou,
				  $"fim = {reto.Fim}");
	}

	// =====================================================================
	// 3) O RAIO
	// =====================================================================
	/// <summary>
	/// BEAM: carrega, sai, o rastro CRESCE enquanto o dono segura, e quando ele solta o raio voa INTEIRO --
	/// o trem de segmentos do DM visto de longe -- ate o fim do alcance, onde ESTOURA (dono, 2026-10-12). O
	/// rastro engolido pra dentro do ponto de parada ficou pro raio que PARA sem estourar: o que bate numa parede.
	/// </summary>
	private void ORaioCarregaCanalizaEDesfaz()
	{
		GD.Print("[projetil] -- 3) O RAIO: CARGA, CANAL, O VOO INTEIRO E O ESTOURO NO FIM DO ALCANCE");

		Vec2 raia = CorredorLivre(28);
		ServerPlayer pl = Forjar("Raiador", raia, bp: 50_000);
		pl.Facing = Facing.East;
		pl.Ficha.Ki = pl.Ficha.MaxKi;

		double kiAntes = pl.Ficha.Ki;
		// O ALCANCE CABE NO CORREDOR (24 tiles mais a boca do cano, em 28): o raio desta familia tem que chegar ao FIM
		// DELE, e nao numa pedra -- o que ele faz ao parar em cenario e outra regra, medida mais abaixo.
		var receita = new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam, BaseDano = 1, Velocidade = 1, AlcanceTiles = 24,
			CargaMinima = 1, Nome = "Onda de Ki",
		};
		Canalizar(pl, "Ki_Wave", 10 * pl.Ficha.BaseDrain(), receita);

		AfirmarPj("comecou a carga (o canal existe)", _canais.ContainsKey(pl.Id));
		AfirmarPj("...e a carga JA COBROU Ki", pl.Ficha.Ki < kiAntes);
		AfirmarPj("...e ainda NAO ha raio no ar (carga nao e disparo)",
				  ProjeteisDaZona(pl.Zone.Hash).Count(t => t.Dono == pl.Id) == 0);

		double carga = Projetil.SegundosDeCarga(1, pl.Ficha);
		AfirmarPj("a carga dura o que a formula do DM manda (`10*chargedelay/3` ciclos de 0,2 s)",
				  carga > 0.05 && carga < 2, $"{carga:0.###}s");

		// ATE A CARGA FECHAR. O laco tem TETO pra que "o raio nunca saiu" seja uma FALHA e nao um
		// travamento -- a mesma disciplina do `TiquesDePouso` da bancada do berco.
		int t = 0;
		for (; t < 300 && _canais[pl.Id].Raio == null; t++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
		}
		Projetil? raio = _canais.GetValueOrDefault(pl.Id)?.Raio;
		AfirmarPj("o raio saiu quando a carga fechou", raio is { Vivo: true }, $"em {t} tiques");
		if (raio == null) return;

		AfirmarPj("...e ele nasceu CANALIZADO (o dono ainda o segura)", raio.Canalizando);

		// O RASTRO CRESCE. Enquanto o dono canaliza a cauda e a mao dele, entao a distancia entre
		// cabeca e cauda so aumenta.
		float rastro1 = raio.Comprimento;
		for (int i = 0; i < 15; i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
		}
		float rastro2 = raio.Comprimento;
		AfirmarPj("o rastro CRESCE enquanto o dono canaliza", rastro2 > rastro1 + 10,
				  $"{rastro1:0} px -> {rastro2:0} px");
		// E A MAO NAO E O UMBIGO: ela e a boca do cano, o MESMO ponto de onde a cabeca nasceu. Antes
		// esta linha media contra `pl.Pos`, e por isso ficava verde com o pedaco `origin` do trem
		// carimbado por cima do personagem.
		AfirmarPj("...e a cauda e a MAO do dono -- a boca do cano, um tile a frente",
				  (raio.Cauda - BocaDeCano.De(pl.Pos, raio.Rumo)).Length < 1f,
				  $"cauda {raio.Cauda}, corpo {pl.Pos}");

		// SOLTOU. A cabeca segue e a cauda vai atras, na mesma velocidade.
		SoltarCanal(pl, "Ki_Wave");
		AfirmarPj("soltar fecha o canal", !_canais.ContainsKey(pl.Id));
		AfirmarPj("...mas o raio que ja saiu CONTINUA no ar", raio.Vivo && !raio.Canalizando);

		// ============================ O RASTRO NAO ENCOLHE NO AR ============================
		// Esta afirmacao ja foi o CONTRARIO, e estava errada. No DM o beam e um trem de segmentos que
		// andam todos na mesma velocidade: solto, ele voa INTEIRO, com comprimento constante. O que
		// esvazia o rastro e a cabeca PARAR -- e dali cada segmento de tras alcanca o ponto onde o da
		// frente se apagou.
		//
		// A bancada pegou a versao errada (a cauda corria atras da cabeca na mesma velocidade, o que
		// deixa a distancia eternamente igual) porque mediu duas vezes e comparou. Ver
		// `Projetil.Esvaziando`.
		// ================================================================================
		float rastro3 = raio.Comprimento;
		for (int i = 0; i < 10; i++) TickDosProjeteis(Protocol.TickSeconds);
		AfirmarPj("...e o rastro voa INTEIRO, sem encolher no ar (o trem do DM)",
				  Math.Abs(raio.Comprimento - rastro3) < 2f,
				  $"{rastro3:0} px -> {raio.Comprimento:0} px");

		// ============================ O ALCANCE ACABA -- E O RAIO ESTOURA (dono, 2026-10-12) ============================
		// *"todo ataque de ki como esfera grande e beam explodem ao chegarem na distancia Maxima, acertando ou nao o
		// inimigo"*. O estouro e o fim do ataque INTEIRO, na hora (`ExplodirNoFimDoAlcance`): a fita some junto com o
		// clarao, sem a cabeca parar pra o rastro ser engolido -- era assim que este raio acabava ate aquele dia, e e
		// o contra-exemplo do fim desta familia.
		// ==================================================================================================================
		int explosoes = ExplosoesDoFimDoAlcanceDeTeste, crateras = CraterasDeExplosaoDeTeste;
		bool viuEsvaziar = false;
		for (int i = 0; i < 900 && raio.Vivo; i++)
		{
			TickDosProjeteis(Protocol.TickSeconds);
			viuEsvaziar |= raio.Esvaziando;
		}
		AfirmarPj("no FIM DO ALCANCE o raio ESTOURA, sem ter encontrado ninguem (`FimDeProjetil.Explodiu`)",
				  !raio.Vivo && raio.Fim == FimDeProjetil.Explodiu && ExplosoesDoFimDoAlcanceDeTeste == explosoes + 1,
				  $"vivo {raio.Vivo}, fim = {raio.Fim}, explosoes +{ExplosoesDoFimDoAlcanceDeTeste - explosoes}");
		AfirmarPj("...com o alcance da receita andado, e nao antes (ele nao parou em cenario)",
				  raio.AndouTiles >= raio.MaxDistancia && raio.AndouTiles < raio.MaxDistancia + 1,
				  $"andou {raio.AndouTiles:0.00} de {raio.MaxDistancia:0} tiles");
		AfirmarPj("...DE UMA VEZ: a cabeca nao para pra o rastro ser engolido -- o estouro e o fim do ataque inteiro",
				  !viuEsvaziar);
		AfirmarPj("...e o chao ganha a cratera GRANDE da explosao (o raio era rasteiro, e a Terra tem chao)",
				  CraterasDeExplosaoDeTeste == crateras + 1, $"crateras +{CraterasDeExplosaoDeTeste - crateras}");

		// O CANAL CAI SEM KI, e nao fica cobrando de um tanque vazio.
		ServerPlayer seco = Forjar("Seco", CorredorLivre(8), bp: 50_000);
		seco.Ficha.Ki = seco.Ficha.MaxKi;
		Canalizar(seco, "Ki_Wave", 10 * seco.Ficha.BaseDrain(), receita);
		seco.Ficha.Ki = 0;
		for (int i = 0; i < 200 && _canais.ContainsKey(seco.Id); i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
		}
		AfirmarPj("o raio morre na mao de quem fica sem Ki", !_canais.ContainsKey(seco.Id));

		ORastroEngolidoEDeQuemPara(raia, receita);
	}

	/// <summary>
	/// O RASTRO ENGOLIDO -- o trem do DM, onde ele continua no jogo, e o contra-exemplo do estouro.
	///
	/// ============================ QUEM PARA SEM ESTOURAR SE ESVAZIA ============================
	/// A cabeca que PARA vira o ponto de encontro e o rabo e sugado pra dentro dela; so entao o tiro sai da
	/// lista, com o motivo que ja tinha sido decidido (`Projetil.Esvaziando`, `Matar`). Ate 2026-10-12 esse era
	/// o fim do raio que gastava o alcance, e ate 2026-10-13 o do que batia em cenario; hoje os dois ESTOURAM
	/// (`KiContraCorpo`: *"a explosao do beam ta sem efeito nenhum"*), e o que se esvazia e o raio que alguem
	/// APAGA -- a Onda de Choque, a cabeca solta empurrada. Tres metades medidas aqui: a parede (estoura; com o
	/// botao da v0.2.0, esvazia), o raio apagado pela onda (a regra de hoje pro rastro engolido) e o fim do
	/// alcance com o jogo de antes.
	/// ==========================================================================================
	/// </summary>
	/// <param name="raia">O corredor da familia, reusado: o raiador do contra-exemplo nasce onde o de cima nasceu.</param>
	/// <param name="receita">A MESMA receita do raio de cima -- no contra-exemplo, so o botao muda.</param>
	private void ORastroEngolidoEDeQuemPara(Vec2 raia, ReceitaDeProjetil receita)
	{
		const int T = ZoneCollision.TileSize;

		// ---------- A PAREDE: o raio ESTOURA nela (dono, 2026-10-13); com o botao da v0.2.0, para e se esvazia ----------
		LimparTudoDaBancada();
		if (_pjMapa is { } mapa && AcharParede(mapa, out int cx, out int cy, livresAtras: 9))
		{
			(Projetil Raio, bool Esvaziou, float Rastro, Vec2 Parou, int Explosoes) RaioNoMuro()
			{
				LimparTudoDaBancada();
				ServerPlayer atira = Forjar("RaiadorNoMuro", new Vec2((cx - 8) * T + T / 2f, cy * T + T / 2f), bp: 50_000);
				atira.Facing = Facing.East;
				Projetil noMuro = RaioDaBancada(atira, baseDano: 1);

				int explosoes = ExplosoesDoFimDoAlcanceDeTeste;
				Vec2 ondeParou = default;
				bool viuEsvaziar = false;
				float rastroAoParar = 0;
				for (int i = 0; i < 300 && noMuro.Vivo; i++)
				{
					TickDosProjeteis(Protocol.TickSeconds);
					if (!noMuro.Esvaziando || viuEsvaziar) continue;
					viuEsvaziar = true;
					ondeParou = noMuro.Pos;
					rastroAoParar = noMuro.Comprimento;
				}
				return (noMuro, viuEsvaziar, rastroAoParar, ondeParou, ExplosoesDoFimDoAlcanceDeTeste - explosoes);
			}

			// (o ki nao mexe no cenario durante esta medida: o muro tem que continuar de pe pro contra-exemplo)
			KiContraCorpo.KiNaoEstragaCenarioDeTeste = true;
			try
			{
				var hoje = RaioNoMuro();
				AfirmarPj("o raio que bate numa PAREDE ESTOURA nela, de uma vez (sem o rastro ser engolido antes)",
						  hoje.Raio is { Vivo: false, Fim: FimDeProjetil.Explodiu } && !hoje.Esvaziou && hoje.Explosoes == 1
						  && Math.Abs(hoje.Raio.Pos.X - cx * T) <= T,
						  $"fim = {hoje.Raio.Fim}, esvaziou {hoje.Esvaziou}, explosoes +{hoje.Explosoes}, parou em {hoje.Raio.Pos} (parede em x {cx * T})");

				KiContraCorpo.SoEstouraNoFimDoAlcanceDeTeste = true;
				try
				{
					var antes = RaioNoMuro();
					AfirmarPj("(defeito injetado: so estoura no fim do alcance) o raio PARA na parede e o rastro e engolido (o trem do DM), ate sair com `Cenario` -- sem estouro",
							  antes.Esvaziou && antes.Rastro > 4 * T && Math.Abs(antes.Parou.X - cx * T) <= T
							  && antes.Raio is { Vivo: false, Fim: FimDeProjetil.Cenario } && antes.Explosoes == 0,
							  $"esvaziou {antes.Esvaziou}, rastro de {antes.Rastro:0} px, parou em {antes.Parou}, fim = {antes.Raio.Fim}, explosoes +{antes.Explosoes}");
					AfirmarPj("(defeito injetado) ...e a cauda terminou EM CIMA do ponto onde a cabeca parou",
							  antes.Esvaziou && (antes.Raio.Cauda - antes.Parou).Length < T, $"cauda {antes.Raio.Cauda}, parou em {antes.Parou}");
				}
				finally { KiContraCorpo.SoEstouraNoFimDoAlcanceDeTeste = false; }
			}
			finally { KiContraCorpo.KiNaoEstragaCenarioDeTeste = false; }
		}
		else AfirmarPj("achei uma parede com nove celulas livres na frente pra medir o raio no muro", false, "varredura nao achou");

		// ---------- O RAIO QUE ALGUEM APAGA (a Onda de Choque): a cabeca para e o rastro e engolido ----------
		LimparTudoDaBancada();
		{
			ServerPlayer atira = Forjar("RaiadorApagado", raia, bp: 50_000);
			atira.Facing = Facing.East;
			// quem apaga fica a um tile e meio da LINHA do raio (fora do caminho da cabeca) e e mais forte que o tiro
			ServerPlayer apaga = Forjar("QuemApaga", raia + new Vec2(7 * T, 1.5f * T), bp: 200_000);
			Projetil apagado = RaioDaBancada(atira, baseDano: 1);

			int explosoes = ExplosoesDoFimDoAlcanceDeTeste;
			Vec2 ondeParou = default;
			bool viuEsvaziar = false, soprou = false;
			float rastroAoParar = 0;
			for (int i = 0; i < 300 && apagado.Vivo; i++)
			{
				TickDosProjeteis(Protocol.TickSeconds);
				if (!soprou && apagado.AoAlcanceDe(apaga.Pos, 2 * T)) { soprou = true; OndaDeChoqueG6(apaga); }
				if (!apagado.Esvaziando || viuEsvaziar) continue;
				viuEsvaziar = true;
				ondeParou = apagado.Pos;
				rastroAoParar = apagado.Comprimento;
			}
			AfirmarPj("o raio que a ONDA DE CHOQUE apaga para onde estava e o rastro comeca a ser engolido (o trem do DM)",
					  soprou && viuEsvaziar && rastroAoParar > 2 * T,
					  $"soprou {soprou}, esvaziou {viuEsvaziar}, rastro de {rastroAoParar:0} px, parou em {ondeParou}");
			AfirmarPj("...ate o raio sair da lista com o motivo que ja estava decidido -- apagado, e nao um estouro",
					  !apagado.Vivo && apagado.Fim == FimDeProjetil.Apagou && ExplosoesDoFimDoAlcanceDeTeste == explosoes,
					  $"vivo {apagado.Vivo}, fim = {apagado.Fim}, explosoes +{ExplosoesDoFimDoAlcanceDeTeste - explosoes}");
			AfirmarPj("...e a cauda terminou EM CIMA do ponto onde a cabeca parou",
					  viuEsvaziar && (apagado.Cauda - ondeParou).Length < T,
					  $"cauda {apagado.Cauda}, parou em {ondeParou}");
		}

		// ---------- O JOGO DE ANTES: no fim do alcance o raio so se apagava ----------
		LimparTudoDaBancada();
		KiContraCorpo.RegraDeAntesDeTeste = true;
		try
		{
			ServerPlayer deAntes = Forjar("RaiadorDeAntes", raia, bp: 50_000);
			deAntes.Facing = Facing.East;
			deAntes.Ficha.Ki = deAntes.Ficha.MaxKi;
			Canalizar(deAntes, "Ki_Wave", 10 * deAntes.Ficha.BaseDrain(), receita);
			for (int i = 0; i < 300 && _canais.GetValueOrDefault(deAntes.Id)?.Raio == null; i++)
			{
				TickDosCanaisDeKi(Protocol.TickSeconds);
				TickDosProjeteis(Protocol.TickSeconds);
			}
			Projetil? velho = _canais.GetValueOrDefault(deAntes.Id)?.Raio;
			for (int i = 0; i < 15; i++)
			{
				TickDosCanaisDeKi(Protocol.TickSeconds);
				TickDosProjeteis(Protocol.TickSeconds);
			}
			SoltarCanal(deAntes, "Ki_Wave");

			int explosoes = ExplosoesDoFimDoAlcanceDeTeste;
			bool viuEsvaziar = false;
			for (int i = 0; i < 900 && velho is { Vivo: true }; i++)
			{
				TickDosProjeteis(Protocol.TickSeconds);
				viuEsvaziar |= velho.Esvaziando;
			}
			AfirmarPj("(defeito injetado: o jogo de antes) o MESMO raio, no fim do alcance, so se APAGA com o rastro engolido -- "
					  + "sem estouro: as reguas 'estoura' e 'de uma vez' ficam vermelhas",
					  velho is { Vivo: false, Fim: FimDeProjetil.Apagou } && viuEsvaziar && ExplosoesDoFimDoAlcanceDeTeste == explosoes,
					  $"fim = {velho?.Fim}, esvaziou {viuEsvaziar}, explosoes +{ExplosoesDoFimDoAlcanceDeTeste - explosoes}");
		}
		finally { KiContraCorpo.RegraDeAntesDeTeste = false; }
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 4) A CARGA PRENDE -- e prende pelo funil certo
	// =====================================================================
	/// <summary>
	/// `canmove = 0` (`beams.dm:294`). A prova que importa nao e "o corpo nao andou": e que a recusa
	/// sai do <c>PodeMexerOCorpo</c>, o MESMO funil da tecla C, do nocaute e do embate -- e por isso
	/// ela vale tambem pra IA, que chama a mesma funcao.
	///
	/// E A METADE QUE FALTA: o corpo volta a andar assim que o raio sai da mao. Sem ela, uma trava
	/// que nunca soltasse passaria verde -- e travar jogador dentro da propria tecnica ja aconteceu
	/// neste projeto.
	/// </summary>
	private void ACargaPrendeOCorpo()
	{
		GD.Print("[projetil] -- 4) A CARGA PRENDE O CORPO (gate de VETOR, no funil de sempre)");

		ServerPlayer pl = Forjar("Plantado", CorredorLivre(8), bp: 50_000);
		pl.Ficha.Ki = pl.Ficha.MaxKi;

		AfirmarPj("antes de carregar, o corpo anda", PodeMexerOCorpo(pl));

		Canalizar(pl, "Ki_Wave", 10 * pl.Ficha.BaseDrain(), new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam, BaseDano = 1, Velocidade = 1,
			AlcanceTiles = 30, CargaMinima = 1,
		});

		AfirmarPj("carregando, `PodeMexerOCorpo` RECUSA", !PodeMexerOCorpo(pl));
		AfirmarPj("...e a recusa e a mesma que a IA obedece (mesma funcao)", EnraizadoPorKi(pl.Id));

		// ATIRANDO tambem prende: no DM o `canmove` so volta no `stopbeaming()`.
		for (int i = 0; i < 300 && _canais.GetValueOrDefault(pl.Id)?.Raio == null; i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
		}
		AfirmarPj("atirando, ele CONTINUA preso", !PodeMexerOCorpo(pl));

		SoltarCanal(pl, "Ki_Wave");
		AfirmarPj("soltou o raio: o corpo anda de novo", PodeMexerOCorpo(pl));

		// E O CONTRA-EXEMPLO DO TIPO: a BOLA nao prende ninguem.
		ServerPlayer livre = Forjar("Livre", CorredorLivre(8), bp: 50_000);
		livre.Ficha.Ki = livre.Ficha.MaxKi;
		Disparar(livre, new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast });
		AfirmarPj("a BOLA nao prende quem atirou", PodeMexerOCorpo(livre));
	}

	// =====================================================================
	// 4b) A POSE SEGUE O CANAL
	// =====================================================================
	/// <summary>
	/// A POSE DO CORPO ENQUANTO O RAIO ESTA DE PE -- e, sobretudo, **os tres jeitos de ela sair**.
	///
	/// ============================ O QUE ESTA FAMILIA EXISTE PRA PEGAR ============================
	/// O pedido do dono e de ESTADO e nao de evento: *"ele so voltaria a posicao de IDLE quando ele
	/// PARASSE DE USAR O BEAM (por vontade propria ou pq ALGUEM BATEU NELE e cancelou o beam)"*. Uma
	/// pose que fosse um gatilho de prazo passaria a metade facil desta bancada (ela APARECE) e
	/// falharia a que importa (ela FICA, e depois SAI).
	///
	/// Por isso cada saida e medida DUAS vezes: que a pose estava la um instante antes, e que ela
	/// nao esta mais depois. Medir so o "depois" ficaria verde com a pose nunca tendo aparecido --
	/// que e o defeito de bancada que este projeto ja catalogou ("nascer DENTRO do estado nunca
	/// testa a ENTRADA nele").
	///
	/// A TERCEIRA SAIDA E NOVA NO PORT. `if(KB) stopbeaming()` (`beams.dm:73-74`) nao tinha porte
	/// nenhum: nao havia caminho pra bater em alguem e derrubar o raio dele. Se alguem apagar o
	/// `DerrubarRaioPorGolpe` do `Arremessar`, a linha do arremesso aqui fica vermelha sozinha.
	/// =========================================================================================
	/// </summary>
	private void APoseSegueOCanal()
	{
		GD.Print("[projetil] -- 4b) A POSE SEGUE O CANAL (e as TRES saidas)");

		var receita = new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam, BaseDano = 1, Velocidade = 1,
			AlcanceTiles = 30, CargaMinima = 1, Nome = "Onda de Ki",
		};

		// O ESTADO DO FIO, e nao o campo interno: `EstadoDe` e a fabrica UNICA do snapshot, e e por
		// ela que a pose vira pixel. Ler `_canais` direto aqui provaria que o dicionario existe e
		// nao provaria que alguem o publica -- que e exatamente a distancia entre "escrito" e
		// "ligado" que este projeto ja pagou com uma API inteira orfa.
		Protocol.Pose PoseNoFio(ServerPlayer p) => EstadoDe(p, NowMs()).Pose;

		// ---------------------------------------------------------------
		// A ENTRADA: parado -> carregando -> atirando
		// ---------------------------------------------------------------
		ServerPlayer pl = Forjar("Poseiro", CorredorLivre(28), bp: 50_000);
		pl.Facing = Facing.East;
		pl.Ficha.Ki = pl.Ficha.MaxKi;

		AfirmarPj("antes do raio a pose e a NORMAL", PoseNoFio(pl) == Protocol.Pose.Normal,
				  $"{PoseNoFio(pl)}");

		Canalizar(pl, "Ki_Wave", 10 * pl.Ficha.BaseDrain(), receita);
		EntityState carregando = EstadoDe(pl, NowMs());
		AfirmarPj("carregando, a pose ja e a do CANAL", carregando.Pose == Protocol.Pose.Canalizando,
				  $"{carregando.Pose}");
		AfirmarPj("...mas a FASE diz que o feixe ainda nao saiu (`charging`, corpo no idle)",
				  !carregando.CanalAtirando);
		AfirmarPj("...e o desenho da carga veio junto (o `ChargeState`, 1..9)",
				  carregando.CargaDoCanal is >= 1 and <= 9, $"{carregando.CargaDoCanal}");

		for (int i = 0; i < 300 && _canais.GetValueOrDefault(pl.Id)?.Raio == null; i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
		}
		EntityState atirando = EstadoDe(pl, NowMs());
		AfirmarPj("a carga fechou e a FASE virou (`beaming`: agora o corpo desenha `blast`)",
				  atirando.Pose == Protocol.Pose.Canalizando && atirando.CanalAtirando);

		// ============================ E ELA NAO E UM PRAZO ============================
		// A `Atacando` vive `AttackPoseMs` e morre sozinha. Esta tem que sobreviver a um numero de
		// tiques MAIOR do que aquele prazo -- senao "a pose fica" seria so "o prazo ainda nao venceu".
		// ==========================================================================
		for (int i = 0; i < 40; i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
		}
		AfirmarPj($"...e ela CONTINUA depois de {Protocol.AttackPoseMs} ms (nao e prazo, e estado)",
				  PoseNoFio(pl) == Protocol.Pose.Canalizando, $"{PoseNoFio(pl)}");

		// ---------------------------------------------------------------
		// O SOCO ATRAVESSA E DEVOLVE -- o `prev_state` do `Fight()`
		// ---------------------------------------------------------------
		// `var/prev_state = icon_state` / `icon_state = "Attack"` / ... / `icon_state = prev_state`
		// (`CombatMovement.dm:133-176`). Aqui isso sai da ORDEM do `ServerPlayer.Pose` e nao de um
		// "guardar e devolver": o prazo do soco ganha enquanto corre, o estado responde de novo
		// quando ele vence.
		pl.AtaqueAte = NowMs() + 400;
		AfirmarPj("socar com o raio de pe mostra o SOCO (o prazo ganha do estado)",
				  PoseNoFio(pl) == Protocol.Pose.Atacando, $"{PoseNoFio(pl)}");
		pl.AtaqueAte = 0;
		AfirmarPj("...e o raio VOLTA sozinho quando o soco acaba (nada foi apagado)",
				  PoseNoFio(pl) == Protocol.Pose.Canalizando, $"{PoseNoFio(pl)}");

		// ---------------------------------------------------------------
		// A DIRECAO NAO GIRA -- o `turnlock` do DM
		// ---------------------------------------------------------------
		// `Rumo` e cravado uma vez no `Disparar` e nunca reescrito: se o corpo girasse, a pose
		// apontaria pra um lado e o feixe pro outro. O caminho do PASSO ja recusa (o
		// `PodeMexerOCorpo`); o do GOLPE nao tinha recusa nenhuma, e e o que esta linha mede.
		ServerPlayer atras = Forjar("PelasCostas", pl.Pos + new Vec2(-64, 0), bp: 10);
		pl.AlvoId = atras.Id;
		Facing antesDoSoco = pl.Facing;
		AfirmarPj("(a bancada marcou mesmo alguem -- senao a linha abaixo passaria de graca)",
				  Marcado(pl) is { } m && m.Id == atras.Id);
		Atacar(pl, Protocol.Golpe.Leve);
		AfirmarPj("socar um alvo PELAS COSTAS nao gira quem esta com o raio na mao (`turnlock`)",
				  pl.Facing == antesDoSoco, $"{antesDoSoco} -> {pl.Facing}");
		pl.AlvoId = 0;

		// ============================ E O SOCO DE VERDADE DEIXA RASTRO -- NA BANCADA TAMBEM ============================
		// O `Atacar` acima e o de PRODUCAO, entao ele escreveu `AtaqueAte` de verdade. Sem esta linha
		// as tres saidas abaixo mediriam um corpo em pose de SOCO e a primeira delas ficava vermelha
		// dizendo "Atacando" -- foi o que aconteceu na primeira rodada desta familia.
		//
		// Vale registrar porque o vermelho foi UTIL: ele provou, por acidente, que a ordem do
		// `ServerPlayer.Pose` e a que esta escrita (o prazo do soco ganha do estado do canal), o que
		// duas linhas acima ja afirmam de proposito.
		// ==========================================================================================================
		pl.AtaqueAte = 0;

		// ---------------------------------------------------------------
		// SAIDA 1 -- POR VONTADE PROPRIA
		// ---------------------------------------------------------------
		AfirmarPj("SAIDA 1, antes: a pose do raio esta de pe",
				  PoseNoFio(pl) == Protocol.Pose.Canalizando);
		SoltarCanal(pl, "Ki_Wave");
		AfirmarPj("SAIDA 1 (soltou): volta ao idle NO MESMO TIQUE",
				  PoseNoFio(pl) == Protocol.Pose.Normal, $"{PoseNoFio(pl)}");
		AfirmarPj("...e o byte do canal nem viaja mais (ele so vai com a pose de canal)",
				  !EstadoDe(pl, NowMs()).CanalAtirando);

		// ---------------------------------------------------------------
		// SAIDA 2 -- O KI ACABOU
		// ---------------------------------------------------------------
		ServerPlayer seco = Forjar("SemFolego", CorredorLivre(28), bp: 50_000);
		seco.Facing = Facing.East;
		seco.Ficha.Ki = seco.Ficha.MaxKi;
		Canalizar(seco, "Ki_Wave", 10 * seco.Ficha.BaseDrain(), receita);
		for (int i = 0; i < 300 && _canais.GetValueOrDefault(seco.Id)?.Raio == null; i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
		}
		AfirmarPj("SAIDA 2, antes: o raio dele saiu e a pose esta de pe",
				  PoseNoFio(seco) == Protocol.Pose.Canalizando, $"{PoseNoFio(seco)}");

		seco.Ficha.Ki = 0;
		for (int i = 0; i < 30 && _canais.ContainsKey(seco.Id); i++)
			TickDosCanaisDeKi(Protocol.TickSeconds);
		AfirmarPj("SAIDA 2 (o Ki acabou): a pose sai junto do canal",
				  PoseNoFio(seco) == Protocol.Pose.Normal, $"{PoseNoFio(seco)}");

		// ---------------------------------------------------------------
		// SAIDA 3 -- ALGUEM BATEU NELE (a que faltava no port inteiro)
		// ---------------------------------------------------------------
		ServerPlayer levou = Forjar("LevouPorrada", CorredorLivre(28), bp: 50_000);
		levou.Facing = Facing.East;
		levou.Ficha.Ki = levou.Ficha.MaxKi;
		Canalizar(levou, "Ki_Wave", 10 * levou.Ficha.BaseDrain(), receita);
		for (int i = 0; i < 300 && _canais.GetValueOrDefault(levou.Id)?.Raio == null; i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
		}
		AfirmarPj("SAIDA 3, antes: ele esta atirando",
				  PoseNoFio(levou) == Protocol.Pose.Canalizando && EnraizadoPorKi(levou.Id));

		// O ARREMESSO E O `KB` DO DM -- os dois escritores de la (`Throw.dm:84` e o
		// `/effect/knockback`) sao este metodo aqui. Chamado pelo funil de producao e nao pelo
		// `DerrubarRaioPorGolpe` na mao: provar que a funcao funciona sem provar que alguem a chama
		// e o defeito que esta bancada existe pra nao repetir.
		Arremessar(levou, new Vec2(-1, 0), forca: 1_000_000, tiques: 5);
		AfirmarPj("SAIDA 3 (apanhou): o arremesso QUEBRA o canal -- o `if(KB) stopbeaming()`",
				  !EnraizadoPorKi(levou.Id));
		AfirmarPj("...e a pose volta ao idle com ele (ele esta voando, nao canalizando)",
				  PoseNoFio(levou) != Protocol.Pose.Canalizando, $"{PoseNoFio(levou)}");

		// ---------------------------------------------------------------
		// O NPC E UM CORPO -- sem `if` de IA em lugar nenhum
		// ---------------------------------------------------------------
		ServerPlayer npc = Forjar("NpcAtirador", CorredorLivre(28), bp: 50_000);
		npc.Facing = Facing.East;
		npc.Ficha.Ki = npc.Ficha.MaxKi;
		npc.Cerebro = new Jandirus.Core.Ai.Cerebro();
		Canalizar(npc, "Ki_Wave", 10 * npc.Ficha.BaseDrain(), receita);
		AfirmarPj("o NPC canaliza pela MESMA porta e ganha a MESMA pose",
				  PoseNoFio(npc) == Protocol.Pose.Canalizando, $"{PoseNoFio(npc)}");
		DerrubarRaioPorGolpe(npc.Id);
		AfirmarPj("...e a solta pela mesma porta tambem", PoseNoFio(npc) == Protocol.Pose.Normal);

		// ---------------------------------------------------------------
		// O FIO: o byte opcional so existe com a pose de canal
		// ---------------------------------------------------------------
		// IDA E VOLTA DE VERDADE (`Write` -> `Read`), porque o custo desta funcionalidade e uma
		// PROMESSA de encanamento: "zero byte pra quem nao esta atirando". Uma leitura que buscasse
		// o byte sem ele ter sido escrito nao daria erro -- daria o campo SEGUINTE lido como canal,
		// e o defeito apareceria como corpos aleatorios em pose de raio.
		int antes = MedirBytes(new EntityState { Id = 1, Pose = Protocol.Pose.Normal });
		var comCanal = new EntityState { Id = 1, Pose = Protocol.Pose.Canalizando };
		comCanal.CanalAtirando = true;
		comCanal.CargaDoCanal = 7;
		AfirmarPj("no fio, quem NAO canaliza nao paga o byte do canal",
				  MedirBytes(comCanal) == antes + 1, $"{antes} -> {MedirBytes(comCanal)} bytes");

		var w = new LiteNetLib.Utils.NetDataWriter();
		comCanal.Write(w);
		EntityState volta = EntityState.Read(new LiteNetLib.Utils.NetDataReader(w.Data, 0, w.Length));
		AfirmarPj("...e ele volta INTEIRO do outro lado (fase + desenho da carga)",
				  volta.Pose == Protocol.Pose.Canalizando && volta.CanalAtirando
				  && volta.CargaDoCanal == 7,
				  $"{volta.Pose}/atirando={volta.CanalAtirando}/carga={volta.CargaDoCanal}");
	}

	// =====================================================================
	// 4c) AS TRES SAIDAS, COM O DEFEITO INJETADO
	// =====================================================================
	/// <summary>
	/// UMA LINHA POR SAIDA, E CADA UMA COM O JOGO POSTO DE VOLTA NO ESTADO DE ANTES DO CONSERTO.
	///
	/// ============================ POR QUE A FAMILIA 4b NAO BASTA ============================
	/// A 4b prova que as tres saidas FUNCIONAM. Ela nao prova que a bancada saberia RECLAMAR se elas
	/// parassem de funcionar -- e uma bancada que nao sabe reprovar e um enfeite verde. Esta casa ja
	/// catalogou o cego ("a bancada mede INTENCAO": 4000 checagens verdes e 4 bugs visuais na tela).
	///
	/// Entao aqui cada saida e remontada com o defeito REAL que ela teria, e a linha passa quando a
	/// regra correspondente fica VERMELHA. Nao ha ruido aleatorio: ruido derruba qualquer regra e nao
	/// prova nada sobre nenhuma. Os tres defeitos abaixo tem endereco no codigo de producao, e dois
	/// deles sao desenhos que esta funcionalidade EXAMINOU E RECUSOU -- ficam aqui como a prova de
	/// que a recusa tinha motivo.
	/// ====================================================================================
	/// </summary>
	private void AsTresSaidasComDefeitoInjetado()
	{
		GD.Print("[projetil] -- 4c) AS TRES SAIDAS, COM O DEFEITO INJETADO");
		LimparTudoDaBancada();

		var receita = new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam, BaseDano = 1, Velocidade = 1,
			AlcanceTiles = 30, CargaMinima = 1, Nome = "Onda de Ki",
		};

		Protocol.Pose PoseNoFio(ServerPlayer p) => EstadoDe(p, NowMs()).Pose;

		/// Abre um canal e roda o tique ate o feixe nascer. Devolve o corpo ja atirando.
		ServerPlayer Atirando(string nome)
		{
			ServerPlayer p = Forjar(nome, CorredorLivre(28), bp: 50_000);
			p.Facing = Facing.East;
			p.Ficha.Ki = p.Ficha.MaxKi;
			Canalizar(p, "Ki_Wave", 10 * p.Ficha.BaseDrain(), receita);
			for (int i = 0; i < 300 && _canais.GetValueOrDefault(p.Id)?.Raio == null; i++)
			{
				TickDosCanaisDeKi(Protocol.TickSeconds);
				TickDosProjeteis(Protocol.TickSeconds);
			}
			return p;
		}

		// ---------------------------------------------------------------
		// SAIDA 1 -- o defeito: UM BIT GUARDADO QUE NINGUEM APAGOU
		// ---------------------------------------------------------------
		// E o `bool SoltandoRaio` no `ServerPlayer` que a fase 0 desta funcionalidade recomendou e que
		// foi recusado. O `Pose(agoraMs, canalDeKi)` recebe a resposta por PARAMETRO justamente pra
		// que nao exista um bit assim -- e injeta-lo aqui e passar `true` depois de o canal ter
		// fechado, que e exatamente o que um campo esquecido produziria.
		//
		// Esta casa perdeu tres vezes numa semana com essa forma (a aureola presa no cadaver, o
		// `MsNoAlem` sem consumidor, o relogio da morte que nao rearmava). A linha existe pra que a
		// quarta seja pega aqui.
		ServerPlayer a = Atirando("InjecaoVontade");
		AfirmarPj("(controle 1) o raio esta de pe e a pose e a do canal",
				  PoseNoFio(a) == Protocol.Pose.Canalizando, $"{PoseNoFio(a)}");
		SoltarCanal(a, "Ki_Wave");
		AfirmarPj("(controle 1) soltou: a pose volta ao idle",
				  PoseNoFio(a) == Protocol.Pose.Normal, $"{PoseNoFio(a)}");
		AfirmarPj("[injecao] SAIDA 1 com um BIT GUARDADO (o `SoltandoRaio` recusado) -> \"volta ao idle\" "
				+ "fica VERMELHA: a pose sobrevive ao canal",
				  a.Pose(NowMs(), canalDeKi: true) == Protocol.Pose.Canalizando,
				  $"{a.Pose(NowMs(), canalDeKi: true)}");

		// ---------------------------------------------------------------
		// SAIDA 2 -- o defeito: A POSE DERIVADA DO PROJETIL
		// ---------------------------------------------------------------
		// O outro desenho recusado: perguntar "este corpo tem um feixe vivo?" em vez de "tem um
		// canal?". Parece equivalente e nao e -- o `FecharCanal` **nao mata o raio** (`beams.dm`: quem
		// para de gerar segmentos deixa o rastro se esvaziar andando). A prova esta abaixo, medida no
		// jogo: o Ki acaba, o canal cai, e o feixe CONTINUA vivo no ar. Uma pose derivada dele ficaria
		// pendurada DEPOIS de o dono ter parado, que e o defeito do pedido ao contrario.
		ServerPlayer b = Atirando("InjecaoSemKi");
		Projetil? feixe = _canais.GetValueOrDefault(b.Id)?.Raio;
		AfirmarPj("(controle 2) o feixe da saida 2 nasceu", feixe is { Vivo: true });

		b.Ficha.Ki = 0;
		for (int i = 0; i < 30 && _canais.ContainsKey(b.Id); i++) TickDosCanaisDeKi(Protocol.TickSeconds);
		AfirmarPj("(controle 2) o Ki acabou: a pose sai junto do canal",
				  PoseNoFio(b) == Protocol.Pose.Normal, $"{PoseNoFio(b)}");
		AfirmarPj("[injecao] SAIDA 2 derivada do PROJETIL -> \"a pose sai junto do canal\" fica VERMELHA: "
				+ "o feixe continua VIVO depois de o canal cair",
				  feixe is { Vivo: true, Canalizando: false },
				  $"vivo={feixe?.Vivo}, canalizando={feixe?.Canalizando}");

		// ---------------------------------------------------------------
		// SAIDA 3 -- o defeito: O ARREMESSO SEM O `DerrubarRaioPorGolpe`
		// ---------------------------------------------------------------
		// Este nao e um desenho recusado: e o PORT INTEIRO ate esta rodada. O `if(KB) stopbeaming()`
		// (`beams.dm:73-74`) nao tinha chamador nenhum, e bater em alguem nao derrubava o raio dele.
		//
		// A injecao copia o `Arremessar` SEM aquela linha -- os mesmos cinco campos de voo, escritos
		// na mao -- e cobra que o canal SOBREVIVA. Depois a MESMA cena roda pelo `Arremessar` de
		// verdade, e ai o canal cai. E o par que prova que a linha e o que resolve, e nao o acaso.
		ServerPlayer c = Atirando("InjecaoGolpe");
		AfirmarPj("(controle 3) o raio da saida 3 esta de pe", EnraizadoPorKi(c.Id));

		c.ArrastoRestante = 0;
		c.TiquesDeVoo = c.TiquesIniciaisDoVoo = 5;
		c.RumoDoVoo = new Vec2(-1, 0);
		c.ForcaDoVoo = 1_000_000;
		c.VooNoTique = 0;
		for (int i = 0; i < 30; i++) TickDosCanaisDeKi(Protocol.TickSeconds);
		AfirmarPj("[injecao] SAIDA 3 com o `Arremessar` SEM o `DerrubarRaioPorGolpe` (o port de ontem) -> "
				+ "\"o arremesso quebra o canal\" fica VERMELHA: o corpo voa COM o raio na mao",
				  EnraizadoPorKi(c.Id) && PoseNoFio(c) == Protocol.Pose.Canalizando,
				  $"enraizado={EnraizadoPorKi(c.Id)}, pose={PoseNoFio(c)}");

		Arremessar(c, new Vec2(-1, 0), forca: 1_000_000, tiques: 5);
		AfirmarPj("...e a MESMA cena pelo `Arremessar` DE VERDADE derruba o canal (a linha e o que resolve)",
				  !EnraizadoPorKi(c.Id) && PoseNoFio(c) != Protocol.Pose.Canalizando,
				  $"enraizado={EnraizadoPorKi(c.Id)}, pose={PoseNoFio(c)}");

		LimparTudoDaBancada();
	}

	/// <summary>Quantos bytes este estado ocupa no fio. So a bancada do canal usa.</summary>
	private static int MedirBytes(EntityState e)
	{
		var w = new LiteNetLib.Utils.NetDataWriter();
		e.Write(w);
		return w.Length;
	}

	// =====================================================================
	// 5) O CENARIO
	// =====================================================================
	/// <summary>
	/// O tiro para no que bloqueia -- e quem voa alto passa por cima, pela MESMA regra que ja
	/// decide o passo de um corpo (`Voo.AtravessaCenario`).
	/// </summary>
	private void OCenarioParaOTiroEAAlturaNao()
	{
		GD.Print("[projetil] -- 5) O CENARIO PARA O TIRO, E A ALTURA O DEIXA PASSAR");

		ZoneCollision? mapa = _pjMapa;
		if (mapa == null) return;   // ja reprovado la em cima, na abertura da bancada

		// ACHA UMA PAREDE DE VERDADE no mapa da Terra, com chao livre a oeste dela. Um ponto
		// escrito a mao envelheceria calado no dia em que o mapa mudasse.
		if (!AcharParede(mapa, out int cx, out int cy))
		{
			AfirmarPj("achei uma parede com chao livre na frente", false, "varredura nao achou");
			return;
		}

		var chao = new Vec2((cx - 4) * ZoneCollision.TileSize + 16, cy * ZoneCollision.TileSize + 16);
		ServerPlayer pl = Forjar("Muralha", chao, bp: 5_000);
		pl.Facing = Facing.East;

		Projetil rasteiro = Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, Velocidade = 5, AlcanceTiles = 30,
		});
		for (int i = 0; i < 300 && rasteiro.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
		AfirmarPj("o tiro rasteiro PARA na parede", rasteiro.Fim == FimDeProjetil.Cenario,
				  $"fim = {rasteiro.Fim}");

		// AGORA DO ALTO. Mesma parede, mesmo rumo -- muda so a altitude do atirador.
		pl.Altitude = Voo.AlturaQueAtravessa + 1;
		Projetil alto = Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, Velocidade = 5, AlcanceTiles = 30,
		});
		for (int i = 0; i < 300 && alto.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
		AfirmarPj("...e o disparado do ALTO passa por cima dela",
				  alto.Fim != FimDeProjetil.Cenario, $"fim = {alto.Fim}");
		pl.Altitude = 0;
	}

	/// <summary>Uma celula bloqueada com quatro celulas livres a oeste dela. Deterministico.</summary>
	/// <param name="livresAtras">
	/// QUANTAS celulas livres a familia precisa a oeste da parede. Era fixo em quatro, que basta pra
	/// a familia 5 (tiro que so precisa de espaco pra sair). A familia 9 precisa de mais: o arrasto so
	/// comeca alem de 4 tiles do atirador (`FatorDeEmpurrao`), entao um corredor de quatro nunca
	/// produziria a cena. Continua UMA varredura -- duas fariam duas ideias de "parede utilizavel".
	/// </param>
	private static bool AcharParede(ZoneCollision mapa, out int cx, out int cy, int livresAtras = 4)
	{
		for (int y = 8; y < 240; y++)
			for (int x = 12; x < 240; x++)
			{
				if (!mapa.BlockedCell(x, y)) continue;
				bool livre = true;
				for (int d = 1; d <= livresAtras; d++)
					livre &= !mapa.BlockedCell(x - d, y) && !mapa.EhAgua(x - d, y);
				if (!livre) continue;
				cx = x; cy = y;
				return true;
			}
		cx = cy = 0;
		return false;
	}

	// =====================================================================
	// 6) O TETO -- ele DISPARA
	// =====================================================================
	/// <summary>
	/// *"Um teto que nunca e atingido e indistinguivel de teto nenhum."* Entao a bancada ENCHE a
	/// zona e afirma as duas coisas que fazem um teto ser um teto: que ele para de aceitar, e que
	/// ele diz por que.
	/// </summary>
	private void OTetoDispara()
	{
		GD.Print("[projetil] -- 6) O TETO DE TIROS POR ZONA DISPARA");

		LimparTudoDaBancada();
		ServerPlayer pl = Forjar("Metralha", CorredorLivre(4), bp: 5_000);
		pl.Ficha.Ki = 1e12;   // o teto que se mede aqui e o de TIROS, nao o de energia

		int aceitos = 0;
		for (int i = 0; i < MaxProjeteisPorZona + 64; i++)
		{
			Projetil p = Disparar(pl, new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast });
			p.VidaRestante = 9999;   // ninguem pode morrer de velhice no meio da contagem
			if (p.Vivo) aceitos++;
		}

		AfirmarPj($"a zona aceitou exatamente {MaxProjeteisPorZona} tiros e nem um a mais",
				  aceitos == MaxProjeteisPorZona, $"aceitou {aceitos}");
		AfirmarPj("...e a lista da zona parou no teto",
				  ProjeteisDaZona(pl.Zone.Hash).Count == MaxProjeteisPorZona);

		// E A RECUSA FALA. `PodeAtirar` e a porta que o jogador de verdade atravessa -- sem esta
		// linha o teto existiria so no `Disparar`, e o jogador veria o botao nao fazer nada.
		bool recusou = !PodeAtirar(pl, 0, out string porque);
		AfirmarPj("...e a porta do jogador RECUSA com motivo", recusou && porque.Length > 0, porque);

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 7) O CUSTO, MEDIDO
	// =====================================================================
	/// <summary>
	/// QUANTO CUSTA O TIQUE COM A ZONA LOTADA -- e a afirmacao de que ele cabe.
	///
	/// ============================ POR QUE MEDIR COM CORPOS DENTRO ============================
	/// O custo por tiro e O(corpos da zona): cada sub-passo da cabeca varre a lista da zona. Medir
	/// com a zona vazia mediria o caso barato e diria "cabe" sobre um numero que ninguem vive. Entao
	/// a medicao poe 12 corpos -- mais gente do que este jogo ja teve num pedaco de planeta -- e o
	/// teto de tiros junto.
	///
	/// O ORCAMENTO e o tique do servidor: 1/30 s = 33,3 ms, e dentro dele ainda correm combate,
	/// forma, carga, voo, IA e o snapshot. Esta bancada exige que os projeteis fiquem abaixo de um
	/// DECIMO disso -- se um dia passarem, o numero cai aqui e o teto desce junto.
	/// ====================================================================================
	/// </summary>
	private void OCustoDoTique()
	{
		GD.Print("[projetil] -- 7) O CUSTO DO TIQUE, MEDIDO");

		LimparTudoDaBancada();

		Vec2 praca = CorredorLivre(8);
		var corpos = new List<ServerPlayer>();
		for (int i = 0; i < 12; i++)
			corpos.Add(Forjar($"Multidao{i}", new Vec2(praca.X + i * 7, praca.Y + i * 5), bp: 5_000));

		ServerPlayer pl = corpos[0];
		pl.Ficha.Ki = 1e12;

		foreach (int quantos in new[] { 16, 64, MaxProjeteisPorZona })
		{
			LimparTudoDaBancada(manter: corpos);

			// ============================ A MEDICAO SO VALE COM O TETO CHEIO ============================
			// A primeira versao media 186 tiros e dizia "com o teto (256)". Eles morriam no caminho --
			// no cenario da Terra e nos proprios 12 corpos --, ou seja, o numero impresso era de uma
			// zona MENOS carregada do que a que ele afirmava. Um teto medido pela metade e a mesma
			// armadilha do teto que nunca dispara.
			//
			// Entao os tiros saem NO AR (atravessam cenario, pela regra que a familia 5 prova), com
			// alcance gigante e espalhados numa grade que nao encosta na multidao. O que se mede
			// continua sendo o caminho de producao inteiro -- inclusive a varredura de corpos, que e o
			// termo caro: cada cabeca compara com os 12 a cada sub-passo.
			// ==========================================================================================
			pl.Altitude = Voo.AlturaQueAtravessa + 1;
			for (int i = 0; i < quantos; i++)
			{
				Projetil p = Disparar(pl, new ReceitaDeProjetil
				{
					Tipo = TipoDeProjetil.Blast, Velocidade = 5, AlcanceTiles = 100_000,
				});
				p.VidaRestante = 9999;
				p.Pos = new Vec2(praca.X + 400 + i % 16 * 96, praca.Y + 400 + i / 16 * 96);
				p.Rumo = new Vec2(MathF.Cos(i * 0.7f), MathF.Sin(i * 0.7f)).Normalized();
			}
			pl.Altitude = 0;

			// AQUECE antes de cronometrar: a primeira passada paga JIT e a leitura do mapa da zona,
			// e medir isso seria medir o compilador.
			for (int i = 0; i < 30; i++) TickDosProjeteis(Protocol.TickSeconds);

			int vivos = ProjeteisDaZona(pl.Zone.Hash).Count;
			ulong t0 = Time.GetTicksUsec();
			const int amostras = 300;
			for (int i = 0; i < amostras; i++) TickDosProjeteis(Protocol.TickSeconds);
			double us = (Time.GetTicksUsec() - t0) / (double)amostras;

			GD.Print($"[projetil]      {vivos,4} tiros x {corpos.Count} corpos"
					 + $" -> {us,7:0.0} us/tique  ({us / (Protocol.TickSeconds * 1e6) * 100:0.0}% do tique)");

			if (quantos != MaxProjeteisPorZona) continue;

			double orcamento = Protocol.TickSeconds * 1e6 / 10;   // um decimo do tique de 33,3 ms
			// A ORDEM DAS DUAS AFIRMACOES E DE PROPOSITO: primeiro prova-se que a zona estava CHEIA,
			// e so entao o tempo medido significa alguma coisa.
			AfirmarPj("a medicao rodou com o teto CHEIO, e nao com uma zona que se esvaziou",
					  vivos == MaxProjeteisPorZona, $"restaram {vivos} de {MaxProjeteisPorZona}");
			AfirmarPj($"com o teto ({MaxProjeteisPorZona} tiros) o tique cabe em 1/10 do orcamento",
					  us < orcamento, $"{us:0.0} us contra {orcamento:0.0} us");
		}

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 8) O RASTRO NO CHAO
	// =====================================================================
	/// <summary>
	/// O RAIO ARA O CHAO POR ONDE PASSA -- e o que se le NAO e a chamada, e o FIO.
	///
	/// ============================ POR QUE A ESCUTA, E NAO UM CONTADOR ============================
	/// Um decalque termina num `Peer.Send`, e pacote que saiu nao volta. Contar chamadas mediria a
	/// INTENCAO: o `MarcarSulcoDoTiro` pode ser chamado 300 vezes e a guarda de celula recusar todas,
	/// e pode ser chamado com a marca caindo no pixel errado. A `EscutaDeDecalques` guarda os BYTES
	/// que sairiam pro cliente, entao daqui se responde onde a marca caiu, com que direcao, e -- o
	/// que mais importa -- QUANTAS sairam.
	///
	/// E os tres contra-exemplos valem mais que a afirmacao positiva: rastro que sai SEMPRE e o modo
	/// classico deste efeito quebrar, e ele fica verde em qualquer teste que so pergunte "saiu?".
	/// ============================================================================================
	/// </summary>
	private void ORaioAraOChaoPorOndePassa()
	{
		GD.Print("[projetil] -- 8) O RAIO ARA O CHAO POR ONDE PASSA");
		LimparTudoDaBancada();

		// ============================ "LIVRE" NAO QUER DIZER "SECO" ============================
		// O `CorredorLivre` procura celulas que nao BLOQUEIAM -- e agua nao bloqueia (ela e a terceira
		// classe de celula, num plano proprio). A Terra e 44% agua, entao o corredor que ele acha e,
		// com boa chance, um LAGO. Esta familia media o rastro sobre agua sem saber, e foi a guarda
		// nova (`na agua nao se ara`) que revelou isso: as marcas sumiram todas.
		//
		// Aqui o corredor precisa ser seco, e a diferenca esta na varredura, nao numa correcao de
		// posicao a mao. O `CorredorLivre` continua como esta -- as outras seis familias nao ligam pra
		// molhado.
		// =====================================================================================
		Vec2 raia = CorredorSeco(28);
		ServerPlayer pl = Forjar("Arador", raia, bp: 50_000);
		pl.Facing = Facing.East;

		List<MarcaDeSulco> sulcos = SulcosDeUmTiro(pl, TipoDeProjetil.Beam);

		AfirmarPj("o raio no chao deixou uma fileira de sulcos", sulcos.Count >= 5,
				  $"{sulcos.Count} marcas");
		if (sulcos.Count == 0) { LimparTudoDaBancada(); return; }

		// ---------- AS CINCO REGRAS, PELA TABELA ----------
		// Elas nao estao escritas aqui dentro, e isso e o que torna a familia 10 possivel: la o MESMO
		// predicado roda em cima do MESMO rastro com um defeito plantado dentro. Detector escrito duas
		// vezes concorda consigo mesmo -- ver <see cref="RegrasDoSulco"/>.
		foreach (RegraDoSulco r in RegrasDoSulco)
			AfirmarPj(r.Nome, r.Vale(sulcos, Facing.East), r.Relato(sulcos));

		// ---------- E NAO ARA ONDE NAO PASSOU ----------
		// As cinco de cima dizem que TODA marca esta na fileira; estas duas dizem que a fileira ACABA.
		// Sem elas, um rastro que continuasse depois do tiro morto -- ou que nascesse atras do
		// atirador, do outro lado do corpo dele -- passaria em todas as cinco.
		const int T = ZoneCollision.TileSize;
		int atras = sulcos.Count(s => s.Onde.X < pl.Pos.X - T);
		AfirmarPj("...e ATRAS do atirador nao ha marca nenhuma: o rastro comeca onde o tiro comeca",
				  atras == 0, $"{atras} marca(s) atras de {pl.Pos.X:0}");

		// O ALCANCE DA RECEITA (`SulcosDeUmTiro` dispara com 12 tiles) e o mesmo teto do fio: uma marca
		// por TILE ANDADO, e nao uma por sub-passo. Sao ~4 sub-passos por tique no raio mais rapido, e
		// e essa a diferenca entre 13 pacotes e 50 -- o teto do lado do servidor. O do cliente (a fila
		// de 120 decalques) e medido na `--diagdecalque`.
		const int AlcanceDaReceita = 12;
		AfirmarPj("...e o raio LONGO nao entulha o fio: uma marca por TILE andado, nao uma por sub-passo",
				  sulcos.Count <= AlcanceDaReceita + 2,
				  $"{sulcos.Count} marcas para {AlcanceDaReceita} tiles de alcance");

		// ---------- CONTRA-EXEMPLO 1: A BOLA NAO ARA ----------
		// Ela voa e estoura; o campo minado da `Ki_Bomb` (sete bolas paradas) pintaria sete manchas
		// de terra sem nada ter acontecido. Ver `RastroDoTiroVale`.
		AfirmarPj("a BOLA, no mesmo chao, nao ara nada",
				  SulcosDeUmTiro(pl, TipoDeProjetil.Blast).Count == 0);

		// ---------- CONTRA-EXEMPLO 2: DO ALTO NAO ARA ----------
		pl.Altitude = Jandirus.Core.World.Voo.AlturaQueAtravessa + 1;
		AfirmarPj("o raio disparado do ALTO passa por cima sem riscar o chao",
				  SulcosDeUmTiro(pl, TipoDeProjetil.Beam).Count == 0);
		pl.Altitude = 0;

		// ---------- CONTRA-EXEMPLO 3: NO VACUO NAO HA CHAO ----------
		// ============================ ESTA CONTA JA FOI PAGA CARO UMA VEZ ============================
		// O arremesso carimbava sulco e cratera NO ESPACO, porque `Altitude` e sempre zero la (a
		// altitude e um sistema de planeta) -- e o pacote ia pra `ZoneList` do espaco, que e o
		// universo INTEIRO: todo mundo online via a marca, a qualquer distancia. O raio herdaria o
		// mesmo defeito de graca se `RastroDoTiroVale` nao perguntasse `Espaco.EhPlaneta`.
		// ============================================================================================
		ZoneList(pl.Zone.Hash).Remove(pl);
		ZoneKey daTerra = pl.Zone;
		pl.Zone = ZonaDoEspaco;
		ZoneList(pl.Zone.Hash).Add(pl);

		AfirmarPj("e no ESPACO ele nao carimba terra batida no nada",
				  SulcosDeUmTiro(pl, TipoDeProjetil.Beam).Count == 0);

		List<Projetil> noVacuo = ProjeteisDaZona(pl.Zone.Hash);
		_projeteisVivos -= noVacuo.Count;
		noVacuo.Clear();
		ZoneList(pl.Zone.Hash).Remove(pl);
		pl.Zone = daTerra;
		ZoneList(pl.Zone.Hash).Add(pl);

		ONaAguaNaoSeAra(pl);
		OCustoDoRastro(pl);
		LimparTudoDaBancada();
	}

	/// <summary>
	/// A AGUA GANHA ONDA, NAO SULCO -- e a celula molhada nao pode receber as duas marcas.
	///
	/// O rastro de terra e do servidor; a onda da agua e do cliente (ele ve o tiro cruzar e nao
	/// precisa de byte nenhum pra isso). Sem a guarda de agua no servidor, uma celula de lago
	/// receberia a onda E uma cratera de terra boiando em cima dela -- as duas ao mesmo tempo, e
	/// nenhuma das duas pontas saberia da outra.
	///
	/// A afirmacao e feita SOBRE O FIO e nas duas metades: sai marca (o pedaco de terra do caminho) e
	/// NENHUMA delas cai em celula de agua.
	/// </summary>
	private void ONaAguaNaoSeAra(ServerPlayer pl)
	{
		ZoneCollision? mapa = _pjMapa;
		if (mapa is not { TemAgua: true })
		{
			AfirmarPj("a zona da bancada tem agua marcada pra medir o rastro sobre lago",
					  false, "sem plano de agua no `.col` -- a guarda de agua ficou sem teste");
			return;
		}

		// UMA MARGEM DE LAGO DE VERDADE: seis celulas secas e livres a oeste de uma molhada. O tiro
		// sai do seco e entra na agua, entao o mesmo disparo cobre os dois casos.
		if (!AcharMargem(mapa, out int cx, out int cy))
		{
			AfirmarPj("achei uma margem de lago no mapa da bancada", false, "varredura nao achou");
			return;
		}

		const int T = ZoneCollision.TileSize;
		ZoneList(pl.Zone.Hash).Remove(pl);
		pl.Pos = new Vec2((cx - 5) * T + T / 2f, cy * T + T / 2f);
		ZoneList(pl.Zone.Hash).Add(pl);
		pl.Facing = Facing.East;

		List<MarcaDeSulco> sulcos = SulcosDeUmTiro(pl, TipoDeProjetil.Beam);
		int naAgua = sulcos.Count(s => mapa.EhAguaEm(s.Onde));

		AfirmarPj("atravessando a margem do lago o raio ainda ara a parte SECA", sulcos.Count > 0,
				  $"{sulcos.Count} marcas");
		AfirmarPj("...e NENHUMA marca de terra caiu dentro da agua (la quem marca e a onda)",
				  naAgua == 0, $"{naAgua} de {sulcos.Count} marcas sobre agua");
	}

	/// <summary>
	/// Um corredor horizontal livre E SECO de <paramref name="tiles"/> celulas. Mesma varredura do
	/// <see cref="CorredorLivre"/> com uma pergunta a mais -- ver o bloco na familia 8.
	/// </summary>
	private Vec2 CorredorSeco(int tiles)
	{
		ZoneCollision? mapa = _pjMapa;
		if (mapa == null) return CorredorLivre(tiles);

		for (int y = _pjProximoCorredor; y < mapa.Height - 8; y++)
			for (int x = 4; x + tiles < mapa.Width - 4; x++)
			{
				bool bom = true;
				for (int d = 0; d <= tiles && bom; d++)
					bom &= !mapa.BlockedCell(x + d, y) && !mapa.EhAgua(x + d, y);
				if (!bom) continue;

				_pjProximoCorredor = y + 3;
				return new Vec2(x * ZoneCollision.TileSize + 16, y * ZoneCollision.TileSize + 16);
			}

		AfirmarPj($"achei um corredor SECO de {tiles} tiles no mapa da bancada", false, "varredura falhou");
		return CorredorLivre(tiles);
	}

	/// <summary>Uma celula de AGUA com cinco celulas secas e livres a oeste dela. Deterministico.</summary>
	/// <param name="secosAtras">Quantas celulas secas e livres a oeste da agua -- ver `AcharParede`.</param>
	private static bool AcharMargem(ZoneCollision mapa, out int cx, out int cy, int secosAtras = 5)
	{
		for (int y = 8; y < mapa.Height - 8; y++)
			for (int x = 12; x < mapa.Width - 12; x++)
			{
				if (!mapa.EhAgua(x, y)) continue;
				bool secoAtras = true;
				for (int d = 1; d <= secosAtras; d++)
					secoAtras &= !mapa.BlockedCell(x - d, y) && !mapa.EhAgua(x - d, y);
				if (!secoAtras) continue;
				cx = x; cy = y;
				return true;
			}
		cx = cy = 0;
		return false;
	}

	/// <summary>
	/// QUANTO CUSTA ARAR O CHAO -- medido por SUB-PASSO, e o teto do tique montado em cima disso.
	///
	/// ============================ POR QUE NAO DA PRA MEDIR "256 RAIOS RASTEIROS" ============================
	/// Foi a primeira versao desta medicao, e ela reprovou a si mesma com uma linha honesta: `0 raios
	/// RASTEIROS`. O motivo e uma regra do proprio jogo, e ela e a MESMA nos dois lados: `Altitude` e o
	/// campo que faz o tiro atravessar cenario (`Voo.AtravessaCenario`) E o que libera o rastro
	/// (`RastroDoTiroVale`). Um raio no chao, na Terra, morre numa pedra em poucos tiles -- e nao ha
	/// como manter 256 deles vivos rasteiros sem desligar a colisao, que e justamente o codigo que a
	/// familia 5 prova. A familia 7 dispara todos ACIMA do limiar por essa razao.
	///
	/// Entao o que se mede aqui e o SUB-PASSO: a chamada que o laco do avanco faz a cada meio tile, com
	/// a zona povoada (a marca varre a `ZoneList` pra mandar o pacote). Metade das chamadas carimba e
	/// metade e recusada pela guarda de celula, que e a mistura do jogo. O TETO do tique sai de
	/// aritmetica do proprio sistema, e nao de chute: no maximo `MaxProjeteisPorZona` tiros na zona, e
	/// no maximo 7 sub-passos por tiro por tique (o raio mais rapido do jogo anda ~107 px por tique e o
	/// sub-passo e de 16 px).
	///
	/// O QUE O NUMERO NAO COBRE: o `Peer.Send` de verdade -- os corpos da bancada nao tem peer. Cobre o
	/// que o servidor gasta pensando: a guarda, o alinhamento, a montagem do pacote e a varredura da
	/// zona. O custo de FIO esta na conta de bytes do relato, e nao aqui.
	/// ========================================================================================================
	/// </summary>
	private void OCustoDoRastro(ServerPlayer pl)
	{
		LimparTudoDaBancada(manter: [pl]);

		// A ZONA POVOADA, porque cada marca varre a `ZoneList` inteira -- medir com um corpo so
		// mediria o caso barato. Doze e mais gente do que este jogo ja teve num pedaco de planeta.
		for (int i = 0; i < 11; i++) Forjar($"Plateia{i}", pl.Pos + new Vec2(0, 8 * (i + 1)), bp: 5_000);

		// UM RAIO DE VERDADE, andando na mao: o que se cronometra e a MESMA chamada que o laco do
		// avanco faz, com o mesmo objeto e a mesma guarda de celula.
		// EM CHAO SECO: a guarda de agua recusa a marca sobre lago, e medir dentro de um seria medir a
		// recusa. Ver o bloco da familia 8 sobre "livre nao quer dizer seco".
		var raio = new Projetil
		{
			Tipo = TipoDeProjetil.Beam, Pos = CorredorSeco(8), Cauda = pl.Pos,
			Rumo = new Vec2(1, 0), Altitude = 0,
		};

		// A MESMA RESPOSTA QUE O TIQUE CALCULA -- e por isso ela e calculada aqui tambem, e nao
		// escrita como `true`: se um dia a Terra deixar de ser planeta, a bancada mede o que o jogo faz.
		bool temChao = Espaco.EhPlaneta(pl.Zone);

		const int passos = 40_000;
		for (int i = 0; i < 200; i++)   // aquece: JIT e o primeiro `Espaco.PreFeitos`
		{
			raio.Pos = new Vec2(raio.Pos.X + Projetil.RaioDeImpacto, raio.Pos.Y);
			MarcarSulcoDoTiro(pl.Zone, raio, temChao, _pjMapa);
		}

		// A ESCUTA FICA DESLIGADA NA CRONOMETRAGEM: ela copia os bytes de cada pacote (`CopyData`), o
		// que o jogo nao faz. Medir com ela ligada mediria a bancada.
		ulong t0 = Godot.Time.GetTicksUsec();
		for (int i = 0; i < passos; i++)
		{
			raio.Pos = new Vec2(raio.Pos.X + Projetil.RaioDeImpacto, raio.Pos.Y);
			MarcarSulcoDoTiro(pl.Zone, raio, temChao, _pjMapa);
		}
		double usPorPasso = (Godot.Time.GetTicksUsec() - t0) / (double)passos;

		// E AGORA A CONTAGEM, num punhado de passos com a escuta ligada -- pra afirmar que aqueles
		// 40 mil passos de fato CARIMBARAM, e nao foram 40 mil recusas cronometradas.
		EscutaDeDecalques = [];
		for (int i = 0; i < 64; i++)
		{
			raio.Pos = new Vec2(raio.Pos.X + Projetil.RaioDeImpacto, raio.Pos.Y);
			MarcarSulcoDoTiro(pl.Zone, raio, temChao, _pjMapa);
		}
		int marcas = (EscutaDeDecalques ?? []).Count(d => d.Tipo == Protocol.Decal.Sulco);
		EscutaDeDecalques = null;

		// O TETO, por aritmetica do sistema: teto de tiros da zona x sub-passos por tique. O tiro mais rapido do
		// jogo e o raio customizado no teto de `speed` (`TecnicaCustomizada.VelocidadeTeto`), e o sub-passo e o
		// raio de impacto. Era um "4" escrito a mao (53 px / 16 px); a conta acompanha a velocidade sozinha.
		double passoMaximo = ZoneCollision.TileSize / Projetil.AtrasoDeRaio(Jandirus.Core.Skills.TecnicaCustomizada.VelocidadeTeto) * Protocol.TickSeconds;
		int SubPassosPorTique = (int)Math.Ceiling(passoMaximo / Projetil.RaioDeImpacto);
		double pior = usPorPasso * MaxProjeteisPorZona * SubPassosPorTique;

		GD.Print($"[projetil]      rastro: {usPorPasso * 1000:0} ns por sub-passo,"
				 + $" {marcas} marcas em 64 passos"
				 + $" -> pior caso {pior:0.0} us/tique ({MaxProjeteisPorZona} raios x {SubPassosPorTique})"
				 + $"  ({pior / (Protocol.TickSeconds * 1e6) * 100:0.0}% do tique)");

		AfirmarPj("a medicao do rastro CARIMBOU de verdade (metade dos passos, uma marca por tile)",
				  marcas >= 30 && marcas <= 34, $"{marcas} marcas em 64 sub-passos");
		AfirmarPj("...e o PIOR CASO (teto de tiros x sub-passos) cabe em 1/10 do orcamento",
				  pior < Protocol.TickSeconds * 1e6 / 10, $"{pior:0.0} us");
	}

	// =====================================================================
	// AS REGRAS DO SULCO -- O DETECTOR, NUMA TABELA SO
	// =====================================================================
	/// <summary>Uma marca como ela sai NO FIO: onde caiu e com que direcao.</summary>
	private readonly record struct MarcaDeSulco(Vec2 Onde, Facing Dir);

	/// <summary>
	/// UMA AFIRMACAO SOBRE O RASTRO INTEIRO: o nome que sai no placar, o predicado e o relato.
	///
	/// ============================ POR QUE ELAS MORAM NUMA TABELA ============================
	/// A familia 8 roda estas cinco em cima do rastro DE VERDADE; a familia 10 roda AS MESMAS em cima
	/// do MESMO rastro com um defeito plantado dentro, e exige que a regra nomeada fique vermelha.
	///
	/// Isso so vale se for o mesmo predicado. Um detector escrito duas vezes -- uma pra medir e outra
	/// pra se cobrar -- concorda sempre consigo mesmo, e a injecao viraria enfeite: ela estaria
	/// provando que a COPIA sabe reprovar, e a copia nao e quem julga o jogo. E o mesmo cego que ja
	/// deixou quatro bugs visuais passarem por bancada verde nesta casa ("uniform escrito != pixel
	/// desenhado").
	/// ====================================================================================
	/// </summary>
	private readonly record struct RegraDoSulco(
		string Nome,
		Func<IReadOnlyList<MarcaDeSulco>, Facing, bool> Vale,
		Func<IReadOnlyList<MarcaDeSulco>, string> Relato);

	private static readonly RegraDoSulco[] RegrasDoSulco =
	[
		// (a regra "alinhadas ao centro da celula" caiu em 2026-09-04: o rastro nasce por onde o raio
		// PASSOU, nos pixels, como o do arremesso -- ver `CarimbarSulco`. As de baixo continuam.)
		new("...uma marca por celula, sem duas no mesmo tile",
			(s, _) => CelulasDistintas(s) == s.Count,
			s => $"{CelulasDistintas(s)} celulas para {s.Count} marcas"),

		// O DEFEITO QUE O DONO FOTOGRAFOU DUAS VEZES no rastro do arremesso ("ta vindo picotado e n
		// continuo") era exatamente isto: marcas a dois tiles uma da outra. O raio anda ate 53 px por
		// tique do servidor, entao carimbar so no fim do tique deixaria o mesmo buraco -- e por isso a
		// chamada mora DENTRO do laco de sub-passos.
		new("...e ELAS SE ENCOSTAM: cada marca a exatamente um tile da anterior",
			(s, _) => Saltos(s) == 0,
			s => $"{Saltos(s)} salto(s) de mais de um tile"),

		new("...na fileira do disparo e avancando pra frente",
			NaFileira,
			s => s.Count == 0 ? "vazio" : $"de {s[0].Onde} a {s[^1].Onde}"),

		new("...e a direcao no fio e a do TIRO (e nao a do chao ou a do dono)",
			(s, dir) => s.Count > 0 && s.All(m => m.Dir == dir),
			s => s.Count == 0 ? "vazio" : $"{s[0].Dir}"),
	];

	private static int CelulasDistintas(IReadOnlyList<MarcaDeSulco> s)
		=> s.Select(m => (m.Onde.X, m.Onde.Y)).Distinct().Count();

	private static int Saltos(IReadOnlyList<MarcaDeSulco> s)
	{
		int n = 0;
		for (int i = 1; i < s.Count; i++)
			if (Math.Abs((s[i].Onde - s[i - 1].Onde).Length - ZoneCollision.TileSize) > 0.5f) n++;
		return n;
	}

	/// <summary>
	/// TODA MARCA NA MESMA FILEIRA DO TIRO, e a fileira andando pro lado certo.
	///
	/// O eixo sai do <paramref name="dir"/> e nao esta cravado em Y: um raio pro norte tem a fileira
	/// no X, e uma regra que so soubesse ler linhas horizontais ficaria verde pra sempre no unico
	/// caso em que ela e usada hoje -- e cega no dia em que a bancada mirasse pra cima.
	/// </summary>
	private static bool NaFileira(IReadOnlyList<MarcaDeSulco> s, Facing dir)
	{
		if (s.Count == 0) return false;
		bool deitado = dir is Facing.East or Facing.West;

		float eixo = deitado ? s[0].Onde.Y : s[0].Onde.X;
		foreach (MarcaDeSulco m in s)
			if (Math.Abs((deitado ? m.Onde.Y : m.Onde.X) - eixo) > 0.5f) return false;

		float ini = deitado ? s[0].Onde.X : s[0].Onde.Y;
		float fim = deitado ? s[^1].Onde.X : s[^1].Onde.Y;
		return dir is Facing.East or Facing.South ? fim > ini : fim < ini;
	}

	/// <summary>
	/// UM TIRO INTEIRO, do disparo ao fim, com a escuta ligada -- e devolve os SULCOS que sairiam no
	/// fio, na ordem.
	///
	/// O raio nasce pelo caminho do jogador (`Canalizar` + os dois tiques), e nao por um atalho: e
	/// pela carga que o `Altitude` do tiro e copiado do dono, e o contra-exemplo do voo depende
	/// exatamente disso.
	/// </summary>
	private List<MarcaDeSulco> SulcosDeUmTiro(ServerPlayer pl, TipoDeProjetil tipo)
	{
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		var receita = new ReceitaDeProjetil
		{
			Tipo = tipo, BaseDano = 1, Velocidade = 1, AlcanceTiles = 12, CargaMinima = 1,
			Nome = "Onda de Ki",
		};

		EscutaDeDecalques = [];
		if (tipo == TipoDeProjetil.Beam) Canalizar(pl, "Ki_Wave", 10 * pl.Ficha.BaseDrain(), receita);
		else Disparar(pl, receita);

		// TETO NO LACO pra que "o tiro nunca saiu" seja falha e nao travamento -- a disciplina do
		// resto desta bancada.
		for (int i = 0; i < 400; i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
		}
		if (_canais.ContainsKey(pl.Id)) SoltarCanal(pl, "Ki_Wave");

		var saiu = new List<MarcaDeSulco>();
		foreach ((_, Protocol.Decal t, byte[] fio) in EscutaDeDecalques ?? [])
		{
			if (t != Protocol.Decal.Sulco) continue;
			(_, Vec2 onde, Facing dir, _, _) = LerDecalque(fio);
			saiu.Add(new MarcaDeSulco(onde, dir));
		}
		EscutaDeDecalques = null;

		List<Projetil> lista = ProjeteisDaZona(pl.Zone.Hash);
		_projeteisVivos -= lista.Count;
		lista.Clear();
		return saiu;
	}

	// =====================================================================
	// 9) O RAIO LEVA QUEM E MAIS FRACO QUE ELE (a escada do `KiContraCorpo`)
	// =====================================================================
	/// <summary>
	/// O ARRASTO -- *"ao ACERTAREM alguem eles deveriam EMPURRAR A PESSOA JUNTO conforme o beam vai
	/// indo"*, na regra de 2026-10-12: quem o raio leva e quem e MAIS FRACO que ele, de qualquer distancia,
	/// na velocidade que a diferenca de forca da, ate o fim do alcance -- onde o raio estoura.
	///
	/// ============================ O QUE ESTA FAMILIA TEM QUE PROVAR, E POR QUE ============================
	/// "O corpo se mexeu" e a afirmacao inutil aqui: o funil do ARREMESSO ja movia corpo, e a primeira
	/// versao imaginavel deste efeito (rearmar o `Arremessar` a cada ciclo) ficaria VERDE num teste
	/// desses -- e estaria errada, porque jogaria a vitima a 640 px/s, pra longe da cabeca que a empurra.
	/// O pedido do dono e "conforme o beam vai indo", entao a medida que decide e **a VELOCIDADE**: o corpo
	/// anda o que a cabeca andou, e a cabeca que leva alguem anda na velocidade do ARRASTO -- a do feixe
	/// vezes o `KiContraCorpo.FatorDoArrasto` do par.
	///
	/// Depois dela vem o que a escada decide, e cada degrau tem a cena dele: o IGUAL apanha e fica; de
	/// perto o raio LEVA como de longe (quem arremessa de perto e a bola); o muro PARA os dois; a agua so
	/// para quem esta a pe; o boneco largado nao vai; o feixe que entra em disputa LARGA; quem deixa de
	/// ser mais fraco no meio do caminho e SOLTO (a escada vale pro arrasto ja comecado); e no fim do
	/// alcance o raio ESTOURA em quem vinha levando, e o corpo para SECO.
	///
	/// E DUAS MEDIDAS SILENCIOSAS, as duas tiradas da vida que o corpo perde: a CADENCIA do dano (uma
	/// cabeca que anda encostada colidiria a cada sub-passo -- seis vezes o dano combinado, sem nada na
	/// tela avisando) e o PRECO de cada batida no caminho (uma fracao: o grosso e o da explosao).
	///
	/// O JOGO DE ANTES -- o arrasto que so comecava alem de quatro tiles, ia na velocidade do feixe,
	/// largava aos dez da mao e nao estourava; a queima-roupa que arremessava -- fica no fim, como o
	/// defeito injetado (<see cref="OArrastoDoJogoDeAntes"/>).
	/// ======================================================================================================
	/// </summary>
	private void ORaioLevaQuemAcerta()
	{
		GD.Print("[projetil] -- 9) O RAIO LEVA QUEM E MAIS FRACO QUE ELE, ATE O FIM DO ALCANCE");
		LimparTudoDaBancada();

		const int T = ZoneCollision.TileSize;
		// UMA FAIXA DE CHAO PRA FAMILIA INTEIRA: cada cena limpa a bancada e nasce de novo no mesmo corredor (o muro
		// e a margem do lago tem lugar proprio no mapa).
		Vec2 raia = CorredorSeco(30);

		// O RAIO DESTA CENA E O PESADO (`speed` 0,5, a do Massive Beam: 10 tiles por segundo), com 16 tiles de alcance
		// e o corpo a 6 da mao: as medidas de TAXA precisam de tempo (o arrasto inteiro dura uns dois segundos, e de
		// um ciclo so nao se tira taxa), e o alcance passa dos dez tiles em que o jogo de antes largava o corpo.
		(ServerPlayer atira, ServerPlayer vitima, Projetil raio) =
			FeixeNoMaisFraco("Levado", raia, tiles: 6, velocidade: 0.5, alcanceTiles: AlcanceDoArrastoDaBancada);
		double razao = KiContraCorpo.Razao(vitima.Ficha.expressedBP, raio.Bp);
		double fator = KiContraCorpo.FatorDoArrasto(razao, defendendo: false);
		AfirmarPj("PREPARO: o corpo tem METADE do poder do tiro -- o degrau de quem e levado -- e o raio faz 12 por batida cheia nele",
				  Math.Abs(razao - BpDoMaisFracoDaEscada / BpDoFeixeDaEscada) < 0.01 && KiContraCorpo.Arrasta(razao)
				  && Math.Abs(DanoFinalDe(raio, vitima) - DanoQueFereDoCorte) < 1e-6,
				  $"razao {razao:0.####}, dano final {DanoFinalDe(raio, vitima):0.####}");

		int t = 0;
		while (raio.Vivo && raio.Arrastando == 0 && t++ < 300) UmTiqueDeArrasto();

		AfirmarPj("o feixe PEGOU quem e mais fraco que ele, em vez de so bater e parar nele",
				  raio.Arrastando == vitima.Id, $"arrastando={raio.Arrastando} apos {t} tiques");
		if (raio.Arrastando != vitima.Id) { LimparTudoDaBancada(); return; }

		AfirmarPj("...e NAO o arremessou: o raio leva -- quem arremessa de perto e a bola",
				  vitima.TiquesDeVoo == 0);
		AfirmarPj("...sem defender, o mais fraco fica ATORDOADO enquanto apanha (`KiContraCorpo.Atordoa`)",
				  vitima.Combate!.Stun > 0, $"stun {vitima.Combate.Stun:0.##} s");
		AfirmarPj("...o corpo perde as redeas pelo funil de VETOR de sempre (`PodeMexerOCorpo`)",
				  !PodeMexerOCorpo(vitima));
		AfirmarPj("...e pelo MESMO bit que o arremesso ja acendia, e nao por um segundo bit",
				  vitima.DirigidoPeloServidor && (vitima.Sheet().Estado & 32) != 0);

		// ---------- A MEDIDA QUE DECIDE: A VELOCIDADE ----------
		const int TiquesDaVelocidade = 6;
		Vec2 corpoAntes = vitima.Pos, cabecaAntes = raio.Pos;
		for (int i = 0; i < TiquesDaVelocidade && raio.Arrastando != 0; i++) UmTiqueDeArrasto();
		float dCorpo = (vitima.Pos - corpoAntes).Length;
		float dCabeca = (raio.Pos - cabecaAntes).Length;

		AfirmarPj("o corpo anda EXATAMENTE o que a cabeca andou -- nem mais, nem menos",
				  dCabeca > 1f && MathF.Abs(dCorpo - dCabeca) < 0.5f,
				  $"corpo {dCorpo:0.0} px, cabeca {dCabeca:0.0} px");

		// OS TRES NUMEROS SAEM DE CONSTANTES, e nao de valores digitados aqui: uma copia do "640" nesta bancada
		// seria a segunda casa do numero, que e o que a regra 4 proibe.
		double vFeixe = ZoneCollision.TileSize / raio.SegundosPorTile;
		double vArrasto = dCorpo / (TiquesDaVelocidade * Protocol.TickSeconds);
		double vArremesso = Empurrao.TilesPorTique * ZoneCollision.TileSize / Empurrao.SegundosPorTique;
		AfirmarPj($"...na velocidade do ARRASTO: a do feixe vezes o fator do par (`KiContraCorpo.FatorDoArrasto`: {fator:0.##} pra quem tem metade do poder do tiro)",
				  fator > KiContraCorpo.ArrastoEntreIguais && fator < 1 && Math.Abs(vArrasto - vFeixe * fator) < vFeixe * 0.02,
				  $"{vArrasto:0.#} px/s; o feixe anda {vFeixe:0.#} e o fator e {fator:0.###} ({vFeixe * fator:0.#} px/s)");
		AfirmarPj("...que nao e a do arremesso -- por isso o funil dele nao serviu pra levar ninguem",
				  vArremesso > vArrasto * 1.5,
				  $"arrasto {vArrasto:0} px/s contra arremesso {vArremesso:0} px/s");

		// ---------- A CADENCIA DO DANO, E O PRECO DE CADA BATIDA ----------
		// ============================ NAO SE MEDE ISTO PELA FLAG ============================
		// A primeira versao contava as subidas de `Encostado` e leu ZERO em um segundo inteiro -- e o
		// codigo estava certo. `Encostado` cai e volta DENTRO do mesmo tique (o passo 4 o derruba, o
		// `Acertar` o levanta de volta no mesmo `AndarProjetil`), entao quem olha entre tiques nunca ve
		// a borda. Medir a flag e medir o observador.
		//
		// O que se conta e o EFEITO: quantos tiques do segundo tiveram perda de vida. O dano de ki so
		// entra por `Acertar`, e `Acertar` roda no maximo uma vez por tique por feixe -- entao "tiques em
		// que a vida caiu" E o numero de ciclos, e ele e observavel de fora sem hook nenhum.
		// ===================================================================================
		// E A JANELA E A QUE O EFEITO PERMITIR (um segundo, ou ate o arrasto acabar): um contador de janela
		// fixa ja leu "2 num segundo" de um arrasto de 0,4 s e reprovou um codigo certo. Mede-se a TAXA.
		int ciclos = 0, rodou = 0;
		double menorBatida = double.MaxValue, maiorBatida = 0;
		for (int i = 0; i < (int)Math.Round(1.0 / Protocol.TickSeconds) && raio.Arrastando != 0; i++)
		{
			double[] antes = VidaPorParte(vitima);
			UmTiqueDeArrasto();
			rodou++;
			double batida = FeridaMaisFunda(vitima, antes);
			if (batida <= 1e-9) continue;
			ciclos++;
			menorBatida = Math.Min(menorBatida, batida);
			maiorBatida = Math.Max(maiorBatida, batida);
		}

		double taxa = rodou > 0 ? ciclos / (rodou * Protocol.TickSeconds) : 0;
		double esperada = 1.0 / Projetil.SegundosPorCicloDeBeam;          // 5 Hz -- o ciclo deste port (o DM bate 12)
		double porSubPasso = 1.0 / Protocol.TickSeconds;                  // ~30 Hz -- o defeito que se teme
		AfirmarPj("...e mesmo ANDANDO encostado ele moi na cadencia do port (o ciclo de 0,2 s; o DM bate 12 por segundo), e nao por sub-passo",
				  rodou > 5 && taxa >= esperada * 0.6 && taxa <= esperada * 1.4,
				  $"{taxa:0.0} moidas/s em {rodou * Protocol.TickSeconds:0.00}s "
				  + $"(o port manda {esperada:0}; por sub-passo seriam ~{porSubPasso:0})");

		// ---------- O FIM: O ALCANCE INTEIRO, E O ESTOURO ----------
		int explosoes = ExplosoesDoFimDoAlcanceDeTeste, crateras = CraterasDeExplosaoDeTeste;
		double[] antesDoUltimoTique = VidaPorParte(vitima);
		while (raio.Vivo && t++ < 900)
		{
			antesDoUltimoTique = VidaPorParte(vitima);
			UmTiqueDeArrasto();
		}
		// A EXPLOSAO FERE TODO MEMBRO DE FORA POR IGUAL (`FerirComAExplosao`: o total dividido entre eles): o que cada um
		// levou, vezes quantos sao, e a explosao inteira. (O menor tombo, e nao a soma: o que escorre de um membro pro
		// filho dele viria junto na soma.)
		int membrosDeFora = vitima.Combate.Corpo.Partes.Count(m => !m.Aninhado && !m.Decepado);
		double porMembro = MenorFeridaDeFora(vitima, antesDoUltimoTique), noEstouro = porMembro * membrosDeFora;
		double corpoDaMao = (vitima.Pos.X - raia.X) / T, cabecaDaMao = (raio.Pos - raio.Cauda).Length / T;

		// OS NUMEROS MEDIDOS SAEM SEMPRE, e nao so no detalhe de uma falha -- o mesmo habito da familia 7. Uma
		// bancada que so mostra a conta quando reprova obriga a quebrar o codigo pra ler o que ele faz.
		GD.Print($"[projetil]      (medido) arrasto: corpo {dCorpo:0.0} px x cabeca {dCabeca:0.0} px em {TiquesDaVelocidade} tiques = {vArrasto:0} px/s "
				 + $"(o feixe anda {vFeixe:0}, o arremesso {vArremesso:0}); {taxa:0.0} moidas/s em {rodou * Protocol.TickSeconds:0.00}s, de {menorBatida:0.###} a {maiorBatida:0.###} cada; "
				 + $"estouro de {noEstouro:0.###} ({porMembro:0.###} em cada um dos {membrosDeFora} membros de fora) com o corpo a {corpoDaMao:0.0} tiles de quem atirou "
				 + $"(a cabeca a {cabecaDaMao:0.0} da mao, alcance {AlcanceDoArrastoDaBancada:0})");

		// ATE O FIM DO ALCANCE, E NAO ALEM. Esta linha so pedia "o corpo alem do alcance", e passava com a cabeca a 18,4
		// tiles da mao num raio de 16: o tique da batida nao pagava o alcance (ver `OAlcancePagaOTiqueDaBatida`, que
		// guarda a medida e o defeito injetado). A regua de agora e a de la: a cabeca estoura onde o alcance acaba.
		double umTiqueDeFeixe = Protocol.TickSeconds / raio.SegundosPorTile;
		float corpoDaCabeca = vitima.Pos.X - raio.Pos.X;
		AfirmarPj("o raio leva o corpo ATE O FIM DO ALCANCE dele -- e nao so dez tiles da mao, como no jogo de antes --, e NAO ALEM: "
				  + "a cabeca estoura onde o alcance acaba, com o corpo na frente dela",
				  raio.AndouTiles >= AlcanceDoArrastoDaBancada && EstourouOndeOAlcanceAcaba(cabecaDaMao, AlcanceDoArrastoDaBancada, umTiqueDeFeixe)
				  && Math.Abs(corpoDaCabeca - Feixe.DistanciaDeContato) < 1f,
				  $"alcance pago {raio.AndouTiles:0.00} de {AlcanceDoArrastoDaBancada:0} tiles, com a cabeca a {cabecaDaMao:0.00} da mao "
				  + $"(um tique de feixe sao {umTiqueDeFeixe:0.00}) e o corpo {corpoDaCabeca:0.0} px a frente dela, a {corpoDaMao:0.0} tiles de quem atirou");
		AfirmarPj("...e la ESTOURA: o tiro acaba como explosao, na hora, e o chao ganha a cratera grande",
				  !raio.Vivo && raio.Fim == FimDeProjetil.Explodiu && ExplosoesDoFimDoAlcanceDeTeste == explosoes + 1
				  && CraterasDeExplosaoDeTeste == crateras + 1,
				  $"vivo {raio.Vivo}, fim = {raio.Fim}, explosoes +{ExplosoesDoFimDoAlcanceDeTeste - explosoes}, crateras +{CraterasDeExplosaoDeTeste - crateras}");
		AfirmarPj($"...e a explosao e o grosso do dano: {KiContraCorpo.BatidasDaExplosao:0} batidas CHEIAS de uma vez, espalhadas por todo membro de fora de quem vinha na ponta, "
				  + $"contra {KiContraCorpo.FracaoDaBatidaNoArrasto * 100:0}% de uma batida por ciclo no caminho",
				  porMembro > 0 && Math.Abs(noEstouro - DanoQueFereDoCorte * KiContraCorpo.BatidasDaExplosao) < 1e-6
				  && Math.Abs(maiorBatida - DanoQueFereDoCorte * KiContraCorpo.FracaoDaBatidaNoArrasto) < 1e-6
				  && Math.Abs(maiorBatida - menorBatida) < 1e-6,
				  $"estouro {noEstouro:0.####} (esperado {DanoQueFereDoCorte * KiContraCorpo.BatidasDaExplosao:0.##}), {porMembro:0.####} em cada um dos {membrosDeFora} membros de fora; "
				  + $"batidas no caminho de {menorBatida:0.####} a {maiorBatida:0.####} (esperado {DanoQueFereDoCorte * KiContraCorpo.FracaoDaBatidaNoArrasto:0.##})");

		// ---------- DEPOIS DO ESTOURO, PARA SECO ----------
		Vec2 parou = vitima.Pos;
		for (int i = 0; i < 10; i++) UmTiqueDeArrasto();
		AfirmarPj("depois do estouro o corpo PARA SECO -- nao ha inercia guardada em lugar nenhum",
				  (vitima.Pos - parou).Length < 0.01f, $"{(vitima.Pos - parou).Length:0.00} px depois de solto");
		AfirmarPj("...e as redeas voltam sozinhas: o prazo escorreu, ninguem apagou bit nenhum",
				  vitima.ArrastoRestante <= 0 && !vitima.DirigidoPeloServidor);

		OIgualApanhaEFica(raia);
		DePertoORaioTambemLeva(raia);
		OAlcancePagaOTiqueDaBatida(raia);
		OMuroParaOsDoisEOFeixeContinuaMoendo();
		AAguaSoParaQuemEstaAPe();
		OBonecoLargadoNaoVai(raia);
		ADisputaDeKiLargaOCorpo(raia);
		OLevadoQueCresceESolto(raia);
		OArrastoDoJogoDeAntes(raia);
		LimparTudoDaBancada();
	}

	/// <summary>
	/// O alcance do raio da cena principal do arrasto, em tiles da mao: alem dos dez em que o jogo de antes largava o
	/// corpo (`Projetil.TilesDeArrasto`), pra "ate o fim do alcance" e "ate dez tiles" nao darem a mesma leitura.
	/// </summary>
	private const double AlcanceDoArrastoDaBancada = 16;

	/// <summary>A vida de cada parte do corpo, na ordem do corpo -- o retrato de ANTES, pra medir o que um tique feriu.</summary>
	private static double[] VidaPorParte(ServerPlayer pl) => [.. pl.Combate!.Corpo.Partes.Select(m => m.Vida)];

	/// <summary>
	/// A FERIDA MAIS FUNDA que o corpo ganhou desde o retrato: o maior tombo de vida numa parte so. E o dano de UMA
	/// batida de ki -- ela fere um membro pelo dano inteiro, e o que escorre pros filhos dele (`Regras.Propagacao`,
	/// um quinto) e sempre menor.
	///
	/// NAO E A SOMA DO CORPO, e a primeira versao desta regua era: a batida no abdomen lia 20% a mais, porque o filho
	/// dele (o reprodutor) fica do lado de FORA e recebe o escorrido. E o membro e sorteado -- a medida reprovava ou
	/// passava conforme o dado.
	/// </summary>
	private static double FeridaMaisFunda(ServerPlayer pl, double[] antes)
	{
		List<BodyPart> partes = pl.Combate!.Corpo.Partes;
		double maior = 0;
		for (int i = 0; i < partes.Count && i < antes.Length; i++) maior = Math.Max(maior, antes[i] - partes[i].Vida);
		return maior;
	}

	/// <summary>
	/// O MENOR TOMBO ENTRE OS MEMBROS DE FORA desde o retrato -- zero se algum saiu ileso. Num dano que se divide por
	/// igual entre eles (a explosao de ki, `FerirComAExplosao`) e a parte de cada um, sem o que escorreu de pai pra filho.
	/// </summary>
	private static double MenorFeridaDeFora(ServerPlayer pl, double[] antes)
	{
		List<BodyPart> partes = pl.Combate!.Corpo.Partes;
		double menor = double.MaxValue;
		for (int i = 0; i < partes.Count && i < antes.Length; i++)
			if (!partes[i].Aninhado && !partes[i].Decepado) menor = Math.Min(menor, antes[i] - partes[i].Vida);
		return menor == double.MaxValue ? 0 : Math.Max(menor, 0);
	}

	/// <summary>
	/// O PAR DAS CENAS DE ARRASTO: quem atira (<see cref="BpDoFeixeDaEscada"/>) e um corpo MAIS FRACO
	/// (<see cref="BpDoMaisFracoDaEscada"/>: metade do poder do tiro) a <paramref name="tiles"/> da mao, com o raio de
	/// bancada ja no ar -- 12 por batida CHEIA neste corpo (<see cref="BaseQueFere"/>). Levado, ele paga a fracao do
	/// arrasto a cada ciclo e a explosao no fim do alcance (`KiContraCorpo`).
	/// </summary>
	private (ServerPlayer Atira, ServerPlayer Fraco, Projetil Raio) FeixeNoMaisFraco(string nome, Vec2 deOnde, int tiles,
																					  double velocidade = 1, double alcanceTiles = 40)
	{
		ServerPlayer atira = Forjar($"Feixe de {nome}", deOnde, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		ServerPlayer fraco = Forjar(nome, deOnde + new Vec2(tiles * ZoneCollision.TileSize, 0), bp: BpDoMaisFracoDaEscada);
		return (atira, fraco, RaioDaBancada(atira, BaseQueFere(atira, fraco), velocidade, alcanceTiles));
	}

	/// <summary>
	/// O DEGRAU DE CIMA: O IGUAL APANHA E FICA. De uma a quatro vezes o poder do tiro o corpo nao e levado -- a
	/// cabeca para na frente dele e moi, e ele paga a batida INTEIRA a cada ciclo (quem e levado paga uma fracao e,
	/// no fim, a explosao). Sem defender, o que o raio lhe cobra e o passo: ele anda devagar enquanto apanha
	/// (`AndarDevagarSobORaio`; quanto, e a `--v020teste` que mede no relogio da ficha).
	/// </summary>
	private void OIgualApanhaEFica(Vec2 raia)
	{
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;

		ServerPlayer atira = Forjar("Feixe do igual", raia, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		ServerPlayer igual = Forjar("Igual", raia + new Vec2(6 * T, 0), bp: BpDoIgualDaEscada);
		Projetil raio = RaioDaBancada(atira, baseDano: BaseQueFereUmIgual(atira));
		double razao = KiContraCorpo.Razao(igual.Ficha.expressedBP, raio.Bp);
		AfirmarPj("PREPARO: o corpo tem o MESMO poder do tiro -- o primeiro degrau de quem fica (`PostoContraKi.MaisForte`)",
				  KiContraCorpo.Posto(razao) == PostoContraKi.MaisForte && Math.Abs(razao - 1) < 0.01, $"razao {razao:0.####}");

		for (int i = 0; i < 300 && raio.Vivo && !raio.Encostado; i++) UmTiqueDeArrasto();
		Vec2 onde = igual.Pos;
		double menorBatida = double.MaxValue, maiorBatida = 0;
		int batidas = 0;
		for (int i = 0; i < 30 && raio.Vivo; i++)
		{
			double[] antes = VidaPorParte(igual);
			UmTiqueDeArrasto();
			double batida = FeridaMaisFunda(igual, antes);
			if (batida <= 1e-9) continue;
			batidas++;
			menorBatida = Math.Min(menorBatida, batida);
			maiorBatida = Math.Max(maiorBatida, batida);
		}

		AfirmarPj("o IGUAL nao e levado: a cabeca para na frente dele e fica, e ele nao sai do lugar nem e arremessado",
				  raio.Vivo && raio.Encostado && raio.Arrastando == 0 && (igual.Pos - onde).Length < 0.01f
				  && igual.TiquesDeVoo == 0 && !igual.DirigidoPeloServidor,
				  $"encostado {raio.Encostado}, levando {raio.Arrastando}, andou {(igual.Pos - onde).Length:0.00} px, voo {igual.TiquesDeVoo}");
		AfirmarPj("...mas APANHA, e a batida inteira: 12 a cada ciclo de moer (quem e levado paga so a fracao do arrasto)",
				  batidas >= 4 && Math.Abs(maiorBatida - DanoQueFereDoCorte) < 1e-6 && Math.Abs(maiorBatida - menorBatida) < 1e-6,
				  $"{batidas} batida(s) num segundo, de {menorBatida:0.####} a {maiorBatida:0.####} cada");
		AfirmarPj("...sem atordoar (isso e do mais fraco), e com o passo LENTO armado enquanto o raio bate (`KiContraCorpo.MsSobORaio`)",
				  igual.Combate!.Stun <= 0 && igual.SobORaioAteMs > NowMs(),
				  $"stun {igual.Combate.Stun:0.##}, sob o raio por mais {igual.SobORaioAteMs - NowMs()} ms");
	}

	/// <summary>
	/// DE PERTO O RAIO TAMBEM LEVA -- *"todo ataque de beam (...) q acertam em cheio o inimigo deveriam
	/// arrastar o mesmo"* (dono, 2026-10-12): a faixa "so alem de quatro tiles" saiu do `Projetil.PodeArrastar`.
	///
	/// E O CONTRA-EXEMPLO DO TIPO: a BOLA, no mesmo par e na mesma distancia, ARREMESSA -- o impulso de perto
	/// do Finale (`objects.dm:449-455`, o `FatorDeEmpurrao`) continua sendo dela. Sem esta metade, "o raio
	/// leva" podia ser "nada mais arremessa", e ninguem veria.
	/// </summary>
	private void DePertoORaioTambemLeva(Vec2 raia)
	{
		LimparTudoDaBancada();

		(_, ServerPlayer colado, Projetil raio) = FeixeNoMaisFraco("Colado", raia, tiles: 2);
		for (int i = 0; i < 200 && raio.Vivo && colado.TiquesDeVoo == 0 && raio.Arrastando == 0; i++)
			UmTiqueDeArrasto();

		AfirmarPj("a QUEIMA-ROUPA do raio LEVA o mais fraco, como de longe -- e nao o arremessa",
				  raio.Arrastando == colado.Id && colado.TiquesDeVoo == 0,
				  $"arrasto={raio.Arrastando} voo={colado.TiquesDeVoo}, cabeca a {raio.AndouTiles:0.0} tiles da mao");

		// A BOLA, NO MESMO PAR: mesma distancia, o mesmo golpe de 12 -- so o TIPO muda.
		LimparTudoDaBancada();
		ServerPlayer atira = Forjar("Bolista de perto", raia, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		ServerPlayer naBola = Forjar("Colado na bola", raia + new Vec2(2 * ZoneCollision.TileSize, 0), bp: BpDoMaisFracoDaEscada);
		Projetil bola = Disparar(atira, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = BaseQueFere(atira, naBola, TipoDeProjetil.Blast), Velocidade = 1,
			AlcanceTiles = 20, Deflectivel = false, Nome = "bola de perto",
		});
		for (int i = 0; i < 60 && bola.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);

		AfirmarPj("...e a BOLA de perto, no mesmo par, ARREMESSA (o impulso do Finale) e nao leva ninguem: ela acaba no corpo",
				  bola.Fim == FimDeProjetil.Acertou && naBola.TiquesDeVoo > 0 && bola.Arrastando == 0,
				  $"fim = {bola.Fim}, voo={naBola.TiquesDeVoo}, arrasto={bola.Arrastando}");
	}

	/// <summary>
	/// O ALCANCE PAGA TODO TIQUE EM QUE A CABECA ANDA, O DA BATIDA INCLUSIVE -- o raio que leva alguem estoura ONDE O
	/// ALCANCE ACABA, e nao adiante.
	///
	/// ============================ O QUE FOI MEDIDO (2026-10-12) ============================
	/// O raio de 12 tiles que pegava o mais fraco aos 3 estourava com a cabeca a 14,25 tiles da mao (o pesado) e a
	/// 14,85 (o comum), com o `AndouTiles` dizendo 12,00 e 12,27; o MESMO raio, sozinho, a 12,00 cravados. No tique
	/// em que a cabeca BATE o `AndarProjetil` voltava antes do desconto do alcance (o passo 7), e a cabeca que leva
	/// alguem bate a cada 0,2 s: um tique em seis andado de graca.
	/// =======================================================================================
	///
	/// A REGUA E O PROPRIO RAIO, SOZINHO: a cabeca que nao encontrou ninguem e a que levou um corpo desde os tres
	/// tiles tem de estourar no mesmo lugar, a menos do que ela anda num tique (o alcance e conferido uma vez por
	/// tique). E o `AndouTiles` tem de dizer onde a cabeca ESTA -- ele decide o `mods` de quem tem `rangemod`.
	///
	/// NAS DUAS VELOCIDADES: o raio pesado da um sub-passo por tique ate encostar; o comum, dois, e encosta no segundo.
	/// </summary>
	private void OAlcancePagaOTiqueDaBatida(Vec2 raia)
	{
		foreach ((string qual, double velocidade) in new[] { ("PESADO", 0.5), ("comum", 1.0) })
		{
			(Projetil sozinho, _, double sozinhoDaMao) = RaioAteOFimDoAlcance(raia, velocidade, comCorpo: false);
			(Projetil levando, bool levou, double levandoDaMao) = RaioAteOFimDoAlcance(raia, velocidade, comCorpo: true);
			double umTique = Protocol.TickSeconds / levando.SegundosPorTile;

			GD.Print($"[projetil]      (medido) raio {qual} de {AlcanceDaCenaDoAlcance:0} tiles: sozinho, a cabeca estoura a {sozinhoDaMao:0.00} tiles da mao; "
					 + $"levando o mais fraco desde os 3, a {levandoDaMao:0.00}, com {levando.AndouTiles:0.00} de alcance pago (um tique de feixe sao {umTique:0.00} tiles)");

			AfirmarPj($"(controle) o raio {qual} SOZINHO estoura onde o alcance acaba: a cabeca a {AlcanceDaCenaDoAlcance:0} tiles da mao, sem passar dele mais que um tique",
					  sozinho.Fim == FimDeProjetil.Explodiu && EstourouOndeOAlcanceAcaba(sozinhoDaMao, AlcanceDaCenaDoAlcance, umTique),
					  $"fim = {sozinho.Fim}, cabeca a {sozinhoDaMao:0.00} tiles da mao");
			AfirmarPj($"o raio {qual} que LEVA o mais fraco estoura NO MESMO LUGAR: o alcance paga todo tique em que a cabeca anda, o da batida inclusive",
					  levou && levando.Fim == FimDeProjetil.Explodiu && EstourouOndeOAlcanceAcaba(levandoDaMao, AlcanceDaCenaDoAlcance, umTique),
					  $"levou {levou}, fim = {levando.Fim}, cabeca a {levandoDaMao:0.00} tiles da mao (sozinho: {sozinhoDaMao:0.00})");
			AfirmarPj($"...e o `AndouTiles` do raio {qual} diz onde a cabeca ESTA: o alcance pago e o caminho andado sao o mesmo numero",
					  levou && Math.Abs(levando.AndouTiles - levandoDaMao) < FolgaDaReguaDoAlcance,
					  $"pago {levando.AndouTiles:0.00} tiles, andado {levandoDaMao:0.00}");
		}

		ACabecaPlantadaNaoGastaAlcance(raia);
		QuemAUltimaBatidaPegaVaiNaPonta(raia);

		BatidaNaoPagaOAlcanceDeTeste = true;
		try
		{
			(Projetil deAntes, bool levou, double daMao) = RaioAteOFimDoAlcance(raia, velocidade: 0.5, comCorpo: true);
			double umTique = Protocol.TickSeconds / deAntes.SegundosPorTile;
			AfirmarPj("(defeito injetado: a batida nao paga o alcance) o MESMO raio pesado leva o corpo ALEM do alcance, com o `AndouTiles` dizendo que nao -- "
					  + "as duas reguas de cima ficam vermelhas",
					  levou && deAntes.Fim == FimDeProjetil.Explodiu && !EstourouOndeOAlcanceAcaba(daMao, AlcanceDaCenaDoAlcance, umTique)
					  && daMao - deAntes.AndouTiles > 1,
					  $"levou {levou}, fim = {deAntes.Fim}, cabeca a {daMao:0.00} tiles da mao com {deAntes.AndouTiles:0.00} pagos (alcance {AlcanceDaCenaDoAlcance:0})");
		}
		finally { BatidaNaoPagaOAlcanceDeTeste = false; }
	}

	/// <summary>
	/// O OUTRO LADO DA CONTA: QUEM NAO ANDA NAO PAGA. A cabeca plantada em quem ela NAO leva (o igual, a parede) da
	/// um sub-passo e e replantada a cada ciclo de moer -- andou zero. O que o alcance cobra no tique da batida e o
	/// que a cabeca ANDOU (os sub-passos menos o recuo do plantio), e nao o sub-passo: cobrado, o alcance de um raio
	/// PARADO escorreria ate ele estourar sozinho em cima de quem so o segura.
	/// </summary>
	private void ACabecaPlantadaNaoGastaAlcance(Vec2 raia)
	{
		const int T = ZoneCollision.TileSize;
		LimparTudoDaBancada();

		ServerPlayer atira = Forjar("Feixe parado", raia, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		Forjar("Igual que segura", raia + new Vec2(3 * T, 0), bp: BpDoIgualDaEscada);
		Projetil raio = RaioDaBancada(atira, BaseQueFereUmIgual(atira), alcanceTiles: AlcanceDaCenaDoAlcance);
		for (int i = 0; i < 300 && raio.Vivo && !raio.Encostado; i++) UmTiqueDeArrasto();
		double aoEncostar = raio.AndouTiles;
		for (int i = 0; i < TiquesDoRaioParado && raio.Vivo; i++) UmTiqueDeArrasto();
		double daMao = (raio.Pos - raio.Cauda).Length / T;

		AfirmarPj($"a cabeca PLANTADA em quem ela nao leva (o igual) NAO gasta alcance: em {TiquesDoRaioParado} tiques de moer o `AndouTiles` continua sendo da mao ate a cabeca",
				  raio.Vivo && raio.Encostado && raio.Arrastando == 0 && Math.Abs(raio.AndouTiles - daMao) < FolgaDaReguaDoAlcance
				  && Math.Abs(raio.AndouTiles - aoEncostar) < FolgaDaReguaDoAlcance,
				  $"vivo {raio.Vivo}, encostado {raio.Encostado}, levando {raio.Arrastando}; pago {aoEncostar:0.00} tiles ao encostar e {raio.AndouTiles:0.00} depois, com a cabeca a {daMao:0.00} da mao");
	}

	/// <summary>Por quantos tiques a cena do raio parado moi: um segundo, cinco ciclos de moer.</summary>
	private const int TiquesDoRaioParado = 30;

	/// <summary>
	/// QUEM A ULTIMA BATIDA PEGA JA VAI NA PONTA. O corpo que a cabeca so alcanca no ULTIMO tique do alcance: a batida o
	/// pega, o alcance acaba naquele mesmo tique, e a explosao INTEIRA e dele -- sem ser levado a lugar nenhum. Sem a
	/// linha que o poe na ponta (o passo 6b do `AndarProjetil`) ele entraria na explosao como um vizinho, pela area.
	///
	/// A CENA E DE REGUA: a cabeca do raio pesado anda um terco de tile por tique e chega aos quatro tiles no 12o; o
	/// corpo esta um contato adiante disso, mais <see cref="FolgaDaUltimaBatida"/> -- a cabeca o alcanca nesse tique e
	/// nao no anterior, e o plantio a deixa essa folga ALEM do alcance (a conta nao depende do ultimo bit de um float).
	/// </summary>
	private void QuemAUltimaBatidaPegaVaiNaPonta(Vec2 raia)
	{
		const int T = ZoneCollision.TileSize;
		LimparTudoDaBancada();

		ServerPlayer atira = Forjar("Feixe curto", raia, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		Vec2 mao = BocaDeCano.De(atira.Pos, new Vec2(1, 0));
		ServerPlayer pego = Forjar("Pego no fim", mao + new Vec2((float)AlcanceDaUltimaBatida * T + Feixe.DistanciaDeContato + FolgaDaUltimaBatida, 0),
								   bp: BpDoMaisFracoDaEscada);
		Projetil raio = RaioDaBancada(atira, BaseQueFere(atira, pego), velocidade: 0.5, alcanceTiles: AlcanceDaUltimaBatida);

		Vec2 ondeEstava = pego.Pos;
		double[] ileso = VidaPorParte(pego), antesDoUltimoTique = ileso;
		int tiques = 0;
		for (; tiques < 300 && raio.Vivo; tiques++)
		{
			antesDoUltimoTique = VidaPorParte(pego);
			UmTiqueDeArrasto();
		}
		// A BATIDA E A EXPLOSAO NO MESMO TIQUE: ate o ultimo o corpo nao tinha levado nada.
		bool ilesoAteOUltimo = antesDoUltimoTique.SequenceEqual(ileso);
		int membrosDeFora = pego.Combate!.Corpo.Partes.Count(m => !m.Aninhado && !m.Decepado);
		double porMembro = MenorFeridaDeFora(pego, antesDoUltimoTique), noEstouro = porMembro * membrosDeFora;
		float andou = (pego.Pos - ondeEstava).Length;

		GD.Print($"[projetil]      (medido) a ultima batida: o raio de {AlcanceDaUltimaBatida:0} tiles acabou no tique {tiques}, com a cabeca a {(raio.Pos - mao).Length / T:0.00} da mao; "
				 + $"o corpo andou {andou:0.00} px e levou {noEstouro:0.###} de explosao ({porMembro:0.###} em cada um dos {membrosDeFora} membros de fora)");
		AfirmarPj("o corpo que a cabeca so alcanca no ULTIMO tique do alcance leva a batida e a explosao INTEIRA no mesmo tique, sem sair do lugar: "
				  + "quem a batida acabou de pegar ja e quem vai na ponta",
				  !raio.Vivo && raio.Fim == FimDeProjetil.Explodiu && ilesoAteOUltimo && andou < 0.01f
				  && Math.Abs(noEstouro - DanoQueFereDoCorte * KiContraCorpo.BatidasDaExplosao) < 1e-6,
				  $"fim = {raio.Fim} no tique {tiques} (ileso ate o ultimo: {ilesoAteOUltimo}), o corpo andou {andou:0.00} px, "
				  + $"explosao de {noEstouro:0.####} (a inteira sao {DanoQueFereDoCorte * KiContraCorpo.BatidasDaExplosao:0.##})");
	}

	/// <summary>O alcance do raio da cena da ultima batida, em tiles: curto, pra a cabeca chegar ao fim dele sem encontrar ninguem antes.</summary>
	private const double AlcanceDaUltimaBatida = 4;

	/// <summary>
	/// Quanto ALEM de um contato do fim do alcance o corpo da cena da ultima batida esta, em pixels: menos que o meio
	/// pixel de tolerancia do encosto (`Feixe.EncostaNoCorpo`), e mil vezes o erro de float de uma posicao.
	/// </summary>
	private const float FolgaDaUltimaBatida = 0.4f;

	/// <summary>
	/// O alcance do raio das cenas que medem ONDE ele estoura, em tiles da mao: o da medida de 2026-10-12. Com o corpo
	/// a tres tiles de quem atira a cabeca o leva por quase onze -- umas dez batidas no caminho --, e mesmo com o
	/// defeito injetado (a cabeca a 14,85) a cena cabe no corredor da familia.
	/// </summary>
	private const double AlcanceDaCenaDoAlcance = 12;

	/// <summary>
	/// A folga de float das reguas do alcance, em tiles (0,64 px). Um tique de batida que nao paga sao pelo menos 0,2
	/// tile: o que a cabeca do arrasto mais lento desta bancada anda num tique.
	/// </summary>
	private const double FolgaDaReguaDoAlcance = 0.02;

	/// <summary>
	/// A REGUA DE "ONDE O RAIO ESTOURA": a cabeca a <paramref name="alcance"/> tiles da mao -- nem aquem, nem alem dele
	/// mais do que ela anda num tique. O alcance e conferido uma vez por tique (o passo 7 do `AndarProjetil`), entao um
	/// tique de caminho e a granularidade dele; a cabeca que leva alguem anda menos que isso.
	/// </summary>
	private static bool EstourouOndeOAlcanceAcaba(double cabecaDaMao, double alcance, double umTique)
		=> cabecaDaMao >= alcance - FolgaDaReguaDoAlcance && cabecaDaMao <= alcance + umTique + FolgaDaReguaDoAlcance;

	/// <summary>
	/// UM RAIO DE BANCADA ATE ACABAR, no corredor da familia: sozinho, ou com o mais fraco a tres tiles de quem atira.
	/// Devolve o raio (morto: onde ele acabou, e que fim teve), se a cabeca chegou a LEVAR o corpo, e a que distancia da
	/// MAO ela acabou, em tiles -- a mao e a boca do cano (`BocaDeCano`), onde a cabeca nasce e onde a cauda de um raio
	/// canalizado fica.
	/// </summary>
	private (Projetil Raio, bool Levou, double CabecaDaMao) RaioAteOFimDoAlcance(Vec2 raia, double velocidade, bool comCorpo)
	{
		LimparTudoDaBancada();
		ServerPlayer? corpo = null;
		Projetil raio;
		if (comCorpo)
			(_, corpo, raio) = FeixeNoMaisFraco("Levado ate o fim", raia, tiles: 3, velocidade: velocidade, alcanceTiles: AlcanceDaCenaDoAlcance);
		else
		{
			ServerPlayer atira = Forjar("Feixe sozinho", raia, bp: BpDoFeixeDaEscada);
			atira.Facing = Facing.East;
			raio = RaioDaBancada(atira, BaseQueFereUmIgual(atira), velocidade, AlcanceDaCenaDoAlcance);
		}

		bool levou = false;
		for (int t = 0; t < 900 && raio.Vivo; t++)
		{
			UmTiqueDeArrasto();
			levou |= corpo != null && raio.Arrastando == corpo.Id;
		}
		return (raio, levou, (raio.Pos - raio.Cauda).Length / ZoneCollision.TileSize);
	}

	/// <summary>
	/// PRENSADO NUM MURO, os dois param -- e o feixe **continua moendo**.
	///
	/// A decisao esta escrita no `ArrastarComOFeixe` e vem do DM pelos dois lados: o `step()` do
	/// arrasto falha contra densidade, e o `Knockback` escreve a mesma conclusao com todas as letras
	/// (`if(loc == old_loc) { KB=0; break }`, `death.dm:230`). O que esta bancada mede alem disso e
	/// que a cabeca **nao atravessa** a vitima prensada: sem a ordem certa (empurrar antes de avancar)
	/// o feixe passaria por dentro de quem ele esta esmagando.
	///
	/// E O MURO NAO E O FIM DO ALCANCE: a cabeca que nao anda nao gasta alcance, entao o raio segurado em
	/// cima de quem esta prensado NAO estoura -- ele moi, na fracao do arrasto, enquanto durar.
	/// </summary>
	private void OMuroParaOsDoisEOFeixeContinuaMoendo()
	{
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;

		if (_pjMapa is not { } mapa) return;
		if (!AcharParede(mapa, out int cx, out int cy, livresAtras: 9))
		{
			AfirmarPj("achei um muro com nove celulas secas na frente pra medir o corpo prensado",
					  false, "varredura nao achou");
			return;
		}

		// O MAIS FRACO COM O MURO NAS COSTAS: a sete tiles da mao, na celula colada na parede.
		var deOnde = new Vec2((cx - 8) * T + T / 2f, cy * T + T / 2f);
		(_, ServerPlayer preso, Projetil raio) = FeixeNoMaisFraco("Prensado", deOnde, tiles: 7);
		for (int i = 0; i < 300 && raio.Vivo && raio.Arrastando == 0; i++) UmTiqueDeArrasto();

		AfirmarPj("o feixe pegou o corpo que esta com o muro nas costas", raio.Arrastando == preso.Id,
				  $"arrastando={raio.Arrastando}");
		if (raio.Arrastando != preso.Id) return;

		// O CORPO PODE ANDAR OS PIXELS QUE FALTAM ATE ENCOSTAR (a caixa dele tem meio tile de folga na celula): o que
		// se mede e que ele PARA -- a posicao de meio segundo depois e a de um segundo depois -- sem entrar no muro.
		int explosoes = ExplosoesDoFimDoAlcanceDeTeste;
		for (int i = 0; i < 30; i++) UmTiqueDeArrasto();
		Vec2 encostou = preso.Pos;
		double vidaAntes = preso.Combate!.Corpo.Vida();
		for (int i = 0; i < 30; i++) UmTiqueDeArrasto();

		AfirmarPj("prensado contra o muro, o corpo PARA (o `step()` que falha do DM), sem entrar na parede",
				  (preso.Pos - encostou).Length < 0.01f && preso.Pos.X + Feixe.MeioCorpo <= cx * T + 0.5f,
				  $"andou {(preso.Pos - encostou).Length:0.00} px no ultimo segundo; beirada do corpo em {preso.Pos.X + Feixe.MeioCorpo:0.0}, parede em {cx * T}");
		AfirmarPj("...e a cabeca do feixe NAO o atravessa: ela para nele",
				  raio.Pos.X <= preso.Pos.X + Projetil.RaioDeImpacto + 1f,
				  $"cabeca em {raio.Pos.X:0}, corpo em {preso.Pos.X:0}");
		AfirmarPj("...e o feixe nao estoura nem some: ele continua MOENDO quem esta prensado (o muro nao e o fim do alcance)",
				  raio.Vivo && raio.Arrastando == preso.Id && preso.Combate.Corpo.Vida() < vidaAntes - 1e-9
				  && ExplosoesDoFimDoAlcanceDeTeste == explosoes,
				  $"vivo={raio.Vivo}, levando {raio.Arrastando}, vida {vidaAntes:0.###} -> {preso.Combate.Corpo.Vida():0.###}, explosoes +{ExplosoesDoFimDoAlcanceDeTeste - explosoes}");
	}

	/// <summary>
	/// A AGUA SO PARA QUEM ESTA A PE -- e quem decide e o `ModoDeTravessiaDe`, a MESMA funcao que valida o
	/// passo do jogador e o da IA.
	///
	/// O mesmo lago, o mesmo tiro, o mesmo corpo: muda so o `Nadando`. Medir so a metade que passa (ou
	/// so a que para) deixaria verde um codigo que ignorasse a agua por inteiro.
	///
	/// O QUE SE LE E "ENTROU NA AGUA", TIQUE A TIQUE, e nao onde o corpo esta no fim: o raio leva quem nada
	/// ate o fim do alcance dele, e um lago estreito seria atravessado de margem a margem.
	/// </summary>
	private void AAguaSoParaQuemEstaAPe()
	{
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;

		if (_pjMapa is not { TemAgua: true } mapa)
		{
			AfirmarPj("a zona da bancada tem agua marcada pra medir o arrasto na beira do lago",
					  false, "sem plano de agua no `.col`");
			return;
		}
		if (!AcharMargem(mapa, out int cx, out int cy, secosAtras: 9))
		{
			AfirmarPj("achei uma margem de lago com nove celulas secas atras", false, "varredura nao achou");
			return;
		}

		for (int volta = 0; volta < 2; volta++)
		{
			bool nadando = volta == 1;
			LimparTudoDaBancada();

			// O MAIS FRACO NA BEIRA: a sete tiles da mao, na celula seca colada na agua. O raio tem 12 tiles de alcance
			// -- sobram cinco lago adentro, e a cena nao varre o mapa.
			var deOnde = new Vec2((cx - 8) * T + T / 2f, cy * T + T / 2f);
			(_, ServerPlayer alvo, Projetil raio) = FeixeNoMaisFraco("Molhado", deOnde, tiles: 7, alcanceTiles: 12);
			alvo.Nadando = nadando;

			for (int i = 0; i < 300 && raio.Vivo && raio.Arrastando == 0; i++) UmTiqueDeArrasto();
			if (raio.Arrastando != alvo.Id)
			{
				AfirmarPj($"o feixe pegou o corpo na beira do lago (nadando={nadando})", false,
						  $"arrastando={raio.Arrastando}");
				continue;
			}

			bool entrou = false;
			Vec2 daBeira = alvo.Pos;
			for (int i = 0; i < 60 && raio.Vivo; i++)
			{
				UmTiqueDeArrasto();
				entrou |= mapa.EhAguaEm(alvo.Pos);
			}

			AfirmarPj(nadando
						  ? "...e quem esta NADANDO e levado pra dentro do lago pelo mesmo feixe"
						  : "quem esta A PE e levado ate a beira do lago e para nela",
					  entrou == nadando && (nadando || (alvo.Pos.X - daBeira.X) < T),
					  $"entrou na agua={entrou}, andou {(alvo.Pos.X - daBeira.X) / T:0.0} tiles desde a beira, fim do raio = {raio.Fim}");
		}
	}

	/// <summary>
	/// O BONECO LARGADO NAO VAI -- nem sendo mais fraco que o tiro.
	///
	/// A recusa e `_players.ContainsKey`, e nao um `if` de `Peer`/`Cerebro` -- ver
	/// `PodeSerLevadoPeloFeixe`. O boneco e definido por NAO estar nessa lista, e e essa mesma lista
	/// que o `TickDoEmpurrao` percorre pra soltar o corpo: carrega-lo seria o unico corpo do jogo que
	/// ninguem solta. A bancada reproduz o boneco do jeito que ele existe -- fora do `_players` e
	/// dentro da zona.
	///
	/// O CORPO E O DO DEGRAU DE QUEM E LEVADO, de proposito: com um igual a cena ficaria verde pela escada
	/// (o igual nunca vai), e nao pela lista. E o controle fecha a conta -- devolvido ao `_players`, o
	/// MESMO corpo passa a ser levado pelo MESMO raio.
	/// </summary>
	private void OBonecoLargadoNaoVai(Vec2 raia)
	{
		LimparTudoDaBancada();

		(_, ServerPlayer boneco, Projetil raio) = FeixeNoMaisFraco("Boneco", raia, tiles: 6);

		// E ASSIM QUE UM BONECO EXISTE: na zona (ele ocupa lugar e apanha) e fora do `_players`.
		_players.Remove(boneco.Id);
		try
		{
			Vec2 onde = boneco.Pos;
			double vida = boneco.Combate!.Corpo.Vida();
			for (int i = 0; i < 18 && raio.Vivo; i++) UmTiqueDeArrasto();

			AfirmarPj("o feixe NAO carrega o corpo largado (ele nao esta na lista que solta ninguem): a cabeca para nele e moi",
					  raio.Vivo && raio.Encostado && raio.Arrastando == 0 && (boneco.Pos - onde).Length < 0.01f
					  && boneco.Combate.Corpo.Vida() < vida - 1e-9,
					  $"encostado {raio.Encostado}, arrastando={raio.Arrastando}, andou {(boneco.Pos - onde).Length:0.0} px, vida {vida:0.###} -> {boneco.Combate.Corpo.Vida():0.###}");
		}
		finally { _players[boneco.Id] = boneco; }   // devolve, senao o `LimparTudoDaBancada` o deixa na zona

		for (int i = 0; i < 12 && raio.Vivo && raio.Arrastando == 0; i++) UmTiqueDeArrasto();
		AfirmarPj("(controle) devolvido a lista, o MESMO corpo passa a ser levado pelo MESMO raio -- a recusa era a da lista, e nao a do degrau",
				  raio.Arrastando == boneco.Id, $"arrastando={raio.Arrastando}");
	}

	/// <summary>
	/// ENTROU NUMA DISPUTA DE KI, LARGA.
	///
	/// Numa colisao de feixes quem manda na cabeca e o `TickDosEmbatesDeKi` (ela e posta no ponto de
	/// encontro a cada tique). Uma cabeca que nao anda nao tem delta pra passar pro corpo, e continuar
	/// "arrastando" seria segurar alguem parado sem nada empurrando -- ver o passo 1 do `AndarProjetil`.
	/// </summary>
	private void ADisputaDeKiLargaOCorpo(Vec2 raia)
	{
		LimparTudoDaBancada();

		(_, ServerPlayer alvo, Projetil raio) = FeixeNoMaisFraco("Disputado", raia, tiles: 6);
		for (int i = 0; i < 300 && raio.Vivo && raio.Arrastando == 0; i++) UmTiqueDeArrasto();
		if (raio.Arrastando != alvo.Id)
		{
			AfirmarPj("o feixe pegou o corpo antes da disputa", false, $"arrastando={raio.Arrastando}");
			return;
		}

		raio.EmEmbate = true;
		UmTiqueDeArrasto();
		AfirmarPj("entrando numa disputa de ki, a cabeca LARGA quem estava levando",
				  raio.Arrastando == 0);

		Vec2 parado = alvo.Pos;
		for (int i = 0; i < 10; i++) UmTiqueDeArrasto();
		AfirmarPj("...e o corpo volta a ser dele no mesmo prazo de sempre",
				  (alvo.Pos - parado).Length < 0.01f && !alvo.DirigidoPeloServidor);
		raio.EmEmbate = false;
	}

	/// <summary>
	/// O ARRASTO JA COMECADO OBEDECE A ESCADA: quem vinha sendo LEVADO e deixa de ser mais fraco que o tiro no meio do
	/// caminho -- o corpo que cresce: uma forma, uma explosao de raiva -- e SOLTO na batida seguinte. A razao e relida
	/// a cada batida (`GameServer.Acertar`), e a regra do dono e uma so: de igual a quatro vezes, apanha e NAO e levado.
	///
	/// ============================ O QUE FOI MEDIDO ANTES DO CONSERTO (2026-10-12) ============================
	/// O par da cena principal (raio de 16 tiles, o corpo com metade do poder a 6), com o poder do corpo saltando no
	/// tique em que o arrasto comeca:
	///   * pra 2x e pra 3,9x o poder do tiro: continuou levado por 70 tiques e 14 tiles, ate o fim do alcance, pagando
	///     a batida CHEIA a cada ciclo, e levou a explosao inteira de quem vinha na ponta (seis batidas cheias);
	///   * pra 5x (parede): solto em cinco tiques -- o unico ramo que zerava o `Projetil.Arrastando` era o do impacto
	///     que nao e golpe (`EstourarSemFerir`; a cena (i) da familia 16).
	/// O `vaiSerLevado` do `Acertar` ja dava falso; faltava soltar. A escada daquela manha fica no fim, como o defeito
	/// injetado (`KiContraCorpo.ArrastoSoSoltaAParedeDeTeste`).
	/// =========================================================================================================
	///
	/// TRES CORPOS, PORQUE A LINHA DO CONSERTO E UMA SO PRA OS TRES: quem leva o raio SEM DEFENDER (fica onde esta,
	/// apanha a batida cheia, anda devagar, e do estouro so paga a area), quem o leva na ESFERA GRANDE (ela acaba nele
	/// e estoura ali, como em qualquer um que ela nao leva) e quem o leva DE GUARDA (solto, as maos dele seguram o raio
	/// na batida seguinte: quem esta sendo arrastado nao abre disputa -- `TentarEmbateDeGuarda`).
	/// </summary>
	private void OLevadoQueCresceESolto(Vec2 raia)
	{
		const int T = ZoneCollision.TileSize;
		const double Cheia = DanoQueFereDoCorte, NoArrasto = DanoQueFereDoCorte * KiContraCorpo.FracaoDaBatidaNoArrasto;
		static bool Todas(List<double> batidas, double valor) => batidas.Count > 0 && batidas.TrueForAll(b => Math.Abs(b - valor) < 1e-6);
		static string Lista(List<double> batidas) => string.Join(", ", batidas.Select(b => $"{b:0.###}"));
		static bool NaBatidaSeguinte(int antes, int depois) => antes > 0 && depois > antes && depois - antes <= TiquesAteABatidaSeguinte;

		// ---------- sem defender, pra duas vezes o poder do tiro: o caso medido ----------
		CrescidoNoArrasto c = RaioQueLevavaQuemCresce(raia, vezes: 2);
		// (os numeros medidos saem sempre, e nao so no detalhe de uma falha -- o habito da cena principal)
		GD.Print($"[projetil]      (medido) LEVADO QUE CRESCE pra {c.Vezes:0.###}x o poder do tiro (raio de {AlcanceDeQuemCresce:0} tiles): pego no tique {c.PegouEm} pagando {c.BatidaAoPegar:0.###}; "
				 + $"solto no {c.SoltouEm} pagando {c.BatidaAoSoltar:0.###}, levado {c.LevadoDepois:0.0} px nesse meio, com {c.RestavaAoSoltar:0.0} tiles de alcance pela frente; "
				 + $"parado andou {c.AndouParado:0.00} px e levou [{Lista(c.BatidasParado)}]; saiu {c.SaiuDeLado:0.0} px de lado; fim {c.Raio.Fim} a {c.PxDoCentro / T:0.00} tiles dele: "
				 + $"pagou {c.NoEstouro:0.###} (a area cobra {c.DaArea:0.###}; a explosao inteira seria {c.Inteira:0.###})");
		AfirmarPj("PREPARO: o raio pegou o mais fraco (a batida de quem e levado: 35% de 12) e, nesse tique, o corpo CRESCEU pra duas vezes o poder do tiro "
				  + "-- o degrau de quem apanha e nao e levado --, com a batida cheia nele cravada em 12",
				  c.PegouEm > 0 && Math.Abs(c.BatidaAoPegar - NoArrasto) < 1e-6 && KiContraCorpo.Posto(c.Vezes) == PostoContraKi.MaisForte
				  && Math.Abs(c.Vezes - 2) < 0.01 && Math.Abs(c.BatidaCheia - Cheia) < 1e-6,
				  $"pego no tique {c.PegouEm} pagando {c.BatidaAoPegar:0.####}; depois de crescer, {c.Vezes:0.####}x o poder do tiro e {c.BatidaCheia:0.####} por batida cheia");
		AfirmarPj("QUEM VINHA SENDO LEVADO E DEIXA DE SER MAIS FRACO E SOLTO NA BATIDA SEGUINTE -- a escada vale pro arrasto ja comecado --, "
				  + "com o raio vivo e alcance pela frente (nao foi o fim do raio que o largou)",
				  NaBatidaSeguinte(c.PegouEm, c.SoltouEm) && c.RestavaAoSoltar > 1,
				  $"pego no tique {c.PegouEm}, solto no {c.SoltouEm}; levado {c.LevadoDepois:0.0} px nesse meio; {c.RestavaAoSoltar:0.0} tiles de alcance pela frente");
		AfirmarPj("...a batida que o solta ja e a CHEIA, e dali em diante ele FICA ONDE ESTA, apanhando 12 a cada ciclo de moer (e nao os 35% de quem e levado), "
				  + "com a cabeca plantada na frente dele sem levar ninguem",
				  Math.Abs(c.BatidaAoSoltar - Cheia) < 1e-6 && c.AndouParado < 0.01f && c.BatidasParado.Count >= 2 && Todas(c.BatidasParado, Cheia) && c.Plantada,
				  $"ao soltar {c.BatidaAoSoltar:0.####}; parado andou {c.AndouParado:0.00} px e levou [{Lista(c.BatidasParado)}]; cabeca plantada sem levar ninguem: {c.Plantada}");
		AfirmarPj("...com as REDEAS de volta (o prazo do arrasto escorre sozinho) e o passo LENTO de quem apanha sem defender armado (`AndarDevagarSobORaio`)",
				  c.ComAsRedeas && c.PassoLento, $"redeas {c.ComAsRedeas}, passo lento {c.PassoLento}");
		AfirmarPj("...e SAINDO DE LADO, o raio segue ate o fim do alcance e estoura sem cobrar dele a explosao de quem vinha na ponta: "
				  + "dentro do raio dela, ele paga SO A AREA, pela distancia (`KiContraCorpo.FracaoDaExplosao`)",
				  c.SaiuDeLado > KiContraCorpo.LarguraPraSairDeCima(c.Raio) && !c.LevadoNoFim && c.Raio.Fim == FimDeProjetil.Explodiu && c.Explosoes == 1
				  && c.NoEstouro > 0 && Math.Abs(c.NoEstouro - c.DaArea) < 1e-6 && c.DaArea < c.Inteira - 1,
				  $"saiu {c.SaiuDeLado:0.0} px de lado (o raio tem {KiContraCorpo.LarguraPraSairDeCima(c.Raio):0.0} de meia largura); fim {c.Raio.Fim}, explosoes +{c.Explosoes}, levado no fim {c.LevadoNoFim}; "
				  + $"a {c.PxDoCentro / T:0.00} tiles do centro pagou {c.NoEstouro:0.####} (a area cobra {c.DaArea:0.####}; a explosao inteira seria {c.Inteira:0.####})");

		// ---------- logo abaixo da parede: o outro lado do degrau ----------
		CrescidoNoArrasto quase = RaioQueLevavaQuemCresce(raia, vezes: VezesLogoAbaixoDaParede);
		AfirmarPj("LOGO ABAIXO DA PAREDE (3,9x) E O MESMO DEGRAU: solto na batida seguinte, parado e apanhando a batida cheia, e do estouro so a area",
				  KiContraCorpo.Posto(quase.Vezes) == PostoContraKi.MaisForte && Math.Abs(quase.Vezes - VezesLogoAbaixoDaParede) < 0.01
				  && NaBatidaSeguinte(quase.PegouEm, quase.SoltouEm) && quase.AndouParado < 0.01f && quase.BatidasParado.Count >= 2 && Todas(quase.BatidasParado, Cheia)
				  && !quase.LevadoNoFim && quase.NoEstouro > 0 && Math.Abs(quase.NoEstouro - quase.DaArea) < 1e-6,
				  $"{quase.Vezes:0.####}x; pego no tique {quase.PegouEm}, solto no {quase.SoltouEm}; parado andou {quase.AndouParado:0.00} px e levou [{Lista(quase.BatidasParado)}]; "
				  + $"no estouro {quase.NoEstouro:0.####} (a area cobra {quase.DaArea:0.####}; a inteira seria {quase.Inteira:0.####}), levado no fim {quase.LevadoNoFim}");

		// ---------- (controle) sem crescer: a escada so solta quem saiu do degrau ----------
		CrescidoNoArrasto fraco = RaioQueLevavaQuemCresce(raia, vezes: 0);
		AfirmarPj("(controle) SEM CRESCER, o mesmo corpo no mesmo roteiro continua levado ate o fim do alcance, pagando a fracao a cada ciclo, e leva a explosao INTEIRA "
				  + "-- as reguas de cima sabem dizer 'levado'",
				  KiContraCorpo.Arrasta(fraco.Vezes) && fraco.PegouEm > 0 && fraco.SoltouEm < 0 && fraco.LevadoNoFim && fraco.LevadoDepois > 2 * T
				  && Todas(fraco.BatidasParado, NoArrasto) && fraco.Raio.Fim == FimDeProjetil.Explodiu && Math.Abs(fraco.NoEstouro - fraco.Inteira) < 1e-6,
				  $"{fraco.Vezes:0.####}x; pego no tique {fraco.PegouEm}, solto no {fraco.SoltouEm}; levado {fraco.LevadoDepois / T:0.0} tiles, ate o fim: {fraco.LevadoNoFim}; "
				  + $"batidas [{Lista(fraco.BatidasParado)}]; fim {fraco.Raio.Fim} com {fraco.NoEstouro:0.####} no estouro (a inteira e {fraco.Inteira:0.####})");

		// ---------- a esfera grande ----------
		var esfera = EsferaQueLevavaQuemCresce(raia, vezes: 2);
		AfirmarPj("A ESFERA GRANDE OBEDECE A MESMA ESCADA: a que vinha levando quem cresce ACABA nele na batida seguinte e estoura ali (a cratera, sem ser o fim do alcance) "
				  + "-- ele paga o golpe em cheio, num lugar so, e nao a explosao de quem vinha na ponta",
				  esfera.Tiro.EsferaGrande && KiContraCorpo.Posto(esfera.Vezes) == PostoContraKi.MaisForte && NaBatidaSeguinte(esfera.PegouEm, esfera.AcabouEm)
				  && esfera.Tiro.Fim == FimDeProjetil.Acertou && Math.Abs(esfera.Golpe - Cheia) < 1e-6 && esfera.PorFora < 1e-9
				  && esfera.Explosoes == 0 && esfera.Crateras == 1,
				  $"{esfera.Vezes:0.####}x; pego no tique {esfera.PegouEm}, a esfera acabou no {esfera.AcabouEm} com fim {esfera.Tiro.Fim}; no ultimo tique o golpe foi {esfera.Golpe:0.####} "
				  + $"e o membro de fora menos ferido levou {esfera.PorFora:0.####}; explosoes de fim de alcance +{esfera.Explosoes}, crateras +{esfera.Crateras}");

		// ---------- de guarda erguida ----------
		var guarda = RaioQueLevavaAGuardaQueCresce(raia, vezes: 2);
		AfirmarPj("DE GUARDA ERGUIDA E A MESMA ESCADA: o mais fraco que o raio levava defendendo, e cresceu, e SOLTO na batida seguinte -- e na outra as maos dele "
				  + "SEGURAM o raio (a disputa de guarda de quem aguenta o raio; arrastado, ele nunca a abriria)",
				  KiContraCorpo.Posto(guarda.Vezes) == PostoContraKi.MaisForte && NaBatidaSeguinte(guarda.PegouEm, guarda.SoltouEm)
				  && NaBatidaSeguinte(guarda.SoltouEm, guarda.SegurouEm),
				  $"{guarda.Vezes:0.####}x; pego no tique {guarda.PegouEm}, solto no {guarda.SoltouEm}, a disputa de guarda abriu no {guarda.SegurouEm}; "
				  + $"andou {guarda.LevadoDepois:0.0} px depois de crescer, ainda levado no fim: {guarda.Levando}");

		// ---------- A ESCADA DAQUELA MANHA, COMO DEFEITO INJETADO: os tres corpos de cima, so o botao muda ----------
		KiContraCorpo.ArrastoSoSoltaAParedeDeTeste = true;
		try
		{
			CrescidoNoArrasto d = RaioQueLevavaQuemCresce(raia, vezes: 2);
			GD.Print($"[projetil]      (medido) ...E COM O DEFEITO INJETADO: pego no tique {d.PegouEm}, solto no {d.SoltouEm}; levado {d.LevadoDepois / T:0.0} tiles depois de crescer pra {d.Vezes:0.###}x, "
					 + $"pagando [{Lista(d.BatidasParado)}]; fim {d.Raio.Fim} no tique {d.AcabouEm} a {d.PxDoCentro / T:0.00} tiles dele: pagou {d.NoEstouro:0.###} (a explosao inteira e {d.Inteira:0.###})");
			AfirmarPj("(defeito injetado: o arrasto so solta a parede) o MESMO corpo que cresceu pra 2x continua LEVADO ate o fim do alcance, pagando a batida CHEIA a cada ciclo, "
					  + "e leva a explosao INTEIRA de quem vinha na ponta -- as reguas 'e solto', 'fica onde esta' e 'so a area' ficam vermelhas",
					  KiContraCorpo.Posto(d.Vezes) == PostoContraKi.MaisForte && d.PegouEm > 0 && d.SoltouEm < 0 && d.LevadoNoFim && d.LevadoDepois > 2 * T
					  && Todas(d.BatidasParado, Cheia) && d.Raio.Fim == FimDeProjetil.Explodiu && Math.Abs(d.NoEstouro - d.Inteira) < 1e-6,
					  $"{d.Vezes:0.####}x; pego no tique {d.PegouEm}, solto no {d.SoltouEm}; levado {d.LevadoDepois / T:0.0} tiles, ate o fim: {d.LevadoNoFim}; batidas [{Lista(d.BatidasParado)}]; "
					  + $"fim {d.Raio.Fim} com {d.NoEstouro:0.####} no estouro (a inteira e {d.Inteira:0.####})");

			var esferaDeAntes = EsferaQueLevavaQuemCresce(raia, vezes: 2);
			AfirmarPj("(defeito injetado: o arrasto so solta a parede) a ESFERA GRANDE continua levando quem cresceu ate o fim do alcance e estoura NELE: a explosao em todo membro de fora",
					  esferaDeAntes.Tiro.Fim == FimDeProjetil.Explodiu && esferaDeAntes.Levado > 2 * T && esferaDeAntes.PorFora > 1e-9 && esferaDeAntes.Explosoes == 1,
					  $"fim {esferaDeAntes.Tiro.Fim} no tique {esferaDeAntes.AcabouEm} (pego no {esferaDeAntes.PegouEm}); o corpo andou {esferaDeAntes.Levado / T:0.0} tiles depois de crescer; "
					  + $"no ultimo tique o membro de fora menos ferido levou {esferaDeAntes.PorFora:0.####}; explosoes de fim de alcance +{esferaDeAntes.Explosoes}");

			var guardaDeAntes = RaioQueLevavaAGuardaQueCresce(raia, vezes: 2);
			AfirmarPj("(defeito injetado: o arrasto so solta a parede) DE GUARDA ele continua levado e nunca chega a segurar o raio: quem esta sendo arrastado nao abre disputa",
					  guardaDeAntes.PegouEm > 0 && guardaDeAntes.SoltouEm < 0 && guardaDeAntes.SegurouEm < 0 && guardaDeAntes.Levando && guardaDeAntes.LevadoDepois > 2 * T,
					  $"pego no tique {guardaDeAntes.PegouEm}, solto no {guardaDeAntes.SoltouEm}, disputa no {guardaDeAntes.SegurouEm}; "
					  + $"andou {guardaDeAntes.LevadoDepois / T:0.0} tiles depois de crescer, ainda levado no fim: {guardaDeAntes.Levando}");
		}
		finally { KiContraCorpo.ArrastoSoSoltaAParedeDeTeste = false; }
	}

	/// <summary>
	/// O alcance do raio (e da esfera) das cenas de quem cresce no meio do arrasto, em tiles da mao. CURTO DE
	/// PROPOSITO: pego a seis tiles e solto um ciclo depois, o corpo fica a uns dois tiles do fim -- quem sai de lado
	/// continua DENTRO do raio da explosao (`KiContraCorpo.TilesDaExplosaoDeEscala1`, quatro), e a regua consegue
	/// dizer "pagou so a area" em vez de "estava longe demais pra pagar". E poucas batidas cheias cabem no caminho: o
	/// golpe nao letal para no piso do membro (`Body.Ferir`), e um membro no piso leria a explosao pela metade.
	/// </summary>
	private const double AlcanceDeQuemCresce = 8;

	/// <summary>
	/// Em quantos tiques, no maximo, a BATIDA SEGUINTE chega: um ciclo de moer (`Projetil.SegundosPorCicloDeBeam`, seis
	/// tiques) e dois de folga -- a tolerancia da cena (i) da familia 16.
	/// </summary>
	private const int TiquesAteABatidaSeguinte = 8;

	/// <summary>Por quantos tiques quem cresceu fica PARADO antes de tentar sair de lado: dois ciclos de moer, com folga.</summary>
	private const int TiquesParadoSobORaio = 15;

	/// <summary>O passo de quem sai de lado, em pixels por tique -- o da parede que anda raio adentro (familia 16).</summary>
	private const float PassoDeLadoDaBancada = 4;

	/// <summary>
	/// O que aconteceu com quem CRESCEU no meio do arrasto de um raio (<see cref="RaioQueLevavaQuemCresce"/>). Os
	/// tiques contam do disparo, e -1 e "nao aconteceu"; as batidas sao a ferida mais funda de um tique
	/// (<see cref="FeridaMaisFunda"/>); `NoEstouro` e o que o ULTIMO tique fez a todo membro de fora, somado
	/// (<see cref="MenorFeridaDeFora"/> vezes quantos sao -- a marca de uma explosao).
	/// </summary>
	private readonly record struct CrescidoNoArrasto(
		Projetil Raio, ServerPlayer Alvo, double Vezes, double BatidaCheia, int PegouEm, double BatidaAoPegar,
		int SoltouEm, double BatidaAoSoltar, float LevadoDepois, double RestavaAoSoltar,
		float AndouParado, List<double> BatidasParado, bool Plantada, bool ComAsRedeas, bool PassoLento,
		float SaiuDeLado, int AcabouEm, bool LevadoNoFim, int Explosoes, double NoEstouro, double PxDoCentro)
	{
		/// <summary>A explosao INTEIRA: o que paga quem vinha na ponta do tiro (`GameServer.Estourar`).</summary>
		public double Inteira => BatidaCheia * KiContraCorpo.BatidasDaExplosao;

		/// <summary>O que a AREA da explosao cobra de quem esta a esta distancia do centro dela -- a conta do `Estourar`.</summary>
		public double DaArea => Inteira * KiContraCorpo.FracaoDaExplosao(
			PxDoCentro / (KiContraCorpo.TilesDaExplosao(Raio.EscalaVisual) * ZoneCollision.TileSize));
	}

	/// <summary>
	/// O RAIO PESADO PEGA O MAIS FRACO A SEIS TILES E, NO TIQUE EM QUE O ARRASTO COMECA, O CORPO CRESCE ate ter
	/// <paramref name="vezes"/> o poder do tiro, com a batida cheia nele cravada de novo em 12. Dali o roteiro e um so,
	/// pra toda rodada -- a limpa, a do controle e a do defeito:
	///   1. espera a batida seguinte (<see cref="TiquesAteABatidaSeguinte"/>);
	///   2. fica PARADO (<see cref="TiquesParadoSobORaio"/>): le-se se o corpo sai do lugar e quanto vale cada batida;
	///   3. tenta sair de LADO, um passo por tique, ate estar fora da largura do raio -- e so anda quem tem as redeas
	///      (`PodeMexerOCorpo`, a porta por onde o input de um jogador passa): quem continua sendo levado aperta a
	///      tecla e nao sai do lugar;
	///   4. a cena segue ate o raio acabar, e o ultimo tique e o do estouro.
	/// O passo de lado e escrito pela bancada (como o da parede que anda raio adentro, na familia 16): o que ele prova
	/// nao e o andar -- e o que o raio faz com quem saiu da frente dele.
	/// </summary>
	/// <param name="vezes">Quantas vezes o poder do tiro o corpo passa a ter. Abaixo de 1 ele NAO cresce: e o controle.</param>
	private CrescidoNoArrasto RaioQueLevavaQuemCresce(Vec2 raia, double vezes)
	{
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;
		(_, ServerPlayer alvo, Projetil raio) =
			FeixeNoMaisFraco("Crescido", raia, tiles: 6, velocidade: 0.5, alcanceTiles: AlcanceDeQuemCresce);
		int explosoes = ExplosoesDoFimDoAlcanceDeTeste;

		// UM TIQUE DO SERVIDOR E O QUE ELE FEZ AO CORPO: devolve a batida, e anota se foi nele que o raio, VIVO, deixou
		// de leva-lo -- o estouro do fim do alcance tambem zera o `Arrastando`, e aquilo nao e soltar.
		int t = 0, soltouEm = -1;
		double batidaAoSoltar = 0, restavaAoSoltar = 0;
		bool levadoNoUltimo = false;
		Vec2 ondeSoltou = alvo.Pos;
		double[] antes = VidaPorParte(alvo);
		double Tique()
		{
			antes = VidaPorParte(alvo);
			levadoNoUltimo = raio.Arrastando == alvo.Id;
			UmTiqueDeArrasto();
			t++;
			double batida = FeridaMaisFunda(alvo, antes);
			if (soltouEm < 0 && levadoNoUltimo && raio.Vivo && raio.Arrastando != alvo.Id)
			{
				soltouEm = t;
				batidaAoSoltar = batida;
				restavaAoSoltar = raio.Distancia;
				ondeSoltou = alvo.Pos;
			}
			return batida;
		}

		// 0. ATE O RAIO PEGA-LO -- e ai ele cresce (o controle, nao).
		double batidaAoPegar = 0;
		while (raio.Vivo && raio.Arrastando != alvo.Id && t < 300) batidaAoPegar = Tique();
		int pegouEm = raio.Arrastando == alvo.Id ? t : -1;
		if (pegouEm > 0 && vezes >= 1)
		{
			CrescerAte(alvo, raio, vezes);
			CravarDanoFinal(raio, alvo, DanoQueFereDoCorte);
		}
		Vec2 ondeCresceu = alvo.Pos;
		double razao = VezesOPoderDoTiro(raio, alvo), cheia = DanoFinalDe(raio, alvo);

		// 1. A BATIDA SEGUINTE.
		for (int i = 0; i < TiquesAteABatidaSeguinte && raio.Vivo; i++) Tique();

		// 2. PARADO. (a "batida" do tique em que o raio ACABA e o estouro: nao entra na lista)
		Vec2 ondeParou = alvo.Pos;
		var batidasParado = new List<double>();
		for (int i = 0; i < TiquesParadoSobORaio && raio.Vivo; i++)
		{
			double batida = Tique();
			if (batida > 1e-9 && raio.Vivo) batidasParado.Add(batida);
		}
		float andouParado = (alvo.Pos - ondeParou).Length;
		bool plantada = raio.Vivo && raio.Encostado && raio.Arrastando == 0;
		bool comAsRedeas = PodeMexerOCorpo(alvo) && !alvo.DirigidoPeloServidor && alvo.ArrastoRestante <= 0;
		bool passoLento = alvo.SobORaioAteMs > NowMs();

		// 3. O PASSO DE LADO, ate a meia largura do raio e mais meio corpo -- pra fileira vizinha que serve de chao,
		//    quando ha (o corredor da bancada so garante a fileira dele; o sul, se as duas servem ou nenhuma serve).
		int passos = (int)MathF.Ceiling((KiContraCorpo.LarguraPraSairDeCima(raio) + Feixe.MeioCorpo) / PassoDeLadoDaBancada);
		int cx = (int)MathF.Floor(alvo.Pos.X / T), cy = (int)MathF.Floor(alvo.Pos.Y / T);
		Vec2 lado = _pjMapa is { } mapa && !mapa.ServeDeChao(cx, cy + 1) && mapa.ServeDeChao(cx, cy - 1) ? new Vec2(0, -1) : new Vec2(0, 1);
		float deOndeSaiu = alvo.Pos.Y;
		for (int i = 0; i < passos && raio.Vivo; i++)
		{
			if (PodeMexerOCorpo(alvo)) alvo.Pos += lado * PassoDeLadoDaBancada;
			Tique();
		}
		float saiuDeLado = MathF.Abs(alvo.Pos.Y - deOndeSaiu);

		// 4. ATE O RAIO ACABAR.
		while (raio.Vivo && t < 300) Tique();
		int membrosDeFora = alvo.Combate!.Corpo.Partes.Count(m => !m.Aninhado && !m.Decepado);

		return new CrescidoNoArrasto(raio, alvo, razao, cheia, pegouEm, batidaAoPegar, soltouEm, batidaAoSoltar,
									 ((soltouEm > 0 ? ondeSoltou : alvo.Pos) - ondeCresceu).Length, restavaAoSoltar,
									 andouParado, batidasParado, plantada, comAsRedeas, passoLento, saiuDeLado, t, levadoNoUltimo,
									 ExplosoesDoFimDoAlcanceDeTeste - explosoes, MenorFeridaDeFora(alvo, antes) * membrosDeFora,
									 Vec2.Distance(alvo.Pos, raio.Pos));
	}

	/// <summary>
	/// O CORPO CRESCE ATE TER <paramref name="vezes"/> O PODER DESTE TIRO -- uma forma, uma explosao de raiva no meio
	/// do raio --, pela conta de producao do poder expresso (`Fighter.Tick`, como no <see cref="Forjar"/> e na cena (i)
	/// da familia 16). O poder expresso nao e so o BP -- vida, folego e raiva entram na conta, e este corpo acabou de
	/// apanhar --, entao uma segunda volta acerta a razao no numero pedido: a cena diz em que degrau esta sem depender
	/// de quanto o corpo ja perdeu (3,9x nao pode virar parede por um arredondamento).
	/// </summary>
	private void CrescerAte(ServerPlayer corpo, Projetil tiro, double vezes)
	{
		corpo.Ficha.BP = tiro.Bp * vezes;
		corpo.Ficha.Tick(agoraMs: NowMs());
		corpo.Ficha.BP *= vezes / VezesOPoderDoTiro(tiro, corpo);
		corpo.Ficha.Tick(agoraMs: NowMs());
	}

	/// <summary>
	/// UMA ESFERA GRANDE (escala 2: `Projetil.EsferaGrande`) no par das cenas de arrasto, com o corpo CRESCENDO no tique
	/// em que ela o pega e o golpe cheio nele cravado em 12, ate ela acabar. Devolve a esfera morta, a razao depois de
	/// crescer, o tique em que ela o pegou e o em que acabou, quanto o corpo andou depois de crescer, e o que o ULTIMO
	/// tique lhe fez: o golpe (a ferida mais funda) e o que o membro de fora MENOS ferido levou (zero se algum saiu
	/// ileso: so uma explosao fere todos) -- mais as explosoes de fim de alcance e as crateras que a cena somou.
	/// </summary>
	private (Projetil Tiro, double Vezes, int PegouEm, int AcabouEm, float Levado, double Golpe, double PorFora, int Explosoes, int Crateras)
		EsferaQueLevavaQuemCresce(Vec2 raia, double vezes)
	{
		LimparTudoDaBancada();
		ServerPlayer atira = Forjar("Esfera em quem cresce", raia, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		ServerPlayer alvo = Forjar("Crescido na esfera", raia + new Vec2(6 * ZoneCollision.TileSize, 0), bp: BpDoMaisFracoDaEscada);
		Projetil esfera = Disparar(atira, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = BaseQueFere(atira, alvo, TipoDeProjetil.Blast), Velocidade = 1,
			AlcanceTiles = AlcanceDeQuemCresce, EscalaVisual = 2, Deflectivel = false, Nome = "esfera grande",
		});
		int explosoes = ExplosoesDoFimDoAlcanceDeTeste, crateras = CraterasDeExplosaoDeTeste;

		int t = 0, pegouEm = -1;
		Vec2 ondeCresceu = alvo.Pos;
		double[] antes = VidaPorParte(alvo);
		while (esfera.Vivo && t < 600)
		{
			antes = VidaPorParte(alvo);
			UmTiqueDeArrasto();
			t++;
			if (pegouEm > 0 || esfera.Arrastando != alvo.Id) continue;

			pegouEm = t;
			CrescerAte(alvo, esfera, vezes);
			CravarDanoFinal(esfera, alvo, DanoQueFereDoCorte);
			ondeCresceu = alvo.Pos;
		}
		return (esfera, VezesOPoderDoTiro(esfera, alvo), pegouEm, t, (alvo.Pos - ondeCresceu).Length,
				FeridaMaisFunda(alvo, antes), MenorFeridaDeFora(alvo, antes),
				ExplosoesDoFimDoAlcanceDeTeste - explosoes, CraterasDeExplosaoDeTeste - crateras);
	}

	/// <summary>
	/// UM KI WAVE DE VERDADE (`Canalizar`, a carga, o canal -- so o raio que uma mao alimenta entra em disputa:
	/// `PodeDisputar`) contra o mais fraco de GUARDA SEGURADA a seis tiles, que CRESCE no tique em que o raio o pega.
	/// A cena vai ate as maos dele segurarem o raio (a disputa de guarda abrir), ou tres batidas depois de ele crescer.
	/// Devolve a razao depois de crescer, os tiques em que o raio o pegou, em que o soltou e em que a disputa abriu
	/// (-1 = nao aconteceu), quanto ele andou depois de crescer e se o raio ainda o levava no fim.
	/// </summary>
	private (double Vezes, int PegouEm, int SoltouEm, int SegurouEm, float LevadoDepois, bool Levando)
		RaioQueLevavaAGuardaQueCresce(Vec2 raia, double vezes)
	{
		LimparEmbatesDaBancada();
		ServerPlayer atira = Forjar("Raio na guarda que cresce", raia, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		atira.Ficha.Ki = atira.Ficha.MaxKi;
		ServerPlayer guarda = Forjar("Guarda que cresce", raia + new Vec2(6 * ZoneCollision.TileSize, 0), bp: BpDoMaisFracoDaEscada);
		guarda.Facing = Facing.West;
		guarda.Ficha.Ki = guarda.Ficha.MaxKi;
		SegurarAGuarda(guarda);

		ReceitaDeProjetil queFere = SemDeflexao();
		queFere.BaseDano = BaseQueFere(atira, guarda);
		queFere.AlcanceTiles = AlcanceDoArrastoDaBancada;
		Canalizar(atira, "Ki_Wave", 10 * atira.Ficha.BaseDrain(), queFere);

		Projetil? raio = null;
		int pegouEm = -1, soltouEm = -1, segurouEm = -1;
		Vec2 ondeCresceu = guarda.Pos;
		for (int t = 1; t <= 30 * 8; t++)
		{
			// (o empurrao antes, como no `UmTiqueDeArrasto`: e ele que escorre o prazo de quem foi solto)
			TickDoEmpurrao();
			UmTiqueDoEncontro();
			raio ??= _canais.GetValueOrDefault(atira.Id)?.Raio;
			if (raio == null) continue;

			if (pegouEm < 0)
			{
				if (raio.Arrastando == guarda.Id)
				{
					pegouEm = t;
					CrescerAte(guarda, raio, vezes);
					ondeCresceu = guarda.Pos;
				}
				else if (!raio.Vivo) break;
				continue;
			}

			if (soltouEm < 0 && raio.Vivo && raio.Arrastando != guarda.Id) soltouEm = t;
			if (_emEmbateDeKi.ContainsKey(guarda.Id)) { segurouEm = t; break; }
			if (!raio.Vivo || t - pegouEm >= 3 * TiquesAteABatidaSeguinte) break;
		}

		var medido = (raio == null ? 0 : VezesOPoderDoTiro(raio, guarda), pegouEm, soltouEm, segurouEm, (guarda.Pos - ondeCresceu).Length,
					  raio is { Vivo: true } && raio.Arrastando == guarda.Id);
		LimparEmbatesDaBancada();
		return medido;
	}

	/// <summary>
	/// O JOGO DE ANTES DE 2026-10-12, COMO DEFEITO INJETADO -- e o que a escada desta familia substituiu.
	///
	/// `KiContraCorpo.RegraDeAntesDeTeste` repoe o porte do DM/DU: perto (ate quatro tiles) o raio ARREMESSA e nao
	/// carrega (`beam_stun_start = 4`, `death.dm:727`; `maxdistance-distance &lt;= 4`, `objects.dm:449-455`); longe
	/// ele carrega na velocidade DELE, sem atordoar, e larga o corpo aos dez tiles da mao (`if(getdist(Owner,P)&lt;10)`,
	/// `Projectiles.dm:584`) -- e segue viagem, sem estouro. O MESMO par e o MESMO raio das cenas de cima: so o botao
	/// muda, e as reguas de cima (leva de perto, na velocidade do arrasto, ate o fim do alcance, onde estoura) tem
	/// de ficar vermelhas com ele.
	/// </summary>
	private void OArrastoDoJogoDeAntes(Vec2 raia)
	{
		const int T = ZoneCollision.TileSize;
		KiContraCorpo.RegraDeAntesDeTeste = true;
		try
		{
			// ---------- de perto: arremessa, e nao carrega ----------
			LimparTudoDaBancada();
			(_, ServerPlayer colado, Projetil dePerto) = FeixeNoMaisFraco("Colado de antes", raia, tiles: 2);
			for (int i = 0; i < 200 && dePerto.Vivo && colado.TiquesDeVoo == 0 && dePerto.Arrastando == 0; i++)
				UmTiqueDeArrasto();
			AfirmarPj("(defeito injetado: o jogo de antes) a QUEIMA-ROUPA do raio ARREMESSA e nao carrega -- a regua 'de perto o raio tambem leva' fica vermelha",
					  colado.TiquesDeVoo > 0 && dePerto.Arrastando == 0,
					  $"voo={colado.TiquesDeVoo} arrasto={dePerto.Arrastando}");

			// ---------- de longe: na velocidade do feixe, sem atordoar, e so ate dez tiles da mao ----------
			LimparTudoDaBancada();
			(_, ServerPlayer vitima, Projetil raio) =
				FeixeNoMaisFraco("Levado de antes", raia, tiles: 6, velocidade: 0.5, alcanceTiles: AlcanceDoArrastoDaBancada);
			int t = 0;
			while (raio.Vivo && raio.Arrastando == 0 && t++ < 300) UmTiqueDeArrasto();
			bool pegou = raio.Arrastando == vitima.Id;
			double stunAoPegar = vitima.Combate!.Stun;

			const int TiquesDaVelocidade = 6;
			Vec2 corpoAntes = vitima.Pos;
			for (int i = 0; i < TiquesDaVelocidade && raio.Arrastando != 0; i++) UmTiqueDeArrasto();
			double vArrasto = (vitima.Pos - corpoAntes).Length / (TiquesDaVelocidade * Protocol.TickSeconds);
			double vFeixe = ZoneCollision.TileSize / raio.SegundosPorTile;

			int explosoes = ExplosoesDoFimDoAlcanceDeTeste;
			while (raio.Vivo && raio.Arrastando != 0 && t++ < 900) UmTiqueDeArrasto();
			double largouCom = raio.AndouTiles, corpoDaMao = (vitima.Pos.X - raia.X) / T;
			bool vivoAoLargar = raio.Vivo;

			AfirmarPj("(defeito injetado: o jogo de antes) de longe o corpo vai na velocidade DO FEIXE (sem o fator da diferenca de forca) e sem atordoar",
					  pegou && Math.Abs(vArrasto - vFeixe) < vFeixe * 0.02 && stunAoPegar <= 0,
					  $"pegou {pegou}, {vArrasto:0} px/s contra {vFeixe:0} do feixe, stun {stunAoPegar:0.##}");
			AfirmarPj("(defeito injetado: o jogo de antes) o arrasto LARGA aos 10 tiles da mao do dono (`if(getdist(Owner,P)<10)`), com o raio VIVO "
					  + "e sem estouro -- as reguas 'ate o fim do alcance' e 'la estoura' ficam vermelhas",
					  pegou && vivoAoLargar && largouCom >= Projetil.TilesDeArrasto && largouCom < AlcanceDoArrastoDaBancada - 2
					  && corpoDaMao < AlcanceDoArrastoDaBancada - 2 && ExplosoesDoFimDoAlcanceDeTeste == explosoes,
					  $"largou com {largouCom:0.0} tiles andados (alcance {AlcanceDoArrastoDaBancada:0}), raio vivo {vivoAoLargar}, "
					  + $"corpo a {corpoDaMao:0.0} tiles de quem atirou, explosoes +{ExplosoesDoFimDoAlcanceDeTeste - explosoes}");
		}
		finally { KiContraCorpo.RegraDeAntesDeTeste = false; }
	}

	// =====================================================================
	// 10) A BANCADA SE COBRA: OS DEFEITOS INJETADOS
	// =====================================================================
	/// <summary>
	/// AS FAMILIAS 8 E 9 SABEM REPROVAR? -- a pergunta que uma bancada verde nunca responde sozinha.
	///
	/// ============================ DUAS ESPECIES DE INJECAO, E ELAS NAO SE SUBSTITUEM ============================
	/// **a) NO DETECTOR.** O rastro de verdade e medido, e entao COPIADO com um defeito plantado
	/// dentro -- picotado, fora do centro, dobrado, fora da fileira, com o rumo do chao. Cada copia
	/// passa pelo MESMO predicado da familia 8 (<see cref="RegrasDoSulco"/>), e a regra nomeada tem
	/// que ficar vermelha. Isto responde "as cinco afirmacoes tem dente?", que e a pergunta que
	/// nenhuma rodada verde responde.
	///
	/// **b) NO JOGO.** Um campo de PRODUCAO e mexido no meio da medicao (o prazo do arrasto apagado, a
	/// vitima morta) e a mesma medida da familia 9 e refeita. Isto responde a outra metade -- "aquela
	/// linha verde e verde POR CAUSA do arrasto, e nao porque alguma outra coisa move corpo".
	///
	/// Uma sozinha nao serve. A (a) nao sabe se o jogo faz o que a regra descreve; a (b) nao sabe se a
	/// regra descreve alguma coisa. E as duas juntas ainda deixam de fora o que so a FOTO responde --
	/// que o recorte certo esta desenhado certo (ver `--diagraio`).
	/// ==========================================================================================================
	/// </summary>
	private void ABancadaSeCobra()
	{
		GD.Print("[projetil] -- 10) A BANCADA SE COBRA: OS DEFEITOS INJETADOS");
		LimparTudoDaBancada();

		// ---------- (a) O DETECTOR ----------
		Vec2 raia = CorredorSeco(28);
		ServerPlayer pl = Forjar("Arador da injecao", raia, bp: 50_000);
		pl.Facing = Facing.East;

		List<MarcaDeSulco> real = SulcosDeUmTiro(pl, TipoDeProjetil.Beam);
		if (real.Count < 5)
		{
			AfirmarPj("a injecao tem um rastro de verdade pra estragar", false, $"{real.Count} marcas");
			LimparTudoDaBancada();
			return;
		}

		// O CONTROLE. Sem ele um detector que reprovasse TUDO (o modo de falha oposto, e igualmente
		// mudo) passaria nas cinco injecoes de baixo com louvor.
		int verdesNoReal = RegrasDoSulco.Count(r => r.Vale(real, Facing.East));
		AfirmarPj($"(controle) o rastro INTACTO passa nas {RegrasDoSulco.Length} regras",
				  verdesNoReal == RegrasDoSulco.Length, $"{verdesNoReal} de {RegrasDoSulco.Length}");

		foreach (DefeitoDeSulco d in DefeitosDoSulco())
		{
			List<MarcaDeSulco> doente = d.Plantar([.. real]);
			RegraDoSulco alvo = RegrasDoSulco.First(r => r.Nome.Contains(d.RegraQueCai, StringComparison.Ordinal));

			AfirmarPj($"[injecao] {d.Nome} -> a regra \"{d.RegraQueCai}\" fica VERMELHA",
					  !alvo.Vale(doente, Facing.East), alvo.Relato(doente));
		}

		LimparTudoDaBancada();

		// ---------- (b) O JOGO ----------
		OArrastoInjetado();
		LimparTudoDaBancada();
	}

	/// <summary>Um defeito plantado no rastro medido, e o nome da regra que ele TEM que derrubar.</summary>
	private readonly record struct DefeitoDeSulco(
		string Nome, string RegraQueCai, Func<List<MarcaDeSulco>, List<MarcaDeSulco>> Plantar);

	/// <summary>
	/// OS QUATRO DEFEITOS, e cada um e um jeito REAL de este efeito quebrar -- nao ruido aleatorio.
	/// Ruido derruba qualquer regra e nao prova nada sobre nenhuma; estes quatro tem endereco no
	/// codigo de producao, e o comentario de cada um diz qual linha o produziria. (O quinto -- "a marca
	/// no pixel da cabeca, sem alinhar ao centro" -- deixou de ser defeito em 2026-09-04: o rastro
	/// nasce nos pixels de proposito, ver `CarimbarSulco`.)
	/// </summary>
	private static DefeitoDeSulco[] DefeitosDoSulco() =>
	[
		// A chamada saindo do fim do `AndarProjetil` em vez de dentro do laco de sub-passos.
		new("o rastro PICOTADO (carimbo so no fim do tique)", "SE ENCOSTAM",
			s => [.. s.Where((_, i) => i % 2 == 0)]),

		// A guarda de celula (`p.UltimoSulco`) solta: o mesmo tile carimbado a cada sub-passo.
		new("a guarda de celula solta (duas marcas no mesmo tile)", "uma marca por celula",
			s => { s.Insert(s.Count / 2, s[s.Count / 2]); return s; }),

		// A marca carimbada na posicao do DONO (que anda) em vez da do tiro.
		new("a marca carimbada onde o DONO esta, e nao onde o tiro passou", "na fileira do disparo",
			s => { s[^2] = s[^2] with { Onde = s[^2].Onde + new Vec2(0, -ZoneCollision.TileSize) }; return s; }),

		// O `FacingFrom` caindo no default (South) por receber rumo nulo -- o que acontece se o rumo
		// vier do CORPO parado em vez do tiro.
		new("o rumo lido do dono parado (o `FacingFrom` caindo no default South)", "direcao no fio",
			s => [.. s.Select(m => m with { Dir = Facing.South })]),
	];

	/// <summary>
	/// A INJECAO NO JOGO: as duas linhas mais fortes da familia 9, refeitas com o arrasto arrancado.
	///
	/// **1. O PRAZO APAGADO A CADA TIQUE.** E o estado PRE-CORRECAO deste port: a cabeca anda por cima
	/// do corpo e ninguem escreve `ArrastoRestante`. Se a linha "o corpo perde as redeas" da familia 9
	/// fosse verde por acidente -- por nocaute, por combate, por qualquer outra das sete recusas do
	/// `PodeMexerOCorpo` --, ela continuaria verde aqui. Ela nao continua.
	///
	/// **2. A VITIMA MORTA NO MEIO.** O `PodeSerLevadoPeloFeixe` recusa, a cabeca segue viagem e o
	/// corpo fica onde estava. E o contra-exemplo de "o corpo anda EXATAMENTE o que a cabeca andou":
	/// sem ele, um corpo que se mexesse por outro motivo (o empurrao do arremesso, o passo da IA, um
	/// `Correction` mal montado) daria a mesma leitura.
	/// </summary>
	private void OArrastoInjetado()
	{
		// ---- 1. O PRAZO APAGADO ----
		// (o par e o das cenas de arrasto da familia 9 -- `FeixeNoMaisFraco`: so quem e mais fraco que o tiro e levado)
		LimparTudoDaBancada();
		Vec2 raia = CorredorSeco(30);
		(_, ServerPlayer vitima, Projetil raio) = FeixeNoMaisFraco("Levado da injecao", raia, tiles: 6);
		for (int i = 0; i < 300 && raio.Vivo && raio.Arrastando == 0; i++) UmTiqueDeArrasto();

		if (raio.Arrastando != vitima.Id)
		{
			AfirmarPj("[injecao] (montagem) o feixe pegou a vitima antes de o prazo ser apagado",
					  false, $"arrastando={raio.Arrastando}");
			return;
		}

		AfirmarPj("(controle) com o arrasto de pe, o corpo NAO tem as redeas", !PodeMexerOCorpo(vitima));

		// A INJECAO: o prazo apagado DEPOIS de cada tique, que e onde o `ArrastarComOFeixe` o escreve.
		int soltou = 0;
		for (int i = 0; i < 20 && raio.Vivo; i++)
		{
			UmTiqueDeArrasto();
			vitima.ArrastoRestante = 0;
			if (PodeMexerOCorpo(vitima)) soltou++;
		}
		AfirmarPj("[injecao] com o prazo do arrasto apagado, o corpo RECUPERA as redeas -- "
				  + "a linha da familia 9 e verde por causa do arrasto, e nao por outra recusa",
				  soltou > 0, $"{soltou} tique(s) com as redeas de volta");

		// ---- 2. A VITIMA QUE O FEIXE NAO PODE LEVAR ----
		LimparTudoDaBancada();
		(_, ServerPlayer morto, Projetil raio2) = FeixeNoMaisFraco("Recusado", raia, tiles: 6);
		for (int i = 0; i < 300 && raio2.Vivo && raio2.Arrastando == 0; i++) UmTiqueDeArrasto();

		if (raio2.Arrastando != morto.Id)
		{
			AfirmarPj("[injecao] (montagem) o segundo feixe pegou a vitima", false,
					  $"arrastando={raio2.Arrastando}");
			return;
		}

		// A INJECAO -- o `PodeSerLevadoPeloFeixe` passa a recusar. Era `morto.Ficha.dead = true`, mas desde
		// 2026-09-05 o feixe LEVA o corpo morto tambem (dono: "corpos mortos ainda sao corpos de personagem");
		// a recusa que sobrou e a do corpo que nao esta mais em `_players` -- e ela e desfeita logo abaixo.
		_players.Remove(morto.Id);
		Vec2 corpoAntes = morto.Pos, cabecaAntes = raio2.Pos;
		for (int i = 0; i < 6 && raio2.Vivo; i++) UmTiqueDeArrasto();
		_players[morto.Id] = morto;
		float dCorpo = (morto.Pos - corpoAntes).Length;
		float dCabeca = (raio2.Pos - cabecaAntes).Length;

		// (2026-09-07: a cabeca que nao pode levar o corpo PARA NA FRENTE dele e o moi -- ela nao o
		//  atravessa nem o empurra. O que a injecao continua provando e que o CORPO nao anda.)
		AfirmarPj("[injecao] com a vitima recusada pelo `PodeSerLevadoPeloFeixe`, a cabeca fica na frente dele (moendo) e o "
				  + "corpo NAO -- a medida da familia 9 mede o arrasto, e nao 'corpo que se mexeu'",
				  (dCabeca > 1f || raio2.Encostado) && dCorpo < 0.5f,
				  $"corpo {dCorpo:0.0} px, cabeca {dCabeca:0.0} px, moendo {raio2.Encostado}");
	}

	/// <summary>
	/// UM TIQUE DO SERVIDOR, NA ORDEM DO SERVIDOR: o empurrao escorre o prazo e manda a correcao, e
	/// SO ENTAO os projeteis empurram de novo. Inverter aqui mediria uma ordem que o jogo nao roda --
	/// e a ordem e justamente o que decide se o prazo do arrasto fecha ou nao.
	/// </summary>
	private void UmTiqueDeArrasto()
	{
		TickDoEmpurrao();
		TickDosProjeteis(Protocol.TickSeconds);
	}

	/// <summary>
	/// UM RAIO NA MAO, sem passar pela carga -- a familia 3 ja mede o canal, e o que se mede aqui e o
	/// arrasto.
	///
	/// `Canalizando` na unha porque um beam nascido pelo <c>Disparar</c> tem cauda EM CIMA da cabeca:
	/// sem o bit, o passo 3(c) do `AndarProjetil` o mata de `Cessou` no primeiro tique, antes de ele
	/// chegar em ninguem. E `Deflectivel = false` porque um sorteio no meio da medicao mediria o dado.
	/// </summary>
	/// <param name="velocidade">O `speed` do raio: 1 = o raio comum; 0,5 = o raio PESADO (a do Massive Beam).</param>
	/// <param name="alcanceTiles">
	/// Ate onde ele vai. Quarenta tiles e "de sobra" pras cenas que medem o impacto; as que medem o FIM do alcance
	/// (onde o raio estoura, e ate onde ele leva o mais fraco) pedem um que caiba no corredor delas.
	/// </param>
	private Projetil RaioDaBancada(ServerPlayer pl, double baseDano, double velocidade = 1, double alcanceTiles = 40)
	{
		Projetil raio = Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam, BaseDano = baseDano, Velocidade = velocidade,
			AlcanceTiles = alcanceTiles, Deflectivel = false, Nome = "Onda de Ki",
		});
		raio.Canalizando = true;
		return raio;
	}

	// =====================================================================
	// OS CORPOS DA BANCADA
	// =====================================================================
	/// <summary>
	/// Um corpo de bancada na zona da Terra. Mesmo padrao do `GameServer.SolTeste`: entra no
	/// `_players` e na `ZoneList` porque as funcoes de producao varrem as duas, e sai no
	/// <see cref="LimparTudoDaBancada"/> -- tudo dentro do mesmo bloco sincrono, entao nenhum tique
	/// do jogo de verdade o enxerga.
	/// </summary>
	private ServerPlayer Forjar(string nome, Vec2 onde, double bp)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDeProjetil + _pjCorpos++,
			Peer = null,
			Name = nome,
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = ZonaDaBancadaDeProjetil,
			Pos = onde,
			Conta = "bancada_projetil",
			Slot = 0,
			Ficha = new Fighter { Race = "Human", BP = bp },

			// O LIVRO VAZIO, e nao nulo. Um corpo de verdade sempre tem um (o login e o clone criam o
			// deles), e sem ele a bancada mediria um caso que so a bancada produz -- foi assim que a
			// bancada da colisao de ki achou um `NullReferenceException` no `TecnicasDe`. A guarda de
			// nulo ficou LA, que e onde ela protege o jogo; aqui o corpo so nasce inteiro.
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;

		// `PorNoMundo` chama `Statify` (os ATRIBUTOS) e nao `powerlevel()` (o PODER). Sem esta linha
		// o `expressedBP` nasce ZERO, e o `BpModulus` da cadeia de ki -- que e uma RAZAO entre
		// poderes -- devolveria o mesmo numero pra todo mundo. Mesma pegadinha da bancada do sol.
		novo.Ficha.Tick(agoraMs: NowMs());
		return novo;
	}

	// =====================================================================
	// 11) A CABECA FICA NA FRENTE DE QUEM ELA ACERTA (dono, 2026-09-07)
	// =====================================================================
	/// <summary>
	/// *"atualmente a cabeca do beam fica SOBRE a pessoa e nao NA FRENTE dela a empurrando e dando
	/// dano"* -- com a foto. Mede-se a projecao (corpo - cabeca) no rumo do feixe: tem que dar
	/// `Feixe.DistanciaDeContato`, no impacto e depois dele; e o defeito injetado (a cabeca fica onde
	/// encostou) tem que ficar vermelho.
	///
	/// NOS DOIS DEGRAUS EM QUE O RAIO FERE (`KiContraCorpo`): o MAIS FRACO, que a cabeca leva empurrando
	/// pela frente, e o IGUAL, em quem ela para e fica moendo. (O terceiro -- a parede, em quem ela bate
	/// sem ferir -- e medido pela familia 16, com o corpo andando raio adentro.)
	/// </summary>
	private void ACabecaFicaNaFrenteDeQuemAcerta()
	{
		GD.Print("[projetil] -- 11) A CABECA FICA NA FRENTE DE QUEM ELA ACERTA, E NAO EM CIMA");
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;
		Vec2 raia = CorredorSeco(30);   // uma faixa pra familia: cada cena limpa a bancada e nasce de novo nela

		// O MAIS FRACO (arrasto): a cabeca leva o corpo e continua na frente dele o tempo todo.
		(_, ServerPlayer vitima, Projetil raio) = FeixeNoMaisFraco("Levado", raia, tiles: 6);
		int t = 0;
		while (raio.Vivo && raio.Arrastando == 0 && t++ < 300) UmTiqueDeArrasto();
		float NaFrente() => (vitima.Pos.X - raio.Pos.X) * raio.Rumo.X + (vitima.Pos.Y - raio.Pos.Y) * raio.Rumo.Y;

		AfirmarPj("ao encostar, a cabeca para NA FRENTE do corpo: o centro dela a `DistanciaDeContato` do centro dele",
				  raio.Arrastando == vitima.Id && Math.Abs(NaFrente() - Feixe.DistanciaDeContato) < 1f,
				  $"levando {raio.Arrastando}, {NaFrente():0.0} px a frente (esperado {Feixe.DistanciaDeContato})");
		float pior = 0;
		Vec2 deOnde = vitima.Pos;
		for (int i = 0; i < 12 && raio.Arrastando == vitima.Id; i++)
		{
			UmTiqueDeArrasto();
			pior = Math.Max(pior, Math.Abs(NaFrente() - Feixe.DistanciaDeContato));
		}
		AfirmarPj("...e continua na frente enquanto EMPURRA (o corpo anda o mesmo delta que a cabeca)",
				  pior < 1f && (vitima.Pos - deOnde).Length > T,
				  $"desvio maximo {pior:0.00} px em 12 tiques, com o corpo levado {(vitima.Pos - deOnde).Length:0.0} px");

		// O IGUAL (nao e levado): a cabeca para na frente dele e FICA ali, moendo -- ciclo apos ciclo.
		LimparTudoDaBancada();
		ServerPlayer atira = Forjar("Feixe do igual", raia, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		vitima = Forjar("Plantado", raia + new Vec2(6 * T, 0), bp: BpDoIgualDaEscada);
		raio = RaioDaBancada(atira, baseDano: BaseQueFereUmIgual(atira));
		t = 0;
		while (raio.Vivo && !raio.Encostado && t++ < 300) UmTiqueDeArrasto();
		pior = Math.Abs(NaFrente() - Feixe.DistanciaDeContato);
		for (int i = 0; i < 12 && raio.Vivo; i++)
		{
			UmTiqueDeArrasto();
			pior = Math.Max(pior, Math.Abs(NaFrente() - Feixe.DistanciaDeContato));
		}
		AfirmarPj("em quem NAO e levado (o igual) a cabeca tambem para na frente -- e fica ali, moendo, sem entrar no corpo",
				  raio.Vivo && raio.Encostado && raio.Arrastando == 0 && pior < 1f,
				  $"encostado {raio.Encostado}, levando {raio.Arrastando}, desvio maximo {pior:0.00} px em 12 tiques");

		// DE PERTO: o instante do impacto tambem planta a cabeca na frente.
		LimparTudoDaBancada();
		atira = Forjar("Feixe de perto", raia, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		vitima = Forjar("Batido", raia + new Vec2(2 * T, 0), bp: BpDoIgualDaEscada);
		raio = RaioDaBancada(atira, baseDano: BaseQueFereUmIgual(atira));
		t = 0;
		while (raio.Vivo && !raio.Encostado && t++ < 60) UmTiqueDeArrasto();
		AfirmarPj("no impacto de perto tambem: a cabeca para na frente do corpo",
				  raio.Encostado && Math.Abs(NaFrente() - Feixe.DistanciaDeContato) < 1f,
				  $"{NaFrente():0.0} px a frente");

		// O DEFEITO INJETADO: a cabeca fica onde encostou -- em cima do corpo, a foto do dono.
		LimparTudoDaBancada();
		Feixe.CabecaEmCimaDeTeste = true;
		try
		{
			(_, vitima, raio) = FeixeNoMaisFraco("Coberto", raia, tiles: 6);
			t = 0;
			while (raio.Vivo && raio.Arrastando == 0 && t++ < 300) UmTiqueDeArrasto();
			float emCima = NaFrente();
			// Sem o plantio a cabeca fica onde o SUB-PASSO a deixou ao encostar -- mais perto que a distancia
			// de contato, e num lugar que depende do passo (16 px) e nao da regra. E o que a foto mostrava.
			AfirmarPj("DEFEITO INJETADO (cabeca em cima) fica vermelho: a cabeca mais perto do corpo que a distancia de contato",
					  raio.Arrastando == vitima.Id && emCima < Feixe.DistanciaDeContato - 1f, $"{emCima:0.0} px a frente");
		}
		finally { Feixe.CabecaEmCimaDeTeste = false; }
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 12) O TRONCO SE CORTA ONDE ALGUEM ENCOSTA (dono, 2026-09-07)
	// =====================================================================
	/// <summary>
	/// *"caso um jogador encoste no tronco de um beam e nao necessariamente na cabeca dele, o beam vai
	/// ser cortado e a cabeca nova do beam vai colidir com essa pessoa, e a outra parte do beam que foi
	/// cortada vai continuar normalmente"*. E o `Crossed(mob)` do DM (`objects.dm:156-172`) num feixe
	/// que e um objeto so -- ver `GameServer.Feixe.cs`. Com os tres casos que NAO cortam e o defeito
	/// injetado. E com alguem NA PONTA da cabeca velha: a parte de la continua levando-o, no mesmo passo
	/// (<see cref="OCorteNaoMudaOPassoDeQuemELevado"/>).
	///
	/// A CABECA NOVA BATE EM QUEM PISOU PELA ESCADA DE TODO IMPACTO (`KiContraCorpo`, o `Acertar` que o corte
	/// chama): o IGUAL e moido no lugar (a cena principal), o MAIS FRACO e levado por ela (logo depois), e em
	/// quem e PAREDE ela para sem ferir (a familia 16).
	/// </summary>
	private void OTroncoSeCortaOndeAlguemEncosta()
	{
		GD.Print("[projetil] -- 12) O TRONCO SE CORTA ONDE ALGUEM ENCOSTA (a parte de la segue, a de ca ganha cabeca nova)");
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;

		Vec2 raia = CorredorSeco(40);   // uma faixa pra familia: cada cena limpa a bancada e nasce de novo nela
		ServerPlayer atira = Forjar("Feixe", raia, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		Projetil raio = RaioDaBancada(atira, baseDano: BaseQueFereUmIgual(atira));
		for (int i = 0; i < 25; i++) UmTiqueDeArrasto();
		List<Projetil> lista = ProjeteisDaZona(atira.Zone.Hash);
		float cabecaAntes = raio.Pos.X;
		AfirmarPj("(o raio andou e tem tronco: um feixe so na zona)",
				  lista.Count == 1 && raio.AndouTiles > 6, $"{raio.AndouTiles:0.0} tiles, {lista.Count} feixe(s)");

		// ALGUEM PISA NO TRONCO, a 4 tiles da mao -- longe da cabeca. Um IGUAL: o degrau que apanha e fica.
		ServerPlayer x = Forjar("Pisou", raia + new Vec2(4 * T, 0), bp: BpDoIgualDaEscada);
		double vidaX = x.Combate!.Corpo.Vida();
		UmTiqueDeArrasto();

		Projetil? deLa = lista.FirstOrDefault(q => q.NascidoDoCorte == raio.Id);
		AfirmarPj("o feixe foi CORTADO: nasceu uma parte de la, marcada como nascida deste corte",
				  deLa != null && lista.Count == 2, $"{lista.Count} feixe(s)");
		AfirmarPj("a parte de CA ganhou cabeca nova NA FRENTE de quem pisou (`DistanciaDeContato`)",
				  Math.Abs((x.Pos.X - raio.Pos.X) - Feixe.DistanciaDeContato) < 1f && Math.Abs(raio.Pos.Y - x.Pos.Y) < 1f,
				  $"cabeca em {raio.Pos}, corpo em {x.Pos}");
		// O `RaioDaBancada` nao passa pelo canal (a familia 3 e quem mede o canal): o que se afirma aqui e
		// que a parte de ca continua sendo a ALIMENTADA -- canalizando, com a cauda na boca do cano do dono.
		AfirmarPj("...e continua sendo o raio alimentado pela mao do dono (canalizando, a cauda na boca do cano)",
				  raio.Canalizando && (raio.Cauda - BocaDeCano.De(atira.Pos, raio.Rumo)).Length < 1f,
				  $"canalizando {raio.Canalizando}, cauda {raio.Cauda}, boca {BocaDeCano.De(atira.Pos, raio.Rumo)}");
		float esperado = (raio.Pos - raio.Cauda).Length / T;
		AfirmarPj("...e o alcance dela voltou o que a cabeca recuou (`AndouTiles` = da mao ate a cabeca nova)",
				  Math.Abs(raio.AndouTiles - esperado) < 0.05, $"andou {raio.AndouTiles:0.00} tiles, mao->cabeca {esperado:0.00}");
		AfirmarPj("...e quem pisou LEVOU a cabeca nova (o `Bump` do DM): esta sendo moido no lugar, e perdeu vida",
				  raio.Encostado && raio.Arrastando == 0 && x.Combate.Corpo.Vida() < vidaX,
				  $"encostado {raio.Encostado}, levando {raio.Arrastando}, vida {vidaX:0.###} -> {x.Combate.Corpo.Vida():0.###}");

		if (deLa != null)
		{
			// A CABECA VELHA ANDOU UM TIQUE antes do corte (o avanco vem antes do corte no tique): ate um
			// tique de raio a frente de onde ela estava, e nunca atras.
			AfirmarPj("a parte de LA nasceu do outro lado do corpo, com a cabeca velha na ponta",
					  deLa.Pos.X >= cabecaAntes && deLa.Pos.X - cabecaAntes < T
					  && Math.Abs(deLa.Cauda.X - (x.Pos.X + Feixe.MeioCorpo + Feixe.AlcanceDaCabeca(deLa))) < 1f,
					  $"cabeca {deLa.Pos.X:0.0} (velha {cabecaAntes:0.0}), cauda {deLa.Cauda.X:0.0}, corpo {x.Pos.X:0.0}");
			float comprimento = deLa.Comprimento, cabecaDeLa = deLa.Pos.X;
			for (int i = 0; i < 6; i++) UmTiqueDeArrasto();
			AfirmarPj("...e SEGUE NORMALMENTE: a cabeca avanca e o comprimento fica (o trem solto do DM)",
					  deLa.Vivo && deLa.Pos.X > cabecaDeLa + T && Math.Abs(deLa.Comprimento - comprimento) < 2f,
					  $"cabeca {cabecaDeLa:0} -> {deLa.Pos.X:0}, comprimento {comprimento:0} -> {deLa.Comprimento:0}");
			AfirmarPj("...sem ninguem a alimentar (ela nao e o raio do canal)", !deLa.Canalizando);
			AfirmarPj("...e a parte de CA nao se corta de novo em quem ja esta na frente da cabeca dela",
					  lista.Count == 2, $"{lista.Count} feixe(s)");
		}

		// (o MAIS FRACO que pisa no tronco) o corte e o mesmo -- e a cabeca nova, em vez de moer no lugar, o LEVA.
		LimparTudoDaBancada();
		atira = Forjar("Feixe do fraco", raia, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		raio = RaioDaBancada(atira, baseDano: BaseQueFereUmIgual(atira));
		for (int i = 0; i < 25; i++) UmTiqueDeArrasto();
		lista = ProjeteisDaZona(atira.Zone.Hash);
		ServerPlayer fraco = Forjar("Pisou e foi", raia + new Vec2(4 * T, 0), bp: BpDoMaisFracoDaEscada);
		UmTiqueDeArrasto();
		bool cortouNoFraco = lista.Count == 2 && raio.Arrastando == fraco.Id;
		Vec2 ondePisou = fraco.Pos;
		for (int i = 0; i < 6; i++) UmTiqueDeArrasto();
		AfirmarPj("o MAIS FRACO que pisa no tronco tambem corta o feixe -- e a cabeca nova o LEVA, na frente dela (a escada vale no corte)",
				  cortouNoFraco && raio.Arrastando == fraco.Id && (fraco.Pos - ondePisou).Length > T
				  && Math.Abs((fraco.Pos.X - raio.Pos.X) - Feixe.DistanciaDeContato) < 1f,
				  $"{lista.Count} feixe(s), levando {raio.Arrastando}, o corpo andou {(fraco.Pos - ondePisou).Length:0.0} px, cabeca {fraco.Pos.X - raio.Pos.X:0.0} px atras dele");

		// (a) UM CORPO NA FRENTE DA CABECA nao corta: e o impacto de sempre, sem feixe novo.
		LimparTudoDaBancada();
		atira = Forjar("Feixe2", raia, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		raio = RaioDaBancada(atira, baseDano: BaseQueFereUmIgual(atira));
		for (int i = 0; i < 25; i++) UmTiqueDeArrasto();
		lista = ProjeteisDaZona(atira.Zone.Hash);
		ServerPlayer y = Forjar("NaCabeca", raio.Pos + raio.Rumo * (Feixe.DistanciaDeContato + 4f), bp: BpDoIgualDaEscada);
		for (int i = 0; i < 3; i++) UmTiqueDeArrasto();
		AfirmarPj("quem esta na FRENTE da cabeca leva o impacto de sempre -- nao corta, nao nasce feixe",
				  lista.Count == 1 && raio.Encostado && raio.Arrastando == 0, $"{lista.Count} feixe(s), encostado {raio.Encostado}, levando {raio.Arrastando}");

		// (b) O DONO NAO CORTA O PROPRIO FEIXE -- nem um feixe solto que passe por cima dele.
		LimparTudoDaBancada();
		atira = Forjar("Feixe3", raia, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		raio = RaioDaBancada(atira, baseDano: BaseQueFereUmIgual(atira));
		for (int i = 0; i < 25; i++) UmTiqueDeArrasto();
		lista = ProjeteisDaZona(atira.Zone.Hash);
		raio.Canalizando = false;                       // solto: a cauda deixa de ser a mao
		atira.Pos = raio.Cauda + raio.Rumo * (2 * T);   // o dono em cima do proprio tronco
		UmTiqueDeArrasto();
		AfirmarPj("o DONO em cima do proprio tronco nao corta nada", lista.Count == 1, $"{lista.Count} feixe(s)");

		// (c) O FEIXE EM DISPUTA nao se corta: o tronco e o cabo de guerra.
		LimparTudoDaBancada();
		(ServerPlayer da, ServerPlayer db, DisputaDeKi? disputa) = DoisRaiosDeFrente(12);
		_ = db;
		lista = ProjeteisDaZona(da.Zone.Hash);
		int antesDaDisputa = lista.Count;
		if (disputa?.A.Feixe is { } fa)
		{
			ServerPlayer w = Forjar("NoCabo", fa.Cauda + fa.Rumo * (2 * T), bp: BpDoIgualDaEscada);
			_ = w;
			UmTiqueDoEncontro();
			AfirmarPj("um corpo no tronco de um feixe EM DISPUTA nao corta: a disputa e quem manda nele",
					  lista.Count == antesDaDisputa && _emEmbateDeKi.ContainsKey(da.Id), $"{antesDaDisputa} -> {lista.Count} feixe(s)");
		}
		else AfirmarPj("(a disputa da familia 12c comecou)", false);
		LimparEmbatesDaBancada();

		// (d) O DEFEITO INJETADO: com o corte desligado, pisar no tronco nao faz nada.
		Feixe.SemCorteDeTeste = true;
		try
		{
			atira = Forjar("Feixe4", raia, bp: BpDoFeixeDaEscada);
			atira.Facing = Facing.East;
			raio = RaioDaBancada(atira, baseDano: BaseQueFereUmIgual(atira));
			for (int i = 0; i < 25; i++) UmTiqueDeArrasto();
			lista = ProjeteisDaZona(atira.Zone.Hash);
			Forjar("PisouEmVao", raia + new Vec2(4 * T, 0), bp: BpDoIgualDaEscada);
			UmTiqueDeArrasto();
			AfirmarPj("DEFEITO INJETADO (corte desligado) fica vermelho: pisar no tronco nao corta e a cabeca segue longe",
					  lista.Count == 1 && raio.AndouTiles > 6, $"{lista.Count} feixe(s), cabeca a {raio.AndouTiles:0.0} tiles");
		}
		finally { Feixe.SemCorteDeTeste = false; }

		// (e) O CORTE COM ALGUEM NA PONTA: a parte de la continua levando o corpo -- e no MESMO passo.
		OCorteNaoMudaOPassoDeQuemELevado(raia);
		LimparTudoDaBancada();
	}

	/// <summary>
	/// O CORTE NAO MUDA O PASSO DE QUEM A CABECA LEVA. A parte de la de um corte herda quem a cabeca velha levava -- e a
	/// velocidade em que o levava: o `Projetil.FatorDoArrasto`, que e do par (tiro, corpo) e nao da cabeca.
	///
	/// ============================ O QUE FOI MEDIDO (2026-10-12) ============================
	/// Com a cabeca levando o mais fraco a 12,8 px por tique (fator 0,6) e outro corpo pisando no tronco, o levado
	/// passou a andar 21,3 px por tique -- a velocidade DO FEIXE -- e so voltou aos 12,8 depois da batida seguinte. O
	/// `Cortar` copiava pra parte de la quem ela levava e o relogio da batida, mas nao o fator: ela nascia com o padrao
	/// (1), e quem o reescreve e o `Acertar` da batida seguinte.
	/// =======================================================================================
	///
	/// A REGUA E O PASSO DE ANTES DO CORTE, medido na mesma cena: nos tiques entre o corte e a batida seguinte o corpo
	/// tem de andar o que andava. E O INTRUSO PISA LOGO DEPOIS DE UMA BATIDA, de proposito: ate a seguinte sobram
	/// quatro tiques limpos. Pisando na vespera dela o defeito duraria um tique so, e a cena mediria em que tique o
	/// intruso pisou.
	/// </summary>
	private void OCorteNaoMudaOPassoDeQuemELevado(Vec2 raia)
	{
		PassoNoCorte m = PassoDeQuemELevadoNoCorte(raia);
		GD.Print($"[projetil]      (medido) corte com alguem na ponta: o levado andava {m.Antes:0.0} px por tique (o feixe anda {m.DoFeixe:0.0}); "
				 + $"nos {m.Tiques} tique(s) entre o corte e a batida seguinte, de {m.MenorDepois:0.0} a {m.MaiorDepois:0.0}");

		AfirmarPj("PREPARO: a cabeca leva o mais fraco no passo do ARRASTO (o do feixe vezes o fator do par) quando outro corpo pisa no tronco -- "
				  + "e o corte acontece: a parte de la nasce levando o MESMO corpo",
				  m.Cortou && m.AParteDeLaLeva && Math.Abs(m.Antes - m.DoArrasto) < 0.1f && m.DoArrasto < m.DoFeixe * 0.75f,
				  $"cortou {m.Cortou}, a parte de la leva o corpo {m.AParteDeLaLeva}; passo de antes {m.Antes:0.00} px (arrasto {m.DoArrasto:0.00}, feixe {m.DoFeixe:0.00})");
		AfirmarPj($"O CORTE NAO MUDA O PASSO DE QUEM E LEVADO: nos {TiquesLimposDepoisDoCorte} tiques entre o corte e a batida seguinte o corpo anda o que andava "
				  + "(a parte de la herda o `FatorDoArrasto` junto com quem ela leva)",
				  m.Tiques == TiquesLimposDepoisDoCorte && Math.Abs(m.MaiorDepois - m.Antes) < 0.5f && Math.Abs(m.MenorDepois - m.Antes) < 0.5f,
				  $"{m.Tiques} tique(s): de {m.MenorDepois:0.00} a {m.MaiorDepois:0.00} px, contra {m.Antes:0.00} antes do corte");

		CortePerdeOFatorDoArrastoDeTeste = true;
		try
		{
			PassoNoCorte d = PassoDeQuemELevadoNoCorte(raia);
			AfirmarPj("(defeito injetado: o corte perde o fator do arrasto) o levado dispara na velocidade DO FEIXE ate a batida seguinte -- a regua de cima fica vermelha",
					  d.Cortou && d.AParteDeLaLeva && d.MaiorDepois > d.Antes + 0.5f && Math.Abs(d.MaiorDepois - d.DoFeixe) < 0.5f,
					  $"cortou {d.Cortou}; depois do corte ate {d.MaiorDepois:0.00} px por tique, contra {d.Antes:0.00} antes (o feixe anda {d.DoFeixe:0.00})");
		}
		finally { CortePerdeOFatorDoArrastoDeTeste = false; }
	}

	/// <summary>
	/// QUANTOS TIQUES LIMPOS SOBRAM ENTRE O CORTE E A BATIDA SEGUINTE quando o intruso pisa logo depois de uma batida: o
	/// ciclo de moer (`Projetil.SegundosPorCicloDeBeam`, seis tiques de servidor) menos o tique do corte e o da batida
	/// que vem.
	/// </summary>
	private static readonly int TiquesLimposDepoisDoCorte = (int)Math.Round(Projetil.SegundosPorCicloDeBeam / Protocol.TickSeconds) - 2;

	/// <summary>O passo de quem a cabeca leva, antes e depois de um corte do tronco -- em pixels por tique de servidor.</summary>
	/// <param name="DoFeixe">O que a cabeca anda num tique sem levar ninguem.</param>
	/// <param name="DoArrasto">O do feixe vezes o fator do par (`KiContraCorpo.FatorDoArrasto`).</param>
	/// <param name="Antes">O maior passo do levado nos doze tiques de antes do corte.</param>
	/// <param name="Tiques">Quantos tiques foram medidos depois do corte, com a parte de la levando o corpo.</param>
	private readonly record struct PassoNoCorte(
		bool Cortou, bool AParteDeLaLeva, float DoFeixe, float DoArrasto, float Antes, float MenorDepois, float MaiorDepois, int Tiques);

	/// <summary>
	/// A CENA DO CORTE COM ALGUEM NA PONTA, do disparo a ultima medida: o raio comum pega o mais fraco a seis tiles, leva-o
	/// por doze tiques, espera a batida seguinte e so entao um IGUAL pisa no tronco, quatro tiles atras da cabeca. O
	/// tique seguinte corta; os de depois sao a medida.
	/// </summary>
	private PassoNoCorte PassoDeQuemELevadoNoCorte(Vec2 raia)
	{
		const int T = ZoneCollision.TileSize;
		LimparTudoDaBancada();

		(ServerPlayer atira, ServerPlayer levado, Projetil raio) = FeixeNoMaisFraco("Levado no corte", raia, tiles: 6);
		for (int i = 0; i < 300 && raio.Vivo && raio.Arrastando == 0; i++) UmTiqueDeArrasto();
		float doFeixe = (float)(T / raio.SegundosPorTile * Protocol.TickSeconds);
		float doArrasto = doFeixe * (float)KiContraCorpo.FatorDoArrasto(KiContraCorpo.Razao(levado.Ficha.expressedBP, raio.Bp), defendendo: false);
		if (raio.Arrastando != levado.Id) return new PassoNoCorte(false, false, doFeixe, doArrasto, 0, 0, 0, 0);

		// A CABECA SE AFASTA DA MAO (o tronco precisa de comprimento pra alguem pisar nele longe da cabeca), e o passo
		// do levado e medido no caminho.
		float antes = 0;
		for (int i = 0; i < 12; i++) antes = MathF.Max(antes, PassoDoCorpoNumTique(levado));

		// ATE A PROXIMA BATIDA -- o tique em que o corpo perde vida.
		for (int i = 0; i < 12; i++)
		{
			double[] vida = VidaPorParte(levado);
			UmTiqueDeArrasto();
			if (FeridaMaisFunda(levado, vida) > 1e-9) break;
		}

		// ALGUEM PISA NO TRONCO, quatro tiles atras da cabeca: um IGUAL, que apanha e fica (a parte de ca para nele).
		List<Projetil> lista = ProjeteisDaZona(atira.Zone.Hash);
		Forjar("Pisou no arrasto", raio.Pos - raio.Rumo * (4 * T), bp: BpDoIgualDaEscada);
		UmTiqueDeArrasto();   // o tique do corte: a cabeca velha ainda anda, e so entao o tronco e partido
		Projetil? deLa = lista.FirstOrDefault(q => q.NascidoDoCorte == raio.Id);
		if (deLa == null) return new PassoNoCorte(false, false, doFeixe, doArrasto, antes, 0, 0, 0);
		bool leva = deLa.Arrastando == levado.Id;

		float menor = float.MaxValue, maior = 0;
		int tiques = 0;
		while (tiques < TiquesLimposDepoisDoCorte && deLa.Vivo && deLa.Arrastando == levado.Id)
		{
			float passo = PassoDoCorpoNumTique(levado);
			menor = MathF.Min(menor, passo);
			maior = MathF.Max(maior, passo);
			tiques++;
		}
		return new PassoNoCorte(true, leva, doFeixe, doArrasto, antes, tiques == 0 ? 0 : menor, maior, tiques);
	}

	/// <summary>Quanto este corpo andou num tique do servidor (`UmTiqueDeArrasto`), em pixels.</summary>
	private float PassoDoCorpoNumTique(ServerPlayer corpo)
	{
		Vec2 de = corpo.Pos;
		UmTiqueDeArrasto();
		return (corpo.Pos - de).Length;
	}

	// =====================================================================
	// 13) APANHAR DERRUBA O RAIO (fora de disputa)
	// =====================================================================
	/// <summary>
	/// *"caso o personagem seja atacado, agarrado ou qualquer coisa desse tipo, ele cancela o beam
	/// dele na hora"*. O tiro e o soco, pelos caminhos de producao (`Acertar` e `Atacar`), com o raio
	/// ainda VIVO quando o canal cai -- senao a medida seria o raio morrendo de velho. O agarrao e a
	/// disputa tem familia propria na `--embatekiteste` (10).
	/// </summary>
	private void ApanharDerrubaORaio()
	{
		GD.Print("[projetil] -- 13) APANHAR DERRUBA O RAIO: tiro e soco, fora de disputa");
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;

		// UM TIRO DE KI de outro derruba o canal. De um IGUAL: o tiro tem que ser GOLPE em quem canaliza -- em quem e
		// parede pra ele (quatro vezes mais forte) ele estoura sem ferir e nao derruba nada (a familia 16 mede).
		Vec2 raia = CorredorSeco(20);
		ServerPlayer canal = Forjar("Canalizador", raia + new Vec2(4 * T, 0), bp: BpDoIgualDaEscada);
		canal.Facing = Facing.East;
		canal.Ficha.Ki = canal.Ficha.MaxKi;
		ServerPlayer atirador = Forjar("Bolista", raia, bp: BpDoFeixeDaEscada);
		atirador.Facing = Facing.East;
		Canalizar(canal, "Ki_Wave", 10 * canal.Ficha.BaseDrain(), SemDeflexao());
		for (int i = 0; i < 40; i++) { TickDosCanaisDeKi(Protocol.TickSeconds); TickDosProjeteis(Protocol.TickSeconds); }
		Projetil? raioDele = _canais.GetValueOrDefault(canal.Id)?.Raio;
		AfirmarPj("(o canalizador esta com o raio na mao)", raioDele is { Vivo: true } && _canais[canal.Id].Atirando);

		Projetil bola = Disparar(atirador, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = BaseQueFereUmIgual(atirador, TipoDeProjetil.Blast), Deflectivel = false, Nome = "bola de bancada",
		});
		int t = 0;
		bool vivoAoCair = false;
		while (bola.Vivo && t++ < 90)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
			if (!_canais.ContainsKey(canal.Id)) { vivoAoCair = raioDele is { Vivo: true }; break; }
		}
		AfirmarPj("uma bola de ki que ACERTA quem canaliza derruba o raio dele na hora (com o raio ainda vivo)",
				  bola.Fim == FimDeProjetil.Acertou && !_canais.ContainsKey(canal.Id) && vivoAoCair,
				  $"fim da bola {bola.Fim}, canal de pe {_canais.ContainsKey(canal.Id)}, raio vivo ao cair {vivoAoCair}");

		// E O SOCO: `Atacar` -> o mesmo funil. Soca todo tique; a recarga recusa os de mais.
		LimparTudoDaBancada();
		raia = CorredorSeco(20);
		canal = Forjar("Canalizador2", raia + new Vec2(2 * T, 0), bp: 200_000);
		canal.Facing = Facing.East;
		canal.Ficha.Ki = canal.Ficha.MaxKi;
		ServerPlayer socador = Forjar("Socador", raia + new Vec2(T, 0), bp: 200_000);
		socador.Facing = Facing.East;
		Canalizar(canal, "Ki_Wave", 10 * canal.Ficha.BaseDrain(), SemDeflexao());
		for (int i = 0; i < 40; i++) { TickDosCanaisDeKi(Protocol.TickSeconds); TickDosProjeteis(Protocol.TickSeconds); }
		raioDele = _canais.GetValueOrDefault(canal.Id)?.Raio;
		t = 0;
		vivoAoCair = false;
		while (t++ < 100 && _canais.ContainsKey(canal.Id))
		{
			Atacar(socador, Protocol.Golpe.Leve);
			TickCombate(Protocol.TickSeconds);
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
			if (!_canais.ContainsKey(canal.Id)) vivoAoCair = raioDele is { Vivo: true };
		}
		AfirmarPj("um soco que encosta em quem canaliza derruba o raio dele (com o raio ainda vivo)",
				  !_canais.ContainsKey(canal.Id) && vivoAoCair, $"em {t} tiques; raio vivo ao cair {vivoAoCair}");
		LimparTudoDaBancada();
	}

	/// <summary>Tira da bancada tudo que ela pos no mundo. Chamado tambem entre familias.</summary>
	private void LimparTudoDaBancada(List<ServerPlayer>? manter = null)
	{
		foreach (int id in _canais.Keys.ToList())
			if (id >= IdBaseDeProjetil) SoltarDoRaio(id);

		List<Projetil> lista = ProjeteisDaZona(ZonaDaBancadaDeProjetil.Hash);
		_projeteisVivos -= lista.Count;
		lista.Clear();

		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			if (pl.Id < IdBaseDeProjetil) continue;
			if (manter != null && manter.Contains(pl)) continue;
			EsquecerTecnicas(pl.Id);   // o mesmo esquecimento do logout: recarga, carga, ancora -- nada sobrevive ao corpo
			_players.Remove(pl.Id);
			ZoneList(pl.Zone.Hash).Remove(pl);
		}
	}
}
