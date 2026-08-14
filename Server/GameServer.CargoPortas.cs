using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DOS CARGOS QUE NAO SE REIVINDICAM -- nomeacao, sucessao, e o que o cargo ENTREGA.
///
/// ============================ O BURACO QUE ESTE ARQUIVO FECHA ============================
/// O `GameServer.Ranks.cs` sabia fazer duas coisas: reivindicar (com requisito) e outorgar (admin).
/// Os outros 14 cargos da tabela declaravam a porta -- `Duelo`, `Sucessao`, `Nomeacao` -- e o
/// `OqueFalta` respondia com a frase honesta *"este cargo nao se reivindica"* e mais nada. Nenhum
/// deles tinha como trocar de dono no jogo.
///
/// Aqui entram DUAS das tres portas (o duelo mora no `GameServer.CargoDuelo.cs`, que e uma maquina
/// de estado com prazo) e a coisa que faltava pros trinta: a DADIVA -- as skills que o cargo
/// concede, que ate agora eram uma lista de texto sem nenhum leitor.
/// ======================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// 0. AS DUAS OPERACOES QUE TODA PORTA USA
	// =====================================================================

	/// <summary>Quem, entre os que estao no mundo agora, e o dono desta conta ("" = ninguem).</summary>
	private ServerPlayer? OnlinePorConta(string conta) =>
		conta.Length == 0
			? null
			: _players.Values.FirstOrDefault(
				p => EhPessoa(p) && string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// POE ALGUEM NO TRONO POR UMA PORTA QUE NAO E O REQUISITO.
	///
	/// Irmao do <see cref="Outorgar"/> e com as mesmas duas invariantes ("uma alma, um trono" dos
	/// dois lados; o mundo fica sabendo), mas com uma diferenca que so as portas novas precisam: o
	/// motivo (`via`) e ANUNCIADO ANTES da coroacao, como no DM -- la sao dois `bev_announce`
	/// separados (`GodOfDestruction.dm:508` e `:104`), porque "derrotou o Deus em duelo" e "e o novo
	/// Deus" sao dois fatos e o segundo sem o primeiro parece um presente.
	/// </summary>
	private void Entronizar(RankDef r, ServerPlayer pl, string via)
	{
		if (via.Length > 0)
			foreach (ServerPlayer o in _players.Values)
				Mandar(o, Protocol.Fala.Sistema, "", via, falante: null);

		string antigo = _tronos.TryGetValue(r.Chave, out string? d) ? d : "";
		string largou = CargoDe(pl.Conta);
		if (largou.Length > 0) _tronos.Remove(largou);

		_tronos[r.Chave] = pl.Conta;
		SalvarCargos();

		// O EX-DONO PERDE O KIT NA HORA. Sem esta linha ele so o perderia no proximo tique de
		// reconciliacao -- e num duelo pelo titulo isso e uma janela em que dois "Deuses" existem.
		if (antigo.Length > 0 && !string.Equals(antigo, pl.Conta, StringComparison.OrdinalIgnoreCase)
			&& OnlinePorConta(antigo) is { } velho)
		{
			Avisar(velho, $"voce nao e mais {r.Nome}.");
			ReconciliarDadiva(velho);
		}

		ReconciliarDadiva(pl);
		GD.Print($"[server] {pl.Name} ({pl.Conta}) assumiu '{r.Nome}' -- {(via.Length > 0 ? via : "porta especial")}");
		AnunciarCargo(r, pl, largou);
	}

	/// <summary>
	/// TIRA O CARGO DE QUEM O CARREGA. **A outra metade do sistema**, e a que faltava inteira.
	///
	/// Ganhar cargo tinha tres caminhos e perder nao tinha nenhum: nem o admin conseguia esvaziar um
	/// trono. Isso importa mais do que parece -- destituicao e o unico mecanismo que faz um cargo com
	/// DEVERES significar alguma coisa, e e por onde a covardia do Deus da Destruicao e a negligencia
	/// dele cobram o preco (`god_lose_title`, `GodOfDestruction.dm:107`).
	///
	/// Devolve `false` quando o trono ja estava vago -- quem chama decide se isso e erro ou rotina.
	/// </summary>
	private bool Destronar(string chave, string motivo)
	{
		RankDef? r = Cargos.Get(chave);
		if (r == null) return false;
		if (!_tronos.TryGetValue(r.Chave, out string? conta) || conta.Length == 0) return false;

		_tronos.Remove(r.Chave);
		SalvarCargos();

		// O TITULO EM DISPUTA TEM RELOGIO PROPRIO (carencia, adiamentos, tarefas), e ele nao pode
		// sobreviver ao dono: sem esta linha, o proximo Deus nasceria com as falhas do anterior.
		if (string.Equals(r.Chave, CargoEmDisputa, StringComparison.OrdinalIgnoreCase))
			LimparEstadoDoTitulo();

		if (OnlinePorConta(conta) is { } ex)
		{
			Avisar(ex, $"voce foi DESTITUIDO de {r.Nome}. ({motivo}) Todo o poder do cargo se esvai.");
			ReconciliarDadiva(ex);
		}

		GD.Print($"[server] '{r.Nome}' vagou: {motivo} (era de {conta})");
		foreach (ServerPlayer o in _players.Values)
		{
			Mandar(o, Protocol.Fala.Sistema, "",
				$"{NomeDoDono(conta)} perdeu o cargo de {r.Nome}. ({motivo}) O trono esta VAGO.", falante: null);
			MandarCargos(o);
		}
		return true;
	}

	// =====================================================================
	// 1. A DADIVA -- o que o cargo entrega, e o que ele leva de volta
	// =====================================================================

	/// <summary>
	/// PoE O LIVRO DE SKILLS DE ACORDO COM O CARGO QUE ESTA ALMA CARREGA AGORA.
	///
	/// ============================ POR QUE RECONCILIAR EM VEZ DE DAR/TIRAR NOS EVENTOS ============================
	/// Um cargo troca de dono por SEIS caminhos hoje (reivindicacao, outorga de admin, nomeacao,
	/// sucessao, duelo, destituicao) e vai trocar por mais quando as quests de cargo chegarem.
	/// Pendurar "da o kit" em cada um deles seria criar seis lugares onde a proxima pessoa liga um e
	/// esquece o outro -- que e, literalmente, o modo de falha mais repetido deste port (o mesmo
	/// argumento que o `AoPerderALuta` ja escreveu pros quatro pontos da derrota).
	///
	/// E tem um caminho que evento NENHUM cobre: **perder o cargo estando OFFLINE**. Quem foi
	/// destituido dormindo voltaria com o kit inteiro pra sempre.
	///
	/// Entao a funcao e IDEMPOTENTE e derivada: ela olha o cargo de agora e o livro de agora, e
	/// conserta a diferenca. Chamada no login, no tique e em toda troca de trono.
	/// ========================================================================================================
	/// </summary>
	/// <summary>
	/// O QUE SAI JUNTO COM UM CARGO, levantado UMA VEZ do catalogo. Ver
	/// <see cref="DadivaDeCargo.LevantarRevogaveis"/> -- a conta varre as 47 arvores e a resposta
	/// nao muda enquanto o servidor viver, entao faze-la no tique seria queimar trabalho de graca.
	/// </summary>
	private IReadOnlySet<string> SoDeCargo =>
		_soDeCargo ??= _skills == null
			? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			: DadivaDeCargo.LevantarRevogaveis(_skills);

	private HashSet<string>? _soDeCargo;

	private void ReconciliarDadiva(ServerPlayer pl)
	{
		if (!EhPessoa(pl) || pl.Livro == null) return;

		string[] kit = DadivaDeCargo.De(CargoDe(pl.Conta));
		bool mudou = false;

		// 1. O QUE SOBROU DE UM CARGO QUE ELE NAO TEM MAIS -- o que SO um cargo poderia ter dado, e
		//    que ninguem lhe ensinou. Ver `DadivaDeCargo.Revogavel` e o `treeshrink` do DM.
		foreach (string path in DadivaDeCargo.Todas)
		{
			if (kit.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
			if (!pl.Livro.Sabe(path)) continue;
			if (!DadivaDeCargo.Revogavel(SoDeCargo, pl.Livro, path)) continue;
			pl.Livro.Esquecer(path);
			mudou = true;
		}

		// 2. O QUE O CARGO DE AGORA DA.
		List<string>? chegaram = null;
		foreach (string path in kit)
		{
			if (pl.Livro.Sabe(path)) continue;

			// TYPEPATH QUE O CATALOGO NAO CONHECE NAO ENTRA. Uma skill guardada no livro sem entrada
			// no catalogo e invisivel e incontavel -- viraria peso morto no save de quem foi Kaio.
			if (_skills?.Get(path) == null) continue;
			pl.Livro.Dar(path);
			(chegaram ??= []).Add(path);
			mudou = true;
		}

		if (chegaram != null) ContarOQueOCargoDeu(pl, chegaram);

		if (!mudou) return;
		AplicarPoderes(pl);
		AplicarEfeitos(pl);

		// ESCREVER O CORTE NAO E APLICAR O CORTE. Sete das skills que saem daqui sao ESTILOS, e o
		// estilo vestido nao mora no livro: mora no `Ficha.EstiloAtual`, e os dez multiplicadores
		// dele ja estao escritos na ficha. Sem esta linha o ex-Eremita Tartaruga perde o
		// `KameStyle` do livro e continua lutando com os numeros dele ate trocar de postura --
		// que e exatamente o defeito que o sigilo de BP ja custou a este projeto uma vez.
		AplicarEstilo(pl);
		MandarEstilos(pl);

		MandarSkills(pl, forcar: true);
	}

	/// <summary>
	/// O CARGO DIZ O QUE DEU -- E DIZ O QUE ELE AINDA NAO FAZ.
	///
	/// ============================ POR QUE ISTO NAO E ENFEITE ============================
	/// Das 51 skills que os cargos entregam, **31 nao tem corpo neste port** -- e o menu do cliente
	/// e desenhado a partir do `Tecnicas`, onde verbo mudo nao entra. O resultado, ate aqui, era o
	/// pior formato possivel de divida: o mundo anunciava o novo Kaio do Norte, o livro recebia a
	/// Genkidama, e no lado do jogador **nao acontecia nada**. Nenhum botao, nenhuma recusa, nenhuma
	/// mensagem. Ele nao tinha como distinguir "o jogo esta quebrado" de "isto ainda nao existe".
	///
	/// A regra 5 da casa manda falhar ALTO em vez de calar, e a regra 3 desta camada manda que o que
	/// depende de sistema inexistente fique DECLARADO e nao morra em silencio. O catalogo ja sabe a
	/// resposta pra cada verbo (`CensoDeSkills.SistemaQueFalta`, a mesma tabela que a bancada usa) --
	/// o que faltava era ela chegar em quem esta jogando.
	///
	/// **A LISTA SAI DO CENSO E NAO DE UMA SEGUNDA TABELA**, e isso e o ponto: no dia em que a
	/// Genkidama for portada, esta mensagem para de mencionar a Genkidama sozinha. Uma lista escrita
	/// a mao aqui continuaria pedindo desculpa por uma tecnica que ja funciona.
	/// ==================================================================================
	/// </summary>
	private void ContarOQueOCargoDeu(ServerPlayer pl, List<string> chegaram)
	{
		if (_skills == null) return;

		List<string> prontas = [], faltando = [];
		var sistemas = new List<string>();
		foreach (string path in chegaram)
		{
			Skill? s = _skills.Get(path);
			if (s == null || s.Nome.Length == 0) continue;

			string? falta = CensoDeSkills.SistemaQueFalta(s);
			if (falta == null) { prontas.Add(s.Nome); continue; }
			faltando.Add(s.Nome);
			if (!sistemas.Contains(falta, StringComparer.OrdinalIgnoreCase)) sistemas.Add(falta);
		}

		if (prontas.Count > 0) Avisar(pl, $"o cargo te entrega: {string.Join(", ", prontas)}.");

		// O RECADO HONESTO. Ele nomeia o SISTEMA e nao pede desculpa generica -- "falta a barragem
		// sustentada" e uma informacao; "em breve" nao e.
		if (faltando.Count > 0)
			Avisar(pl, $"o cargo tambem te dá {string.Join(", ", faltando)} -- mas isso ainda não "
					 + $"funciona neste servidor: falta {string.Join("; ", sistemas)}.");
	}

	/// <summary>
	/// O TIQUE DOS CARGOS -- 1 Hz, junto do resto do bloco lento.
	///
	/// Ele existe pra UMA coisa: a reconciliacao acima nao ter que ser lembrada por quem escrever a
	/// setima porta de cargo. Custa um `CargoDe` por pessoa por segundo (uma varredura de um
	/// dicionario de no maximo 30 entradas) e devolve a garantia de que kit e trono nunca divergem
	/// por mais de um segundo -- inclusive quando quem mexeu no trono foi outro sistema.
	/// </summary>
	private void TickDosCargos()
	{
		foreach (ServerPlayer pl in _players.Values)
			if (EhJogador(pl)) ReconciliarDadiva(pl);

		TickDoDuelo();

		// OS DEVERES DOS 17 CARGOS COM TAREFA. Ele entra AQUI e nao no bloco de 1 Hz de fora pelo
		// mesmo motivo do duelo: quem manda no passo e o relogio do MUNDO, e este metodo so pergunta
		// se ja deu a hora (uma volta por minuto -- ver `TickDasMissoes`). Ver `GameServer.CargoMissoes.cs`.
		TickDasMissoes();
	}

	// =====================================================================
	// 2. NOMEACAO -- os quatro Anciaos de Namek
	// =====================================================================

	/// <summary>
	/// Quem foi convidado a ser Anciao: conta do convidado -> (assento, conta de quem nomeou).
	///
	/// NA MEMORIA e nao em disco, como o pedido de amizade: um convite pendente nao e patrimonio, e
	/// um servidor que reiniciou nao tem como o Grande Anciao continuar esperando a resposta.
	/// </summary>
	private readonly Dictionary<string, (string Assento, string Nomeador)> _convitesDeAnciao =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// `Appoint_Elder` (`ordered/NamekRanks.dm:56`), com as duas correcoes que o Core documenta
	/// (a chave e a CONTA; o alvo precisa de sangue de Namek e nao de ja ser Anciao).
	///
	/// O ALVO VEM MARCADO (duplo clique), como todo verb com alvo neste port -- e o equivalente
	/// honesto do `in view(1)` do BYOND. O assento e opcional: sem ele, o primeiro vago.
	/// </summary>
	private void VerboNomearAnciao(ServerPlayer pl, string arg)
	{
		string[] p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		ServerPlayer? alvo = PorNome(p.Length > 0 ? p[0] : "");
		if (alvo == null || alvo == pl || !EhPessoa(alvo))
		{
			Avisar(pl, "marque o Namekuseijin que voce quer nomear (duplo clique nele).");
			return;
		}

		string assento = p.Length > 1 ? AssentoPorApelido(p[1]) : PrimeiroAssentoVago();
		if (assento.Length == 0)
		{
			Avisar(pl, "os quatro assentos de Anciao tem dono. Diga qual deles voce quer trocar "
					 + "(norte, sul, leste ou oeste).");
			return;
		}

		string? nao = Nomeacao.PorQueNao(CargoDe(pl.Conta), assento, alvo.Race,
										 alvo.Ficha.Genoma?.MajorityRace ?? alvo.Race, CargoDe(alvo.Conta));
		if (nao != null) { Avisar(pl, $"nao da: {nao}."); return; }

		_convitesDeAnciao[alvo.Conta] = (assento, pl.Conta);
		Avisar(pl, $"voce oferece a {alvo.Name} o assento de {Cargos.Get(assento)?.Nome ?? assento}.");
		Avisar(alvo, $"{pl.Name}, o Grande Anciao de Namek, te oferece o assento de "
				   + $"{Cargos.Get(assento)?.Nome ?? assento}. (aceite ou recuse na aba Other)");
	}

	/// <summary>
	/// A RESPOSTA DO CONVIDADO -- o segundo `alert()` do DM (`NamekRanks.dm:60`), que ali era
	/// bloqueante e aqui e um verb, como o pedido de amizade.
	///
	/// OS REQUISITOS SAO CONFERIDOS DE NOVO. Entre o convite e a resposta o Grande Anciao pode ter
	/// perdido o cargo, e o convidado pode ter assumido outro -- e aceitar um convite velho nao pode
	/// abrir uma porta que o mundo ja fechou.
	/// </summary>
	private void VerboResponderNomeacao(ServerPlayer pl, bool aceitou)
	{
		if (!_convitesDeAnciao.TryGetValue(pl.Conta, out (string Assento, string Nomeador) c))
		{
			Avisar(pl, "ninguem te ofereceu um assento de Anciao.");
			return;
		}
		_convitesDeAnciao.Remove(pl.Conta);

		if (!aceitou)
		{
			Avisar(pl, "voce recusa o assento.");
			if (OnlinePorConta(c.Nomeador) is { } n) Avisar(n, $"{pl.Name} recusou o assento.");
			return;
		}

		string? nao = Nomeacao.PorQueNao(CargoDe(c.Nomeador), c.Assento, pl.Race,
										 pl.Ficha.Genoma?.MajorityRace ?? pl.Race, CargoDe(pl.Conta));
		if (nao != null) { Avisar(pl, $"o assento se fechou: {nao}."); return; }

		RankDef? r = Cargos.Get(c.Assento);
		if (r == null) return;
		Entronizar(r, pl, $"O Grande Anciao de Namek nomeia {pl.Name} {r.Nome}.");
	}

	private static string AssentoPorApelido(string s) => s.ToLowerInvariant() switch
	{
		"norte" or "north" or "n" => "northelder",
		"sul" or "south" or "s" => "southelder",
		"leste" or "east" or "l" or "e" => "eastelder",
		"oeste" or "west" or "o" or "w" => "westelder",
		_ => Nomeacao.EhAssento(s) ? s : "",
	};

	private string PrimeiroAssentoVago() =>
		Array.Find(Nomeacao.Assentos,
			a => !_tronos.TryGetValue(a, out string? d) || d.Length == 0) ?? "";

	// =====================================================================
	// 3. SUCESSAO -- o trono passa a quem mata
	// =====================================================================

	/// <summary>
	/// A LINHA DE SANGUE DE VEGETA: contas nomeadas herdeiras pelo Rei, na ordem em que ele as
	/// nomeou. E o `vegeta_royalty` do DM (`RankAssign.dm:255`), preenchido pelo `Vegeta Managament`
	/// (`rankSkills/SpaceRankSkills.dm:54-88`).
	///
	/// EM DISCO, ao lado do `cargos.txt`: quem e Principe e um fato do MUNDO e nao da sessao -- e sem
	/// persistir, a unica coisa que a sucessao faria e vagar o trono depois de um reinicio.
	/// </summary>
	private readonly List<string> _herdeirosDeVegeta = [];

	/// <summary>Pelo `_store` e nao pelo caminho cravado -- ver o porque em `GameServer.Ranks.cs`.</summary>
	private string ArquivoDeHerdeiros =>
		System.IO.Path.Combine(_store?.Pasta ?? ProjectSettings.GlobalizePath("user://saves"), "herdeiros.txt");

	private void CarregarHerdeiros()
	{
		_herdeirosDeVegeta.Clear();
		if (!System.IO.File.Exists(ArquivoDeHerdeiros)) return;
		foreach (string linha in System.IO.File.ReadAllLines(ArquivoDeHerdeiros))
			if (linha.Trim().Length > 0) _herdeirosDeVegeta.Add(linha.Trim());
	}

	private void SalvarHerdeiros()
	{
		try { System.IO.File.WriteAllLines(ArquivoDeHerdeiros, _herdeirosDeVegeta); }
		catch (Exception e) { GD.PushWarning($"[server] nao gravei os herdeiros: {e.Message}"); }
	}

	/// <summary>O Rei nomeia (ou dispensa) um herdeiro. So o Rei, e so sobre quem esta marcado.</summary>
	private void VerboHerdeiro(ServerPlayer pl, string arg, bool nomear)
	{
		if (!string.Equals(CargoDe(pl.Conta), Sucessao.ReiDeVegeta, StringComparison.OrdinalIgnoreCase))
		{
			Avisar(pl, "so o Rei de Vegeta decide quem herda o trono.");
			return;
		}
		ServerPlayer? alvo = PorNome(arg);
		if (alvo == null || alvo == pl || !EhPessoa(alvo))
		{
			Avisar(pl, "marque alguem antes (duplo clique nele).");
			return;
		}

		if (!nomear)
		{
			if (_herdeirosDeVegeta.RemoveAll(c => string.Equals(c, alvo.Conta, StringComparison.OrdinalIgnoreCase)) == 0)
			{
				Avisar(pl, $"{alvo.Name} nao era da linha de sucessao.");
				return;
			}
			SalvarHerdeiros();
			Avisar(pl, $"{alvo.Name} sai da linha de sucessao de Vegeta.");
			Avisar(alvo, "o Rei te retirou da linha de sucessao.");
			return;
		}

		// O REQUISITO E O DA HERANCA, e ele e conferido AQUI e nao so na hora da morte: nomear um
		// Humano Principe de Vegeta cria uma promessa que o `Murder.dm:91` nunca cumpriria.
		if (!Sucessao.HerdeiroServe(alvo.Race, alvo.Ficha.Genoma?.MajorityRace ?? alvo.Race, alvo.Ficha.dead))
		{
			Avisar(pl, "so um Saiyajin vivo entra na linha de sucessao de Vegeta.");
			return;
		}
		if (_herdeirosDeVegeta.Contains(alvo.Conta, StringComparer.OrdinalIgnoreCase))
		{
			Avisar(pl, $"{alvo.Name} ja e da linha de sucessao.");
			return;
		}

		_herdeirosDeVegeta.Add(alvo.Conta);
		SalvarHerdeiros();
		Avisar(pl, $"{alvo.Name} entra na linha de sucessao do trono de Vegeta.");
		Avisar(alvo, $"{pl.Name} te nomeia herdeiro do trono de Vegeta.");
	}

	/// <summary>
	/// MORREU CARREGANDO UM TRONO. Chamado do <see cref="AoPerderALuta"/> com `morreu: true` -- o
	/// funil unico das consequencias de derrota, que ja documenta que a proxima consequencia entra
	/// ali e chega nos quatro pontos de graca.
	///
	/// A ORDEM E A DO `killer_stuff` (`Murder.dm:85-101`): quem mata leva, se for do sangue; nao
	/// sendo, o herdeiro; nao havendo, o trono VAGA.
	/// </summary>
	private void SucessaoPorMorte(ServerPlayer vitima, ServerPlayer algoz)
	{
		if (!EhPessoa(vitima) || !EhPessoa(algoz)) return;
		if (string.Equals(vitima.Conta, algoz.Conta, StringComparison.OrdinalIgnoreCase)) return;

		string cargo = CargoDe(vitima.Conta);
		if (cargo.Length == 0 || !Sucessao.TemSucessao(cargo)) return;
		RankDef? r = Cargos.Get(cargo);
		if (r == null) return;

		// 1. O ALGOZ E DO SANGUE? Toma o trono na hora.
		if (Sucessao.AlgozHerda(cargo, algoz.Race, algoz.Ficha.Genoma?.MajorityRace ?? algoz.Race, algoz.Class))
		{
			Entronizar(r, algoz, $"{algoz.Name} matou {vitima.Name} e tomou o trono de {r.Nome}.");
			return;
		}

		// 2. A LINHA DE SANGUE. So o trono de Vegeta tem uma.
		if (Sucessao.TemHerdeiros(cargo))
			foreach (string conta in _herdeirosDeVegeta)
			{
				ServerPlayer? h = OnlinePorConta(conta);
				if (h == null) continue;   // herdeiro offline nao assume: o DM tambem varre mobs no mundo
				if (!Sucessao.HerdeiroServe(h.Race, h.Ficha.Genoma?.MajorityRace ?? h.Race, h.Ficha.dead)) continue;
				Entronizar(r, h, $"{r.Nome} foi assassinado. {h.Name} herda o trono por direito de sangue.");
				return;
			}

		// 3. NINGUEM: o trono VAGA (`else King_of_Vegeta=null`).
		Destronar(cargo, $"o portador foi morto por {algoz.Name} e nao havia herdeiro");
	}

	// =====================================================================
	// 4. O CANAL DOS CARGOS (RankChat) e o roteador dos verbs
	// =====================================================================

	/// <summary>
	/// `RankChat` (`RankTree.dm:14-31`) -- a unica skill que TODO cargo tem no original.
	///
	/// Ela entra aqui porque e a peca que faz a dadiva parar de ser promessa no primeiro minuto: a
	/// skill que os trinta cargos concedem passa a ter corpo, e nao um botao mudo. Alcance: todo
	/// mundo que carrega um cargo, onde quer que esteja -- e o ponto do canal.
	/// </summary>
	private void VerboFalarAosCargos(ServerPlayer pl, string texto)
	{
		if (CargoDe(pl.Conta).Length == 0) { Avisar(pl, "esse canal e de quem carrega um cargo."); return; }
		if (texto.Trim().Length == 0) { Avisar(pl, "escreva a mensagem."); return; }

		string meu = Cargos.Get(CargoDe(pl.Conta))?.Nome ?? "";
		int ouviram = 0;
		foreach (ServerPlayer o in _players.Values)
		{
			if (o.Peer == null || !EhPessoa(o) || CargoDe(o.Conta).Length == 0) continue;
			Mandar(o, Protocol.Fala.Sistema, "", $"[cargos] {pl.Name} ({meu}): {texto}", falante: null);
			ouviram++;
		}
		if (ouviram <= 1) Avisar(pl, "ninguem mais com cargo esta no mundo agora.");
	}

	/// <summary>
	/// Os verbs das portas. Devolve `true` quando o comando era daqui -- inclusive quando foi
	/// RECUSADO, pela mesma razao do `ComandoDeConvivio`: senao o funil segue e o jogador le
	/// "comando desconhecido" depois de ja ter lido o motivo de verdade.
	/// </summary>
	private bool ComandoDeCargo(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "cargo_nomear": VerboNomearAnciao(pl, arg); return true;
			case "cargo_nomear_aceitar": VerboResponderNomeacao(pl, aceitou: true); return true;
			case "cargo_nomear_recusar": VerboResponderNomeacao(pl, aceitou: false); return true;

			case "cargo_herdeiro": VerboHerdeiro(pl, arg, nomear: true); return true;
			case "cargo_herdeiro_tirar": VerboHerdeiro(pl, arg, nomear: false); return true;
			case "cargo_herdeiros": VerboVerHerdeiros(pl); return true;

			case "cargo_falar": VerboFalarAosCargos(pl, arg); return true;

			// os deveres moram no `GameServer.CargoMissoes.cs` -- o roteador continua sendo um so
			case "cargo_deveres": VerboMeusDeveres(pl); return true;
			case "cargo_verba": VerboDepositarVerba(pl); return true;

			// o duelo mora no arquivo vizinho, mas o roteador e um so
			case "cargo_desafiar": VerboDesafiar(pl); return true;
			case "cargo_duelo_aceitar": VerboResponderDesafio(pl, aceitou: true); return true;
			case "cargo_duelo_adiar": VerboResponderDesafio(pl, aceitou: false); return true;
			case "cargo_titulo": VerboStatusDoTitulo(pl); return true;

			default: return false;
		}
	}

	private void VerboVerHerdeiros(ServerPlayer pl)
	{
		if (_herdeirosDeVegeta.Count == 0) { Avisar(pl, "o trono de Vegeta nao tem linha de sucessao."); return; }
		Avisar(pl, "-- linha de sucessao de Vegeta --");
		int i = 1;
		foreach (string c in _herdeirosDeVegeta) Avisar(pl, $"  {i++}. {NomeDoDono(c)}");
	}
}
