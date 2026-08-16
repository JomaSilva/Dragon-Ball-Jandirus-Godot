using Godot;

namespace Jandirus.Client;

/// <summary>
/// O DESENHO DA AURA -- e ele e o sprite do jogo antigo, colorido, nao um efeito inventado.
///
/// ============================ UM SPRITE SO, TINGIDO ============================
/// Procurando "a aura de cada forma" no DM nao se acha uma por forma: acha-se UMA,
/// `colorablebigaura.dmi` (`Power Control.dm:9`, e escolhivel em `Settings.dm:410`). O que muda
/// de forma pra forma e a COR -- `centerAura()` faz
/// `icolor = rgb(container.AuraR, container.AuraG, container.AuraB)`.
///
/// Isso casa com o port sem esforco: `FormaDef.Aura` ja e uma cor em hexa. Entao ha um desenho e
/// uma paleta, e nao dezoito arquivos pra manter em sincronia.
///
/// (A `Aurabigcombined.dmi`, que aparece em doze lugares do DM, era o FLASH das cinematicas ate o
/// dono pedir o contrario -- *"vamos trocar das cinematicas o Aurabigcombined pela propria aura da
/// transformaçao q vc ta virando"*. Hoje a cena desenha ESTA mesma folha, entao nao ha mais duas
/// artes de aura no jogo: ha uma, e as tres variantes abaixo.)
/// ==============================================================================
///
/// ============================ SPRITE NAO E LUZ, MAS ANDA COM ELA ============================
/// Sao duas camadas distintas -- este sprite e a `PointLight2D` da <see cref="Aura"/> --, e por um
/// tempo elas responderam a coisas diferentes: sprite pra quem carregava, luz so pra quem estava
/// transformado. Isso deixou de valer quando a forma parou de acender aura sozinha; hoje as duas
/// saem do MESMO estado:
///
///   sem chama                 ->  nada
///   chama (carga, sobrecarga  ->  este sprite + a luz da <see cref="Aura"/>, na cor da chama
///   ou cinematica)
///
/// Quem faz a juncao e o `Aura.Aplicar`, num lugar so. Ligar uma e esquecer a outra ja foi defeito
/// duas vezes ("a aura das transformaçoes n estao brilhando ao apertar C" foi a segunda).
/// ==========================================================================================
/// </summary>
public partial class SpriteDeAura : Node2D
{
	/// <summary>
	/// A FOLHA DE TODO MUNDO: 8 quadros de 96x96, coloridos por fora (ver o `tingir` do
	/// `Aura.gdshader`).
	///
	/// ============================ ERA A `colorablebigaura`, E O DONO TROCOU ============================
	/// Pedido dele, marcado como extrema importancia: *"mudar o sprite da CARGA/AURA DE CARREGAMENTO
	/// DE KI e de KI ACIMA DE 100% da FORMA BASE (e das formas q usam o mesmo sprite da base, como o
	/// MISTICO etc) para o sprite `Aura, Big.png`"*.
	///
	/// **E UMA CONSTANTE SO, e e de proposito que seja**: as tres coisas que ele nomeou -- a aura da
	/// base, a carga do C e o Ki acima de 100% -- sao o MESMO desenho lendo o MESMO campo. A carga e
	/// o excesso ja se distinguiam por `forca` e cadencia, nunca por folha (ver `CargaVisual.Definir`).
	/// E o "etc" tem 18 nomes: tudo que cai no `_ => FolhaDeAura.Base` do `Catalogo.Folha` -- Mistico
	/// e Beast, a linha inteira do Frost Demon, Oozaru, Namekiano, Heran, Alien, Bio perfeito e a
	/// `destroyer`. Dezenove formas trocaram de chama nesta linha.
	///
	/// ============================ ELA ESTAVA NO DISCO E MORTA -- A QUARTA VEZ ============================
	/// O `.png`, o `.import` e o `.tres` ja existiam e o Godot ja os tinha importado; nenhum `.cs`
	/// citava o nome. Depois dos 35 atlas, da `FieryGod` e da `Supa Saiyan Rose Aura-1`, e a quarta
	/// vez que este projeto acha arte convertida e nunca ligada.
	///
	/// HA TRES ARQUIVOS COM ESTE DESENHO e dois deles sao a mesma fita: `AuraBig.png` e
	/// `Assets/Sprites/DU/VFX/Aura, Big.png` sao IDENTICOS byte a byte (768x96, 8 quadros em fita).
	/// Este e o terceiro -- 288x288, grade 3x3 com 8 quadros usados --, e e o que o dono nomeou.
	///
	/// ============================ E O DESENHO DELA MORA NO ALFA ============================
	/// `rgb(0,0,0)` nos 27.248 pixels opacos, 100% deles. Ver <see cref="FormaNoAlfa"/> e o
	/// `forma_no_alfa` do shader -- sem aquele ramo, esta linha sozinha pinta uma silhueta PRETA.
	/// ==================================================================================================
	/// </summary>
	public const string FolhaBase = "res://Assets/Sprites/Auras/Aura, Big.tres";

