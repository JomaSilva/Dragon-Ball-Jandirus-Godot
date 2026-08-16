using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// O ESPACO, LADO DO SERVIDOR.
///
/// O QUE O SERVIDOR GUARDA DO UNIVERSO: nada. Nem uma tabela de planetas, nem um mapa de
/// estrelas. O que existe numa chunk e funcao pura de `seed + chunk` (ver <see cref="Espaco"/>),
/// entao ele CALCULA quando precisa e o cliente calcula a mesma coisa sozinho. O unico dado que
/// trafega e "estes planetas estao perto de voce" -- e mesmo esse e derivavel; vai pela rede so
/// pra o cliente nao precisar varrer chunk atras de planeta pre-feito distante.
///
/// INTERESSE POR CHUNK, NAO POR ZONA. O espaco inteiro e UMA zona: fatia-lo em varias traria de
/// volta o problema dos setores-por-z. Quem corta o trafego e a distancia em chunks, e o corte
/// entra no snapshot (ver a chamada a <see cref="Espaco.PertoDeMim"/> no laco de envio).
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A SEED DO UNIVERSO. Um numero, e dele sai todo o resto -- posicao de planeta, bioma,
	/// tamanho, quem nasce em cada cidade.
	///
	/// ============================ ELA DEIXOU DE SER CONSTANTE ============================
	/// Era `public const ulong SeedDoUniverso = 20260802`, e o comentario daqui prometia *"quando
	/// virar por servidor, e so ler do disco aqui"*. Virou: **ela e SORTEADA uma vez, quando o mundo
	/// nasce, e mora no `universo.json`** (ver `GameServer.Semente.cs`, que e o unico lugar que
	/// decide qual e a semente deste mundo).
	///
	/// O que NAO mudou -- e nao podia mudar -- e o determinismo: a mesma semente continua dando o
	/// mesmo mundo, em qualquer maquina, a cada boot. O que mudou e **onde a semente nasce**. Com a
	/// constante, todo mundo novo era o mesmo mundo; com o sorteio no disco, reiniciar devolve o
	/// mesmo mundo (o arquivo esta la) e so o WIPE troca de mundo (o arquivo some junto).
	///
	/// PROPRIEDADE DE INSTANCIA e nao mais `const`: um `const` e lido em tempo de compilacao por
	/// quem o cita, entao ele nunca poderia ser lido do disco. E de INSTANCIA porque quem sabe a
	/// semente e este servidor -- nao ha semente "do processo".
	/// ==================================================================================
	/// </summary>
	public ulong SeedDoUniverso => _semente;

	/// <summary>
	/// A zona unica do espaco DESTE mundo. Deixou de ser `static` junto com a semente: a chave dela
	/// carrega a semente dentro (`Espaco.Zona`), entao uma zona de espaco estatica seria a zona do
	/// espaco de OUTRO universo.
	/// </summary>
	public ZoneKey ZonaDoEspaco => Espaco.Zona(SeedDoUniverso);

	/// <summary>Quem ja ouviu "nao ha mais onde pousar". Mesma disciplina do `_avisadosDeEspera`.</summary>
	private readonly HashSet<int> _avisadosDePlanetaMorto = [];

	/// <summary>
	/// DECOLAR. Sai da superficie do planeta em que se esta e aparece no espaco, logo acima
	/// dele.
	///
	/// So funciona de um planeta que EXISTE no mapa do universo: nao da pra decolar do Outro
	/// Mundo nem da dimensao mental, e isso e regra de mundo, nao limitacao.
	/// </summary>
	private void Decolar(ServerPlayer pl)
	{
		if (Espaco.EhEspaco(pl.Zone)) { Avisar(pl, "voce ja esta no espaco."); return; }
		// PELO LUGAR e nao pelo reflexo: o visitante da mente alheia nao tem clone nenhum, e sem esta
		// troca ele decolaria de dentro da cabeca de outra pessoa. Ver `GameServer.Mente.NaMente`.
		if (NaMente(pl)) { Avisar(pl, "primeiro saia do transe."); return; }

		// MUNDO GERADO SOBE POR OUTRO CAMINHO -- e sem esta linha ele nao subia de jeito nenhum.
		//
		// `DecolarDeProcedural` existia, era publico, e nao era chamado por NINGUEM (o repo inteiro
		// so tinha a definicao). Como a busca abaixo so varre os sete pre-feitos, um planeta
		// sorteado -- cujo nome e "Verdejante-1042" e nao esta em lista nenhuma -- sempre caia no
		// "este lugar nao fica no mapa do universo". Quem pousasse num mundo gerado ficava preso
		// ali PARA SEMPRE, e o unico jeito de sair era morrer.
		//
		// Ficou invisivel enquanto nada convidava a pousar num gerado. A carta estelar convida: ela
		// desenha centenas deles e poe um botao "Viajar" em cada um.
		if (DecolarDeProcedural(pl)) return;

		PlanetaNoEspaco? daqui = null;
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (string.Equals(p.Nome, pl.Zone.Name, StringComparison.OrdinalIgnoreCase)) { daqui = p; break; }

		if (daqui == null)
		{
			Avisar(pl, $"nao da pra decolar de {pl.Zone.Name} -- este lugar nao fica no mapa do universo.");
			return;
		}

		pl.PlanetaDeOrigem = daqui.Value.Nome;
		MoveToZone(pl.Id, ZonaDoEspaco, Espaco.PontoDeDecolagem(daqui.Value));
		// A CHUNK DE CHEGADA JA E A ATUAL: sem isto o tick veria "mudou de chunk" no quadro
		// seguinte e mandaria a vizinhanca DE NOVO, duplicada, a cada decolagem.
		pl.ChunkAtual = ChunkId.De(pl.Pos);
		MandarVizinhanca(pl);

		Avisar(pl, $"voce deixa {daqui.Value.Nome} para tras. O silencio do espaco.");
		GD.Print($"[server] {pl.Name} decolou de {daqui.Value.Nome} -> chunk {ChunkId.De(pl.Pos)}");
	}

	/// <summary>
	/// POUSAR. Encostou num planeta, desce nele -- sem menu e sem porta, como o dono pediu.
	///
	/// Rodado no tick, e nao pedido pelo cliente: quem decide onde o corpo esta e o servidor, e
	/// deixar o cliente dizer "pousei em Namek" seria um teleporte de graca.
	/// </summary>
	private void TickDoEspaco(ServerPlayer pl)
	{
		if (!Espaco.EhEspaco(pl.Zone)) return;

		ChunkId agora = ChunkId.De(pl.Pos);
		if (agora != pl.ChunkAtual)
		{
			pl.ChunkAtual = agora;
			MandarVizinhanca(pl);

			// AS SUPER ESFERAS ANDAM COM A VIZINHANCA, e nao com o tique: elas sao recortadas pela
			// MESMA chunk (`Espaco.PertoDeMim`) e o sinal do radar so muda quando se anda de verdade.
			// Mandar por tique seria trinta pacotes por segundo pra dizer a mesma coisa.
			MandarSupers(pl);
		}

		// O SOL VEM ANTES DO POUSO, e as duas perguntas sao IRMAS e nao uma dentro da outra: a
		// estrela nao entra em `PlanetaSob` porque o que aparece la e um lugar onde se POUSA.
		// Ver o cabecalho de `GameServer.Sol.cs`.
		TickDoSol(pl, Protocol.TickSeconds);

		if (Espaco.PlanetaSob(SeedDoUniverso, pl.Pos) is not { } destino) return;

		// ============================ EM PLANETA MORTO NAO SE POUSA ============================
		// E o `if(P.isDestroyed) return` do `testPlanetbump` (`Planets.dm:178`), e e ELE que faz a
		// evacuacao valer alguma coisa: sem esta linha, quem foi jogado em orbita pelo fim do mundo
		// simplesmente voaria de volta pra baixo, e a evacuacao seria um empurraozinho de 90 px.
		//
		// O AVISO SAI UMA VEZ POR ENCOSTADA e nao por tique -- este metodo roda 30 vezes por segundo
		// enquanto o corpo estiver sobre o disco.
		if (PlanetaMorto(destino))
		{
			if (_avisadosDePlanetaMorto.Add(pl.Id))
				Avisar(pl, $"nao ha mais onde pousar: {destino.Nome} e um campo de destrocos.");
			return;
		}
		_avisadosDePlanetaMorto.Remove(pl.Id);

		// PROCEDURAL AGORA TEM CHAO. O servidor gera a MESMA superficie que o cliente, a partir
		// da mesma seed -- e o motivo de o gerador morar no Core: uma funcao, duas pontas, zero
		// byte de mapa na rede. Aqui ele so precisa da colisao (pra saber onde e parede) e do
		// ponto de chegada; quem desenha e a cena.
		if (!destino.Premade) { PousarEmProcedural(pl, destino); return; }

		// ============================ O (249,250) E O CAMPO DA TERRA, E SO DELA ============================
		// Aqui estava `SpawnPos` cru -- o `locate(rand(240,260),rand(240,260),1)` do BYOND, que e o
		// campo aberto do meio da TERRA -- usado como chegada de TODO mapa feito a mao. Em Icer essa
		// celula e ROCHA (medido: 309 das 441 celulas do quadrado 21x21 em volta sao densas), entao
		// quem descia la chegava dentro da pedra.
		//
		// Ninguem tinha reparado porque ninguem ia a Icer: nao havia regra que mandasse alguem pra
		// la, e do espaco so se chega voando por horas. O berco mandou -- o Frost Demon nasce em Icer
		// --, e a bancada `--bercovivo` largou um corpo em orbita de cada um dos sete pre-feitos e
		// mediu a queda: seis em chao livre e Icer DENTRO DE PAREDE.
		//
		// `PontoDeNascimento` pergunta a colisao em vez de confiar no numero, e e o MESMO ponto que o
		// berco ja usava (`GameServer.Berco.cs`) -- ou seja, nascer em Icer e cair em Icer vindo do
		// espaco passam a chegar no mesmo lugar, que e o que "o funil e unico" quer dizer.
		// ==============================================================================================
		var zonaDoPouso = ZoneKey.Premade(destino.Nome);
		MoveToZone(pl.Id, zonaDoPouso, PontoDeNascimento(zonaDoPouso));
		Avisar(pl, $"voce pousa em {destino.Nome}.");
		GD.Print($"[server] {pl.Name} pousou em {destino.Nome}");
	}

	/// <summary>
	/// O SNAPSHOT DO ESPACO: um por jogador, recortado por CHUNK.
	///
	/// Nas zonas normais o mesmo buffer serve pra todo mundo, porque quem esta na zona ve todo
	/// mundo da zona. Aqui nao: o universo e continuo e a zona inteira e uma so, entao o que
	/// cada um enxerga depende de ONDE ele esta. Custa um buffer por jogador -- e o preco de o
	/// espaco nao ter fim.
	/// </summary>
	private void SnapshotDoEspaco(List<ServerPlayer> zona, long agora)
	{
		foreach (ServerPlayer eu in zona)
		{
			if (eu.Peer == null) continue;

			var vizinhos = new List<ServerPlayer>();
			foreach (ServerPlayer o in zona)
				if (Espaco.PertoDeMim(eu.Pos, o.Pos)) vizinhos.Add(o);

			var w = Protocol.Begin(Protocol.S2C.Snapshot);
			w.Put((ushort)vizinhos.Count);
			// A MESMA FABRICA da zona normal. Esta copia tinha ficado pra tras -- ver `EstadoDe`.
			foreach (ServerPlayer pl in vizinhos) EstadoDe(pl, agora).Write(w);

			// O BLOCO DE PROJETEIS TEM QUE SAIR AQUI TAMBEM, mesmo sempre vazio: o leitor e UM so
			// (`GameClient`, opcode `Snapshot`) e ele conta o segundo bloco sempre. Omitir aqui
			// faria o snapshot do ESPACO ser lido errado -- e o erro apareceria como "o jogo
			// desconecta quando entro em orbita", sem nada apontando pra esta linha.
			//
			// E ele e vazio de proposito: no espaco nao ha zona com colisao nem chao, e o recorte
			// e por CHUNK e nao por zona -- os tiros ficam pro dia em que houver combate espacial.
			EscreverProjeteis(w, 0);

			eu.Peer.Send(w, Protocol.ChannelState, DeliveryMethod.Sequenced);
		}
	}

	/// <summary>
	/// Os planetas que o cliente precisa desenhar agora.
	///
	/// So sai quando a CHUNK muda -- e a mesma disciplina do corpo e dos atributos. Andar dentro
	/// da mesma chunk nao muda o que se ve.
	/// </summary>
	/// <remarks>
	/// TINHA UM PARAMETRO `forcar` QUE NINGUEM LIA. Ele existia pra "forcar" o envio numa decolagem
	/// que cai na mesma chunk -- mas quem decide se manda e o CHAMADOR (este metodo sempre manda),
	/// entao ele nunca teve efeito nenhum. Saiu junto com o carimbo de `ChunkAtual`, que e o que
	/// resolvia o problema de verdade.
	/// </remarks>
	/// <remarks>
	/// DEIXOU DE SER `static` quando a semente saiu da constante: a vizinhanca e "os planetas DESTE
	/// universo perto de voce", e um metodo estatico nao tem universo nenhum pra perguntar. E e a
	/// mesma linha que poe a semente no fio (`w.Put(SeedDoUniverso)`) -- e assim que o cliente
	/// descobre em que mundo ele esta, sem nunca calcular semente nenhuma.
	/// </remarks>
	private void MandarVizinhanca(ServerPlayer pl)
	{
		if (pl.Peer == null) return;

		ChunkId c = ChunkId.De(pl.Pos);
		List<PlanetaNoEspaco> perto = Espaco.PorPerto(SeedDoUniverso, c);

		var w = Protocol.Begin(Protocol.S2C.Vizinhanca);
		w.Put(SeedDoUniverso);
		w.Put(c.X);
		w.Put(c.Y);
		w.Put((byte)Math.Min(perto.Count, 255));
		foreach (PlanetaNoEspaco p in perto.Take(255))
		{
			w.Put(p.Nome);
			w.Put(p.Pos.X);
			w.Put(p.Pos.Y);
			w.Put(p.Raio);
			w.Put(p.Seed);
			w.Put(p.Premade);
		}
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
