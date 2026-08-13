using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O corpo visivel de um personagem: uma PILHA DE CAMADAS que consomem os SpriteFrames que o
/// Tools/AssetPipeline gerou a partir dos .dmi.
///
///     corpo  ->  roupa (ate 4)  ->  cabelo  ->  olhos
///
/// DUAS COISAS QUE PARECEM DETALHE E NAO SAO:
///
/// 1) AS CAMADAS ANDAM TRAVADAS NO MESMO QUADRO. Cada camada tem a folha completa (o .dmi de
///    roupa e de cabelo tem os mesmos estados do corpo), mas deixar cada AnimatedSprite2D
///    tocar sozinha faz elas comecarem em instantes diferentes e SAIREM DE FASE -- a camisa
///    andando fora do passo do corpo. So o CORPO toca; as outras copiam o quadro dele. E o
///    que o BYOND garantia com `VIS_INHERIT_ICON_STATE`.
///
/// 2) COR E SOMADA, NAO MULTIPLICADA. `modulate` multiplica, e os sprites JA VEM COLORIDOS:
///    multiplicar cabelo preto por preto da preto CHAPADO, sem os realces -- o cabelo vira
///    uma silhueta. O jogo usava `ICON_ADD`, que clareia sem apagar o desenho. Aqui isso e um
///    shader de 3 linhas, e cor nula = nao mexe em nada.
///
/// COMO O BYOND ANIMAVA: o estado de nome vazio (aqui "default") tem `movement = 1`, ou seja,
/// ele SO roda enquanto o personagem anda; parado, mostra o primeiro quadro. Os outros
/// estados (meditar, treinar, socar, voar) rodam sozinhos.
/// </summary>
public partial class CharacterVisual : Node2D
{
	/// <summary>
	/// O SHADER DE TODA CAMADA DO PERSONAGEM: a tinta do BYOND, os efeitos de impacto e as
	/// FERIDAS. Um arquivo `.gdshader` de verdade, e nao uma string aqui dentro.
	///
	/// ============================ POR QUE ARQUIVO, E NAO CONSTANTE ============================
	/// Ele era uma `const string` compilada junto. Funcionava -- e cobrava um preco alto pra quem
	/// AJUSTA o efeito: cada meio ponto de intensidade de sangue custava recompilar o C# e reabrir
	/// o jogo. Um efeito procedural nao se acerta lendo codigo, se acerta arrastando o valor e
	/// OLHANDO; foi assim que o dono acertou o do jogo anterior, e ele pediu o mesmo aqui.
	///
	/// Como `.gdshader`, o editor do Godot abre, mostra os uniformes num painel e a previa
	/// atualiza enquanto se arrasta -- exatamente o print que ele mandou de referencia.
	/// ==========================================================================================
	///
	/// CARREGADO UMA VEZ e compartilhado por todas as camadas de todos os personagens; cada
	/// camada tem so o seu `ShaderMaterial`, com os seus valores.
	/// </summary>
	private const string CaminhoDoShader = "res://Assets/Shaders/Personagem.gdshader";

	private static Shader? _shaderTinta;

	private static Shader ShaderTinta => _shaderTinta ??= Carregar();

	private static Shader Carregar()
	{
		if (ResourceLoader.Load<Shader>(CaminhoDoShader) is { } sh) return sh;

		// SEM O ARQUIVO O PERSONAGEM NAO E DESENHADO DIREITO -- a tinta e o que da cor de pele e
		// de cabelo. Um shader vazio deixaria o boneco na cor do arquivo bruto, e sem esta linha
		// ninguem saberia por que.
		GD.PushError($"[visual] nao achei {CaminhoDoShader} -- o personagem vai sair sem tinta nem ferida");
		return new Shader { Code = "shader_type canvas_item;" };
	}

	private readonly List<AnimatedSprite2D> _camadas = [];
	private AnimatedSprite2D? _corpo;

	/// <summary>
	/// A ANIMACAO QUE ESTA TOCANDO no corpo agora. So pras bancadas.
	///
	/// Existe porque "o personagem trocou pro sprite de voo?" nao se responde por foto: no teto o
	/// boneco tem tres pixels de tela. O nome da animacao responde sem ambiguidade.
	/// </summary>
	public string AnimacaoDeTeste => _corpo?.Animation.ToString() ?? "";
	private AnimatedSprite2D? _cabelo, _olhos, _rabo;
	private readonly List<AnimatedSprite2D> _roupa = [];

	/// <summary>
	/// O sprite do RABO. E uma folha completa (walk/train/flight/ko nas quatro direcoes),
	/// entao ele anda travado no mesmo quadro do corpo como qualquer outra camada.
	/// </summary>
	public const string SpriteDoRabo = "res://Assets/Sprites/Clothes/Tail.tres";

	/// <summary>Este personagem tem rabo AGORA (o servidor manda; arrancar tira na hora).</summary>
	private bool _temRabo;

	private Facing _facing = Facing.South;
	private bool _moving;
	private string _state = "default";

	/// <summary>
	/// A FAMILIA DE POSE do momento. O ciclo de caminhada e um estado SEPARADO do parado --
	/// no .dmi sao dois estados de nome vazio, um marcado com `movement = 1` (a caminhada) e
	/// outro sem (a pose parada, que tem animacao propria: no corpo sao 4 quadros com o
	/// ultimo segurando 30 decimos, uma respiracao). O BYOND trocava entre os dois sozinho
	/// conforme o mob andava, e e o que se faz aqui.
	/// </summary>
	private string Familia() => _state == "default" ? (_moving ? "walk" : "default") : _state;

	public override void _Ready()
	{
		Garantir();
		Aplicar(force: true);
	}

	/// <summary>A camada do corpo tem que existir antes de qualquer coisa ser vestida.</summary>
	private void Garantir() => _corpo ??= NovaCamada(0);

	/// <summary>
	/// Uma camada nova. <paramref name="ordem"/> e a posicao dela na PILHA do personagem
	/// (corpo 0, rabo 1, roupa 2.., cabelo 10, olhos 11) -- e nao um z_index.
	///
	/// POR QUE NAO E MAIS z_index. O mundo passou a ordenar por Y, e no Godot o z_index vence
	/// a ordenacao por Y sempre: quem esta em z 10 desenha depois de TUDO que esta em z 0,
	/// esteja onde estiver. Com o cabelo em 10 e as arvores em 0, o corpo sumia atras da
	/// arvore e o cabelo continuava aparecendo por cima dela -- o personagem virava um tufo de
	/// cabelo flutuando na copa.
	///
	/// Agora todas as camadas ficam em z 0 e a pilha e a ORDEM NA ARVORE de nodes (ver
	/// <see cref="Reordenar"/>). Assim o personagem inteiro ocupa um unico degrau de z e o Y
	/// decide, que era a intencao desde o comeco.
	/// </summary>
	private AnimatedSprite2D NovaCamada(int ordem)
	{
		var s = new AnimatedSprite2D
		{
			Centered = true,
			Material = new ShaderMaterial { Shader = ShaderTinta },
		};
		s.SetMeta("ordem", ordem);

		// A CAIXA DO QUADRO TEM QUE ACOMPANHAR A ANIMACAO.
		//
		// O shader de ferida divide o corpo em faixas (cabeca em cima, pernas embaixo) e pra isso
		// precisa do UV DO QUADRO -- mas `UV` cobre a FOLHA inteira, e cada pose mora num
		// retangulo diferente dela. Sem reenviar a caixa a cada troca de quadro, as faixas ficariam
		// travadas no primeiro quadro e a ferida escorreria pelo corpo enquanto o boneco anda.
		//
		// Pelo SINAL, e nao por quadro de render: `FrameChanged` dispara quando a pose troca de
		// verdade (~5-10 Hz), e nao 60 vezes por segundo pra reescrever o mesmo valor.
		s.FrameChanged += () => AtualizarCaixa(s);
		s.AnimationChanged += () => AtualizarCaixa(s);

		AddChild(s);
		_camadas.Add(s);
		return s;
	}

	/// <summary>Manda pro shader onde este quadro comeca e acaba dentro da folha.</summary>
	private static void AtualizarCaixa(AnimatedSprite2D s)
	{
		if (!IsInstanceValid(s) || s.Material is not ShaderMaterial m) return;
		if (s.SpriteFrames is not { } sf || s.Animation.IsEmpty) return;
		if (s.Frame < 0 || s.Frame >= sf.GetFrameCount(s.Animation)) return;

		(Vector2 min, Vector2 max) = BorraoDirecional.Caixa(sf.GetFrameTexture(s.Animation, s.Frame));
		m.SetShaderParameter("quadro_min", min);
		m.SetShaderParameter("quadro_max", max);
	}

	/// <summary>
	/// Poe as camadas na arvore na ordem da pilha.
	///
	/// Precisa ser CHAMADO, e chamado depois de toda mexida: a ordem de criacao nao e a ordem
	/// de desenho. O rabo nasce por ULTIMO (MontarRabo roda no fim de Vestir) e precisa
	/// desenhar em SEGUNDO; uma camisa vestida numa troca posterior entra no fim da arvore mas
	/// pertence ao meio da pilha. Sem isto, a ordem de desenho passaria a depender de quando o
	/// jogador trocou de roupa.
	/// </summary>
	private void Reordenar()
	{
		// a lista pode ter camada que ja saiu da arvore numa troca de aparencia
		_camadas.RemoveAll(s => !IsInstanceValid(s));
		_camadas.Sort((a, b) => a.GetMeta("ordem", 0).AsInt32() - b.GetMeta("ordem", 0).AsInt32());
		for (int i = 0; i < _camadas.Count; i++) MoveChild(_camadas[i], i);
	}

	/// <summary>
	/// Tira a camada de cena AGORA e so depois marca pra liberar.
	///
	/// `QueueFree` sozinho deixa o node na arvore ate o fim do quadro -- e o <see cref="Reordenar"/>
	/// do mesmo quadro contaria com ele nos indices, embaralhando as camadas vivas.
	/// </summary>
	private void Descartar(AnimatedSprite2D s)
	{
		_camadas.Remove(s);
		RemoveChild(s);
		s.QueueFree();
	}

	/// <summary>
	/// ============================ O CABELO, O RABO E OS OLHOS DA FORMA, NUM LUGAR SO ============================
	/// Passar `null` desfaz tudo e devolve o personagem ao que ele era.
	///
	/// (O OLHO ENTROU DEPOIS e o nome do metodo ficou -- ele e o CANAL que a bancada vigia por nome
	/// (`RoboDeForma`, "o `Vestir` continua escrevendo os seis canais"), e no DM ele tambem e uma
	/// coisa so: `AddHair()` e o `ui_apply_eyes()` saem do MESMO `Buff()`, `UltraInstinct.dm:481-482`.)
	///
	/// ============================ POR QUE UMA ENTRADA E NAO TRES CHAMADAS ============================
	/// Vestir a forma no cabelo sao TRES decisoes que tem que concordar -- qual sprite, se pinta, e o
	/// que o rabo faz --, e ate aqui elas viviam soltas em dois chamadores (`World.VestirAFormaSemCena`
	/// e `Transformacao.Vestir`) mais o beat do piscar. Duas delas ja tinham sido escritas na ordem
	/// errada uma vez (o comentario de `Transformacao.Vestir` conta o caso), e a terceira -- a tinta --
	/// nem existia.
	///
	/// Com uma entrada so, quem chama diz **qual forma** e nao **o que fazer**, e a regra
	/// (`Catalogo.ModoDoCabelo`) tem um leitor unico. Acrescentar um modo novo passa a ser uma linha
	/// no Core, e nao uma cacada por chamadores.
	/// ==========================================================================================
	/// </summary>
	public void VestirCabeloDaForma(Jandirus.Core.Forms.FormaDef? d)
	{
		// GUARDA A FORMA ANTES DE QUALQUER COISA -- ver `_formaVestida`. E o que deixa a cor do olho
		// responder a uma virada de POSSE que chegue no meio da forma, sem revestir o corpo inteiro.
		_formaVestida = d;

		Jandirus.Core.Forms.ModoDoCabelo modo = Jandirus.Core.Forms.Catalogo.ModoDoCabelo(d);

		// O SPRITE PRIMEIRO. A tinta mora no MATERIAL e o `CabeloDaForma` troca so a FOLHA, entao a
		// ordem nao muda o pixel hoje -- mas o `TrocarOuTingir` PERGUNTA se a troca aconteceu, e pra
		// isso ela precisa ja ter acontecido.
		//
		// SUFIXO VAZIO NOS MODOS QUE NAO TROCAM, e nao "nao chamar": o `CabeloDaForma("")` e o que
		// devolve o penteado do jogador (o `RemoveHair()` + `/hairs/hair` do DM). Pular a chamada
		// deixaria o cabelo da forma ANTERIOR na cabeca -- o tombo do `ussj_saved_icon`.
		bool troca = modo is Jandirus.Core.Forms.ModoDoCabelo.Trocar
						  or Jandirus.Core.Forms.ModoDoCabelo.TrocarETingir
						  or Jandirus.Core.Forms.ModoDoCabelo.TrocarOuTingir
						  or Jandirus.Core.Forms.ModoDoCabelo.TrocarERecolorir;
		// O SUFIXO SAI DO CORE E NAO DO CAMPO CRU, e a diferenca e o Grade 4: o SSJ1 a 100% de
		// maestria pede `SSjFP` em vez de `SSj` (ver `Catalogo.SufixoDoCabeloDe`). Ler `d.SufixoDoCabelo`
		// aqui era o ultimo dos dois lugares onde o pedido do `fp` morria -- o outro era o catalogo,
		// que nunca escreveu esse sufixo em entrada nenhuma.
		bool trocou = CabeloDaForma(troca
			? Jandirus.Core.Forms.Catalogo.SufixoDoCabeloDe(d, _dominouAForma) : "");

		// A TINTA. No `TrocarOuTingir` ela e ALTERNATIVA e nao acumulo -- ver o enum: quem ganhou a
		// arte propria do Ultra Instinct nao recebe prata por cima dela.
		bool pinta = modo switch
		{
			Jandirus.Core.Forms.ModoDoCabelo.Tingir           => true,
			Jandirus.Core.Forms.ModoDoCabelo.TrocarETingir    => true,
			Jandirus.Core.Forms.ModoDoCabelo.TrocarERecolorir => true,
			Jandirus.Core.Forms.ModoDoCabelo.TrocarOuTingir   => !trocou,
			_ => false,
		};
		// ============================ SOMAR OU SUBSTITUIR DEPENDE DO SPRITE, E NAO DA FORMA ============================
		// Era `modo == TrocarERecolorir` -- so o Beast em MATIZ, todo o resto em soma. Isso partia do
		// pressuposto de que a tinta sempre cai num molde ESCURO, e a soma so funciona nesse caso.
		// Medido: os penteados base sao pretos (`Hair_Goku.png` tem `#000000` como tom dominante) --
		// e ali a soma e mesmo a operacao certa, e e o `ICON_ADD` do DM.
		//
		// Mas quando a forma TROCA o sprite, o que entra e sempre a arte de Super Saiyajin, que ja e
		// DOURADA (`#948c08`, `#c6b508`, `#f7de29`, `#f7ef94` -- os mesmos quatro tons em todas as 57
		// variantes). Somar azul em dourado nao da azul: da BRANCO, e foi o defeito que o dono relatou
		// nos dois lados ao mesmo tempo ("aplicar azul por cima esta dando branco", "o cabelo do Rose
		// esta loiro"). A conta esta escrita em `Catalogo.AzulDoCabeloDivino`.
		//
		// A REGRA DERIVADA, e ela nao precisa de campo novo: **matiz quando a tinta cai num sprite que
		// a FORMA trouxe** (`trocou`), soma quando ela cai no penteado do jogador. Isso pega sozinho o
		// Blue, o Rose e as duas linhas Legendary; deixa de fora o SSG, o Ultra Ego e o Ultra Instinct
		// sem penteado proprio (que pintam a base preta e por isso somam); e o Beast continua em matiz
		// porque ele tambem troca -- o `TrocarERecolorir` deixou de ser a causa e virou consequencia.
		//
		// (A operacao pertence ao SPRITE e nao a forma, e o DM diz o mesmo: `SaiyanObjects.dm:12`
		// ESCURECE o cabelo de SSJ antes de somar, justamente porque somar naquela arte nao bastava.
		// Ver o `<remarks>` de `AzulDoCabeloDivino` pra o porque de portar aquele passo tambem nao
		// resolver -- ele troca branco por ciano.)
		// ==========================================================================================================
		TingirCabelo(pinta && Jandirus.Core.Forms.Catalogo.CorDoCabelo(d) is { } ch ? new Color(ch) : null,
					 matiz: trocou);

		PintarRabo(Jandirus.Core.Forms.Catalogo.CorDoRabo(d) is { } cr ? new Color(cr) : null);

		// ============================ OS OLHOS ============================
		// Ver `Catalogo.CorDoOlho` pra a tabela inteira. UMA linha de codigo pra nove cores, e ela nao
		// mudou quando o eixo saiu de "so o Ultra Instinct" pra "quase toda forma": a camada de olhos
		// ja existia, o canal ja existia, e "sem iris" acabou sendo mais uma COR (o branco da
		// esclerotica do corpo) e nao um estado novo -- a conta esta la.
		//
		// E POR ISSO NAO HA `Esconder` AQUI. Some-la seria o caminho obvio pra "olhos brancos, sem
		// iris" e faria o contrario do pedido: a camada e a iris, e o corpo desenha uma iris AZUL
		// embaixo dela. Esconder revelaria uma iris; pintar de `fcfdfd` e o que apaga.
		//
		// O `_semRedeas` ENTRA AQUI e nao numa segunda chamada: o branco sem iris deixou de ser a cor
		// da linha Legendary e virou a cor de um corpo que a FURIA esta dirigindo (ver
		// `Catalogo.CorDoOlho(d, semRedeas)`). Quem chega numa zona onde alguem ja esta possuido veste
		// o olho certo por esta linha, e nao por um remendo depois.
		TingirOlhos(Jandirus.Core.Forms.Catalogo.CorDoOlho(d, _semRedeas) is { } co ? new Color(co) : null);
	}

	/// <summary>
	/// ============================ A TINTA DA FORMA NO OLHO ============================
	/// Gemea da <see cref="TingirCabelo"/>, e pelo mesmo motivo: a cor da FICHA ja e uma tinta neste
	/// material (`Vestir`, na linha do `BaseDaFicha`), entao reverter tem que devolver a cor da ficha e
	/// nao "nenhuma". Sem isso, sair do Ultra Instinct deixaria os olhos de quem escolheu verde pretos
	/// pra sempre -- o mesmo tombo que o cabelo ja pagou uma vez.
	///
	/// A COR BASE E PERGUNTADA A FICHA (<see cref="BaseDaFicha"/>) e nao guardada num registro. Aqui
	/// morava uma captura preguicosa do material, uma das TRES iguais desta classe -- ver o bloco da
	/// `BaseDaFicha` pra o que elas custaram.
	///
	/// E o DM desfaz explicitamente: `ui_restore_eyes()` (`UltraInstinct.dm:310-312`) tira o overlay
	/// prateado e chama `RefreshEyes()`, que reveste o olho da ficha. Passar `null` aqui e isso.
	///
	/// E O REGISTRO PASSOU A VALER PRA QUASE TODO MUNDO. Ele nasceu pra a prata do Ultra Instinct, que
	/// era a unica cor de olho do jogo; com a tabela do dono sao nove cores em vinte e nove formas, e o
	/// "devolver a cor da ficha ao reverter" deixou de ser um caso de canto pra ser o caminho comum --
	/// sair de qualquer Super Saiyajin passa por aqui.
	///
	/// SEMPRE SOMA (`ModoSoma`): o sprite de olho do jogo e preto, como o `Eyes_Black.dmi` do
	/// original -- e o `ICON_ADD` do `ui_eye_icon()` e literalmente esta operacao.
	/// ================================================================================
	/// </summary>
	private void TingirOlhos(Color? cor)
	{
		if (_olhos is not { } ol || !IsInstanceValid(ol) || ol.Material is not ShaderMaterial mo) return;
		(cor is null ? BaseDaFicha(CamadaTingida.Olho) : Tinta.De(cor)).Escrever(mo);
	}

	/// <summary>
	/// A tinta que o olho REALMENTE recebeu, lida do material. Pra bancada -- e o mesmo motivo do
	/// <see cref="ContornoNoMaterialDeTeste"/>: o campo diz o que se PEDIU, isto diz o que chegou.
	/// Nulo quando nao ha camada de olhos.
	/// </summary>
	public Vector3? TintaDoOlhoDeTeste =>
		_olhos != null && IsInstanceValid(_olhos) && _olhos.Material is ShaderMaterial m
		&& m.GetShaderParameter("tinta") is { } v && v.VariantType == Variant.Type.Vector3
			? v.AsVector3()
			: null;

