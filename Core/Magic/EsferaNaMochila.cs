using Jandirus.Core.Items;

namespace Jandirus.Core.Magic;

/// <summary>
/// **A ESFERA DO DRAGAO NA MOCHILA** -- o pedido do dono (2026-10-19): *"caso pegue as esferas elas vao pro seu inventario
/// (mantendo o sprite atual, se elas estiverem como pedra, elas vao como pedra, se nao vao como a esfera laranja mesmo
/// normal) podendo dropar elas ou nao"*.
///
/// ============================ ELA APARECE NA MOCHILA, MAS NAO MORA NELA ============================
/// Quem carrega uma esfera continua sendo dito por UM lugar so: o `Portador` dela, no servidor (`GameServer.Esferas.cs`). A
/// mochila (<see cref="Inventario.Pilhas"/>) vai pro save do personagem, e a esfera na mao NUNCA foi salva -- nem no DM (o
/// filtro do save exige `x>=1`) nem aqui (o `Portador` e `[JsonIgnore]`). Uma copia dela entre as pilhas seria a segunda
/// resposta pra "quem tem a esfera de 4 estrelas", e no dia em que as duas discordassem a esfera estaria duplicada -- no bau,
/// no saque de um caido, no save de quem deslogou com ela.
///
/// Entao ela entra so no PACOTE da mochila (`Protocol.PutInventario`, depois das pecas vestidas) e na tela, como mais um
/// slot, com este id: `Esfera|set|numero|pedra-ou-acesa|folha`. O dado vai no id pelo motivo do
/// <see cref="LivroDeEnsinamentos"/>: o id ja atravessa a rede intacto, e a tela desenha o slot sem aprender nada novo. Ela
/// NAO OCUPA os trinta espacos (o `Get()` do DM tambem nao pergunta pelo `inven_max`, `Dragonballs.dm:344-350`).
/// ======================================================================================================
///
/// O SPRITE E O DO CHAO: a mesma folha e o mesmo estado (<see cref="Esferas.EstadoDoSprite"/>: "inactive" e a pedra,
/// "1".."7" a esfera laranja) que a `EsferaDesenhada` usa no mundo -- e a folha sai daqui pras duas
/// (<see cref="ArteDaEsfera"/>). O set que acorda com a esfera na mao troca a pedra pela esfera acesa no pacote seguinte.
/// </summary>
public sealed record EsferaNaMochila(int Set, int Numero, bool Apagada, bool DeNamek)
{
	/// <summary>O prefixo do id. Uma constante porque ela e um contrato entre o servidor, o pacote e a tela.</summary>
	public const string Prefixo = "Esfera|";

	/// <summary>
	/// QUANTAS O PACOTE LEVA. Sete do planeta, sete de outro (Lookout) e as sete Black Star, que saem do planeta.
	/// </summary>
	public const int TetoNoPacote = 3 * Esferas.Total;

	/// <summary>A Black Star. O id ganha "negra" no lugar de "comum"/"namek"; os dois antigos continuam valendo.</summary>
	public bool Negra { get; init; }

	public string Folha => Negra ? EsferasNegras.Folha : DeNamek ? "namek" : "comum";

	/// <summary>
	/// A FOLHA DA ESFERA: Terra, Namek ou estrela negra. Uma casa so pro chao e pra mochila.
	/// </summary>
	public static string ArteDaEsfera(bool deNamek) =>
		deNamek ? "res://Assets/Sprites/Misc/dragonball.tres" : "res://Assets/Sprites/Misc/Dragonballs.tres";

	public static string ArteDaFolha(string folha) => folha switch
	{
		"namek" => "res://Assets/Sprites/Misc/dragonball.tres",
		EsferasNegras.Folha => EsferasNegras.ArteDaEsfera,
		_ => "res://Assets/Sprites/Misc/Dragonballs.tres",
	};

	public string Id => $"{Prefixo}{Set}|{Numero}|{(Apagada ? "pedra" : "acesa")}|{Folha}";

	public string Estado => Negra ? EsferasNegras.EstadoDoSprite(Numero, Apagada) : Esferas.EstadoDoSprite(Numero, Apagada);

	/// <summary>
	/// A ficha que a tela le. Sem acao propria: "largar" a mochila acrescenta a todo item (`ItemDef.AcoesDoItem`), e e a
	/// unica coisa que se faz com ela -- invocar e o verbo da aba World, com as sete juntas.
	/// </summary>
	public ItemDef Ficha => new(
		Id,
		Negra ? EsferasNegras.NomeDaEsfera(Numero) : Esferas.NomeDaEsfera(Numero),
		Negra
			? (Apagada ? "Uma Esfera de Estrela Negra -- APAGADA: pedra até o ano do dragão passar. "
					   : "Uma Esfera de Estrela Negra. ")
			  + "Ela sai do planeta com você. Com as sete, INVOCAR (aba World) chama o Shenlong. Um desejo só, bem mais forte."
			: (Apagada ? "Uma das sete Esferas do Dragão -- APAGADA: virou pedra até o set se refazer. "
					   : "Uma das sete Esferas do Dragão. ")
			  + "Com as sete na mochila, INVOCAR (aba World) as tira daqui e chama o dragão. Ela não sai do planeta: nave, espaço e "
			  + "teleporte recusam quem a carrega (caverna e Lookout, não). Nocauteado ou morto, você a derruba.",
		ArteDaFolha(Folha),
		Estado,
		Empilhavel: false,
		Acoes: []);

	/// <summary>
	/// Desmonta um id de esfera. Nulo pra qualquer outro id -- e a porta do <see cref="CatalogoDeItens.Get"/>, e a do servidor
	/// que recebe o "largar" do slot (`GameServer.AcaoDaEsferaNaMochila`).
	/// </summary>
	public static EsferaNaMochila? Ler(string id)
	{
		if (!id.StartsWith(Prefixo, StringComparison.Ordinal)) return null;
		string[] partes = id.Split('|');
		if (partes.Length != 5) return null;
		if (!int.TryParse(partes[1], out int set) || set < 0) return null;
		if (!int.TryParse(partes[2], out int numero) || numero < 1 || numero > Esferas.Total) return null;
		bool? apagada = partes[3] switch { "pedra" => true, "acesa" => false, _ => null };
		bool negra = partes[4] == EsferasNegras.Folha;
		bool? deNamek = partes[4] switch { "namek" => true, "comum" => false, EsferasNegras.Folha => false, _ => null };
		if (apagada is not { } a || deNamek is not { } n) return null;
		return new EsferaNaMochila(set, numero, a, n) { Negra = negra };
	}
}
