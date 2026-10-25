using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// OS CARGOS DO MUNDO. Quem e o Guardiao da Terra, quem e o Kaio do Norte, quem e o Kaioshin.
///
/// SAO DO SERVIDOR, NAO DO PERSONAGEM: um cargo tem UM dono no mundo inteiro, e por isso ele
/// mora aqui e nao no save de ninguem. O que o save do personagem guarda e so a CHAVE do cargo
/// que ele carrega -- e mesmo essa e reconferida contra a lista do mundo ao entrar, senao dois
/// jogadores voltariam de logout se dizendo Kaioshin.
///
/// A CHAVE E A CONTA, e isso e uma licao do original: la o sistema comparava ora por `key`, ora
/// por `name`, ora por `signature`, e o resultado era rank que nao transferia e trono que nunca
/// vagava. Aqui e SEMPRE a conta -- o unico identificador que nao muda e nao se repete.
/// </summary>
public partial class GameServer
{
	/// <summary>chave do cargo -> conta do dono. Vazio = trono vago.</summary>
	private readonly Dictionary<string, string> _tronos = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Ao lado dos saves de conta. Um arquivo de texto de uma dezena de linhas -- cargo e
	/// coisa RARA, e um formato que se le num editor vale mais aqui que economia de bytes.
	/// </summary>
	// ============================ PELO `_store`, E NAO PELO CAMINHO CRAVADO ============================
	// Este arquivo (e mais tres: `mestres.txt`, `herdeiros.txt`, `titulo.txt`) montava o caminho com
	// `GlobalizePath("user://saves")` escrito na mao, enquanto os oito `.json` do mundo perguntam ao
	// `_store.Pasta`. Sao a mesma pasta hoje -- e por isso a divergencia nunca deu sintoma.
	//
	// Ela vira defeito no dia em que a pasta se mover: uma bancada com pasta propria, ou o dono
	// rodando dois servidores na mesma maquina. Ai estes quatro continuariam apontando pro lugar
	// antigo enquanto todo o resto do mundo mudaria de casa -- e a LIMPEZA TOTAL, que apaga a pasta do
	// `_store`, deixaria os tronos, o discipulado e a sucessao inteiros para tras. E exatamente o
	// "apagou de menos" que este trabalho existe pra evitar, escrito em quatro linhas de infra.
	//
	// O `??` continua cravando o caminho pra o caso de nao haver `_store`: nao ha, hoje, mas devolver
	// vazio faria o `Path.Combine` gravar na pasta de trabalho do processo.
	// ==============================================================================================
	private string ArquivoDeCargos =>
		System.IO.Path.Combine(_store?.Pasta ?? ProjectSettings.GlobalizePath("user://saves"), "cargos.txt");

	private void CarregarCargos()
	{
		_tronos.Clear();
		CarregarAusencias();   // (2026-10-19) a ausencia de cada dono, que vaga o trono de quem sumiu -- ver `GameServer.CargoLivre.cs`
		CarregarAprendizes();   // (2026-10-19) os vinculos de aprendiz de Kaioshin -- ver `GameServer.AprendizDeKaioshin.cs`
		if (!System.IO.File.Exists(ArquivoDeCargos)) return;
		foreach (string linha in System.IO.File.ReadAllLines(ArquivoDeCargos))
		{
			string[] p = linha.Split('\t');
			if (p.Length == 2 && p[1].Length > 0) _tronos[p[0]] = p[1];
		}
		GD.Print($"[server] cargos ocupados: {_tronos.Count} de {Cargos.Todos.Length}");
	}

	private void SalvarCargos()
	{
		try
		{
			System.IO.File.WriteAllLines(ArquivoDeCargos,
				_tronos.Select(kv => $"{kv.Key}\t{kv.Value}"));
		}
		catch (Exception e) { GD.PushWarning($"[server] nao gravei os cargos: {e.Message}"); }
	}

