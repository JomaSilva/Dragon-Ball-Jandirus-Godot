using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>Uma construcao DE PE no mundo. E o que o `mundo.json` guarda.</summary>
public sealed class Obra
{
	public int Id;
	public string Tipo = "";        // o id do catalogo ("Research_Station")

	/// <summary>
	/// ONDE ELA ESTA -- as TRES partes da <see cref="ZoneKey"/>, e nao o nome.
	///
	/// ============================ O NOME NAO E ENDERECO, E ISSO JA CUSTOU CARO ============================
	/// Ate aqui este campo era uma STRING de nome e a carga o remontava com `ZoneKey.Premade(...)`.
	/// Duas coisas quebravam, e as duas CORROMPEM DADO em vez de dar erro:
	///
	///   * uma construcao erguida num planeta GERADO voltava do disco numa zona PRE-FEITA que nao
	///     existe -- ela sumia do mundo sem log, e continuava ocupando id e lista;
	///   * dois planetas gerados HOMONIMOS dividiam as mesmas obras. Homonimo acontece: o padrao
	///     `{bioma}-{|Sx|%1000}{|Sy|%1000}{k}` de `SistemaSolar.Planeta` PERDE O SINAL das DUAS
	///     coordenadas, e a varredura do universo de producao acha "Deserto-120" **tres vezes** --
	///     `[-1:-2]k0`, `[1:-2]k0` e `[1:2]k0`. O primeiro par e o que prova que nem o sinal de `Sx`
	///     sobrevive. Isto nao e afirmacao de comentario: a secao 7 da `--obrateste` VARRE o universo,
	///     acha o par sozinha e imprime as celulas -- no dia em que a seed do universo mudar, o log
	///     dela diz qual par existe agora.
	///
	/// ============================ POR QUE A ZoneKey, E NAO O ENDERECO (Sx, Sy, K) ============================
	/// A trinca (sistema, sistema, orbita) e a que o <see cref="Jandirus.Core.Races.Berco"/> e o
	/// <see cref="Dominio"/> guardam -- e o cabecalho do `Dominio.Sx` diz com todas as letras o que ela
	/// e: **POSICAO, nao identidade**. Ela responde "onde no ceu fica este corpo", e uma obra nao mora
	/// num corpo celeste: mora numa ZONA. Ha zonas que nao sao corpo nenhum e que nao teriam endereco
	/// (o interior de nave, a Sala do Tempo, o proprio espaco), e a `Obra` teria que inventar um valor
	/// pra elas -- que e a terceira chave que ninguem quer.
	///
	/// A `ZoneKey` inteira e a mesma resposta que o `Berco` da quando o PERGUNTAM por zona
	/// (`Berco.Zona`: `Premade(nome)` ou `Procedural(nome, seed)`), e ja foi escolhida DUAS vezes neste
	/// repo pelo mesmo motivo: a `Nave` (`GameServer.Nave.cs:42-44`) e o save de personagem
	/// (`CharacterStore.cs:170-180`) guardam exatamente estes tres campos. Nao ha chave nova aqui --
	/// ha uma chave que faltava.
	/// ======================================================================================================
	/// </summary>
	public byte ZonaTipo;
	public string ZonaNome = "";
	public ulong ZonaSeed;

	/// <summary>
	/// A ZONA, montada dos tres campos. `[JsonIgnore]` porque o disco guarda as PARTES: gravar a
	/// montagem junto poria duas verdades no mesmo arquivo pra divergirem.
	/// </summary>
	[JsonIgnore] public ZoneKey Zona => new(ZonaTipo, ZonaNome, ZonaSeed);

	public void PorZona(ZoneKey z) { ZonaTipo = z.Kind; ZonaNome = z.Name; ZonaSeed = z.Seed; }

	/// <summary>
	/// ============================ A MIGRACAO DO `mundo.json` ANTIGO -- ELA CONVERTE ============================
	/// O disco de ontem tem `"Zona": "Earth"`, uma string. Sem esta porta ela seria IGNORADA na carga e
	/// toda obra ja gravada voltaria com `ZonaNome` vazio -- de pe numa zona que nao existe, invisivel,
	/// e sem ninguem por perto pra recolher. Isso e descartar calado, com outro nome.
	///
	/// Entao ela CONVERTE, e a conversao e exatamente o que o codigo antigo fazia ao ler: nome puro vira
	/// `ZoneKey.Premade(nome)`. Nao ha perda nenhuma nisso -- a carga antiga ja tratava TODA obra como
	/// pre-feita, entao a que estava num planeta gerado ja nao voltava pro lugar certo. O que se ganha e
	/// que a bancada da Terra continua na Terra, e que a partir de agora a de um mundo sorteado tambem.
	///
	/// O <see cref="Migrada"/> nao vai pro disco: ele so existe pra a carga poder DIZER quantas converteu
	/// (regra da casa: o que muda dado se anuncia). Na primeira gravacao o campo velho desaparece do
	/// arquivo sozinho, e esta porta passa a nunca mais disparar.
	/// ========================================================================================================
	/// </summary>
	[JsonInclude]
	[JsonPropertyName("Zona")]
	public string ZonaDoDiscoAntigo
	{
		set
		{
			if (string.IsNullOrEmpty(value)) return;
			ZonaTipo = ZoneKey.KindPremade;
			ZonaNome = value;
			ZonaSeed = 0;
			Migrada = true;
		}
	}

	/// <summary>Esta obra veio do formato antigo? So a carga le, e so pra contar.</summary>
	[JsonIgnore] public bool Migrada;

	public float X, Y;
	public string DonoConta = "";   // quem ergueu -- CONTA, nao nome (ver o comentario do trono)
	public string DonoNome = "";
	public bool Aparafusada;        // `Bolted`: aparafusada nao se carrega, e so o dono desfaz

	/// <summary>
	/// VEIO DO MAPA, e nao de alguem que a ergueu.
	///
	/// ============================ POR QUE ELA NAO VAI PRO DISCO ============================
	/// Ela nasce do `.objetos` da zona a cada boot -- a fonte dela e o mapa, nao o `mundo.json`.
	/// Gravar as duas juntas duplicaria cada bancada a cada reinicio, e no dia em que o mapa fosse
	/// reconvertido o disco teria a versao velha discutindo com a nova.
	///
	/// No mais ela e uma construcao como qualquer outra: bloqueia, aparece na tela, responde aos
	/// comandos e conta pro alcance de uso. E isso e a coisa toda -- a bancada que estava no mapa
	/// desde sempre passa a servir pra estudar.
	/// </summary>
	public bool DoMapa;

	/// <summary>
	/// O ESTADO DA MAQUINA DE GRAVIDADE, quando esta obra e uma. Nulo em todo o resto.
	///
	/// ============================ POR QUE ELE MORA NA OBRA ============================
	/// Duas maquinas de gravidade no mesmo mapa sao duas maquinas: uma pode estar a 50x com a
	/// bateria no fim e a outra desligada e recem-melhorada. Guardar isso num dicionario paralelo
	/// (id -> estado) funcionaria ate a obra ser destruida sem ninguem limpar o dicionario -- e
	/// entao a proxima maquina a receber aquele id herdaria a bateria da morta.
	///
	/// Dentro da obra, o estado nasce e morre com ela, e vai pro `mundo.json` de graca.
	/// ==================================================================================
	/// </summary>
	public GravidadeDaObra? Gravidade;

	/// <summary>
	/// QUE LABORATORIO FOI INSTALADO no mainframe: 0 nenhum, 1 Android Lab, 2 Bio-Android Lab.
	///
	/// E assim no original e vale manter: nao se CONSTROI um laboratorio, se constroi o
	/// mainframe e depois se escolhe o que ele vira. A escolha e definitiva, e e ela que
	/// separa quem quis virar maquina de quem quis FAZER uma.
	/// </summary>
	public int Lab;
	public double Vida = 8;         // `DNL_LAB_HEALTH`: 8 golpes derrubam
	public long ErguidaEm;

