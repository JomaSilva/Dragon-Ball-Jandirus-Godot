namespace Jandirus.Core.World;

/// <summary>
/// ============================ OS SERVICOS DO OUTRO MUNDO (pedido de jogador repassado pelo dono, 2026-10-19) ============================
/// *"adicionar talvez uma rede de quests no outro mundo pra ganhar grana? (...) ao menos nao teria que mexer no torneio mais sacas e
/// daria mais profundidade ao outro mundo"*. O morto ganha zeni hoje quase so pelo Torneio do Outro Mundo (1M/500k/250k a cada 12 h,
/// so pro podio) e pelo vilao do Inferno (so quem derruba); e o revive do Enma custava 1.000.000 (200.000 desde 2026-10-23,
/// `Alem.PrecoDoReviveDoEnma`). O dono aprovou o desenho (perguntado):
///   * QUATRO DOADORES de conversa (menu E), com arte que ja existia: o Oni da recepcao (ao lado da mesa do Enma, onde todo morto
///     chega -- e quem abre a rede), a Princesa Serpente (no palacio do Caminho da Serpente), o Sr. Kaioh (o de sempre, na ilha do
///     norte) e o Oni do Inferno (na sala da passagem);
///   * SETE SERVICOS, um de cada vez, cada um repetivel uma vez por dia do jogo (24 min reais): entregas, nocautes, meditar e treinar;
///   * A REDE: o Sr. Kaioh e o Oni do Inferno so dao servico a quem ja fez uma ENTREGA pra eles (a recepcao manda as duas);
///   * SO OS MORTOS (*"Só os mortos"*): e pra quem esta preso no Outro Mundo juntar o revive sem depender do torneio.
/// Uma volta inteira paga 255.000: com o revive a 200.000 (o dono, 2026-10-23), UMA volta ja paga a volta a vida -- eram umas
/// quatro com o milhao de antes. No DM nao ha nada disso: a renda do morto la era o
/// `Afterlife_Fighter` e o torneio celeste (`NPClist.dm:324-333`, `Tournament.dm:29-31`).
///
/// AQUI MORA SO A REGRA: o catalogo, a ficha de cada personagem (que vai no save, dentro do `Fighter`) e o que pode e o que nao pode.
/// Quem esta morto, onde o corpo esta, quem nocauteou quem e quanto zeni entra no bolso e do servidor (`GameServer.ServicosDoAlem.cs`).
/// =====================================================================================================================================
/// </summary>
public enum DoadorDoAlem : byte
{
	Nenhum = 0,
	Recepcao = 1,
	Princesa = 2,
	SrKaioh = 3,
	OniDoInferno = 4,
}

public enum TipoDeServico : byte
{
	/// <summary>Levar algo do doador a outro doador -- e falar com o destino pelo menu E.</summary>
	Entrega = 1,

	/// <summary>Nocautear N corpos diferentes de um molde de NPC (as almas, as almas condenadas).</summary>
	Nocaute = 2,

	/// <summary>Meditar N segundos (somados) dentro de um lugar.</summary>
	Meditar = 3,

	/// <summary>Treinar N segundos (somados) dentro de um lugar.</summary>
	Treinar = 4,
}

/// <summary>Onde os servicos de tempo contam.</summary>
public enum LugarDoServico : byte
{
	Nenhum = 0,

	/// <summary>O palacio da Princesa Serpente, no laco do Caminho da Serpente.</summary>
	PalacioDaPrincesa = 1,

	/// <summary>O planeta do Sr. Kaioh -- a ilha do norte, a 10x (`Alem.IlhasDoOutroMundo`).</summary>
	PlanetaDoKaioh = 2,
}

/// <summary>Um servico do catalogo.</summary>
public sealed record ServicoDoAlem(
	string Id, string Nome, DoadorDoAlem Doador, TipoDeServico Tipo, DoadorDoAlem Destino, string Molde, LugarDoServico Lugar,
	int Meta, double Recompensa, int MinutosDePrazo, string Pedido);

/// <summary>
/// A FICHA DE SERVICOS DE UM PERSONAGEM. Vai no save dentro do `Fighter` (`servicosDoAlem`), entao e do PERSONAGEM: a vaga reciclada
/// nao herda nada (ver `assinatura-reciclada` nas notas do projeto), e o reencarnado comeca do zero.
/// </summary>
public sealed class FichaDoAlem
{
	/// <summary>O servico em curso (o `Id` do catalogo), ou "" sem nenhum.</summary>
	public string Atual = "";