	/// <summary>
	/// Qual cargo esta conta carrega ("" = nenhum). A CHAVE DO CARGO, e nao a da vaga (2026-10-16): quem esta na 3a vaga do
	/// Kaioshin responde `kaioshin`, e por isso os leitores de cargo do servidor (o kit, os verbs, o vacuo...) nao mudaram.
	/// </summary>
	private string CargoDe(string conta) => CargoDaVaga(VagaDaConta(conta));

	// =====================================================================
	// AS VAGAS (2026-10-16) -- o cargo de varias almas
	// =====================================================================
	/// <summary>
	/// A CHAVE DE `_tronos` DA VAGA <paramref name="i"/> (0-based) do cargo. A primeira e a propria chave do cargo -- o arquivo de
	/// sempre e os cargos de uma vaga so nao mudam em nada --, e as outras sao `chave#2` ... `chave#N` (`RankDef.Vagas`; hoje so
	/// o Kaioshin tem 5). Cada vaga e um trono inteiro: tem dono, deveres, renome e destituicao proprios.
	/// </summary>
	private static string Vaga(RankDef r, int i) => i == 0 ? r.Chave : $"{r.Chave}#{i + 1}";

	/// <summary>Todas as vagas do cargo, da primeira a ultima.</summary>
	private static IEnumerable<string> VagasDe(RankDef r)
	{
		int n = VagasIgnoradasDeTeste ? 1 : Math.Max(1, r.Vagas);
		for (int i = 0; i < n; i++) yield return Vaga(r, i);
	}

	/// <summary>O cargo de uma chave de vaga: `kaioshin#3` -> `kaioshin`.</summary>
	private static string CargoDaVaga(string vaga)
	{
		int i = vaga.IndexOf('#');
		return i < 0 ? vaga : vaga[..i];
	}

	/// <summary>A VAGA que esta conta ocupa ("" = nenhuma) -- a chave crua de `_tronos`. Quem tira alguem de um trono tira DAQUI.</summary>
	private string VagaDaConta(string conta) =>
		_tronos.FirstOrDefault(kv => string.Equals(kv.Value, conta, StringComparison.OrdinalIgnoreCase)).Key ?? "";

	/// <summary>As contas que carregam este cargo agora, na ordem das vagas.</summary>
	private List<string> DonosDe(RankDef r)
	{
		var donos = new List<string>();
		foreach (string v in VagasDe(r))
			if (_tronos.TryGetValue(v, out string? d) && d.Length > 0) donos.Add(d);
		return donos;
	}

	/// <summary>A primeira vaga livre do cargo, ou nulo com todas ocupadas.</summary>
	private string? VagaLivre(RankDef r)
	{
		foreach (string v in VagasDe(r))
			if (!_tronos.TryGetValue(v, out string? d) || d.Length == 0) return v;
		return null;
	}

	/// <summary>Quantos jogadores carregam o cargo <paramref name="chave"/> -- o que o povoamento pergunta (`cadaDonoTiraUm`).</summary>
	private int QuantosDonos(string chave) => Cargos.Get(chave) is { } r ? DonosDe(r).Count : 0;

	/// <summary>DEFEITO INJETADO (`--kaioshinteste`): todo cargo tem UMA vaga -- o "um dono por cargo" de antes. Falso em jogo.</summary>
	internal static bool VagasIgnoradasDeTeste;

