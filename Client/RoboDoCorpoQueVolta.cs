using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ A BANCADA DO CORPO QUE VOLTA (`--diagcorpo`) ============================
/// O efeito de esquiva deixou de ser SOBREPOSICAO e virou SUBSTITUICAO: o `flick('Zanzoken.dmi', M)`
/// do DM (`CombatMovement.dm:286`) nao desenha nada por cima do defensor -- ele TROCA o icone do mob
/// por tres quadros e devolve. O porte passou a fazer o mesmo (ver <see cref="EsquivaZanzoken"/>): o
/// corpo inteiro do defensor SOME e as listras aparecem no lugar dele.
///
/// Com isso entrou no jogo um defeito que antes nao existia, e ele e pior que qualquer efeito feio:
/// **A INVISIBILIDADE VAZAR**. Um personagem que some e nao volta e uma partida perdida. E esquiva
/// nao e evento raro -- contra alguem dez vezes mais forte a bancada de foto mediu 82% dos socos
/// esquivados, varias por segundo, SOBREPOSTAS.
///
/// ============================ A FAMILIA QUE IMPORTA ============================
/// **O CORPO QUE VOLTA.** Todas as outras linhas deste arquivo sao conforto; se so uma pudesse ficar,
/// seria a das DEZ ESQUIVAS SOBREPOSTAS (F3.5): dez trocas disparadas mais rapido que a duracao do
/// efeito e, no fim, TODO MUNDO visivel. E a unica linha que reproduz a condicao real da briga --
/// as outras disparam uma troca e esperam ela acabar, que e o caso facil.
///
/// ============================ POR QUE ESTA BANCADA NAO E A `--diagdesvio` ============================
/// A `--diagdesvio` poe dois lutadores brigando e FOTOGRAFA. Ela responde o que so o olho responde
/// (as listras sao pretas? nascem onde o corpo esta? da pra distinguir de um acerto?) e o faz bem --
/// e a prova de COR desta rodada e dela, nao daqui.
///
/// Mas ela mede o que a briga der. Ela nao consegue encomendar um critico pra provar que o anel
/// continua vivo no impacto; nao consegue nocautear alguem no milesimo certo do efeito; nao consegue
/// derrubar a zona no meio da troca; e nao consegue disparar dez esquivas na cadencia que ela quer.
/// Esta bancada faz as quatro coisas -- ela DIRIGE em vez de assistir, e por isso e deterministica e
/// roda `--headless`, sozinha, em segundos.
///
/// AS DUAS SE PRECISAM: esta prova a MAQUINA (esconde, devolve, sobrepoe, e interrompe), aquela
/// prova o PIXEL. Nenhuma das duas cobre o buraco da outra.
///
/// ============================ COMO CADA FAMILIA REPROVA ============================
///   F1 esquivou -> o corpo some          reprova nomeando o filho que SOBROU na tela (uma aura ou
///                                        um rabo boiando sozinho sobre as listras), ou dizendo que
///                                        o node do efeito nem nasceu
///   F2 passado o prazo -> o corpo volta  reprova se o corpo continua apagado depois do prazo, se
///                                        ele voltou CEDO (antes dos tres quadros), ou se o prazo
///                                        medido nao e o do `TempoDoDm` -- em particular os 0,25 s
///                                        de quem divide por `world.fps`
///   F3 dez sobrepostas                   reprova listando, por nome, os corpos que ficaram
///                                        apagados no fim; e reprova ANTES disso se em qualquer
///                                        quadro houve dois donos, corpo visivel sob o efeito, ou
///                                        corpo apagado sem efeito
///   F4 interrompido no meio              reprova por caso (nocaute, transformacao, troca de zona,
///                                        remocao a forca, cena caindo): o corpo daquele caso nao
///                                        voltou, ou o node do efeito sobreviveu ao dono
///   F5 o anel                            reprova nos DOIS sentidos: anel no desvio (alguem
///                                        recolocou o `createShockwavemisc` lendo o DM) e anel de
///                                        MENOS no critico ou na queda (alguem apagou a
///                                        `CombatFx.Onda` inteira em vez de so a chamada do desvio)
///   F6 sem tinta                         reprova se houver `Material`, `Modulate` ou `SelfModulate`
///                                        no node ou no sprite -- e a arte medida no disco tambem,
///                                        que fecha o outro lado
///   F7 acerto nao esconde ninguem        reprova nomeando o desfecho que trocou o corpo sem ser
///                                        esquiva
///
/// ============================ A F6 MEDE INTENCAO, E ISSO PRECISA ESTAR DITO ============================
/// **Ler o campo NAO e ler o pixel.** A F6 afirma que o node nao tem `Material` nem `Modulate`, e
/// isso e a INTENCAO do codigo, nao a tela: um shader herdado do pai, um `Modulate` no ancestral, um
/// `CanvasItemMaterial` de blend ou o ceu da noite por cima passariam inteiros por ela. Esta casa ja
/// perdeu quatro defeitos visuais pra mais de quatro mil checagens verdes exatamente assim.
///
/// A PROVA DE COR E A FOTO, e ela e da fase anterior: `desvio-ar-2-durante.png`, onde o retangulo das
/// listras deu 3384 pixels exatamente `(0,0,0)` e zero pixel azulado. O que a F6 faz e o trabalho de
/// guarda: garantir que ninguem RECOLOQUE tinta no caminho depois que a foto ja foi julgada. Ela nao
/// substitui a foto -- se o efeito voltar a sair colorido por um caminho que nao seja um campo deste
/// node, quem vai pegar e a `--diagdesvio`.
///
/// ============================ E ELA PROVA QUE SABE REPROVAR ============================
/// Toda regra desta bancada e uma FUNCAO (ver a regiao "AS REGRAS"), e depois da rodada real cada uma
/// recebe o defeito que ela existe pra pegar e TEM que ficar vermelha (ver <see cref="Injetar"/>).
/// Uma regra que nao reprova o proprio defeito e uma linha verde que nao significa nada -- e uma
/// injecao que passa e falha DA BANCADA, relatada como tal.
///
/// COMO RODAR (porta propria, sozinha, sem janela):
///     Godot --headless --path . --host --rede 7986 --diagcorpo --raca Human
///           --conta bancada_corpo --nome Corpo
/// Ver `testar-corpo.bat`.
/// =========================================================================================================
/// </summary>
public partial class RoboDoCorpoQueVolta : Node
{
	private static GameClient? C => GameClient.Instance;

	// =====================================================================
	// O PRAZO -- E ELE VEM DO CORE, NAO DAQUI
	// =====================================================================
	/// <summary>Quantos quadros tem o `Zanzoken.dmi`: `frames = 3` (`CombatMovement.dm:286`).</summary>
	private const int Quadros = 3;

	/// <summary>
	/// QUANTO O `flick` TEM QUE DURAR -- e a conta e feita AQUI a partir do <see cref="Jandirus.Core.TempoDoDm"/>,
	/// e nao copiada do <see cref="EsquivaZanzoken"/>.
	///
	/// ============================ POR QUE NAO SE LE A CONSTANTE DO EFEITO ============================
	/// Se a bancada lesse `EsquivaZanzoken.Duracao`, ela mediria o efeito contra ELE MESMO: trocar a
	/// divisao la por `world.fps` mudaria o valor medido E o esperado juntos, e a linha ficaria verde
	/// com o prazo 20% curto. Esse erro exato ja custou 25 cinematicas a este projeto -- e o motivo
	/// dele era um comentario que afirmava que `sleep(N)` valia `N/12 s`.
	///
	/// E se a bancada escrevesse `0,30` na mao, ela viraria a segunda verdade sobre a unidade de
	/// tempo do DM: no dia em que o decissegundo fosse revisto, o efeito seguiria a revisao e a
	/// bancada nao -- e reprovaria um efeito correto.
	///
	/// Entao ela deriva do MESMO lugar de onde qualquer prazo portado tem que derivar: os tres
	/// tiques do `delay = 1,1,1`, divididos pelos tiques por segundo do Core.
	/// ==============================================================================================
	/// </summary>
	private static readonly double PrazoEsperado = Quadros / Jandirus.Core.TempoDoDm.TiquesPorSegundo;