	/// <summary>
	/// A ARMADURA -- e o que decide se esta coisa cai quando alguem soca ou voa nela.
	///
	/// ============================ POR QUE NAO E `Resistencia` ============================
	/// Parede e chao tem `Resistance` e caem por LIMIAR; objeto tem `armor`/`maxarmor` e cai por
	/// dano ACUMULADO, com um piso de 75% que ignora quem bate fraco. Sao dois sistemas no DM e
	/// aqui tambem -- ver <see cref="Jandirus.Core.Combat.Armadura"/>.
	///
	/// O TETO SAI DE QUEM ERGUEU. No original, `built.maxarmor = intBPcap` (`buildable.dm:414`):
	/// a bancada de um lutador forte e forte. A mobilia que ja estava no mapa fica no padrao 1 e
	/// cai no primeiro soco de qualquer um -- de proposito, e sem prejuizo, porque ela renasce do
	/// `.objetos` no proximo boot (ver <see cref="DoMapa"/>). O que alguem ergueu vai pro disco e
	/// nao volta, e e por isso que so ela tem armadura de verdade.
	/// </summary>
	public double ArmaduraMax = Jandirus.Core.Combat.Armadura.Padrao;
	public double Armadura = Jandirus.Core.Combat.Armadura.Padrao;

	/// <summary>Se for um laboratorio: em que estado esta a gestacao (ver <see cref="Gestacao"/>).</summary>
	public Gestacao? Fornada;
}

/// <summary>
/// A GESTACAO DO BIO-ANDROIDE. Um mes in-game (`DNL_BIO_BREW_MONTHS 0.1` de ano) contando os
/// DNAs colhidos -- e quem morre quando termina e o CRIADOR.
/// </summary>
public sealed class Gestacao
{
	public List<string> Dna = [];        // as racas colhidas
	public double MaiorBp;               // BP do doador mais forte: o bio nasce com metade
	public long PrometidaEm;             // ms reais em que a gestacao termina
	public string DonoConta = "";
}

/// <summary>
/// TECNOLOGIA: o caminho de quem nao sobe socando pedra.
///
/// TRES SISTEMAS QUE SE SEGURAM: estudar sobe `techskill`, `techskill` + zeni compram CONSTRUCOES,
/// e duas construcoes especificas (os laboratorios) transformam quem as ergueu -- em androide, ou
/// no criador de um bio-androide. Cortar qualquer um deixa os outros sem sentido: sem construcao
/// nao ha o que fazer com tecnologia, e sem os labs nao ha por que juntar meio milhao de zeni.
///
/// O MUNDO PERSISTE SEPARADO DAS CONTAS (`mundo.json`). Uma construcao nao e propriedade de um
/// personagem, e uma coisa que EXISTE no chao -- e continua existindo com o dono desconectado, ou
/// morto. Guardar isso dentro do save do dono faria o laboratorio piscar junto com ele.
/// </summary>
public partial class GameServer
{
	private CatalogoDeObras? _obras;
	private readonly List<Obra> _noChao = [];
	private int _proximaObraId = 1;

	/// <summary>
	/// O raio, em pixels, dentro do qual da pra usar uma construcao.
	///
	/// O NUMERO E DO CORE (<see cref="Interacoes.Alcance"/>) e nao daqui: o cliente desenha o menu
	/// com ele e o servidor aceita o verbo com ele. Duas copias que combinavam por acaso ja
	/// deixaram uma terceira (a do console da ponte) divergir em 16 px -- ver o comentario de la.
	/// </summary>
	private const float AlcanceDeUso = Interacoes.Alcance;

	/// <summary>
	/// TECH MINIMO PRO LABORATORIO DE DNA (`DNL_INT_REQ`). Setenta pontos: e o numero que faz um
	/// humano precisar de uma vida de estudo pra chegar onde um Saiyajin chega nascendo.
	/// </summary>
	private const double TechDoLaboratorio = 70;

	private const double CustoAndroideAbsorcao = 1_000_000;
	private const double CustoAndroideInfinito = 2_000_000;
	private const double BpAndroideAbsorcao = 1_000_000;
	private const double BpAndroideInfinito = 2_000_000;

	/// <summary>
	/// A GESTACAO: UM MES IN-GAME (`DNL_BIO_BREW_MONTHS 0.1` de ano, que o comentario do original
	/// traduz como "1 MES in-game").
	///
	/// NA ESCALA DESTE JOGO ISSO E ENORME -- o dia in-game tem 24 minutos reais, entao trinta
	/// dias sao DOZE HORAS de relogio. E o preco certo: e a mesma escala que faz Terra->Namek
	/// levar 168 minutos, e o bio-androide e a coisa mais cara que alguem pode fazer. Quem
	/// comeca uma fornada tem que defender aquele tanque por meio dia.
	///
	/// A primeira versao disto multiplicava `0.1 * 12` e dava 1,2 DIAS em vez de um mes -- o erro
	/// classico de misturar "fracao de ano" com "numero de meses". Por isso a conta esta escrita
	/// em dias, que e a unidade que o resto do jogo usa.
	/// </summary>
	private const double DiasDeGestacao = 30;

	private double GestacaoSegundosReais => _gestacaoDeTeste > 0
		? _gestacaoDeTeste
		: DiasDeGestacao * Espaco.SegundosPorDiaInGame;

	/// <summary>Bancada: encurta a gestacao (`--gestacaoteste N`, em segundos reais).</summary>
	private double _gestacaoDeTeste;

	/// <summary>`DNL_BIO_BP_SHARE`: o bio nasce com metade do BP do doador mais forte.</summary>
	private const double BioFracaoDoDoador = 0.5;

	/// <summary>`DNL_BIO_MAX_DNA`.</summary>
	private const int BioMaxDna = 4;

	private string CaminhoDoMundo => System.IO.Path.Combine(_store?.Pasta ?? ".", "mundo.json");

	private void CarregarTech()
	{
		const string cj = "res://Assets/Data/construcoes.json";
		if (Godot.FileAccess.FileExists(cj))
		{
			_obras = CatalogoDeObras.Parse(Godot.FileAccess.GetFileAsString(cj));

			// TODA CONSTRUCAO PASSA A CABER NA MOCHILA. O catalogo de itens tem sete linhas escritas
			// a mao (as que tem nutricao, pilha ou acao propria); as outras noventa e duas nascem
			// daqui. Ver `CatalogoDeItens.Get`.
			Jandirus.Core.Items.CatalogoDeItens.Obras = _obras;

			GD.Print($"[server] construcoes: {_obras.Total} no catalogo");

			// ============================ DENSA E SEM ARTE E UMA PAREDE INVISIVEL ============================
			// O servidor bloqueia a celula pelo campo `Densa` (`AplicarColisaoDasObras`) e o cliente
			// desenha pelo campo `Arte`. Quando o segundo esta vazio e o primeiro nao, o jogador ganha
			// um obstaculo que nao existe na tela -- e ate hoje isso passava calado, porque cada lado
			// so olha o campo dele.
			//
			// O CLIENTE JA TEM RESERVA (`ObraDesenhada._Draw` pinta um retangulo cinza quando o
			// SpriteFrames nao carrega), entao o jogador vai VER alguma coisa. O que faltava era
			// alguem DIZER que aquela coisa cinza e um buraco de asset, e nao arte de proposito.
			// Ver tambem o relatorio de "PAREDE INVISIVEL" do `MapConverter`.
			//
			// A PERGUNTA E DO CORE (`CatalogoDeObras.SemDesenho`), e nao um `if` escrito aqui: a
			// bancada `--cidadeteste` cobra a MESMA lista, e duas copias da regra divergiriam no
			// primeiro campo que alguem acrescentasse. Ver o cabecalho daquele metodo.
			AvisarDasMudas(_obras);
		}
		else GD.PushWarning("[server] sem construcoes.json -- rode o AssetPipeline (comando 'tech')");

		CarregarMundoDeDisco(CaminhoDoMundo);

		// AS OBRAS DO DISCO TAMBEM BLOQUEIAM. Sem esta volta, uma bancada erguida ontem so viraria
		// parede quando alguem construisse OUTRA coisa na mesma zona -- e o `MandarObras` fosse
		// chamado por acaso. Bloquear na carga e o que faz o mundo salvo valer desde o boot.
		int densas = 0;
		foreach (ZoneKey zona in _noChao.Select(o => o.Zona).Distinct())
		{
			AplicarColisaoDasObras(zona);
			densas += _noChao.Count(o => o.Zona.Equals(zona) && _obras?.Get(o.Tipo) is { Densa: true });
		}
		if (densas > 0) GD.Print($"[server] construcoes que bloqueiam: {densas}");

		CarregarObjetosDoMapa();
	}

