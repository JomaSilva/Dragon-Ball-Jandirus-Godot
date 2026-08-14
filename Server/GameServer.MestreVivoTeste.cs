using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DE MESTRE E ALUNO **COM PERSONAGENS DE VERDADE** -- `--mestrevivo`. E a IRMA da
/// `--mestreteste`, na mesma divisao de trabalho que a `--diagberco` tem com a `--bercovivo`.
///
/// ============================ A DIVISAO DE TRABALHO ENTRE AS DUAS ============================
/// A `--mestreteste` mede a REGRA com corpos FORJADOS: `new ServerPlayer` com `Ficha = new Fighter
/// { BP = 9_000_000 }` escrito a mao, `Livro = new SkillBook()` vazio e `Forma.Limiares` NULO. Ela
/// responde "a regra, alimentada com estes numeros, decide assim" -- e nao pode responder mais que
/// isso, porque os numeros sao dela.
///
/// Esta aqui mede a MESMA regra alimentada pelo JOGO: dois personagens criados na conta, nascidos
/// pelo <see cref="Nascer"/> (classe SORTEADA), com o limiar pessoal de SSJ **rolado no nascimento**
/// pelo `LimiaresPessoais.Rolar`, gravados num arquivo de conta de verdade, e trazidos ao mundo pela
/// sequencia do `Entrar`. A diferenca nao e cerimonia -- ela muda o que da pra provar:
///
///   * **a porta cortada pela metade** so quer dizer alguma coisa sobre um limiar SORTEADO. Com
///     `Limiares` nulo o `PortaDeTeste` cai na constante do catalogo, igual pra todo mundo, e a
///     bancada estaria dividindo por dois um numero que o jogo nao usa em ninguem;
///   * **o ganho contra o mestre** so chega no BP se o corpo tiver `relBPmax`, `Egains`, `Etechnique`
///     e `SparMod` de verdade -- todos derivados da raca, da classe e da idade que o nascimento
///     sorteou. Num corpo forjado o `CapCheck` pode devolver zero e o teste passaria comparando
///     dois zeros;
///   * **o relogin** nao existe sem conta no disco. E as quatro coisas que este sistema promete
///     atravessar o logout (o vinculo, a porta cortada, a recarga de 5 min e o `wastaught`) moram em
///     QUATRO lugares diferentes -- `mestres.txt`, `PortasCortadas`, `MestreRecargaAte` e
///     `SkillsEnsinadas` --, e cada uma some sozinha, calada.
/// ==========================================================================================
///
/// AS OITO FAMILIAS, E COMO CADA UMA REPROVA:
///  1. O PORTAO DE 3x, NOS DOIS SENTIDOS -- 2,9x recusa, 3x aceita, 3,1x aceita. **Reprova** se
///     alguem trocar `>=` por `>`, mexer no `MST_BP_RATIO`, ou -- o caso silencioso -- ler o poder
///     EXPRESSO no lugar do BP BASE: os dois numeros sao postos em desacordo de proposito, nas duas
///     direcoes.
///  2. O TETO DE 5 ALUNOS -- **reprova** se o sexto entrar, se dispensar nao abrir vaga, ou se o
///     teto for lido de um contador que nao e a lista.
///  3. O GANHO CHEGA NO BP -- **reprova** se o bit do mestre nao chegar ao `AttackGain` (era o
///     estado do projeto ate ha pouco: sete chamadores, nenhum passando o segundo argumento), se o
///     teto de 3x ou o piso de 1,2x sairem do lugar, ou se ter mestre PIORAR o ganho.
///  4. A METADE -- **reprova** se o corte deixar de entrar no funil do `Avaliar` (viraria desconto
///     do chamador, e a mensagem de recusa mentiria), se a metade virar outro numero, ou se o corte
///     nao ficar anotado (o aluno desperta e nao reentra -- o bug que o `mst_form_apply` descreve).
///  5. AS DUAS RECARGAS -- **reprova** se a de 5 min deixar de ser cobrada no GESTO, se a de 6 s do
///     ensino de skill for confundida com ela, ou se uma consumir a outra (sao dois sistemas).
///  6. O ALUNO NAO REPASSA -- **reprova** nos dois sentidos: se a copia ensinada puder ser
///     repassada (a corrente se forma) **ou** se a origem parar de poder ensinar (ninguem ensina
///     nada, e a metade negativa do teste passaria verde sozinha).
///  7. AS DUAS DISCIPLINAS -- **reprova** se o Ultra Instinto/Ego entrar na lista do discipulado
///     (duas portas pra mesma coisa) **ou** se a cadeia propria deles parar de ensinar. As duas
///     metades numa secao so, de proposito: separadas, "ninguem ensina nada" passaria na primeira.
///  8. O RELOGIN -- **reprova** se qualquer uma das quatro coisas nao atravessar o disco.
///
/// ============================ A CONTA E NOVA, E ELA SAI NO FIM ============================
/// Tres contas de bancada (o `AccountStore` guarda tres personagens cada, e esta bancada precisa de
/// sete corpos com assinatura propria). Elas sao APAGADAS na entrada e na saida: na entrada porque
/// "conta nova" e o pedido -- uma conta reaproveitada traria o vinculo da rodada anterior e a
/// familia 8 passaria sem o disco ter feito nada --, e na saida pra nao virar conta de verdade no
/// painel de admin.
///
/// E o `mestres.txt` do mundo e FOTOGRAFADO e devolvido, como na `--mestreteste`: o discipulado
/// grava em disco na hora, e rodar bancada nao pode mexer no vinculo de ninguem.
/// ======================================================================================
///
///     Godot --headless --path . --server --port 7965 --mestrevivo
/// </summary>
public partial class GameServer
{
	/// <summary>Faixa de ids desta bancada -- acima de todas as outras (a maior era 90.800).</summary>
	private const int IdBaseDoMestreVivo = 91_000;

	/// <summary>
	/// As contas desta bancada. TRES porque sao sete personagens (o teto de cinco alunos precisa de
	/// seis corpos vinculaveis) e cada conta tem tres slots.
	/// </summary>
	private static readonly string[] ContasDaBancadaViva =
		["bancada_mestre_vivo_a", "bancada_mestre_vivo_b", "bancada_mestre_vivo_c"];

	private int _mvOk, _mvFalhou, _mvCorpos;

