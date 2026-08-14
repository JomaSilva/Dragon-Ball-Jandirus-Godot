using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Core.Npc;

/// <summary>O que o sorteio devolve: os MESMOS quatro objetos que um jogador tem, mais o papel.</summary>
public readonly record struct FichaSorteada(
	Fighter Ficha, SkillBook Livro, NiveisDeSkill Niveis, PapelDeNpc Papel,
	string Nome, string Genero, string Raca, string Classe);

/// <summary>
/// O SORTEIO DA FICHA DE UM NPC -- de uma semente e um molde sai um lutador completo.
///
/// ============================ A COERENCIA E CONSEQUENCIA, NAO CONFERENCIA ============================
/// O jeito errado de fazer isto e sortear cada coisa por conta: um BP, umas skills de uma lista, umas
/// formas de outra. Sai o boneco que o dono descreveu -- *"um NPC com BP de SSJ3 e sem nenhuma skill
/// de Ki"* -- e sai tambem o oposto, um bicho de 2 mil de BP sabendo Kaio-ken, porque nada liga um
/// sorteio ao outro.
///
/// Aqui os quatro sorteios sao um FUNIL, e cada elo passa pela porta de producao:
///
///   raca/classe  ->  `Birth.Nascer`         a porta unica de nascimento (Birth.cs:64)
///   BP           ->  a faixa do molde, ou a media do servidor (o `NPCTicker`, NPClist.dm:72)
///   marcos       ->  uma funcao do BP -- e o unico numero inventado aqui, e ele e dado (`npcs.json`)
///   skills       ->  `SkillBook.Aprender`   com gate de raca, de ARVORE, pre-requisito e custo
///   niveis       ->  `NiveisDeSkill.Por`    o degrau que abre capacidade (voar pede 50)
///   formas       ->  `EstadoDeForma.Proxima` o MESMO seletor da tecla C
///
/// Ou seja: **um NPC nunca sabe o que um jogador daquela raca com aquele orcamento nao poderia
/// saber, e nunca tem uma forma que aquele BP nao abriria.** Nao ha nenhuma checagem de coerencia
/// escrita -- a coerencia e o que sobra depois de passar pelos mesmos funis.
/// ================================================================================================
///
/// ============================ NADA AQUI LE UM `Random` GLOBAL ============================
/// A semente e ARGUMENTO, e cada sorteio tem gerador proprio derivado de (semente + nome do campo).
/// E o mesmo desenho do <see cref="LimiaresPessoais"/>, pelas mesmas duas razoes que ja custaram
/// caro naquele arquivo: um `Random` compartilhado faz o resultado depender da ORDEM das chamadas
/// (acrescentar um sorteio novo no meio mudaria todos os seguintes) e `GetHashCode()` de string e
/// randomizado por processo (o mesmo NPC daria numeros diferentes a cada reinicio).
///
/// Consequencia direta, e e a resposta a pergunta do determinismo: **dois servidores com a mesma
/// semente produzem o mesmo NPC** -- mesma raca, mesma classe, mesmo BP, mesmas skills, mesmas
/// formas. A UNICA excecao e o molde que pede <see cref="MoldeDeNpc.BpRelativo"/>, que le a media
/// do servidor de proposito (e a dificuldade seguindo os jogadores, como no original).
/// ====================================================================================
/// </summary>
public static class SorteioDeNpc
{
	/// <summary>
	/// A SEMENTE DE UM NPC. Tres coisas: o universo, a receita e o LUGAR onde ele nasce.
	///
	/// Com as tres, o segundo saibaman do ponto de spawn 4 de Vegeta e sempre o mesmo saibaman --
	/// em qualquer maquina, em qualquer reinicio. Sem a terceira, todos os saibamen do servidor
	/// seriam clones.
	/// </summary>
	public static ulong SementeDe(ulong seedDoUniverso, string moldeId, ulong lugar) =>
		Espaco.Misturar(seedDoUniverso, Espaco.Hash64(moldeId), lugar);

