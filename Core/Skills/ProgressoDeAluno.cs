namespace Jandirus.Core.Skills;

/// <summary>O que um mestre pode ter passado a um aluno. Os numeros viajam no fio e vao pro disco: nao mudam.</summary>
public enum TipoDeEnsinamento : byte
{
	/// <summary>Uma FORMA que o mestre ajudou (ou tentou ajudar) a despertar -- `Ajudar_A_Transformar`, `MasterStudent.dm:477`.</summary>
	Forma = 1,
	/// <summary>Uma SKILL que o mestre passou -- `Teach_Skill`, `teachable.dm:5`.</summary>
	Licao = 2,
}

/// <summary>
/// EM QUE PE ESTA UM ENSINAMENTO. Sao DUAS conquistas, e a etapa diz qual ja foi paga: RECEBER (a forma
/// despertou, a licao esta no livro) e DOMINAR (maestria cheia, ultimo nivel -- ou nada a dominar).
/// </summary>
public enum EtapaDoEnsinamento : byte
{
	/// <summary>A forma ainda nao despertou; a licao foi esquecida.</summary>
	AindaNao = 0,
	/// <summary>Recebeu, e esta dominando (a maestria da forma, os niveis da skill).</summary>
	EmCurso = 1,
	/// <summary>Dominou: maestria 100%, nivel no teto -- ou uma skill que nao tem niveis.</summary>
	Dominado = 2,
}

/// <summary>
/// O PROGRESSO DE ALGUEM NUM ENSINAMENTO -- a unica coisa que esta tela conta de uma pessoa a outra.
///
/// NAO HA PODER AQUI, e nao pode haver (dono, 2026-10-11: *"o mestre nao pode ver o bp dos alunos ... sem scouter vc
/// n tem como saber seu bp de nenhuma forma"*): maestria de forma e nivel de skill sao medidas do que foi TREINADO,
/// e nenhuma das duas deixa reconstruir o poder de luta de ninguem.
/// </summary>
/// <param name="Nivel">Forma: a maestria em % inteira. Licao: o nivel atual.</param>
/// <param name="Teto">Forma: 100. Licao: o ultimo nivel (0 = a skill nao sobe de nivel).</param>
/// <param name="Milesimos">Quanto do caminho ate dominar ja andou, de 0 a 1000 -- e a barra.</param>
public readonly record struct ProgressoNoEnsinamento(EtapaDoEnsinamento Etapa, ushort Nivel, ushort Teto, ushort Milesimos)
{
	/// <summary>
	/// O PROGRESSO NUM NUMERO SO, de 0 a 1 -- o que ordena os alunos e faz a media geral. Receber vale
	/// <see cref="ProgressoDeAluno.MetadeDeReceber"/>; o resto se ganha dominando.
	/// </summary>
	public double Pontos => Etapa switch
	{
		EtapaDoEnsinamento.AindaNao => 0,
		EtapaDoEnsinamento.Dominado => 1,
		_ => ProgressoDeAluno.MetadeDeReceber + (1 - ProgressoDeAluno.MetadeDeReceber) * Math.Clamp(Milesimos, (ushort)0, (ushort)1000) / 1000.0,
	};
}

/// <summary>Um ensinamento NO DIARIO de um aluno: o que foi, quando, e a ultima FOTOGRAFIA do progresso dele.</summary>
public sealed class Ensinamento
{
	public TipoDeEnsinamento Tipo;

	/// <summary>Forma: o id do catalogo ("ssj1"). Licao: o typepath da skill.</summary>
	public string Id = "";

	/// <summary>Quando o mestre o deu (Unix ms) -- na forma, a PRIMEIRA tentativa.</summary>
	public long EmMs;

	/// <summary>So na forma: quantas vezes o mestre tentou desperta-la (cada tentativa custa a recarga dele).</summary>
	public int Tentativas;

	/// <summary>So na forma: ela despertou COM o mestre (foi ele quem cortou a porta pela metade).</summary>
	public bool Assistida;

	// ---- A FOTOGRAFIA: como o aluno estava da ultima vez em que o servidor o viu no mundo ----
	public byte Etapa;
	public ushort Nivel, Teto, Milesimos;

