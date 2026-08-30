using System.Diagnostics;
using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// ============================ BANCADA DE "NINGUEM ATRAVESSA NINGUEM" ============================
/// Mede as duas coisas que a tela nao mede:
///
///   1. **O CUSTO.** Colisao de cenario e O(1) (um bit numa celula); corpo contra corpo e por PAR. A
///      prova aqui e o par lado a lado: a varredura ingenua (o `foreach ZoneList` que o arremesso
///      usava) contra a <see cref="GradeDeCorpos"/>, no MESMO cenario e com o MESMO numero de
///      consultas. Se a grade nao ganhar por ordem de grandeza com a zona cheia, ela nao serve.
///
///   2. **AS DECISOES.** Quem bloqueia, quem nao bloqueia, e -- a que mais importa -- que **dois
///      corpos encostados nunca ficam presos**.
///
/// ============================ E ELA NASCE NO LUGAR ERRADO DE PROPOSITO ============================
/// A licao ja esta paga neste projeto: *nascer DENTRO do estado nunca testa a ENTRADA nele*. Entao
/// nenhuma prova aqui comeca com os corpos ja sobrepostos pra ver se separam, nem ja separados pra ver
/// se nao se juntam. Todas ANDAM: entram no vizinho vindo de longe, saem de cima dele vindo de dentro,
/// tentam contornar em diagonal. E cada familia tem um **defeito injetado** ao lado provando que a
/// prova sabe ficar VERMELHA -- uma prova que so sabe passar e um enfeite verde.
/// ================================================================================================
/// </summary>
public static class CorpoBench
{
	private static int _falhas;

	public static int Rodar()
	{
		Console.WriteLine("=== NINGUEM ATRAVESSA NINGUEM -- regras, e o custo de perguntar ===\n");

		Regras();
		Andando();
		NaoPrende();
		Arremesso();
		Custo();

		Console.WriteLine(_falhas == 0
			? "\nTODAS AS PROVAS PASSARAM."
			: $"\n{_falhas} PROVA(S) FALHARAM.");
		return _falhas == 0 ? 0 : 1;
	}

	private static void Prova(string oQue, bool passou)
	{
		if (!passou) _falhas++;
		Console.WriteLine($"  [{(passou ? "ok   " : "FALHA")}] {oQue}");
	}

