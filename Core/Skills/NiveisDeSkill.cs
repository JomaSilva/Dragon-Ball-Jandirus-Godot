using System.Reflection;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Skills;

/// <summary>
/// UM DEGRAU: o que acontece quando a skill CHEGA neste nivel.
///
/// No DM isto e o corpo de um `if(N)` dentro do `switch(level)` do `effector()`, e o que mora la
/// e quase sempre um `assignverb` -- e a metade de baixo do sistema de habilidades. Comprar a
/// skill da o buff passivo (`after_learn`); USAR a skill ate subir de nivel e o que entrega o
/// GOLPE. Green Dean sem nivel 2 e so +0,3 de tecnica; com nivel 2 e o Dash Attack.
/// </summary>
public sealed class Degrau
{
	public int Nivel;

	/// <summary>Campo do lutador -> quanto somar ao cruzar este degrau. Mesmo canal aditivo do
	/// <see cref="EfeitosDeSkill"/>, so que disparado por nivel em vez de por compra.</summary>
	public Dictionary<string, double> Buffs = new(StringComparer.Ordinal);

	/// <summary>As habilidades ativas que este degrau destrava (os `assignverb` do DM).</summary>
	public string[] Verbos = [];

	/// <summary>
	/// CHAVE LIGADA, e nao valor somado: `campo = n` no DM (`savant.canPower = 1`).
	///
	/// TEM QUE SER UM CANAL SEPARADO do <see cref="Buffs"/>. Somar 1 num campo de chave funciona
	/// da primeira vez e mente na segunda -- dois degraus que ligam a mesma chave deixariam o
	/// campo em 2, e qualquer teste de igualdade a 1 passaria a falhar. Chave se ESCREVE.
	///
	/// Foi por aqui que o `canPower` -- a permissao de fazer power-up, a metade da tecla C --
	/// ficou extraida no `niveis.json` e sem ninguem pra consumir: o extrator emitia `flags`, o
	/// leitor lia so `buffs`, e nada na tela dizia que a skill nao tinha feito nada.
	/// </summary>
	public Dictionary<string, double> Flags = new(StringComparer.Ordinal);

	/// <summary>O que o jogador le quando cruza. Vazio = o servidor monta a frase generica.</summary>
	public string Aviso = "";
}

/// <summary>
/// COMO UMA SKILL SOBE DE NIVEL. E dado (vem do DM), nao estado (nao e de ninguem).
///
/// TRES COISAS: quanto custa o proximo nivel, como esse custo cresce, e por onde entra o exp.
/// </summary>
public sealed class RegraDeNivel
{
	public string Path = "";

	/// <summary>`expbarrier` do nivel 0. Padrao do DM = 100 (skill.dm:9).</summary>
	public double Barreira = NiveisDeSkill.BarreiraPadrao;

	/// <summary>
	/// Quanto a barreira INCHA por nivel: `expbarrier = base * Crescimento**level`.
	///
	/// 1 = barreira fixa, que e o caso das 24 skills fisicas semeadas aqui. As arvores de Ki
	/// usam 1,03 (`expbarrier=(5000*1.03**level)`, Effusion.dm:48) -- com maxlevel 100 isso
	/// quase vintuplica o custo do ultimo nivel, e e o que faz uma arvore durar meses.
	/// </summary>
	public double Crescimento = 1;

	/// <summary>
	/// Esta skill sobe SO POR ESTAR VIVA (o `if(level &lt; maxlevel) if(prob(50)) exp++` do DM).
	///
	/// Falso NAO quer dizer "nao sobe": quer dizer que o exp vem de EVENTO, por
	/// <see cref="NiveisDeSkill.Creditar"/> -- que e o `Skill_EXP_Add` do original
	/// (mobhandlers.dm:37-40). As arvores de Ki contam golpes disparados; as fisicas contam tempo.
	/// </summary>
	public bool GanhoPorTempo;

