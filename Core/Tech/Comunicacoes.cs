using Jandirus.Core.Items;

namespace Jandirus.Core.Tech;

/// <summary>
/// O TELEPAD, O RELOGIO DE TELEPORTE E O ANULADOR -- `obj/items/Telepad`, `Omniwatch` e `Teleport_Nullifier`
/// (`Code/Modules/Tech/Teleports.dm`).
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// O RELOGIO tem um CANAL. O "Teleport" dele lista os destinos do mesmo canal -- os outros relogios (onde quer
/// que estejam, com quem os carrega) e os telepads APARAFUSADOS -- e, escolhido um, leva quem o usa pra onde o
/// destino ESTIVER dois segundos depois (`sleep(20)`, e so entao `choice.loc`). Nao viaja quem esta caido ou preso
/// (`!usr.canmove || usr.KO || usr.deathregening || usr.grabParalysis || usr.stagger`), nem quem esta no mesmo mapa
/// de um ANULADOR ligado (*"Teleport failed!"*). O "Speak" fala com todo relogio do canal e com os comunicadores
/// que o escutam; o "Henshin!" grita *"Transform!!"* e veste (ou tira) um capacete; a autenticacao de DNA prende o
/// relogio a um dono -- e ao mexer no canal, na tranca ou no teleporte, na mao de outro, ele EXPLODE
/// (`"[src] explodes!"`; o "Speak" e o "Henshin!" nao conferem o dono).
/// O TELEPAD e o destino fixo: tem um canal, so vale aparafusado, e o clique nele leva a armadura ao teto de
/// tecnologia de quem clica.
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * O ANULADOR FUNCIONA. No original o `Click()` liga `telenulon` sem nunca escrever a duracao
///     (`telenuldur` fica em zero) e o tique seguinte o desliga: ele dura um decimo de segundo. O que se porta e a
///     descricao -- *"prevent anyone on the same Z-Level from teleporting for about a minute or so. (This includes
///     yourself!) Can be manually turned off"* --: um minuto, mais dez segundos por degrau de tecnologia de quem o
///     liga (`round(log(8, techskill))`), ate o teto de dois minutos do proprio item (`telenulmdur = 1200`).
///   * ELE VALE NA MOCHILA DE QUEM O LIGA. La a conta e `Tn.z == usr.z`, e o `z` de um item carregado e zero: so o
///     anulador largado no chao anularia. O item deste port mora na mochila, e a zona anulada e a de quem o carrega.
///   * O QUE ELE ANULA E O QUE O ORIGINAL ANULA: o teleporte dos relogios. Nenhuma tecnica de la o consulta.
///   * DE ONDE NAO SE SAI, NAO SE SAI PELO RELOGIO, E ONDE NAO SE CHEGA, ELE NAO CHEGA: a pergunta de todo
///     teleporte deste port (`PorQueNaoSaiDaqui`, `ZonaFechadaAoTeleporte` -- a prisao da Sala do Tempo, o selo, o
///     interior de um Majin, a mente, a espera do torneio). O original nao tem esses lugares.
///   * A LISTA DE DESTINOS SAI NO CHAT, numerada, e viaja-se pelo numero: a mochila deste port pergunta um numero
///     ou um texto, nao uma lista.
///   * O DONO DESTRANCA O DNA (la, trancado e pra sempre).
///   * O CAPACETE DO HENSHIN e o de fabrica (`Clothes Helmet.dmi`); a troca por um icone enviado ("Henshin Icon")
///     nao veio. O `Name` (rebatizar o relogio e o telepad) tambem nao: item deste port nao tem nome proprio.
/// ==================================================================================
/// </summary>
public static class Teleporte
{
	public const string Telepad = "Telepad", Relogio = "Omniwatch", Anulador = "Teleport_Nullifier";

	public const string VerboCanalDoTelepad = "telepad_canal", VerboReforcarOTelepad = "telepad_reforcar";

