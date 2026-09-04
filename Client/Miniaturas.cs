using Godot;
using Jandirus.Core.Items;

namespace Jandirus.Client;

/// <summary>
/// O PRIMEIRO QUADRO DE UM SPRITE, VIRADO TEXTURA -- o icone de um item, de uma construcao, de uma
/// tecnica. UM carregador, pra mochila, pra aba Equip, pra aba Tech, pro fantasma de assentar e pro
/// card de skill.
///
/// ============================ ERAM QUATRO COPIAS, E TRES ESTAVAM ERRADAS ============================
/// `TelaDeInventario`, `MenuJogo.Equip`, `MenuJogo.Tech` e `TelaDeConstrucao` tinham cada uma a sua
/// copia. So a da bancada de pesquisa SANEAVA o nome do estado do jeito que o conversor de `.dmi`
/// saneia (minusculas, tudo que nao e letra ou digito vira `_`): as outras tres procuravam
/// `HasAnimation("Radar")` numa folha cuja animacao se chama `radar`, nao achavam, e caiam no
/// PRIMEIRO estado da folha -- o Nav System saia com o icone de outra coisa na aba Equip, e o
/// catalogo da aba Tech mostrava uma bala no lugar do Dragon Radar (o dono viu os dois). A bancada de
/// pesquisa, com a copia certa, desenhava tudo certo -- e foi ela que o dono mostrou como referencia.
///
/// Uma copia so tem um jeito de errar, e quando erra, erra em todo lugar de uma vez -- que e como se
/// descobre.
/// ==================================================================================================
/// </summary>
public static class Miniaturas
{
	private static readonly Dictionary<string, Texture2D?> _cache = new(StringComparer.Ordinal);

	/// <summary>A textura do primeiro quadro de `estado` em `arte`, ou nula sem folha nem estado com quadro.</summary>
	public static Texture2D? De(string arte, string estado)
	{
		string chave = arte + "|" + estado;
		if (_cache.TryGetValue(chave, out Texture2D? pronta)) return pronta;
		Texture2D? tex = Carregar(arte, estado);
		_cache[chave] = tex;
		return tex;
	}

	/// <summary>O icone de um item do catalogo -- a arte e o estado que o `ItemDef` declara.</summary>
	public static Texture2D? DoItem(ItemDef def) => De(def.Arte, def.Estado);

	/// <summary>
	/// O nome da animacao que um `icon_state` do DM vira na folha convertida: o mesmo saneamento do
	/// conversor de `.dmi` (minusculas, tudo que nao e letra ou digito vira `_`), e o estado vazio vira
	/// "default". E A regra -- quem quiser comparar estados compara por aqui, nao pelo texto cru.
	/// </summary>
	public static string NomeDaAnimacao(string estado) => estado.Length > 0 ? Sanear(estado) : "default";

	/// <summary>
	/// A folha TEM este estado com quadro? Distingue o fallback FIEL do defeito: no BYOND um
	/// `icon_state` que nao existe no `.dmi` mostra o estado padrao da folha (e "Camera Computer" com
	/// `PDA.dmi`/"Computer" sai igual ao PDA, la e aqui); ja duas folhas DIFERENTES dividindo um quadro
	/// e o carregador errado. A bancada da aba Tech usa isto pra so acusar o segundo caso.
	/// </summary>
	public static bool TemEstado(string arte, string estado)
	{
		if (arte.Length == 0 || !ResourceLoader.Exists(arte)) return false;
		if (ResourceLoader.Load<SpriteFrames>(arte) is not { } f) return false;
		string anim = NomeDaAnimacao(estado);
		return f.HasAnimation(anim) && f.GetFrameCount(anim) > 0;
	}

	private static Texture2D? Carregar(string arte, string estado)
	{
		if (arte.Length == 0 || !ResourceLoader.Exists(arte)) return null;
		if (ResourceLoader.Load<SpriteFrames>(arte) is not { } f) return null;

		// O NOME DO ESTADO PASSA PELO MESMO SANEAMENTO DO CONVERSOR -- "Radar" e "radar" sao a mesma
		// animacao, e um `.dmi` sem estado vira "default".
		string anim = NomeDaAnimacao(estado);
		if (!f.HasAnimation(anim) || f.GetFrameCount(anim) == 0)
		{
			// SEM O ESTADO PEDIDO, O PRIMEIRO QUE TEM QUADRO -- melhor um icone da folha certa do que
			// nenhum. E o que faz a folha de roupa servir de icone de item: o quadro de frente.
			anim = "";
			foreach (StringName a in f.GetAnimationNames())
				if (f.GetFrameCount(a) > 0) { anim = a; break; }
		}
		return anim.Length > 0 ? f.GetFrameTexture(anim, 0) : null;
	}

	/// <summary>O mesmo saneamento de nome que o conversor aplicou aos estados do `.dmi`.</summary>
	private static string Sanear(string s)
	{
		var sb = new System.Text.StringBuilder(s.Length);
		foreach (char c in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
		string r = sb.ToString().Trim('_');
		while (r.Contains("__")) r = r.Replace("__", "_");
		return r.Length == 0 ? "state" : r;
	}
}
