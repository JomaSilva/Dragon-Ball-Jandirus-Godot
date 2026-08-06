using Godot;

namespace Jandirus.Client;

/// <summary>
/// A TRILHA: que som toca em cada situacao. Todo caminho de audio do jogo mora aqui, e so
/// aqui -- trocar a musica do menu tem que ser uma linha, nao uma cacada.
///
/// DUAS FORMAS DE ESCOLHER, e a diferenca importa:
///
///   * caminho FIXO (`Trilha.Parry`) -- o som e sempre o mesmo;
///   * SACO DE SORTEIO (`Trilha.Menu()`, `Trilha.Acerto(nivel)`) -- sorteia sem repetir ate
///     acabar a lista. Nao e o mesmo que sortear a esmo: `pick()` puro repete a mesma faixa
///     duas, tres vezes seguidas, e quem abre o menu tres vezes ouvindo a mesma musica
///     conclui, com razao, que ela nao esta sorteando.
///
/// AS LISTAS SAO LIDAS DO DISCO, nao escritas na mao. Os nomes de arquivo do jogo original
/// tem espaco, acento e ate travessao tipografico -- transcrever 44 deles a mao e garantir
/// um erro de digitacao que so aparece em runtime como silencio. Quem manda e a PASTA: jogar
/// um .ogg em `Music/Menu ost/` ja o poe na rotacao.
///
/// Os 31 arquivos .mid e 4 .Sfarr do jogo original NAO vieram: o Godot nao le MIDI.
/// </summary>
public static class Trilha
{
    private const string M = "res://Assets/Sounds/Music/";
    private const string A = "res://Assets/Sounds/Ambience/";
    private const string E = "res://Assets/Sounds/Effects/";
    private const string P = E + "Punch Effects/";
    private const string K = E + "Ki Effects/";

    // =====================================================================
    // MUSICA
    // =====================================================================
    /// <summary>
    /// A pasta do tema de MENU. Decisao do dono: a musica de menu sai SO daqui, e cada vez
    /// que o menu abre toca uma diferente.
    /// </summary>
    private static readonly Saco MenuOst = new(M + "Menu ost");

    /// <summary>A pasta das musicas de LUTA. Cada briga comeca com uma faixa diferente.</summary>
    private static readonly Saco BattleOst = new(M + "battle ost");

    /// <summary>Uma faixa de menu, diferente da anterior. Vazio se a pasta nao existir.</summary>
    public static string Menu() => MenuOst.Proxima();

    /// <summary>Uma faixa de combate, diferente da anterior.</summary>
    public static string Combate() => BattleOst.Proxima();

    /// <summary>O som de fundo de cada planeta. Vazio = silencio.</summary>
    public static string? AmbienteDe(string zona) => zona switch
    {
        "Earth" => A + "windy.ogg",
        "Namek" => A + "namek.ogg",
        "Vegeta" => A + "desert.ogg",
        "Hell" => A + "volcanichell.ogg",
        "Heaven" => A + "windy2.ogg",
        "Space" => A + "space.ogg",
        "Arena" => A + "crowdarena.ogg",
        _ => A + "windy.ogg",
    };

    /// <summary>A musica do lugar. Silencio ("") deixa o ambiente falar sozinho.</summary>
    public static string MusicaDe(string zona) => zona switch
    {
        "Hell" => M + "Demon World.mp3",
        _ => "",
    };

    // =====================================================================
    // SOCO
    // =====================================================================
    // Os TRES NIVEIS de impacto sao os do original (`calcs.dm:154-161`): o nivel sai de
    // `min(3, tipo + min(1, combo))`, ou seja um soco leve isolado soa pequeno, o mesmo soco
    // dentro de uma sequencia soa medio, e o golpe pesado soa grande. E o que da a sensacao
    // de a briga ESQUENTAR -- com um som so, dez socos seguidos viram um metronomo.
    private static readonly Saco Pequeno = new([
        P + "ARC_BTL_CMN_Hit_Small-A.ogg",
        P + "ARC_BTL_CMN_Hit_Small-B.ogg",
        P + "hit_s.wav",
        P + "weakkick.ogg",
    ]);

    private static readonly Saco Medio = new([
        P + "ARC_BTL_CMN_Hit_Midle-A.ogg",
        P + "punch_med.ogg",
        P + "mediumpunch.ogg",
        P + "mediumkick.ogg",
        P + "hit_m.wav",
    ]);

