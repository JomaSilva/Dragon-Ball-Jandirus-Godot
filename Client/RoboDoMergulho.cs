using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ O MERGULHO INTEIRO, PELO GESTO DO JOGADOR (`--diagmergulho`) ============================
/// Os quatro pedidos do dono desta rodada sao um caminho so -- apertar M, escolher, ver a tela ondular,
/// acordar num lugar sem beirada e voltar -- e nenhuma das bancadas que existem atravessa esse caminho:
///
///   * `--presoteste` mede a PLANTA (funcao pura: a chapa, o branco, o `SemBorda`, a coleira). Ela e
///     verde num jogo em que ninguem consegue meditar;
///   * `--menteviva` mede a PORTA com dois clientes, mas ela ATRAVESSA a onda de proposito
///     (`AdiantarAOndaDaMenteNoTeste`) -- ou seja, e a unica bancada que por desenho nao pode dizer
///     nada sobre a espera;
///   * `--diaggota` mede o SHADER numa cena de laboratorio: ela chama `Cair()` na mao, sem rede, sem
///     mente e sem servidor. Ela prova que o pano ondula, nunca que o JOGO o acende na hora certa;
///   * `--fotodamente` fotografa o branco, e para na foto.
///
/// Esta aqui e o meio que faltava: **um cliente de verdade, com `--host`, apertando a tecla**.
///
/// ============================ AS OITO FAMILIAS, E COMO CADA UMA REPROVA (medido) ============================
///   #  o que ela afirma                                    | defeito injetado -> o que ele MEDIU
///   ---|-------------------------------------------------- |------------------------------------
///   1  a tecla M abre a TELINHA; o verb sumiu do menu P     | (a) o verb apagado, registrado de volta
///                                                           | (b) o mundo SEM a telinha montada (o M medita direto)
///   2  "Meditar normal" medita e NAO viaja                  | o botao normal ligado no caminho da profunda
///   3  a ida ONDULA, e a viagem so acontece no FIM da onda  | `DefeitoMergulhoSemOnda`: viagem em 17 ms em vez
///                                                           | de 1534, e 0% de pixel movido contra 29,7%
///   4  a volta por VITORIA ondula                           | `DefeitoSaidaSecaDaMente`: 19 ms, zero onda
///   5  o soco no corpo real NAO ondula, e e SECO            | `DefeitoSocoQueOndula`: 187 quadros de onda e a
///                                                           | volta atrasada pra 1845 ms
///   6  a mente nao tem borda: 500 tiles sem esbarrar        | `Colisao.SemBorda = false`: para na celula 99
///   7  o reflexo nao some pra sempre (a coleira)            | a linha da coleira comentada: ele fica a 70 tiles
///                                                           | e nao volta (as TRES provas vermelhas)
///   8  o pedaco descarrega e a conta nao cresce             | `FolgaDeDescarte` 64 -> 4000: 20 pedacos vivos
///                                                           | no fim contra 6, e zero "soltei" no log
///
/// As duas ultimas nao tem gatilho de bancada porque o que elas medem sao um `const` e um `if` dentro
/// do laco de producao -- injeta-las por chave exigiria um `if (modoDeTeste)` no caminho do jogo, que e
/// exatamente o que este port recusa. Elas se injetam no FONTE, e a chave `--mergulhofamilia N` existe
/// pra isso: ela roda uma familia so, e uma rodada de defeito custa meio minuto em vez de quatro.
///
/// ============================ E AS DUAS INJECOES DE FONTE ACHARAM DEFEITO NA PROPRIA BANCADA ============================
/// As duas ficaram VERDES na primeira tentativa, com o defeito dentro, e as duas por medir a coisa
/// errada -- que e o cego registrado desta casa:
///
///   * a 7 contava "o reflexo se moveu mais de 3 tiles entre duas amostras", e um corpo correndo a 7
///     tiles/s faz isso sozinho. So a TRANSICAO (longe numa amostra, colado na seguinte) e coisa que
///     unicamente a coleira produz;
///   * a 8 lia `GetNodeOrNull("DimensaoMental")` e caia num node CONGELADO do cache de zona (ver
///     `World.PedacosVivosDeTeste`). Ela devolvia 6 com o descarte desligado e 6 com ele ligado.
///
/// Nenhuma das duas teria sido descoberta pela rodada boa: as duas passavam.
/// ====================================================================================================================
///
/// ============================ POR QUE ELA MEDE, E NAO SO FOTOGRAFA ============================
/// Porque tres das oito afirmacoes sao sobre ORDEM ("a onda vem ANTES", "a viagem so DEPOIS", "o soco e
/// SECO") e ordem nao cabe num quadro -- e a quarta ("a tela ondulou") e a que este projeto ja provou
/// saber passar verde de graca: `SetShaderParameter` devolve void com o shader inteiro sem compilar.
///
/// Entao cada transicao passa pelo MESMO observador (<see cref="Medir"/>), que roda quadro a quadro e
/// anota tres relogios: quando a tela comecou a ondular, quando a ZONA mudou (o servidor e quem
/// responde), e quanto a foto do meio da onda difere da foto da tela parada. O veredito e a relacao
/// entre eles, e nunca um deles sozinho.
///
/// **O CONTROLE E O QUADRO PARADO, e ele e tirado ao lado da medida** -- nunca no comeco da rodada: o
/// mesmo argumento dos blocos alternados da `--diaggota`. E ha um piso de RUIDO medido (dois quadros
/// parados seguidos), porque a tela deste jogo nunca esta completamente quieta: ha aura, animacao de
/// respiro e HUD contando. Sem esse piso, "a tela mudou" seria verdade sempre.
/// ==========================================================================================
///
/// COMO RODAR (precisa de JANELA -- no headless o `GetImage` volta vazio e as familias de pixel dizem
/// que nao mediram, em vez de passar de graca):
///
///     Godot --path . --host --rede 7914 --diagmergulho --raca Human \
///           --conta bancada_mergulho --nome Mergulho --resolution 1280x720 --position 1920,0
///
/// (o `ver-o-mergulho.bat` sobe isso, e roda tambem a rodada de defeito das familias 7 e 8.)
/// </summary>
public partial class RoboDoMergulho : Node2D
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>SO ESTA FAMILIA (`--mergulhofamilia N`), ou 0 pra rodar todas. Ver o cabecalho.</summary>
	public int SoAFamilia { get; set; }

	/// <summary>
	/// Quantos tiles a familia 6 anda pra fora. QUINHENTOS e o numero do pedido -- e ele importa: a
	/// chapa tem 100 de lado e um pedaco tem 64, entao a 500 a camera esta a sete pedacos de qualquer
	/// coisa que exista como DADO. Andar 60 mediria a costura; andar 500 mede o infinito.
	/// </summary>
	private const int TilesDeFuga = 500;

	/// <summary>
	/// Quantos tiles a familia 7 corre COM o reflexo atras. Ver a familia: a coleira dispara a cada 40
	/// tiles de vantagem, entao 150 ja produzem varias disparadas -- e cada tile aqui e disputado a
	/// soco, entao alongar isto so soma minutos de briga a uma medida que ja fechou.
	/// </summary>
	private const int TilesDaCorridaComOReflexo = 150;

	/// <summary>
	/// O BP que a bancada empurra, e ele e sobre TEMPO e nao sobre poder: velocidade sai do `Espeed`
	/// (`MoveRules.SpeedStatFrom`), e no BP de um personagem recem-criado 500 tiles sao varios minutos
	/// de caminhada. O reflexo copia a mesma ficha, entao a corrida continua sendo entre iguais -- que
	/// e justamente a premissa da familia 7.
	/// </summary>
	private const double BpDaBancada = 5_000_000;

	/// <summary>
	/// O TETO DE PEDACOS VIVOS. A tela mais larga que o jogo permite com o zoom minimo cabe ~4x3
	/// pedacos, e a folga de descarte (64 tiles) poe mais um anel em volta: ~25 no pior caso. Quarenta
	/// e folgado o bastante pra nao reprovar por causa de resolucao, e apertado o bastante pra ficar
	/// vermelho no instante em que o descarte parar de disparar (a caminhada cruza 8 colunas de pedaco).
	/// </summary>
	private const int TetoDePedacos = 40;

	// =====================================================================
	// PLACAR
	// =====================================================================
	private readonly List<string> _linhas = [];
	private int _ok, _falhou, _semMedida;

	private void Afirmar(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _ok++; _linhas.Add($"  OK     {oque}"); GD.Print($"[mergulho]   OK    {oque}"); return; }
		_falhou++;
		_linhas.Add($"  FALHA  {oque}   {detalhe}");
		GD.PrintErr($"[mergulho]   FALHA {oque}   {detalhe}");
	}

	/// <summary>
	/// A FAMILIA NAO MEDIU -- e isto NAO e um "ok". Toda familia de pixel desta bancada passa por aqui
	/// quando nao ha quadro (headless), e o placar conta separado: uma bancada que se desliga sozinha
	/// e uma bancada verde que nao mediu nada, e este projeto tem registro de quatro defeitos visuais
	/// que sobreviveram exatamente assim.
	/// </summary>
	private void NaoMediu(string oque, string porque)
	{
		_semMedida++;
		_linhas.Add($"  ?????  {oque}   ({porque})");
		GD.PrintErr($"[mergulho]   ????? {oque}   ({porque})");
	}

	private void Nota(string t) { _linhas.Add($"   --    {t}"); GD.Print($"[mergulho]    --   {t}"); }

	// =====================================================================
	// RELOGIO E ESPERA
	// =====================================================================
	private double _relogio;
	private bool _rodando, _fechou;

	public override void _Process(double delta)
	{
		_relogio += delta;
		if (_rodando || _fechou) return;
		if (C is not { Connected: true }) return;
		if (World.Instancia == null) return;
		if (_relogio < 4.0) return;   // o mundo assenta: o primeiro quadro ainda monta pedaco

		_rodando = true;
		_ = Rodar();
	}

	private async Task Quadros(int n)
	{
		for (int i = 0; i < n && !_fechou; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Task Esperar(double s)
	{
		double ate = _relogio + s;
		while (_relogio < ate && !_fechou)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Task Rodar()
	{
		GD.Print("[mergulho] ============ O MERGULHO INTEIRO, PELO GESTO DO JOGADOR ============");

		if (S == null)
		{
			Afirmar("o servidor esta neste processo (rode com `--host`)", false,
					"sem `--host` nao ha como perguntar a AUTORIDADE nada");
			Fechar();
			return;
		}

		int eu = C?.LocalId ?? 0;
		float passo = S.PoderNoTesteDoMergulho(eu, BpDaBancada);
		await Esperar(0.5);
		Nota($"corpo #{eu} na zona `{C?.Zone.Name}` com BP {BpDaBancada:N0} -- velocidade {passo:0.00}x "
			 + $"({MoveRules.SpeedPx(passo) / ZoneCollision.TileSize:0.0} tiles/s andando, "
			 + $"{MoveRules.SpeedPx(passo, true) / ZoneCollision.TileSize:0.0} correndo)");

		// ============================ COMECAR FORA DA MENTE, E ISSO NAO E ZELO A TOA ============================
		// A primeira rodada desta bancada LOGOU DENTRO da mente. O `Drop` (a saida limpa) devolve o corpo
		// antes de gravar -- *"o save gravaria a zona da mente [...] um bolso de uma pessoa so, sem clone,
		// sem porta e sem ninguem"* --, mas o SALVAMENTO PERIODICO (`GameServer.cs`, a cada dois minutos)
		// nao passa por aquela linha: quem estiver em transe quando ele cair fica gravado la dentro, e uma
		// queda do processo (ou um `taskkill` de bancada) o deixa assim.
		//
		// Isso e uma nota pra quem for arrumar aquilo, e nao assunto desta rodada -- aqui a bancada so
		// nao pode COMECAR onde deveria terminar.
		// ====================================================================================================
		if (S.NaMenteNoTeste(eu))
		{
			Nota("ATENCAO: este personagem LOGOU DENTRO DA MENTE (o salvamento periodico grava a zona "
				 + "do transe -- ver o comentario aqui). A bancada sai dela antes de comecar.");
			await SairDaMenteSeEstiver();
		}

		await MedirORuidoDoQuadroParado();

		try
		{
			if (Roda(1)) await ATelinha();
			if (Roda(2)) await ONormalMeditaNormal();
			if (Roda(3)) await AIdaOndula();
			if (Roda(4)) await AVoltaPorVitoriaOndula();
			if (Roda(5)) await OSocoNoCorpoRealESeco();
			if (Roda(6) || Roda(7) || Roda(8)) await AFugaDeQuinhentosTiles();
		}
		catch (Exception e)
		{
			Afirmar($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			S.LimparOMergulhoNoTeste(eu);
		}

		Fechar();
	}

	private bool Roda(int familia) => SoAFamilia == 0 || SoAFamilia == familia;

	// =====================================================================
	// 1. A TELINHA -- e o verb que saiu do menu P
	// =====================================================================
	/// <summary>
	/// *"N faca a meditacao profunda ser um VERB DO MENU DO P. faca q ao MEDITAR uma TELINHA vai abrir
	/// e perguntar se vc quer so MEDITAR NORMAL ou ir pra MEDITACAO PROFUNDA"*.
	///
	/// ============================ A TECLA E APERTADA DE VERDADE ============================
	/// `Input.ParseInputEvent` injeta um evento no motor, processado ANTES dos `_Process` do quadro --
	/// e o `LocalPlayer` le a tecla com `IsActionJustPressed`. Chamar o metodo por dentro pularia
	/// justamente a ligacao tecla -> pergunta, que e a metade do pedido do dono que pode faltar.
	/// (`Input.ActionPress` marcaria a acao no quadro em que ESTE `_Process` roda, e o do corpo pode
	/// ja ter rodado -- e a armadilha que a `--diagcolada` registrou.)
	/// ==================================================================================
	///
	/// AS DUAS INJECOES SAO DE NATUREZAS DIFERENTES de proposito: a (a) devolve o verb ao menu P (o
	/// estado que o dono mandou desfazer) e a (b) tira a telinha do mundo (o estado de antes DELA, e
	/// tambem o caso real do mundo ainda subindo -- ali o M tem que meditar direto, e nao ficar mudo).
	/// </summary>
	private async Task ATelinha()
	{
		GD.Print("[mergulho] --- 1. a tecla M abre a telinha (e o verb sumiu do menu P) ---");

		// ---- o menu P, primeiro: e uma pergunta ao registro de verbs, nao a um desenho.
		bool SemOVerbNoMenu() =>
			Verbos.PorChave("hab:mente") == null
			&& !Verbos.Todos.Any(v => v.Nome.Contains("profunda", StringComparison.OrdinalIgnoreCase));

		Afirmar("o verb \"Meditação profunda\" NAO esta no menu P (nem por nome, nem pela chave `hab:mente`)",
				SemOVerbNoMenu());
		Afirmar("...e o \"Sair da mente\" CONTINUA la (a saida de quem ja esta dentro nao virou telinha)",
				Verbos.PorChave("hab:sairdamente") != null);

		// DEFEITO (a): o verb apagado, registrado de volta -- palavra por palavra o que o
		// `Habilidades.Montar` tinha antes desta rodada.
		Verbos.Registrar(new Verbo(
			"Meditação profunda",
			Verbos.Aprendizado,
			"Mergulhe na propria mente pra lutar contra uma copia exata de voce.",
			() => C?.SendHabilidade("mente")) { Chave = "hab:mente" });
		Afirmar("[defeito a] com o verb de volta no menu P, a prova acima REPROVA", !SemOVerbNoMenu());
		Verbos.Esquecer("Meditação profunda");
		Afirmar("...e o defeito foi desfeito (o menu voltou ao que era)", SemOVerbNoMenu());

		// ---- e agora a tecla.
		if (TelaDeMeditacao.Instancia is not { } tela)
		{
			Afirmar("a telinha esta montada na arvore (`Boot.AoEntrarNoMundo`)", false);
			return;
		}

		await ApertarM();
		Afirmar("apertar M ABRE a telinha (e nao medita nada ainda)",
				tela.NaTela && Atividade() != Protocol.Activity.Meditando,
				$"natela={tela.NaTela} atividade={Atividade()}");

		List<string> botoes = tela.BotoesDesenhados();
		Afirmar("...com os DOIS caminhos desenhados, e os dois acesos",
				botoes.Count == 2 && botoes.All(b => !b.Contains("(apagado)")),
				string.Join(" | ", botoes));
		Nota($"botoes na tela: {string.Join(" | ", botoes)}"
			 + (tela.MotivoNaTela.Length > 0 ? $" | aviso: \"{tela.MotivoNaTela}\"" : ""));

		Fotografar("mergulho-1-a-telinha", "1 -- a telinha aberta pela tecla M");

		// ---- o M de novo FECHA, e nao medita (a toc-toc do menu da tecla E).
		await ApertarM();
		Afirmar("apertar M de novo FECHA sem meditar (cancelar nao medita nada)",
				!tela.NaTela && Atividade() != Protocol.Activity.Meditando,
				$"natela={tela.NaTela} atividade={Atividade()}");

		// DEFEITO (b): o mundo SEM a telinha montada. O `LocalPlayer` cai no ramo de tras (medita
		// direto) -- que e o comportamento correto pra esse mundo, e o ERRADO pra este.
		Node? pai = tela.GetParent();
		pai?.RemoveChild(tela);
		tela.QueueFree();
		await Quadros(2);

		await ApertarM();
		Afirmar("[defeito b] sem a telinha montada, o M medita DIRETO -- e a prova de cima REPROVA",
				TelaDeMeditacao.Instancia == null && Atividade() == Protocol.Activity.Meditando,
				$"instancia={(TelaDeMeditacao.Instancia == null ? "nula" : "viva")} atividade={Atividade()}");

		// e desfaz: para de meditar e remonta a telinha, exatamente como o `Boot` a monta.
		await ApertarM();
		var nova = new TelaDeMeditacao { Name = "Meditacao" };
		pai?.AddChild(nova);
		await Quadros(2);
		Afirmar("...e o defeito foi desfeito (a telinha esta de volta na arvore)",
				TelaDeMeditacao.Instancia != null);
	}

	// =====================================================================
	// 2. "MEDITAR NORMAL" MEDITA, E NAO VIAJA
	// =====================================================================
	/// <summary>
	/// A metade da telinha que ninguem repara -- e a que quebraria calada: dois botoes ligados no
	/// mesmo `Pressed` e o jogador que so queria recuperar o folego acorda dentro da propria cabeca.
	///
	/// O BOTAO E APERTADO PELO SINAL (`ApertarDesenhado`), como o dedo faz, e nao pelo metodo de
	/// dentro: senao a bancada pularia exatamente a ligacao que pode estar trocada.
	/// </summary>
	private async Task ONormalMeditaNormal()
	{
		GD.Print("[mergulho] --- 2. \"Meditar normal\" medita normal ---");

		if (TelaDeMeditacao.Instancia is not { } tela) { Afirmar("a telinha esta montada", false); return; }
		int eu = C?.LocalId ?? 0;
		ulong zona = C?.Zone.Hash ?? 0;

		Afirmar("a telinha abriu pra esta familia", await AbrirATelinha());
		Afirmar("o botao \"Meditar normal\" respondeu ao sinal", tela.ApertarDesenhado(profunda: false));

		await Esperar(2.0);

		Afirmar("o corpo esta MEDITANDO (a pose e do `_atividade`, e ela e do `LocalPlayer`)",
				Atividade() == Protocol.Activity.Meditando, $"{Atividade()}");
		Afirmar("...e o SERVIDOR concorda (`Ficha.med`) -- nao e so a tela",
				S?.MeditandoNoTeste(eu) ?? false);
		Afirmar("...e a zona NAO mudou: meditar normal nao leva a lugar nenhum",
				(C?.Zone.Hash ?? 0) == zona && !(S?.NaMenteNoTeste(eu) ?? true));
		Afirmar("...e a tela NAO ondulou (a gota e da profunda)",
				!(World.Instancia?.OndulandoDeTeste ?? false));
		Afirmar("...e a telinha se fechou sozinha ao responder", !tela.NaTela);

		// DEFEITO: o botao normal ligado no caminho da profunda -- os DOIS pacotes que o
		// `EscolheuMeditar(true)` manda. E o erro de fiacao mais provavel desta tela.
		C?.SendActivity(Protocol.Activity.Meditando);
		C?.SendHabilidade("mente");
		await Esperar(0.4);
		Afirmar("[defeito] com o normal ligado na profunda, o servidor JA ESTA levando o corpo -- "
				+ "a prova \"a zona nao mudou\" REPROVA",
				S?.NaOndaNoTeste(eu) ?? false);

		// desfaz: andar cancela a meditacao, e com ela o mergulho (a regra ja existia -- ver
		// `RecusaDoMergulho`). E a forma mais honesta de cancelar: a do jogador.
		World.Instancia?.AndarDeTeste(new Vector2(1, 0));
		await Esperar(0.6);
		World.Instancia?.PararDeTeste();
		C?.SendActivity(Protocol.Activity.Parado);
		await Esperar(0.6);
		Afirmar("...e o defeito foi desfeito: ANDAR cancelou o mergulho no meio da onda "
				+ "(a regra que ja existia, sem uma linha nova)",
				!(S?.NaMenteNoTeste(eu) ?? true) && !(S?.NaOndaNoTeste(eu) ?? true));
	}

	// =====================================================================
	// 3. A IDA -- A TELA ONDULA, E SO ENTAO SE VIAJA
	// =====================================================================
	private async Task AIdaOndula()
	{
		GD.Print("[mergulho] --- 3. a ida: a tela ondula, e a viagem so no fim ---");

		int eu = C?.LocalId ?? 0;
		await SairDaMenteSeEstiver();

		// A TELINHA ABRE **FORA** DA MEDIDA, e o relogio comeca no CLIQUE. O pedido do dono e sobre o
		// que acontece *"ao clicar em meditar profundamente"* -- somar a abertura da tela ao tempo da
		// onda seria medir a bancada abrindo menu.
		Afirmar("a telinha abriu pra esta familia", await AbrirATelinha());

		Transicao ida = await Medir("a IDA", "mergulho-2-meio-da-onda-da-ida", 4.0, () =>
		{
			TelaDeMeditacao.Instancia?.ApertarDesenhado(profunda: true);
			return Task.CompletedTask;
		});

		Nota(ida.Contar("ida"));

		bool OndulouAntesDeViajar() => ida.MsAteOndular >= 0
			&& (ida.MsAteViajar < 0 || ida.MsAteOndular < ida.MsAteViajar);
		bool AViagemEsperouAOnda() => ida.MsAteViajar > 0
			&& ida.MsAteViajar >= DimensaoMental.MsDaOnda * 0.7;

		Afirmar("a tela comecou a ONDULAR ao escolher \"Meditação profunda\"", OndulouAntesDeViajar(),
				ida.Contar("ida"));
		Afirmar($"...e a VIAGEM so aconteceu no fim da onda ({DimensaoMental.MsDaOnda} ms do Core, "
				+ $"medidos {ida.MsAteViajar:0} ms)", AViagemEsperouAOnda(), ida.Contar("ida"));
		Afirmar("...e quem chegou na mente foi a AUTORIDADE (o servidor concorda com a tela)",
				(S?.NaMenteNoTeste(eu) ?? false) && DimensaoMental.EhAMente(C?.Zone ?? default));
		Afirmar("...e a onda MORREU sozinha (nao sobrou tela ondulando dentro da mente)",
				!(World.Instancia?.OndulandoDeTeste ?? false));

		JulgarOPixel("3. A TELA ONDULOU DE VERDADE na ida: o mundo se moveu antes da viagem", ida);

		// ---- DEFEITO: a porta SEM a gota na frente (o `EntrarNaMente` cru, como era antes).
		await SairDaMenteSeEstiver();
		await Esperar(1.0);

		// O `med` PRECISA ESTAR ESCRITO ANTES (e a ordem dos pacotes que o `RecusaDoMergulho` cobra), e
		// essa espera fica FORA do `pedido`: o observador so comeca a contar quando o pedido volta, e um
		// `await` la dentro faria a medida nascer com a viagem ja acontecida -- que foi exatamente o que
		// aconteceu na primeira rodada (zero leitura de tela e a familia do pixel dizendo "nao mediu").
		C?.SendActivity(Protocol.Activity.Meditando);
		await Esperar(0.6);

		Transicao seca = await Medir("a ida SEM ONDA (defeito)", "mergulho-2b-defeito-sem-onda", 3.0, () =>
		{
			S?.DefeitoMergulhoSemOnda(eu);
			return Task.CompletedTask;
		});

		Nota(seca.Contar("ida sem onda"));
		bool OndulouNoDefeito() => seca.MsAteOndular >= 0;
		Afirmar("[defeito] `EntrarNaMente` cru: a tela NAO ondula e a viagem e no mesmo tique -- "
				+ "as duas provas de cima REPROVAM",
				!OndulouNoDefeito() && seca.MsAteViajar >= 0 && seca.MsAteViajar < DimensaoMental.MsDaOnda * 0.7,
				seca.Contar("defeito"));

		if (seca.Diferenca >= 0 && ida.Diferenca >= 0)
			Afirmar("[defeito] ...e a TELA DE ORIGEM fica parada ate a viagem "
					+ $"({seca.Diferenca * 100:0.0}% dos pixels contra {ida.Diferenca * 100:0.0}% com a onda) "
					+ "-- e por isso a familia do pixel nao passa verde com o shader morto",
					seca.Diferenca < ida.Diferenca / 3, $"{seca.Diferenca:0.000} vs {ida.Diferenca:0.000}");
		else
			NaoMediu("[defeito] a tela de origem fica parada ate a viagem", "sem quadro -- rode com janela");
	}

	// =====================================================================
	// 4. A VOLTA POR VITORIA ONDULA
	// =====================================================================
	private async Task AVoltaPorVitoriaOndula()
	{
		GD.Print("[mergulho] --- 4. derrotar o reflexo: a volta tambem ondula ---");

		int eu = C?.LocalId ?? 0;
		if (!await EntrarNaMentePeloGesto()) return;

		Afirmar("ha um REFLEXO de pe na mente pra ser derrotado", S?.PosDoReflexoNoTeste(eu) != null);

		Transicao volta = await Medir("a VOLTA", "mergulho-3-meio-da-onda-da-volta", 4.0, () =>
		{
			S?.MatarOReflexoNoTeste(eu);
			return Task.CompletedTask;
		});

		Nota(volta.Contar("volta"));
		Afirmar("matar o reflexo faz a tela ONDULAR (a saida deixou de ser seca)",
				volta.MsAteOndular >= 0 && (volta.MsAteViajar < 0 || volta.MsAteOndular < volta.MsAteViajar),
				volta.Contar("volta"));
		Afirmar($"...e o mundo real so volta no fim da onda ({volta.MsAteViajar:0} ms)",
				volta.MsAteViajar >= DimensaoMental.MsDaOnda * 0.7, volta.Contar("volta"));
		Afirmar("...e quem voltou foi a AUTORIDADE (o servidor tirou o corpo da mente)",
				!(S?.NaMenteNoTeste(eu) ?? true));

		JulgarOPixel("4. A TELA ONDULOU DE VERDADE na volta: a mente branca se moveu antes da saida", volta);

		// ---- DEFEITO: a volta seca -- a linha que estava em `GameServer.Clone.cs:490` antes da gota.
		if (!await EntrarNaMentePeloGesto()) return;

		Transicao secaVolta = await Medir("a volta SECA (defeito)", "", 2.5, () =>
		{
			S?.DefeitoSaidaSecaDaMente(eu);
			return Task.CompletedTask;
		});

		Nota(secaVolta.Contar("volta seca"));
		Afirmar("[defeito] `SairDaMente` cru (a transicao \"MT RAPIDA E MT SECA\" da queixa): "
				+ "nao ha onda nenhuma e a volta e no mesmo tique -- as provas de cima REPROVAM",
				secaVolta.MsAteOndular < 0 && secaVolta.MsAteViajar >= 0
				&& secaVolta.MsAteViajar < DimensaoMental.MsDaOnda * 0.7,
				secaVolta.Contar("defeito"));
	}

	// =====================================================================
	// 5. O SOCO NO CORPO REAL E SECO -- a linha que o dono escreveu entre parenteses
	// =====================================================================
	/// <summary>
	/// *"(SO N VAI TER EFEITO SE ALGUEM BATER NO CORPO REAL enquanto ta meditando)"*.
	///
	/// **SEM ESTA FAMILIA, "ondula sempre" passaria verde** nas familias 3 e 4 -- e ondular ao levar um
	/// soco e o oposto do que a onda quer dizer. Ser arrancado do transe tem que ser seco: e a
	/// diferenca entre fechar os olhos e levar um soco na cara.
	/// </summary>
	private async Task OSocoNoCorpoRealESeco()
	{
		GD.Print("[mergulho] --- 5. o soco no corpo real NAO ondula ---");

		int eu = C?.LocalId ?? 0;
		if (!await EntrarNaMentePeloGesto()) return;
		Afirmar("o corpo largado esta no mapa la fora pra apanhar", S?.ForaDoCorpoNoTeste(eu) ?? false);

		Transicao soco = await Medir("o SOCO", "", 2.5, () =>
		{
			S?.SocarOCorpoLargadoNoTeste(eu);
			return Task.CompletedTask;
		});

		Nota(soco.Contar("soco"));
		Afirmar("bater no corpo real NAO ondula a tela em quadro nenhum",
				soco.QuadrosOndulando == 0, soco.Contar("soco"));
		Afirmar("...e a volta e SECA: o mundo real chega no mesmo tique (medido "
				+ $"{soco.MsAteViajar:0} ms, contra os {DimensaoMental.MsDaOnda} ms de uma onda)",
				soco.MsAteViajar >= 0 && soco.MsAteViajar < 400, soco.Contar("soco"));
		Afirmar("...e o servidor tirou o corpo da mente pelo caminho de acordar",
				!(S?.NaMenteNoTeste(eu) ?? true));

		// ---- DEFEITO: o soco roteado pela fila da onda.
		if (!await EntrarNaMentePeloGesto()) return;

		Transicao ondulou = await Medir("o soco QUE ONDULA (defeito)", "", 3.0, () =>
		{
			S?.DefeitoSocoQueOndula(eu);
			return Task.CompletedTask;
		});

		Nota(ondulou.Contar("soco que ondula"));
		Afirmar("[defeito] com o acordar roteado pela fila da onda, a tela ondula e a volta atrasa "
				+ "1,8 s -- as duas provas de cima REPROVAM",
				ondulou.QuadrosOndulando > 0
				&& ondulou.MsAteViajar >= DimensaoMental.MsDaOnda * 0.7,
				ondulou.Contar("defeito"));
	}

	// =====================================================================
	// 6, 7 e 8. A FUGA DE QUINHENTOS TILES
	// =====================================================================
	/// <summary>
	/// *"faca tb o MAPA DA MENTE ser INFINITO SEM BORDAS e CARREGAR POR CHUNK [...] as vezes o NPC VOA
	/// PRA FORA e ele TELEPORTA DE VOLTA e fica mt estranho e perde a imersao"*.
	///
	/// ============================ UMA CAMINHADA, TRES FAMILIAS -- E ELAS PRECISAM ESTAR JUNTAS ============================
	/// Nao e economia de tempo: e a unica forma de a familia 7 significar alguma coisa. A coleira so
	/// tem assunto porque a parede saiu, e o reflexo so fica pra tras porque ha pra onde fugir. Medir
	/// "o reflexo volta" num quarto fechado seria medir a parede.
	///
	/// A caminhada anota, a cada amostra: onde o corpo esta (progresso e SALTO PRA TRAS, que e o
	/// "teleporta de volta" do dono), onde o reflexo esta (a coleira), e quantos pedacos estao vivos.
	///
	/// **A BANCADA CURA QUEM FOGE, e nao desliga o reflexo.** O que estas familias medem e a borda, a
	/// coleira e o pedaco; um nocaute no meio da fuga mediria a briga -- e o reflexo copia a ficha do
	/// dono, entao ele bate igual. Curar e o minimo que mantem a cena de pe sem apagar nada dela.
	/// ==============================================================================================================
	/// </summary>
	private async Task AFugaDeQuinhentosTiles()
	{
		GD.Print("[mergulho] --- 6/7/8. quinhentos tiles pra fora: sem borda, com coleira, por pedaco ---");

		int eu = C?.LocalId ?? 0;
		if (!await EntrarNaMentePeloGesto()) return;

		// A MENTE FICA VAZIA PRA ESTA CAMINHADA -- ver `DesfazerOReflexoNoTeste`. A borda e do MAPA;
		// medi-la com um oponente batendo mede a briga.
		Afirmar("o reflexo se desfez pra a caminhada da BORDA (a mente fica vazia)",
				S?.DesfazerOReflexoNoTeste(eu) ?? false);
		await Esperar(0.5);

		Caminhada real = await Caminhar("a borda, com a mente vazia", TilesDeFuga);

		// ---- 6. A MENTE NAO TEM BORDA.
		Afirmar($"o corpo andou os {TilesDeFuga} tiles pedidos sem esbarrar em nada "
				+ $"(chegou a {real.TilesAndados:0} tiles da origem)",
				real.TilesAndados >= TilesDeFuga * 0.95, real.Contar());
		Afirmar("...e ele nunca PAROU no caminho (nenhuma amostra sem progresso)",
				real.AmostrasParado == 0, real.Contar());
		Afirmar("...e nunca foi TELEPORTADO DE VOLTA (nenhum salto pra tras) -- a queixa do dono",
				real.SaltosPraTras == 0, real.Contar());
		Afirmar($"...e a celula final esta MUITO fora da chapa de {DimensaoMental.Lado}x{DimensaoMental.Lado} "
				+ $"(celula {real.CelulaFinal})",
				real.CelulaFinal > DimensaoMental.Lado + 300, real.Contar());

		Fotografar("mergulho-4-branco-a-500-tiles", $"4 -- o branco a {real.TilesAndados:0} tiles da origem");
		JulgarOBranco();

		// ---- 8. O PEDACO DESCARREGA. Medido nesta MESMA caminhada, que e a longa.
		//
		// O CORTE NAO E UM NUMERO ESCRITO: e a conta de quantos pedacos a caminhada ATRAVESSOU. Um
		// pedaco tem 64 tiles de lado e a camera cobre ~3 fileiras, entao 500 tiles pintaram umas duas
		// dezenas deles. Enquanto os vivos ficam em meia duzia, o descarte esta disparando; se ele
		// parasse, os vivos subiriam ate o numero de atravessados -- e e essa relacao que reprova, e
		// nao um teto de fantasia.
		int atravessados = (int)(real.TilesAndados / 64 + 1) * 3;
		Afirmar($"o numero de pedacos vivos ficou sob o teto durante os {TilesDeFuga} tiles "
				+ $"(pico {real.PicoDePedacos}, teto {TetoDePedacos})",
				real.PicoDePedacos > 0 && real.PicoDePedacos <= TetoDePedacos, real.Contar());
		Afirmar($"...e o descarte DISPAROU: ~{atravessados} pedacos foram atravessados e so "
				+ $"{real.PedacosNoFim} continuam vivos no fim",
				atravessados > real.PicoDePedacos * 2 && real.PedacosNoFim <= TetoDePedacos,
				$"atravessados ~{atravessados}, pico {real.PicoDePedacos}, fim {real.PedacosNoFim}");
		Nota($"memoria estatica: {real.MemoriaInicial / 1024 / 1024:0} MB no inicio, "
			 + $"{real.MemoriaFinal / 1024 / 1024:0} MB no fim da fuga");

		// ---- 7. O REFLEXO NAO SOME PRA SEMPRE -- outra entrada, com ele vivo.
		//
		// A CAMINHADA AQUI E CURTA de proposito: a coleira dispara a cada 40 tiles de vantagem, e o que
		// se quer sao varias disparadas -- nao distancia. Andar 500 aqui so somaria minutos de briga a
		// uma medida que ja fechou no primeiro terco.
		await SairDaMenteSeEstiver();
		if (!await EntrarNaMentePeloGesto()) return;

		Caminhada fuga = await Caminhar("a coleira, com o reflexo vivo", TilesDaCorridaComOReflexo);

		if (fuga.AmostrasComReflexo == 0)
			NaoMediu("7. a coleira devolve o reflexo", "nao havia reflexo vivo durante a fuga");
		else
		{
			float raio = DimensaoMental.RaioDaColeira / ZoneCollision.TileSize;
			Afirmar($"o reflexo REAPAREU na frente ao ficar pra tras ({fuga.SaltosDoReflexo} vez(es) ele "
					+ $"estava perto do raio numa amostra e a menos de {DistanciaDeReaparecer:0} tiles na "
					+ "seguinte -- nenhuma corrida faz isso)", fuga.SaltosDoReflexo > 0, fuga.Contar());
			Afirmar($"...e ele nunca virou um ponto no horizonte: a maior distancia foi "
					+ $"{fuga.MaiorDistanciaDoReflexo:0} tiles, contra a coleira de {raio:0}",
					fuga.MaiorDistanciaDoReflexo < raio * 1.6, fuga.Contar());
			Afirmar("...e no fim da fuga ele estava perto de novo (o combate mental ainda pode fechar)",
					fuga.DistanciaFinalDoReflexo < raio, fuga.Contar());
			Nota("os saltos PRA TRAS desta caminhada sao knockback do reflexo, e nao teleporte -- "
				 + $"({fuga.SaltosPraTras} deles). Quem mede teleporte e a familia 6, com a mente vazia.");
		}

		// ---- DEFEITO da familia 6: o quarto fechado de volta.
		//
		// A INJECAO E NA PLANTA E ELA VALE PRAS DUAS PONTAS de uma vez, porque a planta e literalmente
		// o mesmo objeto nos dois lados (o servidor a pega em `MapaDaMente`, o cliente em
		// `PlanetaProcedural.Colisao`). E ela e feita ANTES de reentrar na mente de proposito: o pintor
		// do cliente le o `SemBorda` no NASCIMENTO da zona, entao entrar depois da injecao devolve
		// tambem a metade DESENHADA do defeito (fora da chapa nao se pinta nada).
		await SairDaMenteSeEstiver();
		TerrenoGerado planta = DimensaoMental.Planta();
		planta.Colisao.SemBorda = false;
		planta.QueEsconde.SemBorda = false;
		await Esperar(1.0);

		if (!await EntrarNaMentePeloGesto()) return;
		Caminhada presa = await Caminhar("defeito: o quarto de volta", TilesDeFuga);
		Afirmar("[defeito] com o quarto de volta (`SemBorda = false`), o corpo PARA na beirada da "
				+ $"chapa ({presa.TilesAndados:0} tiles) -- a familia 6 REPROVA",
				presa.TilesAndados < DimensaoMental.Lado, presa.Contar());
		Fotografar("mergulho-4b-defeito-a-parede-de-volta", "4b -- o defeito: parado na beirada da chapa");

		planta.Colisao.SemBorda = true;
		planta.QueEsconde.SemBorda = true;
		Afirmar("...e o defeito foi desfeito (a planta voltou a dizer SEM BEIRADA)",
				DimensaoMental.Planta().Colisao.SemBorda);

		await SairDaMenteSeEstiver();
	}

	// =====================================================================
	// A CAMINHADA, MEDIDA
	// =====================================================================
	private sealed class Caminhada
	{
		public float TilesAndados;
		public int CelulaFinal;
		public int AmostrasParado;
		public int SaltosPraTras;
		public int AmostrasComReflexo;
		public int SaltosDoReflexo;
		public float MaiorDistanciaDoReflexo;
		public float DistanciaFinalDoReflexo;
		public int PicoDePedacos, PedacosNoFim;
		public ulong MemoriaInicial, MemoriaFinal;
		public bool Nocauteado;
		public double Segundos;

		public string Contar() =>
			$"andou {TilesAndados:0} tiles em {Segundos:0} s ({TilesAndados / Math.Max(Segundos, 0.001):0.0} "
			+ $"tiles/s) (celula {CelulaFinal}); parado em {AmostrasParado} amostra(s); "
			+ $"{SaltosPraTras} salto(s) pra tras; reflexo: {SaltosDoReflexo} reaparecimento(s), "
			+ $"maior {MaiorDistanciaDoReflexo:0} tiles, fim {DistanciaFinalDoReflexo:0}; "
			+ $"pedacos pico {PicoDePedacos} fim {PedacosNoFim}"
			+ (Nocauteado ? "; O CORPO CAIU no meio" : "");
	}

	/// <summary>
	/// ANDA <paramref name="tiles"/> tiles a leste e anota o caminho.
	///
	/// O ALVO E ABSOLUTO (`IrAteDeTeste`) e nao um rumo mantido por N segundos: e a diferenca entre
	/// "ande e torca" e "chegue nesta celula". Quem para numa parede fica parado no lugar em vez de
	/// entregar uma medida que parece caminhada.
	/// </summary>
	/// <summary>
	/// O TETO DE RELOGIO de uma caminhada. Generoso de proposito: ele nao e o criterio de nada -- quem
	/// julga e a distancia andada. Ele existe pra a bancada nao ficar pendurada num corpo que travou.
	/// </summary>
	private const double PrazoDaCaminhada = 300.0;

	/// <summary>
	/// A que distancia do alvo a caminhada se da por CHEGADA.
	///
	/// ============================ TRES TILES, E ELES CUSTARAM UMA FALHA FALSA ============================
	/// O piloto do corpo (`LocalPlayer.Destino`) para "perto", e a primeira rodada parou a 498 de 500. A
	/// bancada, que exigia o tile exato, continuou esperando -- e passou a contar como PARADO um corpo
	/// que ja tinha chegado. A familia "ele nunca parou no caminho" ficou vermelha nas TRES caminhadas,
	/// inclusive na que andou os 500 tiles sem esbarrar em nada.
	///
	/// "Parado" so quer dizer alguma coisa ANTES da chegada: e por isso que a mesma tolerancia governa
	/// as duas perguntas (quando parar de andar e quando contar imobilidade).
	/// ================================================================================================
	/// </summary>
	private const float ToleranciaDeChegada = 3 * ZoneCollision.TileSize;

	/// <summary>
	/// Quao perto o reflexo tem que aparecer pra a amostra contar como REAPARECIMENTO. A producao o poe
	/// a 96 px (3 tiles, a `DistanciaDoOponente` da entrada na mente); seis da folga pro quarto de
	/// segundo entre amostras, em que os dois ja se moveram.
	/// </summary>
	private const float DistanciaDeReaparecer = 6f;

	private async Task<Caminhada> Caminhar(string rotulo, int tiles)
	{
		var c = new Caminhada { MemoriaInicial = OS.GetStaticMemoryUsage() };
		int eu = C?.LocalId ?? 0;

		if (World.Instancia?.PosicaoLocal is not { } inicio) { Nota($"{rotulo}: sem posicao local"); return c; }

		float alvoX = inicio.X + tiles * ZoneCollision.TileSize;
		World.Instancia.IrAteDeTeste(new Vec2(alvoX, inicio.Y));
		Nota($"caminhada ({rotulo}): de x={inicio.X:0} ate x={alvoX:0} ({tiles} tiles a leste)");

		Vector2 anterior = inicio;
		Vec2? reflexoAntes = S?.PosDoReflexoNoTeste(eu);
		double comecou = _relogio;
		double prazo = _relogio + PrazoDaCaminhada;
		double proximaCura = _relogio;

		// CORRENDO (a tecla de correr, 2,2x). Ela e do jogador e nao um atalho: `LocalPlayer._shift` le
		// `IsActionPressed("run")`, e o servidor confere o passo com o mesmo bit. Sem ela, 500 tiles no
		// teto de velocidade deste jogo sao minutos de relogio parado olhando um boneco andar.
		Input.ActionPress("run");

		while (_relogio < prazo && !_fechou)
		{
			await Esperar(0.25);

			if (World.Instancia?.PosicaoLocal is not { } agora) break;

			// PROGRESSO E SALTO PRA TRAS. O salto e o "teleporta de volta" do dono: qualquer recuo
			// maior que dois tiles entre duas amostras de um quarto de segundo.
			float d = agora.X - anterior.X;
			bool chegou = agora.X >= alvoX - ToleranciaDeChegada;
			if (d < -2 * ZoneCollision.TileSize) c.SaltosPraTras++;
			if (Math.Abs(d) < 1f && !chegou) c.AmostrasParado++;
			anterior = agora;

			// O REFLEXO -- lido no SERVIDOR: o corpo desenhado interpola, e o que se quer ver e o salto.
			if (S?.PosDoReflexoNoTeste(eu) is { } r)
			{
				c.AmostrasComReflexo++;
				float dist = (float)Math.Sqrt(Math.Pow(r.X - agora.X, 2) + Math.Pow(r.Y - agora.Y, 2))
							 / ZoneCollision.TileSize;
				c.MaiorDistanciaDoReflexo = Math.Max(c.MaiorDistanciaDoReflexo, dist);

				// ============================ O QUE CONTA COMO "REAPARECEU" ============================
				// A primeira versao contava qualquer deslocamento do reflexo acima de 3 tiles entre duas
				// amostras -- e com o defeito da coleira INJETADO ela continuou verde: um corpo correndo
				// a 7 tiles/s cobre isso sozinho de vez em quando, e a bancada leu perseguicao normal
				// como reaparecimento.
				//
				// O que so a coleira produz e a TRANSICAO: estar longe (perto do raio) numa amostra e
				// colado na seguinte. Nenhuma corrida faz isso, porque perseguidor e fugitivo tem a
				// MESMA velocidade -- e essa e a premissa da familia inteira.
				// ==================================================================================
				float raioEmTiles = DimensaoMental.RaioDaColeira / ZoneCollision.TileSize;
				if (c.DistanciaFinalDoReflexo > raioEmTiles * 0.9f && dist < DistanciaDeReaparecer)
					c.SaltosDoReflexo++;

				c.DistanciaFinalDoReflexo = dist;
				reflexoAntes = r;
			}

			// PELA ZONA ATUAL, e nao pelo NOME do node -- ver `World.PedacosVivosDeTeste`: a leitura por
			// nome devolvia um pintor CONGELADO do cache de zona, e ficava em 6 ate com o descarte
			// desligado no fonte. Foi o defeito injetado que descobriu isso, e nao a rodada boa.
			int vivos = World.Instancia?.PedacosVivosDeTeste ?? 0;
			c.PicoDePedacos = Math.Max(c.PicoDePedacos, vivos);
			c.PedacosNoFim = vivos;

			if (C?.Sheet is { KO: true }) c.Nocauteado = true;

			// A CURA E O FOLEGO -- ver o cabecalho da familia. De dois em dois segundos, pelos funis de
			// bancada que ja existem, sem apagar o reflexo nem a briga. A ESTAMINA entra junto porque
			// correr a gasta: sem ela a corrida morre no meio e a caminhada mede fadiga.
			if (_relogio >= proximaCura)
			{
				S?.CurarDeTeste(eu);
				S?.EstaminaDeTeste(eu, 100);
				S?.KiDeTeste(eu, 1.0);
				proximaCura = _relogio + 2.0;
			}

			if (chegou) break;

			// PARADO DE VERDADE: se ele nao anda ha oito amostras seguidas, ele bateu em alguma coisa --
			// nao ha o que esperar (e o caso do defeito, que tem que terminar depressa).
			if (c.AmostrasParado >= 8) break;
		}

		Input.ActionRelease("run");
		World.Instancia?.PararDeTeste();
		if (World.Instancia?.PosicaoLocal is { } fim)
		{
			c.TilesAndados = (fim.X - inicio.X) / ZoneCollision.TileSize;
			c.CelulaFinal = (int)MathF.Floor(fim.X / ZoneCollision.TileSize);
		}
		c.Segundos = _relogio - comecou;
		c.MemoriaFinal = OS.GetStaticMemoryUsage();
		Nota($"caminhada ({rotulo}): {c.Contar()}");
		return c;
	}

	// =====================================================================
	// O OBSERVADOR DE TRANSICAO -- o coracao das familias 3, 4 e 5
	// =====================================================================
	/// <summary>
	/// O QUE UMA TRANSICAO FEZ NA TELA E NO SERVIDOR, quadro a quadro.
	///
	/// TRES RELOGIOS E UMA FOTO, e o veredito e sempre a RELACAO entre eles. Nenhum sozinho responde:
	/// "ondulou" e verdade num jogo que ondula sempre; "a zona mudou" e verdade num jogo sem onda
	/// nenhuma. E o par (ondulou ANTES, viajou DEPOIS) que e o pedido.
	/// </summary>
	private sealed class Transicao
	{
		public double MsAteOndular = -1;
		public double MsAteViajar = -1;
		public int QuadrosOndulando;
		public float MaiorFase;
		/// <summary>
		/// A MAIOR fracao de pixels que mudaram num quadro tirado **ANTES DA VIAGEM**, contra o quadro
		/// parado de controle.
		///
		/// ============================ "ANTES DA VIAGEM" E A METADE QUE FAZ ISTO MEDIR ALGUMA COISA ============================
		/// A primeira versao fotografava "o meio da onda" e comparava com o controle. Ela funcionava
		/// pra rodada boa e MENTIA na rodada de defeito: sem onda a viagem e instantanea, entao a foto
		/// do instante em que a onda cairia ja e do OUTRO MUNDO -- 98% dos pixels diferentes, e o
		/// defeito "passava" na prova do pixel com folga de sobra.
		///
		/// Limitando as amostras aos quadros em que a zona ainda NAO mudou, as duas rodadas medem a
		/// mesma coisa: o que a tela do lugar de ORIGEM fez enquanto se esperava. Com onda, uma faixa
		/// inteira se desloca; sem onda, nada acontece e a medida cai no ruido.
		/// ==================================================================================================================
		/// </summary>
		public double Diferenca = -1;
		public double Ruido = -1;       // a mesma medida entre dois quadros PARADOS
		public string Foto = "";

		public string Contar(string qual) =>
			$"{qual}: ondulou em {Escrever(MsAteOndular)}, zona mudou em {Escrever(MsAteViajar)}, "
			+ $"{QuadrosOndulando} quadro(s) de onda (maior fase {MaiorFase:0.00})"
			+ (Diferenca >= 0 ? $", pixel {Diferenca * 100:0.0}% (ruido {Ruido * 100:0.0}%)" : "");

		private static string Escrever(double ms) => ms < 0 ? "NUNCA" : $"{ms:0} ms";
	}

	/// <summary>O piso de ruido da tela parada -- ver o cabecalho da bancada.</summary>
	private double _ruidoParado = -1;

	private async Task MedirORuidoDoQuadroParado()
	{
		Image? a = Quadro();
		await Quadros(8);
		Image? b = Quadro();
		if (a == null || b == null)
		{
			Nota("sem quadro (headless): as familias de PIXEL nao vao medir nada, e vao dizer isso.");
			return;
		}
		_ruidoParado = Diferenca(a, b);
		Nota($"ruido da tela parada (aura, respiro, HUD contando): {_ruidoParado * 100:0.0}% dos pixels");
	}

	/// <summary>
	/// De quantos em quantos quadros a tela e LIDA de volta da placa, e quantas leituras no maximo.
	///
	/// Ler a tela e uma volta da GPU pra memoria; fazer isso todo quadro num 1080p atrasaria o proprio
	/// laco que esta cronometrando a onda -- a bancada mediria a si mesma. Doze amostras cobrem os
	/// 1,8 s com folga, e o que se quer delas e o MAIOR desvio, nao a curva.
	/// </summary>
	private const int QuadrosEntreLeiturasDaTela = 5;
	private const int LeiturasPorTransicao = 12;

	private async Task<Transicao> Medir(string rotulo, string foto, double segundos, Func<Task> pedido)
	{
		var t = new Transicao { Ruido = _ruidoParado };
		ulong zona = C?.Zone.Hash ?? 0;

		// O CONTROLE E TIRADO AQUI, ao lado da medida, e nao no comeco da rodada.
		Image? calmo = Quadro();
		Image? ultimoAntesDeViajar = null;
		int quadro = 0, leituras = 0;

		double t0 = _relogio;
		await pedido();

		while (_relogio - t0 < segundos && !_fechou)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			double ms = (_relogio - t0) * 1000;
			quadro++;

			bool ondulando = World.Instancia?.OndulandoDeTeste ?? false;
			float fase = World.Instancia?.GotaDeTeste ?? 0f;
			bool viajou = (C?.Zone.Hash ?? 0) != zona;

			if (ondulando)
			{
				t.QuadrosOndulando++;
				t.MaiorFase = Math.Max(t.MaiorFase, fase);
				if (t.MsAteOndular < 0) t.MsAteOndular = ms;
			}

			if (t.MsAteViajar < 0 && viajou) t.MsAteViajar = ms;

			// ---- A LEITURA DE TELA: so ANTES da viagem. Ver o comentario de `Transicao.Diferenca`.
			// O PRIMEIRO QUADRO SEMPRE E LIDO: numa viagem instantanea (o defeito) so ha um punhado de
			// quadros antes da troca, e esperar o quinto deixaria a familia do pixel sem amostra
			// nenhuma -- "nao mediu" no lugar de "nao mudou", que sao coisas diferentes.
			if (viajou || calmo == null || leituras >= LeiturasPorTransicao
				|| (quadro > 1 && quadro % QuadrosEntreLeiturasDaTela != 0)) continue;

			if (Quadro() is not { } agora) continue;
			leituras++;
			ultimoAntesDeViajar = agora;
			t.Diferenca = Math.Max(t.Diferenca, Diferenca(calmo, agora));

			// A FOTO PRO OLHO: entre 30% e 60% da fase, uma vez. Antes disso o anel ainda esta no
			// centro e depois dele ja saiu da tela -- os dois extremos dariam uma foto parecida com a
			// parada por motivo nenhum. O NUMERO acima nao depende dela.
			if (foto.Length > 0 && t.Foto.Length == 0 && ondulando && fase is > 0.30f and < 0.60f)
				t.Foto = Fotografar(foto, $"o meio da onda ({rotulo}, fase {fase:0.00})");
		}

		// SEM ONDA, A FOTO E DO ULTIMO QUADRO ANTES DA VIAGEM -- e o par visual do defeito: a mesma
		// tela, no mesmo lugar, sem anel nenhum.
		if (foto.Length > 0 && t.Foto.Length == 0 && ultimoAntesDeViajar != null)
		{
			string destino = $"user://{foto}.png";
			ultimoAntesDeViajar.SavePng(destino);
			t.Foto = ProjectSettings.GlobalizePath(destino);
			Nota($"foto: {t.Foto}  ({rotulo}: nao houve onda -- o ultimo quadro antes da viagem)");
		}
		return t;
	}

	private void JulgarOPixel(string oque, Transicao t)
	{
		if (t.Diferenca < 0 || t.Ruido < 0) { NaoMediu(oque, "sem quadro -- rode com janela"); return; }

		// O CORTE E CONTRA O RUIDO MEDIDO e nao contra um numero digitado: numa maquina em que a tela
		// parada ja mexe 2%, exigir "mudou mais que 1%" seria uma prova verde por acidente.
		double corte = Math.Max(0.05, t.Ruido * 3);
		Afirmar($"{oque} ({t.Diferenca * 100:0.0}% dos pixels contra um ruido de {t.Ruido * 100:0.0}%)",
				t.Diferenca > corte, $"corte {corte * 100:0.0}%");
	}

	/// <summary>
	/// A FOTO DE 500 TILES DE DISTANCIA E BRANCA? -- a metade que nenhuma medida de colisao alcanca.
	///
	/// Fora da chapa nao ha byte nenhum: ha um pincel repetido. Todas as provas de borda ficam verdes
	/// com esse pincel nulo, errado ou desenhado na camada de baixo -- e o jogador andaria por cima de
	/// PRETO. Ler a cor da foto e o que separa "a colisao deixa passar" de "ha chao ali".
	/// </summary>
	private void JulgarOBranco()
	{
		if (Quadro() is not { } img) { NaoMediu("6. o chao a 500 tiles e BRANCO", "sem quadro"); return; }

		int claros = 0, n = 0;
		double soma = 0;
		for (int y = 0; y < img.GetHeight(); y += 4)
			for (int x = 0; x < img.GetWidth(); x += 4)
			{
				Color c = img.GetPixel(x, y);
				n++; soma += c.Luminance;
				if (c is { R: > 0.85f, G: > 0.85f, B: > 0.85f }) claros++;
			}
		if (n == 0) { NaoMediu("6. o chao a 500 tiles e BRANCO", "quadro vazio"); return; }

		float fracao = claros / (float)n;
		Afirmar($"o chao a {TilesDeFuga} tiles da origem esta DESENHADO e e BRANCO "
				+ $"({fracao * 100:0.0}% dos pixels quase-brancos, luminancia media {soma / n:0.00})",
				fracao > 0.60f, $"{fracao * 100:0.0}%");
	}

	// =====================================================================
	// GESTOS E ATALHOS
	// =====================================================================
	/// <summary>
	/// ABRE A TELINHA -- e ela so abre pra quem NAO esta meditando.
	///
	/// ============================ ISSO NAO E UM DETALHE DE BANCADA, E A REGRA ============================
	/// *"SAIR NAO E ESCOLHA: quem ja esta meditando aperta M e para, como sempre"* (`LocalPlayer`). O
	/// `_atividade` e do CLIENTE, e ele continua dizendo "meditando" depois de a mente devolver o corpo
	/// -- porque quem meditou foi o jogador, e ninguem levantou. Entao o primeiro M depois de uma volta
	/// LEVANTA, e e o segundo que pergunta.
	///
	/// A primeira versao disto nao sabia disso e a familia 4 ficou vermelha no meio: "a telinha abriu"
	/// falhando logo depois de uma volta bem-sucedida. O defeito era da bancada, e a regra estava certa.
	/// ================================================================================================
	/// </summary>
	private async Task<bool> AbrirATelinha()
	{
		if (Atividade() == Protocol.Activity.Meditando)
		{
			await ApertarM();          // levanta
			await Esperar(0.3);
		}
		await ApertarM();
		return TelaDeMeditacao.Instancia is { NaTela: true };
	}

	/// <summary>A tecla M, pelo motor. Ver o comentario da familia 1.</summary>
	private async Task ApertarM()
	{
		Input.ParseInputEvent(new InputEventAction { Action = "meditate", Pressed = false });
		await Quadros(1);
		Input.ParseInputEvent(new InputEventAction { Action = "meditate", Pressed = true });
		await Quadros(2);
		Input.ParseInputEvent(new InputEventAction { Action = "meditate", Pressed = false });
		await Quadros(1);
	}

	private static Protocol.Activity Atividade() =>
		World.Instancia?.CorpoDeTeste(C?.LocalId ?? 0) is LocalPlayer eu
			? eu.AtividadeDeTeste : Protocol.Activity.Parado;

	/// <summary>
	/// ENTRA NA MENTE PELO GESTO INTEIRO (M -> "profunda" -> a onda -> a viagem), e espera a onda.
	///
	/// As familias 4 a 8 precisam do jogador JA la dentro; elas nao medem a entrada (isso e a familia
	/// 3). Ainda assim a entrada e feita pelo caminho do jogador e nunca por um atalho de servidor: se
	/// a porta quebrar, e melhor que TODAS elas fiquem vermelhas do que que continuem verdes por um
	/// caminho que o jogador nao tem.
	/// </summary>
	private async Task<bool> EntrarNaMentePeloGesto()
	{
		int eu = C?.LocalId ?? 0;
		await SairDaMenteSeEstiver();

		await AbrirATelinha();
		if (TelaDeMeditacao.Instancia?.ApertarDesenhado(profunda: true) != true)
		{
			Afirmar("a telinha abriu e o botao da profunda respondeu", false,
					TelaDeMeditacao.Instancia is { } t ? $"natela={t.NaTela} motivo={t.MotivoNaTela}" : "sem telinha");
			return false;
		}

		double prazo = _relogio + 6.0;
		while (_relogio < prazo && !(S?.NaMenteNoTeste(eu) ?? false)) await Esperar(0.1);
		await Esperar(0.5);   // deixa o cenario da mente montar antes de qualquer medida

		bool dentro = S?.NaMenteNoTeste(eu) ?? false;
		if (!dentro) Afirmar("o corpo chegou na mente pelo gesto do jogador", false);
		return dentro;
	}

	private async Task SairDaMenteSeEstiver()
	{
		int eu = C?.LocalId ?? 0;
		if (!(S?.NaMenteNoTeste(eu) ?? false)) { C?.SendActivity(Protocol.Activity.Parado); await Esperar(0.3); return; }

		// A SAIDA VOLUNTARIA -- o verb `sairdamente`, que e seco de proposito e por isso nao atrapalha
		// a medida seguinte.
		C?.SendHabilidade("sairdamente");
		double prazo = _relogio + 4.0;
		while (_relogio < prazo && (S?.NaMenteNoTeste(eu) ?? false)) await Esperar(0.1);
		C?.SendActivity(Protocol.Activity.Parado);
		await Esperar(0.4);
	}

	// =====================================================================
	// PIXEL
	// =====================================================================
	private Image? Quadro()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		return img == null || img.IsEmpty() ? null : img;
	}

	/// <summary>
	/// A FRACAO DE PIXELS QUE MUDARAM entre dois quadros, amostrando de 3 em 3.
	///
	/// FRACAO, E NAO A MAIOR DIFERENCA (que e o que a `--diaggota` usa): la a cena e um xadrez montado
	/// pela bancada, e o maior desvio e limpo; aqui a tela e o jogo -- um numero da HUD virando ja da
	/// uma diferenca maxima enorme num punhado de pixels. A onda move uma FAIXA inteira da tela, e a
	/// fracao e a unica medida que separa as duas coisas.
	/// </summary>
	/// <summary>
	/// QUANTO UM PIXEL PRECISA MUDAR PRA CONTAR.
	///
	/// ============================ ERA 0,06 E ELE MEDIU 0,1% NUMA ONDA QUE ESTAVA LA ============================
	/// A primeira rodada desta bancada caiu de MADRUGADA na Terra (o mundo nasce com o relogio andando),
	/// e a grama a noite e um campo quase liso de verde escuro: a onda deslocava dezenas de pixels de
	/// cada lado e a diferenca de LUMINANCIA entre "grama escura" e "a mesma grama escura tres pixels ao
	/// lado" nao chegava perto de 0,06. A prova ficou vermelha com o shader vivo e a foto na mao.
	///
	/// Duas coisas mudaram por causa disso: este numero (0,02 -- ainda cinco vezes o ruido de compressao
	/// de um quadro parado, que a bancada MEDE) e o `--horateste 0.5` do `.bat`, que poe o sol a pino.
	/// Medir deslocamento num cenario sem contraste e o mesmo erro que a `--diaggota` ja tinha resolvido
	/// com um xadrez; aqui o cenario e o jogo, entao o que se pode escolher e a HORA.
	/// ========================================================================================================
	/// </summary>
	private const float MudouDeVerdade = 0.02f;

	private static double Diferenca(Image a, Image b)
	{
		int w = Math.Min(a.GetWidth(), b.GetWidth());
		int h = Math.Min(a.GetHeight(), b.GetHeight());
		int mudou = 0, n = 0;
		for (int y = 0; y < h; y += 3)
			for (int x = 0; x < w; x += 3)
			{
				n++;
				if (Math.Abs(a.GetPixel(x, y).Luminance - b.GetPixel(x, y).Luminance) > MudouDeVerdade) mudou++;
			}
		return n == 0 ? 0 : mudou / (double)n;
	}

	private string Fotografar(string nome, string rotulo)
	{
		if (Quadro() is not { } img) { Nota($"{rotulo}: sem foto (headless nao renderiza)"); return ""; }
		string destino = $"user://{nome}.png";
		img.SavePng(destino);
		string caminho = ProjectSettings.GlobalizePath(destino);
		Nota($"foto: {caminho}  ({rotulo})");
		return caminho;
	}

	// =====================================================================
	// FIM
	// =====================================================================
	private void Fechar()
	{
		_fechou = true;
		GD.Print("");
		GD.Print("========== BANCADA DO MERGULHO ==========");
		foreach (string l in _linhas) GD.Print(l);
		GD.Print($"===== FIM: {_ok} OK, {_falhou} FALHA(S), {_semMedida} SEM MEDIDA =====");
		GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit(_falhou == 0 ? 0 : 1);
	}
}
