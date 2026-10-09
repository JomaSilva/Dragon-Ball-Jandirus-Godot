using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA MIRA DO KI E DO TELEGUIADO (`--miradekiteste`) -- o pedido do dono de 2026-10-09, cada frase
/// dele pelo funil de producao e cada uma com o seu contra-exemplo:
///
///  1. *"ao ter um target ... o seu personagem ai virar pra direcao do seu target e usar o ataque de ki na
///     direcao do target"* -- o tiro sai no rumo EXATO do marcado (obliqua, diagonal), o corpo vira pro lado
///     dele e a bola acerta quem nao esta em fileira nenhuma dele. Sem marca, sai pra onde o corpo olha.
///  2. *"se o target se mover o seu beam ou ataque de ki nao vao se curvar"* -- a bola e o raio comuns guardam
///     o rumo do nascimento; o raio mira onde o alvo esta quando a CARGA FECHA, e nao quando ela comecou.
///  3. *"teleguiado ... se torce levemente ... n vai fazer uma curva de 90 graus, fazendo assim o ataque
///     errar"* -- a bola teleguiada alcanca quem anda de lado, vira no maximo `Teleguiado.GrausPorTile` por
///     tile, nunca passa de `DesvioMaximoEmGraus` do rumo da saida, e ERRA quem vai pras costas dela.
///  4. *"na criacao de tecnica o usuario coloque q o ataque e teleguiado"* -- o botao da mesa: preco, recusa
///     fora do raio, estorno ao trocar de tipo, o fio e o disco.
///  5. *"beams etc feitos teleguiados"* -- o raio inventado gira RETO atras do alvo (a cabeca nunca sai do
///     eixo, a cauda fica na mao), alcanca quem anda, perde quem corre, vira o corpo de quem o segura e nao
///     varre por dentro de parede.
///  6. As tres telas contam a mesma historia: o olhar que o tiro cravou nao e desfeito pelo pacote de movimento
///     que ja estava a caminho.
///
/// Empresta a infraestrutura da `--projetilteste` (o forjador, a zona da Terra, os corredores livres). Roda no
/// boot, sem ninguem logado.
/// </summary>
public partial class GameServer
{
	private int _miraOk, _miraFalhou;

