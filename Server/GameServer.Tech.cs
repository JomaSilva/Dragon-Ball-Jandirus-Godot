using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Stats;
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
	/// (2026-10-25) LIBERADA PRA TODOS -- o dono: *"permita com q o criador da tech libere ela quando quiser pra qualquer um mexer nela,
	/// desparafusar, pegar, etc, tudo q o dono pode fazer"*. Ligada, quem chegar mexe nela como o dono (`GameServer.MexeComoDono`); quem a liga
	/// e desliga e so o dono (o botao da linha dela na aba Tech, `LiberarObra`). Vai pro `mundo.json` com o resto, e a obra recolhida a esquece.
	/// </summary>
	public bool Liberada;

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
	/// O ESTADO DO REGENERADOR, quando esta obra e um. Nulo em todo o resto -- e mora aqui pelo motivo
	/// escrito no <see cref="Gravidade"/> logo acima: nasce e morre com a obra, e vai pro `mundo.json` de
	/// graca. Ver `GameServer.Regenerador.cs`.
	/// </summary>
	public RegeneradorDaObra? Regenerador;

	/// <summary>
	/// O ESTADO DA DRILL (nivel, estoque e ate quando ela ja foi contada), quando esta obra e uma. Nulo em
	/// todo o resto -- e mora aqui pelo motivo escrito no <see cref="Gravidade"/>: nasce e morre com a obra, e
	/// vai pro `mundo.json` de graca, que e o que faz a Drill trabalhar atraves do reinicio. Ver
	/// `GameServer.Drill.cs`.
	/// </summary>
	public DrillDaObra? Drill;

	/// <summary>
	/// QUE LABORATORIO FOI INSTALADO no mainframe: 0 nenhum, 1 Android Lab, 2 Bio-Android Lab.
	///
	/// E assim no original e vale manter: nao se CONSTROI um laboratorio, se constroi o
	/// mainframe e depois se escolhe o que ele vira. A escolha e definitiva, e e ela que
	/// separa quem quis virar maquina de quem quis FAZER uma.
	/// </summary>
	public int Lab;

	/// <summary>
	/// O QUE ESTA ESCRITO NESTA LAPIDE -- o `A.desc = "[text]"` do `GenerateCross` (`Corpse.dm:53`).
	/// Vazio em todo o resto.
	///
	/// ============================ POR QUE ELE MORA NA OBRA, E NAO NUM DICIONARIO ============================
	/// Exatamente o argumento que o <see cref="Gravidade"/> quatro campos acima ja escreveu: duas
	/// lapides sao duas lapides, e um dicionario paralelo (id -> texto) funcionaria ate uma delas ser
	/// destruida sem ninguem limpar o dicionario -- e a proxima obra a receber aquele id herdaria o
	/// epitafio de um morto que nao e ela.
	///
	/// Dentro da obra, ele nasce e morre com ela, e vai pro `mundo.json` de graca -- que e o que faz um
	/// tumulo sobreviver ao reinicio do servidor. Ver `GameServer.Cadaver.ErguerALapide`.
	/// ====================================================================================================
	/// </summary>
	public string Epitafio = "";

	// ============================ AQUI MORAVA UM `Vida = 8` (`DNL_LAB_HEALTH`), E ELE FOI DELETADO ============================
	// O campo era escrito no nascimento, escrito de novo ao nascer o bio-androide e **nunca
	// decrementado por ninguem**: nao havia um unico `Vida--` no repo. O laboratorio deste port
	// sempre caiu pela ARMADURA generica (`Armadura.Bater`, ver o campo logo abaixo), que e o
	// sistema que este projeto escolheu pra obra erguida por jogador -- e ter os dois numeros
	// convivendo era prometer uma contagem de golpes que nao existia.
	//
	// O ORIGINAL TEM MESMO OS OITO GOLPES (`Attack_Lab`, um verb publico), e a divergencia fica
	// declarada em vez de fingida: aqui quem derruba um laboratorio e quem bate FORTE, e nao quem
	// bate oito vezes. O que importava do original -- que destruir o lab CANCELA a fornada e o
	// mundo inteiro fica sabendo -- esta ligado, e mora no `Estragar`.
	// ========================================================================================================================
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

	/// <summary>
	/// O ESTADO DE UMA MAQUINA PORTADA NA v0.2.1 (o bau, a base, o simulador...): numeros, textos e o que ela guarda.
	/// Nulo em todo o resto, e nulo na maquina que ainda nao guardou nada -- ver <see cref="EstadoDeTech"/>, onde esta
	/// contado por que e um saco so e nao uma classe por maquina. Mora aqui pelo motivo do <see cref="Gravidade"/>:
	/// nasce e morre com a obra, e vai pro `mundo.json` de graca.
	/// </summary>
	public EstadoDeTech? Tech;
}

/// <summary>
/// A GESTACAO DO BIO-ANDROIDE. Um mes in-game (`DNL_BIO_BREW_MONTHS 0.1` de ano) contando os
/// DNAs colhidos -- e quem morre quando termina e o CRIADOR.
/// </summary>
public sealed class Gestacao
{
	/// <summary>
	/// AS AMOSTRAS. Ate quatro (`DNL_BIO_MAX_DNA`), e cada uma e um REGISTRO e nao uma string.
	///
	/// ============================ ELA ERA `List&lt;string&gt;` E VIRAVA LOG ============================
	/// A lista guardava so o nome da raca, e as duas unicas linhas do port que a liam eram
	/// `GD.Print`. Ou seja: derrubar um jogador, enfiar uma agulha nele e esperar meio dia produzia
	/// **uma linha de console**. Era o sexto caso de dado extraido sem consumidor deste porte, e o
	/// mais caro deles em tempo de jogo.
	///
	/// O registro tem os mesmos cinco campos do `obj/items/DNA_Sample` do original
	/// (`DNALabs.dm:258-266`) porque cada um deles tem consumidor de verdade do outro lado:
	/// a RACA vira `brew_has_saiyan`, o BP vira `brew_strongest_bp`, a ASSINATURA reencontra o
	/// doador online pra ler o BP ATUAL dele, e as SKILLS viram as tecnicas que o bio ja nasce
	/// sabendo.
	/// ==========================================================================================
	/// </summary>
	public List<Amostra> Amostras = [];

	public long PrometidaEm;             // ms reais em que a gestacao termina
	public string DonoConta = "";

	/// <summary>
	/// O `creator_sig` do DM (`DNALabs.dm:441`): a ASSINATURA do personagem que fechou o tanque -- e e ele, e nao
	/// outro personagem da mesma conta, que a criatura mata ao nascer (`P.signature == creator_sig`, `:364`).
	/// Vazia em fornada gravada antes de 2026-10-11 (e em corpo sem slot): ai vale a conta, como era.
	/// </summary>
	public string DonoAssinatura = "";

	// =====================================================================
	// O QUE A FORNADA CONSOLIDOU (so depois de `GestarBio`)
	// =====================================================================
	/// <summary>`brew_strongest_bp` -- o bio nasce com METADE disto.</summary>
	public double MaiorBp;

	/// <summary>`brew_has_saiyan` -- havia sangue Saiyajin entre os doadores.</summary>
	public bool TemSaiyajin;

	/// <summary>`brew_verbs` -- a uniao SEM DUPLICATAS das skills dos doadores (desde a v0.3.0, o livro inteiro de cada um).</summary>
	public List<string> Tecnicas = [];
}

/// <summary>
/// UMA AMOSTRA DE DNA -- o `obj/items/DNA_Sample` do original (`DNALabs.dm:258-266`).
///
/// ============================ POR QUE ELA GUARDA A ASSINATURA ============================
/// Porque o BP que vale nao e o da coleta. `DNALabs.dm:430-434`: na hora de FECHAR a fornada, o
/// original procura cada doador entre os jogadores online e usa o BP ATUAL dele; so quem estiver
/// offline entra com o numero congelado da agulha.
///
/// A regra tem consequencia de jogo, e ela e boa: colher DNA de alguem cedo e esperar ele treinar
/// PAGA. O laboratorio nao rouba uma foto -- ele rouba a pessoa.
/// ==========================================================================================
/// </summary>
public sealed class Amostra
{
	public string Raca = "";

	/// <summary>
	/// A RACA DO PAI DO DOADOR -- e ela nao e enfeite: sem esta linha o meio-sangue entra no tanque
	/// cego.
	///
	/// A regra que decide quem respira no vacuo e `Race == X || Parent_Race == X`
	/// (<see cref="Jandirus.Core.World.Vacuo.RespiraNoVacuo"/>, a mesma que todo requisito racial
	/// deste port segue). Um filho de Majin com humana tem `Race = "Human"` e **respira**; uma
	/// amostra que so guardasse `Race` diria "Human", e o bio nasceria sufocando com DNA de Majin
	/// dentro dele -- que e precisamente o caso que aquela regra existe pra cobrir.
	///
	/// Campo novo em classe ja gravada no `mundo.json`: fornada antiga volta do disco com `""`, que
	/// e o mesmo que "sem pai conhecido", e a pergunta continua respondendo pela `Raca`.
	/// </summary>
	public string RacaDoPai = "";

	public string Doador = "";
	public string Assinatura = "";
	public double Bp;

	/// <summary>
	/// AS SKILLS DO DOADOR -- o LIVRO INTEIRO dele (v0.3.0, dono: *"bio androide tb vem com todas as skills da aba learn dos
	/// jogadores q tiveram o dna usado, do mesmo nivel q eles, e as tecnicas customizadas tb e as liberadas"*): o que ele
	/// comprou, o que lhe ensinaram e o que lhe deram. Ver `GameServer.SkillsHerdaveis`.
	///
	/// DIVERGENCIA DECLARADA: o DM guarda so os botoes (`donor_verbs = M.Keyableverbs.Copy()`, `DNALabs.dm:312`) -- nem o buff
	/// passivo, nem o nivel, nem a tecnica customizada. Amostra gravada antes da v0.3.0 tem so as que tem botao.
	/// </summary>
	public List<string> Skills = [];

	/// <summary>(v0.3.0) O NIVEL de cada skill do doador na hora da agulha -- o bio nasce com elas nesse nivel. Vazio = nivel zero.</summary>
	public Dictionary<string, int> Niveis = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>(v0.3.0) AS TECNICAS CUSTOMIZADAS do doador (as confirmadas), copiadas na agulha. O bio as recebe como ensinadas.</summary>
	public List<Jandirus.Core.Skills.TecnicaCustomizada> Customizadas = [];

	/// <summary>
	/// (v0.3.0) AS RACAS DO SANGUE do doador (`SangueDoBio.DeUmDoador`): a raca e a do pai, ou, num bio colhido, as doadoras
	/// dele. Vazio em amostra de antes: ai valem a `Raca` e a `RacaDoPai` (`GameServer.SangueDaAmostra`).
	/// </summary>
	public List<string> Sangue = [];

	/// <summary>(v0.3.0) O doador era um androide de ENERGIA INFINITA (ou um bio que herdou o motor dele): o bio herda o motor.</summary>
	public bool Infinito;
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

	/// <summary>
	/// AS OBRAS QUE O `mundo.json` TROUXE DE UM LUGAR EM QUE NAO SE CONSTROI (`LugarDeObra`): fora do mundo -- ninguem
	/// as ve, elas nao barram, nao contam em lista nenhuma -- e regravadas como vieram. So a limpeza total as leva.
	/// </summary>
	private readonly List<Obra> _obrasSemLugar = [];
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
	// (2026-10-18) O MESMO NUMERO DE CONSTRUIR O MAINFRAME -- ver `CatalogoDeObras.TechDoMainframe`.
	private const double TechDoLaboratorio = CatalogoDeObras.TechDoMainframe;

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
	private const int BioMaxDna = Jandirus.Core.Races.BioAndroids.MaxDna;

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

			// E A IRMA DELA PRO "TECH 999": o que pede tecnologia acima do teto do original nao e oferta, e
			// nao devia nem estar no arquivo. Ver `Construcao.AcimaDoTeto`.
			AvisarDasAcimaDoTeto(_obras);

			// E A TERCEIRA: o id que o arquivo traz duas vezes (o `Staff` e o `PDA` de antes de 2026-10-10).
			AvisarDosRepetidos(_obras);
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
		CarregarOCofreDeTech();   // (v0.2.1) o que os itens de bolso guardam fora do id -- ver `GameServer.TechDeBolso.cs`
		SemearOEnma();   // o juiz dos mortos, na mesa dele -- ver `GameServer.Alem.cs`, secao 5
		SemearOsNpcsDeConversa();   // (v0.3.0) o Sr. Kaioh e o Korin -- ver `GameServer.NpcsDeConversa.cs`

		// ============================ E ENTAO A GUARDA, EM VOZ ALTA ============================
		// DEPOIS do `CarregarObjetosDoMapa` e nao antes: ela cruza o catalogo com o que esta
		// REALMENTE de pe neste mundo, e as duas metades so existem juntas nesta linha.
		//
		// Ela e a irma de boot do `AvisarDasMudas` acima, e cobre o que ele nao alcanca: ele
		// pergunta do CATALOGO ("alguma construcao densa esta sem arte?"), ela pergunta do MAPA
		// ("alguma coisa que este mundo poe de pe nao vai aparecer?") -- e foi a segunda pergunta
		// que faltou, porque "maquina" era um dono ACEITO da parede na varredura antiga.
		// Ver `GameServer.CenarioMudo.cs`.
		// =======================================================================================
		ConferirCenarioMudo();
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
	/// GRITA POR CADA ENTRADA DO CATALOGO QUE PEDE TECNOLOGIA ACIMA DO TETO DO ORIGINAL -- o "Space Pod de tech
	/// 999" que o dono viu na aba Tech (2026-10-10). Devolve quantas eram, pelo motivo do
	/// <see cref="AvisarDasMudas"/>: e o numero que a bancada `--recursosteste` le.
	///
	/// Com o `construcoes.json` saido do extrator de hoje a resposta e ZERO. Se um arquivo antigo (ou uma linha
	/// posta a mao) trouxer uma de volta, a aba nao a mostra (`CatalogoDeObras.Ofertas`) e este aviso diz por que.
	/// </summary>
	private int AvisarDasAcimaDoTeto(CatalogoDeObras cat)
	{
		int n = 0;
		foreach (Construcao c in cat.AcimaDoTeto())
		{
			n++;
			GD.PushWarning($"[server] construcao '{c.Id}' pede tecnologia {c.Tech:0} (o teto do original e "
						 + $"{CatalogoDeObras.TechMaximoDoDm:0}): no DM isso e um balcao posto no mapa, nao uma oferta. "
						 + "Ela fica fora da aba Tech; rode o AssetPipeline ('tech') pra tira-la do construcoes.json.");
		}
		return n;
	}

