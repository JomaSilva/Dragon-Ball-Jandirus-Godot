using Godot;

namespace Jandirus.Client;

/// <summary>
/// A NUVEM DE GALAXIA QUE ENVOLVE QUEM ESTA EM ULTRA INSTINTO.
///
/// Irmao do <see cref="RaiosDaForma"/> em tudo: um node que existe sempre, nasce desligado, e que o
/// <see cref="Transformacao"/> (com cena) e o `World.AcenderFormaNoCorpo` (sem cena) acendem pelo
/// mesmo par de chamadas. Quem decide SE ele acende e o Core --
/// <see cref="Jandirus.Core.Forms.Catalogo.TemNebulosa"/>, derivado da linha da forma --, e o
/// cabecalho de la explica por que a paleta ficou no shader em vez de vir junto.
///
/// ============================ ELE E O PEDACO DA REFERENCIA QUE NAO TEM ARTE ============================
/// A imagem que o dono mandou tem cinco camadas, e tres delas ja tinham dono antes deste arquivo:
///
///   * o AZUL-LAVANDA encostado na pele e as MICROPARTICULAS brancas subindo sao as duas folhas
///     coladas que o DM veste (`UltraInstinct.dm:479-480`) -- passam pelo canal de
///     <see cref="Jandirus.Core.Forms.Colada"/>;
///   * o FIO PRATEADO na silhueta e o contorno de forma que ja existe, escrito no
///     `Personagem.gdshader` pelo uniform `aura` (a cor sai de
///     `Catalogo.CorDoContorno` -> `BrancoDoInstinto`, `f0f6ff`).
///
/// O que sobrava eram as camadas 1 e 2: o fundo indigo quase preto e as volutas violeta-azuladas
/// com filamento. Elas ocupam TRES corpos de largura na referencia -- uma celula de 32 px nao
/// envolve ninguem --, entao nao ha .dmi que sirva e o desenho e procedural. Ver o cabecalho do
/// `NebulosaDaForma.gdshader`.
/// ====================================================================================================
/// </summary>
public partial class NebulosaDaForma : Node2D
{
	private const string CaminhoDoShader = "res://Assets/Shaders/NebulosaDaForma.gdshader";

	/// <summary>
	/// A FOLGA EM VOLTA DA SILHUETA, em pixels de mundo. Palavra do dono, literal:
	///
	///   "a aura do ultra instinto, e do perfected ultra instinct estao mt grandes, era pra estar
	///    perto do corpo, no MAXIMO 5 pixels de distancia do corpo"
	///
	/// ============================ O QUE ELA SUBSTITUIU ============================
	/// Aqui havia `Lado = 96` -- um quad de tres tiles, lido da referencia "em corpos". Ele nao era
	/// arbitrario, era ERRADO por leitura: a foto no jogo mostrou um halo redondo bem maior que o
	/// boneco, com o personagem inteiro dentro dele. Medido naquele quad, contra a silhueta util de
	/// 16 px, a nuvem sobrava **26 px de lado, 19 acima da cabeca e 29 abaixo dos pes** -- entre 3,7x
	/// e 6x o que o dono pediu, e assimetrica de quebra (o `centro.y = 0.55` do shader empurrava a
	/// elipse pra baixo enquanto os pes ficam em +16).
	///
	/// Agora nao ha lado nenhum escrito: ele SAI da silhueta medida mais esta folga (ver
	/// <see cref="ModelarPeloCorpo"/>). O corpo de 32 produz um quad de 42; o Oozaru, de folha 96,
	/// produziria um de ~106 -- com os mesmos 5 px em volta, sem ninguem recalcular nada. E a mesma
	/// escolha do <see cref="SpriteDeAura.AncoraPara"/>, que e uma REGRA (`LinhaDosPes - altura/2`) e
	/// nao um `-16` cravado que so estaria certo pra um tamanho de folha.
	/// ============================================================================
	/// </summary>
	private const float Folga = 5f;

	/// <summary>
	/// A LARGURA DA SILHUETA QUANDO ELA NAO PODE SER MEDIDA, em fracao da largura do quadro.
	///
	/// So vale enquanto o boneco ainda nao tem folha (o node nasce antes de `VestirCorpoInteiro`) ou
	/// se a textura do quadro nao devolver imagem. Metade e o numero que este projeto ja assumia: o
	/// cabecalho do shader dizia "a silhueta em pe ocupa ~16 de largura util" num quadro de 32, e a
	/// medida do alfa confirma. Como fallback ele erra pra ESTREITO, que e o lado seguro: a nuvem sai
	/// mais colada, nunca mais larga que o pedido.
	/// </summary>
	private const float FracaoDaSilhuetaSemMedida = 0.5f;