	/// <summary>
	/// MONTA A FICHA. `mediaDoServidor` so e lido por molde com <see cref="MoldeDeNpc.BpRelativo"/>.
	///
	/// `skills` pode ser nulo (servidor sem `skills.json`): o NPC nasce sem livro, como um jogador
	/// nasceria -- e o aviso ja e dado no carregamento, nao aqui.
	/// </summary>
	public static FichaSorteada Sortear(MoldeDeNpc molde, ulong semente, RaceCatalog racas,
										SkillCatalog? skills, string planeta, double mediaDoServidor)
	{
		// --- 1. QUEM ELE E -------------------------------------------------
		// ============================ A POOL VAZIA SAI DO BERCO, E NAO DO MENU ============================
		// Pool vazia = **as racas cujo BERCO e este planeta** -- a `PlanetaNatal` lida ao contrario,
		// que e a MESMA regra que decide onde o jogador nasce.
		//
		// Isto era `CharacterDraft.RacasDoPlaneta(planeta)`, a tabela do MENU DE CRIACAO, e a troca
		// conserta um defeito de verdade: aquela tabela **nao e** a inversa do berco (ela poe Icer em
		// Vegeta e Arlian/Makyo em Namek, divergencias que o cabecalho da `PlanetaNatal` documenta),
		// entao um cidadao de Vegeta podia nascer Frost Demon -- pela regra do menu -- enquanto o
		// jogador Frost Demon nasce em Icer pela regra do berco. Duas respostas pra "onde esta raca
		// mora", e o NPC usando a errada.
		//
		// Ela tambem devolvia `[]` pra qualquer planeta fora dos cinco do menu, e o `Escolher` caia no
		// padrao "Human": um cidadao de Icer nasceria humano por acidente, calado.
		// ============================================================================================
		string[] pool = molde.Racas.Length > 0
			? molde.Racas
			: Bercos.RacasNascidasEm(planeta, racas.Protos.Keys);

		// SEM NINGUEM PRA NASCER ALI, NAO SE INVENTA NINGUEM. `Escolher` cairia no "Human" e o
		// planeta ganharia habitantes humanos sem que nada no dado dissesse isso -- a armadilha 5 da
		// PARTE 3 ("silencio no lugar de erro") na forma mais cara: dado errado virando jogo plausivel.
		if (pool.Length == 0)
			throw new InvalidOperationException(
				$"molde '{molde.Id}': nenhuma raca tem berco em '{planeta}' e o molde nao declara "
				+ "'racas'. Ou o planeta esta errado no povoamento, ou o molde precisa da lista.");

		string raca = Escolher(pool, semente, "raca", "Human");
		string genero = Escolher(molde.Generos, semente, "genero", "Male");

		// A CLASSE CRAVADA PODE SER UMA LINHAGEM, e as duas entram por portas diferentes do
		// `Birth`: linhagem e escolha do jogador (e o `Birth.RollClass` a converte em classe),
		// classe e resultado. Distinguir pela propria tabela da criacao evita um campo "ehLinhagem"
		// no molde que alguem preencheria errado.
		bool ehLinhagem = Array.Exists(CharacterDraft.EscolhasDeClasse(raca),
			c => string.Equals(c, molde.Classe, StringComparison.OrdinalIgnoreCase));
		string linhagem = ehLinhagem ? molde.Classe : "";
		string classeForcada = !ehLinhagem ? molde.Classe : "";

		// A POOL DA RACA VEM PRIMEIRO -- e o `npc_random_name(race)`. Ver `MoldeDeNpc.NomesPorRaca`
		// pro motivo de o cidadao precisar disto e o inimigo nao.
		string[] poolDeNomes = molde.NomesPorRaca.TryGetValue(raca, out string[]? pr) && pr.Length > 0
			? pr : molde.Nomes;

		string nome = poolDeNomes.Length > 0
			? Escolher(poolDeNomes, semente, "nome", molde.Nome)
			: molde.Nome.Length > 0 ? molde.Nome : raca;

		Fighter f = Birth.Nascer(racas, raca, linhagem, Sorteador(semente, "classe"), nome,
								 classeForcada: classeForcada);

		// --- 2. A IDADE, NO PLATO EM QUE ELA NAO COBRA NADA -----------------
		// `DivisorDeIdade` vale exatamente 1 entre a idade adulta e o auge da raca
		// (Envelhecimento.cs:130-140). Sortear DENTRO desse plato da variedade de ficha sem mexer
		// no poder -- o BP do molde e o BP que o jogador vai sentir.
		//
		// Fora dele o NPC bateria com ate 3/4 do que a ficha promete, e o dono leria isso como
		// "o Freeza de 530 mil bate como 400 mil" sem nada na tela explicando.
		double auge = Envelhecimento.AugeDaRaca(raca);
		f.Idade = auge <= Envelhecimento.IdadeAdulta
			? Envelhecimento.IdadeAdulta
			: Sorteador(semente, "idade").Next((int)Envelhecimento.IdadeAdulta, (int)auge + 1);

		// --- 3. O PODER ----------------------------------------------------
		f.BP = SortearBp(molde, semente, mediaDoServidor);

		// --- 4. O QUE ELE APRENDEU -----------------------------------------
		var livro = new SkillBook();
		var niveis = new NiveisDeSkill();
		if (skills != null)
		{
			livro.Conceder(MarcosDe(molde, f.BP));
			GastarOsMarcos(livro, skills, f, raca, f.Class, semente, molde.Vocacao);

			// ENSINADAS, NAO COMPRADAS. `Dar` nao passa por gate nenhum de proposito -- e o caminho
			// do Kaio-ken e da Genkidama, que sao skills SOLTAS (nao pendem de arvore: ver
			// SkillBook.cs:134) e por isso nunca poderiam ser compradas por ninguem.
			foreach (string p in molde.Dadas) livro.Dar(p);

			EfeitosDeSkill.Aplicar(f, skills, livro.Aprendidas);

			// O DEGRAU EM QUE AS SKILLS NASCEM. Nao e enfeite: `Ki_Unlocked` no nivel 50 e a porta
			// do voo (GameServer.Voo.cs:118). Um NPC com a skill no nivel 1 tem a skill e nao sai
			// do chao -- e o jogador leria isso como uma IA que "nao sabe voar".
			if (molde.NivelDasSkills > 0)
				foreach (string p in livro.Aprendidas) niveis.Por(p, (int)molde.NivelDasSkills);
			foreach ((string p, double n) in molde.Niveis) niveis.Por(p, (int)n);
			niveis.Aplicar(f);
		}

		// --- 5. FECHAR A FICHA ---------------------------------------------
		Normalizar(f);

		var papel = new PapelDeNpc(molde, semente);
		return new FichaSorteada(f, livro, niveis, papel, nome, genero, raca, f.Class);
	}