	/// <summary>
	/// ============================ A TINTA DA FORMA NO CABELO ============================
	/// Ela substitui a cor da FICHA em vez de somar-se a ela, e e o que o DM faz: o `EffectStart` de
	/// cada overlay de forma escreve o `icon` do zero e soma so a cor da forma -- a cor escolhida na
	/// criacao nao entra na conta (`SaiyanObjects.dm:9-20`, `HairObject.dm:88-91`).
	///
	/// REVERTER DEVOLVE A COR DA FICHA, e ela e PERGUNTADA a ficha (<see cref="BaseDaFicha"/>) e nao
	/// guardada: o cabelo da ficha JA e uma tinta neste material (`Vestir`, na linha do `CabeloNatural`),
	/// e sem devolve-la sair da forma daria um cabelo sem cor nenhuma -- o loiro de quem escolheu loiro
	/// seria preto pra sempre. Aqui morava uma captura preguicosa do material, uma das TRES iguais desta
	/// classe; o bloco da `BaseDaFicha` conta por que nenhuma delas ficou.
	///
	/// <paramref name="matiz"/> troca a soma pela substituicao de cor com o sombreado preservado. Quem
	/// decide e o chamador, e a regra dele e "a tinta esta caindo num sprite que a FORMA trouxe?" --
	/// ver o bloco no <see cref="VestirCabeloDaForma"/>. **Ele ja foi exclusivo do Beast** e deixou de
	/// ser quando se mediu que a soma tambem nao servia pro Blue, pro Rose e pro Legendary: os tres
	/// tingem a arte DOURADA de Super Saiyajin, e somar cor em dourado da branco.
	/// ================================================================================
	/// </summary>
	private void TingirCabelo(Color? cor, bool matiz)
	{
		if (_cabelo is not { } cb || !IsInstanceValid(cb) || cb.Material is not ShaderMaterial mc) return;
		(cor is null ? BaseDaFicha(CamadaTingida.Cabelo) : Tinta.De(cor, matiz)).Escrever(mc);
	}

	/// <summary>
	/// ============================ O RABO ACOMPANHA O CABELO ============================
	/// O dono: "o rabo nas transformaçoes q mudam o cabelo (ssj, lssj, blue, rose etc) mudam a
	/// cor do rabo tb". Faz sentido e e o que o BYOND faz: e pelo do MESMO bicho -- um Saiyajin
	/// de cabelo dourado com rabo marrom nao existe em lugar nenhum. Quem decide a cor e o
	/// <see cref="Jandirus.Core.Forms.Catalogo.CorDoRabo"/>, que hoje a DERIVA da tinta do cabelo.
	///
	/// O SSJ4 NAO ENTRA AQUI. O dono: "ssj4 n precisa pintar o rabo pq ele ja e vermelho". E
	/// verdade e e mais que cor: o rabo do SSJ4 faz parte da folha do CORPO dele, e por isso o
	/// `CorpoDaForma` ja esconde o `_rabo` base. Pintar um node escondido nao apareceria hoje --
	/// mas deixaria a tinta armada pra quando ele voltar a aparecer, no reverter.
	///
	/// ============================ O CABELO NAO SE PINTA "PRA FINGIR" ============================
	/// O veto do dono continua de pe: *"n coloque efeitos sobre o cabelo, somente no contorno dele"*.
	/// Ele era sobre dourar o penteado NORMAL pra tapar as 42 variantes de SSJ que faltam desenhar --
	/// e nenhuma forma da escada Saiyajin devolve tinta em `Catalogo.CorDoCabelo`, justamente por
	/// isso. O que voltou a existir e a tinta que a PROPRIA FORMA tem no original (o vermelho do SSG,
	/// o azul do Blue, o verde do Legendary), e essas nunca foram remendo de arte faltando.
	/// ================================================================================
	/// </summary>
	private void PintarRabo(Color? cor)
	{
		// A MESMA PERGUNTA DA `Escondida`, e nao "ha camada de forma": o corpo MUSCULOSO e uma camada
		// de forma que NAO desenha rabo -- escrito do jeito antigo, o rabo do Grade 2 e do Legendary
		// ficava visivel e SEM a tinta que o `Catalogo.CorDoRabo` mandou (dourado no grade, dourado
		// no C-Type), porque este `return` saia antes de pintar.
		if (Jandirus.Core.Forms.Catalogo.FolhaTrazORabo(_simboloDoCorpo)) return;
		if (_rabo is not { } rb || rb.Material is not ShaderMaterial mr) return;
		(cor is null ? BaseDaFicha(CamadaTingida.Rabo) : Tinta.De(cor)).Escrever(mr);
	}

	/// <summary>As tres camadas que a FORMA pinta por cima da ficha. Ver <see cref="BaseDaFicha"/>.</summary>
	private enum CamadaTingida { Cabelo, Olho, Rabo }

	/// <summary>
	/// ============================ A BASE E DERIVADA, E NAO GUARDADA ============================
	/// "Sair da forma devolve a cor da ficha" era feito por TRES registros iguais -- um por camada --,
	/// cada um com um `bool` de "ja capturei" e uma captura preguicosa do material na primeira pintura.
	/// Tres copias do mesmo mecanismo, e a terceira estava quebrada: `Vestir` religava o do cabelo e o do
	/// olho quando a ficha mudava, e o do RABO ficava pra tras -- capturado uma vez na vida. Dois
	/// religados e um esquecido e o defeito que mais se repete neste port.
	///
	/// O conserto nao e religar o terceiro: e nao ter o que religar. A cor base de cada camada JA E um
	/// dado -- ela mora na ficha (`Appearance`), que e exatamente de onde o <see cref="Vestir"/> a tira
	/// pra escrever. Perguntar a ficha na hora de reverter da a MESMA expressao que a escreveu, sem
	/// campo, sem `bool`, sem ordem de chamada pra acertar. Trocar de penteado ou de cor no guarda-roupa
	/// dentro de uma transformacao passa a ser irrelevante: nao ha registro pra ficar velho.
	///
	/// O RABO ENTRA PELA MESMA PORTA e e por isso que ele aparece aqui: a ficha nao tem cor de rabo (o
	/// <see cref="Vestir"/> nunca o tinge), entao a base dele e <see cref="Tinta.Nenhuma"/> -- o sprite
	/// cru. Isso e uma DERIVACAO e nao um caso especial, e e o que torna a assimetria impossivel de
	/// voltar: se um dia a ficha ganhar cor de rabo, muda-se esta linha e os dois lados andam juntos.
	///
	/// (Sem ficha -- boneco de bancada montado sem `Vestir` -- a base e a arte crua, que e o que um
	/// material recem-criado tem mesmo.)
	/// =========================================================================================
	/// </summary>
	private Tinta BaseDaFicha(CamadaTingida qual) => qual switch
	{
		// O `CabeloNatural` e a mesma pergunta da linha do cabelo no `Vestir`: raca de cabelo natural
		// nao aceita a cor escolhida, e a base dela e o desenho.
		CamadaTingida.Cabelo => _ficha is { } f && !VisualCatalog.CabeloNatural(_racaDaFicha)
			? Tinta.DaFicha(f.CorCabelo) : Tinta.Nenhuma,
		CamadaTingida.Olho => _ficha is { } f2 ? Tinta.DaFicha(f2.CorOlho) : Tinta.Nenhuma,
		_ => Tinta.Nenhuma,
	};

	/// <summary>
	/// A ULTIMA FICHA VESTIDA, e a raca com que ela foi lida. So a <see cref="BaseDaFicha"/> usa.
	///
	/// E a referencia viva de proposito: a tela de criacao mexe no proprio objeto e chama o
	/// <see cref="Vestir"/> de novo, entao a base derivada acompanha sem ninguem ter que avisar.
	/// </summary>
	private Appearance? _ficha;
	private string _racaDaFicha = "";

	// =====================================================================
	// FERIDAS
	// =====================================================================
	/// <summary>Quem recebe ferida, e de que tipo. Ver o bloco `ferida_modo` no shader.</summary>
	private const int ModoNada = 0, ModoPele = 1, ModoPano = 2;

	/// <summary>A ultima mascara aplicada -- pra nao reescrever dez uniformes por quadro.</summary>
	private Jandirus.Core.Combat.MascaraDeFeridas _feridas;
	private bool _temFeridas;

	/// <summary>
	/// O SORTEIO DESTE CORPO. Dois lutadores com o mesmo estrago tem manchas em lugares
	/// diferentes -- sem isto, uma briga de dois deixaria os dois com o MESMO respingo, e a
	/// coincidencia denuncia o efeito como desenho gerado.
	/// </summary>
	private float _semente;

	/// <summary>
	/// PINTA (ou rasga) o corpo conforme o estrago que o servidor mandou.
	///
	/// ============================ QUEM RECEBE O QUE ============================
	///   * CORPO -> hematoma e sangue. E a pele: ela fica roxa e depois sangra.
	///   * ROUPA -> rasgo. Pano nao fica roxo, ele abre -- e o buraco mostra a pele ferida que a
	///     camada de baixo ja pintou, que e o encaixe que faz o efeito valer a pena.
	///   * CABELO, OLHOS, RABO -> nada. Foi o que o dono pediu, e faz sentido: cabelo nao
	///     hematoma e olho nao rasga.
	/// ===========================================================================
	///
	/// So mexe nos uniformes quando a mascara MUDA. Ela vem do servidor a 5 Hz e so quando o
	/// corpo muda de cara -- reescrever dez parametros por quadro pra repetir o mesmo valor seria
	/// pagar o efeito inteiro em todo mundo da tela, o tempo todo.
	/// </summary>
	public void Ferir(Jandirus.Core.Combat.MascaraDeFeridas m, int semente)
	{
		if (_temFeridas && _feridas == m) return;
		_feridas = m;
		_temFeridas = true;
		_semente = (semente % 997) * 0.37f;

		var hema = new float[Jandirus.Core.Combat.MascaraDeFeridas.Zonas];
		var sang = new float[Jandirus.Core.Combat.MascaraDeFeridas.Zonas];
		for (int i = 0; i < hema.Length; i++)
		{
			var z = (Jandirus.Core.Combat.ZonaDeFerida)i;
			hema[i] = m.Hematoma(z);
			sang[i] = m.Sangue(z);
		}

		if (_corpo != null) AplicarFerida(_corpo, ModoPele, hema, sang);
		foreach (AnimatedSprite2D r in _roupa) AplicarFerida(r, ModoPano, hema, sang);
	}

	private void AplicarFerida(AnimatedSprite2D s, int modo, float[] hema, float[] sang)
	{
		if (!IsInstanceValid(s) || s.Material is not ShaderMaterial m) return;
		m.SetShaderParameter("ferida_modo", modo);
		m.SetShaderParameter("f_hema", hema);
		m.SetShaderParameter("f_sang", sang);
		m.SetShaderParameter("ferida_semente", _semente);
		AplicarAmputacao(s);
		AtualizarCaixa(s);
	}

	/// <summary>
	/// O LADO DO CORPO NAO E O LADO DA IMAGEM, e a diferenca inverte quando o boneco vira.
	///
	/// De frente (o sprite `south`), o braco ESQUERDO dele aparece a DIREITA de quem olha -- a
	/// mesma inversao de olhar alguem no espelho. De costas (`north`) os lados coincidem; de perfil
	/// so um braco aparece e tanto faz.
	///
	/// Sem esta traducao, arrancar o braco esquerdo apagaria o direito na tela metade das vezes, e
	/// o jogador veria o boneco discordando do paperdoll (que mostra o lado certo).
	/// </summary>
	private void AplicarAmputacao(AnimatedSprite2D s)
	{
		if (!IsInstanceValid(s) || s.Material is not ShaderMaterial m) return;

		bool espelha = _facing is Facing.South;
		Vector2 Lados(bool esq, bool dir) => espelha
			? new Vector2(dir ? 1 : 0, esq ? 1 : 0)
			: new Vector2(esq ? 1 : 0, dir ? 1 : 0);

		m.SetShaderParameter("amp_braco", Lados(
			_feridas.Perdeu(Jandirus.Core.Combat.MascaraDeFeridas.Membro.BracoEsq),
			_feridas.Perdeu(Jandirus.Core.Combat.MascaraDeFeridas.Membro.BracoDir)));
		m.SetShaderParameter("amp_perna", Lados(
			_feridas.Perdeu(Jandirus.Core.Combat.MascaraDeFeridas.Membro.PernaEsq),
			_feridas.Perdeu(Jandirus.Core.Combat.MascaraDeFeridas.Membro.PernaDir)));
	}

	/// <summary>
	/// A direcao mudou: os lados do corpo trocam de lado na imagem, entao a amputacao tem que ser
	/// reescrita. So faz trabalho se houver membro faltando.
	/// </summary>
	private void SeguirDirecaoNaAmputacao()
	{
		if (!_temFeridas || _feridas.Amputados == Jandirus.Core.Combat.MascaraDeFeridas.Membro.Nenhum) return;
		if (_corpo != null) AplicarAmputacao(_corpo);
		foreach (AnimatedSprite2D r in _roupa) AplicarAmputacao(r);
	}

	/// <summary>
	/// Reaplica a mascara depois de uma troca de roupa.
	///
	/// Camada de roupa NASCE limpa: `Vestir` cria um `AnimatedSprite2D` novo com material novo, e
	/// o material novo nao sabe de ferida nenhuma. Sem esta volta, trocar de camisa no meio de uma
	/// luta curava a roupa -- e trocar de roupa nao fecha ferimento.
	/// </summary>
	/// <summary>
	/// Em que modo cada familia de camada esta. SO PRA BANCADA (`--diagferida`).
	///
	/// Devolve (corpo, quantas roupas em modo pano, quantas OUTRAS camadas fora do modo 0) -- e a
	/// terceira que importa: ela e zero quando cabelo, olhos e rabo ficaram de fora, como o dono
	/// pediu. Sem isto, "so o corpo e a roupa recebem" seria afirmacao minha.
	/// </summary>
	public (int Corpo, int Roupas, int Outras) ModosDeTeste()
	{
		int Modo(AnimatedSprite2D? s) =>
			s != null && IsInstanceValid(s) && s.Material is ShaderMaterial m
				? (int)m.GetShaderParameter("ferida_modo") : -1;

		int roupas = _roupa.Count(r => Modo(r) == ModoPano);
		int outras = 0;
		foreach (AnimatedSprite2D? o in new[] { _cabelo, _olhos, _rabo })
			if (o != null && Modo(o) > 0) outras++;

		return (Modo(_corpo), roupas, outras);
	}

	/// <summary>
	/// A TINTA GRAVADA em cada camada de roupa: a cor e o modo. SO PRA BANCADA.
	///
	/// Nao ha outro jeito de provar que a cor CHEGOU AO DESENHO -- um shader nao devolve nada, e a
	/// alternativa seria olhar uma foto e confiar no olho.
	/// </summary>
	public List<(Vector3 Cor, int Modo)> TintaDaRoupaDeTeste()
	{
		var fora = new List<(Vector3, int)>();
		foreach (AnimatedSprite2D r in _roupa)
			if (IsInstanceValid(r) && r.Material is ShaderMaterial m)
				fora.Add((m.GetShaderParameter("tinta").AsVector3(), (int)m.GetShaderParameter("tinta_modo")));
		return fora;
	}

	/// <summary>O shader sabe que o desenho esta deitado? SO PRA BANCADA (`--diagferida`).</summary>
	public bool DeitadoDeTeste => _deitadoEnviado;

	/// <summary>A caixa do quadro que o corpo mandou pro shader. SO PRA BANCADA.</summary>
	public (float Min, float Max)? CaixaDeTeste()
	{
		if (_corpo is not { } s || !IsInstanceValid(s) || s.Material is not ShaderMaterial m) return null;
		Vector2 mn = m.GetShaderParameter("quadro_min").AsVector2();
		Vector2 mx = m.GetShaderParameter("quadro_max").AsVector2();
		return (mn.X + mn.Y, mx.X + mx.Y);
	}

	private void ReaplicarFeridas()
	{
		if (!_temFeridas) return;
		Jandirus.Core.Combat.MascaraDeFeridas m = _feridas;
		_temFeridas = false;   // forca o `Ferir` a passar pelo caminho inteiro
		Ferir(m, (int)(_semente / 0.37f));
	}

	/// <summary>Soma a cor por cima -- o `ICON_ADD` do BYOND. Corpo, cabelo e olho usam este.</summary>
	private const int ModoSoma = 0;

	/// <summary>
	/// TROCA A COR mantendo o sombreado do desenho. E o modo da ROUPA.
	///
	/// ============================ POR QUE A ROUPA NAO PODE SOMAR ============================
	/// Somar so CLAREIA. Em 8 bits: gi branco (255,255,255) + azul = branco -- nao acontece nada.
	/// Gi vermelho + azul = MAGENTA, nao azul. E o unico resultado alcancavel e sempre mais claro
	/// que o original.
	///
	/// Isso serve pro cabelo (base preta, e o dourado do Super Saiyajin E clareamento) e serve pro
	/// olho (o sprite base do DM e `Eyes_Black`). Nao serve pra um catalogo de roupa que ja vem
	/// PINTADO. O dono pediu "mudar as cores das roupas... pra deixar mais customizavel", e com
	/// soma a maioria das pecas nao mudaria de cor nenhuma.
	///
	/// DIVERGE DO DM DE PROPOSITO: la a tintura de roupa era comprada e assada no icone com `+=`,
	/// que e ICON_ADD -- ou seja, o original tinha exatamente esta limitacao.
	/// ========================================================================================
	/// </summary>
	private const int ModoMatiz = 1;

	/// <summary>
	/// ============================ A TINTA E UM VALOR SO: A COR E A OPERACAO ============================
	/// `tinta` e `tinta_modo` eram DOIS uniformes independentes, escritos de seis lugares -- e um deles
	/// (o rabo) escrevia so a cor. Enquanto forem dois campos livres, o estado "modo MATIZ com a cor que
	/// sobrou" e REPRESENTAVEL, e ele nao e um arranhao: o matiz DESCARTA o RGB da arte e devolve
	/// `tinta x (luminancia x 2)`, entao matiz com tinta zero e PRETO CHAPADO -- mecha clara e mecha
	/// escura viram o mesmo tom e o desenho inteiro vira silhueta sem relevo.
	///
	/// Foi a hipotese numero um do relato *"parece q tem algo pintando o cabelo ... n era tao escuro
	/// assim antes"*, e ela ficou de pe ate a bancada medir que a causa era outra (o shader elevando a
	/// folha AO QUADRADO -- ver o bloco do `COLOR` no `Personagem.gdshader`). Ou seja: a combinacao ruim
	/// nunca chegou a acontecer, mas ela era **digitavel** -- bastava um chamador escrever um dos dois.
	///
	/// Aqui os dois viram UM valor, e nao ha construtor que produza matiz sem cor: <see cref="Nenhuma"/>
	/// e SOMA por definicao, e o <c>De</c> exige a cor pra sequer aceitar o modo. O par so alcanca o
	/// material pelo <see cref="Escrever"/>, que grava os dois uniformes na mesma linha. Escrever um sem
	/// o outro deixou de ser possivel de digitar -- que e o unico conserto que sobrevive ao proximo
	/// chamador.
	///
	/// ============================ E PRETO CONTINUA SENDO TINTA LEGITIMA ============================
	/// O atalho tentador era o shader tratar preto como neutro no matiz. ESTA DESCARTADO, e foi medido e
	/// nao suposto: a cor da roupa sai de um `ColorPicker` livre (`CreationScreen.cs:416`), entao preto e
	/// uma escolha que o jogador PODE fazer, e no matiz "roupa preta" e preto chapado mesmo -- e o que a
	/// peca deve parecer. O que nao pode existir e o MODO SEM COR, e e disso que este tipo cuida.
	/// ==============================================================================================
	/// </summary>
	private readonly record struct Tinta(Vector3 Cor, int Modo)
	{
		/// <summary>Sem tinta: a arte crua. SOMA com zero e o unico neutro -- ver o bloco acima.</summary>
		public static readonly Tinta Nenhuma = new(Vector3.Zero, ModoSoma);

		/// <summary>A cor da FORMA (ja em `Color` do Godot). Nula = sem tinta, e ai o modo e SOMA.</summary>
		public static Tinta De(Color? cor, bool matiz = false) =>
			cor is { } c ? new Tinta(new Vector3(c.R, c.G, c.B), matiz ? ModoMatiz : ModoSoma) : Nenhuma;

		/// <summary>A cor da FICHA (o `Rgb` de 8 bits do Core). Nula = sem tinta.</summary>
		public static Tinta DaFicha(Rgb? cor, bool matiz = false) =>
			cor is { } c
				? new Tinta(new Vector3(c.R / 255f, c.G / 255f, c.B / 255f), matiz ? ModoMatiz : ModoSoma)
				: Nenhuma;

		/// <summary>Grava os DOIS uniformes. E a unica porta de entrada do material.</summary>
		public void Escrever(ShaderMaterial m)
		{
			m.SetShaderParameter("tinta_modo", Modo);
			m.SetShaderParameter("tinta", Cor);
		}
	}

	/// <summary>Pinta esta camada. <see cref="Tinta.Nenhuma"/> = a cor natural do sprite.</summary>
	private static void Tingir(AnimatedSprite2D s, Tinta t)
	{
		if (s.Material is ShaderMaterial m) t.Escrever(m);
	}


