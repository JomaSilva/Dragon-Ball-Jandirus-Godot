using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// **A CAPITAL SHIP (CAMADA 2)** -- porte de `obj/PlayerShip` (`Code/Modules/Tech/ShipVessel.dm`).
///
/// ============================ A DIFERENCA DE VERDADE PRA CAMADA 1 ============================
/// A Spacepod e um objeto que anda com voce; esta e um LUGAR que voa. Tudo o que ela tem a mais sai
/// dessa unica frase:
///
///   * ela tem um DENTRO -- uma zona propria (<see cref="NaveGrande.ZonaDoInterior"/>), com chao,
///     parede e duas pecas de mobilia;
///   * ela tem uma PONTE -- o unico lugar de onde ela se dirige, e ele fica la dentro;
///   * ela tem CASCO -- ela se quebra, e quando quebra ha gente dentro pra tirar.
///
/// O que ela **nao** tem a mais e movimento: pilotar a nave grande usa o MESMO `PilotoId` e o MESMO
/// `TickDasNaves` do pod. O que muda e de onde o piloto veio e pra onde ele volta.
/// ==========================================================================================
///
/// ============================ O INTERIOR NAO E UM MAPA, E UMA FUNCAO ============================
/// O DM cria um z novo e assenta dez mil turfs (`build_interior`, :146-176). Aqui a planta mora no
/// Core (<see cref="NaveGrande.Planta"/>) e as duas pontas a produzem sozinhas: o servidor pega dela
/// a colisao, o cliente pega o desenho. Zero byte de mapa na rede -- a mesma disciplina do planeta
/// gerado, e pelo mesmo motivo (regra 0.2).
///
/// O QUE SEPARA A NAVE A DA NAVE B e a `ZoneKey`, e nao a planta: `Interior("Nave", id)`. O hash
/// dela ja e o corte de interesse do servidor, entao dois donos dentro das proprias naves nao se
/// veem, nao se ouvem e nao gastam banda um com o outro -- sem uma linha de codigo de instanciamento.
/// E o mesmo mecanismo que a dimensao mental do clone ja usava.
/// ============================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// O INTERIOR
	// =====================================================================
	/// <summary>
	/// A COLISAO DO INTERIOR -- a mesma pra toda nave. Ver o cabecalho de <see cref="NaveGrande"/>
	/// pro porque compartilhar uma instancia mutavel e seguro aqui, e pra lista do que conferir no
	/// dia em que deixar de ser.
	///
	/// Chamado pelo <see cref="MapaDaZonaOuCatalogo"/>, que e o funil unico de "onde ha parede
	/// nesta zona". Sem esta linha o interior seria uma zona SEM colisao: o `ValidateStep` aprovaria
	/// qualquer passo e daria pra sair da nave andando pelo vazio.
	/// </summary>
	private static ZoneCollision? MapaDoInterior(ZoneKey zona) =>
		NaveGrande.EhInterior(zona, out _) ? NaveGrande.Planta().Colisao : null;

	/// <summary>A nave em cujo interior este corpo esta, ou nula.</summary>
	private Nave? NaveDesteInterior(ServerPlayer pl) =>
		NaveGrande.EhInterior(pl.Zone, out int id) ? _naves.FirstOrDefault(n => n.Id == id) : null;

	/// <summary>
	/// QUANTA GENTE ESTA DENTRO DESTA NAVE.
	///
	/// Conta corpos na zona do interior, e nao "quem embarcou": nao ha registro de tripulacao e nao
	/// deve haver -- uma lista de quem esta dentro seria um segundo estado que precisa concordar com
	/// a zona, e o dia em que discordassem a nave estaria vazia com gente dentro (ou o contrario).
	/// A zona E a verdade.
	/// </summary>
	private int QuantosDentro(Nave n) =>
		NaveGrande.EhNaveGrande(n.Tipo) ? ZoneList(NaveGrande.ZonaDoInterior(n.Id).Hash).Count : 0;

	/// <summary>
	/// EMBARCAR -- `proc/board` (`ShipVessel.dm:102-113`).
	///
	/// A SENHA E O UNICO PORTAO, e o dono e isento (`if(M.ckey != owner_ckey && ship_pass ...)`,
	/// :107). Ela e um numero e nao um texto -- ver o comentario no `Interacoes.De(Capital_Ship)`.
	/// </summary>
	private void EmbarcarNaNaveGrande(ServerPlayer pl, Nave n)
	{
		if (pl.Peer == null) return;   // corpo dirigido nao embarca
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "você não está em condições de embarcar."); return; }

		bool dono = string.Equals(n.DonoConta, pl.Conta, StringComparison.OrdinalIgnoreCase);
		if (!dono && n.Senha.Length > 0)
		{
			// A SENHA NAO E PERGUNTADA AQUI, e sim DIGITADA ANTES. O menu deste port nao encadeia
			// pergunta com acao (ele manda um verbo e fecha), entao o codigo entra pelo mesmo verbo
			// `nave_senha` -- quem nao e dono usa aquele numero pra TENTAR, e quem e dono usa pra
			// TRANCAR. Uma porta so, e o servidor sabe de que lado da fechadura cada um esta.
			if (!_codigoNaMao.TryGetValue(pl.Id, out int tentativa) || tentativa.ToString() != n.Senha)
			{
				Avisar(pl, $"{NomeDaNave(n)} está trancada. Digite o código em \"Trancar com senha\" e tente de novo.");
				return;
			}
			_codigoNaMao.Remove(pl.Id);
		}

		ZoneKey dentro = NaveGrande.ZonaDoInterior(n.Id);

		// DE ONDE ELE VEIO. E o que a plataforma de saida devolve quando a nave nao esta em lugar
		// nenhum que aceite pouso -- ver `SairDaNave`.
		MoveToZone(pl.Id, dentro, NaveGrande.PixelDe(NaveGrande.CelDaChegada));
		MandarObras(dentro);

		Avisar(pl, $"você embarca em {NomeDaNave(n)}. A ponte fica no canto -- siga a parede à esquerda.");
		GD.Print($"[server] {pl.Name} embarcou no interior da nave #{n.Id} ({QuantosDentro(n)} dentro)");
	}

	/// <summary>
	/// O CODIGO QUE CADA UM DIGITOU POR ULTIMO. Em memoria, por sessao, e limpo no uso.
	///
	/// Nao vai pro `ServerPlayer` porque nao e estado de personagem -- e o que os dedos acabaram de
	/// digitar, com a mesma vida util de um campo de formulario. E nao vai pro disco por motivo
	/// nenhum que valha a pena escrever.
	/// </summary>
	private readonly Dictionary<int, int> _codigoNaMao = [];

	/// <summary>
	/// O VERBO DO CODIGO: TRANCAR se voce e o dono, TENTAR se nao e.
	///
	/// Ver `EmbarcarNaNaveGrande` pro porque de ser um verbo so. Zero destranca -- e o `sp ? sp : ""`
	/// do DM (:66), onde cancelar a caixa deixava a nave aberta.
	/// </summary>
	private void CodigoDaNave(ServerPlayer pl, string arg)
	{
		Nave? n = NaveDesteInterior(pl) ?? NavePerto(pl);
		if (n == null || !NaveGrande.EhNaveGrande(n.Tipo))
		{ Avisar(pl, "não há nenhuma Capital Ship por perto."); return; }

		if (!int.TryParse(arg, out int codigo) || codigo < 0) { Avisar(pl, "código inválido."); return; }

		if (!string.Equals(n.DonoConta, pl.Conta, StringComparison.OrdinalIgnoreCase))
		{
			_codigoNaMao[pl.Id] = codigo;
			Avisar(pl, "código anotado. Aperte E na nave e escolha \"Embarcar\".");
			return;
		}

		n.Senha = codigo == 0 ? "" : codigo.ToString();
		GravarNaves();
		Avisar(pl, codigo == 0
			? $"{NomeDaNave(n)} está destrancada: qualquer um embarca."
			: $"{NomeDaNave(n)} trancada. Você nunca precisa do código; os outros precisam.");
	}

	// =====================================================================
	// AS DUAS PECAS DA SALA
	// =====================================================================
	/// <summary>
	/// A NAVE CUJO CONSOLE ESTA AO ALCANCE DA MAO. Nula quando o corpo nao esta na ponte.
	///
	/// E o `set src in oview(1)` dos tres verbs do `obj/ShipControl` (:296, :306, :351), feito
	/// contra a CELULA DA PLANTA em vez de contra um objeto -- porque aqui o console nao e um
	/// objeto guardado, e uma posicao que a planta conhece (ver `PecaDoInterior`).
	/// </summary>
	private Nave? NaveDaPonteDe(ServerPlayer pl) =>
		PertoDaPeca(pl, NaveGrande.CelDoConsole) ? NaveDesteInterior(pl) : null;

	/// <summary>O corpo esta em cima da plataforma de saida?</summary>
	private static bool NaPlataforma(ServerPlayer pl) => PertoDaPeca(pl, NaveGrande.CelDaPlataforma);

	private static bool PertoDaPeca(ServerPlayer pl, (int X, int Y) cel)
	{
		if (!NaveGrande.EhInterior(pl.Zone, out _)) return false;
		Vec2 c = NaveGrande.PixelDe(cel);
		// O MESMO ALCANCE DE QUALQUER COISA COM QUE SE MEXE (`Interacoes.Alcance`), e por eixo, como
		// o `ObraQueAceita` mede. Ele era 48 aqui e 64 no menu do cliente, e a diferenca era uma
		// banda de 16 px em que o menu abria e o verbo falhava -- ver `Interacoes.Alcance`.
		return Math.Abs(c.X - pl.Pos.X) <= Interacoes.Alcance
			&& Math.Abs(c.Y - pl.Pos.Y) <= Interacoes.Alcance;
	}

	/// <summary>
	/// AS DUAS PECAS FIXAS, no formato que o pacote de construcoes carrega.
	///
	/// ============================ POR QUE ELAS VIAJAM NAQUELE PACOTE ============================
	/// Porque pro cliente elas SAO construcoes: sprite ancorado numa celula, entra no Y-sort,
	/// bloqueia (o console) e responde a tecla E. O menu da tecla E le a lista `cli.Obras` e pergunta
	/// ao `Interacoes` o que cada tipo faz -- ou seja, pondo as duas ali elas ganham menu, desenho e
	/// alcance sem uma linha nova no cliente.
	///
	/// O ID VAI NUM TERCEIRO INTERVALO NEGATIVO. A `Obra` usa positivos e a nave parada usa `-Id`
	/// (ver `MandarObras`); duas pecas com id `-1`/`-2` colidiriam com as naves 1 e 2. O corte em
	/// -1.000.000 e folgado de proposito: ele so precisa nao encostar em nenhum id de nave real, e
	/// um servidor com um milhao de naves tem problema maior que este.
	/// ========================================================================================
	/// </summary>
	private static IEnumerable<(int Id, PecaDoInterior Peca)> MobiliaDoInterior(ZoneKey zona)
	{
		if (!NaveGrande.EhInterior(zona, out _)) yield break;
		for (int i = 0; i < NaveGrande.Mobilia.Length; i++)
			yield return (-1_000_000 - i, NaveGrande.Mobilia[i]);
	}

	// =====================================================================
	// SAIR -- `proc/disembark` (`ShipVessel.dm:230-236`)
	// =====================================================================
	/// <summary>
	/// DESEMBARCAR PELA PLATAFORMA: sai pra onde a nave estiver AGORA.
	///
	/// ============================ E "AGORA" E A COISA TODA ============================
	/// O DM devolve o corpo pra um tile livre ao lado do casco (`ship_free_turf_around`), seja ele
	/// no planeta de onde se embarcou ou do outro lado da galaxia -- e e por isso que a nave grande
	/// e um veiculo de verdade e nao uma casa: quem entrou na Terra e ficou uma hora treinando
	/// dentro dela sai em Namek, se foi pra la que ela foi.
	///
	/// SAIR NO ESPACO E PERMITIDO, e nao um descuido. E o mesmo lugar em que o piloto de uma
	/// Spacepod fica quando desembarca em orbita: o vazio deste port nao machuca ninguem (nao ha
	/// porte de traje espacial -- `grep spacesuit` no port: zero), e quem sai flutua ao lado da nave
	/// e pode voltar. A estrela machuca, e ela machuca do lado de fora do casco tanto quanto
	/// machucava antes desta camada existir -- ver o relatorio.
	/// ==============================================================================
	/// </summary>
	private void SairDaNave(ServerPlayer pl)
	{
		Nave? n = NaveDesteInterior(pl);
		if (n == null) { Avisar(pl, "você não está dentro de uma nave."); return; }
		if (!NaPlataforma(pl)) { Avisar(pl, "a plataforma de saída fica no centro da nave."); return; }

		Vec2 fora = PontoAoLadoDaNave(n, pl.Id);
		MoveToZone(pl.Id, n.Zona, fora);
		Avisar(pl, $"você desce de {NomeDaNave(n)}, em {n.Zona.Name}.");
		GD.Print($"[server] {pl.Name} desembarcou da nave #{n.Id} em {n.Zona}");
	}

	/// <summary>
	/// UM PONTO LIVRE AO LADO DO CASCO -- `ship_free_turf_around` (`ShipVessel.dm:358-365`).
	///
	/// O DM varre as oito vizinhas e devolve a primeira sem densidade, caindo no proprio tile da
	/// nave se nao achar nenhuma. Aqui quem responde e o `PontoLivrePerto`, que ja e o funil de
	/// "acha chao perto daqui" deste port (berco, invasao, povoamento e conquista usam ele) -- e ele
	/// nao existe no espaco, onde nao ha mapa: la o vazio ao lado da nave e sempre livre.
	///
	/// O <paramref name="semente"/> ESPALHA quem sai junto. Sem ele, uma nave destruida com cinco
	/// pessoas dentro cuspiria as cinco na MESMA coordenada -- e cinco corpos empilhados num pixel e
	/// o tipo de coisa que o jogador le como "o jogo bugou", nao como "a nave explodiu".
	/// </summary>
	private Vec2 PontoAoLadoDaNave(Nave n, int semente)
	{
		const float raio = 1.5f * ZoneCollision.TileSize;
		double ang = semente % 8 / 8.0 * Math.Tau;
		var desejado = new Vec2(n.X + (float)Math.Cos(ang) * raio, n.Y + (float)Math.Sin(ang) * raio);
		return MapaDaZonaOuCatalogo(n.Zona)?.PontoLivrePerto(desejado) ?? desejado;
	}

	// =====================================================================
	// A PONTE -- `obj/ShipControl` (`ShipVessel.dm:285-355`)
	// =====================================================================
	/// <summary>
	/// OBSERVAR -- `verb/Observe` (:295-303).
	///
	/// ============================ NO DM E UMA CAMERA; AQUI E A CARTA ESTELAR ============================
	/// La sao duas linhas: `client.perspective = EYE_PERSPECTIVE` e `client.eye = ship_ref`. O olho
	/// do jogador passa a ser a nave e ele ve o mundo em volta dela.
	///
	/// Este port nao tem olho remoto, e dar um a ele nao seria uma linha: o corte de interesse do
	/// servidor e POR ZONA (so quem compartilha o hash recebe snapshot), entao "ver de fora" exigiria
	/// entregar a um corpo os pacotes de uma zona em que ele nao esta -- um segundo canal de
	/// snapshot, com um segundo recorte, e a certeza de que um dos dois envelheceria.
	///
	/// O que a pergunta REALMENTE quer saber -- *"onde minha nave esta, e o que tem em volta"* -- ja
	/// tem tela neste port, e ela e melhor que a camera do DM pra isso: a CARTA ESTELAR (menu P, aba
	/// Nav). Entao Observar manda a posicao da nave NA GALAXIA, e a carta passa a se centrar nela
	/// enquanto voce esta a bordo (ver `MapaEstelar.MinhaPosicaoNaGalaxia`). O jogador dentro de uma
	/// sala sem janela deixa de estar perdido, que era o problema.
	///
	/// O QUE SE PERDEU: nao da pra ver um corpo passando do lado de fora do casco. O que se ganhou:
	/// o unico jeito de saber a que distancia de Namek a nave esta -- e a camera do DM tambem nao
	/// dizia isso.
	/// ================================================================================================
	/// </summary>
	private void ObservarDaPonte(ServerPlayer pl, Nave n)
	{
		Vec2? galaxia = PosicaoNaGalaxia(n.Zona, new Vec2(n.X, n.Y));

		Avisar(pl, $"-- de fora de {NomeDaNave(n)} --");
		Avisar(pl, Espaco.EhEspaco(n.Zona)
			? "  o casco flutua no vazio."
			: $"  o casco está pousado em {n.Zona.Name}.");

		if (galaxia is { } g)
		{
			Avisar(pl, $"  posição na galáxia: ({g.X:N0}, {g.Y:N0}).");
			if (Sistemas.EstrelaPerto(SeedDoUniverso, g, out Estrela e, out double dist))
				Avisar(pl, $"  há uma estrela a {dist:N0} px -- o raio dela é {e.Raio:N0}.");
			MandarNaveVista(pl, n, g);
			Avisar(pl, "  a carta estelar (menu P, aba Nav) agora mostra a NAVE, e não você.");
		}
		else Avisar(pl, "  não dá pra situar este lugar no mapa do universo.");

		if (n.ArmaduraMax > 0)
			Avisar(pl, $"  casco: {n.Armadura / n.ArmaduraMax * 100:0}% inteiro.");
	}

	/// <summary>
	/// MANDA A MARCACAO DA NAVE pra a carta estelar de quem pediu -- ver <see cref="Protocol.S2C.Nave"/>.
	///
	/// QUEM APAGA E O CLIENTE, ao sair do interior. Ver o comentario do opcode pro porque de nao
	/// haver um pacote de apagar.
	/// </summary>
	private static void MandarNaveVista(ServerPlayer pl, Nave n, Vec2 galaxia)
	{
		if (pl.Peer == null) return;
		var w = Protocol.Begin(Protocol.S2C.Nave);
		w.Put(galaxia.X);
		w.Put(galaxia.Y);
		w.Put(n.Zona.Name);
		w.Put((float)(n.ArmaduraMax > 0 ? n.Armadura / n.ArmaduraMax * 100 : 100));
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// ONDE, NO MAPA DO UNIVERSO, FICA UM PONTO DE UMA ZONA. Nulo pra lugar que nao fica em lugar
	/// nenhum (Outro Mundo, Sala do Tempo, o interior de outra nave).
	///
	/// TRES RESPOSTAS, e as tres ja existiam espalhadas: no espaco o proprio ponto E a coordenada da
	/// galaxia; num pre-feito e a posicao do planeta na lista; num gerado e a do corpo que o
	/// servidor guardou quando o gerou (`ZonaGerada.NoEspaco`). E a mesma escada que a
	/// `MapaEstelar.MinhaPosicaoNaGalaxia` percorre do lado do cliente -- aqui ela existe porque o
	/// cliente NAO pode responder por uma zona em que ele nao esta.
	/// </summary>
	private Vec2? PosicaoNaGalaxia(ZoneKey zona, Vec2 pos)
	{
		if (Espaco.EhEspaco(zona)) return pos;

		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (string.Equals(p.Nome, zona.Name, StringComparison.OrdinalIgnoreCase)) return p.Pos;

		if (MundoDaZona(zona) is { } gerado) return gerado.NoEspaco.Pos;
		return null;
	}

	/// <summary>
	/// PILOTAR -- `verb/Pilot` (`ShipVessel.dm:305-348`).
	///
	/// ============================ O QUE FOI PORTADO, E O QUE NAO FOI ============================
	/// PORTADO, porque e o miolo: o corpo SAI do interior e vai pro exterior, co-locado com a nave
	/// (`usr.loc = locate(ship.x, ship.y, ship.z)`, :327), e a nave passa a segui-lo -- que aqui e o
	/// mesmo `PilotoId` + `TickDasNaves` que a Spacepod ja usa. Um segundo motor de "veiculo segue
	/// dono" seria a duplicacao que o `RecalcularVelocidade` ja tinha recusado.
	///
	/// PORTADO: o leme e de UM (`if(ship_ref.pilot_mob && ship_ref.pilot_mob != usr)`, :310).
	///
	/// NAO PORTADO: a `invisibility = 101` (:317). Ela existe la pra o jogador ver a NAVE e nao um
	/// sujeito de pe em cima dela -- e aqui isso ja acontece por outro caminho: o sprite da nave e
	/// desenhado POR CIMA do corpo (`NaveDesenhada`, `plane = 6` no DM), e o casco da Capital Ship
	/// tem 128x138 px. Ligar a invisibilidade de verdade seria pior que inutil: o unico registro de
	/// invisivel deste port e o `_invisiveis` da tecnica, e ele COBRA KI por segundo -- o piloto
	/// secaria dirigindo.
	///
	/// NAO PORTADO: o `spacesuit = 1` (:318, *"survive space while at the helm"*). Ele nao tem o que
	/// portar -- o vazio deste port nao machuca ninguem. O que machuca e a ESTRELA, e o que ela faz
	/// com quem esta ao leme e a pergunta de design que o relatorio devolve ao dono.
	///
	/// PORTADO, **E ERA A CORRECAO DE UMA DECISAO**: o `mind_dummy` no leme (:329-346) -- um corpo
	/// real e atacavel deixado na ponte. Este comentario dizia que ele nao tinha o que resolver aqui
	/// *"porque o corpo do piloto NAO some do mundo, ele so muda de zona"*, e as duas metades disso
	/// estavam erradas: `MoveToZone` manda `PeerLeft` pra quem fica, entao pra quem estava na ponte o
	/// piloto **sumia**; e o dono pediu o contrario com todas as letras -- *"teu corpo vai ficar la
	/// parado enquanto vc controla a nave, e caso alguem te bata vc volta pro corpo principal"*.
	///
	/// Quem resolve e o `LargarOCorpo` (`GameServer.CorpoLargado.cs`), o MESMO mecanismo da dimensao
	/// mental -- como no DM, onde a meditacao e o leme compartilham um `mind_dummy` so. Escrever um
	/// boneco proprio pra nave seria o defeito que mais se repetiu neste port.
	/// ========================================================================================
	/// </summary>
	private void PilotarDaPonte(ServerPlayer pl, Nave n)
	{
		if (n.PilotoId == pl.Id) { PararDePilotar(pl, n); return; }
		if (n.PilotoId != 0) { Avisar(pl, "alguém já está no leme."); return; }
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "você não está em condições de pilotar."); return; }

		// O CORPO FICA NA PONTE, de pe, e a atencao vai pro leme. A RECUSA VEM ANTES de registrar o
		// piloto: na ordem inversa, um pedido negado (ja esta fora do corpo, ou esta possuido pela
		// fera) deixaria a nave com um piloto que nunca saiu da ponte e o leme trancado pra todos.
		//
		// O destino e o exterior, exatamente como o `usr.loc = locate(ship...)` do DM: sem isto o
		// `TickDasNaves` copiaria a posicao de alguem que esta DENTRO da nave pra a propria nave, e
		// ela se teleportaria pro meio do proprio interior -- uma zona que nao fica em lugar nenhum.
		if (!LargarOCorpo(pl, n.Zona, new Vec2(n.X, n.Y))) return;

		n.PilotoId = pl.Id;
		n.LancaEm = 0;

		// PILOTAR CONTA COMO VOO, o mesmo `pilot.flight = 1` da camada 1: sem isso a nave grande nao
		// atravessaria agua nem lava, e a `Nave` inteira ja passa por esse bit.
		pl.NaveDevolveVoo = pl.Voando;
		pl.Voando = true;
		RecalcularVelocidade(pl);
		MandarFicha(pl);
		MandarObras(n.Zona);

		// O CASCO VIRA O ALVO DA TECLA E. E O PEDIDO DO DONO EM UMA LINHA: *"ao apertar E DNV vc
		// volta pra dentro da nave"*. Do leme o console esta a um mundo de distancia -- literalmente,
		// numa zona em que o corpo nao esta --, entao sem isto nao haveria botao nenhum pra largar o
		// leme. Ver `Interacoes.DoVeiculo`.
		MandarVeiculo(pl);

		Avisar(pl, $"você assume o leme de {NomeDaNave(n)}. Ande pra dirigir; "
				   + "encoste num planeta pra pousar. Aperte E pra voltar à ponte.");
		GD.Print($"[server] {pl.Name} assumiu o leme da nave #{n.Id}");
	}

	/// <summary>
	/// LARGAR O LEME -- `return_to_interior` + `end_pilot` (`ShipVessel.dm:124-136, 211-227`).
	///
	/// O DM devolve o corpo pro TILE EXATO do computador (`pilot_return_loc`, guardado na hora de
	/// assumir) -- e hoje **este port faz melhor sem guardar nada**: o corpo esta la, de pe na ponte,
	/// e quem volta volta pra onde ELE esta. Se alguem o tiver empurrado dois tiles pro lado, e la
	/// que o piloto reaparece. O `aoLado` abaixo virou o destino de EMERGENCIA, pro caso de o corpo
	/// nao existir mais (a nave explodiu com ele dentro, um admin o apagou).
	///
	/// <paramref name="motivo"/> e o que o jogador le ao voltar. O padrao e largar o leme por
	/// vontade propria; o <see cref="VoltarDeOndeEstiver"/> passa o texto do golpe que o acordou.
	/// </summary>
	private void PararDePilotar(ServerPlayer pl, Nave n, string motivo = "você larga o leme e volta à ponte. Aperte E no console pra dirigir de novo.")
	{
		n.PilotoId = 0;
		n.LancaEm = 0;
		GravarNaves();

		if (!pl.NaveDevolveVoo) { pl.Voando = false; pl.Altitude = 0f; }

		// UM TILE AO SUL DO CONSOLE, e nao em cima dele: o console e PAREDE (a densidade dele esta
		// assada no bitset da planta), e devolver o corpo pra dentro de uma parede e a familia de
		// defeito que o `PontoDeNascimento` ja custou uma bancada inteira pra achar em Icer.
		ZoneKey dentro = NaveGrande.ZonaDoInterior(n.Id);
		Vec2 aoLado = NaveGrande.PixelDe((NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y + 1));
		VoltarProCorpo(pl, motivo, dentro, aoLado);
		MandarObras(pl.Zone);

		RecalcularVelocidade(pl);
		MandarFicha(pl);

		// SAIU DO LEME: o alvo da tecla E deixa de ser o casco. Dentro da ponte quem responde ao E e
		// o CONSOLE, que e mobilia do interior e ja viaja no pacote de construcoes.
		MandarVeiculo(pl);

		GD.Print($"[server] {pl.Name} largou o leme da nave #{n.Id}");
	}

	/// <summary>
	/// LANCAR DA PONTE -- `proc/do_launch` (`ShipVessel.dm:179-199`).
	///
	/// ============================ ELE NAO PRECISA DE PILOTO, E ISSO E DO DM ============================
	/// O `do_launch` nao olha `pilot_mob` nenhuma vez: qualquer um na ponte aperta lancar e a nave
	/// sobe. E faz sentido -- lancar e uma sequencia automatica, dirigir e que exige alguem no leme.
	///
	/// O QUE MUDA PRA CA: o DM teleporta a NAVE (`loc = pick(sT)`) e deixa quem esta dentro dentro,
	/// porque o interior dele e outro z e nao acompanha nada. Aqui e igual, e sai de graca: quem esta
	/// no interior nao muda de zona, e o interior nao tem lado de fora. A nave e que sobe.
	///
	/// A SUBIDA PASSA PELO `Decolar` DE PRODUCAO quando ha piloto, e por um caminho proprio quando
	/// nao ha -- e essa e a unica divergencia real desta funcao com a camada 1. `Decolar` move um
	/// JOGADOR; uma nave sem ninguem ao leme nao tem jogador pra mover. Ver `SubirONaveSozinha`.
	/// ==============================================================================================
	/// </summary>
	private void LancarDaPonte(ServerPlayer pl, Nave n)
	{
		if (Espaco.EhEspaco(n.Zona)) { Avisar(pl, $"{NomeDaNave(n)} já está no espaço."); return; }
		if (n.LancaEm > 0)
		{
			Avisar(pl, $"o lançamento já está em curso ({(n.LancaEm - NowMs()) / 1000.0:0.#} s).");
			return;
		}

		n.LancaEm = NowMs() + (long)(NaveGrande.SegundosDeLancamento * 1000);

		// O AVISO VAI PRA TRIPULACAO INTEIRA -- `to_chat(view(src), ...)` (:186). Quem esta treinando
		// no fundo da nave precisa saber que ela vai sair do chao: e a diferenca entre um veiculo
		// compartilhado e um sequestro.
		foreach (ServerPlayer abordo in ZoneList(NaveGrande.ZonaDoInterior(n.Id).Hash))
			Avisar(abordo, $"{NomeDaNave(n)}: sequência de lançamento iniciada. ETA "
						   + $"{NaveGrande.SegundosDeLancamento:0} segundos.");
	}

	/// <summary>
	/// A NAVE GRANDE SOBE SEM NINGUEM AO LEME.
	///
	/// O `Decolar` de producao move um JOGADOR (ele escreve zona, chunk e vizinhanca dele) e e o
	/// caminho certo pra toda subida que tem piloto -- e o que a camada 1 usa. Aqui nao ha piloto:
	/// a nave e um objeto no chao que precisa virar um objeto no espaco.
	///
	/// A CONTA E A MESMA DAQUELE: <see cref="Espaco.PontoDeDecolagem"/>, a partir do planeta em que a
	/// nave esta. Se este lugar nao fica no mapa do universo (Outro Mundo, o interior de outra nave),
	/// a subida e recusada -- e o mesmo `"este lugar nao fica no mapa do universo"` do `Decolar`.
	/// </summary>
	private bool SubirNaveSozinha(Nave n)
	{
		PlanetaNoEspaco? daqui = null;
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (string.Equals(p.Nome, n.Zona.Name, StringComparison.OrdinalIgnoreCase)) { daqui = p; break; }
		if (daqui == null && MundoDaZona(n.Zona) is { } gerado) daqui = gerado.NoEspaco;
		if (daqui == null) return false;

		ZoneKey antes = n.Zona;
		Vec2 fora = Espaco.PontoDeDecolagem(daqui.Value);
		n.PorZona(ZonaDoEspaco);
		n.X = fora.X;
		n.Y = fora.Y;
		MandarObras(antes);
		MandarObras(n.Zona);
		return true;
	}

	// =====================================================================
	// O CASCO -- `takeDamage`/`testDestroy` (`ShipVessel.dm:239-260`)
	// =====================================================================
	/// <summary>
	/// UMA PANCADA NO CASCO. Espelho exato do <see cref="Estragar"/> das obras, com a mesma
	/// <see cref="Armadura"/> e os mesmos tres desfechos (nao arranhou / rangeu / cedeu).
	///
	/// ============================ POR QUE NAO E O `Estragar` ============================
	/// Porque `Estragar` recebe uma `Obra` -- e a nave nao e uma, pela razao que o cabecalho de
	/// `Nave` explica em tres linhas. O que se repete aqui e a SEQUENCIA (bater, comparar, remover),
	/// e a REGRA continua numa casa so: as duas chamam `Armadura.Bater` e `Armadura.Cedeu`, que sao
	/// as funcoes puras do Core. Se um dia a superarmadura mudar, muda num lugar.
	/// ==================================================================================
	///
	/// O DONO NAO DERRUBA A PROPRIA NAVE -- *"You won't attack your own ship"* (:243-245). Sem essa
	/// guarda, dois milhoes de zeni caem por um soco dado sem querer contra a parede de casa.
	/// </summary>
	private void EstragarNave(Nave n, double dano, ServerPlayer? autor)
	{
		string nome = NomeDaNave(n);
		if (n.ArmaduraMax <= 0)
		{
			if (autor != null) Avisar(autor, $"{nome} não é feita pra apanhar -- recolha-a em vez disso.");
			return;
		}

		if (autor != null && string.Equals(n.DonoConta, autor.Conta, StringComparison.OrdinalIgnoreCase))
		{ Avisar(autor, $"você não vai bater na sua própria {nome}."); return; }

		double antes = n.Armadura;
		n.Armadura = Armadura.Bater(n.Armadura, n.ArmaduraMax, dano);

		if (n.Armadura >= antes)
		{
			if (autor != null) Avisar(autor, $"{nome} nem balança -- você não bate forte o bastante.");
			return;
		}

		if (!Armadura.Cedeu(n.Armadura))
		{
			GravarNaves();
			// O ANUNCIO VAI PRA QUEM ESTA DENTRO TAMBEM -- `to_chat(view(src), ...)` (:247). Sentir a
			// nave apanhar de dentro dela e a unica chance que a tripulacao tem de sair antes do fim.
			foreach (ServerPlayer abordo in ZoneList(NaveGrande.ZonaDoInterior(n.Id).Hash))
				Avisar(abordo, $"o casco de {nome} range: {n.Armadura / n.ArmaduraMax * 100:0}% inteiro.");
			if (autor != null) Avisar(autor, $"{nome} range ({n.Armadura / n.ArmaduraMax * 100:0}% inteira).");
			return;
		}

		DestruirNave(n, autor);
	}

	/// <summary>
	/// ============================ A NAVE CEDEU: PRA ONDE VAI QUEM ESTA DENTRO ============================
	/// Este e o caso que da errado calado, e o DM ja tinha escrito a resposta: `testDestroy` ejeta
	/// TODO mob com cliente que esteja no z do interior pra `ship_free_turf_around(src)` -- um tile
	/// livre ao lado do casco -- ANTES de o pai explodir a nave (:249-260).
	///
	/// AQUI SAO QUATRO PASSOS, E A ORDEM E O QUE IMPEDE OS TRES MODOS DE FALHA:
	///
	///   1. LARGA O LEME PRIMEIRO (`if(pilot_mob) end_pilot(pilot_mob)`, :253). O piloto esta FORA,
	///      montado no casco; se a nave sumisse com ele ainda marcado como piloto, o `TickDasNaves`
	///      procuraria uma nave que nao existe mais. Ele nao volta pra ponte -- a ponte esta
	///      explodindo: ele fica exatamente onde estava, no espaco ou sobre o planeta;
	///   2. EJETA QUEM ESTA DENTRO, cada um numa direcao (ver `PontoAoLadoDaNave`): cinco corpos
	///      numa coordenada so o jogador le como bug, e nao como explosao;
	///   3. SO ENTAO TIRA A NAVE DA LISTA. Ejetar depois seria mover corpos pra a posicao de um
	///      objeto que ja foi apagado;
	///   4. REFAZ A COLISAO E O PACOTE das duas zonas -- a de fora perde uma parede, e a de dentro
	///      deixou de existir.
	///
	/// PRA ONDE, EM UMA LINHA: **pro lado do casco, na zona em que o casco estava.** Se ela caiu
	/// pousada, ficam no chao do planeta; se caiu em orbita, ficam flutuando no espaco -- vivos, e
	/// e isso que importa. O vazio deste port nao machuca (nao ha traje espacial portado), entao
	/// quem sabe voar volta e quem nao sabe pede carona. O unico lugar em que a ejecao dói e DENTRO
	/// DE UMA ESTRELA, e ali quem morre e quem levou a nave pra la.
	/// ================================================================================================
	/// </summary>
	private void DestruirNave(Nave n, ServerPlayer? autor)
	{
		string nome = NomeDaNave(n);
		ZoneKey fora = n.Zona;
		ZoneKey dentro = NaveGrande.ZonaDoInterior(n.Id);

		// 1. o leme
		if (n.PilotoId != 0 && _players.TryGetValue(n.PilotoId, out ServerPlayer? piloto))
		{
			n.PilotoId = 0;
			if (!piloto.NaveDevolveVoo) { piloto.Voando = false; piloto.Altitude = 0f; }
			RecalcularVelocidade(piloto);
			MandarFicha(piloto);

			// O ALVO DA TECLA E MORRE JUNTO COM A NAVE. Sem esta linha o menu do casco continuaria
			// abrindo pra quem estava ao leme -- botoes de um veiculo que acabou de virar destroco,
			// e todos eles cairiam na recusa "não há nenhuma nave por perto".
			MandarVeiculo(piloto);

			Avisar(piloto, $"{nome} se despedaça sob os seus pés.");
		}
		n.PilotoId = 0;

		// 2. a tripulacao
		var aBordo = ZoneList(dentro.Hash).ToList();
		foreach (ServerPlayer t in aBordo)
		{
			MoveToZone(t.Id, fora, PontoAoLadoDaNave(n, t.Id));
			Avisar(t, $"{nome} EXPLODE. Você é arremessado pra fora.");
		}

		// 3. a nave
		_naves.Remove(n);
		GravarNaves();

		// 4. as duas zonas
		MandarObras(fora);
		MandarObras(dentro);

		GD.Print($"[server] {nome} (#{n.Id}) foi destruida em {fora} -- {aBordo.Count} ejetado(s)"
				 + (autor != null ? $", por {autor.Name}" : ""));
		if (autor != null) Avisar(autor, $"{nome} vem abaixo.");
	}

	// =====================================================================
	// O RESGATE DE QUEM ENTROU E O SERVIDOR CAIU
	// =====================================================================
	/// <summary>
	/// ============================ ENTRAR NUMA NAVE QUE NAO EXISTE MAIS ============================
	/// O save guarda a `ZoneKey` inteira desde o `CharacterStore.cs:170-180`, entao quem deslogou
	/// dentro de uma nave volta EXATAMENTE pra `Interior("Nave", 7)`. Isso e o certo -- e o que faz a
	/// nave ser uma casa e nao um corredor -- e tem duas beiradas:
	///
	///   * A NAVE AINDA EXISTE: nao ha nada a fazer. A planta e uma funcao, nao um arquivo; ela
	///     nasce de novo na primeira pergunta que alguem fizer. Foi por isso que ela ficou sendo
	///     funcao, e nao um mapa gravado em disco com o resto do `naves.json`;
	///   * A NAVE FOI DESTRUIDA (ou recolhida) enquanto ele estava fora: ele acordaria num interior
	///     de uma nave que nao existe -- uma zona com chao, sem saida, e sem nenhuma nave pra
	///     desembarcar. O DM tem exatamente este medo escrito: a lista `ship_interior_zs` existe
	///     *"so logout/save can avoid stranding a player on a volatile interior z"* (:38).
	///
	/// A GUARDA DE ZONA MORTA QUE JA EXISTIA NO LOGIN so olha `KindPremade` (ver `GameServer.cs`), e
	/// e por isso que esta precisa existir: um interior nao e pre-feito, entao ele passava batido.
	/// ==========================================================================================
	/// </summary>
	private void ResgatarDeInteriorMorto(ServerPlayer pl)
	{
		if (!NaveGrande.EhInterior(pl.Zone, out int id)) return;
		if (_naves.Any(n => n.Id == id)) return;   // a nave continua de pe: nada a fazer

		GD.Print($"[server] {pl.Name} entrou dentro da nave #{id}, que nao existe mais -- devolvido ao berco");
		PousarNoBercoSemPacote(pl);
		Avisar(pl, "a nave em que você estava não existe mais. Você acorda em terra firme.");
	}

	// =====================================================================
	// O CANAL
	// =====================================================================
	private bool ComandoDaNaveGrande(ServerPlayer pl, string cmd, string arg = "")
	{
		switch (cmd)
		{
			case "nave_embarcar":
			{
				Nave? n = NavePerto(pl);
				if (n == null || !NaveGrande.EhNaveGrande(n.Tipo))
				{ Avisar(pl, "não há nenhuma Capital Ship por perto."); return true; }
				EmbarcarNaNaveGrande(pl, n);
				return true;
			}

			case "nave_sair": SairDaNave(pl); return true;
			case "nave_senha": CodigoDaNave(pl, arg); return true;

			// OS DOIS DA PONTE conferem o CONSOLE e nao a nave: estar dentro nao basta, e o
			// `set src in oview(1)` do DM. Ver `NaveDaPonteDe`.
			case "nave_observar":
			case "nave_pilotar":
			{
				Nave? n = NaveDaPonteDe(pl);
				if (n == null)
				{
					// PILOTANDO, O CONSOLE ESTA A UM MUNDO DE DISTANCIA: quem esta ao leme nao esta na
					// ponte, e sem esta linha ele nao teria como LARGAR o leme -- exatamente o mesmo
					// buraco que fez a camada 1 precisar da aba Nav.
					if (cmd == "nave_pilotar" && NaveDoPiloto(pl) is { } minha
						&& NaveGrande.EhNaveGrande(minha.Tipo))
					{ PararDePilotar(pl, minha); return true; }

					Avisar(pl, "você precisa estar no console da ponte, no canto da nave.");
					return true;
				}
				if (cmd == "nave_observar") ObservarDaPonte(pl, n); else PilotarDaPonte(pl, n);
				return true;
			}

			default: return false;
		}
	}
}
