using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jandirus.Core.Appearance;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>Tudo que sobrevive ao logout de um personagem.</summary>
public sealed class CharacterSave
{
    public string Nome = "";
    public string Raca = "";
    public string Planeta = "";
    public string Genero = "Male";
    public string Linhagem = "";
    public int Idade = 18;

    /// <summary>
    /// A HISTORIA que o jogador escreveu na criacao. Sem efeito mecanico -- e identidade, e o verb
    /// `Backstory` do original existia justamente pra ela poder ser LIDA depois.
    /// </summary>
    public string Historia = "";

    /// <summary>
    /// O PORTE DO CORPO (Small/Medium/Large). Diferente da historia, este MEXE EM STAT e e
    /// permanente -- por isso e salvo: recalcular a ficha sem ele devolveria outro personagem.
    /// </summary>
    public string Porte = "Medium";

    /// <summary>O que o personagem carregava ao sair. Ver `Core.Items.Inventario`.</summary>
    public Jandirus.Core.Items.Inventario Mochila = new();

    public Appearance Visual = new();

    /// <summary>
    /// A ficha INTEIRA. Serializar o objeto todo em vez de listar campo a campo e deliberado:
    /// a lista manual e o lugar onde nasce o bug de "esqueci de salvar o X", que so aparece
    /// quando alguem perde progresso. Os campos derivados vao junto e sao recalculados no
    /// primeiro tick -- custa alguns bytes e economiza uma classe inteira de defeito.
    /// </summary>
    public Fighter Ficha = new();

    /// <summary>
    /// O CORPO em partes: nome do membro -> [vida, decepado].
    ///
    /// Vai a parte da ficha porque o corpo nao e do <see cref="Fighter"/> -- e do combate. E
    /// PRECISA persistir: deslogar com o braco quebrado nao pode ser a cura mais barata do
    /// jogo. Vazio (save antigo, ou personagem novo) = corpo inteiro.
    /// </summary>
    public Dictionary<string, double[]> Membros = [];

    /// <summary>
    /// AS SKILLS APRENDIDAS, por typepath, e os marcos.
    ///
    /// Fora da ficha de proposito: skill nao e stat, e patrimonio de personagem -- e a lista
    /// e o que o jogador construiu escolhendo. Perder isso e perder a sessao inteira dele.
    /// </summary>
    public List<string> Skills = [];

    /// <summary>
    /// QUAIS DAS <see cref="Skills"/> ALGUEM ENSINOU A ELE -- o `wastaught` do DM
    /// (`teachable.dm:2`), que la e var do datum e viaja no savefile do mob junto com a skill.
    ///
    /// **E o unico jeito de "quem foi ensinado nao repassa" atravessar o logout.** Sem esta lista a
    /// marca some e a skill fica, ou seja: aprendeu de um mestre, deslogou, voltou podendo ensinar.
    /// A regra central do sistema viraria um deslogar.
    ///
    /// Subconjunto de <see cref="Skills"/> por construcao, e o leitor
    /// (<see cref="Jandirus.Core.Skills.SkillBook.CarregarEnsinadas"/>) descarta o que sobrar --
    /// save antigo chega com a lista vazia, que e a resposta certa: ninguem ainda foi ensinado.
    /// </summary>
    public List<string> SkillsEnsinadas = [];

    /// <summary>
    /// A CASA ESCOLHIDA nas skills de escolha unica (typepath -> 1, 2 ou 3). Uma skill no jogo
    /// inteiro usa isto -- a `Great Robotic Alliance` do Metamoriano (`meta.dm:104-125`), cujo
    /// `after_learn` abre um `input()` de tres casas exclusivas.
    ///
    /// **SEM PERSISTIR, A ESCOLHA MORRE NO LOGOUT** e o Metamoriano volta com a skill no livro e
    /// sem buff nenhum -- que e exatamente a falha silenciosa que o `wastaught` aqui do lado ja
    /// custou uma vez: a skill continua na lista, so o que ela VALE evapora. No DM o `chosen` e
    /// var do datum e viaja no savefile do mob junto com a skill.
    ///
    /// Save antigo chega vazio, que e a resposta certa: ninguem escolheu ainda, e o dono volta a
    /// ser perguntado.
    /// </summary>
    public Dictionary<string, int> SkillsEscolhas = [];

    /// <summary>O NIVEL de cada skill (e o que os degraus ja somaram). Ver NiveisDeSkill.</summary>
    public Jandirus.Core.Skills.NivelSave Niveis = new();

    /// <summary>
    /// QUEM ESTE PERSONAGEM CONHECE, e o que ele sente por cada um. Ver `Core.Social.Convivio`.
    ///
    /// PERSISTE PORQUE PERSISTE LA: no DM `known_contact_list`, `friendship`, `enmity` e `rivals`
    /// sao `mob/var` comuns (nao `tmp`) e o `mob/Write` nao filtra nada -- viajam no savefile do
    /// mob inteiro. O proprio `Friendship.dm:10` escreve *"persists in the save"*.
    ///
    /// E ha uma razao de jogo alem da fidelidade: a amizade e o que abre o SSJ1 (ver
    /// `GameServer.LutoNaVizinhanca`). Uma amizade que morresse no logout faria a porta do tronco
    /// Saiyajin depender de o amigo estar online -- ou seja, a mecanica emocional do jogo viraria
    /// um efeito colateral de quem entrou primeiro.
    /// </summary>
    public Jandirus.Core.Social.Convivio Social = new();

    /// <summary>
    /// O BP que ESTE personagem precisa pra cada forma. Sorteado no nascimento e nunca mais --
    /// e o `rand()` por classe do `statsaiyan.dm`, que faz o SSJ de cada um custar diferente.
    /// </summary>
    public Jandirus.Core.Forms.LimiaresPessoais Limiares = new();
    public int MarcosTotais, MarcosLivres;

