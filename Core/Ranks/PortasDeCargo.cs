namespace Jandirus.Core.Ranks;

// =====================================================================================
// AS TRES PORTAS QUE A TABELA DE CARGOS DECLARAVA E NINGUEM ABRIA.
//
// `PortaDoCargo` (Ranks.cs:146) ja nomeava as cinco entradas de um cargo, e o `OqueFalta`
// respondia as tres ultimas com a MESMA frase: *"este cargo nao se reivindica"*. Era honesto e era
// tudo -- 14 dos 30 cargos nao tinham como ser conquistados por ninguem, e 6 deles (os 4 Anciaos,
// o Rei de Vegeta e o Deus da Destruicao) nem por admin faziam sentido, porque o original tem
// mecanismo escrito pra cada um.
//
// Este arquivo e o CORE dos tres: Nomeacao, Sucessao e Duelo. Sao regras puras -- quem tem conta,
// quem esta online, quem esta caido e quem grava em disco e o servidor. Aqui so mora o que decide.
//
// POR QUE NUM ARQUIVO SO: as tres respondem a mesma pergunta ("por onde este trono troca de dono")
// e as tres foram escritas no mesmo dia a partir dos mesmos tres arquivos do DM. Separa-las daria
// tres arquivos de 60 linhas com o mesmo cabecalho.
// =====================================================================================

/// <summary>
/// NOMEACAO -- o Grande Anciao de Namek escolhe os quatro Anciaos cardeais.
///
/// Fonte: `Modules/Ranks/ordered/NamekRanks.dm:56-72` (verb `Appoint_Elder`, destravado pela skill
/// `/datum/skill/rank/Appoint_Elder` que so o "Namekian Grand Elder" habilita, `:27`).
///
/// ============================ DUAS COISAS DO DM QUE NAO SE PORTA ============================
/// 1. **O DM ESCREVE A CHAVE ERRADA.** `North_Elder=key` (`:63`) grava a `key` de QUEM NOMEIA --
///    `key` sem prefixo, dentro de um verb, e `src.key`. E o `Rank_Verb_Assign` compara os quatro
///    globais por SIGNATURE (`RankAssign.dm:95`). Ou seja: no original a nomeacao grava a pessoa
///    errada num campo que e lido por outro identificador, e nenhum Anciao cardeal jamais foi
///    reconhecido como tal. E a mesma familia do defeito do Lorde do Gelo (`Murder.dm:98`
///    compara por `key` com o comentario "SIGNATURE, nao key" doze linhas acima). Aqui a chave e
///    a CONTA, como no resto do port.
///
/// 2. **O DM EXIGE QUE O ALVO JA SEJA O QUE ELE VAI VIRAR.** `if(M.Rank=="Namekian Elder")`
///    (`:58`) so deixa nomear quem JA e Anciao cardeal -- com os quatro assentos vazios num
///    servidor novo, ninguem nunca pode ser nomeado. Porta fechada por dentro. Aqui a exigencia e
///    a que o cargo tem sentido de ter: **sangue Namekuseijin** (o mesmo teste do Grande Anciao,
///    `RankQuests.dm:223`, e que o proprio DM aplica a arvore de skills de Namek).
/// ==========================================================================================
/// </summary>
public static class Nomeacao
{
	/// <summary>Quem nomeia: o Grande Anciao, e so ele.</summary>
	public const string CargoQueNomeia = "elder";

	/// <summary>
	/// Os quatro assentos. A ORDEM E A DO MENU do DM (`Rewards.dm:315-318`) e ela importa: e nela
	/// que o servidor procura o primeiro assento vago quando o nomeador nao escolhe um.
	/// </summary>
	public static readonly string[] Assentos = ["northelder", "southelder", "eastelder", "westelder"];

