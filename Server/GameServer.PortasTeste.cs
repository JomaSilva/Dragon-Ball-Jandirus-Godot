using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DAS PORTAS DE CARGO -- `--cargoportas`. Nomeacao, sucessao, duelo formal e dadiva.
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// As regras puras (`Core/Ranks/PortasDeCargo.cs`) se provam com numeros na mao, e a metade delas
/// e prazo: carencia de 7 dias, intervalo de 2, janela semanal de adiamentos. **Nenhuma dessas se
/// prova jogando** -- esperar sete dias reais nao e teste, e um duelo pelo titulo precisa de duas
/// pessoas com God Ki desperto no mesmo minuto.
///
/// E a corrente e o que este port mais quebra: cada elo (o Core, o verb, o tique, o `_tronos`, o
/// livro de skills) pode estar certo com a corrente arrebentada no meio. Foi assim que a
/// `RankDef.Concede` passou meses declarando trinta kits que **nenhuma linha de codigo lia**.
/// ==============================================================================
///
/// AS CINCO SECOES:
///  1. A DADIVA E COERENTE -- toda chave existe, todo typepath existe, e o que sai e so o que entrou.
///  2. NOMEACAO -- inclusive o convite que ENVELHECE (o nomeador perde o cargo antes da resposta).
///  3. SUCESSAO -- as tres saidas do `Murder.dm`, e o NOCAUTE que NAO pode transferir trono.
///  4. O DUELO PURO -- os prazos que ninguem consegue esperar e a escada de quem vence.
///  5. O DUELO VIVO -- desafiar, aceitar, nocautear, e o titulo trocando de mao com o relogio zerado.
///
/// ============================ O ESTADO DE VERDADE E FOTOGRAFADO ============================
/// Esta bancada mexe em TRES arquivos do mundo (`cargos.txt`, `herdeiros.txt`, `titulo.txt`) --
/// mesma precaucao da `--mestreteste`, que grava o `mestres.txt`: os tres estados sao fotografados
/// na entrada e devolvidos (com regravacao) no `finally`. Rodar a bancada tem que terminar como
/// comecou, inclusive no disco.
/// ==========================================================================================
/// </summary>
public partial class GameServer
{
	private const int IdBaseDasPortas = 90_700;

	private int _prtOk, _prtFalhou;