	// =====================================================================
	// APARENCIA
	// =====================================================================
	/// <summary>
	/// Monta (ou remonta) as camadas a partir da ficha de aparencia. Pode ser chamado a cada
	/// mexida na tela de criacao -- e o que faz a previa ser AO VIVO.
	/// </summary>
	public void Vestir(VisualCatalog cat, Appearance ap, string raca, string genero)
	{
		Garantir();

		// A FICHA FICA, e e o que faz a `BaseDaFicha` funcionar: daqui pra frente "voltar ao normal"
		// PERGUNTA a ficha em vez de consultar uma copia tirada em algum momento do passado. Ver o bloco
		// da `BaseDaFicha` -- e o que apagou os tres registros de "guarde a cor pra devolver".
		_ficha = ap;
		_racaDaFicha = raca;

		// --- corpo ---
		(Rgb? soma, float brilho) = cat.TintaDoCorpo(ap, raca);
		Trocar(_corpo!, cat.CorpoSprite(ap, raca, genero));
		_feminina = genero.StartsWith("F", StringComparison.OrdinalIgnoreCase);
		// SO O TOM DO NAMEKUSEIJIN (o `Brilho` 1,18/0,82 de `VisualCatalog.TintaDoCorpo`; 1,0 pro resto,
		// e 1,0 e o neutro da multiplicacao). ESTA LINHA ESTEVE MORTA junto com a tinta da colada: o
		// `Personagem.gdshader` descartava o `self_modulate` no `COLOR = c`, e o efeito era exatamente o
		// que o comentario do catalogo diz que nao pode acontecer -- "os quatro tons de Namekuseijin
		// sairiam todos iguais".
		//
		// Ver o bloco do `COLOR` no shader.
		_corpo!.SelfModulate = new Color(brilho, brilho, brilho);
		Tingir(_corpo, Tinta.DaFicha(soma));

		// --- roupa: uma camada por peca, na ordem em que foi vestida ---
		while (_roupa.Count > ap.Roupa.Count)
		{
			AnimatedSprite2D velha = _roupa[^1];
			_roupa.RemoveAt(_roupa.Count - 1);
			Descartar(velha);
		}
		for (int i = 0; i < ap.Roupa.Count; i++)
		{
			if (i >= _roupa.Count) _roupa.Add(NovaCamada(2 + i));
			Trocar(_roupa[i], ap.Roupa[i].Caminho);

			// ============================ A COR VEM DEPOIS, E VEM SEMPRE ============================
			// FORA do `Trocar`, porque ele SAI ANTECIPADO quando o caminho e o mesmo -- mexer so na
			// cor nao mudaria um pixel, e a previa da criacao ficaria muda.
			//
			// E SEM `if (cor != null)`, porque a camada e RECICLADA entre chamadas: o material (e a
			// tinta gravada nele) sobrevive. Condicionar deixaria a cor velha grudada ao desmarcar o
			// "cor", e faria a peca 0 herdar a cor da peca que estava ali antes quando a lista
			// desliza. E o mesmo cuidado que o cabelo ja toma na linha do `Tingir` dele.
			// =======================================================================================
			// MATIZ, e o `DaFicha` cuida do resto: peca SEM cor cai em `Tinta.Nenhuma`, que e SOMA com
			// zero. Nao ha como pedir "matiz" e ficar sem cor -- ver o bloco do tipo `Tinta`.
			Tingir(_roupa[i], Tinta.DaFicha(ap.Roupa[i].Cor, matiz: true));
		}

		// --- cabelo: acima da roupa, como no jogo (plano 4 contra 3) ---
		string? cabelo = VisualCatalog.TemCabelo(raca) ? cat.SpriteDoCabelo(ap.Cabelo) : null;
		if (cabelo == null)
		{
			if (_cabelo != null) { Descartar(_cabelo); _cabelo = null; }
		}
		else
		{
			_cabelo ??= NovaCamada(10);
			Trocar(_cabelo, cabelo);
			// TROCAR DE APARENCIA REDEFINE O BASE. Sem isto, quem mudasse de penteado no
			// guarda-roupa voltaria ao penteado ANTIGO ao sair de uma forma.
			_cabeloBase = _cabeloAtual = cabelo;
			// A MESMA EXPRESSAO QUE O `BaseDaFicha` devolve ao reverter -- de proposito, e e a unica coisa
			// que precisa ser verdade. Aqui morava um `_tintaDoCabeloGuardada = false` pra corrigir um
			// registro que envelhecia quando o jogador trocava de cor no guarda-roupa; sem registro nao ha
			// o que envelhecer. (O `_cabeloBase` da linha acima continua: aquele e o PENTEADO, e o penteado
			// nao esta na ficha em forma de arquivo -- e resolvido pelo catalogo.)
			Tingir(_cabelo, BaseDaFicha(CamadaTingida.Cabelo));
		}

		// --- olhos: um sprite so -- o jogo tem exatamente um arquivo ---
		if (cat.Olhos == null)
		{
			if (_olhos != null) { Descartar(_olhos); _olhos = null; }
		}
		else
		{
			_olhos ??= NovaCamada(11);
			Trocar(_olhos, cat.Olhos);
			// Irma da linha do cabelo: a MESMA expressao que o `BaseDaFicha` devolve ao reverter.
			Tingir(_olhos, BaseDaFicha(CamadaTingida.Olho));
		}

		MontarRabo();

		// ============================ O CORPO DA FORMA E REFEITO, PORQUE A PELE PODE TER MUDADO ============================
		// So o <see cref="Jandirus.Core.Forms.CorpoDeForma.Musculoso"/> depende de quem esta dentro
		// dele, e ele resolve o arquivo na hora em que e vestido. Quem trocar de tom de pele no
		// guarda-roupa DENTRO de um Grade 2 (ou de um Legendary) ficaria com o musculo da pele
		// ANTIGA por cima do corpo novo -- moreno de cabeca e claro de tronco -- ate a proxima troca
		// de forma.
		//
		// Uma linha, e ela e no-op pra todo o resto: `Nenhum` sai pelo caminho de remover uma camada
		// que nao existe, e o SSJ4/Oozaru revestem a mesma folha (o `Trocar` de dentro do
		// `CorpoDaForma` nem chega a ser chamado -- o `SpriteFrames` e reatribuido igual).
		// ==============================================================================================================
		CorpoDaForma(_simboloDoCorpo);

		Reordenar();
		Aplicar(force: true);

		// (Camada NOVA nasce visivel, e uma troca de aparencia pode chegar a qualquer instante --
		// inclusive com o jogador virado macaco. Quem impede um penteado de 32 px de aparecer
		// flutuando no peito de um Oozaru e o `Aplicar` logo acima, pela `Escondida`.)

		// A ROUPA NOVA NASCE LIMPA, e trocar de roupa nao fecha ferimento. Ver `ReaplicarFeridas`.
		ReaplicarFeridas();
	}

	/// <summary>
	/// O RABO aparece e some em jogo -- nao e escolha de criacao, e estado de corpo. Quem
	/// manda e o servidor (bit no snapshot), porque so ele sabe se o rabo ainda esta la.
	///
	/// Sem rabo o Saiyajin perde o Oozaru e treina 2,5x mais rapido (`tailgain`), entao isto
	/// nao e enfeite: e a leitura visual de uma mudanca grande de ficha.
	/// </summary>
	public void MostrarRabo(bool tem)
	{
		if (_temRabo == tem) return;
		_temRabo = tem;
		MontarRabo();
		Reordenar();
		// ============================ E O RABO NOVO JA NASCE OBEDECENDO A FORMA ============================
		// `MontarRabo` cria um `AnimatedSprite2D` NOVO, e camada nova nasce visivel. O caminho e
		// real -- o rabo vem do bit do snapshot, entao basta o servidor reafirmar "tem rabo"
		// (recolocar, entrar na zona, o tique da forma) durante uma transformacao -- e o resultado
		// seria dois rabos no SSJ4 ou um rabo de 32 px na barriga do macaco.
		//
		// Nao ha guarda escrita aqui de proposito: e o `Aplicar` abaixo que decide, pela
		// `Escondida`. Uma segunda copia da regra neste ponto e o que a `Escondida` existe pra
		// impedir.
		if (_rabo != null) Aplicar(_rabo, _corpo?.Animation, force: true);
	}

	private void MontarRabo()
	{
		if (!_temRabo)
		{
			if (_rabo == null) return;
			Descartar(_rabo);
			_rabo = null;
			Reordenar();
			return;
		}

		// ENTRE O CORPO E A ROUPA. No original o rabo herda de CABELO no typepath
		// (`/obj/overlay/hairs/tails/saiyantail`) e engana quem le, mas o plano e o layer sao
		// recravados pra BODY_LAYER: ele desenha ACIMA do corpo e ABAIXO de tudo o mais.
		//
		// O numero e a posicao na PILHA (ver NovaCamada): 1 e logo acima do corpo, abaixo de
		// qualquer roupa. Quem materializa isso na arvore de nodes e Reordenar().
		bool nasceuAgora = _rabo == null;
		_rabo ??= NovaCamada(1);
		Trocar(_rabo, SpriteDoRabo);
		if (nasceuAgora) RevestirRaboRecemNascido();
	}

	/// <summary>
	/// ============================ O RABO NASCE TARDE, E TEM QUE NASCER JA VESTIDO ============================
	/// Toda outra camada do boneco (corpo, roupa, cabelo, olhos) nasce no <see cref="Vestir"/>, e a forma e
	/// vestida DEPOIS dele -- entao quando a forma escreve, elas ja existem. O rabo nao: ele vem de um bit do
	/// SNAPSHOT (`RemotePlayer.Receive` -> <see cref="MostrarRabo"/>), que e outro canal e outro instante.
	///
	/// ============================ O CASO QUE ISTO CONSERTA, MEDIDO ============================
	/// Bancada de dois corpos, o rabo dourado do `ssj1` medido nos tres cenarios:
	///
	///     A vendo o proprio corpo ................. tinta (0,85 0,85 0,15) -- dourado
	///     B vendo A, tendo ENTRADO DEPOIS ......... tinta (0,00 0,00 0,00) -- PRETO
	///     B vendo A transformar AO VIVO ........... tinta (0,85 0,85 0,15) -- dourado
	///
	/// A terceira linha e a que fecha o caso: nao e "o corpo remoto nao recebe tinta", e a ENTRADA. No
	/// `World.AoReceberSnapshot` o corpo alheio e vestido no nascimento (`VestirCorpoInteiro`) e so OITO
	/// linhas depois consome o snapshot (`r.Receive(..., e.Rabo, ...)`). Naquele momento `_rabo` ainda e
	/// nulo, o <see cref="PintarRabo"/> sai pelo `return` de cima, a tinta cai no vazio -- e o rabo nasce um
	/// instante depois com o material cru, que e o cinza-escuro do `Tail.png`. Pra sempre.
	///
	/// ============================ POR QUE AQUI E NAO NA ORDEM DO `World` ============================
	/// Trocar as duas linhas la consertaria a ENTRADA e mais nada: o rabo tambem nasce tarde quando ele
	/// volta a existir em jogo (o bit do servidor muda -- rabo cortado que rebrota, saida do Oozaru), e
	/// nesses casos nao ha nenhum "vestir" por perto pra reordenar. Enquanto a criacao da camada estiver
	/// fora do `Vestir`, o lugar que sabe consertar e o proprio nascimento.
	///
	/// ============================ E ELE RECEBE TUDO, NAO SO A TINTA ============================
	/// A mesma comparacao achou um SEGUNDO uniform divergindo na mesma camada -- `aura_cor`, 0,824 no dono
	/// contra 0,85 no alheio (o padrao do shader contra o que a forma escreveu). Mesmo defeito, mesma raiz.
	/// Consertar so a tinta deixaria o proximo uniform de fora do mesmo jeito, entao aqui passam os TRES
	/// canais que a classe escreve por camada, cada um pela funcao que ja e dona dele -- nenhuma regra e
	/// copiada, so re-executada.
	/// ==========================================================================================================
	/// </summary>
	private void RevestirRaboRecemNascido()
	{
		// 1. A TINTA DA FORMA. A MESMA EXPRESSAO do `VestirCabeloDaForma` -- com `_formaVestida` nulo o
		//    `CorDoRabo` devolve nulo e o `PintarRabo` cai na `BaseDaFicha`, que e o sprite cru: um rabo
		//    que nasce fora de forma nenhuma continua sendo o rabo de sempre.
		PintarRabo(Jandirus.Core.Forms.Catalogo.CorDoRabo(_formaVestida) is { } cr ? new Color(cr) : null);

		// 2. O CONTORNO DA FORMA (`aura` + `aura_cor`). PELA FASE e nao pelos valores crus, pelo mesmo
		//    motivo do `AuraDaForma`: escrever o topo aqui faria o pulso estalar toda vez que um rabo
		//    aparecesse. Escreve as outras camadas junto, e isso e de graca -- e o mesmo valor que elas
		//    ja tem.
		EscreverContorno(CorNaFaseDaOscilacao(), ForcaNaFaseDoPulso());

		// 3. AS FERIDAS. Um corpo destrocado nao ganha um rabo limpo. E no-op quando nao ha mascara.
		ReaplicarFeridas();
	}

	private static void Trocar(AnimatedSprite2D alvo, string caminho)
	{
		if (alvo.GetMeta("src", "").AsString() == caminho) return;   // ja e esse: nao reinicia a animacao
		var f = ResourceLoader.Load<SpriteFrames>(caminho);
		if (f == null) { GD.PushWarning($"[visual] sprite ausente: {caminho}"); return; }
		alvo.SpriteFrames = f;
		alvo.SetMeta("src", caminho);
		Ancorar(alvo, f);
	}

	/// <summary>O lado do tile. Toda folha de personagem normal e deste tamanho.</summary>
	private const int Celula = 32;

	/// <summary>
	/// ANCORA A CAMADA PELOS PES, e nao pelo centro.
	///
	/// Quase toda folha de personagem e 32x32 e o `Centered = true` acerta sozinho. Mas nao
	/// TODAS: o Big Broly e 32x64 e o Tyrone e 42x32. Centradas no mesmo ponto, uma folha de 64
	/// de altura desce 16 px em relacao as outras -- o cabelo e a roupa (32) ficam na cintura do
	/// corpo (64), e o personagem sai desmontado.
	///
	/// O BYOND ancora icone no canto INFERIOR ESQUERDO, e e por isso que la isso nunca
	/// aconteceu: la as folhas ja se encostam pelo chao e pela esquerda. Aqui a mesma regra
	/// precisa ser escrita -- o mesmo raciocinio do `texture_origin` do conversor de mapa, que
	/// existe pelo mesmo motivo nos tiles grandes.
	///
	/// Folha 32x32 da deslocamento zero, entao o caso comum nao paga nada.
	/// </summary>
	private static void Ancorar(AnimatedSprite2D alvo, SpriteFrames f)
	{
		foreach (string anim in f.GetAnimationNames())
		{
			if (f.GetFrameCount(anim) == 0) continue;
			if (f.GetFrameTexture(anim, 0) is not { } tex) continue;
			Vector2 t = tex.GetSize();
			if (t.X <= 0 || t.Y <= 0) return;
			// esquerda encostada na esquerda, base encostada na base
			alvo.Offset = new Vector2((Celula - t.X) * 0.5f, (Celula - t.Y) * 0.5f);
			return;
		}
	}

	// =====================================================================
	// ANIMACAO
	// =====================================================================
	/// <summary>Traduz a pose que veio do servidor no nome do estado de animacao.</summary>
	public void SetPose(Protocol.Pose pose) => SetState(pose switch
	{
		Protocol.Pose.Treinando => "train",
		Protocol.Pose.Meditando => "meditate",
		Protocol.Pose.Atacando => "attack",
		Protocol.Pose.Voando => "flight",
		Protocol.Pose.Nocauteado => "ko",
		_ => "default",
	});

	// =====================================================================
	// O CORPO DEITADO -- nocaute e arremesso
	// =====================================================================
	/// <summary>
	/// DEITA O SPRITE NA DIRECAO CERTA.
	///
	/// ============================ O PROBLEMA ============================
	/// O `.dmi` tem UM desenho de nocaute e ele cai sempre pro mesmo lado -- deitado pra direita.
	/// Isso vale quando o personagem estava olhando pra direita e fica errado nos outros tres
	/// casos: o dono viu o corpo cair pro mesmo lado independente de pra onde ele encarava.
	///
	/// Nao ha (e nao precisa haver) quatro desenhos de queda: o corpo deitado e simetrico o
	/// bastante pra que GIRAR resolva, e girar e o que o proprio BYOND fazia com `transform` no
	/// `Small_Impact`.
	/// ====================================================================
	///
	/// A CONTA E A QUE O DONO DITOU: olhando pra direita cai como hoje (0 graus); pra esquerda gira
	/// 180; pra cima gira 90 pra baixo; pra baixo gira 90 pra cima.
	///
	/// ============================ OS DOIS EIXOS VERTICAIS ESTAVAM TROCADOS ============================
	/// A tabela dizia uma coisa e fazia a outra: `South` mandava +90 e `North` -90, que e o inverso
	/// exato da linha acima. O efeito so aparecia num dos quatro casos, e o dono achou: "quando o
	/// personagem ta olhando pra baixo e ele desmaia ele ta desmaiando de cabeca pra baixo".
	///
	/// A 0 grau a cabeca do sprite de nocaute aponta pro LESTE, e giro positivo e horario (o Y
	/// cresce pra baixo). Entao +90 leva a cabeca pra BAIXO e -90 leva pra CIMA. Quem cai, cai de
	/// COSTAS: olhando pra baixo (pra camera), a cabeca termina pra cima; olhando pra cima, termina
	/// pra baixo. Leste e oeste ja estavam certos, e e por isso que so metade da queixa existia.
	/// ==================================================================================================
	/// </summary>
	public void DeitarPor(Facing olhando) => Girar(olhando switch
	{
		Facing.East => 0f,
		Facing.West => 180f,
		Facing.South => -90f,
		_ => 90f,
	});

	/// <summary>
	/// DEITA O CORPO **ACORDADO** na direcao do voo -- e o arremesso.
	///
	/// ============================ POR QUE E OUTRA TABELA ============================
	/// O <see cref="DeitarPor"/> serve ao sprite de NOCAUTE, que ja e um desenho DEITADO: a 0 grau
	/// a cabeca dele ja aponta pro LESTE, entao girar dali e so escolher pra que lado.
	///
	/// O arremesso usa o sprite ACORDADO (pedido do dono -- quem voa ainda esta consciente), e esse
	/// esta EM PE: a 0 grau a cabeca aponta pro NORTE. Aplicar a tabela do nocaute nele erra por 90
	/// graus, e o caso mais visivel e justamente o mais comum -- voando pra LESTE o angulo dava 0 e
	/// o corpo continuava DE PE. Foi o que o dono fotografou: "era pra girar o corpo e botar a
	/// cabeca nesse caso no lado direito enquanto ele voa".
	///
	/// A conta: rotacao positiva gira no sentido horario (o Y cresce pra baixo), e a cabeca em pe e
	/// o vetor (0,-1). Girar +90 leva (0,-1) pra (1,0) -- leste. Dai a tabela.
	/// ================================================================================
	/// </summary>
	public void VoarPara(Facing rumo) => Girar(rumo switch
	{
		Facing.North => 0f,
		Facing.East => 90f,
		Facing.South => 180f,
		_ => -90f,
	});

	/// <summary>
	/// GIRA O CORPO NA DIRECAO DO ARREMESSO -- o mesmo desenho deitado, apontado pra onde ele voa.
	///
	/// VETOR ZERO ENDIREITA. E o caminho de volta: fora do voo o sprite tem que estar no prumo, e
	/// deixar isso a cargo de quem chama seria uma chance a mais de o corpo ficar torto pra sempre.
	///
	/// E O SPRITE ACORDADO, nao o de nocaute -- pedido do dono, e faz sentido: quem esta voando
	/// ainda esta consciente, so nao esta no controle. Quem escolhe o estado e o chamador; aqui so
	/// se gira.
	/// </summary>
	public void GirarPara(Vec2 rumo)
	{
		if (rumo.LengthSquared < 1e-6f) { Girar(0f); return; }

		// QUATRO DIRECOES, NAO TRESENTAS E SESSENTA.
		//
		// A primeira versao usava o angulo cru do `atan2`, e o resultado foi o que o dono viu: "ao
		// levar knock back o personagem ta girando". Cada correcao do servidor mexia o rumo alguns
		// graus e o sprite acompanhava -- um corpo rodopiando no ar em vez de um corpo arremessado.
		//
		// O pedido e o mesmo do nocaute: "so virasse pro lado, dando uma rotacao no personagem
		// virando a cabeca dele pra direcao q ele ta voando". Quantizar no eixo dominante entrega
		// exatamente isso e ainda mata o tremor: um rumo que oscila 5 graus continua caindo na mesma
		// direcao, e o sprite fica parado.
		VoarPara(MoveRules.FacingFrom(rumo, Facing.East));
	}

	private void Girar(float graus)
	{
		if (Mathf.IsEqualApprox(RotationDegrees, graus)) return;
		RotationDegrees = graus;
	}

	/// <summary>
	/// ============================ A POSE TEM TRES PORTAS, E EU SO GUARDEI UMA ============================
	/// Quem decide a pose sao <see cref="SetMotion"/>, <see cref="SetState"/> e
	/// <see cref="RestartState"/>. A cinematica de estreia tentou mandar na pose guardando o
	/// CHAMADOR (uma guarda por quadro dentro do `LocalPlayer`) -- e o personagem continuou andando,
	/// porque basta QUALQUER outro caminho tocar numa das outras duas portas pra a cena perder.
	///
	/// A tranca mora aqui, onde a decisao e tomada: enquanto ela esta fechada, as tres portas viram
	/// no-op e a pose e a parada virada pra frente -- o `move = 0; dir = SOUTH` do DM.
	///
	/// NAO e o `Congelado` que o dono ja recusou: aquele prendia os QUADROS em 0 e deixava o boneco
	/// duro. Aqui a pose parada roda a animacao dela normalmente; e literalmente o que o corpo faz
	/// quando o jogador solta a tecla depois de andar.
	/// ================================================================================================
	/// </summary>
	/// <remarks>
	/// CONTA OS DONOS, pelo mesmo motivo do `Transformacao.PrendendoOCorpo`: duas cinematicas na
	/// tela ao mesmo tempo (ou uma acabando no quadro em que outra comeca, porque `QueueFree` e
	/// adiado) faziam o `false` de uma destrancar a pose que a outra ainda segurava.
	/// </remarks>
	public void TravarPose(bool travar)
	{
		_donosDaPose = travar ? _donosDaPose + 1 : Mathf.Max(0, _donosDaPose - 1);
		bool queroTravado = _donosDaPose > 0;
		if (_travado == queroTravado) return;
		travar = queroTravado;
		_travado = false;                 // solta pra poder ESCREVER a pose de tranca
		if (travar)
		{
			_state = "default";
			_moving = false;
			_facing = Facing.South;
			_ritmo = 1;
			Aplicar(force: true);
			SeguirDirecaoNaAmputacao();   // virou pro sul: o braco esquerdo trocou de lado na tela
		}
		_travado = travar;
	}

