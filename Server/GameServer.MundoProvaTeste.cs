using System.Text.Json;
using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO PAR CENTRAL (`--mundoprova`): **a semente que nasce sorteada** e **o NPC que nasce
/// vestido** -- e cada familia daqui obrigada a ficar VERMELHA com o defeito dentro.
///
/// ============================ POR QUE ELA EXISTE, TENDO `--sementeteste` E `--npcteste` ============================
/// Aquelas duas AFIRMAM: 34 + 90 checagens verdes. Nenhuma delas jamais foi vista reprovando -- e
/// uma checagem que so foi vista passando e indistinguivel de `Checa("...", true)`. Este projeto ja
/// pagou esse preco pelo menos tres vezes (a API de sigilo de BP escrita e orfa, o bit de Admin
/// apagado doze linhas depois, o canal de FLAGS extraido e morto): nos tres o que faltou nao foi
/// codigo, foi alguem obrigar a regra a falhar uma vez.
///
/// Entao aqui o desenho e o da <see cref="GameServer.Mutacao"/>, o mesmo da `--provateste`, e ele e
/// o unico desenho desta bancada inteira:
///
///     1. o criterio e uma FUNCAO NOMEADA e mede o codigo de PRODUCAO   -> tem que passar
///     2. injeta-se um defeito de verdade                               -> tem que REPROVAR
///     3. desfaz-se o defeito                                           -> tem que passar de novo
///
/// **O criterio e o MESMO objeto nas tres vezes.** Escrever a versao "com defeito" a mao mediria a
/// copia, e a copia e onde o teste concorda consigo mesmo e discorda do jogo.
/// ==============================================================================================================
///
/// ============================ AS DUAS METADES SE SEGURAM, E ESSE E O PONTO ============================
/// O pedido do dono -- *"os NPCS estao sempre nascendo IGUAIS a cada WIPE e o UNIVERSO tb"* -- tem
/// uma armadilha que so aparece quando se tenta reprovar: **cada metade sozinha e satisfeita por um
/// defeito diferente**, e os dois defeitos sao opostos.
///
///   * a semente CONSTANTE (o estado em que o dono achou o jogo) passa na familia 2 com nota cheia:
///     reiniciar de fato nao muda o mundo. Ela so morre na familia 1.
///   * a semente SORTEADA A CADA BOOT (a correcao apressada) passa na familia 1 com nota cheia: dois
///     mundos novos sao mesmo diferentes. Ela so morre na familia 2 -- e o custo dela em jogo e a
///     construcao e a nave do dono num planeta que nunca houve.
///
/// Uma bancada com so uma das duas ficaria verde no dia em que alguem trocasse um defeito pelo
/// outro. E por isso que as duas familias sao a primeira e a segunda, e por isso que o defeito de
/// cada uma e injetado na outra tambem.
/// ==================================================================================================
///
/// ============================ AS OITO FAMILIAS ============================
///   1. DOIS MUNDOS NOVOS DAO MUNDOS DIFERENTES -- semente, universo, planetas, habitantes, nomes,
///      classes E a roupa deles.
///   2. O MESMO MUNDO, RELIDO DO SAVE, DA O MESMO MUNDO -- a lei da casa.
///   3. A SEMENTE SOBREVIVE AO RESTART E SOME NO WIPE -- as tres coisas que o wipe tem que fazer.
///   4. SAVE ANTIGO SEM O CAMPO MANTEM O MUNDO DE HOJE -- o mundo do dono nao se mexe.
///   5. TODO NPC NASCE VESTIDO, E A ROUPA COMBINA COM A RACA -- uma linha por raca, com a peca
///      NOMEADA. "tem alguma roupa" ficaria verde com todo mundo de armadura saiyajin, e a familia
///      injeta exatamente esse defeito pra provar que aqui nao fica.
///   6. A MESMA SEMENTE VESTE O MESMO NPC IGUAL -- caminho E cor.
///   7. A ROUPA SOBREVIVE A REPOSICAO PELA MANUTENCAO -- o cidadao nao tem save: ela e REDEDUZIDA.
///   8. O CHEFE DE SAGA CONTINUA COM A APARENCIA DELE -- Freeza nao ganha moletom por sorteio.
/// ======================================================================
///
/// ============================ O QUE ELA TOCA ============================
/// As familias 5 a 8 nao tocam em disco nenhum: elas perguntam ao funil de aparencia com sementes
/// escritas NESTA bancada. As familias 1 a 4 rodam dentro do <see cref="NaCaixa"/> -- o temporario
/// da bancada da limpeza, com o mesmo `finally` que devolve o `_store` e recarrega o mundo do dono
/// do disco --, e a familia 3 chega a chamar o `ExecutarLimpeza` DE PRODUCAO. A pasta de verdade
/// nunca e tocada.
///
///     Godot --headless --path . -- --server --rede 7958 --mundoprova
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// AS REGRAS -- e e isto que permite injetar o defeito de verdade
	// =====================================================================
	/// <summary>
	/// AS PERGUNTAS QUE O JOGO FAZ, cada campo comecando no metodo DE PRODUCAO.
	///
	/// Nem todo defeito e um dado errado. "a semente voltou a ser constante", "o wipe parou de
	/// alcancar o arquivo", "o chefe entrou no sorteio racial" sao mudancas de CODIGO, e a unica
	/// forma honesta de provar que a familia pegaria isso e rodar as MESMAS provas contra o codigo
	/// mutante. Trocar um campo aqui e trocar a implementacao debaixo das provas.
	///
	/// O caminho saudavel nao paga nada por isso: com os valores iniciais, a bancada verde e a
	/// bancada medindo o jogo.
	/// </summary>
	private sealed class RegrasDoMundo
	{
		/// <summary>`CarregarSemente` -- de onde a semente deste mundo vem.</summary>
		public required Action Carregar;

		/// <summary>`SalvarSemente` -- o `universo.json`.</summary>
		public required Action Salvar;

		/// <summary>`ExecutarLimpeza(null)` -- a vassoura DE PRODUCAO.</summary>
		public required Func<ResultadoDaLimpeza> Limpar;

		/// <summary>
		/// O que o <see cref="AdminLimparServidor"/> faz DEPOIS da vassoura: grava o endereco do
		/// mundo novo. Campo proprio porque a ORDEM e a regra -- gravar antes seria escrever um
		/// arquivo que o passo 4 da limpeza apaga em seguida, e o boot seguinte sortearia um terceiro
		/// universo.
		/// </summary>
		public required Action GravarDepoisDaVassoura;

		/// <summary>A assinatura de 64 celulas de sistema -- a mesma que o verb `univ|` responde.</summary>
		public required Func<ulong, ulong> Universo;

		/// <summary>Os planetas de 49 celulas em volta da origem: nome e posicao.</summary>
		public required Func<ulong, string> Planetas;

		/// <summary>Quem nasce nos planetas do plano: `planeta|nome|raca|classe|BP`, pelo sorteio de producao.</summary>
		public required Func<ulong, List<string>> Censo;

		/// <summary>`AparenciaDeNpc` -- o funil inteiro (tabela -> `Sanear` -> a ficha que vai pro fio).</summary>
		public required Func<MoldeDeNpc, string, string, ulong, Appearance> Aparencia;

		/// <summary>
		/// COMO O `lugar` DA MANUTENCAO VIRA SEMENTE -- `Espaco.Misturar(zona.Hash, lugar, 0)`, a
		/// conta literal do <see cref="NascerNpc"/>.
		///
		/// Ela e um campo proprio porque **este projeto ja quebrou exatamente aqui**: o povoamento
		/// numera o lugar POR PLANETA, entao sem a zona na conta o habitante 4 da Terra e o 4 de
		/// Vegeta caiam na MESMA semente -- mesmo BP, mesmo genero, mesma idade, mesmo indice de
		/// nome, planeta por planeta, linha por linha (ver o comentario no `NascerNpc`). A raca
		/// disfarcava, porque ela vem do berco. Injetar "a zona saiu da conta" e reviver esse dia.
		/// </summary>
		public required Func<ZoneKey, ulong, ulong> LugarDoHabitante;
	}

	private RegrasDoMundo? _rm;

	/// <summary>
	/// O QUE CADA RACA VESTE -- e esta lista e o ORACULO, escrita a mao de proposito.
	///
	/// ============================ POR QUE ELA E UMA SEGUNDA LISTA ============================
	/// Perguntar ao `RoupaDeNpc` "o que o Saiyajin veste?" e comparar com o que o `RoupaDeNpc`
	/// devolveu e uma tautologia -- ficaria verde com a tabela inteira trocada. O pedido do dono foi
	/// literal sobre isso: *"uma linha por raca, com a peca NOMEADA. 'Tem alguma roupa' ficaria verde
	/// com todo mundo de armadura saiyajin"*.
	///
	/// Entao aqui esta a EXPECTATIVA, com a peca pelo nome e a linha do DM ao lado. Ela envelhece de
	/// proposito: mudar a tabela do jogo sem mudar esta linha deixa a familia 5 vermelha, que e
	/// exatamente a conversa que tem que acontecer.
	///
	/// Lista VAZIA = **nasce sem peca nenhuma, e isso esta certo**: o sprite deste povo ja e a roupa
	/// (ver `RoupaDeNpc.OCorpoJaVeste`). Uma bancada que exigisse peca deles reprovaria a REGRA em
	/// vez do defeito -- foi o que a `--npcteste` fez na primeira rodada, com o cidadao de Icer.
	/// ====================================================================================
	/// </summary>
	private static readonly (string Raca, string[] Pecas)[] OQueCadaRacaVeste =
	[
		// --- as tres do pedido, e as tres com linha no DM ---
		// pick('Armor 8','Armor Bardock','Nappa Armor','RaditzArmorTobiUchiha')  -- PlanetPopulation.dm:368
		("Saiyan",       ["Armor 8", "Armor Bardock", "Nappa Armor", "RaditzArmorTobiUchiha"]),
		// prob(50) -> Gi (feminino ou Top+Bottom); senao pick(TankTop, Short, Long)  -- :396-402
		("Human",        ["ClothesGiFemale", "Clothes_GiTop", "Clothes_GiBottom",
						  "Clothes_TankTop", "Clothes_ShortSleeveShirt", "Clothes_LongSleeveShirt"]),
		// prob(60) jaqueta + prob(50) cachecol -- :413-414 (e por isso 20% nao veste nada)
		("Namekian",     ["ClothesNamekJacket", "Clothes_NamekianScarf"]),

		// --- roupa casual, LITERAL do DM: os Androides 17 e 18 sao vestidos a mao (BossEvents.dm:499,508) ---
		("Android",      ["ClothesGiFemale", "Clothes_GiTop", "Clothes_GiBottom",
						  "Clothes_TankTop", "Clothes_ShortSleeveShirt", "Clothes_LongSleeveShirt"]),

		// --- a armadura de batalha e o UNIFORME DA TROPA DO FREEZA, e nao roupa de Saiyajin ---
		("Alien",        ["Armor 8", "Armor Bardock", "Nappa Armor", "RaditzArmorTobiUchiha"]),
		("Heran",        ["Armor 8", "Armor Bardock", "Nappa Armor", "RaditzArmorTobiUchiha"]),
		("Halfbreed",    ["Armor 8", "Armor Bardock", "Nappa Armor", "RaditzArmorTobiUchiha"]),

		// --- escolhas declaradas: arte que o proprio jogo ja desenhou pra aquele povo ---
		("Yardrat",      ["ClothesFullYardrat"]),      // Clothes.dm:292-293
		("Kai",          ["Clothes Elder Kaio"]),      // Clothes.dm:126-127
		("Demigod",      ["Clothes Elder Kaio"]),
		("Makyo",        ["PhoenixFullMakyo"]),        // Clothes.dm:358-359
		("Demon",        ["Clothes Daimaou", "ClothesDaimaouCape"]),   // Clothes.dm:157-158 e :286-287

		// --- o sprite ja E a roupa: lista vazia e a resposta, e nao um buraco ---
		("Icer",         []),
		("BioAndroid",   []),
		("Saibaman",     []),
		("Dog",          []),
		("SpiritDoll",   []),

		// --- povos CIVIS sem arte propria no jogo: a camisa/regata do DM ---
		("Majin",        ["Clothes_TankTop", "Clothes_ShortSleeveShirt", "Clothes_LongSleeveShirt"]),
		("Shapeshifter", ["Clothes_TankTop", "Clothes_ShortSleeveShirt", "Clothes_LongSleeveShirt"]),
		("Gray",         ["Clothes_TankTop", "Clothes_ShortSleeveShirt", "Clothes_LongSleeveShirt"]),
		("Kanassa",      ["Clothes_TankTop", "Clothes_ShortSleeveShirt", "Clothes_LongSleeveShirt"]),
		("Arlian",       ["Clothes_TankTop", "Clothes_ShortSleeveShirt", "Clothes_LongSleeveShirt"]),
		("Meta",         ["Clothes_TankTop", "Clothes_ShortSleeveShirt", "Clothes_LongSleeveShirt"]),
		("Tsujin",       ["Clothes_TankTop", "Clothes_ShortSleeveShirt", "Clothes_LongSleeveShirt"]),
	];

	/// <summary>
	/// AS PECAS QUE SAO DE UM GENERO SO -- e esta tabela nasceu de uma CEGUEIRA desta bancada.
	///
	/// ============================ O DEFEITO QUE PASSOU BATIDO NA PRIMEIRA RODADA ============================
	/// A injecao *"o Gi FEMININO sumiu: a humana passou a vestir o Gi masculino"* deixou a familia 5
	/// **verde**. O motivo e instrutivo: o oraculo por raca lista `ClothesGiFemale` E `Clothes_GiTop`
	/// como pecas legitimas do Humano, entao uma humana de Gi masculino continua vestindo "peca da
	/// lista". A regra que o DM escreve (`if(g == "female") Gifemale` -- :397, `else` Top+Bottom --
	/// :399-400) simplesmente nao estava sendo medida por ninguem.
	///
	/// Em jogo isso e a metade das mulheres do vilarejo com o traje do homem -- exatamente a classe
	/// de erro que o dono ve na tela e a bancada nao ve no console. Consertei a BANCADA, e nao a
	/// regra: a regra estava certa o tempo todo.
	/// ====================================================================================================
	/// </summary>
	private static readonly (string Peca, string Genero)[] PecaDeUmGeneroSo =
	[
		("ClothesGiFemale", "Female"),    // PlanetPopulation.dm:397
		("Clothes_GiTop", "Male"),        // PlanetPopulation.dm:399
		("Clothes_GiBottom", "Male"),     // PlanetPopulation.dm:400
	];

	/// <summary>
	/// AS SEMENTES DE NPC DESTA BANCADA -- literais deste arquivo, e nao a semente do servidor.
	///
	/// Uma bancada de determinismo pendurada na semente do mundo compara duas coisas que ninguem
	/// escolheu, e fica verde por acidente no dia em que o mundo mudar. E o mundo, agora, MUDA.
	/// </summary>
	private const int SementesDeRoupa = 40;

	/// <summary>Quantos habitantes de cada planeta o censo desta bancada olha. Ver <see cref="CensoDoUniverso"/>.</summary>
	private const int HabitantesPorPlaneta = 4;

	private int _mpOk, _mpFalhou;

	/// <summary>O censo ja calculado de cada universo. Ver <see cref="CensoDoUniverso"/>.</summary>
	private readonly Dictionary<ulong, List<string>> _censoLembrado = [];

	/// <summary>
	/// A FILA DE REPOSICAO ja sorteada -- ver <see cref="AReposicaoVesteIgual"/>.
	///
	/// Ela pode ser lembrada porque o que a familia 7 injeta e a APARENCIA, e nao o sorteio da ficha:
	/// quem nasce no lugar 137 de Namek e o mesmo corpo com o defeito dentro e sem ele. Recalcular
	/// mil e duzentas fichas nove vezes seria pagar caro por uma resposta que ja se tem.
	/// </summary>
	private List<(string Planeta, ulong Lugar, string Raca, string Genero, ulong Semente)>? _filaDeReposicao;

	// =====================================================================
	// A PORTA
	// =====================================================================
	public void RodarProvaDoMundo()
	{
		_mpOk = _mpFalhou = 0;
		GD.Print("[mundo] ============ A SEMENTE QUE NASCE E O NPC QUE SE VESTE ============");
		GD.Print("[mundo] cada familia mede o codigo de PRODUCAO, leva o defeito injetado e tem que "
			   + "ficar VERMELHA -- e voltar ao verde quando ele sai.");

		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { _mpOk++; GD.Print($"[mundo]   OK    {nome}"); }
			else { _mpFalhou++; GD.PrintErr($"[mundo]   FALHA {nome}   {detalhe}"); }
		}

		if (_store == null || _visual == null || _moldes == null || _racas == null)
		{
			Checa("o servidor tem pasta de saves, catalogo visual, moldes e racas", false,
				  "sem um deles nao ha o que medir");
			GD.Print($"[mundo] ============ {_mpOk} OK, {_mpFalhou} FALHA(S) ============");
			return;
		}

		_rm = new RegrasDoMundo
		{
			Carregar = CarregarSemente,
			Salvar = SalvarSemente,
			Limpar = () => ExecutarLimpeza(null),
			GravarDepoisDaVassoura = SalvarSemente,
			Universo = s => Sistemas.Assinatura(s, -4, -4, 8),
			Planetas = PlanetasDoUniverso,
			Censo = CensoDoUniverso,
			Aparencia = AparenciaDeNpc,
			LugarDoHabitante = (zona, lugar) => Espaco.Misturar(zona.Hash, lugar, 0),
		};

		try
		{
			// AS DA ROUPA PRIMEIRO, e elas nao tocam em disco: assim, se a caixa da limpeza falhar
			// por motivo de ambiente (outra sessao segurando o temporario), as quatro familias do
			// segundo pedido do dono ja tem placar.
			FamiliaTodoNpcVestido(Checa);
			FamiliaMesmaSementeMesmaRoupa(Checa);
			FamiliaDaReposicao(Checa);
			FamiliaDoChefe(Checa);

			NaCaixa("mundoprova", _ =>
			{
				try
				{
					FamiliaDoisMundosNovos(Checa);
					FamiliaMesmoMundoRelido(Checa);
					FamiliaRestartEWipe(Checa);
					FamiliaSaveAntigo(Checa);
				}
				// ABORTAR NO MEIO NAO PODE PARECER SUCESSO -- a licao da `--povoteste`. O `NaCaixa`
				// tem tratador proprio, mas ele conta no placar da bancada da LIMPEZA, nao no meu:
				// sem este `catch` a excecao sumiria daqui e o rodape imprimiria "0 falhas".
				catch (Exception e) { Checa("as familias da semente rodaram inteiras", false, e.ToString()); }
			});

			Checa("a bancada chegou ao fim", true);
			OQueEstaBancadaNaoAlcanca();
		}
		catch (Exception e) { Checa("a bancada rodou inteira", false, e.ToString()); }
		finally
		{
			_rm = null;
			GD.Print($"[mundo] ============ {_mpOk} OK, {_mpFalhou} FALHA(S) ============");
			if (_mpFalhou > 0) GD.PushError($"[mundo] a prova do mundo: {_mpFalhou} falha(s)");
		}
	}

	/// <summary>
	/// O QUE ESTA BANCADA **NAO** ALCANCA -- e dizer isso e parte do resultado.
	///
	/// Uma bancada que so lista o que provou deixa quem le achando que o resto tambem esta coberto.
	/// Cada linha aqui nomeia o buraco E quem o cobre, pra a resposta nao ser "ninguem sabe".
	/// </summary>
	private static void OQueEstaBancadaNaoAlcanca()
	{
		GD.Print("[mundo] -- o que esta bancada NAO alcanca --");
		foreach (string s in new[]
		{
			"o PIXEL: que a peca escolhida chegue desenhada na tela. Aqui o mais longe e o caminho "
			+ "`.tres` no catalogo -- quem responde e a foto da `--diagvestido`.",

			"o RELOGIO da manutencao (5 min) e a fila do `TickDoPovoamento`: a familia 7 varre os "
			+ "lugares que o contador PRODUZ, e nao o tique que os enfileira -- quem responde e a `--povoteste`.",

			"a troca de semente com JOGADOR ONLINE: o wipe de verdade derruba todo mundo antes de "
			+ "varrer, e essa parte e da `--wipeteste` (secao 7).",

			"o SAVE DE VERDADE do dono: as familias 1 a 4 rodam numa caixa temporaria de proposito. "
			+ "Que o `universo.json` dele siga com a semente legada e medicao de linha de comando, "
			+ "nao de bancada.",

			"a MIGRACAO de um save que ja tenha `universo.json` com formato antigo: nao ha formato "
			+ "antigo -- o arquivo nasceu neste formato, e a familia 4 so cobre a AUSENCIA dele.",
		}) GD.Print($"[mundo]   [ -- ] {s}");
	}

	// =====================================================================
	// FAMILIA 1 -- DOIS MUNDOS NOVOS DAO MUNDOS DIFERENTES
	// =====================================================================
	/// <summary>
	/// **E ESTA A FAMILIA DO PEDIDO.** *"percebi q os NPCS estao sempre nascendo IGUAIS a cada WIPE e
	/// o UNIVERSO tb, como se a SEED DELES N MUDASSE"*.
	///
	/// Ela nao pergunta se dois numeros sao diferentes: ela sorteia DOIS mundos pelo caminho de
	/// producao e compara o que sai deles -- a assinatura do universo, os planetas com nome e
	/// posicao, quem nasce em cada planeta, o nome dessa gente, a classe dela e a ROUPA dela. Comparar
	/// so as sementes ficaria verde num servidor que ignorasse a semente por completo, que e o cego
	/// que este projeto ja registrou (*"as duas telas concordam" fica verde com as duas erradas
	/// igual*) -- e o terceiro defeito injetado aqui e exatamente esse servidor.
	/// </summary>
	private void FamiliaDoisMundosNovos(Checagem Checa)
	{
		GD.Print("[mundo] -- 1. dois mundos NOVOS dao mundos DIFERENTES (o pedido) --");

		// UMA TIRAGEM SO PRA AS SEIS LINHAS DE LEITURA: elas dizem o que mudou, uma por aspecto. O
		// criterio da mutacao la embaixo tira os dois mundos de novo, toda vez.
		(RetratoDoUniverso a, RetratoDoUniverso b) = DoisMundosNovos();
		GD.Print($"[mundo]        mundo A {Hexa(a.Semente)} contra mundo B {Hexa(b.Semente)}");
		Checa("a semente do segundo mundo novo e outra", a.Semente != b.Semente, $"{a.Semente}");
		Checa("...e o UNIVERSO que sai dela e outro (assinatura de 64 celulas de sistema)",
			  a.Universo != b.Universo, $"{a.Universo:X16}");
		Checa("...e os PLANETAS sao outros (nome e posicao, 49 celulas em volta da origem)",
			  a.Planetas != b.Planetas);
		Checa("...e quem NASCE neles e outra gente (raca, classe, BP)", a.Censo != b.Censo);
		Checa("...e os NOMES sao outros", a.Nomes != b.Nomes, a.Nomes);
		Checa("...e as CLASSES sao outras", a.Classes != b.Classes, a.Classes);
		Checa("...e ate a ROUPA deles e outra (o segundo pedido do dono anda junto com o primeiro)",
			  a.Roupas != b.Roupas);

		Mutacao(Checa,
			"dois mundos novos dao dois mundos, do universo ao guarda-roupa",
			"a semente voltou a ser CONSTANTE de compilacao -- o estado em que o dono achou o jogo",
			DoisMundosNovosDaoMundosDiferentes,
			() => _rm!.Carregar = () => { _semente = SementeLegada; _sementeNoDisco = true; },
			() => _rm!.Carregar = CarregarSemente);

		// ESTE DEFEITO PASSA NA FAMILIA 2 COM NOTA CHEIA, e e por isso que ele esta aqui: derivar a
		// semente de um identificador estavel (a pasta, o nome da maquina, o IP) e a "correcao" que
		// alguem escreve de boa fe pra garantir o determinismo -- e o mundo volta a ser sempre o
		// mesmo, so que por um caminho que ninguem procura.
		Mutacao(Checa,
			"dois mundos novos dao dois mundos, do universo ao guarda-roupa",
			"a semente passou a ser derivada da PASTA de saves (mesmo lugar = mesmo mundo, pra sempre)",
			DoisMundosNovosDaoMundosDiferentes,
			() => _rm!.Carregar = () => { _semente = Espaco.Hash64(_store!.Pasta); _sementeNoDisco = true; },
			() => _rm!.Carregar = CarregarSemente);

		// O CEGO CLASSICO: a semente muda e ninguem a consulta. Sem esta injecao, "o mundo continua o
		// mesmo" e "o mundo mudou" seriam as duas afirmacoes de um servidor que gera sempre o mesmo
		// mapa cravado -- e uma delas ficaria verde.
		Mutacao(Checa,
			"dois mundos novos dao dois mundos, do universo ao guarda-roupa",
			"o gerador IGNORA a semente que recebe e usa a de sempre (mundo cravado no codigo)",
			DoisMundosNovosDaoMundosDiferentes,
			() =>
			{
				_rm!.Universo = _ => Sistemas.Assinatura(SementeLegada, -4, -4, 8);
				_rm.Planetas = _ => PlanetasDoUniverso(SementeLegada);
				_rm.Censo = _ => CensoDoUniverso(SementeLegada);
			},
			() =>
			{
				_rm!.Universo = s => Sistemas.Assinatura(s, -4, -4, 8);
				_rm.Planetas = PlanetasDoUniverso;
				_rm.Censo = CensoDoUniverso;
			});
	}

	/// <summary>
	/// O CRITERIO DA FAMILIA 1, e ele exige que TUDO mude junto -- porque um mundo em que so a
	/// assinatura muda e um mundo em que os habitantes continuam iguais, que e a metade da queixa.
	/// </summary>
	private bool DoisMundosNovosDaoMundosDiferentes()
	{
		(RetratoDoUniverso a, RetratoDoUniverso b) = DoisMundosNovos();
		return a.Semente != b.Semente
			&& a.Universo != b.Universo
			&& a.Planetas != b.Planetas
			&& a.Censo != b.Censo
			&& a.Nomes != b.Nomes
			&& a.Classes != b.Classes
			&& a.Roupas != b.Roupas;
	}

	// =====================================================================
	// FAMILIA 2 -- O MESMO MUNDO, RELIDO DO SAVE, DA O MESMO MUNDO
	// =====================================================================
	/// <summary>
	/// A LEI DA CASA, e a metade que segura a familia 1. Sem ela, "aleatorio a cada boot" passaria
	/// verde -- e o custo disso em jogo nao e estetico: as contas guardam `ZonaSeed`, as obras
	/// guardam a zona onde estao de pe e os dominios de conquista guardam o endereco `(Sx, Sy, K)`
	/// do planeta. Quem deslogou no espaco voltaria pra uma zona que nao existe mais, e a casa que
	/// alguem ergueu ficaria de pe num planeta que nunca houve.
	/// </summary>
	private void FamiliaMesmoMundoRelido(Checagem Checa)
	{
		GD.Print("[mundo] -- 2. o MESMO mundo, relido do save, da o MESMO mundo --");

		LimparACaixa();
		_rm!.Carregar();
		GD.Print($"[mundo]        o mundo desta caixa: {Hexa(SeedDoUniverso)}");

		Mutacao(Checa,
			"cinco releituras do save devolvem a MESMA semente, o mesmo universo e a mesma gente",
			"o boot passou a SORTEAR sempre -- a correcao apressada do pedido",
			ReiniciarNaoMudaOMundo,
			() => _rm!.Carregar = () => { _semente = SortearSemente(); _sementeNoDisco = false; },
			() => _rm!.Carregar = CarregarSemente);

		// A VERSAO PIOR DO MESMO DEFEITO: sorteia E GRAVA. O mundo nao so muda a cada boot como
		// apaga o endereco do mundo anterior -- nao ha volta possivel depois do primeiro reinicio.
		Mutacao(Checa,
			"cinco releituras do save devolvem a MESMA semente, o mesmo universo e a mesma gente",
			"o universo.json e REESCRITO com semente nova a cada boot (o mundo antigo some pra sempre)",
			ReiniciarNaoMudaOMundo,
			() => _rm!.Carregar = () => { _semente = SortearSemente(); SalvarSemente(); },
			() => _rm!.Carregar = CarregarSemente);

		// E O DEFEITO QUE NAO ESTA NA SEMENTE: ela fica parada e o MUNDO anda. E o `DateTime.Now`
		// que alguem poe dentro de um gerador -- a semente continua certa no arquivo, e mesmo assim
		// o jogador volta a um planeta diferente do que deixou.
		Mutacao(Checa,
			"cinco releituras do save devolvem a MESMA semente, o mesmo universo e a mesma gente",
			"o mundo passou a ser derivado do RELOGIO junto com a semente (a semente fica, o mapa anda)",
			ReiniciarNaoMudaOMundo,
			() => _rm!.Censo = s => CensoDoUniverso(Espaco.Misturar(s, (ulong)NowMs(), 7)),
			() => _rm!.Censo = CensoDoUniverso);
	}

	/// <summary>
	/// `CarregarSemente` E LITERALMENTE O QUE O BOOT FAZ (ele e chamado do `CarregarVisual`), entao
	/// chama-lo de novo E um reinicio do ponto de vista deste sistema. A alternativa seria subir um
	/// segundo processo -- que e o que a medicao de linha de comando faz, e as duas concordam.
	///
	/// CINCO E NAO UMA porque o defeito "sorteia sempre" tem 1 chance em 2^64 de repetir na primeira,
	/// mas tambem porque um cache que so se enche na segunda leitura so aparece a partir dela.
	/// </summary>
	private bool ReiniciarNaoMudaOMundo()
	{
		// ============================ A REFERENCIA VEM DE UMA LEITURA, E NAO DA MEMORIA ============================
		// A primeira versao disto fotografava o `SeedDoUniverso` que estivesse na memoria e so entao
		// relia cinco vezes -- e o passo 3 da <see cref="Mutacao"/> ficou VERMELHO: o defeito "sorteia
		// sempre" deixa na memoria uma semente que nao esta no arquivo, entao, desfeito o defeito, a
		// primeira releitura de verdade trazia a semente do disco e "mudou" contra um retrato que
		// nunca existiu em disco nenhum.
		//
		// Nao era estrago da producao: era o criterio dependendo de estado que nao e dele. Uma leitura
		// aqui em cima o torna auto-contido -- ele compara SEMPRE disco contra disco, que e o que a
		// frase "reiniciar nao muda o mundo" quer dizer. E foi o passo 3 quem apontou, que e a razao
		// de ele existir.
		// ======================================================================================================
		_rm!.Carregar();
		RetratoDoUniverso antes = Retratar(SeedDoUniverso);

		for (int i = 0; i < 5; i++)
		{
			_rm!.Carregar();
			RetratoDoUniverso agora = Retratar(SeedDoUniverso);
			if (agora.Semente != antes.Semente || agora.Universo != antes.Universo
				|| agora.Planetas != antes.Planetas || agora.Censo != antes.Censo
				|| agora.Roupas != antes.Roupas) return false;
		}
		return true;
	}

	// =====================================================================
	// FAMILIA 3 -- A SEMENTE SOBREVIVE AO RESTART E SOME NO WIPE
	// =====================================================================
	/// <summary>
	/// AS TRES COISAS QUE O WIPE TEM QUE FAZER COM A SEMENTE, e nenhuma cobre as outras:
	///   * o ARQUIVO some (senao o proximo boot volta pro mesmo universo, e o wipe nao trocou nada);
	///   * a MEMORIA ja nasce noutro universo (senao o admin limpa, continua andando no mundo velho,
	///     e o primeiro cidadao do mundo "novo" nasce com a semente do antigo -- o *"disco limpo com
	///     cache sujo"* do cabecalho de `GameServer.Limpeza.cs`);
	///   * o endereco NOVO e gravado DEPOIS da vassoura (senao o boot seguinte le "pasta vazia" e
	///     sorteia um TERCEIRO universo, e quem criou conta entre o wipe e o reinicio perde o chao).
	///
	/// Os tres defeitos injetados sao esses tres, um a um. O quarto e a outra metade do nome da
	/// familia: a semente que nunca chega ao disco.
	/// </summary>
	private void FamiliaRestartEWipe(Checagem Checa)
	{
		GD.Print("[mundo] -- 3. a semente sobrevive ao restart e SOME no wipe --");

		Mutacao(Checa,
			"a semente do mundo esta no disco e o reinicio a encontra la",
			"o servidor so PENSA a semente e nunca a grava (o proximo boot sorteia outro mundo)",
			ASementeSobreviveAoRestart,
			() => _rm!.Salvar = () => { },
			() => _rm!.Salvar = SalvarSemente);

		Mutacao(Checa,
			"o wipe apaga o endereco, troca de universo na memoria e grava o endereco novo",
			"a vassoura nao alcanca o universo.json (o arquivo ficou de fora do registro de sistemas)",
			OWipeTrocaDeUniverso,
			() => _rm!.Limpar = () =>
			{
				string guardado = System.IO.File.Exists(CaminhoDaSemente)
					? System.IO.File.ReadAllText(CaminhoDaSemente) : "";
				ResultadoDaLimpeza r = ExecutarLimpeza(null);
				if (guardado.Length > 0) System.IO.File.WriteAllText(CaminhoDaSemente, guardado);
				return r;
			},
			() => _rm!.Limpar = () => ExecutarLimpeza(null));

		Mutacao(Checa,
			"o wipe apaga o endereco, troca de universo na memoria e grava o endereco novo",
			"o wipe limpa o DISCO e deixa a MEMORIA no mundo velho (o cache sujo)",
			OWipeTrocaDeUniverso,
			() => _rm!.Limpar = () =>
			{
				ulong velha = _semente;
				ResultadoDaLimpeza r = ExecutarLimpeza(null);
				_semente = velha;
				return r;
			},
			() => _rm!.Limpar = () => ExecutarLimpeza(null));

		Mutacao(Checa,
			"o wipe apaga o endereco, troca de universo na memoria e grava o endereco novo",
			"a gravacao foi posta ANTES da vassoura (o proximo boot cai num TERCEIRO universo)",
			OWipeTrocaDeUniverso,
			() => _rm!.GravarDepoisDaVassoura = () => { },
			() => _rm!.GravarDepoisDaVassoura = SalvarSemente);
	}

	/// <summary>O reinicio acha a semente no disco -- e o arquivo e quem a leva de um boot pro outro.</summary>
	private bool ASementeSobreviveAoRestart()
	{
		LimparACaixa();
		_semente = 0xDB26_0814_5EED_0001UL;
		_rm!.Salvar();

		// O ESQUECIMENTO E DE PROPOSITO: sem apagar a memoria, "reiniciar devolve a mesma semente"
		// ficaria verde com o disco vazio -- a resposta viria do campo que ninguem tocou.
		_semente = SementeLegada;
		_sementeNoDisco = false;

		_rm.Carregar();
		return SeedDoUniverso == 0xDB26_0814_5EED_0001UL && _sementeNoDisco;
	}

	/// <summary>
	/// O WIPE, COM A LIMPEZA DE PRODUCAO DENTRO -- e nao uma copia da ordem dela. A ordem que este
	/// criterio segue e a do <see cref="AdminLimparServidor"/>: vassoura primeiro, gravacao depois.
	/// </summary>
	private bool OWipeTrocaDeUniverso()
	{
		LimparACaixa();
		_rm!.Carregar();

		// UM MUNDO COM GENTE DENTRO, pra a limpeza ter o que limpar alem da semente.
		System.IO.File.WriteAllText(System.IO.Path.Combine(_store!.Pasta, "goku.json"),
			"""{ "Conta": "Goku", "Sal": "", "Hash": "" }""");

		ulong antes = SeedDoUniverso;
		if (!System.IO.File.Exists(CaminhoDaSemente)) return false;

		ResultadoDaLimpeza r = _rm.Limpar();
		if (r.Erros.Count > 0) return false;
		if (System.IO.File.Exists(CaminhoDaSemente)) return false;   // a vassoura alcancou o arquivo?
		if (SeedDoUniverso == antes) return false;                   // ...e a memoria trocou de mundo?

		ulong novo = SeedDoUniverso;
		_rm.GravarDepoisDaVassoura();

		_rm.Carregar();
		return SeedDoUniverso == novo;                               // ...e o proximo boot entra NELE?
	}

	// =====================================================================
	// FAMILIA 4 -- SAVE ANTIGO SEM O CAMPO MANTEM O MUNDO DE HOJE
	// =====================================================================
	/// <summary>
	/// O SAVE DO DONO NASCEU ANTES DESTE SISTEMA: ele tem 209 contas, obras e dominios, e nao tem
	/// `universo.json`. Sortear uma semente pra ele no primeiro boot seria mudar o mundo dele debaixo
	/// dele -- o oposto exato do que este trabalho existe pra fazer.
	///
	/// As tres metades da familia sao as tres perguntas que a pasta responde, e as duas ultimas sao
	/// as que fazem a primeira nao virar armadilha:
	///   * pasta com save e SEM o arquivo -> fica com a <see cref="SementeLegada"/>;
	///   * pasta so com o `admin.log` -> e MUNDO NOVO (ele e o unico arquivo que atravessa a limpeza:
	///     conta-lo faria todo mundo pos-wipe ser lido como antigo, e o wipe deixaria de trocar de
	///     universo -- ou seja, mataria a familia 1);
	///   * `universo.json` ilegivel numa pasta povoada -> PRESERVA, nao sorteia. O custo do erro aqui
	///     e o mundo do dono.
	/// </summary>
	private void FamiliaSaveAntigo(Checagem Checa)
	{
		GD.Print("[mundo] -- 4. save antigo sem o campo mantem o mundo de hoje --");

		Mutacao(Checa,
			"pasta com save fica com a semente de sempre, pasta so com admin.log e mundo novo, "
			+ "e arquivo ilegivel preserva",
			"o boot passou a sortear pra QUALQUER pasta (o mundo do dono muda debaixo dele)",
			OMundoVelhoNaoTrocaDeSemente,
			() => _rm!.Carregar = () => { _semente = SortearSemente(); _sementeNoDisco = false; },
			() => _rm!.Carregar = CarregarSemente);

		Mutacao(Checa,
			"pasta com save fica com a semente de sempre, pasta so com admin.log e mundo novo, "
			+ "e arquivo ilegivel preserva",
			"o admin.log passou a contar como mundo velho (e o wipe deixou de trocar de universo)",
			OMundoVelhoNaoTrocaDeSemente,
			() => _rm!.Carregar = () => CarregarSementeMutante(oAdminLogContaComoMundo: true, ilegivelSorteia: false),
			() => _rm!.Carregar = CarregarSemente);

		Mutacao(Checa,
			"pasta com save fica com a semente de sempre, pasta so com admin.log e mundo novo, "
			+ "e arquivo ilegivel preserva",
			"universo.json corrompido virou MUNDO NOVO (uma queda de energia troca o universo do dono)",
			OMundoVelhoNaoTrocaDeSemente,
			() => _rm!.Carregar = () => CarregarSementeMutante(oAdminLogContaComoMundo: false, ilegivelSorteia: true),
			() => _rm!.Carregar = CarregarSemente);
	}

	private bool OMundoVelhoNaoTrocaDeSemente()
	{
		// ---------------------------------------------------------- 1. pasta com save, sem o arquivo
		LimparACaixa();
		System.IO.File.WriteAllText(System.IO.Path.Combine(_store!.Pasta, "goku.json"),
			"""{ "Conta": "Goku", "Sal": "", "Hash": "" }""");
		_rm!.Carregar();
		if (SeedDoUniverso != SementeLegada) return false;
		if (!System.IO.File.Exists(CaminhoDaSemente)) return false;

		// ---------------------------------------------------------- 2. pasta so com o admin.log
		LimparACaixa();
		System.IO.File.WriteAllText(System.IO.Path.Combine(_store.Pasta, "admin.log"), "a testemunha\n");
		_rm.Carregar();
		if (SeedDoUniverso == SementeLegada) return false;

		// ---------------------------------------------------------- 3. arquivo ilegivel, pasta povoada
		LimparACaixa();
		System.IO.File.WriteAllText(System.IO.Path.Combine(_store.Pasta, "goku.json"),
			"""{ "Conta": "Goku", "Sal": "", "Hash": "" }""");
		System.IO.File.WriteAllText(CaminhoDaSemente, "isto nao e json");
		_rm.Carregar();
		return SeedDoUniverso == SementeLegada;
	}

	/// <summary>
	/// UM `CarregarSemente` MUTANTE -- e ele existe SO pra ser injetado nos dois defeitos da familia
	/// 4, que sao mudancas de codigo e nao de dado.
	///
	/// Ele nao e uma segunda implementacao da producao e nao pode virar uma: quem decide onde a
	/// semente nasce e `GameServer.Semente.cs`, um lugar so. Este aqui e o ERRO, escrito de forma
	/// que da pra ler qual e -- as duas bandeiras sao as duas linhas que a producao tem e que alguem
	/// apagaria de boa fe ("por que o admin.log nao conta?", "por que arquivo quebrado nao sorteia?").
	/// </summary>
	private void CarregarSementeMutante(bool oAdminLogContaComoMundo, bool ilegivelSorteia)
	{
		if (System.IO.File.Exists(CaminhoDaSemente))
		{
			try
			{
				FichaDoUniverso? f = JsonSerializer.Deserialize<FichaDoUniverso>(
					System.IO.File.ReadAllText(CaminhoDaSemente),
					new JsonSerializerOptions { IncludeFields = true });
				if (f != null && LerHexa(f.Semente) is { } lida && lida != 0)
				{
					_semente = lida;
					_sementeNoDisco = true;
					return;
				}
			}
			catch { /* o mutante cai no ramo de baixo, que e o defeito */ }

			_semente = ilegivelSorteia ? SortearSemente() : SementeLegada;
			_sementeNoDisco = false;
			SalvarSemente();
			return;
		}

		bool velho = _store!.TodosOsArquivos().Any(c =>
			oAdminLogContaComoMundo
			|| !string.Equals(System.IO.Path.GetFileName(c), "admin.log", StringComparison.OrdinalIgnoreCase));

		_semente = velho ? SementeLegada : SortearSemente();
		_sementeNoDisco = false;
		SalvarSemente();
	}

	// =====================================================================
	// FAMILIA 5 -- TODO NPC NASCE VESTIDO, E A ROUPA COMBINA COM A RACA
	// =====================================================================
	/// <summary>
	/// O SEGUNDO PEDIDO: *"todo npc ta nascendo SEM ROUPAS, coloque roupas neles, mas claro ROUPA DE
	/// SAIYAJIN pra saiyajins e ROUPAS COMUNS pra humanos, e ROUPAS DE NAMEK pra nameks"*.
	///
	/// A familia tem DUAS afirmacoes coladas de proposito, porque cada uma sozinha tem um defeito que
	/// a satisfaz: *"tem alguma roupa"* fica verde com todo mundo de armadura saiyajin, e *"so veste
	/// pecas da lista"* fica verde com todo mundo pelado (ninguem veste peca fora da lista). O
	/// criterio e a conjuncao das duas, e os dois defeitos estao injetados aqui embaixo.
	/// </summary>
	private void FamiliaTodoNpcVestido(Checagem Checa)
	{
		GD.Print("[mundo] -- 5. todo NPC nasce vestido, e a roupa combina com a RACA --");

		// A TABELA DA BANCADA NAO PODE ENVELHECER CALADA. Uma raca nova no `races.json` sem linha no
		// oraculo daqui sairia da varredura -- e a familia ficaria verde sem nunca ter olhado pra ela.
		var semLinha = _racas!.Protos.Keys.Where(
			r => !OQueCadaRacaVeste.Any(t => string.Equals(t.Raca, r, StringComparison.Ordinal))).ToList();
		Checa($"o oraculo desta bancada cobre as {_racas.Protos.Count} racas do races.json",
			  semLinha.Count == 0, string.Join(", ", semLinha));

		// UMA LINHA POR RACA, COM A PECA NOMEADA -- o pedido literal. Cada linha diz o que aquele
		// povo vestiu nas 40 sementes desta bancada, pra a leitura do console ser a resposta e nao
		// um "ok" generico.
		foreach ((string raca, string[] pecas) in OQueCadaRacaVeste)
		{
			(int vestidos, int total, string vistas, string forasteiras) = OQueEstaRacaVestiu(raca);
			bool ok = forasteiras.Length == 0
					  && (pecas.Length == 0 ? vestidos == 0 : vestidos > 0);
			Checa($"{raca,-13} veste [{string.Join(", ", pecas)}]"
				  + (pecas.Length == 0 ? "  (o sprite dele JA e a roupa)" : ""),
				  ok, forasteiras.Length > 0 ? $"fora da lista ou no genero errado: {forasteiras}"
											 : $"{vestidos}/{total} vestidos, visto: {vistas}");
		}

		// O NAMEKUSEIJIN E A UNICA EXCECAO, E ELA E DO DM. `prob(60)` jaqueta + `prob(50)` cachecol
		// deixa 20% sem peca nenhuma (PlanetPopulation.dm:413-414). Esta linha existe pra a excecao
		// ser MEDIDA em vez de tolerada: se um dia o dono pedir "pelo menos uma peca", ela e quem
		// avisa que a regra do DM mudou.
		int nusDeNamek = 0;
		for (ulong s = 1; s <= 400; s++)
			if (_rm!.Aparencia(_moldes!.Get("cidadao")!, "Namekian", "Male", s).Roupa.Count == 0) nusDeNamek++;
		Checa($"o NAMEKUSEIJIN nasce sem peca em ~20% dos casos, e so ele (DM: 60% + 50%) -- medido "
			  + $"{nusDeNamek}/400", nusDeNamek is > 40 and < 160, $"{nusDeNamek}/400");

		// ============================ O CORPO NO MUNDO, E NAO SO O FUNIL ============================
		// Tudo o que esta acima mede `AparenciaDeNpc` -- o funil inteiro, tabela -> `Sanear` --, e e
		// esse objeto que o `Protocol.PutAppearance` escreve no fio. Entre ele e o cidadao de verdade
		// sobra UMA linha: `Visual = AparenciaDeNpc(...)` dentro do `NascerNpc`. Esta prova e a que
		// nao deixa essa linha ser trocada por outra coisa -- a classe de defeito que este projeto ja
		// nomeou como *"a regra existe e ninguem a chama"*.
		//
		// Ela nasce um cidadao por planeta do plano pelo caminho de PRODUCAO e o remove em seguida. Se
		// o ambiente nao deixar (zona sem mapa, plano vazio), ela vira uma linha de SEM COBERTURA em
		// vez de derrubar as familias seguintes: um verde por acidente seria pior que os dois.
		// =========================================================================================
		var nascidos = new List<ServerPlayer>();
		try
		{
			ulong lugar = 990000;
			int conferidos = 0, divergiram = 0;
			foreach (LinhaDePovoamento linha in _moldes!.Plano)
			{
				ServerPlayer? cid = NascerNpc(linha.Molde, ZoneKey.Premade(linha.Planeta), new Vec2(0, 0), ++lugar);
				if (cid == null) continue;
				nascidos.Add(cid);
				conferidos++;

				ulong s = SorteioDeNpc.SementeDe(SeedDoUniverso, linha.Molde,
					Espaco.Misturar(ZoneKey.Premade(linha.Planeta).Hash, lugar, 0));
				string doFunil = string.Join("+", AparenciaDeNpc(_moldes.Get(linha.Molde)!, cid.Race, cid.Genero, s)
					.Roupa.Select(p => $"{NomeDaPeca(p.Caminho)}#{p.Cor}"));
				string noCorpo = string.Join("+", cid.Visual.Roupa.Select(p => $"{NomeDaPeca(p.Caminho)}#{p.Cor}"));
				if (doFunil != noCorpo) divergiram++;
			}
			Checa($"o corpo que o `NascerNpc` poe no mundo veste o que o funil devolveu ({conferidos} planetas)",
				  conferidos > 0 && divergiram == 0, $"{divergiram} divergencia(s)");
		}
		catch (Exception e)
		{
			GD.Print($"[mundo]   [ -- ] o corpo vivo nao pode ser medido aqui ({e.GetType().Name}) -- "
				   + "quem cobre e a `--npcteste`, no primeiro login");
		}
		finally { foreach (ServerPlayer n in nascidos) RemoverNpc(n); }

		Mutacao(Checa,
			"todo NPC que nao tem sprite proprio nasce vestido, e so com peca da lista da raca dele",
			"a roupa sumiu de novo -- `ap.Roupa` nunca e tocado (o estado em que o dono achou o jogo)",
			TodoNpcNasceVestidoNaRacaCerta,
			() => _rm!.Aparencia = (m, r, g, s) => { Appearance a = AparenciaDeNpc(m, r, g, s); a.Roupa.Clear(); return a; },
			() => _rm!.Aparencia = AparenciaDeNpc);

		// **O DEFEITO QUE O DONO NOMEOU.** "tem alguma roupa" ficaria verde com isto dentro.
		Mutacao(Checa,
			"todo NPC que nao tem sprite proprio nasce vestido, e so com peca da lista da raca dele",
			"todo mundo de armadura saiyajin -- o humano, o namekuseijin e ate o Frost Demon",
			TodoNpcNasceVestidoNaRacaCerta,
			() => _rm!.Aparencia = (m, r, g, s) => AparenciaDeNpc(m, "Saiyan", g, s),
			() => _rm!.Aparencia = AparenciaDeNpc);

		Mutacao(Checa,
			"todo NPC que nao tem sprite proprio nasce vestido, e so com peca da lista da raca dele",
			"o humano e o namekuseijin trocaram de guarda-roupa (duas racas certas, na pessoa errada)",
			TodoNpcNasceVestidoNaRacaCerta,
			() => _rm!.Aparencia = (m, r, g, s) => AparenciaDeNpc(
				m, r == "Human" ? "Namekian" : r == "Namekian" ? "Human" : r, g, s),
			() => _rm!.Aparencia = AparenciaDeNpc);

		// O GI FEMININO E UMA LINHA SO DO DM (:397) e ele desaparece sem barulho: a mulher continua
		// vestida, so que com a peca do homem. Sem uma varredura por GENERO isto passa batido.
		Mutacao(Checa,
			"todo NPC que nao tem sprite proprio nasce vestido, e so com peca da lista da raca dele",
			"o Gi FEMININO sumiu: a humana passou a vestir o Gi masculino (PlanetPopulation.dm:397)",
			TodoNpcNasceVestidoNaRacaCerta,
			() => _rm!.Aparencia = (m, r, g, s) => AparenciaDeNpc(m, r, r == "Human" ? "Male" : g, s),
			() => _rm!.Aparencia = AparenciaDeNpc);

		// A PECA FORA DO CATALOGO E A FALHA SILENCIOSA DESTE FUNIL: o `Sanear` a descarta e o corpo
		// sai pelado com o codigo jurando que o vestiu. Foi o que teria acontecido com as seis
		// armaduras do DM antes de elas entrarem no `visual.json`.
		Mutacao(Checa,
			"todo NPC que nao tem sprite proprio nasce vestido, e so com peca da lista da raca dele",
			"a peca escolhida nao esta no catalogo -- o `Sanear` a joga fora e o NPC nasce nu",
			TodoNpcNasceVestidoNaRacaCerta,
			() => _rm!.Aparencia = (m, r, g, s) =>
			{
				Appearance a = AparenciaDeNpc(m, r, g, s);
				a.Roupa.Clear();
				a.Roupa.Add(new PecaDeRoupa("res://Assets/Sprites/Clothes/InventadaPelaBancada.tres", null));
				_visual!.Sanear(a, r, g);
				return a;
			},
			() => _rm!.Aparencia = AparenciaDeNpc);
	}

	/// <summary>
	/// O CRITERIO: as duas afirmacoes juntas, varridas por raca, por genero e por semente.
	///
	/// "vestido" e medido so em quem NAO tem sprite proprio, e a resposta de quem tem vem do Core
	/// (`RoupaDeNpc.OCorpoJaVeste`) e nao de uma segunda lista aqui -- duas listas e a segunda
	/// envelhece calada na primeira raca nova.
	/// </summary>
	private bool TodoNpcNasceVestidoNaRacaCerta()
	{
		foreach ((string raca, string[] pecas) in OQueCadaRacaVeste)
		{
			(int vestidos, _, _, string forasteiras) = OQueEstaRacaVestiu(raca);
			if (forasteiras.Length > 0) return false;

			// SPRITE PROPRIO: tem que sair SEM peca. Uma peca aqui e o Freeza de moletom.
			if (pecas.Length == 0)
			{
				if (vestidos != 0 || !RoupaDeNpc.OCorpoJaVeste(raca)) return false;
				continue;
			}
			if (vestidos == 0) return false;
		}
		return true;
	}

	/// <summary>
	/// O QUE UMA RACA VESTIU nas sementes desta bancada, pelos DOIS generos e pelo funil de producao
	/// (<see cref="AparenciaDeNpc"/> -> `Sanear`), que e o mesmo objeto que vai pro fio.
	/// </summary>
	/// <returns>quantos sorteios sairam com peca, quantos sorteios houve, as pecas vistas, e as que
	/// NAO estao na lista daquela raca.</returns>
	private (int Vestidos, int Total, string Vistas, string Forasteiras) OQueEstaRacaVestiu(string raca)
	{
		string[] esperadas = OQueCadaRacaVeste.First(t => t.Raca == raca).Pecas;
		MoldeDeNpc cidadao = _moldes!.Get("cidadao")!;
		var vistas = new SortedSet<string>(StringComparer.Ordinal);
		var forasteiras = new SortedSet<string>(StringComparer.Ordinal);
		int vestidos = 0, total = 0;

		// QUE PECA DE GENERO UNICO APARECEU, E EM QUEM. Duas contas e nao uma, porque as duas
		// pontas do defeito sao diferentes: a peca no genero ERRADO (a humana de Gi masculino) e a
		// peca que sumiu (o Gi feminino que ninguem mais veste). Ver <see cref="PecaDeUmGeneroSo"/>.
		var noGeneroErrado = new SortedSet<string>(StringComparer.Ordinal);
		var deGeneroVistas = new SortedSet<string>(StringComparer.Ordinal);

		foreach (string genero in new[] { "Male", "Female" })
			for (ulong s = 1; s <= SementesDeRoupa; s++)
			{
				total++;
				List<PecaDeRoupa> roupa = _rm!.Aparencia(cidadao, raca, genero, s).Roupa;
				if (roupa.Count > 0) vestidos++;
				foreach (PecaDeRoupa p in roupa)
				{
					string nome = NomeDaPeca(p.Caminho);
					vistas.Add(nome);
					// O CASAMENTO E POR NOME EXATO, e nao por "contem": `Clothes_TankTop` e prefixo de
					// `Clothes_TankTopandPants`, e `PhoenixFullMakyo` de `PhoenixFullNegativeMakyo`. Um
					// oraculo por substring aceitaria a peca errada nas duas.
					if (!esperadas.Contains(nome, StringComparer.Ordinal)) forasteiras.Add(nome);

					foreach ((string peca, string dono) in PecaDeUmGeneroSo)
					{
						if (peca != nome) continue;
						deGeneroVistas.Add(peca);
						if (dono != genero) noGeneroErrado.Add($"{nome} num {genero}");
					}
				}
			}

		// A PECA DE GENERO UNICO QUE A RACA DEVERIA MOSTRAR E NAO MOSTROU. Sem esta metade, apagar o
		// Gi feminino do jogo ficaria verde: ninguem o veste, logo ninguem o veste no genero errado.
		foreach ((string peca, _) in PecaDeUmGeneroSo)
			if (esperadas.Contains(peca, StringComparer.Ordinal) && !deGeneroVistas.Contains(peca))
				noGeneroErrado.Add($"{peca} nunca apareceu");

		foreach (string s in noGeneroErrado) forasteiras.Add(s);

		return (vestidos, total, string.Join(", ", vistas), string.Join(", ", forasteiras));
	}

	/// <summary>O nome da peca como o DM o escreve -- o arquivo, sem pasta e sem extensao.</summary>
	private static string NomeDaPeca(string caminho) =>
		System.IO.Path.GetFileNameWithoutExtension(caminho);

	// =====================================================================
	// FAMILIA 6 -- A MESMA SEMENTE VESTE O MESMO NPC IGUAL
	// =====================================================================
	/// <summary>
	/// O NPC NAO TEM SAVE (`GameServer.Npc.cs:234`): a roupa dele nao PERSISTE, ela e REDEDUZIDA da
	/// semente a cada nascimento -- o que e mais forte que persistir, porque um save so pode
	/// discordar da semente.
	///
	/// A familia tem um contra-exemplo colado nela, e ele nao e enfeite: "a mesma semente da a mesma
	/// roupa" fica verde com uma tabela de UM item so, e um mundo em que os 148 cidadaos vestem a
	/// mesma camisa e o defeito visivel que o dono reportaria em seguida.
	/// </summary>
	private void FamiliaMesmaSementeMesmaRoupa(Checagem Checa)
	{
		GD.Print("[mundo] -- 6. a mesma semente veste o mesmo NPC igual --");

		Mutacao(Checa,
			"a mesma semente devolve a MESMA roupa (caminho E cor), e sementes diferentes variam",
			"um `Random` sem semente entrou na escolha da peca",
			AMesmaSementeVesteIgual,
			() => _rm!.Aparencia = (m, r, g, s) => AparenciaDeNpc(m, r, g, s + (ulong)Random.Shared.Next(1, 999)),
			() => _rm!.Aparencia = AparenciaDeNpc);

		// O SORRATEIRO: o caminho fica estavel e so a COR passeia. Uma bancada que comparasse so a
		// lista de pecas ficaria verde, e em jogo a armadura do vizinho mudaria de cor a cada
		// reinicio -- que e a queixa original com outra roupa.
		Mutacao(Checa,
			"a mesma semente devolve a MESMA roupa (caminho E cor), e sementes diferentes variam",
			"a COR da 'Armor 8' virou sorteio novo a cada leitura (o caminho fica, a tinta anda)",
			AMesmaSementeVesteIgual,
			() => _rm!.Aparencia = (m, r, g, s) =>
			{
				Appearance a = AparenciaDeNpc(m, r, g, s);
				for (int i = 0; i < a.Roupa.Count; i++)
					if (a.Roupa[i].Cor != null)
						a.Roupa[i] = new PecaDeRoupa(a.Roupa[i].Caminho,
							new Rgb((byte)Random.Shared.Next(256), 80, 80));
				return a;
			},
			() => _rm!.Aparencia = AparenciaDeNpc);

		Mutacao(Checa,
			"a mesma semente devolve a MESMA roupa (caminho E cor), e sementes diferentes variam",
			"a tabela encolheu pra UMA peca: o determinismo cumprido uniformizando o mundo",
			AMesmaSementeVesteIgual,
			() => _rm!.Aparencia = (m, r, g, s) => AparenciaDeNpc(m, r, g, 7),
			() => _rm!.Aparencia = AparenciaDeNpc);
	}

	private bool AMesmaSementeVesteIgual()
	{
		MoldeDeNpc cidadao = _moldes!.Get("cidadao")!;
		string Vestido(string raca, string genero, ulong s) => string.Join("+",
			_rm!.Aparencia(cidadao, raca, genero, s).Roupa.Select(p => $"{p.Caminho}#{p.Cor}"));

		// ---------------------------------------------------------- a mesma semente, duas vezes
		foreach (string raca in new[] { "Saiyan", "Human", "Namekian", "Demon" })
			for (ulong s = 1; s <= SementesDeRoupa; s++)
				if (Vestido(raca, "Male", s) != Vestido(raca, "Male", s)) return false;

		// ---------------------------------------------------------- ...e ela VARIA entre sementes
		// (o contra-exemplo: sem esta metade, "vestir todo mundo igual" seria determinismo perfeito)
		foreach (string raca in new[] { "Saiyan", "Human" })
			if (Enumerable.Range(1, SementesDeRoupa)
					.Select(i => Vestido(raca, "Male", (ulong)i)).Distinct().Count() < 2) return false;

		// ---------------------------------------------------------- ...e a COR da armadura tambem
		// sai da semente: ela viaja no fio separada do caminho, e perde-la calada ja foi defeito real
		// deste projeto (ver `PecaDeRoupaConverter`).
		var tintas = new HashSet<string>(StringComparer.Ordinal);
		for (ulong s = 1; s <= 200; s++)
			foreach (PecaDeRoupa p in _rm!.Aparencia(cidadao, "Saiyan", "Male", s).Roupa)
				if (p.Cor != null) tintas.Add($"{s}:{p.Cor}");
		return tintas.Count > 1;
	}

	// =====================================================================
	// FAMILIA 7 -- A ROUPA SOBREVIVE A REPOSICAO PELA MANUTENCAO
	// =====================================================================
	/// <summary>
	/// A MANUTENCAO DO POVOAMENTO REPOE O VILAREJO A CADA 5 MINUTOS (`Manutencao`,
	/// `GameServer.Povoamento.cs`), e o `lugar` dela e um contador INCREMENTAL por planeta
	/// (`_lugaresDoPovoamento`) -- entao o cidadao reposto e uma PESSOA NOVA, com outro nome e outra
	/// raca, e nao o mesmo ressuscitado. E por isso que esta familia afirma duas coisas e nao uma:
	///
	///   * **quem a manutencao repoe nasce vestido** -- e a exigencia de verdade, e ela vale pra o
	///     lugar 1 e pra o lugar 300, ou seja pra o vilarejo de daqui a vinte e cinco horas;
	///   * **o mesmo lugar devolve a mesma roupa** -- que e o que faz o reinicio do servidor devolver
	///     o povo identico, roupa e tinta.
	///
	/// A varredura vai ate <see cref="LugaresDeReposicao"/> de propósito: um defeito que so aparece
	/// depois de N reposicoes (uma lista que se esgota, um indice que estoura) e invisivel numa
	/// bancada que olha o primeiro habitante.
	/// </summary>
	private void FamiliaDaReposicao(Checagem Checa)
	{
		GD.Print("[mundo] -- 7. a roupa sobrevive a reposicao pela manutencao --");

		GD.Print($"[mundo]        a manutencao numera o lugar por planeta e ele so cresce "
			   + $"(`_lugaresDoPovoamento`) -- esta familia varre os {LugaresDeReposicao} primeiros de cada planeta do plano");

		Mutacao(Checa,
			$"os {LugaresDeReposicao} primeiros repostos de cada planeta nascem vestidos, e o mesmo lugar veste igual",
			"a roupa passou a ser sorteada NO NASCIMENTO e nao derivada do lugar (o reposto volta diferente)",
			AReposicaoVesteIgual,
			() => _rm!.Aparencia = (m, r, g, s) => AparenciaDeNpc(m, r, g, s ^ (ulong)Random.Shared.Next(1, 999)),
			() => _rm!.Aparencia = AparenciaDeNpc);

		// O DEFEITO COM CARA DE DETALHE: os primeiros nascem vestidos e os repostos, nao. Em jogo
		// isso e o vilarejo que vai ficando pelado ao longo da tarde -- e uma bancada que olhasse o
		// lugar 1 nao veria nada.
		Mutacao(Checa,
			$"os {LugaresDeReposicao} primeiros repostos de cada planeta nascem vestidos, e o mesmo lugar veste igual",
			"a partir de certo lugar a tabela devolve vazio (o vilarejo vai ficando pelado)",
			AReposicaoVesteIgual,
			() => _rm!.Aparencia = (m, r, g, s) =>
			{
				Appearance a = AparenciaDeNpc(m, r, g, s);
				if (s % 3 == 0) a.Roupa.Clear();
				return a;
			},
			() => _rm!.Aparencia = AparenciaDeNpc);

		// O DEFEITO QUE ESTE PROJETO JA COMETEU. Ver <see cref="RegrasDoMundo.LugarDoHabitante"/>: sem
		// a zona na conta, o habitante 4 da Terra e o 4 de Vegeta sao a mesma pessoa vestida igual --
		// e da pra ver no log de nascimento, `BP 2.318` nos tres planetas.
		//
		// A FILA E REMONTADA nas duas pontas porque este defeito muda o SORTEIO e nao a aparencia: a
		// lembranca de <see cref="FilaDeReposicao"/> vale enquanto so a roupa e injetada, e deixa de
		// valer aqui. Esquecer isto faria a familia ficar verde com o defeito dentro -- lembranca que
		// sobrevive ao mutante e um cego, nao uma otimizacao.
		Mutacao(Checa,
			$"os {LugaresDeReposicao} primeiros repostos de cada planeta nascem vestidos, e o mesmo lugar veste igual",
			"a ZONA saiu da conta do lugar: o habitante 4 da Terra e o 4 de Vegeta viram a mesma pessoa",
			AReposicaoVesteIgual,
			() => { _rm!.LugarDoHabitante = (_, lugar) => lugar; _filaDeReposicao = null; },
			() =>
			{
				_rm!.LugarDoHabitante = (zona, lugar) => Espaco.Misturar(zona.Hash, lugar, 0);
				_filaDeReposicao = null;
			});
	}

	/// <summary>Ate onde a familia 7 varre o contador da manutencao. Ver o cabecalho dela.</summary>
	private const int LugaresDeReposicao = 120;

	/// <summary>
	/// O CRITERIO, PELO CAMINHO DE PRODUCAO: o `lugar` vira semente pela MESMA conta do
	/// <see cref="NascerNpc"/> -- `SementeDe(universo, molde, Misturar(zona, lugar, 0))` --, e a raca
	/// sai do sorteio de verdade, que e quem consulta o berco do planeta.
	/// </summary>
	private bool AReposicaoVesteIgual()
	{
		MoldeDeNpc cidadao = _moldes!.Get("cidadao")!;

		var porLugar = new Dictionary<ulong, HashSet<ulong>>();

		// O PLANETA fica no descarte: ele existe na fila pra quem for LER a lista num diagnostico
		// (`--diagvestido` conta corpo por planeta), e nao pra esta conta -- aqui o que importa e que
		// a semente do lugar 4 nao se repita, venha ela de onde vier.
		foreach ((_, ulong lugar, string raca, string genero, ulong semente) in FilaDeReposicao())
		{
			List<PecaDeRoupa> a = _rm!.Aparencia(cidadao, raca, genero, semente).Roupa;

			// 1. NASCEU VESTIDO? -- so cobrado de quem nao tem sprite proprio, e o Namekuseijin fica
			//    de fora porque os 20% nus dele sao o DM literal (ver a familia 5).
			if (!RoupaDeNpc.OCorpoJaVeste(raca) && raca != "Namekian" && a.Count == 0) return false;

			// 2. E O MESMO LUGAR VESTE IGUAL? -- e a rededucao: o NPC nao tem save.
			List<PecaDeRoupa> b = _rm.Aparencia(cidadao, raca, genero, semente).Roupa;
			if (string.Join("+", a.Select(p => $"{p.Caminho}#{p.Cor}"))
				!= string.Join("+", b.Select(p => $"{p.Caminho}#{p.Cor}"))) return false;

			// 3. E DOIS PLANETAS NAO REPOEM A MESMA PESSOA. O contador da manutencao e por planeta,
			//    entao o lugar 4 existe em todos eles -- e sem a zona na conta os seis lugares 4 do
			//    mundo sao o mesmo corpo. Ver `RegrasDoMundo.LugarDoHabitante`.
			if (!porLugar.TryGetValue(lugar, out HashSet<ulong>? sementes))
				porLugar[lugar] = sementes = [];
			if (!sementes.Add(semente)) return false;
		}
		return true;
	}

	/// <summary>
	/// OS PRIMEIROS <see cref="LugaresDeReposicao"/> LUGARES DE CADA PLANETA DO PLANO, montados pela
	/// MESMA conta do <see cref="NascerNpc"/>: `SementeDe(universo, molde, Misturar(zona, lugar, 0))`.
	/// A raca sai do sorteio de producao, que e quem consulta o berco do planeta -- cravar uma raca
	/// aqui mediria uma suposicao minha em vez do povo que o servidor vai pôr no mundo.
	/// </summary>
	private List<(string Planeta, ulong Lugar, string Raca, string Genero, ulong Semente)> FilaDeReposicao()
	{
		if (_filaDeReposicao != null) return _filaDeReposicao;

		MoldeDeNpc cidadao = _moldes!.Get("cidadao")!;
		var fila = new List<(string, ulong, string, string, ulong)>();

		foreach (LinhaDePovoamento linha in _moldes.Plano)
		{
			var zona = ZoneKey.Premade(linha.Planeta);
			for (ulong lugar = 1; lugar <= LugaresDeReposicao; lugar++)
			{
				ulong semente = SorteioDeNpc.SementeDe(
					SeedDoUniverso, cidadao.Id, _rm!.LugarDoHabitante(zona, lugar));
				FichaSorteada f = SorteioDeNpc.Sortear(cidadao, semente, _racas!, _skills, zona.Name, 0);
				fila.Add((linha.Planeta, lugar, f.Raca, f.Genero, semente));
			}
		}

		_filaDeReposicao = fila;
		return fila;
	}

	// =====================================================================
	// FAMILIA 8 -- O CHEFE DE SAGA CONTINUA COM A APARENCIA DELE
	// =====================================================================
	/// <summary>
	/// O RECORTE DO PEDIDO: um chefe cujo sprite ja o veste (Freeza, Cell, Boo) **nao entra no
	/// sorteio racial**, e o DM concorda por omissao -- as fabricas dos tres nao chamam `npc_wear_*`
	/// uma unica vez. Quem tem `roupa` cravada no molde veste EXATAMENTE aquilo, que e como os
	/// Androides 17 e 18 sao vestidos la (BossEvents.dm:499 e :508) e como o kit de elite do
	/// `guardiao_saiyajin` chega ao mundo neste port.
	///
	/// O `majin_boo` e o que da dente a esta familia: a raca dele (Majin) NAO esta no
	/// `OCorpoJaVeste`, entao ele so nasce sem peca por ser chefe. Um corte de chefe que sumisse
	/// poria uma regata nele -- e essa e a primeira injecao aqui.
	/// </summary>
	private void FamiliaDoChefe(Checagem Checa)
	{
		GD.Print("[mundo] -- 8. o chefe de saga continua com a aparencia dele --");

		foreach (MoldeDeNpc chefe in _moldes!.Todos.Where(m => m.EhChefe))
		{
			(string raca, List<PecaDeRoupa> roupa) = ComoOChefeSeVeste(chefe);
			Checa($"{chefe.Id,-18} ({raca}) veste "
				  + (chefe.Roupa.Length == 0 ? "NADA (o sprite dele ja o veste)"
											 : $"[{string.Join(", ", chefe.Roupa)}]"),
				  chefe.Roupa.Length == 0
					  ? roupa.Count == 0
					  : roupa.Count == chefe.Roupa.Length
						&& chefe.Roupa.All(n => roupa.Any(p => NomeDaPeca(p.Caminho) == n)),
				  string.Join(", ", roupa.Select(p => NomeDaPeca(p.Caminho))));
		}

		Mutacao(Checa,
			"chefe sem `roupa` no molde nasce sem peca, e chefe com `roupa` veste exatamente aquilo",
			"o chefe voltou a entrar no sorteio racial (Freeza de moletom, Boo de regata)",
			OChefeMantemAAparencia,
			() => _rm!.Aparencia = (m, r, g, s) =>
			{
				Appearance a = AparenciaDeNpc(m, r, g, s);
				if (m.EhChefe && m.Roupa.Length == 0)
				{
					a.Roupa.AddRange(RoupaDeNpc.Vestir(_visual!, r, g, s));
					_visual!.Sanear(a, r, g);
				}
				return a;
			},
			() => _rm!.Aparencia = AparenciaDeNpc);

		Mutacao(Checa,
			"chefe sem `roupa` no molde nasce sem peca, e chefe com `roupa` veste exatamente aquilo",
			"a `roupa` cravada no molde perdeu pra tabela da raca (o Androide 17 de armadura)",
			OChefeMantemAAparencia,
			() => _rm!.Aparencia = (m, r, g, s) =>
			{
				Appearance a = AparenciaDeNpc(m, r, g, s);
				a.Roupa.Clear();
				a.Roupa.AddRange(RoupaDeNpc.Vestir(_visual!, r, g, s));
				_visual!.Sanear(a, r, g);
				return a;
			},
			() => _rm!.Aparencia = AparenciaDeNpc);

		// O KIT DE ELITE SAO TRES PECAS (armadura + luvas + botas, PlanetPopulation.dm:381-383). Uma
		// so delas sumir e a classe de defeito que o `Aparar` do `RoupaDeNpc` existe pra evitar, e
		// que o `Sanear` faria calado se o teto do guarda-roupa apertasse.
		Mutacao(Checa,
			"chefe sem `roupa` no molde nasce sem peca, e chefe com `roupa` veste exatamente aquilo",
			"o kit de elite do guardiao encolheu pra a primeira peca (as luvas e as botas somem)",
			OChefeMantemAAparencia,
			() => _rm!.Aparencia = (m, r, g, s) =>
			{
				Appearance a = AparenciaDeNpc(m, r, g, s);
				if (a.Roupa.Count > 1) a.Roupa.RemoveRange(1, a.Roupa.Count - 1);
				return a;
			},
			() => _rm!.Aparencia = AparenciaDeNpc);
	}

	private bool OChefeMantemAAparencia()
	{
		foreach (MoldeDeNpc chefe in _moldes!.Todos.Where(m => m.EhChefe))
		{
			(_, List<PecaDeRoupa> roupa) = ComoOChefeSeVeste(chefe);

			if (chefe.Roupa.Length == 0) { if (roupa.Count > 0) return false; continue; }

			if (roupa.Count != chefe.Roupa.Length) return false;
			foreach (string nome in chefe.Roupa)
				if (!roupa.Any(p => NomeDaPeca(p.Caminho) == nome)) return false;
		}
		return true;
	}

	/// <summary>
	/// A RACA DO CHEFE SAI DO SORTEIO DE PRODUCAO e nao do `molde.Racas[0]`: e o sorteio que consulta
	/// o berco e as linhagens, e cravar a raca aqui mediria uma suposicao minha em vez do que o
	/// servidor vai pôr no mundo.
	/// </summary>
	private (string Raca, List<PecaDeRoupa> Roupa) ComoOChefeSeVeste(MoldeDeNpc chefe)
	{
		const ulong semente = 0xDB26_0814_C4EF_0001UL;
		FichaSorteada f = SorteioDeNpc.Sortear(chefe, semente, _racas!, _skills, "Namek", 0);
		return (f.Raca, _rm!.Aparencia(chefe, f.Raca, f.Genero, semente).Roupa);
	}

	// =====================================================================
	// O RETRATO DE UM UNIVERSO
	// =====================================================================
	/// <summary>
	/// O QUE UM MUNDO E, pra efeito de comparacao: nao a semente, e o que sai dela. Comparar sementes
	/// provaria so que dois numeros sao diferentes -- e ha um servidor imaginavel (o do terceiro
	/// defeito da familia 1) em que os numeros diferem e o mundo e o mesmo.
	/// </summary>
	private sealed record RetratoDoUniverso(
		ulong Semente, ulong Universo, string Planetas, string Censo,
		string Nomes, string Classes, string Roupas);

	private RetratoDoUniverso Retratar(ulong semente)
	{
		List<string> censo = _rm!.Censo(semente);
		return new RetratoDoUniverso(
			semente,
			_rm.Universo(semente),
			_rm.Planetas(semente),
			string.Join(";", censo),
			string.Join(",", censo.Select(l => l.Split('|')[1])),
			string.Join(",", censo.Select(l => l.Split('|')[3])),
			RoupaDoCenso(censo));
	}

	/// <summary>
	/// DOIS MUNDOS NOVOS, pelo caminho de producao: pasta vazia -> `CarregarSemente` sorteia. Apagar
	/// a pasta entre os dois e o que o WIPE faz no disco; a familia 3 mede a limpeza inteira.
	/// </summary>
	private (RetratoDoUniverso A, RetratoDoUniverso B) DoisMundosNovos()
	{
		LimparACaixa();
		_rm!.Carregar();
		RetratoDoUniverso a = Retratar(SeedDoUniverso);

		LimparACaixa();
		_rm.Carregar();
		RetratoDoUniverso b = Retratar(SeedDoUniverso);

		return (a, b);
	}

	/// <summary>
	/// OS PLANETAS DE 49 CELULAS DE SISTEMA em volta da origem, com nome e posicao. Uma varredura
	/// larga de proposito: os pre-feitos (a Terra, Namek, Vegeta) sao ANCORADOS e existem em todo
	/// universo, entao uma janela estreita mostraria justamente a parte que nao muda -- foi a mesma
	/// armadilha do enquadramento das fotos da carta estelar.
	/// </summary>
	private static string PlanetasDoUniverso(ulong semente)
	{
		var nomes = new List<string>();
		for (int sy = -3; sy <= 3; sy++)
			for (int sx = -3; sx <= 3; sx++)
			{
				if (Sistemas.Do(semente, sx, sy) is not { } sistema) continue;
				foreach (PlanetaNoEspaco p in sistema.Planetas())
					nomes.Add($"{p.Nome}@{p.Pos.X:0}/{p.Pos.Y:0}");
			}
		nomes.Sort(StringComparer.Ordinal);
		return string.Join(";", nomes);
	}

	/// <summary>
	/// QUEM NASCE NESTE UNIVERSO, pelo sorteio DE PRODUCAO e pela mesma conta de semente do
	/// <see cref="NascerNpc"/>. A media do servidor entra como zero (e nao `MediaDoServidor()`) de
	/// proposito: ela depende de quem esta online, e um censo que mude com o numero de jogadores nao
	/// serve pra comparar dois universos.
	/// </summary>
	private List<string> CensoDoUniverso(ulong semente)
	{
		// LEMBRAR E SEGURO PORQUE O SORTEIO E PURO: de (universo, molde, lugar) sai sempre o mesmo
		// cidadao, e nem `_moldes` nem `_racas` mudam enquanto esta bancada roda. Sem isto, a familia
		// 2 -- que relê o mesmo mundo seis vezes por criterio, tres vezes por defeito -- pagaria o
		// sorteio inteiro cada volta, e o custo dela e o que decidiria quantos habitantes cabem no
		// censo. A chave e a semente EFETIVA, entao o mutante "derivado do relogio" continua sendo
		// medido: ele muda a semente que chega aqui.
		if (_censoLembrado.TryGetValue(semente, out List<string>? pronto)) return pronto;

		var linhas = new List<string>();
		MoldeDeNpc cidadao = _moldes!.Get("cidadao")!;

		foreach (LinhaDePovoamento linha in _moldes.Plano)
		{
			var zona = ZoneKey.Premade(linha.Planeta);
			for (ulong lugar = 1; lugar <= HabitantesPorPlaneta; lugar++)
			{
				ulong s = SorteioDeNpc.SementeDe(semente, cidadao.Id, Espaco.Misturar(zona.Hash, lugar, 0));
				FichaSorteada f = SorteioDeNpc.Sortear(cidadao, s, _racas!, _skills, zona.Name, 0);
				linhas.Add($"{linha.Planeta}|{f.Nome}|{f.Raca}|{f.Classe}|{f.Ficha.BP:0.000}|{s}");
			}
		}

		_censoLembrado[semente] = linhas;
		return linhas;
	}

	/// <summary>
	/// A ROUPA DESSE CENSO -- e ela entra no retrato porque os dois pedidos do dono andam juntos: um
	/// universo novo com a MESMA gente vestida da MESMA forma nao e um universo novo.
	/// </summary>
	private string RoupaDoCenso(List<string> censo)
	{
		MoldeDeNpc cidadao = _moldes!.Get("cidadao")!;
		var partes = new List<string>();

		foreach (string linha in censo)
		{
			string[] campos = linha.Split('|');
			ulong s = ulong.Parse(campos[5]);
			partes.Add(string.Join("+", _rm!.Aparencia(cidadao, campos[2], "Male", s).Roupa
				.Select(p => $"{NomeDaPeca(p.Caminho)}#{p.Cor}")));
		}
		return string.Join(";", partes);
	}
}
