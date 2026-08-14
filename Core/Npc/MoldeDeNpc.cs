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
/// O QUE ESTE CORPO E NO MUNDO -- e a unica pergunta que o recorte do dono precisa responder.
///
/// ============================ A TAXONOMIA E DO PROPRIO DM, E ELA JA ERA O RECORTE ============================
/// O original nao tem um campo destes, mas tem a mesma divisao escrita em TIPO DE MOB, e as tres
/// classes fazem coisas diferentes no `NPCTicker`:
///
///   `mob/npc/Citizen`          (PlanetPopulation.dm:64)  -- pacifico ate apanhar, BP FIXO
///   `mob/npc/Enemy/*`          (NPClist.dm, NPCspawner.dm) -- hostil de nascenca, BP acompanha a media
///   `mob/npc/Enemy/EventBoss`  (BossEvents.dm:170)       -- roteirizado, BP PINADO pelo evento
///
/// O pedido do dono -- *"cidadao sim, chefe de saga sim, inimigo comum nao"* -- e um corte NESTA
/// linha, e por isso ele e um CAMPO DO DADO e nao um `if` no spawner nem uma lista de ids escrita
/// no codigo: o recorte precisa ser afirmavel pela bancada (*"nenhum inimigo comum nasce hoje"*),
/// e uma lista de ids no codigo envelhece calada no dia em que alguem acrescentar um molde novo.
/// Ver <see cref="Povoamento.PodeNascer"/>.
/// ========================================================================================================
/// </summary>
public enum TipoDeNpc
{
	/// <summary>Habitante. Nasce no planeta da raca dele e **nao ataca ninguem sozinho**.</summary>
	Cidadao,

	/// <summary>Inimigo comum de ambiente. **DESLIGADO hoje** -- ver <see cref="Povoamento"/>.</summary>
	Inimigo,

	/// <summary>Chefe de saga: tem <see cref="MoldeDeNpc.Estagios"/> e quem o move e o roteiro.</summary>
	Chefe,
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

	/// <summary>
	/// NOMES POR RACA -- o `npc_random_name(race)` do original inteiro, e nao so uma das pools.
	///
	/// ============================ O MOLDE DO CIDADAO NAO SABE DE QUE RACA ELE E ============================
	/// Um molde de inimigo crava a raca (`racas: ["Saiyan"]`) e por isso a lista plana
	/// <see cref="Nomes"/> basta pra ele. O molde de CIDADAO nao crava: a raca so e conhecida depois
	/// do sorteio, porque ela vem do berco do planeta onde o corpo esta nascendo. Uma lista plana ali
	/// daria nome de Saiyajin a um Namekuseijin.
	///
	/// E o original tem exatamente esta forma -- um `switch(race)` com uma pool por raca
	/// (PlanetPopulation.dm:349-354) --, so que como codigo. Aqui e dado, e por isso o dono acrescenta
	/// nomes de uma raca nova sem recompilar.
	/// ==================================================================================================
	///
	/// Raca sem pool cai no <see cref="Nomes"/>, e depois no <see cref="Nome"/> -- que e o
	/// `return "Stranger"` do original com outro texto.
	/// </summary>
	public Dictionary<string, string[]> NomesPorRaca = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// O QUE ELE E NO MUNDO -- ver <see cref="TipoDeNpc"/>. Escrito no `npcs.json` como
	/// `"tipo": "cidadao" | "inimigo" | "chefe"`, e **obrigatorio**: o padrao do C# seria
	/// `Cidadao`, e um molde de inimigo que esquecesse a linha passaria pelo recorte do dono
	/// disfarcado de habitante. `Problemas()` recusa o molde sem tipo declarado.
	/// </summary>
	public TipoDeNpc Tipo = TipoDeNpc.Cidadao;

	/// <summary>O `tipo` estava escrito no arquivo? So pra `Problemas()` poder exigir. Ver acima.</summary>
	public bool TipoDeclarado;

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

		// O TIPO E EXIGIDO, e nao herdado do padrao do C#: ver <see cref="Tipo"/>. Um molde de
		// inimigo sem a linha viraria "cidadao" e ATRAVESSARIA o recorte do dono -- exatamente o
		// modo de falha da armadilha 5 da PARTE 3 ("silencio no lugar de erro").
		if (!TipoDeclarado)
			p.Add($"'{Id}': sem 'tipo' -- declare \"cidadao\", \"inimigo\" ou \"chefe\". Sem ele o "
				+ "recorte do dono (inimigo comum nao nasce) nao teria como distinguir este molde.");

		// TIPO E ROTEIRO SAO A MESMA VERDADE e tem que concordar: `EhChefe` e derivado dos estagios
		// (e continua sendo a autoridade de quem tem roteiro), e o `Tipo` e o que o povoamento le.
		if (EhChefe && Tipo != TipoDeNpc.Chefe)
			p.Add($"'{Id}': tem roteiro de estagios mas o tipo e '{Tipo}' -- chefe e quem tem roteiro.");
		if (!EhChefe && Tipo == TipoDeNpc.Chefe)
			p.Add($"'{Id}': tipo 'chefe' e nenhum estagio -- um chefe sem ficha pronta nao teria BP.");

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

	/// <summary>
	/// O PLANO DE POVOAMENTO, do MESMO arquivo. Mora junto porque a linha do plano so faz sentido
	/// com o molde a que ela se refere -- e um segundo arquivo seria uma segunda chance de o dono
	/// editar um e esquecer o outro. Ver <see cref="Povoamento.Problemas"/>, que cruza os dois.
	/// </summary>
	public LinhaDePovoamento[] Plano = [];

	/// <summary>
	/// A CADEIA DE SAGAS, do MESMO arquivo e pelo mesmo motivo do <see cref="Plano"/>: cada elo cita
	/// moldes pelo id, e um segundo arquivo seria uma segunda chance de renomear um molde aqui e
	/// esquecer o elo la. Ver <see cref="Sagas.Problemas"/>, que cruza os dois.
	/// </summary>
	public EloDaSaga[] Cadeia = [];

	public static CatalogoDeMoldes Parse(string json)
	{
		var cat = new CatalogoDeMoldes();
		using JsonDocument doc = JsonDocument.Parse(json);

		cat.Plano = Povoamento.Ler(doc);
		cat.Cadeia = Sagas.Ler(doc);

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

			// O TIPO E LIDO COMO TEXTO e nao como numero: um `"tipo": 1` no arquivo nao diria nada a
			// quem o edita. Texto desconhecido NAO cai num padrao -- ele deixa `TipoDeclarado` falso e
			// o molde e recusado com o nome do campo, que e o unico jeito de o dono ver o erro de
			// digitacao em vez de um NPC que nasce onde nao devia.
			string tipo = Txt(e, "tipo");
			switch (tipo.ToLowerInvariant())
			{
				case "cidadao": m.Tipo = TipoDeNpc.Cidadao; m.TipoDeclarado = true; break;
				case "inimigo": m.Tipo = TipoDeNpc.Inimigo; m.TipoDeclarado = true; break;
				case "chefe": m.Tipo = TipoDeNpc.Chefe; m.TipoDeclarado = true; break;
			}

			if (e.TryGetProperty("nomesPorRaca", out JsonElement pools) && pools.ValueKind == JsonValueKind.Object)
				foreach (JsonProperty kv in pools.EnumerateObject())
				{
					var l = new List<string>();
					if (kv.Value.ValueKind == JsonValueKind.Array)
						foreach (JsonElement s in kv.Value.EnumerateArray())
							if (s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } t) l.Add(t);
					if (l.Count > 0) m.NomesPorRaca[kv.Name] = [.. l];
				}

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