	private bool _travado;
	private int _donosDaPose;

	/// <summary>A animacao que o CORPO esta tocando agora. Pra bancada -- ver `--diagforma`.</summary>
	public string PoseDeTeste => _corpo?.Animation.ToString() ?? "";

	/// <summary>Se a pose esta trancada pela cinematica. Pra bancada.</summary>
	public bool PoseTravadaDeTeste => _travado;

	/// <summary>Quantas cenas seguram a pose agora. Pra bancada.</summary>
	public int DonosDaPoseDeTeste => _donosDaPose;

	public void SetState(string state)
	{
		if (_travado || _state == state) return;
		_state = state;
		_ritmo = 1;
		Aplicar(force: true);
	}

	/// <summary>
	/// Entra num estado REINICIANDO do primeiro quadro, mesmo se ja estivesse nele.
	///
	/// E o que da ritmo ao soco: sem isto, socar de novo enquanto a animacao roda nao faz
	/// nada visivel, e o ciclo que se repete sozinho parece um personagem TRAVADO socando
	/// pra sempre.
	/// </summary>
	public void RestartState(string state, double duracaoAlvo = 0)
	{
		if (_travado) return;
		_state = state;
		_ritmo = 1;
		Aplicar(force: true);   // ja zera o relogio: o golpe recomeca do primeiro quadro
		if (duracaoAlvo <= 0 || _corpo?.SpriteFrames is not { } f) return;

		// ENCAIXA A ANIMACAO NO TEMPO DO GOLPE. O .dmi traz o soco na cadencia do BYOND
		// (~0,8 s); com a cadencia nova de ~0,33 s a animacao nao terminaria antes do
		// proximo soco e o boneco pareceria empacado no meio do movimento. Esticar o relogio
		// conserta na raiz -- e como o mesmo relogio move TODAS as camadas, roupa e cabelo
		// aceleram junto sem sair de compasso.
		double ciclo = f.HasAnimation(_corpo.Animation) ? Ciclo(f, _corpo.Animation) : 0;
		if (ciclo > 0) _ritmo = Math.Clamp(ciclo / duracaoAlvo, 0.5, 6);
	}

	/// <summary>
	/// Escala do relogio da animacao. 1 = a duracao que veio do .dmi; 2 = o dobro da
	/// velocidade. Quem mexe nisto e o soco (ver <see cref="RestartState"/>).
	/// </summary>
	private double _ritmo = 1;

	public void SetMotion(Facing facing, bool moving)
	{
		if (_travado || (_facing == facing && _moving == moving)) return;
		bool virou = _facing != facing;
		_facing = facing;
		_moving = moving;
		Aplicar(force: false);
		// VIROU: o braco esquerdo dele mudou de lado na tela. Ver `AplicarAmputacao`.
		if (virou) SeguirDirecaoNaAmputacao();
	}

	// =====================================================================
	// A IMAGEM REMANESCENTE (Zanzoken)
	// =====================================================================
	/// <summary>
	/// UMA FOTOGRAFIA DESTE CORPO no instante atual -- o `image(icon=target, icon_state=..., dir=...)`
	/// que o original larga no chao (`Buff Effects.dm:41-46`).
	///
	/// COPIA A PILHA INTEIRA, e nao so o corpo: um fantasma sem roupa nem cabelo nao parece "voce
	/// que ficou pra tras", parece outra pessoa. A textura de cada camada e a MESMA (nao ha copia
	/// de pixels), entao um fantasma custa alguns Sprite2D e nada de memoria de imagem.
	///
	/// SAI SOLTO DA ARVORE deste no de proposito: o fantasma tem que ficar ONDE O CORPO ESTAVA
	/// enquanto o corpo continua andando. Filho, ele iria junto -- e "imagem remanescente" que
	/// acompanha o dono e so um borrao grudado.
	/// </summary>
	/// <param name="comTinta">
	/// Copiar tambem o material de tinta de cada camada. Quem vai TROCAR o material logo em
	/// seguida (o rastro de corrida, que aplica o borrao) pede `false` -- ver o comentario dentro.
	/// </param>
	public Node2D Fotografar(bool comTinta = true)
	{
		// A FOTO HERDA A ROTACAO. Sem isto, o vulto de quem esta voando (ou caido) nascia EM PE --
		// o `Girar` escreve no proprio `CharacterVisual`, que e o PAI das camadas, e a copia so
		// levava as camadas. Uma miragem em pe ao lado de um corpo deitado entrega o truque.
		var copia = new Node2D { Name = "Fantasma", Rotation = Rotation };
		foreach (AnimatedSprite2D s in _camadas)
		{
			if (!s.Visible || s.SpriteFrames is not { } f || !f.HasAnimation(s.Animation)) continue;
			Texture2D? tex = f.GetFrameTexture(s.Animation, s.Frame);
			if (tex == null) continue;

			var q = new Sprite2D
			{
				Texture = tex,
				Centered = s.Centered,
				Offset = s.Offset,
				Position = s.Position,
				FlipH = s.FlipH,
				TextureFilter = TextureFilterEnum.Nearest,

				// O `Modulate` VAI JUNTO, e ele so e diferente de branco numa camada: a COLADA pintada
				// (ver `ColadasDaForma`, onde a tinta multiplica em vez de somar). Sem esta linha o
				// vulto de um Legendary sairia com a fagulha CINZA, que e a cor crua da folha -- o
				// mesmo tombo que a tinta do cabelo ja levou aqui, num canal que nao existia na epoca.
				Modulate = s.Modulate,
			};

			// A ORDEM DA PILHA VAI JUNTO. E o unico jeito de saber, olhando so a foto, qual copia
			// e o cabelo e qual e o corpo -- as camadas nao tem nome, elas se identificam por este
			// meta (ver `NovaCamada`). Quem precisa: a bancada, que confere que o cabelo fica na
			// parte de cima da caixa do corpo em TODAS as direcoes; e quem for depurar um vulto.
			if (s.HasMeta("ordem")) q.SetMeta("ordem", s.GetMeta("ordem"));

			// A TINTA VAI JUNTO. Sem ela o cabelo do Super Saiyajin volta a preto no fantasma, e a
			// imagem remanescente de um SSJ sairia com o cabelo da forma base.
			//
			// ============================ SO SE ALGUEM FOR OLHAR ============================
			// O rastro de corrida TROCA este material por um de borrao no instante seguinte
			// (`BorraoDirecional.Aplicar`), entao pra ele a tinta e um `ShaderMaterial` criado e
			// jogado fora sem nunca ter desenhado nada. A 30 fotos por segundo e com quatro camadas
			// por foto, sao 120 materiais por segundo de puro desperdicio POR CORPO CORRENDO -- e o
			// suspeito numero um das travadinhas que o dono relatou, porque e o unico que roda o
			// tempo todo e justamente enquanto se anda.
			//
			// Quem vai borrar avisa (`comTinta: false`) e economiza a criacao inteira. Quem quer o
			// fantasma parado (a miragem do Zanzoken) continua pedindo a tinta.
			// ===============================================================================
			if (comTinta && s.Material is ShaderMaterial m)
			{
				// O PAR INTEIRO, e nao dois campos copiados a mao. Copiar so a cor deixaria o material
				// novo no modo SOMA (o padrao) com a cor da roupa -- e copiar so o MODO pinta a peca de
				// PRETO. Ler e escrever um `Tinta` nao deixa nenhuma das duas metades pra tras.
				var mat = new ShaderMaterial { Shader = ShaderTinta };
				new Tinta(m.GetShaderParameter("tinta").AsVector3(),
						  m.GetShaderParameter("tinta_modo").AsInt32()).Escrever(mat);
				q.Material = mat;
			}
			copia.AddChild(q);
		}
		return copia;
	}

	// =====================================================================
	// CORRIDA
	// =====================================================================
	/// <summary>Quanto o passo acelera a plena corrida. Casa com o `MultiplicadorCorrida` (2,2x).</summary>
	private const double RitmoDaCorrida = 2.2;

	private bool _correndo;

	/// <summary>
	/// ESTOU CORRENDO -- e com isso o passo acelera.
	///
	/// ============================ O BORRAO SAIU DAQUI ============================
	/// Havia um smear no shader: 4 amostras extras da PROPRIA textura ao longo do rumo. O dono
	/// reportou duas vezes que o personagem "pisca" correndo, e a segunda vez veio com o conserto
	/// junto: "n teria como o efeito ser um motion blur?".
	///
	/// Ele apontou a natureza do defeito. Aquilo NAO era motion blur -- era um borrao ESPACIAL
	/// calculado dentro do quadro atual da animacao. E como a animacao de corrida roda 2,2x mais
	/// rapido, cada troca de quadro trocava de uma vez todo o conteudo amostrado: o borrao dava um
	/// salto por quadro do .dmi, o que o olho le exatamente como piscada. Suavizar a direcao (o que
	/// tentei antes) nao alcanca isso, porque a descontinuidade nao esta na direcao -- esta na
	/// FONTE.
	///
	/// Motion blur de verdade e TEMPORAL: ele mostra onde o corpo ESTEVE. Quem faz isso agora e o
	/// <see cref="RastroDeCorrida"/>, que larga copias do corpo nas posicoes passadas. Copia velha
	/// guarda o quadro velho, entao trocar de quadro nao muda nada do que ja foi desenhado -- a
	/// piscada nao tem por onde nascer.
	/// ============================================================================
	/// </summary>
	public void Correr(bool correndo, Vector2 rumo) => _correndo = correndo;

	// O soco NAO volta ao normal por "animacao terminou": todo estado vindo do .dmi tem
	// loop=true (o BYOND repetia o ciclo eternamente), entao esse evento nunca dispararia.
	// Quem encerra a pose e o RELOGIO, dos dois lados.

	// =====================================================================
	// O ANIMADOR
	// =====================================================================
	// QUEM ANIMA E ESTA CLASSE, nao o AnimatedSprite2D. Nenhuma camada chama Play().
	//
	// A primeira tentativa foi "so o corpo toca e as outras copiam o quadro dele". Nao basta:
	// o avanco interno do sprite acontece num ponto do quadro e a copia noutro, entao as
	// camadas ficavam SEMPRE UM QUADRO ATRAS do corpo (medido: corpo no quadro 1 com todas as
	// camadas ainda no 0). Um relogio proprio elimina a corrida -- todas as camadas recebem o
	// quadro na MESMA passada, derivado do MESMO tempo.
	private double _relogio;

	/// <summary>Duracao total de uma animacao, em segundos.</summary>
	private static double Ciclo(SpriteFrames f, string anim)
	{
		if (!f.HasAnimation(anim)) return 0;
		double vel = Math.Max(f.GetAnimationSpeed(anim), 0.01);
		double total = 0;
		for (int i = 0; i < f.GetFrameCount(anim); i++) total += f.GetFrameDuration(anim, i) / vel;
		return total;
	}

	/// <summary>
	/// Em que quadro a animacao esta no instante <paramref name="t"/>, RESPEITANDO a duracao
	/// de cada quadro.
	///
	/// Nao da pra dividir o ciclo em partes iguais: o estado parado do corpo tem `delay =
	/// 1,1,1,30`, ou seja tres quadros de 0,1s (a piscada) e um segurando 3 segundos (de pe,
	/// olhos abertos). Distribuindo linearmente, cada quadro ganhava 0,82s e a piscada virava
	/// camera lenta -- foi o que o dono viu como "quase 5 segundos por piscada".
	/// </summary>
	private static int QuadroEm(SpriteFrames f, string anim, double t)
	{
		int n = f.GetFrameCount(anim);
		if (n <= 1) return 0;
		double vel = Math.Max(f.GetAnimationSpeed(anim), 0.01);

		double acc = 0;
		for (int i = 0; i < n; i++)
		{
			acc += f.GetFrameDuration(anim, i) / vel;
			if (t < acc) return i;
		}
		return n - 1;
	}

	// =====================================================================
	// IMPACTO
	// =====================================================================
	// O impacto e ANIMADO NO SHADER, em todas as camadas ao mesmo tempo -- corpo, roupa,
	// cabelo e rabo lavam juntos, que e o unico jeito de o personagem parecer UM objeto
	// levando um golpe em vez de uma pilha de sprites.
	//
	// A versao anterior usava o `Modulate` da raiz. Modulate MULTIPLICA: piscar de vermelho
	// ESCURECIA o boneco, ou seja levar um soco deixava o personagem mais apagado. Agora o
	// shader MISTURA em direcao a cor, o que clareia e preserva a silhueta.
	private double _flash, _flashTotal;
	private Vector2 _empurrao;

	/// <summary>
	/// Marca o impacto: lava a cor, acende o contorno, achata o corpo e empurra na direcao do
	/// golpe. <paramref name="direcao"/> vem normalizada (de quem bateu pra quem levou).
	/// </summary>
	public void Impacto(Color cor, Color contorno, Vector2 direcao, double segundos = 0.15)
	{
		_flash = _flashTotal = segundos;
		_empurrao = direcao * 3f;
		// O GESTO DO SOCO, e ele e reescrito TODA vez porque o canal e compartilhado com o
		// <see cref="Banhar"/>, que o zera. Sem estas duas linhas, o primeiro soco depois de uma
		// transformacao sairia sem achatamento -- o defeito silencioso de quem so escreve o estado
		// no lado que ele considera "o normal".
		_achatamento = 0.18f;
		_lavagem = 0.85f;

		foreach (AnimatedSprite2D s in _camadas)
		{
			if (s.Material is not ShaderMaterial m) continue;
			m.SetShaderParameter("flash_cor", cor);
			m.SetShaderParameter("contorno_cor", contorno);
		}
		AplicarImpacto(1);
	}

	/// <summary>
	/// O CONTORNO DA FORMA -- uma LINHA na cor dela, na borda da silhueta, enquanto a forma durar.
	///
	/// ============================ E OUTRO CONTORNO, DE PROPOSITO ============================
	/// O shader ja tinha um `contorno`, mas ele e do IMPACTO: um lampejo de 0,15 s que PINTA a
	/// borda de branco. Se a forma usasse o mesmo uniform, cada soco levado apagaria a marca do
	/// Super Saiyajin por um sexto de segundo -- e, pior, o impacto ficaria invisivel justamente em
	/// quem esta transformado. Sao dois canais e eles se sobrepoem. Ver `Personagem.gdshader`.
	///
	/// ============================ QUEM BRILHA E A AURA, NAO ISTO ============================
	/// Regra do dono, valendo pra TODAS as formas. O halo por fora do corpo e o uniform `aura_pulso`
	/// morreram juntos no shader: o contorno e um TRACO na borda da silhueta, e a diferenca entre um
	/// SSJ1 e um Limit Breaker parados passa a ser dita pela aura e pelos raios, que e onde ela cabe.
	///
	/// O PULSO VOLTOU, mas do lado de ca e como FORCA. Pedido novo do dono ("o contorno deveria
	/// ficar um pouco mais fraco e ele ficar pulsando lentamente"): quem varia e o `aura` que este
	/// metodo ja escrevia, e nao um uniform novo -- ver <see cref="ForcaNaFaseDoPulso"/>. Aumentar o
	/// shader pra isso seria pagar de novo o preco que o `aura_pulso` cobrou: um canal a mais pra
	/// dizer o que um numero que ja trafega ali sabe dizer.
	/// ========================================================================================
	///
	/// ============================ E ELE PODE OSCILAR ENTRE DUAS CORES ============================
	/// <paramref name="alterna"/> nulo (o caso de 35 das 36 formas) e "contorno parado": a cor e
	/// escrita uma vez e o `_Process` nao toca mais nela. Nao-nulo e a OUTRA ponta, e ai o contorno
	/// vai e volta entre as duas em <see cref="Jandirus.Core.Forms.Catalogo.SegundosDoCicloDoContorno"/>.
	/// Hoje so o Beast pede isso.
	///
	/// O PARAMETRO E OBRIGATORIO de proposito -- podia ter `= null` e nao tem. Com valor padrao, um
	/// chamador esquecido compilaria e simplesmente desenharia a Fera travada no azul, calado; sem
	/// ele, o compilador aponta os seis chamadores um por um. E o mesmo motivo pelo qual a `Aura`
	/// deixou de ser um campo so: o que nao e perguntado nao e respondido.
	///
	/// <paramref name="forca"/> e o TOPO do pulso e nao o valor desenhado -- ver
	/// <see cref="ForcaNaFaseDoPulso"/>. 0 apaga, e apagado o contorno nao anda (ver `_Process`).
	/// ==========================================================================================
	/// </summary>
	public void AuraDaForma(Color cor, float forca, Color? alterna)
	{
		// OS RELOGIOS SO ZERAM QUANDO O PAR MUDA, e nao a cada chamada. Este metodo e reescrito a cada
		// pacote de sobrecarga (`World.AplicarContorno` roda no `aura_ki` e a cada snapshot alheio) com
		// exatamente as mesmas cores; zerando sempre, a oscilacao ficaria presa perto do azul e
		// nunca fecharia meia volta. Zerando so na troca, cada transformacao estreia na cor A.
		//
		// O PULSO ZERA NA MESMA HORA e pelo mesmo motivo, com um ganho proprio: fase 0 e o TOPO do
		// pulso (ver `ForcaNaFaseDoPulso`), entao o contorno estreia cheio e so depois respira. Se ele
		// estreasse no vale, cruzar os 100% de Ki acenderia a coisa no ponto mais fraco dela.
		if (cor != _corDoContorno || alterna != _outraCorDoContorno) _relogioDaCor = _relogioDoPulso = 0;
		_corDoContorno = cor;
		_outraCorDoContorno = alterna;
		// A FORCA PEDIDA E GUARDADA, e o que vai pro shader e a fase dela. Esta linha morava dentro do
		// `EscreverContorno` -- e la ela guardaria o valor JA PULSADO, ou seja o pulso comeria a si
		// mesmo: cada quadro multiplicaria de novo o resultado do anterior e o contorno decairia a
		// zero em poucos segundos, sem nunca voltar.
		_auraDaForma = forca;
		// PELA FASE, E NAO PELOS VALORES CRUS. Escrevendo `cor`/`forca` aqui, toda chamada no meio da
		// animacao jogaria o contorno de volta pro comeco por um quadro -- e este metodo e chamado a
		// cada pacote de sobrecarga (`World.AplicarContorno`), ou seja o Beast piscaria azul e o
		// pulso estalaria no topo toda vez que o Ki cruzasse o limite. Os relogios mandam.
		EscreverContorno(CorNaFaseDaOscilacao(), ForcaNaFaseDoPulso());
	}

	/// <summary>
	/// A cor que o contorno tem NESTE instante: a ponta A quando ele nao oscila, ou o ponto do
	/// caminho entre as duas pontas em que o relogio esta. Fonte unica das duas escritas (a da troca
	/// de forma e a do quadro), pra as duas nao poderem discordar sobre onde a oscilacao esta.
	/// </summary>
	private Color CorNaFaseDaOscilacao()
	{
		if (_outraCorDoContorno is not { } outra) return _corDoContorno;
		double ciclo = Jandirus.Core.Forms.Catalogo.SegundosDoCicloDoContorno;
		return _corDoContorno.Lerp(outra, (float)((1 - Math.Cos(_relogioDaCor / ciclo * Math.Tau)) * 0.5));
	}

	/// <summary>
	/// A FORCA que o contorno tem NESTE instante: a pedida, respirando entre o topo e o
	/// <see cref="Jandirus.Core.Forms.Catalogo.PisoDoPulsoDoContorno"/>. Irma da
	/// <see cref="CorNaFaseDaOscilacao"/> e pelo mesmo motivo -- fonte unica das duas escritas (a da
	/// troca de forma e a do quadro), pra as duas nao poderem discordar sobre onde o pulso esta.
	///
	/// ============================ ELE NAO ACENDE NADA SOZINHO ============================
	/// O pulso e um FATOR sobre a forca pedida, nunca uma parcela: com `_auraDaForma` em 0 (Ki abaixo
	/// dos 100%) o resultado e 0 em toda fase do relogio. A regra do dono e obedecida pela
	/// aritmetica, e nao por uma guarda -- guarda se esquece, multiplicacao por zero nao.
	///
	/// FASE 0 E O TOPO (`Lerp(1, piso, ...)` e nao o contrario): quem acabou de cruzar os 100% ve o
	/// contorno cheio e ele afrouxa depois, em vez de nascer no ponto mais apagado.
	///
	/// COSSENO pelo mesmo motivo da cor -- ver `AnimarContorno`.
	/// ==============================================================================
	/// </summary>
	private float ForcaNaFaseDoPulso()
	{
		double ciclo = Jandirus.Core.Forms.Catalogo.SegundosDoPulsoDoContorno;
		float fase = (float)((1 - Math.Cos(_relogioDoPulso / ciclo * Math.Tau)) * 0.5);
		return _auraDaForma * Mathf.Lerp(1f, (float)Jandirus.Core.Forms.Catalogo.PisoDoPulsoDoContorno, fase);
	}

	/// <summary>
	/// Escreve o contorno nas camadas de SILHUETA. Ponto unico: o `_Process` da animacao passa por
	/// aqui tambem, senao a pelagem do SSJ4 (que e uma camada criada depois, com material novo)
	/// ficaria com a cor da estreia enquanto o resto do corpo muda.
	/// </summary>
	private void EscreverContorno(Color cor, float forca)
	{
		foreach (AnimatedSprite2D s in _camadas)
		{
			if (!EhSilhueta(s) || s.Material is not ShaderMaterial m) continue;
			m.SetShaderParameter("aura_cor", cor);
			m.SetShaderParameter("aura", forca);
		}
	}

