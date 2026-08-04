namespace Jandirus.Core.Items;

/// <summary>
/// UM TIPO DE ITEM -- o que ele e, como se desenha, e o que da pra fazer com ele.
///
/// ============================ POR QUE UM CATALOGO, E NAO CLASSES ============================
/// No DM cada item e um typepath com verbs proprios, e isso funciona la porque o objeto E o
/// codigo. Aqui um item precisa atravessar a rede e o disco, e uma hierarquia de classes obrigaria
/// a serializar POLIMORFISMO -- o pior formato possivel pra uma coisa que so precisa dizer "sou
/// uma maca, tenho seis de nutricao".
///
/// A pilha guarda um ID e uma quantidade. Todo o resto e olhado aqui.
/// ============================================================================================
/// </summary>
public sealed record ItemDef(
	string Id,
	string Nome,
	string Descricao,
	string Arte,
	string Estado,
	bool Empilhavel,
	double Nutricao = 0,
	string[]? Acoes = null)
{
	/// <summary>
	/// O QUE DA PRA FAZER COM ELE. Verbos, como no resto do jogo -- ver `Tech.Interacoes`.
	///
	/// "largar" nao entra aqui: ela vale pra TODO item e o menu a acrescenta sozinha. Repeti-la em
	/// cada linha do catalogo seria trinta chances de esquecer uma.
	/// </summary>
	public string[] AcoesDoItem => Acoes ?? (Nutricao > 0 ? ["comer"] : []);
}

/// <summary>
/// O CATALOGO DE ITENS. Pequeno de proposito: so o que o port ja sabe FAZER.
///
/// ============================ ELE COMECA PEQUENO E ISSO E CERTO ============================
/// O DM tem centenas de itens. Despejar todos aqui daria um inventario cheio de coisas que nao
/// respondem -- e um item que nao faz nada e pior que um item que nao existe: ele promete.
///
/// Cada item entra quando a mecanica dele entra. A maca abre a lista porque comer ja funciona
/// (ver `Stats.Nutricao`).
/// ===========================================================================================
/// </summary>
public static class CatalogoDeItens
{
	public const string Maca = "Apple";
	public const string Scouter = "Scouter";
	public const string Pesos = "Weights";
	public const string Bandagem = "Bandages";
	public const string KitMedico = "First_Aid_Kit";
	public const string Pa = "Shovel";
	public const string Furadeira = "Hand_Drill";

	/// <summary>
	/// ============================ OS IDS SAO OS DO CATALOGO DE CONSTRUCOES ============================
	/// "Scouter" aqui e o MESMO "Scouter" de `construcoes.json`, e nao por acaso: e essa igualdade
	/// que faz `Construir` saber que o item vai pra mochila em vez de virar movel no chao. Um
	/// segundo conjunto de nomes exigiria uma tabela de traducao entre os dois, e a primeira coisa
	/// a divergir seria um item novo que alguem cadastrou so num lado.
	/// ==================================================================================================
	/// </summary>
	private static readonly Dictionary<string, ItemDef> Tudo = new(StringComparer.OrdinalIgnoreCase)
	{
		[Maca] = new ItemDef(
			Maca, "Maçã", "Cada mordida é perfeição frutada.",
			"res://Assets/Sprites/Misc/Misc3.tres", "Apple",
			Empilhavel: true, Nutricao: 6),

		// O SCOUTER ACENDE UM SISTEMA QUE JA EXISTIA INTEIRO E MORTO: o bit `Poder.Scouter`, o corte
		// de BP no pacote de ficha e o "???" da tela estao no port desde sempre, com um comentario
		// dizendo "hoje o bit nunca acende -- o port ainda nao tem o item scouter". Ele e o item.
		[Scouter] = new ItemDef(
			Scouter, "Scouter", "Lê o poder de luta de quem você olha. Sem ele, todo número é \"???\".",
			"res://Assets/Sprites/Misc/Objects/Technology/Scouter.tres", "",
			Empilhavel: false, Acoes: ["equipar"]),

		// PESO NAO E EQUIPAMENTO COMUM: ele ATRAPALHA de propósito, e o ganho e o treino que a
		// dificuldade rende. Por isso a acao e "ajustar" e nao "equipar" -- o quanto importa.
		[Pesos] = new ItemDef(
			Pesos, "Pesos", "Atrasa cada passo e cada golpe. Em troca, treinar rende muito mais.",
			"res://Assets/Sprites/Misc/Objects/Technology/tech.tres", "weights",
			Empilhavel: false, Acoes: ["ajustar", "tirar"]),

		[Bandagem] = new ItemDef(
			Bandagem, "Bandagens", "Fecha o que está aberto. Devagar, e só fora de combate.",
			"res://Assets/Sprites/Misc/Objects/Technology/tech.tres", "bandage",
			Empilhavel: true, Acoes: ["usar"]),

		[KitMedico] = new ItemDef(
			KitMedico, "Kit de Primeiros Socorros", "Dez usos. Cura bem mais que uma bandagem.",
			"res://Assets/Sprites/Misc/Objects/Technology/FirstAid.tres", "",
			Empilhavel: false, Acoes: ["usar"]),

		[Pa] = new ItemDef(
			Pa, "Pá", "Dá pra cavar. Devagar...",
			"res://Assets/Sprites/Misc/Shovel.tres", "",
			Empilhavel: false, Acoes: ["cavar"]),

		[Furadeira] = new ItemDef(
			Furadeira, "Furadeira Manual", "Cava bem mais rápido que uma pá.",
			"res://Assets/Sprites/Misc/Objects/Technology/tech.tres", "drill",
			Empilhavel: false, Acoes: ["cavar"]),
	};

