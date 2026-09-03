using System.Text.Json;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// A DESTRUICAO DE PLANETA -- os dois caminhos do `Area_Death.dm`, inteiros.
///
/// ============================ OS DOIS CAMINHOS, E POR QUE SAO UM SISTEMA SO ============================
///   (a) **MORTE LENTA** (`area/proc/Planet_Death`, `Area_Death.dm:8-50`): quatro estagios de 200,
///       300, 300 e 400 s -- **20 minutos**. A cada estagio morre uma fatia dos habitantes
///       (25/50/75/100%), a partir do 1 a noite nao acaba mais, e a partir do 2 o chao comeca a se
///       desfazer perto de quem esta olhando. No fim, ela **chama a destruicao**.
///   (b) **DESTRUICAO IMEDIATA** (`DestroyPlanet`, `Area_Death.dm:75-147`): ~5 minutos de tremor,
///       explosao e ceu de poeira, e entao o **commit** -- dano em area, morte de quem estava
///       caido, evacuacao pro espaco e o planeta na lista de mortos.
///
/// Os dois terminam no MESMO commit, e e por isso que sao um arquivo so: (a) e um prologo de (b).
/// ==================================================================================================
///
/// ============================ O QUE PERSISTE, E POR QUE ISSO E O CORACAO DO ARQUIVO ============================
/// O original guarda o estado em tres vars de area (`Area_Death.dm:5-7`) e **so uma delas persiste**:
/// `planet_dying` volta do disco, `planet_death_stage` e `death_proc_running` sao `tmp`. O tique de
/// `Weather.dm:72-74` le a primeira e re-`spawn`a o `Planet_Death` -- do estagio 0. Resultado: um
/// planeta com o pavio aceso **recomeca a morrer a cada boot**, para sempre. Foi preciso um remendo
/// no `Boss_Events_Init` (`BossEvents.dm:966-971`) pra desarmar isso na mao.
///
/// Aqui **o registro inteiro persiste junto**, numa escrita so: fase, estagio, quanto FALTA do passo
/// atual, o BP do algoz e o motivo (ver <see cref="EstadoDaMorte"/>). O boot nao reconstroi nada --
/// ele **continua**. E o que falta e guardado em SEGUNDOS QUE RESTAM, e nao num instante do relogio
/// do universo: aquele relogio anda com o servidor desligado, e um prazo absoluto faria uma noite
/// fora consumir o pavio inteiro.
/// ==========================================================================================================
///
/// ============================ SERVIDOR VAZIO ADIA -- E SO O PAVIO ============================
/// A licao esta escrita no proprio original, no ultimato do Freeza (`BossEvents.dm:562`): o planeta
/// nao pode explodir de madrugada sem ninguem pra impedir. **Vale aqui, e vale so pra (a)**:
///   * o PAVIO LENTO congela com o servidor vazio. Os estagios 2 e 3 sao literalmente "destruir chao
///     perto de um jogador" (`Area_Death.dm:64`) -- sem jogador eles nao tem o que fazer, e deixar o
///     relogio correr transformaria vinte minutos de aviso em "o planeta ja era quando voce entrou".
///   * a EXPLOSAO nao congela. Ela so comeca com alguem online (o ultimato da saga ja garante isso,
///     e o verb exige um jogador vivo no planeta), e uma vez comecada ela e um FATO e nao um
///     espetaculo: se todo mundo deslogar no meio dos cinco minutos, o mundo acaba assim mesmo.
/// =====================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// O ESTADO
	// =====================================================================
	/// <summary>
	/// O LIVRO DOS MORTOS -- a `PlanetDisableList` (`Area_Death.dm:144`) com chave honesta.
	///
	/// Ver <see cref="ChaveDePlaneta"/>: o DM chaveia por NOME, e no port o nome de um planeta
	/// procedural nao e unico. Aqui a chave e a seed.
	/// </summary>
	private readonly RegistroDeMortos _mortos = new();

	/// <summary>
	/// ============================ ELE PRECISA ENTRAR NA LISTA DO `CharacterStore` ============================
	/// Este arquivo mora na MESMA pasta das contas, e o `CharacterStore` monta o nome de uma conta
	/// como "conta saneada + .json". Sem a entrada em `CharacterStore.NaoSaoContas`, o painel de admin
	/// cuspiria "conta ilegivel" a cada abertura E uma conta chamada "planetas mortos" gravaria por
	/// cima do estado do mundo. Ja esta la -- e esta nota existe pra que ninguem tire.
	/// ================================================================================================
	/// </summary>
	private string CaminhoDosMortos => System.IO.Path.Combine(_store?.Pasta ?? ".", "planetas-mortos.json");

	/// <summary>O relogio dos tremores da explosao, por chave de planeta. NAO persiste -- e cadencia visual.</summary>
	private readonly Dictionary<string, double> _proximoTremor = [];

	/// <summary>
	/// AS FERIDAS DOS MUNDOS VIVOS -- o dano de ki que veio do espaco, por chave de planeta.
	///
	/// **Fora do <see cref="_mortos"/> de proposito, e fora do disco de proposito.** As duas razoes
	/// estao escritas por inteiro em <see cref="FeridaDeMundo"/>; a curta e: um planeta ferido nao
	/// esta condenado, e `ZonaCondenada` desliga meio jogo (povoamento, berco, invasao, pouso).
	///
	/// Ele se limpa sozinho: a cicatrizacao remove a entrada quando o dano chega a zero, e a
	/// condenacao remove quando o mundo cai. Num servidor onde ninguem esta bombardeando nada, este
	/// dicionario esta vazio.
	/// </summary>
	private readonly Dictionary<string, FeridaDeMundo> _feridasDeMundo = [];

	/// <summary>
	/// A CARGA DE 30 s DO VERB `Planet_Destroy`, por jogador -- `sleep(300)` (`Planets.dm:355`).
	///
	/// NAO E FASE DO PLANETA, e essa distincao e do original: durante a carga o `DestroyPlanet` ainda
	/// nao foi chamado e a area nao tem estado nenhum. Se quem carrega cai, **nada aconteceu com o
	/// planeta**. Por isso mora aqui, no ator, e nao no registro de mortos.
	/// </summary>
	private readonly Dictionary<int, (double Faltam, ZoneKey Zona, double Bp)> _cargaDoPlanetDestroy = [];

	// =====================================================================
	// AS SONDAS DA AGONIA -- os cinco pontos onde a bancada consegue INJETAR um defeito
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ESTE PUNHADO DE `Func` EXISTE ============================
	/// *"Uma prova que nunca ficou vermelha e uma frase."* Este arquivo inteiro esta coberto por
	/// checagens verdes, e verde nao prova nada sozinho: a bancada precisa poder QUEBRAR cada regra
	/// e exigir que a prova correspondente caia. Sem um ponto de injecao, "a rampa e consumida" e
	/// "o portao segura o fraco" continuariam verdes num sistema em que alguem apagou as duas.
	///
	/// Mesmo desenho das <see cref="SondasDoVacuo"/>, e pelo mesmo motivo. Sao **cinco** e nao mais:
	/// cada uma existe pra um defeito nomeado que este projeto cometeria de boa fe --
	///   1. <see cref="Intensidade"/> ...... *"a rampa virou constante"*. E ela e UMA so de proposito:
	///      matar esta sonda tem que derrubar o ceu, o tremor, o chao e a cratera de uma vez. Se um
	///      efeito sobrevivesse, e porque ele tem nocao PROPRIA de intensidade -- que e exatamente o
	///      defeito que a rampa unica existe pra impedir.
	///   2. <see cref="SegundosDeExplosao"/> ... *"os cinco minutos viraram outra coisa"*.
	///   3. <see cref="FuriaDaExplosao"/> ...... *"alguem 'corrigiu' o dano da explosao e ele deixou de
	///      ser a vida do planeta"* -- a metade que o dono amarrou com todas as letras.
	///   4/5. <see cref="ForteOBastante"/> + <see cref="DanoNoMundo"/> ... *"o portao de forca foi
	///      removido"*. Sao duas porque o portao mora em DOIS lugares (a recusa que fala com o
	///      jogador e o zero que sai de dentro da formula), e um defeito honesto apagaria os dois.
	///   6. <see cref="TextoDoFraco"/> ......... *"o aviso resolveu ajudar e disse o numero"*, que e a
	///      unica regra deste sistema que o dono escreveu como proibicao.
	///
	/// **Nulas em jogo**: um `??=` no primeiro uso e uma chamada indireta por evento raro (o aviso do
	/// fraco sai no maximo a cada 4 s por pessoa; a intensidade, uma vez por tique e por planeta
	/// agonizante). Nenhuma delas esta num laco quente.
	/// ==========================================================================================
	/// </summary>
	internal sealed class SondasDaAgonia
	{
		/// <summary>A UNICA fracao de agonia do sistema. Ver <see cref="MortePlanetaria.Intensidade"/>.</summary>
		public Func<EstadoDaMorte, double> Intensidade = MortePlanetaria.Intensidade;

		/// <summary>Os 310 s do `sleep(3100)` (`Area_Death.dm:129`).</summary>
		public double SegundosDeExplosao = MortePlanetaria.SegundosDeExplosao;

		/// <summary>
		/// A JANELA DO RESCALDO -- quanto tempo o `Faltam` de um mundo destruido continua descendo pra
		/// baixo de zero, que e o unico jeito de o cliente saber HA QUANTO TEMPO ele morreu.
		///
		/// Sonda porque o defeito aqui e o mais silencioso de todo este arquivo: se a janela virar zero,
		/// **nada quebra no servidor** -- nenhum log, nenhuma excecao, nenhum estado invalido. O que
		/// acontece e que quem chega na orbita depois do estouro deixa de ver os destrocos, enquanto
		/// quem estava online continua vendo. Um defeito que so aparece em duas telas ao mesmo tempo, e
		/// que uma bancada de um cliente so nunca pegaria.
		/// </summary>
		public double SegundosDosDestrocos = DestrocosDeMundo.SegundosDaJanela;

		/// <summary>
		/// A furia que a EXPLOSAO gasta. Nula = <see cref="FuriaDoPlaneta"/>, a mesma chamada que a
		/// vida do mundo usa -- e o defeito injetavel e justamente as duas deixarem de ser uma.
		/// </summary>
		public Func<ZoneKey, double>? FuriaDaExplosao;

		/// <summary>
		/// A RECUSA DE UM MUNDO QUE JA ESTA MORRENDO. Nula = <see cref="ZonaCondenada"/>.
		///
		/// Sonda porque o defeito aqui e mudo e plausivel: trocar `ZonaCondenada` (que ja diz "sim"
		/// durante os 20 minutos do pavio lento) por `ZonaMorta` (que so diz "sim" depois do commit)
		/// faria o bombardeio **acelerar uma morte ja em curso**, e nada no jogo ficaria diferente
		/// ate um mundo cair antes da hora.
		/// </summary>
		public Func<ZoneKey, bool>? MundoJaCondenado;

		/// <summary>O portao do K5, no ramo que FALA com o jogador.</summary>
		public Func<double, double, bool> ForteOBastante = MortePlanetaria.ForteOBastantePraFerirOMundo;

		/// <summary>O portao do K5, no ramo que faz a CONTA (ele mora dentro da formula).</summary>
		public Func<double, double, double, double> DanoNoMundo = MortePlanetaria.DanoNoMundo;

		/// <summary>
		/// A FRASE DO K5, e ela e sonda porque e a unica regra que o dono escreveu como proibicao:
		/// *"nao e pra dizer o bp minimo ou outra coisa"*. Sem poder injetar um vazamento, a
		/// varredura por digito ficaria verde para sempre -- inclusive no dia em que ela parasse de
		/// varrer.
		/// </summary>
		public Func<string, string> TextoDoFraco = nome =>
			$"sua energia se dissolve na atmosfera de {nome}. "
			+ "Você não é forte o suficiente para ferir um mundo.";
	}

	private SondasDaAgonia? _sondasDaAgonia;

	/// <summary>Ver <see cref="SondasDaAgonia"/>. A bancada troca o campo inteiro e devolve `null`.</summary>
	private SondasDaAgonia Agonia => _sondasDaAgonia ??= new SondasDaAgonia();

	// =====================================================================
	// CARGA E SALVAMENTO
	// =====================================================================
	private void CarregarPlanetasMortos()
	{
		try
		{
			if (!System.IO.File.Exists(CaminhoDosMortos)) return;

			var lista = JsonSerializer.Deserialize<List<EstadoDaMorte>>(
				System.IO.File.ReadAllText(CaminhoDosMortos),
				new JsonSerializerOptions { IncludeFields = true });
			if (lista == null) return;

			foreach (EstadoDaMorte e in lista)
			{
				// ============================ ESTADO IMPOSSIVEL E DESCARTADO, E GRITA ============================
				// Mesma disciplina do `CarregarSagas`: um registro sem chave, ou com uma fase que nao
				// existe, e save corrompido -- e o silencio esconderia um planeta que morreu por engano.
				// ==========================================================================================
				if (e.Chave.Length == 0)
				{
					GD.PushWarning("[server] planetas-mortos.json: registro sem chave -- descartado");
					continue;
				}
				if (e.Fase == FaseDaMorte.Vivo)
				{
					GD.PushWarning($"[server] planetas-mortos.json: '{e.Nome}' esta no arquivo como VIVO "
								 + "-- ausencia e a resposta pra vivo; registro descartado");
					continue;
				}

				FecharAJanelaDoRescaldo(e);
				_mortos.Por(e);
			}

			int destruidos = 0, morrendo = 0;
			foreach (EstadoDaMorte e in _mortos.Todos)
				if (MortePlanetaria.EstaMorto(e.Fase)) destruidos++; else morrendo++;

			GD.Print($"[server] planetas mortos: {destruidos} destruido(s), {morrendo} em curso "
				   + $"({string.Join(", ", _mortos.Todos.Select(e => $"{e.Nome}={e.Fase}"))})");

			// ============================ O PLANETA DE RECUO MORTO E UM ANUNCIO, NAO UMA NOTA DE RODAPE ============================
			// Esta linha existe por medicao e nao por precaucao: a Terra passou dias marcada como
			// destruida neste arquivo e o unico aviso era a linha acima -- seca, no meio de trinta linhas
			// de boot -- mais uma linha por CORPO la no berco ("'Earth' esta destruido -- o corpo vai pra
			// Namek"), que so aparece depois de alguem nascer. O dono viu o SINTOMA ("todo mundo nasce em
			// Namek") sem nunca ver a CAUSA, e uma bancada chegou a escrever no proprio comentario que "no
			// save vivo a Terra esta condenada" como se fosse paisagem (`GameServer.PovoamentoTeste.cs`).
			//
			// A `SpawnZone` nao e um planeta qualquer: e o lar de **10 das 24 racas** (contado pela
			// `--bercoprova`: Alien, Android, BioAndroid, Demigod, Dog, Halfbreed, Human, Majin,
			// Shapeshifter, SpiritDoll, mais todo Saiyajin de classe baixa). O "13" que esta nota
			// trazia era a contagem de LINHAS VERMELHAS da `--bercovivo` (10 racas + o classe-baixa +
			// os dois perfis Human da familia 5), e nao de racas. Com ela morta o jogo continua
			// rodando e mentindo baixinho.
			//
			// **O TEXTO DO AVISO MUDOU COM A REGRA.** Ele dizia *"quem tem berco nele nasce e RENASCE
			// no primeiro pre-feito vivo da carta"* -- era o `ZonaDeRecuoViva`, e ele foi DELETADO.
			// Hoje quem perde o berco cai no REFUGIO (`GameServer.Refugio.cs`): o dominio que ele
			// conquistou, ou o mundo vivo mais perto de casa. O aviso continua valendo pelo mesmo
			// motivo -- ninguem pousa aqui vindo de orbita e o povoamento nao repovoa --, mas dizer o
			// destino errado seria pior que nao dizer nada.
			// ==================================================================================================================
			if (ZonaMorta(SpawnZone))
				GD.PushWarning($"[server] O PLANETA DE RECUO ('{SpawnZone.Name}') ESTA DESTRUIDO: quem tem "
							 + "berco nele nasce e RENASCE no REFUGIO (um dominio conquistado, ou o mundo "
							 + "vivo mais perto de casa), ninguem pousa la vindo de orbita e o povoamento "
							 + "nao repovoa. Se isto nao foi de proposito, use o verb de admin "
							 + "'Restore Planet' em orbita dele.");
		}
		catch (Exception e) { GD.PushWarning($"[server] planetas-mortos.json ilegivel: {e.Message}"); }
	}

	/// <summary>
	/// ============================ O RESCALDO NAO RESSUSCITA NO BOOT ============================
	/// O `Faltam` de um mundo destruido virou o relogio do rescaldo (ver <see cref="RelogioDoRescaldo"/>):
	/// ele desce pra baixo de zero pelos 60 s da janela dos destrocos e para. **Mundo que volta do
	/// disco tem a janela FECHADA**, e nao aberta.
	///
	/// Sem isto, todo boot de servidor reacenderia a explosao e o campo de cacos de **todos os mundos
	/// que ja morreram na vida daquele save** -- o <see cref="ConsumarDestruicao"/> grava `Faltam = 0`,
	/// e zero e o instante da morte. Um `planetas-mortos.json` com as quatro sagas consumadas viraria
	/// quatro explosoes no ceu a cada vez que o dono sobe o servidor, por mundos que sumiram ha meses.
	///
	/// **METODO E NAO LINHA SOLTA** porque a prova precisa chamar a regra sem passar por arquivo: a
	/// bancada roda dentro do <see cref="PalcoDeMortes"/>, onde toda gravacao e barrada de proposito, e
	/// uma regra que so pode ser exercitada lendo disco e uma regra que nao vai ser exercitada.
	/// ======================================================================================
	/// </summary>
	private static void FecharAJanelaDoRescaldo(EstadoDaMorte e)
	{
		if (MortePlanetaria.EstaMorto(e.Fase))
			e.Faltam = -DestrocosDeMundo.SegundosDaJanela;
	}

	/// <summary>
	/// GRAVA O LIVRO INTEIRO, numa escrita atomica (`.tmp` + `File.Move`). Mesmo desenho do
	/// `SalvarSagas` -- e a mesma razao: uma escrita cortada no meio custaria o estado do mundo.
	///
	/// E grava **tudo de uma vez** de proposito. Gravar campo a campo, ou so "o que mudou", e
	/// exatamente como o original se meteu na armadilha de metade do estado sobreviver ao boot.
	/// </summary>
	private void SalvarPlanetasMortos()
	{
		// O PALCO DE BANCADA (ver <see cref="PalcoDeMortes"/>): enquanto uma medicao esta em curso, a
		// morte de planeta acontece so na MEMORIA e e desfeita no fim. Esta e a linha que impede que
		// ela chegue ao mundo do dono -- e ela vem ANTES do `_store`, porque o que se recusa aqui e a
		// gravacao inteira e nao o caminho dela.
		if (_mortesDeBancada)
		{
			// CONTADO, E NAO SO RECUSADO. Um palco que nunca barra nada e indistinguivel de palco
			// nenhum -- e a bancada precisa poder AFIRMAR que a arma disparou dentro dele. Esta e a
			// linha exata que teria escrito no `planetas-mortos.json` do dono.
			_gravacoesBarradasPeloPalco++;
			return;
		}

		if (_store == null) return;
		try
		{
			string tmp = CaminhoDosMortos + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(
				_mortos.Todos.ToList(),
				new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDosMortos, overwrite: true);
		}
		catch (Exception e)
		{
			GD.PushWarning($"[server] nao deu pra salvar planetas-mortos.json: {e.Message}");
		}
	}

	// =====================================================================
	// O PALCO DE BANCADA -- o mundo do dono nao paga o preco da medicao
	// =====================================================================
	/// <summary>
	/// LIGADO, A MORTE DE PLANETA ACONTECE SO NA MEMORIA. Ver <see cref="PalcoDeMortes"/>.
	/// </summary>
	private bool _mortesDeBancada;

	/// <summary>
	/// Quantas gravacoes do livro dos mortos o palco ja barrou. Ver <see cref="SalvarPlanetasMortos"/>.
	/// </summary>
	private int _gravacoesBarradasPeloPalco;

	/// <summary>
	/// ============================ O QUE ISTO CUSTOU PRA EXISTIR ============================
	/// A `--escudoteste` monta o palco dela na TERRA de verdade (ela precisa de colisao carregada pro
	/// tiro que voa) e uma das dezesseis fontes de dano dela e uma **Final Explosion de raio maximo
	/// com BP de 1e12**. O gancho de producao (`GameServer.Tecnicas.G3.cs:644`, o `misc.dm:324-335`)
	/// fez exatamente o que ele deve fazer -- raio 25 + BP acima de dez milhoes leva o planeta -- e o
	/// `SalvarPlanetasMortos` gravou a Terra como destruida **na pasta de saves do dono**, porque
	/// bancada nao tem pasta propria.
	///
	/// A consequencia nao apareceu na destruicao, e sim no BERCO, dias depois: `DestinoDoBerco` recusa
	/// por um corpo pra nascer num cadaver (linha 83, comportamento CERTO e escrito pensando na saga
	/// que destroi Vegeta), o recuo desce a lista de pre-feitos e a **segunda entrada dela e Namek**
	/// (`Core/World/Espaco.cs:116`). Resultado medido: **13 das 24 racas do jogo nascendo e renascendo
	/// em Namek**, os 42 cidadaos humanos da Terra deixando de nascer, e ninguem conseguindo pousar na
	/// Terra vindo de orbita. Nao havia uma linha errada no berco.
	///
	/// ============================ POR QUE UM ESCOPO, E NAO CUIDADO ============================
	/// A propria `--escudoteste` **ja tinha o cuidado** na outra fonte que mata planeta: o
	/// `DispararDestruicaoDoPlaneta` monta um `EstadoDaMorte` local com o comentario *"o estado e
	/// proprio da bancada e nao entra no registro"*. Ou seja, o autor conhecia o risco e protegeu um
	/// caminho -- e o outro escapou, porque a protecao dependia de LEMBRAR. Tres outras bancadas
	/// (`--planetateste`, `--sagateste`, `--wipeteste`) fotografam o registro a mao pelo mesmo motivo.
	///
	/// Aqui a protecao e MECANICA e vale pra qualquer fonte, inclusive as que ainda nao existem: quem
	/// abre o palco nao precisa saber por quais funis o dano dele passa. E o escopo devolve o mundo
	/// inteiro -- registro, tremores, cargas de `Planet_Destroy` e o ceu de destruicao dos planetas
	/// que morreram dentro dele.
	/// ==================================================================================
	/// </summary>
	private PalcoDeMortes PalcoDeMortesDeBancada() => new(this);

	/// <summary>
	/// O ESCOPO: `using (PalcoDeMortesDeBancada()) { ... }`. Ver <see cref="PalcoDeMortesDeBancada"/>.
	///
	/// **Ele e MEDIVEL de proposito** (<see cref="MatouAqui"/>): uma bancada que se protege sem provar
	/// que a arma disparou fica verde para sempre no dia em que a fonte parar de matar planeta --
	/// "um crivo que nunca corta e indistinguivel de crivo nenhum".
	/// </summary>
	internal sealed class PalcoDeMortes : IDisposable
	{
		private readonly GameServer _s;
		private readonly List<EstadoDaMorte> _foto;
		private readonly Dictionary<string, double> _tremores;
		private readonly Dictionary<int, (double Faltam, ZoneKey Zona, double Bp)> _cargas;

		/// <summary>
		/// AS FERIDAS DE MUNDO TAMBEM ENTRAM NO PALCO. Elas nao vao pro disco, entao a pasta do dono
		/// nunca correu risco por elas -- mas o MUNDO EM MEMORIA corria: uma bancada que abrisse a
		/// Terra ate 90% e fosse embora deixaria o servidor rodando com um planeta a dois tiros de
		/// morrer, e o proximo jogador que passasse por perto acabaria com ele sem entender.
		///
		/// Copia de VALOR pela mesma razao do registro logo acima: <see cref="FeridaDeMundo"/> e
		/// classe mutavel, e o `FerirOMundo` escreve por cima do objeto que ja existe.
		/// </summary>
		private readonly Dictionary<string, FeridaDeMundo> _feridas;

		private readonly bool _antes;
		private readonly int _barradasAoAbrir;
		private bool _fechado;

		/// <summary>
		/// Quantos planetas MORRERAM dentro do palco -- contado no `Dispose`, comparando o registro
		/// com a foto.
		///
		/// **Ele e zero quando a bancada limpa o registro sozinha entre familias**, e isso nao quer
		/// dizer que ela nao matou nada: e por isso que existe o <see cref="EscritasBarradas"/>, que
		/// conta no instante do fato em vez de no fim.
		/// </summary>
		public int MatouAqui { get; private set; }

		/// <summary>
		/// Quantas gravacoes do `planetas-mortos.json` este palco barrou -- ou seja, quantas vezes o
		/// mundo do dono teria sido reescrito se ele nao existisse. **E a medida honesta de "o palco
		/// cobriu alguma coisa"**, porque ela e contada quando a linha de producao roda e nao depende
		/// do que sobrou no registro no fim.
		/// </summary>
		public int EscritasBarradas { get; private set; }

		/// <summary>Os nomes deles, pro detalhe da linha da bancada.</summary>
		public string NomesQueMorreram { get; private set; } = "";

		internal PalcoDeMortes(GameServer s)
		{
			_s = s;
			_antes = s._mortesDeBancada;
			_barradasAoAbrir = s._gravacoesBarradasPeloPalco;

			// CÓPIA DE VALOR, e nao de referencia: `EstadoDaMorte` e classe MUTAVEL e o
			// `ComecarDestruicao` escreve por cima do registro que ja existe quando o planeta ja estava
			// morrendo (`e ??= new`). Guardar a referencia devolveria o objeto que a bancada estragou.
			_foto = [.. s._mortos.Todos.Select(Copiar)];
			_tremores = new Dictionary<string, double>(s._proximoTremor);
			_cargas = new Dictionary<int, (double, ZoneKey, double)>(s._cargaDoPlanetDestroy);
			_feridas = s._feridasDeMundo.ToDictionary(
				kv => kv.Key,
				kv => new FeridaDeMundo
				{
					Chave = kv.Value.Chave, Nome = kv.Value.Nome,
					Dano = kv.Value.Dano, DesdeOAviso = kv.Value.DesdeOAviso,
					SemLevarTiro = kv.Value.SemLevarTiro,
				});

			s._mortesDeBancada = true;
		}

		public void Dispose()
		{
			if (_fechado) return;
			_fechado = true;

			// QUEM MORREU AQUI DENTRO: o que esta no registro agora e nao estava na foto. E por eles que
			// o ceu de destruicao (`ForcarClima`) precisa ser desfeito -- ele nao esta no registro, mora
			// no clima, e sem esta volta a Terra ficaria com ceu de fim de mundo depois da bancada.
			var mortasAqui = _s._mortos.Todos
				.Where(e => !_foto.Any(f => f.Chave == e.Chave))
				.Select(e => (e.Nome, Zona: e.Zona()))
				.ToList();

			MatouAqui = mortasAqui.Count;
			EscritasBarradas = _s._gravacoesBarradasPeloPalco - _barradasAoAbrir;
			NomesQueMorreram = string.Join(", ", mortasAqui.Select(m => m.Nome));

			_s._mortos.Substituir(_foto);
			_s._proximoTremor.Clear();
			foreach ((string k, double v) in _tremores) _s._proximoTremor[k] = v;
			_s._cargaDoPlanetDestroy.Clear();
			foreach ((int k, (double, ZoneKey, double) v) in _cargas) _s._cargaDoPlanetDestroy[k] = v;
			_s._feridasDeMundo.Clear();
			foreach ((string k, FeridaDeMundo v) in _feridas) _s._feridasDeMundo[k] = v;

			_s._mortesDeBancada = _antes;

			foreach ((string nome, ZoneKey zona) in mortasAqui)
			{
				_s.ForcarClima(zona, TipoDeClima.Limpo, 0);
				GD.Print($"[bancada] '{nome}' morreu DENTRO de uma bancada: o planeta volta a existir e "
					   + "o `planetas-mortos.json` do dono nunca foi tocado.");
			}

			_s.MandarMortosPraTodos();
		}

		private static EstadoDaMorte Copiar(EstadoDaMorte e) => new()
		{
			Chave = e.Chave, Nome = e.Nome, Fase = e.Fase, Estagio = e.Estagio,
			Faltam = e.Faltam, BpDoAlgoz = e.BpDoAlgoz, IdDoAlgoz = e.IdDoAlgoz, Motivo = e.Motivo,
		};
	}

	// =====================================================================
	// AS PERGUNTAS QUE O RESTO DO JOGO FAZ
	// =====================================================================
	/// <summary>Esta zona e a superficie de um planeta DESTRUIDO? (`testPlanetbump`, `Planets.dm:178`).</summary>
	public bool ZonaMorta(ZoneKey z) => _mortos.Morto(z);

	/// <summary>Este corpo do mapa do universo esta destruido? (`if(D.pobj && D.pobj.isDestroyed)`).</summary>
	public bool PlanetaMorto(PlanetaNoEspaco p) => _mortos.Morto(p);

	/// <summary>Ha morte em curso ou consumada aqui -- o `planet_dying` do DM.</summary>
	public bool ZonaCondenada(ZoneKey z) => _mortos.Condenado(z);

	/// <summary>O estado de morte de uma zona (nulo = viva). Publico pra bancada e pro painel.</summary>
	public EstadoDaMorte? MorteDaZona(ZoneKey z) => _mortos.De(z);

	/// <summary>Quantos planetas ja morreram de vez.</summary>
	public int PlanetasDestruidos => _mortos.Todos.Count(e => MortePlanetaria.EstaMorto(e.Fase));

	// =====================================================================
	// O TAMANHO E O PESO DE UM MUNDO -- a entrada da formula unica
	// =====================================================================
	/// <summary>
	/// ============================ AS DUAS MEDIDAS DE UM PLANETA, NUM LUGAR SO ============================
	/// O LADO (em tiles) e a GRAVIDADE, que sao os dois argumentos da <see cref="MortePlanetaria.Furia"/>
	/// -- a formula que e ao mesmo tempo o dano da explosao e a vida do planeta sob fogo de ki.
	///
	/// **Ha duas populacoes de planeta e elas guardam as medidas em lugares diferentes**, e e por isso
	/// que isto e um metodo e nao duas linhas espalhadas:
	///   * PRE-FEITO -- o lado esta no `manifest.json` (`ZoneEntry.W`, ja lido no boot) e a gravidade
	///     no `planetas.json` (`CatalogoDePlanetas`, com os apelidos que separam "Icer" de
	///     "Icer Planet"). Os dois sao dado extraido do DM, e nao numero escrito aqui.
	///   * GERADO -- os dois sao **funcao pura da seed** (<see cref="MundoProcedural.DaSeed"/>), e por
	///     isso a resposta existe mesmo pra um mundo que NAO esta carregado no servidor. Isso importa
	///     de verdade: a vida de um planeta pode ser consultada por quem esta em orbita atirando nele,
	///     e ninguem carregou aquele mapa.
	///
	/// Sem manifesto, o lado cai em <see cref="MortePlanetaria.LadoDeReferencia"/> -- que e o tamanho
	/// medido de TODOS os 26 mapas principais, ou seja o palpite menos errado possivel, e nao um zero
	/// que zeraria a furia calada.
	/// ==================================================================================================
	/// </summary>
	public (int Lado, double Gravidade) MedidasDoPlaneta(ZoneKey zona)
	{
		if (EhZonaProcedural(zona) && !Espaco.EhEspaco(zona))
		{
			MundoProcedural m = MundoProcedural.DaSeed(zona.Seed, zona.Name);
			return (m.Lado, m.Gravidade);
		}

		int lado = _catalogo?.Get(zona) is { W: > 0 } e ? e.W : (int)MortePlanetaria.LadoDeReferencia;
		double g = _planetas?.De(zona.Name).Gravidade ?? 1;
		return (lado, g);
	}

	/// <summary>
	/// A FURIA DESTE MUNDO -- o unico numero, pelos dois consumidores.
	///
	/// Publico porque o segundo consumidor (o ataque de ki vindo do espaco, que gasta este mesmo
	/// numero como VIDA) pergunta daqui, e nao de uma copia da conta. Ver
	/// <see cref="MortePlanetaria.Furia"/>, onde o pedido do dono esta transcrito.
	/// </summary>
	public double FuriaDoPlaneta(ZoneKey zona)
	{
		(int lado, double g) = MedidasDoPlaneta(zona);
		return MortePlanetaria.Furia(lado, g);
	}

	// =====================================================================
	// (K) DERRUBAR UM MUNDO COM KI, DO ESPACO -- **invencao do dono, nao ha nada disto no DM**
	// =====================================================================
	/// <summary>
	/// ============================ O PEDIDO, E O QUE O ORIGINAL TEM A OFERECER (NADA) ============================
	/// *"pessoas q estao no espaco poderiam jogar ataques de KI no planeta pra comecar a causar dano
	/// nele (as pessoas q estao no planeta iriam receber um aviso q alguem ta jogando um ataque de ki
	/// no espaco) ... ao zerar a vida do planeta, ia comecar a contagem dos 5 minutos igual e com
	/// planet destroy"*.
	///
	/// O `obj/Planets` do DM (`Planets.dm:30-52`) tem `isDestroyed`, `isBeingDestroyed` e
	/// `destroyAble` -- **tres booleanos e zero vida**. Ele e `density=1` e nao tem `Bump`, nao tem
	/// handler de tiro, e nenhum proc do jogo inteiro causa dano a um planeta. Isto aqui e desenho
	/// novo do comeco ao fim; o que NAO e novo sao as pecas, e essa foi a regra de construcao:
	///
	///   * a VIDA e a <see cref="MortePlanetaria.Furia"/> -- a mesma funcao que da o dano da
	///     explosao, porque o dono amarrou os dois (*"dano de quando ele explode = vida do planeta"*);
	///   * o PORTAO e o <see cref="MortePlanetaria.BpExigido"/> -- o mesmo do verb Planet Destroy;
	///   * o DANO e a cadeia de ki de sempre (<see cref="DanoDeKi.BrutoContra"/>) pelo gap de poder
	///     de sempre (`BpModulus`);
	///   * a MORTE e o <see cref="ComecarDestruicao"/> -- a mesma porta, e nao uma copia dela.
	///
	/// ============================ E ELE NAO PEDE O BIT DE VILAO. RELATADO ============================
	/// O verb Planet Destroy exige `isVillain` (`Planets.dm:323`); esta rota **nao**. A razao e a
	/// letra do pedido -- *"pessoas q estao no espaco poderiam jogar ataques de KI no planeta"*, sem
	/// uma palavra sobre vilania --, e o precedente ja existe e nao e bonito: a Final Explosion
	/// (`GameServer.Tecnicas.G3`) tambem mata planeta sem o bit, e no DM aquela mesma linha
	/// (`misc.dm:332`) conferia dois portoes que **nao foram portados** (`destroyAble` por planeta e
	/// o interruptor global `canplanetdestroy` do admin, `SettingsDatum.dm:63`).
	///
	/// Ou seja: esta e a TERCEIRA porta pra matar um mundo e a segunda sem crivo social. Ela e mais
	/// cara que as outras duas (exige fogo sustentado, e publica desde o primeiro tiro e o alvo pode
	/// revidar), mas o dono precisa saber que ela existe assim -- e que o interruptor de admin que o
	/// original tem continua faltando nas tres.
	/// ==========================================================================================
	/// </summary>
	/// <param name="corpo">O disco que o tiro encostou -- ver `Espaco.PlanetaSob`, o mesmo teste do pouso.</param>
	/// <param name="p">O tiro. Dele saem o `Bp` (fixado no disparo) e o dano cru da cadeia de ki.</param>
	/// <param name="atirador">Quem atirou, ou nulo se ele ja saiu do mundo entre o disparo e o impacto.</param>
	/// <returns>Sempre verdadeiro: o tiro acaba no mundo, tenha ele ferido ou nao.</returns>
	public bool AtingirMundoComKi(PlanetaNoEspaco corpo, Projetil p, ServerPlayer? atirador)
	{
		ZoneKey zona = Espaco.ZonaDe(corpo);

		// ============================ NUM MUNDO QUE JA ESTA MORRENDO NAO HA MAIS VIDA PRA TIRAR ============================
		// O planeta ja foi condenado por outro caminho -- o verb, uma saga, o fim do pavio lento, ou o
		// tiro de outra pessoa trinta segundos atras. A vida DEIXOU DE EXISTIR no instante da
		// condenacao (a ferida some, ver `FerirOMundo`), entao nao ha o que somar: o tiro estoura no
		// disco e nada acontece.
		//
		// **E ele nao reinicia nem acelera nada**: `ComecarDestruicao` ja recusa quem esta explodindo,
		// mas confiar nisso seria deixar a regra viva num `if` de outro arquivo.
		// ==============================================================================================================
		if (Agonia.MundoJaCondenado?.Invoke(zona) ?? ZonaCondenada(zona))
		{
			if (atirador != null && PossoFalarDoMundo(atirador))
				Avisar(atirador, $"{corpo.Nome} já está se despedaçando. Não há mais o que quebrar.");
			return true;
		}

		(_, double gravidade) = MedidasDoPlaneta(zona);

		// ============================ K5: O AVISO NAO DIZ UM NUMERO. NENHUM. ============================
		// *"pessoas fracas receberiam um aviso q o ataque dela n fez nada ao planeta (nao e pra dizer o
		// bp minimo ou outra coisa, so dizer q n e forte o suficiente)"*.
		//
		// A frase abaixo nao tem o limiar, nao tem o BP do atirador, nao tem a razao entre os dois e
		// nao tem quanto falta. E o `ForteOBastantePraFerirOMundo` devolve **bool** justamente pra que
		// nao haja um `double` por perto pra alguem interpolar num texto meses depois.
		// ==========================================================================================
		if (!Agonia.ForteOBastante(p.Bp, gravidade))
		{
			if (atirador != null && PossoFalarDoMundo(atirador))
				Avisar(atirador, Agonia.TextoDoFraco(corpo.Nome));
			return true;
		}

		// ============================ O DANO CRU: A CADEIA DE KI, SEM UM DIVISOR ============================
		// `defesa: 0` = um mundo nao tem `Ekidef`, nao tem tecnica e nao tem pericia -- e a mesma
		// guarda que o `BrutoContra` ja aplicava a um corpo sem defesa. E ele passa pelo `ModsAgora()`
		// e nao pelo `ModsBase`: um tiro que enfraquece com a distancia (o `rangemod`) chega FRACO num
		// planeta que estava longe, igualzinho ao que ele faz num corpo.
		// =============================================================================================
		double bruto = DanoDeKi.BrutoContra(p.ModsAgora(), p.BaseDano, p.MaxDano, defesa: 0, p.Fisico);
		double dano = Agonia.DanoNoMundo(bruto, gravidade, p.Bp);
		if (dano <= 0) return true;

		FerirOMundo(zona, corpo.Nome, dano, p.Bp, atirador,
					$"ataque de ki de {atirador?.Name ?? "alguém"}");

		if (atirador != null && PossoFalarDoMundo(atirador))
			Avisar(atirador, $"sua energia rasga a crosta de {corpo.Nome}.");

		return true;
	}

	/// <summary>
	/// ============================ SOMA A FERIDA, AVISA O CHAO, E ENTREGA PELA PORTA UNICA ============================
	/// O funil de dano em planeta. Separado do <see cref="AtingirMundoComKi"/> porque aquele e sobre
	/// o TIRO (geometria, cadeia de ki, portao) e este e sobre o MUNDO -- e porque a bancada precisa
	/// entrar por aqui pra medir a ferida sem inventar um projetil.
	///
	/// ============================ K6: A MESMA PORTA, E NAO UMA COPIA DELA ============================
    /// *"ao zerar a vida do planeta, ia comecar a contagem dos 5 minutos igual e com planet destroy"*.
	/// Literalmente a mesma chamada que a carga de 30 s faz: <see cref="ComecarDestruicao"/>. Todo o
	/// resto -- os 310 s, a rampa de agonia, o ceu de destruicao, a crosta de magma vista do espaco, o
	/// commit e a evacuacao -- vem de graca e **nao pode divergir**, porque nao ha um segundo caminho.
	///
	/// ============================ DOIS ATACANTES AO MESMO TEMPO ============================
	/// A ferida e do PLANETA e nao de quem atira: dois (ou vinte) atacantes somam no mesmo numero, o
	/// que e o desenho certo pra um cerco. Quem der o ultimo tiro vira o algoz -- e o `BpDoAlgoz`
	/// fixado no commit e o DAQUELE tiro, nao o do mais forte do grupo.
	///
	/// **Isso e uma escolha e ela tem consequencia**: num cerco de um forte e um fraco, se o tiro que
	/// zera for o do fraco, o BP fixado e o dele. Hoje o `BpDoAlgoz` decide bem menos do que decidia
	/// (a explosao passou a ferir pela regua do PLANETA, ver `MortePlanetaria.DanoNoCorpo`) -- ele so
	/// aparece no log e no anuncio do admin --, e por isso "quem deu o ultimo tiro" e a resposta
	/// simples e honesta em vez de guardar um maximo que ninguem mais consome.
	/// ==========================================================================================================
	/// </summary>
	/// <returns>Verdadeiro quando ESTE dano condenou o mundo.</returns>
	public bool FerirOMundo(ZoneKey zona, string nome, double dano, double bpDoAlgoz,
							ServerPlayer? algoz, string motivo)
	{
		if (dano <= 0) return false;
		if (ChaveDePlaneta.Da(zona) is not { } chave) return false;

		// A MESMA PERGUNTA DO `AtingirMundoComKi`, E PELA MESMA SONDA. Ela aparece duas vezes porque
		// ha dois caminhos ate aqui (o tiro e a bancada), e nao porque sao duas regras -- por isso as
		// duas leem o mesmo `MundoJaCondenado`. Ate a bancada da fase 3-B elas eram duas leituras
		// independentes, e o efeito colateral era mudo: **desligar uma nao mudava nada**, e a prova
		// que existia pra pegar exatamente esse defeito ficava verde com ele dentro.
		if (Agonia.MundoJaCondenado?.Invoke(zona) ?? ZonaCondenada(zona)) return false;

		if (!_feridasDeMundo.TryGetValue(chave.Texto, out FeridaDeMundo? f))
		{
			_feridasDeMundo[chave.Texto] = f = new FeridaDeMundo { Chave = chave.Texto, Nome = nome };

			// ============================ O CERCO COMECOU -- e o operador precisa saber ============================
			// So no PRIMEIRO tiro (a entrada acabou de nascer), e so no console. Um cerco pode durar
			// minutos e terminar com um mundo a menos; sem esta linha, a unica pista no log seria a
			// destruicao consumada, ja tarde demais pra um admin intervir.
			//
			// **Console e nao chat**: quem esta no chao ja e avisado (K2), e quem esta no espaco nao
			// tem por que saber. O `AnunciarNoMundo` fica pro momento em que o planeta de fato cai.
			// ==================================================================================================
			GD.Print($"[server] '{nome}' comecou a levar fogo de ki do espaco ({motivo}) -- "
				   + $"vida {FuriaDoPlaneta(zona):N0}");
		}

		f.Dano += dano;
		f.SemLevarTiro = 0;   // sob fogo o mundo nao se refaz -- ver `MortePlanetaria.Cicatrizar`

		// ============================ K2: O CHAO SABE QUE ESTA SENDO BOMBARDEADO -- E NAO SABE POR QUEM ============================
		// *"as pessoas q estao no planeta iriam receber um aviso q alguem ta jogando um ataque de ki no
		// espaco"*. O dono nao disse se o aviso nomeia o atacante, e a escolha aqui e a CONSERVADORA:
		// **nao nomeia**.
		//
		// Tres razoes, e a primeira e de mundo e nao de gosto:
		//   1. de dentro de um planeta ninguem enxerga a orbita. O jogo nao tem linha de visao entre a
		//      superficie e o espaco, e um nome caindo do ceu seria informacao que ninguem podia ter;
		//   2. nomear entregaria de graca uma posicao que o atacante nao escolheu revelar -- e ele esta
		//      parado, longe e sozinho, que e o pior lugar do jogo pra se estar marcado;
		//   3. e o aviso e sobre o PLANETA, nao sobre uma briga. Quem quiser saber quem foi tem o
		//      caminho que o mundo ja oferece: decolar e olhar.
		//
		// E ele **nao diz quanto falta**, pelo mesmo motivo do K5: nem numero, nem "metade", nem uma
		// escala de tres frases que fosse uma barra de vida escrita por extenso.
		// ===============================================================================================================================
		if (f.DesdeOAviso >= SegundosEntreAvisosDeBombardeio)
		{
			f.DesdeOAviso = 0;
			AnunciarNoPlaneta(zona, $"*O CÉU DE {nome.ToUpperInvariant()} SE ABRE EM FOGO: "
								  + "alguém está atacando este planeta do espaço!*");
		}

		// A VIDA E A FURIA, PELA MESMA PORTA QUE O COMMIT USA (`FuriaDoPlaneta`) -- nao ha uma
		// segunda conta aqui, e e literalmente o pedido do dono: *"dano de quando ele explode = vida
		// do planeta"*.
		double furia = FuriaDoPlaneta(zona);
		if (f.Dano < furia) return false;

		// ZERADO. A ferida deixa de existir aqui: dali pra frente quem guarda o estado deste mundo e o
		// `EstadoDaMorte`, que persiste inteiro. Duas nocoes de "este planeta esta acabando" seria
		// exatamente o defeito do original.
		_feridasDeMundo.Remove(chave.Texto);

		GD.Print($"[server] '{nome}' teve a VIDA ZERADA por fogo de ki do espaco "
			   + $"(furia {furia:N0}; {motivo})");
		AnunciarNoMundo($"{nome} não aguenta mais. O bombardeio partiu o planeta ao meio.");

		ComecarDestruicao(zona, bpDoAlgoz, motivo, algoz?.Id ?? 0);
		return true;
	}

	/// <summary>
	/// De quanto em quanto tempo o chao ouve que esta sendo bombardeado. Ver <see cref="FerirOMundo"/>.
	///
	/// Quinze segundos porque a cadencia de tiro e de ~3 por segundo: por tiro seriam quarenta e cinco
	/// linhas nesse mesmo intervalo, e o jogador pararia de ler o chat justo quando ele passou a
	/// importar.
	/// </summary>
	private const double SegundosEntreAvisosDeBombardeio = 15;

	/// <summary>
	/// A MESMA MORDACA PRO ATIRADOR, e ela existe pelo mesmo motivo do aviso do chao: ele atira tres
	/// vezes por segundo, e tres linhas por segundo nao sao retorno, sao ruido.
	///
	/// **E ela e a mesma pras tres frases** (o "nao e forte o bastante", o "ja esta se despedacando" e
	/// o "sua energia rasga a crosta"). Uma mordaca por frase deixaria as tres se revezando e o chat
	/// continuaria cheio.
	///
	/// Guarda instante ABSOLUTO e nao contador: assim nao ha o que tiquetear por jogador, e a entrada
	/// velha e podada no tique junto com as feridas.
	/// </summary>
	private readonly Dictionary<int, long> _faleiDoMundo = [];

	private bool PossoFalarDoMundo(ServerPlayer pl)
	{
		if (pl.Peer == null) return false;

		long agora = NowMs();
		if (_faleiDoMundo.TryGetValue(pl.Id, out long quando)
			&& agora - quando < (long)(SegundosEntreFalasDoMundo * 1000)) return false;

		_faleiDoMundo[pl.Id] = agora;
		return true;
	}

	/// <summary>Ver <see cref="_faleiDoMundo"/>.</summary>
	private const double SegundosEntreFalasDoMundo = 4;

	/// <summary>
	/// ============================ A FERIDA FECHA SOZINHA -- e a decisao esta no Core ============================
	/// Ver <see cref="MortePlanetaria.SegundosParaCicatrizar"/>: um mundo se refaz em vinte minutos, o
	/// mesmo relogio do pavio lento. As duas razoes (o universo e infinito, e o esforco tem que ser
	/// SUSTENTADO) estao escritas la.
	///
	/// **O laco varre so o que esta ferido**, e a entrada sai do dicionario quando o dano chega a
	/// zero: num servidor onde ninguem esta bombardeando nada isto e uma comparacao de contador, igual
	/// ao `TickDaDestruicao` que o chama.
	/// ======================================================================================================
	/// </summary>
	private void TickDasFeridasDeMundo(double dt)
	{
		if (_feridasDeMundo.Count > 0)
			foreach (string k in _feridasDeMundo.Keys.ToList())
			{
				FeridaDeMundo f = _feridasDeMundo[k];
				f.DesdeOAviso += dt;
				f.SemLevarTiro += dt;

				// A FURIA VEM DA ZONA, e nao de um numero guardado na ferida. Guardar a vida maxima
				// dentro dela seria uma copia da formula que envelheceria sozinha no dia em que o
				// `FuriaBase` mudasse -- e o planeta ferido continuaria medindo pela regua velha.
				f.Dano = MortePlanetaria.Cicatrizar(f.Dano, FuriaDoPlaneta(f.Zona()), dt, f.SemLevarTiro);
				if (f.Dano <= 0) _feridasDeMundo.Remove(k);
			}

		// A PODA DA MORDACA: quem nao ouve uma frase de mundo ha um minuto nao precisa de entrada.
		// Sem ela o dicionario cresceria um `long` por jogador que ja passou pelo servidor -- pequeno,
		// mas e exatamente o "dado orfao eterno" que este arquivo passa o tempo todo evitando.
		if (_faleiDoMundo.Count > 0)
		{
			long agora = NowMs();
			foreach (int id in _faleiDoMundo.Keys.ToList())
				if (agora - _faleiDoMundo[id] > 60_000) _faleiDoMundo.Remove(id);
		}
	}

	/// <summary>
	/// QUANTO DESTE MUNDO JA FOI ARRANCADO, em pontos de furia. Zero = intacto.
	///
	/// ============================ ELE NAO TEM CONSUMIDOR DE JOGO, E ISSO E O PONTO ============================
	/// O unico leitor e a bancada (`--planetateste`). Normalmente um numero com um leitor so seria
	/// suspeito neste repo -- e aqui a AUSENCIA dos outros e a regra: o dono proibiu dizer *"o bp
	/// minimo ou outra coisa"*, e uma barra de vida de planeta na tela de quem atira e o mesmo numero
	/// vazando por outra porta. Ver <see cref="MortePlanetaria.ForteOBastantePraFerirOMundo"/>.
	///
	/// Por isso ele nao entra no `S2C.Mortos` (que so carrega a condenacao, que ja e publica), nao
	/// vira HUD e nao aparece no chat. O que o operador ve e o log de "comecou a levar fogo" no
	/// console do servidor, que nao chega a jogador nenhum.
	/// ======================================================================================================
	/// </summary>
	public double FeridaDoMundo(ZoneKey zona) =>
		ChaveDePlaneta.Da(zona) is { } c && _feridasDeMundo.TryGetValue(c.Texto, out FeridaDeMundo? f)
			? f.Dano : 0;

	/// <summary>
	/// TEM ALGUEM ONLINE? A pergunta do "servidor vazio adia".
	///
	/// **Vivo e com teclado**: o `!p.Ficha.dead` esta aqui pela mesma razao do ultimato das sagas --
	/// um servidor em que todo mundo esta no Outro Mundo nao tem ninguem pra impedir nada.
	///
	/// E "com teclado" e o crivo unico (`Gente.EhJogador`) e nao `Peer != null`: os 148 habitantes do
	/// povoamento nao sao alguem pra impedir nada, e o boneco de quem esta meditando ja e contado pelo
	/// proprio dono.
	/// </summary>
	private bool AlguemOnline() =>
		_players.Values.Any(p => EhJogador(p) && !p.Ficha.dead);

	// =====================================================================
	// (a) A MORTE LENTA -- `Planet_Death`
	// =====================================================================
	/// <summary>
	/// ACENDE O PAVIO LENTO. Devolve falso quando nao ha o que acender.
	///
	/// No DM quem chama isto e a colheita da Tree of Might (`Plants.dm:647-661`) -- e o `Ticker` da
	/// area, que e o retomador defeituoso. Aqui a arvore ainda nao foi portada; os chamadores hoje
	/// sao o verb de admin e a bancada, e o gancho fica pronto pra ela.
	///
	/// **Nao reacende o que ja esta aceso** -- e o `if(death_proc_running) return` (`:10`), que no
	/// original existe porque o Ticker e a propagacao entre areas irmas criavam instancias duplas do
	/// proc. Aqui nao ha areas irmas (a zona e o planeta), mas a guarda vale pelo mesmo motivo: dois
	/// pavios no mesmo planeta correriam em cadencias diferentes.
	/// </summary>
	public bool ComecarMorteLenta(ZoneKey zona, double bpDoAlgoz, string motivo, int idDoAlgoz = 0)
	{
		if (ChaveDePlaneta.Da(zona) is not { } chave)
		{
			GD.PushWarning($"[server] morte lenta pedida em '{zona.Name}', que nao e um planeta");
			return false;
		}
		if (_mortos.De(chave) != null) return false;   // ja esta morrendo, ou ja morreu

		_mortos.Por(new EstadoDaMorte
		{
			Chave = chave.Texto,
			Nome = zona.Name,
			Fase = FaseDaMorte.Morrendo,
			Estagio = 0,
			Faltam = MortePlanetaria.SegundosDoEstagio[0],
			BpDoAlgoz = bpDoAlgoz,
			IdDoAlgoz = idDoAlgoz,
			Motivo = motivo,
		});
		SalvarPlanetasMortos();
		MandarMortosPraTodos();

		// O estagio 0 ja mata a primeira fatia -- `limit_life()` roda no COMECO do estagio
		// (`Area_Death.dm:29`), e nao no fim dele.
		LimitarVida(zona, 0);

		AnunciarNoPlaneta(zona, $"Algo está errado com {zona.Name}. O ar ficou pesado, "
							  + "e os animais sumiram.");
		GD.Print($"[server] MORTE LENTA acesa em '{zona.Name}' ({motivo}) -- "
			   + $"{MortePlanetaria.PavioInteiro:0}s ate a destruicao");
		return true;
	}

	/// <summary>
	/// O `limit_life()` (`Area_Death.dm:52-55`): `prob((estagio+1)*25)` de cada NPC daqui morrer.
	///
	/// **Pela porta unica.** O DM chama `M.mobDeath()` direto; aqui a morte de um corpo sem dono
	/// passa pelo `CombatState.Morrer()` como a de qualquer um -- e um NPC que tenha um seguro
	/// pendurado (a Aura of Destruction, hoje o unico) sobrevive ao estagio, o que e a resposta
	/// certa e nao um caso especial.
	/// </summary>
	private void LimitarVida(ZoneKey zona, int estagio)
	{
		double chance = MortePlanetaria.ChanceDeMorrerPct(estagio);
		int mortos = 0;

		// COPIA: `Morrer` mexe no mundo, e o `RemoverNpc` da morte tira o corpo da lista da zona.
		foreach (ServerPlayer npc in ZoneList(zona.Hash).ToList())
		{
			// `if(M.isNPC)` (`:54`). O `Papel` e o `isNPC` deste port -- e ele exclui de graca o
			// clone da mente e o corpo de bancada, que tambem tem `Peer` nulo e nao moram aqui.
			if (npc.Papel == null) continue;
			if (npc.Ficha.dead || npc.Combate == null) continue;

			// O HABITANTE INTOCAVEL NAO ENTRA NO SORTEIO. Terceira morte deste arquivo que nao passa
			// por dano nenhum -- e a mais traicoeira das tres, porque e SORTEADA: um NPC transformando
			// morreria aqui em 25% a 100% dos estagios, e o mesmo teste rodado de novo daria outro
			// resultado. Ver o `protegido` do `ConcluirDestruicao`, mesma regra e mesma razao.
			if (npc.Combate.Intocavel) continue;

			if (_rng.NextDouble() * 100 >= chance) continue;

			if (!npc.Combate.Morrer()) continue;
			mortos++;
		}

		if (mortos > 0)
			GD.Print($"[server] '{zona.Name}' estagio {estagio}: {mortos} habitante(s) morreram "
				   + $"(chance {chance:0}%)");
	}

	/// <summary>
	/// O `death_turf_destroy()` (`Area_Death.dm:57-74`): **um pedaco de chao por jogador**, de tempos
	/// em tempos, perto de quem esta olhando.
	///
	/// O comentario do original explica a escolha melhor do que eu conseguiria: *"why affect land
	/// that won't affect players?"*. Aqui isso vale duas vezes, porque cada celula derrubada e um
	/// pacote **confiavel** por pessoa na zona (`MandarCelulaCaida`): "os turfs viram Stars" num mapa
	/// 500x500 seriam dezenas de milhares deles.
	///
	/// Reusa o `DerrubarCelula` da destruicao de cenario, que ja sabe abrir a colisao dos dois lados
	/// e ja tem a guarda de borda. Um segundo caminho pro chao cair divergiria do primeiro.
	/// </summary>
	/// <param name="porJogador">
	/// Quantas celulas cada jogador ve cair nesta volta. **UMA e o numero do original** (o `break` do
	/// `:74`) e continua sendo o padrao -- quem sobe daqui e so a agonia dos cinco minutos, e ela sobe
	/// com o teto de <see cref="TetoDeCelulasPorVolta"/> por cima. Ver <see cref="TremorDaExplosao"/>.
	/// </param>
	private void QuebrarChaoPertoDosJogadores(ZoneKey zona, int porJogador = 1)
	{
		// ============================ O TETO E POR ZONA, E NAO POR PESSOA ============================
		// Cada celula derrubada e um pacote confiavel **por pessoa na zona** -- ou seja o custo cresce
		// com o QUADRADO da plateia. Com uma celula por jogador e dez jogadores ja sao cem pacotes por
		// volta; deixar a rampa multiplicar isso por tres daria trezentos.
		//
		// O teto entao e em CELULAS POR VOLTA DA ZONA, que e a grandeza que de fato paga a conta. Numa
		// zona vazia ou com dois jogadores ele nunca morde; numa cheia, ele reparte o mesmo estrago
		// entre quem esta la em vez de somar.
		// =========================================================================================
		var gente = ZoneList(zona.Hash).Where(p => p.Peer != null).ToList();
		if (gente.Count == 0) return;

		int orcamento = Math.Min(porJogador * gente.Count, TetoDeCelulasPorVolta);

		for (int i = 0; i < orcamento; i++)
		{
			ServerPlayer pl = gente[i % gente.Count];

			int cx = (int)(pl.Pos.X / ZoneCollision.TileSize) + _rng.Next(-10, 11);
			int cy = (int)(pl.Pos.Y / ZoneCollision.TileSize) + _rng.Next(-10, 11);
			if (cx < 0 || cy < 0) continue;

			// O sorteio pode nao achar celula derrubavel, e tudo bem -- na proxima volta ele tenta
			// de novo.
			DerrubarCelula(zona, cx, cy);
		}
	}

	/// <summary>Teto de celulas derrubadas por volta, na ZONA. Ver <see cref="QuebrarChaoPertoDosJogadores"/>.</summary>
	private const int TetoDeCelulasPorVolta = 12;

	// =====================================================================
	// (b) A DESTRUICAO -- `DestroyPlanet`
	// =====================================================================
	/// <summary>
	/// ============================ COMECA OS ~5 MINUTOS ============================
	/// E o `area/proc/DestroyPlanet(mexpressedBP)` (`Area_Death.dm:75-147`), do `Quake()` inicial ate
	/// o `sleep(3100)`. O commit vem depois, no <see cref="ConsumarDestruicao"/>.
	///
	/// **O BP DO ALGOZ E FIXADO AQUI** -- `var/mexpressedBP = usr.expressedBP` (`Planets.dm:342`), e
	/// e ele que decide quem sobrevive ao fim. Mesma disciplina do BP pinado das sagas: entre o
	/// comeco e o commit passam cinco minutos, e sem o pino quem se transformasse no meio mudaria
	/// retroativamente quem morre.
	///
	/// **Nao ha volta a partir daqui** a nao ser pelo <see cref="AbortarMorte"/> (admin), e e isso
	/// que o `isBeingDestroyed = 1` do original quer dizer.
	/// ==============================================================
	/// </summary>
	public bool ComecarDestruicao(ZoneKey zona, double bpDoAlgoz, string motivo, int idDoAlgoz = 0)
	{
		if (ChaveDePlaneta.Da(zona) is not { } chave)
		{
			GD.PushWarning($"[server] destruicao pedida em '{zona.Name}', que nao e um planeta");
			return false;
		}

		EstadoDaMorte? e = _mortos.De(chave);
		if (e is { Fase: FaseDaMorte.Explodindo or FaseDaMorte.Destruido }) return false;

		e ??= new EstadoDaMorte { Chave = chave.Texto, Nome = zona.Name };
		e.Fase = FaseDaMorte.Explodindo;
		e.Estagio = MortePlanetaria.UltimoEstagio + 1;   // o `planet_death_stage = 4` do `:78`
		e.Faltam = Agonia.SegundosDeExplosao;
		e.BpDoAlgoz = bpDoAlgoz;
		e.IdDoAlgoz = idDoAlgoz;
		e.Motivo = motivo;
		_mortos.Por(e);
		_proximoTremor[e.Chave] = 0;

		SalvarPlanetasMortos();
		MandarMortosPraTodos();

		// O CEU VIRA "Destruction" -- `IsWeathering = 1; currentWeather = "Destruction"` (`:94-95`).
		// Passa pela porta unica do clima, que e publica e ja citava este uso pelo nome.
		//
		// A DURACAO PEDE UM POUCO A MAIS que a explosao: o clima forcado sai suave (45 s de
		// transicao, ver `Clima.De`), e um prazo exato faria o ceu comecar a clarear justamente no
		// minuto final -- o mundo acabaria com o sol voltando.
		//
		// ============================ A FORCA COMECA NO PE DA RAMPA, E NAO EM 1 ============================
		// Era `1` cravado, e com isso o ceu ficava CHAPADO no auge pelos 310 s inteiros -- o oposto do
		// *"quanto mais perto ta de explodir, mais intenso"*. Agora ela entra no piso da agonia e sobe
		// pelo <see cref="ApertarClima"/>, um degrau de 0,05 por vez.
		//
		// O PRAZO CONTINUA SENDO O TOTAL e nao o que falta: `ApertarClima` preserva `Ate` e `Duracao`
		// justamente pra a curva de entrada/saida do `Clima.De` nao reiniciar a cada degrau.
		// ==============================================================================================
		ForcarClima(zona, TipoDeClima.Destruicao, Agonia.SegundosDeExplosao + 60,
					ForcaDoCeuNaAgonia(MortePlanetaria.PisoDaAgonia), $"destruicao de {zona.Name}");

		foreach (ServerPlayer pl in ZoneList(zona.Hash))
		{
			if (pl.Peer == null) continue;
			Avisar(pl, "*O PLANETA COMEÇA A SE PARTIR AO SEU REDOR!!*");
			MandarEfeito(pl, "terremoto", 1800);
		}

		AnunciarNoMundo($"{zona.Name} está se despedaçando. Quem estiver lá tem minutos.");
		GD.Print($"[server] DESTRUICAO de '{zona.Name}' comecou ({motivo}); BP do algoz fixado em "
			   + $"{bpDoAlgoz:0}; {Agonia.SegundosDeExplosao:0}s ate o fim");
		return true;
	}

	/// <summary>
	/// ============================ O COMMIT -- `Area_Death.dm:130-147` ============================
	/// O que acontece, na ordem do original:
	///   1. quem ja esta morto ou saiu do planeta e PULADO (`:132-133`, o fix do "chovia explosao no
	///      z6": a lista de jogadores da area do DM e pegajosa);
	///   2. todo NPC daqui morre, sem chance (`M.buudead = "force"; M.Death()`, `:134-136`);
	///   3. quem tem `expressedBP <= mexpressedBP` leva **99 de dano em cada membro** (`:137-139`);
	///   4. quem estava NOCAUTEADO e nao morreu ainda **morre de verdade** (`:140-142`) -- o
	///      comentario do original diz por que: antes ele ficava "jogado no espaco, em combate
	///      eterno, sem nunca morrer";
	///   5. o planeta entra na lista e vira `isDestroyed` (`:143-145`);
	///   6. quem sobrou e jogado pro espaco (no DM isso e um tique de `Stats.dm:427-434`; aqui e
	///      feito aqui, ver <see cref="EvacuarParaOEspaco"/>).
	///
	/// **TUDO PELA PORTA UNICA.** O dano vai por `EspalharDanoG3` (o `SpreadDamage` deste port) e a
	/// morte por `CombatState.Morrer()`, que e onde o seguro da Aura of Destruction se pendura. Um
	/// caminho proprio aqui faria a unica morte do jogo que ignora o seguro ser justamente a maior.
	/// ======================================================================================
	/// </summary>
	private void ConsumarDestruicao(EstadoDaMorte e, ZoneKey zona)
	{
		e.Fase = FaseDaMorte.Destruido;
		e.Faltam = 0;
		_proximoTremor.Remove(e.Chave);

		// O ALGOZ RESOLVIDO -- ou `null`. O id e uma PISTA e nao identidade: cinco minutos depois ele
		// pode ter deslogado, morrido ou (no caso da saga) sido removido do mundo. Sem algoz o dano
		// entra como dano SEM autor, que e o mesmo caminho que a Final Explosion ja usa pra ferir
		// quem a soltou -- ninguem vira heroi nem leva Zenkai por um planeta que explodiu.
		ServerPlayer? algoz = e.IdDoAlgoz != 0 && _players.TryGetValue(e.IdDoAlgoz, out ServerPlayer? a)
							  && !a.Ficha.dead ? a : null;

		// ============================ A FURIA DO MUNDO, MEDIDA UMA VEZ ============================
		// Fora do laco de proposito: ela e do PLANETA e nao de quem esta nele, entao calcula-la por
		// corpo seria a mesma conta N vezes -- e, pior, abriria a porta pra alguem um dia enfiar um
		// numero do corpo aqui dentro e transformar a furia do mundo numa propriedade da vitima.
		// ======================================================================================
		(int lado, double gravidade) = MedidasDoPlaneta(zona);

		// A FURIA VEM DA PORTA UNICA (`FuriaDoPlaneta` -> `MortePlanetaria.Furia`), a MESMA chamada
		// que a vida do mundo sob fogo de ki usa. A sonda existe pra a bancada poder desamarrar as
		// duas de proposito e exigir que a prova de igualdade caia -- ver `SondasDaAgonia`.
		double furia = Agonia.FuriaDaExplosao?.Invoke(zona) ?? FuriaDoPlaneta(zona);

		int npcsMortos = 0, feridos = 0, mortos = 0, evacuados = 0;

		foreach (ServerPlayer pl in ZoneList(zona.Hash).ToList())
		{
			if (pl.Ficha.dead) continue;                      // `:132` -- ja esta no Outro Mundo
			if (!pl.Zone.Equals(zona)) continue;              // `:133` -- saiu antes do fim
			if (pl.Combate == null) continue;

			// ============================ O CORPO INTOCAVEL ATRAVESSA O FIM DO MUNDO ============================
			// Aqui ha DUAS mortes que nao passam por dano nenhum -- o habitante que morre direto
			// (`:134-136` do DM, sem checagem de BP) e o nocauteado que morre depois do estrago -- e por
			// isso nem o funil do `CombatState.Ferir` nem o crivo do `EspalharDanoG3` as alcancam. Sem
			// esta bandeira, a explosao do planeta seria o UNICO jeito de matar quem esta transformando,
			// e o NPC morreria por ela sem sequer um teste de poder: o "as vezes" mais dificil de
			// reproduzir do jogo, porque exige dois eventos raros no mesmo instante.
			//
			// Vale tambem pra carencia de renascimento, que e a outra metade do `Intocavel`: renascer no
			// planeta no segundo em que ele estoura ja era uma morte que o escudo devia ter barrado.
			//
			// **BANDEIRA E NAO `continue`**: quem sobrevive ao fim do mundo ainda tem que SUBIR
			// (`EvacuarParaOEspaco`, la embaixo). Pular a volta inteira deixaria o transformado de pe num
			// planeta que nao existe mais -- trocar uma morte injusta por um corpo preso no nada.
			// ==============================================================================================
			bool protegido = pl.Combate.Intocavel;

			// ============================ 1-bis. A MEGA EXPLOSAO, VISTA DE DENTRO ============================
			// O `spawnExplosion(P.loc, mexpressedBP, 20)` do fim do original (`Area_Death.dm:147`). Sai
			// ANTES do dano e ANTES da evacuacao de proposito: quem morre aqui tem que ver o clarao que o
			// matou, e quem sobe tem que ve-lo do chao e nao ja do vacuo.
			//
			// **O canal existia e nao tinha ouvinte.** `MandarEfeito(pl, "explosao_final", ...)` era
			// mandado de quatro lugares (aqui do lado, no tremor; as duas Auras da Destruicao; a Final
			// Explosion) e o `switch` do `World.AoCairEfeito` nao tinha o `case` -- todos caiam no chao
			// caladamente, inclusive a Final Explosion do jogo inteiro. O desenho entrou junto com esta
			// linha, e conserta os outros tres de brinde.
			//
			// QUEM ESTA NO ESPACO NAO RECEBE PACOTE NENHUM e nao precisa: ele ja tem o prazo da agonia
			// no `S2C.Mortos` e desenha a explosao do planeta como funcao pura dele -- mesma disciplina
			// do ceu, da lua e do terreno. Ver `Client/CeuDoEspaco.PlanetaDesenhado`.
			// ============================================================================================
			if (pl.Peer != null) MandarEfeito(pl, "explosao_final", 2200);

			// ============================ 2. O HABITANTE NAO SOBREVIVE ============================
			// `if(M.isNPC) { M.buudead = "force"; M.Death() }` (`:134-136`) -- **sem checagem de BP**.
			// O cidadao de Vegeta com BP de milhoes morre igual ao de BP 3.
			//
			// O crivo e o `Papel` e nao o `Peer`, e a diferenca importa: `Peer == null` tambem pega o
			// clone da mente e os corpos de bancada, que nao sao habitantes de lugar nenhum. `Papel`
			// e o `isNPC` deste port.
			// ================================================================================
			if (pl.Papel != null)
			{
				if (!protegido && pl.Combate.Morrer()) npcsMortos++;
				continue;
			}

			// ============================ 3. O DANO E DO PLANETA, E NAO DO ALGOZ ============================
			// Era `if(M.expressedBP <= mexpressedBP) SpreadDamage(99)` (`Area_Death.dm:137-138`): 99 fixo
			// em cada membro, com o crivo no BP de quem apertou o botao. **As duas metades mudaram**, e a
			// razao esta escrita por inteiro em `MortePlanetaria.DanoNoCorpo`:
			//   * o VALOR agora sai da <see cref="MortePlanetaria.Furia"/> -- tamanho x gravidade, que e
			//     o pedido do dono e o MESMO numero que sera a vida do planeta sob fogo de ki;
			//   * o CRIVO deixou de ser binario. Nao ha mais "sobrevive ileso" e "morre": a furia passa
			//     pelo gap de poder do jogo (`CombatMath.BpModulus`), medida contra o
			//     `MortePlanetaria.BpExigido` deste chao -- o mesmo limiar que o verb cobra pra deixar
			//     alguem quebrar este mundo. Quem esta muito acima dele sai machucado, e nao intacto.
			//
			// DANO ZERO NAO ENTRA NO FUNIL: `EspalharDanoG3` ja recusa `dano <= 0`, mas passar por la
			// tambem poria os dois em COMBATE (o `EntrarEmCombate` das duas pontas) por um arranhao que
			// nao aconteceu. Um planeta que nao arranha ninguem nao inicia briga com ninguem.
			// ==========================================================================================
			double dano = protegido ? 0 : MortePlanetaria.DanoNoCorpo(furia, gravidade, pl.Ficha.expressedBP);
			if (dano > 0)
			{
				EspalharDanoG3(pl, algoz ?? pl, dano, letal: true);
				Avisar(pl, "a explosão do planeta rasga você inteiro.");
				feridos++;

				// 4. NOCAUTEADO MORRE. Depois do dano, e conferindo `dead` de novo: o `SpreadDamage`
				// acima ja pode ter matado, e chamar `Morrer()` duas vezes contaria duas mortes.
				if (pl.Ficha.KO && !pl.Ficha.dead && pl.Combate.Morrer())
				{
					mortos++;
					continue;
				}
			}

			if (pl.Ficha.dead) { mortos++; continue; }

			// 6. QUEM SOBROU SOBE.
			EvacuarParaOEspaco(pl, zona);
			evacuados++;
		}

		SalvarPlanetasMortos();
		MandarMortosPraTodos();

		// O CEU FORCADO SAI JUNTO: nao ha mais planeta pra ter ceu, e deixar o clima pendurado num
		// hash de zona morta seria estado orfao vivendo pra sempre em memoria.
		ForcarClima(zona, TipoDeClima.Limpo, 0);

		AnunciarNoMundo($"{zona.Name} NÃO EXISTE MAIS.");

		// ============================ E AS ESFERAS DO DRAGAO DAQUI VAO JUNTO ============================
		// **Depois** do anuncio de proposito: a ordem em que as duas linhas chegam ao chat e a ordem dos
		// fatos -- o mundo acabou, e entao se ve o que ele levou junto. E depois da evacuacao tambem,
		// porque e ela que decide quem ainda esta com uma esfera na mao pra receber o aviso.
		//
		// O QUE ISTO FAZ (e por que nao esta escrito aqui): ver `EnterrarSetsDeMundosMortos`. Este e o
		// **instante** da regra; a rede que cobre as outras portas do registro dos mortos esta no
		// `TickDasEsferas`. Duas chamadas do MESMO funil, e nenhuma copia da regra.
		// ==========================================================================================
		int setsEnterrados = EnterrarSetsDeMundosMortos();

		GD.Print($"[server] '{zona.Name}' DESTRUIDO ({e.Motivo}): {npcsMortos} habitante(s) mortos, "
			   + $"{feridos} ferido(s), {mortos} morto(s), {evacuados} evacuado(s), "
			   + $"{setsEnterrados} set(s) de esferas enterrado(s); "
			   + $"furia {furia:N0} (lado {lado}, gravidade {gravidade:0.##}); "
			   + $"BP do algoz {e.BpDoAlgoz:0}");
	}

	/// <summary>
	/// ============================ A EVACUACAO ============================
	/// No DM ela e um tique de `Stats` (`Stats.dm:427-434`): enquanto a area estiver destruida, todo
	/// mob com `client` e sorteado pra um turf em `view(1, P)` -- ou seja, **orbita**, repetindo pra
	/// sempre. E a rede que impede alguem de voltar.
	///
	/// Aqui e o caminho do DECOLAR, que ja existe inteiro e ja esta certo (`GameServer.Espaco.cs:67`).
	/// Os tres carimbos que um teleporte ingenuo esqueceria, e que sao o motivo de isto nao ser um
	/// `MoveToZone` solto:
	///   * `PlanetaDeOrigem` -- pra onde a nave "volta";
	///   * `ChunkAtual` -- sem ele o tique do espaco veria "mudou de chunk" no quadro seguinte e
	///     mandaria a vizinhanca DUPLICADA;
	///   * `MandarVizinhanca` forcado -- sem ele a pessoa chega ao espaco **sem nenhum planeta
	///     desenhado**, inclusive sem o cadaver do que ela acabou de deixar.
	///
	/// E o ponto de chegada e o `PontoDeDecolagem`, que fica FORA do raio do disco -- entao o teste
	/// de pouso nao dispara no mesmo quadro e ninguem cai de volta. O `pspace_noland_until` do DM
	/// (`NewTurfs.dm:57`) nao foi portado e, por esta rota, nao faz falta: quem impede a volta e o
	/// filtro de planeta morto no <see cref="TickDoEspaco"/>.
	/// ====================================================
	/// </summary>
	private void EvacuarParaOEspaco(ServerPlayer pl, ZoneKey zona)
	{
		PlanetaNoEspaco? corpo = null;

		// O planeta gerado sabe onde fica pelo registro de zonas vivas; o pre-feito, pela carta.
		if (EhZonaProcedural(zona) && _zonasGeradas.TryGetValue(zona.Hash, out ZonaGerada? viva))
			corpo = viva.NoEspaco;
		else
			foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
				if (string.Equals(p.Nome, zona.Name, StringComparison.OrdinalIgnoreCase)) { corpo = p; break; }

		if (corpo is not { } onde)
		{
			// SEM LUGAR NO UNIVERSO: o berco e a resposta, e nao "fica onde esta". Um corpo numa zona
			// morta ficaria preso num planeta que nao existe mais.
			GD.PushWarning($"[server] '{zona.Name}' nao esta no mapa do universo -- {pl.Name} vai pro berco");
			MandarProBerco(pl);
			return;
		}

		pl.PlanetaDeOrigem = onde.Nome;
		MoveToZone(pl.Id, ZonaDoEspaco, Espaco.PontoDeDecolagem(onde));
		pl.ChunkAtual = ChunkId.De(pl.Pos);
		MandarVizinhanca(pl);
		Avisar(pl, $"o chão some debaixo de você. {zona.Name} vira pó, e você fica no vácuo.");
	}

	/// <summary>
	/// ============================ ONDE ESTE CORPO PODE ACORDAR -- as duas zonas que nao recebem ninguem ============================
	/// Roda no `Entrar`, uma vez por login, ANTES de qualquer pacote. Duas guardas, e a segunda e a
	/// que faltava:
	///
	///  1. **A ZONA NAO EXISTE MAIS.** Um planeta gerado some quando o universo e regerado com outra
	///     seed, e uma zona pre-feita some se o mapa for reconvertido sem ela. Acordar num lugar que
	///     nao carrega e ficar preso no vazio.
	///
	///  2. **O PLANETA EXPLODIU ENQUANTO ELE ESTAVA FORA.** A guarda 1 pergunta *"o MAPA existe?"* --
	///     e um planeta destruido continua no manifesto, entao ela nunca disparava por ele. Medido de
	///     ponta a ponta com a Terra marcada como `Destruido`: o boot dizia certinho *"'Earth' esta
	///     condenado -- ninguem nasce la"*, e a linha seguinte era `Ausente entrou (id 10) em Earth`,
	///     com o mapa carregando normalmente. Quem deslogou num mundo que depois foi destruido
	///     **voltava a jogar dentro do cadaver**: nao levava o dano do commit (ele ja passou), nao era
	///     evacuado, e ficava num planeta-fantasma habitavel que nao existe pra mais ninguem.
	///
	/// **PRO BERCO E NAO PRO ESPACO**, e a escolha e consciente: aplicar o X4 tarde (largar o corpo em
	/// orbita) daria 20 segundos de folego -- o tempo do vacuo -- pra alguem que nem estava online
	/// quando o mundo acabou. Morte por sufocamento na tela de entrada. O berco ja sabe recuar de
	/// planeta morto (`DestinoDoBerco` recusa cadaver e desce a lista de pre-feitos vivos), entao isto
	/// reusa o funil inteiro em vez de inventar um destino.
	///
	/// `PousarNoBercoSemPacote` escreve zona E posicao de uma vez -- as duas tem que andar juntas,
	/// senao o corpo chega no mapa novo com a coordenada do antigo (que e como se atravessa montanha
	/// num mundo que nunca se viu). E a ordem importa: `CarregarPlanetasMortos` roda no boot, muito
	/// antes de qualquer login.
	///
	/// **METODO E NAO BLOCO INLINE** porque a bancada precisa entrar por aqui: uma checagem que
	/// escrevesse `pl.Zone` a mao e conferisse o resultado estaria medindo a si mesma. Ver a familia
	/// do ausente em `GameServer.DestruicaoTeste`.
	/// =====================================================================================================================================
	/// </summary>
	private void OndeEsteCorpoPodeAcordar(ServerPlayer pl)
	{
		if (pl.Zone.Kind == ZoneKey.KindPremade && _catalogo?.Get(pl.Zone) == null
			&& !Espaco.EhEspaco(pl.Zone))
		{
			PousarNoBercoSemPacote(pl);
			return;
		}

		if (!ZonaMorta(pl.Zone)) return;

		GD.Print($"[server] {pl.Name} deslogou em '{pl.Zone.Name}', que foi DESTRUIDO desde entao "
			   + "-- o corpo acorda no berco");
		PousarNoBercoSemPacote(pl);
	}

	/// <summary>
	/// ============================ A PORTA QUE DAVA PRA UM MUNDO QUE NAO EXISTE MAIS ============================
	/// **O caso que o pedido do dono nao cobre, e que a medicao encontrou.** Ele descreveu o que
	/// acontece com *"todos q estao no planeta"* -- e ha gente que nao esta no planeta e mesmo assim
	/// depende dele. As zonas de INTERIOR ancoradas num mundo:
	///
	///   Lookout (o Templo) ......... unica saida: Earth
	///   Hyperbolic_Time_Chamber .... sai pelo Lookout, que sai na Earth
	///   Earth_Cave (tres bocas) .... Earth
	///   Vegeta_Cave ................ Vegeta
	///   o SELO ..................... volta pra `Selo.ZonaDeVolta`, gravada quando a pessoa foi presa
	///
	/// Elas **nao sao planetas** (`Espaco.EhPlaneta` so aceita procedural e os 7 pre-feitos), entao nao
	/// morrem, nao sao varridas pelo commit e ninguem la dentro leva dano nem e evacuado. Isso esta
	/// certo -- quem esta na Sala do Tempo nao esta no planeta. O que estava errado e o CAMINHO DE
	/// VOLTA: ele e cravado no mapa e nao consultava a lista de mortos, entao quem saisse do Templo
	/// depois de a Terra explodir era depositado no cadaver, sem uma linha de aviso.
	///
	/// ============================ A ESCOLHA: O INTERIOR VIVE, A PORTA MUDA ============================
	/// A alternativa era evacuar as zonas de interior junto com o planeta, no commit. Foi recusada:
	/// a Sala do Tempo e UMA zona pro servidor inteiro (nao ha uma por planeta) e o Selo pode ter
	/// gente presa por gente de outro mundo -- "o interior e do planeta" simplesmente nao e verdade
	/// aqui. O que e verdade e que a SAIDA deixou de existir.
	///
	/// Entao a pessoa sai -- ninguem fica preso -- e sai pelo mesmo lugar que todo mundo que estava
	/// no chao: o <see cref="EvacuarParaOEspaco"/>, que larga o corpo exatamente onde o planeta
	/// ficava. E o X4 do dono aplicado tarde, pelo funil que ja existe.
	/// ============================================================================================
	/// </summary>
	/// <returns>Verdadeiro quando o destino era um cadaver e o corpo JA foi mandado pro espaco.</returns>
	private bool SaidaParaUmMundoMorto(ServerPlayer pl, ZoneKey destino)
	{
		if (!ZonaMorta(destino)) return false;

		Avisar(pl, $"você abre a passagem e não há nada do outro lado. {destino.Name} virou pó "
				 + "enquanto você estava aqui dentro.");
		EvacuarParaOEspaco(pl, destino);
		GD.Print($"[server] {pl.Name} saiu de '{pl.Zone.Name}' pra '{destino.Name}', que esta morto "
			   + "-- foi pro espaco");
		return true;
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>
	/// O TIQUE DA MORTE DE PLANETA. Roda no bloco de 1 Hz, junto do das sagas.
	///
	/// **1 Hz basta e 30 Hz seria desperdicio**: o passo mais curto deste sistema sao os 3 s entre
	/// dois tremores, e os estagios sao medidos em MINUTOS. O laco varre so o que esta no registro
	/// (planeta vivo nao aparece nele), entao num servidor sem nenhuma morte em curso ele e uma
	/// comparacao de contador.
	/// </summary>
	private void TickDaDestruicao(double dt)
	{
		TickDaCargaDeDestruicao(dt);
		TickDasFeridasDeMundo(dt);
		if (_mortos.Quantos == 0) return;

		bool mudou = false;
		foreach (EstadoDaMorte e in _mortos.Todos.ToList())
		{
			if (MortePlanetaria.EstaMorto(e.Fase)) { if (RelogioDoRescaldo(e, dt)) mudou = true; continue; }

			ZoneKey zona = e.Zona();
			switch (e.Fase)
			{
				case FaseDaMorte.Morrendo:
					// SERVIDOR VAZIO ADIA -- ver o cabecalho deste arquivo.
					if (!AlguemOnline()) continue;
					if (PassoDaMorteLenta(e, zona)) mudou = true;
					break;

				case FaseDaMorte.Explodindo:
					// A EXPLOSAO NAO ESPERA NINGUEM. Ver o cabecalho.
					TremorDaExplosao(e, zona, dt);
					e.Faltam -= dt;
					if (e.Faltam <= 0) { ConsumarDestruicao(e, zona); return; }
					break;
			}
		}

		// SALVA UMA VEZ POR TIQUE, e so quando ALGO MUDOU DE ESTAGIO. O `Faltam` desce a cada
		// segundo, e gravar por isso seria uma escrita em disco por segundo por planeta morrendo --
		// pra proteger, no pior caso, um segundo de pavio. O que nao pode se perder e o ESTAGIO.
		if (mudou) { SalvarPlanetasMortos(); MandarMortosPraTodos(); }
	}

	/// <summary>
	/// ============================ O RELOGIO DO RESCALDO -- E TODO O CUSTO DE SERVIDOR DOS DESTROCOS ============================
	/// O dono pediu o despawn dos asteroides com uma razao explicita: *"pro servidor n ter q ficar
	/// gastando tempo de tick pra ver a posicao de asteroides"*. **Isto e o servidor inteiro pagando
	/// pelos destrocos**, e sao duas comparacoes de `double` por mundo morto, uma vez por SEGUNDO --
	/// nao ha posicao de asteroide em lugar nenhum daqui, nem por um quadro. A posicao e funcao pura de
	/// `(semente, indice, tempo)` no cliente; ver `Core.World.DestrocosDeMundo`.
	///
	/// Medido nesta mesma casa, com o orcamento de um tique em 33.333 us (30 Hz): 0,025 us com zero
	/// mortos, 0,457 us com 128 e **1,686 us com 512** -- e so 1 tique em 30 paga, porque este laco
	/// mora no bloco de 1 Hz. Com o ceu inteiro em cinzas o custo por segundo continua sendo micros.
	///
	/// ============================ POR QUE `Faltam` E NAO UM CAMPO NOVO ============================
	/// O cliente precisa de UM numero: ha quantos segundos este mundo morreu. Esse numero ja viaja --
	/// `EstadoDaMorte.Faltam` esta no `S2C.Mortos` desde que a rampa da agonia foi ligada. O que faltava
	/// era o servidor **deixar de congela-lo em zero** no commit: dali pra frente ele desce pra
	/// negativo, e negativo passou a querer dizer "faz tanto tempo".
	///
	/// Um campo novo no pacote seria uma segunda fonte pro mesmo instante -- e a primeira a divergir
	/// seria a de la, calada. Ver o cabecalho do <see cref="MandarMortos"/>, que ja diz isso do `Faltam`.
	///
	/// ============================ E ELE PARA, E O FIM E GRAVADO ============================
	/// Passada a janela o valor CONGELA em `-janela` e o metodo devolve verdadeiro uma unica vez, o que
	/// pede o save (o campo fechado tem que sobreviver a um boot) e um `MandarMortosPraTodos` (quem
	/// esta olhando fica sabendo que acabou, sem ter que descobrir sozinho).
	///
	/// Sem o congelamento, o `Faltam` de um mundo morto desceria pra sempre -- e um `double` que so
	/// desce vira, no save de um servidor de anos, um numero grande o bastante pra perder precisao no
	/// lugar que importa.
	/// ========================================================================================================================
	/// </summary>
	/// <returns>Verdadeiro **uma unica vez**: no segundo em que a janela do rescaldo fecha.</returns>
	private bool RelogioDoRescaldo(EstadoDaMorte e, double dt)
	{
		double janela = Agonia.SegundosDosDestrocos;

		// A SAIDA BARATA VEM PRIMEIRO, e ela e o caso de quase todo mundo morto de um servidor antigo:
		// a janela deles fechou ha muito tempo, e o que resta e esta comparacao.
		if (e.Faltam <= -janela) return false;

		e.Faltam -= dt;
		if (e.Faltam > -janela) return false;

		e.Faltam = -janela;
		return true;
	}

	/// <summary>
	/// UM SEGUNDO DO PAVIO LENTO. Devolve verdadeiro quando o ESTAGIO mudou (o que pede save).
	///
	/// A ordem e a do `switch` do original (`Area_Death.dm:27-45`): o estagio faz o que tem que
	/// fazer, dorme o tempo dele, e so entao sobe.
	/// </summary>
	private bool PassoDaMorteLenta(EstadoDaMorte e, ZoneKey zona)
	{
		// O CHAO SE DESFAZENDO acontece DENTRO do estagio, e nao na virada dele -- e um laco proprio
		// no original (`spawn while(...)`, `:62`), com 7 a 15 s entre uma celula e outra.
		if (e.Estagio >= MortePlanetaria.EstagioQueQuebraOChao)
		{
			double proximo = _proximoTremor.GetValueOrDefault(e.Chave);
			if (proximo <= 0)
				_proximoTremor[e.Chave] = 7 + _rng.NextDouble() * 8;
			else if ((_proximoTremor[e.Chave] = proximo - 1) <= 0)
				QuebrarChaoPertoDosJogadores(zona);
		}

		if ((e.Faltam -= 1) > 0) return false;

		e.Estagio++;

		// `if(planet_death_stage==4) goto destroy` (`:46`) -- o pavio acabou, a destruicao comeca.
		if (e.Estagio > MortePlanetaria.UltimoEstagio)
		{
			ComecarDestruicao(zona, e.BpDoAlgoz, e.Motivo + " (pavio lento)", e.IdDoAlgoz);
			return false;   // o `ComecarDestruicao` ja salvou e ja avisou
		}

		e.Faltam = MortePlanetaria.SegundosDoEstagio[e.Estagio];
		LimitarVida(zona, e.Estagio);

		// ============================ A NOITE QUE NAO ACABA ============================
		// `Weather.dm:166-167`: com `planet_death_stage` entre 1 e 3, o `daylightcycle` e forcado
		// pra >= 6, ou seja, escuro pra sempre. O ceu deste port e funcao pura do tempo e nao tem
		// como ser forcado por planeta -- entao o equivalente honesto e o CLIMA, que tem gancho: um
		// ceu de destruicao permanente encobre 0,98, que apaga o dia tao bem quanto a noite do DM.
		//
		// O que se perde: a lua nao some (ela e do relogio, nao do clima), entao um Saiyajin ainda
		// pode virar Oozaru num planeta agonizando. O que se ganha: nao ha um segundo relogio de
		// dia/noite por zona, que e a coisa que a regra 0.2 proibe.
		// ===========================================================
		//
		// A FORCA SAI DA MESMA RAMPA DA EXPLOSAO (era 0,55 cravado). Isso importa por dois motivos que
		// nao sao estetica: (1) a agonia passa a ser MONOTONA -- o pavio sobe em degraus de estagio ate
		// exatamente o piso em que a explosao comeca, e o planeta nunca fica visivelmente mais calmo no
		// instante em que a conta regressiva dispara; (2) o `ForcarClima` tem a guarda do "o mais forte
		// vence", e um pavio cravado em 0,55 com prazo longo poderia RECUSAR o ceu da explosao que
		// comeca no piso 0,516. Com uma rampa so, os dois nunca se cruzam pelo lado errado.
		if (e.Estagio >= MortePlanetaria.EstagioDaNoiteEterna)
			ForcarClima(zona, TipoDeClima.Destruicao,
						MortePlanetaria.SegundosDoEstagio[e.Estagio] + 60,
						ForcaDoCeuNaAgonia(Agonia.Intensidade(e)),
						$"{zona.Name} agonizando");

		AnunciarNoPlaneta(zona, e.Estagio switch
		{
			1 => $"O sol de {zona.Name} não nasce mais. O céu ficou da cor de ferrugem.",
			2 => $"O chão de {zona.Name} começa a se abrir sozinho.",
			_ => $"Não sobrou quase ninguém em {zona.Name}. O planeta está morrendo.",
		});
		GD.Print($"[server] '{zona.Name}' entrou no estagio {e.Estagio} da morte lenta "
			   + $"({MortePlanetaria.SegundosDoEstagio[e.Estagio]:0}s)");
		return true;
	}

	/// <summary>
	/// O TREMOR E A EXPLOSAO da fase final -- `Area_Death.dm:96-116`, com o `sleep(20 + rand(10,90))`
	/// (3 a 11 s) e o `pick(1,2,3)`.
	///
	/// O `if(4)` do original e **inalcancavel** (`pick(1,2,3)` nunca sorteia 4) e por isso os efeitos
	/// dele -- poeira, relampago no chao, cratera -- nunca rodaram no jogo de verdade. Nao portei um
	/// ramo morto: o que existe aqui sao os tres que existiam.
	/// </summary>
	private void TremorDaExplosao(EstadoDaMorte e, ZoneKey zona, double dt)
	{
		// ============================ A RAMPA, LIDA UMA VEZ E SO AQUI ============================
		// `MortePlanetaria.Intensidade` e a UNICA fracao de agonia do sistema (ver la o porque). Tudo
		// o que este metodo faz -- cadencia do tremor, forca do abalo, quantas celulas de chao caem,
		// quantas crateras abrem, o quanto o ceu aperta -- sai deste `double`. Um efeito com nocao
		// propria de intensidade ficaria "certo sozinho" e errado junto dos outros.
		// ====================================================================================
		double agonia = Agonia.Intensidade(e);

		// O CEU APERTA CONTINUO, e nao de volta em volta: ele nao depende do relogio do tremor, e
		// pendura-lo ali daria um ceu que so piora quando o chao treme. Ver `ApertarClima`.
		ApertarClima(zona, ForcaDoCeuNaAgonia(agonia), $"{zona.Name} agonizando");

		double falta = _proximoTremor.GetValueOrDefault(e.Chave) - dt;
		if (falta > 0) { _proximoTremor[e.Chave] = falta; return; }

		// ============================ A CADENCIA ANDA DE 11 s PRA 3 s ============================
		// Os dois extremos sao os do DM (`sleep(20 + rand(10,90))`, `Area_Death.dm:96`): o que mudou e
		// que eles deixaram de ser as pontas de um sorteio UNIFORME e viraram as pontas da RAMPA. No
		// comeco dos cinco minutos o chao treme a cada ~10 s; no ultimo minuto, a cada ~3.
		//
		// O JITTER FICA, e ele e +-25% em volta do alvo: sem ele o tremor viraria metronomo, e o
		// ouvido pega metronomo muito antes do olho pegar qualquer outra coisa.
		// ====================================================================================
		double alvo = MortePlanetaria.TremorMax
					+ (MortePlanetaria.TremorMin - MortePlanetaria.TremorMax) * agonia;
		double intervalo = alvo * (0.75 + _rng.NextDouble() * 0.5);
		_proximoTremor[e.Chave] = intervalo;

		// ============================ QUANTO CHAO CAI, E O TETO EM CELULAS POR SEGUNDO ============================
		// O aviso da medicao de custo era literal: *"o teto tem que ser em CELULAS POR SEGUNDO, nao em
		// 'mais rapido conforme aperta'"*. Entao a conta e feita nessa unidade e so depois convertida
		// pra "quantas nesta volta" -- assim apertar a cadencia **nao** multiplica o estrago por dois
		// (mais voltas x mais celulas), que e como um efeito assim costuma estourar sem ninguem ver.
		// =====================================================================================================
		int porJogador = Math.Max(1, (int)Math.Round(TetoDeCelulasPorSegundo * agonia * intervalo));

		foreach (ServerPlayer pl in ZoneList(zona.Hash).ToList())
		{
			if (pl.Peer == null || pl.Ficha.dead) continue;   // `:98`

			// A FORCA DO ABALO TAMBEM SOBE. O cliente deriva a forca da DURACAO
			// (`World.AoCairEfeito`, `case "terremoto"`), entao mandar mais milissegundos e mandar um
			// tremor mais forte E mais longo de uma vez -- que e o que um planeta se partindo faz.
			MandarEfeito(pl, "terremoto", (long)(700 + 1700 * agonia));

			switch (_rng.Next(1, 4))
			{
				case 1:
					Avisar(pl, "um estrondo desce das nuvens.");
					break;

				// `spawnExplosion(randturf, null, mexpressedBP/100, 5)` -- explosao PERTO, nao em
				// cima. O port nao tem explosao de cenario com raio; o que existe e o efeito de tela
				// e o chao caindo, e os dois juntos leem como a mesma coisa.
				case 2:
					MandarEfeito(pl, "explosao_final", (long)(300 + 500 * agonia));
					QuebrarChaoPertoDosJogadores(zona, porJogador);
					break;

				// `T.Destroy()` e `prob(50)` de virar `/turf/Other/Stars` -- o chao virando vacuo.
				case 3:
					QuebrarChaoPertoDosJogadores(zona, porJogador);
					break;
			}

			AbrirCrateras(pl, agonia);
		}
	}

	/// <summary>Teto de celulas de chao derrubadas por segundo, no pico da agonia. Ver <see cref="TremorDaExplosao"/>.</summary>
	private const double TetoDeCelulasPorSegundo = 1.0;

	/// <summary>
	/// ============================ AS CRATERAS -- O RAMO MORTO DO DM, AGORA VIVO ============================
	/// O `createCrater` do original mora no `if(4)` do `switch(pick(1,2,3))` (`Area_Death.dm:128`) --
	/// um ramo **inalcancavel**: `pick(1,2,3)` nunca sorteia 4, entao nem cratera nem relampago de chao
	/// jamais rodaram no jogo de verdade. O port nao havia portado ramo morto, e estava certo em nao
	/// portar; o dono pediu o efeito por nome (*"explosoes e crateras aparecendo"*), e agora ele existe
	/// -- **pela porta unica de marca de chao** (`MandarDecalque`), que ja tem prazo, teto de 120 vivos
	/// e despejo por mais-velho. Nao ha fila nova nem estado novo pra vazar.
	///
	/// A CRATERA GRANDE so aparece na segunda metade da agonia, e e uma so: ela dura 40 s (contra 18 da
	/// pequena) e ocupa a cota protegida de pecas. Ter as duas desde o segundo zero tiraria da rampa o
	/// unico degrau que o olho percebe sem comparar dois momentos.
	/// ====================================================================================================
	/// </summary>
	private void AbrirCrateras(ServerPlayer pl, double agonia)
	{
		int quantas = 1 + (int)(agonia * 2);
		for (int i = 0; i < quantas; i++)
		{
			var onde = new Vec2(
				pl.Pos.X + _rng.Next(-8, 9) * ZoneCollision.TileSize,
				pl.Pos.Y + _rng.Next(-8, 9) * ZoneCollision.TileSize);

			MandarDecalque(pl.Zone, Protocol.Decal.Cratera, onde, pl.Facing);
			MandarDecalque(pl.Zone, Protocol.Decal.Fumaca, onde, pl.Facing);
		}

		if (agonia >= 0.5)
			MandarDecalque(pl.Zone, Protocol.Decal.CrateraGrande, new Vec2(
				pl.Pos.X + _rng.Next(-10, 11) * ZoneCollision.TileSize,
				pl.Pos.Y + _rng.Next(-10, 11) * ZoneCollision.TileSize), pl.Facing);
	}

	/// <summary>
	/// A FORCA DO CEU EM FUNCAO DA AGONIA -- e ela **nao comeca em zero e nem perto disso**.
	///
	/// O piso e 0,45 porque esse e exatamente o limiar em que o raio comeca a cair
	/// (`TickDoRaio`: `ceu.Forca >= 0.45`). Um piso mais baixo daria minutos de "ceu de fim de mundo"
	/// sem um relampago sequer -- e o raio e o A2 do pedido do dono (*"varios efeitos climaticos como
	/// raios etc"*), que ja existe de graca porque a cadencia dele **ja escala com a forca do clima**
	/// (`aperto = 1.9 - ceu.Forca`). Ou seja: uma rampa, e o raio acelera junto sem uma linha a mais.
	/// </summary>
	private static double ForcaDoCeuNaAgonia(double agonia) => 0.45 + 0.55 * Math.Clamp(agonia, 0, 1);

	/// <summary>
	/// A CARGA DE 30 s DO VERB. Um relogio por pessoa, e ele **e interrompivel**.
	///
	/// ============================ ONDE ISTO DIVERGE DO ORIGINAL, E POR QUE ============================
	/// No DM a carga e um `sleep(300)` cru (`Planets.dm:355`): nada a interrompe -- nem morrer. Aqui
	/// ela cai se quem carrega for nocauteado, morrer ou sair do planeta.
	///
	/// **O que se ganha**: os trinta segundos viram uma JANELA, que e exatamente o desenho que o
	/// proprio original escolheu pro ultimato de chefe (`BEV_PD_CHARGE`, `BossEvents.dm:29`, com o
	/// comentario *"janela p/ matar/nocautear e interromper"*). Ter dois trinta-segundos com regras
	/// opostas no mesmo jogo seria o defeito, nao a fidelidade.
	/// **O que se perde**: um vilao muito mais forte que todo mundo perde a garantia de que o planeta
	/// explode -- ele agora precisa sobreviver meio minuto. O que, sendo ele muito mais forte, ele faz.
	/// ==========================================================================================
	/// </summary>
	private void TickDaCargaDeDestruicao(double dt)
	{
		if (_cargaDoPlanetDestroy.Count == 0) return;

		foreach (int id in _cargaDoPlanetDestroy.Keys.ToList())
		{
			(double faltam, ZoneKey zona, double bp) = _cargaDoPlanetDestroy[id];

			if (!_players.TryGetValue(id, out ServerPlayer? pl))
			{
				_cargaDoPlanetDestroy.Remove(id);
				continue;
			}

			if (pl.Ficha.KO || pl.Ficha.dead || !pl.Zone.Equals(zona))
			{
				_cargaDoPlanetDestroy.Remove(id);
				MandarEfeito(pl, "carga_final", 0);
				Avisar(pl, "a energia que você juntava se desfaz.");
				AnunciarNoPlaneta(zona, $"A energia que {pl.Name} juntava sobre {zona.Name} SE DESFEZ!");
				continue;
			}

			faltam -= dt;
			if (faltam > 0) { _cargaDoPlanetDestroy[id] = (faltam, zona, bp); continue; }

			_cargaDoPlanetDestroy.Remove(id);
			MandarEfeito(pl, "carga_final", 0);

			// ============================ O `bool` QUE NINGUEM LIA -- E O K TORNOU ISSO COMUM ============================
			// `ComecarDestruicao` devolve FALSO quando o planeta ja esta explodindo ou ja morreu, e
			// esta era a unica das chamadas do jogo que jogava a resposta fora. O sintoma media-se
			// pelo jogador: dois vilaos apertam Planet Destroy dentro da mesma janela de 30 s, os
			// DOIS passam pela recusa do verb (o registro ainda esta vazio) e os dois pagam 1000 de
			// Ki; aos 30 s o primeiro vence, e o segundo perde meio minuto e o Ki **sem uma linha
			// sequer** -- e ainda ficaria achando que a tecnica dele nao funciona.
			//
			// Isto passou a importar muito mais com o K: agora ha uma terceira porta pro mesmo
			// planeta, ela nao tem carga nenhuma e ela pode disparar a qualquer instante dos trinta
			// segundos de outra pessoa. O Ki nao volta (ele saiu no `:338` do original, como no DM),
			// mas o jogador pelo menos fica sabendo o que aconteceu com ele.
			// ======================================================================================================
			if (!ComecarDestruicao(zona, bp, $"Planet Destroy de {pl.Name}", pl.Id))
			{
				Avisar(pl, $"você solta tudo o que juntou e não acontece nada: {zona.Name} já estava "
						 + "condenado por outra mão.");
				GD.Print($"[server] a carga de {pl.Name} em '{zona.Name}' chegou tarde -- "
					   + "o planeta ja estava condenado");
			}
		}
	}

	// =====================================================================
	// O VERB DO JOGADOR -- `Planet_Destroy` (`Planets.dm:318-370`)
	// =====================================================================
	private void RegistrarTecnicasDaDestruicao()
	{
		IniciarLote("destruicao");
		Vivo("Planet_Destroy", PlanetDestroy);
	}

	/// <summary>
	/// O VERB, literal onde da e explicito onde nao da.
	///
	/// A ORDEM DAS RECUSAS E A DO ORIGINAL:
	///   1. `if(!usr.isVillain)` (`:323`) -- ver <see cref="EhVilao"/>;
	///   2. `Ki >= 1000 && expressedBP >= 10000 * Planetgrav` (`:326`);
	///   3. o lugar tem que ser um planeta destruivel, e nao o espaco (`:331`);
	///   4. o Ki e cobrado ANTES da confirmacao (`:338`).
	///
	/// ============================ A CAIXA DE DIALOGO NAO FOI PORTADA ============================
	/// O original abre um `input("Destroy this Planet?")` (`:339`) **depois** de cobrar os 1000 de Ki
	/// -- ou seja, clicar "No" custa mil de Ki e nao faz nada. Nao portei o defeito nem a caixa: o
	/// canal de habilidade deste port manda um id e mais nada, e a confirmacao virou os trinta
	/// segundos de carga, que sao publicos e canceláveis. Quem apertou sem querer e nocauteado pelos
	/// outros -- o que e uma confirmacao melhor do que um botao.
	/// ====================================================================================
	/// </summary>
	private void PlanetDestroy(ServerPlayer pl)
	{
		if (_cargaDoPlanetDestroy.ContainsKey(pl.Id))
		{
			Avisar(pl, "você já está juntando essa energia.");
			return;
		}

		// 1. SO VILAO.
		if (!EhVilao(pl))
		{
			Avisar(pl, "só um vilão tem vontade de arrasar um planeta.");
			return;
		}

		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "você não está em condições."); return; }

		// 3. O LUGAR. (Antes do custo, porque recusar por lugar e mais barato que recusar por Ki, e
		// porque cobrar Ki de quem esta no espaco seria cobrar por nada.)
		ZoneKey zona = pl.Zone;
		if (ChaveDePlaneta.Da(zona) is not { } chave)
		{
			Avisar(pl, "não dá pra usar Planet Destroy aqui.");
			return;
		}
		if (_mortos.De(chave) != null)
		{
			Avisar(pl, $"{zona.Name} já está condenado.");
			return;
		}

		// 2. O CUSTO -- os dois numeros do `1A Defines` do original.
		double bpExigido = MortePlanetaria.BpExigido(pl.Ficha.Planetgrav);
		if (pl.Ficha.Ki < MortePlanetaria.KiDaDestruicao || pl.Ficha.expressedBP < bpExigido)
		{
			Avisar(pl, $"você não tem energia. São {MortePlanetaria.KiDaDestruicao:0} de Ki e "
					   + $"{bpExigido:0} de BP expresso aqui (a gravidade deste planeta é "
					   + $"{pl.Ficha.Planetgrav:0.##}x). Você tem {pl.Ficha.Ki:0} de Ki e "
					   + $"{pl.Ficha.expressedBP:0} de BP.");
			return;
		}

		// 4. O KI SAI AGORA (`:338`).
		pl.Ficha.Ki -= MortePlanetaria.KiDaDestruicao;

		// 6. `var/mexpressedBP = usr.expressedBP` (`:342`) -- fixado AQUI, antes da carga.
		_cargaDoPlanetDestroy[pl.Id] = (MortePlanetaria.SegundosDeCarga, zona, pl.Ficha.expressedBP);
		MandarEfeito(pl, "carga_final", -1);

		Avisar(pl, $"você começa a concentrar tudo o que tem sobre {zona.Name}. Trinta segundos.");
		AnunciarNoPlaneta(zona, $"*{pl.Name} está concentrando energia para DESTRUIR {zona.Name}!* "
							  + "Nocauteiem-no em 30 SEGUNDOS!");
		GD.Print($"[server] {pl.Name} comecou a carga do Planet Destroy em '{zona.Name}' "
			   + $"(BP fixado em {pl.Ficha.expressedBP:0})");
	}

	/// <summary>
	/// ============================ QUEM E VILAO NESTE PORT ============================
	/// O DM responde com um `mob/var/isVillain`, e o comentario da propria skill diz como ele e
	/// preenchido: *"only an admin-designated Villain can learn it"* (`Planets.dm:382`). Ou seja: **a
	/// designacao e um ato de admin**, e nao uma consequencia do que a pessoa fez.
	///
	/// Portei isso literalmente: `Fighter.isVillain`, persistido junto da ficha, escrito pelo verb
	/// `admin_vilao`. As alternativas foram consideradas e recusadas:
	///   * **reputacao <= -30** (`Reputacao.LimiarDeVilao`) -- e por PLANETA, e faria "ser inimigo do
	///     povo da Terra" abrir a arma que apaga a Terra. Um ciclo, e um que o DM nao tem.
	///   * **karma / PK** -- o sistema de karma nao foi portado.
	///
	/// **Isto e a unica skill `vilao: 1` do catalogo inteiro** (medido: 1 de 366 entradas do
	/// `skills.json`). Entao "quem e vilao" nao e uma pergunta de sistema social -- e o interruptor
	/// desta arma, e so dela.
	/// ==================================================================
	/// </summary>
	public static bool EhVilao(ServerPlayer pl) => pl.Ficha.isVillain;

	// =====================================================================
	// DESFAZER -- e por que so ha UMA porta
	// =====================================================================
	/// <summary>
	/// INTERROMPE UMA MORTE EM CURSO. E o `abort_planet_destroy` do original
	/// (`BossEvents.dm:836-855`), que existe porque ate o DM precisou de uma faxina.
	///
	/// **So funciona antes do commit.** Depois de destruido, o caminho e o
	/// <see cref="RessuscitarPlaneta"/>.
	/// </summary>
	public bool AbortarMorte(ZoneKey zona, string motivo)
	{
		if (ChaveDePlaneta.Da(zona) is not { } chave) return false;
		EstadoDaMorte? e = _mortos.De(chave);
		if (e == null || MortePlanetaria.EstaMorto(e.Fase)) return false;

		_mortos.Tirar(chave);
		_proximoTremor.Remove(chave.Texto);
		ForcarClima(zona, TipoDeClima.Limpo, 0);
		SalvarPlanetasMortos();
		MandarMortosPraTodos();

		AnunciarNoPlaneta(zona, $"O céu de {zona.Name} se abre. O planeta vai viver.");
		GD.Print($"[server] morte de '{zona.Name}' ABORTADA ({motivo})");
		return true;
	}

	/// <summary>
	/// ============================ "HEAL PLANET": A DECISAO, ESCRITA ============================
	/// No original ha tres desfazimentos: o desejo de Dragon Ball `"Heal Planet"`
	/// (`WishTable.dm:327-343`), o verb de admin `Restaurar_Planeta` (`Planets.dm:254-280`) e o
	/// `Planet_Options` (`:282-311`).
	///
	/// ============================ ISTO DIZIA "NAO HA ESFERAS DO DRAGAO", E ISSO CADUCOU ============================
	/// A frase antiga era *"o desejo nao existe neste port -- nao ha Esferas do Dragao, nem coleta,
	/// nem invocacao, nem tabela de desejos"*. **As tres primeiras existem agora**
	/// (`GameServer.Esferas.cs`): a estatua se ergue, as sete nascem, se espalham por semente, se
	/// pegam do chao e o dragao sobe. O que falta e so a TABELA -- a Fase 2 --, e ela ja tem ponto de
	/// plugue nomeado (`AbrirOsDesejos` / `ContarUmDesejo`).
	///
	/// **A DECISAO ABAIXO NAO MUDOU**, e e importante que nao mude por engano no dia em que a Fase 2
	/// chegar: o desejo do original e defeituoso -- ele zera `isDestroyed` e **nao tira da
	/// `PlanetDisableList`**, entao o planeta volta a morrer no boot seguinte, e foi justamente isso
	/// que o verb de admin (escrito depois, com `Save_Settings()`) veio consertar. Se a Fase 2 portar
	/// "Heal Planet", ela tem que passar pelo `RessuscitarPlaneta` daqui e nao por um caminho proprio.
	/// ==========================================================================================================
	///
	/// Entao a escolha aqui e a **(A)**: destruicao PERMANENTE, com valvula de admin. E a unica das
	/// tres que fecha o sistema sem inventar escopo. A consequencia esta anotada e e real: a saga 1
	/// destroi **Vegeta**, que e o berco dos Saiyajin -- e por isso o berco tem filtro (ver
	/// `GameServer.Berco.cs`), senao um recem-nascido acordaria num cadaver.
	/// ======================================================================================
	/// </summary>
	public bool RessuscitarPlaneta(ZoneKey zona)
	{
		if (ChaveDePlaneta.Da(zona) is not { } chave) return false;
		if (!_mortos.Tirar(chave)) return false;

		_proximoTremor.Remove(chave.Texto);
		SalvarPlanetasMortos();
		MandarMortosPraTodos();

		AnunciarNoMundo($"{zona.Name} voltou a existir.");

		// ============================ E O PORUNGA VOLTA COM NAMEK ============================
		// A outra metade do pedido do dono (*"so voltando quando o planeta e restaurado pelas esferas de
		// outro lugar"*), e ela e uma linha porque o set eterno **e derivavel de constantes**: quem sabe
		// erguer o Porunga ja existe, ja e idempotente e ja pergunta se o planeta esta vivo. Ver
		// `ErguerOSetEterno`.
		//
		// **CHAMADA SEM `if`, e de proposito.** Perguntar aqui *"a zona restaurada e Namek?"* seria uma
		// SEGUNDA copia de "onde o set eterno mora" -- e ela existe uma vez so, no
		// `Esferas.PlanetaEterno`. Restaurando outro planeta com o Porunga de pe, o metodo cai na
		// primeira linha dele (o zelador); restaurando outro planeta com Namek ainda morta, ele recusa
		// pelo proprio portao. Os dois casos ja estao certos la dentro.
		//
		// **O SET DE JOGADOR NAO VOLTA**, e nao ha linha nenhuma sobre isso aqui: ele nao volta porque
		// ninguem o reconstroi -- a estatua de alguem nao e derivavel de constante. Ver o cabecalho do
		// `EnterrarSetsDeMundosMortos`.
		// ================================================================================
		ErguerOSetEterno();

		GD.Print($"[server] '{zona.Name}' RESTAURADO -- fora da lista de mortos");
		return true;
	}

	// =====================================================================
	// O CANAL PRO CLIENTE
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE O CLIENTE PRECISA DA LISTA INTEIRA ============================
	/// Um bit no `S2C.Vizinhanca` **nao resolveria**: a carta estelar do cliente **enumera planetas
	/// sozinha**, chamando `Espaco.PreFeitos()` e `Sistemas.Do` direto (`Client/MapaEstelar.cs`), e
	/// ela desenha o que esta a anos-luz da vizinhanca ativa. Sem a lista, ela poria um botao
	/// "Viajar" em cima de um planeta que nao existe mais -- e a carta mentiria.
	///
	/// **E ela e pequena por construcao**: so entra planeta que alguem matou. Num servidor com as
	/// quatro sagas consumadas sao duas ou tres entradas.
	/// ==============================================================================================
	/// </summary>
	internal void MandarMortos(ServerPlayer pl)
	{
		if (pl.Peer == null) return;

		var w = Protocol.Begin(Protocol.S2C.Mortos);
		w.Put((byte)Math.Min(_mortos.Quantos, 255));
		foreach (EstadoDaMorte e in _mortos.Todos.Take(255))
		{
			w.Put(e.Chave);
			w.Put(e.Nome);
			w.Put((byte)e.Fase);
			w.Put((byte)Math.Clamp(e.Estagio, 0, 255));

			// ============================ O RELOGIO DA AGONIA, E POR QUE ELE FALTAVA ============================
			// Sem este `double` o cliente sabia QUE um planeta esta explodindo e nao sabia HA QUANTO
			// TEMPO -- ou seja, a rampa do dono (*"vai se intensificando durante esses 5 minutos"*) era
			// **impossivel por falta de byte, e nao por falta de shader**. E ele nem podia integrar
			// sozinho: este pacote so sai quando algo MUDA DE ESTAGIO, e a explosao nao tem estagio
			// nenhum pelos 310 s.
			//
			// SEGUNDOS QUE FALTAM E NAO INSTANTE ABSOLUTO, igual ao `EstadoDaMorte.Faltam` que ele
			// espelha -- ver la o porque (o relogio do mundo anda com o servidor desligado). O cliente
			// converte pra prazo absoluto **no instante em que recebe**, que e o que ele sabe fazer e o
			// servidor nao: ele tem o relogio de mundo sincronizado e o pacote acabou de chegar. Assim
			// a rampa vira funcao pura do relogio e nao custa nem mais um pacote.
			// ================================================================================================
			w.Put(e.Faltam);
		}

		// ============================ A BANCADA LE O PACOTE, E NAO A INTENCAO ============================
		// Este e o UNICO pacote que carrega estado de morte de planeta, e o sigilo do K5 depende de
		// uma afirmacao sobre ELE: *"nada da ferida viaja"*. Afirmar isso lendo o codigo seria
		// intencao; a bancada le os BYTES que saem daqui e varre cada janela deles atras dos numeros
		// proibidos (o limiar, a vida, o quanto falta). `CopyData` depois do pacote pronto, pelo mesmo
		// motivo do `EscutaDeDecalques`: o writer e reusado no envio.
		// Nula em jogo -- uma comparacao contra null por envio, e este pacote so sai quando um planeta
		// muda de estagio. Ver `GameServer.DestruicaoProva.cs`.
		// ==========================================================================================
		EscutaDeMortos?.Add(w.CopyData());

		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	private void MandarMortosPraTodos()
	{
		foreach (ServerPlayer p in _players.Values) if (p.Peer != null) MandarMortos(p);
	}

	// =====================================================================
	// OS VERBS DE ADMIN
	// =====================================================================
	/// <summary>
	/// LIGA/DESLIGA O BIT DE VILAO do alvo marcado -- o `isVillain` que so o admin escreve
	/// (`Planets.dm:382`). Sem alvo, em mim mesmo (a convencao dos outros verbs deste arquivo).
	///
	/// **Re-manda as skills forcado**: a lista de compraveis do cliente muda com este bit, e a
	/// assinatura anti-repeticao nao mudaria por conta propria (marcos e aprendidas continuam iguais).
	/// </summary>
	private void AdminVilao(ServerPlayer adm, string arg)
	{
		ServerPlayer alvo = PorNome(arg) ?? adm;
		alvo.Ficha.isVillain = !alvo.Ficha.isVillain;

		MandarSkills(alvo, forcar: true);
		Persistir(alvo);

		Avisar(adm, $"{alvo.Name} {(alvo.Ficha.isVillain ? "AGORA E" : "nao e mais")} um vilao.");
		if (alvo != adm)
			Avisar(alvo, alvo.Ficha.isVillain
				? "algo mudou em voce. O mundo passa a ser um alvo."
				: "a vontade de destruir te abandona.");
	}

	/// <summary>Acende o pavio lento no planeta em que o admin esta -- 20 min ate a explosao.</summary>
	private void AdminMorteLenta(ServerPlayer adm)
	{
		if (!ComecarMorteLenta(adm.Zone, adm.Ficha.expressedBP, $"admin {adm.Name}", adm.Id))
		{
			Avisar(adm, $"nao da pra matar {adm.Zone.Name} devagar (nao e planeta, ou ja esta condenado).");
			return;
		}
		Avisar(adm, $"{adm.Zone.Name} comecou a morrer. {MortePlanetaria.PavioInteiro / 60:0} minutos, "
				  + "quatro estagios, e entao a explosao.");
	}

	/// <summary>Pula o pavio: os cinco minutos e o commit. O BP do commit e o do admin.</summary>
	private void AdminDestruirPlaneta(ServerPlayer adm)
	{
		if (!ComecarDestruicao(adm.Zone, adm.Ficha.expressedBP, $"admin {adm.Name}", adm.Id))
		{
			Avisar(adm, $"nao da pra destruir {adm.Zone.Name} (nao e planeta, ou ja esta explodindo).");
			return;
		}
		Avisar(adm, $"{adm.Zone.Name} tem {MortePlanetaria.SegundosDeExplosao / 60:0.#} minutos. "
				  + $"Quem tiver BP expresso ate {adm.Ficha.expressedBP:0} nao sobrevive.");
	}

	private void AdminAbortarMorte(ServerPlayer adm)
	{
		if (!AbortarMorte(adm.Zone, $"admin {adm.Name}"))
			Avisar(adm, $"{adm.Zone.Name} nao esta morrendo (ou ja morreu -- ai e Restore Planet).");
		else
			Avisar(adm, $"a morte de {adm.Zone.Name} foi interrompida.");
	}

	/// <summary>
	/// O `Restaurar_Planeta` (`Planets.dm:254-280`) -- **a unica volta que existe neste port**.
	///
	/// Ele age sobre a zona em que o admin ESTA, e isso e uma diferenca do original de proposito:
	/// la o verb pede o nome numa lista, e aqui o planeta destruido nao aceita pouso -- entao o
	/// admin precisa de outra forma de chegar. Ele chega **em orbita** (o pouso e recusado) e o verb
	/// atua na zona sob ele... o que nao funcionaria. Por isso este verb tambem aceita o planeta que
	/// esta LOGO ABAIXO de quem esta no espaco.
	/// </summary>
	private void AdminRestaurarPlaneta(ServerPlayer adm)
	{
		ZoneKey alvo = adm.Zone;

		// NO ESPACO, o alvo e o disco sob o corpo -- senao um planeta morto seria inalcancavel: ele
		// recusa pouso (e por isso que ele esta morto), e o admin ficaria sem como apontar pra ele.
		if (Espaco.EhEspaco(alvo))
		{
			if (Espaco.PlanetaSob(SeedDoUniverso, adm.Pos) is not { } sob)
			{
				Avisar(adm, "voce esta no vazio -- va ate a orbita do planeta que quer restaurar.");
				return;
			}
			alvo = Espaco.ZonaDe(sob);
		}

		if (!RessuscitarPlaneta(alvo))
		{
			Avisar(adm, $"{alvo.Name} nao esta na lista de mortos.");
			return;
		}
		Avisar(adm, $"{alvo.Name} volta a existir. Da pra pousar, e o povo volta na proxima manutencao.");
	}

	/// <summary>
	/// UMA LINHA PRA QUEM ESTA NO PLANETA. Nao usa o `AnunciarNoMundo` (que fala com o servidor
	/// inteiro): a maior parte dos avisos da morte lenta so faz sentido pra quem esta olhando o ceu.
	/// </summary>
	private void AnunciarNoPlaneta(ZoneKey zona, string texto)
	{
		foreach (ServerPlayer p in ZoneList(zona.Hash))
			if (p.Peer != null) Avisar(p, texto);
		GD.Print($"[planeta] {zona.Name}: {texto}");
	}
}
