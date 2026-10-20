using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ O SLEEP DO MAJIN, NO SERVIDOR (dono, 2026-10-18) ============================
/// *"o personagem ficaria 5 segundos parados, ao qual ao final ele regeneraria grande parte do seu hp dos membros, mas se for interrompido
/// nesses 5s nada acontece e tem um cooldown de 1 minuto mesmo cancelando"*. O que a skill e, os numeros e por que ela e so do Majin moram em
/// `Core/Races/SonoDoMajin.cs`; aqui mora o CORPO QUE DORME.
///
/// DORMIR E UM ESTADO DO CORPO (`ServerPlayer.SonoDesde`, a `Ocupacao.Dormindo`): a tela desenha o "z" subindo sobre ele
/// (`Client/SonoDesenhado.cs`), pra ele e pra quem esta em volta, e quem anda nao o empurra com o ombro. DORME EM PE (dono, 2026-10-19:
/// *"ele ta deitando pra dormir na skill de regeneraçao dele, ele deveria dormir em pé"*): o sono nao mexe na pose -- o corpo fica parado
/// como estava, ou pairando no ar (a linha do sono no `ServerPlayer.Pose`). A 0.4.1 o deitava no quadro de nocaute.
///
/// O QUE NAO DEIXA DORMIR (`PorQueNaoDorme`, cada um com o seu aviso): o corpo caido, andando, agarrado, ocupado com outra coisa -- e o corpo
/// ESMAGADO pelo peso ou pela gravidade (dono, 2026-10-19: *"majin q esta sofrendo dano de peso ou gravidade o jogo n deveria deixa-lo
/// dormir, dando um aviso"*).
///
/// AO DORMIR, O KI ACIMA DE 100% VOLTA PRA 100% (dono, 2026-10-19: *"ao dormir caso o ki dele esteja acima de 100% ele volta pra 100%"*).
///
/// O QUE ACORDA -- e acordado antes da hora nada acontece, e a recarga de 1 min corre do mesmo jeito:
///   * cair (o nocaute, a morte, o Majin que se desfaz);
///   * o peso ou a gravidade passando a esmagar o corpo (a maquina de gravidade subida por alguem) -- a mesma pergunta que nao o deixa dormir;
///   * apanhar: um dano encostou no corpo (o contador do funil unico, `CombatState.GolpesQueFeriram`), ou o relogio de luta foi renovado
///     (um golpe que acertou sem dano);
///   * andar, ou sair do lugar por qualquer caminho (um empurrao, um arremesso, uma viagem) -- e no ar, subir, descer, pousar ou cair: quem
///     voa DORME PAIRANDO (como medita e treina pairando), e o que acorda e o corpo sair da altura em que dormiu;
///   * agarrar ou ser agarrado;
///   * apertar qualquer outra habilidade (`UsarHabilidade` acorda antes de despachar);
///   * qualquer outra ocupacao do corpo -- soco, guarda, carga, canal, embate, treino, meditacao. O Dormindo e o ULTIMO da cadeia
///     (`CorpoOcupado.De`): se o corpo esta ocupado com qualquer outra coisa, ele ja nao esta so dormindo.
/// =======================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (`--sonoteste`): andar e sair do lugar deixam de acordar -- quem se mexe dormindo acorda refeito do mesmo jeito. Lido
	/// pelo <see cref="PorQueAcorda"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool SonoSemInterrupcaoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (`--sonoteste`, robo `--diagsono`): quem dorme o Sleep DEITA no quadro de nocaute -- o jogo da 0.4.1. Lido pela linha
	/// do sono no `ServerPlayer.Pose`. Falso em jogo, sempre.
	/// </summary>
	internal static bool SonoDeitadoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (`--sonoteste`): o peso e a gravidade deixam de barrar o sono -- o corpo esmagado dorme (o jogo da 0.4.1: o primeiro
	/// dano do castigo o acordava em ate 1 s, com a recarga de 1 min gasta), e o esmagamento que comeca no meio do sono nao o acorda por si.
	/// Lido pelo <see cref="PorQueNaoDorme"/> e pelo <see cref="PorQueAcorda"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool SonoSobOPesoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (`--sonoteste`): dormir nao mexe no Ki -- quem dorme a 150% continua a 150%, o jogo da 0.4.1. Lido pelo
	/// <see cref="Dormir"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool SonoGuardaASobrecargaDeTeste;

	/// <summary>Anuncia o Sleep no catalogo. Chamado uma vez, no boot do servidor.</summary>
	private void RegistrarTecnicasDoSono()
	{
		IniciarLote("sono");
		Vivo(SonoDoMajin.Verbo, Dormir);
	}

	/// <summary>O verb `Sleep`: dorme em pe e comeca a contar. Recusa quem nao pode dormir agora, dizendo por que.</summary>
	private void Dormir(ServerPlayer pl)
	{
		if (pl.SonoDesde >= 0) { Avisar(pl, "você já está dormindo."); return; }
		long agora = NowMs();
		if (agora < pl.SonoLivreEm) { Avisar(pl, $"seu corpo ainda não quer dormir ({(pl.SonoLivreEm - agora) / 1000 + 1}s)."); return; }
		if (PorQueNaoDorme(pl) is { Length: > 0 } porque) { Avisar(pl, porque); return; }

		// (2026-10-19) O KI ACIMA DE 100% VOLTA PRA 100% -- o dono: *"ao dormir caso o ki dele esteja acima de 100% ele volta pra 100%"*. A
		// mesma regra do nocaute (`CombatState.Nocautear`): quem relaxa nao segura a sobrecarga, e o `kiratio` dela deixaria o corpo que dorme
		// com o poder de quem ainda esta carregado. Com 100% ou menos, nada muda.
		bool dispersou = !SonoGuardaASobrecargaDeTeste && pl.Ficha.Ki > pl.Ficha.MaxKi;
		if (dispersou) pl.Ficha.Ki = pl.Ficha.MaxKi;

		pl.SonoDesde = agora;
		pl.SonoOnde = pl.Pos;
		pl.SonoVoando = pl.Voando;
		pl.SonoAltura = pl.Altitude;
		pl.SonoGolpes = pl.Combate!.GolpesQueFeriram;
		RetratarOSono(pl, pl.Combate);
		Falar(pl, Protocol.Fala.Emote, "dorme em pé.");
		Avisar(pl, (dispersou ? "você dorme, e o Ki acima de 100% se dispersa." : "você dorme.")
				   + $" Fique {SonoDoMajin.Segundos:0} s sem ser incomodado e o seu corpo se refaz.");
		GD.Print($"[sono] {pl.Name} dormiu" + (dispersou ? " (o Ki acima do cheio voltou pra 100%)" : ""));
	}

	/// <summary>Por que este corpo nao dorme agora ("" = dorme).</summary>
	private string PorQueNaoDorme(ServerPlayer pl)
	{
		if (pl.Combate == null || EhCadaver(pl)) return "agora não.";
		if (pl.Ficha.KO) return "você está caído.";
		// (2026-10-19) O CORPO ESMAGADO NAO DORME -- o dono: *"majin q esta sofrendo dano de peso ou gravidade o jogo n deveria deixa-lo dormir,
		// dando um aviso"*. A pergunta e a do castigo (`EsmagadoAgora`, a que o `TickDoEsmagamento` faz antes de ferir), e o aviso diz o que
		// afrouxar, como o aviso do castigo: o peso se tira, da gravidade se sai. Sem isto o corpo dormia e o primeiro dano do castigo o
		// acordava em ate 1 s -- com a recarga de 1 min gasta a toa.
		if (!SonoSobOPesoDeTeste && EsmagadoAgora(pl))
			return Esmagamento.PorPeso(pl.Ficha)
				? "não dá pra dormir com o PESO esmagando o seu corpo. Tire os pesos (menu P, aba Tech) primeiro."
				: "não dá pra dormir com a GRAVIDADE esmagando o seu corpo. Saia daqui primeiro.";
		if (pl.Moving) return "pare primeiro: não se dorme andando.";
		if (pl.AgarradoPorId != 0) return "alguém está segurando você.";
		Ocupacao o = OcupacaoDe(pl);
		return o == Ocupacao.Livre ? "" : $"agora não: você está {CorpoOcupado.Nome(o)}.";
	}

	/// <summary>
	/// O TIQUE DOS QUE DORMEM, no tique cheio (30 Hz): acorda quem foi incomodado, e refaz quem dormiu os 5 s inteiros. O relogio e o do
	/// servidor (`NowMs`) -- e ele que decide, e a tela so desenha.
	/// </summary>
	private void TickDosSonos()
	{
		long agora = NowMs();
		foreach (ServerPlayer pl in _players.Values)
		{
			if (pl.SonoDesde < 0) continue;
			if (PorQueAcorda(pl) is { Length: > 0 } motivo) { AcordarDoSono(pl, motivo); continue; }
			if (agora - pl.SonoDesde < SonoDoMajin.Segundos * 1000) { RetratarOSono(pl, pl.Combate!); continue; }
			FecharOSono(pl);
		}
	}

	/// <summary>O que acordou este corpo neste tique ("" = nada, ele continua dormindo). Ver o cabecalho.</summary>
	private string PorQueAcorda(ServerPlayer pl)
	{
		if (pl.Combate is not { } c) return "o corpo sumiu.";
		if (pl.Ficha.KO || EhCadaver(pl) || c.Desfeito) return "você cai, e o sono se perde.";
		// (2026-10-19) O ESMAGAMENTO QUE COMECA NO MEIO DO SONO ACORDA -- a mesma pergunta que nao deixa dormir (`PorQueNaoDorme`). Vem ANTES do
		// golpe: o dano do castigo tambem passa pelo funil, e o recado tem de dizer o que acordou de fato.
		if (!SonoSobOPesoDeTeste && EsmagadoAgora(pl))
			return Esmagamento.PorPeso(pl.Ficha) ? "o peso esmaga o seu corpo e acorda você." : "a gravidade esmaga o seu corpo e acorda você.";
		if (c.GolpesQueFeriram != pl.SonoGolpes || c.LutandoDeVerdade > pl.SonoLuta + 1e-6)
		{
			GD.Print($"[sono] {pl.Name}: o golpe -- {c.GolpesQueFeriram - pl.SonoGolpes} dano(s) pelo funil, relogio de luta {c.LutandoDeVerdade:0.###} s "
				   + $"(no tique anterior {pl.SonoLuta:0.###} s)");
			return "um golpe acorda você.";
		}
		if (!SonoSemInterrupcaoDeTeste && (pl.Moving || Vec2.Distance(pl.Pos, pl.SonoOnde) > 1
																	   || pl.Voando != pl.SonoVoando || Math.Abs(pl.Altitude - pl.SonoAltura) > 2))
			return "você se mexe e acorda.";
		if (pl.AgarradoPorId != 0 || pl.AgarrandoId != 0) return "um aperto acorda você.";
		Ocupacao o = OcupacaoDe(pl);
		return o is Ocupacao.Dormindo or Ocupacao.Livre ? "" : $"você acorda ({CorpoOcupado.Nome(o)}).";
	}

	/// <summary>
	/// O RETRATO DO TIQUE: o relogio de luta de AGORA, pra o tique seguinte comparar -- ele so DESCE sozinho, entao subir e golpe (o que
	/// acertou sem dano tambem renova a luta). Os golpes COM dano o funil conta (`SonoGolpes`, guardado ao deitar).
	/// </summary>
	private static void RetratarOSono(ServerPlayer pl, CombatState c) => pl.SonoLuta = c.LutandoDeVerdade;

	/// <summary>Acordou antes da hora: nada acontece, e a recarga corre do mesmo jeito.</summary>
	private void AcordarDoSono(ServerPlayer pl, string motivo)
	{
		pl.SonoDesde = -1;
		pl.SonoLivreEm = NowMs() + (long)(SonoDoMajin.SegundosDeRecarga * 1000);
		Avisar(pl, $"{motivo} O sono se perdeu -- o Sleep volta em {SonoDoMajin.SegundosDeRecarga:0} s.");
		GD.Print($"[sono] {pl.Name} acordou antes da hora: {motivo}");
	}

	/// <summary>Os 5 s inteiros: cada parte do corpo se refaz em <see cref="SonoDoMajin.FracaoDaCura"/> da vida dela (a decepada, nao).</summary>
	private void FecharOSono(ServerPlayer pl)
	{
		CombatState c = pl.Combate!;
		int refeitas = 0;
		foreach (BodyPart parte in c.Corpo.Partes)
		{
			if (parte.Decepado || parte.Vida >= parte.VidaMax) continue;
			parte.Vida = Math.Min(parte.VidaMax, parte.Vida + parte.VidaMax * SonoDoMajin.FracaoDaCura);
			refeitas++;
		}
		c.SincronizarVida();
		pl.SonoDesde = -1;
		pl.SonoLivreEm = NowMs() + (long)(SonoDoMajin.SegundosDeRecarga * 1000);
		Falar(pl, Protocol.Fala.Emote, "acorda refeito.");
		Avisar(pl, refeitas > 0 ? "você acorda: o sono refez o seu corpo." : "você acorda descansado -- o corpo já estava inteiro.");
		GD.Print($"[sono] {pl.Name} dormiu os {SonoDoMajin.Segundos:0} s: {refeitas} parte(s) refeitas");
	}

	// =====================================================================
	// AS PORTAS DAS BANCADAS
	// =====================================================================
	/// <summary>
	/// (`--doiscorposteste`, `--sonoteste`, robo `--diagsono`) poe este corpo no ponto de dormir -- a skill no livro, a recarga livre, de pe e
	/// parado -- e aperta o Sleep pela porta de producao (`UsarHabilidade`). Devolve se ele dormiu.
	/// </summary>
	internal bool DormirDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Livro == null) return false;
		pl.Livro.Dar(SonoDoMajin.Path);
		pl.SonoLivreEm = 0;
		pl.Moving = false;
		UsarHabilidade(pl, SonoDoMajin.Verbo);
		return pl.SonoDesde >= 0;
	}

	/// <summary>(robo `--diagsono`) Vira este corpo pra <paramref name="olhar"/> e o poe pra dormir (<see cref="DormirDeTeste"/>).</summary>
	internal bool DormirOlhandoDeTeste(int id, Facing olhar)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Facing = olhar;
		return DormirDeTeste(id);
	}

	/// <summary>(robo `--diagsono`) Fere cada parte do corpo ate esta fracao da vida dela -- a cena de quem precisa dormir.</summary>
	internal void SonoFerirDeTeste(int id, double fracao)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate is not { } c) return;
		foreach (BodyPart p in c.Corpo.Partes) if (!p.Decepado) p.Vida = p.VidaMax * fracao;
		c.SincronizarVida();
	}

	/// <summary>(robo `--diagsono`) A recarga do Sleep deste corpo livre agora -- a cena seguinte nao espera o minuto.</summary>
	internal void SonoLivreDeTeste(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.SonoLivreEm = 0;
	}

	/// <summary>(2026-10-19, robo `--diagsono`) Poe o Ki deste corpo nesta fracao do tanque (1,2 = 120%) -- a cena do Ki acima de 100%.</summary>
	internal void SonoKiDeTeste(int id, double fracao)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Ficha.Ki = pl.Ficha.MaxKi * fracao;
	}

	/// <summary>(robo `--diagsono`) A menor fracao de vida entre as partes inteiras deste corpo (1 sem corpo).</summary>
	internal double SonoVidaMinimaDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate is { } c && c.Corpo.Partes.Any(p => !p.Decepado)
			? c.Corpo.Partes.Where(p => !p.Decepado).Min(p => p.Fracao) : 1;

	/// <summary>(bancadas) Este corpo dorme? E quanto falta, em segundos, pro sono fechar (-1 acordado), no relogio do servidor.</summary>
	internal (bool Dormindo, double Falta) SonoDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) && pl.SonoDesde >= 0
			? (true, SonoDoMajin.Segundos - (NowMs() - pl.SonoDesde) / 1000.0)
			: (false, -1);
}
