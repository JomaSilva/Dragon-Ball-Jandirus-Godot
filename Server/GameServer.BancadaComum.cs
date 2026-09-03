using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O QUE TODA BANCADA DE TECNICA REPETIA, num lugar so: o corpo que SABE skills, a dupla colada, o
/// corredor de terra firme e "aperta o verb e ouve o que o servidor disse".
///
/// ============================ POR QUE ISTO EXISTE ============================
/// Nove bancadas (arsenal, punho, censo, catalogo, selo, G10, G11, G12, IA) tinham cada uma o seu
/// `ForjarX`, o seu `Dupla`, o seu corredor e o seu bloco `EscutaDeAvisos = []; ...; = null`. Sao
/// copias com pequenas diferencas -- uma sobe o nivel pelo `DoSave`, outra pelo `Por`; uma enche o
/// Ki, outra esquece; nenhuma devolvia o `EscutaDeAvisos` a nulo num `finally` -- e cada diferenca
/// e uma pergunta a mais na hora de ler uma prova vermelha. Aqui cada gesto tem UMA forma.
///
/// O que fica em cada bancada e so o que e DELA: quais skills, quais degraus, quanto Ki. Os tres
/// lotes de tecnica ja migraram: `ForjarLutadorG10`, `ForjarG11` e `ForjarG12` sao hoje uma linha
/// cada -- a CONFIGURACAO do <see cref="ForjarComSkills"/> --, e as 58 aberturas de escuta escritas
/// a mao viraram <see cref="ApertarEOuvir"/>/<see cref="Ouvir"/>.
///
/// A MIGRACAO ACHOU UM DEFEITO NA PROPRIA FORJA: ela enchia o tanque DEPOIS do `Tick`, e o `Tick` e
/// quem roda o `powerlevel()`. O poder expresso nascia calculado com um corpo em jejum, e a prova do
/// `splitformdeBuff` (G12) reprovou na hora em que a bancada dela passou a usar esta forja. A ordem
/// agora e a mesma do `Forjar`, do `EspelharODono` e do login (2026-09-02) -- e o remendo que a forja
/// do G12 escrevia por conta propria morreu com a troca. Ver o corpo do metodo.
///
/// E AQUELA REPROVACAO FOI O FIO QUE DESENROLOU UM DEFEITO MAIOR (2026-09-02): a prova so ficava
/// verde por causa do Ki que a divisao cobrava, e o corte por copia que ela DIZIA medir nao existia
/// em ramo nenhum da conta de poder. Uma prova que le a variavel errada e uma prova que continua
/// passando depois que o mecanismo morre -- ver a divergencia declarada em `Fighter.Power.cs` e as
/// duas causas separadas (cada uma com o seu contra-exemplo) no `GameServer.G12Teste`.
/// ==========================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// UM CORPO QUE SABE. O <see cref="Forjar"/> da bancada de projetil poe o corpo no mundo; aqui
	/// entram as skills COMPRADAS (`Livro.Dar`, o caminho do ensino e do cargo), os DEGRAUS de nivel
	/// (`Niveis.Por`, o `learn(trainee, baselevel)` do DM -- o maior degrau vence quando o mesmo path
	/// aparece duas vezes) e, se a familia pede, um tanque de Ki e folego grande o bastante pra que a
	/// medida seja o EFEITO do verb, e nao a recusa por falta de energia (essa ja tem bancada propria).
	///
	/// `Statify` DEPOIS das skills, como no login: os atributos nascem com o que o corpo sabe. O tanque
	/// vem depois do `Statify` (e dele que sai o `MaxKi`) e ANTES da conta de poder -- a ordem de
	/// producao, explicada no corpo do metodo.
	/// </summary>
	/// <param name="baseKi">
	/// O TANQUE DE BASE (`baseKi`), escrito ANTES do `Statify` porque e dele que o `MaxKi` sai. O
	/// `Forjar` nasce com 100, e uma familia inteira de tecnicas pede `150*BaseDrain` -- num tanque
	/// desses toda prova mediria a recusa por falta de energia. Diferente do `kiMin`, que carimba o
	/// `MaxKi` DEPOIS da conta: este passa pela conta, como o Ki de quem treinou.
	/// </param>
	/// <param name="efeitosDeSkill">
	/// Aplica os efeitos das skills recem-dadas (`EfeitosDeSkill.Aplicar`) ANTES do `Statify`, que e o
	/// que o login faz. So a bancada do G11 pedia isso, porque e ela quem mede as FLAGS que a skill
	/// escreve na ficha (`can_stretch_arms` e companhia). Fica desligado por padrao de proposito: as
	/// outras cinco bancadas medem receitas calculadas a partir de uma ficha CRUA, e liga-lo pra todas
	/// mudaria o numero que elas conferem. E uma diferenca de verdade entre as bancadas -- por isso ela
	/// tem nome aqui em vez de morar escondida numa quarta copia da forja.
	/// </param>
	private ServerPlayer ForjarComSkills(string nome, Vec2 onde, double bp,
										 string[]? skills = null, (string Path, int Nivel)[]? degraus = null,
										 double kiMin = 0, double staminaMin = 0,
										 double baseKi = 0, bool efeitosDeSkill = false)
	{
		ServerPlayer pl = Forjar(nome, onde, bp);
		if (baseKi > 0) pl.Ficha.baseKi = baseKi;
		if (skills != null) foreach (string s in skills) pl.Livro!.Dar(s);
		if (degraus != null)
			foreach ((string path, int nivel) in degraus)
				if (nivel > pl.Niveis.Nivel(path)) pl.Niveis.Por(path, nivel);
		if (efeitosDeSkill && _skills != null)
			EfeitosDeSkill.Aplicar(pl.Ficha, _skills, pl.Livro!.Aprendidas, pl.Livro.Escolhas);

		// ==================== A ORDEM E A DE PRODUCAO: TANQUE ANTES DA CONTA ====================
		// Primeiro os STATS (`Statify` escreve o `MaxKi`), depois o TANQUE, e so entao a CONTA DE
		// PODER. E a sequencia do `Forjar`, do `EspelharODono` (`Statify` -> `Ki = MaxKi` ->
		// `PowerLevel`) e do login -- e ela e obrigatoria aqui pelo motivo mais simples que existe:
		// CORPO DE BANCADA DIFERENTE DO CORPO DO JOGO MEDE O QUE NAO EXISTE. Toda prova calibrada
		// por limiar (porta de BP, gap de poder, dano minimo, "forte o suficiente") passa a valer
		// sobre um corpo que nenhum jogador tem, e verde nessas condicoes nao diz nada sobre o jogo.
		//
		// ATE 2026-09-02 A ORDEM ERA `Statify` -> `Tick` -> enche o tanque. O `Tick` roda o
		// `powerlevel()`, entao o `expressedBP` nascia calculado com o tanque de ANTES -- um corpo
		// em jejum. MEDIDO nas oito configuracoes de bancada antes de trocar: a diferenca so aparecia
		// onde o `MaxKi` sobe ENTRE o `Forjar` e a conta, ou seja no unico parametro que passa pelo
		// `Statify` -- o `baseKi` (hoje so o lote G12). Nessa faixa o `kiratio` caia no piso 0,6 do
		// `PowerLevel` e o corpo expressava 0,6x do que devia -- o certo e 1,667x o que a bancada via,
		// INVARIANTE por raca, BP e forma (o piso e um `Math.Max`, e 0,6 x AgeDiv nao chega no joelho
		// do `NetCap`). O `BaseDrain` nao mudava em configuracao nenhuma: ele so le o `MaxKi`, que
		// esta escrito antes das duas ordens. As outras sete forjas nasciam com o tanque cheio por
		// acidente (o `Forjar` ja enche, e o `MaxKi` delas nao muda depois) -- estavam certas sem
		// saber, e e por isso que a troca nao mexeu numa unica prova delas.
		//
		// O `PowerLevel()` SOZINHO, e nao o `Tick()`, e essa linha e load-bearing: o `Tick` comeca
		// por um `Statify`, e o `Statify` REESCREVE o `MaxKi` a partir do `baseKi` -- ele apagaria o
		// carimbo do `kiMin` que acabou de subir o tanque, e o corpo seguiria com `Ki` de milhoes
		// num `MaxKi` de 100 (`kiratio` de 50 mil, `nnetBuff` no teto de 10). Foi essa a tentativa
		// que estourou a prova do Rugido Explosivo no `--censoteste`: nao a ordem, o `Tick`.
		// `ClampAnger` e `WeightTick` continuam aqui porque sao os outros dois passos do `Tick`.
		// =======================================================================================
		pl.Ficha.Statify();
		if (kiMin > 0) pl.Ficha.MaxKi = Math.Max(pl.Ficha.MaxKi, kiMin);
		if (staminaMin > 0) pl.Ficha.maxstamina = Math.Max(pl.Ficha.maxstamina, staminaMin);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Ficha.stamina = pl.Ficha.maxstamina;
		pl.Ficha.ClampAnger();
		pl.Ficha.PowerLevel(agoraMs: NowMs());
		pl.Ficha.WeightTick();
		return pl;
	}

	/// <summary>
	/// DUAS PESSOAS COLADAS, olhando uma pra outra, A mirando D, D de guarda baixa -- o cenario de quase
	/// toda familia de golpe. <paramref name="forjador"/> e o corpo que a bancada quer (o socador do G7,
	/// o lutador do G10...); a geometria e a mesma pra todas.
	///
	/// SEM GUARDA E SEM ESQUIVA nas familias que medem DANO: o `MeleeResolver` sorteia pontaria,
	/// bloqueio e esquiva, e um golpe que "as vezes sai" transforma toda medicao de dano numa moeda.
	/// Quem mede a rolagem e a bancada do soco (`--socoteste`); estas medem a RECEITA.
	/// </summary>
	private (ServerPlayer A, ServerPlayer D) Dupla(Func<string, Vec2, double, ServerPlayer> forjador,
												  double bpA = 5_000, double bpD = 5_000,
												  float tilesEntre = 0.9f, Vec2? chao = null)
	{
		Vec2 c = chao ?? CorredorLivre(24);
		ServerPlayer a = forjador("Lutador", c, bpA);
		ServerPlayer d = forjador("Alvo", c + new Vec2(ZoneCollision.TileSize * tilesEntre, 0), bpD);
		a.Facing = Facing.East;
		d.Facing = Facing.West;
		a.AlvoId = d.Id;
		d.Combate.Bloqueando = false;
		return (a, d);
	}

	/// <summary>
	/// UM CORREDOR DE TERRA FIRME, e nao so sem parede: o <see cref="CorredorLivre"/> pergunta a
	/// colisao (`BlockedCell`), e a agua nao bloqueia a colisao -- bloqueia o PASSO (`ClasseDeAgua`,
	/// via `MoveRules.Occupied`). As provas que ANDAM (corrida, arrasto, teleporte pra um vizinho) num
	/// corredor em cima do mar mediam a agua e nao o verb. Confere a fileira inteira e, se pedido, as
	/// quatro diagonais em volta da celula <paramref name="diagonaisEm"/> (o passo de lado de uma
	/// esquiva; -1 = nao exige). A recusa conta no placar da bancada de projetil, como a do
	/// `CorredorLivre` que ela usa.
	/// </summary>
	private Vec2 CorredorDeTerraFirme(int tiles = 24, int diagonaisEm = -1)
	{
		for (int tentativa = 0; tentativa < 60; tentativa++)
		{
			Vec2 c = CorredorLivre(tiles);
			if (_pjMapa is not { } mapa) return c;
			bool ok = !MoveRules.PathOccupied(mapa, c, c + new Vec2((tiles - 2) * ZoneCollision.TileSize, 0));
			if (ok && diagonaisEm >= 0)
				for (int dx = -1; dx <= 1 && ok; dx += 2)
					for (int dy = -1; dy <= 1 && ok; dy += 2)
						ok &= !MoveRules.Occupied(mapa, c + new Vec2((diagonaisEm + dx) * ZoneCollision.TileSize, dy * ZoneCollision.TileSize));
			if (ok) return c;
		}
		AfirmarPj($"achei um corredor de TERRA FIRME de {tiles} tiles (sem agua na fileira) pra bancada", false, "varredura falhou");
		return CorredorLivre(tiles);
	}

	/// <summary>
	/// APERTA O VERB PELO FUNIL DE PRODUCAO e devolve o que o servidor DISSE ao corpo. E o
	/// `EscutaDeAvisos = []; ...; = null` que toda bancada escrevia a mao -- com o `finally` que
	/// nenhuma tinha: uma prova que estourasse no meio deixava a escuta ligada pro jogo inteiro.
	/// </summary>
	private List<string> ApertarEOuvir(ServerPlayer pl, string id) => Ouvir(() => UsarHabilidade(pl, id));

	/// <summary>O MESMO OUVIDO pra qualquer gesto -- um comando da IA, um tique, uma tecla.</summary>
	private static List<string> Ouvir(Action gesto)
	{
		List<string> falas = [];
		EscutaDeAvisos = falas;
		try { gesto(); }
		finally { EscutaDeAvisos = null; }
		return falas;
	}

	/// <summary>
	/// O SERVIDOR DISSE ISTO? Cada bancada tinha a sua: `Disse(falas, t)` na G12, `DisseG11(t)` na
	/// G11 (sobre a escuta ambiente) e um `EscutaDeAvisos.Exists(a => a.Contains(...))` escrito a mao
	/// na G10. Sao a mesma pergunta -- e as tres ja comparavam sem ligar pra caixa alta.
	/// </summary>
	private static bool Disse(List<string> falas, string trecho) =>
		falas.Exists(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	/// <summary>O que o servidor disse, numa linha so -- o rodape de toda prova vermelha.</summary>
	private static string Ultimos(List<string>? falas) => string.Join(" | ", falas ?? []);
}