	/// <summary>A unica excecao: as linhas Legendary. Ver `Core/Forms/Formas.cs`, `FolhaDeAura`.</summary>
	public const string FolhaLssj = "res://Assets/Sprites/Auras/AuraLSSjBig.tres";

	/// <summary>A escada Saiyajin comum. Arte JA dourada -- ver `SemTinta`.</summary>
	public const string FolhaSsj = "res://Assets/Sprites/Auras/AuraSSjBig.tres";

	/// <summary>
	/// A CHAMA QUENTE DO KI DIVINO -- `gkaura = 'FieryGod.dmi'` (`AuraObject.dm:10`). SSG e o SSG da
	/// linha Rose, e mais ninguem. Arte ja colorida (pessego/laranja) -- medida: 10.912 pixels opacos,
	/// tom dominante `ff411c`, a folha inteira entre 7 e 10 graus de matiz.
	///
	/// A LINHA PRODIGIAL ESTAVA AQUI E SAIU por ordem do dono (*"o mistico e beast tao usando a aura
	/// de carga do ssj god"*). Ver `Catalogo.Folha` no Core -- e nao "conserte" isto de volta lendo o
	/// `AuraObject.dm`, que de fato poe os dois aqui.
	///
	/// ============================ ELA ESTAVA NO DISCO E MORTA ============================
	/// O `.png`, o `.import` e o `.tres` ja existiam e o Godot ja os tinha importado; nenhum `.cs`
	/// citava o nome. E a segunda vez que este projeto acha arte convertida e nunca ligada (a
	/// primeira foram os 35 atlas de animacao), e o remedio e o mesmo: a bancada confere
	/// `ResourceLoader.Exists` folha por folha, e nao so as que alguem lembrou de testar.
	/// ================================================================================
	/// </summary>
	public const string FolhaDeusQuente = "res://Assets/Sprites/Auras/FieryGod.tres";

	/// <summary>
	/// A CHAMA FRIA -- `sgkaura = 'FieryGodBlue.dmi'` (`AuraObject.dm:12`). Blue e Blue Evolution.
	/// </summary>
	public const string FolhaDeusFrio = "res://Assets/Sprites/Auras/FieryGodBlue.tres";

	/// <summary>
	/// A CHAMA DO ROSE, e ela e arte PROPRIA -- ver
	/// <see cref="Jandirus.Core.Forms.FolhaDeAura.DeusRosa"/>.
	///
	/// ELA ESTAVA NO DISCO E MORTA, como a `FieryGod` antes dela: o `.png`, o `.import` e o `.tres` ja
	/// existiam, o Godot ja os tinha importado, e nenhum `.cs` citava o nome. Quem a achou foi o dono.
	/// </summary>
	public const string FolhaDeusRosa = "res://Assets/Sprites/Auras/Supa Saiyan Rose Aura-1.tres";

	/// <summary>
	/// FOLHA QUE NAO SE PINTA. O dono: "ela ja vem naturalmente dourada, nem precisa colori".
	///
	/// Pintar por cima seria pior que inutil: o shader descarta o RGB do arquivo e usa o canal mais
	/// forte como intensidade -- o dourado do desenho e jogado fora e trocado pela cor de fora. E a
	/// mesma armadilha que deixou a aura do SSJ marrom.
	///
	/// ============================ E POR QUE ELA LE O SIMBOLO, E NAO O CAMINHO ============================
	/// Era `_folha == FolhaSsj`, comparacao de string. Isso deixou de bastar quando duas folhas do enum
	/// apontavam pro MESMO arquivo (a `DeusRosa` era a `FieryGodBlue` tingida), e continua nao bastando
	/// agora que ela tem folha propria: a pergunta "esta folha se pinta?" e do SIMBOLO, e responde-la
	/// por caminho amarra a regra ao nome do arquivo -- duas folhas novas apontando pro mesmo `.tres`
	/// (que ja aconteceu uma vez) voltariam a dar a mesma resposta pra duas perguntas diferentes.
	///
	/// O caminho continua sendo o fallback pra quem entra pelo `DefinirFolha(string)` (so a bancada),
	/// e ai a resposta antiga vale porque ali nao ha simbolo pra ler.
	/// ==================================================================================================
	///
	/// SEM FOLHA NAO HA TINTA, e este ramo vem PRIMEIRO de proposito. Ele fecha a armadilha 2 do
	/// simbolo novo: a <see cref="PreColorida"/> responde por uma lista, e um simbolo fora dela cai em
	/// "se pinta" -- ou seja a <see cref="FolhaDeAura.Nebulosa"/> responderia que se tinge, e mandar cor
	/// pra uma folha que nao existe e ruido que passa VERDE numa leitura de uniform.
	/// </summary>
	public bool SemTinta => _semFolha || (_simbolo is { } s ? PreColorida(s) : _folha == FolhaSsj);