	/// <summary>
	/// EXP POR ESTADO DO CORPO: quanto esta skill ganha por tique quando a condicao vale.
	///
	/// ============================ ISTO ESTAVA EXTRAIDO E MORTO ============================
	/// O `niveis.json` tem 146 blocos `exp`, cada um com `quanto` e `cond` -- lidos direto do DM.
	/// O leitor (`RegrasDoDisco`) abria o bloco, olhava so pra `prob`, e jogava o resto fora.
	/// Resultado: as 122 regras cuja condicao NAO e "por tempo" nunca creditaram nada.
	///
	/// A mais visivel e a raiz da arvore de Ki: `/datum/skill/mind/Ki_Unlocked` ganha
	/// `KiSkillGains(2)` MEDITANDO e `KiSkillGains(2)` VOANDO (Mind.dm:81), e 1 andando por ai.
	/// Nenhum dos tres chegava -- a skill que abre o Ki inteiro estava congelada no nivel em que
	/// tivesse sido comprada, e nada na tela dizia isso.
	///
	/// Mesma familia do `canPower` e do `expbarrier`: o extrator entregou, ninguem consumiu.
	/// =====================================================================================
	/// </summary>
	public sealed class GanhoPorEstado
	{
		/// <summary>Quanto exp por tique.</summary>
		public double Quanto;

		/// <summary>Que estado do corpo liga este ganho.</summary>
		public Estado Quando;
	}

	/// <summary>
	/// AS CONDICOES QUE ESTE PORT SABE AVALIAR.
	///
	/// O `cond` do extrator e uma EXPRESSAO do DM em texto (`savant.med`,
	/// `savant.kiratio>1&&savant.lssj>=2`, `attackcounter&lt;savant.beamcounter`...). Escrever um
	/// avaliador de expressoes de DM aqui seria construir um interpretador pra usar tres casos --
	/// e um interpretador incompleto erra CALADO, que e pior que nao ter.
	///
	/// Entao so as condicoes reconhecidas viram regra, e o carregador CONTA as que ficaram de fora
	/// (ver o aviso no `RegrasDoDisco`). O que nao entra continua nao creditando, exatamente como
	/// antes -- a diferenca e que agora isso e um numero no log e nao um silencio.
	/// </summary>
	public enum Estado
	{
		/// <summary>Sem condicao: sempre.</summary>
		Sempre,
		/// <summary>`savant.med` -- meditando.</summary>
		Meditando,
		/// <summary>`savant.flight` -- no ar.</summary>
		Voando,
		/// <summary>`savant.train` -- treinando.</summary>
		Treinando,
		/// <summary>`!savant.med&amp;&amp;!savant.flight` -- nem uma coisa nem outra.</summary>
		Ocioso,
	}

	/// <summary>Os ganhos por estado desta skill.</summary>
	public List<GanhoPorEstado> PorEstado = [];

	public Degrau[] Degraus = [];

	/// <summary>Quanto exp falta pra sair de <paramref name="nivel"/> pro seguinte.</summary>
	public double BarreiraEm(int nivel)
	{
		// MULTIPLICACAO REPETIDA, e nao `Math.Pow`. Cliente e servidor precisam chegar no MESMO
		// numero, e `Math.Pow` nao tem resultado identico garantido entre implementacoes de
		// libm -- o `GeradorDeTerreno` deste mesmo lote se proibe disso pelo mesmo motivo, e
		// duas regras opostas no mesmo commit e a semente de uma divergencia sutil.
		//
		// O teto e `maxlevel` (100 no pior caso), entao o laco custa nada.
		double b = Barreira;
		for (int i = 0; i < nivel && i < 1000; i++) b *= Crescimento;
		return b;
	}

	public Degrau? DegrauDe(int nivel)
	{
		foreach (Degrau d in Degraus) if (d.Nivel == nivel) return d;
		return null;
	}
}

/// <summary>O estado de nivel de um personagem, no formato que vai pro disco.</summary>
public sealed class NivelSave
{
	/// <summary>typepath -> [nivel, exp].</summary>
	public Dictionary<string, double[]> Skills = [];

	/// <summary>
	/// O QUE OS DEGRAUS JA SOMARAM NO CORPO (campo -> quanto). E o razao da idempotencia, o
	/// mesmo papel de <c>Fighter.BuffsDeSkill</c>.
	///
	/// PRECISA ir pro disco, e nao ser recalculado no login a partir dos niveis: o
	/// <see cref="RegrasDeNivel"/> vai CRESCER (hoje ele cobre 24 skills, o DM tem 200+). Se o
	/// razao fosse deduzido do nivel na hora de entrar, um degrau portado DEPOIS do save do
	/// jogador nasceria ja marcado como "aplicado" -- e o buff novo nunca chegaria no corpo de
	/// quem ja tinha o nivel. O bug seria invisivel: a ficha existe, o nivel existe, o numero
	/// nao aparece.
	/// </summary>
	public Dictionary<string, double> Somados = [];
}

