using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// DE ONDE O PODER DA LUTA VEM -- o dono (2026-10-14): *"socar o ar ta dando bp, ganhar bp so seria socando seres (npcs)
/// ou players (e oq mais da, porem so ganha se o outro player atacar tb, precisa ter uma luta/troca de socos pra ganhar
/// poder de pvp, porem e o maior ganho do jogo) e objetos (e oq menos da) ou alvos de ki claro"*. Perguntado, ele fechou
/// o resto: objeto rende 0,1 do NPC; a troca rende 2x; ela vale se o outro te atacou nos ultimos 10 s (soco ou ki,
/// acertando, errando ou bloqueado); contra jogador so quem esta na troca ganha, e apanhar de NPC continua rendendo; e
/// o ki no nada nao rende -- so acertando alguem, um objeto ou os alvos do Ki Targets.
///
///   - SOCO: no ar, nada; num objeto, <see cref="GainKnobs.GanhoDeObjeto"/>; em alguem, o de sempre (gap, mestre)
///     vezes <see cref="FatorDaLuta"/> -- NPC 1, jogador em troca <see cref="GainKnobs.GanhoDaTrocaPvp"/>, jogador
///     fora da troca 0. Vale pros dois lados do golpe.
///   - KI: o ganho por acerto (`Acertar`) leva o mesmo fator; e o ganho que cada tecnica paga no gesto viaja com os
///     tiros dele e so sai quando um encosta em algo (abaixo).
///   - Os alvos do Ki Targets continuam pagando o `Train_Gain` deles no acerto (lote G12), intocados.
///
/// ============================ DIVERGENCIA DECLARADA DO DM ============================
/// No original o soco no ar treinava (`Attack_Gain()` sem alvo), cada verb de ki treinava NO DISPARO, acertando ou
/// nao, e jogador contra jogador rendia como qualquer luta. O que se ganhou: o poder sai de lutar, e nao de apertar
/// botao num canto do mapa (nem de surrar um amigo parado). O que se perdeu: sozinho e sem nada por perto ninguem
/// progride lutando -- sobram o verbo Train, a meditacao, os objetos e os alvos do Ki Targets.
/// =====================================================================================
///
/// ============================ O GANHO DO KI VIAJA COM O TIRO ============================
/// As tecnicas de ki pagam o `Blast_Gain` do DM no gesto: antes do tiro, depois, ou a cada tique do que esta
/// carregando. Elas continuam chamando no mesmo lugar, so que pelo <see cref="GanhoDeKi"/>, e o ganho vira um PACOTE
/// preso aos tiros daquele gesto: o primeiro que encostar em algo que conta o paga, com o peso do que encostou. Tiro
/// que morre no nada leva o pacote junto. A quantidade que um acerto rende e a de sempre; o que mudou e que errar
/// nao rende.
///
/// O GESTO E O QUADRO: ganho e tiro do mesmo jogador no mesmo quadro do motor caem no mesmo pacote, seja qual for a
/// ordem (a bola basica ganha antes de atirar, o Kit do Deus depois, a Barragem ganha uma vez e solta dez tiros). O
/// que carrega por varios tiques (o raio canalizado, a Death Ball, a Genkidama) diz de que tiro o ganho e. O pacote
/// que ja encostou paga na hora o que ainda chegar nele enquanto o encosto for recente (o raio que segue em cima do
/// alvo, o fim da Death Ball que acabou de bater).
///
/// O QUE NAO SOLTA TIRO paga em quem pegou: o Grilhao no alvo dele (<see cref="GanhoDeKiNoAlvo"/>); o sopro e o
/// rugido em quem foi marcado como agredido no mesmo quadro (o `MarcarAgressao` chama o <see cref="PagarOGestoSemTiro"/>).
/// Sem ninguem pego, nada.
/// ======================================================================================
/// </summary>
public partial class GameServer
{
	// ---------------------------------------------------------------- a troca de golpes

	/// <summary>Quem atacou quem, e quando: (agressor, vitima) -> ms. E a memoria da troca.</summary>
	private readonly Dictionary<(int Agressor, int Vitima), long> _ultimoAtaque = [];

	private static long JanelaDaTrocaMs => (long)(GainKnobs.SegundosDaTroca * 1000);

	/// <summary>
	/// FULANO ATACOU SICRANO: soco que saiu contra ele (acertando, aparado ou esquivado), ki que encostou nele, ou ki
	/// atirado com ele marcado. Chamado pelo `MarcarAgressao`, pelo `Acertar` e pelo `Disparar`.
	/// </summary>
	private void RegistrarAtaque(ServerPlayer agressor, ServerPlayer vitima)
	{
		if (agressor.Id == vitima.Id) return;
		long agora = NowMs();
		_ultimoAtaque[(agressor.Id, vitima.Id)] = agora;
		// So a janela importa: o que passou dela nao responde mais nada, e sai daqui pra a memoria nao crescer com toda
		// briga que o servidor ja viu.
		if (_ultimoAtaque.Count > 512)
			foreach (var velho in _ultimoAtaque.Where(kv => agora - kv.Value > JanelaDaTrocaMs).Select(kv => kv.Key).ToList())
				_ultimoAtaque.Remove(velho);
	}

