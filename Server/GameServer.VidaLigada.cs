using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ A VIDA DOS KAIOSHINS LIGADA A DO DEUS DA DESTRUICAO (2026-11-01, o pedido 216) ============================
/// O dono: *"sempre q um kaioshin player morrer o player deus da destruição recebe um aviso urgente q alguem ta matando um kaioshin, isso pq a
/// vids dos kaioshins estao ligadas ao do deus da destruição, com isso de todos os players kaioshins morrerem o deus da destruição tb morre. Se
/// n tiver kaioshin player nads acontece com o deus mesmo se matar os npcs kaioshins (eles n contam) e se n tiver player deus da destruição e
/// todos os players kaioshins morrerem o bills tb n morre por ser npc"*.
///
/// QUEM CONTA: so os PERSONAGENS sentados nas vagas do cargo `kaioshin` (as cinco) e o personagem no trono do `godofdestruction`. Os cinco
/// Supremos Kaiohs do Ceu e o Bills sao NPC e nao tem vaga -- ficam de fora sem uma linha so pra eles, e e isso que o dono pediu duas vezes.
///
/// O CAMINHO, em tres passos:
///   1. O FUNIL DA MORTE (`AMorteAconteceu`) ANOTA o Kaioshin que caiu, e so anota: ele roda de dentro dos lacos de dano, e o resto deste
///      arquivo le disco (o save de quem esta fora) e mata outro corpo.
///   2. O TIQUE DE 1 Hz (`TickDaVidaLigada`) avisa o Deus -- o cartao vermelho na tela e a linha no chat (`Fala.AvisoUrgente`) -- com quem
///      caiu e quantos Kaioshins ainda vivem. Se nenhum vive, o Deus deve a morte e paga no mesmo tique; ou, se ele estiver dentro da mente,
///      quando sair dela (a morte DENTRO da mente se desfaz sozinha -- ver `AMorteAconteceu` --, e a ligacao nao pode ser desfeita por ela).
///   3. O DEUS FORA DO JOGO recebe tudo no save (`CharacterSave.KaioshinsQueCairam` e `DeveAMorteDosKaioshins`) e le ao entrar; a morte
///      que ele devia e cobrada ali -- se nenhum Kaioshin tiver voltado a vida nesse meio tempo.
/// ===============================================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Os Kaioshins de jogador que cairam desde o ultimo tique (o nome de cada um, na ordem das mortes). Ver <see cref="KaioshinCaiu"/>.</summary>
	private readonly List<string> _kaioshinsQueCairam = [];

	/// <summary>O Deus que deve a morte e ainda nao a pagou (estava na mente): a conta e o slot do personagem. Nulo = ninguem deve.</summary>
	private (string Conta, int Slot)? _deusDevendoAMorte;

	/// <summary>Gancho de bancada: cada aviso urgente desta ligacao, com o id de quem o recebeu. Nulo fora de teste.</summary>
	internal static List<(int Para, string Texto)>? UrgentesDeTeste;

	/// <summary>Quantos nomes o save do Deus guarda enquanto ele esta fora (ficam os mais novos).</summary>
	private const int KaioshinsGuardadosNoSave = 20;

	internal const string FraseDaMorteDoDeus = "a sua vida estava ligada à dos Kaioshins: com o último deles morto, você morre também.";
	internal const string FraseDoKaioshinQueVoltou = "um Kaioshin voltou à vida antes que a ligação cobrasse a sua: você continua vivo.";

	/// <summary>O aviso urgente ao Deus, pela queda de um Kaioshin -- com quantos ainda vivem (a vida dele vai com o ultimo).</summary>
	internal static string FraseDaQueda(string nome, int vivos) => vivos switch
	{
		0 => $"URGENTE: alguém matou {nome}, o último Kaioshin vivo! A sua vida estava ligada à dos Kaioshins.",
		1 => $"URGENTE: alguém está matando os Kaioshins! {nome} acabou de morrer -- a sua vida está ligada à deles, e resta 1 Kaioshin vivo.",
		_ => $"URGENTE: alguém está matando os Kaioshins! {nome} acabou de morrer -- a sua vida está ligada à deles, e restam {vivos} Kaioshins vivos.",
	};

	// =====================================================================
	// 1. O FUNIL DA MORTE ANOTA
	// =====================================================================
	/// <summary>
	/// CHAMADO PELO `AMorteAconteceu`, pra todo corpo que morre. Anota so o Kaioshin de JOGADOR -- o corpo com conta e slot que esta sentado
	/// numa vaga do cargo -- e nao o que morre dentro da mente (aquela morte se desfaz ao sair dela, e um aviso dela seria alarme falso).
	/// </summary>
	private void KaioshinCaiu(ServerPlayer pl)
	{
		if (pl.Conta.Length == 0 || pl.Slot < 0 || NaMente(pl)) return;
		if (!string.Equals(CargoDe(pl), RitualDoKaioshin.Cargo, StringComparison.OrdinalIgnoreCase)) return;
		_kaioshinsQueCairam.Add(pl.Name);
	}

	// =====================================================================
	// 2. O TIQUE AVISA E COBRA
	// =====================================================================
	/// <summary>A 1 Hz: o aviso das quedas anotadas e a morte que o Deus deve.</summary>
	private void TickDaVidaLigada()
	{
		if (_kaioshinsQueCairam.Count > 0)
		{
			List<string> cairam = [.. _kaioshinsQueCairam];
			_kaioshinsQueCairam.Clear();
			AvisarODeusDasQuedas(cairam);
		}
		if (_deusDevendoAMorte != null) CobrarAMorteDoDeus();
	}

	/// <summary>
	/// O AVISO URGENTE, um por Kaioshin que caiu, ao Deus da Destruicao JOGADOR -- no jogo, na hora; fora dele, no save. Sem Deus jogador nao
	/// ha a quem avisar nem quem morrer: o Bills e NPC (*"se n tiver player deus da destruição ... o bills tb n morre por ser npc"*).
	/// </summary>
	private void AvisarODeusDasQuedas(List<string> cairam)
	{
		if (Cargos.Get(KitDoDeus.Cargo) is not { } r || PrimeiraVagaOcupada(r) is not { Length: > 0 } vaga) return;
		int vivos = KaioshinsVivos();
		if (OnlineDoTrono(vaga) is { } deus)
		{
			foreach (string nome in cairam) AvisarComUrgencia(deus, FraseDaQueda(nome, vivos));
			if (vivos == 0 && !deus.Ficha.dead) _deusDevendoAMorte = (deus.Conta, deus.Slot);
			return;
		}
		// FORA DO JOGO: os nomes e a divida vao pro save dele (`EntregarOQueOsKaioshinsDeixaram` le na entrada). O trono de antes, so da
		// conta e sem personagem, nao diz QUAL save -- e fica sem recado.
		if (SlotDoTrono(vaga, out int slot)) GuardarNoSaveDoDeus(_tronos[vaga], slot, cairam, deveAMorte: vivos == 0);
	}

	/// <summary>
	/// A MORTE QUE O DEUS DEVE. Paga agora, com o corpo dele fora da mente; se ele saiu do jogo devendo, a divida vai pro save; se um Kaioshin
	/// voltou a vida antes da cobranca, a ligacao o poupa.
	/// </summary>
	private void CobrarAMorteDoDeus()
	{
		if (_deusDevendoAMorte is not { } d) return;
		ServerPlayer? deus = _players.Values.FirstOrDefault(p => EhPessoa(p) && p.Slot == d.Slot
																 && string.Equals(p.Conta, d.Conta, StringComparison.OrdinalIgnoreCase));
		if (deus == null)
		{
			GuardarNoSaveDoDeus(d.Conta, d.Slot, [], deveAMorte: true);
			_deusDevendoAMorte = null;
			return;
		}
		if (deus.Ficha.dead) { _deusDevendoAMorte = null; return; }
		if (NaMente(deus)) return;   // a morte na mente se desfaz ao sair dela: a cobranca espera ele voltar ao corpo
		_deusDevendoAMorte = null;
		if (KaioshinsVivos() > 0) { AvisarComUrgencia(deus, FraseDoKaioshinQueVoltou); return; }
		MorrerPelosKaioshins(deus);
	}

	/// <summary>
	/// O DEUS MORRE COM O ULTIMO KAIOSHIN. `Morrer()` e a porta unica de qualquer morte (ver `AdminMatar`), e `ignorarSeguro: true` porque a
	/// ligacao nao e um golpe: nenhum seguro de zona, de torneio ou de golpe nao-letal a segura. A morte e das comuns -- o Enma, as esferas e a
	/// tecnica de reviver o trazem de volta.
	/// </summary>
	private void MorrerPelosKaioshins(ServerPlayer deus)
	{
		deus.Combate?.Morrer(ignorarSeguro: true);
		MandarFicha(deus);
		Persistir(deus);
		AvisarComUrgencia(deus, FraseDaMorteDoDeus);
		foreach (ServerPlayer o in ZoneList(deus.Zone.Hash))
			if (o != deus) Avisar(o, $"{deus.Name} cai sem que ninguém o toque: a vida dele estava ligada à dos Kaioshins.");
		GD.Print($"[vidaligada] {deus.Name}, o Deus da Destruicao, morreu com o ultimo Kaioshin");
	}

	// =====================================================================
	// 3. QUEM CONTA E QUEM ESTA FORA
	// =====================================================================
	/// <summary>
	/// QUANTOS KAIOSHINS DE JOGADOR VIVEM AGORA: cada vaga ocupada do cargo, pelo corpo no jogo ou, fora dele, pelo save. Na duvida -- o trono
	/// de antes, so da conta, que nao diz qual personagem; o save que nao se le -- conta VIVO: matar um Deus por engano e pior que poupa-lo.
	/// </summary>
	internal int KaioshinsVivos()
	{
		if (Cargos.Get(RitualDoKaioshin.Cargo) is not { } r) return 0;
		int vivos = 0;
		foreach (string vaga in VagasDe(r))
		{
			if (!_tronos.TryGetValue(vaga, out string? conta) || conta.Length == 0) continue;
			if (OnlineDoTrono(vaga) is { } kai) { if (!kai.Ficha.dead) vivos++; continue; }
			if (!SlotDoTrono(vaga, out int slot)) { vivos++; continue; }
			AccountSave? acc = ContaViva(conta) ?? _store?.Carregar(conta);
			CharacterSave? c = acc != null && slot < acc.Slots.Length ? acc.Slots[slot] : null;
			if (c?.Ficha is not { dead: true }) vivos++;
		}
		return vivos;
	}

	/// <summary>O DEUS FORA DO JOGO: os nomes de quem caiu e a divida vao pro save dele (a copia viva, se a conta estiver na selecao).</summary>
	private void GuardarNoSaveDoDeus(string conta, int slot, List<string> cairam, bool deveAMorte)
	{
		AccountSave? acc = ContaViva(conta) ?? _store?.Carregar(conta);
		if (acc == null || slot < 0 || slot >= acc.Slots.Length || acc.Slots[slot] is not { } c) return;
		if (cairam.Count > 0)
		{
			c.KaioshinsQueCairam ??= [];
			c.KaioshinsQueCairam.AddRange(cairam);
			if (c.KaioshinsQueCairam.Count > KaioshinsGuardadosNoSave)
				c.KaioshinsQueCairam.RemoveRange(0, c.KaioshinsQueCairam.Count - KaioshinsGuardadosNoSave);
		}
		if (deveAMorte) c.DeveAMorteDosKaioshins = true;
		_store?.Gravar(acc);
	}

	/// <summary>
	/// O DEUS QUE ENTRA (o login, com o mundo ja na tela): os Kaioshins que cairam com ele fora, num aviso urgente so, e a morte que ficou
	/// devendo -- cobrada agora, se ele ainda e o Deus e nenhum Kaioshin voltou a vida. Os dois campos morrem no proximo `Persistir`: o save
	/// novo nasce do corpo (`AccountStore.DeJogador`), e o corpo nao os carrega -- entregues uma vez, de proposito.
	/// </summary>
	private void EntregarOQueOsKaioshinsDeixaram(ServerPlayer pl, CharacterSave? c)
	{
		if (c == null) return;
		if (c.KaioshinsQueCairam is { Count: > 0 } nomes)
		{
			AvisarComUrgencia(pl, nomes.Count == 1
				? $"URGENTE: enquanto você estava fora, alguém matou o Kaioshin {nomes[0]}."
				: $"URGENTE: enquanto você estava fora, alguém matou {nomes.Count} Kaioshins: {string.Join(", ", nomes)}.");
			c.KaioshinsQueCairam = null;
		}
		if (!c.DeveAMorteDosKaioshins) return;
		c.DeveAMorteDosKaioshins = false;
		if (pl.Ficha.dead || !string.Equals(CargoDe(pl), KitDoDeus.Cargo, StringComparison.OrdinalIgnoreCase)) return;
		if (KaioshinsVivos() > 0) { AvisarComUrgencia(pl, FraseDoKaioshinQueVoltou); return; }
		MorrerPelosKaioshins(pl);
	}

	/// <summary>O aviso no tom urgente (o cartao vermelho e a linha no chat), anotado pra bancada.</summary>
	private void AvisarComUrgencia(ServerPlayer para, string texto)
	{
		UrgentesDeTeste?.Add((para.Id, texto));
		Avisar(para, texto, Protocol.Fala.AvisoUrgente);
	}
}
