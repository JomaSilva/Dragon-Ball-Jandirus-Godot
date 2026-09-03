using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Skills;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// LOTE G13 -- O SISTEMA DE ESTUDO DA ARVORE "STRENGTH OF MIND".
///
/// ============================ OS TRES VERBS QUE FALTAVAM DA ARVORE DA MENTE ============================
/// A arvore `/datum/skill/tree/Mind` (`Core Trees/Mind.dm:1`) tem dezessete skills e e dada a TODO
/// MUNDO (`Skills Master/mobhandlers.dm:46`). Quinze delas concedem verb em algum degrau, e doze
/// desses verbs ja estavam vivos (Kiai, Bola de Ki, Foco, Eficiencia, Controle de Poder, Ocultar o
/// Poder, Esfera Teleguiada, Campo Minado, Alvos de Ki, Avaliar o Ki, Telepatia, Observar).
///
/// FALTAVAM TRES, e os tres sao a MESMA coisa -- o sistema de estudo, que o censo catalogava como
/// *"percepcao e estudo de Ki (aprender vendo o outro usar)"*:
///
///     Study_Other       `KiStatsModule.dm:51`    estuda quem esta por perto e RENDE progresso
///     Focus_Skill       `KiStatsModule.dm:88`    escolhe QUAL skill o estudo adianta (10x)
///     Write_Teachings   `KiStatsModule.dm:148`   escreve um livro que OUTRA pessoa le
///
/// Os tres depositam (ou gastam) no mesmo lugar: o `expbuffer` de cada skill da Mente
/// (`NiveisDeSkill.Progresso.Buffer`), o banco de exp adiantado que este port declarava, por
/// escrito, nao ter *"nem campo nem quem o encha"*. Este lote e quem enche.
/// ====================================================================================================
///
/// ============================ POR QUE ELES SAO UM SISTEMA E NAO TRES BOTOES ============================
/// Sozinho, o `Study_Other` deposita `5*(nivel dele - seu nivel)*max(1,log2(seu nivel))` por segundo
/// naquela skill. Com uma skill EM FOCO, o mesmo laco deposita `50*` -- dez vezes -- naquela e em
/// nenhuma outra. E o `Write_Teachings` e o estudo ASSINCRONO: quem chegou longe escreve, e quem esta
/// atras le sem precisar ficar do lado.
///
/// A regra e sempre a mesma e ela e o coracao do sistema: **so se aprende de quem esta na frente**
/// (`if(nS.level < S.level)`, `:75`; `if(S.level <= level)`, `:196`).
/// ====================================================================================================
///
/// O QUE ESTE LOTE NAO TROUXE, DECLARADO:
///   * o `input()` que lista os alvos numa janela -- aqui e `Study_Other:&lt;nome&gt;`, como o resto do
///     port (`Telepathy:`, `Observe:`), e sem nome o verb LISTA quem da pra estudar;
///   * o `alert()` de "Continuar/Parar" do `Write_Teachings` -- aqui apertar de novo diz quanto falta
///     e `Write_Teachings:parar` desiste (perdendo o progresso, como no DM, `:172-179`).
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// OS NUMEROS, TODOS DO DM
	// =====================================================================
	/// <summary>`oview(10)` (`KiStatsModule.dm:70`): o laco so rende com o alvo a dez tiles.</summary>
	private const float AlcanceDoEstudoG13 = 10 * ZoneCollision.TileSize;

	/// <summary>`oview(20)` (`:57`): a lista de quem da pra escolher pra estudar.</summary>
	private const float AlcanceDaEscolhaG13 = 20 * ZoneCollision.TileSize;

	/// <summary>`sleep(10)` (`:69`): o laco do estudo rende UMA vez por segundo.</summary>
	private const double SegundosPorPassoDoEstudoG13 = 1.0;

	/// <summary>`expbuffer += 5*(...)` (`:78`) -- estudo sem foco.</summary>
	private const double DepositoSoltoG13 = 5;

	/// <summary>`expbuffer += 50*(...)` (`:76`) -- a skill EM FOCO rende dez vezes mais.</summary>
	private const double DepositoFocadoG13 = 50;

	/// <summary>
	/// `writetarget = nA.level * 300` (`:161`), em TIQUES DO EFETOR.
	///
	/// ============================ O DM CONTA O TEMPO ERRADO NA PROPRIA FRASE ============================
	/// `writetime++` mora no `medproc()` (`Meditate.dm:130`), que roda no laco de `Stats.dm:511` com
	/// `sleep(sleep_tiem)` e `sleep_tiem = 2` -- ou seja 5 Hz, 0,2 s por passo. Mas a frase que o verb
	/// imprime promete `[usr.writetarget/10] seconds` (`:167`), que so estaria certa a 10 Hz: o
	/// original anuncia METADE do tempo que vai cobrar.
	///
	/// A MECANICA FICA COMO ESTA LA (300 tiques por nivel, no relogio do efetor); o que o port nao
	/// copia e o numero errado da frase -- ele diz ao jogador o tempo de verdade. Mentira de tela nao
	/// e regra de jogo.
	/// ================================================================================================
	/// </summary>
	private const int TiquesPorNivelEscritoG13 = 300;

	// =====================================================================
	// ESTADO
	// =====================================================================
	/// <summary>Quem esta estudando quem: id do estudioso -> nome do alvo (o `studying` do DM e um bit).</summary>
	private readonly Dictionary<int, string> _estudoG13 = [];

	/// <summary>
	/// A skill EM FOCO de cada um -- o `mob/var/focusskill` (`:86`), que guarda o NOME e nao o
	/// typepath. Guardo o typepath: o nome vem do catalogo quando alguem precisa le-lo, e assim
	/// renomear uma skill nao apaga o foco de ninguem.
	/// </summary>
	private readonly Dictionary<int, string> _focoG13 = [];

	/// <summary>O livro em obra: a skill, o quanto falta, e o alvo. Os seis `mob/var` do `:141-146`.</summary>
	private sealed class EscritaG13
	{
		public string Path = "";
		public int NivelDoAutor;
		public int Feito;
		public int Alvo;
	}

	private readonly Dictionary<int, EscritaG13> _escritaG13 = [];

	/// <summary>O acumulador do passo de 1 s do estudo (o `sleep(10)` do laco).</summary>
	private double _relogioDoEstudoG13;

	// =====================================================================
	// REGISTRO
	// =====================================================================
	/// <summary>
	/// AS TRES DESTE LOTE. As linhas-espelho estao em `Core/Skills/Tecnicas.Portadas.cs` -- a
	/// `--catalogoteste` cobra as duas direcoes.
	/// </summary>
	private void RegistrarTecnicasG13()
	{
		IniciarLote("G13");
		Vivo("Study_Other", EstudarOutroG13);
		Vivo("Focus_Skill", FocarSkillG13);
		Vivo("Write_Teachings", EscreverEnsinamentosG13);
	}

	/// <summary>Este corpo saiu: o estudo, o foco e o livro em obra morrem com ele (`tmp` no DM).</summary>
	private void EsquecerG13(int id)
	{
		_estudoG13.Remove(id);
		_focoG13.Remove(id);
		_escritaG13.Remove(id);
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Ficha.studying = 0;
	}

	// =====================================================================
	// 1) STUDY_OTHER -- o laco que aprende olhando
	// =====================================================================
	/// <summary>
	/// ESTUDAR ALGUEM (`KiStatsModule.dm:51-83`).
	///
	/// Alterna-estado: apertando de novo (ou sem nome, ja estudando) para. Com nome, comeca -- e a
	/// partir dai, uma vez por segundo, toda skill da Mente que o ALVO tem num nivel mais alto que o
	/// seu deposita no seu banco de exp adiantado.
	///
	/// O `studying` da ficha e o que as tres skills de Percepcao leem como fonte de exp
	/// (`Mind.dm:189`, `:459`, `:846`) -- ele vale enquanto o laco esta de pe, mesmo que naquele
	/// segundo o alvo nao tenha nada a ensinar.
	/// </summary>
	private void EstudarOutroG13(ServerPlayer pl, string arg)
	{
		if (_estudoG13.ContainsKey(pl.Id))
		{
			PararDeEstudarG13(pl, "voce para de estudar.");
			return;
		}

		// ============================ O APERTO NU ESTUDA QUEM ESTA MARCADO ============================
		// No DM o verb abre um `input()` com os nomes a vinte tiles (`:56-62`). Este port nao tem
		// caixa modal e o painel de verbs so tem BOTAO -- entao o aperto nu usa o ALVO MARCADO (duplo
		// clique), que e o mesmo caminho que o `Assess_Ki_Skill` ja usa pro `oview(20)` dele.
		//
		// SEM ISSO O VERB SERIA INALCANCAVEL PELA TELA: `Study_Other:<nome>` continua valendo (e o
		// que a bancada e o admin usam), mas nada no cliente monta essa string. A lista so aparece
		// quando nao ha ninguem marcado, que e quando ela e util.
		// ==========================================================================================
		if (arg.Length == 0 && Marcado(pl) is { } marcado && marcado != pl
			&& Vec2.Distance(marcado.Pos, pl.Pos) <= AlcanceDaEscolhaG13)
			arg = marcado.Name;

		if (arg.Length == 0)
		{
			// `if(M.client)` (`:59`): a LISTA so oferece quem tem dono. O laco do estudo (`:71`) nao
			// tem esse filtro -- ver `AchaPorPertoG13`, que por isso tambem nao tem.
			var nomes = new List<string>();
			foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
				if (o != pl && o.Peer != null
					&& Vec2.Distance(o.Pos, pl.Pos) <= AlcanceDaEscolhaG13) nomes.Add(o.Name);
			Avisar(pl, nomes.Count == 0
				? "nao ha ninguem por perto pra estudar (marque alguem com duplo clique)."
				: $"da pra estudar: {string.Join(", ", nomes)}. Marque com duplo clique, ou Study_Other:<nome>.");
			return;
		}

		ServerPlayer? alvo = AchaPorPertoG13(pl, arg, AlcanceDaEscolhaG13);
		if (alvo == null) { Avisar(pl, $"nao ha ninguem chamado '{arg}' por perto."); return; }
		if (alvo == pl) { Avisar(pl, "estudar a si mesmo nao ensina nada novo."); return; }

		_estudoG13[pl.Id] = alvo.Name;
		pl.Ficha.studying = 1;
		Avisar(pl, $"voce comeca a estudar {alvo.Name}.");
		string foco = _focoG13.TryGetValue(pl.Id, out string? f) ? NomeDaSkillG13(f) : "";
		Avisar(pl, foco.Length > 0
			? $"seu foco esta em {foco} -- o estudo rende dez vezes mais nela."
			: "sem foco escolhido, o estudo rende um pouco em tudo (Focus_Skill:<nome> concentra).");
	}

	private void PararDeEstudarG13(ServerPlayer pl, string frase)
	{
		_estudoG13.Remove(pl.Id);
		pl.Ficha.studying = 0;
		Avisar(pl, frase);
	}

	/// <summary>
	/// O PASSO DE UM SEGUNDO DO ESTUDO -- o corpo do `while(studying)` do DM (`:68-83`).
	///
	/// O `range` do original vira a checagem de distancia: alvo fora dos dez tiles (ou fora da zona,
	/// ou morto, ou desconectado) e `studying = 0` -- literal ao `if(range<=0) studying=0` (`:82`).
	/// </summary>
	private void PassoDoEstudoG13()
	{
		foreach (int id in _estudoG13.Keys.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) { _estudoG13.Remove(id); continue; }
			if (pl.Livro == null || pl.Ficha.dead || pl.Ficha.KO)
			{
				PararDeEstudarG13(pl, "voce perde a concentracao e para de estudar.");
				continue;
			}

			ServerPlayer? alvo = AchaPorPertoG13(pl, _estudoG13[id], AlcanceDoEstudoG13);
			if (alvo == null || alvo.Livro == null)
			{
				PararDeEstudarG13(pl, "voce perde seu alvo de vista e para de estudar.");
				continue;
			}

			string? foco = _focoG13.GetValueOrDefault(id);
			foreach (string path in pl.Livro.Aprendidas)
			{
				// SO AS DA MENTE: o laco do DM e `for(var/datum/skill/mind/S in nM.learned_skills)`
				// (`:72`) -- ele nao enxerga skill de outro typepath. Sao as 17 da arvore da Mente
				// mais as 33 pericias de Ki, que sao da mesma classe (ver `RegraDeNivel.Mental`).
				if (RegrasDeNivel.Get(path) is not { Mental: true }) continue;
				if (!alvo.Livro.Sabe(path)) continue;

				int meu = pl.Niveis.Nivel(path);
				int dele = alvo.Niveis.Nivel(path);
				if (meu >= dele) continue;   // `if(nS.level < S.level)`

				// `50*` na skill em foco, `5*` em todas quando nao ha foco. E EXCLUSIVO: com foco
				// escolhido, o DM nao deposita nada nas outras (`else if(!focusskill && ...)`, `:77`).
				double taxa;
				if (foco != null) { if (!string.Equals(foco, path, StringComparison.OrdinalIgnoreCase)) continue; taxa = DepositoFocadoG13; }
				else taxa = DepositoSoltoG13;

				// `max(1, log(2, nS.level))` -- `log(2, 0)` no BYOND e indefinido, e o `max(1,...)`
				// e o que segura isso; aqui o piso de 1 no nivel faz o mesmo trabalho.
				double quanto = taxa * (dele - meu) * Math.Max(1, Math.Log2(Math.Max(meu, 1)));
				pl.Niveis.Depositar(path, quanto);
			}
		}
	}

	/// <summary>
	/// Quem se chama assim, esta nesta zona, e a esta distancia. Nulo se nao houver.
	///
	/// SEM FILTRO DE DONO, e e do DM: o laco do estudo e `for(var/mob/nM in oview(10)) if(nM.name ==
	/// target)` (`:71-72`) -- qualquer corpo serve. So a LISTA de escolha pede `M.client` (`:59`).
	/// Estudar um NPC que sabe mais que voce funciona no original, e funciona aqui.
	/// </summary>
	private ServerPlayer? AchaPorPertoG13(ServerPlayer pl, string nome, float alcance)
	{
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			if (string.Equals(o.Name, nome, StringComparison.OrdinalIgnoreCase)
				&& Vec2.Distance(o.Pos, pl.Pos) <= alcance) return o;
		return null;
	}

	// =====================================================================
	// 2) FOCUS_SKILL -- a escolha que decupla o estudo
	// =====================================================================
	/// <summary>
	/// FOCAR UMA SKILL (`KiStatsModule.dm:88-95`). Sem argumento, LISTA as suas skills da Mente e diz
	/// qual esta em foco; com o nome, escolhe. `Focus_Skill:nenhuma` solta o foco -- o DM nao tem esse
	/// caminho (la o `input()` sempre devolve uma das suas skills), mas sem ele nao haveria como voltar
	/// ao estudo largo depois de escolher uma vez.
	/// </summary>
	private void FocarSkillG13(ServerPlayer pl, string arg)
	{
		if (pl.Livro == null || _skills == null) return;

		var minhas = new List<string>();
		foreach (string path in pl.Livro.Aprendidas)
			if (RegrasDeNivel.Get(path) is { Mental: true }) minhas.Add(path);

		if (arg.Length == 0)
		{
			if (minhas.Count == 0) { Avisar(pl, "voce nao tem nada em que focar!"); return; }   // `:90`
			string atual = _focoG13.TryGetValue(pl.Id, out string? f) ? NomeDaSkillG13(f) : "nenhuma";
			Avisar(pl, $"seu foco: {atual}. Use Focus_Skill:<nome> (ou 'nenhuma' pra soltar).");
			Avisar(pl, "pode focar: " + string.Join(", ",
				minhas.Select(p => $"{NomeDaSkillG13(p)} (nivel {pl.Niveis.Nivel(p)})")));
			return;
		}

		if (string.Equals(arg, "nenhuma", StringComparison.OrdinalIgnoreCase))
		{
			_focoG13.Remove(pl.Id);
			Avisar(pl, "voce solta o foco: o estudo volta a render um pouco em tudo.");
			return;
		}

		foreach (string path in minhas)
			if (string.Equals(NomeDaSkillG13(path), arg, StringComparison.OrdinalIgnoreCase))
			{
				_focoG13[pl.Id] = path;
				Avisar(pl, $"voce agora vai focar em aprender {NomeDaSkillG13(path)}.");   // `:94`
				return;
			}

		Avisar(pl, $"voce nao sabe nenhuma habilidade da Mente chamada '{arg}'.");
	}

	private string NomeDaSkillG13(string path) => _skills?.Get(path)?.Nome ?? path;

	// =====================================================================
	// 3) WRITE_TEACHINGS -- o estudo que atravessa a sala
	// =====================================================================
	/// <summary>
	/// ESCREVER UM LIVRO (`KiStatsModule.dm:148-179`).
	///
	/// Escolhe uma skill da Mente que voce ja subiu, e a partir dai MEDITAR escreve: `level*300`
	/// tiques do efetor. No fim, o livro cai na mochila (`src.contents += A`, `Meditate.dm:139`) e
	/// ensina ate a METADE do seu nivel.
	///
	/// `nA.level == 0` e recusado com "You have no knowledge to teach!" (`:158-160`) -- comprar a
	/// skill nao e saber nada dela.
	/// </summary>
	private void EscreverEnsinamentosG13(ServerPlayer pl, string arg)
	{
		if (pl.Livro == null || _skills == null) return;

		if (_escritaG13.TryGetValue(pl.Id, out EscritaG13? emObra))
		{
			if (string.Equals(arg, "parar", StringComparison.OrdinalIgnoreCase))
			{
				// `if("Stop")`: zera TUDO -- "You will lose all progress if you stop." (`:171-179`)
				_escritaG13.Remove(pl.Id);
				Avisar(pl, "voce para de escrever, e o que ja tinha escrito se perde.");
				return;
			}
			double faltam = (emObra.Alvo - emObra.Feito) * NiveisDeSkill.SegundosPorTique;
			Avisar(pl, $"voce ainda precisa de {faltam:0} segundos MEDITANDO pra terminar "
					 + $"'{NomeDaSkillG13(emObra.Path)}'. (Write_Teachings:parar desiste e perde tudo.)");
			return;
		}

		var minhas = new List<string>();
		foreach (string path in pl.Livro.Aprendidas)
			if (RegrasDeNivel.Get(path) is { Mental: true }) minhas.Add(path);

		if (arg.Length == 0)
		{
			Avisar(pl, minhas.Count == 0
				? "voce nao tem nenhuma habilidade da Mente pra ensinar."
				: "sobre o que quer escrever? " + string.Join(", ",
					minhas.Select(p => $"{NomeDaSkillG13(p)} (nivel {pl.Niveis.Nivel(p)})"))
				  + ". Use Write_Teachings:<nome>. So se escreve MEDITANDO.");
			return;
		}

		foreach (string path in minhas)
		{
			if (!string.Equals(NomeDaSkillG13(path), arg, StringComparison.OrdinalIgnoreCase)) continue;

			int nivel = pl.Niveis.Nivel(path);
			if (nivel <= 0) { Avisar(pl, "voce nao tem conhecimento nenhum pra ensinar!"); return; }

			_escritaG13[pl.Id] = new EscritaG13
			{
				Path = path,
				NivelDoAutor = nivel,
				Alvo = nivel * TiquesPorNivelEscritoG13,
			};
			double segundos = nivel * TiquesPorNivelEscritoG13 * NiveisDeSkill.SegundosPorTique;
			Avisar(pl, $"voce comeca a escrever um livro sobre {NomeDaSkillG13(path)}. "
					 + $"Vai levar {segundos:0} segundos MEDITANDO. (Medite pra escrever.)");
			return;
		}

		Avisar(pl, $"voce nao sabe nenhuma habilidade da Mente chamada '{arg}'.");
	}

	/// <summary>
	/// O PASSO DA ESCRITA -- `if(writing) writetime++` dentro do `medproc()` (`Meditate.dm:130-142`).
	///
	/// SO MEDITANDO, e essa e a mecanica inteira: o livro e o preco de ficar parado. Roda na cadencia
	/// do efetor (5 Hz), que e a do `medproc` do original -- ver <see cref="TiquesPorNivelEscritoG13"/>.
	/// </summary>
	private void PassoDaEscritaG13()
	{
		foreach (int id in _escritaG13.Keys.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) { _escritaG13.Remove(id); continue; }
			if (!pl.Ficha.med || pl.Ficha.KO || pl.Ficha.dead) continue;

			EscritaG13 e = _escritaG13[id];
			if (++e.Feito < e.Alvo) continue;

			_escritaG13.Remove(id);
			var livro = new LivroDeEnsinamentos(NomeDaSkillG13(e.Path), e.NivelDoAutor);
			if (!Guardar(pl, livro.Id)) { Avisar(pl, "o livro esta pronto e nao cabe na sua mochila!"); continue; }
			Avisar(pl, $"voce terminou de escrever! '{livro.Ficha.Nome}' esta na sua mochila.");
			GD.Print($"[server] {pl.Name} escreveu um livro de '{livro.Skill}' (autor nivel {e.NivelDoAutor}, "
				   + $"ensina ate o {livro.NivelQueEnsina})");
		}
	}

	/// <summary>
	/// LER O LIVRO -- o `Study_Book` do original (`KiStatsModule.dm:190-203`).
	///
	///     if(S.name == skillname)
	///         if(S.level <= level) { gain = S.KiSkillGains(exp); S.exp += gain; del(src) }
	///         else "You can't learn anything from this book"
	///     if(!count) "You don't know this skill!"
	///
	/// O exp gravado no livro passa pelo `KiSkillGains` de QUEM LE (e pelo banco de exp adiantado
	/// dele) -- e por isso o mesmo livro rende diferente em duas pessoas.
	/// </summary>
	private void LerOsEnsinamentosG13(ServerPlayer pl, ItemDef def)
	{
		if (pl.Livro == null) return;
		if (LivroDeEnsinamentos.Ler(def.Id) is not { } livro) { Avisar(pl, "esse livro esta ilegivel."); return; }

		string? path = null;
		foreach (string p in pl.Livro.Aprendidas)
			if (RegrasDeNivel.Get(p) is { Mental: true } && string.Equals(NomeDaSkillG13(p), livro.Skill, StringComparison.OrdinalIgnoreCase))
			{ path = p; break; }

		if (path == null) { Avisar(pl, "voce nao conhece essa habilidade!"); return; }   // `:202`

		if (pl.Niveis.Nivel(path) > livro.NivelQueEnsina)
		{
			Avisar(pl, "voce nao consegue aprender nada com este livro.");   // `:200`
			return;
		}

		double ganho = pl.Niveis.CreditarComCurva(path, livro.Exp, pl.Ficha);
		pl.Mochila.Tirar(def.Id);   // `del(src)` -- o livro se gasta ao ser lido
		MandarMochila(pl);
		Avisar(pl, $"voce le os ensinamentos: {livro.Skill} ganhou {ganho:0} de experiencia!");
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>
	/// O TIQUE DESTE LOTE, na cadencia do EFETOR (5 Hz) -- chamado de dentro do `TickDosNiveis`.
	///
	/// Os dois passos moram em relogios diferentes no DM e por isso tambem aqui: a escrita anda no
	/// `medproc` (5 Hz, um tique por chamada) e o estudo tem `sleep(10)` proprio (1 Hz). Contar o
	/// segundo do estudo com um acumulador em vez de um relogio de parede e o que deixa a bancada
	/// exercita-lo sem esperar.
	/// </summary>
	private void TickG13()
	{
		PassoDaEscritaG13();

		_relogioDoEstudoG13 += NiveisDeSkill.SegundosPorTique;
		if (_relogioDoEstudoG13 < SegundosPorPassoDoEstudoG13) return;
		_relogioDoEstudoG13 -= SegundosPorPassoDoEstudoG13;
		PassoDoEstudoG13();
	}
}
