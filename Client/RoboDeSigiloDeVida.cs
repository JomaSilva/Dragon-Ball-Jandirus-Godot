using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO SIGILO DA VIDA ALHEIA (`--vida a` e `--vida b`). DOIS PROCESSOS, e nao cabe em um.
///
/// ============================ O QUE ELA MEDE, E POR QUE NAO E UM DESENHO ============================
/// O dono pediu: *"tire a barra de vida q aparece acima da cabeca dos personagens, n deveria dar pra
/// ver o hp dos outros, so ter uma ideia com base nos FERIMENTOS"*. Isso NAO e "sumir com um
/// desenho" -- e uma decisao sobre QUE INFORMACAO EXISTE no jogo. Apagar so o desenho deixaria o
/// numero viajando no fio, e ele voltaria na primeira tela que alguem escrevesse.
///
/// Este port ja pagou essa conta uma vez, no sigilo do BP: **escrever o corte nao e aplicar o
/// corte** -- a API do corte ficou 100% orfa e o BP vazava por sete lugares. A licao virou esta
/// bancada, e por isso ela nao pergunta "a barra sumiu?". Ela pergunta quatro coisas que so duas
/// telas respondem:
///
///   1. o cliente do OUTRO tem, em algum lugar, o numero da minha vida?  (tem que NAO ter)
///   2. e o DONO da ficha continua tendo a dele?                          (tem que ter -- sem este
///      contra-exemplo, "nao mandar nada" ficaria verde e a HUD estaria morta)
///   3. o GRAU de ferida ainda viaja e ainda MUDA com o dano?             (se congelar, o corpo para
///      de contar a historia e o dono perde o sinal que ele mandou manter)
///   4. e continua nao sobrando desenho nenhum sobre cabeca nenhuma?
///
/// ============================ A TENSAO, MEDIDA E NAO SUPOSTA ============================
/// Um pedaco da vida alheia TEM que viajar, senao o corpo machucado para de parecer machucado. A
/// pergunta certa nunca foi "mando ou nao mando", e sim **"mando o NUMERO ou mando o GRAU?"**. Esta
/// bancada mede os dois lados da mesma briga ao mesmo tempo e compara:
///
///   * o FERIDO conta por quantos valores distintos a PROPRIA vida passou (numero: continuo);
///   * o OLHADOR conta por quantas MASCARAS distintas ele viu daquele mesmo corpo (grau: 16 degraus
///     por camada, e so quando muda).
///
/// Se os dois numeros forem parecidos, o grau esta fino demais pra ser grau -- e a informacao
/// continua no jogo por outro nome. E por isso a comparacao e uma checagem e nao uma nota.
///
/// ============================ OS DOIS PAPEIS ============================
/// `--vida a` e O FERIDO. Ele hospeda, apanha, se cura sozinho no meio da rodada e mede a PROPRIA
/// vida (familia 2). Escreve tudo o que mediu num arquivo do `user://` -- e a unica regua possivel
/// pra "o outro nao sabe o meu numero" e o que o DONO do corpo sabe, e isso mora do outro lado do
/// soquete.
///
/// `--vida b` e O OLHADOR. Ele olha o corpo alheio nascer amputado, ve a amputacao sumir na cura
/// (que ele NAO causou) e depois bate pra ver o grau subir de novo. Ele e quem tem placar das
/// familias 1, 3, 4, 5 e 6.
///
/// ============================ POR QUE O SERVIDOR PRECISA DO `--feridateste` ============================
/// A amputacao (familia 4) nao pode depender do sorteio de uma briga letal -- decepar exige golpe
/// letal num membro JA zerado, e uma bancada que so as vezes arranca um braco nao mede nada nas
/// outras vezes. O `--feridateste` faz todo corpo NASCER com um braco e uma perna arrancados, o que
/// da o caso pronto; e a CURA do papel A e o controle que falta (o membro tem que VOLTAR, senao
/// "sempre aceso" passaria verde).
///
/// COMO RODAR -- ver `testar-sigilo-de-vida.bat`. Os dois na mesma porta, o A primeiro.
/// ====================================================================================================
/// </summary>
public partial class RoboDeSigiloDeVida : Node
{
	/// <summary>`a` apanha e mede a propria vida, `b` olha o corpo alheio. Vem do `--vida`.</summary>
	public string Papel = "b";

	/// <summary>A conta do A, pro `admin_promover` -- ver <see cref="PassoDoFerido"/>.</summary>
	public string Conta = "";

	/// <summary>O nome do OUTRO (`--vidaalvo`). O berco tem NPC: "o primeiro do snapshot" nao serve.</summary>
	public string Alvo = "";

	/// <summary>
	/// Segundos de rodada antes do veredito. O A julga em <see cref="Fim"/>, o B seis segundos depois
	/// (ele le o placar do A -- ver <see cref="OContraExemploDoOutroLado"/>).
	///
	/// 90 E MEDIDO, e nao um numero redondo: com 60 a fase de pancada fechava antes de o corpo do outro
	/// chegar a SANGRAR (o sangue so comeca aos 55% de dano num membro), e a familia do respingo
	/// reprovava por falta de estrago. Rodada curta demais tem sintoma proprio no relatorio -- ver a
	/// linha de controle em <see cref="ORespingoVemDoGrau"/>.
	/// </summary>
	public double Fim = 90;

	// =====================================================================
	// O PLACAR
	// =====================================================================
	private int _oks;
	private readonly List<string> _falhas = [];

	private void Conferir(bool ok, string oque)
	{
		if (ok) _oks++; else _falhas.Add(oque);
		GD.Print($"[vida-{Papel}] " + (ok ? "  ok   " : "  FALHA") + "  " + oque);
	}

	private void Anotar(string s)
	{
		GD.Print($"[vida-{Papel}] {s}");
	}

	// =====================================================================
	// ENTRADA
	// =====================================================================
	private bool _dentro, _fechou;
	private double _relogio;

