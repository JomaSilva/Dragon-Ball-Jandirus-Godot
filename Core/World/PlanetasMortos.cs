namespace Jandirus.Core.World;

/// <summary>
/// A CHAVE DE UM PLANETA -- e ela **nao e o nome**.
///
/// ============================ POR QUE O DM NAO SERVE DE MODELO AQUI ============================
/// No original a lista de mortos e `PlanetDisableList += P.planetType` (`Area_Death.dm:144`), ou
/// seja: uma lista de STRINGS. Isso funciona la porque todo planeta do DM tem mapa proprio e nome
/// unico -- sao sete.
///
/// Aqui o nome de um planeta procedural sai de
/// `$"{NomeDeBioma(seed)}-{|Sx| % 1000}{|Sy| % 1000}{k}"` (<see cref="SistemaSolar.Planeta"/>), e
/// ele **nao e unico** por duas razoes independentes: o `% 1000` colide a cada mil celulas, e a
/// concatenacao e ambigua (`Sx=1,Sy=234` e `Sx=12,Sy=34` produzem a mesma string). Copiar a lista
/// por nome mataria planetas inteiros do outro lado da galaxia, calado -- exatamente a familia de
/// defeito que a regra 0.2 existe pra evitar.
///
/// Entao a chave e a SEED do planeta (`PlanetaNoEspaco.Seed`, derivada de
/// `Misturar(H ^ SalDoPlaneta, k, 0)` -- uma por orbita, unica por construcao). Pre-feito continua
/// por nome, que la e unico e legivel no save.
/// ==========================================================================================
/// </summary>
public readonly record struct ChaveDePlaneta(bool PreFeito, string Nome, ulong Seed)
{
	/// <summary>A chave de um corpo do mapa do universo.</summary>
	public static ChaveDePlaneta De(PlanetaNoEspaco p) =>
		p.Premade ? new ChaveDePlaneta(true, p.Nome, 0) : new ChaveDePlaneta(false, p.Nome, p.Seed);

	/// <summary>
	/// A chave da SUPERFICIE em que alguem esta -- nulo quando o lugar nao e um planeta.
	///
	/// O crivo e o <see cref="Espaco.EhPlaneta"/>, que ja e a definicao unica de "planeta" das duas
	/// pontas: o espaco, a Sala do Tempo, o Inferno e o interior de uma nave nao morrem.
	/// </summary>
	public static ChaveDePlaneta? Da(ZoneKey z)
	{
		if (!Espaco.EhPlaneta(z)) return null;
		return z.Kind == ZoneKey.KindPremade
			? new ChaveDePlaneta(true, z.Name, 0)
			: new ChaveDePlaneta(false, z.Name, z.Seed);
	}

	/// <summary>
	/// A chave em TEXTO -- e o que vira campo do JSON e chave do dicionario.
	///
	/// O `#` na frente do procedural nao e enfeite: sem ele um pre-feito que alguem chamasse de
	/// "1234" colidiria com a seed 1234. O prefixo faz os dois espacos de nome nunca se tocarem.
	/// </summary>
	public string Texto => PreFeito ? Nome : $"#{Seed}";

	/// <summary>O caminho de volta, pra ler o save. Devolve falso pra texto que nao e chave.</summary>
	public static bool Ler(string texto, string nome, out ChaveDePlaneta chave)
	{
		if (texto.Length > 1 && texto[0] == '#' && ulong.TryParse(texto[1..], out ulong s))
		{
			chave = new ChaveDePlaneta(false, nome, s);
			return true;
		}
		if (texto.Length > 0 && texto[0] != '#')
		{
			chave = new ChaveDePlaneta(true, texto, 0);
			return true;
		}
		chave = default;
		return false;
	}
}

/// <summary>
/// EM QUE PONTO DA MORTE UM PLANETA ESTA.
///
/// O DM guarda isto em TRES vars de area (`Area_Death.dm:5-7`) -- `death_proc_running` (tmp),
/// `planet_dying` (persistente) e `planet_death_stage` (tmp) --, e e dessa separacao que nasce a
/// armadilha registrada na memoria do projeto: `planet_dying` volta do disco e o estagio nao, entao
/// o tique de `Weather.dm:72-74` **retoma a morte lenta do estagio 0 a cada boot**. O planeta ficava
/// preso num pavio eterno.
///
/// Aqui e UM enum e UM registro, gravados juntos. Ver <see cref="EstadoDaMorte"/>.
/// </summary>
public enum FaseDaMorte : byte
{
	/// <summary>Vivo. Nao aparece no registro -- ausencia e a resposta.</summary>
	Vivo = 0,

	/// <summary>O pavio lento: os quatro estagios do `Planet_Death`.</summary>
	Morrendo = 1,

