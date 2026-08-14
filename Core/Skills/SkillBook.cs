namespace Jandirus.Core.Skills;

/// <summary>Por que uma skill nao pode ser aprendida agora. Vazio = pode.</summary>
public enum Recusa
{
	Pode = 0,
	NaoExiste,
	JaSabe,
	Desligada,
	SoVilao,
	RacaOuClasse,
	SemArvore,
	FaltaPreRequisito,
	SemMarcos,
}

/// <summary>
/// O QUE ESTE PERSONAGEM APRENDEU, e quanto ainda pode gastar.
///
/// Vive no CORE porque as duas pontas precisam da mesma resposta: o cliente pra pintar o botao
/// de "aprender" (e nao prometer o que o servidor vai recusar) e o servidor pra decidir de
/// verdade. Sao as mesmas regras, na mesma funcao -- e o que evita duas implementacoes
/// divergirem em silencio.
///
/// MARCOS SAO A MOEDA. O original chama de `skillpoints` e mantem DOIS contadores: quantos voce
/// ja ganhou na vida e quantos ainda tem pra gastar. Os dois importam -- o total e progressao
/// (nao volta atras), o saldo e escolha.
/// </summary>
public sealed class SkillBook
{
	private readonly HashSet<string> _aprendidas = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// O QUE ALGUEM ME ENSINOU (subconjunto de <see cref="_aprendidas"/>).
	///
	/// ============================ UM CONJUNTO SO PRA OS DOIS BITS DO DM ============================
	/// O original tem DUAS variaveis por skill, e elas so aparecem juntas, nas duas linhas seguidas
	/// que o `Study` escreve (`teachable.dm:53-54`):
	///
	///     nS.teacher   = FALSE   // o aluno NAO repassa
	///     nS.wastaught = TRUE    // e pode esquecer o que foi ensinado
	///
	/// Nunca uma sem a outra, em lugar nenhum do jogo. Sao portanto **a mesma informacao vista de
	/// dois lados**: "esta copia veio de um mestre". Guardar dois campos aqui seria a mesma verdade
	/// escrita duas vezes, com uma delas fadada a envelhecer -- e a pergunta que o jogo faz e sempre
	/// a mesma (`FoiEnsinada`), com o "nao repassa" derivado dela em
	/// <see cref="EnsinoDeSkill.PodeRepassar"/>.
	///
	/// **E ISTO E O QUE IMPEDE UMA SKILL RARA DE VIRAR CORRENTE.** Sem esta marca, o primeiro
	/// Kamehameha ensinado se espalharia pelo servidor inteiro em uma tarde.
	/// ============================================================================================
	/// </summary>
	private readonly HashSet<string> _ensinadas = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Quantos marcos este personagem JA GANHOU na vida. So sobe.</summary>
	public int MarcosTotais;

	/// <summary>Quantos ainda estao pra gastar.</summary>
	public int MarcosLivres;

	public IReadOnlyCollection<string> Aprendidas => _aprendidas;
	public bool Sabe(string path) => _aprendidas.Contains(path);

	public void Conceder(int marcos)
	{
		if (marcos <= 0) return;
		MarcosTotais += marcos;
		MarcosLivres += marcos;
	}

	/// <summary>Aprende sem cobrar nada. E o caminho de quem foi ENSINADO, nao de quem comprou.</summary>
	public void Dar(string path) => _aprendidas.Add(path);

	/// <summary>
	/// APRENDEU PORQUE ALGUEM ENSINOU -- as duas linhas do `Study` (`teachable.dm:53-54`) numa so.
	///
	/// Existe separada do <see cref="Dar"/> porque `Dar` tem outros seis chamadores (a concessao do
	/// admin, o Ki liberado, os moldes de NPC, a bancada) e nenhum deles e ensino: marcar todos
	/// eles como ensinados travaria o repasse de skill que ninguem ensinou a ninguem.
	/// </summary>
	public void DarComoEnsinada(string path)
	{
		_aprendidas.Add(path);
		_ensinadas.Add(path);
	}

	/// <summary>Esta copia veio de um mestre? (o `wastaught` do DM.)</summary>
	public bool FoiEnsinada(string path) => _ensinadas.Contains(path);

	/// <summary>As que vieram de mestre. So o save le isto.</summary>
	public IReadOnlyCollection<string> Ensinadas => _ensinadas;

