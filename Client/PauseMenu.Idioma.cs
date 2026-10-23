using Godot;

namespace Jandirus.Client;

/// <summary>
/// A SECAO "IDIOMA / LANGUAGE" DAS OPCOES (2026-10-22) -- a porta do ingles. Ver `Idioma.cs`.
///
/// ELA E A PRIMEIRA DA TELA E NAO SE TRADUZ, e as duas coisas sao pelo mesmo motivo: quem precisa dela e justamente
/// quem nao le a lingua que esta na tela. O titulo diz as duas palavras ("Idioma / Language") e cada opcao se escreve
/// na propria lingua ("Português", "English"), como em todo jogo -- traduzida, a opcao do portugues viraria
/// "Portuguese" pra quem procura a palavra que conhece.
///
/// VALE NA HORA E FICA GRAVADA (`Settings.Lingua`), no lobby e no mundo: o Godot refaz o texto de toda tela aberta.
/// </summary>
public partial class PauseMenu
{
    private OptionButton _lingua = null!;

    private void MontarOIdioma(VBoxContainer caixa)
    {
        Label titulo = Secao("Idioma / Language");
        titulo.AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled;
        caixa.AddChild(titulo);

        _lingua = new OptionButton { AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled };
        _lingua.AddItem("Português", 0);
        _lingua.AddItem("English", 1);
        _lingua.Selected = _cfg.Lingua == Idioma.Ingles ? 1 : 0;
        _lingua.ItemSelected += i =>
        {
            _cfg.Lingua = i == 1 ? Idioma.Ingles : Idioma.Portugues;
            // SO GRAVA: o `Aplicar` do config mexe na janela, e a lingua nao e janela. Quem a poe na tela e o `Idioma`.
            _cfg.Gravar();
            Idioma.Aplicar(_cfg.Lingua);
        };
        caixa.AddChild(_lingua);
    }

    /// <summary>O seletor de lingua desta tela. SO PRA BANCADA (`--diaglingua`), que o aperta como o mouse apertaria.</summary>
    public OptionButton SeletorDeLinguaDeTeste => _lingua;
}
