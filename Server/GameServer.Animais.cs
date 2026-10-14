using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Appearance;
using Jandirus.Core.Items;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// OS ANIMAIS -- o encanamento do <see cref="Animais"/> (o pedido do dono, os numeros e o que nao e daqui estao
/// la). Lobo, leao, vaca, galinha: forca fixa, so corpo a corpo, morrem quando caem e deixam carne.
///
/// ============================ A FAUNA DE UM PLANETA ============================
///   1. SO EXISTE BICHO ONDE HA JOGADOR. Os planetas gerados nao tem fim, e um bicho num mundo vazio custaria
///      tique e rede pra ninguem ver (e ainda seguraria o mundo gerado no cache -- `PodarZonasVazias` so solta
///      zona sem corpo nenhum). Cada planeta guarda so um NUMERO, o estoque (<see cref="FaunaDoPlaneta"/>): com
///      gente la, o servidor materializa um bicho por segundo ate o estoque; sem ninguem, tira todos.
///      (Makyo Star e a excecao do catalogo: la nao ha fauna nenhuma -- `Animais.PlanetasSemFauna`.)
///   2. ELES APARECEM EM VOLTA DE QUEM ESTA LA, fora da tela (`Animais.DistMinDeGenteTiles` ..
///      `DistMaxDeGenteTiles`), em chao de verdade, longe de predio, de base e do ponto de chegada
///      (<see cref="LugarDeAnimal"/>). O que ficou longe de todo mundo volta pro estoque e reaparece perto.
///   3. MATAR TIRA DO ESTOQUE, e ele se repoe devagar (`Animais.SegundosPorReposicao`), com ou sem gente la:
///      sair do planeta e voltar nao enche a fauna.
///
/// ============================ UM BICHO ============================
///   * NASCE pela propria fabrica (<see cref="NascerAnimal"/>): a ficha e neutra (sem raca, classe ou skill),
///     com o BP FIXO do molde, e o corpo e uma folha de sprite so. Quem pedir um molde de animal ao `NascerNpc`
///     cai aqui.
///   * PENSA pelo ramo proprio do `TicarUmCorpo` (<see cref="GuiarOAnimal"/>): todos pastam EM VOLTA DA TOCA
///     (onde nasceram -- <see cref="Pastar"/>), e e isso que mantem a fauna fora da cidade; o manso corre de
///     quem bate; o bravo morde o jogador que entra no faro, ate a borda do territorio. A luta em si e a do
///     cerebro de todo NPC -- sem voo, sem ki, sem guarda e SEM INVESTIDA.
///   * MORRE quando cai: nocaute de bicho e morte. Sai pelo caminho de todo NPC morto (`_npcsPraTirar`), e o
///     cadaver que fica vira CARCACA (<see cref="AnimalVirouCarcaca"/>): carne na mochila dela, saqueavel pela
///     tecla E -- o mesmo verbo `saquear` de quem revista um nocauteado (`GameServer.Saque.cs`).
///
/// SEM CONSEQUENCIA DE GENTE: nada de karma, reputacao, saga, Zenkai, luto nem ganho de treino -- ver
/// <see cref="DerrotaComBicho"/> e <see cref="GolpeDeBichoNaoTreina"/>.
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// 1. OS REGISTROS
	// =====================================================================

	/// <summary>O que o servidor lembra de um bicho de pe, alem do corpo dele.</summary>
	private sealed class AnimalVivo
	{
		public int Id;
		public ulong Zona;

		/// <summary>A forca do molde: e pra ca que o BP volta a cada segundo.</summary>
		public double Bp;

		/// <summary>
		/// A TOCA: onde ele nasceu. O pasto e em volta dela (`Animais.PastoTiles`) e o territorio do bravo se mede
		/// dela (`Animais.TerritorioTiles`). Nao muda: e o que impede a fauna de ir parar dentro da cidade.
		/// </summary>
		public Vec2 Casa;

		/// <summary>
		/// ATE QUANDO ELE NAO NOTA NINGUEM: largou a presa na borda do territorio e esta voltando
		/// (`Animais.SegundosDeVolta`), ou derrubou a presa e deu a briga por ganha (a tregua,
		/// `Animais.SegundosDeTregua`).
		/// </summary>
		public long DeVoltaAte;

		/// <summary>Desde quando esta longe de todo jogador (0 = nao esta). Ver `Animais.DistQueSomeTiles`.</summary>
		public long LongeDesde;

		// ---- o pasto (ver `Pastar`): parado ate `ProximoPassoMs`, depois andando ate `Destino` ----
		public bool Andando;
		public Vec2 Destino;
		public long AndarAteMs;
		public long ProximoPassoMs;

		// ---- o desvio da fuga (ver `GuiarOAnimal`): ate quando corre de lado, e pra que lado ----
		public long DeLadoAte;
		public bool DesvioPelaEsquerda;
	}

	/// <summary>Os bichos de pe, por id do corpo.</summary>
	private readonly Dictionary<int, AnimalVivo> _animais = [];

	/// <summary>O ESTOQUE de um planeta: quantos bichos ele tem (de pe ou por materializar), e quando a conta foi feita.</summary>
	private sealed class FaunaDoPlaneta
	{
		public double Estoque;
		public long AtualizadoMs;
	}

	/// <summary>Por zona (o hash). Planeta que nunca recebeu ninguem nao tem entrada: o estoque dele e o cheio.</summary>
	private readonly Dictionary<ulong, FaunaDoPlaneta> _fauna = [];

	/// <summary>As carcacas no chao, por id do cadaver, com a hora em que cairam.</summary>
	private readonly Dictionary<int, (ServerPlayer Corpo, long CaiuMs)> _carcacas = [];

	/// <summary>
	/// A faixa PROPRIA de "lugar" dos animais -- o numero que semeia cada corpo (`SementeDe`). Longe das faixas
	/// do povoamento, das sagas e dos viloes.
	/// </summary>
	private ulong _lugarDosAnimais = 0x7A00_0000UL;

	/// <summary>A MOEDA DA BANCADA: o sorteio da especie cravado (0 a 1). Nulo em jogo.</summary>
	internal double? SorteioDeAnimalDeTeste;

	private static bool EhAnimal(ServerPlayer p) => p.Papel is { Tipo: TipoDeNpc.Animal };

	/// <summary>Este cadaver e a carcaca de um bicho (e nao o corpo de uma pessoa)?</summary>
	private bool EhCarcaca(ServerPlayer c) => c.ECadaver && _carcacas.ContainsKey(c.Id);

	/// <summary>Os moldes de bicho do `npcs.json` que tem arte. Lidos uma vez.</summary>
	private List<MoldeDeNpc> Especies() =>
		_especies ??= [.. (_moldes?.Todos ?? []).Where(m => m.Tipo == TipoDeNpc.Animal && m.Animal is { } a
															&& Animais.IndiceDaFolha(a.Folha) >= 0)];

	private List<MoldeDeNpc>? _especies;

	/// <summary>
	/// ESTE CHAO TEM FAUNA? A superficie de um planeta -- pre-feito da carta estelar ou gerado --, vivo e nao
	/// condenado. Ficam de fora o espaco, os interiores, o Mirante, a Sala do Tempo e o Outro Mundo: e a mesma
	/// pergunta de "isto e um planeta?" que a volta do mapa e a carta ja fazem (`Espaco.EhPlaneta`).
	///
	/// E, dos planetas do catalogo, os que o dono mandou nao terem nada (`Animais.PlanetasSemFauna`: Makyo
	/// Star). A lista e de nomes de MAPA, entao so vale pra zona pre-feita: um mundo gerado que por acaso (ou
	/// por batismo) tenha o mesmo nome continua com a fauna dele.
	/// </summary>
	private bool RecebeFauna(ZoneKey zona) =>
		Espaco.EhPlaneta(zona) && !ZonaMorta(zona) && !ZonaCondenada(zona)
		&& (zona.Kind != ZoneKey.KindPremade || Animais.PlanetaTemFauna(zona.Name));

	// =====================================================================
	// 2. O TIQUE (1 Hz)
	// =====================================================================

	private readonly Dictionary<ulong, ZoneKey> _zonasDeFauna = [];
	private readonly List<(int Id, ServerPlayer? Corpo)> _animaisPraTirar = [];
	private readonly List<ServerPlayer> _carcacasPraTirar = [];
	private readonly Dictionary<ulong, (int Quantas, ServerPlayer MaisAntiga, long CaiuMs)> _carcacasPorZona = [];

	/// <summary>
	/// O tique da fauna, a 1 Hz: quem esta de pe (a forca fixa, o abate de quem caiu, o que ficou longe), as
	/// carcacas (o prazo e o teto) e os nascimentos (um por planeta com gente, ate o estoque).
	/// </summary>
	private void TickDosAnimais()
	{
		long agora = NowMs();

		// OS PLANETAS COM GENTE. Morto conta: quem morreu continua com a tela ligada (ver `_zonasComGente`).
		_zonasDeFauna.Clear();
		foreach (ServerPlayer p in _players.Values)
			if (EhJogador(p) && RecebeFauna(p.Zone)) _zonasDeFauna[p.Zone.Hash] = p.Zone;

		// ---- os bichos de pe ----
		_animaisPraTirar.Clear();
		foreach (AnimalVivo a in _animais.Values)
		{
			// SAIU DO MUNDO POR OUTRA PORTA (um admin, uma bancada, o planeta que explodiu): so o registro sobra.
			if (!_players.TryGetValue(a.Id, out ServerPlayer? npc) || !EhAnimal(npc)) { _animaisPraTirar.Add((a.Id, null)); continue; }
			a.Zona = npc.Zone.Hash;

			if (npc.Ficha.dead) { ApressarACarcaca(npc, agora); continue; }

			// PLANETA SEM NINGUEM: ele volta pro estoque. Ver o cabecalho, item 1.
			if (!_zonasDeFauna.ContainsKey(a.Zona)) { _animaisPraTirar.Add((a.Id, npc)); continue; }

			// LONGE DE TODO MUNDO por tempo demais: idem, e reaparece no anel de alguem.
			if (!PertoDeGente(npc.Zone, npc.Pos, Animais.DistQueSomeTiles * ZoneCollision.TileSize))
			{
				if (a.LongeDesde == 0) a.LongeDesde = agora;
				else if (agora - a.LongeDesde >= (long)(Animais.SegundosAteSumir * 1000)) { _animaisPraTirar.Add((a.Id, npc)); continue; }
			}
			else a.LongeDesde = 0;

			ManterOAnimal(npc, a);
		}
		foreach ((int id, ServerPlayer? corpo) in _animaisPraTirar)
		{
			_animais.Remove(id);
			if (corpo != null) RemoverNpc(corpo);
		}

		// ---- as carcacas: a vazia, a vencida, a de planeta sem ninguem, e o teto por planeta ----
		_carcacasPraTirar.Clear();
		_carcacasPorZona.Clear();
		foreach ((ServerPlayer corpo, long caiu) in _carcacas.Values)
		{
			ulong zona = corpo.Zone.Hash;
			if (corpo.Mochila.Ocupados == 0 || agora - caiu >= (long)(Animais.SegundosDaCarcaca * 1000)
				|| !_zonasDeFauna.ContainsKey(zona))
			{ _carcacasPraTirar.Add(corpo); continue; }

			(int quantas, ServerPlayer antiga, long caiuAntiga) = _carcacasPorZona.GetValueOrDefault(zona, (0, corpo, caiu));
			_carcacasPorZona[zona] = caiu < caiuAntiga ? (quantas + 1, corpo, caiu) : (quantas + 1, antiga, caiuAntiga);
		}
		foreach ((int quantas, ServerPlayer antiga, long _) in _carcacasPorZona.Values)
			if (quantas > Animais.MaxCarcacasPorZona) _carcacasPraTirar.Add(antiga);
		foreach (ServerPlayer corpo in _carcacasPraTirar) DesfazerOCadaver(corpo);

		// ---- os nascimentos: UM por planeta com gente por segundo, ate o estoque ----
		foreach ((ulong hash, ZoneKey zona) in _zonasDeFauna)
		{
			ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
			if (mapa == null) continue;
			FaunaDoPlaneta fauna = FaunaDe(zona, mapa, agora);
			if (!Animais.Ligado) continue;

			int dePe = 0;
			foreach (AnimalVivo a in _animais.Values) if (a.Zona == hash) dePe++;
			if (dePe >= (int)Math.Floor(fauna.Estoque + 1e-9)) continue;

			// OS TETOS: o da fauna, e os do povoamento (corpo sem dono e corpo sem dono -- o bicho nao pode
			// lotar a zona nem o servidor por cima dos cidadaos).
			if (_animais.Count >= Animais.MaxNoServidor) continue;
			if (!CabeMaisUmCorpo(zona, Povoamento.MaxPorZona, Povoamento.MaxNoServidor, out _)) continue;

			MaterializarUmAnimal(zona, mapa);
		}

		// ---- a poda: planeta sem ninguem e com o estoque cheio volta a nao ter entrada ----
		if (_fauna.Count > 0)
		{
			_faunaPraEsquecer.Clear();
			foreach ((ulong hash, FaunaDoPlaneta f) in _fauna)
				if (!_zonasDeFauna.ContainsKey(hash)
					&& agora - f.AtualizadoMs >= (long)(Animais.SegundosPorReposicao * Animais.MaxPorPlaneta * 1000))
					_faunaPraEsquecer.Add(hash);
			foreach (ulong hash in _faunaPraEsquecer) _fauna.Remove(hash);
		}
	}

	private readonly List<ulong> _faunaPraEsquecer = [];

	/// <summary>
	/// O ESTOQUE DESTE PLANETA, atualizado ate agora. Na primeira vez ele nasce CHEIO; depois sobe sozinho com
	/// o relogio (`Animais.Repor`) -- e por isso a entrada so pode ser esquecida depois de tempo bastante pra
	/// ele ter enchido de qualquer jeito (a poda do tique).
	/// </summary>
	private FaunaDoPlaneta FaunaDe(ZoneKey zona, ZoneCollision mapa, long agora)
	{
		int alvo = Animais.AlvoDoPlaneta(mapa.Width, mapa.Height);
		if (!_fauna.TryGetValue(zona.Hash, out FaunaDoPlaneta? f))
			return _fauna[zona.Hash] = new FaunaDoPlaneta { Estoque = alvo, AtualizadoMs = agora };

		f.Estoque = Animais.Repor(f.Estoque, (agora - f.AtualizadoMs) / 1000.0, alvo);
		f.AtualizadoMs = agora;
		return f;
	}

	/// <summary>Ha algum jogador desta zona a menos de <paramref name="raioPx"/> deste ponto?</summary>
	private bool PertoDeGente(ZoneKey zona, Vec2 ponto, float raioPx)
	{
		float r2 = raioPx * raioPx;
		foreach (ServerPlayer p in ZoneList(zona.Hash))
			if (EhJogador(p) && (p.Pos - ponto).LengthSquared <= r2) return true;
		return false;
	}

	/// <summary>
	/// A MANUTENCAO DE UM BICHO DE PE, uma vez por segundo.
	///
	///   * QUEM CAIU, MORRE -- a rede de seguranca do <see cref="GuiarOAnimal"/>, que faz o mesmo no tique cheio
	///     mas so pensa com gente no planeta.
	///   * A FORCA E FIXA: o corpo passa pelos lacos de ficha de todo mundo (bater rende BP, `Fighter.AttackGain`),
	///     e sem este pino uma briga comprida deixaria o lobo mais forte do que o molde diz. E a mesma forma do
	///     pino do reflexo da mente (`BpDaMente`) e do chefe de saga (`TickDoRoteiro`).
	///   * BICHO PASTA: o estomago dele (que todo corpo do `_players` tem) fica cheio. Sem isto um lobo de horas
	///     de idade morreria de fome sozinho, que e um prazo que ninguem pediu.
	/// </summary>
	private void ManterOAnimal(ServerPlayer npc, AnimalVivo a)
	{
		Fighter f = npc.Ficha;
		if (f.KO && !Animais.NocauteNaoMataDeTeste) { AbaterAnimal(npc); return; }

		if (!Animais.BpSoltoDeTeste && Math.Abs(f.BP - a.Bp) > 1e-9)
		{
			f.BP = a.Bp;
			f.Statify();
			f.PowerLevel();
			npc.SigAtributos = "";
		}
		f.CurrentNutrition = Nutricao.Tanque(f.Metabolism);
	}

	// =====================================================================
	// 3. NASCER
	// =====================================================================

	/// <summary>Poe um bicho no planeta, num lugar que sirva. Nulo quando nenhum ponto sorteado serviu (o proximo segundo tenta de novo).</summary>
	private ServerPlayer? MaterializarUmAnimal(ZoneKey zona, ZoneCollision mapa)
	{
		List<MoldeDeNpc> especies = Especies();
		if (especies.Count == 0) return null;
		if (LugarDeAnimal(zona, mapa) is not { } onde) return null;

		MoldeDeNpc? molde = Animais.Escolher(especies, SorteioDeAnimalDeTeste ?? _rng.NextDouble());
		return molde == null ? null : NascerAnimal(molde, zona, onde, ++_lugarDosAnimais);
	}

	private readonly List<Vec2> _genteDaFauna = [];

	/// <summary>
	/// ONDE UM BICHO APARECE: um ponto sorteado no anel de algum jogador do planeta, que passe nas quatro
	/// perguntas -- fora do minimo de TODO jogador (nao brota na frente de ninguem), longe do ponto de chegada,
	/// e as duas do <see cref="Animais.LugarServe"/> (chao de verdade com espaco, longe de predio e de base).
	///
	/// SORTEIO E NAO VARREDURA de proposito: o mapa nao e pre-levantado (o `Habitat` de um mundo gerado de 1000
	/// tiles custaria mais de cem milissegundos no tique de todo mundo), e cada ponto custa uma enchente curta.
	/// Nenhum ponto serviu em <see cref="Animais.TentativasPorNascimento"/>? O tique tenta de novo no segundo
	/// seguinte, com outro sorteio.
	/// </summary>
	private Vec2? LugarDeAnimal(ZoneKey zona, ZoneCollision mapa)
	{
		_genteDaFauna.Clear();
		foreach (ServerPlayer p in ZoneList(zona.Hash))
			if (EhJogador(p)) _genteDaFauna.Add(p.Pos);
		if (_genteDaFauna.Count == 0) return null;

		const float T = ZoneCollision.TileSize;
		float min = Animais.DistMinDeGenteTiles * T, max = Animais.DistMaxDeGenteTiles * T;
		Vec2 berco = ChegadaDaZonaGerada(zona) ?? PontoDeNascimento(zona);
		float longeDoBerco = Animais.TilesLongeDoBerco * T;

		for (int t = 0; t < Animais.TentativasPorNascimento; t++)
		{
			Vec2 centro = _genteDaFauna[_rng.Next(_genteDaFauna.Count)];
			double ang = _rng.NextDouble() * Math.Tau;
			float dist = min + (float)_rng.NextDouble() * (max - min);
			Vec2 p = centro + new Vec2((float)Math.Cos(ang), (float)Math.Sin(ang)) * dist;

			int cx = (int)MathF.Floor(p.X / T), cy = (int)MathF.Floor(p.Y / T);
			if (cx < 0 || cy < 0 || cx >= mapa.Width || cy >= mapa.Height) continue;
			Vec2 onde = mapa.CentroDaCelula(cx, cy);

			if (!Animais.QualquerLugarDeTeste)
			{
				if (PertoDeGente(zona, onde, min)) continue;
				if ((onde - berco).Length < longeDoBerco) continue;
			}
			if (!Animais.LugarServe(mapa, cx, cy)) continue;
			return onde;
		}
		return null;
	}

	/// <summary>
	/// NASCE UM BICHO. A fabrica dele, e nao a de gente (`NascerNpc`): nao ha raca, classe, idade, skill, forma
	/// nem roupa pra sortear. O que e igual e o que faz dele um corpo do mundo -- a ficha e um `Fighter`, o corpo
	/// entra pelo <see cref="PorNoMundo"/> e a mente sai do molde (`CerebroDeNascimento`, que pra bicho ja vem
	/// sem investida, com a voz da especie e com a distancia de bote: `Animais.MenteDeBicho`).
	/// </summary>
	private ServerPlayer? NascerAnimal(MoldeDeNpc molde, ZoneKey zona, Vec2 pos, ulong lugar)
	{
		int folha = molde.Animal is { } ficha ? Animais.IndiceDaFolha(ficha.Folha) : -1;
		if (folha < 0)
		{
			GD.PushError($"[server] molde de animal '{molde.Id}' sem folha de sprite valida -- nao nasceu");
			return null;
		}

		ulong semente = SorteioDeNpc.SementeDe(SeedDoUniverso, molde.Id, Espaco.Misturar(zona.Hash, lugar, 0));
		string nome = molde.Nome.Length > 0 ? molde.Nome : molde.Id;
		double bp = Animais.BpDe(molde);
		var papel = new PapelDeNpc(molde, semente);

		var npc = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,
			Name = nome,
			Zone = zona,
			Pos = pos,
			Facing = (Facing)SorteioDeNpc.Sorteador(semente, "olhar").Next(4),
			Race = Animais.Raca,
			Genero = "Male",
			Idade = (int)Jandirus.Core.Races.Envelhecimento.IdadeAdulta,
			Planeta = zona.Name,
			LastInputMs = NowMs(),

			// A FICHA NEUTRA: sem proto de raca, todo modificador nasce em 1 e o que diferencia um bicho do outro e
			// o BP -- que e o pedido ("a forca deles e fixa"). A idade adulta e o que deixa o divisor de idade em 1
			// (`Envelhecimento.DivisorDeIdade`): o BP do molde e o BP que o jogador sente.
			Ficha = new Fighter { Name = nome, Race = Animais.Raca, BP = bp, Idade = Jandirus.Core.Races.Envelhecimento.IdadeAdulta },
			Livro = new Jandirus.Core.Skills.SkillBook(),
			Papel = papel,

			// O CORPO E A FOLHA DA ESPECIE (`VisualCatalog.CorpoSprite`), e mais nada: sem cabelo, roupa ou olhos.
			Visual = new Appearance { Corpo = folha, CorAura = CorDeAura.DeSemente(semente) },

			Cerebro = CerebroDeNascimento(papel),
			// SO CORPO A CORPO: nem voo, nem ki, nem sopro. (Ele tambem nao SABE nenhum dos tres -- o perfil e a
			// segunda tranca, pro dia em que alguem der uma skill a um molde de bicho.)
			Perfil = PerfilDeCombate.SoCorpo,

			// MORDIDA NAO ARREMESSA. Todo corpo nasce com o `knockbackon` ligado (o golpe joga o outro longe, e
			// quem bate na parede no fim do voo leva o dano da batida -- que nao tem o piso do golpe nao-letal).
			// Num lobo isso seria um jogador novato atravessando a clareira e morrendo num tronco: nem "animal
			// comum", nem o "bicho so derruba" que o golpe nao-letal promete.
			Knockback = false,

			Berco = new Jandirus.Core.Races.Berco
			{
				Planeta = zona.Name,
				Natal = zona.Name,
				Seed = zona.Seed,
				PreFeito = zona.Kind != ZoneKey.KindProcedural,
			},
		};

		PorNoMundo(npc);
		npc.Combate.MembrosFirmes = true;     // pata de bicho nao cai: a peca no chao seria um braco de gente
		npc.SpeedStat = Animais.VelocidadeDoPasso;   // o passo de bicho, e nao o que a ficha neutra daria por acidente
		SorteioDeNpc.Normalizar(npc.Ficha);   // tanque cheio, folego cheio, calmo: o BP expresso bate com o do molde
		TrocarAparencias(npc);

		_animais[npc.Id] = new AnimalVivo
		{
			Id = npc.Id, Zona = zona.Hash, Bp = bp, Casa = pos,
			// O PRIMEIRO PASSO E SORTEADO por bicho: sem isso a fauna inteira de um planeta andaria no mesmo tique.
			ProximoPassoMs = NowMs() + (long)(_rng.NextDouble() * Animais.PastoParadoMax * 1000),
		};

		// (So com o diagnostico da IA: a fauna vai e volta com quem anda pelo planeta, e uma linha por bicho
		// encheria o console de um servidor com gente.)
		if (_diagIa)
			GD.Print($"[animais] {nome} ({(molde.Animal!.Bravo ? "bravo" : "manso")}, BP {bp:N0}) apareceu em {zona.Name} "
				   + $"em ({pos.X:0}, {pos.Y:0}) | {_animais.Count} de pe no servidor");
		return npc;
	}

	// =====================================================================
	// 4. PENSAR -- o ramo do bicho no `TicarUmCorpo`
	// =====================================================================

	/// <summary>
	/// O QUE ESTE BICHO FAZ NESTE TIQUE. Devolve TRUE quando ha presa e o cerebro de combate assume (so o
	/// bravo); FALSE quando o tique ja foi resolvido aqui (caido, fugindo, pastando).
	///
	///   * CAIU, MORREU. Nocaute de bicho nao levanta: vira morte no tique seguinte, e a carcaca aparece logo.
	///   * BRAVO morde o jogador que entra no faro (ou que bateu nele), CORRENDO atras dele (<see cref="PassoDeBicho"/>),
	///     ate `Animais.TerritorioTiles` da toca; passou disso, larga, ignora todo mundo por uns segundos e volta
	///     pro pasto. Derrubou a presa? Da a briga por ganha e a deixa em paz (`Animais.SegundosDeTregua`).
	///   * QUEM APANHA E NAO TEM EM QUEM MORDER, CORRE de quem bateu, por `Animais.SegundosDeFuga`: o manso
	///     sempre (ele nunca tem presa), e o bravo quando quem bate esta fora do alcance dele -- voando, ou
	///     atirando de longe. Barrado no caminho, desvia de lado (`Animais.SegundosDeDesvio`).
	///   * O RESTO DO TEMPO, PASTA: anda de um ponto a outro em volta da toca (<see cref="Pastar"/>).
	/// </summary>
	private bool GuiarOAnimal(ServerPlayer npc, double dt, out ServerPlayer? presa, out Vec2 destino)
	{
		presa = null;
		destino = npc.Pos;
		long agora = NowMs();

		if (npc.Ficha.dead) { npc.Moving = false; ApressarACarcaca(npc, agora); return false; }
		if (npc.Ficha.KO)
		{
			npc.Moving = false;
			if (!Animais.NocauteNaoMataDeTeste) AbaterAnimal(npc);
			return false;
		}

		// SEM REGISTRO nao ha toca nem pasto: fica parado. (Nao acontece em jogo -- o registro nasce com o corpo
		// e so sai com ele --, e parado e melhor que solto pelo mapa.)
		if (!_animais.TryGetValue(npc.Id, out AnimalVivo? a)) { npc.Moving = false; return false; }

		bool bravo = npc.Papel?.Molde.Animal?.Bravo == true || Animais.MansoAtacaDeTeste;
		if (bravo && agora >= a.DeVoltaAte && PresaDoAnimal(npc, a, agora) is { } alvo)
		{
			if ((npc.Pos - a.Casa).Length <= Animais.TerritorioTiles * ZoneCollision.TileSize)
			{
				a.Andando = false;   // a briga interrompe o pasto
				presa = alvo;
				destino = alvo.Pos;
				return true;
			}

			// A BORDA DO TERRITORIO: larga a presa e volta. A marca do rancor cai junto, senao quem bateu nele
			// seria readotado no tique seguinte (ou ele fugiria dele) e a volta nunca comecaria. O primeiro passo
			// de volta sai agora: todo destino do pasto e em volta da toca.
			npc.PresaEngajada = 0;
			npc.RancorAte = 0;
			a.DeVoltaAte = agora + (long)(Animais.SegundosDeVolta * 1000);
			a.Andando = false;
			a.ProximoPassoMs = 0;
		}

		if (DeQuemOBichoFoge(npc, agora) is { } algoz)
		{
			a.Andando = false;
			Vec2 praLonge = npc.Pos - algoz.Pos;
			praLonge = praLonge.LengthSquared > 1e-6f ? praLonge.Normalized() : new Vec2(1, 0);

			// BARRADO NA FUGA: quis correr no tique passado e nao saiu do lugar (uma arvore, uma pedra, a beira
			// d'agua). Corre DE LADO por um instante e retoma o rumo -- sem isto ele ficaria empurrando o
			// obstaculo, parado, de costas pra quem bate. Barrado tambem de lado, troca de lado.
			if (!Animais.SemDesvioDeTeste && npc.QuisAndar && !npc.Moving)
			{
				if (agora < a.DeLadoAte) a.DesvioPelaEsquerda = !a.DesvioPelaEsquerda;
				a.DeLadoAte = agora + (long)(Animais.SegundosDeDesvio * 1000);
			}
			if (agora < a.DeLadoAte)
				praLonge = a.DesvioPelaEsquerda ? new Vec2(praLonge.Y, -praLonge.X) : new Vec2(-praLonge.Y, praLonge.X);

			Andar(npc, praLonge, correndo: true, dt);
			return false;
		}

		Pastar(npc, a, agora, dt);
		return false;
	}

	/// <summary>
	/// O PASTO: parado uns segundos, depois andando ate um ponto sorteado EM VOLTA DA TOCA
	/// (`Animais.PastoTiles`), e de novo. E o passeio dos NPCs (`Rotina.Passeio`) com uma diferenca que e a
	/// razao de ele ser proprio: la o destino e sorteado em volta de ONDE O CORPO ESTA, e um passeio assim e um
	/// andar de bebado -- em meia hora o lobo que nasceu no mato esta dentro da cidade, em cima de quem acabou
	/// de criar o personagem. Aqui todo destino e do disco da toca, entao o bicho nunca se afasta dela andando.
	///
	/// Quem foi tirado de la (fugiu, perseguiu, foi arremessado) volta do mesmo jeito: o proximo destino e do
	/// pasto. BARRADO (uma arvore, um lago) ele para e sorteia outro ponto depois -- nunca fica empurrando parede.
	/// </summary>
	private void Pastar(ServerPlayer npc, AnimalVivo a, long agora, double dt)
	{
		if (a.Andando)
		{
			Vec2 falta = a.Destino - npc.Pos;
			bool barrado = npc.QuisAndar && !npc.Moving;   // quis andar no tique passado e nao saiu do lugar
			if (falta.Length > Animais.ChegouNoPastoPx && !barrado && agora < a.AndarAteMs)
			{
				Andar(npc, falta.Normalized(), correndo: false, dt);
				return;
			}
			a.Andando = false;
			a.ProximoPassoMs = agora + (long)((Animais.PastoParadoMin
				+ _rng.NextDouble() * (Animais.PastoParadoMax - Animais.PastoParadoMin)) * 1000);
		}
		else if (agora >= a.ProximoPassoMs)
		{
			// UNIFORME NO DISCO (a raiz no raio): sem ela os destinos se amontoam no centro da toca.
			double ang = _rng.NextDouble() * Math.Tau;
			float raio = Animais.PastoTiles * ZoneCollision.TileSize * (float)Math.Sqrt(_rng.NextDouble());
			a.Destino = a.Casa + new Vec2((float)Math.Cos(ang), (float)Math.Sin(ang)) * raio;
			a.Andando = true;
			a.AndarAteMs = agora + (long)(Animais.SegundosDePasseio * 1000);
		}

		// PARADO NESTE TIQUE -- pelo mesmo atuador (um bicho jogado num lago ainda nada ate a margem).
		AplicarComando(npc, AtravessarAgua(npc, new Comando(), dt), dt);
		npc.QuisAndar = false;
	}

	/// <summary>
	/// O BRAVO CORRE ATRAS DA PRESA. O cerebro de combate ANDA ate o alvo -- gente fecha o resto da distancia
	/// com a investida do golpe, e bicho nao investe (`Animais.InvesteDeTeste`). Andando, um lobo nunca
	/// alcancaria nem quem se afasta a passo. Chamado pelo `TicarUmCorpo` entre a decisao do cerebro e o
	/// atuador: todo passo que o cerebro pede com presa vira passo de corrida (a corrida de todo corpo: 2,2x o
	/// passo, gastando Ki, e o atuador a recusa quando o Ki acaba -- bicho tambem cansa).
	/// </summary>
	private static Comando PassoDeBicho(in Comando c) =>
		c.Rumo.LengthSquared > 1e-6f && !c.Correndo ? c with { Correndo = true } : c;

	/// <summary>Um passo do bicho por conta do servidor (pasto, fuga): o MESMO atuador do cerebro e da rotina, agua inclusa.</summary>
	private void Andar(ServerPlayer npc, Vec2 rumo, bool correndo, double dt)
	{
		var c = new Comando { Rumo = rumo, Olhar = rumo, Correndo = correndo };
		AplicarComando(npc, AtravessarAgua(npc, c, dt), dt);
		npc.QuisAndar = true;
	}

	/// <summary>
	/// DE QUEM ESTE BICHO ESTA FUGINDO: de quem bateu nele, pelos `Animais.SegundosDeFuga` depois do ultimo
	/// golpe. A hora do golpe sai do proprio rancor (`MarcarAgressao` escreve `agora + SegundosDeRancor`): nao ha
	/// um segundo relogio pra discordar dele. (O bravo so chega aqui sem presa -- quem ele alcanca, ele morde.)
	/// </summary>
	private ServerPlayer? DeQuemOBichoFoge(ServerPlayer npc, long agora)
	{
		if (npc.RancorAte == 0) return null;
		long ultimoGolpe = npc.RancorAte - (long)(Povoamento.SegundosDeRancor * 1000);
		if (agora - ultimoGolpe >= (long)(Animais.SegundosDeFuga * 1000)) return null;
		return _players.TryGetValue(npc.UltimoAgressor, out ServerPlayer? q) && q.Id != npc.Id
			   && q.Zone.Hash == npc.Zone.Hash && !q.Ficha.dead ? q : null;
	}

	/// <summary>
	/// ATRAS DE QUEM O BRAVO VAI. So JOGADOR, vivo, de pe e AO ALCANCE DA MORDIDA pela altura (do chao nao se
	/// alcanca quem voa -- `Voo.PodeAcertar`); bicho nao briga com cidadao nem com outro bicho, que e o "AI nao
	/// ataca AI" de todo NPC hostil. E sempre dentro de um raio:
	///   1. a presa de antes, enquanto estiver na perseguicao (`Animais.PerseguicaoTiles`);
	///   2. quem bateu nele, se estiver na perseguicao -- o tiro de perto tambem chama a briga;
	///   3. o jogador mais perto dentro do FARO (`Animais.FaroTiles`).
	/// Os dois raios sao a histerese do `PresaDoHostil`, com os numeros de bicho: sem ela a briga pisca na borda.
	/// </summary>
	private ServerPlayer? PresaDoAnimal(ServerPlayer npc, AnimalVivo a, long agora)
	{
		const float T = ZoneCollision.TileSize;
		float perseguicao2 = Animais.PerseguicaoTiles * T * (Animais.PerseguicaoTiles * T);

		bool Serve(ServerPlayer o) =>
			o.Id != npc.Id && EhJogador(o) && !o.Ficha.dead && !o.Ficha.KO && o.Zone.Hash == npc.Zone.Hash
			&& AlcancaPelaAltura(npc, o);

		if (npc.PresaEngajada != 0 && _players.TryGetValue(npc.PresaEngajada, out ServerPlayer? antes))
		{
			// A PRESA CAIU: a briga esta ganha, e comeca a TREGUA (`Animais.SegundosDeTregua`). O rancor cai
			// junto -- quem levantar nao e readotado nem por ter batido nele antes.
			if (!Animais.SemTreguaDeTeste && EhJogador(antes) && antes.Zone.Hash == npc.Zone.Hash
				&& (antes.Ficha.KO || antes.Ficha.dead))
			{
				npc.PresaEngajada = 0;
				npc.RancorAte = 0;
				a.DeVoltaAte = agora + (long)(Animais.SegundosDeTregua * 1000);
				return null;
			}
			if (Serve(antes) && (antes.Pos - npc.Pos).LengthSquared <= perseguicao2) return antes;
		}

		ServerPlayer? nova = null;
		if (agora < npc.RancorAte && _players.TryGetValue(npc.UltimoAgressor, out ServerPlayer? algoz)
			&& Serve(algoz) && (algoz.Pos - npc.Pos).LengthSquared <= perseguicao2)
			nova = algoz;

		if (nova == null)
		{
			float perto = Animais.FaroTiles * T * (Animais.FaroTiles * T);
			foreach (ServerPlayer o in ZoneList(npc.Zone.Hash))
			{
				// QUEM ACABOU DE RENASCER (carencia) nao e alvo: o golpe nem encostaria nele.
				if (!Serve(o) || o.Combate is { Intocavel: true }) continue;
				float d2 = (o.Pos - npc.Pos).LengthSquared;
				if (d2 >= perto) continue;
				perto = d2;
				nova = o;
			}
		}

		npc.PresaEngajada = nova?.Id ?? 0;
		return nova;
	}

	// =====================================================================
	// 5. MORRER, E A CARCACA
	// =====================================================================

	/// <summary>
	/// BICHO QUE CAI, MORRE. O golpe nao-letal (o padrao de todo jogador) so nocauteia, e gente levanta do
	/// nocaute; bicho nao -- *"ao matar eles vc pode lootear"*. A morte e a de todo corpo (`CombatState.Morrer`),
	/// e a saida e a de todo NPC morto: o prazo vence, o `TickCombate` deixa o cadaver e tira o corpo.
	/// </summary>
	private void AbaterAnimal(ServerPlayer npc)
	{
		if (npc.Ficha.dead || npc.Combate == null) return;
		npc.Combate.Morrer(ignorarSeguro: true);
		ApressarACarcaca(npc, NowMs());
	}

	/// <summary>
	/// UM GOLPE ACABOU DE NOCAUTEAR ESTE CORPO (chamado pelo `AoPerderALuta`, o funil de toda derrota): se e
	/// bicho, ele morre AGORA, e nao no tique seguinte. A diferenca de um tique importa: toda tecnica que pede
	/// "um alvo nocauteado e vivo" (a absorcao do bio-androide, que cura quem absorve; a do Majin; a de alma)
	/// acharia o lobo nessa fresta, se o golpe e o verbo chegassem no mesmo pacote. Sem bicho caido e vivo, nao
	/// ha o que absorver -- e nao ha uma linha sobre bicho em nenhuma delas.
	///
	/// O nocaute que nao vem de golpe (fome, gravidade) nao passa pelo funil: quem o pega e o
	/// <see cref="GuiarOAnimal"/>, no tique seguinte.
	/// </summary>
	private void BichoCaiu(ServerPlayer vitima)
	{
		if (EhAnimal(vitima) && !Animais.NocauteNaoMataDeTeste) AbaterAnimal(vitima);
	}

	/// <summary>
	/// O bicho morto nao espera os dois segundos do `Alem.MsNoChao` (que existem pra PESSOA olhar o proprio
	/// corpo antes de ser puxada pro Outro Mundo): o relogio da morte vence agora, e a carcaca aparece no
	/// tique seguinte -- ja saqueavel.
	/// </summary>
	private static void ApressarACarcaca(ServerPlayer npc, long agora)
	{
		if (npc.RelogioDaMorte > agora) npc.RelogioDaMorte = agora;
	}

	/// <summary>
	/// O CADAVER DE UM BICHO E UMA CARCACA. Chamado pelo `DeixarOCadaver`, no instante em que o corpo sem dono
	/// sai do mundo -- o unico ponto por onde toda morte de NPC passa. Aqui o planeta perde um do estoque e a
	/// carne vai pra mochila da carcaca: e de la que o `saquear` da tecla E a tira, um pedaco por vez.
	///
	/// No DM o mob morto largava um `obj/items/food/corpse` chamado "[nome] Meat" (`mobDeath.dm:11-14`) ao lado
	/// do cadaver; aqui a carne esta DENTRO dele, e some com ele.
	/// </summary>
	private void AnimalVirouCarcaca(ServerPlayer morto, ServerPlayer carcaca)
	{
		long agora = NowMs();
		_animais.Remove(morto.Id);

		if (_fauna.TryGetValue(morto.Zone.Hash, out FaunaDoPlaneta? f))
		{
			// A REPOSICAO ANDA ATE AGORA antes de descontar: senao o tempo desde a ultima conta seria creditado
			// depois, por cima do abate.
			if (MapaDaZonaOuCatalogo(morto.Zone) is { } mapa) FaunaDe(morto.Zone, mapa, agora);
			f.Estoque = Math.Max(0, f.Estoque - 1);
		}

		int carne = morto.Papel?.Molde.Animal?.Carne ?? 0;
		if (carne > 0) carcaca.Mochila.Guardar(CatalogoDeItens.Carne, carne);
		_carcacas[carcaca.Id] = (carcaca, agora);
		if (carcaca.Combate != null) carcaca.Combate.MembrosFirmes = true;   // a carcaca tambem nao solta braco de gente

		// QUEM ABATEU FICA SABENDO ONDE ESTA A CARNE -- so ele (o ultimo que bateu, pelo rancor do bicho), e so se
		// ainda estiver no planeta. E o unico aviso deste sistema: sem ele o primeiro lobo que cai parece so um
		// corpo no chao, e ninguem adivinha que a tecla E serve nele.
		if (carne > 0 && agora <= morto.RancorAte && _players.TryGetValue(morto.UltimoAgressor, out ServerPlayer? quemAbateu)
			&& EhJogador(quemAbateu) && quemAbateu.Zone.Hash == morto.Zone.Hash)
			Avisar(quemAbateu, $"você abateu {morto.Name}: a carne está na carcaça (chegue perto e aperte E).");

		GD.Print($"[animais] {morto.Name} foi abatido em {morto.Zone.Name}: carcaca #{carcaca.Id} com {carne} de carne");
	}

	// =====================================================================
	// 6. OS CORTES -- bicho nao e gente
	// =====================================================================

	/// <summary>
	/// A DERROTA COM BICHO NO MEIO NAO TEM CONSEQUENCIA DE GENTE. Chamado pelo `AoPerderALuta`, o funil unico
	/// da derrota: apanhar de um leao nao da Zenkai (seria uma fonte de BP que nunca seca) nem acende o luto de
	/// quem viu (a porta do Super Saiyajin, que nao se abre com um lobo); e abater um bicho nao e matar ninguem
	/// -- sem karma, sem odio, sem sucessao.
	///
	/// A fusao que se desfaz quando o corpo cai continua valendo: ela vem ANTES deste corte, no funil.
	/// </summary>
	private static bool DerrotaComBicho(ServerPlayer vitima, ServerPlayer algoz) =>
		!Animais.DerrotaDeGenteDeTeste && (EhAnimal(vitima) || EhAnimal(algoz));

	/// <summary>
	/// APANHAR DE BICHO NAO TREINA. Com um bicho que nao cansa nem morre de velho, tudo o que o jogo paga a
	/// quem APANHA viraria treino parado: fique ao lado de um lobo que nao te machuca (ele tem 50 de BP, voce
	/// tem milhares) e deixe ele morder. Sao as duas coisas que o `Atacar` da a quem leva o golpe, e as duas
	/// perguntam aqui:
	///   * o ganho de luta de quem apanhou (`Fighter.AttackGain`), por golpe que encosta;
	///   * o relogio da "luta de verdade" (`CombatState.LutandoDeVerdade` -> `Fighter.IsInFight`), que e o que
	///     liga o treino de gravidade (`GravGain`), a experiencia das skills de luta e a maestria do estilo.
	///
	/// QUEM REVIDA ESTA LUTANDO: o golpe do proprio jogador no bicho liga o relogio dele como qualquer golpe, e
	/// rende o que socar o ar rende (ver `Animais.SemBonusDeLuta`). E a TAG de combate (o que se ve e se ouve)
	/// fica nos dois casos -- tem um lobo te mordendo.
	/// </summary>
	private static bool GolpeDeBichoNaoTreina(ServerPlayer atacante) =>
		EhAnimal(atacante) && !Animais.ApanharTreinaDeTeste;

	// =====================================================================
	// 7. PRA BANCADA
	// =====================================================================

	/// <summary>Quantos bichos de pe no servidor e nesta zona, e o estoque dela (-1 = sem entrada). SO PRA BANCADA.</summary>
	internal (int NoServidor, int NaZona, double Estoque) AnimaisDeTeste(ZoneKey zona) =>
		(_animais.Count, _animais.Values.Count(a => a.Zona == zona.Hash),
		 _fauna.TryGetValue(zona.Hash, out FaunaDoPlaneta? f) ? f.Estoque : -1);
}
