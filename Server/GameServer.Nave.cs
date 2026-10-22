using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// UMA NAVE NO MUNDO -- a Spacepod. E o que o `naves.json` guarda.
///
/// ============================ POR QUE ELA NAO E UMA `Obra` ============================
/// A tentacao era grande: a nave se COMPRA na aba Tech, se GUARDA na mochila e se ASSENTA no chao,
/// exatamente como uma bancada de pesquisa -- e a `Obra` ja faz as tres coisas. O que separava as
/// duas era a CHAVE DE ZONA: a `Obra` guardava a zona como **nome puro** e a remontava com
/// `ZoneKey.Premade(...)`, e isso matava uma nave de tres jeitos:
///
///   * uma nave assentada num planeta GERADO voltaria do disco numa zona pre-feita que nao existe;
///   * dois planetas gerados de mesmo nome (o `% 1000` do `SistemaSolar.Planeta` colide) dividiriam
///     a mesma nave;
///   * uma nave largada NO ESPACO nem teria como ser descrita -- o espaco e `KindProcedural` com a
///     seed do universo, e um campo de nome joga fora tanto o tipo quanto a seed.
///
/// **ESSE MOTIVO MORREU**: a `Obra` passou a guardar os mesmos tres campos (ver `Obra.ZonaTipo`, em
/// `GameServer.Tech.cs`), como aqui e como o save de personagem (`CharacterStore.cs:170-180`). As
/// tres estruturas usam hoje a MESMA chave de zona.
///
/// O que mantem a `Nave` separada e o que ela tem A MAIS, e nao o que a `Obra` tinha a menos:
/// piloto, lancamento, casco, senha, uso unico, viagem entre mundos e um interior proprio -- nada
/// disso cabe numa bancada, e junta-las poria oito campos mortos em cada banco de praca do mapa.
/// ====================================================================================
/// </summary>
public sealed class Nave
{
	public int Id;

	/// <summary>O id do catalogo ("Spacepod" ou "Personal_Spacepod"). Ver <see cref="Naves.EhNave"/>.</summary>
	public string Tipo = "";

	/// <summary>
	/// ONDE ELA ESTA -- as TRES partes da <see cref="ZoneKey"/>, e nao o nome.
	///
	/// O texto inteiro do porque esta no cabecalho da classe. Aqui basta a regra: nome nao e
	/// endereco, e uma nave e a unica construcao deste jogo que se move entre mundos.
	/// </summary>
	public byte ZonaTipo;
	public string ZonaNome = "";
	public ulong ZonaSeed;

	public float X, Y;

	public string DonoConta = "";
	public string DonoNome = "";

	/// <summary>O `var/Speed=1` do `obj/Spacepod` (`PlanetTech.dm:51`). Ver <see cref="Naves"/>.</summary>
	public int Velocidade = Naves.VelocidadeInicial;

	public long ErguidaEm;

	// =====================================================================
	// A CAMADA 2 -- o que so a Capital Ship e o foguete tem
	// =====================================================================
	/// <summary>
	/// O CASCO -- `maxarmor = max(intBPcap*5, 1000)` / `armor = maxarmor` (`ShipVessel.dm:63-64`).
	///
	/// So a Capital Ship o usa (ver <see cref="NaveGrande.ArmaduraMaxima"/>). Nas outras duas ele
	/// fica em zero e a nave nao entra no funil de estrago -- e isso e DIVERGENCIA, e nao copia: o
	/// `obj/Spacepod` do DM declara `fragile = 1` (`PlanetTech.dm:47`; o `obj/Rocketship` tambem, :244)
	/// com a armadura padrao 1 (`barrier.dm:8-9`), entao la o soco em cenario (`attack_proc.dm:6-7`) e o
	/// corpo arremessado (`Movement Effects.dm:74-76`) o derrubam, e o upgrade "Armor (1000z)"
	/// (`PlanetTech.dm:208-215`) sobe a armadura pro `intBPcap` de quem paga. O que o pod NAO tem e verb de
	/// destruir. Aqui ele nao se quebra -- se recolhe.
	///
	/// VAI PRO DISCO, ao contrario do piloto e do lancamento: uma nave que voltasse do reboot com o
	/// casco cheio faria de reiniciar o servidor a melhor forma de reparo do jogo.
	/// </summary>
	public double ArmaduraMax;
	public double Armadura;

	/// <summary>
	/// A SENHA DO DONO -- `var/ship_pass` (`ShipVessel.dm:84`), perguntada no `Click()` da
	/// construcao (:65-66). Vazia = qualquer um embarca. O DONO NUNCA PRECISA DELA (:107).
	///
	/// GUARDADA EM CLARO, e de proposito: ela nao e credencial de conta, e uma tranca de porta
	/// dentro do jogo. Trata-la como segredo real (sal, hash) daria a ela uma seriedade que ela nao
	/// tem e faria alguem um dia reusa-la como se fosse senha de verdade.
	/// </summary>
	public string Senha = "";

	/// <summary>
	/// O FOGUETE JA VOOU -- `var/didland = 0` e o `didland=1` do fim do `verb/Launch`
	/// (`PlanetTech.dm:251, 288`). Um foguete usado nao lanca de novo ate ser recondicionado.
	///
	/// PERSISTE porque e a coisa toda: um uso unico que se apaga no reboot nao e uso unico.
	/// </summary>
	public bool Usada;

	/// <summary>
	/// (2026-10-18) O RUMO DA NAVE GRANDE SEM NINGUEM AO LEME: o ponto da GALAXIA que o nav system do ultimo piloto apontava quando ele largou
	/// o leme -- o centro do planeta escolhido, ou o porto de um sistema. O dono: *"permita q na capital ship ao colocar pra ela ir pra um
	/// planeta com nav system e o jogador sair do piloto, ela ainda continua, e avisa no chat quando chegar"*. PERSISTE: a viagem atravessa o
	/// reinicio, como a tripulacao dentro dela. Ver `GameServer.NaveRumo.cs`.
	/// </summary>
	public bool TemRumo;
	public float RumoX, RumoY;

	/// <summary>A CONTA de quem largou o leme com o rumo: ouve a chegada mesmo fora da nave, como o dono.</summary>
	public string RumoPor = "";

	/// <summary>
	/// (2026-10-18) O TANQUE, em unidades de combustivel (`Combustivel`, no Core). NEGATIVO = CHEIO DE FABRICA: a nave recem-assentada e a
	/// que voltou do disco de antes do combustivel existir (o campo nem estava no `naves.json`) nascem cheias, e o primeiro passo grava o
	/// numero de verdade. Quem le e o `NivelDoTanque`, nunca o campo cru. PERSISTE: um tanque que enchesse no reinicio faria de reiniciar
	/// o servidor o posto de gasolina do jogo -- o mesmo argumento do casco.
	/// </summary>
	public double Tanque = -1;

	/// <summary>O que a nave gastou desde a ultima volta de um segundo (`TickDoCombustivel`). Sessao, nao disco.</summary>
	[JsonIgnore] public double GastoNoSegundo;

	/// <summary>O gasto da ULTIMA volta de um segundo: e o "por segundo" que a barra mostra (como a mineracao mostra o que rendeu).</summary>
	[JsonIgnore] public double GastoPorSegundo;

	/// <summary>
	/// QUANDO O FOGUETE VOLTA (ms de relogio real). Zero = nao esta la em cima.
	///
	/// E o `sleep(150)` + `sleep(100)` do `verb/Launch` (:276-279) virado prazo, pelo mesmo motivo
	/// do <see cref="LancaEm"/>: um `sleep` de 25 segundos dentro do tique para o servidor inteiro.
	///
	/// **NAO VAI PRO DISCO**: um foguete que voltasse do reboot "em orbita" desceria sozinho num
	/// tique qualquer, carregando um piloto que nao esta mais nele.
	/// </summary>
	[JsonIgnore] public long VoltaEm;

	/// <summary>Ja avisei da reentrada nesta subida? Ver <see cref="Naves.SegundosAteAvisarAReentrada"/>.</summary>
	[JsonIgnore] public bool AvisouDaReentrada;

	/// <summary>
	/// DE ONDE O FOGUETE SUBIU: e pra la que ele desce. `pickTurf(nA,2)` (`PlanetTech.dm:281`) --
	/// o DM guarda a AREA de onde se lancou e devolve o corpo pra ela.
	///
	/// A `ZoneKey` inteira pelo mesmo motivo de sempre neste arquivo: descer num planeta gerado
	/// homonimo ao de origem seria descer no planeta errado.
	/// </summary>
	[JsonIgnore] public byte VoltaTipo;
	[JsonIgnore] public string VoltaNome = "";
	[JsonIgnore] public ulong VoltaSeed;
	[JsonIgnore] public float VoltaX, VoltaY;

	public ZoneKey ZonaDaVolta => new(VoltaTipo, VoltaNome, VoltaSeed);

	public void PorZonaDaVolta(ZoneKey z, Vec2 p)
	{
		VoltaTipo = z.Kind; VoltaNome = z.Name; VoltaSeed = z.Seed;
		VoltaX = p.X; VoltaY = p.Y;
	}

	/// <summary>
	/// QUEM ESTA PILOTANDO AGORA (id de rede). Zero = parada no chao.
	///
	/// **NAO VAI PRO DISCO**: pilotar e sessao, e uma nave que voltasse do reboot "com piloto"
	/// teria um dono fantasma que ninguem consegue desembarcar. E o mesmo raciocinio que mantem
	/// guarda erguida e atordoamento fora do `CharacterSave`.
	/// </summary>
	[JsonIgnore] public int PilotoId;

