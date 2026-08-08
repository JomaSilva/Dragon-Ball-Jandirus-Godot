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

    public float X, Y;

    public long CriadoEm, VistoEm;
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
    /// </summary>
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
    /// OS .json DA PASTA QUE NAO SAO CONTA.
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
    /// Lista explicita e nao heuristica: "todo array nao e conta" seria verdade hoje e mentira no
    /// dia em que o formato mudasse, e o silencio esconderia um save corrompido de verdade.
    /// </summary>
    private static readonly HashSet<string> NaoSaoContas =
        new(StringComparer.OrdinalIgnoreCase) { "mundo.json" };

    private static bool EhArquivoDeConta(string caminho) =>
        !NaoSaoContas.Contains(Path.GetFileName(caminho));

    /// <summary>
    /// ESTE NOME DE CONTA COLIDE COM UM ARQUIVO DO SERVIDOR?
    ///
    /// O arquivo de uma conta e o nome dela saneado + ".json", na MESMA pasta em que o mundo mora.
    /// Ou seja: a conta "mundo" (ou "Mundo", ou "mu ndo" -- o saneamento troca tudo por '_' e
    /// junta) aponta pro `mundo.json`. Quem logasse com esse nome faria o servidor gravar uma
    /// conta por cima das construcoes de todo mundo.
    ///
    /// Confere pela forma SANEADA, e nao pelo texto cru, porque e ela que vira caminho.
    /// </summary>
    public static bool NomeReservado(string conta) =>
        NaoSaoContas.Contains(Arquivo(conta) + ".json");

    private IEnumerable<string> ArquivosDeConta() =>
        Directory.Exists(Pasta) ? Directory.GetFiles(Pasta, "*.json").Where(EhArquivoDeConta) : [];

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
        Niveis = pl.Niveis?.ParaSave() ?? new(),
        Social = pl.Social ?? new(),
        Maestrias = pl.Forma?.Maestria.ParaSave() ?? [],
        Disciplina = pl.UltraInstinct.Aprendida ? 1 : pl.PoderDaDestruicao.Aprendida ? 2 : 0,
        DiscReal = pl.UltraInstinct.Aprendida ? pl.UltraInstinct.Real : pl.PoderDaDestruicao.Real,
        DiscAtual = pl.UltraInstinct.Aprendida ? pl.UltraInstinct.Atual : pl.PoderDaDestruicao.Atual,
        FormasDespertadas = pl.Forma != null ? [.. pl.Forma.Liberadas] : [],
        FormasEstreadas = pl.Forma != null ? [.. pl.Forma.EstreiaVista] : [],
        MarcosTotais = pl.Livro?.MarcosTotais ?? 0,
        MarcosLivres = pl.Livro?.MarcosLivres ?? 0,
        Zona = pl.Zone.Name,
        ZonaTipo = pl.Zone.Kind,
        ZonaSeed = pl.Zone.Seed,
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
        pl.Ficha = s.Ficha;
        pl.Class = s.Ficha.Class;

        // A IDADE PRECISA CHEGAR NA FICHA, e nao so no jogador: quem calcula poder e ela, e o
        // divisor de idade le daqui. Sem esta linha a curva de `Envelhecimento` receberia sempre o
        // 18 do valor inicial, e um ancia de 300 anos lutaria como um adulto no auge.
        pl.Ficha.Idade = s.Idade;
        pl.Ficha.Race = s.Raca;   // o mesmo motivo: o divisor de idade e por raca
        pl.Pos = new Vec2(s.X, s.Y);
        pl.CriadoEm = s.CriadoEm;
    }
}