	/// <summary>
	/// ESQUECE. **Tira das duas listas** -- e a segunda linha nao e zelo: o DM DELETA o datum
	/// (`skill.dm:79`), e com ele o `wastaught`. Deixar a marca pra tras faria a skill voltar
	/// "ensinada" no dia em que a pessoa a comprasse de verdade com os proprios marcos, e ela
	/// continuaria sem poder repassar uma coisa que ninguem lhe deu.
	/// </summary>
	public void Esquecer(string path)
	{
		_aprendidas.Remove(path);
		_ensinadas.Remove(path);
		// A CASA ESCOLHIDA MORRE JUNTO, pelo mesmo motivo do `wastaught`: guardar a escolha de uma
		// skill esquecida faria os buffs dela voltarem sozinhos no dia em que alguem a comprasse
		// de novo, sem passar pela pergunta.
		_escolhas.Remove(path);
	}

	public void Carregar(IEnumerable<string> paths)
	{
		_aprendidas.Clear();
		foreach (string p in paths) _aprendidas.Add(p);
	}

	// =====================================================================
	// A ESCOLHA UNICA
	// =====================================================================
	private readonly Dictionary<string, int> _escolhas = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// QUAL CASA o dono escolheu em cada skill de escolha unica (1-based, como o `chosen` do DM).
	///
	/// UMA SKILL NO JOGO usa isto -- a `Great Robotic Alliance` (`meta.dm:104-125`). Ela nao mora
	/// nas <see cref="Aprendidas"/> porque nao e "sabe / nao sabe": e um dado A MAIS sobre uma
	/// skill que ele ja sabe, do mesmo jeito que <see cref="Ensinadas"/> e.
	///
	/// SEM ESCOLHA REGISTRADA A SKILL NAO RENDE NADA, e isso e fiel: no DM os buffs moram dentro
	/// do `switch(input(...))` e sem resposta nenhuma casa entra.
	/// </summary>
	public IReadOnlyDictionary<string, int> Escolhas => _escolhas;

	/// <summary>Registra a casa escolhida. Devolve false se a skill nem e de escolha unica.</summary>
	public bool Escolher(SkillCatalog cat, string path, int casa)
	{
		Skill? s = cat.Get(path);
		if (s == null || s.Escolhas.Length == 0) return false;
		if (casa < 1 || casa > s.Escolhas.Length) return false;
		if (!_aprendidas.Contains(path)) return false;
		_escolhas[path] = casa;
		return true;
	}

	/// <summary>
	/// Le do save. **DEPOIS do <see cref="Carregar"/>**, pelo mesmo motivo do
	/// <see cref="CarregarEnsinadas"/>: escolha de skill que o livro nao tem mais e escolha orfa,
	/// e ela reapareceria como buff de uma skill esquecida.
	/// </summary>
	public void CarregarEscolhas(IEnumerable<KeyValuePair<string, int>> pares)
	{
		_escolhas.Clear();
		foreach ((string p, int c) in pares) if (_aprendidas.Contains(p)) _escolhas[p] = c;
	}

	/// <summary>
	/// Le do save quais copias vieram de mestre. **Chamada DEPOIS do <see cref="Carregar"/>**, e o
	/// filtro existe porque um save pode ter perdido a skill sem perder a marca (skill removida do
	/// catalogo, admin que apagou): marca orfa faria o livro achar que ele "foi ensinado" numa
	/// coisa que ele nem sabe.
	/// </summary>
	public void CarregarEnsinadas(IEnumerable<string> paths)
	{
		_ensinadas.Clear();
		foreach (string p in paths) if (_aprendidas.Contains(p)) _ensinadas.Add(p);
	}

	/// <summary>
	/// Pode aprender ESTA skill AGORA? A ordem das checagens e a do original, e ela importa pra
	/// mensagem que o jogador le: dizer "faltam marcos" pra uma skill que a raca dele nunca vai
	/// poder aprender manda a pessoa juntar marcos a toa.
	/// </summary>
	public Recusa PodeAprender(SkillCatalog cat, string path, string raca, string classe, bool vilao)
	{
		Skill? s = cat.Get(path);
		if (s == null || s.Arvore) return Recusa.NaoExiste;
		if (Sabe(path)) return Recusa.JaSabe;
		if (!s.Ligada) return Recusa.Desligada;
		if (s.SoVilao && !vilao) return Recusa.SoVilao;
		if (!cat.Permitida(s, raca, classe, vilao)) return Recusa.RacaOuClasse;

		// A ARVORE E O GATE DE VERDADE. No original a skill nao esta solta no mundo: ela pende
		// de uma arvore, e a arvore vem da raca (`generatetrees`). Sem esta checagem, um humano
		// compraria a regeneracao namekuseijin so por ela nao declarar restricao de raca.
		if (!PenduraEmArvoreDe(cat, s, raca, classe)) return Recusa.SemArvore;

		if (!SkillCatalog.PreReqsOk(s, _aprendidas)) return Recusa.FaltaPreRequisito;
		if (MarcosLivres < SkillCatalog.CustoDe(s)) return Recusa.SemMarcos;
		return Recusa.Pode;
	}

