using Godot;

namespace Jandirus.Client;

/// <summary>
/// O SOM DO JOGO: musica, ambiente e efeitos.
///
/// TRES BARRAMENTOS, criados em codigo (nao num .tres de layout, que e um arquivo binario
/// chato de versionar): Musica, Efeitos e Ambiente, todos filhos do Master. E o que permite
/// o menu de pause ter um controle de volume separado por tipo -- baixar a musica sem perder
/// o som dos golpes.
///
/// A MUSICA TEM PRIORIDADE, nao fila. Uma transformacao interrompe o tema de combate, que
/// interrompe o tema do lugar; quando a de cima acaba, a de baixo VOLTA sozinha. E o mesmo
/// desenho do BYOND, onde a musica de batalha abaixava pro tema de transformacao.
///
/// Autoload: a musica nao pode parar quando a cena troca (login -> selecao -> mundo).
/// </summary>
public partial class AudioDirector : Node
{
    public static AudioDirector? Instance { get; private set; }

    public const string BusMusica = "Musica";
    public const string BusEfeitos = "Efeitos";
    public const string BusAmbiente = "Ambiente";

    /// <summary>
    /// Quem manda quando duas musicas querem tocar. Maior vence.
    ///
    /// ============================ A `Raiva` NASCEU ENTRE `Combate` E `Transformacao`, E O LUGAR E O DM ============================
    /// O original tem tres canais de musica e a ordem entre eles esta escrita: `emit_TransformMusic`
    /// abre cortando o canal de raiva (*"a transformation always wins: cut any rage theme first"*,
    /// `BattleMusic.dm:133`) e `emit_RageMusic` desiste se um tema de forma estiver no ar
    /// (`:148`, `if(world.time &lt; M.transform_music_until) continue`); os dois chamam
    /// `duck_battle_music`, que cala o combate enquanto tocam.
    ///
    /// Essas TRES regras sao exatamente o que a comparacao numerica deste enum ja faz -- camada maior
    /// interrompe, camada menor so fica de sobreaviso (ver <see cref="Musica"/>). Nao houve canal novo,
    /// nem `if` novo: houve um valor no meio da fila.
    ///
    /// E POR ISSO O `Transformacao` MUDOU DE NUMERO. Ele e ordinal puro -- nao viaja na rede, nao vai
    /// pro save e nao e escrito em lugar nenhum como literal --, entao renumerar e de graca. Enfiar a
    /// raiva como `4` (acima da transformacao) custaria nada em compilacao e inverteria a unica regra
    /// que o DM escreveu duas vezes.
    /// ================================================================================================================
    /// </summary>
    public enum Camada { Lugar = 0, Menu = 1, Combate = 2, Raiva = 3, Transformacao = 4 }

    private AudioStreamPlayer _musicaA = null!, _musicaB = null!;   // dois: a troca e por fade
    private AudioStreamPlayer _ambiente = null!;
    private bool _usandoA = true;

    private Camada _camadaAtual = Camada.Lugar;
    private string _faixaAtual = "";
    private string _faixaDeBaixo = "";      // a que volta quando a de cima termina

    private double _fade;                    // 0..1 do cruzamento em andamento
    private const double DuracaoFade = 1.2;

    /// <summary>
    /// QUE CAMADA MANDA AGORA, e QUE FAIXA esta no ar. So a bancada le -- ver `--diagforma`.
    ///
    /// ============================ POR QUE ELAS PRECISAM SER LIDAS DE FORA ============================
    /// A regra "a musica da transformacao alcanca o planeta inteiro" nao tem NADA na tela: ela e a
    /// diferenca entre um `if (_souEu)` existir ou nao existir no `Transformacao._Ready`. Quem roda o
    /// jogo sozinho -- que e como este port e testado -- ouve a faixa dos dois jeitos, porque ele e
    /// sempre o dono da cena. O defeito so aparece com duas pessoas no mesmo planeta, e nessa hora
    /// ninguem esta com o depurador aberto.
    ///
    /// E o comentario do `Rodar` AFIRMAVA por anos que a musica era so de quem virava, com o codigo
    /// fazendo o contrario. Um campo lido pela bancada e o que impede a proxima afirmacao dessas de
    /// sobreviver sem ninguem conferir.
    /// ============================================================================================
    /// </summary>
    public Camada CamadaDeTeste => _camadaAtual;