	/// <summary>
	/// POR CIMA DO CORPO, e o `ZIndex` NAO e quem decide isso -- e a ORDEM DE IRMAO. Por isso ele
	/// continua ZERO.
	///
	/// A regra ja esta escrita no `World.AoEntrar` pra <see cref="Aura"/>: com todos os desenhos do
	/// corpo em z 0, quem desenha por cima e o irmao mais NOVO. Este node entra DEPOIS do
	/// `CharacterVisual` -- entao a pilha do corpo fica
	/// aura &lt; carga &lt; boneco &lt; nebulosa &lt; raios (z 6).
	///
	/// ============================ ERA ATRAS, E O DONO PEDIU NA FRENTE ============================
	/// "o efeito deveria ficar sobre o corpo e nao atras". A nuvem nascia como o irmao MAIS VELHO,
	/// justamente pra ficar por tras -- e a primeira versao tinha RECUSADO a passada por cima com um
	/// argumento que continua valendo: nuvem desenhada sobre o sprite tapa o rosto e a roupa.
	///
	/// O que muda e a resposta. Nao se volta pra tras: baixa-se o ALFA por cima da silhueta (o
	/// `veu_no_corpo` do shader, ~0,2 em cima do rosto), que e o que a referencia mostra -- da pra ver
	/// o rosto do Goku ATRAVES do efeito. O anel colado no corpo continua cheio.
	/// ====================================================================================
	///
	/// ============================ POR QUE NAO UM Z ALTO ============================
	/// Z vence Y-sort, e e isso que o torna perigoso aqui: um z positivo tiraria a nuvem da ordem do
	/// mundo inteiro e ela passaria a desenhar por CIMA das arvores e paredes do cenario -- que e o
	/// tombo que o `_cabelo` ja levou neste projeto. O `SpriteDeAura` pagou o mesmo erro pelo outro
	/// lado (era `ZIndex = -4` e a aura sumia debaixo do chao; o conserto foi voltar pra 0).
	///
	/// Com z 0 a nuvem herda o Y-sort do corpo -- ela anda com ele na profundidade da cena, que e o
	/// certo: um lutador atras de uma arvore tem a aura atras da arvore tambem.
	/// ==================================================================================
	/// </summary>
	private const int Camada = 0;

	/// <summary>O lado do quad em pixels de mundo, ja derivado da silhueta. Ver <see cref="ModelarPeloCorpo"/>.</summary>
	private int _lado = 42;

	/// <summary>O centro da elipse no espaco do corpo (o Y do meio da silhueta). Ver <see cref="ModelarPeloCorpo"/>.</summary>
	private int _centro;

	private Sprite2D _quad = null!;
	private ShaderMaterial _tinta = null!;

	/// <summary>
	/// A NUVEM ESTA LIGADA? Lida do UNIFORM e nao de um campo guardado -- mesmo motivo do
	/// <see cref="RaiosDaForma.CorDeTeste"/>: escrever num uniform que nao existe e SILENCIOSO no
	/// Godot, entao um campo responderia o que <see cref="Definir"/> pediu e nao o que o shader
	/// recebeu. Pra bancada.
	/// </summary>
	public float ForcaDeTeste => (float)_tinta.GetShaderParameter("forca");

	/// <summary>O lado do quad em pixels de mundo, ja com a escala aplicada. Pra bancada.</summary>
	public float LadoDeTeste => _quad.Scale.X;

	/// <summary>
	/// O `ZIndex` DO QUE DESENHA, lido do <c>Sprite2D</c> e nao do <see cref="Camada"/>. Pra bancada.
	///
	/// Ela e a OUTRA PONTA do pedido do dono: a nuvem por cima do corpo (que e ordem de irmao) e ainda
	/// assim ATRAS do cenario (que e este zero -- z vence Y-sort). Ler o const responderia o que eu quis;
	/// ler o node responde o que esta desenhando, que e o que a arvore pode ter mudado desde o `_Ready`.
	/// </summary>
	public int CamadaDeTeste => _quad.ZIndex;

	/// <summary>
	/// A FOLGA DESENHADA, em pixels de mundo, por eixo -- (lateral, vertical). Pra bancada, e ela e a
	/// unica linha que responde a pergunta que o dono fez: "no MAXIMO 5 pixels de distancia do corpo".
	///
	/// Sai dos MESMOS uniforms que o shader le, e nao dos campos que os escreveram: e a diferenca
	/// entre a elipse da aura e a elipse da silhueta, em pixel. Um erro de conversao pra UV apareceria
	/// aqui; um numero guardado em campo responderia o que eu quis, e nao o que o shader recebeu.
	/// </summary>
	public Vector2 FolgaDeTeste => SemiEixosDeTeste - SilhuetaDeTeste;

	/// <summary>
	/// OS SEMI-EIXOS DA ELIPSE DA NUVEM em pixels de mundo -- (lateral, vertical). Pra bancada.
	///
	/// Ela e a metade que a <see cref="FolgaDeTeste"/> nao mostra, e sem ela a bancada nao consegue
	/// perguntar **se o quad cresceu**: a folga e uma DIFERENCA entre as duas elipses, e as duas iam
	/// juntas num quad de qualquer tamanho -- inclusive no de 96 px que o dono reclamou. Ver
	/// `RoboDeNebulosa.AFolgaCabe`, que cobra `2 x maior semi-eixo == lado do quad`.
	/// </summary>
	public Vector2 SemiEixosDeTeste => (Vector2)_tinta.GetShaderParameter("raio") * _quad.Scale.X;