	/// <summary>
	/// AS FOLHAS QUE JA VEM PINTADAS -- hoje TODAS menos a `Base` e a `Lssj`. A `AuraSSjBig` e dourada
	/// e as duas divinas tem `icolor = null` no proprio original (`AuraObject.dm:168` e `:175`), que e
	/// o jeito do DM dizer a mesma coisa.
	///
	/// A `DeusRosa` ENTROU NESTA LISTA quando ganhou arte propria: medida, a folha dela e magenta e
	/// carmesim (`#9d1c9d`, `#93092c`, `#6d0129`). Tingi-la seria jogar esse desenho fora pra pintar
	/// por cima -- a mesma armadilha do paragrafo acima.
	/// </summary>
	public static bool PreColorida(Jandirus.Core.Forms.FolhaDeAura f) =>
		f is Jandirus.Core.Forms.FolhaDeAura.Ssj
		  or Jandirus.Core.Forms.FolhaDeAura.DeusQuente
		  or Jandirus.Core.Forms.FolhaDeAura.DeusFrio
		  or Jandirus.Core.Forms.FolhaDeAura.DeusRosa;

	/// <summary>
	/// ============================ EM QUE CANAL MORA O DESENHO DESTA FOLHA? ============================
	/// A <see cref="PreColorida"/> responde *"a cor vem de dentro do arquivo?"*. Esta responde outra
	/// pergunta, e as duas sao INDEPENDENTES -- confundi-las e o que faz uma folha sair preta ou
	/// chapada. Medido pixel a pixel nas seis folhas:
	///
	/// <code>
	///   folha                       RGB                        alfa            desenho mora em
	///   colorablebigaura            cinza, pico 200/255        255 niveis      RGB   (falso)
	///   Aura, Big                   PRETO PURO (0,0,0) 100%    250 niveis      ALFA  (verdadeiro)
	///   AuraSSjBig                  `ffff80` CHAPADO 100%      250 niveis      ALFA  (verdadeiro)
	///   AuraLSSjBig                 `d9ff00` CHAPADO 100%      250 niveis      ALFA  (verdadeiro)
	///   FieryGod                    variado                    CONSTANTE 160   RGB   (falso)
	///   FieryGodBlue                1.422 cores                variado         RGB   (falso)
	///   Supa Saiyan Rose Aura-1     5 cores                    variado         RGB   (falso)
	/// </code>
	///
	/// ============================ A `Aura, Big` E A `AuraSSjBig` SAO O MESMO DESENHO ============================
	/// Conferido canal a canal: o alfa das duas e IDENTICO nos 82.944 pixels (288x288), zero
	/// diferenca. A `AuraSSjBig` e literalmente esta folha com um `ffff80` chapado por cima. Isso e o
	/// que da confianca nesta troca sem precisar de foto: a chama da base vai renderizar pelo MESMO
	/// caminho que a do Super Saiyajin ja usa e que o dono ja aprovou -- mesma arte, mesma conta,
	/// mudando so de onde vem a cor.
	///
	/// ============================ E POR QUE A LSSj ENTRA NA LISTA ============================
	/// Porque ela satisfaz a regra, e nao pra "consertar" nada: ela e chapada em `d9ff00`, entao o
	/// `i` do shader ja vale exatamente 1,0 em todo pixel dela (`255/255 / 0,784 = 1,276`, cortado
	/// pelo `clamp`). Marca-la aqui e um NAO-OPERACAO comprovavel -- nenhum pixel muda. Deixa-la de
	/// fora e que seria a armadilha: duas folhas iguais em natureza respondendo diferente faria a
	/// proxima pessoa acreditar que ha duas classes onde ha uma.
	///
	/// ============================ AS PRE-COLORIDAS NAO PRECISAM DISTO ============================
	/// A `AuraSSjBig` esta aqui por HONESTIDADE, nao por necessidade: sendo pre-colorida ela sai pelo
	/// ramo `!tingir`, que copia `c.rgb` e nunca calcula `i`. Ou seja este valor e lido e descartado
	/// pra ela. Escreve-lo mesmo assim e o que impede a lista de virar "as folhas que precisam" --
	/// uma lista que muda de conteudo quando alguem mexe na <see cref="PreColorida"/>, e nao quando a
	/// ARTE muda, que e a unica coisa que esta funcao deveria acompanhar.
	/// ==============================================================================================
	/// </summary>
	public static bool FormaNoAlfa(Jandirus.Core.Forms.FolhaDeAura f) =>
		f is Jandirus.Core.Forms.FolhaDeAura.Base
		  or Jandirus.Core.Forms.FolhaDeAura.Ssj
		  or Jandirus.Core.Forms.FolhaDeAura.Lssj;

