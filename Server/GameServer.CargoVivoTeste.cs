using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DAS SKILLS DE CARGO -- roda dentro do `--formasteste`.
///
/// ============================ POR QUE ELA EXISTE, SE JA HA A `--cargoportas` ============================
/// A `--cargoportas` mede a dadiva **chamando o `ReconciliarDadiva` a mao**, sobre corpos forjados com
/// `Peer` nulo. Ela e boa e continua valendo, mas ha uma coisa que ela nao consegue afirmar: que o
/// jogo entrega o kit **sozinho, a quem esta jogando**. Duas razoes concretas:
///
///   1. quem entrega em producao e o <see cref="TickDosCargos"/>, e ele so olha pra quem
///      <see cref="EhJogador"/> aprova -- e esse predicado pede `Peer != null`. Um corpo de bancada de
///      boot **nao e gente pro proprio jogo**: as 108 checagens de la passam por um cano que o tique
///      nunca percorreria;
///   2. **`ReivindicarCargo` e `Outorgar` NAO chamam `ReconciliarDadiva`.** Quem reivindica um cargo
///      recebe o anuncio e mais nada; o kit so chega no proximo tique. Uma bancada que chama a
///      reconciliacao a mao nunca poderia descobrir isso -- ela ja comeca do outro lado da porta.
///
/// Aqui os corpos EMPRESTAM o `Peer` do host (a mesma saida da `--karmateste`), o trono se ocupa e o
/// **tique de producao** e quem tem que entregar. Nenhuma linha desta bancada chama a reconciliacao.
/// ====================================================================================================
///
/// ============================ O PAR QUE SE SEGURA EM CADA LINHA ============================
/// Toda familia daqui tem DUAS metades, e a segunda e a que nao deixa a primeira passar de graca:
///
///   A. o dono do cargo GANHA a skill (pelo nome dela) -- **e o controle, que nunca teve cargo, NAO**.
///      Sem a segunda metade, um `Dar()` que desse tudo pra todo mundo ficaria verde nas 51 linhas;
///   B. da pra USAR: sem o cargo o jogo recusa PELO NOME da tecnica, com o cargo o portao abre --
///      e o que nao tem corpo neste port DIZ que nao tem, em vez de calar;
///   C. a condicao extra e COBRADA: com karma/BP/God Ki abaixo do exigido a porta nao abre **e o kit
///      nao chega**; um ponto acima, abre. Um teto que nunca dispara e indistinguivel de teto nenhum;
///   D. largar o cargo DESFAZ -- e o que o DM NAO desfaz (`treeshrink`, `trees.dm:119`) fica.
///
/// E UMA LINHA POR SKILL, COM O NOME DELA. "o cargo entregou alguma coisa" ficaria verde com a skill
/// errada, e foi assim que a `RankDef.Concede` passou meses prometendo trinta kits que ninguem lia.
/// ==========================================================================================
///
/// ============================ A ARMADILHA DO `Peer` EMPRESTADO -- LEIA ANTES DE ESTENDER ============================
/// Corpo com `Peer` emprestado **nao pode passar por <see cref="Persistir"/>**. O `Persistir` acha a
/// conta pelo PEER (`_contas[pl.Peer]`) e grava em `acc.Slots[pl.Slot]` -- ou seja, um corpo forjado
/// com o `Peer` do host e `Slot` 0 sobrescreveria o personagem do host no disco.
///
/// Por isso o `Revive` (a unica outra tecnica de cargo com efeito visivel de fora) NAO e disparada
/// aqui: `RessuscitarG4` termina em `Persistir(alvo)` (`GameServer.Tecnicas.G4.cs:330`). A prova de
/// efeito ao vivo e feita com o Kaio-ken, que mexe so na ficha.
///
/// A outra metade da precaucao e o `finally`: enquanto um corpo emprestado estiver no `_players`, o
/// autosave de 2 minutos (`GameServer.cs:4081`) o alcanca. Esta bancada e sincrona -- o tique nao
/// corre no meio dela --, mas um corpo VAZADO daqui viraria corrupcao de save, e nao lixo.
/// ================================================================================================================
/// </summary>
public partial class GameServer
{
	private const int IdBaseDoCargoVivo = 91_100;

