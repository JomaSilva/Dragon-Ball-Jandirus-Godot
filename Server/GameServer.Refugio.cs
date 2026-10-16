using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// **O REFUGIO EM JOGO** -- para onde vai quem ficou sem planeta natal, e como o jogador escolhe.
///
/// ============================ O PEDIDO DO DONO, LITERAL ============================
/// *"quando uma raca fica sem planeta natal, o jogador pode ou spawnar em um planeta q ele
/// conquistou ou em um planeta proximo do planeta natal dele"*.
///
/// Duas opcoes, e o "ou ... ou" e ESCOLHA e nao cascata:
///   **B1** -- um planeta que ESTE personagem conquistou (o livro de dominios, ja existente);
///   **B2** -- um mundo vivo perto do natal (<see cref="Refugios.MundosPertoDe"/>, funcao pura).
///
/// Com as duas disponiveis, quem decide e o jogador. Com uma so, ela e o destino e ninguem e
/// perguntado. Sem nenhuma, o corpo abre os olhos no ESPACO ABERTO -- ver o bloco do ultimo recurso.
/// ================================================================================
///
/// ============================ (v0.3.0) E A TERCEIRA: A TERRA, O PONTO DE ENCONTRO ============================
/// *"faca q alem do jogador poder escolher spawnar em um planeta proximo ao planeta natal dele, ele sempre tem a opcao de
/// reviver na terra se o planeta natal dele foi destruido. a terra seria o ponto de encontro principal do jogo"* (o dono).
///   **B3** -- a TERRA (<see cref="TerraServeDeRefugio"/>), escolhida pelo jogador (`ServerPlayer.RefugioNaTerra`).
///
/// Ela e OPCAO e nao padrao (*"alem de poder escolher..., ele sempre tem a opcao"*): sem escolha, o destino continua a
/// vizinhanca. Com ela as saidas sao tres e a escolha existe com duas -- ou seja, a tela passou a abrir pra todo mundo
/// que perdeu o natal, e nao so pra quem tem territorio. E ela e a ultima saida antes do vacuo.
/// ==========================================================================================================
///
/// ============================ O QUE MORREU PRA ISTO NASCER ============================
/// O `GameServer.Berco.cs` tinha um `ZonaDeRecuoViva` que descia `Espaco.PreFeitos()` e devolvia o
/// PRIMEIRO planeta vivo. **Ele foi DELETADO**, com a carta trocavel (`_cartaDeRecuo`) e o escopo
/// dela junto: nao ha um `if` guardando os dois comportamentos, porque dois comportamentos atras de
/// um `if` sao duas regras pra envelhecer.
///
/// O motivo de ele morrer nao e que errava -- e que o destino de quem perdia o berco era **uma
/// posicao numa lista**. Namek so recebia os desabrigados da Terra por ser a segunda linha de um
/// `yield return`. Medido: Terra morta mandava 10 de 24 racas pra Namek; Terra+Namek mandavam 14 pra
/// Vegeta; e as sagas do `npcs.json` ja destroem Vegeta e Namek sozinhas.
/// ==================================================================================
///
/// ============================ NENHUM DOS DOIS CAMINHOS E NOVO, E ISSO E O PONTO ============================
/// B1 ja estava construido e ligado: `Dominio.EhOSpawn`, o verb `conq_spawn` ("Renascer aqui", na
/// bandeira) e o <see cref="DestinoDe"/> que prefere o dominio ao berco. B2 tambem: o
/// `MotivoDoBerco.VizinhoDoNatal` e a opcao "Num mundo vizinho" da tela de criacao.
///
/// O que faltava era o caso do dono -- **o berco MORTO** -- cair nos dois em vez de cair na lista.
/// Por isso este arquivo e curto: ele e uma juncao, nao um sistema.
/// ========================================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// O RAIO DA BUSCA -- e por que ele e trocavel
	// =====================================================================
	/// <summary>
	/// ATE ONDE O REFUGIO PROCURA, em celulas de sistema. Em jogo e sempre
	/// <see cref="Refugios.CelulasDeBusca"/>; **so a bancada escreve aqui**.
	///
	/// ============================ ELE E O HERDEIRO DO `_cartaDeRecuo`, INVERTIDO ============================
	/// A carta trocavel existia pra a bancada PROVAR que o destino do defeito era uma posicao numa
	/// lista: enfiava-se "Hera" na frente de `Espaco.PreFeitos()` e o destino mudava de planeta. Com a
	/// lista morta aquela injecao nao tem mais o que provar -- e mais: ela ficaria VERDE PARA SEMPRE,
	/// que e o modo de falha que este projeto ja catalogou (afirmacao verde num sistema morto).
	///
	/// A manivela que sobrou e outra e mede outra coisa: **desligando a busca**, a vizinhanca sai
	/// vazia e a bancada alcanca os dois ramos que em jogo nunca disparam -- *"so o dominio existe"* e
	/// *"nao existe nenhum dos dois"*. Mesma disciplina do `teto` do <see cref="Bercos.ServeDeBerco"/>
	/// e do `tetoZona` da manutencao: aperta-se o PARAMETRO contra o codigo de producao, em vez de
	/// escrever um caminho paralelo que testaria o atalho.
	/// ====================================================================================================
	/// </summary>
	private int _celulasDeRefugio = Refugios.CelulasDeBusca;

	/// <summary>O escopo: `using (SemVizinhancaDeRefugio()) { ... }`. Ver <see cref="_celulasDeRefugio"/>.</summary>
	private BuscaDeRefugioTrocada SemVizinhancaDeRefugio() => new(this, -1);

	/// <summary>Devolve o raio de verdade no fim, mesmo se a bancada estourar no meio.</summary>
	internal sealed class BuscaDeRefugioTrocada : IDisposable
	{
		private readonly GameServer _s;
		private readonly int _antes;

		internal BuscaDeRefugioTrocada(GameServer s, int celulas)
		{
			_s = s;
			_antes = s._celulasDeRefugio;
			s._celulasDeRefugio = celulas;
		}

		public void Dispose() => _s._celulasDeRefugio = _antes;
	}

	// =====================================================================
	// A CARTA -- e por que ela voltou a ser trocavel
	// =====================================================================
	/// <summary>
	/// A CARTA ESTELAR QUE O REFUGIO LE. Em jogo e sempre <see cref="Espaco.PreFeitos"/> (nulo aqui);
	/// **so a bancada escreve neste campo**.
	///
	/// ============================ ELE E O `_cartaDeRecuo`, APONTADO PRO CONTRARIO ============================
	/// O recuo por lista tinha uma carta trocavel, e a bancada a usava pra PROVAR o defeito: enfiava
	/// um planeta ficticio ("Hera") na FRENTE de `Espaco.PreFeitos()` e o destino de todo desabrigado
	/// mudava de planeta, sem mais nada no mundo mudar. Aquele campo morreu com o `ZonaDeRecuoViva`.
	///
	/// **Ele volta aqui porque a mesma injecao passou a medir a afirmacao oposta, e ela e a prova
	/// central desta regra**: o refugio ainda le a carta -- e o unico lugar em que le e o
	/// <see cref="AncoraDoRefugio"/>, procurando o natal PELO NOME --, entao um planeta a mais na
	/// frente da lista NAO PODE mover ninguem. Se mover, a posicao numa lista voltou a decidir
	/// destino, e e exatamente isso que o pedido do dono substituiu.
	///
	/// Sem esta manivela a frase *"a ordem da carta nao importa mais"* seria um argumento; com ela e
	/// uma medida. Mesma disciplina do <see cref="_celulasDeRefugio"/>: aperta-se o PARAMETRO contra o
	/// codigo de producao, em vez de escrever um caminho paralelo que testaria o atalho.
	/// ====================================================================================================
	/// </summary>
	private IReadOnlyList<PlanetaNoEspaco>? _cartaDoRefugio;

	/// <summary>
	/// O escopo: `using (CartaDeRefugioCom(intruso)) { ... }` -- os intrusos entram NA FRENTE da carta
	/// de verdade, que e onde o recuo antigo os encontrava primeiro.
	/// </summary>
	private CartaDeRefugioTrocada CartaDeRefugioCom(params PlanetaNoEspaco[] naFrente) =>
		new(this, [.. naFrente, .. Espaco.PreFeitos()]);

	/// <summary>Devolve a carta de verdade no fim, mesmo se a bancada estourar no meio.</summary>
	internal sealed class CartaDeRefugioTrocada : IDisposable
	{
		private readonly GameServer _s;
		private readonly IReadOnlyList<PlanetaNoEspaco>? _antes;

		internal CartaDeRefugioTrocada(GameServer s, IReadOnlyList<PlanetaNoEspaco> carta)
		{
			_s = s;
			_antes = s._cartaDoRefugio;
			s._cartaDoRefugio = carta;
		}

		public void Dispose() => _s._cartaDoRefugio = _antes;
	}

	// =====================================================================
	// A REGRA
	// =====================================================================
	/// <summary>
	/// **PARA ONDE VAI ESTE CORPO** quando o berco dele nao existe mais. Chamado de um lugar so --
	/// o <see cref="DestinoDoBerco"/>, que e o funil de todo mundo.
	///
	/// ============================ A ANCORA E O NATAL, E NAO ONDE O CORPO ESTA ============================
	/// O dono escreveu *"proximo do planeta natal dele"*. Medir de onde o corpo caiu daria um refugio
	/// "perto" de um canto do mapa que a pessoa talvez nunca tenha visto -- e faria duas mortes no
	/// mesmo dia mandarem o mesmo jogador pra dois cantos diferentes da galaxia.
	///
	/// `b.Pos` E a posicao do natal no caso comum. Quando o jogador pediu pra nascer perto de casa,
	/// `b.Pos` e a IRMA de orbita -- a 0,14 celula do natal, ou seja a mesma ancora pra qualquer efeito
	/// (e de quebra: se a irma morrer e o natal viver, o mundo vivo mais perto E O NATAL, e a pessoa
	/// volta pra casa sem uma linha de codigo falando disso).
	///
	/// **`K < 0` NAO TEM POSICAO**: Paraiso e Inferno existem como zona e nao como corpo, e o `Pos`
	/// deles e zero por ausencia e nao por escolha. A ancora vira a ORIGEM da carta explicitamente --
	/// que e onde a Terra fica, e onde o zero do universo esta. O mesmo vale pro corpo SEM BERCO
	/// (clone, NPC, corpo de bancada), que tambem chega aqui.
	/// ================================================================================================
	/// </summary>
	/// <param name="semente">
	/// A semente DESTE personagem -- ela decide QUAL dos mundos vizinhos sai (ver
	/// <see cref="Arredores.Sorteia"/>). Sem ela, as dez racas que nascem na Terra acordariam todas
	/// no mesmo pedregulho no dia em que a Terra acabasse.
	/// </param>
	private (ZoneKey Zona, Vec2 Pos) RefugioDoBerco(Berco b, ServerPlayer? dono, ulong semente)
	{
		string casa = CasaDe(b);
		Vec2 ancora = AncoraDoRefugio(b);

		List<Dominio> dominios = DominiosDeRefugio(dono, ancora);
		Arredores perto = VizinhancaDeRefugio(ancora);
		MundoDeRefugio? sorteado = perto.Sorteia(semente);
		bool terra = TerraServeDeRefugio();

		// ============================ (v0.3.0) A TERRA ESCOLHIDA VEM PRIMEIRO ============================
		// B3 e PEDIDA (`RefugioNaTerra`), como a B1 -- e quem marcou um dominio nem chega aqui: o `DestinoDe` o atende antes,
		// e o verb apaga a Terra quando o dominio e escolhido (uma escolha so). Com a Terra MORTA a escolha fica guardada e
		// nao vale: o corpo segue a regra de sempre, e nunca vai parar num cadaver.
		// ============================================================================================
		if (terra && dono is { RefugioNaTerra: true })
		{
			ContarORefugio(dono, casa, TerraDoRefugio.Name, MotivoDoRefugio.Terra, dominios.Count, perto.CelulasOlhadas, terra);
			return PousarNo(BercoDaTerra(casa));
		}

		// ============================ AS DUAS EXISTEM: QUEM DECIDE E O JOGADOR ============================
		// E aqui que o "ou ... ou" do dono vira codigo. O DESTINO DE AGORA e a vizinhanca, e nao o
		// dominio -- e a razao e estrutural e nao gosto:
		//
		//   o dominio ja tem uma porta de OPT-IN construida e escrita (`Dominio.EhOSpawn`, o verb
		//   `conq_spawn`: *"voce passa a renascer em X enquanto for o soberano"*). Se a AUSENCIA desse
		//   bit tambem significasse "renasco no dominio", o bit deixaria de significar coisa alguma --
		//   e todo dono de planeta deste servidor passaria a renascer noutro lugar sem ter pedido nada.
		//
		// Entao: B1 e sempre pedido, B2 e sempre o padrao, e a tela poe o pedido a um clique de
		// distancia. Quem ja marcou um dominio nem chega aqui -- o <see cref="DestinoDe"/> o atende
		// antes. Ver <see cref="OferecerORefugio"/> pro momento em que a pergunta e feita (a chegada ao
		// Outro Mundo, 60 s antes de o corpo voltar), que e o que faz a escolha valer PRA ESTA morte e
		// nao pra proxima.
		// ============================================================================================
		if (dominios.Count > 0 && sorteado is { } comEscolha)
		{
			ContarORefugio(dono, casa, comEscolha.Corpo.Nome,
				perto.Reserva ? MotivoDoRefugio.MundoPesado : MotivoDoRefugio.PertoDoNatal,
				dominios.Count, perto.CelulasOlhadas, terra);
			return PousarNo(BercoDoRefugio(comEscolha, casa));
		}

		// ---- SO UMA EXISTE: ela e o destino, e ninguem e perguntado -------
		if (sorteado is { } so)
		{
			ContarORefugio(dono, casa, so.Corpo.Nome,
				perto.Reserva ? MotivoDoRefugio.MundoPesado : MotivoDoRefugio.PertoDoNatal,
				0, perto.CelulasOlhadas, terra);
			return PousarNo(BercoDoRefugio(so, casa));
		}

		if (dominios.Count > 0 && CorpoDoDominio(dominios[0]) is { } doDominio)
		{
			Dominio d = dominios[0];
			ContarORefugio(dono, casa, d.Planeta, MotivoDoRefugio.Dominio, 0, perto.CelulasOlhadas, terra);
			return PousarNo(new Berco
			{
				Planeta = doDominio.Nome,
				Seed = doDominio.Seed,
				PreFeito = doDominio.Premade,
				Sx = d.Sx, Sy = d.Sy, K = d.K,
				Pos = doDominio.Pos,
				Raio = doDominio.Raio,
				Gravidade = doDominio.Premade ? 0 : MundoProcedural.GravidadeDaSeed(doDominio.Seed),
				Natal = casa,
			});
		}

		// ============================ (v0.3.0) SEM DOMINIO E SEM VIZINHANCA, SOBRA A TERRA ============================
		// Uma saida so, e a regra desta casa e "com uma so, ela e o destino e ninguem e perguntado". A Terra viva e chao de
		// verdade -- povo, colisao e o marco de nascimento do mapa --, melhor que o vacuo, que continua sendo o ultimo recurso
		// pra quando ela TAMBEM acabou. Em jogo isto e tao inalcancavel quanto o vacuo (a vizinhanca nunca sai vazia); a
		// bancada o alcanca pelo mesmo `_celulasDeRefugio`.
		// ======================================================================================================
		if (terra)
		{
			ContarORefugio(dono, casa, TerraDoRefugio.Name, MotivoDoRefugio.Terra, 0, perto.CelulasOlhadas, terra);
			return PousarNo(BercoDaTerra(casa));
		}

		// ============================ O ULTIMO RECURSO: O ESPACO ABERTO ============================
		// **DECISAO ESCRITA, porque a instrucao e "prefira a resposta que NAO PRENDE NINGUEM".**
		//
		// O que havia antes era pior que nada: o `ZonaDeRecuoViva` desistia devolvendo "a Terra, viva
		// ou MORTA" -- um corpo num cadaver, sem colisao carregada e sem povo, e o login seguinte
		// caia no mesmo funil e devolvia a mesma Terra morta. Era um LACO, e nenhuma bancada o cobria.
		//
		// O espaco aberto e o oposto disso: e o unico lugar deste jogo de onde se alcanca TODOS os
		// outros. Dali se abre a carta estelar, se escolhe um mundo e se voa. Nao ha chao, e e por
		// isso mesmo que ele nao prende: o corpo nao esta preso num lugar, esta ENTRE lugares.
		//
		// A coordenada e a do natal -- o corpo abre os olhos exatamente onde o mundo dele ficava, o
		// que e a unica coisa honesta a se fazer com esse ponto do mapa. Em jogo isto e inalcancavel
		// (a vizinhanca so fica vazia com a busca desligada); a bancada o alcanca pelo
		// <see cref="_celulasDeRefugio"/> com a Terra morta junto (desde a v0.3.0 ela vem antes do vacuo), porque recurso
		// que nunca roda e recurso que nao existe.
		// ======================================================================================
		ContarORefugio(dono, casa, "", MotivoDoRefugio.OEspacoAberto, dominios.Count,
					   perto.CelulasOlhadas, terra);
		return (ZonaDoEspaco, ancora);
	}

	/// <summary>
	/// O PLANETA NATAL DESTE BERCO -- o nome que responde "de onde esta pessoa e".
	///
	/// `Natal` e o campo certo e `Planeta` e o recuo: quem nasceu numa orbita irma (a opcao "perto de
	/// casa" da criacao) tem `Planeta` = a irma e `Natal` = o mundo do povo dele, e e o segundo que o
	/// pedido do dono cita. Corpo SEM BERCO (clone, NPC de bancada) nao tem nem um nem outro.
	/// </summary>
	private static string CasaDe(Berco b) => b.Natal is { Length: > 0 } ? b.Natal : b.Planeta ?? "";

	/// <summary>
	/// **DE ONDE SE MEDE "PERTO"** -- a posicao do planeta NATAL na carta estelar.
	///
	/// ============================ UMA CASA SO, PORQUE SAO TRES CHAMADORES ============================
	/// O destino (<see cref="RefugioDoBerco"/>) e a oferta (<see cref="EscolhaDeRefugio"/>) tem que
	/// medir do MESMO ponto: a tela lista os mundos que a busca achou e o corpo vai parar num deles.
	/// Duas contas de ancora divergindo dariam uma tela oferecendo tres mundos e um corpo acordando
	/// num quarto -- e "a regra num chamador, esquecida no outro" e o defeito mais repetido deste port.
	/// ============================================================================================
	///
	/// ============================ E ELA NAO CONFIA NO `Berco.Pos` ============================
	/// `b.Pos` costuma servir, e nao serve sempre: o berco de um NPC e montado a mao com so tres
	/// campos (`GameServer.Npc.cs`, o bloco "O BERCO DELE E ONDE ELE NASCEU"), entao `Pos` fica em
	/// (0,0) -- **que e exatamente onde a Terra esta**. Um cidadao de Namek cujo planeta acabasse
	/// procuraria refugio na vizinhanca da TERRA, calado e plausivel.
	///
	/// Perguntar a CARTA pelo nome do natal responde certo nos dois casos, com a mesma fonte que a
	/// carta estelar desenha. O `b.Pos` fica como recuo pra quando o nome nao esta na carta, e a
	/// ORIGEM como ultimo recuo -- que e o caso do Paraiso e do Inferno (`K = -1`: existem como zona
	/// e nao como corpo) e o do corpo sem berco nenhum.
	/// =====================================================================================
	///
	/// **DE GRACA VEM UM CASO BOM**: quem nasceu numa orbita irma e viu a IRMA morrer tem o natal
	/// vivo a distancia ZERO da ancora -- ou seja, ele volta pra casa sem uma linha de codigo
	/// falando disso.
	/// </summary>
	private Vec2 AncoraDoRefugio(Berco b)
	{
		string casa = CasaDe(b);

		// **ESTA E A UNICA LEITURA DA CARTA EM TODO O CAMINHO DO REFUGIO**, e ela procura o natal
		// PELO NOME -- a posicao na lista nao entra na conta. Ver <see cref="_cartaDoRefugio"/>: a
		// bancada troca a carta por uma com um planeta ficticio na frente e exige que NINGUEM se mova.
		foreach (PlanetaNoEspaco p in _cartaDoRefugio ?? Espaco.PreFeitos())
			if (string.Equals(p.Nome, casa, StringComparison.OrdinalIgnoreCase)) return p.Pos;

		return b.NoMapaDoUniverso ? b.Pos : default;
	}

	/// <summary>
	/// A VIZINHANCA VIVA DE UM PONTO -- a chamada de producao do <see cref="Refugios.MundosPertoDe"/>.
	///
	/// O crivo `existe` junta as DUAS maneiras de um corpo nao servir de destino, e as duas ja eram
	/// perguntadas pelo recuo antigo:
	///   * ele foi DESTRUIDO (<see cref="ZonaMorta"/> -- a fonte unica da destruicao de planeta);
	///   * ele e pre-feito e NAO TEM MAPA no manifesto. Um planeta sem `.dmm` convertido nao e um
	///     planeta destruido, e o registro de mortos nao tem por que saber disso -- mas tambem nao e
	///     um lugar onde um corpo pode acordar.
	/// </summary>
	private Arredores VizinhancaDeRefugio(Vec2 ancora) => Refugios.MundosPertoDe(
		SeedDoUniverso, ancora,
		p => !_mortos.Morto(p) && (!p.Premade || _catalogo?.Get(ZoneKey.Premade(p.Nome)) != null),
		celulas: _celulasDeRefugio);

	/// <summary>
	/// OS DOMINIOS QUE PODEM SER REFUGIO -- os desta assinatura que ainda existem, do mais perto de
	/// casa pro mais longe.
	///
	/// A ORDEM E POR DISTANCIA DO NATAL e nao a de insercao no livro: quando o dominio e a UNICA
	/// opcao (vizinhanca vazia) e preciso escolher um deles sem perguntar, e "o primeiro da lista" e
	/// exatamente o tipo de regra que o `ConquistaSpawn` ja se recusou a ter (*"uma escolha decidida
	/// pela ordem de insercao, que e o pior tipo de regra que existe"*).
	///
	/// `dono == null` devolve vazio, e nao e caso de canto: e o NASCIMENTO. Um personagem que esta
	/// sendo criado nao tem assinatura no livro, nao tem BP pra conquistar nada
	/// (<see cref="Jandirus.Core.Social.Conquista.BpMinimoSemPovo"/> = 250.000 contra os 10 de quem
	/// nasce) e nem existe ainda -- B1 e impossivel na criacao pelas tres razoes, e por isso a tela de
	/// criacao so oferece a vizinhanca.
	/// </summary>
	private List<Dominio> DominiosDeRefugio(ServerPlayer? dono, Vec2 ancora)
	{
		if (dono == null) return [];

		var vivos = new List<(Dominio D, double Dist)>();
		foreach (Dominio d in DominiosDe(dono.Assinatura))
		{
			if (PlanetaDoDominioMorreu(d) || CorpoDoDominio(d) is not { } p) continue;
			double dx = p.Pos.X - ancora.X, dy = p.Pos.Y - ancora.Y;
			vivos.Add((d, Math.Sqrt(dx * dx + dy * dy)));
		}

		return [.. vivos.OrderBy(v => v.Dist).ThenBy(v => v.D.Chave.Texto, StringComparer.Ordinal)
					   .Select(v => v.D)];
	}

	/// <summary>
	/// UM MUNDO DE REFUGIO VIRA UM BERCO, pra entrar no MESMO <see cref="PousarNo"/> de todo mundo.
	///
	/// **O ENDERECO (Sx, Sy, K) E O QUE FAZ O MUNDO GERADO EXISTIR DE VERDADE QUANDO O CORPO CHEGA.**
	/// Sem `K >= 0` o <see cref="Berco.NoEspaco"/> devolve nulo, ninguem encomenda o terreno e o corpo
	/// pousaria numa zona sem mapa -- a armadilha que o `--geradoteste` ja documentou. Com ele, o
	/// `PousarNo` poe o corpo em ORBITA e o `TickDoEspaco` gera o mundo e pousa pelo caminho de
	/// verdade. Ver o cabecalho do <see cref="MundoDeRefugio"/>.
	///
	/// O `Natal` NAO e reescrito: continua sendo de onde esta pessoa veio. E o que a ficha, a dica de
	/// classe e a propria proxima busca de refugio leem -- se o mundo de refugio tambem morrer um dia,
	/// a ancora tem que continuar sendo casa e nao o abrigo anterior.
	/// </summary>
	private static Berco BercoDoRefugio(MundoDeRefugio m, string natal) => new()
	{
		Planeta = m.Corpo.Nome,
		Seed = m.Corpo.Seed,
		PreFeito = m.Corpo.Premade,
		Sx = m.Sx,
		Sy = m.Sy,
		K = m.K,
		Pos = m.Corpo.Pos,
		Raio = m.Corpo.Raio,
		Gravidade = m.Corpo.Premade ? 0 : MundoProcedural.GravidadeDaSeed(m.Corpo.Seed),
		Natal = natal,
		Motivo = MotivoDoBerco.VizinhoDoNatal,
	};

	// =====================================================================
	// (v0.3.0) A TERRA -- o ponto de encontro, a saida B3
	// =====================================================================
	/// <summary>
	/// **A TERRA, O PONTO DE ENCONTRO PRINCIPAL DO JOGO** -- ver o bloco (v0.3.0) do cabecalho deste arquivo.
	///
	/// ============================ "SEMPRE" TEM DUAS BORDAS, E AS DUAS SAO DO MUNDO ============================
	/// A Terra tem que estar VIVA -- ela tambem pode ser destruida, e entao a opcao some e quem a tinha escolhido volta pela
	/// vizinhanca, nunca por um cadaver -- e tem que ter MAPA (a mesma exigencia que a vizinhanca faz a todo pre-feito). Quem
	/// nasceu na Terra e a perdeu nao tem a opcao porque ela e justamente o planeta que acabou.
	///
	/// A escolha mora no PERSONAGEM (`ServerPlayer.RefugioNaTerra`) e nao no livro de dominios como a B1: a Terra nao e de
	/// ninguem, e nao ha bandeira pra ir ate ela.
	/// ==========================================================================================================
	/// </summary>
	private static readonly ZoneKey TerraDoRefugio = ZoneKey.Premade("Earth");

	/// <summary>A TERRA SERVE DE REFUGIO AGORA? A pergunta unica -- o destino, a oferta, o verb e a tela leem esta linha.</summary>
	private bool TerraServeDeRefugio() =>
		!TerraForaDoRefugioDeTeste && !ZonaMorta(TerraDoRefugio) && _catalogo?.Get(TerraDoRefugio) != null;

	/// <summary>
	/// DEFEITO INJETADO (`--bercoprova` familia 11, `--diagv030` familia TR): o refugio sem a Terra -- o jogo de antes. Falso
	/// em jogo, sempre.
	/// </summary>
	internal static bool TerraForaDoRefugioDeTeste;

	/// <summary>
	/// O BERCO QUE LEVA A TERRA, pra entrar no MESMO <see cref="PousarNo"/> de todo mundo: o pre-feito pousa no marco do mapa
	/// dela (`PontoDeNascimento`), o mesmo ponto de quem nasce la -- e e isso que faz dela um ponto de ENCONTRO. O `Natal`
	/// nao e reescrito, pelo motivo escrito no <see cref="BercoDoRefugio"/>.
	/// </summary>
	private static Berco BercoDaTerra(string natal) => new()
	{
		Planeta = TerraDoRefugio.Name,
		PreFeito = true,
		Natal = natal,
	};

	/// <summary>Onde a Terra fica na carta -- perguntado a carta, e nao cravado na origem. Le a distancia da oferta.</summary>
	private static Vec2 PosicaoDaTerra()
	{
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (string.Equals(p.Nome, TerraDoRefugio.Name, StringComparison.OrdinalIgnoreCase)) return p.Pos;
		return default;
	}

	/// <summary>
	/// **O JOGADOR TEM QUE SABER QUE O DESTINO DELE MUDOU.** Antes disto havia um `GD.Print` -- ou
	/// seja, o console do servidor sabia, e a pessoa que perdeu o planeta natal nao.
	///
	/// A frase e diferente por motivo porque os quatro casos sao coisas diferentes de se viver: um
	/// mundo vizinho, um mundo pesado, o proprio territorio conquistado e o vacuo. E quando ha escolha
	/// a linha diz que ela existe -- sem isso a tela seria a unica pista, e quem a fechasse sem ler
	/// nunca mais saberia que podia escolher.
	/// </summary>
	/// <param name="celulas">
	/// Quantas celulas de sistema a busca varreu (<see cref="Arredores.CelulasOlhadas"/>). Vai pro
	/// log e nao pro chat: e o numero que responde *"por que esta pessoa foi parar no vacuo"* quando
	/// alguem for investigar. Zero quer dizer que a busca estava DESLIGADA -- em jogo isso nao
	/// acontece, e e exatamente por isso que o numero precisa aparecer se acontecer.
	/// </param>
	/// <param name="terra">(v0.3.0) A Terra servia de saida -- entra na linha das outras saidas.</param>
	private void ContarORefugio(ServerPlayer? dono, string natal, string destino,
								MotivoDoRefugio motivo, int dominios, int celulas, bool terra)
	{
		string casa = NomeDoPlaneta(natal);
		GD.Print($"[refugio] {dono?.Name ?? "(corpo sem dono)"}: '{natal}' nao existe mais -- "
			   + $"{motivo} -> '{(destino.Length > 0 ? destino : Espaco.NomeDoEspaco)}' "
			   + $"({dominios} dominio(s) disponivel(is), {celulas} celula(s) olhada(s))");

		if (dono == null) return;

		Avisar(dono, motivo switch
		{
			MotivoDoRefugio.Dominio =>
				$"{casa} nao existe mais. Voce abre os olhos no seu proprio territorio: "
				+ $"{NomeDoPlaneta(destino)}, onde a sua bandeira esta fincada.",

			MotivoDoRefugio.PertoDoNatal =>
				$"{casa} nao existe mais. A estrela continua la, e voce acorda no mundo vivo mais "
				+ $"perto de onde era casa: {destino}.",

			MotivoDoRefugio.MundoPesado =>
				$"{casa} nao existe mais. O que sobrou perto de casa e pesado demais pra qualquer um "
				+ $"-- e mesmo assim e melhor que lugar nenhum. Voce acorda em {destino}, e o proprio "
				+ "corpo vai ter que se acostumar.",

			MotivoDoRefugio.Terra =>
				$"{casa} não existe mais. Você volta pela Terra -- o ponto de encontro de todo mundo.",

			_ =>
				$"{casa} nao existe mais, e nao havia para onde ir. Voce abre os olhos no vacuo, "
				+ "exatamente onde o seu mundo ficava. Nao ha chao -- ha a carta estelar.",
		});

		// AS OUTRAS SAIDAS, numa linha so -- sem ela a tela seria a unica pista, e quem a fechasse sem ler nunca mais saberia
		// que podia escolher. (v0.3.0) A Terra entrou na lista, e a porta passou a ser o cartao Refugio da aba World: a frase
		// apontava pra aba Nav, que passou a exigir o item Nav System -- uma porta que nem todo mundo tem.
		var outras = new List<string>(2);
		if (dominios > 0 && motivo != MotivoDoRefugio.Dominio)
			outras.Add(dominios == 1 ? "no planeta que você conquistou" : $"num dos {dominios} planetas que você conquistou");
		if (terra && motivo != MotivoDoRefugio.Terra) outras.Add("na Terra, o ponto de encontro de todo mundo");
		if (outras.Count > 0)
			Avisar(dono, $"dá pra renascer {string.Join(" ou ", outras)} em vez de aqui -- o cartão Refúgio (menu P, aba World) "
					   + "tem as opções.");
	}

	// =====================================================================
	// A PERGUNTA
	// =====================================================================
	/// <summary>
	/// ESTE PERSONAGEM PERDEU O BERCO? A pergunta unica -- a tela, o verb e a oferta leem esta linha.
	///
	/// O corpo SEM BERCO (clone, NPC, corpo de bancada) responde falso: ele nao tem natal pra perder,
	/// e oferecer refugio a ele seria oferecer uma escolha a quem nao tem personagem.
	/// </summary>
	private bool PerdeuOBerco(ServerPlayer pl) =>
		pl.Berco.Planeta is { Length: > 0 } && ZonaMorta(pl.Berco.Zona);

	/// <summary>
	/// **AS SAIDAS QUE ESTA PESSOA TEM AGORA** -- as duas listas do pedido do dono e se ha ESCOLHA.
	///
	/// ============================ POR QUE ELA E UMA FUNCAO, E NAO CODIGO DENTRO DA OFERTA ============================
	/// Duas pontas leem a mesma resposta e nenhuma pode inventar a dela:
	///   * a OFERTA (<see cref="OferecerORefugio"/>), que so empurra a tela quando ha o que decidir;
	///   * a BANCADA, que precisa afirmar "aqui havia escolha" / "aqui havia uma saida so" contra o
	///     codigo de producao, num corpo que nao tem cliente e portanto nunca recebe pacote.
	///
	/// A bancada afirma as DUAS METADES: esta (a escolha que existia) e o DESTINO de verdade
	/// (`DestinoDe`, o corpo no chao). Uma sozinha seria medir intencao; a outra sozinha nao
	/// distinguiria "foi pra vizinhanca porque escolheu" de "foi porque nao havia mais nada".
	/// ============================================================================================================
	///
	/// A ESCOLHA SO EXISTE COM DUAS SAIDAS OU MAIS. Com uma saida so nao ha nada pra decidir, e empurrar uma tela
	/// nessa hora seria roubar o clique de quem esta morto no meio de uma briga.
	/// </summary>
	private (Vec2 Ancora, List<Dominio> Dominios, Arredores Perto, bool Escolha) EscolhaDeRefugio(ServerPlayer pl)
	{
		Vec2 ancora = AncoraDoRefugio(pl.Berco);
		List<Dominio> dominios = DominiosDeRefugio(pl, ancora);
		Arredores perto = VizinhancaDeRefugio(ancora);
		// (v0.3.0) AS SAIDAS SAO TRES, e a escolha existe com DUAS: a Terra conta (`TerraServeDeRefugio`) -- e por isso a tela
		// passou a abrir pra quem perdeu o natal mesmo sem territorio nenhum.
		int saidas = (dominios.Count > 0 ? 1 : 0) + (perto.Vazia ? 0 : 1) + (TerraServeDeRefugio() ? 1 : 0);
		return (ancora, dominios, perto, saidas >= 2);
	}

	/// <summary>
	/// **A PERGUNTA CHEGA NO OUTRO MUNDO, E NAO NA HORA DE VOLTAR.**
	///
	/// ============================ POR QUE AQUI, E POR QUE NADA BLOQUEIA ============================
	/// O `GameServer.Conquista.cs` registra uma decisao de arquitetura deste port: as perguntas
	/// modais do DM (`alert()`, `input()`) foram MORTAS, *"pra que nada bloqueie o tique esperando um
	/// clique"*. Essa decisao continua de pe aqui, e ela e o motivo do desenho:
	///
	///   * o SERVIDOR nunca espera. Ele decide o destino sozinho, com o padrao, e segue;
	///   * a pergunta e feita na CHEGADA AO OUTRO MUNDO (<see cref="IrProAlem"/>), e fica aberta
	///     ate alguem pagar a volta (esferas, tecnica, Enma -- nao ha mais prazo la). Quem responde
	///     antes dela muda o destino DESTA morte; quem nao responde volta pelo padrao e continua
	///     podendo escolher depois, porque a resposta e uma preferencia e nao um voto de uma vez so;
	///   * a tela do cliente nao trava tecla nenhuma (mesmo molde da telinha de meditar).
	///
	/// UMA VEZ SO (v0.3.0; era uma por sessao). O aviso e sobre uma catastrofe, nao sobre uma morte nem sobre um
	/// login -- o dono: *"sempre q entra no jogo e essa pergunta chata q ja respondi antes"*. A marca
	/// (`ServerPlayer.RefugioPerguntado`) vai pro disco, e o natal que volta a zera. Depois da primeira, a porta e o
	/// cartao Refugio da aba World do menu P -- e o verb sem argumento (`pedidoDoJogador`), que abre sempre.
	/// ==========================================================================================
	/// </summary>
	private void OferecerORefugio(ServerPlayer pl, bool podeAbrir, bool pedidoDoJogador = false)
	{
		if (pl.Peer == null || !EhJogador(pl)) return;
		if (!PerdeuOBerco(pl))
		{
			EsquecerAPerguntaDoRefugio(pl);
			return;
		}

		(Vec2 ancora, List<Dominio> dominios, Arredores perto, bool escolha) = EscolhaDeRefugio(pl);
		bool abrir = DeveAbrirORefugio(pl, podeAbrir, pedidoDoJogador, escolha);
		if (abrir && !pl.RefugioPerguntado) { pl.RefugioPerguntado = true; Persistir(pl); }

		var w = Protocol.Begin(Protocol.S2C.Refugio);
		w.Put(true);                                     // o berco desta pessoa esta destruido
		w.Put(abrir);
		w.Put(NomeDoPlaneta(CasaDe(pl.Berco)));

		w.Put((byte)Math.Min(dominios.Count, byte.MaxValue));
		foreach (Dominio d in dominios)
		{
			w.Put(d.Chave.Texto);
			w.Put(NomeDoPlaneta(d.Planeta));
			w.Put(d.EhOSpawn);
			w.Put((float)MinutosDeVoo(ancora, CorpoDoDominio(d)?.Pos ?? ancora));
		}

		w.Put((byte)Math.Min(perto.Mundos.Count, byte.MaxValue));
		foreach (MundoDeRefugio m in perto.Mundos)
		{
			w.Put(m.Corpo.Nome);
			w.Put((float)MinutosDeVoo(ancora, m.Corpo.Pos));
			w.Put((float)(m.Corpo.Premade ? 0 : MundoProcedural.GravidadeDaSeed(m.Corpo.Seed)));
			w.Put(m.ServeDeBerco);
		}

		w.Put(perto.Reserva);

		// (v0.3.0) A TERRA: se ela serve, se e a escolhida, e a quantos minutos de voo de onde era casa. Ver `S2C.Refugio`.
		bool terra = TerraServeDeRefugio();
		w.Put(terra);
		w.Put(terra && pl.RefugioNaTerra);
		w.Put((float)MinutosDeVoo(ancora, PosicaoDaTerra()));
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// (v0.3.0) A TELA SE EMPURRA AGORA? So com o que decidir (`escolha`), so onde a pergunta pode abrir (`podeAbrir`: a
	/// chegada ao Outro Mundo, o login) e so UMA VEZ por catastrofe -- a marca gravada no personagem
	/// (`ServerPlayer.RefugioPerguntado`). O pedido do proprio jogador (o verb sem argumento) abre sempre. Funcao propria pra
	/// a bancada medir a regra num corpo sem tela, que nunca recebe o pacote.
	/// </summary>
	private static bool DeveAbrirORefugio(ServerPlayer pl, bool podeAbrir, bool pedidoDoJogador, bool escolha) =>
		podeAbrir && escolha && (pedidoDoJogador || !pl.RefugioPerguntado || RefugioSempreAbreDeTeste);

	/// <summary>
	/// DEFEITO INJETADO (`--bercoprova` familia 12, `--diagv030` familia TR): a tela volta a abrir a cada login -- a marca
	/// "ja perguntei" e ignorada, o jogo de antes. Falso em jogo, sempre.
	/// </summary>
	internal static bool RefugioSempreAbreDeTeste;

	/// <summary>
	/// (v0.3.0) O NATAL ESTA DE PE (ou voltou: um desejo das Esferas ressuscita mundo) -- a marca do "ja perguntei" se apaga, e a
	/// proxima catastrofe pergunta de novo. Chamada pelas duas portas que descobrem o berco vivo: a oferta
	/// (`OferecerORefugio`) e o verb (`ComandoDeRefugio`, o caminho do cartao do menu P).
	/// </summary>
	private void EsquecerAPerguntaDoRefugio(ServerPlayer pl)
	{
		if (!pl.RefugioPerguntado) return;
		pl.RefugioPerguntado = false;
		Persistir(pl);
	}

	/// <summary>
	/// A DISTANCIA EM MINUTOS DE VOO BASE. E a unica unidade legivel deste universo -- ninguem sabe o
	/// que sao 9.800 px, e todo mundo sabe o que e um minuto voando. Sai das mesmas duas constantes
	/// que a carta estelar usa (<see cref="Espaco.SegundosDeViagem"/> sobre a
	/// <see cref="MoveRules.BaseSpeedPx"/>), e nao de um numero cravado aqui.
	/// </summary>
	private static double MinutosDeVoo(Vec2 a, Vec2 b)
	{
		double dx = b.X - a.X, dy = b.Y - a.Y;
		return Espaco.SegundosDeViagem(Math.Sqrt(dx * dx + dy * dy)) / 60.0;
	}

	// =====================================================================
	// O VERBO
	// =====================================================================
	/// <summary>
	/// O CANAL DO REFUGIO. Quatro argumentos, e os de escrever sao os unicos com trava:
	///   `""`             -- me manda a oferta de novo (o botao do menu abre a tela com isto);
	///   `"vizinhanca"`   -- eu escolho B2: nenhum dominio meu e ponto de renascimento, e a Terra tambem nao;
	///   `"terra"`        -- (v0.3.0) eu escolho B3: a Terra, o ponto de encontro (`ServerPlayer.RefugioNaTerra`);
	///   `<chave>`        -- eu escolho B1: este dominio passa a ser.
	///
	/// UMA ESCOLHA SO: cada uma das tres apaga as outras duas. A Terra nao tem campo no livro de dominios porque nao e de
	/// ninguem -- mora no personagem e vai pro save dele.
	///
	/// ============================ ELE ESCREVE NO MESMO CAMPO QUE O `conq_spawn`, E SO PODE ============================
	/// A escolha nao ganhou campo novo: ela e o `Dominio.EhOSpawn` que ja existe, com a mesma regra de
	/// UM SO ligado por assinatura. Um segundo lugar guardando "onde eu renasco" seria duas verdades
	/// pra a mesma pergunta -- e a que erraria primeiro seria a que ninguem olha.
	///
	/// **E POR ISSO ELE SO ABRE COM O BERCO MORTO.** O `conq_spawn` exige estar de pe JUNTO DA
	/// BANDEIRA (o `get_dist(usr, src) > 1` do DM), e um verb remoto que escrevesse o mesmo bit sem
	/// essa exigencia apagaria a regra do irmao dele calado -- e o modo de falha "regra num chamador,
	/// esquecida no outro" que este port ja pagou mais de uma vez.
	///
	/// A fronteira e exatamente o pedido do dono: **so quando a raca ficou sem planeta natal**. Nesse
	/// caso ir ate a bandeira e impossivel de exigir (o mundo acabou, e quem esta escolhendo costuma
	/// estar morto no Outro Mundo), e e o unico caso em que a escolha nao existia antes.
	/// ==========================================================================================================
	/// </summary>
	private bool ComandoDeRefugio(ServerPlayer pl, string cmd, string arg)
	{
		if (cmd != "refugio") return false;

		if (!PerdeuOBerco(pl))
		{
			Avisar(pl, $"{NomeDoPlaneta(CasaDe(pl.Berco))} "
					 + "continua de pe -- e e para la que voce volta. Pra renascer num planeta seu, "
					 + "va ate a sua bandeira e use 'Renascer aqui'.");

			// A TELA TEM QUE SABER QUE NAO PRECISA MAIS. Sem este pacote ela ficaria mostrando a
			// escolha de uma catastrofe que ja passou (um planeta pode voltar por desejo das Esferas).
			EsquecerAPerguntaDoRefugio(pl);
			if (pl.Peer != null)
			{
				var limpo = Protocol.Begin(Protocol.S2C.Refugio);
				limpo.Put(false);
				limpo.Put(false);
				limpo.Put("");
				limpo.Put((byte)0);
				limpo.Put((byte)0);
				limpo.Put(false);
				limpo.Put(false);   // (v0.3.0) a Terra: com o berco de pe nao ha o que oferecer
				limpo.Put(false);
				limpo.Put(0f);
				pl.Peer.Send(limpo, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			}
			return true;
		}

		string escolha = arg.Trim();
		if (escolha.Length == 0) { OferecerORefugio(pl, podeAbrir: true, pedidoDoJogador: true); return true; }

		// ---- (v0.3.0) B3: A TERRA ---------------------------------------------
		if (string.Equals(escolha, "terra", StringComparison.OrdinalIgnoreCase))
		{
			if (!TerraServeDeRefugio())
			{
				Avisar(pl, "a Terra também não existe mais -- não dá pra voltar por ela.");
				OferecerORefugio(pl, podeAbrir: false);
				return true;
			}
			foreach (Dominio o in DominiosDe(pl.Assinatura)) o.EhOSpawn = false;   // uma escolha so
			SalvarConquista();
			pl.RefugioNaTerra = true;
			Persistir(pl);
			Avisar(pl, "você escolhe a Terra: renasce nela, o ponto de encontro de todo mundo.");
			OferecerORefugio(pl, podeAbrir: false);
			return true;
		}

		if (string.Equals(escolha, "vizinhanca", StringComparison.OrdinalIgnoreCase))
		{
			foreach (Dominio o in DominiosDe(pl.Assinatura)) o.EhOSpawn = false;
			SalvarConquista();
			pl.RefugioNaTerra = false;
			Persistir(pl);
			Avisar(pl, "voce escolhe a vizinhanca de casa: renasce no mundo vivo mais perto de onde "
					 + "o seu planeta ficava.");
			OferecerORefugio(pl, podeAbrir: false);
			return true;
		}

		Dominio? alvo = DominiosDe(pl.Assinatura).Find(d => string.Equals(d.Chave.Texto, escolha, StringComparison.Ordinal));
		if (alvo == null || PlanetaDoDominioMorreu(alvo))
		{
			Avisar(pl, "esse dominio nao e mais seu.");
			OferecerORefugio(pl, podeAbrir: false);
			return true;
		}

		foreach (Dominio o in DominiosDe(pl.Assinatura)) o.EhOSpawn = false;
		alvo.EhOSpawn = true;
		SalvarConquista();
		pl.RefugioNaTerra = false;   // (v0.3.0) uma escolha so
		Persistir(pl);
		Avisar(pl, $"voce passa a renascer em {NomeDoPlaneta(alvo.Planeta)}, no seu proprio territorio.");
		OferecerORefugio(pl, podeAbrir: false);
		return true;
	}
}