	/// <summary>
	/// O DESENHO DESTE SPRITE ESTA NO ALFA? Ver <see cref="FormaNoAlfa"/>. Sem simbolo (so a
	/// bancada entra por caminho cru) responde pelo arquivo, como o <see cref="SemTinta"/> faz.
	/// </summary>
	public bool DesenhoNoAlfa => _simbolo is { } s
		? FormaNoAlfa(s)
		: _folha == FolhaBase || _folha == FolhaSsj || _folha == FolhaLssj;

	/// <summary>Qual folha esta carregada agora. Pra bancada.</summary>
	public string FolhaDeTeste => _folha;

	/// <summary>
	/// Qual SIMBOLO esta carregado -- nulo quando entrou pelo caminho cru. Pra bancada: e a unica
	/// medida que separa a `DeusFrio` da `DeusRosa`, que compartilham o arquivo.
	/// </summary>
	public Jandirus.Core.Forms.FolhaDeAura? SimboloDeTeste => _simbolo;

	private Jandirus.Core.Forms.FolhaDeAura? _simbolo;

	/// <summary>
	/// O SIMBOLO DO CORE -> O ARQUIVO. **UMA traducao, e ela mora aqui.**
	///
	/// ============================ POR QUE ELA SUBIU PRA CA ============================
	/// Este `switch` estava escrito palavra por palavra na <see cref="Aura"/> e na
	/// <see cref="CargaVisual"/> -- os dois nodes que desenham a mesma arte. Era suportavel enquanto
	/// eram dois; a cinematica virou o TERCEIRO desenho (a chama da cena, ver
	/// <see cref="Transformacao"/>), e uma terceira copia e o jeito conhecido de uma folha nova
	/// nascer certa em dois lugares e errada no que alguem esquecer.
	///
	/// Quem DECIDE continua sendo o Core (`Catalogo.Folha`, derivado da linha da forma); aqui so se
	/// traduz o simbolo em `res://`, que e a fronteira exata entre o Core e o Godot.
	/// ================================================================================
	///
	/// ============================ NULO E UMA RESPOSTA: "A MINHA NAO E FOLHA" ============================
	/// A <see cref="Jandirus.Core.Forms.FolhaDeAura.Nebulosa"/> nao tem arquivo -- o Ultra Instinto e o
	/// `ultra_ego` desenham a nuvem procedural e chama nenhuma (a segunda em roxo, pedido do dono; a
	/// paleta e do Core e o simbolo e o mesmo). Quem chama isto tem que tratar o nulo; quem desenha
	/// (este proprio node, ver <see cref="DefinirFolha(Jandirus.Core.Forms.FolhaDeAura)"/>) trata uma
	/// vez pelos tres desenhistas.
	///
	/// ============================ E O `_ =>` MORREU, QUE ERA A ARMADILHA 1 ============================
	/// Havia um `_ => FolhaBase` aqui, e ele e literalmente o defeito que esta tarefa conserta: o Ultra
	/// Instinto nao tinha ramo no Core, caia no fallback, e acendia a `colorablebigaura` por cima da
	/// nuvem. Um simbolo sem arquivo cai CALADO na folha de todo mundo, e o unico sintoma e uma chama
	/// que ninguem pediu -- sem uma linha vermelha em lugar nenhum.
	///
	/// Sem o `_`, o `switch` passa a ser exaustivo sobre o enum: um simbolo novo vira **aviso de
	/// compilacao** (CS8509) na hora, e nao uma aura errada descoberta em jogo tres meses depois. Foi
	/// pra isso que ele saiu -- nao por estilo.
	/// ==================================================================================================
	/// </summary>
	// CS8524 e o aviso de "e se alguem fizer `(FolhaDeAura)7`?", e essa nao e uma pergunta deste
	// projeto: o simbolo sempre vem do `Catalogo.Folha`. O aviso que se QUER e o outro (CS8509, "falta
	// um membro do enum"), e ele so existe enquanto nao houver `_ =>` -- calar o primeiro com um
	// `default` calaria o segundo junto, que e o defeito inteiro desta tarefa voltando pela porta dos
	// fundos. Entao o silencio e cirurgico: um pragma nesta linha, e nao um ramo.
#pragma warning disable CS8524
	public static string? CaminhoDa(Jandirus.Core.Forms.FolhaDeAura f) => f switch
	{
		Jandirus.Core.Forms.FolhaDeAura.Base => FolhaBase,
		Jandirus.Core.Forms.FolhaDeAura.Lssj => FolhaLssj,
		Jandirus.Core.Forms.FolhaDeAura.Ssj => FolhaSsj,
		Jandirus.Core.Forms.FolhaDeAura.DeusQuente => FolhaDeusQuente,
		Jandirus.Core.Forms.FolhaDeAura.DeusFrio => FolhaDeusFrio,
		Jandirus.Core.Forms.FolhaDeAura.DeusRosa => FolhaDeusRosa,
		Jandirus.Core.Forms.FolhaDeAura.Nebulosa => null,
	};
#pragma warning restore CS8524