	/// <summary>
	/// GRITA POR CADA CONSTRUCAO QUE BLOQUEIA SEM TER ARTE. Devolve quantas eram.
	///
	/// ============================ POR QUE ISTO E UM METODO, E COM RETORNO ============================
	/// Porque um `foreach` com `PushError` dentro da carga nao pode ser PROVADO. A bancada
	/// `--cidadeteste` precisa injetar uma construcao muda e conferir que o alarme disparou, e a
	/// unica forma honesta de conferir isso e o alarme de PRODUCAO responder -- se a bancada tivesse
	/// o proprio laco, ela mediria a si mesma, e o dia em que alguem apagasse este trecho da carga
	/// ela continuaria verde. O numero devolvido e a resposta que a bancada le.
	///
	/// Foi assim que quatro paredes invisiveis nasceram neste port sem ninguem notar: nao faltou
	/// codigo, faltou alguem PERGUNTAR em voz alta.
	/// ==============================================================================================
	/// </summary>
	private int AvisarDasMudas(CatalogoDeObras cat)
	{
		int n = 0;
		foreach (Construcao c in cat.SemDesenho())
		{
			n++;
			GD.PushError($"[server] construcao '{c.Id}' BLOQUEIA e nao tem arte -- ela vira parede "
						 + "invisivel. Converta o .dmi dela e rode o AssetPipeline ('tech').");
		}
		return n;
	}

	/// <summary>
	/// AS MAQUINAS QUE O MAPA JA TRAZ viram construcoes de pe.
	///
	/// ============================ O QUE ISTO CONSERTA ============================
	/// Banco, bancada de pesquisa, sala de gravidade e os laboratorios estao espalhados pelos
	/// `.dmm` do original desde sempre -- e eram celula de tilemap: desenho parado, sem estado e
	/// sem resposta. Um jogador podia ERGUER uma bancada ao lado de outra que ja estava la e so a
	/// dele funcionava. As duas eram a mesma maquina; uma era um node e a outra era pintura.
	///
	/// Registrando-as aqui, elas entram inteiras no sistema que ja existe: o mesmo pacote as
	/// desenha, a mesma colisao as bloqueia, o mesmo alcance de uso as alcanca e os mesmos
	/// comandos as usam. Estudar numa bancada do mapa passa a funcionar sem uma linha nova no
	/// caminho de estudar.
	/// =============================================================================
	///
	/// APARAFUSADAS de saida: `estudar` exige bancada aparafusada, e mobilia de mapa nao tem dono
	/// pra aparafusar. Ela sempre esteve la -- e o mesmo que dizer que ela e parte do lugar.
	/// </summary>
	private void CarregarObjetosDoMapa()
	{
		if (_catalogo == null) return;

		int n = 0, semArte = 0;
		foreach (ZoneEntry e in _catalogo.Todas)
		{
			if (e.Objetos.Length == 0 || !Godot.FileAccess.FileExists(e.Objetos)) continue;

			foreach (ObjetoDoMapa o in ObjetosDoMapa.Parse(Godot.FileAccess.GetFileAsString(e.Objetos)))
			{
				if (_obras?.Get(o.Id) == null) { semArte++; continue; }

				// A CELULA VIRA PIXEL NO MEIO DELA. O conversor grava a celula; a obra guarda a
				// posicao do CORPO que a ergueu, e `CatalogoDeObras.Celula` desfaz isso somando o
				// deslocamento dos pes. Aqui a conta e a inversa, e tem que ser a mesma -- senao a
				// maquina desenha uma celula acima de onde ela esta no mapa.
				const int t = ZoneCollision.TileSize;
				var maquina = new Obra
				{
					Id = _proximaObraId++,
					Tipo = o.Id,
					X = o.X * t + t / 2f,
					Y = o.Y * t + t / 2f - MoveRules.FeetOffsetY,
					DonoNome = "",
					Aparafusada = true,
					DoMapa = true,
					ErguidaEm = 0,
				};

				// PRE-FEITA POR CONSTRUCAO: o `ZoneEntry` e uma zona de ARQUIVO (convertida de .dmm).
				// Nao ha `.objetos` de mundo sorteado -- o gerador nao escreve maquina nenhuma.
				maquina.PorZona(ZoneKey.Premade(e.Zona));
				_noChao.Add(maquina);
				n++;
			}

			AplicarColisaoDasObras(ZoneKey.Premade(e.Zona));
		}

		if (n > 0) GD.Print($"[server] maquinas do mapa: {n} viraram construcoes de pe");
		if (semArte > 0)
			GD.PushWarning($"[server] {semArte} maquina(s) do mapa sem entrada no catalogo -- "
						   + "rode o AssetPipeline ('tech' e depois 'maps')");
	}

	/// <summary>
	/// LE O `mundo.json`. Separada do <see cref="CarregarTech"/> pelo caminho, e nao por gosto: a
	/// bancada (`--obrateste`) precisa exercitar ESTA leitura -- com a migracao dentro -- contra um
	/// arquivo proprio, sem tocar no mundo de verdade. Uma copia da leitura na bancada testaria a
	/// copia.
	/// </summary>
	private void CarregarMundoDeDisco(string caminho)
	{
		try
		{
			if (!System.IO.File.Exists(caminho)) return;

			List<Obra>? l = JsonSerializer.Deserialize<List<Obra>>(
				System.IO.File.ReadAllText(caminho), new JsonSerializerOptions { IncludeFields = true });
			if (l != null) _noChao.AddRange(l);
			_proximaObraId = _noChao.Count > 0 ? _noChao.Max(o => o.Id) + 1 : 1;
			GD.Print($"[server] mundo: {_noChao.Count} construcoes de pe");

			// A MIGRACAO SE ANUNCIA (ver `Obra.ZonaDoDiscoAntigo`). Converter em silencio seria
			// pedir pra alguem, daqui a um mes, descobrir pelo bug que o formato mudou.
			int convertidas = _noChao.Count(o => o.Migrada);
			if (convertidas > 0)
				GD.Print($"[server] mundo: {convertidas} construcao(oes) convertida(s) do formato antigo "
						 + "(zona por nome -> zona pre-feita). Elas voltam pro mesmo lugar de sempre; "
						 + "a partir de agora as de planeta gerado tambem voltam pro delas.");
		}
		catch (Exception e) { GD.PushWarning($"[server] mundo.json ilegivel: {e.Message}"); }
	}

	private void GravarMundo() => GravarMundoEm(CaminhoDoMundo);

