using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ O CARGO PATRULHA GALACTICA (dono, 2026-10-31, o pedido 198i) ============================
/// O dono pediu o cargo Patrulha Galactica sem limite de membros; missoes de derrotar NPCs procurados pela galaxia (zeni e karma) e de ir
/// atras do procurado localizado ou denunciado; um botao no nav system, so pra eles, que mira o planeta da missao; um scouter de graca que
/// serve de radio; e o requisito: 50 mil de BP, karma maior que 0 e nao ser procurado (o texto inteiro esta no PEDIDO-2026-10-23.md, 198i).
///
///   * ENTRAR (`patrulha_entrar`): o BP base de `PatrulhaGalactica.BpParaEntrar`, karma acima de 0, sem ficha de procurado e fora da
///     prisao. Quem entra ganha um SCOUTER (o radio da Patrulha: o mesmo item da bancada de pesquisa, com a frequencia do radio) e a primeira
///     missao. SAIR: `patrulha_sair`. Quem vira procurado, ou perde o karma, e EXPULSO no segundo seguinte. A ficha da Patrulha
///     (`FichaDaPatrulha`, `patrulha.json`) guarda o cargo e a missao.
///   * A MISSAO (`patrulha_missao` mostra a de agora; sem missao, pega uma):
///       - (2026-10-31, o pedido 200) O VILAO POS-SAGA vem antes de tudo: onde ele invade, so a Patrulha sabe (o chat de ninguem diz). Ele
///         vira a missao de todo membro sem missao ou atras de um foragido -- no instante em que invade, e de novo a cada planeta novo
///         (`MissaoDeInvasao`) --, e quem o derrota ganha o zeni da missao do vilao (`PatrulhaGalactica.ZeniDaMissaoDeVilao`).
///       - O PROCURADO JOGADOR LOCALIZADO OU DENUNCIADO vem primeiro: a Patrulha manda o membro ao planeta onde sabe que ele esta (onde a
///         nave dela o cacou, ou onde foi denunciado). Cumprida quando o membro o ENTREGA (`patrulha_chamar`); a Patrulha prende-lo antes, ou
///         ele sair do jogo, a cancela.
///       - SEM ELE, UM FORAGIDO NPC: um planeta com povo, e o foragido nasce la quando o membro chega -- o vilao avulso, pela porta dele
///         (`NascerVilao`). Derrota-lo cumpre a missao.
///       Cumprida, o premio da Patrulha (`PatrulhaGalactica.ZeniDaMissao` e `KarmaDaMissao`) -- alem do que o vilao e a recompensa ja pagam --,
///       e a proxima missao. Ela vence em `PatrulhaGalactica.MinutosDaMissao`.
///   * O BOTAO NO NAV (so o membro): o `S2C.MissaoDaPatrulha` diz ao cliente o planeta da missao (o endereco na carta, como a casa do Bills),
///     e a barra da carta ganha o "missão da Patrulha", que leva a carta ate la e o seleciona.
/// ===============================================================================================================================
/// Bancada: `--cargopgteste`; fotos: `--diagcargopg`.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Os tipos de missao.</summary>
	internal const byte MissaoForagido = 1, MissaoProcurado = 2, MissaoVilao = 3;

	/// <summary>Os verbos do cargo -- dentro das 24 letras do fio.</summary>
	internal const string VerboEntrar = "patrulha_entrar", VerboSair = "patrulha_sair", VerboMissao = "patrulha_missao";

	/// <summary>O que cada jogador ja recebeu no `S2C.MissaoDaPatrulha` (pelo id do corpo) -- so sai de novo quando muda.</summary>
	private readonly Dictionary<int, string> _missaoMandada = [];

	/// <summary>DEFEITO INJETADO (`--cargopgteste`): a Patrulha aceita qualquer um, sem requisito. Lido pelo <see cref="PorQueNaoEntra"/>. Falso em jogo, sempre.</summary>
	internal static bool CargoSemRequisitoDeTeste;

	/// <summary>E membro da Patrulha Galactica?</summary>
	internal bool MembroDaPatrulha(ServerPlayer pl) => FichaOuNulo(pl) is { Membro: true };

	/// <summary>Por que este corpo nao entra na Patrulha ("" = entra). Os tres requisitos do dono, e a prisao.</summary>
	internal string PorQueNaoEntra(ServerPlayer pl)
	{
		if (CargoSemRequisitoDeTeste) return "";
		if (pl.Ficha.BP < PatrulhaGalactica.BpParaEntrar)
			return $"a Patrulha Galáctica só aceita quem tem {PatrulhaGalactica.BpParaEntrar.ToString("N0", Pt)} de poder ou mais.";
		if (pl.Karma <= 0) return "a Patrulha Galáctica só aceita quem tem o karma limpo (maior que zero).";
		if (NivelDeProcurado(pl) > 0) return "a Patrulha Galáctica não aceita um procurado.";
		if (PresoNaPatrulha(pl)) return "quem cumpre pena não entra na Patrulha Galáctica.";
		return "";
	}

	/// <summary>`patrulha_entrar` -- os requisitos, o cargo, o scouter de graca e a primeira missao.</summary>
	private void EntrarNaPatrulha(ServerPlayer pl)
	{
		if (MembroDaPatrulha(pl)) { Avisar(pl, "você já é da Patrulha Galáctica."); return; }
		if (PorQueNaoEntra(pl) is { Length: > 0 } nao) { Avisar(pl, nao); return; }
		FichaDaPatrulha f = FichaDe(pl);
		f.Membro = true;
		f.MembroDesde = NowMs();
		f.Missao = null;
		SalvarAPatrulha();
		Avisar(pl, "PATRULHA GALÁCTICA: bem-vindo à Patrulha. A sua missão é manter a paz na galáxia -- e a Patrulha cuida dos seus.");
		if (pl.Mochila.Guardar(CatalogoDeItens.Scouter) == 0)
		{
			MandarMochila(pl);
			Persistir(pl);
			Avisar(pl, "a Patrulha te entrega um SCOUTER: ele lê o poder de luta e é o rádio da Patrulha.");
		}
		else
		{
			PorNoChao(pl.Zone, OndeCaiOQueSeLarga(pl), TipoDeLargado.Item, CatalogoDeItens.Scouter, 1);
			Avisar(pl, "a Patrulha te entrega um SCOUTER -- mas a sua mochila está cheia, e ele caiu aos seus pés.");
		}
		AnunciarNoMundo($"{pl.Name} entrou para a Patrulha Galáctica.");
		GD.Print($"[patrulha] {pl.Name} entrou para a Patrulha");
		DarMissao(pl, f);
	}

	/// <summary>`patrulha_sair` -- deixa o cargo (e a missao).</summary>
	private void SairDaPatrulha(ServerPlayer pl)
	{
		if (FichaOuNulo(pl) is not { Membro: true } f) { Avisar(pl, "você não é da Patrulha Galáctica."); return; }
		DeixarOCargo(pl, f, "você deixa a Patrulha Galáctica. O scouter é seu.");
	}

	private void DeixarOCargo(ServerPlayer pl, FichaDaPatrulha f, string frase)
	{
		f.Membro = false;
		f.Missao = null;
		SalvarAPatrulha();
		Avisar(pl, frase);
		MandarAMissao(pl);
	}

	// =====================================================================
	// A MISSAO
	// =====================================================================
	/// <summary>
	/// UMA MISSAO NOVA: o procurado jogador cuja posicao a Patrulha conhece primeiro (o da cacada de agora, ou o denunciado/avistado ONDE
	/// esta); sem ele, um foragido NPC num planeta com povo, longe do membro.
	/// </summary>
	private void DarMissao(ServerPlayer pl, FichaDaPatrulha f)
	{
		long agora = NowMs();
		var m = new MissaoDaPatrulha { Ate = agora + (long)(PatrulhaGalactica.MinutosDaMissao * 60_000) };
		GarantirOsSoltos();
		ServerPlayer? procurado = _players.Values
			.Where(o => o != pl && EhPessoa(o) && (o.Peer != null || PatrulhaVeCorpoSemTelaDeTeste) && !o.Ficha.dead && NivelDeProcurado(o) > 0 && !PresoNaPatrulha(o)
						&& LocalizadoPelaPatrulha(o))
			.OrderByDescending(o => NivelDeProcurado(o)).FirstOrDefault();
		// (2026-10-31, o pedido 200) O VILAO POS-SAGA vem primeiro: e a ameaca maior, e so a Patrulha sabe onde ele esta -- (o pedido 206)
		// o que esta num planeta agora; o sumido nao tem onde ser cacado.
		if (_soltos.Find(s => !s.Sumido) is { } ameaca)
		{
			PorAMissaoDoVilao(m, ameaca);
		}
		else if (procurado != null)
		{
			m.Tipo = MissaoProcurado;
			m.Alvo = procurado.Assinatura;
			m.NomeDoAlvo = procurado.Name;
			m.PorZona(procurado.Zone);
		}
		else if (PlanetaDoForagido(pl.Zone) is { } z)
		{
			m.Tipo = MissaoForagido;
			m.PorZona(z);
		}
		else
		{
			Avisar(pl, "PATRULHA GALÁCTICA: nenhuma missão agora. A galáxia está calma.");
			return;
		}
		f.Missao = m;
		SalvarAPatrulha();
		Avisar(pl, TextoDaMissao(m));
		MandarAMissao(pl);
		GD.Print($"[patrulha] missao pra {pl.Name}: tipo {m.Tipo} em {m.ZonaNome}{(m.Alvo.Length > 0 ? $" ({m.NomeDoAlvo})" : "")}");
	}

	/// <summary>A Patrulha sabe onde este procurado esta agora? A cacada de agora, ou o planeta dele na memoria dela (avistado ou denunciado).</summary>
	private bool LocalizadoPelaPatrulha(ServerPlayer o) =>
		_cacadas.ContainsKey(o.Id) || FichaOuNulo(o)?.Avistado.Contains(ChaveDoAvistamento(o.Zone)) == true;

	/// <summary>O planeta do foragido NPC: um com povo (o mesmo crivo do vilao avulso), vivo, fora daquele em que o membro esta. Nulo = nenhum.</summary>
	private ZoneKey? PlanetaDoForagido(ZoneKey fora)
	{
		var candidatos = new List<ZoneKey>();
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
		{
			ZoneKey z = Espaco.ZonaDe(p);
			if (!z.Equals(fora) && RecebeVilao(z) && !candidatos.Contains(z)) candidatos.Add(z);
		}
		if (candidatos.Count == 0) return null;
		return candidatos[SorteioDaMissaoDeTeste ?? _rng.Next(candidatos.Count)];
	}

	/// <summary>A MOEDA DA BANCADA: o indice fixo do planeta do foragido. Nulo em jogo.</summary>
	internal int? SorteioDaMissaoDeTeste;

	/// <summary>A frase da missao (o verbo, o aviso da missao nova e o botao do Nav dizem a mesma).</summary>
	internal string TextoDaMissao(MissaoDaPatrulha m)
	{
		string onde = NomeDoPlaneta(m.ZonaNome);
		long falta = Math.Max(0, m.Ate - NowMs());
		string prazo = $"{falta / 60_000} min";
		if (m.Tipo == MissaoVilao)
			return $"MISSÃO DA PATRULHA: {m.NomeDoAlvo} invade {onde}. Vá até lá e derrote-o -- a Patrulha paga "
				   + $"{PatrulhaGalactica.ZeniDaMissaoDeVilao.ToString("N0", Pt)} zeni (prazo: {prazo}).";
		return m.Tipo == MissaoProcurado
			? $"MISSÃO DA PATRULHA: {m.NomeDoAlvo} é procurado e foi localizado em {onde}. Derrote-o e entregue-o à Patrulha (prazo: {prazo})."
			: $"MISSÃO DA PATRULHA: um foragido procurado pela Patrulha se esconde em {onde}. Vá até lá e derrote-o (prazo: {prazo}).";
	}

	/// <summary>`patrulha_missao` -- a missao de agora (sem missao, pega uma).</summary>
	private void VerAMissao(ServerPlayer pl)
	{
		if (FichaOuNulo(pl) is not { Membro: true } f) { Avisar(pl, "só quem é da Patrulha Galáctica recebe missões dela."); return; }
		if (f.Missao is { } m) { Avisar(pl, TextoDaMissao(m)); MandarAMissao(pl); return; }
		DarMissao(pl, f);
	}

	/// <summary>
	/// O SEGUNDO DO CARGO (de dentro do `TickDaPatrulha`): o membro que virou procurado ou perdeu o karma e expulso; a missao que venceu ou
	/// perdeu o alvo cai (e vem outra); o foragido nasce quando o membro pisa no planeta dele; e o `S2C.MissaoDaPatrulha` sai pra quem mudou.
	/// </summary>
	private void TickDoCargo(long agora)
	{
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			if ((pl.Peer == null && !PatrulhaVeCorpoSemTelaDeTeste) || !EhPessoa(pl) || FichaOuNulo(pl) is not { Membro: true } f) continue;

			if (NivelDeProcurado(pl) > 0) { DeixarOCargo(pl, f, "PATRULHA GALÁCTICA: um procurado não veste o uniforme da Patrulha -- você foi EXPULSO."); continue; }
			if (pl.Karma <= 0) { DeixarOCargo(pl, f, "PATRULHA GALÁCTICA: o seu karma sujou -- você foi EXPULSO da Patrulha."); continue; }

			if (f.Missao is not { } m) { MandarAMissao(pl); continue; }
			// (o pedido 206) a do vilao pos-saga nao vence pelo relogio: ela acaba quando ele sai do planeta (`MissaoAoSumirOSolto`) -- e a luta
			// que segura o prazo dele seguraria a missao de quem esta lutando.
			if (m.Tipo != MissaoVilao && agora >= m.Ate)
			{
				Avisar(pl, "PATRULHA GALÁCTICA: o prazo da missão acabou.");
				f.Missao = null;
				DarMissao(pl, f);
				continue;
			}
			if (m.Tipo == MissaoProcurado)
			{
				ServerPlayer? alvo = _players.Values.FirstOrDefault(o => o.Assinatura == m.Alvo && EhPessoa(o));
				if (alvo == null || (alvo.Peer == null && !PatrulhaVeCorpoSemTelaDeTeste) || NivelDeProcurado(alvo) == 0 || PresoNaPatrulha(alvo))
				{
					Avisar(pl, $"PATRULHA GALÁCTICA: a missão atrás de {m.NomeDoAlvo} acabou -- ele não está mais ao alcance da Patrulha.");
					f.Missao = null;
					DarMissao(pl, f);
					continue;
				}
				// ele mudou de planeta e a Patrulha sabe onde (a cacada, ou a memoria): a missao segue
				if (!alvo.Zone.Equals(m.Zona) && LocalizadoPelaPatrulha(alvo))
				{
					m.PorZona(alvo.Zone);
					SalvarAPatrulha();
					Avisar(pl, TextoDaMissao(m));
				}
			}
			else if (m.Tipo == MissaoVilao)
			{
				// o vilao que saiu do jogo por outra porta (derrotado por quem nao e da Patrulha ja cai no `MissaoAoDerrotarSolto`), ou que sumiu
				// enquanto este membro estava fora (o pedido 206: quem estava online ja leu o `MissaoAoSumirOSolto`)
				VilaoSolto? atras = _soltos.Find(x => x.Elo == m.Alvo);
				if (atras is null or { Sumido: true })
				{
					Avisar(pl, atras == null ? $"PATRULHA GALÁCTICA: a ameaça de {m.NomeDoAlvo} acabou -- a sua missão contra ele também."
						: TextoDoRastroPerdido(atras));
					f.Missao = null;
					DarMissao(pl, f);
					continue;
				}
			}
			else if (m.Tipo == MissaoForagido && pl.Zone.Equals(m.Zona) && (m.Npc == 0 || !_players.ContainsKey(m.Npc)))
			{
				// O FORAGIDO NASCE QUANDO O MEMBRO CHEGA -- o vilao avulso, pela porta dele (o aviso e o premio sao os dele)
				if (NascerVilao(m.Zona, agora) is { } foragido)
				{
					m.Npc = foragido.Id;
					Avisar(pl, $"PATRULHA GALÁCTICA: o foragido {foragido.Name} está aqui -- {RumoDe(pl.Pos, foragido.Pos)}. Derrote-o.");
				}
			}
			MandarAMissao(pl);
		}
	}

	/// <summary>O FORAGIDO DERROTADO PELO MEMBRO (o funil, de dentro do `PatrulhaAoDerrotar`): a missao cumprida.</summary>
	private void MissaoAoDerrotar(ServerPlayer vitima, ServerPlayer algoz)
	{
		if (FichaOuNulo(algoz) is not { Membro: true, Missao: { Tipo: MissaoForagido } m } f || m.Npc != vitima.Id) return;
		CumprirAMissao(algoz, f, $"derrotou o foragido {vitima.Name} em {NomeDoPlaneta(m.ZonaNome)}");
	}

	/// <summary>O PROCURADO ENTREGUE PELO MEMBRO (de dentro do `Prender`): a missao dele cumprida, e a dos outros membros atras do mesmo, cancelada.</summary>
	private void MissaoAoPrender(ServerPlayer preso, ServerPlayer? entregador)
	{
		foreach (ServerPlayer o in _players.Values.ToList())
		{
			if (FichaOuNulo(o) is not { Membro: true, Missao: { Tipo: MissaoProcurado } m } f || m.Alvo != preso.Assinatura) continue;
			if (o == entregador) CumprirAMissao(o, f, $"entregou {preso.Name} à Patrulha");
			else
			{
				Avisar(o, $"PATRULHA GALÁCTICA: {preso.Name} foi preso -- a sua missão atrás dele acabou.");
				f.Missao = null;
				DarMissao(o, f);
			}
		}
	}

	private void CumprirAMissao(ServerPlayer pl, FichaDaPatrulha f, string oQue, double? quanto = null, string chave = "")
	{
		f.Missao = null;
		SalvarAPatrulha();
		double zeni = quanto ?? PatrulhaGalactica.ZeniDaMissao;
		pl.Ficha.Zeni += zeni;
		MandarCarteira(pl);
		Persistir(pl);
		SomarKarma(pl, PatrulhaGalactica.KarmaDaMissao, "você serviu à Patrulha Galáctica.");
		Avisar(pl, $"PATRULHA GALÁCTICA: MISSÃO CUMPRIDA -- você {oQue}. A Patrulha te paga {zeni.ToString("N0", Pt)} zeni.");
		Noticiar(TipoDeNoticia.VilaoDerrotado, $"{pl.Name}, da Patrulha Galáctica, {oQue}.", chave);
		GD.Print($"[patrulha] {pl.Name} cumpriu a missao: {oQue}");
		DarMissao(pl, f);
	}

	// =====================================================================
	// O VILAO POS-SAGA (o pedido 200)
	// =====================================================================
	/// <summary>A missao contra este vilao solto: o planeta que ele invade agora, e o prazo dele la (os dois dias, e um minuto de folga).</summary>
	private static void PorAMissaoDoVilao(MissaoDaPatrulha m, VilaoSolto v)
	{
		m.Tipo = MissaoVilao;
		m.Alvo = v.Elo;
		m.NomeDoAlvo = v.Nome;
		m.PorZona(v.Zona);
		m.Ate = v.SaiEm + 60_000;
		m.Npc = 0;
	}

	/// <summary>
	/// O VILAO POS-SAGA INVADIU UM PLANETA (chamado de `GameServer.ViloesSoltos.cs`, quando ele fica solto e a cada planeta novo): o
	/// dono -- *"deveria aparecer apenas como missao pra player da patrulha e no jornal do dia"*. Todo membro online sem missao, atras de
	/// um foragido, ou ja atras deste vilao recebe (ou atualiza) a missao contra ele, com o planeta no fio do botao da carta. Quem esta
	/// atras de um procurado jogador continua nele.
	/// </summary>
	private void MissaoDeInvasao(VilaoSolto v)
	{
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			if ((pl.Peer == null && !PatrulhaVeCorpoSemTelaDeTeste) || !EhPessoa(pl) || FichaOuNulo(pl) is not { Membro: true } f) continue;
			if (f.Missao is { Tipo: MissaoProcurado }) continue;
			if (f.Missao is { Tipo: MissaoVilao } atual && atual.Alvo != v.Elo) continue;
			var m = f.Missao is { Tipo: MissaoVilao } dele ? dele : new MissaoDaPatrulha();
			PorAMissaoDoVilao(m, v);
			f.Missao = m;
			Avisar(pl, TextoDaMissao(m));
			MandarAMissao(pl);
		}
		SalvarAPatrulha();
	}

	/// <summary>
	/// O VILAO POS-SAGA DERROTADO (de dentro do `SoltoFoiDerrotado`): a missao de quem o derrotou e cumprida, com o zeni da missao do
	/// vilao -- e a noticia dela, com a chave da derrota, toma o lugar da do `SoltoFoiDerrotado` (uma derrota, uma linha no jornal); a dos
	/// outros membros atras dele acaba, e vem outra.
	/// </summary>
	private void MissaoAoDerrotarSolto(VilaoSolto v, ServerPlayer algoz)
	{
		string onde = NomeDoPlaneta(v.ZonaNome);
		foreach (ServerPlayer o in _players.Values.ToList())
		{
			if (FichaOuNulo(o) is not { Membro: true, Missao: { Tipo: MissaoVilao } m } f || m.Alvo != v.Elo) continue;
			if (o == algoz) CumprirAMissao(o, f, $"derrotou {v.Nome}, que invadia {onde}", PatrulhaGalactica.ZeniDaMissaoDeVilao, ChaveDaDerrotaDoSolto(v));
			else
			{
				Avisar(o, $"PATRULHA GALÁCTICA: {v.Nome} foi derrotado -- a sua missão contra ele acabou.");
				f.Missao = null;
				DarMissao(o, f);
			}
		}
	}

	/// <summary>
	/// (2026-10-31, o pedido 206) O VILAO POS-SAGA SAIU DO PLANETA sem ser vencido (de dentro do `SairDoPlaneta`): a Patrulha perde o rastro
	/// -- a missao de quem estava atras dele acaba, e vem outra. Quando ele atacar de novo, a missao volta (`MissaoDeInvasao`).
	/// </summary>
	private void MissaoAoSumirOSolto(VilaoSolto v)
	{
		foreach (ServerPlayer o in _players.Values.ToList())
		{
			if (FichaOuNulo(o) is not { Membro: true, Missao: { Tipo: MissaoVilao } m } f || m.Alvo != v.Elo) continue;
			Avisar(o, TextoDoRastroPerdido(v));
			f.Missao = null;
			DarMissao(o, f);
		}
		SalvarAPatrulha();
	}

	/// <summary>(o pedido 206) O aviso de quem perde a missao porque o vilao sumiu.</summary>
	private string TextoDoRastroPerdido(VilaoSolto v) =>
		$"PATRULHA GALÁCTICA: {v.Nome} deixou {NomeDoPlaneta(v.ZonaNome)} e a Patrulha perdeu o rastro dele -- quando ele atacar de novo, a missão volta.";

	// =====================================================================
	// O FIO DO BOTAO DO NAV
	// =====================================================================
	/// <summary>
	/// O `S2C.MissaoDaPatrulha`: `bool membro` + (membro) `bool temMissao` + (missao) `string texto` + `bool naCarta` + (na carta) `int sx`,
	/// `int sy`, `byte orbita`, `ulong semente` -- o endereco do planeta na carta, como o da casa do Bills. So sai quando muda.
	/// </summary>
	private void MandarAMissao(ServerPlayer pl)
	{
		if (pl.Peer == null) return;
		FichaDaPatrulha? f = FichaOuNulo(pl);
		bool membro = f is { Membro: true };
		MissaoDaPatrulha? m = membro ? f!.Missao : null;
		(int Sx, int Sy, int K) end = (0, 0, -1);
		ulong semente = 0;
		if (m != null && CorpoDaZona(m.Zona) is { } corpo)
		{
			end = EnderecoDoCorpo(SeedDoUniverso, corpo);
			semente = corpo.Seed;
		}
		string assinatura = $"{membro}|{m?.Tipo}|{m?.ZonaNome}|{m?.ZonaSeed}|{m?.NomeDoAlvo}|{end.K}";
		if (_missaoMandada.TryGetValue(pl.Id, out string? ja) && ja == assinatura) return;
		_missaoMandada[pl.Id] = assinatura;

		var w = Protocol.Begin(Protocol.S2C.MissaoDaPatrulha);
		w.Put(membro);
		if (membro)
		{
			w.Put(m != null);
			if (m != null)
			{
				w.Put(TextoDaMissao(m));
				w.Put(end.K >= 0);
				if (end.K >= 0)
				{
					w.Put(end.Sx);
					w.Put(end.Sy);
					w.Put((byte)end.K);
					w.Put(semente);
				}
			}
		}
		EscutaDaMissao?.Add((pl.Id, w.CopyData()));
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
