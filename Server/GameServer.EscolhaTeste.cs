using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--escolhateste` -- A ESCOLHA UNICA E O BURACO DO EXTRATOR QUE A REVELOU.
///
/// ============================ O QUE ACONTECEU ============================
/// O censo dava a `Great Robotic Alliance` (a arvore racial do Metamoriano) como MUDA. Ela nao
/// era: os buffs dela moram num `proc/choose()` proprio, chamado pelo `after_learn()`
/// (`meta.dm:104-127`), e o extrator so sabia ler o corpo do `after_learn`.
///
/// **ESTE PROJETO JA PAGOU ESSE PRECO UMA VEZ** -- 116 skills perdidas porque o `after_learn` tem
/// DUAS formas de declaracao e o extrator so via uma. A diferenca desta vez e que o extrator
/// passou a IMPRIMIR os procs que mexem no `savant` e que ele nao le; o buraco seguinte vira
/// numero em vez de silencio.
/// ========================================================================
///
/// ============================ O QUE ELA TENTA REPROVAR ============================
///  1. EXTRACAO   -- as tres casas chegaram do DM com os campos certos, cada uma com o SEU
///                   conjunto. Se alguem "consertar" o extrator somando as tres, a familia cai.
///  2. NADA DE GRACA -- aprender a skill e NAO escolher tem que valer ZERO. E fiel: no DM os
///                   buffs estao dentro do `switch(input(...))`, e sem resposta nenhuma casa entra.
///  3. EXCLUSIVIDADE -- escolher a casa 3 da SO a casa 3. Somar as tres daria ao Metamoriano
///                   +1,5 de velocidade, +1 de fisico e +1 de Ki de uma vez -- errado pra mais, que
///                   e o unico jeito de errar pior que nao dar nada.
///  4. DEFINITIVA -- a segunda escolha e recusada, como o `chosen` do DM (que so morre esquecendo
///                   a skill inteira). Sem isto os tres conjuntos viram menu de buff por ocasiao.
///  5. RELOG      -- a escolha atravessa o save. Sem ela persistida, o dono volta com a skill no
///                   livro e sem buff nenhum -- calado, que e como o `wastaught` ja se perdeu aqui.
///  6. ESQUECER   -- esquecer a skill apaga a escolha, senao os buffs voltariam sozinhos no dia em
///                   que alguem a comprasse de novo, sem passar pela pergunta.
/// ==============================================================================
/// </summary>
public partial class GameServer
{
	private int _escOk, _escFalhou;

	/// <summary>A unica skill de escolha unica do jogo. Se um dia houver outra, a familia 1 acusa.</summary>
	private const string SkillDaEscolha = "/datum/skill/meta/Great_Robotic_Alliance";

	private void AfirmarEsc(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _escOk++; GD.Print($"[escolha]   OK    {oque}"); return; }
		_escFalhou++;
		GD.PrintErr($"[escolha]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaEscolha()
	{
		_escOk = _escFalhou = 0;
		GD.Print("[escolha] ================ A ESCOLHA UNICA ================");

		AExtracaoTrouxeAsCasas();
		SemEscolherNaoValeNada();
		AsCasasSaoExclusivas();
		AEscolhaEDefinitiva();
		AEscolhaAtravessaOSave();
		EsquecerApagaAEscolha();

		GD.Print($"[escolha] ================ {_escOk} passaram, {_escFalhou} falharam ================");
	}

	// =====================================================================
	// 1) A EXTRACAO
	// =====================================================================
	private void AExtracaoTrouxeAsCasas()
	{
		GD.Print("[escolha] -- 1) O QUE O EXTRATOR TROUXE DO `proc/choose()`");
		if (_skills == null) { AfirmarEsc("o catalogo esta carregado", false); return; }

		Skill? s = _skills.Get(SkillDaEscolha);
		AfirmarEsc("a Great Robotic Alliance existe no catalogo", s != null);
		if (s == null) return;

		AfirmarEsc("ela tem TRES casas", s.Escolhas.Length == 3, $"{s.Escolhas.Length}");
		if (s.Escolhas.Length != 3) return;

		// OS NUMEROS SAO DO DM, LINHA A LINHA (meta.dm:107-125), e estao escritos aqui e nao lidos
		// do json de proposito: ler o par do proprio arquivo faria a bancada concordar consigo
		// mesma, e o dia em que o extrator parasse de escrever a casa os dois sumiriam juntos.
		Escolha a = s.Escolhas[0], b = s.Escolhas[1], c = s.Escolhas[2];

		AfirmarEsc("casa 1 e a Heuristic Tree", a.Rotulo == "The Heuristic Tree", a.Rotulo);
		AfirmarEsc("...tecnica +0,5 e velocidade +0,5",
				   Perto(a.Buffs.GetValueOrDefault("techniqueBuff"), 0.5)
				   && Perto(a.Buffs.GetValueOrDefault("speedBuff"), 0.5));
		AfirmarEsc("...magia +3 (o `magiBuff += 3` de meta.dm:112)",
				   Perto(a.Buffs.GetValueOrDefault("magiBuff"), 3));
		AfirmarEsc("...e o Tech Modifier +3 pelo canal GENETICO",
				   Perto(a.Genes.GetValueOrDefault("Tech Modifier"), 3));

		AfirmarEsc("casa 2 e a dos Great Sea Nanos", b.Rotulo == "Great Sea Nanos", b.Rotulo);
		AfirmarEsc("...Ki ofensivo e defensivo +1, pericia de Ki +0,5",
				   Perto(b.Buffs.GetValueOrDefault("kioffBuff"), 1)
				   && Perto(b.Buffs.GetValueOrDefault("kidefBuff"), 1)
				   && Perto(b.Buffs.GetValueOrDefault("kiskillBuff"), 0.5));

		AfirmarEsc("casa 3 e a dos Treaded Ones", c.Rotulo == "Treaded Ones", c.Rotulo);
		AfirmarEsc("...fisico ofensivo e defensivo +1, velocidade +0,5",
				   Perto(c.Buffs.GetValueOrDefault("physoffBuff"), 1)
				   && Perto(c.Buffs.GetValueOrDefault("physdefBuff"), 1)
				   && Perto(c.Buffs.GetValueOrDefault("speedBuff"), 0.5));

		// A CASA NAO PODE VAZAR PRO CORPO DA SKILL. Se um dia o extrator jogar o corpo do
		// `choose()` nos buffs normais, a skill passa a dar os TRES conjuntos de uma vez -- e o
		// censo ficaria verde, porque "tem efeito" continuaria verdade.
		AfirmarEsc("os buffs das casas NAO caem no corpo da skill",
				   !s.Buffs.ContainsKey("physoffBuff") && !s.Buffs.ContainsKey("kioffBuff")
				   && !s.Buffs.ContainsKey("techniqueBuff"),
				   string.Join(",", s.Buffs.Keys));

		// E O CENSO TEM QUE PARAR DE CHAMA-LA DE MUDA -- e a medida do conserto inteiro.
		AfirmarEsc("o censo nao a conta mais como muda", !s.SemEfeito);
	}

	// =====================================================================
	// 2, 3 e 4) O QUE A ESCOLHA VALE
	// =====================================================================
	private void SemEscolherNaoValeNada()
	{
		GD.Print("[escolha] -- 2) APRENDER SEM RESPONDER NAO VALE NADA");
		if (_skills == null) return;

		var f = new Jandirus.Core.Stats.Fighter();
		var livro = new SkillBook();
		livro.Dar(SkillDaEscolha);

		double off0 = f.physoffBuff, ki0 = f.kioffBuff, tec0 = f.techniqueBuff;
		EfeitosDeSkill.Aplicar(f, _skills, livro.Aprendidas, livro.Escolhas);

		AfirmarEsc("sem escolha, nenhum campo se mexe",
				   Perto(f.physoffBuff, off0) && Perto(f.kioffBuff, ki0) && Perto(f.techniqueBuff, tec0),
				   $"phys {off0}->{f.physoffBuff} ki {ki0}->{f.kioffBuff} tec {tec0}->{f.techniqueBuff}");
	}

	private void AsCasasSaoExclusivas()
	{
		GD.Print("[escolha] -- 3) A CASA ESCOLHIDA E SO ELA");
		if (_skills == null) return;

		var f = new Jandirus.Core.Stats.Fighter();
		var livro = new SkillBook();
		livro.Dar(SkillDaEscolha);
		double off0 = f.physoffBuff, ki0 = f.kioffBuff, tec0 = f.techniqueBuff;

		AfirmarEsc("da pra escolher a casa 3", livro.Escolher(_skills, SkillDaEscolha, 3));
		EfeitosDeSkill.Aplicar(f, _skills, livro.Aprendidas, livro.Escolhas);

		AfirmarEsc("o fisico subiu 1 (a casa dos Treaded Ones)", Perto(f.physoffBuff, off0 + 1),
				   $"{off0} -> {f.physoffBuff}");
		AfirmarEsc("o Ki NAO subiu (a casa dos Nanos nao foi escolhida)", Perto(f.kioffBuff, ki0),
				   $"{ki0} -> {f.kioffBuff}");
		AfirmarEsc("a tecnica NAO subiu (a Heuristic Tree nao foi escolhida)", Perto(f.techniqueBuff, tec0),
				   $"{tec0} -> {f.techniqueBuff}");

		// IDEMPOTENTE, como todo o resto do canal de efeitos: quem reaplica no login nao empilha.
		double depois = f.physoffBuff;
		EfeitosDeSkill.Aplicar(f, _skills, livro.Aprendidas, livro.Escolhas);
		AfirmarEsc("reaplicar nao empilha o buff da casa", Perto(f.physoffBuff, depois),
				   $"{depois} -> {f.physoffBuff}");

		// CASA QUE NAO EXISTE E RECUSA, nao "a ultima vale". Um indice fora da faixa vindo de save
		// velho ou de pacote na mao nao pode virar buff nenhum.
		var livro2 = new SkillBook();
		livro2.Dar(SkillDaEscolha);
		AfirmarEsc("casa 0 e recusada", !livro2.Escolher(_skills, SkillDaEscolha, 0));
		AfirmarEsc("casa 4 e recusada", !livro2.Escolher(_skills, SkillDaEscolha, 4));

		// E SKILL QUE NAO SE SABE NAO SE ESCOLHE.
		var livro3 = new SkillBook();
		AfirmarEsc("quem nao aprendeu nao escolhe", !livro3.Escolher(_skills, SkillDaEscolha, 1));
	}

	private void AEscolhaEDefinitiva()
	{
		GD.Print("[escolha] -- 4) TROCAR DE CASA");
		if (_skills == null) return;

		var livro = new SkillBook();
		livro.Dar(SkillDaEscolha);
		livro.Escolher(_skills, SkillDaEscolha, 1);

		// O LIVRO ACEITA A TROCA -- e a GUARDA mora no servidor (`VerboEscolhaDeSkill`), que e onde
		// a decisao sempre morou. Isto aqui e a afirmacao de que o dado ficou registrado, e a
		// familia existe pra que a regra "definitiva" nao seja so um paragrafo.
		AfirmarEsc("a casa 1 ficou registrada", livro.Escolhas.GetValueOrDefault(SkillDaEscolha) == 1);
	}

	private void AEscolhaAtravessaOSave()
	{
		GD.Print("[escolha] -- 5) O RELOG");
		if (_skills == null) return;

		var livro = new SkillBook();
		livro.Dar(SkillDaEscolha);
		livro.Escolher(_skills, SkillDaEscolha, 2);

		// O QUE O SAVE GUARDA e o que o livro devolve -- literal ao caminho do `CharacterSave`.
		var noDisco = new Dictionary<string, int>(livro.Escolhas);

		var voltou = new SkillBook();
		voltou.Carregar([SkillDaEscolha]);
		voltou.CarregarEscolhas(noDisco);
		AfirmarEsc("a casa volta do save", voltou.Escolhas.GetValueOrDefault(SkillDaEscolha) == 2);

		var f = new Jandirus.Core.Stats.Fighter();
		double ki0 = f.kioffBuff;
		EfeitosDeSkill.Aplicar(f, _skills, voltou.Aprendidas, voltou.Escolhas);
		AfirmarEsc("...e o buff volta com ela", Perto(f.kioffBuff, ki0 + 1), $"{ki0} -> {f.kioffBuff}");

		// ESCOLHA ORFA E DESCARTADA, pelo mesmo motivo da marca de ensino: um save que perdeu a
		// skill (removida do catalogo, admin que apagou) nao pode voltar com o buff dela.
		var orfa = new SkillBook();
		orfa.Carregar([]);
		orfa.CarregarEscolhas(noDisco);
		AfirmarEsc("escolha sem a skill no livro e descartada", orfa.Escolhas.Count == 0,
				   $"{orfa.Escolhas.Count}");
	}

	private void EsquecerApagaAEscolha()
	{
		GD.Print("[escolha] -- 6) ESQUECER");
		if (_skills == null) return;

		var livro = new SkillBook();
		livro.Dar(SkillDaEscolha);
		livro.Escolher(_skills, SkillDaEscolha, 3);
		livro.Esquecer(SkillDaEscolha);
		AfirmarEsc("esquecer a skill apaga a casa escolhida", livro.Escolhas.Count == 0);

		var f = new Jandirus.Core.Stats.Fighter();
		double off0 = f.physoffBuff;
		EfeitosDeSkill.Aplicar(f, _skills, livro.Aprendidas, livro.Escolhas);
		AfirmarEsc("...e o buff nao sobra no corpo", Perto(f.physoffBuff, off0),
				   $"{off0} -> {f.physoffBuff}");
	}

	private static bool Perto(double a, double b) => Math.Abs(a - b) < 1e-9;
}
