using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O BERCO EM JOGO -- o unico lugar do servidor que responde "onde este corpo aparece".
///
/// ============================ DOIS CAMINHOS, UMA REGRA ============================
/// Antes disto havia CINCO lugares cravando `SpawnZone` + `SpawnPos` (a Terra, tile 249,250):
/// o nascimento (`CreateChar`), a morte (`Renascer`), o verb `spawn`, o `admin_spawn` e o recuo do
/// `Entrar` pra zona que sumiu. Cinco copias da mesma decisao e cinco lugares pra a regra
/// envelhecer -- e o modo de falha e o pior possivel: ligar so um deles faria o jogador nascer em
/// casa e ressuscitar na Terra, e ninguem perceberia por semanas.
///
/// O DM tinha um funil so, e ele tem nome: `mob/proc/Locate()` (`SpawnPoints.dm`), chamado tanto
/// pelo nascimento quanto pelo `Return_Mob_To_Spawn`. Aqui o funil e <see cref="DestinoDoBerco"/>:
/// os cinco caminhos passam por ele e nenhum sabe o que e a Terra.
/// ================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// ONDE ESTE PERSONAGEM NASCE, a partir do que esta no disco.
	///
	/// Fina de proposito: a REGRA mora no `Core` (<see cref="Bercos.Onde"/>), porque a tela de
	/// criacao tem que chegar no mesmo planeta sem trocar pacote (regra 0.2). Este metodo so junta
	/// os argumentos que so o servidor tem -- a seed do universo e a CLASSE, que foi sorteada aqui e
	/// que o jogador nao ve.
	///
	/// `Ficha.Class` e nao `Linhagem`: a linhagem e o que o jogador escolheu ("Saiyan", "Primal
	/// Saiyan"), a classe e o que o dado deu ("Low-Class", "Legendary"). As duas excecoes do dono
	/// dependem da segunda, e a primeira e a trava que impede um Heran "Low-Class" de ser despejado
	/// junto (ver o comentario no `Bercos.Onde`).
	/// </summary>
	public Berco BercoDe(CharacterSave c) => Bercos.Onde(
		SeedDoUniverso,
		c.Raca,
		c.Ficha?.Class ?? "",
		c.Linhagem,
		c.SeedDoBerco != 0 ? c.SeedDoBerco : Bercos.SementeDoBerco(c.Nome, c.CriadoEm),
		c.PertoDeCasa);

	/// <summary>
	/// **O FUNIL.** Um berco vira uma zona e um ponto -- e so isto decide onde alguem aparece.
	///
	/// ============================ O PRE-FEITO NAO CHEGA MAIS NUM PONTO FIXO ============================
	/// O (249,250) e o `locate(rand(240,260),rand(240,260),1)` do BYOND, que e o campo aberto do meio
	/// da TERRA -- e ele estava sendo usado como chegada de TODO mapa pre-feito. Em Icer essa celula
	/// e PAREDE (medido no `.col`), entao o Frost Demon, que so agora tem um planeta pra nascer,
	/// nasceria dentro da rocha. `PontoLivrePerto` pergunta a colisao em vez de confiar no numero.
	/// ==============================================================================================
	///
	/// ============================ O GERADO NASCE EM ORBITA, DE PROPOSITO ============================
	/// Cravar a `ZoneKey` de um mundo sorteado parece mais direto e pula justamente o que faz o mundo
	/// EXISTIR: sem `PousarEmProcedural` ninguem encomenda o terreno, `MapaDaZona` devolve nulo, o
	/// passo passa a ser validado so por velocidade e da pra atravessar montanha. E a armadilha que
	/// o `--geradoteste` ja documentou tendo caido nela.
	///
	/// Entao o corpo e posto SOBRE O DISCO, no espaco, e o `TickDoEspaco` faz o resto no proximo
	/// tique pelo caminho de verdade: encomenda o mundo fora do tique, segura o corpo em orbita
	/// enquanto ele nasce ("{planeta} esta se formando sob voce") e pousa. O unico atalho e quando o
	/// mundo JA esta vivo -- o caso comum de morrer no proprio planeta --, e ai a chegada dele ja
	/// existe e nao ha o que esperar.
	/// ==========================================================================================
	/// </summary>
	public (ZoneKey Zona, Vec2 Pos) DestinoDoBerco(Berco b)
	{
		// SEM BERCO = corpo sem dono. O `SpawnPos` continua sendo a resposta, mas passando pela
		// colisao: mesmo na Terra uma construcao levantada em cima do ponto o bloqueia em runtime.
		if (b.Planeta is not { Length: > 0 })
			return (ZonaDeRecuoViva(), PontoDeNascimento(ZonaDeRecuoViva()));

		// ============================ NINGUEM NASCE NUM CADAVER ============================
		// **A saga 1 destroi VEGETA, que e o berco dos Saiyajin.** Sem esta linha, todo Saiyajin
		// criado depois dela -- e toda morte de Saiyajin, que passa por aqui -- mandaria um corpo
		// pra uma zona que nao existe mais: sem colisao carregada, sem povo, e com o `TickDoEspaco`
		// se recusando a pousar la. A pessoa nasceria presa.
		//
		// O recuo e a mesma <see cref="ZonaDeRecuoViva"/> do berco vazio, e nao "o planeta mais
		// parecido": inventar um segundo lar por raca seria uma tabela nova pra envelhecer.
		if (ZonaMorta(b.Zona))
		{
			ZoneKey recuo = ZonaDeRecuoViva();
			GD.Print($"[server] berco: '{b.Planeta}' esta destruido -- o corpo vai pra {recuo.Name}");
			return (recuo, PontoDeNascimento(recuo));
		}

		if (b.PreFeito)
			return (b.Zona, PontoDeNascimento(b.Zona));

		// O mundo gerado ja esta carregado: entra direto no ponto que o gerador garantiu livre.
		if (ChegadaDaZonaGerada(b.Zona) is { } chegada)
			return (b.Zona, chegada);

		if (b.NoEspaco() is { } corpo)
			return (ZonaDoEspaco, corpo.Pos);

		// Nao ha como chegar aqui -- um berco gerado sempre fica no mapa do universo (`K >= 0`).
		// O recuo existe pra que a resposta a um estado impossivel seja "a Terra" e nao um vazio.
		return (SpawnZone, PontoDeNascimento(SpawnZone));
	}

	/// <summary>
	/// ============================ A ZONA DE RECUO -- E ELA PODE MORRER ============================
	/// O `SpawnZone` deste servidor e a Terra, cravado (`GameServer.cs`). Isso era seguro enquanto
	/// nenhum planeta podia deixar de existir; agora nao e: a Terra morre por Final Explosion, e um
	/// elo de saga novo apontado pra ela a mataria tambem. Um `SpawnZone` cravado num planeta morto
	/// manda **todo mundo que renasce** pro cadaver.
	///
	/// Entao o recuo pergunta antes, e desce a lista dos pre-feitos ate achar um vivo. A ORDEM e a
	/// da carta estelar (`Espaco.PreFeitos()`, a Terra primeiro) -- ela ja e a lista canonica de
	/// "lugares onde se pousa", e uma segunda lista aqui envelheceria separada.
	///
	/// Se TODOS morrerem, a resposta continua sendo a Terra: um corpo sem lugar e um corpo no vazio,
	/// que e pior. Nesse caso o servidor grita, porque e um mundo sem nenhum lugar pra nascer.
	/// ======================================================================================
	/// </summary>
	private ZoneKey ZonaDeRecuoViva()
	{
		if (!ZonaMorta(SpawnZone)) return SpawnZone;

		foreach (PlanetaNoEspaco p in _cartaDeRecuo ?? Espaco.PreFeitos())
		{
			var z = ZoneKey.Premade(p.Nome);
			if (!ZonaMorta(z) && _catalogo?.Get(z) != null) return z;
		}

		GD.PushError("[server] TODOS os planetas pre-feitos estao destruidos -- o recuo do berco "
				   + "cai na Terra morta. Um admin precisa restaurar algum planeta.");
		return SpawnZone;
	}

	// =====================================================================
	// A CARTA QUE O RECUO LE -- e por que ela e trocavel
	// =====================================================================
	/// <summary>
	/// A CARTA ESTELAR QUE O <see cref="ZonaDeRecuoViva"/> DESCE. **Nula em jogo**, sempre: quem
	/// responde e <see cref="Espaco.PreFeitos"/>.
	///
	/// ============================ POR QUE ISTO EXISTE ============================
	/// O recuo escolhe "o primeiro pre-feito VIVO da carta", e esse *primeiro* e uma propriedade da
	/// ORDEM de um `yield return` (`Core/World/Espaco.cs:115-121`) -- ou seja, o destino de quem perde
	/// o berco e decidido por uma posicao numa lista, e nao por uma regra. Foi assim que a Terra morta
	/// virou **Namek** e nao qualquer outro planeta: Namek e a segunda linha daquele metodo.
	///
	/// Uma bancada que afirmasse "com a Terra morta o corpo vai parar em Namek" estaria gravando o
	/// ACIDENTE. O `Bercos.PlanetaNatal` ja avisa, com todas as letras, que um dia alguem vai
	/// acrescentar Hera e a Big Gete Star aquela lista ("E uma LINHA em `Espaco.PreFeitos()` pra
	/// cada") -- e no dia em que a linha nova entrar ANTES das outras, o destino do defeito muda de
	/// planeta e a bancada que cravou "Namek" fica verde em cima do mesmo estrago.
	///
	/// Entao a `--bercoprova` TROCA a carta e mede de novo, contra o mesmo codigo de producao: e o
	/// mesmo motivo do `teto` do <see cref="Bercos.ServeDeBerco"/> e do `tetoZona` da
	/// <see cref="Manutencao"/> -- apertar o parametro contra o jogo em vez de escrever um caminho
	/// paralelo que testaria o atalho. Em jogo ninguem escreve neste campo.
	/// ==========================================================================
	/// </summary>
	private IReadOnlyList<PlanetaNoEspaco>? _cartaDeRecuo;

	/// <summary>O escopo: `using (OutraCartaDeRecuo([...])) { ... }`. Ver <see cref="_cartaDeRecuo"/>.</summary>
	private CartaDeRecuoTrocada OutraCartaDeRecuo(IReadOnlyList<PlanetaNoEspaco> carta) => new(this, carta);

	/// <summary>Devolve a carta de verdade no fim, mesmo se a bancada estourar no meio.</summary>
	internal sealed class CartaDeRecuoTrocada : IDisposable
	{
		private readonly GameServer _s;
		private readonly IReadOnlyList<PlanetaNoEspaco>? _antes;

		internal CartaDeRecuoTrocada(GameServer s, IReadOnlyList<PlanetaNoEspaco> carta)
		{
			_s = s;
			_antes = s._cartaDeRecuo;
			s._cartaDeRecuo = carta;
		}

		public void Dispose() => _s._cartaDeRecuo = _antes;
	}

	/// <summary>
	/// O ponto de chegada de uma zona PRE-FEITA, conferido contra a colisao dela.
	///
	/// `MapaDaZonaOuCatalogo` e nao o catalogo cru: e ele que enxerga as construcoes e o cenario
	/// derrubado, que sao estado de runtime. Um mapa que o servidor nao conhece devolve o ponto
	/// cru -- nao ha o que perguntar, e recusar seria pior que arriscar.
	/// </summary>
	private Vec2 PontoDeNascimento(ZoneKey zona) =>
		MapaDaZonaOuCatalogo(zona)?.PontoLivrePerto(SpawnPos) ?? SpawnPos;

	/// <summary>
	/// MANDA UM CORPO QUE JA ESTA EM JOGO PRO BERCO DELE. Morte, verb `spawn`, admin.
	///
	/// O `MoveToZone` cuida de tudo o que a mudanca de zona exige (avisar quem ficou, zerar o
	/// orcamento de passo, derrubar o voo, remandar cenario e obras). O que ele NAO faz e o
	/// protocolo do espaco -- o carimbo de chunk e a vizinhanca --, e sem isso quem cai no espaco
	/// chega sem nenhum planeta desenhado, inclusive sem o que esta bem debaixo dele. E o mesmo
	/// par de linhas que o `DecolarDeProcedural` ja precisou escrever.
	/// </summary>
	private void MandarProBerco(ServerPlayer pl)
	{
		// `DestinoDe` E O FUNIL DE CIMA: ele pergunta primeiro se esta pessoa escolheu um DOMINIO
		// conquistado como ponto de renascimento e, quando nao, cai no `DestinoDoBerco` de sempre.
		// Ver `GameServer.Conquista.cs` -- o dominio nao e um caminho paralelo, e um berco montado do
		// endereco dele.
		(ZoneKey zona, Vec2 pos) = DestinoDe(pl);
		MoveToZone(pl.Id, zona, pos);

		if (!Espaco.EhEspaco(zona)) return;

		pl.ChunkAtual = ChunkId.De(pl.Pos);
		MandarVizinhanca(pl);
		Avisar(pl, $"a orbita de {pl.Berco.Planeta} te recebe. O chao vem em seguida.");
	}

	/// <summary>
	/// O MESMO DESTINO, mas escrito DIRETO no jogador -- sem pacote nenhum.
	///
	/// Existe pro `Entrar`, que roda ANTES de o jogador estar em `_players` e em `ZoneList`: chamar
	/// `MoveToZone` la seria mandar pacote pra quem ainda nao entrou e mexer numa lista de zona em
	/// que ele ainda nao esta. O destino e o mesmo funil; o que muda e quem escreve.
	/// </summary>
	private void PousarNoBercoSemPacote(ServerPlayer pl)
	{
		(ZoneKey zona, Vec2 pos) = DestinoDe(pl);   // ver `MandarProBerco`: o dominio entra pelo funil
		pl.Zone = zona;
		pl.Pos = pos;
	}

	/// <summary>
	/// O BERCO ESCRITO NO SAVE de um personagem recem-criado.
	///
	/// Guarda a ZONA e o PONTO, e nao o berco: o `Entrar` de todo login le `Zona`/`ZonaTipo`/
	/// `ZonaSeed` e a posicao, e o recem-nascido tem que passar por esse mesmo caminho. Quem se
	/// desconectar entre a criacao e o pouso acorda em ORBITA do proprio planeta -- que e onde ele
	/// de fato estava, e e o unico lugar honesto pra deixa-lo.
	/// </summary>
	private void AplicarBercoNoSave(CharacterSave c, Berco b)
	{
		(ZoneKey zona, Vec2 pos) = DestinoDoBerco(b);
		c.Zona = zona.Name;
		c.ZonaTipo = zona.Kind;
		c.ZonaSeed = zona.Seed;
		c.X = pos.X;
		c.Y = pos.Y;
	}

	/// <summary>
	/// A FRASE QUE O JOGADOR LE AO NASCER. Ela e a unica coisa que conta a historia do berco --
	/// sem ela, o Lendario so descobre que foi exilado achando que o jogo quebrou.
	///
	/// ============================ E ELA NAO DIZ A CLASSE ============================
	/// A classe e segredo deste jogo (por isso existe a `DicaDeClasse`), e o berco ja e uma pista
	/// forte: um Saiyajin que acorda na Terra e provavelmente de classe baixa. Isso e do desenho do
	/// dono e nao da pra desfazer -- o que da pra nao fazer e CONFIRMAR. Nenhuma destas frases diz
	/// "voce e classe baixa" nem "voce e Lendario": elas dizem o que aconteceu com o corpo, na voz
	/// do mundo, e deixam a conclusao pro jogador. E o mesmo tom da dica de classe.
	/// ==============================================================================
	/// </summary>
	private static string HistoriaDoBerco(Berco b) => b.Motivo switch
	{
		MotivoDoBerco.ExilioDoLendario =>
			$"Voce nao foi criado em {b.Natal}. Puseram o berco numa capsula e a capsula num rumo "
			+ $"que ninguem anotou -- {b.Planeta} foi so onde ela parou. Havia medo naquela decisao.",

		_ when b.Despejado =>
			"Voce nao foi criado em Vegeta. Mandaram voce ainda bebe a um mundo fraco de se "
			+ "conquistar, e este e ele: a Terra. Ninguem esperava que voce voltasse.",

		MotivoDoBerco.VizinhoDoNatal =>
			$"Voce nao nasceu em {b.Natal}, mas a estrela e a mesma: {b.Planeta} fica ao lado de casa.",

		_ => $"Voce abre os olhos em {b.Planeta}.",
	};
}