	/// <summary>
	/// QUANDO O LANCAMENTO COMPLETA (ms de relogio real). Zero = nao esta lancando.
	///
	/// E o `sleep(400/Speed)` do `verb/Launch` (`PlanetTech.dm:68`) virado prazo em vez de sono: o
	/// DM podia dormir dentro do verb porque cada `proc` la e uma linha de execucao propria; aqui um
	/// `sleep` dentro do tique pararia o servidor inteiro por ate 40 segundos (regra 0.4).
	///
	/// Tambem nao persiste, pelo mesmo motivo do <see cref="PilotoId"/>.
	/// </summary>
	[JsonIgnore] public long LancaEm;

	public ZoneKey Zona => new(ZonaTipo, ZonaNome, ZonaSeed);

	public void PorZona(ZoneKey z) { ZonaTipo = z.Kind; ZonaNome = z.Name; ZonaSeed = z.Seed; }
}

/// <summary>
/// **A NAVE (CAMADA 1: A SPACEPOD)** -- porte de `obj/Spacepod` (`Code/Modules/Tech/PlanetTech.dm:41`).
///
/// ============================ O FLUXO INTEIRO, E ELE E TODO POR CAMINHO QUE JA EXISTIA ============================
///   1. FABRICAR  -- a aba Tech (`Construir`): 100.000z e tech 40, direto do `construcoes.json`.
///   2. ASSENTAR  -- o verbo `posicionar`, o mesmo fantasma no mouse de qualquer construcao. Ele
///                   desvia pra ca quando o tipo e nave (ver `Posicionar` em `GameServer.Tech.cs`).
///   3. EMBARCAR  -- `nave_usar`. O pod deixa de bloquear (`density=0`, :137), o piloto ganha voo
///                   (`pilot.flight = 1`, :135) e a nave passa a copiar a posicao dele.
///   4. LANCAR    -- `nave_lancar`. Espera `400/Speed` decimos e cai no `Decolar` de producao --
///                   o MESMO que a skill Space Flight usa, com os dois ramos (pre-feito e gerado).
///   5. VIAJAR    -- a velocidade da NAVE substitui a do corpo no ponto unico onde `SpeedStat` e
///                   escrito (`GameServer.TickFichas`). Nenhum pacote novo, nenhuma formula nova.
///   6. POUSAR    -- nada. O `TickDoEspaco` ja pousa quem encosta num planeta, e a nave vai junto
///                   porque ela copia a zona do piloto. Nao ha um segundo caminho de pouso.
/// ==============================================================================================================
///
/// ============================ O COMBUSTIVEL (dono, 2026-10-18) ============================
/// Nenhuma das tres naves do original consome nada (`grep fuel` no DM: zero), e este bloco dizia que
/// inventar um recurso novo seria design sem pedido. O pedido veio: o pod e a Capital Ship gastam
/// combustivel pela distancia andada e se abastecem com recursos -- ver `Core/Tech/Combustivel.cs` e
/// `GameServer.Combustivel.cs`. O foguete continua sem tanque (ele nao viaja: e uma janela de 25 s).
/// ===================================================================================
/// </summary>
public partial class GameServer
{
	private readonly List<Nave> _naves = [];
	private int _proximaNaveId = 1;

	private string CaminhoDasNaves => System.IO.Path.Combine(_store?.Pasta ?? ".", "naves.json");

	// =====================================================================
	// DISCO
	// =====================================================================
	private void CarregarNaves()
	{
		try
		{
			if (!System.IO.File.Exists(CaminhoDasNaves)) return;
			List<Nave>? l = JsonSerializer.Deserialize<List<Nave>>(
				System.IO.File.ReadAllText(CaminhoDasNaves), new JsonSerializerOptions { IncludeFields = true });
			if (l == null) return;
			_naves.AddRange(l);
			_proximaNaveId = _naves.Count > 0 ? _naves.Max(n => n.Id) + 1 : 1;
			GD.Print($"[server] naves: {_naves.Count} de pe");
		}
		catch (Exception e) { GD.PushWarning($"[server] naves.json ilegivel: {e.Message}"); }
	}

