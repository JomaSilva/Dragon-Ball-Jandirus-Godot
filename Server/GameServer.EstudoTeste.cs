using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--estudoteste` (2026-10-27) -- o pedido 120 do dono: os minijogos de estudo da Research Station. Roda no BOOT, com um corpo de
/// PESSOA forjado ao lado de uma mesa aparafusada, e mede pelas portas de producao -- o `TickDoEstudo` (1 Hz), o canal de tecnologia
/// (`ComandoDeTech`, `estudo_resposta`) e as tabelas do Core:
///   E1  um minuto de estudo NA MESA abre um minijogo (59 s nao); estudando meditando, sem mesa, nunca abre;
///   E2  as contas: so soma, multiplicacao e divisao, toda divisao exata, resultado inteiro de cabeca -- e quase nunca repetem;
///   E3  os 118 elementos, simbolos e nomes unicos; o baralho pergunta todos antes de repetir;
///   E4  o ACERTO: +25% no ganho de tech por acerto valendo, ate +150% (o setimo renova o mais antigo), e o bonus vence em 10 min;
///   E5  o ERRO tira o bonus mais antigo;
///   E6  a MOLECULA vale pelas ligacoes certas (a dupla e a tripla contam), tudo ligado; e toda molecula da tabela fecha a conta;
///   E7  o TEMPO esgotado fecha o minijogo e nao muda o bonus;
///   E8  o bonus vale no estudo da mesa E no estudo meditando.
///
///     Godot --headless --path . --server --port 11761 --estudoteste
/// </summary>
public sealed partial class GameServer
{
	private int _etOk, _etFalhou;
	private const int IdDoEstudoDeTeste = 93_800;

	private void AfirmarEt(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _etOk++; GD.Print($"[estudo]   OK    {oque}   [{detalhe}]"); return; }
		_etFalhou++;
		GD.PrintErr($"[estudo]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDoEstudo()
	{
		_etOk = _etFalhou = 0;
		GD.Print("[estudo] ================ OS MINIJOGOS DE ESTUDO (dono, 2026-10-27) ================");

		ZoneKey zona = ZoneKey.Premade("Earth");
		var pl = new ServerPlayer
		{
			Id = IdDoEstudoDeTeste, Peer = null, Name = "bancada: o estudioso", Race = "Human", Genero = "Male", Idade = 25, Zone = zona,
			Pos = new Vec2(4 * ZoneCollision.TileSize, 0), Conta = "bancada_estudo", Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 10_000 }, Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		pl.Ficha.Class = "Normal";
		PorNoMundo(pl);
		pl.Ficha.techskill = 60;   // o proximo nivel longe: o XP de um segundo nao sobe nivel no meio da medida
		var mesa = new Obra { Id = 990_820, Tipo = "Research_Station", X = pl.Pos.X, Y = pl.Pos.Y, DonoNome = pl.Name, Aparafusada = true };
		mesa.PorZona(pl.Zone);

		try
		{
			// ---- E1) o minuto na mesa
			_noChao.Add(mesa);
			pl.Estudando = true;
			for (int s = 0; s < Minijogos.SegundosEntreDesafios - 1; s++) TickDoEstudo();
			bool aos59 = _minijogoDoEstudo.ContainsKey(pl.Id);
			TickDoEstudo();
			bool aos60 = _minijogoDoEstudo.ContainsKey(pl.Id);
			_minijogoDoEstudo.Remove(pl.Id);
			_noChao.Remove(mesa);
			pl.Ficha.med = true;
			for (int s = 0; s < 2 * Minijogos.SegundosEntreDesafios; s++) TickDoEstudo();
			bool meditando = _minijogoDoEstudo.ContainsKey(pl.Id);
			pl.Ficha.med = false;
			AfirmarEt("E1. um MINUTO de estudo na mesa abre o minijogo (59 s nao); estudando meditando, sem mesa, dois minutos nao abrem nada",
					  !aos59 && aos60 && !meditando, $"aos 59 s {aos59}, aos 60 s {aos60}, meditando {meditando}");

			// ---- E2) as contas
			var r = new Random(20261027);
			var distintas = new HashSet<string>();
			string? ruim = null;
			for (int i = 0; i < 3000 && ruim == null; i++)
			{
				(string expr, long resposta) = Minijogos.Conta(r);
				distintas.Add(expr);
				if (expr.Any(c => !(char.IsDigit(c) || c is ' ' or '+' or '×' or '÷'))) ruim = $"simbolo estranho: {expr}";
				else if (Avaliar(expr) is not { } v) ruim = $"divisao inexata: {expr}";
				else if (v != resposta || resposta < 0 || resposta >= 10_000) ruim = $"{expr} = {v}, a tabela diz {resposta}";
			}
			AfirmarEt("E2. as CONTAS: so +, × e ÷, toda divisao exata, resultado inteiro e de cabeca -- e quase nunca repetem (3000 sorteios)",
					  ruim == null && distintas.Count > 1500, ruim ?? $"{distintas.Count} distintas de 3000");

			// ---- E3) os elementos e o baralho
			bool tabela = Minijogos.Elementos.Length == 118 && Minijogos.Elementos.Select((e, i) => e.Z == i + 1).All(b => b)
						  && Minijogos.Elementos.Select(e => e.Simbolo).Distinct().Count() == 118
						  && Minijogos.Elementos.Select(e => e.Nome).Distinct().Count() == 118
						  && Minijogos.Elementos.Select(e => e.NomeEn).Distinct().Count() == 118;
			var perguntados = new HashSet<int>();
			for (int i = 0; i < 118; i++)
			{
				perguntados.Add(AbrirMinijogo(pl, TipoDeMinijogo.Elemento).Indice);
				_minijogoDoEstudo.Remove(pl.Id);
			}
			AfirmarEt("E3. os 118 ELEMENTOS (simbolos e nomes unicos), e o baralho pergunta todos os 118 antes de repetir um",
					  tabela && perguntados.Count == 118, $"tabela {tabela}; {perguntados.Count} distintos em 118 perguntas");

			// ---- E4) o acerto, o teto, a renovacao e o prazo
			_bonusDoEstudo.Remove(pl.Id);
			_noChao.Add(mesa);
			double semBonus = XpDeUmSegundo(pl);
			Acertar(pl);
			double comUm = XpDeUmSegundo(pl);
			for (int i = 0; i < 5; i++) Acertar(pl);
			int noTeto = BonusDoEstudoAtivos(pl.Id);
			double comSeis = XpDeUmSegundo(pl);
			// prazos distintos (os seis nasceram no mesmo milissegundo): o mais antigo e o primeiro
			for (int i = 0; i < _bonusDoEstudo[pl.Id].Count; i++) _bonusDoEstudo[pl.Id][i] = NowMs() + 1000 * (i + 1);
			long segundoMaisAntigo = _bonusDoEstudo[pl.Id][1];
			Acertar(pl);
			int depoisDoSetimo = BonusDoEstudoAtivos(pl.Id);
			bool renovou = _bonusDoEstudo[pl.Id][0] == segundoMaisAntigo && _bonusDoEstudo[pl.Id][^1] > NowMs() + Minijogos.MsDoBonus - 5000;
			AfirmarEt("E4. cada ACERTO soma +25% no ganho de tech (1 = 1,25x; 6 = 2,5x, o teto de +150%), e o setimo renova o mais antigo",
					  Math.Abs(comUm / semBonus - 1.25) < 1e-6 && noTeto == 6 && Math.Abs(comSeis / semBonus - 2.5) < 1e-6 && depoisDoSetimo == 6 && renovou,
					  $"{comUm / semBonus:0.###}x, {comSeis / semBonus:0.###}x com {noTeto}; depois do setimo {depoisDoSetimo}, renovou {renovou}");
			List<long> prazos = _bonusDoEstudo[pl.Id];
			for (int i = 0; i < prazos.Count; i++) prazos[i] = NowMs() - 1;   // os dez minutos passaram
			AfirmarEt("E4b. ...e cada bonus VENCE nos 10 minutos dele", BonusDoEstudoAtivos(pl.Id) == 0 && Math.Abs(XpDeUmSegundo(pl) / semBonus - 1) < 1e-6,
					  $"{BonusDoEstudoAtivos(pl.Id)} valendo");

			// ---- E5) o erro tira o mais antigo
			Acertar(pl);
			Acertar(pl);
			long primeiro = _bonusDoEstudo[pl.Id][0] = NowMs() + 60_000, segundo = _bonusDoEstudo[pl.Id][1] = NowMs() + 120_000;
			AbrirMinijogo(pl, TipoDeMinijogo.Elemento);
			ComandoDeTech(pl, "estudo_resposta", "Xx");
			bool tirouOAntigo = BonusDoEstudoAtivos(pl.Id) == 1 && _bonusDoEstudo[pl.Id][0] == segundo && segundo != primeiro;
			AfirmarEt("E5. o ERRO tira o bonus MAIS ANTIGO (de dois, fica o mais novo)", tirouOAntigo && !_minijogoDoEstudo.ContainsKey(pl.Id),
					  $"{BonusDoEstudoAtivos(pl.Id)} valendo");

			// ---- E6) a molecula
			bool agua = Minijogos.MoleculaFechou(["O", "H", "H"], [(0, 1, 1), (0, 2, 1)]);
			bool aguaFaltando = Minijogos.MoleculaFechou(["O", "H", "H"], [(0, 1, 1)]);
			bool co2 = Minijogos.MoleculaFechou(["C", "O", "O"], [(0, 1, 2), (0, 2, 2)]);
			bool co2Simples = Minijogos.MoleculaFechou(["C", "O", "O"], [(0, 1, 1), (0, 2, 1)]);
			bool n2 = Minijogos.MoleculaFechou(["N", "N"], [(0, 1, 3)]);
			bool n2Dupla = Minijogos.MoleculaFechou(["N", "N"], [(0, 1, 2)]);
			bool eterDimetilico = Minijogos.MoleculaFechou(["C", "C", "O", "H", "H", "H", "H", "H", "H"],
				[(0, 2, 1), (1, 2, 1), (0, 3, 1), (0, 4, 1), (0, 5, 1), (1, 6, 1), (1, 7, 1), (1, 8, 1)]);
			bool soltas = Minijogos.MoleculaFechou(["H", "H", "H", "H"], [(0, 1, 1), (2, 3, 1)]);
			string? naoFecha = Minijogos.Moleculas.Select(m => (m.Nome, Soma: m.Atomos.Sum(a => Minijogos.Valencia.GetValueOrDefault(a, -99)), N: m.Atomos.Length))
				.Where(x => x.Soma < 0 || x.Soma % 2 != 0 || x.Soma / 2 < x.N - 1).Select(x => x.Nome).FirstOrDefault();
			int iCo2 = Array.FindIndex(Minijogos.Moleculas, m => m.Formula == "CO2");
			AbrirMinijogo(pl, TipoDeMinijogo.Molecula).Indice = iCo2;
			ComandoDeTech(pl, "estudo_resposta", "0-1-2;0-2-2");
			bool pelaPorta = BonusDoEstudoAtivos(pl.Id) == 2;
			AfirmarEt("E6. a MOLECULA vale pelas ligacoes certas: agua, CO2 com duas duplas, N2 com tripla e qualquer isomero (o eter do C2H6O) "
					  + "fecham; faltando ligacao, simples no lugar da dupla, ou em dois pedacos, nao -- e toda molecula da tabela fecha a conta",
					  agua && !aguaFaltando && co2 && !co2Simples && n2 && !n2Dupla && eterDimetilico && !soltas && naoFecha == null && pelaPorta,
					  $"agua {agua}/{aguaFaltando}, co2 {co2}/{co2Simples}, n2 {n2}/{n2Dupla}, eter {eterDimetilico}, soltas {soltas}, "
					  + $"tabela {naoFecha ?? "ok"}, pela porta {pelaPorta}");

			// ---- E7) o tempo
			int antesDoTempo = BonusDoEstudoAtivos(pl.Id);
			AbrirMinijogo(pl, TipoDeMinijogo.Conta).Prazo = NowMs() - 1;
			TickDoEstudo();
			AfirmarEt("E7. o TEMPO esgotado fecha o minijogo e nao mexe no bonus",
					  !_minijogoDoEstudo.ContainsKey(pl.Id) && BonusDoEstudoAtivos(pl.Id) == antesDoTempo, $"{antesDoTempo} -> {BonusDoEstudoAtivos(pl.Id)}");

			// ---- E8) na mesa e meditando
			_bonusDoEstudo.Remove(pl.Id);
			_noChao.Remove(mesa);
			pl.Ficha.med = true;
			double meditandoSem = XpDeUmSegundo(pl);
			Acertar(pl);
			Acertar(pl);
			double meditandoCom = XpDeUmSegundo(pl);
			pl.Ficha.med = false;
			AfirmarEt("E8. o bonus vale TAMBEM no estudo meditando (dois acertos = 1,5x no quarto da mesa)",
					  meditandoSem > 0 && Math.Abs(meditandoCom / meditandoSem - 1.5) < 1e-6, $"{meditandoCom / meditandoSem:0.###}x");
		}
		finally
		{
			_noChao.Remove(mesa);
			EsquecerOEstudo(pl.Id);
			_players.Remove(pl.Id);
			ZoneList(zona.Hash).Remove(pl);
		}
		GD.Print($"[estudo] ================ {_etOk} passaram, {_etFalhou} falharam ================");
	}

	/// <summary>Abre um minijogo de conta e responde certo, pela porta do canal de tecnologia.</summary>
	private void Acertar(ServerPlayer pl)
	{
		Minijogo m = AbrirMinijogo(pl, TipoDeMinijogo.Conta);
		ComandoDeTech(pl, "estudo_resposta", m.Resposta);
	}

	/// <summary>O XP de tecnologia de UM segundo de estudo, pelo tique de producao (sem minijogo novo no meio).</summary>
	private double XpDeUmSegundo(ServerPlayer pl)
	{
		_estudoDesdeODesafio[pl.Id] = 0;
		double antes = pl.Ficha.techXp;
		TickDoEstudo();
		_minijogoDoEstudo.Remove(pl.Id);
		return pl.Ficha.techXp - antes;
	}

	/// <summary>A conta pela precedencia de escola (× e ÷ antes do +); nulo se uma divisao nao for exata.</summary>
	private static long? Avaliar(string expr)
	{
		long total = 0;
		foreach (string termo in expr.Split('+'))
		{
			string[] p = termo.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			long v = long.Parse(p[0]);
			for (int i = 1; i + 1 < p.Length; i += 2)
			{
				long n = long.Parse(p[i + 1]);
				if (p[i] == "×") v *= n;
				else if (n == 0 || v % n != 0) return null;
				else v /= n;
			}
			total += v;
		}
		return total;
	}
}
