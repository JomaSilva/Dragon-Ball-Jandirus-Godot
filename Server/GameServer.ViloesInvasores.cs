using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ OS VILOES SOLTOS ATRAS DE POVO (o pedido 227, 2026-11-02) ============================
/// O dono: *"viloes como cell e freeza dao prioridade, ao vencerem suas sagas e sairem invadindo planetas, pra planetas q tenham npcs, entao
/// todo planeta q eles invadirem vao ter npcs neles gerados na hora ao qual cell ou freeza vao atacar, freeza com seu exercito e ele mesmo, e
/// cell sozinho como normalmente. se o kid buu n lutar enquanto estiver no heaven por 20 minutos, ele vai sumir com o efeito do instant
/// transmission e o jogo vai falar q ele ficou entediado e foi procurar algum lugar pra destruir e fazer o caos, assim ele vai fazer a mesma
/// coisa q o freeza e o cell [...] esse planeta vai ter uma populaçao e vai atacar os npcs nativos, ele vai ficar no planeta por 2 dias e dps
/// sumir pra dps de uns dias aparecer no proximo e assim vai [...] cell e majin boo diferente do freeza, ao passarem os 2 dias eles vao
/// explodir o planeta q estao atacando. caso um jogador esteja presente vendo tudo, se for o cell ele vai ver o cell fazendo o kamehameha
/// igual ele fez na terra pra explodir o planeta (mesmas regras da terra de colisao etc), e o majin boo vai usar a mesma esfera de energia q
/// ele usou na terra"*. Os numeros moram em `Core/Npc/ViloesSoltos.cs`; o resto do vilao solto, em `GameServer.ViloesSoltos.cs`. Aqui:
///   * O PLANETA COM POVO (<see cref="PlanetasComPovo"/>): os pre-feitos povoados e os mundos com civilizacao da vizinhanca deles e de onde
///     ha gente. E ele que o invasor sorteia, e o cacador so fica com o planeta do mais forte se ele tem povo (<see cref="PlanetaDoMaisForteComPovo"/>).
///     O povo de um mundo sorteado nasce com ele (`GameServer.Civilizacao.cs`) -- na hora em que alguem chega pra ver.
///   * O VILAO SOLTO CACA O POVO (<see cref="PovoPraOSolto"/>, perguntado pela presa do hostil): sem jogador a vista, o habitante mais perto.
///   * O FIM DOS DOIS DIAS DO CELL E DO BOO (<see cref="ExplodirOPlanetaDoSolto"/>): com gente la e o corpo de pe, a cena da Terra -- o
///     Kamehameha do Cell (o mesmo raio 2x, a mesma disputa de ki: perdendo ela, ele morre) e a esfera rosa do Boo (a mesma Supernova); sem
///     ninguem, o planeta explode sozinho. Ele some do mesmo jeito que o que so vai embora, e dias depois ataca outro.
///   * O KID NO CEU (<see cref="VigiarOTedioDoKid"/>): vinte minutos sem lutar, e ele some pelo IT -- vira um vilao solto, sumido.
/// ==================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	// ============================ OS DEFEITOS INJETADOS (`--soltosteste`) -- falsos em jogo, sempre ============================
	/// <summary>O vilao volta a sortear qualquer planeta (o jogo de antes do pedido 227).</summary>
	internal static bool SoltoSemPrioridadeDePovoDeTeste;

	/// <summary>O Cell e o Boo so vao embora no fim dos dois dias, como o Freeza (o jogo de antes).</summary>
	internal static bool SoltoNaoExplodeDeTeste;

	/// <summary>O vilao solto so caca jogador (o "AI nao ataca AI" de antes).</summary>
	internal static bool SoltoNaoCacaOPovoDeTeste;

	/// <summary>O Kid fica no Ceu pra sempre (o jogo de antes).</summary>
	internal static bool KidNaoSeEntediaDeTeste;
	// ==============================================================================================================================

	/// <summary>Quantos sistemas pra cada lado de um pre-feito o vilao conhece os mundos com civilizacao.</summary>
	private const int RaioDeSistemasComPovo = 2;

	/// <summary>OS PRE-FEITOS POVOADOS: os planetas do plano de povoamento que estao no espaco (a Terra, Namek, Vegeta, Icer, Arlia, Makyo).</summary>
	private IEnumerable<ZoneKey> PreFeitosComPovo()
	{
		var nomes = new HashSet<string>((_moldes?.Plano ?? []).Select(l => l.Planeta), StringComparer.OrdinalIgnoreCase);
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (nomes.Contains(p.Nome)) yield return Espaco.ZonaDe(p);
	}

	/// <summary>
	/// OS MUNDOS COM CIVILIZACAO QUE O VILAO CONHECE: os dos sistemas em volta de cada pre-feito (<see cref="RaioDeSistemasComPovo"/> pra cada
	/// lado -- o universo e grande demais pra varrer) e os em que ha gente agora.
	/// </summary>
	private IEnumerable<ZoneKey> MundosComPovo()
	{
		var vistos = new HashSet<ulong>();
		foreach (PlanetaNoEspaco pre in Espaco.PreFeitos())
		{
			SistemaId c = SistemaId.De(pre.Pos);
			for (int dy = -RaioDeSistemasComPovo; dy <= RaioDeSistemasComPovo; dy++)
				for (int dx = -RaioDeSistemasComPovo; dx <= RaioDeSistemasComPovo; dx++)
				{
					if (Sistemas.Do(SeedDoUniverso, c.Sx + dx, c.Sy + dy) is not { } s) continue;
					foreach (PlanetaNoEspaco p in s.Planetas())
					{
						if (p.Premade || !Civilizacao.Tem(p)) continue;
						var z = ZoneKey.Procedural(p.Nome, p.Seed);
						if (vistos.Add(z.Hash)) yield return z;
					}
				}
		}
		foreach (ServerPlayer pl in Jogadores)
			if (Civilizacao.Tem(pl.Zone) && vistos.Add(pl.Zone.Hash)) yield return pl.Zone;
	}

	/// <summary>Este planeta tem povo? Um pre-feito povoado, ou um mundo sorteado com civilizacao.</summary>
	private bool TemPovo(ZoneKey z) => Civilizacao.Tem(z) || PreFeitosComPovo().Any(p => p.Equals(z));

	/// <summary>
	/// OS PLANETAS COM POVO pra uma invasao: vivos e INTEIROS (o que ja esta explodindo nao e alvo de ninguem), fora o de agora (e a prisao da
	/// Patrulha, que nao e povoada).
	/// </summary>
	private List<ZoneKey> PlanetasComPovo(ZoneKey? fora)
	{
		var lista = new List<ZoneKey>();
		foreach (ZoneKey z in PreFeitosComPovo().Concat(MundosComPovo()))
			if (!ZonaMorta(z) && !ZonaCondenada(z) && (fora is not { } f || !z.Equals(f)) && !lista.Contains(z)) lista.Add(z);
		return lista;
	}

	/// <summary>
	/// O PLANETA DO MAIS FORTE, SE ELE TEM POVO (o cacador, o Cell): senao, um planeta com povo como o do invasor. Nulo = nenhum.
	/// </summary>
	private ZoneKey? PlanetaDoMaisForteComPovo(ZoneKey? fora)
	{
		if (SoltoSemPrioridadeDePovoDeTeste) return PlanetaDoMaisForte(fora);
		return PlanetaDoMaisForte(fora) is { } z && TemPovo(z) ? z : SortearPlanetaDeInvasao(fora);
	}

	/// <summary>
	/// O POVO QUE O VILAO SOLTO CACA, quando nao ha jogador pra ele: o habitante de pe mais perto, no planeta inteiro (*"vai atacar os npcs
	/// nativos"*). So o corpo de um vilao solto -- o chefe da saga continua so atras de gente.
	/// </summary>
	private ServerPlayer? PovoPraOSolto(ServerPlayer npc)
	{
		if (SoltoNaoCacaOPovoDeTeste || !EhOCorpoDeUmSolto(npc)) return null;
		ServerPlayer? melhor = null;
		float perto = float.MaxValue;
		foreach (ServerPlayer o in ZoneList(npc.Zone.Hash))
		{
			if (o.Id == npc.Id || o.Peer != null || o.Papel is not { Tipo: TipoDeNpc.Cidadao } || EhCadaver(o) || o.Ficha.KO || o.Ficha.dead) continue;
			float d2 = (o.Pos - npc.Pos).LengthSquared;
			if (d2 < perto) { perto = d2; melhor = o; }
		}
		return melhor;
	}

	/// <summary>Este corpo e o de um vilao solto?</summary>
	private bool EhOCorpoDeUmSolto(ServerPlayer npc) => npc.Peer == null && _soltos.Any(v => v.Corpo == npc.Id);

	// =====================================================================
	// O FIM DOS DOIS DIAS DO CELL E DO BOO
	// =====================================================================
	/// <summary>
	/// O CELL OU O BOO, NO FIM DOS DOIS DIAS: o planeta explode. Com gente la e o corpo de pe, pela cena da Terra -- o Kamehameha do Cell (o tique
	/// dele, no `TorneioDoCell`, chama <see cref="ExplodirPeloKamehamehaDoSolto"/> quando o raio chega) ou a esfera rosa do Boo (o tique dos
	/// soltos espera a Supernova); devolve verdadeiro, e o fim e da cena. Sem ninguem: o planeta explode agora e devolve falso (ele sai como
	/// sempre).
	/// </summary>
	private bool ExplodirOPlanetaDoSolto(VilaoSolto v, long agora)
	{
		if (v.ExplodeEm != 0) return true;   // a cena ja esta no ar
		bool gente = Jogadores.Any(p => p.Zone.Equals(v.Zona) && !p.Ficha.dead);
		if (gente && v.Corpo != 0 && _players.TryGetValue(v.Corpo, out ServerPlayer? corpo) && !corpo.Ficha.dead)
		{
			if (string.Equals(v.Elo, "cell", StringComparison.OrdinalIgnoreCase))
			{
				if (_kamehamehaDoCell != null) return true;   // outro Kamehameha no ar: o do torneio, ou este, ainda carregando
				_kamehamehaDoCell = new KamehamehaDoCellVivo { Elo = v.Elo, Cell = corpo.Id, DoSolto = true };
				corpo.Combate.Levantar();
				AnunciarNoPlaneta(v.Zona, $"{corpo.Name} se cansou de esperar: ele vai explodir {NomeDoPlaneta(v.ZonaNome)} com um KAMEHAMEHA!");
				Falar(corpo, Jandirus.Net.Protocol.Fala.Diz, ViloesSoltos.FalaDoKamehamehaDoCellSolto);
				CarregarOKamehamehaDoCell(corpo);
			}
			else
			{
				ComecarASupernova(corpo, AtaqueDoUltimato.EsferaRosa);
				AnunciarNoPlaneta(v.Zona, $"{corpo.Name} ergue uma esfera rosa sobre {NomeDoPlaneta(v.ZonaNome)}...");
			}
			v.ExplodeEm = agora + (long)(Supernova.SegundosAteAExplosao * 1000);
			GD.Print($"[soltos] {v.Nome}: os dois dias acabaram -- a cena da explosao de {v.ZonaNome} comecou");
			return true;
		}

		ExplodirSemCena(v, 0);
		return false;
	}

	/// <summary>O planeta do vilao solto comeca a explodir (a destruicao de sempre), com o BP dele no degrau em que esta.</summary>
	private void ExplodirSemCena(VilaoSolto v, int idDoAlgoz)
	{
		double bp = v.Bps.Length == 0 ? 0 : v.Bps[Math.Clamp(v.Degrau, 0, v.Bps.Length - 1)];
		if (_players.TryGetValue(idDoAlgoz, out ServerPlayer? corpo)) bp = corpo.Ficha.expressedBP;
		if (!ComecarDestruicao(v.Zona, bp, $"vilao solto '{v.Elo}': {v.Nome}", idDoAlgoz))
			GD.PushWarning($"[soltos] {v.Nome} nao conseguiu explodir {v.ZonaNome} (ja condenado, ou nao e planeta)");
		Noticiar(TipoDeNoticia.VilaoAtacando, $"{v.Nome} explodiu {NomeDoPlaneta(v.ZonaNome)}.");
		GD.Print($"[soltos] {v.Nome} explodiu {v.ZonaNome}");
	}

	/// <summary>
	/// A CENA DO BOO, NO TIQUE DOS SOLTOS: a esfera rosa cai quando a Supernova chega ao fim -- o planeta comeca a explodir, ele some pelo IT
	/// (o clarao da saida, como na Terra) e sai do planeta. Devolve se a cena acabou.
	/// </summary>
	private bool TickDaCenaDoSolto(VilaoSolto v, long agora)
	{
		if (v.ExplodeEm == 0) return false;
		if (string.Equals(v.Elo, "cell", StringComparison.OrdinalIgnoreCase))
		{
			// o Cell termina pelo Kamehameha (`ExplodirPeloKamehamehaDoSolto`) ou morre na disputa; sem o corpo e sem o raio, acabou
			if (v.Corpo == 0 || _kamehamehaDoCell is not { DoSolto: true })
			{
				v.ExplodeEm = 0;
				return v.Corpo == 0;
			}
			return false;
		}
		if (agora < v.ExplodeEm) return false;
		int id = v.Corpo;
		if (_players.TryGetValue(id, out ServerPlayer? corpo))
		{
			corpo.SupernovaDesde = 0;
			corpo.CenaSegundos = 0;
			PiscarNaZona(corpo.Zone, corpo.Id, corpo.Pos);
		}
		ExplodirSemCena(v, id);
		v.ExplodeEm = 0;
		SairDoPlaneta(v, agora);
		return true;
	}

	/// <summary>O KAMEHAMEHA DO CELL SOLTO CHEGOU (o tique do Kamehameha, no `TorneioDoCell`): o planeta dele explode e ele sai.</summary>
	private void ExplodirPeloKamehamehaDoSolto(ServerPlayer cell)
	{
		_kamehamehaDoCell = null;
		if (_soltos.Find(v => v.Corpo == cell.Id) is not { } v) return;
		ExplodirSemCena(v, cell.Id);
		v.ExplodeEm = 0;
		SairDoPlaneta(v, NowMs());
	}

	// =====================================================================
	// O KID NO CEU
	// =====================================================================
	/// <summary>Desde quando (ms) o Kid do Ceu nao luta, e o proximo segundo da vigia.</summary>
	private long _kidSemLutarDesde, _proximaVigiaDoKid;

	/// <summary>
	/// O TEDIO DO KID NO CEU, a 1 Hz (chamado do tique da fuga dele): o Kid da saga de pe no Ceu que nao briga com ninguem (`EmBrigaComAlguem`)
	/// por <see cref="ViloesSoltos.MinutosDeTedioDoKid"/> minutos some pelo IT -- o clarao da saida, a frase pro mundo --, e vira um vilao solto
	/// SUMIDO: dias depois ele aparece num planeta com povo, como o Freeza e o Cell. O elo da saga acaba como o de um chefe que venceu.
	/// </summary>
	private void VigiarOTedioDoKid()
	{
		long agora = NowMs();
		if (agora < _proximaVigiaDoKid || KidNaoSeEntediaDeTeste) return;
		_proximaVigiaDoKid = agora + 1000;
		if (KidDoCeu() is not { } achado) { _kidSemLutarDesde = 0; return; }
		(ServerPlayer kid, EloDaSaga elo, EstadoDoElo est, EstadoDoChefe ec) = achado;
		if (_kidSemLutarDesde == 0 || kid.Combate.EmBrigaComAlguem) { _kidSemLutarDesde = agora; return; }
		if (agora - _kidSemLutarDesde < (long)(ViloesSoltos.MinutosDeTedioDoKid * 60_000)) return;
		_kidSemLutarDesde = 0;
		KidEntediado(kid, elo, est, ec);
	}

	/// <summary>O Kid da saga de pe no Ceu, com o elo e o estado dele -- ou nulo.</summary>
	private (ServerPlayer Kid, EloDaSaga Elo, EstadoDoElo Est, EstadoDoChefe Ec)? KidDoCeu()
	{
		foreach (EloDaSaga elo in _cadeia)
		{
			if (_sagas.Do(elo.Id) is not { } est) continue;
			foreach (EstadoDoChefe ec in est.Chefes)
				if (ec.NoCeu && ec.Corpo != 0 && _players.TryGetValue(ec.Corpo, out ServerPlayer? kid) && !kid.Ficha.dead
					&& string.Equals(kid.Zone.Name, SagaDoBuu.ZonaDoKid, StringComparison.Ordinal))
					return (kid, elo, est, ec);
		}
		return null;
	}

	/// <summary>
	/// O KID SE ENTEDIOU: o clarao do IT, a frase pro mundo, o elo fechado (como o do chefe que venceu: ele nao nasce mais no Ceu), e o registro
	/// de vilao solto, sumido, com o degrau e o BP de agora.
	/// </summary>
	internal void KidEntediado(ServerPlayer kid, EloDaSaga elo, EstadoDoElo est, EstadoDoChefe ec)
	{
		GarantirOsSoltos();
		PiscarNaZona(kid.Zone, kid.Id, kid.Pos);
		AnunciarNoMundo(ViloesSoltos.FraseDoTedioDoKid(kid.Name));
		_soltos.RemoveAll(x => string.Equals(x.Elo, elo.Id, StringComparison.OrdinalIgnoreCase));
		var v = new VilaoSolto
		{
			Elo = elo.Id, Molde = ec.Molde, Nome = kid.Name, Jeito = ViloesSoltos.Jeito.Invasor, ComExercito = false,
			Degrau = kid.Papel?.Estagio ?? 0, Bps = [.. kid.Papel?.BpsPinados ?? []],
		};
		v.PorZona(kid.Zone);
		v.VoltaEm = NowMs() + (long)(ViloesSoltos.MinutosSumido * 60_000);
		_soltos.Add(v);
		SalvarOsSoltos();
		LigarPulso();
		ec.NoCeu = false;
		ec.Fase = FaseDoChefe.Consumido;
		ec.Corpo = 0;
		RemoverNpc(kid);
		SalvarSagas();
		Noticiar(TipoDeNoticia.VilaoAtacando, $"{v.Nome} sumiu do Céu -- ninguém sabe onde vai aparecer.");
		GD.Print($"[soltos] {v.Nome} se entediou no Ceu e virou um vilao solto (sumido: aparece em {ViloesSoltos.MinutosSumido} min)");
	}
}