	public const string AcaoCanal = "relogio_canal", AcaoDestinos = "relogio_destinos", AcaoIr = "relogio_ir",
						AcaoFalar = "relogio_falar", AcaoHenshin = "relogio_henshin", AcaoDna = "relogio_dna",
						AcaoAnular = "anulador_ligar";

	/// <summary>A chave do canal no estado de um telepad (`Obra.Tech.S`).</summary>
	public const string ChaveCanal = "canal";

	/// <summary>O teto de letras de um canal (ele mora no id do relogio).</summary>
	public const int MaxDeCanal = 12;

	/// <summary>`sleep(20)`: quanto demora a viagem do relogio.</summary>
	public const double SegundosDeViagem = 2;

	/// <summary>Quantos destinos a lista mostra.</summary>
	public const int MaxDeDestinos = 30;

	/// <summary>A folha do capacete do "Henshin!" (`storedclothesicon = 'Clothes Helmet.dmi'`).</summary>
	public const string FolhaDoCapacete = "res://Assets/Sprites/Clothes/Clothes Helmet.tres";

	// ---- o relogio: `Omniwatch|<canal>|<marca do dono>` ----
	public static string Canal(string id) => TechDeBolso.Campo(id, 0);
	public static string Dna(string id) => TechDeBolso.Campo(id, 1);
	public static string Escrever(string canal, string dna) => canal.Length == 0 && dna.Length == 0 ? Relogio : TechDeBolso.Com(Relogio, canal, dna);

	/// <summary>
	/// A MARCA DE UM DONO num item trancado por DNA (`mastersig = usr.signature`): seis digitos tirados da conta. Nao
	/// e segredo nem identidade -- so o que basta pra o item saber que mudou de mao.
	/// </summary>
	public static string Marca(string conta)
	{
		uint h = 2166136261;
		foreach (char c in conta.ToLowerInvariant()) { h ^= c; h *= 16777619; }
		return (h % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
	}

	/// <summary>Este item esta trancado, e pra outra pessoa? (Sem tranca, e de qualquer um.)</summary>
	public static bool TrancadoPraOutro(string dna, string conta) => dna.Length > 0 && dna != Marca(conta);

	/// <summary>Quanto tempo o anulador fica ligado na mao de quem tem esta tecnologia -- ver o cabecalho.</summary>
	public static double SegundosDoAnulador(double techskill) =>
		Math.Min(60 + 10 * Math.Round(Math.Log(Math.Max(techskill, 1), 8), MidpointRounding.AwayFromZero), 120);

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X5): o anulador ligado nao anula nada -- o do original, que se desliga sozinho.</summary>
	public static bool AnuladorNaoAnulaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X5): o relogio alcanca destino de qualquer canal.</summary>
	public static bool RelogioIgnoraOCanalDeTeste;
}

