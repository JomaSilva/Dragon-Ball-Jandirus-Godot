using System.Text.Json;

namespace Jandirus.Core.Npc;

/// <summary>
/// UM DEGRAU DA FICHA PRONTA DE UM CHEFE -- o `BEV_FREEZA2_BPS[i]` do original.
///
/// ============================ O CHEFE NAO TEM UM BP: TEM UMA LISTA ============================
/// A pergunta do dono era exatamente esta -- *"o freeza do planeta vegeta nao transforma enquanto o
/// freeza de namek transforma"* -- e a resposta do DM nao esta na IA: os dois sao o MESMO mob
/// (`mob/npc/Enemy/EventBoss`), pela MESMA fabrica (`init_event_boss`, BossEvents.dm:205), com o
/// MESMO `ai_no_powerup = 1` (BossEvents.dm:181) e o mesmo cerebro. O que difere e o DADO:
///
///   Vegeta -- `#define BEV_FREEZA1_BP 530000`                        (BossEvents.dm:53) um ESCALAR
///   Namek  -- `BEV_FREEZA2_BPS = list(530000, 1e6, 2e6, 3e6, 4e6)`   (BossEvents.dm:54) uma LISTA
///
/// O Freeza de Vegeta nao transforma porque **a lista dele tem um item so**. E por isso este tipo
/// existe: com ele, "transforma" e "nao transforma" sao o mesmo campo com tamanhos diferentes, e
/// nao dois caminhos de codigo que alguem tem que lembrar de manter iguais.
/// ==========================================================================================
/// </summary>
public sealed class EstagioDeChefe
{
	/// <summary>O BP PINADO deste degrau. E promessa, nao media: ver <see cref="MoldeDeNpc.AscendePorDecisao"/>.</summary>
	public double Bp;

	/// <summary>
	/// A forma do <see cref="Forms.Catalogo"/> em que o corpo entra ao chegar neste degrau. Vazio =
	/// o degrau nao mexe na escada (e o caso do Freeza, cuja "forma" no DM e a troca de ICONE --
	/// `BEV_FREEZA2_ICONS`, BossEvents.dm:79).
	/// </summary>
	public string Forma = "";

	/// <summary>
	/// A APARENCIA deste degrau (caminho de sprite de corpo). O `boss2.icon = BEV_FREEZA2_ICONS[s2_form]`
	/// de `freeza_transform` (BossEvents.dm:668). Vazio = nao troca de corpo.
	/// </summary>
	public string Corpo = "";

	/// <summary>
	/// A FRACAO DE VIDA DO PIOR MEMBRO que faz este degrau ACABAR -- `BEV_FREEZA2_THRESHOLDS`
	/// (BossEvents.dm:67), lido com o indice do degrau ATUAL (`THRESHOLDS[s2_form]`, :590).
	///
	/// Negativo = degrau final: nada o encerra. E o unico jeito de a lista acabar.
	///
	/// ============================ O GATILHO E DE DANO, NAO DE DECISAO ============================
	/// Quem le isto e o <c>TickDoRoteiro</c> do servidor, nao o cerebro. A transformacao de chefe no
	/// original NAO e uma escolha da IA: e um estagio disparado pelo membro mais ferido e executado
	/// pelo controlador do evento (BossEvents.dm:588-590), que tambem CURA e reancora o gatilho.
	/// ==========================================================================================
	/// </summary>
	public double GatilhoMembro = -1;

	/// <summary>
	/// Quanto do dano ja levado o corpo recupera ao ENTRAR neste degrau -- `BEV_FREEZA2_TRANSFORM_HEAL`
	/// (BossEvents.dm:69, 0,5 = "um membro a 80% volta pra 90%").
	/// </summary>
	public double Cura = 0.5;

	/// <summary>O que a zona ouve quando o degrau vira. Vazio = silencio.</summary>
	public string Anuncio = "";
}

