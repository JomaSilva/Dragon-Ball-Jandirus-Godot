using System.IO;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Items;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DO VACUO (`--vacuoteste`) -- UMA FAMILIA POR NEGACAO DO PEDIDO.
///
/// ============================ O PEDIDO DO DONO, LITERAL ============================
///     *"faca todas as racas MENOS a FROST DEMON, ALIEN e MAJIN sofrerem DANO POR SEGUNDO NO ESPACO
///      pois elas n conseguem respirar la, precisando da ROUPA ESPACIAL ou estar dentro de uma POD
///      ou NAVE CAPITAL SHIP"*
///
/// Isso nao e um recurso: e uma regra descrita por EXCECOES. Tres racas, tres abrigos, e todo o
/// resto morrendo. Cada excecao dessas tem um consumidor diferente (a zona, a nave, a mochila, o
/// cargo, a cinematica), e o jeito conhecido de uma passar batido e provar so a primeira -- "o
/// Saiyajin perdeu vida, pronto" -- e descobrir seis meses depois que TODO MUNDO perdia vida em
/// TODA PARTE, ou que ninguem perdia em lugar nenhum. Por isso cada excecao virou uma FAMILIA.
/// ==============================================================================
///
/// ============================ E CADA FAMILIA TEM QUE SABER REPROVAR ============================
/// Uma prova que nunca ficou vermelha e uma frase. Toda familia aqui declara os DEFEITOS que ela
/// existe pra pegar, a bancada INJETA cada um (trocando as <see cref="SondasDoVacuo"/> por baixo das
/// mesmas provas) e exige que a familia fique vermelha. Se ela continuar verde com o defeito dentro,
/// isso sai como `CEGA` -- e um buraco de cobertura, nao um sucesso.
///
/// Os defeitos nao sao inventados. Cada um e um erro que este projeto ja cometeu ou que a proxima
/// pessoa cometeria de boa fe:
///
///   * *"o dano ganhou um divisor de BP por simetria com o sol"* -- o irmao deste sistema escala com
///     o poder do corpo, e copiar aquilo aqui faria o fim de jogo nadar no vacuo de graca;
///   * *"a pergunta de zona ficou larga"* -- `EhEspaco` virar "tudo que nao e planeta pre-feito" faz
///     o interior da nave-capital e todo planeta GERADO virarem vacuo, e ai todo mundo sufoca em
///     toda parte;
///   * *"o abrigo da pod foi esquecido"* -- a nave COPIA a zona do piloto, entao quem pilota esta,
///     pra todos os efeitos do servidor, dentro da zona do espaco;
///   * *"o guarda do `Intocavel` saiu"* -- foi exatamente o furo que o calor da estrela teve;
///   * *"a dose virou por TIQUE"* -- a mesma armadilha do `TickDoEstomago`, e a 30 Hz o castigo
///     seria trinta vezes maior.
///
/// ============================ OS CONTRA-EXEMPLOS SAO METADE DA BANCADA ============================
/// Metade das provas afirma o CONTRARIO do vacuo, e elas nao sao enfeite. Sem *"no chao de um
/// planeta NAO perde"*, uma pergunta de zona larga demais passaria verde e o jogo inteiro viraria
/// uma camara de gas. Sem *"o Saiyajin PERDE"*, a regra invertida passaria verde. Sem *"desembarcou
/// da pod e voltou a perder"*, um abrigo que nunca desliga passaria verde. Um sistema so esta
/// provado quando as duas respostas -- sim e nao -- saem do mesmo lugar.
/// ==================================================================================================
///
/// ============================ E ELA MEDE ENTRANDO, NAO ESTANDO ============================
/// Esta casa ja pagou por bancada que nasce DENTRO do estado que devia testar (ver a nota da agua:
/// 48 provas verdes e o jogador nao conseguia molhar o pe). Entao o corpo desta bancada **comeca em
/// terra firme**, prova que ali nao perde nada, e so entao vai pro vacuo pelo `MoveToZone` de
/// producao -- e o traje entra e SAI da mochila, e o piloto EMBARCA e DESEMBARCA.
/// ======================================================================================
///
///     Godot --headless --path . --host --rede 7920 --vacuoteste
/// </summary>
public partial class GameServer
{
	private bool _vacuoDeTeste;

	/// <summary>Faixa de ids desta bancada -- longe do `_nextId`, da do sol (90.400) e da do convivio.</summary>
	private const int IdBaseDoVacuoDeTeste = 90_600;

	/// <summary>Faixa de ids das naves de papel desta bancada.</summary>
	private const int IdBaseDaNaveDoVacuo = 90_690;

	/// <summary>
	/// Teto da medicao de morte, em segundos. O prazo do DM e 20 s
	/// (<see cref="Vacuo.SegundosDeFolego"/>); 60 e o triplo. Bater no teto e FALHA -- e o que
	/// impede um laco infinito no dia em que alguem mexer no dano pra baixo.
	/// </summary>
	private const int SegundosMaximosDaMedicaoDoVacuo = 60;

	private int _vacProximoId;
	private readonly List<ServerPlayer> _corposDoVacuo = [];
	private readonly List<Nave> _navesDoVacuo = [];

	/// <summary>
	/// A ZONA DE CHAO da bancada -- a do host, que e um planeta de verdade com mapa ja carregado.
	/// Ela e o contra-exemplo mais importante do arquivo (familia 6) e o ponto de partida de quase
	/// toda familia (o corpo nasce em terra firme e ENTRA no vacuo).
	/// </summary>
	private ZoneKey _vacZonaDeChao;

	/// <summary>
	/// COMO A BANCADA LE O FONTE DO SERVIDOR (familia 10). Nulo = leitura de verdade. Existe porque a
	/// prova estrutural tambem precisa saber reprovar: o defeito e injetado devolvendo o arquivo com
	/// a chamada apagada. Mesmo truque do `LerFonte` da bancada da agua.
	/// </summary>
	private Func<string, string>? _vacFonteMutante;

	// =====================================================================
	// O MOTOR: placar, familias e injecao
	// =====================================================================
	private sealed class PlacarDoVacuo
	{
		public int Ok, Falhas;
		public bool Mudo;
		public readonly List<string> Vermelhas = [];
		public readonly List<string> SemCobertura = [];

		public void Prova(string oQue, bool passou, string detalhe = "")
		{
			if (passou) Ok++; else { Falhas++; Vermelhas.Add(oQue); }
			if (Mudo) return;
			if (passou) GD.Print($"[vacuo]   ok    {oQue}   {detalhe}");
			else GD.PrintErr($"[vacuo]   FALHA {oQue}   {detalhe}");
		}

		/// <summary>Prova que nao deu pra fazer (falta o fonte, falta o dado).</summary>
		public void NaoDeu(string oQue)
		{
			SemCobertura.Add(oQue);
			if (!Mudo) GD.Print($"[vacuo]   --    {oQue}  (sem cobertura)");
		}
	}

	private sealed class FamiliaDoVacuo
	{
		public required string Nome { get; init; }
		public required string Frase { get; init; }
		public required Action<PlacarDoVacuo> Provas { get; init; }
		public required List<(string Nome, Action<SondasDoVacuo> Injetar)> Defeitos { get; init; }
	}

	public void RodarBancadaDoVacuo(ServerPlayer host)
	{
		GD.Print("[vacuo] ================ O VACUO COBRA -- uma familia por negacao do pedido ================");

		_vacZonaDeChao = host.Zone;
		GD.Print($"[vacuo] zona de chao (a do host): {_vacZonaDeChao}   |   zona do vacuo: {ZonaDoEspaco}");

		// O HOST NAO PODE ESTAR NO VACUO: este laco varre `_players` de verdade, e uma medicao que
		// comecasse com o dono da tela la em cima o mataria pra medir o proprio dano.
		if (Espaco.EhEspaco(_vacZonaDeChao))
		{
			GD.PrintErr("[vacuo] ABORTADA: o host esta NO VACUO, e a bancada o mataria. Pouse antes.");
			return;
		}

		List<FamiliaDoVacuo> familias =
		[
			ATaxaPorSegundo(),
			AsTresRacasDoDono(),
			OAbrigoDaPod(),
			OAbrigoDaNaveCapital(),
			OAbrigoDoTraje(),
			ForaDoVacuoNinguemSufoca(),
			ACinematicaNaoSufoca(),
			OFolegoDoCargo(),
			DaPraMorrerDisso(),
			ACorrenteEstaLigada(),
			OFolegoDoDnaDoBio(),
		];

		int provas = 0, falhas = 0, defeitos = 0, cegos = 0, semCobertura = 0;
		var buracos = new List<string>();

		foreach (FamiliaDoVacuo f in familias)
		{
			GD.Print($"[vacuo] === {f.Nome} ===");
			GD.Print($"[vacuo]     \"{f.Frase}\"");

			var sao = new PlacarDoVacuo();
			RodarProvas(f, sao);
			provas += sao.Ok + sao.Falhas;
			falhas += sao.Falhas;
			semCobertura += sao.SemCobertura.Count;
			foreach (string s in sao.SemCobertura) buracos.Add($"{f.Nome}: {s}");

			GD.Print("[vacuo]   -- e ela reprova assim:");
			foreach ((string nome, Action<SondasDoVacuo> injetar) in f.Defeitos)
			{
				defeitos++;
				var mutante = new SondasDoVacuo { Pilotando = EstaPilotando };
				injetar(mutante);

				var p = new PlacarDoVacuo { Mudo = true };
				_sondasDoVacuo = mutante;
				RodarProvas(f, p);

				if (p.Falhas == 0)
				{
					cegos++;
					GD.PrintErr($"[vacuo]      [CEGA] {nome}");
					GD.PrintErr("[vacuo]             ...a familia continuou VERDE com o defeito dentro.");
					buracos.Add($"{f.Nome}: cega para \"{nome}\"");
				}
				else
				{
					GD.Print($"[vacuo]      [pega] {nome}");
					GD.Print($"[vacuo]             -> {p.Falhas} prova(s) em vermelho, a 1a: \"{Curto(p.Vermelhas[0])}\"");
				}
			}
		}

		GD.Print("[vacuo] ================ PLACAR ================");
		GD.Print($"[vacuo]   familias           : {familias.Count}");
		GD.Print($"[vacuo]   provas             : {provas}   ({provas - falhas} verdes, {falhas} vermelhas)");
		GD.Print($"[vacuo]   defeitos injetados : {defeitos}   ({defeitos - cegos} pegos, {cegos} passaram batido)");
		GD.Print($"[vacuo]   provas sem rodar   : {semCobertura}");
		foreach (string b in buracos) GD.PrintErr($"[vacuo]     - {b}");
		GD.Print(falhas == 0 && cegos == 0
			? "[vacuo] ================ OK -- toda familia esta verde E sabe ficar vermelha ================"
			: "[vacuo] ================ ATENCAO -- ha familia vermelha ou cega acima ================");
	}

