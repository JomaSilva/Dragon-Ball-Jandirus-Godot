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
			OTeleguiadoPerseguemQuemFoge();
			ORaioCarregaCanalizaEDesfaz();
			ACargaPrendeOCorpo();
			OCenarioParaOTiroEAAlturaNao();
			OTetoDispara();
			OCustoDoTique();
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

		AfirmarPj("a bola nasceu viva e na mao do atirador", p.Vivo && p.Pos.Equals(atirador.Pos));

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
		AfirmarPj("...e a cauda e a MAO do dono", (raio.Cauda - pl.Pos).Length < 1f);

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
	private static bool AcharParede(ZoneCollision mapa, out int cx, out int cy)
	{
		for (int y = 8; y < 240; y++)
			for (int x = 12; x < 240; x++)
			{
				if (!mapa.BlockedCell(x, y)) continue;
				bool livre = true;
				for (int d = 1; d <= 4; d++) livre &= !mapa.BlockedCell(x - d, y);
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
