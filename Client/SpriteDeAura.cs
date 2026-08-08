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
	/// A FOLHA DE TODO MUNDO: 4 quadros de 96x96, desenhados em cinza pra serem coloridos por cima
	/// (ver o `tingir` do `Aura.gdshader`). E a `colorablebigaura` do original.
	/// </summary>
	public const string FolhaBase = "res://Assets/Sprites/Auras/colorablebigaura.tres";

	/// <summary>A unica excecao: as linhas Legendary. Ver `Core/Forms/Formas.cs`, `FolhaDeAura`.</summary>
	public const string FolhaLssj = "res://Assets/Sprites/Auras/AuraLSSjBig.tres";

	/// <summary>A escada Saiyajin comum. Arte JA dourada -- ver `SemTinta`.</summary>
	public const string FolhaSsj = "res://Assets/Sprites/Auras/AuraSSjBig.tres";

	/// <summary>
	/// A CHAMA QUENTE DO KI DIVINO -- `gkaura = 'FieryGod.dmi'` (`AuraObject.dm:10`). SSG, o SSG da
	/// linha Rose e a linha Prodigial inteira. Arte ja colorida (pessego/laranja).
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
	/// </summary>
	public bool SemTinta => _simbolo is { } s ? PreColorida(s) : _folha == FolhaSsj;

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
	/// </summary>
	public static string CaminhoDa(Jandirus.Core.Forms.FolhaDeAura f) => f switch
	{
		Jandirus.Core.Forms.FolhaDeAura.Lssj => FolhaLssj,
		Jandirus.Core.Forms.FolhaDeAura.Ssj => FolhaSsj,
		Jandirus.Core.Forms.FolhaDeAura.DeusQuente => FolhaDeusQuente,
		Jandirus.Core.Forms.FolhaDeAura.DeusFrio => FolhaDeusFrio,
		Jandirus.Core.Forms.FolhaDeAura.DeusRosa => FolhaDeusRosa,
		_ => FolhaBase,
	};

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
		DefinirFolha(CaminhoDa(f));
	}

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
		if (_s != null) { _s.QueueFree(); _s = null; _mat = null; }
		if (_aceso) { Montar(); Pintar(); }
	}

	public void Definir(bool aceso, Color cor, float forca = 1f)
	{
		_cor = cor;
		_forca = forca;

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
	}
}
