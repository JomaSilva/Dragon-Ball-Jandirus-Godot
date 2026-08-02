using System.Text.Json;
using Godot;

namespace Jandirus.Client;

/// <summary>
/// As preferencias DESTA MAQUINA: janela, zoom e volume.
///
/// Ficam em `user://config.json`, junto dos perfis -- nada disso e do servidor, e nada disso
/// afeta o jogo dos outros. Zoom e o unico que chega perto: ver mais longe e vantagem, entao
/// ele tem TETO (ver <see cref="ZoomMax"/>).
/// </summary>
public sealed class Settings
{
    private const string Arquivo = "user://config.json";

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