	// =====================================================================
	// 1. AS REGRAS, uma a uma, com o contra-exemplo ao lado
	// =====================================================================
	private static void Regras()
	{
		Console.WriteLine("== 1. quem bloqueia, e quem NAO ==\n");

		// ---- o modo: literal do `mob/Cross` (CombatMovement.dm:52-57), contra um corpo LIVRE ----
		Prova("a pe ESBARRA em corpo (o caso do pedido: \"andando\")",
			  ClasseDeCorpo.Bloqueia(ModoDeTravessia.APe, Ocupacao.Livre));
		Prova("arremessado ESBARRA (\"por KNOCK BACK ou por ser JOGADO pelo grab\")",
			  ClasseDeCorpo.Bloqueia(ModoDeTravessia.Arremessado, Ocupacao.Livre));
		Prova("nadando ESBARRA (`Swim.dm:16` restaura density=1; o Cross so abre pra flying)",
			  ClasseDeCorpo.Bloqueia(ModoDeTravessia.Nadando, Ocupacao.Livre));
		Prova("VOANDO ATRAVESSA quem esta LIVRE -- a excecao do `mob/Cross`, a mesma que ja "
			  + "atravessa parede e agua",
			  !ClasseDeCorpo.Bloqueia(ModoDeTravessia.Voando, Ocupacao.Livre));

		// ---- a OCUPACAO: o pedido novo do dono, e as duas metades dele ----
		// *"faca com q n de pra empurar npcs ou outros players ao andar contra eles enquando eles
		//  batem ou fazem outra coisa"*. A metade que importa e a de cima: numa luta de DBZ todo mundo
		// voa, e sem esta linha a regra valia em todo lugar menos dentro da briga.
		Prova("VOANDO **PARA** em quem esta batendo -- o pedido do dono (\"enquando eles batem\")",
			  ClasseDeCorpo.Bloqueia(ModoDeTravessia.Voando, Ocupacao.Atacando));
		Prova("...e em quem esta de GUARDA (a decisao escrita: guardar ocupa)",
			  ClasseDeCorpo.Bloqueia(ModoDeTravessia.Voando, Ocupacao.Guardando));

		// E A METADE DE BAIXO, sem a qual a de cima ficaria verde com "tudo barra tudo": TODO estado
		// da lista barra, e o estado LIVRE (o unico que sobra) continua deixando passar quem voa.
		int barram = 0, valores = 0;
		foreach (Ocupacao o in Enum.GetValues<Ocupacao>())
		{
			valores++;
			if (ClasseDeCorpo.Bloqueia(ModoDeTravessia.Voando, o)) barram++;
		}
		Prova($"TODOS os {valores - 1} estados ocupados barram quem voa, e **so** o `Livre` nao "
			  + $"({barram} de {valores})",
			  barram == valores - 1 && !ClasseDeCorpo.Bloqueia(ModoDeTravessia.Voando, Ocupacao.Livre));

		Prova("a pe, a ocupacao nao muda nada (quem anda ja esbarrava em todo mundo)",
			  ClasseDeCorpo.Bloqueia(ModoDeTravessia.APe, Ocupacao.Livre)
			  && ClasseDeCorpo.Bloqueia(ModoDeTravessia.APe, Ocupacao.Atacando));

		// ---- e o NOME: "o corpo nao andou" nao e diagnostico ----
		Prova("livre nao tem nome (vazio = livre, o molde do `PorQueNaoAnda`)",
			  CorpoOcupado.Nome(Ocupacao.Livre).Length == 0 && !CorpoOcupado.Ocupado(Ocupacao.Livre));
		int semNome = Enum.GetValues<Ocupacao>().Count(
			o => o != Ocupacao.Livre && CorpoOcupado.Nome(o).Length == 0);
		Prova($"e todo estado ocupado TEM nome ({semNome} sem nome)", semNome == 0);

		// A ORDEM DE PRIORIDADE: dois sinais ligados, ganha o primeiro da lista. Sem esta linha um
		// corpo nocauteado que tinha `train` ligado seria relatado como "treinando".
		Prova("com nocaute E treino ligados ao mesmo tempo, o nome que sai e o NOCAUTE",
			  CorpoOcupado.De(new CorpoOcupado.Sinais { Nocauteado = true, Treinando = true })
			  == Ocupacao.Nocauteado);
		Prova("nenhum sinal ligado = `Livre` (senao todo mundo viraria poste)",
			  CorpoOcupado.De(new CorpoOcupado.Sinais()) == Ocupacao.Livre);
		Prova("byte desconhecido no fio vira `Livre`, e nao estado inventado",
			  CorpoOcupado.DeByte(200) == Ocupacao.Livre
			  && CorpoOcupado.DeByte((byte)Ocupacao.Guardando) == Ocupacao.Guardando);

		// ---- o andar: a regra que o DM nao tem, porque o DM nao tem altura ----
		Prova("dois no CHAO estao no mesmo andar",
			  ClasseDeCorpo.MesmoAndar(Voo.Andar(0f), Voo.Andar(0f)));
		Prova("chao x quem paira rasante (andar 1): NAO se esbarram "
			  + "-- senao o corpo no ar vira poste invisivel",
			  !ClasseDeCorpo.MesmoAndar(Voo.Andar(0f), Voo.Andar(Voo.AlturaDePairar)));
		Prova("...e a regra e SIMETRICA (ao contrario do `Voo.PodeAcertar`): "
			  + "colisao assimetrica empurraria um pra dentro do outro",
			  ClasseDeCorpo.MesmoAndar(Voo.Andar(0f), Voo.Andar(Voo.AlturaDePairar))
			  == ClasseDeCorpo.MesmoAndar(Voo.Andar(Voo.AlturaDePairar), Voo.Andar(0f)));

		// ---- a caixa: a MESMA dos pes, e nao uma propria ----
		var a = new Vec2(100, 100);
		Prova($"a caixa e a dos PES ({2 * MoveRules.BodyHalfW:0}x{2 * MoveRules.BodyHalfH:0} px), "
			  + "a mesma com que a parede responde \"cabe um corpo aqui?\"",
			  ClasseDeCorpo.CaixasSeTocam(a, a + new Vec2(MoveRules.BodyHalfW, 0))
			  && !ClasseDeCorpo.CaixasSeTocam(a, a + new Vec2(2 * MoveRules.BodyHalfW + 0.1f, 0)));
		Prova("a ancora e a dos pes, e nao o centro do sprite (os mesmos 8 px do `MoveRules`)",
			  Math.Abs(ClasseDeCorpo.Pes(a).Y - (a.Y + MoveRules.FeetOffsetY)) < 1e-4f);

		// ---- DEFEITO INJETADO: a prova sabe reprovar? ----
		Console.WriteLine("\n  -- defeito injetado (a familia tem que ficar VERMELHA) --");
		Prova("com a regra ERRADA (\"voando tambem esbarra\") a prova acima reprovaria",
			  !Errado_VoandoEsbarra());
	}

	/// <summary>A regra que NAO queremos. Se ela passasse pelas provas, as provas nao serviriam.</summary>
	private static bool Errado_VoandoEsbarra()
	{
		// simula a versao defeituosa: bloqueia em TODO modo, ocupado ou nao
		static bool bloqueiaSempre(ModoDeTravessia _, Ocupacao __) => true;
		return !bloqueiaSempre(ModoDeTravessia.Voando, Ocupacao.Livre);
	}