    private static readonly Saco Grande = new([
        P + "ARC_BTL_CMN_Hit_Large-A.ogg",
        P + "punch_hvy.ogg",
        P + "hit_l.wav",
        P + "strongkick.ogg",
        P + "strongpunch.ogg",
    ]);

    private static readonly Saco Erro = new([
        P + "meleemiss1.ogg",
        P + "meleemiss2.ogg",
        P + "meleemiss3.ogg",
    ]);

    /// <summary>O baque do impacto. <paramref name="nivel"/> 1 = pequeno, 2 = medio, 3 = grande.</summary>
    public static string Acerto(int nivel) => nivel switch
    {
        >= 3 => Grande.Proxima(),
        2 => Medio.Proxima(),
        _ => Pequeno.Proxima(),
    };

    /// <summary>Soco no ar. E o mesmo som pra errar e pra socar o vazio -- e o mesmo gesto.</summary>
    public static string SocoNoAr() => Erro.Proxima();

    /// <summary>O "sopro" do golpe saindo, que toca junto com o impacto no original.</summary>
    public const string Assobio = P + "meleeflash.ogg";

    /// <summary>
    /// Guarda aparou. DESVIO CONSCIENTE: no original bloquear e MUDO -- o `BlockLimbs.dm`
    /// nao toca nada, e o `parry.ogg` so sai no contra-ataque. Guarda silenciosa nao da
    /// retorno nenhum ao jogador, e como nao ha pose de bloqueio nos .dmi, o som e a unica
    /// coisa que confirma que o ALT funcionou.
    /// </summary>
    public const string Aparou = P + "parry.ogg";

    /// <summary>Bloqueio PERFEITO. No original os dois tocam JUNTOS, e e o que se faz aqui.</summary>
    public const string ContraAtaque = P + "perfectsoundeffect.ogg";
    public const string ContraAtaqueParry = P + "parry.ogg";

    /// <summary>
    /// Corpo batendo no chao: nocaute e morte. E o `groundhit2`, NAO o `groundhit` -- o sem
    /// numero e outro som (o baque do agarrao). Os dois existem, e trocar passa despercebido.
    /// </summary>
    public const string Queda = P + "groundhit2.ogg";

    /// <summary>Membro arrancado.</summary>
    public const string Decepou = E + "swordkill.ogg";

    // =====================================================================
    // MOVIMENTO E ESTADO
    // =====================================================================
    /// <summary>O rasgo do dash de aproximacao.</summary>
    public const string Dash = E + "chainswoop.ogg";

    /// <summary>
    /// O ZANZOKEN. E `teleport.wav` no original -- conferido no `Zanzoken_Dodge`
    /// (`Physical Skills.dm:26-35`), que faz `flick('Zanzoken.dmi', src)` + `Move(...)` +
    /// `emit_Sound('teleport.wav')`.
    ///
    /// Eu tinha usado o `chainswoop` do dash, e o dono ouviu a diferenca. Nao e o mesmo gesto:
    /// investir e atravessar a distancia (ar rasgando); piscar e nao atravessar coisa nenhuma.
    /// </summary>
    public const string Teleporte = E + "teleport.ogg";

    /// <summary>Aura de quem esta carregando/treinando.</summary>
    public const string PowerUp = E + "powerup.wav";

    /// <summary>
    /// O ESTALO DE COMECAR a reunir energia -- um toque so, no instante em que a tecla desce.
    /// E o `emit_Sound('chargeaura.wav')` do `Draw_Energy` (Meditate.dm:181).
    /// </summary>
    public const string CargaInicio = E + "chargeaura.wav";   // Effects/, NAO Ki Effects/

    /// <summary>
    /// O ZUMBIDO CONTINUO de quem esta carregando. Toca EM LACO enquanto durar.
    ///
    /// E o mesmo arquivo do original, no mesmo papel: `Sound.dm:68` monta
    /// `sound('aurapowered.wav', repeat=1, channel=50)` e o liga/desliga conforme alguem em
    /// volta esteja com `poweruprunning`. Aqui o laco e um player dedicado no corpo de quem
    /// carrega, o que resolve de graca a parte que la era um `for(var/mob/M in view(src))` a
    /// cada 0,2 s: quem esta perto ouve porque o som tem posicao.
    /// </summary>
    public const string CargaLaco = K + "aurapowered.wav";