	/// <summary>
	/// A RESPOSTA ERRADA, escrita de proposito: `3 / world.fps` com `world.fps = 12`
	/// (`Globals/World.dm:5`) da 0,25 s. Ela existe pra a F2.3 poder afirmar que a TOLERANCIA dela e
	/// menor que a distancia entre as duas respostas -- uma tolerancia frouxa aceitaria as duas e a
	/// linha nao saberia reprovar o unico erro que ela existe pra pegar.
	/// </summary>
	private const double PrazoPeloWorldFps = Quadros / 12.0;

	// =====================================================================
	// O RELATORIO
	// =====================================================================
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private int _ok;
	private int _injecoesOk, _injecoesFalhas;

	// =====================================================================
	// O RELOGIO E O ROTEIRO
	// =====================================================================
	private double _t;
	private double _deltaSoma;
	private double _ultimoDelta;
	private ulong _quadrosVistos;
	private IEnumerator<double>? _roteiro;
	private double _espera;
	private bool _acabou;

	/// <summary>Quanto tempo o quadro leva, em media. E o que decide a precisao da F2.3.</summary>
	private double DeltaMedio => _quadrosVistos > 0 ? _deltaSoma / _quadrosVistos : 1.0 / 60.0;

	// =====================================================================
	// OS CONTADORES POR QUADRO
	// =====================================================================
	// Eles sao a metade da bancada que a foto nunca deu: um instante nao distingue "some e volta" de
	// "some pra sempre", e as tres coisas abaixo so podem dar ZERO em qualquer quadro da rodada.
	private int _doisDonos;              // dono unico quebrado -- ver `EsquivaZanzoken.Trocar`
	private int _corpoVisivelComEfeito;  // a queixa do dono, de volta: o corpo aparecendo sob as listras
	private int _corpoInvisivelSemEfeito;// O VAZAMENTO: corpo apagado sem ninguem pra devolve-lo
	private int _quadrosComEfeito;

	/// <summary>Enquanto for falso os contadores param -- a rodada de injecao ESCONDE corpos de proposito.</summary>
	private bool _vigiando = true;

	// =====================================================================
	// QUEM ESTA SENDO OLHADO
	// =====================================================================
	/// <summary>
	/// UM CORPO SOB OBSERVACAO, com o filho que serve de TERMOMETRO da visibilidade.
	///
	/// O termometro nao pode ser "algum filho": aura, nuvem de forma e sombra passam a vida
	/// legitimamente apagadas, e uma delas apagada nao diz nada sobre o corpo ter sumido. No corpo de
	/// verdade o termometro e o <see cref="CharacterVisual"/> (a pilha inteira do personagem: corpo,
	/// roupa, cabelo, contorno, rabo), que em jogo normal esta SEMPRE visivel.
	/// </summary>
	private sealed class Alvo(Node2D corpo, CanvasItem termometro, string rotulo)
	{
		public readonly Node2D Corpo = corpo;
		public readonly CanvasItem Termometro = termometro;
		public readonly string Rotulo = rotulo;

		/// <summary>Como estavam os filhos ANTES da troca. E contra isto que a volta e conferida.</summary>
		public Dictionary<string, bool> Antes = [];

		public bool Vivo => GodotObject.IsInstanceValid(Corpo) && GodotObject.IsInstanceValid(Termometro);
	}

	private readonly List<Alvo> _vigiados = [];

	/// <summary>Onde os bonecos de papel moram. Fora da camada `Atores` de proposito -- ver <see cref="Aneis"/>.</summary>
	private Node2D _palco = null!;

	public override void _Ready()
	{
		_palco = new Node2D { Name = "BonecosDePapel" };
		AddChild(_palco);
		_roteiro = Roteiro();
		GD.Print("[corpo] no ar. Ninguem precisa socar ninguem: esta bancada DIRIGE o efeito.");
	}

	public override void _Process(double delta)
	{
		_t += delta;
		_deltaSoma += delta;
		_ultimoDelta = delta;
		_quadrosVistos++;
		Vigiar();

		if (_roteiro == null || _acabou) return;
		if ((_espera -= delta) > 0) return;
		if (!_roteiro.MoveNext()) { _roteiro = null; Relatar(); return; }
		_espera = _roteiro.Current;
	}