	[System.Text.Json.Serialization.JsonIgnore]
	public ProgressoNoEnsinamento Progresso => new((EtapaDoEnsinamento)Math.Min(Etapa, (byte)EtapaDoEnsinamento.Dominado), Nivel, Teto, Milesimos);
}

/// <summary>
/// O DIARIO DE UM VINCULO: o que ESTE mestre ensinou a ESTE aluno, e desde quando. Um por aluno (a chave do
/// discipulado e o aluno: um aluno, um mestre); nasce com o vinculo e morre com ele.
/// </summary>
public sealed class DiarioDeAluno
{
	public string Aluno = "", Mestre = "";

	/// <summary>Quando o vinculo nasceu (Unix ms). ZERO = nao se sabe: o vinculo e de antes de o diario existir.</summary>
	public long DesdeMs;

	public List<Ensinamento> Ensinamentos = [];

	public Ensinamento? Achar(TipoDeEnsinamento tipo, string id)
	{
		foreach (Ensinamento e in Ensinamentos)
			if (e.Tipo == tipo && string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)) return e;
		return null;
	}
}

/// <summary>O que o diario lembra de uma PESSOA do discipulado (mestre ou aluno), pra desenha-la com ela fora do mundo.</summary>
public sealed class PessoaDoDiario
{
	public string Nome = "", Raca = "";

	/// <summary>A ultima vez que o servidor a viu no mundo (Unix ms). Zero = nunca, desde que o diario existe.</summary>
	public long VistoEm;
}

/// <summary>O arquivo do mundo (`diario-dos-alunos.json`, ao lado do `mestres.txt`). Campo que faltar num arquivo antigo nasce no valor de fabrica.</summary>
public sealed class LivroDoDiscipulado
{
	public List<DiarioDeAluno> Diarios = [];
	public Dictionary<string, PessoaDoDiario> Pessoas = [];
}

// =====================================================================
// O QUE VIAJA -- o painel de UM jogador, ja cortado pelo que ele pode ver
// =====================================================================

/// <summary>
/// Uma pessoa num cartao do painel.
/// </summary>
/// <param name="Assinatura">A identidade opaca (a mesma da aba People): e por ela que o cartao pede um gesto de volta.</param>
/// <param name="Online">Esta no mundo agora.</param>
/// <param name="VistoHaSegundos">Ha quanto tempo saiu do mundo (0 = esta nele, ou nao se sabe).</param>
/// <param name="DesdeHaSegundos">Ha quanto tempo o vinculo existe (0 = nao se sabe: e de antes do diario).</param>
public readonly record struct PessoaNoPainel(string Assinatura, string Nome, string Raca, bool Online,
											 uint VistoHaSegundos, uint DesdeHaSegundos);

/// <summary>Um ensinamento como alguem o le: o que foi, ha quanto tempo, e o progresso.</summary>
public readonly record struct EnsinamentoNoPainel(TipoDeEnsinamento Tipo, string Id, uint HaSegundos, byte Tentativas,
												  bool Assistida, ProgressoNoEnsinamento Progresso);

/// <summary>VISAO DO MESTRE: um aluno, e tudo o que EU ensinei a ele.</summary>
public sealed record AlunoNoPainel(PessoaNoPainel Quem, IReadOnlyList<EnsinamentoNoPainel> Ensinamentos);

/// <summary>O progresso de um COLEGA num ensinamento que eu tambem recebi. `Colega` e o indice na lista de colegas.</summary>
public readonly record struct ColegaNoEnsinamento(byte Colega, ProgressoNoEnsinamento Progresso);

/// <summary>
/// VISAO DO ALUNO: uma coisa que o mestre ME ensinou, com o MEU progresso -- e os colegas que receberam a mesma,
/// do mais adiantado pro menos. E o unico lugar em que o progresso de outro aluno chega a mim.
/// </summary>
public sealed record EnsinamentoDoMestre(EnsinamentoNoPainel Meu, IReadOnlyList<ColegaNoEnsinamento> Colegas);