	public static ItemDef? Get(string id) => Tudo.GetValueOrDefault(id);
	public static IEnumerable<ItemDef> Todos => Tudo.Values;

	/// <summary>
	/// ESTA CONSTRUCAO E, NA VERDADE, UM ITEM DE CARREGAR?
	///
	/// E a pergunta que `Construir` faz: pa, scouter e bandagem sao comprados na mesma bancada que
	/// a maquina de gravidade, mas nao ficam no chao -- vao pra mochila.
	/// </summary>
	public static bool EhCarregavel(string idDaConstrucao) => Tudo.ContainsKey(idDaConstrucao);
}

/// <summary>
/// UMA PILHA no inventario: o tipo e quantos.
///
/// RECORD STRUCT porque ela e valor puro. A lista inteira e copiada pra rede e pro disco, e uma
/// classe faria duas pilhas apontarem pro mesmo objeto depois de um `[.. lista]`.
/// </summary>
public record struct Pilha(string Id, int Quantidade);

/// <summary>
/// O QUE O PERSONAGEM CARREGA.
///
/// ============================ TRINTA SLOTS, COMO NO ORIGINAL ============================
/// `inven_max = 30` (`Inventory.dm:158-162`). O limite e em SLOTS e nao em peso, e uma pilha ocupa
/// um slot inteiro por maior que seja -- e o que faz item empilhavel valer a pena e o que da ao
/// jogador uma razao pra escolher o que largar.
/// ========================================================================================
///
/// ============================ EMPILHAR E DO TIPO, NAO DO INVENTARIO ============================
/// O DM decide isso por item (`stackable`), e nao globalmente: maca junta, maquina de gravidade
/// nao. Duas maquinas iguais tem que ocupar dois slots porque cada uma tem ESTADO proprio (bateria,
/// alcance, gravidade ligada) -- fundi-las numa pilha de dois apagaria um dos dois estados.
/// ==============================================================================================
/// </summary>
public sealed class Inventario
{
	public const int Slots = 30;

	public List<Pilha> Pilhas = [];

	public int Ocupados => Pilhas.Count;
	public bool Cheio => Pilhas.Count >= Slots;

	/// <summary>Quantos deste tipo o personagem tem, somando as pilhas.</summary>
	public int Quantos(string id)
	{
		int n = 0;
		foreach (Pilha p in Pilhas)
			if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) n += p.Quantidade;
		return n;
	}

	/// <summary>
	/// GUARDA. Devolve quantos NAO couberam -- zero quer dizer que entrou tudo.
	///
	/// Devolver o resto em vez de um booleano importa: quem chama precisa saber o que fazer com a
	/// sobra (deixar no chao, avisar, cancelar a colheita), e um `false` nao diz quanto sobrou.
	/// </summary>
	public int Guardar(string id, int quantos = 1)
	{
		if (quantos <= 0) return 0;
		ItemDef? def = CatalogoDeItens.Get(id);
		if (def == null) return quantos;

		if (def.Empilhavel)
		{
			for (int i = 0; i < Pilhas.Count; i++)
			{
				if (!string.Equals(Pilhas[i].Id, id, StringComparison.OrdinalIgnoreCase)) continue;
				Pilhas[i] = Pilhas[i] with { Quantidade = Pilhas[i].Quantidade + quantos };
				return 0;
			}
		}

		// UM SLOT POR UNIDADE quando o item nao empilha -- e por isso o laco: guardar tres maquinas
		// de uma vez tem que ocupar tres slots, ou parar quando acabarem.
		int sobrou = quantos;
		while (sobrou > 0)
		{
			if (Cheio) return sobrou;
			int nesteSlot = def.Empilhavel ? sobrou : 1;
			Pilhas.Add(new Pilha(def.Id, nesteSlot));
			sobrou -= nesteSlot;
		}
		return 0;
	}

	/// <summary>
	/// TIRA. Devolve quantos saiu de verdade (pode ser menos do que se pediu).
	///
	/// A pilha que zera SOME da lista: deixar um slot com quantidade zero seria um buraco que
	/// aparece na tela como um quadrado vazio no meio das coisas.
	/// </summary>
	public int Tirar(string id, int quantos = 1)
	{
		int levou = 0;
		for (int i = Pilhas.Count - 1; i >= 0 && levou < quantos; i--)
		{
			if (!string.Equals(Pilhas[i].Id, id, StringComparison.OrdinalIgnoreCase)) continue;

			int daqui = Math.Min(Pilhas[i].Quantidade, quantos - levou);
			levou += daqui;

			int resta = Pilhas[i].Quantidade - daqui;
			if (resta > 0) Pilhas[i] = Pilhas[i] with { Quantidade = resta };
			else Pilhas.RemoveAt(i);
		}
		return levou;
	}

	/// <summary>
	/// JOGA FORA O QUE NAO EXISTE MAIS NO CATALOGO.
	///
	/// Um save antigo pode carregar o id de um item que foi renomeado ou removido, e um id que o
	/// catalogo nao conhece vira um slot que a tela nao consegue desenhar e o menu nao consegue
	/// usar -- ocupando espaco pra sempre. Roda na carga.
	/// </summary>
	public void Sanear()
	{
		Pilhas.RemoveAll(p => CatalogoDeItens.Get(p.Id) == null || p.Quantidade <= 0);
		if (Pilhas.Count > Slots) Pilhas.RemoveRange(Slots, Pilhas.Count - Slots);
	}
}