/// <summary>
/// O MOLDE: a receita de onde sai uma ficha de NPC. **Dado, nao codigo.**
///
/// ============================ POR QUE ISTO NAO E UMA CLASSE POR INIMIGO ============================
/// A tentacao e escrever `class Saibaman : Npc` e por os numeros no construtor. O original nao faz
/// isso e a razao aparece em `PlanetPopulation.dm:359-445`: os "tipos" de NPC de la
/// (`make_saiyan_commoner`, `make_saiyan_royal`, `make_human`, `make_namek`, `make_guru`) sao a MESMA
/// receita com faixas diferentes -- raca, classe, faixa de BP, genero, nome, cabelo, armadura. Um tipo
/// novo nao precisa de codigo novo; precisa de uma linha de dado.
///
/// E o dono afina a dificuldade sem recompilar: <c>Assets/Data/npcs.json</c> e lido no boot como o
/// `skills.json`, o `races.json` e o `niveis.json` ja sao. Trocar `bpMin`/`bpMax`/`bpRelativo` num
/// arquivo de texto e reiniciar o servidor e o ciclo inteiro.
/// ==============================================================================================
///
/// ============================ E POR QUE NAO HA "FICHA DE NPC" NENHUMA ============================
/// O molde e a RECEITA. O que ele produz e um <see cref="Stats.Fighter"/>, um
/// <see cref="Skills.SkillBook"/>, um <see cref="Skills.NiveisDeSkill"/> e um
/// <see cref="Forms.EstadoDeForma"/> -- os MESMOS quatro objetos que um jogador tem, montados pelas
/// MESMAS funcoes (`Birth.Nascer`, `SkillBook.Aprender`, `EstadoDeForma.Avaliar`). Uma segunda
/// estrutura "igual mas de NPC" divergiria no primeiro campo novo, e a divergencia apareceria em jogo
/// como "o NPC nao sente o que eu sinto".
/// ============================================================================================
/// </summary>
public sealed class MoldeDeNpc
{
	/// <summary>Chave do molde. E por ela que o mundo, o admin e o save pedem um NPC.</summary>
	public string Id = "";

	/// <summary>Nome de exibicao quando <see cref="Nomes"/> esta vazio.</summary>
	public string Nome = "";

	/// <summary>Nomes possiveis (o `npc_random_name`, PlanetPopulation.dm:349). Sorteado.</summary>
	public string[] Nomes = [];

	// =====================================================================
	// QUEM ELE E
	// =====================================================================

	/// <summary>
	/// Pool de racas. Vazio = as racas jogaveis do planeta do spawn
	/// (<see cref="Races.CharacterDraft.RacasDoPlaneta"/>) -- a MESMA tabela que a criacao de
	/// personagem oferece, e nao uma segunda lista pra manter em dia.
	/// </summary>
	public string[] Racas = [];

	/// <summary>
	/// A CLASSE/LINHAGEM CRAVADA. Vazio = sorteada pelo `Class_Spread` do proto, como a de jogador.
	///
	/// O DM crava a classe nos NPCs por um motivo tecnico que este port nao tem (`Class` != "None"
	/// faz o `stat&lt;Raca&gt;()` pular um `input()` que TRAVA mob sem client -- PlanetPopulation.dm:314),
	/// mas o efeito de desenho vale aqui tambem: um chefe nao pode sortear a propria classe, porque
	/// a classe muda o poder e o BP dele e promessa.
	/// </summary>
	public string Classe = "";

	/// <summary>Generos possiveis. O `pick("male","male","male","female")` do DM e uma lista com repeticao.</summary>
	public string[] Generos = ["Male"];

	// =====================================================================
	// PODER
	// =====================================================================

	/// <summary>Faixa de BP sorteada (linear, como o `rand(2000,5000)` do DM). Ignorada se ha estagios.</summary>
	public double BpMin, BpMax;

	/// <summary>
	/// MULTIPLO DA MEDIA DO SERVIDOR. Zero = usa a faixa fixa.
	///
	/// E o `NPCTicker` base (NPClist.dm:72): `BP = max(BP, rand(AverageBP * 0.5 * quality, ...))` --
	/// o bicho de mundo ACOMPANHA os jogadores, e o `Bosses.dm:19` faz o mesmo pra cima
	/// (`max(AverageBP*2.8, BP)`).
	///
	/// **ESTE CAMPO TROCA DETERMINISMO POR DIFICULDADE, e a troca e por molde**: um molde com faixa
	/// fixa da o mesmo NPC em qualquer servidor com a mesma semente; um molde relativo da um NPC que
	/// depende de quem esta online. As duas coisas sao desejaveis em lugares diferentes, e por isso
	/// sao dois campos e nao um interruptor global.
	/// </summary>
	public double BpRelativo;

