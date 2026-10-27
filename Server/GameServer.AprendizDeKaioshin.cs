using System.Text.Json;
using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;

namespace Jandirus.Server;

/// <summary>
/// O APRENDIZ DE KAIOSHIN (2026-10-19, pedido do dono) -- o lado do servidor. A regra (os numeros, o kit, as portas e o motivo de o
/// vinculo cair) mora em `Core/Ranks/AprendizDeKaioshin.cs`; aqui ficam:
///   * os VINCULOS (assinatura do aprendiz -> a conta do mestre), em disco (`aprendizes.json`), com o que cada um comprou COMO
///     aprendiz -- e o reembolso da queda precisa disso, porque o livro de skills nao sabe de que arvore veio cada compra;
///   * os verbos do Kaioshin (`aprendiz_nomear`, `aprendiz_dispensar` -- os dois verbos da skill `Kaioshin Apprenticeship`, pelo canal
///     de verbos, como o `Appoint_Elder`) e a resposta do convidado (`aprendiz_aceitar`/`aprendiz_recusar`, o cartao de pedido);
///   * o TIQUE (1 Hz, no `TickDosCargos`): o vinculo que caiu (mestre fora do trono, aprendiz com cargo proprio) desfaz o
///     aprendizado de quem esta no jogo -- e espera quem esta fora (`ksap_login_check`);
///   * a arvore abre pela regra do port (`AcendedoresDoPort.ComMestreKaioshin`, respondida no `ContadorDoServidor`) e o kit nao e
///     apagado pela reconciliacao dos cargos (`ReconciliarDadiva`).
/// </summary>
public partial class GameServer
{
	/// <summary>Um vinculo. Campos publicos, como o resto do que vai pro disco (`IncludeFields`).</summary>
	private sealed class VinculoDeAprendiz
	{
		public string ContaDoMestre = "";
		public string NomeDoMestre = "";
		public string ContaDoAprendiz = "";
		public string NomeDoAprendiz = "";

		/// <summary>As skills do kit compradas PELO aprendizado -- as que a queda esquece e reembolsa.</summary>
		public List<string> Compradas = [];

		/// <summary>O motivo da queda, esperando o aprendiz entrar no jogo pra ser aplicada. Vazio = o vinculo esta de pe.</summary>
		public string Caiu = "";
	}

	/// <summary>Assinatura do aprendiz -> o vinculo (`ksap_list`).</summary>
	private readonly Dictionary<string, VinculoDeAprendiz> _aprendizes = new(StringComparer.Ordinal);

	/// <summary>Conta do convidado -> quem convidou e ate quando. Na memoria, como todo convite: o servidor que reinicia nao espera.</summary>
	private readonly Dictionary<string, (string ContaDoMestre, string NomeDoMestre, long Ate)> _ofertasDeAprendiz =
		new(StringComparer.OrdinalIgnoreCase);

	private const string ArquivoDosAprendizes = "aprendizes.json";

	private string CaminhoDosAprendizes => System.IO.Path.Combine(_store?.Pasta ?? ".", ArquivoDosAprendizes);

	/// <summary>Le os vinculos do disco. Chamado junto com os cargos (`CarregarCargos`), no boot.</summary>
	private void CarregarAprendizes()
	{
		_aprendizes.Clear();
		try
		{
			if (System.IO.File.Exists(CaminhoDosAprendizes)
				&& JsonSerializer.Deserialize<Dictionary<string, VinculoDeAprendiz>>(System.IO.File.ReadAllText(CaminhoDosAprendizes),
					new JsonSerializerOptions { IncludeFields = true }) is { } lidos)
				foreach ((string sig, VinculoDeAprendiz v) in lidos) _aprendizes[sig] = v;
		}
		catch (Exception e) { GD.PushWarning($"[server] {ArquivoDosAprendizes} ilegivel: {e.Message}"); }
	}