	/// <summary>
	/// GRITA POR CADA ID QUE O `construcoes.json` TRAZ MAIS DE UMA VEZ -- so a ultima linha vira construcao, e a
	/// outra some calada (`CatalogoDeObras.Repetidos`). Devolve quantos eram, pelo motivo do
	/// <see cref="AvisarDasMudas"/>: e o numero que a bancada `--aposentadosteste` le.
	/// </summary>
	private int AvisarDosRepetidos(CatalogoDeObras cat)
	{
		foreach (string id in cat.Repetidos)
			GD.PushWarning($"[server] o construcoes.json traz '{id}' mais de uma vez: so a ULTIMA linha vale, e a outra "
						 + "some. No DM o mesmo tipo declarado em dois arquivos e UM tipo -- tire a linha que sobra.");
		return cat.Repetidos.Count;
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
			(int postas, int mudas) = PorMobiliaDoMapa(e, soAQueFalta: false);
			n += postas;
			semArte += mudas;
		}

		if (n > 0) GD.Print($"[server] maquinas do mapa: {n} viraram construcoes de pe");
		if (semArte > 0)
			GD.PushWarning($"[server] {semArte} maquina(s) do mapa sem entrada no catalogo -- "
						   + "rode o AssetPipeline ('tech' e depois 'maps')");
	}

	/// <summary>
	/// A MOBILIA DE UMA ZONA DE ARQUIVO, posta de pe a partir do `.objetos` dela. Devolve quantas pos e quantas o
	/// catalogo nao conhece.
	///
	/// DOIS CHAMADORES: o boot (todas, de todas as zonas) e o `Restaurar` do admin, que so repoe
	/// <paramref name="soAQueFalta"/> -- a macieira, o banco e a bancada que cairam na porrada. Sem a segunda volta,
	/// refazer o cenario refechava a celula delas (o bit do `.col`, ver `ACelulaDaObraCaiu`) e a obra so voltava no
	/// boot seguinte: uma parede sem desenho no lugar de cada uma.
	/// </summary>
	private (int Postas, int SemCatalogo) PorMobiliaDoMapa(ZoneEntry e, bool soAQueFalta)
	{
		if (e.Objetos.Length == 0 || !Godot.FileAccess.FileExists(e.Objetos)) return (0, 0);

		// PRE-FEITA POR CONSTRUCAO: o `ZoneEntry` e uma zona de ARQUIVO (convertida de .dmm).
		// Nao ha `.objetos` de mundo sorteado -- la a mobilia sai da seed (o pomar, `GameServer.Pomar.cs`).
		ZoneKey zona = ZoneKey.Premade(e.Zona);
		int postas = 0, semCatalogo = 0;

		foreach (ObjetoDoMapa o in ObjetosDoMapa.Parse(Godot.FileAccess.GetFileAsString(e.Objetos)))
		{
			if (_obras?.Get(o.Id) == null) { semCatalogo++; continue; }
			if (soAQueFalta && ObraNaCelula(zona, o.X, o.Y) != null) continue;   // de pe, ou a celula ja tem outra obra

			PorMobiliaDePe(zona, o.Id, o.X, o.Y);
			postas++;
		}

		if (postas > 0 || !soAQueFalta) AplicarColisaoDasObras(zona);
		return (postas, semCatalogo);
	}

	/// <summary>
	/// UMA MOBILIA DO MAPA, posta de pe numa celula: sem dono, aparafusada e fora do disco (<see cref="Obra.DoMapa"/>).
	///
	/// DUAS FONTES, a mesma obra: o `.objetos` das zonas de arquivo (logo acima) e o pomar dos mundos sorteados
	/// (`GameServer.Pomar.cs`), cuja lista sai da seed. Quem a poe nao aplica a colisao -- isso e de quem sabe
	/// quando a leva acabou.
	/// </summary>
	private Obra PorMobiliaDePe(ZoneKey zona, string tipo, int cx, int cy)
	{
		// A CELULA VIRA PIXEL NO MEIO DELA. O conversor grava a celula; a obra guarda a
		// posicao do CORPO que a ergueu, e `CatalogoDeObras.Celula` desfaz isso somando o
		// deslocamento dos pes. Aqui a conta e a inversa, e tem que ser a mesma -- senao a
		// maquina desenha uma celula acima de onde ela esta no mapa.
		const int t = ZoneCollision.TileSize;
		var maquina = new Obra
		{
			Id = _proximaObraId++,
			Tipo = tipo,
			X = cx * t + t / 2f,
			Y = cy * t + t / 2f - MoveRules.FeetOffsetY,
			DonoNome = "",
			Aparafusada = true,
			DoMapa = true,
			ErguidaEm = 0,
		};
		maquina.PorZona(zona);
		_noChao.Add(maquina);
		return maquina;
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
			// (2026-10-26) O SINTETIZADOR DE VIRUS SAIU DO JOGO: o que estava aparafusado no chao nao volta -- ver `TirarOsSintetizadoresDoMundo`.
			TirarOsSintetizadoresDoMundo();
			_proximaObraId = _noChao.Count > 0 ? _noChao.Max(o => o.Id) + 1 : 1;

			// A OBRA GRAVADA NUM LUGAR EM QUE NAO SE CONSTROI (mente, bolso da fusao, selo, interior de Majin --
			// `LugarDeObra`) e de antes da recusa. Ela NAO VOLTA PRO MUNDO: a chave desses lugares e um id de sessao,
			// e de pe ela apareceria -- desenhada e barrando -- na mente de quem tivesse aquele numero agora. E NAO
			// SAI DO ARQUIVO: fica guardada a parte e e regravada como veio (`GravarMundoEm`). O id dela continua
			// contando pro proximo id livre, que foi calculado na linha de cima.
			List<Obra> semLugar = _noChao.Where(o => !LugarDeObra.Aceita(o.Zona)).ToList();
			if (semLugar.Count > 0)
			{
				_noChao.RemoveAll(o => !LugarDeObra.Aceita(o.Zona));
				_obrasSemLugar.AddRange(semLugar);
				GD.Print($"[server] mundo: {semLugar.Count} construcao(oes) gravada(s) num lugar em que nao se constroi mais "
						 + $"({string.Join(", ", semLugar.Select(o => $"{o.Tipo} em {o.Zona}").Take(6))}{(semLugar.Count > 6 ? ", ..." : "")}). "
						 + "Ficam FORA do mundo e continuam no arquivo, como estavam.");
			}
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
			// ...MAIS O QUE FICOU FORA DO MUNDO AO CARREGAR (`_obrasSemLugar`): o servidor nao apaga do arquivo o
			// que ele so deixou de por de pe.
			System.IO.File.WriteAllText(caminho,
				JsonSerializer.Serialize(_noChao.Where(o => !o.DoMapa).Concat(_obrasSemLugar).ToList(),
										 new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
		}
		catch (Exception e) { GD.PushWarning($"[server] nao gravei o mundo: {e.Message}"); }
	}

	// =====================================================================
	// CONSTRUIR
	// =====================================================================
	/// <summary>
	/// FABRICA UMA COISA NA BANCADA. Ela vai pra MOCHILA -- nunca pro chao.
	///
	/// ============================ A REGRA 1 DO DONO ============================
	/// *"faca q todo item q vc produzir na research table, va parar no inventario do personagem"*.
	/// E o que este metodo faz, e a guarda de mochila cheia vem ANTES da cobranca: tirar o zeni pra
	/// descobrir depois que nao coube seria vender um item que nao foi entregue.
	/// ===========================================================================
	///
	/// ============================ COM ZENI OU COM RECURSOS (dono, 2026-10-10) ============================
	/// *"toda tech tem seu custo dobrado em recursos em realacao ao zenni, entao se algo custo 1000 zennis pra
	/// fazer vc pode fazer com 2000 resources"*. A moeda vem do botao que o jogador apertou (`construir` ou
	/// `construir_recursos`), e vale por inteiro: a pergunta "pode?" e a cobranca sao feitas NELA
	/// (`Permitida` e <see cref="Cobrar(ServerPlayer, double, Moeda)"/>), e saldo curto numa nao se completa
	/// com a outra.
	/// ======================================================================================================
	/// </summary>
	private void Construir(ServerPlayer pl, string tipo, Moeda moeda = Moeda.Zeni)
	{
		if (_obras == null) { Avisar(pl, "o servidor esta sem catalogo de construcoes."); return; }
		Construcao? c = _obras.Get(tipo);
		if (c == null) { Avisar(pl, "isso nao existe."); return; }

		RecusaObra r = CatalogoDeObras.Permitida(c, pl.Ficha, pl.Race, moeda);
		if (r != RecusaObra.Pode) { Avisar(pl, MotivoObra(r, c, pl)); return; }

		// ============================ FABRICAR NAO E ERGUER -- E AQUI O PORT DIVERGE DO DM DE PROPOSITO ============================
		// Ate aqui, comprar uma maquina de gravidade a plantava NA HORA, embaixo dos pes de quem
		// comprou. Nao havia como escolher onde -- e "onde" e metade da decisao quando a coisa
		// bloqueia passagem e custa meio milhao.
		//
		// **NO ORIGINAL E O CONTRARIO, E ISTO PRECISA ESTAR ESCRITO CERTO.** O comentario que estava
		// aqui dizia que o `Click()` do Creatable manda o objeto pro `contents` do jogador -- e
		// FALSO. `TechSupport.dm:50` faz `new create_type(locate(usr.x,usr.y,usr.z))`, e
		// `locate(x,y,z)` e um TURF: no DM a coisa nasce no chao, aos pes de quem comprou, sem
		// escolha de lugar. O desvio e o pedido do dono, e ele fica melhor documentado como desvio
		// do que disfarcado de fidelidade.
		//
		// A COBRANCA SO ACONTECE SE COUBER. Tirar o zeni antes de descobrir que a mochila esta
		// cheia seria vender um item que nao foi entregue.
		if (pl.Mochila.Cheio)
		{
			Avisar(pl, $"sua mochila está cheia ({Jandirus.Core.Items.Inventario.Slots} espaços).");
			return;
		}

		// (v0.2.1) O QUE A FABRICACAO PEDE ALEM DO PRECO: a semente da capsula de Saibaman (`GameServer.Companheiros.cs`).
		if (PorQueNaoFabrica(pl, c.Id) is { } falta) { Avisar(pl, falta); return; }

		// A PORTA UNICA DA COBRANCA. O `Permitida` de cima ja conferiu o saldo NESTA moeda, entao a recusa
		// daqui so dispara se as duas contas divergirem -- e nesse caso nada foi cobrado e nada e entregue.
		if (!Cobrar(pl, c.Custo, moeda))
		{
			Avisar(pl, MotivoObra(moeda == Moeda.Recursos ? RecusaObra.SemRecursos : RecusaObra.SemZeni, c, pl));
			return;
		}
		Guardar(pl, c.Id);
		AoFabricarTech(pl, c.Id);   // (v0.2.1) o que vem junto de uma fabricacao -- o barril da nuke, `GameServer.Armas.cs`

		string preco = moeda == Moeda.Recursos
			? $"{Recurso.CustoEmRecursos(c.Custo):N0} recursos"
			: $"{c.Custo:N0} zeni";
		GD.Print($"[server] {pl.Name} fabricou {c.Nome} ({preco})");
		// A FRASE PERGUNTA A MESMA COISA QUE O MENU E O `Posicionar` -- ver
		// `CatalogoDeItens.PodeAssentarNoChao`. Prometer "assente no chao" pra uma armadura era o
		// sintoma visivel de a classificacao estar errada, e agora a promessa e a acao sao a mesma
		// pergunta.
		Avisar(pl, Jandirus.Core.Items.CatalogoDeItens.PodeAssentarNoChao(c.Id)
			? $"você fabrica {c.Nome} por {preco}. Está na mochila -- use \"Instalar no chão\" pra escolher o lugar."
			: $"você fabrica {c.Nome} por {preco}. Está na sua mochila.");
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
	/// As guardas sao as mesmas que o construir de antes ja tinha, mais o ALCANCE (que so faz sentido
	/// agora que o ponto deixou de ser os proprios pes) e mais a pergunta da REGRA 2.
	/// =======================================================================================================
	///
	/// ============================ E O CLIENTE FAZ AS MESMAS PERGUNTAS ANTES DE MANDAR ============================
	/// Nao pra o servidor confiar nele -- ele nao confia --, mas pra o jogador ver o "nao cabe" antes
	/// do clique. As perguntas de MAPA e de ALCANCE moram no Core (<see cref="Assentamento.DoLugar"/>)
	/// justamente pra as duas pontas nao poderem discordar por escrito.
	///
	/// **QUANDO ELAS DISCORDAM MESMO ASSIM** -- e discordam, porque o cliente mede da posicao
	/// interpolada e o servidor da ultima confirmada, e porque a lista de obras dele pode estar um
	/// pacote atrasada -- o desfecho e este, e ele e deliberado:
	///
	///   1. **O ITEM NAO SAIU DA MOCHILA.** O `Tirar` acontece depois de TODAS as guardas, la
	///      embaixo. Uma recusa nao custa nada ao jogador: nada e gasto, nada some.
	///   2. **A RECUSA DIZ O MOTIVO**, com a frase do Core -- a mesma que o cliente teria dito.
	///   3. **O FANTASMA VOLTA PRA MAO.** O cliente segura a previa ate a resposta chegar e, se o
	///      item continuar na mochila, rearma o fantasma pra tentar um tile ao lado sem ter que
	///      reabrir a bolsa. Ver `TelaDeConstrucao`.
	/// ========================================================================================================
	/// </summary>
	private void Posicionar(ServerPlayer pl, string arg)
	{
		// "<id>/<x>/<y>" -- o canal de verbos tem um argumento so.
		string[] p = arg.Split('/');
		if (p.Length < 3 || !float.TryParse(p[1], out float x) || !float.TryParse(p[2], out float y)
			|| !NumeroFinito(x) || !NumeroFinito(y))   // "NaN" passa no `TryParse`: ver `NumeroFinito`
		{ Avisar(pl, "ponto inválido."); return; }

		// O ITEM NA MAO E O QUE ELE VIRA NO CHAO sao quase sempre a mesma coisa (a bancada e a bancada). A
		// SEMENTE e a excecao: sai a semente da mochila, e o que se ergue e a arvore -- o `Plant()` de
		// `Plants.dm:949-953`. Quem diz o que vira o que e o Core (`OQueAssenta`), a mesma casa que o
		// fantasma do cliente consulta.
		Jandirus.Core.Items.ItemDef? item = Jandirus.Core.Items.CatalogoDeItens.Get(p[0]);
		Construcao? c = _obras?.Get(Jandirus.Core.Items.CatalogoDeItens.OQueAssenta(p[0]));
		if (c == null || item == null) { Avisar(pl, "isso não existe."); return; }
		if (pl.Mochila.Quantos(item.Id) <= 0) { Avisar(pl, $"você não tem {item.Nome}."); return; }

		// (2026-10-18) AO LEME NAO SE CONSTROI: o corpo de quem pilota esta do lado de fora do casco, e o assento cairia no chao de fora --
		// a nave grande se monta por DENTRO. Ver `Naves.AoLemeNaoSeConstroi`.
		if (NaveDoPiloto(pl) is { } aoLeme && !ConstruirAoLemeDeTeste) { Avisar(pl, Naves.AoLemeNaoSeConstroi(aoLeme.Tipo)); return; }
		// (2026-10-19) PLANTIO E SO A SEMENTE: a maquina recolhida com as melhorias tambem assenta um id diferente do dela
		// (`Gravity|c12|500x` vira `Gravity`), e e instalada. A pergunta e a mesma do fantasma do cliente.
		bool plantio = Jandirus.Core.Items.CatalogoDeItens.EhPlantio(item.Id);

		// ============================ A REGRA 2, DO LADO DO SERVIDOR ============================
		// *"item de uso pessoal (scouter, armaduras, pesos) NAO ganha [instalar] -- ele e equipavel."*
		//
		// ESTA GUARDA FALTAVA INTEIRA, e sem ela a regra so existia na tela. Como Scouter, Armadura e
		// Pesos ESTAO no `construcoes.json`, bastava um `posicionar Scouter/x/y` -- de um cliente
		// mexido, ou do proprio menu enquanto a classificacao estava errada -- pra o servidor plantar
		// um scouter no chao como obra gravada no `mundo.json`. Regra ligada num chamador e esquecida
		// no outro e a armadilha que este projeto ja pagou mais vezes; aqui as duas pontas fazem a
		// MESMA pergunta (`CatalogoDeItens.PodeAssentarNoChao`), que le a MESMA lista de acoes.
		// =====================================================================================
		if (!Jandirus.Core.Items.CatalogoDeItens.PodeAssentarNoChao(item.Id))
		{
			Avisar(pl, Assentamento.Motivo(RecusaDeAssento.NaoEDoChao, item.Nome));
			return;
		}

		// ============================ O LUGAR: ALCANCE, PAREDE, AGUA, NUVEM E BEIRADA ============================
		// As cinco de uma vez, pela funcao do Core que o fantasma do cliente tambem chama. Antes daqui
		// eram duas e meia: alcance e parede, mais a agua **por acidente** (o `MoveRules.Occupied`
		// passava `ModoDeTravessia.APe`, e agua bloqueia quem anda). O acidente dava a frase errada --
		// "dentro de uma parede", em cima de um lago -- e a nuvem que DERRUBA passava batido: dava pra
		// assentar uma maquina de gravidade em cima do Caminho da Serpente.
		//
		// A CELULA E A DA CONSTRUCAO, e nao o pixel cru: e ela que o `Obra` ocupa e que o fantasma
		// desenha. Ver `CatalogoDeObras.Celula`.
		// ======================================================================================================
		(int cx, int cy) = CatalogoDeObras.Celula(x, y);
		RecusaDeAssento rl = Assentamento.DoLugar(MapaDaZonaOuCatalogo(pl.Zone), pl.Pos, cx, cy, pl.Zone);
		if (rl != RecusaDeAssento.Pode) { Avisar(pl, Assentamento.Motivo(rl, item.Nome)); return; }

		// ============================ ONDE NAO SE CONSTROI -- POR TIPO DE ZONA, NUM LUGAR SO ============================
		// A MENTE, O BOLSO DA FUSAO, O SELO E O INTERIOR DE UM MAJIN recusam (dono, 2026-10-11: *"n deveria ser possivel
		// contruir nada dentro desses interiores"*): a chave deles e o id de SESSAO de alguem, entao a maquina gravada na
		// "mente #5" apareceria na mente de quem fosse o #5 depois de um reinicio. A Sala do Tempo ACEITA -- a chave dela e a
		// seed do mundo, e a obra fica gravada so naquela Sala. E A NAVE GRANDE ACEITA desde 2026-10-18 (o dono: *"permita
		// construir dentro da capital ship"*): ela recusava pelo CICLO DE VIDA -- o `mundo.json` nao sabia quando a nave acaba,
		// e a obra ficaria de pe pra sempre numa zona onde ninguem mais entra --, e agora o fim dela tira o que esta dentro
		// (`TirarAsObrasDaNave`). Quem responde, com a frase de cada lugar, e o Core: `LugarDeObra`. Vem antes de tirar o item
		// da mochila, e vale pra tudo o que este verbo assenta: maquina, semente e NAVE.
		// =============================================================================================================
		if (LugarDeObra.PorQueNaoSeConstroi(pl.Zone) is { } aquiNao)
		{
			Avisar(pl, aquiNao);
			return;
		}

		// UMA NAVE NAO CABE DENTRO DE OUTRA (2026-10-18). Ela tem casco, piloto e decolagem proprios, e um interior nao e planeta de onde
		// se suba; o que a nave grande aceita sao as maquinas.
		if (Naves.EhNave(c.Id) && NaveGrande.EhInterior(pl.Zone, out _))
		{
			Avisar(pl, $"{c.Nome} não cabe dentro de outra nave -- assente do lado de fora.");
			return;
		}

		// NAO EMPILHA: duas construcoes no mesmo lugar viram uma so na tela e as duas respondem ao
		// mesmo clique. Meio tile de folga e o bastante pra nao encavalar.
		//
		// AS NAVES CONTAM NA MESMA CONFERENCIA. Elas moram noutra lista (ver `GameServer.Nave.cs`),
		// mas ocupam o mesmo chao -- e "ja tem coisa demais neste ponto" e uma pergunta sobre o
		// CHAO, nao sobre a lista em que a coisa esta guardada.
		//
		// A FOLGA E A DO CORE (`Assentamento.FolgaEntreObras`) porque o fantasma do cliente faz esta
		// mesma conferencia com a lista que ele recebeu -- e duas copias do numero 24 eram uma
		// chance de o fantasma ficar branco onde o servidor recusa.
		//
		// ...E A MOBILIA DA NAVE GRANDE (o console e a plataforma, 2026-10-18): o fantasma do cliente ja a contava -- ela chega no mesmo
		// pacote das obras (`MandarObras`) --, e o servidor nao.
		if (Assentamento.TemCoisaEm(
				_noChao.Where(o => o.Zona.Equals(pl.Zone)).Select(o => new Vec2((float)o.X, (float)o.Y))
					   .Concat(NavesParadasEm(pl.Zone).Select(n => new Vec2(n.X, n.Y)))
					   .Concat(MobiliaDoInterior(pl.Zone).Select(m => NaveGrande.PixelDe(m.Peca.Cel))),
				new Vec2(x, y)))
		{ Avisar(pl, Assentamento.Motivo(RecusaDeAssento.LugarOcupado, item.Nome)); return; }

		pl.Mochila.Tirar(item.Id);
		MandarMochila(pl);

		// ============================ A NAVE SEGUE POR OUTRA PORTA ============================
		// Ela passa pelas MESMAS guardas acima (alcance, nao-empilhar, nao dentro de parede) porque
		// elas sao sobre o chao e valem pra qualquer coisa que se assente. O que muda e o RESTO da
		// vida dela: ela se pilota, viaja entre mundos, tem casco, senha e piloto -- ver o cabecalho
		// de `Nave`. A chave de zona deixou de ser o motivo da separacao: as duas guardam a mesma.
		if (Naves.EhNave(c.Id)) { AssentarNave(pl, c, x, y, item.Id); return; }

		var obra = new Obra
		{
			Id = _proximaObraId++,
			Tipo = c.Id,
			X = x,
			Y = y,
			DonoConta = pl.Conta,
			DonoNome = pl.Name,
			ErguidaEm = NowMs(),
			// A MESA DE PESQUISA JA SAI APARAFUSADA (o `Bolted = 1` do tipo no DM); o resto nasce solto. Ver `Interacoes.NasceAparafusada`.
			Aparafusada = Interacoes.NasceAparafusada(c.Id),

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
		// (2026-10-19) A MAQUINA RECOLHIDA COM AS MELHORIAS volta como estava: o estado sai da gaveta do item. Ver `DevolverAsMelhorias`.
		bool voltou = DevolverAsMelhorias(obra, item.Id);
		_noChao.Add(obra);
		GravarMundo();

		GD.Print($"[server] {pl.Name} {(plantio ? "plantou" : "assentou")} {c.Nome} (#{obra.Id}) em {obra.Zona} @ ({x:0},{y:0})"
				 + (voltou ? " -- com o estado da gaveta" : ""));
		Avisar(pl, plantio
			? "você planta a semente. Uma macieira nasce ali -- já dá pra colher."
			: $"você assenta {c.Nome} no chão{(obra.Aparafusada ? ", já aparafusada" : "")}"
			  + (voltou ? ", com as melhorias e os ajustes que ela tinha." : "."));
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

		// ============================ POTE COM GENTE DENTRO NAO SE CARREGA ============================
		// Desvio DECLARADO do original, e a razao e de identidade: no DM da pra por o pote selado na
		// mochila e o `checkdur` reescreve o ponto de volta do preso pro lugar do pote (`Sealing.dm:122`).
		// Aqui um pote no chao e uma `Obra` COM ID (gravada no `mundo.json`) e um pote na mochila e um
		// item de pilha, sem identidade nenhuma -- recolher apagaria o vinculo `Selo.PoteId` e deixaria
		// um preso sem carcere, com o selo corroendo por horas ate soltar sozinho.
		//
		// O que se perde e poder ANDAR com o prisioneiro; o que se mantem e a interacao que importa,
		// que e quebrar o pote pra solta-lo. Ver `GameServer.Selo.cs`.
		if (PoteEstaSelado(o)) { Avisar(pl, "há alguém selado aí dentro -- isso não sai do lugar."); return; }
		// (2026-10-25) o dono -- ou o Neo Tuffle no corpo dele --, ou qualquer um com ela liberada: ver `MexeComoDono`
		if (!MexeComoDono(o, pl, semDonoEDeTodos: true))
		{ Avisar(pl, $"{NomeDaObra(o)} é de {o.DonoNome}."); return; }
		if (pl.Mochila.Cheio) { Avisar(pl, "sua mochila está cheia."); return; }
		// (v0.2.1) COM COISA DENTRO NAO SE RECOLHE: o que ela guardava sumiria com ela -- a gaveta da maquina recolhida
		// (`IdAoRecolher`) leva o estado dela, e nao os itens. Ver `Deposito`.
		if (o.Tech is { Itens.Count: > 0 }) { Avisar(pl, $"esvazie {NomeDaObra(o)} antes de recolher."); return; }
		// (v0.2.1) A LUA ACESA NAO SE RECOLHE: ela arde onde foi acesa, ate se apagar e sumir.
		if (LuaArtificial.EhFonte(o.Tipo) && o.Tech?.Num(LuaArtificial.ChaveAcesaAte) > 0)
		{ Avisar(pl, $"{NomeDaObra(o)} está acesa: não dá pra recolher."); return; }

		// A DRILL NAO PERDE NADA AO SER RECOLHIDA: o estoque vai pro saldo e o nivel fica com quem a recolhe.
		// Ver `GameServer.Drill.AoRecolherADrill`.
		if (o.Tipo == Drill.Tipo) AoRecolherADrill(pl, o);

		// (2026-10-19) AS OUTRAS TAMBEM NAO: a maquina mexida volta pra mochila como um item com estado, e o estado dela fica
		// numa gaveta do cofre ate ela ser assentada de novo (dono: *"todos os upgrades sao removidos e vc tem q pagar todo
		// o upgrade dnv"*). A de fabrica volta com o id cru. Ver `IdAoRecolher` e `MaquinaGuardada`.
		string recolhida = IdAoRecolher(o);
		_noChao.Remove(o);
		GravarMundo();
		Guardar(pl, recolhida);

		// A COLISAO E REFEITA PELA LISTA INTEIRA (ver `AplicarColisaoDasObras`): tirar a obra sem
		// isto deixaria a parede dela no lugar, e o jogador levaria correcao num ponto vazio.
		AplicarColisaoDasObras(pl.Zone);
		Avisar(pl, $"você recolhe {NomeDaObra(o)}.");
		if (recolhida != o.Tipo) Avisar(pl, "as melhorias e os ajustes dela vão junto: assentada de novo, ela volta como estava.");
		MandarObras(pl.Zone);
	}


	private static string MotivoObra(RecusaObra r, Construcao c, ServerPlayer pl) => r switch
	{
		RecusaObra.SemTech => $"{c.Nome} pede {c.Tech:0} de tecnologia -- voce tem {pl.Ficha.techskill:0}.",
		RecusaObra.SemZeni => $"{c.Nome} custa {c.Custo:N0} zeni -- voce tem {pl.Ficha.Zeni:N0}.",
		RecusaObra.SemRecursos => $"{c.Nome} custa {Recurso.CustoEmRecursos(c.Custo):N0} recursos -- voce tem {pl.Ficha.Recursos:N0}.",
		RecusaObra.RacaErrada => $"{c.Nome} nao e coisa de {Jandirus.Core.Races.NomeDaRaca.Bonito(pl.Race)}.",
		RecusaObra.ForaDoPort => $"{c.Nome} ainda não existe neste jogo: pede {TechFechada.Falta}. Nada foi cobrado.",
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
		// (2026-10-25) ...e o que o dono liberou, e o do corpo que o Neo Tuffle dirige: ver `MexeComoDono`
		if (o.DonoConta != pl.Conta && !MexeComoDono(o, pl, semDonoEDeTodos: false)) { Avisar(pl, "isso nao e seu."); return; }

		o.Aparafusada = !o.Aparafusada;
		GravarMundo();
		Avisar(pl, o.Aparafusada
			? $"voce aparafusa {NomeDaObra(o)} no chao."
			: $"voce solta {NomeDaObra(o)} do chao.");
		MandarObras(pl.Zone);
	}

	// =====================================================================
	// (2026-10-25) QUEM MEXE NA TECH COMO DONO -- o pedido 74
	// =====================================================================
	/// <summary>
	/// A CONTA DONA E A DESTE CORPO -- a dele mesmo, ou (o dono: *"Faca q personagens q viraram neotuffle ou bio, consigam usar os itens tech
	/// do personagem anterior antes dele ser substituido"*) a do corpo que o Neo Tuffle esta dirigindo: dentro do hospede, a tech do hospede
	/// responde a ele como responderia ao dono. O bio nao precisa de nada aqui: ele e o MESMO save, com a mesma conta (`NascerBioAndroide`).
	///
	/// E a pergunta das TRAVAS que so o dono de verdade poe -- a senha da clonagem e da Capital Ship, a cifra do supercomputador: a obra
	/// LIBERADA nao entra nela, senao qualquer um trancaria a maquina do dono pra sempre (a senha da clonagem e permanente).
	/// </summary>
	private bool EDaContaDona(string conta, ServerPlayer pl) =>
		conta.Length > 0
		&& (string.Equals(conta, pl.Conta, StringComparison.OrdinalIgnoreCase)
			|| (!TuffleSemATechDoHospedeDeTeste && PosseDoTuffleDe(pl.Id) is { } posse && posse.Tuffle == pl
				&& string.Equals(conta, posse.Hospede.Conta, StringComparison.OrdinalIgnoreCase)));

	/// <summary>
	/// MEXE NESTA OBRA COMO DONO? O dono (<see cref="EDaContaDona"/>), ou qualquer um se o dono a LIBEROU (`Obra.Liberada` -- o pedido: *"permita
	/// com q o criador da tech libere ela quando quiser pra qualquer um mexer nela, desparafusar, pegar, etc, tudo q o dono pode fazer"*). A
	/// obra SEM dono responde como cada porta ja respondia (<paramref name="semDonoEDeTodos"/>): o pegar e a Drill a davam a quem chegasse; o
	/// parafuso, o laboratorio e o deposito, a ninguem.
	/// </summary>
	private bool MexeComoDono(Obra o, ServerPlayer pl, bool semDonoEDeTodos) =>
		o.DonoConta.Length == 0 ? semDonoEDeTodos : (o.Liberada && !LiberadaNaoValeDeTeste) || EDaContaDona(o.DonoConta, pl);

	/// <summary>O mesmo, pra nave parada (`Nave.Liberada`): recolher, recondicionar, melhorar, e embarcar sem o codigo.</summary>
	private bool MexeNaNaveComoDono(Nave n, ServerPlayer pl, bool semDonoEDeTodos) =>
		n.DonoConta.Length == 0 ? semDonoEDeTodos : (n.Liberada && !LiberadaNaoValeDeTeste) || EDaContaDona(n.DonoConta, pl);

	/// <summary>DEFEITO INJETADO (`--v021teste`): a tech liberada continua so do dono -- o jogo de antes de 2026-10-25. Falso em jogo, sempre.</summary>
	internal static bool LiberadaNaoValeDeTeste;

	/// <summary>DEFEITO INJETADO (`--tuffleteste`): dentro do hospede, o Neo Tuffle nao mexe na tech dele -- o jogo de antes. Falso em jogo, sempre.</summary>
	internal static bool TuffleSemATechDoHospedeDeTeste;

	/// <summary>O que ouve quem pede pra liberar uma construcao que nao esta (mais) na zona dele.</summary>
	internal const string FraseDaObraLonge = "essa construção não está mais nesta zona.";

	/// <summary>
	/// LIBERAR / TRANCAR -- o botao da linha de cada construcao na aba Tech ("Suas construções nesta zona"). O argumento e o id do pacote das
	/// obras (`S2C.Construcoes`): positivo e obra, negativo e nave parada. So o dono troca (a conta, ou o Neo Tuffle no corpo dele), e de
	/// qualquer ponto da zona -- a aba so lista o que esta nela.
	/// </summary>
	private void LiberarObra(ServerPlayer pl, string arg)
	{
		if (!int.TryParse(arg.TrimStart('#'), out int id) || id == 0) { Avisar(pl, "isso não existe."); return; }
		if (id < 0)
		{
			Nave? n = _naves.FirstOrDefault(x => x.Id == -id);
			if (n == null || !n.Zona.Equals(pl.Zone)) { Avisar(pl, FraseDaObraLonge); return; }
			if (!EDaContaDona(n.DonoConta, pl)) { Avisar(pl, $"só o dono libera {NomeDaNave(n)}."); return; }
			n.Liberada = !n.Liberada;
			GravarNaves();
			Avisar(pl, n.Liberada
				? $"{NomeDaNave(n)} está liberada: qualquer um mexe nela como você -- recolhe, melhora, embarca."
				: $"{NomeDaNave(n)} volta a ser só sua.");
			MandarObras(pl.Zone);
			return;
		}
		Obra? o = _noChao.FirstOrDefault(x => x.Id == id);
		if (o == null || !o.Zona.Equals(pl.Zone)) { Avisar(pl, FraseDaObraLonge); return; }
		if (!EDaContaDona(o.DonoConta, pl)) { Avisar(pl, $"só o dono libera {NomeDaObra(o)}."); return; }
		o.Liberada = !o.Liberada;
		GravarMundo();
		Avisar(pl, o.Liberada
			? $"{NomeDaObra(o)} está liberada: qualquer um mexe nela como você -- aparafusa, solta, pega, usa."
			: $"{NomeDaObra(o)} volta a ser só sua.");
		MandarObras(pl.Zone);
	}

	/// <summary>
	/// A TECH DO HUMANO QUE MORREU FICA COM O BIO, NO NOME DELE -- o dono: *"meu amigo q virou bio n tava conseguindo usar o simulator dps q
	/// virou bio androide (simulator ele fez quando ainda era humano/androide)"*. A posse sempre foi da CONTA, e o bio e o mesmo save; mas o
	/// nome do dono que cada obra e cada nave guardam ficava o do morto: a aba Tech ("Suas construções") lista pelo nome e o deixava de fora,
	/// e a recusa a terceiros dizia de quem nao existe mais. Chamada pelo parto (`NascerBioAndroide`), com o nome de antes.
	/// </summary>
	private void PassarATechProNomeNovo(ServerPlayer pl, string antes)
	{
		int n = 0;
		foreach (Obra o in _noChao.Where(o => o.DonoNome == antes && string.Equals(o.DonoConta, pl.Conta, StringComparison.OrdinalIgnoreCase)))
		{ o.DonoNome = pl.Name; n++; }
		foreach (Nave x in _naves.Where(x => x.DonoNome == antes && string.Equals(x.DonoConta, pl.Conta, StringComparison.OrdinalIgnoreCase)))
		{ x.DonoNome = pl.Name; n++; }
		if (n == 0) return;
		GravarMundo();
		GravarNaves();
		MandarObras(pl.Zone);
	}

	/// <summary>
	/// DEFEITO INJETADO (`--naveteste`, camada 7): o `Posicionar` volta a assentar com o corpo ao leme -- no chao de FORA da nave, o jogo de
	/// antes de 2026-10-18. Falso em jogo, sempre.
	/// </summary>
	internal static bool ConstruirAoLemeDeTeste;

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
	/// (2026-10-25) **MENOS MEDITANDO, COM UM QUARTO DO GANHO** -- o dono: *"faca q tenha como estudar meditando sem research table, mas o ganho
	/// de tech e 1/4, so pra players em planetas isolados conseguirem sair de la"*. DIVERGENCIA DECLARADA do `Experiment()`: sem mesa por perto
	/// e meditando (`Ficha.med`), o laco rende <see cref="FracaoDoEstudoMeditando"/> do da mesa; parou de meditar longe da mesa, o estudo cai.
	/// O gesto e a telinha da tecla M ("Meditar estudando", o comando `estudar_meditando`).
	///
	/// Sem material pra sacrificar (o port ainda nao tem itens), o custo e TEMPO: a estacao rende
	/// por segundo enquanto a pessoa fica nela. O ganho passa pelo `techmod`, como la.
	/// </summary>
	private void TickDoEstudo()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			if (!pl.Estudando || pl.Ficha.KO || CaidoPelaMorte(pl)) continue;

			Obra? est = BancadaDeEstudoPerto(pl);
			bool meditando = pl.Ficha.med && !EstudoSoNaMesaDeTeste;
			if (est == null && !meditando)
			{
				pl.Estudando = false;
				Avisar(pl, FraseDoEstudoQueCaiu);
				continue;
			}

			// (2026-10-25) sem a mesa, meditando: um quarto -- ver o cabecalho
			int subiu = pl.Ficha.Estudar(est != null ? XpDeEstudoPorSegundo : XpDeEstudoPorSegundo * FracaoDoEstudoMeditando);

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

	/// <summary>(2026-10-25) QUANTO DO GANHO DA MESA O ESTUDO MEDITANDO RENDE, sem mesa nenhuma: um quarto (o dono). Ver `TickDoEstudo`.</summary>
	internal const double FracaoDoEstudoMeditando = 0.25;

	/// <summary>(2026-10-25) O que ouve quem pede o estudo meditando sem mesa por perto.</summary>
	internal const string FraseDoEstudoMeditando = "voce medita e estuda de cabeca: sem uma Research Station por perto, o estudo rende um quarto do que renderia nela.";

	/// <summary>(2026-10-25) O que ouve quem estava estudando e ficou sem mesa e sem meditar.</summary>
	internal const string FraseDoEstudoQueCaiu = "voce para de estudar: sem uma Research Station aparafusada por perto, so se estuda meditando (tecla M), com um quarto do ganho.";

	/// <summary>DEFEITO INJETADO (bancada `--cidadeteste`): so se estuda na mesa -- o jogo de antes de 2026-10-25. Falso em jogo, sempre.</summary>
	internal static bool EstudoSoNaMesaDeTeste;

	/// <summary>A Research Station aparafusada ao alcance deste corpo, ou nula -- a de estudar.</summary>
	private Obra? BancadaDeEstudoPerto(ServerPlayer pl) =>
		_noChao.FirstOrDefault(o => o.Tipo == "Research_Station" && o.Aparafusada && o.Zona.Equals(pl.Zone)
								 && Math.Abs(o.X - pl.Pos.X) <= AlcanceDeUso && Math.Abs(o.Y - pl.Pos.Y) <= AlcanceDeUso);

	/// <summary>
	/// (2026-10-25) "MEDITAR ESTUDANDO" (o botao da tecla M): liga o estudo -- e nao alterna, como o `estudar` da mesa: a meditacao e que o
	/// segura. Com mesa por perto e o estudo de sempre.
	/// </summary>
	private void ComecarAEstudarMeditando(ServerPlayer pl)
	{
		pl.Estudando = true;
		Avisar(pl, BancadaDeEstudoPerto(pl) != null ? "voce se debruca sobre a bancada." : FraseDoEstudoMeditando);
	}

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
	private void VirarAndroide(ServerPlayer pl, bool infinito, Moeda moeda)
	{
		if (ObraPerto(pl) is not { Aparafusada: true, Lab: 1 })
		{ Avisar(pl, "voce precisa estar dentro de um Android Lab."); return; }
		if (pl.Race != "Human") { Avisar(pl, "essa maquina foi feita pra fisiologia humana."); return; }
		if (pl.Ficha.techskill < TechDoLaboratorio)
		{
			Avisar(pl, $"instalar isso pede {TechDoLaboratorio:0} de tecnologia -- voce tem {pl.Ficha.techskill:0}.");
			return;
		}

		// EM ZENI OU, PELO DOBRO, EM RECURSOS (dono, 2026-10-10): a moeda vem no pedido -- ver `CobrarOuExplicar`.
		double custo = infinito ? CustoAndroideInfinito : CustoAndroideAbsorcao;
		if (!CobrarOuExplicar(pl, custo, moeda, "a conversão")) return;

		double piso = infinito ? BpAndroideInfinito : BpAndroideAbsorcao;
		pl.Ficha.BP = pl.Ficha.BP < piso ? piso : pl.Ficha.BP + piso;
		pl.Race = "Android";
		// (2026-10-18) A MESMA CARA: a lista de corpos do Androide e o `GochekAndroid` seguido dos tres corpos humanos (`DmAppearanceScanner.
		// Corpos`), entao o corpo humano `i` e o `i + 1` dela. Sem esta linha o convertido acordaria com a cara do GochekAndroid. E todo mundo
		// em volta fica sabendo pelo `TrocarAparencias` -- o pacote leva a raca, e a cara e raca + indice.
		pl.Visual.Corpo += 1;
		TrocarAparencias(pl);
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
		if (LabTrancadoPra(pl, lab)) return;

		ServerPlayer? vitima = _players.Values
			// (2026-10-18) menos o corpo escondido de quem o Neo Tuffle possui -- ver `GameServer.NeoTuffle.cs`
			.Where(o => o != pl && EhJogador(o) && o.Zone.Equals(pl.Zone) && (o.Ficha.KO || o.Ficha.dead) && o.PossuidoPor == 0)
			.OrderBy(o => Vec2.Distance(o.Pos, pl.Pos))
			.FirstOrDefault(o => Vec2.Distance(o.Pos, pl.Pos) <= AlcanceDeUso);

		// (v0.2.1) SEM NINGUEM CAIDO POR PERTO, O QUE SE COLHE E O QUE ESTA NO RECIPIENTE DE DNA DA MOCHILA -- o
		// `DNA_Container` do original e a ferramenta de coleta, e a amostra e trazida ao laboratorio dentro dele
		// (`DNALabs.dm:268-334`). Ver `DespejarOsRecipientesNoLab`.
		if (vitima == null)
		{
			if (lab.Fornada is { PrometidaEm: > 0 }) { Avisar(pl, "a gestacao ja comecou -- nao da pra acrescentar DNA."); return; }
			int doRecipiente = DespejarOsRecipientesNoLab(pl, lab);
			Avisar(pl, doRecipiente > 0
				? $"voce despeja {doRecipiente} amostra(s) do recipiente no tanque. Amostras: {lab.Fornada!.Amostras.Count}/{BioMaxDna}."
				: "nao ha ninguem caido ao seu alcance -- e nenhuma amostra nova num recipiente de DNA da sua mochila.");
			return;
		}

		lab.Fornada ??= new Gestacao { DonoConta = pl.Conta };
		if (lab.Fornada.PrometidaEm > 0) { Avisar(pl, "a gestacao ja comecou -- nao da pra acrescentar DNA."); return; }
		if (lab.Fornada.Amostras.Count >= BioMaxDna) { Avisar(pl, $"o tanque so comporta {BioMaxDna} amostras."); return; }

		// UMA AMOSTRA POR PESSOA. `DNL_BIO_MAX_DNA` e quatro, e sem esta linha o tanque enchia com
		// quatro agulhadas no MESMO nocauteado -- que anula a mecanica inteira (o preco do sistema e
		// derrubar gente forte, no plural).
		if (lab.Fornada.Amostras.Any(a => a.Assinatura == vitima.Assinatura))
		{ Avisar(pl, $"o tanque ja tem uma amostra de {vitima.Name}."); return; }

		Amostra amostra = AmostraDe(vitima);
		lab.Fornada.Amostras.Add(amostra);
		GravarMundo();

		Avisar(pl, $"voce colhe DNA de {vitima.Name} ({Jandirus.Core.Races.NomeDaRaca.Bonito(vitima.Race)}) -- {amostra.Skills.Count} tecnica(s) "
				   + $"gravada(s) no tecido. Amostras: {lab.Fornada.Amostras.Count}/{BioMaxDna}.");
		Avisar(vitima, "alguem enfia uma agulha em voce enquanto voce esta caido.");
	}

	/// <summary>
	/// COMECA A GESTACAO. Um mes in-game, e destruir o laboratorio cancela.
	/// </summary>
	private void GestarBio(ServerPlayer pl)
	{
		Obra? lab = LabDeBio(pl);
		if (lab != null && LabTrancadoPra(pl, lab)) return;
		if (lab?.Fornada == null || lab.Fornada.Amostras.Count == 0)
		{ Avisar(pl, "o tanque esta vazio -- colha DNA primeiro."); return; }
		if (lab.Fornada.PrometidaEm > 0)
		{
			double falta = (lab.Fornada.PrometidaEm - NowMs()) / 1000.0;
			Avisar(pl, $"ja esta gestando. Faltam {falta / 60:0.#} minutos.");
			return;
		}

		// ============================ A FORNADA SE CONSOLIDA AQUI, E NAO NA AGULHA ============================
		// `Topic()` do original (`DNALabs.dm:422-444`) faz exatamente estas tres contas no instante em
		// que a gestacao COMECA, e depois **destroi as amostras**. Faze-las na coleta seria outra
		// mecanica: o BP do doador mais forte tem que ser o de HOJE, e nao o do dia da agulhada.
		// ==================================================================================================
		Gestacao g = lab.Fornada;

		// 1) `brew_strongest_bp` -- e o BP ATUAL de quem estiver online (`:430-434`). Colher DNA de
		//    alguem cedo e esperar essa pessoa treinar PAGA: o laboratorio nao rouba uma foto.
		g.MaiorBp = 0;
		foreach (Amostra a in g.Amostras)
		{
			ServerPlayer? vivo = _players.Values.FirstOrDefault(
				o => o.Assinatura.Length > 0 && o.Assinatura == a.Assinatura);
			g.MaiorBp = Math.Max(g.MaiorBp, vivo?.Ficha.BP ?? a.Bp);
		}

		// 2) `brew_has_saiyan` -- QUALQUER doador de sangue Saiyajin serve, puro ou meio (`:435`
		//    testa `"Saiyan"` e `"Half-Saiyan"`). `Catalogo.EhSaiyajin` cobre os dois mais o
		//    `"Halfbreed"` do `races.json`, e e o MESMO predicado que decide quem tem a escada --
		//    duas respostas pra "tem sangue Saiyajin?" e como um bio nasce com `canSSJ` e sem escada.
		// (v0.3.0) pelo SANGUE da amostra (`SangueDaAmostra`): o meio-sangue de pai Saiyajin e o bio colhido com sangue
		// Saiyajin contam -- a mesma pergunta que da o Zenkai ao bio (`SangueDoBio.TemSaiyajin`).
		g.TemSaiyajin = g.Amostras.Any(a => SangueDaAmostra(a).Any(Jandirus.Core.Forms.Catalogo.EhSaiyajin));

		// 3) `brew_verbs` -- a UNIAO SEM DUPLICATAS (`:436-437`). Quatro doadores que sabem a mesma
		//    skill dao uma skill, nao quatro. (v0.3.0: o livro inteiro de cada um -- ver `Amostra.Skills`.)
		g.Tecnicas = [.. g.Amostras.SelectMany(a => a.Skills).Distinct(StringComparer.OrdinalIgnoreCase)];

		// O CRIADOR E QUEM FECHA O TANQUE -- `creator_sig = usr.signature` (`DNALabs.dm:441`), o personagem e nao
		// so a conta: ver `Gestacao.DonoAssinatura`.
		g.DonoConta = pl.Conta;
		g.DonoAssinatura = pl.Assinatura;

		g.PrometidaEm = NowMs() + (long)(GestacaoSegundosReais * 1000);
		GravarMundo();

		GD.Print($"[server] {pl.Name} iniciou gestacao de bio-androide "
				 + $"({string.Join("+", g.Amostras.Select(a => a.Raca))}) -- BP alvo {g.MaiorBp * BioFracaoDoDoador:N0}, "
				 + $"saiyajin={g.TemSaiyajin}, tecnicas={g.Tecnicas.Count}");

		Avisar(pl, $"o tanque se fecha. A criatura leva um mes pra ficar pronta "
				   + $"({GestacaoSegundosReais / 60:0} minutos reais). Nao deixe destruirem o laboratorio.");
		Avisar(pl, g.TemSaiyajin
			? "no meio da sopa de celulas ha DNA SAIYAJIN -- o que sair dali vai poder ir alem."
			: "nenhuma das amostras tem sangue Saiyajin. O que sair dali sera forte, mas nao vai passar da perfeicao.");

		// O FOLEGO NO VACUO, dito na hora de fechar o tanque pelo mesmo motivo do sangue Saiyajin: e
		// AQUI que o jogador ainda poderia ter escolhido outro doador. A frase nomeia o doador em vez
		// de nomear as racas -- lista de raca escrita em texto e a lista escrita pela segunda vez, e
		// ela mentiria calada no dia em que o dono mudasse a dele (ver `PulmaoDaFornada`).
		Amostra? pulmao = PulmaoDaFornada(g);
		Avisar(pl, pulmao != null
			? $"e ha um pulmao de vacuo na sopa: o DNA de {pulmao.Doador} ({SangueDe(pulmao)}) aguenta o "
			  + "espaco, e o que sair dali vai aguentar tambem."
			: "e nenhuma das amostras aguenta o vacuo -- o que sair dali sufoca no espaco como qualquer um.");
	}

	private Obra? LabDeBio(ServerPlayer pl)
	{
		Obra? o = ObraPerto(pl);
		return o is { Aparafusada: true, Lab: 2 } ? o : null;
	}

	/// <summary>
	/// O PAINEL DO BIO-ANDROID LAB E DE QUEM O ERGUEU -- `if(usr.signature != owner_sig) "O painel esta trancado
	/// no DNA de [owner_name]."` no `Click()` (`DNALabs.dm:371-373`) e de novo no `Topic()` (`:414`). Devolve se
	/// recusou (e ja disse por que).
	///
	/// AS DUAS PORTAS DO TANQUE (colher e gestar) estavam abertas a quem chegasse perto, e o tanque decide QUEM A
	/// CRIATURA MATA: a primeira agulha de um estranho fazia dele o criador no laboratorio alheio, e qualquer
	/// passante fechava o tanque de outra pessoa -- na hora dele, com o DNA dele, e era o DONO da fornada que
	/// perdia o personagem.
	/// </summary>
	private bool LabTrancadoPra(ServerPlayer pl, Obra lab)
	{
		if (LabSemDonoDeTeste || string.Equals(lab.DonoConta, pl.Conta, StringComparison.OrdinalIgnoreCase)) return false;
		Avisar(pl, $"o painel está trancado no DNA de {(lab.DonoNome.Length > 0 ? lab.DonoNome : "outra pessoa")}.");
		return true;
	}

	/// <summary>
	/// ESTE CORPO E O CRIADOR DESTA FORNADA -- o que a criatura mata ao nascer? `P.signature == creator_sig`
	/// (`DNALabs.dm:364`): o PERSONAGEM que fechou o tanque. So pela conta (como era), outro personagem do mesmo
	/// jogador -- outro slot -- logado na hora do parto era quem virava o bio. Fornada sem assinatura (gravada
	/// antes de 2026-10-11, ou fechada por corpo sem slot) continua valendo pela conta.
	/// </summary>
	private bool EhOCriadorDaFornada(Gestacao g, ServerPlayer p) =>
		p.Conta == g.DonoConta
		&& (LabSemDonoDeTeste || g.DonoAssinatura.Length == 0 || p.Assinatura == g.DonoAssinatura);

	/// <summary>
	/// ============================ O PULMAO DA FORNADA -- QUAL DOADOR RESPIRA NO VACUO ============================
	/// Devolve a PRIMEIRA amostra cujo sangue aguenta o espaco, ou `null` se nenhuma aguenta. Pedido
	/// do dono, literal: *"bio androides pegam a capacidade de respirar no espaco caso uma das racas
	/// q esta em seu dna consiga"*. **Uma basta** -- quatro doadores e a resposta e um OU, do mesmo
	/// jeito que o `brew_has_saiyan` acende com um Saiyajin so (`DNALabs.dm:435`).
	///
	/// **A PERGUNTA NAO E UMA LISTA, E ELA E A DO CORE.** Quem responde "esta raca respira" e
	/// <see cref="Vacuo.RespiraNoVacuo"/> -- a MESMA funcao que decide, uma vez por segundo, se o
	/// corpo perde vida no vacuo. Escrever aqui um `a.Raca == "Majin" || a.Raca == "Icer"` seria a
	/// segunda lista de raca deste port, e a primeira a envelhecer no dia em que o dono mudar a dele:
	/// e exatamente o erro que a regeneracao por raca cometeu nos dois sentidos.
	///
	/// Os dois ultimos parametros ficam nos padroes (`false`) de proposito: **cargo e traje nao estao
	/// no DNA**. Um Deus da Destruicao colhido nao passa o folego do trono pro filho de tanque, e uma
	/// roupa espacial na mochila do doador nao vira pulmao -- so o SANGUE atravessa a agulha.
	///
	/// E devolve a amostra e nao um `bool` porque quem chama diz o NOME do doador ao jogador: "o DNA
	/// de Fulano (Majin) respira no espaco" e uma frase que se le sem o codigo do lado; "true" nao.
	/// =========================================================================================================
	/// </summary>
	private static Amostra? PulmaoDaFornada(Gestacao g) =>
		g.Amostras.FirstOrDefault(a => SangueDaAmostra(a).Any(r => Vacuo.RespiraNoVacuo(r)));

	/// <summary>
	/// O SANGUE DE UMA AMOSTRA, EM UMA PALAVRA -- "Majin", ou "Human/Majin" no meio-sangue.
	///
	/// Existe porque a linha que o jogador le mentia por omissao: um doador meio-Majin tem
	/// `Raca = "Human"`, e "o DNA de Fulano (Human) aguenta o espaco" e uma frase que so confunde --
	/// quem lesse aquilo concluiria que humano respira. Quando o pai e que responde, ele aparece.
	/// </summary>
	private static string SangueDe(Amostra a) =>
		a.RacaDoPai.Length > 0 && !string.Equals(a.RacaDoPai, a.Raca, StringComparison.OrdinalIgnoreCase)
			? $"{a.Raca}/{a.RacaDoPai}" : a.Raca;

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
		// (2026-10-18) O NEO TUFFLE LAB E DOS TSUFURUJINS, e os outros dois dos humanos -- ver `GameServer.NeoTuffle.cs`.
		if (qual == LabDoNeoTuffle && pl.Race != RacaDoTuffle) { Avisar(pl, "o Neo Tuffle Lab pede um Tsufurujin."); return; }
		if (qual != LabDoNeoTuffle && pl.Race != "Human") { Avisar(pl, "a instalacao pede um humano."); return; }
		if (pl.Ficha.techskill < TechDoLaboratorio)
		{
			Avisar(pl, $"instalar pede {TechDoLaboratorio:0} de tecnologia -- voce tem {pl.Ficha.techskill:0}.");
			return;
		}

		o.Lab = qual;
		GravarMundo();
		Avisar(pl, qual == 1 ? "o mainframe vira um Android Lab. Agora da pra entrar nele."
			: qual == LabDoNeoTuffle ? "o mainframe vira um Neo Tuffle Lab. Agora dá pra entrar nele."
			: "o mainframe vira um Bio-Android Lab. Agora falta o DNA.");
		MandarObras(pl.Zone);
	}

	/// <summary>
	/// A GESTACAO TERMINOU.
	///
	/// ============================ "A CRIATURA MATA O CRIADOR" E UMA FRASE, NAO DUAS PESSOAS ============================
	/// Este metodo dizia por escrito que a troca de corpo era o passo que faltava, porque "a criacao
	/// de personagem passa pela tela de slots". **A leitura estava errada, e o original resolve isso
	/// sem criar personagem nenhum:** `dnl_bio_hatch` (`DNALabs.dm:449-500`) opera sobre o
	/// **MESMO mob** -- `creator.Race = "Bio-Android"`, `creator.genome = null`, `creator.BP = ...`.
	/// A morte e da PERSONA: o jogador nao perde o slot, ele perde quem ele era.
	///
	/// Por isso nao ha slot novo aqui, nao ha segundo caminho de criacao e nao ha nada pra validar:
	/// e o mesmo save, o mesmo peer e o mesmo corpo, reescritos. O que morre e o nome, a raca, o
	/// genoma, a classe, o cabelo e as arvores de skill.
	/// ================================================================================================================
	///
	/// O CRIADOR OFFLINE ADIA O PARTO, e isso e literal (`:355-367` espera `creator.client`). Sem
	/// isso havia uma saida: deslogar antes da hora e voltar depois com o laboratorio sumido e o
	/// personagem intacto -- pagar meio milhao pra escapar da propria sentenca.
	/// </summary>
	private void TickDaGestacao()
	{
		long agora = NowMs();
		foreach (Obra lab in _noChao.ToList())
		{
			if (lab.Fornada is not { PrometidaEm: > 0 } g || agora < g.PrometidaEm) continue;

			ServerPlayer? criador = _players.Values.FirstOrDefault(p => p.Peer != null && EhOCriadorDaFornada(g, p));

			// ELE PRECISA ESTAR ONLINE **E VIVO**. Morto tambem espera: no original o nascimento e o
			// instante em que a criatura o mata, e nao da pra matar quem ja esta morto -- o parto
			// fica aguardando o corpo se levantar. O tanque nao estraga.
			if (criador == null || criador.Ficha.dead) continue;

			NascerBioAndroide(criador, lab, g);
		}
	}

	/// <summary>
	/// O NASCIMENTO. Porte de `dnl_bio_hatch` (`DNALabs.dm:449-500`), passo a passo e na mesma ordem.
	///
	/// ============================ O QUE VEM DA **PESSOA** E O QUE VEM DA **RACA** ============================
	/// A decisao esta escrita aqui porque foi ela que o dono pediu por extenso ("pegar as SKILLS,
	/// HABILIDADES RACIAIS etc das PESSOAS e RACA q ele tem o dna"), e porque o original entrega
	/// **muito menos** do que a frase sugere. O que o DM faz, medido linha a linha:
	///
	/// DA PESSOA (do doador individual), duas coisas e so duas:
	///   * **o BP** -- metade do BP do doador mais FORTE (`:471`), o atual se ele estiver online;
	///   * **as tecnicas** -- a uniao dos `Keyableverbs` dos ate quatro doadores (`:483-489`).
	///
	/// DA RACA, **uma** coisa: o teste de string `"Saiyan"`/`"Half-Saiyan"` (`:435`), que vira
	/// `canSSJ` + o SSJ1 ja possuido + o Zenkai anunciado.
	///
	/// O QUE **NAO** ATRAVESSA, e o DM e explicito nos quatro: os STATS (o genoma e destruido e
	/// reconstruido como Bio-Android puro, `:460`), as ARVORES RACIAIS (`generatetrees` despacha por
	/// `Parent_Race`, que agora e Bio-Android -- um bio feito de DNA Namekuseijin NAO regenera como
	/// Namekuseijin), as HABILIDADES RACIAIS ativas de outras racas, e a CLASSE (`:463` forca
	/// "None", o tipo Cell).
	///
	/// **ONDE EU ESCOLHI, E O QUE ESCOLHI:** o DM copia VERBS soltos; o port nao tem verb solto, o
	/// que ele tem e skill no livro. Copiei o typepath das skills que DECLARAM verb (ver
	/// `TecnicasHerdaveis`) e marquei todas como ENSINADAS -- ou seja, o bio sabe usa-las e **nao
	/// pode repassa-las**. Isso nao esta no DM. Esta aqui porque a alternativa transforma o
	/// laboratorio numa lavanderia de skill: derrube quatro especialistas, gere um bio e ele
	/// distribui o repertorio inteiro do servidor. Memoria muscular vinda de DNA nao e entendimento.
	/// =========================================================================================================
	/// </summary>
	private void NascerBioAndroide(ServerPlayer pl, Obra lab, Gestacao g)
	{
		string antes = pl.Name;
		double bp = Math.Max(Math.Round(g.MaiorBp * BioFracaoDoDoador), 1);

		// --- o tanque se rompe ANTES do resto -------------------------------
		// Primeiro porque o original abre com isso, e segundo por seguranca: tudo abaixo mexe no
		// jogador, e um `return` no meio deixaria uma fornada pronta pra nascer de novo no proximo
		// segundo -- um bio novo por tique.
		lab.Fornada = null;
		_noChao.Remove(lab);
		GravarMundo();
		AplicarColisaoDasObras(lab.Zona);
		MandarObras(lab.Zona);

		AnunciarNoMundo($"O tanque do Bio-Android Lab de {antes} se rompe. A CRIATURA DESPERTOU -- "
						+ "e a primeira coisa que ela fez foi matar o proprio criador.");

		// --- a persona morre -------------------------------------------------
		pl.Forma.Entrar(Jandirus.Core.Forms.Catalogo.IdBase);   // `Revert()`
		DerrubarBuffs(pl);                                      // `clearbuffs()`

		pl.Name = $"Bio-Androide de {antes}";
		// (2026-10-25) ...e a tech do humano que ele era passa pro nome novo -- ver `PassarATechProNomeNovo`
		PassarATechProNomeNovo(pl, antes);
		pl.Race = Jandirus.Core.Races.BioAndroids.Raca;
		pl.Class = "";                      // `Class = "None"` -- o tipo Cell
		pl.Ficha.Race = pl.Race;
		pl.Ficha.ParentRace = pl.Race;
		pl.Ficha.Class = "";
		pl.Ficha.SaiyanLineage = "";
		pl.Ficha.Genoma = null;             // `genome = null`: nenhum stat de doador atravessa

		// AS ARVORES SAO REFEITAS DO ZERO -- `generatetrees(1)` + `generatetrees(0)`. O livro e um
		// objeto novo e nao um `Clear()`: marcos, escolhas e skills ensinadas do humano que ele era
		// morrem junto com ele, e um `Clear()` esqueceria algum desses tres.
		pl.Livro = new Jandirus.Core.Skills.SkillBook();
		pl.Livro.Conceder(MarcosIniciais);
		// (v0.3.0) E AS TECNICAS CUSTOMIZADAS DELE, pelo mesmo motivo: sao do humano que morreu.
		pl.Customizadas.Clear();
		pl.Mesa = null;

		// --- e o que sobrou dos doadores: o LIVRO de cada um, no nivel dele, e as tecnicas que eles inventaram ---
		// (v0.3.0, o dono: *"todas as skills da aba learn [...] do mesmo nivel q eles, e as tecnicas customizadas tb e as
		// liberadas"*) -- ver `GameServer.SangueDoBio.cs`.
		int herdadas = HerdarOLivroDosDoadores(pl, g);
		int customizadas = HerdarAsCustomizadas(pl, g);

		// --- o corpo ---------------------------------------------------------
		pl.Ficha.BP = bp;
		pl.Ficha.bio_lab_born = true;
		pl.Ficha.bio_stage = Jandirus.Core.Races.BioAndroids.Larva;
		pl.Ficha.bio_marco = 0;
		pl.Ficha.bio_passos = 0;
		pl.Ficha.bio_tabela = 1;
		pl.Ficha.bio_saiyan_dna = g.TemSaiyajin;

		// ============================ O FOLEGO NO VACUO VEM DO DNA -- E TEM QUE SER GRAVADO AQUI ============================
		// *"bio androides pegam a capacidade de respirar no espaco caso uma das racas q esta em seu
		// dna consiga"*. Esta e a UNICA janela em que a pergunta tem resposta: a fornada acabou de ser
		// destruida (`lab.Fornada = null`, la em cima), o `Genoma` vira nulo e a `ParentRace` vira
		// "BioAndroid" -- daqui a duas linhas nao existe mais no mundo quem soubesse os doadores.
		//
		// Quem responde continua sendo o Core (`Vacuo.RespiraNoVacuo`, via `PulmaoDaFornada`): este
		// campo e uma MEMORIA da pergunta, e nao uma segunda regra.
		// ==============================================================================================================
		Amostra? pulmao = PulmaoDaFornada(g);
		pl.Ficha.bio_dna_respira = pulmao != null;

		// (v0.3.0) E O SANGUE INTEIRO, pela mesma janela: as racas doadoras (as passivas e as arvores raciais do bio -- ver
		// `SangueDoBio`) e o motor de energia infinita de um androide doador.
		pl.Ficha.bio_racas_dna = Jandirus.Core.Races.SangueDoBio.DaFornada(g.Amostras.Select(SangueDaAmostra));
		pl.Ficha.bio_dna_infinito = g.Amostras.Any(a => a.Infinito);

		pl.Ficha.form3cantrevert = false;
		pl.Ficha.bio_ssj2_by_death = false;
		pl.Ficha.bio_larva = false;   // (2026-10-17) a larva de nascenca e o degrau Larva, e nao a vestida
		pl.Ficha.bio_nucleo = "";      // (2026-10-17) o nucleo se escolhe na criacao -- os botoes acendem agora (`GameServer.Nucleo.cs`)
		pl.Ficha.bio_nucleo_usos = 0;

		// A ASCENSAO MORRE COM ELE. `NoAscension = 1` (`statbiodroid.dm:2`) e o `BPBoost = 1` do
		// re-hook de login (`DNALabs.dm:709-711`): o bio e raca de FORMAS, entao ele nao acumula o
		// multiplicador de Ascensao (~317x) que um humano velho pode ter no save. Sem esta linha, o
		// bio nasceria com o boost do criador por cima do BP do doador mais forte.
		pl.Ficha.BPBoost = 1;

		// --- o DNA Saiyajin ---------------------------------------------------
		if (g.TemSaiyajin)
		{
			// `canSSJ = 1` -- o bypass que abre a escada Saiyajin inteira, na versao nerfada
			// (ver `SangueDiluido`). `hasssj = 1` -- ele ja NASCE com o SSJ1 possuido, sem despertar
			// por raiva: transforma assim que o BP alcancar a porta. Maestria em zero.
			pl.Ficha.canSSJ = true;
			pl.Forma.Liberar("ssj1");
		}

		// --- a aparencia ------------------------------------------------------
		// CARECA, sempre (`RemoveHair()` + `hair = "Bald"`, e `HairObject.dm:168` bloqueia cabelo
		// pro bio em TODA forma -- nem o Super Saiyajin poe cabelo nele, so aura e raios).
		pl.Visual.Cabelo = "Bald";
		pl.Visual.CorCabelo = null;
		pl.Visual.Corpo = Jandirus.Core.Races.BioAndroids.IndiceDoCorpo(pl.Ficha.bio_stage);
		pl.Visual.FormasDeFrost.Clear();
		pl.Visual.Roupa.Clear();            // a carapaca E a roupa dele
		_visual?.Sanear(pl.Visual, pl.Race, pl.Genero);

		// --- e o corpo passa a ser outro corpo --------------------------------
		// (v0.3.0) AS SKILLS HERDADAS ENTRAM PELA PORTA DE QUEM APRENDE: os poderes que elas dao, e os efeitos delas na ficha --
		// o `AplicarEfeitos` faz o `Statify`, refaz o perfil de cura pelo sangue novo (`EixoDeRegen`) e manda o pacote das
		// skills. E a mesma dupla do fim de `AprenderSkill`.
		AplicarPoderes(pl);
		AplicarEfeitos(pl);
		AplicarForma(pl);                   // reescreve ssjBuff/trueKiMod e chama `RepercutirPoder`
		pl.Ficha.Ki = pl.Ficha.MaxKi;        // `Ki = MaxKi`: virar bio poe o Ki em 100% EXATOS
		pl.Combate?.Corpo.Restaurar();
		pl.Combate?.SincronizarVida();
		pl.SigAtributos = "";
		TrocarAparencias(pl);
		MandarCustomizadas(pl);
		Persistir(pl);

		GD.Print($"[server] BIO-ANDROIDE nasceu: {antes} -> {pl.Name} | BP {bp:N0} | "
				 + $"saiyajin={g.TemSaiyajin} | vacuo={pl.Ficha.bio_dna_respira} | sangue={Jandirus.Core.Races.SangueDoBio.Texto(pl.Ficha)}"
				 + $"{(pulmao != null ? $" (por {pulmao.Doador}/{SangueDe(pulmao)})" : "")} | "
				 + $"{herdadas} skill(s) e {customizadas} tecnica(s) customizada(s) herdada(s)");

		Avisar(pl, "a criatura atravessa o seu peito com a cauda. Voce morre... e seus olhos "
				   + "continuam abertos, agora DENTRO dela.");
		Avisar(pl, "Você abre os olhos pela primeira vez. Fraco. Uma LARVA -- o seu poder está selado na carapaça: você expressa 5, e nem o "
				   + "sense nem o scouter te acham. Enterre-se (Enterrar-se) e fique "
				   + $"{Jandirus.Core.Races.BioAndroids.SegundosEnterradoPraAmadurecer / 60:0} min debaixo da terra: a carapaça se rompe.");
		Avisar(pl, "escolha onde mora o seu NÚCLEO (aba Skills): braço esquerdo, braço direito, perna esquerda, perna direita ou cabeça. É "
				   + "uma vez só -- e é ele que te reergue da morte.");
		// (2026-10-17) AS SKILLS DO DNA (o adendo do dono) -- ver `GameServer.SkillsDoDna.cs`
		int vagas = Jandirus.Core.Races.SkillsRaciais.AindaEscolhe(pl.Ficha);
		if (vagas > 0)
			Avisar(pl, $"o seu sangue guarda as skills das raças que te fizeram: escolha {vagas} delas na aba Skills (\"DNA: ...\"). Cada escolha é "
					   + "pra sempre.");
		if (herdadas > 0)
			Avisar(pl, $"memórias fluem do DNA: você nasce sabendo {herdadas} skill(s) dos seus doadores, no nível em que eles "
					   + "as tinham -- e não sabe ENSINAR nenhuma delas.");
		if (customizadas > 0)
			Avisar(pl, $"...e {customizadas} técnica(s) que eles mesmos inventaram: estão entre as suas, e não se repassam.");
		if (g.TemSaiyajin)
			Avisar(pl, "celulas SAIYAJIN pulsam no seu nucleo: o Super Saiyajin ja corre no seu DNA "
					   + "(maestria zero -- treine a forma).");
		if (pulmao != null)
			Avisar(pl, $"e voce nao precisa de ar: o sangue de {pulmao.Doador} ({SangueDe(pulmao)}) "
					   + "atravessa o vacuo, e agora e o seu.");
		// (v0.3.0) AS PASSIVAS, SO AS QUE O SANGUE TROUXE -- sozinho, o bio nao tem nenhuma (ver `SangueDoBio`). Era aqui que
		// morava o "seu corpo aprende com a derrota. Zenkai." de todo bio.
		foreach ((string frase, Jandirus.Net.Protocol.Fala tom) in FrasesDoSangue(pl)) Avisar(pl, frase, tom);
	}

	/// <summary>
	/// AS REGRAS RETROATIVAS DO BIO E A LARVA ENTERRADA, a cada segundo.
	///
	/// (2026-10-17) A LARVA SO AMADURECE ENTERRADA (o adendo do dono -- ver `GameServer.Larva.cs`). Era o `dnl_larva_mature()` do DM
	/// (`DNALabs.dm:503-518`): um dia in-game na superficie e a carapaca rompia sozinha. A porta do rompimento continua a mesma
	/// (`SubirDegrauDoBio`); o que mudou e quem a abre.
	///
	/// ============================ O ORIGINAL TEM DUAS VIAS PORQUE A DELE MORRE ============================
	/// La ha um `spawn dnl_larva_watch()` (um laco que dorme) **e** um fallback dentro do
	/// `powerlevel()`, e o comentario diz por que: *"o watcher morre com uma morte/runtime e so
	/// re-armava no login"*. Aqui nao ha laco pra morrer -- quem pergunta e o
	/// <see cref="TickDaGestacao"/>, que e um tique de servidor e nao um `spawn` por jogador. Uma
	/// via so, e ela nao tem como parar.
	/// ==================================================================================================
	/// </summary>
	private void TickDaLarva()
	{
		long agora = NowMs();
		foreach (ServerPlayer pl in _players.Values)
		{
			Fighter f = pl.Ficha;

			// ============================ AS REGRAS RETROATIVAS -- `dnl_login_check` (`DNALabs.dm:705-728`) ============================
			// Elas moram no TIQUE e nao num gancho de login, e a diferenca e a que o proprio original
			// paga: la o `dnl_login_check` existe porque os lacos morrem, e ele so roda na entrada --
			// entao um bio que ja estava online quando a regra mudou fica de fora ate deslogar. Aqui
			// a pergunta e feita todo segundo e nao tem como alguem escapar dela.
			//
			// A ASCENSAO E A PRIMEIRA porque e a que estraga o balanceamento inteiro: o bio e raca de
			// FORMAS (`NoAscension = 1`, `statbiodroid.dm:2`), e o `BPBoost` que o humano acumulou
			// antes de virar bio chega a ~317x. Zerado no nascimento e re-zerado aqui, porque o
			// ganho passivo pode reescreve-lo enquanto ele joga.
			// ==========================================================================================================================
			if (f.bio_lab_born || Jandirus.Core.Races.BioAndroids.EhBio(pl.Race))
			{
				if (f.BPBoost != 1) { f.BPBoost = 1; f.Statify(); }
			}

			// ============================ E O SSJ1 TAMBEM E RETROATIVO -- `DNALabs.dm:712-713` ============================
			// `if(bio_lab_born && bio_saiyan_dna) if(!hasssj) hasssj = 1`. O nascimento ja o concede
			// (ver `NascerBioAndroide`), entao isto so alcanca quem escapou: o save gravado antes de a
			// regra existir, e o bio cujo `EstadoDeForma` foi refeito por qualquer caminho.
			//
			// **NAO E COSMETICO NESTE SISTEMA**, e e por isso que ele entra: o SSJ2 que cancela a morte
			// exige `canSSJ` **e** 100% de maestria no SSJ1 (`DespertarSsj2DoBio`), ou seja exige que o
			// bio tenha o SSJ1 pra treinar. Um bio com o DNA e sem a forma liberada nunca alcanca o
			// despertar mais caro da raca -- e falharia calado, porque a porta que ele nao passa e uma
			// leitura de maestria e nao um aviso.
			//
			// A CONDICAO E A DO DM E CONTINUA SENDO O DNA (`bio_saiyan_dna`). O dono pediu *"bio
			// androide JA COMECA PODENDO VIRAR SSJ1"*; no original isso e verdade **para a fornada com
			// DNA saiyajin** e nao pra toda -- e o mesmo campo e pre-requisito do pedido seguinte dele.
			// Ver o relatorio desta tarefa.
			if (f.bio_lab_born && f.bio_saiyan_dna && !f.canSSJ) f.canSSJ = true;
			if (f.bio_lab_born && f.bio_saiyan_dna && pl.Forma.Liberar("ssj1"))
				Avisar(pl, "celulas SAIYAJIN pulsam no seu nucleo: o Super Saiyajin ja corre no seu DNA.");

			// ============================ A ESCADA NOVA E RETROATIVA (2026-10-17) ============================
			// O perfeito do DM levava o BP base a 8x o imperfeito (2x e depois 4x por cima); na tabela do dono o perfeito e 4x e o MARCO 2 dele
			// e 8x -- entao o perfeito antigo vira o marco 2 e o BP nao se mexe. O semi do DM (2x) ja e o semi novo no marco zero.
			// ================================================================================================
			if (f.bio_tabela < 1)
			{
				if (f.bio_lab_born && f.bio_stage == Jandirus.Core.Races.BioAndroids.Perfeito) f.bio_marco = 2;
				f.bio_tabela = 1;
			}

			// (2026-10-17) A LARVA AMADURECE ENTERRADA, e so assim -- a recem-nascida e a vestida. Ver `GameServer.Larva.cs`.
			AndarALarva(pl, agora);
		}
	}

	/// <summary>
	/// ABSORVEU ALGUEM: A ESCADA ANDA. Era o `bio_note_absorb` do DM (`DNALabs.dm:534-546`: dez jogadores, o NPC valendo meio, ou UM
	/// androide por degrau); desde o adendo do dono (2026-10-17) e a escada de MARCOS -- ver `BioAndroids.Andar`: o jogador vale um passo, o
	/// NPC um quinto, cinco passos fazem os cinco marcos e o sexto sobe a forma. O ANDROIDE DE ENERGIA INFINITA e o atalho: a forma seguinte
	/// direto, e no perfeito o marco 5. (O androide de ABSORCAO nem chega aqui: a execucao o recusa -- `AbsorverBio`.)
	///
	/// Larva nao absorve (`Absorption.dm:111-113`) e o marco 5 do perfeito e o teto -- dali pra cima o que ha e a Super Perfeita, que e forma.
	/// </summary>
	private void ContarAbsorcaoDoBio(ServerPlayer pl, ServerPlayer vitima)
	{
		Fighter f = pl.Ficha;
		if (!f.bio_lab_born || f.bio_stage < Jandirus.Core.Races.BioAndroids.Imperfeito) return;

		if (vitima.Ficha.AndroideInfinito)
		{
			(int estagio, int marco) = Jandirus.Core.Races.BioAndroids.PeloAndroide(f.bio_stage, f.bio_marco);
			f.bio_passos = 0;
			Avisar(pl, "um ANDROIDE DE ENERGIA INFINITA: o motor dele reescreve o seu núcleo de uma vez só.");
			MudarDegrauDoBio(pl, estagio, marco);
			return;
		}

		double ganho = EhJogador(vitima) ? Jandirus.Core.Races.BioAndroids.PassoDoJogador : Jandirus.Core.Races.BioAndroids.PassoDoNpc;
		(int novoEstagio, int novoMarco, double passos) = Jandirus.Core.Races.BioAndroids.Andar(f.bio_stage, f.bio_marco, f.bio_passos, ganho);
		f.bio_passos = passos;
		if (novoEstagio == f.bio_stage && novoMarco == f.bio_marco)
		{
			Avisar(pl, f.bio_stage >= Jandirus.Core.Races.BioAndroids.Perfeito && f.bio_marco >= Jandirus.Core.Races.BioAndroids.MarcosPorForma
				? "a sua forma perfeita está no topo da escada: dali pra cima é a Super Perfeita."
				: $"seu núcleo processa a nova biomassa... ({passos * 100:0}% do próximo marco -- um jogador vale um marco inteiro, um NPC um quinto)");
			return;
		}
		MudarDegrauDoBio(pl, novoEstagio, novoMarco);
	}

	/// <summary>
	/// O BIO SOBE UM DEGRAU. E o `dnl_larva_mature()` e o `BioLabEvolve()` na mesma funcao, porque
	/// no original eles fazem a MESMA lista de coisas -- o que muda entre os dois e o tamanho da
	/// cena, e cena e do cliente.
	///
	/// ============================ O QUE UM DEGRAU FAZ, NA ORDEM DO DM ============================
	///   1. o BP BASE e multiplicado (`BP *= 2`, `BP *= 4`) -- **permanente**, e por isso o Zenkai,
	///      o `CapCheck` e o teto de treino acompanham. A larva nao multiplica nada: o que ela ganha
	///      e a carapaca SAINDO (`BPrestriction = 1`), que aqui e o proprio `bio_stage` deixar de
	///      ser 1 e o teto duro do `Fighter.PowerLevel` parar de valer;
	///   2. a forma perfeita marca `form3cantrevert` -- ela e PERMANENTE, e e o pre-requisito da
	///      Super Perfeita e (no original) da via de sobreviver a propria morte;
	///   3. `Ki = MaxKi` EXATOS -- "nova forma = folego cheio", e sem isso a barra estoura junto
	///      com o salto de BP;
	///   4. o MARCO de ganho sobe (`bp_milestone_reach`): 1,5x / 2x / 3x no multiplicador global de
	///      treino. **Este era o quarto orfao deste sistema** -- a tabela `Milestones` tinha as
	///      quatro linhas do bio e `ReachMilestone` tinha um unico chamador em todo o repo (a
	///      Ascensao). Nenhum marco de FORMA era concedido por ninguem;
	///   5. o CORPO muda (`icon` **e** `oicon`) -- aqui e o indice de `Appearance.Corpo` + o
	///      `TrocarAparencias`, que ja e como a zona inteira ve alguem mudar. **O ESTADO muda agora; o
	///      PIXEL muda na virada da cena** -- ver o bloco da ordem dos dois pacotes la embaixo.
	/// =============================================================================================
	/// </summary>
	private void SubirDegrauDoBio(ServerPlayer pl, int alvo) => MudarDegrauDoBio(pl, alvo, 0);

	/// <summary>
	/// (2026-10-17) A ESCADA SOBE -- um degrau, ou so um marco dentro dele. O BP base anda pela RAZAO entre os multiplicadores absolutos da
	/// tabela do dono (`BioAndroids.MultAbsoluto`): do semi no marco 5 (3x) pro perfeito (4x) e 4/3, e nao os 4x do DM. Marco dentro do mesmo
	/// degrau e so o BP e a frase; DEGRAU NOVO e a lista inteira de baixo (o corpo, a cena, o marco de treino, o Ki cheio).
	/// </summary>
	private void MudarDegrauDoBio(ServerPlayer pl, int estagio, int marco)
	{
		estagio = Math.Clamp(estagio, Jandirus.Core.Races.BioAndroids.Imperfeito, Jandirus.Core.Races.BioAndroids.Perfeito);
		marco = Math.Clamp(marco, 0, Jandirus.Core.Races.BioAndroids.MarcosPorForma);
		Fighter f = pl.Ficha;
		int antes = f.bio_stage, marcoAntes = f.bio_marco;
		bool degrauNovo = estagio > antes;
		if (!degrauNovo && (estagio < antes || marco <= marcoAntes)) return;   // a escada so sobe

		double razao = Jandirus.Core.Races.BioAndroids.MultAbsoluto(estagio, marco) / Jandirus.Core.Races.BioAndroids.MultAbsoluto(antes, marcoAntes);
		f.bio_stage = estagio;
		f.bio_marco = marco;
		if (razao > 1) f.BP *= razao;

		if (!degrauNovo)
		{
			f.Statify();
			AplicarForma(pl);
			pl.SigAtributos = "";
			Persistir(pl);
			Avisar(pl, $"a biomassa assenta no seu núcleo: MARCO {marco} da {Jandirus.Core.Races.BioAndroids.NomeDoDegrau(estagio)} -- o seu corpo agora "
						   + $"vale {Jandirus.Core.Races.BioAndroids.MultAbsoluto(estagio, marco):0.#}x o imperfeito.");
			GD.Print($"[server] {pl.Name}: marco {marco} do degrau {estagio} (BP {f.BP:N0})");
			return;
		}
		int alvo = estagio;

		if (alvo == Jandirus.Core.Races.BioAndroids.Perfeito) f.form3cantrevert = true;

		// O MARCO -- e ele e concedido pela porta de producao (`ReachMilestone`), nao escrito na mao.
		string marcoDeTreino = Jandirus.Core.Races.BioAndroids.MarcoDoDegrau(alvo);
		double novo = marcoDeTreino.Length > 0 ? f.ReachMilestone(marcoDeTreino) : 0;

		pl.Visual.Corpo = Jandirus.Core.Races.BioAndroids.IndiceDoCorpo(alvo);
		f.Statify();
		AplicarForma(pl);
		f.Ki = f.MaxKi;                      // nova forma = Ki em 100% EXATOS
		pl.Combate?.Corpo.Restaurar();
		pl.Combate?.SincronizarVida();
		pl.SigAtributos = "";

		// ============================ A CENA SAI **ANTES** DA APARENCIA, E A ORDEM E O CONSERTO ============================
		// O dono, com foto: *"o bio androide ta MUDANDO O CORPO ANTES DA CINEMATICA ACABAR ai ta ficando
		// BUGADO"* -- o corpo meio trocado, duas silhuetas de tamanhos diferentes empilhadas. Este bloco
		// rodava DEPOIS do `TrocarAparencias`, e a divergencia contra o DM estava anotada como divida
		// aceita aqui e no cabecalho das cenas do bio. Era a divida que o dono cobrou.
		//
		// NO DM O CORPO ENTRA NO CLIMAX: `BioLabEvolve()` pendura o contorno sobre o corpo VELHO na
		// largada (`image(...)` + `overlayList += MORPH`, `DNALabs.dm:563-565`) e so aos 28 s tira o
		// contorno e escreve o icone novo, nesta ordem literal (`overlayList -= MORPH` :614, `icon =`
		// :617). Aqui a cena tem 28,0 s e o corpo trocava no segundo 0,0.
		//
		// ============================ O QUE MUDA E O QUE **NAO** MUDA ============================
		// A ESCADA NAO E ADIADA -- ela esta medida e verde e continua acontecendo agora: o `bio_stage`,
		// o BP, o marco, o Ki cheio e o proprio `Visual.Corpo` ja foram escritos acima. O servidor e
		// AUTORIDADE e nao espera cena nenhuma.
		//
		// O QUE MUDA E A ORDEM DOS DOIS PACOTES. Os dois vao no mesmo canal confiavel e ORDENADO
		// (`ChannelReliable`/`ReliableOrdered`, ver `CenaDoBio` e `TrocarAparencias`), entao "cena antes
		// de aparencia" e uma garantia e nao uma corrida. E com ela na ordem certa, quem espera passa a
		// ser o CLIENTE: o `PeerLook` chega no meio da cena, o `World` o guarda em `_pendentes` e o
		// pixel so muda na VIRADA (`Transformacao.NaVirada`). O comentario antigo aqui temia justamente
		// isto -- "a silhueta por cima do corpo velho" --, e e exatamente o que o DM faz e o que o dono
		// esta pedindo.
		//
		// QUAL cena sai do proprio degrau (`Cinematicas.CenaDoDegrau`), e nao de um `switch` aqui.
		// ==========================================================================================================
		if (Jandirus.Core.Forms.Cinematicas.CenaDoDegrau(alvo) is { } cena) CenaDoBio(pl, cena);

		TrocarAparencias(pl);

		Persistir(pl);

		string nome = Jandirus.Core.Races.BioAndroids.NomeDoDegrau(alvo);
		GD.Print($"[server] {pl.Name} evoluiu pro degrau {alvo} ({nome}): BP {f.BP:N0}"
				 + (novo > 0 ? $", marco de treino {marcoDeTreino} = {novo:0.#}x" : ""));

		Avisar(pl, alvo switch
		{
			Jandirus.Core.Races.BioAndroids.Imperfeito =>
				"sua carapaca larval se rompe e voce emerge INTEIRO. 100% do seu poder foi liberado.",
			Jandirus.Core.Races.BioAndroids.SemiPerfeito =>
				$"o poder deles agora e SEU: {Jandirus.Core.Races.BioAndroids.MultAbsoluto(alvo, marco):0.#}x o imperfeito -- mas voce sente que ainda nao e a forma final.",
			_ =>
				$"PERFEICAO. O seu corpo vale {Jandirus.Core.Races.BioAndroids.MultAbsoluto(alvo, marco):0.#}x o imperfeito, e esta forma e sua para sempre.",
		});
		if (novo > 0) Avisar(pl, $"voce rompeu um patamar: daqui pra frente treinar rende {novo:0.#}x.");

		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			if (o != pl) Avisar(o, $"o corpo de {pl.Name} INCHA e borbulha -- a biomassa absorvida "
								   + $"esta reescrevendo a forma dele. {pl.Name} atingiu a {nome}!");
	}

	/// <summary>
	/// ============================ A CINEMATICA DO BIO-ANDROIDE SAI DAQUI, E SO DAQUI ============================
	/// O dono: *"vc n colocou a CINEMATICA DE TRANSFORMACAO dos bio androides, olhe no byond como era,
	/// tinha um OVERLAY q fazia o CORPO BRILHAR etc."*. O motor de cena ja existia inteiro
	/// (`Core.Forms.Cinematicas` + `Client.Transformacao`); o que faltava era o bio ter **entrada**
	/// nele -- as quatro cenas do original (`imperfecttranscinematic`, `perfecttranscinematic`,
	/// `BioLabEvolve`, `bio_ssj2_awaken`) nao tinham nenhuma linha portada, e a Super Perfeita era a
	/// unica forma do catalogo inteiro sem cena.
	///
	/// ============================ POR QUE UM FUNIL, E NAO UM `Send` EM CADA CHAMADOR ============================
	/// Sao TRES chamadores hoje (o degrau da larva, o degrau da absorcao e o SSJ2 pela morte) e os
	/// tres precisam fazer a MESMA dupla de coisas: mandar o pacote pra zona e ANOTAR o prazo em que o
	/// corpo fica preso. Escrever as duas nos tres e o modo de falha que este projeto ja documenta em
	/// meia duzia de lugares -- alguem acrescenta a quarta cena, lembra do pacote e esquece do prazo, e
	/// o sintoma e um jogador andando por dentro da propria metamorfose.
	///
	/// ============================ O PRAZO SAI DA CENA, PELA MESMA CONTA DO `MarcarCena` ============================
	/// `CenaSegundos` e o campo que o `EmCena` le -- ele desliga o dreno da forma, a regeneracao, a
	/// carga e o custo do voo, e e o que segura o corpo. O numero e o `SegundosPreso` da propria cena,
	/// que e o mesmo que o `Transformacao` do cliente usa pra soltar o boneco: uma segunda verdade
	/// aqui apareceria como "as vezes o Ki some" (o comentario e do `MarcarCena`, e vale igual).
	///
	/// ZERO NAS CENAS QUE NAO PRENDEM, e isso e do DM: o `bio_ssj2_awaken` escreve `canmove = 1`,
	/// `move = 1`, `canfight = 1` na primeira coisa que faz -- quem acabou de cancelar a propria morte
	/// nao pode ficar oito segundos parado na frente de quem o matou.
	/// =========================================================================================================
	/// </summary>
	private void CenaDoBio(ServerPlayer pl, Jandirus.Core.Forms.Cinematicas.CenaBio qual)
	{
		// O PRAZO PRIMEIRO: quem receber o pacote comeca a contar no quadro seguinte, e o portao do
		// servidor tem que ja estar fechado quando isso acontecer.
		pl.CenaSegundos = Jandirus.Core.Forms.Cinematicas.DoBio(qual)?.SegundosPreso ?? 0;

		var w = Protocol.Begin(Protocol.S2C.CenaDoBio);
		w.Put(pl.Id);
		w.Put((byte)qual);
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		GD.Print($"[server] {pl.Name}: CENA DO BIO '{qual}' ({pl.CenaSegundos:0.#}s preso)");
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
			w.Put((byte)0);       // mobilia de nave nao tem melhoria (ver `MelhoriasFeitas`, logo abaixo das obras)
			w.Put(false);         // (2026-10-25) "liberada": a mobilia e da nave, e a nave tem a dela
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
			w.Put((byte)0);
			w.Put(n.Liberada);    // (2026-10-25) o botao "Liberar"/"Trancar" da aba Tech (`LiberarObra`)
		}
		foreach (Obra o in daZona)
		{
			Construcao? c = _obras?.Get(o.Tipo);
			w.Put(o.Id);
			w.Put(o.Tipo);
			// O NOME E POR INSTANCIA QUANDO A OBRA TEM UM. Hoje ha um caso: a LAPIDE, cujo nome e o
			// EPITAFIO (`A.desc`, `Corpse.dm:53`) -- assim o menu da tecla E escreve "Aqui jaz Fulano"
			// no titulo em vez de "Grave". E o mesmo canal que ja fez o console da ponte deixar de se
			// chamar "Ship Control": o nome sempre viajou por obra, so nunca tinha VARIADO por obra.
			w.Put(o.Epitafio.Length > 0 ? o.Epitafio : c?.Nome ?? o.Tipo);
			w.Put(o.X);
			w.Put(o.Y);
			w.Put(o.Aparafusada);
			w.Put((byte)o.Lab);
			w.Put(o.DonoNome);
			// A ARTE VIAJA JUNTO. O cliente tem o catalogo, mas so o que ELE pode comprar -- e a
			// bancada de outra pessoa tem que aparecer do mesmo jeito. Mandar o caminho aqui evita
			// que "ver" dependa de "poder construir".
			// (2026-10-21) Folha, estado e deslocamento numa conta so (`ArteDaObra`): os do catalogo -- menos o tanque LIGADO, que ganha a
			// campanula, e o mainframe com o laboratorio do bio ou do Neo Tuffle, que vira o Cell Pod.
			(string arte, string estado, float px, float py) = ArteDaObra(o, c);
			w.Put(arte);
			w.Put(estado);
			w.Put(px);
			w.Put(py);
			// ...e a DENSIDADE pelo mesmo motivo: o cliente tem que barrar o corpo no que o
			// servidor barra, e sem isto ele teria que adivinhar pelo catalogo -- que so lista o
			// que ELE pode comprar.
			w.Put(c?.Densa ?? false);
			// ...E O QUE ESTA MAQUINA JA TEM de melhoria de uma vez so: e por este byte que o menu da tecla E tira da
			// lista o que nao da mais pra comprar NESTA maquina. Ver `Interacoes.BitQueEsconde`.
			w.Put(MelhoriasFeitas(o));
			// (2026-10-25) ...e se o dono a LIBEROU pra todos: o botao "Liberar"/"Trancar" da linha dela na aba Tech (`LiberarObra`)
			w.Put(o.Liberada);
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
	/// <summary>
	/// AS MELHORIAS QUE ESTA OBRA JA TEM e que saem do menu dela, nos bits do `Interacoes` (`MelhoriaUnicaFeita`,
	/// `MelhoriaNoTeto`, `ForcaNoTetoDoMundo`). Le o estado sem cria-lo: a maquina que nunca foi mexida nao tem nenhuma -- menos o tanque
	/// do MAPA, que nasce turbinado (`RegeneradorDaObra.DaCidade`) e por isso ja nasce sem a opcao.
	/// </summary>
	private static byte MelhoriasFeitas(Obra o) => o.Tipo switch
	{
		Jandirus.Core.Tech.Regenerador.Tipo =>
			(byte)((o.Regenerador ?? (o.DoMapa ? RegeneradorDaObra.DaCidade() : null))?.CuraFerimentos == true
				? Jandirus.Core.Tech.Interacoes.MelhoriaUnicaFeita : 0),
		"Gravity" => (byte)((o.Gravidade?.Estavel == true ? Jandirus.Core.Tech.Interacoes.MelhoriaUnicaFeita : 0)
						   | (o.Gravidade?.Range >= AlcanceMaximo ? Jandirus.Core.Tech.Interacoes.MelhoriaNoTeto : 0)
						   // (2026-10-19) a forca no teto do mundo: dali nao sobe pra ninguem (`TetoDaForca`)
						   | (o.Gravidade?.Max >= Jandirus.Core.Tech.Interacoes.TetoDeGravidade ? Jandirus.Core.Tech.Interacoes.ForcaNoTetoDoMundo : 0)
						   // (2026-10-21) a trava de seguranca, compra unica, bit proprio (o 1 ja e a estabilizacao)
						   | (o.Gravidade?.Trava == true ? Jandirus.Core.Tech.Interacoes.TravaDeSegurancaFeita : 0)),
		// (v0.2.1) a ressurreicao de androide do super computador: comprada uma vez, sai do menu dele.
		Jandirus.Core.Tech.SuperComputador.Tipo =>
			(byte)(o.Tech?.Ligado(Jandirus.Core.Tech.SuperComputador.ChaveRessurreicao) == true
				? Jandirus.Core.Tech.Interacoes.MelhoriaUnicaFeita : 0),
		_ => 0,
	};

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

		// A CARTEIRA VAI ATRAS DE TODO CATALOGO: e por ela que os RECURSOS chegam (o `S2C.Tech` so traz o zeni),
		// e assim o login e todo comando de tecnologia ja a entregam sem uma linha a mais em cada chamador.
		MandarCarteira(pl);
	}

	/// <summary>O canal unico de tecnologia, do mesmo jeito que o de habilidade.</summary>
	private void ComandoDeTech(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "lista": MandarCatalogoDeObras(pl); break;
			case "construir": Construir(pl, arg); break;
			// O MESMO FABRICAR, PAGO COM RECURSOS (o dobro). Comando a parte, e nao um sufixo no argumento: o
			// argumento e o id do catalogo, e a moeda nao e parte dele.
			case "construir_recursos": Construir(pl, arg, Moeda.Recursos); break;
			case "posicionar": Posicionar(pl, arg); break;
			case "pegar": PegarObra(pl); break;
			case "aparafusar": Aparafusar(pl); break;
			case "liberar": LiberarObra(pl, arg); break;   // (2026-10-25) o botao "Liberar"/"Trancar" da aba Tech
			case "estudar":
				pl.Estudando = !pl.Estudando;
				Avisar(pl, pl.Estudando ? "voce se debruca sobre a bancada." : "voce para de estudar.");
				break;
			// (2026-10-25) o estudo de cabeca, da telinha da tecla M -- ver `ComecarAEstudarMeditando`
			case "estudar_meditando": ComecarAEstudarMeditando(pl); break;
			// A MOEDA E O ARGUMENTO (vazio = zeni, o de sempre; "recursos" = o dobro em recursos).
			case "androide_absorcao": VirarAndroide(pl, infinito: false, Recurso.LerPedidoPago(arg).Moeda); break;
			case "androide_infinito": VirarAndroide(pl, infinito: true, Recurso.LerPedidoPago(arg).Moeda); break;
			case "lab_androide": InstalarLab(pl, 1); break;
			case "lab_bio": InstalarLab(pl, 2); break;
			// (2026-10-18) o Neo Tuffle Lab e a conversao dentro dele -- ver `GameServer.NeoTuffle.cs`
			case "lab_tuffle": InstalarLab(pl, LabDoNeoTuffle); break;
			case "virar_tuffle": VirarNeoTuffle(pl, Recurso.LerPedidoPago(arg).Moeda); break;
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