	/// <summary>Piso absoluto quando <see cref="BpRelativo"/> manda (servidor vazio nao gera NPC de BP 0).</summary>
	public double BpPiso = 1;

	// =====================================================================
	// SKILLS -- o orcamento, e nao a lista
	// =====================================================================

	/// <summary>Marcos de partida (o `MarcosIniciais` do jogador e 3).</summary>
	public int MarcosBase = 3;

	/// <summary>
	/// Marcos por POTENCIA DE DEZ de BP. E a peca que amarra poder e pericia: um bicho de 1e6 de BP
	/// tem 6 decadas, um de 1e9 tem 9 -- e a diferenca entre eles deixa de ser so o numero do soco.
	///
	/// ============================ POR QUE UMA TABELA, E NAO O ACUMULO DO JOGADOR ============================
	/// Porque o acumulo do jogador AINDA NAO EXISTE neste port: `PrepararSkills` da 3 marcos e nada
	/// mais os aumenta fora do admin (GameServer.Skills.cs:32,57). No dia em que o marco por progresso
	/// entrar, esta linha vira uma chamada aquela funcao e o NPC passa a pagar o mesmo preco que o
	/// jogador -- que e o unico jeito honesto de responder "quantas skills um BP desses compra?".
	/// ==================================================================================================
	/// </summary>
	public double MarcosPorDecada = 1.5;

	/// <summary>
	/// A VOCACAO: prefixos de typepath na ordem de preferencia. O sorteio gasta os marcos NESTA ordem,
	/// e o que sobra vai por tier crescente.
	///
	/// E o que faz um bicho ser lutador de corpo ou de Ki sem ninguem escrever a lista de skills dele:
	/// as skills continuam vindo da arvore da raca (que o `SkillBook` ja cobra), a vocacao so escolhe
	/// por onde comecar.
	/// </summary>
	public string[] Vocacao = [];

	/// <summary>
	/// Skills ENSINADAS (`SkillBook.Dar`) -- as que nao se compram. E o caminho do Kaio-ken e da
	/// Genkidama (skill solta, que nao pende de arvore nenhuma: ver SkillBook.cs:134) e o kit fixo
	/// de um chefe.
	/// </summary>
	public string[] Dadas = [];

	/// <summary>
	/// O DEGRAU em que as skills deste NPC nascem (0..100), aplicado a tudo que ele sabe.
	///
	/// Nao e enfeite: e o nivel que abre capacidade. Voar pede `Ki_Unlocked` no nivel 50
	/// (`GameServer.Voo.cs:118`) -- um NPC com a skill no nivel 1 tem a skill e nao levanta do chao.
	/// </summary>
	public double NivelDasSkills;

	/// <summary>Degraus especificos, quando um chefe precisa de um nivel que a faixa geral nao daria.</summary>
	public Dictionary<string, double> Niveis = new(StringComparer.Ordinal);

	// =====================================================================
	// FORMAS
	// =====================================================================

	/// <summary>
	/// LIBERA A ESCADA ATE ONDE O BP ALCANCA -- subindo degrau a degrau pelo
	/// <see cref="Forms.EstadoDeForma.Proxima"/>, que e o MESMO seletor da tecla C.
	///
	/// Assim um NPC nunca tem uma forma que um jogador daquele BP nao teria, e nunca falta uma que
	/// ele teria: a coerencia nao e conferida, ela e consequencia de usar o funil de producao.
	/// </summary>
	public bool EscadaAutomatica = true;

	/// <summary>Formas cravadas (ids do <see cref="Forms.Catalogo"/>), pra quando o chefe precisa de uma que a escada nao daria.</summary>
	public string[] Formas = [];

