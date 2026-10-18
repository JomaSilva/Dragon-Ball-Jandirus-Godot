using Jandirus.Core.World;

namespace Jandirus.Core.Races;

/// <summary>
/// (v0.3.0) COMO OS PERSONAGENS NOVOS DE UM SERVIDOR GANHAM A RACA. E configuracao do SERVIDOR (quem escolhe e quem o
/// hospeda, na tela de hospedar, ou um administrador, na aba Admin do menu P) -- ver `Server/GameServer.ModoDeRacas.cs`.
/// </summary>
public enum ModoDeRacas : byte
{
	/// <summary>O jogo de sempre: cada jogador escolhe a propria raca na criacao.</summary>
	Padrao = 0,

	/// <summary>A raca inicial e SORTEADA pelo servidor, com os pesos do dono (<see cref="SorteioDeRaca"/>).</summary>
	Aleatorio = 1,
}

/// <summary>
/// ============================ O SORTEIO DA RACA INICIAL (v0.3.0, item 34 do pedido 15) ============================
/// O pedido: *"raças aleatorias onde o jogador n pode escolher sua raça inicial, sendo aleatoria ... as % de cada raça
/// seriam alien 22%, terraqueo 20%, namek 18%, saiyajin 15%, frost demon 8%, majin 4%. isso sao % aproximadas pois n dao
/// 100%, as outras raças vc pode colocar % pra dar 100% ... mas saiba q alien, terraqueo e namek sempre terao a maior
/// chance"*.
///
/// AS SEIS DO DONO SOMAM 87, e as outras TREZE racas que a criacao oferece (<see cref="CharacterDraft.RacasDoPlaneta"/>)
/// ficam com 1 cada -- 13 pontos, e a tabela fecha em 100 EXATOS sem mexer em nenhum numero dele. As tres maiores
/// continuam sendo Alien, Humano e Namekuseijin, como ele pediu. A bancada confere que a tabela tem TODA raca da criacao
/// e nenhuma outra: uma raca nova na criacao que nao ganhasse peso nunca sairia no sorteio, calada.
///
/// A SUB-RACA: *"primal saiyan e sub raças sao 50% quando o jogo escolher uma raça aleatoria q tem sub raça, classe como
/// lendario, mutante, elite etc segue o padrao aleatorio do jogo atual"*. As racas com sub-raca escolhida na criacao sao
/// as de <see cref="CharacterDraft.EscolhasDeClasse"/>:
///   * Saiyajin: Saiyajin ou Primal Saiyan, metade cada;
///   * Namekuseijin: o cla Guerreiro e a base (50%), e os dois clas "sub" dividem a outra metade (Dragao 25%, Demonio
///     25%) -- que e a MESMA divisao do DM pro Namekuseijin que nasce sem escolher (`PlanetPopulation.dm:405`).
/// A CLASSE (Lendario, Elite, Mutante...) nao passa por aqui: o servidor a sorteia no `Nascer`, como sempre.
/// ==========================================================================================================
/// </summary>
public static class SorteioDeRaca
{
	/// <summary>
	/// A TABELA DO SORTEIO, em pontos de 100. As seis primeiras sao as do dono, literais; as treze seguintes sao o resto
	/// da criacao, com 1 ponto cada. A chave e a da ficha ("Icer" e o Frost Demon, "Namekian" e o Namekuseijin).
	/// </summary>
	public static readonly (string Raca, int Peso)[] Pesos =
	[
		("Alien", 22), ("Human", 20), ("Namekian", 18), ("Saiyan", 15), ("Icer", 8), ("Majin", 4),
		("Shapeshifter", 1), ("Demigod", 1), ("Tsujin", 1), ("Saibaman", 1), ("Heran", 1), ("Meta", 1), ("Arlian", 1),
		("Makyo", 1), ("Gray", 1), ("Kanassa", 1), ("Yardrat", 1), ("Kai", 1), ("Demon", 1),
	];

