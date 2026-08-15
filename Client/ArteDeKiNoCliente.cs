using Godot;
using Jandirus.Core.Combat;

namespace Jandirus.Client;

/// <summary>
/// ONDE MORA CADA FOLHA DE ATAQUE DE KI -- a metade `res://` da <see cref="ArteDeProjetil"/>.
///
/// ============================ ELE E UM FORMATADOR, E ISSO E DE PROPOSITO ============================
/// A tentacao era escrever aqui as 41 linhas `ArteDeKi.X => "res://.../X.tres"`. Isso criaria uma
/// SEGUNDA tabela de arte, e o unico modo de falha dela -- uma linha apontando pra folha da tecnica
/// vizinha -- compila, roda, e sai como *"o Masenko virou um Kienzan"*. Este projeto ja tem o
/// registro de duas tabelas paralelas divergindo em silencio.
///
/// Entao o Core diz a identidade da folha no BYOND (pasta + arquivo, `ArteDeProjetil.Folha`) e aqui
/// so se acrescenta o prefixo e a extensao. A regra da casa continua valendo ao pe da letra: o Core
/// nao sabe o que e `res://` nem o que e `.tres`; quem sabe e este arquivo, em uma linha.
///
/// O NOME DO `.tres` E O NOME DO `.dmi`, e isso e do pipeline, nao coincidencia: o conversor grava
/// `Assets/Sprites/&lt;pasta de Icons&gt;/&lt;mesmo nome&gt;.tres`. Foi conferido nas 41 folhas desta
/// tabela antes de a funcao ser escrita -- inclusive nos dois nomes com espaco e nos catorze que sao
/// so um numero.
/// ================================================================================================
///
/// ============================ AS FOLHAS SAO CACHEADAS, E O MISS TAMBEM ============================
/// `ResourceLoader.Load` bate no disco. Um Scattershot solta doze bolas de uma vez, e todas usam a
/// MESMA folha -- sem cache seriam doze cargas por disparo. O nulo tambem entra no dicionario: uma
/// folha ausente e permanente, e sem memorizar o miss o jogo tentaria carrega-la de novo a cada
/// tiro, pra sempre.
/// ===========================================================================================
/// </summary>
public static class ArteDeKiNoCliente
{
	private const string Raiz = "res://Assets/Sprites/";

	private static readonly Dictionary<ArteDeKi, SpriteFrames?> Cache = [];

	/// <summary>
	/// O caminho da folha, ou vazio quando a arte e <see cref="ArteDeKi.Nenhuma"/> (ou desconhecida
	/// desta versao do cliente -- ver o cabecalho de <see cref="ArteDeKi"/> sobre os numeros).
	/// </summary>
	public static string Caminho(ArteDeKi a)
	{
		(string pasta, string arquivo) = ArteDeProjetil.Folha(a);
		return pasta.Length == 0 ? "" : $"{Raiz}{pasta}/{arquivo}.tres";
	}

	/// <summary>
	/// A FOLHA. Nulo = desenhe pela primitiva de sempre.
	///
	/// FALHA MACIA E OBRIGATORIA AQUI: um `.tres` que nao importou (o modo de falha que este repo ja
	/// viu 35 vezes, com os atlas escritos e nunca importados) nao pode virar tiro INVISIVEL -- o
	/// jogador nao teria como distinguir isso de "a tecnica nao funciona". O aviso sai uma vez, no
	/// log, e o tiro continua sendo desenhado.
	/// </summary>
	public static SpriteFrames? Folha(ArteDeKi a)
	{
		if (Cache.TryGetValue(a, out SpriteFrames? f)) return f;

		string caminho = Caminho(a);
		f = caminho.Length == 0 ? null : ResourceLoader.Load<SpriteFrames>(caminho);
		if (f == null && caminho.Length > 0) GD.PushWarning($"[ki] folha ausente: {caminho}");

		Cache[a] = f;
		return f;
	}

	/// <summary>
	/// O NOME QUE O MENU DA TECNICA CUSTOMIZADA MOSTRA -- o `"[prefix]: [f]"` do `pick_game_icon`
	/// (`customattacks.dm:551`), que e literalmente `"Blasts: 12.dmi"`.
	///
	/// O rotulo e o NOME DO ARQUIVO e nao um apelido bonito, e isso e escolha: o catalogo tem 41
	/// folhas e nenhuma tem nome de jogo (o `28.dmi` nao se chama nada). Inventar apelidos seria
	/// inventar conteudo, e o original ja resolveu mostrando o arquivo.
	/// </summary>
	public static string Rotulo(ArteDeKi a)
	{
		(string pasta, string arquivo) = ArteDeProjetil.Folha(a);
		return pasta.Length == 0 ? "(padrao)" : $"{pasta}: {arquivo}.dmi";
	}
}