	/// <summary>Os ~5 min de tremor e explosao do `DestroyPlanet`, antes do commit.</summary>
	Explodindo = 2,

	/// <summary>Morto. `isDestroyed = 1` + entrada na `PlanetDisableList`.</summary>
	Destruido = 3,
}

/// <summary>
/// O ESTADO DE MORTE DE UM PLANETA -- **e ele e UM registro, de proposito**.
///
/// ============================ O QUE PERSISTE E O QUE NAO PERSISTE, ESCRITO ============================
/// Tudo o que esta nesta classe vai pro disco JUNTO, num arquivo so, numa escrita so. Nao ha campo
/// "tmp" aqui, e essa e a correcao do defeito do original: la o "esta morrendo" persistia e o
/// "em que estagio" nao, e o boot seguinte reconstruia um pavio novo.
///
/// O que **nao** persiste, e nao esta aqui porque nao e estado:
///   * o efeito de clima (`ForcarClima` remonta a partir da fase);
///   * o relogio de 3-11 s entre um tremor e outro (`Area_Death.dm:96`) -- e cadencia visual, e um
///     tremor a mais ou a menos depois de um reinicio nao muda resultado de jogo nenhum;
///   * quem estava na zona (a evacuacao acontece no commit e le a zona de entao).
/// =============================================================================================
/// </summary>
public sealed class EstadoDaMorte
{
	/// <summary>A chave em texto -- ver <see cref="ChaveDePlaneta.Texto"/>.</summary>
	public string Chave = "";

	/// <summary>O nome legivel, so pro anuncio e pro log. NAO e a identidade.</summary>
	public string Nome = "";

	public FaseDaMorte Fase = FaseDaMorte.Vivo;

	/// <summary>0 a 3 -- o `planet_death_stage` do DM, e o que decide a chance do `limit_life()`.</summary>
	public int Estagio;

	/// <summary>
	/// QUANTO FALTA DO PASSO ATUAL, em segundos.
	///
	/// SEGUNDOS QUE FALTAM, e nao um instante absoluto do relogio do universo. A diferenca importa:
	/// o relogio do mundo anda com o servidor DESLIGADO (ele e o relogio de parede, ver
	/// `GameServer.TempoDoMundo`), e um prazo absoluto faria uma noite de servidor fora consumir o
	/// pavio inteiro -- o planeta explodiria sozinho, que e exatamente o que o original aprendeu a
	/// nao fazer no Freeza (`BossEvents.dm:562`). Guardando o que FALTA, o pavio retoma de onde
	/// parou, sem reiniciar (o bug do DM) e sem pular (o bug do relogio de parede).
	/// </summary>
	public double Faltam;

	/// <summary>
	/// O `mexpressedBP` -- o BP EXPRESSO de quem matou o planeta, fixado no comeco
	/// (`Planets.dm:342`). E o numero que decide quem sobrevive ao commit, e por isso ele persiste:
	/// se o servidor cair no meio dos cinco minutos, a explosao tem que voltar com a mesma forca.
	/// </summary>
	public double BpDoAlgoz;

	/// <summary>O id de rede de quem causou -- PISTA, nao identidade. Ver `GameServer.Destruicao`.</summary>
	public int IdDoAlgoz;

	/// <summary>Quem/o que causou, pro log e pro anuncio ("saga freeza_vegeta", "Planet Destroy de X").</summary>
	public string Motivo = "";

	/// <summary>
	/// A ZONA deste planeta, reconstruida do registro.
	///
	/// E por isso que <see cref="Nome"/> e gravado alem da <see cref="Chave"/>: a chave de um mundo
	/// procedural e a SEED (a unica identidade honesta, ver <see cref="ChaveDePlaneta"/>), mas a
	/// `ZoneKey` dele e `(nome, seed)` -- o nome nao identifica, e mesmo assim faz parte do endereco.
	/// </summary>
	public ZoneKey Zona() =>
		Chave.Length > 0 && Chave[0] == '#' && ulong.TryParse(Chave[1..], out ulong s)
			? ZoneKey.Procedural(Nome, s)
			: ZoneKey.Premade(Nome);
}

