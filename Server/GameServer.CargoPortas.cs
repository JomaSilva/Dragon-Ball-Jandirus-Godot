using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DOS CARGOS QUE NAO SE REIVINDICAM -- nomeacao, sucessao, e o que o cargo ENTREGA.
///
/// ============================ O BURACO QUE ESTE ARQUIVO FECHA ============================
/// O `GameServer.Ranks.cs` sabia fazer duas coisas: reivindicar (com requisito) e outorgar (admin).
/// Os outros 14 cargos da tabela declaravam a porta -- `Duelo`, `Sucessao`, `Nomeacao` -- e o
/// `OqueFalta` respondia com a frase honesta *"este cargo nao se reivindica"* e mais nada. Nenhum
/// deles tinha como trocar de dono no jogo.
///
/// Aqui entram DUAS das tres portas (o duelo mora no `GameServer.CargoDuelo.cs`, que e uma maquina
/// de estado com prazo) e a coisa que faltava pros trinta: a DADIVA -- as skills que o cargo
/// concede, que ate agora eram uma lista de texto sem nenhum leitor.
/// ======================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// 0. AS DUAS OPERACOES QUE TODA PORTA USA
	// =====================================================================

	/// <summary>Quem, entre os que estao no mundo agora, e o dono desta conta ("" = ninguem).</summary>
	private ServerPlayer? OnlinePorConta(string conta) =>
		conta.Length == 0
			? null
			: _players.Values.FirstOrDefault(
				p => EhPessoa(p) && string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// POE ALGUEM NO TRONO POR UMA PORTA QUE NAO E O REQUISITO.
	///
	/// Irmao do <see cref="Outorgar"/> e com as mesmas duas invariantes ("uma alma, um trono" dos
	/// dois lados; o mundo fica sabendo), mas com uma diferenca que so as portas novas precisam: o
	/// motivo (`via`) e ANUNCIADO ANTES da coroacao, como no DM -- la sao dois `bev_announce`
	/// separados (`GodOfDestruction.dm:508` e `:104`), porque "derrotou o Deus em duelo" e "e o novo
	/// Deus" sao dois fatos e o segundo sem o primeiro parece um presente.
	/// </summary>
	private void Entronizar(RankDef r, ServerPlayer pl, string via)
	{
		if (via.Length > 0)
			foreach (ServerPlayer o in _players.Values)
				Mandar(o, Protocol.Fala.Sistema, "", via, falante: null);

		string vagaVelha = VagaDaConta(pl.Conta);
		string largou = CargoDaVaga(vagaVelha);
		if (vagaVelha.Length > 0) _tronos.Remove(vagaVelha);

		// (2026-10-16) A VAGA LIVRE, se o cargo tiver (o Kaioshin, `RankDef.Vagas`); senao a primeira, e o dono dela sai.
		string vaga = VagaLivre(r) ?? r.Chave;
		string antigo = _tronos.TryGetValue(vaga, out string? d) ? d : "";

		_tronos[vaga] = pl.Conta;
		SalvarCargos();

		// O EX-DONO PERDE O KIT NA HORA. Sem esta linha ele so o perderia no proximo tique de
		// reconciliacao -- e num duelo pelo titulo isso e uma janela em que dois "Deuses" existem.
		if (antigo.Length > 0 && !string.Equals(antigo, pl.Conta, StringComparison.OrdinalIgnoreCase)
			&& OnlinePorConta(antigo) is { } velho)
		{
			Avisar(velho, $"voce nao e mais {r.Nome}.");
			ReconciliarDadiva(velho);
		}

		ReconciliarDadiva(pl);
		GD.Print($"[server] {pl.Name} ({pl.Conta}) assumiu '{r.Nome}' -- {(via.Length > 0 ? via : "porta especial")}");
		AnunciarCargo(r, pl, largou);
		AparaOPovoDoCargo(r.Chave);
	}

	/// <summary>
	/// TIRA O CARGO DE QUEM O CARREGA. **A outra metade do sistema**, e a que faltava inteira.
	///
	/// Ganhar cargo tinha tres caminhos e perder nao tinha nenhum: nem o admin conseguia esvaziar um
	/// trono. Isso importa mais do que parece -- destituicao e o unico mecanismo que faz um cargo com
	/// DEVERES significar alguma coisa, e e por onde a covardia do Deus da Destruicao e a negligencia
	/// dele cobram o preco (`god_lose_title`, `GodOfDestruction.dm:107`).
	///
	/// Devolve `false` quando o trono ja estava vago -- quem chama decide se isso e erro ou rotina.
	/// </summary>
	/// <param name="quem">(2026-10-16) A CONTA a destituir num cargo de varias vagas (o Kaioshin): a vaga DELA vaga, e so ela. Vazio
	/// (ou uma conta que nao carrega o cargo) = a primeira vaga ocupada -- o "dono do cargo" de sempre.</param>
	private bool Destronar(string chave, string motivo, string quem = "")
	{
		RankDef? r = Cargos.Get(chave);
		if (r == null) return false;
		string vaga = quem.Length > 0 && string.Equals(CargoDe(quem), r.Chave, StringComparison.OrdinalIgnoreCase)
			? VagaDaConta(quem)
			: VagasDe(r).FirstOrDefault(v => _tronos.TryGetValue(v, out string? x) && x.Length > 0) ?? "";
		if (vaga.Length == 0 || !_tronos.TryGetValue(vaga, out string? conta) || conta.Length == 0) return false;

		_tronos.Remove(vaga);
		SalvarCargos();

		// O TITULO EM DISPUTA TEM RELOGIO PROPRIO (carencia, adiamentos, tarefas), e ele nao pode
		// sobreviver ao dono: sem esta linha, o proximo Deus nasceria com as falhas do anterior.
		if (string.Equals(r.Chave, CargoEmDisputa, StringComparison.OrdinalIgnoreCase))
			LimparEstadoDoTitulo();

		if (OnlinePorConta(conta) is { } ex)
		{
			Avisar(ex, $"voce foi DESTITUIDO de {r.Nome}. ({motivo}) Todo o poder do cargo se esvai.");
			ReconciliarDadiva(ex);
		}

		GD.Print($"[server] '{r.Nome}' vagou: {motivo} (era de {conta})");
		foreach (ServerPlayer o in _players.Values)
		{
			Mandar(o, Protocol.Fala.Sistema, "",
				$"{NomeDoDono(conta)} perdeu o cargo de {r.Nome}. ({motivo}) O trono esta VAGO.", falante: null);
			MandarCargos(o);
		}
		return true;
	}

	// =====================================================================
	// 1. A DADIVA -- o que o cargo entrega, e o que ele leva de volta
	// =====================================================================

	/// <summary>
	/// PoE O LIVRO DE SKILLS DE ACORDO COM O CARGO QUE ESTA ALMA CARREGA AGORA.
	///
	/// ============================ POR QUE RECONCILIAR EM VEZ DE DAR/TIRAR NOS EVENTOS ============================
	/// Um cargo troca de dono por SEIS caminhos hoje (reivindicacao, outorga de admin, nomeacao,
	/// sucessao, duelo, destituicao) e vai trocar por mais quando as quests de cargo chegarem.
	/// Pendurar "da o kit" em cada um deles seria criar seis lugares onde a proxima pessoa liga um e
	/// esquece o outro -- que e, literalmente, o modo de falha mais repetido deste port (o mesmo
	/// argumento que o `AoPerderALuta` ja escreveu pros quatro pontos da derrota).
	///
	/// E tem um caminho que evento NENHUM cobre: **perder o cargo estando OFFLINE**. Quem foi
	/// destituido dormindo voltaria com o kit inteiro pra sempre.
	///
	/// Entao a funcao e IDEMPOTENTE e derivada: ela olha o cargo de agora e o livro de agora, e
	/// conserta a diferenca. Chamada no login, no tique e em toda troca de trono.
	/// ========================================================================================================
	/// </summary>
	/// <summary>
	/// O QUE SAI JUNTO COM UM CARGO, levantado UMA VEZ do catalogo. Ver
	/// <see cref="DadivaDeCargo.LevantarRevogaveis"/> -- a conta varre as 47 arvores e a resposta
	/// nao muda enquanto o servidor viver, entao faze-la no tique seria queimar trabalho de graca.
	/// </summary>
	private IReadOnlySet<string> SoDeCargo =>
		_soDeCargo ??= _skills == null
			? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			: DadivaDeCargo.LevantarRevogaveis(_skills);

	private HashSet<string>? _soDeCargo;

	/// <summary>
	/// (2026-10-23) A COPIA ENSINADA QUE NAO FICA, levantada uma vez pelo mesmo motivo do <see cref="SoDeCargo"/>. Ver
	/// <see cref="DadivaDeCargo.LevantarEnsinadasQueSaem"/>.
	/// </summary>
	private IReadOnlySet<string> EnsinadasQueSaem =>
		_ensinadasQueSaem ??= _skills == null
			? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			: DadivaDeCargo.LevantarEnsinadasQueSaem(_skills);

	private HashSet<string>? _ensinadasQueSaem;

	private void ReconciliarDadiva(ServerPlayer pl)
	{
		if (!EhPessoa(pl) || pl.Livro == null) return;

		// (2026-10-19) O APRENDIZ DE KAIOSHIN CAIDO, ANTES DE TUDO: o kit que ele comprou sai com os Marcos de volta -- se a reconciliacao
		// chegasse primeiro, apagaria a Cura sem devolver nada. Ver `AplicarAQuedaDoAprendiz`.
		AplicarAQuedaDoAprendiz(pl);

		// O KIT DO DEUS DA DESTRUICAO nao e skill de livro (ver `DadivaDeCargo`, que deixa o kit dele vazio de
		// proposito): o que entra e o que sai com o titulo e reconciliado por `GameServer.KitDoDeus.cs`, aqui,
		// pelo mesmo argumento do cabecalho deste metodo.
		ReconciliarOKitDoDeus(pl);

		string[] kit = DadivaDeCargo.De(CargoDe(pl.Conta));
		bool mudou = false;
		// (2026-10-19) O APRENDIZ DE KAIOSHIN COMPRA na arvore dele duas skills que sao de cargo (a Cura e o Ver Mortos): com o vinculo de
		// pe, o passo 1 nao as toca. Quando ele cai, quem as tira -- devolvendo os Marcos -- e o `DesfazerOAprendizado`.
		bool aprendiz = !AprendizDeKaioshin.KitApagadoDeTeste && EAprendizDeKaioshin(pl);

		// 1. O QUE SOBROU DE UM CARGO QUE ELE NAO TEM MAIS -- o que SO um cargo poderia ter dado, e
		//    que ninguem lhe ensinou (ou que lhe ensinaram e o ensino nao da mais, 2026-10-23). Ver
		//    `DadivaDeCargo.Revogavel` e o `treeshrink` do DM.
		foreach (string path in DadivaDeCargo.Todas)
		{
			if (kit.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
			if (aprendiz && AprendizDeKaioshin.Kit.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
			if (!pl.Livro.Sabe(path)) continue;
			if (!DadivaDeCargo.Revogavel(SoDeCargo, pl.Livro, path, EnsinadasQueSaem)) continue;
			// A LICAO QUE SAI SE EXPLICA: quem perde o cargo sabe por que o kit foi embora, mas quem so tinha aprendido a chave da
			// Sala com o Guardiao a veria sumir calada. (Copia ensinada que sai e so a da regra de 2026-10-23.)
			bool ensinada = pl.Livro.FoiEnsinada(path);
			pl.Livro.Esquecer(path);
			if (ensinada)
				Avisar(pl, $"{_skills?.Get(path)?.Nome ?? path} é skill de cargo e saiu do seu livro: só quem tem o cargo fica com ela.");
			mudou = true;
		}

		// 2. O QUE O CARGO DE AGORA DA.
		List<string>? chegaram = null;
		foreach (string path in kit)
		{
			if (pl.Livro.Sabe(path)) continue;

			// TYPEPATH QUE O CATALOGO NAO CONHECE NAO ENTRA. Uma skill guardada no livro sem entrada
			// no catalogo e invisivel e incontavel -- viraria peso morto no save de quem foi Kaio.
			if (_skills?.Get(path) == null) continue;
			pl.Livro.Dar(path);
			(chegaram ??= []).Add(path);
			mudou = true;
		}

		if (chegaram != null) ContarOQueOCargoDeu(pl, chegaram);

		// ============================ E O MISTICO -- QUE O CARGO NAO DA MAIS (2026-10-23) ============================
		// Ate 2026-10-23 a forma do Mistico DERIVAVA da skill `kai/Mystic` do kit: quem tinha a skill ganhava a forma, quem a
		// perdia a perdia. O dono trocou a skill pelo Ritual Mistico do Velho Kaiohshin, e a forma (o Potential Unleashed) virou um
		// DIREITO GRAVADO que ninguem tira por tique. O que sobra aqui e a migracao de quem ainda tem a skill no livro -- e ela entra
		// por esta porta pelo argumento do cabecalho desta funcao. Ver <see cref="ReconciliarODomDoMistico"/>.
		if (ReconciliarODomDoMistico(pl)) mudou = true;

		// O TRAJE DO DEUS DA DESTRUICAO (dono, 2026-10-11): quem ganha o titulo -- por qualquer porta -- recebe a
		// peca na mochila, uma vez por personagem. Ver `GameServer.Bills.ReconciliarOTrajeDoDeus`.
		ReconciliarOTrajeDoDeus(pl);

		// 3. AS INSIGNIAS -- o que o cargo entrega que nao e skill. Hoje sao os brincos Potara do
		//    Kaioshin, e eles entram AQUI (e nao num evento de "virou Kaioshin") exatamente pelo
		//    argumento do cabecalho desta funcao: seis caminhos trocam um cargo de dono, e um deles
		//    -- perder o cargo OFFLINE -- nao e evento nenhum. Ver `ReconciliarOsBrincos`.
		ReconciliarOsBrincos(pl);

		// ============================ 4. A LINGUA DOS DEUSES -- `RankAssign.dm:144` ============================
		// O DM chama `godtongue_check()` no fim do `Rank_Verb_Assign`, ou seja no instante em que se
		// GANHA um cargo. Ela entra AQUI pelo mesmo argumento do cabecalho desta funcao (seis caminhos
		// trocam um trono de dono, e ligar a regra em um e esquece-la nos outros e o modo de falha mais
		// repetido deste port) -- e porque ela e IDEMPOTENTE por construcao: `LinguaDosDeuses.Reavaliar`
		// so sabe LIGAR, entao passar por aqui mil vezes tem o mesmo efeito de passar uma.
		//
		// **E ELA FICA FORA DO `if (!mudou)` de proposito**: a lingua nao e um item do kit. Um Kaio
		// coroado cujo livro ja tivesse todas as skills do cargo sairia por aquele `return` e nunca
		// aprenderia a falar -- e o defeito seria invisivel, porque o cargo dele estaria perfeito.
		// =================================================================================================
		ConferirALingua(pl);

		if (!mudou) return;
		AplicarPoderes(pl);
		AplicarEfeitos(pl);

		// ESCREVER O CORTE NAO E APLICAR O CORTE. Sete das skills que saem daqui sao ESTILOS, e o
		// estilo vestido nao mora no livro: mora no `Ficha.EstiloAtual`, e os dez multiplicadores
		// dele ja estao escritos na ficha. Sem esta linha o ex-Eremita Tartaruga perde o
		// `KameStyle` do livro e continua lutando com os numeros dele ate trocar de postura --
		// que e exatamente o defeito que o sigilo de BP ja custou a este projeto uma vez.
		AplicarEstilo(pl);
		MandarEstilos(pl);

		MandarSkills(pl, forcar: true);
	}

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--v030teste`, familia D, e `--ritualkaiteste`): a forma volta a SEGUIR a skill -- quem a tem sem a
	/// skill no livro (o desejo do Namekuseijin, o ritual) a perde no tique seguinte, o defeito de antes de 2026-10-23. Lido pela linha 1
	/// do <see cref="ReconciliarODomDoMistico"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool FormaSegueASkillDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--ritualkaiteste`): a skill do Mistico que o cargo deu FICA no livro, e a forma com ela -- o Kaioshin
	/// de antes da regra continua com o Potential Unleashed sem ritual nenhum. Lido pela linha 3 do <see cref="ReconciliarODomDoMistico"/>.
	/// Falso em jogo, sempre.
	/// </summary>
	internal static bool MisticoDoCargoFicaDeTeste;

	/// <summary>
	/// ============================ A FORMA DO MISTICO E UM DIREITO GRAVADO (dono, 2026-10-23) ============================
	/// A HISTORIA: no DM a skill `/datum/skill/kai/Mystic` (`Race Trees/kai.dm:12-32`) ERA a forma -- o `after_learn()` dela da o verb
	/// que liga o buff (`Magic/Mystic.dm:9-18`), e o `before_forget()` o tira. Este port fazia o mesmo por DERIVACAO, aqui, a 1 Hz: quem
	/// tinha a skill (o kit do Kaioshin, uma copia ensinada, a heranca do bio) ganhava a forma, e quem tinha a forma SEM a skill a perdia
	/// no tique seguinte.
	///
	/// O DEFEITO QUE A DERIVACAO ESCONDIA: a forma dada SEM a skill -- a do desejo do Namekuseijin (`DesejoDePotencial`) -- sumia um
	/// segundo depois do desejo. As bancadas do desejo nao ticavam depois dele, e nao viram.
	///
	/// A REGRA DE AGORA (o dono): o Kaioshin perdeu a skill e ganhou o Ritual Mistico do Velho Kaiohshin (`GameServer.RitualDoKaioshin.cs`),
	/// e a forma -- o Potential Unleashed -- deixou de seguir a skill: e um DIREITO GRAVADO no `Forma.Liberadas` (que vai pro save), dado
	/// pelo ritual ou pelo desejo, e NINGUEM o tira por tique. O que sobra aqui e a MIGRACAO de quem ainda tem a skill, idempotente (roda a
	/// 1 Hz, como toda a reconciliacao, e so age enquanto a skill estiver no livro):
	///   1. SEM A SKILL, NADA A FAZER -- o caso de quase todo mundo, numa consulta ao livro;
	///   2. A COPIA ENSINADA FICA (o dono: "quem tem fica"): a forma ja e dela (a derivacao de antes a abriu), e a skill no livro nao faz
	///      mais nada -- nem se ensina (`EnsinoDeSkill.SoPeloRitual`);
	///   3. A COPIA QUE NINGUEM ENSINOU SAI -- a do kit de antes (ou um `Dar` de fusao) -- E LEVA A FORMA: *"o Kaioshin cuja forma vinha
	///      so do cargo a perde quando a skill sai"*. Ele volta a ela pelo ritual de OUTRO Kaioshin.
	///
	/// O RECUO E OBRIGATORIO: quem perde a forma ESTANDO nela sai dela primeiro, senao `AplicarForma` recalcularia com um id que o
	/// `Avaliar` ja recusa ("escrever o corte nao e aplicar o corte", o mesmo do `AplicarEstilo` da reconciliacao).
	/// ============================================================================================================================
	/// </summary>
	/// <returns>Se o livro mudou (a skill saiu) -- quem chama manda as skills de novo.</returns>
	private bool ReconciliarODomDoMistico(ServerPlayer pl)
	{
		if (pl.Livro == null) return false;
		// 1. A FORMA NAO SEGUE MAIS A SKILL: sem ela no livro, nada a reconciliar (o conserto do desejo que sumia).
		if (!pl.Livro.Sabe(RitualDoKaioshin.SkillDoMistico) && !FormaSegueASkillDeTeste) return false;
		// 2. A COPIA ENSINADA FICA, e a forma com ela.
		if (pl.Livro.FoiEnsinada(RitualDoKaioshin.SkillDoMistico)) return false;
		// 3. A SKILL QUE O CARGO DEU SAI -- e leva a forma.
		if (MisticoDoCargoFicaDeTeste) return false;
		bool sabia = pl.Livro.Sabe(RitualDoKaioshin.SkillDoMistico);
		pl.Livro.Esquecer(RitualDoKaioshin.SkillDoMistico);
		if (pl.Forma.Despertou(Jandirus.Core.Forms.Catalogo.IdMistico))
		{
			if (string.Equals(pl.Forma.Atual, Jandirus.Core.Forms.Catalogo.IdMistico, StringComparison.OrdinalIgnoreCase))
			{
				pl.Forma.Entrar(Jandirus.Core.Forms.Catalogo.IdBase);
				AplicarForma(pl);
			}
			pl.Forma.Liberadas.Remove(Jandirus.Core.Forms.Catalogo.Rede(Jandirus.Core.Forms.Catalogo.IdMistico));
			ReapresentarOCorpo(pl);   // o potencial na pele sai junto com a forma (`Catalogo.PotencialMarcaAPele`)
			Avisar(pl, "o cargo de Kaioshin não abre mais o Potential Unleashed: a forma que ele te deu se fecha. Agora ela só "
					   + "desperta pelo Ritual Místico do Velho Kaiohshin, feito por outro Kaioshin.");
			GD.Print($"[server] {pl.Name} PERDEU o POTENTIAL UNLEASHED (a skill do cargo saiu do livro: so o ritual a da agora)");
		}
		// (Sem `Persistir` aqui, como na derivacao de antes: a gravacao periodica e a do logout levam o livro e as formas pro disco --
		// e as bancadas que emprestam o `Peer` do host gravariam o corpo forjado por cima do personagem dele.)
		return sabia;
	}

	/// <summary>
	/// O CARGO DIZ O QUE DEU -- E DIZ O QUE ELE AINDA NAO FAZ.
	///
	/// ============================ POR QUE ISTO NAO E ENFEITE ============================
	/// Das 51 skills que os cargos entregam, **21 ainda nao tem corpo neste port** (eram 31 quando
	/// este bloco foi escrito; o lote G6 fechou quatro e o G8 fechou seis -- o numero e do
	/// `--censoteste`, que o mede toda rodada: *"dos que algum CARGO entrega (51 skills): 47 verbos,
	/// 26 com efeito"*) -- e o menu do cliente
	/// e desenhado a partir do `Tecnicas`, onde verbo mudo nao entra. O resultado, ate aqui, era o
	/// pior formato possivel de divida: o mundo anunciava o novo Kaio do Norte, o livro recebia a
	/// Genkidama, e no lado do jogador **nao acontecia nada**. Nenhum botao, nenhuma recusa, nenhuma
	/// mensagem. Ele nao tinha como distinguir "o jogo esta quebrado" de "isto ainda nao existe".
	///
	/// A regra 5 da casa manda falhar ALTO em vez de calar, e a regra 3 desta camada manda que o que
	/// depende de sistema inexistente fique DECLARADO e nao morra em silencio. O catalogo ja sabe a
	/// resposta pra cada verbo (`CensoDeSkills.SistemaQueFalta`, a mesma tabela que a bancada usa) --
	/// o que faltava era ela chegar em quem esta jogando.
	///
	/// **A LISTA SAI DO CENSO E NAO DE UMA SEGUNDA TABELA**, e isso e o ponto: no dia em que a
	/// Genkidama for portada, esta mensagem para de mencionar a Genkidama sozinha. Uma lista escrita
	/// a mao aqui continuaria pedindo desculpa por uma tecnica que ja funciona.
	/// ==================================================================================
	/// </summary>
	private void ContarOQueOCargoDeu(ServerPlayer pl, List<string> chegaram)
	{
		if (_skills == null) return;

		List<string> prontas = [], faltando = [];
		var sistemas = new List<string>();
		foreach (string path in chegaram)
		{
			Skill? s = _skills.Get(path);
			if (s == null || s.Nome.Length == 0) continue;

			string? falta = CensoDeSkills.SistemaQueFalta(s);
			if (falta == null) { prontas.Add(s.Nome); continue; }
			faltando.Add(s.Nome);
			if (!sistemas.Contains(falta, StringComparer.OrdinalIgnoreCase)) sistemas.Add(falta);
		}

		if (prontas.Count > 0) Avisar(pl, $"o cargo te entrega: {string.Join(", ", prontas)}.");

		// O RECADO HONESTO. Ele nomeia o SISTEMA e nao pede desculpa generica -- "falta a barragem
		// sustentada" e uma informacao; "em breve" nao e.
		if (faltando.Count > 0)
			Avisar(pl, $"o cargo tambem te dá {string.Join(", ", faltando)} -- mas isso ainda não "
					 + $"funciona neste servidor: falta {string.Join("; ", sistemas)}.");
	}

	/// <summary>
	/// O TIQUE DOS CARGOS -- 1 Hz, junto do resto do bloco lento.
	///
	/// Ele existe pra UMA coisa: a reconciliacao acima nao ter que ser lembrada por quem escrever a
	/// setima porta de cargo. Custa um `CargoDe` por pessoa por segundo (uma varredura de um
	/// dicionario de no maximo 30 entradas) e devolve a garantia de que kit e trono nunca divergem
	/// por mais de um segundo -- inclusive quando quem mexeu no trono foi outro sistema.
	/// </summary>
	private void TickDosCargos()
	{
		if (ValidacoesDeTeste) DispararABancadaDasValidacoes();   // a bancada `--validacoesteste` (so com a flag) -- ver `GameServer.ValidacoesTeste.cs`

		foreach (ServerPlayer pl in _players.Values)
			if (EhJogador(pl)) ReconciliarDadiva(pl);

		// (2026-10-19) OS VINCULOS DE APRENDIZ DE KAIOSHIN: o que caiu com o mestre fora do trono, e o convite vencido. Ver
		// `GameServer.AprendizDeKaioshin.cs`.
		TickDosAprendizes();

		// (2026-10-23) O RITUAL MISTICO DO VELHO KAIOHSHIN: o que o quebra, os minutos no chat e o fim. Ver `GameServer.RitualDoKaioshin.cs`.
		TickDosRituais();

		TickDoDuelo();

		// OS DEVERES DOS 17 CARGOS COM TAREFA. Ele entra AQUI e nao no bloco de 1 Hz de fora pelo
		// mesmo motivo do duelo: quem manda no passo e o relogio do MUNDO, e este metodo so pergunta
		// se ja deu a hora (uma volta por minuto -- ver `TickDasMissoes`). Ver `GameServer.CargoMissoes.cs`.
		TickDasMissoes();

		// (2026-10-19) O TRONO DE QUEM SUMIU: 7 dias de servidor no ar sem o dono entrar e o cargo vaga (menos o Anjo). Uma volta a
		// cada 10 min. Ver `GameServer.CargoLivre.cs`.
		TickDoAbandono();

		// (2026-10-19) O JUIZ DA PROVA DO ESPIRITO DO CARGO: recrava o espirito e decide quem venceu. Ver `GameServer.ProvaDoCargo.cs`.
		TickDasProvas();
	}

	// =====================================================================
	// 2. NOMEACAO -- os quatro Anciaos de Namek
	// =====================================================================

	/// <summary>
	/// Quem foi convidado a ser Anciao: conta do convidado -> (assento, conta de quem nomeou).
	///
	/// NA MEMORIA e nao em disco, como o pedido de amizade: um convite pendente nao e patrimonio, e
	/// um servidor que reiniciou nao tem como o Grande Anciao continuar esperando a resposta.
	/// </summary>
	private readonly Dictionary<string, (string Assento, string Nomeador)> _convitesDeAnciao =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>`Appoint_Elder(var/mob/M in view(1))` (`ordered/NamekRanks.dm:56`): o convidado esta no tile vizinho.</summary>
	private const int TilesDeNomear = 1;

	/// <summary>`as mob in view(10)` do `Vegeta Managament` (`rankSkills/SpaceRankSkills.dm:52`): o Rei escolhe entre quem ele ve.</summary>
	private const int TilesDeHerdeiro = 10;

	/// <summary>
	/// `Appoint_Elder` (`ordered/NamekRanks.dm:56`), com as duas correcoes que o Core documenta
	/// (a chave e a CONTA; o alvo precisa de sangue de Namek e nao de ja ser Anciao).
	///
	/// O ALVO VEM MARCADO (duplo clique), como todo verb com alvo neste port -- e o equivalente
	/// honesto do `in view(1)` do BYOND. O assento e opcional: sem ele, o primeiro vago.
	///
	/// E O `view(1)` E CONFERIDO AQUI (<see cref="AlvoAoAlcance"/>): o id da mira e so o que o cliente MANDA, e
	/// ate 2026-10-10 o servidor o aceitava de qualquer planeta, pelo id ou pelo nome -- um cliente mexido
	/// nomeava Anciao quem nunca viu o Grande Anciao. Nomear e um gesto presencial.
	/// </summary>
	private void VerboNomearAnciao(ServerPlayer pl, string arg)
	{
		string[] p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		ServerPlayer? alvo = AlvoAoAlcance(pl, p.Length > 0 ? p[0] : "", TilesDeNomear, out ServerPlayer? longe);
		if (longe != null) { Avisar(pl, FraseDeLonge(longe)); return; }
		if (alvo == null || !EhPessoa(alvo))
		{
			Avisar(pl, "marque o Namekuseijin que voce quer nomear (duplo clique nele).");
			return;
		}

		string assento = p.Length > 1 ? AssentoPorApelido(p[1]) : PrimeiroAssentoVago();
		if (assento.Length == 0)
		{
			Avisar(pl, "os quatro assentos de Anciao tem dono. Diga qual deles voce quer trocar "
					 + "(norte, sul, leste ou oeste).");
			return;
		}

		string? nao = Nomeacao.PorQueNao(CargoDe(pl.Conta), assento, alvo.Race,
										 alvo.Ficha.Genoma?.MajorityRace ?? alvo.Race, CargoDe(alvo.Conta));
		if (nao != null) { Avisar(pl, $"nao da: {nao}."); return; }

		_convitesDeAnciao[alvo.Conta] = (assento, pl.Conta);
		Avisar(pl, $"voce oferece a {alvo.Name} o assento de {Cargos.Get(assento)?.Nome ?? assento}.");
		Avisar(alvo, $"{pl.Name}, o Grande Anciao de Namek, te oferece o assento de "
				   + $"{Cargos.Get(assento)?.Nome ?? assento}. (aceite ou recuse na aba Other)");
	}

	/// <summary>
	/// A RESPOSTA DO CONVIDADO -- o segundo `alert()` do DM (`NamekRanks.dm:60`), que ali era
	/// bloqueante e aqui e um verb, como o pedido de amizade.
	///
	/// OS REQUISITOS SAO CONFERIDOS DE NOVO. Entre o convite e a resposta o Grande Anciao pode ter
	/// perdido o cargo, e o convidado pode ter assumido outro -- e aceitar um convite velho nao pode
	/// abrir uma porta que o mundo ja fechou.
	/// </summary>
	private void VerboResponderNomeacao(ServerPlayer pl, bool aceitou)
	{
		if (!_convitesDeAnciao.TryGetValue(pl.Conta, out (string Assento, string Nomeador) c))
		{
			Avisar(pl, "ninguem te ofereceu um assento de Anciao.");
			return;
		}
		_convitesDeAnciao.Remove(pl.Conta);

		if (!aceitou)
		{
			Avisar(pl, "voce recusa o assento.");
			if (OnlinePorConta(c.Nomeador) is { } n) Avisar(n, $"{pl.Name} recusou o assento.");
			return;
		}

		string? nao = Nomeacao.PorQueNao(CargoDe(c.Nomeador), c.Assento, pl.Race,
										 pl.Ficha.Genoma?.MajorityRace ?? pl.Race, CargoDe(pl.Conta));
		if (nao != null) { Avisar(pl, $"o assento se fechou: {nao}."); return; }

		RankDef? r = Cargos.Get(c.Assento);
		if (r == null) return;
		Entronizar(r, pl, $"O Grande Anciao de Namek nomeia {pl.Name} {r.Nome}.");
	}

	private static string AssentoPorApelido(string s) => s.ToLowerInvariant() switch
	{
		"norte" or "north" or "n" => "northelder",
		"sul" or "south" or "s" => "southelder",
		"leste" or "east" or "l" or "e" => "eastelder",
		"oeste" or "west" or "o" or "w" => "westelder",
		_ => Nomeacao.EhAssento(s) ? s : "",
	};

	private string PrimeiroAssentoVago() =>
		Array.Find(Nomeacao.Assentos,
			a => !_tronos.TryGetValue(a, out string? d) || d.Length == 0) ?? "";

	// =====================================================================
	// 3. SUCESSAO -- o trono passa a quem mata
	// =====================================================================

	/// <summary>
	/// A LINHA DE SANGUE DE VEGETA: contas nomeadas herdeiras pelo Rei, na ordem em que ele as
	/// nomeou. E o `vegeta_royalty` do DM (`RankAssign.dm:255`), preenchido pelo `Vegeta Managament`
	/// (`rankSkills/SpaceRankSkills.dm:54-88`).
	///
	/// EM DISCO, ao lado do `cargos.txt`: quem e Principe e um fato do MUNDO e nao da sessao -- e sem
	/// persistir, a unica coisa que a sucessao faria e vagar o trono depois de um reinicio.
	/// </summary>
	private readonly List<string> _herdeirosDeVegeta = [];

	/// <summary>Pelo `_store` e nao pelo caminho cravado -- ver o porque em `GameServer.Ranks.cs`.</summary>
	private string ArquivoDeHerdeiros =>
		System.IO.Path.Combine(_store?.Pasta ?? ProjectSettings.GlobalizePath("user://saves"), "herdeiros.txt");

	private void CarregarHerdeiros()
	{
		_herdeirosDeVegeta.Clear();
		if (!System.IO.File.Exists(ArquivoDeHerdeiros)) return;
		foreach (string linha in System.IO.File.ReadAllLines(ArquivoDeHerdeiros))
			if (linha.Trim().Length > 0) _herdeirosDeVegeta.Add(linha.Trim());
	}

	private void SalvarHerdeiros()
	{
		try { System.IO.File.WriteAllLines(ArquivoDeHerdeiros, _herdeirosDeVegeta); }
		catch (Exception e) { GD.PushWarning($"[server] nao gravei os herdeiros: {e.Message}"); }
	}

	/// <summary>
	/// O Rei nomeia (ou dispensa) um herdeiro. So o Rei, e so sobre quem esta marcado -- e A VISTA dele: o
	/// `Vegeta Managament` escolhe o mob `in view(10)` (`SpaceRankSkills.dm:52`), pra promover e pra rebaixar.
	/// Ver <see cref="AlvoAoAlcance"/>.
	/// </summary>
	private void VerboHerdeiro(ServerPlayer pl, string arg, bool nomear)
	{
		if (!string.Equals(CargoDe(pl.Conta), Sucessao.ReiDeVegeta, StringComparison.OrdinalIgnoreCase))
		{
			Avisar(pl, "so o Rei de Vegeta decide quem herda o trono.");
			return;
		}
		ServerPlayer? alvo = AlvoAoAlcance(pl, arg, TilesDeHerdeiro, out ServerPlayer? longe);
		if (longe != null) { Avisar(pl, FraseDeLonge(longe)); return; }
		if (alvo == null || !EhPessoa(alvo))
		{
			Avisar(pl, "marque alguem antes (duplo clique nele).");
			return;
		}

		if (!nomear)
		{
			if (_herdeirosDeVegeta.RemoveAll(c => string.Equals(c, alvo.Conta, StringComparison.OrdinalIgnoreCase)) == 0)
			{
				Avisar(pl, $"{alvo.Name} nao era da linha de sucessao.");
				return;
			}
			SalvarHerdeiros();
			Avisar(pl, $"{alvo.Name} sai da linha de sucessao de Vegeta.");
			Avisar(alvo, "o Rei te retirou da linha de sucessao.");
			return;
		}

		// O REQUISITO E O DA HERANCA, e ele e conferido AQUI e nao so na hora da morte: nomear um
		// Humano Principe de Vegeta cria uma promessa que o `Murder.dm:91` nunca cumpriria.
		if (!Sucessao.HerdeiroServe(alvo.Race, alvo.Ficha.Genoma?.MajorityRace ?? alvo.Race, alvo.Ficha.dead))
		{
			Avisar(pl, "so um Saiyajin vivo entra na linha de sucessao de Vegeta.");
			return;
		}
		if (_herdeirosDeVegeta.Contains(alvo.Conta, StringComparer.OrdinalIgnoreCase))
		{
			Avisar(pl, $"{alvo.Name} ja e da linha de sucessao.");
			return;
		}

		_herdeirosDeVegeta.Add(alvo.Conta);
		SalvarHerdeiros();
		Avisar(pl, $"{alvo.Name} entra na linha de sucessao do trono de Vegeta.");
		Avisar(alvo, $"{pl.Name} te nomeia herdeiro do trono de Vegeta.");
	}

	/// <summary>
	/// MORREU CARREGANDO UM TRONO. Chamado do <see cref="AoPerderALuta"/> com `morreu: true` -- o
	/// funil unico das consequencias de derrota, que ja documenta que a proxima consequencia entra
	/// ali e chega nos quatro pontos de graca.
	///
	/// A ORDEM E A DO `killer_stuff` (`Murder.dm:85-101`): quem mata leva, se for do sangue; nao
	/// sendo, o herdeiro; nao havendo, o trono VAGA.
	/// </summary>
	private void SucessaoPorMorte(ServerPlayer vitima, ServerPlayer algoz)
	{
		if (!EhPessoa(vitima) || !EhPessoa(algoz)) return;
		if (string.Equals(vitima.Conta, algoz.Conta, StringComparison.OrdinalIgnoreCase)) return;

		string cargo = CargoDe(vitima.Conta);
		if (cargo.Length == 0 || !Sucessao.TemSucessao(cargo)) return;
		RankDef? r = Cargos.Get(cargo);
		if (r == null) return;

		// 1. O ALGOZ E DO SANGUE? Toma o trono na hora.
		if (Sucessao.AlgozHerda(cargo, algoz.Race, algoz.Ficha.Genoma?.MajorityRace ?? algoz.Race, algoz.Class))
		{
			Entronizar(r, algoz, $"{algoz.Name} matou {vitima.Name} e tomou o trono de {r.Nome}.");
			return;
		}

		// 2. A LINHA DE SANGUE. So o trono de Vegeta tem uma.
		if (Sucessao.TemHerdeiros(cargo))
			foreach (string conta in _herdeirosDeVegeta)
			{
				ServerPlayer? h = OnlinePorConta(conta);
				if (h == null) continue;   // herdeiro offline nao assume: o DM tambem varre mobs no mundo
				if (!Sucessao.HerdeiroServe(h.Race, h.Ficha.Genoma?.MajorityRace ?? h.Race, h.Ficha.dead)) continue;
				Entronizar(r, h, $"{r.Nome} foi assassinado. {h.Name} herda o trono por direito de sangue.");
				return;
			}

		// 3. NINGUEM: o trono VAGA (`else King_of_Vegeta=null`).
		Destronar(cargo, $"o portador foi morto por {algoz.Name} e nao havia herdeiro", vitima.Conta);
	}

	// =====================================================================
	// 4. O CANAL DOS CARGOS (RankChat) e o roteador dos verbs
	// =====================================================================

	/// <summary>
	/// `RankChat` (`RankTree.dm:14-31`) -- a unica skill que TODO cargo tem no original.
	///
	/// Ela entra aqui porque e a peca que faz a dadiva parar de ser promessa no primeiro minuto: a
	/// skill que os trinta cargos concedem passa a ter corpo, e nao um botao mudo. Alcance: todo
	/// mundo que carrega um cargo, onde quer que esteja -- e o ponto do canal.
	/// </summary>
	private void VerboFalarAosCargos(ServerPlayer pl, string texto)
	{
		if (CargoDe(pl.Conta).Length == 0) { Avisar(pl, "esse canal e de quem carrega um cargo."); return; }

		// E UMA FALA, E PASSA PELO QUE TODA FALA PASSA (`Falar`, `GameServer.Chat.cs`): o calado nao fala, o texto
		// perde as quebras de linha (o `html_encode(msg)` do `RankChat`, `RankTree.dm:148-152` -- com "\n" dava
		// pra desenhar linhas falsas de SISTEMA no chat de todo portador de cargo) e ha o mesmo intervalo entre
		// duas falas (um pacote vira uma mensagem confiavel por portador: sem freio, e um megafone).
		if (!CanalDosCargosCruDeTeste)
		{
			if (EstaCalado(pl)) { Avisar(pl, "voce esta calado."); return; }
			texto = Limpar(texto);
		}
		if (texto.Trim().Length == 0) { Avisar(pl, "escreva a mensagem."); return; }
		if (!CanalDosCargosCruDeTeste)
		{
			long agora = NowMs();
			if (_ultimaFala.TryGetValue(pl.Id, out long antes) && agora - antes < MsEntreFalas) return;
			_ultimaFala[pl.Id] = agora;
		}
		EscutaDeFalas?.Add((pl.Id, Protocol.Fala.Sistema, texto));   // o gancho de bancada de toda fala (nulo em jogo)

		string meu = Cargos.Get(CargoDe(pl.Conta))?.Nome ?? "";
		int ouviram = 0;
		foreach (ServerPlayer o in _players.Values)
		{
			if (o.Peer == null || !EhPessoa(o) || CargoDe(o.Conta).Length == 0) continue;
			Mandar(o, Protocol.Fala.Sistema, "", $"[cargos] {pl.Name} ({meu}): {texto}", falante: null);
			ouviram++;
		}
		if (ouviram <= 1) Avisar(pl, "ninguem mais com cargo esta no mundo agora.");
	}

	/// <summary>
	/// Os verbs das portas. Devolve `true` quando o comando era daqui -- inclusive quando foi
	/// RECUSADO, pela mesma razao do `ComandoDeConvivio`: senao o funil segue e o jogador le
	/// "comando desconhecido" depois de ja ter lido o motivo de verdade.
	/// </summary>
	private bool ComandoDeCargo(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "cargo_nomear": VerboNomearAnciao(pl, arg); return true;
			case "cargo_nomear_aceitar": VerboResponderNomeacao(pl, aceitou: true); return true;
			case "cargo_nomear_recusar": VerboResponderNomeacao(pl, aceitou: false); return true;

			// (2026-10-19) o patronato do Kaioshin -- ver `GameServer.AprendizDeKaioshin.cs`. A resposta mora aqui pelo motivo da juventude
			// de baixo: quem responde e o convidado, que nao tem a skill.
			case AprendizDeKaioshin.VerboNomear: VerboNomearAprendiz(pl, arg); return true;
			case AprendizDeKaioshin.VerboDispensar: VerboDispensarAprendiz(pl, arg); return true;
			case AprendizDeKaioshin.VerboAceitar: VerboResponderAprendizado(pl, aceitou: true); return true;
			case AprendizDeKaioshin.VerboRecusar: VerboResponderAprendizado(pl, aceitou: false); return true;

			case "cargo_herdeiro": VerboHerdeiro(pl, arg, nomear: true); return true;
			case "cargo_herdeiro_tirar": VerboHerdeiro(pl, arg, nomear: false); return true;
			case "cargo_herdeiros": VerboVerHerdeiros(pl); return true;

			case "cargo_falar": VerboFalarAosCargos(pl, arg); return true;

			// A RESPOSTA A OFERTA DE JUVENTUDE (`Restore_Youth`, lote G8). Ela mora no roteador dos
			// cargos e nao no despacho de tecnicas porque quem responde NAO tem a skill -- e o alvo da
			// oferta, nao o dono do dom. Mandar isto pelo funil de tecnica faria o `SabeTecnica`
			// recusar todo mundo que a tecnica existe pra atender.
			case "juventude_aceitar": ResponderJuventudeG8(pl, aceitou: true); return true;
			case "juventude_recusar": ResponderJuventudeG8(pl, aceitou: false); return true;

			// A RESPOSTA A OFERTA DE DESPERTAR O POTENCIAL (`Unlock_Potential`, lote G11) -- o mesmo
			// molde da juventude, pelo mesmo motivo: quem responde e o ALVO, que nao tem a skill.
			case "potencial_aceitar": ResponderPotencialG11(pl, aceitou: true); return true;
			case "potencial_recusar": ResponderPotencialG11(pl, aceitou: false); return true;

			// os deveres moram no `GameServer.CargoMissoes.cs` -- o roteador continua sendo um so
			case "cargo_deveres": VerboMeusDeveres(pl); return true;
			case "cargo_verba": VerboDepositarVerba(pl); return true;

			// o duelo mora no arquivo vizinho, mas o roteador e um so
			case "cargo_desafiar": VerboDesafiar(pl); return true;
			case "cargo_duelo_aceitar": VerboResponderDesafio(pl, aceitou: true); return true;
			case "cargo_duelo_adiar": VerboResponderDesafio(pl, aceitou: false); return true;
			case "cargo_titulo": VerboStatusDoTitulo(pl); return true;

			default: return false;
		}
	}

	private void VerboVerHerdeiros(ServerPlayer pl)
	{
		if (_herdeirosDeVegeta.Count == 0) { Avisar(pl, "o trono de Vegeta nao tem linha de sucessao."); return; }
		Avisar(pl, "-- linha de sucessao de Vegeta --");
		int i = 1;
		foreach (string c in _herdeirosDeVegeta) Avisar(pl, $"  {i++}. {NomeDoDono(c)}");
	}
}
