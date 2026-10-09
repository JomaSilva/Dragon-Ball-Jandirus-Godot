using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// 13) A ESCADA DA GUARDA (`--embatekiteste`) -- quanto dano as MAOS seguram no embate de guarda.
///
/// ============================ O QUE SE MEDE ============================
/// O embate de guarda da familia 6 (`TentarEmbateDeGuarda`, que e deste port: o DM so disputa feixe contra feixe),
/// entre dois IGUAIS, com o dano que o raio faria atraves da guarda varrido do corte dos fracos
/// (`DanoDeKi.CorteDoFraco`) pra cima, e cada lado respondendo ao quick time event de um jeito: o jogador que
/// acerta toda letra no tique em que ela nasce, o jogador de 450 ms, quem nao tem teclado (a taxa do
/// `ApertosDeQuemNaoTemTeclado`, inteligencia 50) e quem tem teclado e nao aperta. Sai uma tabela -- desfecho e
/// segundos de disputa por dano -- lida do estado de producao: de quem e o feixe no fim, o `Corridos` e o `Medidor`.
///
/// A ESCALA (dono, 2026-10-08): as maos se medem pelo dano. A razao da disputa e o dano que o raio faria atraves da
/// guarda sobre `EmbateDeKi.DanoQueAsMaosEmpatam` (20); ver `EmbateDeKi.PoderDeSegurar`. A familia 3 mede a mesma
/// fisica de medidor com dois FEIXES (a escada de 1x a 6x); esta mede o lado sem feixe.
///
/// ============================ O QUE SE AFIRMA, E O QUE SO SE LE ============================
/// A escala (a vantagem com que a disputa abre e dano sobre o que as maos empatam, presa entre 1/6 e 6), as duas
/// pontas (o raio mais fraco que abre embate -- o do corte -- e segurado e devolvido; no teto da vantagem quem
/// segura perde) e a forma (em cada coluna um raio mais forte nunca e MELHOR pra guarda). O MEIO da tabela -- ate
/// que dano cada coluna ainda vence -- cai das constantes do `BeamClash.dm` e do prazo, e se LE: cravar a medida
/// de hoje seria escreve-la como regra (a disciplina da escada da familia 3).
///
/// O CONTRA-EXEMPLO E O JOGO DE ANTES: com `EmbateDeKi.MaosPelaDeflexaoDeTeste` as maos voltam a valer o numerador
/// da chance de deflexao sobre 100 (`objects.dm:333`), e o raio do corte ja nasce no teto e engole quem segura.
///
/// A FICHA NAO ANDA nesta bancada (ninguem roda o `Ficha.Tick`, a mesma nota das familias 11 e 12): o dano fica
/// parado do comeco ao fim da disputa. Em jogo o Ki que os dois pagam mexe no `expressedBP` dos dois.
/// ================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Como um lado da disputa responde ao quick time event, nas cenas da escada da guarda.</summary>
	private enum MaoNaEscada
	{
		/// <summary>Tem teclado e nao aperta nada: por ele so a deriva trabalha.</summary>
		Parado,

		/// <summary>Nao tem teclado: a taxa do `ApertosDeQuemNaoTemTeclado`, com a inteligencia padrao (50).</summary>
		Npc,

		/// <summary>Tem teclado e aperta toda letra, depois de olhar pra ela pelo tempo de reacao pedido.</summary>
		Jogador,
	}

	/// <summary>
	/// O que UM embate de guarda da escada deu. `Dano` e o que o raio faria atraves da guarda no comeco da disputa
	/// (a chamada do `Acertar`: <see cref="DanoFinalDe"/>) e `VantagemDoFeixe` e a que a disputa usou (`LadoDeKi`).
	/// `Fim` e `Medidor` sao do ponto de vista de QUEM SEGURA: `Venci` = devolveu o ataque, 100 = as maos engoliram.
	/// `PeloMedidor` separa a disputa decidida pela fisica (ponta do medidor ou prazo) da que caiu por outra porta
	/// (`LadoOk`: Ki, guarda, canal).
	/// </summary>
	private readonly record struct GuardaMedida(
		bool Abriu, double Dano, double VantagemDoFeixe, DesfechoDeTeste Fim, double Segundos, double Medidor, bool PeloMedidor);

	private void AEscadaDaGuarda()
	{
		GD.Print("[embateki] -- 13) A ESCADA DA GUARDA: quanto dano as maos seguram (dano atraves da guarda x desfecho x segundos)");

		// RODA POR ULTIMO E REUSA O PRIMEIRO CORREDOR: sao cento e tantas cenas, e um corredor novo pra cada uma
		// (tres linhas de mapa por pedido) acabaria com o mapa. As outras familias ja tiraram os corpos delas, e
		// cada cena daqui limpa o que pos antes da seguinte.
		_pjProximoCorredor = 8;
		Vec2 chao = CorredorLivre(20);

		(string Nome, MaoNaEscada Guarda, double Reacao, MaoNaEscada Atirador)[] colunas =
		[
			("guarda PERFEITA (acerta toda letra) x atirador que nao aperta", MaoNaEscada.Jogador, 0, MaoNaEscada.Parado),
			("guarda PERFEITA x atirador NPC", MaoNaEscada.Jogador, 0, MaoNaEscada.Npc),
			("guarda PERFEITA x atirador perfeito", MaoNaEscada.Jogador, 0, MaoNaEscada.Jogador),
			("guarda NPC x atirador que nao aperta", MaoNaEscada.Npc, 0, MaoNaEscada.Parado),
			("guarda NPC x atirador NPC", MaoNaEscada.Npc, 0, MaoNaEscada.Npc),
			("guarda NPC x atirador perfeito", MaoNaEscada.Npc, 0, MaoNaEscada.Jogador),
			("guarda de 450 ms x atirador que nao aperta", MaoNaEscada.Jogador, 0.45, MaoNaEscada.Parado),
			("guarda de 450 ms x atirador NPC", MaoNaEscada.Jogador, 0.45, MaoNaEscada.Npc),
		];

		// AS RAZOES (dano sobre o que as maos empatam). A primeira e a do CORTE -- o raio mais fraco que abre embate
		// --, lida dos dois botoes; as outras sao as que ficam acima dela.
		double empata = EmbateDeKi.DanoQueAsMaosEmpatam;
		double noCorte = DanoDeKi.CorteDoFraco / empata;
		var razoes = new List<double> { noCorte };
		foreach (double r in new[] { 0.6, 0.75, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.25, 2.5, 3, 4, 6, 20, 165 })
			if (r > noCorte * 1.05) razoes.Add(r);

		var relogio = System.Diagnostics.Stopwatch.StartNew();
		var medidas = new GuardaMedida[razoes.Count, colunas.Length];
		int naoAbriu = 0, foraDoDano = 0, porOutraPorta = 0, foraDaEscala = 0;
		double piorDano = 0, piorEscala = 0;
		for (int r = 0; r < razoes.Count; r++)
			for (int c = 0; c < colunas.Length; c++)
			{
				GuardaMedida m = SegurarORaio(chao, razoes[r] * empata, colunas[c].Guarda, colunas[c].Atirador, colunas[c].Reacao);
				medidas[r, c] = m;
				if (!m.Abriu) { naoAbriu++; continue; }

				double desvioDoDano = Math.Abs(m.Dano / (razoes[r] * empata) - 1);
				piorDano = Math.Max(piorDano, desvioDoDano);
				if (desvioDoDano > 0.03) foraDoDano++;
				if (!m.PeloMedidor) porOutraPorta++;

				double esperada = Math.Clamp(razoes[r], 1 / EmbateDeKi.TetoDaVantagem, EmbateDeKi.TetoDaVantagem);
				double desvioDaEscala = Math.Abs(m.VantagemDoFeixe / esperada - 1);
				piorEscala = Math.Max(piorEscala, desvioDaEscala);
				if (desvioDaEscala > 0.03) foraDaEscala++;
			}
		relogio.Stop();
		int cenas = razoes.Count * colunas.Length;

		// ---- A TABELA ----
		for (int c = 0; c < colunas.Length; c++) GD.Print($"[embateki]       [{c + 1}] {colunas[c].Nome}");
		GD.Print("[embateki]       dano na guarda (razao) | [1] | [2] | [3] | [4] | [5] | [6] | [7] | [8]   "
				 + "(VENCE = quem segura devolveu o ataque; mNN = o medidor de quem segura quando foi o prazo que decidiu)");
		for (int r = 0; r < razoes.Count; r++)
		{
			var celulas = new string[colunas.Length];
			for (int c = 0; c < colunas.Length; c++) celulas[c] = CelulaDaEscada(medidas[r, c]);
			GD.Print($"[embateki]        {medidas[r, 0].Dano,7:0.#} ({razoes[r],6:0.###}x) | {string.Join(" | ", celulas)}");
		}
		GD.Print($"[embateki]       ({cenas} disputas em {relogio.ElapsedMilliseconds} ms de relogio; pior desvio do dano pedido "
				 + $"{piorDano * 100:0.####}%, da escala {piorEscala * 100:0.####}%)");

		// ---- ATE ONDE CADA COLUNA VENCE: o que se LE ----
		for (int c = 0; c < colunas.Length; c++)
		{
			double venceAte = double.NaN, perdeDesde = double.NaN;
			string empate = "";
			for (int r = 0; r < razoes.Count; r++)
			{
				GuardaMedida m = medidas[r, c];
				if (m.Fim == DesfechoDeTeste.Venci) venceAte = m.Dano;
				else if (m.Fim == DesfechoDeTeste.Empate) empate += $"{(empate.Length > 0 ? ", " : "")}{m.Dano:0.#}";
				else if (m.Fim == DesfechoDeTeste.Perdi && double.IsNaN(perdeDesde)) perdeDesde = m.Dano;
			}
			GD.Print($"[embateki]      (medido) [{c + 1}] {colunas[c].Nome}: vence ate {venceAte:0.#} de dano"
					 + (empate.Length > 0 ? $", empata em {empate}" : "") + $", perde a partir de {perdeDesde:0.#}");
		}

		// ---- O QUE SE AFIRMA ----
		AfirmarEk($"PREPARO: as {cenas} cenas da escada abriram embate de guarda com o dano pedido (a menos de 3%) e foram decididas pelo "
				  + "medidor ou pelo prazo -- ninguem ficou sem Ki nem largou o que segurava",
				  naoAbriu == 0 && foraDoDano == 0 && porOutraPorta == 0,
				  $"{naoAbriu} nao abriram, {foraDoDano} fora do dano (pior desvio {piorDano * 100:0.##}%), {porOutraPorta} por outra porta");

		AfirmarEk($"A ESCALA: a disputa abre com a vantagem do feixe em DANO NA GUARDA sobre o que as maos empatam ({empata}), presa entre "
				  + $"1/{EmbateDeKi.TetoDaVantagem} e {EmbateDeKi.TetoDaVantagem} -- {empata} de dano e 1 x 1, o dobro e 2 x 0,5",
				  naoAbriu == 0 && foraDaEscala == 0,
				  $"{foraDaEscala} de {cenas} cenas fora da escala (pior desvio {piorEscala * 100:0.#}%)");

		bool noCorteVence = true, noTetoPerde = true, desce = true, desceMesmo = true;
		string trilha = "";
		for (int c = 0; c < colunas.Length; c++)
		{
			trilha += $"[{c + 1}]";
			for (int r = 0; r < razoes.Count; r++)
			{
				DesfechoDeTeste fim = medidas[r, c].Fim;
				trilha += fim == DesfechoDeTeste.Venci ? "V" : fim == DesfechoDeTeste.Empate ? "E" : fim == DesfechoDeTeste.Perdi ? "P" : "-";
				if (r == 0) noCorteVence &= fim == DesfechoDeTeste.Venci;
				if (razoes[r] >= EmbateDeKi.TetoDaVantagem) noTetoPerde &= fim == DesfechoDeTeste.Perdi;
				if (r > 0) desce &= Degrau(fim) <= Degrau(medidas[r - 1, c].Fim);
			}
			desceMesmo &= Degrau(medidas[razoes.Count - 1, c].Fim) < Degrau(medidas[0, c].Fim);
			trilha += " ";
		}

		AfirmarEk($"NO CORTE QUEM SEGURA VENCE: o raio mais fraco que abre embate ({DanoDeKi.CorteDoFraco} de dano na guarda) e segurado e "
				  + "DEVOLVIDO -- jogador ou NPC, e ate contra um atirador que acerta toda letra",
				  noCorteVence, trilha);
		AfirmarEk($"NO TETO DA DESVANTAGEM ({EmbateDeKi.TetoDaVantagem}x o que as maos empatam, ou mais) QUEM SEGURA PERDE, em toda coluna -- "
				  + "por mais rapido que aperte",
				  noTetoPerde, trilha);
		AfirmarEk("A ESCADA DA GUARDA SO DESCE: um raio mais forte nunca e MELHOR pra quem segura "
				  + "(Venci >= Empate >= Perdi em toda coluna -- e desce mesmo)",
				  desce && desceMesmo, trilha);

		// ABAIXO DO CORTE NAO HA O QUE SEGURAR: o embate vem depois do corte dos fracos (passo 3a-bis do `Acertar`).
		GuardaMedida fraco = SegurarORaio(chao, DanoDeKi.CorteDoFraco * 0.99, MaoNaEscada.Jogador, MaoNaEscada.Parado, 0);
		AfirmarEk("ABAIXO DO CORTE NAO HA EMBATE: o raio que nao fere atraves da guarda nao abre disputa -- a escada comeca no corte",
				  !fraco.Abriu, $"abriu {fraco.Abriu}, dano {fraco.Dano:0.##}");

		// ---- O DEFEITO INJETADO: a escala de antes ----
		EmbateDeKi.MaosPelaDeflexaoDeTeste = true;
		try
		{
			GuardaMedida antes = SegurarORaio(chao, DanoDeKi.CorteDoFraco, MaoNaEscada.Jogador, MaoNaEscada.Parado, 0);
			AfirmarEk("(defeito injetado: as maos na escala da deflexao) o raio do CORTE ja nasce no teto da vantagem e ENGOLE quem segura "
					  + "-- mesmo acertando toda letra contra um atirador que nao aperta nada",
					  antes.Abriu && Math.Abs(antes.VantagemDoFeixe - EmbateDeKi.TetoDaVantagem) < 1e-9 && antes.Fim == DesfechoDeTeste.Perdi,
					  $"abriu {antes.Abriu}, vantagem do feixe {antes.VantagemDoFeixe:0.##}, {antes.Fim} em {antes.Segundos:0.0} s");
		}
		finally { EmbateDeKi.MaosPelaDeflexaoDeTeste = false; }

		OAluguelDoRaioNaEscada(chao);
		LimparEmbatesDaBancada();
	}

	/// <summary>
	/// ============================ O KI DE QUEM ATIRA TAMBEM DECIDE (medido, sem afirmacao) ============================
	/// A escada de cima usa o raio mais BARATO do jogo (o custo do Ki Wave), que paga os quinze segundos do prazo com
	/// folga. Os raios nomeados nao: o aluguel deles (`Canalizar`: custo do verb / 40 x `BaseDrain` por ciclo de
	/// 0,2 s, `beams.dm:38` e `:83`) sobe 10, 20 e 30 vezes com a pericia de raio (`Kamehameha.dm:47-77`), e numa
	/// disputa os dois ainda pagam o `EmbateDeKi.KiPorCiclo`. Sem Ki o lado cai (`LadoOk`, o `side_ok` do DM) -- e
	/// quem cai e quem atira, porque as maos so pagam a disputa.
	///
	/// Entao aqui a mesma cena roda com o custo do Kamehameha (20 drenos de entrada) nos quatro degraus, contra
	/// quem segura acertando toda letra e um atirador NPC, e imprime onde a disputa acabou pelo MEDIDOR e onde acabou
	/// porque o tanque de quem atira secou (`*`). E a mesma linha com as maos na escala de antes (o teto sempre):
	/// la a disputa durava 0,9 s, e os degraus mais caros ja nao pagavam nem isso.
	///
	/// SO SE LE. Os corpos da bancada tem 100 de Ki maximo e nenhuma eficiencia de ki treinada -- o caso mais caro: a
	/// entrada pesa 23% do tanque, e com a pericia de eficiencia em 100 o aluguel cai a um oitavo (`Fighter.BaseDrain`).
	/// ====================================================================================================================
	/// </summary>
	private void OAluguelDoRaioNaEscada(Vec2 chao)
	{
		double empata = EmbateDeKi.DanoQueAsMaosEmpatam;
		double[] danos = [1.5 * empata, 2 * empata, 3 * empata, 6 * empata];
		GD.Print("[embateki]      (medido) O ALUGUEL DO RAIO: guarda perfeita x atirador NPC, com o custo do Kamehameha (entrada de 20 drenos) "
				 + "em cada degrau da pericia de raio  (* = acabou porque o Ki de quem atira secou)");
		string cabecalho = string.Join(" | ", danos.Select(d => $"dano {d,4:0}      "));
		GD.Print($"[embateki]       aluguel | {cabecalho} | na escala de antes (teto)");
		foreach (double degrau in new[] { 1.0, 10, 20, 30 })
		{
			var casas = new List<string>();
			foreach (double dano in danos)
				casas.Add(CelulaDaEscada(SegurarORaio(chao, dano, MaoNaEscada.Jogador, MaoNaEscada.Npc, 0, entradaEmDrenos: 20, degrauDeCusto: degrau)));

			EmbateDeKi.MaosPelaDeflexaoDeTeste = true;
			try
			{
				casas.Add(CelulaDaEscada(SegurarORaio(chao, danos[0], MaoNaEscada.Jogador, MaoNaEscada.Npc, 0, entradaEmDrenos: 20, degrauDeCusto: degrau)));
			}
			finally { EmbateDeKi.MaosPelaDeflexaoDeTeste = false; }

			GD.Print($"[embateki]        {degrau,5:0}x | {string.Join(" | ", casas)}");
		}
	}

	/// <summary>
	/// Uma casa da tabela: o desfecho de quem segura, os segundos de disputa e, se foi o prazo, o medidor dele. O `*`
	/// marca a disputa que NAO acabou pelo medidor nem pelo prazo (um lado caiu: `LadoOk`).
	/// </summary>
	private static string CelulaDaEscada(GuardaMedida m)
	{
		if (!m.Abriu) return "------------";
		string palavra = m.Fim switch
		{
			DesfechoDeTeste.Venci => "VENCE ",
			DesfechoDeTeste.Empate => "empate",
			DesfechoDeTeste.Perdi => "perde ",
			_ => "------",
		};
		string noPrazo = m.Segundos >= EmbateDeKi.SegundosMaximos ? $" m{m.Medidor:00}" : m.PeloMedidor ? "    " : " *  ";
		return $"{palavra} {m.Segundos,4:0.0}s{noPrazo}";
	}

	/// <summary>
	/// UM EMBATE DE GUARDA COMPLETO entre dois iguais, com o raio fazendo o dano pedido atraves da guarda, so por
	/// chamadas de producao: o raio nasce pelo `Canalizar`, encontra a guarda pelo `Acertar` (que e quem chama o
	/// `TentarEmbateDeGuarda`), a disputa corre pelo `TickDosEmbatesDeKi` e as letras entram pelo
	/// `TeclaDeQualquerEmbate`.
	///
	/// O DANO SAI DA BASE DA TECNICA: a conta de producao e quadratica nela (ver <see cref="BaseQueFereUmIgual"/>),
	/// entao a base que faz 12 (<see cref="BaseQueFere"/>) vezes a raiz de `dano / 12` faz o dano pedido. Os corpos
	/// sao os da familia 16 (200.000 de poder cada, a mesma ficha), e o raio sai sem o dado da deflexao
	/// (<see cref="SemDeflexao"/>), como na familia 6.
	/// </summary>
	/// <param name="reacaoDaGuarda">Segundos que a letra fica na tela antes de quem segura apertar. Zero = o jogador perfeito.</param>
	/// <param name="entradaEmDrenos">O `kireq` do verb, em multiplos do `BaseDrain`: 10 e o Ki Wave, 20 o Kamehameha.</param>
	/// <param name="degrauDeCusto">Quantas vezes a entrada o raio cobra pra ficar de pe (o `lastbeamcost` do degrau de pericia).</param>
	private GuardaMedida SegurarORaio(Vec2 chao, double dano, MaoNaEscada maoDaGuarda, MaoNaEscada maoDoAtirador,
									  double reacaoDaGuarda, double entradaEmDrenos = 10, double degrauDeCusto = 1)
	{
		LimparEmbatesDaBancada();

		ServerPlayer atirador = Forjar("Agressor", chao, bp: 200_000);
		atirador.Facing = Facing.East;
		atirador.Ficha.Ki = atirador.Ficha.MaxKi;
		ServerPlayer guarda = Forjar("Muralha", new Vec2(chao.X + 8 * ZoneCollision.TileSize, chao.Y), bp: 200_000);
		guarda.Ficha.Ki = guarda.Ficha.MaxKi;
		SegurarAGuarda(guarda);

		ReceitaDeProjetil queFaz = SemDeflexao();
		queFaz.BaseDano = BaseQueFere(atirador, guarda) * Math.Sqrt(dano / DanoQueFereDoCorte);

		if (maoDaGuarda != MaoNaEscada.Npc) _comTecladoDeTeste.Add(guarda.Id);
		if (maoDoAtirador != MaoNaEscada.Npc) _comTecladoDeTeste.Add(atirador.Id);
		double entrada = entradaEmDrenos * atirador.Ficha.BaseDrain();
		Canalizar(atirador, "Ki_Wave", entrada, queFaz, custoPorTiro: entrada * degrauDeCusto);

		// ATE A DISPUTA ABRIR -- ou ate o raio parar na frente de quem segura, batendo sem ferir (abaixo do corte).
		DisputaDeKi? d = null;
		Projetil? raio = null;
		for (int i = 0; i < 30 * 8 && d == null; i++)
		{
			UmTiqueDoEncontro();
			raio ??= _canais.GetValueOrDefault(atirador.Id)?.Raio;
			d = _emEmbateDeKi.GetValueOrDefault(guarda.Id);
			if (raio is { BatendoSemFerir: not 0 }) break;
		}
		if (d == null)
		{
			var semEmbate = new GuardaMedida(false, raio != null ? DanoFinalDe(raio, guarda) : 0, 0, DesfechoDeTeste.NaoComecou, 0, 0, false);
			LimparEmbatesDaBancada();
			return semEmbate;
		}

		LadoDeKi dasMaos = d.A.Quem == guarda ? d.A : d.B;
		LadoDeKi doFeixe = dasMaos == d.A ? d.B : d.A;
		Projetil cabeca = doFeixe.Feixe!;
		double danoNaGuarda = DanoFinalDe(cabeca, guarda), vantagemDoFeixe = doFeixe.Vantagem;

		double naTelaDaGuarda = 0, naTelaDoAtirador = 0;
		for (int i = 0; i < 30 * 20 && _emEmbateDeKi.ContainsKey(guarda.Id); i++)
		{
			ApertarNaEscada(guarda, dasMaos, maoDaGuarda, reacaoDaGuarda, ref naTelaDaGuarda);
			ApertarNaEscada(atirador, doFeixe, maoDoAtirador, 0, ref naTelaDoAtirador);
			UmTiqueDoEncontro();
		}

		// QUEM VENCEU, PELO ESTADO DO JOGO (como a `DisputarComLetras`): devolvido, o feixe e de quem segurava; no
		// empate ele estoura e morre; engolida a guarda, ele continua vivo e de quem atirou, a caminho do corpo.
		DesfechoDeTeste fim = _emEmbateDeKi.ContainsKey(guarda.Id) ? DesfechoDeTeste.NaoComecou
			: cabeca.Dono == guarda.Id ? DesfechoDeTeste.Venci
			: cabeca.Vivo ? DesfechoDeTeste.Perdi
			: DesfechoDeTeste.Empate;
		bool peloMedidor = d.Medidor <= 0 || d.Medidor >= 100 || d.Corridos >= EmbateDeKi.SegundosMaximos;
		var medido = new GuardaMedida(true, danoNaGuarda, vantagemDoFeixe, fim, d.Corridos,
									  dasMaos == d.B ? 100 - d.Medidor : d.Medidor, peloMedidor);

		LimparEmbatesDaBancada();
		return medido;
	}

	/// <summary>
	/// O APERTO DE UM LADO NUM TIQUE: so o <see cref="MaoNaEscada.Jogador"/> aperta, e so depois de a letra ficar na
	/// tela pelo tempo de reacao. O relogio zera quando nao ha letra, e nao na troca de caractere -- duas letras
	/// seguidas podem sair iguais (a mesma conta da `DisputarComLetras`).
	/// </summary>
	private void ApertarNaEscada(ServerPlayer quem, LadoDeKi lado, MaoNaEscada mao, double reacao, ref double naTela)
	{
		if (mao != MaoNaEscada.Jogador) return;

		if (lado.Letra == '\0') naTela = 0;
		else if (naTela >= reacao) TeclaDeQualquerEmbate(quem, lado.Letra);
		else naTela += Protocol.TickSeconds;
	}
}
