using System.Text.Json;
using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ A PATRULHA GALACTICA -- A POLICIA DO JOGO (dono, 2026-10-31, o pedido 198) ============================
/// *"jogadores q matam muitos npcs de um planeta, destroem planetas , etc vao ganhando níveis de procurado na galaxia, ao qual npcs da
/// patrulha galatica irao atras. Mecanica: ao ser procurado caso o jogador esteja em um dos planetas prefeitos, spacepods tripuladas com os
/// npcs irao pousar perto do jogador procurado, onde sairao os npcs (...) e eles irao tentar NOCAUTEAR o jogador e prendê-lo no planeta
/// prisao."* Os numeros (niveis, recompensas, penas, o esquadrao, a demora) moram no Core: `Jandirus.Core.Npc.PatrulhaGalactica`.
///
///   * O CRIME: o habitante morto por um jogador (o gancho da reputacao, `MorreuUmCorpoSemDono` -- o defensor nativo de uma invasao
///     tambem), o planeta destruido (`ConsumarDestruicao`) e o patrulheiro derrubado (resistir a prisao) somam pontos na FICHA do
///     personagem (<see cref="FichaDaPatrulha"/>, `patrulha.json`); os pontos dao o NIVEL (1 a 5) e o nivel a RECOMPENSA. A ficha diz os
///     crimes ("matou 7 habitantes de Namek", "destruiu o planeta Vegeta").
///   * A CACADA: o procurado num planeta PRE-FEITO, ou num mundo em que a Patrulha ja o viu, ve a nave dela POUSAR perto dele (uma Spacepod
///     no chao) e os patrulheiros descerem -- de qualquer raca, com a armadura do exercito do Freeza -- pra NOCAUTEA-LO. O nivel decide
///     quantos descem e com quanto do poder dele. Num mundo sorteado em que nunca o viram, ele se esconde (*"Se ele for pra um planeta
///     procedural q a patrulha Galáctica nunca viu ele la, ele pode se esconder deles la"*). A zona segura do ponto de chegada continua
///     valendo pra eles como pra todo mundo (o pedido 113: ninguem luta la dentro).
///   * A MEMORIA: todo planeta em que a nave pousou fica anotado na ficha; voltar a ele e ser achado em 10 s, e nao em 60.
///   * A PRISAO: o procurado nocauteado por um patrulheiro e levado ao PLANETA PRISAO (`PatrulhaGalactica.ZonaDaPrisao`) pela pena do nivel
///     (ate 1 h); a ficha zera. Enquanto a pena corre nada o tira de la -- o campo de forca e a primeira pergunta de toda saida do planeta
///     (`PorQueAsEsferasPrendem`, que a decolagem, as naves e os teleportes ja perguntam), e o corpo vivo que aparecer fora dela (o morto
///     que reviveu, o login em outro lugar) e devolvido no proximo segundo.
///   * A DENUNCIA (`patrulha_denunciar`): quem esta no mesmo planeta de um procurado o denuncia -- a Patrulha anota o planeta e vem; se o
///     prender em 30 min, 25% da recompensa e de quem denunciou.
///   * A ENTREGA (`patrulha_chamar`): quem nocauteou um procurado chama a Patrulha pro corpo desmaiado -- a nave desce, leva o corpo, e
///     100% da recompensa e de quem chamou.
///   * O DEUS DA DESTRUICAO: no nivel mais alto, o procurado que derruba o esquadrao inteiro faz o Deus da Destruicao receber a tarefa de
///     elimina-lo (*"um criminoso causando desequilíbrio no universo"*); sem jogador no cargo, o proprio Bills desce, com todo o poder e o
///     kit do Deus. Quem o Deus elimina tem a ficha encerrada.
/// ===============================================================================================================================
/// Bancada: `--patrulhateste` (`GameServer.PatrulhaGalacticaTeste.cs`); fotos: `--diagpatrulha`.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>A FICHA DE UM PERSONAGEM NA PATRULHA -- o que vai pro `patrulha.json`, pela assinatura.</summary>
	public sealed class FichaDaPatrulha
	{
		public string Assinatura = "", Nome = "";

		/// <summary>Os pontos de crime (ver `PatrulhaGalactica.Nivel`). Zeram quando a pena e cumprida.</summary>
		public double Pontos;

		/// <summary>Os habitantes mortos por planeta -- a frase "matou N habitantes de X".</summary>
		public Dictionary<string, int> MortosPorPlaneta = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>Os planetas destruidos, e os patrulheiros derrotados.</summary>
		public List<string> PlanetasDestruidos = [];
		public int PatrulheirosDerrotados;

		/// <summary>ONDE A PATRULHA JA O VIU: as zonas (pela chave de texto) em que a nave dela pousou atras dele, ou em que ele foi denunciado.</summary>
		public List<string> Avistado = [];

		/// <summary>Ate quando ele cumpre pena no planeta prisao (ms reais; 0 = livre).</summary>
		public long PresoAte;

		/// <summary>Quem o denunciou e ate quando a denuncia paga.</summary>
		public string Denunciante = "", NomeDoDenunciante = "";
		public long DenunciaAte;

		/// <summary>A recompensa que ESTE personagem ganhou e ainda nao recebeu (estava fora do jogo na hora).</summary>
		public double RecompensaAReceber;

		/// <summary>(2026-10-31, o pedido 198i) O CARGO: e da Patrulha, desde quando, e a missao de agora. Ver `GameServer.CargoDaPatrulha.cs`.</summary>
		public bool Membro;
		public long MembroDesde;
		public MissaoDaPatrulha? Missao;
	}

	/// <summary>(2026-10-31, o pedido 198i) A MISSAO DE UM MEMBRO: o tipo, o alvo (o procurado jogador), o planeta e o prazo. Ver `GameServer.CargoDaPatrulha.cs`.</summary>
	public sealed class MissaoDaPatrulha
	{
		public byte Tipo;
		public string Alvo = "", NomeDoAlvo = "";
		public byte ZonaTipo;
		public string ZonaNome = "";
		public ulong ZonaSeed;
		public long Ate;

		/// <summary>O foragido de pe agora (0 = nenhum) -- e do mundo, e nao do disco.</summary>
		[System.Text.Json.Serialization.JsonIgnore] public int Npc;
		[System.Text.Json.Serialization.JsonIgnore] public ZoneKey Zona => new(ZonaTipo, ZonaNome, ZonaSeed);
		public void PorZona(ZoneKey z) { ZonaTipo = z.Kind; ZonaNome = z.Name; ZonaSeed = z.Seed; }
	}

	/// <summary>Uma cacada em andamento: o procurado, a zona, quando a nave chega, o esquadrao e a nave no chao.</summary>
	private sealed class Cacada
	{
		public int Alvo;
		public ZoneKey Zona;
		public long ChegaEm;
		public bool Pousou;
		public readonly List<int> Patrulheiros = [];

		/// <summary>
		/// (2026-10-31, o pedido 202) AS NAVES NO CHAO -- uma por patrulheiro --, e as que ainda descem: quando, onde, com quanto poder e com que
		/// semente. Ate o pedido 202 era uma nave so (`Nave`), e o esquadrao inteiro nascia em volta dela.
		/// </summary>
		public readonly List<int> Naves = [];
		public readonly List<(long Em, Vec2 Onde, double Poder, ulong Lugar)> NoCeu = [];
		public bool DeusChamado;
	}

	private Dictionary<string, FichaDaPatrulha> _patrulha = new(StringComparer.Ordinal);
	private bool _patrulhaCarregada, _pulsoDaPatrulhaInscrito;
	private long _proximoTiqueDaPatrulha;

	/// <summary>As cacadas, pelo id do procurado; de quem cada patrulheiro (e o Bills chamado) esta atras; e o Bills que a Patrulha chamou.</summary>
	private readonly Dictionary<int, Cacada> _cacadas = [];
	private readonly Dictionary<int, int> _patrulheiroDe = [];
	private readonly HashSet<int> _billsDaPatrulha = [];

	/// <summary>
	/// O NOCAUTE QUE VIRA PRISAO, no proximo pulso (o procurado -> quem o derrubou). Nao na hora: o funil da derrota roda no meio da
	/// resolucao do golpe, e mudar o corpo de zona ali deixaria o resto do funil (a fusao, o luto) olhando um corpo que ja saiu.
	/// </summary>
	private readonly Dictionary<int, int> _prisoesPendentes = [];

	/// <summary>A assinatura do criminoso que a Patrulha passou ao Deus da Destruicao jogador (a morte dele pelas maos do Deus encerra a ficha).</summary>
	private string _criminosoDoDeus = "";

	private string CaminhoDaPatrulha => System.IO.Path.Combine(_store?.Pasta ?? ".", "patrulha.json");

	/// <summary>DEFEITO INJETADO (`--patrulhateste`): a Patrulha nao pousa em planeta nenhum. Lido pelo <see cref="TickDaPatrulha"/>. Falso em jogo, sempre.</summary>
	internal static bool PatrulhaNaoCacaDeTeste;

	/// <summary>DEFEITO INJETADO (`--patrulhateste`): a Patrulha nao lembra onde viu o procurado -- todo planeta e novo. Falso em jogo, sempre.</summary>
	internal static bool PatrulhaSemMemoriaDeTeste;

	/// <summary>DEFEITO INJETADO (`--patrulhateste`): o planeta prisao nao segura ninguem. Lido pelo <see cref="PorQueAPrisaoPrende"/>. Falso em jogo, sempre.</summary>
	internal static bool PrisaoSemCampoDeForcaDeTeste;

	/// <summary>(bancada) O tique da Patrulha tambem ve corpo sem tela -- os corpos forjados da `--patrulhateste`. Falso em jogo, sempre.</summary>
	internal static bool PatrulhaVeCorpoSemTelaDeTeste;

	// =====================================================================
	// O DISCO
	// =====================================================================
	/// <summary>Carrega o `patrulha.json` uma vez e pendura o tique no pulso. Chamado de todo leitor e do tique do calendario.</summary>
	private void GarantirAPatrulha()
	{
		if (_patrulhaCarregada) return;
		_patrulhaCarregada = true;
		try
		{
			if (System.IO.File.Exists(CaminhoDaPatrulha)
				&& JsonSerializer.Deserialize<Dictionary<string, FichaDaPatrulha>>(System.IO.File.ReadAllText(CaminhoDaPatrulha),
					new JsonSerializerOptions { IncludeFields = true }) is { } lido)
				_patrulha = new Dictionary<string, FichaDaPatrulha>(lido, StringComparer.Ordinal);
		}
		catch (Exception e) { GD.PushWarning($"[server] patrulha.json ilegivel: {e.Message}"); }
		InscreverOPulsoDaPatrulha();
	}

	private void SalvarAPatrulha()
	{
		if (_store == null || _limpezaEmCurso) return;
		try
		{
			string tmp = CaminhoDaPatrulha + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(_patrulha, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDaPatrulha, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra salvar patrulha.json: {e.Message}"); }
	}

	/// <summary>A LIMPEZA TOTAL (`GameServer.Limpeza.cs`): as fichas, as cacadas, os patrulheiros de pe e o jornal somem.</summary>
	private void ZerarAPatrulha()
	{
		foreach (int alvo in _cacadas.Keys.ToList()) EncerrarACacada(alvo);
		_patrulha.Clear();
		_prisoesPendentes.Clear();
		_criminosoDoDeus = "";
		_patrulhaCarregada = true;
		ZerarOJornal();
	}

	/// <summary>(a vaga reciclada, `GameServer.VagaReciclada.cs`) o personagem novo da vaga nao herda a ficha do que foi apagado.</summary>
	private void EsquecerNaPatrulha(string assinatura)
	{
		GarantirAPatrulha();
		if (_patrulha.Remove(assinatura)) SalvarAPatrulha();
	}

	private FichaDaPatrulha FichaDe(ServerPlayer pl)
	{
		GarantirAPatrulha();
		if (!_patrulha.TryGetValue(pl.Assinatura, out FichaDaPatrulha? f))
			_patrulha[pl.Assinatura] = f = new FichaDaPatrulha { Assinatura = pl.Assinatura };
		f.Nome = pl.Name;
		return f;
	}

	private FichaDaPatrulha? FichaOuNulo(ServerPlayer pl)
	{
		GarantirAPatrulha();
		return pl.Assinatura.Length > 0 && _patrulha.TryGetValue(pl.Assinatura, out FichaDaPatrulha? f) ? f : null;
	}

	/// <summary>O nivel de procurado de agora (0 = livre).</summary>
	internal int NivelDeProcurado(ServerPlayer pl) => FichaOuNulo(pl) is { } f ? PatrulhaGalactica.Nivel(f.Pontos) : 0;

	/// <summary>Esta cumprindo pena no planeta prisao agora?</summary>
	internal bool PresoNaPatrulha(ServerPlayer pl) => FichaOuNulo(pl) is { } f && f.PresoAte > NowMs();

	/// <summary>Os crimes da ficha, numa frase (o motivo do procurado, no jornal e no aviso).</summary>
	internal static string CrimesDe(FichaDaPatrulha f)
	{
		var partes = new List<string>();
		foreach ((string planeta, int n) in f.MortosPorPlaneta.OrderByDescending(x => x.Value))
			partes.Add(n == 1 ? $"matou 1 habitante de {planeta}" : $"matou {n} habitantes de {planeta}");
		foreach (string p in f.PlanetasDestruidos) partes.Add($"destruiu o planeta {p}");
		if (f.PatrulheirosDerrotados > 0)
			partes.Add(f.PatrulheirosDerrotados == 1 ? "derrotou 1 patrulheiro" : $"derrotou {f.PatrulheirosDerrotados} patrulheiros");
		return partes.Count == 0 ? "nenhum crime" : string.Join("; ", partes);
	}

	/// <summary>Os numeros com o ponto de milhar do portugues, em qualquer maquina (o servidor da VPS nao e pt-BR).</summary>
	private static readonly System.Globalization.CultureInfo Pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

	// =====================================================================
	// O CRIME
	// =====================================================================
	/// <summary>O HABITANTE MORTO POR UM JOGADOR -- chamado do gancho da reputacao (`MorreuUmCorpoSemDono`), com o planeta natal do morto.</summary>
	private void CrimeContraOPovo(ServerPlayer matador, string planeta, bool realeza)
	{
		if (!EhPessoa(matador) || matador.Assinatura.Length == 0) return;
		FichaDaPatrulha f = FichaDe(matador);
		string nome = NomeDoPlaneta(planeta);
		f.MortosPorPlaneta[nome] = f.MortosPorPlaneta.GetValueOrDefault(nome) + 1;
		SomarCrime(matador, f, realeza ? PatrulhaGalactica.PontosPorRealeza : PatrulhaGalactica.PontosPorHabitante);
	}

	/// <summary>O PLANETA DESTRUIDO -- chamado do `ConsumarDestruicao`, com o algoz resolvido.</summary>
	private void CrimeDePlaneta(ServerPlayer algoz, string planeta)
	{
		if (!EhPessoa(algoz) || algoz.Assinatura.Length == 0) return;
		FichaDaPatrulha f = FichaDe(algoz);
		f.PlanetasDestruidos.Add(NomeDoPlaneta(planeta));
		SomarCrime(algoz, f, PatrulhaGalactica.PontosPorPlaneta);
	}

	/// <summary>Soma os pontos; ao subir de nivel, o procurado fica sabendo -- e o jornal tambem.</summary>
	private void SomarCrime(ServerPlayer quem, FichaDaPatrulha f, double pontos)
	{
		int antes = PatrulhaGalactica.Nivel(f.Pontos);
		f.Pontos += pontos;
		int depois = PatrulhaGalactica.Nivel(f.Pontos);
		if (depois > antes)
		{
			string recompensa = PatrulhaGalactica.Recompensa(depois).ToString("N0", Pt);
			Avisar(quem, $"PATRULHA GALÁCTICA: você agora é PROCURADO, nível {depois} de {PatrulhaGalactica.NivelMaximo} -- recompensa de "
						 + $"{recompensa} zeni pela sua cabeça ({CrimesDe(f)}).");
			GD.Print($"[patrulha] {quem.Name} subiu pro nivel {depois} ({f.Pontos:0} pontos): {CrimesDe(f)}");
			Noticiar(TipoDeNoticia.Procurado, $"{quem.Name} é PROCURADO pela Patrulha Galáctica (nível {depois}): {CrimesDe(f)}. "
											+ $"Recompensa: {recompensa} zeni.", chave: "procurado:" + quem.Assinatura);   // um procurado, uma noticia por dia
		}
		SalvarAPatrulha();
	}

	// =====================================================================
	// A CACADA
	// =====================================================================
	/// <summary>A chave de texto de uma zona na memoria da Patrulha.</summary>
	private static string ChaveDoAvistamento(ZoneKey z) => $"{z.Kind}:{z.Name}:{z.Seed}";

	/// <summary>A Patrulha CACA neste planeta? Pre-feito (fora o planeta prisao), ou um mundo em que ela ja o viu.</summary>
	private bool PlanetaCacavel(FichaDaPatrulha f, ZoneKey z) =>
		Espaco.EhPlaneta(z) && !NaZonaDaPrisao(z)
		&& (z.Kind == ZoneKey.KindPremade || (!PatrulhaSemMemoriaDeTeste && f.Avistado.Contains(ChaveDoAvistamento(z))));

	private static bool NaZonaDaPrisao(ZoneKey z) =>
		z.Kind == ZoneKey.KindPremade && string.Equals(z.Name, PatrulhaGalactica.ZonaDaPrisao, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// HA TRABALHO PRA PATRULHA? Uma ficha (a pena, a recompensa guardada, a cacada que pode comecar), uma cacada ou uma prisao pendente. E a
	/// pergunta da agenda dela no pulso -- e a de quem a REARMA: o pulso de 10 Hz se desliga sozinho quando nenhuma agenda tem trabalho, e
	/// quem poe trabalho tem de religa-lo (`LigarPulso`). O tique do ceu pergunta a cada segundo (`TickDoCalendario`).
	/// </summary>
	private bool PatrulhaTemTrabalho => _patrulha.Count > 0 || _cacadas.Count > 0 || _prisoesPendentes.Count > 0;

	/// <summary>O tique da Patrulha pendurado no pulso de 10 Hz (`InscreverNoPulso`): a prisao pendente a cada pulso, o resto uma vez por segundo.</summary>
	private void InscreverOPulsoDaPatrulha()
	{
		if (_pulsoDaPatrulhaInscrito) return;
		_pulsoDaPatrulhaInscrito = true;
		InscreverNoPulso(() => PatrulhaTemTrabalho, () =>
		{
			CumprirAsPrisoesPendentes();
			PousarAsNavesDaVez();   // (2026-10-31, o pedido 202) cada nave no instante dela -- no pulso, e nao de segundo em segundo
			long agora = NowMs();
			if (agora < _proximoTiqueDaPatrulha) return;
			_proximoTiqueDaPatrulha = agora + 1000;
			TickDaPatrulha();
		});
		LigarPulso();
	}

	/// <summary>
	/// UM SEGUNDO DA PATRULHA: a prisao (quem cumpre pena fica no planeta prisao; quem cumpriu e solto), e a cacada de cada procurado online
	/// -- a nave que pousa na hora dela, o esquadrao derrubado (outro em 2 min; no nivel maximo, o Deus da Destruicao), o procurado que sai do
	/// planeta (a nave vai embora). Chamado pela bancada tambem, com o relogio adiantado.
	/// </summary>
	internal void TickDaPatrulha()
	{
		GarantirAPatrulha();
		long agora = NowMs();
		PagarAsRecompensasGuardadas();
		TickDoCargo(agora);   // (2026-10-31, o pedido 198i) o cargo e as missoes -- ver `GameServer.CargoDaPatrulha.cs`
		PousarAsNavesDaVez();   // (o pedido 202) tambem aqui: a bancada anda o relogio e chama so este tique

		foreach (Cacada c in _cacadas.Values.ToList())
			if (!_players.TryGetValue(c.Alvo, out ServerPlayer? alvo) || !alvo.Zone.Equals(c.Zona) || alvo.Ficha.dead
				|| NivelDeProcurado(alvo) == 0 || PresoNaPatrulha(alvo))
				EncerrarACacada(c.Alvo);

		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			if ((pl.Peer == null && !PatrulhaVeCorpoSemTelaDeTeste) || !EhPessoa(pl) || pl.Assinatura.Length == 0) continue;
			if (FichaOuNulo(pl) is not { } f) continue;

			// ---- A PRISAO
			if (f.PresoAte > 0)
			{
				if (agora < f.PresoAte)
				{
					if (!pl.Ficha.dead && !NaZonaDaPrisao(pl.Zone))
						LevarPraPrisao(pl, "PATRULHA GALÁCTICA: a sua pena ainda não acabou -- a Patrulha te leva de volta ao "
										   + PatrulhaGalactica.NomeDaPrisao + ".");
					continue;
				}
				f.PresoAte = 0;
				SalvarAPatrulha();
				Avisar(pl, $"PATRULHA GALÁCTICA: a sua pena acabou. O campo de força do {PatrulhaGalactica.NomeDaPrisao} se abre pra você -- você está livre.");
			}

			int nivel = PatrulhaGalactica.Nivel(f.Pontos);
			if (nivel == 0 || pl.Ficha.dead || PatrulhaNaoCacaDeTeste || !PlanetaCacavel(f, pl.Zone)) continue;

			// ---- A CACADA DESTE PROCURADO, neste planeta
			if (!_cacadas.TryGetValue(pl.Id, out Cacada? c))
			{
				bool conhecido = !PatrulhaSemMemoriaDeTeste && f.Avistado.Contains(ChaveDoAvistamento(pl.Zone));
				c = new Cacada
				{
					Alvo = pl.Id, Zona = pl.Zone,
					ChegaEm = agora + (long)((conhecido ? PatrulhaGalactica.SegundosNoPlanetaConhecido : PatrulhaGalactica.SegundosNoPlanetaNovo) * 1000),
				};
				_cacadas[pl.Id] = c;
				if (conhecido) Avisar(pl, "PATRULHA GALÁCTICA: você já foi visto neste planeta -- uma nave está a caminho.");
				continue;
			}

			if (!c.Pousou)
			{
				if (agora >= c.ChegaEm) PousarOEsquadrao(pl, f, c, nivel);
				continue;
			}

			// ---- O ESQUADRAO DERRUBADO: os caidos sobem na nave e ela vai embora; no nivel maximo, o Deus da Destruicao; abaixo dele, outro
			// esquadrao em 2 min
			// (o pedido 202) so com todas as naves no chao: antes disso o esquadrao ainda esta chegando
			if (c.NoCeu.Count > 0 || !c.Patrulheiros.TrueForAll(id => !_players.TryGetValue(id, out ServerPlayer? npc) || npc.Ficha.dead || npc.Ficha.KO)) continue;
			RecolherOEsquadrao(c);
			Avisar(pl, "PATRULHA GALÁCTICA: o esquadrão caiu, e a nave decola com os feridos -- mas a Patrulha não desiste.");
			if (nivel >= PatrulhaGalactica.NivelMaximo && !c.DeusChamado)
			{
				c.DeusChamado = true;
				ChamarODeusDaDestruicao(pl, c);
				// o Bills desceu: ele E o esquadrao agora -- nada de nave nova enquanto ele estiver de pe
				if (c.Patrulheiros.Count > 0) continue;
			}
			c.Pousou = false;
			c.ChegaEm = agora + (long)(PatrulhaGalactica.SegundosEntreEsquadroes * 1000);
		}
	}

	/// <summary>
	/// AS NAVES DESCEM: (2026-10-31, o pedido 202) UMA SPACEPOD POR PATRULHEIRO, cada uma no seu instante e no seu ponto, com a cratera dos
	/// pods que chegam do espaco (`AbrirACrateraDoPod`) -- e de cada uma desce um. Os pontos ficam em volta do procurado, longe uns dos outros
	/// (`PontosDasNaves`); a primeira pousa agora e as outras vem uma a uma (`PousarAsNavesDaVez`, no pulso). Quantos e com quanto do poder
	/// dele, o nivel diz. O planeta fica na memoria da Patrulha. (Ate o pedido 202 era uma nave so, e o esquadrao inteiro nascia em volta.)
	/// </summary>
	private void PousarOEsquadrao(ServerPlayer pl, FichaDaPatrulha f, Cacada c, int nivel)
	{
		int quantos = PatrulhaGalactica.QuantosPatrulheiros(nivel);
		double poderDoAlvo = Math.Max(pl.Ficha.BP, pl.Ficha.expressedBP);
		List<Vec2> pontos = PontosDasNaves(pl, quantos);
		long em = NowMs();
		for (int i = 0; i < pontos.Count; i++)
		{
			c.NoCeu.Add((em, pontos[i], poderDoAlvo * PatrulhaGalactica.Forca(nivel), (ulong)(c.ChegaEm + i)));
			em += (long)(1000 * (PatrulhaGalactica.SegundosEntrePodsMin
								 + _rng.NextDouble() * (PatrulhaGalactica.SegundosEntrePodsMax - PatrulhaGalactica.SegundosEntrePodsMin)));
		}
		c.Pousou = true;

		string chave = ChaveDoAvistamento(pl.Zone);
		if (!f.Avistado.Contains(chave)) f.Avistado.Add(chave);
		SalvarAPatrulha();

		Avisar(pl, $"PATRULHA GALÁCTICA: {pontos.Count} naves da Patrulha descem do céu perto de você -- um patrulheiro em cada, pra te prender "
				   + $"(procurado, nível {nivel}: {CrimesDe(f)}).");
		AvisarPerto(pl, $"naves da Patrulha Galáctica descem do céu perto de {pl.Name}: {pontos.Count} patrulheiros vêm prendê-lo!");
		GD.Print($"[patrulha] {pontos.Count} nave(s) descem atras de {pl.Name} em {pl.Zone.Name}: um patrulheiro em cada, nivel {nivel}");
		PousarAsNavesDaVez();   // a primeira pousa agora
	}

	/// <summary>
	/// (2026-10-31, o pedido 202) OS PONTOS DE POUSO DAS NAVES: em volta do procurado, em terra livre, cada um a pelo menos
	/// `TilesEntrePods` tiles de todos os outros -- duas crateras uma em cima da outra bugam o desenho (o dono). Aneis de `VagasPorAnel`
	/// vagas, girados ao acaso, do mais justo pro mais largo: no primeiro as vagas vizinhas ficam a 3,8 tiles, e e o crivo da distancia que
	/// escolhe as que servem. Pode voltar menos que o pedido (o mapa sem lugar): o esquadrao desce com as que couberem.
	/// </summary>
	private List<Vec2> PontosDasNaves(ServerPlayer alvo, int quantos)
	{
		const int T = ZoneCollision.TileSize;
		var pontos = new List<Vec2>();
		var mapa = MapaDaZonaOuCatalogo(alvo.Zone);
		double giro = _rng.NextDouble() * Math.PI * 2;
		for (int anel = 0; anel < PatrulhaGalactica.AneisDoPouso && pontos.Count < quantos; anel++)
		{
			float raio = (PatrulhaGalactica.TilesDoPouso + anel * PatrulhaGalactica.TilesEntreAneis) * T;
			for (int k = 0; k < PatrulhaGalactica.VagasPorAnel && pontos.Count < quantos; k++)
			{
				double a = giro + k * 2 * Math.PI / PatrulhaGalactica.VagasPorAnel;
				Vec2 bruto = alvo.Pos + new Vec2(raio * (float)Math.Cos(a), raio * (float)Math.Sin(a));
				Vec2 livre = mapa?.PontoLivrePerto(bruto) ?? bruto;
				Vec2 p = new(MathF.Floor(livre.X / T) * T + T / 2f, MathF.Floor(livre.Y / T) * T + T / 2f);
				if (pontos.TrueForAll(q => Vec2.Distance(q, p) >= PatrulhaGalactica.TilesEntrePods * T)) pontos.Add(p);
			}
		}
		return pontos;
	}

	/// <summary>
	/// (2026-10-31, o pedido 202) AS NAVES QUE CHEGAM AGORA: cada uma cuja hora chegou pousa no ponto dela -- a Spacepod no chao (mobilia sem
	/// dono e fora do disco, como a do mapa), a cratera do pod e o patrulheiro dela descendo ao lado. Chamado no pulso de 10 Hz, pra os
	/// pousos nao virem todos no mesmo segundo.
	/// </summary>
	private void PousarAsNavesDaVez()
	{
		if (_cacadas.Count == 0) return;
		const int T = ZoneCollision.TileSize;
		long agora = NowMs();
		foreach (Cacada c in _cacadas.Values)
		{
			if (c.NoCeu.Count == 0 || !_players.TryGetValue(c.Alvo, out ServerPlayer? alvo) || !alvo.Zone.Equals(c.Zona)) continue;
			bool pousou = false;
			for (int i = 0; i < c.NoCeu.Count; i++)
			{
				(long em, Vec2 onde, double poder, ulong lugar) = c.NoCeu[i];
				if (agora < em) continue;
				c.NoCeu.RemoveAt(i--);
				var nave = new Obra { Id = _proximaObraId++, Tipo = "Spacepod", DoMapa = true, Aparafusada = true, X = onde.X, Y = onde.Y };
				nave.PorZona(c.Zona);
				_noChao.Add(nave);
				c.Naves.Add(nave.Id);
				AbrirACrateraDoPod(c.Zona, onde);
				Vec2 aoLado = MapaDaZonaOuCatalogo(c.Zona)?.PontoLivrePerto(onde + new Vec2(0, T)) ?? onde + new Vec2(0, T);
				if (NascerUmPatrulheiro(alvo, aoLado, poder, lugar) is { } npc) c.Patrulheiros.Add(npc.Id);
				pousou = true;
			}
			if (!pousou) continue;
			AplicarColisaoDasObras(c.Zona);
			MandarObras(c.Zona);
		}
	}

	/// <summary>
	/// UM PATRULHEIRO: o molde `patrulheiro_galactico` (qualquer raca), com a ARMADURA do exercito do Freeza -- uma das quatro da tabela do
	/// soldado, sorteada -- no lugar da roupa da raca, o poder que a Patrulha mandou, e o golpe que NAO MATA (ela quer o corpo nocauteado).
	/// </summary>
	private ServerPlayer? NascerUmPatrulheiro(ServerPlayer alvo, Vec2 onde, double poder, ulong lugar)
	{
		if (NascerNpc(PatrulhaGalactica.MoldeDoPatrulheiro, alvo.Zone, onde, lugar) is not { } npc) return null;
		npc.Ficha.BP = Math.Max(1, poder);
		npc.Ficha.Statify();
		npc.Ficha.PowerLevel();
		npc.Ficha.Ki = npc.Ficha.MaxKi;
		// NAO MATA, escrito: e o padrao de todo corpo sem dono hoje, mas a prisao depende disto -- o dia em que o padrao mudar, o patrulheiro
		// nao passa a matar o procurado que veio prender.
		npc.Combate.Letal = false;
		if (_visual != null)
		{
			string armadura = PatrulhaGalactica.Armaduras[(int)(lugar % (ulong)PatrulhaGalactica.Armaduras.Length)];
			npc.Visual.Roupa.Clear();
			npc.Visual.Roupa.AddRange(RoupaDeNpc.Vestir(_visual, npc.Race, npc.Genero, lugar, [armadura]));
			TrocarAparencias(npc);
		}
		_patrulheiroDe[npc.Id] = alvo.Id;
		return npc;
	}

	/// <summary>
	/// O PATRULHEIRO NO TIQUE DA IA (o ramo dele no `TicarUmCorpo`, antes do soldado): a presa e o procurado que ele veio prender, e so ele --
	/// de pe e no mesmo planeta. Caido, fica no chao (o esquadrao conta como derrubado).
	/// </summary>
	private bool GuiarOPatrulheiro(ServerPlayer npc, out ServerPlayer? presa, out Vec2 destino)
	{
		presa = null;
		destino = npc.Pos;
		if (npc.Ficha.dead || npc.Ficha.KO) { npc.Moving = false; return false; }
		if (!_patrulheiroDe.TryGetValue(npc.Id, out int alvoId) || !_players.TryGetValue(alvoId, out ServerPlayer? alvo)
			|| !alvo.Zone.Equals(npc.Zone) || alvo.Ficha.dead || alvo.Ficha.KO)
		{
			npc.Moving = false;
			return false;
		}
		presa = alvo;
		destino = alvo.Pos;
		return true;
	}

	/// <summary>A cacada acaba: os patrulheiros (de pe ou caidos) sobem na nave, e a nave vai embora.</summary>
	private void EncerrarACacada(int alvo)
	{
		if (!_cacadas.Remove(alvo, out Cacada? c)) return;
		RecolherOEsquadrao(c);
		foreach (int id in _patrulheiroDe.Where(x => x.Value == alvo).Select(x => x.Key).ToList())
		{
			_patrulheiroDe.Remove(id);
			_billsDaPatrulha.Remove(id);
			if (_players.TryGetValue(id, out ServerPlayer? npc)) RemoverNpc(npc);
		}
	}

	/// <summary>O esquadrao sai do mundo (os de pe e os caidos) e a nave decola.</summary>
	private void RecolherOEsquadrao(Cacada c)
	{
		foreach (int id in c.Patrulheiros)
		{
			_patrulheiroDe.Remove(id);
			_billsDaPatrulha.Remove(id);
			if (_players.TryGetValue(id, out ServerPlayer? npc)) RemoverNpc(npc);
		}
		c.Patrulheiros.Clear();
		c.NoCeu.Clear();   // (o pedido 202) as que ainda desciam voltam pro ceu
		if (c.Naves.Count > 0)
		{
			_noChao.RemoveAll(o => c.Naves.Contains(o.Id));
			c.Naves.Clear();
			AplicarColisaoDasObras(c.Zona);
			MandarObras(c.Zona);
		}
	}

	// =====================================================================
	// A DERROTA E A PRISAO
	// =====================================================================
	/// <summary>
	/// O FUNIL DA DERROTA (`AoPerderALuta`): o procurado nocauteado por um patrulheiro (ou pelo Bills chamado) vai preso no proximo pulso; o
	/// eliminado pelo Deus da Destruicao tem a ficha encerrada; e o patrulheiro derrubado pelo procurado e mais um crime (resistir a prisao).
	/// </summary>
	private void PatrulhaAoDerrotar(ServerPlayer vitima, ServerPlayer algoz, bool morreu)
	{
		// (2026-10-31, o pedido 198i) o foragido da missao derrotado pelo membro -- ver `GameServer.CargoDaPatrulha.cs`
		MissaoAoDerrotar(vitima, algoz);
		if (_patrulheiroDe.TryGetValue(algoz.Id, out int alvo) && alvo == vitima.Id)
		{
			if (morreu && _billsDaPatrulha.Contains(algoz.Id)) JusticaDoDeus(vitima, "o Deus da Destruição, Bills");
			else if (!morreu && NivelDeProcurado(vitima) > 0)
			{
				_prisoesPendentes[vitima.Id] = algoz.Id;
				LigarPulso();   // a prisao e no proximo pulso: ele tem de estar armado
			}
			return;
		}
		if (morreu && _criminosoDoDeus.Length > 0 && vitima.Assinatura == _criminosoDoDeus && ContaDoDeus.Length > 0
			&& string.Equals(algoz.Conta, ContaDoDeus, StringComparison.OrdinalIgnoreCase))
		{
			JusticaDoDeus(vitima, $"o Deus da Destruição, {algoz.Name}");
			return;
		}
		if (_patrulheiroDe.TryGetValue(vitima.Id, out int deQuem) && deQuem == algoz.Id && EhPessoa(algoz) && !_billsDaPatrulha.Contains(vitima.Id))
		{
			FichaDaPatrulha f = FichaDe(algoz);
			f.PatrulheirosDerrotados++;
			SomarCrime(algoz, f, PatrulhaGalactica.PontosPorPatrulheiro);
		}
	}

	/// <summary>As prisoes do nocaute, no pulso seguinte: o procurado ainda no jogo, vivo e procurado vai pro planeta prisao.</summary>
	private void CumprirAsPrisoesPendentes()
	{
		if (_prisoesPendentes.Count == 0) return;
		foreach (int id in _prisoesPendentes.Keys.ToList())
		{
			_prisoesPendentes.Remove(id);
			if (_players.TryGetValue(id, out ServerPlayer? pl) && !pl.Ficha.dead && NivelDeProcurado(pl) > 0)
				Prender(pl, null, 0, "nocauteado pela Patrulha Galáctica");
		}
	}

	/// <summary>
	/// A PRISAO: a pena do nivel no planeta prisao, a ficha zerada, a recompensa paga (a quem entregou o corpo, e os 25% de quem denunciou
	/// dentro do prazo), e o mundo ouve.
	/// </summary>
	private void Prender(ServerPlayer pl, ServerPlayer? entregador, double parte, string comoFoi)
	{
		FichaDaPatrulha f = FichaDe(pl);
		int nivel = PatrulhaGalactica.Nivel(f.Pontos);
		if (nivel == 0) return;
		double recompensa = PatrulhaGalactica.Recompensa(nivel);
		double minutos = PatrulhaGalactica.MinutosDaPena(nivel);
		string crimes = CrimesDe(f);
		long agora = NowMs();

		if (entregador != null && parte > 0) PagarARecompensa(entregador.Assinatura, entregador.Name, recompensa * parte, $"pela entrega de {pl.Name}");
		// quem denunciou E entregou leva a entrega so (100%, e nao 125%)
		if (f.Denunciante.Length > 0 && agora <= f.DenunciaAte && f.Denunciante != entregador?.Assinatura)
			PagarARecompensa(f.Denunciante, f.NomeDoDenunciante, recompensa * PatrulhaGalactica.ParteDaDenuncia, $"pela denúncia de {pl.Name}");

		f.PresoAte = agora + (long)(minutos * 60_000);
		ZerarOsCrimes(f);
		SalvarAPatrulha();
		EncerrarACacada(pl.Id);
		MissaoAoPrender(pl, entregador);   // (2026-10-31, o pedido 198i) a missao de quem o entregou -- ver `GameServer.CargoDaPatrulha.cs`

		if (pl.Ficha.KO) pl.Combate.Levantar();
		LevarPraPrisao(pl, $"PATRULHA GALÁCTICA: você foi preso ({comoFoi}) -- {minutos:0} minutos no {PatrulhaGalactica.NomeDaPrisao} pelos "
						   + $"seus crimes ({crimes}). Ninguém sai daqui antes de a pena acabar.");
		AnunciarNoMundo($"a Patrulha Galáctica prendeu {pl.Name} ({comoFoi}): {minutos:0} minutos no {PatrulhaGalactica.NomeDaPrisao}.");
		Noticiar(TipoDeNoticia.Preso, $"{pl.Name} foi PRESO pela Patrulha Galáctica ({comoFoi}): {minutos:0} minutos no "
									  + $"{PatrulhaGalactica.NomeDaPrisao}. Os crimes: {crimes}.");
		GD.Print($"[patrulha] {pl.Name} preso por {minutos:0} min ({comoFoi}); recompensa {recompensa:N0}");
	}

	private static void ZerarOsCrimes(FichaDaPatrulha f)
	{
		f.Pontos = 0;
		f.MortosPorPlaneta.Clear();
		f.PlanetasDestruidos.Clear();
		f.PatrulheirosDerrotados = 0;
		f.Denunciante = f.NomeDoDenunciante = "";
		f.DenunciaAte = 0;
	}

	/// <summary>O criminoso eliminado pelo Deus da Destruicao (o Bills chamado, ou o jogador com o titulo): a ficha se encerra, e o jornal conta.</summary>
	private void JusticaDoDeus(ServerPlayer vitima, string quem)
	{
		if (FichaOuNulo(vitima) is not { } f || PatrulhaGalactica.Nivel(f.Pontos) == 0) return;
		string crimes = CrimesDe(f);
		ZerarOsCrimes(f);
		SalvarAPatrulha();
		if (vitima.Assinatura == _criminosoDoDeus) _criminosoDoDeus = "";
		AnunciarNoMundo($"{quem} eliminou {vitima.Name}: o equilíbrio do universo foi restaurado.");
		Noticiar(TipoDeNoticia.Desequilibrio, $"{quem} eliminou {vitima.Name}, procurado por {crimes}: o equilíbrio do universo foi restaurado.");
		GD.Print($"[patrulha] {quem} eliminou {vitima.Name}; a ficha foi encerrada");
	}

	/// <summary>O corpo vai pro planeta prisao, no ponto de chegada dele.</summary>
	private void LevarPraPrisao(ServerPlayer pl, string frase)
	{
		ZoneKey prisao = ZoneKey.Premade(PatrulhaGalactica.ZonaDaPrisao);
		MoveToZone(pl.Id, prisao, PontoDeNascimento(prisao));
		Avisar(pl, frase);
	}

	/// <summary>
	/// O CAMPO DE FORCA: a primeira pergunta de toda saida do planeta prisao (a decolagem, as naves, os teleportes -- todos perguntam o
	/// `PorQueAsEsferasPrendem`). Vazio = livre.
	/// </summary>
	internal string PorQueAPrisaoPrende(ServerPlayer pl)
	{
		if (PrisaoSemCampoDeForcaDeTeste || !NaZonaDaPrisao(pl.Zone) || FichaOuNulo(pl) is not { } f) return "";
		long falta = f.PresoAte - NowMs();
		if (falta <= 0) return "";
		return PatrulhaGalactica.FraseDoCampoDeForca($"{falta / 60_000}min{falta % 60_000 / 1000:00}s");
	}

	// =====================================================================
	// A RECOMPENSA
	// =====================================================================
	/// <summary>Paga a quem esta online agora; a quem esta fora, guarda na ficha dele (paga no proximo segundo em que ele estiver no jogo).</summary>
	private void PagarARecompensa(string assinatura, string nome, double zeni, string porque)
	{
		if (assinatura.Length == 0 || zeni < 1) return;
		double valor = Math.Floor(zeni);
		ServerPlayer? quem = _players.Values.FirstOrDefault(p => p.Assinatura == assinatura && EhPessoa(p));
		if (quem != null)
		{
			quem.Ficha.Zeni += valor;
			MandarCarteira(quem);
			Persistir(quem);
			Avisar(quem, $"PATRULHA GALÁCTICA: a recompensa {porque} é sua -- {valor.ToString("N0", Pt)} zeni.");
			return;
		}
		GarantirAPatrulha();
		if (!_patrulha.TryGetValue(assinatura, out FichaDaPatrulha? f))
			_patrulha[assinatura] = f = new FichaDaPatrulha { Assinatura = assinatura, Nome = nome };
		f.RecompensaAReceber += valor;
		SalvarAPatrulha();
	}

	private void PagarAsRecompensasGuardadas()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			if (pl.Peer == null || FichaOuNulo(pl) is not { RecompensaAReceber: >= 1 } f) continue;
			double zeni = f.RecompensaAReceber;
			f.RecompensaAReceber = 0;
			pl.Ficha.Zeni += zeni;
			MandarCarteira(pl);
			Persistir(pl);
			SalvarAPatrulha();
			Avisar(pl, $"PATRULHA GALÁCTICA: uma recompensa esperava por você -- {zeni.ToString("N0", Pt)} zeni.");
		}
	}

	// =====================================================================
	// OS VERBOS: A DENUNCIA, A ENTREGA, A FICHA E O JORNAL
	// =====================================================================
	/// <summary>Os verbos da Patrulha e do jornal -- todos dentro das 24 letras do fio (o servidor le o nome com `GetString(24)`).</summary>
	internal const string VerboDenunciar = "patrulha_denunciar", VerboChamar = "patrulha_chamar", VerboFicha = "patrulha_ficha", VerboJornal = "jornal_ler";

	private bool ComandoDaPatrulha(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case VerboDenunciar: Denunciar(pl); return true;
			case VerboChamar: ChamarAPatrulha(pl); return true;
			case VerboFicha: VerAFicha(pl); return true;
			case VerboJornal: MandarOJornal(pl, aviso: false); return true;
			// (2026-10-31, o pedido 198i) o cargo -- ver `GameServer.CargoDaPatrulha.cs`
			case VerboEntrar: EntrarNaPatrulha(pl); return true;
			case VerboSair: SairDaPatrulha(pl); return true;
			case VerboMissao: VerAMissao(pl); return true;
			default: return false;
		}
	}

	/// <summary>
	/// `patrulha_denunciar` -- *"jogadores q sabem da localização do jogador podem denunciar a localização desse jogador e ganhar uma parte da
	/// recompensa (25%) e deixar pra patrulha resolver"*. Quem sabe e quem esta no MESMO planeta: os procurados daqui ficam anotados na
	/// memoria da Patrulha (mesmo num mundo sorteado), ela vem em 10 s, e a prisao dentro de 30 min paga 25% a quem denunciou.
	/// </summary>
	private void Denunciar(ServerPlayer pl)
	{
		GarantirAPatrulha();
		var aqui = _players.Values.Where(o => o != pl && EhPessoa(o) && o.Zone.Equals(pl.Zone) && !o.Ficha.dead && NivelDeProcurado(o) > 0
											  && !PresoNaPatrulha(o)).ToList();
		if (aqui.Count == 0) { Avisar(pl, "não há nenhum procurado pela Patrulha Galáctica neste planeta."); return; }
		if (NivelDeProcurado(pl) > 0) { Avisar(pl, "a Patrulha Galáctica não aceita a denúncia de um procurado."); return; }
		long agora = NowMs();
		foreach (ServerPlayer o in aqui)
		{
			FichaDaPatrulha f = FichaDe(o);
			string chave = ChaveDoAvistamento(o.Zone);
			if (!f.Avistado.Contains(chave)) f.Avistado.Add(chave);
			f.Denunciante = pl.Assinatura;
			f.NomeDoDenunciante = pl.Name;
			f.DenunciaAte = agora + (long)(PatrulhaGalactica.MinutosDaDenuncia * 60_000);
			if (_cacadas.TryGetValue(o.Id, out Cacada? c) && !c.Pousou)
				c.ChegaEm = Math.Min(c.ChegaEm, agora + (long)(PatrulhaGalactica.SegundosNoPlanetaConhecido * 1000));
			double parte = PatrulhaGalactica.Recompensa(PatrulhaGalactica.Nivel(f.Pontos)) * PatrulhaGalactica.ParteDaDenuncia;
			Avisar(pl, $"você denuncia {o.Name} à Patrulha Galáctica: se ele for preso nos próximos {PatrulhaGalactica.MinutosDaDenuncia:0} "
					   + $"minutos, {PatrulhaGalactica.ParteDaDenuncia * 100:0}% da recompensa ({parte.ToString("N0", Pt)} zeni) são seus.");
			GD.Print($"[patrulha] {pl.Name} denunciou {o.Name} em {o.Zone.Name}");
		}
		SalvarAPatrulha();
	}

	/// <summary>
	/// `patrulha_chamar` -- *"o próprio jogador derrotar o player procurado e chamar a patrulha pra capiturar o corpo desmaiado do procurado
	/// assim ganhando 100% da recompensa"*. O procurado NOCAUTEADO, a ate <c>TilesDaEntrega</c> tiles, por quem chama (o ultimo agressor):
	/// a nave desce, leva o corpo, e a recompensa inteira e de quem chamou.
	/// </summary>
	private void ChamarAPatrulha(ServerPlayer pl)
	{
		const int T = ZoneCollision.TileSize;
		ServerPlayer? caido = _players.Values
			.Where(o => o != pl && EhPessoa(o) && o.Zone.Equals(pl.Zone) && o.Ficha.KO && !o.Ficha.dead && NivelDeProcurado(o) > 0
						&& (o.Pos - pl.Pos).Length <= PatrulhaGalactica.TilesDaEntrega * T)
			.OrderBy(o => (o.Pos - pl.Pos).LengthSquared).FirstOrDefault();
		if (caido == null)
		{
			Avisar(pl, $"não há nenhum procurado desmaiado ao seu lado (a até {PatrulhaGalactica.TilesDaEntrega} tiles) pra entregar à Patrulha Galáctica.");
			return;
		}
		if (caido.UltimoAgressor != pl.Id)
		{
			Avisar(pl, $"quem derrubou {caido.Name} não foi você: só quem o nocauteou o entrega à Patrulha Galáctica.");
			return;
		}
		string frase = $"uma nave da Patrulha Galáctica desce, os patrulheiros recolhem o corpo desmaiado de {caido.Name} e sobem com ele.";
		Avisar(pl, frase);
		AvisarPerto(pl, frase);
		Prender(caido, pl, PatrulhaGalactica.ParteDaEntrega, $"entregue por {pl.Name}");
	}

	/// <summary>`patrulha_ficha` -- a ficha do proprio personagem: o nivel, os crimes, a recompensa, a pena.</summary>
	private void VerAFicha(ServerPlayer pl)
	{
		FichaDaPatrulha? f = FichaOuNulo(pl);
		if (f != null && f.PresoAte > NowMs())
		{
			long falta = f.PresoAte - NowMs();
			Avisar(pl, $"PATRULHA GALÁCTICA: você cumpre pena no {PatrulhaGalactica.NomeDaPrisao} -- faltam {falta / 60_000}min{falta % 60_000 / 1000:00}s.");
			return;
		}
		int nivel = f == null ? 0 : PatrulhaGalactica.Nivel(f.Pontos);
		Avisar(pl, nivel == 0
			? "PATRULHA GALÁCTICA: você não é procurado."
			: $"PATRULHA GALÁCTICA: PROCURADO, nível {nivel} de {PatrulhaGalactica.NivelMaximo} -- {CrimesDe(f!)}. Recompensa: "
			  + $"{PatrulhaGalactica.Recompensa(nivel).ToString("N0", Pt)} zeni.");
	}

	/// <summary>Os jogadores perto (o raio da vista) leem a frase -- sem o proprio, que ja leu a dele.</summary>
	private void AvisarPerto(ServerPlayer centro, string frase)
	{
		foreach (ServerPlayer o in ZoneList(centro.Zone.Hash).ToList())
			if (o != centro && EhPessoa(o) && (o.Pos - centro.Pos).Length <= RaioDaVista) Avisar(o, frase);
	}

	// =====================================================================
	// O DEUS DA DESTRUICAO
	// =====================================================================
	/// <summary>
	/// O CRIMINOSO DO NIVEL MAXIMO DERRUBOU A PATRULHA: o Deus da Destruicao jogador recebe a tarefa de elimina-lo (`DeusChamadoPelaPatrulha`,
	/// no sistema de tarefas do cargo); sem jogador no cargo, o proprio Bills desce no planeta dele -- com todo o poder (`PoderTotal`) e o kit
	/// do Deus, o Hakai inclusive -- e vai atras dele, e so dele.
	/// </summary>
	private void ChamarODeusDaDestruicao(ServerPlayer criminoso, Cacada c)
	{
		string planeta = NomeDoPlaneta(criminoso.Zone.Name);
		Noticiar(TipoDeNoticia.Desequilibrio, $"{criminoso.Name} derrotou a Patrulha Galáctica no nível máximo em {planeta}: um criminoso causa "
											+ "desequilíbrio no universo, e o Deus da Destruição foi avisado.");
		if (ContaDoDeus.Length > 0)
		{
			_criminosoDoDeus = criminoso.Assinatura;
			DeusChamadoPelaPatrulha(criminoso);
			Avisar(criminoso, "PATRULHA GALÁCTICA: você derrotou a Patrulha... e o DEUS DA DESTRUIÇÃO foi avisado de que você desequilibra o universo.");
			return;
		}

		const int T = ZoneCollision.TileSize;
		Vec2 perto = criminoso.Pos + new Vec2(-6 * T, 0);
		Vec2 onde = MapaDaZonaOuCatalogo(criminoso.Zone)?.PontoLivrePerto(perto) ?? perto;
		if (NascerNpc(BillsEWhis.MoldeDoBills, criminoso.Zone, onde, (ulong)NowMs()) is not { } bills) return;
		bills.Ficha.BP = BillsEWhis.PoderTotal;
		bills.Ficha.Statify();
		bills.Ficha.PowerLevel();
		bills.Ficha.Ki = bills.Ficha.MaxKi;
		ConsagrarPraLuta(bills, comHakai: true);
		_patrulheiroDe[bills.Id] = criminoso.Id;
		_billsDaPatrulha.Add(bills.Id);
		c.Patrulheiros.Add(bills.Id);
		Avisar(criminoso, "o céu escurece... o DEUS DA DESTRUIÇÃO, BILLS, desce em pessoa: \"Então é você quem desequilibra o meu universo.\"");
		AnunciarNoMundo($"o Deus da Destruição, Bills, desceu em {planeta} atrás de {criminoso.Name}!");
		GD.Print($"[patrulha] o Bills desceu atras de {criminoso.Name} em {criminoso.Zone.Name} (BP {bills.Ficha.BP:N0})");
	}
}
