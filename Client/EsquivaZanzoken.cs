using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ O `flick('Zanzoken.dmi', M)` DA ESQUIVA ============================
/// `CombatMovement.dm:286`. E preciso ser literal sobre o que `flick()` faz no BYOND, porque a
/// primeira versao deste efeito entendeu o contrario e o dono viu na tela:
///
///   **`flick()` nao SOBREPOE nada. Ele TROCA o icone do mob por outro e devolve depois.**
///
/// Durante os tres quadros do `Zanzoken.dmi` o defensor NAO ESTA LA -- o que se ve no lugar dele
/// sao as listras. A versao anterior (a `CombatFx.Esquiva`, deletada -- e com ela o shader de
/// silhueta que so ela usava) desenhava tres silhuetas TINGIDAS DE AZUL por cima do corpo, com o
/// corpo continuando visivel e um anel de choque em volta. O dono descreveu os tres defeitos de
/// uma vez:
///
///   *"esse efeito de dodge ta estranho, era pra ser PRETO, o CIRCULO em volta da onda de choque
///   nem deveria ter, e o personagem q desviou deveria ficar INVISIVEL, as LINHAS PRETAS
///   aparecerem ONDE O CORPO DELE ESTA, e dps as linhas iam sumir e o CORPO DELE APARECER DNV"*.
///
/// ============================ A COR: NAO HA COR ============================
/// O `Zanzoken.png` foi MEDIDO, pixel a pixel, e nao consultado no catalogo (este porte ja
/// "consertou" cor duas vezes lendo codigo em vez de arte, e errou nas duas): 4096 pixels, dos
/// quais 172 opacos, e os 172 sao exatamente `(0, 0, 0, 255)` -- PRETO CHAPADO. O resto e
/// transparente.
///
/// Ou seja, a arte ja nasce da cor que o dono pediu. O conserto da cor nao e pintar de preto: e
/// PARAR DE TINGIR. Este node nao tem shader, nao tem `Modulate` e nao tem constante de cor --
/// desenhar a textura como ela e no disco E o requisito.
///
/// ============================ E O RISCO QUE MANDA NESTE ARQUIVO ============================
/// A INVISIBILIDADE VAZAR. Um corpo que some e nao volta e infinitamente pior que um efeito feio,
/// e esquiva nao e evento raro: contra alguem dez vezes mais forte a bancada mediu 82% dos socos
/// esquivados, varias por segundo, SOBREPOSTAS. Tres decisoes de desenho saem so disso:
///
///   1. **DONO UNICO POR CORPO.** Nao existe "o segundo efeito": a segunda esquiva ACHA o node que
///      ja esta rodando neste corpo e so empurra o fim dele pra frente (<see cref="Trocar"/>). Com
///      um contador de dois donos, o primeiro a terminar devolveria o corpo com o segundo ainda
///      correndo; com dois nodes independentes, o primeiro a morrer restauraria a visibilidade por
///      baixo do segundo. Com UM dono nenhuma das duas existe, e o instante-de-fim so AVANCA --
///      exatamente o que o `flick()` do BYOND faz quando chega outro `flick` por cima.
///   2. **ESCONDE E MOSTRA NO MESMO LUGAR**, e o lugar e o par `_EnterTree`/`_ExitTree` deste node.
///      Nao ha caminho de saida que passe por um e nao pelo outro: `QueueFree` pelo cronometro,
///      nocaute, morte, transformacao, troca de zona, o dono deslogando, a cena inteira sendo
///      derrubada -- todos removem o node da arvore, e remover da arvore E devolver o corpo.
///   3. **FILHO DO PROPRIO CORPO.** Se o corpo for liberado, este node morre junto (e ai nao ha
///      corpo pra ficar invisivel). Se ele fosse filho da camada de atores, um corpo liberado no
///      meio do efeito deixaria o node vivo apontando pra um fantasma -- e o proximo corpo com o
///      mesmo `Name` herdaria o estrago.
///
/// ============================ O QUE SOME, E O QUE FICA ============================
/// A regra e a MESMA INVERSAO do <see cref="SubirComOVoo"/>, e pelo mesmo motivo: **todo filho
/// visual do corpo some, e quem NAO some declara isso em si mesmo**
/// (<see cref="INaoSomeComOCorpo"/>). Uma lista por nome escrita aqui esqueceria o node que outra
/// pessoa pendurar no corpo amanha -- e o defeito seria uma aura, um rabo ou uma nuvem de forma
/// flutuando SOZINHA sobre as listras, que le pior que o bug que este arquivo veio consertar.
///
/// E O QUE O BYOND DEIXAVA DE FORA: as barras de vida/Ki do original nao sao do mob, sao
/// `/obj/screen` do HUD do cliente (`Sense3.0.dm:130`, `Sense3.0.dm:17-18`) -- `flick()` nunca as
/// tocou, porque `flick()` so mexe no `icon` do mob. O nome tambem nao: no BYOND ele aparece no
/// hover e no painel, nunca como pixel do mob. Aqui o equivalente sao os nodes que declaram
/// <see cref="INaoSomeComOCorpo"/> (hoje o balao de fala e a marca de alvo): rotulo nao e corpo, e
/// o corpo sumir nao pode levar a etiqueta junto.
/// =================================================================================================
/// </summary>
public partial class EsquivaZanzoken : Node2D
{
	/// <summary>
	/// O nome do node no corpo. E por ele que a segunda esquiva acha a primeira -- ver
	/// <see cref="Trocar"/>.
	/// </summary>
	public const string NomeDoNode = "EsquivaZanzoken";