    /// <inheritdoc cref="CamadaDeTeste"/>
    public string FaixaDeTeste => _faixaAtual;

    public override void _Ready()
    {
        Instance = this;
        CriarBarramentos();

        _musicaA = NovoPlayer(BusMusica);
        _musicaB = NovoPlayer(BusMusica);
        _ambiente = NovoPlayer(BusAmbiente);

        _musicaA.Finished += AoTerminarMusica;
        _musicaB.Finished += AoTerminarMusica;
    }

    public override void _ExitTree() => Instance = null;

    private AudioStreamPlayer NovoPlayer(string bus)
    {
        var p = new AudioStreamPlayer { Bus = bus, VolumeDb = 0 };
        AddChild(p);
        return p;
    }

    /// <summary>
    /// Cria os barramentos se ainda nao existirem. Idempotente -- o autoload pode ser
    /// recarregado numa troca de cena e nao pode duplicar barramento.
    /// </summary>
    private static void CriarBarramentos()
    {
        foreach (string nome in new[] { BusMusica, BusEfeitos, BusAmbiente })
        {
            if (AudioServer.GetBusIndex(nome) >= 0) continue;
            int i = AudioServer.BusCount;
            AudioServer.AddBus(i);
            AudioServer.SetBusName(i, nome);
            AudioServer.SetBusSend(i, "Master");
        }
    }

    // =====================================================================
    // VOLUME
    // =====================================================================
    public void AplicarVolumes(Settings s)
    {
        Volume("Master", s.VolumeGeral);
        Volume(BusMusica, s.VolumeMusica);
        Volume(BusEfeitos, s.VolumeEfeitos);
        Volume(BusAmbiente, s.VolumeAmbiente);
    }

    /// <summary>
    /// Volume de 0 a 1 -> decibeis. O ouvido e logaritmico: sem a conversao, metade do
    /// controle deslizante ja soaria quase no maximo. Zero vira MUDO de verdade, nao -80 dB.
    /// </summary>
    private static void Volume(string bus, float v)
    {
        int i = AudioServer.GetBusIndex(bus);
        if (i < 0) return;
        AudioServer.SetBusMute(i, v <= 0.001f);
        AudioServer.SetBusVolumeDb(i, Mathf.LinearToDb(Mathf.Clamp(v, 0.0001f, 1f)));
    }

    // =====================================================================
    // MUSICA
    // =====================================================================
    /// <summary>
    /// Poe uma faixa no ar. Camada mais alta interrompe a mais baixa; camada mais baixa
    /// enquanto uma alta toca so fica GUARDADA e entra quando a de cima sair.
    /// </summary>
    public void Musica(string caminho, Camada camada, bool repetir = true)
    {
        if (caminho.Length == 0) return;

        if (camada < _camadaAtual)
        {
            _faixaDeBaixo = caminho;   // fica de sobreaviso
            return;
        }

        if (caminho == _faixaAtual && camada == _camadaAtual) return;

        // guarda o que estava tocando pra voltar depois
        if (camada > _camadaAtual && _faixaAtual.Length > 0) _faixaDeBaixo = _faixaAtual;

        _camadaAtual = camada;
        _faixaAtual = caminho;
        Cruzar(caminho, repetir);
    }

    /// <summary>Encerra uma camada. Se era a que tocava, a de baixo volta.</summary>
    public void PararCamada(Camada camada)
    {
        if (camada != _camadaAtual) return;

        _camadaAtual = Camada.Lugar;
        if (_faixaDeBaixo.Length > 0)
        {
            _faixaAtual = _faixaDeBaixo;
            _faixaDeBaixo = "";
            Cruzar(_faixaAtual, true);
        }
        else
        {
            _faixaAtual = "";
            Atual().Stop();
        }
    }

    private void AoTerminarMusica()
    {
        // faixa nao-repetida acabou: devolve o comando pra camada de baixo
        if (_camadaAtual > Camada.Lugar) PararCamada(_camadaAtual);
    }

    private AudioStreamPlayer Atual() => _usandoA ? _musicaA : _musicaB;
    private AudioStreamPlayer Outro() => _usandoA ? _musicaB : _musicaA;