/// <summary>
/// AS CAMERAS, O COMPUTADOR DE CAMERAS E O COMUNICADOR -- `obj/items/Camera`, `Camera_Computer`, as frequencias e a
/// biblioteca do `PDA` (`Code/Modules/Tech/Camera.dm`) e o `obj/items/Communicator` (`Communicator.dm`).
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// A CAMERA, posta no chao, tem uma FREQUENCIA. O computador de cameras (uma frequencia), o PDA (uma lista delas) e
/// o comunicador (com a melhoria) escolhem uma camera da frequencia deles e passam a VER por ela
/// (`usr.client.eye = nA`).
/// O COMUNICADOR e um console de chao com uma LISTA de frequencias: o "Send" manda a frase pra quem escuta uma
/// delas -- os relogios do canal, os scouters na frequencia, os comunicadores (que a guardam no historico, e a
/// dizem em voz alta com o DIFUSOR) --, e o MEGAFONE a manda pra todo mundo, com uma espera de um minuto que cresce
/// a cada uso. Melhorias: megafone (100.000 zeni, 35 de tecnologia), difusor (100.000, 40) e camera (150.000, 45).
/// O PDA tambem publica um livro na biblioteca global (`Upload_to_Library`) e imprime um de la.
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * A CAMERA MANDA UM RELATO, E NAO A IMAGEM. O cliente deste port desenha UMA zona, a de quem joga; trocar o
///     olho dele pra outro mapa nao existe. Quem consulta uma camera recebe o que ela ve AGORA -- quem esta a
///     vista dela e o que cada um faz --, por escrito. Sem numero de poder nenhum (a camera nao e medidor) e sem
///     quem esta invisivel.
///   * A CAMERA E O COMUNICADOR SE ASSENTAM NO CHAO, pela mochila ("Instalar no chão"), como toda maquina. O
///     catalogo extraido os da como itens pessoais: o extrator perde o `Bolt` do comunicador (a string de varias
///     linhas do `messagelist` engole o resto do tipo) e os verbs da camera sao `view(1)`, que nao dizem de que lado
///     ela fica. Quem diz e a descricao da camera -- *"Place it down, bolt it, set a frequency"* --, e por ela a
///     camera so transmite APARAFUSADA (o codigo de la nao confere; e ela nem tem o verb `Bolt`).
///   * AS FREQUENCIAS TEM TETO e nao se repetem: quatro no PDA (elas moram no id dele) e oito no comunicador. La a
///     lista cresce sem fim, e o "Subtract" (`freqlist.Cut(texto)`) nao tira a frequencia pedida.
///   * O "Send" FALA COM TODOS os que escutam. La o laco tem um `return` depois do primeiro que acha
///     (`Communicator.dm:120`), e a frase chega a um aparelho so. As naves e os trajes espaciais do original tem
///     canal proprio; os deste port nao, e ficam de fora.
///   * O HISTORICO E O DIFUSOR SAO DE QUEM RECEBE, como no "Speak" do relogio (`nO.messagelist`,
///     `nO.hasbroadcaster`, `Teleports.dm:153-154`). O "Send" de la escreve os dois sem o `nO.`: o historico de quem
///     MANDA cresce uma linha por console que escuta, e e o difusor de quem manda que faz o outro falar.
///   * AS TRANCAS DE DNA TRANCAM, E O HISTORICO ABRE. As de la estao escritas `if(!DNA == usr.signature)`, que nunca
///     recusa ninguem; a do comunicador comeca por `if(siglock != usr.signature) return`, que com a tranca vazia
///     devolve sempre -- ninguem consegue trancar, e o `Communication_History`, que tem a mesma linha, nunca abre.
///     Aqui a tranca guarda o que la ela diz guardar (ver as cameras e ler o historico), e so quem trancou destranca.
///   * O MEGAFONE ESPERA UM MINUTO POR USO ACUMULADO, e a pilha cai um por minuto parado -- a descricao (*"a one
///     minute timer whose delay increases every minute you use it"*). No `Ticker` de la a ultima linha
///     (`cooldownstack = max(0,cooldowndown)`) escreve o cronometro na pilha, e o segundo uso logo depois do
///     primeiro esperaria dez horas. A frase do megafone sai marcada "(Megafone)" (la, "(Communications)", igual a
///     do "Send").
///   * AS MELHORIAS ACEITAM RECURSOS, pelo dobro. A biblioteca guarda o texto (o livro deste port nao tem titulo), e
///     o livro impresso cai na mochila (la, no chao).
/// ==================================================================================
/// </summary>
public static class Vigilancia
{
	public const string Camera = "Camera", Computador = "Camera_Computer", Comunicador = "Communicator";

	// ---- as acoes de mochila ----
	public const string AcaoFrequencia = "vig_freq", AcaoMaisFrequencia = "vig_mais", AcaoMenosFrequencia = "vig_menos",
						AcaoVer = "vig_ver", AcaoDna = "vig_dna",
						AcaoPublicar = "pda_publicar", AcaoBiblioteca = "pda_biblioteca", AcaoImprimir = "pda_imprimir";