	private bool AtacouHaPouco(ServerPlayer agressor, ServerPlayer vitima) =>
		_ultimoAtaque.TryGetValue((agressor.Id, vitima.Id), out long quando) && NowMs() - quando <= JanelaDaTrocaMs;

	/// <summary>
	/// QUANTO UM GOLPE ENTRE ESTES DOIS RENDE, em vezes do ganho contra um NPC, por cima do gap e do mestre -- pros dois
	/// lados do golpe. Com um NPC no meio: 1, como sempre foi (apanhar de NPC inclusive). Entre dois jogadores: a troca
	/// se o ALVO atacou o ATACANTE na janela, e zero se nao. E o *"precisa ter uma luta"*: bater em quem nao revida nao
	/// rende, e apanhar sem revidar tambem nao.
	/// </summary>
	private double FatorDaLuta(ServerPlayer atacante, ServerPlayer alvo)
	{
		// (2026-10-24) O TREINO COM O WHIS E O BILLS RENDE MUITO (o dono: *"igual no anime"*) -- os dois lados de todo golpe, enquanto a aula
		// durar. Ver `BillsEWhis.GanhoDoTreino`.
		if (!Jandirus.Core.Npc.BillsEWhis.TreinoRendeOComumDeTeste && TreinoDivinoEntre(atacante, alvo)) return Jandirus.Core.Npc.BillsEWhis.GanhoDoTreino;
		if (GainKnobs.PvpSemTrocaDeTeste || !EhJogador(atacante) || !EhJogador(alvo)) return 1;
		return AtacouHaPouco(alvo, atacante) ? GainKnobs.GanhoDaTrocaPvp : 0;
	}

	// ---------------------------------------------------------------- o ganho do ki

	/// <summary>
	/// O GANHO DE KI DE UM GESTO, preso aos tiros que ele soltou. Pago pelo primeiro encosto que conta; dali em diante o
	/// que ainda chegar nele paga na hora, enquanto o encosto for recente.
	/// </summary>
	private sealed class PacoteDeKi
	{
		/// <summary>Cada ganho que o gesto pagaria: o multiplicador, e se e de punho (`Attack_Gain`) em vez de `Blast_Gain`.</summary>
		public readonly List<(double Mult, bool DePunho)> Ganhos = [];
		public ulong Quadro;
		public long Nasceu;
		public int Tiros;
		/// <summary>O peso do encosto que pagou o pacote; zero enquanto nada que conta foi tocado.</summary>
		public double Fator;
		public long EncostouEm;
	}

	private readonly Dictionary<int, PacoteDeKi> _gestoDeKi = [];          // jogador -> o pacote do quadro de agora
	private readonly Dictionary<Projetil, PacoteDeKi> _pacoteDoTiro = [];  // tiro -> o pacote que ele carrega

	/// <summary>Quanto tempo um encosto continua "em cima" pra pagar o que ainda chega no pacote dele.</summary>
	private const long EncostoRecenteMs = 1500;

	/// <summary>
	/// O `Blast_Gain` (ou o `Attack_Gain`, com <paramref name="dePunho"/>) que uma tecnica de ki paga no gesto, `vezes`
	/// vezes. Vai pro pacote dos tiros deste quadro -- ou pro do <paramref name="tiro"/>, no que carrega por tiques.
	/// </summary>
	private void GanhoDeKi(ServerPlayer pl, int vezes = 1, double mult = 1, Projetil? tiro = null, bool dePunho = false)
	{
		if (GainKnobs.KiRendeNoDisparoDeTeste)
		{
			for (int i = 0; i < vezes; i++) PagarUm(pl, mult, dePunho);
			return;
		}
		PacoteDeKi pacote = tiro != null ? PacoteDoTiro(tiro) : PacoteDoGesto(pl);
		for (int i = 0; i < vezes; i++) pacote.Ganhos.Add((mult, dePunho));
		if (pacote.Fator > 0 && NowMs() - pacote.EncostouEm <= EncostoRecenteMs) Pagar(pacote, pl);
	}

	/// <summary>O GESTO DE KI QUE PEGA O ALVO DIRETO (o Grilhao): conta como ataque nele e paga na hora, com o peso da luta.</summary>
	private void GanhoDeKiNoAlvo(ServerPlayer pl, ServerPlayer alvo, int vezes)
	{
		RegistrarAtaque(pl, alvo);
		double fator = GainKnobs.KiRendeNoDisparoDeTeste ? 1 : FatorDaLuta(pl, alvo);
		if (fator <= 0) return;
		for (int i = 0; i < vezes; i++) PagarUm(pl, fator, dePunho: false);
	}