	private void AfirmarPrt(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _prtOk++; GD.Print($"[portas]   OK    {oque}"); return; }
		_prtFalhou++;
		GD.PrintErr($"[portas]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDasPortas()
	{
		_prtOk = _prtFalhou = 0;
		GD.Print("[portas] ============ PORTAS DE CARGO: nomeacao, sucessao, duelo ============");

		// A FOTO DO MUNDO DE VERDADE.
		var tronosReais = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		var herdeirosReais = new List<string>(_herdeirosDeVegeta);
		// A TAREFA DE MUNDO ENTROU NA FOTO com o segundo ramo do `god_task_new`: sem estas duas, rodar
		// esta bancada APAGARIA a tarefa de um Deus de verdade -- o `LimparEstadoDoTitulo` logo abaixo
		// zera tudo, e o `finally` so devolve o que a foto tiver.
		(long desde, long ultimo, int adi, long semana, string alvo, string nome, long prazo, long prox,
		 int falhas, string pChave, string pNome) tituloReal =
			(_duelo.TituloDesde, _duelo.UltimoDuelo, _duelo.Adiamentos, _duelo.SemanaDosAdiamentos,
			 _tarefaAlvo, _tarefaNomeDoAlvo, _tarefaPrazo, _tarefaProxima, _tarefaFalhas,
			 _tarefaPlanetaChave, _tarefaPlanetaNome);

		_tronos.Clear();
		_herdeirosDeVegeta.Clear();
		LimparEstadoDoTitulo();

		ZoneKey zona = ZoneKey.Premade(
			Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		ServerPlayer Forjar(int i, string nome, string raca, double bp = 1_000_000)
		{
			var novo = new ServerPlayer
			{
				Id = IdBaseDasPortas + i,
				Peer = null,
				Name = nome,
				Race = raca,
				Genero = "Male",
				Idade = 30,
				Zone = zona,
				Pos = new Vec2(i * 3f * ZoneCollision.TileSize, 0),
				Conta = $"bancada_portas_{i}",
				Slot = 0,
				Ficha = new Fighter { Race = raca, BP = bp },
				Livro = new SkillBook(),
			};
			novo.Ficha.Class = "Normal";
			PorNoMundo(novo);
			novo.Ficha.Ki = novo.Ficha.MaxKi;
			return novo;
		}

		ServerPlayer namek = Forjar(1, "bancada: o Grande Anciao", "Namekian");
		ServerPlayer novico = Forjar(2, "bancada: o Namekuseijin", "Namekian");
		ServerPlayer humano = Forjar(3, "bancada: o humano", "Human");
		ServerPlayer rei = Forjar(4, "bancada: o Rei", "Saiyan");
		ServerPlayer saiya = Forjar(5, "bancada: o Saiyajin", "Saiyan");
		ServerPlayer herdeiro = Forjar(6, "bancada: o Principe", "Saiyan");
		ServerPlayer deus = Forjar(7, "bancada: o Deus", "Saiyan", 50_000_000);
		ServerPlayer desafiante = Forjar(8, "bancada: o desafiante", "Saiyan", 50_000_000);

		try
		{
			ADadivaEhCoerente(namek);
			ANomeacaoDosAnciaos(namek, novico, humano);
			ASucessaoPorMorte(rei, saiya, humano, herdeiro);
			ODueloEhPuro();
			ODueloAoVivo(deus, desafiante);
		}
		catch (Exception e) { AfirmarPrt($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? ""); }
		finally
		{
			foreach (ServerPlayer p in new[] { namek, novico, humano, rei, saiya, herdeiro, deus, desafiante })
			{
				ZoneList(p.Zone.Hash).Remove(p);
				_players.Remove(p.Id);
			}

			_tronos.Clear();
			foreach ((string k, string v) in tronosReais) _tronos[k] = v;
			SalvarCargos();

			_herdeirosDeVegeta.Clear();
			_herdeirosDeVegeta.AddRange(herdeirosReais);
			SalvarHerdeiros();

			_duelo.TituloDesde = tituloReal.desde;
			_duelo.UltimoDuelo = tituloReal.ultimo;
			_duelo.Adiamentos = tituloReal.adi;
			_duelo.SemanaDosAdiamentos = tituloReal.semana;
			_duelo.EmDuelo = false;
			_desafianteEsperando = "";
			_dueloDono = _dueloDesafiante = "";
			_tarefaAlvo = tituloReal.alvo;
			_tarefaNomeDoAlvo = tituloReal.nome;
			_tarefaPrazo = tituloReal.prazo;
			_tarefaProxima = tituloReal.prox;
			_tarefaFalhas = tituloReal.falhas;
			_tarefaPlanetaChave = tituloReal.pChave;
			_tarefaPlanetaNome = tituloReal.pNome;
			SalvarTitulo();
		}

		GD.Print($"[portas] ============ {_prtOk} OK, {_prtFalhou} FALHA(S) ============");
	}

	// =====================================================================
	// 1. A DADIVA
	// =====================================================================
	private void ADadivaEhCoerente(ServerPlayer cobaia)
	{
		// A TABELA COBRE OS TRINTA. Uma chave a menos aqui e um cargo que entrega o nada em
		// silencio -- e "o nada" tambem e uma resposta valida (o Lorde do Gelo), entao a diferenca
		// entre "nao concede" e "esquecemos" precisa estar ESCRITA.
		var semKit = Cargos.Todos.Select(r => r.Chave)
			.Where(c => !DadivaDeCargo.Cobertos.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
		AfirmarPrt("os 30 cargos tem entrada na tabela de dadiva", semKit.Count == 0, string.Join(", ", semKit));

		var inventados = DadivaDeCargo.Cobertos
			.Where(c => Cargos.Get(c) == null).ToList();
		AfirmarPrt("a dadiva nao inventa cargo que a tabela nao tem", inventados.Count == 0, string.Join(", ", inventados));

		// TODO TYPEPATH EXISTE NO CATALOGO. E a guarda contra o defeito mais barato e mais mudo
		// possivel: uma letra trocada num typepath vira uma skill que `Dar()` guarda e ninguem ve.
		if (_skills == null) { AfirmarPrt("o catalogo de skills carregou (sem ele nao ha o que conceder)", false); return; }
		var fantasmas = DadivaDeCargo.Todas.Where(p => _skills.Get(p) == null).ToList();
		AfirmarPrt($"os {DadivaDeCargo.Todas.Count} typepaths concedidos existem no catalogo",
			fantasmas.Count == 0, string.Join(", ", fantasmas));

		// ============================ O RELATORIO DA DADIVA ============================
		// Nao e uma checagem: e o NUMERO que o censo do catalogo pediu, agora tirado do proprio
		// codigo em vez de um script de fora. Ele diz quanto da promessa dos cargos ja tem corpo --
		// e vai acusar sozinho quando uma folha muda for portada (ou quando outra nascer muda).
		int comVerbo = 0, verbosVivos = 0, verbosMudos = 0, soNumero = 0;
		foreach (string p in DadivaDeCargo.Todas)
		{
			Skill? s = _skills.Get(p);
			if (s == null) continue;
			if (s.Verbos.Length == 0) { if (s.Buffs.Count > 0 || s.Estilo.Length > 0) soNumero++; continue; }
			comVerbo++;
			foreach (string v in s.Verbos)
				if (Tecnicas.Get(v) is { } t && t.Modo != Modo.NaoPortada) verbosVivos++; else verbosMudos++;
		}
		GD.Print($"[portas] RELATORIO DA DADIVA: {DadivaDeCargo.Todas.Count} skills concedidas pelos cargos"
			   + $" | {comVerbo} dao verbo ({verbosVivos} verbos VIVOS, {verbosMudos} ainda mudos)"
			   + $" | {soNumero} sao so numero/estilo");

		// TODO CARGO CONCEDE O CANAL (`RankChat` e a excecao `enabled=1` do DM).
		bool todosComCanal = Cargos.Todos.All(
			r => DadivaDeCargo.De(r.Chave).Contains(DadivaDeCargo.CanalDosCargos, StringComparer.OrdinalIgnoreCase));
		AfirmarPrt("todo cargo concede o canal dos cargos (RankChat)", todosComCanal);
		AfirmarPrt("quem nao tem cargo nao recebe nada", DadivaDeCargo.De("").Length == 0);

		// ============================ O QUE SAI COM O CARGO, E O QUE FICA ============================
		// A tabela do `LevantarRevogaveis` e derivada do catalogo, entao ela nao se le -- se afirma.
		// As duas listas abaixo sao as duas metades da regra, e cada uma pega um erro diferente:
		// esquecer um estilo reabre o exploit, e incluir a Telepatia rouba de quem a comprou.
		IReadOnlySet<string> sai = DadivaDeCargo.LevantarRevogaveis(_skills);

		string[] deviamSair =
		[
			"/datum/skill/style/KameStyle", "/datum/skill/style/CraneStyle", "/datum/skill/style/GodStyle",
			"/datum/skill/style/NamekStyle", "/datum/skill/style/SaiyanStyle", "/datum/skill/style/AlienStyle",
			"/datum/skill/style/DemonStyle", "/datum/skill/ki/Heal", "/datum/skill/general/kikoho",
			"/datum/skill/kai/Mystic", "/datum/skill/demon/Majin", "/datum/skill/rank/RankChat",
		];
		var faltando = deviamSair.Where(p => !sai.Contains(p)).ToList();
		AfirmarPrt("os 7 estilos (e a Cura, o Kikoho, o Mistico, o Majin) saem com o cargo",
			faltando.Count == 0, string.Join(", ", faltando));

		// AS QUE TEM DONO FORA DO CARGO. Cada uma pende de pelo menos uma arvore que NAO e de rank
		// (`Telepathy` em demon/kai, `expand` em Body e seis raciais, `observe` em demigod/kanassajin,
		// `splitform` em kanassajin/majin, `selfdestruct` em android/saibaman) -- quem as tem pode
		// te-las COMPRADO, e o cargo nao tem direito sobre elas. O `Enkumei` fica por outro motivo:
		// ele nao esta na `constituentskills` de arvore nenhuma, nem no DM (ver `DadivaDeCargo`).
		string[] deviamFicar =
		[
			"/datum/skill/Telepathy", "/datum/skill/expand", "/datum/skill/general/observe",
			"/datum/skill/general/splitform", "/datum/skill/general/selfdestruct", "/datum/skill/Enkumei",
		];
		var roubadas = deviamFicar.Where(sai.Contains).ToList();
		AfirmarPrt("o que tambem pende de arvore racial (ou de nenhuma) NAO sai com o cargo",
			roubadas.Count == 0, string.Join(", ", roubadas));

		// O PREFIXO DE ARVORE DE CARGO NAO PODE PEGAR UMA ARVORE RACIAL. E a invariante em que a
		// regra inteira se apoia: se amanha nascer uma `/datum/skill/tree/Ranger`, metade das
		// skills raciais dela viraria "coisa de cargo" e sumiria do livro de quem nunca teve um.
		var arvoresDeCargo = _skills.Arvores
			.Where(a => a.Path.StartsWith("/datum/skill/tree/Rank", StringComparison.OrdinalIgnoreCase))
			.Select(a => a.Path).ToList();
		AfirmarPrt($"so as 6 arvores de cargo casam com o prefixo (achei {arvoresDeCargo.Count})",
			arvoresDeCargo.Count == 6 && arvoresDeCargo.All(
				p => p.StartsWith("/datum/skill/tree/Rank/", StringComparison.OrdinalIgnoreCase)
				  || p.Equals("/datum/skill/tree/RankTree", StringComparison.OrdinalIgnoreCase)),
			string.Join(", ", arvoresDeCargo));

		// ---- a reconciliacao viva ----
		// O KAIO-KEN ENTRA COMO LICAO, que e a historia que esta linha sempre contou ("como se o Sr.
		// Kaioh o tivesse ensinado") e que o `Dar()` cru nao expressava. Ele e a testemunha do
		// `treeshrink` do Outro Mundo (`OtherworldRanks.dm:23-27`): perder o cargo NAO pode leva-lo
		// junto -- e agora que o Kaio-ken TAMBEM e revogavel (ele so pende da arvore do Outro Mundo),
		// a unica coisa que o salva e a marca de `wastaught`. Com `Dar()` cru ele cairia, e com razao.
		cobaia.Livro.DarComoEnsinada("/datum/skill/kaioken");

		_tronos["nkai"] = cobaia.Conta;
		ReconciliarDadiva(cobaia);
		AfirmarPrt("virar Kaio do Norte entrega a Spirit Bomb do cargo",
			cobaia.Livro.Sabe("/datum/skill/rank/SpiritBomb"));
		AfirmarPrt("...e o canal dos cargos", cobaia.Livro.Sabe(DadivaDeCargo.CanalDosCargos));

		// SUBIU DE CARGO: o kit velho sai, o novo entra.
		_tronos.Remove("nkai");
		_tronos["kaioshin"] = cobaia.Conta;
		ReconciliarDadiva(cobaia);
		AfirmarPrt("subir a Kaioshin tira a Spirit Bomb (que era do Kaio do Norte)",
			!cobaia.Livro.Sabe("/datum/skill/rank/SpiritBomb"));
		AfirmarPrt("...e entrega o Mistico do Kaioshin", cobaia.Livro.Sabe("/datum/skill/kai/Mystic"));
		AfirmarPrt("o Kaio-ken que veio de FORA do cargo continua (o treeshrink do DM)",
			cobaia.Livro.Sabe("/datum/skill/kaioken"));

		// PERDEU TUDO.
		_tronos.Remove("kaioshin");
		ReconciliarDadiva(cobaia);
		AfirmarPrt("sem cargo, as skills de rank somem", !cobaia.Livro.Sabe("/datum/skill/rank/Revive")
													  && !cobaia.Livro.Sabe(DadivaDeCargo.CanalDosCargos));

		// O MISTICO SAI JUNTO -- e ate ontem ele NAO saia. Esta linha era, literalmente, um
		// `Livro.Esquecer("/datum/skill/kai/Mystic")` de faxina no fim desta bancada: a checagem
		// nao existia e a sobra era varrida a mao. A faxina era a confissao do defeito.
		AfirmarPrt("...e o Mistico do Kaioshin sai junto (ele so pende da arvore do Outro Mundo)",
			!cobaia.Livro.Sabe("/datum/skill/kai/Mystic"));
		AfirmarPrt("...e o Kaio-ken ENSINADO continua sendo dele", cobaia.Livro.Sabe("/datum/skill/kaioken"));
		cobaia.Livro.Esquecer("/datum/skill/kaioken");

		OEstiloVoltaComOCargo(cobaia);
		ALicaoSobreviveAoTique(cobaia);
		AChaveDaSalaEhASkill(cobaia);
		ODebitoEhDITO(cobaia);
	}

	/// <summary>
	/// O QUE O CARGO NAO FAZ, ELE DIZ -- a regra 3 desta camada, e a regra 5 da casa ("falhar alto
	/// em vez de calar") do lado de quem joga.
	///
	/// A afirmacao dificil e a SEGUNDA: e facil escrever uma mensagem, e o defeito de verdade seria
	/// ela ser generica ("algumas habilidades ainda nao funcionam"). O que se prova aqui e que ela
	/// nomeia o sistema que falta, e que ele vem do CENSO -- ou seja, que ela se corrige sozinha no
	/// dia em que a Genkidama for portada.
	/// </summary>
	private void ODebitoEhDITO(ServerPlayer cobaia)
	{
		if (_skills == null) return;

		// TODA SKILL DE CARGO TEM RESPOSTA: ou ela faz alguma coisa, ou o censo sabe do que ela
		// depende. "Nao sei" nao pode ser uma das opcoes -- e a mesma regra que ja vale pros verbos.
		var semResposta = DadivaDeCargo.Todas
			.Select(p => _skills.Get(p)).Where(s => s != null)
			.Where(s => CensoDeSkills.SistemaQueFalta(s!) == "um sistema que este port ainda nao tem")
			.Select(s => s!.Path).ToList();
		AfirmarPrt("toda skill de cargo muda tem um SISTEMA nomeado (nenhuma cai no generico)",
			semResposta.Count == 0, string.Join(", ", semResposta));

		AfirmarPrt("o estilo Kame nao conta como muda (ele nao da verbo, mas MEXE na ficha)",
			CensoDeSkills.SistemaQueFalta(_skills.Get("/datum/skill/style/KameStyle")!) == null);
		AfirmarPrt("o Kamehameha nao conta como mudo (ele tem corpo no lote G5)",
			CensoDeSkills.SistemaQueFalta(_skills.Get("/datum/skill/rank/Kamehameha")!) == null);

		var escutaReal = EscutaDeAvisos;
		var ditos = new List<string>();
		EscutaDeAvisos = ditos;
		// ============================ A TESTEMUNHA TROCOU: A GENKIDAMA FOI PORTADA ============================
		// Ate 2026-09-02 a testemunha era o Kaio do Norte: a Genkidama do kit dele "nao tinha corpo neste
		// port" e a bancada exigia o recado nomeando "energia EMPRESTADA". O lote G12 portou a Genkidama
		// e o G11 o Observe -- o kit do Kaio do Norte ficou INTEIRO e as tres linhas envelheceram
		// (ficaram vermelhas com o kit funcionando, que e o jeito certo de envelhecer). A prova de que o
		// debito e DITO continua a mesma, agora com o GRANDE KAIO: o kit dele tem o Reincarnate, que
		// espera a burocracia do alem (`CensoDeSkills.SistAlem`). E o Kaio do Norte vira o controle da
		// outra direcao: "tudo pronto" nao pode dizer que falta alguma coisa.
		// ======================================================================================================
		try
		{
			_tronos["grandkai"] = cobaia.Conta;
			ReconciliarDadiva(cobaia);
		}
		finally { EscutaDeAvisos = escutaReal; }

		AfirmarPrt("virar Grande Kaio AVISA o que o cargo entregou",
			ditos.Any(t => t.Contains("o cargo te entrega", StringComparison.OrdinalIgnoreCase)),
			string.Join(" | ", ditos));

		// O REINCARNATE E A TESTEMUNHA: ele e do kit do Grande Kaio e nao tem corpo neste port.
		string recado = ditos.FirstOrDefault(
			t => t.Contains("ainda não funciona", StringComparison.OrdinalIgnoreCase)) ?? "";
		AfirmarPrt("...e AVISA, pelo nome, o que ele entregou e o port ainda nao faz",
			recado.Contains("Reincarnat", StringComparison.OrdinalIgnoreCase), string.Join(" | ", ditos));
		AfirmarPrt("...nomeando o SISTEMA que falta, e nao um 'em breve'",
			recado.Contains("burocracia do alem", StringComparison.OrdinalIgnoreCase), recado);

		_tronos.Remove("grandkai");
		ReconciliarDadiva(cobaia);

		// ============================ E A MESMA VERDADE NO PAINEL, ANTES DE TOMAR O CARGO ============================
		// O aviso acima chega a quem JA assumiu. O painel de cargos nao dizia nada a ninguem -- ele
		// mandava chave, dono e o que falta, e trinta cargos eram trinta nomes sem conteudo. **A
		// pergunta do dono (*"o Kaio aprende Kaioken e a Genkidama?"*) era literalmente irrespondivel
		// de dentro do jogo.**
		//
		// A LINHA DO PAINEL SAI DA MESMA TABELA E DO MESMO CENSO do aviso, e por isso as duas nao
		// podem divergir. Aqui a bancada afirma as duas metades no mesmo cargo: o Revive do lado
		// PRONTO, o Reincarnate do lado MUDO.
		// ========================================================================================================
		string vitrine = OQueOCargoEntrega("grandkai");
		AfirmarPrt("o painel diz o que o Grande Kaio DA (antes era chave, dono e mais nada)",
			vitrine.Length > 0, vitrine);
		AfirmarPrt("...e o Revive aparece do lado PRONTO da linha",
			vitrine.Contains("Revive", StringComparison.OrdinalIgnoreCase)
			&& vitrine.IndexOf("Revive", StringComparison.OrdinalIgnoreCase)
			   < (vitrine.IndexOf("ainda mudo", StringComparison.OrdinalIgnoreCase) is var m && m >= 0
				  ? m : int.MaxValue),
			vitrine);
		AfirmarPrt("...e o Reincarnate aparece marcado como AINDA MUDO, e nao prometido em silencio",
			vitrine.Contains("ainda mudo", StringComparison.OrdinalIgnoreCase)
			&& vitrine.IndexOf("Reincarnat", StringComparison.OrdinalIgnoreCase)
			   > vitrine.IndexOf("ainda mudo", StringComparison.OrdinalIgnoreCase),
			vitrine);

		// O CONTROLE DA OUTRA DIRECAO: o kit do Kaio do Norte ficou inteiro (Genkidama no G12, Observe
		// no G11) e a vitrine dele NAO pode mais dizer "ainda mudo" -- a mesma tabela que acusa o
		// Reincarnate e a que absolve a Genkidama, sozinha, no dia em que ela ganhou corpo.
		string nkai = OQueOCargoEntrega("nkai");
		AfirmarPrt("e o Kaio do Norte, cujo kit ficou INTEIRO (Genkidama e Observe portados), nao tem mais nada marcado como mudo",
			nkai.Contains("Spirit Bomb", StringComparison.OrdinalIgnoreCase)
			&& !nkai.Contains("ainda mudo", StringComparison.OrdinalIgnoreCase),
			nkai);

		// O CARGO QUE O DM DEIXA DE MAOS VAZIAS continua de maos vazias na vitrine -- ele so tem o
		// canal dos cargos. Sem esta checagem, um bug que enchesse a linha de todo mundo passaria.
		string vazio = OQueOCargoEntrega("frostlord");
		AfirmarPrt("e o Lorde do Gelo (que o DM deixa de maos vazias) so anuncia o canal dos cargos",
			vazio.Contains("Rank", StringComparison.OrdinalIgnoreCase) || vazio.Length <= 40, vazio);
	}

	/// <summary>
	/// O ESTILO DE LUTA E O CASO QUE ABRIA O EXPLOIT -- e ele tem DUAS metades, porque tirar do
	/// livro nao e tirar da ficha.
	///
	/// Reivindicar o Eremita Tartaruga, receber o `KameStyle`, largar o cargo e ficar com os
	/// multiplicadores era gratis: os sete estilos do jogo so pendem de arvore de cargo, ninguem
	/// nunca comprou um, e a regra velha (prefixo `/datum/skill/rank/`) nao os alcancava.
	/// </summary>
	private void OEstiloVoltaComOCargo(ServerPlayer cobaia)
	{
		_tronos["turtle"] = cobaia.Conta;
		ReconciliarDadiva(cobaia);
		AfirmarPrt("virar Eremita Tartaruga entrega o estilo Kame",
			cobaia.Livro.Sabe("/datum/skill/style/KameStyle"));

		TrocarEstilo(cobaia, "KameStyle");
		AfirmarPrt("...e da pra vestir a postura", cobaia.Ficha.EstiloAtual == "KameStyle");
		AfirmarPrt("...e ela MEXE na ficha (`defaultphysoff=5` do estilos.json)",
			cobaia.Ficha.physoffStyle > 1);

		// A INJECAO, e ela e o ponto da regra 0.7: com a regra VELHA (so o prefixo) este mesmo
		// estilo nao seria revogado. Se alguem voltar a tabela pro prefixo, as tres afirmacoes
		// abaixo caem juntas -- e esta aqui prova que elas tem o que reprovar.
		var regraVelha = new HashSet<string>(
			DadivaDeCargo.Todas.Where(p => p.StartsWith("/datum/skill/rank/", StringComparison.OrdinalIgnoreCase)),
			StringComparer.OrdinalIgnoreCase);
		AfirmarPrt("INJECAO: com a regra velha (so o prefixo) o estilo NAO sairia -- era o exploit",
			!DadivaDeCargo.Revogavel(regraVelha, cobaia.Livro, "/datum/skill/style/KameStyle"));

		_tronos.Remove("turtle");
		ReconciliarDadiva(cobaia);
		AfirmarPrt("largar o cargo TIRA o estilo do livro",
			!cobaia.Livro.Sabe("/datum/skill/style/KameStyle"));
		AfirmarPrt("...e DESPE a postura (escrever o corte nao e aplicar o corte)",
			cobaia.Ficha.EstiloAtual.Length == 0);
		AfirmarPrt("...e os multiplicadores da ficha voltam a 1",
			Math.Abs(cobaia.Ficha.physoffStyle - 1) < 1e-9
			&& Math.Abs(cobaia.Ficha.techniqueStyle - 1) < 1e-9
			&& Math.Abs(cobaia.Ficha.staminadrainStyle - 1) < 1e-9);
	}

	/// <summary>
	/// O DEFEITO NA OUTRA DIRECAO, e ninguem o via porque ele era silencioso: metade das skills de
	/// cargo tem `teacher = TRUE` (o Kamehameha entre elas), e o tique de 1 Hz comia a licao do
	/// aluno um segundo depois de ele te-la recebido, so porque o typepath comecava com `rank/`.
	/// </summary>
	private void ALicaoSobreviveAoTique(ServerPlayer cobaia)
	{
		const string kame = "/datum/skill/rank/Kamehameha";

		cobaia.Livro.DarComoEnsinada(kame);
		ReconciliarDadiva(cobaia);
		AfirmarPrt("o Kamehameha que um MESTRE ensinou sobrevive ao tique (aluno sem cargo nenhum)",
			cobaia.Livro.Sabe(kame));

		// E O CONTRARIO TAMBEM: a mesma skill sem a marca de licao continua saindo. Sem esta metade,
		// "sobrevive" poderia significar so "a reconciliacao parou de tirar qualquer coisa".
		cobaia.Livro.Esquecer(kame);
		cobaia.Livro.Dar(kame);
		ReconciliarDadiva(cobaia);
		AfirmarPrt("...e a MESMA skill sem marca de licao continua saindo", !cobaia.Livro.Sabe(kame));
	}

	/// <summary>
	/// A SALA DO TEMPO E O UNICO ITEM DO `Concede` QUE ABRE UMA PORTA DE MUNDO -- e ate agora ele
	/// era decorativo: quem autorizava era o TRONO (`EhGuardiao`) e nao a skill, entao o Mestre
	/// Korin, cujo `Concede` promete "Autorizar Sala do Tempo" desde sempre, ouvia do jogo que so o
	/// Guardiao autoriza. No DM os dois cargos ligam a mesma skill (`EarthRanks.dm:22` e `:33`).
	/// </summary>
	private void AChaveDaSalaEhASkill(ServerPlayer cobaia)
	{
		ServerPlayer? aluno = _players.Values.FirstOrDefault(p => p != cobaia && EhPessoa(p));
		if (aluno == null) { AfirmarPrt("havia outro corpo no mundo pra autorizar", false); return; }

		bool antes = aluno.SalaAutorizada;
		aluno.SalaAutorizada = false;

		_tronos["korin"] = cobaia.Conta;
		ReconciliarDadiva(cobaia);
		AfirmarPrt("o Mestre Korin recebe a chave da Sala do Tempo (o `Concede` dele sempre prometeu)",
			cobaia.Livro.Sabe("/datum/skill/rank/Permission"));

		ComandoDaSalaDoTempo(cobaia, "sala_autorizar", aluno.Id.ToString());
		AfirmarPrt("...e ele autoriza de verdade (o portao e a SKILL, nao o trono)", aluno.SalaAutorizada);

		aluno.SalaAutorizada = false;
		_tronos.Remove("korin");
		ReconciliarDadiva(cobaia);
		AfirmarPrt("perder o cargo tira a chave do livro",
			!cobaia.Livro.Sabe("/datum/skill/rank/Permission"));
		ComandoDaSalaDoTempo(cobaia, "sala_autorizar", aluno.Id.ToString());
		AfirmarPrt("...e a porta se fecha junto (ninguem precisou lembrar de fecha-la)",
			!aluno.SalaAutorizada);

		aluno.SalaAutorizada = antes;
	}

	// =====================================================================
	// 2. NOMEACAO
	// =====================================================================
	private void ANomeacaoDosAnciaos(ServerPlayer anciao, ServerPlayer novico, ServerPlayer humano)
	{
		_convitesDeAnciao.Clear();

		// QUEM NAO E GRANDE ANCIAO NAO NOMEIA -- a primeira pergunta do Core.
		AfirmarPrt("sem o cargo de Grande Anciao, nao se nomeia",
			Nomeacao.PorQueNao("", "northelder", "Namekian", "Namekian", "") != null);

		_tronos["elder"] = anciao.Conta;

		AfirmarPrt("o Grande Anciao pode nomear um Namekuseijin",
			Nomeacao.PorQueNao("elder", "northelder", "Namekian", "Namekian", "") == null);
		AfirmarPrt("um humano nao vira Anciao de Namek",
			Nomeacao.PorQueNao("elder", "northelder", "Human", "Human", "") != null);
		AfirmarPrt("meio-Namekuseijin vale (o sangue da mae conta)",
			Nomeacao.PorQueNao("elder", "northelder", "Human", "Namekian", "") == null);
		AfirmarPrt("nao se nomeia quem ja carrega OUTRO cargo",
			Nomeacao.PorQueNao("elder", "northelder", "Namekian", "Namekian", "turtle") != null);
		AfirmarPrt("mas trocar de ASSENTO e permitido (nao e acumular)",
			Nomeacao.PorQueNao("elder", "northelder", "Namekian", "Namekian", "southelder") == null);
		AfirmarPrt("'guardian' nao e assento de Anciao",
			Nomeacao.PorQueNao("elder", "guardian", "Namekian", "Namekian", "") != null);

		// ---- a corrente: convidar, aceitar, e o kit chegando ----
		VerboNomearAnciao(anciao, novico.Id.ToString());
		AfirmarPrt("o convite fica pendente pro convidado", _convitesDeAnciao.ContainsKey(novico.Conta));

		VerboResponderNomeacao(novico, aceitou: false);
		AfirmarPrt("recusar nao entroniza ninguem", CargoDe(novico.Conta).Length == 0);

		VerboNomearAnciao(anciao, novico.Id.ToString());
		VerboResponderNomeacao(novico, aceitou: true);
		AfirmarPrt("aceitar poe o Namekuseijin no primeiro assento vago",
			string.Equals(CargoDe(novico.Conta), "northelder", StringComparison.OrdinalIgnoreCase),
			CargoDe(novico.Conta));
		AfirmarPrt("o Anciao cardeal recebe o kit dos quatro (Makankosappo)",
			novico.Livro.Sabe("/datum/skill/rank/Makkankosappo"));

		// O SEGUNDO CONVIDADO CAI NO ASSENTO SEGUINTE -- o "primeiro vago" anda.
		VerboNomearAnciao(anciao, humano.Id.ToString());
		AfirmarPrt("o humano nem recebe convite", !_convitesDeAnciao.ContainsKey(humano.Conta));

		// ============================ O CONVITE QUE ENVELHECE ============================
		// Entre o convite e a resposta o mundo anda. Sem a reconferencia, aceitar um convite de tres
		// dias atras abriria uma porta que o mundo ja fechou -- e e o mesmo tipo de defeito que o
		// pedido de amizade ja teve que resolver.
		// ==============================================================================
		novico.Livro.Esquecer("/datum/skill/rank/Makkankosappo");
		_tronos.Remove("northelder");
		VerboNomearAnciao(anciao, novico.Id.ToString());
		_tronos.Remove("elder");                       // o Grande Anciao cai ANTES da resposta
		VerboResponderNomeacao(novico, aceitou: true);
		AfirmarPrt("convite de um Grande Anciao que caiu nao vale mais",
			CargoDe(novico.Conta).Length == 0, CargoDe(novico.Conta));

		_tronos.Clear();
		ReconciliarDadiva(novico);
	}

	// =====================================================================
	// 3. SUCESSAO
	// =====================================================================
	private void ASucessaoPorMorte(ServerPlayer rei, ServerPlayer saiya, ServerPlayer humano, ServerPlayer herdeiro)
	{
		// ---- as regras puras ----
		AfirmarPrt("so dois cargos trocam de dono por morte",
			Sucessao.TemSucessao("kov") && Sucessao.TemSucessao("frostlord")
			&& !Sucessao.TemSucessao("kaioshin"));
		AfirmarPrt("matar o Rei sendo Saiyajin da o trono", Sucessao.AlgozHerda("kov", "Saiyan", "Saiyan", "Normal"));
		AfirmarPrt("matar o Rei sendo humano NAO da o trono", !Sucessao.AlgozHerda("kov", "Human", "Human", "Normal"));
		AfirmarPrt("o Lorde do Gelo tambem sai pela CLASSE (o `|Class==` do DM)",
			Sucessao.AlgozHerda("frostlord", "Human", "Human", "Frost Demon"));
		AfirmarPrt("e 'Icer' vale por 'Frost Demon' (o rotulo do races.json)",
			Sucessao.AlgozHerda("frostlord", "Icer", "Icer", "Normal"));
		AfirmarPrt("so o trono de Vegeta tem linha de sangue",
			Sucessao.TemHerdeiros("kov") && !Sucessao.TemHerdeiros("frostlord"));
		AfirmarPrt("herdeiro MORTO nao herda", !Sucessao.HerdeiroServe("Saiyan", "Saiyan", morto: true));

		// ---- 1. o algoz do sangue leva ----
		_tronos["kov"] = rei.Conta;
		rei.Ficha.dead = true;
		AoPerderALuta(rei, saiya, morreu: true);
		AfirmarPrt("matar o Rei sendo Saiyajin transfere o trono na hora",
			string.Equals(CargoDe(saiya.Conta), "kov", StringComparison.OrdinalIgnoreCase), CargoDe(saiya.Conta));
		AfirmarPrt("...e o kit do trono vai junto (Final Flash)",
			saiya.Livro.Sabe("/datum/skill/rank/FinalFlash"));
		AfirmarPrt("...e o Rei morto fica sem o kit", !rei.Livro.Sabe("/datum/skill/rank/FinalFlash"));

		// ---- 2. o algoz de fora, com herdeiro nomeado ----
		_tronos.Clear();
		ReconciliarDadiva(saiya);
		_tronos["kov"] = rei.Conta;
		_herdeirosDeVegeta.Clear();

		// O REI NOMEIA. Passa pelo verb de producao, e nao pela lista na mao.
		VerboHerdeiro(rei, herdeiro.Id.ToString(), nomear: true);
		AfirmarPrt("o Rei poe um Saiyajin na linha de sucessao",
			_herdeirosDeVegeta.Contains(herdeiro.Conta, StringComparer.OrdinalIgnoreCase));
		VerboHerdeiro(rei, humano.Id.ToString(), nomear: true);
		AfirmarPrt("um humano nao entra na linha de sucessao de Vegeta",
			!_herdeirosDeVegeta.Contains(humano.Conta, StringComparer.OrdinalIgnoreCase));

		// O NOCAUTE NAO E MORTE. Este e o defeito injetado desta secao: se a sucessao estivesse
		// pendurada em "perdeu a luta" em vez de "morreu", cada nocaute do jogo trocaria um trono.
		AoPerderALuta(rei, humano, morreu: false);
		AfirmarPrt("NOCAUTEAR o Rei nao mexe no trono",
			string.Equals(CargoDe(rei.Conta), "kov", StringComparison.OrdinalIgnoreCase), CargoDe(rei.Conta));

		AoPerderALuta(rei, humano, morreu: true);
		AfirmarPrt("morto por quem nao e do sangue, o trono vai pro herdeiro",
			string.Equals(CargoDe(herdeiro.Conta), "kov", StringComparison.OrdinalIgnoreCase), CargoDe(herdeiro.Conta));

		// ---- 3. sem herdeiro nenhum: o trono VAGA ----
		_tronos.Clear();
		ReconciliarDadiva(herdeiro);
		_herdeirosDeVegeta.Clear();
		_tronos["kov"] = rei.Conta;
		AoPerderALuta(rei, humano, morreu: true);
		AfirmarPrt("sem herdeiro, o trono de Vegeta VAGA",
			!_tronos.ContainsKey("kov"));

		// ---- 4. o Lorde do Gelo, e a comparacao por CONTA ----
		_tronos["frostlord"] = rei.Conta;
		humano.Class = "Frost Demon";
		humano.Ficha.Class = "Frost Demon";
		AoPerderALuta(rei, humano, morreu: true);
		AfirmarPrt("o titulo de Lorde do Gelo passa a quem matou, pela CONTA",
			_tronos.TryGetValue("frostlord", out string? dono) && dono == humano.Conta, dono ?? "vago");

		_tronos.Clear();
		_herdeirosDeVegeta.Clear();
		rei.Ficha.dead = false;
		humano.Class = "Normal";
		humano.Ficha.Class = "Normal";
		foreach (ServerPlayer p in new[] { rei, saiya, humano, herdeiro }) ReconciliarDadiva(p);
	}

	// =====================================================================
	// 4. O DUELO -- as regras puras
	// =====================================================================
	private void ODueloEhPuro()
	{
		long t0 = 1_000_000_000_000;
		var e = new EstadoDoDuelo { TituloDesde = t0, UltimoDuelo = t0, SemanaDosAdiamentos = t0 };

		// CADA RECUSA ISOLADA. Um teste que so chamasse "pode desafiar?" com tudo errado passaria
		// verde com uma unica guarda viva -- e o corolario da casa e justo esse: teto que nunca
		// dispara e indistinguivel de teto nenhum.
		long depois = t0 + Duelo.Carencia + Duelo.Intervalo;
		AfirmarPrt("sem Deus no trono nao ha desafio",
			Duelo.PodeDesafiar(e, depois, false, false, true, true, true) == RecusaDeDuelo.SemDono);
		AfirmarPrt("o proprio Deus nao se desafia",
			Duelo.PodeDesafiar(e, depois, true, true, true, true, true) == RecusaDeDuelo.VoceEhODono);
		AfirmarPrt("caido nao desafia",
			Duelo.PodeDesafiar(e, depois, true, false, false, true, true) == RecusaDeDuelo.Caido);
		AfirmarPrt("sem God Ki desperto nao se desafia um deus",
			Duelo.PodeDesafiar(e, depois, true, false, true, false, true) == RecusaDeDuelo.SemGodKi);
		AfirmarPrt("na carencia de 7 dias o trono nao aceita desafio",
			Duelo.PodeDesafiar(e, t0 + Duelo.Carencia - 1, true, false, true, true, true) == RecusaDeDuelo.Carencia);
		// O INTERVALO PRECISA DO SEU PROPRIO ESTADO, e isto foi a bancada corrigindo a bancada: com
		// `UltimoDuelo = TituloDesde`, vencer a carencia de 7 dias vence o intervalo de 2 junto -- a
		// checagem passava pela guarda ERRADA e nunca teria acusado o intervalo quebrado. E o
		// corolario da casa outra vez: teto que nunca dispara e teto nenhum.
		var recemDuelado = new EstadoDoDuelo
		{
			TituloDesde = t0,
			UltimoDuelo = t0 + Duelo.Carencia,   // duelou ONTEM, ja fora da carencia
			SemanaDosAdiamentos = t0,
		};
		AfirmarPrt("o intervalo de 2 dias entre desafios vale",
			Duelo.PodeDesafiar(recemDuelado, t0 + Duelo.Carencia + 1000, true, false, true, true, true)
				== RecusaDeDuelo.IntervaloFechado);
		AfirmarPrt("...e abre quando os 2 dias passam",
			Duelo.PodeDesafiar(recemDuelado, t0 + Duelo.Carencia + Duelo.Intervalo + 1, true, false, true, true, true)
				== RecusaDeDuelo.Pode);
		AfirmarPrt("nao se toma o titulo de quem esta offline",
			Duelo.PodeDesafiar(e, depois, true, false, true, true, false) == RecusaDeDuelo.DonoAusente);
		AfirmarPrt("com tudo em ordem, o desafio passa",
			Duelo.PodeDesafiar(e, depois, true, false, true, true, true) == RecusaDeDuelo.Pode);

		var emDuelo = new EstadoDoDuelo { EmDuelo = true };
		AfirmarPrt("um duelo por vez",
			Duelo.PodeDesafiar(emDuelo, depois, true, false, true, true, true) == RecusaDeDuelo.DueloEmCurso);

		// ---- adiamentos e covardia ----
		var a = new EstadoDoDuelo { TituloDesde = t0, UltimoDuelo = t0, SemanaDosAdiamentos = t0 };
		AfirmarPrt("1o adiamento nao e covardia", !Duelo.Adiar(a, depois));
		AfirmarPrt("adiar CONSOME o desafio da janela de 2 dias", a.UltimoDuelo == depois);
		AfirmarPrt("2o adiamento ainda nao e covardia", !Duelo.Adiar(a, depois + 1000));
		AfirmarPrt("o 3o adiamento na semana E covardia", Duelo.Adiar(a, depois + 2000));

		// A JANELA ROLA: dois adiamentos, oito dias depois, o terceiro nao pune.
		var b = new EstadoDoDuelo { SemanaDosAdiamentos = t0 };
		Duelo.Adiar(b, t0);
		Duelo.Adiar(b, t0 + 1000);
		AfirmarPrt("passada a semana, os adiamentos zeram",
			!Duelo.Adiar(b, t0 + Duelo.JanelaDeAdiamentos + 1) && b.Adiamentos == 1);

		// ---- quem vence ----
		AfirmarPrt("Deus nocauteado: o titulo troca",
			Duelo.Passo(false, false, true, false, 0, 50, false) == FimDeDuelo.Desafiante);
		AfirmarPrt("desafiante nocauteado: o Deus retem",
			Duelo.Passo(false, false, false, true, 50, 0, false) == FimDeDuelo.Dono);
		AfirmarPrt("os dois no chao: vence quem tem mais vida",
			Duelo.Passo(false, false, true, true, 10, 40, false) == FimDeDuelo.Desafiante);
		AfirmarPrt("empate exato de vida fica com o Deus (o `>=` do DM)",
			Duelo.Passo(false, false, true, true, 30, 30, false) == FimDeDuelo.Dono);
		AfirmarPrt("prazo estourado: o desafiante FALHOU (o trono nao muda por empate)",
			Duelo.Passo(false, false, false, false, 100, 100, true) == FimDeDuelo.Dono);
		AfirmarPrt("Deus que sumiu/morreu perde o titulo",
			Duelo.Passo(true, false, false, false, 100, 100, false) == FimDeDuelo.Desafiante);
		AfirmarPrt("desafiante que sumiu perde",
			Duelo.Passo(false, true, false, false, 100, 100, false) == FimDeDuelo.Dono);
		AfirmarPrt("com os dois de pe e prazo aberto, o duelo CONTINUA",
			Duelo.Passo(false, false, false, false, 100, 100, false) == FimDeDuelo.Continua);
	}

	// =====================================================================
	// 5. O DUELO AO VIVO
	// =====================================================================
	private void ODueloAoVivo(ServerPlayer deus, ServerPlayer desafiante)
	{
		_tronos.Clear();
		LimparEstadoDoTitulo();
		_tronos["godofdestruction"] = deus.Conta;
		ReconciliarDadiva(deus);

		desafiante.Ficha.godki = new GodKiState { awakened = true, mastery = 70 };

		// SEM GOD KI NAO PASSA -- pelo VERB de producao, e nao so pela regra pura.
		deus.Ficha.godki = null;
		long agora = NowMs();
		_duelo.TituloDesde = agora - Duelo.Carencia - 1;   // carencia vencida
		_duelo.UltimoDuelo = agora - Duelo.Intervalo - 1;  // intervalo aberto

		VerboDesafiar(deus);   // o proprio Deus tentando: nao abre duelo
		AfirmarPrt("o Deus nao se desafia (pelo verb)", _desafianteEsperando.Length == 0);

		VerboDesafiar(desafiante);
		AfirmarPrt("o desafio com God Ki desperto fica pendente",
			string.Equals(_desafianteEsperando, desafiante.Conta, StringComparison.OrdinalIgnoreCase));
		AfirmarPrt("...e o trono fica ocupado com a disputa aberta", _duelo.EmDuelo);

		// ADIAR: o desafio se desfaz e o contador anda.
		VerboResponderDesafio(deus, aceitou: false);
		AfirmarPrt("adiar desfaz o desafio", _desafianteEsperando.Length == 0 && !_duelo.EmDuelo);
		AfirmarPrt("adiar gasta 1 dos 2 adiamentos", _duelo.Adiamentos == 1);
		AfirmarPrt("adiar fecha o intervalo de 2 dias (nao da pra desafiar de novo agora)",
			Duelo.PodeDesafiar(_duelo, NowMs(), true, false, true, true, true) == RecusaDeDuelo.IntervaloFechado);

		// ---- o duelo de verdade ----
		_duelo.UltimoDuelo = NowMs() - Duelo.Intervalo - 1;
		VerboDesafiar(desafiante);
		VerboResponderDesafio(deus, aceitou: true);
		AfirmarPrt("aceitar poe os dois em duelo", _dueloDono.Length > 0 && _dueloDesafiante.Length > 0);
		AfirmarPrt("o desafiante foi levado pra perto do Deus (nao ha arena neste port)",
			desafiante.Zone.Hash == deus.Zone.Hash
			&& Math.Abs(desafiante.Pos.X - deus.Pos.X) <= 4 * ZoneCollision.TileSize + 1);

		// A CONTAGEM SEGURA O DUELO: nocautear o Deus ANTES do "COMECEM!" nao pode decidir nada.
		deus.Ficha.KO = true;
		PassoDoDueloEmCurso(NowMs());
		AfirmarPrt("durante a contagem o duelo nao termina",
			string.Equals(_tronos["godofdestruction"], deus.Conta, StringComparison.OrdinalIgnoreCase));

		// AGORA VALE.
		PassoDoDueloEmCurso(_dueloComeca + 1);
		AfirmarPrt("nocautear o Deus depois do COMECEM transfere o titulo",
			_tronos.TryGetValue("godofdestruction", out string? novo)
			&& string.Equals(novo, desafiante.Conta, StringComparison.OrdinalIgnoreCase), novo ?? "vago");
		AfirmarPrt("o duelo se fecha", !_duelo.EmDuelo && _dueloDono.Length == 0);
		AfirmarPrt("o novo Deus comeca com a carencia de 7 dias",
			Duelo.PodeDesafiar(_duelo, NowMs(), true, false, true, true, true) == RecusaDeDuelo.Carencia);
		AfirmarPrt("...e com a ficha de falhas limpa", _tarefaFalhas == 0);
		AfirmarPrt("...e com os adiamentos zerados", _duelo.Adiamentos == 0);

		// ---- a covardia derruba o titulo ----
		_duelo.TituloDesde = NowMs() - Duelo.Carencia - 1;
		_duelo.UltimoDuelo = NowMs() - Duelo.Intervalo - 1;
		_duelo.Adiamentos = Duelo.AdiamentosPermitidos;   // ja gastou os dois desta semana
		deus.Ficha.KO = false;
		deus.Ficha.godki = new GodKiState { awakened = true, mastery = 70 };

		VerboDesafiar(deus);                       // agora o ex-Deus e quem desafia
		AfirmarPrt("o ex-Deus pode desafiar de volta", _desafianteEsperando.Length > 0);
		VerboResponderDesafio(desafiante, aceitou: false);   // o 3o adiamento
		AfirmarPrt("o 3o adiamento na semana VAGA o trono", !_tronos.ContainsKey("godofdestruction"));
		AfirmarPrt("...e o ex-Deus perde o relogio do titulo", _duelo.TituloDesde == 0);

		ReconciliarDadiva(deus);
		ReconciliarDadiva(desafiante);
	}
}