    /// <summary>
    /// A FORMA e a MAESTRIA de cada uma (chave = o id numerico da forma).
    ///
    /// Maestria e a coisa mais cara do jogo: so se ganha DENTRO da forma, gastando Ki, ~3h
    /// por forma. Perder isso num save e apagar semanas de alguem.
    /// </summary>
    public Dictionary<string, double> Maestrias = [];

    /// <summary>Formas LIBERADAS (o que os gates leem). Ver <see cref="Jandirus.Core.Forms.EstadoDeForma"/>.</summary>
    public List<int> FormasDespertadas = [];

    /// <summary>
    /// Formas cuja ESTREIA ja foi assistida -- e a outra metade do antigo `JaDespertou`.
    ///
    /// NULAVEL DE PROPOSITO, e e a unica coisa que separa "save de antes desta separacao" de
    /// "personagem que nunca se transformou": os dois teriam a lista vazia, e so o primeiro pode
    /// herdar as liberadas. Ver a migracao em `GameServer.cs`.
    /// </summary>
    public List<int>? FormasEstreadas;

    /// <summary>
    /// FORMAS QUE ESTE PERSONAGEM JA VIU ALGUEM USAR -- o `mst_seen_forms` do DM
    /// (`MasterStudent.dm:36`), que la e var comum do mob e por isso entra no savefile sozinha.
    ///
    /// E o pre-requisito da transformacao assistida: **o aluno tem que ter visto a forma**, ou o
    /// mestre tem que conhece-la. Guardado por <see cref="Jandirus.Core.Forms.FormaDef.IdRede"/>,
    /// no mesmo formato dos dois vizinhos de cima -- o DM guarda tambem DE QUEM se viu, e isso
    /// aqui e sabor: o portao le a existencia da chave.
    ///
    /// NAO CONFUNDIR COM `FormasEstreadas`: aquela e *"a MINHA estreia nesta forma ja rolou"* (o
    /// gate da cinematica), esta e *"eu vi OUTRA PESSOA nesta forma"*. Perguntas opostas, nomes
    /// parecidos -- este arquivo ja pagou uma vez por dois conceitos num nome so.
    /// </summary>
    public List<int> FormasVistas = [];

    /// <summary>
    /// OS CHEFES QUE ESTE PERSONAGEM JA VIU DE PERTO, por id de molde do `npcs.json`.
    ///
    /// E o pre-requisito da luta simulada na mente (*"enfrentar NPCS BOSS Q VC JA VIU ANTES"*), e
    /// nao ha nada disso no original -- la a dimensao mental so tem o proprio reflexo.
    ///
    /// POR ID DE MOLDE E NAO POR NOME: dois Freezas do mesmo molde sao a mesma lembranca, e o nome
    /// que o corpo carrega e sorteado. Molde que sumir do JSON continua na lista (o dono pode estar
    /// so renomeando uma saga) -- quem recusa e a convocacao, que pergunta ao catalogo.
    /// </summary>
    public List<string> ChefesVistos = [];

    /// <summary>
    /// AS PORTAS QUE UM MESTRE CORTOU PELA METADE. Ver
    /// <see cref="Jandirus.Core.Forms.EstadoDeForma.PortasCortadas"/> -- no DM isto e o `ssjat /= 2`
    /// gravado no proprio limiar; aqui o limiar sorteado no nascimento fica intacto e o que
    /// persiste e o corte.
    ///
    /// **Persistir e obrigatorio, nao enfeite**: sem esta linha o aluno desperta com o mestre,
    /// desloga, e volta sem conseguir reentrar na propria forma.
    /// </summary>
    public List<int> PortasCortadas = [];

    /// <summary>
    /// ATE QUANDO A RECARGA DE ENSINO DESTE MESTRE CORRE (relogio de parede, ms de Unix).
    ///
    /// PERSISTE porque no DM tambem persiste: `mst_teach_cd` (`MasterStudent.dm:37`) e var comum do
    /// mob, e o comentario de la diz em voz alta que *"relogar nao zera"*. Sem isso a recarga de 5
    /// minutos viraria "aperte alt+F4 entre um ensino e outro".
    ///
    /// UNIX MS E NAO O RELOGIO DO PROCESSO: o `NowMs()` do servidor ja e
    /// `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`, entao o prazo atravessa reboot do servidor
    /// sem virar um numero do passado (ou do futuro distante) por acidente.
    /// </summary>
    public long MestreRecargaAte;

    /// <summary>
    /// A DISCIPLINA DIVINA: 0 = nenhuma, 1 = Ultra Instinto, 2 = Poder da Destruicao.
    ///
    /// As tres coisas persistem no DM (`ui_learned`/`ui_prof_real`/`ui_prof` sao vars normais do
    /// mob) e aqui tambem. A ATUAL persiste junto da REAL de proposito: quem desloga com a precisao
    /// no chao volta com ela no chao -- deslogar nao pode ser um jeito de descansar o instinto.
    /// </summary>
    public int Disciplina;
    public double DiscReal, DiscAtual;

    // onde estava quando saiu
    public string Zona = "Earth";

    /// <summary>
    /// O TIPO e a SEED da zona -- sem eles a `ZoneKey` nao se remonta.
    ///
    /// ============================ POR QUE O NOME NAO BASTA ============================
    /// Uma `ZoneKey` tem tres partes (tipo, nome, seed) e o save guardava so o nome. Na volta o
    /// servidor reconstruia com `ZoneKey.Premade(nome)`, o que quer dizer: quem deslogou num
    /// planeta GERADO voltava numa zona pre-feita de mesmo nome -- que nao existe no catalogo --,
    /// e quem deslogou no ESPACO voltava numa zona fantasma. Sem cena, sem colisao, sem chao.
    ///
    /// A seed importa tanto quanto o tipo: dois planetas gerados podem ter o mesmo nome e mundos
    /// completamente diferentes. Voltar sem ela e voltar pra outro planeta.
    /// =================================================================================
    /// </summary>
    public byte ZonaTipo;
    public ulong ZonaSeed;