	/// <summary>Ver <see cref="CarregarMundoDeDisco"/> -- a irma, e pelo mesmo motivo.</summary>
	private void GravarMundoEm(string caminho)
	{
		try
		{
			// SO O QUE ALGUEM ERGUEU. A mobilia do mapa nasce do `.objetos` a cada boot; grava-la
			// aqui duplicaria cada bancada a cada reinicio. Ver `Obra.DoMapa`.
			System.IO.File.WriteAllText(caminho,
				JsonSerializer.Serialize(_noChao.Where(o => !o.DoMapa).ToList(),
										 new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
		}
		catch (Exception e) { GD.PushWarning($"[server] nao gravei o mundo: {e.Message}"); }
	}

	// =====================================================================
	// CONSTRUIR
	// =====================================================================
	/// <summary>
	/// ERGUE UMA CONSTRUCAO onde a pessoa esta.
	///
	/// A POSICAO E A DO SERVIDOR, nao a que o cliente mandou. Deixar o cliente escolher onde
	/// construir seria deixar ele construir do outro lado do mapa, dentro de parede, ou dentro da
	/// casa de outro. Constroi-se onde se esta -- e onde se esta o servidor ja sabe.
	/// </summary>
	private void Construir(ServerPlayer pl, string tipo)
	{
		if (_obras == null) { Avisar(pl, "o servidor esta sem catalogo de construcoes."); return; }
		Construcao? c = _obras.Get(tipo);
		if (c == null) { Avisar(pl, "isso nao existe."); return; }

		RecusaObra r = CatalogoDeObras.Permitida(c, pl.Ficha, pl.Race);
		if (r != RecusaObra.Pode) { Avisar(pl, MotivoObra(r, c, pl)); return; }

		// ============================ FABRICAR NAO E ERGUER ============================
		// Ate aqui, comprar uma maquina de gravidade a plantava NA HORA, embaixo dos pes de quem
		// comprou. Nao havia como escolher onde -- e "onde" e metade da decisao quando a coisa
		// bloqueia passagem e custa meio milhao.
		//
		// Agora a bancada FABRICA e a mochila carrega; assentar e um segundo gesto, com o fantasma
		// no mouse (ver `Posicionar`). E tambem o que o original faz: o `Click()` do creatable cria
		// um `/obj/items/...` que vai pro `contents` do jogador.
		//
		// A COBRANCA SO ACONTECE SE COUBER. Tirar o zeni antes de descobrir que a mochila esta
		// cheia seria vender um item que nao foi entregue.
		if (pl.Mochila.Cheio)
		{
			Avisar(pl, $"sua mochila está cheia ({Jandirus.Core.Items.Inventario.Slots} espaços).");
			return;
		}

		pl.Ficha.Zeni -= c.Custo;
		Guardar(pl, c.Id);
		GD.Print($"[server] {pl.Name} fabricou {c.Nome} ({c.Custo:N0}z)");
		Avisar(pl, Jandirus.Core.Items.CatalogoDeItens.EhConstrucao(c.Id)
			? $"você fabrica {c.Nome}. Está na mochila -- use \"posicionar\" pra assentar no chão."
			: $"você fabrica {c.Nome}. Está na sua mochila.");
		MandarCatalogoDeObras(pl);
	}

	/// <summary>
	/// ASSENTA NO CHAO uma construcao que estava na mochila.
	///
	/// ============================ O PONTO E DO CLIENTE, E POR ISSO E CONFERIDO ============================
	/// Todo o resto do jogo constroi "onde eu estou", que dispensa validacao. Aqui o jogador aponta
	/// com o mouse, e um ponto que vem do cliente e um ponto que um cliente mexido escolhe: dentro
	/// de parede, dentro da casa de outro, do outro lado do mapa.
	///
	/// As tres guardas sao as mesmas que o construir de antes ja tinha, mais uma nova (o ALCANCE),
	/// que so faz sentido agora que o ponto deixou de ser os proprios pes.
	/// =======================================================================================================
	/// </summary>
	private void Posicionar(ServerPlayer pl, string arg)
	{
		// "<id>/<x>/<y>" -- o canal de verbos tem um argumento so.
		string[] p = arg.Split('/');
		if (p.Length < 3 || !float.TryParse(p[1], out float x) || !float.TryParse(p[2], out float y))
		{ Avisar(pl, "ponto inválido."); return; }

		Construcao? c = _obras?.Get(p[0]);
		if (c == null) { Avisar(pl, "isso não existe."); return; }
		if (pl.Mochila.Quantos(c.Id) <= 0) { Avisar(pl, $"você não tem {c.Nome}."); return; }

		// PERTO DE MIM. Sem isto da pra plantar uma bancada do outro lado do planeta.
		if (Math.Abs(x - pl.Pos.X) > AlcanceDePosicionar || Math.Abs(y - pl.Pos.Y) > AlcanceDePosicionar)
		{ Avisar(pl, "longe demais -- chegue mais perto do lugar."); return; }

		// ============================ NAO SE CONSTROI DENTRO DE UMA NAVE -- E O MOTIVO MUDOU ============================
		// A recusa NASCEU de um defeito da chave: a `Obra` guardava a zona como nome puro, o interior de
		// toda nave se chama "Nave", e uma bancada erguida na #7 apareceria dentro da #8 e de todas as
		// outras. **Esse defeito acabou** -- o interior e `ZoneKey.Interior("Nave", id da nave)` e agora
		// a obra guarda tipo, nome e seed, entao a #7 e a #8 sao zonas distintas.
		//
		// A RECUSA FICA POR OUTRA RAZAO, e ela e do CICLO DE VIDA e nao da chave: o interior existe
		// enquanto a nave existe. Uma nave se recolhe pra mochila e se destroi (`GameServer.Estrago`),
		// e o `mundo.json` nao sabe disso -- a bancada ficaria de pe pra sempre numa zona onde ninguem
		// mais entra, ocupando id e lista, sem nenhum caminho pra recolhe-la. Guardar obra dentro de
		// coisa que morre pede que a morte da nave leve as obras junto, e isso e trabalho da nave.
		// =============================================================================================================
		if (Jandirus.Core.Tech.NaveGrande.EhInterior(pl.Zone, out _))
		{
			Avisar(pl, "não dá pra construir dentro de uma nave: se ela for destruída, o que estiver "
					   + "aqui dentro fica preso num lugar onde ninguém mais entra.");
			return;
		}

		// NAO EMPILHA: duas construcoes no mesmo lugar viram uma so na tela e as duas respondem ao
		// mesmo clique. Meio tile de folga e o bastante pra nao encavalar.
		//
		// AS NAVES CONTAM NA MESMA CONFERENCIA. Elas moram noutra lista (ver `GameServer.Nave.cs`),
		// mas ocupam o mesmo chao -- e "ja tem coisa demais neste ponto" e uma pergunta sobre o
		// CHAO, nao sobre a lista em que a coisa esta guardada.
		if (_noChao.Any(o => o.Zona.Equals(pl.Zone)
							 && Math.Abs(o.X - x) < 24 && Math.Abs(o.Y - y) < 24)
			|| NavesParadasEm(pl.Zone).Any(n => Math.Abs(n.X - x) < 24 && Math.Abs(n.Y - y) < 24))
		{ Avisar(pl, "já tem coisa demais neste ponto."); return; }

		// NEM DENTRO DE PAREDE. A construcao densa vira parede, e uma parede dentro de outra e um
		// buraco no mapa que ninguem consegue desfazer sem admin.
		//
		// `MapaDaZonaOuCatalogo` E NAO `_catalogo?.Get`: o catalogo so conhece mapa de ARQUIVO, e
		// escrever a busca na mao aqui era um dos 18 lugares que o cabecalho daquele metodo cita --
		// nenhum deles funciona em planeta gerado. Numa nave isso deixaria de ser detalhe: assentar
		// uma no meio de uma montanha de mundo sorteado nao seria nem recusado.
		if (MapaDaZonaOuCatalogo(pl.Zone) is { } mapa && MoveRules.Occupied(mapa, new Vec2(x, y)))
		{ Avisar(pl, "não dá pra assentar dentro de uma parede."); return; }

		pl.Mochila.Tirar(c.Id);
		MandarMochila(pl);

		// ============================ A NAVE SEGUE POR OUTRA PORTA ============================
		// Ela passa pelas MESMAS guardas acima (alcance, nao-empilhar, nao dentro de parede) porque
		// elas sao sobre o chao e valem pra qualquer coisa que se assente. O que muda e o RESTO da
		// vida dela: ela se pilota, viaja entre mundos, tem casco, senha e piloto -- ver o cabecalho
		// de `Nave`. A chave de zona deixou de ser o motivo da separacao: as duas guardam a mesma.
		if (Naves.EhNave(c.Id)) { AssentarNave(pl, c, x, y); return; }

		var obra = new Obra
		{
			Id = _proximaObraId++,
			Tipo = c.Id,
			X = x,
			Y = y,
			DonoConta = pl.Conta,
			DonoNome = pl.Name,
			ErguidaEm = NowMs(),

			// A ARMADURA NASCE DO BP DE QUEM ERGUEU -- `built.maxarmor = intBPcap`
			// (`buildable.dm:414`). E o que faz a bancada de um lutador forte ser dificil de
			// derrubar e a de um novato nao ser: quem quiser quebrar precisa bater no nivel dela.
			//
			// A MOBILIA DO MAPA NAO PASSA POR AQUI e fica no padrao 1, de proposito -- ela cai
			// facil e volta no proximo boot. Esta vai pro disco e nao volta.
			ArmaduraMax = Math.Max(Jandirus.Core.Combat.Armadura.Padrao, pl.Ficha.expressedBP),
			Armadura = Math.Max(Jandirus.Core.Combat.Armadura.Padrao, pl.Ficha.expressedBP),
		};

		// A ZONA INTEIRA, e nao o nome dela: e o que faz esta bancada voltar do disco no MESMO mundo
		// em que ela foi erguida, inclusive num sorteado homonimo de outro. Ver `Obra.ZonaTipo`.
		obra.PorZona(pl.Zone);
		_noChao.Add(obra);
		GravarMundo();

		GD.Print($"[server] {pl.Name} assentou {c.Nome} (#{obra.Id}) em {obra.Zona} @ ({x:0},{y:0})");
		Avisar(pl, $"você assenta {c.Nome} no chão.");
		MandarObras(pl.Zone);
	}

	/// <summary>
	/// PEGA DE VOLTA uma construcao que voce ergueu.
	///
	/// SO O DONO, e so o que NAO veio do mapa: a mobilia que sempre esteve la (banco, bancada das
	/// cidades) nao e de ninguem, e deixar alguem guardar o banco da Terra na mochila seria um jeito
	/// de apagar o mapa. Ver `Obra.DoMapa`.
	/// </summary>
	private void PegarObra(ServerPlayer pl)
	{
		Obra? o = ObraPerto(pl);
		if (o == null) { Avisar(pl, "não há nada por perto pra pegar."); return; }

		if (o.DoMapa) { Avisar(pl, "isto faz parte do lugar -- não é seu pra levar."); return; }
		if (o.DonoConta.Length > 0 && !string.Equals(o.DonoConta, pl.Conta, StringComparison.OrdinalIgnoreCase))
		{ Avisar(pl, $"{NomeDaObra(o)} é de {o.DonoNome}."); return; }
		if (pl.Mochila.Cheio) { Avisar(pl, "sua mochila está cheia."); return; }

		_noChao.Remove(o);
		GravarMundo();
		Guardar(pl, o.Tipo);

		// A COLISAO E REFEITA PELA LISTA INTEIRA (ver `AplicarColisaoDasObras`): tirar a obra sem
		// isto deixaria a parede dela no lugar, e o jogador levaria correcao num ponto vazio.
		AplicarColisaoDasObras(pl.Zone);
		Avisar(pl, $"você recolhe {NomeDaObra(o)}.");
		MandarObras(pl.Zone);
	}

	/// <summary>A que distancia da pra assentar uma construcao. Tres tiles -- o braco, e nao a vista.</summary>
	private const float AlcanceDePosicionar = 96f;

	private static string MotivoObra(RecusaObra r, Construcao c, ServerPlayer pl) => r switch
	{
		RecusaObra.SemTech => $"{c.Nome} pede {c.Tech:0} de tecnologia -- voce tem {pl.Ficha.techskill:0}.",
		RecusaObra.SemZeni => $"{c.Nome} custa {c.Custo:N0} zeni -- voce tem {pl.Ficha.Zeni:N0}.",
		RecusaObra.RacaErrada => $"{c.Nome} nao e coisa de {pl.Race}.",
		_ => "nao deu pra construir.",
	};

	/// <summary>
	/// APARAFUSA (o `Bolt` do original): a construcao para de ser carregavel e passa a poder ser
	/// USADA. E o que separa uma caixa no chao de uma instalacao.
	/// </summary>
	private void Aparafusar(ServerPlayer pl)
	{
		Obra? o = ObraPerto(pl);
		if (o == null) { Avisar(pl, "nao ha nada aqui pra aparafusar."); return; }
		if (o.DonoConta != pl.Conta) { Avisar(pl, "isso nao e seu."); return; }

		o.Aparafusada = !o.Aparafusada;
		GravarMundo();
		Avisar(pl, o.Aparafusada
			? $"voce aparafusa {NomeDaObra(o)} no chao."
			: $"voce solta {NomeDaObra(o)} do chao.");
		MandarObras(pl.Zone);
	}

	private Obra? ObraPerto(ServerPlayer pl) => _noChao
		.Where(o => o.Zona.Equals(pl.Zone))
		.OrderBy(o => (o.X - pl.Pos.X) * (o.X - pl.Pos.X) + (o.Y - pl.Pos.Y) * (o.Y - pl.Pos.Y))
		.FirstOrDefault(o => Math.Abs(o.X - pl.Pos.X) <= AlcanceDeUso && Math.Abs(o.Y - pl.Pos.Y) <= AlcanceDeUso);

	private string NomeDaObra(Obra o) => NomeDoTipo(o.Tipo);

	/// <summary>
	/// O NOME DE UM TIPO DO CATALOGO. Vale pra tudo que aparece no pacote de construcoes -- obra,
	/// nave parada e mobilia do interior --, e por isso ele e por TIPO e nao por `Obra`: as outras
	/// duas nao sao `Obra` nenhuma.
	/// </summary>
	private string NomeDoTipo(string tipo) => _obras?.Get(tipo)?.Nome ?? tipo;

	// =====================================================================
	// ESTUDAR -- o que faz o techskill subir
	// =====================================================================
	/// <summary>
	/// ESTUDA numa Research Station. E o `Experiment()` do original, e a regra que importa dele e
	/// esta: <b>so se ganha tecnologia numa estacao</b> ("Research Stations are the only really
	/// good way of getting Tech XP at all"). Estudar sozinho no campo nao existe.
	///
	/// Sem material pra sacrificar (o port ainda nao tem itens), o custo e TEMPO: a estacao rende
	/// por segundo enquanto a pessoa fica nela. O ganho passa pelo `techmod`, como la.
	/// </summary>
	private void TickDoEstudo()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			if (!pl.Estudando || pl.Ficha.KO || pl.Ficha.dead) continue;

			Obra? est = _noChao.FirstOrDefault(o => o.Tipo == "Research_Station" && o.Aparafusada
				&& o.Zona.Equals(pl.Zone)
				&& Math.Abs(o.X - pl.Pos.X) <= AlcanceDeUso && Math.Abs(o.Y - pl.Pos.Y) <= AlcanceDeUso);
			if (est == null)
			{
				pl.Estudando = false;
				Avisar(pl, "voce precisa de uma Research Station aparafusada por perto pra estudar.");
				continue;
			}

			int subiu = pl.Ficha.Estudar(XpDeEstudoPorSegundo);

			// O PAINEL TEM QUE ANDAR ENQUANTO SE ESTUDA. Um nivel leva mais de vinte minutos no
			// topo da curva; sem a barra de XP mexendo, quem estuda nao tem NENHUM sinal de que a
			// coisa esta funcionando -- e para. O pacote so sai pra quem esta debrucado na
			// bancada, entao e um por segundo por estudante, nao por jogador.
			MandarCatalogoDeObras(pl);
			if (subiu <= 0) continue;

			Avisar(pl, $"seu estudo rende: tecnologia agora e {pl.Ficha.techskill:0}.");
			GD.Print($"[server] {pl.Name}: tech {pl.Ficha.techskill:0}");
			pl.SigAtributos = "";
		}
	}

