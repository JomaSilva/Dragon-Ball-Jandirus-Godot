using System.Text.Json;
using Godot;

namespace Jandirus.Client;

/// <summary>
/// As preferencias DESTA MAQUINA: janela, zoom, volume e TECLAS.
///
/// Ficam em `user://config.json`, junto dos perfis -- nada disso e do servidor, e nada disso
/// afeta o jogo dos outros. Zoom e o unico que chega perto: ver mais longe e vantagem, entao
/// ele tem TETO (ver <see cref="ZoomMax"/>).
///
/// ============================ POR QUE A TECLA MORA AQUI E NAO NO PERFIL ============================
/// Porque o dedo e da PESSOA e nao do personagem. `Profiles.cs` guarda conta por servidor: uma
/// ligacao gravada la mudaria ao trocar de servidor, e sumiria ao apagar o personagem. Quem liga o
/// Kamehameha ao Q quer o Q ligado no Saiyajin, no Namekuseijin e no servidor do amigo.
///
/// A consequencia esta escrita: uma tecla ligada a uma acao que ESTE personagem nao tem continua
/// gravada, apagada na tela, e nao manda pacote nenhum -- ver `Atalhos.Disparar`. Apagar a ligacao
/// faria quem volta pro Namekuseijin perder o Regenerar pra sempre.
/// ==============================================================================================
/// </summary>
public sealed class Settings
{
    private const string Arquivo = "user://config.json";

    /// <summary>Uma acao do jogo que o jogador tirou da tecla de fabrica. Ver `Teclas`.</summary>
    public sealed class LigacaoDeTecla
    {
        public string Acao = "";
        /// <summary>O nome do `Godot.Key` (`"J"`). Vazio = a acao ficou SEM tecla.</summary>
        public string Tecla = "";
    }

    /// <summary>Uma tecla que o jogador ligou a um verbo ou a uma transformacao. Ver `Teclas.Atalho`.</summary>
    public sealed class AtalhoGravado
    {
        public string Tecla = "";
        public string Tipo = "";
        public string Chave = "";
        public string Rotulo = "";
    }

    // ============================ SAO LISTAS DE OBJETO, E NAO UM DICIONARIO ============================
    // `Dictionary<Key,string>` teria sido menos codigo e um erro: o `System.Text.Json` serializa
    // chave de enum como NUMERO, e o arquivo passaria a depender da ordem interna do enum do Godot
    // -- uma atualizacao do motor que insira uma tecla no meio remapearia as ligacoes de todo mundo,
    // caladamente. Este projeto ja perdeu dado gravado por causa da FORMA do JSON (as cores de
    // roupa); aqui o campo e o nome da tecla em texto, que so muda se o Godot renomear a tecla.
    // ==============================================================================================
    public List<LigacaoDeTecla> LigacoesDeTecla = [];
    public List<AtalhoGravado> AtalhosDeTecla = [];

    // --- video ---
    public int LarguraJanela = 1280;
    public int AlturaJanela = 720;
    public bool TelaCheia;

    /// <summary>
    /// Ampliacao da camera. INTEIRO de proposito: em arte de pixel um zoom quebrado mapeia
    /// texel em pixel de tela de forma irregular e a imagem cintila andando.
    /// </summary>
    public int Zoom = 3;

    /// <summary>
    /// QUALIDADE GRAFICA: 0 baixo, 1 medio, 2 alto. Hoje ela decide UMA coisa -- o filtro da
    /// sombra das luzes --, e por isso o nome e generico de proposito: e o lugar onde o proximo
    /// ajuste de custo visual entra sem virar mais uma opcao solta na tela.
    ///
    ///   baixo  sem filtro  -- a sombra sai com a borda dura, e e a mais barata
    ///   medio  PCF5        -- cinco amostras: suaviza sem pesar
    ///   alto   PCF13       -- treze amostras, a borda mais macia que o Godot 2D oferece
    ///
    /// PADRAO ALTO: o custo de PCF13 e por LUZ COM SOMBRA na tela, e o cenario tem poucas
    /// (fogueira, tocha, lava). Quem precisar baixar tem a opcao; comecar no feio pra economizar
    /// o que nao esta caro seria escolher pelo jogador.
    /// </summary>
    public int Grafico = 2;

    public const int GraficoBaixo = 0, GraficoMedio = 1, GraficoAlto = 2;

    /// <summary>O filtro que a qualidade atual pede. E o unico lugar que traduz o numero.</summary>
    public Godot.Light2D.ShadowFilterEnum FiltroDeSombra => Grafico switch
    {
        GraficoBaixo => Godot.Light2D.ShadowFilterEnum.None,
        GraficoMedio => Godot.Light2D.ShadowFilterEnum.Pcf5,
        _ => Godot.Light2D.ShadowFilterEnum.Pcf13,
    };

    /// <summary>
    /// Teto do zoom-out. Diminuir o zoom mostra MAIS mundo, e enxergar mais longe que os
    /// outros e vantagem de verdade num jogo de luta -- por isso o minimo nao e livre.
    /// </summary>
    public const int ZoomMin = 2;
    public const int ZoomMax = 6;