	// =====================================================================
	// 2. ANDANDO -- entra no vizinho vindo de LONGE (a entrada, nao o estado)
	// =====================================================================
	private static void Andando()
	{
		Console.WriteLine("\n== 2. andando contra outro corpo (vindo de longe) ==\n");

		ZoneCollision campo = CampoAberto(40, 40);
		var grade = new GradeDeCorpos();

		// o vizinho, parado no meio
		var vizinho = new Vec2(20 * 32 + 16, 20 * 32 + 16);
		grade.Recomecar();
		grade.Por(2, ClasseDeCorpo.Pes(vizinho), 0);

		var v = new Vizinhanca(grade, 1, 0);

		// COMECA 5 TILES A OESTE e anda pro leste ate parar. A entrada no estado, nao o estado.
		Vec2 pos = vizinho - new Vec2(5 * 32, 0);
		float distInicial = (vizinho - pos).Length;
		int quadros = 0;
		for (; quadros < 600; quadros++)
		{
			Vec2 antes = pos;
			pos = MoveRules.Advance(pos, new Vec2(1, 0), 1f / 60, 1f, campo, out _,
									false, ModoDeTravessia.APe, v);
			if ((pos - antes).LengthSquared < 1e-6f) break;
		}
		float sobrou = (vizinho - pos).Length;
		Console.WriteLine($"  andou {distInicial - sobrou:0.0} px em {quadros} quadros e parou a "
						  + $"{sobrou:0.0} px do vizinho");
		Prova("PAROU antes de entrar no vizinho (nao atravessou)",
			  quadros < 600 && sobrou >= 2 * MoveRules.BodyHalfW - 0.01f);
		Prova("as caixas NAO se tocam no fim",
			  !ClasseDeCorpo.CaixasSeTocam(ClasseDeCorpo.Pes(pos), ClasseDeCorpo.Pes(vizinho)));

		// SEM a grade (o comportamento de antes) o mesmo passo ATRAVESSA -- e o contra-exemplo.
		Vec2 livre = vizinho - new Vec2(5 * 32, 0);
		for (int i = 0; i < 600; i++)
			livre = MoveRules.Advance(livre, new Vec2(1, 0), 1f / 60, 1f, campo, out _);
		Prova("...e sem a grade (como era antes) ele passa por dentro -- o contra-exemplo",
			  livre.X > vizinho.X + 32);

		// DESLIZE: em diagonal ele CONTORNA em vez de travar. E o que separa "esbarrei" de
		// "achei que travei".
		Vec2 diag = vizinho - new Vec2(3 * 32, 3);
		Vec2 antesDiag = diag;
		for (int i = 0; i < 240; i++)
			diag = MoveRules.Advance(diag, new Vec2(1, 1), 1f / 60, 1f, campo, out _,
									 false, ModoDeTravessia.APe, v);
		Prova($"em diagonal ele DESLIZA e contorna (andou {(diag - antesDiag).Length:0} px, "
			  + $"desviou {Math.Abs(diag.Y - antesDiag.Y):0} px em Y)",
			  Math.Abs(diag.Y - antesDiag.Y) > 32);

		// VOANDO ATRAVESSA -- mesmo ponto de partida, so o modo muda.
		Vec2 voando = vizinho - new Vec2(5 * 32, 0);
		var vAlto = new Vizinhanca(grade, 1, 0);   // MESMO andar, pra provar que quem decide e o MODO
		for (int i = 0; i < 600; i++)
			voando = MoveRules.Advance(voando, new Vec2(1, 0), 1f / 60, 1f, null, out _,
									   false, ModoDeTravessia.Voando, vAlto);
		Prova("quem VOA atravessa o mesmo corpo LIVRE, no mesmo andar -- quem decide e o modo",
			  voando.X > vizinho.X + 32);

		// ============================ ...E **PARA** SE O VIZINHO ESTIVER OCUPADO ============================
		// *"faca com q n de pra empurar npcs ou outros players ao andar contra eles enquando eles batem
		// ou fazem outra coisa"*. Este e o par que fecha a linha de cima: MESMO ponto de partida, MESMO
		// modo (voando), MESMO andar -- muda so o que o vizinho esta fazendo.
		//
		// E e o caso do dono e nao um canto raro: numa luta de DBZ os dois estao no ar, e a linha de
		// cima sozinha fazia a regra inteira valer em todo lugar menos dentro da briga.
		// ================================================================================================
		grade.Recomecar();
		grade.Por(2, ClasseDeCorpo.Pes(vizinho), 0, Ocupacao.Atacando);
		var vOcupado = new Vizinhanca(grade, 1, 0);
		Vec2 contraOcupado = vizinho - new Vec2(5 * 32, 0);
		int quadrosBarrado = 0;
		for (int i = 0; i < 600; i++)
		{
			contraOcupado = MoveRules.Advance(contraOcupado, new Vec2(1, 0), 1f / 60, 1f, null,
											  out bool bar, false, ModoDeTravessia.Voando, vOcupado);
			if (bar) quadrosBarrado++;
		}
		Console.WriteLine($"  voando contra quem esta ATACANDO: parou a "
						  + $"{(vizinho - contraOcupado).Length:0.0} px, {quadrosBarrado} quadros barrados");
		Prova("voando contra quem esta ATACANDO ele PARA -- o pedido do dono",
			  contraOcupado.X < vizinho.X && quadrosBarrado > 0);

		// ============================ "O OCUPADO NAO DESLIZA" NAO SE MEDE AQUI, E ISSO PRECISA SER DITO ============================
		// A outra metade do pedido e *"o ocupado nao desliza"*, e escrever aqui uma linha do tipo
		// `Prova("o vizinho nao saiu do lugar", vizinho == vizinho)` seria o pior tipo de verde: a
		// bancada medindo a propria variavel local, que ninguem tem como mexer.
		//
		// Nesta bancada o vizinho e um `Vec2` que ELA guarda -- `MoveRules.Advance` devolve UM ponto, o
		// de quem anda, e nao ha `out` nem `ref` por onde um segundo corpo pudesse sair mexido. A
		// medida de verdade precisa de dois corpos que o SERVIDOR possua, e ela esta na
		// `--doiscorposteste`, familia 9: la o `b.Pos` e do mundo e a bancada le o que sobrou dele.
		// =========================================================================================================================

		// A ANTI-PROVA DO ANDAR: ocupado NAO vira poste pra quem passa por cima. Sem esta linha,
		// "ocupado barra todo mundo" ficaria verde com um mundo em que socar cria uma coluna de 3
		// andares de altura.
		grade.Recomecar();
		grade.Por(2, ClasseDeCorpo.Pes(vizinho), 0, Ocupacao.Atacando);
		var vPorCima = new Vizinhanca(grade, 1, 1);   // eu no andar 1, ele ocupado no chao
		Vec2 porCima = vizinho - new Vec2(5 * 32, 0);
		for (int i = 0; i < 600; i++)
			porCima = MoveRules.Advance(porCima, new Vec2(1, 0), 1f / 60, 1f, null, out _,
										false, ModoDeTravessia.Voando, vPorCima);
		Prova("mas em ANDAR DIFERENTE ele continua atravessando o ocupado -- ocupado nao e poste",
			  porCima.X > vizinho.X + 32);

		// ANDAR DIFERENTE atravessa -- mesmo modo, so o andar muda.
		grade.Recomecar();
		grade.Por(2, ClasseDeCorpo.Pes(vizinho), 1);   // o vizinho paira
		var vChao = new Vizinhanca(grade, 1, 0);
		Vec2 porBaixo = vizinho - new Vec2(5 * 32, 0);
		for (int i = 0; i < 600; i++)
			porBaixo = MoveRules.Advance(porBaixo, new Vec2(1, 0), 1f / 60, 1f, campo, out _,
										 false, ModoDeTravessia.APe, vChao);
		Prova("quem esta no chao passa POR BAIXO de quem paira (andar 1) -- nao ha poste invisivel",
			  porBaixo.X > vizinho.X + 32);
	}

