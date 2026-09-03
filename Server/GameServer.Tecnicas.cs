using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// AS TECNICAS ATIVAS -- o que acontece quando o jogador aperta o botao.
///
/// Os NUMEROS moram no Core (<see cref="Tecnicas"/>), portados do corpo dos verbs do DM; o que
/// mora aqui e o EFEITO no mundo, que so o servidor pode fazer: quem foi cegado, quem some do
/// snapshot, quanto Ki saiu.
///
/// TRES TECNICAS DE TIPOS DIFERENTES, e isso e escolha, nao amostra aleatoria. Solar Flare mexe
/// na VISAO alheia, Invisibility mexe no que o snapshot conta, Ki Shield mexe na cadeia de stats.
/// Cada uma abriu um encanamento que faltava, e sao esses tres encanamentos que as proximas
/// quarenta e tantas vao usar -- portar uma quarta tecnica de dano em area agora e escrever o
/// efeito, nao a infraestrutura.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// CEGUEIRA, INVISIBILIDADE E ESCUDO precisam de prazo, e prazo mora no servidor.
	///
	/// Guardado FORA do <see cref="ServerPlayer"/> de proposito: sao estados de sessao de uma
	/// tecnica so, e enfiar um campo por tecnica na ficha de todo mundo faria a ficha crescer uma
	/// linha a cada skill portada. Trezentas skills, trezentos campos.
	/// </summary>
	private readonly Dictionary<int, long> _cegoAte = [];
	private readonly Dictionary<int, long> _solarPronto = [];
	private readonly HashSet<int> _invisiveis = [];
	private readonly Dictionary<int, double> _escudoAtivo = [];   // id -> bonus aplicado

	public bool EstaOculto(int id) => _invisiveis.Contains(id);

	/// <summary>
	/// QUEM SAIU LEVA O ESTADO DELE JUNTO -- inscrito no `EsquecerTecnicas`, porque id se reusa. So o
	/// REGISTRO some: o bonus do escudo mora em `Tphysdef`/`Tkidef`, que nao vao pro disco -- o corpo
	/// que relogar nasce com a defesa limpa, e e por isso que herdar a ENTRADA era o problema (o
	/// desligar subtrairia de um corpo que nunca somou). A `--catalogoteste` (familia 7) prova.
	/// </summary>
	private void EsquecerBaseDasTecnicas(int id)
	{
		_cegoAte.Remove(id);
		_solarPronto.Remove(id);
		_invisiveis.Remove(id);
		_escudoAtivo.Remove(id);
	}

	// =====================================================================
	// SOLAR FLARE
	// =====================================================================
	/// <summary>
	/// CEGA QUEM ESTAVA OLHANDO. O original checa a direcao da vitima contra a sua e so pega
	/// quem esta de frente ou nas duas diagonais vizinhas -- 135 graus de arco.
	///
	/// Isso e o coracao da tecnica e nao da pra simplificar pra "todo mundo por perto": se cegar
	/// de costas tambem, nao existe mais razao pra desviar o olhar, e a unica defesa da tecnica
	/// desaparece. O jogador aprende a virar o rosto porque virar o rosto FUNCIONA.
	/// </summary>
	private void SolarFlare(ServerPlayer pl)
	{
		if (EmEspera(pl, _solarPronto, "a tecnica ainda se recompoe")) return;
		long agora = NowMs();

		double custo = Tecnicas.SolarCustoKi(pl.Ficha);
		if (pl.Ficha.Ki < custo) { Avisar(pl, $"isso pede pelo menos {custo:0} de energia."); return; }
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "voce nao esta em condicoes."); return; }

		pl.Ficha.Ki -= custo;
		_solarPronto[pl.Id] = agora + Tecnicas.SolarRecargaMs(pl.Ficha);
		pl.Ficha.kidebuffskill += 0.4;   // `usr.kidebuffcounter += 4` -- usar treina
		CreditarContador(pl, "kidebuffcounter", 4);   // `misc.dm:14`

		double alcance = Tecnicas.SolarAlcanceTiles(pl.Ficha) * ZoneCollision.TileSize;
		long dura = Tecnicas.SolarCegueiraMs(pl.Ficha);
		int pegos = 0;

		Falar(pl, Protocol.Fala.Diz, "Solar Flare!!");

		foreach (ServerPlayer outro in _players.Values)
		{
			if (outro == pl || !outro.Zone.Equals(pl.Zone)) continue;
			if (Vec2.Distance(outro.Pos, pl.Pos) > alcance) continue;
			if (!Tecnicas.EstaOlhandoPra(outro.Pos, outro.Facing, pl.Pos)) continue;

			_cegoAte[outro.Id] = Math.Max(_cegoAte.GetValueOrDefault(outro.Id), agora + dura);
			MandarEfeito(outro, "cegueira", dura);
			Avisar(outro, $"a luz do Solar Flare de {pl.Name} apaga tudo.");
			pegos++;
		}

		Avisar(pl, pegos == 0
			? "a luz estoura e ninguem estava olhando."
			: $"a luz estoura: {pegos} {(pegos == 1 ? "pessoa fica cega" : "pessoas ficam cegas")}.");
	}

	// =====================================================================
	// INVISIBILITY
	// =====================================================================
	private void Invisibilidade(ServerPlayer pl)
	{
		if (_invisiveis.Remove(pl.Id))
		{
			pl.Ficha.isconcealed = false;
			Avisar(pl, "voce reaparece.");
			MandarEfeito(pl, "invisivel", 0);
			return;
		}
		if (pl.Ficha.Ki < Tecnicas.InvisKiMinimo) { Avisar(pl, "energia de menos pra sumir."); return; }

		_invisiveis.Add(pl.Id);
		pl.Ficha.isconcealed = true;
		Avisar(pl, "voce some da vista.");
		MandarEfeito(pl, "invisivel", -1);
	}

	// =====================================================================
	// KI SHIELD
	// =====================================================================
	/// <summary>
	/// O BONUS E GUARDADO, nao recalculado no fim. O original soma `initbuff` ao ligar e subtrai
	/// O MESMO `initbuff` ao desligar -- se recalculasse na hora de tirar, um jogador que
	/// ficasse mais forte com o escudo de pe sairia com defesa NEGATIVA. Vale pra qualquer
	/// buff temporario que dependa de stat: guarde o que somou.
	/// </summary>
	private void KiShield(ServerPlayer pl)
	{
		if (_escudoAtivo.Remove(pl.Id, out double bonus))
		{
			pl.Ficha.Tphysdef -= bonus;
			pl.Ficha.Tkidef -= bonus;
			pl.Ficha.Statify();
			Avisar(pl, "o escudo se desfaz.");
			MandarEfeito(pl, "escudo", 0);
			return;
		}

		double custo = Tecnicas.EscudoCustoKi(pl.Ficha);
		if (pl.Ficha.Ki < custo) { Avisar(pl, $"isso pede {custo:0} de energia."); return; }

		pl.Ficha.Ki -= custo;
		bonus = Tecnicas.EscudoBonus(pl.Ficha);
		_escudoAtivo[pl.Id] = bonus;
		pl.Ficha.Tphysdef += bonus;
		pl.Ficha.Tkidef += bonus;
		pl.Ficha.Statify();
		Avisar(pl, $"uma casca de Ki se fecha em volta de voce (+{bonus:0.##} de defesa).");
		MandarEfeito(pl, "escudo", -1);
	}

	// =====================================================================
	// O TICK das sustentadas
	// =====================================================================
	/// <summary>
	/// COBRA O ALUGUEL das tecnicas ligadas e derruba as que nao se sustentam mais.
	///
	/// Roda uma vez por segundo (junto do tick de ficha) porque o dreno do original e por
	/// segundo -- cobrar a 30 Hz e dividir por 30 daria o mesmo numero e trinta vezes o trabalho.
	/// </summary>
	private void TickDasTecnicas()
	{
		long agora = NowMs();

		foreach (int id in _invisiveis.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) { _invisiveis.Remove(id); continue; }

			// O ZANZO CLASH NAO PAGA ALUGUEL. Ele usa este mesmo esconderijo (um caminho so pra
			// sumir), mas quem some la nao esta sustentando a tecnica: esta em cima de uma cena de
			// quatro segundos que o servidor conduz. Cobrar Ki dela derrubaria os dois no meio do
			// embate -- e o pior jeito de acabar uma disputa e sem que nenhum dos dois tenha errado.
			if (_emEmbate.ContainsKey(id)) continue;
			// O SNEAK (lote G11) TAMBEM NAO PAGA ALUGUEL: no DM ele e um `TempBuff` com custo unico no
			// verb (`Assassain Skills.dm:167-171`), sem dreno por segundo; quem o expira e o efetor do G11.
			if (_sneakAteG11.ContainsKey(id)) continue;

			pl.Ficha.Ki -= Tecnicas.InvisDrenoPorSegundo(pl.Ficha);
			if (pl.Ficha.Ki >= Tecnicas.InvisKiMinimo && !pl.Ficha.KO) continue;

			_invisiveis.Remove(id);
			pl.Ficha.isconcealed = false;
			pl.Ficha.Ki = Math.Max(pl.Ficha.Ki, 0);
			Avisar(pl, "voce nao consegue mais se sustentar invisivel.");
			MandarEfeito(pl, "invisivel", 0);
		}

		// O ESCUDO CAI NO NOCAUTE E NA MEDITACAO (`while(KiShieldOn && !usr.med)`): quem apagou
		// nao esta segurando casca nenhuma, e essa e a janela em que o escudo mais valeria.
		foreach (int id in _escudoAtivo.Keys.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) { _escudoAtivo.Remove(id); continue; }
			if (!pl.Ficha.KO && !pl.Ficha.med) continue;
			KiShield(pl);   // o proprio desligar ja devolve o bonus guardado
		}

		foreach (int id in _cegoAte.Keys.ToList())
			if (agora >= _cegoAte[id]) _cegoAte.Remove(id);
	}

	/// <summary>Um efeito caiu (ou saiu) de cima de voce. `ms` negativo = enquanto durar.</summary>
	private static void MandarEfeito(ServerPlayer pl, string id, long ms)
	{
		var w = Protocol.Begin(Protocol.S2C.Efeito);
		w.Put(id);
		w.Put(ms);
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// O REGISTRO DAS TECNICAS VIVAS -- uma tabela, e nao doze cadeias de `if`
	// =====================================================================
	/// <summary>
	/// UMA TECNICA VIVA no servidor: o verb que a DESTRAVA (<see cref="Gate"/>, o que o
	/// <see cref="SabeTecnica"/> pergunta), o LOTE que a registrou (so as bancadas leem) e o que
	/// fazer quando o jogador aperta. <see cref="Agir"/> recebe o ARGUMENTO do id -- o que vem
	/// depois do ':' (`Telepathy:Goku:oi`) -- ou, nas registradas por prefixo, o id inteiro
	/// (`Kaioken_20`, `Phrase_3`), que cada familia parte do seu jeito.
	/// </summary>
	private sealed record TecnicaViva(string Gate, string Lote, bool PorPrefixo, Action<ServerPlayer, string> Agir);

	private readonly Dictionary<string, TecnicaViva> _tecnicas = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<(string Prefixo, TecnicaViva Tecnica)> _tecnicasPorPrefixo = [];
	private string _loteEmRegistro = "";

	/// <summary>O lote que as proximas chamadas de <see cref="Vivo"/> assinam. Cada `RegistrarTecnicasGx` abre o seu.</summary>
	private void IniciarLote(string lote) => _loteEmRegistro = lote;

	/// <summary>
	/// REGISTRA uma tecnica viva pelo id exato. O MESMO lote pode registrar de novo (a bancada do selo
	/// re-roda o `RegistrarTecnicasG9` pra desfazer uma arrancada de efeito); DOIS lotes com o mesmo id
	/// e erro de programacao e derruba o boot de proposito -- e o "todo verb esta em uma lista, e em
	/// uma so", que antes era uma afirmacao de bancada sobre doze listas e agora e uma propriedade da
	/// tabela.
	/// </summary>
	private void Vivo(string id, Action<ServerPlayer> agir) => Vivo(id, (pl, _) => agir(pl));

	private void Vivo(string id, Action<ServerPlayer, string> agir)
	{
		if (_tecnicas.TryGetValue(id, out TecnicaViva? outra) && outra.Lote != _loteEmRegistro)
			throw new InvalidOperationException($"tecnica registrada por dois lotes: {id} ({outra.Lote} e {_loteEmRegistro})");
		_tecnicas[id] = new TecnicaViva(id, _loteEmRegistro, PorPrefixo: false, agir);
	}

	/// <summary>
	/// REGISTRA uma FAMILIA de ids por prefixo (`Kaioken_20`, `Final_Explosion_25`, `Phrase_3`): quem
	/// destrava e <paramref name="gate"/>, e o handler recebe o id INTEIRO pra partir o sufixo.
	/// </summary>
	private void VivoPorPrefixo(string prefixo, string gate, Action<ServerPlayer, string> agir)
	{
		_tecnicasPorPrefixo.RemoveAll(p => p.Prefixo.Equals(prefixo, StringComparison.OrdinalIgnoreCase));   // idem: re-registro do mesmo lote
		_tecnicasPorPrefixo.Add((prefixo, new TecnicaViva(gate, _loteEmRegistro, PorPrefixo: true, agir)));
	}

	/// <summary>A tecnica que atende este id-base: exata primeiro, senao a familia cujo prefixo casa.</summary>
	private TecnicaViva? TecnicaVivaDe(string baseId)
	{
		if (_tecnicas.TryGetValue(baseId, out TecnicaViva? t)) return t;
		foreach ((string prefixo, TecnicaViva familia) in _tecnicasPorPrefixo)
			if (baseId.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)) return familia;
		return null;
	}

	/// <summary>
	/// VARRE UM DICIONARIO DE ESTADO SUSTENTADO e devolve so as linhas que ainda tem DONO NO MUNDO --
	/// tirando do dicionario, no caminho, quem saiu. Era o preambulo de seis tiques do G12, escrito
	/// seis vezes:
	///
	///     foreach (int id in _xG12.Keys.ToList())
	///     {
	///         EstadoX x = _xG12[id];
	///         if (!_players.TryGetValue(id, out ServerPlayer? pl)) { _xG12.Remove(id); continue; }
	///
	/// So o PREAMBULO mora aqui. A condicao de QUEDA ("cai quando ele medita", "cai quando ele troca
	/// de planeta") continua escrita em cada tique, porque ela e LITERAL DO DM e cada tecnica tem a
	/// sua: o `med`/`train` derruba a Death Ball e o Buster e NAO derruba o Volei nem a Genkidama, e
	/// "trocou de zona" so o G12 confere. Uma condicao unica aqui inventaria regra pra quem nao tinha.
	///
	/// `Keys.ToList()` porque o corpo do laco REMOVE do proprio dicionario; e o `TryGetValue` no
	/// estado (onde antes havia um indexador cru) porque um tique pode tirar a linha de OUTRO id --
	/// o indexador estouraria, e a varredura ja tinha a resposta certa a mao.
	/// </summary>
	private IEnumerable<(int Id, T Estado, ServerPlayer Corpo)> Varrer<T>(Dictionary<int, T> estados)
	{
		foreach (int id in estados.Keys.ToList())
		{
			if (!estados.TryGetValue(id, out T? estado)) continue;
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) { estados.Remove(id); continue; }
			yield return (id, estado, pl);
		}
	}

	/// <summary>
	/// OS IDS QUE ESTE SERVIDOR SABE ATENDER -- o lado "vivo no jogo" da conta das DUAS BOCAS.
	///
	/// ============================ POR QUE ISTO PRECISOU EXISTIR ============================
	/// Enquanto cada lote escrevia o DESCRITOR (`Tecnicas.Registrar`) alem do CORPO (<see cref="Vivo"/>),
	/// "vivo no jogo" dava pra ler do proprio catalogo: `Tecnicas.Vivas` era o que os lotes tinham
	/// registrado. Com o descritor morando so no Core, `Tecnicas.Vivas` passou a ser *o espelho* -- e
	/// comparar o espelho com ele mesmo e uma prova que nunca reprova. A pergunta de verdade e outra e
	/// continua valendo: **todo id com CORPO tem descritor, e todo descritor tem CORPO?**
	///
	/// AS FAMILIAS POR PREFIXO NAO SE ENUMERAM (`Kaioken_20`, `Phrase_3`: um corpo atende qualquer
	/// sufixo), entao elas entram pelo lado do ESPELHO -- todo descritor que <see cref="TecnicaVivaDe"/>
	/// consegue atender conta como "tem corpo". Quem responde e o MESMO despacho que o jogador aperta,
	/// e nao uma segunda lista de prefixos que poderia discordar dele.
	/// ======================================================================================
	/// </summary>
	private List<string> TecnicasComCorpo()
	{
		var ids = new List<string>(_tecnicas.Keys);
		foreach (string id in Tecnicas.NoEspelho)
			if (!_tecnicas.ContainsKey(id) && TecnicaVivaDe(id) != null) ids.Add(id);
		return ids;
	}

	/// <summary>Os ids que um lote registrou, na ordem do registro -- pras bancadas de lote.</summary>
	private List<string> IdsDoLote(string lote) => [.. _tecnicas.Where(kv => kv.Value.Lote == lote).Select(kv => kv.Key)];

	/// <summary>Este id foi registrado por este lote? -- pras bancadas que afirmam "continua FORA do lote".</summary>
	private bool EhDoLote(string lote, string id) => _tecnicas.TryGetValue(id, out TecnicaViva? t) && t.Lote == lote;

	/// <summary>
	/// AS CINCO DA BASE. Os descritores delas moram no Core (o construtor estatico de `Tecnicas`);
	/// aqui entra so o corpo, como em todo lote.
	/// </summary>
	private void RegistrarTecnicasBase()
	{
		IniciarLote("base");
		Vivo("Solar_Flare", SolarFlare);
		Vivo("Invisibility", Invisibilidade);
		Vivo("Ki_Shield", KiShield);
		Vivo("Regenerate", Regenerar);
		Vivo("Space_Flight", Decolar);
	}

	/// <summary>
	/// O DESPACHO das tecnicas ativas, pelo id do verb do DM.
	///
	/// ============================ UMA TABELA, E NAO DOZE CADEIAS DE `if` ============================
	/// Cada lote registra no boot as suas tecnicas vivas (<see cref="Vivo"/>/<see cref="VivoPorPrefixo"/>,
	/// nos `RegistrarTecnicasGx`), e este metodo faz TRES coisas, uma vez cada: parte o argumento no
	/// primeiro ':' (`Telepathy:Goku:oi` -> `Telepathy` + `Goku:oi`), acha a tecnica (id exato, senao
	/// a familia do prefixo: `Kaioken_20`, `Phrase_3`) e pergunta ao <see cref="SabeTecnica"/> pelo
	/// verb que a DESTRAVA -- que nem sempre e o id: `Phrase_3` e destravada por `Magic_Words`.
	///
	/// Antes eram sete lotes com o proprio `if`/`switch`, cada um partindo o id do seu jeito e
	/// repetindo o gate com a sua frase de recusa, mais cinco lotes pendurados num `default` -- e a
	/// ORDEM entre eles era regra ("quem aceita argumento vem antes do gate generico"). Todo id e
	/// comparado sem diferenciar maiusculas, em todos os lotes.
	///
	/// A tecnica que o jogador NAO sabe e recusada aqui, nao no cliente: o botao pode nem existir na
	/// tela, mas quem manda o pacote na mao tem que ouvir nao. AS INVENTADAS (`Custom_AttackN`)
	/// entram DEPOIS da tabela e ANTES da recusa generica, como sempre: nenhuma skill as destrava --
	/// no DM os verbos custom sao concedidos pelo `after_learn()` do proprio datum (`:122-127`).
	/// ================================================================================================
	/// </summary>
	private bool UsarTecnica(ServerPlayer pl, string id)
	{
		if (id.Length == 0) return false;

		int corte = id.IndexOf(':');
		string baseId = corte < 0 ? id : id[..corte];
		string arg = corte < 0 ? "" : id[(corte + 1)..].Trim();

		if (TecnicaVivaDe(baseId) is { } viva)
		{
			if (!SabeTecnica(pl, viva.Gate))
			{
				Avisar(pl, $"voce nao sabe {Tecnicas.Get(viva.Gate)?.Nome ?? viva.Gate}.");
				return true;
			}
			viva.Agir(pl, viva.PorPrefixo ? baseId : arg);
			return true;
		}

		if (UsarTecnicaCustomizada(pl, id)) return true;

		Tecnicas.Tecnica? t = Tecnicas.Get(id);
		if (t == null) return false;

		if (!SabeTecnica(pl, id))
		{
			Avisar(pl, $"voce nao sabe {t.Nome}.");
			return true;
		}
		if (t.Modo == Modo.NaoPortada)
		{
			Avisar(pl, $"{t.Nome}: voce sabe a tecnica, mas o efeito dela ainda nao foi portado.");
			return true;
		}
		Avisar(pl, $"{t.Nome} ainda nao tem efeito.");
		return true;
	}

	/// <summary>
	/// Alguma skill aprendida destrava este verb?
	///
	/// ============================ CORPO SEM LIVRO E UM ESTADO LEGITIMO ============================
	/// `ServerPlayer.Livro` nasce `null!` e so e preenchido por quem passa pelo login ou pelo clone --
	/// um corpo forjado por bancada, ou um NPC cujo molde nao trouxe livro, tem NULO ali. O
	/// `TentarEmbate` do ZanzoClash ja sabia disso (`a.Livro?.Sabe(...)`) e a bancada da IA chega a
	/// NULAR o livro de proposito pra provar que um corpo quebrado nao derruba o servidor.
	///
	/// Isto aqui era seguro por ACIDENTE: as duas funcoes so eram chamadas por verb de jogador. Com a
	/// tabela de tecnicas de longe preenchida, o `ArsenalDeLonge` passou a varrer o livro de TODO
	/// corpo dirigido, 1 vez por segundo -- e a primeira coisa que a bancada da colisao de ki viu foi
	/// um `NullReferenceException` aqui. Em jogo o estrago seria pior que um teste vermelho: o `try`
	/// por corpo do `TicarUmCorpo` engoliria a excecao e o NPC viraria estatua, com o defeito a tres
	/// arquivos de distancia da causa.
	/// ==========================================================================================
	/// </summary>
	private bool SabeTecnica(ServerPlayer pl, string verb)
	{
		if (_skills == null || pl.Livro == null) return false;
		foreach (string path in pl.Livro.Aprendidas)
		{
			Skill? s = _skills.Get(path);
			if (s != null && s.Verbos.Contains(verb, StringComparer.OrdinalIgnoreCase)) return true;
		}

		// ============================ E OS DEGRAUS DE NIVEL, QUE NINGUEM LIA ============================
		// COMPRAR a skill nao e o unico jeito de destravar uma habilidade: o `effector()` do DM concede
		// verbs por NIVEL (`assignverb` dentro do degrau), e o extrator ja trazia isso -- `niveis.json`
		// tem `Basic_Blast` no nivel 35 de `Ki_Unlocked` e `Guided_Ball` no nivel 30 de
		// `Basic_Ki_Control`. `NiveisDeSkill.VerbosAtivos()` foi escrita pra responder exatamente essa
		// pergunta e **nao tinha um unico chamador**.
		//
		// O efeito era invisivel porque nada dependia dela ate a camada 1 dos ataques de ki: dois dos
		// tres verbs que atiram sao concedidos POR NIVEL, entao um jogador que subisse `Ki_Unlocked`
		// ate 35 continuaria ouvindo "voce nao sabe Bola de Ki" -- e a tecnica nao apareceria no menu,
		// porque o menu sai do `TecnicasDe`, que tinha o mesmo buraco.
		//
		// Quem achou foi a bancada da IA, e por um caminho torto: ela afirma que um corpo que aprendeu
		// o catalogo INTEIRO sai com as tres tecnicas de longe, e ele saia com UMA. E a quarta vez que
		// este projeto encontra dado extraido sem consumidor.
		// ==========================================================================================
		foreach (string v in pl.Niveis.VerbosAtivos(path => CasaEscolhidaDe(pl, path)))
			if (string.Equals(v, verb, StringComparison.OrdinalIgnoreCase)) return true;

		return false;
	}

	/// <summary>
	/// A CASA ESCOLHIDA numa skill de escolha unica ("Van-sama"), ou nulo -- o que o verb POR CASA de um
	/// degrau pergunta (`Degrau.VerbosPorCasa`: o Taunt/Counter_Taunt/Slap do degrau 2 da Trindade). A
	/// resolucao e a do `EfeitosDeSkill` (propria ou herdada da lider), pra que o degrau e a Grace nunca
	/// discordem sobre em que casa o jogador esta.
	/// </summary>
	private string? CasaEscolhidaDe(ServerPlayer pl, string path)
		=> _skills == null || pl.Livro == null ? null : EfeitosDeSkill.RotuloDaCasa(_skills, pl.Livro.Escolhas, path);

	/// <summary>
	/// As tecnicas ATIVAS que este personagem tem -- pro menu do cliente e pro arsenal da IA.
	///
	/// A GUARDA DE LIVRO NULO E A MESMA DO <see cref="SabeTecnica"/>, e pela mesma razao: desde que a
	/// tabela de tecnicas de longe deixou de estar vazia, esta funcao roda pra todo corpo dirigido.
	/// </summary>
	private List<string> TecnicasDe(ServerPlayer pl)
	{
		var l = new List<string>();
		if (_skills == null || pl.Livro == null) return l;
		foreach (string path in pl.Livro.Aprendidas)
		{
			Skill? s = _skills.Get(path);
			if (s == null) continue;
			foreach (string v in s.Verbos) if (!l.Contains(v)) l.Add(v);
		}

		// OS VERBS CONCEDIDOS POR NIVEL entram pela mesma porta -- ver o bloco no `SabeTecnica` sobre
		// por que esta chamada faltava. As duas listas tem que ser a MESMA: o que o menu mostra e o
		// que o `SabeTecnica` aceita, senao o botao existe e o servidor diz nao (ou o contrario).
		foreach (string v in pl.Niveis.VerbosAtivos(path => CasaEscolhidaDe(pl, path))) if (!l.Contains(v)) l.Add(v);

		return l;
	}
}