	private FichaDeRank Ficha(ServerPlayer pl) => new()
	{
		Raca = pl.Race,
		Classe = pl.Class,
		BpBase = pl.Ficha.BP,
		Karma = pl.Karma,
		CargoAtual = CargoDe(pl.Conta),

		// OS QUATRO CAMPOS NOVOS. Sem preencher aqui, todo requisito que depende deles passava
		// de graca (bool false e double 0 sao os defaults, e a comparacao e ">=") -- os cargos de
		// Kaio, que o DM gateia por God Ki desperto, abririam pra qualquer um.
		RacaMae = pl.Ficha.Genoma?.MajorityRace ?? pl.Race,
		GodKiDesperto = pl.Ficha.godki?.awakened == true,
		GodKiMaestria = pl.Ficha.godki?.mastery ?? 0,
		Zeni = pl.Ficha.Zeni,

		// ============================ OS DOIS CAMPOS QUE A CONQUISTA TROUXE ============================
		// Quatro cargos carregavam a pendencia *"o port nao tem reputacao de planeta"* e ela caducou --
		// o livro-caixa existe desde a cadeia de sagas e ganhou duas fontes novas (matar habitante,
		// conquistar). O que faltava era CHEGAR aqui: regra escrita nao e regra ligada.
		//
		// O mapa e montado por planeta com povo, que sao os unicos que o DM cita nos requisitos. Nao ha
		// atalho lendo "a reputacao do jogador": ela e por planeta, e um escalar responderia a pergunta
		// errada com um numero plausivel.
		ReputacaoDePlaneta = ReputacaoDe(pl.Conta),
		PlanetasDominados = QuantosDomina(pl.Assinatura),

		// (2026-10-19) OS DOIS CAMPOS DOS CARGOS QUE SAIRAM DO ADMIN: o planeta pre-feito que o King of Acronia pede (Arconia), e o
		// Vegeta condenado que abre o trono vago do Rei -- ver `GameServer.CargoLivre.cs`.
		PlanetasQueDomina = PreFeitosQueDomina(pl.Assinatura),
		VegetaCondenado = VegetaCondenado,
	};

