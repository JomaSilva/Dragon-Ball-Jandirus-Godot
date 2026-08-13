using Jandirus.Core.Stats;

namespace Jandirus.Core.Skills;

/// <summary>
/// QUAIS TECNICAS SAO ATAQUE A DISTANCIA -- e, pra cada uma, as cinco perguntas que a IA precisa
/// fazer antes de decidir atirar.
///
/// ============================ ESTA TABELA ESTA VAZIA HOJE, E ESSE E O ESTADO CERTO ============================
/// Nenhuma das 28 tecnicas portadas e um ataque que VIAJA: nao ha, no servidor, uma entidade que
/// saia de um corpo, ande entre tiques e acerte outro. O pedido do dono foi *"deixar ja pronto o
/// terreno pra IA saber usar ataques de ki como beam e blast QUANDO adicionarmos as mecanicas"* --
/// entao o que nasce aqui e o LUGAR da resposta, e nao uma resposta inventada.
///
/// Enquanto <see cref="Alguma"/> for falso, o ramo inteiro da decisao de longe morre numa
/// comparacao com zero: o arsenal de todo corpo sai vazio, a pergunta de linha de visao nem chega a
/// ser feita (ela custa uma varredura de segmento -- ver `GameServer.LerPercepcao`) e o cerebro
/// nunca escolhe o plano de atirar. A bancada afirma exatamente isso.
///
/// **E a tabela nao pode ser preenchida "por enquanto, so pra testar".** Uma linha aqui liga a IA a
/// um efeito que nao existe: ela pararia de socar pra conjurar um nada, e o defeito apareceria como
/// "o NPC fica parado olhando". Se voce quer exercitar o gancho, monte um <c>Jandirus.Core.Ai.Tiro</c>
/// na mao e ponha nas <c>Capacidades</c> daquele corpo -- e o que a bancada faz, sem tocar nesta
/// tabela nem no processo inteiro.
/// ==========================================================================================================
///
/// ============================ POR QUE O CUSTO E UMA FUNCAO E NAO UM NUMERO ============================
/// A tentacao obvia e escrever `custoKi: 250` na linha. Isso cria a SEGUNDA COPIA DO PRECO: a
/// tecnica cobra o dela la no efeito (`Tecnicas.SolarCustoKi(f)`, `GameServer.Tecnicas.cs:58`) e a
/// IA acredita neste; no dia em que alguem reequilibrar um dos dois, a IA passa a decidir com um
/// preco que o jogo nao pratica -- e ninguem liga o defeito a esta linha.
///
/// Entao a linha aponta pra a MESMA funcao que o efeito chama. Quem registrar uma tecnica de longe
/// e nao tiver essa funcao esta dizendo, pela forma do dado, que o preco dela ainda mora dentro do
/// efeito e precisa sair de la primeiro.
///
/// E, mesmo assim, a IA trata o custo como PISO e nunca como permissao: quem recusa por falta de Ki
/// e o efeito. Uma divergencia entre os dois pode deixar a IA timida (ela nao atira quando podia);
/// nunca pode deixa-la trapacear (atirar sem pagar). Errar pro lado seguro e uma escolha.
/// ================================================================================================
/// </summary>
public static class TecnicasDeLonge
{
	/// <summary>
	/// UMA LINHA DA TABELA -- a descricao ESTATICA de uma tecnica de longe.
	///
	/// Nao confundir com <c>Jandirus.Core.Ai.Tiro</c>: aquele e o que sobra depois de resolver esta
	/// linha PARA UM CORPO (o custo vira numero, os tiles viram pixels). Esta aqui nao conhece
	/// ninguem, e por isso e dado compartilhado; aquele e resposta do jogo pra um corpo so, e por
	/// isso mora nas `Capacidades`.
	/// </summary>
	public readonly record struct Linha(
		/// <summary>O id do verb do DM -- o MESMO que viaja no `C2S.Habilidade` ("Kamehameha").</summary>
		string Id,
		/// <summary>Perto demais nao vale: quem esta colado leva soco, e nao raio. Em TILES.</summary>
		float AlcanceMinTiles,
		/// <summary>Alem disto o golpe nao chega. Em TILES.</summary>
		float AlcanceMaxTiles,
		/// <summary>Quanto tempo de pe, parado, ate o golpe sair. Segundos.</summary>
		double TempoDeConjuracao,
		/// <summary>O preco, pela MESMA funcao que o efeito cobra. Ver o cabecalho.</summary>
		Func<Fighter, double> CustoDeKi,
		/// <summary>Parede no caminho impede? Um raio sim; uma bomba em arco, nao.</summary>
		bool PrecisaDeLinhaLivre,
		/// <summary>0..1 -- o quanto ela perdoa alvo longe e alvo em movimento. 1 = nunca erra.</summary>
		double Precisao);

	/// <summary>
	/// A TABELA, e ela e a coisa inteira. Vazia -- ver o cabecalho.
	///
	/// ============================ NO DIA DO BEAM, E UMA LINHA AQUI ============================
	/// <code>
	/// ["Kamehameha"] = new("Kamehameha", 3f, 14f, 1.6, Tecnicas.KameCustoKi, true, 0.70),
	/// </code>
	/// Alcance minimo e maximo em tiles, tempo de conjuracao em segundos, a FUNCAO de custo da
	/// propria tecnica, se parede atrapalha, e o quanto ela perdoa. Nada mais: a decisao de quando
	/// usar ja esta escrita (`Cerebro.EscolherTiro`), o gesto ja esta escrito (`Cerebro.Disparo`) e
	/// o canal ja esta escrito (`GameServer.AplicarComando` -> `UsarHabilidade`).
	///
	/// A tabela e um inicializador e nao um `Registrar(...)`: um metodo de registro que ninguem
	/// chama e codigo morto esperando um dono, e este projeto ja tem historico de escrever a porta
	/// antes da casa. Um dicionario vazio com o formato na frente e a mesma porta, sem o corpo.
	/// ====================================================================================
	/// </summary>
	private static readonly Dictionary<string, Linha> Tudo = new(StringComparer.OrdinalIgnoreCase)
	{
		// (vazia de proposito -- ver o cabecalho do arquivo)
	};

	/// <summary>Quantas tecnicas de longe o jogo conhece. ZERO hoje.</summary>
	public static int Quantas => Tudo.Count;

	/// <summary>
	/// EXISTE ALGUM ATAQUE DE LONGE NESTE JOGO? A poda mais barata do sistema: enquanto for falso,
	/// nenhum livro de skill e varrido e nenhuma linha de visao e tracada.
	/// </summary>
	public static bool Alguma => Tudo.Count > 0;

	/// <summary>
	/// Esta tecnica e de longe? Nulo = nao (que e a resposta pra TODAS, hoje).
	///
	/// O SOLAR FLARE NAO ENTRA NA TABELA, e vale dizer por que: ele TEM alcance
	/// (`Tecnicas.SolarAlcanceTiles`) mas nao e um ataque -- cega quem esta OLHANDO, nao causa dano
	/// e nao viaja. Registra-lo faria a IA "atirar" flashes no meio do soco, comportamento que
	/// ninguem pediu. A tabela e de ATAQUE A DISTANCIA, e a distincao e essa.
	/// </summary>
	public static Linha? Get(string id) =>
		id.Length > 0 && Tudo.TryGetValue(id, out Linha l) ? l : null;
}
