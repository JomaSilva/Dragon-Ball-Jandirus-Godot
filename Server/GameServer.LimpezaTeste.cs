using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA LIMPEZA TOTAL -- `--wipeteste`.
///
/// ============================ ELA E A UNICA PERNA QUE NAO APODRECE ============================
/// A limpeza tem tres pernas (ver o cabecalho de `GameServer.Limpeza.cs`): a pasta inteira no disco,
/// o `Zerar()` de cada inscrito na memoria, e ESTA BANCADA. As duas primeiras dependem de alguem se
/// inscrever; esta aqui e quem RECLAMA quando ninguem se inscreveu.
///
/// O defeito que ela pega e o mais silencioso do projeto: alguem escreve um `clima.json` na pasta de
/// saves, esquece de declarar, e a partir dali (a) o painel de admin cospe "conta ilegivel" a cada
/// abertura, (b) a contagem de contas do boot infla, (c) uma conta chamada "clima" grava por cima do
/// estado do mundo, e (d) -- o pior -- a limpeza total deixa aquele arquivo para tras e o mundo
/// "novo" volta com metade da memoria do antigo. Nada disso da erro. Tudo isso aparece semanas
/// depois como "comportamento estranho".
///
/// Isto **ja aconteceu**: `conquista.json`, `naves.json` e `missoes-de-cargo.json` nasceram depois da
/// lista a mao de `AccountStore.NaoSaoContas` e ficaram tres anos-luz atras dela.
/// ==========================================================================================
///
/// AS SECOES:
///  1. O REGISTRO -- todo inscrito tem nome, unidade e `Zerar`; nenhum arquivo e reivindicado por
///     dois sistemas; e todo arquivo declarado esta RESERVADO contra uma conta homonima.
///  2. O ARQUIVO ORFAO -- a varredura da pasta de verdade. **Read-only.** Reprova se sobrou um
///     arquivo que nenhum inscrito reivindica. E a trava.
///  3. O PORTAO DE GRAVACAO -- a MEDIDA do que o dono pediu pra medir: com o portao aberto,
///     `Persistir` grava; com ele fechado, nao grava. Sem esta linha, deslogar todo mundo RECRIA as
///     contas e a limpeza se desfaz sozinha.
/// 3b. A CONFIRMACAO -- as quatro portas: sem previa, codigo errado, codigo vencido, outra conta.
///  4. A LIMPEZA DE VERDADE -- roda o `ExecutarLimpeza` de producao contra o mundo carregado, afirma
///     que a memoria zerou e que a pasta esvaziou (menos o `admin.log`), e DEVOLVE tudo.
///  5. O RETRATO DE CONJUNTO -- **a afirmacao central**: primeiro boot -> jogar -> limpar, e o
///     retrato do fim tem que ser IGUAL ao do comeco. Conjunto derivado (disco cru + estado dos
///     inscritos), e nao lista de nomes.
///  6. O DEFEITO INJETADO -- a secao 5 provada DISPARANDO, duas vezes: um sistema que nunca foi
///     inscrito (so a metade do disco pega) e um `Zerar` mudo (a metade da memoria pega).
///  7. O JOGADOR ONLINE -- ele nao ressuscita o proprio save, medido no pior instante: uma sonda
///     inscrita tenta gravar a conta DENTRO da limpeza.
///  8. O SERVIDOR FUNCIONA DEPOIS -- conta nova, corpo que nasce, anda e transforma no mundo limpo.
///  9. O CONTEUDO SOBREVIVEU -- o contra-exemplo. Sem ele, "apagar mais" deixaria tudo verde.
/// 10. AS ESFERAS DO DRAGAO, JOGADAS DE VERDADE -- um jogador ERGUE a estatua, PEGA as sete,
///     INVOCA, REIVINDICA as Super Esferas e deixa uma PROCURACAO de pe, tudo pelos verbos de
///     producao; e so entao a limpeza. E a unica secao que mede este sistema pelo gesto, e nao pelo
///     campo escrito a mao. **10b** e ela provada disparando, com o `Zerar` das esferas mudo.
///
/// ============================ AS SECOES 5 A 8 NAO SAO ENFEITE DA 4 ============================
/// A secao 4 pergunta "a memoria zerou e a pasta esvaziou?", e as duas respostas sao SIM num
/// servidor que zerou de menos: a vassoura varre a pasta inteira, entao o disco fica limpo mesmo
/// quando um sistema inteiro foi esquecido -- e volta a sujar sozinho na primeira gravacao. E a
/// secao 4 tambem fica verde num servidor que zerou DEMAIS (sem skills, sem mobilia, sem colisao).
/// A 5 pega o primeiro caso, a 9 pega o segundo, e a 6 prova que a 5 dispara mesmo.
/// ==========================================================================================
///
/// ============================ ELA MEXE NO MUNDO DE VERDADE, E POR ISSO FAZ COPIA ============================
/// A secao 4 nao tem como ser honesta de outro jeito: uma limpeza testada contra uma pasta de
/// mentira testa a pasta de mentira. Entao ela COPIA a pasta inteira pra um temporario ANTES (e diz
/// no console onde ficou), roda a limpeza de producao, afirma, e restaura -- os arquivos de volta e
/// os carregadores de producao de novo, que e o que reconstroi a memoria.
///
/// A restauracao usa os `Carregar*` de PRODUCAO e nao uma copia: quatro deles (`mundo`, `naves`,
/// `conquista`, `reputacao`) enchem sem `Clear()` antes, e so sao reexecutaveis porque o `Zerar()`
/// acabou de esvaziar tudo. Isso e uma afirmacao a mais, de graca: se um `Zerar` esquecer um
/// contentor, a restauracao DUPLICA e a secao 4 fica vermelha na contagem final.
/// ========================================================================================================
///
///     Godot --headless -- --server --wipeteste
/// </summary>
public partial class GameServer
{
	private int _wtOk, _wtFalhou;