	/// <summary>
	/// QUANTO XP UM SEGUNDO DE ESTUDO VALE.
	///
	/// Calibrado pra que os 70 pontos do laboratorio de DNA custem algumas HORAS, nao alguns
	/// minutos: com a curva de <see cref="Jandirus.Core.Stats.Fighter.TechXpDoProximo"/>, 15 por
	/// segundo poe o nivel 70 por volta de 4 h de estudo continuo. E o preco certo pra uma coisa
	/// que transforma um humano comum num androide.
	/// </summary>
	private const double XpDeEstudoPorSegundo = 15;

	// =====================================================================
	// OS LABORATORIOS
	// =====================================================================
	/// <summary>
	/// VIRAR ANDROIDE. O `Android Lab` do original: um humano com tecnologia e dinheiro reconstroi
	/// o proprio corpo por dentro.
	///
	/// O QUE NAO MUDA: icone, roupas e skills. A maquina mexe no que ha DENTRO -- e por isso o
	/// personagem continua sendo o mesmo personagem, com a mesma cara. Trocar a aparencia junto
	/// faria parecer que o jogador perdeu o personagem em vez de transformar.
	///
	/// O BP VIRA OU SOMA (`dnl_apply_android_bp`): quem estava abaixo do piso e alcado ate ele;
	/// quem ja passou GANHA o valor por cima. Sem essa segunda regra, um humano forte ficaria mais
	/// fraco ao se tornar androide, e ninguem faria isso depois de treinar.
	/// </summary>
	private void VirarAndroide(ServerPlayer pl, bool infinito)
	{
		if (ObraPerto(pl) is not { Aparafusada: true, Lab: 1 })
		{ Avisar(pl, "voce precisa estar dentro de um Android Lab."); return; }
		if (pl.Race != "Human") { Avisar(pl, "essa maquina foi feita pra fisiologia humana."); return; }
		if (pl.Ficha.techskill < TechDoLaboratorio)
		{
			Avisar(pl, $"instalar isso pede {TechDoLaboratorio:0} de tecnologia -- voce tem {pl.Ficha.techskill:0}.");
			return;
		}

		double custo = infinito ? CustoAndroideInfinito : CustoAndroideAbsorcao;
		if (pl.Ficha.Zeni < custo) { Avisar(pl, $"a conversao custa {custo:N0} zeni."); return; }

		double piso = infinito ? BpAndroideInfinito : BpAndroideAbsorcao;
		pl.Ficha.Zeni -= custo;
		pl.Ficha.BP = pl.Ficha.BP < piso ? piso : pl.Ficha.BP + piso;
		pl.Race = "Android";
		pl.Ficha.AndroideInfinito = infinito;
		pl.Ficha.AndroideAbsorcao = !infinito;
		pl.Ficha.Statify();
		pl.SigAtributos = "";

		GD.Print($"[server] {pl.Name} virou androide ({(infinito ? "energia infinita" : "absorcao")}), BP {pl.Ficha.BP:N0}");
		Avisar(pl, infinito
			? "a maquina se fecha sobre voce. Quando abre, voce nao sente mais fome nem cansaco -- a energia simplesmente NAO ACABA."
			: "a maquina se fecha sobre voce. Quando abre, o Ki alheio parece... comestivel.");
		Persistir(pl);
	}