	// =====================================================================
	// O PODER
	// =====================================================================
	private static double SortearBp(MoldeDeNpc molde, ulong semente, double mediaDoServidor)
	{
		// O CHEFE NAO SORTEIA: o BP dele e o primeiro degrau da ficha pronta. E a diferenca que o
		// comentario do `BossEvents.dm:181` resume -- *"BP anunciado = BP real"*.
		if (molde.Estagios.Length > 0) return molde.Estagios[0].Bp;

		Random r = Sorteador(semente, "bp");

		if (molde.BpRelativo > 0)
		{
			// O `NPCTicker` base (NPClist.dm:72) escala o bicho pra media do servidor. A variacao de
			// +-15% e o `rand()` daquele proc: sem ela, vinte inimigos spawnados no mesmo minuto
			// teriam o MESMO BP ate o ultimo digito, e isso se ve.
			double bp = mediaDoServidor * molde.BpRelativo * (0.85 + r.Next(0, 301) / 1000.0);
			return Math.Max(bp, molde.BpPiso);
		}

		double lo = Math.Max(molde.BpMin, 1);
		double hi = Math.Max(molde.BpMax, lo);
		// `rand(lo,hi)` do DM e LINEAR (`rand(2000,5000)`, PlanetPopulation.dm:363). Uma faixa
		// log-uniforme daria uma distribuicao mais bonita e outro jogo: a maioria dos bichos cairia
		// no fundo da faixa.
		return lo + (hi - lo) * r.NextDouble();
	}

	/// <summary>
	/// O ORCAMENTO DE MARCOS. Base + um tanto por POTENCIA DE DEZ de BP.
	///
	/// E o unico numero deste arquivo que nao vem de uma funcao de producao, e por isso ele mora no
	/// `npcs.json` e nao aqui -- o dono afina a pericia dos inimigos sem recompilar. Ver
	/// <see cref="MoldeDeNpc.MarcosPorDecada"/> pro que acontece quando o jogador tambem ganhar
	/// marcos por progresso.
	/// </summary>
	public static int MarcosDe(MoldeDeNpc molde, double bp) =>
		Math.Max(0, molde.MarcosBase + (int)(molde.MarcosPorDecada * Math.Log10(Math.Max(bp, 1))));