	/// <summary>
	/// AS SEIS QUE FICAM QUANDO O CARGO SE VAI -- e elas estao escritas AQUI, a mao, de proposito.
	///
	/// A tabela de verdade (<see cref="DadivaDeCargo.LevantarRevogaveis"/>) DERIVA esta resposta do
	/// catalogo. Se a bancada perguntasse a ela o que devia sair, as duas concordariam sempre --
	/// inclusive erradas, que e o modo de falha que este projeto ja pagou ("as duas telas concordam"
	/// fica verde com as duas erradas igual).
	///
	/// Entao a expectativa vem do DM e nao do codigo em teste: `Telepathy` (arvores demon/kai),
	/// `expand` (Body e seis raciais), `observe` (demigod/kanassajin), `splitform`
	/// (kanassajin/majin) e `selfdestruct` (android/saibaman) tem dono FORA do cargo -- quem os tem
	/// pode te-los COMPRADO. O `Enkumei` fica por outro motivo, e o `DadivaDeCargo` ja o escreve: ele
	/// nao esta na `constituentskills` de arvore nenhuma, nem no DM, entao nem o `treeshrink` de la o
	/// tira. **Todo o resto de um kit tem que sumir.**
	/// </summary>
	private static readonly string[] FicamMesmoSemOCargo =
	[
		"/datum/skill/Telepathy", "/datum/skill/expand", "/datum/skill/general/observe",
		"/datum/skill/general/splitform", "/datum/skill/general/selfdestruct", "/datum/skill/Enkumei",
	];

	/// <summary>
	/// A RECUSA DE QUEM NAO SABE A TECNICA, nas DUAS grafias que o servidor usa: o funil generico
	/// escreve sem acento (`UsarTecnica`, "voce nao sabe X") e o lote G2 escreve com
	/// (`SabeG2`, "voce nao sabe X"). Procurar so uma delas daria uma bancada que reprova o
	/// Kaio-ken por causa de um cedilha.
	/// </summary>
	private static bool NegouPorNaoSaber(string dito) =>
		dito.Contains("voce nao sabe", StringComparison.OrdinalIgnoreCase)
		|| dito.Contains("você não sabe", StringComparison.OrdinalIgnoreCase);