	/// <summary>A soma da tabela -- 100.</summary>
	public static int SomaDosPesos
	{
		get
		{
			int s = 0;
			foreach ((_, int p) in Pesos) s += p;
			return s;
		}
	}

	/// <summary>
	/// O SORTEIO: a raca pela tabela e a sub-raca pela regra de cima. Funcao PURA da semente -- quem escolhe a semente e
	/// o servidor (uma que so ele conhece), e a bancada roda milhares delas pra medir as porcentagens.
	/// </summary>
	/// <param name="so">(2026-10-16) Quando nao nulo, a tabela fica so com as racas que ele aprova -- a garantia de classe rara do
	/// admin (`Birth.TemClasseRara`). Os pesos das que ficam nao mudam; se nenhuma passar, vale a tabela inteira.</param>
	public static (string Raca, string Linhagem) Sortear(ulong semente, Func<string, bool>? so = null)
	{
		(string Raca, int Peso)[] tabela = PesosDeTeste ?? Pesos;
		if (so != null && tabela.Where(x => so(x.Raca)).ToArray() is { Length: > 0 } filtrada) tabela = filtrada;
		int soma = 0;
		foreach ((_, int p) in tabela) soma += p;

		Random r = Npc.SorteioDeNpc.Sorteador(semente, "raca inicial");
		int ponto = r.Next(soma);
		string raca = tabela[^1].Raca;
		foreach ((string nome, int peso) in tabela)
		{
			if (ponto < peso) { raca = nome; break; }
			ponto -= peso;
		}
		return (raca, Linhagem(raca, r.NextDouble()));
	}

	/// <summary>
	/// A SUB-RACA de quem saiu no sorteio, por um numero em [0, 1): Saiyajin meio a meio, Namekuseijin com o cla Guerreiro
	/// na metade e os outros dois num quarto cada. Vazio pra quem nao tem sub-raca (a classe dele e do `Nascer`).
	/// </summary>
	public static string Linhagem(string raca, double u) => raca switch
	{
		"Saiyan" => u < 0.5 ? "Saiyan" : "Primal Saiyan",
		"Namekian" => u < 0.5 ? "Warrior clan" : u < 0.75 ? "Dragon clan" : "Demon clan",
		_ => "",
	};

	/// <summary>
	/// O MUNDO DE ORIGEM que a ficha leva pra uma raca sorteada: o primeiro planeta da criacao que a oferece -- o mesmo
	/// filtro que a etapa "DE ONDE VEM O SEU POVO?" faria, se o jogador tivesse passado por ela.
	/// </summary>
	public static string PlanetaDaRaca(string raca)
	{
		foreach (string p in CharacterDraft.Planetas)
			if (Array.IndexOf(CharacterDraft.RacasDoPlaneta(p), raca) >= 0) return p;
		return "";
	}

	/// <summary>A SEMENTE DE UM SORTEIO: o universo (que so o servidor conhece), a conta, a vaga e quantos sorteios a conta ja fez.</summary>
	public static ulong SementeDe(ulong seedDoUniverso, string conta, int vaga, int jaSorteados) =>
		Espaco.Misturar(seedDoUniverso, Espaco.Hash64(conta.ToLowerInvariant()), Espaco.Misturar((ulong)vaga, (ulong)jaSorteados, 0x5241_4341UL));

	/// <summary>O nome do modo, como a tela e o log o dizem.</summary>
	public static string Nome(ModoDeRacas modo) => modo == ModoDeRacas.Aleatorio ? "raças aleatórias" : "modo padrão";

	/// <summary>
	/// DEFEITO INJETADO (`--v030teste`): a tabela troca pela uniforme (todas com o mesmo peso) -- o sorteio que ignora o
	/// pedido. Nulo em jogo, sempre.
	/// </summary>
	public static (string Raca, int Peso)[]? PesosDeTeste;
}
