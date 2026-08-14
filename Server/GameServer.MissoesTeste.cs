using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DOS DEVERES DE CARGO -- `--cargomissoes`. Vocacao, prazo, servico, renome e destituicao.
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// O sistema inteiro e feito de PRAZOS de 72 minutos e de um contador de tres falhas. Jogando, a
/// primeira falha chega em pouco mais de uma hora e a destituicao em quase quatro -- e a destituicao
/// e a unica coisa que faz um cargo com dever significar alguma coisa. Um sistema cujo desfecho
/// ninguem consegue esperar e um sistema nao testado.
///
/// E ele e uma CORRENTE longa: a vocacao (Core), o sorteio, o relogio do mundo, o congelamento por
/// ausencia, o disco, a destituicao (`Destronar`), a dadiva (`ReconciliarDadiva`) e o portao de
/// renome do `ReivindicarCargo`. Qualquer elo pode estar certo com a corrente arrebentada -- foi
/// assim que a `RankDef.Concede` passou meses declarando trinta kits que ninguem lia, e foi assim
/// que o `TemDeveres` passou este tempo todo marcado nos 17 cargos certos e sem UM leitor.
/// ==============================================================================
///
/// AS QUATRO SECOES:
///  1. AS REGRAS PURAS -- vocacao derivada da tabela, escada derivada dos `Degraus`, prazo em dias
///     IN-GAME, quem serve de alvo, quem conta ponto de servico.
///  2. O CICLO VIVO -- atribuir, cumprir, pagar, falhar TRES vezes e ser DESTITUIDO.
///  3. O SERVICO E A VERBA -- as duas tarefas que ninguem cumpre matando.
///  4. O RELOGIO -- o congelamento por ausencia e o perdao de boot.
///
/// O ESTADO DE VERDADE E FOTOGRAFADO: `cargos.txt`, o livro das missoes e o adianto do ceu entram na
/// foto e voltam no `finally` (com regravacao), como a `--cargoportas` faz com os tres arquivos dela.
/// </summary>
public partial class GameServer
{
	private const int IdBaseDasMissoes = 90_800;

	private int _misOk, _misFalhou;

