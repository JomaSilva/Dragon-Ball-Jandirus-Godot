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
		SementeDoBercoDe(c),
		c.PertoDeCasa);

	/// <summary>
	/// A SEMENTE DE BERCO DE UM SAVE -- do disco, ou derivada de nome + instante de criacao.
	///
	/// Existe como funcao porque DOIS lugares precisam do mesmo numero: o <see cref="BercoDe"/>, que
	/// escolhe o planeta, e o <see cref="AplicarBercoNoSave"/>, que precisa dele pro sorteio do
	/// REFUGIO quando o planeta ja nao existe. Escrever a expressao duas vezes seria duas fontes pro
	/// mesmo acaso, e a segunda a mudar mandaria a pessoa pra outro mundo calada.
	///
	/// Ver <see cref="Bercos.SementeDoBerco"/> pro porque de o save antigo tambem ter uma.
	/// </summary>
	public static ulong SementeDoBercoDe(CharacterSave c) =>
		c.SeedDoBerco != 0 ? c.SeedDoBerco : Bercos.SementeDoBerco(c.Nome, c.CriadoEm);

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
	public (ZoneKey Zona, Vec2 Pos) DestinoDoBerco(Berco b, ServerPlayer? dono = null, ulong semente = 0)
	{
		// ============================ NINGUEM NASCE NUM CADAVER ============================
		// **A saga 1 destroi VEGETA, que e o berco dos Saiyajin.** Sem esta linha, todo Saiyajin
		// criado depois dela -- e toda morte de Saiyajin, que passa por aqui -- mandaria um corpo
		// pra uma zona que nao existe mais: sem colisao carregada, sem povo, e com o `TickDoEspaco`
		// se recusando a pousar la. A pessoa nasceria presa.
		//
		// ============================ E O DESTINO DEIXOU DE SER UMA LISTA ============================
		// Aqui havia um `ZonaDeRecuoViva`: descia `Espaco.PreFeitos()` e devolvia o primeiro planeta
		// VIVO. **Ele foi DELETADO** -- com a carta trocavel junto --, e nao ha `if` guardando os dois
		// comportamentos. O motivo esta escrito em `GameServer.Refugio.cs`: aquele destino era uma
		// POSICAO NUMA LISTA (Namek so recebia os desabrigados da Terra por ser a segunda linha de um
		// `yield return`), e hoje e uma REGRA -- o dominio que o jogador conquistou, ou o mundo vivo
		// mais perto de casa.
		//
		// O CORPO SEM BERCO (clone, NPC, corpo de bancada) entra pelo mesmo lugar: sem natal, a ancora
		// do refugio e a origem da carta, que e onde a Terra fica.
		// ========================================================================================
		if (b.Planeta is not { Length: > 0 } || ZonaMorta(b.Zona))
			return RefugioDoBerco(b, dono, semente != 0 ? semente : b.Seed);

		return PousarNo(b);
	}

	/// <summary>
	/// **UM CORPO CELESTE CONCRETO VIRA UMA ZONA E UM PONTO.** A ponta do funil -- e ela nao decide
	/// nada: quem chega aqui ja sabe em que mundo vai pousar.
	///
	/// Dois donos, e e por isso que ela e uma funcao e nao o final do <see cref="DestinoDoBerco"/>: o
	/// berco de sempre e o REFUGIO. Se o refugio repetisse estas quatro linhas, o dia em que o pouso
	/// mudasse seria o dia em que quem perdeu o planeta natal pousaria pela regra velha -- e esse e
	/// justamente o caminho que ninguem exercita a mao.
	///
	/// (E ela nao pode voltar a chamar o refugio: o mundo que chega aqui ja foi conferido vivo. Sem
	/// essa separacao, um refugio que caisse num mundo morto chamaria o refugio de novo, pra sempre.)
	/// </summary>
	private (ZoneKey Zona, Vec2 Pos) PousarNo(Berco b)
	{
		if (b.PreFeito)
			return (b.Zona, PontoDeNascimento(b.Zona));

		// O mundo gerado ja esta carregado: entra direto no ponto que o gerador garantiu livre.
		if (ChegadaDaZonaGerada(b.Zona) is { } chegada)
			return (b.Zona, chegada);

		if (b.NoEspaco() is { } corpo)
			return (ZonaDoEspaco, corpo.Pos);

		// Nao ha como chegar aqui -- um berco gerado sempre fica no mapa do universo (`K >= 0`), e o
		// refugio so monta berco com endereco. Se acontecer, a resposta e o ESPACO e nao "a Terra":
		// um planeta cravado aqui e exatamente o que voltaria a prender alguem num cadaver no dia em
		// que aquele planeta morresse. Ver o ultimo recurso em `GameServer.Refugio.cs`.
		GD.PushError($"[server] berco '{b.Planeta}' sem endereco no mapa do universo (K={b.K}) -- "
				   + "o corpo vai pro espaco aberto");
		return (ZonaDoEspaco, b.Pos);
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
	/// <remarks>
	/// `DestinoDe` E O FUNIL DE CIMA: ele pergunta primeiro se esta pessoa escolheu um DOMINIO
	/// conquistado como ponto de renascimento e, quando nao, cai no `DestinoDoBerco` de sempre. Ver
	/// `GameServer.Conquista.cs` -- o dominio nao e um caminho paralelo, e um berco montado do
	/// endereco dele.
	/// </remarks>
	private void MandarProBerco(ServerPlayer pl) => MandarProBerco(pl, DestinoDe(pl));

	/// <summary>
	/// O MESMO, com o destino JA CALCULADO.
	///
	/// ============================ PORQUE PERGUNTAR DUAS VEZES DEIXOU DE SER DE GRACA ============================
	/// O `Renascer` chamava `DestinoDe` pra decidir se a zona mudava e, quando mudava, chamava
	/// `MandarProBerco`, que perguntava **de novo**. Isso era inofensivo enquanto a resposta fosse uma
	/// leitura de tabela; com o REFUGIO deixou de ser: cada chamada varre a vizinhanca do natal e,
	/// principalmente, **conta ao jogador o que aconteceu** (`ContarORefugio`). Duas chamadas viravam a
	/// mesma frase duas vezes no chat, na mesma morte -- e a frase e a unica coisa que explica pra
	/// pessoa por que ela nao acordou em casa.
	/// ========================================================================================================
	/// </summary>
	private void MandarProBerco(ServerPlayer pl, (ZoneKey Zona, Vec2 Pos) destino)
	{
		(ZoneKey zona, Vec2 pos) = destino;
		MoveToZone(pl.Id, zona, pos);

		if (!Espaco.EhEspaco(zona)) return;

		pl.ChunkAtual = ChunkId.De(pl.Pos);
		MandarVizinhanca(pl);

		// ============================ A FRASE SAI DO CEU, E NAO DO BERCO ============================
		// Ela dizia `pl.Berco.Planeta` -- o planeta do berco --, e isso passou a ser mentira em dois
		// casos: o dominio (o corpo vai pro territorio conquistado) e o ULTIMO RECURSO do refugio (o
		// corpo fica no vacuo, onde o mundo dele ficava, e nao ha chao nenhum vindo em seguida).
		//
		// Perguntar ao ESPACO o que ha sob o corpo responde certo nos tres casos com uma linha, e usa
		// a mesma funcao que o pouso ja consulta. Ver `Espaco.PlanetaSob`.
		// ========================================================================================
		//
		// ============================ E "HA UM DISCO SOB VOCE" NAO E A MESMA COISA QUE "VEM CHAO" ============================
		// **A explosao nao apaga o disco do ceu.** Quem cai no ultimo recurso do refugio abre os olhos
		// na coordenada exata de onde o mundo dele ficava, ou seja, EM CIMA do cadaver -- e o
		// `PlanetaSob` responde o nome dele, alegremente. A frase saia assim:
		//
		//     "Terra nao existe mais... Voce abre os olhos no vacuo... NAO HA CHAO -- ha a carta estelar."
		//     "a orbita de Earth te recebe. O CHAO VEM EM SEGUIDA."
		//
		// As duas na mesma morte, e a segunda prometendo um chao que o `TickDoEspaco` se recusa a
		// entregar (`if (PlanetaMorto(destino)) return` -- `GameServer.Espaco.cs`). A pessoa ficaria
		// parada esperando pousar num planeta que acabou de explodir debaixo dela.
		//
		// **E o modo de falha que este port ja catalogou: a regra existia num chamador e faltava no
		// outro.** O tique sabia que em cadaver nao se pousa; a chegada nao sabia. Agora a pergunta e
		// a mesma nos dois lugares. A borda 9(c) da `--bercoprova` guarda isto pelo nome.
		// ==============================================================================================================
		Avisar(pl, Espaco.PlanetaSob(SeedDoUniverso, pl.Pos) is { } sob && !PlanetaMorto(sob)
			? $"a orbita de {sob.Nome} te recebe. O chao vem em seguida."
			: "voce abre os olhos no vacuo, sem chao nenhum sob voce. A carta estelar e a sua saida.");
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
		// A SEMENTE VAI JUNTO, e ela nao e detalhe: e o que decide QUAL mundo vizinho recebe este
		// corpo quando o planeta natal ja esta destruido. Sem ela as dez racas que nascem na Terra
		// acordariam todas no mesmo pedregulho -- e `dono` e nulo aqui de proposito: um personagem em
		// criacao nao tem dominio nenhum pra escolher (ver `DominiosDeRefugio`).
		(ZoneKey zona, Vec2 pos) = DestinoDoBerco(b, null, SementeDoBercoDe(c));
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