	/// <summary>
	/// A SILHUETA MEDIDA que o shader recebeu, em pixels de mundo -- (meia-largura, meia-altura). Pra
	/// bancada, e ela e o que permite conferir que o tamanho da nuvem foi DERIVADO do boneco: a bancada
	/// remede a caixa do alfa da folha por conta propria e compara com este numero. Um lado cravado no
	/// `.cs` reprova ali. Ver <see cref="Silhueta"/>.
	/// </summary>
	public Vector2 SilhuetaDeTeste => (Vector2)_tinta.GetShaderParameter("raio_do_corpo") * _quad.Scale.X;

	/// <summary>
	/// O CENTRO DA ELIPSE no espaco do corpo. Pra bancada: o quad e centrado na SILHUETA, e nao na
	/// origem do node, entao o meio dele nao cai no pe do personagem -- ver <see cref="ModelarPeloCorpo"/>.
	/// </summary>
	public float CentroDeTeste => _centro;

	/// <summary>
	/// Onde o quad esta DE VERDADE no mundo. Pra bancada, e ela nasceu de uma foto: a nuvem apareceu
	/// desencostada do corpo e nao havia como saber, olhando, se quem estava fora do lugar era o node,
	/// o quad ou a mascara do shader. Esta linha separa os tres.
	/// </summary>
	/// <summary>
	/// O CENTRO do quad no mundo -- e centro, nao `GlobalPosition`, desde que a ancora foi pro canto
	/// (ver `_Ready`). Ler o `GlobalPosition` aqui devolveria o canto superior esquerdo e a bancada
	/// acusaria "o quad esta 48 px fora do corpo" justamente depois do conserto que o centrou.
	///
	/// Sai da transformada aplicada ao meio do retangulo local (0..1), entao acompanha escala e
	/// rotacao sozinha em vez de somar meio lado na mao.
	/// </summary>
	public Vector2 PosicaoDoQuadDeTeste => _quad.GetGlobalTransform() * new Vector2(0.5f, 0.5f);

	/// <summary>
	/// O RETANGULO DO QUAD EM PIXEL DE TELA, ja com camera e zoom aplicados. Pra bancada, e ela
	/// nasceu de uma conta minha que nao fechava: eu deduzia esse retangulo multiplicando o lado pelo
	/// zoom que eu ACHAVA que estava valendo, e a foto nao batia com a deducao de jeito nenhum.
	/// Perguntar pra a transformada e a unica resposta que nao depende do que eu acho.
	/// </summary>
	public Rect2 RetanguloNaTelaDeTeste
	{
		get
		{
			Transform2D t = _quad.GetGlobalTransformWithCanvas();
			// O retangulo local vai de 0 a 1 (ancora no canto -- ver `_Ready`), entao o meio dele e
			// (0,5, 0,5) passado pela transformada. Esta conta ASSUMIA o quad centrado e por isso
			// respondia "192x192 com centro no corpo" enquanto a foto mostrava a nuvem 51 px fora --
			// era a bancada confirmando a suposicao de quem a escreveu em vez de medir o desenho.
			Vector2 meio = t * new Vector2(0.5f, 0.5f);
			Vector2 meiaExtensao = (t.X.Abs() + t.Y.Abs()) * 0.5f;
			return new Rect2(meio - meiaExtensao, meiaExtensao * 2f);
		}
	}

	/// <summary>
	/// MODO DIAGNOSTICO: pinta a mascara CHAPADA, sem rampa de alfa e sem particula. Nao e enfeite de
	/// bancada -- e o unico jeito de a foto responder "onde este quad esta desenhando?", que foi
	/// justamente o que eu nao consegui responder olhando: sobre grama, a nuvem de verdade muda a cor
	/// media do anel em 2 unidades de 255, e um efeito assim nao tem contorno pra se localizar.
	///
	/// Ele escreve UNIFORMS e nao troca o shader: o que se ve chapado e exatamente a mesma elipse, no
	/// mesmo lugar, que o modo normal desenha translucida.
	/// </summary>
	public void DiagnosticoChapado(bool ligado)
	{
		Afinar("opacidade", ligado, 1.0f);
		Afinar("atenua_escuro", ligado, 1.0f);
		Afinar("curva_alfa", ligado, 0.001f);
		Afinar("pontos_brilho", ligado, 0.0f);
	}

