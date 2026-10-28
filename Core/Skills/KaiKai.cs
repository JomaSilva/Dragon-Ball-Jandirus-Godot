namespace Jandirus.Core.Skills;

/// <summary>
/// (2026-10-23) O KAI KAI COM TELA -- os numeros e as regras puras. O dono: *"faça o kai kai abrir uma tela com todos os planetas
/// conhecidos por jogadores e ao clicar ele teleporta pra eles no mesmo estilo do IT"*. As decisoes dele, no mesmo dia:
///   * a tela lista os DOZE destinos de sempre (`kai.dm:117`) e os mundos GERADOS que algum jogador ja pisou -- o livro de batismo,
///     global, com o nome do batismo; planeta destruido sai da lista;
///   * clicar leva NA HORA (sem concentracao, como o Kai Kai do DM), com o clarao e o som do Instant Transmission na saida e na
///     chegada, vistos por quem esta perto;
///   * continua cobrando o Ki CHEIO (Ki -> 0) e levando quem esta colado (a carona de 1,5 tile de sempre).
/// (2026-10-27, o pedido 135) O KI CHEIO CAIU: o dono, *"tira o consumo absurdo do kaikai, faça ele no maximo usar 10% do ki inicial de um
/// kaio, ou seja, bem pouco"* -- o salto custa <see cref="Custo"/> (10% do Ki com que um Kai nasce, um numero fixo e pequeno pra todo
/// mundo) e nao pede mais o tanque cheio. Vigor o Kai Kai nunca gastou.
/// Quem monta a lista e decide e o servidor (`Server/GameServer.KaiKai.cs`); o cliente so desenha (`Client/TelaDoKaiKai.cs`).
///
/// OS DOIS ATRASOS DO CLARAO (decisao minha): o clarao do IT e o efeito de desviar um golpe, e ele e desenhado NO CORPO de quem
/// pisca. Quem esta perto so o ve se o corpo estiver na tela dele: na saida o servidor pisca e so depois tira o corpo da zona
/// (<see cref="AtrasoDaSaidaMs"/>); na chegada o corpo novo entra no retrato da zona e so depois pisca
/// (<see cref="AtrasoDoClaraoDaChegadaMs"/>). Sem os dois, quem esta perto ouve o som e ve o corpo sumir e aparecer seco.
/// </summary>
public static class KaiKai
{
	/// <summary>
	/// QUANTO O CORPO FICA PISCANDO ANTES DE SUMIR, em ms. O clarao (`Zanzoken.dmi`) tem 3 quadros de 0,1 s; 0,2 s mostra dois deles a
	/// quem fica, e o corpo sai antes de o efeito acabar (com 0,3 s ou mais ele voltaria a aparecer por um instante antes de sumir).
	/// </summary>
	public const long AtrasoDaSaidaMs = 200;

	/// <summary>
	/// QUANTO O CLARAO DA CHEGADA ESPERA DEPOIS DO SALTO, em ms: tres tiques do servidor (30 Hz). Quem ja esta no destino recebe o
	/// corpo novo no retrato da zona do tique do salto; o clarao no mesmo tique chegaria antes do corpo e piscaria nada.
	/// </summary>
	public const long AtrasoDoClaraoDaChegadaMs = 100;

	/// <summary>
	/// MUNDO GERADO QUE AINDA NAO NASCEU NO SERVIDOR: o salto espera a geracao (o molde do pouso, `PousarEmProcedural`) e so fala
	/// se ela demorar mais que isto. O comum e o mundo ficar pronto em poucos tiques, e um aviso que some antes de ser lido e ruido.
	/// </summary>
	public const long EsperaSemAvisoMs = 1500;

	/// <summary>...e desiste depois disto, devolvendo a palavra ao jogador (o Ki nunca foi cobrado: so se paga quando se salta).</summary>
	public const long TetoDaEsperaMs = 30_000;

	/// <summary>(2026-10-27, o pedido 135) Que parte do Ki com que um Kai NASCE o salto custa -- o "no maximo 10%" do dono.</summary>
	public const double FracaoDoKiDeUmKai = 0.10;

	/// <summary>
	/// (2026-10-27, o pedido 135) O PRECO DO SALTO, em Ki: <see cref="FracaoDoKiDeUmKai"/> do Ki maximo de um Kai recem-nascido (quem chama
	/// passa esse numero -- o servidor o tira do funil de nascimento de producao), arredondado, e nunca menos de 1. Fixo e igual pra todo
	/// mundo: quem tem um tanque grande paga o mesmo pouco.
	/// </summary>
	public static double Custo(double kiDeUmKaiRecemNascido) => Math.Max(1, Math.Round(kiDeUmKaiRecemNascido * FracaoDoKiDeUmKai));

	/// <summary>
	/// O NOME DE SEMPRE DE UM DESTINO DO MAPA, como o jogador le (quando ninguem o batizou). A Terra e a "Terra" (o
	/// `NomeDeCatalogo` do servidor); o Ceu, o Inferno e o Outro Mundo tem nome em portugues, os mesmos da criacao de personagem
	/// (o Ceu como "Paraiso": e a palavra que o dicionario do ingles devolve como "Heaven"); o resto e o nome do mapa sem o
	/// sublinhado, como o DM escreve no menu dele ("Large Space Station").
	/// </summary>
	public static string NomePadrao(string zona) => zona switch
	{
		"Earth" => "Terra",
		"Heaven" => "Paraíso",
		"Hell" => "Inferno",
		"Afterlife" => "Outro Mundo",
		_ => zona.Replace('_', ' '),
	};

	/// <summary>
	/// O MESMO NOME DIGITADO DE OUTRO JEITO? Sem caixa e sem sublinhado: quem digita `Kai_Kai:Large Space Station` e quem digita
	/// `Kai_Kai:large_space_station` pedem o mesmo lugar.
	/// </summary>
	public static bool MesmoNome(string a, string b) =>
		string.Equals(a.Replace('_', ' ').Trim(), b.Replace('_', ' ').Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// UMA LINHA DA TELA DO KAI KAI. <see cref="Chave"/> e o que o botao manda no verbo (`Kai_Kai:chave`): o nome da zona pro mapa feito
/// a mao, `#seed` pro mundo gerado -- a <see cref="Jandirus.Core.World.ChaveDePlaneta"/> em texto, a mesma do livro de batismo e do
/// dos mortos. <see cref="Nome"/> e o que se le (o batismo, se houver). <see cref="Aqui"/>: e onde quem pediu esta agora.
/// </summary>
public readonly record struct DestinoDoKaiKai(string Chave, string Nome, bool Gerado, bool Aqui);