	/// <summary>Maestria (0..100) nas formas liberadas. E ela que abre o degrau seguinte da escada.</summary>
	public double Maestria;

	// =====================================================================
	// O QUE A FICHA DECLARA SOBRE O COMPORTAMENTO
	// =====================================================================

	/// <summary>
	/// ============================ "TEM A FORMA E NAO A USA" ============================
	/// Este e o campo que responde ao pedido do dono, e ele e DIFERENTE de nao ter a forma: um chefe
	/// com <see cref="Formas"/> preenchidas e este campo em `false` **conhece** a transformacao (o
	/// `Despertou()` dele devolve verdadeiro, o multiplicador existe, o sprite existe) e mesmo assim
	/// nunca sobe por conta propria. Quem o move e o roteiro (<see cref="Estagios"/>).
	///
	/// E o `ai_no_powerup` do original (NPCAI.dm:64), ligado nos quatro mobs roteirizados
	/// (Tournament.dm:78, MajinSaga.dm:303 e :480, BossEvents.dm:181) com o comentario que diz tudo
	/// numa frase: *"BP anunciado = BP real (sem surto de tier)"*.
	///
	/// NO DM ELE COBRE DUAS COISAS e aqui cobre uma: la o `npc_try_transform()` e chamado de DENTRO
	/// do `npc_power_up()` (NPCAI.dm:254), entao a mesma flag desliga o surto de BP e a transformacao.
	/// O surto de BP (`NPCAscension`, `BPBoost`) nao esta portado; quando estiver, le este campo --
	/// nao um segundo.
	/// ================================================================================
	/// </summary>
	public bool AscendePorDecisao = true;

	/// <summary>
	/// Temperamento -- `behavior_vals` (NPCAI.dm:76) e os defaults `INT_DEFAULT 35` / `AGGR_DEFAULT 50`
	/// / `INT_BOSS 85` / `AGGR_BOSS 75` (NPCAI.dm:8-29). Escritos na ficha aqui porque sao dela; quem
	/// os LE e a decisao, que vem depois.
	/// </summary>
	public double Inteligencia = 35, Agressividade = 50, Coragem = 50, Furia = 50;

	/// <summary>A ficha pronta, degrau a degrau. Vazia = bicho de mundo (BP sorteado, sem roteiro).</summary>
	public EstagioDeChefe[] Estagios = [];

	/// <summary>Chefe e quem tem roteiro. DERIVADO -- um `bool Chefe` ao lado seria a mesma verdade duas vezes.</summary>
	public bool EhChefe => Estagios.Length > 0;

	/// <summary>
	/// O que ha de contraditorio neste molde. Vazio = coerente.
	///
	/// Nao e paranoia: o arquivo e editado a mao pelo dono, e um molde incoerente falha CALADO --
	/// o NPC nasce e so quem estiver olhando o combate percebe que ele nunca transforma.
	/// </summary>
	public List<string> Problemas()
	{
		var p = new List<string>();
		if (Id.Length == 0) p.Add("molde sem id");

		if (EhChefe && AscendePorDecisao)
			p.Add($"'{Id}': tem roteiro de estagios E ascendePorDecisao=true -- os dois moveriam a "
				+ "mesma escada (o roteiro pelo dano, a IA pela decisao). Escolha um.");

		if (!EhChefe && BpRelativo <= 0 && BpMax <= 0)
			p.Add($"'{Id}': sem estagios, sem bpRelativo e sem bpMax -- este NPC nasceria com BP 0.");

		if (BpMax > 0 && BpMin > BpMax) p.Add($"'{Id}': bpMin maior que bpMax");

		for (int i = 0; i < Estagios.Length; i++)
		{
			if (Estagios[i].Bp <= 0) p.Add($"'{Id}': estagio {i + 1} sem bp");
			// o ULTIMO nao tem gatilho (nada o encerra) e os outros TEM: sem isso a escada trava no meio
			bool ultimo = i == Estagios.Length - 1;
			if (!ultimo && Estagios[i].GatilhoMembro < 0)
				p.Add($"'{Id}': estagio {i + 1} nao e o ultimo e nao tem gatilhoMembro -- a escada pararia nele");
		}
		return p;
	}
}