	/// <summary>
	/// Escreve um uniform enquanto o diagnostico esta ligado e o DEVOLVE AO PADRAO DO SHADER quando
	/// sai -- passar `default` remove a sobrescrita, e a paleta volta a valer a do arquivo.
	///
	/// ============================ POR QUE NAO SE ESCREVE O VALOR DE VOLTA ============================
	/// A primeira versao destes metodos repunha os padroes a mao ("opacidade de volta pra 0,85"), e
	/// isso e uma copia dos numeros do `.gdshader` morando num `.cs`. Na recalibragem seguinte os
	/// padroes mudaram -- `opacidade` foi a 0,95, `curva_alfa` a 0,85 -- e estas linhas teriam
	/// silenciosamente REBAIXADO a nuvem de volta pros valores velhos toda vez que a bancada saisse do
	/// modo diagnostico. O jogo ficaria certo, e a foto da bancada (a unica que alguem olha) sairia
	/// com a arte antiga.
	///
	/// Pior ainda: o dono afina esses numeros editando o `.gdshader` SEM recompilar (e a propriedade
	/// que a bancada do `--diagforma` protege de proposito). Um padrao copiado aqui desfaria o ajuste
	/// dele sem aviso.
	/// ============================================================================================
	/// </summary>
	private void Afinar(string uniform, bool ligado, float valor)
	{
		if (ligado) _tinta.SetShaderParameter(uniform, valor);
		else _tinta.SetShaderParameter(uniform, default);
	}

	/// <summary>
	/// MODO MOLDURA: pinta o QUAD INTEIRO, mascara e tudo. Serve pra uma pergunta so, e ela apareceu
	/// quando a elipse chapada saiu fora da caixa que o C# dizia que o quad tinha: "onde este
	/// retangulo esta, afinal?".
	///
	/// Um raio gigante com queda quase dura deixa a mascara valendo 1 em cada pixel do quad, entao o
	/// que a foto mostra e a borda do RETANGULO -- e comparar essa borda com a elipse do modo chapado
	/// diz, sem conta nenhuma, se o problema e o quad estar fora do lugar ou a elipse estar fora do
	/// quad.
	/// </summary>
	public void DiagnosticoMoldura(bool ligado)
	{
		DiagnosticoChapado(ligado);

		// ============================ AQUI O `default` VIROU O ERRADO ============================
		// A moldura infla as DUAS elipses (a da aura e a do corpo) pra alem do quad: com a silhueta
		// engolindo o retangulo inteiro, `dist_corpo < 1` em todo pixel e a mascara sai cheia -- e o que
		// a foto mostra e a borda do RETANGULO. A `suavidade` que este metodo apagava nao existe mais: a
		// queda da nuvem passou a ser a propria folga, ou seja geometria, e inflar as duas elipses juntas
		// ja a some.
		//
		// E SAIR DAQUI NAO PODE DEVOLVER O PADRAO DO SHADER. Nos outros uniforms `default` e o certo (o
		// dono afina a paleta editando o `.gdshader`, e um valor copiado pro `.cs` desfaria o ajuste
		// dele). Estes dois nao sao arte: sao a silhueta MEDIDA deste corpo, e o padrao do arquivo e a
		// silhueta de um boneco de 32 qualquer. Devolver o padrao aqui poria a nuvem no tamanho errado
		// pra sempre depois do primeiro diagnostico -- e calada.
		// ==========================================================================================
		if (ligado)
		{
			_tinta.SetShaderParameter("raio", new Vector2(9f, 9f));
			_tinta.SetShaderParameter("raio_do_corpo", new Vector2(8.9f, 8.9f));
		}
		else ModelarPeloCorpo();
	}

	/// <summary>
	/// ESCREVE UM UNIFORM QUALQUER; `null` DEVOLVE O PADRAO DO ARQUIVO. Pra bancada, e o publico do
	/// <see cref="Afinar"/> -- as mesmas duas linhas, com o mesmo cuidado de nunca copiar um padrao do
	/// `.gdshader` pra dentro de um `.cs` (o bloco do `Afinar` explica por que isso custou caro).
	///
	/// ============================ POR QUE A BANCADA PRECISA DISTO ============================
	/// "A densidade das microparticulas e AFINAVEL" e um requisito que nao da pra provar lendo o
	/// arquivo: `code.Contains(" pontos_densidade ")` prova que a PALAVRA existe, e nao que girar o
	/// botao muda alguma coisa na tela. Um uniform declarado e nunca lido passaria nessa leitura --
	/// que e a mesma familia de defeito dos 35 atlas escritos e nunca importados.
	///
	/// Com esta porta a bancada gira o botao pros DOIS extremos e conta os pixels brancos nas duas
	/// fotos. Se a contagem nao se mexer, o botao nao existe de verdade.
	/// ======================================================================================
	/// </summary>
	public void AfinarDeTeste(string uniform, float? valor)
	{
		if (valor is { } v) _tinta.SetShaderParameter(uniform, v);
		else _tinta.SetShaderParameter(uniform, default);
	}

