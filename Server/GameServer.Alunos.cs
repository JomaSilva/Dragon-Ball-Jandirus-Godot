using System.Text.Json;
using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Skills;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// **A TELA "MESTRE E ALUNOS", LADO DO SERVIDOR** -- o diario de cada vinculo e o painel que cada jogador recebe.
///
/// O dono (2026-10-11): *"ter tipo uma aba people so q de alunos (caso vc seja o mestre) e mestre (caso vc seja
/// aluno) ao qual vc pode ver seus alunos e o quao fortes eles estao progredindo no q vc ensinou. e o aluno pode ver
/// o mestre e os outros alunos pra ver o quao bem ele ta progredindo em relacao aos outros alunos"*.
///
/// As regras (o que e progresso, a ordem, QUEM VE O QUE) moram no <see cref="ProgressoDeAluno"/>, sem rede nem Godot.
/// Aqui mora o que e do servidor: o que ele GUARDA de cada vinculo, quando fotografa, a quem manda e a quem recusa.
///
/// ============================ O QUE O SERVIDOR GUARDAVA, E O QUE PASSOU A GUARDAR ============================
/// Do vinculo existia so o par (`mestres.txt`: aluno -> mestre, e os nomes). Do que o mestre ENSINA sobravam dois
/// rastros sem autor -- `EstadoDeForma.PortasCortadas` (um mestre cortou esta porta: qual?) e o `wastaught` do
/// livro (alguem ensinou esta skill: quem?) -- e nenhuma data. O DIARIO (`diario-dos-alunos.json`, ao lado do `mestres.txt`)
/// e o que faltava: por vinculo, desde quando ele existe e cada coisa que ESTE mestre passou a ESTE aluno. Quatro
/// portas escrevem nele, cada uma no instante em que o gesto acontece:
///
///   * <see cref="AbrirDiarioDoAluno"/>      -- o vinculo nasceu (`Vincular`);
///   * <see cref="AnotarTentativaDeForma"/>   -- o mestre tentou despertar uma forma (`OferecerDespertar`);
///   * <see cref="AnotarDespertarAssistido"/> -- ela despertou com ele (`DespertarAssistido`);
///   * <see cref="AnotarLicaoDoMestre"/>      -- ele passou uma skill (`ResponderALicao`).
///
/// O QUE FICA DE FORA, e por que: o que o aluno despertou ou aprendeu ANTES do vinculo, sozinho ou com outra
/// pessoa (nao e "o que voce ensinou"); e tudo o que aconteceu antes de o diario existir (os dois rastros de cima
/// nao dizem quem foi). Vinculo antigo ganha um diario vazio e sem data na primeira consulta.
///
/// ============================ A FOTOGRAFIA ============================
/// O progresso de um aluno e lido do corpo VIVO dele (a maestria da forma, o nivel da skill) e guardado no diario
/// a cada segundo em que ele esta no mundo -- e e a fotografia que o mestre e os colegas leem quando ele nao esta
/// (o molde e a ficha do conhecido da aba People). Ela vai pro disco quando muda, no maximo a cada
/// <see cref="ProgressoDeAluno.SegundosEntreGravacoes"/> s.
///
/// ============================ O SIGILO ============================
/// NAO HA PODER NO PAINEL (dono, 2026-10-11: *"o mestre nao pode ver o bp dos alunos, pq lembre da regra principal
/// do bp: Nao e possivel saber o proprio bp ou de ninguem sem um medidor como um scouter ... oq vc sabe e se alguem
/// e mais forte ou mais fraco q vc em porcentagem"*). O original dava a razao de poder base a qualquer mestre ou
/// aluno presente, e o numero com scouter (`mst_gap_txt`, `MasterStudent.dm:571-578`): DIVERGENCIA DECLARADA. O
/// "mais forte / mais fraco em porcentagem" continua tendo UM caminho so no jogo -- o sentido de Ki
/// (`GameServer.Sentidos.SentidosDe`, o `S2C.Sentidos` que so sai pra quem tem a skill e so de quem esta ao alcance
/// dela) --, e a tela de alunos o desenha a partir do que o cliente ja recebeu por la: este arquivo nao escreve
/// forca de ninguem em pacote nenhum.
///
/// ============================ QUEM RECEBE ============================
/// So quem TEM o vinculo, e so o painel DELE: o mestre recebe os alunos dele; o aluno recebe o mestre e, dos
/// colegas, o nome e o progresso nos ensinamentos em comum. Quem nao tem mestre nem aluno ouve a recusa e nao
/// recebe pacote. O pedido e o verb `alunos` (ao abrir a tela) e `alunos seguir` (com ela aberta: so responde se o
/// conteudo mudou).
/// ================================================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// O ESTADO
	// =====================================================================
	/// <summary>assinatura do ALUNO -> o diario do vinculo dele. A mesma chave do <see cref="_mestreDe"/>.</summary>
	private readonly Dictionary<string, DiarioDeAluno> _diarios = new(StringComparer.Ordinal);

	/// <summary>assinatura -> o que o diario lembra da pessoa (nome, raca, quando foi vista): mestres e alunos.</summary>
	private readonly Dictionary<string, PessoaDoDiario> _pessoasDoDiario = new(StringComparer.Ordinal);

	private bool _diariosCarregados, _diariosSujos;
	private long _proximaGravacaoDosDiarios, _proximoCarimboDeVisto;

	/// <summary>Quem do discipulado estava no mundo no tique anterior (pra perceber quem saiu) e as duas listas de trabalho do tique.</summary>
	private HashSet<string> _doDiscipuladoNoMundo = new(StringComparer.Ordinal), _noMundoAgora = new(StringComparer.Ordinal);
	private readonly HashSet<string> _mestresDoTique = new(StringComparer.Ordinal);

	/// <summary>
	/// O conteudo do ultimo painel que cada TELA recebeu (sem os relogios -- ver <see cref="ProgressoDeAluno.Conteudo"/>).
	/// A tela vai junto porque id se reusa, como nos fatos.
	/// </summary>
	private readonly Dictionary<int, (NetPeer? Tela, string Conteudo)> _paineisMandados = [];

	/// <summary>Os paineis que sairam (pra quem, e os bytes). So a bancada liga. Ver <see cref="MandarPainelDoDiscipulado"/>.</summary>
	internal static List<(int Para, byte[] Pacote)>? EscutaDoDiscipulado;

	/// <summary>A frase com que o servidor recusa o painel a quem nao tem vinculo. (A tabela de requisitos cobra um pedaco dela.)</summary>
	internal const string SemDiscipulado = "você não tem mestre nem alunos: não há discipulado pra mostrar.";

	/// <summary>A frase com que ele recusa um gesto de cartao sobre quem nao e aluno de quem pede.</summary>
	internal const string SoPraAlunoSeu = "isso só vale pra um aluno seu.";

	// =====================================================================
	// OS DEFEITOS INJETADOS (bancada `--alunosteste`); falsos em jogo, sempre
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada --alunosteste): o painel sai pra QUALQUER um que peca, com vinculo ou sem. Lido pela
	/// propria linha do portao, no <see cref="PedidoDoPainel"/>.
	/// </summary>
	internal bool PainelSemVinculoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --alunosteste): a fotografia do progresso nao e guardada -- quem sai do mundo fica
	/// com o progresso do dia em que o ensinamento foi anotado. Lido pela propria linha da foto.
	/// </summary>
	internal bool FotoDoAlunoNaoSeGuardaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --alunosteste): o poder de luta de quem aparece no painel viaja nele -- escrito no
	/// campo da raca e, cru, no fim do pacote. Lido pelas duas linhas que montam e fecham o pacote.
	/// </summary>
	internal bool PoderNoPainelDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --alunosteste): o "Ajudar a transformar" de um cartao nao confere quem esta na
	/// frente -- provoca o aluno que estiver ali, seja ou nao o do cartao. Lido pela propria linha da mira.
	/// </summary>
	internal bool CartaoSemMiraDeTeste;

	// =====================================================================
	// O ARQUIVO
	// =====================================================================
	/// <summary>
	/// O NOME DO ARQUIVO DO DIARIO. **COM HIFEN, de proposito**: ele mora na MESMA pasta das contas, e o arquivo de uma
	/// conta e o nome dela saneado + ".json" -- o saneamento troca tudo o que nao e letra nem digito por '_', entao um
	/// nome de conta nunca produz um hifen (`CharacterStore.NomeReservado`, a licao do `planetas-mortos.json`). Com um
	/// nome liso ("alunos.json"), uma conta chamada "alunos" que ja existisse seria LIDA como diario e REGRAVADA por
	/// cima. Ele tambem esta inscrito no registro da limpeza total (`GameServer.Limpeza.cs`, no inscrito dos vinculos):
	/// e la que um arquivo do mundo se declara, pra reserva do nome e pra bancada `--wipeteste`.
	/// </summary>
	internal const string NomeDoArquivoDosDiarios = "diario-dos-alunos.json";

	/// <summary>Ao lado do `mestres.txt`, pelo mesmo `_store` -- ver o porque em `GameServer.Ranks.cs`.</summary>
	private string ArquivoDosDiarios =>
		System.IO.Path.Combine(_store?.Pasta ?? ProjectSettings.GlobalizePath("user://saves"), NomeDoArquivoDosDiarios);

	private static readonly JsonSerializerOptions OpcoesDoDiario = new() { IncludeFields = true, WriteIndented = true };

	/// <summary>O diario e lido UMA vez, na primeira vez em que alguem precisa dele.</summary>
	private void GarantirDiarios()
	{
		if (_diariosCarregados) return;
		_diariosCarregados = true;
		CarregarDiarios();
	}

	/// <summary>
	/// LE O ARQUIVO DO DIARIO. Arquivo que nao existe, que esta ilegivel ou que nao tem um campo (de uma versao mais
	/// velha) nao derruba nada: o que falta nasce vazio, e um vinculo sem diario ganha um na primeira consulta.
	/// </summary>
	private void CarregarDiarios()
	{
		_diarios.Clear();
		_pessoasDoDiario.Clear();
		try
		{
			if (!System.IO.File.Exists(ArquivoDosDiarios)) return;
			LivroDoDiscipulado? livro = JsonSerializer.Deserialize<LivroDoDiscipulado>(
				System.IO.File.ReadAllText(ArquivoDosDiarios), OpcoesDoDiario);
			if (livro == null) return;

			// (o arquivo e de fora: uma lista nula, uma entrada nula ou um texto nulo nao podem derrubar a carga)
			if (livro.Diarios != null)
				foreach (DiarioDeAluno? d in livro.Diarios)
				{
					if (d == null || string.IsNullOrEmpty(d.Aluno) || string.IsNullOrEmpty(d.Mestre)) continue;
					var ensinamentos = new List<Ensinamento>();
					if (d.Ensinamentos != null)
						foreach (Ensinamento? e in d.Ensinamentos)
							if (e != null && !string.IsNullOrEmpty(e.Id) && e.Tipo is TipoDeEnsinamento.Forma or TipoDeEnsinamento.Licao)
								ensinamentos.Add(e);
					d.Ensinamentos = ensinamentos;
					_diarios[d.Aluno] = d;
				}
			if (livro.Pessoas != null)
				foreach (KeyValuePair<string, PessoaDoDiario> par in livro.Pessoas)
				{
					if (string.IsNullOrEmpty(par.Key) || par.Value is not { } p) continue;
					p.Nome ??= "";
					p.Raca ??= "";
					_pessoasDoDiario[par.Key] = p;
				}
			GD.Print($"[server] discipulado: {_diarios.Count} diario(s) de aluno, {_diarios.Values.Sum(d => d.Ensinamentos.Count)} ensinamento(s)");
		}
		catch (Exception e)
		{
			_diarios.Clear();
			_pessoasDoDiario.Clear();
			GD.PushWarning($"[server] {NomeDoArquivoDosDiarios} ilegivel ({e.Message}): o diario do discipulado recomeca vazio");
		}
	}

	/// <summary>
	/// GRAVA O DIARIO. Le antes, se ainda nao leu (o diario se carrega na primeira consulta: gravar sem ter lido
	/// escreveria um arquivo VAZIO por cima do que ha no disco), e nao grava nada com a limpeza total em curso (o
	/// portao de todo escritor do mundo: `_limpezaEmCurso`).
	/// </summary>
	private void SalvarDiarios()
	{
		if (_limpezaEmCurso) return;
		GarantirDiarios();
		_diariosSujos = false;
		_proximaGravacaoDosDiarios = NowMs() + (long)(ProgressoDeAluno.SegundosEntreGravacoes * 1000);
		try
		{
			var livro = new LivroDoDiscipulado
			{
				Diarios = [.. _diarios.Values.OrderBy(d => d.Aluno, StringComparer.Ordinal)],
				Pessoas = new Dictionary<string, PessoaDoDiario>(_pessoasDoDiario, StringComparer.Ordinal),
			};
			string tmp = ArquivoDosDiarios + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(livro, OpcoesDoDiario));
			System.IO.File.Move(tmp, ArquivoDosDiarios, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao gravei o diario do discipulado: {e.Message}"); }
	}

	/// <summary>
	/// A LIMPEZA TOTAL DO SERVIDOR (o `Zerar` do inscrito dos vinculos, em `GameServer.Limpeza.cs`): o diario volta ao
	/// zero NA MEMORIA, e so nela -- quem apaga o disco e a vassoura, depois (a regra de todo `Zerar`). Sem isto o
	/// diario de vinculos que nao existem mais voltaria pro disco na primeira gravacao do mundo "novo". A proxima
	/// consulta rele a pasta (que entao esta vazia -- ou, na bancada, e outra pasta).
	/// </summary>
	private void ZerarDiarios()
	{
		_diarios.Clear();
		_pessoasDoDiario.Clear();
		_paineisMandados.Clear();
		_doDiscipuladoNoMundo.Clear();
		_diariosSujos = false;
		_diariosCarregados = false;
		_proximaGravacaoDosDiarios = _proximoCarimboDeVisto = 0;
	}

	// =====================================================================
	// O DIARIO DE UM VINCULO
	// =====================================================================
	/// <summary>Este corpo tem mestre, ou guia alguem? E a pergunta do portao do painel.</summary>
	private bool TemDiscipulado(ServerPlayer pl)
	{
		if (!EhPessoa(pl)) return false;
		string sig = pl.Assinatura;
		return MestreDe(sig).Length > 0 || ContarAlunos(sig) > 0;
	}

	/// <summary>
	/// O DIARIO DO VINCULO deste aluno (nulo = ele nao tem mestre). Vinculo sem diario -- de antes de o diario
	/// existir, ou feito por fora do <see cref="AbrirDiarioDoAluno"/> -- ganha um agora, VAZIO e SEM DATA: nao se
	/// inventa "aluno desde hoje" pra quem e aluno ha um mes. E diario de OUTRO mestre nao serve: e trocado.
	/// </summary>
	private DiarioDeAluno? DiarioDoVinculo(string sigAluno)
	{
		string sigMestre = MestreDe(sigAluno);
		if (sigMestre.Length == 0) return null;
		GarantirDiarios();
		if (_diarios.TryGetValue(sigAluno, out DiarioDeAluno? d) && d.Mestre == sigMestre) return d;

		d = new DiarioDeAluno { Aluno = sigAluno, Mestre = sigMestre };
		_diarios[sigAluno] = d;
		_diariosSujos = true;
		return d;
	}

	/// <summary>O diario do par, se `mestre` e MESMO o mestre de `aluno` agora. O que outra pessoa ensina nao entra em diario nenhum.</summary>
	private DiarioDeAluno? DiarioDoPar(ServerPlayer mestre, ServerPlayer aluno) =>
		EhPessoa(mestre) && EhPessoa(aluno) && MestreDe(aluno.Assinatura) == mestre.Assinatura
			? DiarioDoVinculo(aluno.Assinatura)
			: null;

	/// <summary>
	/// O VINCULO NASCEU (`Vincular`): diario novo, com a data de hoje. Sobrescreve o que houvesse nesta chave -- um
	/// vinculo desfeito e refeito recomeca do zero, e uma assinatura reciclada nao herda o diario do personagem
	/// anterior.
	/// </summary>
	private void AbrirDiarioDoAluno(ServerPlayer mestre, ServerPlayer aluno)
	{
		GarantirDiarios();
		long agora = NowMs();
		_diarios[aluno.Assinatura] = new DiarioDeAluno { Aluno = aluno.Assinatura, Mestre = mestre.Assinatura, DesdeMs = agora };
		LembrarPessoa(mestre, mestre.Assinatura, agora);
		LembrarPessoa(aluno, aluno.Assinatura, agora);
		SalvarDiarios();
	}

	/// <summary>
	/// O MESTRE TENTOU DESPERTAR UMA FORMA NESTE ALUNO (`OferecerDespertar`, no ponto em que a recarga dele comeca a
	/// correr -- a tentativa e o gesto, como no DM, `MasterStudent.dm:510`). A forma entra no diario ja na primeira
	/// tentativa: "tentei duas vezes e ele ainda nao despertou" e progresso tambem.
	/// </summary>
	private void AnotarTentativaDeForma(ServerPlayer mestre, ServerPlayer aluno, FormaDef forma)
	{
		if (DiarioDoPar(mestre, aluno) is not { } d) return;
		if (ProgressoDeAluno.AnotarTentativa(d, forma.Id, NowMs()) is not { } e) return;
		ProgressoDeAluno.Fotografar(e, ProgressoAgora(aluno, e));
		SalvarDiarios();
	}

	/// <summary>A FORMA DESPERTOU COM O MESTRE (`DespertarAssistido`, depois de o corpo entrar nela).</summary>
	private void AnotarDespertarAssistido(ServerPlayer mestre, ServerPlayer aluno, FormaDef forma)
	{
		if (DiarioDoPar(mestre, aluno) is not { } d) return;
		if (ProgressoDeAluno.AnotarDespertar(d, forma.Id, NowMs()) is not { } e) return;
		ProgressoDeAluno.Fotografar(e, ProgressoAgora(aluno, e));
		SalvarDiarios();
	}

	/// <summary>
	/// ALGUEM PASSOU UMA SKILL A ALGUEM (`ResponderALicao`). So entra no diario quando quem ensinou E o mestre de quem
	/// aprendeu: o ensino de skill nao pede vinculo (`EnsinoDeSkill`), e a licao de um estranho nao e ensinamento de
	/// mestre nenhum.
	/// </summary>
	private void AnotarLicaoDoMestre(ServerPlayer quemEnsinou, ServerPlayer aluno, Skill skill)
	{
		if (DiarioDoPar(quemEnsinou, aluno) is not { } d) return;
		if (ProgressoDeAluno.AnotarLicao(d, skill.Path, NowMs()) is not { } e) return;
		ProgressoDeAluno.Fotografar(e, ProgressoAgora(aluno, e));
		SalvarDiarios();
	}

	// =====================================================================
	// A FOTOGRAFIA
	// =====================================================================
	/// <summary>
	/// O PROGRESSO DESTE CORPO NUM ENSINAMENTO, lido do estado vivo dele: a forma pelo `EstadoDeForma` (despertou? e a
	/// maestria), a licao pelo livro (ainda a tem?) e pelos niveis. Nada daqui e poder de luta.
	/// </summary>
	private ProgressoNoEnsinamento ProgressoAgora(ServerPlayer aluno, Ensinamento e)
	{
		if (e.Tipo == TipoDeEnsinamento.Forma)
		{
			// FORMA QUE O CATALOGO NAO CONHECE MAIS (um id que mudou de nome) nao despertou: `Catalogo.Rede` devolveria
			// o numero da base pra ela, e a base esta liberada em todo mundo.
			if (Catalogo.Def(e.Id) is not { } d || d.Id == Catalogo.IdBase) return ProgressoDeAluno.DaForma(false, 0);
			return ProgressoDeAluno.DaForma(aluno.Forma.Despertou(d.Id), aluno.Forma.Maestria.De(d.Id));
		}

		bool sabe = aluno.Livro?.Sabe(e.Id) == true;
		if (_skills is not { } catalogo || catalogo.Get(e.Id) is not { } s) return ProgressoDeAluno.DaLicao(sabe, 0, 0, 0, 0);

		// SO TEM NIVEL A SKILL QUE TEM REGRA DE NIVEL (`niveis.json`): o `MaxNivel = 3` de fabrica do catalogo nao e
		// teto de nada numa skill que nunca sobe -- e a maioria das que se ensinam (o Kamehameha) nao sobe.
		int teto = RegrasDeNivel.Get(e.Id) != null ? Math.Max(1, s.MaxNivel) : 0;
		(double exp, double barreira) = aluno.Niveis.Andamento(catalogo, e.Id);
		return ProgressoDeAluno.DaLicao(sabe, teto, aluno.Niveis.Nivel(e.Id), exp, barreira);
	}

	/// <summary>Refaz a fotografia de cada ensinamento de um aluno que esta no mundo. Devolve TRUE se alguma mudou.</summary>
	private bool FotografarOAluno(ServerPlayer aluno, DiarioDeAluno d)
	{
		if (FotoDoAlunoNaoSeGuardaDeTeste) return false;
		bool mudou = false;
		foreach (Ensinamento e in d.Ensinamentos) mudou |= ProgressoDeAluno.Fotografar(e, ProgressoAgora(aluno, e));
		return mudou;
	}

	/// <summary>A fotografia de um diario, se o aluno dele esta no mundo (e a lembranca da pessoa junto).</summary>
	private void FotografarSeEstaNoMundo(DiarioDeAluno d, long agora, ServerPlayer? corpo = null)
	{
		corpo ??= CorpoDaAssinatura(d.Aluno);
		if (corpo == null) return;
		LembrarPessoa(corpo, d.Aluno, agora);
		if (FotografarOAluno(corpo, d)) _diariosSujos = true;
	}

	/// <summary>O que o diario lembra de uma pessoa que esta no mundo: o nome e a raca de agora, e a hora.</summary>
	private void LembrarPessoa(ServerPlayer pl, string sig, long agora)
	{
		if (!_pessoasDoDiario.TryGetValue(sig, out PessoaDoDiario? p))
		{
			p = new PessoaDoDiario();
			_pessoasDoDiario[sig] = p;
			_diariosSujos = true;
		}
		if (p.Nome != pl.Name || p.Raca != pl.Race)
		{
			p.Nome = pl.Name;
			p.Raca = pl.Race;
			_diariosSujos = true;
		}
		p.VistoEm = agora;
	}

	/// <summary>De quanto em quanto tempo o "visto por ultimo" de quem esta no mundo vai pro disco, em segundos (uma queda do servidor perde no maximo isto).</summary>
	private const double SegundosEntreCarimbosDeVisto = 60;

	/// <summary>
	/// O TIQUE DE 1 Hz DO DIARIO: fotografa quem esta no mundo, percebe quem saiu, joga fora o diario de vinculo que
	/// acabou e grava o que mudou. Sem vinculo nenhum no servidor ele nao faz nada.
	///
	/// A FAXINA E POR LEITURA, e nao por aviso de cada porta que desfaz um vinculo (largar, dispensar, a assinatura
	/// reciclada, e a que nascer depois): o diario segue o <see cref="_mestreDe"/>, e um laco que pergunta nao tem
	/// o que esquecer. Correcao nenhuma depende dela -- o <see cref="DiarioDoVinculo"/> ja recusa diario de outro
	/// mestre, e o <see cref="AbrirDiarioDoAluno"/> sobrescreve.
	/// </summary>
	private void TickDosAlunos()
	{
		if (AlunosDeTeste) DispararABancadaDosAlunos();   // a bancada `--alunosteste` (so com a flag) -- ver `GameServer.AlunosTeste.cs`

		if (_mestreDe.Count == 0 && _diarios.Count == 0 && _doDiscipuladoNoMundo.Count == 0) return;
		GarantirDiarios();
		long agora = NowMs();

		_mestresDoTique.Clear();
		foreach (string m in _mestreDe.Values) _mestresDoTique.Add(m);

		// 1. QUEM DO DISCIPULADO ESTA NO MUNDO: a lembranca da pessoa e, do aluno, a fotografia.
		_noMundoAgora.Clear();
		foreach (ServerPlayer pl in _players.Values)
		{
			if (pl.Conta.Length == 0 || !EhPessoa(pl)) continue;
			string sig = pl.Assinatura;
			bool aluno = _mestreDe.ContainsKey(sig);
			if (!aluno && !_mestresDoTique.Contains(sig)) continue;

			_noMundoAgora.Add(sig);
			LembrarPessoa(pl, sig, agora);
			if (aluno && DiarioDoVinculo(sig) is { } d && FotografarOAluno(pl, d)) _diariosSujos = true;
		}

		// 2. QUEM SAIU DO MUNDO desde o tique passado: o "visto por ultimo" dele e o de agora ha pouco, e vai pro disco.
		foreach (string sig in _doDiscipuladoNoMundo)
			if (!_noMundoAgora.Contains(sig)) { _diariosSujos = true; break; }
		(_doDiscipuladoNoMundo, _noMundoAgora) = (_noMundoAgora, _doDiscipuladoNoMundo);

		// 3. A FAXINA: diario de vinculo que acabou, e lembranca de quem nao esta mais em vinculo nenhum.
		List<string>? fora = null;
		foreach ((string sigAluno, DiarioDeAluno d) in _diarios)
			if (MestreDe(sigAluno) != d.Mestre) (fora ??= []).Add(sigAluno);
		if (fora != null)
		{
			foreach (string sig in fora) _diarios.Remove(sig);
			_diariosSujos = true;
		}
		fora = null;
		foreach (string sig in _pessoasDoDiario.Keys)
			if (!_mestreDe.ContainsKey(sig) && !_mestresDoTique.Contains(sig)) (fora ??= []).Add(sig);
		if (fora != null)
		{
			foreach (string sig in fora) _pessoasDoDiario.Remove(sig);
			_diariosSujos = true;
		}

		if (_paineisMandados.Count > _players.Count)
			foreach (int id in _paineisMandados.Keys.ToList())
				if (!_players.ContainsKey(id)) _paineisMandados.Remove(id);

		// 4. O DISCO: o que mudou, no maximo a cada `SegundosEntreGravacoes`; e o carimbo de "visto", de minuto em minuto.
		if (_doDiscipuladoNoMundo.Count > 0 && agora >= _proximoCarimboDeVisto)
		{
			_proximoCarimboDeVisto = agora + (long)(SegundosEntreCarimbosDeVisto * 1000);
			_diariosSujos = true;
		}
		if (_diariosSujos && agora >= _proximaGravacaoDosDiarios) SalvarDiarios();
	}

	// =====================================================================
	// O PAINEL
	// =====================================================================
	/// <summary>
	/// O QUE SE DIZ DE UMA PESSOA NUM CARTAO: o nome e a raca (do corpo vivo, ou do que o diario lembra), se esta no
	/// mundo e ha quanto tempo saiu. A raca e a presenca ja sao de todo mundo (o `Who` as lista); o resto do corpo
	/// -- poder, atributos, classe, lugar -- nao tem campo aqui.
	/// </summary>
	private PessoaNoPainel PessoaVista(string sig, long agora)
	{
		ServerPlayer? corpo = CorpoDaAssinatura(sig);
		_pessoasDoDiario.TryGetValue(sig, out PessoaDoDiario? lembrada);
		string nome = corpo?.Name ?? (lembrada is { Nome.Length: > 0 } ? lembrada.Nome : NomeDaAssinatura(sig));
		string raca = corpo?.Race ?? lembrada?.Raca ?? "";
		if (PoderNoPainelDeTeste && corpo != null) raca = ((long)corpo.Ficha.BP).ToString();
		return new PessoaNoPainel(sig, nome, raca, corpo != null,
								  corpo != null ? 0 : ProgressoDeAluno.Ha(lembrada?.VistoEm ?? 0, agora), 0);
	}

	/// <summary>
	/// **O PAINEL DE UM JOGADOR, AGORA.** Junta os diarios que sao dele -- o do vinculo dele como aluno, os dos colegas
	/// (os outros alunos do MESMO mestre) e os dos alunos que ele guia --, refaz a fotografia de quem esta no mundo,
	/// e entrega ao <see cref="ProgressoDeAluno.Montar"/>, que e quem corta o que ele nao pode ver.
	/// </summary>
	internal PainelDoDiscipulado PainelDe(ServerPlayer pl)
	{
		if (!EhPessoa(pl)) return PainelDoDiscipulado.Vazio;
		string sig = pl.Assinatura;
		GarantirDiarios();
		long agora = NowMs();

		DiarioDeAluno? meu = DiarioDoVinculo(sig);
		var colegas = new List<DiarioDeAluno>();
		if (meu != null)
		{
			FotografarSeEstaNoMundo(meu, agora, pl);
			foreach (string outro in AlunosDe(meu.Mestre).ToList())
				if (outro != sig && DiarioDoVinculo(outro) is { } dele) { FotografarSeEstaNoMundo(dele, agora); colegas.Add(dele); }
		}

		var meusAlunos = new List<DiarioDeAluno>();
		foreach (string aluno in AlunosDe(sig).ToList())
			if (DiarioDoVinculo(aluno) is { } dele) { FotografarSeEstaNoMundo(dele, agora); meusAlunos.Add(dele); }

		return ProgressoDeAluno.Montar(meu, colegas, meusAlunos, s => PessoaVista(s, agora), agora);
	}

	/// <summary>
	/// MANDA O PAINEL A QUEM O PEDIU. Com `forcar` (a tela acabou de abrir) sai sempre; sem ele, so se o CONTEUDO
	/// mudou desde o ultimo que esta tela recebeu -- uma tela aberta num mundo parado nao recebe pacote nenhum.
	/// </summary>
	private void MandarPainelDoDiscipulado(ServerPlayer pl, bool forcar)
	{
		PainelDoDiscipulado painel = PainelDe(pl);
		string conteudo = ProgressoDeAluno.Conteudo(painel);
		if (!forcar && _paineisMandados.TryGetValue(pl.Id, out (NetPeer? Tela, string Conteudo) antes)
			&& ReferenceEquals(antes.Tela, pl.Peer) && antes.Conteudo == conteudo)
			return;
		_paineisMandados[pl.Id] = (pl.Peer, conteudo);

		var w = Protocol.Begin(Protocol.S2C.MestreEAlunos);
		FioDosAlunos.Escrever(w, painel);
		if (PoderNoPainelDeTeste) w.Put(pl.Ficha.BP);

		// A BANCADA ESCUTA AQUI: e ESTE writer que vira os bytes do fio. Nulo em jogo.
		EscutaDoDiscipulado?.Add((pl.Id, w.CopyData()));
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// OS VERBOS
	// =====================================================================
	/// <summary>
	/// Os verbos da tela. Devolve `true` quando o comando era deste canal -- inclusive quando foi RECUSADO (senao o
	/// funil seguiria ate "comando desconhecido").
	///
	///   * `alunos` [`seguir`]        -- o pedido do painel (ao abrir a tela / com ela aberta);
	///   * `alunos_ensinar &lt;aluno&gt;`   -- o "Ajudar a transformar" do cartao DESTE aluno;
	///   * `alunos_dispensar &lt;aluno&gt;` -- o "Dispensar aluno" do cartao DESTE aluno.
	/// </summary>
	private bool ComandoDeAlunos(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "alunos": PedidoDoPainel(pl, seguir: arg == "seguir"); return true;
			case "alunos_ensinar": EnsinarPeloCartao(pl, arg); return true;
			case "alunos_dispensar": DispensarPeloCartao(pl, arg); return true;
			default: return false;
		}
	}

	/// <summary>
	/// O PEDIDO DO PAINEL. **O PORTAO E AQUI**: quem nao tem mestre nem aluno nao recebe pacote nenhum -- a aba nem
	/// aparece pra ele (`RequisitosDosVerbos`), mas esconder aba nunca foi permissao. O `seguir` de quem acabou de
	/// perder o vinculo (a tela dele ainda nao soube) e recusado calado: a frase de quem desfez o vinculo ja chegou.
	/// </summary>
	private void PedidoDoPainel(ServerPlayer pl, bool seguir)
	{
		if (!PainelSemVinculoDeTeste && !TemDiscipulado(pl))
		{
			_paineisMandados.Remove(pl.Id);
			if (!seguir) Avisar(pl, SemDiscipulado);
			return;
		}
		MandarPainelDoDiscipulado(pl, forcar: !seguir);
	}

	/// <summary>
	/// "AJUDAR A TRANSFORMAR", NO CARTAO DE UM ALUNO. E o MESMO gesto do verb da aba Learning (`mst_ensinar`: de
	/// frente pro aluno, a mesma recarga, as mesmas recusas -- entra pelo `UsarHabilidade` de sempre), so que MIRADO:
	/// o cartao e deste aluno, e o gesto ou e nele ou nao e em ninguem. Sem isto, com dois alunos por perto o botao do
	/// cartao de um provocaria o outro -- e gastaria os 5 minutos de folego do mestre.
	///
	/// A mira e o proprio marcado: o `PessoaNaFrente` da preferencia a quem o mestre marcou, e o cartao marca o aluno
	/// dele so durante o gesto.
	/// </summary>
	private void EnsinarPeloCartao(ServerPlayer mestre, string sigAluno)
	{
		if (!EhPessoa(mestre) || sigAluno.Length == 0 || MestreDe(sigAluno) != mestre.Assinatura)
		{
			Avisar(mestre, SoPraAlunoSeu);
			return;
		}
		if (CorpoDaAssinatura(sigAluno) is not { } aluno)
		{
			Avisar(mestre, $"{NomeDaAssinatura(sigAluno)} não está no mundo agora: não há como guiá-lo daqui.");
			return;
		}

		int alvoDeAntes = mestre.AlvoId;
		mestre.AlvoId = aluno.Id;
		try
		{
			if (!CartaoSemMiraDeTeste && PessoaNaFrente(mestre, soMeusAlunos: true) != aluno)
			{
				Avisar(mestre, $"fique de frente para {aluno.Name} pra guiá-lo numa transformação.");
				return;
			}
			UsarHabilidade(mestre, "mst_ensinar");
		}
		finally { mestre.AlvoId = alvoDeAntes; }
	}

	/// <summary>
	/// "DISPENSAR ALUNO", NO CARTAO DE UM ALUNO -- o `mstdrop` do painel do original (`HtmlUI.dm:918-922`): pela
	/// ASSINATURA, de qualquer lugar, com o aluno no mundo ou fora dele; a unica pergunta e se ele e aluno de quem
	/// pede. E o gesto que o `DispensarAluno` (o verb da aba Learning, que dispensa quem esta na frente) deixou
	/// escrito que faltava: *"quem quiser dispensar alguem que nao esta por perto usa o painel, no dia em que ele
	/// existir"*. O desfazer e o de sempre (<see cref="Desvincular"/>).
	/// </summary>
	private void DispensarPeloCartao(ServerPlayer mestre, string sigAluno)
	{
		if (!EhPessoa(mestre) || sigAluno.Length == 0 || MestreDe(sigAluno) != mestre.Assinatura)
		{
			Avisar(mestre, SoPraAlunoSeu);
			return;
		}
		Desvincular(sigAluno, "o mestre dispensou o aluno");

		// A TELA DE QUEM DISPENSOU se acerta na hora (se ele ainda guia alguem, ou tem mestre): o aluno saiu dela.
		if (TemDiscipulado(mestre)) MandarPainelDoDiscipulado(mestre, forcar: true);
	}
}