	/// <summary>
	/// ============================ O CONTORNO E DA SILHUETA, E O OLHO NAO E SILHUETA ============================
	/// Defeito visto pelo dono: *"o contorno ... ta mudando a cor dos olhos"*. E o shader estava
	/// certo -- o errado era a lista de quem recebe.
	///
	/// A CONTA DO SHADER: `borda = c.a * (1 - viz)`, com `viz` = o alfa dos quatro vizinhos. Ela quer
	/// dizer "pixel opaco encostado em vazio", e num desenho GRANDE isso e mesmo o fio de fora. O
	/// olho e um sprite de meia duzia de pixels: nele quase todo pixel tem vizinho transparente,
	/// entao `borda` da ~1 no desenho INTEIRO e o "contorno" pinta o olho de ponta a ponta. Nao ha
	/// numero que conserte isso -- e a propria definicao de borda que nao se aplica a um detalhe.
	///
	/// ============================ POR QUE POR CAMPO E NAO POR `ordem` NEM POR FLAG ============================
	/// A pilha (`NovaCamada`) numera corpo 0, rabo/forma 1, roupa 2.., cabelo 10, olhos 11 -- e daria
	/// pra cortar em "ordem &lt; 11". So que `ordem` significa ORDEM DE DESENHO e mais nada: no dia em
	/// que um detalhe novo (uma cicatriz, uma marca de rosto) entrar em 12 ele herda o contorno
	/// calado, e no dia em que alguem reempilhar as camadas a regra muda de significado sem ninguem
	/// tocar nela. Sao dois conceitos diferentes viajando no mesmo numero.
	///
	/// Um uniform ou um `SetMeta("silhueta")` seria ESTADO NOVO: cada camada teria que lembrar de se
	/// declarar no nascimento, e a que esquecesse falharia em silencio -- exatamente o modo de falha
	/// que este arquivo ja documenta em outros pontos.
	///
	/// Aqui a pergunta e DERIVADA dos campos que a classe ja tem, e e o mesmo idioma do
	/// <see cref="Escondida"/> logo abaixo, que ja decide comportamento por camada assim. Nao ha
	/// campo novo, nao ha uniform novo e nao ha nada pra sincronizar: o dia em que alguem
	/// acrescentar um detalhe, ele acrescenta um campo -- e o campo cai ao lado deste metodo.
	///
	/// CABELO, RABO, ROUPA E CORPO DA FORMA FICAM: os quatro sao pedacos grandes que desenham a
	/// borda de fora do boneco, e sem eles a linha se interromperia justamente onde o personagem tem
	/// contorno de verdade (o topete do Super Saiyajin, a ponta do rabo).
	///
	/// ============================ E AS COLADAS SAEM PELO MESMO MOTIVO DO OLHO ============================
	/// As folhas de <see cref="ColadasDaForma"/> sao ESPARSAS por desenho -- a `LSSJpowerz` acende 374
	/// pixels num quadro de 6144. Nelas quase todo pixel tem vizinho vazio, `borda` da ~1 no desenho
	/// inteiro, e o "contorno" repinta a fagulha de ponta a ponta. E o defeito do olho, na mesma conta.
	///
	/// E antes disso: contorno e o traco da SILHUETA DO LUTADOR. A colada nao e o lutador, e luz por
	/// cima dele -- contorna-la seria desenhar a borda duas vezes, e na segunda por cima do brilho que
	/// deveria estar apagando essa borda.
	/// ==================================================================================================
	/// </summary>
	private bool EhSilhueta(AnimatedSprite2D s) => !ReferenceEquals(s, _olhos) && !EhColada(s);

	/// <summary>As duas pontas do contorno. <see cref="_outraCorDoContorno"/> nulo = nao oscila.</summary>
	private Color _corDoContorno = Colors.White;
	private Color? _outraCorDoContorno;
	private double _relogioDaCor;

	/// <summary>
	/// O relogio do PULSO DE FORCA. Separado do <see cref="_relogioDaCor"/> de proposito: os dois
	/// tem periodos diferentes justamente pra nao travarem em fase -- ver
	/// <see cref="Jandirus.Core.Forms.Catalogo.SegundosDoPulsoDoContorno"/>.
	/// </summary>
	private double _relogioDoPulso;

	/// <summary>
	/// ============================ O CONTORNO SE MEXE EM DOIS EIXOS ============================
	/// A COR troca (so o Beast, hoje) e a FORCA respira (todas as formas). O CORE diz quais sao as
	/// duas cores, qual e o fundo do pulso e quanto dura cada volta (dado puro, sem relogio -- ver
	/// `Catalogo.CorDoContornoAlterna`); daqui pra frente e tempo, e tempo e do cliente. Este e o
	/// unico ponto do jogo que conta quadro pro contorno de uma forma.
	///
	/// ============================ OS DOIS EIXOS NAO SE ANULAM ============================
	/// Cada um tem RELOGIO e PERIODO proprios (2,6 s o pulso contra 4,0 s a cor). Um relogio so, ou
	/// dois com o mesmo periodo, travaria os dois em fase pra sempre: a Fera ficaria mais fraca
	/// exatamente quando vira roxa, e o olho leria UMA animacao em vez de duas. Sao coisas
	/// diferentes -- "que forma e essa" e "quanto estou passando do meu limite" -- e elas tem que
	/// poder ser lidas separadas.
	///
	/// ============================ A GUARDA E O TRABALHO ============================
	/// `_auraDaForma <= 0` e a porta, e e a que o dono pediu com todas as letras: o contorno so
	/// acende acima dos 100% de Ki, e a animacao NAO PODE acende-lo nem gastar quadro enquanto ele
	/// esta apagado. Um Beast parado, com Ki normal, custa exatamente uma comparacao por quadro --
	/// nenhuma escrita de uniform, nenhum laco de camadas. E os relogios nem andam: apagado o tempo
	/// nao passa, entao acender de novo continua de onde parou em vez de saltar pra uma fase
	/// qualquer.
	///
	/// A COR TEM UMA GUARDA A MAIS (`_outraCorDoContorno == null`), e a forca nao tem irma dela: 35
	/// das 36 formas nao oscilam de cor e nao devem pagar o `%` de um relogio que nao usam, enquanto
	/// o pulso vale pra todas. Sem essa segunda porta o SSJ3 inteiro passaria a mudar de cor.
	///
	/// (Nao da pra `SetProcess(false)` aqui: este `_Process` tambem sincroniza os quadros de todas
	/// as camadas do boneco. Desligar o node pra economizar a animacao pararia o boneco inteiro.)
	///
	/// ============================ COSSENO, E NAO VAI-E-VEM RETO ============================
	/// `(1 - cos(2 pi t)) / 2` sobe e desce sem canto: nas duas pontas a velocidade chega a zero e
	/// inverte suavemente. Uma interpolacao linear em pingue-pongue passa pelo mesmo caminho, mas
	/// vira de direcao num quadro so -- e e exatamente nesse instante que o olho ve o "corte duro"
	/// que o pedido proibe, mesmo sem nenhum salto de cor. Vale pros dois eixos: no brilho um canto
	/// desses le como ESTALO, que e pior do que na cor.
	/// ==============================================================================
	/// </summary>
	private void AnimarContorno(double delta)
	{
		if (_auraDaForma <= 0f) return;

		if (_outraCorDoContorno != null)
			_relogioDaCor = (_relogioDaCor + delta) % Jandirus.Core.Forms.Catalogo.SegundosDoCicloDoContorno;
		_relogioDoPulso = (_relogioDoPulso + delta) % Jandirus.Core.Forms.Catalogo.SegundosDoPulsoDoContorno;

		EscreverContorno(CorNaFaseDaOscilacao(), ForcaNaFaseDoPulso());
	}

	/// <summary>
	/// O CORPO PROPRIO DA FORMA -- a pelagem do SSJ4, ou o macaco inteiro do Oozaru. Vazio/nulo tira.
	///
	/// E uma CAMADA por cima do corpo, e nao uma troca do corpo: assim a roupa, as feridas e o
	/// tom de pele continuam valendo por baixo, e voltar ao normal e so remover a camada. Trocar o
	/// sprite do corpo obrigaria a guardar o antigo e a restaura-lo -- que e onde o DM tem o
	/// `ussj_saved_icon` e um comentario sobre relogar no meio da forma.
	///
	/// ============================ MENOS QUANDO A FORMA E OUTRO BICHO ============================
	/// O Oozaru quebra o desenho acima: `Oozaru.dm:123-139` faz `overlayList.Remove(overlayList)`,
	/// `RemoveHair()` e `container.icon = targicon` -- ele NAO poe uma pelagem por cima, ele TROCA
	/// o mob por um macaco de 96x96. Cabelo, olhos, roupa e rabo somem; nao ha camisa que caiba
	/// num bicho tres vezes maior.
	///
	/// A distincao e DERIVADA do tamanho do quadro (ver <see cref="EhCriatura"/>) e nao de um
	/// campo novo no catalogo: a folha ja sabe o tamanho dela, e um `bool SubstituiTudo` seria um
	/// segundo lugar dizendo o que o `.tres` ja diz -- com a chance de os dois discordarem.
	/// ==========================================================================================
	/// </summary>
	public void CorpoDaForma(Jandirus.Core.Forms.CorpoDeForma simbolo)
	{
		// ============================ O SIMBOLO VIRA CAMINHO AQUI, E COM A PELE JUNTO ============================
		// O `Musculoso` e o unico que depende de QUEM esta se transformando, e a resposta esta a uma
		// linha de distancia: o `Trocar` guarda no meta `src` o caminho da folha que cada camada
		// veste, entao o corpo base sabe dizer a propria pele. E o `container.icon` do
		// `apply_ussj_body()` (`supersaiyanbuff.dm:357`), que compara icone com icone pelo mesmo motivo.
		//
		// Nao ha ficha de aparencia nesta classe nem nos chamadores (`Transformacao.Vestir`,
		// `World.VestirAFormaSemCena`), e nao precisa haver: puxar `Appearance` ate aqui so pra ler
		// o tom de pele acrescentaria uma segunda fonte pra um dado que o boneco JA carrega -- e as
		// duas divergiriam no instante em que alguem trocasse de corpo no guarda-roupa.
		//
		// `null` de volta = nao ha folha musculosa pra esta pele (toda mulher, e toda raca fora das
		// tres humanas). Cai no mesmo caminho do `Nenhum`: o corpo fica o que era. Ver
		// `CorposDeForma.Musculoso`.
		// ====================================================================================================
		string? caminho = CorposDeForma.Caminho(simbolo, _corpo?.GetMeta("src", "").AsString() ?? "");

		if (string.IsNullOrEmpty(caminho))
		{
			// E O SIMBOLO GUARDADO TEM QUE CAIR JUNTO. Ele responde "esta folha desenha o proprio
			// rabo?" pra `Escondida`; deixa-lo aceso sem camada esconderia o rabo de um lutador que
			// voltou ao normal -- e o rabo do Saiyajin nao e enfeite (sem ele nao ha Oozaru).
			_simboloDoCorpo = Jandirus.Core.Forms.CorpoDeForma.Nenhum;

			if (_corpoDaForma != null && IsInstanceValid(_corpoDaForma))
			{
				_camadas.Remove(_corpoDaForma);
				RemoveChild(_corpoDaForma);
				_corpoDaForma.QueueFree();
			}
			_corpoDaForma = null;
			_criatura = false;
			PararCrescimento();

			// E TUDO VOLTA -- corpo, roupa, cabelo, olhos e RABO. Quem devolve e o `Aplicar`, pela
			// `Escondida`, e agora ele e o unico dono da visibilidade: a versao anterior reacendia o
			// rabo com uma linha propria aqui, e essa linha era metade de uma regra escrita em dois
			// lugares. Sem o rabo de volta nao ha Oozaru, que e justamente a porta do SSJ4.
			Aplicar(force: true);
			return;
		}

		var frames = ResourceLoader.Load<SpriteFrames>(caminho);
		if (frames == null) { GD.PushWarning($"[visual] corpo de forma nao carregou: {caminho}"); return; }

		_simboloDoCorpo = simbolo;

		// ORDEM 1: logo acima do corpo (0) e abaixo da roupa -- a pelagem fica na pele, e a roupa
		// continua por cima dela. Quem esconde o rabo (e, no macaco, o resto) e a `Escondida`.
		//
		// E O MUSCULOSO FICA NA MESMA ORDEM, mesmo sendo um corpo INTEIRO e nao uma pelagem: ele e
		// opaco e do mesmo tamanho, entao cobre a pele de baixo sem esconder a roupa (2..5), o
		// cabelo (10) nem os olhos (11) -- que e exatamente o que o DM faz trocando `container.icon`
		// e deixando o `overlayList` intacto (`supersaiyanbuff.dm:363`).
		_corpoDaForma ??= NovaCamada(1);
		_corpoDaForma.SpriteFrames = frames;

		// ============================ ALINHAR PELOS PES, NAO PELO CENTRO ============================
		// Toda camada e `Centered = true`, o que poe o MEIO do quadro na origem do node. Num quadro
		// de 32 os pes caem em +16; num de 96 cairiam em +48 -- o macaco nasceria 32 px enterrado no
		// chao, e o Y-sort (que ordena pela origem) continuaria achando que ele esta um andar acima
		// de onde ele parece estar.
		//
		// A conta ancora a BASE do quadro na base do quadro base: `(base - dele) / 2`. Da 0 pra
		// quem tem o mesmo tamanho (o SSJ4 nao muda um pixel) e -32 pro macaco. Derivada da folha,
		// entao uma arte de outro tamanho ja nasce alinhada.
		//
		// O DM resolve o mesmo problema com `pixel_x = -16; pixel_y = -16` cravados no `spawn` --
		// numeros que so servem pra um icone de 96 e que ninguem sabe de onde vieram ao ler.
		float alturaBase = TamanhoDoQuadro(_corpo?.SpriteFrames).Y;
		if (alturaBase <= 0) alturaBase = 32f;
		float alturaDele = TamanhoDoQuadro(frames).Y;
		_corpoDaForma.Offset = new Vector2(0, (alturaBase - alturaDele) * 0.5f);

		bool eraCriatura = _criatura;
		_criatura = alturaDele > alturaBase;
		if (_criatura && !eraCriatura) Crescer();
		// ============================ CAMADA NOVA TEM QUE PASSAR PELO `Aplicar` ============================
		// Aqui havia `Animation = _corpo.Animation; Play();` -- o UNICO `Play()` da classe, contra
		// o contrato escrito la em cima ("nenhuma camada chama Play(); quem manda no quadro e o
		// `_Process`").
		//
		// O estrago: pro corpo do jogador NAO existe estado `default_*` nas folhas base -- so
		// `walk_*`. Ficar parado nao e outra animacao, e a caminhada PRESA no quadro 0 pelo meta
		// `sync`. E o `sync` so e escrito dentro do `Aplicar`. Uma camada nascida aqui nunca
		// passava por ele, entao herdava o padrao `true` e saia percorrendo o ciclo de
		// `walk_south` por conta propria -- o corpo do SSJ4 ANDANDO no meio da cinematica,
		// por fora das tres portas de pose (e por fora da tranca).
		//
		// `Aplicar(force: true)` resolve os dois: escolhe o nome certo pra folha nova e escreve
		// o `sync` de TODAS as camadas, inclusive desta.
		//
		// ============================ E SEM GUARDA NENHUMA, QUE ERA UM BURACO ============================
		// A chamada vivia dentro de `if (_corpo != null && !_corpo.Animation.IsEmpty &&
		// frames.HasAnimation(_corpo.Animation))`. O ultimo termo faz a pergunta que o `Escolher`
		// existe pra responder -- com toda a cadeia de substitutas (direcao, familia, apelido
		// `_mov`, sul) --, e responde pior que ele.
		//
		// Com a pelagem do SSJ4 (que saiu do mesmo .dmi do corpo) os nomes batiam sempre e ninguem
		// percebeu. O macaco tem OUTRO conjunto: ele traz `flight_south`, e o corpo em voo esta em
		// `flight_mov_south`. Quem olhasse pra lua VOANDO cairia na guarda, a camada nasceria sem
		// `Animation` nenhuma, e o macaco simplesmente NAO SERIA DESENHADO ate a proxima troca de
		// pose -- e agora que ele apaga o corpo por baixo, isso seria um jogador invisivel.
		//
		// Chamar sempre e o que o `Vestir` ja faz no fim dele.
		Aplicar(force: true);
		Reordenar();
	}

	private AnimatedSprite2D? _corpoDaForma;

	/// <summary>
	/// QUAL corpo de forma esta vestido. Nao e so registro: e a `Escondida` que o le, pra saber se a
	/// folha desenha o proprio rabo (<see cref="Jandirus.Core.Forms.Catalogo.FolhaTrazORabo"/>).
	///
	/// Nao da pra derivar do node como se faz com o <see cref="_criatura"/> (tamanho do quadro): o
	/// corpo musculoso tem os MESMOS 32x32 da pelagem do SSJ4 e a mesma ordem na pilha -- o que os
	/// separa e o que a arte desenha, e isso so o simbolo sabe.
	/// </summary>
	private Jandirus.Core.Forms.CorpoDeForma _simboloDoCorpo = Jandirus.Core.Forms.CorpoDeForma.Nenhum;

	// =====================================================================
	// AS CAMADAS COLADAS NO CORPO
	// =====================================================================
	/// <summary>
	/// ONDE A COLADA ENTRA NA PILHA: 12, acima dos olhos (11) e portanto acima de TUDO.
	///
	/// ============================ O NUMERO E O DO ORIGINAL, NAO UM GOSTO MEU ============================
	/// `Overlays.dm:1-9` da os planos do BYOND: corpo 2, roupa 3, cabelo 4, chapeu 5, e
	/// `AURA_LAYER = 7` com o comentario "aura layer 1 over clothes". As duas coladas vivem em 7 --
	/// `/obj/overlay/auras/gk` (`godki.dm:236`) e `/obj/overlay/effects` (`EffectLayer.dm:2`). Ou seja
	/// no original elas desenham por cima de roupa E de cabelo, e e o que 12 faz aqui.
	///
	/// E TEM QUE SER POR CIMA: e um brilho de ki no contorno do corpo. Debaixo da camisa, o efeito
	/// sumiria justamente em quem esta vestido -- que e todo mundo.
	/// ==============================================================================================
	/// </summary>
	private const int OrdemDaColada = 12;

	/// <summary>
	/// As camadas coladas ATIVAS, na ordem em que o <see cref="Jandirus.Core.Forms.Catalogo.Coladas"/>
	/// as devolveu. Vazia e o caso da grande maioria das formas.
	/// </summary>
	private readonly List<AnimatedSprite2D> _coladas = [];

	/// <summary>
	/// O RELOGIO QUE NAO PARA -- ver o bloco dele no `_Process`. Serve a quem tem ciclo proprio (TODA
	/// camada de efeito, ou seja toda colada), e por isso e separado do `_relogio`, que e zerado a cada
	/// troca de pose. Nao ser zerado e o que garante que a aura nunca fique presa no primeiro quadro.
	/// </summary>
	private double _relogioSolto;

