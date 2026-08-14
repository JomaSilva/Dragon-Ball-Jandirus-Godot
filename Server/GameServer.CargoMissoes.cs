using System.Text.Json;
using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O MOTOR DE DEVERES DOS CARGOS -- as tarefas com prazo, a recompensa, o renome e a destituicao.
///
/// Porte de `Modules/Ranks/RankQuests.dm:417-696` (o `rq_loop` e tudo o que ele chama). As regras
/// puras -- vocacao, numeros, quem serve de alvo, o texto do prazo -- moram no
/// `Core/Ranks/MissoesDeCargo.cs`; aqui mora quem esta online, quem esta perto, quem domina o
/// planeta, quem morreu e quem grava.
///
/// ============================ O QUE ESTE ARQUIVO FECHA ============================
/// O `RankDef.TemDeveres` estava marcado nos 17 cargos certos e **nao tinha um unico leitor**. Um
/// cargo se ganhava e depois nao acontecia mais nada: nem cobranca, nem recompensa, nem risco. Era o
/// terceiro campo da tabela de cargos a viver assim (`Concede` foi o primeiro, as tres portas foram
/// o segundo), e o padrao ja tem nome nesta casa -- regra escrita nao e regra ligada.
///
/// E ele fecha as DUAS pendencias de RENOME que o `Ranks.cs` carregava escritas no Grand Kai e no
/// Kaioshin: *"3 tarefas cumpridas no cargo atual -- o port nao tem o motor de tarefas"*. Agora tem.
/// ================================================================================
///
/// ============================ TRES DECISOES QUE NAO SAO DO DM ============================
/// 1. **O RELOGIO E O DO MUNDO (`TempoDoMundo`), E O PRAZO E EM DIAS IN-GAME.** O DM ja mede em dias
///    in-game (o comentario do `RQ_TASK_DAYS` e explicito), mas com o `DAY_REAL_MINUTES` dele. Aqui o
///    dia in-game e o do CEU deste port (`Espaco.SegundosPorDiaInGame`, 24 min), entao 3 dias sao
///    ~72 min reais. Ver `MissoesDeCargo.SegundosDePrazo`.
///
/// 2. **A FICHA SE ZERA SOZINHA NA TROCA DE DONO.** O DM apaga `rq_state[key]` na mao em quatro
///    lugares; aqui a ficha guarda de quem ela e e o tique compara com o trono. Ver
///    <see cref="FichaDeMissao.Dono"/> -- e o mesmo argumento da `ReconciliarDadiva`.
///
/// 3. **NAO HA CAIXA DE CORREIO OFFLINE.** O DM usa o `conq_notify_owner` porque la a tarefa pode ser
///    cumprida ou falhada com o dono fora do ar. Aqui **nao pode**: o relogio do cargo CONGELA quando
///    o portador sai (e isso e do DM, `:644-651`), entao nada e atribuido, cumprido nem cobrado na
///    ausencia dele. Um correio que nunca receberia carta seria API orfa.
/// ======================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>chave do cargo -> a ficha de deveres dele. Sem entrada = trono vago ou ficha nova.</summary>
	private readonly Dictionary<string, FichaDeMissao> _missoes = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// O COFRE DA TERRA -- o `EarthBank` do DM, onde a verba do Presidente cai (`:693`).
	///
	/// ============================ ELE E UM LIVRO-CAIXA, E NAO UMA BOLSA ============================
	/// No original o `EarthBank` e a conta pra onde os impostos da Terra vao e de onde o Presidente
	/// tira. Este port **nao tem o sistema de impostos** (a skill `/datum/skill/rank/Taxes` que os
	/// cargos concedem e um verb ainda mudo -- o relatorio da dadiva ja conta isso), entao o cofre
	/// aqui so RECEBE.
	///
	/// Ele existe assim mesmo porque a tarefa do Presidente e o dinheiro SAIR do bolso dele: essa
	/// metade e real e e o custo de manter o cargo. E o numero tem leitor -- o painel do cargo o
	/// imprime --, entao nao e a API orfa que este projeto ja pagou uma vez. Quando os impostos
	/// existirem, e daqui que eles sacam.
	/// ========================================================================================
	/// </summary>
	private double _cofreDaTerra;

	/// <summary>
	/// A CADENCIA DO `rq_loop`: uma volta por MINUTO (`sleep(600)`, `:632`), e nao por segundo.
	///
	/// Ela importa e nao e enfeite: o servico do cargo pontua **por minuto** com teto de 3 visitantes
	/// (`RQ_SERVICE_CAP`). Rodando a 1 Hz, um mestre com tres alunos fecharia a meta de 15 em cinco
	/// segundos -- o teto viraria teto nenhum, que e o corolario que esta casa ja escreveu.
	/// </summary>
	private double _proximaVoltaDasMissoes;

	private string CaminhoDasMissoes =>
		System.IO.Path.Combine(_store?.Pasta ?? ".", "missoes-de-cargo.json");

	/// <summary>O que vai pro disco. Um registro so, como o livro da conquista.</summary>
	private sealed class LivroDeMissoes
	{
		public Dictionary<string, FichaDeMissao> Fichas = [];
		public double CofreDaTerra;
	}

	// =====================================================================
	// PERSISTENCIA
	// =====================================================================
	private void CarregarMissoes()
	{
		_missoes.Clear();
		_cofreDaTerra = 0;

		try
		{
			if (System.IO.File.Exists(CaminhoDasMissoes))
			{
				LivroDeMissoes? l = JsonSerializer.Deserialize<LivroDeMissoes>(
					System.IO.File.ReadAllText(CaminhoDasMissoes),
					new JsonSerializerOptions { IncludeFields = true });

				if (l != null)
				{
					foreach ((string k, FichaDeMissao f) in l.Fichas)
					{
						// FICHA DE CARGO QUE NAO EXISTE MAIS (ou que perdeu os deveres) E LIXO, e ela
						// GRITA em vez de dormir: e save de outra versao da tabela.
						if (Cargos.Get(k) is not { TemDeveres: true })
						{
							GD.PushWarning($"[server] missoes: ficha de '{k}', que nao e cargo com deveres -- descartada");
							continue;
						}
						_missoes[k] = f;
					}
					_cofreDaTerra = l.CofreDaTerra;
				}
			}
		}
		catch (Exception e) { GD.PushWarning($"[server] missoes-de-cargo.json ilegivel: {e.Message}"); }

		PerdoarPrazosNoBoot();

		GD.Print($"[server] deveres de cargo: {_missoes.Count} ficha(s) de {MissoesDeCargo.ComDeveres.Count()} "
			   + $"cargos com dever | cofre da Terra: {_cofreDaTerra:N0} zeni");
	}

	private void SalvarMissoes()
	{
		if (_store == null) return;
		try
		{
			var l = new LivroDeMissoes { CofreDaTerra = _cofreDaTerra };
			foreach ((string k, FichaDeMissao f) in _missoes) l.Fichas[k] = f;

			// TEMPORARIO E RENOMEIA, como o `conquista.json` e o `planetas-mortos.json`.
			string tmp = CaminhoDasMissoes + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(l,
				new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDasMissoes, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra salvar missoes-de-cargo.json: {e.Message}"); }
	}

	/// <summary>
	/// O PERDAO DE BOOT -- `rq_boot_forgive` (`:437`), e o motivo dele e o mesmo aqui.
	///
	/// O prazo mora no <see cref="TempoDoMundo"/>, que **anda com o servidor fora do ar** (e por isso
	/// que ele foi escolhido: e ele que faz "tres dias" significar tres dias). Com o prazo curto
	/// (~72 min reais), qualquer manutencao maior que isso deixaria TODA tarefa em voo vencida no
	/// primeiro tique pos-boot: falha de graca, anuncio ao mundo e um passo rumo a destituicao sem
	/// ninguem ter jogado.
	///
	/// Prazo que vence com o mundo DE PE nao passa por aqui -- continua caindo normal no tique.
	/// </summary>
	private void PerdoarPrazosNoBoot()
	{
		double agora = TempoDoMundo;
		bool mexeu = false;

		foreach (FichaDeMissao f in _missoes.Values)
		{
			if (MissoesDeCargo.ApararJanela(f, agora)) mexeu = true;
			if (f.Tarefa == TipoDeTarefa.Nenhuma) continue;

			// Ainda sobra prazo (um minuto de folga, como o `+ 600` do DM): nao mexe.
			if (f.Prazo > agora + 60) continue;
			f.Prazo = agora + MissoesDeCargo.SegundosDePrazo;
			mexeu = true;
		}

		if (mexeu) SalvarMissoes();
	}

	// =====================================================================
	// O TIQUE -- uma volta por minuto do relogio do mundo
	// =====================================================================
	/// <summary>
	/// UM PASSO DE TODOS OS DEVERES. Chamado do <see cref="TickDosCargos"/> (1 Hz) e gastando uma
	/// volta por MINUTO -- ver <see cref="_proximaVoltaDasMissoes"/>.
	///
	/// O RELOGIO PODE RECUAR (a bancada do ceu desfaz o adianto): reancora sem cobrar nada, como o
	/// `TickDaConquista` e o `ContarOsDias` das sagas, e pela mesma razao -- nada do que passou se
	/// desfaz.
	/// </summary>
	private void TickDasMissoes()
	{
		double agora = TempoDoMundo;
		if (_proximaVoltaDasMissoes <= 0 || agora < _proximaVoltaDasMissoes - MissoesDeCargo.SegundosDoLaco)
		{
			_proximaVoltaDasMissoes = agora + MissoesDeCargo.SegundosDoLaco;
			return;
		}
		if (agora < _proximaVoltaDasMissoes) return;
		_proximaVoltaDasMissoes = agora + MissoesDeCargo.SegundosDoLaco;

		bool mexeu = false;
		foreach (RankDef r in MissoesDeCargo.ComDeveres.ToList())
		{
			string dono = _tronos.TryGetValue(r.Chave, out string? d) ? d : "";

			// TRONO VAGO: a ficha morre com ele (`if(rq_state[key]) rq_state -= key`, `:636`).
			if (dono.Length == 0)
			{
				if (_missoes.Remove(r.Chave)) mexeu = true;
				continue;
			}

			// A FICHA E DO TRONO, MAS ELA ZERA QUANDO O TRONO TROCA DE DONO. Ver `FichaDeMissao.Dono`.
			if (!_missoes.TryGetValue(r.Chave, out FichaDeMissao? f)
				|| !string.Equals(f.Dono, dono, StringComparison.OrdinalIgnoreCase))
			{
				f = new FichaDeMissao { Dono = dono, Proxima = agora + MissoesDeCargo.SegundosDeIntervalo };
				_missoes[r.Chave] = f;
				mexeu = true;
			}

			if (MissoesDeCargo.ApararJanela(f, agora)) mexeu = true;

			// ============================ PORTADOR OFFLINE: RELOGIO CONGELADO ============================
			// `:644-651`, e o DM explica por que: 11 dos 17 cargos so pontuam com ele no ar, entao correr
			// prazo na ausencia seria tres falhas em poucas horas e o cargo perdido dormindo. Nada e
			// atribuido nem cobrado, e o prazo renasce inteiro quando ele voltar.
			// ==========================================================================================
			ServerPlayer? portador = OnlinePorConta(dono);
			if (portador == null)
			{
				f.Proxima = agora + MissoesDeCargo.SegundosDoLaco;
				if (f.Tarefa != TipoDeTarefa.Nenhuma) f.Prazo = agora + MissoesDeCargo.SegundosDePrazo;
				continue;
			}

			if (f.Tarefa == TipoDeTarefa.Nenhuma)
			{
				if (agora >= f.Proxima && AtribuirTarefa(r, f, portador, agora)) mexeu = true;
				continue;
			}

			TipoDeTarefa antes = f.Tarefa;
			if (f.Tarefa == TipoDeTarefa.Servico) { PontuarServico(r, f, portador); mexeu = true; }

			if (TarefaCumprida(r, f, portador, agora)) { CumprirTarefa(r, f, portador, agora); mexeu = true; continue; }

			// ============================ A GUARDA DO `st["task"]`, E ELA NAO E ENFEITE ============================
			// O `TarefaCumprida` pode ter ANULADO a tarefa no caminho (o alvo saiu do mundo), e o prazo
			// dela quase sempre ja passou. Sem esta guarda -- que e o `if(st["task"] && ...)` do DM
			// (`:659`) --, a anulacao "sem punir" cobraria a falha no mesmo tique, que e o oposto exato
			// do que ela existe pra fazer.
			// ==================================================================================================
			if (f.Tarefa != TipoDeTarefa.Nenhuma && agora > f.Prazo)
			{
				FalharTarefa(r, f, portador, agora);
				mexeu = true;
				continue;
			}

			if (f.Tarefa != antes) mexeu = true;   // a anulacao tambem tem que chegar ao disco
		}

		if (mexeu) SalvarMissoes();
	}

	// =====================================================================
	// 1. ATRIBUIR -- `rq_assign` (`:486`)
	// =====================================================================
	/// <summary>
	/// Sorteia a tarefa deste cargo. Devolve `false` quando nada foi escrito em disco (so o adiamento
	/// por falta de alvo, que e o `st["next"] = ... RQ_TASK_RETRY` do original).
	/// </summary>
	private bool AtribuirTarefa(RankDef r, FichaDeMissao f, ServerPlayer portador, double agora)
	{
		// O PLANETA DO CARGO ESTA SOB BANDEIRA ALHEIA? A pergunta e por ASSINATURA porque dominio e do
		// personagem (ver `Dominio.Assinatura`), enquanto cargo e da conta -- os dois so se encontram
		// aqui, no corpo que esta online.
		string meuPlaneta = MissoesDeCargo.PlanetaDoCargo(r.Chave);
		bool dominado = false;
		if (meuPlaneta.Length > 0 && DominioDe(new ChaveDePlaneta(true, meuPlaneta, 0)) is { } dom)
			dominado = !string.Equals(dom.Assinatura, portador.Assinatura, StringComparison.Ordinal);

		TipoDeTarefa tipo = MissoesDeCargo.Escolher(r, dominado);

		var nova = new FichaDeMissao
		{
			Dono = f.Dono,
			Falhas = f.Falhas,
			Cumpridas = f.Cumpridas,
			AvisouAscensao = f.AvisouAscensao,
			Tarefa = tipo,
			Prazo = agora + MissoesDeCargo.SegundosDePrazo,
			Proxima = f.Proxima,
		};

		switch (tipo)
		{
			case TipoDeTarefa.Servico:
				nova.Meta = MissoesDeCargo.MetaDeServico(r.Chave);
				break;

			case TipoDeTarefa.Libertar:
				nova.Planeta = meuPlaneta;
				nova.PlanetaChave = new ChaveDePlaneta(true, meuPlaneta, 0).Texto;
				break;

			case TipoDeTarefa.Verba:
				nova.Meta = 1;
				break;

			case TipoDeTarefa.Planeta:
				if (SortearMundoParaDestruir(portador) is not { } alvoMundo)
				{
					f.Proxima = agora + MissoesDeCargo.SegundosSemAlvo;
					return false;
				}
				nova.Planeta = alvoMundo.Nome;
				nova.PlanetaChave = ChaveDePlaneta.De(alvoMundo).Texto;
				break;

			default:   // Vilao / Heroi
				if (SortearAlvoDeCaca(r, portador) is not { } presa)
				{
					f.Proxima = agora + MissoesDeCargo.SegundosSemAlvo;
					return false;
				}
				nova.AlvoConta = presa.Conta;
				nova.AlvoNome = presa.Name;
				break;
		}

		_missoes[r.Chave] = nova;
		Avisar(portador, $"{r.Nome}: NOVA TAREFA -- {MissoesDeCargo.Descricao(nova)}. "
					   + $"Prazo: {MissoesDeCargo.TextoDeTempo(MissoesDeCargo.SegundosDePrazo)}. "
					   + $"Falhar corrói o trono ({nova.Falhas}/{MissoesDeCargo.FalhasQueDestituem}).");
		return true;
	}

	/// <summary>
	/// A PRESA -- `:514-526`. Protetores cacam vilao; lordes malignos cacam HEROI.
	///
	/// Nao entra quem carrega o proprio cargo (o `P.signature == sig` do DM), nem NPC: o alvo tem que
	/// ser alguem que possa ser cacado de volta.
	///
	/// O CRIVO E O `EhPessoa`, e nao o `Peer != null`: e o MESMO que o <see cref="OnlinePorConta"/>
	/// usa pra responder "o portador esta no mundo?", e ter dois criterios de presenca no mesmo
	/// sistema e como o port ja se meteu em confusao antes (ora `key`, ora `signature`, ora `name`).
	/// Clone de meditacao e NPC nao tem conta, entao nao tem assinatura, entao nao entram.
	/// </summary>
	private ServerPlayer? SortearAlvoDeCaca(RankDef r, ServerPlayer portador)
	{
		var alvos = _players.Values.Where(p =>
			EhPessoa(p) && !p.Ficha.dead
			&& !string.Equals(p.Conta, portador.Conta, StringComparison.OrdinalIgnoreCase)
			&& MissoesDeCargo.ServeDeAlvo(r.Maligno, EhVilao(p), p.Karma)).ToList();

		return alvos.Count == 0 ? null : alvos[Random.Shared.Next(alvos.Count)];
	}

	/// <summary>
	/// O MUNDO A DESTRUIR -- a tarefa do Lorde do Gelo (`:503-513`), e a divergencia esta aqui.
	///
	/// ============================ O DM SORTEIA NUM REGISTRO QUE ESTE PORT NAO TEM ============================
	/// La a escolha sai de `pspace_planets` -- o registro dos mundos procedurais **ja gerados**, que
	/// no BYOND e o conjunto do que alguem visitou. Aqui o universo e funcao PURA da seed (regra 0.2)
	/// e nao existe registro de mundos vivos: o unico livro que existe e o dos MORTOS.
	///
	/// O substituto e o SETOR DO PROPRIO LORDE: os planetas dos sistemas que a carta estelar alcanca
	/// de onde ele esta (`Sistemas.PorPerto`, o mesmo recorte 3x3 que o `EnderecoDoCorpo` da conquista
	/// ja usa). Nao e um consolo -- e mais perto do original do que uma lista global seria, porque o
	/// `pspace_planets` do DM tambem so contem o que esta perto de alguem.
	///
	/// PRE-FEITO NAO ENTRA. A Terra, Namek e Vegeta tem mapa, povo e cargos pendurados neles; sortear
	/// a Terra como dever burocratico de um cargo seria o sistema de destruicao de planeta entrando
	/// pela porta dos fundos. No DM o `pspace_planets` tambem so tem procedurais.
	///
	/// SEM SETOR NAO HA TAREFA: quem esta no Inferno, no Paraiso ou dentro de uma nave nao tem onde a
	/// carta o coloque, e o sorteio e adiado em 10 min -- que e literalmente o `else` do original
	/// quando a lista de vivos sai vazia.
	/// ====================================================================================================
	/// </summary>
	private PlanetaNoEspaco? SortearMundoParaDestruir(ServerPlayer portador)
	{
		Vec2? onde = Espaco.EhEspaco(portador.Zone) ? portador.Pos : CorpoDaZona(portador.Zone)?.Pos;
		if (onde == null) return null;

		var sistemas = new List<SistemaSolar>();
		Sistemas.PorPerto(SeedDoUniverso, onde.Value, sistemas);

		var vivos = new List<PlanetaNoEspaco>();
		foreach (SistemaSolar s in sistemas)
			for (int k = 0; k < s.Orbitas; k++)
			{
				PlanetaNoEspaco p = s.Planeta(k);
				if (!p.Premade && !PlanetaMorto(p)) vivos.Add(p);
			}

		return vivos.Count == 0 ? null : vivos[Random.Shared.Next(vivos.Count)];
	}

	// =====================================================================
	// 2. O SERVICO -- `rq_service_tick` (`:560`)
	// =====================================================================
	/// <summary>
	/// PONTOS DE PRESENCA: ate 3 por minuto, com visitantes qualificados a 6 tiles do portador.
	///
	/// A DIFERENCA ENTRE AS DUAS VOCACOES DE SABEDORIA MORA NO CORE: os cargos do Outro Mundo atendem
	/// ALMAS (mortos), os mestres contam quem esta TREINANDO. Um mestre parado numa praca cheia nao
	/// pontua -- e essa e a regra que separa "servir" de "estar onde tem gente".
	/// </summary>
	private void PontuarServico(RankDef r, FichaDeMissao f, ServerPlayer portador)
	{
		if (!MissoesDeCargo.PodeServir(r.DoOutroMundo, portador.Ficha.dead)) return;

		float alcance = MissoesDeCargo.TilesDoServico * ZoneCollision.TileSize;
		int pts = 0;

		foreach (ServerPlayer o in ZoneList(portador.Zone.Hash))
		{
			if (o == portador || !EhPessoa(o)) continue;   // ver o crivo em `SortearAlvoDeCaca`
			if ((o.Pos - portador.Pos).Length > alcance) continue;
			if (!MissoesDeCargo.ContaComoServico(r.DoOutroMundo, o.Ficha.dead, o.Ficha.train)) continue;
			if (++pts >= MissoesDeCargo.TetoDoServico) break;
		}

		if (pts == 0) return;
		int antes = f.Progresso;
		f.Progresso += pts;

		// O AVISO A CADA CINCO PONTOS, como no DM (`:572`): um por minuto viraria ruido, e nenhum
		// deixaria o servico parecendo que nao esta contando.
		if (f.Progresso < f.Meta && antes / 5 != f.Progresso / 5)
			Avisar(portador, $"serviço do cargo: {Math.Min(f.Progresso, f.Meta)}/{f.Meta}.");
	}

	// =====================================================================
	// 3. CUMPRIU? -- `rq_check_done` (`:537`)
	// =====================================================================
	private bool TarefaCumprida(RankDef r, FichaDeMissao f, ServerPlayer portador, double agora)
	{
		switch (f.Tarefa)
		{
			case TipoDeTarefa.Libertar:
				// Sem dominio (`isnull(o)`) ou o dominio virou DELE: as duas contam (`:541`).
				Dominio? dom = DominioDe(new ChaveDePlaneta(true, f.Planeta, 0));
				return dom == null || string.Equals(dom.Assinatura, portador.Assinatura, StringComparison.Ordinal);

			case TipoDeTarefa.Vilao:
			case TipoDeTarefa.Heroi:
				ServerPlayer? alvo = OnlinePorConta(f.AlvoConta);
				if (alvo == null)
				{
					// O ALVO SUMIU DO JOGO: a tarefa se ANULA SEM PUNIR (`:544-548`). E a unica saida
					// justa -- o portador nao pode ser destituido porque o vilao deslogou.
					f.Tarefa = TipoDeTarefa.Nenhuma;
					f.AlvoConta = f.AlvoNome = "";
					f.Proxima = agora + MissoesDeCargo.SegundosSemAlvo;
					Avisar(portador, $"{r.Nome}: o alvo deixou o plano físico. A tarefa se anula sem punição.");
					return false;
				}
				return alvo.Ficha.dead;

			case TipoDeTarefa.Planeta:
				return ChaveDePlaneta.Ler(f.PlanetaChave, f.Planeta, out ChaveDePlaneta ch)
					&& PlanetaMorto(ch);

			case TipoDeTarefa.Verba:
				return f.Progresso >= 1;

			case TipoDeTarefa.Servico:
				return f.Progresso >= f.Meta;

			default:
				return false;
		}
	}

	/// <summary>Este mundo ja morreu? A pergunta pela CHAVE, pro alvo que so existe como identidade.</summary>
	private bool PlanetaMorto(ChaveDePlaneta c) => _mortos.Morto(c);

	// =====================================================================
	// 4. CUMPRIR E FALHAR -- `rq_complete` (`:574`) e `rq_fail` (`:603`)
	// =====================================================================
	/// <summary>
	/// A RECOMPENSA, E ELA TEM TRES PARTES. Zeni, karma (so pros que nao sao malignos -- *"cumprir NAO
	/// paga karma bom"*, `RQ_EVIL`) e o abate de UMA falha antiga: servir bem apaga negligencia, que e
	/// o que faz o contador de falhas ser uma corda e nao uma catraca.
	///
	/// E a quarta parte, que nao e recompensa e sim ESCADA: o RENOME. Tres tarefas cumpridas no cargo
	/// de agora destrancam a ascensao (`RQ_PROMO_QUESTS`), e e por isso que o `ReivindicarCargo`
	/// pergunta por ele.
	/// </summary>
	private void CumprirTarefa(RankDef r, FichaDeMissao f, ServerPlayer portador, double agora)
	{
		string oque = MissoesDeCargo.Descricao(f);

		f.Falhas = Math.Max(f.Falhas - 1, 0);
		f.Cumpridas++;
		f.Tarefa = TipoDeTarefa.Nenhuma;
		f.AlvoConta = f.AlvoNome = f.Planeta = f.PlanetaChave = "";
		f.Progresso = f.Meta = 0;
		f.Proxima = agora + MissoesDeCargo.SegundosDeIntervalo;

		portador.Ficha.Zeni += MissoesDeCargo.ZeniPorTarefa;
		if (!r.Maligno)
			portador.Karma = Math.Min(portador.Karma + MissoesDeCargo.KarmaPorTarefa, MissoesDeCargo.KarmaMaximo);

		Avisar(portador, $"TAREFA DE {r.Nome.ToUpperInvariant()} CUMPRIDA! ({oque}) "
					   + $"+{MissoesDeCargo.ZeniPorTarefa:N0} zeni"
					   + (r.Maligno ? "" : $", +{MissoesDeCargo.KarmaPorTarefa} karma")
					   + $". Falhas: {f.Falhas}/{MissoesDeCargo.FalhasQueDestituem}. {TextoDeRenome(r, f)}");
		Anunciar($"{r.Nome} cumpriu seu dever para com o universo.");

		// ============================ O AVISO DE ASCENSAO CAI QUANDO O DEGRAU FECHA ============================
		// `:586-590`. O latch nao e "ja avisei uma vez pra sempre": ele CAI enquanto nao houver degrau
		// vago, senao o unico aviso da tenencia seria gasto num trono ocupado e nada avisaria quando o
		// degrau certo vagasse.
		// ==================================================================================================
		if (!MissoesDeCargo.PodeAscender(f.Cumpridas)) return;
		bool haVaga = MissoesDeCargo.DegrausAcima(r.Chave).Any(
			a => !_tronos.TryGetValue(a.Chave, out string? dn) || dn.Length == 0);

		if (!haVaga) { f.AvisouAscensao = false; return; }
		if (f.AvisouAscensao) return;

		f.AvisouAscensao = true;
		Avisar(portador, $"seu renome como {r.Nome} correu o universo. O caminho para "
					   + $"{MissoesDeCargo.TextoDosDegraus(r.Chave)} está aberto -- reivindique o cargo.");
	}

	/// <summary>
	/// A FALHA, E O QUE ELA COBRA. Uma a mais no contador, a proxima tarefa em METADE do intervalo
	/// (`RQ_TASK_INTERVAL / 2`, `:607`: quem falhou nao ganha folga) e o mundo sabendo.
	///
	/// A TERCEIRA DESTITUI, e a destituicao ja existia inteira: o <see cref="Destronar"/> vaga o
	/// trono, avisa o mundo, tira o kit na hora e -- por ser o funil unico -- limpa junto o relogio de
	/// qualquer titulo em disputa. O tique seguinte apaga a ficha sozinho, porque o trono ficou vago.
	/// </summary>
	private void FalharTarefa(RankDef r, FichaDeMissao f, ServerPlayer portador, double agora)
	{
		f.Falhas++;
		f.Tarefa = TipoDeTarefa.Nenhuma;
		f.AlvoConta = f.AlvoNome = f.Planeta = f.PlanetaChave = "";
		f.Progresso = f.Meta = 0;
		f.Proxima = agora + MissoesDeCargo.SegundosDeIntervalo / 2;

		Avisar(portador, $"{r.Nome}: TAREFA FALHADA. Falhas: {f.Falhas}/{MissoesDeCargo.FalhasQueDestituem}.");
		Anunciar($"{r.Nome} FALHOU em seu dever ({f.Falhas}/{MissoesDeCargo.FalhasQueDestituem}).");

		if (f.Falhas >= MissoesDeCargo.FalhasQueDestituem)
			Destronar(r.Chave, "negligenciou os deveres do cargo");
	}

	// =====================================================================
	// 5. O QUE O RESTO DO SERVIDOR PERGUNTA
	// =====================================================================
	/// <summary>
	/// O RENOME de um cargo -- tarefas cumpridas por QUEM O CARREGA AGORA.
	///
	/// A conferencia do dono esta aqui e nao so no tique porque a ascensao pode ser tentada no mesmo
	/// segundo em que o trono trocou de mao: herdar o renome de quem serviu antes seria comprar
	/// promocao com o trabalho alheio.
	/// </summary>
	private int RenomeDe(string chave)
	{
		if (!_missoes.TryGetValue(chave, out FichaDeMissao? f)) return 0;
		string dono = _tronos.TryGetValue(chave, out string? d) ? d : "";
		return string.Equals(f.Dono, dono, StringComparison.OrdinalIgnoreCase) ? f.Cumpridas : 0;
	}

	private string TextoDeRenome(RankDef r, FichaDeMissao f)
	{
		if (!MissoesDeCargo.DegrausAcima(r.Chave).Any())
			return $"Renome: {f.Cumpridas} tarefa(s) cumprida(s) neste cargo.";
		return $"Renome: {Math.Min(f.Cumpridas, MissoesDeCargo.TarefasParaAscender)}"
			 + $"/{MissoesDeCargo.TarefasParaAscender} para a ascensão.";
	}

	// =====================================================================
	// 6. OS VERBS -- `Meu Rank` (`:664`)
	// =====================================================================
	/// <summary>
	/// O PAINEL DO PORTADOR: o que o cargo cobra, quanto falta, quantas falhas e onde a escada esta.
	///
	/// Ele existe porque um prazo que ninguem consegue LER e um prazo que so aparece quando ja
	/// venceu -- e a destituicao chegaria como castigo sem aviso. E o mesmo argumento do
	/// `GoD_Status`, que ja e um verb neste port.
	/// </summary>
	private void VerboMeusDeveres(ServerPlayer pl)
	{
		string chave = CargoDe(pl.Conta);
		RankDef? r = Cargos.Get(chave);
		if (r == null || !r.TemDeveres)
		{
			Avisar(pl, chave.Length == 0
				? "você não carrega cargo nenhum. (a aba Ranks mostra os vagos)"
				: $"{r?.Nome ?? chave} não é um cargo com deveres -- ele não recebe tarefas.");
			return;
		}

		double agora = TempoDoMundo;
		_missoes.TryGetValue(chave, out FichaDeMissao? f);
		int renome = RenomeDe(chave);

		Avisar(pl, $"-- {r.Nome} ({(r.Sabedoria ? "vocação de SABEDORIA: serviço" : "vocação de PODER")}) --");
		Avisar(pl, TextoDeRenome(r, f ?? new FichaDeMissao()));

		// A ESCADA, em tres estados diferentes -- e os tres dizem coisas opostas ao jogador (`:674-679`).
		var acima = MissoesDeCargo.DegrausAcima(chave).ToList();
		if (acima.Count == 0)
		{
			Avisar(pl, $"este é o fim da sua escada -- nenhum cargo exige {r.Nome} como pré-requisito.");
		}
		else if (!MissoesDeCargo.PodeAscender(renome))
		{
			Avisar(pl, $"degrau acima: {MissoesDeCargo.TextoDosDegraus(chave)} "
					 + $"(faltam {MissoesDeCargo.TarefasParaAscender - renome} tarefa(s) de renome).");
		}
		else if (acima.Any(a => !_tronos.TryGetValue(a.Chave, out string? dn) || dn.Length == 0))
		{
			Avisar(pl, $"ASCENSÃO LIBERADA: {MissoesDeCargo.TextoDosDegraus(chave)} -- reivindique o cargo.");
		}
		else
		{
			Avisar(pl, $"renome suficiente, mas {MissoesDeCargo.TextoDosDegraus(chave)} segue OCUPADO "
					 + "-- o trono precisa vagar.");
		}

		if (f == null || f.Tarefa == TipoDeTarefa.Nenhuma)
		{
			Avisar(pl, f == null || f.Proxima <= 0
				? "sem tarefa no momento. A próxima chega em breve."
				: $"sem tarefa no momento. A próxima chega em ~{MissoesDeCargo.TextoDeTempo(f.Proxima - agora)}.");
			Avisar(pl, $"falhas: {f?.Falhas ?? 0}/{MissoesDeCargo.FalhasQueDestituem}.");
			return;
		}

		Avisar(pl, $"tarefa: {MissoesDeCargo.Descricao(f)}");
		Avisar(pl, $"prazo restante: {MissoesDeCargo.TextoDeTempo(f.Prazo - agora)} | "
				 + $"falhas: {f.Falhas}/{MissoesDeCargo.FalhasQueDestituem}");

		if (f.Tarefa == TipoDeTarefa.Verba)
			Avisar(pl, $"o cofre da Terra tem {_cofreDaTerra:N0} zeni. Use o verb Fund Earth para depositar.");
	}

	/// <summary>
	/// A VERBA DO PRESIDENTE -- `:686-696`, que la e um `alert()` dentro do proprio "Meu Rank".
	///
	/// VIROU VERB PROPRIO pelo mesmo motivo do desafio ao Deus da Destruicao: um servidor autoritativo
	/// nao para esperando resposta de caixa de dialogo. Aqui o painel diz que existe e o verb faz.
	/// </summary>
	private void VerboDepositarVerba(ServerPlayer pl)
	{
		string chave = CargoDe(pl.Conta);
		if (!_missoes.TryGetValue(chave, out FichaDeMissao? f) || f.Tarefa != TipoDeTarefa.Verba)
		{
			Avisar(pl, "você não tem uma tarefa de verba em aberto.");
			return;
		}
		if (pl.Ficha.Zeni < MissoesDeCargo.VerbaDoPresidente)
		{
			Avisar(pl, $"zeni insuficiente: a verba é de {MissoesDeCargo.VerbaDoPresidente:N0} "
					 + $"e você tem {pl.Ficha.Zeni:N0}.");
			return;
		}

		pl.Ficha.Zeni -= MissoesDeCargo.VerbaDoPresidente;
		_cofreDaTerra += MissoesDeCargo.VerbaDoPresidente;
		f.Progresso = 1;
		SalvarMissoes();

		Avisar(pl, $"verba depositada -- o cofre da Terra agradece ({_cofreDaTerra:N0} zeni).");
	}
}
