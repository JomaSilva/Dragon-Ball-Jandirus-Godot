using Jandirus.Core.Magic;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// **AS ESFERAS NA MOCHILA, E O PLANETA QUE ELAS NAO DEIXAM** -- o pedido do dono (2026-10-19), inteiro:
///
/// *"faca com q caso pegue as esferas elas vao pro seu inventario (mantendo o sprite atual, se elas estriverem como pedra,
/// elas vao como pedra, se nao vao como a esfera laranja mesmo normal) podendo dropar elas ou nao, se vc for nocauteado ou
/// morto vc dropa elas no chao, n da pra sair do planeta com elas , entao naves e espaco vc e bloqueado de ir com elas, mas
/// locais como cavernas, lookout etc vc pode ir pq ainda estao dentro do planeta. caso vc tenha as 7 no inventario e clique no
/// verb q ja existe de invocar o dragao elas vao sair do seu inventario e acontecer oq ja acontece atualmente com invocar as
/// esferas."*
///
/// ============================ O QUE MUDOU, E ONDE ============================
///   1. **A MOCHILA MOSTRA** -- a esfera carregada vira um slot da mochila, com o sprite do chao (pedra ou laranja). Ela nao
///      MORA na mochila: o `Portador` continua sendo a unica resposta pra "quem tem esta esfera" (ver `EsferaNaMochila`, no
///      Core, que conta por que), e o pacote da mochila a leva depois das pecas vestidas (<see cref="EsferasNaMochilaDe"/>).
///   2. **O SLOT LARGA** -- "Largar no chao" poe AQUELA esfera aos pes de quem a carrega (<see cref="AcaoDaEsferaNaMochila"/>);
///      o "Largar tudo" da aba World continua largando as sete.
///   3. **NOCAUTE E MORTE DERRUBAM** -- isto ja era do funil das tres quedas (`TickDasEsferas`, item 3); a mochila acompanha
///      no mesmo segundo (<see cref="SincronizarAsEsferasNasMochilas"/>).
///   4. **DENTRO DO PLANETA ELA VAI JUNTO** -- caverna e Lookout sao do planeta (<see cref="DentroDoPlanetaDoSet"/>, pelo
///      caminho das passagens: `InteriorDoPlaneta`). Antes, QUALQUER troca de zona derrubava a esfera, e a policia de planeta
///      a mandava de volta pro ponto da semente.
///   5. **PRA FORA DELE, NAO** -- decolar, embarcar em nave (pod ou Capital Ship), romper a atmosfera voando e todo teleporte
///      RECUSAM quem carrega (<see cref="PorQueAsEsferasPrendem"/>); e o que nenhuma porta recusa (a morte que leva ao Outro
///      Mundo, o admin, a evacuacao de um planeta que explode) passa pela rede do `MoveToZone`: a esfera fica no chao, onde a
///      pessoa estava (<see cref="SoltarAsEsferasQueNaoSaemDoPlaneta"/>).
///   6. **A INVOCACAO TIRA DA MOCHILA** -- com as sete juntas, o `INVOCAR` de sempre as poe no chao aos pes de quem invoca
///      (<see cref="TirarDaMochilaPraInvocacao"/>), e dali o fluxo e o de antes: o dragao, os pedidos, o espalhar.
/// ======================================================================================================
///
/// ============================ O TELEPORTE RECUSA MESMO DENTRO DO PLANETA, E E DECISAO ============================
/// A pergunta de todo teleporte (`PorQueNaoSaiDaqui`) nao sabe o DESTINO -- a Transmissao a faz antes de escolher o alvo, o
/// salto de planeta antes da lista. Recusar quem carrega e o que nunca perde uma esfera; a alternativa (deixar ir e largar
/// na origem) faria um clique na Transmissao entregar as sete a quem estivesse do lado.
/// ===================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// OS DEFEITOS INJETADOS (bancada `--esferamochilateste`) -- falsos em jogo, sempre
	// =====================================================================
	/// <summary>DEFEITO INJETADO: a esfera carregada nao vai no pacote da mochila -- o jogo de antes. Lido pelo <see cref="EsferasNaMochilaDe"/>.</summary>
	internal static bool EsferaForaDaMochilaDeTeste;

	/// <summary>DEFEITO INJETADO: a esfera de set apagado vai pra mochila acesa (laranja), e nao como pedra. Lido pelo <see cref="EsferasNaMochilaDe"/>.</summary>
	internal static bool EsferaSempreAcesaNaMochilaDeTeste;

	/// <summary>DEFEITO INJETADO: o "largar" do slot nao e atendido -- cai no caminho dos itens da mochila, que nao a tem. Lido pelo `ComandoDeItem`.</summary>
	internal static bool SlotDaEsferaNaoLargaDeTeste;

	/// <summary>DEFEITO INJETADO: o tique nao acerta a mochila de quem perdeu ou ganhou esfera por fora (o nocaute, o espalhar, o set que acorda). Lido pelo <see cref="SincronizarAsEsferasNasMochilas"/>.</summary>
	internal static bool MochilaNaoAcompanhaAsEsferasDeTeste;

	/// <summary>DEFEITO INJETADO: caverna e Lookout nao sao do planeta -- toda troca de zona derruba, o jogo de antes. Lido pelo <see cref="DentroDoPlanetaDoSet"/>.</summary>
	internal static bool InteriorNaoEDoPlanetaDeTeste;

	/// <summary>DEFEITO INJETADO: a esfera carregada nao segue o corpo (zona e ponto ficam onde ela foi pega). Lido pelo <see cref="AcompanharOPortador"/>.</summary>
	internal static bool EsferaNaoAcompanhaOPortadorDeTeste;

	/// <summary>DEFEITO INJETADO: nenhuma porta recusa quem carrega esfera (decolar, nave, voo, teleporte). Lido pelo <see cref="PorQueAsEsferasPrendem"/>.</summary>
	internal static bool EsferaNaoPrendeDeTeste;

	/// <summary>DEFEITO INJETADO: a rede do `MoveToZone` solta -- quem sai do planeta leva a esfera junto. Lido pelo <see cref="SoltarAsEsferasQueNaoSaemDoPlaneta"/>.</summary>
	internal static bool EsferaSaiDoPlanetaDeTeste;

	/// <summary>DEFEITO INJETADO: a invocacao deixa as sete na mochila, como antes. Lido pelo <see cref="TirarDaMochilaPraInvocacao"/>.</summary>
	internal static bool InvocacaoDeixaNaMochilaDeTeste;

	/// <summary>DEFEITO INJETADO: o dragao sobe fora do mundo do set (na caverna, no Lookout). Lido pelo `InvocarODragao`.</summary>
	internal static bool DragaoSobeForaDoSetDeTeste;

	/// <summary>DEFEITO INJETADO: quem esta ao leme de uma nave pega a esfera do chao. Lido pelo `PegarEsfera`.</summary>
	internal static bool PegaDeDentroDaNaveDeTeste;

	// =====================================================================
	// 1. A MOCHILA MOSTRA
	// =====================================================================
	/// <summary>
	/// OS SLOTS DE ESFERA DA MOCHILA DESTE CORPO, na ordem das estrelas: o id de cada uma (`EsferaNaMochila`), com a folha do
	/// set e o estado de agora -- pedra se o set esta apagado ou inerte (<see cref="SetAtivo"/>), laranja se acordado. E o que o
	/// `MandarMochila` poe na assinatura e no pacote.
	/// </summary>
	private List<string> EsferasNaMochilaDe(ServerPlayer pl)
	{
		var ids = new List<string>();
		if (EsferaForaDaMochilaDeTeste) return ids;
		foreach (Esfera e in _esferas.Where(x => x.Portador == pl.Id).OrderBy(x => x.Set).ThenBy(x => x.Numero))
		{
			SetDeEsferas? s = SetPorId(e.Set);
			bool apagada = !EsferaSempreAcesaNaMochilaDeTeste && (s == null || !SetAtivo(s));
			ids.Add(new EsferaNaMochila(e.Set, e.Numero, apagada, s?.Folha == "namek").Id);
		}
		return ids;
	}

	/// <summary>
	/// O `S2C.Inventario` que saiu, por destinatario: pra quem e o fio (a linha de escuta mora no `MandarMochila`). Nulo fora da bancada
	/// (`--esferamochilateste`) -- o molde do `EscutaDoFioDasEsferas`.
	/// </summary>
	internal static List<(int Para, byte[] Fio)>? EscutaDaMochila;

	/// <summary>
	/// QUEM FICOU COM SLOT DE ESFERA NA TELA: o ultimo `S2C.Inventario` que saiu pra ele levou ao menos uma. Quem escreve e o
	/// `MandarMochila`, no pacote que sai -- e nao o tique pelo que VIU: a primeira versao guardava "quem a varredura anterior viu
	/// carregando", e quem pegava e perdia a esfera entre duas varreduras (pegou, e no mesmo segundo caiu) ficava com o slot preso na
	/// tela -- a `--esferamochilateste` (D2) o pegou na r1. Por id de sessao.
	/// </summary>
	private readonly HashSet<int> _comEsferaNaMochila = [];

	/// <summary>
	/// A MOCHILA ACOMPANHA AS ESFERAS -- uma vez por segundo, no fim do <see cref="TickDasEsferas"/>. Os gestos do jogador
	/// (pegar, largar, invocar) mandam a mochila na hora; o que muda POR FORA -- o nocaute que derruba, o espalhar depois do
	/// pedido, o set refeito, derrubado ou enterrado com o planeta, e o set que ACORDA e troca a pedra pela esfera acesa --
	/// chega por aqui: pra quem carrega agora e pra quem tinha slot na tela. E o molde do disco do radar: estado comparado, e nao
	/// um gancho em cada porta. O `MandarMochila` so manda quando a assinatura mudou.
	/// </summary>
	private void SincronizarAsEsferasNasMochilas()
	{
		if (MochilaNaoAcompanhaAsEsferasDeTeste) return;
		var alvo = new HashSet<int>(_comEsferaNaMochila);
		foreach (Esfera e in _esferas)
			if (e.Portador != 0) alvo.Add(e.Portador);

		foreach (int id in alvo)
		{
			if (_players.TryGetValue(id, out ServerPlayer? quem)) MandarMochila(quem);
			else _comEsferaNaMochila.Remove(id);   // saiu do jogo: nao ha tela pra acertar
		}
	}

	// =====================================================================
	// 2. O SLOT LARGA
	// =====================================================================
	/// <summary>
	/// O BOTAO DO SLOT DE UMA ESFERA. So "largar" existe: a esfera vai pro chao aos pes de quem a carrega, e as outras ficam na
	/// mochila. O id do slot so diz QUAL (set e numero); quem confere se ela esta mesmo com ele e a lista das esferas.
	/// </summary>
	private void AcaoDaEsferaNaMochila(ServerPlayer pl, string acao, EsferaNaMochila qual)
	{
		Esfera? e = _esferas.Find(x => x.Set == qual.Set && x.Numero == qual.Numero && x.Portador == pl.Id);
		string nome = Esferas.NomeDaEsfera(qual.Numero);
		if (e == null)
		{
			// A TELA ESTAVA ATRASADA (a esfera caiu ou se espalhou neste meio): a mochila vai de novo, inteira.
			Avisar(pl, $"você não está mais com a {nome}.");
			MandarMochila(pl, forcar: true);
			return;
		}
		if (acao != "largar")
		{
			Avisar(pl, $"com a {nome} só dá pra largar no chão -- ou juntar as sete e invocar o dragão (aba World).");
			return;
		}

		e.Portador = 0;
		e.X = pl.Pos.X;
		e.Y = pl.Pos.Y;
		e.PorZona(pl.Zone);
		SalvarEsferas();
		MandarEsferas(pl.Zone);
		MandarMochila(pl);
		Avisar(pl, $"você põe a {nome} no chão ({QuantasCarrega(pl.Id)}/{Esferas.Total} com você).");
	}

	// =====================================================================
	// 3. A INVOCACAO TIRA DA MOCHILA
	// =====================================================================
	/// <summary>
	/// AS QUE ESTAVAM NA MAO DE QUEM INVOCA VAO PRO CHAO, aos pes dele -- o leque de `Esferas.LequeDe` as abre em roda, como as
	/// do chao. Chamado pelo `InvocarODragao` depois de todas as recusas, no instante em que o dragao sobe: daqui pra frente e o
	/// fluxo de sempre, e o ultimo pedido as espalha de onde estao (`ContarUmDesejo`).
	/// </summary>
	private void TirarDaMochilaPraInvocacao(ServerPlayer pl, List<Esfera> sete)
	{
		if (InvocacaoDeixaNaMochilaDeTeste) return;
		List<Esfera> daMao = [.. sete.Where(e => e.Portador == pl.Id)];
		if (daMao.Count == 0) return;

		foreach (Esfera e in daMao)
		{
			e.Portador = 0;
			e.X = pl.Pos.X;
			e.Y = pl.Pos.Y;
			e.PorZona(pl.Zone);
		}
		SalvarEsferas();
		MandarMochila(pl);
	}

	// =====================================================================
	// 4. DENTRO DO PLANETA
	// =====================================================================
	/// <summary>Os interiores de cada planeta do catalogo, por nome (`InteriorDoPlaneta`). As passagens sao lidas uma vez, no boot.</summary>
	private readonly Dictionary<string, HashSet<string>> _interioresDoPlaneta = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// ESTA ZONA AINDA E O PLANETA DESTE SET? A propria zona dele, ou um interior que se alcanca dela a pe (a caverna, o
	/// Lookout). So mapa do catalogo tem passagem -- mundo gerado, nave, mente e Sala do Tempo de planeta sao so eles mesmos.
	/// </summary>
	private bool DentroDoPlanetaDoSet(ZoneKey zona, SetDeEsferas s)
	{
		if (zona.Hash == s.Zona.Hash) return true;
		if (InteriorNaoEDoPlanetaDeTeste) return false;
		return zona.Kind == ZoneKey.KindPremade && s.Zona.Kind == ZoneKey.KindPremade
			&& InterioresDoPlaneta(s.ZonaNome).Contains(zona.Name);
	}

	private HashSet<string> InterioresDoPlaneta(string planeta)
	{
		if (_interioresDoPlaneta.TryGetValue(planeta, out HashSet<string>? ja)) return ja;
		HashSet<string> achados = InteriorDoPlaneta.Do(planeta,
			nome => _passagens.TryGetValue(nome, out List<Passagem>? bocas) ? bocas.Select(b => b.Zona) : [],
			nome => Espaco.EhPlaneta(ZoneKey.Premade(nome)));
		// SEM PASSAGEM LIDA (antes do boot terminar) a resposta vazia nao vai pro cache: seria "nenhum interior" pra sempre.
		if (_passagens.Count > 0) _interioresDoPlaneta[planeta] = achados;
		return achados;
	}

	/// <summary>
	/// A ESFERA NA MAO SEGUE O CORPO: a zona e o ponto dela passam a ser os de quem a carrega, a cada tique (item 3 do
	/// <see cref="TickDasEsferas"/>). O radar pergunta pela zona dela, e o logout -- que nao deixa corpo pra perguntar -- a
	/// deixa onde ela estava: com isto, onde a pessoa estava um segundo antes, e nao no ponto em que a pegou (que pode ser
	/// noutra zona do mesmo planeta).
	/// </summary>
	private void AcompanharOPortador(Esfera e, ServerPlayer quem)
	{
		if (EsferaNaoAcompanhaOPortadorDeTeste) return;
		e.X = quem.Pos.X;
		e.Y = quem.Pos.Y;
		e.PorZona(quem.Zone);
	}

	// =====================================================================
	// 5. AS PORTAS QUE RECUSAM
	// =====================================================================
	/// <summary>
	/// POR QUE ESTE CORPO NAO SAI DO PLANETA AGORA -- vazio se ele nao carrega esfera nenhuma. A frase de toda porta que leva
	/// pra fora: decolar (`Decolar`, e o voo que rompe a atmosfera), embarcar (o pod e a Capital Ship) e o teleporte
	/// (`PorQueNaoSaiDaqui`, que a carona tambem pergunta -- quem carrega nao vai de carona no salto de outro).
	/// </summary>
	private string PorQueAsEsferasPrendem(ServerPlayer pl)
	{
		if (EsferaNaoPrendeDeTeste) return "";
		Esfera? uma = _esferas.Find(e => e.Portador == pl.Id);
		if (uma == null) return "";

		int quantas = QuantasCarrega(pl.Id);
		string planeta = NomeDoPlaneta(SetPorId(uma.Set)?.ZonaNome ?? pl.Zone.Name);
		return quantas == 1
			? $"você carrega uma Esfera do Dragão, e ela não sai de {planeta} -- nem de nave, nem pelo espaço, nem por teleporte. Largue-a antes (na mochila)."
			: $"você carrega {quantas} Esferas do Dragão, e elas não saem de {planeta} -- nem de nave, nem pelo espaço, nem por teleporte. Largue-as antes (na mochila).";
	}

	// =====================================================================
	// 6. A REDE DO `MoveToZone`
	// =====================================================================
	/// <summary>
	/// QUEM SAI DO PLANETA CARREGANDO, SAI SEM A ESFERA: ela fica no chao, onde a pessoa estava. Chamado pelo `MoveToZone` antes
	/// de o corpo mudar de zona -- o funil de toda viagem, e por isso o lugar de uma regra que nao pode depender de alguem
	/// lembrar dela. As portas que o jogador escolhe ja recusaram antes (<see cref="PorQueAsEsferasPrendem"/>); aqui passam as
	/// que ninguem escolhe: a morte que leva pro Outro Mundo, o admin que traz, a evacuacao de um mundo que explode, a queda
	/// da nuvem do Lookout que da noutro planeta.
	///
	/// SO QUEM ESTA SAINDO DO PLANETA DA ESFERA: uma esfera que ja estava fora dele (um estado que o jogo nao produz -- so uma
	/// bancada o monta) fica pro tique, que a derruba e a policia de planeta a manda pra casa, como sempre.
	/// </summary>
	private void SoltarAsEsferasQueNaoSaemDoPlaneta(ServerPlayer pl, ZoneKey destino)
	{
		if (EsferaSaiDoPlanetaDeTeste) return;
		List<Esfera> caem = [.. _esferas.Where(e => e.Portador == pl.Id && SetPorId(e.Set) is { } s
														&& DentroDoPlanetaDoSet(pl.Zone, s) && !DentroDoPlanetaDoSet(destino, s))];
		if (caem.Count == 0) return;

		foreach (Esfera e in caem)
		{
			e.Portador = 0;
			e.X = pl.Pos.X;
			e.Y = pl.Pos.Y;
			e.PorZona(pl.Zone);
		}
		SalvarEsferas();
		MandarEsferas(pl.Zone);
		MandarMochila(pl);

		string planeta = NomeDoPlaneta(SetPorId(caem[0].Set)?.ZonaNome ?? pl.Zone.Name);
		Avisar(pl, caem.Count == 1
			? $"a {Esferas.NomeDaEsfera(caem[0].Numero)} não sai de {planeta}: ela fica no chão, onde você estava."
			: $"as {caem.Count} Esferas do Dragão não saem de {planeta}: ficam no chão, onde você estava.");
	}
}