	/// <summary>A arte. Tres quadros de 32x32 num atlas de 64x64 (`Zanzoken.tres`).</summary>
	private const string Arte = "res://Assets/Sprites/Misc/Zanzoken.tres";

	/// <summary>Quantos quadros tem o `Zanzoken.dmi`: `frames = 3`.</summary>
	private const int Quadros = 3;

	/// <summary>
	/// QUANTO DURA A TROCA -- os tres quadros do `flick`, `delay = 1,1,1`.
	///
	/// Um tique do DM e um DECISSEGUNDO (ver <see cref="Jandirus.Core.TempoDoDm"/>): 0,10 s por
	/// quadro, 0,30 s no total. Dividir por `world.fps` daria 0,25 s -- 20% curto, que e o erro que
	/// este projeto ja cometeu em 25 cinematicas.
	/// </summary>
	private const double Duracao = Quadros / Jandirus.Core.TempoDoDm.TiquesPorSegundo;

	/// <summary>Quanto ainda falta pro corpo voltar. So <see cref="Trocar"/> escreve pra cima.</summary>
	private double _resta = Duracao;

	private Sprite2D _listras = null!;
	private SpriteFrames _folha = null!;
	private string _animacao = "";

	/// <summary>
	/// O QUE ESTE NODE ESCONDEU, e o que cada um era ANTES.
	///
	/// Guardar o estado anterior em vez de "mostrar tudo no fim" nao e zelo: metade destes nodes
	/// esta legitimamente invisivel na hora da esquiva (a aura so acende com Ki, a nuvem da forma
	/// so existe em Ultra Instinto, a sombra so aparece voando). Restaurar `true` cravado acenderia
	/// a aura de quem nao esta carregando -- o efeito devolveria mais corpo do que tirou.
	/// </summary>
	private readonly List<(CanvasItem No, bool Estava)> _escondidos = [];

