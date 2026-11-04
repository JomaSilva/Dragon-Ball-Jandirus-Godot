using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ OS VILOES DE SAGA SOLTOS (dono, 2026-10-31, o pedido 198g) ============================
/// O chefe de saga que vence e explode Namek ou a Terra continua vivo (os numeros e as escolhas moram em `Core/Npc/ViloesSoltos.cs`):
///   * A VITORIA: o `GanchoDeDestruicao` da saga (o chefe acendeu a destruicao do planeta) o SOLTA (<see cref="SoltarOVilao"/>): o molde,
///     o nome, o degrau e o BP pinado em que ele estava vao pro `viloes-soltos.json`, e o mundo ouve pra onde ele vai.
///   * (2026-10-31, o pedido 200) TODO VILAO SOLTO FICA DOIS DIAS DO JOGO NUM PLANETA. (O pedido 206) Nao vencido neles -- e sem luta
///     com gente perto dele (`SoltoEmLuta`), que segura o prazo --, ele SAI (`SairDoPlaneta`): o corpo e a tropa somem, a missao da
///     Patrulha acaba (ela perde o rastro), e ele fica sumido tres dias antes de atacar outro planeta (`VoltarAAtacar`) -- o INVASOR (o
///     Freeza, com o exercito; o Boo) sorteia um pre-feito vivo ou um mundo sorteado com gente; o CACADOR (o Cell) vai pro planeta do
///     mais forte. E assim ate alguem o vencer.
///   * (2026-10-31, o pedido 200) ONDE ELE ESTA NAO VAI PRO CHAT DE NINGUEM: vira MISSAO pra quem e da Patrulha Galactica
///     (`MissaoDeInvasao`, `GameServer.CargoDaPatrulha.cs` -- com o botao da carta e o zeni da missao) e noticia no jornal do dia.
///   * O CORPO SO EXISTE COM GENTE LA (*"so nascem nos planetas que estao invadindo"*): o primeiro jogador que pisa no planeta o encontra --
///     pela mesma receita do vilao do Inferno (`NascerNpc` + o BP pinado + o degrau, caca o planeta inteiro, Ki de chefe, letal) -- e a tropa
///     desembarca (12 de uma vez, e o resto um a cada 3 s, ate 40; os soldados do Freeza, que aqui atacam o povo de QUALQUER raca). Sem
///     ninguem por um minuto, ele e a tropa somem, e voltam quando alguem chega.
///   * A REVANCHE: derrota-lo (nocaute ou morte, por gente -- o Boo, so a morte, como no Inferno) o tira do jogo de vez: a tropa vai embora,
///     quem o derrotou ganha o karma de chefe derrotado, e o mundo e o Jornal Galactico ficam sabendo.
/// ===============================================================================================================================
/// Bancada: `--soltosteste`; fotos: `--diagsoltos`.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Um vilao solto -- o que vai pro disco (o corpo e a tropa sao do mundo, e voltam quando alguem chega).</summary>
	public sealed class VilaoSolto
	{
		public string Elo = "", Molde = "", Nome = "";
		public ViloesSoltos.Jeito Jeito;
		public bool ComExercito;
		public int Degrau;
		public double[] Bps = [];

		/// <summary>O planeta que ele invade agora (as tres partes da `ZoneKey`, como a obra e a nave guardam).</summary>
		public byte ZonaTipo;
		public string ZonaNome = "";
		public ulong ZonaSeed;

		/// <summary>Quando ele passa a outro planeta (ms reais): dois dias do jogo depois de chegar (o pedido 200).</summary>
		public long SaiEm;

		/// <summary>
		/// (2026-10-31, o pedido 206) SUMIDO: quando ele ataca de novo (ms reais), depois de sair de um planeta sem ser vencido. Zero = ele
		/// esta num planeta. Sumido, a `Zona` e a do ultimo planeta (o de onde ele saiu).
		/// </summary>
		public long VoltaEm;

		[JsonIgnore] public bool Sumido => VoltaEm > 0;

		[JsonIgnore] public ZoneKey Zona => new(ZonaTipo, ZonaNome, ZonaSeed);
		public void PorZona(ZoneKey z) { ZonaTipo = z.Kind; ZonaNome = z.Name; ZonaSeed = z.Seed; }

		/// <summary>O corpo de pe agora (0 = nenhum), desde quando o planeta esta sem gente, e o relogio da tropa.</summary>
		[JsonIgnore] public int Corpo;
		[JsonIgnore] public long SemNinguemDesde;
		[JsonIgnore] public long ProximoSoldado;
		[JsonIgnore] public bool Desembarcou;

		/// <summary>
		/// (2026-11-02, o pedido 227o) A CENA DA EXPLOSAO NO AR (o Cell ou o Boo no fim dos dois dias, com gente la): quando a esfera do Boo cai
		/// (ms reais). Zero = sem cena. Ver `GameServer.ViloesInvasores.cs`.
		/// </summary>
		[JsonIgnore] public long ExplodeEm;

		/// <summary>A marca dos soldados dele no registro da tropa (`_tropa`).</summary>
		[JsonIgnore] public string MarcaDaTropa => ViloesSoltos.PrefixoDaTropaSolta + Elo;
	}

	private List<VilaoSolto> _soltos = [];
	private bool _soltosCarregados, _pulsoDosSoltosInscrito;
	private long _proximoTiqueDosSoltos;

	/// <summary>A faixa propria de "lugar" (a semente do sorteio do NPC), longe das do povoamento, das sagas, do Inferno e da tropa.</summary>
	private ulong _lugarDosSoltos = 0x5017_0000UL;

	private string CaminhoDosSoltos => System.IO.Path.Combine(_store?.Pasta ?? ".", "viloes-soltos.json");

	/// <summary>A MOEDA DA BANCADA: o indice fixo do sorteio de planeta. Nulo em jogo.</summary>
	internal int? SorteioDoPlanetaDeTeste;

	/// <summary>DEFEITO INJETADO (`--soltosteste`): a vitoria nao solta ninguem -- o jogo de antes. Lido pelo <see cref="SoltarOVilao"/>. Falso em jogo, sempre.</summary>
	internal static bool VilaoNaoFicaSoltoDeTeste;

	// =====================================================================
	// O DISCO
	// =====================================================================
	private void GarantirOsSoltos()
	{
		if (_soltosCarregados) return;
		_soltosCarregados = true;
		try
		{
			if (System.IO.File.Exists(CaminhoDosSoltos)
				&& JsonSerializer.Deserialize<List<VilaoSolto>>(System.IO.File.ReadAllText(CaminhoDosSoltos),
					new JsonSerializerOptions { IncludeFields = true }) is { } lido)
				_soltos = lido;
		}
		catch (Exception e) { GD.PushWarning($"[server] viloes-soltos.json ilegivel: {e.Message}"); }
		if (_pulsoDosSoltosInscrito) return;
		_pulsoDosSoltosInscrito = true;
		InscreverNoPulso(() => _soltos.Count > 0, () =>
		{
			long agora = NowMs();
			if (agora < _proximoTiqueDosSoltos) return;
			_proximoTiqueDosSoltos = agora + 1000;
			TickDosSoltos();
		});
		LigarPulso();
	}

	private void SalvarOsSoltos()
	{
		if (_store == null || _limpezaEmCurso) return;
		try
		{
			string tmp = CaminhoDosSoltos + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(_soltos, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDosSoltos, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra salvar viloes-soltos.json: {e.Message}"); }
	}

	/// <summary>A LIMPEZA TOTAL (`GameServer.Limpeza.cs`): os viloes soltos, os corpos e as tropas somem.</summary>
	private void ZerarOsSoltos()
	{
		foreach (VilaoSolto v in _soltos) RecolherOSolto(v);
		_soltos.Clear();
		_soltosCarregados = true;
	}

	/// <summary>(a bancada) os soltos de agora.</summary>
	internal IReadOnlyList<VilaoSolto> SoltosDeTeste
	{
		get
		{
			GarantirOsSoltos();
			return _soltos;
		}
	}

	// =====================================================================
	// A VITORIA SOLTA O VILAO
	// =====================================================================
	/// <summary>
	/// O CHEFE VENCEU E EXPLODIU O PLANETA (chamado do `GanchoDeDestruicao`, antes de o corpo sair de cena): se o elo e um dos que soltam o
	/// chefe, ele vai pro registro -- com o degrau e o BP pinado de agora -- e pro primeiro planeta que invade (ou o do mais forte).
	/// </summary>
	private void SoltarOVilao(EloDaSaga elo, EstadoDoChefe ec, ServerPlayer corpo, ZoneKey? ondeDoPalco = null)
	{
		if (VilaoNaoFicaSoltoDeTeste || ViloesSoltos.DoElo(elo.Id) is not { } jeito || corpo.Papel == null) return;
		GarantirOsSoltos();
		_soltos.RemoveAll(v => string.Equals(v.Elo, elo.Id, StringComparison.OrdinalIgnoreCase));
		ZoneKey destruido = ZoneKey.Premade(elo.Planeta);
		var v = new VilaoSolto
		{
			Elo = elo.Id, Molde = ec.Molde, Nome = corpo.Name, Jeito = jeito.Jeito, ComExercito = jeito.ComExercito,
			Degrau = corpo.Papel.Estagio, Bps = [.. corpo.Papel.BpsPinados],
		};
		// (2026-11-02, o pedido 227) o cacador so fica com o planeta do mais forte se ele tem povo -- ver `PlanetaDoMaisForteComPovo`
		ZoneKey? alvo = v.Jeito == ViloesSoltos.Jeito.Cacador ? PlanetaDoMaisForteComPovo(destruido) : null;
		alvo ??= SortearPlanetaDeInvasao(destruido);
		if (ondeDoPalco is { } daCamera) alvo = daCamera;   // (so o palco do robo: o planeta da camera)
		if (alvo is not { } z) return;
		v.PorZona(z);
		long agora = NowMs();
		v.SaiEm = agora + (long)(ViloesSoltos.MinutosNoPlaneta * 60_000);
		_soltos.Add(v);
		SalvarOsSoltos();
		LigarPulso();   // o pulso se desliga sozinho sem trabalho: agora ha

		string onde = NomeDoPlaneta(z.Name);
		string frase = v.Jeito == ViloesSoltos.Jeito.Cacador
			? $"{v.Nome} deixou {NomeDoPlaneta(elo.Planeta)} em ruínas -- e agora caça os mais fortes do universo. Ele foi visto em {onde}."
			: v.ComExercito
				? $"{v.Nome} deixou {NomeDoPlaneta(elo.Planeta)} em ruínas -- e agora o império dele invade {onde}."
				: $"{v.Nome} deixou {NomeDoPlaneta(elo.Planeta)} em ruínas -- e agora invade {onde}.";
		// (2026-10-31, o pedido 200) SEM ANUNCIO NO CHAT DE NINGUEM: onde ele esta, so a Patrulha sabe (a missao dos membros) e o jornal do
		// dia conta. Quem pisa no planeta dele ve a cena de chegada, como sempre.
		Noticiar(TipoDeNoticia.VilaoAtacando, frase);
		MissaoDeInvasao(v);   // a Patrulha sabe onde
		GD.Print($"[soltos] {v.Nome} ('{v.Molde}', saga '{v.Elo}') ficou solto: {v.Jeito}{(v.ComExercito ? " com o exercito" : "")}, em {z.Name}");
	}

	/// <summary>
	/// O PLANETA DA INVASAO: os pre-feitos vivos e os mundos sorteados onde ha gente agora, menos o de agora (e o planeta prisao, que nao e
	/// da carta). Nulo = nenhum.
	/// </summary>
	private ZoneKey? SortearPlanetaDeInvasao(ZoneKey? fora)
	{
		// (2026-11-02, o pedido 227) PRIMEIRO OS PLANETAS COM POVO: os pre-feitos povoados e os mundos com civilizacao -- *"dao prioridade [...]
		// pra planetas q tenham npcs"*. So sem nenhum vale o sorteio de antes. Ver `GameServer.ViloesInvasores.cs`.
		if (!SoltoSemPrioridadeDePovoDeTeste && PlanetasComPovo(fora) is { Count: > 0 } comPovo)
			return comPovo[Math.Clamp(SorteioDoPlanetaDeTeste ?? _rng.Next(comPovo.Count), 0, comPovo.Count - 1)];

		var candidatos = new List<ZoneKey>();
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
		{
			ZoneKey z = Espaco.ZonaDe(p);
			if (!ZonaMorta(z) && (fora is not { } f || !z.Equals(f)) && !candidatos.Contains(z)) candidatos.Add(z);
		}
		foreach (ServerPlayer pl in Jogadores)
			if (pl.Zone.Kind == ZoneKey.KindProcedural && Espaco.EhPlaneta(pl.Zone) && !ZonaMorta(pl.Zone)
				&& (fora is not { } f2 || !pl.Zone.Equals(f2)) && !candidatos.Contains(pl.Zone))
				candidatos.Add(pl.Zone);
		if (candidatos.Count == 0) return null;
		int i = SorteioDoPlanetaDeTeste ?? _rng.Next(candidatos.Count);
		return candidatos[Math.Clamp(i, 0, candidatos.Count - 1)];
	}

	/// <summary>O PLANETA DO MAIS FORTE: o jogador online de maior poder expresso, num planeta vivo (fora o que acabou de cair). Nulo = nenhum.</summary>
	private ZoneKey? PlanetaDoMaisForte(ZoneKey? fora)
	{
		ServerPlayer? forte = Jogadores
			.Where(p => !p.Ficha.dead && Espaco.EhPlaneta(p.Zone) && !ZonaMorta(p.Zone) && (fora is not { } f || !p.Zone.Equals(f)))
			.OrderByDescending(p => p.Ficha.expressedBP).FirstOrDefault();
		return forte?.Zone;
	}

	// =====================================================================
	// O TIQUE (1 Hz, no pulso)
	// =====================================================================
	/// <summary>
	/// UM SEGUNDO DE CADA VILAO SOLTO: os dois dias no planeta (e a saida, que a luta segura), os dias sumido (e a volta, o cacador atras do
	/// mais forte), o corpo que nasce com gente no planeta (e a tropa) e some sem ela. Chamado pela bancada tambem, com o relogio adiantado.
	/// </summary>
	internal void TickDosSoltos()
	{
		GarantirOsSoltos();
		long agora = NowMs();
		foreach (VilaoSolto v in _soltos.ToList())
		{
			// O CORPO QUE SAIU POR OUTRA PORTA (o laco de combate, um admin): a vaga fica pro proximo que chegar.
			if (v.Corpo != 0 && !_players.ContainsKey(v.Corpo)) v.Corpo = 0;

			// (2026-11-02, o pedido 227o) A CENA DA EXPLOSAO DO CELL OU DO BOO NO AR: o fim e dela (`TickDaCenaDoSolto`)
			if (v.ExplodeEm != 0)
			{
				TickDaCenaDoSolto(v, agora);
				continue;
			}

			// (2026-10-31, o pedido 206) SUMIDO: passados os dias dele, ataca outro planeta -- o invasor sorteia, o cacador vai atras do mais
			// forte. O prazo novo vem antes da volta: a missao da Patrulha o le. Sem planeta nenhum agora, tenta de novo daqui a um minuto.
			if (v.Sumido)
			{
				if (agora < v.VoltaEm) continue;
				ZoneKey? volta = v.Jeito == ViloesSoltos.Jeito.Cacador ? PlanetaDoMaisForteComPovo(v.Zona) ?? SortearPlanetaDeInvasao(v.Zona) : SortearPlanetaDeInvasao(v.Zona);
				if (volta is not { } z) { v.VoltaEm = agora + 60_000; continue; }
				v.VoltaEm = 0;
				v.SaiEm = agora + (long)(ViloesSoltos.MinutosNoPlaneta * 60_000);
				VoltarAAtacar(v, z);
			}
			// O PLANETA QUE CAIU (outro o destruiu), ou os DOIS DIAS que todo vilao fica num planeta (o pedido 200) sem ninguem vence-lo: ele
			// SAI (o pedido 206) -- e no meio de uma luta, o prazo se estende ate ela acabar.
			else if (ZonaMorta(v.Zona) || (agora >= v.SaiEm && !SoltoEmLuta(v)))
			{
				// (2026-11-02, o pedido 227o) O CELL E O BOO EXPLODEM O PLANETA no fim dos dois dias (o Freeza so vai embora): com gente la, pela
				// cena da Terra (o fim e dela); sem ninguem, o planeta explode agora e ele sai -- ver `ExplodirOPlanetaDoSolto`.
				if (!ZonaMorta(v.Zona) && ViloesSoltos.ExplodeOPlaneta(v.Elo) && !SoltoNaoExplodeDeTeste && ExplodirOPlanetaDoSolto(v, agora)) continue;
				SairDoPlaneta(v, agora);
				continue;
			}

			// COM GENTE NO PLANETA ELE EXISTE; SEM, ELE SOME DEPOIS DE UM MINUTO.
			bool gente = Jogadores.Any(p => p.Zone.Equals(v.Zona) && !p.Ficha.dead);
			if (gente)
			{
				v.SemNinguemDesde = 0;
				if (v.Corpo == 0) NascerOSolto(v);
				if (v.ComExercito && v.Corpo != 0) ManterATropaSolta(v, agora);
			}
			else if (v.Corpo != 0)
			{
				if (v.SemNinguemDesde == 0) v.SemNinguemDesde = agora;
				else if (agora - v.SemNinguemDesde >= (long)(ViloesSoltos.SegundosSemNinguemPraSumir * 1000)) RecolherOSolto(v);
			}
		}
	}

	/// <summary>
	/// O CORPO DO VILAO SOLTO NO PLANETA QUE ELE INVADE -- pela receita do vilao do Inferno: `NascerNpc` + o BP pinado + o degrau, caca o
	/// planeta inteiro, Ki de chefe, e luta pra matar como todo chefe de saga. Quem esta no planeta ve a cena de chegada.
	/// </summary>
	private void NascerOSolto(VilaoSolto v)
	{
		ZoneKey zona = v.Zona;
		if (_moldes?.Get(v.Molde) is not { } molde) return;
		ulong lugar = ++_lugarDosSoltos;
		ServerPlayer? npc = NascerNpc(v.Molde, zona, PontoDeHabitante(zona, lugar), lugar);
		if (npc?.Papel == null) return;
		npc.Papel.BpsPinados = [.. v.Bps];
		npc.Papel.Estagio = Math.Clamp(v.Degrau, 0, Math.Max(molde.Estagios.Length - 1, 0));
		// (2026-11-02, o pedido 228) CHEFE DE SAGA ANTES DO DEGRAU: o que o degrau do Boo faz (`AjustarOBooAoDegrau`, no fim do
		// `EntrarNoDegrau`) so vale pra corpo de saga -- e ate o pedido 229 o desenho tambem era de la: marcado depois, o Kid solto nascia com
		// o corpo generico do molde (a foto `invasao-2`). Desde o 229 o desenho e do dado do degrau (o `"corpoDoBoo"`, como o do Freeza e o
		// do Cell): os tres nascem na forma em que venceram a saga.
		npc.Papel.DaSaga = true;
		EntrarNoDegrau(npc, cura: 0, anunciar: false);
		npc.Ficha.KiDeChefe = true;
		npc.Combate.Letal = true;
		v.Corpo = npc.Id;
		v.Desembarcou = false;
		v.ProximoSoldado = 0;

		string frase = v.Jeito == ViloesSoltos.Jeito.Cacador
			? $"{npc.Name.ToUpperInvariant()} ESTÁ AQUI -- e procura os mais fortes."
			: $"{npc.Name.ToUpperInvariant()} INVADE {NomeDoPlaneta(zona.Name).ToUpperInvariant()}!";
		AnunciarChegadaDoChefe(npc, zona.Name, frase, p => p.Zone.Equals(zona));
		GD.Print($"[soltos] {npc.Name} nasceu em {zona.Name} (degrau {npc.Papel.Estagio + 1}, BP {npc.Ficha.BP:N0})");
	}

	/// <summary>A TROPA DO IMPERIO: o desembarque de uma vez, e depois um a um ate o teto -- a reposicao de quem caiu e o mesmo passo.</summary>
	private void ManterATropaSolta(VilaoSolto v, long agora)
	{
		List<int> deles = [.. _tropa.Where(x => x.Value.Elo == v.MarcaDaTropa && _players.ContainsKey(x.Key)).Select(x => x.Key)];
		if (!v.Desembarcou)
		{
			for (int i = deles.Count; i < ViloesSoltos.SoldadosNoDesembarque; i++) NascerSoldadoSolto(v);
			v.Desembarcou = true;
			v.ProximoSoldado = agora + (long)(ViloesSoltos.SegundosEntreSoldados * 1000);
			AnunciarNoPlaneta(v.Zona, $"as naves do império de {v.Nome} pousam em {NomeDoPlaneta(v.Zona.Name)}: os soldados atacam a população!");
			return;
		}
		if (deles.Count >= ViloesSoltos.SoldadosDoImperio || agora < v.ProximoSoldado) return;
		NascerSoldadoSolto(v);
		v.ProximoSoldado = agora + (long)(ViloesSoltos.SegundosEntreSoldados * 1000);
	}

	/// <summary>Um soldado do imperio, pela mesma porta do soldado da saga -- e com o teto de lotacao do povoamento.</summary>
	private void NascerSoldadoSolto(VilaoSolto v)
	{
		if (!CabeMaisUmCorpo(v.Zona, Povoamento.MaxPorZona, Povoamento.MaxNoServidor, out _)) return;
		ulong lugar = ++_lugarDaTropa;
		if (NascerNpc(ViloesSoltos.MoldeDoSoldado, v.Zona, PontoDeHabitante(v.Zona, lugar), lugar) is { } s)
			_tropa[s.Id] = new SoldadoDaTropa { Elo = v.MarcaDaTropa };
	}

	/// <summary>O corpo e a tropa saem do mundo (sem gente no planeta, ou ele mudou de planeta). O vilao continua solto.</summary>
	private void RecolherOSolto(VilaoSolto v)
	{
		foreach (int id in _tropa.Where(x => x.Value.Elo == v.MarcaDaTropa).Select(x => x.Key).ToList())
		{
			_tropa.Remove(id);
			if (_players.TryGetValue(id, out ServerPlayer? s)) RemoverNpc(s);
		}
		if (v.Corpo != 0 && _players.TryGetValue(v.Corpo, out ServerPlayer? corpo)) RemoverNpc(corpo);
		v.Corpo = 0;
		v.SemNinguemDesde = 0;
		v.Desembarcou = false;
	}

	/// <summary>
	/// (2026-10-31, o pedido 206) O PRAZO ACABOU SEM NINGUEM VENCE-LO: o corpo e a tropa somem, a Patrulha perde o rastro (a missao de quem
	/// estava atras dele acaba) e o jornal do dia conta -- o chat de ninguem. Ele fica sumido <see cref="ViloesSoltos.MinutosSumido"/>
	/// minutos antes de atacar outro planeta.
	/// </summary>
	private void SairDoPlaneta(VilaoSolto v, long agora)
	{
		string onde = NomeDoPlaneta(v.ZonaNome);
		RecolherOSolto(v);
		v.VoltaEm = agora + (long)(ViloesSoltos.MinutosSumido * 60_000);
		SalvarOsSoltos();
		string frase = v.ComExercito
			? $"{v.Nome} e o império deixaram {onde} -- ninguém sabe onde vão atacar agora."
			: $"{v.Nome} deixou {onde} -- ninguém sabe onde vai atacar agora.";
		Noticiar(TipoDeNoticia.VilaoAtacando, frase);
		MissaoAoSumirOSolto(v);   // a Patrulha perde o rastro
		GD.Print($"[soltos] {v.Nome} saiu de {v.ZonaNome} sem ser vencido: ataca de novo em {ViloesSoltos.MinutosSumido} min");
	}

	/// <summary>
	/// (2026-10-31, o pedido 206) OS DIAS SUMIDO ACABARAM: ele ataca este planeta -- e a Patrulha (a missao dos membros) e o jornal do dia
	/// ficam sabendo onde, o chat de ninguem (o pedido 200). O corpo nasce quando alguem estiver la, como sempre.
	/// </summary>
	private void VoltarAAtacar(VilaoSolto v, ZoneKey nova)
	{
		v.PorZona(nova);
		SalvarOsSoltos();
		string onde = NomeDoPlaneta(nova.Name);
		string frase = v.Jeito == ViloesSoltos.Jeito.Cacador
			? $"{v.Nome} voltou atrás de adversários fortes: agora ele está em {onde}."
			: v.ComExercito
				? $"{v.Nome} e o império voltaram: agora invadem {onde}."
				: $"{v.Nome} voltou: agora invade {onde}.";
		Noticiar(TipoDeNoticia.VilaoAtacando, frase);
		MissaoDeInvasao(v);   // a missao volta
		GD.Print($"[soltos] {v.Nome} voltou a atacar: {nova.Name}");
	}

	/// <summary>
	/// (2026-10-31, o pedido 206) ELE ESTA NO MEIO DE UMA LUTA: o corpo dele brigou com alguem nos ultimos 90 s (`EmBrigaComAlguem`) e ha
	/// gente viva brigando a ate <see cref="ViloesSoltos.TilesDaLuta"/> tiles dele -- contra ele ou contra a tropa dele. Enquanto isso, o
	/// prazo do planeta se estende: ninguem perde a luta porque o relogio do vilao acabou.
	/// </summary>
	private bool SoltoEmLuta(VilaoSolto v)
	{
		if (v.Corpo == 0 || !_players.TryGetValue(v.Corpo, out ServerPlayer? corpo) || corpo.Combate is not { EmBrigaComAlguem: true }) return false;
		float raio = ViloesSoltos.TilesDaLuta * ZoneCollision.TileSize;
		return Jogadores.Any(p => p.Zone.Equals(corpo.Zone) && !p.Ficha.dead && p.Combate is { EmBrigaComAlguem: true }
								  && Vec2.Distance(p.Pos, corpo.Pos) <= raio);
	}

	// =====================================================================
	// A REVANCHE
	// =====================================================================
	/// <summary>
	/// O FUNIL DA DERROTA (`AoPerderALuta`): o corpo de um vilao solto derrotado por gente -- nocaute ou morte (o de Majin, so a morte) -- e a
	/// revanche ganha: ele sai do jogo de vez, a tropa vai embora, e quem o derrotou ganha o karma de chefe derrotado.
	/// </summary>
	private void SoltoFoiDerrotado(ServerPlayer vitima, ServerPlayer algoz, bool morreu)
	{
		if (_soltos.Count == 0 || _soltos.Find(x => x.Corpo == vitima.Id) is not { } v) return;
		if (!EhPessoa(algoz) || (!morreu && SoAMorteDerrota(vitima))) return;
		string onde = NomeDoPlaneta(v.ZonaNome);
		_soltos.Remove(v);
		RecolherOSolto(v);
		SalvarOsSoltos();
		KarmaPorDerrotarChefe(algoz);
		// A NOTICIA VEM ANTES DA MISSAO, com a chave da derrota: se quem o derrotou estava na missao da Patrulha contra ele, a linha da
		// Patrulha (com o cargo) toma o lugar desta -- uma derrota, uma noticia no jornal do dia (o pedido 200).
		Noticiar(TipoDeNoticia.VilaoDerrotado, $"{algoz.Name} derrotou {v.Nome} em {onde}: a ameaça que escapou da saga acabou.", ChaveDaDerrotaDoSolto(v));
		MissaoAoDerrotarSolto(v, algoz);   // (2026-10-31, o pedido 200) a missao da Patrulha contra ele, com o zeni
		string frase = v.ComExercito
			? $"{algoz.Name} derrotou {v.Nome} em {onde} -- e o império dele foge do planeta!"
			: $"{algoz.Name} derrotou {v.Nome} em {onde}!";
		AnunciarNoMundo(frase);
		GD.Print($"[soltos] {algoz.Name} derrotou {v.Nome} em {v.ZonaNome}");
	}

	/// <summary>A chave da derrota deste vilao solto no jornal do dia: a noticia do `SoltoFoiDerrotado` e a da missao cumprida sao uma so.</summary>
	private static string ChaveDaDerrotaDoSolto(VilaoSolto v) => "derrota-do-solto:" + v.Elo;
}