	/// <summary>Nocautes contados, ou segundos somados.</summary>
	public int Progresso;

	/// <summary>Quando o servico em curso vence (ms Unix).</summary>
	public long PrazoMs;

	/// <summary>Os corpos ja contados neste servico de nocaute -- o mesmo NPC derrubado duas vezes conta uma.</summary>
	public List<int> Derrubados = [];

	/// <summary>Servico -> quando ele volta a ser oferecido (ms Unix). A recarga de um dia do jogo, contada da entrega.</summary>
	public Dictionary<string, long> DeNovoEm = [];

	/// <summary>Os doadores que ja receberam uma entrega deste personagem (a REDE: o Sr. Kaioh e o Oni do Inferno so atendem estes).</summary>
	public List<DoadorDoAlem> Apresentado = [];

	/// <summary>Quantos servicos este personagem ja cumpriu.</summary>
	public int Feitos;
}

public static class ServicosDoAlem
{
	// =====================================================================
	// OS NUMEROS -- a tabela que o dono aprovou
	// =====================================================================

	/// <summary>A recarga de cada servico: um dia do jogo (`Espaco.SegundosPorDiaInGame`, 24 min reais), contado da entrega.</summary>
	public static long MsDeRecarga => (long)(Espaco.SegundosPorDiaInGame * 1000);

	/// <summary>Onde os doadores ficam (celulas do port, y de cima pra baixo). Os tres novos; o Sr. Kaioh e o de `NpcsDeConversa`.</summary>
	public const int RecepcaoX = 191, RecepcaoY = 396;    // cinco celulas a leste da mesa do Enma, onde todo morto chega (`Alem.MesaDoEnma`)
	public const int PrincesaX = 286, PrincesaY = 134;    // no salao do meio do palacio, de frente pra porta de baixo
	public const int OniDoInfernoX = 70, OniDoInfernoY = 205;   // na sala da passagem do Inferno, perto de onde a alma chega

	/// <summary>O palacio da Princesa Serpente, em celulas (inclusive): o salao, as alas e a entrada.</summary>
	public const int PalacioX0 = 262, PalacioY0 = 132, PalacioX1 = 312, PalacioY1 = 149;

	/// <summary>Os tipos de obra dos tres doadores novos (o menu E de cada um mora em `Tech.Interacoes`).</summary>
	public const string TipoDaRecepcao = "Oni_da_Recepcao", TipoDaPrincesa = "Princesa_Serpente", TipoDoOniDoInferno = "Oni_do_Inferno";

	/// <summary>Os dois verbos do menu E de todo doador: pedir servico (uma lista) e entregar.</summary>
	public const string VerboPedir = "alem_pedir", VerboEntregar = "alem_entregar", VerboDesistir = "alem_desistir";

	public static readonly ServicoDoAlem[] Todos =
	[
		new("entrega_kaioh", "Entrega ao Sr. Kaioh", DoadorDoAlem.Recepcao, TipoDeServico.Entrega, DoadorDoAlem.SrKaioh, "",
			LugarDoServico.Nenhum, 1, 40_000, 20,
			"Leve a ficha das almas novas ao Sr. Kaioh, no planeta dele (a ilha do norte, no fim do Caminho da Serpente)."),
		new("convite_princesa", "Convite à Princesa", DoadorDoAlem.Recepcao, TipoDeServico.Entrega, DoadorDoAlem.Princesa, "",
			LugarDoServico.Nenhum, 1, 25_000, 15,
			"Leve o convite do Enma à Princesa Serpente, no palácio dela no meio do Caminho da Serpente."),
		new("almas_briguentas", "Almas briguentas", DoadorDoAlem.Recepcao, TipoDeServico.Nocaute, DoadorDoAlem.Nenhum, "alma",
			LugarDoServico.Nenhum, 3, 30_000, 20,
			"Acalme três almas briguentas: nocauteie três almas diferentes do Outro Mundo."),
		new("recado_inferno", "Recado do Inferno", DoadorDoAlem.Recepcao, TipoDeServico.Entrega, DoadorDoAlem.OniDoInferno, "",
			LugarDoServico.Nenhum, 1, 30_000, 20,
			"Leve a lista dos condenados ao Oni do Inferno (a passagem pro Inferno fica no Outro Mundo)."),
		new("cha_princesa", "Chá da Princesa", DoadorDoAlem.Princesa, TipoDeServico.Meditar, DoadorDoAlem.Nenhum, "",
			LugarDoServico.PalacioDaPrincesa, 180, 20_000, 15,
			"Fique pro chá: medite 3 minutos dentro do palácio da Princesa Serpente."),
		new("treino_kaioh", "Treino na gravidade", DoadorDoAlem.SrKaioh, TipoDeServico.Treinar, DoadorDoAlem.Nenhum, "",
			LugarDoServico.PlanetaDoKaioh, 300, 50_000, 20,
			"Treine 5 minutos no planeta do Sr. Kaioh, na gravidade de 10x."),
		new("fujoes_inferno", "Fujões do Inferno", DoadorDoAlem.OniDoInferno, TipoDeServico.Nocaute, DoadorDoAlem.Nenhum, "alma_condenada",
			LugarDoServico.Nenhum, 5, 60_000, 20,
			"Cinco almas condenadas fugiram da fila: nocauteie cinco diferentes no Inferno."),
	];