	/// <summary>
	/// COLHE DNA de alguem NOCAUTEADO. O `Extract DNA` do original.
	///
	/// So de nocauteado, e isso e a mecanica inteira: pra fazer um bio-androide e preciso DERRUBAR
	/// gente forte primeiro. O cientista nao escapa de lutar -- ele escapa de lutar SOZINHO.
	///
	/// ============================ E "GENTE" AQUI E `EhJogador`, SENAO ELE ESCAPA ============================
	/// A vitima era qualquer corpo caido, e com o povoamento ligado isso apagava a mecanica inteira:
	///
	///   * **cidadao e gratis e infinito** -- a `Manutencao` repoe a populacao ate a meta a cada 5 min,
	///     e a raca sai do berco do planeta: o tanque de 6 amostras se enche visitando 6 planetas e
	///     nocauteando um habitante em cada, sem tocar num jogador;
	///   * **o `MaiorBp` pegava o numero do CHEFE** -- e o filtro aceita `KO`, nao exige matar: bastava
	///     nocautear o Freeza de Vegeta (530 mil) ou um androide (100 milhoes), colher, e deixa-lo de
	///     pe pra saga terminar normalmente.
	/// ==================================================================================================
	/// </summary>
	private void ColherDna(ServerPlayer pl)
	{
		Obra? lab = LabDeBio(pl);
		if (lab == null) { Avisar(pl, "voce precisa de um Bio-Android Lab aparafusado por perto."); return; }

		ServerPlayer? vitima = _players.Values
			.Where(o => o != pl && EhJogador(o) && o.Zone.Equals(pl.Zone) && (o.Ficha.KO || o.Ficha.dead))
			.OrderBy(o => Vec2.Distance(o.Pos, pl.Pos))
			.FirstOrDefault(o => Vec2.Distance(o.Pos, pl.Pos) <= AlcanceDeUso);

		if (vitima == null) { Avisar(pl, "nao ha ninguem caido ao seu alcance."); return; }

		lab.Fornada ??= new Gestacao { DonoConta = pl.Conta };
		if (lab.Fornada.PrometidaEm > 0) { Avisar(pl, "a gestacao ja comecou -- nao da pra acrescentar DNA."); return; }
		if (lab.Fornada.Dna.Count >= BioMaxDna) { Avisar(pl, $"o tanque so comporta {BioMaxDna} amostras."); return; }

		lab.Fornada.Dna.Add(vitima.Race);
		lab.Fornada.MaiorBp = Math.Max(lab.Fornada.MaiorBp, vitima.Ficha.BP);
		GravarMundo();

		Avisar(pl, $"voce colhe DNA de {vitima.Name} ({vitima.Race}). "
				   + $"Amostras: {lab.Fornada.Dna.Count}/{BioMaxDna}.");
		Avisar(vitima, "alguem enfia uma agulha em voce enquanto voce esta caido.");
	}

	/// <summary>
	/// COMECA A GESTACAO. Um mes in-game, e destruir o laboratorio cancela.
	/// </summary>
	private void GestarBio(ServerPlayer pl)
	{
		Obra? lab = LabDeBio(pl);
		if (lab?.Fornada == null || lab.Fornada.Dna.Count == 0)
		{ Avisar(pl, "o tanque esta vazio -- colha DNA primeiro."); return; }
		if (lab.Fornada.PrometidaEm > 0)
		{
			double falta = (lab.Fornada.PrometidaEm - NowMs()) / 1000.0;
			Avisar(pl, $"ja esta gestando. Faltam {falta / 60:0.#} minutos.");
			return;
		}

		lab.Fornada.PrometidaEm = NowMs() + (long)(GestacaoSegundosReais * 1000);
		GravarMundo();
		GD.Print($"[server] {pl.Name} iniciou gestacao de bio-androide ({string.Join("+", lab.Fornada.Dna)})");
		Avisar(pl, $"o tanque se fecha. A criatura leva um mes pra ficar pronta "
				   + $"({GestacaoSegundosReais / 60:0} minutos reais). Nao deixe destruirem o laboratorio.");
	}

