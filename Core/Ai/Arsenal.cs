namespace Jandirus.Core.Ai;

/// <summary>
/// PRA QUE SERVE UMA TECNICA NA MAO DA IA (dono, 2026-10-13: *"deveriam usar todas as skills disponiveis"*).
///
/// E o que a tecnica E, e nao quando usa-la: a decisao continua toda no <see cref="Cerebro"/>, que tem uma receita
/// por uso. Mora ao lado do <see cref="Tiro"/> porque e ele que carrega o dado ate o cerebro.
/// </summary>
public enum Uso : byte
{
	/// <summary>VIAJA: janela de distancia, linha de visao, risco de errar. O arsenal de longe (`TecnicasDeLonge`).</summary>
	Tiro,

	/// <summary>DE PERTO: sai no lugar de um soco, com o alvo colado (os combos, os agarroes, o Solar Flare).</summary>
	Golpe,

	/// <summary>DE MEIA DISTANCIA: fecha o espaco e bate no fim, ou pega o alvo marcado de onde esta (as corridas, o grilhao).</summary>
	Investida,

	/// <summary>REFORCO: liga quando a luta comeca e desliga quando ela acaba (ou quando o Ki pede).</summary>
	Reforco,

	/// <summary>SOCORRO: quando a vida cai.</summary>
	Socorro,
}

/// <summary>O que a tecnica pede da CENA. Sao fatos do verb -- ele recusaria (ou puniria) fora deles.</summary>
[Flags]
public enum Exige : byte
{
	Nada = 0,

	/// <summary>So pega quem VOA (o Chute Descendente arranca o alvo do ar).</summary>
	AlvoNoAr = 1,

	/// <summary>Nao pega quem voa (a Rasteira: *"nao ha chao pra ele tropecar"*).</summary>
	AlvoNoChao = 2,

	/// <summary>Cobra do PROPRIO corpo (o Kikoho, o Cuspe Acido): so com a vida acima da metade.</summary>
	VidaSobrando = 4,

	/// <summary>E golpe de AGARRAO: pega quem esta na frente (ou usa quem ja esta nos bracos).</summary>
	Agarra = 8,

	/// <summary>NAO SE DESLIGA: de pe, nao se aperta de novo (a Divisao do Corpo -- o segundo aperto faria OUTRA copia).</summary>
	NaoSeDesliga = 16,

	/// <summary>Reage a TIRO: so serve com ki alheio vindo pra cima do corpo (a Deflexao).</summary>
	TiroVindo = 32,

	/// <summary>(Socorro.) Acode o TANQUE, e nao a vida: sai com o Ki no fim (o Estirao).</summary>
	KiNoFim = 64,
}

/// <summary>
/// UM ATAQUE DE LONGE, JA RESOLVIDO PRA ESTE CORPO -- alcance em pixels, custo em Ki de verdade.
///
/// E o par de <c>TecnicasDeLonge.Linha</c> (que e a descricao estatica, compartilhada) depois que o
/// servidor perguntou "quanto isto custa PRA ELE?" e "ele sabe esta tecnica?". Quem monta e
/// `GameServer.ArsenalDeLonge`, junto com o resto das <see cref="Capacidades"/>, a 1 Hz.
///
/// ============================ NENHUM CAMPO DAQUI E UMA DECISAO ============================
/// Sao os cinco dados que o dono nomeou -- alcance, tempo de conjuracao, custo de Ki, linha de
/// visao e risco de errar -- e nada mais. Nao ha "prioridade", "peso" nem "quando usar": isso e
/// escolha, e escolha e do <see cref="Cerebro"/>. Um campo `Prioridade` aqui pareceria inofensivo e
/// espalharia a decisao por dois arquivos, que e como uma IA vira impossivel de calibrar.
/// ======================================================================================
/// </summary>
public readonly struct Tiro
{
	/// <summary>O id do verb, o MESMO que o `C2S.Habilidade` do jogador carrega.</summary>
	public string Id { get; init; }

	/// <summary>O QUE ESTA TECNICA E -- ver <see cref="Ai.Uso"/>. `Tiro` (o valor zero) pra toda linha do arsenal de longe.</summary>
	public Uso Uso { get; init; }

	/// <summary>
	/// A SEGUNDA MOEDA: o preco em FOLEGO absoluto (o Spirit Gun, o Punho Espiritual, o Hokuto). Zero pra quase
	/// todas. Mesma disciplina do <see cref="CustoDeKi"/>: e PISO da decisao, e quem recusa de verdade e o verb.
	/// </summary>
	public double CustoDeFolego { get; init; }

	/// <summary>O que a tecnica pede da CENA pra servir pra alguma coisa -- ver <see cref="Ai.Exige"/>.</summary>
	public Exige Exige { get; init; }

	/// <summary>
	/// SEGUNDOS SEM REPETIR ESTA MESMA TECNICA. E o que faz o corpo VARIAR: sem ele a nota da escolha
	/// (`(1 - risco) * CustoDeKi`) elege sempre a mais cara, e um corpo que sabe trinta tiros soltaria um so. Zero =
	/// o descanso padrao do cerebro. Como a <see cref="Precisao"/>, e julgamento de quem escreve a linha.
	/// </summary>
	public double Descanso { get; init; }

	/// <summary>
	/// (So <see cref="Uso.Reforco"/>.) ESTA DE PE AGORA? Lido no relogio de 1 Hz do registro de buffs do servidor --
	/// o cerebro nao guarda "eu liguei": quem derruba um reforco (o nocaute, o Ki zerado) nao avisa ninguem.
	/// </summary>
	public bool Ligada { get; init; }

	/// <summary>(So <see cref="Uso.Reforco"/>.) Os que dividem o mesmo numero nao se sustentam juntos. Zero = sem grupo.</summary>
	public byte Grupo { get; init; }

	/// <summary>(So <see cref="Uso.Reforco"/>.) O id que DESLIGA, quando nao e o mesmo que liga (o grau zero da Expansao). Nulo = o mesmo.</summary>
	public string? IdDeDesligar { get; init; }

	/// <summary>Colado demais nao sai: em PIXELS (a tabela guarda tiles; a conversao e do servidor).</summary>
	public float AlcanceMin { get; init; }

	/// <summary>Alem disto nao chega, em PIXELS.</summary>
	public float AlcanceMax { get; init; }

	/// <summary>Segundos de pe, parado, antes do golpe sair.</summary>
	public double TempoDeConjuracao { get; init; }

	/// <summary>
	/// O preco em Ki ABSOLUTO pra este corpo. E PISO da decisao, nunca permissao: quem recusa de
	/// verdade e o efeito da tecnica. Ver o cabecalho de `TecnicasDeLonge`.
	/// </summary>
	public double CustoDeKi { get; init; }

	/// <summary>Parede no caminho impede este golpe?</summary>
	public bool PrecisaDeLinhaLivre { get; init; }

	/// <summary>0..1 -- o quanto ela perdoa distancia e alvo em movimento.</summary>
	public double Precisao { get; init; }
}