	/// <summary>
	/// O TIRO QUE ACABOU DE NASCER leva o pacote do gesto. Chamado pelo `Disparar` -- e atirar com alguem marcado e atacar
	/// essa pessoa, acerte ou nao: conta pra troca. (A bola ainda em formacao e o alvo de treino, `Inerte`, nao atacam.)
	/// </summary>
	private void PrenderOGanhoNoTiro(ServerPlayer pl, Projetil p)
	{
		PacoteDeKi pacote = PacoteDoGesto(pl);
		pacote.Tiros++;
		_pacoteDoTiro[p] = pacote;
		if (!p.Inerte && Marcado(pl) is { } mira) RegistrarAtaque(pl, mira);

		// O pacote de um tiro morto so serve no instante em que ele morre (o fim da Death Ball chega logo depois do
		// impacto); um minuto depois ja nao paga nada, e sai daqui. Meia hora depois sai de qualquer jeito: nenhum tiro
		// vive tanto, e o que sumiu de uma lista sem passar pelo `Matar` continuaria "vivo" aqui pra sempre.
		if (_pacoteDoTiro.Count > 256)
		{
			long agora = NowMs();
			foreach (Projetil velho in _pacoteDoTiro
						 .Where(kv => agora - kv.Value.Nasceu > (kv.Key.Vivo ? 1_800_000 : 60_000)).Select(kv => kv.Key).ToList())
				_pacoteDoTiro.Remove(velho);
		}
	}

	/// <summary>
	/// UM TIRO ENCOSTOU EM ALGO que rende `fator`: um corpo (<see cref="FatorDaLuta"/>), uma parede ou o chao de um
	/// planeta (<see cref="GainKnobs.GanhoDeObjeto"/>). Paga o pacote dele. O jogador fora da troca (fator 0) nao paga
	/// nem gasta o pacote: ele espera outro tiro do gesto encostar em algo que conte.
	/// </summary>
	private void AoTiroEncostar(Projetil p, ServerPlayer? dono, double fator)
	{
		if (dono == null || fator <= 0 || !_pacoteDoTiro.TryGetValue(p, out PacoteDeKi? pacote)) return;
		// O PRIMEIRO ENCOSTO DA O PESO, e o pacote sai uma vez so: os outros tiros do gesto (a Barragem que acerta dez
		// vezes) ja pagam o acerto deles no `Acertar`, e nao pagam o gesto de novo.
		if (pacote.Fator <= 0) pacote.Fator = fator;
		pacote.EncostouEm = NowMs();
		Pagar(pacote, dono);
	}

	/// <summary>
	/// O GESTO DE KI SEM TIRO PEGOU ALGUEM (o sopro, o rugido): o pacote dele -- deste quadro, sem tiro nenhum -- paga com
	/// o peso de quem foi pego. Chamado pelo `MarcarAgressao`.
	/// </summary>
	private void PagarOGestoSemTiro(ServerPlayer agressor, ServerPlayer vitima)
	{
		if (!_gestoDeKi.TryGetValue(agressor.Id, out PacoteDeKi? pacote) || pacote.Tiros > 0
			|| pacote.Quadro != Engine.GetProcessFrames() || pacote.Ganhos.Count == 0)
			return;
		double fator = FatorDaLuta(agressor, vitima);
		if (fator <= 0) return;
		if (pacote.Fator <= 0) pacote.Fator = fator;
		pacote.EncostouEm = NowMs();
		Pagar(pacote, agressor);
	}

	/// <summary>O pacote do gesto de agora: o mesmo pra todo ganho e todo tiro deste jogador neste quadro do motor.</summary>
	private PacoteDeKi PacoteDoGesto(ServerPlayer pl)
	{
		ulong quadro = Engine.GetProcessFrames();
		if (_gestoDeKi.TryGetValue(pl.Id, out PacoteDeKi? deste) && deste.Quadro == quadro) return deste;
		var novo = new PacoteDeKi { Quadro = quadro, Nasceu = NowMs() };
		// (o de um quadro que passou nao serve pra mais nada aqui: os tiros que o levam ja o tem)
		if (_gestoDeKi.Count > 256)
			foreach (int velho in _gestoDeKi.Where(kv => kv.Value.Quadro != quadro).Select(kv => kv.Key).ToList())
				_gestoDeKi.Remove(velho);
		_gestoDeKi[pl.Id] = novo;
		return novo;
	}

	/// <summary>O pacote que o tiro carrega; o tiro que nasceu sem passar pelo `Disparar` ganha um agora.</summary>
	private PacoteDeKi PacoteDoTiro(Projetil tiro)
	{
		if (!_pacoteDoTiro.TryGetValue(tiro, out PacoteDeKi? pacote))
			_pacoteDoTiro[tiro] = pacote = new PacoteDeKi { Quadro = Engine.GetProcessFrames(), Nasceu = NowMs(), Tiros = 1 };
		return pacote;
	}

	private void Pagar(PacoteDeKi pacote, ServerPlayer dono)
	{
		foreach ((double mult, bool dePunho) in pacote.Ganhos) PagarUm(dono, mult * pacote.Fator, dePunho);
		pacote.Ganhos.Clear();
	}

	private void PagarUm(ServerPlayer pl, double mult, bool dePunho)
	{
		if (dePunho) pl.Ficha.AttackGain(_rng, mult);
		else pl.Ficha.BlastGain(_rng, mult);
	}
}