	private Obra? LabDeBio(ServerPlayer pl)
	{
		Obra? o = ObraPerto(pl);
		return o is { Aparafusada: true, Lab: 2 } ? o : null;
	}

	/// <summary>
	/// INSTALA UM LABORATORIO no mainframe aparafusado. E o passo do meio que o original tem e
	/// que e facil achar burocratico -- ate perceber que ele e a ESCOLHA: o mesmo meio milhao de
	/// zeni vira "eu viro maquina" ou "eu construo uma". Um mainframe so faz uma das duas coisas.
	/// </summary>
	private void InstalarLab(ServerPlayer pl, int qual)
	{
		Obra? o = ObraPerto(pl);
		if (o is not { Tipo: "Android_Creation_Mainframe" })
		{ Avisar(pl, "voce precisa estar perto de um mainframe de criacao de androides."); return; }
		if (!o.Aparafusada) { Avisar(pl, "aparafuse o mainframe no chao antes de instalar."); return; }
		if (o.DonoConta != pl.Conta) { Avisar(pl, "isso nao e seu."); return; }
		if (o.Lab != 0) { Avisar(pl, "este mainframe ja foi convertido -- ergue outro."); return; }
		if (pl.Race != "Human") { Avisar(pl, "a instalacao pede um humano."); return; }
		if (pl.Ficha.techskill < TechDoLaboratorio)
		{
			Avisar(pl, $"instalar pede {TechDoLaboratorio:0} de tecnologia -- voce tem {pl.Ficha.techskill:0}.");
			return;
		}

		o.Lab = qual;
		GravarMundo();
		Avisar(pl, qual == 1
			? "o mainframe vira um Android Lab. Agora da pra entrar nele."
			: "o mainframe vira um Bio-Android Lab. Agora falta o DNA.");
		MandarObras(pl.Zone);
	}

	/// <summary>
	/// A GESTACAO TERMINOU. No original a criatura MATA o criador e o jogador passa a jogar com
	/// ela -- e essa e a parte que da peso ao sistema inteiro: nao e um pet, e uma sucessao.
	///
	/// O PORT AINDA NAO TROCA O PERSONAGEM DA CONTA (a criacao de personagem passa pela tela de
	/// slots, e forcar um slot por fora dela abriria um segundo caminho de criacao que ninguem
	/// mais valida). Entao aqui a criatura NASCE, mata o criador e fica anotada; a troca de corpo
	/// e o passo que falta, e esta escrito em vez de fingido.
	/// </summary>
	private void TickDaGestacao()
	{
		long agora = NowMs();
		foreach (Obra lab in _noChao.ToList())
		{
			if (lab.Fornada is not { PrometidaEm: > 0 } g || agora < g.PrometidaEm) continue;

			double bp = g.MaiorBp * BioFracaoDoDoador;
			ServerPlayer? criador = _players.Values.FirstOrDefault(p => p.Conta == g.DonoConta);

			GD.Print($"[server] bio-androide pronto no lab #{lab.Id}: DNA {string.Join("+", g.Dna)}, BP {bp:N0}");
			if (criador != null)
			{
				Avisar(criador, "o tanque se abre. A coisa la dentro olha pra voce -- e ela sabe exatamente "
								+ "de quem ela veio.");
				// A CRIATURA MATA O CRIADOR. E o original inteiro, e e o que fecha o ciclo: quem
				// faz um bio-androide esta assinando a propria sentenca, e sabe disso desde a
				// primeira agulha. Sem isto o sistema vira "ganhe um pet muito forte".
				//
				// **PELA PORTA, E NAO NA MAO.** Isto escrevia `dead = true` direto na ficha e pulava o
				// `CombatState.Morrer()` -- e com ele o gancho `AoMorrer`, que e quem arma o relogio da
				// morte e manda pro Outro Mundo. O criador morria e ficava caido pra sempre, sem prazo.
				// `ignorarSeguro: true` porque a criatura nao negocia com a Aura of Destruction: o
				// original nao da escapatoria nenhuma a quem a gestou.
				criador.Combate?.Morrer(ignorarSeguro: true);
			}

			lab.Fornada = null;
			lab.Vida = 0;   // o tanque se rompe ao nascer
			_noChao.Remove(lab);
			GravarMundo();
			MandarObras(lab.Zona);
		}
	}

	// =====================================================================
	// REDE
	// =====================================================================
	/// <summary>Manda as construcoes de uma zona pra quem esta nela. So quando muda.</summary>
	private void MandarObras(ZoneKey zona)
	{
		List<Obra> daZona = [.. _noChao.Where(o => o.Zona.Equals(zona))];

		// ============================ AS NAVES PARADAS VAO NESTE MESMO PACOTE ============================
		// Nao por economia de opcode: porque uma nave POUSADA e, pro cliente, exatamente o que uma
		// construcao e -- um objeto no chao, com sprite do catalogo, que entra no Y-sort, bloqueia
		// passagem e responde a tecla E. Um opcode proprio duplicaria o desenho, a colisao e o menu
		// pra dizer a mesma coisa.
		//
		// O QUE NAO E IGUAL FICA DE FORA DAQUI: a nave PILOTADA nao entra na lista (ela deixou de
		// estar no chao), e quem a desenha e o corpo do piloto pelo bit `Pilotando` do snapshot.
		// Mandar a lista inteira 30x/s pra acompanhar um veiculo seria o caminho errado -- este
		// pacote e confiavel e so sai quando algo MUDA.
		//
		// O ID VAI NEGATIVO porque as duas listas tem numeracao propria: sem isso a nave #3 e a
		// bancada #3 seriam o mesmo node no cliente (`Name = "Obra" + id`) e uma apagaria a outra.
		// ============================================================================================
		List<Nave> navesDaZona = NavesParadasEm(zona);

		// ============================ E A MOBILIA DO INTERIOR DE NAVE VAI JUNTO ============================
		// Pelo MESMO argumento das naves paradas, um degrau adiante: pro cliente, o console da ponte e
		// a plataforma de saida sao construcoes -- sprite ancorado numa celula, Y-sort, densidade e
		// menu da tecla E. Mandando-as por aqui elas ganham as quatro coisas sem uma linha nova no
		// cliente, e o menu delas sai do mesmo `Interacoes` que o banco e a macieira usam.
		//
		// ELAS NAO SAO GUARDADAS EM LUGAR NENHUM: sao derivadas da planta na hora (ver
		// `MobiliaDoInterior`). Nao ha lista pra sanear e nao ha como uma nave perder a ponte dela.
		// ==============================================================================================
		var mobilia = MobiliaDoInterior(zona).ToList();

		var w = Protocol.Begin(Protocol.S2C.Construcoes);
		w.Put((ushort)(daZona.Count + navesDaZona.Count + mobilia.Count));
		foreach ((int id, Jandirus.Core.Tech.PecaDoInterior p) in mobilia)
		{
			w.Put(id);
			w.Put(p.Tipo);
			// O NOME VIAJA JUNTO, como a arte -- e pelo mesmo motivo. O catalogo do cliente vem do
			// `Ofertas`, que esconde mobilia de mapa (custo negativo): sem esta linha o menu da tecla
			// E chamava o console da ponte de "Ship Control" e o banco de "Bank", em ingles.
			w.Put(NomeDoTipo(p.Tipo));
			Vec2 c = Jandirus.Core.Tech.NaveGrande.PixelDe(p.Cel);
			w.Put(c.X);
			w.Put(c.Y);
			w.Put(true);          // "aparafusada": mobilia de nave nao pisca de solta
			w.Put((byte)0);
			w.Put("");            // sem dono: ela e da NAVE, e a nave ja tem dono
			w.Put(p.Arte);
			w.Put(p.Estado);
			w.Put(0f);
			w.Put(0f);
			// A DENSIDADE VIAJA, mas quem BLOQUEIA de verdade e a planta: o `AplicarColisaoDasObras`
			// limpa a camada de obras a cada pacote, e a planta e compartilhada por todas as naves --
			// por isso a parede do console esta assada no bitset dela. Ver `NaveGrande.Montar`.
			w.Put(p.Densa);
		}
		foreach (Nave n in navesDaZona)
		{
			Construcao? cn = _obras?.Get(n.Tipo);
			w.Put(-n.Id);
			w.Put(n.Tipo);
			w.Put(cn?.Nome ?? n.Tipo);
			w.Put(n.X);
			w.Put(n.Y);
			w.Put(true);          // "aparafusada": nave parada nao pisca de solta -- ela esta pousada
			w.Put((byte)0);
			w.Put(n.DonoNome);
			w.Put(cn?.Arte ?? "");
			w.Put(cn?.Estado ?? "");
			w.Put((float)(cn?.PixelX ?? 0));
			w.Put((float)(cn?.PixelY ?? 0));
			w.Put(cn?.Densa ?? false);
		}
		foreach (Obra o in daZona)
		{
			Construcao? c = _obras?.Get(o.Tipo);
			w.Put(o.Id);
			w.Put(o.Tipo);
			w.Put(c?.Nome ?? o.Tipo);
			w.Put(o.X);
			w.Put(o.Y);
			w.Put(o.Aparafusada);
			w.Put((byte)o.Lab);
			w.Put(o.DonoNome);
			// A ARTE VIAJA JUNTO. O cliente tem o catalogo, mas so o que ELE pode comprar -- e a
			// bancada de outra pessoa tem que aparecer do mesmo jeito. Mandar o caminho aqui evita
			// que "ver" dependa de "poder construir".
			w.Put(c?.Arte ?? "");
			w.Put(c?.Estado ?? "");
			w.Put((float)(c?.PixelX ?? 0));
			w.Put((float)(c?.PixelY ?? 0));
			// ...e a DENSIDADE pelo mesmo motivo: o cliente tem que barrar o corpo no que o
			// servidor barra, e sem isto ele teria que adivinhar pelo catalogo -- que so lista o
			// que ELE pode comprar.
			w.Put(c?.Densa ?? false);
		}
		foreach (ServerPlayer pl in _players.Values)
			if (pl.Zone.Equals(zona)) pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		AplicarColisaoDasObras(zona);
	}