	/// <inheritdoc cref="CaminhoDa"/>
	public void DefinirFolha(Jandirus.Core.Forms.FolhaDeAura f)
	{
		// O SIMBOLO ANTES DO CAMINHO, e a ordem continua sendo regra: `DefinirFolha(string)` LIMPA o
		// simbolo quando o caminho nao bate com ele, entao escrever depois apagaria o que acabou de
		// ser escolhido -- e o `SemTinta` voltaria a responder pelo caminho.
		//
		// (Havia aqui um `mudouSoATinta` que reprintava quando duas folhas do enum dividiam o mesmo
		// `.tres` -- o caso `DeusFrio`/`DeusRosa`. Ele MORREU com a divisao: hoje cada simbolo tem
		// arquivo proprio, entao a condicao nunca e verdadeira. Codigo que so pode ser falso e codigo
		// morto, e ele saiu junto com o motivo dele.)
		_simbolo = f;

		// ============================ O SIMBOLO SEM ARQUIVO APAGA O DESENHO, AQUI E SO AQUI ============================
		// `CaminhoDa` devolvendo nulo quer dizer "esta forma nao usa folha" (hoje so o Ultra Instinto).
		// A decisao mora NESTE node, e nao nos donos dele, porque os donos sao TRES -- o node `Aura`, a
		// `CargaVisual` e a chama da cinematica -- e cada um deles ja mostrou, uma vez, o que custa
		// ensinar uma regra a dois e esquecer o terceiro. Aqui a regra e uma e vale pra quem quer que
		// esteja segurando este sprite: sem folha, nao ha o que desenhar.
		//
		// E E ISTO QUE IMPEDE AS DUAS CHAMAS EMPILHADAS em Ultra Instinto -- os dois desenhistas do
		// corpo recebem o MESMO simbolo (ver `World.PrepararAuraDaForma`), entao os dois ficam mudos
		// no mesmo instante. Nao ha estado em que um saiba e o outro nao.
		//
		// `_folha` VAI A VAZIO junto: ele e a memoria de "ja estou com este arquivo montado", e deixar
		// o caminho velho ali faria a VOLTA pra uma forma com folha (sair do Ultra Instinto) cair no
		// `if (_folha == caminho) return` e nunca remontar o sprite -- a chama sumiria pra sempre.
		// ==========================================================================================================
		if (CaminhoDa(f) is { } caminho) { _semFolha = false; DefinirFolha(caminho); return; }

		_semFolha = true;
		_folha = "";
		if (_s != null) { _s.QueueFree(); _s = null; _mat = null; }
		Visible = false;
		SetProcess(false);
	}

	/// <summary>
	/// ESTE SPRITE ESTA VESTINDO UM SIMBOLO SEM ARQUIVO? Ver
	/// <see cref="DefinirFolha(Jandirus.Core.Forms.FolhaDeAura)"/>. Publico pra bancada: e a unica
	/// medida que separa "a chama esta apagada agora" de "esta chama nao pode acender".
	/// </summary>
	public bool SemFolha => _semFolha;

	private bool _semFolha;

	private string _folha = FolhaBase;

	/// <summary>
	/// Segundos de um ciclo da aura. O .dmi nao traz duracao util (o BYOND animava no proprio
	/// relogio), entao o valor e escolhido: rapido o bastante pra parecer energia agitada, lento
	/// o bastante pra nao virar estroboscopio atras do personagem.
	/// </summary>
	private const double Ciclo = 0.32;

	/// <summary>
	/// O `ICON_ADD` de novo, e pelo mesmo motivo do corpo: `modulate` MULTIPLICA, e a aura ja vem
	/// desenhada em tons claros -- multiplicar por dourado daria um borrao escuro sem os fachos.
	/// Somar clareia e preserva o desenho. E o `blend_mode = BLEND_MODE_ADD` do original.
	/// </summary>
	/// <summary>
	/// O CODIGO DESTE EFEITO mora num `.gdshader` de verdade -- ver o comentario de
	/// <see cref="CharacterVisual"/>: efeito procedural nao se acerta lendo codigo, se acerta
	/// arrastando o valor e OLHANDO, e pra isso ele precisa abrir no editor do Godot.
	/// </summary>
	private const string CaminhoDoShader = "res://Assets/Shaders/Aura.gdshader";