	/// <summary>
	/// RODA UMA FAMILIA E LIMPA TUDO DEPOIS -- inclusive quando a familia estoura.
	///
	/// A limpeza no `finally` e o que permite rodar a mesma familia cinco vezes (uma sa e quatro
	/// mutantes) sem que a segunda meça os corpos moribundos da primeira. E ela desfaz TRES coisas: os
	/// corpos forjados, as naves de papel e as sondas mutantes -- devolver `null` nas sondas remonta
	/// os padroes de producao, entao nao ha campo pra esquecer de repor.
	/// </summary>
	private void RodarProvas(FamiliaDoVacuo f, PlacarDoVacuo p)
	{
		try { RetratarOMundo(); f.Provas(p); }
		catch (Exception e)
		{
			// UM DEFEITO QUE FAZ A BANCADA ESTOURAR TAMBEM E UM DEFEITO PEGO -- so nao pode passar
			// despercebido, entao ele sai com o nome da excecao.
			p.Prova($"estourou: {e.GetType().Name} {e.Message}", false);
		}
		finally
		{
			LimparAOficinaDoVacuo();
			_sondasDoVacuo = null;
			_vacFonteMutante = null;
			DevolverOMundo();
		}
	}

	/// <summary>
	/// ============================ O DEFEITO INJETADO NAO PODE MACHUCAR QUEM ESTA JOGANDO ============================
	/// **Isto e um achado da primeira rodada desta bancada, e nao uma precaucao teorica.** O defeito
	/// *"o mundo inteiro virou vacuo"* (`EhEspaco = _ => true`) e injetado por baixo do `TickDoVacuo`
	/// DE PRODUCAO -- e aquele laco varre `_players` inteiro. Ou seja: durante os 3 segundos daquela
	/// injecao, o HOST em Vegeta e os quarenta e tantos NPCs da Terra sufocaram de verdade, perderam
	/// 15 de vida cada um, entraram em combate e entraram no `_sufocando`. O rastro apareceu na
	/// familia seguinte, que colheu DEZ linhas de "você volta a respirar" que nao eram dela.
	///
	/// Uma bancada que estraga o mundo pra medir o mundo e uma bancada que ninguem pode rodar duas
	/// vezes. Entao ela fotografa quem NAO e dela antes de cada rodada e devolve tudo depois: vida de
	/// cada membro, decepados, nocaute, morte, tag de combate e o relogio do aviso.
	///
	/// A JANELA E CURTA POR CONSTRUCAO -- a exposicao maxima de um corpo de fora e a familia mais
	/// longa com uma injecao de zona larga (3 s = 15 de vida), bem abaixo dos 80 do nocaute e dos 100
	/// da morte. Ainda assim a devolucao cobre nocaute e morte: se um dia alguem escrever uma familia
	/// de 60 segundos com `EhEspaco` largo, o preco nao pode ser o personagem do dono.
	/// =========================================================================================================
	/// </summary>
	private readonly List<(ServerPlayer Pl, double[] Vidas, bool[] Decepados, bool Ko, bool Morto,
						   double EmCombate, double Nocaute, long RelogioDaMorte)> _retratoDoMundo = [];

	private void RetratarOMundo()
	{
		_retratoDoMundo.Clear();
		foreach (ServerPlayer pl in _players.Values)
		{
			if (pl.Combate == null) continue;
			List<BodyPart> partes = pl.Combate.Corpo.Partes;
			var vidas = new double[partes.Count];
			var decepados = new bool[partes.Count];
			for (int i = 0; i < partes.Count; i++) { vidas[i] = partes[i].Vida; decepados[i] = partes[i].Decepado; }
			_retratoDoMundo.Add((pl, vidas, decepados, pl.Ficha.KO, pl.Ficha.dead,
								 pl.Combate.EmCombate, pl.Combate.NocauteRestante, pl.RelogioDaMorte));
		}
	}

	private void DevolverOMundo()
	{
		double consertado = 0;
		foreach ((ServerPlayer pl, double[] vidas, bool[] decepados, bool ko, bool morto,
				  double emCombate, double nocaute, long relogio) in _retratoDoMundo)
		{
			if (pl.Combate == null) continue;
			List<BodyPart> partes = pl.Combate.Corpo.Partes;
			for (int i = 0; i < partes.Count && i < vidas.Length; i++)
			{
				consertado += Math.Abs(partes[i].Vida - vidas[i]);
				partes[i].Vida = vidas[i];
				partes[i].Decepado = decepados[i];
			}

			pl.Ficha.KO = ko;
			pl.Ficha.dead = morto;
			pl.Combate.EmCombate = emCombate;
			pl.Combate.NocauteRestante = nocaute;
			pl.RelogioDaMorte = relogio;
			pl.Combate.SincronizarVida();

			// E O RELOGIO DO AVISO TAMBEM: sem isto, um corpo de fora que sufocou durante a injecao
			// receberia a linha de alivio na familia seguinte, e ela apareceria na escuta de outra
			// familia -- que foi exatamente como este defeito se mostrou.
			EsquecerVacuo(pl.Id);
		}

		if (consertado > 0.001)
			GD.Print($"[vacuo]       (o defeito injetado tinha respingado {consertado:0.#} de vida em corpos "
					 + "de FORA da bancada -- devolvido)");
		_retratoDoMundo.Clear();
	}

	private static string Curto(string s) => s.Length <= 80 ? s : s[..78] + "..";

	// =====================================================================
	// FAMILIA 1 -- A TAXA
	// =====================================================================
	/// <summary>
	/// *"sofrerem DANO POR SEGUNDO NO ESPACO"* -- e a bancada diz O NUMERO, porque "caiu" nao e uma
	/// medicao: caiu quanto, por quanto tempo, e em cima do que?
	///
	/// A taxa e <see cref="Vacuo.DanoPorSegundo"/> = vida cheia do nucleo / 20 s = **5,00 por membro
	/// por segundo**, e os 20 s sao o `spacetime = 100` do `Stats.dm:120` decrementado a cada
	/// `sleep(2)` (0,2 s). Tres coisas sao afirmadas junto com ela, e cada uma ja foi um bug em
	/// algum sistema desta casa:
	///
	///   * **e por SEGUNDO e nao por tique** -- a 30 Hz o castigo seria trinta vezes maior;
	///   * **nao olha o BP** -- o corpo de 1e13 sufoca no mesmo ritmo do recem-nascido, porque fogo se
	///     resiste e ar e ar. No DM `spacetime` e o mesmo 100 pra qualquer um;
	///   * **a cura passiva nao devolve nada** -- o dano entra em combate (`EntrarEmCombate`) e a
	///     `RegenerarPassivo` tem `EmCombate > 0` na guarda. Esta bancada roda a cura 30x por segundo
	///     como o servidor roda: se ela nao estivesse barrada, a taxa medida seria 3,33 e nao 5,00.
	/// </summary>
	private FamiliaDoVacuo ATaxaPorSegundo() => new()
	{
		Nome = "FAMILIA 1 -- UM SAIYAJIN NO VACUO PERDE VIDA POR SEGUNDO, E A TAXA E ESTA",
		Frase = "todas as racas ... sofrerem DANO POR SEGUNDO NO ESPACO",
		Provas = p =>
		{
			ServerPlayer sai = CorpoDeVacuo("saiyajin", "Saiyan", _vacZonaDeChao);

			// ---- A ENTRADA: em terra firme, um segundo inteiro nao custa nada
			double emTerra = MenorNucleo(sai);
			UmSegundoDeMundo();
			p.Prova("em terra firme o mesmo corpo nao perde NADA em 1 s",
					Igual(MenorNucleo(sai), emTerra), $"{emTerra:0.###} -> {MenorNucleo(sai):0.###}");

			// ---- ...e agora ele ENTRA no vacuo, pelo `MoveToZone` de producao
			MoveToZone(sai.Id, ZonaDoEspaco, PontoDoVacuo());

			var antes = new Dictionary<string, double>();
			foreach (BodyPart b in sai.Combate!.Corpo.Partes) antes[b.Nome] = b.Vida;

			UmSegundoDeMundo();

			// ============================ O QUE A MEDICAO ACHOU, E QUE NAO E BUG ============================
			// A primeira versao desta prova exigia que TODO membro externo perdesse exatamente a taxa, e
			// ela reprovou com "8 de 9". O nono e o **Reprodutor**, e ele nao e uma excecao inventada
			// aqui: no original ele e `isnested = 0` com `targetchance = 55` (`mobparts_logic.dm`), ou
			// seja ele SAI NO SORTEIO como qualquer membro **e** e filho do Abdomen. Entao o
			// `SpreadDamage` bate nele direto (5,00) e o `Ferir` do Abdomen ainda escorre os 20% de
			// `Regras.Propagacao` por cima (mais 1,00). Ele e o unico membro externo com dono.
			//
			// A prova foi corrigida em vez de afrouxada: quem decide a morte e o NUCLEO
			// (`Body.DeveMorrer`), entao e a taxa DELE que tem que ser exata -- e o Reprodutor ganha
			// linha propria, com o numero que a propagacao produz, pra que mexer em `Propagacao` um dia
			// apareça aqui em vez de passar calado.
			// ==========================================================================================
			double esperado = Vacuo.DanoPorSegundo(100);
			int nucleos = 0, nucleosCertos = 0, externos = 0, perderamAlgo = 0;
			foreach (BodyPart b in sai.Combate.Corpo.Partes)
			{
				if (b.Aninhado || b.Decepado) continue;
				externos++;
				if (antes[b.Nome] - b.Vida > 0.001) perderamAlgo++;
				if (b.Papel != Vitalidade.Nucleo) continue;
				nucleos++;
				if (Igual(antes[b.Nome] - b.Vida, esperado)) nucleosCertos++;
			}

			p.Prova($"a taxa e {esperado:0.00} de vida por NUCLEO por segundo (= vida cheia / {Vacuo.SegundosDeFolego:0} s)",
					nucleos == 3 && nucleosCertos == nucleos,
					$"{nucleosCertos} de {nucleos} nucleos (Cabeca, Torso, Abdomen)");
			p.Prova("...e o dano foi ESPALHADO: TODO membro externo perdeu, nao um sorteado",
					externos >= 8 && perderamAlgo == externos, $"{perderamAlgo} de {externos} membros externos");

			BodyPart? reprodutor = sai.Combate.Corpo.Achar("Reprodutor");
			p.Prova($"...e o Reprodutor perde {esperado * (1 + Regras.Propagacao):0.00}: e o unico membro externo COM DONO "
					+ "(leva o golpe direto e mais os 20% que escorrem do Abdomen)",
					reprodutor != null && Igual(antes["Reprodutor"] - reprodutor.Vida, esperado * (1 + Regras.Propagacao)),
					reprodutor == null ? "sem Reprodutor" : $"{antes["Reprodutor"] - reprodutor.Vida:0.###}");
			p.Prova($"...e {Vacuo.SegundosDeFolego:0} s dessa taxa gastam um nucleo inteiro (o prazo do `spacetime`)",
					Igual(esperado * Vacuo.SegundosDeFolego, 100), $"{esperado * Vacuo.SegundosDeFolego:0.##}");

			// ---- O SEGUNDO SEGUNDO: e aqui que a cura passiva teria chance de devolver
			double depoisDoPrimeiro = MenorNucleo(sai);
			UmSegundoDeMundo();
			p.Prova("no 2o segundo a taxa e a MESMA -- a cura passiva nao devolve nada (o dano poe em combate)",
					Igual(depoisDoPrimeiro - MenorNucleo(sai), esperado),
					$"caiu {depoisDoPrimeiro - MenorNucleo(sai):0.###}, cura seria {CombatKnobs.RegenPorSegundo:0.##}/s");

			// ---- CINCO SEGUNDOS SEGUIDOS: a soma bate com a taxa (e nao ha aceleracao escondida)
			double antesDosCinco = MenorNucleo(sai);
			for (int i = 0; i < 5; i++) UmSegundoDeMundo();
			p.Prova("5 s seguidos custam exatamente 5x a taxa (sem aceleracao escondida)",
					Igual(antesDosCinco - MenorNucleo(sai), esperado * 5),
					$"{antesDosCinco - MenorNucleo(sai):0.###}");

			// ---- O BP NAO ENTRA NA CONTA: o mais forte do jogo ao lado do mais fraco
			ServerPlayer fraco = CorpoDeVacuo("recem-nascido", "Saiyan", ZonaDoEspaco, bp: 25);
			ServerPlayer forte = CorpoDeVacuo("o mais forte do jogo", "Saiyan", ZonaDoEspaco, bp: 1e13);
			double f0 = MenorNucleo(fraco), F0 = MenorNucleo(forte);
			UmSegundoDeMundo();
			p.Prova("o corpo de 1e13 de BP perde o MESMO que o de 25 -- o vacuo nao olha o poder",
					Igual(f0 - MenorNucleo(fraco), F0 - MenorNucleo(forte))
					&& Igual(F0 - MenorNucleo(forte), esperado),
					$"fraco {f0 - MenorNucleo(fraco):0.###} | forte {F0 - MenorNucleo(forte):0.###}");
		},
		Defeitos =
		[
			("a dose virou POR TIQUE e nao por segundo (a armadilha do `TickDoEstomago`, 30x)",
			 s => s.Dano = v => v / Vacuo.SegundosDeFolego / 30),

			("o prazo foi mexido pra 200 s sem ninguem olhar a taxa",
			 s => s.Dano = v => v / 200),

			("a regra foi invertida -- TODO MUNDO respira no vacuo",
			 s => s.Respira = (_, _, _, _) => true),

			("a pergunta de zona parou de achar o espaco (`EhEspaco` sempre falso)",
			 s => s.EhEspaco = _ => false),
		],
	};