	public static bool EhAssento(string chave) =>
		Array.Exists(Assentos, a => string.Equals(a, chave, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// POR QUE NAO DA PRA NOMEAR. Nulo = pode.
	///
	/// Devolve TEXTO pelo mesmo motivo do <see cref="Cargos.OqueFalta"/>: "voce nao e o Grande
	/// Anciao" e "ele nao tem sangue de Namek" mandam fazer coisas opostas.
	///
	/// O CARGO DO ALVO E CONFERIDO AQUI e nao no servidor porque a regra tem uma excecao que so se
	/// enxerga de dentro: trocar um Anciao cardeal de assento e MUDAR DE ASSENTO, nao acumular
	/// cargo -- e "uma alma, um trono" (`rq_any_rank`, RankQuests.dm:135) nao pode barrar isso.
	/// </summary>
	public static string? PorQueNao(string cargoDoNomeador, string assento,
								   string racaDoAlvo, string racaMaeDoAlvo, string cargoDoAlvo)
	{
		if (!string.Equals(cargoDoNomeador, CargoQueNomeia, StringComparison.OrdinalIgnoreCase))
			return "só o Grande Ancião de Namek nomeia Anciãos";

		if (!EhAssento(assento))
			return "isso não é um assento de Ancião (Norte, Sul, Leste ou Oeste)";

		bool namek = string.Equals(racaDoAlvo, "Namekian", StringComparison.OrdinalIgnoreCase)
				  || string.Equals(racaMaeDoAlvo, "Namekian", StringComparison.OrdinalIgnoreCase);
		if (!namek) return "um Ancião de Namek precisa ter sangue Namekuseijin";

		if (cargoDoAlvo.Length > 0 && !EhAssento(cargoDoAlvo))
			return "essa alma já carrega outro cargo -- uma alma, um trono";

		return null;
	}
}

/// <summary>
/// SUCESSAO VIOLENTA -- o trono passa a quem MATA quem o carrega.
///
/// Fonte: `Modules/CombatMechanics/Murder.dm:85-101`, dentro do `killer_stuff` (o funil por onde
/// toda morte de jogador passa no DM). Dois cargos, duas regras diferentes:
///
///   * **Rei de Vegeta** (`:85-96`): o algoz Saiyajin toma o trono. Nao sendo, o trono vai pro
///     **Principe/Princesa** nomeado (`A.Prince|A.Princess`, e quem nomeia e o proprio Rei pelo
///     `Vegeta Managament`, `rankSkills/SpaceRankSkills.dm:54-88`). Nao havendo herdeiro vivo, o
///     trono **VAGA** (`else King_of_Vegeta=null`).
///   * **Lorde do Gelo** (`:97-101`): o algoz Frost Demon (por RACA **ou** CLASSE) toma o titulo.
///     Sem herdeiro nenhum -- quem nao for da estirpe simplesmente mata e nao herda.
///
/// ============================ A SEGUNDA PORTA DO LORDE DO GELO ============================
/// A `RankDef` do `frostlord` marca `Porta = Prova`, e esta certa: o DM o poe no `RQ_CLAIMABLE`
/// (`RankQuests.dm:251`). O que a tabela nao tem como dizer com um campo so e que ele tem **DUAS**
/// portas -- reivindicar com requisito **ou** matar o portador. A segunda mora aqui, e e por isso
/// que este arquivo pergunta pela CHAVE do cargo e nao pela `Porta`: um cargo pode ter duas.
/// ========================================================================================
///
/// E O BUG DO DM NAO VEM JUNTO: `if(Frost_Demon_Lord==M.key)` (`:98`) compara por `key` doze linhas
/// depois de um comentario que diz, em voz alta, *"SIGNATURE, nao key: todo o sistema de rank
/// compara por signature"* (`:85`). Aqui os dois cargos comparam pela CONTA, como todo o resto.
/// </summary>
public static class Sucessao
{
	public const string ReiDeVegeta = "kov";
	public const string LordeDoGelo = "frostlord";

	/// <summary>Este cargo troca de dono quando o portador e morto?</summary>
	public static bool TemSucessao(string cargo) =>
		string.Equals(cargo, ReiDeVegeta, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(cargo, LordeDoGelo, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// FROST DEMON x ICER -- o mesmo par de nomes que a tabela de cargos ja aceita (ver
	/// `Cargos.RacaFrostDemon`): o DM chama a raca de "Frost Demon" e o `races.json` do port gravou
	/// "Icer". Fechar a sucessao por causa de um rotulo seria um bug invisivel.
	/// </summary>
	private static bool EhFrostDemon(string s) =>
		string.Equals(s, "Frost Demon", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(s, "Icer", StringComparison.OrdinalIgnoreCase);

	private static bool EhSaiyajin(string s) => string.Equals(s, "Saiyan", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// O ALGOZ TOMA O TRONO NA HORA?
	///
	/// O sangue da MAE conta pelo mesmo motivo de sempre (`FichaDeRank.RacaMae`): quase todo teste
	/// racial do DM pergunta `Race == X || Parent_Race == X`, e meio-sangue vale pelos dois.
	/// A CLASSE so entra no Lorde do Gelo, porque so la o DM a consulta (`Class=="Frost Demon"`).
	/// </summary>
	public static bool AlgozHerda(string cargo, string racaDoAlgoz, string racaMaeDoAlgoz, string classeDoAlgoz)
	{
		if (string.Equals(cargo, ReiDeVegeta, StringComparison.OrdinalIgnoreCase))
			return EhSaiyajin(racaDoAlgoz) || EhSaiyajin(racaMaeDoAlgoz);

		if (string.Equals(cargo, LordeDoGelo, StringComparison.OrdinalIgnoreCase))
			return EhFrostDemon(racaDoAlgoz) || EhFrostDemon(racaMaeDoAlgoz) || EhFrostDemon(classeDoAlgoz);

		return false;
	}

	/// <summary>
	/// O TRONO TEM LINHA DE SANGUE? So o de Vegeta (`Murder.dm:91`). O Lorde do Gelo nao herda de
	/// ninguem: quem nao mata, nao e lorde.
	/// </summary>
	public static bool TemHerdeiros(string cargo) =>
		string.Equals(cargo, ReiDeVegeta, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// ESTE HERDEIRO SERVE? `if(A.Race=="Saiyan"&&!A.dead)` (`Murder.dm:91`) -- Saiyajin e VIVO.
	///
	/// A ordem de quem entra primeiro e a da lista de herdeiros do Rei, e nao um sorteio: o DM pega
	/// o primeiro `for(var/mob/A)` que casar, que e ordem de criacao do mob e nao significa nada.
	/// Aqui significa: e a ordem em que o Rei os nomeou.
	/// </summary>
	public static bool HerdeiroServe(string racaDoHerdeiro, string racaMaeDoHerdeiro, bool morto) =>
		!morto && (EhSaiyajin(racaDoHerdeiro) || EhSaiyajin(racaMaeDoHerdeiro));
}

/// <summary>Por que um desafio pelo titulo foi recusado. A ordem e a do DM.</summary>
public enum RecusaDeDuelo
{
	Pode,
	SemDono,        // nao ha Deus da Destruicao pra desafiar
	VoceEhODono,
	Caido,          // morto ou nocauteado
	SemGodKi,       // `GodOfDestruction.dm:434`
	DueloEmCurso,
	Carencia,       // o novo Deus tem 7 dias de paz
	IntervaloFechado, // 2 dias entre desafios
	DonoAusente,    // offline: nao se toma titulo de quem nao esta no plano fisico
}

/// <summary>Como um duelo termina.</summary>
public enum FimDeDuelo { Continua, Dono, Desafiante }

/// <summary>
/// O ESTADO DO TITULO EM DUELO. Uma instancia por cargo com <see cref="PortaDoCargo.Duelo"/> --
/// hoje so o Deus da Destruicao, e o tipo nao sabe disso de proposito.
///
/// TUDO EM MILISSEGUNDOS DE RELOGIO REAL, como no DM (`world.realtime`, e nao `world.time`): a
/// carencia de 7 dias e o intervalo de 2 dias sao promessas feitas a uma PESSOA, e mede-las no
/// relogio do jogo faria o prazo depender de quanto o servidor ficou no ar.
/// </summary>
public sealed class EstadoDoDuelo
{
	/// <summary>Quando o portador atual assumiu. E o `god_title_since`.</summary>
	public long TituloDesde;

	/// <summary>Ultimo duelo OU adiamento -- `god_duel_last`. Adiar consome o desafio da janela.</summary>
	public long UltimoDuelo;

	/// <summary>`god_postpones`: adiamentos gastos na janela semanal.</summary>
	public int Adiamentos;

	/// <summary>`god_postpone_week`: quando a janela semanal comecou.</summary>
	public long SemanaDosAdiamentos;

	/// <summary>
	/// `god_duel_busy`. **NAO PERSISTE**, e o DM explica o porque no proprio nome do campo: uma
	/// queda no meio de um duelo destrava o trono em vez de trancar o titulo pra sempre.
	/// </summary>
	public bool EmDuelo;
}

/// <summary>
/// DUELO FORMAL PELO TITULO -- as regras puras.
///
/// Fonte: `Modules/Ranks/GodOfDestruction.dm:30-35` (as constantes), `:424-473` (o desafio e o
/// adiamento) e `:475-518` (o `god_duel_run`). Os numeros sao os de la, convertidos de decimos de
/// segundo pra milissegundos.
/// </summary>
public static class Duelo
{
	/// <summary>`GOD_DUEL_INTERVAL` 1728000 ds = 2 dias reais entre desafios.</summary>
	public const long Intervalo = 2L * 24 * 60 * 60 * 1000;

	/// <summary>`GOD_DUEL_GRACE` 6048000 ds = 7 dias de paz pro Deus recem-coroado.</summary>
	public const long Carencia = 7L * 24 * 60 * 60 * 1000;

	/// <summary>`GOD_DUEL_WEEK`: a janela em que os adiamentos contam.</summary>
	public const long JanelaDeAdiamentos = 7L * 24 * 60 * 60 * 1000;

	/// <summary>`GOD_DUEL_POSTPONE_MAX` 2. O TERCEIRO adiamento e covardia e custa o titulo.</summary>
	public const int AdiamentosPermitidos = 2;

	/// <summary>`GOD_DUEL_TIMEOUT` 6000 ds = 10 min. Estourou, o Deus RETEM o titulo.</summary>
	public const long Prazo = 10L * 60 * 1000;

	/// <summary>Contagem regressiva antes do "COMECEM!" (`:488`, cinco `sleep(10)`).</summary>
	public const long Contagem = 5000;

	/// <summary>
	/// A JANELA SEMANAL DOS ADIAMENTOS ROLA SOZINHA (`:453-455`). Chamada antes de qualquer conta
	/// que envolva adiamento -- e nao no adiamento em si, senao um Deus que nao e desafiado ha um
	/// mes carregaria adiamentos velhos pra sempre.
	/// </summary>
	public static void RolarSemana(EstadoDoDuelo e, long agora)
	{
		if (agora - e.SemanaDosAdiamentos <= JanelaDeAdiamentos) return;
		e.SemanaDosAdiamentos = agora;
		e.Adiamentos = 0;
	}

	/// <summary>
	/// DA PRA DESAFIAR? A ordem das checagens e a do verb do DM (`:427-449`) e ela decide a frase
	/// que o desafiante le -- dizer "o trono so aceita desafios a cada 2 dias" pra quem nem
	/// despertou o God Ki manda a pessoa esperar por nada.
	/// </summary>
	public static RecusaDeDuelo PodeDesafiar(EstadoDoDuelo e, long agora, bool haDono, bool ehODono,
											 bool desafianteDePe, bool godKiDesperto, bool donoPresente)
	{
		if (!haDono) return RecusaDeDuelo.SemDono;
		if (ehODono) return RecusaDeDuelo.VoceEhODono;
		if (!desafianteDePe) return RecusaDeDuelo.Caido;
		if (!godKiDesperto) return RecusaDeDuelo.SemGodKi;
		if (e.EmDuelo) return RecusaDeDuelo.DueloEmCurso;
		if (agora - e.TituloDesde < Carencia) return RecusaDeDuelo.Carencia;
		if (agora - e.UltimoDuelo < Intervalo) return RecusaDeDuelo.IntervaloFechado;
		if (!donoPresente) return RecusaDeDuelo.DonoAusente;
		return RecusaDeDuelo.Pode;
	}

	/// <summary>
	/// O DEUS ADIOU. Devolve **true quando isso foi covardia** e o titulo esta perdido.
	///
	/// Duas coisas acontecem antes da conta e as duas sao do DM (`:460-468`): o adiamento **consome
	/// o desafio da janela de 2 dias** (`god_duel_last = world.realtime`), entao adiar compra dois
	/// dias de paz; e o terceiro adiamento **na mesma semana** derruba o titulo na hora.
	/// </summary>
	public static bool Adiar(EstadoDoDuelo e, long agora)
	{
		RolarSemana(e, agora);
		e.Adiamentos++;
		e.UltimoDuelo = agora;
		return e.Adiamentos > AdiamentosPermitidos;
	}

	/// <summary>
	/// UM PASSO DO DUELO -- a escada literal do `while` do `god_duel_run` (`:496-504`), na ordem.
	///
	/// ============================ POR QUE ISTO E PURO ============================
	/// A escada tem seis degraus e cinco deles decidem quem fica com o titulo do jogo. Escrita
	/// dentro do laco do servidor, a unica forma de provar que "os dois caidos empatam por vida" e
	/// que "o prazo estourado NAO tira o titulo do Deus" seria por um duelo de dez minutos com dois
	/// clientes. Aqui sao duas chamadas.
	/// ==========================================================================
	/// </summary>
	/// <param name="donoSumiu">offline ou morto -- o `!G.client || G.dead` do DM.</param>
	public static FimDeDuelo Passo(bool donoSumiu, bool desafianteSumiu,
								   bool donoCaido, bool desafianteCaido,
								   double vidaDoDono, double vidaDoDesafiante,
								   bool prazoEstourou)
	{
		if (donoSumiu) return FimDeDuelo.Desafiante;
		if (desafianteSumiu) return FimDeDuelo.Dono;

		// OS DOIS NO CHAO: vence quem tem MAIS VIDA (`:501`). Empate exato fica com o Deus, que e o
		// que o `>=` do DM faz -- e e a mesma regra do prazo estourado: o trono nao muda de mao por
		// falta de decisao.
		if (donoCaido && desafianteCaido)
			return vidaDoDono >= vidaDoDesafiante ? FimDeDuelo.Dono : FimDeDuelo.Desafiante;

		if (donoCaido) return FimDeDuelo.Desafiante;
		if (desafianteCaido) return FimDeDuelo.Dono;

		// TEMPO ESGOTADO: `if(!winner) winner = G` (`:504`) -- "o desafiante falhou em derrubar o
		// Deus". Quem desafia tem que VENCER; empatar e perder.
		return prazoEstourou ? FimDeDuelo.Dono : FimDeDuelo.Continua;
	}

	/// <summary>
	/// QUANTO FALTA, em palavras. E o `god_time_left()` do DM (que imprime dias/horas/minutos).
	///
	/// Mora no Core porque a frase e a REGRA falando: um "aguarde" sem prazo transforma a carencia
	/// de 7 dias em "o botao nao funciona".
	/// </summary>
	public static string TempoQueFalta(long alvo, long agora)
	{
		long ms = alvo - agora;
		if (ms <= 0) return "agora";
		long min = ms / 60000;
		if (min < 60) return $"{Math.Max(min, 1)} min";
		long h = min / 60;
		if (h < 24) return $"{h}h {min % 60}min";
		return $"{h / 24}d {h % 24}h";
	}
}