	/// <summary>
	/// ============================ TODA AURA COMECA NO PE ============================
	/// Palavras do dono, e por isso isto e uma REGRA e nao um numero: a ancora sai da ALTURA DO
	/// QUADRO, entao folha nova (a `Aura, Big`, a `AuraLSSJBig`, o que vier) ja nasce no lugar sem
	/// ninguem recalcular nada. Um `-16` cravado so estaria certo pra quadros de 64.
	///
	/// E a regra do original: `AuraObject.dm:61-62` manda todo icone que nao seja 32x32 pra
	/// `I.center("center-bottom")`, e `ImageCenter.dm:34-36` devolve `pixel_x = -(W/2)+16` e
	/// `pixel_y = 0` -- a BASE do icone encostando na base do ladrilho do mob.
	///
	/// A CONTA, com o sprite `Centered`:
	///   a base do quadro cai em +altura/2 da origem do node
	///   os pes caem em <see cref="LinhaDosPes"/>
	///   -> Offset.Y = LinhaDosPes - altura/2      (64 -> -16;  32 -> 0;  96 -> -32)
	///
	/// Horizontalmente nao ha o que corrigir: `-(W/2)+16` da 0 pra qualquer folha de 32 de largura,
	/// que e o caso das nossas (a arte ja e centrada no quadro).
	/// ============================================================================
	/// </summary>
	public static Vector2 AncoraPara(float alturaDoQuadro) =>
		new(0, LinhaDosPes - alturaDoQuadro * 0.5f);

	/// <summary>
	/// A linha dos PES no espaco do personagem: o corpo e um quadro de 32 `Centered`, e o desenho
	/// vai ate a ultima linha dele (medido: zero margem vazia embaixo, nas 6 folhas base).
	/// </summary>
	public const float LinhaDosPes = 16f;

	private static Shader? _shader;
	private static Shader Sh => _shader ??= ResourceLoader.Load<Shader>(CaminhoDoShader);

	private AnimatedSprite2D? _s;
	private ShaderMaterial? _mat;
	private double _relogio;
	private int _quadros;

	private bool _aceso;
	private Color _cor = Colors.White;
	private float _forca = 1f;

	public override void _Ready()
	{
		// ATRAS DO CORPO. Por cima, a aura cobriria o rosto e o personagem viraria uma mancha --
		// ============================ ATRAS DO CORPO, MAS NAO DEBAIXO DO CHAO ============================
		// Isto era `ZIndex = -4`, e por isso o dono nunca via aura nenhuma ao segurar C: a zona
		// desenha o `Chao` em z -2 e o `Decor` em z -1, e no Godot o z_index tem precedencia
		// ABSOLUTA sobre o Y-sort. A chama estava sempre la, animando, visivel -- e enterrada.
		//
		// Nada de aura tem z proprio agora: quem poe ela ATRAS do corpo e a ORDEM DE IRMAO. O
		// proprio projeto ja escreve essa regra em `World.cs:646-648` ("com YSortEnabled o indice
		// do filho e o desempate quando o Y empata"), e Aura, Carga e Visual sao filhos do MESMO
		// node, na MESMA posicao -- Y empatado, desempate pelo indice. Ver a ordem de `AddChild`
		// em `World.cs`, que teve que ser trocada junto: sem isso a aura passaria pra FRENTE.
		//
		// Afundar o `Chao` em vez disto seria pior: sao 40 `.tscn` mais a reconversao binaria, mais
		// o `PlanetaProcedural.cs:281` que crava o z em C#, mais o `Decalques.cs:124` que esta em
		// z -2 colado no chao -- baixar o chao deixaria os decalques flutuando.
		// ================================================================================================
		ZIndex = 0;
		Visible = false;
		SetProcess(false);
	}

	/// <summary>
	/// Acende (ou apaga) o desenho. <paramref name="forca"/> 1 e a aura comum; acima disso ela
	/// fica mais densa, o que separa visualmente o esforco de carregar de uma transformacao.
	/// </summary>
	/// <summary>
	/// TROCA A FOLHA. Derruba o sprite montado -- trocar `SpriteFrames` em cima do node vivo deixa
	/// pra tras o `Offset`, que e calculado da ALTURA DO QUADRO (ver <see cref="AncoraPara"/>) e muda
	/// junto com a folha. Hoje as tres folhas sao 96x96 e a conta da no mesmo; ela existe pra que a
	/// quarta -- de qualquer tamanho -- ja nasca com a base no pe. Remontar e barato: so acontece
	/// quando a forma muda.
	/// </summary>
	public void DefinirFolha(string caminho)
	{
		// QUEM ENTRA PELO CAMINHO CRU PERDE O SIMBOLO (so a bancada faz isso), e perder e o certo:
		// guardar um simbolo que ninguem escolheu faria o `SemTinta` responder por uma folha que o
		// chamador nao pediu. Nulo la e "nao ha simbolo pra ler" e cai no fallback por caminho.
		if (_simbolo is { } s && CaminhoDa(s) != caminho) _simbolo = null;
		if (_folha == caminho) return;
		_folha = caminho;
		_semFolha = false;
		if (_s != null) { _s.QueueFree(); _s = null; _mat = null; }
		// `Visible` E `SetProcess` ENTRAM AQUI, e nao so o `Montar`: quem estava sem folha (Ultra
		// Instinto) tem os dois DESLIGADOS mesmo com `_aceso` verdadeiro -- o estado ficou guardado
		// esperando uma folha. Sem estas duas escritas, sair do Ultra Instinto com o C segurado
		// remontaria o sprite invisivel e parado.
		if (_aceso) { Montar(); Visible = true; SetProcess(true); Pintar(); }
	}