	// =====================================================================
	// A VIGIA -- roda em TODO quadro, independente do passo do roteiro
	// =====================================================================
	/// <summary>
	/// O QUE SO PODE DAR ZERO, contado quadro a quadro em todos os corpos vigiados.
	///
	/// Ela roda solta, fora do roteiro, de proposito: os defeitos que ela persegue nao acontecem no
	/// instante em que a bancada olha, acontecem NO MEIO -- entre dois passos, num quadro que nenhuma
	/// conferencia pontual visitaria.
	/// </summary>
	private void Vigiar()
	{
		if (!_vigiando) return;

		foreach (Alvo a in _vigiados)
		{
			if (!a.Vivo) continue;

			int quantos = 0;
			foreach (Node f in a.Corpo.GetChildren())
				if (f is EsquivaZanzoken) quantos++;

			// DONO UNICO. Dois nodes no mesmo corpo quebram do jeito pior: o primeiro a vencer o
			// cronometro devolve o corpo POR BAIXO do segundo, que segue desenhando listras sobre um
			// personagem visivel -- o bug que o dono fotografou, de volta pela porta dos fundos.
			if (quantos > 1) _doisDonos++;

			if (quantos > 0)
			{
				_quadrosComEfeito++;
				if (a.Termometro.Visible) _corpoVisivelComEfeito++;
			}
			else if (!a.Termometro.Visible)
			{
				// Sem efeito rodando nao ha desculpa nenhuma pro corpo estar apagado.
				_corpoInvisivelSemEfeito++;
			}
		}
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	/// <summary>
	/// A rodada inteira, em ordem. `yield return X` espera X segundos; `yield return 0` espera o
	/// proximo quadro (que e o grao mais fino que existe aqui -- o efeito conta o prazo no
	/// `_Process` dele).
	/// </summary>
	private IEnumerator<double> Roteiro()
	{
		// ---- 0. o mundo e o corpo de verdade ----
		double t0 = _t;
		while (MeuCorpo() == null && _t - t0 < 60) yield return 0;
		if (MeuCorpo() is not { } eu)
		{
			Conferir(new Regra(false, "o corpo local existe (sem ele nao ha o que trocar)"));
			yield break;
		}
		// A APARENCIA CHEGA POR OUTRO CANAL que nao o do nascimento do corpo: medir a visibilidade
		// dos filhos antes disso assentar mediria a montagem, nao o efeito.
		yield return 0.75;

		_linhas.Add($"  --     prazo esperado do `flick`: {PrazoEsperado:0.000} s"
					+ $" = {Quadros} tiques / {Jandirus.Core.TempoDoDm.TiquesPorSegundo:0} (TempoDoDm)"
					+ $"  |  pelo `world.fps` daria {PrazoPeloWorldFps:0.000} s (a resposta errada)");
		_linhas.Add($"  --     quadro medio da maquina: {DeltaMedio * 1000:0.0} ms");

		foreach (double w in F1_EsquivouOCorpoSome(eu)) yield return w;
		foreach (double w in F2_PassadoOPrazoOCorpoVolta(eu)) yield return w;
		foreach (double w in F3_DezSobrepostas()) yield return w;
		foreach (double w in F4_InterrompidoNoMeio()) yield return w;
		foreach (double w in F5eF7_OAnelEOsDesfechos(eu)) yield return w;

		// A rodada de injecao ESCONDE corpos de proposito -- a vigia tem que estar desligada, senao
		// ela contaria o defeito injetado como defeito do jogo.
		_vigiando = false;
		yield return 0;
		RodadaDeInjecao();
	}

	// =====================================================================
	// F1 -- ESQUIVOU: O CORPO SOME, E AS LISTRAS FICAM NO LUGAR DELE
	// =====================================================================
	/// <summary>
	/// A primeira metade do pedido do dono: *"o personagem q desviou deveria ficar INVISIVEL, as
	/// LINHAS PRETAS aparecerem ONDE O CORPO DELE ESTA"*.
	///
	/// ELA RODA NO CORPO DE VERDADE, e nao num boneco de papel. O que esta em jogo aqui e o ELENCO:
	/// aura, carga, visual, nebulosa, raios, rastro, sombra, camera -- a lista que o `World.AoEntrar`
	/// monta e que ninguem copiou pra ca. Uma lista escrita na bancada envelheceria calada e o node
	/// que alguem pendurar no corpo amanha nunca seria conferido, que e exatamente o defeito que a
	/// regra invertida do <see cref="INaoSomeComOCorpo"/> existe pra impedir.
	/// </summary>
	private IEnumerable<double> F1_EsquivouOCorpoSome(Node2D eu)
	{
		Dictionary<string, bool> antes = Estado(eu);
		_linhas.Add($"  --     o elenco do corpo de verdade ({antes.Count} filhos): "
					+ string.Join(", ", antes.Select(p => $"{p.Key}{(p.Value ? "" : "(apagado)")}")));

		EsquivaZanzoken.Trocar(eu);

		// SEM ESPERAR UM QUADRO: `AddChild` dispara o `_EnterTree` na hora, e esconder o corpo E o
		// `_EnterTree`. Se fosse preciso um quadro pra o corpo sumir, haveria um quadro com o corpo
		// visivel e as listras por cima -- que e literalmente a foto que o dono reclamou.
		Conferir(UmNodeSo(eu, "F1.1 a esquiva criou UM node de efeito no corpo (e um so)"));
		Conferir(SoSobraramOsDeclarados(eu, "F1.2"));
		Conferir(TemArte(eu, "F1.3"));
		Conferir(SemTinta(EfeitoDe(eu), "F1.4"));

		// F1.5 -- ONDE. As listras nascem onde o corpo e DESENHADO.
		Vector2 ondeODesenhoEsta = eu.GetNodeOrNull<CharacterVisual>("Visual")?.Position ?? Vector2.Zero;
		Vector2 ondeAsListrasEstao = EfeitoDe(eu)?.Position ?? new Vector2(9999, 9999);
		Conferir(new Regra(ondeAsListrasEstao == ondeODesenhoEsta,
			$"F1.5 as listras nasceram onde o corpo e DESENHADO: efeito em {ondeAsListrasEstao}, desenho em {ondeODesenhoEsta}"));

		foreach (double w in EsperarSumir(eu)) yield return w;
		Conferir(VoltouAoAnterior("F1.6 o corpo de verdade", antes, Estado(eu)));

		// ============================ F1.7 -- O CORPO NO AR ============================
		// Voando, o desenho do corpo sobe ate 160 px acima do no (`SubirComOVoo`). As listras tem que
		// subir junto: plantadas nos pes, elas apareceriam no chao com o personagem no ceu -- o mesmo
		// defeito que a faisca do golpe ja teve e que o dono leu como "a hitbox pega MUITO longe".
		//
		// Aqui isso e provado em DOIS tempos, porque sao dois donos diferentes: quem poe a altura no
		// NASCIMENTO e o proprio `Trocar` (que le a posicao do `Visual`), e quem a mantem em dia
		// DEPOIS e o `SubirComOVoo`, que varre os filhos do corpo sem saber que este node existe.
		// =============================================================================
		var noAr = NovoBoneco("CorpoNoAr", comVisualDeVerdade: true);
		var visual = noAr.Corpo.GetNode<CharacterVisual>("Visual");
		visual.Position = new Vector2(0, -160);

		EsquivaZanzoken.Trocar(noAr.Corpo);
		Conferir(new Regra(EfeitoDe(noAr.Corpo)?.Position == new Vector2(0, -160),
			$"F1.7 no ar, as listras nasceram NA ALTURA DO DESENHO (-160 px): {EfeitoDe(noAr.Corpo)?.Position}"));

		// e agora o corpo SOBE no meio do efeito
		SubirComOVoo.Aplicar(noAr.Corpo, new Vector2(0, -320));
		Conferir(new Regra(EfeitoDe(noAr.Corpo)?.Position == new Vector2(0, -320),
			$"F1.8 subindo no meio da troca, as listras subiram junto: {EfeitoDe(noAr.Corpo)?.Position}"));

		foreach (double w in EsperarSumir(noAr.Corpo)) yield return w;
	}

	// =====================================================================
	// F2 -- PASSADO O PRAZO: O CORPO VOLTA
	// =====================================================================
	private IEnumerable<double> F2_PassadoOPrazoOCorpoVolta(Node2D eu)
	{
		// ============================ CINCO MEDIDAS, E NAO UMA ============================
		// O prazo e medido POR QUADRO, entao cada medida vem com a largura de um quadro de incerteza
		// -- e um engasgo isolado (uma coleta de lixo, um pedaco de cenario carregando) alarga a
		// janela dessa medida a ponto de ela nao conseguir separar 0,30 de 0,25. Cinco medidas
		// resolvem sem tolerancia chutada: a regra exige que TODAS contenham o prazo do `TempoDoDm` e
		// que PELO MENOS UMA seja estreita o bastante pra excluir a resposta do `world.fps`.
		// ==================================================================================
		var janelas = new List<(double De, double Ate)>();

		for (int i = 0; i < 5; i++)
		{
			double comecou = _t;
			EsquivaZanzoken.Trocar(eu);

			if (i == 0)
			{
				// NO MEIO DO PRAZO o corpo ainda tem que estar trocado. Sem esta linha, um efeito que
				// devolvesse o corpo no primeiro quadro passaria em todas as outras: ele "some e
				// volta", so que em 16 ms em vez de 300.
				yield return PrazoEsperado * 0.5;
				Conferir(new Regra(EfeitoDe(eu) != null && !Termometro(eu)!.Visible,
					$"F2.1 na METADE do prazo ({PrazoEsperado * 0.5:0.000} s) o corpo ainda esta trocado"));
			}

			// ---- a janela: entre a ULTIMA vez que vi o efeito e a PRIMEIRA em que nao vi mais ----
			double ultimoVisto = _t, piorQuadro = 0;
			while (EfeitoDe(eu) != null && _t - comecou < 3)
			{
				ultimoVisto = _t;
				yield return 0;
				piorQuadro = Math.Max(piorQuadro, _ultimoDelta);
			}

			// O EFEITO CONTA O PRAZO EM QUADROS, e o primeiro `_Process` dele cobra o quadro inteiro
			// em que ele nasceu -- o relogio interno corre ate um quadro ADIANTADO. Por isso a borda
			// de baixo da janela recua um quadro: e incerteza de medicao, nao folga inventada.
			janelas.Add((ultimoVisto - comecou - piorQuadro, _t - comecou));

			if (i == 0)
				Conferir(new Regra(EfeitoDe(eu) == null && Termometro(eu)!.Visible,
					"F2.2 passado o prazo o corpo esta VISIVEL de novo e o node saiu da arvore"));
			yield return 0;
		}

		_linhas.Add("  --     as cinco medidas do prazo (janela por quadro): "
					+ string.Join(", ", janelas.Select(j => $"[{j.De:0.000}..{j.Ate:0.000}]")));
		Conferir(PrazoDoFlick(janelas));

		// ============================ F2.4 -- A VOLTA E O ESTADO ANTERIOR ============================
		// Restaurar `true` cravado devolveria MAIS corpo do que se tirou: metade dos filhos esta
		// legitimamente apagada na hora da esquiva (a aura so acende com Ki, a nuvem so existe em
		// Ultra Instinto, a sombra so aparece voando), e o efeito acabaria acendendo a aura de quem
		// nao esta carregando. Por isso o boneco desta linha nasce com um filho APAGADO.
		// ==========================================================================================
		var b = NovoBoneco("AuraApagada");
		b.Corpo.GetNode<Sprite2D>("Aura").Visible = false;
		Dictionary<string, bool> antes = Estado(b.Corpo);

		EsquivaZanzoken.Trocar(b.Corpo);
		foreach (double w in EsperarSumir(b.Corpo)) yield return w;

		Dictionary<string, bool> depois = Estado(b.Corpo);
		Conferir(VoltouAoAnterior("F2.4 o corpo voltou ao estado ANTERIOR (a aura apagada continuou apagada)",
			antes, depois));
	}

	// =====================================================================
	// F3 -- DEZ ESQUIVAS SOBREPOSTAS  (A RAZAO DE SER DESTA BANCADA)
	// =====================================================================
	/// <summary>
	/// ============================ O CASO REAL, E O UNICO QUE JA QUEBROU ============================
	/// Uma troca de socos entre desiguais produz esquiva atras de esquiva, mais rapido que os 0,30 s
	/// do `flick`. Toda a arquitetura do <see cref="EsquivaZanzoken"/> saiu disso: dono unico por
	/// corpo, o fim que so AVANCA, e o `RemoveChild` sincrono em vez do `QueueFree` sozinho.
	///
	/// Os tres jeitos de errar isso, e os tres estao cobertos aqui:
	///   * DOIS DONOS: o primeiro a terminar devolve o corpo por baixo do segundo -- o corpo aparece
	///     com as listras ainda rodando (F3.2);
	///   * O SEGUNDO NODE LE OS IRMAOS JA ESCONDIDOS e grava `Estava = false` como estado "anterior":
	///     o corpo fica invisivel PRA SEMPRE (F3.4 e F3.5);
	///   * O NOME AINDA OCUPADO por um node em `QueueFree`: a esquiva nova nao desenha nada.
	///
	/// DUAS FASES, porque sao dois eixos diferentes: dez trocas no MESMO corpo (a reentrada) e dez
	/// CORPOS trocando ao mesmo tempo (a plateia). Uma so cobriria metade -- e a metade que sobra e
	/// justamente a que a `--diagdesvio` viu acontecer 97 vezes numa rodada.
	/// ==========================================================================================
	/// </summary>
	private IEnumerable<double> F3_DezSobrepostas()
	{
		int donos0 = _doisDonos, visivel0 = _corpoVisivelComEfeito, vazou0 = _corpoInvisivelSemEfeito;
		int efeito0 = _quadrosComEfeito;

		// ---- fase A: DEZ no MESMO corpo, uma a cada 30% do prazo ----
		var mesmo = NovoBoneco("DezNoMesmo");
		Dictionary<string, bool> antesDoMesmo = Estado(mesmo.Corpo);
		for (int i = 0; i < 10; i++)
		{
			EsquivaZanzoken.Trocar(mesmo.Corpo);
			// A CADENCIA E MAIS CURTA QUE O PRAZO de proposito: 0,09 s contra 0,30 s. Se ela fosse
			// maior, isto seriam dez esquivas em fila -- o caso facil, que a F2 ja cobre.
			yield return PrazoEsperado * 0.3;
		}

		// ---- fase B: DEZ CORPOS ao mesmo tempo, tres rodadas por cima ----
		var muitos = new List<Alvo>();
		for (int i = 0; i < 10; i++) muitos.Add(NovoBoneco($"Boneco{i}"));
		var antesDeMuitos = muitos.ToDictionary(a => a.Rotulo, a => Estado(a.Corpo));

		for (int rodada = 0; rodada < 3; rodada++)
			foreach (Alvo a in muitos)
			{
				EsquivaZanzoken.Trocar(a.Corpo);
				// os dez cabem dentro de um prazo: no fim da volta todos os dez estao trocados AO
				// MESMO TEMPO, e a rodada seguinte cai por cima de todos eles ainda rodando
				yield return PrazoEsperado * 0.09;
			}

		int trocasFeitas = 10 + 30;
		Conferir(new Regra(_quadrosComEfeito - efeito0 > 0,
			$"F3.1 as {trocasFeitas} trocas REALMENTE rodaram (quadros com efeito: {_quadrosComEfeito - efeito0})"));
		Conferir(DonoUnico(_doisDonos - donos0, "F3.2"));
		Conferir(CorpoEscondidoDurante(_corpoVisivelComEfeito - visivel0, "F3.3"));
		Conferir(SemVazamento(_corpoInvisivelSemEfeito - vazou0, "F3.4"));

		// ---- e agora ninguem mais dispara nada: o prazo tem que fechar sozinho ----
		yield return PrazoEsperado * 2;

		// ============================ A LINHA ============================
		// Se so uma linha desta bancada pudesse existir, seria esta.
		// =================================================================
		Conferir(TodoMundoVisivel(muitos.Append(mesmo), "F3.5"));
		Conferir(VoltouAoAnterior("F3.6 o corpo das dez trocas seguidas voltou ao estado anterior",
			antesDoMesmo, Estado(mesmo.Corpo)));

		int erradas = muitos.Count(a => !MesmoEstado(antesDeMuitos[a.Rotulo], Estado(a.Corpo)));
		Conferir(new Regra(erradas == 0,
			$"F3.7 os dez corpos voltaram ao estado anterior, um por um: {erradas} fora do lugar"));
	}

	// =====================================================================
	// F4 -- INTERROMPIDO NO MEIO
	// =====================================================================
	/// <summary>
	/// ============================ QUATRO PORTAS DE SAIDA, E ELAS SAO DE VERDADE ============================
	/// O <see cref="EsquivaZanzoken"/> afirma que **nao existe caminho de saida que passe pelo
	/// `_EnterTree` e nao pelo `_ExitTree`**. Esta familia e onde essa afirmacao e cobrada, um caso
	/// por linha, e cada caso reproduz o MECANISMO que o cliente usa de verdade:
	///
	///   F4.1 NOCAUTE          o cliente NAO libera o corpo ao nocautear -- o que ele faz e mexer nos
	///                         IRMAOS (troca a animacao do `CharacterVisual`, e a `SombraDeVoo`
	///                         reescreve o proprio `Visible` a cada mudanca de altitude,
	///                         `SombraDeVoo.cs:48`). Um irmao que se reacende no meio do efeito e uma
	///                         mancha no chao sem ninguem em cima dela.
	///   F4.2 TRANSFORMACAO    um irmao e LIBERADO e outro nasce no meio (o que uma remontagem de
	///                         aparencia faz). O `_ExitTree` tem que sobreviver a restaurar um node
	///                         morto -- e por isso ele pergunta `IsInstanceValid` antes.
	///   F4.3 TROCA DE ZONA    o corpo inteiro e liberado (`EsvaziarRemotos`, `World.cs:1840`, e
	///                         `AoSair`, `:1861`). O que nao pode e o efeito sobreviver ao dono.
	///   F4.4 A FORCA          alguem chama `RemoveChild`/`QueueFree` no node do efeito. O corpo tem
	///                         que voltar NA HORA -- e o que prova que o par mora no
	///                         `_EnterTree`/`_ExitTree` e nao num cronometro.
	///   F4.5 A CENA CAINDO    o corpo sai da arvore sem ser liberado (troca de cena, logout).
	/// ====================================================================================================
	/// </summary>
	private IEnumerable<double> F4_InterrompidoNoMeio()
	{
		// ---- F4.1 NOCAUTE: um irmao se reacende no meio ----
		var ko = NovoBoneco("Nocaute");
		Dictionary<string, bool> antesKo = Estado(ko.Corpo);
		EsquivaZanzoken.Trocar(ko.Corpo);
		yield return PrazoEsperado * 0.3;

		var sombra = ko.Corpo.GetNode<Sprite2D>("Sombra");
		sombra.Visible = true;                       // o que a `SombraDeVoo` faz sozinha ao cair
		yield return 0;                              // um quadro pro efeito reafirmar o esconde
		Conferir(new Regra(!sombra.Visible,
			"F4.1a NOCAUTE no meio: o irmao que se reacendeu sozinho foi escondido de novo no quadro seguinte"));

		foreach (double w in EsperarSumir(ko.Corpo)) yield return w;
		Conferir(VoltouAoAnterior("F4.1b NOCAUTE no meio: o corpo voltou inteiro", antesKo, Estado(ko.Corpo)));

		// ---- F4.2 TRANSFORMACAO: um irmao morre e outro nasce no meio ----
		var tr = NovoBoneco("Transformacao");
		EsquivaZanzoken.Trocar(tr.Corpo);
		yield return PrazoEsperado * 0.3;

		tr.Corpo.GetNode<Sprite2D>("Aura").QueueFree();          // a aura antiga sai
		var nova = new Sprite2D { Name = "AuraNova" };
		tr.Corpo.AddChild(nova);                                  // e a nova entra, ja com o efeito rodando
		foreach (double w in EsperarSumir(tr.Corpo)) yield return w;

		Conferir(new Regra(tr.Termometro.Visible,
			"F4.2 TRANSFORMACAO no meio (um irmao liberado, outro nascido): o corpo voltou"));
		// E O QUE FICA DE FORA, DITO EM VOZ ALTA: um node que nasce DEPOIS do `_EnterTree` nao esta
		// na lista de escondidos e continua na tela. Hoje isso e inofensivo porque nenhum caminho do
		// cliente cria filho de corpo no meio de uma partida (o elenco inteiro nasce no
		// `World.AoEntrar`); no dia em que criar, e aqui que a conta vem cobrar.
		_linhas.Add($"  --     F4.2 nota: o irmao NASCIDO no meio do efeito ficou "
					+ (nova.Visible ? "VISIVEL" : "escondido")
					+ " -- hoje nenhum caminho do cliente cria filho de corpo em partida; se criar, e defeito");

		// ---- F4.3 TROCA DE ZONA: o corpo inteiro e liberado no meio ----
		var zona = NovoBoneco("TrocaDeZona");
		EsquivaZanzoken.Trocar(zona.Corpo);
		yield return PrazoEsperado * 0.3;

		EsquivaZanzoken? orfao = EfeitoDe(zona.Corpo);
		_vigiados.Remove(zona);                                   // ele nao vai existir no proximo quadro
		zona.Corpo.QueueFree();
		yield return 0;
		yield return 0;
		Conferir(new Regra(orfao != null && !GodotObject.IsInstanceValid(orfao),
			"F4.3 TROCA DE ZONA no meio: o corpo foi liberado e o node do efeito morreu JUNTO (nao sobrou orfao)"));

		// ---- F4.4 A FORCA: o node do efeito e arrancado ----
		var forca = NovoBoneco("AForca");
		Dictionary<string, bool> antesForca = Estado(forca.Corpo);
		EsquivaZanzoken.Trocar(forca.Corpo);
		yield return PrazoEsperado * 0.3;

		EsquivaZanzoken? no = EfeitoDe(forca.Corpo);
		no?.GetParent()?.RemoveChild(no);
		// SEM ESPERAR QUADRO NENHUM: `RemoveChild` dispara o `_ExitTree` na hora. Se o corpo so
		// voltasse no proximo quadro, a devolucao estaria num cronometro e nao no par de portas -- e
		// ai existiria um caminho de saida que nao devolve o corpo.
		Conferir(new Regra(forca.Termometro.Visible,
			"F4.4a A FORCA: `RemoveChild` no node do efeito devolveu o corpo NO MESMO QUADRO"));
		Conferir(VoltouAoAnterior("F4.4b A FORCA: e devolveu o estado anterior inteiro", antesForca, Estado(forca.Corpo)));
		no?.QueueFree();

		// ---- F4.5 A CENA CAINDO: o corpo sai da arvore ----
		var cena = NovoBoneco("CenaCaindo");
		EsquivaZanzoken.Trocar(cena.Corpo);
		yield return PrazoEsperado * 0.3;

		_palco.RemoveChild(cena.Corpo);
		Conferir(new Regra(cena.Termometro.Visible,
			"F4.5 A CENA CAINDO: o corpo saiu da arvore e o efeito devolveu tudo antes de morrer"));
		cena.Corpo.QueueFree();
		_vigiados.Remove(cena);
		yield return 0;
	}

	// =====================================================================
	// F5 e F7 -- O ANEL, E QUEM MAIS ESCONDE O CORPO
	// =====================================================================
	/// <summary>
	/// As duas familias que precisam do `World.AoGolpe` de verdade, e por isso vem juntas: as duas
	/// entram pelo <see cref="World.GolpeDeTeste"/>, com um `HitEvent` por desfecho.
	///
	/// ============================ O ANEL, NOS DOIS SENTIDOS ============================
	/// O `createShockwavemisc(M.loc,1)` (`CombatMovement.dm:283`) EXISTE no original e foi tirado do
	/// desvio A PEDIDO DO DONO (*"o CIRCULO em volta da onda de choque nem deveria ter"*). E a unica
	/// divergencia deliberada deste porte no ramo da esquiva.
	///
	/// Uma bancada que so conferisse "nao ha anel no desvio" ficaria verde se alguem apagasse a
	/// `CombatFx.Onda` INTEIRA -- e o jogo perderia o anel do critico, o da queda e o das
	/// transformacoes sem uma linha vermelha. Por isso o contra-exemplo e obrigatorio: o critico e a
	/// queda TEM que continuar desenhando anel.
	/// ==================================================================================
	/// </summary>
	private IEnumerable<double> F5eF7_OAnelEOsDesfechos(Node2D eu)
	{
		if (World.Instancia?.GetNodeOrNull<Node2D>("Atores") == null)
		{
			Conferir(new Regra(false, "F5 a camada `Atores` existe (sem ela nao da pra contar anel)"));
			yield break;
		}

		// ---- F5.1 a ESQUIVA nao desenha anel ----
		int antes = Aneis();
		Golpe(Jandirus.Core.Combat.Desfecho.Esquivou);
		int naEsquiva = Aneis() - antes;
		// E O EFEITO NASCEU? Sem esta linha o "zero anel" ficaria verde por nada ter acontecido --
		// um `switch` que caisse fora do ramo da esquiva passaria nas duas familias de uma vez.
		Conferir(new Regra(EfeitoDe(eu) != null,
			"F5.0 o desfecho `Esquivou` chegando pelo `AoGolpe` TROCOU o corpo (o efeito nasceu)"));
		Conferir(Aneis(naEsquiva, 0, "F5.1 na ESQUIVA"));

		foreach (double w in EsperarSumir(eu)) yield return w;

		// ---- F5.2 o CRITICO continua desenhando anel ----
		antes = Aneis();
		Golpe(Jandirus.Core.Combat.Desfecho.Critico);
		Conferir(Aneis(Aneis() - antes, 1, "F5.2 no CRITICO (o contra-exemplo)"));

		// ---- F5.3 a QUEDA continua desenhando anel ----
		antes = Aneis();
		Golpe(Jandirus.Core.Combat.Desfecho.Acertou, nocauteou: true);
		Conferir(Aneis(Aneis() - antes, 1, "F5.3 na QUEDA (nocaute)"));

		// ---- F7: de todos os desfechos, SO a esquiva troca o corpo ----
		var trocaram = new List<string>();
		foreach (Jandirus.Core.Combat.Desfecho d in new[]
		{
			Jandirus.Core.Combat.Desfecho.Errou,
			Jandirus.Core.Combat.Desfecho.Aparou,
			Jandirus.Core.Combat.Desfecho.Contra,
			Jandirus.Core.Combat.Desfecho.Acertou,
			Jandirus.Core.Combat.Desfecho.Critico,
		})
		{
			Golpe(d);
			if (EfeitoDe(eu) != null) trocaram.Add(d.ToString());
			if (!Termometro(eu)!.Visible) trocaram.Add(d + "(corpo apagado)");
			yield return 0;
		}
		Conferir(NinguemTrocado(trocaram, "F7.1"));
	}

	// =====================================================================
	// AS REGRAS -- funcoes, pra a rodada de injecao poder cobra-las
	// =====================================================================
	/// <summary>Uma resposta de regra: passou ou nao, e a frase que vai pro relatorio.</summary>
	private readonly record struct Regra(bool Ok, string Texto);

	private Regra UmNodeSo(Node2D corpo, string rotulo)
	{
		int quantos = corpo.GetChildren().Count(f => f is EsquivaZanzoken);
		return new Regra(quantos == 1, $"{rotulo}: {quantos}");
	}

	/// <summary>
	/// SO PODEM TER SOBRADO OS DECLARADOS. A regra e invertida de proposito (ver
	/// <see cref="INaoSomeComOCorpo"/>): a bancada nao tem lista de quem deve sumir -- ela cobra
	/// TODO filho visual e aceita como excecao apenas quem declarou a interface na propria classe.
	///
	/// Uma lista por nome aqui teria o mesmo destino da lista do voo, que esqueceu alguem quatro
	/// vezes: o node que outra sessao pendurar no corpo amanha passaria despercebido, e o defeito
	/// seria uma aura ou um rabo boiando sozinho sobre as listras.
	/// </summary>
	private Regra SoSobraramOsDeclarados(Node2D corpo, string rotulo)
	{
		var sobrou = new List<string>();
		var rotulos = new List<string>();
		foreach (Node f in corpo.GetChildren())
		{
			if (f is EsquivaZanzoken or Camera2D) continue;
			if (f is not CanvasItem { Visible: true } ci) continue;
			if (f is INaoSomeComOCorpo) { rotulos.Add(ci.Name); continue; }
			sobrou.Add($"{ci.Name}<{ci.GetType().Name}>");
		}
		return new Regra(sobrou.Count == 0,
			$"{rotulo} do corpo so ficaram os declarados [{(rotulos.Count > 0 ? string.Join(", ", rotulos) : "nenhum")}]"
			+ (sobrou.Count > 0 ? $" -- SOBROU NA TELA: {string.Join(" + ", sobrou)}" : ""));
	}

	private Regra TemArte(Node2D corpo, string rotulo)
	{
		Sprite2D? s = EfeitoDe(corpo)?.GetChildren().OfType<Sprite2D>().FirstOrDefault();
		return new Regra(s?.Texture != null,
			$"{rotulo} o que aparece no lugar do corpo TEM textura (esconder sem desenhar nada seria a queixa levada ao extremo)");
	}

	/// <summary>
	/// SEM TINTA -- e vale repetir aqui, junto da regra, o que o cabecalho ja diz: **isto mede a
	/// INTENCAO, nao o pixel**. Ela afirma que nenhum campo DESTE node pinta a arte; nao afirma que a
	/// tela saiu preta. A prova de cor e a foto (`desvio-*-2-durante.png`), e esta linha e so a
	/// tranca que impede alguem de recolocar o `ShaderSilhueta` azul-gelo que produziu a queixa.
	/// </summary>
	private Regra SemTinta(Node2D? efeito, string rotulo)
	{
		if (efeito == null) return new Regra(false, $"{rotulo} sem tinta: o node do efeito nem existe");

		var sujos = new List<string>();
		void Olhar(CanvasItem ci)
		{
			if (ci.Material != null) sujos.Add($"{ci.Name}.Material={ci.Material.GetType().Name}");
			if (ci.Modulate != Colors.White) sujos.Add($"{ci.Name}.Modulate={ci.Modulate}");
			if (ci.SelfModulate != Colors.White) sujos.Add($"{ci.Name}.SelfModulate={ci.SelfModulate}");
			foreach (Node f in ci.GetChildren()) if (f is CanvasItem filho) Olhar(filho);
		}
		Olhar(efeito);

		return new Regra(sujos.Count == 0,
			$"{rotulo} as listras saem SEM TINTA -- nenhum Material, nenhum Modulate (INTENCAO, nao pixel: a cor quem prova e a foto)"
			+ (sujos.Count > 0 ? $" -- TINGIDO: {string.Join(", ", sujos)}" : ""));
	}

	private Regra DonoUnico(int quantos, string rotulo)
		=> new(quantos == 0, $"{rotulo} DONO UNICO por corpo -- nenhum quadro com dois nodes empilhados: {quantos}");

	private Regra CorpoEscondidoDurante(int quantos, string rotulo)
		=> new(quantos == 0, $"{rotulo} nenhum quadro com o corpo APARECENDO sob as listras: {quantos}");

	private Regra SemVazamento(int quantos, string rotulo)
		=> new(quantos == 0, $"{rotulo} a invisibilidade NAO VAZOU -- nenhum quadro com o corpo apagado e o efeito fora: {quantos}");

	/// <summary>
	/// TODO MUNDO VISIVEL. **A linha.** Ela reprova NOMEANDO os corpos que ficaram apagados, porque
	/// "algum corpo sumiu" nao ajuda ninguem a achar o que sobrou pendurado.
	/// </summary>
	private Regra TodoMundoVisivel(IEnumerable<Alvo> alvos, string rotulo)
	{
		var apagados = new List<string>();
		var rodando = new List<string>();
		int olhados = 0;
		foreach (Alvo a in alvos)
		{
			if (!a.Vivo) continue;
			olhados++;
			if (!a.Termometro.Visible) apagados.Add(a.Rotulo);
			if (EfeitoDe(a.Corpo) != null) rodando.Add(a.Rotulo);
		}
		return new Regra(olhados > 0 && apagados.Count == 0 && rodando.Count == 0,
			$"{rotulo} depois das trocas sobrepostas TODO MUNDO esta visivel: {olhados} corpo(s)"
			+ (apagados.Count > 0 ? $"  APAGADOS: {string.Join(", ", apagados)}" : "")
			+ (rodando.Count > 0 ? $"  AINDA COM EFEITO: {string.Join(", ", rodando)}" : "")
			+ (olhados == 0 ? "  (nenhum corpo olhado -- a linha nao mediu nada)" : ""));
	}

	/// <summary>
	/// O PRAZO, e ele e cobrado por JANELA e nao por tolerancia.
	///
	/// ============================ POR QUE NAO HA UM "+-" ESCRITO AQUI ============================
	/// Um numero de tolerancia escrito na bancada e um chute que envelhece: frouxo, ele aceita 0,30 e
	/// 0,25 igualmente e a linha para de saber reprovar o unico erro que ela existe pra pegar (o
	/// `sleep(N) = N/12`, que ja saiu 20% curto em 25 cinematicas deste projeto); apertado, ele
	/// reprova um efeito correto num quadro lento.
	///
	/// Entao cada medida vira uma JANELA -- do ultimo quadro em que o efeito foi visto ate o primeiro
	/// em que ele nao estava mais -- e a regra faz duas perguntas separadas:
	///
	///   1. TODAS as janelas contem o prazo do `TempoDoDm`?  (senao o prazo esta errado)
	///   2. ALGUMA janela EXCLUI o prazo do `world.fps`?     (senao a bancada nao mediu fino o
	///      bastante pra afirmar coisa nenhuma -- e ela diz isso, em vez de passar verde)
	///
	/// A segunda pergunta e a que separa "o efeito esta certo" de "a maquina nao deixou eu medir".
	/// ==========================================================================================
	/// </summary>
	private Regra PrazoDoFlick(List<(double De, double Ate)> janelas)
	{
		if (janelas.Count == 0) return new Regra(false, "F2.3 o prazo: nenhuma medida foi feita");

		int foraDoCerto = janelas.Count(j => PrazoEsperado < j.De || PrazoEsperado > j.Ate);
		int separaram = janelas.Count(j => PrazoPeloWorldFps < j.De || PrazoPeloWorldFps > j.Ate);

		if (foraDoCerto > 0)
			return new Regra(false,
				$"F2.3 o prazo NAO e o do `TempoDoDm`: {foraDoCerto} de {janelas.Count} medidas nao contem"
				+ $" {PrazoEsperado:0.000} s ({Quadros} tiques / {Jandirus.Core.TempoDoDm.TiquesPorSegundo:0})");

		if (separaram == 0)
			return new Regra(false,
				$"F2.3 o prazo: nenhuma das {janelas.Count} medidas foi fina o bastante pra separar"
				+ $" {PrazoEsperado:0.000} s de {PrazoPeloWorldFps:0.000} s (quadro medio {DeltaMedio * 1000:0.0} ms)"
				+ " -- a linha REPROVA em vez de passar verde sem ter medido nada");

		return new Regra(true,
			$"F2.3 o prazo e o do `TempoDoDm` ({PrazoEsperado:0.000} s = {Quadros} tiques / "
			+ $"{Jandirus.Core.TempoDoDm.TiquesPorSegundo:0}): as {janelas.Count} medidas o contem, e {separaram}"
			+ $" delas EXCLUEM os {PrazoPeloWorldFps:0.000} s do `world.fps`");
	}

	private Regra Aneis(int quantos, int esperado, string rotulo)
		=> new(quantos == esperado,
			esperado == 0
				? $"{rotulo}: nenhum anel de choque nasceu na camada Atores ({quantos}) -- a divergencia do `createShockwavemisc`"
				: $"{rotulo}: o anel CONTINUA nascendo ({quantos} de {esperado} esperado) -- a `CombatFx.Onda` nao foi apagada junto");

	private Regra NinguemTrocado(List<string> trocaram, string rotulo)
		=> new(trocaram.Count == 0,
			$"{rotulo} de todos os desfechos, SO a esquiva troca o corpo"
			+ (trocaram.Count > 0 ? $" -- ESCONDERAM ALGUEM: {string.Join(", ", trocaram)}" : ""));

	private Regra VoltouAoAnterior(string rotulo, Dictionary<string, bool> antes, Dictionary<string, bool> depois)
	{
		var erradas = new List<string>();
		foreach ((string nome, bool estava) in antes)
		{
			if (!depois.TryGetValue(nome, out bool agora)) continue;   // liberado no meio -- ver F4.2
			if (agora != estava) erradas.Add($"{nome}: era {estava}, virou {agora}");
		}
		return new Regra(erradas.Count == 0,
			$"{rotulo}" + (erradas.Count > 0 ? $" -- FORA DO LUGAR: {string.Join(" | ", erradas)}" : ""));
	}

	private static bool MesmoEstado(Dictionary<string, bool> a, Dictionary<string, bool> b)
		=> a.All(p => !b.TryGetValue(p.Key, out bool v) || v == p.Value);

	// =====================================================================
	// A RODADA DE INJECAO
	// =====================================================================
	/// <summary>
	/// ============================ TODA REGRA TEM QUE SABER REPROVAR ============================
	/// Depois da rodada real, cada regra recebe EXATAMENTE o defeito que ela existe pra pegar e tem
	/// que ficar vermelha. O primeiro caso e o mais importante: um corpo escondido sem ninguem pra
	/// devolve-lo, que e o que sobra do <see cref="EsquivaZanzoken"/> quando se tira o caminho de
	/// restauracao do `_ExitTree`. Se a F3.5 nao reprovar isso, a bancada inteira e enfeite.
	/// =======================================================================================
	/// </summary>
	private void RodadaDeInjecao()
	{
		_linhas.Add("  --     ===== INJECAO: cada regra recebe o defeito que ela existe pra pegar =====");

		// 1. O CORPO QUE SUMIU E NAO VOLTOU -- o `_ExitTree` sem restauracao.
		var fantasma = NovoBoneco("InjecaoSumiu", vigiar: false);
		foreach (Node f in fantasma.Corpo.GetChildren())
			if (f is CanvasItem ci) ci.Visible = false;
		Injetar(TodoMundoVisivel([fantasma], "F3.5"),
			"o caminho de restauracao arrancado: o corpo fica escondido e ninguem o devolve");

		// 2. o dono unico quebrado
		Injetar(DonoUnico(1, "F3.2"), "dois nodes de esquiva no mesmo corpo");

		// 3. o corpo aparecendo sob as listras (a queixa do dono)
		Injetar(CorpoEscondidoDurante(3, "F3.3"), "tres quadros com o corpo visivel por baixo do efeito");

		// 4. o vazamento
		Injetar(SemVazamento(2, "F3.4"), "dois quadros com o corpo apagado e o efeito ja fora");

		// 5. O PRAZO PELO `world.fps` -- os 20% curtos que ja custaram 25 cinematicas.
		Injetar(PrazoDoFlick([(0.244, 0.252), (0.245, 0.251), (0.243, 0.253)]),
			"o prazo dividido por `world.fps` (0,25 s) em vez do decissegundo");
		// 5b. E O OUTRO JEITO DE MENTIR: medidas tao grosseiras que aceitam as duas respostas. A
		// linha nao pode passar verde por ter olhado com o olho fechado.
		Injetar(PrazoDoFlick([(0.20, 0.40), (0.19, 0.41)]),
			"medidas grosseiras demais pra separar 0,30 de 0,25 (a maquina engasgada passando por prova)");

		// 6. A TINTA DE VOLTA, e na cor exata do `ShaderSilhueta` que foi deletado.
		var tingido = new Node2D { Name = "InjecaoTinta" };
		tingido.AddChild(new Sprite2D { Name = "Listras", Modulate = new Color(0.55f, 0.80f, 1.00f) });
		_palco.AddChild(tingido);
		Injetar(SemTinta(tingido, "F1.4"), "o azul-gelo do `ShaderSilhueta` de volta por cima da arte preta");
		tingido.QueueFree();

		// 7. O ANEL RECOLOCADO no desvio (alguem "consertando" de volta lendo o `CombatMovement.dm`).
		Injetar(Aneis(1, 0, "F5.1 na ESQUIVA"), "o `createShockwavemisc` recolocado no ramo da esquiva");

		// 8. O CONTRA-EXEMPLO: a `CombatFx.Onda` apagada INTEIRA em vez de so a chamada do desvio.
		Injetar(Aneis(0, 1, "F5.2 no CRITICO (o contra-exemplo)"), "a `CombatFx.Onda` apagada inteira -- o critico perdeu o anel");

		// 9. um acerto escondendo o corpo
		Injetar(NinguemTrocado(["Acertou"], "F7.1"), "um acerto trocando o corpo como se fosse esquiva");

		// 10. A RESTAURACAO CRAVADA EM `true`: a aura de quem nao esta carregando, acesa.
		Injetar(VoltouAoAnterior("F2.4 o corpo voltou ao estado ANTERIOR",
				new Dictionary<string, bool> { ["Aura"] = false },
				new Dictionary<string, bool> { ["Aura"] = true }),
			"a restauracao cravada em `true`: a aura apagada voltou ACESA");

		// 11. a regra que so pode passar VENDO alguem
		Injetar(TodoMundoVisivel([], "F3.5"), "nenhum corpo pra olhar -- a linha nao pode passar verde sem ter medido nada");
	}

	private void Injetar(Regra r, string defeito)
	{
		if (!r.Ok)
		{
			_linhas.Add($"  ok     [injecao] {defeito}  ->  a regra REPROVOU");
			_injecoesOk++;
			return;
		}
		_linhas.Add($"  FALHA  [injecao] {defeito}  ->  A REGRA PASSOU (ela nao sabe reprovar o proprio defeito): {r.Texto}");
		_injecoesFalhas++;
		_falhas.Add($"[injecao] {defeito}: a regra passou verde");
	}

	// =====================================================================
	// FERRAMENTA
	// =====================================================================
	private static Node2D? MeuCorpo()
		=> C is { } cli ? World.Instancia?.CorpoDeTeste(cli.LocalId) : null;

	private static EsquivaZanzoken? EfeitoDe(Node2D? corpo)
		=> corpo != null && GodotObject.IsInstanceValid(corpo)
			? corpo.GetChildren().OfType<EsquivaZanzoken>().FirstOrDefault()
			: null;

	/// <summary>O termometro de um corpo: o `CharacterVisual` no corpo de verdade, a "Pele" no boneco.</summary>
	private static CanvasItem? Termometro(Node2D corpo)
		=> (CanvasItem?)corpo.GetNodeOrNull<CharacterVisual>("Visual") ?? corpo.GetNodeOrNull<Sprite2D>("Pele");

	/// <summary>Quem esta visivel agora, por nome de filho. O node do efeito nao entra -- ele e o que aparece no LUGAR.</summary>
	private static Dictionary<string, bool> Estado(Node2D corpo)
	{
		var d = new Dictionary<string, bool>();
		foreach (Node f in corpo.GetChildren())
			if (f is CanvasItem ci and not EsquivaZanzoken) d[ci.Name] = ci.Visible;
		return d;
	}

	private IEnumerable<double> EsperarSumir(Node2D corpo, double teto = 3.0)
	{
		double t0 = _t;
		while (EfeitoDe(corpo) != null && _t - t0 < teto) yield return 0;
		// UM QUADRO A MAIS: o `_ExitTree` ja devolveu o corpo, mas quem ainda nao rodou neste quadro
		// pode reescrever `Visible` (a `SombraDeVoo` faz isso). Medir a volta no quadro seguinte e
		// medir o que fica na tela, e nao o que este node acabou de escrever.
		yield return 0;
	}

	/// <summary>
	/// QUANTOS ANEIS DE CHOQUE EXISTEM na camada de atores AGORA.
	///
	/// O anel e o unico `ColorRect` com `ShaderMaterial` que nasce ali (`CombatFx.Onda`); a faisca e
	/// um `AnimatedSprite2D` e os corpos sao `Node2D`. Contar antes e depois da chamada, no MESMO
	/// quadro, funciona porque `AddChild` e sincrono -- e o anel so se auto-libera 0,25 s depois.
	///
	/// Os bonecos de papel desta bancada moram no `_palco`, e nao aqui, exatamente pra nao entrarem
	/// nesta conta.
	/// </summary>
	private static int Aneis()
	{
		if (World.Instancia?.GetNodeOrNull<Node2D>("Atores") is not { } atores) return 0;
		return atores.GetChildren().Count(f => f is ColorRect { Material: ShaderMaterial });
	}

	/// <summary>Um `HitEvent` como o servidor manda, entregue pelo caminho de verdade.</summary>
	private static void Golpe(Jandirus.Core.Combat.Desfecho d, bool nocauteou = false)
	{
		if (C is not { } cli || World.Instancia is not { } mundo) return;
		mundo.GolpeDeTeste(new Protocol.HitEvent
		{
			// ATACANTE ZERO: nao ha segundo corpo nesta bancada, e o `AoGolpe` ja trata isso (a faisca
			// do desvio nasce em QUEM BATEU, entao sem atacante ela simplesmente nao sai). O que esta
			// em jogo aqui e o que acontece com QUEM LEVA.
			Atacante = 0,
			Alvo = cli.LocalId,
			Desfecho = (byte)d,
			Nivel = 2,
			Membro = "",
			Nocauteou = nocauteou,
		});
	}

	/// <summary>
	/// UM BONECO DE PAPEL: um corpo com filhos de mentira, pra as familias que medem a MAQUINA.
	///
	/// O ELENCO DE VERDADE e medido na F1, no corpo de verdade -- e la ele nao pode ser copiado pra
	/// ca, senao a copia envelheceria calada. Aqui o que se mede e esconder/devolver/sobrepor, e pra
	/// isso bastam quatro filhos: um visivel que serve de termometro, um APAGADO de nascenca (a aura
	/// de quem nao carrega), uma sombra e um rotulo declarado.
	/// </summary>
	private Alvo NovoBoneco(string nome, bool comVisualDeVerdade = false, bool vigiar = true)
	{
		var corpo = new Node2D { Name = nome };
		_palco.AddChild(corpo);

		corpo.AddChild(new Sprite2D { Name = "Pele" });
		corpo.AddChild(new Sprite2D { Name = "Aura" });
		corpo.AddChild(new Sprite2D { Name = "Sombra" });
		// O ROTULO DECLARADO: e um `BalaoDeFala` de verdade, nao uma imitacao. Quem responde "eu nao
		// sou o corpo" e a interface na declaracao DELE (`INaoSomeComOCorpo`), e uma imitacao aqui
		// provaria a interface da bancada, nao a do jogo.
		var balao = new BalaoDeFala { Name = "Balao" };
		corpo.AddChild(balao);
		balao.Visible = true;   // ele nasce calado (e apagado); aqui ele precisa estar na tela pra ser cobrado

		if (comVisualDeVerdade) corpo.AddChild(new CharacterVisual { Name = "Visual" });

		var a = new Alvo(corpo, Termometro(corpo)!, nome);
		if (vigiar) _vigiados.Add(a);
		return a;
	}

	// =====================================================================
	// O RELATORIO
	// =====================================================================
	private void Conferir(Regra r)
	{
		_linhas.Add((r.Ok ? "  ok     " : "  FALHA  ") + r.Texto);
		if (r.Ok) _ok++;
		else _falhas.Add(r.Texto);
	}

	private void Relatar()
	{
		_acabou = true;

		GD.Print("\n[corpo] ===== BANCADA DO CORPO QUE VOLTA =====");
		foreach (string l in _linhas) GD.Print("[corpo] " + l);
		GD.Print($"[corpo] quadros vistos: {_quadrosVistos} ({DeltaMedio * 1000:0.0} ms cada)"
				 + $" | quadros com efeito rodando: {_quadrosComEfeito}"
				 + $" | corpos vigiados: {_vigiados.Count}");
		GD.Print($"[corpo] conferencias: {_ok} ok, {_falhas.Count(f => !f.StartsWith("[injecao]"))} falha(s)"
				 + $" | injecoes: {_injecoesOk} reprovaram como deviam, {_injecoesFalhas} passaram verde");
		GD.Print(_falhas.Count == 0
			? "[corpo] ===== TUDO OK ====="
			: $"[corpo] ===== {_falhas.Count} FALHA(S) =====\n[corpo]   " + string.Join("\n[corpo]   ", _falhas));

		GetTree().Quit();
	}
}