/// <summary>
/// O NIVEL DAS SKILLS -- o motor que faltava pro <see cref="SkillBook"/>.
///
/// O livro sabia SE voce aprendeu. O DM sabe QUANTO: cada `/datum/skill` carrega `level`, `exp`,
/// `expbarrier` e `maxlevel` (skill.dm:7-10), e o `effector()` (skill.dm:86-95) e um lacinho de
/// quatro linhas que roda pra sempre enquanto a skill tem dono:
///
///     level = min(level, maxlevel)
///     exp   = max(exp, 0)
///     if(exp >= expbarrier &amp;&amp; level &lt; maxlevel) { exp = 0; level += 1; levelup = 1 }
///
/// POR QUE ISSO IMPORTA MAIS DO QUE PARECE: das 24 skills fisicas semeadas aqui, NENHUMA entrega
/// o golpe na compra -- o `assignverb` mora dentro do `switch(level)`, no degrau. O catalogo
/// extraido confirma: `/datum/skill/MartialSkill/Green_Dean` sai do extrator com
/// `"verbos": []`, porque o Dash Attack nao esta no `after_learn()`, esta no efetor. Sem este
/// arquivo, comprar a arvore de Martial Skill inteira nao da UM golpe a ninguem.
///
/// A CADENCIA E O PRECO. Ver <see cref="SegundosPorTique"/>: quem chama isto na cadencia errada
/// nao quebra nada visivelmente, so muda o jogo de escala -- uma skill que devia levar 40
/// segundos passa a levar 4 minutos, e isso ninguem reporta como bug.
/// </summary>
public sealed class NiveisDeSkill
{
	/// <summary>`expbarrier = 100` e o valor de fabrica do `/datum/skill` (skill.dm:9).</summary>
	public const double BarreiraPadrao = 100;

	/// <summary>
	/// A CADENCIA DO EFETOR, EM SEGUNDOS. O `learn()` do DM larga um laco eterno por skill --
	/// `spawn while(savant) { effector(); sleep(2) }` (skill.dm:39-42), repetido no `login()`
	/// (skill.dm:50-52). `sleep(2)` no BYOND sao 2 decimos: 0,2 s.
	///
	/// NAO ACHEI NENHUM "Unitimer" no codigo -- a unica ocorrencia da palavra em toda a arvore e
	/// um COMENTARIO (speedy.dm:94, "The process' loop, handled in Unitimer"), sobra de um
	/// agendador central que nao existe mais. O laco por skill do skill.dm E o Unitimer hoje.
	///
	/// (A UNIDADE ESTA EXPLICADA E PROVADA EM <see cref="TempoDoDm"/> -- este arquivo foi um dos
	/// poucos que sempre a leu certo, e o resto do port dividia por `world.fps`.)
	///
	/// UMA RESSALVA HONESTA: o mundo roda a 12 fps (World.dm:5), entao um tick vale 0,833
	/// decimos e um `sleep(2)` cai no primeiro tick DEPOIS de 0,2 s -- 0,25 s de verdade. Os
	/// 0,2 s daqui sao a leitura do que o codigo PEDE; a diferenca e artefato do relogio do
	/// BYOND, que este port nao tem. Em numero fechado: com prob(50) isso da 2,5 exp/s, e uma
	/// barreira de 100 vira ~40 s por nivel (no BYOND real, ~50 s).
	/// </summary>
	public const double SegundosPorTique = 0.2;

	/// <summary>`prob(50)` -- a chance de ganhar 1 de exp em cada tique (Body.dm:180 e 23 irmas).</summary>
	public const double ChanceDeGanho = 0.5;

	private readonly Dictionary<string, Progresso> _p = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>O que os degraus ja somaram no corpo. Ver <see cref="NivelSave.Somados"/>.</summary>
	private Dictionary<string, double> _somados = new(StringComparer.Ordinal);

	public struct Progresso
	{
		public int Nivel;
		public double Exp;
	}

	/// <summary>Uma subida de nivel acontecida NESTE tique. E o `levelup=1` do DM, ja consumido.</summary>
	public readonly record struct Subida(string Path, string Nome, int Nivel, Degrau? Degrau);

	public int Nivel(string path) => _p.TryGetValue(path, out Progresso pr) ? pr.Nivel : 0;
	public double Exp(string path) => _p.TryGetValue(path, out Progresso pr) ? pr.Exp : 0;
	public IEnumerable<(string Path, int Nivel, double Exp)> Todas =>
		_p.Select(kv => (kv.Key, kv.Value.Nivel, kv.Value.Exp));