/// <summary>
/// VISAO DO ALUNO: o meu mestre, o que ele me ensinou, e os NOMES dos outros alunos dele. Dos colegas so o nome
/// viaja (nem assinatura, nem raca, nem se estao no mundo): o resto deles nao e assunto meu.
/// </summary>
public sealed record MestreNoPainel(PessoaNoPainel Quem, IReadOnlyList<EnsinamentoDoMestre> Ensinamentos,
									IReadOnlyList<string> Colegas);

/// <summary>O PAINEL DE UM JOGADOR: o mestre dele (se tem) e os alunos dele (se guia alguem).</summary>
public sealed record PainelDoDiscipulado(MestreNoPainel? Mestre, IReadOnlyList<AlunoNoPainel> Alunos)
{
	public static readonly PainelDoDiscipulado Vazio = new(null, []);
}

/// <summary>Quem recebeu um ensinamento, no ranking dele. `Aluno` e o indice na lista de alunos do painel.</summary>
public readonly record struct LugarNoRanking(int Aluno, ProgressoNoEnsinamento Progresso, int Posicao);

/// <summary>VISAO DE CONJUNTO DO MESTRE: um ensinamento e quem o recebeu, do mais adiantado pro menos.</summary>
public sealed record RankingDoEnsinamento(TipoDeEnsinamento Tipo, string Id, IReadOnlyList<LugarNoRanking> Lugares);

/// <summary>
/// **O PROGRESSO DOS ALUNOS** -- as regras puras da tela "Mestre e alunos" (dono, 2026-10-11: *"ter tipo uma aba
/// people so q de alunos (caso vc seja o mestre) e mestre (caso vc seja aluno) ao qual vc pode ver seus alunos e o
/// quao fortes eles estao progredindo no q vc ensinou. e o aluno pode ver o mestre e os outros alunos pra ver o
/// quao bem ele ta progredindo em relacao aos outros alunos"*).
///
/// ============================ O QUE UM MESTRE ENSINA, E O QUE SE MEDE DE CADA COISA ============================
/// O vinculo (<see cref="Discipulado"/>) faz tres coisas, e o ensino de skill (<see cref="EnsinoDeSkill"/>) uma quarta:
///
///   * DESPERTAR UMA FORMA com a porta pela metade -- mede-se se ela despertou e a MAESTRIA dela (0 a 100);
///   * PASSAR UMA SKILL -- mede-se se o aluno ainda a tem e o NIVEL dela (as que sobem de nivel; a maioria das
///     que se ensinam, como o Kamehameha, nao tem niveis: aprendeu, esta completo);
///   * o treino junto (ate 3x de ganho) -- nao deixa rastro nenhum: e ganho de poder, e poder nao aparece aqui;
///   * o tempo de vinculo -- uma data.
///
/// O original mostrava menos e mais: a aba dele (`ui_tab_alunos`, `MasterStudent.dm:596`) so tinha o nome,
/// "presente/ausente" e a RAZAO de poder base entre os dois (`mst_gap_txt`, `:571`), com o numero pra quem usasse
/// scouter. DIVERGENCIA DECLARADA, por ordem do dono: esta tela nao conta poder nenhum -- nem razao, nem numero, nem
/// com scouter. A unica leitura de forca que ela desenha e a que o jogador JA TEM pelo sentido de Ki (a aba Sense),
/// e quem a desenha e o cliente, com o que o `GameServer.Sentidos` ja lhe mandou: nada disso passa por aqui.
///
/// ============================ QUEM VE O QUE ============================
///   * o MESTRE ve, de cada aluno, o progresso no que ELE ensinou (o diario e do vinculo: o que o aluno aprendeu
///     com outra pessoa, ou sozinho, nao entra);
///   * o ALUNO ve o proprio progresso no que o mestre lhe ensinou e, de cada colega, SO o progresso nos
///     ensinamentos que os dois receberam -- e o nome. O que o mestre ensinou so ao colega nao chega a ele.
/// Quem decide e o <see cref="Montar"/>, e o que ele devolve e o pacote inteiro: a tela nao recebe nada pra esconder.
/// ================================================================================================================
/// </summary>
public static class ProgressoDeAluno
{
	// =====================================================================
	// OS NUMEROS (ajustaveis)
	// =====================================================================
	/// <summary>
	/// QUANTO VALE TER RECEBIDO um ensinamento (a forma despertou, a licao esta no livro), de 0 a 1. O resto se ganha
	/// DOMINANDO. Com 0,5: quem acabou de despertar o Super Saiyajin esta na metade do caminho dele; com a maestria
	/// em 100%, no fim.
	/// </summary>
	public const double MetadeDeReceber = 0.5;