	/// <summary>
	/// A ASSINATURA DO MATERIAL: TODO uniform do shader com o valor que ESTE corpo esta usando agora,
	/// em ordem alfabetica. Pra bancada, e ela existe pra uma pergunta so -- **os dois estagios do
	/// Ultra Instinto mostram o MESMO overlay?**
	///
	/// ============================ POR QUE NAO BASTA COMPARAR O `forca` ============================
	/// `forca == 1` nos dois estagios prova que a nuvem ACENDE nos dois, e nao que ela e a mesma. No
	/// dia em que alguem quiser "o Perfected um pouco mais azul" o caminho mais curto e escrever
	/// `cor_perto` (ou `opacidade`, ou `pontos_densidade`) do lado do cliente conforme a `Ordem` -- e
	/// nenhuma checagem de `forca` veria isso. A palavra do dono e do DM (`UltraInstinct.dm:479`, que
	/// veste os dois overlays sem olhar o estagio) e que os dois sao IGUAIS.
	///
	/// A assinatura pega qualquer ramificacao dessas, porque ela nao sabe quais uniforms existem: ela
	/// pergunta ao SHADER a lista e le todos. Um uniform novo entra na comparacao sozinho.
	///
	/// A `semente` fica de fora, e ela e a unica: ela e por CORPO de proposito (ver `_Ready`) --
	/// compara-la seria cobrar que dois lutadores desenhem a mesma voluta no mesmo quadro, que e
	/// exatamente o estroboscopio sincronizado que ela existe pra evitar.
	/// ========================================================================================
	/// </summary>
	public string AssinaturaDeTeste()
	{
		var partes = new List<string>();
		foreach (Variant item in _tinta.Shader.GetShaderUniformList())
		{
			string nome = item.AsGodotDictionary()["name"].AsString();
			if (nome == "semente") continue;
			partes.Add($"{nome}={_tinta.GetShaderParameter(nome)}");
		}
		partes.Sort(StringComparer.Ordinal);
		return string.Join(";", partes);
	}

	public override void _Ready()
	{
		_tinta = new ShaderMaterial { Shader = GD.Load<Shader>(CaminhoDoShader) };

		_quad = new Sprite2D
		{
			Name = "Quad",
			// ============================ UM PIXEL BRANCO ESTICADO ============================
			// O shader nao amostra `TEXTURE` uma vez sequer: a nuvem inteira e gerada. A textura
			// existe so pra o `Sprite2D` ter um QUAD com UV de 0 a 1 -- e 1x1 esticado e a forma
			// mais barata de conseguir isso (nao ocupa VRAM e nao passa por importacao, entao nao
			// pode repetir o tombo dos 35 atlas que estavam no disco e o Godot nunca importou).
			//
			// ============================ E O `Centered` NAO SERVE PRA UMA TEXTURA DE 1x1 ============================
			// Aqui estava o padrao (`Centered = true`) e a FOTO mostrou o estrago: a nuvem desenhava
			// inteira PRA BAIXO E PRA DIREITA do corpo, desencostada dele. Medido na foto do modo
			// chapado: a elipse saiu com o tamanho exato que devia (76x88 px de mundo, contra 77x90 de
			// projeto) e com o centro deslocado em +51,+47 -- ou seja, MEIO QUAD, e nao um erro de
			// mascara. O corpo ficava fora da propria aura.
			//
			// A causa e a soma de duas coisas certas. `Centered` desloca a arte em
			// `-tamanho_da_textura / 2`, e numa textura de 1x1 isso e MEIO PIXEL. E este projeto liga
			// `snap_2d_vertices_to_pixel` (project.godot:42), como todo jogo de pixel art liga. Meio
			// pixel nao sobrevive a um arredondamento pra pixel inteiro: o deslocamento some e o quad
			// passa a ocupar 0..+96 em vez de -48..+48. O erro nasce de meio pixel e chega na tela
			// multiplicado pela escala de 96, que e aplicada depois.
			//
			// O conserto nao e desligar o snap (ele e do jogo inteiro) nem inchar a textura pra 96x96
			// (36 KB de VRAM pra nao amostrar nem um texel). E parar de pedir o centro em TEXEL, que e
			// fracionario, e pedi-lo em PIXEL DE MUNDO: ancora no canto e o node em `-lado/2`, que e
			// INTEIRO (o `ModelarPeloCorpo` arredonda de proposito) e atravessa qualquer arredondamento
			// intacto.
			// ================================================================================================
			Texture = QuadBranco(),
			Centered = false,
			Material = _tinta,
			ZIndex = Camada,
			Visible = false,
		};
		AddChild(_quad);

		// ============================ CADA CORPO COM A PROPRIA NUVEM ============================
		// `TIME` e global no shader. Sem semente, dois lutadores em Ultra Instinto lado a lado
		// desenhariam a MESMA voluta no mesmo quadro -- o estroboscopio sincronizado que o
		// `RaiosDaForma` inteiro existe pra evitar, do outro lado do mesmo problema.
		//
		// A semente sai do id da INSTANCIA e nao de um sorteio: e estavel enquanto o corpo viver
		// (a nuvem nao "pula" quando alguem troca de degrau) e nao gasta um gerador por corpo.
		_tinta.SetShaderParameter("semente", GetInstanceId() % 997 * 0.113f);

		// A GEOMETRIA JA SAI DAQUI COM VALOR, e nao so quando alguem acender: a bancada le
		// <see cref="LadoDeTeste"/> e <see cref="FolgaDeTeste"/> num corpo que nunca se transformou, e
		// um quad de escala zero devolveria "folga 0 px" -- um verde vazio. Aqui o boneco ainda nao tem
		// folha (este node nasce antes do `VestirCorpoInteiro`), entao vale o fallback; o
		// <see cref="Definir"/> remede quando a forma acende.
		ModelarPeloCorpo();
	}