	// =====================================================================
	// FAMILIA 2 -- AS TRES RACAS QUE O DONO NOMEOU
	// =====================================================================
	/// <summary>
	/// *"todas as racas MENOS a FROST DEMON, ALIEN e MAJIN"*.
	///
	/// **UMA LINHA POR RACA, COM O NOME.** "Alguem nao perde" ficaria verde com a regra invertida, e
	/// "as tres nao perdem" ficaria verde com a lista vazia se ninguem perdesse. Por isso as tres
	/// afirmacoes nominais andam sempre ao lado das tres NEGATIVAS (Saiyajin, Humano, Namekuseijin
	/// PERDEM) -- as duas metades saem do mesmo `TickDoVacuo`.
	///
	/// E ha duas armadilhas cobertas aqui que ja custaram caro neste port:
	///   * **"Icer" e o nome do proto; "Frost Demon" e o da CLASSE** (ver `FormasDeFrost.Raca`). As
	///     duas grafias tem que valer, senao metade dos Frost Demons sufoca;
	///   * **filho de Majin respira** -- `Race == X || Parent_Race == X` e a regra racial que todo
	///     requisito deste port ja segue (`PortasDeCargo.cs:135`).
	/// </summary>
	private FamiliaDoVacuo AsTresRacasDoDono() => new()
	{
		Nome = "FAMILIA 2 -- FROST DEMON, ALIEN E MAJIN NAO PERDEM (uma linha por raca)",
		Frase = "todas as racas MENOS a FROST DEMON, ALIEN e MAJIN",
		Provas = p =>
		{
			ServerPlayer icer = CorpoDeVacuo("Frost Demon (proto 'Icer')", Jandirus.Core.Races.FormasDeFrost.Raca, ZonaDoEspaco);
			ServerPlayer frost = CorpoDeVacuo("Frost Demon (a grafia que circula)", Jandirus.Core.Races.FormasDeFrost.ClasseNormal, ZonaDoEspaco);
			ServerPlayer alien = CorpoDeVacuo("Alien", "Alien", ZonaDoEspaco);
			ServerPlayer majin = CorpoDeVacuo("Majin", "Majin", ZonaDoEspaco);
			ServerPlayer filho = CorpoDeVacuo("filho de Majin com humana", "Human", ZonaDoEspaco, racaDoPai: "Majin");

			ServerPlayer saiyan = CorpoDeVacuo("Saiyajin", "Saiyan", ZonaDoEspaco);
			ServerPlayer humano = CorpoDeVacuo("Humano", "Human", ZonaDoEspaco);
			ServerPlayer namek = CorpoDeVacuo("Namekuseijin", "Namekian", ZonaDoEspaco);

			var antes = new Dictionary<int, double>();
			foreach (ServerPlayer pl in _corposDoVacuo) antes[pl.Id] = MenorNucleo(pl);

			UmSegundoDeMundo();

			void NaoPerdeu(ServerPlayer pl) =>
				p.Prova($"{pl.Name} NAO perde vida no vacuo",
						Igual(MenorNucleo(pl), antes[pl.Id]), $"{antes[pl.Id]:0.##} -> {MenorNucleo(pl):0.##}");

			void Perdeu(ServerPlayer pl) =>
				p.Prova($"{pl.Name} PERDE vida no vacuo (o par que segura a linha de cima)",
						MenorNucleo(pl) < antes[pl.Id] - 0.001, $"{antes[pl.Id]:0.##} -> {MenorNucleo(pl):0.##}");

			NaoPerdeu(icer);
			NaoPerdeu(frost);
			NaoPerdeu(alien);
			NaoPerdeu(majin);
			NaoPerdeu(filho);

			Perdeu(saiyan);
			Perdeu(humano);
			Perdeu(namek);

			// E A DIVERGENCIA MEDIDA, dita e nao escondida: o DM da folego a DEZESSEIS racas e este
			// port da a TRES, porque foi o que o dono pediu. Ver o cabecalho de `Core/World/Vacuo.cs`.
			p.Prova("a lista deste port tem as 3 do dono (e as 2 grafias de Frost Demon), e nao as 16 do DM",
					Vacuo.RacasQueRespiram.Length == 4
					&& !Vacuo.RacaRespira("Yardrat") && !Vacuo.RacaRespira("Kai") && !Vacuo.RacaRespira("Android"),
					string.Join(", ", Vacuo.RacasQueRespiram));
		},
		Defeitos =
		[
			("a lista racial sumiu -- so traje e cargo salvam",
			 s => s.Respira = (_, _, cargo, traje) => cargo || traje),

			("so a grafia 'Frost Demon' entrou na lista, e 'Icer' (o nome do PROTO) ficou de fora",
			 s => s.Respira = (raca, pai, cargo, traje) => cargo || traje
				  || EhUmaDestas(raca, "Frost Demon", "Alien", "Majin")
				  || EhUmaDestas(pai, "Frost Demon", "Alien", "Majin")),

			("a raca do PAI foi ignorada (filho de Majin passou a sufocar)",
			 s => s.Respira = (raca, _, cargo, traje) => Vacuo.RespiraNoVacuo(raca, null, cargo, traje)),

			("a regra foi INVERTIDA (respira quem nao devia, sufoca quem devia)",
			 s => s.Respira = (raca, pai, cargo, traje) => !Vacuo.RespiraNoVacuo(raca, pai, cargo, traje)),
		],
	};