	/// <summary>
	/// REIVINDICAR. Tres portas, nesta ordem, e a ordem decide a mensagem que o jogador le:
	/// o trono precisa estar VAGO, a alma nao pode carregar outro cargo, e os requisitos tem
	/// que estar cumpridos.
	///
	/// (2026-10-19) E A CONCESSAO, E NAO MAIS A PORTA DO JOGADOR: o pacote `C2S.Cargo` chama a PROVA DO ESPIRITO DO CARGO
	/// (`PedirAProvaDoCargo`, `GameServer.ProvaDoCargo.cs`), que passa por aqui DUAS vezes -- com `conceder` falso pra conferir
	/// tudo (as mesmas mensagens) antes de erguer o espirito, e de verdade quando ele cai. Verdadeiro = passou nas portas.
	/// </summary>
	private bool ReivindicarCargo(ServerPlayer pl, string chave, bool conceder = true)
	{
		RankDef? r = Cargos.Get(chave);
		if (r == null) { Avisar(pl, "esse cargo nao existe."); return false; }

		// `if(!usr.client || !usr.signature || usr.KO) return` (`RankQuests.dm:272`), e o morto so reivindica cargo
		// do Outro Mundo: `if(usr.dead && !(key in RQ_SKY))` (`:299`, `:320` -- o `RQ_SKY` e o `DoOutroMundo`
		// daqui). O port nao perguntava nenhum dos dois: nocauteado no chao, ou fantasma no Alem, o cliente mandava
		// o pacote e saia Guardiao da Terra, com o kit na hora.
		if (!CaidoAgeDeTeste)
		{
			if (pl.Ficha.KO) { Avisar(pl, "nao da pra reivindicar cargo nenhum caido."); return false; }
			if (pl.Ficha.dead && !r.DoOutroMundo)
			{ Avisar(pl, $"um morto so reivindica cargo do Outro Mundo -- {r.Nome} e do mundo dos vivos."); return false; }
		}

		// (2026-10-16) POR VAGA: o cargo de varias vagas (o Kaioshin, `RankDef.Vagas`) so recusa com TODAS ocupadas.
		if (DonosDe(r).Contains(pl.Conta, StringComparer.OrdinalIgnoreCase)) { Avisar(pl, $"voce JA e {r.Nome}."); return false; }
		if (VagaLivre(r) is not { } vaga)
		{
			Avisar(pl, r.Vagas > 1 && !VagasIgnoradasDeTeste ? $"as {r.Vagas} vagas de {r.Nome} estao ocupadas." : $"{r.Nome} ja tem dono.");
			return false;
		}

		// UMA ALMA, UM TRONO. A escada e a excecao: subir de Kaio a Grande Kaio LARGA o cargo
		// anterior em vez de acumular -- senao o mundo perderia um Kaio cardeal a cada promocao.
		string atual = CargoDe(pl.Conta);
		bool subindo = atual.Length > 0 && r.Degraus.Contains(atual, StringComparer.OrdinalIgnoreCase);
		if (atual.Length > 0 && !subindo)
		{
			Avisar(pl, $"voce ja carrega {Cargos.Get(atual)?.Nome ?? atual}. Uma alma, um trono.");
			return false;
		}

		// ============================ O PEDAGIO DA ASCENSAO E O RENOME ============================
		// `RQ_PROMO_QUESTS 3` (`RankQuests.dm:53`, cobrado em `:292`): so sobe quem SERVIU no cargo de
		// agora. Enquanto o motor de tarefas nao existia, esta linha era uma pendencia escrita no
		// Grand Kai e no Kaioshin (*"o port nao tem o motor de tarefas"*) e a escada dos Kaios subia so
		// com o requisito -- mais barata que o original, e a divida mais cara do `Ranks.cs`.
		//
		// O RENOME E DE QUEM CARREGA O CARGO AGORA, e nao do trono: ver `RenomeDe`. Herdar o servico de
		// quem veio antes seria comprar promocao com o trabalho alheio.
		// ======================================================================================
		if (subindo && !MissoesDeCargo.PodeAscender(RenomeDe(VagaDaConta(pl.Conta))))
		{
			int tem = RenomeDe(VagaDaConta(pl.Conta));
			Avisar(pl, $"ainda falta RENOME para almejar algo maior: {tem}/{MissoesDeCargo.TarefasParaAscender} "
					 + $"tarefas cumpridas como {Cargos.Get(atual)?.Nome ?? atual}. Sirva ao cargo primeiro "
					 + "(verb Rank Duty).");
			return false;
		}

		if (Cargos.OqueFalta(r, Ficha(pl)) is { } falta)
		{
			Avisar(pl, $"{r.Nome} exige: {falta}.");
			return false;
		}

		if (!conceder) return true;

		if (subindo) _tronos.Remove(VagaDaConta(pl.Conta));
		_tronos[vaga] = pl.Conta;
		SalvarCargos();

		// (2026-10-19) A CAMPANHA: `if(key == "president") M.zenni -= RQ_PRESIDENT_CAMPAIGN //a campanha cobra ao VENCER`
		// (`RankQuests.dm:405`). O requisito so conferia o bolso; quem cobrava era o fim da prova, que nao existia -- e o
		// Presidente saia de graca. A porta de cima ja garantiu que o dinheiro esta la.
		if (r.Campanha > 0 && !ProvaDoCargo.CampanhaDeGracaDeTeste)
		{
			pl.Ficha.Zeni -= r.Campanha;
			MandarCarteira(pl);
			Avisar(pl, $"a campanha custou {r.Campanha:N0} zeni.");
		}

		GD.Print($"[server] {pl.Name} ({pl.Conta}) assumiu o cargo '{r.Nome}'"
				 + (subindo ? $" (largou {atual})" : ""));

		// ============================ O KIT CHEGA AGORA, E NAO NO TIQUE SEGUINTE ============================
		// **ESTA LINHA FALTAVA, e quem a encomendou foi a bancada de producao** (`--cargovivo`,
		// `GameServer.CargoVivoTeste.cs:21`): *"`ReivindicarCargo` e `Outorgar` NAO chamam
		// `ReconciliarDadiva`"*. As duas escreviam o trono e paravam -- quem reivindicava um cargo
		// recebia o kit **de zero a um segundo depois**, quando o `TickDosCargos` passasse.
		//
		// A reconciliacao ja era idempotente e o tique ja a chamava, entao a janela nunca virou bug de
		// jogo permanente -- ela virava a pior forma de latencia que existe: o mundo anunciava
		// "VOCE E O NOVO EREMITA TARTARUGA", o jogador abria a aba de skills e o Kamehameha nao estava
		// la. Um segundo depois estava. Nao ha como distinguir isso de "o jogo esta quebrado".
		//
		// **ANTES do `AnunciarCargo`**, e a ordem e o ponto: o anuncio manda `MandarCargos` pra todo
		// mundo, e a reconciliacao manda `MandarSkills` pro dono. Nesta ordem, o pacote de skills chega
		// ANTES do "voce e o novo X" -- o jogador le o titulo com o kit ja na mao.
		// =================================================================================================
		ReconciliarDadiva(pl);

		AnunciarCargo(r, pl, subindo ? atual : "");
		AparaOPovoDoCargo(r.Chave);   // (2026-10-16) cada Kaioshin jogador tira um Supremo Kaioh NPC do Ceu
		return true;
	}