    /// <summary>
    /// A SEMENTE DO BERCO -- de onde sai o planeta em que este corpo nasce e RENASCE.
    ///
    /// Ver <see cref="Jandirus.Core.Races.Bercos.SementeDoBerco"/>: **zero quer dizer "deriva"**, e
    /// e assim que o personagem criado antes desta regra ganha um berco sem ramo de migracao e sem
    /// flag de "ja escolheu" -- exatamente como a cor da aura fez. Quem nasce a partir de hoje leva
    /// o numero gravado, pra que o berco nao ande no dia em que existir um verb de renomear.
    ///
    /// NAO SE GUARDA O PLANETA, e isso e a regra e nao economia: o endereco `(Sx, Sy, K)` e o nome
    /// e a seed do mundo sao todos DERIVAVEIS desta semente mais a raca, a classe e a linhagem, que
    /// ja estao aqui do lado. Gravar o resultado seria uma segunda verdade sobre o mesmo lugar --
    /// e a pergunta "e se as duas divergirem?" nao tem resposta boa (ver o cabecalho do `Berco`).
    /// </summary>
    public ulong SeedDoBerco;

    /// <summary>
    /// O jogador pediu, na criacao, pra nascer num mundo qualquer perto de casa. Ver
    /// <see cref="Jandirus.Core.Races.CharacterDraft.PertoDeCasa"/>.
    ///
    /// PERSISTE PORQUE O RENASCIMENTO USA A MESMA FUNCAO DO NASCIMENTO: sem este bit no disco,
    /// quem escolheu o vizinho nasceria la e ressuscitaria no natal -- os dois caminhos sairiam da
    /// mesma funcao com argumentos diferentes, que e o jeito mais silencioso de a regra divergir.
    /// </summary>
    public bool PertoDeCasa;

    public float X, Y;

    public long CriadoEm, VistoEm;

    /// <summary>
    /// AS TECNICAS DE KI QUE ESTE PERSONAGEM INVENTOU (ate 10). Ver `Core.Skills.TecnicaCustomizada`.
    ///
    /// ============================ NULAVEL, E NO FIM, E ISSO E MEDIDO ============================
    /// No original elas persistem porque `mob/var/list/customattacks` nao e `tmp` e o `mob/Write`
    /// nao filtra nada -- viajam no savefile do mob. Aqui a mesma coisa precisa de um campo, e a
    /// forma dele nao e gosto: **campo NOVO, ANULAVEL, no FIM da classe e seguro**; o que apaga
    /// conta e mudar o TIPO de um item de lista que ja esta no disco (foi o que quase aconteceu com
    /// a cor de roupa -- ver o registro `dbclimax-port-cor-de-roupa`).
    ///
    /// Nulo quer dizer "save escrito antes disto existir", e ele cai no mesmo lugar que uma lista
    /// vazia: ninguem inventou tecnica nenhuma. Nao ha ramo de migracao, pela mesma razao da cor de
    /// aura e do berco -- os dois casos sao indistinguiveis E devem ser tratados igual.
    ///
    /// E ELA E GRAVADA EM `DeJogador`. Escrever o campo aqui e esquecer a linha de la e como os
    /// `Limiares` sumiram do disco por meses: o metodo monta o save do ZERO a cada `Persistir`, e
    /// campo nao escrito e campo apagado.
    /// ==========================================================================================
    /// </summary>
    public List<Jandirus.Core.Skills.TecnicaCustomizada>? Customizadas;

    // ============================ A SALA DO TEMPO ============================
    // Tres campos NOVOS, no FIM da classe e de tipo de VALOR -- a forma segura descrita no bloco
    // das `Customizadas` logo acima. Um save gravado antes disto existir chega com 0/false, e os
    // tres defaults sao exatamente a resposta certa pra "este personagem nunca entrou na sala":
    // nunca visitou, nao tem chave, nao esta preso. Nao ha ramo de migracao porque nao ha nada
    // que distinguir.
    //
    // O QUE CADA UM SEGURA, e o motivo de nenhum poder morrer no logout, esta em `ServerPlayer`
    // (`GameServer.cs`) e em `GameServer.SalaDoTempo.cs`.

    /// <summary>`htc_last_visit`: quando entrou na Sala do Tempo pela ultima vez (Unix ms).</summary>
    public long SalaUltimaEntrada;

    /// <summary>`permission`: a chave do Guardiao da Terra, boa por UMA entrada.</summary>
    public bool SalaAutorizada;

    /// <summary>Ficou preso na sala. Regra do dono (13.6c): relogar NAO solta.</summary>
    public bool SalaPreso;

    /// <summary>
    /// `htc_session_years`: quantos DIAS IN-GAME esta sessao ja gastou. O DM guarda isto no save
    /// pelo mesmo motivo, e escreve o motivo do lado ("relogar la dentro NAO zera a conta e
    /// re-ganha 2 anos"). Zero num save antigo = "sessao nenhuma", que e a resposta certa.
    /// </summary>
    public double SalaDiasDaSessao;

