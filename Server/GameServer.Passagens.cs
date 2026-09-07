using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PASSAGENS ENTRE MAPAS -- pisou, mudou de mundo.
///
/// ============================ ELAS NAO EXISTIAM, E OS DESTINOS TAMBEM NAO ============================
/// A boca da caverna, a escada do Templo e a saida do Inferno eram celulas de tilemap: desenho e
/// nada mais. Pior -- tres dos destinos NEM ESTAVAM NO CATALOGO: nove andares diferentes tinham
/// `/area/Outside` como area dominante e viravam todos a zona "Outside", e o `ZoneCatalog` guarda
/// um por nome. O Templo Sagrado (z12), a caverna de Vegeta (z22) e a caverna da Terra (z23) eram
/// mapas convertidos, no disco, que o servidor nunca conseguiria carregar.
///
/// Isso foi consertado no conversor (ver `Passagens.NomeDoZ`), e este arquivo e a outra metade: o
/// gancho que leva o corpo pra la.
/// ======================================================================================================
///
/// ============================ E O MESMO CAMINHO DO DECOLAR ============================
/// O dono descreveu o efeito que queria: "assim como acontece com ir pro espaço ao apertar decolar".
/// E literalmente o mesmo caminho -- `MoveToZone`, que ja sabe tirar o corpo de uma zona, por na
/// outra, avisar os dois lados e carimbar a sequencia pra o cliente nao puxar de volta. Passagem
/// nao precisou de mecanismo novo; precisou de um gatilho.
/// =======================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>As passagens de cada zona, por nome de zona. Lidas no boot.</summary>
	private readonly Dictionary<string, List<Passagem>> _passagens =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// QUEM ACABOU DE CHEGAR POR UMA PASSAGEM, e ate quando esta imune.
	///
	/// ============================ SEM ISTO, IDA E VOLTA VIRA UM LOOP ============================
	/// A saida da caverna fica na frente da entrada correspondente do outro lado -- e assim no DM
	/// tambem. Sem carencia, o corpo chega, o tique seguinte ve o passo dele entrando na boca de
	/// volta (a tecla continua apertada), e ele volta. E de novo. O jogador ficaria piscando entre
	/// dois mapas sem conseguir andar.
	///
	/// A carencia e por PESSOA e nao por celula: ela protege a chegada, e nao a passagem. E ela e
	/// so o PISO: quem decide a volta e a BORDA do gesto (<see cref="_naBocaDaPassagem"/>) -- a tecla
	/// segurada desde a chegada nao vale, e preciso soltar e empurrar de novo.
	/// ============================================================================================
	/// </summary>
	private readonly Dictionary<int, long> _acabouDeAtravessar = [];

	/// <summary>
	/// QUEM JA ESTA EMPURRANDO UMA BOCA -- o gatilho dispara na BORDA, nao no nivel.
	///
	/// ============================ SEGURAR A TECLA NAO E ENTRAR DE NOVO ============================
	/// O corpo chega do outro lado um tile na frente da boca de volta, olhando pra ela, com a
	/// tecla ainda apertada. Se o gatilho fosse "esta empurrando a boca?", a carencia de 1,5 s
	/// venceria e ele voltaria sozinho -- e o dono viu isso: "as vezes o personagem volta". Entao o
	/// que dispara e a MUDANCA: de "nao empurrava" pra "empurra". Na chegada o corpo ja entra
	/// marcado como "empurrando", e so sai da marca no tique em que parar ou virar pra outro lado.
	///
	/// A marca so e mexida DEPOIS da carencia. Durante a troca de mapa o cliente pode mandar um
	/// tique sem `Moving` (a tela de carregamento no meio dos inputs); se a marca caisse ali, a
	/// tecla segurada viraria "empurra" de novo assim que a carencia acabasse -- o mesmo ricochete
	/// por outra porta.
	/// ==============================================================================================
	/// </summary>
	private readonly HashSet<int> _naBocaDaPassagem = [];

	/// <summary>Quanto tempo o corpo fica imune a passagens depois de atravessar uma.</summary>
	private const long MsDeCarenciaDePassagem = 1500;

	/// <summary>Le o `.passagens` de cada zona do manifesto. Chamado no boot, junto do `.col`.</summary>
	private void CarregarPassagens()
	{
		if (_catalogo == null) return;

		int total = 0;
		foreach (ZoneEntry e in _catalogo.Todas)
		{
			if (e.PassagensArq.Length == 0 || !Godot.FileAccess.FileExists(e.PassagensArq)) continue;

			List<Passagem> lista = Passagem.Parse(Godot.FileAccess.GetFileAsString(e.PassagensArq));

			// UMA PASSAGEM PRA ZONA QUE NAO EXISTE E PIOR QUE NENHUMA: ela levaria o corpo pro
			// vazio. Melhor recusar aqui, com o nome no log, do que descobrir com um jogador preso.
			int mortas = lista.RemoveAll(p => _catalogo.Get(p.Zona) == null);
			if (mortas > 0)
				GD.PushWarning($"[server] {e.Zona}: {mortas} passagem(ns) apontam pra zona inexistente");

			if (lista.Count == 0) continue;
			_passagens[e.Zona] = lista;
			e.Passagens = lista;
			total += lista.Count;

			// A BOCA E LACRADA NO MAPA DO SERVIDOR. O cliente faz o mesmo ao carregar a zona, pela
			// mesma lista (`World.LacrarPassagens`). Ver `ZoneCollision.Selar` pro porque.
			if (e.Mapa is { } mapa) foreach (Passagem p in lista) mapa.Selar(p.X, p.Y);
		}
		if (total > 0) GD.Print($"[server] passagens: {total} em {_passagens.Count} zona(s), bocas lacradas");
	}

	/// <summary>
	/// QUEM PISOU NUMA PASSAGEM ATRAVESSA. Roda no tique cheio.
	///
	/// O CUSTO E O NUMERO DE JOGADORES, e nao o de passagens -- como nas portas. So as zonas com
	/// gente sao olhadas, e a mais carregada tem seis.
	/// </summary>
	private void TickDasPassagens()
	{
		long agora = NowMs();

		// A LISTA E COPIADA porque atravessar MEXE em `_players` por dentro (`MoveToZone` troca a
		// lista da zona), e iterar uma colecao que muda no meio estoura.
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			if (pl.Ficha.KO) continue;

			// ============================ O MORTO ATRAVESSA, MAS SO DENTRO DO ALEM ============================
			// Esta linha era `if (pl.Ficha.dead || pl.Ficha.KO) continue`, e ela **apagava um lugar
			// inteiro do jogo sem que nada reclamasse**: com ela, o morto que chega ao Outro Mundo nunca
			// alcanca o Ceu nem o Inferno, porque as tres unicas passagens do z6 sao justamente essas
			// duas (`z06_Afterlife.passagens`) -- e do outro lado, a volta.
			//
			// O ORIGINAL CONTA COM A TRAVESSIA: os cacos de revivencia so funcionam em
			// `Afterlife/Heaven/Hell` (`RevivalShards.dm:109`), o Kai ganha 1,25x **no Ceu** e o Demonio
			// **no Inferno** (`Gravity.dm:131-132`), e a gravidade do Inferno e 10x contra 1x do Ceu
			// (`:107-111`). Tres regras que so existem pra quem PODE ir aos tres lugares.
			//
			// E ISTO JA ESTAVA PREVISTO DO OUTRO LADO: o `Alem.MortoDePe` diz, por escrito, que ele
			// responde as tres zonas juntas "senao o morto que atravessa a passagem pro Inferno deitaria
			// no chao de novo do outro lado". Aquele caso nao podia acontecer -- a guarda daqui o
			// impedia. O comentario descrevia a intencao; esta linha e o que faltava pra ela ser
			// verdade.
			//
			// O `KO` FICA SOZINHO na linha de cima: caido e caido, e um corpo desmaiado empurrado pra
			// cima de uma boca de caverna nao "atravessa" nada.
			//
			// FORA DO ALEM O MORTO CONTINUA PARADO, e nao por esta regra: la ele esta `Deitado` (o
			// cadaver dos 15 s, `ServerPlayer.Deitado`) e nao anda ate onde ha passagem. A guarda esta
			// escrita mesmo assim porque ela e a metade barata do `Aging.dm:123` -- ver
			// <see cref="OAlemNaoDeixaSair"/> pra a outra metade, a que importa.
			// ==========================================================================================
			if (pl.Ficha.dead && !Alem.EhOAlem(pl.Zone)) continue;

			if (_acabouDeAtravessar.TryGetValue(pl.Id, out long livre) && agora < livre) continue;
			if (!_passagens.TryGetValue(pl.Zone.Name, out List<Passagem>? lista)) continue;

			// O PASSO QUE ENTRA NA BOCA, e nao "o pe em cima dela". A boca esta lacrada (ninguem pisa
			// nela, como no BYOND), entao o gesto e o do `Enter()`: andando, com a caixa dos pes
			// projetada um tile no rumo do olhar tocando a celula. Ver `Passagem.NoPasso`.
			Passagem? boca = null;
			if (pl.Moving)
				foreach (Passagem p in lista)
					if (p.NoPasso(pl.Pos, pl.Facing)) { boca = p; break; }

			// A BORDA DO GESTO. Parou ou virou: a marca cai e a boca esta armada de novo. Continua
			// empurrando desde o ultimo tique (ou desde a chegada): nada acontece.
			if (boca == null) { _naBocaDaPassagem.Remove(pl.Id); continue; }
			if (!_naBocaDaPassagem.Add(pl.Id)) continue;

			// A PRISAO DA SALA DO TEMPO RECUSA AQUI, e nao no `Atravessar`: a recusa e uma
			// resposta ao GESTO (empurrar a saida), e ela precisa acontecer antes de a carencia
			// ser armada la dentro -- senao quem esta preso ficaria 1,5 s sem nem ouvir por que
			// nao saiu. Ver `GameServer.SalaSessao.cs`.
			if (APrisaoRecusaASaida(pl, boca)) continue;

			// E O ALEM NAO DEIXA SAIR -- pelo mesmo motivo e no mesmo lugar que a prisao da Sala:
			// a recusa responde ao GESTO, antes de a carencia ser armada.
			if (OInfernoNaoDeixaSair(pl, boca)) continue;

			if (OAlemNaoDeixaSair(pl, boca)) continue;

			Atravessar(pl, boca);
		}
	}

	private void Atravessar(ServerPlayer pl, Passagem p)
	{
		// A CARENCIA E POSTA ANTES DA MUDANCA, e no jogador -- a saida do outro lado costuma cair
		// em cima da entrada de volta, e sem isto o corpo ricochetearia entre os dois mapas.
		_acabouDeAtravessar[pl.Id] = NowMs() + MsDeCarenciaDePassagem;
		// CHEGA "EMPURRANDO": a tecla que trouxe o corpo ate aqui continua apertada do outro lado, e
		// nao pode contar como um gesto novo. Ver `_naBocaDaPassagem`.
		_naBocaDaPassagem.Add(pl.Id);

		string destino = p.Nome.Length > 0 ? p.Nome : p.Zona;

		// O DESTINO PODE TER EXPLODIDO ENQUANTO A PESSOA ESTAVA DO OUTRO LADO. Ver
		// `SaidaParaUmMundoMorto`: o Templo, a Sala do Tempo e as cavernas tem saida CRAVADA no mapa,
		// e sem esta linha elas depositavam o corpo dentro de um cadaver de planeta.
		//
		// **AQUI E NAO NO LACO DE CIMA**: a carencia (`_acabouDeAtravessar`) ja foi armada, e e ela
		// que impede o corpo de ricochetear na boca da passagem. Recusar antes dela faria a pessoa
		// pisar na saida trinta vezes por segundo.
		if (SaidaParaUmMundoMorto(pl, ZoneKey.Premade(p.Zona))) return;

		Avisar(pl, $"você atravessa e chega em {destino}.");
		GD.Print($"[server] {pl.Name}: {pl.Zone.Name} -> {p.Zona} ({destino})");

		MoveToZone(pl.Id, ZoneKey.Premade(p.Zona), ChegadaDaPassagem(pl.Zone.Name, p));
	}

	/// <summary>
	/// ONDE ESTA PASSAGEM DEIXA O CORPO: um tile na frente da boca de volta, nunca dentro de parede.
	/// A regra e do Core (<see cref="Passagem.Chegada"/>); aqui so se juntam o mapa lacrado do destino
	/// e a lista de bocas dele. Sem mapa (zona sem colisao) vale o ponto que o DM cravou.
	/// </summary>
	private Vec2 ChegadaDaPassagem(string origem, Passagem p)
	{
		var dm = new Vec2(p.Dx, p.Dy);
		if (_catalogo?.Get(p.Zona)?.Mapa is not { } mapa) return dm;
		List<Passagem> voltas = _passagens.TryGetValue(p.Zona, out List<Passagem>? l) ? l : [];
		Vec2 chegada = Passagem.Chegada(mapa, voltas, origem, dm);
		if ((chegada - dm).LengthSquared > 1f)
			GD.Print($"[server] passagem {origem} -> {p.Zona}: o DM cravava ({dm.X:0},{dm.Y:0}); "
					 + $"o corpo chega na frente da boca, em ({chegada.X:0},{chegada.Y:0})");
		return chegada;
	}

	/// <summary>
	/// ============================ DO ALEM NAO SE SAI ANDANDO ============================
	/// O `AgeCheck` do original resolve isto DEPOIS do fato: *"if(dead && !Planet in list("Heaven",
	/// "Hell","Afterlife")) returning = 1"* (`Aging.dm:123`) -- ele acha o morto fora de lugar e o
	/// reboca de volta pro checkpoint. Aqui a mesma regra e escrita como RECUSA, na porta, e o motivo
	/// e que rebocar depois e o pior dos dois: o jogador chega em Namek, ve o cenario carregar, e e
	/// puxado de volta -- que le como bug, e nao como regra.
	///
	/// ============================ ELA E DEFENSIVA HOJE, E DE PROPOSITO ============================
	/// **Nenhuma passagem existente sai do alem**: as tres do z6 vao pro Ceu e pro Inferno, e as
	/// desses dois voltam pro z6 (conferido nos `.passagens` convertidos). Ou seja este metodo nunca
	/// recusa nada com os mapas de hoje -- e e exatamente por isso que ele precisa estar escrito.
	///
	/// A Serpentina (`Snake Way`) e um mapa do original, e o dia em que ela virar uma passagem do
	/// Outro Mundo esta regra decide sozinha se o morto pode andar ate a Terra. No DM ela nem e
	/// aberta a qualquer um: ha uma BARREIRA ESPIRITUAL na entrada (`barrier.dm:61-76`) que so cede a
	/// quem tem `kaiTrainingAllowed` -- a licenca que o Enma da. Sem o Enma portado, "ninguem passa" e
	/// a resposta certa, e nao a ausencia de resposta.
	///
	/// SE ELA VIRAR "SE O ENMA DEIXOU", e AQUI que a licenca entra.
	/// ==========================================================================================
	/// </summary>
	/// <summary>
	/// "SEMPRE QUE VOCE TENTAR SAIR VOCE E TELEPORTADO DE VOLTA" (o dono; o `afterlife_alignment_check`
	/// do DM, `SkyNPCs.dm:235-236`): com a pena correndo, a passagem que sai do Inferno nao atravessa --
	/// devolve ao portao, com a sentenca na cara.
	/// </summary>
	private bool OInfernoNaoDeixaSair(ServerPlayer pl, Passagem p)
	{
		long agora = NowMs();
		if (pl.Ficha.hell_lockout_until == 0 || agora >= pl.Ficha.hell_lockout_until) return false;
		if (string.Equals(p.Zona, Alem.ZonaDoInferno, StringComparison.OrdinalIgnoreCase)) return false;

		_acabouDeAtravessar[pl.Id] = agora + MsDeCarenciaDePassagem;
		long resta = pl.Ficha.hell_lockout_until - agora;
		Avisar(pl, $"as correntes do Inferno te puxam de volta: a sua pena ainda tem {Math.Max(1, resta / 60_000)} minuto(s).");
		ZoneKey inferno = ZoneKey.Premade(Alem.ZonaDoInferno);
		MoveToZone(pl.Id, inferno, PortaoDoInferno(inferno));
		return true;
	}

	private bool OAlemNaoDeixaSair(ServerPlayer pl, Passagem p)
	{
		if (!pl.Ficha.dead || !Alem.EhOAlem(pl.Zone) || Alem.EhOAlem(p.Zona)) return false;

		Avisar(pl, "os seus pés não passam daqui. Quem está morto não volta ao mundo dos vivos "
				   + "andando -- há um preço, e não é este.");
		return true;
	}

	/// <summary>Esquece a carencia e a marca de quem saiu -- senao os dicionarios crescem a sessao inteira.</summary>
	private void EsquecerPassagem(int id)
	{
		_acabouDeAtravessar.Remove(id);
		_naBocaDaPassagem.Remove(id);
	}
}
