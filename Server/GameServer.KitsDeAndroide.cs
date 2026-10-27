using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// (v0.2.1, lote T5 da tech) O KIT DE REPARO E O KIT MECANICO -- a cura do androide de absorcao, que o regenerador nao
/// cura (regra do dono, 2026-10-13). A regra inteira, com a frase dele, esta em `Core/Tech/KitsDeAndroide.cs`.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>As acoes de mochila deste lote. Falso quando a acao nao e daqui.</summary>
	private bool AcaoDeKit(ServerPlayer pl, ItemDef def, string acao, string arg2)
	{
		switch (acao)
		{
			case KitsDeAndroide.AcaoReparar: RepararComOKit(pl); return true;
			case KitsDeAndroide.AcaoRefazer: RefazerComOKit(pl); return true;
			case KitsDeAndroide.AcaoUpgrade: UsarOUpgrade(pl); return true;   // (2026-10-26) o pedido 95c
			// (lote T7) o pente e a tinta de olhos da bancada de criacao -- `GameServer.BancadaDeCriacao.cs`.
			default: return AcaoDaBancadaDeCriacao(pl, acao, arg2);
		}
	}

	/// <summary>Os kits servem a este corpo? So ao androide de absorcao -- ver o cabecalho do `KitsDeAndroide`.</summary>
	private static bool OsKitsServemA(ServerPlayer pl) => pl.Ficha.AndroideAbsorcao || KitsDeAndroide.QualquerUmUsaDeTeste;

	/// <summary>
	/// O `verb/Repair()` (`Cyborgs.dm:149-163`): dez segundos parado e sem apanhar, e cada parte ferida ganha dez de
	/// vida; o kit se gasta. (La o kit se gasta mesmo sem nada pra consertar; aqui ele recusa antes.)
	/// </summary>
	private void RepararComOKit(ServerPlayer pl)
	{
		if (pl.Ficha.KO || CaidoPelaMorte(pl)) { Avisar(pl, "caído, você não consegue."); return; }
		if (!OsKitsServemA(pl))
		{ Avisar(pl, "o kit de reparo só conserta o corpo de um androide de absorção -- o resto do mundo se cura no regenerador."); return; }
		if (pl.Combate is not { } c) return;
		if (!c.Corpo.Partes.Any(p => !p.Decepado && p.Vida < p.VidaMax)) { Avisar(pl, "não há nada ferido em você pra consertar."); return; }

		// `"This will take a moment, don't move or take further damage!"`.
		Avisar(pl, "isto leva um instante: não se mexa nem apanhe!");
		EsperarParado(pl, KitsDeAndroide.SegundosParado, "você se mexeu (ou apanhou): o conserto não saiu.", quem =>
		{
			if (quem.Mochila.Quantos(KitsDeAndroide.Reparo) <= 0 || quem.Combate is not { } corpo) return;
			foreach (BodyPart parte in corpo.Corpo.Partes)
				if (!parte.Decepado && parte.Vida < parte.VidaMax)
					parte.Vida = Math.Min(parte.Vida + KitsDeAndroide.VidaPorKit, parte.VidaMax);   // `C.health += 10; C.health = min(C.health, C.maxhealth)`
			corpo.SincronizarVida();
			quem.CorpoEnviado = "";
			quem.Mochila.Tirar(KitsDeAndroide.Reparo);
			MandarMochila(quem);
			Falar(quem, Protocol.Fala.Emote, "conserta o próprio corpo.");   // `"[usr] repairs themselves!"`
			Avisar(quem, "o kit conserta o que estava ferido em você.");
		});
	}

	/// <summary>
	/// (2026-10-26) O ANDROID UPGRADE -- o pedido 95c do dono (a regra em `KitsDeAndroide.Upgrade`): dez segundos parado e sem apanhar, e o
	/// banco de potencial do androide de absorcao se solta 1x pelo MESMO funil dos outros liberadores (`LiberarOPotencial`, com a hora de
	/// recarga deles) -- so que pela porta que so ele tem. As recusas vem antes da espera, e nao gastam o item.
	/// </summary>
	private void UsarOUpgrade(ServerPlayer pl)
	{
		if (pl.Ficha.KO || CaidoPelaMorte(pl)) { Avisar(pl, "caído, você não consegue."); return; }
		if (!pl.Ficha.AndroideAbsorcao) { Avisar(pl, "o upgrade é do corpo de um androide de absorção -- o seu potencial se solta pelos caminhos de sempre."); return; }
		if (PorQueNaoLibera(pl, comRecarga: true, proprio: true, upgradeDeAndroide: true) is { } recusa) { Avisar(pl, recusa); return; }

		Avisar(pl, "isto leva um instante: não se mexa nem apanhe!");
		EsperarParado(pl, KitsDeAndroide.SegundosParado, "você se mexeu (ou apanhou): o upgrade não entrou.", quem =>
		{
			if (quem.Mochila.Quantos(KitsDeAndroide.Upgrade) <= 0) return;
			if (!LiberarOPotencial(quem, quem, Jandirus.Core.Stats.GainKnobs.LiberaOUpgrade, autor: "o upgrade", upgradeDeAndroide: true)) return;
			quem.Mochila.Tirar(KitsDeAndroide.Upgrade);
			MandarMochila(quem);
			Falar(quem, Protocol.Fala.Emote, "instala um upgrade no próprio corpo.");
		});
	}

	/// <summary>O primeiro membro caido que pode voltar: nao o perdido de vez, e nao o que pende de outro que tambem caiu (a mao volta com o braco).</summary>
	private static BodyPart? MembroQueOKitRefaz(ServerPlayer alvo)
	{
		if (alvo.Combate is not { } c) return null;
		foreach (BodyPart p in c.Corpo.Partes)
			if (p.Decepado && !p.Perdido && !Caiu(c.Corpo, p.Dono)) return p;
		return null;
	}

	/// <summary>
	/// O KIT MECANICO: refaz um membro perdido de um androide de absorcao -- o de quem usa, ou o de um caido a frente
	/// (`Install_Module(mob/T in view(1))`, que la so pede consentimento de quem esta de pe). Dez segundos parado e sem
	/// apanhar, como o kit de reparo.
	/// </summary>
	private void RefazerComOKit(ServerPlayer pl)
	{
		if (pl.Ficha.KO || CaidoPelaMorte(pl)) { Avisar(pl, "caído, você não consegue."); return; }
		ServerPlayer alvo = QuemEstaNaFrente(pl, o => o.Ficha.KO && !o.Ficha.dead && OsKitsServemA(o) && MembroQueOKitRefaz(o) != null) ?? pl;
		if (!OsKitsServemA(alvo))
		{ Avisar(pl, "o kit mecânico só refaz o membro de um androide de absorção -- o seu, ou o de um caído à sua frente."); return; }
		if (MembroQueOKitRefaz(alvo) is not { } perdido)
		{ Avisar(pl, "não há membro perdido que o kit possa refazer."); return; }

		int idDoAlvo = alvo.Id;
		Avisar(pl, alvo == pl
			? $"você começa a refazer o seu {perdido.Nome}: não se mexa nem apanhe!"
			: $"você começa a refazer o {perdido.Nome} de {NomeVisivel(alvo)}: não se mexa nem apanhe!");
		EsperarParado(pl, KitsDeAndroide.SegundosParado, "você se mexeu (ou apanhou): o membro não foi refeito.", quem =>
		{
			if (quem.Mochila.Quantos(KitsDeAndroide.Mecanico) <= 0) return;
			if (!_players.TryGetValue(idDoAlvo, out ServerPlayer? m) || m.Ficha.dead || m.Zone.Hash != quem.Zone.Hash
				|| Vec2.Distance(m.Pos, quem.Pos) > 1.5f * ZoneCollision.TileSize)
			{ Avisar(quem, "quem você consertava saiu do seu lado: o membro não foi refeito."); return; }
			if (MembroQueOKitRefaz(m) is not { } membro || m.Combate is not { } corpo || !corpo.Corpo.Regenerar(membro))
			{ Avisar(quem, "não há mais membro perdido que o kit possa refazer."); return; }
			AjustarGanhoDoRabo(m);
			corpo.SincronizarVida();
			m.CorpoEnviado = "";
			quem.Mochila.Tirar(KitsDeAndroide.Mecanico);
			MandarMochila(quem);
			Falar(quem, Protocol.Fala.Emote, m == quem ? $"encaixa um {membro.Nome} novo no próprio corpo." : $"encaixa um {membro.Nome} novo em {NomeVisivel(m)}.");
			if (m != quem) Avisar(m, $"{NomeVisivel(quem)} refaz o seu {membro.Nome}.");
		});
	}

	// =====================================================================
	// O TANQUE NAO CURA O ANDROIDE DE ABSORCAO -- o aviso, espacado
	// =====================================================================
	/// <summary>Quando o tanque pode explicar de novo, por corpo.</summary>
	private readonly Dictionary<int, long> _avisoDoTanqueEm = [];

	/// <summary>
	/// O regenerador explica ao androide de absorcao por que nao o cura -- e como ele se cura. De trinta em trinta
	/// segundos, e nao a cada pulso do tanque.
	/// </summary>
	private void AvisarQueOTanqueNaoCura(ServerPlayer pl, long agora)
	{
		if (_avisoDoTanqueEm.TryGetValue(pl.Id, out long em) && agora < em) return;
		_avisoDoTanqueEm[pl.Id] = agora + (long)(KitsDeAndroide.SegundosEntreAvisosDoTanque * 1000);
		Avisar(pl, "o regenerador não cura um androide de absorção: o seu corpo se conserta com o kit de reparo (e o membro perdido, com o kit mecânico).");
	}

	// =====================================================================
	// A ESPERA PARADA -- o `var/prevHP = usr.HP; var/prevloc = usr.loc; sleep(100); if(usr.HP>=prevHP&&usr.loc==prevloc&&!usr.KO)`
	// (2026-10-26) Nasceu no sintetizador de virus (o Antivirus Mike, a agulha em outro); quando ele saiu do jogo, ficou com os kits.
	// =====================================================================
	private sealed class EsperaParada
	{
		public int Quem;
		public long Em;
		public Vec2 Onde;
		public double Vida;
		public string Quebrou = "";
		public Action<ServerPlayer> AoFim = _ => { };
	}

	/// <summary>Quem esta esperando parado, e o que acontece se chegar ao fim. Uma espera por corpo: a nova troca a velha.</summary>
	private readonly Dictionary<int, EsperaParada> _esperasParadas = [];

	private void EsperarParado(ServerPlayer pl, double segundos, string quebrou, Action<ServerPlayer> aoFim) =>
		_esperasParadas[pl.Id] = new EsperaParada
		{
			Quem = pl.Id, Em = NowMs() + (long)(segundos * 1000), Onde = pl.Pos,
			Vida = pl.Combate?.Corpo.Vida() ?? 0, Quebrou = quebrou, AoFim = aoFim,
		};

	private void TickDasEsperasParadas()
	{
		if (_esperasParadas.Count == 0) return;
		long agora = NowMs();
		foreach (EsperaParada e in _esperasParadas.Values.ToList())
		{
			if (!_players.TryGetValue(e.Quem, out ServerPlayer? pl)) { _esperasParadas.Remove(e.Quem); continue; }
			bool quebrou = pl.Ficha.KO || pl.Ficha.dead || (pl.Pos - e.Onde).LengthSquared > 16f
						   || (pl.Combate?.Corpo.Vida() ?? 0) < e.Vida - 1e-6;
			if (quebrou)
			{
				_esperasParadas.Remove(e.Quem);
				Avisar(pl, e.Quebrou);
				continue;
			}
			if (agora < e.Em) continue;
			_esperasParadas.Remove(e.Quem);
			e.AoFim(pl);
		}
	}

	/// <summary>Quem saiu do mundo leva o aviso dele, e a espera parada.</summary>
	private void EsquecerKits(int id)
	{
		_avisoDoTanqueEm.Remove(id);
		_esperasParadas.Remove(id);
	}
}