	public static ServicoDoAlem? Get(string id) => Array.Find(Todos, s => string.Equals(s.Id, id, StringComparison.Ordinal));

	/// <summary>O servico do catalogo pelo NOME que a lista do menu E mostrou (a escolha volta como argumento do verbo).</summary>
	public static ServicoDoAlem? PeloNome(string nome) =>
		Array.Find(Todos, s => string.Equals(s.Nome, nome.Trim(), StringComparison.OrdinalIgnoreCase));

	public static IEnumerable<ServicoDoAlem> De(DoadorDoAlem doador) => Todos.Where(s => s.Doador == doador);

	public static string NomeDo(DoadorDoAlem d) => d switch
	{
		DoadorDoAlem.Recepcao => "o Oni da recepção",
		DoadorDoAlem.Princesa => "a Princesa Serpente",
		DoadorDoAlem.SrKaioh => "o Sr. Kaioh",
		DoadorDoAlem.OniDoInferno => "o Oni do Inferno",
		_ => "ninguém",
	};

	/// <summary>O doador de um tipo de obra ("" e tipo de outra coisa = `Nenhum`). O Sr. Kaioh e o de `NpcsDeConversa`.</summary>
	public static DoadorDoAlem DoTipo(string tipo) => tipo switch
	{
		TipoDaRecepcao => DoadorDoAlem.Recepcao,
		TipoDaPrincesa => DoadorDoAlem.Princesa,
		NpcsDeConversa.TipoDoSrKaioh => DoadorDoAlem.SrKaioh,
		TipoDoOniDoInferno => DoadorDoAlem.OniDoInferno,
		_ => DoadorDoAlem.Nenhum,
	};

	// =====================================================================
	// OS DEFEITOS INJETADOS (bancada `--servicosteste`) -- falsos em jogo, sempre
	// =====================================================================

	/// <summary>DEFEITO INJETADO: o vivo tambem pega servico -- a regra "so os mortos" sem a linha dela.</summary>
	public static bool VivoPegaDeTeste;

	/// <summary>DEFEITO INJETADO: o Sr. Kaioh e o Oni do Inferno atendem quem nunca lhes fez uma entrega (a rede sem a porta dela).</summary>
	public static bool RedeAbertaDeTeste;

	/// <summary>DEFEITO INJETADO: o servico cumprido volta na hora, sem a recarga de um dia.</summary>
	public static bool SemRecargaDeTeste;

	/// <summary>DEFEITO INJETADO: o mesmo corpo derrubado de novo conta outra vez.</summary>
	public static bool MesmoCorpoContaDeTeste;

	/// <summary>DEFEITO INJETADO: meditar ou treinar FORA do lugar do servico tambem conta.</summary>
	public static bool ForaDoLugarContaDeTeste;

	// =====================================================================
	// A REGRA
	// =====================================================================