	private void OsCargosAoVivo(ServerPlayer host, Action<string, bool, string> Checa)
	{
		int ok = 0, falhou = 0;
		void C(string oque, bool passou, string detalhe = "")
		{
			if (passou) ok++; else falhou++;
			Checa($"[cargo] {oque}", passou, detalhe);
		}

		if (_skills == null)
		{
			Checa("[cargo] o catalogo de skills carregou (sem ele nao ha o que conceder)", false, "");
			return;
		}

		GD.Print("[cargo] ======== A DADIVA DOS CARGOS, MEDIDA NUM JOGADOR DE VERDADE ========");

		// ============================ A FOTO DO MUNDO ============================
		// Esta bancada ocupa ~20 tronos e roda o tique de producao dezenas de vezes. Os tronos, as
		// fichas de dever e o relogio do laco de deveres voltam como estavam -- e o `SalvarCargos` do
		// `finally` e o que garante que o DISCO tambem volte (a secao C passa pelo
		// `ReivindicarCargo`, que grava).
		// ======================================================================
		var tronosReais = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		var missoesReais = new Dictionary<string, FichaDeMissao>(_missoes, StringComparer.OrdinalIgnoreCase);
		double voltaReal = _proximaVoltaDasMissoes;
		_tronos.Clear();

		ZoneKey zona = ZoneKey.Premade(
			Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		// O `Peer` EMPRESTADO E O PONTO DA BANCADA -- ver o cabecalho. Sem ele `EhJogador` devolve
		// falso, o `TickDosCargos` pula o corpo e TUDO aqui embaixo passaria por ausencia.
		ServerPlayer Forjar(int i, string nome, string raca = "Saiyan", double bp = 5_000_000)
		{
			var novo = new ServerPlayer
			{
				Id = IdBaseDoCargoVivo + i,
				Peer = host.Peer,
				Name = nome,
				Race = raca,
				Genero = "Male",
				Idade = 30,
				Zone = zona,
				Pos = new Vec2(i * 3f * ZoneCollision.TileSize, 0),
				Conta = $"bancada_cargo_{i}",
				Slot = 0,
				Ficha = new Fighter { Race = raca, BP = bp },
				Livro = new SkillBook(),
			};
			novo.Ficha.Class = "Normal";
			PorNoMundo(novo);
			novo.Ficha.Ki = novo.Ficha.MaxKi;
			return novo;
		}

		ServerPlayer dono = Forjar(1, "bancada cargo: o dono");
		ServerPlayer controle = Forjar(2, "bancada cargo: o sem-cargo");
		ServerPlayer namek = Forjar(3, "bancada cargo: o namekuseijin", "Namekian");

		try
		{
			C("o corpo forjado E JOGADOR pro proprio jogo (`EhJogador`, com `Peer`)",
			  EhJogador(dono) && EhJogador(controle), "sem isto o tique de cargos nunca olharia pra ele");

			OTiqueEQuemEntrega(dono, C);
			UmaLinhaPorSkill(dono, controle, C);
			ACondicaoExtraEhCobrada(dono, controle, C);
			AsPortasQueNaoSaoOCanalDeTecnica(dono, controle, namek, C);
			OKaiokenAcendeDeVerdade(dono, controle, C);
		}
		catch (Exception e)
		{
			C($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			// OS CORPOS SAEM PRIMEIRO. Ver a armadilha do `Peer` emprestado no cabecalho: corpo
			// emprestado esquecido no `_players` nao e lixo, e save corrompido no proximo autosave.
			foreach (ServerPlayer p in new[] { dono, controle, namek })
			{
				_players.Remove(p.Id);
				ZoneList(p.Zone.Hash).Remove(p);
			}

			_tronos.Clear();
			foreach ((string k, string v) in tronosReais) _tronos[k] = v;
			SalvarCargos();

			_missoes.Clear();
			foreach ((string k, FichaDeMissao f) in missoesReais) _missoes[k] = f;
			_proximaVoltaDasMissoes = voltaReal;

			// E O MUNDO DE VERDADE VOLTA A BATER COM O DISCO. Os tronos ficaram vazios durante a
			// bancada, e o tique tirou o kit de quem esta jogando agora; este tique com os tronos ja
			// devolvidos poe tudo de volta. Sem ele, um Kaio do Norte de verdade que estivesse online
			// terminaria a bancada sem o proprio kit -- em silencio, ate o proximo segundo.
			TickDosCargos();

			// A LISTA DE SKILLS DO HOST VOLTA PRA SER A DELE. `MandarSkills` manda pro PEER, e o peer
			// aqui e emprestado: o cliente do host recebeu, dezenas de vezes, o livro dos corpos desta
			// bancada. Sem esta linha ele termina com a aba de skills de um cadaver de teste.
			MandarSkills(host, forcar: true);
			MandarCargos(host);
		}

		GD.Print($"[cargo] ======== {ok} OK, {falhou} FALHA(S) ========");
	}

	// =====================================================================
	// 0. QUEM ENTREGA E O TIQUE -- e nao a porta por onde o cargo entrou
	// =====================================================================
	/// <summary>
	/// O DEFEITO INJETADO DESTA FAMILIA, e ele nao precisou ser inventado: **ocupar o trono nao
	/// entrega nada**. `ReivindicarCargo` (`GameServer.Ranks.cs:154`) e `Outorgar` (`:188`) escrevem
	/// no `_tronos`, gravam e anunciam -- nenhum dos dois chama a reconciliacao. Quem entrega e o
	/// tique de 1 Hz, e so ele.
	///
	/// Esta secao prova as duas metades na ordem em que o jogo as vive: com o trono ocupado e ANTES
	/// do tique o livro esta vazio; um tique depois, o kit chegou. Uma bancada que chamasse
	/// `ReconciliarDadiva` a mao (como a `--cargoportas` faz) nao consegue enxergar esta janela --
	/// ela comeca do outro lado dela.
	/// </summary>
	private void OTiqueEQuemEntrega(ServerPlayer dono, Action<string, bool, string> C)
	{
		const string estilo = "/datum/skill/style/KameStyle";

		_tronos["turtle"] = dono.Conta;
		C("INJECAO: ocupar o trono, sozinho, NAO entrega nada (nem `Reivindicar` nem `Outorgar` reconciliam)",
		  !dono.Livro.Sabe(estilo), "o kit chegou sem tique -- entao ha um segundo entregador nao declarado");

		TickDosCargos();
		C("...e UM TIQUE de 1 Hz depois, o kit esta no livro (o entregador e o `TickDosCargos`)",
		  dono.Livro.Sabe(estilo), "o tique de producao nao entregou");

		_tronos.Remove("turtle");
		TickDosCargos();
		C("...e o tique tambem e quem TIRA (perder o cargo dormindo tem que valer igual)",
		  !dono.Livro.Sabe(estilo), "");
	}

	// =====================================================================
	// 1, 2 e 4. UMA LINHA POR SKILL: ganha / usa / perde
	// =====================================================================
	/// <summary>
	/// AS 51 SKILLS QUE OS CARGOS ENTREGAM, UMA A UMA, PELO NOME.
	///
	/// A varredura e por CARGO (e o cargo que se ocupa), mas a linha e por SKILL e cada skill so
	/// aparece uma vez -- no primeiro cargo que a concede. Sao tres perguntas por skill, e nesta
	/// ordem, porque e a ordem em que o jogador as vive:
	///
	///   A. o dono do cargo recebeu -- e o CONTROLE, que nunca teve cargo nenhum, nao;
	///   B. da pra usar: o controle e recusado PELO NOME da tecnica, e o dono passa o portao (ou le
	///      que o efeito ainda nao existe -- o que nao pode e o silencio);
	///   D. largando o cargo, sai o que o `treeshrink` do DM tiraria e fica o que ele deixa.
	/// </summary>
	private void UmaLinhaPorSkill(ServerPlayer dono, ServerPlayer controle, Action<string, bool, string> C)
	{
		var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int cargosUsados = 0;

		foreach (RankDef r in Cargos.Todos)
		{
			string[] kit = DadivaDeCargo.De(r.Chave);
			string[] novas = [.. kit.Where(p => !vistas.Contains(p) && _skills!.Get(p) != null)];
			if (novas.Length == 0) continue;
			foreach (string p in novas) vistas.Add(p);
			cargosUsados++;

			_tronos[r.Chave] = dono.Conta;
			TickDosCargos();

			// ---- A: ganhou (e o controle nao) ----
			foreach (string path in novas)
			{
				Skill s = _skills!.Get(path)!;
				bool temDono = dono.Livro.Sabe(path), temControle = controle.Livro.Sabe(path);
				C($"[{r.Nome}] entrega '{s.Nome}' -- e quem NAO tem o cargo nao recebe",
				  temDono && !temControle,
				  !temDono ? "nao chegou no livro do dono" : "o controle SEM CARGO recebeu junto");
			}

			// ---- B: da pra usar ----
			foreach (string path in novas)
			{
				Skill s = _skills!.Get(path)!;
				if (s.Verbos.Length == 0) continue;
				AVerbaEhUsavel(s, dono, controle, C);
			}

			// ---- D: largar desfaz (o que o DM desfaz) ----
			_tronos.Remove(r.Chave);
			TickDosCargos();
			foreach (string path in novas)
			{
				Skill s = _skills!.Get(path)!;
				bool deveFicar = FicamMesmoSemOCargo.Contains(path, StringComparer.OrdinalIgnoreCase);
				bool ficou = dono.Livro.Sabe(path);
				C(deveFicar
					? $"[{r.Nome}] largar o cargo NAO tira '{s.Nome}' (tem dono fora do cargo -- `treeshrink`)"
					: $"[{r.Nome}] largar o cargo TIRA '{s.Nome}'",
				  ficou == deveFicar,
				  ficou ? "continuou no livro sem cargo nenhum" : "sumiu, e esta skill tem dono fora do cargo");
			}
		}

		C($"as 51 skills concedidas foram medidas uma a uma ({vistas.Count} em {cargosUsados} cargos)",
		  vistas.Count == DadivaDeCargo.Todas.Count,
		  $"{vistas.Count} de {DadivaDeCargo.Todas.Count} -- alguma skill do kit nao existe no catalogo");

		C("o controle terminou a varredura sem UMA skill no livro",
		  controle.Livro.Aprendidas.Count == 0, string.Join(", ", controle.Livro.Aprendidas));
	}

	/// <summary>
	/// A SEGUNDA FAMILIA: **entrar no livro nao e poder usar**.
	///
	/// A metade que ninguem escreveria sozinho e a do CONTROLE: sem o cargo, o jogo tem que recusar
	/// **pelo nome da tecnica**. Recusa generica nao serve -- "nao deu" e indistinguivel de um verb
	/// que nao existe, e foi assim que 31 verbos de cargo passaram meses sendo botao mudo.
	///
	/// Com o cargo, a resposta certa depende de a tecnica ter corpo neste port:
	///   * PORTADA  -> o portao (`SabeTecnica`, o mesmo que o `UsarTecnica` consulta) abre;
	///   * MUDA     -> o funil DIZ que o efeito ainda nao foi trazido. Declarar e a regra 3 da camada;
	///   * OUTRA PORTA -> o verb do DM foi atendido por outro canal (`CensoDeSkills.PorOutroCanal`),
	///     e ai o portao de tecnica nao e o juiz -- as portas de verdade sao dirigidas na secao 3.
	///
	/// **A TECNICA VIVA NAO E DISPARADA AQUI**, e e de proposito: metade delas atira, uma cobra
	/// vida (Kikoho) e uma grava no disco (Revive -- ver a armadilha do `Peer` emprestado). O que se
	/// afirma e o PORTAO; que o disparo acontece de verdade e o que o Kaio-ken prova, na secao 4.
	/// </summary>
	private void AVerbaEhUsavel(Skill s, ServerPlayer dono, ServerPlayer controle, Action<string, bool, string> C)
	{
		var problemas = new List<string>();
		var canais = new List<string>();

		foreach (string verb in s.Verbos)
		{
			// 1) SEM O CARGO: a recusa sai, e sai com o NOME da tecnica.
			List<string>? antes = EscutaDeAvisos;
			var ditos = new List<string>();
			EscutaDeAvisos = ditos;
			try { UsarHabilidade(controle, verb); }
			finally { EscutaDeAvisos = antes; }
			string semCargo = string.Join(" | ", ditos);

			// O NOME PODE SER O DA TECNICA **OU O DA SKILL**, e a diferenca foi a bancada corrigindo a
			// bancada: `Kaioken_Settings` e um ALIAS -- quem nao sabe a tecnica ouve "voce nao sabe
			// Kaio-ken", que e o nome do verb-mae (e da skill), e nao "Kaioken Settings". Exigir so o
			// primeiro reprovava o servidor por acertar. O que se afirma e que a recusa NOMEIA alguma
			// coisa: "nao deu" seria indistinguivel de um verb que nao existe.
			string nomeDaTecnica = Tecnicas.Get(verb)!.Nome;
			if (!NegouPorNaoSaber(semCargo)) problemas.Add($"{verb}: sem cargo o jogo respondeu '{semCargo}'");
			else if (!semCargo.Contains(nomeDaTecnica, StringComparison.OrdinalIgnoreCase)
				  && !semCargo.Contains(s.Nome, StringComparison.OrdinalIgnoreCase))
				problemas.Add($"{verb}: a recusa nao diz o nome da tecnica nem o da skill ('{semCargo}')");

			// 2) COM O CARGO.
			bool outraPorta = CensoDeSkills.PorOutroCanal.TryGetValue(verb, out string? canal);
			if (outraPorta) canais.Add($"{verb}->{canal}");

			if (!SabeTecnica(dono, verb))
			{
				problemas.Add($"{verb}: com o cargo o portao continuou fechado");
				continue;
			}

			// A MUDA TEM QUE DIZER QUE E MUDA. So estas sao disparadas: `NaoPortada` nao faz nada por
			// definicao, entao dispara-las e seguro -- e e a unica forma de provar que o jogador ouve
			// a divida em vez de apertar um botao morto.
			if (!outraPorta && Tecnicas.Get(verb)!.Modo == Modo.NaoPortada)
			{
				var ditos2 = new List<string>();
				EscutaDeAvisos = ditos2;
				try { UsarHabilidade(dono, verb); }
				finally { EscutaDeAvisos = antes; }
				string comCargo = string.Join(" | ", ditos2);
				if (!comCargo.Contains("nao foi portado", StringComparison.OrdinalIgnoreCase)
					&& !comCargo.Contains("não foi portado", StringComparison.OrdinalIgnoreCase))
					problemas.Add($"{verb}: muda, e nao disse que era muda ('{comCargo}')");
			}
		}

		string rotulo = canais.Count > 0 ? $" [outra porta: {string.Join(", ", canais)}]" : "";
		C($"...e '{s.Nome}' e USAVEL: sem cargo o jogo recusa pelo nome, com cargo o portao abre{rotulo}",
		  problemas.Count == 0, string.Join(" ; ", problemas));
	}

	// =====================================================================
	// 3. A CONDICAO EXTRA E COBRADA
	// =====================================================================
	/// <summary>
	/// O CARGO NAO SE PEGA SO POR QUERER -- e o kit nao chega pra quem nao cumpriu.
	///
	/// Tudo aqui passa pelo <see cref="ReivindicarCargo"/>, o verb de producao. Sao tres ferrolhos
	/// diferentes, cada um provado NAS DUAS DIRECOES e com a consequencia medida no LIVRO (a recusa
	/// que deixasse o kit passar seria uma recusa decorativa):
	///
	///   * KARMA -- o Eremita Tartaruga pede 25 (`RankQuests.dm:210`). E o cargo certo pra esta prova
	///     porque ele pede exatamente DUAS coisas, e a outra (1M de BP) o corpo ja tem de sobra: o
	///     karma e o unico ferrolho. Ver a `--karmateste`, que aprendeu isso reprovando com o Guardiao
	///     (o `OqueFalta` devolve a PRIMEIRA regra que falha, e a do Guardiao e a raca);
	///   * BP -- a mesma porta, com o karma ja pago e o BP abaixo do minimo;
	///   * GOD KI -- o Kaio do Norte (`RankQuests.dm:227-228`), que e a pergunta do dono: pra aprender
	///     Kaio-ken e Genkidama do cargo e preciso karma 50 **e** God Ki desperto (ou sangue Kai).
	///
	/// E O LIMITE E MEDIDO NO PONTO EXATO (24 recusa, 25 abre). Teto que nunca dispara e indistinguivel
	/// de teto nenhum -- o corolario da casa.
	/// </summary>
	private void ACondicaoExtraEhCobrada(ServerPlayer dono, ServerPlayer controle, Action<string, bool, string> C)
	{
		const string kame = "/datum/skill/style/KameStyle";
		const string kaioken = "/datum/skill/kaioken";
		const string genkidama = "/datum/skill/rank/SpiritBomb";

		_tronos.Clear();
		dono.Livro.Esquecer(kame);
		dono.Ficha.BP = 5_000_000;

		// ---- KARMA: o ponto exato ----
		dono.Karma = 24;
		ReivindicarCargo(dono, "turtle");
		TickDosCargos();
		C("com karma 24 o Eremita Tartaruga (que pede 25) RECUSA -- e o kit nao chega",
		  CargoDe(dono.Conta).Length == 0 && !dono.Livro.Sabe(kame),
		  $"cargo '{CargoDe(dono.Conta)}', estilo Kame no livro: {dono.Livro.Sabe(kame)}");

		dono.Karma = 25;
		ReivindicarCargo(dono, "turtle");
		C("...e com 25 exatos ele ABRE (o `>=` do DM, medido no ponto)",
		  string.Equals(CargoDe(dono.Conta), "turtle", StringComparison.OrdinalIgnoreCase),
		  Cargos.OqueFalta(Cargos.Get("turtle")!, Ficha(dono)) ?? "");

		TickDosCargos();
		C("...e SO ENTAO o estilo Kame entra no livro (a porta abre, o tique entrega)",
		  dono.Livro.Sabe(kame) && !controle.Livro.Sabe(kame), "");

		// ---- BP: o segundo ferrolho da MESMA porta ----
		_tronos.Clear();
		TickDosCargos();
		dono.Ficha.BP = 999_999;
		ReivindicarCargo(dono, "turtle");
		TickDosCargos();
		C("com o karma pago e o BP abaixo de 1M, a MESMA porta recusa -- e o kit nao chega",
		  CargoDe(dono.Conta).Length == 0 && !dono.Livro.Sabe(kame),
		  $"cargo '{CargoDe(dono.Conta)}'");

		dono.Ficha.BP = 1_000_000;
		ReivindicarCargo(dono, "turtle");
		TickDosCargos();
		C("...e com 1M exato ela abre, e o estilo chega",
		  string.Equals(CargoDe(dono.Conta), "turtle", StringComparison.OrdinalIgnoreCase)
		  && dono.Livro.Sabe(kame), "");

		// ---- GOD KI: a pergunta do dono, no cargo do Kaio ----
		_tronos.Clear();
		TickDosCargos();
		dono.Ficha.BP = 5_000_000;
		dono.Karma = 100;
		dono.Ficha.godki = null;
		ReivindicarCargo(dono, "nkai");
		TickDosCargos();
		C("com karma de sobra e SEM God Ki desperto, o Kaio do Norte recusa -- e o Kaio-ken nao vem",
		  CargoDe(dono.Conta).Length == 0 && !dono.Livro.Sabe(kaioken),
		  $"cargo '{CargoDe(dono.Conta)}'");

		dono.Ficha.godki = new GodKiState { awakened = true, mastery = 0 };
		dono.Karma = 49;
		ReivindicarCargo(dono, "nkai");
		C("...e com God Ki desperto e karma 49 (pede 50) ele continua recusando",
		  CargoDe(dono.Conta).Length == 0, $"cargo '{CargoDe(dono.Conta)}'");

		dono.Karma = 50;
		ReivindicarCargo(dono, "nkai");
		TickDosCargos();
		C("com God Ki desperto E karma 50, o Kaio do Norte ABRE e o KAIO-KEN entra no livro",
		  string.Equals(CargoDe(dono.Conta), "nkai", StringComparison.OrdinalIgnoreCase)
		  && dono.Livro.Sabe(kaioken),
		  Cargos.OqueFalta(Cargos.Get("nkai")!, Ficha(dono)) ?? "");

		C("...e a GENKIDAMA entra junto (o cargo entrega as DUAS, como o `OtherworldRanks.dm:116-117`)",
		  dono.Livro.Sabe(genkidama), "");

		// A VITRINE DO PAINEL DIZ A VERDADE SOBRE AS DUAS: o Kaio-ken do lado pronto, a Genkidama
		// marcada como muda. E a resposta que o jogador consegue LER antes de disputar o cargo.
		string vitrine = OQueOCargoEntrega("nkai");
		int corte = vitrine.IndexOf("ainda mudo", StringComparison.OrdinalIgnoreCase);
		C("...e o painel do cargo mostra o Kaio-ken PRONTO e a Genkidama como AINDA MUDA",
		  corte > 0
		  && vitrine.IndexOf("Kaio-ken", StringComparison.OrdinalIgnoreCase) is var k && k >= 0 && k < corte
		  && vitrine.IndexOf("Spirit Bomb", StringComparison.OrdinalIgnoreCase) > corte,
		  vitrine);

		// UMA ALMA, UM TRONO -- e a recusa nao pode entregar kit de brinde.
		controle.Karma = 100;
		controle.Ficha.godki = new GodKiState { awakened = true, mastery = 0 };
		ReivindicarCargo(controle, "nkai");
		TickDosCargos();
		C("o cargo com dono recusa o segundo pretendente -- e ele nao ganha nada do kit",
		  CargoDe(controle.Conta).Length == 0 && !controle.Livro.Sabe(kaioken)
		  && controle.Livro.Aprendidas.Count == 0,
		  string.Join(", ", controle.Livro.Aprendidas));

		controle.Ficha.godki = null;
		controle.Karma = 0;
		dono.Karma = 0;
		dono.Ficha.godki = null;
		_tronos.Clear();
		TickDosCargos();
	}

	// =====================================================================
	// 3b. AS PORTAS QUE NAO SAO O CANAL DE TECNICA
	// =====================================================================
	/// <summary>
	/// TRES DOS VERBOS DO KIT NAO ENTRAM PELO CANAL DE TECNICA -- o censo os declara em
	/// <see cref="CensoDeSkills.PorOutroCanal"/>, e declarar nao e ligar. Aqui as portas sao
	/// DIRIGIDAS, com o par de sempre: com o cargo funciona, sem o cargo o jogo recusa.
	///
	/// `RankChat` -> `cargo_falar`, `Permission` -> `sala_autorizar`, `Appoint_Elder` -> `cargo_nomear`.
	///
	/// FICAM DE FORA, e esta escrito pra nao virar cobertura imaginaria: `Kaioken_Settings` (que o
	/// censo aponta pro proprio `Kaioken`, provado na secao 4) e `Mystic` (`forma:mistico`) -- e o
	/// Mistico e o achado desta secao, no `GD.Print` logo abaixo.
	/// </summary>
	private void AsPortasQueNaoSaoOCanalDeTecnica(
		ServerPlayer dono, ServerPlayer controle, ServerPlayer namek, Action<string, bool, string> C)
	{
		_tronos.Clear();
		TickDosCargos();

		// ---- RankChat -> cargo_falar ----
		List<string>? guarda = EscutaDeAvisos;
		var ditos = new List<string>();
		EscutaDeAvisos = ditos;
		try { ComandoDeCargo(controle, "cargo_falar", "alguem me ouve?"); }
		finally { EscutaDeAvisos = guarda; }
		C("sem cargo, o canal dos cargos (`cargo_falar`) recusa",
		  string.Join(" | ", ditos).Contains("canal e de quem carrega um cargo", StringComparison.OrdinalIgnoreCase),
		  string.Join(" | ", ditos));

		_tronos["turtle"] = dono.Conta;
		TickDosCargos();
		ditos = [];
		EscutaDeAvisos = ditos;
		try { ComandoDeCargo(dono, "cargo_falar", "o Eremita fala"); }
		finally { EscutaDeAvisos = guarda; }
		C("...e com o cargo ele NAO recusa (o RankChat e a unica skill que todo cargo tem)",
		  !string.Join(" | ", ditos).Contains("canal e de quem carrega", StringComparison.OrdinalIgnoreCase),
		  string.Join(" | ", ditos));

		// ---- Permission -> sala_autorizar ----
		// O Eremita Tartaruga NAO tem a chave da Sala (o kit dele nao a inclui); o Mestre Korin tem.
		// Sao os dois lados da mesma porta, e o portao e a SKILL e nao o trono.
		bool antes = controle.SalaAutorizada;
		controle.SalaAutorizada = false;
		ComandoDaSalaDoTempo(dono, "sala_autorizar", controle.Id.ToString());
		C("o Eremita Tartaruga (sem a skill Permission) NAO autoriza a Sala do Tempo",
		  !controle.SalaAutorizada, "");

		_tronos.Clear();
		_tronos["korin"] = dono.Conta;
		TickDosCargos();
		ComandoDaSalaDoTempo(dono, "sala_autorizar", controle.Id.ToString());
		C("...e o Mestre Korin, que recebe a Permission do cargo, autoriza de verdade",
		  controle.SalaAutorizada && dono.Livro.Sabe("/datum/skill/rank/Permission"), "");
		controle.SalaAutorizada = antes;

		// ---- Appoint_Elder -> cargo_nomear ----
		_convitesDeAnciao.Clear();
		_tronos.Clear();
		TickDosCargos();
		VerboNomearAnciao(dono, namek.Id.ToString());
		C("sem ser Grande Anciao, `cargo_nomear` nao convida ninguem",
		  !_convitesDeAnciao.ContainsKey(namek.Conta), "");

		_tronos["elder"] = dono.Conta;
		TickDosCargos();
		VerboNomearAnciao(dono, namek.Id.ToString());
		C("...e o Grande Anciao, que recebe o Appoint_Elder do cargo, convida de verdade",
		  _convitesDeAnciao.ContainsKey(namek.Conta)
		  && dono.Livro.Sabe("/datum/skill/rank/Appoint_Elder"), "");
		_convitesDeAnciao.Clear();

		// ============================ O MISTICO DO KAIOSHIN -- DIVIDA ACHADA AQUI ============================
		// O censo aponta o verb `Mystic` do DM pro canal `forma:mistico`, e a forma existe. O que nao
		// existe e o elo: a unica coisa que libera o Mistico e o `ConcederMistico`
		// (`GameServer.Formas.cs:273`), e ele **nao tem um unico chamador de producao** -- so bancada.
		// Ou seja: o Kaioshin recebe a skill `kai/Mystic` no livro e continua sem conseguir entrar na
		// forma, e o painel do cargo a anuncia como PRONTA.
		//
		// A linha abaixo afirma so o que e verdade hoje (a skill chega); a divida vai no console toda
		// rodada, porque uma checagem que so ficasse verde no dia do conserto seria uma bancada que
		// reprova quando o bug e corrigido.
		// ================================================================================================
		_tronos.Clear();
		_tronos["kaioshin"] = dono.Conta;
		TickDosCargos();
		bool temSkill = dono.Livro.Sabe("/datum/skill/kai/Mystic");
		bool formaAberta = dono.Forma.Despertou(Jandirus.Core.Forms.Catalogo.IdMistico);
		C("o Kaioshin recebe a skill do Mistico",
		  temSkill, "");
		if (temSkill && !formaAberta)
			GD.Print("[cargo] DIVIDA: o Kaioshin tem a skill `kai/Mystic` no livro e a FORMA continua "
				   + "trancada -- `ConcederMistico` (GameServer.Formas.cs:273) nao tem chamador de "
				   + "producao, e o painel do cargo anuncia o Mistico como PRONTO.");

		_tronos.Clear();
		TickDosCargos();
	}

	// =====================================================================
	// 4. O EFEITO DE VERDADE, NO CORPO
	// =====================================================================
	/// <summary>
	/// A SKILL NO LIVRO NAO E A TECNICA NO CORPO -- e o Kaio-ken e quem prova a diferenca.
	///
	/// Ele e a escolha certa por tres motivos: e a metade VIVA da pergunta do dono ("o Kaio aprende
	/// Kaioken e a Genkidama?"), o efeito dele e um numero da ficha (`KaioPcnt`, familia 2 do
	/// `powerlevel()`) e nao um projetil no mundo, e ele nao toca o disco -- ver a armadilha do
	/// `Peer` emprestado no cabecalho.
	///
	/// As tres metades: sem o cargo nao acende, com o cargo acende, e apertar de novo apaga.
	/// </summary>
	private void OKaiokenAcendeDeVerdade(ServerPlayer dono, ServerPlayer controle, Action<string, bool, string> C)
	{
		_tronos.Clear();
		TickDosCargos();

		double antesControle = controle.Ficha.KaioPcnt;
		UsarHabilidade(controle, "Kaioken");
		C("sem o cargo, apertar o Kaio-ken NAO acende nada no corpo",
		  !TemBuff(controle, "Kaioken") && Math.Abs(controle.Ficha.KaioPcnt - antesControle) < 1e-9,
		  $"KaioPcnt {controle.Ficha.KaioPcnt}");

		_tronos["nkai"] = dono.Conta;
		TickDosCargos();
		dono.Ficha.KO = false;
		dono.Ficha.dead = false;
		dono.Ficha.Ki = dono.Ficha.MaxKi;

		UsarHabilidade(dono, "Kaioken");
		C("...e com o cargo do Kaio do Norte ele ACENDE no corpo (KaioPcnt sobe de 1)",
		  TemBuff(dono, "Kaioken") && dono.Ficha.KaioPcnt > 1,
		  $"buff {TemBuff(dono, "Kaioken")}, KaioPcnt {dono.Ficha.KaioPcnt}");

		UsarHabilidade(dono, "Kaioken");
		C("...e apertar de novo APAGA (o toggle dos cinco verbs do `kaioken.dm`)",
		  !TemBuff(dono, "Kaioken") && Math.Abs(dono.Ficha.KaioPcnt - 1) < 1e-9,
		  $"KaioPcnt {dono.Ficha.KaioPcnt}");

		// E LARGAR O CARGO TIRA A TECNICA DO CORPO, e nao so do livro: a mesma tecla, um tique depois,
		// volta a ser recusada. E a metade que o `--cargoportas` nao consegue afirmar (la nao ha quem
		// aperte a tecla).
		_tronos.Remove("nkai");
		TickDosCargos();
		List<string>? guarda = EscutaDeAvisos;
		var ditos = new List<string>();
		EscutaDeAvisos = ditos;
		try { UsarHabilidade(dono, "Kaioken"); }
		finally { EscutaDeAvisos = guarda; }
		C("largando o cargo, a MESMA tecla volta a ser recusada pelo nome",
		  !TemBuff(dono, "Kaioken") && NegouPorNaoSaber(string.Join(" | ", ditos)),
		  string.Join(" | ", ditos));
	}
}
