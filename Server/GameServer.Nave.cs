using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
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
	/// fica em zero e a nave nao entra no funil de estrago: o `obj/Spacepod` do DM nao declara
	/// `fragile` e nao tem verb de destruir, entao um pod nao se quebra -- se recolhe.
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
/// ============================ O QUE **NAO** FOI INVENTADO ============================
/// **Combustivel nao existe.** Nenhuma das tres naves do original consome nada (`grep fuel` no DM:
/// zero), e inventar um recurso novo seria design sem pedido. O que segura o abuso e o preco: 100
/// mil pra ter, 741 mil pra chegar ao teto de velocidade.
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

	/// <summary>A nave que este corpo esta pilotando, ou nula.</summary>
	private Nave? NaveDoPiloto(ServerPlayer pl) => _naves.FirstOrDefault(n => n.PilotoId == pl.Id);

	/// <summary>Ele esta pilotando alguma coisa? E o bit que vai no snapshot.</summary>
	public bool EstaPilotando(int id) => _naves.Any(n => n.PilotoId == id);

	/// <summary>
	/// ...E E A GRANDE? O segundo bit do snapshot, e ele existe pra o cliente escolher o SPRITE --
	/// ver `EntityState.NaveGrande`. Sem ele, quem pilota uma Capital Ship apareceria dentro de uma
	/// capsula de um tile.
	/// </summary>
	public bool PilotaNaveGrande(int id) =>
		_naves.Any(n => n.PilotoId == id && NaveGrande.EhNaveGrande(n.Tipo));

	/// <summary>
	/// A NAVE PARADA AO ALCANCE DA MAO -- mesmo `AlcanceDeUso` de qualquer construcao, e mesma
	/// ordenacao por distancia que o `ObraPerto` usa.
	/// </summary>
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
		pl.SpeedStat = MoveRules.SpeedStatFrom(pl.Ficha.Espeed)
					 * (float)Jandirus.Core.Stats.Esmagamento.FatorDePasso(pl.Ficha);

		// NADAR E MAIS DEVAGAR QUE ANDAR -- `mobTime -= 0.3` (`movement handler.dm:66`). Entra AQUI
		// e nao no tique do nado pelo motivo que este metodo inteiro existe: velocidade tem um dono
		// so neste port, e daqui ela segue pelos dois trilhos que precisam concordar (a ficha, que o
		// cliente obedece, e o `ValidateStep`, que o servidor confere). Aplicada so num dos dois,
		// seria o cliente nadando rapido e o servidor puxando de volta trinta vezes por segundo.
		//
		// DEPOIS DO ESMAGAMENTO E ANTES DA NAVE, na mesma ordem e pela mesma razao do peso: sao
		// penalidades do CORPO, e dentro de uma nave a velocidade passa a ser a do veiculo.
		if (pl.Nadando)
			pl.SpeedStat *= (float)Jandirus.Core.World.Nado.FatorDePasso(pl.Ficha.Epspeed);

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
	/// </summary>
	private void AssentarNave(ServerPlayer pl, Construcao c, float x, float y)
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

		_naves.Add(nave);
		GravarNaves();

		GD.Print($"[server] {pl.Name} assentou {c.Nome} (nave #{nave.Id}) em {pl.Zone} @ ({x:0},{y:0})"
				 + (nave.ArmaduraMax > 0 ? $" -- casco {nave.ArmaduraMax:N0}" : ""));
		Avisar(pl, NaveGrande.EhNaveGrande(c.Id)
			? $"você assenta {c.Nome} no chão. Aperte E nela pra EMBARCAR -- a ponte fica lá dentro."
			: $"você assenta {c.Nome} no chão. Aperte E nela pra entrar.");
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

		// A VELOCIDADE COMPRADA SE PERDE, e isso e deliberado: o item da mochila e uma linha do
		// catalogo ("Spacepod"), sem estado proprio. Guardar o degrau exigiria item COM ESTADO, que
		// e um sistema que este port nao tem -- e fingir que tem seria pior. O aviso diz em voz alta,
		// que e a regra 5 da casa (falhar alto em vez de silenciar).
		if (n.Velocidade > Naves.VelocidadeInicial)
			Avisar(pl, $"ao desmontar, os {n.Velocidade - 1} degraus de velocidade se perdem.");

		_naves.Remove(n);
		GravarNaves();
		Guardar(pl, n.Tipo);
		Avisar(pl, $"você recolhe {NomeDaNave(n)}.");
		MandarObras(pl.Zone);
	}

	// =====================================================================
	// EMBARCAR E DESEMBARCAR -- o `verb/Use` (PlanetTech.dm:119)
	// =====================================================================
	private void UsarNave(ServerPlayer pl)
	{
		if (NaveDoPiloto(pl) is { } minha) { Desembarcar(pl, minha); return; }

		Nave? n = NavePerto(pl);
		if (n == null) { Avisar(pl, "não há nenhuma nave por perto."); return; }
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "você não está em condições de pilotar."); return; }

		// A NAVE GRANDE NAO SE VESTE, SE HABITA: o `Click()` dela chama `board()` e nao `Use()`
		// (`ShipVessel.dm:91-99`). Um jogador que apertasse E nela e saisse cavalgando um casco de
		// 128x138 px seria a leitura errada do objeto -- e a ponte, que e o que a torna diferente do
		// pod, ficaria inalcancavel.
		if (NaveGrande.EhNaveGrande(n.Tipo)) { EmbarcarNaNaveGrande(pl, n); return; }

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
		// `pl.Voando`, que e o mesmo bit que o `AtravessandoCenario` (GameServer.Voo.cs:399) le pra
		// validar o passo com o mapa NULO.
		pl.NaveDevolveVoo = pl.Voando;   // o `pod_had_flight` do DM (:53): quem ja voava continua voando
		pl.Voando = true;
		RecalcularVelocidade(pl);
		MandarFicha(pl);

		// O VEICULO VIRA O ALVO DA TECLA E na mesma hora em que ele sai do chao: e a troca de uma
		// porta pela outra, e ela tem que ser no mesmo instante -- senao ha um piscar em que nem o
		// objeto nem o veiculo respondem. Ver `MandarVeiculo`.
		MandarVeiculo(pl);

		// A NAVE SAI DA LISTA DE PARADAS na hora, e com ela sai a parede: e o `density = 0` do
		// original (:137). Sem isto o piloto ficaria preso dentro do proprio veiculo.
		Avisar(pl, $"você entra em {NomeDaNave(n)}. Ela anda com você -- e atravessa o que houver embaixo.");
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

		Avisar(pl, $"você sai de {NomeDaNave(n)}.");
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
	private void RecondicionarNave(ServerPlayer pl)
	{
		Nave? n = MinhaNaveOuPerto(pl);
		if (n == null) { Avisar(pl, "não há nenhum foguete por perto."); return; }
		if (!Naves.EhFoguete(n.Tipo)) { Avisar(pl, $"{NomeDaNave(n)} não é um foguete."); return; }
		if (n.DonoConta.Length > 0 && !string.Equals(n.DonoConta, pl.Conta, StringComparison.OrdinalIgnoreCase))
		{ Avisar(pl, $"{NomeDaNave(n)} é de {n.DonoNome}."); return; }
		if (!n.Usada) { Avisar(pl, $"{NomeDaNave(n)} ainda está inteiro -- não há o que recondicionar."); return; }

		if (pl.Ficha.Zeni < Naves.CustoDeRecondicionar)
		{
			Avisar(pl, $"recondicionar custa {Naves.CustoDeRecondicionar:N0} zeni -- você tem {pl.Ficha.Zeni:N0}.");
			return;
		}

		pl.Ficha.Zeni -= Naves.CustoDeRecondicionar;
		n.Usada = false;
		GravarNaves();
		MandarFicha(pl);
		Avisar(pl, $"a cápsula é montada num casco novo. {NomeDaNave(n)} voa de novo.");
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
				foreach (ServerPlayer abordo in ZoneList(dentro.Hash))
					Avisar(abordo, subiu
						? $"{NomeDaNave(n)}: agora no espaço. Assuma a ponte pra pilotar."
						: $"{NomeDaNave(n)}: lançamento abortado -- este lugar não fica no mapa do universo.");
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
			}
		}

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

		MoveToZone(pl.Id, destino, onde);
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
	private void MelhorarNave(ServerPlayer pl)
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

		double custo = Naves.CustoDoUpgrade(n.Velocidade);
		if (pl.Ficha.Zeni < custo)
		{ Avisar(pl, $"o próximo degrau custa {custo:N0} zeni -- você tem {pl.Ficha.Zeni:N0}."); return; }

		pl.Ficha.Zeni -= custo;
		n.Velocidade++;
		GravarNaves();

		// MELHOROU COM ELA EMBAIXO: o degrau novo tem que chegar ao cliente AGORA. Ver
		// `RecalcularVelocidade` -- a velocidade nao dispara reenvio de ficha sozinha.
		if (n.PilotoId == pl.Id) { RecalcularVelocidade(pl); MandarFicha(pl); }

		Avisar(pl, $"velocidade agora é {n.Velocidade}x ({custo:N0}z). "
				   + $"Terra→Namek: {Naves.SegundosDeViagem(Espaco.DistanciaTerraNamek, n.Velocidade) / 60:0.#} min.");
		GD.Print($"[server] {pl.Name} melhorou a nave #{n.Id} pra velocidade {n.Velocidade}");
	}

	private void InfoDaNave(ServerPlayer pl)
	{
		// O `MinhaNaveOuPerto` NAO ALCANCA QUEM ESTA DENTRO: a nave grande esta noutra zona que o
		// corpo. `NaveDaPonteDe` responde por ela -- ver `GameServer.NaveGrande.cs`.
		Nave? n = NaveDaPonteDe(pl) ?? NaveDesteInterior(pl) ?? MinhaNaveOuPerto(pl);
		if (n == null) { Avisar(pl, "não há nenhuma nave por perto."); return; }

		double d = Espaco.DistanciaTerraNamek;
		Avisar(pl, $"-- {NomeDaNave(n)} #{n.Id} --");
		Avisar(pl, $"  dono: {(n.DonoNome.Length > 0 ? n.DonoNome : "ninguém")}");
		Avisar(pl, $"  velocidade: {n.Velocidade}x  (teto {Naves.VelocidadeMaxima}x)");
		Avisar(pl, $"  Terra→Namek: {Naves.SegundosDeViagem(d, n.Velocidade) / 60:0.#} min reais "
				   + $"({Naves.DiasInGame(d, n.Velocidade):0.##} dias in-game)");
		Avisar(pl, $"  espera do lançamento: {SegundosDeLancamentoDe(n):0.#} s");
		Avisar(pl, n.Velocidade >= Naves.VelocidadeMaxima
			? "  no limite: não há mais degrau."
			: $"  próximo degrau: {Naves.CustoDoUpgrade(n.Velocidade):N0} zeni");

		// O CASCO SO APARECE EM QUEM TEM CASCO. Escrever "0%" numa Spacepod diria que ela esta
		// destruida, quando o certo e que ela nao participa daquele sistema.
		if (n.ArmaduraMax > 0)
			Avisar(pl, $"  casco: {n.Armadura / n.ArmaduraMax * 100:0}% ({n.Armadura:N0} de {n.ArmaduraMax:N0})");
		if (NaveGrande.EhNaveGrande(n.Tipo))
			Avisar(pl, n.Senha.Length > 0 ? "  trancada: só quem sabe a senha embarca." : "  destrancada.");
		if (Naves.EhFoguete(n.Tipo))
			Avisar(pl, n.Usada
				? $"  JÁ USADO: recondicionar custa {Naves.CustoDeRecondicionar:N0} zeni."
				: "  inteiro: pode lançar uma vez.");
		if (n.VoltaEm > 0) Avisar(pl, $"  em órbita: desce em {(n.VoltaEm - NowMs()) / 1000.0:0} s.");

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
			case "nave_melhorar": MelhorarNave(pl); return true;
			case "nave_info": InfoDaNave(pl); return true;
			case "nave_pegar": PegarNave(pl); return true;
			case "nave_recondicionar": RecondicionarNave(pl); return true;

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
