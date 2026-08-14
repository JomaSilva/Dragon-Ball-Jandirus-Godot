using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Skills;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DO DISCIPULADO -- `--mestreteste`. Porte de `Code/Modules/Players/MasterStudent.dm`.
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// As regras puras (<see cref="Discipulado"/>) se provam com numeros na mao. O que elas **nao**
/// conseguem provar e a CORRENTE, que e onde este port ja quebrou muitas vezes:
///
///   convidar -> aceitar -> o vinculo grava -> treinar rende mais -> o mestre provoca ->
///   a porta cai pela metade -> o aluno desperta -> **e consegue reentrar sozinho depois**
///
/// Cada elo mora num arquivo diferente (Core, `GameServer.Mestre.cs`, `GameServer.Formas.cs`,
/// `GameServer.Combat.cs`), e os quatro podem estar certos com a corrente arrebentada no meio. Foi
/// literalmente o estado deste projeto ate hoje: o `Fighter.FightGainMult` tinha o ramo do mestre
/// inteiro, testado, e **nenhum dos sete chamadores passava o segundo argumento**.
/// ==============================================================================
///
/// AS SETE SECOES:
///  1. AS EXCLUSOES SAO DERIVADAS -- e a lista sai de campos, nao de ids escritos a mao.
///  2. O PORTAO DE 3x -- e ele le o BP **BASE**, com o expresso mentindo de proposito.
///  3. O TETO DE 5 ALUNOS.
///  4. O GANHO -- os numeros, a assimetria, e a varredura que prova que os 7 chamadores passam o bit.
///  5. A TESTEMUNHA -- e o RAIO dela, que e o "teto que nunca dispara" deste sistema.
///  6. O DESPERTAR ASSISTIDO -- a metade, a ordem da raiva, e a REENTRADA depois.
///  7. RECARGA, PERSISTENCIA, LOGOUT E ASSINATURA RECICLADA.
///
/// ============================ OS CORPOS SAO FORJADOS, E O ARQUIVO E DEVOLVIDO ============================
/// Mesmo padrao do `OConvivioAoVivo`: corpos inventados numa zona pre-feita onde nao ha ninguem
/// conectado, entrando e saindo do `_players`/`ZoneList` dentro do mesmo bloco sincrono.
///
/// COM UMA PRECAUCAO A MAIS QUE SO ESTA BANCADA PRECISA: o discipulado **grava em disco na hora**
/// (`mestres.txt`, mesmo ciclo de vida do `cargos.txt`). Uma bancada que vincula dois corpos
/// forjados deixaria assinaturas de bancada no arquivo do mundo. Por isso os dois dicionarios sao
/// FOTOGRAFADOS na entrada e restaurados (com regravacao) no `finally` -- rodar a bancada tem que
/// terminar como comecou, inclusive no disco.
/// ======================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Faixa de ids de bancada -- bem longe do `_nextId`, que comeca em 1.</summary>
	private const int IdBaseDoMestreDeTeste = 90_300;

	private int _mstOk, _mstFalhou;

	private void AfirmarMst(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _mstOk++; GD.Print($"[mestre]   OK    {oque}"); return; }
		_mstFalhou++;
		GD.PrintErr($"[mestre]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDoMestre()
	{
		_mstOk = _mstFalhou = 0;
		GD.Print("[mestre] ================ MESTRE E ALUNO ================");

		// A FOTO DO ESTADO DE VERDADE. Ver o cabecalho.
		var vinculosReais = new Dictionary<string, string>(_mestreDe, StringComparer.Ordinal);
		var nomesReais = new Dictionary<string, string>(_nomePorAssinatura, StringComparer.Ordinal);
		_mestreDe.Clear();
		_nomePorAssinatura.Clear();

		ZoneKey zona = ZoneKey.Premade(
			Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		ServerPlayer Forjar(int i, string nome, float tilesX, double bp)
		{
			var novo = new ServerPlayer
			{
				Id = IdBaseDoMestreDeTeste + i,
				Peer = null,
				Name = nome,
				Race = "Saiyan",
				Genero = "Male",
				Idade = 25,
				Zone = zona,
				Pos = new Vec2(tilesX * ZoneCollision.TileSize, 0),
				// A CONTA E O SLOT SAO O QUE DAO ASSINATURA a este corpo, e a assinatura E a chave do
				// discipulado inteiro: sem eles a bancada mediria o nada (ver `EhPessoa`).
				Conta = $"bancada_mestre_{i}",
				Slot = 0,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = "Saiyan", BP = bp },
			};
			novo.Ficha.Class = "Normal";
			PorNoMundo(novo);
			novo.Ficha.Ki = novo.Ficha.MaxKi;
			return novo;
		}

		// O MESTRE e forte; o ALUNO tem BP de sobra pra ser aluno dele; o TESTEMUNHA fica colado
		// (3 tiles, dentro dos 8) e o DISTANTE a 40 tiles -- e ele que prova o raio.
		ServerPlayer mestre = Forjar(1, "bancada: o mestre", 0, 9_000_000);
		ServerPlayer aluno = Forjar(2, "bancada: o aluno", 1, 3_000_000);
		ServerPlayer perto = Forjar(3, "bancada: quem estava perto", 3, 1_000);
		ServerPlayer longe = Forjar(4, "bancada: quem estava longe", 40, 1_000);

		try
		{
			AsExclusoesSaoDerivadas();
			OPortaoDeTresVezes(mestre, aluno);
			OTetoDeCincoAlunos(mestre, aluno, zona);
			OGanhoContraOMestre(mestre, aluno);
			ATestemunhaTemRaio(mestre, perto, longe);
			ODespertarAssistido(mestre, aluno);
			RecargaPersistenciaELogout(mestre, aluno);
		}
		catch (Exception e) { AfirmarMst($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? ""); }
		finally
		{
			foreach (ServerPlayer p in new[] { mestre, aluno, perto, longe })
			{
				_players.Remove(p.Id);
				ZoneList(zona.Hash).Remove(p);
			}
			_mestreDe.Clear();
			_nomePorAssinatura.Clear();
			foreach (var kv in vinculosReais) _mestreDe[kv.Key] = kv.Value;
			foreach (var kv in nomesReais) _nomePorAssinatura[kv.Key] = kv.Value;
			SalvarMestres();
		}

		GD.Print($"[mestre] ================ {_mstOk} passaram, {_mstFalhou} falharam ================");
	}

	// =====================================================================
	// 1) AS EXCLUSOES SAO DERIVADAS
	// =====================================================================
	/// <summary>
	/// A lista de ensinaveis sai de CAMPOS do catalogo, e nao de ids escritos a mao -- e as
	/// exclusoes que a spec nomeia (Ultra Instinto, Ego, laboratorio Bio, saga Majin) caem de cada
	/// criterio, uma por uma.
	/// </summary>
	private void AsExclusoesSaoDerivadas()
	{
		GD.Print("[mestre] -- 1) O QUE SE ENSINA, DERIVADO DOS CAMPOS");

		bool Tem(string id) => Discipulado.Ensinaveis.Any(d => d.Id == id);

		AfirmarMst("os quatro que a spec pede e que existem no catalogo entram (SSJ1/2/3 + lssj1)",
				   Tem("ssj1") && Tem("ssj2") && Tem("ssj3") && Tem(Catalogo.IdWrathful),
				   string.Join(", ", Discipulado.Ensinaveis.Select(d => d.Id)));

		// AS DUAS DISCIPLINAS -- a exclusao que a spec grifa. Elas tem ensino PROPRIO, em cadeia,
		// com raiz no cargo (`GameServer.Disciplinas.EnsinarDisciplina`). Duas portas pra mesma
		// coisa e o defeito que este teste existe pra impedir.
		AfirmarMst("Ultra Instinto e Ultra Ego NAO se ensinam por aqui (eles tem cadeia propria)",
				   !Tem("ui_sign") && !Tem("ui_perfected") && !Tem("destroyer") && !Tem("ultra_ego"));

		AfirmarMst("...nem nenhuma forma das linhas divinas (SSG/Blue/Rose): ensino proprio",
				   !Discipulado.Ensinaveis.Any(d => d.Linha is LinhaDeForma.GodKi or LinhaDeForma.GodKiRose
													or LinhaDeForma.UltraInstinct or LinhaDeForma.UltraEgo));

		AfirmarMst("...nem o Mistico (dom de ritual de Kaioshin) nem o Beast", !Tem(Catalogo.IdMistico) && !Tem("beast"));
		AfirmarMst("...nem o Oozaru: ninguem ensina ninguem a ter lua cheia",
				   !Discipulado.Ensinaveis.Any(d => d.Linha == LinhaDeForma.Oozaru));
		AfirmarMst("...nem o SSJ4, que pede maestria no OOZARU DOURADO (maestria de OUTRA linha)", !Tem("ssj4"));
		AfirmarMst("...nem Full Power / Limit Breaker, que pedem o degrau anterior dominado INTEIRO",
				   !Tem("ssj4_full_power") && !Tem("ssj4_limit_breaker")
				   && !Tem("primal_legendary4_full_power") && !Tem("primal_legendary4_limit_breaker"));
		AfirmarMst("...nem os grades (USSJ), que sao ramo lateral aberto por MAESTRIA",
				   !Tem("grade2") && !Tem("grade3"));

		// ============================ O CRITERIO INTEIRO, DE UM SO GOLPE ============================
		// **Toda** entrada da lista tem uma porta de BP -- que e exatamente a coisa que o `MST_HALF`
		// corta. Uma forma sem isso na lista seria uma forma que o mestre "ensina" sem ter o que dar.
		//
		// ============================ O `ChaveDoLimiar` SAIU DAQUI, E ELE ESTAVA ERRADO ============================
		// Esta linha cobrava `PortaBp > 0 **&&** ChaveDoLimiar.Length > 0`, uma copia exata da condicao
		// que o `Discipulado.Ensinavel` tinha e que a sessao do Frost Demon ja havia removido de la --
		// com o motivo escrito: as duas evolucoes do Frost Demon tem porta de BP e **nao** tem limiar
		// pessoal (o `FD_FORM6_AT`/`FD_FORM7_AT` sao `#define`, nao ha `RolarIcer`), e o DM as ensina
		// assim mesmo (`IcerTransform.dm:89` multiplica o proprio `#define` por `mst_desc`).
		//
		// A bancada ficou pra tras e reprovava desde entao, sem que a lista estivesse errada: era ela
		// afirmando uma regra que o codigo tinha deixado de ter. **E o modo de falhar disto e o pior de
		// todos os dois lados** -- uma bancada vermelha por um motivo velho e uma bancada que ninguem
		// le mais, e a proxima falha de verdade entra no meio das ja aceitas.
		//
		// O que ela mede agora e a regra que sobrou, e ela e a que o `MST_HALF` de fato usa: o corte se
		// aplica ao limiar pessoal QUANDO ele existe, e ao valor de fabrica quando nao existe (ver o
		// passo 8 do `EstadoDeForma.Avaliar`, que ja cai num quando o outro devolve zero).
		// ======================================================================================================
		AfirmarMst("toda forma ensinavel tem porta de BP -- e ela que o MST_HALF corta",
				   Discipulado.Ensinaveis.All(d => d.PortaBp > 0),
				   string.Join(", ", Discipulado.Ensinaveis.Where(d => d.PortaBp <= 0).Select(d => d.Id)));

		// E O QUE SE **COMPRA** FICA DE FORA -- ver o passo (1b) do `Discipulado.Ensinavel`. Sao as tres
		// formas com `PedeFlag` (Super Namekuseijin e as duas Alien): elas TEM porta de BP, entao a
		// linha acima sozinha as aceitaria, e o DM nao as ensina (`mst_form_seal` lista oito tags e
		// nenhuma delas esta la). O discipulado provoca um DESPERTAR; numa forma comprada nao ha o que
		// provocar.
		AfirmarMst("...e nenhuma forma COMPRADA com marcos entra (Super Namekuseijin, formas Alien)",
				   !Discipulado.Ensinaveis.Any(d => d.PedeFlag != null),
				   string.Join(", ", Discipulado.Ensinaveis.Where(d => d.PedeFlag != null).Select(d => d.Id)));

		// E AS DUAS FORMAS HERAN **ENTRAM**, que e a outra metade da mesma regra: elas sao raciais e
		// nao-Saiyajin como as tres de cima, e mesmo assim se ensinam -- porque nao se compram. Sem
		// esta linha, a de cima passaria num mundo em que "raca nao-Saiyajin" fosse o criterio.
		AfirmarMst("...mas as duas do HERAN sim -- o `mst_form_seal` do DM lista `heran1` e `heran2`",
				   Tem("heran1") && Tem("heran2"));

		// E A LISTA NAO E UMA LISTA: e o filtro rodando sobre o catalogo inteiro. Se alguem trocar a
		// derivacao por ids escritos a mao, esta linha continua verde -- por isso ela vem
		// acompanhada da de cima e das de baixo, que falam dos CAMPOS.
		AfirmarMst("a lista e o filtro aplicado ao catalogo inteiro (nenhum id fora dele)",
				   Discipulado.Ensinaveis.Count == Catalogo.Todas.Count(Discipulado.Ensinavel),
				   $"{Discipulado.Ensinaveis.Count} entradas");

		// A FORMA DE BASE NAO E ENSINAVEL -- guarda boba, e ela e a que impediria "o mestre te
		// ensina a ficar normal".
		AfirmarMst("a forma base nao e ensinavel", !Discipulado.Ensinavel(Catalogo.Def(Catalogo.IdBase)));

		OConjuntoInteiro();
	}

	// =====================================================================
	// 1b) O CONJUNTO INTEIRO -- forma por forma, sem excecao
	// =====================================================================
	/// <summary>
	/// ============================ AS CHECAGENS POR ID SAO CEGAS PRA ENTRADA NOVA ============================
	/// Tudo acima desta secao pergunta *"fulano esta na lista?"* e *"nenhum ciclano esta?"*, e as duas
	/// perguntas sao **surdas ao que ninguem nomeou**. O criterio unico que existia -- "toda ensinavel
	/// tem porta de BP" -- e verdadeiro pra metade do catalogo: as onze formas de escada de sangue
	/// passam por ele, e a lista continuaria certa por acidente se a derivacao mudasse.
	///
	/// **A LINHA DO FROST DEMON PROVOU ISSO NA PRATICA.** Quando as sete entradas dela nasceram, DUAS
	/// entraram na lista de ensinaveis sozinhas -- e isso era o certo (`mst_teachable` lista `frost6` e
	/// `frost7`) -- mas ninguem teria notado se fossem SETE: nenhuma checagem daqui cobria as cascas
	/// pelo nome, e "toda ensinavel tem porta de BP" continuaria verde, porque a forma base do Frost
	/// Demon nao tem porta e as quatro supressoes tambem nao. Foi sorte, nao teste.
	///
	/// ============================ ENTAO A LISTA E ESCRITA A MAO, E DE PROPOSITO ============================
	/// Este e o unico lugar desta area onde ids literais sao a resposta certa, e o motivo e o mesmo da
	/// tabela de paridade da bancada `formas`: gerar o esperado a partir do `Discipulado.Ensinavel`
	/// faria a checagem provar a si mesma. As oito primeiras sao as tags do `mst_form_seal` do DM
	/// (`MasterStudent.dm:393`); as cinco seguintes sao a divergencia que o proprio `Discipulado`
	/// declara por escrito no cabecalho de `Ensinaveis`, com o ganho e a perda anotados.
	///
	/// E O EFEITO PRETENDIDO E ESTE: uma entrada nova que caia na lista deixa a bancada VERMELHA ate
	/// alguem decidir se ela devia cair. O cabecalho do `Discipulado` diz que a fidelidade estrita "e
	/// uma linha, e fica anotado aqui pra ser uma decisao e nao um descuido" -- esta secao e o que
	/// transforma a anotacao em decisao obrigatoria.
	/// ==================================================================================================
	/// </summary>
	private void OConjuntoInteiro()
	{
		string[] esperado =
		[
			// AS OITO DO `mst_form_seal` -- as unicas que o DM ensina.
			"ssj1", "ssj2", "ssj3", Catalogo.IdWrathful, "heran1", "heran2", "frost6", "frost7",

			// AS CINCO DIVERGENCIAS DECLARADAS. O `future_ssj` e o SSJ1 do Futuro (o DM concorda: o
			// `mst_form_open("ssj")` nao exclui `FutureLineage`; o port so partiu a linha em duas); as
			// quatro Legendary vem da limitacao de REPRESENTACAO do original, onde nao havia proc que
			// um mestre pudesse disparar. Ver o cabecalho de `Discipulado.Ensinaveis`.
			"future_ssj", "c_type", "legendary", "primal_c_type", "primal_legendary",
			"primal_legendary2",
		];

		string[] hoje = [.. Discipulado.Ensinaveis.Select(d => d.Id).OrderBy(x => x, StringComparer.Ordinal)];
		string[] sobrando = [.. hoje.Except(esperado)];
		string[] faltando = [.. esperado.Except(hoje)];

		AfirmarMst($"a lista de ensinaveis e EXATAMENTE o conjunto esperado ({hoje.Length} formas)",
				   sobrando.Length == 0 && faltando.Length == 0,
				   $"sobrando: [{string.Join(", ", sobrando)}] | faltando: [{string.Join(", ", faltando)}]");

		// E O RECORTE DA LINHA DO FROST DEMON, FORMA POR FORMA -- as duas Evolucoes dentro, as cinco
		// cascas fora. Ela ganha checagem propria porque e a unica linha do jogo em que a divisao NAO
		// e "tudo ou nada": um criterio novo que pegasse a linha inteira (pra dentro ou pra fora)
		// continuaria passando na checagem de conjunto acima se alguem atualizasse a lista sem pensar.
		foreach (FormaDef d in Catalogo.DaLinha(LinhaDeForma.FrostDemon))
		{
			bool devia = d.Ordem >= 6;
			AfirmarMst($"`{d.Id}` ({d.Nome}): {(devia ? "SE ensina" : "NAO se ensina")}",
					   Discipulado.EhEnsinavel(d.Id) == devia,
					   $"EhEnsinavel = {Discipulado.EhEnsinavel(d.Id)}");
		}
	}

	// =====================================================================
	// 2) O PORTAO DE 3x, E ELE LE O BP BASE
	// =====================================================================
	private void OPortaoDeTresVezes(ServerPlayer mestre, ServerPlayer aluno)
	{
		GD.Print("[mestre] -- 2) O PORTAO DE 3x, SOBRE O BP BASE");

		_mestreDe.Clear();
		double bpAluno = aluno.Ficha.BP;

		mestre.Ficha.BP = bpAluno * Discipulado.RazaoDeBp;
		AfirmarMst($"exatamente {Discipulado.RazaoDeBp:0}x ja basta (`>=`, e nao `>`)",
				   Discipulado.MestreForteOBastante(mestre.Ficha.BP, bpAluno));

		mestre.Ficha.BP = bpAluno * Discipulado.RazaoDeBp - 1;
		AfirmarMst("um ponto abaixo de 3x nao basta",
				   !Discipulado.MestreForteOBastante(mestre.Ficha.BP, bpAluno));

		// PELO VERB DE VERDADE, e nao so pela funcao pura: e o `ConvidarAluno` que decide qual
		// numero passar.
		Encostar(mestre, aluno, tiles: 1);
		ConvidarAluno(mestre);
		AfirmarMst("...e o convite pelo verb tambem e recusado (nenhum pedido pendente sobrou)",
				   aluno.PedidoDoMestre == null);

		// ============================ O BP **BASE**, COM O EXPRESSO MENTINDO ============================
		// A PARTE 3 da spec: *"expressedBP NAO E BP -- personagem novo expressa entre 2 e 21 enquanto
		// o BP bruto e bem maior"*. Aqui os dois numeros sao postos em desacordo DE PROPOSITO: pelo
		// BP o mestre e 3x mais forte; pelo expresso ele e mil vezes mais FRACO. Se o portao lesse o
		// expresso (ou se alguem "consertar" pra ler um dia), o convite seria recusado.
		mestre.Ficha.BP = bpAluno * Discipulado.RazaoDeBp;
		mestre.Ficha.expressedBP = 10;
		aluno.Ficha.expressedBP = 10_000;

		ConvidarAluno(mestre);
		AfirmarMst("o portao le o BP BASE: mestre com 3x de BP e 1/1000 do poder EXPRESSO convida igual",
				   aluno.PedidoDoMestre is { Despertar: false },
				   $"BP {mestre.Ficha.BP:N0} x {aluno.Ficha.BP:N0} | expresso {mestre.Ficha.expressedBP:N0} x {aluno.Ficha.expressedBP:N0}");

		ResponderAoMestre(aluno, aceitou: true);
		AfirmarMst("...e o aceite fecha o vinculo", MestreDe(aluno.Assinatura) == mestre.Assinatura);
		AfirmarMst("...e o pendente e consumido no gesto", aluno.PedidoDoMestre == null);

		// UM ALUNO, UM MESTRE -- e quem garante isso e a CHAVE do dicionario ser o aluno.
		aluno.PedidoDoMestre = null;
		ConvidarAluno(mestre);
		AfirmarMst("quem ja tem mestre nao recebe convite de ninguem", aluno.PedidoDoMestre == null);

		// E A RECUSA E RESPONDIDA: um "nao" nao pode deixar o pendente pendurado pra sempre.
		Desvincular(aluno.Assinatura, "bancada");
		ConvidarAluno(mestre);
		ResponderAoMestre(aluno, aceitou: false);
		AfirmarMst("o 'nao' do aluno consome o pedido e nao vincula ninguem",
				   aluno.PedidoDoMestre == null && MestreDe(aluno.Assinatura).Length == 0);

		// O PRAZO -- ele existe porque aqui nao ha `alert()` bloqueante como no BYOND.
		ConvidarAluno(mestre);
		if (aluno.PedidoDoMestre is { } p) aluno.PedidoDoMestre = p with { Ate = NowMs() - 1 };
		ResponderAoMestre(aluno, aceitou: true);
		AfirmarMst("convite VENCIDO nao vincula (o prazo que substitui o alert() do BYOND)",
				   MestreDe(aluno.Assinatura).Length == 0 && aluno.PedidoDoMestre == null);

		// E VOLTA A VALER pro resto da bancada.
		ConvidarAluno(mestre);
		ResponderAoMestre(aluno, aceitou: true);
		AfirmarMst("...e um convite novo, dentro do prazo, vincula",
				   MestreDe(aluno.Assinatura) == mestre.Assinatura);
	}

	// =====================================================================
	// 3) O TETO DE CINCO ALUNOS
	// =====================================================================
	private void OTetoDeCincoAlunos(ServerPlayer mestre, ServerPlayer aluno, ZoneKey zona)
	{
		GD.Print("[mestre] -- 3) O TETO DE CINCO ALUNOS");

		// O ALUNO DA SECAO 2 SAI DE PERTO. Ver `Estacionar`: com ele colado no mestre, TODO verb de
		// alvo desta secao acertaria ele em vez do corpo que a bancada esta olhando.
		Estacionar(aluno, 200);

		// ============================ O TETO TEM QUE DISPARAR ============================
		// PARTE 3, armadilha 3: *"um teto que nunca e atingido e indistinguivel de teto nenhum"*.
		// Entao a bancada enche a lista ate o limite e tenta o sexto -- pelo VERB, nao pelo
		// dicionario.
		// ================================================================================
		var extras = new List<ServerPlayer>();
		try
		{
			for (int i = 0; i < Discipulado.MaxAlunos + 1; i++)
			{
				var novo = new ServerPlayer
				{
					Id = IdBaseDoMestreDeTeste + 20 + i,
					Peer = null, Name = $"bancada: aluno {i}", Race = "Saiyan", Genero = "Male",
					Idade = 25, Zone = zona,
					// NASCEM LONGE, um em cada endereco. So o corpo que a bancada esta convidando
					// se aproxima -- ver `Estacionar`.
					Pos = new Vec2((300 + i * 20) * ZoneCollision.TileSize, 0),
					Conta = $"bancada_mestre_extra_{i}", Slot = 0,
					Ficha = new Jandirus.Core.Stats.Fighter { Race = "Saiyan", BP = 1_000 },
				};
				novo.Ficha.Class = "Normal";
				PorNoMundo(novo);
				extras.Add(novo);
			}

			// o aluno da secao 2 ainda esta vinculado (so foi pra longe): ele CONTA no teto.
			int jaTem = ContarAlunos(mestre.Assinatura);
			for (int i = 0; i < extras.Count; i++)
			{
				if (ContarAlunos(mestre.Assinatura) >= Discipulado.MaxAlunos) break;
				Encostar(mestre, extras[i], tiles: 1);
				ConvidarAluno(mestre);
				ResponderAoMestre(extras[i], aceitou: true);
				Estacionar(extras[i], 300 + i * 20);
			}

			AfirmarMst($"o mestre chega a {Discipulado.MaxAlunos} alunos",
					   ContarAlunos(mestre.Assinatura) == Discipulado.MaxAlunos,
					   $"{ContarAlunos(mestre.Assinatura)} (comecou com {jaTem})");

			ServerPlayer sobrando = extras[^1];
			Encostar(mestre, sobrando, tiles: 1);
			ConvidarAluno(mestre);
			AfirmarMst("...e o sexto e RECUSADO (o teto dispara mesmo)", sobrando.PedidoDoMestre == null);
			Estacionar(sobrando, 500);

			// DISPENSAR ABRE VAGA -- senao o teto viraria uma porta de mao unica.
			ServerPlayer dispensado = extras[0];
			Encostar(mestre, dispensado, tiles: 1);
			DispensarAluno(mestre);
			Estacionar(dispensado, 520);
			AfirmarMst("dispensar um aluno abre vaga",
					   ContarAlunos(mestre.Assinatura) == Discipulado.MaxAlunos - 1
					   && MestreDe(dispensado.Assinatura).Length == 0);

			Encostar(mestre, sobrando, tiles: 1);
			ConvidarAluno(mestre);
			ResponderAoMestre(sobrando, aceitou: true);
			AfirmarMst("...e agora o que sobrava entra",
					   MestreDe(sobrando.Assinatura) == mestre.Assinatura);

			// E O ALUNO SAI SOZINHO se quiser.
			LargarOMestre(sobrando);
			AfirmarMst("o aluno pode largar o mestre por conta propria",
					   MestreDe(sobrando.Assinatura).Length == 0);
		}
		finally
		{
			// o aluno da secao 2 volta pro lado do mestre, e sem convite pendurado
			Encostar(mestre, aluno, tiles: 1);
			aluno.PedidoDoMestre = null;
			foreach (ServerPlayer p in extras)
			{
				PurgarAssinatura(p.Assinatura);
				_players.Remove(p.Id);
				ZoneList(zona.Hash).Remove(p);
			}
		}
	}

	// =====================================================================
	// 4) O GANHO
	// =====================================================================
	private void OGanhoContraOMestre(ServerPlayer mestre, ServerPlayer aluno)
	{
		GD.Print("[mestre] -- 4) TREINAR CONTRA O PROPRIO MESTRE");

		GarantirVinculo(mestre, aluno);

		// os dois com o mesmo poder EXPRESSO: assim o bonus geral (que le o expresso) vale 1x e o
		// unico numero em jogo e o do mestre, que le o BP BASE.
		mestre.Ficha.expressedBP = aluno.Ficha.expressedBP = 1_000;
		aluno.Ficha.BP = 1_000_000;

		mestre.Ficha.BP = aluno.Ficha.BP * 3;
		double comMestre = aluno.Ficha.FightGainMult(mestre.Ficha, ehMeuMestre: true);
		double semMestre = aluno.Ficha.FightGainMult(mestre.Ficha, ehMeuMestre: false);
		AfirmarMst("mestre 3x mais forte (BP base) rende 3x -- contra 1x de um estranho igual",
				   Math.Abs(comMestre - 3) < 1e-9 && Math.Abs(semMestre - 1) < 1e-9,
				   $"{comMestre:0.###} x {semMestre:0.###}");

		mestre.Ficha.BP = aluno.Ficha.BP * 50;
		AfirmarMst($"...e o teto e {Jandirus.Core.Stats.GainKnobs.MasterGainCap:0}x, por mais forte que ele seja",
				   Math.Abs(aluno.Ficha.FightGainMult(mestre.Ficha, true) - Jandirus.Core.Stats.GainKnobs.MasterGainCap) < 1e-9);

		mestre.Ficha.BP = aluno.Ficha.BP * 1.1;
		AfirmarMst($"...e abaixo do piso ({Jandirus.Core.Stats.GainKnobs.MasterGainFloor:0.0}x) o bonus nao vale (mestre fraco demais nao ensina)",
				   Math.Abs(aluno.Ficha.FightGainMult(mestre.Ficha, true) - 1) < 1e-9);

		// TER MESTRE NUNCA PODE PIORAR: se o gap de poder EXPRESSO paga mais que o bonus, vale o gap.
		aluno.Ficha.expressedBP = 100;
		mestre.Ficha.expressedBP = 100_000;
		AfirmarMst("ter mestre nunca PIORA o ganho: com o mestre fraco em BP e forte em poder, vale o teto geral",
				   Math.Abs(aluno.Ficha.FightGainMult(mestre.Ficha, true)
							- Jandirus.Core.Stats.GainKnobs.FightGainCap) < 1e-9);

		// A ASSIMETRIA: o bonus e de quem APRENDE.
		mestre.Ficha.BP = aluno.Ficha.BP * 3;
		AfirmarMst("o vinculo e assimetrico: o aluno reconhece o mestre, e o mestre nao 'reconhece' o aluno",
				   EhMeuMestre(aluno, mestre) && !EhMeuMestre(mestre, aluno));
		AfirmarMst("corpo sem assinatura (NPC, clone) nunca e mestre de ninguem",
				   !EhMeuMestre(aluno, null));

		// ============================ E A VARREDURA: O BIT CHEGA AO COMBATE ============================
		// Esta e a checagem que fecha a divida deste sistema. O `FightGainMult` tem o ramo do mestre
		// desde que foi portado e os SETE chamadores de producao passavam um argumento so -- regra
		// escrita, regra desligada, e nada no projeto acusava (armadilha 1 da PARTE 3).
		//
		// Ela le o FONTE porque a afirmacao e sobre QUEM CHAMA, e nao sobre um valor: um teste de
		// comportamento so ve os chamadores que existem hoje, e o proximo golpe novo e, por
		// definicao, o que ainda nao foi escrito.
		// ==========================================================================================
		string[] fontes = ["Server/GameServer.Combat.cs", "Server/GameServer.Projeteis.cs",
						   "Server/GameServer.Tecnicas.G2.cs", "Server/GameServer.Tecnicas.G3.cs"];
		int comBit = 0, semBit = 0;
		foreach (string f in fontes)
			foreach (string bruta in Fonte(f))
			{
				string l = SemTextoNemComentario(bruta);
				int i = l.IndexOf("FightGainMult(", StringComparison.Ordinal);
				if (i < 0) continue;
				if (l.IndexOf(',', i) >= 0) comBit++; else semBit++;
			}
		AfirmarMst("todos os chamadores de producao do `FightGainMult` passam o bit do mestre",
				   semBit == 0 && comBit >= 7, $"{comBit} com bit, {semBit} sem");
	}

	// =====================================================================
	// 5) A TESTEMUNHA, E O RAIO DELA
	// =====================================================================
	private void ATestemunhaTemRaio(ServerPlayer mestre, ServerPlayer perto, ServerPlayer longe)
	{
		GD.Print("[mestre] -- 5) A TESTEMUNHA (E O RAIO, QUE E O QUE PODE NAO DISPARAR)");

		perto.FormasVistas.Clear();
		longe.FormasVistas.Clear();
		mestre.Forma.Entrar(Catalogo.IdBase);

		FormaDef ssj1 = Catalogo.Def("ssj1")!;
		Encostar(mestre, perto, tiles: 3);
		longe.Pos = new Vec2(40 * ZoneCollision.TileSize, 0);

		// PELO FUNIL DE VERDADE: `EntrarNaForma` -> `AnunciarForma` -> o laco da zona.
		EntrarNaForma(mestre, ssj1);

		AfirmarMst("quem estava a 3 tiles VIU a forma",
				   perto.FormasVistas.Contains(ssj1.IdRede), string.Join(",", perto.FormasVistas));
		AfirmarMst($"quem estava a 40 tiles NAO viu (o raio de {Discipulado.TilesDeTestemunha} tiles dispara "
				   + "-- o laco do anuncio e da ZONA INTEIRA)",
				   !longe.FormasVistas.Contains(ssj1.IdRede));
		AfirmarMst("...e quem se transformou nao 've a si mesmo' (isso e a estreia, outra pergunta)",
				   !mestre.FormasVistas.Contains(ssj1.IdRede));

		// A BORDA: 8 tiles ve, 9 nao. Sem isto o raio poderia estar em qualquer numero grande.
		perto.FormasVistas.Clear();
		mestre.Forma.Entrar(Catalogo.IdBase);
		Encostar(mestre, perto, tiles: Discipulado.TilesDeTestemunha);
		EntrarNaForma(mestre, ssj1);
		AfirmarMst($"na borda exata ({Discipulado.TilesDeTestemunha} tiles) ainda ve",
				   perto.FormasVistas.Contains(ssj1.IdRede));

		perto.FormasVistas.Clear();
		mestre.Forma.Entrar(Catalogo.IdBase);
		Encostar(mestre, perto, tiles: Discipulado.TilesDeTestemunha + 1);
		EntrarNaForma(mestre, ssj1);
		AfirmarMst("um tile alem da borda ja nao ve", !perto.FormasVistas.Contains(ssj1.IdRede));

		// DESCER NAO E UMA FORMA VISTA -- a base nao se ensina.
		perto.FormasVistas.Clear();
		Encostar(mestre, perto, tiles: 1);
		EntrarNaForma(mestre, Catalogo.Def(Catalogo.IdBase)!);
		AfirmarMst("voltar a base nao vira 'forma vista'", perto.FormasVistas.Count == 0);

		// as duas testemunhas saem de perto: a secao seguinte usa verbo com ALVO. Ver `Estacionar`.
		Estacionar(perto, 600);
		Estacionar(longe, 620);
	}

	// =====================================================================
	// 6) O DESPERTAR ASSISTIDO
	// =====================================================================
	/// <summary>
	/// A metade do requisito PESSOAL, a ordem em que a raiva e acesa, e -- o que mais importa -- a
	/// REENTRADA depois: no DM o corte e permanente porque as procs canonicas reescrevem o limiar,
	/// e sem equivalente aqui o aluno despertaria uma vez e ficaria trancado fora da propria forma.
	/// </summary>
	private void ODespertarAssistido(ServerPlayer mestre, ServerPlayer aluno)
	{
		GD.Print("[mestre] -- 6) O DESPERTAR ASSISTIDO: A PORTA PELA METADE");

		GarantirVinculo(mestre, aluno);

		FormaDef ssj1 = Catalogo.Def("ssj1")!;
		double porta = PortaDeTeste(aluno, "ssj1");
		PerfilDeFormas perfil = Perfil(aluno);

		// O ALUNO FICA ENTRE A METADE E A PORTA INTEIRA. E a unica faixa em que a assistida decide
		// alguma coisa: abaixo da metade ela nao ajuda, acima da porta ela nao e necessaria.
		aluno.Forma.Entrar(Catalogo.IdBase);
		aluno.Forma.PortasCortadas.Clear();
		aluno.Forma.Liberadas.Clear();
		aluno.Ficha.BP = porta * 0.6;
		aluno.Ficha.Ki = aluno.Ficha.MaxKi;
		aluno.FuriaExtremaAte = aluno.RaivaLendariaAte = 0;

		AfirmarMst("sozinho, com 60% da porta, o SSJ1 e recusado por PODER",
				   aluno.Forma.Avaliar("ssj1", aluno.Ficha.BP, 1, false, perfil) == RecusaForma.SemPoder);
		AfirmarMst("com o corte pela metade, a unica pendencia vira a RAIVA "
				   + "(e e a prova de que o corte entrou no FUNIL, e nao como desconto depois)",
				   aluno.Forma.Avaliar("ssj1", aluno.Ficha.BP, 1, false, perfil, Discipulado.FatorAssistido)
					   == RecusaForma.SemFuria);

		// O MESTRE PRECISA CONHECER A FORMA (ou o aluno ter visto) -- pre-requisito da spec.
		mestre.Forma.Entrar(Catalogo.IdBase);
		aluno.FormasVistas.Clear();
		mestre.Forma.Liberadas.Clear();
		Encostar(mestre, aluno, tiles: 1);
		mestre.RecargaDeEnsino = 0;
		OferecerDespertar(mestre, "ssj1");
		AfirmarMst("sem o aluno ter visto e sem o mestre possuir a forma, nao ha o que ensinar",
				   aluno.PedidoDoMestre == null);
		AfirmarMst("...e a recarga NAO foi gasta numa oferta que nem saiu",
				   mestre.RecargaDeEnsino == 0);

		// agora o mestre possui o SSJ1 (o `mst_form_known` do DM)
		mestre.Forma.Liberar("ssj1");
		OferecerDespertar(mestre, "ssj1");
		AfirmarMst("com o mestre possuindo a forma, a oferta sai",
				   aluno.PedidoDoMestre is { Despertar: true, FormaId: "ssj1" });
		AfirmarMst("...e a recarga comeca a correr NO GESTO, antes da resposta (o `:510` do DM)",
				   mestre.RecargaDeEnsino > NowMs());

		long raivaAntes = aluno.FuriaExtremaAte;
		ResponderAoMestre(aluno, aceitou: true);

		AfirmarMst("o aluno DESPERTA o SSJ1 com 60% da porta pessoal",
				   aluno.Forma.Atual == "ssj1", $"{aluno.Forma.Atual}, BP {aluno.Ficha.BP:N0} de {porta:N0}");
		AfirmarMst("...e a raiva foi acesa pela provocacao (a forma nasce dela)",
				   aluno.FuriaExtremaAte > raivaAntes);
		AfirmarMst("...e o corte ficou anotado", aluno.Forma.PortasCortadas.Contains(ssj1.IdRede));
		AfirmarMst("...e a forma entrou pelo funil normal (liberada + estreia consumida)",
				   aluno.Forma.Despertou("ssj1") && aluno.Forma.JaViuAEstreia("ssj1"));

		// ============================ A REENTRADA -- O BUG QUE O DM DESCREVE ============================
		// `mst_form_apply` (`:358-360`): cravar a posse sem cortar o limiar deixava o aluno *"com uma
		// forma na qual nao conseguia reentrar"*. Aqui o passo 8 do `Avaliar` cobra a porta SEMPRE,
		// inclusive de forma ja liberada -- entao sem o `PortasCortadas` persistente aconteceria
		// exatamente isso.
		// ==========================================================================================
		aluno.Forma.Entrar(Catalogo.IdBase);
		aluno.FuriaExtremaAte = aluno.RaivaLendariaAte = 0;
		AfirmarMst("depois de voltar a base, o aluno REENTRA sozinho na forma (sem mestre e sem raiva)",
				   aluno.Forma.Avaliar("ssj1", aluno.Ficha.BP, 1, false, Perfil(aluno)) == RecusaForma.Pode,
				   aluno.Forma.Avaliar("ssj1", aluno.Ficha.BP, 1, false, Perfil(aluno)).ToString());

		// ABAIXO DA METADE NAO HA MILAGRE -- e, o que importa mais, **nao ha raiva de graca**.
		var virgem = new EstadoDeForma();
		ServerPlayer fraco = aluno;
		EstadoDeForma guardado = fraco.Forma;
		try
		{
			fraco.Forma = virgem;
			fraco.Ficha.BP = porta * 0.4;
			fraco.FuriaExtremaAte = fraco.RaivaLendariaAte = 0;
			fraco.FormasVistas.Add(ssj1.IdRede);
			mestre.RecargaDeEnsino = 0;
			Encostar(mestre, fraco, tiles: 1);

			OferecerDespertar(mestre, "ssj1");
			bool ofereceu = fraco.PedidoDoMestre != null;
			ResponderAoMestre(fraco, aceitou: true);

			AfirmarMst("com 40% da porta (abaixo da metade) o despertar assistido FALHA",
					   fraco.Forma.Atual == Catalogo.IdBase, $"ofereceu={ofereceu}, forma={fraco.Forma.Atual}");
			AfirmarMst("...e a tentativa fadada NAO da raiva de graca "
					   + "(a ordem do `Avaliar` poe a raiva por ultimo, e e isso que a garante)",
					   fraco.FuriaExtremaAte == 0 && fraco.RaivaLendariaAte == 0);
		}
		finally { fraco.Forma = guardado; }
	}

	// =====================================================================
	// 7) RECARGA, PERSISTENCIA, LOGOUT E ASSINATURA RECICLADA
	// =====================================================================
	private void RecargaPersistenciaELogout(ServerPlayer mestre, ServerPlayer aluno)
	{
		GD.Print("[mestre] -- 7) RECARGA, DISCO, LOGOUT E ASSINATURA RECICLADA");

		GarantirVinculo(mestre, aluno);

		// --- A RECARGA ---
		aluno.Forma.Entrar(Catalogo.IdBase);
		aluno.FormasVistas.Add(Catalogo.Def("ssj1")!.IdRede);
		Encostar(mestre, aluno, tiles: 1);
		mestre.RecargaDeEnsino = 0;

		OferecerDespertar(mestre, "ssj1");
		long prazo = mestre.RecargaDeEnsino;
		AfirmarMst($"a recarga de ensino e de {Discipulado.RecargaDeEnsinoSegundos / 60:0} min",
				   Math.Abs((prazo - NowMs()) / 1000.0 - Discipulado.RecargaDeEnsinoSegundos) < 2,
				   $"{(prazo - NowMs()) / 1000.0:0} s");

		aluno.PedidoDoMestre = null;
		OferecerDespertar(mestre, "ssj1");
		AfirmarMst("...e a segunda tentativa dentro da recarga e recusada", aluno.PedidoDoMestre == null);

		// O CASTIGO E DO GESTO, NAO DO RESULTADO (`:510`): o "nao" do aluno nao devolve os 5 min.
		mestre.RecargaDeEnsino = 0;
		OferecerDespertar(mestre, "ssj1");
		ResponderAoMestre(aluno, aceitou: false);
		AfirmarMst("...e o 'nao' do aluno NAO devolve a recarga (senao provocar seria de graca)",
				   mestre.RecargaDeEnsino > NowMs());

		// --- O DISCO ---
		// A gravacao ja aconteceu (o `Vincular` grava na hora, como o `cargos.txt`). Aqui se prova o
		// ida-e-volta: o dicionario e limpo e RELIDO do arquivo.
		string sigAluno = aluno.Assinatura, sigMestre = mestre.Assinatura;
		if (MestreDe(sigAluno).Length == 0)
		{
			ConvidarAluno(mestre);
			ResponderAoMestre(aluno, aceitou: true);
		}
		SalvarMestres();
		_mestreDe.Clear();
		_nomePorAssinatura.Clear();
		CarregarMestres();
		AfirmarMst("o vinculo atravessa o disco POR ASSINATURA (o savefile `MASTER` do DM)",
				   MestreDe(sigAluno) == sigMestre, $"'{MestreDe(sigAluno)}'");
		AfirmarMst("...e o NOME do mestre sobrevive junto (o `mst_names`, pra o painel de quem esta so)",
				   NomeDaAssinatura(sigMestre) == mestre.Name, NomeDaAssinatura(sigMestre));

		// --- O LOGOUT ---
		// O vinculo e do MUNDO: sair do jogo nao o desfaz. O que muda e que o corpo some.
		_players.Remove(mestre.Id);
		AfirmarMst("com o mestre deslogado o vinculo CONTINUA (ele e do mundo, nao da sessao)",
				   MestreDe(sigAluno) == sigMestre);
		AfirmarMst("...mas o corpo dele nao e achavel", CorpoDaAssinatura(sigMestre) == null);
		AfirmarMst("...e o bonus de ganho simplesmente nao se aplica, sem estourar nada",
				   !EhMeuMestre(aluno, CorpoDaAssinatura(sigMestre)));

		aluno.PedidoDoMestre = new PedidoDeMestre(false, sigMestre, mestre.Name, "",
												  NowMs() + 60_000);
		ResponderAoMestre(aluno, aceitou: true);
		AfirmarMst("...e responder a um pedido de quem saiu nao resolve nada (o molde do PedidoDeAmizade)",
				   aluno.PedidoDoMestre == null);
		_players[mestre.Id] = mestre;

		// --- A ASSINATURA RECICLADA ---
		// `hash(conta, slot)`: apagar o personagem e criar outro no mesmo slot devolve a MESMA
		// assinatura. Sem a purga, o personagem novo nasceria com o mestre (ou os alunos) do morto.
		PurgarAssinatura(sigAluno);
		AfirmarMst("assinatura reciclada nao herda MESTRE (o `mst_purge_sig`)",
				   MestreDe(sigAluno).Length == 0);

		ConvidarAluno(mestre);
		ResponderAoMestre(aluno, aceitou: true);
		PurgarAssinatura(sigMestre);
		AfirmarMst("...nem herda ALUNOS (a purga limpa os dois lados)",
				   ContarAlunos(sigMestre) == 0 && MestreDe(sigAluno).Length == 0);
	}

	// =====================================================================
	// FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// Poe os dois colados e virados um pro outro. O <see cref="AlvoNaFrente"/> e um CONE -- quem
	/// esta ao lado nao esta na frente --, e sem virar o `Facing` metade dos verbos desta bancada
	/// falharia por geometria e nao por regra.
	/// </summary>
	private static void Encostar(ServerPlayer a, ServerPlayer b, double tiles)
	{
		b.Pos = a.Pos + new Vec2((float)(tiles * ZoneCollision.TileSize), 0);
		a.Facing = Facing.East;
		b.Facing = Facing.West;
	}

	/// <summary>
	/// MANDA ALGUEM PRA LONGE.
	///
	/// ============================ ESTA FUNCAO NASCEU DE UMA FALHA DA BANCADA ============================
	/// A primeira versao empilhava os corpos no mesmo tile e chamava os verbos. Resultado: o
	/// <see cref="AlvoNaFrente"/> devolve o MAIS PROXIMO, e com todos colados ele devolvia sempre o
	/// mesmo -- entao `DispensarAluno` desfazia o vinculo de quem a bancada nao estava olhando, e um
	/// convite ficava PENDURADO no corpo errado ate a secao seguinte. Doze checagens vermelhas, e
	/// nenhuma delas era um defeito do jogo: era a bancada medindo geometria.
	///
	/// Fica escrito porque a licao vale pra qualquer bancada que use verbo com alvo: **quem nao e o
	/// alvo tem que estar longe**.
	/// ================================================================================================
	/// </summary>
	private static void Estacionar(ServerPlayer p, double tilesX) =>
		p.Pos = new Vec2((float)(tilesX * ZoneCollision.TileSize), 0);

	/// <summary>
	/// GARANTE O VINCULO pra uma secao que quer medir OUTRA coisa. E preparo, nao afirmacao: o
	/// caminho de verdade (convite -> aceite -> gravacao) e provado na secao 2, pelos verbos.
	/// </summary>
	private void GarantirVinculo(ServerPlayer mestre, ServerPlayer aluno)
	{
		mestre.PedidoDoMestre = aluno.PedidoDoMestre = null;
		if (MestreDe(aluno.Assinatura) == mestre.Assinatura) return;
		if (MestreDe(aluno.Assinatura).Length > 0) Desvincular(aluno.Assinatura, "preparo de bancada");
		Vincular(mestre, aluno);
	}
}