	/// <summary>
	/// PoE ALGUEM NO TRONO IGNORANDO OS REQUISITOS. E o `Give (Rank)` do original, e a unica
	/// porta de entrada que a reivindicacao nao cobre.
	///
	/// Existe porque cargo, hoje, so se consegue cumprindo requisito -- e isso deixa sem conserto
	/// o caso em que ele TRAVOU: o dono abandonou o jogo, ou o requisito mudou depois de alguem ja
	/// ter assumido. Sem admin, o trono fica preso pra sempre.
	///
	/// O QUE ELE NAO IGNORA: "uma alma, um trono" continua valendo dos dois lados -- quem recebe
	/// larga o que tinha, e quem estava no trono sai dele.
	/// </summary>
	private void Outorgar(RankDef r, ServerPlayer pl)
	{
		string vagaVelha = VagaDaConta(pl.Conta);
		string largou = CargoDaVaga(vagaVelha);
		if (vagaVelha.Length > 0) _tronos.Remove(vagaVelha);

		// (2026-10-16) A VAGA: a livre, se o cargo tiver (o Kaioshin, `RankDef.Vagas`); com todas ocupadas, a PRIMEIRA troca de dono.
		string vaga = VagaLivre(r) ?? r.Chave;

		// O EX-DONO E GUARDADO PRA DEPOIS DO `_tronos[...] =`: a reconciliacao dele so responde certo
		// com o trono JA trocado, senao ela leria o cargo velho e devolveria o mesmo kit.
		string perdeu = "";
		if (_tronos.TryGetValue(vaga, out string? antigo) && antigo.Length > 0
			&& !string.Equals(antigo, pl.Conta, StringComparison.OrdinalIgnoreCase))
		{
			perdeu = antigo;
			foreach (ServerPlayer o in _players.Values)
				if (string.Equals(o.Conta, antigo, StringComparison.OrdinalIgnoreCase))
					Avisar(o, $"voce nao e mais {r.Nome}.");
		}

		_tronos[vaga] = pl.Conta;
		SalvarCargos();
		GD.Print($"[server] {pl.Name} ({pl.Conta}) recebeu o cargo '{r.Nome}' por outorga de admin");

		// A MESMA JANELA DE 1 s DO `ReivindicarCargo`, fechada dos DOIS lados -- ver o bloco de la. O
		// `Entronizar` (`GameServer.CargoPortas.cs:59-66`) ja fazia exatamente isto para as portas
		// novas, com o argumento escrito: *"sem esta linha ele so o perderia no proximo tique de
		// reconciliacao -- e num duelo pelo titulo isso e uma janela em que dois 'Deuses' existem"*. A
		// outorga de admin tinha a mesma janela e nao tinha a linha.
		if (perdeu.Length > 0 && OnlinePorConta(perdeu) is { } velho) ReconciliarDadiva(velho);
		ReconciliarDadiva(pl);

		AnunciarCargo(r, pl, largou);
		AparaOPovoDoCargo(r.Chave);
	}

