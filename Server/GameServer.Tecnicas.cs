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
		long agora = NowMs();
		if (_solarPronto.TryGetValue(pl.Id, out long pronto) && agora < pronto)
		{
			Avisar(pl, $"a tecnica ainda se recompoe ({(pronto - agora) / 1000.0:0.#}s).");
			return;
		}

		double custo = Tecnicas.SolarCustoKi(pl.Ficha);
		if (pl.Ficha.Ki < custo) { Avisar(pl, $"isso pede pelo menos {custo:0} de energia."); return; }
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "voce nao esta em condicoes."); return; }

		pl.Ficha.Ki -= custo;
		_solarPronto[pl.Id] = agora + Tecnicas.SolarRecargaMs(pl.Ficha);
		pl.Ficha.kidebuffskill += 0.4;   // `usr.kidebuffcounter += 4` -- usar treina

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

	/// <summary>
	/// O DESPACHO das tecnicas ativas, pelo id do verb do DM.
	///
	/// A tecnica que o jogador NAO sabe e recusada aqui, nao no cliente: o botao pode nem existir
	/// na tela, mas quem manda o pacote na mao tem que ouvir nao.
	/// </summary>
	private bool UsarTecnica(ServerPlayer pl, string id)
	{
		// OS LOTES PORTADOS VEM PRIMEIRO, e a ordem nao e gosto: eles aceitam ids com ARGUMENTO
		// ("Kaioken_20", "DirectSSJ:2", "Final_Explosion_25"), e o gate generico logo abaixo
		// pergunta `SabeTecnica(id)` -- que compara o id INTEIRO com os verbs da skill e nunca
		// casaria com a variante. Passando antes, cada lote confere o verb BASE por conta propria.
		if (UsarTecnicasG1(pl, id)) return true;
		if (UsarTecnicasG2(pl, id)) return true;
		if (UsarTecnicasG3(pl, id)) return true;
		if (UsarTecnicasG4(pl, id)) return true;

		// AS TECNICAS QUE O JOGADOR INVENTOU entram AQUI, antes do gate generico, e pelo mesmo
		// motivo dos lotes acima: `SabeTecnica` pergunta se alguma SKILL destrava este verbo, e
		// nenhuma skill destrava `Custom_Attack7` -- no DM os verbos custom sao concedidos pelo
		// `after_learn()` do proprio datum (`:122-127`), sem `assignverb` e sem arvore. Passando
		// depois do gate, toda tecnica inventada ouviria "voce nao sabe" -- sobre uma tecnica que
		// o jogador desenhou com a propria mao.
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

		switch (id)
		{
			case "Solar_Flare": SolarFlare(pl); break;
			case "Invisibility": Invisibilidade(pl); break;
			case "Ki_Shield": KiShield(pl); break;
			case "Regenerate": Regenerar(pl); break;
			case "Space_Flight": Decolar(pl); break;

			// OS TRES QUE ATIRAM entram AQUI e nao num lote proprio antes do gate, de proposito:
			// eles nao aceitam id com argumento (nao ha "Basic_Blast:3"), entao o `SabeTecnica` logo
			// acima ja e a porta certa -- e assim quem nao comprou a skill ouve "voce nao sabe" pelo
			// mesmo caminho de todo mundo, em vez de cada tecnica repetir a recusa.
			case "Ki_Wave":
			case "Basic_Blast":
			case "Guided_Ball": UsarTecnicasDeProjetil(pl, id); break;

			// A UNICA TECNICA SO-DE-VILAO do catalogo. Entra aqui e nao num lote proprio porque ela
			// tambem nao aceita id com argumento -- a caixa "Sim/Nao" do original virou os trinta
			// segundos de carga, que sao publicos. Ver `GameServer.Destruicao.cs`.
			case "Planet_Destroy": PlanetDestroy(pl); break;

			// O ARSENAL NOMEADO (lote G5): catorze verbos de ki que uma skill ja concedia e que o
			// servidor nao atendia. Entram pelo `default` e nao por catorze `case` porque a lista
			// deles mora no proprio lote (`IdsG5`) -- acrescentar a decima quinta nao deve mexer
			// neste arquivo. Nenhum aceita argumento, entao o `SabeTecnica` la em cima ja e o
			// portao, igual aos tres de projetil.
			default:
				if (EhDoLoteG5(id)) UsarTecnicasG5(pl, id);
				// O LOTE G6 entra pela mesma porta e pelo mesmo motivo: a lista dos dezenove mora no
				// proprio lote (`IdsG6`), entao portar o vigesimo nao deve mexer neste arquivo.
				else if (EhDoLoteG6(id)) UsarTecnicasG6(pl, id);
				// E O LOTE G7 pela terceira vez pela mesma porta: catorze punhos nomeados e as duas
				// bolas que faltavam. A lista mora no proprio lote (`IdsG7`).
				else if (EhDoLoteG7(id)) UsarTecnicasG7(pl, id);
				else Avisar(pl, $"{t.Nome} ainda nao tem efeito.");
				break;
		}
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
		foreach (string v in pl.Niveis.VerbosAtivos())
			if (string.Equals(v, verb, StringComparison.OrdinalIgnoreCase)) return true;

		return false;
	}

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
		foreach (string v in pl.Niveis.VerbosAtivos()) if (!l.Contains(v)) l.Add(v);

		return l;
	}
}