	/// <summary>
	/// Quanto falta pro proximo nivel desta skill: (exp atual, barreira). (0,0) = nao progride.
	/// E o que a aba "Check Skill Stats" do original mostrava (skill.dm:127-128).
	/// </summary>
	public (double Exp, double Barreira) Andamento(SkillCatalog cat, string path)
	{
		RegraDeNivel? r = RegrasDeNivel.Get(path);
		if (r == null) return (0, 0);
		int n = Nivel(path);
		if (n >= MaxNivel(cat, path)) return (0, 0);
		return (Exp(path), r.BarreiraEm(n));
	}

	private static int MaxNivel(SkillCatalog cat, string path) => Math.Max(1, cat.Get(path)?.MaxNivel ?? 1);

	// =====================================================================
	// O EFETOR
	// =====================================================================
	/// <summary>
	/// UM TIQUE DO EFETOR pra todas as skills deste personagem. CHAME A CADA
	/// <see cref="SegundosPorTique"/> SEGUNDOS -- ver o comentario da constante.
	///
	/// Faz, na ordem do DM: acerta a lista com o livro (skill esquecida perde o nivel), paga o
	/// ganho por tempo, e roda o `effector()` base (skill.dm:86-95). Devolve as subidas do tique
	/// -- lista vazia (o caso normal) nao custa alocacao.
	/// </summary>
	/// <summary>
	/// O QUE O CORPO ESTA FAZENDO neste tique -- o que decide quais ganhos por estado valem.
	///
	/// Um struct e nao tres parametros porque a lista vai crescer (o DM tem `currush`, `kiratio`,
	/// contadores de golpe...), e trocar a assinatura de um metodo chamado de tres lugares toda vez
	/// que uma condicao nova entra e como se perde chamada pelo caminho.
	/// </summary>
	public readonly record struct EstadoDoCorpo(bool Meditando, bool Voando, bool Treinando);

	public List<Subida> Efetor(Random rng, SkillCatalog cat, SkillBook livro,
							   EstadoDoCorpo corpo = default)
	{
		Sincronizar(livro);
		List<Subida>? subidas = null;

		foreach (string path in _p.Keys.ToList())
		{
			RegraDeNivel r = RegrasDeNivel.Get(path)!;
			int max = MaxNivel(cat, path);
			Progresso pr = _p[path];

			// skill.dm:90-91 -- as duas travas vem ANTES da subida, e nesta ordem. Sem o
			// clamp de nivel, baixar o maxlevel numa atualizacao deixaria gente acima do teto.
			if (pr.Nivel > max) pr.Nivel = max;
			if (pr.Exp < 0) pr.Exp = 0;

			// `if(level < maxlevel) if(prob(50)) exp++` -- so quem ainda tem pra onde subir
			// acumula. Deixar o exp correr no teto faria a skill "estourar" no dia em que o
			// maxlevel subisse.
			if (r.GanhoPorTempo && pr.Nivel < max && rng.NextDouble() < ChanceDeGanho) pr.Exp++;

			// GANHO POR ESTADO DO CORPO -- o que estava extraido e nunca creditado. Ver
			// `RegraDeNivel.PorEstado`. Mesma trava do ganho por tempo: quem ja esta no teto nao
			// acumula, senao a skill "estouraria" no dia em que o maxlevel subisse.
			if (pr.Nivel < max)
				foreach (RegraDeNivel.GanhoPorEstado g in r.PorEstado)
					if (g.Quando switch
						{
							RegraDeNivel.Estado.Sempre => true,
							RegraDeNivel.Estado.Meditando => corpo.Meditando,
							RegraDeNivel.Estado.Voando => corpo.Voando,
							RegraDeNivel.Estado.Treinando => corpo.Treinando,
							RegraDeNivel.Estado.Ocioso => !corpo.Meditando && !corpo.Voando,
							_ => false,
						})
						pr.Exp += g.Quanto;

			// skill.dm:92-95 -- a subida em si
			double barreira = r.BarreiraEm(pr.Nivel);
			if (pr.Exp >= barreira && pr.Nivel < max)
			{
				pr.Exp = 0;
				pr.Nivel++;
				(subidas ??= []).Add(new Subida(path, cat.Get(path)?.Nome ?? path, pr.Nivel, r.DegrauDe(pr.Nivel)));
			}

			_p[path] = pr;
		}

		return subidas ?? [];
	}

