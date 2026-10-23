using Godot;

namespace Jandirus.Client;

/// <summary>
/// A PORTA DO GODOT PRO TRADUTOR DO JOGO. O `TranslationServer` pergunta a esta `Translation` o texto de todo controle
/// antes de desenha-lo (`auto_translate_mode`, que de fabrica vale pra arvore inteira), e ela responde com o
/// <see cref="Jandirus.Core.Lingua.Tradutor"/> -- a frase inteira, o molde com pedaco, a frase composta.
///
/// POR QUE UMA CLASSE E NAO O `AddMessage` DO GODOT: o `AddMessage` so conhece a frase inteira, e metade do que o jogo
/// escreve tem numero ou nome no meio ("BP: 1.200", "Kuririn caiu."). So uma `Translation` que CALCULA a resposta
/// alcanca o molde. Ela so existe registrada com o jogo em ingles (ver `Idioma.Aplicar`).
/// </summary>
public partial class TraducaoDoJogo : Translation
{
	public override StringName _GetMessage(StringName srcMessage, StringName context)
	{
		string pt = srcMessage.ToString();
		return Idioma.Tradutor.Traduzir(pt);
	}
}