	/// <summary>
	/// ============================ A ELIPSE E MEDIDA DO CORPO, NAO ESCRITA ============================
	/// O pedido do dono e uma DISTANCIA ("no MAXIMO 5 pixels de distancia do corpo"), e distancia
	/// precisa de um corpo pra ser medida. Entao aqui nao ha tamanho de nuvem: ha a silhueta do quadro
	/// que o boneco esta vestindo agora, mais <see cref="Folga"/>.
	///
	///   silhueta (caixa do alfa)  ->  `raio_do_corpo`
	///   silhueta + Folga          ->  `raio`
	///   quad                      ->  2 x o maior dos dois semi-eixos, CENTRADO na silhueta
	///
	/// A elipse passa pelos meios dos lados da caixa, entao ela corta as quinas -- que e o certo pra um
	/// boneco em pe: o ponto mais alto (a cabeca) e estreito e centrado, o mais largo (os bracos) fica
	/// na meia altura. Uma elipse que circunscrevesse a caixa inteira sobraria ~40% em cada diagonal, e
	/// a queixa era justamente sobra.
	///
	/// ============================ TRES CUIDADOS QUE PARECEM DETALHE E NAO SAO ============================
	///   1. TUDO INTEIRO. O `Position` do quad e o meio-lado precisam atravessar o
	///      `snap_2d_vertices_to_pixel` (`project.godot:42`) sem virar meio pixel -- e meio pixel aqui
	///      sai multiplicado pela escala do quad, que e o tombo do `Centered` que este arquivo ja pagou
	///      uma vez. Por isso `Mathf.CeilToInt` nos dois semi-eixos e lado sempre PAR.
	///   2. O QUAD E CENTRADO NA SILHUETA, e nao na origem do node. Os pes ficam em +16
	///      (`SpriteDeAura.LinhaDosPes`) e a cabeca em -16 num quadro de 32, entao o meio cai em 0 --
	///      mas num Oozaru de 96 ele cai em -32. Centrar no node deixaria a nuvem 32 px enterrada, que e
	///      o mesmo erro que o `CharacterVisual` conserta com `(alturaBase - alturaDele)/2`.
	///   3. NADA DE `centro` NO SHADER. Com o quad centrado na elipse, o meio do quad E o meio dela --
	///      um lugar so pra a mesma verdade. O vies pra cima que morava la (`centro.y = 0.55`) existia
	///      porque sobravam 30 px de nuvem abaixo dos pes; com 5 px de folga nao ha o que enviesar.
	/// ================================================================================================
	/// </summary>
	private void ModelarPeloCorpo()
	{
		(float meiaLargura, float meiaAltura, float centro) = Silhueta();

		// ARREDONDA PRA BAIXO, e isso e o pedido e nao gosto: "no MAXIMO 5 pixels". A caixa do alfa pode
		// dar meio pixel (altura impar), e `Ceil` devolveria uma folga de 5,5 px -- passando do teto por
		// arredondamento, que e a maneira mais boba de descumprir um numero que alguem mediu na tela.
		// Pra baixo a folga cai entre 4 e 5 px, sempre dentro.
		int rx = Mathf.Max(Mathf.FloorToInt(meiaLargura + Folga), 2);
		int ry = Mathf.Max(Mathf.FloorToInt(meiaAltura + Folga), 2);
		_lado = 2 * Mathf.Max(rx, ry);
		_centro = Mathf.RoundToInt(centro);

		_quad.Position = new Vector2(-_lado / 2f, _centro - _lado / 2f);
		_quad.Scale = new Vector2(_lado, _lado);

		// ============================ O LADO VAI PRO SHADER, E NAO E DUPLICADO LA ============================
		// As microparticulas tem tamanho em PIXELS ("~1 px", da referencia) e o shader trabalha em UV --
		// a conversao precisa do lado do quad. Escrever daqui mantem UM lado no projeto: este, que ja e
		// quem escala o `Sprite2D`. Cravado no shader seriam dois numeros iguais em arquivos diferentes,
		// e o dia em que a nuvem mudasse de tamanho os pontinhos encolheriam sozinhos, sem ninguem
		// entender por que -- que e literalmente o que aconteceria agora, porque o lado deixou de ser 96.
		// ====================================================================================================
		_tinta.SetShaderParameter("lado_do_quad", (float)_lado);

		// AS DUAS ELIPSES EM UV. O quad vai de 0 a 1, entao meio quad e 0,5 -- dividir o semi-eixo pelo
		// LADO (e nao pelo meio-lado) ja da a fracao que o shader espera.
		//
		// A DO CORPO VAI COM A MEDIDA CRUA, e nao com `raio - Folga`: ela e quem diz ao shader onde a
		// pele acaba (o `dist_corpo == 1.0` que decide o veu e o comeco da rampa), e derivar isso do
		// raio ja arredondado poria a fronteira meio pixel dentro do boneco. Assim a folga desenhada e
		// literalmente a diferenca entre as duas -- que e o que a bancada le em `FolgaDeTeste`.
		_tinta.SetShaderParameter("raio", new Vector2(rx / (float)_lado, ry / (float)_lado));
		_tinta.SetShaderParameter("raio_do_corpo",
								  new Vector2(meiaLargura / _lado, meiaAltura / _lado));
	}