	public override void _Ready()
	{
		if (GameClient.Instance is not { } cli) return;
		// METODO NOMEADO E `-=` NO `_ExitTree`, nunca lambda: lambda nao da pra cancelar, e este
		// projeto ja pagou 19 assinaturas orfas por ciclo de relog por causa disso.
		cli.Joined += AoEntrar;
		cli.SnapshotReceived += AoReceberSnapshot;
		cli.Golpe += AoGolpe;
		cli.SheetUpdated += AoReceberFicha;
		cli.CorpoAtualizado += AoReceberOCorpo;
		GD.Print($"[vida-{Papel}] no ar (alvo '{Alvo}', fim {Fim}s)");

		// O `Joined` JA PASSOU quando este no nasce (ele e criado dentro da resposta dele) -- ver o
		// bloco irmao no `RoboDeDoisCorpos`, que custou uma rodada inteira em silencio.
		if (cli.LocalId != 0) AoEntrar(cli.LocalId, cli.ZonaDeTeste, default, cli.LocalName);
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is not { } cli) return;
		cli.Joined -= AoEntrar;
		cli.SnapshotReceived -= AoReceberSnapshot;
		cli.Golpe -= AoGolpe;
		cli.SheetUpdated -= AoReceberFicha;
		cli.CorpoAtualizado -= AoReceberOCorpo;
	}

	private void AoReceberFicha(SheetState f) => _pacotesDeFicha++;
	private void AoReceberOCorpo(List<Protocol.ParteState> p) => _pacotesDeCorpo++;

	private void AoEntrar(int id, Jandirus.Core.World.ZoneKey z, Jandirus.Core.World.Vec2 spawn, string nome)
	{
		if (_dentro) return;
		_dentro = true;
		_relogio = 0;
		GD.Print($"[vida-{Papel}] ENTREI: id {id} nome '{nome}' zona {z}");
	}

	/// <summary>
	/// O snapshot serve pra UMA coisa aqui, e ela nao e vida: saber ONDE o outro esta, pra andar ate
	/// ele. E, de brinde, e a prova viva da familia 1 -- este e o pacote que carregava o byte, e o
	/// tratador dele nao tem mais o que ler sobre a vida de ninguem.
	/// </summary>
	private void AoReceberSnapshot(List<EntityState> estados)
	{
		if (_idDoOutro == 0) return;
		foreach (EntityState e in estados)
			if (e.Id == _idDoOutro) _posDoOutro = new Vector2(e.Pos.X, e.Pos.Y);
	}

	/// <summary>Quando o outro CLIENTE apareceu. O A so se cura depois que ha quem veja.</summary>
	private double _viOOutroEm;

	private int _idDoOutro;
	private Vector2? _posDoOutro;

	public override void _Process(double delta)
	{
		if (_fechou || !_dentro) return;
		if (GameClient.Instance is not { Connected: true } cli) return;
		_relogio += delta;

		// ============================ O OUTRO SE ACHA PELO NOME, E NOS DOIS PAPEIS ============================
		// "O primeiro corpo do snapshot" nao serve: o berco tem NPC, e a primeira rodada da bancada do
		// desvio terminou com o socador nocauteado pelo Krillin (ver `World.IdPeloNome`). Aqui o nome
		// importa duas vezes -- o B bate no corpo certo, e o A so se cura depois que o CLIENTE que vai
		// medir chegou. Com um NPC valendo como "o outro", o A se curaria antes de o olhador existir e
		// a familia 4 mediria um corpo que nunca esteve amputado aos olhos de ninguem.
		if (_idDoOutro == 0 && Alvo.Length > 0 && World.Instancia is { } mundo)
		{
			int achei = mundo.IdPeloNome(Alvo);
			if (achei != 0 && achei != cli.LocalId)
			{
				_idDoOutro = achei;
				_viOOutroEm = _relogio;
				if (Papel == "b") cli.SendAlvo(_idDoOutro);
				Anotar($"achei o outro: '{Alvo}' = id {_idDoOutro} (t={_relogio:0.0}s)");
			}
		}

		if (Papel == "a") PassoDoFerido(cli, delta);
		else PassoDoOlhador(cli, delta);
	}

	// =====================================================================================
	// O PAPEL A -- O FERIDO. Mede a PROPRIA vida (familia 2) e serve de regua pro outro lado.
	// =====================================================================================
	private bool _promoveu, _curou, _julguei;
	private double _instanteDaCura;
	private double? _ultimoHp;
	private readonly HashSet<double> _valores = [];
	private readonly HashSet<double> _valoresDepois = [];
	private double _menorPasso = double.MaxValue, _menorHp = double.MaxValue, _maiorHp;
	private int _pacotesDeFicha, _pacotesDeCorpo;
	private bool _hudSeguiuSempre = true;
	private double _proximoDespejo;

	/// <summary>
	/// QUANTO ESPERAR, depois que o outro apareceu, ate se curar.
	///
	/// Nao e enfeite de roteiro: a cura e o instante que apaga a amputacao, e o olhador precisa ter
	/// tido tempo de MEDIR o corpo amputado antes. Curar cedo demais transformaria a familia 4 numa
	/// bancada que nunca viu o membro faltando -- e ela fecharia verde dizendo que ele voltou.
	/// </summary>
	private const double EsperaAntesDeCurar = 16.0;

	private void PassoDoFerido(GameClient cli, double delta)
	{
		// ============================ O ADMIN VAI PRO DISCO ANTES DE O OUTRO CHEGAR ============================
		// O servidor DESLIGA o admin-por-endereco assim que duas contas chegam de 127.0.0.1
		// (`GameServer.Admin.cs`), e com razao. Os dois processos desta bancada moram na mesma
		// maquina por construcao, entao o `admin_curar` do meio da rodada sairia e ninguem
		// responderia -- e o log leria como "a cura nao apaga a ferida" quando na verdade ela nunca
		// aconteceu. `admin_promover` grava a marca no arquivo da conta, e conta marcada nao depende
		// de endereco. Roda no primeiro segundo, enquanto o A ainda e o unico local.
		if (!_promoveu && _relogio >= 1 && Conta.Length > 0)
		{
			_promoveu = true;
			cli.SendVerbo("admin_promover", Conta);
		}

		// ---------- A PROPRIA VIDA, POR QUADRO ----------
		// Amostrada por QUADRO e nao no evento da ficha porque o que se quer medir e a FAIXA e o
		// PASSO: quantos valores distintos a vida deste corpo percorreu enquanto o outro lado via
		// um punhado de degraus. Contar pacotes daria a taxa do canal, que nao e a pergunta.
		double hp = cli.Sheet.HP;
		if (hp > 0)
		{
			if (_ultimoHp is { } antes && Math.Abs(hp - antes) > 1e-9)
			{
				_menorPasso = Math.Min(_menorPasso, Math.Abs(hp - antes));
			}
			_ultimoHp = hp;
			_valores.Add(hp);
			if (_curou) _valoresDepois.Add(hp);
			_menorHp = Math.Min(_menorHp, hp);
			_maiorHp = Math.Max(_maiorHp, hp);

			// A HUD E O CONSUMIDOR QUE TEM QUE SOBREVIVER. A barra de vida da HUD e A MINHA -- ela nao
			// foi tocada, e e justamente ela que faz "nao mandar vida nenhuma" ser um conserto errado.
			// Conferida por QUADRO e com `&&`: uma HUD que congelasse depois do terceiro pacote passaria
			// numa leitura pontual no fim.
			if (_relogio > 3)
			{
				if (Hud.Instancia is { } h)
					_hudSeguiuSempre &= Math.Abs(h.BarraDeVida.Valor - hp / 100.0) <= 0.02;
				else _hudSeguiuSempre = false;
			}
		}

		// ---------- A CURA ----------
		// ============================ NAO ESPERA A VIDA "ASSENTAR" ============================
		// A primeira versao disto so curava depois de 2 s SEM a vida mudar, pra garantir que a queda
		// da mascara acontecesse com o outro parado. A REGENERACAO PASSIVA nunca deixa: a ficha muda
		// em passos de centesimo o tempo todo, e a cura escorregou de t=27 pra t=53 -- o que sobrou de
		// rodada nao deu nem cinco valores de vida depois dela, e a bancada reprovou por causa do
		// proprio roteiro.
		//
		// A garantia que aquela condicao buscava ja existe, e MEDIDA em vez de suposta: o olhador so
		// comeca a bater depois de ver a mascara limpa, entao ele confere que tinha ZERO golpes dados
		// no instante da queda (ver `_golpesQuandoACuraVeio`). Prova melhor, e sem acoplar o roteiro a
		// um valor que o jogo mexe sozinho.
		// ======================================================================================
		if (!_curou && _viOOutroEm > 0 && _relogio >= _viOOutroEm + EsperaAntesDeCurar)
		{
			_curou = true;
			_instanteDaCura = _relogio;
			cli.SendVerbo("admin_curar");
			Anotar($"CUREI a mim mesmo em t={_relogio:0.0}s (vida {hp:0.0}) -- o membro tem que VOLTAR "
				 + "na tela do outro, e a mascara dele tem que ficar limpa");
		}

		_proximoDespejo -= delta;
		if (_proximoDespejo <= 0) { _proximoDespejo = 1.0; Despejar(cli); }

		if (_relogio >= Fim && !_julguei) { _julguei = true; JulgarAPropriaVida(cli); }

		// SAI DEPOIS DO OUTRO. O B le o meu arquivo no veredito dele; se eu morresse antes, ele
		// julgaria a familia 2 por um arquivo velho -- ou por nenhum.
		if (_relogio >= Fim + 25) Fechar();
	}

	/// <summary>
	/// ONDE O FERIDO POUSA O QUE MEDIU. Os dois processos moram na mesma maquina (e a bancada de
	/// dois corpos deste projeto ja depende disso), entao o `user://` e o mesmo.
	///
	/// E NAO HA OUTRO CAMINHO: a regua de "o outro nao sabe o meu numero" e o numero que EU sei, e
	/// ele esta deste lado do soquete. Sem o arquivo, o olhador so poderia afirmar "eu nao tenho um
	/// numero" -- que fica verde tambem num jogo em que ninguem tem vida nenhuma.
	/// </summary>
	private const string ArquivoDoFerido = "user://sigilo-do-ferido.txt";

	private void Despejar(GameClient cli)
	{
		int decepados = cli.Corpo.Count(p => p.Decepado);
		var sb = new StringBuilder();
		sb.AppendLine($"quando={DateTime.Now.Ticks}");
		sb.AppendLine($"nome={cli.LocalName}");
		sb.AppendLine($"id={cli.LocalId}");
		sb.AppendLine($"t={_relogio.ToString("0.0", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"hp={cli.Sheet.HP.ToString("0.###", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"curou={(_curou ? 1 : 0)}");
		sb.AppendLine($"t_da_cura={_instanteDaCura.ToString("0.0", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"distintos={_valores.Count}");
		sb.AppendLine($"distintos_depois={_valoresDepois.Count}");
		sb.AppendLine($"menor_passo={(_menorPasso == double.MaxValue ? 0 : _menorPasso).ToString("0.####", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"menor_hp={(_menorHp == double.MaxValue ? 0 : _menorHp).ToString("0.###", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"maior_hp={_maiorHp.ToString("0.###", CultureInfo.InvariantCulture)}");
		sb.AppendLine($"membros={cli.Corpo.Count}");
		sb.AppendLine($"decepados={decepados}");
		sb.AppendLine($"pacotes_ficha={_pacotesDeFicha}");
		sb.AppendLine($"ok={_oks}");
		sb.AppendLine($"falhas={_falhas.Count}");
		using Godot.FileAccess? f = Godot.FileAccess.Open(ArquivoDoFerido, Godot.FileAccess.ModeFlags.Write);
		f?.StoreString(sb.ToString());
	}

	/// <summary>
	/// ============================ FAMILIA 2 -- A MINHA VIDA CHEGA (O CONTRA-EXEMPLO) ============================
	/// Sem esta familia a bancada inteira teria um buraco do tamanho do conserto: um servidor que
	/// parasse de mandar vida pra QUALQUER UM passaria em todas as outras linhas com louvor. O corte
	/// e no que viaja sobre os OUTROS -- a minha ficha nao mudou, e a HUD depende dela.
	///
	/// E ELA MEDE A GRANULARIDADE, e nao so a chegada: a vida do dono e um NUMERO continuo (um passo
	/// de decimo de ponto e normal), e e esse contraste que da sentido ao que o outro lado recebe. Um
	/// dia em que a ficha do dono passasse a vir arredondada em 16 degraus, esta linha reprova -- e
	/// tem que reprovar, porque ai o dono teria perdido o proprio numero sem ninguem pedir.
	/// ============================================================================================================
	/// </summary>
	private void JulgarAPropriaVida(GameClient cli)
	{
		Conferir(_pacotesDeFicha > 0 && cli.Sheet.HP > 0,
				 $"a MINHA vida CHEGA: {_pacotesDeFicha} pacote(s) de ficha, HP agora {cli.Sheet.HP:0.0}");

		Conferir(_valores.Count >= 10,
				 $"e ela chega como NUMERO: a minha vida passou por {_valores.Count} valores distintos "
			   + $"({_menorHp:0.0} a {_maiorHp:0.0}) -- o outro lado nao tem nada disso");

		Conferir(_menorPasso < 1.0,
				 $"e com passo FINO: a menor variacao que eu senti foi {(_menorPasso == double.MaxValue ? 0 : _menorPasso):0.###} "
			   + "ponto(s) de vida -- um degrau de ferida nao chega perto disso");

		Conferir(_valoresDepois.Count >= 5,
				 $"e ela continuou mudando DEPOIS da cura ({_valoresDepois.Count} valores distintos) -- "
			   + "sem isto, a briga que o olhador mede do outro lado nao teria acontecido");

		Conferir(_hudSeguiuSempre,
				 "a BARRA DE VIDA DA HUD (que e a minha, e ficou) seguiu a ficha em TODO quadro da rodada");

		Conferir(_pacotesDeCorpo > 0 && cli.Corpo.Count >= 10,
				 $"o corpo POR MEMBRO tambem chega, e so pra mim ({cli.Corpo.Count} membros em "
			   + $"{_pacotesDeCorpo} pacote(s)) -- e a informacao de ficha que o outro nao tem");

		Anotar($"FERIDO: vida {_menorHp:0.0}..{_maiorHp:0.0} | {_valores.Count} valores distintos "
			 + $"({_valoresDepois.Count} depois da cura) | menor passo {(_menorPasso == double.MaxValue ? 0 : _menorPasso):0.###}");

		Despejar(cli);
		GD.Print($"\n===== [vida-a] {_oks} OK, {_falhas.Count} FALHA =====");
		foreach (string f in _falhas) GD.PrintErr("  FALHA  " + f);
	}

	// =====================================================================================
	// O PAPEL B -- O OLHADOR
	// =====================================================================================
	/// <summary>
	/// A que distancia o olhador para de andar. O soco alcanca 40 px; parar em 30 deixa margem pro
	/// atraso da interpolacao sem empilhar os dois corpos (corpos empilhados nao se leem).
	/// </summary>
	private const float Perto = 30f;

	/// <summary>
	/// A zona mirada na fase de pancada: CABECA. Indice em <see cref="Protocol.Zonas"/>.
	///
	/// ============================ POR QUE A CABECA, E NAO AS PERNAS ============================
	/// A escolha e sobre CONCENTRACAO, e ela foi medida: mirando nas pernas, o dano se reparte entre
	/// quatro membros (duas pernas e dois pes) e em 30 s de pancada o grau daquele corpo so chegou ao
	/// HEMATOMA -- a rodada fechava sem uma gota de sangue, e a familia do respingo reprovava por
	/// falta de estrago e nao por defeito. A zona `cabeca` tem UM membro sorteavel (o cerebro e
	/// aninhado), entao a mesma quantidade de socos empurra um membro so pela curva inteira: roxo,
	/// folga, sangue.
	///
	/// O PRECO E O NOCAUTE, e ele nao atrapalha: a cabeca e nucleo, entao quebra-la derruba o sujeito
	/// -- mas golpe NAO-LETAL nunca passa dos 19,8% de vida, ninguem morre, e corpo caido continua
	/// sendo corpo que se ve na tela. Nocaute nao apaga ferida.
	/// ==========================================================================================
	/// </summary>
	private const byte ZonaMirada = 1;

	private enum Fase { Espiando, Batendo, Fechando }
	private Fase _fase = Fase.Espiando;

	/// <summary>
	/// Quanto tempo entre um soco e outro. E MAIOR que a pose do golpe (`AttackPoseMs`, 240 ms) de
	/// proposito: no ritmo da pose o robo esvazia o vigor em vinte segundos e passa o resto da rodada
	/// mandando pacote que o servidor recusa -- ver a guarda do folego em <see cref="AndarEBater"/>.
	/// </summary>
	private const double Cadencia = 0.6;

	private double _proximoSoco;
	private int _golpes, _acertos;
	private double _comecouABater;
	private RemotePlayer? _corpoAlheio;
	private double _proximaBusca;

	private void PassoDoOlhador(GameClient cli, double delta)
	{
		ColherOCorpoAlheio(cli);

		switch (_fase)
		{
			case Fase.Espiando:
				// A CURA E O SINAL, e nao um relogio: quando a mascara do outro fica LIMPA depois de ter
				// tido membro arrancado, o dono se curou. Amarrar isto a um segundo fixo acoplaria os
				// dois processos por um relogio que eles nao compartilham (o B entra depois do A).
				if (_viAmputacaoNaMascara && _mascaraDoOutro.Limpa && _relogio > 4)
				{
					_curaVista = _relogio;
					_golpesQuandoACuraVeio = _golpes;
					ComecarAPancada(cli, $"a mascara do outro FICOU LIMPA em t={_relogio:0.0}s (ele se curou, "
										+ "e eu nao encostei nele)");
				}
				// ============================ UMA FAMILIA QUEBRADA NAO PODE APAGAR AS OUTRAS ============================
				// O roteiro inteiro pendura na cura, e a cura so e reconhecida porque a AMPUTACAO chegou
				// antes. Medido injetando o defeito da familia 4 (`PutFeridas` mandando zero no byte dos
				// membros): sem amputacao a fase nunca virava, a bancada nao dava um soco, e as familias 3
				// e 6 fechavam vermelhas dizendo "o grau nao mudou" -- diagnostico errado pro defeito certo.
				//
				// Entao, passado o prazo, a pancada comeca do mesmo jeito e o relatorio diz que a cura
				// nunca foi vista. As linhas da cura continuam reprovando (elas e que sao sobre ela); as
				// outras voltam a ter veredito proprio.
				// ==================================================================================================
				else if (_relogio > EsperaAntesDeCurar + 14)
					ComecarAPancada(cli, "a cura NUNCA foi vista (a mascara do outro nunca ficou limpa depois "
									   + "de ter membro arrancado) -- bato assim mesmo, pra as outras familias "
									   + "nao ficarem sem veredito por causa desta");
				break;

			case Fase.Batendo:
				AndarEBater(cli, delta);
				// FECHA POR MEDIDA, e nao por relogio: quando ja se viu escada de sobra, insistir so
				// aproxima o corpo do piso e nao acrescenta nivel nenhum.
				if (_degrausVistos.Count >= 8 || _relogio - _comecouABater > Fim * 0.55) _fase = Fase.Fechando;
				break;

			case Fase.Fechando:
				Parar();
				break;
		}

		if (_relogio >= Fim + 6) { Julgar(cli); Fechar(); }
	}

	/// <summary>
	/// Abre a fase de pancada e liga a MEDICAO. Os dois caminhos que chegam aqui estao no
	/// <see cref="PassoDoOlhador"/>; o que muda entre eles e so o que se pode afirmar depois.
	/// </summary>
	private void ComecarAPancada(GameClient cli, string porque)
	{
		_fase = Fase.Batendo;
		_medindo = true;
		_comecouABater = _relogio;
		// LUTA NAO-LETAL de proposito: o piso do golpe nao-letal (19,8% de vida) e o que impede a
		// rodada de virar uma morte no meio da medicao -- e a escada de graus ate esse piso ja e bem
		// mais longa do que os tres niveis que o dono pediu.
		cli.SendLethal(false);
		cli.SendAim(ZonaMirada);
		Anotar(porque + " -- comeco a bater pra ver o grau subir de novo");
	}

	/// <summary>A janela de medicao esta aberta. Ver <see cref="ComecarAPancada"/>.</summary>
	private bool _medindo;

	private void AndarEBater(GameClient cli, double delta)
	{
		if (World.Instancia?.PosicaoLocal is not { } eu || _posDoOutro is not { } dele) { Parar(); return; }

		// ANDA COM AS TECLAS DE VERDADE (`Input.ActionPress`), como o `RoboDeSoco`: assim o passo
		// passa pelo mesmo caminho do jogador (movimento local, bit de corrida, concessao do
		// servidor) em vez de teleportar o corpo pra dentro do alcance.
		Vector2 d = dele - eu;
		if (d.Length() > Perto)
		{
			Segurar("move_right", d.X > 8f);
			Segurar("move_left", d.X < -8f);
			Segurar("move_down", d.Y > 8f);
			Segurar("move_up", d.Y < -8f);
		}
		else Parar();

		// A MIRA E A LETALIDADE SAO REAFIRMADAS de tempos em tempos: os dois sao estado do servidor, e
		// um pacote perdido no comeco deixaria a rodada inteira batendo na zona errada -- o que nao
		// da erro nenhum, so um grau que sobe devagar no lugar que a bancada nao esta olhando.
		_reafirmar -= delta;
		if (_reafirmar <= 0) { _reafirmar = 3; cli.SendLethal(false); cli.SendAim(ZonaMirada); }

		_proximoSoco -= delta;
		if (_proximoSoco > 0) return;
		_proximoSoco = Cadencia;

		// ============================ SOCAR SEM FOLEGO NAO E SOCAR ============================
		// Medido numa rodada que reprovou com o jogo certo: 67 golpes saidos, 13 acertos, e o corpo do
		// outro parou de piorar na METADE da janela -- eu continuava mandando pacote de golpe e o
		// servidor continuava recusando, porque o VIGOR tinha acabado (o log do outro lado fecha a
		// conta: "Olhador levantou"). A bancada nao estava medindo o canal de ferida, estava medindo o
		// cansaco do robo.
		//
		// Entao o robo respeita o folego como um jogador respeitaria: abaixo de um quarto do tanque ele
		// espera. Sai menos soco e chega MAIS dano -- que e o que a escada de graus precisa.
		// ==================================================================================
		if (cli.Sheet.VigorMax > 0 && cli.Sheet.Vigor < cli.Sheet.VigorMax * 0.25) return;

		_golpes++;
		cli.SendAction();
	}

	private double _reafirmar;

	private static void Segurar(string acao, bool sim)
	{
		if (sim) Godot.Input.ActionPress(acao);
		else Godot.Input.ActionRelease(acao);
	}

	private static void Parar()
	{
		foreach (string a in new[] { "move_right", "move_left", "move_up", "move_down" })
			Godot.Input.ActionRelease(a);
	}

	/// <summary>
	/// ============================ O UNICO NUMERO QUE O ATACANTE TEM, E DE ONDE ELE VEM ============================
	/// Quem bate recebe o `S2C.Hit` COM o dano -- e tem que receber: e o dano que EU causei, no
	/// membro que EU acertei, e sem ele o combate fica mudo pra quem esta lutando. Quem so assiste ja
	/// recebe a versao magra (`TemDano = false`), que e o precedente do sigilo dentro do proprio
	/// combate.
	///
	/// Isto NAO e a vida do outro, e a familia 1 confere justamente isso no formato do pacote: dano
	/// causado nao e vida restante. Anotado aqui, e nao escondido, porque uma bancada que se calasse
	/// sobre o proprio alcance viraria, no relatorio de alguem, uma prova que ela nunca deu -- um
	/// atacante teimoso pode somar o que causou, e essa e uma inferencia dele, nao um campo do fio.
	/// ==========================================================================================================
	/// </summary>
	private void AoGolpe(Protocol.HitEvent h)
	{
		if (GameClient.Instance is not { } cli) return;
		if (h.Atacante == cli.LocalId && h.TemDano) { _acertos++; _danoQueEuCausei += h.Dano; }
		if (h.Alvo == cli.LocalId && h.TemDano) _danoQueEuLevei += h.Dano;
	}

	private double _danoQueEuCausei, _danoQueEuLevei;

	// ---------------------------------------------------------------------
	// A COLHEITA DO CORPO ALHEIO
	// ---------------------------------------------------------------------
	private MascaraDeFeridas _mascaraDoOutro;
	private bool _viAmputacaoNaMascara, _viMascaraDoOutro;
	private double _curaVista;

	/// <summary>As mascaras distintas que eu vi daquele corpo, na ordem. E o "grau" do titulo.</summary>
	private readonly List<MascaraDeFeridas> _mascarasVistas = [];

	/// <summary>Os graus TOTAIS distintos vistos DEPOIS da cura -- os "niveis de dano" do pedido.</summary>
	private readonly List<int> _degrausVistos = [];

	/// <summary>O grau da zona MIRADA, na ordem em que apareceu. So pro relatorio dizer onde doeu.</summary>
	private readonly List<int> _degrausDaZona = [];

	/// <summary>Quantas vezes o grau daquele corpo subiu e desceu depois da cura. Ver <see cref="OGrauViajaEMuda"/>.</summary>
	private int _subidas, _descidas;

	private bool _desenhoBateuSempre = true;
	private int _quadrosDeDesenhoConferidos;
	private bool _ampNoMaterialAcendeu, _ampNoLadoCerto = true, _ampZerouDepoisDaCura;
	private int _quadrosDeAmputacaoConferidos;
	private int _golpesQuandoACuraVeio = -1;
	private bool _cruzouOLimiar;

	/// <summary>Quantos respingos cairam AO LADO do corpo alheio, e o menor sangue que ele tinha num deles.</summary>
	private int _respingosDoOutro, _ultimoContadorDeSangue, _menorSangueNumRespingo = int.MaxValue;

	/// <summary>Dois tiles: o efeito planta num vizinho sorteado da celula do corpo (ver `World.Decalques`).</summary>
	private const float RaioDoRespingo = 2 * Jandirus.Core.World.ZoneCollision.TileSize;

	/// <summary>
	/// O GRAU TOTAL de um corpo: a soma das duas camadas das cinco zonas. Sobe com o dano e so com
	/// ele, entao "quantos valores distintos DESTE numero eu vi" e literalmente "por quantos niveis
	/// de dano aquele corpo passou aos meus olhos". Uma zona so nao serviria: a mira pesa o sorteio,
	/// nao o crava, e um teste que exigisse tudo na perna reprovaria por causa do sorteio do servidor.
	/// </summary>
	private static int GrauTotal(MascaraDeFeridas m)
	{
		int s = 0;
		for (int z = 0; z < MascaraDeFeridas.Zonas; z++) s += (m.Bruto(z) >> 4) + (m.Bruto(z) & 0x0F);
		return s;
	}

	private static int GrauDaZona(MascaraDeFeridas m, int z) => (m.Bruto(z) >> 4) + (m.Bruto(z) & 0x0F);

	/// <summary>O pior SANGUE do corpo -- a mesma leitura que o respingo usa (ver `World.Decalques`).</summary>
	private static int SangueDe(MascaraDeFeridas m)
	{
		if (m.Amputados != MascaraDeFeridas.Membro.Nenhum) return MascaraDeFeridas.Degraus;
		int pior = 0;
		for (int z = 0; z < MascaraDeFeridas.Zonas; z++) pior = Math.Max(pior, m.Bruto(z) & 0x0F);
		return pior;
	}

	private void ColherOCorpoAlheio(GameClient cli)
	{
		if (_idDoOutro == 0) return;

		// ---------- A MASCARA (o que o fio traz) ----------
		if (cli.Feridas.TryGetValue(_idDoOutro, out MascaraDeFeridas m))
		{
			if (!_viMascaraDoOutro || m != _mascaraDoOutro)
			{
				_mascaraDoOutro = m;
				_viMascaraDoOutro = true;
				_mascarasVistas.Add(m);
				Anotar($"t={_relogio:0.0}s  mascara do outro: {m}  (grau total {GrauTotal(m)})");

				if (m.Amputados != MascaraDeFeridas.Membro.Nenhum) _viAmputacaoNaMascara = true;

				// ============================ MEDE DEPOIS DA CURA, E NAO "ENQUANTO EU BATO" ============================
				// Isto era `_fase == Fase.Batendo`, e a bancada ficava cega na melhor parte: a fase fechava
				// assim que aparecia escada de sobra, o mundo continuava acontecendo, e o corpo do outro foi
				// de grau 44 a 104 (com sangue de sobra) DEPOIS que eu parei -- a linha do respingo reprovou
				// dizendo que o sangue nunca cruzou o limiar, que era mentira.
				//
				// A janela certa e "da cura em diante": a cura e o corte que separa o corpo limpo do corpo
				// que volta a se estragar, e quem estraga nao precisa ser eu. O berco e povoado -- nesta
				// rodada dois NPCs nocautearam os dois lados --, e isso nao e sujeira: a afirmacao e que o
				// SINAL responde ao dano, venha ele de quem vier. O que a bancada nao pode e depender de eu
				// ser a fonte (e nao depende: a queda da cura ja e medida com zero golpes meus).
				// ==================================================================================================
				if (_medindo)
				{
					int total = GrauTotal(m);
					if (_degrausVistos.Count > 0)
					{
						if (total > _degrausVistos[^1]) _subidas++;
						else if (total < _degrausVistos[^1]) _descidas++;
					}
					if (_degrausVistos.Count == 0 || _degrausVistos[^1] != total) _degrausVistos.Add(total);
					int zona = GrauDaZona(m, ZonaMirada - 1);
					if (_degrausDaZona.Count == 0 || _degrausDaZona[^1] != zona) _degrausDaZona.Add(zona);

					if (SangueDe(m) >= LimiarDoRespingo()) _cruzouOLimiar = true;
				}
			}
		}

		// ============================ O RESPINGO PRECISA SER ATRIBUIDO A UM CORPO ============================
		// Duas versoes desta medida ja reprovaram com o jogo CERTO, e as duas erraram pela mesma razao:
		// contavam pingos sem perguntar de QUEM eram.
		//
		//   1a: "quantos respingos havia quando eu VI o limiar ser cruzado" -- o plantio e a mudanca de
		//       mascara caem no MESMO quadro (o `World.TickDosDecalques` roda antes do meu `_Process`),
		//       entao a conta ja incluia o respingo daquela travessia;
		//   2a: "quantos havia enquanto o corpo dele estava limpo" -- o berco e POVOADO, os NPCs brigam
		//       entre si, e o pingo de uma briga do outro lado do mapa entrava na conta.
		//
		// A pergunta certa e sobre O CORPO DELE: **todo respingo que caiu AO LADO DELE caiu com o sangue
		// DELE acima do limiar?**. A posicao do ultimo pingo (`Decalques.OndeSangrouDeTeste`) e o que
		// permite atribuir -- o efeito planta num vizinho da celula do corpo, entao dois tiles de raio
		// cobrem o sorteio e nao alcancam quem esta longe.
		//
		// O MEU PROPRIO CORPO ESTA A ESSA DISTANCIA (eu estou socando ele), e por construcao ele nao
		// suja a medida: com um membro arrancado, o `SangueDaMascara` do meu corpo fica cravado no
		// MAXIMO (ver `World.Decalques`), nunca PIORA, e portanto nunca dispara respingo nenhum.
		//
		// E ESTE BLOCO VEM DEPOIS DA LEITURA DA MASCARA, no mesmo metodo -- a 3a reprovacao com o jogo
		// certo foi isto: o pingo do quadro da travessia era comparado com a mascara do quadro
		// ANTERIOR, e a bancada acusava respingo de "corpo arranhado" (sangue 1) no exato quadro em
		// que ele tinha acabado de passar pra 11.
		// ====================================================================================================
		if (_medindo && _viMascaraDoOutro && Decalques.SangueDeTeste != _ultimoContadorDeSangue)
		{
			_ultimoContadorDeSangue = Decalques.SangueDeTeste;
			if (CorpoAlheio() is { } dele && GodotObject.IsInstanceValid(dele)
				&& Decalques.OndeSangrouDeTeste.DistanceTo(dele.GlobalPosition) <= RaioDoRespingo)
			{
				_respingosDoOutro++;
				_menorSangueNumRespingo = Math.Min(_menorSangueNumRespingo, SangueDe(_mascaraDoOutro));
			}
		}

		// ---------- O DESENHO (o que o shader recebeu) ----------
		if (CorpoAlheio() is not { } corpo) return;
		if (corpo.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis) return;
		if (vis.FeridaNoMaterialDeTeste() is not { } f) return;
		if (!_viMascaraDoOutro || f.Hema.Length < MascaraDeFeridas.Zonas) return;

		// A MASCARA CHEGAR NO DICIONARIO NAO E CHEGAR NO PIXEL. Conferido por quadro e com `&&`,
		// como todo o resto: o `Ferir` so reescreve os uniformes quando a mascara MUDA, entao um
		// caminho que parasse de chamar deixaria o corpo alheio congelado no desenho antigo -- e
		// isso nao da erro, nao da log e nao muda o dicionario.
		bool bate = true;
		for (int z = 0; z < MascaraDeFeridas.Zonas; z++)
		{
			bate &= Math.Abs(f.Hema[z] - _mascaraDoOutro.Hematoma((ZonaDeFerida)z)) <= 0.01f;
			bate &= Math.Abs(f.Sang[z] - _mascaraDoOutro.Sangue((ZonaDeFerida)z)) <= 0.01f;
		}
		_desenhoBateuSempre &= bate;
		_quadrosDeDesenhoConferidos++;
		if (!bate && _quadrosDeDesenhoConferidos % 60 == 1)
			Anotar($"o desenho do corpo alheio NAO bate com a mascara: hema [{string.Join(",", f.Hema.Select(v => v.ToString("0.00", CultureInfo.InvariantCulture)))}]"
				 + $" vs {_mascaraDoOutro}");

		// ---------- A AMPUTACAO NO DESENHO ----------
		ConferirAmputacao(corpo, f);
	}

	/// <summary>
	/// ============================ FAMILIA 4, MEDIDA NO MATERIAL E NO LADO CERTO ============================
	/// A mascara diz o lado do CORPO; o material guarda o lado da IMAGEM (`CharacterVisual.AplicarAmputacao`
	/// espelha quando o boneco esta de frente). Conferir so "acendeu alguma coisa" deixaria passar
	/// exatamente o defeito que aquela traducao existe pra evitar -- arrancar o braco esquerdo e apagar
	/// o direito na tela metade das vezes.
	///
	/// Por isso a expectativa e recalculada AQUI, com o `OlharDeTeste` lido no MESMO quadro do uniform:
	/// o corpo alheio vira sozinho, e comparar com uma direcao de dois quadros atras reprovaria a
	/// bancada por uma virada de sprite.
	/// ======================================================================================================
	/// </summary>
	private void ConferirAmputacao(RemotePlayer corpo, (float[] Hema, float[] Sang, Vector2 AmpBraco, Vector2 AmpPerna) f)
	{
		bool espelha = corpo.OlharDeTeste is Jandirus.Core.World.Facing.South;
		Vector2 Lados(bool esq, bool dir) => espelha
			? new Vector2(dir ? 1 : 0, esq ? 1 : 0)
			: new Vector2(esq ? 1 : 0, dir ? 1 : 0);

		Vector2 braco = Lados(_mascaraDoOutro.Perdeu(MascaraDeFeridas.Membro.BracoEsq),
							  _mascaraDoOutro.Perdeu(MascaraDeFeridas.Membro.BracoDir));
		Vector2 perna = Lados(_mascaraDoOutro.Perdeu(MascaraDeFeridas.Membro.PernaEsq),
							  _mascaraDoOutro.Perdeu(MascaraDeFeridas.Membro.PernaDir));

		if (_mascaraDoOutro.Amputados != MascaraDeFeridas.Membro.Nenhum)
		{
			_quadrosDeAmputacaoConferidos++;
			if (f.AmpBraco.Length() > 0 || f.AmpPerna.Length() > 0) _ampNoMaterialAcendeu = true;
			_ampNoLadoCerto &= f.AmpBraco.IsEqualApprox(braco) && f.AmpPerna.IsEqualApprox(perna);
		}
		// O CONTROLE: depois da cura o membro VOLTA, e o uniform tem que apagar. Sem esta metade,
		// "acendeu" ficaria verde num shader que apagasse o braco de todo mundo pra sempre.
		else if (_curaVista > 0 && _relogio > _curaVista + 1
				 && f.AmpBraco.Length() <= 0.001f && f.AmpPerna.Length() <= 0.001f)
			_ampZerouDepoisDaCura = true;
	}

	private RemotePlayer? CorpoAlheio()
	{
		if (_corpoAlheio != null && GodotObject.IsInstanceValid(_corpoAlheio)) return _corpoAlheio;
		if (_relogio < _proximaBusca || _idDoOutro == 0) return null;
		_proximaBusca = _relogio + 1;
		// PELA MESMA BUSCA QUE O JOGO USA (`World.CorpoDeTeste` -> o mapa `_remotos`), e nao por
		// `FindChild`: achar o node por nome passaria mesmo se o corpo nunca tivesse entrado no mapa
		// de onde a fala, o alvo e a forma tiram o corpo de alguem -- e desceria a arvore inteira da
		// zona por quadro, que ja derrubou uma bancada deste projeto pra menos de um quadro por segundo.
		_corpoAlheio = World.Instancia?.CorpoDeTeste(_idDoOutro) as RemotePlayer;
		return _corpoAlheio;
	}

	// =====================================================================
	// O VEREDITO DO OLHADOR
	// =====================================================================
	private void Julgar(GameClient cli)
	{
		Dictionary<string, string> ferido = LerOFerido();

		AVidaAlheiaNaoEUmNumero(cli, ferido);
		OGrauViajaEMuda(ferido);
		OMembroArrancadoSome();
		NaoSobrouDesenhoSobreCabecaNenhuma(cli);
		ORespingoVemDoGrau();
		OContraExemploDoOutroLado(ferido);

		GD.Print($"\n===== [vida-b] {_oks} OK, {_falhas.Count} FALHA =====");
		foreach (string f in _falhas) GD.PrintErr("  FALHA  " + f);
	}

	/// <summary>
	/// O QUE O FERIDO MEDIU. Vazio = ele nao escreveu (ou nao subiu), e isso reprova nas linhas que
	/// dependem dele em vez de sumir: uma bancada de dois processos que perde um lado tem que ficar
	/// vermelha, senao "0 falhas" se le exatamente igual a "tudo certo".
	/// </summary>
	private static Dictionary<string, string> LerOFerido()
	{
		var d = new Dictionary<string, string>();
		using Godot.FileAccess? f = Godot.FileAccess.Open(ArquivoDoFerido, Godot.FileAccess.ModeFlags.Read);
		if (f == null) return d;
		foreach (string linha in f.GetAsText().Split('\n'))
		{
			int i = linha.IndexOf('=');
			if (i > 0) d[linha[..i].Trim()] = linha[(i + 1)..].Trim();
		}
		return d;
	}

	private static double Num(Dictionary<string, string> d, string chave) =>
		d.TryGetValue(chave, out string? v)
		&& double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : -1;

	/// <summary>
	/// ============================ FAMILIA 1 -- A VIDA ALHEIA NAO CHEGA COMO NUMERO ============================
	/// A linha que separa "apaguei o desenho" de "tirei a informacao do jogo". Ela e cobrada de tres
	/// jeitos porque um so nao fecha o assunto:
	///
	///   * NO FORMATO (reflexao): o pacote que carregava o byte nao pode declarar campo de vida de
	///     novo. E a unica checagem que pega uma regressao ANTES de ela ser usada -- alguem pode
	///     recolocar o campo hoje e so escrever a barrinha semana que vem.
	///   * NO CORPO REMOTO: o node que representa o outro na minha tela nao guarda vida nenhuma.
	///   * NA MEDIDA (empirica): a mesma briga, os dois lados. O dono passou por N valores distintos
	///     de vida; eu, olhando o corpo dele o tempo todo, vi M mascaras. Se M chegasse perto de N, o
	///     que viaja seria numero com outro nome -- e nenhuma reflexao pegaria isso.
	///
	/// O QUE ELA **NAO** PROVA, e esta anotado no lugar de ser escondido: eu sou o ATACANTE, e
	/// atacante recebe o dano que ELE causou (ver <see cref="AoGolpe"/>). Somar os proprios golpes e
	/// uma inferencia de quem bateu, nao um campo do fio -- e o pacote de golpe e conferido aqui pra
	/// garantir que ele nao passou a carregar vida restante junto.
	/// ==========================================================================================================
	/// </summary>
	private void AVidaAlheiaNaoEUmNumero(GameClient cli, Dictionary<string, string> ferido)
	{
		List<string> noPacote = CamposDeVida(typeof(EntityState));
		Conferir(noPacote.Count == 0,
				 "o pacote de snapshot (`EntityState`) nao declara NENHUM campo de vida"
			   + (noPacote.Count > 0 ? " -- achei: " + string.Join(", ", noPacote) : ""));

		List<string> noCorpo = CamposDeVida(typeof(RemotePlayer));
		Conferir(noCorpo.Count == 0,
				 "o CORPO ALHEIO na minha tela (`RemotePlayer`) nao guarda vida nenhuma"
			   + (noCorpo.Count > 0 ? " -- achei: " + string.Join(", ", noCorpo) : ""));

		List<string> noGolpe = CamposDeVida(typeof(Protocol.HitEvent));
		Conferir(noGolpe.Count == 0,
				 "o pacote de GOLPE carrega o dano que EU causei, e nao a vida que sobrou nele"
			   + (noGolpe.Count > 0 ? " -- achei: " + string.Join(", ", noGolpe) : ""));

		// ---------- A MEDIDA EMPIRICA ----------
		int meus = _mascarasVistas.Count;
		double dele = Num(ferido, "distintos");
		Conferir(dele >= 10,
				 $"o dono do corpo mediu a propria vida de verdade ({dele:0} valores distintos) -- sem "
			   + "essa regua, 'eu nao vi numero nenhum' ficaria verde num jogo sem vida nenhuma");
		Conferir(dele > meus * 2,
				 $"**a mesma briga, os dois lados**: o DONO passou por {dele:0} valores distintos de vida "
			   + $"e eu vi {meus} mascara(s) daquele corpo -- o que viaja e GRAU, e nao numero");

		// O CANAL POR MEMBRO E PESSOAL, e da pra provar sem reflexao nenhuma: depois da cura o dono
		// registrou o corpo INTEIRO, e o meu `Corpo` continua com o membro que o `--feridateste` me
		// arrancou. Se aquele pacote fosse de zona, os dois teriam a mesma lista.
		int meusDecepados = cli.Corpo.Count(p => p.Decepado);
		double deleDecepados = Num(ferido, "decepados");
		Conferir(meusDecepados > 0 && deleDecepados == 0,
				 $"o pacote por MEMBRO e pessoal: eu ainda tenho {meusDecepados} membro(s) decepado(s) e o "
			   + $"dono registrou {deleDecepados:0} depois da cura -- duas listas, dois corpos");

		Anotar($"o unico numero que EU tenho e o meu: causei {_danoQueEuCausei:0.0} de dano em {_acertos} "
			 + $"acerto(s) de {_golpes} golpe(s), e levei {_danoQueEuLevei:0.0}");
	}

	/// <summary>
	/// Todo campo/propriedade de um tipo cujo NOME fala de vida. Inclui os privados de proposito: o
	/// campo que morreu neste conserto (`VidaDeTeste`, e o `byte Vida` do pacote) voltaria com o
	/// mesmo nome, e um `_vida` privado alimentando um desenho e exatamente o jeito silencioso de
	/// isto voltar.
	/// </summary>
	private static List<string> CamposDeVida(Type t)
	{
		var achados = new List<string>();
		const BindingFlags onde = BindingFlags.Public | BindingFlags.NonPublic
								| BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
		foreach (MemberInfo mi in t.GetFields(onde).Cast<MemberInfo>().Concat(t.GetProperties(onde)))
			if (NomeDeVida.IsMatch(mi.Name)) achados.Add($"{t.Name}.{mi.Name}");
		return achados;
	}

	/// <summary>
	/// O que conta como "nome de vida". `hp` so casa como palavra inteira: um `_hpx` qualquer nao e
	/// vida, e uma regra que casasse em qualquer lugar acusaria `Sharpness` e ensinaria a ignorar
	/// vermelho.
	/// </summary>
	private static readonly Regex NomeDeVida =
		new(@"vida|saude|health|life|(^|_|\b)hp(\b|_|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	/// <summary>
	/// ============================ FAMILIA 3 -- O GRAU VIAJA E MUDA COM O DANO ============================
	/// E a metade que o dono pediu pra MANTER, e a que mais se perde por descuido: um canal de ferida
	/// congelado nao da erro, nao aparece em log nenhum e deixa o corpo com a cara do primeiro
	/// pacote pra sempre. Quem olhasse a tela leria "aquele cara nao esta apanhando".
	///
	/// TRES NIVEIS ERA O PISO DO PEDIDO -- aqui a escada e medida inteira, e a bancada exige mais que
	/// tres porque o dano de um soco atravessa mais de um degrau quando o BP e alto: exigir
	/// exatamente tres deixaria passar um canal que so manda o comeco e o fim.
	///
	/// E ELA COBRA A QUEDA TAMBEM. Um canal que so sobe descreve um corpo que nunca se recupera, e a
	/// queda e a unica prova de que quem conta a historia daquele corpo e o SERVIDOR: ela acontece
	/// enquanto eu estou parado, sem ter causado nada.
	/// ====================================================================================================
	/// </summary>
	private void OGrauViajaEMuda(Dictionary<string, string> ferido)
	{
		Conferir(_viMascaraDoOutro,
				 $"a mascara de feridas do corpo alheio CHEGA (vi {_mascarasVistas.Count} valor(es) dela)");

		Conferir(_degrausVistos.Count >= 4,
				 $"e ela MUDA COM O DANO: depois da cura, o corpo dele passou por {_degrausVistos.Count} "
			   + $"niveis distintos aos meus olhos [{string.Join(" -> ", _degrausVistos)}] "
			   + "(o pedido eram tres)");

		// ============================ SUBIDAS, E NAO MONOTONIA ============================
		// A primeira versao desta linha exigia que o grau so SUBISSE enquanto o corpo apanha, e ela
		// reprovou numa rodada em que o jogo estava certo: a REGENERACAO PASSIVA fecha ferida entre um
		// soco e outro, entao a serie sobe e desce de verdade. Exigir monotonia seria a bancada
		// cobrando do jogo uma regra que o jogo nao tem -- e o jeito mais rapido de ensinar a ignorar
		// vermelho.
		//
		// O que o dono pediu ("tres niveis de dano") e sobre o SINAL responder ao dano, e e isso que se
		// conta: quantas vezes o grau daquele corpo SUBIU. As descidas entram no relatorio como o que
		// sao -- o corpo se recuperando --, e nao como falha.
		// ================================================================================
		Conferir(_subidas >= 3,
				 $"e ela SOBE com o dano {_subidas} vez(es) (e desce {_descidas}, que e a regeneracao "
			   + $"passiva fechando ferida entre um soco e outro) -- zona mirada: [{string.Join(" -> ", _degrausDaZona)}]");

		Conferir(_quadrosDeDesenhoConferidos > 60 && _desenhoBateuSempre,
				 $"e o grau chega ao DESENHO, e nao so ao dicionario: os uniformes do corpo alheio "
			   + $"bateram com a mascara em TODOS os {_quadrosDeDesenhoConferidos} quadros conferidos");

		// E A QUEDA E O QUE PROVA DE QUEM E A HISTORIA. Eu sou o atacante, e atacante pode somar o
		// dano que causou -- entao "eu vi o corpo piorar" tem uma explicacao que nao precisa do
		// servidor. A MELHORA nao tem: ela aconteceu com `_golpesQuandoACuraVeio` golpes dados por
		// mim, ou seja nenhum. O que desenha aquele corpo na minha tela e o que o servidor manda.
		Conferir(_curaVista > 0 && _golpesQuandoACuraVeio == 0,
				 $"e o grau CAI tambem, SEM eu ter causado nada: a mascara do outro ficou limpa em "
			   + $"t={_curaVista:0.0}s com {_golpesQuandoACuraVeio} golpe(s) meus dados ate ali "
			   + $"(o dono registra a cura em t={Num(ferido, "t_da_cura"):0.0}s no relogio dele)");
	}

	/// <summary>
	/// ============================ FAMILIA 4 -- O MEMBRO ARRANCADO CONTINUA SUMINDO ============================
	/// E o degrau mais alto do sinal que substituiu a barra: um corpo com um braco a menos se le de
	/// longe e nao precisa de numero nenhum. Ele viaja separado das cinco zonas (a zona "bracos" tem
	/// DOIS bracos dentro -- ver `MascaraDeFeridas.Membro`), entao e o campo mais facil de perder num
	/// conserto de fio: `PutFeridas` escrever cinco bytes em vez de seis nao quebra nada.
	/// ==========================================================================================================
	/// </summary>
	private void OMembroArrancadoSome()
	{
		Conferir(_viAmputacaoNaMascara,
				 "a AMPUTACAO do corpo alheio chegou na mascara (o `--feridateste` arranca um braco e "
			   + "uma perna no nascimento -- sem isso esta familia nao mediu nada)");

		Conferir(_ampNoMaterialAcendeu && _quadrosDeAmputacaoConferidos > 30,
				 $"e ela chegou ao DESENHO do corpo alheio: o uniform de amputacao acendeu "
			   + $"({_quadrosDeAmputacaoConferidos} quadros com membro faltando)");

		Conferir(_ampNoLadoCerto,
				 "e no LADO CERTO da imagem em todo quadro (o material guarda o lado da IMAGEM, que "
			   + "inverte quando o boneco esta de frente -- ver `CharacterVisual.AplicarAmputacao`)");

		Conferir(_ampZerouDepoisDaCura,
				 "e o membro VOLTA: depois da cura do dono, o uniform de amputacao zerou no corpo "
			   + "alheio -- sem esta linha, um shader que apagasse o braco de todos pra sempre passaria");
	}

	/// <summary>
	/// ============================ FAMILIA 5 -- NAO SOBROU DESENHO SOBRE CABECA NENHUMA ============================
	/// A parte literal do pedido, e a mais facil de fingir: o arquivo da barra foi deletado, entao
	/// "nao existe classe de barra" e verdade hoje e continua verdade se alguem desenhar a mesma
	/// coisa com um `ColorRect` amanha. Por isso sao TRES perguntas:
	///
	///   * o tipo nao existe mais em lugar nenhum do binario (a regressao obvia);
	///   * nenhum descendente dos corpos e barra por TIPO ou por NOME (a regressao disfarcada);
	///   * e ninguem desenha acima da cabeca alem do BALAO DE FALA (a regressao de qualquer formato:
	///     a barra antiga era um `Node2D` com dois `DrawRect`, e nenhum dos dois filtros acima a
	///     pegaria).
	///
	/// NOS DOIS CORPOS, o alheio E o meu -- o dono disse "nem a sua". E com CONTROLE de varredura:
	/// uma busca que nao achasse node nenhum devolveria "nada suspeito" e passaria como perfeita.
	/// ==============================================================================================================
	/// </summary>
	private void NaoSobrouDesenhoSobreCabecaNenhuma(GameClient cli)
	{
		var tipos = new List<string>();
		foreach (Type t in typeof(RoboDeSigiloDeVida).Assembly.GetTypes())
		{
			string n = t.Name.ToLowerInvariant();
			if (n.Contains("healthbar") || n.Contains("barradevida") || n.Contains("barradesaude")
				|| n.Contains("hpbar") || n.Contains("vidabar")) tipos.Add(t.Name);
		}
		Conferir(tipos.Count == 0,
				 "nenhum TIPO de barra de vida existe no binario"
			   + (tipos.Count > 0 ? " -- achei: " + string.Join(", ", tipos) : ""));

		(int nAlheio, List<string> susAlheio) = VarrerCorpo(CorpoAlheio());
		(int nMeu, List<string> susMeu) = VarrerCorpo(World.Instancia?.CorpoDeTeste(cli.LocalId));

		Conferir(nAlheio >= 5 && nMeu >= 5,
				 $"a varredura olhou corpo de verdade nos dois lados ({nMeu} nodes no MEU corpo, "
			   + $"{nAlheio} no ALHEIO) -- sem este controle, 'nao achei barra' e 'nao olhei' sao a mesma frase");

		foreach (string s in susAlheio.Concat(susMeu)) GD.Print("[vida-b]        > " + s);
		Conferir(susAlheio.Count == 0,
				 $"nada desenha sobre a cabeca do CORPO ALHEIO alem do balao de fala ({susAlheio.Count} suspeito(s))");
		Conferir(susMeu.Count == 0,
				 $"nem sobre a MINHA -- o dono disse 'nem a sua' ({susMeu.Count} suspeito(s))");
	}

	/// <summary>
	/// Onde comeca "acima da cabeca". O sprite e 32x32 e centrado, entao o topo da cabeca esta em
	/// -16; a barra deletada ocupava de -23 a -18. Qualquer coisa que desenhe daqui pra cima esta na
	/// faixa que o dono mandou limpar, e o balao de fala (-26) e a unica excecao conhecida.
	/// </summary>
	private const float AcimaDaCabeca = -17f;

	/// <summary>
	/// ============================ O QUE PODE PASSAR DA CABECA, E POR QUE ============================
	/// A regra da familia 5 e "ninguem desenha ACIMA DA CABECA", e ela reprova por FORMATO nenhum --
	/// e assim que ela pega uma barra desenhada a mao (a que foi deletada era um `Node2D` com dois
	/// `DrawRect`, que nenhum filtro de tipo pegaria). O preco disso e que ela tambem pega o que tem
	/// motivo pra estar la, e cada motivo mora nesta lista:
	///
	///   * <see cref="BalaoDeFala"/> -- a fala, em -26 px. E o unico desenho de INFORMACAO que o dono
	///     quer sobre a cabeca dos outros, e ele ja existia antes da barra.
	///   * a AURA, a CARGA, a NEBULOSA e os RAIOS -- energia, e energia envolve o corpo INTEIRO por
	///     construcao: o quad da nuvem e ancorado em `-lado/2` justamente pra ficar centrado no
	///     boneco, entao metade dele esta acima da cabeca em todo mundo, o tempo todo.
	///
	/// A LISTA E DE TIPOS DE EFEITO, e vale pros descendentes deles: o que se permite e "energia sobe
	/// mais que a cabeca", nao "qualquer coisa chamada Quad". Um mostrador novo pendurado ali reprova
	/// ate alguem escrever aqui por que ele nao e uma barra de vida com outro nome -- que e
	/// exatamente a conversa que este pedido do dono existe pra forcar.
	/// ==========================================================================================
	/// </summary>
	private static bool EhEfeitoQueEnvolveOCorpo(Node n) =>
		n is BalaoDeFala or Aura or CargaVisual or NebulosaDaForma or RaiosDaForma;

	private static (int, List<string>) VarrerCorpo(Node2D? corpo)
	{
		var sus = new List<string>();
		int n = 0;
		if (corpo == null || !GodotObject.IsInstanceValid(corpo)) return (0, sus);

		void Descer(Node no, float y, bool dentroDeEfeito)
		{
			foreach (Node f in no.GetChildren())
			{
				n++;
				float alt = y + (f is Node2D n2 ? n2.Position.Y : 0f);
				string tipo = f.GetType().Name;
				string nome = f.Name.ToString();
				bool efeito = dentroDeEfeito || EhEfeitoQueEnvolveOCorpo(f);

				// O NOME E O TIPO VALEM MESMO DENTRO DE UM EFEITO: a permissao acima e pra ALTURA, e
				// nao um lugar onde da pra esconder um mostrador de vida.
				if (NomeDeVida.IsMatch(nome) || NomeDeVida.IsMatch(tipo))
					sus.Add($"'{nome}' ({tipo}) -- nome de VIDA num filho do corpo");
				else if (f is Godot.Range or ColorRect or TextureProgressBar)
					sus.Add($"'{nome}' ({tipo}) -- desenho de BARRA pendurado no corpo");
				else if (alt <= AcimaDaCabeca && f is CanvasItem && !efeito)
					sus.Add($"'{nome}' ({tipo}) desenha {-alt:0} px acima da origem do corpo");

				Descer(f, alt, efeito);
			}
		}

		Descer(corpo, 0, false);
		return (n, sus);
	}

	/// <summary>
	/// ============================ FAMILIA 6 -- O CONSUMIDOR QUE TROCOU DE FONTE ============================
	/// O respingo de sangue no chao era disparado pela VIDA que vinha no snapshot (`vida < 35 && vida
	/// caiu`). Aquele campo morreu, e o gatilho passou pro GRAU DE FERIDA. Um consumidor que troca de
	/// fonte precisa de uma medida que diga que ele CONTINUA disparando -- senao o conserto entrega
	/// um efeito que ninguem ve sumir.
	///
	/// O LIMIAR NAO E ESCRITO AQUI: ele e derivado da mesma curva do Core (65% de dano), que e a
	/// derivacao que o comentario do consumidor afirma. Se alguem mexer na curva, os dois lados andam
	/// juntos; se alguem cravar um numero la, esta bancada e quem grita.
	///
	/// E A ORDEM E METADE DA PROVA: nada pode respingar ENQUANTO o corpo esta limpo (logo depois da
	/// cura), e tem que respingar DEPOIS que o sangue dele cruza o limiar. So o total final ficaria
	/// verde com um respingo que caisse por qualquer outro motivo.
	/// ======================================================================================================
	/// </summary>
	private void ORespingoVemDoGrau()
	{
		// ESTA LINHA E O CONTROLE DA FAMILIA, e ela tem que dizer isso: se ela e a primeira a cair, a
		// rodada nao machucou o bastante pra haver sangue (aumente o `--vidafim`) e as duas linhas
		// abaixo estao falando de uma briga que nao aconteceu -- nao de um consumidor quebrado.
		Conferir(_cruzouOLimiar,
				 $"o sangue do corpo alheio chegou ao limiar do respingo (grau {LimiarDoRespingo()}, derivado "
			   + "da curva do Core em 65% de dano) -- se so ESTA linha caiu, a rodada foi curta demais e as "
			   + "de baixo nao tem veredito");

		Conferir(_respingosDoOutro > 0,
				 $"e o sangue CAIU no chao ao lado DAQUELE corpo ({_respingosDoOutro} respingo(s) a menos de "
			   + $"{RaioDoRespingo:0} px dele, de {Decalques.SangueDeTeste} na zona inteira) -- o efeito que "
			   + "lia a vida alheia continua vivo lendo o GRAU");

		Conferir(_respingosDoOutro > 0 && _menorSangueNumRespingo >= LimiarDoRespingo(),
				 $"e NENHUM deles caiu com ele pouco ferido: o menor sangue que ele tinha num respingo foi "
			   + $"{(_menorSangueNumRespingo == int.MaxValue ? -1 : _menorSangueNumRespingo)}, e o limiar e "
			   + $"{LimiarDoRespingo()} (corpo arranhado nao pinga)");
	}

	/// <summary>
	/// O degrau de sangue de um corpo em 65% de dano -- pela mesma funcao do Core que o servidor usa.
	///
	/// Escrito assim, e nao como um `4` cravado, porque este numero E a afirmacao do consumidor: se a
	/// curva mudar, o limiar dele muda junto e esta bancada acompanha em vez de virar o unico lugar
	/// do projeto que ainda acha que sao quatro degraus.
	/// </summary>
	private static int LimiarDoRespingo()
	{
		Body corpo = Body.Novo();
		foreach (BodyPart p in corpo.Partes) p.Vida = p.VidaMax * 0.35;
		return SangueDe(Jandirus.Core.Combat.Feridas.De(corpo));
	}

	/// <summary>
	/// O OUTRO LADO DO SOQUETE, trazido pro placar. O papel A ja julga a propria vida no processo
	/// dele; esta linha existe pra que UM placar diga se a rodada inteira valeu -- um veredito verde
	/// aqui com o outro processo vermelho seria a bancada mentindo por omissao.
	/// </summary>
	private void OContraExemploDoOutroLado(Dictionary<string, string> ferido)
	{
		Conferir(ferido.Count > 0,
				 "o processo do FERIDO escreveu o que mediu (`sigilo-do-ferido.txt`) -- sem ele nao ha "
			   + "contra-exemplo, e uma bancada sem contra-exemplo fica verde com o jogo inteiro mudo");

		double ok = Num(ferido, "ok"), falhas = Num(ferido, "falhas");
		Conferir(ok > 0 && falhas == 0,
				 $"e o placar dele fechou verde: {ok:0} ok, {falhas:0} falha(s) -- e la que mora a familia 2 "
			   + "(a vida do DONO chega, como numero, e a barra da HUD continua seguindo ela)");
	}

	private void Fechar()
	{
		_fechou = true;
		Parar();
		// SAIR SOZINHA em vez de esperar o `Stop-Process` do roteiro: matar o processo a forca deixa a
		// saida padrao sem descarregar, e a bancada "termina" com metade das linhas no arquivo -- o que
		// se le exatamente igual a uma bancada que TRAVOU.
		GetTree().Quit();
	}
}