	// =====================================================================
	// AS SKILLS -- gastar o orcamento pelas portas do jogador
	// =====================================================================
	/// <summary>
	/// COMPRA ATE NAO DAR MAIS. Cada compra e um <see cref="SkillBook.Aprender"/> -- a MESMA
	/// funcao que o botao da aba de aprendizado chama, com os mesmos seis motivos de recusa.
	///
	/// ============================ POR QUE EM VOLTAS, E NAO NUMA LISTA SO ============================
	/// Porque comprar MUDA o que da pra comprar. Metade da progressao fisica do jogo esta atras de
	/// arvores que so abrem quando os contadores `bodyskill`/`bodyreadiness` sobem
	/// (SkillBook.cs:114-123, o `growbranches()` do original), e esses contadores sao efeito das
	/// skills ja compradas. Uma lista montada de uma vez so nunca alcancaria Martial Skill nem
	/// Cultivation -- e ninguem notaria, porque o NPC nasceria com skills, so que sempre as mesmas.
	/// =========================================================================================
	/// </summary>
	private static void GastarOsMarcos(SkillBook livro, SkillCatalog cat, Fighter f,
									   string raca, string classe, ulong semente, string[] vocacao)
	{
		for (int volta = 0; volta < 200 && livro.MarcosLivres > 0; volta++)
		{
			// materializa: `Aprender` mexe no conjunto que `Ofertas` esta percorrendo
			List<Skill> podem = [.. livro.Ofertas(cat, raca, classe, vilao: false)
				.Where(o => o.Estado == Recusa.Pode && SkillCatalog.CustoDe(o.Skill) <= livro.MarcosLivres)
				.Select(o => o.Skill)];

			if (podem.Count == 0) break;

			// A ORDEM E A VOCACAO, depois o tier (o tronco antes do galho), depois um desempate
			// DETERMINISTICO por (semente, path) -- que e o que faz dois saibamen do mesmo molde
			// terem kits parecidos e nao identicos.
			Skill escolhida = podem
				.OrderBy(s => IndiceDaVocacao(cat, s.Path, vocacao))
				.ThenBy(s => s.Tier)
				.ThenBy(s => Espaco.Misturar(semente, Espaco.Hash64(s.Path), 0x5343_4B4C_5F4E_5043UL))
				.First();

			if (livro.Aprender(cat, escolhida.Path, raca, classe, vilao: false) != Recusa.Pode) break;

			// os contadores de arvore sao efeito de skill: sem reaplicar, a arvore nova so
			// apareceria na volta seguinte a proxima compra
			EfeitosDeSkill.Aplicar(f, cat, livro.Aprendidas);
			livro.RecalcularDestravadas(f.bodyskill, f.bodyreadiness, f.weaponeq > 0);
		}
	}

	/// <summary>
	/// Em que posicao da vocacao esta skill cai. Fora dela = por ultimo.
	///
	/// ============================ A VOCACAO E POR ARVORE, E NAO SO POR PREFIXO ============================
	/// A primeira versao daqui casava por prefixo de typepath e nao selecionava NADA -- e a bancada
	/// pegou pelo efeito, nao pela leitura: nenhum NPC destravava arvore por progresso.
	///
	/// O motivo e que as skills de corpo do jogo nao moram sob um prefixo comum: `blocking`,
	/// `training`, `grace` e `bulk` estao TODAS soltas em `/datum/skill/<nome>`. O que as junta e a
	/// ARVORE que as pendura (`/datum/skill/tree/Body`), que e o mesmo gate que o `SkillBook` ja usa
	/// pra decidir quem pode comprar o que. Escrever a vocacao com o vocabulario do proprio jogo era
	/// a saida; casar por texto era a ilusao dela.
	///
	/// O prefixo continua valendo pra quem quer apontar uma skill (ou um ramo) exato --
	/// `/datum/skill/mind/Ki_Unlocked` nao e arvore nenhuma.
	/// ==================================================================================================
	/// </summary>
	private static int IndiceDaVocacao(SkillCatalog cat, string path, string[] vocacao)
	{
		for (int i = 0; i < vocacao.Length; i++)
		{
			Skill? alvo = cat.Get(vocacao[i]);
			if (alvo is { Arvore: true })
			{
				if (Array.Exists(alvo.Galhos, g => string.Equals(g, path, StringComparison.OrdinalIgnoreCase)))
					return i;
				continue;
			}
			if (path.StartsWith(vocacao[i], StringComparison.OrdinalIgnoreCase)) return i;
		}
		return int.MaxValue;
	}