	private void AfirmarWt(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _wtOk++; GD.Print($"[wipe]   OK    {oque}"); return; }
		_wtFalhou++;
		GD.PrintErr($"[wipe]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaLimpeza()
	{
		_wtOk = _wtFalhou = 0;
		GD.Print("[wipe] ================ A LIMPEZA TOTAL DO SERVIDOR ================");

		if (_store == null)
		{
			AfirmarWt("o servidor tem pasta de saves", false, "sem AccountStore -- nada a medir");
			return;
		}

		// O RETRATO DO CONTEUDO E TIRADO AGORA, antes de qualquer limpeza -- ele e o
		// contra-exemplo (secao 9). Tirado depois da primeira, ja nao provaria nada: mediria o
		// mundo pos-limpeza contra ele mesmo.
		List<string> conteudoAntes = RetratoDoConteudo();

		SecaoDoRegistro();
		SecaoDoArquivoOrfao();
		SecaoDoPortaoDeGravacao();
		SecaoDaConfirmacao();
		SecaoDaLimpezaDeVerdade();
		SecaoDoRetratoDeConjunto();
		SecaoDaInjecaoDoDefeito();
		SecaoDoJogadorOnline();
		SecaoDoServidorDepois();
		SecaoDasEsferasJogadas();
		SecaoDoZerarMudoDasEsferas();
		SecaoDoConteudoDoJogo(conteudoAntes);

		GD.Print($"[wipe] ================ {_wtOk} OK, {_wtFalhou} FALHA(S) ================");
	}

	// =====================================================================
	// 1. O REGISTRO
	// =====================================================================
	private void SecaoDoRegistro()
	{
		GD.Print("[wipe] -- 1. o registro dos sistemas do mundo --");

		AfirmarWt("ha sistemas inscritos", _sistemasDoMundo.Count > 0);

		foreach (SistemaDoMundo s in _sistemasDoMundo)
		{
			AfirmarWt($"'{s.Nome}' tem nome legivel", s.Nome.Trim().Length > 3, s.Nome);
			// O QUE PRESERVA NAO PRECISA DE `Zerar` NEM DE UNIDADE (o `admin.log` nao e contado).
			if (s.Preservar) continue;
			AfirmarWt($"'{s.Nome}' declara a unidade da contagem", s.Unidade.Trim().Length > 0);
		}

		// ---------------------------------------------------------- dois donos pro mesmo arquivo
		// Isto nao e capricho: com dois donos, o `Meu()` do primeiro esconde o segundo da secao 2 --
		// e o segundo poderia nunca ter `Zerar` nenhum, ficando invisivel exatamente como um orfao.
		var vistos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		bool duplicado = false;
		foreach (SistemaDoMundo s in _sistemasDoMundo)
			foreach (string a in s.Arquivos)
			{
				if (vistos.TryGetValue(a, out string? outro))
				{
					duplicado = true;
					GD.PrintErr($"[wipe]         '{a}' e reivindicado por '{outro}' E por '{s.Nome}'");
				}
				else vistos[a] = s.Nome;
			}
		AfirmarWt("nenhum arquivo tem dois donos", !duplicado);

		// ---------------------------------------------------------- a reserva de nome
		// A inscricao TEM que reservar o nome, senao volta o defeito que ela veio consertar: a conta
		// chamada "naves" gravando por cima da frota do servidor.
		bool todosReservados = true;
		foreach ((string arquivo, string dono) in vistos)
		{
			if (AccountStore.ArquivosReservados.Contains(arquivo)) continue;
			todosReservados = false;
			GD.PrintErr($"[wipe]         '{arquivo}' ({dono}) nao esta reservado contra conta homonima");
		}
		AfirmarWt($"os {vistos.Count} arquivos declarados estao reservados", todosReservados);

		// E A RESERVA TEM QUE FUNCIONAR NO CAMINHO DE PRODUCAO. `NomeReservado` sofre o saneamento
		// (`Arquivo()`), entao "naves.json" nao se testa pelo nome do arquivo e sim pelo nome da
		// CONTA que produziria aquele arquivo. Este e o crivo que o `Login` chama.
		AfirmarWt("o Login recusa a conta 'mundo'", AccountStore.NomeReservado("mundo"));
		AfirmarWt("o Login recusa a conta 'naves'", AccountStore.NomeReservado("naves"),
				  "era um nome de conta ALCANCAVEL antes desta lista virar derivada");
		AfirmarWt("o Login recusa a conta 'conquista'", AccountStore.NomeReservado("conquista"));
		// A CAIXA NAO PODE SER A BRECHA: o saneamento passa tudo pra minusculo antes de comparar.
		AfirmarWt("o Login recusa a conta 'MuNdO' (a caixa nao burla)", AccountStore.NomeReservado("MuNdO"));

		// ============================ ESTA LINHA ACHOU UM COMENTARIO MENTIROSO ============================
		// Ela nasceu afirmando o oposto -- que "Sa Gas" seria RECUSADO --, porque o cabecalho do
		// `NomeReservado` dizia que o saneamento "troca tudo por '_' e junta". Ele nao junta: troca um
		// por um, e "Sa Gas" vira `sa_gas.json`, que e outro arquivo. A bancada reprovou, o comentario
		// foi corrigido, e a afirmacao virou esta -- que documenta o comportamento REAL.
		//
		// Ela vale a pena porque e dessa regra que sai a garantia de que `planetas-mortos.json` e
		// `missoes-de-cargo.json` sao inalcancaveis: nome de conta nunca produz hifen.
		// ============================================================================================
		AfirmarWt("o saneamento TROCA e nao junta ('Sa Gas' -> sa_gas.json, inofensivo)",
				  !AccountStore.NomeReservado("Sa Gas") && AccountStore.NomeDeArquivo("Sa Gas") == "sa_gas");
		AfirmarWt("nome de conta nunca produz hifen (o que protege os dois arquivos com hifen)",
				  !AccountStore.NomeDeArquivo("planetas-mortos").Contains('-'));

		AfirmarWt("o Login continua aceitando um nome comum", !AccountStore.NomeReservado("goku"));
	}

	// =====================================================================
	// 2. O ARQUIVO ORFAO -- a trava
	// =====================================================================
	/// <summary>
	/// VARRE A PASTA DE VERDADE e reprova arquivo sem dono. NAO ESCREVE NADA.
	///
	/// A pergunta e feita ao contrario de propósito: nao "cada inscrito tem o arquivo dele?" (que
	/// passaria com um arquivo a mais na pasta), e sim "cada arquivo da pasta tem um inscrito?".
	/// A primeira pergunta e sobre a lista; so a segunda e sobre a REALIDADE.
	/// </summary>
	private void SecaoDoArquivoOrfao()
	{
		GD.Print("[wipe] -- 2. arquivo orfao na pasta de saves --");

		List<string> orfaos = AcharOrfaos(gritar: true);
		AfirmarWt("nenhum arquivo da pasta esta sem dono", orfaos.Count == 0,
				  orfaos.Count > 0 ? string.Join(", ", orfaos.Take(5)) : "");

		// ---------------------------------------------------------- e a trava DISPARA?
		// ============================ UMA TRAVA QUE NUNCA DISPAROU NAO ESTA PROVADA ============================
		// A afirmacao de cima passa hoje porque a pasta esta limpa -- e passaria igualzinho se o
		// `Meu()` respondesse "meu" pra tudo, que e como a trava morreria sem ninguem notar. E o
		// mesmo cego que este projeto ja registrou noutra bancada: "as duas telas concordam" fica
		// verde com as duas erradas.
		//
		// Entao aqui a pasta e MONTADA com um arquivo que ninguem declarou, e a trava tem que
		// aponta-lo -- e SO ele. Numa pasta de mentira, porque criar um `.json` estranho na de
		// verdade faria o painel de admin de outra sessao cuspir "conta ilegivel" no meio do teste.
		// ==================================================================================================
		string caixa = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
			"jandirus-orfaoteste-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
		AccountStore real = _store!;
		try
		{
			System.IO.Directory.CreateDirectory(caixa);

			// UMA CONTA DE VERDADE (tem que ser reconhecida pelo crivo forte -- desserializa, tem
			// nome, e o nome saneado bate com o arquivo), um arquivo de sistema declarado, o log
			// preservado, e o INTRUSO.
			System.IO.File.WriteAllText(System.IO.Path.Combine(caixa, "goku.json"),
				"""{ "Conta": "Goku", "Sal": "", "Hash": "" }""");
			System.IO.File.WriteAllText(System.IO.Path.Combine(caixa, "mundo.json"), "[]");
			System.IO.File.WriteAllText(System.IO.Path.Combine(caixa, "admin.log"), "linha\n");
			System.IO.File.WriteAllText(System.IO.Path.Combine(caixa, "clima.json"), "{}");
			// E O `.tmp` DE UMA ESCRITA INTERROMPIDA: ele e do dono do nome de baixo, e nao um orfao
			// -- senao toda queda de processo no meio de um save deixaria a bancada vermelha.
			System.IO.File.WriteAllText(System.IO.Path.Combine(caixa, "sagas.json.tmp"), "{}");

			_store = new AccountStore(caixa);
			List<string> achados = AcharOrfaos(gritar: false);

			AfirmarWt("a trava DISPARA no arquivo que ninguem declarou",
					  achados.Contains("clima.json"),
					  "sem isto, um `clima.json` novo sobrevive a limpeza e o mundo 'novo' nasce "
					+ "lembrando do velho");
			AfirmarWt("...e SO nele (conta, arquivo de sistema, log e .tmp tem dono)",
					  achados.Count == 1, string.Join(", ", achados));
		}
		catch (Exception e) { AfirmarWt("consegui montar a pasta de mentira da trava", false, e.Message); }
		finally
		{
			_store = real;
			try { System.IO.Directory.Delete(caixa, recursive: true); } catch { /* temporario */ }
		}
	}

	/// <summary>
	/// OS ARQUIVOS DA PASTA DO `_store` QUE NENHUM INSCRITO REIVINDICA.
	///
	/// A pergunta e feita ao contrario de proposito: nao "cada inscrito tem o arquivo dele?" (que
	/// passaria com um arquivo a mais na pasta), e sim "cada arquivo da pasta tem um inscrito?".
	/// A primeira pergunta e sobre a lista; so a segunda e sobre a REALIDADE.
	/// </summary>
	private List<string> AcharOrfaos(bool gritar)
	{
		var orfaos = new List<string>();
		int contas = 0, deSistema = 0, preservados = 0;

		foreach (string caminho in _store!.TodosOsArquivos())
		{
			string nome = System.IO.Path.GetFileName(caminho);
			SistemaDoMundo? dono = _sistemasDoMundo.Find(s => s.Meu(nome));
			if (dono == null) { orfaos.Add(nome); continue; }

			if (dono.Preservar) preservados++;
			else if (dono.Arquivos.Length == 0) contas++;   // as contas nao tem nome fixo
			else deSistema++;
		}

		if (gritar)
		{
			GD.Print($"[wipe]         {contas} conta(s), {deSistema} arquivo(s) de sistema, "
				   + $"{preservados} preservado(s), {orfaos.Count} orfao(s)");
			foreach (string o in orfaos.Take(20))
				GD.PrintErr($"[wipe]         orfao: '{o}' -- nenhum sistema o reivindica. "
						  + "Inscreva-o em RegistrarSistemasDoMundo (Arquivos + Contar + Zerar), "
						  + "senao ele SOBREVIVE a limpeza total e o mundo novo nasce com memoria do velho.");
		}
		return orfaos;
	}

	// =====================================================================
	// 3. O PORTAO DE GRAVACAO -- a medida
	// =====================================================================
	/// <summary>
	/// O QUE O DONO MANDOU MEDIR: *"a sessao viva regrava o save que voce acabou de apagar, e a
	/// limpeza se desfaz sozinha. Meça isso e resolva."*
	///
	/// A medida e simetrica de proposito, e a primeira metade e tao importante quanto a segunda: um
	/// portao que trava SEMPRE tambem faria esta secao passar pela metade -- e o sintoma seria um
	/// servidor que nunca mais grava progresso nenhum, calado.
	/// </summary>
	private void SecaoDoPortaoDeGravacao()
	{
		GD.Print("[wipe] -- 3. o portao de gravacao (`_limpezaEmCurso`) --");

		const string conta = "bancada_do_wipe";
		string caminho = System.IO.Path.Combine(_store!.Pasta, conta + ".json");

		// O CORPO E MINIMO de proposito: o que esta secao mede e o `if` do `Persistir`, e nao o
		// conteudo do save. Um corpo completo (racas, forma, livro) so acrescentaria motivos de esta
		// secao quebrar por coisas que nao sao dela.
		var pl = new ServerPlayer
		{
			Id = -910_001,
			Peer = null,
			Name = "BancadaDoWipe",
			Conta = conta,
			Slot = 0,
			Ficha = new Fighter { Name = "BancadaDoWipe", BP = 1 },
		};
		var acc = new AccountSave { Conta = conta, CriadaEm = NowMs() };

		bool antes = _limpezaEmCurso;
		try
		{
			// ------------------------------------------------------ portao ABERTO: grava
			_limpezaEmCurso = false;
			try { System.IO.File.Delete(caminho); } catch { /* nao existia */ }
			Persistir(pl, acc);
			AfirmarWt("com o portao aberto, Persistir GRAVA", System.IO.File.Exists(caminho),
					  "se isto falha, a segunda metade passa de graca");

			// ------------------------------------------------------ portao FECHADO: nao grava
			try { System.IO.File.Delete(caminho); } catch { /* ja apagado */ }
			_limpezaEmCurso = true;
			Persistir(pl, acc);
			AfirmarWt("com o portao fechado, Persistir NAO grava", !System.IO.File.Exists(caminho),
					  "sem esta guarda, derrubar todo mundo RECRIA as contas no `Drop` e a limpeza "
					+ "se desfaz no proprio gesto de fazer a limpeza");
		}
		finally
		{
			_limpezaEmCurso = antes;
			try { System.IO.File.Delete(caminho); } catch { /* ok */ }
		}
	}

	// =====================================================================
	// 3b. A CONFIRMACAO
	// =====================================================================
	/// <summary>
	/// AS QUATRO PORTAS DA CONFIRMACAO -- *"a confirmacao tem que ser dificil de dar por acidente"*.
	///
	/// ============================ ELA RODA NUMA PASTA DE MENTIRA, E O MOTIVO E O PROPRIO TESTE ============================
	/// Aqui se chama o verb que APAGA O SERVIDOR, de proposito com o codigo errado, esperando que ele
	/// recuse. E se ele nao recusar? Se a guarda estiver quebrada -- que e exatamente o defeito que
	/// esta secao existe pra pegar --, ele apaga.
	///
	/// Uma bancada que so descobre o furo depois de cair nele nao pode cair na pasta de verdade. Entao
	/// o `_store` aponta pro temporario, e a afirmacao e "a pasta continua com os arquivos" -- que e a
	/// prova de que nada rodou. Vermelho aqui custa um diretorio no temp; na pasta real custaria o
	/// servidor do dono.
	/// ================================================================================================================
	/// </summary>
	private void SecaoDaConfirmacao()
	{
		GD.Print("[wipe] -- 3b. as portas da confirmacao --");

		string caixa = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
			"jandirus-confirmateste-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
		AccountStore real = _store!;
		string codigoAntes = _limpezaCodigo, pediuAntes = _limpezaPedidaPor;
		long venceAntes = _limpezaVenceEm;
		List<string>? escutaAntes = EscutaDeAvisos;

		try
		{
			System.IO.Directory.CreateDirectory(caixa);
			System.IO.File.WriteAllText(System.IO.Path.Combine(caixa, "sentinela.json"), "{}");
			_store = new AccountStore(caixa);

			int Arquivos() => _store!.TodosOsArquivos().Count();

			// O ADMIN DE MENTIRA. Sem `Peer`, entao o `Avisar` nao manda nada no fio -- e a `Escuta`
			// e como a bancada le o que ele teria ouvido.
			var adm = new ServerPlayer
			{
				Id = -910_002, Peer = null, Name = "AdminDaBancada",
				Conta = "adm_da_bancada", Slot = 0,
				Ficha = new Fighter { Name = "AdminDaBancada", BP = 1 },
			};

			// ------------------------------------------------------ porta 1: sem previa
			EscutaDeAvisos = [];
			_limpezaCodigo = _limpezaPedidaPor = ""; _limpezaVenceEm = 0;
			AdminLimparServidor(adm, "ABCD");
			AfirmarWt("sem previa, o verb recusa", Arquivos() == 1,
					  "um comando decorado nao pode apagar o servidor sozinho");
			AfirmarWt("...e diz por que", EscutaDeAvisos.Any(t => t.Contains("previa")));

			// ------------------------------------------------------ porta 2: codigo errado
			EscutaDeAvisos = [];
			_limpezaCodigo = "WXYZ"; _limpezaPedidaPor = adm.Conta; _limpezaVenceEm = NowMs() + 60_000;
			AdminLimparServidor(adm, "ABCD");
			AfirmarWt("com o codigo errado, o verb recusa", Arquivos() == 1);
			AfirmarWt("...e diz que o codigo esta errado",
					  EscutaDeAvisos.Any(t => t.Contains("codigo errado")));

			// ------------------------------------------------------ porta 3: codigo vencido
			// UM PAINEL ESQUECIDO ABERTO NAO E UMA ARMA CARREGADA: o codigo certo, um minuto depois,
			// nao vale mais -- e a previa e refeita, com a contagem de agora.
			EscutaDeAvisos = [];
			_limpezaCodigo = "WXYZ"; _limpezaPedidaPor = adm.Conta; _limpezaVenceEm = NowMs() - 1;
			AdminLimparServidor(adm, "WXYZ");
			AfirmarWt("com o codigo CERTO mas vencido, o verb recusa", Arquivos() == 1);
			AfirmarWt("...e a previa e descartada", _limpezaCodigo.Length == 0);

			// ------------------------------------------------------ porta 4: outra conta
			// QUEM PEDIU E QUEM CONFIRMA. Senao um admin veria a lista e outro apertaria o gatilho
			// com um codigo que ele nao leu -- e a contagem da tela de um nao seria o que o outro
			// apagou.
			EscutaDeAvisos = [];
			_limpezaCodigo = "WXYZ"; _limpezaPedidaPor = "outro_adm"; _limpezaVenceEm = NowMs() + 60_000;
			AdminLimparServidor(adm, "WXYZ");
			AfirmarWt("com o codigo de OUTRA conta, o verb recusa", Arquivos() == 1);
			AfirmarWt("...e manda pedir a propria", EscutaDeAvisos.Any(t => t.Contains("outra conta")));

			// ------------------------------------------------------ a previa em si
			EscutaDeAvisos = [];
			_limpezaCodigo = _limpezaPedidaPor = ""; _limpezaVenceEm = 0;
			AdminPrepararLimpeza(adm);
			AfirmarWt("a previa NAO apaga nada", Arquivos() == 1,
					  "o passo 1 e so olhar -- se ele apagar, nao ha confirmacao nenhuma");
			AfirmarWt("a previa sorteia um codigo", _limpezaCodigo.Length == 4, _limpezaCodigo);
			AfirmarWt("...que so tem caracteres sem ambiguidade (sem O/0/I/1)",
					  _limpezaCodigo.All(c => AlfabetoDoCodigo.Contains(c)), _limpezaCodigo);
			AfirmarWt("...com prazo pra vencer", _limpezaVenceEm > NowMs());

			// E O CODIGO E DIFERENTE A CADA PEDIDO: se fosse fixo, daria pra decorar de ontem, e a
			// segunda porta viraria enfeite.
			string primeiro = _limpezaCodigo;
			bool repetiu = false;
			for (int i = 0; i < 8 && !repetiu; i++)
			{
				AdminPrepararLimpeza(adm);
				repetiu = _limpezaCodigo == primeiro;
			}
			AfirmarWt("o codigo muda a cada previa (nao da pra decorar)", !repetiu, primeiro);
		}
		catch (Exception e) { AfirmarWt("a secao da confirmacao rodou inteira", false, e.ToString()); }
		finally
		{
			EscutaDeAvisos = escutaAntes;
			_store = real;
			_limpezaCodigo = codigoAntes; _limpezaPedidaPor = pediuAntes; _limpezaVenceEm = venceAntes;
			try { System.IO.Directory.Delete(caixa, recursive: true); } catch { /* temporario */ }
		}
	}

	// =====================================================================
	// 4. A LIMPEZA DE VERDADE
	// =====================================================================
	private void SecaoDaLimpezaDeVerdade()
	{
		GD.Print("[wipe] -- 4. a limpeza de verdade (com copia e devolucao) --");

		// ---------------------------------------------------------- a foto de antes
		var antes = new Dictionary<string, int>();
		foreach (SistemaDoMundo s in _sistemasDoMundo)
		{
			if (s.Preservar) continue;
			try { antes[s.Nome] = s.Contar(); } catch { antes[s.Nome] = -1; }
		}
		int mobiliaAntes = _noChao.Count(o => o.DoMapa);

		// ---------------------------------------------------------- a pasta de mentira
		// ============================ A PASTA DE VERDADE NUNCA E TOCADA ============================
		// A primeira versao desta secao apagava a pasta real e restaurava de uma copia. Funcionava --
		// e era irresponsavel por duas razoes que so aparecem fora do papel: nesta maquina rodam
		// VARIAS bancadas ao mesmo tempo (`--host` de outra sessao grava conta na mesma pasta no meio
		// da janela), e um travamento entre o apagar e o devolver custaria o mundo do dono.
		//
		// Entao a secao 4 aponta o `_store` pra um TEMPORARIO semeado com uma copia fiel do mundo, e
		// e o temporario que e apagado. O que se mede continua sendo o de producao: o `ExecutarLimpeza`
		// e o mesmo, a MEMORIA e a memoria de verdade (carregada do mundo de verdade no boot), e os
		// `Carregar*` da devolucao sao os mesmos do boot. So o chao muda.
		//
		// E o `_store` inteiro E o suficiente pra mudar de chao: os doze arquivos do mundo perguntam a
		// ele onde escrever. Era `onze` ate agora -- os quatro `.txt` de cargo tinham o caminho
		// cravado, e por isso esta secao tambem so passou a ser honesta depois de conserta-los
		// (ver `GameServer.Ranks.cs`).
		// ======================================================================================
		string caixa = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
			"jandirus-wipeteste-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
		var arquivos = new List<string>();
		AccountStore real = _store!;
		try
		{
			System.IO.Directory.CreateDirectory(caixa);
			foreach (string f in real.TodosOsArquivos())
			{
				System.IO.File.Copy(f, System.IO.Path.Combine(caixa, System.IO.Path.GetFileName(f)), overwrite: true);
				arquivos.Add(System.IO.Path.GetFileName(f));
			}
		}
		catch (Exception e)
		{
			// SEM A COPIA NAO HA O QUE MEDIR: uma limpeza contra uma pasta vazia passaria de graca
			// (tudo ja e zero) e diria exatamente nada.
			AfirmarWt("consegui semear a pasta de mentira com o mundo de verdade", false, e.Message);
			return;
		}
		GD.Print($"[wipe]         {arquivos.Count} arquivo(s) copiados para a caixa: {caixa}");
		GD.Print($"[wipe]         a pasta de verdade ({real.Pasta}) NAO sera tocada");

		try
		{
			_store = new AccountStore(caixa);
			// ------------------------------------------------------ a limpeza de PRODUCAO
			ResultadoDaLimpeza r = ExecutarLimpeza(null);

			AfirmarWt("nenhum erro de disco", r.Erros.Count == 0,
					  r.Erros.Count > 0 ? string.Join("; ", r.Erros) : "");

			// A MEMORIA
			foreach (SistemaDoMundo s in _sistemasDoMundo)
			{
				if (s.Preservar) continue;
				int agora;
				try { agora = s.Contar(); } catch (Exception e) { AfirmarWt($"'{s.Nome}' conta depois do wipe", false, e.Message); continue; }
				AfirmarWt($"'{s.Nome}' zerou na memoria", agora == 0,
						  $"tinha {antes.GetValueOrDefault(s.Nome)}, ficou {agora}");
			}

			// A MOBILIA DO MAPA E CONTEUDO, e continua de pe. Esta e a fronteira do pedido: "nao
			// apague conteudo do jogo". Um `_noChao.Clear()` seco faria esta linha ficar vermelha --
			// e o servidor "novo" nasceria sem banco, sem bancada e sem sala de gravidade.
			AfirmarWt("a mobilia do mapa NAO foi apagada", _noChao.Count(o => o.DoMapa) == mobiliaAntes,
					  $"tinha {mobiliaAntes}, ficou {_noChao.Count(o => o.DoMapa)}");

			// A CADEIA DE SAGAS VOLTA ADORMECIDA, e nao VAZIA: o tique procura o estado de cada elo
			// do `npcs.json`, e uma lista vazia faria a primeira saga do mundo novo nunca comecar.
			AfirmarWt("a cadeia de sagas volta adormecida (e nao vazia)",
					  _sagas.Elos.Count == _cadeia.Length,
					  $"{_sagas.Elos.Count} elo(s) pra uma cadeia de {_cadeia.Length}");

			// O DISCO
			var sobraram = _store.TodosOsArquivos().Select(System.IO.Path.GetFileName).ToList();
			bool soLog = sobraram.All(n => string.Equals(n, "admin.log", StringComparison.OrdinalIgnoreCase));
			AfirmarWt("a pasta so tem o admin.log depois do wipe", soLog,
					  soLog ? "" : string.Join(", ", sobraram.Take(8)));
			AfirmarWt("o admin.log SOBREVIVEU (e a testemunha do wipe)",
					  !arquivos.Contains("admin.log")
					  || sobraram.Any(n => string.Equals(n, "admin.log", StringComparison.OrdinalIgnoreCase)));
			AfirmarWt("a pasta continua existindo", System.IO.Directory.Exists(_store.Pasta));

			AfirmarWt("ninguem sobrou logado", _players.Count == 0 && _contas.Count == 0 && _logados.Count == 0,
					  $"{_players.Count} corpo(s), {_contas.Count} conta(s), {_logados.Count} na selecao");
			AfirmarWt("o portao voltou a abrir depois da limpeza", !_limpezaEmCurso,
					  "travado, o servidor nunca mais grava progresso -- e calado");
		}
		finally
		{
			// ------------------------------------------------------ a devolucao
			// O `_store` VOLTA PRA PASTA DE VERDADE antes de qualquer coisa: e dela que a memoria do
			// servidor tem que ser reconstruida, e e nela que ele vai gravar quando alguem entrar.
			// Um `finally` que esquecesse esta linha deixaria o servidor rodando a sessao inteira
			// gravando dentro de um temporario -- e o dono so descobriria na proxima vez que abrisse
			// o jogo e nao achasse o personagem.
			_store = real;

			RecarregarMundoDoDisco();
			GD.Print($"[wipe]         mundo recarregado da pasta de verdade ({real.Pasta})");

			// A CAIXA SOME. Ela tem copia de conta (com sal e hash dentro) e nao tem por que
			// envelhecer no temporario da maquina de ninguem.
			try { System.IO.Directory.Delete(caixa, recursive: true); }
			catch (Exception e) { GD.PushWarning($"[wipe] nao apaguei a caixa {caixa}: {e.Message}"); }

			// A CONTAGEM TEM QUE BATER COM A DE ANTES. Esta e a afirmacao mais barata da secao e a
			// que pega mais coisa: se um `Zerar` esqueceu um contentor, a recarga SOMA por cima do
			// que sobrou e o numero fica maior; se um carregador nao voltou, fica menor.
			bool bateu = true;
			foreach (SistemaDoMundo s in _sistemasDoMundo)
			{
				if (s.Preservar || !antes.TryGetValue(s.Nome, out int n) || n < 0) continue;
				int agora;
				try { agora = s.Contar(); } catch { continue; }
				if (agora == n) continue;
				bateu = false;
				GD.PrintErr($"[wipe]         '{s.Nome}': era {n}, voltou {agora}");
			}
			AfirmarWt("o mundo voltou exatamente como estava", bateu);
		}
	}

	/// <summary>
	/// RECARREGA O MUNDO DO DISCO -- os carregadores DE PRODUCAO, um por arquivo.
	///
	/// ============================ POR QUE NAO O `CarregarTech` INTEIRO ============================
	/// Porque ele faz duas coisas: le o `mundo.json` E registra a MOBILIA DO MAPA a partir dos
	/// `.objetos`. A mobilia nunca foi apagada (ela e conteudo -- ver `ZerarConstrucoes`), entao
	/// chamar o carregador inteiro a DUPLICARIA: dois bancos por cima um do outro, para sempre.
	///
	/// Os outros treze podem ser chamados de novo tal como estao: sete ja dao `Clear()` antes de ler,
	/// e os quatro que enchem sem limpar (`mundo`, `naves`, `conquista`, `reputacao`) so sao seguros
	/// aqui porque o `Zerar()` acabou de esvazia-los -- e essa dependencia e justamente o que a
	/// contagem final da secao 4 vigia.
	/// ==========================================================================================
	///
	/// ============================ AS DUAS DAS ESFERAS TIVERAM QUE APRENDER A LIMPAR ============================
	/// `CarregarEsferas` e `CarregarSupers` estariam na terceira familia -- a dos que enchem sem limpar
	/// -- e nela **nao dava** pra deixa-los, porque o `Zerar` deles nao termina vazio de proposito: as
	/// sete Super voltam livres e o Porunga de Namek volta de pe. Uma recarga por cima disso duplicaria o
	/// set eterno (dois `Id = 0`) e daria catorze Super Esferas. Os dois agora esvaziam antes de ler, e o
	/// porque esta escrito neles.
	/// ========================================================================================================
	///
	/// SO A BANCADA CHAMA. O jogo nunca recarrega o mundo com o servidor de pe -- e essa e a razao
	/// de o `Zerar()` existir em vez de "apagar o disco e reler tudo".
	/// </summary>
	private void RecarregarMundoDoDisco()
	{
		// A SEMENTE NA FRENTE, e ela e a razao de esta lista ter treze linhas e nao doze: sem ela, a
		// devolucao do `finally` traria de volta as obras e os dominios do mundo do dono **dentro do
		// universo que o `ZerarSemente` acabou de sortear** -- o servidor seguiria a sessao inteira
		// achando que a Terra fica noutro lugar. Ela le o `universo.json` da pasta pra onde o `_store`
		// esta apontando AGORA, que e como a devolucao recupera a semente de verdade.
		CarregarSemente();
		CarregarMundoDeDisco(CaminhoDoMundo);
		CarregarNaves();
		RefazerColisaoDeTudo();
		CarregarSagas();
		CarregarReputacao();
		CarregarPlanetasMortos();
		CarregarConquista();

		// AS ESFERAS DEPOIS DA CONQUISTA, como no boot (`CarregarVisual`): `ErguerEstatua` pergunta ao
		// livro dos dominios quem manda no planeta, e o set eterno precisa do catalogo pra saber que
		// Namek existe. As Super vao junto por serem o mesmo sistema pro jogador.
		CarregarEsferas();
		CarregarSupers();

		CarregarCargos();
		CarregarMestres();
		CarregarHerdeiros();
		CarregarTitulo();
		CarregarMissoes();
	}

	// =====================================================================
	// O RETRATO -- a ferramenta das secoes 5 a 8
	// =====================================================================
	/// <summary>
	/// O ESTADO PERSISTENTE DO SERVIDOR, COMO CONJUNTO.
	///
	/// ============================ POR QUE CONJUNTO, E NAO LISTA DE ARQUIVOS ============================
	/// Uma bancada que afirmasse "sobrou o `mundo.json`? e o `naves.json`? e o `cargos.txt`?" estaria
	/// escrevendo a mao exatamente a lista que este trabalho inteiro existe pra nao ter. O arquivo que
	/// nascer amanha escaparia da limpeza E da conferencia, e a bancada ficaria **verde mentindo** --
	/// que e pior do que vermelha, porque desliga a desconfianca.
	///
	/// Entao o retrato tem duas metades e as DUAS sao derivadas:
	///
	///   * O DISCO, varrido CRU (`TodosOsArquivos`) -- sem filtro, sem lista, sem excecao. Todo
	///     arquivo que existir aparece aqui, tenha dono ou nao, tenha nascido hoje ou ano que vem.
	///     E com o TAMANHO, porque "o arquivo existe" nao e a pergunta: um `naves.json` de 2 bytes
	///     (`[]`) e um mundo limpo, e um de 300 e um mundo que lembrou da frota antiga.
	///   * A MEMORIA, iterando os inscritos. Um sistema novo aparece aqui assim que se inscreve.
	///
	/// AS DUAS METADES SE COBREM, e essa e a razao de serem duas: um sistema que NAO se inscreveu
	/// escapa da metade da memoria (ninguem pergunta por ele) -- mas o arquivo dele nao escapa da
	/// varredura crua do disco. E o que a secao 6 prova, disparando exatamente esse defeito.
	/// ================================================================================================
	///
	/// NADA E EXCLUIDO, nem o `admin.log`. A tentacao era pular o que esta marcado `Preservar`, e ela
	/// e uma armadilha: a regra do retrato passaria a ser a MESMA regra que decide o que apagar, e
	/// marcar um arquivo como preservado o esconderia dos dois lados de uma vez. Preservando o log nos
	/// DOIS lados da comparacao (ver `SemearPrimeiroBoot`), o retrato **prova** a preservacao em vez de
	/// assumi-la: se a limpeza comesse o `admin.log`, o retrato ficaria vermelho.
	/// </summary>
	private List<string> RetratoDoMundo()
	{
		var linhas = new List<string>();

		foreach (string caminho in _store!.TodosOsArquivos())
		{
			long tam;
			try { tam = new System.IO.FileInfo(caminho).Length; } catch { tam = -1; }
			linhas.Add($"disco    {System.IO.Path.GetFileName(caminho)} ({tam} b)");
		}

		foreach (SistemaDoMundo s in _sistemasDoMundo)
		{
			if (s.Preservar) continue;
			int n;
			try { n = s.Contar(); }
			catch (Exception e) { linhas.Add($"memoria  {s.Nome} = ERRO({e.GetType().Name})"); continue; }
			linhas.Add($"memoria  {s.Nome} = {n}");
		}

		linhas.Sort(StringComparer.Ordinal);
		return linhas;
	}

	/// <summary>Imprime a diferenca entre dois retratos, dos dois lados. Vazio = iguais.</summary>
	private static List<string> Diferenca(List<string> a, List<string> b)
	{
		var d = new List<string>();
		foreach (string l in a) if (!b.Contains(l)) d.Add($"so no primeiro:  {l}");
		foreach (string l in b) if (!a.Contains(l)) d.Add($"so no segundo:   {l}");
		return d;
	}

	/// <summary>
	/// O SERVIDOR ESCREVE TUDO O QUE ELE LEMBRA -- os catorze escritores DE PRODUCAO.
	///
	/// ============================ ESTA LISTA E A MAO, E E DE PROPOSITO ============================
	/// Ela e a UNICA lista escrita a mao neste trabalho, e ela tem que ser, porque e a
	/// CONTRAPROVA: se ela fosse derivada do registro (um campo `Gravar` no `SistemaDoMundo`), entao
	/// esconder um sistema do registro esconderia junto o escritor dele -- o arquivo nunca voltaria
	/// pro disco e a secao 6 ficaria verde em cima do defeito que ela existe pra pegar.
	///
	/// Duas travas independentes a impedem de apodrecer:
	///   * a secao 5 afirma que TODO arquivo declarado no registro aparece no disco depois desta
	///     passada -- um sistema novo que ninguem puser aqui deixa a secao 5 vermelha;
	///   * a secao 2 (`AcharOrfaos`) pega o caminho contrario -- um arquivo na pasta que ninguem
	///     declarou.
	/// ==========================================================================================
	///
	/// E ISTO NAO E ARTIFICIO DE BANCADA: e o que o servidor faz sozinho o tempo todo. Cada um destes
	/// escritores e chamado na mudanca do sistema dele, e o autosave repassa por eles. "Zerei o disco
	/// mas a memoria lembrou" **nao e um estado estavel** -- ele dura ate a proxima gravacao, e ai o
	/// arquivo volta. Forcar a passada aqui e so antecipar o inevitavel pra dentro da medicao.
	/// </summary>
	private void ForcarGravacaoDeTudo()
	{
		// A SEMENTE PRIMEIRO, porque ela e o endereco de tudo o que vem depois. E ela e o unico
		// escritor desta lista que a bancada exercita em TRES momentos diferentes de proposito
		// (primeiro boot, mundo jogado, mundo limpo) -- ver a nota de largura fixa na
		// `FichaDoUniverso`, que e o que faz os tres retratos serem comparaveis.
		SalvarSemente();
		GravarMundo();
		GravarNaves();
		SalvarSagas();
		SalvarReputacao();
		SalvarConquista();
		SalvarPlanetasMortos();
		SalvarEsferas();
		SalvarSupers();
		SalvarMissoes();
		SalvarCargos();
		SalvarMestres();
		SalvarHerdeiros();
		SalvarTitulo();
		// AS CONTAS NAO TEM ESCRITOR COLETIVO -- cada uma e gravada por `Persistir`/`_store.Gravar`,
		// e o `Persistir` esta medido na secao 3. Quem semeia conta aqui grava na hora.
	}

	/// <summary>
	/// Zera a memoria de todos os inscritos SEM tocar no disco -- o avesso do `Carregar*`.
	///
	/// ============================ "SEM TOCAR NO DISCO" PRECISOU DE UMA LINHA PRA CONTINUAR VERDADE ============================
	/// Ela era verdade por acaso: nenhum `Zerar` gravava nada. O `Zerar` das esferas quebrou isso, porque
	/// ele nao termina vazio -- a passada de baixo re-ergue o Porunga de Namek, e erguer passa por
	/// `SalvarEsferas()`.
	///
	/// E o instante em que isso aconteceria e o pior possivel: o `finally` do <see cref="NaCaixa"/>
	/// devolve o `_store` pra pasta DE VERDADE do dono **antes** de chamar esta funcao. Sem o portao, uma
	/// bancada de leitura gravaria um `esferas.json` de mundo recem-nascido por cima das estatuas do dono
	/// -- e a `--wipeteste` apagaria justamente o mundo que veio medir. Este projeto ja pagou esse preco
	/// uma vez, com o `planetas-mortos.json`.
	///
	/// O portao e o MESMO de producao (`_limpezaEmCurso`), e nao um segundo eixo: e ele que `SalvarEsferas`
	/// e `SalvarSupers` consultam, e esta funcao e literalmente o passo 3 da limpeza rodando sozinho.
	/// Guardado e devolvido, porque o `ExecutarLimpeza` pode estar por fora.
	/// ====================================================================================================================
	/// </summary>
	private void ZerarSoAMemoria()
	{
		bool portaoAntes = _limpezaEmCurso;
		_limpezaEmCurso = true;
		try
		{
			foreach (SistemaDoMundo s in _sistemasDoMundo)
			{
				if (s.Preservar) continue;
				try { s.Zerar(); } catch (Exception e) { GD.PushWarning($"[wipe] Zerar de '{s.Nome}': {e.Message}"); }
			}
			RemontarOMundoNovo();
		}
		finally { _limpezaEmCurso = portaoAntes; }
	}

	/// <summary>
	/// RODA UM CORPO COM O `_store` NUMA PASTA TEMPORARIA, e devolve o mundo de verdade no fim.
	///
	/// A pasta REAL do dono nunca e tocada por nenhuma destas secoes -- elas apagam servidor de
	/// mentira. A devolucao zera a memoria e recarrega dos arquivos de verdade: sem o `Zerar` na
	/// frente, os quatro carregadores que enchem sem limpar (`mundo`, `naves`, `conquista`,
	/// `reputacao`) DUPLICARIAM o mundo do dono a cada secao.
	/// </summary>
	private void NaCaixa(string etiqueta, Action<string> corpo)
	{
		string caixa = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
			$"jandirus-{etiqueta}-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
		AccountStore real = _store!;
		try
		{
			System.IO.Directory.CreateDirectory(caixa);
			_store = new AccountStore(caixa);
			corpo(caixa);
		}
		catch (Exception e) { AfirmarWt($"a secao '{etiqueta}' rodou inteira", false, e.ToString()); }
		finally
		{
			_store = real;
			ZerarSoAMemoria();
			RecarregarMundoDoDisco();
			try { System.IO.Directory.Delete(caixa, recursive: true); } catch { /* temporario */ }
		}
	}

	/// <summary>
	/// O PRIMEIRO BOOT: memoria zerada e os carregadores DE PRODUCAO rodando numa pasta vazia.
	///
	/// E o que o servidor faz na primeira vez que sobe numa maquina limpa, e nao uma imitacao disso:
	/// os mesmos `Carregar*` do `CarregarVisual`, cada um achando o arquivo dele ausente. O retrato
	/// tirado daqui e a REFERENCIA contra a qual a limpeza e julgada -- e ele nao passa por lugar
	/// nenhum do codigo da limpeza, que e o que impede a comparacao de ser circular.
	///
	/// O `admin.log` e semeado de proposito: e a unica coisa que a limpeza pode deixar pra tras, e
	/// po-lo nos dois lados faz o retrato PROVAR a preservacao (ver `RetratoDoMundo`).
	/// </summary>
	private void SemearPrimeiroBoot()
	{
		System.IO.File.WriteAllText(System.IO.Path.Combine(_store!.Pasta, "admin.log"),
			"a testemunha que atravessa a limpeza\n");
		ZerarSoAMemoria();
		RecarregarMundoDoDisco();
	}

	/// <summary>
	/// JOGAR: acender TODOS os sistemas do mundo, e nao um deles.
	///
	/// ============================ POR QUE TODOS, E NAO "UM EXEMPLO" ============================
	/// A comparacao "primeiro boot == depois da limpeza" so vale pro que MUDOU no meio. Um sistema
	/// que a bancada nunca acendeu tem o mesmo valor nos tres retratos por nao ter sido tocado --
	/// e passaria verde com o `Zerar` dele quebrado, apagado, ou nunca escrito. Semear os dezesseis e
	/// o que faz o retrato C ser uma afirmacao sobre os dezesseis.
	///
	/// E ISSO NAO E TEORIA: as esferas do dragao e as Super Esferas passaram semanas inteiramente fora
	/// desta lista, e a secao 5 ficou VERDE o tempo todo comparando um vazio com outro vazio. Os itens
	/// 15 e 16 sao o conserto desse cego, e nao enfeite.
	///
	/// E o dono pediu nominalmente sete destes: conta, construcao, conquista, saga, reputacao,
	/// tecnica customizada (ela mora DENTRO do save da conta -- ver `CharacterSave.Customizadas`) e
	/// o jogador online (secao 7).
	/// ======================================================================================
	/// </summary>
	private void SemearMundoJogado()
	{
		ZoneKey zona = _catalogo != null && _catalogo.Todas.Any()
			? ZoneKey.Premade(_catalogo.Todas.First().Zona)
			: ZoneKey.Premade("Earth");

		// ---------------------------------------------------------- 1. uma conta, com personagem e TECNICA
		// A tecnica customizada nao tem arquivo proprio: ela viaja dentro do save da conta. Entao
		// "criei uma tecnica" e uma conta que existe -- e some quando a conta some.
		var c = new CharacterSave
		{
			Nome = "JogadorDoWipe", Raca = "Saiyan", Planeta = "Vegeta", Genero = "Male",
			Idade = 20, Zona = zona.Name, ZonaTipo = zona.Kind, ZonaSeed = zona.Seed,
			Customizadas =
			[
				new Jandirus.Core.Skills.TecnicaCustomizada { Nome = "Onda do Wipe" },
			],
		};
		var acc = new AccountSave { Conta = "jogador_do_wipe", CriadaEm = NowMs() };
		acc.Slots[0] = c;
		_store!.Gravar(acc);

		// ---------------------------------------------------------- 2. uma construcao de pe
		// O TIPO E COPIADO DA MOBILIA DO MAPA pra ser um id que o catalogo conhece de verdade --
		// um tipo inventado voltaria do disco sem arte e sem parede, e a colisao nao seria exercida.
		Obra? molde = _noChao.Find(o => o.DoMapa);
		var obra = new Obra { Id = 90_001, Tipo = molde?.Tipo ?? "Research_Station", DoMapa = false };
		obra.PorZona(zona);
		_noChao.Add(obra);

		// ---------------------------------------------------------- 3. uma nave comprada
		var nave = new Nave { Id = 90_002, Tipo = "Spacepod", DonoConta = "jogador_do_wipe", DonoNome = "JogadorDoWipe" };
		nave.ZonaTipo = zona.Kind; nave.ZonaNome = zona.Name; nave.ZonaSeed = zona.Seed;
		_naves.Add(nave);

		// ---------------------------------------------------------- 4. uma saga disparada
		if (_sagas.Elos.Count > 0)
		{
			_sagas.Elos[0].Fase = Jandirus.Core.Npc.FaseDaSaga.EmCurso;
			_sagas.Elos[0].Disparou = true;
		}

		// ---------------------------------------------------------- 5. fama com um povo
		_reputacao["jogador_do_wipe"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
		{
			["Earth"] = 42.0,
		};
		_nomesDaReputacao["jogador_do_wipe"] = "JogadorDoWipe";

		// ---------------------------------------------------------- 6. um planeta dominado
		_dominios.Add(new Dominio
		{
			PreFeito = true, Planeta = zona.Name, Nome = "JogadorDoWipe",
			Conta = "jogador_do_wipe", Assinatura = "assinatura_do_wipe",
		});

		// ---------------------------------------------------------- 7. um planeta destruido
		_mortos.Por(new Jandirus.Core.World.EstadoDaMorte
		{
			Chave = "PlanetaDoWipe", Nome = "PlanetaDoWipe",
			Fase = Jandirus.Core.World.FaseDaMorte.Destruido, Motivo = "bancada da limpeza",
		});

		// ---------------------------------------------------------- 8. um dever de cargo em curso
		_missoes["Guardian"] = new Jandirus.Core.Ranks.FichaDeMissao { Dono = "assinatura_do_wipe" };
		_cofreDaTerra = 1234;

		// ---------------------------------------------------------- 9 a 12. os quatro de texto
		_tronos["Guardian"] = "assinatura_do_wipe";
		_mestreDe["assinatura_do_aluno"] = "assinatura_do_wipe";
		_nomePorAssinatura["assinatura_do_wipe"] = "JogadorDoWipe";
		_herdeirosDeVegeta.Add("assinatura_do_wipe");
		_duelo.TituloDesde = NowMs();

		// ---------------------------------------------------------- 13 e 14. os dois SEM arquivo
		// Eles nao aparecem no disco por construcao -- so na metade da memoria do retrato. Se a
		// metade da memoria nao existisse, estes dois sumiriam da medicao inteira.
		_adiantoDoCeu = 9_999;
		var chave = new Jandirus.Core.World.ChaveDePlaneta(true, zona.Name, 0);
		_invasoes[chave] = new Invasao { Chave = chave, Zona = zona, Planeta = zona.Name };

		// ---------------------------------------------------------- 15. um SET DE ESFERAS de jogador
		// ============================ AS QUATRO COISAS QUE O DONO NOMEOU, NUMA SEMEADURA SO ============================
		// A estatua (com o vinculo pro criador e o desejo supremo COMPRADO), as sete espalhadas, uma
		// esfera NA MAO de alguem, e o dragao de pe. Sem elas aqui, este sistema teria o mesmo valor nos
		// tres retratos por nunca ter sido tocado -- e a secao 5 passaria verde com o `Zerar` dele
		// quebrado, apagado ou nunca escrito. E exatamente o cego que deixou este sistema inteiro fora da
		// medicao ate agora.
		//
		// O `Ciclo`, o `Pedidos` e o `AtivoEm` sao postos na mao de proposito: sao eles que fazem o
		// arquivo do retrato B ter conteudo diferente do de um mundo recem-nascido.
		// ==========================================================================================================
		var setDoWipe = new SetDeEsferas
		{
			Id = 903,
			Ex = 640, Ey = 640,
			CriadorSig = "assinatura_do_wipe",
			CriadorConta = "jogador_do_wipe",
			CriadorNome = "JogadorDoWipe",
			Poder = 1_000_000, Desejos = 2, Pedidos = 1, Ciclo = 5,
			AtivoEm = 1_700_000_000, TemSupremo = true,
		};
		setDoWipe.PorZona(zona);
		_sets.Add(setDoWipe);

		for (int n = 1; n <= Jandirus.Core.Magic.Esferas.Total; n++)
		{
			var esfera = new Esfera { Set = setDoWipe.Id, Numero = n, X = 640 + n * 8, Y = 640 };
			esfera.PorZona(zona);
			_esferas.Add(esfera);
		}

		// A ESFERA CARREGADA. `Portador` e `[JsonIgnore]` -- ele nao vai pro disco --, entao ele so
		// aparece na medicao porque o objeto inteiro tem que sumir da MEMORIA junto com o resto.
		_esferas[^1].Portador = -910_009;
		_invocacoes[setDoWipe.Id] = new Invocacao
		{
			Set = setDoWipe.Id, Invocador = -910_009,
			Onde = new Vec2(640, 620), ExpiraEm = 1_700_000_060,
		};

		// ---------------------------------------------------------- 16. as SUPER ESFERAS presas
		// Uma reivindicada, um canal de disputa em pe, o ciclo andado, e a PROCURACAO da lingua dos
		// deuses com o pedido ja registrado -- que e o fantasma que trancava o Super Shenron pra sempre.
		// As duas ultimas linhas nao vao pro disco: elas so existem na metade da MEMORIA do retrato, e e
		// por isso que a contagem deste inscrito as soma.
		_cicloDasSupers = 3;
		if (_supers.Find(s => s.Numero == 1) is { } super1)
		{
			super1.Dono = "assinatura_do_wipe";
			super1.DonoNome = "JogadorDoWipe";
		}
		_disputasDeSuper[-910_009] = new DisputaDeSuper
		{
			Numero = 2, Quem = -910_009, Faltam = 42, Tomada = false, ProximoAviso = 0,
		};
		_benefSig = "assinatura_do_wipe";
		_benefNome = "JogadorDoWipe";
		_pedidoDoBenef = "riqueza";
		_alvoDoPedido = "JogadorDoWipe";
		_ofertasDeGuarda["jogador_do_wipe"] = new OfertaDeGuarda
		{
			DeConta = "jogador_do_wipe", DeNome = "JogadorDoWipe",
			DeAssinatura = "assinatura_do_wipe", Ate = NowMs() + 60_000,
		};
		_precoPendente = ("jogador_do_wipe", -910_009, NowMs() + 60_000);

		ForcarGravacaoDeTudo();
	}

	// =====================================================================
	// 5. O RETRATO DE CONJUNTO -- a afirmacao central
	// =====================================================================
	/// <summary>
	/// *"depois de limpar, o servidor tem que estar no MESMO estado de um primeiro boot"* -- provado
	/// por COMPARACAO, e nao por inspecao.
	///
	/// Tres retratos, e o do meio e o que da sentido aos outros dois:
	///
	///   A. PRIMEIRO BOOT -- pasta vazia, os `Carregar*` de producao rodando. A referencia.
	///   B. DEPOIS DE JOGAR -- conta, construcao, nave, saga, fama, dominio, planeta morto, dever,
	///      trono, discipulado, herdeiro, titulo, relogio adiantado e invasao. **B tem que ser
	///      DIFERENTE de A**, e essa afirmacao nao e enfeite: se o semeador nao acendesse nada, C
	///      seria igual a A por nunca ter havido o que limpar, e a bancada inteira seria uma
	///      tautologia verde.
	///   C. DEPOIS DA LIMPEZA -- e a passada de gravacao que vem DEPOIS dela.
	///
	/// **C == A, item por item.**
	///
	/// ============================ POR QUE GRAVAR DEPOIS DE LIMPAR ============================
	/// Porque "o disco esta vazio" nao e a pergunta -- ele esta vazio no instante seguinte a
	/// vassoura por construcao, ja que a vassoura varre a pasta inteira. A pergunta e se o servidor
	/// **continua vazio quando volta a escrever**, e ela so tem resposta depois de ele escrever.
	///
	/// Um sistema cujo `Zerar` falta (ou quebrou, ou nunca existiu) sobrevive intacto a esta
	/// limpeza: o arquivo dele e apagado como todos, e volta na primeira gravacao com o conteudo
	/// que a memoria guardou. Em producao isso e o autosave de dois minutos, ou a primeira mudanca
	/// naquele sistema -- ou seja, o mundo "novo" nasce limpo e volta a ficar sujo sozinho, que e
	/// exatamente o *"aparece dias depois como comportamento estranho"* do pedido.
	/// ======================================================================================
	/// </summary>
	private void SecaoDoRetratoDeConjunto()
	{
		GD.Print("[wipe] -- 5. o retrato de conjunto: limpar == primeiro boot --");

		NaCaixa("bootteste", _ =>
		{
			// ------------------------------------------------------ A. o primeiro boot
			SemearPrimeiroBoot();
			ForcarGravacaoDeTudo();
			List<string> a = RetratoDoMundo();
			GD.Print($"[wipe]         retrato A (primeiro boot): {a.Count} item(ns)");

			// ------------------------------------------------------ B. jogado
			SemearMundoJogado();
			List<string> b = RetratoDoMundo();
			GD.Print($"[wipe]         retrato B (depois de jogar): {b.Count} item(ns)");

			AfirmarWt("jogar MUDA o retrato (senao a comparacao final seria tautologia)",
					  Diferenca(a, b).Count > 0);

			// TODO ARQUIVO DECLARADO TEM QUE ESTAR NO DISCO AGORA. E a trava que impede a lista da
			// `ForcarGravacaoDeTudo` de apodrecer: um sistema novo que se inscreva e que ninguem
			// puser la deixa esta linha vermelha, com o nome do arquivo faltando.
			var faltando = new List<string>();
			foreach (SistemaDoMundo s in _sistemasDoMundo)
			{
				if (s.Preservar) continue;
				foreach (string arq in s.Arquivos)
					if (!b.Any(l => l.StartsWith($"disco    {arq} (", StringComparison.Ordinal)))
						faltando.Add($"{arq} ({s.Nome})");
			}
			AfirmarWt("todo arquivo declarado no registro esta no disco depois de jogar",
					  faltando.Count == 0, string.Join(", ", faltando));

			// ------------------------------------------------------ C. limpo
			ResultadoDaLimpeza r = ExecutarLimpeza(null);
			AfirmarWt("a limpeza nao deu erro de disco", r.Erros.Count == 0,
					  string.Join("; ", r.Erros));

			ForcarGravacaoDeTudo();
			List<string> c = RetratoDoMundo();
			GD.Print($"[wipe]         retrato C (depois de limpar): {c.Count} item(ns)");

			// ------------------------------------------------------ A AFIRMACAO
			List<string> d = Diferenca(a, c);
			foreach (string l in d.Take(20)) GD.PrintErr($"[wipe]         {l}");
			AfirmarWt("o retrato DEPOIS DA LIMPEZA e igual ao do PRIMEIRO BOOT",
					  d.Count == 0, $"{d.Count} diferenca(s)");

			// E O LOG ATRAVESSOU. Ele esta no retrato como todo o resto (nada e excluido de la),
			// entao esta linha e redundante de proposito: ela NOMEIA o que aquela comparacao ja
			// provou, pra a garantia nao depender de alguem reparar no item certo da lista.
			AfirmarWt("o admin.log atravessou os tres retratos",
					  c.Any(l => l.StartsWith("disco    admin.log", StringComparison.Ordinal)));
		});
	}

	// =====================================================================
	// 6. A INJECAO -- a trava provada disparando
	// =====================================================================
	/// <summary>
	/// ESCONDER UM ARQUIVO DA LISTA, e ver a comparacao de conjunto pega-lo.
	///
	/// ============================ POR QUE ESTE DEFEITO, E NAO OUTRO ============================
	/// Porque e o unico que a limpeza NAO pega sozinha, e por isso e o unico que valia injetar.
	///
	/// A vassoura do disco varre a pasta inteira: um arquivo que ninguem declarou e apagado do mesmo
	/// jeito. Ou seja, esconder um sistema do registro **nao deixa lixo no disco** -- deixa lixo na
	/// MEMORIA, calado, e o disco volta a sujar sozinho na primeira gravacao. Nenhuma inspecao da
	/// pasta logo depois da limpeza acharia isso; a pasta esta perfeita.
	///
	/// E ele escapa da metade da memoria do retrato TAMBEM, e de propósito: um sistema que nao esta
	/// inscrito nao e perguntado por ninguem. Quem o pega e a varredura CRUA do disco depois da
	/// passada de gravacao -- e e exatamente por isso que a metade do disco nao filtra nada.
	///
	/// Sem esta secao, as duas metades do retrato pareceriam redundantes e alguem cortaria uma.
	/// ======================================================================================
	///
	/// A SEGUNDA INJECAO e o mesmo defeito um passo adiante: o sistema INSCRITO, com `Zerar` mudo.
	/// Esse a metade da memoria pega na hora -- e as duas juntas mostram que as duas metades
	/// respondem por coisas diferentes.
	/// </summary>
	private void SecaoDaInjecaoDoDefeito()
	{
		GD.Print("[wipe] -- 6. o defeito injetado: um sistema fora da lista --");

		SistemaDoMundo? naves = _sistemasDoMundo.Find(s => s.Arquivos.Contains("naves.json"));
		if (naves == null)
		{
			AfirmarWt("achei o inscrito das naves pra injetar o defeito", false,
					  "o alvo da injecao sumiu -- reescolha um sistema com arquivo proprio");
			return;
		}

		// ---------------------------------------------------------- injecao 1: fora da lista
		NaCaixa("injecaoteste", _ =>
		{
			// ============================ O SISTEMA SAI ANTES DOS DOIS RETRATOS ============================
			// O defeito de verdade nao e "alguem removeu a inscricao no meio da partida": e um
			// sistema que **nunca foi inscrito** -- escrito, gravando em disco, e esquecido no
			// registro. Entao ele sai daqui, antes do primeiro boot, e os dois retratos sao tirados
			// com a MESMA forma de registro.
			//
			// Tirando-o no meio (como esta secao fazia primeiro), a metade da memoria mudava de
			// forma entre A e C e a diferenca aparecia com uma linha `memoria naves = 0` que era
			// artefato da medicao, e nao deteccao. A afirmacao ficava verde pelo motivo errado --
			// e a de baixo, que diz que foi o DISCO quem pegou, ficava vermelha e estava certa.
			// ==========================================================================================
			_sistemasDoMundo.Remove(naves);
			try
			{
				// E A MEMORIA COMECA LIMPA. Sem esta linha o `SemearPrimeiroBoot` nao esvaziaria a
				// frota (o `Zerar` dela acabou de sair da lista) e o "primeiro boot" nasceria com as
				// naves do mundo real do dono dentro.
				naves.Zerar();

				SemearPrimeiroBoot();
				ForcarGravacaoDeTudo();
				List<string> a = RetratoDoMundo();

				SemearMundoJogado();
				ExecutarLimpeza(null);
				ForcarGravacaoDeTudo();
				List<string> c = RetratoDoMundo();
				List<string> d = Diferenca(a, c);

				AfirmarWt("COM O DEFEITO, o retrato de conjunto fica DIFERENTE do primeiro boot",
						  d.Count > 0,
						  "a comparacao nao pegou um sistema inteiro esquecido -- ela nao vale nada");
				AfirmarWt("...e a diferenca aponta o arquivo do sistema escondido",
						  d.Any(l => l.Contains("naves.json", StringComparison.Ordinal)),
						  string.Join(" | ", d.Take(6)));

				// E A PROVA DE QUE FOI A METADE DO DISCO QUE PEGOU: o sistema escondido nao esta no
				// registro, entao a metade da memoria nao pergunta por ele. Uma bancada que so
				// olhasse a memoria teria passado verde neste exato estado.
				// `Contains` E NAO `StartsWith`: as linhas da `Diferenca` vem prefixadas com o lado
				// ("so no primeiro:"). Escrita com `StartsWith`, esta afirmacao NEGATIVA era
				// verdadeira sempre -- verde de graca, em cima de nada. Quem a pegou foi a positiva
				// da 6b, que e a mesma linha ao contrario: uma passava sozinha, as duas juntas nao.
				AfirmarWt("...e a metade da MEMORIA nao o via (foi o disco cru que pegou)",
						  !d.Any(l => l.Contains("memoria  naves", StringComparison.Ordinal)),
						  string.Join(" | ", d.Take(6)));
			}
			finally { _sistemasDoMundo.Add(naves); }
		});

		// ---------------------------------------------------------- injecao 2: inscrito, Zerar mudo
		GD.Print("[wipe] -- 6b. o defeito injetado: o `Zerar` que nao zera --");
		NaCaixa("injecaoteste2", _ =>
		{
			SemearPrimeiroBoot();
			ForcarGravacaoDeTudo();
			List<string> a = RetratoDoMundo();

			SemearMundoJogado();

			Action zerarDeVerdade = naves.Zerar;
			naves.Zerar = () => { };   // inscrito, contado, e mudo
			try
			{
				ExecutarLimpeza(null);
				ForcarGravacaoDeTudo();
				List<string> d = Diferenca(a, RetratoDoMundo());

				AfirmarWt("COM O `Zerar` MUDO, o retrato tambem fica diferente", d.Count > 0);
				AfirmarWt("...e agora a metade da MEMORIA acusa (o sistema esta inscrito)",
						  d.Any(l => l.Contains("memoria  naves", StringComparison.Ordinal)),
						  string.Join(" | ", d.Take(6)));
			}
			finally { naves.Zerar = zerarDeVerdade; }
		});
	}

	// =====================================================================
	// 7. O JOGADOR ONLINE NAO RESSUSCITA O PROPRIO SAVE
	// =====================================================================
	/// <summary>
	/// *"um jogador ONLINE nao ressuscita o proprio save depois da limpeza"*.
	///
	/// ============================ E O MODO DE FALHA MAIS PROVAVEL DE TODOS ============================
	/// Nao e hipotese: `Drop(peer)` chama `Persistir` ANTES de soltar. Uma limpeza escrita do jeito
	/// obvio -- "apaga os arquivos e derruba todo mundo" -- **recria cada conta na saida**, uma por
	/// uma, no proprio gesto de fazer a limpeza. O servidor ficaria com a pasta cheia de contas e o
	/// mundo zerado: metade lembrada, metade esquecida, que e o pior estado possivel.
	///
	/// A secao 3 ja mede o portao (`Persistir` grava com ele aberto, nao grava com ele fechado).
	/// ESTA secao mede outra coisa: o caminho INTEIRO, com um corpo de verdade em `_players`, pelo
	/// `ExecutarLimpeza` de producao -- porque o portao certo na ordem errada nao serve de nada, e
	/// e a ORDEM que nenhuma afirmacao sobre o `if` do `Persistir` alcanca.
	/// ==============================================================================================
	/// </summary>
	private void SecaoDoJogadorOnline()
	{
		GD.Print("[wipe] -- 7. o jogador ONLINE nao ressuscita o proprio save --");

		NaCaixa("onlineteste", caixa =>
		{
			SemearPrimeiroBoot();
			SemearMundoJogado();

			const string conta = "online_do_wipe";
			string arquivo = System.IO.Path.Combine(caixa, conta + ".json");

			var acc = new AccountSave { Conta = conta, CriadaEm = NowMs() };
			acc.Slots[0] = new CharacterSave { Nome = "OnlineDoWipe", Raca = "Saiyan" };
			_store!.Gravar(acc);
			AfirmarWt("o jogador online tinha save antes da limpeza", System.IO.File.Exists(arquivo));

			// O CORPO ENTRA NAS COLECOES DE SESSAO, como o de quem esta jogando. Sem `Peer` -- o que
			// esta secao mede e a ORDEM do `ExecutarLimpeza`, e um peer de mentira so acrescentaria
			// caminhos de rede que nao sao dela.
			var pl = new ServerPlayer
			{
				Id = -910_003, Peer = null, Name = "OnlineDoWipe", Conta = conta, Slot = 0,
				Ficha = new Fighter { Name = "OnlineDoWipe", BP = 1000 },
			};
			_players[pl.Id] = pl;
			AfirmarWt("o corpo do jogador esta nas colecoes de sessao", _players.ContainsKey(pl.Id));

			// A SONDA TEM QUE SABER ESCREVER, senao o "nao gravou" de baixo nao quer dizer nada --
			// uma sonda quebrada acusaria portao fechado num servidor completamente aberto. E a
			// mesma simetria da secao 3, feita pelo caminho exato que a sonda vai usar.
			System.IO.File.Delete(arquivo);
			Persistir(pl, acc);
			AfirmarWt("a sonda SABE gravar com o portao aberto", System.IO.File.Exists(arquivo),
					  "sem isto, o 'foi recusado' de baixo passa mesmo com o portao escancarado");

			// ============================ A SONDA -- A TENTATIVA NO PIOR INSTANTE ============================
			// Aqui esta o limite honesto desta bancada: `Jogadores` e `_players.Values.Where(EhJogador)`
			// (ver `Core/Npc/Gente.cs`), e um `NetPeer` nao se fabrica sem uma conexao de verdade. Um
			// corpo sem peer nao e jogador pro servidor -- ele nao entra no laco que derruba, e afirmar
			// "o jogador caiu" com ele seria afirmar sobre um NPC.
			//
			// Entao esta secao mede o que PODE ser medido de verdade, e mede no pior instante
			// possivel: um sistema INSCRITO cujo `Zerar` tenta gravar a conta bem no meio da
			// limpeza. Ele roda dentro do `ExecutarLimpeza` de producao, no passo 3, com o portao
			// fechado -- o mesmo portao e o mesmo estado do passo 2, que e onde o `Drop` grava.
			//
			// A sonda APAGA o arquivo antes de tentar. Sem isso ela leria o save de antes da limpeza
			// e responderia "gravou" sempre -- a pasta so e varrida no passo 4, depois dela.
			// ==============================================================================================
			bool sondaRodou = false, gravouNoMeio = false;
			var sonda = new SistemaDoMundo
			{
				Nome = "sonda da bancada: grava a conta NO MEIO da limpeza",
				Unidade = "sonda",
				Zerar = () =>
				{
					sondaRodou = true;
					try { System.IO.File.Delete(arquivo); } catch { /* ja nao existia */ }
					Persistir(pl, acc);
					gravouNoMeio = System.IO.File.Exists(arquivo);
				},
			};
			_sistemasDoMundo.Add(sonda);
			try { ExecutarLimpeza(null); }
			finally { _sistemasDoMundo.Remove(sonda); }

			AfirmarWt("a sonda rodou DENTRO da limpeza", sondaRodou,
					  "sem isto a linha de baixo passa de graca");
			AfirmarWt("no meio da limpeza, gravar a conta do jogador e RECUSADO", !gravouNoMeio,
					  "e este o gesto que recria cada conta na saida e desfaz a limpeza sozinha");

			AfirmarWt("depois da limpeza, o save do jogador online NAO existe",
					  !System.IO.File.Exists(arquivo),
					  "o `Drop` chama `Persistir` antes de soltar -- sem o portao, deslogar todo "
					+ "mundo recria cada conta e a limpeza se desfaz sozinha");
			AfirmarWt("...e o corpo dele saiu de `_players`", _players.Count == 0,
					  $"{_players.Count} corpo(s)");

			// ============================ A SEGUNDA TENTATIVA DE RESSURREICAO ============================
			// Em producao o autosave passa de dois em dois minutos por todo mundo, e o portao JA
			// REABRIU aqui (o `finally` do `ExecutarLimpeza`) -- ou seja, este e o caso honesto, sem
			// a guarda pra segurar nada. A garantia tem que ser outra: nao ha mais por quem passar.
			//
			// A gravacao e forcada pelo funil de DOIS argumentos de proposito. O `Persistir(pl)` de
			// um argumento desiste na primeira linha quando o corpo nao tem `NetPeer` (e o desta
			// bancada nao tem), entao afirmar por ele seria afirmar sobre o `return` dele -- verde
			// de graca, e verde de graca e o que esta secao inteira existe pra nao ser.
			foreach (ServerPlayer p in Jogadores.ToList()) Persistir(p, acc);
			AfirmarWt("o autosave depois da limpeza nao ressuscita ninguem",
					  !System.IO.File.Exists(arquivo));
		});
	}

	// =====================================================================
	// 8. O SERVIDOR FUNCIONA DEPOIS
	// =====================================================================
	/// <summary>
	/// *"limpar e deixar o jogo quebrado nao e limpar"*.
	///
	/// Uma limpeza que zera um contentor a mais do que devia (o catalogo de zonas, o livro de
	/// skills, a colisao das zonas) passa em TODAS as afirmacoes das secoes de cima: o disco fica
	/// vazio, a memoria fica zerada, o retrato bate com o primeiro boot. E o servidor esta morto.
	///
	/// Entao aqui se JOGA no mundo recem-limpo, pelos caminhos de producao: cria conta, grava,
	/// nasce um corpo, anda com a regra de passo do servidor, e sobe a escada de transformacao.
	/// </summary>
	private void SecaoDoServidorDepois()
	{
		GD.Print("[wipe] -- 8. o servidor funciona DEPOIS da limpeza --");

		NaCaixa("depoisteste", caixa =>
		{
			SemearPrimeiroBoot();
			SemearMundoJogado();
			ExecutarLimpeza(null);

			// ------------------------------------------------------ criar conta e gravar
			const string conta = "novato_do_wipe";
			string arquivo = System.IO.Path.Combine(caixa, conta + ".json");

			var ficha = new CharacterDraft
			{
				Name = "NovatoDoWipe", Race = "Saiyan", Planet = "Vegeta", Gender = "Male",
				Backstory = "Nasceu no mundo recem-limpo, pra provar que ele ainda serve.",
			};
			ficha.Age = Math.Min(18, ficha.IdadeMaxima);
			string[] linhagens = CharacterDraft.EscolhasDeClasse("Saiyan");
			if (linhagens.Length > 0) ficha.ChosenClass = linhagens[0];

			// A MESMA PORTA DO JOGADOR. Uma limpeza que estragasse o catalogo de racas reprovaria
			// AQUI -- e este e o ponto da secao.
			string motivo = ValidarFicha(ficha);
			AfirmarWt("a ficha de um personagem novo passa no validador depois da limpeza",
					  motivo.Length == 0, motivo);

			Fighter corpo = Nascer(ficha, "NovatoDoWipe");
			AfirmarWt("o corpo nasce com stats (o catalogo de racas sobreviveu)",
					  corpo.BP > 0, $"BP {corpo.BP}");

			ZoneKey zona = _catalogo != null && _catalogo.Todas.Any()
				? ZoneKey.Premade(_catalogo.Todas.First().Zona)
				: ZoneKey.Premade("Earth");

			var c = new CharacterSave
			{
				Nome = "NovatoDoWipe", Raca = "Saiyan", Planeta = "Vegeta", Genero = "Male",
				Idade = ficha.Age, Linhagem = ficha.ChosenClass, Ficha = corpo,
				Zona = zona.Name, ZonaTipo = zona.Kind, ZonaSeed = zona.Seed,
			};
			var acc = new AccountSave { Conta = conta, CriadaEm = NowMs() };
			acc.Slots[0] = c;
			_store!.Gravar(acc);
			AfirmarWt("a conta nova GRAVA no mundo limpo", System.IO.File.Exists(arquivo),
					  "a pasta e recriada pela propria limpeza -- sem isso o primeiro login se perde");
			AfirmarWt("...e o servidor a enxerga", _store.Quantas() == 1, $"{_store.Quantas()}");

			// ------------------------------------------------------ entrar
			var pl = new ServerPlayer
			{
				Id = -910_004, Peer = null, LastInputMs = NowMs(), Conta = conta, Slot = 0,
			};
			AccountStore.ParaJogador(c, pl);
			pl.Zone = zona;
			pl.Pos = new Vec2(20 * ZoneCollision.TileSize, 20 * ZoneCollision.TileSize);
			pl.Facing = Facing.South;
			pl.Ficha.Statify();
			pl.SpeedStat = MoveRules.SpeedStatFrom(pl.Ficha.Espeed);
			_players[pl.Id] = pl;

			// ------------------------------------------------------ andar
			// PELA REGRA DE PASSO DO SERVIDOR, com o mapa de colisao da zona -- que e o contentor
			// que a limpeza reescreve (`RefazerColisaoDeTudo`). Um mundo limpo cujo mapa voltou
			// todo bloqueado recusaria este passo, e o sintoma em jogo seria "nao consigo andar".
			ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
			AfirmarWt("a zona ainda tem mapa de colisao depois da limpeza", mapa != null);

			Vec2 antes = pl.Pos;
			var alvo = new Vec2(antes.X + 8, antes.Y);
			float orcamento = 0;
			bool andou = MoveRules.ValidateStep(antes, alvo, 0.25f, pl.SpeedStat, mapa,
												ref orcamento, out Vec2 ok, false);
			AfirmarWt("o personagem ANDA no mundo limpo", andou && ok.X > antes.X,
					  $"de {antes.X:F1} para {ok.X:F1}");
			pl.Pos = ok;

			// ------------------------------------------------------ transformar
			// O BP E EMPURRADO PRA CIMA de proposito: o que esta secao mede e se a ESCADA existe
			// depois da limpeza, e nao se este corpo especifico ja teria merecido o degrau.
			AfirmarWt("a escada de transformacao existe depois da limpeza", TemEscada(pl));
			pl.Ficha.BP = 1e12;
			pl.Ficha.Statify();
			pl.Ficha.Ki = pl.Ficha.MaxKi;   // a porta do degrau le a FRACAO de Ki, nao so o BP

			// ============================ O SSJ NAO SE COMPRA COM BP, E ISSO E DE PROPOSITO ============================
			// Com BP e Ki de sobra o servidor ainda recusa, e a frase dele e a regra do jogo:
			// *"Super Saiyajin nao se alcanca querendo -- ele sai de uma dor que voce ainda nao
			// teve."* A primeira subida e por RAIVA, e encena-la aqui seria escrever a bancada da
			// transformacao dentro da bancada da limpeza.
			//
			// Entao o que se mede e o caso que interessa a ESTA secao: um jogador que **ja havia
			// despertado** entra no mundo limpo e usa a forma dele. E o estado que o login restaura
			// do save de qualquer veterano (`Liberadas`), pela mesma API de producao que o Oozaru e
			// o despertar assistido usam -- e o degrau liberado e exatamente aquele a quem so
			// faltava a raiva, achado perguntando ao catalogo em vez de cravado pelo nome.
			// ======================================================================================================
			PerfilDeFormas perfil = Perfil(pl);
			foreach (FormaDef d in Catalogo.Todas)
			{
				if (pl.Forma.Avaliar(d.Id, pl.Ficha.BP, 1, caido: false, perfil) != RecusaForma.SemFuria) continue;
				pl.Forma.Liberar(d.Id);
				break;
			}

			string formaAntes = pl.Forma.Atual;

			// A RECUSA DO SERVIDOR E LIDA, e nao adivinhada: `Transformar` explica por que nao subiu
			// (`PorQueNao`), e sem capturar isso um vermelho aqui viraria caca ao tesouro.
			List<string>? escutaAntes = EscutaDeAvisos;
			EscutaDeAvisos = [];
			Transformar(pl, subir: true);
			string recusa = string.Join(" | ", EscutaDeAvisos);
			EscutaDeAvisos = escutaAntes;

			AfirmarWt("o personagem TRANSFORMA no mundo limpo", pl.Forma.Atual != formaAntes,
					  $"continuou em '{pl.Forma.Atual}' -- o servidor disse: {recusa}");

			// ------------------------------------------------------ e o progresso dele persiste
			Persistir(pl);
			AfirmarWt("o progresso do novato GRAVA (o portao reabriu)",
					  System.IO.File.Exists(arquivo));

			_players.Clear();
			_contas.Clear();
		});
	}

	// =====================================================================
	// 10. AS ESFERAS DO DRAGAO, JOGADAS DE VERDADE
	// =====================================================================
	/// <summary>
	/// **UM JOGADOR JOGA O SISTEMA INTEIRO, E SO ENTAO A LIMPEZA VEM.**
	///
	/// ============================ POR QUE ESTA SECAO EXISTE, TENDO A 5 ============================
	/// A secao 5 semeia estes dois sistemas ESCREVENDO NOS CAMPOS (`_sets.Add(new SetDeEsferas{...})`).
	/// Isso basta pra ela -- o que ela compara e o retrato --, e **nao basta pra responder ao pedido do
	/// dono**, que foi sobre *"as esferas criadas por jogadores"*. Um estado montado a mao prova que o
	/// `Zerar` esvazia uma lista; ele nao prova que o que o JOGO cria cabe naquela lista.
	///
	/// A diferenca nao e filosofica, e ela ja mordeu neste projeto: o `Portador` (a esfera na mao) e
	/// `[JsonIgnore]`, o `_invocacoes` e um dicionario por id de SESSAO, a procuracao mora em quatro
	/// campos soltos de OUTRO arquivo, e o `Ciclo` -- que e o endereco das sete -- so anda quando alguem
	/// espalha. Nada disso aparece se a semeadura for `= 1` num campo escolhido a dedo: aparece quando
	/// `ErguerEstatua`, `PegarEsfera`, `InvocarODragao`, `ReivindicarSuper` e `TransferirAGuarda` rodam
	/// de verdade, um depois do outro, com um corpo no mundo.
	/// ==========================================================================================
	///
	/// ============================ E A PROVA NAO E "O ARQUIVO SUMIU" ============================
	/// O arquivo some pela VASSOURA (a limpeza varre a pasta inteira), com ou sem `Zerar`. Entao esta
	/// secao mede as DUAS metades em cada passo:
	///
	///   * ANTES da limpeza o estado EXISTE -- no disco (o `esferas.json` cru carrega a conta e o nome
	///     de quem ergueu) e na memoria. Sem esta metade, "sumiu" fica verde num sistema onde nunca
	///     houve nada -- que e literalmente como estes dois passaram semanas fora da medicao.
	///   * DEPOIS, o disco esta limpo **e o servidor volta a gravar**. E a segunda parte que importa:
	///     um `Zerar` que falte deixa o cache sujo, e o arquivo RESSUSCITA com o conteudo velho na
	///     primeira gravacao -- o autosave de dois minutos, ou o proximo gesto de qualquer jogador.
	///
	/// A secao <see cref="SecaoDoZerarMudoDasEsferas"/> (10b) prova exatamente isso, disparando.
	/// ======================================================================================
	///
	/// O LIMITE HONESTO, escrito porque ele e real: a CONTAGEM REGRESSIVA da disputa
	/// (<see cref="TickDasSuperEsferas"/>) exige `Peer != null`, e um `NetPeer` nao se fabrica sem
	/// conexao de verdade -- a secao 7 ja documenta a mesma parede. Entao o canal e ABERTO pelo verb de
	/// producao (<see cref="ReivindicarSuper"/>) e FECHADO pelo funil de producao
	/// (<see cref="FecharADisputa"/>, o mesmo que o tique chama); o que fica de fora e so o relogio
	/// entre os dois.
	/// </summary>
	private void SecaoDasEsferasJogadas()
	{
		GD.Print("[wipe] -- 10. as esferas do dragao: jogar de verdade, e SO ENTAO limpar --");

		var forjados = new List<ServerPlayer>();
		NaCaixa("esferasjogadas", _ =>
		{
			try
			{
				// ------------------------------------------------------ o primeiro boot, e a referencia
				SemearPrimeiroBoot();

				SetDeEsferas? eternoAntes = _sets.Find(s => s.Eterno);
				AfirmarWt("no primeiro boot Namek ja tem o set ETERNO", eternoAntes != null,
						  "sem ele as duas direcoes da prova do eterno nao tem referencia");
				double esperaNoBoot = eternoAntes != null ? eternoAntes.AtivoEm - TempoDoMundo : -1;

				// O CONTRA-EXEMPLO, na frente de tudo: num mundo onde nunca houve set de jogador,
				// "sumiu" e verdade de graca.
				AfirmarWt("...e NAO ha set de jogador nenhum, nem vinculo com as Super",
						  _sets.Count(s => !s.Eterno) == 0 && QuantoAsSupersPrendem() == 0,
						  $"{_sets.Count(s => !s.Eterno)} set(s), {QuantoAsSupersPrendem()} vinculo(s)");

				// ------------------------------------------------------ 1. ERGUER (verb de producao)
				ServerPlayer? heroi = ErguerUmaEstatuaDeJogador("guardiao_do_wipe", "Kami do Wipe", forjados);
				AfirmarWt("um JOGADOR ergue a Estatua do Dragao pelo verb de producao", heroi != null,
						  "o verb recusou -- raca, classe, trono ou a Terra fora do catalogo");
				if (heroi == null) return;

				SetDeEsferas? set = _sets.Find(s => !s.Eterno);
				if (set == null) { AfirmarWt("a estatua erguida esta no registro", false); return; }

				AfirmarWt("...e a estatua guarda QUEM a ergueu (conta, nome e assinatura)",
						  string.Equals(set.CriadorConta, heroi.Conta, StringComparison.Ordinal)
						  && string.Equals(set.CriadorNome, heroi.Name, StringComparison.Ordinal)
						  && string.Equals(set.CriadorSig, heroi.Assinatura, StringComparison.Ordinal));

				// O DESEJO SUPREMO E COMPRADO NO CAMINHO, e nao escrito: dois milhoes de zeni saem do
				// bolso dentro do proprio `ErguerEstatua`.
				AfirmarWt("...e O MAIS FORTE DO UNIVERSO foi COMPRADO (2.000.000 de zeni sairam do bolso)",
						  set.TemSupremo && heroi.Ficha.Zeni < 1, $"supremo={set.TemSupremo}, zeni={heroi.Ficha.Zeni:N0}");

				AfirmarWt("...e as SETE nasceram espalhadas pelo mundo",
						  _esferas.Count(e => e.Set == set.Id) == Jandirus.Core.Magic.Esferas.Total,
						  $"{_esferas.Count(e => e.Set == set.Id)}");

				// ------------------------------------------------------ 2. PEGAR (a esfera na mao)
				// A "mochila" nao existe: carregar e o campo `Portador` DENTRO da propria esfera. Ele e
				// `[JsonIgnore]` -- nao vai pro disco --, entao ele so pode ser medido na memoria, e e
				// exatamente o tipo de estado que um `Zerar` esquece.
				foreach (Esfera e in _esferas.Where(x => x.Set == set.Id).ToList())
				{
					heroi.Pos = new Vec2(e.X, e.Y);
					PegarEsfera(heroi);
				}
				AfirmarWt("...e o jogador PEGA as sete do chao (o verb `db_pegar` de producao)",
						  QuantasCarrega(heroi.Id) == Jandirus.Core.Magic.Esferas.Total,
						  $"{QuantasCarrega(heroi.Id)} na mao");

				// ------------------------------------------------------ 3. INVOCAR
				// A ESPERA DE NASCIMENTO E DE VERDADE (0,4 ano do DM), entao o relogio do mundo anda --
				// pela mesma manivela do admin. E ela e ESTADO DE OUTRO INSCRITO: e por isso que a
				// direcao 2 da prova do eterno, la embaixo, tem com o que medir.
				double adiantoUsado = Jandirus.Core.Magic.Esferas.SegundosDe(
					Jandirus.Core.Magic.Esferas.EsperaDeNascimento) + 10;
				_adiantoDoCeu += adiantoUsado;
				AfirmarWt("o set ACORDA quando o relogio passa da espera de nascimento", SetAtivo(set));

				InvocarODragao(heroi);
				AfirmarWt("...e o DRAGAO fica de pe (contador de sessao: nao existe no disco)",
						  _invocacoes.ContainsKey(set.Id));

				// ------------------------------------------------------ 4. O CICLO DAS SUPER, ANDADO
				// ============================ ELE PRECISA ANDAR, SENAO A AFIRMACAO E VAZIA ============================
				// `_cicloDasSupers` nasce zero, e a limpeza o poe em zero. Medido sem ninguem tendo pedido
				// nada, o "voltou a zero" do fim seria verde por nunca ter saido de la -- vacuidade, que e
				// o cego que esta secao inteira existe pra nao ter.
				//
				// E ele so anda por UM caminho: um desejo ao SUPER SHENRON o consome
				// (`ConsumirAsSupers`, o `pspace_sdb_scatter()` do DM). Entao um Kai que fala a lingua dos
				// deuses junta as sete, pede, e as sete reaparecem NOUTRO ENDERECO -- que e literalmente o
				// que o ciclo significa (ele e metade da entrada de `SuperEsferas.PosicaoDa`).
				// ================================================================================================
				ZoneKey espaco = Espaco.Zona(SeedDoUniverso);

				ServerPlayer falante = Forjar("Kaio do Wipe", OndeEstaASuper(1), espaco, "falante_do_wipe");
				forjados.Add(falante);
				falante.Race = "Kai";
				falante.Ficha.Race = "Kai";
				ConferirALingua(falante);

				for (int n = 1; n <= Jandirus.Core.Magic.SuperEsferas.Total; n++)
				{
					falante.Pos = OndeEstaASuper(n);
					ReivindicarSuper(falante);
					FecharOCanalDaBancada(falante);
				}
				AfirmarWt("um Kai junta as SETE Super Esferas pelos verbos de producao",
						  QuantasSupersTem(falante.Assinatura) == Jandirus.Core.Magic.SuperEsferas.Total,
						  $"{QuantasSupersTem(falante.Assinatura)}");

				ChamarOSuperShenron(falante, "sdb_riqueza");
				AfirmarWt("...e o desejo dele ANDA o ciclo (o endereco das sete muda)",
						  _cicloDasSupers == 1 && !_supers.Any(s => s.Dono.Length > 0),
						  $"ciclo {_cicloDasSupers}, {_supers.Count(s => s.Dono.Length > 0)} ainda com dono");

				// ------------------------------------------------------ 5. REIVINDICAR uma Super Esfera
				MoveToZone(heroi.Id, espaco, OndeEstaASuper(1));

				ReivindicarSuper(heroi);
				AfirmarWt("no espaco, o jogador ABRE o canal de disputa pela Super Esfera #1",
						  _disputasDeSuper.ContainsKey(heroi.Id),
						  "o verb recusou -- ver o alcance e a zona");
				FecharOCanalDaBancada(heroi);
				AfirmarWt("...e o canal fechado lhe da a POSSE da #1",
						  QuantasSupersTem(heroi.Assinatura) == 1, $"{QuantasSupersTem(heroi.Assinatura)}");

				// AS OUTRAS SEIS, pelo mesmo par de verbos: a procuracao exige as SETE, e ela e a coisa
				// que o dono nomeou. Nao ha atalho aqui de proposito.
				for (int n = 2; n <= Jandirus.Core.Magic.SuperEsferas.Total; n++)
				{
					heroi.Pos = OndeEstaASuper(n);
					ReivindicarSuper(heroi);
					FecharOCanalDaBancada(heroi);
				}
				AfirmarWt("...e as SETE Super Esferas ficam com ele",
						  QuantasSupersTem(heroi.Assinatura) == Jandirus.Core.Magic.SuperEsferas.Total,
						  $"{QuantasSupersTem(heroi.Assinatura)}");

				// ------------------------------------------------------ 6. a PROCURACAO de pe
				ServerPlayer voz = Forjar("Kaioshin do Wipe", heroi.Pos, espaco, "portavoz_do_wipe");
				forjados.Add(voz);

				TransferirAGuarda(heroi, voz.Name);
				AfirmarWt("a oferta de guarda fica de pe esperando o SIM",
						  _ofertasDeGuarda.ContainsKey(voz.Conta));

				ResponderAGuarda(voz, aceitou: true);
				AfirmarWt("aceita, a PROCURACAO fica de pe: as sete com o porta-voz, o desejo do pedinte",
						  HaProcuracao
						  && string.Equals(_benefSig, heroi.Assinatura, StringComparison.Ordinal)
						  && QuantasSupersTem(voz.Assinatura) == Jandirus.Core.Magic.SuperEsferas.Total);

				RegistrarPedido(heroi, "sdb_riqueza");
				AfirmarWt("...e o PEDIDO do beneficiario fica registrado",
						  _pedidoDoBenef.Length > 0,
						  "e este o fantasma que trancava o Super Shenron pra sempre num mundo herdado");

				// UM CANAL DE TOMADA ABERTO NO INSTANTE DA LIMPEZA. Ele existe pra medir a contaminacao
				// cruzada que a inscricao documenta: o tique seguinte derrubaria esta disputa e chamaria
				// `RecadoAoDono`, que grava o `conquista.json` de OUTRO inscrito.
				ServerPlayer ladrao = Forjar("Ladrao do Wipe", OndeEstaASuper(3), espaco, "ladrao_do_wipe");
				forjados.Add(ladrao);
				ReivindicarSuper(ladrao);
				AfirmarWt("um canal de TOMADA fica aberto no instante da limpeza",
						  _disputasDeSuper.ContainsKey(ladrao.Id));

				// ------------------------------------------------------ A FOTOGRAFIA DO ANTES
				// O DISCO CRU, e nao a memoria: os verbos ja gravaram sozinhos (cada um chama o
				// `Salvar*` dele), e a passada abaixo e o autosave por cima -- os dois caminhos.
				ForcarGravacaoDeTudo();

				string contaHeroi = heroi.Conta, nomeHeroi = heroi.Name;
				string sigHeroi = heroi.Assinatura, sigVoz = voz.Assinatura;
				int idDoSet = set.Id;

				string esferasAntes = LerOuVazio(CaminhoDasEsferas);
				string supersAntes = LerOuVazio(CaminhoDasSupers);

				AfirmarWt("ANTES: o esferas.json do DISCO carrega a conta e o nome de quem ergueu",
						  esferasAntes.Contains(contaHeroi, StringComparison.Ordinal)
						  && esferasAntes.Contains(nomeHeroi, StringComparison.Ordinal),
						  $"{esferasAntes.Length} b");
				AfirmarWt("ANTES: o superesferas.json carrega o dono das sete E a procuracao",
						  supersAntes.Contains(sigVoz, StringComparison.Ordinal)
						  && supersAntes.Contains(sigHeroi, StringComparison.Ordinal),
						  $"{supersAntes.Length} b");
				AfirmarWt("ANTES: a MEMORIA tem estatua, esferas na mao, dragao de pe, disputa e procuracao",
						  _sets.Count(s => !s.Eterno) == 1
						  && _esferas.Count(e => e.Portador != 0) > 0
						  && _invocacoes.Count > 0
						  && _disputasDeSuper.Count > 0
						  && HaProcuracao,
						  $"sets={_sets.Count(s => !s.Eterno)}, mao={_esferas.Count(e => e.Portador != 0)}, "
						+ $"dragoes={_invocacoes.Count}, disputas={_disputasDeSuper.Count}, proc={HaProcuracao}");
				AfirmarWt("ANTES: os dois inscritos contam o que ha pra apagar",
						  _sets.Count(s => !s.Eterno) == 1 && QuantoAsSupersPrendem() > 0,
						  $"{QuantoAsSupersPrendem()} vinculo(s)");
				// E O CICLO ESTA FORA DE ZERO. Sem esta linha, o "voltou a zero" do fim seria verde por
				// nunca ter saido -- a metade que falta em toda afirmacao de ausencia.
				AfirmarWt("ANTES: o ciclo das Super esta FORA de zero (senao o 'voltou a zero' e vazio)",
						  _cicloDasSupers > 0, $"{_cicloDasSupers}");

				// A MARCA NO ETERNO. Ela nao muda nada no jogo -- ela existe pra a direcao 2 poder
				// distinguir "o set eterno voltou" de "o set eterno nunca saiu".
				if (eternoAntes != null) eternoAntes.Ciclo = 4242;

				// ------------------------------------------------------ **A LIMPEZA**
				ResultadoDaLimpeza r = ExecutarLimpeza(null);
				AfirmarWt("a limpeza nao deu erro de disco", r.Erros.Count == 0,
						  string.Join("; ", r.Erros));

				// ------------------------------------------------------ DEPOIS: o disco
				AfirmarWt("DISCO: nao ha esferas.json depois da limpeza",
						  !System.IO.File.Exists(CaminhoDasEsferas));
				AfirmarWt("DISCO: nao ha superesferas.json depois da limpeza",
						  !System.IO.File.Exists(CaminhoDasSupers));

				// ------------------------------------------------------ DEPOIS: a MEMORIA
				// E ESTA E A METADE QUE O DISCO ESCONDE. As duas linhas de cima ficariam verdes com o
				// `Zerar` arrancado (a vassoura varre a pasta inteira); estas nao.
				AfirmarWt("MEMORIA: a estatua do jogador sumiu", !_sets.Any(s => !s.Eterno),
						  $"{_sets.Count(s => !s.Eterno)} set(s) de jogador de pe");
				AfirmarWt("MEMORIA: as sete daquele set sumiram", !_esferas.Any(e => e.Set == idDoSet));
				AfirmarWt("MEMORIA: nenhuma esfera na mao de ninguem (o `Portador`, que nao vai pro disco)",
						  !_esferas.Any(e => e.Portador != 0),
						  $"{_esferas.Count(e => e.Portador != 0)} carregada(s)");
				AfirmarWt("MEMORIA: nenhum dragao de pe (o `_invocacoes`, indexado por id de sessao)",
						  _invocacoes.Count == 0, $"{_invocacoes.Count}");
				AfirmarWt("MEMORIA: nenhuma Super Esfera tem dono",
						  !_supers.Any(s => s.Dono.Length > 0)
						  && QuantasSupersTem(sigHeroi) == 0 && QuantasSupersTem(sigVoz) == 0);
				AfirmarWt("MEMORIA: a PROCURACAO caiu, com o pedido dela",
						  !HaProcuracao && _pedidoDoBenef.Length == 0 && _alvoDoPedido.Length == 0);
				AfirmarWt("MEMORIA: nenhum canal de disputa e nenhuma oferta de guarda de pe",
						  _disputasDeSuper.Count == 0 && _ofertasDeGuarda.Count == 0);
				AfirmarWt("MEMORIA: o ciclo das Super voltou a zero (ele e metade do endereco delas)",
						  _cicloDasSupers == 0, $"{_cicloDasSupers}");
				AfirmarWt("MEMORIA: os dois inscritos contam ZERO",
						  _sets.Count(s => !s.Eterno) == 0 && QuantoAsSupersPrendem() == 0,
						  $"{_sets.Count(s => !s.Eterno)} set(s), {QuantoAsSupersPrendem()} vinculo(s)");

				// ------------------------------------------------------ O CONJUNTO ETERNO, as duas direcoes
				SetDeEsferas? eternoDepois = _sets.Find(s => s.Eterno);

				// DIRECAO 1 -- ELE VOLTA DE PE, e sem reiniciar o servidor. Um `Zerar` que so esvaziasse
				// as listas deixaria Namek SEM Esferas do Dragao ate o proximo boot: `ErguerOSetEterno`
				// so tinha o boot como chamador, e a limpeza roda com o servidor no ar.
				AfirmarWt("ETERNO (1): Namek continua com as Esferas do Dragao depois da limpeza",
						  eternoDepois != null,
						  "sem a remontagem, o unico set que existe sem jogador nenhum some ate o reinicio");
				AfirmarWt("ETERNO (1): ...com as sete penduradas nela",
						  _esferas.Count(e => e.Set == Jandirus.Core.Magic.Esferas.IdDoSetEterno)
						  == Jandirus.Core.Magic.Esferas.Total);
				AfirmarWt("ETERNO (1): ...e ha UM SO (dois com o mesmo Id = 0 e o defeito da recarga)",
						  _sets.Count(s => s.Eterno) == 1, $"{_sets.Count(s => s.Eterno)}");

				// DIRECAO 2 -- ELE NAO SOBREVIVEU: ele foi RECRIADO. E a diferenca importa, porque um
				// eterno POUPADO traria junto tudo o que estivesse escrito nele.
				// O `!= null` NAO E REDUNDANTE com a direcao 1: sem ele, um eterno AUSENTE satisfaz esta
				// linha de graca (`null` nunca e o objeto de antes) -- e a injecao que arranca o
				// `ErguerOSetEterno` do <see cref="RemontarOMundoNovo"/> deixava exatamente ESTA verde
				// enquanto as tres da direcao 1 ficavam vermelhas. Verde por ausencia e o cego que este
				// projeto ja registrou; a afirmacao passa a exigir um eterno de pe E diferente.
				AfirmarWt("ETERNO (2): o eterno de agora e outro OBJETO (recriado, e nao poupado)",
						  eternoDepois != null && !ReferenceEquals(eternoAntes, eternoDepois));
				AfirmarWt("ETERNO (2): ...e a marca posta no eterno do mundo velho nao veio junto",
						  eternoDepois != null && eternoDepois.Ciclo != 4242,
						  $"Ciclo {eternoDepois?.Ciclo}");

				// E ELE NASCEU SOB O RELOGIO DO MUNDO NOVO. `AtivoEm` e carimbo ABSOLUTO do
				// `TempoDoMundo`, e o adianto do ceu -- que esta secao empurrou pra frente pra poder
				// invocar -- e zerado por OUTRO inscrito. Erguido antes dele (dentro do `ZerarEsferas`,
				// que era a forma obvia), o Porunga do mundo novo nasceria apagado por todo o tamanho do
				// adianto, e nada no jogo diria por que. E o motivo inteiro de o `RemontarOMundoNovo`
				// existir como passada separada.
				AfirmarWt("ETERNO (2): o relogio do mundo voltou ao lugar", Math.Abs(_adiantoDoCeu) < 1,
						  $"adianto {_adiantoDoCeu:0}");
				double esperaDepois = eternoDepois != null ? eternoDepois.AtivoEm - TempoDoMundo : -1;
				AfirmarWt("ETERNO (2): e a espera dele e a do PRIMEIRO BOOT, nao a do mundo adiantado",
						  Math.Abs(esperaDepois - esperaNoBoot) <= 60,
						  $"primeiro boot {esperaNoBoot:0} s, depois da limpeza {esperaDepois:0} s -- "
						+ $"o adianto que a bancada usou foi {adiantoUsado:0} s");

				// ------------------------------------------------------ **O ARQUIVO NAO RESSUSCITA**
				// ============================ A METADE QUE O DISCO ESCONDE, MEDIDA ============================
				// "o arquivo sumiu" nao e a pergunta: ele some pela vassoura de qualquer jeito. A
				// pergunta e o que o servidor escreve na PROXIMA gravacao -- em producao, o autosave de
				// dois minutos, ou o primeiro gesto de qualquer jogador.
				// ==========================================================================================
				ForcarGravacaoDeTudo();
				AfirmarWt("o esferas.json VOLTA na primeira gravacao (senao a leitura de baixo nao diz nada)",
						  System.IO.File.Exists(CaminhoDasEsferas));
				AfirmarWt("...e o superesferas.json tambem", System.IO.File.Exists(CaminhoDasSupers));

				string esferasDepois = LerOuVazio(CaminhoDasEsferas);
				string supersDepois = LerOuVazio(CaminhoDasSupers);

				AfirmarWt("o arquivo que voltou NAO lembra da conta de quem ergueu a estatua",
						  !esferasDepois.Contains(contaHeroi, StringComparison.Ordinal),
						  $"{esferasDepois.Length} b (era {esferasAntes.Length} b)");
				AfirmarWt("...nem do nome dele, nem da assinatura",
						  !esferasDepois.Contains(nomeHeroi, StringComparison.Ordinal)
						  && !esferasDepois.Contains(sigHeroi, StringComparison.Ordinal));
				AfirmarWt("...e o superesferas.json que voltou nao lembra do dono nem do procurador",
						  !supersDepois.Contains(sigHeroi, StringComparison.Ordinal)
						  && !supersDepois.Contains(sigVoz, StringComparison.Ordinal),
						  $"{supersDepois.Length} b (era {supersAntes.Length} b)");

				// ------------------------------------------------------ E O SISTEMA CONTINUA VIVO
				// Um gesto de jogo DE VERDADE no mundo limpo -- e nao so o autosave. Ele responde a
				// outra metade do pedido ("limpar e deixar o jogo quebrado nao e limpar") e prova de
				// graca o contador de id, que cai sem precisar de linha nenhuma: `ProximoIdDeSet` varre
				// `_sets` atras do primeiro livre, entao a lista vazia devolve o 1.
				ServerPlayer? novo = ErguerUmaEstatuaDeJogador("novo_kami_do_wipe", "Novo Kami do Wipe", forjados);
				AfirmarWt("o mundo limpo aceita uma estatua NOVA (o sistema continua vivo)", novo != null);

				SetDeEsferas? setNovo = _sets.Find(s => !s.Eterno);
				AfirmarWt("...e o id de set volta pro comeco -- nada foi herdado do mundo velho",
						  setNovo != null && setNovo.Id == 1, $"id {setNovo?.Id}");
				AfirmarWt("...e o esferas.json gravado por esse gesto continua sem o jogador antigo",
						  !LerOuVazio(CaminhoDasEsferas).Contains(contaHeroi, StringComparison.Ordinal));
			}
			finally { SoltarOsForjados(forjados); }
		});
	}

	// =====================================================================
	// 10b. O DEFEITO INJETADO -- o `Zerar` das esferas mudo
	// =====================================================================
	/// <summary>
	/// **A SECAO 10 PROVADA DISPARANDO -- E DISPARANDO NA METADE CERTA.**
	///
	/// ============================ ELA E A UNICA QUE MOSTRA POR QUE O DISCO NAO BASTA ============================
	/// Com o `Zerar` das esferas arrancado (a inscricao ficando so com a linha de `Arquivos`), o
	/// `esferas.json` **some do disco do mesmo jeito** -- a vassoura varre a pasta inteira, sem olhar
	/// quem declarou o que. Uma bancada que so inspecionasse a pasta depois da limpeza daria 100% verde
	/// em cima do defeito.
	///
	/// O que muda esta tudo na MEMORIA, e as tres afirmacoes abaixo sao a historia inteira do modo de
	/// falha que o cabecalho da limpeza descreve:
	///
	///   1. o disco fica limpo (a injecao nao mediu errado);
	///   2. a memoria NAO fica (o defeito e real e visivel);
	///   3. e na primeira gravacao o arquivo **RESSUSCITA com o jogador dentro** -- que e o mundo
	///      "novo" voltando a ficar sujo sozinho, dias depois, sem ninguem entender por que.
	/// ========================================================================================================
	///
	/// O DEFEITO E INJETADO NO PROPRIO INSCRITO (`s.Zerar = () => { }`), como a secao 6b faz com as
	/// naves: e a forma que sobrevive a qualquer reescrita do `ZerarEsferas`.
	/// </summary>
	private void SecaoDoZerarMudoDasEsferas()
	{
		GD.Print("[wipe] -- 10b. o defeito injetado: o `Zerar` das esferas mudo --");

		SistemaDoMundo? inscrito = _sistemasDoMundo.Find(s => s.Arquivos.Contains("esferas.json"));
		if (inscrito == null)
		{
			AfirmarWt("achei o inscrito das esferas pra injetar o defeito", false,
					  "o sistema saiu do registro -- e isso e a secao 2 quem reprova");
			return;
		}

		var forjados = new List<ServerPlayer>();
		NaCaixa("esferasmudo", _ =>
		{
			try
			{
				SemearPrimeiroBoot();

				ServerPlayer? heroi = ErguerUmaEstatuaDeJogador("mudo_do_wipe", "Kami Mudo", forjados);
				if (heroi == null) { AfirmarWt("a estatua da injecao subiu", false); return; }
				string conta = heroi.Conta;

				ForcarGravacaoDeTudo();
				AfirmarWt("a estatua da injecao esta no disco ANTES (senao nao ha defeito a injetar)",
						  LerOuVazio(CaminhoDasEsferas).Contains(conta, StringComparison.Ordinal));

				Action zerarDeVerdade = inscrito.Zerar;
				inscrito.Zerar = () => { };   // inscrito, contado, e mudo
				try
				{
					ExecutarLimpeza(null);

					AfirmarWt("COM O `Zerar` MUDO, o DISCO fica limpo DO MESMO JEITO",
							  !System.IO.File.Exists(CaminhoDasEsferas),
							  "a vassoura varre a pasta inteira -- se ate isto falhar, a injecao nao "
							+ "esta medindo o que devia");

					AfirmarWt("...mas a MEMORIA acusa: a estatua do jogador continua de pe",
							  _sets.Any(s => !s.Eterno && string.Equals(s.CriadorConta, conta, StringComparison.Ordinal)),
							  "com o `Zerar` mudo e o estado sumindo assim mesmo, esta injecao nao "
							+ "prova nada -- e a secao 10 tambem nao");

					// E ESTE E O DEFEITO INTEIRO, NUMA LINHA: o disco volta a sujar sozinho.
					ForcarGravacaoDeTudo();
					AfirmarWt("...e o esferas.json RESSUSCITA com o jogador dentro na primeira gravacao",
							  LerOuVazio(CaminhoDasEsferas).Contains(conta, StringComparison.Ordinal),
							  "e o 'disco limpo com cache sujo' que a limpeza inteira existe pra evitar");
				}
				finally { inscrito.Zerar = zerarDeVerdade; }
			}
			finally { SoltarOsForjados(forjados); }
		});
	}

	// =====================================================================
	// AS FERRAMENTAS DA SECAO 10
	// =====================================================================
	/// <summary>
	/// UMA ESTATUA DO DRAGAO ERGUIDA POR UM JOGADOR, pelo verb de producao -- devolve quem a ergueu.
	///
	/// ============================ OS PORTOES SAO LIGADOS, E NAO CONTORNADOS ============================
	/// `ErguerEstatua` pergunta tres coisas (raca, classe, e territorio: Guardiao na Terra ou dono da
	/// bandeira fora dela). A bancada responde as tres NO CORPO E NO LIVRO DE TRONOS, e chama o verb --
	/// que e a diferenca entre medir o sistema e medir um atalho. Se um dia o portao mudar, esta funcao
	/// para de devolver set e a secao 10 fica vermelha na primeira linha, com o motivo no console.
	/// ==============================================================================================
	///
	/// O ZENI ENTRA CHEIO de proposito: o desejo supremo custa 2.000.000 e e comprado DENTRO do verb,
	/// entao pagar por ele e a unica forma de o `TemSupremo` do arquivo ser verdade de jogo.
	/// </summary>
	private ServerPlayer? ErguerUmaEstatuaDeJogador(string conta, string nome, List<ServerPlayer> forjados)
	{
		ZoneKey terra = ZoneKey.Premade("Earth");
		if (CorpoDaZona(terra) == null)
		{
			AfirmarWt("a Terra e um mundo do catalogo (o verb da estatua ancora nela)", false,
					  "sem corpo celeste nao ha onde erguer set nenhum");
			return null;
		}

		ServerPlayer pl = Forjar(nome, PontoDeNascimento(terra), terra, conta);
		forjados.Add(pl);

		pl.Race = "Namekian";
		pl.Class = "Dragon clan";
		pl.Ficha.Zeni = Jandirus.Core.Magic.Desejos.PrecoDoSupremo;
		_tronos["guardian"] = pl.Conta;

		ErguerEstatua(pl, "supremo");
		return _sets.Any(s => string.Equals(s.CriadorConta, conta, StringComparison.Ordinal)) ? pl : null;
	}

	/// <summary>
	/// FECHA O CANAL DE DISPUTA ABERTO POR `quem`, pelo funil de producao.
	///
	/// ============================ O QUE FICA DE FORA, E POR QUE ============================
	/// So a CONTAGEM REGRESSIVA. `TickDasSuperEsferas` derruba qualquer disputa cujo dono nao tenha
	/// `Peer` -- *"o disputante saiu do mundo"* --, e um `NetPeer` nao se fabrica sem uma conexao de
	/// verdade (a secao 7 documenta a mesma parede). Entao o canal e ABERTO pelo verb
	/// (<see cref="ReivindicarSuper"/>) e FECHADO por <see cref="FecharADisputa"/>, que e literalmente
	/// a linha seguinte do tique -- a posse, o anuncio, o recado ao ex-dono e a quebra da procuracao
	/// por tomada passam todos por la.
	/// ====================================================================================
	/// </summary>
	private void FecharOCanalDaBancada(ServerPlayer quem)
	{
		if (!_disputasDeSuper.Remove(quem.Id, out DisputaDeSuper? d)) return;
		if (_supers.Find(s => s.Numero == d.Numero) is { } alvo) FecharADisputa(quem, alvo, d);
	}

	/// <summary>Tira os corpos forjados do mundo. A limpeza ja esvazia `_players`; isto cobre o resto.</summary>
	private void SoltarOsForjados(List<ServerPlayer> forjados)
	{
		foreach (ServerPlayer f in forjados)
		{
			_players.Remove(f.Id);
			ZoneList(f.Zone.Hash).Remove(f);
		}
		forjados.Clear();
	}

	/// <summary>O arquivo cru, ou vazio. Ler o CONTEUDO e o que separa "sumiu" de "nao lembra".</summary>
	private static string LerOuVazio(string caminho)
	{
		try { return System.IO.File.Exists(caminho) ? System.IO.File.ReadAllText(caminho) : ""; }
		catch { return ""; }
	}

	// =====================================================================
	// 9. O CONTEUDO DO JOGO SOBREVIVEU -- o contra-exemplo
	// =====================================================================
	/// <summary>
	/// A FRONTEIRA DO PEDIDO, medida: *"como se tivesse acabado de rodar pela primeira vez"* e um
	/// servidor com skills, mapas e catalogos -- e nao um servidor sem jogo dentro.
	///
	/// ESTE E O CONTRA-EXEMPLO das outras secoes. Todas elas afirmam que alguma coisa SUMIU, e uma
	/// bancada so de afirmacoes desse tipo tem um jeito trivial de ficar 100% verde: apagar mais.
	/// Um `_skills = null` ou um `_noChao.Clear()` seco deixariam as secoes 4 a 8 quase intactas e
	/// o jogo inutilizavel -- sem skills, sem mobilia, sem colisao. Esta secao e a unica que cobra
	/// o outro lado.
	///
	/// O retrato do conteudo e tirado ANTES de qualquer limpeza (ver `RodarBancadaDaLimpeza`) e
	/// conferido no fim de todas elas: OITO chamadas do `ExecutarLimpeza` de producao rodaram no meio
	/// (secoes 4, 5, 6, 6b, 7, 8, 10 e 10b).
	/// </summary>
	private void SecaoDoConteudoDoJogo(List<string> antes)
	{
		GD.Print("[wipe] -- 9. o conteudo do jogo (skills, mapas, catalogos) sobreviveu --");

		// NAO PODE SER TUDO ZERO. Um retrato de conteudo vazio bateria consigo mesmo e a secao
		// inteira ficaria verde sem medir nada -- que e o cego que este projeto ja registrou.
		AfirmarWt("o retrato do conteudo tem o que medir", antes.Count > 0);
		var vazios = antes.FindAll(l => l.EndsWith(" = 0", StringComparison.Ordinal));
		AfirmarWt("nenhum catalogo estava vazio ANTES (senao a comparacao nao diria nada)",
				  vazios.Count == 0, string.Join(" | ", vazios));

		List<string> depois = RetratoDoConteudo();
		List<string> d = Diferenca(antes, depois);
		foreach (string l in d.Take(20)) GD.PrintErr($"[wipe]         {l}");
		AfirmarWt("o conteudo do jogo esta intacto depois de OITO limpezas", d.Count == 0,
				  $"{d.Count} diferenca(s)");
	}

	/// <summary>
	/// O QUE E CONTEUDO, e nao mundo: o que nasce de `res://Assets/**` a cada boot.
	///
	/// Ele fica FORA do registro dos sistemas do mundo de proposito -- nada disto tem `Zerar`, e
	/// nada disto deve ter. E a mobilia do mapa entra aqui, e nao la, pelo mesmo motivo: ela nasce
	/// do `.objetos` da zona a cada boot (ver `Obra.DoMapa`).
	/// </summary>
	private List<string> RetratoDoConteudo()
	{
		var l = new List<string>
		{
			$"skills = {_skills?.Total ?? 0}",
			$"arvores de skill = {_skills?.Arvores.Count() ?? 0}",
			$"zonas do catalogo = {_catalogo?.Todas.Count() ?? 0}",
			$"racas = {_racas?.Count ?? 0}",
			$"planetas com gravidade = {_planetas?.Total ?? 0}",
			$"moldes de npc = {_moldes?.Total ?? 0}",
			$"elos da cadeia de sagas = {_cadeia.Length}",
			$"cabelos = {_visual?.Cabelos.Count ?? 0}",
			$"roupas = {_visual?.Roupas.Count ?? 0}",
			$"mobilia do mapa de pe = {_noChao.Count(o => o.DoMapa)}",
		};
		l.Sort(StringComparer.Ordinal);
		return l;
	}
}