	/// <summary>Compra a skill. Devolve o motivo da recusa quando nao da.</summary>
	public Recusa Aprender(SkillCatalog cat, string path, string raca, string classe, bool vilao)
	{
		Recusa r = PodeAprender(cat, path, raca, classe, vilao);
		if (r != Recusa.Pode) return r;
		MarcosLivres -= SkillCatalog.CustoDe(cat.Get(path)!);
		_aprendidas.Add(path);
		return Recusa.Pode;
	}

	/// <summary>
	/// ARVORES DESTRAVADAS PELO PROGRESSO -- as que nao vem da raca nem da classe, e sim do que
	/// voce ja aprendeu. E o `enabletree()` do `growbranches()` do original.
	///
	/// Sem isto o catalogo era so estatico: as arvores que voce tem no nascimento, e mais nada
	/// pra sempre. Metade da progressao fisica do jogo (Bodybuilding, Martial Skill, Weapons
	/// Expert, Cultivation) fica ATRAS desta porta e ficava inalcancavel.
	/// </summary>
	public HashSet<string> Destravadas { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// RECALCULA QUE ARVORES O PROGRESSO ABRIU. Copia literal de `Body.dm:24-32`.
	///
	/// Os dois contadores (`bodyskill`, `bodyreadiness`) sobem 1 por skill de base do corpo
	/// aprendida -- sao, portanto, uma medida de QUANTO do tronco voce ja percorreu, e o que
	/// eles abrem e o galho especializado. Repare que Cultivation pede os DOIS acima de 2:
	/// e a arvore de quem treinou corpo e tecnica, nao de quem foi fundo num so.
	///
	/// Chamado depois de qualquer mudanca no livro; e idempotente (so acrescenta).
	/// </summary>
	public void RecalcularDestravadas(double bodyskill, double bodyreadiness, bool temArma)
	{
		if (bodyreadiness > 2) Destravadas.Add("/datum/skill/tree/Bodybuilding");
		if (bodyskill > 2)
		{
			Destravadas.Add("/datum/skill/tree/MartialSkill");
			if (temArma) Destravadas.Add("/datum/skill/tree/WeaponsExpert");
		}
		if (bodyskill >= 2 && bodyreadiness >= 2) Destravadas.Add("/datum/skill/tree/Cultivation");
	}

	/// <summary>Esta skill pende de alguma arvore que este personagem possui?</summary>
	public bool PenduraEmArvoreDe(SkillCatalog cat, Skill s, string raca, string classe)
	{
		foreach (Skill arv in cat.ArvoresDe(raca, classe))
			if (Array.IndexOf(arv.Galhos, s.Path) >= 0) return true;

		foreach (string p in Destravadas)
			if (cat.Get(p) is { } arv && Array.IndexOf(arv.Galhos, s.Path) >= 0) return true;

		// Skill SOLTA (nao pendurada em arvore nenhuma do catalogo) e ensinada, nao comprada:
		// Kaio-ken vem do Senhor Kaioh, a Genkidama tambem. Se ninguem a pendurou, so o
		// caminho do ensino a concede -- e `Dar()` nao passa por aqui.
		return false;
	}

	/// <summary>O que da pra comprar agora, em ordem de arvore e tier. E o que a aba mostra.</summary>
	public IEnumerable<(Skill Skill, Recusa Estado)> Ofertas(SkillCatalog cat, string raca, string classe, bool vilao)
	{
		var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		IEnumerable<Skill> arvores = cat.ArvoresDe(raca, classe)
			.Concat(Destravadas.Select(cat.Get).Where(a => a != null)!);

		foreach (Skill arv in arvores)
			foreach (string p in arv.Galhos)
			{
				Skill? s = cat.Get(p);
				if (s == null || s.Nome.Length == 0 || !vistas.Add(p)) continue;
				yield return (s, PodeAprender(cat, p, raca, classe, vilao));
			}
	}
}
