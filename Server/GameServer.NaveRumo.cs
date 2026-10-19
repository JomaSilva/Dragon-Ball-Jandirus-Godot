using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// O PILOTO AUTOMATICO DA NAVE GRANDE SEM NINGUEM AO LEME (dono, 2026-10-18): *"permita q na capital ship ao colocar pra ela ir pra um
/// planeta com nav system e o jogador sair do piloto, ela ainda continua, e avisa no chat quando chegar"*.
///
/// ============================ O RUMO MORAVA SO NO CLIENTE ============================
/// O nav system liga um piloto automatico do CLIENTE (`LocalPlayer.Destino`, e o `RumoDepoisDeDecolar` de quem esta pousado): ele anda com o
/// corpo do piloto como se fossem teclas, e a nave copia o piloto (`TickDasNaves`). Largado o leme, o corpo volta pra ponte e a nave ficava
/// onde estava -- nao havia rumo no servidor pra seguir. Agora o cliente que larga o leme com o piloto ligado manda o destino junto (o
/// argumento do `nave_pilotar`, `MenuDeInteracao`), e o servidor assume a viagem: pousada, a nave LANCA (a sequencia de 10 s da ponte); no
/// espaco, anda pro destino na velocidade DELA (`Naves.Fator`, a mesma conta da estimativa da carta estelar); sobre o planeta do destino,
/// POUSA num lugar sorteado em terra firme (`GameServer.PousoDaNave.cs`; sem lugar que sirva, ao lado do berco ou da chegada do gerado --
/// esperando a superficie nascer, como o corpo espera) -- e AVISA quem esta dentro, o dono e quem a mandou. Destino que nao e planeta (o
/// porto de um sistema): para no ponto e avisa.
///
/// QUEM ASSUME O LEME DESLIGA o rumo (`PilotarDaPonte`; *"piloto que ignora o piloto e um sequestro"*, o mesmo do cliente). Explodida ou
/// recolhida, a nave leva o rumo junto. Planeta morto: para em orbita e avisa que nao ha onde pousar (o `isDestroyed` do `testPlanetbump`).
///
/// DIVERGENCIAS DECLARADAS (o DM nao tem piloto automatico de nave -- o nav system de la so mostra o mapa): a nave sozinha so pousa no
/// planeta do DESTINO (o corpo pousa em qualquer disco que cruzar; aqui o destino e a promessa), e a estrela nao a queima (quem queima e
/// corpo do lado de fora do casco, e ela nao leva nenhum).
/// ===================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Quando o proximo pacote/gravacao da nave que segue sozinha sai -- ver o fim do <see cref="TickDasNaves"/>.</summary>
	private long _proximoPassoDoRumo;

	/// <summary>
	/// DEFEITO INJETADO (`--naveteste`, camada 7): o servidor ignora o rumo que vem com o leme largado -- o jogo de antes, em que a nave ficava
	/// parada onde o piloto a largou. Falso em jogo, sempre.
	/// </summary>
	internal static bool RumoSoNoClienteDeTeste;

	/// <summary>
	/// O PILOTO LARGOU O LEME COM O NAV SYSTEM LIGADO: a nave segue sozinha. <paramref name="arg"/> e o destino que o cliente manda, "x/y" em
	/// coordenada de galaxia; vazio = largou sem piloto automatico, e a nave fica onde esta, como sempre.
	/// </summary>
	private void ComecarARumarSozinha(ServerPlayer pl, Nave n, string arg)
	{
		string[] p = arg.Split('/');
		if (RumoSoNoClienteDeTeste || p.Length != 2 || !float.TryParse(p[0], out float x) || !float.TryParse(p[1], out float y)
			|| !NumeroFinito(x) || !NumeroFinito(y))   // "NaN" passa no `TryParse`: ver `NumeroFinito`
			return;

		// (2026-10-18) DE TANQUE SECO A NAVE NAO VAI A LUGAR NENHUM (o dono: sem combustivel ela para de vez) -- ver `GameServer.Combustivel.cs`.
		if (TanqueVazio(n) && !TanqueVazioAndaDeTeste) { Avisar(pl, FraseDoTanqueVazio(n)); return; }

		n.TemRumo = true;
		n.RumoX = x;
		n.RumoY = y;
		n.RumoPor = pl.Conta;
		bool noEspaco = Espaco.EhEspaco(n.Zona);
		if (!noEspaco) n.LancaEm = NowMs() + (long)(NaveGrande.SegundosDeLancamento * 1000);   // pousada: a sequencia de lancamento da ponte
		GravarNaves();

		string praOnde = NomeDoRumo(n);
		foreach (ServerPlayer abordo in ZoneList(NaveGrande.ZonaDoInterior(n.Id).Hash))
			Avisar(abordo, noEspaco
				? $"{NomeDaNave(n)} segue sozinha rumo a {praOnde}. Quando ela chegar, você fica sabendo."
				: $"{NomeDaNave(n)}: sequência de lançamento iniciada -- ela sobe sozinha e segue rumo a {praOnde}. Quando ela chegar, você fica sabendo.");
		GD.Print($"[server] {pl.Name} largou o leme da nave #{n.Id} com rumo a {praOnde} ({x:0},{y:0})");
	}

	/// <summary>O nome do destino da nave, pro chat: o mundo sob o rumo (com o nome de batismo), ou "o ponto marcado no nav system".</summary>
	private string NomeDoRumo(Nave n) =>
		Espaco.PlanetaSob(SeedDoUniverso, new Vec2(n.RumoX, n.RumoY)) is { } p ? NomeDoMundo(p) : "o ponto marcado no nav system";

	/// <summary>
	/// UM TIQUE DA NAVE QUE SEGUE SOZINHA NO ESPACO: sobre o planeta do destino, pousa; senao anda um passo pro rumo (e no rumo sem planeta,
	/// para e avisa). Devolve se ela ANDOU -- quem chama manda o pacote e grava uma vez por segundo.
	/// </summary>
	private bool RumarSozinha(Nave n)
	{
		var rumo = new Vec2(n.RumoX, n.RumoY);
		PlanetaNoEspaco? destino = Espaco.PlanetaSob(SeedDoUniverso, rumo);
		var aqui = new Vec2(n.X, n.Y);

		if (destino is { } alvo && Espaco.PlanetaSob(SeedDoUniverso, aqui) is { } sob && sob.Pos.X == alvo.Pos.X && sob.Pos.Y == alvo.Pos.Y)
		{
			PousarNaveSozinha(n, alvo);
			return false;
		}

		// (2026-10-18) A NAVE SECA PARA ONDE ESTIVER -- quem desliga o rumo no passo em que o tanque seca e o `AcabouOCombustivel`; esta e a
		// guarda da que ja voltou seca (um reinicio no meio da viagem). Ver `GameServer.Combustivel.cs`.
		if (TanqueVazio(n) && !TanqueVazioAndaDeTeste)
		{
			n.TemRumo = false;
			GravarNaves();
			AvisarDoRumo(n, FraseDoTanqueVazio(n));
			return false;
		}

		Vec2 falta = rumo - aqui;
		float passo = (float)(MoveRules.BaseSpeedPx * Naves.Fator(n.Velocidade) * Protocol.TickSeconds);
		if (falta.Length > passo)
		{
			Vec2 d = falta.Normalized() * passo;
			n.X += d.X;
			n.Y += d.Y;
			GastarCombustivel(n, passo);   // (2026-10-18) o passo paga o tanque
			return true;
		}

		n.X = rumo.X;
		n.Y = rumo.Y;
		GastarCombustivel(n, falta.Length);
		if (destino != null) return true;   // o centro de um planeta esta sob o disco dele: o proximo tique pousa

		// O PORTO DE UM SISTEMA (ou qualquer ponto vazio): chegou, e fica ali.
		n.TemRumo = false;
		GravarNaves();
		MandarObras(n.Zona);
		AvisarDoRumo(n, $"{NomeDaNave(n)} chegou ao ponto marcado no nav system e parou ali, no espaço.");
		GD.Print($"[server] nave #{n.Id} chegou sozinha ao ponto ({n.X:0},{n.Y:0})");
		return false;
	}

	/// <summary>
	/// A NAVE SOZINHA DESCE NO PLANETA DO DESTINO -- o pouso do `TickDoEspaco`, sem o corpo. Planeta morto nao recebe ninguem: ela para em
	/// orbita. (2026-10-18) Pousa num LUGAR SORTEADO, em terra firme e com a folga do casco (`PousoSorteadoEm`, o mesmo do piloto ao leme). Sem
	/// lugar que sirva, o pouso de antes: no pre-feito ao lado do berco (`PontoDeNascimento` -- e ali que o corpo chegaria, mas a nave e DENSA:
	/// em cima do berco ela prenderia quem nasce, entao ela pousa tres tiles ao lado, na primeira celula livre, `ChaoLivrePerto`), no gerado
	/// ao lado da chegada que o gerador garantiu livre. No gerado, so quando a superficie existir -- enquanto ela nasce (`MundoDaZona`
	/// encomenda), a nave segura a orbita e o tique seguinte pergunta de novo.
	/// </summary>
	private void PousarNaveSozinha(Nave n, PlanetaNoEspaco p)
	{
		if (PlanetaMorto(p))
		{
			n.TemRumo = false;
			GravarNaves();
			AvisarDoRumo(n, $"{NomeDaNave(n)} chegou a {NomeDoMundo(p)}, mas não há onde pousar: é um campo de destroços. Ela fica em órbita.");
			return;
		}

		ZoneKey zona;
		Vec2 chegada;
		if (p.Premade)
		{
			zona = ZoneKey.Premade(p.Nome);
			chegada = PontoDeNascimento(zona);
		}
		else
		{
			zona = ZoneKey.Procedural(p.Nome, p.Seed);
			if (MundoDaZona(zona, p) is not { } viva) return;   // a superficie esta nascendo: segura a orbita
			chegada = viva.Chegada;
		}

		if (PousoSorteadoEm(zona, naveGrande: true) is not { } ponto)
		{
			List<Vec2> ocupados = [.. _noChao.Where(o => o.Zona.Equals(zona)).Select(o => new Vec2(o.X, o.Y))
				.Concat(NavesParadasEm(zona).Select(x => new Vec2(x.X, x.Y)))];
			ponto = ChaoLivrePerto(zona, chegada + new Vec2(3 * ZoneCollision.TileSize, 0), 0, MapaDaZonaOuCatalogo(zona), ocupados,
				ZoneList(zona.Hash));
		}

		ZoneKey antes = n.Zona;
		n.PorZona(zona);
		n.X = ponto.X;
		n.Y = ponto.Y;
		n.TemRumo = false;
		GravarNaves();
		MandarObras(antes);
		MandarObras(zona);
		AvisarDoRumo(n, $"{NomeDaNave(n)} chegou: pousou em {NomeDoMundo(p)}.");
		GD.Print($"[server] nave #{n.Id} pousou sozinha em {zona} @ ({ponto.X:0},{ponto.Y:0})");
	}

	/// <summary>
	/// O AVISO DA VIAGEM -- *"e avisa no chat quando chegar"*: pra quem esta DENTRO (a tripulacao inteira, como o lancamento da ponte), pro
	/// DONO e pra quem a mandou, onde estiverem. Uma vez por pessoa.
	/// </summary>
	private void AvisarDoRumo(Nave n, string texto)
	{
		var avisados = new HashSet<int>();
		foreach (ServerPlayer abordo in ZoneList(NaveGrande.ZonaDoInterior(n.Id).Hash))
			if (EhJogador(abordo) && avisados.Add(abordo.Id)) Avisar(abordo, texto);
		foreach (ServerPlayer p in _players.Values)
			if (EhJogador(p) && (string.Equals(p.Conta, n.DonoConta, StringComparison.OrdinalIgnoreCase)
								   || string.Equals(p.Conta, n.RumoPor, StringComparison.OrdinalIgnoreCase))
				&& avisados.Add(p.Id))
				Avisar(p, texto);
	}
}