	private void GravarNaves()
	{
		try
		{
			System.IO.File.WriteAllText(CaminhoDasNaves,
				JsonSerializer.Serialize(_naves, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
		}
		catch (Exception e) { GD.PushWarning($"[server] nao gravei as naves: {e.Message}"); }
	}

	// =====================================================================
	// AS PERGUNTAS QUE O RESTO DO SERVIDOR FAZ
	// =====================================================================
	/// <summary>
	/// AS NAVES PARADAS NESTA ZONA -- o crivo e a `ZoneKey` INTEIRA, e nao o nome.
	///
	/// A que esta sendo pilotada fica de fora de proposito: ela nao esta no chao, esta em cima de
	/// alguem. Quem a desenha e o corpo do piloto (ver o bit `Pilotando` no snapshot).
	/// </summary>
	private List<Nave> NavesParadasEm(ZoneKey z) =>
		[.. _naves.Where(n => n.PilotoId == 0 && n.Zona.Equals(z))];

	// ============================ TRES LACOS, E NAO TRES `_naves.Any(n => ...)` ============================
	// As duas perguntas de baixo sao feitas pelo SNAPSHOT (`EstadoDe`), por corpo, por tique -- e a de cima pelo
	// tique de fichas. Escritas com LINQ, cada consulta montava um fecho (o lambda captura o `id`) e um
	// delegate: medido em 2026-10-08 com 149 corpos, 10 mil por segundo, 0,87 MB/s -- quase metade de tudo o
	// que o servidor aloca por tique (`--diagcoletor`). A resposta e a mesma; so nao sobra nada pro coletor.
	// ========================================================================================================

	/// <summary>A nave que este corpo esta pilotando, ou nula.</summary>
	private Nave? NaveDoPiloto(ServerPlayer pl)
	{
		foreach (Nave n in _naves)
			if (n.PilotoId == pl.Id) return n;
		return null;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagcoletor --coletordefeito piloto`): as duas perguntas do snapshot voltam a
	/// ser feitas por LINQ, com um fecho e um delegate por corpo, por tique. A regua da bancada tem que reprovar
	/// o servidor.
	/// </summary>
	internal static bool PilotoPorFechoDeTeste;

	// (Em metodos proprios: um lambda que captura o parametro faz o compilador montar o fecho na ENTRADA do
	// metodo, com o defeito ligado ou nao.)
	private bool PilotandoPorFecho(int id) => _naves.Any(n => n.PilotoId == id);
	private bool PilotandoGrandePorFecho(int id) => _naves.Any(n => n.PilotoId == id && NaveGrande.EhNaveGrande(n.Tipo));

	/// <summary>Ele esta pilotando alguma coisa? E o bit que vai no snapshot.</summary>
	public bool EstaPilotando(int id)
	{
		if (PilotoPorFechoDeTeste) return PilotandoPorFecho(id);
		foreach (Nave n in _naves)
			if (n.PilotoId == id) return true;
		return false;
	}

	/// <summary>
	/// ...E E A GRANDE? O segundo bit do snapshot, e ele existe pra o cliente escolher o SPRITE --
	/// ver `EntityState.NaveGrande`. Sem ele, quem pilota uma Capital Ship apareceria dentro de uma
	/// capsula de um tile.
	/// </summary>
	public bool PilotaNaveGrande(int id)
	{
		if (PilotoPorFechoDeTeste) return PilotandoGrandePorFecho(id);
		foreach (Nave n in _naves)
			if (n.PilotoId == id && NaveGrande.EhNaveGrande(n.Tipo)) return true;
		return false;
	}

	/// <summary>
	/// A NAVE PARADA AO ALCANCE DA MAO -- mesmo `AlcanceDeUso` de qualquer construcao, e mesma
	/// ordenacao por distancia que o `ObraPerto` usa.
	/// </summary>
	// =====================================================================
	// A NAVE VOA COMO SE VOA (v0.2.1, item 26)
	// =====================================================================
	// Dono (2026-10-13): *"space pods e naves sao igual voar, ao pilotar e apertar page up ou page down vc sobe ou
	// desce (ate pousar) com elas, se subir muito sai do planeta e comeca a voar pelo espaco"*.
	//
	// QUASE TUDO JA EXISTIA, e por isso o lote e pequeno: embarcar ja liga o voo do piloto (`pilot.flight = 1`,
	// `PlanetTech.dm:135`) e o voo ja tem altura -- as teclas de subir e descer ja levavam a nave pra cima e pra
	// baixo. O que faltava eram as tres PONTAS:
	//   * O CHAO: descendo ate o fim o piloto perdia o voo e ficava a pe DENTRO da nave, andando com ela no chao.
	//     Agora a nave POUSA: fica onde desceu, e nao anda (`NavePousada`, no `PodeMexerOCorpo`).
	//   * A VOLTA: pra sair do chao so havia a tecla de voar, que pergunta pelo treino de voo do PILOTO -- e quem
	//     compra uma nave quase nunca tem. A tecla de subir decola a nave (`DecolarANavePousada`).
	//   * O TETO: la em cima o corpo esbarrava nos portoes do `Space_Flight` (Ki, poder e treino de voo de quem voa
	//     sozinho). Ao leme quem rompe a atmosfera e a nave, pelo funil de lancamento de sempre
	//     (`RomperAAtmosferaComANave`).
	// O `nave_lancar` do menu continua: e a mesma subida, sem precisar segurar a tecla.
	//
	// O PRECO DO VOO MUDOU DE DONO (2026-10-18): pilotar ainda conta como voar (a altura, a agua, o cenario), mas quem paga
	// e a NAVE, em combustivel -- o dono escolheu *"so combustivel"*: o Ki do piloto nao desce por estar ao leme (antes era
	// o Ki do voo, `Stats.dm:411-427` com o `flight = 1` do pod, e quem nao treinou voo cansava em um segundo). Sem
	// combustivel a nave desce e pousa, e so levanta de novo abastecida (`GameServer.Combustivel.cs`).
	//
	// QUEM CHEGA DO ESPACO CHEGA POUSADO: a troca de zona zera o voo de todo corpo (`MoveToZone`), entao a nave que
	// desce num planeta ja esta no chao, parada -- e o piloto ouve o mesmo aviso do pouso. NO ESPACO NAO HA POUSO: la
	// fora o voo tambem esta zerado, e a nave anda como sempre andou (ver `NavePousada`).
	// =====================================================================

	/// <summary>DEFEITO INJETADO (bancada `--naveteste`): a nave pousada anda no chao com o piloto dentro, como na v0.2.0.</summary>
	internal static bool NavePousadaAndaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--naveteste`): a tecla de subir nao tira a nave do chao (so a de voar, que pede o treino do piloto).</summary>
	internal static bool NavePousadaNaoDecolaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--naveteste`): no teto a nave pergunta pelos portoes de quem voa sozinho -- so sai do planeta pelo menu.</summary>
	internal static bool NaveSoSaiPeloMenuDeTeste;

	/// <summary>
	/// A NAVE ESTA POUSADA? O piloto ao leme, sem voo, num planeta. NO ESPACO NAO HA POUSO: la a nave anda mesmo com
	/// o piloto exausto, como sempre andou -- nao existe chao onde ficar parado, e prender alguem no vazio por falta
	/// de Ki seria uma armadilha.
	/// </summary>
	private bool NavePousada(ServerPlayer pl) =>
		!pl.Voando && !NavePousadaAndaDeTeste && EstaPilotando(pl.Id) && !Espaco.EhEspaco(pl.Zone);

	/// <summary>
	/// A TECLA DE SUBIR, COM A NAVE NO CHAO: ela decola. Sem a porta do treino de voo (o voo e da nave, como ao
	/// embarcar) e sem a taxa de decolagem do corpo (`Voo.CustoParaLigar` e do gesto de quem voa sozinho); o que ela
	/// pede e o que a nave pede pra andar -- combustivel no tanque (2026-10-18; antes era o Ki do piloto, que nao paga
	/// mais o voo ao leme -- ver `GameServer.Combustivel.cs`).
	/// </summary>
	private void DecolarANavePousada(ServerPlayer pl)
	{
		if (NavePousadaNaoDecolaDeTeste || NaveDoPiloto(pl) is not { } n) return;
		if (NaoPilota(pl) || AlturaPresaPeloKi(pl) || PresoNoTorneio(pl.Id)) return;

		if (TanqueVazio(n))
		{
			long agora = NowMs();
			if (agora < pl.AvisoDeTetoAte) return;   // (a tecla fica apertada: um aviso a cada quatro segundos)
			pl.AvisoDeTetoAte = agora + 4000;
			Avisar(pl, FraseDoTanqueVazio(n));
			return;
		}

		pl.Voando = true;
		MandarEfeito(pl, "voo", -1);
		MandarFicha(pl);
		Avisar(pl, $"{NomeDaNave(n)} decola.");
	}

	/// <summary>Ao leme, quem pousou foi a nave: o piloto continua dentro, e o aviso diz como sair do chao.</summary>
	private void AvisarDoPousoDaNave(ServerPlayer pl)
	{
		if (NaveDoPiloto(pl) is not { } n || Espaco.EhEspaco(pl.Zone)) return;
		Avisar(pl, $"{NomeDaNave(n)} pousa. Suba pra decolar de novo -- ou desembarque.");
	}

	/// <summary>
	/// NO TETO DO CEU, AO LEME: a nave sai do planeta. Pelo MESMO funil do `nave_lancar` -- o relogio de lancamento,
	/// que aqui ja nasce vencido: o `TickDasNaves` completa a subida no proximo tique (o `Decolar` de producao, a
	/// orbita do foguete, a zona da nave), e nao ha um segundo caminho de decolagem pra manter em dia.
	///
	/// A ESPERA DO MENU (`400/Speed` decimos, `PlanetTech.dm:68`) NAO SE REPETE AQUI: quem chegou ao teto ja subiu o
	/// ceu inteiro segurando a tecla -- a subida FOI a espera.
	/// </summary>
	private void RomperAAtmosferaComANave(ServerPlayer pl, Nave n)
	{
		if (n.LancaEm > 0) return;   // ja esta saindo: o tique das naves completa

		long agora = NowMs();
		// UM FOGUETE GASTO NAO SOBE -- a recusa do `LancarNave` (`PlanetTech.dm:255`), com o aviso espacado: a tecla
		// fica apertada, e o teto e perguntado a cada tique.
		if (Naves.EhFoguete(n.Tipo) && n.Usada)
		{
			if (agora < pl.AvisoDeTetoAte) return;
			pl.AvisoDeTetoAte = agora + 4000;
			Avisar(pl, $"{NomeDaNave(n)} já foi usado: o casco não aguenta outra subida. Recondicionar custa {Naves.CustoDeRecondicionar:N0} zeni.");
			return;
		}
		// (o lugar que nao fica no mapa do universo recusa la no `Decolar`, em voz alta -- uma tentativa a cada quatro segundos)
		if (agora < pl.AvisoDeTetoAte) return;
		pl.AvisoDeTetoAte = agora + 4000;

		n.PorZonaDaVolta(pl.Zone, pl.Pos);
		n.AvisouDaReentrada = false;
		n.LancaEm = agora;
		Avisar(pl, $"{NomeDaNave(n)} rompe a atmosfera.");
	}

	private Nave? NavePerto(ServerPlayer pl) => NavesParadasEm(pl.Zone)
		.OrderBy(n => (n.X - pl.Pos.X) * (n.X - pl.Pos.X) + (n.Y - pl.Pos.Y) * (n.Y - pl.Pos.Y))
		.FirstOrDefault(n => Math.Abs(n.X - pl.Pos.X) <= AlcanceDeUso && Math.Abs(n.Y - pl.Pos.Y) <= AlcanceDeUso);

	/// <summary>A minha, se eu estiver pilotando; senao a que estiver ao alcance da mao.</summary>
	private Nave? MinhaNaveOuPerto(ServerPlayer pl) => NaveDoPiloto(pl) ?? NavePerto(pl);

	private string NomeDaNave(Nave n) => _obras?.Get(n.Tipo)?.Nome ?? n.Tipo;

	/// <summary>
	/// DIZ AO CLIENTE QUAL NAVE ESTA EMBAIXO DELE -- ver <see cref="Protocol.S2C.Veiculo"/>.
	///
	/// ============================ E ISTO E O QUE FAZ A TECLA E ALCANCAR O VEICULO ============================
	/// A tecla E procura alvo na lista de construcoes da zona, e a nave pilotada nao esta nela de
	/// proposito (ela deixou de estar no chao). Sem esta linha o piloto nao teria como abrir o menu do
	/// proprio veiculo -- nem pra descer dele --, que e o buraco que a aba Nav tapava com uma segunda
	/// fileira de botoes. Ver `Interacoes.DoVeiculo`.
	/// ======================================================================================================
	///
	/// MANDA SEMPRE, inclusive o vazio: e o vazio que APAGA o alvo virtual quando se desembarca. Um
	/// pacote que so falasse pra dizer "sim" deixaria o menu do pod aberto pra quem ja desceu dele.
	///
	/// O CUSTO E UMA STRING POR EVENTO. Chamada de embarque, desembarque, leme e destruicao -- nao ha
	/// caminho de tique aqui, e nao deve haver.
	/// </summary>
	private void MandarVeiculo(ServerPlayer pl)
	{
		if (pl.Peer == null) return;
		Nave? n = NaveDoPiloto(pl);
		var w = Protocol.Begin(Protocol.S2C.Veiculo);
		w.Put(n?.Tipo ?? "");
		w.Put(n != null ? NomeDaNave(n) : "");
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// A VELOCIDADE
	// =====================================================================
	/// <summary>
	/// ESCREVE `SpeedStat` -- **o unico lugar que o faz por tique**.
	///
	/// ============================ POR QUE A NAVE ENTRA AQUI, E NAO NUM CAMINHO PROPRIO ============================
	/// Daqui o numero segue por dois trilhos que ja existem e que precisam concordar: o `SheetState`
	/// (que o cliente obedece em `LocalPlayer.OnSheet`) e o `MoveRules.ValidateStep` (que o servidor
	/// usa pra conferir cada passo). Uma "velocidade de veiculo" separada obrigaria os quatro
	/// chamadores do `MoveRules.SpeedPx` a lembrar de passa-la, e esquecer isso nao aparece em teste
	/// -- e o mesmo argumento que o `CalorDaEstrela.Fator` ja escreveu sobre passar o raio.
	///
	/// Zero pacote novo, zero formula nova: o campo ja viajava e a conta mora no Core
	/// (<see cref="Naves.FatorDoPiloto"/>).
	/// ==========================================================================================================
	///
	/// CHAMADO TAMBEM FORA DO TIQUE (embarcar, desembarcar, melhorar) porque `SpeedStat` **nao esta
	/// na lista de campos que o `TickFichas` compara** antes de reenviar a ficha: embarcar mudaria a
	/// velocidade sem mandar nada, e o cliente andaria devagar dentro de uma nave rapida ate algum
	/// outro numero da ficha mexer por acaso. E a mesma familia do bug da barra de Ki, e a defesa e
	/// mandar a ficha DEPOIS de recalcular, na hora do evento.
	/// </summary>
	/// <remarks>
	/// ============================ O PESO E O ESMAGAMENTO ENTRAM AQUI ============================
	/// E a mesma razao da nave, invertida: sao mais dois donos da velocidade do corpo (o peso vestido
	/// e a gravidade acima da maestria), e eles precisam chegar aos MESMOS dois trilhos -- o
	/// `SheetState`, que o cliente obedece, e o `ValidateStep`, que o servidor confere. Aplicar a
	/// penalidade so num dos dois seria o cliente andando rapido e o servidor puxando de volta trinta
	/// vezes por segundo: o corpo tremendo.
	///
	/// A CONTA MORA NO CORE (<see cref="Jandirus.Core.Stats.Esmagamento.FatorDePasso"/>), como a da
	/// nave mora em `Naves.FatorDoPiloto` -- aqui so se multiplica.
	///
	/// ANTES DA NAVE, e a ordem tem razao: o fator e do CORPO. Quem esta esmagado se arrasta ate a
	/// nave; uma vez la dentro, `FatorDoPiloto` substitui a velocidade do corpo pela do veiculo e a
	/// penalidade some junto -- que e o certo, porque a nave nao anda com as pernas do piloto.
	/// ========================================================================================
	/// </remarks>
	private void RecalcularVelocidade(ServerPlayer pl)
	{
		// O CORPO FERIDO ANDA DEVAGAR (`mobTime *= HP/100`, `movement handler.dm:55`) -- o terceiro dono da
		// velocidade do corpo, e ele entra por DENTRO do `FatorDePasso` porque no DM a linha vem antes do peso.
		// Ver `FatorDeVidaNoPasso`, `GameServer.Membros.cs`.
		pl.SpeedStat = MoveRules.SpeedStatFrom(pl.Ficha.Espeed)
					 * (float)Jandirus.Core.Stats.Esmagamento.FatorDePasso(pl.Ficha, FatorDeVidaNoPasso(pl));

		// NADAR E MAIS DEVAGAR QUE ANDAR -- `mobTime -= 0.3` (`movement handler.dm:66`). Entra AQUI
		// e nao no tique do nado pelo motivo que este metodo inteiro existe: velocidade tem um dono
		// so neste port, e daqui ela segue pelos dois trilhos que precisam concordar (a ficha, que o
		// cliente obedece, e o `ValidateStep`, que o servidor confere). Aplicada so num dos dois,
		// seria o cliente nadando rapido e o servidor puxando de volta trinta vezes por segundo.
		//
		// DEPOIS DO ESMAGAMENTO E ANTES DA NAVE, na mesma ordem e pela mesma razao do peso: sao
		// penalidades do CORPO, e dentro de uma nave a velocidade passa a ser a do veiculo.
		// (v0.2.1) MENOS QUEM VELEJA: o barco anda no passo do corpo (`move_delay = 0.1`, `Boat.dm:9`).
		if (pl.Nadando && !VelaPoupaONado(pl))
			pl.SpeedStat *= (float)Jandirus.Core.World.Nado.FatorDePasso(pl.Ficha.Epspeed);

		// SOB UM RAIO, QUEM E MAIS FORTE QUE ELE ANDA DEVAGAR (dono, 2026-10-12: *"ainda pode tentar sair do ataque mas
		// ele anda bem mais devagar enquanto toma o dano"*). Penalidade do CORPO, antes da nave -- como o nado.
		if (NowMs() < pl.SobORaioAteMs) pl.SpeedStat *= (float)Jandirus.Core.Combat.KiContraCorpo.AndarSobORaio;

		if (NaveDoPiloto(pl) is { } n) pl.SpeedStat = Naves.FatorDoPiloto(pl.SpeedStat, n.Velocidade);
	}

	// =====================================================================
	// ASSENTAR
	// =====================================================================
	/// <summary>
	/// ASSENTA UMA NAVE no chao. Chamado pelo `Posicionar`, DEPOIS das guardas dele (alcance,
	/// nao-empilhar, nao dentro de parede) -- elas valem pra nave exatamente como pra bancada.
	///
	/// A DIFERENCA E SO A CHAVE: aqui a zona vai inteira (tipo, nome, seed).
	///
	/// <paramref name="idDoItem"/> e o item que saiu da mochila: a nave recolhida com velocidade comprada (`Spacepod|c12|20x`)
	/// volta com ela (2026-10-19, `DevolverAVelocidade`).
	/// </summary>
	private void AssentarNave(ServerPlayer pl, Construcao c, float x, float y, string idDoItem)
	{
		var nave = new Nave
		{
			Id = _proximaNaveId++,
			Tipo = c.Id,
			X = x,
			Y = y,
			DonoConta = pl.Conta,
			DonoNome = pl.Name,
			ErguidaEm = NowMs(),
		};
		nave.PorZona(pl.Zone);

		// O CASCO NASCE DO BP DE QUEM ERGUEU -- `S.maxarmor = max(usr.intBPcap * 5, 1000)`
		// (`ShipVessel.dm:63`), a mesma traducao de `intBPcap` que o `Posicionar` da `Obra` ja usa.
		// So a nave grande tem casco: ver o campo.
		if (NaveGrande.EhNaveGrande(c.Id))
		{
			nave.ArmaduraMax = NaveGrande.ArmaduraMaxima(pl.Ficha.expressedBP);
			nave.Armadura = nave.ArmaduraMax;
		}
		bool voltou = DevolverAVelocidade(nave, idDoItem);

		_naves.Add(nave);
		GravarNaves();

		GD.Print($"[server] {pl.Name} assentou {c.Nome} (nave #{nave.Id}) em {pl.Zone} @ ({x:0},{y:0})"
				 + (nave.ArmaduraMax > 0 ? $" -- casco {nave.ArmaduraMax:N0}" : "") + (voltou ? $" -- velocidade {nave.Velocidade}" : ""));
		Avisar(pl, (NaveGrande.EhNaveGrande(c.Id)
			? $"você assenta {c.Nome} no chão. Aperte E nela pra EMBARCAR -- a ponte fica lá dentro."
			: $"você assenta {c.Nome} no chão. Aperte E nela pra entrar.")
			+ (voltou ? $" A velocidade que ela tinha volta com ela: {nave.Velocidade}x." : ""));
		MandarObras(pl.Zone);
	}

	/// <summary>
	/// RECOLHE A NAVE pra mochila. Espelho do `PegarObra`, com as mesmas duas recusas.
	/// </summary>
	private void PegarNave(ServerPlayer pl)
	{
		Nave? n = NavePerto(pl);
		if (n == null) { Avisar(pl, "não há nenhuma nave parada por perto."); return; }
		if (n.DonoConta.Length > 0 && !string.Equals(n.DonoConta, pl.Conta, StringComparison.OrdinalIgnoreCase))
		{ Avisar(pl, $"{NomeDaNave(n)} é de {n.DonoNome}."); return; }
		if (pl.Mochila.Cheio) { Avisar(pl, "sua mochila está cheia."); return; }

		// ============================ NAO SE DOBRA UMA CASA COM GENTE DENTRO ============================
		// Recolher uma Capital Ship habitada apagaria a UNICA zona em que aquelas pessoas estao: elas
		// ficariam num interior sem chao, sem colisao e sem saida, e o unico jeito de sair seria
		// morrer. O DM nao tem este caso porque nao ha como recolher a nave la -- e e justamente
		// quando um caminho novo cruza um sistema velho que se ganha um modo de falha novo.
		//
		// RECUSAR E MELHOR QUE EJETAR: quem esta la dentro pode estar treinando, e ser cuspido no
		// chao sem aviso porque o dono clicou "pegar" e o mesmo estrago com outro nome. A destruicao
		// ejeta porque ali nao ha escolha; aqui ha.
		if (QuantosDentro(n) is > 0 and var quantos)
		{
			Avisar(pl, $"{NomeDaNave(n)} não pode ser recolhida: {quantos} pessoa(s) lá dentro.");
			return;
		}

		// (2026-10-18) SO DE TANQUE CHEIO: o item da mochila nao guarda o tanque (so a velocidade, logo abaixo), e a nave assentada de novo
		// nasce cheia -- recolher e assentar seria abastecer de graca, em qualquer lugar, e o combustivel que o dono pediu pra obrigar o
		// pouso estrategico deixaria de obrigar qualquer coisa. Ver `GameServer.Combustivel.cs`.
		if (!RecolheComTanqueFuradoDeTeste && Combustivel.TemTanque(n.Tipo) && !Combustivel.Cheio(n.Tipo, NivelDoTanque(n)))
		{
			Avisar(pl, $"{NomeDaNave(n)} só vai pra mochila de tanque cheio (faltam {Combustivel.Capacidade(n.Tipo) - NivelDoTanque(n):N0}): "
					   + "abasteça antes -- assentada de novo ela volta cheia, e é por isso.");
			return;
		}

		// (2026-10-18) O QUE ESTA MONTADO DENTRO DA NAVE GRANDE SAI PRO LADO, SOLTO -- a mesma porta da explosao, e ANTES de a nave sair da
		// lista. A recusa de cima e pra GENTE; uma maquina nao esta treinando. Ver `TirarAsObrasDaNave`.
		int obras = TirarAsObrasDaNave(n);

		// A VELOCIDADE COMPRADA VAI JUNTO (2026-10-19). Ela se perdia de proposito -- o item da mochila era a linha do catalogo
		// ("Spacepod"), sem estado, e um aviso dizia em voz alta. Agora o item tem estado: a nave melhorada vai pra mochila com
		// uma gaveta do cofre (`Spacepod|c12|20x`), como a maquina de gravidade. Ver `IdDaNaveAoRecolher` e `MaquinaGuardada`.
		string recolhida = IdDaNaveAoRecolher(n);
		_naves.Remove(n);
		GravarNaves();
		Guardar(pl, recolhida);
		Avisar(pl, $"você recolhe {NomeDaNave(n)}."
				   + (recolhida != n.Tipo ? $" A velocidade vai junto: assentada de novo, ela volta a {n.Velocidade}x." : ""));
		if (obras > 0)
			Avisar(pl, obras == 1
				? "a construção que estava lá dentro fica no chão, ao lado -- solta."
				: $"as {obras} construções que estavam lá dentro ficam no chão, ao lado -- soltas.");
		MandarObras(pl.Zone);
	}

	// =====================================================================
	// EMBARCAR E DESEMBARCAR -- o `verb/Use` (PlanetTech.dm:119)
	// =====================================================================
	/// <summary>
	/// (2026-10-18) QUEM NAO PILOTA: o caido e o CADAVER -- o morto de pe (o fantasma do Outro Mundo, o do passe do Enma, o da passagem)
	/// pilota como um vivo. O dono: *"n da pra pilotas naves nem pods quando estamos mortos, permita isso"* -- a regra dele de 2026-10-16,
	/// *"unica restriçao de morto deveria ser zenkai"*. As quatro portas (embarcar no pod, decolar o pousado, embarcar na nave grande e pegar o
	/// leme dela) perguntavam o `dead` cru. A nave NAO vira saida do Outro Mundo: de la nao se decola (`Decolar` so sobe de planeta do mapa do
	/// universo), e quem e puxado de volta pra la desembarca antes (`AcabarOPasse`, `AcabarAPassagem`).
	/// </summary>
	private bool NaoPilota(ServerPlayer pl) => pl.Ficha.KO || (NaveRecusaOMortoDePeDeTeste ? pl.Ficha.dead : EhCadaver(pl));

	/// <summary>DEFEITO INJETADO (`--naveteste`): o morto de pe volta a nao pilotar -- o `dead` cru, o jogo de antes. Falso em jogo, sempre.</summary>
	internal static bool NaveRecusaOMortoDePeDeTeste;

	private void UsarNave(ServerPlayer pl)
	{
		if (NaveDoPiloto(pl) is { } minha) { Desembarcar(pl, minha); return; }

		Nave? n = NavePerto(pl);
		if (n == null) { Avisar(pl, "não há nenhuma nave por perto."); return; }
		if (NaoPilota(pl)) { Avisar(pl, "você não está em condições de pilotar."); return; }

		// A NAVE GRANDE NAO SE VESTE, SE HABITA: o `Click()` dela chama `board()` e nao `Use()`
		// (`ShipVessel.dm:91-99`). Um jogador que apertasse E nela e saisse cavalgando um casco de
		// 128x138 px seria a leitura errada do objeto -- e a ponte, que e o que a torna diferente do
		// pod, ficaria inalcancavel.
		if (NaveGrande.EhNaveGrande(n.Tipo)) { EmbarcarNaNaveGrande(pl, n); return; }

		// (2026-10-19) NAVE NENHUMA LEVA ESFERA DO DRAGAO (dono: *"naves e espaco vc e bloqueado de ir com elas"*): o pod nao abre pra
		// quem carrega uma. A Capital Ship recusa na porta dela (`EmbarcarNaNaveGrande`). Ver `GameServer.EsferasNaMochila.cs`.
		if (PorQueAsEsferasPrendem(pl) is { Length: > 0 } presas) { Avisar(pl, presas); return; }

		// FOGUETE GASTO NAO SE PILOTA -- `if(icon_state != "stable") return` (`PlanetTech.dm:255`).
		// Entrar num foguete usado nao faria mal nenhum, mas a unica coisa que ele faria depois seria
		// recusar o lancamento -- e recusar na PORTA e dizer por que e melhor que deixar entrar e
		// descobrir la dentro.
		if (Naves.EhFoguete(n.Tipo) && n.Usada)
		{
			Avisar(pl, $"{NomeDaNave(n)} já voou: o casco está queimado. "
					   + $"Recondicionar custa {Naves.CustoDeRecondicionar:N0} zeni.");
			return;
		}

		n.PilotoId = pl.Id;
		n.LancaEm = 0;

		// PILOTAR CONTA COMO VOO (`pilot.flight = 1`, :135) -- e o que faz o pod atravessar agua,
		// lava e abismo no original (`testWaters` confere `flight`). Aqui o equivalente e o
		// `pl.Voando`, que e o mesmo bit de que sai a altura que o `Voo.ModoNaAltura` le pra validar o
		// passo com o modo `PorCima` (do mapa, so a parede de predio para quem pilota).
		pl.NaveDevolveVoo = pl.Voando;   // o `pod_had_flight` do DM (:53): quem ja voava continua voando
		pl.Voando = true;
		RecalcularVelocidade(pl);
		MandarFicha(pl);

		// O VEICULO VIRA O ALVO DA TECLA E na mesma hora em que ele sai do chao: e a troca de uma
		// porta pela outra, e ela tem que ser no mesmo instante -- senao ha um piscar em que nem o
		// objeto nem o veiculo respondem. Ver `MandarVeiculo`.
		MandarVeiculo(pl);
		MandarCombustivel(pl);   // (2026-10-18) a barra do tanque aparece no mesmo instante -- ver `GameServer.Combustivel.cs`

		// A NAVE SAI DA LISTA DE PARADAS na hora, e com ela sai a parede: e o `density = 0` do
		// original (:137). Sem isto o piloto ficaria preso dentro do proprio veiculo.
		Avisar(pl, $"você entra em {NomeDaNave(n)}. Ela voa com você -- e atravessa o que houver embaixo. "
				   + "Suba e desça como no voo: descendo até o chão ela pousa, e lá no alto ela sai do planeta.");
		SoltarPraNaoLevarAoEspaco(pl);   // o pod vai pro espaco: o agarrao acaba com essa frase, antes do leme
		TomarOLeme(pl);   // (2026-10-19) o casco isola o piloto, e as tecnicas acesas se apagam -- ver `GameServer.Leme.cs`
		GD.Print($"[server] {pl.Name} embarcou na nave #{n.Id} (velocidade {n.Velocidade})");
		MandarObras(pl.Zone);
	}

	private void Desembarcar(ServerPlayer pl, Nave n)
	{
		if (n.LancaEm > 0)
		{
			// ABORTA O LANCAMENTO, e o original faz igual: `if(!pilot || pilot != expilot)` depois do
			// sono (:70) desiste em silencio. Aqui ele diz por que.
			n.LancaEm = 0;
			Avisar(pl, "o lançamento é abortado.");
		}

		n.PilotoId = 0;
		n.PorZona(pl.Zone);
		n.X = pl.Pos.X;
		n.Y = pl.Pos.Y;
		GravarNaves();

		// `if(!pod_had_flight) pilot.flight = 0` (:126): quem nao voava por conta propria volta ao
		// chao. Sem esta linha, entrar e sair de um pod seria um jeito de aprender a voar de graca.
		if (!pl.NaveDevolveVoo) { pl.Voando = false; pl.Altitude = 0f; }

		// A FICHA SAI SEMPRE, e nao so quando o voo cai: a velocidade acabou de VOLTAR pra do corpo,
		// e ela nao esta na lista de campos que o `TickFichas` compara. Ver `RecalcularVelocidade`.
		RecalcularVelocidade(pl);
		MandarFicha(pl);

		// E O ALVO DA TECLA E VOLTA A SER O OBJETO NO CHAO: o pacote vazio apaga o veiculo montado.
		// Sem ele o menu do pod continuaria abrindo pra quem acabou de descer -- e o pod esta ali,
		// aos seus pes, ja de volta na lista de construcoes. Duas portas pra mesma coisa.
		MandarVeiculo(pl);
		MandarCombustivel(pl);   // (2026-10-18) ...e a barra do tanque some junto

		Avisar(pl, $"você sai de {NomeDaNave(n)}.");
		LargarOLeme(pl);   // (2026-10-19) fora do casco, o chao volta a pesar -- ver `GameServer.Leme.cs`
		GD.Print($"[server] {pl.Name} desembarcou da nave #{n.Id} em {pl.Zone}");
		MandarObras(pl.Zone);
	}

	// =====================================================================
	// LANCAR -- o `verb/Launch` (PlanetTech.dm:58)
	// =====================================================================
	private void LancarNave(ServerPlayer pl)
	{
		// ============================ A NAVE GRANDE SE LANCA DA PONTE ============================
		// Ela nao passa por `NaveDoPiloto`: quem manda lancar esta DENTRO dela, num console, e nao
		// montado nela. Ver `NaveDaPonteDe` -- e a mesma pergunta de alcance de sempre, feita contra
		// a celula do console em vez de contra a posicao da nave.
		if (NaveDaPonteDe(pl) is { } daPonte) { LancarDaPonte(pl, daPonte); return; }

		Nave? n = NaveDoPiloto(pl);
		if (n == null)
		{
			// O MESMO AVISO QUE O DM APRENDEU A DAR. O `Launch` sem piloto la estourava um runtime e
			// o verb morria SEM AVISO -- e o comentario no proprio original diz isso (:61-63).
			Avisar(pl, "ninguém está pilotando -- entre na nave primeiro.");
			return;
		}
		if (Espaco.EhEspaco(pl.Zone)) { Avisar(pl, "você já está no espaço."); return; }
		if (n.LancaEm > 0)
		{
			Avisar(pl, $"o lançamento já está em curso ({(n.LancaEm - NowMs()) / 1000.0:0.#} s).");
			return;
		}

		// UM FOGUETE GASTO NAO SOBE -- `if(icon_state!="stable") return` (`PlanetTech.dm:255`).
		if (Naves.EhFoguete(n.Tipo) && n.Usada)
		{
			Avisar(pl, $"{NomeDaNave(n)} já foi usado. Recondicionar custa {Naves.CustoDeRecondicionar:N0} zeni.");
			return;
		}

		// (2026-10-18) DE TANQUE VAZIO NAO SE SOBE: no espaco a nave vazia para de vez (o dono), e lancar seria largar o piloto la em cima.
		if (TanqueVazio(n)) { Avisar(pl, FraseDoTanqueVazio(n)); return; }

		double s = Naves.SegundosDeLancamento(n.Velocidade);
		n.LancaEm = NowMs() + (long)(s * 1000);

		// DE ONDE ELE SUBIU. So o foguete usa, mas guardar sempre custa tres campos e evita a
		// pergunta "sera que este caminho tambem precisa?" na proxima nave -- e o `pickTurf(nA,2)`
		// do DM (:281) tambem le uma area que foi lida ANTES do sono, e nao depois.
		n.PorZonaDaVolta(pl.Zone, pl.Pos);
		n.AvisouDaReentrada = false;

		Avisar(pl, Naves.EhFoguete(n.Tipo)
			? $"os motores acendem. ETA {s:0.#} segundo(s) -- e ele volta sozinho em "
			  + $"{Naves.SegundosEmOrbita:0} s depois disso."
			: $"os motores acendem. ETA {s:0.#} segundo(s).");
	}

	/// <summary>
	/// RECONDICIONAR O FOGUETE -- `verb/Fit` (`PlanetTech.dm:320-334`).
	///
	/// *"Fit the pod onto another rocket? Costs 100k Zenni."* -- e literalmente isso: a capsula e
	/// aproveitada, o foguete e novo. O DM nao pede que voce seja o dono; aqui pede, pela mesma
	/// razao que `MelhorarNave` pede -- gastar 100 mil zeni no equipamento de outra pessoa e um jeito
	/// de errar que nao tem desfazer.
	/// </summary>
	private void RecondicionarNave(ServerPlayer pl, string pedido)
	{
		Nave? n = MinhaNaveOuPerto(pl);
		if (n == null) { Avisar(pl, "não há nenhum foguete por perto."); return; }
		if (!Naves.EhFoguete(n.Tipo)) { Avisar(pl, $"{NomeDaNave(n)} não é um foguete."); return; }
		if (n.DonoConta.Length > 0 && !string.Equals(n.DonoConta, pl.Conta, StringComparison.OrdinalIgnoreCase))
		{ Avisar(pl, $"{NomeDaNave(n)} é de {n.DonoNome}."); return; }
		if (!n.Usada) { Avisar(pl, $"{NomeDaNave(n)} ainda está inteiro -- não há o que recondicionar."); return; }

		// EM ZENI OU, PELO DOBRO, EM RECURSOS (dono, 2026-10-10): a moeda vem no pedido -- ver `CobrarOuExplicar`.
		Moeda moeda = Recurso.LerPedidoPago(pedido).Moeda;
		if (!CobrarOuExplicar(pl, Naves.CustoDeRecondicionar, moeda, "recondicionar")) return;

		n.Usada = false;
		GravarNaves();
		MandarFicha(pl);
		Avisar(pl, $"a cápsula é montada num casco novo por {Recurso.PrecoEscrito(Naves.CustoDeRecondicionar, moeda)}. "
				   + $"{NomeDaNave(n)} voa de novo.");
		GD.Print($"[server] {pl.Name} recondicionou o foguete #{n.Id}");
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>
	/// O TIQUE DAS NAVES: a nave copia o piloto, e o lancamento vence.
	///
	/// ============================ ELE E O `while` DO DM, SEM O `sleep` ============================
	/// O original mantem um laco por pod (`while(!eject&amp;&amp;pilot) sleep(0.2)`, :140-144) copiando a
	/// posicao do piloto cinco vezes por segundo. Aqui e o tique do servidor que faz isso, a 30 Hz e
	/// pra todas as naves de uma vez -- um laco dormindo por veiculo e exatamente o que a regra 0.4
	/// proibe, e a 500 naves seriam 500 linhas de execucao acordando o tempo todo.
	/// ============================================================================================
	///
	/// A ZONA VEM JUNTO DA POSICAO, e e ela que faz o pouso funcionar de graca: quando o
	/// `TickDoEspaco` desce o piloto num planeta, a nave ja esta descrita na zona nova no MESMO
	/// tique -- sem um segundo caminho de pouso pra manter em dia.
	/// </summary>
	private void TickDasNaves()
	{
		long agora = NowMs();
		bool mudouAlgo = false;
		bool rumou = false;   // (2026-10-18) alguma nave grande andou sozinha neste tique -- ver o fim

		foreach (Nave n in _naves.ToList())
		{
			// ============================ UM FOGUETE EM ORBITA CONTA MESMO SEM PILOTO ============================
			// A guarda de `PilotoId == 0` vinha primeiro, e ela cobria todo o arquivo enquanto a unica
			// coisa que uma nave fazia sozinha era nada. O foguete faz: ele DESCE por conta propria, e
			// o `verb/Launch` do DM trata exatamente o caso de o piloto ter saltado no meio -- o
			// `if(pilot) ... else ... "Re-entry failure" / del(src)` (`PlanetTech.dm:284-292`).
			//
			// Sem esta linha antes daquela, um foguete abandonado em orbita ficaria voando pra sempre
			// com um prazo que ninguem nunca olharia: um vazamento silencioso, que e a familia de
			// defeito que este arquivo ja evitou duas vezes com `[JsonIgnore]`.
			// ================================================================================================
			if (n.VoltaEm > 0 && n.PilotoId == 0) { ReentradaSemPiloto(n, agora, ref mudouAlgo); continue; }

			// (2026-10-18) A NAVE GRANDE COM RUMO E SEM NINGUEM AO LEME SEGUE SOZINHA -- ver `GameServer.NaveRumo.cs`. No espaco, anda pro
			// destino; pousada, LANCA pela sequencia da ponte (o ramo de baixo) -- e se um reinicio comeu a contagem (ela nao vai pro disco), a
			// contagem recomeca aqui.
			if (n.PilotoId == 0 && n.TemRumo && NaveGrande.EhNaveGrande(n.Tipo))
			{
				if (Espaco.EhEspaco(n.Zona)) { rumou |= RumarSozinha(n); continue; }
				if (n.LancaEm == 0) n.LancaEm = agora + (long)(NaveGrande.SegundosDeLancamento * 1000);
			}

			// ============================ E A NAVE GRANDE TAMBEM SOBE SEM NINGUEM AO LEME ============================
			// O `do_launch` do DM (`ShipVessel.dm:179`) nao olha `pilot_mob` uma vez sequer: quem
			// aperta lancar na ponte pode nao ser quem vai dirigir, e a nave sobe do mesmo jeito. Aqui
			// a subida sem piloto e um caminho proprio porque o `Decolar` de producao move um JOGADOR,
			// e aqui nao ha um -- ver `SubirNaveSozinha`.
			//
			// QUEM ESTA DENTRO NAO E MOVIDO, e isso e o desenho: o interior nao tem lado de fora. A
			// nave e que troca de zona; a tripulacao continua na mesma sala, e ao sair pela plataforma
			// desce onde a nave estiver AGORA.
			// =====================================================================================================
			if (n.PilotoId == 0 && n.LancaEm > 0 && NaveGrande.EhNaveGrande(n.Tipo))
			{
				if (agora < n.LancaEm) continue;
				n.LancaEm = 0;
				mudouAlgo = true;
				ZoneKey dentro = NaveGrande.ZonaDoInterior(n.Id);
				bool subiu = SubirNaveSozinha(n);
				// (2026-10-18) COM RUMO ela nao espera ninguem: segue sozinha pro destino (`RumarSozinha`). Sem subir, o rumo morre aqui.
				if (!subiu) n.TemRumo = false;
				foreach (ServerPlayer abordo in ZoneList(dentro.Hash))
					Avisar(abordo, !subiu
						? $"{NomeDaNave(n)}: lançamento abortado -- este lugar não fica no mapa do universo."
						: n.TemRumo
							? $"{NomeDaNave(n)}: agora no espaço, seguindo sozinha rumo a {NomeDoRumo(n)}."
							: $"{NomeDaNave(n)}: agora no espaço. Assuma a ponte pra pilotar.");
				continue;
			}

			if (n.PilotoId == 0) continue;

			if (!_players.TryGetValue(n.PilotoId, out ServerPlayer? pl))
			{
				// O PILOTO SUMIU (deslogou, morreu e foi recolhido). A nave fica onde estava, e nao
				// em coordenada nenhuma: sem isto ela ficaria "pilotada" pra sempre, invisivel e
				// inalcancavel -- que e o mesmo modo de falha que o `[JsonIgnore]` do `PilotoId` ja
				// evita no disco.
				n.PilotoId = 0;
				n.LancaEm = 0;
				mudouAlgo = true;
				continue;
			}

			ZoneKey zonaAntes = n.Zona;
			// (2026-10-18) O QUE A NAVE ANDOU NESTE TIQUE PAGA O TANQUE -- so dentro da mesma zona: trocar de zona (decolar, pousar, a
			// reentrada) e salto, e nao viagem. Ver `GameServer.Combustivel.cs`.
			if (zonaAntes.Equals(pl.Zone)) GastarCombustivel(n, (pl.Pos - new Vec2(n.X, n.Y)).Length);
			n.PorZona(pl.Zone);
			n.X = pl.Pos.X;
			n.Y = pl.Pos.Y;

			// O LANCAMENTO VENCEU: cai no funil de decolagem de PRODUCAO (`Decolar`), que ja sabe
			// distinguir planeta pre-feito de gerado e ja recusa lugar que nao fica no mapa do
			// universo. Um segundo caminho de subida seria um segundo lugar pra esquecer o registro
			// de chunk e a vizinhanca.
			if (n.LancaEm > 0 && agora >= n.LancaEm)
			{
				n.LancaEm = 0;
				Decolar(pl);
				n.PorZona(pl.Zone);
				n.X = pl.Pos.X;
				n.Y = pl.Pos.Y;
				mudouAlgo = true;

				// O FOGUETE JA SOBE COM A HORA DA VOLTA MARCADA -- e o `sleep(150)` logo depois do
				// teleporte pro espaco (`PlanetTech.dm:276`). Ele nao decide voltar; ele SEMPRE volta.
				if (Naves.EhFoguete(n.Tipo) && Espaco.EhEspaco(pl.Zone))
				{
					n.VoltaEm = agora + (long)(Naves.SegundosEmOrbita * 1000);
					n.Usada = true;   // `didland=1` -- a subida ja gastou o casco
					Avisar(pl, $"{NomeDaNave(n)} atinge a órbita. Ele desce sozinho em "
							   + $"{Naves.SegundosEmOrbita:0} segundos.");
				}
			}

			// A REENTRADA DO FOGUETE. Duas bordas: o aviso e a descida.
			if (n.VoltaEm > 0) TicarAReentrada(n, pl, agora, ref mudouAlgo);

			// MUDOU DE MUNDO: quem esta na zona nova precisa ver a nave, e quem ficou na velha
			// precisa parar de ver. O pacote de construcoes so sai quando algo muda -- e isto muda.
			if (!zonaAntes.Equals(n.Zona))
			{
				MandarObras(zonaAntes);
				MandarObras(n.Zona);
				mudouAlgo = true;
				// (v0.2.1) CHEGOU DO ESPACO: a nave desce POUSADA. O voo nao atravessa a troca de zona (`MoveToZone`:
				// *"quem viaja chega no chao"*), e a nave pousada nao anda -- o piloto tem de saber como sair dali.
				if (Espaco.EhEspaco(zonaAntes)) AvisarDoPousoDaNave(pl);
			}
		}

		// (2026-10-18) A NAVE QUE SEGUE SOZINHA ANDA A CADA TIQUE, e o pacote de construcoes (que a desenha pra quem esta no espaco) e o disco
		// so saem quando algo muda: uma vez por segundo basta pra ninguem ver o casco congelado no ponto de partida, nem perder a viagem num
		// reinicio.
		if (rumou && agora >= _proximoPassoDoRumo)
		{
			_proximoPassoDoRumo = agora + 1000;
			MandarObras(ZonaDoEspaco);
			mudouAlgo = true;
		}

		// (2026-10-18) OS OLHOS DE QUEM OBSERVA vao pra onde as naves ficaram neste tique -- ver `GameServer.NaveObservar.cs`.
		TickDosObservadores();

		// (2026-10-18) A BARRA DO TANQUE, uma vez por segundo, pra quem pilota e pra quem observa -- e o tanque vai pro disco de tempos em
		// tempos enquanto alguem gasta. Ver `GameServer.Combustivel.cs`.
		mudouAlgo |= TickDoCombustivel(agora);

		if (mudouAlgo) GravarNaves();
	}

	// =====================================================================
	// A REENTRADA DO FOGUETE -- a segunda metade do `verb/Launch` (`PlanetTech.dm:276-292`)
	// =====================================================================
	/// <summary>
	/// O FOGUETE COM PILOTO A BORDO: avisa, e depois desce os dois juntos.
	///
	/// ============================ POR QUE ELE VOLTA PRA COORDENADA GUARDADA ============================
	/// O DM desce em `pickTurf(nA,2)` -- um tile sorteado da AREA de onde se lancou, lida ANTES do
	/// sono. Aqui a `ZoneKey` inteira e o ponto exato foram guardados no lancamento
	/// (`PorZonaDaVolta`), e a razao e a mesma que fez a nave nao ser uma `Obra`: dois planetas
	/// gerados podem ter o mesmo nome, e "voltar pra Verdejante-1042" nao diz qual delas.
	///
	/// O PONTO PASSA PELA COLISAO na descida (`PontoLivrePerto`), e nao no lancamento: o lugar de
	/// onde se subiu estava livre naquele instante, mas 25 segundos e tempo de sobra pra alguem
	/// assentar uma bancada ali. Descer dentro de uma parede e o defeito que o `PontoDeNascimento`
	/// ja tinha custado uma bancada inteira pra achar em Icer.
	/// ============================================================================================
	/// </summary>
	private void TicarAReentrada(Nave n, ServerPlayer pl, long agora, ref bool mudouAlgo)
	{
		long faltam = n.VoltaEm - agora;

		if (!n.AvisouDaReentrada
			&& faltam <= (long)((Naves.SegundosEmOrbita - Naves.SegundosAteAvisarAReentrada) * 1000))
		{
			n.AvisouDaReentrada = true;
			Avisar(pl, $"{NomeDaNave(n)}: reentrada iminente.");
		}

		if (faltam > 0) return;

		n.VoltaEm = 0;
		mudouAlgo = true;

		// ZONA DE VOLTA PERDIDA (foguete que subiu antes de um reboot, save adulterado): ele fica no
		// espaco em vez de teleportar pra lugar nenhum. Falhar alto, e nao mover o corpo pra (0,0).
		if (n.VoltaNome.Length == 0)
		{
			Avisar(pl, $"{NomeDaNave(n)} perdeu a referência de origem e fica em órbita.");
			GD.PushWarning($"[server] foguete #{n.Id} sem zona de volta");
			return;
		}

		ZoneKey destino = n.ZonaDaVolta;
		var desejado = new Vec2(n.VoltaX, n.VoltaY);
		Vec2 onde = MapaDaZonaOuCatalogo(destino)?.PontoLivrePerto(desejado) ?? desejado;

		ViajarVoluntario(pl, destino, onde);
		n.PorZona(destino);
		n.X = onde.X;
		n.Y = onde.Y;
		Avisar(pl, $"{NomeDaNave(n)}: reentrada bem-sucedida. O casco não serve pra outra viagem.");
		GD.Print($"[server] foguete #{n.Id} desceu com {pl.Name} em {destino}");
	}

	/// <summary>
	/// O FOGUETE QUE VOLTOU VAZIO -- `else ... "Re-entry failure" ... del(src)` (`PlanetTech.dm:290-292`).
	///
	/// O DM DESTROI O FOGUETE, e nao e crueldade: sem piloto ninguem controla a descida, e a nave
	/// que voltasse sozinha estaria de graca pra quem a abandonou. Aqui vale igual -- e e por isso
	/// que saltar do foguete em orbita e uma decisao e nao um descuido: quem tem como voar sozinho
	/// troca 200 mil zeni por uma passagem so de ida.
	/// </summary>
	private void ReentradaSemPiloto(Nave n, long agora, ref bool mudouAlgo)
	{
		if (agora < n.VoltaEm) return;

		n.VoltaEm = 0;
		mudouAlgo = true;
		ZoneKey ondeEstava = n.Zona;
		_naves.Remove(n);
		GD.Print($"[server] foguete #{n.Id} se perdeu na reentrada (sem piloto)");
		MandarObras(ondeEstava);
	}

	// =====================================================================
	// UPGRADE E INFO -- `verb/Upgrade` (:190) e `verb/Info` (:185)
	// =====================================================================
	private void MelhorarNave(ServerPlayer pl, string pedido)
	{
		// ============================ A NAVE GRANDE SE MELHORA DA PONTE ============================
		// `MinhaNaveOuPerto` procura no CHAO da zona em que o corpo esta, e quem esta dentro da Capital
		// Ship esta noutra zona que ela -- ele nunca acharia a propria nave. Mesma correcao do `InfoDaNave`.
		//
		// ============================ E ELA PODE SER MELHORADA, EMBORA O DM NAO DEIXE ============================
		// O `obj/PlayerShip` do original nao tem `verb/Upgrade` nem `var/Speed`: la a nave grande anda na
		// velocidade do piloto, e o `Speed` da Spacepod nao acelerava viagem nenhuma (ver o cabecalho de
		// `Naves`). Como este port CONSTRUIU velocidade de nave a pedido da spec, deixar a Capital Ship
		// de fora criaria uma coisa que o DM nunca teve: uma nave de dois milhoes de zeni mais lenta que
		// um pod de cem mil.
		//
		// O que se GANHOU: a escada de preco e o teto sao os mesmos, entao nao ha regra nova pra
		// balancear. O que se PERDEU: uma divergencia declarada com o original, que nao tinha esse
		// problema porque nao tinha esse numero.
		// ====================================================================================================
		Nave? n = NaveDaPonteDe(pl) ?? NaveDesteInterior(pl) ?? MinhaNaveOuPerto(pl);
		if (n == null) { Avisar(pl, "não há nenhuma nave por perto."); return; }
		if (n.DonoConta.Length > 0 && !string.Equals(n.DonoConta, pl.Conta, StringComparison.OrdinalIgnoreCase))
		{ Avisar(pl, $"{NomeDaNave(n)} é de {n.DonoNome}."); return; }

		if (n.Velocidade >= Naves.VelocidadeMaxima)
		{ Avisar(pl, $"{NomeDaNave(n)} já está no limite ({Naves.VelocidadeMaxima}x)."); return; }
		// (2026-10-19) O TETO DE QUEM PAGA (`TetoPorTecnologia`): a recusa vem antes da cobranca, e a nave que ja passou dele
		// continua como esta.
		int teto = TetoPorTecnologia.VelocidadeDaNave(pl.Ficha.techskill);
		if (n.Velocidade >= teto)
		{ Avisar(pl, TetoPorTecnologia.Recusa($"a velocidade de {NomeDaNave(n)}", pl.Ficha.techskill, $"{teto}x", proporcional: true)); return; }

		// EM ZENI OU, PELO DOBRO, EM RECURSOS (dono, 2026-10-10): a moeda vem no pedido -- ver `CobrarOuExplicar`.
		double custo = Naves.CustoDoUpgrade(n.Velocidade);
		Moeda moeda = Recurso.LerPedidoPago(pedido).Moeda;
		if (!CobrarOuExplicar(pl, custo, moeda, "o próximo degrau")) return;

		n.Velocidade++;
		GravarNaves();

		// MELHOROU COM ELA EMBAIXO: o degrau novo tem que chegar ao cliente AGORA. Ver
		// `RecalcularVelocidade` -- a velocidade nao dispara reenvio de ficha sozinha.
		if (n.PilotoId == pl.Id) { RecalcularVelocidade(pl); MandarFicha(pl); }

		Avisar(pl, $"velocidade agora é {n.Velocidade}x ({Recurso.PrecoEscrito(custo, moeda)}). "
				   + $"Terra→Namek: {Naves.SegundosDeViagem(Espaco.DistanciaTerraNamek, n.Velocidade) / 60:0.#} min.");
		GD.Print($"[server] {pl.Name} melhorou a nave #{n.Id} pra velocidade {n.Velocidade}");
	}

	private void InfoDaNave(ServerPlayer pl)
	{
		// O `MinhaNaveOuPerto` NAO ALCANCA QUEM ESTA DENTRO: a nave grande esta noutra zona que o
		// corpo. `NaveDaPonteDe` responde por ela -- ver `GameServer.NaveGrande.cs`. (2026-10-18) Nem
		// quem a observa de fora quando ela tem piloto (ela nao esta parada): `NaveQueObservo`.
		Nave? n = NaveDaPonteDe(pl) ?? NaveDesteInterior(pl) ?? NaveQueObservo(pl) ?? MinhaNaveOuPerto(pl);
		if (n == null) { Avisar(pl, "não há nenhuma nave por perto."); return; }

		double d = Espaco.DistanciaTerraNamek;
		Avisar(pl, $"-- {NomeDaNave(n)} #{n.Id} --");
		Avisar(pl, $"  dono: {(n.DonoNome.Length > 0 ? n.DonoNome : "ninguém")}");
		// (2026-10-19) ...e o teto de quem pergunta, quando a tecnologia dele nao chega ao do jogo (`TetoPorTecnologia`).
		Avisar(pl, $"  velocidade: {n.Velocidade}x  (teto {Naves.VelocidadeMaxima}x"
				   + (TetoPorTecnologia.Livre(pl.Ficha.techskill) ? ")" : $"; a sua tecnologia leva até {TetoPorTecnologia.VelocidadeDaNave(pl.Ficha.techskill)}x)"));
		// (2026-10-18) O TANQUE, so em quem tem: o foguete nao viaja, e "0 de combustivel" nele diria que ele esta seco.
		if (Combustivel.TemTanque(n.Tipo))
			Avisar(pl, $"  combustível: {NivelDoTanque(n):N0} de {Combustivel.Capacidade(n.Tipo):N0} "
					   + $"(um tanque leva {Combustivel.TilesDeAlcance(n.Tipo):N0} tiles; abastecer custa {Combustivel.RecursosPorUnidade:0.#} recurso por unidade)");
		Avisar(pl, $"  Terra→Namek: {Naves.SegundosDeViagem(d, n.Velocidade) / 60:0.#} min reais "
				   + $"({Naves.DiasInGame(d, n.Velocidade):0.##} dias in-game)");
		Avisar(pl, $"  espera do lançamento: {SegundosDeLancamentoDe(n):0.#} s");
		Avisar(pl, n.Velocidade >= Naves.VelocidadeMaxima
			? "  no limite: não há mais degrau."
			: $"  próximo degrau: {Recurso.DoisPrecos(Naves.CustoDoUpgrade(n.Velocidade))}");

		// O CASCO SO APARECE EM QUEM TEM CASCO. Escrever "0%" numa Spacepod diria que ela esta
		// destruida, quando o certo e que ela nao participa daquele sistema.
		if (n.ArmaduraMax > 0)
			Avisar(pl, $"  casco: {n.Armadura / n.ArmaduraMax * 100:0}% ({n.Armadura:N0} de {n.ArmaduraMax:N0})");
		if (NaveGrande.EhNaveGrande(n.Tipo))
			Avisar(pl, n.Senha.Length > 0 ? "  trancada: só quem sabe a senha embarca." : "  destrancada.");
		if (Naves.EhFoguete(n.Tipo))
			Avisar(pl, n.Usada
				? $"  JÁ USADO: recondicionar custa {Recurso.DoisPrecos(Naves.CustoDeRecondicionar)}."
				: "  inteiro: pode lançar uma vez.");
		if (n.VoltaEm > 0) Avisar(pl, $"  em órbita: desce em {(n.VoltaEm - NowMs()) / 1000.0:0} s.");

		// O SALDO AO LADO DOS PRECOS (quando ha o que pagar nela): e com ele que se escolhe a moeda.
		if (n.Velocidade < Naves.VelocidadeMaxima || (Naves.EhFoguete(n.Tipo) && n.Usada))
			Avisar(pl, $"  você tem {CarteiraEscrita(pl)}.");

		Avisar(pl, $"  onde: {n.Zona}");
		if (n.PilotoId == pl.Id) Avisar(pl, "  você está pilotando.");
	}

	/// <summary>
	/// A ESPERA DO LANCAMENTO DESTA NAVE. Uma pergunta, duas respostas, e elas nao sao a mesma
	/// funcao: o pod divide `400/Speed` (`PlanetTech.dm:68`) e a nave grande e fixa em 10 s
	/// (`ShipVessel.dm:186`, e o proprio texto de la diz "ETA 10 seconds").
	/// </summary>
	private static double SegundosDeLancamentoDe(Nave n) =>
		NaveGrande.EhNaveGrande(n.Tipo)
			? NaveGrande.SegundosDeLancamento
			: Naves.SegundosDeLancamento(n.Velocidade);

	// =====================================================================
	// O CANAL
	// =====================================================================
	/// <summary>
	/// OS VERBOS DA NAVE. Devolve `true` quando o comando era deste canal -- inclusive quando ele
	/// foi RECUSADO, pela mesma razao do `ComandoDeInteracao`.
	/// </summary>
	private bool ComandoDeNave(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "nave_usar": UsarNave(pl); return true;
			case "nave_lancar": LancarNave(pl); return true;
			case "nave_melhorar": MelhorarNave(pl, arg); return true;
			case "nave_info": InfoDaNave(pl); return true;
			case "nave_pegar": PegarNave(pl); return true;
			case "nave_recondicionar": RecondicionarNave(pl, arg); return true;
			case "nave_abastecer": Abastecer(pl); return true;   // (2026-10-18) ver `GameServer.Combustivel.cs`

			// A CAMADA 2 -- arquivo proprio (`GameServer.NaveGrande.cs`) pela mesma razao que a
			// camada 1 nao mora no `GameServer.Tech.cs`: interior, ponte e ejecao sao um sistema, e
			// nao mais cinco `case`.
			case "nave_embarcar":
			case "nave_sair":
			case "nave_observar":
			case "nave_pilotar":
			case "nave_senha":
				return ComandoDaNaveGrande(pl, cmd, arg);

			default: return false;
		}
	}
}
