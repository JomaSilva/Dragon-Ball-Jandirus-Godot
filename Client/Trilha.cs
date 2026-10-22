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
    private const string T = E + "Tech Effects/";

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

    /// <summary>
    /// A faixa que o proximo <see cref="Combate"/> vai devolver, SEM tira-la do saco -- vazio se a pasta nao
    /// existir. E o que deixa o `AudioDirector` le-la numa thread de carga antes de a luta comecar: ver
    /// `AudioDirector.Adiantar`.
    /// </summary>
    public static string ProximaDeCombate() => BattleOst.Espiar();

    /// <summary>
    /// A faixa que o proximo <see cref="Menu"/> vai devolver, SEM tira-la do saco -- vazio se a pasta nao
    /// existir. Irma da <see cref="ProximaDeCombate"/>, e pelo mesmo motivo: e o que deixa o `AudioDirector`
    /// le-la numa thread de carga antes de o ESC abrir.
    /// </summary>
    public static string ProximaDeMenu() => MenuOst.Espiar();

    /// <summary>
    /// A MUSICA DOS PLANETAS (dono, 2026-10-16): *"musicas q vao tocar quando n ta em combate nem em cinematica de
    /// transformaçao, elas tocam como musica padrao nos planetas do jogo"*. E o fundo de todo lugar sem tema proprio (ver
    /// <see cref="MusicaDe"/>), sorteado como o de combate: cada faixa toca UMA vez e a seguinte sai do saco. A pasta e dele --
    /// jogar uma faixa la ja a poe no rodizio.
    /// </summary>
    private static readonly Saco PlanetOst = new(A + "Planet Ambience Music");

    /// <summary>Uma faixa da musica dos planetas, diferente da anterior. Vazio se a pasta nao existir.</summary>
    public static string Planeta() => PlanetOst.Proxima();

    /// <summary>
    /// A faixa que o proximo <see cref="Planeta"/> vai devolver, SEM tira-la do saco -- vazio se a pasta nao existir. Irma da
    /// <see cref="ProximaDeCombate"/>: e o que deixa o `AudioDirector` le-la numa thread de carga antes de ela entrar no ar.
    /// </summary>
    public static string ProximaDePlaneta() => PlanetOst.Espiar();

    /// <summary>O som de fundo de cada planeta. Vazio = silencio.</summary>
    public static string? AmbienteDe(string zona) => zona switch
    {
        "Earth" => A + "windy.ogg",
        "Namek" => A + "namek.ogg",
        // (2026-10-15) O MESMO DA TERRA -- dono: *"o som ambiente do planeta vegeta ta ruim, coloque o mesmo som ambiente
        // q tem na terra"*. Escrito como a REFERENCIA a Terra, e nao como o arquivo: se o da Terra mudar, Vegeta vai junto.
        "Vegeta" => AmbienteDe("Earth"),
        "Hell" => A + "volcanichell.ogg",
        "Heaven" => A + "windy2.ogg",
        "Space" => A + "space.ogg",
        "Arena" => A + "crowdarena.ogg",
        _ => A + "windy.ogg",
    };

    /// <summary>
    /// O TEMA PROPRIO DO LUGAR, que toca em LACO enquanto se esta la. Vazio = o lugar nao tem tema proprio, e o que toca la e a
    /// musica dos planetas em rodizio (<see cref="Planeta"/>) -- quem decide e o `AudioDirector.MusicaDoLugar`.
    ///
    /// O dono (2026-10-16): *"sacred land of korin vai tocar no mapa do lookout, skies above beerus vai tocar no planeta onde o
    /// bills mora, e sacred land of the kais vai tocar no mapa heaven"*, e *"o inferno pode manter a musica atual, o resto vai
    /// usar o planet ambience music"*. Os tres arquivos sao dele, na pasta do ambiente, e se acham por um TRECHO do nome
    /// (<see cref="TemaDaPasta"/>): os nomes trazem ♠ e 【】, e transcreve-los na mao e o erro de digitacao que so aparece como
    /// silencio.
    /// </summary>
    /// <param name="casaDeBills">Este lugar e o mundo gerado onde o Bills mora, e o cliente ja ouviu o endereco (ver
    /// `MenuJogo.CasaDeBills`). Antes de o Bills o revelar ninguem sabe qual mundo e -- e a musica nao o entrega.</param>
    public static string MusicaDe(string zona, bool casaDeBills = false) =>
        casaDeBills ? TemaDaPasta("Skies above Beerus")
        : zona switch
        {
            "Hell" => M + "Demon World.mp3",
            "Lookout" => TemaDaPasta("Sacred Land of Korin"),
            "Heaven" => TemaDaPasta("Sacred Land of the Kais"),
            _ => "",
        };

    /// <summary>
    /// O ARQUIVO DA PASTA DO AMBIENTE CUJO NOME TRAZ <paramref name="trecho"/>, procurado uma vez e guardado. So a pasta de
    /// cima: a subpasta do rodizio nao entra. Vazio -- com aviso -- se nenhum trouxer: o lugar fica com a musica dos planetas, e
    /// o aviso diz qual tema sumiu.
    /// </summary>
    private static string TemaDaPasta(string trecho)
    {
        if (TemasAchados.TryGetValue(trecho, out string? achado)) return achado;
        achado = ArquivosDeAudio(A.TrimEnd('/')).Find(c => c.GetFile().Contains(trecho, StringComparison.OrdinalIgnoreCase)) ?? "";
        if (achado.Length == 0) GD.PushWarning($"[audio] tema de lugar ausente na pasta do ambiente: '{trecho}'");
        TemasAchados[trecho] = achado;
        return achado;
    }

    private static readonly Dictionary<string, string> TemasAchados = [];

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

    /// <summary>
    /// TODAS AS AMOSTRAS DE GOLPE -- a pasta inteira, uns 40 arquivos e 1 MB. E o que o `Aquecimento`
    /// carrega no lobby e SEGURA: ninguem as segurava, entao cada uma saia do cache junto com o ultimo
    /// tocador e voltava do disco, na thread principal, no golpe seguinte que a sorteasse. Medido com
    /// janela (`--diagestouro`, 2026-10-08): com elas ja na memoria a primeira bola da noite caiu de 16
    /// pra 10 a 14 ms, e o relato do primeiro golpe de 17-22 pra 12-18. O que sobrava no golpe nao era
    /// amostra: era a musica de combate, lida do disco na hora -- e que hoje chega antes da luta (ver
    /// `AudioDirector.Adiantar`).
    /// </summary>
    public static List<string> AmostrasDeGolpe() => ArquivosDeAudio(P.TrimEnd('/'));

    private static readonly string[] Extensoes = [".ogg", ".mp3", ".wav"];

    /// <summary>
    /// OS ARQUIVOS DE AUDIO DE UMA PASTA. O Godot deixa .ogg/.mp3/.wav passarem pro pacote com o nome
    /// original, entao listar funciona tambem na build exportada -- mas as sobras `.import` e `.remap`
    /// aparecem na listagem e precisam sair.
    /// </summary>
    private static List<string> ArquivosDeAudio(string pasta)
    {
        var achados = new List<string>();
        foreach (string bruto in DirAccess.GetFilesAt(pasta))
        {
            string nome = bruto;
            if (nome.EndsWith(".remap")) nome = nome[..^6];
            if (nome.EndsWith(".import")) nome = nome[..^7];

            bool audio = false;
            foreach (string ext in Extensoes)
                if (nome.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { audio = true; break; }
            if (!audio) continue;

            string caminho = $"{pasta}/{nome}";
            if (!achados.Contains(caminho)) achados.Add(caminho);
        }
        return achados;
    }

    /// <summary>
    /// TODO SOM DE CAMINHO FIXO DESTA CLASSE -- o valor de cada constante publica dela, sem repeticao. E o que deixa
    /// o `Aquecimento` trazer no lobby, numa thread, os sons que os gestos tocam (ver `Aquecimento.SonsDeGesto`).
    ///
    /// SAI DO SIMBOLO, e nao de uma segunda lista escrita ao lado: constante de caminho nova nesta classe ja esta
    /// nesta conta. (Os de SACO DE SORTEIO nao sao constantes, e saem todos da pasta de <see cref="AmostrasDeGolpe"/>.)
    /// </summary>
    public static List<string> EfeitosFixos()
    {
        var caminhos = new List<string>();
        foreach (System.Reflection.FieldInfo campo in typeof(Trilha).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            if (campo.GetValue(null) is string { Length: > 0 } caminho && !caminhos.Contains(caminho)) caminhos.Add(caminho);
        return caminhos;
    }

    /// <summary>O "sopro" do golpe saindo, que toca junto com o impacto no original.</summary>
    public const string Assobio = P + "meleeflash.ogg";

    /// <summary>O Instant Transmission (dono, 2026-10-21): o piscar usa o efeito do desvio, e este som no lugar do assobio.</summary>
    public const string TransmissaoInstantanea = E + "instanttransmission.wav";

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

    /// <summary>
    /// (2026-10-17) A CAUDA DO BIO-ANDROIDE CRAVADA, BEBENDO -- o `emit_Sound('absorb.wav')` do `Bio_Absorb` (`Absorption.dm:157`), um toque
    /// so, no instante em que a vitima comeca a secar (`World.MarcarSecando`). Estava importado e nenhum `.cs` o citava.
    /// </summary>
    public const string Absorcao = E + "absorb.wav";

    // =====================================================================
    // MOVIMENTO E ESTADO
    // =====================================================================
    /// <summary>O rasgo do dash de aproximacao.</summary>
    public const string Dash = E + "chainswoop.ogg";

    /// <summary>
    /// PEDRAS SE SOLTANDO DO CHAO -- o `rockmoving.wav` que abre a cinematica do SSJ1 e a do SSJ2
    /// (ver `Cinematicas.Ssj1`, que cita o nome do DM no comentario do beat).
    ///
    /// ============================ ELE ESTAVA ESCRITO E NAO TOCAVA ============================
    /// O arquivo esta convertido e importado desde o pipeline, e as duas cenas pedem
    /// `Som: "rockmoving"` -- mas o resolvedor de `Transformacao` nao tinha caso pra ele e o `_ =>`
    /// devolvia o <see cref="Dash"/>. Resultado: a estreia do Super Saiyajin abria com o SWOOSH do
    /// dash em vez do chao rachando, e nada no jogo dizia que o som pedido nao existia -- um som
    /// errado toca igual a um som certo.
    ///
    /// Quem achou foi a bancada de forma, escrevendo a checagem "todo `Beat.Som` resolve". Por isso
    /// o resolvedor agora devolve NULO pro nome desconhecido (ver `Transformacao.CaminhoDoSom`):
    /// silencio + aviso e detectavel; substituicao silenciosa nao e.
    /// ========================================================================================
    /// </summary>
    public const string PedrasRolando = E + "rockmoving.ogg";

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
    /// OS DOIS VIRAM UM -- o `emit_Sound('fusion.wav')` do original, e ele e o UNICO som que o
    /// `Fusion.dm` inteiro toca.
    ///
    /// Ele cai no mesmo instante nos dois caminhos do DM: logo depois do `sleep(40)` da Potara e
    /// antes do `Fuse()` (`Fusion.dm:679`), e junto do anuncio da Danca (`:728`). Por isso ele e o
    /// `Som` do beat que ASSUME em `Cinematicas.Fusao` -- e nao de um beat qualquer da subida.
    ///
    /// ============================ ESTAVA NO DISCO E SEM LEITOR ============================
    /// `fusion.wav` esta convertido e com `.import` desde o pipeline e nao tinha uma linha de `.cs`
    /// que o pedisse -- o mesmo modo de falha do `rockmoving`, do `ssg.wav` e do `ssb.wav`, que este
    /// arquivo ja documenta tres vezes: arte pronta que ninguem pede nao da erro, so nao soa.
    ///
    /// O IRMAO DELE FICA DE FORA: `Effects/voiced/fusionvoiced.wav` e a versao com o grito
    /// ("FU-SION-HA!"), e ela nao e pedida por linha nenhuma do DM tambem. Trazer os dois faria a
    /// cena tocar duas vozes por cima uma da outra; trazer so o com voz poria uma fala em ingles no
    /// unico som de um jogo que fala portugues.
    /// ==================================================================================
    /// </summary>
    public const string Fusao = E + "fusion.wav";

    /// <summary>
    /// O ESTALO DE COMECAR a reunir energia -- um toque so, no instante em que a tecla desce.
    /// E o `emit_Sound('chargeaura.wav')` do `Draw_Energy` (Meditate.dm:181).
    /// </summary>
    public const string CargaInicio = E + "chargeaura.wav";   // Effects/, NAO Ki Effects/

    /// <summary>
    /// O ESTOURO DO KAIO-KEN -- `emit_Sound('kaioken.wav')` no `Buff()` (`kaioken.dm:175`), um toque so,
    /// no instante em que a aura vermelha acende. O arquivo estava importado e nenhum `.cs` o citava.
    /// </summary>
    public const string Kaioken = K + "kaioken.wav";

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

    /// <summary>
    /// O KI DIVINO CHEGANDO -- o `ssg.wav` de `startbuff(/obj/buff/Ritual_God)` (`GodRitual.dm:78`),
    /// que e a ultima linha do ritual que concede o poder de deus. Toca tambem em
    /// `GodkiIncreaseScene()` (`godki.dm:157`) e no verb `God_Ki()` (`:207`).
    ///
    /// ============================ ELE ESTAVA IMPORTADO E MORTO ============================
    /// O arquivo esta em `Assets/Sounds/Effects/Ki Effects/ssg.wav` com o `.import` ao lado desde o
    /// pipeline -- e ate agora nenhum `.cs` do projeto o citava. E a MESMA familia de defeito da
    /// `FieryGod.tres` e da `FieryGodBlue.tres` (convertidas, importadas, sem um leitor), e do
    /// `rockmoving.wav` (pedido pelo beat e devolvido como `Dash` pelo resolvedor): arte que chegou e
    /// nunca foi ligada nao aparece em lugar nenhum -- nem no jogo, nem numa lista de pendencias.
    ///
    /// Quem o abriu foi a cena do ritual, que tocava `powerup.wav` no beat do buff porque era o nome
    /// que o resolvedor tinha. Ver `Cinematicas.RitualDivino`.
    /// ==================================================================================
    /// </summary>
    public const string KiDivino = K + "ssg.wav";

    /// <summary>
    /// A CHEGADA DO SUPER SAIYAJIN BLUE -- o `ssb.wav` de `do_first_godki_appearance()`
    /// (`buffs.dm:59-66`), o proc que roda quando o buff de Super Saiyajin nasce com o God Ki ja
    /// aceso. Ele NAO toca no `do_godki_appearance()` (`:68-74`), a versao de quem ja era SSJ e
    /// acendeu o divino depois -- la ha o mesmo clarao e nenhum som.
    ///
    /// Mesma historia do <see cref="KiDivino"/>: importado, com `.import`, e sem leitor nenhum.
    /// </summary>
    public const string KiDivinoAzul = K + "ssb.wav";

    /// <summary>Zona de dano nova destravada / marco de poder.</summary>
    public const string NovaHabilidade = E + "NEWSKILL.WAV";

    /// <summary>
    /// O RUGIDO DO MACACO. `Oozaru.dm:158` -- `if(prob(1)&&prob(50)) container.emit_Sound('Roar.wav')`,
    /// no `Loop()` da forma: no DM ele e um rugido ALEATORIO e raro de quem ja esta transformado.
    ///
    /// Aqui ele estreia no instante em que o macaco NASCE (o beat `Assumir` da cena do Oozaru), que
    /// e o unico momento em que ele conta alguma coisa a quem esta olhando. O rugido aleatorio do
    /// `Loop()` continua nao portado -- ver a divida do tique da forma.
    /// </summary>
    public const string Rugido = E + "Roar.wav";

    /// <summary>
    /// O SOM DO ULTRA INSTINTO (2026-10-17, pedido do dono): toda transformacao numa forma da linha do instinto, menos a estreia
    /// (que tem a cena cheia e o tema). Nao e do DM -- la o despertar so tinha tema. Ver `Cinematicas.SomDoDegrau`.
    /// </summary>
    public const string UltraInstinto = E + "UltraInstinctSoundEffect.ogg";

    /// <summary>
    /// A EXPLOSAO -- o `explosion.wav` do original (`Tech/Bombs.dm:42`).
    ///
    /// ============================ ELE MUDA DE PAPEL AQUI, E ESTA MARCADO ============================
    /// No DM este arquivo so toca quando uma BOMBA estoura. Neste port ele estreia num segundo lugar:
    /// o penultimo beat da cinematica do SSJ3, colado na segunda descarga do ceu.
    ///
    /// O motivo e de ritmo e nao de fidelidade. A cena do SSJ3 tem 140 s e termina em sete gritos
    /// separados por silencios de doze segundos; o unico som que ela tinha da metade pro fim era o
    /// `powerup` dos 33,0 s. Sem nada novo no ouvido, os ultimos trinta segundos soam iguais aos
    /// primeiros trinta -- e a cena mais longa do jogo acaba sem clímax sonoro.
    ///
    /// **Apagar o `Som: "explosao"` daquele beat devolve o silencio do DM sem mexer em mais nada.**
    /// ============================================================================================
    /// </summary>
    public const string Explosao = E + "explosion.ogg";

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

    /// <summary>
    /// O SOPRO DO KIAI: `emit_Sound('scouterexplode.ogg')` (`Ki2.0/Kiai.dm:53`) -- toca sempre, pegue
    /// alguem ou nao. E o mesmo som que a Onda de Choque e a Deflexao usam la (`:82`, `:118`).
    /// </summary>
    public const string Kiai = T + "scouterexplode.ogg";

    /// <summary>A LAMINA DE AR saindo: `emit_Sound('fire_kiblast.wav')` (`Kiai.dm:37`), so no `if(!mobaff)`.</summary>
    public const string LaminaDeAr = K + "fire_kiblast.ogg";

    // =====================================================================
    // OS ATAQUES DE KI (dono, 2026-10-13) -- a tabela e do `Core.Combat.SonsDeKi`
    // =====================================================================
    /// <summary>`fire_kiblast.wav` -- a bola de ki (`blasts.dm:58,108`).</summary>
    public const string KiBola = K + "fire_kiblast.ogg";
    /// <summary>`KIBLAST.WAV` -- as rajadas (`blasts.dm:164,227,290,360,434,497`).</summary>
    public const string KiRajada = K + "KIBLAST.ogg";
    /// <summary>`burning_fire.wav` -- a teleguiada, a Buster Shell, a Scattering Bullet (`blasts.dm:561`, `GuidedBall.dm:77`).</summary>
    public const string KiQueima = K + "burning_fire.ogg";
    /// <summary>`disc_fire.wav` -- o Kienzan e o Kill Driver (`discs.dm:52`, `KillDriver.dm:44`).</summary>
    public const string KiDisco = K + "disc_fire.ogg";
    /// <summary>`kikoho.wav` (`Kikoho.dm:31`).</summary>
    public const string KiKikoho = K + "kikoho.wav";
    /// <summary>`Blast.wav` -- a Death Ball partindo (`DeathBall.dm:87`).</summary>
    public const string KiEsferaDaMorte = K + "Blast.wav";
    /// <summary>`spiritbombfire.wav` -- a Genkidama partindo (`SpiritBomb.dm:173`).</summary>
    public const string KiGenkidama = K + "spiritbombfire.wav";
    /// <summary>(v0.3.1) `spiritbomb.wav` -- a Genkidama se erguendo (`SpiritBomb.dm:41`): o gesto `ErgueAGenkidama`.</summary>
    public const string KiGenkidamaErguida = K + "spiritbomb.wav";
    /// <summary>(v0.3.2) `deathball_charge` -- a Death Ball se erguendo (`DeathBall.dm:39`): o gesto `ErgueADeathBall`.</summary>
    public const string KiDeathBallErguida = K + "deathball_charge.ogg";
    /// <summary>(v0.3.2) `chargeincrease.ogg` -- a Death Ball crescendo um estagio (`DeathBall.dm:83`): o gesto `CresceADeathBall`.</summary>
    public const string KiDeathBallCresce = K + "chargeincrease.ogg";
    /// <summary>`kamehameha_fire.wav` -- todo raio ao sair, e ele ANDA com a cabeca (`beams.dm:283` e as irmas).</summary>
    public const string KiRaio = K + "kamehameha_fire.ogg";
    /// <summary>`basicbeam_fire.wav` -- o Final Flash (`FinalFlash.dm:38`).</summary>
    public const string KiRaioBasico = K + "basicbeam_fire.ogg";

    /// <summary>
    /// `kame_charge.wav` -- o raio sendo CARREGADO (`beams.dm:290` e as irmas). O Final Flash usa outro arquivo no DM
    /// (`basicbeam_chargeoriginal.wav`, `FinalFlash.dm:41`); aqui a carga soa igual pros dez raios, porque o snapshot
    /// nao diz qual tecnica esta na mao -- so que ha uma.
    /// </summary>
    public const string CargaDeRaio = K + "kame_charge.ogg";

    /// <summary>
    /// O ESTALO DO ESTOURO DE UMA BOLA: `scouterexplode.ogg`, o que a explosao do DM toca em quem ela pega
    /// (`explosions.dm:158`). E o mesmo arquivo do <see cref="Kiai"/>.
    /// </summary>
    public const string EstaloDeKi = T + "scouterexplode.ogg";

    /// <summary>O ESTRONDO DA EXPLOSAO GRANDE (o raio, a esfera grande): `kiplosion.wav` (`Ki Attacks.dm:20`).</summary>
    public const string ExplosaoDeKi = K + "kiplosion.ogg";

    /// <summary>A TECLA X: `descend.wav` (`Power Control.dm:68`) -- o Ki que excedia, ou um estagio da forma, descendo.</summary>
    public const string Descer = E + "descend.ogg";

    /// <summary>O arquivo de cada som de ki. Vazio = calado.</summary>
    public static string DoKi(Jandirus.Core.Combat.SomDeKi som) => som switch
    {
        Jandirus.Core.Combat.SomDeKi.Bola => KiBola,
        Jandirus.Core.Combat.SomDeKi.Rajada => KiRajada,
        Jandirus.Core.Combat.SomDeKi.Queima => KiQueima,
        Jandirus.Core.Combat.SomDeKi.Disco => KiDisco,
        Jandirus.Core.Combat.SomDeKi.Kikoho => KiKikoho,
        Jandirus.Core.Combat.SomDeKi.EsferaDaMorte => KiEsferaDaMorte,
        Jandirus.Core.Combat.SomDeKi.Genkidama => KiGenkidama,
        Jandirus.Core.Combat.SomDeKi.Raio => KiRaio,
        Jandirus.Core.Combat.SomDeKi.RaioBasico => KiRaioBasico,
        // (v0.2.1) as armas de fogo -- o `shotsound` de cada uma (`Tech/Guns.dm`).
        Jandirus.Core.Combat.SomDeKi.Pistola => T + "handgunheavy.ogg",
        Jandirus.Core.Combat.SomDeKi.Rajada7mm => T + "riflelight.ogg",
        Jandirus.Core.Combat.SomDeKi.Escopeta => T + "shotgun.ogg",
        Jandirus.Core.Combat.SomDeKi.Foguete => T + "RPGshot.ogg",
        Jandirus.Core.Combat.SomDeKi.Blaster => T + "rifleheavy.ogg",
        _ => "",
    };

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
            // UMA CONTA SO: o que sai e o que o `Espiar` diria agora. Duas contas pra mesma resposta sao duas
            // contas pra discordarem -- e aqui a discordancia e uma faixa carregada a toa e outra lida do disco
            // no meio do golpe (ver `AudioDirector.Adiantar`).
            string proxima = Espiar();
            if (_itens.Count > 1) { _ultima = proxima; _i++; }
            return proxima;
        }

        /// <summary>
        /// A QUE O PROXIMO <see cref="Proxima"/> VAI ENTREGAR, sem tira-la do saco.
        ///
        /// Quem espia no fim de uma rodada faz o embaralhamento da rodada seguinte acontecer AGORA, em vez de
        /// na entrega. E a mesma chamada, com a mesma `_ultima`, so mais cedo: a regra "a rodada nova nao
        /// comeca com a faixa em que a velha terminou" e decidida la dentro e nao muda de lugar. O que nao
        /// da e responder sem embaralhar -- seria um palpite, e a entrega seguinte sortearia outra coisa.
        /// </summary>
        public string Espiar()
        {
            if (_pasta != null && _itens.Count == 0) Varrer();
            if (_itens.Count == 0) return "";
            if (_itens.Count == 1) return _itens[0];

            if (_i >= _itens.Count) Embaralhar();
            return _itens[_i];
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

        /// <summary>Le a pasta -- ver <see cref="ArquivosDeAudio"/>.</summary>
        private void Varrer()
        {
            if (DirAccess.GetFilesAt(_pasta!).Length == 0)
            {
                GD.PushWarning($"[audio] pasta vazia ou ausente: {_pasta}");
                return;
            }

            _itens = ArquivosDeAudio(_pasta!);
            Embaralhar();
            GD.Print($"[audio] {_pasta}: {_itens.Count} faixas na rotacao");
        }
    }
}