	/// <summary>
	/// POR QUE ESTE PERSONAGEM NAO PODE PEGAR ESTE SERVICO AGORA. Nulo = pode. A ordem e a das perguntas que o doador faria: voce esta
	/// morto?, ja tem um servico?, eu te conheco?, esse ja nao foi feito hoje?
	/// </summary>
	public static string? PorQueNaoPega(FichaDoAlem f, ServicoDoAlem s, bool morto, long agoraMs)
	{
		if (PorQueNaoAtende(morto) is { } vivo) return vivo;
		if (f.Atual.Length > 0)
			return $"você já tem um serviço: {Get(f.Atual)?.Nome ?? f.Atual}. Termine (ou desista dele na aba World) antes de pegar outro.";
		if (PrecisaDeApresentacao(s.Doador) && !f.Apresentado.Contains(s.Doador) && !RedeAbertaDeTeste)
			return $"{NomeDo(s.Doador)} ainda não te conhece -- faça uma entrega pra ele primeiro (peça na recepção, ao lado do Enma).";
		if (!SemRecargaDeTeste && f.DeNovoEm.TryGetValue(s.Id, out long volta) && volta > agoraMs)
			return $"\"{s.Nome}\" já foi feito hoje: volta em {MinutosAte(volta, agoraMs)} min.";
		return null;
	}

	/// <summary>O doador atende? So as almas (*"Só os mortos"*, a escolha do dono). Nulo = atende.</summary>
	public static string? PorQueNaoAtende(bool morto) =>
		morto || VivoPegaDeTeste ? null : "só as almas trabalham pro Outro Mundo -- os vivos não têm ficha aqui.";

	/// <summary>Como um servico aparece na rede do cartao (aba World): em curso, de volta em N min, quem ainda nao te conhece, ou disponivel.</summary>
	public static string EstadoNaRede(FichaDoAlem f, ServicoDoAlem s, long agoraMs)
	{
		if (f.Atual == s.Id) return "em curso";
		if (f.DeNovoEm.TryGetValue(s.Id, out long volta) && volta > agoraMs) return $"volta em {MinutosAte(volta, agoraMs)} min";
		if (PrecisaDeApresentacao(s.Doador) && !f.Apresentado.Contains(s.Doador)) return $"{NomeDo(s.Doador)} ainda não te conhece";
		return $"com {NomeDo(s.Doador)} · {s.Recompensa:N0} zeni";
	}

	/// <summary>
	/// OS DOIS BOTOES DO MENU E DE TODO DOADOR: pedir servico (a lista dos dele, `Forma.Lista` -- a escolha volta como argumento) e
	/// entregar. Os tres doadores novos tem so estes; o Sr. Kaioh os ganhou por cima da conversa dele (`Tech.Interacoes`).
	/// </summary>
	public static readonly Tech.Interacoes.Acao[] AcoesDoDoador =
	[
		new(RotuloPedir, VerboPedir, "", "os serviços que ele dá -- um de cada vez, cada um uma vez por dia do jogo", Tech.Interacoes.Forma.Lista),
		new(RotuloEntregar, VerboEntregar, "", "cumpre a entrega que é pra ele"),
	];

	/// <summary>Os rotulos dos dois botoes -- o robo `--diagservicos` os aperta por aqui.</summary>
	public const string RotuloPedir = "Pedir serviço", RotuloEntregar = "Entregar";

	/// <summary>A REDE: os doadores que so atendem quem ja lhes fez uma entrega.</summary>
	public static bool PrecisaDeApresentacao(DoadorDoAlem d) => d is DoadorDoAlem.SrKaioh or DoadorDoAlem.OniDoInferno;

	/// <summary>Pega o servico (a pergunta ja foi feita): zera o progresso e arma o prazo.</summary>
	public static void Pegar(FichaDoAlem f, ServicoDoAlem s, long agoraMs)
	{
		f.Atual = s.Id;
		f.Progresso = 0;
		f.Derrubados.Clear();
		f.PrazoMs = agoraMs + s.MinutosDePrazo * 60_000L;
	}

	/// <summary>Larga o servico em curso, sem recompensa e sem recarga (da pra pegar de novo na hora).</summary>
	public static void Desistir(FichaDoAlem f)
	{
		f.Atual = "";
		f.Progresso = 0;
		f.Derrubados.Clear();
		f.PrazoMs = 0;
	}

	/// <summary>O servico em curso venceu?</summary>
	public static bool Venceu(FichaDoAlem f, long agoraMs) => f.Atual.Length > 0 && agoraMs > f.PrazoMs;

	/// <summary>
	/// UM NOCAUTE: conta se o servico em curso e de nocaute DESTE molde e o corpo ainda nao foi contado. Devolve verdadeiro se contou.
	/// </summary>
	public static bool Nocaute(FichaDoAlem f, string molde, int idDoCorpo)
	{
		if (Get(f.Atual) is not { Tipo: TipoDeServico.Nocaute } s || !string.Equals(s.Molde, molde, StringComparison.Ordinal)) return false;
		if (!MesmoCorpoContaDeTeste)
		{
			if (f.Derrubados.Contains(idDoCorpo)) return false;
			f.Derrubados.Add(idDoCorpo);
		}
		f.Progresso++;
		return true;
	}

