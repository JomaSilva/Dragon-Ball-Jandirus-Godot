using System;
using System.Linq;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// QUEBRAR O CENARIO COM AS MAOS -- e nao so voando nele.
///
/// ============================ O QUE FALTAVA ============================
/// A destruicao tinha dois caminhos e os dois eram indiretos: a parede caia quando um corpo
/// ARREMESSADO batia nela, e o chao rachava sob um golpe PESADO que acertou alguem. Socar uma
/// parede de proposito nao fazia nada -- o golpe sem alvo saia por `AnunciarSocoNoAr` e voltava.
///
/// No original socar cenario e um caminho de primeira classe (`attack_proc.dm:95-112`): quando nao
/// ha ninguem na frente, o soco olha o que ESTA la, e o que estiver aguenta ou nao.
/// =======================================================================
///
/// ============================ TURF E OBJ SE QUEBRAM DIFERENTE ============================
/// Ver <see cref="Armadura"/> pro porque. Em resumo:
///
///   PAREDE/CHAO: limiar. `expressedBP >= Resistencia` e, no soco, um `prob(34)` -- a parede nao
///   cede sempre, e por isso socar um muro e uma sequencia e nao um clique.
///
///   OBRA (arvore, bancada, banco, maquina): armadura acumulada, sem sorteio. Cada pancada tira
///   `dano - 0,75 x armaduraMaxima`, e ela some quando zera.
///
/// A OBRA VEM PRIMEIRO na consulta porque ela OCUPA a celula: uma bancada assentada bloqueia por
/// cima do chao, e perguntar do turf antes derrubaria o piso debaixo dela deixando a bancada
/// flutuando sobre um buraco.
/// =========================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// `prob(34)` do `attack_proc.dm:104` -- a chance de UM soco derrubar o tile que ele alcanca.
	///
	/// O sorteio nao existe no arremesso (la a parede cede na hora) e existe aqui de proposito: um
	/// soco que sempre derruba transforma qualquer parede em porta, e o original quis que abrir
	/// caminho na mao custasse tempo.
	/// </summary>
	private const double ChanceDoSocoDerrubar = 0.34;

	/// <summary>
	/// SOCOU E NAO HAVIA NINGUEM: tem cenario na frente? Devolve se o golpe encontrou ALGUMA coisa.
	///
	/// Devolver `true` sem quebrar nada e o caso comum e esta certo -- o punho bateu na parede, ela
	/// aguentou. Quem chama usa isso pra narrar "voce soca a parede" em vez de "voce soca o ar".
	/// </summary>
	private bool SocarCenario(ServerPlayer a, int nivel = 1)
	{
		Vec2 frente = MeleeArea.Frente(a.Facing);
		var alvo = new Vec2(a.Pos.X + frente.X * ZoneCollision.TileSize,
							a.Pos.Y + frente.Y * ZoneCollision.TileSize);
		(int cx, int cy) = CelulaDoPonto(alvo);

		double bp = a.Ficha.expressedBP;

		// ---- 1. UMA OBRA NA CELULA? ----
		if (ObraNaCelula(a.Zone, cx, cy) is { } obra)
		{
			Baque(a, nivel);
			Estragar(obra, bp, a);
			return true;
		}

		// ---- 1b. UMA NAVE NA CELULA? ----
		// O `verb/Destroy` do `obj/PlayerShip` (`ShipVessel.dm:239-247`) e um verb de clique: `set src
		// in view(6)`, e ele bate com `usr.expressedBP / 7 * usr.Ephysoff`. Aqui ele entra pelo SOCO,
		// que e o caminho de primeira classe deste port pra quebrar cenario -- e com o mesmo `bp` que
		// a obra recebe, pra a nave e a bancada do mesmo dono cairem sob a mesma forca.
		//
		// DEPOIS DA OBRA E ANTES DO TURF, no mesmo lugar em que a obra entra e pelo mesmo motivo: a
		// nave OCUPA a celula (ela e parede pelo `AplicarColisaoDasObras`), e perguntar do chao antes
		// derrubaria o piso debaixo dela.
		if (NaveNaCelula(a.Zone, cx, cy) is { } nave)
		{
			Baque(a, nivel);
			EstragarNave(nave, bp, a);
			return true;
		}

		// ---- 2. UM TILE DENSO? ----
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(a.Zone);

		// ============================ AGUA NAO SE SOCA ============================
		// A pergunta inteira ("o punho alcanca o cenario desta celula?") e do `ClasseDeAgua`, e nao
		// de duas linhas escritas aqui -- ver `ClasseDeAgua.SocoAlcanca`, que testa a agua ANTES do
		// bitset. Hoje isso nao muda desfecho nenhum (agua nao esta no `.col`, entao o `BlockedCell`
		// ja diria nao), e e de proposito: no dia em que alguem marcar agua no `.col` (a tentacao
		// obvia, "e tipo uma parede"), o lago viraria cenario socavel e o `DerrubarCelula` o abriria
		// em chao seco permanente, sem nada dizendo que aquilo esta errado.
		//
		// E o que o dono pediu, na letra: "ela e tipo uma parede, mas N DA PRA SOCAR (igual o
		// chao)". No original o punho tambem nao a alcanca -- `attack_proc.dm:96` exige
		// `T.Resistance && T.density`, e agua tem `density = 0`.
		//
		// ESTA E A FUNCAO QUE A BANCADA MEDE (`agua-prova`, familia 3), e por isso ela mora no Core:
		// enquanto a regra estava escrita aqui dentro, a bancada so conseguia testar uma copia dela.
		// ==========================================================================
		if (mapa == null || !ClasseDeAgua.SocoAlcanca(mapa, cx, cy)) return false;

		// O SOM SAI ANTES DE SABER SE QUEBROU, e sai sempre. E o que o original faz
		// (`attack_proc.dm:99-103`: os sons de soco vem antes do `prob(34)`), e e o que da peso a
		// bater numa parede que aguenta -- silencio ali seria indistinguivel de socar o vazio.
		Baque(a, nivel);

		if (mapa.NaBorda(cx, cy)) { Avisar(a, "isto e o limite do mundo -- nao ha o que quebrar."); return true; }

		// ============================ O `destroyable = 0`, AGORA DE VERDADE ============================
		// A linha da borda logo acima ERA a aproximacao dele, e o comentario que estava aqui dizia
		// isso com todas as letras. A aproximacao vale pra beirada e nao vale pro miolo: as costuras
		// de `/turf/Other/Blank` do Templo estao a centenas de tiles de qualquer margem, e eram elas
		// que o dono socava e via cair. Ver `ZoneCollision.Indestrutivel` e `DerrubarCelula`, que e
		// quem recusa de fato -- esta linha existe pra AVISAR, e nao pra decidir.
		//
		// O `Baque` ja saiu acima, e continua saindo: no original os sons de soco vem antes do
		// `T.Destroy()` (`attack_proc.dm:99-105`), entao bater no que nao cede sempre fez barulho.
		// ==============================================================================================
		if (mapa.Indestrutivel(cx, cy))
		{
			Avisar(a, "seu punho nao arranha isto -- e mais duro que o mundo.");
			return true;
		}

		if (bp >= Empurrao.ResistenciaPadrao && _rng.NextDouble() < ChanceDoSocoDerrubar)
		{
			DerrubarCelula(a.Zone, cx, cy);
			Avisar(a, "o cenario cede sob o seu punho.");
		}
		return true;
	}

	/// <summary>
	/// O BAQUE DE TER ACERTADO ALGUMA COISA -- o MESMO som e a mesma faisca de acertar um corpo.
	///
	/// ============================ POR QUE REUSAR O EVENTO DE GOLPE ============================
	/// A alternativa seria um pacote proprio de "soco em cenario", e ele teria que reaprender tudo
	/// que o relato de golpe ja sabe fazer: o assobio de quem bate, o baque no nivel certo, a
	/// faisca, o tremor de tela de quem levou o impacto na cara. Seriam duas descricoes do mesmo
	/// impacto, e a segunda ficaria pra tras na primeira vez que alguem mexesse na primeira.
	///
	/// `Alvo = 0` E O QUE FAZ ISSO FUNCIONAR SEM MUDAR O CLIENTE. Ele ja trata o alvo ausente:
	/// `Som(quemLeva ?? quemBate, ...)` toca o baque em quem bateu, e o meio do impacto vira a
	/// posicao dele. Pra um soco em parede isso e exatamente o certo -- o punho e a parede estao
	/// no mesmo lugar.
	/// ==========================================================================================
	/// </summary>
	private void Baque(ServerPlayer a, int nivel)
	{
		var e = new Protocol.HitEvent
		{
			Atacante = a.Id,
			Alvo = 0,
			Desfecho = (byte)Desfecho.Acertou,
			Nivel = (byte)Math.Clamp(nivel, 1, 3),
			PosAtacante = a.Pos,
			Membro = "",
		};

		var w = Protocol.Begin(Protocol.S2C.Hit);
		e.Write(w);

		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelState, DeliveryMethod.Unreliable);
	}

	/// <summary>
	/// UMA PANCADA NUMA OBRA. Vale pro soco e pro corpo arremessado -- a regra e a mesma, so o
	/// numero muda (o soco usa o BP de quem bate, o voo usa a forca do arremesso).
	///
	/// <paramref name="autor"/> so serve pra avisar quem bateu; pode ser nulo (o corpo que voa
	/// pode nao ser o culpado).
	/// </summary>
	private void Estragar(Obra o, double dano, ServerPlayer? autor)
	{
		Construcao? c = _obras?.Get(o.Tipo);
		string nome = c?.Nome ?? o.Tipo;

		// ============================ O QUE NAO SE QUEBRA ============================
		// `turf/Teleporters` tem `destroyable = 0` no DM (`Code/Turfs.dm:102`), e a porta da Sala do
		// Tempo e um deles. Ela virou mobilia pra ganhar a tecla E (ver `GameServer.SalaDoTempo.cs`),
		// e mobilia de mapa nasce com a armadura PADRAO -- ou seja, cai no primeiro soco de
		// qualquer um. Isso e inofensivo num banco (ele volta no proximo boot), e nao e aqui: a
		// porta e o UNICO caminho pra dentro do z13, e um soco a esmo trancaria a sala pro servidor
		// inteiro ate alguem reiniciar.
		//
		// A GUARDA E POR TIPO e nao um campo do catalogo porque `destroyable` nao e extraido hoje
		// -- o dia em que outra coisa indestrutivel entrar no catalogo, o certo e o extrator trazer
		// o campo e esta linha virar `c?.Destrutivel == false`.
		if (o.Tipo == TipoDaPorta)
		{
			if (autor != null) Avisar(autor, $"{nome} não cede: a barreira mágica engole a pancada.");
			return;
		}

		double antes = o.Armadura;
		o.Armadura = Armadura.Bater(o.Armadura, o.ArmaduraMax, dano);

		if (o.Armadura >= antes)
		{
			// NAO ARRANHOU. E o `superarmor`: dano abaixo de 75% da armadura maxima nao entra. Sem
			// este aviso o jogador socaria um poste por um minuto achando que o jogo esta quebrado.
			if (autor != null) Avisar(autor, $"{nome} nem balanca -- voce nao bate forte o bastante.");
			return;
		}

		if (!Armadura.Cedeu(o.Armadura))
		{
			if (autor != null)
				Avisar(autor, $"{nome} range ({o.Armadura / o.ArmaduraMax * 100:0}% inteira).");
			MandarObras(o.Zona);
			return;
		}

		// ---- CEDEU ----

		// ============================ E SE ELE TINHA UMA FORNADA DENTRO, ELA MORRE COM ELE ============================
		// `on_destroyed` (`DNALabs.dm:350-353`): destruir o Bio-Android Lab CANCELA a gestacao, e o
		// anuncio vai pro servidor inteiro. Isso e metade da mecanica -- o preco de doze horas de
		// gestacao e ter que DEFENDER o tanque, e um preco que ninguem sabe que esta pagando nao e
		// preco nenhum.
		//
		// ATE AQUI O LAB CAIA CALADO: o dono nao era avisado, o mundo nao ficava sabendo, e a
		// `Fornada` sumia junto com a obra na lista. Um jogador podia perder meio milhao de zeni,
		// quatro amostras e meio dia de espera sem NUNCA descobrir o que aconteceu -- e ainda voltar
		// pro lugar procurando o laboratorio.
		//
		// O AVISO E POR CONTA e nao por id de sessao: o dono pode ter deslogado e voltado no meio da
		// gestacao, e e a conta que a `Gestacao` guarda.
		// ==========================================================================================================
		if (o.Fornada is { } fornada)
		{
			ServerPlayer? dono = _players.Values.FirstOrDefault(p => p.Conta == fornada.DonoConta);
			if (dono != null)
				Avisar(dono, fornada.PrometidaEm > 0
					? $"seu Bio-Android Lab foi DESTRUIDO. A criatura morreu no tanque, e as "
					  + $"{fornada.Amostras.Count} amostra(s) se perderam com ela."
					: $"seu Bio-Android Lab foi DESTRUIDO -- as {fornada.Amostras.Count} amostra(s) "
					  + "que estavam no tanque se perderam.");

			if (fornada.PrometidaEm > 0)
				AnunciarNoMundo($"o experimento do Bio-Android Lab de {o.DonoNome} foi INTERROMPIDO: "
								+ "o tanque se rompeu e o que crescia la dentro morreu.");

			o.Fornada = null;
		}

		// ============================ E SE ELE TINHA ALGUEM DENTRO, ESSE ALGUEM SAI ============================
		// `SealingItem/Del()` (`Sealing.dm:140-144`): quebrar o pote SOLTA o selado. E o irmao exato
		// do bloco de cima -- destruir a construcao tem que desfazer o que ela segurava --, e e a
		// interacao principal do Mafuba no jogo original: quem quer o preso de volta nao precisa
		// bater no preso, precisa achar o pote. Ver `GameServer.Selo.cs`.
		PoteFoiAoChao(o);

		_noChao.Remove(o);
		if (!o.DoMapa) GravarMundo();

		ZoneKey zona = o.Zona;

		// A COLISAO E REFEITA PELA LISTA INTEIRA (ver `AplicarColisaoDasObras`): tirar a obra sem
		// isto deixaria a parede dela no lugar, e o corpo levaria correcao num ponto vazio.
		AplicarColisaoDasObras(zona);
		MandarObras(zona);

		// A POEIRA SAI NO LUGAR DELA. O pacote de cenario e o unico canal de "caiu alguma coisa
		// aqui" que o cliente ja sabe desenhar; a celula nao tem tile (o conversor tirou a obra do
		// tilemap), entao apagar nao apaga nada e o efeito e o que sobra.
		(int cx, int cy) = CatalogoDeObras.Celula(o.X, o.Y);
		MandarCelulaCaida(zona, cx, cy);

		GD.Print($"[server] {nome} (#{o.Id}) foi ao chao em {o.Zona}"
				 + (autor != null ? $" -- {autor.Name} com {dano:0} de forca" : ""));
		if (autor != null) Avisar(autor, $"{nome} vem abaixo.");
	}

	/// <summary>
	/// A obra que ocupa uma celula, se houver.
	///
	/// A conta e a MESMA do `AplicarColisaoDasObras` (`CatalogoDeObras.Celula`), e tem que ser: se
	/// as duas divergirem meio tile, existe uma celula que bloqueia por causa de uma obra e onde
	/// nenhum soco a encontra.
	/// </summary>
	private Obra? ObraNaCelula(ZoneKey zona, int cx, int cy) => _noChao.FirstOrDefault(o =>
	{
		if (!o.Zona.Equals(zona)) return false;
		(int ox, int oy) = CatalogoDeObras.Celula(o.X, o.Y);
		return ox == cx && oy == cy;
	});

	/// <summary>
	/// A NAVE PARADA que ocupa uma celula. Irma do <see cref="ObraNaCelula"/>, e com a MESMA conta de
	/// celula pelo mesmo motivo: se as duas divergirem meio tile, existe uma celula que bloqueia por
	/// causa da nave e onde nenhum soco a encontra.
	///
	/// O CRIVO E A `ZoneKey` INTEIRA (`NavesParadasEm`) e nao o nome -- e o mesmo motivo de sempre
	/// neste sistema: socar o chao de um planeta gerado nao pode acertar a nave de um planeta
	/// homonimo do outro lado da galaxia. A `Obra` acima ainda filtra por nome, e ainda esta errada.
	/// </summary>
	private Nave? NaveNaCelula(ZoneKey zona, int cx, int cy) => NavesParadasEm(zona).FirstOrDefault(n =>
	{
		(int ox, int oy) = CatalogoDeObras.Celula(n.X, n.Y);
		return ox == cx && oy == cy;
	});

	/// <summary>
	/// A celula de um ponto, com o deslocamento dos PES -- a mesma conta do `DerrubarCenario`.
	///
	/// Existe como funcao porque ela agora tem tres chamadores, e meia celula de diferenca entre
	/// eles e um soco que atravessa a parede sem tocar nela.
	/// </summary>
	private static (int Cx, int Cy) CelulaDoPonto(Vec2 p) => (
		(int)Math.Floor(p.X / ZoneCollision.TileSize),
		(int)Math.Floor((p.Y + MoveRules.FeetOffsetY) / ZoneCollision.TileSize));

	/// <summary>`--socoteste`: nasce colado na parede mais proxima, virado pra ela.</summary>
	private bool _nascerNaParede;

	/// <summary>
	/// `--semduro`: o boot NAO le o `.duro`, ou seja o mundo volta a ser o de ANTES deste conserto.
	/// Existe pra a bancada poder fotografar o defeito do dono sem apagar arquivo nenhum do disco --
	/// ver a nota junto do `CarregarDuro` em `GameServer.CarregarZonas`.
	/// </summary>
	private bool _semDuro;

	/// <summary>
	/// BANCADA: poe o corpo encostado no cenario mais proximo, olhando pra ele.
	///
	/// Existe porque o caso de teste e chato de montar a mao: o campo da Terra nao tem parede
	/// perto do spawn, e o planeta gerado ESCAVA uma clareira em volta do pouso -- ou seja, os dois
	/// lugares onde uma bancada nasce sao justamente os dois sem nada pra socar. Sem isto, provar
	/// que socar cenario funciona depende de alguem andar ate achar um muro.
	///
	/// Anda em aneis a partir do corpo, igual a busca de clareira do gerador, e para no primeiro
	/// tile que bloqueia, que NAO e borda do mundo e que NAO e indestrutivel -- as duas recusas
	/// respondem antes de olhar forca, entao nascer de frente pra uma delas testaria a recusa e nao
	/// a quebra. A segunda entrou junto com o `.duro`: sem ela, um teste em Arconia ou no Templo
	/// escolheria uma costura de `/turf/Other/Blank` e a bancada reprovaria com "40 socos e a parede
	/// continua de pe" -- que ali e o comportamento CERTO, e nao uma falha.
	/// </summary>
	private void EncostarNaParede(ServerPlayer pl)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		if (mapa == null) { GD.PushWarning("[server] BANCADA: sem mapa pra achar parede"); return; }

		const int T = ZoneCollision.TileSize;
		(int cx0, int cy0) = CelulaDoPonto(pl.Pos);

		for (int r = 1; r < 60; r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
				{
					if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;   // so a casca do anel
					int cx = cx0 + dx, cy = cy0 + dy;
					if (!mapa.BlockedCell(cx, cy) || mapa.NaBorda(cx, cy)
						|| mapa.Indestrutivel(cx, cy)) continue;

					// UM TILE AO SUL DELA, OLHANDO PRO NORTE: e a unica combinacao em que a celula
					// da frente (`MeleeArea.Frente`) cai exatamente na parede.
					pl.Pos = new Vec2(cx * T + T / 2f, (cy + 1) * T + T / 2f - MoveRules.FeetOffsetY);
					pl.Facing = Facing.North;
					GD.Print($"[server] BANCADA: {pl.Name} nasce colado na parede em ({cx},{cy})");
					MartelarComOSoco(pl, cx, cy);
					MartelarNoDuro(pl, mapa);
					MartelarNaObra(pl);
					return;
				}

		GD.PushWarning("[server] BANCADA: nao achei parede em 60 tiles");
	}

	/// <summary>
	/// BANCADA: O CONTRA-EXEMPLO -- socar o que NAO PODE cair, e cobrar que ele fique de pe.
	///
	/// ============================ POR QUE ELA E OBRIGATORIA, E NAO UM EXTRA ============================
	/// Porque o defeito que ela guarda e o que o dono relatou, e ele e INVISIVEL numa bancada que so
	/// mede quebra: "a parede caiu no 3o soco" fica verde tanto no mundo certo quanto no mundo em que
	/// TUDO cai, inclusive o vazio. A unica prova de que o `destroyable = 0` chegou ao servidor e
	/// bater numa celula dura e ela CONTINUAR la -- e a bancada tem que exigir isso, senao a proxima
	/// vez que o `.duro` deixar de ser lido (manifesto reescrito sem a chave, pipeline nao rodado, o
	/// `CarregarDuro` recusando calado por tamanho errado) tudo segue verde.
	///
	/// E a mesma licao ja escrita neste arquivo pro caso oposto: "ver FALHA no caso certo treina quem
	/// le o log a ignorar o log". Aqui o caso certo e a parede AGUENTAR.
	/// ==================================================================================================
	///
	/// A celula dura mais proxima pode estar longe (na Terra as costuras de vazio ficam nas beiradas
	/// do desenho), entao o corpo e TELEPORTADO pra junto dela -- honesto pelo mesmo motivo do
	/// <see cref="MartelarNaObra"/>: o que se mede e a RECUSA, e nao se da pra andar ate la.
	///
	/// NAO ACHAR NENHUMA NAO E FALHA: tres zonas do jogo (Makyo_Star, Inbetween_Realm, Void) nao tem
	/// typepath indestrutivel nenhum, e uma zona gerada por semente nao tem `.duro`.
	/// </summary>
	private void MartelarNoDuro(ServerPlayer pl, ZoneCollision mapa)
	{
		if (!mapa.TemDuro)
		{
			GD.Print("[server] BANCADA: esta zona nao tem plano de indestrutivel -- nada a provar aqui");
			return;
		}

		const int T = ZoneCollision.TileSize;
		(int cx0, int cy0) = CelulaDoPonto(pl.Pos);

		for (int r = 1; r < 200; r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
				{
					if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;   // so a casca do anel
					int cx = cx0 + dx, cy = cy0 + dy;

					// DURA, QUE BLOQUEIA, E FORA DA BEIRADA: as tres condicoes juntas. A beirada sai
					// porque ela ja tem a recusa DELA (`NaBorda`), e provar a guarda velha nao prova a
					// nova -- era exatamente esse engano que deixava o miolo do Templo cair.
					if (!mapa.BlockedCell(cx, cy) || mapa.NaBorda(cx, cy) || !mapa.Indestrutivel(cx, cy))
						continue;
					// E COM CHAO LIVRE AO SUL, senao nao ha de onde socar.
					if (mapa.BlockedCell(cx, cy + 1)) continue;

					pl.Pos = new Vec2(cx * T + T / 2f, (cy + 1) * T + T / 2f - MoveRules.FeetOffsetY);
					pl.Facing = Facing.North;
					GD.Print($"[server] BANCADA: socando o INDESTRUTIVEL em ({cx},{cy}) -- "
							 + $"BP {pl.Ficha.expressedBP:0} (resistencia {Empurrao.ResistenciaPadrao})");

					for (int i = 1; i <= 40; i++)
					{
						if (!SocarCenario(pl))
						{
							GD.PushWarning($"[server] BANCADA: FALHA -- o soco nao acha a celula dura "
										   + $"em ({cx},{cy})");
							return;
						}
						if (!mapa.BlockedCell(cx, cy))
						{
							GD.PushError($"[server] BANCADA: FALHA -- a celula INDESTRUTIVEL em "
										 + $"({cx},{cy}) caiu no {i}o soco. O `destroyable = 0` do DM "
										 + "nao chegou ao servidor: confira o `.duro` da zona e o "
										 + "`CarregarDuro` do boot.");
							return;
						}
					}

					GD.Print($"[server] BANCADA: a celula INDESTRUTIVEL em ({cx},{cy}) aguentou 40 "
							 + "socos, e devia mesmo -- e o `destroyable = 0` do original");
					return;
				}

		GD.Print("[server] BANCADA: nao achei celula dura socavel em 200 tiles");
	}

	/// <summary>
	/// BANCADA: a OUTRA metade -- socar uma obra, que cai por armadura e nao por limiar.
	///
	/// Sao dois sistemas (ver <see cref="Armadura"/>) e um teste que so exercita o turf deixaria o
	/// caminho da arvore e da bancada sem nenhuma prova. Aqui o corpo e teleportado pra junto da
	/// obra mais proxima da zona -- teleportar e honesto porque o que se testa e o ESTRAGO, e nao
	/// se da pra andar ate la.
	/// </summary>
	private void MartelarNaObra(ServerPlayer pl)
	{
		Obra? alvo = _noChao
			.Where(o => o.Zona.Equals(pl.Zone))
			.OrderBy(o => (o.X - pl.Pos.X) * (o.X - pl.Pos.X) + (o.Y - pl.Pos.Y) * (o.Y - pl.Pos.Y))
			.FirstOrDefault();
		if (alvo == null) { GD.Print("[server] BANCADA: esta zona nao tem obra pra socar"); return; }

		string nome = _obras?.Get(alvo.Tipo)?.Nome ?? alvo.Tipo;
		int id = alvo.Id;
		const int T = ZoneCollision.TileSize;

		(int ox, int oy) = CatalogoDeObras.Celula(alvo.X, alvo.Y);
		pl.Pos = new Vec2(ox * T + T / 2f, (oy + 1) * T + T / 2f - MoveRules.FeetOffsetY);
		pl.Facing = Facing.North;

		GD.Print($"[server] BANCADA: socando {nome} (#{id}) em ({ox},{oy}) -- "
				 + $"armadura {alvo.ArmaduraMax:0.##}, piso {Armadura.Piso(alvo.ArmaduraMax):0.##}, "
				 + $"BP {pl.Ficha.expressedBP:0}");

		for (int i = 1; i <= 40; i++)
		{
			if (!SocarCenario(pl))
			{
				GD.PushWarning($"[server] BANCADA: FALHA -- o soco nao acha {nome} em ({ox},{oy})");
				return;
			}
			if (_noChao.All(o => o.Id != id))
			{
				GD.Print($"[server] BANCADA: {nome} caiu no {i}o soco "
						 + $"(previsto {Armadura.Pancadas(alvo.ArmaduraMax, pl.Ficha.expressedBP)})");
				return;
			}
		}

		GD.PushWarning($"[server] BANCADA: FALHA -- 40 socos e {nome} continua de pe "
					   + $"(armadura {alvo.Armadura:0.##}/{alvo.ArmaduraMax:0.##})");
	}

	/// <summary>
	/// BANCADA: soca a parede da frente ate ela cair, e conta quantas tentativas foram.
	///
	/// ============================ POR QUE O ROBO NAO SERVE AQUI ============================
	/// O robo de soco ANDA e VIRA sozinho, e a direcao do corpo vem do cliente -- ou seja, meio
	/// segundo depois de nascer colado na parede ele ja esta de lado e socando o vazio. Foi o que
	/// aconteceu na primeira tentativa: 45 golpes, 42 "erros", nenhum encostou em cenario.
	///
	/// Entao a bancada chama a FUNCAO DE VERDADE (<see cref="SocarCenario"/>), como o
	/// `MartelarABorda` ja faz com a destruicao por arremesso. Um atalho de teste que nao passa
	/// pelo codigo de producao testa o atalho.
	///
	/// O NUMERO ESPERADO E ~3: a chance por soco e 34%, entao a media e 1/0,34. Ver dez tentativas
	/// sem quebrar seria sinal de que a celula da frente nao e a que se acha que e.
	/// </summary>
	private void MartelarComOSoco(ServerPlayer pl, int cx, int cy)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		if (mapa == null) return;

		// FRACO DEMAIS NAO E FALHA, E A REGRA. `Resistance` e um LIMIAR: abaixo dele a parede nao
		// cede nunca, por mais socos que levem. Um personagem recem-criado sorteia BP em volta de
		// 20, entao os dois lados desta linha acontecem de verdade -- e ver "FALHA" no caso certo
		// treina quem le o log a ignorar o log.
		bool alcanca = pl.Ficha.expressedBP >= Empurrao.ResistenciaPadrao;

		for (int i = 1; i <= 40; i++)
		{
			if (!SocarCenario(pl))
			{
				GD.PushWarning($"[server] BANCADA: FALHA -- o soco nao acha a parede em ({cx},{cy}) "
							   + $"olhando {pl.Facing} de ({pl.Pos.X:0},{pl.Pos.Y:0})");
				return;
			}

			if (!mapa.BlockedCell(cx, cy))
			{
				GD.Print(alcanca
					? $"[server] BANCADA: a parede em ({cx},{cy}) caiu no {i}o soco "
					  + $"(BP {pl.Ficha.expressedBP:0} >= resistencia {Empurrao.ResistenciaPadrao})"
					: $"[server] BANCADA: FALHA -- a parede caiu com BP {pl.Ficha.expressedBP:0}, "
					  + $"abaixo da resistencia {Empurrao.ResistenciaPadrao}");
				return;
			}
		}

		GD.Print(alcanca
			? $"[server] BANCADA: FALHA -- 40 socos com BP {pl.Ficha.expressedBP:0} e a parede em "
			  + $"({cx},{cy}) continua de pe"
			: $"[server] BANCADA: a parede em ({cx},{cy}) AGUENTOU 40 socos, e devia mesmo -- "
			  + $"BP {pl.Ficha.expressedBP:0} nao alcanca a resistencia {Empurrao.ResistenciaPadrao}");
	}

	/// <summary>
	/// AS OBRAS FRAGEIS NO CAMINHO DE UM CORPO ARREMESSADO.
	///
	/// E o `for(var/obj/O in get_step(target,dir)) if(O.fragile) O.takeDamage(pow)` do
	/// `Movement Effects.dm:74-76`: o voo estraga o que atravessa, com a MESMA forca com que
	/// derruba parede.
	/// </summary>
	private void EstragarObrasNoCaminho(ServerPlayer pl, Vec2 ponto)
	{
		(int cx, int cy) = CelulaDoPonto(ponto);
		if (ObraNaCelula(pl.Zone, cx, cy) is { } o) Estragar(o, pl.ForcaDoVoo, null);
	}
}