	// =====================================================================
	// FAMILIA 3 -- A POD
	// =====================================================================
	/// <summary>
	/// *"estar dentro de uma POD"*.
	///
	/// **ESTE E O UNICO ABRIGO QUE PRECISOU DE LINHA ESCRITA**, e a razao e que a nave COPIA a zona do
	/// piloto (`GameServer.Nave.cs:659-662`): quem esta pilotando esta, pra todos os efeitos do
	/// servidor, DENTRO da zona do espaco. E o `pilot.ship = src` do DM (`PlanetTech.dm:133`), que faz
	/// o `!ship` do `Stats.dm:218` falhar.
	///
	/// O par que segura a linha e o DESEMBARQUE: um abrigo que nunca desliga e indistinguivel de
	/// imunidade, e ficaria verde numa bancada que so soubesse embarcar. O outro par e o VIZINHO --
	/// um corpo na mesma zona, a poucos pixels, sem nave: se ele tambem parasse de sofrer, o que a
	/// pod teria feito era tornar a ZONA segura, e nao o piloto.
	/// </summary>
	private FamiliaDoVacuo OAbrigoDaPod() => new()
	{
		Nome = "FAMILIA 3 -- DENTRO DA POD NAO PERDE",
		Frase = "precisando ... estar dentro de uma POD",
		Provas = p =>
		{
			ServerPlayer piloto = CorpoDeVacuo("piloto", "Saiyan", ZonaDoEspaco);
			ServerPlayer vizinho = CorpoDeVacuo("vizinho sem nave", "Saiyan", ZonaDoEspaco);

			Nave pod = PodDeBancada(piloto);

			p.Prova("o servidor concorda que ele esta pilotando (`EstaPilotando`, a funcao de producao)",
					EstaPilotando(piloto.Id), $"nave {pod.Id} tipo {pod.Tipo}");
			p.Prova("...e a nave esta NA MESMA ZONA do espaco que ele (por isso o abrigo precisa existir)",
					Espaco.EhEspaco(pod.Zona) && pod.Zona.Equals(piloto.Zone), pod.Zona.ToString());

			double p0 = MenorNucleo(piloto), v0 = MenorNucleo(vizinho);
			for (int i = 0; i < 3; i++) UmSegundoDeMundo();

			p.Prova("3 s dentro da pod NAO custam vida nenhuma",
					Igual(MenorNucleo(piloto), p0), $"{p0:0.##} -> {MenorNucleo(piloto):0.##}");
			p.Prova("...e o vizinho SEM nave, na mesma zona, perdeu (a pod abriga o piloto, nao a zona)",
					MenorNucleo(vizinho) < v0 - 0.001, $"{v0:0.##} -> {MenorNucleo(vizinho):0.##}");

			// ---- E O DESEMBARQUE: sair da pod volta a doer
			pod.PilotoId = 0;
			double p1 = MenorNucleo(piloto);
			UmSegundoDeMundo();
			p.Prova("DESEMBARCOU: no segundo seguinte ele volta a perder vida",
					MenorNucleo(piloto) < p1 - 0.001, $"{p1:0.##} -> {MenorNucleo(piloto):0.##}");
		},
		Defeitos =
		[
			("o abrigo da pod foi esquecido (ninguem pergunta quem esta pilotando)",
			 s => s.Pilotando = _ => false),

			("o abrigo nunca desliga (todo mundo conta como pilotando)",
			 s => s.Pilotando = _ => true),
		],
	};

	// =====================================================================
	// FAMILIA 4 -- A NAVE-CAPITAL
	// =====================================================================
	/// <summary>
	/// *"ou NAVE CAPITAL SHIP"*.
	///
	/// **E ELA NAO TEM UMA LINHA DE EXCECAO, DE PROPOSITO.** O interior da nave-capital e uma ZONA
	/// PROPRIA (`NaveGrande.ZonaDoInterior` = `ZoneKey.Interior("Nave", id)`), entao `Espaco.EhEspaco`
	/// ja devolve falso e o tique sequer olha pra quem esta la dentro -- inclusive pra quem esta na
	/// PONTE pilotando, que tambem esta dentro. Escrever `|| EstaNaNaveGrande(pl)` seria uma condicao
	/// que nunca muda de valor: verde em toda bancada, e mentindo no dia em que o interior virasse
	/// outra coisa.
	///
	/// Entao o que esta familia prova nao e a excecao -- e a PREMISSA dela. Se a premissa cair (a
	/// pergunta de zona ficar larga), quem esta dentro da nave-capital comeca a sufocar sem que uma
	/// linha do sistema do vacuo tenha mudado. E exatamente esse o defeito injetado.
	/// </summary>
	private FamiliaDoVacuo OAbrigoDaNaveCapital() => new()
	{
		Nome = "FAMILIA 4 -- DENTRO DA NAVE-CAPITAL NAO PERDE",
		Frase = "ou NAVE CAPITAL SHIP",
		Provas = p =>
		{
			ZoneKey dentro = NaveGrande.ZonaDoInterior(IdBaseDaNaveDoVacuo);

			ServerPlayer passageiro = CorpoDeVacuo("passageiro da nave-capital", "Saiyan", dentro);
			ServerPlayer comandante = CorpoDeVacuo("comandante na ponte", "Saiyan", dentro);
			ServerPlayer laFora = CorpoDeVacuo("o que ficou do lado de fora", "Saiyan", ZonaDoEspaco);

			// A nave-capital de papel, com a CASCA no espaco e o comandante pilotando: e a unica
			// configuracao em que "pilotar" e "estar dentro" acontecem ao mesmo tempo.
			Nave grande = NaveGrandeDeBancada(comandante);

			p.Prova("o interior da nave-capital NAO e a zona do espaco (e por isso nao ha excecao escrita)",
					!Espaco.EhEspaco(dentro), dentro.ToString());
			p.Prova("...e a CASCA dela esta no espaco (a nave voa la fora; o dentro e outro lugar)",
					Espaco.EhEspaco(grande.Zona), grande.Zona.ToString());

			double a0 = MenorNucleo(passageiro), c0 = MenorNucleo(comandante), f0 = MenorNucleo(laFora);
			for (int i = 0; i < 3; i++) UmSegundoDeMundo();

			p.Prova("3 s dentro da nave-capital nao custam vida",
					Igual(MenorNucleo(passageiro), a0), $"{a0:0.##} -> {MenorNucleo(passageiro):0.##}");
			p.Prova("...nem pra quem esta PILOTANDO ela da ponte",
					Igual(MenorNucleo(comandante), c0), $"{c0:0.##} -> {MenorNucleo(comandante):0.##}");
			p.Prova("...e quem ficou do lado de fora perdeu (o par que segura as duas linhas de cima)",
					MenorNucleo(laFora) < f0 - 0.001, $"{f0:0.##} -> {MenorNucleo(laFora):0.##}");
		},
		Defeitos =
		[
			("a pergunta de zona ficou larga: 'o que nao e planeta e vacuo' (o interior virou espaco)",
			 s => s.EhEspaco = z => !Espaco.EhPlaneta(z)),

			("`EhEspaco` passou a olhar o TIPO da zona e nao o nome (todo interior virou vacuo)",
			 s => s.EhEspaco = z => z.Kind != ZoneKey.KindPremade),
		],
	};

	// =====================================================================
	// FAMILIA 5 -- O TRAJE
	// =====================================================================
	/// <summary>
	/// *"precisando da ROUPA ESPACIAL"*.
	///
	/// Sao DUAS pecas e nao uma: o `Spacesuit` (`PlanetTech.dm:230`) e o `Rebreather`
	/// (`Tier2.dm:24`), que no DM somam na mesma var `spacesuit`. As duas ja existiam no
	/// `construcoes.json` e ate este trabalho a unica coisa que sabiam fazer era virar movel no chao.
	///
	/// **A PROTECAO E ESTAR COM A ROUPA, E NAO UM BIT DE "EQUIPADO"** -- e isso e o conserto, nao
	/// preguica: um bit de sessao (como o do scouter) nao vai pro disco, e deslogar no vacuo com o
	/// traje vestido seria voltar e morrer em 20 s sem ter feito nada. A mochila E salva.
	///
	/// As tres armadilhas cobertas: a segunda peca (quem lembra do traje esquece do respirador), a
	/// pilha VAZIA (quantidade zero nao e "ter"), e o item qualquer (a maca nao respira por voce).
	/// </summary>
	private FamiliaDoVacuo OAbrigoDoTraje() => new()
	{
		Nome = "FAMILIA 5 -- COM O TRAJE NA MOCHILA NAO PERDE",
		Frase = "precisando da ROUPA ESPACIAL",
		Provas = p =>
		{
			ServerPlayer comTraje = CorpoDeVacuo("com Roupa Espacial", "Saiyan", ZonaDoEspaco);
			ServerPlayer comResp = CorpoDeVacuo("com Respirador", "Saiyan", ZonaDoEspaco);
			ServerPlayer comMaca = CorpoDeVacuo("com uma maca na mochila", "Saiyan", ZonaDoEspaco);
			ServerPlayer pilhaVazia = CorpoDeVacuo("com a pilha VAZIA do traje", "Saiyan", ZonaDoEspaco);

			p.Prova("as duas pecas existem no catalogo de itens (nao sao construcao: nao viram movel)",
					CatalogoDeItens.Get(CatalogoDeItens.Traje) != null
					&& CatalogoDeItens.Get(CatalogoDeItens.Respirador) != null, "");

			comTraje.Mochila.Guardar(CatalogoDeItens.Traje);
			comResp.Mochila.Guardar(CatalogoDeItens.Respirador);
			comMaca.Mochila.Guardar(CatalogoDeItens.Maca);
			pilhaVazia.Mochila.Pilhas.Add(new Pilha(CatalogoDeItens.Traje, 0));

			double t0 = MenorNucleo(comTraje), r0 = MenorNucleo(comResp);
			double m0 = MenorNucleo(comMaca), v0 = MenorNucleo(pilhaVazia);
			for (int i = 0; i < 3; i++) UmSegundoDeMundo();

			p.Prova("3 s de vacuo com a Roupa Espacial na mochila nao custam vida",
					Igual(MenorNucleo(comTraje), t0), $"{t0:0.##} -> {MenorNucleo(comTraje):0.##}");
			p.Prova("...e o RESPIRADOR vale igual (as duas pecas somam na mesma var no DM)",
					Igual(MenorNucleo(comResp), r0), $"{r0:0.##} -> {MenorNucleo(comResp):0.##}");
			p.Prova("...mas uma MACA na mochila nao protege de nada",
					MenorNucleo(comMaca) < m0 - 0.001, $"{m0:0.##} -> {MenorNucleo(comMaca):0.##}");
			p.Prova("...e uma pilha de traje com QUANTIDADE ZERO tambem nao (ter o slot nao e ter a roupa)",
					MenorNucleo(pilhaVazia) < v0 - 0.001, $"{v0:0.##} -> {MenorNucleo(pilhaVazia):0.##}");

			// ---- E TIRAR A ROUPA VOLTA A DOER (o par que segura a primeira linha)
			comTraje.Mochila.Tirar(CatalogoDeItens.Traje);
			double t1 = MenorNucleo(comTraje);
			UmSegundoDeMundo();
			p.Prova("TIROU o traje da mochila: no segundo seguinte volta a perder vida",
					MenorNucleo(comTraje) < t1 - 0.001, $"{t1:0.##} -> {MenorNucleo(comTraje):0.##}");
		},
		Defeitos =
		[
			("ninguem abre a mochila (o abrigo do traje foi esquecido)",
			 s => s.TemTraje = _ => false),

			("so o Spacesuit entrou na conta -- o Rebreather ficou de fora",
			 s => s.TemTraje = pl => pl.Mochila.Quantos(CatalogoDeItens.Traje) > 0),

			("a QUANTIDADE da pilha foi ignorada (uma pilha vazia passou a proteger)",
			 s => s.TemTraje = pl => pl.Mochila.Pilhas.Exists(pi => CatalogoDeItens.ProtegeDoVacuo(pi.Id))),

			("todo mundo conta como vestido (o abrigo nunca desliga)",
			 s => s.TemTraje = _ => true),
		],
	};