	/// <summary>
	/// A CAIXA DO DESENHO do corpo PARADO: meia largura, meia altura e o Y do meio, tudo no espaco do
	/// personagem.
	///
	/// ============================ POR QUE MEDIR O ALFA, E NAO O QUADRO ============================
	/// O quadro do corpo e 32x32 e o desenho dentro dele NAO e: a silhueta em pe ocupa ~16 de largura e
	/// comeca 6 px abaixo do topo (medido nas folhas base). Uma folga de 5 px contada da borda do QUADRO
	/// seria 13 px de folga do BONECO nos lados e 11 acima da cabeca -- ou seja o mesmo defeito da
	/// versao antiga, de novo, so que menor. O dono falou do CORPO.
	///
	/// Medir tambem e o que faz a regra valer pra outra folha sem ninguem recalibrar: o Oozaru enche bem
	/// mais que a metade do quadro de 96 dele, e um `largura * 0.5` daria uma elipse estreita demais.
	///
	/// ============================ E POR QUE O QUADRO PARADO, E NAO O QUE ESTA TOCANDO ============================
	/// A primeira versao mediu o quadro CORRENTE, e a foto no jogo cobrou: a nuvem saiu com ~8 px de
	/// folga lateral em vez de 5. A causa e que "o quadro corrente" na hora de acender e o que a
	/// transformacao estiver tocando -- e as folhas base tem poses que enchem o quadro inteiro (a `sp` e
	/// as quatro do `take_off` do `BaseTanMale` sao 32x32 de alfa CHEIO). Medir uma delas devolve a caixa
	/// do QUADRO com outro nome, e o tamanho da aura passa a depender de que animacao estava no ar
	/// naquele instante -- uma aura que muda de tamanho por sorteio.
	///
	/// A silhueta que o dono ve na captura dele e a do boneco EM PE, que neste projeto e o quadro 0 do
	/// ciclo de caminhada (as folhas base nao tem `default_*`; parado e o `walk_*` travado no quadro 0
	/// pelo meta `sync` -- ver `CharacterVisual.NovaCamada`). Sao quatro quadros, um por direcao, e
	/// entram pela UNIAO: virar de lado nao pode mudar o tamanho da aura.
	/// ========================================================================================================
	///
	/// ============================ E O CENTRO HORIZONTAL CONTINUA SENDO ZERO ============================
	/// Verticalmente a caixa manda (a cabeca tem linha vazia acima, e os pes sao a base --
	/// `SpriteDeAura.AncoraPara` e a regra de sempre). Horizontalmente NAO: usar o meio da caixa medida
	/// deixaria a nuvem seguir a assimetria de um quadro (um braco esticado pra um lado). A arte ja e
	/// centrada no quadro (`AncoraPara` diz o mesmo), entao o que se mede e o ALCANCE: o maior dos lados.
	/// ================================================================================================
	/// </summary>
	private (float MeiaLargura, float MeiaAltura, float Centro) Silhueta()
	{
		SpriteFrames? folha = GetParent()?.GetNodeOrNull<CharacterVisual>("Visual")?.FolhaDoCorpo;
		Texture2D[] parados = QuadrosParados(folha);

		float largura = parados.Length > 0 ? parados[0].GetWidth() : 32f;
		float altura = parados.Length > 0 ? parados[0].GetHeight() : 32f;

		// O topo do quadro no espaco do personagem: a folha e `Centered` com a ancora da
		// `SpriteDeAura.AncoraPara`, ou seja o meio dela cai em `LinhaDosPes - altura/2`.
		float meioDoQuadro = SpriteDeAura.AncoraPara(altura).Y;
		float topo = meioDoQuadro - altura * 0.5f;
		(float, float, float) semMedida =
			(largura * FracaoDaSilhuetaSemMedida * 0.5f, altura * 0.5f, meioDoQuadro);

		int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue;

		// UMA IMAGEM POR ATLAS, e nao uma por quadro: os quatro quadros parados moram na MESMA folha, e
		// `AtlasTexture.GetImage()` recorta a regiao decodificando o atlas inteiro toda vez. Sem o cache
		// seriam quatro decodificacoes da folha pra ler 4 KB dela.
		var imagens = new Dictionary<Texture2D, Image>();
		foreach (Texture2D quadro in parados)
		{
			Texture2D dono = quadro is AtlasTexture at && at.Atlas != null ? at.Atlas : quadro;
			if (!imagens.TryGetValue(dono, out Image? img))
			{
				if (dono.GetImage() is not { } lida) continue;
				img = lida;
				imagens[dono] = img;
			}

			Rect2I regiao = quadro is AtlasTexture a2 && a2.Atlas != null
				? new Rect2I((Vector2I)a2.Region.Position, (Vector2I)a2.Region.Size)
				: new Rect2I(0, 0, img.GetWidth(), img.GetHeight());

			for (int y = 0; y < regiao.Size.Y; y++)
				for (int x = 0; x < regiao.Size.X; x++)
				{
					// PIXEL ART TEM ALFA QUASE BINARIO -- o corte baixo pega tambem a franja antialiasada
					// que alguma folha convertida do `.dmi` trouxe, e franja tambem e silhueta.
					if (img.GetPixel(regiao.Position.X + x, regiao.Position.Y + y).A < 0.1f) continue;
					if (x < x0) x0 = x;
					if (x > x1) x1 = x;
					if (y < y0) y0 = y;
					if (y > y1) y1 = y;
				}
		}

		// NADA MEDIDO (sem folha, ou uma pose inteira transparente): cai no fallback em vez de devolver
		// uma elipse de tamanho negativo.
		if (x1 < x0 || y1 < y0) return semMedida;

		// `+1` nos dois maximos porque a caixa vai ate o FIM do pixel aceso, e nao ate o comeco dele.
		float meiaLargura = Mathf.Max(largura * 0.5f - x0, x1 + 1f - largura * 0.5f);
		float cima = topo + y0;
		float baixo = topo + y1 + 1f;
		return (meiaLargura, (baixo - cima) * 0.5f, (cima + baixo) * 0.5f);
	}