	/// <summary>
	/// EXP POR EVENTO. E o `mob/proc/Skill_EXP_Add(stype, n)` do original (mobhandlers.dm:37-40),
	/// e e por aqui que as arvores de Ki sobem: elas nao contam tempo, contam golpe disparado
	/// (`exp += KiSkillGains(10*(effusioncounter-attackcounter))`, Effusion.dm:76).
	///
	/// So credita quem tem regra e ja foi aprendida -- creditar numa skill que a pessoa nao tem
	/// criaria progresso fantasma que aparece do nada no dia em que ela comprar.
	/// </summary>
	public bool Creditar(string path, double quanto)
	{
		if (quanto <= 0 || !_p.TryGetValue(path, out Progresso pr)) return false;
		pr.Exp += quanto;
		_p[path] = pr;
		return true;
	}

	/// <summary>
	/// Poe uma skill num nivel na marra. E o `learn(trainee, baselevel)` do DM (skill.dm:34-36):
	/// quem foi ENSINADO as vezes ja nasce no nivel 1 (Mind.dm:104 passa baselevel=1).
	/// </summary>
	public void Por(string path, int nivel, double exp = 0)
	{
		if (RegrasDeNivel.Get(path) == null) return;
		_p[path] = new Progresso { Nivel = Math.Max(0, nivel), Exp = Math.Max(0, exp) };
	}

	/// <summary>
	/// Acerta a lista com o livro: skill nova entra no nivel 0, skill esquecida sai.
	///
	/// SAIR IMPORTA. O `forget()` do DM deleta o datum inteiro (skill.dm:78), e junto vai o
	/// nivel. Guardar o nivel de uma skill devolvida seria um jeito barato de reaprender no topo.
	/// </summary>
	private void Sincronizar(SkillBook livro)
	{
		foreach (string path in livro.Aprendidas)
			if (!_p.ContainsKey(path) && RegrasDeNivel.Get(path) != null)
				_p[path] = default;

		foreach (string path in _p.Keys.ToList())
			if (!livro.Sabe(path)) _p.Remove(path);
	}

	// =====================================================================
	// OS EFEITOS DE DEGRAU
	// =====================================================================
	/// <summary>
	/// As habilidades ativas que os niveis ATUAIS destravam.
	///
	/// E o `login()` do DM virado do avesso. La, cada skill reatribui os proprios verbs na
	/// entrada (`if(level >= 2) assignverb(...)`, Martial Skill.dm:49) porque o verb some com a
	/// sessao. Aqui nao existe "verb do mob": existe a pergunta "voce pode usar isto?", e a
	/// resposta e recalculada do nivel toda vez. Nada pra reatribuir, nada pra esquecer.
	/// </summary>
	public IEnumerable<string> VerbosAtivos()
	{
		foreach ((string path, Progresso pr) in _p)
		{
			RegraDeNivel? r = RegrasDeNivel.Get(path);
			if (r == null) continue;
			foreach (Degrau d in r.Degraus)
				if (d.Nivel <= pr.Nivel)
					foreach (string v in d.Verbos) yield return v;
		}
	}

	/// <summary>
	/// POE NO CORPO os buffs de todos os degraus ja cruzados -- nem mais, nem de novo.
	///
	/// MESMO PADRAO DO <see cref="EfeitosDeSkill"/>, e de proposito: totaliza o que DEVERIA estar
	/// aplicado, desconta o que ja estava, mexe so na diferenca. Chamar duas vezes seguidas nao
	/// faz nada na segunda; chamar no login nao empilha. O razao (<see cref="NivelSave.Somados"/>)
	/// e o que torna isso possivel, e por isso ele vai pro disco junto.
	///
	/// CANAL SEPARADO do <c>Fighter.BuffsDeSkill</c>: aquele razao e da COMPRA e o
	/// <see cref="EfeitosDeSkill.Aplicar"/> o reescreve inteiro a cada chamada. Somar degrau la
	/// dentro faria a proxima compra de skill apagar o ganho de nivel de todo mundo.
	///
	/// Devolve quantos campos foram efetivamente mexidos.
	/// </summary>
	public int Aplicar(Fighter f)
	{
		var alvo = new Dictionary<string, double>(StringComparer.Ordinal);
		var chaves = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach ((string path, Progresso pr) in _p)
		{
			RegraDeNivel? r = RegrasDeNivel.Get(path);
			if (r == null) continue;
			foreach (Degrau d in r.Degraus)
			{
				if (d.Nivel > pr.Nivel) continue;
				foreach ((string campo, double v) in d.Buffs)
					alvo[campo] = alvo.GetValueOrDefault(campo) + v;

				// CHAVE SE ESCREVE, nao se acumula -- e por isso ela nao entra no razao
				// `_somados`: nao ha o que desfazer. Duas skills que ligam a mesma chave chegam
				// no mesmo estado, que e o que "ligado" quer dizer.
				foreach ((string campo, double v) in d.Flags) chaves[campo] = v;
			}
		}

		int mexidos = 0;
		foreach ((string campo, double v) in chaves)
			if (Escrever(f, campo, v)) mexidos++;
		foreach ((string campo, double antigo) in _somados)
			if (!alvo.ContainsKey(campo) && Somar(f, campo, -antigo)) mexidos++;

		foreach ((string campo, double v) in alvo)
		{
			double delta = v - _somados.GetValueOrDefault(campo);
			if (Math.Abs(delta) > 1e-9 && Somar(f, campo, delta)) mexidos++;
		}

		_somados = alvo;
		return mexidos;
	}