	/// <summary>
	/// O MUNDO INTEIRO FICA SABENDO. Cargo e posicao social: se ninguem soube, nao houve.
	/// Um lugar so porque a reivindicacao e a outorga precisam anunciar igual.
	/// </summary>
	private void AnunciarCargo(RankDef r, ServerPlayer pl, string largou)
	{
		foreach (ServerPlayer o in _players.Values)
		{
			// `falante: null` -- e o servidor anunciando pro mundo inteiro, e nao alguem falando
			// perto de voce: ninguem fica mais familiar por ter lido a outorga de um cargo.
			Mandar(o, Protocol.Fala.Sistema, "",
				o == pl ? $"VOCE E O NOVO {r.Nome.ToUpperInvariant()}."
						: $"{pl.Name} assumiu o cargo de {r.Nome}.",
				falante: null);

			// E A LISTA VAI JUNTO. O cliente so pede a lista de cargos quando ela esta VAZIA -- ou
			// seja, quem ja tinha aberto a aba continuaria lendo o dono antigo ate relogar. A
			// reivindicacao escapava disso por acidente (o handler do pedido responde logo depois);
			// a outorga de admin nao tinha ninguem pra responder por ela.
			MandarCargos(o);
		}
		if (largou.Length > 0) Avisar(pl, $"voce largou {Cargos.Get(largou)?.Nome ?? largou}.");
	}

