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
///     que FOGE, e o raio saindo de uma carga com rastro que cresce e depois se esvazia.
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
///  8. O RAIO LEVA QUEM ACERTA, na velocidade DELE -- com o muro, a agua, a queima-roupa, o boneco
///     largado e a disputa de ki como contra-exemplos.
///  9. E ELA SABE REPROVAR (familia 10): cinco defeitos plantados no rastro medido, cada um obrigado
///     a deixar VERMELHA a regra que leva o nome dele, mais duas injecoes no estado de PRODUCAO do
///     arrasto. Sem esta familia, as oito de cima sao afirmacoes sobre si mesmas.
/// =========================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Faixa de ids desta bancada -- longe do `_nextId`, do convivio (90.100) e do sol (90.400).</summary>
	private const int IdBaseDeProjetil = 90_800;

	/// <summary>A zona da bancada: a Terra, porque ela tem colisao de verdade no catalogo.</summary>
	private static readonly ZoneKey ZonaDaBancadaDeProjetil = new(ZoneKey.KindPremade, "Earth");

	private int _pjOk, _pjFalhou, _pjCorpos;

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
			ABancadaSeCobra();
		}
		finally
		{
			LimparTudoDaBancada();
		}

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

		Projetil p = Disparar(atirador, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 20,
		});

		// A BOCA DO CANO, E NAO O UMBIGO. Ate aqui esta linha cobrava `p.Pos == atirador.Pos`, e era
		// ela que ficava verde com o defeito que o dono fotografou: o quadro de 32x32 da cabeca,
		// carimbado CENTRADO no centro do sprite, tapava o personagem inteiro. O DM da `step(A,A.dir)`
		// no mesmo tique do nascimento -- ver `BocaDeCano`.
		AfirmarPj("a bola nasceu viva e na BOCA DO CANO -- um tile a frente, nunca em cima do corpo",
				  p.Vivo && p.Pos.Equals(BocaDeCano.De(atirador.Pos, MeleeArea.Frente(Facing.East))),
				  $"{p.Pos} vs corpo em {atirador.Pos}");

		double esperadoPorTile = Projetil.AtrasoDeBola(1);
		AfirmarPj("o atraso por tile e o do DM (`max(1, round(4-speed))` tiques)",
				  Math.Abs(p.SegundosPorTile - esperadoPorTile) < 1e-9,
				  $"{p.SegundosPorTile:0.###} vs {esperadoPorTile:0.###}");

		// UM SEGUNDO DE VOO. A distancia esperada sai da MESMA constante, nunca de um literal.
		float antes = p.Pos.X;
		for (int i = 0; i < 30; i++) TickDosProjeteis(Protocol.TickSeconds);
		float andou = p.Pos.X - antes;
		float previsto = (float)(ZoneCollision.TileSize / p.SegundosPorTile);

		AfirmarPj("em 1 s ela anda o que a formula do DM manda",
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

		Projetil tiro = Disparar(atirador, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 6,
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
	/// GUIDED: `walk_towards` -- corrige o rumo TODO tique, sem limite de angulo. A prova nao pode
	/// ser "acertou um alvo parado" (uma bola reta faria isso): o alvo TEM que sair da linha, e o
	/// tiro tem que virar atras dele.
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

		Projetil p = Disparar(atirador, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Guided, BaseDano = 15, Velocidade = 1, AlcanceTiles = 60,
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
	/// BEAM: carrega, sai, o rastro CRESCE enquanto o dono segura, e quando ele solta o rastro se
	/// ESVAZIA em vez de sumir de uma vez -- que e o trem de segmentos do DM visto de longe.
	/// </summary>
	private void ORaioCarregaCanalizaEDesfaz()
	{
		GD.Print("[projetil] -- 3) O RAIO: CARGA, CANAL E O RASTRO QUE SE ESVAZIA");

		Vec2 raia = CorredorLivre(28);
		ServerPlayer pl = Forjar("Raiador", raia, bp: 50_000);
		pl.Facing = Facing.East;
		pl.Ficha.Ki = pl.Ficha.MaxKi;

		double kiAntes = pl.Ficha.Ki;
		var receita = new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam, BaseDano = 1, Velocidade = 1, AlcanceTiles = 30,
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

		// SOLTOU. A cabeca segue, a cauda corre atras, e o raio morre quando ela alcanca.
		SoltarCanal(pl, "Ki_Wave");
		AfirmarPj("soltar fecha o canal", !_canais.ContainsKey(pl.Id));
		AfirmarPj("...mas o raio que ja saiu CONTINUA no ar", raio.Vivo && !raio.Canalizando);

		// ============================ O RASTRO NAO ENCOLHE NO AR ============================
		// Esta afirmacao ja foi o CONTRARIO, e estava errada. No DM o beam e um trem de segmentos que
		// andam todos na mesma velocidade: solto, ele voa INTEIRO, com comprimento constante. O que
		// esvazia o rastro e a cabeca PARAR -- ela e o segmento mais velho e a primeira a esgotar o
		// alcance, e dali cada segmento de tras alcanca o ponto onde o da frente se apagou.
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

		// AGORA A CABECA PARA (o alcance acaba) e o rabo e engolido pra dentro do ponto de parada.
		Vec2 ondeParou = default;
		bool viuEsvaziar = false;
		for (int i = 0; i < 900 && raio.Vivo; i++)
		{
			TickDosProjeteis(Protocol.TickSeconds);
			if (!raio.Esvaziando || viuEsvaziar) continue;
			viuEsvaziar = true;
			ondeParou = raio.Pos;
		}
		AfirmarPj("...a cabeca PARA quando o alcance acaba e o rastro comeca a ser engolido", viuEsvaziar);
		AfirmarPj("...ate o raio se apagar sozinho", !raio.Vivo, $"fim = {raio.Fim}");
		AfirmarPj("...e a cauda terminou EM CIMA do ponto onde a cabeca parou",
				  viuEsvaziar && (raio.Cauda - ondeParou).Length < ZoneCollision.TileSize,
				  $"cauda {raio.Cauda}, parou em {ondeParou}");

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
	/// no maximo 4 sub-passos por tiro por tique (o raio mais rapido do jogo anda ~53 px por tique e o
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

		// O TETO, por aritmetica do sistema: teto de tiros da zona x sub-passos por tique.
		const int SubPassosPorTique = 4;   // ~53 px de passo maximo / 16 px de sub-passo
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
		new("...todas alinhadas ao CENTRO da celula, como as do arremesso",
			(s, _) => ForaDaGrade(s) == 0,
			s => $"{ForaDaGrade(s)} fora da grade"),

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

	private static int ForaDaGrade(IReadOnlyList<MarcaDeSulco> s)
	{
		const int T = ZoneCollision.TileSize;
		return s.Count(m => Math.Abs(m.Onde.X % T - T / 2f) > 0.5f
							|| Math.Abs(m.Onde.Y % T - T / 2f) > 0.5f);
	}

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
	// 9) O RAIO LEVA QUEM ACERTA
	// =====================================================================
	/// <summary>
	/// O ARRASTO -- *"ao ACERTAREM alguem eles deveriam EMPURRAR A PESSOA JUNTO conforme o beam vai
	/// indo"*.
	///
	/// ============================ O QUE ESTA FAMILIA TEM QUE PROVAR, E POR QUE ============================
	/// "O corpo se mexeu" e a afirmacao inutil aqui: o funil do ARREMESSO ja movia corpo, e a primeira
	/// versao imaginavel deste efeito (rearmar o `Arremessar` a cada ciclo) ficaria VERDE num teste
	/// desses -- e estaria errada, porque jogaria a vitima a 640 px/s contra os 320 px/s do proprio
	/// feixe: ela sairia da frente do raio e ele nunca mais a alcancaria. O pedido do dono e "conforme
	/// o beam vai indo", entao a medida que decide e **a VELOCIDADE**, comparada com a da cabeca no
	/// mesmo intervalo.
	///
	/// Depois dela vem o que o pedido nao diz e o codigo tem que decidir, e cada uma tem contra-exemplo
	/// proprio: perto ARREMESSA (nao carrega), o muro PARA os dois, a agua so para quem esta a pe, o
	/// boneco largado nao vai, o feixe que entra em disputa LARGA, e o corpo solto para SECO.
	///
	/// E a ULTIMA e a mais silenciosa: a CADENCIA DO DANO. Uma cabeca que anda encostada colidiria a
	/// cada sub-passo -- seis vezes o dano do DM, sem nada na tela avisando. Ela e medida por contagem
	/// de ciclos por segundo, e nao por "levou dano".
	/// ======================================================================================================
	/// </summary>
	private void ORaioLevaQuemAcerta()
	{
		GD.Print("[projetil] -- 9) O RAIO LEVA QUEM ACERTA");
		LimparTudoDaBancada();

		const int T = ZoneCollision.TileSize;
		Vec2 raia = CorredorSeco(30);
		ServerPlayer atira = Forjar("Feixe", raia, bp: 200_000);
		atira.Facing = Facing.East;
		// SEIS TILES: alem dos 4 do arremesso (`FatorDeEmpurrao`) e dentro dos 10 do arrasto.
		ServerPlayer vitima = Forjar("Levado", raia + new Vec2(6 * T, 0), bp: 200_000);

		Projetil raio = RaioDaBancada(atira, baseDano: 0.002);

		int t = 0;
		while (raio.Vivo && raio.Arrastando == 0 && t++ < 300) UmTiqueDeArrasto();

		AfirmarPj("o feixe PEGOU a vitima em vez de so bater e parar nela",
				  raio.Arrastando == vitima.Id, $"arrastando={raio.Arrastando} apos {t} tiques");
		if (raio.Arrastando != vitima.Id) { LimparTudoDaBancada(); return; }

		AfirmarPj("...e NAO a arremessou: os dois empurroes se excluem (um `if`/`else`, como no DU)",
				  vitima.TiquesDeVoo == 0);
		AfirmarPj("...o corpo perde as redeas pelo funil de VETOR de sempre (`PodeMexerOCorpo`)",
				  !PodeMexerOCorpo(vitima));
		AfirmarPj("...e pelo MESMO bit que o arremesso ja acendia, e nao por um segundo bit",
				  vitima.DirigidoPeloServidor && (vitima.Sheet().Estado & 32) != 0);

		// ---------- A MEDIDA QUE DECIDE: A VELOCIDADE ----------
		Vec2 corpoAntes = vitima.Pos, cabecaAntes = raio.Pos;
		for (int i = 0; i < 6 && raio.Arrastando != 0; i++) UmTiqueDeArrasto();
		float dCorpo = (vitima.Pos - corpoAntes).Length;
		float dCabeca = (raio.Pos - cabecaAntes).Length;

		AfirmarPj("o corpo anda EXATAMENTE o que a cabeca andou -- nem mais, nem menos",
				  dCabeca > 1f && MathF.Abs(dCorpo - dCabeca) < 0.5f,
				  $"corpo {dCorpo:0.0} px, cabeca {dCabeca:0.0} px");

		// E O NUMERO QUE EXPLICA POR QUE O FUNIL DO ARREMESSO NAO SERVIA PRO DESLOCAMENTO. Os dois
		// saem de constantes, e nao de valores digitados aqui: uma copia do "640" nesta bancada seria
		// a segunda casa do numero, que e o que a regra 4 proibe.
		double vFeixe = ZoneCollision.TileSize / raio.SegundosPorTile;
		double vArremesso = Empurrao.TilesPorTique * ZoneCollision.TileSize / Empurrao.SegundosPorTique;
		AfirmarPj("...e essa e a velocidade DO FEIXE, que nao e a do arremesso (por isso ele nao serviu)",
				  vArremesso > vFeixe * 1.5,
				  $"feixe {vFeixe:0} px/s contra arremesso {vArremesso:0} px/s");

		// ---------- A CADENCIA DO DANO NAO MUDOU ----------
		// ============================ NAO SE MEDE ISTO PELA FLAG ============================
		// A primeira versao contava as subidas de `Encostado` e leu ZERO em um segundo inteiro -- e o
		// codigo estava certo. `Encostado` cai e volta DENTRO do mesmo tique (o passo 4 o derruba, o
		// `Acertar` o levanta de volta no mesmo `AndarProjetil`), entao quem olha entre tiques nunca ve
		// a borda. Medir a flag e medir o observador.
		//
		// O que se conta agora e o EFEITO: quantos tiques do segundo tiveram perda de vida. O dano de
		// ki so entra por `Acertar`, e `Acertar` roda no maximo uma vez por tique por feixe -- entao
		// "tiques em que a vida caiu" E o numero de ciclos, e ele e observavel de fora sem hook nenhum.
		// ===================================================================================
		// E NAO SE MEDE EM "POR SEGUNDO", TAMBEM: o arrasto nao dura um segundo. Ele acaba nos 10
		// tiles da mao do dono (regra do DU), e daqui pra la sobram ~4 tiles a 10 tiles/s -- 0,4 s. Um
		// contador de janela fixa leu "2 num segundo" e reprovou um codigo certo, pela mesma familia
		// de erro da versao anterior: a bancada media a JANELA e nao o efeito. Aqui se mede a TAXA, com
		// a janela que o proprio efeito permitir.
		int ciclos = 0, rodou = 0;
		double vida = vitima.Combate!.Corpo.Partes.Sum(x => x.Vida);
		for (int i = 0; i < (int)Math.Round(1.0 / Protocol.TickSeconds) && raio.Arrastando != 0; i++)
		{
			UmTiqueDeArrasto();
			rodou++;
			double agora = vitima.Combate.Corpo.Partes.Sum(x => x.Vida);
			if (agora < vida) ciclos++;
			vida = agora;
		}

		double taxa = rodou > 0 ? ciclos / (rodou * Protocol.TickSeconds) : 0;
		double esperada = 1.0 / Projetil.SegundosPorCicloDeBeam;          // 5 Hz -- o `sleep(2)` do DM
		double porSubPasso = 1.0 / Protocol.TickSeconds;                  // ~30 Hz -- o defeito que se teme
		// OS NUMEROS MEDIDOS SAIEM SEMPRE, e nao so no detalhe de uma falha -- o mesmo habito da
		// familia 7. Uma bancada que so mostra a conta quando reprova obriga a quebrar o codigo pra
		// ler o que ele faz.
		GD.Print($"[projetil]      arrasto: corpo {dCorpo:0.0} px x cabeca {dCabeca:0.0} px em 6 tiques; "
				 + $"{vFeixe:0} px/s (o arremesso seria {vArremesso:0}); "
				 + $"{taxa:0.0} moidas/s em {rodou * Protocol.TickSeconds:0.00}s "
				 + $"(DM {esperada:0}; por sub-passo ~{porSubPasso:0})");

		AfirmarPj("...e mesmo ANDANDO encostado ele moi na cadencia do DM (o `sleep(2)`), e nao por sub-passo",
				  rodou > 5 && taxa >= esperada * 0.6 && taxa <= esperada * 1.4,
				  $"{taxa:0.0} moidas/s em {rodou * Protocol.TickSeconds:0.00}s "
				  + $"(o DM manda {esperada:0}; por sub-passo seriam ~{porSubPasso:0})");

		// ---------- O FIM DA CORDA: DEZ TILES ----------
		while (raio.Vivo && raio.Arrastando != 0 && t++ < 900) UmTiqueDeArrasto();
		AfirmarPj("o arrasto larga aos 10 tiles da mao do dono (`if(getdist(Owner,P)<10)`)",
				  raio.AndouTiles >= Projetil.TilesDeArrasto,
				  $"largou com {raio.AndouTiles:0.0} tiles andados");

		// ---------- LARGADO, PARA SECO ----------
		Vec2 parou = vitima.Pos;
		for (int i = 0; i < 10; i++) UmTiqueDeArrasto();
		AfirmarPj("largado, o corpo PARA SECO -- nao ha inercia guardada em lugar nenhum",
				  (vitima.Pos - parou).Length < 0.01f, $"{(vitima.Pos - parou).Length:0.00} px depois de solto");
		AfirmarPj("...e as redeas voltam sozinhas: o prazo escorreu, ninguem apagou bit nenhum",
				  vitima.ArrastoRestante <= 0 && !vitima.DirigidoPeloServidor);

		OPertoArremessaEmVezDeLevar();
		OMuroParaOsDoisEOFeixeContinuaMoendo();
		AAguaSoParaQuemEstaAPe();
		OBonecoLargadoNaoVai();
		ADisputaDeKiLargaOCorpo();
		LimparTudoDaBancada();
	}

	/// <summary>
	/// CONTRA-EXEMPLO 1: DE PERTO ELE ARREMESSA, e nao carrega.
	///
	/// E o corte que as DUAS fontes escrevem no mesmo numero -- `beam_stun_start = 4` no DU
	/// (`death.dm:727`) e `maxdistance-distance &lt;= 4` no Finale (`objects.dm:449-455`). Sem esta
	/// medida, "carrega" e uma regra que vale sempre, e uma regra que vale sempre nao e uma regra.
	/// </summary>
	private void OPertoArremessaEmVezDeLevar()
	{
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;

		Vec2 raia = CorredorSeco(20);
		ServerPlayer atira = Forjar("Queima-roupa", raia, bp: 500_000);
		atira.Facing = Facing.East;
		ServerPlayer colado = Forjar("Colado", raia + new Vec2(2 * T, 0), bp: 5_000);

		Projetil raio = RaioDaBancada(atira, baseDano: 5);
		for (int i = 0; i < 200 && raio.Vivo && colado.TiquesDeVoo == 0 && raio.Arrastando == 0; i++)
			UmTiqueDeArrasto();

		AfirmarPj("a QUEIMA-ROUPA ARREMESSA (o impulso do Finale) e NAO carrega",
				  colado.TiquesDeVoo > 0 && raio.Arrastando == 0,
				  $"voo={colado.TiquesDeVoo} arrasto={raio.Arrastando}");
	}

	/// <summary>
	/// CONTRA-EXEMPLO 2: PRENSADO NUM MURO, os dois param -- e o feixe **continua moendo**.
	///
	/// A decisao esta escrita no `ArrastarComOFeixe` e vem do DM pelos dois lados: o `step()` do
	/// arrasto falha contra densidade, e o `Knockback` escreve a mesma conclusao com todas as letras
	/// (`if(loc == old_loc) { KB=0; break }`, `death.dm:230`). O que esta bancada mede alem disso e
	/// que a cabeca **nao atravessa** a vitima prensada: sem a ordem certa (empurrar antes de avancar)
	/// o feixe passaria por dentro de quem ele esta esmagando.
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

		var deOnde = new Vec2((cx - 8) * T + T / 2f, cy * T + T / 2f);
		ServerPlayer atira = Forjar("Prensador", deOnde, bp: 300_000);
		atira.Facing = Facing.East;
		ServerPlayer preso = Forjar("Prensado", new Vec2((cx - 1) * T + T / 2f, cy * T + T / 2f), bp: 300_000);

		Projetil raio = RaioDaBancada(atira, baseDano: 0.002);
		for (int i = 0; i < 300 && raio.Vivo && raio.Arrastando == 0; i++) UmTiqueDeArrasto();

		AfirmarPj("o feixe pegou o corpo que esta com o muro nas costas", raio.Arrastando == preso.Id,
				  $"arrastando={raio.Arrastando}");
		if (raio.Arrastando != preso.Id) return;

		Vec2 encostou = preso.Pos;
		double vidaAntes = preso.Combate!.Corpo.Partes.Sum(p => p.Vida);
		for (int i = 0; i < 60; i++) UmTiqueDeArrasto();

		AfirmarPj("prensado contra o muro, o corpo PARA (o `step()` que falha do DM)",
				  (preso.Pos - encostou).Length < 2f, $"{(preso.Pos - encostou).Length:0.0} px");
		AfirmarPj("...e a cabeca do feixe NAO o atravessa: ela para nele",
				  raio.Pos.X <= preso.Pos.X + Projetil.RaioDeImpacto + 1f,
				  $"cabeca em {raio.Pos.X:0}, corpo em {preso.Pos.X:0}");
		AfirmarPj("...e o feixe nao estoura nem some: ele continua MOENDO quem esta prensado",
				  raio.Vivo && preso.Combate.Corpo.Partes.Sum(p => p.Vida) < vidaAntes,
				  $"vivo={raio.Vivo}");
	}

	/// <summary>
	/// CONTRA-EXEMPLO 3: A AGUA SO PARA QUEM ESTA A PE -- e quem decide e o `ModoDeTravessiaDe`, a
	/// MESMA funcao que valida o passo do jogador e o da IA.
	///
	/// O mesmo lago, o mesmo tiro, o mesmo corpo: muda so o `Nadando`. Medir so a metade que passa (ou
	/// so a que para) deixaria verde um codigo que ignorasse a agua por inteiro.
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

			var deOnde = new Vec2((cx - 8) * T + T / 2f, cy * T + T / 2f);
			ServerPlayer atira = Forjar("Molhador", deOnde, bp: 300_000);
			atira.Facing = Facing.East;
			ServerPlayer alvo = Forjar("Molhado", new Vec2((cx - 1) * T + T / 2f, cy * T + T / 2f),
									   bp: 300_000);
			alvo.Nadando = nadando;

			Projetil raio = RaioDaBancada(atira, baseDano: 0.002);
			for (int i = 0; i < 300 && raio.Vivo && raio.Arrastando == 0; i++) UmTiqueDeArrasto();
			if (raio.Arrastando != alvo.Id)
			{
				AfirmarPj($"o feixe pegou o corpo na beira do lago (nadando={nadando})", false,
						  $"arrastando={raio.Arrastando}");
				continue;
			}

			for (int i = 0; i < 60 && raio.Vivo; i++) UmTiqueDeArrasto();
			bool molhou = mapa.EhAguaEm(alvo.Pos);

			AfirmarPj(nadando
						  ? "...e quem esta NADANDO e levado pra dentro do lago pelo mesmo feixe"
						  : "quem esta A PE e levado ate a beira do lago e para nela",
					  molhou == nadando, $"molhou={molhou}");
		}
	}

	/// <summary>
	/// CONTRA-EXEMPLO 4: O BONECO LARGADO NAO VAI.
	///
	/// A recusa e `_players.ContainsKey`, e nao um `if` de `Peer`/`Cerebro` -- ver
	/// `PodeSerLevadoPeloFeixe`. O boneco e definido por NAO estar nessa lista, e e essa mesma lista
	/// que o `TickDoEmpurrao` percorre pra soltar o corpo: carrega-lo seria o unico corpo do jogo que
	/// ninguem solta. A bancada reproduz o boneco do jeito que ele existe -- fora do `_players` e
	/// dentro da zona.
	/// </summary>
	private void OBonecoLargadoNaoVai()
	{
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;

		Vec2 raia = CorredorSeco(24);
		ServerPlayer atira = Forjar("Feixe-boneco", raia, bp: 200_000);
		atira.Facing = Facing.East;
		ServerPlayer boneco = Forjar("Boneco", raia + new Vec2(6 * T, 0), bp: 200_000);

		// E ASSIM QUE UM BONECO EXISTE: na zona (ele ocupa lugar e apanha) e fora do `_players`.
		_players.Remove(boneco.Id);

		Projetil raio = RaioDaBancada(atira, baseDano: 0.002);
		Vec2 onde = boneco.Pos;
		for (int i = 0; i < 300 && raio.Vivo; i++) UmTiqueDeArrasto();

		AfirmarPj("o feixe NAO carrega o corpo largado (ele nao esta na lista que solta ninguem)",
				  raio.Arrastando == 0 && (boneco.Pos - onde).Length < 0.01f,
				  $"arrastando={raio.Arrastando}, andou {(boneco.Pos - onde).Length:0.0} px");

		_players[boneco.Id] = boneco;   // devolve, senao o `LimparTudoDaBancada` o deixa na zona
	}

	/// <summary>
	/// CONTRA-EXEMPLO 5: ENTROU NUMA DISPUTA DE KI, LARGA.
	///
	/// Numa colisao de feixes quem manda na cabeca e o `TickDosEmbatesDeKi` (ela e posta no ponto de
	/// encontro a cada tique). Uma cabeca que nao anda nao tem delta pra passar pro corpo, e continuar
	/// "arrastando" seria segurar alguem parado sem nada empurrando -- ver o passo 1 do `AndarProjetil`.
	/// </summary>
	private void ADisputaDeKiLargaOCorpo()
	{
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;

		Vec2 raia = CorredorSeco(24);
		ServerPlayer atira = Forjar("Feixe-disputa", raia, bp: 200_000);
		atira.Facing = Facing.East;
		ServerPlayer alvo = Forjar("Disputado", raia + new Vec2(6 * T, 0), bp: 200_000);

		Projetil raio = RaioDaBancada(atira, baseDano: 0.002);
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
	/// OS CINCO DEFEITOS, e cada um e um jeito REAL de este efeito quebrar -- nao ruido aleatorio.
	/// Ruido derruba qualquer regra e nao prova nada sobre nenhuma; estes cinco tem endereco no
	/// codigo de producao, e o comentario de cada um diz qual linha o produziria.
	/// </summary>
	private static DefeitoDeSulco[] DefeitosDoSulco() =>
	[
		// A chamada saindo do fim do `AndarProjetil` em vez de dentro do laco de sub-passos.
		new("o rastro PICOTADO (carimbo so no fim do tique)", "SE ENCOSTAM",
			s => [.. s.Where((_, i) => i % 2 == 0)]),

		// O `CarimbarSulco` sem o alinhamento ao centro: a marca cai no pixel exato da cabeca.
		new("a marca no PIXEL da cabeca, sem alinhar ao centro da celula", "CENTRO",
			s => { s[s.Count / 2] = s[s.Count / 2] with { Onde = s[s.Count / 2].Onde + new Vec2(5, 3) }; return s; }),

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
		const int T = ZoneCollision.TileSize;

		// ---- 1. O PRAZO APAGADO ----
		LimparTudoDaBancada();
		Vec2 raia = CorredorSeco(30);
		ServerPlayer atira = Forjar("Feixe da injecao", raia, bp: 200_000);
		atira.Facing = Facing.East;
		ServerPlayer vitima = Forjar("Levado da injecao", raia + new Vec2(6 * T, 0), bp: 200_000);

		Projetil raio = RaioDaBancada(atira, baseDano: 0.002);
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
		Vec2 outra = CorredorSeco(30);
		ServerPlayer atira2 = Forjar("Feixe da injecao 2", outra, bp: 200_000);
		atira2.Facing = Facing.East;
		ServerPlayer morto = Forjar("Recusado", outra + new Vec2(6 * T, 0), bp: 200_000);

		Projetil raio2 = RaioDaBancada(atira2, baseDano: 0.002);
		for (int i = 0; i < 300 && raio2.Vivo && raio2.Arrastando == 0; i++) UmTiqueDeArrasto();

		if (raio2.Arrastando != morto.Id)
		{
			AfirmarPj("[injecao] (montagem) o segundo feixe pegou a vitima", false,
					  $"arrastando={raio2.Arrastando}");
			return;
		}

		morto.Ficha.dead = true;   // A INJECAO -- o `PodeSerLevadoPeloFeixe` passa a recusar
		Vec2 corpoAntes = morto.Pos, cabecaAntes = raio2.Pos;
		for (int i = 0; i < 6 && raio2.Vivo; i++) UmTiqueDeArrasto();
		float dCorpo = (morto.Pos - corpoAntes).Length;
		float dCabeca = (raio2.Pos - cabecaAntes).Length;

		AfirmarPj("[injecao] com a vitima recusada pelo `PodeSerLevadoPeloFeixe`, a cabeca anda e o "
				  + "corpo NAO -- a medida da familia 9 mede o arrasto, e nao 'corpo que se mexeu'",
				  dCabeca > 1f && dCorpo < 0.5f, $"corpo {dCorpo:0.0} px, cabeca {dCabeca:0.0} px");
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
	private Projetil RaioDaBancada(ServerPlayer pl, double baseDano)
	{
		Projetil raio = Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam, BaseDano = baseDano, Velocidade = 1,
			AlcanceTiles = 40, Deflectivel = false, Nome = "Onda de Ki",
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
			_players.Remove(pl.Id);
			ZoneList(pl.Zone.Hash).Remove(pl);
		}
	}
}
