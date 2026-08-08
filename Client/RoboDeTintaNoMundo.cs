using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A MESMA MEDIDA, MAS NO JOGO DE VERDADE ============================
/// A <see cref="RoboDeTinta"/> mede o cabelo e o rabo numa cena de laboratorio -- duas folhas, um
/// shader, um `CanvasModulate` posto a mao. Isto aqui mede o boneco que o jogador ve, com a zona
/// carregada, a camera no lugar e o CEU do planeta mandando na luz.
///
/// PRECISA EXISTIR PORQUE A CENA DE LABORATORIO NAO PROVA A ARVORE. A pergunta que sobra e "o
/// ambiente do jogo REALMENTE cai em cima do personagem?", e ela e sobre topologia: quem esta em
/// que canvas, quem e filho de quem. Nenhuma medida em cena montada a mao responde isso.
///
/// COMO ELA RESPONDE: tres fotos da mesma tela.
///
///   1. sem ambiente, COM o boneco
///   2. sem ambiente, SEM o boneco  -> a diferenca entre 1 e 2 e a MASCARA do personagem
///   3. com ambiente, COM o boneco
///
/// Ai a razao de brilho 3/1 e medida DUAS VEZES: dentro da mascara (o boneco) e fora dela (o chao).
/// O chao e a TESTEMUNHA -- ele obedece o ambiente por definicao --, e a razao dele e a escala em que
/// a do boneco se le. Sao tres leituras possiveis, e o <see cref="Veredito"/> as escreve por extenso:
///
///   boneco ~ chao     UMA aplicacao. E o certo, e e o que se mede hoje.
///   boneco ~ chao x chao   DUAS -- alguem pos o ambiente no caminho do shader tambem, e a folha sai
///                          ao quadrado (o defeito que deixaria o cabelo preto e o rabo chapado).
///   boneco ~ 1,000    o personagem ESCAPA da luz: brilha igual de dia e de noite.
///
/// ============================ E O BONECO SEMPRE OBEDECEU ============================
/// Aqui esteve escrito que o personagem *nao* obedecia o ambiente porque o shader dele terminava em
/// `COLOR = c`. E FALSO, e foi medido: o `CanvasModulate` e aplicado pelo Godot DEPOIS do `fragment`
/// (troque o fim do `Personagem.gdshader` por uma cor CONSTANTE e ela ainda sai multiplicada pelo
/// ceu). Nenhuma versao daquela linha jamais tirou o boneco da luz do dia -- entao "o cabelo
/// escureceu" nao se explica por ela, e esta bancada existe pra que a proxima suspeita comece pelo
/// numero e nao pela historia.
/// ================================================================================
///
/// NAO DA PRA TESTAR TROCANDO A COR do `CanvasModulate`: a <see cref="Iluminacao"/> reescreve essa
/// cor A CADA QUADRO pelo ceu do planeta, entao o branco posto aqui nunca chega a ser desenhado --
/// a primeira versao desta bancada mediu razao 1,000 exatamente por isso e quase derrubou a
/// suspeita certa. Quem se apaga e o NODE (`Visible`), que ninguem reescreve.
///
///     &lt;godot&gt; --path . --host --rede 7919 --nome Zx --conta ... --raca Saiyan --diagtintamundo
/// ================================================================================================
/// </summary>
public partial class RoboDeTintaNoMundo : Node
{
    private readonly List<string> _linhas = [];
    private double _t;
    private int _passo;
    private bool _acabou;
    private CanvasModulate? _ambiente;
    private Color _corDoCeu;
    private Image? _semAmbiente, _semBoneco;

    /// <summary>
    /// O QUADRADO EM VOLTA DO BONECO, em pixels de tela. A camera segue o personagem, entao o centro
    /// da tela E o personagem -- e um sprite de 32 px com a camera do jogo cabe folgado em 120.
    /// </summary>
    private const int Meio = 120;