	/// <summary>
	/// ============================ O OVERLAY COLADO NO CORPO, POR FORMA ============================
	/// Instala (ou tira) as camadas que a forma cola no boneco -- as fagulhas do Legendary, o brilho
	/// do ki divino. `null` desfaz, e desfazer e a mesma linha de reverter pra base.
	///
	/// ============================ POR QUE ISTO NAO E UMA FOLHA DE AURA ============================
	/// Ver o cabecalho de <see cref="Jandirus.Core.Forms.FolhaColada"/>. Em uma frase: a aura e um node
	/// IRMAO de 96x96 desenhado atras do corpo, com UMA animacao; isto e um sprite de 32x32 que veste
	/// o corpo POSE POR POSE. As tres folhas grandes (`god`, `god blue`, `god - grey`) trazem as mesmas
	/// 24 animacoes do corpo -- `walk_south`, `attack_east`, `flight_north`, `kb`, `ko` --, e foi isso
	/// que decidiu o desenho: elas ja foram desenhadas pra ser camada.
	///
	/// ============================ E POR QUE E UMA LISTA E NAO UMA CAMADA ============================
	/// O Legendary usa DUAS ao mesmo tempo (`LSSJpowerz` mais a cinza pintada de verde). Foi
	/// exatamente isso que impediu a ideia de caber em `Catalogo.Folha`, que devolve uma.
	///
	/// ============================ O QUE ELA NAO RECEBE ============================
	///   * CONTORNO -- ela sai da <see cref="EhSilhueta"/>, e pelo mesmo motivo dos OLHOS: a conta do
	///     shader (`borda = c.a * (1 - viz)`) descreve um fio de fora em desenho CHEIO, e estas folhas
	///     sao esparsas de proposito (a `LSSJpowerz` acende 374 pixels em 6144). Quase todo pixel dela
	///     tem vizinho vazio, entao `borda` da ~1 no desenho inteiro e o contorno a repintaria de ponta
	///     a ponta. E, no fundo: contorno e o traco da SILHUETA do lutador, e isto e luz por cima dela.
	///   * FERIDA -- o `Ferir` so procura `_corpo` e `_roupa`, entao ela ja passa batido. Certo: ki nao
	///     hematoma, e um hematoma pintado no brilho seria uma mancha flutuando.
	///
	/// ============================ E O QUE ELA RECEBE ============================
	/// A pose, como toda camada (o `Aplicar` escreve o `sync`), e o sumico do Oozaru -- a
	/// <see cref="Escondida"/> ja apaga TUDO quando o corpo vira criatura, sem uma linha nova.
	/// ==========================================================================================
	/// </summary>
	public void ColadasDaForma(Jandirus.Core.Forms.FormaDef? d)
	{
		Jandirus.Core.Forms.Colada[] quero = Jandirus.Core.Forms.Catalogo.Coladas(d);

		// SOBRA SE DESCARTA. A lista encolhe quando a forma nova pede menos camadas que a anterior --
		// sair do Legendary (duas) pro Blue (uma) tem que APAGAR a segunda, senao a fagulha verde fica
		// grudada no divino pra sempre. E o mesmo tombo do `ussj_saved_icon` do DM, um andar acima.
		// `Descartar` MEXE NA `_camadas`, NAO NESTA. Ele e o dono da pilha de desenho e nao sabe que
		// existe uma segunda lista aqui -- entao quem tira daqui e este laco. Sem a linha de baixo o
		// node continuava na `_coladas` depois de descartado, e a proxima troca de forma tentava
		// `RemoveChild` num node que ja nao era filho: `Condition "p_child->data.parent != this"`, aos
		// montes, na primeira execucao da bancada.
		for (int i = _coladas.Count - 1; i >= quero.Length; i--)
		{
			if (IsInstanceValid(_coladas[i])) Descartar(_coladas[i]);
			_coladas.RemoveAt(i);
		}

		for (int i = 0; i < quero.Length; i++)
		{
			string caminho = ColadasDeForma.CaminhoDa(quero[i].Folha);
			var frames = ResourceLoader.Load<SpriteFrames>(caminho);
			if (frames == null) { GD.PushWarning($"[visual] colada nao carregou: {caminho}"); continue; }

			// CAMADA MORTA SE SUBSTITUI EM VEZ DE SE REUSAR: uma troca de aparencia (`Vestir`) pode
			// derrubar nodes por baixo desta lista, e escrever num node liberado e o mesmo erro de
			// cima pelo outro lado.
			if (i < _coladas.Count && !IsInstanceValid(_coladas[i])) _coladas[i] = NovaCamada(OrdemDaColada);
			else if (i >= _coladas.Count) _coladas.Add(NovaCamada(OrdemDaColada));
			AnimatedSprite2D s = _coladas[i];
			s.SpriteFrames = frames;

			// ANCORADA PELOS PES, a mesma conta do `CorpoDaForma`: as folhas de hoje sao todas 32x32 e
			// a conta da zero, e ela existe pra a proxima -- de qualquer tamanho -- ja nascer alinhada.
			float alturaBase = TamanhoDoQuadro(_corpo?.SpriteFrames).Y;
			if (alturaBase <= 0) alturaBase = 32f;
			s.Offset = new Vector2(0, (alturaBase - TamanhoDoQuadro(frames).Y) * 0.5f);

			// ============================ A TINTA AQUI MULTIPLICA, E NAO SOMA ============================
			// `Modulate` e o `color = rgb(110,255,140)` do `EffectLayer.dm:32` -- na linguagem do BYOND o
			// `color` de um atom multiplica. E tem que multiplicar: a folha `god - grey` e cinza CLARO
			// (medida: 150..192 nos tres canais), e somar cor num cinza de 192 estoura tudo pro branco.
			//
			// E o oposto do que o resto desta classe faz, de proposito. Corpo, cabelo e olho somam
			// (`ICON_ADD`) porque os sprites deles sao moldes quase PRETOS -- medido: `Hair_Goku.png` e
			// preto puro no tom dominante. O DM usa as duas operacoes pelo mesmo criterio, e nao ha uma
			// resposta certa pras duas: a operacao pertence a FOLHA, nao a classe.
			//
			// BRANCO E "NAO TINGE" AQUI (multiplicar por 1 nao mexe em nada) -- ao contrario da aura,
			// onde branco APAGA a arte. As duas folhas ja coloridas passam por este ramo.
			//
			// ============================ E ESTA LINHA JA FOI ESCRITA NO VAZIO ============================
			// Tudo acima estava certo e o dono via CINZA assim mesmo: o `Personagem.gdshader` terminava em
			// `COLOR = c` e nunca lia o `COLOR` que o Godot entrega, entao o `Modulate` de toda esta pilha
			// era descartado sem um aviso. Escrever a regra nao e liga-la -- o mesmo modo de falhar do
			// corte de sigilo de BP, cuja API ficou inteira e orfa. A trava contra a volta disso mora na
			// `RoboDeForma.AsDuasContasSaoAsDoShader`, junto das outras tres do shader.
			//
			// E O CEU NAO ENTRA POR ESTE CANAL, medido depois que se suspeitou disso: o `CanvasModulate` do
			// mundo e aplicado pelo Godot DEPOIS do fragment, e nao pelo `modulate` do node (a prova, com
			// numeros, esta no fim do `Personagem.gdshader`). Ligar o canal nao pos o boneco debaixo da luz
			// do dia -- ele ja estava, desde sempre, exatamente como o cenario.
			// =========================================================================================
			s.Modulate = quero[i].Tinta is { } hexa ? new Color(hexa) : Colors.White;
		}

		Aplicar(force: true);
		Reordenar();
	}

	/// <summary>
	/// ESTA CAMADA E UMA COLADA? Derivada da lista, como o <see cref="EhSilhueta"/> e a
	/// <see cref="Escondida"/> derivam dos campos -- sem `SetMeta` novo, que e o modo de falhar calado
	/// que esta classe ja documenta em tres lugares.
	/// </summary>
	private bool EhColada(AnimatedSprite2D s)
	{
		foreach (AnimatedSprite2D c in _coladas) if (ReferenceEquals(c, s)) return true;
		return false;
	}

	/// <summary>
	/// ESTA CAMADA E UM EFEITO, OU E PARTE DO DESENHO DO CORPO?
	///
	/// ============================ A PERGUNTA QUE DIZ QUEM TEM RELOGIO PROPRIO ============================
	/// A pilha tem duas naturezas misturadas, e ate aqui so havia uma resposta pras duas:
	///
	///   PARTE DO CORPO -- cabelo, roupa, rabo, corpo-da-forma. Sao o MESMO desenho que o corpo, so que
	///   recortado em folhas. Tem que casar QUADRO A QUADRO: um quadro fora de fase e um braco no lugar
	///   errado, uma camisa no passo que a perna nao esta dando. Continuam travadas na fase do corpo.
	///
	///   EFEITO -- as coladas. Sao brilho de ki POR CIMA do boneco, nao um pedaco dele. Precisam casar
	///   so a POSE e a DIRECAO (o boneco de perfil nao pode ter a aura de frente); o QUADRO e assunto da
	///   folha delas. Andam no relogio proprio.
	///
	/// E o que o `VIS_INHERIT_ICON_STATE` do BYOND (`Overlays.dm:19`) de fato herda: o NOME DO ESTADO. O
	/// `vis_flags` nao tem nenhuma bandeira que herde INDICE DE QUADRO -- isso foi invencao nossa, e e a
	/// causa medida do slideshow (ver o bloco no `_Process`).
	///
	/// DERIVADA, e nao um `if` por ordem na pilha nem por id de node -- o mesmo idioma da
	/// <see cref="Escondida"/> e da <see cref="EhSilhueta"/>. A lista `_coladas` ja E a resposta: quem
	/// entra nela vem de `Catalogo.Coladas`, e o tipo `Colada` do Core e literalmente "efeito grudado no
	/// corpo". O <see cref="_corpoDaForma"/> NAO entra nela (ele nasce em `CorpoDaForma`, na ordem 1) --
	/// e tem que continuar nao entrando: ele e a pelagem do SSJ4 e o corpo do Oozaru, ou seja o proprio
	/// boneco, e solta-lo faria o macaco andar fora de compasso com as pernas que ele mesmo desenha.
	/// ==================================================================================================
	/// </summary>
	private bool EhEfeito(AnimatedSprite2D s) => EhColada(s);

	/// <summary>Quantas camadas coladas estao instaladas agora. Pra bancada.</summary>
	public int ColadasDeTeste => _coladas.Count;

	/// <summary>A folha e a tinta de cada colada, como o node REALMENTE ficou. Pra bancada.</summary>
	public (string Folha, Color Tinta)[] ColadasNoCorpoDeTeste
	{
		get
		{
			var r = new List<(string, Color)>();
			foreach (AnimatedSprite2D c in _coladas)
				if (IsInstanceValid(c) && c.SpriteFrames is { } f)
					r.Add((f.ResourcePath, c.Modulate));
			return [.. r];
		}
	}

	/// <summary>
	/// O QUADRO que cada colada esta desenhando agora. Pra bancada -- ver `--diagforma`.
	///
	/// Existe porque a bancada era CEGA justo pro defeito do slideshow: ela conferia folha, tinta e pose
	/// (dados escritos no `Aplicar`) e nunca olhou o quadro andar no tempo, que e onde o defeito morava.
	/// E o mesmo buraco da tinta que passava verde no campo e saia cinza na tela.
	/// </summary>
	public int[] QuadrosDasColadasDeTeste
	{
		get
		{
			var r = new List<int>();
			foreach (AnimatedSprite2D c in _coladas) if (IsInstanceValid(c)) r.Add(c.Frame);
			return [.. r];
		}
	}

	/// <summary>A pose que cada colada esta tocando. Pra bancada -- ver `--diagforma`.</summary>
	public string[] PosesDasColadasDeTeste
	{
		get
		{
			var r = new List<string>();
			foreach (AnimatedSprite2D c in _coladas)
				if (IsInstanceValid(c)) r.Add(c.Visible ? c.Animation.ToString() : "");
			return [.. r];
		}
	}

	/// <summary>
	/// A CAMADA DE FORMA E OUTRO BICHO (o Oozaru), e nao uma pelagem -- ver
	/// <see cref="CorpoDaForma"/>. Derivado do tamanho do quadro na hora de vestir; nao e campo de
	/// catalogo nem parametro.
	/// </summary>
	private bool _criatura;

	/// <summary>Este corpo esta como CRIATURA agora? Pra bancada.</summary>
	public bool EhCriatura => _criatura;

	/// <summary>
	/// A FOLHA QUE O CORPO ESTA VESTINDO -- a da criatura quando ha uma, a pele base quando nao ha. E a
	/// CAMADA DO CORPO e nao a roupa nem o cabelo: quem pergunta quer a silhueta.
	///
	/// ============================ QUEM PERGUNTA, E POR QUE NAO DA PRA CRAVAR ============================
	/// A BANCADA DA NEBULOSA (`RoboDeNebulosa.AFolgaEDerivada`), que remede o alfa desta folha por conta
	/// propria pra cobrar que o efeito tenha chegado no mesmo numero.
	///
	/// A PROPRIA <see cref="NebulosaDaForma"/> DEIXOU DE PERGUNTAR ISTO. Ela pedia a folha do CORPO
	/// enquanto a mascara dela era uma ELIPSE ajustada a caixa do alfa; agora ela pede a
	/// <see cref="SilhuetaDesenhada"/>, que entrega o quadro VIVO de TODAS as camadas -- a folha do corpo
	/// sozinha nao tem topete de Super Saiyajin nem rabo, e eram justamente esses dois que sobravam pra
	/// fora da aura na foto do dono.
	///
	/// Sem isto o unico caminho seria cravar "a silhueta tem 16 px" num `.cs` -- que e
	/// verdade pras folhas de 32 e mentira pro macaco de 96, e o tipo de numero que este projeto ja
	/// pagou caro (`AuraObject.dm` versus `SpriteDeAura.AncoraPara`).
	///
	/// Devolve `null` enquanto o boneco nao tem folha (entre o nascimento do node e o `Vestir`), e quem
	/// chama tem que ter um fallback -- nao ha como este metodo inventar uma silhueta que ainda nao
	/// existe.
	/// ================================================================================================
	/// </summary>
	public SpriteFrames? FolhaDoCorpo
	{
		get
		{
			AnimatedSprite2D? s = _criatura && _corpoDaForma != null && IsInstanceValid(_corpoDaForma)
				? _corpoDaForma
				: _corpo;
			return s != null && IsInstanceValid(s) ? s.SpriteFrames : null;
		}
	}

	/// <summary>
	/// ============================ A SILHUETA VIVA: O QUADRO QUE CADA CAMADA ESTA DESENHANDO AGORA ============================
	/// Recheia <paramref name="saida"/> com o par (quadro, centro dele no espaco deste node) de cada
	/// camada que compoe o DESENHO DO LUTADOR. Quem pergunta e a <see cref="NebulosaDaForma"/>, pra
	/// compor a mascara que a nuvem do Ultra Instinto veste -- o dono pediu que o efeito "CONTORNE O
	/// CORPO", e contorno de corpo e a uniao do alfa das camadas, nao a caixa de uma delas.
	///
	/// ============================ QUEM ENTRA E A `EhSilhueta`, E ELA JA EXISTIA ============================
	/// A mesma pergunta que decide quem recebe o CONTORNO da forma decide quem entra aqui, e nao e
	/// coincidencia: as duas querem "o traco de fora do lutador". Entao corpo, corpo-da-forma, rabo,
	/// roupa e cabelo entram; os OLHOS e as COLADAS ficam de fora.
	///
	/// E as duas exclusoes valem por motivos diferentes:
	///   * o olho e um detalhe DENTRO da cabeca -- incluir nao mudaria um pixel do contorno e so custaria
	///     mais uma folha decodificada;
	///   * a colada NAO PODE entrar. Em Ultra Instinto as coladas sao justamente as duas folhas de aura
	///     do DM (`ui_aura` e `ui_dots`), que enchem o quadro de 32x32 quase inteiro. A mascara passaria
	///     a abracar a AURA em vez do lutador -- ou seja, voltaria a ser um retangulo folgado, que e
	///     exatamente o defeito que este canal existe pra consertar. Circular, e calado.
	///
	/// A `Visible` E QUEM CORTA O RESTO, e por isso ela e consultada em vez de uma segunda regra: e ela
	/// que a <see cref="Escondida"/> escreve, e e como o Oozaru some com o corpo de 32 por baixo dele. Um
	/// `if (_criatura)` aqui seria a terceira copia dessa decisao.
	///
	/// ============================ A LISTA E DE QUEM CHAMA ============================
	/// Ela e recheada e nao devolvida porque o chamador le isto POR QUADRO (a mascara acompanha a
	/// animacao): alocar uma lista de seis tuplas 60 vezes por segundo por corpo pra quase sempre
	/// concluir "a pose nao mudou" seria lixo puro.
	/// ==========================================================================================================
	/// </summary>
	public void SilhuetaDesenhada(List<(Texture2D Quadro, Vector2 Centro)> saida)
	{
		saida.Clear();

		foreach (AnimatedSprite2D s in _camadas)
		{
			if (!IsInstanceValid(s) || !s.Visible || !EhSilhueta(s)) continue;
			if (s.SpriteFrames is not { } f || s.Animation.IsEmpty) continue;
			if (s.Frame < 0 || s.Frame >= f.GetFrameCount(s.Animation)) continue;
			if (f.GetFrameTexture(s.Animation, s.Frame) is not { } t) continue;

			// `Centered` e sempre `true` nestas camadas (ver `NovaCamada`), entao o MEIO do quadro cai
			// em `Position + Offset` -- e o `Offset` e quem ancora folha de tamanho estranho pelos pes
			// (ver `Ancorar` e a linha do `_corpoDaForma`). Somar os dois e o que faz o macaco de 96
			// cair no lugar certo sem ninguem repetir a conta la.
			saida.Add((t, s.Position + s.Offset));
		}
	}

	/// <summary>
	/// O tamanho do quadro desta folha, em pixels. Pega o primeiro quadro da primeira animacao:
	/// todo `.tres` do pipeline sai de um `.dmi`, e um `.dmi` tem UM tamanho de icone -- nao ha
	/// folha com quadros de tamanhos diferentes.
	///
	/// `internal` porque a <see cref="Transformacao"/> pergunta o mesmo: o buraco que ela abre no
	/// meio do sorteio das pedras e o retangulo DESENHADO do corpo (ver `TilesEmVolta`), e um
	/// segundo "quanto mede este boneco" escrito la seria a mentira de sempre -- 32 e verdade pras
	/// folhas normais e mentira pro macaco de 96.
	/// </summary>
	internal static Vector2 TamanhoDoQuadro(SpriteFrames? f)
	{
		if (f == null) return Vector2.Zero;
		foreach (StringName a in f.GetAnimationNames())
			if (f.GetFrameCount(a) > 0 && f.GetFrameTexture(a, 0) is { } t) return t.GetSize();
		return Vector2.Zero;
	}

	/// <summary>
	/// ESTA CAMADA ESTA APAGADA PELA FORMA? A unica autoridade sobre isso -- consultada pelo
	/// <see cref="Aplicar(AnimatedSprite2D, string?, bool)"/>, que e o unico lugar que liga camada.
	///
	/// Duas regras, e as duas vem do DM:
	///
	///   * **CRIATURA ENGOLE TUDO.** `Oozaru.dm:123-125` faz `overlayList.Remove(overlayList)` e
	///     `RemoveHair()` antes de trocar o icone: o Oozaru nao veste nada. La as camadas sao
	///     GUARDADAS (`storedoverlays`) e devolvidas no DeBuff -- e o proprio original tem um
	///     comentario sobre o que acontece se alguem relogar no meio. Aqui elas so ficam
	///     invisiveis: nao ha o que guardar, e reverter e apagar um `bool`.
	///
	///   * **O RABO SAI PRA QUEM DESENHA O PROPRIO RABO.** O `saiyan4body` ja tem rabo desenhado
	///     (`supersaiyanbuff.dm:245`), e o macaco tem o dele; deixar o de 32 px por cima da dois.
	///
	/// ============================ E ISTO ERA "QUALQUER CORPO DE FORMA" ============================
	/// A regra do rabo perguntava so se HAVIA camada de forma, o que dava o mesmo resultado enquanto
	/// as unicas folhas eram a do SSJ4 e a do macaco -- as duas com rabo desenhado. O corpo
	/// MUSCULOSO quebrou isso: ele e o corpo da raca inchado, humano e SEM rabo nenhum, e um
	/// Saiyajin em Grade 2 ou Legendary sairia sem cauda. Pior: sem cauda ele nao vira Oozaru, e o
	/// Oozaru e a porta do SSJ4 -- um detalhe de desenho apagaria um degrau da escada.
	///
	/// Quem responde agora e o SIMBOLO, no Core, junto da mesma pergunta que a `Catalogo.CorDoRabo`
	/// faz do outro lado (a tinta). Uma regra, dois consumidores.
	/// ============================================================================================
	///
	/// O CORPO ESCONDIDO CONTINUA MANDANDO NA POSE: quem escolhe a animacao e o `_corpo` (ver
	/// `Aplicar(bool)`), e ele nao precisa estar visivel pra isso -- o macaco anda no compasso do
	/// corpo que esta por baixo dele. Apagar o `_corpo` de verdade obrigaria a eleger outro condutor.
	/// </summary>
	private bool Escondida(AnimatedSprite2D s)
	{
		if (ReferenceEquals(s, _corpoDaForma)) return false;
		if (_criatura) return true;
		return ReferenceEquals(s, _rabo)
			&& Jandirus.Core.Forms.Catalogo.FolhaTrazORabo(_simboloDoCorpo);
	}

	/// <summary>
	/// O MACACO CRESCE, E ISSO E DO DM. `Oozaru.dm:141-149`:
	///
	///     container.transform = matrix().Scale(1/5, 1/5)
	///     animate(container, transform = null, time = 10, alpha = 255, icon = targicon)
	///
	/// O corpo encolhe a 20% e volta ao normal em 10 tiques (**1,0 s** -- o `time` do `animate` do
	/// BYOND tambem e decissegundo, ver `Jandirus.Core.TempoDoDm`) JA com o icone
	/// trocado. Sem isso o lutador de 32 px vira um bicho de 96 px em um quadro -- e um salto de
	/// tres vezes num quadro nao le como transformacao, le como o sprite errado ter carregado.
	///
	/// MORA AQUI E NAO NA CINEMATICA de proposito: quem instala uma criatura ganha o crescimento
	/// por qualquer caminho -- a cena, a bancada, e o dia em que alguem chegar numa zona onde ja ha
	/// um Oozaru. A cena manda no que vem ANTES (aura e tremor); o corpo manda no proprio corpo.
	///
	/// ESCALA O NODE INTEIRO, como o `container.transform` do DM escala o mob inteiro. As camadas
	/// escondidas vao junto e nao custam nada; a aura e os raios sao IRMAOS deste node e nao
	/// encolhem -- o que e certo, porque a aura da cena ja apagou no mesmo instante.
	/// </summary>
	private void Crescer()
	{
		PararCrescimento();

		// FORA DA ARVORE NAO HA TWEEN (o `CreateTween` do Godot exige o node dentro dela), e sair
		// SEM TOCAR na escala e o certo: a previa da criacao e a tela de selecao montam um
		// `CharacterVisual` com `Scale = 5` pra caber no painel, e reescrever 1 aqui encolheria o
		// boneco daquelas telas por um caminho que nao tem nada a ver com elas.
		if (!IsInsideTree()) return;

		Scale = Vector2.One * EscalaInicialDaCriatura;
		_crescimento = CreateTween();
		_crescimento.TweenProperty(this, "scale", Vector2.One, SegundosCrescendo)
					.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
	}

	/// <summary>O `Scale(1/5,1/5)` do DM.</summary>
	private const float EscalaInicialDaCriatura = 0.2f;

	/// <summary>
	/// Os `time = 10` tiques do DM. Estava `10.0/12.0` (0,83 s) pela leitura errada da unidade:
	/// tique do BYOND e DECISSEGUNDO, entao sao 1,0 s. Ver <see cref="Jandirus.Core.TempoDoDm"/>.
	/// </summary>
	private const double SegundosCrescendo = 10 / Jandirus.Core.TempoDoDm.TiquesPorSegundo;

