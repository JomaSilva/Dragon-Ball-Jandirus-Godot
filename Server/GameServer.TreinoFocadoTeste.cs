using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO TREINO FOCADO (<c>--treinofocadoteste</c>). Roda no boot, sem cliente: a conta pura, o
/// passe de producao do <c>Treinar</c> (o AFK em 1x e o focado a 2x, no treino e na meditacao), a letra
/// que muda, o prazo que aperta, o vigor do erro, a faixa da respiracao e a pausa do embate.
///
///     Godot --headless --path . --server --port 10150 --treinofocadoteste
/// </summary>
public partial class GameServer
{
	private int _focoOk, _focoFalhou;

	private void AfirmarFoco(string oque, bool passou, string detalhe = "")
	{
		GD.Print("[treinofocado] " + (passou ? "  ok     " : "  FALHA  ") + oque
				 + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _focoOk++;
		else _focoFalhou++;
	}

	private void RodarBancadaDoTreinoFocado()
	{
		_focoOk = _focoFalhou = 0;
		GD.Print("[treinofocado] ===== BANCADA DO TREINO FOCADO E DA MEDITACAO FOCADA =====");
		int id = 910150;
		var pl = new ServerPlayer
		{
			Id = id,
			Name = "Foco",
			Race = "Human",
			Ficha = CorpoDoFoco(),
		};
		pl.Combate = new CombatState(pl.Ficha);
		_players[id] = pl;
		try
		{
			AContaPura();
			OGanho(pl);
			ALetraEOPrazoEOVigor(pl);
			ARespiracao(pl);
			OEmbatePausa(pl);
		}
		catch (Exception e)
		{
			AfirmarFoco("o roteiro correu ate o fim sem lancar excecao", false, e.GetType().Name + ": " + e.Message);
		}
		finally
		{
			_players.Remove(id);
			_foco.Remove(id);
			_emEmbate.Remove(id);
			TreinoFocado.BonusNaoSobeDeTeste = false;
			TreinoFocado.BonusNaoMultiplicaDeTeste = false;
			TreinoFocado.AfkLevaOBonusDeTeste = false;
			TreinoFocado.PrazoNaoApertaDeTeste = false;
			TreinoFocado.LetraFixaDeTeste = false;
			TreinoFocado.ErroNaoCustaDeTeste = false;
			TreinoFocado.RespiracaoSempreDentroDeTeste = false;
			TreinoFocado.WasdAndaNoFocadoDeTeste = false;
			TreinoFocado.EspacoSocaNaRespiracaoDeTeste = false;
			TreinoFocado.RespiracaoSemPrazoDeTeste = false;
			TreinoFocado.RespiracaoSemCompensarAtrasoDeTeste = false;
			IdaEVoltaDeTeste = null;
			GD.Print($"[treinofocado] ================ {_focoOk} OK, {_focoFalhou} FALHA(S) ================");
		}
	}

	private static Fighter CorpoDoFoco() => new()
	{
		Name = "Foco",
		Race = "Human",
		BP = 5000,
		relBPmax = 1e15,
		hiddenpotential = 0,
		stamina = 100,
		maxstamina = 100,
		weight = 1,
		TrainMod = 1,
		MedMod = 1,
		Egains = 1,
		BPMod = 1,
		baseKi = 100,
		baseKiMax = 1000,
	};

	private void AContaPura()
	{
		GD.Print("[treinofocado] -- a conta --");
		double b = TreinoFocado.PisoDoBonus;
		for (int i = 0; i < 10; i++) b = TreinoFocado.Subir(b);
		AfirmarFoco("T4. dez acertos chegam no teto de 2x",
			b <= TreinoFocado.Teto && b >= TreinoFocado.Teto - 1e-6, b.ToString("0.######"));
		AfirmarFoco("T4c. o decimo primeiro acerto nao passa do teto", TreinoFocado.Subir(b) == TreinoFocado.Teto);
		AfirmarFoco("T5c. o erro derruba meio ponto e nao desce do ganho normal",
			Math.Abs(TreinoFocado.Cair(TreinoFocado.Teto) - 1.5) < 1e-9
			&& TreinoFocado.Cair(TreinoFocado.PisoDoBonus) == TreinoFocado.PisoDoBonus);

		try
		{
			TreinoFocado.BonusNaoSobeDeTeste = true;
			double parado = TreinoFocado.PisoDoBonus;
			for (int i = 0; i < 10; i++) parado = TreinoFocado.Subir(parado);
			AfirmarFoco("T4b. (defeito injetado: o acerto nao sobe o bonus) a mesma regua REPROVA",
				parado < TreinoFocado.Teto - 0.5, parado.ToString("0.######"));
		}
		finally { TreinoFocado.BonusNaoSobeDeTeste = false; }

		AfirmarFoco("T6. o prazo aperta com a sequencia e para no piso",
			TreinoFocado.PrazoDaLetra(0) == TreinoFocado.PrazoInicialMs
			&& TreinoFocado.PrazoDaLetra(20) == TreinoFocado.PisoDoPrazoMs
			&& TreinoFocado.PrazoDaLetra(20) < TreinoFocado.PrazoDaLetra(0));
		try
		{
			TreinoFocado.PrazoNaoApertaDeTeste = true;
			AfirmarFoco("T6b. (defeito injetado: o prazo nao aperta) a mesma regua REPROVA",
				TreinoFocado.PrazoDaLetra(20) == TreinoFocado.PrazoInicialMs);
		}
		finally { TreinoFocado.PrazoNaoApertaDeTeste = false; }

		AfirmarFoco("T5. o erro custa vigor", TreinoFocado.VigorDoErro(100) == 100 * TreinoFocado.FracaoDeVigorPorErro);
		try
		{
			TreinoFocado.ErroNaoCustaDeTeste = true;
			AfirmarFoco("T5b. (defeito injetado: o erro nao custa vigor) a mesma regua REPROVA",
				TreinoFocado.VigorDoErro(100) == 0);
		}
		finally { TreinoFocado.ErroNaoCustaDeTeste = false; }

		long meio = 1_000_000 + TreinoFocado.PeriodoDaRespiracaoMs / 4;
		AfirmarFoco("T8. no meio da barra o aperto acerta, e na ponta erra",
			TreinoFocado.NaFaixa(meio, 1_000_000, TreinoFocado.PeriodoDaRespiracaoMs)
			&& !TreinoFocado.NaFaixa(1_000_000, 1_000_000, TreinoFocado.PeriodoDaRespiracaoMs));
		try
		{
			TreinoFocado.RespiracaoSempreDentroDeTeste = true;
			AfirmarFoco("T8b. (defeito injetado: a faixa ocupa a barra inteira) a mesma regua REPROVA",
				TreinoFocado.NaFaixa(1_000_000, 1_000_000, TreinoFocado.PeriodoDaRespiracaoMs));
		}
		finally { TreinoFocado.RespiracaoSempreDentroDeTeste = false; }

		AfirmarFoco("T9. no treino focado o WASD nao anda", TreinoFocado.WasdNaoAnda(true) && !TreinoFocado.WasdNaoAnda(false));
		try
		{
			TreinoFocado.WasdAndaNoFocadoDeTeste = true;
			AfirmarFoco("T9b. (defeito injetado: o WASD anda no focado) a mesma regua REPROVA",
				!TreinoFocado.WasdNaoAnda(true));
		}
		finally { TreinoFocado.WasdAndaNoFocadoDeTeste = false; }

		AfirmarFoco("T10. o espaco da respiracao nao e soco",
			TreinoFocado.EspacoEhDaRespiracao(true) && !TreinoFocado.EspacoEhDaRespiracao(false));
		try
		{
			TreinoFocado.EspacoSocaNaRespiracaoDeTeste = true;
			AfirmarFoco("T10b. (defeito injetado: o espaco da respiracao soca) a mesma regua REPROVA",
				!TreinoFocado.EspacoEhDaRespiracao(true));
		}
		finally { TreinoFocado.EspacoSocaNaRespiracaoDeTeste = false; }
	}

	private double MedirPasse(ServerPlayer pl, bool treino, int vezes)
	{
		pl.Ficha.BP = 5000;
		pl.Ficha.BPBuffer = 0;
		pl.Ficha.stamina = 100;
		pl.Ficha.train = treino;
		pl.Ficha.med = !treino;
		double antes = pl.Ficha.BP;
		for (int i = 0; i < vezes; i++) Treinar(pl);
		return pl.Ficha.BP - antes;
	}

	private void OGanho(ServerPlayer pl)
	{
		GD.Print("[treinofocado] -- o ganho, pelo Treinar de producao --");
		_foco.Remove(pl.Id);
		const int vezes = 30;
		double afkTreino = MedirPasse(pl, treino: true, vezes);
		double afkMed = MedirPasse(pl, treino: false, vezes);

		pl.Ficha.train = true;
		pl.Ficha.med = false;
		ComandoDeTreinoFocado(pl, "focado", "treino");
		for (int i = 0; i < 12 && _foco[pl.Id].Bonus < TreinoFocado.Teto - 1e-6; i++)
			TeclaDoTreinoFocado(pl, _foco[pl.Id].Letra);
		AfirmarFoco("T4d. os acertos de producao chegam no teto", _foco[pl.Id].Bonus >= TreinoFocado.Teto - 1e-6,
			_foco[pl.Id].Bonus.ToString("0.######"));
		double focoTreino = MedirPasse(pl, treino: true, vezes);

		pl.Ficha.med = true;
		pl.Ficha.train = false;
		ComandoDeTreinoFocado(pl, "focado", "meditar");
		_foco[pl.Id].Bonus = TreinoFocado.Teto;
		double focoMed = MedirPasse(pl, treino: false, vezes);

		bool dobroTreino = afkTreino > 0 && Math.Abs(focoTreino / afkTreino - TreinoFocado.Teto) < 0.02;
		bool dobroMed = afkMed > 0 && Math.Abs(focoMed / afkMed - TreinoFocado.Teto) < 0.02;
		AfirmarFoco("T2. o treino focado a 2x rende o dobro do treino parado", dobroTreino,
			$"afk {afkTreino:0.####} focado {focoTreino:0.####}");
		AfirmarFoco("T2b. a meditacao focada a 2x rende o dobro da meditacao parada", dobroMed,
			$"afk {afkMed:0.####} focada {focoMed:0.####}");

		_foco.Remove(pl.Id);
		double afkDeNovo = MedirPasse(pl, treino: true, vezes);
		AfirmarFoco("T3. o treino parado nao leva o bonus do focado",
			afkTreino > 0 && Math.Abs(afkDeNovo / afkTreino - 1) < 0.02, $"de novo {afkDeNovo:0.####}");

		try
		{
			TreinoFocado.BonusNaoMultiplicaDeTeste = true;
			pl.Ficha.train = true;
			pl.Ficha.med = false;
			ComandoDeTreinoFocado(pl, "focado", "treino");
			_foco[pl.Id].Bonus = TreinoFocado.Teto;
			double sem = MedirPasse(pl, treino: true, vezes);
			AfirmarFoco("T2c. (defeito injetado: o bonus nao multiplica o ganho) a mesma regua REPROVA",
				afkTreino > 0 && sem / afkTreino < 1.2, $"sem o bonus {sem:0.####}");
		}
		finally { TreinoFocado.BonusNaoMultiplicaDeTeste = false; _foco.Remove(pl.Id); }

		try
		{
			TreinoFocado.AfkLevaOBonusDeTeste = true;
			double inflado = MedirPasse(pl, treino: true, vezes);
			AfirmarFoco("T3b. (defeito injetado: o afk leva o teto) a mesma regua REPROVA",
				afkTreino > 0 && inflado / afkTreino > 1.5, $"afk inflado {inflado:0.####}");
		}
		finally { TreinoFocado.AfkLevaOBonusDeTeste = false; }
	}

	private void ALetraEOPrazoEOVigor(ServerPlayer pl)
	{
		GD.Print("[treinofocado] -- a letra, o prazo e o vigor --");
		pl.Ficha = CorpoDoFoco();
		pl.Ficha.train = true;
		pl.Combate = new CombatState(pl.Ficha);
		ComandoDeTreinoFocado(pl, "focado", "treino");
		var vistas = new HashSet<char>();
		long prazo0 = _foco[pl.Id].Prazo - NowMs();
		for (int i = 0; i < 24; i++)
		{
			vistas.Add(_foco[pl.Id].Letra);
			TeclaDoTreinoFocado(pl, _foco[pl.Id].Letra);
		}
		AfirmarFoco("T7. a letra sorteada nao e sempre a mesma", vistas.Count >= 2, string.Join("", vistas));
		long prazoCheio = Math.Max(0, _foco[pl.Id].Prazo - NowMs());
		AfirmarFoco("T6c. depois dos acertos o prazo da proxima letra e mais curto",
			prazoCheio < prazo0 - 100, $"comeco {prazo0} agora {prazoCheio}");

		try
		{
			TreinoFocado.LetraFixaDeTeste = true;
			var fixas = new HashSet<char>();
			for (int i = 0; i < 8; i++)
			{
				TeclaDoTreinoFocado(pl, _foco[pl.Id].Letra);
				fixas.Add(_foco[pl.Id].Letra);
			}
			AfirmarFoco("T7b. (defeito injetado: a letra e sempre W) a mesma regua REPROVA",
				fixas.Count == 1 && fixas.Contains('W'), string.Join("", fixas));
		}
		finally { TreinoFocado.LetraFixaDeTeste = false; }

		pl.Ficha.stamina = 80;
		_foco[pl.Id].Bonus = TreinoFocado.Teto;
		char certa = _foco[pl.Id].Letra;
		char errada = certa == 'W' ? 'A' : 'W';
		TeclaDoTreinoFocado(pl, errada);
		AfirmarFoco("T5d. errar a letra derruba o bonus e tira vigor",
			_foco[pl.Id].Bonus < TreinoFocado.Teto - 0.2 && pl.Ficha.stamina < 80,
			$"bonus {_foco[pl.Id].Bonus:0.##} vigor {pl.Ficha.stamina:0.##}");

		_foco[pl.Id].Prazo = 0;
		double bonusAntes = _foco[pl.Id].Bonus;
		double vigorAntes = pl.Ficha.stamina;
		TicarTreinoFocado();
		AfirmarFoco("T6d. a letra que vence sem aperto e erro",
			_foco[pl.Id].Bonus < bonusAntes && pl.Ficha.stamina < vigorAntes);

		_foco.Remove(pl.Id);
	}

	private void ARespiracao(ServerPlayer pl)
	{
		GD.Print("[treinofocado] -- a respiracao --");
		pl.Ficha = CorpoDoFoco();
		pl.Ficha.med = true;
		pl.Ficha.stamina = 90;
		pl.Combate = new CombatState(pl.Ficha);
		ComandoDeTreinoFocado(pl, "focado", "meditar");
		SessaoFocada s = _foco[pl.Id];
		s.InicioResp = NowMs() - TreinoFocado.PeriodoDaRespiracaoMs / 4;
		s.TravaAte = 0;
		double bonus = s.Bonus;
		EspacoDaRespiracao(pl);
		AfirmarFoco("T8c. o espaco no meio da faixa sobe o bonus", _foco[pl.Id].Bonus > bonus,
			_foco[pl.Id].Bonus.ToString("0.##"));

		s = _foco[pl.Id];
		s.InicioResp = NowMs();
		s.TravaAte = 0;
		bonus = s.Bonus;
		double vigor = pl.Ficha.stamina;
		EspacoDaRespiracao(pl);
		AfirmarFoco("T8d. o espaco fora da faixa derruba o bonus e custa vigor",
			_foco[pl.Id].Bonus < bonus && pl.Ficha.stamina < vigor);

		// (2026-10-21, revisao) T8e. A RESPIRACAO SEM APERTO E ERRO: no teto, parado alem do prazo, o tique derruba o bonus e cobra
		// o vigor -- a mesma conta da letra que vence (T6d). Sem isto o 2x ficava pra sempre no AFK.
		bool SemApertoErra()
		{
			SessaoFocada r = _foco[pl.Id];
			r.Pausado = false;
			r.Bonus = TreinoFocado.Teto;
			r.UltimoAperto = NowMs() - TreinoFocado.RespiracaoSemApertoMs - 50;
			pl.Ficha.stamina = 90;
			TicarTreinoFocado();
			return _foco.ContainsKey(pl.Id) && _foco[pl.Id].Bonus < TreinoFocado.Teto && pl.Ficha.stamina < 90;
		}
		AfirmarFoco("T8e. a respiracao que fica sem aperto alem do prazo e erro: o bonus cai e custa vigor", SemApertoErra(),
			$"bonus {_foco[pl.Id].Bonus:0.##} vigor {pl.Ficha.stamina:0.##}");
		bool comDefeito;
		try
		{
			TreinoFocado.RespiracaoSemPrazoDeTeste = true;
			comDefeito = SemApertoErra();
		}
		finally { TreinoFocado.RespiracaoSemPrazoDeTeste = false; }
		AfirmarFoco("T8e2. (defeito injetado: a respiracao sem aperto nunca erra) a mesma regua REPROVA", !comDefeito,
			$"bonus {_foco[pl.Id].Bonus:0.##}");

		// (2026-10-21, revisao) T8f. O ATRASO DA REDE ENTRA NA CONTA: o jogador viu o marcador no meio da faixa 200 ms antes de o aperto
		// chegar (a ida e volta). Na hora da chegada o marcador ja saiu da faixa, e da folga curta tambem.
		bool ComAtrasoAcerta()
		{
			SessaoFocada r = _foco[pl.Id];
			r.Bonus = TreinoFocado.PisoDoBonus;
			r.TravaAte = 0;
			IdaEVoltaDeTeste = 200;
			try
			{
				r.InicioResp = NowMs() - 200 - TreinoFocado.PeriodoDaRespiracaoMs / 4;
				EspacoDaRespiracao(pl);
			}
			finally { IdaEVoltaDeTeste = null; }
			return _foco[pl.Id].Bonus > TreinoFocado.PisoDoBonus;
		}
		AfirmarFoco("T8f. com 200 ms de ida e volta, o aperto que o jogador viu no meio da faixa acerta", ComAtrasoAcerta(),
			$"bonus {_foco[pl.Id].Bonus:0.##}");
		bool semCompensar;
		try
		{
			TreinoFocado.RespiracaoSemCompensarAtrasoDeTeste = true;
			semCompensar = ComAtrasoAcerta();
		}
		finally { TreinoFocado.RespiracaoSemCompensarAtrasoDeTeste = false; }
		AfirmarFoco("T8f2. (defeito injetado: o julgamento usa a hora crua da chegada) a mesma regua REPROVA", !semCompensar,
			$"bonus {_foco[pl.Id].Bonus:0.##}");
		_foco.Remove(pl.Id);
	}

	private void OEmbatePausa(ServerPlayer pl)
	{
		GD.Print("[treinofocado] -- o embate pausa --");
		pl.Ficha = CorpoDoFoco();
		pl.Ficha.train = true;
		pl.Combate = new CombatState(pl.Ficha);
		ComandoDeTreinoFocado(pl, "focado", "treino");
		_foco[pl.Id].Bonus = 1.4;
		char letra = _foco[pl.Id].Letra;
		_emEmbate[pl.Id] = new Embate { A = pl, B = pl, Acaba = NowMs() + 60_000 };
		try
		{
			TicarTreinoFocado();
			TeclaDoTreinoFocado(pl, letra);
			AfirmarFoco("T11. o embate pausa o focado: a tecla nao conta e o bonus nao multiplica",
				_foco.ContainsKey(pl.Id) && _foco[pl.Id].Pausado && _foco[pl.Id].Letra == letra
				&& Math.Abs(_foco[pl.Id].Bonus - 1.4) < 1e-9
				&& Math.Abs(MultiplicadorDoTreinoFocado(pl) - TreinoFocado.PisoDoBonus) < 1e-9,
				$"letra {_foco[pl.Id].Letra} bonus {_foco[pl.Id].Bonus:0.##}");
		}
		finally { _emEmbate.Remove(pl.Id); }

		TicarTreinoFocado();
		AfirmarFoco("T11b. acabou o embate, o anel volta",
			_foco.ContainsKey(pl.Id) && !_foco[pl.Id].Pausado && _foco[pl.Id].Letra != '\0');
		_foco.Remove(pl.Id);
	}
}