    // --- audio (0 a 1) ---
    public float VolumeGeral = 0.8f;
    public float VolumeMusica = 0.6f;
    public float VolumeEfeitos = 0.9f;
    public float VolumeAmbiente = 0.5f;

    /// <summary>
    /// A voz das outras pessoas. Comeca ALTA (0,9): quem ligou a voz ligou pra ouvir, e uma voz mais
    /// baixa que o vento so faria a pessoa achar que o sistema nao funciona.
    /// </summary>
    public float VolumeVoz = 0.9f;

    // --- voz ---
    /// <summary>
    /// O MICROFONE ESTA LIGADO?
    ///
    /// ============================ **FALSO POR PADRAO**, E ISSO NAO E CONSERVADORISMO ============================
    /// Microfone e a unica coisa deste jogo que capta o quarto de uma pessoa. Um sistema de voz que
    /// nasce ligado transforma "instalei um jogo" em "abri o microfone", e ninguem tomou essa decisao.
    ///
    /// Enquanto isto for falso o `Microfone` **nao cria o `AudioStreamPlayer` do microfone**: nao ha
    /// captura, nao ha codificador, nao ha pacote. Nao e um `if` no envio -- e o aparelho nao existir.
    ///
    /// OUVIR NAO DEPENDE DISTO. Quem nao quer falar continua ouvindo os outros, que e o que a maioria
    /// vai querer; pra nao ouvir tambem existe o <see cref="VolumeVoz"/> em zero.
    /// ==========================================================================================================
    /// </summary>
    public bool VozLigada;

    /// <summary>
    /// APERTA-PRA-FALAR (verdadeiro) ou MICROFONE ABERTO (falso).
    ///
    /// **O padrao e aperta-pra-falar** -- foi o que o dono pediu ao dizer *"ao apertar V"*, e e o unico
    /// modo em que a pessoa sabe, sem olhar pra tela, se o quarto dela esta indo pra rede: o dedo esta
    /// na tecla ou nao esta.
    ///
    /// O modo aberto existe pra quem quer -- e ele nao e microfone LIVRE: ele ainda depende de
    /// <see cref="VozLigada"/>, e ainda so manda quadro acima do <see cref="LimiarDeVoz"/>.
    /// </summary>
    public bool VozApertarParaFalar = true;

    /// <summary>
    /// QUAO ALTO PRECISA SER PRA CONTAR COMO VOZ (amplitude de pico, 0 a 1).
    ///
    /// Vale nos DOIS modos, e no de apertar tambem: quadro de silencio codificado e mandado e banda
    /// gasta pra transmitir o ar de um quarto vazio. 0,02 e baixo o bastante pra passar uma fala
    /// sussurrada e alto o bastante pra segurar o chiado de fundo de um microfone barato.
    /// </summary>
    public float LimiarDeVoz = 0.02f;

    /// <summary>
    /// O microfone escolhido, pelo nome que o `AudioServer.GetInputDeviceList()` da. Vazio = o padrao
    /// do sistema. Nome e nao indice: a lista muda de ordem quando alguem pluga um fone.
    /// </summary>
    public string DispositivoDeVoz = "";

    private static readonly JsonSerializerOptions Opcoes = new() { IncludeFields = true, WriteIndented = true };

    public static Settings Carregar()
    {
        if (!Godot.FileAccess.FileExists(Arquivo)) return new Settings();
        try
        {
            return JsonSerializer.Deserialize<Settings>(
                Godot.FileAccess.GetFileAsString(Arquivo), Opcoes) ?? new Settings();
        }
        catch (Exception e)
        {
            GD.PushWarning($"[config] ilegivel, usando o padrao: {e.Message}");
            return new Settings();
        }
    }

    public void Gravar()
    {
        using Godot.FileAccess? f = Godot.FileAccess.Open(Arquivo, Godot.FileAccess.ModeFlags.Write);
        if (f == null) { GD.PushWarning("[config] nao consegui gravar"); return; }
        f.StoreString(JsonSerializer.Serialize(this, Opcoes));
    }

    /// <summary>Poe a janela e o audio no estado gravado. Chamado no boot e a cada mudanca.</summary>
    public void Aplicar()
    {
        Zoom = Math.Clamp(Zoom, ZoomMin, ZoomMax);

        DisplayServer.WindowSetMode(TelaCheia
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);

        if (!TelaCheia)
        {
            DisplayServer.WindowSetSize(new Vector2I(LarguraJanela, AlturaJanela));
            // recentra: mudar de resolucao com a janela no canto joga metade dela pra fora
            Vector2I tela = DisplayServer.ScreenGetSize();
            var tam = new Vector2I(LarguraJanela, AlturaJanela);
            DisplayServer.WindowSetPosition((tela - tam) / 2);
        }

        AudioDirector.Instance?.AplicarVolumes(this);
    }

    /// <summary>Resolucoes oferecidas. So 16:9 -- o jogo desenha em pixel e nao estica.</summary>
    public static readonly (int L, int A)[] Resolucoes =
    [
        (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440),
    ];
}