/// <summary>
/// O QUE ESTE CORPO PODE ATIRAR AGORA -- as tecnicas de `TecnicasDeLonge` que ele SABE, ja com o
/// preco em Ki resolvido pra ficha dele.
///
/// ============================ ELE FOI UM GANCHO INERTE POR TRES CAMADAS ============================
/// Nasceu vazio de proposito, com a regra de admissao escrita aqui: um gancho so nao e codigo morto
/// se tiver (a) um consumidor de verdade -- o <see cref="Cerebro"/> pergunta por ele em toda decisao
/// --, (b) uma afirmacao de que hoje ele nao dispara, e (c) uma bancada que o EXERCITA com dado
/// sintetico. As tres existiam.
///
/// A condicao pra ele deixar de ser inerte era existir ataque que VIAJA, e ela foi cumprida na
/// camada 1 do sistema de ki. Hoje a tabela tem tres linhas (raio, bola, teleguiado) e este arsenal
/// sai preenchido pra quem comprou as skills -- sem uma linha de codigo novo no cerebro.
/// ================================================================================================
///
/// `default(Arsenal)` e vazio de proposito: um corpo cujas capacidades nunca foram lidas nao tem
/// arsenal, e nao tem que virar caso especial em lugar nenhum.
/// </summary>
public readonly struct Arsenal
{
	private readonly Tiro[]? _opcoes;

	public Arsenal(Tiro[]? opcoes) => _opcoes = opcoes;

	/// <summary>Nenhum ataque de longe -- o valor de quem nao comprou nenhuma das tres skills.</summary>
	public static readonly Arsenal Vazio = new(null);

	public int Quantas => _opcoes?.Length ?? 0;

	/// <summary>
	/// A PODA. Um `int` contra zero -- e a primeira pergunta do ramo de longe, no cerebro E no
	/// servidor. Enquanto ela for falsa, nem o livro de skills e varrido nem a linha de visao e
	/// tracada; o custo do gancho e literalmente uma comparacao por decisao.
	/// </summary>
	public bool TemAlguma => Quantas > 0;

	public Tiro this[int i] => _opcoes![i];

	/// <summary>
	/// O RISCO DE ERRAR, 0..1. Funcao PURA -- da pra tabelar num teste sem servidor nenhum.
	///
	/// Tres coisas, e as tres sao as que um jogador pesa antes de largar um Kamehameha:
	///   * a tecnica em si (<see cref="Tiro.Precisao"/>);
	///   * a distancia DENTRO da janela -- no limite do alcance erra-se mais que no meio dela;
	///   * o alvo estar andando, que e de longe o maior fator (e por isso o multiplicador, e nao
	///     uma soma: contra alvo parado o risco tem que poder chegar perto de zero).
	///
	/// O `Moving` do alvo ja existe e ja e escrito todo tique pelo passo -- nenhuma varredura nova
	/// nasceu pra responder isto, que e a regra de admissao da <see cref="Percepcao"/>.
	/// </summary>
	public static double RiscoDeErrar(in Tiro t, float distancia, bool alvoSeMovendo)
	{
		double faixa = t.AlcanceMax - t.AlcanceMin;
		double naFaixa = faixa > 0.01 ? Math.Clamp((distancia - t.AlcanceMin) / faixa, 0, 1) : 0;
		double risco = (1 - Math.Clamp(t.Precisao, 0, 1)) * (0.35 + 0.65 * naFaixa);
		if (alvoSeMovendo) risco *= 1.6;
		return Math.Clamp(risco, 0, 1);
	}
}