	private void AfirmarMis(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _misOk++; GD.Print($"[missoes]   OK    {oque}"); return; }
		_misFalhou++;
		GD.PrintErr($"[missoes]   FALHA {oque}   {detalhe}");
	}

	/// <summary>
	/// ADIANTA O RELOGIO DO MUNDO -- pela MESMA manivela do ceu (`_adiantoDoCeu`) que a bancada da
	/// conquista ja usa, e nao por um relogio paralelo. E o que torna um prazo de tres dias in-game
	/// testavel sem esperar 72 minutos: o codigo de producao le o `TempoDoMundo` como sempre.
	/// </summary>
	private void AdiantarMundo(double segundos) => _adiantoDoCeu += segundos;

	/// <summary>Uma volta do motor. Ele so anda uma vez por minuto do mundo -- ver `TickDasMissoes`.</summary>
	private void UmaVoltaDoMotor()
	{
		AdiantarMundo(MissoesDeCargo.SegundosDoLaco);
		TickDasMissoes();
	}

	public void RodarBancadaDasMissoes()
	{
		_misOk = _misFalhou = 0;
		GD.Print("[missoes] ============ DEVERES DE CARGO: vocacao, prazo, renome, destituicao ============");

		// A FOTO DO MUNDO DE VERDADE.
		var tronosReais = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		var missoesReais = new Dictionary<string, FichaDeMissao>(_missoes, StringComparer.OrdinalIgnoreCase);
		double cofreReal = _cofreDaTerra, ceuReal = _adiantoDoCeu, voltaReal = _proximaVoltaDasMissoes;

		_tronos.Clear();
		_missoes.Clear();
		_cofreDaTerra = 0;

		// O MOTOR JA NASCE ANCORADO NO AGORA. Com ele em zero, a primeira volta so ANCORARIA (e o
		// comportamento certo em producao: o primeiro tique nao pode cobrar um minuto que ninguem
		// viveu) e a bancada leria a ancoragem como "o motor nao fez nada".
		_proximaVoltaDasMissoes = TempoDoMundo;

		// ============================ A TERRA E VEGETA SAIEM DO LIVRO DE DOMINIOS ============================
		// Tres tarefas mudam de cara conforme o planeta do cargo estar sob bandeira alheia (e a
		// primeira pergunta do `rq_assign`). Num servidor em que alguem conquistou a Terra, o Guardiao
		// receberia LIBERTAR onde a bancada espera CACAR -- e a bancada acusaria um defeito que nao
		// existe. Os dominios voltam inteiros no `finally`.
		// ================================================================================================
		var dominiosGuardados = _dominios
			.Where(d => d.Planeta is "Earth" or "Vegeta").ToList();
		foreach (Dominio d in dominiosGuardados) _dominios.Remove(d);

		// UMA ZONA SO PRA BANCADA, e vazia de gente de verdade: o servico conta VISITANTES por perto,
		// entao um jogador real parado no mesmo planeta pontuaria pelo cargo forjado.
		ZoneKey zona = ZoneKey.Premade(
			Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		ServerPlayer Forjar(int i, string nome, string raca = "Human")
		{
			var novo = new ServerPlayer
			{
				Id = IdBaseDasMissoes + i,
				Peer = null,
				Name = nome,
				Race = raca,
				Genero = "Male",
				Idade = 30,
				Zone = zona,
				Pos = new Vec2(i * 2f * ZoneCollision.TileSize, 0),
				Conta = $"bancada_missoes_{i}",
				Slot = 0,
				Ficha = new Fighter { Race = raca, BP = 1_000_000 },
				Livro = new SkillBook(),
			};
			novo.Ficha.Class = "Normal";
			PorNoMundo(novo);
			novo.Ficha.Ki = novo.Ficha.MaxKi;
			return novo;
		}

		ServerPlayer guardiao = Forjar(1, "bancada: o Guardiao", "Namekian");
		ServerPlayer vilao = Forjar(2, "bancada: o vilao");
		ServerPlayer mestre = Forjar(3, "bancada: o mestre");
		ServerPlayer aluno = Forjar(4, "bancada: o aluno");
		ServerPlayer presidente = Forjar(5, "bancada: o Presidente");
		ServerPlayer kaio = Forjar(6, "bancada: o Kaio");

		try
		{
			AsRegrasSaoPuras();
			OCicloVivo(guardiao, vilao);
			OServicoEAVerba(mestre, aluno, presidente, kaio);
			ORelogio(guardiao, vilao);
		}
		catch (Exception e) { AfirmarMis($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? ""); }
		finally
		{
			foreach (ServerPlayer p in new[] { guardiao, vilao, mestre, aluno, presidente, kaio })
			{
				ZoneList(p.Zone.Hash).Remove(p);
				_players.Remove(p.Id);
			}

			_tronos.Clear();
			foreach ((string k, string v) in tronosReais) _tronos[k] = v;
			SalvarCargos();

			_missoes.Clear();
			foreach ((string k, FichaDeMissao v) in missoesReais) _missoes[k] = v;
			_cofreDaTerra = cofreReal;
			SalvarMissoes();

			_dominios.AddRange(dominiosGuardados);

			_adiantoDoCeu = ceuReal;
			_proximaVoltaDasMissoes = voltaReal;
		}

		GD.Print($"[missoes] ============ {_misOk} OK, {_misFalhou} FALHA(S) ============");
	}

	// =====================================================================
	// 1. AS REGRAS PURAS
	// =====================================================================
	private void AsRegrasSaoPuras()
	{
		// ---- as quatro listas do DM, DERIVADAS da tabela de cargos ----
		// Se algum dia a tabela perder uma marcacao, e aqui que se descobre -- e nao num servidor em
		// que um cargo simplesmente parou de cobrar nada, calado.
		var comDever = MissoesDeCargo.ComDeveres.ToList();
		AfirmarMis("17 cargos tem deveres (o RQ_ALL do DM, derivado)", comDever.Count == 17, $"{comDever.Count}");
		AfirmarMis("11 deles sao de SABEDORIA (o RQ_WISDOM)",
			comDever.Count(r => r.Sabedoria) == 11, $"{comDever.Count(r => r.Sabedoria)}");
		AfirmarMis("7 servem MORTOS (o RQ_SKY)",
			comDever.Count(r => r.DoOutroMundo) == 7, $"{comDever.Count(r => r.DoOutroMundo)}");
		AfirmarMis("2 sao MALIGNOS e nao ganham karma (o RQ_EVIL)",
			comDever.Count(r => r.Maligno) == 2, $"{comDever.Count(r => r.Maligno)}");
		AfirmarMis("o Deus da Destruicao e o Anjo NAO estao nos deveres (tem motor proprio)",
			!comDever.Any(r => r.Chave is "godofdestruction" or "angel"));
		AfirmarMis("os 4 Anciaos cardeais tambem nao (o DM nao os poe no RQ_ALL)",
			!comDever.Any(r => Nomeacao.EhAssento(r.Chave)));

		// ---- a vocacao decide a tarefa, e a ORDEM e a regra ----
		RankDef guardian = Cargos.Get("guardian")!, president = Cargos.Get("president")!;
		RankDef frost = Cargos.Get("frostlord")!, demon = Cargos.Get("demonlord")!, kai = Cargos.Get("nkai")!;

		AfirmarMis("cargo de SABEDORIA recebe SERVICO",
			MissoesDeCargo.Escolher(kai, false) == TipoDeTarefa.Servico);
		AfirmarMis("...mesmo com o planeta dele dominado (servico nao olha bandeira)",
			MissoesDeCargo.Escolher(kai, true) == TipoDeTarefa.Servico);
		AfirmarMis("o Guardiao caca VILAO com a Terra livre",
			MissoesDeCargo.Escolher(guardian, false) == TipoDeTarefa.Vilao);
		AfirmarMis("...e LIBERTA a Terra quando ela esta sob bandeira alheia",
			MissoesDeCargo.Escolher(guardian, true) == TipoDeTarefa.Libertar);
		AfirmarMis("o Presidente financia a Terra",
			MissoesDeCargo.Escolher(president, false) == TipoDeTarefa.Verba);
		AfirmarMis("...mas LIBERTAR vem antes da verba (a ordem do rq_assign)",
			MissoesDeCargo.Escolher(president, true) == TipoDeTarefa.Libertar);
		AfirmarMis("o Lorde do Gelo destroi um mundo",
			MissoesDeCargo.Escolher(frost, false) == TipoDeTarefa.Planeta);
		AfirmarMis("o lorde MALIGNO caca HEROI, e nao vilao",
			MissoesDeCargo.Escolher(demon, false) == TipoDeTarefa.Heroi);

		AfirmarMis("so tres cargos tem planeta proprio",
			MissoesDeCargo.PlanetaDoCargo("kov") == "Vegeta"
			&& MissoesDeCargo.PlanetaDoCargo("guardian") == "Earth"
			&& MissoesDeCargo.PlanetaDoCargo("president") == "Earth"
			&& MissoesDeCargo.PlanetaDoCargo("turtle").Length == 0);

		// ---- quem serve de alvo: as duas metades sao OPOSTAS ----
		AfirmarMis("o protetor caca quem tem o selo de vilao",
			MissoesDeCargo.ServeDeAlvo(false, selado: true, karma: 100));
		AfirmarMis("...e quem tem karma podre sem selo",
			MissoesDeCargo.ServeDeAlvo(false, selado: false, karma: -50));
		AfirmarMis("...e NAO caca gente comum",
			!MissoesDeCargo.ServeDeAlvo(false, selado: false, karma: 0));
		AfirmarMis("o lorde maligno caca quem tem karma ALTO",
			MissoesDeCargo.ServeDeAlvo(true, selado: false, karma: 50));
		AfirmarMis("...e o vilao selado NAO serve de presa pra ele",
			!MissoesDeCargo.ServeDeAlvo(true, selado: true, karma: -80));

		// ---- o servico: alma pro Outro Mundo, aluno TREINANDO pro mestre ----
		AfirmarMis("o Outro Mundo pontua com ALMAS",
			MissoesDeCargo.ContaComoServico(true, visitanteMorto: true, visitanteTreinando: false));
		AfirmarMis("...e nao com vivos", !MissoesDeCargo.ContaComoServico(true, false, true));
		AfirmarMis("o mestre pontua com quem TREINA",
			MissoesDeCargo.ContaComoServico(false, false, true));
		AfirmarMis("...e NAO com quem so esta por perto (servir nao e estar onde tem gente)",
			!MissoesDeCargo.ContaComoServico(false, false, false));
		AfirmarMis("mestre MORTO nao da aula", !MissoesDeCargo.PodeServir(false, portadorMorto: true));
		AfirmarMis("...mas o Kaio atende ate morto", MissoesDeCargo.PodeServir(true, portadorMorto: true));
		AfirmarMis("a meta da escada divina e 10, a dos mestres e 15",
			MissoesDeCargo.MetaDeServico("grandkai") == 10 && MissoesDeCargo.MetaDeServico("turtle") == 15
			&& MissoesDeCargo.MetaDeServico("yemma") == 15);

		// ---- a escada, DERIVADA dos `Degraus` ----
		// O DM escreve a escada duas vezes e deixou o aviso escrito de que as duas divergem. Aqui a
		// segunda e LIDA da primeira, e esta checagem e o que prova que a leitura casa.
		var acimaDoKaio = MissoesDeCargo.DegrausAcima("nkai").Select(r => r.Chave).ToList();
		AfirmarMis("acima do Kaio do Norte ficam o Grand Kai e o Kaioshin",
			acimaDoKaio.Count == 2 && acimaDoKaio.Contains("grandkai") && acimaDoKaio.Contains("kaioshin"),
			string.Join(",", acimaDoKaio));
		AfirmarMis("acima do Grand Kai so o Kaioshin",
			MissoesDeCargo.DegrausAcima("grandkai").Select(r => r.Chave).SequenceEqual(["kaioshin"]));
		AfirmarMis("o Kaioshin e o fim da escada", !MissoesDeCargo.DegrausAcima("kaioshin").Any());
		AfirmarMis("o Guardiao nao tem degrau acima", !MissoesDeCargo.DegrausAcima("guardian").Any());
		AfirmarMis("3 tarefas destrancam a ascensao, 2 nao",
			MissoesDeCargo.PodeAscender(3) && !MissoesDeCargo.PodeAscender(2));

		// ---- O PRAZO E EM DIAS IN-GAME, E NAO NOS 60 MINUTOS DO DM ----
		// Este e o numero que o pedido cobrava por escrito. Ele sai do dia do CEU deste port, e nao
		// de uma constante solta: mexer no dia in-game arrasta os deveres junto, que e literalmente o
		// que o comentario do `RQ_TASK_DAYS` promete.
		AfirmarMis("o prazo e 3 dias IN-GAME derivados do ceu (nao um numero solto)",
			Math.Abs(MissoesDeCargo.SegundosDePrazo - 3 * Espaco.SegundosPorDiaInGame) < 1e-9,
			$"{MissoesDeCargo.SegundosDePrazo:0} s");
		AfirmarMis("...e isso da 72h in-game", MissoesDeCargo.TextoDeTempo(MissoesDeCargo.SegundosDePrazo).StartsWith("72h"),
			MissoesDeCargo.TextoDeTempo(MissoesDeCargo.SegundosDePrazo));
		AfirmarMis("o texto tambem diz o equivalente REAL (senao ninguem sabe se da tempo)",
			MissoesDeCargo.TextoDeTempo(MissoesDeCargo.SegundosDePrazo).Contains("min reais"));

		// ---- o aparo de janela: teto que DISPARA ----
		var velha = new FichaDeMissao { Tarefa = TipoDeTarefa.Servico, Prazo = 1_000_000, Proxima = 1_000_000 };
		AfirmarMis("ficha gravada com janela maior que a de hoje se conserta sozinha",
			MissoesDeCargo.ApararJanela(velha, 0)
			&& Math.Abs(velha.Prazo - MissoesDeCargo.SegundosDePrazo) < 1e-9);
		var ok = new FichaDeMissao { Tarefa = TipoDeTarefa.Servico, Prazo = 10, Proxima = 10 };
		AfirmarMis("...e uma janela sa nao e mexida", !MissoesDeCargo.ApararJanela(ok, 0));
	}

	// =====================================================================
	// 2. O CICLO VIVO -- atribuir, cumprir, falhar TRES vezes, ser destituido
	// =====================================================================
	private void OCicloVivo(ServerPlayer guardiao, ServerPlayer vilao)
	{
		_tronos.Clear();
		_missoes.Clear();
		_tronos["guardian"] = guardiao.Conta;
		ReconciliarDadiva(guardiao);

		// O VILAO EXISTE E ESTA DE PE: sem alvo elegivel o sorteio adia 10 min e nada acontece.
		vilao.Karma = -80;
		vilao.Ficha.dead = false;
		guardiao.Karma = 50;
		double zeniAntes = guardiao.Ficha.Zeni;

		// A PRIMEIRA VOLTA SO ANCORA (a ficha nasce com o intervalo inteiro pela frente).
		UmaVoltaDoMotor();
		AfirmarMis("o trono ocupado ganha ficha de deveres", _missoes.ContainsKey("guardian"));
		AfirmarMis("...e ela nasce SEM tarefa (a primeira chega no intervalo)",
			_missoes["guardian"].Tarefa == TipoDeTarefa.Nenhuma);

		// PASSADO O INTERVALO, A TAREFA CHEGA.
		AdiantarMundo(MissoesDeCargo.SegundosDeIntervalo);
		UmaVoltaDoMotor();
		FichaDeMissao f = _missoes["guardian"];
		AfirmarMis("passado o intervalo, o Guardiao recebe a caca ao vilao",
			f.Tarefa == TipoDeTarefa.Vilao && string.Equals(f.AlvoConta, vilao.Conta, StringComparison.OrdinalIgnoreCase),
			$"{f.Tarefa} / {f.AlvoNome}");

		// CUMPRIU: o alvo caiu.
		vilao.Ficha.dead = true;
		UmaVoltaDoMotor();
		f = _missoes["guardian"];
		AfirmarMis("o vilao morto CUMPRE a tarefa", f.Tarefa == TipoDeTarefa.Nenhuma && f.Cumpridas == 1,
			$"{f.Tarefa} / renome {f.Cumpridas}");
		AfirmarMis("...e ela paga zeni",
			Math.Abs(guardiao.Ficha.Zeni - (zeniAntes + MissoesDeCargo.ZeniPorTarefa)) < 1e-6,
			$"{guardiao.Ficha.Zeni:N0}");
		AfirmarMis("...e karma, porque o Guardiao nao e um cargo maligno",
			guardiao.Karma == 55, $"{guardiao.Karma}");

		// A TAREFA SE ANULA SEM PUNIR quando o alvo some do mundo -- e a unica saida justa.
		vilao.Ficha.dead = false;
		AdiantarMundo(MissoesDeCargo.SegundosDeIntervalo);
		UmaVoltaDoMotor();
		AfirmarMis("uma segunda tarefa e atribuida", _missoes["guardian"].Tarefa == TipoDeTarefa.Vilao);
		ZoneList(vilao.Zone.Hash).Remove(vilao);
		_players.Remove(vilao.Id);
		UmaVoltaDoMotor();
		f = _missoes["guardian"];
		AfirmarMis("o alvo que deixou o mundo ANULA a tarefa sem cobrar falha",
			f.Tarefa == TipoDeTarefa.Nenhuma && f.Falhas == 0, $"{f.Tarefa} / falhas {f.Falhas}");

		// ---- AS TRES FALHAS, E A DESTITUICAO ----
		// O vilao volta ao mundo pra haver alvo; o prazo e que vai estourar.
		PorNoMundo(vilao);

		int destituido = 0;
		for (int volta = 1; volta <= MissoesDeCargo.FalhasQueDestituem; volta++)
		{
			AdiantarMundo(MissoesDeCargo.SegundosDeIntervalo);
			UmaVoltaDoMotor();
			if (!_missoes.TryGetValue("guardian", out FichaDeMissao? atual)
				|| atual.Tarefa == TipoDeTarefa.Nenhuma) break;

			// O PRAZO ESTOURA. Nada mais muda: o alvo continua vivo e o Guardiao continua parado.
			AdiantarMundo(MissoesDeCargo.SegundosDePrazo + 1);
			UmaVoltaDoMotor();

			if (!_tronos.ContainsKey("guardian")) { destituido = volta; break; }
			AfirmarMis($"a falha {volta} entra no contador",
				_missoes["guardian"].Falhas == volta, $"{_missoes["guardian"].Falhas}");
		}

		AfirmarMis("a TERCEIRA falha DESTITUI (o trono vaga)",
			destituido == MissoesDeCargo.FalhasQueDestituem && !_tronos.ContainsKey("guardian"),
			$"destituido na volta {destituido}");
		AfirmarMis("...e o kit do cargo sai junto (o Makankosappo do Guardiao)",
			!guardiao.Livro.Sabe("/datum/skill/rank/Makkankosappo"));

		// O TRONO VAGO NAO GUARDA FICHA: o proximo dono comeca limpo.
		UmaVoltaDoMotor();
		AfirmarMis("trono vago nao guarda ficha de deveres", !_missoes.ContainsKey("guardian"));

		// ---- A FICHA ZERA SOZINHA NA TROCA DE DONO ----
		// Este e o defeito injetado desta secao: se a ficha fosse do TRONO e nao de quem o carrega,
		// o novo Guardiao herdaria o renome (e as falhas) de quem serviu antes.
		_tronos["guardian"] = guardiao.Conta;
		UmaVoltaDoMotor();
		_missoes["guardian"].Cumpridas = 7;
		_missoes["guardian"].Falhas = 2;
		_tronos["guardian"] = vilao.Conta;
		UmaVoltaDoMotor();
		AfirmarMis("trocar de dono ZERA a ficha (renome nao se herda)",
			_missoes["guardian"].Cumpridas == 0 && _missoes["guardian"].Falhas == 0,
			$"renome {_missoes["guardian"].Cumpridas}, falhas {_missoes["guardian"].Falhas}");

		// ---- O PORTAO DE RENOME DA ASCENSAO, PELO VERB DE PRODUCAO ----
		_tronos.Clear();
		_missoes.Clear();
		_tronos["nkai"] = guardiao.Conta;
		guardiao.Ficha.godki = new GodKiState { awakened = true, mastery = 40 };
		guardiao.Karma = 80;
		UmaVoltaDoMotor();

		ReivindicarCargo(guardiao, "grandkai");
		AfirmarMis("sem RENOME o Kaio NAO sobe a Grand Kai",
			!_tronos.ContainsKey("grandkai")
			&& string.Equals(CargoDe(guardiao.Conta), "nkai", StringComparison.OrdinalIgnoreCase),
			CargoDe(guardiao.Conta));

		_missoes["nkai"].Cumpridas = MissoesDeCargo.TarefasParaAscender;
		ReivindicarCargo(guardiao, "grandkai");
		AfirmarMis("com 3 tarefas cumpridas, a ascensao abre",
			string.Equals(CargoDe(guardiao.Conta), "grandkai", StringComparison.OrdinalIgnoreCase),
			CargoDe(guardiao.Conta));
		AfirmarMis("...e o degrau de baixo VAGA", !_tronos.ContainsKey("nkai"));

		UmaVoltaDoMotor();
		AfirmarMis("o cargo novo comeca com renome ZERO (a escada nao se sobe de graca duas vezes)",
			RenomeDe("grandkai") == 0, $"{RenomeDe("grandkai")}");

		_tronos.Clear();
		_missoes.Clear();
		guardiao.Ficha.godki = null;
		ReconciliarDadiva(guardiao);
		ReconciliarDadiva(vilao);
	}

	// =====================================================================
	// 3. O SERVICO E A VERBA
	// =====================================================================
	private void OServicoEAVerba(ServerPlayer mestre, ServerPlayer aluno, ServerPlayer presidente, ServerPlayer kaio)
	{
		_tronos.Clear();
		_missoes.Clear();

		// ---- O SERVICO DO MESTRE: so conta quem TREINA ----
		_tronos["turtle"] = mestre.Conta;
		aluno.Pos = mestre.Pos + new Vec2(2 * ZoneCollision.TileSize, 0);
		aluno.Ficha.train = false;
		aluno.Ficha.dead = false;

		UmaVoltaDoMotor();
		AdiantarMundo(MissoesDeCargo.SegundosDeIntervalo);
		UmaVoltaDoMotor();
		FichaDeMissao f = _missoes["turtle"];
		AfirmarMis("o mestre (SABEDORIA) recebe SERVICO, e a meta e 15",
			f.Tarefa == TipoDeTarefa.Servico && f.Meta == 15, $"{f.Tarefa} meta {f.Meta}");

		UmaVoltaDoMotor();
		AfirmarMis("aluno PARADO do lado nao pontua", _missoes["turtle"].Progresso == 0,
			$"{_missoes["turtle"].Progresso}");

		aluno.Ficha.train = true;
		UmaVoltaDoMotor();
		AfirmarMis("aluno TREINANDO pontua 1 por minuto", _missoes["turtle"].Progresso == 1,
			$"{_missoes["turtle"].Progresso}");

		// LONGE NAO CONTA -- o raio de 6 tiles do DM.
		aluno.Pos = mestre.Pos + new Vec2(20 * ZoneCollision.TileSize, 0);
		UmaVoltaDoMotor();
		AfirmarMis("aluno a 20 tiles nao pontua (o raio e 6)", _missoes["turtle"].Progresso == 1,
			$"{_missoes["turtle"].Progresso}");
		aluno.Pos = mestre.Pos + new Vec2(2 * ZoneCollision.TileSize, 0);

		// A META FECHA A TAREFA.
		_missoes["turtle"].Progresso = _missoes["turtle"].Meta - 1;
		UmaVoltaDoMotor();
		f = _missoes["turtle"];
		AfirmarMis("bater a meta CUMPRE o servico", f.Tarefa == TipoDeTarefa.Nenhuma && f.Cumpridas == 1,
			$"{f.Tarefa} renome {f.Cumpridas}");
		aluno.Ficha.train = false;

		// ---- O KAIO ATENDE ALMAS, E NAO VIVOS ----
		_tronos.Clear();
		_missoes.Clear();
		_tronos["nkai"] = kaio.Conta;
		kaio.Pos = mestre.Pos + new Vec2(40 * ZoneCollision.TileSize, 0);
		aluno.Pos = kaio.Pos + new Vec2(2 * ZoneCollision.TileSize, 0);
		aluno.Ficha.train = true;

		UmaVoltaDoMotor();
		AdiantarMundo(MissoesDeCargo.SegundosDeIntervalo);
		UmaVoltaDoMotor();
		AfirmarMis("o Kaio (Outro Mundo) tem meta 10", _missoes["nkai"].Meta == 10, $"{_missoes["nkai"].Meta}");

		UmaVoltaDoMotor();
		AfirmarMis("vivo treinando NAO conta pro Outro Mundo", _missoes["nkai"].Progresso == 0,
			$"{_missoes["nkai"].Progresso}");

		aluno.Ficha.train = false;
		aluno.Ficha.dead = true;
		UmaVoltaDoMotor();
		AfirmarMis("ALMA por perto conta pro Kaio", _missoes["nkai"].Progresso == 1,
			$"{_missoes["nkai"].Progresso}");

		// O KAIO ATENDE ATE MORTO (o `sky` do DM) -- e o mestre nao atenderia.
		kaio.Ficha.dead = true;
		UmaVoltaDoMotor();
		AfirmarMis("...e o Kaio pontua ate estando MORTO", _missoes["nkai"].Progresso == 2,
			$"{_missoes["nkai"].Progresso}");
		kaio.Ficha.dead = false;
		aluno.Ficha.dead = false;

		// ---- A VERBA DO PRESIDENTE ----
		_tronos.Clear();
		_missoes.Clear();
		_tronos["president"] = presidente.Conta;
		presidente.Ficha.Zeni = 0;

		UmaVoltaDoMotor();
		AdiantarMundo(MissoesDeCargo.SegundosDeIntervalo);
		UmaVoltaDoMotor();
		AfirmarMis("o Presidente recebe a tarefa de VERBA",
			_missoes["president"].Tarefa == TipoDeTarefa.Verba, $"{_missoes["president"].Tarefa}");

		double cofreAntes = _cofreDaTerra;
		VerboDepositarVerba(presidente);
		AfirmarMis("sem zeni, o deposito e RECUSADO",
			Math.Abs(_cofreDaTerra - cofreAntes) < 1e-6 && _missoes["president"].Progresso == 0);

		presidente.Ficha.Zeni = MissoesDeCargo.VerbaDoPresidente;
		VerboDepositarVerba(presidente);
		AfirmarMis("com zeni, a verba SAI do bolso e entra no cofre",
			Math.Abs(presidente.Ficha.Zeni) < 1e-6
			&& Math.Abs(_cofreDaTerra - (cofreAntes + MissoesDeCargo.VerbaDoPresidente)) < 1e-6,
			$"bolso {presidente.Ficha.Zeni:N0}, cofre {_cofreDaTerra:N0}");

		UmaVoltaDoMotor();
		AfirmarMis("...e a tarefa se fecha",
			_missoes["president"].Tarefa == TipoDeTarefa.Nenhuma && _missoes["president"].Cumpridas == 1);

		_tronos.Clear();
		_missoes.Clear();
	}

	// =====================================================================
	// 4. O RELOGIO -- ausencia e perdao de boot
	// =====================================================================
	private void ORelogio(ServerPlayer portador, ServerPlayer vilao)
	{
		_tronos.Clear();
		_missoes.Clear();
		_tronos["guardian"] = portador.Conta;
		vilao.Karma = -80;
		vilao.Ficha.dead = false;

		UmaVoltaDoMotor();
		AdiantarMundo(MissoesDeCargo.SegundosDeIntervalo);
		UmaVoltaDoMotor();
		AfirmarMis("ha tarefa em voo antes do teste de ausencia",
			_missoes["guardian"].Tarefa == TipoDeTarefa.Vilao, $"{_missoes["guardian"].Tarefa}");

		// ============================ O PORTADOR SAI DO MUNDO: O RELOGIO CONGELA ============================
		// Este e o segundo defeito injetado: com o prazo correndo na ausencia, o cargo se perderia
		// DORMINDO -- tres falhas em pouco mais de tres horas, sem ninguem ter jogado. O DM congela, e
		// escreveu o motivo (`:644-651`).
		// ==============================================================================================
		ZoneList(portador.Zone.Hash).Remove(portador);
		_players.Remove(portador.Id);

		int falhasAntes = _missoes["guardian"].Falhas;
		AdiantarMundo(MissoesDeCargo.SegundosDePrazo * 3);
		for (int i = 0; i < 4; i++) UmaVoltaDoMotor();

		AfirmarMis("o portador AUSENTE nao acumula falha nenhuma",
			_missoes["guardian"].Falhas == falhasAntes && _tronos.ContainsKey("guardian"),
			$"{_missoes["guardian"].Falhas}");
		AfirmarMis("...e a tarefa continua de pe, com o prazo renascido",
			_missoes["guardian"].Tarefa == TipoDeTarefa.Vilao
			&& _missoes["guardian"].Prazo > TempoDoMundo);

		PorNoMundo(portador);

		// ---- O PERDAO DE BOOT ----
		// O mundo andou com o servidor fora do ar: sem o perdao, o primeiro tique pos-boot cobraria
		// uma falha de graca e anunciaria ao mundo inteiro.
		_missoes["guardian"].Prazo = TempoDoMundo - 1;
		PerdoarPrazosNoBoot();
		AfirmarMis("o perdao de boot devolve o prazo inteiro a uma tarefa vencida no escuro",
			_missoes["guardian"].Prazo > TempoDoMundo + MissoesDeCargo.SegundosDePrazo - 60,
			$"{_missoes["guardian"].Prazo - TempoDoMundo:0} s");

		// E ELE NAO PERDOA O QUE VENCEU COM O MUNDO DE PE: uma tarefa com folga nao e mexida.
		double prazoSao = TempoDoMundo + MissoesDeCargo.SegundosDePrazo / 2;
		_missoes["guardian"].Prazo = prazoSao;
		PerdoarPrazosNoBoot();
		AfirmarMis("...e nao mexe em quem ainda tem prazo de sobra",
			Math.Abs(_missoes["guardian"].Prazo - prazoSao) < 1e-6);

		_tronos.Clear();
		_missoes.Clear();
		ReconciliarDadiva(portador);
	}
}