	public void Definir(bool aceso, Color cor, float forca = 1f)
	{
		_cor = cor;
		_forca = forca;

		// SEM FOLHA O ESTADO E GUARDADO E NADA E DESENHADO. Guardar em vez de ignorar e o que faz a
		// volta funcionar: quem soltou o Ultra Instinto ainda com o C na mao ja esta com `_aceso`
		// verdadeiro, e o `DefinirFolha` acima acende no mesmo quadro em que a folha chega.
		if (_semFolha) { _aceso = aceso; Visible = false; SetProcess(false); return; }

		if (aceso == _aceso) { Pintar(); return; }
		_aceso = aceso;

		if (!aceso)
		{
			Visible = false;
			SetProcess(false);
			return;
		}

		Montar();
		_relogio = 0;
		Visible = true;
		SetProcess(true);
		Pintar();
	}

	/// <summary>
	/// Monta o sprite na PRIMEIRA vez que a aura acende, e nao no `_Ready`.
	///
	/// A grande maioria dos corpos da zona nunca acende aura nenhuma; criar o node e carregar a
	/// folha pra todos custaria em cada personagem que entra no campo de visao. Depois de montado
	/// ele fica -- acender de novo e so trocar `Visible`.
	/// </summary>
	private void Montar()
	{
		if (_s != null) return;

		var frames = ResourceLoader.Load<SpriteFrames>(_folha);
		// O AVISO CITAVA `colorablebigaura`, que nao e a folha que esta linha carrega. Aviso que
		// aponta pro arquivo errado e como nao ter aviso -- foi exatamente a confusao entre essas
		// duas folhas que custou quatro rodadas nesta sessao.
		if (frames == null) { GD.PushWarning($"[aura] nao carregou {_folha}"); return; }

		string anim = frames.HasAnimation("default") ? "default"
					: frames.GetAnimationNames() is { Length: > 0 } n ? n[0] : "";
		if (anim.Length == 0) return;

		_quadros = Mathf.Max(1, frames.GetFrameCount(anim));
		_mat = new ShaderMaterial { Shader = Sh };
		_s = new AnimatedSprite2D
		{
			Name = "Desenho",
			SpriteFrames = frames,
			Animation = anim,
			Centered = true,

				// Ver `AncoraPara`: sai da altura do quadro, entao vale pra qualquer folha.
				Offset = AncoraPara(frames.GetFrameTexture(anim, 0)?.GetHeight() ?? 0),
			Material = _mat,
			TextureFilter = TextureFilterEnum.Nearest,
		};
		AddChild(_s);
	}

	/// <summary>
	/// ONDE A BASE DA CHAMA CAI, no espaco do personagem. Tem que bater com
	/// <see cref="LinhaDosPes"/>. Julgar isto por foto ja me custou tres tentativas erradas.
	/// </summary>
	public float BaseDeTeste => Quadro() is { } q ? _s!.Offset.Y + q.GetHeight() * 0.5f : float.NaN;

	/// <summary>A largura do quadro. Se nao for 32, o recorte do `.tres` regrediu pra 64.</summary>
	public float LarguraDeTeste => Quadro() is { } q ? q.GetWidth() : float.NaN;

	/// <summary>
	/// EM QUE QUADRO DA FITA ESTA A CHAMA AGORA -- e ele existe pra a FOTO, nao pra a logica.
	///
	/// ============================ SEM ISTO, DUAS FOTOS NAO SE COMPARAM ============================
	/// A prova de que a folha base mudou e um PAR de fotos (a de hoje contra a da folha antiga) medido
	/// em pixel. Mas a chama anda sozinha -- 8 quadros em <see cref="Ciclo"/> segundos, um quadro a
	/// cada 40 ms --, entao duas fotos tiradas em instantes diferentes ja diferem por ANIMACAO. A
	/// medida ficaria alta com a folha trocada E com a folha igual, ou seja diria nada.
	///
	/// A bancada espera este numero bater no valor combinado antes de cada disparo. E ele e SO leitura:
	/// nao ha como escrever o quadro daqui, porque congelar a animacao pra fotografar seria fotografar
	/// um estado que o jogo nao tem.
	/// ================================================================================================
	/// </summary>
	public int QuadroDeTeste => _s?.Frame ?? -1;