	private void AfirmarMv(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _mvOk++; GD.Print($"[mestrevivo]   OK    {oque}"); return; }
		_mvFalhou++;
		GD.PrintErr($"[mestrevivo]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDoMestreVivo()
	{
		_mvOk = _mvFalhou = 0;
		_mvCorpos = 0;
		GD.Print("[mestrevivo] ============ MESTRE E ALUNO, COM DOIS PERSONAGENS DE VERDADE ============");

		if (_store == null || _racas == null || _skills == null)
		{
			// EM VOZ ALTA, e nao com zero checagens: uma bancada que nao roda tem que ser
			// distinguivel de uma bancada que passou (PARTE 3, armadilha 5).
			AfirmarMv("o servidor tem conta, racas e skills carregados (sem os tres nao ha personagem)",
					  false, $"store={_store != null} racas={_racas != null} skills={_skills != null}");
			GD.Print($"[mestrevivo] ============ {_mvOk} passaram, {_mvFalhou} falharam ============");
			return;
		}

		// A FOTO DO VINCULO DE VERDADE. Ver o cabecalho.
		var vinculosReais = new Dictionary<string, string>(_mestreDe, StringComparer.Ordinal);
		var nomesReais = new Dictionary<string, string>(_nomePorAssinatura, StringComparer.Ordinal);
		_mestreDe.Clear();
		_nomePorAssinatura.Clear();

		ApagarContasDaBancadaViva();

		ZoneKey zona = ZoneKey.Premade(
			Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		var vivos = new List<ServerPlayer>();
		try
		{
			// ============================ OS SETE PERSONAGENS, CRIADOS DE VERDADE ============================
			// Dois Saiyajins (mestre e aluno -- a escada de formas so existe pra quem tem uma) e cinco
			// corpos quaisquer pro teto e pra corrente do ensino. Cada um num slot proprio, porque a
			// assinatura -- que e a chave do discipulado inteiro -- e `hash(conta, slot)`.
			// ==========================================================================================
			ServerPlayer mestre = CriarELogar("Vivo Mestre", "Saiyan", 0, 0, zona, 100);
			ServerPlayer aluno = CriarELogar("Vivo Aluno", "Saiyan", 0, 1, zona, 130);
			ServerPlayer terceiro = CriarELogar("Vivo Terceiro", "Human", 0, 2, zona, 160);
			ServerPlayer[] extras =
			[
				CriarELogar("Vivo Quarto", "Human", 1, 0, zona, 190),
				CriarELogar("Vivo Quinto", "Human", 1, 1, zona, 220),
				CriarELogar("Vivo Sexto", "Human", 1, 2, zona, 250),
			];
			ServerPlayer setimo = CriarELogar("Vivo Setimo", "Human", 2, 0, zona, 280);

			vivos.AddRange([mestre, aluno, terceiro, setimo]);
			vivos.AddRange(extras);

			AfirmarMv("os sete personagens nasceram pelo caminho de producao e estao no mundo",
					  vivos.All(p => p.Assinatura.Length > 0 && p.Livro != null && p.Combate != null
									 && _players.ContainsKey(p.Id)),
					  string.Join(" | ", vivos.Select(p => $"{p.Name}/{p.Class}")));

			// O QUE SEPARA ESTA BANCADA DA OUTRA, DITO COMO AFIRMACAO: o limiar de forma destes dois
			// foi SORTEADO no nascimento. Num corpo forjado ele e nulo e o codigo escorrega pra
			// constante do catalogo -- calado, porque `Limiares?.Porta(d) is > 0` devolve falso.
			AfirmarMv("...e os dois Saiyajins tem LIMIAR PESSOAL de forma, sorteado no nascimento "
					+ "(e o que o MST_HALF corta -- num corpo forjado ele e nulo)",
					  mestre.Forma.Limiares is { Rolado: true } && aluno.Forma.Limiares is { Rolado: true },
					  $"aluno: semente {aluno.Forma.Limiares?.Semente}");

			OPortaoDeTresVezes(mestre, aluno, terceiro);
			OTetoDeCincoAlunos(mestre, aluno, terceiro, extras, setimo);
			OGanhoChegaNoBp(mestre, aluno);
			AMetadeDaPorta(mestre, aluno);
			AsDuasRecargas(mestre, aluno, terceiro);
			OAlunoNaoRepassa(mestre, aluno, terceiro);
			AsDuasDisciplinas(mestre, aluno, terceiro);
			ORelogin(mestre, aluno, terceiro);
		}
		catch (Exception e)
		{
			AfirmarMv($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			foreach (ServerPlayer p in vivos) Recolher(p);
			_mestreDe.Clear();
			_nomePorAssinatura.Clear();
			foreach (var kv in vinculosReais) _mestreDe[kv.Key] = kv.Value;
			foreach (var kv in nomesReais) _nomePorAssinatura[kv.Key] = kv.Value;
			SalvarMestres();
			ApagarContasDaBancadaViva();
		}

		GD.Print($"[mestrevivo] ============ {_mvOk} passaram, {_mvFalhou} falharam ============");
	}

	// =====================================================================
	// 1) O PORTAO DE 3x, NOS DOIS SENTIDOS
	// =====================================================================
	/// <summary>
	/// `mst_bp_ok` (`MasterStudent.dm:88`): o mestre precisa de 3x o BP do aluno, e os dois numeros
	/// sao o BP **BASE**.
	///
	/// Tudo pelo VERB (`ConvidarAluno` -> `ResponderAoMestre`), e nao pela funcao pura: a funcao pura
	/// ja e provada na `--mestreteste`; o que so daqui se ve e QUAL numero o verb escolhe passar.
	/// </summary>
	private void OPortaoDeTresVezes(ServerPlayer mestre, ServerPlayer aluno, ServerPlayer terceiro)
	{
		GD.Print("[mestrevivo] -- 1) O PORTAO DE 3x (2,9x recusa / 3x aceita / 3,1x aceita)");

		Estacionar(terceiro, 160);
		double bpAluno = 1_000_000;
		PorBp(aluno, bpAluno);
		Encostar(mestre, aluno, tiles: 1);

		// 2,9x: RECUSA.
		PorBp(mestre, bpAluno * 2.9);
		aluno.PedidoDoMestre = null;
		ConvidarAluno(mestre);
		AfirmarMv("com 2,9x o convite e RECUSADO (nao ha pedido pendente nenhum)",
				  aluno.PedidoDoMestre == null,
				  $"mestre {mestre.Ficha.BP:N0} x aluno {aluno.Ficha.BP:N0}");
		AfirmarMv("   ...e nada foi vinculado por baixo do pano",
				  MestreDe(aluno.Assinatura).Length == 0);

		// 3,0x EXATO: aceita. E a borda que separa `>=` de `>`.
		PorBp(mestre, bpAluno * Discipulado.RazaoDeBp);
		ConvidarAluno(mestre);
		AfirmarMv($"com {Discipulado.RazaoDeBp:0}x EXATO o convite sai (`>=`, e nao `>`)",
				  aluno.PedidoDoMestre is { Despertar: false });

		// 3,1x: aceita e VINCULA, pelo aceite do aluno.
		aluno.PedidoDoMestre = null;
		PorBp(mestre, bpAluno * 3.1);
		ConvidarAluno(mestre);
		ResponderAoMestre(aluno, aceitou: true);
		AfirmarMv("com 3,1x o aluno aceita e o vinculo fecha",
				  MestreDe(aluno.Assinatura) == mestre.Assinatura);
		AfirmarMv("   ...e o vinculo ja esta no disco (`mestres.txt`, gravado no gesto)",
				  System.IO.File.Exists(ArquivoDeMestres)
				  && System.IO.File.ReadAllText(ArquivoDeMestres).Contains(aluno.Assinatura, StringComparison.Ordinal));

		// ============================ O BP **BASE**, E O EXPRESSO MENTINDO NOS DOIS SENTIDOS ============================
		// PARTE 3 da spec: *"expressedBP NAO E BP"*. A `--mestreteste` ja poe os dois em desacordo num
		// sentido (BP forte, expresso fraco); aqui vao os DOIS, porque um sentido sozinho nao separa
		// "le o BP" de "le o maior dos dois".
		// ==========================================================================================================
		Desvincular(aluno.Assinatura, "bancada: recomecando o portao");

		PorBp(mestre, bpAluno * 3.1);
		mestre.Ficha.expressedBP = 10;          // pelo EXPRESSO ele e mil vezes mais fraco
		aluno.Ficha.expressedBP = 10_000;
		aluno.PedidoDoMestre = null;
		ConvidarAluno(mestre);
		AfirmarMv("mestre com 3,1x de BP e 1/1000 do poder EXPRESSO convida igual (o portao le a BASE)",
				  aluno.PedidoDoMestre is { Despertar: false });

		PorBp(mestre, bpAluno * 2.9);
		mestre.Ficha.expressedBP = 10_000_000;  // e agora ele e mil vezes mais FORTE no expresso
		aluno.Ficha.expressedBP = 10_000;
		aluno.PedidoDoMestre = null;
		ConvidarAluno(mestre);
		AfirmarMv("...e mestre com 2,9x de BP e MIL VEZES o poder expresso continua RECUSADO "
				+ "(o outro sentido: nao e 'o maior dos dois')",
				  aluno.PedidoDoMestre == null,
				  $"BP {mestre.Ficha.BP:N0} x {aluno.Ficha.BP:N0} | expresso "
				  + $"{mestre.Ficha.expressedBP:N0} x {aluno.Ficha.expressedBP:N0}");

		// E VOLTA A VALER pro resto da bancada.
		PorBp(mestre, bpAluno * 4);
		ConvidarAluno(mestre);
		ResponderAoMestre(aluno, aceitou: true);
		AfirmarMv("com folga o vinculo fecha de novo (o estado que as familias seguintes usam)",
				  MestreDe(aluno.Assinatura) == mestre.Assinatura);
	}

	// =====================================================================
	// 2) O TETO DE CINCO ALUNOS DISPARA
	// =====================================================================
	/// <summary>
	/// `MST_MAX_STUDENTS` (`MasterStudent.dm:28`). PARTE 3, armadilha 3: *"um teto que nunca e
	/// atingido e indistinguivel de teto nenhum"* -- entao a lista e ENCHIDA ate o limite, pelos
	/// verbos, com personagens de verdade.
	/// </summary>
	private void OTetoDeCincoAlunos(ServerPlayer mestre, ServerPlayer aluno, ServerPlayer terceiro,
									ServerPlayer[] extras, ServerPlayer setimo)
	{
		GD.Print("[mestrevivo] -- 2) O TETO DE CINCO ALUNOS");

		// O MESTRE PRECISA DE 3x O BP DE CADA UM. Os outros nascem com BP de recem-nascido, entao o
		// portao nao e o que esta em jogo aqui -- ele ja foi medido na familia 1.
		foreach (ServerPlayer p in extras.Append(terceiro).Append(setimo)) PorBp(p, 1_000);
		PorBp(mestre, 4_000_000);

		// ============================ O ALUNO DA FAMILIA 1 SAI DE PERTO ============================
		// Ele ficou colado no mestre (o `Encostar` da familia 1), e o `AlvoNaFrente` devolve o MAIS
		// PROXIMO: com ele ali, TODO convite desta secao cairia nele -- que ja tem mestre -- e a lista
		// nunca encheria. E a licao do `Estacionar` da `--mestreteste`, e ela custou quatro vermelhas
		// aqui: quem nao e o alvo tem que estar longe.
		// ======================================================================================
		Estacionar(aluno, 130);

		ServerPlayer[] fila = [terceiro, .. extras];
		for (int i = 0; i < fila.Length; i++)
		{
			// CADA CORPO, UM ENDERECO -- a licao do `Estacionar` da `--mestreteste`: com dois no mesmo
			// tile o `AlvoNaFrente` devolve o errado e a bancada mede geometria.
			Encostar(mestre, fila[i], tiles: 1);
			ConvidarAluno(mestre);
			ResponderAoMestre(fila[i], aceitou: true);
			Estacionar(fila[i], 160 + i * 30);
		}

		AfirmarMv($"o mestre chega a {Discipulado.MaxAlunos} alunos (o aluno da familia 1 mais quatro)",
				  ContarAlunos(mestre.Assinatura) == Discipulado.MaxAlunos,
				  $"{ContarAlunos(mestre.Assinatura)}");

		Encostar(mestre, setimo, tiles: 1);
		ConvidarAluno(mestre);
		AfirmarMv("...e o SEXTO e recusado -- o teto dispara mesmo",
				  setimo.PedidoDoMestre == null && MestreDe(setimo.Assinatura).Length == 0);
		Estacionar(setimo, 280);

		// DISPENSAR ABRE VAGA -- senao o teto seria uma porta de mao unica.
		ServerPlayer dispensado = extras[^1];
		Encostar(mestre, dispensado, tiles: 1);
		DispensarAluno(mestre);
		Estacionar(dispensado, 250);
		AfirmarMv("dispensar um aluno abre vaga",
				  ContarAlunos(mestre.Assinatura) == Discipulado.MaxAlunos - 1
				  && MestreDe(dispensado.Assinatura).Length == 0);

		Encostar(mestre, setimo, tiles: 1);
		ConvidarAluno(mestre);
		ResponderAoMestre(setimo, aceitou: true);
		AfirmarMv("...e agora o que sobrava entra",
				  MestreDe(setimo.Assinatura) == mestre.Assinatura);

		// A LISTA VOLTA AO TAMANHO QUE AS OUTRAS FAMILIAS ESPERAM: so o aluno e o terceiro ficam.
		foreach (ServerPlayer p in extras.Append(setimo))
			if (MestreDe(p.Assinatura) == mestre.Assinatura)
				Desvincular(p.Assinatura, "bancada: fim da familia do teto");
		Estacionar(terceiro, 160);
		AfirmarMv("   (preparo) o mestre volta a ter so o aluno e o terceiro",
				  ContarAlunos(mestre.Assinatura) == 2);
	}

	// =====================================================================
	// 3) O GANHO CHEGA NO BP DE VERDADE
	// =====================================================================
	/// <summary>
	/// `fight_gain_mult` (`combatgains.dm:88-100`): treinar contra o proprio mestre paga ate 3x, com
	/// piso de 1,2x.
	///
	/// ============================ AQUI ELE E MEDIDO NO BP, E NAO NA FUNCAO ============================
	/// A `--mestreteste` compara o retorno de `FightGainMult` e varre os fontes atras dos chamadores.
	/// As duas coisas juntas ainda deixam um buraco: o multiplicador pode chegar ao `AttackGain` e
	/// morrer la dentro -- `CapCheck` devolve zero com `relBPmax` colado no BP (o bug do `UPMod = 0`
	/// que este port ja pagou), e o teste continuaria verde comparando dois zeros.
	///
	/// Entao aqui o gesto e o soco de verdade (`Atacar`, o mesmo do pacote do cliente) e a medida e o
	/// **BP antes e depois**. As tres medidas saem do mesmo corpo, com o mesmo estado, mudando so o
	/// vinculo -- e o que sobra na razao e o bonus.
	/// ==========================================================================================
	/// </summary>
	private void OGanhoChegaNoBp(ServerPlayer mestre, ServerPlayer aluno)
	{
		GD.Print("[mestrevivo] -- 3) O GANHO CONTRA O MESTRE, MEDIDO NO BP");

		const double bpDoAluno = 1_000_000;

		// NINGUEM MAIS NO CONE: o `AlvoNaFrente` devolve o MAIS PROXIMO, e um terceiro corpo por
		// perto faria esta familia medir socos em quem nao e o mestre.
		Estacionar(mestre, 100);

		// SEM VINCULO -- o controle.
		Desvincular(aluno.Assinatura, "bancada: medindo o ganho sem mestre");
		PorBp(mestre, bpDoAluno * 3);
		Soco sem = GanhoDeUmSoco(aluno, mestre, bpDoAluno);

		// COM VINCULO, mestre 3x mais forte: o teto.
		Vincular(mestre, aluno);
		Soco com = GanhoDeUmSoco(aluno, mestre, bpDoAluno);

		// ============================ O SOCO TEM QUE TER ACERTADO ============================
		// Socar o AR tambem rende BP (`GameServer.Combat.cs:249`), so que sem multiplicador nenhum --
		// entao uma bancada que nao confere o alvo mede dois socos no ar, acha os dois iguais e chama
		// isso de "o bonus nao chegou". Foi literalmente a primeira leitura vermelha desta familia.
		// ================================================================================
		AfirmarMv("os dois socos ACERTARAM o mestre (socar o ar tambem rende BP, e sem bonus nenhum)",
				  sem.Acertou && com.Acertou);
		AfirmarMv("um soco no mestre rende BP de verdade (o ganho nao morre no CapCheck)",
				  sem.Ganho > 0 && com.Ganho > 0, $"sem {sem.Ganho:0.######} | com {com.Ganho:0.######}");

		double razao = sem.Ganho > 0 ? com.Ganho / sem.Ganho : 0;
		AfirmarMv($"...e treinar contra o PROPRIO MESTRE rende {GainKnobs.MasterGainCap:0}x "
				+ "mais BP que treinar contra o mesmo corpo sem vinculo",
				  Math.Abs(razao - GainKnobs.MasterGainCap) < 0.02,
				  $"razao medida {razao:0.###} (mult {sem.Mult:0.##} -> {com.Mult:0.##})");

		// O PISO: mestre 1,1x mais forte nao paga nada (abaixo de `MasterGainFloor`), e 1,2x paga.
		PorBp(mestre, bpDoAluno * 1.1);
		Soco fraco = GanhoDeUmSoco(aluno, mestre, bpDoAluno);
		AfirmarMv($"mestre so 1,1x mais forte (abaixo do piso de {GainKnobs.MasterGainFloor:0.0}x) nao "
				+ "paga bonus nenhum",
				  fraco.Acertou && Math.Abs(fraco.Ganho / sem.Ganho - 1) < 0.02,
				  $"razao {fraco.Ganho / sem.Ganho:0.###}");

		PorBp(mestre, bpDoAluno * GainKnobs.MasterGainFloor);
		Soco noPiso = GanhoDeUmSoco(aluno, mestre, bpDoAluno);
		AfirmarMv($"...e no piso EXATO ({GainKnobs.MasterGainFloor:0.0}x) o bonus ja vale",
				  noPiso.Acertou
				  && Math.Abs(noPiso.Ganho / sem.Ganho - GainKnobs.MasterGainFloor) < 0.02,
				  $"razao {noPiso.Ganho / sem.Ganho:0.###}");

		// E O GANHO FICA NA FAIXA QUE A SPEC PEDE, seja qual for a forca do mestre.
		PorBp(mestre, bpDoAluno * 50);
		Soco gigante = GanhoDeUmSoco(aluno, mestre, bpDoAluno);
		double razaoGigante = gigante.Ganho / sem.Ganho;
		AfirmarMv("com o mestre 50x mais forte o ganho PARA no teto -- o bonus vive entre "
				+ $"{GainKnobs.MasterGainFloor:0.0}x e {GainKnobs.MasterGainCap:0}x",
				  gigante.Acertou && razaoGigante >= GainKnobs.MasterGainFloor - 0.02
				  && razaoGigante <= GainKnobs.MasterGainCap + 0.02,
				  $"razao {razaoGigante:0.###}");

		// A ASSIMETRIA: o bonus e de quem APRENDE. O mestre batendo no aluno cai no teto geral.
		PorBp(mestre, bpDoAluno * 3);
		Soco doMestre = GanhoDeUmSoco(mestre, aluno, bpDoAluno * 3);
		Soco doAluno = GanhoDeUmSoco(aluno, mestre, bpDoAluno);
		AfirmarMv("o vinculo e ASSIMETRICO: o aluno ganha 3x batendo no mestre, e o mestre bate no "
				+ "aluno sem bonus nenhum",
				  EhMeuMestre(aluno, mestre) && !EhMeuMestre(mestre, aluno)
				  && Math.Abs(doAluno.Mult - GainKnobs.MasterGainCap) < 1e-9
				  && Math.Abs(doMestre.Mult - 1) < 1e-9,
				  $"aluno x{doAluno.Mult:0.##} ({doAluno.Ganho:0.######}) | "
				  + $"mestre x{doMestre.Mult:0.##} ({doMestre.Ganho:0.######})");
	}

	// =====================================================================
	// 4) A TRANSFORMACAO ASSISTIDA CORTA O REQUISITO PELA METADE
	// =====================================================================
	/// <summary>
	/// `MST_HALF` (`1A Defines.dm:29`), aplicado no passo 8 do <see cref="EstadoDeForma.Avaliar"/>.
	///
	/// ============================ O MESMO PERSONAGEM, COM E SEM MESTRE ============================
	/// O pedido e literal: *"meca o limiar pessoal com e sem mestre, no MESMO personagem"*. E tem que
	/// ser o mesmo corpo porque o limiar e SORTEADO por personagem (`rand(9,13)/10` do
	/// `statsaiyan.dm:50-56`) -- medir num e no outro compararia dois sorteios diferentes e a metade
	/// se perderia no ruido.
	///
	/// A FORMA ALVO E DERIVADA, e nao escrita a mao: a classe deste Saiyajin foi SORTEADA no
	/// nascimento, e cravar `"ssj1"` daria uma bancada que reprova por sorteio -- que e pior que
	/// bancada nenhuma, porque ensina a ignorar a cor vermelha.
	/// ======================================================================================
	/// </summary>
	private void AMetadeDaPorta(ServerPlayer mestre, ServerPlayer aluno)
	{
		GD.Print("[mestrevivo] -- 4) A PORTA PELA METADE (o mesmo personagem, com e sem mestre)");

		GarantirVinculoVivo(mestre, aluno);
		aluno.Forma.Entrar(Catalogo.IdBase);
		aluno.Forma.PortasCortadas.Clear();
		aluno.FuriaExtremaAte = aluno.RaivaLendariaAte = 0;

		FormaDef? alvo = PrimeiroDegrauEnsinavel(aluno);
		if (alvo == null)
		{
			AfirmarMv("ha um degrau ensinavel ao alcance deste aluno (a classe foi sorteada)", false,
					  $"{aluno.Race}/{aluno.Class}");
			return;
		}

		double porta = PortaDeTeste(aluno, alvo.Id);
		AfirmarMv($"o degrau alvo saiu da regra e nao de um id escrito a mao: {alvo.Id} "
				+ $"(porta pessoal {porta:N0})",
				  porta > 0 && Discipulado.EhEnsinavel(alvo.Id));

		// O ALUNO FICA ENTRE A METADE E A PORTA INTEIRA -- a unica faixa em que a assistida decide
		// alguma coisa. Abaixo da metade ela nao ajuda; acima da porta ela nao e necessaria.
		PorBp(aluno, porta * 0.6);
		aluno.Ficha.Ki = aluno.Ficha.MaxKi;

		PerfilDeFormas perfil = Perfil(aluno);
		AfirmarMv("SEM mestre, com 60% da porta pessoal, o degrau e recusado por PODER",
				  aluno.Forma.Avaliar(alvo.Id, aluno.Ficha.BP, 1, false, perfil) == RecusaForma.SemPoder,
				  $"BP {aluno.Ficha.BP:N0} de {porta:N0}");
		AfirmarMv("COM o corte pela metade, a unica pendencia que sobra e a RAIVA "
				+ "(prova de que o corte entra no FUNIL, e nao como desconto depois)",
				  aluno.Forma.Avaliar(alvo.Id, aluno.Ficha.BP, 1, false, perfil,
									  Discipulado.FatorAssistido) == RecusaForma.SemFuria);

		// A TESTEMUNHA, PELO CAMINHO DE VERDADE: o mestre assume a forma na frente do aluno, e e o
		// laco do `AnunciarForma` que escreve o "eu vi" (`mst_note_form`, `:207`).
		aluno.FormasVistas.Clear();
		Encostar(mestre, aluno, tiles: 3);
		EntrarNaForma(mestre, alvo);
		AfirmarMv("o aluno VIU a forma no mestre (o pre-requisito do ensino, escrito pelo anuncio)",
				  aluno.FormasVistas.Contains(alvo.IdRede));

		// E AGORA O GESTO INTEIRO, pelos verbos.
		mestre.RecargaDeEnsino = 0;
		Encostar(mestre, aluno, tiles: 1);
		long raivaAntes = aluno.FuriaExtremaAte;
		UsarVerboDeMestre(mestre, $"mst_ensinar:{alvo.Id}");
		AfirmarMv("a oferta de despertar sai pelo verb", aluno.PedidoDoMestre is { Despertar: true });
		UsarVerboDeMestre(aluno, "mst_aceitar");

		AfirmarMv($"O ALUNO DESPERTA {alvo.Id} com 60% da porta PESSOAL dele -- a metade do MST_HALF",
				  aluno.Forma.Atual == alvo.Id,
				  $"{aluno.Forma.Atual}, BP {aluno.Ficha.BP:N0} de {porta:N0}");
		AfirmarMv("...e a raiva foi acesa pela provocacao (a forma nasce dela)",
				  aluno.FuriaExtremaAte > raivaAntes || Catalogo.RaivaExigida(alvo) == NivelDeRaiva.Nenhuma);
		AfirmarMv("...e o corte ficou ANOTADO no personagem (sem isto ele nao reentra na propria forma)",
				  aluno.Forma.PortasCortadas.Contains(alvo.IdRede));

		// A REENTRADA -- o bug que o `mst_form_apply` (`:358-360`) descreve.
		aluno.Forma.Entrar(Catalogo.IdBase);
		aluno.FuriaExtremaAte = aluno.RaivaLendariaAte = 0;
		AfirmarMv("depois de voltar a base ele REENTRA sozinho, sem mestre e sem raiva",
				  aluno.Forma.Avaliar(alvo.Id, aluno.Ficha.BP, 1, false, Perfil(aluno)) == RecusaForma.Pode);

		// ============================ A METADE E METADE, E NAO "UM DESCONTO" ============================
		// Com 49% da porta a assistida FALHA. Sem esta checagem, um `fatorDaPorta` de 0,1 (ou uma
		// porta simplesmente ignorada quando ha mestre) passaria verde em tudo o que esta acima.
		// ========================================================================================
		var virgem = new EstadoDeForma { Limiares = aluno.Forma.Limiares };
		EstadoDeForma guardado = aluno.Forma;
		try
		{
			aluno.Forma = virgem;
			PorBp(aluno, porta * 0.49);
			aluno.Ficha.Ki = aluno.Ficha.MaxKi;
			aluno.FuriaExtremaAte = aluno.RaivaLendariaAte = 0;
			aluno.FormasVistas.Add(alvo.IdRede);
			mestre.RecargaDeEnsino = 0;
			Encostar(mestre, aluno, tiles: 1);

			UsarVerboDeMestre(mestre, $"mst_ensinar:{alvo.Id}");
			UsarVerboDeMestre(aluno, "mst_aceitar");
			AfirmarMv("com 49% da porta (um fio abaixo da metade) o despertar assistido FALHA",
					  aluno.Forma.Atual == Catalogo.IdBase, aluno.Forma.Atual);
			AfirmarMv("...e a tentativa fadada nao da raiva de graca (a raiva e o ULTIMO passo do Avaliar)",
					  aluno.FuriaExtremaAte == 0 && aluno.RaivaLendariaAte == 0);

			// E NA BORDA EXATA (50%) ela acontece.
			PorBp(aluno, porta * Discipulado.FatorAssistido);
			aluno.Ficha.Ki = aluno.Ficha.MaxKi;
			mestre.RecargaDeEnsino = 0;
			UsarVerboDeMestre(mestre, $"mst_ensinar:{alvo.Id}");
			UsarVerboDeMestre(aluno, "mst_aceitar");
			AfirmarMv($"na METADE EXATA ({Discipulado.FatorAssistido:0.0}x da porta) ela acontece",
					  aluno.Forma.Atual == alvo.Id, aluno.Forma.Atual);
		}
		finally
		{
			aluno.Forma = guardado;
			aluno.Forma.Entrar(Catalogo.IdBase);
			PorBp(aluno, porta * 0.6);
		}

		mestre.Forma.Entrar(Catalogo.IdBase);
		AplicarForma(mestre);
	}

	// =====================================================================
	// 5) AS DUAS RECARGAS, E ELAS SAO DE DOIS SISTEMAS
	// =====================================================================
	/// <summary>
	/// A de 5 min e do DISCIPULADO (`MST_TEACH_COOLDOWN`, 3000 tiques do BYOND); a de 6 s e do
	/// ENSINO DE SKILL (`sleep(60)` do `Teach_Skill`). Sao dois relogios de dois sistemas que no DM
	/// nao se tocam em linha nenhuma -- e a checagem que importa e justamente essa: **uma nao
	/// consome a outra**.
	/// </summary>
	private void AsDuasRecargas(ServerPlayer mestre, ServerPlayer aluno, ServerPlayer terceiro)
	{
		GD.Print("[mestrevivo] -- 5) AS DUAS RECARGAS (5 min do discipulado, 6 s do ensino)");

		GarantirVinculoVivo(mestre, aluno);
		Estacionar(terceiro, 160);
		Encostar(mestre, aluno, tiles: 1);
		mestre.RecargaDeEnsino = 0;
		mestre.RecargaDaLicao = 0;
		aluno.PedidoDoMestre = null;

		FormaDef? alvo = PrimeiroDegrauEnsinavel(aluno) ?? Catalogo.Def("ssj1");
		if (alvo == null) { AfirmarMv("ha forma pra medir a recarga do discipulado", false); return; }
		aluno.FormasVistas.Add(alvo.IdRede);

		UsarVerboDeMestre(mestre, $"mst_ensinar:{alvo.Id}");
		double segundos = (mestre.RecargaDeEnsino - NowMs()) / 1000.0;
		AfirmarMv($"a recarga do discipulado e de {Discipulado.RecargaDeEnsinoSegundos / 60:0} min "
				+ "e comeca NO GESTO, antes da resposta do aluno (`:510`)",
				  Math.Abs(segundos - Discipulado.RecargaDeEnsinoSegundos) < 2, $"{segundos:0} s");

		aluno.PedidoDoMestre = null;
		UsarVerboDeMestre(mestre, $"mst_ensinar:{alvo.Id}");
		AfirmarMv("...e a segunda tentativa dentro da recarga e recusada",
				  aluno.PedidoDoMestre == null);

		// ============================ AS DUAS SAO DE SISTEMAS DIFERENTES ============================
		// O mestre acabou de gastar os 5 minutos do discipulado. Ele TEM que continuar podendo dar
		// uma licao de skill no mesmo instante -- no DM o `Teach_Skill` nao consulta o `mst_list` nem
		// o `mst_teach_cd` em linha nenhuma. Amarrar os dois relogios seria inventar regra.
		// ======================================================================================
		Skill? skill = AlvoDesligado();
		if (skill == null) { AfirmarMv("ha skill ensinavel pra medir a recarga de 6 s", false); return; }

		mestre.Livro.Dar(skill.Path);
		aluno.Livro.Conceder(50);
		Encostar(mestre, aluno, tiles: 1);
		bool licao = Licao(mestre, aluno, skill);
		AfirmarMv("com os 5 min do discipulado gastos, o mestre AINDA ensina uma skill: "
				+ "sao dois sistemas e dois relogios",
				  licao && mestre.RecargaDeEnsino > NowMs());

		double seg6 = (mestre.RecargaDaLicao - NowMs()) / 1000.0;
		AfirmarMv($"a recarga do ensino de skill e de {EnsinoDeSkill.RecargaSegundos:0} s "
				+ "(3000 tiques seriam 5 min, e isso e o outro sistema)",
				  Math.Abs(seg6 - EnsinoDeSkill.RecargaSegundos) < 1, $"{seg6:0.#} s");

		Skill? outra = OutroAlvo(skill);
		if (outra != null)
		{
			mestre.Livro.Dar(outra.Path);
			AfirmarMv("...e a segunda licao no mesmo instante e recusada pela recarga de 6 s",
					  !Licao(mestre, aluno, outra) && !aluno.Livro.Sabe(outra.Path));
			mestre.RecargaDaLicao = NowMs() - 1;
			AfirmarMv("...e passada a recarga ela acontece", Licao(mestre, aluno, outra));
		}

		// E O CONTRARIO TAMBEM: a licao de skill nao gastou os 5 min do discipulado.
		AfirmarMv("e ensinar skill NAO consumiu a recarga do discipulado (o inverso do teste de cima)",
				  mestre.RecargaDeEnsino > NowMs());
	}

	// =====================================================================
	// 6) O ALUNO NAO REPASSA -- E O CONTRA-EXEMPLO
	// =====================================================================
	/// <summary>
	/// `nS.teacher = FALSE ; nS.wastaught = TRUE` (`teachable.dm:53`). A regra central do ensino de
	/// skill, e ela precisa de TRES corpos: com dois, o segundo elo da corrente nao existe pra ser
	/// recusado.
	///
	/// E precisa dos dois sentidos: um jogo em que NINGUEM ensina nada passaria na metade negativa
	/// sozinho.
	/// </summary>
	private void OAlunoNaoRepassa(ServerPlayer mestre, ServerPlayer aluno, ServerPlayer terceiro)
	{
		GD.Print("[mestrevivo] -- 6) O ALUNO NAO REPASSA (e o mestre repassa)");

		Skill? alvo = AlvoDesligado();
		if (alvo == null) { AfirmarMv("ha skill ensinavel pra montar a corrente", false); return; }

		// LIVROS DE VERDADE, so limpos do que as familias anteriores deixaram.
		foreach (ServerPlayer p in new[] { mestre, aluno, terceiro })
		{
			p.Livro.Esquecer(alvo.Path);
			p.RecargaDaLicao = 0;
			p.PedidoDeLicao = null;
			p.Livro.Conceder(50);
		}
		mestre.Livro.Dar(alvo.Path);

		Estacionar(terceiro, 400);
		Encostar(mestre, aluno, tiles: 1);
		AfirmarMv("A ORIGEM ENSINA: o mestre passa a skill ao aluno", Licao(mestre, aluno, alvo));
		AfirmarMv("   ...e a copia do aluno entrou marcada como ENSINADA (`wastaught = TRUE`)",
				  aluno.Livro.FoiEnsinada(alvo.Path));

		Estacionar(mestre, 100);
		Encostar(aluno, terceiro, tiles: 1);
		aluno.RecargaDaLicao = 0;
		AfirmarMv("O ALUNO NAO REPASSA: a corrente para no primeiro elo",
				  !Licao(aluno, terceiro, alvo) && !terceiro.Livro.Sabe(alvo.Path));
		AfirmarMv("   ...e a recusa e a especifica (`AprendeuDeAlguem`, e nao 'nao sabe' nem 'longe')",
				  EnsinoDeSkill.Avaliar(aluno.Livro, terceiro.Livro, alvo, true, true, false, 1, false)
				  == RecusaDeLicao.AprendeuDeAlguem);

		// O CONTRA-EXEMPLO: quem trava e a COPIA do aluno, nao a skill.
		Estacionar(aluno, 130);
		Encostar(mestre, terceiro, tiles: 1);
		mestre.RecargaDaLicao = 0;
		AfirmarMv("mas o MESTRE ainda repassa a mesma skill a um terceiro -- quem trava e a copia, "
				+ "nao a skill",
				  Licao(mestre, terceiro, alvo) && terceiro.Livro.Sabe(alvo.Path));

		AfirmarMv("e a lista que a interface le concorda com as duas coisas",
				  !EnsinoDeSkill.Repassaveis(_skills!, aluno.Livro).Any(s => s.Path == alvo.Path)
				  && EnsinoDeSkill.Repassaveis(_skills!, mestre.Livro).Any(s => s.Path == alvo.Path));

		Estacionar(terceiro, 160);
		Estacionar(aluno, 130);
		Estacionar(mestre, 100);
	}

	// =====================================================================
	// 7) ULTRA INSTINTO E EGO: FECHADOS AQUI, VIVOS NA CADEIA DELES
	// =====================================================================
	/// <summary>
	/// AS DUAS METADES NA MESMA SECAO, e isso e o pedido e tambem a unica forma honesta de fazer:
	/// *"os dois juntos, senao 'ninguem ensina nada' passaria verde"*.
	///
	/// O discipulado exclui as duas disciplinas porque elas TEM ensino proprio, em cadeia, com raiz
	/// no cargo (`GameServer.Disciplinas.EnsinarDisciplina`). Provar so a exclusao seria provar que
	/// uma porta esta fechada sem olhar se a outra existe.
	/// </summary>
	private void AsDuasDisciplinas(ServerPlayer mestre, ServerPlayer aluno, ServerPlayer terceiro)
	{
		GD.Print("[mestrevivo] -- 7) UI E EGO: RECUSADOS AQUI, ENSINADOS PELA CADEIA");

		// --- A PORTA FECHADA (o discipulado) ---
		string[] disciplinares = ["ui_sign", "ui_perfected", "destroyer", "ultra_ego"];
		AfirmarMv("nenhuma forma de Ultra Instinto ou Ultra Ego e ensinavel pelo discipulado",
				  disciplinares.All(id => !Discipulado.EhEnsinavel(id)),
				  string.Join(",", disciplinares.Where(Discipulado.EhEnsinavel)));

		GarantirVinculoVivo(mestre, aluno);
		Encostar(mestre, aluno, tiles: 1);
		foreach (string id in disciplinares)
		{
			mestre.RecargaDeEnsino = 0;
			aluno.PedidoDoMestre = null;
			UsarVerboDeMestre(mestre, $"mst_ensinar:{id}");
			AfirmarMv($"   e pelo VERB tambem: `mst_ensinar:{id}` nao oferece nada",
					  aluno.PedidoDoMestre == null);
			AfirmarMv("   ...e a recarga de 5 min nao foi gasta numa oferta que nem saiu",
					  mestre.RecargaDeEnsino == 0);
		}

		// --- A PORTA ABERTA (a cadeia das disciplinas) ---
		// O mestre e a raiz da cadeia (no jogo ele seria o Anjo ou o Deus da Destruicao pelo cargo);
		// os dois alunos tem o ki divino que o `GODKI_UIUE_LEARN_PCT` exige. Sao DOIS alunos porque
		// as duas disciplinas se EXCLUEM no mesmo corpo.
		mestre.UltraInstinct.Aprendida = true;
		mestre.PoderDaDestruicao.Aprendida = true;
		foreach (ServerPlayer p in new[] { aluno, terceiro })
		{
			p.Ficha.godki = new GodKiState { awakened = true, mastery = Disciplinas.GodKiParaAprender };
			p.UltraInstinct.Aprendida = false;
			p.PoderDaDestruicao.Aprendida = false;
		}

		Estacionar(terceiro, 400);
		Encostar(mestre, aluno, tiles: 1);
		UsarDisciplina(mestre, "ui_ensinar");
		AfirmarMv("O ULTRA INSTINTO CONTINUA SENDO ENSINADO -- pela cadeia propria dele, "
				+ "e no mesmo par de corpos que o discipulado acabou de recusar",
				  aluno.UltraInstinct.Aprendida);

		Estacionar(aluno, 130);
		Encostar(mestre, terceiro, tiles: 1);
		UsarDisciplina(mestre, "ue_ensinar");
		AfirmarMv("...e o PODER DA DESTRUICAO tambem, no outro corpo (as duas se excluem no mesmo)",
				  terceiro.PoderDaDestruicao.Aprendida);

		AfirmarMv("AS DUAS METADES JUNTAS: a porta do discipulado esta fechada E a cadeia esta viva "
				+ "(uma sozinha nao prova nada)",
				  disciplinares.All(id => !Discipulado.EhEnsinavel(id))
				  && aluno.UltraInstinct.Aprendida && terceiro.PoderDaDestruicao.Aprendida);

		Estacionar(terceiro, 160);
	}

	// =====================================================================
	// 8) O RELOGIN: O QUE ATRAVESSA O DISCO
	// =====================================================================
	/// <summary>
	/// QUATRO COISAS, EM QUATRO LUGARES DIFERENTES, e cada uma some sozinha e calada:
	///
	///   * o VINCULO -- `mestres.txt`, que e do MUNDO (mesmo ciclo de vida do `cargos.txt`);
	///   * a PORTA CORTADA -- `CharacterSave.PortasCortadas`, que e do PERSONAGEM;
	///   * a RECARGA de 5 min -- `CharacterSave.MestreRecargaAte` (`mst_teach_cd`, e o DM diz em voz
	///     alta que *relogar nao zera*);
	///   * o `wastaught` -- `CharacterSave.SkillsEnsinadas`, sem o qual o jeito de repassar uma skill
	///     rara e DESLOGAR.
	///
	/// A ida e a volta sao o caminho de producao inteiro: `Persistir` (o mesmo que o jogo chama),
	/// `_store.Carregar` e a sequencia do `Entrar` -- inclusive o `RestaurarFormaEDisciplina`, que e
	/// literalmente o bloco do login e nao uma copia dele.
	/// </summary>
	private void ORelogin(ServerPlayer mestre, ServerPlayer aluno, ServerPlayer terceiro)
	{
		GD.Print("[mestrevivo] -- 8) O RELOGIN (o vinculo, a porta cortada, a recarga e o wastaught)");

		GarantirVinculoVivo(mestre, aluno);
		Skill? ensinada = AlvoDesligado();
		Skill? propria = OutroAlvo(ensinada);
		if (ensinada == null || propria == null)
		{
			AfirmarMv("ha duas skills pra separar 'ensinada' de 'propria' no save", false);
			return;
		}

		// O ESTADO QUE TEM QUE ATRAVESSAR. Tudo ja aconteceu nas familias anteriores; aqui so se
		// garante que ele existe ANTES do save -- senao a familia 8 estaria provando que o disco
		// guarda um vazio.
		if (!aluno.Livro.FoiEnsinada(ensinada.Path))
		{
			mestre.Livro.Dar(ensinada.Path);
			mestre.RecargaDaLicao = 0;
			Encostar(mestre, aluno, tiles: 1);
			Licao(mestre, aluno, ensinada);
		}
		// A DE CONTROLE: ele TEM a skill e ninguem lhe ensinou. O `Esquecer` vem antes porque a
		// familia 5 usou esta mesma skill numa LICAO -- e sem limpar a marca, o "contra-exemplo"
		// seria outra skill ensinada e a checagem passaria a afirmar o contrario do que promete.
		aluno.Livro.Esquecer(propria.Path);
		aluno.Livro.Dar(propria.Path);
		mestre.RecargaDeEnsino = NowMs() + 200_000;

		string sigMestre = mestre.Assinatura, sigAluno = aluno.Assinatura;
		int cortesAntes = aluno.Forma.PortasCortadas.Count;
		int vistasAntes = aluno.FormasVistas.Count;
		long recargaAntes = mestre.RecargaDeEnsino;

		AfirmarMv("preparo: antes do relogin o aluno tem porta cortada, forma vista e skill ensinada",
				  cortesAntes > 0 && vistasAntes > 0 && aluno.Livro.FoiEnsinada(ensinada.Path),
				  $"cortes {cortesAntes} | vistas {vistasAntes}");

		// --- SAI DO MUNDO (o `Persistir` do jogo, e o corpo some) ---
		foreach (ServerPlayer p in new[] { mestre, aluno, terceiro })
		{
			PersistirNaBancada(p);
			Recolher(p);
		}

		// --- O MUNDO ESQUECE TUDO O QUE ESTAVA NA MEMORIA ---
		// O `mestres.txt` e RELIDO do disco: sem esta limpeza a familia inteira estaria conferindo o
		// dicionario que nunca saiu da RAM.
		_mestreDe.Clear();
		_nomePorAssinatura.Clear();
		CarregarMestres();

		AfirmarMv("O VINCULO ATRAVESSA O DISCO, por assinatura (o savefile `MASTER` do DM)",
				  MestreDe(sigAluno) == sigMestre, $"'{MestreDe(sigAluno)}'");
		AfirmarMv("...e o NOME do mestre volta junto (o `mst_names`, pro painel de quem esta so)",
				  NomeDaAssinatura(sigMestre) == mestre.Name, NomeDaAssinatura(sigMestre));

		// --- VOLTA AO MUNDO, pelo caminho do `Entrar` ---
		ServerPlayer mestreVolta = Logar(mestre.Conta, mestre.Slot, 100);
		ServerPlayer alunoVolta = Logar(aluno.Conta, aluno.Slot, 130);

		try
		{
			AfirmarMv("o corpo que voltou e o MESMO personagem (a assinatura e conta+slot)",
					  alunoVolta.Assinatura == sigAluno && mestreVolta.Assinatura == sigMestre);
			AfirmarMv("...e ele continua sendo aluno do mesmo mestre depois de voltar",
					  EhMeuMestre(alunoVolta, mestreVolta) && !EhMeuMestre(mestreVolta, alunoVolta));

			AfirmarMv("A PORTA CORTADA atravessa o logout (sem isto ele desperta com o mestre e "
					+ "volta sem conseguir reentrar na propria forma)",
					  alunoVolta.Forma.PortasCortadas.Count == cortesAntes,
					  $"{alunoVolta.Forma.PortasCortadas.Count} de {cortesAntes}");

			// E O CORTE VALE DE VERDADE DEPOIS DA VOLTA, e nao so como numero no `HashSet`.
			FormaDef? cortada = Catalogo.Todas.FirstOrDefault(
				d => alunoVolta.Forma.PortasCortadas.Contains(d.IdRede));
			if (cortada != null)
			{
				double porta = PortaDeTeste(alunoVolta, cortada.Id);
				PorBp(alunoVolta, porta * 0.6);
				alunoVolta.Ficha.Ki = alunoVolta.Ficha.MaxKi;
				alunoVolta.Forma.Entrar(Catalogo.IdBase);
				AfirmarMv($"   ...e ele REENTRA em {cortada.Id} com 60% da porta depois do relogin",
						  alunoVolta.Forma.Avaliar(cortada.Id, alunoVolta.Ficha.BP, 1, false,
												   Perfil(alunoVolta)) == RecusaForma.Pode);
			}

			AfirmarMv("A FORMA VISTA atravessa o logout (o `mst_seen_forms`)",
					  alunoVolta.FormasVistas.Count == vistasAntes,
					  $"{alunoVolta.FormasVistas.Count} de {vistasAntes}");

			AfirmarMv("A RECARGA DE 5 MIN atravessa o logout -- relogar nao e um jeito de descansar "
					+ "(o `mst_teach_cd`)",
					  mestreVolta.RecargaDeEnsino == recargaAntes && mestreVolta.RecargaDeEnsino > NowMs(),
					  $"{(mestreVolta.RecargaDeEnsino - NowMs()) / 1000.0:0} s");

			AfirmarMv("O `wastaught` ATRAVESSA O LOGOUT: ele ainda SABE a skill...",
					  alunoVolta.Livro.Sabe(ensinada.Path) && alunoVolta.Livro.Sabe(propria.Path));
			AfirmarMv("...e continua NAO PODENDO repassa-la (senao o jeito de repassar seria deslogar)",
					  !EnsinoDeSkill.PodeRepassar(alunoVolta.Livro, ensinada));
			AfirmarMv("...e o contra-exemplo: a que e DELE ele repassa (o save nao marcou tudo)",
					  EnsinoDeSkill.PodeRepassar(alunoVolta.Livro, propria));

			// PELO VERB, pra fechar: o "nao repassa" depois do relogin nao e so um booleano.
			ServerPlayer terceiroVolta = Logar(terceiro.Conta, terceiro.Slot, 160);
			try
			{
				Estacionar(mestreVolta, 100);
				Encostar(alunoVolta, terceiroVolta, tiles: 1);
				alunoVolta.RecargaDaLicao = 0;
				terceiroVolta.Livro.Esquecer(ensinada.Path);
				terceiroVolta.Livro.Conceder(50);
				AfirmarMv("   ...e pelo VERB tambem: depois do relogin ele tenta ensinar e e recusado",
						  !Licao(alunoVolta, terceiroVolta, ensinada)
						  && !terceiroVolta.Livro.Sabe(ensinada.Path));
			}
			finally { Recolher(terceiroVolta); }
		}
		finally
		{
			Recolher(mestreVolta);
			Recolher(alunoVolta);
			// os corpos originais voltam pro mundo, pro `finally` da bancada recolher todo mundo igual
			_players[mestre.Id] = mestre;
			_players[aluno.Id] = aluno;
			_players[terceiro.Id] = terceiro;
			ZoneList(mestre.Zone.Hash).Add(mestre);
			ZoneList(aluno.Zone.Hash).Add(aluno);
			ZoneList(terceiro.Zone.Hash).Add(terceiro);
		}
	}

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// CRIA UM PERSONAGEM DE VERDADE E O POE NO MUNDO -- as duas metades do que o jogador faz.
	///
	/// A criacao e a do `CreateChar` sem o peer: a ficha passa pelo MESMO `ValidarFicha` que o
	/// cliente atravessa, o corpo nasce pelo `Nascer` (classe SORTEADA) e o limiar de forma e rolado
	/// pelo `LimiaresPessoais.Rolar` com a mesma semente de la.
	///
	/// A ZONA E ESCOLHIDA PELA BANCADA, e nao pelo berco, e isto e uma divergencia consciente: o
	/// berco de verdade pode ser um planeta GERADO, e ai o corpo nasce em orbita e precisa do laco de
	/// pouso (`--bercovivo`). O que esta bancada mede nao tem nada a ver com onde o corpo esta -- so
	/// precisa que os sete estejam JUNTOS num lugar sem ninguem conectado. E o mesmo estado de quem
	/// deslogou naquela zona: o save guarda onde voce estava.
	/// </summary>
	private ServerPlayer CriarELogar(string nome, string raca, int conta, int slot, ZoneKey zona,
									 double tilesX)
	{
		var ficha = new CharacterDraft
		{
			Name = nome,
			Race = raca,
			Planet = Array.Find(CharacterDraft.Planetas,
				p => Array.IndexOf(CharacterDraft.RacasDoPlaneta(p), raca) >= 0) ?? "Earth",
			Gender = "Male",
			Backstory = "Personagem de bancada, criado pela linha de comando para o teste do discipulado.",
		};
		ficha.Age = Math.Min(18, ficha.IdadeMaxima);

		// A LINHAGEM E OBRIGATORIA nas tres racas que escolhem (o `ValidarFicha` recusa sem ela), e
		// ela e a POOL de onde a classe sai -- a classe em si continua sendo SORTEADA pelo `Nascer`.
		// Isto e o mesmo que o caminho automatico do cliente faz (`Boot.AutoEscolher`).
		string[] linhagens = CharacterDraft.EscolhasDeClasse(raca);
		if (linhagens.Length > 0) ficha.ChosenClass = linhagens[0];

		// A MESMA PORTA DO JOGADOR. Uma bancada que pula o validador deixa de testar o validador --
		// e ja aconteceu neste projeto (a historia obrigatoria recusava toda bancada).
		string motivo = ValidarFicha(ficha);
		if (motivo.Length > 0) AfirmarMv($"a ficha de '{nome}' passa no validador do jogo", false, motivo);

		Fighter lutador = Nascer(ficha, nome);
		long nasceuEm = NowMs();
		var c = new CharacterSave
		{
			Nome = nome, Raca = raca, Planeta = ficha.Planet, Genero = ficha.Gender,
			Linhagem = ficha.ChosenClass, Idade = ficha.Age, Ficha = lutador,
			Historia = ficha.Backstory, Porte = ficha.Porte,
			CriadoEm = nasceuEm,
			SeedDoBerco = Bercos.SementeDoBerco(nome, nasceuEm),
			// OS LIMIARES SAO SORTEADOS AGORA, uma vez, com a semente do `CreateChar` (`:1902`). E o
			// que faz o SSJ de cada um custar diferente -- e o que a metade do mestre corta.
			Limiares = LimiaresPessoais.Rolar(raca, lutador.Class,
				Espaco.Misturar((ulong)nasceuEm, (ulong)slot, (ulong)nome.GetHashCode())),
			Zona = zona.Name, ZonaTipo = zona.Kind, ZonaSeed = zona.Seed,
			X = (float)(tilesX * ZoneCollision.TileSize), Y = 0,
		};

		string nomeDaConta = ContasDaBancadaViva[conta];
		AccountSave acc = _store!.Carregar(nomeDaConta)
			?? new AccountSave { Conta = nomeDaConta, CriadaEm = nasceuEm };
		acc.Slots[slot] = c;
		_store.Gravar(acc);

		// E DE NOVO A PURGA, como o `CreateChar` faz (`:1917`): a assinatura e `hash(conta, slot)` e
		// pode estar reciclada de uma rodada anterior.
		PurgarAssinatura(ServerPlayer.AssinaturaDe(nomeDaConta, slot));

		return Logar(nomeDaConta, slot, tilesX);
	}

	/// <summary>
	/// O LOGIN, sem o peer -- a sequencia do <see cref="Entrar"/> menos tudo o que vira pacote.
	///
	/// O pedaco que esta bancada existe pra vigiar (forma, limiares, `FormasVistas`,
	/// `PortasCortadas`, recarga de ensino e disciplina) NAO e reescrito aqui: e a chamada de
	/// <see cref="RestaurarFormaEDisciplina"/>, o mesmo metodo que o login de verdade usa. Ver o
	/// cabecalho dele pro porque de o bloco ter virado metodo.
	/// </summary>
	private ServerPlayer Logar(string conta, int slot, double tilesX)
	{
		AccountSave acc = _store!.Carregar(conta)!;
		CharacterSave c = acc.Slots[slot]!;

		var pl = new ServerPlayer
		{
			Id = IdBaseDoMestreVivo + _mvCorpos++,
			Peer = null,
			LastInputMs = NowMs(),
			Conta = conta,
			Slot = slot,
		};
		AccountStore.ParaJogador(c, pl);
		pl.Zone = new ZoneKey(c.ZonaTipo, c.Zona, c.ZonaSeed);
		pl.Berco = BercoDe(c);
		pl.Pos = new Vec2((float)(tilesX * ZoneCollision.TileSize), 0);
		pl.Facing = Facing.South;

		pl.Ficha.Statify();
		pl.SpeedStat = MoveRules.SpeedStatFrom(pl.Ficha.Espeed);
		PrepararCombate(pl, c);
		PrepararSkills(pl, c);
		PrepararCustomizadas(pl, c);
		pl.Niveis = new NiveisDeSkill();
		pl.Niveis.DoSave(c.Niveis);
		pl.Niveis.Aplicar(pl.Ficha);
		AplicarPoderes(pl);
		AplicarEfeitos(pl);
		RestaurarFormaEDisciplina(pl, c);

		_players[pl.Id] = pl;
		ZoneList(pl.Zone.Hash).Add(pl);
		AplicarGravidade(pl);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		return pl;
	}

	/// <summary>
	/// GRAVA O PERSONAGEM como o jogo grava. O <see cref="Persistir(ServerPlayer)"/> acha a conta
	/// pelo `Peer`, e um corpo de bancada nao tem um -- entao a conta vem do disco e o resto e a
	/// MESMA funcao, e nao uma copia dela.
	/// </summary>
	private void PersistirNaBancada(ServerPlayer pl)
	{
		if (_store?.Carregar(pl.Conta) is { } acc) Persistir(pl, acc);
	}

	/// <summary>
	/// O PRIMEIRO DEGRAU ENSINAVEL QUE ESTE CORPO ALCANCA -- derivado, porque a classe foi sorteada
	/// no nascimento.
	///
	/// O criterio e "com BP de sobra, o que falta e so a porta ou a raiva": e exatamente a pergunta
	/// que o <see cref="EscolherFormaDoEnsino"/> faz em producao, so que sem depender do BP atual do
	/// corpo (que a bancada ainda vai escolher, justamente em funcao desta resposta).
	/// </summary>
	private static FormaDef? PrimeiroDegrauEnsinavel(ServerPlayer pl)
	{
		PerfilDeFormas perfil = Perfil(pl);
		foreach (FormaDef d in Discipulado.Ensinaveis)
		{
			if (pl.Forma.Despertou(d.Id)) continue;
			double porta = PortaDeTeste(pl, d.Id);
			if (porta <= 0) continue;
			RecusaForma r = pl.Forma.Avaliar(d.Id, porta * 4, 1, false, perfil, Discipulado.FatorAssistido);
			if (r is RecusaForma.Pode or RecusaForma.SemFuria) return d;
		}
		return null;
	}

	/// <summary>
	/// POE O BP E RECALCULA. O `Tick` e o que reescreve `expressedBP` e `relBPmax` -- sem ele o
	/// `CapCheck` mediria o teto do BP anterior e o ganho sairia zero ou cortado.
	/// </summary>
	private static void PorBp(ServerPlayer pl, double bp)
	{
		pl.Ficha.BP = bp;
		pl.Ficha.Tick();
	}

	/// <summary>O que um soco de bancada rendeu: o BP ganho, o multiplicador em vigor, e se acertou.</summary>
	private readonly record struct Soco(double Ganho, double Mult, bool Acertou);

	/// <summary>
	/// UM SOCO PELO CAMINHO DE PRODUCAO (o mesmo <see cref="Atacar"/> do pacote do cliente), e quanto
	/// BP ele rendeu a quem bateu.
	///
	/// Os quatro zeramentos NAO sao conveniencia: o `AttackGain` soma o potencial escondido, o
	/// acumulador de ocio e o `BPBuffer` ao ganho do golpe, e os tres carregam de uma medida pra
	/// outra. Com eles vivos, a razao entre duas medidas nao seria o multiplicador do mestre --
	/// seria o multiplicador mais um resto que muda sozinho.
	///
	/// ============================ O PODER EXPRESSO E IGUALADO **DEPOIS** DO `PorBp` ============================
	/// `FightGainMult` tem DOIS bonus: o GERAL, que le o poder EXPRESSO, e o do mestre, que le o BP
	/// BASE -- e o resultado e o MAIOR dos dois. Igualar o expresso e o que faz o geral valer 1x e
	/// deixa o do mestre sozinho na medida.
	///
	/// **Depois** porque o `PorBp` chama `Tick`, e o `Tick` reescreve o expresso a partir do BP: com
	/// o `expressedBP` igualado antes, o `PorBp` desfazia a igualdade e a bancada media a soma dos
	/// dois bonus. E o tipo de erro que da um numero plausivel -- foi a segunda leitura vermelha
	/// desta familia.
	/// ======================================================================================================
	/// </summary>
	private Soco GanhoDeUmSoco(ServerPlayer quemBate, ServerPlayer alvo, double bpInicial)
	{
		PorBp(quemBate, bpInicial);
		quemBate.Ficha.expressedBP = alvo.Ficha.expressedBP = 1_000;
		quemBate.Ficha.hiddenpotential = 0;
		quemBate.Ficha.tmp_activ_gains = 0;
		quemBate.Ficha.BPBuffer = 0;
		quemBate.Combate.Recarga = 0;
		quemBate.AtaqueAte = 0;
		quemBate.UltimoAlvo = 0;
		quemBate.Ficha.Ki = quemBate.Ficha.MaxKi;

		// O MULTIPLICADOR EM VIGOR, lido pela MESMA expressao da linha de producao
		// (`GameServer.Combat.cs:330`) e no mesmo estado em que ela vai le-lo.
		double mult = quemBate.Ficha.FightGainMult(alvo.Ficha, EhMeuMestre(quemBate, alvo));

		Encostar(quemBate, alvo, tiles: 1);
		Atacar(quemBate, Jandirus.Net.Protocol.Golpe.Leve);
		return new Soco(quemBate.Ficha.BP - bpInicial, mult, quemBate.UltimoAlvo == alvo.Id);
	}

	/// <summary>
	/// GARANTE O VINCULO pra uma familia que quer medir OUTRA coisa -- pelos verbos quando da, e
	/// pelo <see cref="Vincular"/> quando o portao de BP nao esta no estado certo. E preparo, nao
	/// afirmacao: o caminho de verdade e provado na familia 1.
	/// </summary>
	private void GarantirVinculoVivo(ServerPlayer mestre, ServerPlayer aluno)
	{
		mestre.PedidoDoMestre = aluno.PedidoDoMestre = null;
		if (MestreDe(aluno.Assinatura) == mestre.Assinatura) return;
		if (MestreDe(aluno.Assinatura).Length > 0) Desvincular(aluno.Assinatura, "preparo de bancada");
		Vincular(mestre, aluno);
	}

	/// <summary>Apaga os arquivos das contas de bancada. Ver o cabecalho da classe.</summary>
	private void ApagarContasDaBancadaViva()
	{
		if (_store == null) return;
		foreach (string conta in ContasDaBancadaViva)
			try
			{
				string caminho = System.IO.Path.Combine(_store.Pasta, conta + ".json");
				if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
			}
			catch (Exception e) { GD.PushWarning($"[mestrevivo] nao apaguei '{conta}': {e.Message}"); }
	}
}
