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
    public List<int> FormasDespertadas = [];

    // onde estava quando saiu
    public string Zona = "Earth";
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

    public int Quantas() => Directory.Exists(Pasta) ? Directory.GetFiles(Pasta, "*.json").Length : 0;

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
        Visual = pl.Visual,
        Ficha = pl.Ficha,
        Membros = pl.Combate != null ? GameServer.FotografarCorpo(pl.Combate) : [],
        Skills = pl.Livro != null ? [.. pl.Livro.Aprendidas] : [],
        Niveis = pl.Niveis?.ParaSave() ?? new(),
        Maestrias = pl.Forma?.Maestria.ParaSave() ?? [],
        FormasDespertadas = pl.Forma != null ? [.. pl.Forma.JaDespertou] : [],
        MarcosTotais = pl.Livro?.MarcosTotais ?? 0,
        MarcosLivres = pl.Livro?.MarcosLivres ?? 0,
        Zona = pl.Zone.Name,
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
        pl.Visual = s.Visual;
        pl.Ficha = s.Ficha;
        pl.Class = s.Ficha.Class;
        pl.Pos = new Vec2(s.X, s.Y);
        pl.CriadoEm = s.CriadoEm;
    }
}