	/// <summary>
	/// TROCA O CORPO PELAS LISTRAS. E o `flick('Zanzoken.dmi', M)` -- <paramref name="corpo"/> e o
	/// `M`, quem DESVIOU.
	///
	/// ============================ POR QUE ELA NAO CRIA UM SEGUNDO NODE ============================
	/// Chamada de novo com o efeito ja rodando, ela nao empilha: acha o dono unico deste corpo e
	/// empurra o fim dele pra frente. E o que o `flick()` do BYOND faz -- um `flick` novo substitui
	/// o que estava tocando, ele nao vira dois icones. E e a unica forma que nao tem como devolver o
	/// corpo cedo: com dois donos, o primeiro a vencer o cronometro mostraria o corpo por baixo do
	/// segundo, que continuaria desenhando listras sobre um personagem visivel -- o bug de hoje, de
	/// volta pela porta dos fundos.
	/// ==============================================================================================
	/// </summary>
	public static void Trocar(Node2D? corpo)
	{
		if (corpo == null || !GodotObject.IsInstanceValid(corpo)) return;

		// O DONO UNICO. `_resta` so sobe: o fim avanca, nunca recua.
		if (corpo.GetNodeOrNull<EsquivaZanzoken>(NomeDoNode) is { } jaRodando)
		{
			jaRodando._resta = Duracao;
			return;
		}

		var folha = ResourceLoader.Load<SpriteFrames>(Arte);
		if (folha == null) return;
		string[] nomes = folha.GetAnimationNames();
		// SEM ARTE, SEM TROCA. Esconder o corpo e nao desenhar nada no lugar seria a queixa do dono
		// levada ao extremo: o personagem sumiria por 0,30 s sem explicacao nenhuma na tela.
		if (nomes.Length == 0 || folha.GetFrameCount(nomes[0]) == 0) return;

		var no = new EsquivaZanzoken
		{
			Name = NomeDoNode,
			_folha = folha,
			_animacao = nomes[0],
			// ONDE O CORPO E DESENHADO, e nao onde o node esta: voando, o desenho sobe ate 160 px
			// acima da origem (`SubirComOVoo`), e o `Position` do visual E esse deslocamento. Daqui
			// pra frente quem mantem este valor em dia e o proprio `SubirComOVoo`, que varre os
			// filhos do corpo e nao precisa saber que este node existe -- escrever a posicao tambem
			// no `_Process` daria DOIS donos pro mesmo campo.
			Position = corpo.GetNodeOrNull<CharacterVisual>("Visual")?.Position ?? Vector2.Zero,
		};

		no._listras = new Sprite2D
		{
			Texture = folha.GetFrameTexture(nomes[0], 0),
			// SEM `Modulate` E SEM MATERIAL. A arte medida ja e preta chapada -- ver o cabecalho.
			// Qualquer coisa escrita aqui e a tinta que o dono mandou tirar.
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
		};
		no.AddChild(no._listras);

		corpo.AddChild(no);
	}

	// =====================================================================
	// ESCONDER E MOSTRAR -- O MESMO PAR, SEMPRE
	// =====================================================================
	/// <summary>
	/// ESCONDE O CORPO INTEIRO. Entrar na arvore E o comeco do efeito; nao ha outra porta.
	/// </summary>
	public override void _EnterTree()
	{
		if (GetParent() is not Node2D corpo) return;

		foreach (Node filho in corpo.GetChildren())
		{
			if (ReferenceEquals(filho, this)) continue;          // eu sou o que aparece no lugar
			if (filho is INaoSomeComOCorpo) continue;            // rotulo nao e corpo -- ver a interface
			// A CAMERA NAO DESENHA NADA, e mexer no `Visible` dela e mexer num campo que nao e meu:
			// ela e o olho do jogador local, e este efeito nao tem negocio nenhum com o enquadramento.
			if (filho is Camera2D) continue;
			if (filho is not CanvasItem ci) continue;

			_escondidos.Add((ci, ci.Visible));
			ci.Visible = false;
		}
	}

	/// <summary>
	/// DEVOLVE O CORPO. Sair da arvore E o fim do efeito, e todo fim passa por aqui: o cronometro
	/// (`QueueFree`), o corpo sendo liberado por morte/nocaute/troca de zona/logout, a cena inteira
	/// caindo. Nao ha caminho que esconda sem passar pelo <see cref="_EnterTree"/> nem que termine
	/// sem passar por aqui -- e e so isso que impede a invisibilidade de vazar.
	/// </summary>
	public override void _ExitTree()
	{
		foreach ((CanvasItem no, bool estava) in _escondidos)
		{
			// O CORPO PODE ESTAR SENDO LIBERADO AGORA. Quando o pai cai, os filhos caem com ele e a
			// ordem nao e garantida: um irmao ja liberado responderia a `Visible` como objeto morto.
			if (GodotObject.IsInstanceValid(no)) no.Visible = estava;
		}
		_escondidos.Clear();
	}