	private void AfirmarMira(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _miraOk++; GD.Print($"[mira]   OK    {oque}"); return; }
		_miraFalhou++;
		GD.PrintErr($"[mira]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaMiraDeKi()
	{
		_miraOk = _miraFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[mira] ================ A MIRA DO KI E O TELEGUIADO ================");

		AfirmarMira("a zona da bancada tem colisao carregada", _pjMapa != null);

		try
		{
			OTiroSaiNoMarcado();
			DepoisDeSoltoNaoAcompanha();
			ABolaTeleguiadaFazCurvaLimitada();
			TecnicaCustomizada? raio = OBotaoDaMesa();
			if (raio != null) ORaioTeleguiadoGiraReto(raio);
			AParedeSeguraORaio();
			OOlharCravado();
		}
		finally
		{
			MiraDeKi.TiroSoPraFrenteDeTeste = false;
			MiraDeKi.OlharSoltoDeTeste = false;
			Teleguiado.SemLimiteDeTeste = false;
			Teleguiado.VarreParedeDeTeste = false;
			LimparTudoDaBancada();
		}

		GD.Print($"[mira] ================ {_miraOk} passaram, {_miraFalhou} falharam ================");
	}

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// Uma bola da bancada. `Deflectivel = false` pelo motivo do `RaioDaBancada`: o que se mede e pra onde ela
	/// vai, e um sorteio de deflexao no fim trocaria `Acertou` por `Defletido` sem ela ter errado nada.
	/// </summary>
	private static ReceitaDeProjetil BolaDaMira(TipoDeProjetil tipo = TipoDeProjetil.Blast) => new()
	{
		Tipo = tipo, BaseDano = 15, Velocidade = 1, AlcanceTiles = 40, Deflectivel = false, Nome = "bola da mira",
	};

	/// <summary>
	/// OS DOIS DA CENA, NO AR. Acima do limiar em que o tiro atravessa cenario (`Voo.AtravessaCenario`): o que
	/// se mede e o RUMO, e na Terra uma obliqua de dez tiles acaba dentro de uma arvore. No mesmo andar, senao
	/// `Voo.PodeAcertar` recusaria o impacto e a bancada mediria a regra de altura.
	/// </summary>
	private (ServerPlayer Atira, ServerPlayer Alvo) ParDaMira(string nome, Vec2 ondeOAlvo, Facing olhando, bool marcado = true,
															 double bp = 5_000)
	{
		float noAr = Voo.AlturaQueAtravessa + 1;
		Vec2 chao = CorredorLivre(4);
		ServerPlayer a = Forjar(nome, chao, bp);
		a.Altitude = noAr;
		a.Facing = olhando;
		ServerPlayer alvo = Forjar(nome + "Alvo", chao + ondeOAlvo, bp);
		alvo.Altitude = noAr;
		if (marcado) a.AlvoId = alvo.Id;
		return (a, alvo);
	}

	private void VoarAteAcabar(Projetil p, int tiques = 600)
	{
		for (int i = 0; i < tiques && p.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
	}

	/// <summary>Roda os canais ate a cabeca do raio deste corpo nascer. Nulo = a carga nao fechou.</summary>
	private Projetil? NascerORaioDe(ServerPlayer pl)
	{
		for (int i = 0; i < 900; i++)
		{
			if (!_canais.TryGetValue(pl.Id, out CanalDeKi? c)) return null;
			if (c.Raio != null) return c.Raio;
			TickDosCanaisDeKi(Protocol.TickSeconds);
		}
		return null;
	}

	/// <summary>Quantos pixels a cabeca esta FORA do eixo do proprio feixe (a reta da cauda no rumo).</summary>
	private static float ForaDoEixo(Projetil p)
	{
		Vec2 d = p.Pos - p.Cauda;
		return MathF.Abs(d.X * p.Rumo.Y - d.Y * p.Rumo.X);
	}

	// =====================================================================
	// 1) COM ALGUEM MARCADO, O TIRO SAI NELE
	// =====================================================================
	private void OTiroSaiNoMarcado()
	{
		GD.Print("[mira] -- 1) COM ALGUEM MARCADO, O TIRO SAI NELE E O CORPO VIRA");
		const int T = ZoneCollision.TileSize;

		// ---------------------------------------------------------------- a obliqua
		(ServerPlayer a, ServerPlayer alvo) = ParDaMira("Mirador", new Vec2(8 * T, 5 * T), Facing.North);
		Projetil p = Disparar(a, BolaDaMira());
		Vec2 exato = (alvo.Pos - a.Pos).Normalized();
		AfirmarMira("com alguem MARCADO na obliqua (8 tiles a leste, 5 ao sul), a bola sai no rumo EXATO dele",
					MeleeArea.Angulo(p.Rumo, exato) < 0.01, $"rumo {p.Rumo}, esperado {exato}");
		AfirmarMira("...e o corpo vira pro lado dele, o mais perto dos quatro: olhava pro norte, encara o leste",
					a.Facing == Facing.East, $"{a.Facing}");
		VoarAteAcabar(p);
		AfirmarMira("...e ela ACERTA o alvo parado, que nao esta em fileira nenhuma de quem atirou",
					p.Fim == FimDeProjetil.Acertou, $"fim = {p.Fim}, bola em {p.Pos}, alvo em {alvo.Pos}");

		// ---------------------------------------------------------------- a diagonal do pedido
		LimparTudoDaBancada();
		(a, alvo) = ParDaMira("Diagonal", new Vec2(6 * T, 6 * T), Facing.West);
		p = Disparar(a, BolaDaMira());
		AfirmarMira("o inimigo na DIAGONAL: o tiro sai na diagonal (os dois eixos do rumo iguais)",
					MathF.Abs(p.Rumo.X - p.Rumo.Y) < 1e-4f && p.Rumo.X > 0.70f, $"rumo {p.Rumo}");
		VoarAteAcabar(p);
		AfirmarMira("...e acerta", p.Fim == FimDeProjetil.Acertou, $"fim = {p.Fim}");

		// ---------------------------------------------------------------- sem marca, nada muda
		LimparTudoDaBancada();
		(a, alvo) = ParDaMira("SemMarca", new Vec2(6 * T, 6 * T), Facing.North, marcado: false);
		p = Disparar(a, BolaDaMira());
		AfirmarMira("SEM ninguem marcado o tiro sai pra onde o corpo olha (norte), e o olhar continua do teclado",
					p.Rumo.Equals(new Vec2(0, -1)) && a.Facing == Facing.North && a.OlharCravadoAte == 0,
					$"rumo {p.Rumo}, olhando {a.Facing}, cravado ate {a.OlharCravadoAte}");
		VoarAteAcabar(p);
		AfirmarMira("...e quem esta na diagonal nao e acertado", p.Fim != FimDeProjetil.Acertou, $"fim = {p.Fim}");

		// ---------------------------------------------------------------- o contra-exemplo
		LimparTudoDaBancada();
		MiraDeKi.TiroSoPraFrenteDeTeste = true;
		try
		{
			(a, alvo) = ParDaMira("SoPraFrente", new Vec2(6 * T, 6 * T), Facing.North);
			p = Disparar(a, BolaDaMira());
			VoarAteAcabar(p);
			AfirmarMira("(defeito injetado: o tiro sai so pra frente) com a marca posta, a bola vai pro norte e o marcado da diagonal escapa",
						p.Fim != FimDeProjetil.Acertou && a.Facing == Facing.North, $"fim = {p.Fim}, olhando {a.Facing}");
		}
		finally { MiraDeKi.TiroSoPraFrenteDeTeste = false; }
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 2) DEPOIS DE SOLTO, O TIRO NAO ACOMPANHA NINGUEM
	// =====================================================================
	private void DepoisDeSoltoNaoAcompanha()
	{
		GD.Print("[mira] -- 2) DEPOIS DE SOLTO O TIRO COMUM NAO FAZ CURVA; O RAIO MIRA QUANDO A CARGA FECHA");
		const int T = ZoneCollision.TileSize;

		// ---------------------------------------------------------------- a bola
		(ServerPlayer a, ServerPlayer alvo) = ParDaMira("Reto", new Vec2(12 * T, 0), Facing.East);
		Projetil p = Disparar(a, BolaDaMira());
		Vec2 nascimento = p.Rumo;
		double maiorDesvio = 0;
		for (int i = 0; i < 600 && p.Vivo; i++)
		{
			if (i == 3) alvo.Pos = a.Pos + new Vec2(12 * T, 5 * T);   // o alvo sai da frente com a bola ja no ar
			TickDosProjeteis(Protocol.TickSeconds);
			maiorDesvio = Math.Max(maiorDesvio, MeleeArea.Angulo(p.Rumo, nascimento));
		}
		AfirmarMira("a bola COMUM nao persegue ninguem (nasce sem alvo) e guarda o rumo do nascimento ate o fim",
					p.Alvo == 0 && maiorDesvio < 1e-6, $"alvo {p.Alvo}, desvio maximo {maiorDesvio:0.####} graus");
		AfirmarMira("...e o alvo que saiu da frente com ela no ar nao e acertado", p.Fim != FimDeProjetil.Acertou, $"fim = {p.Fim}");

		// ---------------------------------------------------------------- o raio, pelo verb de producao
		LimparTudoDaBancada();
		(a, alvo) = ParDaMira("Raiador", new Vec2(10 * T, 2 * T), Facing.North, bp: 50_000);
		KiWave(a);
		AfirmarMira("ao COMECAR a carga do raio o corpo ja vira pro marcado (olhava pro norte, encara o leste)",
					_canais.ContainsKey(a.Id) && a.Facing == Facing.East, $"canal {_canais.ContainsKey(a.Id)}, olhando {a.Facing}");

		alvo.Pos = a.Pos + new Vec2(2 * T, 10 * T);   // ele anda DURANTE a carga
		Projetil? raio = NascerORaioDe(a);
		AfirmarMira("a carga fechou e a cabeca nasceu", raio != null);
		if (raio == null) { LimparTudoDaBancada(); return; }

		Vec2 agora = (alvo.Pos - a.Pos).Normalized();
		AfirmarMira("o rumo do raio e o do alvo QUANDO A CARGA FECHA, e nao o de quando ela comecou",
					MeleeArea.Angulo(raio.Rumo, agora) < 0.01, $"rumo {raio.Rumo}, esperado {agora}");
		AfirmarMira("...e o corpo vira de novo com ele (agora o sul)", a.Facing == Facing.South, $"{a.Facing}");

		nascimento = raio.Rumo;
		alvo.Pos = a.Pos + new Vec2(-9 * T, 0);   // ...e anda de novo, com o raio ja saindo
		maiorDesvio = 0;
		float maiorFora = 0;
		for (int i = 0; i < 25 && raio.Vivo; i++)
		{
			TickDosProjeteis(Protocol.TickSeconds);
			maiorDesvio = Math.Max(maiorDesvio, MeleeArea.Angulo(raio.Rumo, nascimento));
			maiorFora = MathF.Max(maiorFora, ForaDoEixo(raio));
		}
		AfirmarMira("depois de nascido o raio COMUM nao gira atras de ninguem: mesmo rumo, cabeca no mesmo eixo",
					raio.Alvo == 0 && maiorDesvio < 1e-6 && maiorFora < 0.5f,
					$"alvo {raio.Alvo}, desvio {maiorDesvio:0.####} graus, {maiorFora:0.00} px fora do eixo");
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 3) A BOLA TELEGUIADA: CURVA, MAS SO ATE ONDE A CURVA DEIXA
	// =====================================================================
	/// <summary>
	/// A bola teleguiada contra um alvo que anda. Devolve o maior giro num tique e o maior desvio do rumo da saida.
	/// </summary>
	/// <param name="foge">O que o alvo faz a cada tique (recebe o numero do tique e quantos tiles a bola ja andou).</param>
	private (Projetil Bola, double MaiorGiro, double MaiorDesvio) CacadaDaBola(string nome, Vec2 ondeOAlvo,
		Action<ServerPlayer, ServerPlayer, int, double> foge)
	{
		(ServerPlayer a, ServerPlayer alvo) = ParDaMira(nome, ondeOAlvo, Facing.East);
		Projetil p = Disparar(a, BolaDaMira(TipoDeProjetil.Guided));
		p.Alvo = alvo.Id;

		double maiorGiro = 0, maiorDesvio = 0;
		for (int i = 0; i < 600 && p.Vivo; i++)
		{
			foge(a, alvo, i, p.AndouTiles);
			Vec2 antes = p.Rumo;
			TickDosProjeteis(Protocol.TickSeconds);
			maiorGiro = Math.Max(maiorGiro, MeleeArea.Angulo(p.Rumo, antes));
			maiorDesvio = Math.Max(maiorDesvio, Teleguiado.Desvio(p.Rumo, p.RumoDaSaida));
		}
		return (p, maiorGiro, maiorDesvio);
	}

	private void ABolaTeleguiadaFazCurvaLimitada()
	{
		GD.Print("[mira] -- 3) A BOLA TELEGUIADA FAZ CURVA -- DE LEVE, E NUNCA ALEM DO LEQUE");
		const int T = ZoneCollision.TileSize;

		// ---------------------------------------------------------------- quem anda de lado e alcancado
		(Projetil p, double giro, double desvio) = CacadaDaBola("Cacador", new Vec2(12 * T, 0),
			(_, alvo, _, _) => alvo.Pos = new Vec2(alvo.Pos.X, alvo.Pos.Y + 3f));   // 2,8 tiles por segundo, de lado
		float porTique = (float)(T / p.SegundosPorTile * Protocol.TickSeconds);
		double teto = Teleguiado.GiroDaBola(porTique);
		AfirmarMira("a bola teleguiada ALCANCA quem anda de lado (uma bola reta passaria reto)",
					p.Fim == FimDeProjetil.Acertou, $"fim = {p.Fim}");
		AfirmarMira($"...virando de verdade (o rumo saiu do da saida), e no maximo {Teleguiado.GrausPorTile:0} graus por tile andado",
					desvio > 2 && giro <= teto + 0.02, $"desvio {desvio:0.0} graus, maior giro num tique {giro:0.00} (teto {teto:0.00})");
		LimparTudoDaBancada();

		// ---------------------------------------------------------------- quem vai pras costas dela e perdido
		void PrasCostas(ServerPlayer a, ServerPlayer alvo, int _, double andou)
		{
			if (andou >= 3) alvo.Pos = a.Pos + new Vec2(-9 * T, 0);   // some da frente e reaparece atras de quem atirou
		}
		(p, giro, desvio) = CacadaDaBola("Perdedor", new Vec2(9 * T, 0), PrasCostas);
		AfirmarMira($"o alvo que vai pras COSTAS dela e perdido: o rumo nunca passa de {Teleguiado.DesvioMaximoEmGraus:0} graus do da saida",
					desvio <= Teleguiado.DesvioMaximoEmGraus + 0.05 && desvio > Teleguiado.DesvioMaximoEmGraus - 1,
					$"desvio maximo {desvio:0.00} graus");
		AfirmarMira("...e a bola ERRA -- nao ha curva de 90 graus", p.Fim != FimDeProjetil.Acertou, $"fim = {p.Fim}");
		LimparTudoDaBancada();

		// ---------------------------------------------------------------- o contra-exemplo
		Teleguiado.SemLimiteDeTeste = true;
		try
		{
			(p, giro, desvio) = CacadaDaBola("SemLimite", new Vec2(9 * T, 0), PrasCostas);
			AfirmarMira("(defeito injetado: o teleguiado vira sem limite) a mesma bola da meia-volta no ar e acerta quem foi pras costas dela",
						p.Fim == FimDeProjetil.Acertou && desvio > 90, $"fim = {p.Fim}, desvio maximo {desvio:0.0} graus");
		}
		finally { Teleguiado.SemLimiteDeTeste = false; }
		LimparTudoDaBancada();

		// ---------------------------------------------------------------- a que nasce parada (o cerco)
		(ServerPlayer dono, ServerPlayer presa) = ParDaMira("Cerco", new Vec2(8 * T, 0), Facing.East);
		Vec2 canto = presa.Pos + new Vec2(3 * T, 3 * T);
		Projetil parada = Disparar(dono, BolaDaMira(TipoDeProjetil.Guided), rumoDado: Vec2.Zero, deOnde: canto);
		parada.Alvo = presa.Id;
		parada.EsperaDeCaca = 0.2;
		for (int i = 0; i < 3; i++) TickDosProjeteis(Protocol.TickSeconds);
		AfirmarMira("a bola que nasce PARADA em volta do alvo espera sem andar, e ainda nao tem rumo de saida",
					parada.Vivo && parada.Pos.Equals(canto) && parada.RumoDaSaida.LengthSquared < 1e-6f,
					$"em {parada.Pos} (nasceu em {canto}), saida {parada.RumoDaSaida}");
		for (int i = 0; i < 6 && parada.Rumo.LengthSquared < 1e-6f; i++) TickDosProjeteis(Protocol.TickSeconds);
		AfirmarMira("...e quando comeca a cacar aponta DIRETO pro alvo -- e desse rumo que o leque dela passa a contar",
					MeleeArea.Angulo(parada.Rumo, presa.Pos - canto) < 3 && parada.RumoDaSaida.LengthSquared > 0.9f
					&& Teleguiado.Desvio(parada.Rumo, parada.RumoDaSaida) < 0.01,
					$"rumo {parada.Rumo}, saida {parada.RumoDaSaida}");
		VoarAteAcabar(parada);
		AfirmarMira("...e fecha o cerco", parada.Fim == FimDeProjetil.Acertou, $"fim = {parada.Fim}");
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 4) O BOTAO DA MESA
	// =====================================================================
	/// <summary>Monta o raio teleguiado pela mesa de PRODUCAO e o devolve pronto (nulo = a mesa recusou).</summary>
	private TecnicaCustomizada? OBotaoDaMesa()
	{
		GD.Print("[mira] -- 4) NA MESA DE TECNICAS: O RAIO TELEGUIADO E UM BOTAO DE 2 PONTOS");

		ServerPlayer pl = Forjar("Inventor", CorredorLivre(4), bp: 50_000);
		CriarTecnica(pl);   // nasce raio
		ComprarNaMesa(pl, nameof(Compra.TeleguiadoLigar));
		AfirmarMira($"ligar o teleguiado num raio custa {TecnicaCustomizada.PrecoDoTeleguiado} pontos",
					pl.Mesa is { Teleguiado: true, Gasto: TecnicaCustomizada.PrecoDoTeleguiado },
					pl.Mesa == null ? "mesa fechada" : $"teleguiado {pl.Mesa.Teleguiado}, gasto {pl.Mesa.Gasto}");
		ComprarNaMesa(pl, nameof(Compra.TeleguiadoLigar));
		AfirmarMira("...ligar de novo e recusado e nao cobra duas vezes",
					pl.Mesa is { Teleguiado: true, Gasto: TecnicaCustomizada.PrecoDoTeleguiado }, $"gasto {pl.Mesa?.Gasto}");
		ComprarNaMesa(pl, nameof(Compra.TeleguiadoDesligar));
		AfirmarMira("...e desligar devolve os mesmos pontos", pl.Mesa is { Teleguiado: false, Gasto: 0 }, $"gasto {pl.Mesa?.Gasto}");

		// TROCAR DE TIPO DESFAZ O BOTAO, como os outros modificadores de raio -- senao os pontos ficariam presos
		// num campo que a bola nao le.
		ComprarNaMesa(pl, nameof(Compra.TeleguiadoLigar));
		ComprarNaMesa(pl, nameof(Compra.InstantaneoLigar));
		TipoDaTecnica(pl, "blast");
		AfirmarMira("virar BOLA desfaz o teleguiado e devolve os pontos dele (com os do instantaneo)",
					pl.Mesa is { Tipo: TipoDeProjetil.Blast, Teleguiado: false, Gasto: 0 },
					pl.Mesa == null ? "mesa fechada" : $"{pl.Mesa.Tipo}, teleguiado {pl.Mesa.Teleguiado}, gasto {pl.Mesa.Gasto}");
		ComprarNaMesa(pl, nameof(Compra.TeleguiadoLigar));
		AfirmarMira("...e numa bola o botao e RECUSADO (a bola que persegue e o tipo teleguiado)",
					pl.Mesa is { Teleguiado: false, Gasto: 0 }, $"teleguiado {pl.Mesa?.Teleguiado}, gasto {pl.Mesa?.Gasto}");
		TipoDaTecnica(pl, "guided");
		AfirmarMira("a receita da bola teleguiada NAO usa o botao: ela persegue pelo tipo, e quem a atira escreve o alvo",
					pl.Mesa is { Tipo: TipoDeProjetil.Guided } && !pl.Mesa.Receita().Teleguiado);

		// A TECNICA QUE FICA: um raio teleguiado, salvo pelo verbo de producao.
		TipoDaTecnica(pl, "beam");
		ComprarNaMesa(pl, nameof(Compra.TeleguiadoLigar));
		TextoDaMesa(pl, "nome/Raio Cacador");
		SalvarTecnica(pl);
		TecnicaCustomizada? tec = pl.Customizadas.Find(t => t.Teleguiado);
		AfirmarMira("o raio teleguiado e salvo, e a receita dele sai marcada como teleguiada",
					tec is { Criada: true, Tipo: TipoDeProjetil.Beam } && tec.Receita().Teleguiado,
					tec == null ? "nao achei a tecnica" : $"{tec.Tipo}, criada {tec.Criada}");
		if (tec == null) { LimparTudoDaBancada(); return null; }

		// ---------------------------------------------------------------- o fio
		var w = new LiteNetLib.Utils.NetDataWriter();
		CustomWire.Escrever(w, tec);
		var r = new LiteNetLib.Utils.NetDataReader(w.CopyData());
		TecnicaCustomizada pelaRede = CustomWire.Ler(r);
		AfirmarMira("o botao atravessa o FIO (a mesa do cliente mostra o que o servidor guardou) e o pacote e lido inteiro",
					pelaRede.Teleguiado && pelaRede.Gasto == tec.Gasto && r.AvailableBytes == 0,
					$"teleguiado {pelaRede.Teleguiado}, gasto {pelaRede.Gasto}, sobraram {r.AvailableBytes} bytes");

		// ---------------------------------------------------------------- o disco
		string pasta = Path.Combine(Path.GetTempPath(), "jandirus_mira_" + Guid.NewGuid().ToString("N"));
		try
		{
			var loja = new AccountStore(pasta);
			var conta = new AccountSave { Conta = "bancada_mira" };
			conta.Slots[0] = AccountStore.DeJogador(pl, 0);
			loja.Gravar(conta);
			TecnicaCustomizada? doDisco = loja.Carregar("bancada_mira")?.Slots[0]?.Customizadas?.Find(t => t.Id == tec.Id);
			AfirmarMira("...e atravessa o DISCO, com os pontos que custou",
						doDisco is { Teleguiado: true } && doDisco.Gasto == tec.Gasto,
						doDisco == null ? "a tecnica nao voltou" : $"teleguiado {doDisco.Teleguiado}, gasto {doDisco.Gasto}");
		}
		catch (Exception e) { AfirmarMira("o raio teleguiado atravessa o disco", false, e.Message); }
		finally
		{
			try { if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true); }
			catch (Exception e) { GD.Print($"[mira] nao consegui apagar {pasta}: {e.Message}"); }
		}

		LimparTudoDaBancada();
		return tec;
	}

	// =====================================================================
	// 5) O RAIO TELEGUIADO GIRA RETO
	// =====================================================================
	/// <summary>O que uma cacada de raio mediu.</summary>
	private readonly record struct CacadaDeRaio(bool Nasceu, bool TinhaAlvo, bool Tocou, double MaiorDesvio, float MaiorFora,
											   float MaiorLado, float MaiorBoca, Facing OlharNoFim, Facing OlharCravado);

	/// <summary>
	/// O raio inventado, atirado pelo VERBO DE PRODUCAO (`UsarTecnicaCustomizada`: carga, canal, cabeca), contra um
	/// alvo que anda <paramref name="passoDoAlvo"/> por tique depois que a cabeca nasce.
	/// </summary>
	private CacadaDeRaio CacarComORaio(string nome, TecnicaCustomizada tec, Vec2 ondeOAlvo, Vec2 passoDoAlvo, Facing olhando)
	{
		(ServerPlayer a, ServerPlayer alvo) = ParDaMira(nome, ondeOAlvo, olhando, bp: 50_000);
		a.Customizadas.Add(tec);
		UsarTecnicaCustomizada(a, tec.Verbo);
		Projetil? raio = NascerORaioDe(a);
		if (raio == null) return default;

		bool tinhaAlvo = raio.Alvo == alvo.Id;
		bool tocou = false;
		double maiorDesvio = 0;
		float maiorFora = 0, maiorLado = 0, maiorBoca = 0;
		for (int i = 0; i < 200 && raio.Vivo && !raio.Encostado; i++)
		{
			alvo.Pos += passoDoAlvo;
			Vec2 cabecaAntes = raio.Pos, rumoAntes = raio.Rumo;
			TickDosProjeteis(Protocol.TickSeconds);

			Vec2 andou = raio.Pos - cabecaAntes;
			maiorLado = MathF.Max(maiorLado, MathF.Abs(andou.X * rumoAntes.Y - andou.Y * rumoAntes.X));
			maiorFora = MathF.Max(maiorFora, ForaDoEixo(raio));
			maiorDesvio = Math.Max(maiorDesvio, Teleguiado.Desvio(raio.Rumo, raio.RumoDaSaida));
			if (raio.Vivo && raio.Canalizando)
				maiorBoca = MathF.Max(maiorBoca, Vec2.Distance(raio.Cauda, BocaDeCano.De(a.Pos, raio.Rumo)));
			tocou |= Feixe.EncostaNoCorpo(raio, alvo.Pos, alvo.Altitude);
		}
		tocou |= raio.Encostado || raio.Fim == FimDeProjetil.Acertou || raio.Fim == FimDeProjetil.Defletido;
		return new CacadaDeRaio(true, tinhaAlvo, tocou, maiorDesvio, maiorFora, maiorLado, maiorBoca, a.Facing, a.OlharCravado);
	}

	private void ORaioTeleguiadoGiraReto(TecnicaCustomizada tec)
	{
		GD.Print("[mira] -- 5) O RAIO TELEGUIADO GIRA RETO ATRAS DO ALVO -- E PERDE QUEM CORRE");
		const int T = ZoneCollision.TileSize;
		var devagar = new Vec2(0, 3f);    // 2,8 tiles por segundo, de lado
		var correndo = new Vec2(0, 12f);  // 11 tiles por segundo: a corrida de quem tem a velocidade de base

		// O TETO DO ESCORREGAO, pela regra e nao por um numero digitado: a ponta anda de lado ate
		// `LadoPorFrente` do que anda pra frente (mais o seno do proprio giro no passo seguinte, que e miudo).
		float porTique = (float)(T / Projetil.AtrasoDeRaio(tec.Velocidade) * Protocol.TickSeconds);
		float tetoDoLado = Teleguiado.LadoPorFrente * porTique + 1.5f;

		// ---------------------------------------------------------------- quem anda e alcancado
		CacadaDeRaio c = CacarComORaio("Giro", tec, new Vec2(14 * T, 0), devagar, Facing.East);
		AfirmarMira("o raio inventado com o botao nasce perseguindo quem esta marcado", c.Nasceu && c.TinhaAlvo);
		AfirmarMira("...e ALCANCA quem anda de lado: ele girou atras do alvo",
					c.Tocou && c.MaiorDesvio > 2, $"tocou {c.Tocou}, girou {c.MaiorDesvio:0.0} graus");
		AfirmarMira("...RETO: a cabeca nunca sai do eixo do feixe, e a cauda continua na mao de quem atira",
					c.MaiorFora < 0.5f && c.MaiorBoca < 0.5f, $"{c.MaiorFora:0.00} px fora do eixo, cauda a {c.MaiorBoca:0.00} px da boca do cano");
		AfirmarMira($"...DE LEVE: a ponta escorrega de lado no maximo {Teleguiado.LadoPorFrente:0.00} do que avanca",
					c.MaiorLado <= tetoDoLado, $"{c.MaiorLado:0.0} px de lado num tique (teto {tetoDoLado:0.0})");
		LimparTudoDaBancada();

		// ---------------------------------------------------------------- sem o botao, a mesma cena erra
		var comum = new TecnicaCustomizada { Id = 2, Nome = "Raio Comum", Criada = true };
		comum.PorTipo(TipoDeProjetil.Beam);
		c = CacarComORaio("SemBotao", comum, new Vec2(14 * T, 0), devagar, Facing.East);
		AfirmarMira("SEM o botao o mesmo raio nasce sem alvo, nao gira e passa reto por quem andou",
					c.Nasceu && !c.TinhaAlvo && !c.Tocou && c.MaiorDesvio < 1e-6, $"alvo {c.TinhaAlvo}, tocou {c.Tocou}, girou {c.MaiorDesvio:0.####}");
		LimparTudoDaBancada();

		// ---------------------------------------------------------------- quem corre escapa
		c = CacarComORaio("Corrida", tec, new Vec2(14 * T, 0), correndo, Facing.East);
		AfirmarMira($"quem CORRE de lado escapa do raio teleguiado, e ele nunca passa de {Teleguiado.DesvioMaximoEmGraus:0} graus do rumo da saida",
					c.Nasceu && !c.Tocou && c.MaiorDesvio <= Teleguiado.DesvioMaximoEmGraus + 0.05,
					$"tocou {c.Tocou}, girou {c.MaiorDesvio:0.0} graus");
		LimparTudoDaBancada();

		Teleguiado.SemLimiteDeTeste = true;
		try
		{
			c = CacarComORaio("CorridaSemLimite", tec, new Vec2(14 * T, 0), correndo, Facing.East);
			AfirmarMira("(defeito injetado: o teleguiado vira sem limite) o raio varre atras de quem corre e o pega",
						c.Nasceu && c.Tocou, $"tocou {c.Tocou}, girou {c.MaiorDesvio:0.0} graus");
		}
		finally { Teleguiado.SemLimiteDeTeste = false; }
		LimparTudoDaBancada();

		// ---------------------------------------------------------------- o corpo acompanha o feixe
		// Alvo na diagonal exata: o empate vira leste. Ele desce, o feixe passa dos 45 graus e o corpo passa a
		// encarar o sul -- pela mesma porta do disparo (`CravarOlhar`).
		c = CacarComORaio("Corpo", tec, new Vec2(9 * T, 9 * T), devagar, Facing.North);
		AfirmarMira("o corpo de quem segura o raio VIRA com ele: saiu encarando o leste, termina encarando o sul",
					c.Nasceu && c.OlharNoFim == Facing.South && c.OlharCravado == Facing.South,
					$"olhando {c.OlharNoFim}, cravado {c.OlharCravado}");
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 5b) A PAREDE SEGURA O RAIO
	// =====================================================================
	/// <summary>
	/// UM RETANGULO DE CHAO LIVRE: `larg` celulas pra leste por `alt` pra sul, a partir de onde as outras cenas
	/// pararam. A cena da parede e a unica desta bancada que roda NO CHAO (ela mede cenario), e precisa das
	/// fileiras de baixo livres tambem -- senao o controle sem parede esbarraria numa arvore.
	/// </summary>
	private (int Cx, int Cy)? RetanguloLivre(int larg, int alt)
	{
		ZoneCollision? mapa = _pjMapa;
		if (mapa == null) return null;
		for (int y = _pjProximoCorredor; y + alt < 250; y++)
			for (int x = 4; x + larg < 250; x++)
			{
				bool livre = true;
				for (int dy = 0; dy < alt && livre; dy++)
					for (int dx = 0; dx < larg && livre; dx++)
						livre &= !mapa.BlockedCell(x + dx, y + dy);
				if (!livre) continue;
				_pjProximoCorredor = y + alt + 2;
				return (x, y);
			}
		return null;
	}

	/// <summary>
	/// A CENA: o raio teleguiado voa pro leste rente a uma parede comprida (a fileira de baixo), e com ele ja
	/// estendido o alvo passa pra TRAS dela. Devolve quanto o raio girou e quantas amostras do tronco ficaram
	/// dentro de celula bloqueada (uma a cada 8 px).
	/// </summary>
	private (bool Rodou, double Girou, int Dentro, bool Tocou) CenaDaParede(string nome, int cx, int cy, bool comParede)
	{
		const int T = ZoneCollision.TileSize;
		ZoneCollision mapa = _pjMapa!;
		Vec2 Centro(int x, int y) => new(x * T + 16, y * T + 16);

		ServerPlayer a = Forjar(nome, Centro(cx, cy), bp: 50_000);
		a.Facing = Facing.East;
		ServerPlayer alvo = Forjar(nome + "Alvo", Centro(cx + 18, cy), bp: 50_000);
		a.AlvoId = alvo.Id;

		if (comParede) for (int x = cx + 3; x <= cx + 21; x++) mapa.Erguer(x, cy + 1);
		try
		{
			Projetil raio = Disparar(a, new ReceitaDeProjetil
			{
				Tipo = TipoDeProjetil.Beam, BaseDano = 1, Velocidade = 1, AlcanceTiles = 20, Deflectivel = false,
				Teleguiado = true, Nome = "raio da parede",
			});
			raio.Canalizando = true;   // como o `RaioDaBancada`: sem o canal a cauda nasce em cima da cabeca
			if (raio.Alvo != alvo.Id) return (false, 0, 0, false);

			double girou = 0;
			int dentro = 0;
			bool tocou = false;
			for (int i = 0; i < 80 && raio.Vivo && !raio.Encostado; i++)
			{
				if (i == 10) alvo.Pos = Centro(cx + 18, cy + 3);   // o alvo passa pra tras da parede, com o raio ja comprido
				TickDosProjeteis(Protocol.TickSeconds);
				girou = Math.Max(girou, Teleguiado.Desvio(raio.Rumo, raio.RumoDaSaida));

				int agora = 0;
				Vec2 d = raio.Pos - raio.Cauda;
				int n = (int)(d.Length / 8f);
				for (int k = 1; k <= n; k++)
					if (mapa.BlockedAt(raio.Cauda + d * (k / (float)n))) agora++;
				dentro = Math.Max(dentro, agora);
				tocou |= Feixe.EncostaNoCorpo(raio, alvo.Pos, alvo.Altitude);
			}
			return (true, girou, dentro, tocou);
		}
		finally
		{
			if (comParede) for (int x = cx + 3; x <= cx + 21; x++) mapa.Baixar(x, cy + 1);
		}
	}

	private void AParedeSeguraORaio()
	{
		GD.Print("[mira] -- 5b) A PAREDE SEGURA O RAIO TELEGUIADO: ELE NAO VARRE POR DENTRO DE UM MURO");

		if (RetanguloLivre(24, 5) is not var (cx, cy))
		{
			AfirmarMira("achei um retangulo de chao livre de 24x5 pra cena da parede", false, "varredura falhou");
			return;
		}

		(bool rodou, double girou, int dentro, bool tocou) = CenaDaParede("SemMuro", cx, cy, comParede: false);
		AfirmarMira("(controle) sem parede, o raio gira atras do alvo que desceu tres fileiras",
					rodou && girou > 6, $"girou {girou:0.0} graus, tocou {tocou}");
		LimparTudoDaBancada();

		(rodou, girou, dentro, tocou) = CenaDaParede("ComMuro", cx, cy, comParede: true);
		AfirmarMira("com uma parede entre os dois, o raio NAO gira por dentro dela e nao alcanca quem esta atras",
					rodou && !tocou && dentro <= 3, $"girou {girou:0.0} graus, {dentro} amostra(s) do tronco dentro da parede, tocou {tocou}");
		LimparTudoDaBancada();

		Teleguiado.VarreParedeDeTeste = true;
		try
		{
			(rodou, girou, dentro, tocou) = CenaDaParede("MuroFurado", cx, cy, comParede: true);
			AfirmarMira("(defeito injetado: o raio gira sem olhar a parede) o tronco fica desenhado por DENTRO do muro",
						rodou && dentro >= 8, $"girou {girou:0.0} graus, {dentro} amostra(s) do tronco dentro da parede");
		}
		finally { Teleguiado.VarreParedeDeTeste = false; }
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 6) O OLHAR CRAVADO
	// =====================================================================
	private void OOlharCravado()
	{
		GD.Print("[mira] -- 6) O OLHAR DE QUEM ATIROU NO MARCADO NAO E DESFEITO PELO PACOTE QUE JA VINHA");
		const int T = ZoneCollision.TileSize;

		(ServerPlayer a, _) = ParDaMira("Encarando", new Vec2(8 * T, 0), Facing.North);
		Disparar(a, BolaDaMira());
		AfirmarMira("o tiro no marcado crava o olhar: o pacote de movimento que chega com o olhar de antes (norte) nao desvira o corpo",
					a.Facing == Facing.East && OlharDoPacote(a, Facing.North) == Facing.East,
					$"olhando {a.Facing}, o pacote daria {OlharDoPacote(a, Facing.North)}");

		MiraDeKi.OlharSoltoDeTeste = true;
		try
		{
			AfirmarMira("(defeito injetado: o olhar cravado nao segura o pacote) o mesmo pacote devolve o corpo pro norte",
						OlharDoPacote(a, Facing.North) == Facing.North, $"{OlharDoPacote(a, Facing.North)}");
		}
		finally { MiraDeKi.OlharSoltoDeTeste = false; }

		long adianto = AdiantoDoRelogioDeTeste;
		try
		{
			AdiantoDoRelogioDeTeste += MiraDeKi.OlharDoTiroMs + 50;
			AfirmarMira($"passados os {MiraDeKi.OlharDoTiroMs} ms da pose do tiro, o olhar volta a ser do teclado",
						OlharDoPacote(a, Facing.North) == Facing.North, $"{OlharDoPacote(a, Facing.North)}");
		}
		finally { AdiantoDoRelogioDeTeste = adianto; }
		LimparTudoDaBancada();

		(a, _) = ParDaMira("Livre", new Vec2(8 * T, 0), Facing.North, marcado: false);
		Disparar(a, BolaDaMira());
		AfirmarMira("quem atira SEM marca nao tem o olhar cravado: o teclado continua mandando",
					OlharDoPacote(a, Facing.West) == Facing.West, $"{OlharDoPacote(a, Facing.West)}");
		LimparTudoDaBancada();
	}
}
