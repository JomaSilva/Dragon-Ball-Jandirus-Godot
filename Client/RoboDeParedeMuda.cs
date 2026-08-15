using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ A PAREDE MUDA, FOTOGRAFADA (`--diagmuda`) ============================
/// O relato do dono: *"em VARIOS MAPAS PRE-FEITOS tem VARIOS TILES INVISIVEIS COM COLISAO... ai
/// quando eu soco ele QUEBRA e faz TODOS OS EFEITOS mas N TINHA NADA LA, so colisao"*.
///
/// O censo fecha em ZERO em 40 de 40 andares e a `--socoteste` mede, em numero, que a celula dura
/// aguenta 40 socos. **As duas ficariam verdes num mundo em que nada disso chega a tela** -- e a
/// queixa e sobre o que se VE. A memoria deste projeto ja tem o cego escrito com todas as letras:
/// "a bancada mede INTENCAO".
///
/// E ha uma dificuldade propria desta foto: **parede invisivel nao tem pixel**. Nao da pra
/// fotografar a coisa; da pra fotografar a CONSEQUENCIA dela -- o corpo parado no meio de um chao
/// que continua desenhado atras dele. Por isso toda cena aqui e uma TIRA de tres quadros:
///
///     muda-A-lookout-&lt;antes|depois&gt;.png    o Templo, coluna x=274 (266 celulas de costura)
///     muda-B-arconia-&lt;antes|depois&gt;.png    Arconia, linha y=203 -- a mesma coisa, noutro mapa
///     muda-C-quebravel-&lt;...&gt;.png           o CONTRA-EXEMPLO: parede normal, socada, caida, pisada
///
/// As duas primeiras rodam DUAS VEZES, no mesmo binario e no mesmo mapa: com `--semduro` (o mundo de
/// ANTES, em que o `.duro` fica no disco e nao e lido) e sem ele (o de DEPOIS).
/// ==================================================================================================
///
/// ============================ QUEM DECIDE O QUE E "INVISIVEL" E O DESENHO ============================
/// A celula fotografada nao vem de uma lista escrita a mao nem do `.duro`. O servidor entrega as
/// CANDIDATAS por geometria (bloqueia + chao livre dos dois lados, a mesma definicao de "CORTA area
/// andavel" do censo) e o robo pergunta ao TILEMAP MONTADO qual delas nao tem tile nenhum
/// (`World.CelulaDesenhaDeTeste`). E a mesma fonte de onde sai o pixel na tela.
///
/// **Perguntar ao `.duro` seria circular**: o `.duro` e o que esta sendo testado.
/// ======================================================================================================
///
/// ============================ POR QUE A CENA C E OBRIGATORIA ============================
/// Porque "nada quebrou" e o desfecho de um conserto certo E o desfecho de um conserto que matou o
/// cenario destrutivel inteiro -- e as duas coisas dao a mesma foto. Sem a cena C, marcar o mapa
/// todo como duro passaria verde em tudo que esta acima. Ela e tambem a resposta do pedido "o que
/// foi TIRADO do mapa nao colide mais": o robo soca a parede ate ela cair e depois ANDA POR CIMA.
/// ========================================================================================
///
/// ============================ NADA AQUI E FORJADO ============================
/// O soco e o `SocarCenario` (o mesmo da tecla), o passo e o `AplicarComando` (o mesmo atuador da IA
/// e do jogador), a viagem e o `MoveToZone` (o mesmo da troca de planeta). Ver
/// `Server/GameServer.FotoDaMuda.cs`.
/// ==============================================================================
///
/// COMO RODAR -- um processo so, e ele PRECISA de janela (no headless o `GetImage` volta vazio):
///
///     &lt;godot&gt; --path . --host --rede 7924 --diagmuda --position 1920,0 --resolution 1600x900 \
///              --raca Human --conta bancada_muda --nome Olheiro
///     ...e de novo com `--semduro` pra o mundo de ANTES.
///
/// A JANELA ABRE NO SEGUNDO MONITOR (`--position 1920,0`) porque o dono trabalha no principal.
/// </summary>
public partial class RoboDeParedeMuda : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S
		=> Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	/// <summary>Depois disto ela desiste e conta o que faltou -- bancada travada se le como bancada morta.</summary>
	private const double Paciencia = 300;

	/// <summary>
	/// OS DOIS MAPAS DA FOTO -- os dois que o censo aponta, e que o dono acharia primeiro.
	///
	/// **So o MAPA e cravado, e nao a celula nem a linha.** Uma versao anterior recebia a coluna do
	/// relatorio ("x=274" no Templo, "y=203" em Arconia) e isso envelheceu na mesma sessao: quando a
	/// agua entrou na conta de "andavel", a linha y=203 ficou sem nenhuma candidata alcancavel, porque
	/// ela esta inteira dentro do lago. O servidor varre a zona e o desenho escolhe.
	/// </summary>
	private const string ZonaA = "Lookout", ZonaB = "Arconia";

	/// <summary>Quantos socos por cena. A chance por soco e 34%, entao 40 sem cair e recusa e nao azar.</summary>
	private const int Socos = 40;

	/// <summary>
	/// ATE ONDE A CAMINHADA VAI, em tiles. Quatro: o bastante pra "atravessou" nao se confundir com
	/// "escorregou meio pixel", e pouco o bastante pra o corpo CABER no recorte de doze tiles. Sem o
	/// teto, 3,5 s de caminhada levavam o boneco pra fora do quadro e a foto do defeito ficava vazia.
	/// </summary>
	private const int TilesDaCaminhada = 4;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private double _t, _vida;
	private int _passo;

	private string _sufixo = "";     // "antes" ou "depois" -- entra no nome do arquivo
	private string _zonaAgora = "";
	private int _cx, _cy, _dx, _dy;
	private int _caiuNo;

	/// <summary>A varredura de perto: qual candidata esta sendo olhada, e o que ja se viu.</summary>
	private List<(int Cx, int Cy, int Dx, int Dy)> _candidatas = [];
	private int _iCandidata, _examinadas, _comDesenho, _semControle;

	/// <summary>Onde o corpo estava quando a caminhada comecou -- o zero da regua do `Furar`.</summary>
	private Vec2 _ondeComecou;

	/// <summary>O corpo foi FLAGRADO ocupando a celula derrubada em algum quadro da cena C.</summary>
	private bool _pisou;

	/// <summary>
	/// O PASSO JA FEZ A SUA PREPARACAO? -- e por que isto nao e um `if (_t < 0.05)`.
	///
	/// ============================ O RELOGIO NAO SERVE DE GATILHO ============================
	/// Os passos que MONTAM alguma coisa (postar o corpo, ligar o piloto automatico) rodavam sob
	/// `if (_t < 0.05) { ...; return; }`, contando com o primeiro quadro ser curto. **No headless era,
	/// e com janela nao foi**: o quadro em que a janela nasce leva bem mais que 50 ms, entao a
	/// preparacao era PULADA. O sintoma foi cruel de ler -- o log dizia "andou 11.863 px" e "PAROU
	/// nela" na mesma linha, porque a regua nunca fora zerada e o corpo nunca fora mandado andar.
	///
	/// Uma bandeira nao depende de quanto durou o quadro.
	/// ========================================================================================
	/// </summary>
	private bool _preparado;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --    " + oque);

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private const int PAssentar = 0,
					  PA_Viajar = 1, PA_Escolher = 2, PA_Encostar = 3, PA_Socar = 4, PA_Furar = 5,
					  PB_Viajar = 6, PB_Escolher = 7, PB_Encostar = 8, PB_Socar = 9, PB_Furar = 10,
					  PC_Achar = 11, PC_Socar = 12, PC_Atravessar = 13,
					  PFim = 14;

	public override void _Process(double delta)
	{
		if (_acabou) return;

		// ============================ A PACIENCIA CONTA ANTES DE CONECTAR, E TEM QUE CONTAR ============================
		// A primeira versao so somava `_vida` DEPOIS de o cliente estar conectado -- e quando a porta ja
		// estava tomada por outra rodada o robo nunca chegava la: o processo ficou de pe pra sempre, sem
		// uma linha de log, e quem esperava leu como "a bancada esta rodando". Bancada travada tem que
		// morrer falando.
		// ==============================================================================================================
		_vida += delta;
		if (_vida > Paciencia)
		{
			Nota(C is { Connected: true }
				? $"acabou a paciencia ({Paciencia:0} s) no passo {_passo}"
				: $"acabou a paciencia ({Paciencia:0} s) e o cliente NUNCA CONECTOU -- porta tomada por "
				  + "outra rodada, ou o `--host` nao subiu");
			Fechar();
			return;
		}

		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv) { Nota("sem servidor no processo (`--diagmuda` precisa de `--host`)"); Fechar(); return; }

		_t += delta;

		switch (_passo)
		{
			case PAssentar: Assentar(srv); break;

			case PA_Viajar: Viajar(srv, cli, ZonaA, "A", PA_Escolher, PB_Viajar); break;
			case PA_Escolher: Escolher(srv, cli, mundo, "A", PA_Encostar, PB_Viajar); break;
			case PA_Encostar: Encostar(srv, cli, mundo, "a1", "A1", PA_Socar); break;
			case PA_Socar: Socar(srv, cli, mundo, "a2", "A2", PA_Furar); break;
			case PA_Furar: Furar(srv, cli, mundo, "a3", "A3", "muda-A-lookout", ["a1", "a2", "a3"], PB_Viajar); break;

			case PB_Viajar: Viajar(srv, cli, ZonaB, "B", PB_Escolher, PC_Achar); break;
			case PB_Escolher: Escolher(srv, cli, mundo, "B", PB_Encostar, PC_Achar); break;
			case PB_Encostar: Encostar(srv, cli, mundo, "b1", "B1", PB_Socar); break;
			case PB_Socar: Socar(srv, cli, mundo, "b2", "B2", PB_Furar); break;
			case PB_Furar: Furar(srv, cli, mundo, "b3", "B3", "muda-B-arconia", ["b1", "b2", "b3"], PC_Achar); break;

			case PC_Achar: C_Achar(srv, cli); break;
			case PC_Socar: C_Socar(srv, cli, mundo); break;
			case PC_Atravessar: C_Atravessar(srv, cli, mundo, delta); break;

			default: Fechar(); break;
		}
	}

	/// <summary>
	/// POSTA O CORPO **NAS DUAS COPIAS** -- servidor e cliente.
	///
	/// ============================ A LICAO QUE ESTA FUNCAO PAGA ============================
	/// A primeira versao so chamava o `PostarNaFotoDaMuda` (servidor). Em Lookout deu certo por
	/// acaso: o robo examinou UMA candidata, que era a mesma onde o `MoveToZone` tinha largado o
	/// corpo, entao as duas copias ja concordavam. Em Arconia ele examinou tres -- o corpo do
	/// SERVIDOR andou duas celulas e o do CLIENTE ficou onde estava. Dai em diante o cliente predizia
	/// o passo a partir do lugar velho, o servidor corrigia de volta, e a caminhada rendeu **0 px**
	/// com o comando dado e as duas colisoes concordando que o caminho estava aberto.
	///
	/// Um corpo so tem que estar num lugar so.
	/// ======================================================================================
	/// </summary>
	/// <summary>
	/// O ALVO DA CAMINHADA: o centro da celula da cena, e mais tres tiles ADIANTE no mesmo rumo.
	///
	/// O centro da celula e o que faz a linha reta passar por DENTRO dela (ver `World.IrAteDeTeste`);
	/// os tres tiles a mais existem porque o piloto desliga a 64 px do destino -- mirar no proprio
	/// centro faria o corpo parar antes de chegar.
	/// </summary>
	private Vec2 AlvoDaCaminhada()
	{
		const int T = ZoneCollision.TileSize;
		return new Vec2((_cx - _dx * 3 + 0.5f) * T, (_cy - _dy * 3 + 0.5f) * T);
	}

	private bool Postar(Jandirus.Server.GameServer srv, GameClient cli, World mundo,
						int cx, int cy, int dx, int dy)
	{
		if (!srv.PostarNaFotoDaMuda(cli.LocalId, cx, cy, dx, dy)) return false;
		mundo.TeleportarLocalDeTeste(srv.OndeEstouNaFotoDaMuda(cli.LocalId));
		return true;
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; _preparado = false; }

	// =====================================================================
	// 0) O BERCO ASSENTA
	// =====================================================================
	private void Assentar(Jandirus.Server.GameServer srv)
	{
		// TRES SEGUNDOS: um corpo recem-criado entra nocauteado por um instante e o `PodeMexerOCorpo`
		// recusaria o passo com razao. Esperar o estado e mais honesto que escrever `KO = false`.
		if (_t < 3) return;

		// A CENA TEM QUE SER LEGIVEL -- ver `Iluminacao.MeioDiaDeTeste`. A primeira rodada com janela
		// pegou o Templo de madrugada e as seis fotos sairam azul-escuras, com tudo verde no log.
		Iluminacao.MeioDiaDeTeste = true;

		_sufixo = srv.SemDuroNaFotoDaMuda ? "antes" : "depois";
		Nota(srv.SemDuroNaFotoDaMuda
			? "`--semduro` LIGADO: este e o mundo de ANTES -- o `.duro` esta no disco e nao foi lido"
			: "`--semduro` desligado: este e o mundo de DEPOIS -- o `.duro` foi lido no boot");
		Virar(PA_Viajar);
	}

	// =====================================================================
	// AS DUAS COSTURAS -- o mesmo roteiro, dois mapas
	// =====================================================================
	/// <summary>
	/// VIAJA PRA ZONA. Vai pra PRIMEIRA candidata so pra ter destino -- a escolha de verdade e do
	/// passo seguinte, e ela depende do tilemap, que so existe DEPOIS de chegar.
	/// </summary>
	private void Viajar(Jandirus.Server.GameServer srv, GameClient cli, string zona,
						string cena, int proximo, int pula)
	{
		var lista = srv.CandidatasDaMuda(zona);
		Conferir(lista.Count > 0,
			$"{cena}: o servidor lista {lista.Count} divisoria(s) em {zona} -- celulas que bloqueiam, "
			+ "estao fora da beirada e tem chao ANDAVEL (nem parede nem agua) dos dois lados");
		if (lista.Count == 0) { Virar(pula); return; }

		_zonaAgora = zona;
		_candidatas = lista;
		_iCandidata = _examinadas = _comDesenho = _semControle = 0;
		Conferir(srv.ViajarNaFotoDaMuda(cli.LocalId, zona, lista[0].Cx + lista[0].Dx, lista[0].Cy + lista[0].Dy),
			$"{cena}: ...e o corpo viajou pra la pelo `MoveToZone` de producao");
		Virar(proximo);
	}

	/// <summary>
	/// O DESENHO ESCOLHE A CELULA -- a linha mais importante desta bancada, e ela custa uma CAMINHADA.
	///
	/// ============================ POR QUE NAO DA PRA PERGUNTAR DE LONGE ============================
	/// A primeira versao varreu as 266 candidatas do Templo de uma vez, de onde o corpo caiu, e
	/// respondeu "266 nao desenham". **Era mentira, e a mentira era conveniente**: o cenario deste
	/// port e montado POR PEDACO (a memoria "cenario por PEDACO"), entao o `TileMapLayer` so tem
	/// celula perto da camera -- tudo que esta longe responde "nao desenha" porque ainda nao foi
	/// pintado. Em Arconia isso escolheu uma parede DESENHADA como se fosse muda, e o passo seguinte,
	/// que perguntou de perto, se contradisse: `B1 ... nao tem tile` FALHOU logo depois de a escolha
	/// dizer que nao tinha.
	///
	/// Entao a pergunta e feita **com o corpo encostado**, uma candidata por vez.
	/// ==============================================================================================
	///
	/// ============================ O CONTROLE, e sem ele nada disto vale ============================
	/// "A celula alvo nao tem tile" e indistinguivel de "o pedaco dela ainda nao foi pintado". O que
	/// separa os dois e o CHAO ONDE O CORPO ESTA: se a celula sob os pes desenha e a da frente nao,
	/// o pedaco esta pintado e o vazio e do mapa. Sem essa linha a bancada aprovaria um mapa que nao
	/// carregou.
	/// ==============================================================================================
	/// </summary>
	private void Escolher(Jandirus.Server.GameServer srv, GameClient cli, World mundo,
						  string cena, int proximo, int pula)
	{
		if (_iCandidata >= _candidatas.Count)
		{
			Nota($"{cena}: examinei {_examinadas} candidata(s) de perto -- {_comDesenho} desenham, "
			   + $"{_semControle} sem pedaco pintado");
			Conferir(false, $"{cena}: ha ao menos uma celula que BLOQUEIA e NAO DESENHA em {_zonaAgora}");
			Virar(pula);
			return;
		}

		(int cx, int cy, int dx, int dy) = _candidatas[_iCandidata];

		// POSTA E ESPERA O PINTOR. O primeiro quadro depois de mudar de lugar tem o pedaco antigo.
		if (!_preparado) { _preparado = true; _t = 0; Postar(srv, cli, mundo, cx, cy, dx, dy); return; }
		if (_t < 1.6) return;

		if (mundo.CamadasDoCenarioDeTeste.Length == 0)
		{
			// O cenario ainda nem montou: espera de novo, sem gastar candidata.
			if (_vida < Paciencia - 30) { _t = 0; return; }
			Conferir(false, $"{cena}: o cenario de {_zonaAgora} nunca montou na tela");
			Virar(pula);
			return;
		}

		bool chaoDesenha = mundo.CelulaDesenhaDeTeste(cx + dx, cy + dy);   // o CONTROLE -- ver a nota
		bool alvoDesenha = mundo.CelulaDesenhaDeTeste(cx, cy);
		_examinadas++;

		if (!chaoDesenha) { _semControle++; _iCandidata++; Virar(_passo); return; }
		if (alvoDesenha) { _comDesenho++; _iCandidata++; Virar(_passo); return; }

		(_cx, _cy, _dx, _dy) = (cx, cy, dx, dy);
		Nota($"{cena}: examinei {_examinadas} de perto, das {_candidatas.Count} divisorias de "
		   + $"{_zonaAgora} -- {_comDesenho} desenham, {_semControle} sem pedaco pintado");
		Conferir(true, $"{cena}: o CHAO sob os pes em ({cx + dx},{cy + dy}) DESENHA -- o pedaco esta "
					 + "pintado, entao \"a da frente nao desenha\" e do mapa e nao do carregamento");
		Conferir(Postar(srv, cli, mundo, _cx, _cy, _dx, _dy),
			$"{cena}: o corpo esta postado em ({_cx + _dx},{_cy + _dy}), olhando pra costura ({_cx},{_cy})");
		Virar(proximo);
	}

	/// <summary>
	/// A FOTO DO PROBLEMA: o corpo encostado numa parede que nao tem pixel.
	///
	/// Ela e o CONTROLE das outras duas: sem ela, "o corpo nao atravessou" nao se distingue de "o
	/// corpo nunca chegou perto".
	/// </summary>
	private void Encostar(Jandirus.Server.GameServer srv, GameClient cli, World mundo,
						  string nome, string cena, int proximo)
	{
		if (_t < 1.5) return;

		(bool bloqueia, bool duro, bool agua) = srv.CelulaNaFotoDaMuda(_zonaAgora, _cx, _cy);
		Tomar(mundo, $"muda-{nome}-{_sufixo}",
			  $"{cena} ({_sufixo}): {_zonaAgora} ({_cx},{_cy}) -- BLOQUEIA e NAO DESENHA. duro={duro}");

		Conferir(bloqueia, $"{cena}: a celula ({_cx},{_cy}) BLOQUEIA -- e a parede que o dono esbarra");
		Conferir(!mundo.CelulaDesenhaDeTeste(_cx, _cy),
			$"{cena}: ...e nao tem tile em camada nenhuma -- \"N TEM SPRITE NENHUM\", na letra");
		// O CONTROLE, DE NOVO E NO INSTANTE DA FOTO (ver a nota do `Escolher`): a celula sob os pes
		// desenha. Sem ele, esta foto e indistinguivel da foto de um pedaco que nao carregou.
		Conferir(mundo.CelulaDesenhaDeTeste(_cx + _dx, _cy + _dy),
			$"{cena}: ...enquanto o chao sob os pes ({_cx + _dx},{_cy + _dy}) DESENHA na mesma tela");
		Conferir(duro == !srv.SemDuroNaFotoDaMuda,
			$"{cena}: e o `destroyable = 0` do DM chegou (duro={duro}) -- o esperado com `--semduro` "
			+ $"{(srv.SemDuroNaFotoDaMuda ? "LIGADO" : "desligado")}");
		Virar(proximo);
	}

	/// <summary>
	/// SOCA A COSTURA <see cref="Socos"/> VEZES -- e o desfecho e a diferenca inteira entre as rodadas.
	///
	/// O som e a faisca do baque saem nas DUAS (no original os sons vem ANTES do `Destroy()`,
	/// `attack_proc.dm:99-105`): o que muda e a celula ficar ou nao.
	/// </summary>
	private void Socar(Jandirus.Server.GameServer srv, GameClient cli, World mundo,
					   string nome, string cena, int proximo)
	{
		if (_t < 0.6) return;

		_caiuNo = 0;
		for (int i = 1; i <= Socos; i++)
		{
			if (!srv.SocarNaFotoDaMuda(cli.LocalId))
			{
				Conferir(false, $"{cena}: o punho nao acha cenario nenhum em ({_cx},{_cy}) -- palco montado errado");
				Virar(proximo);
				return;
			}
			if (!srv.CelulaNaFotoDaMuda(_zonaAgora, _cx, _cy).Bloqueia) { _caiuNo = i; break; }
		}

		(bool bloqueia, bool duro, bool agua) = srv.CelulaNaFotoDaMuda(_zonaAgora, _cx, _cy);
		Tomar(mundo, $"muda-{nome}-{_sufixo}",
			  _caiuNo > 0
				? $"{cena} ({_sufixo}): CAIU no {_caiuNo}o soco -- o nada quebrou (bloqueia={bloqueia})"
				: $"{cena} ({_sufixo}): {Socos} socos e ela CONTINUA de pe (bloqueia={bloqueia} duro={duro})");

		// ============================ A EXIGENCIA MUDA COM A CHAVE, E TEM QUE MUDAR ============================
		// No mundo de ANTES a queda e o comportamento ESPERADO -- e o defeito que se quer mostrar. Cobrar
		// "ela ficou de pe" ali reprovaria a foto do problema, e treinaria quem le o log a ignorar o log.
		// ======================================================================================================
		if (srv.SemDuroNaFotoDaMuda)
			Conferir(_caiuNo > 0,
				$"{cena} (ANTES): a costura invisivel CAIU no {_caiuNo}o soco -- e o defeito do dono, "
				+ "reproduzido com o `.duro` intacto no disco e apenas nao lido");
		else
			Conferir(_caiuNo == 0,
				$"{cena} (DEPOIS): {Socos} socos e a costura invisivel NAO cedeu -- o `destroyable = 0` "
				+ "do `turf/proc/Destroy()` chegou ao servidor");
		Virar(proximo);
	}

	/// <summary>
	/// ANDA CONTRA A COSTURA -- e a CELULA em que o corpo termina e o que fecha a foto.
	///
	/// ============================ QUEM ANDA E O CLIENTE, E NAO O SERVIDOR ============================
	/// A primeira versao empurrava o corpo pelo `AplicarComando` do servidor, quadro a quadro, e o
	/// resultado foi um corpo que dava passo de 0,8 px por tique e nao saia do lugar em 2,5 s. O motivo
	/// e a arquitetura: o cliente tambem esta mandando o rumo DELE (parado) e a posicao ia e voltava.
	/// Duas maos no mesmo corpo se anulam.
	///
	/// O `AndarDeTeste` e o PILOTO AUTOMATICO do cliente -- o mesmo caminho de quem clica pra andar --,
	/// e ele passa pelo `MoveRules` e pela conferencia do servidor do jeito de sempre. Uma so mao.
	/// =================================================================================================
	/// </summary>
	private void Furar(Jandirus.Server.GameServer srv, GameClient cli, World mundo,
					   string nome, string cena, string tira, string[] pedacos, int proximo)
	{
		if (!_preparado)
		{
			_preparado = true;
			_t = 0;
			_ondeComecou = srv.OndeEstouNaFotoDaMuda(cli.LocalId);
			mundo.IrAteDeTeste(AlvoDaCaminhada());   // alvo ABSOLUTO: atravessa a costura pelo centro
			return;
		}
		// PARA DEPOIS DE `TilesDaCaminhada`, e nao so no relogio: andando 3,5 s o corpo sai do
		// enquadramento e a foto do "atravessou" vira uma foto de chao vazio. O que precisa caber no
		// quadro e a costura E o corpo do outro lado dela.
		Vec2 agoraPx = srv.OndeEstouNaFotoDaMuda(cli.LocalId);
		bool longeOBastante = (agoraPx - _ondeComecou).Length >= TilesDaCaminhada * ZoneCollision.TileSize;
		if (!longeOBastante && _t < 3.5) return;
		mundo.PararDeTeste();

		Vec2 fim = srv.OndeEstouNaFotoDaMuda(cli.LocalId);
		float andou = (fim - _ondeComecou).Length;
		(int cxAgora, int cyAgora) = srv.CelulaDoCorpoNaFotoDaMuda(cli.LocalId);
		bool passou = cxAgora != _cx + _dx || cyAgora != _cy + _dy;
		(bool bloqueia, _, _) = srv.CelulaNaFotoDaMuda(_zonaAgora, _cx, _cy);

		// AS DUAS COPIAS DA COLISAO, lado a lado (ver `World.CelulaBloqueiaDeTeste`): quem da o
		// primeiro passo e o cliente, entao o servidor ter aberto a celula nao basta.
		bool bloqueiaNoCliente = mundo.CelulaBloqueiaDeTeste(_cx, _cy);
		var corredor = new System.Text.StringBuilder();
		for (int k = 0; k <= 3; k++)
			corredor.Append($"({_cx - _dx * k},{_cy - _dy * k})="
						  + $"{(srv.CelulaNaFotoDaMuda(_zonaAgora, _cx - _dx * k, _cy - _dy * k).Bloqueia ? "X" : ".")}"
						  + $"{(mundo.CelulaBloqueiaDeTeste(_cx - _dx * k, _cy - _dy * k) ? "X" : ".")} ");
		Nota($"{cena}: colisao da celula ({_cx},{_cy}) -- servidor={bloqueia} cliente={bloqueiaNoCliente}"
		   + $" | corredor (servidor/cliente): {corredor}");

		Tomar(mundo, $"muda-{nome}-{_sufixo}",
			  $"{cena} ({_sufixo}): ANDANDO contra a costura (ate {TilesDaCaminhada} tiles ou 3,5 s) -- "
			  + $"andou {andou:0} px, "
			  + $"corpo em ({cxAgora},{cyAgora}), {(passou ? "SAIU DO POSTO" : "PAROU nela")}");

		if (srv.SemDuroNaFotoDaMuda)
			Conferir(passou, $"{cena} (ANTES): o corpo saiu do posto e ATRAVESSOU o que era parede "
						   + $"({andou:0} px) -- o soco abriu buraco num lugar que no BYOND nao abre nunca");
		else
		{
			Conferir(!passou, $"{cena} (DEPOIS): o corpo PAROU na costura depois de {Socos} socos "
							+ $"(andou {andou:0} px em 3,5 s, continua em ({cxAgora},{cyAgora}))");
			Conferir(bloqueia, $"{cena} (DEPOIS): ...e a celula continua bloqueando");
		}

		Montar($"{tira}-{_sufixo}.png", [.. pedacos.Select(p => $"muda-{p}-{_sufixo}")]);
		Virar(proximo);
	}

	// =====================================================================
	// C) O CONTRA-EXEMPLO: o que SE SOCA continua caindo, e se anda por cima
	// =====================================================================
	private void C_Achar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		// A busca e na zona ONDE O CORPO ESTA. Trocar de zona aqui recarregaria o cenario e a cena
		// perderia a comparacao com o quadro anterior.
		(bool achou, int cx, int cy, int dx, int dy) = srv.AcharParedeQueCedeNaMuda(cli.LocalId);
		Conferir(achou, "C: achei uma parede NORMAL (bloqueia, fora da beirada, e NAO dura) pra socar");
		if (!achou) { Virar(PFim); return; }

		(_cx, _cy, _dx, _dy) = (cx, cy, dx, dy);
		_zonaAgora = srv.ZonaNaFotoDaMuda(cli.LocalId);
		Conferir(Postar(srv, cli, World.Instancia!, cx, cy, dx, dy),
			$"...e o corpo encostou nela em ({cx},{cy}), em {_zonaAgora}");
		Virar(PC_Socar);
	}

	private void C_Socar(Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		if (_t < 2.0) return;

		Tomar(mundo, $"muda-c1-{_sufixo}",
			  $"C1 ({_sufixo}): a parede NORMAL de pe, em {_zonaAgora} ({_cx},{_cy})");

		_caiuNo = 0;
		for (int i = 1; i <= Socos; i++)
		{
			if (!srv.SocarNaFotoDaMuda(cli.LocalId))
			{
				Conferir(false, $"C: o punho nao acha a parede em ({_cx},{_cy}) -- palco montado errado");
				Virar(PFim);
				return;
			}
			if (!srv.CelulaNaFotoDaMuda(_zonaAgora, _cx, _cy).Bloqueia) { _caiuNo = i; break; }
		}

		Conferir(_caiuNo > 0,
			$"C: a parede NORMAL caiu no {_caiuNo}o soco -- o cenario destrutivel continua vivo "
			+ "(sem esta linha, marcar o mapa inteiro como duro passaria verde em tudo acima)");
		Virar(PC_Atravessar);
	}

	private void C_Atravessar(Jandirus.Server.GameServer srv, GameClient cli, World mundo, double delta)
	{
		// O pacote de "esta celula caiu" viaja e o cliente troca o tile por terra batida. Andar por
		// cima antes disso daria a foto certa com o desenho velho. Quem anda e o PILOTO AUTOMATICO do
		// cliente -- ver a nota do `Furar` sobre as duas maos no mesmo corpo.
		if (!_preparado) { _preparado = true; _t = 0; _pisou = false; mundo.IrAteDeTeste(AlvoDaCaminhada()); return; }

		// ============================ "PISOU" E UM FLAGRANTE, E NAO UMA POSE FINAL ============================
		// A primeira versao so olhava onde o corpo estava no FIM da caminhada, e reprovava a rodada
		// certa: andando 4 s o corpo atravessa a celula derrubada e segue -- ele acaba tres tiles alem
		// dela, e "esta em cima?" responde nao. O que se quer provar e que ele PASSOU POR ALI, entao a
		// medicao e por flagrante, quadro a quadro.
		// ======================================================================================================
		(int cxAqui, int cyAqui) = srv.CelulaDoCorpoNaFotoDaMuda(cli.LocalId);
		if (cxAqui == _cx && cyAqui == _cy) _pisou = true;

		// ...e PARA DEPOIS DE PISAR, e nao no relogio -- o mesmo teto do `Furar`, pelo mesmo motivo:
		// andando 4 s o corpo ia parar dez tiles adiante e a foto do "pisou" saia sem ninguem nela.
		if (!_pisou && _t < 4.0) return;
		mundo.PararDeTeste();

		(int cxAgora, int cyAgora) = srv.CelulaDoCorpoNaFotoDaMuda(cli.LocalId);
		bool emCima = _pisou || (cxAgora == _cx && cyAgora == _cy);
		(bool bloqueia, _, _) = srv.CelulaNaFotoDaMuda(_zonaAgora, _cx, _cy);

		Tomar(mundo, $"muda-c2-{_sufixo}",
			  $"C2 ({_sufixo}): a celula ({_cx},{_cy}) caiu no {_caiuNo}o soco e o corpo "
			  + $"{(emCima ? "PASSOU POR CIMA" : "NAO passou")} dela -- agora em ({cxAgora},{cyAgora})");

		Conferir(!bloqueia, "C: a celula derrubada nao bloqueia mais");
		Conferir(emCima, "C: ...e o corpo ANDOU POR CIMA dela (flagrado ocupando a celula) -- o que sai do "
					   + "mapa some da colisao tambem");

		Montar($"muda-C-quebravel-{_sufixo}.png", [$"muda-c1-{_sufixo}", $"muda-c2-{_sufixo}"]);
		Virar(PFim);
	}

	// =====================================================================
	// AS FOTOS -- mesma receita do `RoboDeColisao`, e de proposito
	// =====================================================================
	private sealed class Tomada
	{
		public required string Nome;
		public required string Rotulo;
		public Image? Quadro;
		public Image? Perto;
	}

	/// <summary>
	/// O recorte, e por que ele e obrigatorio: num quadro de 1600x900 a 1x, um boneco de 32 px encostado
	/// numa linha que nao tem pixel e ilegivel. Nearest, sempre -- interpolar pixel art e inventar. Os
	/// dois arquivos ficam: a tela cheia prova o LUGAR, o recorte prova a CENA.
	///
	/// ============================ DOZE TILES, E ANCORADO NA COSTURA ============================
	/// A primeira tira recortou 10 tiles CENTRADOS NO CORPO, e o resultado foi tres quadros
	/// praticamente identicos: o boneco fica no meio dos tres por construcao, entao a unica coisa que
	/// a cena tinha pra mostrar -- que num deles ele ATRAVESSOU e no outro nao -- some junto com o
	/// enquadramento. Centrar no corpo e a escolha certa pra uma foto DE CORPO e a errada pra uma foto
	/// de LUGAR.
	///
	/// Agora a ancora e a CELULA, que nao se mexe: o corpo entra e sai do quadro, e e isso que o olho
	/// tem que ler. Doze tiles a 2x em vez de dez a 3x pelo mesmo motivo -- cabe a caminhada inteira.
	/// ==========================================================================================
	/// </summary>
	private const int LadoDoRecorte = 384, EscalaDoRecorte = 2;

	private readonly List<Tomada> _tomadas = [];

	private Tomada? Achar(string nome) => _tomadas.Find(t => t.Nome == nome);

	private void Tomar(World mundo, string nome, string rotulo)
	{
		var t = new Tomada { Nome = nome, Rotulo = rotulo };

		Image? tela = GetViewport()?.GetTexture()?.GetImage();
		if (tela == null || tela.IsEmpty())
		{
			Nota($"{rotulo}: SEM FOTO (headless nao renderiza -- rode com janela)");
			_tomadas.Add(t);
			_linhas.Add($"  cena  {rotulo}");
			return;
		}

		tela.Convert(Image.Format.Rgba8);
		t.Quadro = tela;
		Gravar(tela, nome + ".png", rotulo);

		{
			// A ANCORA E A CELULA DA CENA, e nao o corpo -- ver a nota do `LadoDoRecorte`.
			Transform2D tr = GetViewport()?.CanvasTransform ?? Transform2D.Identity;
			var noMundo = new Vector2((_cx + 0.5f) * ZoneCollision.TileSize,
									  (_cy + 0.5f) * ZoneCollision.TileSize);
			Image perto = tela.GetRegion(Janela(tela, tr * noMundo));
			perto.Convert(Image.Format.Rgba8);
			perto.Resize(perto.GetWidth() * EscalaDoRecorte, perto.GetHeight() * EscalaDoRecorte,
						 Image.Interpolation.Nearest);
			t.Perto = perto;
			Gravar(perto, nome + "-perto.png", rotulo + " (recorte)");
		}

		_tomadas.Add(t);
		_linhas.Add($"  foto  {rotulo}");
	}

	private static Rect2I Janela(Image tela, Vector2 centro)
	{
		int lado = Math.Min(LadoDoRecorte, Math.Min(tela.GetWidth(), tela.GetHeight()));
		int x0 = Math.Clamp((int)centro.X - lado / 2, 0, tela.GetWidth() - lado);
		int y0 = Math.Clamp((int)centro.Y - lado / 2, 0, tela.GetHeight() - lado);
		return new Rect2I(x0, y0, lado, lado);
	}

	private void Gravar(Image img, string arquivo, string rotulo)
	{
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + arquivo);
			img.SavePng(caminho);
			_linhas.Add($"         {caminho}");
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	/// <summary>A tira colada -- os quadros da cena lado a lado, feita dos RECORTES.</summary>
	private void Montar(string arquivo, string[] nomes)
	{
		var uteis = new List<Image>();
		foreach (string n in nomes) if ((Achar(n)?.Perto ?? Achar(n)?.Quadro) is { } q) uteis.Add(q);
		if (uteis.Count == 0) return;

		const int Vao = 8;
		int larg = uteis[0].GetWidth(), alt = uteis[0].GetHeight();
		Image colagem = Image.CreateEmpty(larg * uteis.Count + Vao * (uteis.Count - 1), alt,
										  false, Image.Format.Rgba8);
		colagem.Fill(new Color(0.06f, 0.06f, 0.06f));

		for (int i = 0; i < uteis.Count; i++)
		{
			// O `BlitRect` EXIGE O MESMO FORMATO nos dois lados e CALA quando nao tem -- a primeira tira
			// da `--diagraio` saiu um retangulo preto sem erro nenhum no log.
			var pedaco = (Image)uteis[i].Duplicate();
			pedaco.Convert(Image.Format.Rgba8);
			if (pedaco.GetWidth() != larg || pedaco.GetHeight() != alt) continue;
			colagem.BlitRect(pedaco, new Rect2I(Vector2I.Zero, pedaco.GetSize()),
							 new Vector2I(i * (larg + Vao), 0));
		}

		Gravar(colagem, arquivo, "a tira");
	}

	// =====================================================================
	// O FIM
	// =====================================================================
	private void Fechar()
	{
		_acabou = true;

		GD.Print("\n[muda] ===== A PAREDE MUDA, FOTOGRAFADA =====");
		foreach (string l in _linhas) GD.Print("[muda] " + l);
		GD.Print(_falhas.Count == 0
			? $"[muda] ===== TUDO OK ({_tomadas.Count} tomadas) ====="
			: $"[muda] ===== {_falhas.Count} FALHA(S) =====\n[muda]   "
			  + string.Join("\n[muda]   ", _falhas));
		GetTree().Quit();
	}
}