    /// <summary>Zona de dano nova destravada / marco de poder.</summary>
    public const string NovaHabilidade = E + "NEWSKILL.WAV";

    // =====================================================================
    // VOO
    // =====================================================================
    /// <summary>
    /// O IMPULSO DE SAIR DO CHAO. E o `usr.emit_Sound('buku.wav')` do verb `Fly`
    /// (flying.dm:95) -- o arquivo do original, no mesmo instante.
    /// </summary>
    public const string Decolagem = E + "buku.ogg";

    /// <summary>
    /// O BAQUE DE POUSAR. `emit_Sound('buku_land.wav')` -- e o original toca esse mesmo som nos
    /// DOIS jeitos de descer: pousar de proposito (flying.dm:91) e cair de exausto (Stats.dm:422).
    /// Aqui tambem: quem ouve nao precisa saber qual dos dois foi, mas precisa ouvir que acabou.
    /// </summary>
    public const string Pouso = E + "buku_land.ogg";

    // NAO HA SOM DE LACO NO AR, e e decisao do dono: "enquanto ta voando n deveria ter som nenhum".
    // O original tambem e mudo depois do impulso. `db_flying.ogg` esta convertido e nao e usado.

    // =====================================================================
    // O SACO DE SORTEIO
    // =====================================================================
    /// <summary>
    /// Sorteia sem repetir: embaralha a lista, entrega uma por vez, e so re-embaralha quando
    /// acaba. Ao re-embaralhar, garante que a primeira da rodada nova nao e a ultima da
    /// rodada velha -- senao a unica repeticao possivel e justamente a mais audivel.
    /// </summary>
    private sealed class Saco
    {
        private readonly string? _pasta;
        private List<string> _itens;
        private int _i;
        private string _ultima = "";
        private static readonly RandomNumberGenerator Rng = new();

        private static readonly string[] Extensoes = [".ogg", ".mp3", ".wav"];

        public Saco(string[] itens)
        {
            _itens = [.. itens];
            Embaralhar();
        }

        /// <summary>Saco que se enche do CONTEUDO de uma pasta, na primeira vez que e usado.</summary>
        public Saco(string pasta)
        {
            _pasta = pasta;
            _itens = [];
        }

        public string Proxima()
        {
            if (_pasta != null && _itens.Count == 0) Varrer();
            if (_itens.Count == 0) return "";
            if (_itens.Count == 1) return _itens[0];

            if (_i >= _itens.Count) Embaralhar();
            _ultima = _itens[_i++];
            return _ultima;
        }

        private void Embaralhar()
        {
            for (int i = _itens.Count - 1; i > 0; i--)
            {
                int j = (int)(Rng.Randi() % (uint)(i + 1));
                (_itens[i], _itens[j]) = (_itens[j], _itens[i]);
            }
            // a rodada nova nao pode comecar com a mesma faixa que a velha terminou
            if (_itens.Count > 1 && _itens[0] == _ultima)
                (_itens[0], _itens[^1]) = (_itens[^1], _itens[0]);
            _i = 0;
        }

        /// <summary>
        /// Le a pasta. O Godot deixa .ogg/.mp3/.wav passarem pro pacote com o nome original,
        /// entao listar funciona tambem na build exportada -- mas as sobras `.import` e
        /// `.remap` aparecem na listagem e precisam sair.
        /// </summary>
        private void Varrer()
        {
            string[] nomes = DirAccess.GetFilesAt(_pasta!);
            if (nomes.Length == 0)
            {
                GD.PushWarning($"[audio] pasta vazia ou ausente: {_pasta}");
                return;
            }

            var achados = new List<string>();
            foreach (string bruto in nomes)
            {
                string nome = bruto;
                if (nome.EndsWith(".remap")) nome = nome[..^6];
                if (nome.EndsWith(".import")) nome = nome[..^7];

                bool audio = false;
                foreach (string ext in Extensoes)
                    if (nome.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { audio = true; break; }
                if (!audio) continue;

                string caminho = $"{_pasta}/{nome}";
                if (!achados.Contains(caminho)) achados.Add(caminho);
            }

            _itens = achados;
            Embaralhar();
            GD.Print($"[audio] {_pasta}: {_itens.Count} faixas na rotacao");
        }
    }
}