    /// <summary>
    /// A TECLA C DESTE PERSONAGEM PASSA PELOS GRADES DO SSJ1? Ver
    /// <see cref="Jandirus.Core.Forms.EstadoDeForma.GradesLigados"/> e o verb `graus`.
    ///
    /// ============================ CAMPO NOVO, ANULAVEL E NO FIM -- A FORMA SEGURA ============================
    /// A mesma receita das `Customizadas` la em cima, e pelo mesmo motivo: o que apaga conta e mudar
    /// o TIPO de um item de lista que ja esta no disco; campo novo no fim so chega ausente. Aqui o
    /// nulo AINDA por cima diz alguma coisa -- "este save e de antes do verb existir" --, e quem le
    /// (`GameServer.RestaurarFormaEDisciplina`) o traduz pra LIGADO, que e o comportamento que aquele
    /// personagem ja tinha ontem. Um `bool` cru chegaria `false` e teria DESLIGADO os grades de todo
    /// mundo do servidor calado, no primeiro login depois desta linha.
    /// =======================================================================================================
    ///
    /// E ELE E GRAVADO EM `DeJogador`: este arquivo ja explicou duas vezes (nas `Customizadas` e nos
    /// `Limiares`) que o metodo monta o save do ZERO e campo nao escrito e campo APAGADO.
    /// </summary>
    public bool? GradesLigados;
}

/// <summary>
/// Uma CONTA no servidor. Guarda a credencial e ate tres personagens.
///
/// O modelo e o do Project Zomboid, e foi decisao do dono: nao existe conta global -- em cada
/// servidor voce tem um perfil proprio, e nele cabem tres personagens. E tambem o que o BYOND
/// fazia (`Save/&lt;ckey&gt;/save1..3.dbcsav`).
/// </summary>
public sealed class AccountSave
{
    public string Conta = "";

    /// <summary>Sal e hash da senha. A senha em si NUNCA e gravada.</summary>
    public string Sal = "";
    public string Hash = "";

    public long CriadaEm, VistoEm;

    /// <summary>
    /// ESTA CONTA E DE ADMINISTRADOR.
    ///
    /// ============================ POR QUE NA CONTA, E NAO NO PERSONAGEM ============================
    /// O original amarra admin ao `ckey` -- a IDENTIDADE de quem joga -- e nao ao mob
    /// (`Admin_Check.dm`: `Admin1s.Add(trueckey)`, `world.SetConfig("APP/admin", "[ckey]", ...)`).
    /// Faz sentido: promover alguem e dizer "confio nesta PESSOA", e a pessoa tem tres personagens.
    /// Amarrar ao slot obrigaria a promover tres vezes e deixaria um buraco na hora em que ela
    /// criasse o quarto.
    ///
    /// Vai no arquivo da conta, ao lado do hash da senha, porque e a mesma coisa que ele guarda:
    /// quem e voce neste servidor. Assim promover alguem OFFLINE e so carregar, marcar e gravar.
    /// ==============================================================================================
    ///
    /// O HOST NAO PRECISA DISTO. Quem conecta da propria maquina do servidor ja entra admin por
    /// endereco (ver `GameServer.EhHost`) -- este campo e pra dar admin a OUTRA pessoa.
    /// </summary>
    public bool Admin;

    /// <summary>
    /// BANIDA: nao entra mais. O `Ban()` do original (`Punishments.dm`) guardava numa lista de
    /// ckeys em savefile; aqui mora na propria conta, que e onde a identidade ja esta.
    /// </summary>
    public bool Banida;
    public string MotivoDoBanimento = "";

    /// <summary>
    /// CALADA: nao fala em canal nenhum. O `Mute` do original.
    ///
    /// Mora aqui, e nao num conjunto em memoria, pelo mesmo motivo do banimento: a punicao tem que
    /// sobreviver ao reinicio do servidor. Com o mute so na RAM bastava o dono fechar o jogo (o que
    /// derruba o servidor no fluxo "Hospedar") pra o spammer voltar a falar -- e nem log ficava.
    /// Estando na conta, tambem da pra calar e descalar quem esta OFFLINE.
    /// </summary>
    public bool Calada;

    /// <summary>Os tres slots. Nulo = vazio.</summary>
    public CharacterSave?[] Slots = new CharacterSave?[AccountStore.Slots];
}

/// <summary>
/// PERSISTENCIA POR SERVIDOR. Um arquivo JSON por conta, na pasta de dados do servidor.
///
/// SENHA: guardamos SAL + HASH (PBKDF2-SHA256, 100 mil rodadas). A senha em texto nunca toca
/// o disco nem o log. Nao e um sistema de contas serio -- e um cadeado pra ninguem entrar com
/// o personagem do outro, que e o que "login por servidor" precisa ser.
/// </summary>
public sealed class AccountStore(string pasta)
{
    public const int Slots = 3;