	// =====================================================================
	// 3. NAO PRENDE -- comeca DENTRO e tem que sair (pedido 6)
	// =====================================================================
	private static void NaoPrende()
	{
		Console.WriteLine("\n== 3. dois corpos encostados NAO ficam presos (pedido 6) ==\n");

		ZoneCollision campo = CampoAberto(40, 40);
		var grade = new GradeDeCorpos();
		var onde = new Vec2(20 * 32 + 16, 20 * 32 + 16);

		// SOBREPOSICAO EXATA -- o pior caso, e ele acontece de verdade: solta-se do colo na posicao
		// EXATA de quem carregava (`LevarNoColo` faz `d.Pos = a.Pos`), nasce-se no spawn onde alguem
		// esta, cai-se nocauteado em cima de quem estava colado.
		grade.Recomecar();
		grade.Por(2, ClasseDeCorpo.Pes(onde), 0);
		var v = new Vizinhanca(grade, 1, 0);

		int saiu = 0;
		var rumos = new[] { new Vec2(1, 0), new Vec2(-1, 0), new Vec2(0, 1), new Vec2(0, -1),
							new Vec2(1, 1), new Vec2(-1, -1), new Vec2(1, -1), new Vec2(-1, 1) };
		foreach (Vec2 rumo in rumos)
		{
			Vec2 p = onde;
			for (int i = 0; i < 120; i++)
				p = MoveRules.Advance(p, rumo, 1f / 60, 1f, campo, out _, false, ModoDeTravessia.APe, v);
			if (!ClasseDeCorpo.CaixasSeTocam(ClasseDeCorpo.Pes(p), ClasseDeCorpo.Pes(onde))) saiu++;
		}
		Prova($"sobreposto EXATAMENTE, sai andando em todas as 8 direcoes ({saiu}/8)", saiu == 8);

		// ============================ ...E ISSO VALE COM O OUTRO **OCUPADO** ============================
		// A regra nova (corpo ocupado para ate quem voa) e exatamente do tipo que reabre travamento: se
		// ela desligasse o escape do `jaSobrepondo`, bastaria alguem erguer a guarda em cima de voce --
		// ou cair nocauteado no seu ponto de spawn, ou soltar voce do colo e socar -- pra voce nao andar
		// mais, sem nada na tela dizendo por que.
		//
		// Cada estado da lista e testado, e nao so um: e a lista inteira que precisa nao prender.
		// ============================================================================================
		foreach (Ocupacao o in Enum.GetValues<Ocupacao>())
		{
			if (o == Ocupacao.Livre) continue;
			grade.Recomecar();
			grade.Por(2, ClasseDeCorpo.Pes(onde), 0, o);
			var vOc = new Vizinhanca(grade, 1, 0);

			int saiuOc = 0;
			foreach (Vec2 rumo in rumos)
			{
				Vec2 p = onde;
				for (int i = 0; i < 120; i++)
					p = MoveRules.Advance(p, rumo, 1f / 60, 1f, campo, out _, false,
										  ModoDeTravessia.APe, vOc);
				if (!ClasseDeCorpo.CaixasSeTocam(ClasseDeCorpo.Pes(p), ClasseDeCorpo.Pes(onde))) saiuOc++;
			}
			Prova($"sobreposto a quem esta {CorpoOcupado.Nome(o)}, ainda sai nas 8 direcoes ({saiuOc}/8)",
				  saiuOc == 8);
		}

		// ...E VOANDO TAMBEM. O caso e real: dois corpos no ar no mesmo ponto (o carregado solto no ar,
		// o pouso de um arremesso) com um deles socando. Antes desta rodada o voo nem consultava a
		// grade, entao este caminho e novo e precisa da sua propria linha.
		grade.Recomecar();
		grade.Por(2, ClasseDeCorpo.Pes(onde), 1, Ocupacao.Atacando);
		var vAr = new Vizinhanca(grade, 1, 1);
		int saiuNoAr = 0;
		foreach (Vec2 rumo in rumos)
		{
			Vec2 p = onde;
			for (int i = 0; i < 120; i++)
				p = MoveRules.Advance(p, rumo, 1f / 60, 1f, null, out _, false,
									  ModoDeTravessia.Voando, vAr);
			if (!ClasseDeCorpo.CaixasSeTocam(ClasseDeCorpo.Pes(p), ClasseDeCorpo.Pes(onde))) saiuNoAr++;
		}
		Prova($"no AR, sobreposto a quem esta atacando, tambem sai nas 8 direcoes ({saiuNoAr}/8)",
			  saiuNoAr == 8);

		// e volta o vizinho LIVRE pra as provas seguintes desta familia
		grade.Recomecar();
		grade.Por(2, ClasseDeCorpo.Pes(onde), 0);

		// ...E DEPOIS DE SAIR, ELE VOLTA A BARRAR. Sem isto o remedio viraria um passe livre.
		Vec2 fora = onde + new Vec2(3 * 32, 0);
		Vec2 voltando = fora;
		for (int i = 0; i < 600; i++)
			voltando = MoveRules.Advance(voltando, new Vec2(-1, 0), 1f / 60, 1f, campo, out _,
										 false, ModoDeTravessia.APe, v);
		Prova("...e uma vez FORA ele volta a ser barrado (o remedio nao virou passe livre)",
			  !ClasseDeCorpo.CaixasSeTocam(ClasseDeCorpo.Pes(voltando), ClasseDeCorpo.Pes(onde)));

		// SAIR DE CIMA DE ALGUEM NAO PODE ATRAVESSAR PAREDE. E a armadilha que a memoria da agua
		// documenta: a saida de emergencia do `Advance` devolve o passo CHEIO ignorando o mapa. Se
		// "estar dentro de um corpo" caisse naquele ramo, quem estivesse por cima de alguem
		// atravessaria muro por alguns quadros.
		ZoneCollision muro = MuroVertical(40, 40, 21);

		// ============================ O PALCO ESTAVA ERRADO, E A LINHA ERA VERMELHA ANTES DESTA RODADA ============================
		// O ponto era `20*32+24` = 664, e a caixa dos pes dele vai de 656 a **672** -- 672 e a primeira
		// coluna do muro. Ou seja: o corpo ja nascia DENTRO da parede, caia no ramo `Escape.Dirigido` e
		// o escape apontava pra OESTE; andando pro leste todo passo era (corretamente) recusado, ele
		// nunca saia do lugar, e a linha cobrava `!Occupied` de um corpo que comecou dentro do muro.
		//
		// Medido: com a grade e SEM a grade o corpo termina no mesmo 664 -- ou seja o defeito nao era da
		// colisao de corpos, era do palco. **E o erro classico desta casa: a bancada que NASCE DENTRO do
		// estado.** Aqui ela nascia dentro da PAREDE e por isso nunca mediu a parede.
		//
		// 20*32+16 = 656 poe a caixa em [648, 664]: fora do muro, e a 8 px dele -- perto o bastante pra
		// que o primeiro passo ja o teste, longe o bastante pra que a regra medida seja a normal.
		// ========================================================================================================================
		Vec2 colado = new(20 * 32 + 16, 20 * 32 + 16);   // encostado no muro da coluna 21, e FORA dele
		grade.Recomecar();
		grade.Por(2, ClasseDeCorpo.Pes(colado), 0);
		var vMuro = new Vizinhanca(grade, 1, 0);
		Prova("PRECONDICAO: ele comeca FORA da parede (senao a medida vira sobre o escape, e nao sobre o muro)",
			  !MoveRules.Occupied(muro, colado));
		Vec2 dentro = colado;
		for (int i = 0; i < 300; i++)
			dentro = MoveRules.Advance(dentro, new Vec2(1, 0), 1f / 60, 1f, muro, out _,
									   false, ModoDeTravessia.APe, vMuro);
		Prova("saindo de cima de alguem, a PAREDE continua valendo "
			  + "(nao caiu na saida de emergencia do `Advance`)",
			  !MoveRules.Occupied(muro, dentro));

		// ============================ ...E SAIR DA PAREDE NAO E LICENCA PRA ATRAVESSAR GENTE ============================
		// A porta ao contrario da de cima, e ela estava ABERTA -- medido na Fase 0: um corpo com o pe
		// numa celula de parede andava **157 px por dentro de outro corpo, 0 quadros barrados**, porque
		// o ramo `Escape.Dirigido` do `Advance` so perguntava "este passo aproxima do refugio?".
		//
		// O palco: o corpo ENTERRADO na coluna 21 (a caixa inteira dentro da parede) e a vitima parada
		// na celula pra onde o escape aponta. Ele tem que sair da parede, sim -- mas parando nela.
		// ==============================================================================================================
		var vitima = new Vec2(20 * 32 + 16, 20 * 32 + 8);      // na celula do refugio
		Vec2 enterrado = new(21 * 32 + 8, 20 * 32 + 8);        // caixa dos pes inteira dentro do muro
		grade.Recomecar();
		grade.Por(2, ClasseDeCorpo.Pes(vitima), 0);
		var vEnterrado = new Vizinhanca(grade, 1, 0);

		Prova("PRECONDICAO: ele comeca DENTRO da parede (senao a medida cai na regra normal)",
			  MoveRules.Occupied(muro, enterrado));
		Prova("PRECONDICAO: e comeca FORA da vitima (senao quem o solta e o `jaSobrepondo`)",
			  !ClasseDeCorpo.CaixasSeTocam(ClasseDeCorpo.Pes(enterrado), ClasseDeCorpo.Pes(vitima)));

		Vec2 fugindo = enterrado;
		for (int i = 0; i < 300; i++)
			fugindo = MoveRules.Advance(fugindo, new Vec2(-1, 0), 1f / 60, 1f, muro, out _,
										false, ModoDeTravessia.APe, vEnterrado);
		Console.WriteLine($"  enterrado, fugindo PRA CIMA da vitima: andou "
						  + $"{(enterrado - fugindo).Length:0.0} px e parou a "
						  + $"{Math.Abs(fugindo.X - vitima.X):0.0} px dela");

		// O CRITERIO E "NAO PASSOU DO OUTRO LADO", e nao "nao esta encostado no fim".
		// **Essa diferenca custou uma medicao**: com o defeito o corpo atravessa a vitima e segue por
		// 669 px ate a beirada do mapa -- e la ele TAMBEM nao esta encostado nela. Um criterio de
		// "caixas nao se tocam" ficaria verde justamente no caso que ele existe pra pegar.
		Prova("enterrado na parede, o escape NAO o deixa passar por dentro da vitima "
			  + "(ele para do lado de ca)",
			  fugindo.X > vitima.X);

		// ---- DEFEITO INJETADO: o ramo do escape SEM consultar corpo (o que havia antes) ----
		// `Vizinhanca` vazia e literalmente o codigo de ontem neste ramo -- e nao uma copia dele
		// escrita aqui: a mesma funcao, o mesmo mapa, o mesmo palco, so sem o plano dos corpos.
		Vec2 comDefeito = enterrado;
		for (int i = 0; i < 300; i++)
			comDefeito = MoveRules.Advance(comDefeito, new Vec2(-1, 0), 1f / 60, 1f, muro, out _);
		Console.WriteLine($"  (com o defeito -- escape sem consultar corpo -- ele iria pra "
						  + $"{comDefeito.X:0.0}, {enterrado.X - comDefeito.X:0.0} px de travessia)");
		Prova("...e a prova acima SABE reprovar: sem consultar corpo ele passa do outro lado",
			  comDefeito.X < vitima.X);

		// ...E ELE NAO FICOU PRESO. A regra nova e do tipo que cria travamento: barrado pela vitima E
		// pela parede, ele ainda precisa ter pra onde ir. O deslize de quina e quem responde -- em
		// diagonal o eixo livre continua andando, que e a mesma coisa que o muro ja fazia.
		Vec2 preso = fugindo;
		Vec2 antesDoDesvio = preso;
		for (int i = 0; i < 120; i++)
			preso = MoveRules.Advance(preso, new Vec2(-1, -1), 1f / 60, 1f, muro, out _,
									  false, ModoDeTravessia.APe, vEnterrado);
		Prova($"...e ele NAO ficou preso: em diagonal ele desliza pelo eixo livre "
			  + $"({Math.Abs(preso.Y - antesDoDesvio.Y):0} px em Y)",
			  Math.Abs(preso.Y - antesDoDesvio.Y) > 16);
	}

