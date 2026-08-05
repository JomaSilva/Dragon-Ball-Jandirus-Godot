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

		// ---- 2. UM TILE DENSO? ----
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(a.Zone);
		if (mapa == null || !mapa.BlockedCell(cx, cy)) return false;

		// O SOM SAI ANTES DE SABER SE QUEBROU, e sai sempre. E o que o original faz
		// (`attack_proc.dm:99-103`: os sons de soco vem antes do `prob(34)`), e e o que da peso a
		// bater numa parede que aguenta -- silencio ali seria indistinguivel de socar o vazio.
		Baque(a, nivel);

		// A BORDA E O `destroyable = 0` DAQUI (ver `DerrubarCenario`): ela recusa antes de olhar
		// forca, entao bater nela e sempre so barulho.
		if (mapa.NaBorda(cx, cy)) { Avisar(a, "isto e o limite do mundo -- nao ha o que quebrar."); return true; }

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
			MandarObras(ZoneKey.Premade(o.Zona));
			return;
		}

		// ---- CEDEU ----
		_noChao.Remove(o);
		if (!o.DoMapa) GravarMundo();

		var zona = ZoneKey.Premade(o.Zona);

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
		if (o.Zona != zona.Name) return false;
		(int ox, int oy) = CatalogoDeObras.Celula(o.X, o.Y);
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
	/// BANCADA: poe o corpo encostado no cenario mais proximo, olhando pra ele.
	///
	/// Existe porque o caso de teste e chato de montar a mao: o campo da Terra nao tem parede
	/// perto do spawn, e o planeta gerado ESCAVA uma clareira em volta do pouso -- ou seja, os dois
	/// lugares onde uma bancada nasce sao justamente os dois sem nada pra socar. Sem isto, provar
	/// que socar cenario funciona depende de alguem andar ate achar um muro.
	///
	/// Anda em aneis a partir do corpo, igual a busca de clareira do gerador, e para no primeiro
	/// tile que bloqueia e que NAO e borda do mundo (a borda recusa antes de olhar forca, entao
	/// nascer de frente pra ela testaria a recusa e nao a quebra).
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
					if (!mapa.BlockedCell(cx, cy) || mapa.NaBorda(cx, cy)) continue;

					// UM TILE AO SUL DELA, OLHANDO PRO NORTE: e a unica combinacao em que a celula
					// da frente (`MeleeArea.Frente`) cai exatamente na parede.
					pl.Pos = new Vec2(cx * T + T / 2f, (cy + 1) * T + T / 2f - MoveRules.FeetOffsetY);
					pl.Facing = Facing.North;
					GD.Print($"[server] BANCADA: {pl.Name} nasce colado na parede em ({cx},{cy})");
					MartelarComOSoco(pl, cx, cy);
					MartelarNaObra(pl);
					return;
				}

		GD.PushWarning("[server] BANCADA: nao achei parede em 60 tiles");
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
			.Where(o => o.Zona == pl.Zone.Name)
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