	// =====================================================================
	// OS TRES QUADROS
	// =====================================================================
	public override void _Process(double delta)
	{
		_resta -= delta;
		if (_resta <= 0)
		{
			// ============================ SAI DA ARVORE AGORA, NAO NO FIM DO QUADRO ============================
			// `QueueFree()` sozinho deixaria este node PENDURADO no corpo, com o nome ocupado, ate o
			// fim do quadro -- e uma esquiva nova no mesmo quadro (que e o caso comum: varias por
			// segundo, sobrepostas) cairia num de dois buracos, os dois ruins:
			//
			//   * achando este aqui, ela so empurraria o `_resta` de um node que vai morrer de todo
			//     jeito -- a esquiva nova nao desenharia nada;
			//   * nao achando (se o nome fosse liberado), nasceria um SEGUNDO node que leria os irmaos
			//     JA ESCONDIDOS por este e gravaria `Estava = false`. Este aqui devolveria o corpo no
			//     fim do quadro, o segundo o esconderia de novo e, ao terminar, "restauraria" o falso
			//     -- e o corpo ficaria invisivel PRA SEMPRE. E exatamente o vazamento que este arquivo
			//     inteiro existe pra impedir.
			//
			// `RemoveChild` dispara o `_ExitTree` na hora: o corpo volta AQUI, o nome fica livre AQUI, e
			// a esquiva seguinte encontra um corpo visivel e um lugar vazio, como se nada tivesse
			// acontecido. O `QueueFree` depois so recolhe a memoria.
			// ==================================================================================================
			GetParent()?.RemoveChild(this);
			QueueFree();
			return;
		}

		// O QUADRO SAI DO QUE FALTA, e nao de um contador proprio. Assim uma esquiva nova (que so
		// devolve `_resta` ao cheio) reinicia a animacao pelo mesmo caminho -- que e o que o `flick`
		// por cima de outro `flick` faz no BYOND, sem uma linha a mais aqui.
		int falta = Mathf.Clamp((int)(_resta / (Duracao / Quadros)), 0, Quadros - 1);
		if (GodotObject.IsInstanceValid(_listras))
			_listras.Texture = _folha.GetFrameTexture(_animacao, Quadros - 1 - falta);

		// E O ESCONDIDO SE REAFIRMA TODO QUADRO. Nem todo irmao deixa o `Visible` quieto: a
		// `SombraDeVoo` reescreve o proprio a cada mudanca de altitude, e uma esquiva no ar cai
		// justamente no meio dessas mudancas. Sem esta linha a sombra reacenderia sozinha no meio
		// da troca e ficaria uma mancha no chao sem ninguem em cima dela.
		foreach ((CanvasItem no, bool _) in _escondidos)
			if (GodotObject.IsInstanceValid(no)) no.Visible = false;
	}
}

/// <summary>
/// "EU NAO SOU O CORPO" -- a UNICA forma de continuar na tela enquanto o corpo esta trocado pelo
/// Zanzoken (ver <see cref="EsquivaZanzoken"/>).
///
/// Irma do <see cref="IFicaNoChao"/>, e pela mesma razao: a regra e invertida de proposito. Todo
/// filho visual do corpo SOME, porque a lista escrita a mao ja esqueceu alguem quatro vezes na
/// regra da subida, e aqui o esquecimento seria pior -- uma aura ou um rabo boiando sozinho sobre
/// as listras le como defeito grave, nao como efeito.
///
/// O TESTE PRA UM NODE NOVO: "se o personagem ficasse invisivel, eu ainda faria sentido na tela?".
/// Aura, rabo, roupa, cabelo, contorno, nuvem de forma, raios, sombra: nao -- eles SAO o
/// personagem, e nao declaram nada. Balao de fala, marca de alvo e (quando existir) barra de vida
/// ou nome sobre a cabeca: sim -- sao rotulo sobre o dono, nao pixel do dono. No original esses
/// rotulos sequer eram do mob (as barras sao `/obj/screen` do HUD, `Sense3.0.dm:130`), e por isso o
/// `flick()` nunca os tocou.
/// </summary>
public interface INaoSomeComOCorpo { }