    public override void _Process(double delta)
    {
        if (_acabou) return;
        if (GameClient.Instance is not { Connected: true } || World.Instancia is not { } mundo) return;
        if (mundo.VisualLocalDeTeste is not { } vis) return;

        _t += delta;
        if (_t < 0.7) return;
        _t = 0;

        switch (_passo++)
        {
            case 0:
                _ambiente = mundo.GetNodeOrNull<Iluminacao>("Iluminacao")?.GetNodeOrNull<CanvasModulate>("Ambiente");
                if (_ambiente == null) { _linhas.Add("NAO ACHEI o CanvasModulate 'Ambiente' -- a bancada nao vale"); _passo = 99; return; }
                _corDoCeu = _ambiente.Color;
                _linhas.Add($"ambiente do ceu AGORA: {_corDoCeu.ToHtml(false)}  (r{_corDoCeu.R:0.000} g{_corDoCeu.G:0.000} b{_corDoCeu.B:0.000})");
                _linhas.Add($"cabelo do boneco: {(vis.TintaDoCabeloDeTeste is { } tc ? $"{tc.Tinta} / {(tc.Modo == 1 ? "MATIZ" : "SOMA")}" : "sem camada")}");
                _linhas.Add($"rabo do boneco:   {(vis.TintaDoRaboDeTeste is { } tr ? tr.ToString() : "sem camada")}");
                _linhas.Add($"contorno de forma armado: {vis.AuraDaFormaDeTeste}");
                _ambiente.Visible = false;
                break;

            case 1:
                _semAmbiente = Foto();
                vis.Visible = false;
                break;

            case 2:
                _semBoneco = Foto();
                vis.Visible = true;
                _ambiente!.Visible = true;
                break;

            case 3:
            {
                Image? comAmbiente = Foto();
                _linhas.Add($"A. SEM ambiente: {Medir(_semAmbiente)}");
                _linhas.Add($"B. COM ambiente ({_corDoCeu.ToHtml(false)}): {Medir(comAmbiente)}");
                _linhas.Add("   " + Comparar(_semAmbiente, _semBoneco, comAmbiente));
                Salvar(_semAmbiente, "user://tinta-sem-ambiente.png");
                Salvar(comAmbiente, "user://tinta-com-ambiente.png");
                break;
            }

            default:
                _acabou = true;
                GD.Print("\n[tintamundo] ===== A TINTA NO JOGO DE VERDADE =====");
                foreach (string l in _linhas) GD.Print("[tintamundo] " + l);
                GD.Print("[tintamundo] ===== FIM =====");
                GetTree().Quit();
                break;
        }
    }

    private Image? Foto() => GetViewport()?.GetTexture()?.GetImage() is { } i && !i.IsEmpty() ? i : null;

    private void Salvar(Image? img, string destino)
    {
        if (img == null) return;
        string caminho = ProjectSettings.GlobalizePath(destino);
        img.SavePng(caminho);
        _linhas.Add("   foto em " + caminho);
    }

    /// <summary>Quantos tons distintos e que brilho medio no quadrado em volta do boneco.</summary>
    private string Medir(Image? img)
    {
        if (img == null) return "SEM FOTO (headless nao renderiza)";
        var tons = new HashSet<int>();
        double soma = 0;
        int n = 0, claro = 0;
        foreach ((int x, int y) in Caixa(img))
        {
            Color c = img.GetPixel(x, y);
            int r = (int)(c.R * 255 + 0.5f), g = (int)(c.G * 255 + 0.5f), b = (int)(c.B * 255 + 0.5f);
            tons.Add((r << 16) | (g << 8) | b);
            int luz = (r * 299 + g * 587 + b * 114) / 1000;
            soma += luz; n++;
            if (luz > claro) claro = luz;
        }
        return n == 0 ? "caixa vazia" : $"{tons.Count} tom(ns) em {n}px | brilho medio {soma / n:0.0} | mais claro {claro}";
    }