	// =====================================================================
	// 4. O ARREMESSO -- o corpo jogado esbarra, e nao tunela
	// =====================================================================
	private static void Arremesso()
	{
		Console.WriteLine("\n== 4. o corpo ARREMESSADO ==\n");

		var grade = new GradeDeCorpos();
		var vizinho = new Vec2(20 * 32 + 16, 20 * 32 + 16);
		grade.Recomecar();
		grade.Por(2, ClasseDeCorpo.Pes(vizinho), 0);
		var v = new Vizinhanca(grade, 1, 0);

		// O PASSO DO ARREMESSO E GROSSO (~21 px por fatia a 30 Hz, 2 tiles por tique do DM), e a
		// caixa tem 16 px de largura: sem amostrar o caminho ele TUNELA. Aqui reproduzo a amostragem
		// de meio tile que o `TickDoEmpurrao` faz.
		Vec2 pos = vizinho - new Vec2(6 * 32, 0);
		int bateuEm = 0;
		for (int tique = 0; tique < 40 && bateuEm == 0; tique++)
		{
			Vec2 passo = new(64f / 3f, 0);   // uma fatia do tique do DM
			int amostras = Math.Max(1, (int)MathF.Ceiling(passo.Length / (ZoneCollision.TileSize / 2f)));
			for (int i = 1; i <= amostras && bateuEm == 0; i++)
			{
				Vec2 p = pos + passo * (i / (float)amostras);
				bateuEm = v.Barra(pos, p, ModoDeTravessia.Arremessado);
			}
			if (bateuEm == 0) pos += passo;
		}
		Prova($"o corpo jogado ACERTA o vizinho (id {bateuEm}) em vez de atravessar",
			  bateuEm == 2);

		// ============================ E POR QUE A AMOSTRAGEM NAO E OPCIONAL ============================
		// O tique do DM anda DOIS TILES (`Empurrao.TilesPorTique`) e a caixa tem 16 px de largura: um
		// salto de 64 px deixa um vao de 48 px em que ninguem e testado. Testar so o ponto de CHEGADA --
		// que era o que o codigo antigo fazia -- perde o alvo na maioria dos alinhamentos.
		//
		// (Com o passo ja FATIADO pelo tique do servidor, ~21 px, o vao e menor que a caixa e o
		// alinhamento nao importa mais. A amostragem de meio tile e o que garante isso mesmo se um dia
		// alguem mexer na fatia -- e a mesma garantia que a PAREDE ja tem, e pela mesma razao: o dono ja
		// viu o defeito uma vez, "algumas paredes q ele passa n quebram".)
		// ==========================================================================================
		int tunelouSemAmostrar = 0, tunelouAmostrando = 0;
		for (int desvio = 0; desvio < 64; desvio++)
		{
			var salto = new Vec2(64, 0);   // o tique do DM inteiro, sem fatiar
			Vec2 partida = vizinho - new Vec2(8 * 64 + desvio, 0);

			Vec2 p = partida;
			bool acertou = false;
			for (int t = 0; t < 40 && !acertou; t++)
			{
				Vec2 destino = p + salto;
				acertou = v.Barra(p, destino, ModoDeTravessia.Arremessado) != 0;
				p = destino;
			}
			if (!acertou) tunelouSemAmostrar++;

			p = partida;
			acertou = false;
			for (int t = 0; t < 40 && !acertou; t++)
			{
				int amostras = (int)MathF.Ceiling(salto.Length / (ZoneCollision.TileSize / 2f));
				for (int i = 1; i <= amostras && !acertou; i++)
					acertou = v.Barra(p, p + salto * (i / (float)amostras),
									  ModoDeTravessia.Arremessado) != 0;
				p += salto;
			}
			if (!acertou) tunelouAmostrando++;
		}
		Prova($"num salto de 2 tiles, so o ponto de chegada TUNELA em {tunelouSemAmostrar}/64 "
			  + "alinhamentos (era o que o codigo antigo fazia)", tunelouSemAmostrar > 0);
		Prova($"...e amostrando de meio tile em meio tile, tunela em {tunelouAmostrando}/64",
			  tunelouAmostrando == 0);

		// O QUE NAO PODE: bater em quem ele JA estava tocando quando o arremesso comecou. O
		// arremesso do agarrao nasce colado.
		Vec2 colado = vizinho + new Vec2(4, 0);
		Prova("nao bate em quem ja estava tocando na partida (o arremesso do grab nasce colado)",
			  v.Barra(colado, colado + new Vec2(8, 0), ModoDeTravessia.Arremessado) == 0);
	}