	private static readonly Dictionary<string, FieldInfo?> Cache = new(StringComparer.Ordinal);

	/// <summary>
	/// Reflexao pro campo do lutador. Copia curta do helper privado de
	/// <see cref="EfeitosDeSkill"/> -- vale a pena tornar aquele `internal` e apagar este; ate
	/// la, o canal de diagnostico e O MESMO (<see cref="EfeitosDeSkill.Desconhecidos"/>), pra
	/// que campo que falta apareca numa lista so em vez de duas.
	/// </summary>
	/// <summary>
	/// Soma num campo do lutador reusando o cache de reflexao de <see cref="EfeitosDeSkill"/>.
	///
	/// Havia um segundo cache aqui, com a mesma regra e o mesmo destino. Dois caches sobre o
	/// mesmo `Fighter` nao quebram nada hoje -- e amanha um deles ganha um tratamento que o
	/// outro nao tem, e o campo passa a valer coisas diferentes dependendo de quem somou.
	/// A lista de campos desconhecidos tambem tem que ser UMA so, senao o relatorio mente.
	/// </summary>
	private static bool Somar(Fighter f, string campo, double delta)
	{
		System.Reflection.FieldInfo? fi = EfeitosDeSkill.Campo(campo);
		if (fi == null) return false;
		fi.SetValue(f, (double)fi.GetValue(f)! + delta);
		return true;
	}

	/// <summary>
	/// ESCREVE uma chave (`campo = n`), em vez de somar. Idempotente por natureza: chamar dez
	/// vezes deixa o mesmo valor, entao nao ha razao a manter nem nada a desfazer no login.
	/// </summary>
	private static bool Escrever(Fighter f, string campo, double valor)
	{
		System.Reflection.FieldInfo? fi = EfeitosDeSkill.Campo(campo);
		if (fi == null) return false;
		if ((double)fi.GetValue(f)! == valor) return false;
		fi.SetValue(f, valor);
		return true;
	}


	// =====================================================================
	// DISCO
	// =====================================================================
	public NivelSave ParaSave()
	{
		var s = new NivelSave { Somados = new Dictionary<string, double>(_somados, StringComparer.Ordinal) };
		foreach ((string path, Progresso pr) in _p)
			if (pr.Nivel > 0 || pr.Exp > 0) s.Skills[path] = [pr.Nivel, pr.Exp];
		return s;
	}

	public void DoSave(NivelSave? s)
	{
		_p.Clear();
		_somados = new Dictionary<string, double>(StringComparer.Ordinal);
		if (s == null) return;
		foreach ((string path, double[] v) in s.Skills)
		{
			// SO ENTRA QUEM TEM REGRA. O save guarda o progresso de tudo que ja subiu; se uma
			// regra for retirada numa atualizacao (ou se o save vier de uma versao que tinha
			// mais), o path continua no disco. O `Efetor` faz `RegrasDeNivel.Get(path)!` --
			// aquele `!` vira NullReferenceException no primeiro tique depois do login, e o
			// laco de nivel morre pra AQUELE jogador sem barulho nenhum.
			//
			// Filtrar na ENTRADA, e nao no laco: assim o progresso orfao simplesmente nao
			// existe, em vez de existir e ter que ser desviado em todo lugar que o percorre.
			if (v.Length >= 2 && RegrasDeNivel.Get(path) != null)
				_p[path] = new Progresso { Nivel = (int)v[0], Exp = v[1] };
		}
		foreach ((string campo, double v) in s.Somados) _somados[campo] = v;
	}