	/// <summary>
	/// Corta o crescimento e devolve o tamanho normal.
	///
	/// CHAMADO TAMBEM AO TIRAR a camada, e nao so ao repor: sem isto, sair do Oozaru no meio do
	/// crescimento (KO, rabo cortado, o prazo da forma) deixaria o tween vivo mexendo na escala de
	/// um corpo que ja voltou ao normal -- o jogador ficaria minusculo por um instante, ou preso
	/// no tamanho em que estava quando a forma caiu.
	/// </summary>
	private void PararCrescimento()
	{
		if (_crescimento == null) return;   // ninguem mexeu na escala: nao ha o que devolver

		if (IsInstanceValid(_crescimento) && _crescimento.IsValid()) _crescimento.Kill();
		_crescimento = null;
		Scale = Vector2.One;
	}

	private Tween? _crescimento;

	/// <summary>
	/// TROCA O SPRITE DO CABELO PELA VARIANTE DA FORMA. Sufixo vazio volta ao normal.
	///
	/// Guarda o penteado base pra poder VOLTAR: sem isso, reverter deixaria o jogador com o cabelo
	/// de Super Saiyajin pra sempre -- o mesmo tombo que o `ussj_saved_icon` do DM documenta ter
	/// levado com o corpo do USSJ.
	///
	/// Quando o penteado nao tem variante, `CabelosDeForma.De` devolve nulo e aqui NAO se mexe: o
	/// cabelo normal fica e so recebe a tinta. Cabelo sem variante nunca some.
	///
	/// ============================ E DEVOLVE **SE A FORMA GANHOU ARTE PROPRIA** ============================
	/// Nao "se trocou de sprite": um jogador que ja estava com o cabelo da forma recebe `true` de novo,
	/// e um que voltou ao penteado normal recebe `false` mesmo tendo mexido no node. A pergunta que o
	/// `ModoDoCabelo.TrocarOuTingir` faz e sobre o RESULTADO ("este boneco esta com a arte do Ultra
	/// Instinct?"), e nao sobre o evento -- se fosse sobre o evento, reafirmar a mesma forma duas vezes
	/// (o que o `Loop` do buff faz o tempo todo) acenderia a prata por cima da arte na segunda.
	/// ==================================================================================================
	/// </summary>
	public bool CabeloDaForma(string sufixo)
	{
		if (_cabelo == null || !IsInstanceValid(_cabelo)) return false;
		_cabeloBase ??= _cabeloAtual;

		string? variante = string.IsNullOrEmpty(sufixo)
			? null : CabelosDeForma.De(_cabeloBase, sufixo, _feminina);
		string? alvo = variante ?? _cabeloBase;
		if (alvo == null || alvo == _cabeloAtual) return variante != null;

		Trocar(_cabelo, alvo);
		_cabeloAtual = alvo;

		// MESMO MOTIVO DO CORPO DA FORMA: `Trocar` troca a FOLHA sem tocar em `Animation`, `Frame`
		// nem no meta `sync`. Como o beat da cinematica pisca o cabelo varias vezes, sem esta linha
		// o cabelo saia andando sozinho enquanto o resto do corpo estava parado.
		Aplicar(force: true);
		return variante != null;
	}

	private string? _cabeloBase, _cabeloAtual;

	/// <summary>
	/// O corpo e feminino? So o cabelo de SSJ4 usa isto -- ele tem arquivo proprio pra cada
	/// (`Hair_SSj4` / `Hair_SSJ4Female`), e nao e o mesmo desenho espelhado.
	/// </summary>
	private bool _feminina;

	/// <summary>
	/// ESTE CORPO DOMINOU A FORMA EM QUE ESTA (maestria 100%). So o cabelo usa isto -- e so o
	/// Super Saiyajin comum, que aos 100% troca a folha `SSj` pela `SSjFP` (o Grade 4).
	///
	/// ============================ POR QUE CAMPO, E NAO PARAMETRO ============================
	/// Irmao do <see cref="_feminina"/> logo acima, e pelo mesmo motivo: e um fato do CORPO, nao da
	/// chamada. Quem sabe do dominio e o <see cref="World"/> (o bit chega no `S2C.Forma`), mas quem
	/// veste o cabelo tambem e a <see cref="Transformacao"/> -- o beat que PISCA o cabelo durante a
	/// cinematica chama o `VestirCabeloDaForma` varias vezes, de dentro do tocador, sem nenhum
	/// caminho ate o pacote. Um parametro obrigaria a cena inteira a carregar o fato so pra
	/// devolve-lo intacto no fim.
	/// ===================================================================================
	/// </summary>
	private bool _dominouAForma;

	/// <summary>
	/// Diz a este corpo que ele dominou (ou nao) a forma que vai vestir. Chamar ANTES do
	/// <see cref="VestirCabeloDaForma"/> -- ver <see cref="_dominouAForma"/>.
	/// </summary>
	public void MarcarFormaDominada(bool sim) => _dominouAForma = sim;

	/// <summary>
	/// O SERVIDOR ESTA DIRIGINDO ESTE CORPO? (a furia lendaria, o Oozaru sem controle, ou o que vier
	/// depois). So o OLHO usa isto -- ver `Catalogo.CorDoOlho(d, semRedeas)`.
	///
	/// CAMPO E NAO PARAMETRO, pelo mesmo motivo do <see cref="_dominouAForma"/> logo acima: e um fato
	/// do CORPO e nao da chamada, e quem veste o cabelo tambem e a cinematica, que nao tem caminho ate
	/// o pacote.
	/// </summary>
	private bool _semRedeas;

	/// <summary>
	/// A FORMA QUE ESTE CORPO ESTA VESTINDO. Guardada por UM motivo: a posse muda no MEIO da forma, e
	/// o olho tem que responder na hora sem esperar a proxima transformacao.
	///
	/// ============================ POR QUE NAO SE REVESTE TUDO DE NOVO ============================
	/// O caminho obvio pra "a posse mudou, redesenha" seria o `World` chamar `VestirAFormaSemCena` de
	/// novo. Ele erraria feio num caso: quem esta em OOZARU tem a escada na BASE (o `Apeshit` reverte
	/// a forma antes de chamar a fera), entao revestir a "forma atual" arrancaria o corpo do macaco no
	/// instante em que ele perdesse o controle -- que e exatamente quando isso acontece.
	///
	/// A posse muda UMA coisa no desenho, e e esta classe que sabe qual. Guardar a forma e repintar so
	/// o olho e a diferenca entre um redesenho e um efeito colateral.
	/// ========================================================================================
	/// </summary>
	private Jandirus.Core.Forms.FormaDef? _formaVestida;

	/// <summary>
	/// Diz a este corpo quem o esta dirigindo, e REPINTA o olho na hora.
	///
	/// Chamada de dois lugares, e os dois sao necessarios: o funil que veste a forma (pra o corpo que
	/// NASCE ja possuido -- quem entra numa zona onde alguem esta em furia) e o snapshot (pra a
	/// virada, que acontece com o boneco parado na tela). Ver `World.AoMudarPosse`.
	/// </summary>
	public void MarcarSemRedeas(bool sim)
	{
		_semRedeas = sim;
		TingirOlhos(Jandirus.Core.Forms.Catalogo.CorDoOlho(_formaVestida, _semRedeas) is { } co
			? new Color(co) : null);
	}

	/// <summary>Qual sprite de cabelo esta vestido agora. Pra bancada.</summary>
	public string CabeloDeTeste => _cabeloAtual ?? "";

	/// <summary>
	/// ESTE CORPO TEM CABELO? Pra bancada.
	///
	/// Um teste de "o cabelo doura" num personagem CARECA passa sem provar nada -- nao ha o que
	/// dourar. Por isso a bancada nasce com o cabelo do Goku e confere isto antes.
	/// </summary>
	public bool TemCabeloDeTeste => _cabelo != null && IsInstanceValid(_cabelo) && _cabelo.SpriteFrames != null;

	/// <summary>O rabo esta visivel? Pra bancada -- o SSJ4 o esconde.</summary>
	public bool RaboVisivelDeTeste => _rabo == null || !IsInstanceValid(_rabo) || _rabo.Visible;

	/// <summary>
	/// A TINTA ARMADA NO CABELO AGORA, e o MODO dela (0 = soma, 1 = matiz). Nula quando este corpo
	/// nao tem cabelo.
	///
	/// Lida do material pelo mesmo motivo do <see cref="ContornoNoMaterialDeTeste"/>: um campo diria o
	/// que foi PEDIDO, e so o uniform diz o que esta desenhado. E o modo vem junto porque a diferenca
	/// entre o Beast certo e o Beast dourado-lavado E o modo -- medir so a cor aprovaria os dois.
	/// </summary>
	public (Vector3 Tinta, int Modo)? TintaDoCabeloDeTeste =>
		_cabelo is { } cbt && IsInstanceValid(cbt) && cbt.Material is ShaderMaterial mct
			? (mct.GetShaderParameter("tinta").AsVector3(), mct.GetShaderParameter("tinta_modo").AsInt32())
			: null;

	/// <summary>
	/// A TINTA ARMADA NO RABO AGORA -- nula quando este corpo nao tem rabo.
	///
	/// Mesmo motivo da irma de cima, e aqui ele tem nome: o comentario do <see cref="PintarRabo"/>
	/// descreve um defeito em que a tinta fica ARMADA num node escondido "pra quando ele voltar a
	/// aparecer, no reverter". Um campo guardado diria o que foi pedido; so o uniform diz o que vai
	/// reaparecer.
	/// </summary>
	public Vector3? TintaDoRaboDeTeste =>
		_rabo is { } rb && IsInstanceValid(rb) && rb.Material is ShaderMaterial mr
			? mr.GetShaderParameter("tinta").AsVector3()
			: null;

	/// <summary>
	/// ============================ TODA CAMADA, COM O SPRITE VIVO. SO PRA BANCADA ============================
	/// As duas propriedades acima respondem por UMA camada cada, e as duas devolvem um VALOR -- o que o C#
	/// escreveu no uniform. Isso basta pra "a cor pedida chegou", e nao basta pra duas perguntas que o
	/// relato do dono (*"parece q tem algo pintando o cabelo, e o rabo, de pretos"*) obrigou a fazer:
	///
	///   1. **o PAR.** `tinta` sozinha nao descreve o desenho: `(0,0,0)` em SOMA e a arte intacta e
	///      `(0,0,0)` em MATIZ e preto chapado. Quem pergunta "ha alguma camada em matiz com preto?"
	///      precisa varrer TODAS elas -- inclusive as que nao tem propriedade propria (roupa, corpo de
	///      forma, colada), que sao justamente as que ninguem olha.
	///   2. **o PIXEL.** Ler o uniform nao ve o shader. A causa daquele relato foi uma linha do
	///      `.gdshader` elevando a folha ao quadrado, com os uniformes todos certos -- e nenhuma leitura
	///      de uniform, de qualquer camada, teria mudado de valor. Pra fotografar o que SAI e preciso o
	///      sprite vivo: a folha, o quadro e o material, os tres que o jogo esta desenhando agora.
	///
	/// Por isso aqui volta o NODE e nao um valor. A bancada (`RoboDeForma.ACorQueSaiNaTela`) monta um
	/// `SubViewport` com uma copia que compartilha ESTE material e ESTA folha, e mede o resultado.
	///
	/// So leitura: escrever no material devolvido mexeria no personagem de verdade.
	/// ==================================================================================================
	/// </summary>
	public (string Nome, AnimatedSprite2D Sprite)[] CamadasDeTeste()
	{
		var fora = new List<(string, AnimatedSprite2D)>();
		void Uma(string nome, AnimatedSprite2D? s)
		{
			if (s != null && IsInstanceValid(s)) fora.Add((nome, s));
		}

		Uma("corpo", _corpo);
		Uma("corpodaforma", _corpoDaForma);
		Uma("rabo", _rabo);
		for (int i = 0; i < _roupa.Count; i++) Uma($"roupa{i}", _roupa[i]);
		for (int i = 0; i < _coladas.Count; i++) Uma($"colada{i}", _coladas[i]);
		Uma("cabelo", _cabelo);
		Uma("olho", _olhos);
		return [.. fora];
	}

	/// <summary>
	/// O cabelo esta DESENHADO na tela? Pra bancada.
	///
	/// Diferente do <see cref="TemCabeloDeTeste"/>, e a diferenca e o Oozaru: a camada continua
	/// existindo e carregada, so que invisivel. Perguntar "tem cabelo?" responderia SIM num macaco
	/// gigante, e o teste passaria sem provar que o `RemoveHair()` do DM foi portado.
	/// </summary>
	public bool TemCabeloVisivelDeTeste =>
		_cabelo != null && IsInstanceValid(_cabelo) && _cabelo.SpriteFrames != null && _cabelo.Visible;

	/// <summary>
	/// Quanto a camada de forma foi deslocada pra a base do quadro dela cair na linha dos pes.
	/// Pra bancada -- ver a conta no <see cref="CorpoDaForma"/>. `NaN` = nao ha camada.
	/// </summary>
	public float AncoraDoCorpoDaFormaDeTeste =>
		_corpoDaForma != null && IsInstanceValid(_corpoDaForma) ? _corpoDaForma.Offset.Y : float.NaN;

	/// <summary>O corpo de forma esta vestido? Pra bancada.</summary>
	public bool CorpoDaFormaDeTeste => _corpoDaForma != null && IsInstanceValid(_corpoDaForma);

	/// <summary>
	/// A camada de forma esta SENDO DESENHADA? Pra bancada.
	///
	/// Diferente do <see cref="CorpoDaFormaDeTeste"/> pelo mesmo motivo que o
	/// <see cref="TemCabeloVisivelDeTeste"/> difere do <see cref="TemCabeloDeTeste"/>: existir e
	/// aparecer sao duas coisas. E no macaco a distincao vale um jogador INVISIVEL -- quando a
	/// criatura esta vestida o <see cref="Escondida"/> apaga o corpo de baixo, entao uma camada de
	/// forma que nao ligue (pose sem substituta, folha vazia) nao deixa "o lutador normal" na tela:
	/// nao deixa nada.
	/// </summary>
	public bool CorpoDaFormaVisivelDeTeste =>
		_corpoDaForma != null && IsInstanceValid(_corpoDaForma) && _corpoDaForma.Visible;

	/// <summary>
	/// QUAL folha a camada de forma esta vestindo. Vazio = nenhuma. Pra bancada.
	///
	/// Existe por causa do corpo MUSCULOSO, que e o unico cujo arquivo nao sai so do catalogo: a
	/// bancada precisa provar que a PELE do boneco chegou na conta, e "tem camada" nao prova isso --
	/// as tres folhas criam camada igual.
	/// </summary>
	public string FolhaDoCorpoDaFormaDeTeste =>
		_corpoDaForma != null && IsInstanceValid(_corpoDaForma)
			? _corpoDaForma.SpriteFrames?.ResourcePath ?? "" : "";

	/// <summary>Que animacao a camada de forma esta tocando. Vazio = nenhuma. Pra bancada.</summary>
	public string PoseDoCorpoDaFormaDeTeste =>
		_corpoDaForma != null && IsInstanceValid(_corpoDaForma) ? _corpoDaForma.Animation.ToString() : "";

	/// <summary>
	/// O corpo BASE esta visivel? Pra bancada -- sob a criatura ele tem que estar apagado
	/// (`Oozaru.dm:123-125`: o macaco nao veste nada, ele SUBSTITUI o mob).
	/// </summary>
	public bool CorpoBaseVisivelDeTeste => _corpo != null && IsInstanceValid(_corpo) && _corpo.Visible;

	/// <summary>
	/// A forca PEDIDA do contorno -- o TOPO do pulso, e nao o que esta desenhado neste quadro. Pra
	/// bancada. Quem quiser o valor do quadro le a <see cref="ContornoNoMaterialDeTeste"/>.
	/// </summary>
	public float AuraDaFormaDeTeste => _auraDaForma;
	private float _auraDaForma;

	/// <summary>
	/// OS DOIS CONTORNOS escritos no material dos OLHOS -- o da forma (`aura`) e o do impacto
	/// (`contorno`). Pra bancada: os dois tem que ficar SEMPRE em zero, porque os dois saem da mesma
	/// conta de borda e ela nao vale num detalhe (ver <see cref="EhSilhueta"/>).
	///
	/// OS DOIS JUNTOS de proposito: consertar um so e o erro provavel aqui, e um hook que medisse
	/// apenas o da forma nao teria como acusa-lo. Nulo quando nao ha camada de olhos, e quem chama
	/// confere isso antes -- "nao ha olhos" nao prova nada sobre a regra.
	///
	/// O `Flash` VEM JUNTO porque e o CONTROLE: ele e do corpo inteiro e TEM que chegar nos olhos.
	/// Sem ele, "os olhos estao em zero" passaria verde com a camada dos olhos excluida de tudo --
	/// e ai o rosto se descolaria do boneco no quadro do soco. Uma medicao que so sabe dizer o que
	/// NAO chega nao distingue "filtrado certo" de "esquecido".
	/// </summary>
	public (float Forma, float Impacto, float Flash)? ContornosNosOlhosDeTeste =>
		_olhos != null && IsInstanceValid(_olhos) && _olhos.Material is ShaderMaterial m
			? ((float)m.GetShaderParameter("aura"),
			   (float)m.GetShaderParameter("contorno"),
			   (float)m.GetShaderParameter("flash")) : null;

	/// <summary>Ha camada de olhos vestida? Pra bancada -- ver <see cref="ContornosNosOlhosDeTeste"/>.</summary>
	public bool TemOlhosDeTeste => _olhos != null && IsInstanceValid(_olhos) && _olhos.SpriteFrames != null;

	/// <summary>
	/// O CONTORNO COMO O SHADER O VE -- lido do MATERIAL de cada camada, e nao do campo guardado.
	///
	/// ============================ POR QUE NAO BASTA O `AuraDaFormaDeTeste` ============================
	/// Aquele campo diz o que o ULTIMO `AuraDaForma` pediu; este diz o que cada camada REALMENTE tem
	/// escrito. Os dois divergem exatamente no defeito que a Fase 2 corrigiu: `CorpoDaForma` CRIA uma
	/// camada nova (a pelagem do SSJ4) com material proprio e uniform zerado, entao escrever o contorno
	/// ANTES de vestir a pelagem deixava o campo em 1,0 e a pelagem em 0 -- a bancada media 1,0 e o
	/// jogador via um SSJ4 sem contorno.
	///
	/// Por isso a `Forca` devolvida e a MENOR de todas as camadas: uma unica camada que ficou pra tras
	/// derruba o numero. `Camadas` vem junto porque "menor de zero camadas" seria 0 e passaria por
	/// "apagado" -- quem le precisa saber se havia o que medir.
	///
	/// SO AS CAMADAS DE SILHUETA, pela mesma <see cref="EhSilhueta"/> que escreve. Os olhos nao
	/// recebem contorno de proposito, e como a `Forca` e a MENOR de todas, incluir a camada deles
	/// aqui devolveria 0 pra sempre -- a bancada acusaria "o contorno nao chegou" justamente quando o
	/// conserto esta funcionando. Medidor e escritor tem que fazer a mesma pergunta.
	/// ==============================================================================================
	/// </summary>
	public (Color Cor, float Forca, int Camadas) ContornoNoMaterialDeTeste()
	{
		Color cor = Colors.White;
		float menor = float.MaxValue;
		int n = 0;
		foreach (AnimatedSprite2D s in _camadas)
		{
			if (!IsInstanceValid(s) || !EhSilhueta(s) || s.Material is not ShaderMaterial m) continue;
			n++;
			if (m.GetShaderParameter("aura_cor").VariantType == Variant.Type.Color)
				cor = (Color)m.GetShaderParameter("aura_cor");
			menor = Math.Min(menor, (float)m.GetShaderParameter("aura"));
		}
		return (cor, n == 0 ? 0f : menor, n);
	}

	/// <summary>
	/// Escreve o estado do impacto nas camadas. 0 = corpo em repouso.
	///
	/// ============================ TRES DELES VAO EM TODAS, UM NAO ============================
	/// `flash`, `achatar` e `empurrao` sao do CORPO INTEIRO: o boneco clareia, achata e cede na
	/// direcao do golpe, e uma camada que ficasse de fora se descolaria do resto -- os olhos
	/// flutuariam fora do rosto no quadro do soco.
	///
	/// O `contorno` nao: ele e a MESMA conta de borda do contorno da forma
	/// (`borda = c.a * (1 - viz)` no shader), entao tem o MESMO defeito nos detalhes -- pinta o olho
	/// inteiro em vez de contorna-lo. Ver <see cref="EhSilhueta"/>.
	///
	/// AQUI ELE ERA QUASE INVISIVEL, e e por isso que o dono so viu o da forma: o impacto dura 0,15 s
	/// e o `flash` branco cobre o boneco no mesmo instante. Mas "invisivel" nao e "certo", e deixar a
	/// escrita errada num canal e a certa no outro e como o proximo ajuste no flash viraria um
	/// defeito visivel sem ninguem ligar uma coisa a outra.
	/// ====================================================================================
	/// </summary>
	private void AplicarImpacto(float f)
	{
		foreach (AnimatedSprite2D s in _camadas)
		{
			if (s.Material is not ShaderMaterial m) continue;
			m.SetShaderParameter("flash", f * _lavagem);
			// ============================ O ACHATAMENTO E DO GOLPE, NAO DO CANAL ============================
			// Isto era `f * 0.18f` cravado, e ficou errado no dia em que um SEGUNDO gesto passou a usar o
			// mesmo `flash`: o <see cref="Banhar"/> lava o corpo na cor da forma por 0,6 s, e com o literal
			// aqui ele espremia o boneco esse tempo todo -- o Legendary "levava um soco" ao virar. Quem
			// achata e quem CHAMA, e por isso o fator virou campo (`_achatamento`), escrito por
			// `Impacto` (0,18) e por `Banhar` (0). Mesma razao do `_empurrao`, que ja era campo.
			// ==========================================================================================
			m.SetShaderParameter("achatar", f * _achatamento);
			m.SetShaderParameter("empurrao", _empurrao * f);
			if (EhSilhueta(s)) m.SetShaderParameter("contorno", f);
		}
	}