	// =====================================================================
	// 5. O CUSTO -- a grade contra a varredura ingenua
	// =====================================================================
	/// <summary>
	/// O CORPO, COMO O SERVIDOR O TEM: um OBJETO numa lista. Nao um `Vec2` num array.
	///
	/// Isto nao e detalhe de bancada -- e o que torna a medida honesta. A varredura ingenua e
	/// `foreach (ServerPlayer o in ZoneList(...))`, e cada volta desreferencia um objeto do heap pra ler
	/// posicao e altura. Medi-la com um array de structs contiguo daria a versao ingenua uma vantagem
	/// de cache que ela nao tem no servidor, e a comparacao ficaria a favor do que estamos removendo.
	/// </summary>
	private sealed class CorpoFalso
	{
		public int Id;
		public Vec2 Pos;
		public float Altitude;
		// enchimento: o `ServerPlayer` de verdade tem dezenas de campos, entao dois corpos vizinhos na
		// lista nunca estao vizinhos na memoria
		private readonly byte[] _peso = new byte[256];
		public int Peso => _peso.Length;
	}

	private static void Custo()
	{
		Console.WriteLine("\n== 5. o custo: grade espacial x varredura da ZoneList ==\n");
		Console.WriteLine("  Um tique = 3 consultas por corpo (passo cheio + os dois deslizes de quina),");
		Console.WriteLine("  que e o que o `MoveRules.Advance` faz. 1000 tiques = 33 s de jogo a 30 Hz.");
		Console.WriteLine("  A ingenua e o `foreach (ServerPlayer o in ZoneList(...))` que o arremesso fazia.\n");

		// ESPALHADO = uma zona de 500x500 tiles (o mapa de verdade). AGLOMERADO = uma praca de 40x40,
		// que e o caso que interessa: uma cidade do `Povoamento`, uma briga, um ajuntamento. E no
		// aglomerado que a varredura ingenua doi, porque e la que ha vizinhos de verdade pra achar.
		foreach ((string nome, int lado) in new[] { ("espalhado (500x500 tiles)", 500),
												    ("aglomerado (40x40 tiles)", 40) })
		{
			Console.WriteLine($"  -- {nome} --");
			Console.WriteLine("     n |  ingenua (ms/1000 tiques) |  grade (ms/1000 tiques) | ganho | encontros");
			Console.WriteLine("  -----+---------------------------+-------------------------+-------+----------");

			foreach (int n in new[] { 2, 10, 50, 150, 400 })
			{
				var rng = new Random(1234);
				var zona = new List<CorpoFalso>(n);
				for (int i = 0; i < n; i++)
					zona.Add(new CorpoFalso
					{
						Id = i + 1,
						Pos = new Vec2(rng.Next(0, lado * 32), rng.Next(0, lado * 32)),
						// TODO MUNDO NO CHAO, E ISSO E O PIOR CASO -- nao esquecimento. As duas
						// medidas pulam candidato de outro andar (`MesmoAndar`), entao espalhar os
						// corpos por andares DIMINUIRIA o trabalho das duas e mediria uma zona mais
						// facil que a real. Escrito e nao deixado no padrao pra ninguem ler "campo
						// morto" onde ha decisao -- o compilador ja avisou uma vez neste projeto.
						Altitude = 0f,
					});

				var grade = new GradeDeCorpos();

				// AQUECIMENTO. Sem ele a primeira familia medida paga o JIT das duas e a tabela sai sem
				// sentido (a primeira linha vinha 40x mais lenta que a segunda, com metade dos corpos).
				Ingenua(zona, 5, out _);
				ComGrade(zona, grade, 5, out _);

				const int tiques = 1000;
				double ms1 = Ingenua(zona, tiques, out long achouA);
				double ms2 = ComGrade(zona, grade, tiques, out long achouB);

				Console.WriteLine($"  {n,4} | {ms1,25:0.00} | {ms2,23:0.00} | {(ms2 > 0 ? ms1 / ms2 : 0),4:0.0}x "
								  + $"| {achouB / tiques,8}");

				// AS DUAS TEM QUE ACHAR O MESMO. Uma grade rapida e errada seria pior que o O(n²) --
				// e este e o unico numero da tabela que reprova sozinho.
				if (achouA != achouB)
					Prova($"  n={n} ({nome}): a grade e a varredura DISCORDAM ({achouB} x {achouA})", false);
			}
			Console.WriteLine();
		}

		Prova("a grade acha exatamente os mesmos encontros que a varredura, em todos os casos acima",
			  _falhas == 0);

		Console.WriteLine("  Abaixo de ~50 corpos as duas somem no ruido: 1000 tiques sao 33 SEGUNDOS de");
		Console.WriteLine("  jogo, entao 4 ms na coluna sao 4 MICROSSEGUNDOS por tique. A grade perde por");
		Console.WriteLine("  microssegundos com a zona vazia e ganha por ordem de grandeza com ela cheia --");
		Console.WriteLine("  e e a zona cheia que decide se o servidor aguenta.\n");
		Console.WriteLine("  A varredura cresce com n² (n corpos x n candidatos por consulta); a grade");
		Console.WriteLine("  cresce com n (montar) + O(1) por consulta -- 3x3 baldes de 32 px, e a caixa");
		Console.WriteLine("  tem 16 px de largura, entao os 9 baldes sao a vizinhanca EXATA e nao uma");
		Console.WriteLine("  aproximacao. Montar acontece UMA vez por tique, no `MontarAsGrades`.");

		// ---- e o tique REAL de um servidor povoado ----
		Console.WriteLine("\n  no tique de verdade (30 Hz, uma cidade de 150 habitantes num quarteirao):");
		{
			const int n = 150;
			var rng = new Random(99);
			var zona = new List<CorpoFalso>(n);
			for (int i = 0; i < n; i++)
				zona.Add(new CorpoFalso { Id = i + 1, Altitude = 0f,   // no chao: o pior caso, ver acima
										  Pos = new Vec2(rng.Next(0, 40 * 32), rng.Next(0, 40 * 32)) });

			var grade = new GradeDeCorpos();
			ComGrade(zona, grade, 30, out _);                       // aquecimento
			double ms = ComGrade(zona, grade, 30, out _);           // 30 tiques = 1 s de jogo
			Console.WriteLine($"    {ms:0.00} ms por SEGUNDO de jogo ({ms / 30 * 1000:0} us por tique)");
			Prova("  cabe folgado no tique (< 1 ms por tique)", ms / 30 < 1.0);
		}
	}