	// =====================================================================
	// FAMILIA 6 -- FORA DO VACUO NINGUEM SUFOCA
	// =====================================================================
	/// <summary>
	/// **O CONTRA-EXEMPLO MAIS IMPORTANTE DO ARQUIVO.** Se a pergunta *"estou no espaco?"* estiver
	/// larga demais, todo mundo sufoca em toda parte -- e nenhuma das cinco familias acima notaria,
	/// porque todas elas medem corpos que ESTAO no vacuo.
	///
	/// Sao quatro lugares, e os quatro sao classes diferentes de <see cref="ZoneKey"/>: o planeta
	/// PRE-FEITO (o do host), o planeta GERADO (procedural, com nome != "Espaco"), o INTERIOR (a
	/// Dimensao Mental) e -- o par que segura tudo -- o mesmo corpo movido pro vacuo, que perde.
	/// </summary>
	private FamiliaDoVacuo ForaDoVacuoNinguemSufoca() => new()
	{
		Nome = "FAMILIA 6 -- NO CHAO DE UM PLANETA (E EM QUALQUER LUGAR QUE NAO SEJA O VACUO) NAO PERDE",
		Frase = "o dano e NO ESPACO -- e so no espaco",
		Provas = p =>
		{
			ZoneKey gerado = ZoneKey.Procedural("Planeta de Bancada", 4242);
			ZoneKey interior = ZoneKey.Interior("Interdimension", 7);

			ServerPlayer noChao = CorpoDeVacuo("no chao do planeta do host", "Saiyan", _vacZonaDeChao);
			ServerPlayer noGerado = CorpoDeVacuo("no chao de um planeta GERADO", "Saiyan", gerado);
			ServerPlayer noInterior = CorpoDeVacuo("dentro de um interior", "Saiyan", interior);
			ServerPlayer noVacuo = CorpoDeVacuo("no vacuo (o par que segura)", "Saiyan", ZonaDoEspaco);

			double a0 = MenorNucleo(noChao), b0 = MenorNucleo(noGerado);
			double c0 = MenorNucleo(noInterior), d0 = MenorNucleo(noVacuo);
			for (int i = 0; i < 3; i++) UmSegundoDeMundo();

			p.Prova("no chao do planeta pre-feito: 3 s nao custam nada",
					Igual(MenorNucleo(noChao), a0), $"{a0:0.##} -> {MenorNucleo(noChao):0.##}");
			p.Prova("no chao de um planeta GERADO (mesma classe de zona do espaco!): nao custa nada",
					Igual(MenorNucleo(noGerado), b0), $"{gerado} | {b0:0.##} -> {MenorNucleo(noGerado):0.##}");
			p.Prova("dentro de um interior: nao custa nada",
					Igual(MenorNucleo(noInterior), c0), $"{c0:0.##} -> {MenorNucleo(noInterior):0.##}");
			p.Prova("...e no VACUO, no mesmo tique, o corpo perdeu (senao 'ninguem sufoca' ficaria verde)",
					MenorNucleo(noVacuo) < d0 - 0.001, $"{d0:0.##} -> {MenorNucleo(noVacuo):0.##}");

			// E A REGRA CRUA, sem corpo no meio: o espaco e UMA zona so pro universo inteiro.
			p.Prova("`EhEspaco` diz sim so pra zona do espaco, e nao pro planeta gerado de nome parecido",
					Espaco.EhEspaco(ZonaDoEspaco) && !Espaco.EhEspaco(gerado)
					&& !Espaco.EhEspaco(_vacZonaDeChao) && !Espaco.EhEspaco(interior), "");
		},
		Defeitos =
		[
			("todo planeta GERADO virou vacuo (a pergunta olhou o tipo da zona)",
			 s => s.EhEspaco = z => z.Kind == ZoneKey.KindProcedural),

			("a pergunta ficou larga: 'o que nao e planeta e vacuo'",
			 s => s.EhEspaco = z => !Espaco.EhPlaneta(z)),

			("o mundo inteiro virou vacuo",
			 s => s.EhEspaco = _ => true),
		],
	};

	// =====================================================================
	// FAMILIA 7 -- A CINEMATICA
	// =====================================================================
	/// <summary>
	/// A regra que o calor da estrela ja quebrou nesta casa: **quem esta em cinematica de
	/// transformacao nao pode ser tocado**. O `EspalharDanoG3` recusa `Intocavel` na propria porta,
	/// mas o guarda explicito do `Sufocar` para ANTES do AVISO -- e e por isso que esta familia
	/// afirma DUAS coisas e nao uma: nem dano NEM linha no chat. Um aviso e uma promessa de dano, e
	/// promessa que nao se cumpre le como bug igual.
	///
	/// E a terceira linha e a mais fina de todo o arquivo: **o estado nao e limpo durante a cena**.
	/// Quem ja estava sufocando quando a cinematica comecou volta a sufocar no fim dela SEM uma
	/// segunda mensagem de abertura -- se o `return` apagasse o estado, cada entrada no vacuo pagaria
	/// uma transformacao inteira de graca (e o chat repetiria "o VACUO arranca o ar" pra sempre).
	/// </summary>
	private FamiliaDoVacuo ACinematicaNaoSufoca() => new()
	{
		Nome = "FAMILIA 7 -- QUEM ESTA EM CINEMATICA DE TRANSFORMACAO NAO SUFOCA",
		Frase = "o funil da imunidade vale pro vacuo tambem",
		Provas = p =>
		{
			ServerPlayer pl = CorpoDeVacuo("em transformacao", "Saiyan", ZonaDoEspaco);

			// ---- 1) dois segundos sufocando de verdade (a abertura ja saiu)
			EscutaDeAvisos = [];
			UmSegundoDeMundo();
			UmSegundoDeMundo();
			List<string> abertura = EscutaDeAvisos!;
			EscutaDeAvisos = null;
			double antesDaCena = MenorNucleo(pl);
			p.Prova("antes da cena ele sufocava mesmo (vida caiu e o aviso de abertura saiu)",
					antesDaCena < 100 && abertura.Exists(l => l.Contains("VÁCUO")),
					string.Join(" | ", abertura));

			// ---- 2) a cinematica: nem dano, nem uma linha
			bool emCena = true;
			pl.Combate!.EmCinematica = () => emCena;
			EscutaDeAvisos = [];
			for (int i = 0; i < 6; i++) UmSegundoDeMundo();
			List<string> durante = EscutaDeAvisos!;
			EscutaDeAvisos = null;

			p.Prova("6 s de cinematica no vacuo NAO custam vida nenhuma",
					Igual(MenorNucleo(pl), antesDaCena), $"{antesDaCena:0.##} -> {MenorNucleo(pl):0.##}");
			p.Prova("...e NENHUMA linha de sufocamento foi mandada (aviso e promessa de dano)",
					durante.Count == 0, string.Join(" | ", durante));

			// ---- 3) o fim da cena: o castigo retoma, e SEM uma segunda abertura
			emCena = false;
			pl.Combate.EmCinematica = null;
			EscutaDeAvisos = [];
			UmSegundoDeMundo();
			List<string> depois = EscutaDeAvisos!;
			EscutaDeAvisos = null;

			p.Prova("acabou a cena: o castigo retoma no segundo seguinte",
					MenorNucleo(pl) < antesDaCena - 0.001, $"{antesDaCena:0.##} -> {MenorNucleo(pl):0.##}");
			p.Prova("...e NAO repetiu a abertura ('o VÁCUO arranca o ar') -- o estado nao foi limpo na cena",
					!depois.Exists(l => l.Contains("arranca o ar")), string.Join(" | ", depois));
		},
		Defeitos =
		[
			("o guarda do `Intocavel` saiu do `Sufocar` (o furo que o calor da estrela teve)",
			 s => s.Intocavel = _ => false),

			("o dano do vacuo virou zero (o castigo nao retoma depois da cena)",
			 s => s.Dano = _ => 0),
		],
	};

	// =====================================================================
	// FAMILIA 8 -- O FOLEGO DO CARGO
	// =====================================================================
	/// <summary>
	/// **O `spacebreather` do DM nao e so raca.** O Deus da Destruicao ganha a flag ao assumir o
	/// titulo (`Ranks/GodOfDestruction.dm:118-121`) e a devolve ao perde-lo (`:143`).
	///
	/// Esta familia nasceu ACHANDO UM BURACO: a ficha do cargo neste port ja prometia, por escrito,
	/// *"Nao envelhece, **respira no vacuo**, e carrega o Hakai"* (`Core/Ranks/Ranks.cs:556`) -- e
	/// nenhuma linha de codigo lia isso. O Deus da Destruicao sufocava como qualquer um. E o mesmo
	/// padrao do "escrever o corte nao e aplicar o corte" que este port ja pagou no sigilo do BP.
	///
	/// O par que segura a linha e LARGAR O TRONO: um folego que fica depois do titulo e um folego que
	/// nunca sai, e como o cargo mora na CONTA, largar tem que doer no segundo seguinte.
	/// </summary>
	private FamiliaDoVacuo OFolegoDoCargo() => new()
	{
		Nome = "FAMILIA 8 -- O DEUS DA DESTRUICAO NAO SUFOCA (o folego e do CARGO)",
		Frase = "GodOfDestruction.dm:118-121 -- spacebreather vem com o titulo, e sai com ele",
		Provas = p =>
		{
			const string chave = "godofdestruction";
			const string conta = "bancada_vacuo_deus";

			ServerPlayer deus = CorpoDeVacuo("o Deus da Destruicao", "Saiyan", ZonaDoEspaco, conta: conta);
			ServerPlayer mortal = CorpoDeVacuo("um mortal do mesmo sangue", "Saiyan", ZonaDoEspaco);

			p.Prova("a regra do Core conhece o cargo (`Vacuo.CargoRespira`)",
					Vacuo.CargoRespira(chave) && !Vacuo.CargoRespira("kaioshin") && !Vacuo.CargoRespira(""),
					string.Join(", ", Vacuo.CargosQueRespiram));

			string donoAntes = _tronos.TryGetValue(chave, out string? d) ? d : "";
			try
			{
				_tronos[chave] = conta;
				p.Prova("o servidor concorda que este corpo carrega o titulo (`CargoDe`)",
						CargoDe(deus.Conta) == chave, CargoDe(deus.Conta));

				double g0 = MenorNucleo(deus), m0 = MenorNucleo(mortal);
				for (int i = 0; i < 3; i++) UmSegundoDeMundo();

				p.Prova("3 s de vacuo nao custam vida ao Deus da Destruicao",
						Igual(MenorNucleo(deus), g0), $"{g0:0.##} -> {MenorNucleo(deus):0.##}");
				p.Prova("...e o mortal da MESMA RACA, ao lado, perdeu (e o cargo, nao o sangue)",
						MenorNucleo(mortal) < m0 - 0.001, $"{m0:0.##} -> {MenorNucleo(mortal):0.##}");

				// ---- LARGOU O TRONO: o folego sai junto
				_tronos.Remove(chave);
				double g1 = MenorNucleo(deus);
				UmSegundoDeMundo();
				p.Prova("LARGOU o titulo: no segundo seguinte ele volta a sufocar (`GodOfDestruction.dm:143`)",
						MenorNucleo(deus) < g1 - 0.001, $"{g1:0.##} -> {MenorNucleo(deus):0.##}");
			}
			finally
			{
				// O TRONO DE VERDADE VOLTA COMO ESTAVA -- e sem `SalvarCargos`: esta bancada nunca
				// escreve no `cargos.txt` do dono.
				if (donoAntes.Length > 0) _tronos[chave] = donoAntes; else _tronos.Remove(chave);
			}
		},
		Defeitos =
		[
			("a concessao do cargo foi desligada (o estado ANTERIOR a este trabalho)",
			 s => s.CargoRespira = _ => false),

			("todo cargo passou a dar folego (e o folego nunca sai do corpo)",
			 s => s.CargoRespira = _ => true),
		],
	};