/// <summary>
/// OS MOLDES LIDOS DO `npcs.json`. Mesmo desenho do <see cref="Races.RaceCatalog"/> e do
/// <see cref="Skills.SkillCatalog"/>: dado no disco, regra no codigo.
/// </summary>
public sealed class CatalogoDeMoldes
{
	private readonly Dictionary<string, MoldeDeNpc> _porId = new(StringComparer.OrdinalIgnoreCase);

	public IEnumerable<MoldeDeNpc> Todos => _porId.Values;
	public int Total => _porId.Count;
	public MoldeDeNpc? Get(string id) => _porId.GetValueOrDefault(id);

	public static CatalogoDeMoldes Parse(string json)
	{
		var cat = new CatalogoDeMoldes();
		using JsonDocument doc = JsonDocument.Parse(json);

		if (!doc.RootElement.TryGetProperty("moldes", out JsonElement moldes)) return cat;

		foreach (JsonElement e in moldes.EnumerateArray())
		{
			var m = new MoldeDeNpc
			{
				Id = Txt(e, "id"),
				Nome = Txt(e, "nome"),
				Nomes = Lista(e, "nomes"),
				Racas = Lista(e, "racas"),
				Classe = Txt(e, "classe"),
				BpMin = Num(e, "bpMin", 0),
				BpMax = Num(e, "bpMax", 0),
				BpRelativo = Num(e, "bpRelativo", 0),
				BpPiso = Num(e, "bpPiso", 1),
				MarcosBase = (int)Num(e, "marcosBase", 3),
				MarcosPorDecada = Num(e, "marcosPorDecada", 1.5),
				Vocacao = Lista(e, "vocacao"),
				Dadas = Lista(e, "dadas"),
				NivelDasSkills = Num(e, "nivelDasSkills", 0),
				EscadaAutomatica = Bit(e, "escadaAutomatica", true),
				Formas = Lista(e, "formas"),
				Maestria = Num(e, "maestria", 0),
				AscendePorDecisao = Bit(e, "ascendePorDecisao", true),
				Inteligencia = Num(e, "inteligencia", 35),
				Agressividade = Num(e, "agressividade", 50),
				Coragem = Num(e, "coragem", 50),
				Furia = Num(e, "furia", 50),
			};

			string[] generos = Lista(e, "generos");
			if (generos.Length > 0) m.Generos = generos;

			if (e.TryGetProperty("niveis", out JsonElement niveis) && niveis.ValueKind == JsonValueKind.Object)
				foreach (JsonProperty kv in niveis.EnumerateObject())
					m.Niveis[kv.Name] = kv.Value.GetDouble();

			if (e.TryGetProperty("estagios", out JsonElement est) && est.ValueKind == JsonValueKind.Array)
			{
				var l = new List<EstagioDeChefe>();
				foreach (JsonElement s in est.EnumerateArray())
					l.Add(new EstagioDeChefe
					{
						Bp = Num(s, "bp", 0),
						Forma = Txt(s, "forma"),
						Corpo = Txt(s, "corpo"),
						GatilhoMembro = Num(s, "gatilhoMembro", -1),
						Cura = Num(s, "cura", 0.5),
						Anuncio = Txt(s, "anuncio"),
					});
				m.Estagios = [.. l];
			}

			if (m.Id.Length > 0) cat._porId[m.Id] = m;
		}
		return cat;
	}

	private static string Txt(JsonElement e, string chave) =>
		e.TryGetProperty(chave, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

	private static double Num(JsonElement e, string chave, double padrao) =>
		e.TryGetProperty(chave, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : padrao;

	private static bool Bit(JsonElement e, string chave, bool padrao) =>
		e.TryGetProperty(chave, out JsonElement v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
			? v.GetBoolean() : padrao;

	private static string[] Lista(JsonElement e, string chave)
	{
		if (!e.TryGetProperty(chave, out JsonElement v) || v.ValueKind != JsonValueKind.Array) return [];
		var l = new List<string>();
		foreach (JsonElement s in v.EnumerateArray())
			if (s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } t) l.Add(t);
		return [.. l];
	}
}