/// <summary>
/// OS NUMEROS DOS DOIS CAMINHOS DE MORTE DE UM PLANETA, tirados do DM linha a linha.
///
/// ============================ OS DOIS CAMINHOS ============================
///   (a) MORTE LENTA -- `area/proc/Planet_Death` (`Area_Death.dm:8-50`). Quatro estagios; a cada um
///       morre uma fracao dos habitantes, e a partir do 2 o chao comeca a se desfazer perto de quem
///       esta olhando. No fim, chama a destruicao.
///   (b) DESTRUICAO IMEDIATA -- o verb `Planet_Destroy` (`Planets.dm:318-370`) + o
///       `area/proc/DestroyPlanet` (`Area_Death.dm:75-147`): 30 s de carga, ~5 min de tremor, e o
///       commit (dano, morte, evacuacao, planeta na lista).
/// ==========================================================================
///
/// Tudo aqui e `const`/`static readonly` e nao ha estado: e o Core dizendo QUANTO, e o servidor
/// decidindo QUANDO.
/// </summary>
public static class MortePlanetaria
{
	// =====================================================================
	// (a) O PAVIO LENTO
	// =====================================================================
	/// <summary>
	/// QUANTO DURA CADA ESTAGIO, em segundos. Os quatro `sleep()` do `Planet_Death`
	/// (`Area_Death.dm:30, 34, 39, 44`): 2000, 3000, 3000 e 4000 DECIMOS.
	///
	/// **Decimos, e a unidade e a armadilha ja registrada na memoria deste projeto**: `sleep(N)` do
	/// BYOND e N/10 segundos, e nao N/`world.fps`. 2000 sao 200 s, nao 166.
	/// </summary>
	public static readonly double[] SegundosDoEstagio = [200, 300, 300, 400];

	/// <summary>O ultimo estagio antes do `goto destroy`. Quatro estagios: 0, 1, 2 e 3.</summary>
	public const int UltimoEstagio = 3;

	/// <summary>O pavio inteiro: 1200 decimos = **20 minutos**.</summary>
	public static double PavioInteiro
	{
		get
		{
			double t = 0;
			foreach (double s in SegundosDoEstagio) t += s;
			return t;
		}
	}

	/// <summary>
	/// A CHANCE DE UM HABITANTE MORRER NESTE ESTAGIO, em porcento -- `prob((planet_death_stage+1)*25)`
	/// (`Area_Death.dm:55`). Da 25 / 50 / 75 / 100: no estagio 3 nao sobra ninguem.
	/// </summary>
	public static double ChanceDeMorrerPct(int estagio) => Math.Clamp((estagio + 1) * 25.0, 0, 100);

	/// <summary>
	/// A PARTIR DE QUE ESTAGIO O CHAO COMECA A SE DESFAZER -- `while(planet_death_stage>=2 ...)`
	/// (`Area_Death.dm:62`).
	/// </summary>
	public const int EstagioQueQuebraOChao = 2;

	/// <summary>
	/// A NOITE PERMANENTE comeca no estagio 1 (`Area_Death.dm:32`), e o efeito real mora no clima
	/// (`Weather.dm:166-167`: `planet_death_stage && < 4` forca `daylightcycle >= 6`).
	/// </summary>
	public const int EstagioDaNoiteEterna = 1;

	// =====================================================================
	// (b) A DESTRUICAO
	// =====================================================================
	/// <summary>`PDESTROY_KI_COST 1000` (`Planets.dm:315`) -- Ki FLAT, cobrado ANTES da confirmacao.</summary>
	public const double KiDaDestruicao = 1000;

	/// <summary>`PDESTROY_BP_PER_GRAV 10000` (`Planets.dm:316`) -- expressedBP exigido por 1x de gravidade.</summary>
	public const double BpPorGravidade = 10000;

	/// <summary>
	/// O BP EXPRESSO EXIGIDO NESTE CHAO -- `expressedBP >= PDESTROY_BP_PER_GRAV * Planetgrav`
	/// (`Planets.dm:326`).
	///
	/// ============================ POR QUE ISTO E UMA FUNCAO E NAO UMA LINHA NO VERB ============================
	/// Ela morava inline no `PlanetDestroy`, e por isso a regra so podia ser medida DE FORA, apertando o
	/// botao e vendo se ele recusa. Isso e caro (obriga a caçar por bisseccao o BP em que o verbo vira) e
	/// e ambiguo: gravidade maior tambem DERRUBA o `expressedBP` (`StatCurves.GravFelt`), entao "recusou
	/// num planeta pesado" nao prova que foi o limiar que subiu -- podia ser o poder que desceu.
	///
	/// Com a conta aqui, a bancada afirma as duas coisas separadas: que o limiar e `10000 x g` (aqui) e
	/// que o verbo obedece a ele (la). Uma casa so, no Core, como manda a regra 0.4.
	///
	/// O `Math.Max(g, 1)` e o piso: no DM a gravidade do espaco e 0 (`Planetgrav = 0`), e sem o piso o
	/// exigido seria ZERO -- ou seja, qualquer um destruiria qualquer coisa em gravidade baixa.
	/// ======================================================================================================
	/// </summary>
	public static double BpExigido(double planetgrav) => BpPorGravidade * Math.Max(planetgrav, 1);

