using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ NINGUEM ATRAVESSA NINGUEM -- o encanamento ============================
/// A REGRA mora no <see cref="ClasseDeCorpo"/> (Core) e o FUNIL e o <see cref="MoveRules.Advance"/>.
/// Aqui fica so o que e do servidor: **manter uma grade por zona em dia**, uma vez por tique, e
/// entregar a <see cref="Vizinhanca"/> pronta pra quem anda.
///
/// ============================ POR QUE UMA GRADE POR ZONA, E NAO UMA SO ============================
/// Porque a zona ja e o corte de interesse deste servidor: o snapshot sai por zona, o `ZoneList`
/// existe, e dois corpos em planetas diferentes nao tem o que colidir. Uma grade unica pro universo
/// juntaria a Terra e Namek nos mesmos baldes -- e o pior: as coordenadas se repetem entre mapas, entao
/// o (249,250) da Terra e o de Namek cairiam no MESMO balde e as duas pessoas se barrariam a um planeta
/// de distancia.
/// ================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// A GRADE DE CADA ZONA, indexada pelo mesmo hash do <c>ZoneList</c>.
	///
	/// O objeto e REUSADO entre tiques (o `Recomecar` e O(1) e nao aloca -- ver `GradeDeCorpos`),
	/// entao este dicionario cresce ate o numero de zonas visitadas e para. Uma grade vazia custa os
	/// dois arrays de 1024 ints dela e nada por tique.
	/// </summary>
	private readonly Dictionary<ulong, GradeDeCorpos> _gradeDaZona = [];

	/// <summary>
	/// ============================ MONTA AS GRADES DO TIQUE -- **O(n), UMA VEZ** ============================
	/// Este e o metodo que evita o O(n²). A alternativa obvia -- cada corpo varrer o `ZoneList` ao andar
	/// -- custaria n consultas de O(n) POR TIQUE, e o passo faz ate tres consultas (passo cheio + os dois
	/// deslizes), e o arremesso multiplica por cada amostra de meio tile do caminho.
	///
	/// Aqui cada corpo e inserido UMA vez por tique e as consultas viram O(1) amortizado (3x3 baldes de
	/// 32 px, e a caixa tem 16 px de largura -- ver `GradeDeCorpos`).
	///
	/// **RODA ANTES DE QUALQUER UM ANDAR**, e por isso e a primeira linha do `Tick()`: o NPC do
	/// `TickDosCorposSemDono` e o corpo do `TickDoEmpurrao` leem a grade, e uma grade montada depois
	/// deles descreveria o quadro anterior. Todo mundo enxerga o MESMO instante -- que e o que impede
	/// "A parou em B mas B nao parou em A" dentro do mesmo tique.
	/// ======================================================================================================
	/// </summary>
	private void MontarAsGrades()
	{
		// Marca as grades como vazias em O(1). Zonas que perderam o ultimo habitante ficam com a grade
		// zerada -- e nao com a do tique passado, que seria um corpo fantasma barrando quem chega.
		foreach (GradeDeCorpos g in _gradeDaZona.Values) g.Recomecar();

		// ============================ A FONTE E A `ZoneList`, E **NAO** O `_players` ============================
		// A tentacao e varrer `_players.Values` (e a lista "de todo mundo"). Ela perderia o BONECO DO
		// CORPO LARGADO, que e de proposito o unico corpo do jogo que NAO entra no `_players` -- ele
		// aponta pra a MESMA ficha do dono, e estar naquela lista faria o BP, o Ki e a fome serem
		// tiqueados duas vezes no mesmo personagem (ver `GameServer.CorpoLargado.cs`).
		//
		// A `ZoneList` e literalmente "o que existe NESTE lugar": e dela que saem o snapshot, o
		// `AlvoNaFrente` e o alcance das rajadas. Colisao e a mesma pergunta -- **quem ocupa chao aqui**
		// --, entao ela le a mesma lista. Fosse pelo `_players`, o corpo em transe seria o unico do jogo
		// que da pra socar, agarrar e ver, e que ninguem consegue esbarrar.
		// ====================================================================================================
		foreach ((ulong hash, List<ServerPlayer> zona) in _zones)
		{
			if (zona.Count == 0) continue;

			if (!_gradeDaZona.TryGetValue(hash, out GradeDeCorpos? grade))
			{
				grade = new GradeDeCorpos();   // nasce vazia -- ver o `_geracao = 1` do `GradeDeCorpos`
				_gradeDaZona[hash] = grade;
			}

			// ============================ O INTERRUPTOR DO DEFEITO INJETADO ============================
			// FALSO EM JOGO, SEMPRE. Ele so e ligado pela bancada `--doiscorposteste`, e o que ele
			// reproduz e o par de defeitos que este sistema ja teve -- a grade montada com a FONTE
			// errada (`_players`, que perde o boneco e o cadaver) e a grade montada FORA DE HORA
			// (descrevendo o quadro passado). Nos dois casos a consulta responde "nao ha ninguem aqui",
			// que e exatamente o que uma grade vazia responde.
			//
			// Ele mora AQUI e nao dentro da bancada porque o `Mutacao` exige que o criterio seja o MESMO
			// objeto com e sem o defeito: uma copia da colisao escrita na bancada mediria a copia
			// concordando consigo mesma. Custa uma comparacao por zona por tique.
			// ==========================================================================================
			if (_dcGradeCega) continue;

			foreach (ServerPlayer pl in zona)
			{
				if (!EntraNaGrade(pl)) continue;
				grade.Por(pl.Id, ClasseDeCorpo.Pes(pl.Pos), Voo.Andar(pl.Altitude));
			}
		}
	}

	/// <summary>
	/// ============================ QUEM NAO ENTRA NA GRADE -- **um caso so, e ele nao e sobre identidade** ============================
	/// A pergunta foi feita corpo a corpo, como no agarrao, e a resposta e quase sempre "entra". As
	/// decisoes ficam escritas aqui porque a AUSENCIA de um `if` e o que precisa ser lida como escolha:
	///
	///   * **O NOCAUTEADO E O MORTO NO CHAO -- ENTRAM.** O DM nunca desliga `density` por KO nem por
	///     morrer (as unicas coisas que desligam sao o `effect/undense` de uma skill de espada e
	///     obj/turf). E o pedido 3 do dono e literal: *"o corpo mesmo morto TEM TODAS AS INTERACOES DE
	///     UM CORPO VIVO"* -- ocupar chao e uma delas. Perguntar `Ficha.dead` aqui seria reabrir
	///     exatamente a excecao que o `GameServer.Agarrao.cs` gastou um arquivo inteiro pra nao ter.
	///     E o corpo caido nao vira armadilha: da pra AGARRAR e tirar do caminho, que e o mecanismo que
	///     o proprio dono desenhou pra isso.
	///
	///   * **O BONECO DO CORPO LARGADO -- ENTRA.** Mesma razao, e a mesma que o agarrao ja usou pra
	///     deixar agarra-lo: e um corpo deitado no mundo com o dono noutro lugar. Recusar aqui seria a
	///     primeira excecao, e a segunda seria o cadaver.
	///
	///   * **O INTOCAVEL -- ENTRA**, e esta e a que mais tenta virar excecao. `Combate.Intocavel` (a
	///     carencia de renascimento e a cinematica de transformacao) tira alguem da MIRA -- do soco, do
	///     tiro, do agarrao --, e tudo isso e AGRESSAO. Andar para dentro de alguem nao e agressao: e
	///     ocupar o mesmo chao. Um corpo em cinematica esta de pe ali, e atravessa-lo seria mais estranho
	///     que esbarrar. (De quebra, nao precisa de bit novo no snapshot -- o cliente nao sabe quem esta
	///     intocavel, e por isso uma excecao aqui faria as duas pontas discordarem sobre o que barra.)
	///
	///   * **O CARREGADO NO COLO -- **NAO ENTRA**, e e o unico.** E o unico corpo do jogo cuja posicao e
	///     escrita como a de OUTRO (`LevarNoColo` faz `d.Pos = a.Pos`): ele nao ocupa um chao proprio,
	///     ele esta EM alguem que ja ocupa. Deixa-lo na grade poria duas caixas no mesmo ponto e faria
	///     quem carrega ser barrado pelo proprio passageiro -- que o `jaSobrepondo` do `GradeDeCorpos`
	///     resolveria, sim, mas ao preco de quem carrega passar a atravessar TODO mundo enquanto carrega.
	///     Tirar da grade e a resposta honesta: quem esta no colo ja e barrado pelo corpo que o leva.
	/// ========================================================================================================================
	/// </summary>
	private bool EntraNaGrade(ServerPlayer pl) => !SendoCarregado(pl);

	/// <summary>
	/// A <see cref="Vizinhanca"/> DESTE CORPO -- o unico jeito de alguem no servidor pegar a grade.
	///
	/// Ela junta as tres coisas que nunca podem se separar (a grade DA ZONA dele, o id DELE e o andar
	/// DELE); passar as tres soltas seria convidar o dia em que alguem consulta a grade da Terra com o
	/// andar de quem esta em Namek.
	/// </summary>
	private Vizinhanca VizinhancaDe(ServerPlayer pl)
		=> new(_gradeDaZona.GetValueOrDefault(pl.Zone.Hash), pl.Id, Voo.Andar(pl.Altitude));

	/// <summary>
	/// ============================ TODO CORPO QUE EXISTE NO MUNDO -- e nao todo corpo SIMULADO ============================
	/// A mesma fonte que o <see cref="MontarAsGrades"/> ja usa, e pela mesma razao, escrita oitenta
	/// linhas acima: **`_players` quer dizer "um corpo que o servidor SIMULA", e nem todo corpo que
	/// existe num lugar e simulado.** Sao tres, hoje:
	///
	///   * o **BONECO DO CORPO LARGADO** -- fica fora do `_players` porque compartilha a ficha do dono;
	///   * o **CADAVER** -- fica fora porque nao e simulado (o `TickDoEstomago` nao pergunta `dead`, e
	///     um cadaver com fome se desfaria sozinho por um relogio invisivel);
	///   * e amanha o terceiro.
	///
	/// ============================ ELA NASCEU DE DOIS DEFEITOS MEDIDOS, E OS DOIS SAO ANTIGOS ============================
	/// **Nenhum dos dois foi introduzido pelo cadaver -- os dois ja estavam la, contra o boneco**, e a
	/// documentacao do repo afirmava o contrario nos dois casos:
	///
	///   1. `GameServer.CorpoLargado.cs` diz *"o boneco e um corpo comum, entao um soco o empurra"*. NAO
	///      empurrava: o `TickDoEmpurrao` varria `_players.Values`, entao o boneco recebia `TiquesDeVoo`
	///      e ficava com eles pra sempre -- parado, e (por `Deitado`) desenhado deitado ate alguem
	///      voltar pro corpo;
	///   2. `GameServer.Agarrao.cs` diz, num bloco proprio, que o boneco **PODE** ser agarrado. Nao
	///      podia: `CorpoNaFrente` o achava (varre a `ZoneList`) e o `Prender` escrevia os dois lados,
	///      mas no tique seguinte o `_players.TryGetValue(a.AgarrandoId)` falhava e o aperto era
	///      desfeito na hora. Agarrar quem esta meditando "nao fazia nada", sem mensagem.
	///
	/// Trocar a FONTE conserta os dois e faz o cadaver funcionar sem uma linha propria -- que e a
	/// instrucao da tarefa em letra: *"sai de graca se o agarrao foi escrito sobre corpo; se nao saiu,
	/// volte e conserte la em vez de abrir excecao aqui"*.
	///
	/// ============================ ELA COPIA, E A COPIA E A PARTE IMPORTANTE ============================
	/// Nao e um `yield return` sobre as listas vivas, e nao pode ser: os dois consumidores MEXEM em
	/// corpo enquanto andam -- o arremesso solta um agarrao ao acertar alguem, e a varredura orfa
	/// manda ficha. Iterar a colecao viva e o "Collection was modified" que o `_npcsPraTirar` e o
	/// `TickDeQuemVolta` deste port ja existem pra evitar; um iterador preguicoso so adiaria a mesma
	/// bomba pro dia em que um deles crescesse.
	///
	/// A LISTA E DE INSTANCIA E REUSADA (o `Clear` nao devolve capacidade), pelo mesmo argumento do
	/// `_npcsPraTirar`: alocar uma lista por tique num laco de 30 Hz e lixo constante pro coletor. Os
	/// dois consumidores rodam **em sequencia** e cada um a REENCHE no proprio topo -- ninguem a
	/// guarda entre chamadas, e por isso um buffer basta.
	///
	/// **NAO SUBSTITUI O `_players` EM LUGAR NENHUM ALEM DESSES DOIS LACOS.** Ela e a lista de "quem
	/// tem corpo aqui", nao a de "quem o servidor simula" -- usa-la no `TickFichas` seria treinar
	/// cadaver.
	/// ================================================================================================================
	/// </summary>
	private List<ServerPlayer> TodosOsCorpos()
	{
		_corposDoMundo.Clear();
		foreach (List<ServerPlayer> zona in _zones.Values) _corposDoMundo.AddRange(zona);
		return _corposDoMundo;
	}

	/// <summary>O buffer do <see cref="TodosOsCorpos"/> -- ver la por que ele e um so.</summary>
	private readonly List<ServerPlayer> _corposDoMundo = [];
}