    /// <summary>
    /// O FATOR MEDIDO, pixel a pixel, SEPARADO entre o boneco e o chao.
    ///
    /// O chao e a testemunha: ele sempre obedeceu o ambiente, entao a razao dele e a escala certa
    /// pra ler a do boneco. Boneco em 1,00 e chao em 0,3 quer dizer "o personagem escapa da luz"
    /// (o comportamento antigo); os dois no mesmo numero quer dizer que o personagem passou a ser
    /// multiplicado igual ao cenario.
    /// </summary>
    /// <param name="semBoneco">a foto sem o personagem -- e ela que diz QUAIS pixels sao dele</param>
    private string Comparar(Image? a, Image? semBoneco, Image? b)
    {
        if (a == null || b == null || semBoneco == null) return "sem comparacao (faltou foto)";
        double rBoneco = 0, rChao = 0;
        int nBoneco = 0, nChao = 0;
        foreach ((int x, int y) in Caixa(a))
        {
            Color ca = a.GetPixel(x, y), cb = b.GetPixel(x, y);
            double la = ca.R * 0.299 + ca.G * 0.587 + ca.B * 0.114;
            double lb = cb.R * 0.299 + cb.G * 0.587 + cb.B * 0.114;
            if (la < 0.02) continue;      // pixel quase preto nao mede razao nenhuma
            bool doBoneco = !ca.IsEqualApprox(semBoneco.GetPixel(x, y));
            if (doBoneco) { rBoneco += lb / la; nBoneco++; }
            else { rChao += lb / la; nChao++; }
        }
        if (nBoneco == 0 || nChao == 0)
            return $"razao B/A no BONECO = {(nBoneco > 0 ? (rBoneco / nBoneco).ToString("0.000") : "-")} ({nBoneco}px)"
                 + $"  |  no CHAO = {(nChao > 0 ? (rChao / nChao).ToString("0.000") : "-")} ({nChao}px)";

        double boneco = rBoneco / nBoneco, chao = rChao / nChao;
        return $"razao B/A no BONECO = {boneco:0.000} ({nBoneco}px)  |  no CHAO = {chao:0.000} ({nChao}px)"
             + $"  ->  {Veredito(boneco, chao)}";
    }

    /// <summary>
    /// O QUE AS DUAS RAZOES QUEREM DIZER, escrito por extenso na saida.
    ///
    /// Sem isto a bancada entrega dois numeros e deixa a conclusao com quem estiver lendo -- e foi
    /// assim que "0,187 contra 0,182" (que e a saude) chegou a ser lido como a doenca. As faixas sao
    /// FOLGADAS de proposito: a antialias da borda do sprite e o filtro do chao empurram a media
    /// alguns centesimos, e a diferenca entre UMA e DUAS aplicacoes e grande demais pra caber nisso
    /// (no ambiente de noite: 0,18 contra 0,03).
    /// </summary>
    private static string Veredito(double boneco, double chao)
    {
        if (chao > 0.97) return "ambiente quase neutro agora -- rode de novo em outra hora do dia";
        if (boneco > 0.97) return "o BONECO ESCAPA da luz (brilha igual de dia e de noite)";
        if (Math.Abs(boneco - chao * chao) < Math.Abs(boneco - chao))
            return "o BONECO ESCURECE DUAS VEZES (a folha ao quadrado) -- alguem pos o ambiente no shader";
        return "uma aplicacao so, como o cenario -- que e o certo";
    }

    private IEnumerable<(int, int)> Caixa(Image img)
    {
        int cx = img.GetWidth() / 2, cy = img.GetHeight() / 2;
        for (int y = Math.Max(0, cy - Meio); y < Math.Min(img.GetHeight(), cy + Meio); y++)
            for (int x = Math.Max(0, cx - Meio); x < Math.Min(img.GetWidth(), cx + Meio); x++)
                yield return (x, y);
    }
}