	/// <summary>
	/// Quanto o corpo se espreme e quanto ele se lava no pico do <see cref="AplicarImpacto"/>. Sao dois
	/// gestos no mesmo canal e eles pedem numeros diferentes -- ver <see cref="Impacto"/> e
	/// <see cref="Banhar"/>. Nascem com os valores do IMPACTO, que e quem usava o canal sozinho.
	/// </summary>
	private float _achatamento = 0.18f, _lavagem = 0.85f;

	/// <summary>
	/// BANHA O CORPO INTEIRO NUMA COR -- o `animate(src, time=6, color=rgb(...))` do DM.
	///
	/// ============================ E O CANAL DO IMPACTO, DE PROPOSITO ============================
	/// O `Personagem.gdshader` ja sabe MISTURAR todas as camadas em direcao a uma cor (o `flash` /
	/// `flash_cor` que o <see cref="Impacto"/> usa), e sabe faze-lo em corpo, roupa, cabelo e rabo ao
	/// mesmo tempo -- que e o requisito inteiro: no BYOND `src.color` e do ATOM, entao ele pinta o
	/// boneco e tudo o que estiver colado nele de uma vez.
	///
	/// Um segundo uniform faria o shader crescer pra dizer o que este ja diz, e o `_Process` teria dois
	/// relogios pra decair. O que o banho NAO quer do impacto e o gesto: nada de achatar, nada de
	/// empurrao. Os dois viraram campo (ver <see cref="_achatamento"/>) exatamente por isso.
	///
	/// ============================ E O ULTIMO A ESCREVER GANHA, QUE E O CERTO ============================
	/// Levar um soco durante o banho corta o banho, e vice-versa. Nao ha o que somar aqui: sao duas
	/// afirmacoes sobre a MESMA cor do corpo, e a mais recente e a verdadeira. Empilha-las daria um tom
	/// que nao e nenhuma das duas -- e o caso e raro de todo jeito, porque quem esta virando esta preso
	/// (`Cinematica.SegundosPreso`).
	///
	/// ============================ A LAVAGEM E MAIS FORTE QUE A DO SOCO ============================
	/// 0,85 no impacto e um lampejo de 0,15 s; o banho dura quatro vezes isso e tem que ler como o corpo
	/// TOMADO pela cor, nao como um retoque. Ele nao vai a 1,0 porque a silhueta tem que continuar
	/// legivel -- em 1,0 o boneco vira uma mancha chapada e o penteado que a cena acabou de trocar some.
	/// ======================================================================================
	/// </summary>
	/// <param name="cor">A cor da forma -- ver `Aura.CorDaChamaDe`, que e quem a deriva.</param>
	/// <param name="segundos">Quanto o banho leva pra escoar. O DM usa `time=6` + `spawn(12)`.</param>
	public void Banhar(Color cor, double segundos)
	{
		_flash = _flashTotal = segundos;
		_empurrao = Vector2.Zero;
		_achatamento = 0f;
		_lavagem = 0.92f;

		foreach (AnimatedSprite2D s in _camadas)
		{
			if (s.Material is not ShaderMaterial m) continue;
			m.SetShaderParameter("flash_cor", cor);
			m.SetShaderParameter("contorno_cor", cor);
		}
		AplicarImpacto(1);
	}

	/// <summary>Quanto falta do banho/impacto, em segundos. Pra bancada -- ver `--diagforma`.</summary>
	public double BanhoDeTeste => _flash;

	/// <summary>
	/// O QUE O CORPO ESTA LAVANDO AGORA, lido do MATERIAL: a mistura (`flash`), a cor pra onde ela vai
	/// (`flash_cor`) e o achatamento (`achatar`). Nulo quando este corpo nao tem camada com shader.
	///
	/// Lido do uniform e nao dos campos pelo mesmo motivo do <see cref="TintaDoCabeloDeTeste"/>: um
	/// campo diz o que foi PEDIDO. E aqui a diferenca importa mais que o normal, porque o canal e
	/// COMPARTILHADO com o soco -- o defeito a vigiar e o banho da transformacao saindo com o
	/// achatamento do golpe (o boneco espremido por um segundo ao virar), e so o `achatar` desenhado
	/// separa um do outro.
	/// </summary>
	public (float Mistura, Color Cor, float Achatar)? LavagemDeTeste =>
		_corpo is { } cb && IsInstanceValid(cb) && cb.Material is ShaderMaterial mb
			? (mb.GetShaderParameter("flash").AsSingle(),
			   mb.GetShaderParameter("flash_cor").AsColor(),
			   mb.GetShaderParameter("achatar").AsSingle())
			: null;

	public override void _Process(double delta)
	{
		if (_flash > 0)
		{
			_flash -= delta;
			AplicarImpacto(_flashTotal > 0 ? (float)Math.Max(_flash / _flashTotal, 0) : 0);
		}

		AnimarContorno(delta);

		if (_corpo?.SpriteFrames == null) return;

		// TUDO anima, inclusive parado: a pose parada e um estado proprio com ciclo proprio
		// (a "respiracao"). Quem NAO anima e a camada que caiu numa pose emprestada.
		SpriteFrames? corpoF = _corpo.SpriteFrames;
		double ciclo = corpoF == null ? 0 : Ciclo(corpoF, _corpo.Animation);

		// O PASSO ACELERA NA CORRIDA. Sem isto o personagem desliza: as pernas andam na cadencia
		// de caminhada enquanto o corpo atravessa o dobro do chao, e o cerebro le como patinacao.
		// So vale enquanto ANDANDO -- correndo parado (empurrando parede) nao existe, e acelerar a
		// pose de respiracao daria um personagem ofegante de pe.
		double ritmo = _ritmo * (_correndo && _moving && _state == "default" ? RitmoDaCorrida : 1);
		_relogio = ciclo > 0 ? (_relogio + delta * ritmo) % ciclo : 0;

		// ============================ UM SEGUNDO RELOGIO, E ELE NAO PODE SER O DE CIMA ============================
		// O `_relogio` e a FASE DENTRO DO CICLO DO CORPO: o `Aplicar` o zera a cada troca de pose (pra
		// o soco recomecar do primeiro quadro) e o `%` o prende ao ciclo do corpo. Quem tem ciclo
		// proprio nao pode viver nele -- cada passo do jogador reiniciaria a fagulha do Legendary.
		//
		// Este anda sempre, no ritmo real, e nao e zerado por ninguem. E o mesmo `_relogio` privado que
		// o `SpriteDeAura` mantem pela mesma razao: efeito nao acompanha pose.
		_relogioSolto += delta;

		double fase = ciclo > 0 ? _relogio / ciclo : 0;   // 0..1 dentro do ciclo do corpo

		foreach (AnimatedSprite2D s in _camadas)
		{
			SpriteFrames? f = s.SpriteFrames;
			if (f == null || !s.Visible || !f.HasAnimation(s.Animation)) continue;
			// ============================ EFEITO NAO EMPRESTA O RELOGIO DO CORPO ============================
			// Aqui embaixo estava o defeito que o dono viu: "os overlays das formas god e lssj estao com
			// baixo fps quando to PARADO, cada frame demora pra trocar, parece um slide show, mas quando
			// ANDO elas voltam a andar em um frame rate bom".
			//
			// A causa nao e taxa de quadros nenhuma: e a linha do `alvo`, la embaixo, que REESCALA o ciclo
			// da folha pra caber no ciclo do CORPO. Medido nos `.tres`, sul, `speed = 10`:
			//
			//   corpo `NewPaleMale`   `default_south` = 4 quadros `1,1,1,30`  -> ciclo 3,300 s
			//   corpo `NewPaleMale`   `walk_south`    = 4 quadros `2,2,2,2`   -> ciclo 0,800 s
			//   `god` / `god blue` / `god - grey`  `default_south` = `1,1,1,1` -> ciclo 0,400 s
			//   `god` / `god blue` / `god - grey`  `walk_south`    = `2,2,2,2` -> ciclo 0,800 s
			//
			// ANDANDO os dois ciclos sao 0,800 s: razao 1:1, e a aura roda na velocidade desenhada -- o
			// "frame rate bom" do relato. PARADO a razao vira 3,300 / 0,400 = 8,25, e cada quadro da aura
			// e esticado de 0,100 s pra 0,825 s: 1,21 quadros por segundo. E o slideshow, e ele nao vem da
			// piscada ser irregular -- vem do `30` do ultimo quadro do corpo (de pe, olhos abertos) ser
			// 91% do ciclo dele. VOAR PARADO tem o mesmo `1,1,1,30` (`flight_south`, 3,300 s) e sofre
			// igual; ATACAR e o defeito pelo avesso (`attack_south` tem 1 quadro, ciclo 0,100 s: a aura
			// correria 4x rapido demais).
			//
			// A roupa se disfarcava porque o `ClothesSaiyanSuit` tem 8 quadros de idle (ciclo 1,600 s) de
			// um traje que quase nao muda -- os 0,4125 s por quadro nao aparecem. A aura nao tem onde se
			// esconder.
			//
			// A `LSSJpowerz` chegava no mesmo lugar pelo caminho oposto: ela tem UMA animacao (`default`,
			// 6 quadros, 0,600 s) e nao um vocabulario de poses. PARADO o `Escolher` devolve `"default"`,
			// o `naPose` do `Aplicar` da true, o `sync` sai VERDADEIRO e ela caia no ramo sincronizado --
			// 3,300 / 0,600 = 5,5x. O relogio proprio so a alcancava ANDANDO, que era exatamente quando
			// ela menos precisava. Era literalmente o inverso do que se queria.
			//
			// A regra agora e a natureza da camada (<see cref="EhEfeito"/>) e nao mais o `sync`: efeito
			// herda POSE e DIRECAO do corpo e avanca o quadro pelos delays da folha DELE.
			//
			// NAO E `Play()`, DE PROPOSITO. Este metodo continua sendo o unico dono do quadro de toda a
			// pilha, e a colada continua passando pelo `Aplicar` (que escreve o `sync`, chama `Stop()` e
			// consulta a `Escondida`). Foi assim que o corpo do SSJ4 saiu ANDANDO no meio da cinematica da
			// outra vez: uma camada com `Play()` proprio pulava o funil inteiro.
			//
			// E NAO PRENDE NO QUADRO 0: o `_relogioSolto` nao e zerado por ninguem (nem pelo `Aplicar`,
			// que zera o `_relogio`). Mesmo que uma troca de pose por quadro escrevesse `Frame = 0` toda
			// vez, a linha de baixo recalcularia o quadro pelo tempo real na mesma passada. A pose em si
			// troca na hora porque o `Aplicar` reescreve o `Animation`, e o ciclo passa a ser o da pose
			// nova (parar de andar, atacar, voar, cair).
			// ==============================================================================================
			if (EhEfeito(s))
			{
				int n = f.GetFrameCount(s.Animation);
				double proprio = Ciclo(f, s.Animation);
				int q = n > 1 && proprio > 0
					? Mathf.Clamp(QuadroEm(f, s.Animation, _relogioSolto % proprio), 0, n - 1)
					: 0;
				if (s.Frame != q) s.Frame = q;
				continue;
			}

			if (!s.GetMeta("sync", true).AsBool())
			{
				// PARTE DO CORPO em pose EMPRESTADA congela no primeiro quadro: a peca nao tem aquele
				// movimento (13 roupas nao tem ataque, 68 nao tem treino -- ver `Escolher`), e percorrer
				// um ciclo que nao e o dela sai fora de compasso com o corpo que ela veste.
				if (s.Frame != 0) s.Frame = 0;
				continue;
			}

			// A MESMA FASE do corpo, lida no relogio DESTA camada: o `ClothesSaiyanSuit` tem
			// 8 quadros de caminhada onde o corpo tem 4, e cada folha pode ter duracoes
			// diferentes. Assim ninguem corre em velocidade errada nem sai de compasso.
			int alvo = QuadroEm(f, s.Animation, fase * Ciclo(f, s.Animation));
			if (s.Frame != alvo) s.Frame = alvo;
		}
	}

	private void Aplicar(bool force)
	{
		// O CORPO ESCOLHE A POSE PRIMEIRO e as camadas seguem o nome DELE quando podem.
		// Sem isso elas divergem por um detalhe dos .dmi: o corpo tem `train` (uma direcao
		// so) enquanto o cabelo e o `GokuDBSSuit` tem `train_east/north/south/west`. Cada um
		// escolhendo por conta propria acabava com o corpo em `train` e o cabelo em
		// `train_east` -- animacoes diferentes, contagens diferentes, tudo fora de compasso.
		string? doCorpo = _corpo == null ? null : Escolher(_corpo, null);
		foreach (AnimatedSprite2D s in _camadas) Aplicar(s, doCorpo, force);
		_relogio = 0;
		AvisarSeDeitado(doCorpo);
	}

	/// <summary>
	/// AS POSES EM QUE O CORPO ESTA DEITADO DENTRO DO QUADRO.
	///
	/// Medido: das 48 animacoes do corpo, so o `ko` e um desenho deitado (pes em x=0, cabeca em
	/// x=0.78). `flight`, `train` e `meditate` sao desenhos EM PE -- neles o node gira e o UV gira
	/// junto, e o shader nao precisa saber de nada.
	/// </summary>
	private static bool PoseDeitada(string? pose) =>
		pose != null && pose.StartsWith("ko", StringComparison.Ordinal);

	private bool _deitadoEnviado;

	/// <summary>
	/// Diz ao shader que o corpo virou de lado DENTRO do desenho.
	///
	/// Sem isto o dono via o defeito exato: "quando voce gira o personagem por knock back ou ko, o
	/// shader nao gira, com isso os ferimentos ficam no lugar errado (e a roupa tb fica errada)".
	/// Girar o NODE resolve a pose na tela e nao mexe no que esta desenhado no quadro -- e o quadro
	/// de nocaute ja vem deitado.
	/// </summary>
	private void AvisarSeDeitado(string? pose)
	{
		bool deitado = PoseDeitada(pose);
		if (deitado == _deitadoEnviado) return;
		_deitadoEnviado = deitado;

		foreach (AnimatedSprite2D s in _camadas)
			if (IsInstanceValid(s) && s.Material is ShaderMaterial m)
				m.SetShaderParameter("ferida_deitado", deitado ? 1f : 0f);
	}

	/// <summary>
	/// A pose desta camada. Ordem: o nome que o CORPO usou, depois a variante com direcao,
	/// depois a sem direcao, e so entao a substituta -- que tem que manter a DIRECAO.
	///
	/// A substituta importa porque 13 roupas nao tem pose de ataque e 68 nao tem a de treino.
	/// A versao antiga caia em `default_south` FIXO: a camisa aparecia de frente, em pose de
	/// caminhada, por cima de um corpo socando pro lado. Errar a direcao e muito mais visivel
	/// que emprestar a pose.
	/// </summary>
	/// <summary>O nome da animacao termina em `_north/_south/_east/_west`?</summary>
	private static bool TemSufixoDeDirecao(string nome) =>
		nome.EndsWith("_north", StringComparison.Ordinal) || nome.EndsWith("_south", StringComparison.Ordinal)
		|| nome.EndsWith("_east", StringComparison.Ordinal) || nome.EndsWith("_west", StringComparison.Ordinal);

	private string? Escolher(AnimatedSprite2D sprite, string? doCorpo)
	{
		SpriteFrames? f = sprite.SpriteFrames;
		if (f == null) return null;

		string fam = Familia();

		// ============================ A DIRECAO SAI DO CORPO, NAO DO FACING ============================
		// Quando a pose do CORPO nao tem sufixo de direcao -- e o caso do `train`, que no .dmi e uma
		// unica animacao virada pro SUL --, as camadas nao podem usar o facing do jogador: o corpo
		// vai estar de frente e o cabelo iria pro lado pra onde o personagem estava olhando quando
		// comecou a treinar.
		//
		// Foi o que o dono viu: "se eu treinar quando estava virado pra esquerda ou pra cima o
		// cabelo buga, ele n gira pro lado certo -- a animacao de train e sempre pra mesma direcao
		// entao n importa a posicao inicial do personagem".
		//
		// Regra: pose sem direcao no corpo => as camadas usam SUL, que e a direcao em que o BYOND
		// desenha um estado unico.
		bool corpoSemDirecao = doCorpo != null && !TemSufixoDeDirecao(doCorpo);
		string dir = corpoSemDirecao ? "south" : MoveRules.FacingSuffix(_facing);

		if (doCorpo != null && f.HasAnimation(doCorpo)) return doCorpo;
		if (f.HasAnimation($"{fam}_{dir}")) return $"{fam}_{dir}";
		if (f.HasAnimation(fam)) return fam;

		// APELIDO `_mov`. Estado com `movement = 1` no .dmi virou `<nome>_mov` na conversao, e
		// nem toda peca marcou o mesmo estado como de movimento -- o corpo tem
		// `flight_mov_south` e o rabo tambem, mas quem escrever "flight" nao acha nenhum dos
		// dois. Sem este apelido, voar deixaria o rabo em pose de caminhada.
		if (f.HasAnimation($"{fam}_mov_{dir}")) return $"{fam}_mov_{dir}";
		if (f.HasAnimation($"{fam}_mov")) return $"{fam}_mov";

		// peca sem POSE PARADA propria usa o primeiro quadro da caminhada, e vice-versa --
		// varias roupas so trazem uma das duas
		string outra = fam == "walk" ? "default" : fam == "default" ? "walk" : "";
		if (outra.Length > 0)
		{
			if (f.HasAnimation($"{outra}_{dir}")) return $"{outra}_{dir}";
			if (f.HasAnimation(outra)) return outra;
		}

		if (f.HasAnimation($"default_{dir}")) return $"default_{dir}";
		if (f.HasAnimation($"walk_{dir}")) return $"walk_{dir}";
		if (f.HasAnimation("default_south")) return "default_south";
		if (f.HasAnimation("walk_south")) return "walk_south";

		// ============================ E A ULTIMA SAIDA E A FOLHA SEM DIRECAO NENHUMA ============================
		// Toda a escada acima procura nome COM sufixo (ou o `fam` cru la em cima, que so serve quando o
		// proprio `fam` e a animacao). Uma folha de UMA animacao chamada `default` -- que e o caso da
		// `LSSJpowerz`, a fagulha do Legendary -- passa por todos os degraus sem casar em nenhum e cai no
		// `return null`, e `null` no `Aplicar` quer dizer ESCONDER a camada.
		//
		// O estrago era visivel e ninguem tinha citado: a fagulha SUMIA por inteiro enquanto o Legendary
		// VOAVA (`flight_east`) e apagava a cada soco (`attack_east`), voltando entre um golpe e outro --
		// um pisca-pisca no meio da luta. Parado e andando funcionava por acidente: com o corpo em
		// `default_*` a linha `if (f.HasAnimation(fam))` casa o `default` cru, e com o corpo em `walk_*` o
		// ramo do `outra` faz o mesmo. Nenhuma das outras poses tem esse atalho.
		//
		// SO SOBRA PRA QUEM NAO TEM DIRECAO ALGUMA, e por isso vem por ULTIMO: qualquer folha com
		// `default_south` ja voltou dois degraus acima. Ou seja, isto nao muda uma unica escolha de
		// cabelo, roupa ou rabo -- eles sao recortes direcionais do boneco e todos tem `default_south`.
		// Quem cai aqui e efeito de animacao unica, e pra ele o certo NAO e sumir: ele nao veste o corpo,
		// ele brilha por cima dele, e brilho nao tem lado.
		// ====================================================================================================
		if (f.HasAnimation("default")) return "default";
		if (f.HasAnimation("walk")) return "walk";
		return null;
	}

	private void Aplicar(AnimatedSprite2D sprite, string? doCorpo, bool force)
	{
		if (sprite.SpriteFrames == null) return;

		string? nome = Escolher(sprite, doCorpo);
		if (nome == null)
		{
			// a peca nao tem nem uma pose parada nesta direcao: some, em vez de desenhar
			// qualquer coisa por cima do corpo
			sprite.Visible = false;
			return;
		}

		// ============================ AQUI E O UNICO LUGAR QUE LIGA UMA CAMADA ============================
		// Era `sprite.Visible = true` cravado, e isso fazia deste metodo o DONO ESCONDIDO da
		// visibilidade: qualquer regra escrita fora dele durava ate a proxima troca de pose. Foi o
		// que aconteceu quando o Oozaru entrou -- as camadas eram escondidas na troca de corpo e o
		// `Aplicar(force: true)` da linha seguinte ja as devolvia, e depois cada passo do macaco
		// devolvia de novo.
		//
		// Agora a regra mora numa funcao so (<see cref="Escondida"/>) e este metodo a consulta.
		// ==========================================================================================
		sprite.Visible = !Escondida(sprite);

		// "sincronizada" = esta na MESMA pose do corpo. Quem esta numa substituta fica
		// congelada no primeiro quadro em vez de acompanhar um ciclo que nao e o dela.
		//
		// SO VALE PRA PARTE DO CORPO (cabelo, roupa, rabo, corpo-da-forma). A camada de EFEITO nem
		// consulta esta meta -- ela tem relogio proprio, ver `EhEfeito` e o bloco no `_Process`. A meta
		// continua sendo escrita pra TODAS de proposito: e por este funil unico que a colada passa, e
		// tirar a colada daqui e o caminho conhecido pro node sair da alcada da `Escondida`.
		bool naPose = nome.StartsWith(Familia(), StringComparison.Ordinal);
		sprite.SetMeta("sync", naPose);

		if (force || sprite.Animation != nome) sprite.Animation = nome;
		sprite.Stop();          // ninguem toca sozinho: o relogio desta classe manda em todos
		sprite.Frame = 0;
	}
}