	/// <summary>A lista de cargos e quem os ocupa -- e o que a aba mostra.</summary>
	private void MandarCargos(ServerPlayer pl)
	{
		var w = Protocol.Begin(Protocol.S2C.Cargos);
		w.Put((byte)Cargos.Todos.Length);
		FichaDeRank f = Ficha(pl);
		foreach (RankDef r in Cargos.Todos)
		{
			w.Put(r.Chave);
			// (2026-10-16) OS DONOS, separados por virgula: o cargo de varias vagas tem ate cinco (ver `RankDef.Vagas`)
			List<string> donos = DonosDe(r);
			w.Put(string.Join(", ", donos.Select(NomeDoDono)));
			// o que falta PRA MIM: e a unica parte que muda de jogador pra jogador
			// (2026-10-19) ...ou que o espirito dele esta sendo enfrentado agora -- e o botao "Reivindicar" some (`ProvaQueOcupa`)
			w.Put(Cargos.OqueFalta(r, f) ?? ProvaQueOcupa(r, pl) ?? "");

			// ============================ E O QUE O CARGO DA, QUE NUNCA SAIU DAQUI ============================
			// O painel mandava chave, dono e o que falta -- e mais nada. **Ninguem no jogo conseguia
			// descobrir o que um cargo entrega antes de toma-lo**, e a informacao existia em DOIS
			// lugares: a `RankDef.Desc` (texto, escrita pra ser lida) e a `DadivaDeCargo` (a tabela de
			// verdade, a que o `ReconciliarDadiva` executa).
			//
			// Sao os dois campos, e nao um: a `Desc` diz o que o cargo E ("a escola que forma
			// protetores"), a dadiva diz o que ele DA. Um sem o outro deixa metade da escolha no escuro.
			// ==========================================================================================
			w.Put(r.Desc);
			w.Put(OQueOCargoEntrega(r.Chave));

			// (2026-10-16) AS VAGAS E QUANTAS ESTAO LIVRES, no FIM do cargo: e o que deixa a aba oferecer "Reivindicar" num cargo que
			// ja tem dono (o Kaioshin com dois de cinco) -- o "dono vazio = vago" de antes so sabia responder pra uma vaga.
			int vagas = VagasDe(r).Count();
			w.Put((byte)vagas);
			w.Put((byte)Math.Max(0, vagas - donos.Count));
		}

		// A BANCADA ESCUTA AQUI, e o lugar e o ponto todo: e ESTE writer que vira os bytes do fio.
		// Uma bancada que chamasse `OQueOCargoEntrega` direto leria a TABELA -- e a pergunta do dono
		// ("o painel ainda anuncia o Mafuba?") e sobre o que CHEGA no cliente, que passa por um
		// `Put`/`GetString` com limite de tamanho no meio. Nulo em jogo. Ver `GameServer.SeloTeste.cs`.
		EscutaDeCargos?.Add((pl.Id, w));
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// ============================ O QUE ESTE CARGO ENTREGA, EM UMA LINHA HONESTA ============================
	/// A lista sai da <see cref="DadivaDeCargo"/> -- **a mesma tabela que o `ReconciliarDadiva`
	/// executa de verdade** --, e nao da `RankDef.Concede`, que e texto escrito a mao descrevendo o
	/// DM. A diferenca nao e estilo: `Concede` promete pelo original (o Guardiao "também cria
	/// Esferas"), e a dadiva diz o que ESTE servidor poe no livro. Uma tela alimentada pela promessa
	/// mentiria; alimentada pela tabela, ela acompanha sozinha o dia em que a tabela mudar.
	///
	/// **E ELA SEPARA O QUE FUNCIONA DO QUE E BOTAO MUDO**, pelo mesmo censo que o
	/// <see cref="ContarOQueOCargoDeu"/> ja usa (`CensoDeSkills.SistemaQueFalta`). Esconder a metade
	/// muda faria o painel prometer uma Genkidama que nao dispara -- e mostrar tudo junto, sem marcar,
	/// faria o jogador chamar de defeito o que e divida declarada. No dia em que a Genkidama for
	/// portada, ela troca de lado sozinha.
	/// ====================================================================================================
	/// </summary>
	private string OQueOCargoEntrega(string chave)
	{
		string[] kit = DadivaDeCargo.De(chave);
		if (_skills == null || kit.Length == 0) return "";

		List<string> prontas = [], mudas = [];
		foreach (string path in kit)
		{
			Skill? s = _skills.Get(path);
			if (s == null || s.Nome.Length == 0) continue;
			(CensoDeSkills.SistemaQueFalta(s) == null ? prontas : mudas).Add(s.Nome);
		}

		string texto = prontas.Count > 0 ? string.Join(", ", prontas) : "";
		// (2026-10-23) O RITUAL DO KAIOSHIN NAO E SKILL DE LIVRO (e do trono, como a travessia do juiz), e e o que o kit ganhou no lugar
		// do Mistico (o dono): entra na linha pelo nome, na frente -- e quem escolhe o cargo descobre o ritual aqui.
		if (string.Equals(chave, RitualDoKaioshin.Cargo, StringComparison.OrdinalIgnoreCase))
			texto = RitualDoKaioshin.Nome + (texto.Length > 0 ? ", " + texto : "");
		if (mudas.Count > 0)
			texto += (texto.Length > 0 ? " | " : "") + "ainda mudo neste servidor: " + string.Join(", ", mudas);
		return texto;
	}

	/// <summary>
	/// O NOME de quem ocupa, e nao a conta. A conta e a chave interna; mostrar "roboA" onde
	/// deveria estar "Goku" entrega o login de quem joga.
	/// </summary>
	private string NomeDoDono(string? conta)
	{
		if (string.IsNullOrEmpty(conta)) return "";
		ServerPlayer? vivo = _players.Values.FirstOrDefault(
			p => string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase));
		if (vivo != null) return vivo.Name;

		// offline: nao vamos abrir save pra desenhar uma lista. A conta serve de rotulo ate
		// a pessoa entrar -- e cargo e de conta, nao de personagem.
		return conta;
	}
}