	// =====================================================================
	// FAMILIA 9 -- DA PRA MORRER DISSO, E AVISA ANTES
	// =====================================================================
	/// <summary>
	/// *"da pra morrer disso se ficar tempo demais, e avisa antes"*.
	///
	/// O PRAZO E O DO DM: `spacetime = 100` decrementado a cada 0,2 s = **20 s** (`Stats.dm:120,126`).
	/// La, ao fim, era `to_chat(view(), "[src] suffocates and dies!")` + `spawn Death()` -- morte
	/// instantanea, zero de vida perdida. O dono pediu dano por segundo, entao o prazo virou
	/// ORCAMENTO: o corpo morre na mesma hora, mas visivelmente (sangra, desmaia antes, da tempo de
	/// alguem puxar).
	///
	/// E o aviso e cobrado por CONTEUDO e nao por existencia: a primeira linha tem que dizer AS TRES
	/// SAIDAS. Uma linha que so diz "voce esta sufocando" ensina o jogador a morrer.
	/// </summary>
	private FamiliaDoVacuo DaPraMorrerDisso() => new()
	{
		Nome = "FAMILIA 9 -- DA PRA MORRER DISSO, E O SERVIDOR AVISA ANTES",
		Frase = "morre em 20 s (o prazo do `spacetime`), com nocaute antes e 4 avisos no caminho",
		Provas = p =>
		{
			ServerPlayer pl = CorpoDeVacuo("o que ficou tempo demais", "Saiyan", ZonaDoEspaco);

			EscutaDeAvisos = [];
			int segundos = 0, segundoDoKo = 0;
			double vidaNaAbertura = MenorNucleo(pl);
			while (!pl.Ficha.dead && segundos < SegundosMaximosDaMedicaoDoVacuo)
			{
				UmSegundoDeMundo();
				segundos++;
				if (segundoDoKo == 0 && pl.Ficha.KO) segundoDoKo = segundos;
			}
			List<string> ditos = EscutaDeAvisos!;
			EscutaDeAvisos = null;

			GD.Print($"[vacuo]       MEDIDO: morte em {segundos} s, nocaute em {segundoDoKo} s, {ditos.Count} linhas de chat");
			foreach (string l in ditos) GD.Print($"[vacuo]         \"{l}\"");

			p.Prova($"o corpo MORREU no vacuo (em {segundos} s)", pl.Ficha.dead, $"{segundos} s");
			p.Prova($"...e o prazo e o do DM: {Vacuo.SegundosDeFolego:0} s (`spacetime = 100` x 0,2 s)",
					segundos == (int)Vacuo.SegundosDeFolego, $"{segundos} s");
			p.Prova("...tendo perdido vida no caminho (foi dano, nao morte instantanea como no DM)",
					vidaNaAbertura >= 100 && segundos > 1, $"comecou com {vidaNaAbertura:0.##}");
			p.Prova("...e NOCAUTEOU antes de morrer (da tempo de alguem puxar o corpo)",
					segundoDoKo > 0 && segundoDoKo < segundos, $"nocaute em {segundoDoKo} s de {segundos} s");
			p.Prova("...com a bandeira `dead` levantada pelo funil unico da morte", pl.Ficha.dead, "");
			p.Prova("...e com o prazo de renascer marcado (a mesma morte de qualquer outra)",
					pl.RelogioDaMorte > 0, $"{pl.RelogioDaMorte}");

			p.Prova("o PRIMEIRO aviso sai no instante em que o ar acaba e diz AS TRES SAIDAS",
					ditos.Count > 0 && ditos[0].Contains("Roupa Espacial") && ditos[0].Contains("pod")
					&& ditos[0].Contains("nave"), ditos.Count > 0 ? ditos[0] : "(nenhuma linha)");
			p.Prova($"...e ele repete a cada {SegundosEntreAvisosDeVacuo:0} s: 4 linhas antes da morte",
					ditos.FindAll(l => l.Contains("sufocando") || l.Contains("arranca o ar")).Count == 4,
					$"{ditos.FindAll(l => l.Contains("sufocando") || l.Contains("arranca o ar")).Count} linhas");
			p.Prova("...as repeticoes dizem quanto tempo SOBRA (a conta e do corpo, nao um cronometro)",
					ditos.Exists(l => l.Contains("antes do seu corpo ceder")), "");
			p.Prova("...e a ultima linha e a da morte",
					ditos.Count > 0 && ditos[^1].Contains("sufoca e morre"), ditos.Count > 0 ? ditos[^1] : "");

			// ---- E O ALIVIO: quem PARA de sufocar precisa saber que fez a coisa certa
			ServerPlayer salvo = CorpoDeVacuo("o que vestiu a roupa a tempo", "Saiyan", ZonaDoEspaco);
			for (int i = 0; i < 3; i++) UmSegundoDeMundo();
			double antesDoTraje = MenorNucleo(salvo);

			EscutaDeAvisos = [];
			salvo.Mochila.Guardar(CatalogoDeItens.Traje);
			UmSegundoDeMundo();
			List<string> alivio = EscutaDeAvisos!;
			EscutaDeAvisos = null;

			p.Prova("vestiu a roupa no meio do sufoco: a vida PARA de cair",
					Igual(MenorNucleo(salvo), antesDoTraje), $"{antesDoTraje:0.##} -> {MenorNucleo(salvo):0.##}");
			p.Prova("...e o servidor confirma o alivio ('voce volta a respirar')",
					alivio.Exists(l => l.Contains("volta a respirar")), string.Join(" | ", alivio));
		},
		Defeitos =
		[
			("o dano deixou de ser letal (o vacuo passou a so desmaiar)",
			 s => s.Letal = false),

			("o dano ficou insignificante (ninguem morre mais no teto da medicao)",
			 s => s.Dano = v => v / 2000),

			("todo mundo virou intocavel (nem dano, nem aviso, nem morte)",
			 s => s.Intocavel = _ => true),

			("a regra do vacuo parou de valer pra Saiyajin",
			 s => s.Respira = (_, _, _, _) => true),
		],
	};

	// =====================================================================
	// FAMILIA 10 -- A CORRENTE ESTA LIGADA
	// =====================================================================
	/// <summary>
	/// **A FAMILIA QUE PERGUNTA "QUEM CHAMA".** Este repo ja perdeu meses com sistema inteiro escrito,
	/// certo e ORFAO: 60 verbos concedidos e mudos, a API do sigilo do BP 100% sem chamador, os 35
	/// atlas escritos e nunca importados. Todas as nove familias acima chamam `TickDoVacuo()` na mao
	/// -- ou seja, todas continuariam VERDES com a chamada apagada do tique do servidor.
	///
	/// Ela e estrutural (le o fonte) porque a alternativa -- rodar o `Tick()` inteiro do servidor
	/// dentro da bancada -- mexeria no mundo do dono pra responder uma pergunta de uma linha. E a
	/// CADENCIA entra junto: a chamada tem que estar no bloco de 1 Hz, ao lado do
	/// `TickDoEsmagamento`, porque no tique cheio o castigo seria 30x maior.
	/// </summary>
	private FamiliaDoVacuo ACorrenteEstaLigada() => new()
	{
		Nome = "FAMILIA 10 -- A CORRENTE ESTA LIGADA (quem chama, e em que cadencia)",
		Frase = "sistema certo e orfao continua sendo sistema que nao acontece",
		Provas = p =>
		{
			string caminho = ProjectSettings.GlobalizePath("res://Server/GameServer.cs");
			if (!File.Exists(caminho)) { p.NaoDeu($"o fonte do servidor nao esta no disco ({caminho})"); return; }

			string fonte = (_vacFonteMutante ?? File.ReadAllText)(caminho);

			string? linhaDoTique = null;
			foreach (string linha in fonte.Split('\n'))
				if (linha.Contains("TickDoVacuo();")) { linhaDoTique = linha; break; }

			p.Prova("`TickDoVacuo()` E CHAMADO pelo tique do servidor (nao e um sistema orfao)",
					linhaDoTique != null, "nenhuma chamada no `GameServer.cs`");
			p.Prova("...no bloco de 1 Hz, ao lado do `TickDoEsmagamento` (no tique cheio seria 30x)",
					linhaDoTique?.Contains("TickDoEsmagamento();") == true, Curto(linhaDoTique ?? ""));
			p.Prova("...e a desconexao esquece o relogio do aviso (`EsquecerVacuo`), senao o proximo a "
					+ "herdar o id nasce sufocando",
					fonte.Contains("EsquecerVacuo(pl.Id);"), "");
		},
		Defeitos =
		[
			("a chamada do tique foi apagada (o sistema inteiro virou orfao)",
			 _ => _vacFonteMutante = c => File.ReadAllText(c).Replace("TickDoVacuo();", "")),

			("a chamada mudou pro bloco de 30 Hz (o castigo ficaria 30x maior)",
			 _ => _vacFonteMutante = c => File.ReadAllText(c)
					.Replace("TickDoEsmagamento(); TickDoVacuo();", "TickDoEsmagamento();")
					.Replace("TickDosEmbates();", "TickDosEmbates(); TickDoVacuo();")),

			("o `EsquecerVacuo` da desconexao sumiu",
			 _ => _vacFonteMutante = c => File.ReadAllText(c).Replace("EsquecerVacuo(pl.Id);", "")),
		],
	};