    private const int Rodadas = 100_000;
    private const int TamanhoHash = 32;
    private const int TamanhoSal = 16;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        IncludeFields = true,
        WriteIndented = true,
    };

    public string Pasta { get; } = pasta;

    private string Caminho(string conta) => Path.Combine(Pasta, Arquivo(conta) + ".json");

    /// <summary>
    /// Nome de arquivo seguro a partir do nome da conta. Sem isto, um nome com "../" ou ":"
    /// escreveria fora da pasta -- e o nome vem do cliente.
    ///
    /// PUBLICO porque a limpeza total precisa fazer a pergunta inversa ("este arquivo da pasta e a
    /// conta que ele diz ser?") pelo MESMO saneamento que gravou o nome. Uma segunda copia da regra
    /// responderia "sim" pra arquivos que este metodo nunca produziria.
    /// </summary>
    public static string NomeDeArquivo(string nome) => Arquivo(nome);

    private static string Arquivo(string nome)
    {
        var sb = new StringBuilder();
        foreach (char c in nome.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        string s = sb.ToString();
        return s.Length == 0 ? "sem_nome" : s;
    }

    public bool Existe(string conta) => File.Exists(Caminho(conta));

    public AccountSave? Carregar(string conta)
    {
        string p = Caminho(conta);
        if (!File.Exists(p)) return null;
        try
        {
            AccountSave? a = JsonSerializer.Deserialize<AccountSave>(File.ReadAllText(p), Opcoes);
            if (a == null) return null;
            // save de uma versao com menos slots nao pode explodir na primeira indexacao
            if (a.Slots.Length < Slots) Array.Resize(ref a.Slots, Slots);
            return a;
        }
        catch (Exception e)
        {
            // save corrompido nao pode virar conta nova por cima da antiga
            Console.Error.WriteLine($"[store] conta ilegivel '{conta}': {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Grava. Escreve num temporario e RENOMEIA por cima: se o processo cair no meio, a conta
    /// antiga continua inteira em vez de virar um arquivo pela metade.
    /// </summary>
    public void Gravar(AccountSave a)
    {
        Directory.CreateDirectory(Pasta);
        string destino = Caminho(a.Conta);
        string tmp = destino + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(a, Opcoes), new UTF8Encoding(false));
        File.Move(tmp, destino, overwrite: true);
    }

    /// <summary>
    /// OS ARQUIVOS DA PASTA QUE NAO SAO CONTA -- e esta lista **nao e escrita a mao**.
    ///
    /// ============================ POR QUE ISTO PRECISA EXISTIR ============================
    /// A pasta de saves guarda mais do que contas: o `mundo.json` (as construcoes de pe) mora ali
    /// tambem, e ele e um ARRAY -- ler como <see cref="AccountSave"/> estoura no parser.
    ///
    /// Isso ficou invisivel enquanto ninguem varria a pasta: `Carregar` sempre foi chamado com um
    /// nome de conta conhecido. Assim que o painel de admin passou a LISTAR tudo, cada abertura
    /// dele cuspia tres linhas de "conta ilegivel" no console -- e um erro que aparece toda hora
    /// e um erro que ninguem le mais, inclusive os de verdade.
    /// ======================================================================================
    ///
    /// ============================ ELA ERA UMA LISTA A MAO, E JA TINHA APODRECIDO ============================
    /// Ate aqui os quatro nomes estavam cravados neste arquivo. Tres sistemas nasceram depois e
    /// ninguem lembrou de voltar: `conquista.json`, `naves.json` e `missoes-de-cargo.json` ficaram de
    /// fora. O resultado media-se: `Todas()` tentava ler a lista de naves (um ARRAY) como conta e
    /// cuspia "conta ilegivel" a cada abertura do painel de admin -- exatamente o ruido que o
    /// paragrafo acima jura ter resolvido --, e `Quantas()` inflava a contagem de contas do boot em
    /// tres. Pior: `NomeReservado` tambem le desta lista, entao **"naves" e "conquista" eram nomes de
    /// conta alcancaveis** (o saneamento so mata o hifen), e quem logasse com um deles gravaria a
    /// propria conta por cima da frota, ou dos dominios, do servidor inteiro.
    ///
    /// Agora quem preenche e o REGISTRO DOS SISTEMAS DO MUNDO (`GameServer.Limpeza.cs`): o mesmo
    /// lugar onde um sistema declara o que ele persiste. Um arquivo novo entra aqui no dia em que
    /// entra la, sem ninguem lembrar de nada -- e a bancada `--wipeteste` reprova quando alguem
    /// grava na pasta sem se declarar.
    /// ====================================================================================================
    /// </summary>
    private static readonly HashSet<string> NaoSaoContas = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// "ESTE ARQUIVO E MEU, E NAO E CONTA." Chamado pelo registro dos sistemas do mundo, no boot.
    ///
    /// Idempotente de proposito: o registro roda uma vez por processo, mas as bancadas sobem o
    /// servidor mais de uma vez e uma segunda inscricao nao pode virar erro.
    /// </summary>
    public static void ReservarArquivo(string arquivo)
    {
        if (arquivo.Length > 0) NaoSaoContas.Add(arquivo);
    }

    /// <summary>Os nomes reservados hoje. Existe pra bancada conferir o que o registro inscreveu.</summary>
    public static IReadOnlyCollection<string> ArquivosReservados => NaoSaoContas;

    private static bool EhArquivoDeConta(string caminho) =>
        !NaoSaoContas.Contains(Path.GetFileName(caminho));

    /// <summary>
    /// ESTE NOME DE CONTA COLIDE COM UM ARQUIVO DO SERVIDOR?
    ///
    /// O arquivo de uma conta e o nome dela saneado + ".json", na MESMA pasta em que o mundo mora.
    /// Ou seja: a conta "mundo" -- ou "Mundo", ou "MUNDO" -- aponta pro `mundo.json`. Quem logasse
    /// com esse nome faria o servidor gravar uma conta por cima das construcoes de todo mundo.
    ///
    /// Confere pela forma SANEADA, e nao pelo texto cru, porque e ela que vira caminho.
    ///
    /// ============================ O QUE O SANEAMENTO FAZ, E O QUE ELE **NAO** FAZ ============================
    /// Ele TROCA cada caractere que nao e letra nem digito por '_', **um por um**. Ele nao apaga e
    /// nao junta: "mu ndo" vira `mu_ndo.json`, que e um arquivo diferente e inofensivo.
    ///
    /// Esta linha ja disse o contrario ("troca tudo por '_' e junta"), e a diferenca importa porque e
    /// dela que sai a conclusao de que o hifen protege o `planetas-mortos.json` e o
    /// `missoes-de-cargo.json`: um nome de conta nunca produz um hifen, entao esses dois sao
    /// inalcancaveis por construcao. A afirmacao errada foi pega pela bancada `--wipeteste`, que
    /// tinha acreditado nela.
    /// ====================================================================================================
    /// </summary>
    public static bool NomeReservado(string conta) =>
        NaoSaoContas.Contains(Arquivo(conta) + ".json");

    private IEnumerable<string> ArquivosDeConta() =>
        Directory.Exists(Pasta) ? Directory.GetFiles(Pasta, "*.json").Where(EhArquivoDeConta) : [];

    /// <summary>
    /// TUDO O QUE ESTA NA PASTA, caminho cheio. Sem filtro nenhum -- inclusive `.txt`, `.log` e o
    /// `.tmp` orfao de uma escrita atomica interrompida.
    ///
    /// E o crivo da limpeza total e da bancada dela: as duas precisam ver o que EXISTE, e nao o que
    /// alguem se lembrou de listar. Um filtro aqui seria a lista a mao voltando pela porta dos
    /// fundos -- o arquivo que ninguem previu e justamente o que tem que aparecer.
    /// </summary>
    public IEnumerable<string> TodosOsArquivos() =>
        Directory.Exists(Pasta) ? Directory.GetFiles(Pasta) : [];

    public int Quantas() => ArquivosDeConta().Count();

    /// <summary>
    /// TODAS AS CONTAS DO SERVIDOR, lidas do disco.
    ///
    /// Existe pro painel de admin: promover alguem exige poder VER quem existe, inclusive quem
    /// nao esta online agora -- que e o caso normal (o dono do servidor promove um amigo que
    /// jogou ontem). Le a pasta inteira a cada chamada de proposito: sao dezenas de arquivos
    /// pequenos, e um cache aqui seria uma copia pra manter em sincronia com sete pontos de
    /// gravacao. So o painel chama, e so quando o admin abre a aba.
    /// </summary>
    public List<AccountSave> Todas()
    {
        var l = new List<AccountSave>();
        foreach (string f in ArquivosDeConta())
        {
            try
            {
                AccountSave? a = JsonSerializer.Deserialize<AccountSave>(File.ReadAllText(f), Opcoes);
                if (a == null || a.Conta.Length == 0) continue;
                if (a.Slots.Length < Slots) Array.Resize(ref a.Slots, Slots);
                l.Add(a);
            }
            catch (Exception e)
            {
                // uma conta ilegivel nao pode esconder as outras do painel
                Console.Error.WriteLine($"[store] conta ilegivel '{f}': {e.Message}");
            }
        }
        return [.. l.OrderBy(a => a.Conta, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Acha a conta por NOME DE CONTA ou por NOME DE PERSONAGEM.
    ///
    /// Os dois porque o admin conhece as duas coisas por caminhos diferentes: a conta ele ve no
    /// painel, o personagem ele ve andando na tela. Obrigar a traduzir um no outro seria obrigar
    /// a decorar. Personagem so casa com nome inteiro -- prefixo casaria "Go" com "Goku" e com
    /// "Gohan", e o verb que promove nao pode acertar o alvo errado.
    /// </summary>
    public AccountSave? Achar(string quem) => Achar(quem, out _);

    /// <summary>
    /// Acha a conta por NOME DE CONTA ou por NOME DE PERSONAGEM, e AVISA quando ha empate.
    ///
    /// ============================ POR QUE O EMPATE IMPORTA ============================
    /// Nome de personagem NAO e unico: a criacao so recusa nome repetido entre quem esta online
    /// naquele instante. Duas contas podem ter um "Goku" cada uma.
    ///
    /// Numa busca isso seria chato; num verbo que muda PRIVILEGIO e um roubo. Bastava criar uma
    /// conta qualquer com um personagem homonimo ao de alguem que o admin fosse promover: a
    /// varredura devolve a PRIMEIRA em ordem alfabetica de conta, e a promocao (ou o banimento)
    /// cai na pessoa errada -- em silencio, porque o painel confirma pelo nome da conta que o
    /// admin nem olhou.
    ///
    /// Por isso: nome de CONTA sempre vence (e unico, e o arquivo), e nome de PERSONAGEM so
    /// resolve quando ha um candidato so. Havendo mais, quem chamou recusa e pede pra desambiguar.
    /// =================================================================================
    /// </summary>
    public AccountSave? Achar(string quem, out List<string> empate)
    {
        empate = [];
        quem = quem.Trim();
        if (quem.Length == 0) return null;

        // pelo nome da CONTA primeiro: e unico, e nao ha o que desempatar
        AccountSave? direta = Carregar(quem);
        if (direta != null) return direta;

        var candidatas = new List<AccountSave>();
        foreach (AccountSave a in Todas())
        {
            if (string.Equals(a.Conta, quem, StringComparison.OrdinalIgnoreCase)) return a;
            foreach (CharacterSave? s in a.Slots)
                if (s != null && string.Equals(s.Nome, quem, StringComparison.OrdinalIgnoreCase))
                {
                    candidatas.Add(a);
                    break;
                }
        }
        if (candidatas.Count == 1) return candidatas[0];
        empate = [.. candidatas.Select(a => a.Conta)];
        return null;
    }

    // =====================================================================
    // SENHA
    // =====================================================================
    public static (string sal, string hash) Cadastrar(string senha)
    {
        byte[] sal = RandomNumberGenerator.GetBytes(TamanhoSal);
        return (Convert.ToBase64String(sal), Convert.ToBase64String(Derivar(senha, sal)));
    }

    public static bool Confere(AccountSave a, string senha)
    {
        if (a.Sal.Length == 0) return true;   // conta antiga, sem senha: deixa entrar
        try
        {
            byte[] esperado = Convert.FromBase64String(a.Hash);
            byte[] veio = Derivar(senha, Convert.FromBase64String(a.Sal));
            // comparacao em tempo FIXO: comparar byte a byte e sair no primeiro erro vaza,
            // pelo tempo de resposta, quantos bytes estavam certos
            return CryptographicOperations.FixedTimeEquals(esperado, veio);
        }
        catch { return false; }
    }

    private static byte[] Derivar(string senha, byte[] sal) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(senha), sal, Rodadas, HashAlgorithmName.SHA256, TamanhoHash);

    // =====================================================================
    // PONTE COM O JOGADOR VIVO
    // =====================================================================
    public static CharacterSave DeJogador(ServerPlayer pl, long agora) => new()
    {
        Nome = pl.Name,
        Raca = pl.Race,
        Planeta = pl.Planeta,
        Genero = pl.Genero,
        Linhagem = pl.Linhagem,
        Idade = pl.Idade,
        Historia = pl.Historia,
        Porte = pl.Porte,
        Mochila = pl.Mochila,
        Visual = pl.Visual,
        Ficha = pl.Ficha,
        Membros = pl.Combate != null ? GameServer.FotografarCorpo(pl.Combate) : [],
        Skills = pl.Livro != null ? [.. pl.Livro.Aprendidas] : [],
        SkillsEnsinadas = pl.Livro != null ? [.. pl.Livro.Ensinadas] : [],
        SkillsEscolhas = pl.Livro != null ? new Dictionary<string, int>(pl.Livro.Escolhas) : [],
        Niveis = pl.Niveis?.ParaSave() ?? new(),
        Social = pl.Social ?? new(),
        Maestrias = pl.Forma?.Maestria.ParaSave() ?? [],
        Disciplina = pl.UltraInstinct.Aprendida ? 1 : pl.PoderDaDestruicao.Aprendida ? 2 : 0,
        DiscReal = pl.UltraInstinct.Aprendida ? pl.UltraInstinct.Real : pl.PoderDaDestruicao.Real,
        DiscAtual = pl.UltraInstinct.Aprendida ? pl.UltraInstinct.Atual : pl.PoderDaDestruicao.Atual,
        FormasDespertadas = pl.Forma != null ? [.. pl.Forma.Liberadas] : [],
        FormasEstreadas = pl.Forma != null ? [.. pl.Forma.EstreiaVista] : [],

        // A PREFERENCIA DOS GRADES. Vai crua, INCLUSIVE o nulo: um corpo que nunca passou pelo login
        // de jogador (a bancada, o clone) nao tem opiniao, e gravar `true` no lugar dela inventaria
        // uma escolha que ninguem fez. Ver `CharacterSave.GradesLigados`.
        GradesLigados = pl.Forma?.GradesLigados,

        // O DISCIPULADO. O VINCULO em si nao vem pra ca -- ele e do MUNDO e mora no
        // `mestres.txt` (mesmo ciclo de vida do `cargos.txt`); o que e do PERSONAGEM sao estas
        // tres coisas: o que ele viu, a porta que abriram pra ele e o folego de quem ensina.
        FormasVistas = [.. pl.FormasVistas],
        PortasCortadas = pl.Forma != null ? [.. pl.Forma.PortasCortadas] : [],
        MestreRecargaAte = pl.RecargaDeEnsino,

        // OS CHEFES VISTOS. Mesma disciplina dos tres de cima -- e este metodo monta o save do ZERO a
        // cada `Persistir`, entao campo nao escrito e campo APAGADO (foi assim que os `Limiares`
        // sumiram do disco por meses; ver o bloco mais abaixo).
        ChefesVistos = [.. pl.ChefesVistos],

        // A SALA DO TEMPO. As tres escritas AQUI e nao so declaradas la em cima: este metodo monta
        // o save do ZERO a cada `Persistir`, e campo nao escrito e campo APAGADO -- foi assim que
        // os `Limiares` sumiram do disco por meses (ver o bloco logo abaixo).
        SalaUltimaEntrada = pl.SalaUltimaEntrada,
        SalaAutorizada = pl.SalaAutorizada,
        SalaPreso = pl.SalaPreso,
        SalaDiasDaSessao = pl.SalaDiasDaSessao,

        // ============================ ESTA LINHA FALTAVA, E ELA APAGAVA OS LIMIARES ============================
        // Achado escrevendo o berco, e nao e do berco: `Limiares` esta em `CharacterSave` e em
        // `ParaJogador` (o `Entrar` le `c.Limiares`), mas NUNCA foi escrito de volta aqui. Como este
        // metodo monta o save do ZERO a cada `Persistir` -- e o `Entrar` chama `Persistir` na mesma
        // funcao em que acabou de LER os limiares --, o campo virava `new()` no primeiro login e
        // sumia do disco. Todo Saiyajin do servidor perdeu a propria porta de SSJ (o `rand(9,13)/10`
        // do `statsaiyan.dm`) e caiu na constante generica, calado: `Limiares?.Porta(d) is > 0`
        // devolve falso pro objeto vazio e o codigo escorrega pro `d.PortaBp` sem reclamar.
        //
        // E a armadilha nomeada na PARTE 3 do plano ("campo novo que se lista a mao some, calado").
        // Ela mordeu de novo, e e por isso que os dois campos de berco entram JUNTO com esta linha.
        // ==================================================================================================
        Limiares = pl.Forma?.Limiares ?? new(),

        MarcosTotais = pl.Livro?.MarcosTotais ?? 0,
        MarcosLivres = pl.Livro?.MarcosLivres ?? 0,
        Zona = pl.Zone.Name,
        ZonaTipo = pl.Zone.Kind,
        ZonaSeed = pl.Zone.Seed,
        // O BERCO VOLTA PRO DISCO tal como veio. `ParaJogador` ja resolveu o "zero = deriva", entao
        // o que sai daqui e sempre um numero de verdade -- inclusive pro personagem antigo, que
        // grava hoje o berco que ele sempre teve sem nunca ter tido o campo.
        SeedDoBerco = pl.SeedDoBerco,
        PertoDeCasa = pl.PertoDeCasa,

        // AS TECNICAS INVENTADAS. Lista NOVA e nao a referencia viva: `Persistir` roda com o
        // jogador em jogo, e guardar a mesma lista que o servidor continua editando faria o JSON
        // ser serializado enquanto alguem a altera. Os itens vao por referencia de proposito --
        // eles nao mudam depois de salvos (a mesa edita uma COPIA; ver `TecnicaCustomizada`).
        Customizadas = pl.Customizadas.Count > 0 ? [.. pl.Customizadas] : null,

        X = pl.Pos.X,
        Y = pl.Pos.Y,
        CriadoEm = pl.CriadoEm,
        VistoEm = agora,
    };

    public static void ParaJogador(CharacterSave s, ServerPlayer pl)
    {
        pl.Name = s.Nome;
        pl.Race = s.Raca;
        pl.Planeta = s.Planeta;
        pl.Genero = s.Genero;
        pl.Linhagem = s.Linhagem;
        pl.Idade = s.Idade;
        pl.Historia = s.Historia;
        pl.Porte = s.Porte.Length > 0 ? s.Porte : "Medium";

        // SANEIA NA CARGA, e nao na hora de desenhar: um id que o catalogo nao conhece mais (item
        // renomeado, item removido) vira um slot que a tela nao sabe desenhar e o menu nao sabe
        // usar -- ocupando espaco pra sempre. Ver `Inventario.Sanear`.
        pl.Mochila = s.Mochila ?? new();
        pl.Mochila.Sanear();   // save antigo nao tem porte

        // SAVE ANTIGO NAO TEM CONVIVIO: o `?? new()` e o que separa "nunca conheceu ninguem" de
        // uma referencia nula que estouraria no primeiro tique de proximidade.
        pl.Social = s.Social ?? new();
        pl.Visual = s.Visual;

        // ============================ A COR DA AURA: SAVE ANTIGO NAO TEM, E NAO PRECISA TER ============================
        // O `??=` E A MIGRACAO INTEIRA. Nao ha ramo "personagem velho" nenhum: a cor e uma funcao
        // PURA de nome + instante de criacao (ver `CorDeAura.De`), e essa dupla esta gravada em
        // todo save desde sempre. Quem nasceu ontem e quem nasceu antes desta funcionalidade caem
        // na mesma conta e recebem a mesma cor -- todo login, pra sempre.
        //
        // AQUI, E NAO NA HORA DE DESENHAR, pela mesma razao que o `Mochila.Sanear` logo acima:
        // este metodo e o funil unico "save -> jogador em jogo" (o `Entrar` e o unico chamador), e
        // dele a cor sai de graca pro `JoinAccepted`, pro `PeerLook` de todo mundo da zona e pro
        // clone (que copia o `Visual` do dono).
        //
        // E ELA VAI PRO DISCO no primeiro `Persistir`, o que e desejado e nao efeito colateral: o
        // campo e o OVERRIDE (o dia do verb `Aura_Color()` do DM escreve nele). Gravado ou
        // derivado, o valor e o mesmo -- por isso apagar o campo do JSON a mao devolve a MESMA cor,
        // que e como esta migracao foi provada.
        // ==========================================================================================
        pl.Visual.CorAura ??= Jandirus.Core.Appearance.CorDeAura.De(s.Nome, s.CriadoEm);

        // ============================ O BERCO: MESMA MIGRACAO, MESMO MOTIVO ============================
        // Zero = save de antes do berco (ou save de um personagem que nasceu na Terra cravada). A
        // conta e a MESMA dupla que a cor da aura acabou de usar duas linhas acima -- nome + instante
        // de criacao --, e ela esta em todo save desde sempre. Nao ha ramo "personagem velho": ele
        // ganha o berco que teria se nascesse hoje, e ganha o MESMO em todo login, pra sempre.
        //
        // AQUI, e nao no `BercoDe`, pelo mesmo motivo do `Mochila.Sanear` e da cor: este metodo e o
        // funil unico "save -> jogador em jogo". Derivando aqui, o resto do servidor pode ler
        // `pl.SeedDoBerco` como se ele sempre tivesse existido.
        // ==========================================================================================
        pl.SeedDoBerco = s.SeedDoBerco != 0
            ? s.SeedDoBerco
            : Jandirus.Core.Races.Bercos.SementeDoBerco(s.Nome, s.CriadoEm);
        pl.PertoDeCasa = s.PertoDeCasa;


        pl.Ficha = s.Ficha;
        pl.Class = s.Ficha.Class;

        // A IDADE PRECISA CHEGAR NA FICHA, e nao so no jogador: quem calcula poder e ela, e o
        // divisor de idade le daqui. Sem esta linha a curva de `Envelhecimento` receberia sempre o
        // 18 do valor inicial, e um ancia de 300 anos lutaria como um adulto no auge.
        pl.Ficha.Idade = s.Idade;
        pl.Ficha.Race = s.Raca;   // o mesmo motivo: o divisor de idade e por raca
        pl.Pos = new Vec2(s.X, s.Y);
        pl.CriadoEm = s.CriadoEm;

        // ============================ A SALA DO TEMPO ============================
        // AQUI, e nao no bloco de forma/disciplina do `GameServer`, porque este metodo se declara
        // (tres vezes, la em cima) o **funil unico "save -> jogador em jogo"** -- e a bancada
        // `--salateste` provou que a segunda casa nao basta: os tres campos eram escritos por
        // `DeJogador` e nunca voltavam por aqui, entao uma ida e volta pelo disco devolvia
        // "nunca entrou, sem chave, solto" pra quem estava preso.
        //
        // OS TRES DEFAULTS SAO A MIGRACAO INTEIRA. Save de antes disto existir chega com 0/false,
        // e 0/false e a resposta certa -- nao ha ramo de personagem velho porque nao ha nada que
        // distinguir. Ver `GameServer.SalaDoTempo.cs`.
        pl.SalaUltimaEntrada = s.SalaUltimaEntrada;
        pl.SalaAutorizada = s.SalaAutorizada;
        pl.SalaPreso = s.SalaPreso;
        pl.SalaDiasDaSessao = s.SalaDiasDaSessao;
    }
}