    /// <summary>
    /// AS FAIXAS JA CARREGADAS.
    ///
    /// ============================ MP3 NAO SE CARREGA DUAS VEZES ============================
    /// O importador do Godot le o arquivo INTEIRO pra memoria, de forma sincrona, na thread
    /// principal. As faixas de batalha tem 0,9 a 1,9 MB e a maior do menu tem 14,2 MB -- e o
    /// carregamento acontecia no PRIMEIRO SOCO de cada briga e a cada abertura do menu de pause.
    /// Um engasgo de quadro inteiro, no pior instante, toda vez.
    ///
    /// O cache troca isso por um custo unico. Ele so cresce com o que a sessao de fato tocou.
    /// =====================================================================================
    /// </summary>
    private readonly Dictionary<string, AudioStream> _faixas = [];

    private void Cruzar(string caminho, bool repetir)
    {
        if (!_faixas.TryGetValue(caminho, out AudioStream? fluxo))
        {
            fluxo = ResourceLoader.Load<AudioStream>(caminho);
            if (fluxo != null) _faixas[caminho] = fluxo;
        }
        if (fluxo == null) { GD.PushWarning($"[audio] faixa ausente: {caminho}"); return; }

        // o .import do Godot ja resolve o loop de ogg/mp3; wav depende do arquivo
        if (fluxo is AudioStreamOggVorbis ogg) ogg.Loop = repetir;
        else if (fluxo is AudioStreamMP3 mp3) mp3.Loop = repetir;

        AudioStreamPlayer novo = Outro();
        novo.Stream = fluxo;
        novo.VolumeDb = -60;
        novo.Play();

        _usandoA = !_usandoA;
        _fade = 0;
    }

    public override void _Process(double delta)
    {
        if (_fade >= 1) return;

        _fade = Math.Min(_fade + delta / DuracaoFade, 1);
        float t = (float)_fade;

        Atual().VolumeDb = Mathf.LinearToDb(Mathf.Max(t, 0.0001f));
        Outro().VolumeDb = Mathf.LinearToDb(Mathf.Max(1 - t, 0.0001f));
        if (_fade >= 1) Outro().Stop();
    }

    // =====================================================================
    // AMBIENTE
    // =====================================================================
    /// <summary>O fundo do lugar (vento, mar, cidade). Troca quando se muda de planeta.</summary>
    public void Ambiente(string? caminho)
    {
        if (string.IsNullOrEmpty(caminho)) { _ambiente.Stop(); return; }

        var fluxo = ResourceLoader.Load<AudioStream>(caminho);
        if (fluxo == null) { GD.PushWarning($"[audio] ambiente ausente: {caminho}"); return; }
        if (_ambiente.Stream == fluxo && _ambiente.Playing) return;

        if (fluxo is AudioStreamOggVorbis ogg) ogg.Loop = true;
        else if (fluxo is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;

        _ambiente.Stream = fluxo;
        _ambiente.Play();
    }

    // =====================================================================
    // EFEITOS
    // =====================================================================
    /// <summary>
    /// Um som pontual sem posicao (interface, aviso). Cria e descarta o player: som de UI e
    /// raro e nao vale um pool.
    /// </summary>
    public void Efeito(string caminho, float volume = 1f)
    {
        var fluxo = ResourceLoader.Load<AudioStream>(caminho);
        if (fluxo == null) return;

        var p = new AudioStreamPlayer
        {
            Stream = fluxo,
            Bus = BusEfeitos,
            VolumeDb = Mathf.LinearToDb(Mathf.Clamp(volume, 0.0001f, 1f)),
        };
        AddChild(p);
        p.Finished += p.QueueFree;
        p.Play();
    }

    /// <summary>
    /// Som COM posicao no mundo -- so quem esta perto ouve, e o volume cai com a distancia.
    /// E o que faz um soco do outro lado do mapa nao estourar no ouvido de todo mundo.
    /// </summary>
    public static void EfeitoNoLugar(Node2D onde, string caminho, float volume = 1f, float alcance = 480f)
    {
        if (string.IsNullOrEmpty(caminho)) return;
        var fluxo = ResourceLoader.Load<AudioStream>(caminho);
        if (fluxo == null) { GD.PushWarning($"[audio] efeito ausente: {caminho}"); return; }

        var p = new AudioStreamPlayer2D
        {
            Stream = fluxo,
            Bus = BusEfeitos,
            MaxDistance = alcance,
            Attenuation = 1.5f,
            VolumeDb = Mathf.LinearToDb(Mathf.Clamp(volume, 0.0001f, 1f)),
        };
        onde.AddChild(p);
        p.Finished += p.QueueFree;
        p.Play();
    }
}