	/// <summary>`sleep(300)` (`Planets.dm:355`) -- os 30 s de carga, a janela pra interromper.</summary>
	public const double SegundosDeCarga = 30;

	/// <summary>
	/// `sleep(3100)` (`Area_Death.dm:129`) -- o comentario do original diz *"five minutes lol"* e
	/// sao **310 s**. O numero literal vence o comentario.
	/// </summary>
	public const double SegundosDeExplosao = 310;

	/// <summary>`sleep(20 + rand(10,90))` (`Area_Death.dm:96`): de 3 a 11 s entre dois tremores.</summary>
	public const double TremorMin = 3, TremorMax = 11;

	/// <summary>
	/// `M.SpreadDamage(99)` (`Area_Death.dm:138`): 99 em CADA membro atingivel de quem tem
	/// `expressedBP <= mexpressedBP`. Quem esta acima do algoz atravessa o fim do mundo inteiro.
	/// </summary>
	public const double DanoDoCommit = 99;

	/// <summary>A duracao total de (b), do "sim" ate o commit: carga + explosao.</summary>
	public static double DestruicaoInteira => SegundosDeCarga + SegundosDeExplosao;

	// =====================================================================
	// AS PERGUNTAS QUE O RESTO DO JOGO FAZ
	// =====================================================================
	/// <summary>Este estado impede pousar, povoar, nascer e viajar? Morrendo AINDA nao -- so morto.</summary>
	public static bool EstaMorto(FaseDaMorte f) => f == FaseDaMorte.Destruido;

	/// <summary>O ceu esta sob o efeito da morte? Vale do estagio da noite eterna ate o commit.</summary>
	public static bool CeuCondenado(EstadoDaMorte e) =>
		e.Fase == FaseDaMorte.Explodindo
		|| (e.Fase == FaseDaMorte.Morrendo && e.Estagio >= EstagioDaNoiteEterna);
}

/// <summary>
/// O LIVRO DOS PLANETAS MORTOS -- a `PlanetDisableList` do original, com chave honesta.
///
/// ============================ AS DUAS PONTAS TEM UM ============================
/// O servidor tem o dele (autoridade, persistido em `planetas-mortos.json`); o cliente tem uma copia
/// que chega por pacote (`S2C.Mortos`). O cliente precisa porque **ele enumera planetas sozinho**:
/// a carta estelar chama `Espaco.PreFeitos()` e `Sistemas.Do` direto (`Client/MapaEstelar.cs`), e
/// sem esta copia ela desenharia um planeta destruido com um botao "Viajar" em cima. Um bit no
/// `S2C.Vizinhanca` nao cobriria a carta -- ela desenha o que esta a anos-luz de distancia.
///
/// A classe e a MESMA nas duas pontas justamente pra nao haver duas nocoes de "morto" (regra 0.4).
/// ==========================================================================
/// </summary>
public sealed class RegistroDeMortos
{
	private readonly Dictionary<string, EstadoDaMorte> _porChave = [];

	public int Quantos => _porChave.Count;
	public IEnumerable<EstadoDaMorte> Todos => _porChave.Values;

	public EstadoDaMorte? De(ChaveDePlaneta c) =>
		_porChave.TryGetValue(c.Texto, out EstadoDaMorte? e) ? e : null;

	public EstadoDaMorte? De(ZoneKey z) =>
		ChaveDePlaneta.Da(z) is { } c ? De(c) : null;

	/// <summary>Este planeta esta DESTRUIDO? A pergunta do `if(A.planetType in PlanetDisableList)`.</summary>
	public bool Morto(ChaveDePlaneta c) => De(c) is { } e && MortePlanetaria.EstaMorto(e.Fase);

	public bool Morto(ZoneKey z) => De(z) is { } e && MortePlanetaria.EstaMorto(e.Fase);

	public bool Morto(PlanetaNoEspaco p) => Morto(ChaveDePlaneta.De(p));

	/// <summary>Ha alguma morte em curso ou consumada aqui? (o `planet_dying` do DM).</summary>
	public bool Condenado(ZoneKey z) => De(z) != null;

	public void Por(EstadoDaMorte e) => _porChave[e.Chave] = e;

	public bool Tirar(ChaveDePlaneta c) => _porChave.Remove(c.Texto);

	public void Limpar() => _porChave.Clear();

	/// <summary>Troca o conteudo inteiro de uma vez. E o que o cliente faz ao receber o pacote.</summary>
	public void Substituir(IEnumerable<EstadoDaMorte> novos)
	{
		_porChave.Clear();
		foreach (EstadoDaMorte e in novos) _porChave[e.Chave] = e;
	}
}