	private void SalvarAprendizes()
	{
		if (_store == null) return;
		try
		{
			// TEMPORARIO E RENOMEIA, como o `cargos-ausencia.json`.
			string tmp = CaminhoDosAprendizes + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(_aprendizes, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDosAprendizes, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra salvar {ArquivoDosAprendizes}: {e.Message}"); }
	}

	// =====================================================================
	// AS PERGUNTAS
	// =====================================================================

	/// <summary>
	/// (2026-10-26) A CONTA tem um personagem Kaioshin. O mestre do vinculo e guardado pela conta, e a mesma conta nao tem dois Kaioshins
	/// (o cargo nao se repete na conta), entao "a conta do mestre e Kaioshin" e "o mestre e Kaioshin" sao a mesma pergunta -- com ele no
	/// jogo ou fora, e com o irmao de conta jogando.
	/// </summary>
	private bool EKaioshin(string conta) => ContaTemOCargo(conta, AprendizDeKaioshin.CargoDoMestre);

	/// <summary>(2026-10-26) ESTE PERSONAGEM e Kaioshin -- quem nomeia e quem dispensa e o personagem no jogo, e nao o irmao de conta.</summary>
	private bool EKaioshin(ServerPlayer pl) =>
		string.Equals(CargoDe(pl), AprendizDeKaioshin.CargoDoMestre, StringComparison.OrdinalIgnoreCase);

	/// <param name="sig">(2026-10-26) A assinatura do APRENDIZ (a chave do `_aprendizes`): o cargo que derruba o vinculo e o DELE, e nao o
	/// do irmao de conta que estiver jogando.</param>
	private string? MotivoDaQuedaDe(string sig, VinculoDeAprendiz v) =>
		AprendizDeKaioshin.MotivoDaQueda(EKaioshin(v.ContaDoMestre),
			Cargos.Get(AprendizDeKaioshin.CargoDoMestre) is { } k && DonosDe(k).Count > 0, CargoDaAssinatura(v.ContaDoAprendiz, sig).Length > 0);

	/// <summary>O vinculo DE PE deste corpo -- ou nulo (sem vinculo, ou caido).</summary>
	private VinculoDeAprendiz? VinculoDePe(ServerPlayer pl) =>
		pl.Assinatura.Length > 0 && _aprendizes.TryGetValue(pl.Assinatura, out VinculoDeAprendiz? v) && v.Caiu.Length == 0
		&& MotivoDaQuedaDe(pl.Assinatura, v) == null ? v : null;

	/// <summary>Este corpo e Aprendiz de Kaioshin agora? (A arvore, o kit e o titulo perguntam isto.)</summary>
	private bool EAprendizDeKaioshin(ServerPlayer pl) => VinculoDePe(pl) != null;

	/// <summary>Quantos aprendizes este Kaioshin guia agora (`ksap_count`) -- os de pe, online ou nao.</summary>
	private int QuantosGuia(string contaDoMestre) =>
		_aprendizes.Values.Count(v => v.Caiu.Length == 0 && string.Equals(v.ContaDoMestre, contaDoMestre, StringComparison.OrdinalIgnoreCase));

	/// <summary>O titulo de quem nao carrega cargo -- o que a aba People mostra no lugar dele. Vazio = nenhum.</summary>
	private string TituloSemCargo(ServerPlayer pl) => EAprendizDeKaioshin(pl) ? AprendizDeKaioshin.Titulo : "";

	// =====================================================================
	// OS VERBOS DO KAIOSHIN
	// =====================================================================

	/// <summary>
	/// "NOMEAR APRENDIZ" (`mob/Rank/verb/Nomear_Aprendiz`, `KaioshinApprentice.dm:315-343`). Com alguem marcado, a oferta vai pra ele;
	/// sem, a lista de quem por perto pode ser aprendiz sobe pra aba Other (o `input()` do DM), e o botao de cada um volta pra ca.
	/// </summary>
	private void VerboNomearAprendiz(ServerPlayer pl, string arg)
	{
		if (pl.Ficha.dead || pl.Ficha.KO) { Avisar(pl, "não dá: você precisa estar vivo e de pé."); return; }
		if (AprendizDeKaioshin.PorQueNaoNomeia(EKaioshin(pl), QuantosGuia(pl.Conta), true, true, false, false) is { } geral)
		{
			Avisar(pl, $"não dá: {geral}.");
			return;
		}

		if (arg.Trim().Length == 0)
		{
			List<string> candidatos = [];
			foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			{
				if (o == pl || !EhJogador(o) || AlvoAoAlcance(pl, o.Id.ToString(), AprendizDeKaioshin.TilesParaNomear, out _) == null) continue;
				if (PorQueNaoNomeiaEste(pl, o) == null) candidatos.Add($"{o.Id}|{o.Name}");
			}
			MandarEscolhas(pl, AprendizDeKaioshin.VerboNomear, candidatos);
			Avisar(pl, candidatos.Count == 0
				? "ninguém por perto pode ser seu aprendiz (precisa ser um jogador vivo, sem cargo próprio e ainda sem mestre, a até "
				  + $"{AprendizDeKaioshin.TilesParaNomear} tiles)."
				: "escolha o discípulo na aba Other (ou marque quem você quer e aperte de novo).");
			return;
		}

		ServerPlayer? alvo = AlvoAoAlcance(pl, arg.Trim(), AprendizDeKaioshin.TilesParaNomear, out ServerPlayer? longe);
		if (longe != null) { Avisar(pl, FraseDeLonge(longe)); return; }
		if (alvo == null) { Avisar(pl, "marque quem você quer tomar como aprendiz (duplo clique nele)."); return; }
		if (PorQueNaoNomeiaEste(pl, alvo) is { } nao) { Avisar(pl, $"não dá: {nao}."); return; }

		_ofertasDeAprendiz[alvo.Conta] = (pl.Conta, pl.Name, NowMs() + AprendizDeKaioshin.MsDaOferta);
		MandarEscolhas(pl, AprendizDeKaioshin.VerboNomear, []);
		Avisar(pl, $"você se oferece para tomar {alvo.Name} como Aprendiz de Kaioshin.");
		Avisar(alvo, $"{pl.Name}, o Kaioshin, se oferece para te tomar como APRENDIZ DE KAIOSHIN: você poderá aprender a Técnica de Cura e "
				   + "outros segredos dos deuses -- mas o vínculo termina se o seu mestre deixar o trono. (aceite ou recuse no cartão)");
	}

	private string? PorQueNaoNomeiaEste(ServerPlayer mestre, ServerPlayer alvo) =>
		AprendizDeKaioshin.PorQueNaoNomeia(EKaioshin(mestre), QuantosGuia(mestre.Conta), EhJogador(alvo) && alvo.Assinatura.Length > 0,
			!alvo.Ficha.dead && !alvo.Ficha.KO, CargoDe(alvo).Length > 0, _aprendizes.ContainsKey(alvo.Assinatura));

	/// <summary>
	/// A RESPOSTA DO CONVIDADO -- o `alert()` do DM (`:337-339`), que aqui e o cartao de pedido. TUDO e conferido de novo
	/// (`:340-343`): o mestre pode ter perdido o trono, o convidado pode ter tomado um cargo, a vaga pode ter enchido, alguem pode ter
	/// se afastado.
	/// </summary>
	private void VerboResponderAprendizado(ServerPlayer pl, bool aceitou)
	{
		if (!_ofertasDeAprendiz.Remove(pl.Conta, out (string ContaDoMestre, string NomeDoMestre, long Ate) oferta) || oferta.Ate < NowMs())
		{
			Avisar(pl, "ninguém te ofereceu aprendizado de Kaioshin.");
			return;
		}
		ServerPlayer? mestre = OnlinePorConta(oferta.ContaDoMestre);
		if (!aceitou)
		{
			Avisar(pl, "você recusa o aprendizado.");
			if (mestre != null) Avisar(mestre, $"{pl.Name} recusou o aprendizado.");
			return;
		}
		if (mestre == null) { Avisar(pl, $"o aprendizado se fechou: {oferta.NomeDoMestre} não está mais no jogo."); return; }
		if (PorQueNaoNomeiaEste(mestre, pl) is { } nao) { Avisar(pl, $"o aprendizado se fechou: {nao}."); return; }
		if (AlvoAoAlcance(mestre, pl.Id.ToString(), AprendizDeKaioshin.TilesParaNomear, out _) == null)
		{
			Avisar(pl, $"o aprendizado se fechou: {mestre.Name} está longe demais.");
			return;
		}

		VincularAoKaioshin(pl, mestre);
		AvisarPertoG3(mestre, RaioDaVista, $"{mestre.Name} toma {pl.Name} como Aprendiz de Kaioshin.");
		Avisar(pl, "Você é agora um Aprendiz de Kaioshin. A árvore \"Aprendiz de Kaioshin\" se abriu na aba Learning -- gaste Marcos "
				 + "nela para aprender o que seu mestre permite.");
	}

	/// <summary>Amarra o vinculo (`ksap_grant`) e abre a arvore na hora (o livro recalcula com a regra do port).</summary>
	private void VincularAoKaioshin(ServerPlayer aprendiz, ServerPlayer mestre)
	{
		_aprendizes[aprendiz.Assinatura] = new VinculoDeAprendiz
		{
			ContaDoMestre = mestre.Conta, NomeDoMestre = mestre.Name, ContaDoAprendiz = aprendiz.Conta, NomeDoAprendiz = aprendiz.Name,
		};
		SalvarAprendizes();
		AplicarEfeitos(aprendiz);
		MandarSkills(aprendiz, forcar: true);
		GD.Print($"[server] {mestre.Name} ({mestre.Conta}) tomou {aprendiz.Name} ({aprendiz.Conta}) como Aprendiz de Kaioshin");
	}

	/// <summary>
	/// "DISPENSAR APRENDIZ" (`mob/Rank/verb/Dispensar_Aprendiz`, `:347-372`). Sem argumento, a lista dos aprendizes dele -- online ou
	/// nao -- sobe pra aba Other; com a assinatura de um, ele e dispensado (o kit sai e os Marcos voltam).
	/// </summary>
	private void VerboDispensarAprendiz(ServerPlayer pl, string arg)
	{
		if (!EKaioshin(pl)) { Avisar(pl, "não dá: só o Kaioshin no trono guia discípulos."); return; }
		List<(string Sig, VinculoDeAprendiz V)> meus = [.. _aprendizes
			.Where(kv => kv.Value.Caiu.Length == 0 && string.Equals(kv.Value.ContaDoMestre, pl.Conta, StringComparison.OrdinalIgnoreCase))
			.Select(kv => (kv.Key, kv.Value))];
		if (meus.Count == 0) { Avisar(pl, "você não guia nenhum aprendiz."); return; }

		string sig = arg.Trim();
		if (sig.Length == 0)
		{
			MandarEscolhas(pl, AprendizDeKaioshin.VerboDispensar, meus.Select(m => $"{m.Sig}|{m.V.NomeDoAprendiz}"));
			Avisar(pl, "escolha na aba Other quem dispensar (ele perde o kit e recebe os Marcos de volta).");
			return;
		}
		if (meus.Find(m => m.Sig == sig) is not { V: { } v }) { Avisar(pl, "esse não é seu aprendiz."); return; }
		DerrubarOVinculo(sig, v, "seu mestre o dispensou");
		MandarEscolhas(pl, AprendizDeKaioshin.VerboDispensar, []);
		Avisar(pl, $"você dispensou {v.NomeDoAprendiz}.");
	}

	// =====================================================================
	// A QUEDA E A DEVOLUCAO
	// =====================================================================

	/// <summary>Derruba o vinculo (`ksap_revoke`): no jogo, o aprendizado se desfaz agora; fora, quando ele voltar.</summary>
	private void DerrubarOVinculo(string sig, VinculoDeAprendiz v, string motivo)
	{
		v.Caiu = motivo;
		if (JogadorDaAssinatura(sig) is { } aprendiz)
		{
			DesfazerOAprendizado(aprendiz, v);
			_aprendizes.Remove(sig);
		}
		SalvarAprendizes();
	}

	/// <summary>
	/// O `ksap_strip` (`:111-155`): o que foi comprado PELO aprendizado sai do livro e os Marcos VOLTAM -- ninguem perde investimento.
	/// O que ele tinha por outro caminho (a arvore da raca, uma licao) fica.
	/// </summary>
	private void DesfazerOAprendizado(ServerPlayer aprendiz, VinculoDeAprendiz v)
	{
		int antes = aprendiz.Livro.MarcosLivres;
		if (!AprendizDeKaioshin.SemDevolucaoDeTeste && _skills != null)
			foreach (string path in v.Compradas)
				if (aprendiz.Livro.Sabe(path) && aprendiz.Livro.FoiComprada(path) && !aprendiz.Livro.FoiEnsinada(path))
					aprendiz.Livro.EsquecerEReembolsar(_skills, path, RacaComSangue(aprendiz), aprendiz.Class);
		int devolvidos = aprendiz.Livro.MarcosLivres - antes;
		AplicarEfeitos(aprendiz);
		AplicarPoderes(aprendiz);
		MandarSkills(aprendiz, forcar: true);
		Avisar(aprendiz, $"Seu aprendizado com o Kaioshin chegou ao fim: {v.Caiu}."
						 + (devolvidos > 0 ? $" Os {devolvidos} Marcos investidos voltaram para você." : ""));
		GD.Print($"[server] o aprendizado de {aprendiz.Name} acabou ({v.Caiu}); {devolvidos} marco(s) de volta");
	}

	/// <summary>
	/// A QUEDA APLICADA A QUEM ESTA NO JOGO -- chamada do `ReconciliarDadiva`, ANTES de ele mexer no kit. Tem de ser la: o trono muda por
	/// muitas portas (reivindicar, outorgar, entronizar, destronar) e todas reconciliam o kit na hora -- a reconciliacao veria o vinculo
	/// ja caido e apagaria a Cura comprada SEM devolver os Marcos, antes de qualquer tique do aprendiz.
	/// </summary>
	private void AplicarAQuedaDoAprendiz(ServerPlayer pl)
	{
		if (_aprendizes.Count == 0 || pl.Assinatura.Length == 0 || !_aprendizes.TryGetValue(pl.Assinatura, out VinculoDeAprendiz? v)) return;
		if (v.Caiu.Length == 0 && MotivoDaQuedaDe(pl.Assinatura, v) is { } motivo) v.Caiu = motivo;
		if (v.Caiu.Length == 0 || !EhJogador(pl)) return;
		DesfazerOAprendizado(pl, v);
		_aprendizes.Remove(pl.Assinatura);
		SalvarAprendizes();
	}

	/// <summary>
	/// O TIQUE (1 Hz, no `TickDosCargos`): o vinculo que caiu enquanto o aprendiz esta FORA do jogo fica marcado (o `ksap_prune`: a vaga
	/// do mestre se solta na hora) e o aprendizado se desfaz quando ele voltar (`ksap_login_check` -- pelo `AplicarAQuedaDoAprendiz`); e o
	/// convite vencido sai da mesa.
	/// </summary>
	private void TickDosAprendizes()
	{
		if (_aprendizes.Count > 0)
		{
			bool mexeu = false;
			foreach ((string sig, VinculoDeAprendiz v) in _aprendizes)
				if (v.Caiu.Length == 0 && MotivoDaQuedaDe(sig, v) is { } motivo) { v.Caiu = motivo; mexeu = true; }
			if (mexeu) SalvarAprendizes();
		}
		if (_ofertasDeAprendiz.Count > 0)
		{
			long agora = NowMs();
			foreach (string conta in _ofertasDeAprendiz.Where(kv => kv.Value.Ate < agora).Select(kv => kv.Key).ToList())
				_ofertasDeAprendiz.Remove(conta);
		}
	}

	/// <summary>
	/// A COMPRA FEITA PELO APRENDIZADO (chamada do `Aprender`, depois da compra): so a skill do kit, de quem tem o vinculo de pe, e que
	/// nenhuma OUTRA arvore dele oferece -- a Telepatia de quem e da raca Kai vem da arvore da raca, e fica com ele quando o vinculo cai.
	/// </summary>
	private void AnotarACompraDoAprendiz(ServerPlayer pl, string path)
	{
		if (_skills == null || !AprendizDeKaioshin.Kit.Contains(path, StringComparer.OrdinalIgnoreCase) || VinculoDePe(pl) is not { } v) return;
		foreach (EstadoDeArvore e in pl.Livro.Arvores)
			if (!string.Equals(e.Path, AprendizDeKaioshin.Arvore, StringComparison.OrdinalIgnoreCase)
				&& _skills.Get(e.Path)?.Galhos.Contains(path, StringComparer.OrdinalIgnoreCase) == true)
				return;
		if (v.Compradas.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
		v.Compradas.Add(path);
		SalvarAprendizes();
	}

	/// <summary>
	/// A VAGA PERDEU (OU GANHOU) UM PERSONAGEM (`EsquecerAAssinaturaNoMundo`): o vinculo era DELE, e a assinatura se reusa -- quem
	/// nascer na vaga nao herda um mestre que nunca o escolheu.
	/// </summary>
	private void EsquecerOAprendiz(string sig)
	{
		if (_aprendizes.Remove(sig)) SalvarAprendizes();
	}

	// =====================================================================
	// O ADMIN (`Rewards.dm:302-305`: "Kaioshin Apprentice" e "(revogar)")
	// =====================================================================

	/// <summary>Da o aprendizado a quem esta marcado, amarrado ao Kaioshin da primeira vaga.</summary>
	private void AdminDarAprendiz(ServerPlayer adm, ServerPlayer p)
	{
		List<string> kaioshins = Cargos.Get(AprendizDeKaioshin.CargoDoMestre) is { } k ? DonosDe(k) : [];
		if (kaioshins.Count == 0)
		{
			Avisar(adm, "não há Kaioshin no trono -- dê o cargo kaioshin a alguém antes de nomear aprendizes.");
			return;
		}
		if (OnlineDoTrono(PrimeiraVagaOcupada(Cargos.Get(AprendizDeKaioshin.CargoDoMestre)!)) is not { } mestre)   // (2026-10-26) o personagem
		{
			Avisar(adm, "o Kaioshin da primeira vaga precisa estar no jogo pra amarrar o vínculo.");
			return;
		}
		if (PorQueNaoNomeiaEste(mestre, p) is { } nao) { Avisar(adm, $"não dá: {nao}."); return; }
		VincularAoKaioshin(p, mestre);
		Registrar(adm, $"fez {p.Name} ({p.Conta}) Aprendiz de Kaioshin de {mestre.Name}");
		Avisar(adm, $"{p.Name} agora é Aprendiz de Kaioshin de {mestre.Name}.");
		Avisar(p, $"por decisão de um administrador, você é Aprendiz de Kaioshin de {mestre.Name}.");
	}

	/// <summary>Tira o aprendizado de quem esta marcado.</summary>
	private void AdminTirarAprendiz(ServerPlayer adm, ServerPlayer? p)
	{
		if (p == null || !_aprendizes.TryGetValue(p.Assinatura, out VinculoDeAprendiz? v)) { Avisar(adm, "marque um Aprendiz de Kaioshin."); return; }
		DerrubarOVinculo(p.Assinatura, v, "decisão administrativa");
		Registrar(adm, $"tirou o aprendizado de {p.Name} ({p.Conta})");
		Avisar(adm, $"{p.Name} não é mais Aprendiz de Kaioshin.");
	}
}