	// =====================================================================
	// FAMILIA 11 -- O BIO-ANDROIDE HERDA O FOLEGO PELO DNA
	// =====================================================================
	/// <summary>
	/// *"bio androides pegam a capacidade de respirar no espaco caso uma das racas q esta em seu dna
	/// consiga (lembrando q as racas q podem respirar no espaco sao majin e frost demon)"*.
	///
	/// ============================ ESTA E A UNICA FAMILIA EM QUE A RACA NAO RESPONDE ============================
	/// Todas as outras perguntam algo do corpo: a raca dele, o cargo dele, a mochila dele, a zona
	/// dele. Esta pergunta o que ele FOI FEITO DE -- e por isso dois corpos identicos, mesma raca,
	/// mesma ParentRace, mesmo BP, mesma zona, tem respostas OPOSTAS. Uma bancada que so soubesse
	/// dizer "bio-androide nao perde vida" ficaria verde com a raca "BioAndroid" cravada na lista do
	/// Core -- que e o conserto errado, e o que o DM faz (`statbiodroid.dm:51`).
	///
	/// Por isso as duas metades andam SEMPRE juntas aqui, e elas sao o mesmo tique:
	///   * o bio de doador que respira **nao perde vida**;
	///   * o bio de quatro doadores humanos **perde**, ao lado dele, no mesmo segundo.
	/// ========================================================================================================
	///
	/// ============================ E A DIVERGENCIA COM O DM E O CENTRO DISTO ============================
	/// No original o folego do bio e da RACA e vem de graca: `statbiodroid.dm:51` poe
	/// `"Space Breath" = 1` no proto, e `dnl_bio_hatch` (`DNALabs.dm:460-465`) semeia o genoma so do
	/// proto -- quatro doadores humanos dao um bio que respira igualzinho. O dono pediu o contrario
	/// disso, e a prova do humano acima e o que impede alguem de "consertar" o port pro lado do DM
	/// sem saber que esta desfazendo um pedido.
	/// ==============================================================================================
	///
	/// ============================ E O PARTO E MEDIDO NA OUTRA BANCADA ============================
	/// Aqui os corpos recebem o bit pela FUNCAO DE PRODUCAO que o parto usa (`PulmaoDaFornada`, sobre
	/// fornadas forjadas) -- e isso mede a derivacao e o castigo, nao a ENTRADA. Quem dirige a cadeia
	/// de verdade (construir -> `colher_dna` -> `gestar` -> nascer, pelos verbos) e a `--bioteste`, e
	/// e la que se prova que o parto escreve o campo. As tres provas estruturais do fim desta familia
	/// existem so pra que as duas bancadas nao possam divergir caladas.
	/// =========================================================================================
	/// </summary>
	private FamiliaDoVacuo OFolegoDoDnaDoBio() => new()
	{
		Nome = "FAMILIA 11 -- O BIO-ANDROIDE RESPIRA SE O DNA DELE RESPIRAR",
		Frase = "bio androides pegam a capacidade de respirar no espaco caso uma das racas q esta em seu dna consiga",
		Provas = p =>
		{
			// ---- 1. A DERIVACAO, sobre fornadas forjadas -----------------------
			// Cada fornada responde uma pergunta diferente, e as duas ultimas sao as negativas.
			Gestacao soMajin = FornadaDeVacuo(("Majin", ""));
			Gestacao soIcer = FornadaDeVacuo((Jandirus.Core.Races.FormasDeFrost.Raca, ""));
			Gestacao soFrost = FornadaDeVacuo((Jandirus.Core.Races.FormasDeFrost.ClasseNormal, ""));
			Gestacao meioMajin = FornadaDeVacuo(("Human", "Majin"));
			Gestacao umEmQuatro = FornadaDeVacuo(("Human", ""), ("Saiyan", ""), ("Majin", ""), ("Namekian", ""));
			Gestacao ninguem = FornadaDeVacuo(("Human", ""), ("Saiyan", ""), ("Namekian", ""), ("Human", ""));

			void Deriva(string oQue, Gestacao g, bool esperado)
			{
				Amostra? pulmao = PulmaoDaFornada(g);
				p.Prova(oQue, (pulmao != null) == esperado,
						pulmao == null ? "nenhum doador respira"
									   : $"pelo DNA de {pulmao.Doador} ({SangueDe(pulmao)})");
			}

			Deriva("uma fornada com um MAJIN respira", soMajin, true);
			Deriva("...com um Frost Demon na grafia do PROTO ('Icer') tambem", soIcer, true);
			Deriva("...e na grafia que circula ('Frost Demon') tambem -- as duas, senao metade dos "
				   + "doadores Frost entra cega", soFrost, true);
			Deriva("...com um doador MEIO-MAJIN (Race 'Human', pai Majin) tambem -- e por isso a "
				   + "agulha guarda a raca do PAI", meioMajin, true);
			Deriva("...e UM doador em quatro ja basta (o pedido e um OU, como o `brew_has_saiyan`)",
				   umEmQuatro, true);
			Deriva("quatro doadores que NAO respiram nao dao folego nenhum (a metade que segura "
				   + "todas as linhas de cima)", ninguem, false);

			p.Prova("a raca 'BioAndroid' NAO esta na lista do Core -- o folego e do DNA e nao do cracha "
					+ "(cravar a raca la seria o conserto errado, e e o que o DM faz)",
					!Vacuo.RacaRespira(Jandirus.Core.Races.BioAndroids.Raca)
					&& !Vacuo.RacaRespira(Jandirus.Core.Races.BioAndroids.RacaDoDm),
					string.Join(", ", Vacuo.RacasQueRespiram));

			// ---- 2. E O CASTIGO, pelo tique de producao ------------------------
			ServerPlayer bioMajin = BioDeVacuo("bio de doador MAJIN", soMajin);
			ServerPlayer bioFrost = BioDeVacuo("bio de doador Frost Demon", soFrost);
			// A GRAFIA DO PROTO ('Icer') TEM QUE PERDER VIDA-NENHUMA TAMBEM, e nao so derivar: e ELA
			// que um doador de verdade carrega no `Ficha.Race` (`FormasDeFrost.Raca`), porque
			// "Frost Demon" e o nome da CLASSE. Sem esta linha, a metade das duas racas que o dono
			// nomeou que mais aparece em jogo so tinha prova de INTENCAO (o `PulmaoDaFornada` viu),
			// e nenhuma de RESULTADO (o corpo nao sangrou).
			ServerPlayer bioIcer = BioDeVacuo("bio de doador Frost Demon na grafia do proto ('Icer')", soIcer);
			ServerPlayer bioMeio = BioDeVacuo("bio de doador meio-Majin", meioMajin);
			ServerPlayer bioMisto = BioDeVacuo("bio de 4 doadores, UM deles Majin", umEmQuatro);
			ServerPlayer bioHumano = BioDeVacuo("bio de 4 doadores que nao respiram", ninguem);

			var antes = new Dictionary<int, double>();
			foreach (ServerPlayer pl in _corposDoVacuo) antes[pl.Id] = MenorNucleo(pl);

			UmSegundoDeMundo();

			void NaoPerdeu(ServerPlayer pl) =>
				p.Prova($"{pl.Name} NAO perde vida no vacuo",
						Igual(MenorNucleo(pl), antes[pl.Id]), $"{antes[pl.Id]:0.##} -> {MenorNucleo(pl):0.##}");

			NaoPerdeu(bioMajin);
			NaoPerdeu(bioFrost);
			NaoPerdeu(bioIcer);
			NaoPerdeu(bioMeio);
			NaoPerdeu(bioMisto);

			p.Prova("...e o bio de doadores que nao respiram PERDE, no mesmo segundo, ao lado deles -- "
					+ "mesma raca, mesma ParentRace, mesma zona, DNA diferente (no DM este respiraria: "
					+ "`statbiodroid.dm:51`)",
					MenorNucleo(bioHumano) < antes[bioHumano.Id] - 0.001,
					$"{antes[bioHumano.Id]:0.##} -> {MenorNucleo(bioHumano):0.##}");

			// ---- 3. E O PARTO ESCREVE ISSO? (o cruzamento com a `--bioteste`) --
			string caminho = ProjectSettings.GlobalizePath("res://Server/GameServer.Tech.cs");
			if (!File.Exists(caminho)) { p.NaoDeu($"o fonte da tecnologia nao esta no disco ({caminho})"); return; }
			string fonte = (_vacFonteMutante ?? File.ReadAllText)(caminho);

			p.Prova("o PARTO grava o folego herdado na ficha (`bio_dna_respira`) -- a fornada e "
					+ "destruida no nascimento, entao ou e agora ou a informacao some",
					fonte.Contains("pl.Ficha.bio_dna_respira = pulmao != null;"), "");
			p.Prova("...e a derivacao pergunta ao CORE, doador por doador (`Vacuo.RespiraNoVacuo`), em "
					+ "vez de escrever a lista de racas pela segunda vez",
					fonte.Contains("Vacuo.RespiraNoVacuo(a.Raca, a.RacaDoPai)"), "");
			p.Prova("...e a agulha guarda a raca do PAI do doador (senao o meio-sangue entraria cego "
					+ "no tanque, e a prova do meio-Majin la em cima seria vacua)",
					fonte.Contains("RacaDoPai = vitima.Ficha.ParentRace"), "");
		},
		Defeitos =
		[
			("o bio parou de herdar o folego (o estado ANTERIOR a este trabalho: todo bio sufocava)",
			 s => s.FolegoDoDna = _ => false),

			("todo bio-androide passou a respirar, com ou sem DNA (o DM 1:1 -- que NAO e o pedido)",
			 s => s.FolegoDoDna = _ => true),

			("o folego concedido parou de chegar ao funil (a linha do `||` em `SufocaAgora`)",
			 s => s.Respira = (raca, pai, _, traje) => Vacuo.RespiraNoVacuo(raca, pai, false, traje)),

			("o parto parou de gravar o campo (o bit escrito em lugar nenhum)",
			 _ => _vacFonteMutante = c => File.ReadAllText(c)
					.Replace("pl.Ficha.bio_dna_respira = pulmao != null;", "")),

			("a derivacao virou uma SEGUNDA lista de raca, cravada no parto",
			 _ => _vacFonteMutante = c => File.ReadAllText(c)
					.Replace("Vacuo.RespiraNoVacuo(a.Raca, a.RacaDoPai)", "a.Raca == \"Majin\"")),

			("a agulha parou de guardar a raca do pai (meio-sangue entra cego no tanque)",
			 _ => _vacFonteMutante = c => File.ReadAllText(c)
					.Replace("RacaDoPai = vitima.Ficha.ParentRace", "RacaDoPai = \"\"")),
		],
	};

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// UM SEGUNDO DE MUNDO, na ordem e na cadencia do servidor de verdade: 30 tiques de regeneracao
	/// passiva (com a ficha andando a 5 Hz) e UM tique de vacuo.
	///
	/// **A CURA ENTRA DE PROPOSITO.** Ela e o que faz a familia 1 medir o jogo e nao uma conta: se o
	/// `EmCombate > 0` da `RegenerarPassivo` um dia sair, a taxa medida cai de 5,00 pra 3,33 e a
	/// bancada reprova -- e essa e exatamente a diferenca entre este sistema e o do sol, que precisou
	/// de um piso justamente porque a coroa nao poe ninguem em combate.
	///
	/// E O TIQUE DO VACUO E O DE PRODUCAO (`TickDoVacuo`), varrendo `_players` inteiro: chamar
	/// `Sufocar(pl)` na mao provaria que o metodo existe, e nao que o laco alcanca o corpo.
	/// </summary>
	private void UmSegundoDeMundo()
	{
		for (int t = 0; t < TicksPorSegundo; t++)
			foreach (ServerPlayer pl in _corposDoVacuo.ToList())
			{
				if (t % TicksPorFicha == 0) pl.Ficha.Tick(agoraMs: NowMs());
				RegenerarPassivo(pl, Protocol.TickSeconds);
			}

		TickDoVacuo();
	}