	/// <summary>
	/// AS CONSTRUCOES VIRAM PAREDE no mapa da zona.
	///
	/// ============================ POR QUE ISTO FALTAVA ============================
	/// A queixa do dono foi sobre o banco: "o banco n tem fisica, eu atravesso ele". O banco do
	/// MAPA foi consertado na arvore de tipos do proprio DM. Mas a construcao ERGUIDA por um
	/// jogador nunca teve fisica em lugar nenhum -- ela era so um desenho no cliente. A mesma
	/// Research Station bloqueia quando vem do `.dmm` e nao bloqueava quando alguem a construia.
	/// ==============================================================================
	///
	/// REFAZ A ZONA INTEIRA em vez de somar a celula nova: e uma lista de dezenas, muda so quando
	/// alguem constroi ou derruba, e o caminho incremental teria que acertar tambem o caso de
	/// remocao -- que e onde uma parede fantasma ficaria pra tras sem nada apontando pra ela.
	/// </summary>
	private void AplicarColisaoDasObras(ZoneKey zona)
	{
		// `MapaDaZonaOuCatalogo` E NAO `_catalogo?.Get`: o catalogo so conhece mapa de ARQUIVO, e
		// esta era uma das 18 escritas na mao que o cabecalho daquele metodo denuncia -- o resultado
		// medido era que uma construcao DENSA erguida num planeta gerado nunca virava parede no
		// servidor, enquanto o cliente a desenhava densa (a densidade viaja em `MandarObras`).
		// Cliente e servidor discordando sobre onde ha parede e exatamente o que o `MoveRules` foi
		// escrito pra impedir.
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		if (mapa == null) return;

		mapa.LimparObras();
		foreach (Obra o in _noChao)
		{
			if (!o.Zona.Equals(zona)) continue;
			if (_obras?.Get(o.Tipo) is not { Densa: true }) continue;
			(int cx, int cy) = CatalogoDeObras.Celula(o.X, o.Y);
			mapa.Bloquear(cx, cy);
		}

		// A NAVE PARADA TAMBEM E PAREDE -- `density=1` no `obj/Spacepod` (`PlanetTech.dm:43`). Ela
		// entra na MESMA passada porque o `LimparObras` acima apaga a camada inteira: uma segunda
		// funcao pra bloquear naves apagaria o trabalho desta, ou seria apagada por ela.
		//
		// A PILOTADA nao bloqueia nada, e e o `density = 0` do `verb/Use` (:137) -- sem isso o
		// piloto ficaria preso dentro do proprio veiculo.
		foreach (Nave n in NavesParadasEm(zona))
		{
			if (_obras?.Get(n.Tipo) is not { Densa: true }) continue;
			(int cx, int cy) = CatalogoDeObras.Celula(n.X, n.Y);
			mapa.Bloquear(cx, cy);
		}
	}

	/// <summary>Manda o catalogo com o motivo de cada recusa PRA MIM. E a aba Tech.</summary>
	private void MandarCatalogoDeObras(ServerPlayer pl)
	{
		if (_obras == null) return;
		List<(Construcao Obra, RecusaObra Motivo)> ofertas = _obras.Ofertas(pl.Ficha, pl.Race);

		var w = Protocol.Begin(Protocol.S2C.Tech);
		w.Put(pl.Ficha.techskill);
		w.Put(pl.Ficha.Zeni);
		w.Put(pl.Ficha.techXp);
		w.Put(pl.Ficha.TechXpDoProximo());
		w.Put((ushort)ofertas.Count);
		foreach ((Construcao c, RecusaObra r) in ofertas)
		{
			w.Put(c.Id);
			w.Put(c.Nome);
			w.Put(c.Custo);
			w.Put(c.Tech);
			w.Put((byte)r);
			// A ARTE VIAJA porque o cliente NAO le `construcoes.json` -- ele so conhece o que o
			// servidor manda. Sem ela a grade da bancada seria uma lista de nomes, e o dono pediu
			// icone justamente pro jogador "saber oq e oq".
			w.Put(c.Arte);
			w.Put(c.Estado);
		}
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>O canal unico de tecnologia, do mesmo jeito que o de habilidade.</summary>
	private void ComandoDeTech(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "lista": MandarCatalogoDeObras(pl); break;
			case "construir": Construir(pl, arg); break;
			case "posicionar": Posicionar(pl, arg); break;
			case "pegar": PegarObra(pl); break;
			case "aparafusar": Aparafusar(pl); break;
			case "estudar":
				pl.Estudando = !pl.Estudando;
				Avisar(pl, pl.Estudando ? "voce se debruca sobre a bancada." : "voce para de estudar.");
				break;
			case "androide_absorcao": VirarAndroide(pl, infinito: false); break;
			case "androide_infinito": VirarAndroide(pl, infinito: true); break;
			case "lab_androide": InstalarLab(pl, 1); break;
			case "lab_bio": InstalarLab(pl, 2); break;
			case "colher_dna": ColherDna(pl); break;
			case "gestar": GestarBio(pl); break;
			default: Avisar(pl, $"comando de tecnologia desconhecido: {cmd}"); break;
		}

		// QUALQUER COMANDO PODE TER MEXIDO NO ZENI OU NA RACA, e as duas coisas mudam o que a aba
		// mostra e o que ela deixa comprar. Reenviar o catalogo depois de TODOS e mais barato que
		// lembrar, comando a comando, qual deles precisava -- e e o esquecimento nesse tipo de
		// lista que deixa a interface mentindo.
		if (cmd != "lista") MandarCatalogoDeObras(pl);
	}
}