	/// <summary>
	/// OS QUADROS DO BONECO PARADO -- o quadro 0 de cada `walk_*`, um por direcao. Ver o bloco de
	/// <see cref="Silhueta"/> que explica por que e este e nao o quadro que esta tocando.
	///
	/// Sem `walk_*` na folha (uma criatura com uma animacao so, por exemplo) vale o quadro 0 da primeira
	/// animacao que existir: continua sendo "a pose de descanso desta folha", que e o que se procura.
	/// </summary>
	private static Texture2D[] QuadrosParados(SpriteFrames? folha)
	{
		if (folha == null) return [];

		var achados = new List<Texture2D>();
		foreach (StringName nome in folha.GetAnimationNames())
			if (nome.ToString().StartsWith("walk_", StringComparison.Ordinal)
			 && folha.GetFrameCount(nome) > 0 && folha.GetFrameTexture(nome, 0) is { } t)
				achados.Add(t);

		if (achados.Count > 0) return [.. achados];

		foreach (StringName nome in folha.GetAnimationNames())
			if (folha.GetFrameCount(nome) > 0 && folha.GetFrameTexture(nome, 0) is { } t)
				return [t];

		return [];
	}

	/// <summary>
	/// Acende ou apaga a nebulosa. Chamado dos dois caminhos que vestem uma forma -- o
	/// `Transformacao.Vestir` (com cinematica) e o `World.AcenderFormaNoCorpo` (sem) --, sempre com
	/// a resposta do <see cref="Jandirus.Core.Forms.Catalogo.TemNebulosa"/>.
	///
	/// O `Visible` DESLIGA O DESENHO DE VERDADE, e nao so o alfa: com `forca = 0` o shader ainda
	/// seria chamado pra cada um dos ~1800 pixels do quad, por corpo, por quadro -- pra devolver
	/// transparente. Quase ninguem no jogo esta em Ultra Instinto; o custo tem que ser zero pra
	/// esse "quase todo mundo".
	///
	/// ============================ E E AQUI QUE A ELIPSE E MEDIDA ============================
	/// Acender e o unico instante em que o corpo comprovadamente ja vestiu a forma -- e a folha do
	/// boneco pode ter mudado desde a ultima vez (o SSJ4 troca o corpo inteiro, o Oozaru troca o quadro
	/// de 32 por um de 96). Remedir aqui e o que faz a folga de <see cref="Folga"/> px continuar sendo
	/// 5 px em qualquer folha, em vez de 5 px na folha que estava carregada quando o node nasceu.
	///
	/// Custa a leitura dos quatro quadros parados (4096 pixels num corpo normal, de uma folha so) por
	/// transformacao. Por quadro de render seria caro; por transformacao e menos que o pacote que chegou
	/// pra pedi-la.
	/// ====================================================================================
	/// </summary>
	public void Definir(bool ligado)
	{
		if (ligado) ModelarPeloCorpo();
		_tinta.SetShaderParameter("forca", ligado ? 1f : 0f);
		_quad.Visible = ligado;
	}

	/// <inheritdoc cref="_Ready"/>
	private static ImageTexture QuadBranco()
	{
		Image img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		img.Fill(Colors.White);
		return ImageTexture.CreateFromImage(img);
	}
}