	/// <summary>
	/// Quantos quadros a folha carregada REALMENTE toca. Zero enquanto a chama nunca acendeu.
	///
	/// **Nao e o numero de recortes do `.tres`** -- e o da animacao escolhida pelo <see cref="Montar"/>.
	/// A distincao virou medida quando a bancada da foto mostrou 4 onde o comentario prometia 8: a
	/// `Aura, Big.tres` (e a `AuraSSjBig.tres`) trazem os 8 quadros divididos em DUAS animacoes de 4
	/// (`default` e `2`), e so a primeira toca.
	/// </summary>
	public int QuantosQuadrosDeTeste => _s == null ? 0 : _quadros;

	/// <summary>
	/// SO BANCADA -- escreve o `forma_no_alfa` do material por fora, contra o que a folha manda.
	///
	/// ============================ ELE EXISTE PRA A MEDIDA PODER FICAR VERMELHA ============================
	/// O desastre concreto que esta troca de folha podia produzir e UM: `Aura, Big.png` e preto puro
	/// nos 27.248 pixels opacos, entao sem o ramo `forma_no_alfa` do shader ela sai como uma SILHUETA
	/// PRETA em volta de 19 formas. Nao ha estado de jogo que encene isso -- o valor certo e escrito
	/// pelo `Pintar` a cada pintura --, e uma prova que nunca ficou vermelha e uma frase.
	///
	/// Com esta porta a bancada renderiza a MESMA folha com o uniform errado e mede: se o preto nao
	/// aparecer ali, a medicao nao enxerga o desastre e nao vale nada -- e ai e a BANCADA que reprova.
	///
	/// Ele nao muda nada em jogo: ninguem o chama fora da `--diagchama`, e a proxima
	/// <see cref="Pintar"/> reescreve o valor certo por cima.
	/// ======================================================================================================
	/// </summary>
	public void ForcarDesenhoNoAlfaDeTeste(bool v) => _mat?.SetShaderParameter("forma_no_alfa", v);

	/// <summary>
	/// ============================ A COR COMO O SHADER A VE ============================
	/// Lida do MATERIAL e nao do campo `_cor`, pelo mesmo motivo do
	/// `CharacterVisual.ContornoNoMaterialDeTeste`: o campo diz o que o ultimo `Definir` PEDIU, e
	/// isto diz o que a chama REALMENTE recebeu. Os dois divergem exatamente onde este arquivo ja
	/// falhou -- `Montar()` cria o material tarde, e uma cor escrita antes da montagem fica so no
	/// campo.
	///
	/// E e a unica medicao que consegue comparar as DUAS chamas do corpo (a do node `Aura` e a da
	/// `CargaVisual`, que sao dois `SpriteDeAura` distintos) contra a mesma verdade. Foi haver duas
	/// respostas pra "de que cor e esta chama" que produziu o dourado fixo na base acima dos 100%.
	///
	/// NULO ENQUANTO A CHAMA NUNCA ACENDEU (o material so nasce no `Montar`), e quem le tem que
	/// tratar isso como "nao ha o que medir" -- e nao como "esta certo".
	/// ==============================================================================
	/// </summary>
	public Color? CorNoMaterialDeTeste => _mat?.GetShaderParameter("cor") is { } v
		&& v.VariantType == Variant.Type.Vector3
			? new Color(v.AsVector3().X, v.AsVector3().Y, v.AsVector3().Z)
			: null;

	private Texture2D? Quadro() =>
		_s?.SpriteFrames is { } f ? f.GetFrameTexture(_s.Animation, 0) : null;

	public override void _Process(double delta)
	{
		if (_s == null || _quadros <= 1) return;
		// RELOGIO PROPRIO em vez de `Play()`: o mesmo motivo das camadas do corpo -- assim a
		// cadencia e nossa e nao depende do que o conversor de .dmi tiver escrito na folha.
		_relogio = (_relogio + delta) % Ciclo;
		int q = Mathf.Clamp((int)(_relogio / Ciclo * _quadros), 0, _quadros - 1);
		if (_s.Frame != q) _s.Frame = q;
	}

	private void Pintar()
	{
		if (_mat == null) return;
		_mat.SetShaderParameter("cor", new Vector3(_cor.R, _cor.G, _cor.B));
		_mat.SetShaderParameter("forca", _forca);
		// Ver o cabecalho de `Aura.gdshader`: mandar branco NAO e o mesmo que nao tingir.
		_mat.SetShaderParameter("tingir", !SemTinta);
		// A SEGUNDA PERGUNTA SOBRE A MESMA FOLHA, e ela vai pelo mesmo lugar de proposito: as duas
		// saem daqui ou nenhuma sai. Escrever `tingir` num lugar e `forma_no_alfa` noutro e como as
		// tres chamas deste projeto ja divergiram antes. Ver `SpriteDeAura.FormaNoAlfa`.
		_mat.SetShaderParameter("forma_no_alfa", DesenhoNoAlfa);
	}
}