	/// <summary>
	/// A VIDA DO NUCLEO MAIS FERIDO -- e nao <see cref="Body.Vida"/>.
	///
	/// A media do corpo inclui os ANINHADOS (cerebro, orgaos, maos, pes), que recebem so a fracao de
	/// propagacao do golpe: medir por ela daria uma taxa "de 4,3" que nao e a taxa de nada. O nucleo
	/// e o que decide a morte (`Body.DeveMorrer`), entao e ele que a bancada olha.
	/// </summary>
	private static double MenorNucleo(ServerPlayer pl)
	{
		double menor = double.PositiveInfinity;
		foreach (BodyPart b in pl.Combate!.Corpo.Partes)
			if (b.Papel == Vitalidade.Nucleo && !b.Decepado) menor = Math.Min(menor, b.Vida);
		return double.IsInfinity(menor) ? 0 : menor;
	}

	private static bool Igual(double a, double b) => Math.Abs(a - b) < 0.0005;

	private static bool EhUmaDestas(string? valor, params string[] lista)
	{
		if (string.IsNullOrEmpty(valor)) return false;
		foreach (string s in lista)
			if (string.Equals(s, valor, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	/// <summary>Um ponto qualquer do vacuo, longe da origem e das estrelas ancoradas.</summary>
	private static Vec2 PontoDoVacuo() => new(1_500_000, 1_500_000);

	/// <summary>
	/// UM CORPO DE BANCADA. Mesmo padrao do `--solteste`: entra no `_players` e na `ZoneList` pelo
	/// `PorNoMundo` de producao (porque o `TickDoVacuo` varre `_players`), e sai no
	/// <see cref="LimparAOficinaDoVacuo"/>.
	///
	/// `Peer = null`, ou seja **ele e um NPC do mundo pra todos os efeitos** -- e isso responde de
	/// graca a pergunta do dono sobre NPC: se este laco os alcanca, eles sufocam.
	/// </summary>
	private ServerPlayer CorpoDeVacuo(string rotulo, string raca, ZoneKey zona,
									  string racaDoPai = "", double bp = 100_000, string conta = "")
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDoVacuoDeTeste + (++_vacProximoId),
			Peer = null,
			Name = rotulo,
			Race = raca,
			Genero = "Male",
			Idade = 25,
			Zone = zona,
			Pos = PontoDoVacuo(),
			Conta = conta,
			Slot = 0,
			Ficha = new Fighter { Race = raca, ParentRace = racaDoPai, BP = bp },
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.ChunkAtual = ChunkId.De(novo.Pos);

		// `PorNoMundo` chama `Statify` (os ATRIBUTOS) e nao `PowerLevel` (o PODER) -- ver a mesma
		// linha na bancada do sol. Aqui o BP nao entra na conta do dano, mas entra na do nocaute e na
		// da regeneracao, e um `expressedBP` zero mediria outro corpo.
		novo.Ficha.Tick(agoraMs: NowMs());

		_corposDoVacuo.Add(novo);
		return novo;
	}

	/// <summary>
	/// UMA FORNADA DE PAPEL -- so os doadores, que e a unica coisa que a familia 11 pergunta.
	///
	/// Ela nao passa pelo `colher_dna` de proposito, e pelo mesmo motivo da <see cref="PodDeBancada"/>:
	/// o que esta familia mede e o FOLEGO, e quem dirige a agulha de verdade e a `--bioteste`. Uma
	/// fornada que dependesse do preco de um laboratorio faria esta bancada reprovar quando quem
	/// quebrasse fosse a aba Tech.
	/// </summary>
	private static Gestacao FornadaDeVacuo(params (string Raca, string Pai)[] doadores)
	{
		var g = new Gestacao();
		int n = 0;
		foreach ((string raca, string pai) in doadores)
			g.Amostras.Add(new Amostra
			{
				Raca = raca,
				RacaDoPai = pai,
				Doador = $"doador {++n}",
				Assinatura = $"bancada-vacuo-{n}",
				Bp = 1000,
			});
		return g;
	}

	/// <summary>
	/// UM BIO-ANDROIDE DE BANCADA, no vacuo, com o folego que ESTA fornada lhe daria.
	///
	/// ============================ O CORPO E O QUE O PARTO DEIXA, E ISSO IMPORTA ============================
	/// `Race` E `ParentRace` valem as duas "BioAndroid" porque e isso que `NascerBioAndroide` escreve
	/// -- e e justamente por isso que o bio nao tinha como respirar por raca nenhuma antes deste
	/// trabalho: nao ha mais nenhum vestigio do doador no corpo, so o bit.
	///
	/// **O BIT SAI DA FUNCAO DE PRODUCAO** (`PulmaoDaFornada`, a mesma que o parto chama) e nao de um
	/// `true` escrito a mao: um `bio.Ficha.bio_dna_respira = true` aqui mediria o castigo e nao a
	/// heranca, e ficaria verde com a derivacao inteira quebrada.
	/// ===================================================================================================
	/// </summary>
	private ServerPlayer BioDeVacuo(string rotulo, Gestacao g)
	{
		ServerPlayer bio = CorpoDeVacuo(rotulo, Jandirus.Core.Races.BioAndroids.Raca, ZonaDoEspaco,
										racaDoPai: Jandirus.Core.Races.BioAndroids.Raca);
		bio.Ficha.bio_lab_born = true;
		bio.Ficha.bio_stage = Jandirus.Core.Races.BioAndroids.Larva;
		bio.Ficha.bio_dna_respira = PulmaoDaFornada(g) != null;
		return bio;
	}

	/// <summary>
	/// UMA POD DE PAPEL com este corpo no comando. Ela nao passa pelo caminho de compra/assentar/
	/// embarcar de proposito: o que esta familia mede e o ABRIGO, e o abrigo pergunta uma coisa so
	/// (`EstaPilotando`, a funcao de producao). Fabricar a nave pela aba Tech faria a bancada do
	/// vacuo reprovar quando quem quebrasse fosse o preco de uma construcao.
	/// </summary>
	private Nave PodDeBancada(ServerPlayer piloto)
	{
		var pod = new Nave
		{
			Id = IdBaseDaNaveDoVacuo + _navesDoVacuo.Count + 1,
			Tipo = "Spacepod",
			PilotoId = piloto.Id,
			DonoConta = piloto.Conta,
			DonoNome = piloto.Name,
		};
		pod.PorZona(piloto.Zone);
		_naves.Add(pod);
		_navesDoVacuo.Add(pod);
		return pod;
	}

	/// <summary>A nave-capital de papel: casca NO ESPACO, comandante pilotando de DENTRO.</summary>
	private Nave NaveGrandeDeBancada(ServerPlayer comandante)
	{
		var grande = new Nave
		{
			Id = IdBaseDaNaveDoVacuo,
			Tipo = NaveGrande.Tipo,
			PilotoId = comandante.Id,
			DonoConta = comandante.Conta,
			DonoNome = comandante.Name,
		};
		grande.PorZona(ZonaDoEspaco);
		_naves.Add(grande);
		_navesDoVacuo.Add(grande);
		return grande;
	}

	/// <summary>
	/// DESMONTA A OFICINA: os corpos saem do mundo, as naves de papel saem da lista e os relogios de
	/// aviso sao esquecidos pelo `EsquecerVacuo` de producao.
	///
	/// **O `EsquecerVacuo` importa aqui e nao e enfeite**: `_sufocando` e indexado por id, e um id de
	/// bancada deixado para tras ficaria no dicionario pra sempre (o laco so visita quem esta em
	/// `_players`). E o mesmo vazamento que a desconexao de um jogador de verdade evita.
	/// </summary>
	private void LimparAOficinaDoVacuo()
	{
		foreach (ServerPlayer pl in _corposDoVacuo)
		{
			_players.Remove(pl.Id);
			ZoneList(pl.Zone.Hash).Remove(pl);
			EsquecerVacuo(pl.Id);
		}
		_corposDoVacuo.Clear();

		foreach (Nave n in _navesDoVacuo) _naves.Remove(n);
		_navesDoVacuo.Clear();
	}
}
