using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// O DUELO FORMAL PELO TITULO -- a terceira porta, e a unica que e uma MAQUINA DE ESTADO.
///
/// Porte de `Code/Modules/Ranks/GodOfDestruction.dm:424-518` (desafio, adiamento, `god_duel_run`) e
/// `:544-627` (as tarefas do Deus). As regras puras -- prazos, adiamentos, quem vence -- moram no
/// `Core/Ranks/PortasDeCargo.cs`; aqui mora quem esta online, quem foi teleportado e quem grava.
///
/// ============================ O QUE MUDA DO ORIGINAL, E POR QUE ============================
/// 1. **O `alert()` VIRA VERB.** No DM o desafio abre uma caixa BLOQUEANTE no cliente do Deus
///    (`:456`) e o codigo espera ali dentro. Um servidor autoritativo nao pode parar num pedido de
///    resposta -- entao o desafio fica PENDENTE, o Deus responde por verb (aceitar/adiar) e existe
///    um PRAZO pra resposta. Sem o prazo, um Deus que fechasse o jogo com o desafio na tela
///    trancaria o trono pra sempre (`god_duel_busy` fica em 1).
///    **Nao responder e ADIAR**, e nao "nao aconteceu": senao a saida mais barata pra covardia seria
///    ignorar a caixa -- exatamente o que a regra dos 3 adiamentos existe pra punir.
///
/// 2. **NAO HA ARENA.** O DM reusa a geometria do torneio da Terra (`TRN_*`, `:480`), que este port
///    ainda nao tem. O duelo acontece ONDE O DEUS ESTA, com o desafiante posto a quatro tiles e os
///    dois curados -- que e o que aquelas linhas de fato fazem (`trn_heal` + duas posicoes).
///
/// 3. **AS DUAS TAREFAS ESTAO LIGADAS -- e a segunda demorou uma fase inteira.** O DM sorteia o mundo
///    a destruir entre os `pspace_planets`, um REGISTRO de planetas procedurais vivos que este port
///    nao tem (aqui o universo e funcao pura da seed; o unico livro que existe e o dos MORTOS). Isto
///    ficou escrito aqui como impedimento ate os DEVERES DE CARGO precisarem da mesma resposta pro
///    Lorde do Gelo -- e a resposta e o SETOR do portador na carta estelar
///    (`SortearMundoParaDestruir`, em `GameServer.CargoMissoes.cs`), que e mais perto do original do
///    que uma lista global, porque o `pspace_planets` do DM tambem so contem o que esta perto de
///    alguem. A ordem e a de la (`:592-602`): o mundo so entra quando NAO ha vilao online. Sem
///    nenhum dos dois, o sorteio e adiado em 10 minutos -- literalmente o `else` do original (`:610`).
/// ========================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>O unico cargo com <see cref="PortaDoCargo.Duelo"/> hoje. A maquina nao sabe disso.</summary>
	private const string CargoEmDisputa = "godofdestruction";

	private readonly EstadoDoDuelo _duelo = new();

	// ---- o desafio esperando resposta (memoria: ver o cabecalho, item 1) ----
	private string _desafianteEsperando = "";
	private long _prazoDaResposta;

	/// <summary>
	/// Quanto o Deus tem pra responder ao desafio. **Nao esta no DM** (la a caixa espera pra
	/// sempre) e existe pelo motivo do item 1 do cabecalho.
	/// </summary>
	private const long PrazoDeResposta = 90_000;

	// ---- o duelo em curso ----
	private string _dueloDono = "", _dueloDesafiante = "";
	private long _dueloComeca, _dueloAcaba;

	// ---- a tarefa do Deus (`god_task_*`) ----
	private string _tarefaAlvo = "", _tarefaNomeDoAlvo = "";
	private long _tarefaPrazo, _tarefaProxima;
	private int _tarefaFalhas;

	/// <summary>
	/// A CHAVE DO MUNDO A DESTRUIR -- o segundo ramo do `god_task_new` (`:604-614`), e ele so entrou
	/// agora porque so agora ha de onde sortear (ver <see cref="SortearMundoParaDestruir"/>).
	///
	/// SEPARADO do <see cref="_tarefaAlvo"/> porque sao duas identidades diferentes (conta x
	/// `ChaveDePlaneta.Texto`) e misturar as duas na mesma string faria "o vilao chamado #123" e o
	/// planeta de seed 123 serem a mesma tarefa. Ter tarefa e ter UM dos dois preenchido.
	/// </summary>
	private string _tarefaPlanetaChave = "", _tarefaPlanetaNome = "";

	/// <summary>`GOD_TASK_INTERVAL` / `GOD_TASK_DEADLINE`: 2 dias reais (`:36-37`).</summary>
	private const long IntervaloDeTarefa = 2L * 24 * 60 * 60 * 1000;

	/// <summary>`GOD_TASK_FAILS_MAX` 3 (`:38`). E a destituicao por negligencia.</summary>
	private const int FalhasQueDestituem = 3;

	/// <summary>`GOD_TASK_VILLAIN_KARMA` -50: karma daqui pra baixo marca uma ameaca (`:39`).</summary>
	private const int KarmaDeAmeaca = -50;

	/// <summary>Sem alvo elegivel, tenta de novo em ~10 min (`:610`, `god_task_next + 6000`).</summary>
	private const long EsperaSemAlvo = 10L * 60 * 1000;

	// =====================================================================
	// PERSISTENCIA -- o `GoDState` do DM (`:632-658`)
	// =====================================================================

	/// <summary>Pelo `_store` e nao pelo caminho cravado -- ver o porque em `GameServer.Ranks.cs`.</summary>
	private string ArquivoDoTitulo =>
		System.IO.Path.Combine(_store?.Pasta ?? ProjectSettings.GlobalizePath("user://saves"), "titulo.txt");

	private void CarregarTitulo()
	{
		LimparEstadoDoTitulo();
		if (!System.IO.File.Exists(ArquivoDoTitulo)) return;
		foreach (string linha in System.IO.File.ReadAllLines(ArquivoDoTitulo))
		{
			string[] p = linha.Split('\t');
			if (p.Length != 2) continue;
			switch (p[0])
			{
				case "desde": _duelo.TituloDesde = long.TryParse(p[1], out long a) ? a : 0; break;
				case "ultimo": _duelo.UltimoDuelo = long.TryParse(p[1], out long b) ? b : 0; break;
				case "adiamentos": _duelo.Adiamentos = int.TryParse(p[1], out int c) ? c : 0; break;
				case "semana": _duelo.SemanaDosAdiamentos = long.TryParse(p[1], out long d) ? d : 0; break;
				case "tarefa": _tarefaAlvo = p[1]; break;
				case "tarefa_nome": _tarefaNomeDoAlvo = p[1]; break;
				case "tarefa_prazo": _tarefaPrazo = long.TryParse(p[1], out long e) ? e : 0; break;
				case "tarefa_proxima": _tarefaProxima = long.TryParse(p[1], out long f) ? f : 0; break;
				case "falhas": _tarefaFalhas = int.TryParse(p[1], out int g) ? g : 0; break;
				// AUSENTES NUM ARQUIVO ANTIGO = vazias, que e exatamente "sem tarefa de planeta".
				case "tarefa_planeta": _tarefaPlanetaChave = p[1]; break;
				case "tarefa_planeta_nome": _tarefaPlanetaNome = p[1]; break;
			}
		}
	}

	private void SalvarTitulo()
	{
		try
		{
			System.IO.File.WriteAllLines(ArquivoDoTitulo,
			[
				$"desde\t{_duelo.TituloDesde}", $"ultimo\t{_duelo.UltimoDuelo}",
				$"adiamentos\t{_duelo.Adiamentos}", $"semana\t{_duelo.SemanaDosAdiamentos}",
				$"tarefa\t{_tarefaAlvo}", $"tarefa_nome\t{_tarefaNomeDoAlvo}",
				$"tarefa_prazo\t{_tarefaPrazo}", $"tarefa_proxima\t{_tarefaProxima}",
				$"falhas\t{_tarefaFalhas}",
				$"tarefa_planeta\t{_tarefaPlanetaChave}", $"tarefa_planeta_nome\t{_tarefaPlanetaNome}",
			]);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao gravei o estado do titulo: {e.Message}"); }
	}

	/// <summary>
	/// O TITULO VAGOU: o relogio do proximo Deus comeca do zero. E o `god_lose_title` zerando o
	/// estado (`:110-111`) mais o que o `god_gain_title` reinicia ao coroar (`:85-90`).
	/// </summary>
	private void LimparEstadoDoTitulo()
	{
		_duelo.TituloDesde = _duelo.UltimoDuelo = _duelo.SemanaDosAdiamentos = 0;
		_duelo.Adiamentos = 0;
		_duelo.EmDuelo = false;
		_desafianteEsperando = "";
		_dueloDono = _dueloDesafiante = "";
		_tarefaAlvo = _tarefaNomeDoAlvo = "";
		_tarefaPlanetaChave = _tarefaPlanetaNome = "";
		_tarefaPrazo = _tarefaProxima = 0;
		_tarefaFalhas = 0;
		SalvarTitulo();
	}

	/// <summary>Quem carrega o titulo agora ("" = trono vago).</summary>
	private string ContaDoDeus =>
		_tronos.TryGetValue(CargoEmDisputa, out string? c) ? c : "";

	// =====================================================================
	// 1. O DESAFIO
	// =====================================================================

	/// <summary>
	/// `Desafiar_God_of_Destruction` (`:424`). A ordem das recusas e a de la, e ela decide o que o
	/// desafiante le.
	/// </summary>
	private void VerboDesafiar(ServerPlayer pl)
	{
		long agora = NowMs();
		string dono = ContaDoDeus;
		ServerPlayer? deus = OnlinePorConta(dono);

		RecusaDeDuelo r = Duelo.PodeDesafiar(_duelo, agora,
			haDono: dono.Length > 0,
			ehODono: string.Equals(dono, pl.Conta, StringComparison.OrdinalIgnoreCase),
			desafianteDePe: !pl.Ficha.dead && !pl.Ficha.KO,
			godKiDesperto: pl.Ficha.godki?.awakened == true,
			donoPresente: deus != null);

		if (r != RecusaDeDuelo.Pode) { Avisar(pl, MotivoDaRecusa(r, agora)); return; }

		_duelo.EmDuelo = true;                 // `god_duel_busy = 1`
		_desafianteEsperando = pl.Conta;
		_prazoDaResposta = agora + PrazoDeResposta;

		Duelo.RolarSemana(_duelo, agora);
		Avisar(pl, "seu desafio foi entregue. Aguarde a resposta do Deus...");
		Avisar(deus!, $"{pl.Name} te desafia FORMALMENTE pelo titulo de {Cargos.Get(CargoEmDisputa)?.Nome}! "
					+ $"Aceite agora ou adie (adiamentos usados: {_duelo.Adiamentos}/{Duelo.AdiamentosPermitidos} "
					+ $"-- o 3o na semana e covardia e custa o titulo). Voce tem "
					+ $"{PrazoDeResposta / 1000} segundos, e nao responder CONTA COMO ADIAR.");
	}

	private string MotivoDaRecusa(RecusaDeDuelo r, long agora) => r switch
	{
		RecusaDeDuelo.SemDono => "nao ha God of Destruction para desafiar.",
		RecusaDeDuelo.VoceEhODono => "voce E o God of Destruction.",
		RecusaDeDuelo.Caido => "nao se desafia um deus do chao.",
		RecusaDeDuelo.SemGodKi => "so quem despertou o God Ki pode desafiar um Deus.",
		RecusaDeDuelo.DueloEmCurso => "um duelo pelo titulo ja esta acontecendo.",
		RecusaDeDuelo.Carencia =>
			$"o novo Deus esta em periodo de carencia. Desafios abrem em {Duelo.TempoQueFalta(_duelo.TituloDesde + Duelo.Carencia, agora)}.",
		RecusaDeDuelo.IntervaloFechado =>
			$"o trono so aceita desafios a cada 2 dias. Proximo em {Duelo.TempoQueFalta(_duelo.UltimoDuelo + Duelo.Intervalo, agora)}.",
		RecusaDeDuelo.DonoAusente => "o Deus esta ausente do plano fisico. Tente quando ele estiver presente.",
		_ => "nao da.",
	};

	/// <summary>A resposta do Deus: aceitar agora, ou adiar (`:456-473`).</summary>
	private void VerboResponderDesafio(ServerPlayer pl, bool aceitou)
	{
		if (_desafianteEsperando.Length == 0
			|| !string.Equals(ContaDoDeus, pl.Conta, StringComparison.OrdinalIgnoreCase))
		{
			Avisar(pl, "ninguem esta te desafiando pelo titulo.");
			return;
		}

		ServerPlayer? desafiante = OnlinePorConta(_desafianteEsperando);
		if (desafiante == null)
		{
			// O DESAFIANTE SUMIU no meio da pergunta -- o `if(!god_is_god(G) || usr.dead)` do DM
			// (`:457`), que tambem desfaz tudo sem cobrar adiamento de ninguem.
			_desafianteEsperando = "";
			_duelo.EmDuelo = false;
			Avisar(pl, "o desafiante deixou o plano fisico. O desafio se desfaz.");
			return;
		}

		if (aceitou) { ComecarDuelo(pl, desafiante); return; }
		Adiar(pl, desafiante.Name);
	}

	/// <summary>
	/// ADIAR -- e a covardia. Um lugar so porque o adiamento tem DUAS entradas: o verb e o prazo de
	/// resposta estourado (ver o item 1 do cabecalho).
	/// </summary>
	private void Adiar(ServerPlayer deus, string nomeDoDesafiante)
	{
		long agora = NowMs();
		_desafianteEsperando = "";
		_duelo.EmDuelo = false;

		bool covardia = Duelo.Adiar(_duelo, agora);
		SalvarTitulo();

		if (covardia)
		{
			// `god_lose_title("covardia: 3o adiamento na semana")` (`:465`). O `Destronar` ja anuncia
			// ao mundo, tira o kit e limpa o estado do titulo.
			Destronar(CargoEmDisputa, "covardia: 3o adiamento de desafio na mesma semana");
			return;
		}

		foreach (ServerPlayer o in _players.Values)
			Mandar(o, Protocol.Fala.Sistema, "",
				$"O GOD OF DESTRUCTION adiou o desafio de {nomeDoDesafiante}. "
				+ $"({_duelo.Adiamentos}/{Duelo.AdiamentosPermitidos} adiamentos na semana)", falante: null);
		Avisar(deus, $"voce adiou. Mais {Duelo.AdiamentosPermitidos - _duelo.Adiamentos + 1} adiamento(s) "
				   + "nesta semana e o titulo e seu passado.");
	}

	// =====================================================================
	// 2. O DUELO
	// =====================================================================

	/// <summary>Quatro tiles entre os dois -- o `-4`/`+4` da arena do DM (`:480-481`).</summary>
	private const float PassoDoDuelo = 4 * ZoneCollision.TileSize;

	private void ComecarDuelo(ServerPlayer deus, ServerPlayer desafiante)
	{
		long agora = NowMs();
		_desafianteEsperando = "";
		_dueloDono = deus.Conta;
		_dueloDesafiante = desafiante.Conta;
		_dueloComeca = agora + Duelo.Contagem;
		_dueloAcaba = _dueloComeca + Duelo.Prazo;
		_duelo.EmDuelo = true;

		// OS DOIS INTEIROS, como o `trn_heal` dos dois lados (`:482-483`): um duelo pelo titulo do
		// mundo nao se decide por quem chegou machucado da luta anterior.
		//
		// O `Restaurar` traz junto os 6 s de carencia de toda ressurreicao deste jogo, e a contagem
		// e de 5 -- entao o primeiro segundo de luta e intocavel PROS DOIS. E simetrico e some
		// sozinho; tirar a carencia daqui exigiria um caminho de cura paralelo ao de producao, que e
		// exatamente o que a regra da casa proibe numa bancada e desaconselha aqui.
		Restaurar(deus);
		Restaurar(desafiante);

		// O DESAFIANTE VAI ATE O DEUS -- ver o item 2 do cabecalho (este port nao tem a arena).
		MoveToZone(desafiante.Id, deus.Zone, new Vec2(deus.Pos.X + PassoDoDuelo, deus.Pos.Y));

		foreach (ServerPlayer o in _players.Values)
			Mandar(o, Protocol.Fala.Sistema, "",
				$"DUELO PELO TITULO DE GOD OF DESTRUCTION: {deus.Name} (Deus) VS {desafiante.Name} "
				+ "(desafiante)! Sem fusao; apenas armas e roupas. Nocaute decide!", falante: null);

		Avisar(deus, "cinco...");
		Avisar(desafiante, "cinco...");
	}

	/// <summary>
	/// UM PASSO DO DUELO E DA TAREFA -- 1 Hz, chamado pelo <see cref="TickDosCargos"/>.
	///
	/// A DECISAO NAO MORA AQUI: quem diz quem venceu e o <see cref="Duelo.Passo"/>, que e puro e
	/// tem bancada. Este metodo so responde as perguntas que o Core nao pode fazer (quem esta
	/// online, quem esta caido, quanta vida tem).
	/// </summary>
	private void TickDoDuelo()
	{
		long agora = NowMs();

		// (0) O TITULO APARECEU POR OUTRA PORTA (outorga de admin, e amanha uma quest) e o relogio
		// dele esta zerado. Ligar o relogio aqui, em vez de dentro do `Outorgar`, e a mesma escolha
		// da reconciliacao da dadiva: quem escrever a proxima porta nao vai lembrar de acender a
		// carencia -- e um Deus coroado sem carencia pode ser desafiado no minuto seguinte.
		if (ContaDoDeus.Length > 0 && _duelo.TituloDesde == 0)
		{
			_duelo.TituloDesde = agora;
			_duelo.SemanaDosAdiamentos = agora;
			_tarefaProxima = agora + IntervaloDeTarefa;
			SalvarTitulo();
		}

		// (a) DESAFIO SEM RESPOSTA = ADIAMENTO. Ver o item 1 do cabecalho.
		if (_desafianteEsperando.Length > 0 && agora > _prazoDaResposta)
		{
			ServerPlayer? deus = OnlinePorConta(ContaDoDeus);
			string nome = OnlinePorConta(_desafianteEsperando)?.Name ?? "o desafiante";
			if (deus != null) { Adiar(deus, nome); }
			else { _desafianteEsperando = ""; _duelo.EmDuelo = false; }
		}

		// (b) O DUELO EM CURSO.
		if (_dueloDono.Length > 0) PassoDoDueloEmCurso(agora);

		// (c) AS TAREFAS DO DEUS.
		TickDaTarefaDoDeus(agora);
	}

	private void PassoDoDueloEmCurso(long agora)
	{
		ServerPlayer? deus = OnlinePorConta(_dueloDono);
		ServerPlayer? desafiante = OnlinePorConta(_dueloDesafiante);

		if (agora < _dueloComeca)
		{
			long faltam = (_dueloComeca - agora + 999) / 1000;
			if (faltam <= 4)
			{
				string txt = faltam <= 0 ? "COMECEM!" : $"{faltam}...";
				if (deus != null) Avisar(deus, txt);
				if (desafiante != null) Avisar(desafiante, txt);
			}
			return;
		}

		// O TITULO PODE TER TROCADO DE DONO NO MEIO (admin, destituicao): o duelo perde o objeto.
		if (!string.Equals(ContaDoDeus, _dueloDono, StringComparison.OrdinalIgnoreCase))
		{
			TerminarDuelo(FimDeDuelo.Dono, deus, desafiante, "o titulo mudou de mao antes do fim");
			return;
		}

		FimDeDuelo fim = Duelo.Passo(
			donoSumiu: deus == null || deus.Ficha.dead,
			desafianteSumiu: desafiante == null || desafiante.Ficha.dead,
			donoCaido: deus?.Ficha.KO == true,
			desafianteCaido: desafiante?.Ficha.KO == true,
			vidaDoDono: deus?.Ficha.HP ?? 0,
			vidaDoDesafiante: desafiante?.Ficha.HP ?? 0,
			prazoEstourou: agora >= _dueloAcaba);

		if (fim == FimDeDuelo.Continua) return;
		TerminarDuelo(fim, deus, desafiante, "");
	}

	private void TerminarDuelo(FimDeDuelo fim, ServerPlayer? deus, ServerPlayer? desafiante, string nota)
	{
		string nomeDoDesafiante = desafiante?.Name ?? "o desafiante";
		_dueloDono = _dueloDesafiante = "";
		_duelo.EmDuelo = false;
		_duelo.UltimoDuelo = NowMs();       // `god_duel_last = world.realtime` (`:505`)

		if (fim == FimDeDuelo.Desafiante && desafiante != null)
		{
			// A COROACAO PASSA PELO FUNIL DOS CARGOS, e nao por uma escrita direta no `_tronos`: e o
			// `Entronizar` que tira o kit do antigo, da o novo, grava e avisa o mundo.
			if (Cargos.Get(CargoEmDisputa) is { } r)
			{
				_duelo.TituloDesde = NowMs();     // a carencia de 7 dias comeca AGORA (`:85`)
				_duelo.Adiamentos = 0;
				_duelo.SemanaDosAdiamentos = NowMs();
				_tarefaAlvo = _tarefaNomeDoAlvo = "";
				_tarefaPlanetaChave = _tarefaPlanetaNome = "";
				_tarefaFalhas = 0;
				_tarefaProxima = NowMs() + IntervaloDeTarefa;
				Entronizar(r, desafiante, $"{desafiante.Name} DERROTOU o God of Destruction em duelo formal!!");
			}
		}
		else
		{
			foreach (ServerPlayer o in _players.Values)
				Mandar(o, Protocol.Fala.Sistema, "",
					$"O GOD OF DESTRUCTION defendeu seu titulo contra {nomeDoDesafiante}."
					+ (nota.Length > 0 ? $" ({nota})" : ""), falante: null);
		}

		SalvarTitulo();

		// POS-DUELO: os dois curados e devolvidos ao proprio berco (`:517-518`, `GotoPlanet`).
		foreach (ServerPlayer? p in new[] { deus, desafiante })
		{
			if (p == null) continue;
			Restaurar(p);
			MandarProBerco(p);
		}
	}

	// =====================================================================
	// 3. AS TAREFAS DO DEUS -- e a destituicao por negligencia
	// =====================================================================

	private void TickDaTarefaDoDeus(long agora)
	{
		if (ContaDoDeus.Length == 0) return;

		// SEM TAREFA: sorteia quando der a hora. TER tarefa e ter UM dos dois alvos preenchido.
		if (_tarefaAlvo.Length == 0 && _tarefaPlanetaChave.Length == 0)
		{
			if (agora >= _tarefaProxima) SortearTarefaDoDeus(agora);
			return;
		}

		// ---- TAREFA DE MUNDO (`:604-614`): a pergunta e uma so, e o livro dos mortos responde. ----
		if (_tarefaPlanetaChave.Length > 0)
		{
			if (ChaveDePlaneta.Ler(_tarefaPlanetaChave, _tarefaPlanetaNome, out ChaveDePlaneta ch)
				&& PlanetaMorto(ch))
			{
				TarefaCumprida();
				return;
			}
			if (agora >= _tarefaPrazo) FalharTarefaDoDeus(agora);
			return;
		}

		// ---- TAREFA DE AMEACA. A pergunta e a do `god_task_loop` (`:565-577`). ----
		ServerPlayer? alvo = OnlinePorConta(_tarefaAlvo);
		if (alvo == null)
		{
			// "a ameaca fugiu do plano fisico": a tarefa se anula SEM PUNIR. E a unica saida justa --
			// o Deus nao pode ser destituido porque o vilao deslogou.
			_tarefaAlvo = _tarefaNomeDoAlvo = "";
			_tarefaProxima = agora + EsperaSemAlvo;
			if (OnlinePorConta(ContaDoDeus) is { } g)
				Avisar(g, "GoD: a ameaca fugiu do plano fisico. Nova tarefa em breve.");
			SalvarTitulo();
			return;
		}

		if (alvo.Ficha.dead) { TarefaCumprida(); return; }
		if (agora < _tarefaPrazo) return;
		FalharTarefaDoDeus(agora);
	}

	/// <summary>
	/// PRAZO ESTOURADO (`:580-589`) -- **um lugar so**, porque agora ha DUAS tarefas que vencem
	/// (a ameaca e o mundo) e a punicao e a mesma. Duplicar a contagem seria duplicar a destituicao.
	/// </summary>
	private void FalharTarefaDoDeus(long agora)
	{
		_tarefaFalhas++;
		_tarefaAlvo = _tarefaNomeDoAlvo = "";
		_tarefaPlanetaChave = _tarefaPlanetaNome = "";
		_tarefaProxima = agora + IntervaloDeTarefa;
		SalvarTitulo();

		if (OnlinePorConta(ContaDoDeus) is { } deus)
			Avisar(deus, $"GoD: TAREFA FALHADA. Falhas: {_tarefaFalhas}/{FalhasQueDestituem}.");
		foreach (ServerPlayer o in _players.Values)
			Mandar(o, Protocol.Fala.Sistema, "",
				$"O God of Destruction FALHOU em manter o equilibrio... ({_tarefaFalhas}/{FalhasQueDestituem})",
				falante: null);

		if (_tarefaFalhas >= FalhasQueDestituem)
			Destronar(CargoEmDisputa, $"negligencia: {FalhasQueDestituem} tarefas falhadas");
	}

	/// <summary>
	/// `god_task_new` (`:591`) -- **agora com os DOIS ramos**, na ordem do original.
	///
	/// O CANDIDATO E O `isVillain` **ou** karma <= -50, exatamente como la (`:597`): sao duas fontes
	/// porque uma e designacao de admin e a outra e reputacao ganha matando -- e o Deus da Destruicao
	/// existe pra desequilibrio, venha ele de onde vier.
	///
	/// ============================ O SEGUNDO RAMO DEIXOU DE SER IMPOSSIVEL ============================
	/// O cabecalho deste arquivo dizia que a tarefa de DESTRUIR UM MUNDO ficava de fora porque o DM
	/// sorteia entre os `pspace_planets` -- um registro de mundos procedurais vivos que este port nao
	/// tem, ja que o universo e funcao pura da seed. O impedimento CADUCOU com os deveres de cargo: a
	/// tarefa do Lorde do Gelo precisou resolver a mesma pergunta e resolveu (o SETOR do portador na
	/// carta estelar -- ver `SortearMundoParaDestruir`). Reusar dali e uma linha; escrever um segundo
	/// sorteio seria duas fontes pra mesma verdade.
	///
	/// A ORDEM E A DO DM: o planeta so entra quando NAO ha ameaca online (`:592-602`) -- o Deus e
	/// primeiro um juiz de pessoas, e so na falta delas um demolidor de mundos.
	/// ==========================================================================================
	/// </summary>
	private void SortearTarefaDoDeus(long agora)
	{
		string deusConta = ContaDoDeus;
		var vilaes = _players.Values.Where(p =>
			p.Peer != null && EhPessoa(p) && !p.Ficha.dead
			&& !string.Equals(p.Conta, deusConta, StringComparison.OrdinalIgnoreCase)
			&& (EhVilao(p) || p.Karma <= KarmaDeAmeaca)).ToList();

		ServerPlayer? deus = OnlinePorConta(deusConta);

		if (vilaes.Count == 0)
		{
			// SEM AMEACA: o mundo. Sem setor tambem (o Deus esta no Inferno, no Paraiso ou numa nave),
			// e ai o adiamento de 10 min do `else` original vale pros dois ramos.
			if (deus == null || SortearMundoParaDestruir(deus) is not { } mundo)
			{
				_tarefaProxima = agora + EsperaSemAlvo;
				return;
			}

			_tarefaPlanetaChave = ChaveDePlaneta.De(mundo).Texto;
			_tarefaPlanetaNome = mundo.Nome;
			_tarefaPrazo = agora + IntervaloDeTarefa;
			SalvarTitulo();

			Avisar(deus, $"GoD: NOVA TAREFA -- DESTRUIR o mundo {mundo.Nome}. "
					   + $"Prazo: {Duelo.TempoQueFalta(_tarefaPrazo, agora)}. Falhar corroi o trono.");
			return;
		}

		ServerPlayer v = vilaes[Random.Shared.Next(vilaes.Count)];
		_tarefaAlvo = v.Conta;
		_tarefaNomeDoAlvo = v.Name;
		_tarefaPrazo = agora + IntervaloDeTarefa;
		SalvarTitulo();

		if (deus != null)
			Avisar(deus, $"GoD: NOVA TAREFA -- ELIMINAR a ameaca {v.Name}. "
					   + $"Prazo: {Duelo.TempoQueFalta(_tarefaPrazo, agora)}. Falhar corroi o trono.");
	}

	/// <summary>`god_task_complete` (`:620`) -- e o bom servico ABATE uma falha antiga (`:625`).</summary>
	private void TarefaCumprida()
	{
		bool eraMundo = _tarefaPlanetaChave.Length > 0;
		string quem = eraMundo ? _tarefaPlanetaNome : _tarefaNomeDoAlvo;
		_tarefaAlvo = _tarefaNomeDoAlvo = "";
		_tarefaPlanetaChave = _tarefaPlanetaNome = "";
		_tarefaFalhas = Math.Max(_tarefaFalhas - 1, 0);
		_tarefaProxima = NowMs() + IntervaloDeTarefa;
		SalvarTitulo();

		if (OnlinePorConta(ContaDoDeus) is { } deus)
			Avisar(deus, $"GoD: TAREFA CUMPRIDA ({quem}). O equilibrio agradece.");
		foreach (ServerPlayer o in _players.Values)
			Mandar(o, Protocol.Fala.Sistema, "",
				"O God of Destruction manteve o equilibrio do universo. "
				+ (eraMundo ? $"(o mundo {quem} foi apagado da carta)" : $"(a ameaca {quem} foi eliminada)"),
				falante: null);
	}

	// =====================================================================
	// 4. O PAINEL -- `GoD_Status` (`:528`)
	// =====================================================================

	private void VerboStatusDoTitulo(ServerPlayer pl)
	{
		long agora = NowMs();
		string dono = ContaDoDeus;
		Avisar(pl, $"-- GOD OF DESTRUCTION: {(dono.Length > 0 ? NomeDoDono(dono) : "trono VAGO")} --");
		if (dono.Length == 0)
		{
			Avisar(pl, "com o trono vago, o titulo se ganha por outorga -- e se defende em duelo.");
			return;
		}

		if (_tarefaAlvo.Length > 0)
			Avisar(pl, $"tarefa: ELIMINAR a ameaca {_tarefaNomeDoAlvo} (prazo: {Duelo.TempoQueFalta(_tarefaPrazo, agora)})");
		else if (_tarefaPlanetaChave.Length > 0)
			Avisar(pl, $"tarefa: DESTRUIR o mundo {_tarefaPlanetaNome} (prazo: {Duelo.TempoQueFalta(_tarefaPrazo, agora)})");
		else
			Avisar(pl, "tarefa: nenhuma no momento.");
		Avisar(pl, $"falhas acumuladas: {_tarefaFalhas}/{FalhasQueDestituem}");
		Avisar(pl, $"adiamentos na semana: {_duelo.Adiamentos}/{Duelo.AdiamentosPermitidos}");

		if (agora - _duelo.TituloDesde < Duelo.Carencia)
			Avisar(pl, $"carencia de desafios: {Duelo.TempoQueFalta(_duelo.TituloDesde + Duelo.Carencia, agora)}");
		else if (agora - _duelo.UltimoDuelo < Duelo.Intervalo)
			Avisar(pl, $"proximo desafio possivel em: {Duelo.TempoQueFalta(_duelo.UltimoDuelo + Duelo.Intervalo, agora)}");
		else
			Avisar(pl, "o trono ACEITA desafios agora (exige God Ki desperto).");
	}
}