	// =====================================================================
	// AS FORMAS
	// =====================================================================
	/// <summary>
	/// LIBERA A ESCADA DESTE NPC. Chamada pelo servidor DEPOIS do corpo existir, porque o
	/// <see cref="PerfilDeFormas"/> e derivado dele -- e `Perfil()` e o funil unico dos gates
	/// (GameServer.Formas.cs:37). Montar um perfil aqui seria a segunda copia, e a segunda copia
	/// e a que discorda um dia.
	///
	/// ============================ SOBE E DESCE, E NAO "ESCREVE AS FORMAS" ============================
	/// Ela ENTRA em cada degrau (o que LIBERA a forma e consome a estreia da cinematica) e volta pra
	/// base no fim. Poderia escrever direto no `Liberadas`; nao escreve, porque `Proxima()` e o
	/// mesmo seletor da tecla C e ele SO oferece o que `Avaliar()` aprova -- BP, maestria, degrau
	/// anterior, linha aberta, ki divino. Andando a escada de verdade, um NPC nunca tem uma forma
	/// que um jogador daquele BP nao teria.
	///
	/// A MAESTRIA E POSTA ANTES DE CADA PASSO porque ela E o que abre o degrau seguinte
	/// (`PedeMaestria`, EstadoDeForma.cs:262). Sem ela a escada para no SSJ1 pra sempre.
	/// ============================================================================================
	/// </summary>
	public static int AbrirFormas(EstadoDeForma est, MoldeDeNpc molde, double bpBase, PerfilDeFormas perfil)
	{
		int abertas = 0;

		// as cravadas primeiro: o chefe que precisa de uma forma que a escada nao daria
		foreach (string id in molde.Formas)
		{
			if (Catalogo.Def(id) == null) continue;
			est.Entrar(id);
			est.Maestria.Por(id, molde.Maestria);
			abertas++;
		}

		if (molde.EscadaAutomatica)
			// teto de voltas: a escada mais longa do jogo tem menos de dez degraus, e um `while`
			// sem teto num catalogo mal editado seria um servidor travado no boot
			for (int i = 0; i < 40; i++)
			{
				FormaDef? d = est.Proxima(bpBase, perfil) ?? DespertarNaBiografia(est, bpBase, perfil);
				if (d == null) break;
				est.Entrar(d.Id);
				est.Maestria.Por(d.Id, molde.Maestria);
				abertas++;
			}

		// ELE COMECA NA BASE. Ter a forma e estar nela sao coisas diferentes -- e a diferenca e o
		// que o dono pediu pro Freeza de Vegeta.
		est.Entrar(Catalogo.IdBase);
		return abertas;
	}