	// ---- os verbos das obras ----
	public const string VerboFrequencia = "cam_freq", VerboMaisFrequencia = "com_mais", VerboMenosFrequencia = "com_menos",
						VerboFrequencias = "com_freqs", VerboEnviar = "com_enviar", VerboMegafone = "com_megafone",
						VerboMelhorar = "com_up", VerboCameras = "com_cameras", VerboHistorico = "com_historico",
						VerboDna = "com_dna";
	public const string Megafone = "megafone", Difusor = "difusor", OlhoDeCamera = "camera";

	// ---- as chaves do estado (`Obra.Tech`) ----
	public const string ChaveFrequencia = "freq", ChaveFrequencias = "freqs", ChaveHistorico = "hist", ChaveDna = "dna",
						ChaveMegafone = "megafone", ChaveDifusor = "difusor", ChaveOlho = "camera",
						ChaveMegaAte = "megaAte", ChaveMegaPilha = "megaPilha", ChaveMegaDecaiEm = "megaDecaiEm";

	/// <summary>O teto de letras de uma frequencia, e de frequencias num comunicador e num PDA.</summary>
	public const int MaxDeFrequencia = 8, MaxDeFrequencias = 8, MaxDeFrequenciasNoPda = 4;

	/// <summary>O teto de uma frase mandada, e de linhas guardadas no historico.</summary>
	public const int MaxDeFrase = 160, MaxDeHistorico = 20;

	/// <summary>Ate onde a camera ve, em tiles: a tela de quem olharia por ela.</summary>
	public const int RaioDaCameraEmTiles = 9;

	/// <summary>`megaphonecooldown = 600 * cooldownstack` (em decimos): um minuto por uso acumulado, e a pilha cai um por minuto.</summary>
	public const double SegundosDoMegafonePorUso = 60;

	public const double CustoDoMegafone = 100_000, CustoDoDifusor = 100_000, CustoDoOlho = 150_000;
	public const double TecnologiaDoMegafone = 35, TecnologiaDoDifusor = 40, TecnologiaDoOlho = 45;

	/// <summary>Quantos livros cabem na biblioteca global.</summary>
	public const int MaxDeLivrosNaBiblioteca = 200;

	// ---- o computador de cameras: `Camera_Computer|<frequencia>|<marca do dono>` ----
	public static string FrequenciaDoComputador(string id) => TechDeBolso.Campo(id, 0);
	public static string DnaDoComputador(string id) => TechDeBolso.Campo(id, 1);
	public static string EscreverComputador(string frequencia, string dna) =>
		frequencia.Length == 0 && dna.Length == 0 ? Computador : TechDeBolso.Com(Computador, frequencia, dna);

	// ---- o PDA: `PDA|<nota>|<frequencias, separadas por virgula>|<marca do dono>` ----
	public static List<string> FrequenciasDoPda(string id) => Frequencias(TechDeBolso.Campo(id, 1));
	public static string DnaDoPda(string id) => TechDeBolso.Campo(id, 2);
	public static string EscreverPda(string nota, IEnumerable<string> frequencias, string dna)
	{
		string f = string.Join(",", frequencias);
		return nota.Length == 0 && f.Length == 0 && dna.Length == 0 ? TechDeBolso.Pda : TechDeBolso.Com(TechDeBolso.Pda, nota, f, dna);
	}

	/// <summary>A tranca de DNA de um item de cameras (o computador ou o PDA).</summary>
	public static string DnaDoItem(string id) => TechDeBolso.Base(id) == TechDeBolso.Pda ? DnaDoPda(id) : DnaDoComputador(id);

	/// <summary>Uma lista de frequencias como esta guardada (separadas por virgula).</summary>
	public static List<string> Frequencias(string guardadas) =>
		[.. guardadas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

	/// <summary>A frequencia digitada, como ela pode morar numa lista: limpa, sem virgula, cortada no teto.</summary>
	public static string FrequenciaLimpa(string? cru) => TechDeBolso.Limpo(cru?.Replace(",", ""), MaxDeFrequencia);

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X5): a camera responde a qualquer frequencia.</summary>
	public static bool CameraIgnoraAFrequenciaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X5): o "Send" para no primeiro que escuta -- o `return` do original.</summary>
	public static bool EnvioParaNoPrimeiroDeTeste;
}