	/// <summary>
	/// UM SEGUNDO DE MEDITACAO OU DE TREINO: conta se o servico em curso e desse tipo e o corpo esta no lugar dele. Verdadeiro se contou.
	/// </summary>
	public static bool Segundo(FichaDoAlem f, TipoDeServico fazendo, LugarDoServico ondeEsta)
	{
		if (Get(f.Atual) is not { } s || s.Tipo != fazendo || fazendo is not (TipoDeServico.Meditar or TipoDeServico.Treinar)) return false;
		if (ondeEsta != s.Lugar && !ForaDoLugarContaDeTeste) return false;
		f.Progresso++;
		return true;
	}

	/// <summary>O servico em curso chegou na meta (a entrega se cumpre falando com o destino, ver <see cref="EntregaPara"/>)?</summary>
	public static bool Pronto(FichaDoAlem f) => Get(f.Atual) is { } s && s.Tipo != TipoDeServico.Entrega && f.Progresso >= s.Meta;

	/// <summary>O servico em curso e uma entrega PRA ESTE doador?</summary>
	public static bool EntregaPara(FichaDoAlem f, DoadorDoAlem destino) =>
		Get(f.Atual) is { Tipo: TipoDeServico.Entrega } s && s.Destino == destino;

	/// <summary>
	/// CUMPRE o servico em curso: arma a recarga, conta o feito e, se foi uma entrega, apresenta o destino (a REDE). Devolve a
	/// recompensa em zeni -- quem poe no bolso e o servidor.
	/// </summary>
	public static double Cumprir(FichaDoAlem f, long agoraMs)
	{
		if (Get(f.Atual) is not { } s) return 0;
		f.DeNovoEm[s.Id] = agoraMs + MsDeRecarga;
		f.Feitos++;
		if (s.Tipo == TipoDeServico.Entrega && !f.Apresentado.Contains(s.Destino)) f.Apresentado.Add(s.Destino);
		Desistir(f);
		return s.Recompensa;
	}

	/// <summary>Em que lugar de servico esta celula fica (`Nenhum` fora deles). So o Outro Mundo tem lugar de servico.</summary>
	public static LugarDoServico LugarEm(string zona, int cx, int cy)
	{
		if (!string.Equals(zona, Alem.ZonaDoOutroMundo, StringComparison.OrdinalIgnoreCase)) return LugarDoServico.Nenhum;
		if (cx >= PalacioX0 && cx <= PalacioX1 && cy >= PalacioY0 && cy <= PalacioY1) return LugarDoServico.PalacioDaPrincesa;
		return Alem.IlhaEm(zona, cx, cy) == Alem.IlhasDoOutroMundo[0].Nome ? LugarDoServico.PlanetaDoKaioh : LugarDoServico.Nenhum;
	}

	// =====================================================================
	// TEXTO
	// =====================================================================

	/// <summary>Minutos inteiros (pra cima) entre agora e o instante -- nunca negativo.</summary>
	public static long MinutosAte(long ms, long agoraMs) => Math.Max(0, (ms - agoraMs + 59_999) / 60_000);

	/// <summary>O que falta do servico em curso, em palavras ("2/3 almas", "1:20 de 3:00", "fale com o Sr. Kaioh").</summary>
	public static string Andamento(FichaDoAlem f)
	{
		if (Get(f.Atual) is not { } s) return "";
		return s.Tipo switch
		{
			TipoDeServico.Entrega => $"fale com {NomeDo(s.Destino)} (menu E: Entregar)",
			TipoDeServico.Nocaute => $"{Math.Min(f.Progresso, s.Meta)}/{s.Meta} {(s.Molde == "alma" ? "almas" : "almas condenadas")} nocauteadas",
			_ => $"{Relogio(Math.Min(f.Progresso, s.Meta))} de {Relogio(s.Meta)} {(s.Tipo == TipoDeServico.Meditar ? "meditando" : "treinando")}",
		};
	}

	private static string Relogio(int segundos) => $"{segundos / 60}:{segundos % 60:00}";

	/// <summary>O rotulo de um servico na lista do menu E: o nome, que e o que volta como argumento -- e o pagamento vai no aviso.</summary>
	public static string RotuloNaLista(ServicoDoAlem s) => s.Nome;
}