	/// <summary>
	/// ============================ A FICHA E BIOGRAFIA, E A RAIVA E UM MOMENTO ============================
	/// A escada automatica travava no primeiro degrau de TODO Saiyajin, e a bancada pegou: com 1e13 de
	/// BP, zero formas abertas.
	///
	/// A razao e legitima e nao e um bug do gate: o despertar do tronco Saiyajin cobra FURIA
	/// (`RaivaExigida`, Formas.cs:2141 -- e a regra do dono, ver amigo morto/nocauteado), e um NPC
	/// recem-sorteado nunca esta em luto. O gate estava certo; a pergunta e que estava errada.
	///
	/// Um NPC nao esta despertando AGORA -- ele **ja despertou em algum momento do passado dele**, e a
	/// ficha esta contando a vida que ele teve, nao o instante em que nasceu. Entao o que se concede
	/// aqui e exatamente o que o `Liberadas` significa no jogador: o `hasbeast` do original, pago uma
	/// vez e nunca mais (ver o passo 9 do `EstadoDeForma.Avaliar`, que explica por que cobrar a raiva
	/// toda vez seria outra regra, e pior).
	///
	/// **SO A RAIVA E CONCEDIDA.** Poder, maestria, degrau anterior, linha aberta, linhagem, classe,
	/// ki divino e energia continuam sendo cobrados pelo `Avaliar` normal -- se a recusa for qualquer
	/// outra, esta funcao devolve nulo e a escada para ali. E o unico gate que se perdoa e o unico que
	/// mede um ESTADO EMOCIONAL DE AGORA em vez de uma conquista.
	///
	/// E quem NAO quer isso ja tem como dizer: `escadaAutomatica: false`. Um saibaman nunca despertou
	/// nada, e o molde dele diz isso em uma linha -- nao precisa de um campo novo.
	/// ==================================================================================================
	/// </summary>
	private static FormaDef? DespertarNaBiografia(EstadoDeForma est, double bpBase, PerfilDeFormas perfil)
	{
		FormaDef? melhor = null;
		double melhorMult = 0;

		foreach (FormaDef d in Catalogo.Todas)
		{
			if (d.Id == est.Atual || d.Id == Catalogo.IdBase) continue;
			if (est.Avaliar(d.Id, bpBase, kiFracao: 1, caido: false, perfil) != RecusaForma.SemFuria) continue;

			double m = Catalogo.Multiplicador(d.Id, est.Maestria, perfil, est.CombateSegundos);
			if (m <= melhorMult) continue;
			melhorMult = m;
			melhor = d;
		}

		// LIBERAR ja e o bastante: com a forma em `Liberadas`, o proprio `Avaliar` para de cobrar a
		// furia (`!Despertou(d.Id)`) e o degrau passa a sair pelo caminho normal.
		if (melhor != null) est.Liberar(melhor.Id);
		return melhor;
	}

	// =====================================================================
	// A NORMALIZACAO -- sem ela o poder ANUNCIADO nao e o poder SENTIDO
	// =====================================================================
	/// <summary>
	/// Poe o corpo no estado que o `powerlevel()` chama de "inteiro", pra o BP expresso bater com o
	/// BP da ficha.
	///
	/// E a normalizacao do `init_citizen` (PlanetPopulation.dm:324-335) e do `bev_pin_bp`
	/// (BossEvents.dm:230), e o comentario de la explica o que ela conserta: `staminadeBuff` so era
	/// escrito pelo `CheckNutrition`, que e gated por client -- entao TODO NPC ficava no default,
	/// `staminaratio` caia pra 0,3 e o BP expresso deflacionava pra 0,3x. Era o bug do "Sense %%".
	///
	/// Este port ja nasce com `staminadeBuff = 100` (Fighter.cs:41), mas a normalizacao continua
	/// necessaria pelos outros tres: Ki cheio (`kiratio`), folego cheio e raiva calma
	/// (`angerBuff = Anger/100`). E ela vem DEPOIS do `Statify`, senao encheria um tanque cujo
	/// tamanho ainda ia mudar.
	/// </summary>
	public static void Normalizar(Fighter f)
	{
		f.Statify();
		f.Ki = f.MaxKi;
		f.stamina = f.maxstamina;
		f.staminadeBuff = 100;
		f.CurrentNutrition = Nutricao.TanqueInicial;
		f.Anger = 100;
		f.HP = 100;
		f.PowerLevel();
	}

	// =====================================================================
	// O GERADOR
	// =====================================================================
	/// <summary>
	/// UM GERADOR POR CAMPO, derivado de (semente + nome do campo). Ver o cabecalho -- e o mesmo
	/// desenho do <see cref="LimiaresPessoais"/>, e existe pelo mesmo motivo: acrescentar um
	/// sorteio novo amanha nao pode mexer nos que ja existem.
	/// </summary>
	public static Random Sorteador(ulong semente, string campo)
	{
		ulong h = Espaco.Misturar(semente, Espaco.Hash64(campo), 0x4E50_435F_5345_4544UL);
		return new Random(unchecked((int)(h ^ (h >> 32))));
	}

	private static string Escolher(string[] opcoes, ulong semente, string campo, string padrao) =>
		opcoes.Length == 0 ? padrao : opcoes[Sorteador(semente, campo).Next(opcoes.Length)];
}