	// =====================================================================
	// DIAGNOSTICO
	// =====================================================================
	/// <summary>
	/// SKILLS QUE DEVIAM SUBIR E NAO SOBEM: o catalogo diz `maxnivel > 1` e nao ha regra portada.
	///
	/// Mesmo espirito do <see cref="Skill.SemEfeito"/>: o buraco vira numero em vez de silencio.
	/// Uma skill de maxlevel 3 parada no nivel 0 pra sempre parece funcionar.
	/// </summary>
	public static IEnumerable<string> SemRegra(SkillCatalog cat) =>
		cat.Todas.Where(s => !s.Arvore && s.MaxNivel > 1 && RegrasDeNivel.Get(s.Path) == null)
			.Select(s => s.Path);
}

/// <summary>
/// AS REGRAS DE NIVEL, POR SKILL. Registro global -- e dado do jogo, igual pra todo personagem.
///
/// ISTO AINDA E SEMENTE, E ASSUMO. O certo e o `Tools/AssetPipeline` emitir `expbarrier` e os
/// degraus no `skills.json`, como ja faz com `buffs` e `verbos` -- sao 200+ skills com
/// `expbarrier` declarado no DM e transcrever isso a mao seria repetir o erro que o catalogo
/// extraido existe pra evitar. O que esta aqui sao as 24 skills que sobem por TEMPO
/// (`prob(50) exp++`), que e uma lista fechada e conferida uma a uma no DM: um grep exato, sem
/// ambiguidade de parser. As arvores de Ki (Mind, Effusion) sobem por EVENTO e entram por
/// <see cref="NiveisDeSkill.Creditar"/> quando o extrator trouxer os numeros delas.
///
/// Enquanto o extrator nao vem, <see cref="Registrar"/> e a porta: um lote extraido pode ser
/// despejado aqui no boot sem que uma linha deste arquivo mude.
/// </summary>
public static class RegrasDeNivel
{
	private static readonly Dictionary<string, RegraDeNivel> Mapa = new(StringComparer.OrdinalIgnoreCase);

	public static RegraDeNivel? Get(string path) => Mapa.GetValueOrDefault(path);
	public static int Total => Mapa.Count;

	public static void Registrar(RegraDeNivel r)
	{
		if (r.Path.Length > 0) Mapa[r.Path] = r;
	}

	/// <summary>Atalho do caso comum: sobe por tempo, barreira fixa, um verbo por degrau.</summary>
	private static void Tempo(string path, double barreira, params (int Nivel, string Verbo, string Aviso)[] degraus)
	{
		Registrar(new RegraDeNivel
		{
			Path = path,
			Barreira = barreira,
			GanhoPorTempo = true,
			Degraus = [.. degraus.Select(d => new Degrau { Nivel = d.Nivel, Verbos = [d.Verbo], Aviso = d.Aviso })],
		});
	}

