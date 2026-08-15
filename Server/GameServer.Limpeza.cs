using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// A LIMPEZA TOTAL DO SERVIDOR -- o pedido do dono: *"um verb pra adm no menu P q LIMPA O SERVER
/// TODO -- saves de jogadores, de mapa, construcoes, tudo, dando uma limpa total como se tivesse
/// acabado de rodar pela primeira vez"*.
///
/// ============================ O RISCO NAO E APAGAR DEMAIS. E APAGAR DE MENOS. ============================
/// Uma limpeza que esquece UM arquivo e pior que nenhuma. O mundo volta meio povoado: contas que
/// apontam pra zonas que sumiram, sagas em fase 3 sem chefe, obras de donos que nao existem,
/// dominios cobrando tributo pra fantasmas, tronos ocupados por contas apagadas -- e o cargo nunca
/// mais vaga, porque `CargoDe()` continua devolvendo o dono morto. E o defeito **nao aparece na
/// hora**: aparece dias depois, como "comportamento estranho" que ninguem liga a limpeza.
///
/// Por isso a regra central deste arquivo: **a lista do que apagar e DERIVADA, nao escrita a mao.**
/// Ela se sustenta em tres pernas, e nenhuma delas e uma lista de nomes:
///
///   1. NO DISCO, o alvo e a PASTA (`_store.Pasta`), nao os arquivos dela. Apaga-se tudo o que esta
///      la dentro e que nenhum sistema marcou como <see cref="SistemaDoMundo.Preservar"/> -- hoje so
///      o `admin.log`. Um sistema de persistencia novo nasce dentro da pasta e some junto, sem
///      ninguem lembrar de nada. Cobre ate o `.tmp` orfao de uma escrita atomica interrompida.
///
///   2. NA MEMORIA, cada sistema se INSCREVE com um `Zerar()` (ver <see cref="RegistrarSistemasDoMundo"/>),
///      do mesmo jeito que os `Carregar*` ja se enfileiram em `CarregarVisual()`. Disco limpo com
///      cache sujo e um mundo que mente ate o proximo reinicio -- e que REGRAVA na primeira mudanca.
///
///   3. A TRAVA CONTRA O APODRECIMENTO e a bancada `--wipeteste`: ela lista a pasta e **reprova se
///      sobrou um arquivo que nenhum inscrito reivindica**. E o unico jeito de o arquivo que nascer
///      amanha ser pego por quem o escreveu, e nao por um jogador daqui a tres semanas.
///
/// A prova de que isto era necessario esta no proprio projeto: a lista a mao de
/// `AccountStore.NaoSaoContas` tinha quatro nomes e ja estava tres atras (`conquista.json`,
/// `naves.json`, `missoes-de-cargo.json` nasceram depois e ninguem voltou la). Hoje ela e preenchida
/// por este registro -- ver `CharacterStore.cs`.
/// ======================================================================================================
///
/// ============================ A ORDEM E OBRIGATORIA, E NAO E DETALHE ============================
/// O `wipe-servidor.bat` do projeto BYOND ja avisava disso em letras garrafais -- *"com o dd.exe
/// aberto ele regrava os saves ao fechar"* -- e la a solucao era RECUSAR rodar com o servidor no ar.
/// Aqui o servidor E quem roda a limpeza, entao a recusa nao serve; o que serve e a ordem:
///
///   1. <see cref="_limpezaEmCurso"/> LIGA. E o unico jeito honesto: enquanto ele esta ligado,
///      `Persistir` nao grava linha nenhuma e `Login` recusa quem chega. Sem ele, o `Drop(peer)` --
///      que chama `Persistir` ANTES de soltar -- recria cada conta na saida, e a limpeza se desfaz
///      sozinha no exato gesto de deslogar todo mundo. A bancada mede isto (ver `--wipeteste`).
///   2. Todo mundo cai (jogando E preso na tela de selecao), e as colecoes de sessao esvaziam.
///   3. `Zerar()` de cada inscrito.
///   4. So entao o disco.
///
/// Qualquer outra ordem devolve os arquivos.
/// ============================================================================================
///
/// ============================ O QUE ISTO **NAO** TOCA ============================
/// A fronteira e geografica e o disco confirma: 100% do que o servidor escreve em runtime esta em
/// `user://saves/`, e nada alem disso esta la. Fora dela ficam:
///   * `res://Assets/**` -- CONTEUDO do jogo (racas, skills, mapas, sprites). Apagar isso nao e
///     "primeiro boot", e um servidor onde todo mundo nasce com stat 1 e sem colisao.
///   * a MOBILIA DO MAPA (`Obra.DoMapa`), que nasce do `.objetos` a cada boot e por isso e conteudo
///     e nao mundo -- ver o `Zerar` das construcoes.
///   * `user://config.json` do cliente -- configuracao, e nem esta nesta maquina necessariamente.
///
/// E UMA COISA QUE SOME POR CONSTRUCAO, e por isso e dita em voz alta na previa: banimentos e mutes
/// moram DENTRO do arquivo da conta (`AccountSave.Banida/Calada`), entao apagar contas desbane e
/// descala todo mundo. O `wipe-servidor.bat` do BYOND preservava os dois; aqui preservar exigiria
/// escrever contas-carcaca, e isso e decisao do dono e nao efeito colateral. Entao a previa CONTA
/// quantos vao ser perdoados, e quem confirma sabe.
/// ===============================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// O REGISTRO
	// =====================================================================
	/// <summary>
	/// UM SISTEMA QUE TEM ESTADO DE MUNDO. E a unidade da inscricao: o que ele escreve no disco,
	/// quanto ele tem agora, e como ele volta ao zero.
	/// </summary>
	private sealed class SistemaDoMundo
	{
		/// <summary>Como ele aparece na previa. Em portugues, porque quem le e o dono do servidor.</summary>
		public string Nome = "";

		/// <summary>
		/// OS ARQUIVOS QUE ELE ESCREVE, so o nome (a pasta e sempre a mesma). Vazio = sistema so de
		/// memoria, que existe aqui pelo `Zerar` -- o relogio do ceu, o povoamento.
		///
		/// Esta lista **nao dirige a exclusao** (quem apaga e a pasta inteira). Ela serve pra duas
		/// coisas: reservar o nome contra uma conta homonima, e dar a bancada o gabarito de quem
		/// reivindica o que.
		/// </summary>
		public string[] Arquivos = [];

		/// <summary>SOBREVIVE A LIMPEZA. Hoje so o `admin.log` -- ver o cabecalho.</summary>
		public bool Preservar;

		/// <summary>
		/// CRIVO PROPRIO, pra quem nao tem nome fixo. So as contas usam: o arquivo delas e
		/// `&lt;nome saneado&gt;.json`, entao nenhuma lista consegue nomea-las.
		/// </summary>
		public Func<string, bool>? Reivindica;

		public Func<int> Contar = () => 0;

		/// <summary>O substantivo da contagem: "conta(s)", "construcao(oes) de pe".</summary>
		public string Unidade = "";

		public Action Zerar = () => { };

		/// <summary>Este arquivo (so o nome) e meu? O `.tmp` de uma escrita atomica conta como meu.</summary>
		public bool Meu(string arquivo)
		{
			string n = arquivo.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
				? arquivo[..^4]
				: arquivo;
			return Reivindica != null
				? Reivindica(n)
				: Arquivos.Contains(n, StringComparer.OrdinalIgnoreCase);
		}
	}

	private readonly List<SistemaDoMundo> _sistemasDoMundo = [];

	/// <summary>
	/// A LISTA DE INSCRITOS. Chamada no boot, dentro do `CarregarVisual`, logo depois de o
	/// `AccountStore` existir e ANTES de qualquer varredura da pasta (o `_store.Quantas()` do fim do
	/// boot ja depende dos nomes reservados aqui).
	///
	/// ============================ POR QUE UMA TABELA, E O QUE ISSO AINDA NAO RESOLVE ============================
	/// O ideal seria cada sistema se inscrever do proprio arquivo, e a inscricao nem passar por aqui.
	/// Em C# isso custaria um atributo + varredura por reflexao, ou uma interface + registro estatico
	/// -- e as duas coisas tem o mesmo furo desta tabela: o sistema que nao se inscrever continua
	/// invisivel. Ou seja, **o problema nao se resolve por forma de declaracao**; ele se resolve por
	/// alguem RECLAMAR quando a inscricao falta.
	///
	/// Entao a tabela fica aqui (uma linha por sistema, legivel de uma olhada, com o dado do lado do
	/// nome) e a garantia mora na bancada `--wipeteste`, que varre a pasta de verdade e reprova
	/// arquivo sem dono. Ela e o que faz esta lista NAO apodrecer -- e e por isso que ela existe.
	/// ========================================================================================================
	/// </summary>
	private void RegistrarSistemasDoMundo()
	{
		if (_sistemasDoMundo.Count > 0) return;   // as bancadas sobem o servidor mais de uma vez

		// ---------------------------------------------------------- as contas
		// SEM NOME FIXO: o arquivo e `<conta saneada>.json`. O crivo pergunta o que a bancada precisa
		// perguntar -- "este .json e a conta que ele diz ser?" -- pelo MESMO saneamento que gravou.
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "contas de jogador (com os 3 personagens de cada)",
			Unidade = "conta(s)",
			Reivindica = EhArquivoDeConta,
			Contar = () => _store?.Quantas() ?? 0,
			// O QUE ZERA AQUI E SO O CACHE DE PUNICAO: as contas em si sao arquivos, e quem as
			// carregava (`_contas`/`_logados`) ja esvaziou quando todo mundo caiu. O conjunto dos
			// calados sobreviveria ao wipe e calaria o proximo dono do nome de conta.
			Zerar = () => { _calados.Clear(); _contasLocais.Clear(); },
		});

		// ---------------------------------------------------------- a semente do universo
		// ============================ SEM ESTA INSCRICAO, O WIPE NAO TROCA O MUNDO ============================
		// E o pedido do dono: *"os NPCS estao sempre nascendo IGUAIS a cada WIPE e o UNIVERSO tb"*. A
		// semente virou um arquivo (`universo.json`, ver `GameServer.Semente.cs`), e um arquivo de
		// mundo que nao se inscreve aqui e um arquivo que a bancada `--wipeteste` reprova como orfao --
		// e, pior, que sobreviveria ao wipe deixando o mundo "novo" no universo velho.
		//
		// A CONTAGEM E BINARIA e ela responde "o wipe alcancou a semente?": 1 enquanto a semente da
		// memoria for a que esta gravada nesta pasta, 0 depois do `Zerar` (que ja sorteia o proximo
		// universo). Quem grava o novo e o `AdminLimparServidor`, DEPOIS da vassoura -- ver la.
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "a semente deste universo (planetas, biomas, quem nasce em cada cidade)",
			Arquivos = ["universo.json"],
			Unidade = "universo com endereco",
			Contar = () => _sementeNoDisco ? 1 : 0,
			Zerar = ZerarSemente,
		});

		// ---------------------------------------------------------- construcoes
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "construcoes erguidas por jogador",
			Arquivos = ["mundo.json"],
			Unidade = "construcao(oes) de pe",
			Contar = () => _noChao.Count(o => !o.DoMapa),
			Zerar = ZerarConstrucoes,
		});

		// ---------------------------------------------------------- naves
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "naves compradas",
			Arquivos = ["naves.json"],
			Unidade = "nave(s)",
			Contar = () => _naves.Count,
			Zerar = () => { _naves.Clear(); _proximaNaveId = 1; },
		});

		// ---------------------------------------------------------- sagas
		// A CONTAGEM E DE ELOS ACORDADOS, e nao de elos: a cadeia inteira existe no `npcs.json` (que
		// e conteudo) desde sempre. O que a limpeza apaga e a FASE de cada um.
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "cadeia de sagas (Freeza, Cell, Boo)",
			Arquivos = ["sagas.json"],
			Unidade = "saga(s) em andamento",
			Contar = () => _sagas.Elos.Count(e => (int)e.Fase != 0),
			Zerar = ZerarSagas,
		});

		// ---------------------------------------------------------- reputacao
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "reputacao com o povo dos planetas",
			Arquivos = ["reputacao.json"],
			Unidade = "registro(s) de fama",
			Contar = () => _reputacao.Values.Sum(l => l.Count),
			Zerar = () => { _reputacao.Clear(); _nomesDaReputacao.Clear(); },
		});

		// ---------------------------------------------------------- conquista
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "dominios de planeta (bandeira, tributo, lealdade)",
			Arquivos = ["conquista.json"],
			Unidade = "planeta(s) dominado(s)",
			Contar = () => _dominios.Count,
			Zerar = () =>
			{
				_dominios.Clear();
				_recadosDeConquista.Clear();
				_ultimaCobrancaDeLealdade = 0;
			},
		});

		// ---------------------------------------------------------- planetas mortos
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "planetas destruidos ou explodindo",
			Arquivos = ["planetas-mortos.json"],
			Unidade = "planeta(s) morto(s)",
			Contar = () => _mortos.Quantos,
			Zerar = () => { _mortos.Limpar(); _proximoTremor.Clear(); _cargaDoPlanetDestroy.Clear(); },
		});

		// ---------------------------------------------------------- deveres de cargo
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "deveres de cargo em curso (+ o cofre da Terra)",
			Arquivos = ["missoes-de-cargo.json"],
			Unidade = "ficha(s) de dever",
			Contar = () => _missoes.Count,
			Zerar = () => { _missoes.Clear(); _cofreDaTerra = 0; _proximaVoltaDasMissoes = 0; },
		});

		// ---------------------------------------------------------- cargos
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "tronos ocupados (Guardiao, Kaio, Rei de Vegeta...)",
			Arquivos = ["cargos.txt"],
			Unidade = "cargo(s) com dono",
			Contar = () => _tronos.Count,
			Zerar = _tronos.Clear,
		});

		// ---------------------------------------------------------- discipulado
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "vinculos de mestre e aluno",
			Arquivos = ["mestres.txt"],
			Unidade = "discipulado(s)",
			Contar = () => _mestreDe.Count,
			Zerar = () => { _mestreDe.Clear(); _nomePorAssinatura.Clear(); },
		});

		// ---------------------------------------------------------- herdeiros
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "linha de sucessao do trono de Vegeta",
			Arquivos = ["herdeiros.txt"],
			Unidade = "herdeiro(s)",
			Contar = () => _herdeirosDeVegeta.Count,
			Zerar = () => { _herdeirosDeVegeta.Clear(); _convitesDeAnciao.Clear(); },
		});

		// ---------------------------------------------------------- titulo do Deus da Destruicao
		// A CONTAGEM E BINARIA porque o estado e um so: ou ha um relogio de titulo correndo, ou nao ha.
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "relogio do titulo de Deus da Destruicao",
			Arquivos = ["titulo.txt"],
			Unidade = "titulo em disputa",
			Contar = () => _duelo.TituloDesde != 0 || _tarefaAlvo.Length > 0 || _tarefaFalhas > 0 ? 1 : 0,
			Zerar = LimparEstadoDoTitulo,
		});

		// ---------------------------------------------------------- o relogio do mundo
		// SEM ARQUIVO, e ele precisa estar aqui mesmo assim: uma limpeza que se propoe a devolver o
		// "primeiro boot" nao pode deixar o mundo novo nascer num dia in-game arbitrario -- os prazos
		// de cargo sairiam tortos a partir do primeiro tique.
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "adianto do relogio do ceu (manivela de admin/bancada)",
			Unidade = "relogio adiantado",
			Contar = () => _adiantoDoCeu != 0 ? 1 : 0,
			Zerar = () => _adiantoDoCeu = 0,
		});

		// ---------------------------------------------------------- povoamento e invasoes
		// TAMBEM SEM ARQUIVO. Os habitantes sao corpos em `_players` e somem quando ele esvazia; o
		// que sobreviveria e o CONTADOR de lugares (que sorteia a ficha de cada NPC) e as campanhas
		// em voo -- um mundo "novo" cujo primeiro cidadao nasce com a semente do mundo anterior.
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "povoamento e invasoes em voo",
			Unidade = "invasao(oes)",
			Contar = () => _invasoes.Count,
			Zerar = () =>
			{
				_filaDoPovoamento.Clear();
				_lugaresDoPovoamento.Clear();
				_proximaManutencao = 0;
				_relogioDoPovoamento = 0;
				_invasoes.Clear();
				_arranques.Clear();
				_proximaCampanha.Clear();
			},
		});

		// ---------------------------------------------------------- o que SOBREVIVE
		// O `admin.log` e a unica testemunha do que os administradores fizeram -- inclusive de quem
		// rodou esta limpeza. O `wipe-servidor.bat` do BYOND tambem preservava os logs, e pelo mesmo
		// motivo: um poder sem rastro e um poder em que ninguem confia, inclusive quem o tem.
		//
		// Ele esta INSCRITO (e nao numa excecao a parte) porque e assim que a bancada sabe que ele
		// tem dono: um `admin.log` fora do registro seria reprovado como arquivo orfao.
		_sistemasDoMundo.Add(new SistemaDoMundo
		{
			Nome = "trilha de acoes de admin (SOBREVIVE a limpeza)",
			Arquivos = ["admin.log"],
			Preservar = true,
			Unidade = "arquivo de log",
			Contar = () => 0,
		});

		// A INSCRICAO RESERVA O NOME. E o que impede a conta chamada "naves" de gravar por cima da
		// frota do servidor -- ver o cabecalho de `AccountStore.NaoSaoContas`, e o defeito que isto
		// conserta. Feito daqui, e nao la, porque so aqui alguem sabe a lista inteira.
		foreach (SistemaDoMundo s in _sistemasDoMundo)
			foreach (string a in s.Arquivos)
				AccountStore.ReservarArquivo(a);

		GD.Print($"[limpeza] {_sistemasDoMundo.Count} sistema(s) de mundo inscritos | "
			   + $"{AccountStore.ArquivosReservados.Count} nome(s) de arquivo reservados");
	}

	/// <summary>
	/// ESTE `.json` DA PASTA E UMA CONTA DE VERDADE?
	///
	/// Nao basta "termina em .json e nao esta na lista dos outros" -- era exatamente essa a
	/// heuristica que deixava um arquivo novo passar por conta. Aqui a pergunta e a forte: o
	/// conteudo desserializa como <see cref="AccountSave"/>, tem nome de conta, e o nome SANEADO
	/// dele produz este mesmo arquivo. Um `clima.json` que alguem criar amanha reprova nas tres.
	/// </summary>
	private bool EhArquivoDeConta(string arquivo)
	{
		if (_store == null || !arquivo.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
		try
		{
			string texto = System.IO.File.ReadAllText(System.IO.Path.Combine(_store.Pasta, arquivo));
			AccountSave? a = System.Text.Json.JsonSerializer.Deserialize<AccountSave>(
				texto, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
			if (a == null || a.Conta.Length == 0) return false;
			return string.Equals(AccountStore.NomeDeArquivo(a.Conta) + ".json", arquivo,
								 StringComparison.OrdinalIgnoreCase);
		}
		catch { return false; }
	}

	// =====================================================================
	// OS `Zerar` QUE NAO CABEM NUMA LINHA
	// =====================================================================
	/// <summary>
	/// AS CONSTRUCOES SOMEM, A MOBILIA DO MAPA FICA -- e a distincao e a fronteira do pedido.
	///
	/// `_noChao` guarda as duas coisas: o que um jogador ergueu e as maquinas que o `.objetos` do
	/// mapa ja trazia (banco, bancada, sala de gravidade). As segundas nascem do disco de CONTEUDO a
	/// cada boot e nao vao pro `mundo.json` (ver `GravarMundoEm`) -- limpa-las aqui deixaria o
	/// servidor "novo" sem banco e sem bancada, que e o oposto de "como se tivesse acabado de rodar
	/// pela primeira vez".
	///
	/// E A COLISAO PRECISA SER REFEITA, senao o mundo limpo fica com PAREDE INVISIVEL onde havia
	/// construcao: `AplicarColisaoDasObras` escreveu celulas bloqueadas no mapa da zona no boot, e
	/// esvaziar a lista nao desfaz o que ja foi escrito. Refazer por zona e barato (ele reconstroi a
	/// camada inteira a partir do que sobrou) e e o mesmo metodo que o boot usa.
	/// </summary>
	private void ZerarConstrucoes()
	{
		// AS ZONAS AFETADAS SAO COLHIDAS ANTES: depois do `RemoveAll` nao ha mais de onde tirar.
		// As naves entram na mesma conta porque nave parada tambem bloqueia celula, e o
		// `AplicarColisaoDasObras` refaz as duas camadas na mesma passada.
		foreach (Obra o in _noChao) if (!o.DoMapa) _zonasPorRecolidir.Add(o.Zona);
		foreach (Nave n in _naves) _zonasPorRecolidir.Add(n.Zona);

		_noChao.RemoveAll(o => !o.DoMapa);
		_proximaObraId = _noChao.Count > 0 ? _noChao.Max(o => o.Id) + 1 : 1;

		// JA SE REFAZ O QUE DA AQUI, pra quem chamar este `Zerar` sozinho (a bancada) ver o efeito
		// completo -- mas as celulas das NAVES so caem na passada final, porque `_naves` ainda esta
		// cheio neste ponto. Ver `RefazerColisaoDeTudo`.
		foreach (ZoneKey z in _zonasPorRecolidir) AplicarColisaoDasObras(z);
	}

	/// <summary>
	/// AS ZONAS QUE TINHAM OBRA OU NAVE -- guardadas entre o `Zerar` e a passada final.
	///
	/// ============================ SEM ISTO, PLANETA GERADO FICA COM PAREDE INVISIVEL ============================
	/// A passada final varria `_catalogo.Todas`, que so conhece as zonas PRE-FEITAS -- as de arquivo.
	/// Uma nave parada num planeta SORTEADO nao esta em nenhuma delas, e a celula dela continuaria
	/// bloqueada no mundo "limpo": uma parede invisivel no meio do nada, num planeta onde nunca houve
	/// nave nenhuma, sem nada no disco que explicasse.
	///
	/// A ordem dos inscritos e o que cria a janela: `ZerarConstrucoes` roda com `_naves` ainda cheio,
	/// entao ele reblocqueia as celulas das naves que estao prestes a sumir. Inverter a ordem da lista
	/// resolveria hoje e quebraria no dia em que alguem a reordenasse -- guardar as zonas resolve
	/// independente de ordem, que e a unica forma que continua valendo depois.
	/// ========================================================================================================
	/// </summary>
	private readonly HashSet<ZoneKey> _zonasPorRecolidir = [];

	/// <summary>
	/// A CADEIA VOLTA ADORMECIDA -- e nao "vazia".
	///
	/// `EstadoDasSagas` novo teria a lista de elos VAZIA, e o `TickDasSagas` procura o estado de cada
	/// elo do `npcs.json` la dentro. O boot resolve isso alinhando estado a cadeia (`CarregarSagas`);
	/// aqui a mesma linha e refeita, senao a primeira saga do mundo novo nunca comeca.
	/// </summary>
	private void ZerarSagas()
	{
		_sagas = new Jandirus.Core.Npc.EstadoDasSagas();
		foreach (Jandirus.Core.Npc.EloDaSaga e in _cadeia)
			_sagas.Elos.Add(new Jandirus.Core.Npc.EstadoDoElo { Id = e.Id });
		_sagasPorRetomar = true;
	}

	/// <summary>
	/// A PASSADA FINAL DA COLISAO: toda zona pre-feita, MAIS as que tinham obra ou nave.
	///
	/// As duas metades sao necessarias e nenhuma cobre a outra: o catalogo pega as zonas de arquivo
	/// (inclusive as que tinham obra e cujo registro ja sumiu da lista), e o
	/// <see cref="_zonasPorRecolidir"/> pega os planetas SORTEADOS, que nao estao em catalogo nenhum.
	/// </summary>
	private void RefazerColisaoDeTudo()
	{
		if (_catalogo != null)
			foreach (ZoneEntry e in _catalogo.Todas) AplicarColisaoDasObras(ZoneKey.Premade(e.Zona));

		foreach (ZoneKey z in _zonasPorRecolidir) AplicarColisaoDasObras(z);
		_zonasPorRecolidir.Clear();
	}

	// =====================================================================
	// A PREVIA -- o PRIMEIRO dos dois passos
	// =====================================================================
	/// <summary>
	/// LIGADO, O SERVIDOR NAO GRAVA NADA E NAO ACEITA NINGUEM.
	///
	/// Ver a ordem no cabecalho: sem este bit, deslogar todo mundo RECRIA cada conta (o `Drop` chama
	/// `Persistir` antes de soltar) e a limpeza se desfaz no proprio gesto de fazer a limpeza.
	/// </summary>
	private bool _limpezaEmCurso;

	public bool LimpezaEmCurso => _limpezaEmCurso;

	/// <summary>A conta que pediu a previa, o codigo sorteado, e ate quando ele vale (Unix ms).</summary>
	private string _limpezaPedidaPor = "", _limpezaCodigo = "";
	private long _limpezaVenceEm;

	/// <summary>
	/// QUANTO TEMPO O CODIGO VALE. Um minuto: tempo de ler oito linhas e digitar quatro caracteres,
	/// e curto o bastante pra que um painel esquecido aberto nao seja uma arma carregada em cima da
	/// mesa. Vencido, o verb manda pedir de novo -- e a previa e recontada, o que tambem e certo:
	/// confirmar com uma contagem de dez minutos atras e confirmar outra coisa.
	/// </summary>
	private const long ValidadeDaPrevia = 60_000;

	/// <summary>
	/// O ALFABETO DO CODIGO: sem `O`, `0`, `I`, `1`. Um codigo que se le errado vira um codigo que
	/// se digita errado, e o custo disso aqui e o admin achar que o servidor recusou por outro
	/// motivo. Quatro caracteres deste conjunto dao ~1 milhao de combinacoes -- nao e criptografia,
	/// e uma tranca contra o dedo escorregando.
	/// </summary>
	private const string AlfabetoDoCodigo = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

	/// <summary>
	/// O INVENTARIO DO QUE VAI SUMIR, uma linha por sistema com CONTAGEM.
	///
	/// So o que tem alguma coisa. As linhas zeradas viram um resumo no fim -- oito linhas com "0" no
	/// meio da lista e ruido, e ruido e o que faz ninguem ler o aviso importante.
	/// </summary>
	private List<string> InventarioDaLimpeza()
	{
		var linhas = new List<string>();
		int vazios = 0;

		foreach (SistemaDoMundo s in _sistemasDoMundo)
		{
			if (s.Preservar) continue;
			int n;
			try { n = s.Contar(); }
			catch (Exception e)
			{
				// UM CONTADOR QUEBRADO NAO PODE ESCONDER A PREVIA INTEIRA. Ele aparece na lista com o
				// erro escrito: "nao sei quantos" e uma resposta honesta e visivel; sumir nao e.
				linhas.Add($"?  {s.Nome} -- nao consegui contar ({e.Message})");
				continue;
			}
			if (n == 0) { vazios++; continue; }
			linhas.Add($"{n:N0}  {s.Unidade} -- {s.Nome}");
		}

		// AS DUAS LINHAS QUE NAO SAO CONTAGEM DE SISTEMA, e as duas importam pra decisao.
		int online = Jogadores.Count();
		if (online > 0) linhas.Add($"{online}  jogador(es) ONLINE -- todos caem, sem salvar");

		int banidas = 0, caladas = 0;
		foreach (AccountSave a in _store?.Todas() ?? [])
		{
			if (a.Banida) banidas++;
			if (a.Calada) caladas++;
		}
		if (banidas + caladas > 0)
			linhas.Add($"{banidas}  banimento(s) e {caladas} mute(s) -- PERDOADOS (moram dentro da conta)");

		if (vazios > 0) linhas.Add($"({vazios} outro(s) sistema(s) ja estao vazios)");
		linhas.Add("o admin.log SOBREVIVE -- e a unica testemunha de quem limpou");
		return linhas;
	}

	/// <summary>
	/// PASSO 1: monta a previa, sorteia o codigo e manda pro painel.
	///
	/// O codigo e sorteado AQUI e nunca sai do servidor a nao ser neste pacote. Ele e o que separa
	/// "cliquei sem querer" de "eu quis": pra apagar o servidor o admin precisa ter visto a lista,
	/// ter lido quatro caracteres que so existem nesta tela e digita-los dentro de um minuto.
	/// </summary>
	private void AdminPrepararLimpeza(ServerPlayer adm)
	{
		if (_store == null) { Avisar(adm, "este servidor nao guarda nada em disco -- nao ha o que limpar."); return; }

		var sb = new System.Text.StringBuilder(4);
		Span<byte> b = stackalloc byte[4];
		// `System.Security.Cryptography` escrito por extenso: `Godot.RandomNumberGenerator` (o node
		// de sorteio) esta no escopo e o nome cru fica ambiguo -- a mesma armadilha que o
		// `System.Environment` do `Registrar` ja pagou neste projeto.
		System.Security.Cryptography.RandomNumberGenerator.Fill(b);
		foreach (byte x in b) sb.Append(AlfabetoDoCodigo[x % AlfabetoDoCodigo.Length]);

		_limpezaCodigo = sb.ToString();
		_limpezaPedidaPor = adm.Conta;
		_limpezaVenceEm = NowMs() + ValidadeDaPrevia;

		List<string> linhas = InventarioDaLimpeza();
		MandarPrevia(adm, _limpezaCodigo, linhas);

		GD.Print($"[limpeza] {adm.Name} ({adm.Conta}) pediu a previa da limpeza total -- "
			   + string.Join(" | ", linhas));
	}

	private void MandarPrevia(ServerPlayer adm, string codigo, List<string> linhas)
	{
		var w = Protocol.Begin(Protocol.S2C.Limpeza);
		w.Put(codigo);
		w.Put((ushort)(ValidadeDaPrevia / 1000));
		w.Put((byte)Math.Min(linhas.Count, byte.MaxValue));
		foreach (string l in linhas.Take(byte.MaxValue)) w.Put(l);
		adm.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Fecha o painel de perigo do admin: previa consumida, ou vencida.</summary>
	private void CancelarPrevia(ServerPlayer adm)
	{
		_limpezaCodigo = _limpezaPedidaPor = "";
		_limpezaVenceEm = 0;
		MandarPrevia(adm, "", []);
	}

	// =====================================================================
	// PASSO 2: A LIMPEZA
	// =====================================================================
	/// <summary>
	/// APAGA O SERVIDOR INTEIRO. So roda com o codigo da previa, digitado, dentro do prazo, pela
	/// MESMA conta que pediu.
	///
	/// A ordem esta no cabecalho deste arquivo e ela e a parte que importa.
	/// </summary>
	private void AdminLimparServidor(ServerPlayer adm, string codigo)
	{
		if (_store == null) { Avisar(adm, "este servidor nao guarda nada em disco."); return; }

		codigo = codigo.Trim().ToUpperInvariant();
		if (_limpezaCodigo.Length == 0)
		{
			Avisar(adm, "peca a previa antes -- e ela que mostra o que vai sumir e sorteia o codigo.");
			return;
		}
		if (NowMs() > _limpezaVenceEm)
		{
			Avisar(adm, "o codigo venceu. Peca a previa de novo (a contagem tambem sera refeita).");
			CancelarPrevia(adm);
			return;
		}
		if (!string.Equals(_limpezaPedidaPor, adm.Conta, StringComparison.OrdinalIgnoreCase))
		{
			// QUEM PEDIU E QUEM CONFIRMA. Sem isto, um admin veria a lista e outro apertaria o
			// gatilho com um codigo que ele nao leu -- e a contagem que apareceu na tela de um
			// nao seria o que o outro apagou.
			Avisar(adm, "quem pediu esta previa foi outra conta. Peca a sua.");
			return;
		}
		if (!string.Equals(_limpezaCodigo, codigo, StringComparison.Ordinal))
		{
			Avisar(adm, $"codigo errado. Digite exatamente o que esta na tela ({_limpezaCodigo.Length} caracteres).");
			return;
		}

		// ---------------------------------------------------------- o rastro, ANTES de apagar
		// Escrito agora e nao depois, e com a contagem: depois da limpeza nao ha mais como contar
		// nada, e uma linha de log que diz so "limpou" nao responde a pergunta que alguem vai fazer.
		List<string> inventario = InventarioDaLimpeza();
		Registrar(adm, "LIMPEZA TOTAL DO SERVIDOR -- " + string.Join(" | ", inventario));

		GD.Print("[limpeza] ================ LIMPEZA TOTAL ================");
		foreach (string l in inventario) GD.Print("[limpeza]   " + l);

		_limpezaCodigo = _limpezaPedidaPor = "";
		_limpezaVenceEm = 0;

		ResultadoDaLimpeza r = ExecutarLimpeza(adm);

		// ---------------------------------------------------------- o endereco do mundo novo
		// ============================ AQUI, E NAO NO `Zerar` ============================
		// O `ZerarSemente` (passo 3) ja sorteou o universo novo NA MEMORIA; gravar la seria escrever
		// um arquivo que a vassoura do passo 4 apagaria em seguida. Entao a gravacao e esta linha, com
		// a pasta ja limpa e ja recriada.
		//
		// E ela nao pode faltar: sem arquivo, o proximo boot leria "pasta vazia = mundo novo" e
		// sortearia uma TERCEIRA semente -- e as contas criadas entre o wipe e o reinicio apontariam
		// pra zonas de um universo que deixou de existir. E o mesmo motivo pelo qual a semente mora no
		// disco em primeiro lugar.
		//
		// A BANCADA NAO PASSA POR AQUI de proposito: ela chama o `ExecutarLimpeza` direto, e afirma
		// que a pasta fica so com o `admin.log` depois dele. Esta linha e do caminho de producao.
		SalvarSemente();
		GD.Print($"[limpeza] o mundo novo tem endereco proprio: semente {SeedDoUniverso}");

		// A ULTIMA LINHA DO LOG e a que fecha a historia -- e ela cabe porque o `admin.log`
		// sobrevive. Escrita pelo mesmo funil das outras, com a pasta ja recriada.
		Registrar(adm, $"limpeza concluida: {r.Apagados} arquivo(s) apagado(s), {r.Caiu} conexao(oes) derrubada(s)"
					 + (r.Erros.Count > 0 ? $", {r.Erros.Count} erro(s) de disco" : ""));
	}

	/// <summary>O que a limpeza fez. Existe pra bancada poder afirmar sobre numeros e nao sobre log.</summary>
	private readonly record struct ResultadoDaLimpeza(
		int Caiu, int Zerados, int Apagados, int Poupados, List<string> Erros);

	/// <summary>
	/// A LIMPEZA EM SI -- os quatro passos, na ordem que o cabecalho deste arquivo explica.
	///
	/// SEPARADA DO VERB de proposito: quem confere codigo, prazo e permissao e o
	/// <see cref="AdminLimparServidor"/>; quem apaga e isto. A bancada `--wipeteste` chama ESTE
	/// metodo, e nao uma copia dele -- uma bancada que reimplementasse a ordem testaria a copia, que
	/// e exatamente como a ordem errada continuaria passando.
	///
	/// O `adm` e nulo quando quem chama e a bancada: nao ha ninguem online pra avisar.
	/// </summary>
	private ResultadoDaLimpeza ExecutarLimpeza(ServerPlayer? adm)
	{
		// ---------------------------------------------------------- 1. o portao fecha
		_limpezaEmCurso = true;
		try
		{
			// ------------------------------------------------------ 2. todo mundo cai
			int caiu = DerrubarTodoMundoParaALimpeza(adm);

			// ------------------------------------------------------ 3. a memoria
			int zerados = 0;
			foreach (SistemaDoMundo s in _sistemasDoMundo)
			{
				if (s.Preservar) continue;
				try { s.Zerar(); zerados++; }
				catch (Exception e)
				{
					// UM `Zerar` QUE ESTOURA NAO PODE PARAR OS OUTROS: parar no meio deixaria
					// exatamente o estado que este arquivo inteiro existe pra evitar -- metade do
					// mundo lembrado, metade esquecido.
					GD.PushError($"[limpeza] o Zerar de '{s.Nome}' quebrou: {e}");
				}
			}
			// A COLISAO POR ULTIMO: `ZerarConstrucoes` roda antes de `_naves.Clear()` na ordem dos
			// inscritos, entao as celulas das naves so somem nesta passada final.
			RefazerColisaoDeTudo();

			// ------------------------------------------------------ 4. o disco
			(int apagados, int poupados, List<string> erros) = ApagarAPasta();

			GD.Print($"[limpeza] pronto: {caiu} conexao(oes) derrubada(s), {zerados} sistema(s) zerado(s), "
				   + $"{apagados} arquivo(s) apagado(s), {poupados} poupado(s)");
			foreach (string e in erros) GD.PushWarning("[limpeza] " + e);

			return new ResultadoDaLimpeza(caiu, zerados, apagados, poupados, erros);
		}
		finally
		{
			// SEMPRE DESLIGA. Um portao que fica fechado por causa de uma excecao e um servidor que
			// nunca mais grava nada -- e ninguem descobriria antes de perder um dia de progresso.
			_limpezaEmCurso = false;
		}
	}

	/// <summary>
	/// DERRUBA TODO MUNDO, sem salvar ninguem (o portao ja esta fechado), e esvazia as colecoes de
	/// sessao na mao.
	///
	/// ============================ POR QUE ESVAZIAR NA MAO ============================
	/// `peer.Disconnect()` nao derruba na hora: o evento chega no proximo `PollEvents`, e ate la
	/// `_players` continua cheio -- o `Zerar` das construcoes refaria a colisao com jogadores dentro,
	/// e o tique seguinte ainda mandaria snapshot de um mundo que acabou de sumir. Limpando aqui, o
	/// `Drop` que chegar depois cai no ramo curto (`_byPeer.Remove` falha) e nao faz nada.
	///
	/// E O ADMIN CAI TAMBEM, de proposito. Ele nao pode continuar andando: a conta dele nao existe
	/// mais, o personagem dele nao existe mais, e a primeira coisa que qualquer sistema faria com
	/// aquele corpo seria gravar um save novo. "Como se tivesse acabado de rodar pela primeira vez"
	/// inclui nao ter ninguem dentro.
	/// ================================================================================
	/// </summary>
	private int DerrubarTodoMundoParaALimpeza(ServerPlayer? adm)
	{
		int n = 0;

		foreach (ServerPlayer p in Jogadores.ToList())
		{
			Avisar(p, p == adm
				? "limpeza total em curso -- voce sera desconectado."
				: "o servidor foi limpo por um administrador. Tudo recomeca do zero.");
			p.Peer?.Disconnect();
			n++;
		}

		// E QUEM ESTA PRESO NA TELA DE SELECAO. Essa gente nao tem `ServerPlayer` -- o laco acima nao
		// a alcanca --, e sem isto ela clicaria num slot e entraria com um personagem apagado.
		foreach (NetPeer preso in _logados.Keys.ToList()) { preso.Disconnect(); n++; }

		_players.Clear();
		_byPeer.Clear();
		_contas.Clear();
		_logados.Clear();
		_zones.Clear();
		return n;
	}

	/// <summary>
	/// APAGA A PASTA -- tudo o que nenhum inscrito marcou como <see cref="SistemaDoMundo.Preservar"/>.
	///
	/// ============================ O ALVO E A PASTA, E NAO UMA LISTA DE NOMES ============================
	/// E a perna 1 do cabecalho, e e ela que faz esta limpeza nao apodrecer. Um sistema de
	/// persistencia que nascer amanha vai gravar dentro desta pasta (nao ha outra: `_store.Pasta` e o
	/// unico diretorio que o servidor cria) e vai sumir daqui sem ninguem ter tocado neste metodo.
	///
	/// E cobre o que uma lista nunca cobriria: o `.tmp` orfao de uma escrita atomica interrompida por
	/// uma queda do processo.
	/// ================================================================================================
	///
	/// APAGA ARQUIVO A ARQUIVO em vez de `Directory.Delete(recursive)` por uma razao pratica: um
	/// arquivo aberto noutro programa (o dono espiando o `mundo.json` no editor) faria o delete da
	/// pasta inteira falhar e **nada** seria apagado. Assim, o que da pra apagar e apagado e o que
	/// resistiu e nomeado no log.
	/// </summary>
	private (int Apagados, int Poupados, List<string> Erros) ApagarAPasta()
	{
		var erros = new List<string>();
		int apagados = 0, poupados = 0;
		if (_store == null) return (0, 0, erros);

		foreach (string caminho in _store.TodosOsArquivos().ToList())
		{
			string nome = System.IO.Path.GetFileName(caminho);
			if (_sistemasDoMundo.Any(s => s.Preservar && s.Meu(nome))) { poupados++; continue; }

			try { System.IO.File.Delete(caminho); apagados++; }
			catch (Exception e) { erros.Add($"nao apaguei '{nome}': {e.Message}"); }
		}

		// A PASTA VOLTA A EXISTIR na hora, e nao no proximo save: o `Registrar` de baixo escreve
		// nela, e `AccountStore.Gravar` so a cria no caminho de gravar uma conta.
		try { System.IO.Directory.CreateDirectory(_store.Pasta); }
		catch (Exception e) { erros.Add($"nao recriei a pasta: {e.Message}"); }

		return (apagados, poupados, erros);
	}
}