	/// <summary>O teto de ensinamentos de um diario (o pacote leva um byte de contagem, e o cartao tem que caber na tela).</summary>
	public const int MaxEnsinamentos = 40;

	/// <summary>De quanto em quanto tempo a tela aberta pergunta ao servidor se algo mudou, em segundos.</summary>
	public const double SegundosEntrePedidos = 2;

	/// <summary>De quanto em quanto tempo o servidor poe no disco as fotografias que mudaram, em segundos.</summary>
	public const double SegundosEntreGravacoes = 20;

	/// <summary>Dois progressos tao proximos assim contam como EMPATE (a barra tem mil passos).</summary>
	private const double FolgaDoEmpate = 0.0005;

	// =====================================================================
	// OS DEFEITOS INJETADOS (bancada `--alunosteste`); falsos em jogo, sempre
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada --alunosteste): o aluno recebe TODOS os ensinamentos dos colegas, e nao so os que
	/// tem em comum com eles. Lido pela propria linha do corte, no <see cref="Montar"/>.
	/// </summary>
	public static bool ColegaVeTudoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --alunosteste): as listas saem na ordem em que foram anotadas, e nao do mais
	/// adiantado pro menos. Lido pelas proprias linhas da ordenacao.
	/// </summary>
	public static bool SemOrdemDeTeste;

	// =====================================================================
	// O PROGRESSO DE UMA COISA
	// =====================================================================
	/// <summary>O progresso numa FORMA: despertou? e quanto a domina (a maestria do `EstadoDeForma`, 0 a 100).</summary>
	public static ProgressoNoEnsinamento DaForma(bool despertou, double maestria)
	{
		if (!despertou) return new(EtapaDoEnsinamento.AindaNao, 0, 100, 0);
		double m = double.IsFinite(maestria) ? Math.Clamp(maestria, 0, 100) : 0;
		if (m >= 100) return new(EtapaDoEnsinamento.Dominado, 100, 100, 1000);
		return new(EtapaDoEnsinamento.EmCurso, (ushort)Math.Floor(m), 100, (ushort)Math.Min(999, Math.Floor(m * 10)));
	}

	/// <summary>
	/// O progresso numa LICAO: ainda a tem? e em que nivel esta. <paramref name="teto"/> zero = a skill nao sobe de
	/// nivel (nao ha regra de nivel pra ela): quem a tem ja a tem inteira. <paramref name="exp"/> e
	/// <paramref name="barreira"/> sao o andamento DENTRO do nivel (`NiveisDeSkill.Andamento`).
	/// </summary>
	public static ProgressoNoEnsinamento DaLicao(bool sabe, int teto, int nivel, double exp, double barreira)
	{
		ushort t = (ushort)Math.Clamp(teto, 0, ushort.MaxValue);
		if (!sabe) return new(EtapaDoEnsinamento.AindaNao, 0, t, 0);
		if (t == 0) return new(EtapaDoEnsinamento.Dominado, 0, 0, 1000);
		int n = Math.Clamp(nivel, 0, t);
		if (n >= t) return new(EtapaDoEnsinamento.Dominado, t, t, 1000);
		double dentro = barreira > 0 && double.IsFinite(exp) ? Math.Clamp(exp / barreira, 0, 0.999) : 0;
		return new(EtapaDoEnsinamento.EmCurso, (ushort)n, t, (ushort)Math.Min(999, Math.Floor((n + dentro) / t * 1000)));
	}

	/// <summary>
	/// O PROGRESSO GERAL de alguem num conjunto de ensinamentos: a SOMA dos pontos (ordena -- quem recebeu e dominou
	/// mais vem na frente) e a MEDIA (a porcentagem que a tela escreve). Sem ensinamento nenhum, zero e zero.
	/// </summary>
	public static (double Soma, double Media) Geral(IEnumerable<ProgressoNoEnsinamento> progressos)
	{
		double soma = 0;
		int n = 0;
		foreach (ProgressoNoEnsinamento p in progressos) { soma += p.Pontos; n++; }
		return (soma, n == 0 ? 0 : soma / n);
	}

	// =====================================================================
	// A ORDEM
	// =====================================================================
	/// <summary>
	/// A POSICAO DE CADA UM, do mais adiantado (1) pro menos, com EMPATE dividindo o lugar: 1, 1, 3. Quem empata
	/// nao passa na frente de ninguem por sorteio de ordem.
	/// </summary>
	public static int[] Posicoes(IReadOnlyList<double> pontos)
	{
		var pos = new int[pontos.Count];
		for (int i = 0; i < pontos.Count; i++)
		{
			int naFrente = 0;
			for (int j = 0; j < pontos.Count; j++)
				if (pontos[j] > pontos[i] + FolgaDoEmpate) naFrente++;
			pos[i] = naFrente + 1;
		}
		return pos;
	}

	/// <summary>A minha posicao num ensinamento, contra os colegas que tambem o receberam, e se ha alguem empatado comigo.</summary>
	public static (int Posicao, int De, bool Empate) Posicao(ProgressoNoEnsinamento meu, IReadOnlyList<ColegaNoEnsinamento> colegas)
	{
		int naFrente = 0;
		bool empate = false;
		foreach (ColegaNoEnsinamento c in colegas)
		{
			if (c.Progresso.Pontos > meu.Pontos + FolgaDoEmpate) naFrente++;
			else if (Math.Abs(c.Progresso.Pontos - meu.Pontos) <= FolgaDoEmpate) empate = true;
		}
		return (naFrente + 1, colegas.Count + 1, empate);
	}

	/// <summary>
	/// A VISAO DE CONJUNTO DO MESTRE: cada coisa que ele ensinou (na ordem em que aparece nos cartoes) e quem a
	/// recebeu, do mais adiantado pro menos, com a posicao. E a resposta de "quem esta mais adiantado em cada
	/// ensinamento" -- derivada do proprio painel, pelas duas pontas com a mesma conta.
	/// </summary>
	public static List<RankingDoEnsinamento> Conjunto(IReadOnlyList<AlunoNoPainel> alunos)
	{
		var chaves = new List<(TipoDeEnsinamento Tipo, string Id)>();
		foreach (AlunoNoPainel a in alunos)
			foreach (EnsinamentoNoPainel e in a.Ensinamentos)
				if (!chaves.Exists(k => k.Tipo == e.Tipo && string.Equals(k.Id, e.Id, StringComparison.OrdinalIgnoreCase)))
					chaves.Add((e.Tipo, e.Id));

		var conjunto = new List<RankingDoEnsinamento>(chaves.Count);
		foreach ((TipoDeEnsinamento tipo, string id) in chaves)
		{
			var quem = new List<(int Aluno, ProgressoNoEnsinamento P)>();
			for (int i = 0; i < alunos.Count; i++)
				foreach (EnsinamentoNoPainel e in alunos[i].Ensinamentos)
					if (e.Tipo == tipo && string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)) { quem.Add((i, e.Progresso)); break; }

			// do mais adiantado pro menos; no empate, a ordem dos cartoes (que ja e deterministica)
			if (!SemOrdemDeTeste)
				quem.Sort((a, b) => Math.Abs(a.P.Pontos - b.P.Pontos) > FolgaDoEmpate ? b.P.Pontos.CompareTo(a.P.Pontos) : a.Aluno.CompareTo(b.Aluno));
			int[] pos = Posicoes([.. quem.Select(q => q.P.Pontos)]);
			var lugares = new List<LugarNoRanking>(quem.Count);
			for (int i = 0; i < quem.Count; i++) lugares.Add(new(quem[i].Aluno, quem[i].P, pos[i]));
			conjunto.Add(new RankingDoEnsinamento(tipo, id, lugares));
		}
		return conjunto;
	}

	// =====================================================================
	// O DIARIO
	// =====================================================================
	/// <summary>
	/// O MESTRE TENTOU despertar esta forma no aluno (a oferta saiu e a recarga dele comecou a correr). A primeira
	/// tentativa poe a forma no diario; as seguintes so contam. Devolve o ensinamento, ou nulo com o diario cheio.
	/// </summary>
	public static Ensinamento? AnotarTentativa(DiarioDeAluno d, string idDaForma, long agoraMs)
	{
		Ensinamento? e = d.Achar(TipoDeEnsinamento.Forma, idDaForma);
		if (e == null)
		{
			if (d.Ensinamentos.Count >= MaxEnsinamentos) return null;
			e = new Ensinamento { Tipo = TipoDeEnsinamento.Forma, Id = idDaForma, EmMs = agoraMs, Teto = 100 };
			d.Ensinamentos.Add(e);
		}
		e.Tentativas = Math.Min(e.Tentativas + 1, byte.MaxValue);
		return e;
	}

	/// <summary>
	/// A FORMA DESPERTOU COM O MESTRE. Vale sozinha tambem (um diario que nao viu a tentativa -- a oferta saiu antes
	/// de ele existir): a forma entra, com uma tentativa.
	/// </summary>
	public static Ensinamento? AnotarDespertar(DiarioDeAluno d, string idDaForma, long agoraMs)
	{
		Ensinamento? e = d.Achar(TipoDeEnsinamento.Forma, idDaForma) ?? AnotarTentativa(d, idDaForma, agoraMs);
		if (e != null) e.Assistida = true;
		return e;
	}

	/// <summary>O MESTRE PASSOU UMA SKILL. Repetir a licao (o aluno a esqueceu e a recebeu de novo) nao cria segunda linha.</summary>
	public static Ensinamento? AnotarLicao(DiarioDeAluno d, string typepath, long agoraMs)
	{
		Ensinamento? e = d.Achar(TipoDeEnsinamento.Licao, typepath);
		if (e != null) return e;
		if (d.Ensinamentos.Count >= MaxEnsinamentos) return null;
		e = new Ensinamento { Tipo = TipoDeEnsinamento.Licao, Id = typepath, EmMs = agoraMs };
		d.Ensinamentos.Add(e);
		return e;
	}

	/// <summary>Guarda a fotografia do progresso num ensinamento. Devolve TRUE se ela mudou (e o disco esta atrasado).</summary>
	public static bool Fotografar(Ensinamento e, ProgressoNoEnsinamento p)
	{
		if (e.Etapa == (byte)p.Etapa && e.Nivel == p.Nivel && e.Teto == p.Teto && e.Milesimos == p.Milesimos) return false;
		e.Etapa = (byte)p.Etapa;
		e.Nivel = p.Nivel;
		e.Teto = p.Teto;
		e.Milesimos = p.Milesimos;
		return true;
	}

	// =====================================================================
	// O PAINEL -- quem ve o que
	// =====================================================================
	/// <summary>Ha quantos segundos foi este instante (no minimo 1). Zero quer dizer "nao sei": o carimbo nunca foi escrito.</summary>
	public static uint Ha(long quandoMs, long agoraMs) =>
		quandoMs <= 0 ? 0u : (uint)Math.Clamp((agoraMs - quandoMs) / 1000, 1, uint.MaxValue);

	private static EnsinamentoNoPainel Visto(Ensinamento e, long agoraMs) =>
		new(e.Tipo, e.Id, Ha(e.EmMs, agoraMs), (byte)Math.Clamp(e.Tentativas, 0, byte.MaxValue), e.Assistida, e.Progresso);

	/// <summary>
	/// **MONTA O PAINEL DE UM JOGADOR**, ja cortado pelo que ele pode ver. Ver "QUEM VE O QUE" no cabecalho da classe.
	/// </summary>
	/// <param name="meu">O diario do MEU vinculo como aluno (nulo = nao tenho mestre).</param>
	/// <param name="dosColegas">Os diarios dos OUTROS alunos do meu mestre.</param>
	/// <param name="dosMeusAlunos">Os diarios dos alunos que EU guio.</param>
	/// <param name="pessoa">Nome, raca e presenca de uma assinatura (o servidor responde com o corpo vivo, ou com o que o diario lembra).</param>
	public static PainelDoDiscipulado Montar(DiarioDeAluno? meu, IReadOnlyList<DiarioDeAluno> dosColegas,
											 IReadOnlyList<DiarioDeAluno> dosMeusAlunos,
											 Func<string, PessoaNoPainel> pessoa, long agoraMs)
	{
		MestreNoPainel? mestre = null;
		if (meu != null)
		{
			// OS COLEGAS, por nome: e a ordem dos cartoes deles, e a que desempata o ranking.
			List<(DiarioDeAluno Diario, string Nome)> colegas = [.. dosColegas.Select(d => (d, pessoa(d.Aluno).Nome))];
			if (!SemOrdemDeTeste)
				colegas.Sort((a, b) =>
				{
					int porNome = string.Compare(a.Nome, b.Nome, StringComparison.OrdinalIgnoreCase);
					return porNome != 0 ? porNome : string.CompareOrdinal(a.Diario.Aluno, b.Diario.Aluno);
				});
			if (colegas.Count > byte.MaxValue) colegas.RemoveRange(byte.MaxValue, colegas.Count - byte.MaxValue);

			var ensinamentos = new List<EnsinamentoDoMestre>(meu.Ensinamentos.Count);
			foreach (Ensinamento e in NaOrdemEmQueVieram(meu.Ensinamentos))
				ensinamentos.Add(new EnsinamentoDoMestre(Visto(e, agoraMs), DosColegas(colegas, e.Tipo, e.Id)));

			// O CORTE: do que o mestre ensinou SO a um colega, nada entra. (O defeito injetado poe de volta.)
			if (ColegaVeTudoDeTeste)
				foreach ((DiarioDeAluno diario, _) in colegas)
					foreach (Ensinamento dele in NaOrdemEmQueVieram(diario.Ensinamentos))
						if (meu.Achar(dele.Tipo, dele.Id) == null
							&& !ensinamentos.Exists(x => x.Meu.Tipo == dele.Tipo && string.Equals(x.Meu.Id, dele.Id, StringComparison.OrdinalIgnoreCase)))
							ensinamentos.Add(new EnsinamentoDoMestre(
								new EnsinamentoNoPainel(dele.Tipo, dele.Id, 0, 0, false, new(EtapaDoEnsinamento.AindaNao, 0, dele.Teto, 0)),
								DosColegas(colegas, dele.Tipo, dele.Id)));

			mestre = new MestreNoPainel(pessoa(meu.Mestre) with { DesdeHaSegundos = Ha(meu.DesdeMs, agoraMs) },
										ensinamentos, [.. colegas.Select(c => c.Nome)]);
		}

		var alunos = new List<(AlunoNoPainel Aluno, double Soma)>(dosMeusAlunos.Count);
		foreach (DiarioDeAluno d in dosMeusAlunos)
		{
			List<EnsinamentoNoPainel> lista = [.. NaOrdemEmQueVieram(d.Ensinamentos).Select(e => Visto(e, agoraMs))];
			alunos.Add((new AlunoNoPainel(pessoa(d.Aluno) with { DesdeHaSegundos = Ha(d.DesdeMs, agoraMs) }, lista),
						Geral(lista.Select(e => e.Progresso)).Soma));
		}

		// OS CARTOES DOS ALUNOS, do mais adiantado pro menos; no empate, por nome (e a assinatura desempata homonimos).
		if (!SemOrdemDeTeste)
			alunos.Sort((a, b) =>
			{
				if (Math.Abs(a.Soma - b.Soma) > FolgaDoEmpate) return b.Soma.CompareTo(a.Soma);
				int porNome = string.Compare(a.Aluno.Quem.Nome, b.Aluno.Quem.Nome, StringComparison.OrdinalIgnoreCase);
				return porNome != 0 ? porNome : string.CompareOrdinal(a.Aluno.Quem.Assinatura, b.Aluno.Quem.Assinatura);
			});
		if (alunos.Count > byte.MaxValue) alunos.RemoveRange(byte.MaxValue, alunos.Count - byte.MaxValue);

		return new PainelDoDiscipulado(mestre, [.. alunos.Select(a => a.Aluno)]);
	}

	/// <summary>Os ensinamentos de um diario na ordem em que o mestre os deu (o mais antigo primeiro; a lista ja nasce assim).</summary>
	private static IEnumerable<Ensinamento> NaOrdemEmQueVieram(List<Ensinamento> lista) =>
		lista.Take(MaxEnsinamentos);

	/// <summary>Os colegas que receberam ESTE ensinamento, do mais adiantado pro menos (no empate, a ordem dos cartoes).</summary>
	private static List<ColegaNoEnsinamento> DosColegas(List<(DiarioDeAluno Diario, string Nome)> colegas, TipoDeEnsinamento tipo, string id)
	{
		var deles = new List<ColegaNoEnsinamento>();
		for (int i = 0; i < colegas.Count; i++)
			if (colegas[i].Diario.Achar(tipo, id) is { } dele) deles.Add(new ColegaNoEnsinamento((byte)i, dele.Progresso));
		if (!SemOrdemDeTeste)
			deles.Sort((a, b) => Math.Abs(a.Progresso.Pontos - b.Progresso.Pontos) > FolgaDoEmpate
				? b.Progresso.Pontos.CompareTo(a.Progresso.Pontos)
				: a.Colega.CompareTo(b.Colega));
		return deles;
	}

	/// <summary>
	/// O CONTEUDO DE UM PAINEL SEM OS RELOGIOS, numa linha: o que o servidor compara pra decidir se ha o que mandar de
	/// novo a uma tela aberta. Os "ha quanto tempo" ficam de fora -- eles andam sozinhos, e quem os faz andar na tela
	/// e o cliente (somando o tempo desde que o pacote chegou).
	/// </summary>
	public static string Conteudo(PainelDoDiscipulado p)
	{
		var sb = new System.Text.StringBuilder();
		static void Pessoa(System.Text.StringBuilder s, PessoaNoPainel q) =>
			s.Append(q.Assinatura).Append('/').Append(q.Nome).Append('/').Append(q.Raca).Append('/').Append(q.Online ? '1' : '0')
			 .Append('/').Append(q.DesdeHaSegundos == 0 ? '-' : 'd').Append('/').Append(q.VistoHaSegundos == 0 ? '-' : 'v');
		static void Ens(System.Text.StringBuilder s, EnsinamentoNoPainel e) =>
			s.Append((byte)e.Tipo).Append(':').Append(e.Id).Append(':').Append(e.Tentativas).Append(e.Assistida ? 'a' : '-');
		static void Prog(System.Text.StringBuilder s, ProgressoNoEnsinamento g) =>
			s.Append('=').Append((byte)g.Etapa).Append(',').Append(g.Nivel).Append(',').Append(g.Teto).Append(',').Append(g.Milesimos);

		if (p.Mestre is { } m)
		{
			sb.Append("M|");
			Pessoa(sb, m.Quem);
			foreach (string c in m.Colegas) sb.Append("|c:").Append(c);
			foreach (EnsinamentoDoMestre e in m.Ensinamentos)
			{
				sb.Append('|');
				Ens(sb, e.Meu);
				Prog(sb, e.Meu.Progresso);
				foreach (ColegaNoEnsinamento c in e.Colegas) { sb.Append('+').Append(c.Colega); Prog(sb, c.Progresso); }
			}
		}
		foreach (AlunoNoPainel a in p.Alunos)
		{
			sb.Append("||A|");
			Pessoa(sb, a.Quem);
			foreach (EnsinamentoNoPainel e in a.Ensinamentos) { sb.Append('|'); Ens(sb, e); Prog(sb, e.Progresso); }
		}
		return sb.ToString();
	}
}
