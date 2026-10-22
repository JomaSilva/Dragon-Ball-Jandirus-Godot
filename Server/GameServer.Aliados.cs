using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-21) OS ALIADOS NO MUNDO -- o habitante recrutado pela amizade (`GameServer.Amizade.cs`). Decisao do dono: *"vira
/// companheiro"* -- ele anda no MOTOR DA CRIATURA DE CAPSULA (`GameServer.Companheiros.cs`): solto, e um corpo sem dono de tela que
/// segue o dono, obedece seguir/ficar/atacar/matar, ganha experiencia lutando, tem os dois reflexos e volta pra "casa" quando o dono
/// sai da zona ou do jogo. O registro dele mora na ficha do personagem (`Fighter.Aliados`), e as ordens saem da aba Allies.
///
/// ============================ O QUE E SO DO ALIADO ============================
///   * A MORTE E DEFINITIVA (decisao do orquestrador): ele morre como o habitante que era -- deixa o corpo e sai da aba. A criatura de
///     capsula volta pra capsula; o aliado nao volta (senao a furia viraria fazenda).
///   * O LUTO, "COMO AMIGO JOGADOR" (decisao do dono): o NOCAUTE dele acende a raiva do dono, e a MORTE a furia -- pelo mesmo gancho do
///     amigo declarado (`AmigoAbatido`), com as mesmas condicoes (o dono estava vendo; quem derrubou nao foi o proprio dono, nem outro
///     companheiro dele, nem um amigo dele). Isto derruba, SO PRA ALIADO, o veto escrito em `EhPessoa` (`GameServer.Convivio.cs`): o
///     NPC que nao e aliado continua fora do luto. Ver `LutoDoAliado`.
///   * O TETO: ate `Aliados.Maximo` por personagem; soltos, os dois do motor (`Companheiros.MaxSoltos`), contando junto as criaturas de
///     capsula do dono -- o mesmo `out_mobs` do DM.
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// 1. ONDE OS ALIADOS MORAM
	// =====================================================================
	/// <summary>
	/// A FICHA DE VERDADE DESTE PERSONAGEM: o Neo Tuffle dentro de outro corpo carrega a ficha do HOSPEDE (`GameServer.NeoTuffle.cs`), e os
	/// aliados dele continuam na dele -- na ficha que a posse guardou.
	/// </summary>
	private Fighter FichaDeVerdade(ServerPlayer pl) =>
		PosseDoTuffleDe(pl.Id) is { } posse && ReferenceEquals(posse.Tuffle, pl) ? posse.FichaDoTuffle : pl.Ficha;

	private List<Aliado>? AliadosDe(ServerPlayer pl) => FichaDeVerdade(pl).Aliados;

	private List<Aliado> AliadosOuNovos(ServerPlayer pl) => FichaDeVerdade(pl).Aliados ??= [];

	/// <summary>O aliado que a aba apontou (o argumento comeca pelo `Aliado.Id`).</summary>
	private Aliado? AliadoPeloId(ServerPlayer pl, string arg)
	{
		string numero = arg.Split('/')[0].Trim();
		return int.TryParse(numero, out int id) ? AliadosDe(pl)?.Find(a => a.Id == id) : null;
	}

	/// <summary>O corpo solto deste aliado, se ele esta no mundo agora.</summary>
	private (ServerPlayer Corpo, CompanheiroSolto Estado)? SoltoDoAliado(ServerPlayer dono, Aliado a)
	{
		foreach ((int id, CompanheiroSolto s) in _companheiros)
			if (ReferenceEquals(s.Aliado, a) && s.Dono == dono.Id && _players.TryGetValue(id, out ServerPlayer? corpo)) return (corpo, s);
		return null;
	}

	/// <summary>Quantos companheiros este dono tem soltos agora -- criaturas de capsula e aliados, no mesmo teto.</summary>
	private int SoltosDe(ServerPlayer dono) => _companheiros.Values.Count(s => s.Dono == dono.Id);

	/// <summary>Por que nenhum aliado vem ate este dono agora (nulo = vem). As portas da capsula (`SoltarOuRecolher`).</summary>
	private string? PorQueNaoSolta(ServerPlayer pl)
	{
		if (pl.Ficha.KO || CaidoPelaMorte(pl)) return "caído, você não chama ninguém.";
		if (ZonaFechadaAoTeleporte(pl.Zone) || PresoNoTorneio(pl.Id)) return "aqui ninguém consegue chegar até você.";
		if (pl.CorpoDaPosse != null) return "você está no corpo de outra pessoa: ninguém reconheceria você.";
		return null;
	}

	/// <summary>O aliado aparece ao lado do dono (ou onde ele estava, no recrutamento), pelo nascimento da criatura de capsula.</summary>
	private ServerPlayer SoltarOAliado(ServerPlayer dono, Aliado a, Vec2? onde = null) => CriarCompanheiro(dono, a.Criatura, 0, a, onde);

	// =====================================================================
	// 2. AS ORDENS DA ABA ALLIES -- `aliado_<ordem> <id>[/<argumento>]`
	// =====================================================================
	/// <summary>
	/// As ordens que a aba manda. As de luta sao as da capsula (`OrdenarOAtaque`, `AlternarReflexo`, `GastarAMelhoria`), pela mesma
	/// pergunta de inteligencia e de confianca; o resto e do aliado (soltar, guardar, dispensar).
	/// </summary>
	private bool OrdemAoAliado(ServerPlayer pl, string cmd, string arg)
	{
		if (!cmd.StartsWith("aliado_", StringComparison.Ordinal)) return false;
		if (AliadoPeloId(pl, arg) is not { } a) { Avisar(pl, "você não tem esse aliado."); return true; }
		Companheiro c = a.Criatura;
		(ServerPlayer Corpo, CompanheiroSolto Estado)? solto = SoltoDoAliado(pl, a);

		switch (cmd)
		{
			case Aliados.VerboSoltar:
				if (solto != null) { Avisar(pl, $"{c.Nome} já está ao seu lado."); break; }
				if (PorQueNaoSolta(pl) is { } recusa) { Avisar(pl, recusa); break; }
				if (!Companheiros.SemTetoDeSoltosDeTeste && SoltosDe(pl) >= Companheiros.MaxSoltos)
				{ Avisar(pl, $"você já tem {Companheiros.MaxSoltos} companheiros ao seu lado: guarde um antes."); break; }
				SoltarOAliado(pl, a);
				Falar(pl, Protocol.Fala.Emote, $"chama {c.Nome}, que chega e passa a segui-lo.");
				Avisar(pl, $"{c.Nome} chega e passa a seguir você.");
				break;

			case Aliados.VerboGuardar:
				if (solto is not { } g) { Avisar(pl, $"{c.Nome} já está em casa."); break; }
				RecolherCompanheiro(g.Corpo.Id, null);
				Avisar(pl, $"{c.Nome} volta pra casa: chame-o de novo pela aba {Aliados.NomeDaAba}.");
				break;

			case Aliados.VerboSeguir:
				if (solto is not { } x || !EntendeAOrdem(pl, c, Companheiros.IntDeObedecer)) { if (solto == null) AvisarQueEstaEmCasa(pl, c); break; }
				bool fica = x.Estado.Ordem == OrdemDeCompanheiro.Seguir;
				x.Estado.Ordem = fica ? OrdemDeCompanheiro.Ficar : OrdemDeCompanheiro.Seguir;
				Avisar(pl, fica ? $"você manda {c.Nome} ficar." : $"você manda {c.Nome} seguir você.");
				break;

			case Aliados.VerboAtacar:
			case Aliados.VerboMatar:
				if (solto is not { } y) { AvisarQueEstaEmCasa(pl, c); break; }
				MandarAtacar(pl, y.Corpo, y.Estado, c, matar: cmd == Aliados.VerboMatar);
				break;

			case Aliados.VerboParar:
				if (solto is not { } z) { AvisarQueEstaEmCasa(pl, c); break; }
				LargarAPresa(z.Corpo, z.Estado);
				z.Corpo.Combate.Letal = pl.Combate?.Letal ?? false;   // a ordem de matar acaba com a luta: o golpe volta ao do dono
				Avisar(pl, $"{c.Nome} larga o que fazia e volta pra você.");
				break;

			case Aliados.VerboQuandoAtaco:
			case Aliados.VerboQuandoApanho:
				AlternarReflexoDe(pl, c, quandoAtaco: cmd == Aliados.VerboQuandoAtaco);
				break;

			case Aliados.VerboMelhorar:
				string[] partes = arg.Split('/');
				GastarAMelhoriaDe(pl, c, partes.Length > 1 ? partes[1] : "", solto?.Corpo, "ganha uma a cada nível, lutando ao seu lado");
				break;

			case Aliados.VerboDispensar:
				if (solto is { } h) RecolherCompanheiro(h.Corpo.Id, null);
				AliadosDe(pl)?.Remove(a);
				Falar(pl, Protocol.Fala.Emote, $"se despede de {c.Nome}.");
				Avisar(pl, $"{c.Nome} se despede e volta pra vida dele. Ele não é mais seu aliado.");
				GD.Print($"[server] aliados: {pl.Name} dispensou {c.Nome}");
				Persistir(pl);
				break;

			default:
				Avisar(pl, "essa ordem não existe.");
				break;
		}
		MandarAliados(pl);
		return true;
	}

	private void AvisarQueEstaEmCasa(ServerPlayer pl, Companheiro c) =>
		Avisar(pl, $"{c.Nome} está em casa: solte-o pela aba {Aliados.NomeDaAba} antes de dar ordens.");

	// =====================================================================
	// 3. A MORTE -- definitiva
	// =====================================================================
	/// <summary>DEFEITO INJETADO (bancada `--aliadosteste`): o aliado morto volta pra "casa", como a criatura de capsula -- a furia vira fazenda.</summary>
	internal static bool AliadoVoltaAoMorrerDeTeste;

	/// <summary>
	/// O ALIADO MORREU (o relogio do motor, `TickDosCompanheiros`): morre como o habitante que era -- deixa o corpo no chao (o
	/// `mobDeath` de todo NPC, `DeixarOCadaver`) e SAI DA FICHA do dono. A furia do dono ja foi acesa no golpe (`LutoDoAliado`, pelo funil
	/// da derrota); aqui so se recolhe o que sobrou.
	/// </summary>
	private void MorreuOAliado(int id, ServerPlayer corpo, CompanheiroSolto s)
	{
		if (s.Aliado is not { } a) return;
		_companheiros.Remove(id);
		_players.TryGetValue(s.Dono, out ServerPlayer? dono);
		if (!AliadoVoltaAoMorrerDeTeste)
		{
			if (dono != null) AliadosDe(dono)?.Remove(a);
			DeixarOCadaver(corpo);
		}
		RemoverNpc(corpo);
		GD.Print($"[server] aliados: {a.Criatura.Nome} morreu" + (dono != null ? $" (aliado de {dono.Name})" : ""));
		if (dono == null) return;
		Avisar(dono, AliadoVoltaAoMorrerDeTeste
			? $"{a.Criatura.Nome} foi derrubado e volta pra casa."
			: $"{a.Criatura.Nome} morreu. Ele não volta mais -- e sai da sua aba {Aliados.NomeDaAba}.");
		Persistir(dono);
		MandarAliados(dono);
	}

	// =====================================================================
	// 4. O LUTO -- "como amigo jogador"
	// =====================================================================
	/// <summary>DEFEITO INJETADO (bancada `--aliadosteste`): o aliado fica fora do luto, como todo NPC -- o veto antigo de volta.</summary>
	internal static bool AliadoForaDoLutoDeTeste;

	/// <summary>
	/// O ALIADO CAIU OU MORREU NUM GOLPE (o funil da derrota, `AoPerderALuta`): o NOCAUTE acende a raiva comum do dono, a MORTE a furia
	/// extrema -- o mesmo gancho e o mesmo degrau do amigo declarado (`LutoNaVizinhanca`), com as condicoes dele postas no lugar da lista
	/// que o NPC nao tem:
	///   1. o dono ESTAVA VENDO -- na zona, dentro do `RaioDeTestemunha`;
	///   2. QUEM DERRUBOU NAO FOI O PROPRIO DONO, nem outro companheiro dele (o tiro torto do irmao);
	///   3. o ALGOZ E INIMIGO AOS OLHOS DO DONO (`Convivio.AlgozEhInimigo`): o amigo dele derrubando o aliado e treino, e nao assassinato.
	/// O NPC que nao e aliado continua de fora (`LutoNaVizinhanca` sai no `EhPessoa`).
	/// </summary>
	private void LutoDoAliado(ServerPlayer vitima, ServerPlayer? algoz, bool morreu)
	{
		if (AliadoForaDoLutoDeTeste || algoz == null || algoz == vitima) return;
		if (!_companheiros.TryGetValue(vitima.Id, out CompanheiroSolto? s) || s.Aliado == null) return;
		if (!_players.TryGetValue(s.Dono, out ServerPlayer? dono) || !EhPessoa(dono)) return;
		if (algoz == dono) return;
		if (_companheiros.TryGetValue(algoz.Id, out CompanheiroSolto? irmao) && irmao.Dono == dono.Id) return;
		if (dono.Zone.Hash != vitima.Zone.Hash) return;
		Vec2 d = dono.Pos - vitima.Pos;
		if (d.X * d.X + d.Y * d.Y > RaioDeTestemunha * RaioDeTestemunha) return;
		if (!dono.Social.AlgozEhInimigo(algoz.Assinatura)) return;

		NivelDeRaiva grau = morreu ? NivelDeRaiva.Extrema : NivelDeRaiva.Comum;
		bool erupcao = AmigoAbatido(dono, vitima.Name, grau);
		// (o amigo morto parte a forma 1 do Corrupted Majin -- `Death/Death.dm:82` --, fora do teste de erupcao, como no `LutoNaVizinhanca`)
		if (morreu) SagaNaFuriaDaMorte(dono);
		if (erupcao) AnunciarRaiva(dono, morreu);
	}

	// =====================================================================
	// 5. A ABA ALLIES -- `S2C.Aliados`
	// =====================================================================
	/// <summary>A ultima assinatura da aba de cada um: o pacote so sai quando ela muda (e a PRIMEIRA de cada sessao sai sempre, vazia que seja).</summary>
	private readonly Dictionary<int, string> _sigAliados = [];

	/// <summary>A ESCUTA DA BANCADA: cada aba que sairia, com quem, e os bytes. Nula em jogo.</summary>
	internal static List<(int Quem, byte[] Pacote)>? EscutaDosAliados;

	/// <summary>DEFEITO INJETADO (bancada `--aliadosteste`): o pacote leva o poder do aliado em numero, com ou sem medidor.</summary>
	internal static bool PoderDoAliadoEmNumeroDeTeste;

	/// <summary>
	/// O QUE A ABA DESTE PERSONAGEM DESENHA: os aliados (com o poder ESCRITO pela regra do BP -- em numero so com medidor) e as amizades
	/// em andamento, as mais perto de encher primeiro.
	/// </summary>
	private AliadosState EstadoDosAliados(ServerPlayer pl)
	{
		var aliados = new List<AliadoNaAba>();
		foreach (Aliado a in AliadosDe(pl) ?? [])
		{
			Companheiro c = a.Criatura;
			(ServerPlayer Corpo, CompanheiroSolto Estado)? solto = SoltoDoAliado(pl, a);
			EstadoDeAliado e = EstadoDeAliado.Nenhum;
			if (solto is { } x)
			{
				e |= EstadoDeAliado.Solto;
				if (x.Estado.Ordem == OrdemDeCompanheiro.Ficar) e |= EstadoDeAliado.Ficando;
				if (x.Estado.Presa != 0) e |= EstadoDeAliado.Lutando;
				if (x.Corpo.Ficha.KO) e |= EstadoDeAliado.Caido;
				if (x.Corpo.Combate.Letal) e |= EstadoDeAliado.Matando;
			}
			if (c.QuandoAtaco) e |= EstadoDeAliado.QuandoAtaco;
			if (c.QuandoApanho) e |= EstadoDeAliado.QuandoApanho;
			string poder = PoderDoAliadoEmNumeroDeTeste ? c.Bp.ToString("N0") : PoderEscrito(pl, c.Bp);
			aliados.Add(new AliadoNaAba(a.Id, c.Nome, c.Raca, c.Nivel, (int)c.Exp, (int)Companheiros.ExpDoNivel(c.Nivel), poder,
										(int)Math.Floor(c.Relacao), c.Inteligencia, c.Sobras, e, a.Planeta));
		}

		var amizades = new List<(double Pontos, AmizadeNaAba Linha)>();
		if (pl.Assinatura.Length > 0)
			foreach ((int id, Dictionary<string, LacoDeAmizade> lacos) in _amizades)
				if (lacos.TryGetValue(pl.Assinatura, out LacoDeAmizade? l) && _players.TryGetValue(id, out ServerPlayer? npc) && HabitanteDeAmizade(npc))
					amizades.Add((l.Pontos, new AmizadeNaAba(npc.Name, npc.Race, (int)Math.Floor(l.Pontos), NomeDoPlaneta(npc.Zone.Name))));

		return new AliadosState(Aliados.Maximo, Companheiros.MaxSoltos, aliados,
								[.. amizades.OrderByDescending(x => x.Pontos).Select(x => x.Linha)]);
	}

	/// <summary>Manda a aba a este jogador se ela mudou (ou se e a primeira desta sessao).</summary>
	private void MandarAliados(ServerPlayer pl, bool forcar = false)
	{
		if (pl.Peer == null && EscutaDosAliados == null) return;
		AliadosState s = EstadoDosAliados(pl);
		string sig = FioDosAliados.Assinatura(s);
		if (!forcar && _sigAliados.TryGetValue(pl.Id, out string? antiga) && antiga == sig) return;
		_sigAliados[pl.Id] = sig;
		var w = Protocol.Begin(Protocol.S2C.Aliados);
		FioDosAliados.Escrever(w, s);
		EscutaDosAliados?.Add((pl.Id, w.CopyData()));
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// 6. O RELOGIO -- 30 Hz pros treinos, 1 Hz pra poda e pra aba
	// =====================================================================
	private double _proximaVoltaDosAliados;

	/// <summary>O relogio da amizade e dos aliados. Chamado do `TickDaTechPortada` (`GameServer.TechDeBolso.cs`), a cada tique.</summary>
	private void TickDosAliados(double dt)
	{
		_relogioDosAliados += dt;
		if (_treinosDeAmizade.Count > 0)
			foreach (TreinoDeAmizade t in _treinosDeAmizade.Values.ToList()) JulgarOTreinoDeAmizade(t);

		if (_relogioDosAliados < _proximaVoltaDosAliados) return;
		_proximaVoltaDosAliados = _relogioDosAliados + 1;
		PodarAsAmizades();
		if (_sigAliados.Count > 0)
			foreach (int id in _sigAliados.Keys.ToList())
				if (!_players.ContainsKey(id)) _sigAliados.Remove(id);
		foreach (ServerPlayer pl in _players.Values.ToList())
			if (pl.Peer != null) MandarAliados(pl);
	}
}