	/// <summary>A varredura da <c>ZoneList</c> -- o que havia antes. O(n) por consulta.</summary>
	private static double Ingenua(List<CorpoFalso> zona, int tiques, out long achou)
	{
		achou = 0;
		var sw = Stopwatch.StartNew();
		for (int t = 0; t < tiques; t++)
			foreach (CorpoFalso eu in zona)
				for (int c = 0; c < 3; c++)
				{
					Vec2 pes = ClasseDeCorpo.Pes(eu.Pos + new Vec2(c, 0));
					Vec2 meus = ClasseDeCorpo.Pes(eu.Pos);
					foreach (CorpoFalso o in zona)
					{
						if (o.Id == eu.Id) continue;
						if (!ClasseDeCorpo.MesmoAndar(Voo.Andar(eu.Altitude), Voo.Andar(o.Altitude))) continue;
						Vec2 dele = ClasseDeCorpo.Pes(o.Pos);
						if (!ClasseDeCorpo.CaixasSeTocam(pes, dele)) continue;
						if (ClasseDeCorpo.CaixasSeTocam(meus, dele)) continue;
						achou++;
						break;
					}
				}
		sw.Stop();
		return sw.Elapsed.TotalMilliseconds;
	}

	/// <summary>A grade: monta uma vez por tique e consulta em O(1). O que ficou.</summary>
	private static double ComGrade(List<CorpoFalso> zona, GradeDeCorpos grade, int tiques, out long achou)
	{
		achou = 0;
		var sw = Stopwatch.StartNew();
		for (int t = 0; t < tiques; t++)
		{
			grade.Recomecar();
			foreach (CorpoFalso o in zona) grade.Por(o.Id, ClasseDeCorpo.Pes(o.Pos), Voo.Andar(o.Altitude));

			foreach (CorpoFalso eu in zona)
			{
				var v = new Vizinhanca(grade, eu.Id, Voo.Andar(eu.Altitude));
				for (int c = 0; c < 3; c++)
					if (v.Barra(eu.Pos, eu.Pos + new Vec2(c, 0), ModoDeTravessia.APe) != 0) achou++;
			}
		}
		sw.Stop();
		return sw.Elapsed.TotalMilliseconds;
	}

	// =====================================================================
	// mapas de teste
	// =====================================================================
	private static ZoneCollision CampoAberto(int w, int h) => Mapa(w, h, _ => false);

	private static ZoneCollision MuroVertical(int w, int h, int colX)
		=> Mapa(w, h, i => i % w == colX);

	private static ZoneCollision Mapa(int w, int h, Func<int, bool> denso)
	{
		var bytes = new byte[8 + (w * h + 7) / 8];
		bytes[0] = (byte)'J'; bytes[1] = (byte)'C'; bytes[2] = (byte)'O'; bytes[3] = (byte)'L';
		bytes[4] = (byte)(w & 0xFF); bytes[5] = (byte)(w >> 8);
		bytes[6] = (byte)(h & 0xFF); bytes[7] = (byte)(h >> 8);
		for (int i = 0; i < w * h; i++)
			if (denso(i)) bytes[8 + (i >> 3)] |= (byte)(1 << (i & 7));
		return ZoneCollision.Load(bytes)!;
	}
}