	static RegrasDeNivel()
	{
		// =================================================================
		// AS 24 QUE SOBEM POR TEMPO.
		//
		// Todas tem a mesma forma no DM: `if(level < maxlevel) if(prob(50)) exp++` seguido de um
		// `switch(level)` que so faz `assignverb`. O `maxlevel` NAO esta repetido aqui de
		// proposito -- ele ja vem do catalogo extraido (`maxnivel`), e ter o numero em dois
		// lugares e ter dois numeros diferentes daqui a um ano.
		//
		// A barreira, sim, esta: o extrator ainda nao emite `expbarrier`. Onde o DM nao declara,
		// vale o padrao 100 do `/datum/skill` (skill.dm:9) -- e o que passo aqui explicitamente
		// pra que o numero apareca, em vez de sumir num default.
		// =================================================================

		// --- Corpo (Body.dm) ---
		// DEFEITO VIVO DO ORIGINAL, portado como esta: `grace` tem maxlevel = 1 (Body.dm:159) mas
		// o degrau esta no nivel 2 (Body.dm:182-185). Como o efetor so sobe `if(level < maxlevel)`
		// (skill.dm:92), o nivel 2 e inalcancavel e o Falling Kick e codigo morto no BYOND --
		// mesmo com o `before_forget()` se dando ao trabalho de remove-lo (Body.dm:177). Deixo o
		// degrau declarado: o dia que o maxlevel virar 2 no catalogo, o golpe acende sozinho.
		Tempo("/datum/skill/grace", 100,
			(2, "Falling_Kick", "voce coordena um chute basico: Falling Kick, que machuca mais quem voa."));

		Tempo("/datum/skill/MartialSkill/Kicker", 100,     // Body.dm:201
			(2, "Kickup", "seu chute derruba: Kickup atordoa quem esta no chao."),
			(3, "Dropkick", "Dropkick liberado -- voce avanca em linha reta com o golpe."));

		Tempo("/datum/skill/barrel", 100,                  // sem expbarrier no DM -> padrao
			(2, "Seismic_Press", "seu peso virou arma: Seismic Press."),
			(3, "Gigantic_Spike", "Gigantic Spike liberado."));

		Tempo("/datum/skill/MartialSkill/Beserker", 100,   // Body.dm:315
			(2, "Power_Drag", "Power Drag liberado."),
			(3, "Revenge_Demon", "Revenge Demon liberado."));

		Tempo("/datum/skill/boxing", 100,                  // sem expbarrier no DM -> padrao
			(2, "One_Two", "voce encadeia os punhos: One Two."));

		Tempo("/datum/skill/Small_Boxer", 100,             // Body.dm:445
			(2, "One_Two_Five", "One Two Five liberado."),
			(3, "Two_One_Four", "Two One Four liberado."));

		Tempo("/datum/skill/Bigtime_Boxer", 100,           // Body.dm:488
			(2, "KO_Punch", "KO Punch liberado."));

		// --- Assassino (Assassain.dm) -- todas expbarrier = 100, maxlevel = 2, um verbo no 2 ---
		Tempo("/datum/skill/Assassain/Precise_Arts", 100, (2, "Shock", "Shock liberado."));                      // :22
		Tempo("/datum/skill/Assassain/Reverbrate", 100, (2, "Reverb", "Reverb liberado."));                      // :53
		Tempo("/datum/skill/Assassain/Omae_Wa_Moe", 100, (2, "Precise_Explosion", "Precise Explosion liberado."));// :84
		Tempo("/datum/skill/Assassain/Hokuto_no_Shinken", 100,
			(2, "Hokuto_Hyakuretsu_Ken", "Hokuto Hyakuretsu Ken liberado."));                                    // :115
		Tempo("/datum/skill/Assassain/Cutthroat", 100, (2, "Cutthroat", "Cutthroat liberado."));                 // :150
		Tempo("/datum/skill/Assassain/Backstab", 100, (2, "Backstab", "Backstab liberado."));                    // :183
		Tempo("/datum/skill/Assassain/Sneak", 100, (2, "Sneak", "Sneak liberado."));                             // :214
		Tempo("/datum/skill/Assassain/Trip", 100, (2, "Trip", "Trip liberado."));                                // :245

		// --- Marcial (Martial Skill.dm) -- nenhuma declara expbarrier: padrao 100 (skill.dm:9) ---
		Tempo("/datum/skill/MartialSkill/Green_Dean", 100, (2, "Dash_Attack", "Dash Attack liberado."));          // :42-45
		Tempo("/datum/skill/MartialSkill/Catflex", 100, (2, "Flip", "Flip liberado."));                           // :76-79
		Tempo("/datum/skill/MartialSkill/Tiger_Paw", 100, (2, "Takedown", "Takedown liberado."));                  // :112-115
		Tempo("/datum/skill/MartialSkill/Dragon_Sweep", 100, (2, "Stun_Attack", "Stun Attack liberado."));         // :146-149
		Tempo("/datum/skill/MartialSkill/Reflexes_Training", 100, (2, "Spin_Attack", "Spin Attack liberado."));    // :182-185

		// --- Luta livre (Wrestling.dm) -- todas expbarrier = 100, maxlevel = 2 ---
		Tempo("/datum/skill/Wrestling/Grabber", 100, (2, "Hold", "Hold liberado."));          // :20
		Tempo("/datum/skill/Wrestling/Superstar", 100, (2, "Clench", "Clench liberado."));    // :53
		Tempo("/datum/skill/Wrestling/Rasslin", 100, (2, "Power_Slam", "Power Slam liberado."));// :84
		Tempo("/datum/skill/Wrestling/Main_Event", 100, (2, "Suplex", "Suplex liberado."));   // :115
	}
}
