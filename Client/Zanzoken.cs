using Godot;

namespace Jandirus.Client;

/// <summary>Os dois jeitos de o vulto sumir. Ver <see cref="Zanzoken.Deixar"/>.</summary>
public enum EstiloDeVulto
{
	/// <summary>
	/// MIRAGEM: dissolve em pedacos, com ondulacao e borda acesa. E o do TELEPORTE e da investida
	/// -- gestos em que o corpo atravessou espaco e a vista ficou pra tras.
	/// </summary>
	Miragem,

	/// <summary>
	/// VULTO SIMPLES: so esmaece e encolhe. Foi o primeiro efeito que existiu aqui, e o dono
	/// pediu pra guarda-lo: "o efeito do zanzoken antigo poderia deixar so pro dodge de golpes
	/// corpo a corpo".
	///
	/// Faz sentido que sejam diferentes: esquivar nao e atravessar distancia, e um desvio no
	/// lugar. A dissolucao com borda acesa e cara demais de atencao pra uma coisa que acontece
	/// varias vezes por troca de golpes -- ela chamaria mais atencao que o proprio soco.
	/// </summary>
	Simples,
}

/// <summary>
/// A IMAGEM REMANESCENTE do Zanzoken -- o vulto que fica pra tras quando alguem se move rapido
/// demais pra vista acompanhar.
///
/// ============================ DE ONDE ISTO VEM ============================
/// O original larga uma `image()` do corpo no TURF e a tira depois de 10 ticks
/// (`Buff Effects.dm:41-46`). E uma copia congelada do sprite, na pose e na direcao do instante --
/// nao um efeito de particula. Aqui e o mesmo: <see cref="CharacterVisual.Fotografar"/> tira a
/// foto da pilha inteira e este node so a desfaz.
///
/// O GATE E A SKILL `Afterimage Technique` (`misc.dm:35`), e quem decide e o SERVIDOR -- ele so
/// marca o `HitEvent.Zanzo` quando a investida REALMENTE deslocou o corpo. Sem deslocamento nao ha
/// vulto: nao houve nada que a vista tenha perdido.
/// =========================================================================
///
/// ============================ SUMIR COMO MIRAGEM, E NAO COMO SPRITE APAGANDO ============================
/// A primeira versao so baixava o alfa e encolhia. O dono viu o que faltava: "o personagem tinha q
/// sumir como uma miragem ficando transparente e sumindo (pode usar shaders pra isso)".
///
/// Alfa uniforme le como "objeto sendo apagado" -- e um defeito de render, nao um acontecimento.
/// Miragem e OUTRA coisa: ela se DESFAZ, em pedacos, de baixo pra cima, com a silhueta tremendo
/// como ar quente. O shader faz as tres coisas:
///
///   1. DISSOLUCAO POR RUIDO -- cada pedaco do corpo some na sua vez, nao todos juntos;
///   2. ONDULACAO -- a UV oscila, entao o que ainda nao sumiu treme como reflexo no asfalto;
///   3. BORDA ACESA -- o limiar da dissolucao acende em ciano no fio, que e o que da o "ki" e
///      impede o vulto de virar uma mancha cinza sem forma.
/// ======================================================================================================
/// </summary>
public partial class Zanzoken : Node2D
{
	/// <summary>Quanto dura o vulto. O DM usa `spawn(10)` = 1 s; 0,55 s le melhor num jogo mais rapido.</summary>
	private const double Duracao = 0.55;


	/// <summary>
	/// O CODIGO DESTE EFEITO mora num `.gdshader` de verdade -- ver o comentario de
	/// <see cref="CharacterVisual"/>: efeito procedural nao se acerta lendo codigo, se acerta
	/// arrastando o valor e OLHANDO, e pra isso ele precisa abrir no editor do Godot.
	///
	/// PUBLICO porque quem o traz pra memoria e o `Aquecimento`, no lobby, numa thread de carga -- e porque a bancada
	/// pergunta por ele ao cache do Godot (a `--diagestouro --pecas`: o arquivo ja estava na memoria ANTES da primeira
	/// miragem do processo?). Ver <see cref="Ensaiar"/>.
	/// </summary>
	public const string CaminhoDoShader = "res://Assets/Shaders/Zanzoken.gdshader";

	private static Shader? _shader;

	/// <summary>
	/// PREGUICOSO, E QUASE NUNCA O PRIMEIRO A PEDIR: o arquivo ja veio do lobby, e este `Load` so o acha no cache. Quem
	/// ainda le o disco aqui e o processo que nao aquece (`--semaquecimento`).
	/// </summary>
	public static Shader Sh => _shader ??= ResourceLoader.Load<Shader>(CaminhoDoShader);

	/// <summary>
	/// Quantas MIRAGENS o JOGO ja deixou neste processo -- o estilo <see cref="EstiloDeVulto.Miragem"/>; o vulto simples
	/// da esquiva nao leva shader, e nao conta, e a do ensaio do lobby (<see cref="Ensaiar"/>) tambem nao, que nao e de
	/// ninguem. SO PRA BANCADA: e como a `--diagestouro` acha o quadro em que uma nasce quando quem a pede e um pacote do
	/// servidor, e nao ela.
	/// </summary>
	public static int MiragensDeTeste { get; private set; }

	private double _resta = Duracao;
	private bool _simples;
	private readonly List<ShaderMaterial> _mats = [];

	/// <summary>
	/// Deixa um vulto em <paramref name="onde"/> -- a posicao de ONDE o corpo saiu, e nao onde ele
	/// esta agora.
	///
	/// A posicao vem de fora de proposito: o relato do servidor chega um RTT depois do golpe, e ate
	/// la o corpo ja investiu. Fotografar "onde ele esta" poria a miragem em cima do alvo.
	/// </summary>
	public static void Deixar(Node palco, Node2D corpo, Vector2? onde = null,
							  EstiloDeVulto estilo = EstiloDeVulto.Miragem)
	{
		if (corpo.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis) return;

		var v = new Zanzoken
		{
			Name = "Zanzoken",
			// O `+ vis.Position` E A ALTURA: voando, o corpo e desenhado acima do no, e a miragem
			// tem que nascer onde o corpo SE VE -- senao o vulto do dash fica no chao enquanto o
			// personagem risca o ceu. `vis.Position` e exatamente o deslocamento de altitude (e
			// zero no chao), entao no chao esta linha nao muda nada.
			GlobalPosition = (onde ?? corpo.GlobalPosition) + vis.Position,
			Modulate = new Color(0.78f, 0.90f, 1f, 0.62f),   // frio: e a marca do ki, nao um clone
			ZIndex = corpo.ZIndex,
			YSortEnabled = false,
		};

		Node2D foto = vis.Fotografar();
		v.AddChild(foto);

		// O SIMPLES NAO LEVA SHADER. Ele desaparece pelo `Modulate` do proprio node -- o mesmo
		// caminho da primeira versao, e e justamente por ser discreto que ele serve pra esquiva.
		if (estilo == EstiloDeVulto.Simples)
		{
			v._simples = true;
			palco.AddChild(v);
			return;
		}

		// ============================ A PILHA SE DESFAZ COMO UM CORPO SO ============================
		// O personagem sao quatro camadas (corpo, roupa, cabelo, rabo), cada uma com a propria
		// folha. Se cada uma dissolve no proprio espaco, elas somem em ordens diferentes -- e o
		// que se via era o vulto ficando CARECA por alguns quadros, virado pra direita e pra
		// baixo (nas outras duas direcoes os quadros calhavam de cair em linhas parecidas da
		// folha, e o defeito nao aparecia).
		//
		// A caixa da UNIAO e o corpo inteiro. Cada camada recebe essa mesma caixa expressa no
		// proprio espaco local -- por isso o `- s.Position`. Com isso o `p` do shader e o mesmo
		// ponto do corpo em todas elas, e a dissolucao passa a ser uma so.
		// ============================================================================================
		Rect2 corpo2 = CaixaDoCorpo(foto);
		var tamanho = new Vector2(Mathf.Max(corpo2.Size.X, 1), Mathf.Max(corpo2.Size.Y, 1));

		// O SHADER VAI EM CADA CAMADA da foto. A `Fotografar` ja poe um material com a TINTA de
		// cada uma; aqui ele e trocado pelo da miragem -- a tinta do cabelo de Super Saiyajin se
		// perde, e vale a pena: o que importa no vulto e a silhueta se desfazendo.
		foreach (Node n in foto.GetChildren())
		{
			if (n is not Sprite2D s) continue;
			var m = new ShaderMaterial { Shader = Sh };
			(Vector2 min, Vector2 max) = BorraoDirecional.Caixa(s.Texture);
			m.SetShaderParameter("quadro_min", min);
			m.SetShaderParameter("quadro_max", max);
			m.SetShaderParameter("corpo_origem", corpo2.Position - s.Position);
			m.SetShaderParameter("corpo_tamanho", tamanho);
			s.Material = m;
			v._mats.Add(m);
		}

		MiragensDeTeste++;
		palco.AddChild(v);
	}

	/// <summary>
	/// O ENSAIO DO LOBBY (ver `Aquecimento.AtosDaMiragem`): uma miragem de verdade -- a mesma <see cref="Deixar"/> que o
	/// `World.AoPiscar` chama -- de um boneco que nao e de ninguem, num palco fora da tela, pra o shader ser compilado e
	/// as pipelines dele montadas ALI, e nao no quadro do primeiro Zanzoken do processo.
	///
	/// ============================ O QUE ISTO TIRA DO MEIO DA LUTA ============================
	/// O shader deste efeito era carregado na hora do primeiro uso (o <see cref="Sh"/> e preguicoso, e ninguem o trazia
	/// antes), e o primeiro uso e o primeiro Zanzoken de uma luta, ou o primeiro arranque de quem tem a Afterimage.
	/// MEDIDO em 2026-10-09 pela `--diagestouro --pecas`, antes do conserto -- a miragem sozinha num quadro, de um corpo
	/// de tres camadas --, com o cache de shader do Godot vazio (o de quem abre o jogo pela primeira vez, e o de toda
	/// bancada) e o do driver de video quente:
	///
	///     o quadro da primeira miragem do processo ....... 32,8 a 34,8 ms de trabalho   (o de uma miragem qualquer: 2,3 a 3,9)
	///       -- esperando o shader compilar ............... 24 a 27 ms     a thread principal parada no PREPARO do desenho
	///       -- a chamada inteira, na primeira vez ........ 3,2 a 3,3 ms   a foto, um material por camada, e o codigo
	///                                                                     dela rodando pela primeira vez
	///       -- o arquivo, lido do disco .................. 0,8 ms
	///       -- a pipeline, no primeiro desenho ........... 1,1 a 1,4 ms   com o driver de video quente
	///
	/// (Com o cache de shader do Godot CHEIO -- o de quem ja jogou uma vez -- a espera some sozinha: o defeito eram 7,3 ms
	/// de trabalho, num quadro de 8,8 de relogio. Com o driver FRIO, o de quem acabou de instalar, 56 a 57 ms de relogio:
	/// os 25 da espera e mais 24 a 25 da pipeline montada do zero. E COM UMA LUZ EM CIMA DA MIRAGEM -- de noite, ao lado
	/// de uma aura, de uma fogueira, de um tiro de ki -- a pipeline e a gemea do sprite iluminado, que custa ao driver
	/// quase o triplo: 68 a 75 ms, num quadro de 101 a 108.)
	///
	/// DEPOIS: 3,2 a 4,4 ms de trabalho, numa volta do monitor, em qualquer um desses casos; nenhuma pipeline nasce no
	/// quadro, e a thread principal nao le o arquivo. A conta inteira, o preco no lobby e a regua estao no cabecalho do
	/// `RoboDoPrimeiroEstouro`, no bloco "E A PRIMEIRA MIRAGEM".
	///
	/// A ESPERA E DO MATERIAL, E NAO DO DESENHO, como a do borrao (`--pecas --miragempartida`, que nasce cada peca sozinha
	/// num quadro): a miragem inteira montada pela <see cref="Deixar"/> num palco FORA da arvore, sem nada desenhado, parou
	/// o quadro 26 ms no preparo; o primeiro desenho, um segundo depois, custou 1 ms (25 com o driver frio) e trouxe so a
	/// pipeline.
	///
	/// E O ATO PAGA TAMBEM O CODIGO DE PRIMEIRA VEZ, que o do borrao nao paga: por ser a chamada do jogo, e nao um sprite
	/// solto, a primeira miragem de verdade custa de script o que custa uma qualquer (2,7 a 3,9 ms contra 1,8 a 3,3).
	/// ========================================================================================
	///
	/// O CORPO E DE MENTIRA E NUNCA E DESENHADO: um node escondido com o boneco do ensaio dentro, sob o nome que a
	/// <see cref="Deixar"/> procura (`Visual`). Escondido porque quem tem que aparecer no palco e a FOTO, e nao ele: o
	/// boneco desenhado montaria aqui as pipelines do `Personagem.gdshader`, que sao do ato do corpo
	/// (`Aquecimento.AtosDoCorpo`) -- e a rodada de injecao daquele ato deixaria de reprovar. A foto sai inteira mesmo
	/// assim: a <see cref="CharacterVisual.Fotografar"/> olha se a CAMADA esta visivel, e nao o pai dela. Tirada a foto,
	/// o corpo sai do palco.
	///
	/// COM A FOLHA DE MENTIRA DO BONECO (um quadro branco so, feito na memoria): a pipeline e do shader e do jeito de
	/// desenhar, e nao da textura -- e nenhuma folha de aparencia pode ser pedida daqui, que quando o ensaio comeca elas
	/// ainda estao na thread de carga do `Aquecimento`.
	/// </summary>
	public static void Ensaiar(Node2D pai, Vector2 onde)
	{
		var corpo = new Node2D { Name = "CorpoDaMiragemDoEnsaio", Position = onde, Visible = false };
		pai.AddChild(corpo);
		CharacterVisual.PorOBonecoDoEnsaio(corpo, Vector2.Zero, "Visual");
		// (a conta da bancada e das miragens do JOGO: a do ensaio nao entra nela)
		int doJogo = MiragensDeTeste;
		Deixar(pai, corpo);
		MiragensDeTeste = doJogo;
		corpo.QueueFree();
	}

	/// <summary>
	/// A CAIXA DO CORPO INTEIRO numa foto de <see cref="CharacterVisual.Fotografar"/>, em
	/// coordenada da propria foto (a uniao das camadas).
	///
	/// E o espaco comum em que a dissolucao roda. Publico porque a bancada confere, direcao por
	/// direcao, que o cabelo cai na parte de CIMA dele -- que e o que garante que ele seja a
	/// ultima coisa a sumir, e portanto que o vulto nunca fique careca.
	/// </summary>
	public static Rect2 CaixaDoCorpo(Node2D foto)
	{
		Rect2? uniao = null;
		foreach (Node n in foto.GetChildren())
		{
			if (n is not Sprite2D s) continue;
			Rect2 r = s.GetRect();
			r.Position += s.Position;
			uniao = uniao?.Merge(r) ?? r;
		}
		return uniao ?? new Rect2(Vector2.Zero, new Vector2(32, 32));
	}

	public override void _Process(double delta)
	{
		_resta -= delta;
		if (_resta <= 0) { QueueFree(); return; }

		var t = (float)(1.0 - _resta / Duracao);   // 0 -> 1 ao longo da vida

		if (_simples)
		{
			float f = 1f - t;
			Modulate = new Color(Modulate.R, Modulate.G, Modulate.B, 0.55f * f * f);
			Scale = new Vector2(0.92f + 0.08f * f, 0.92f + 0.08f * f);
			return;
		}

		// AO QUADRADO: fica quase inteiro no comeco e se desfaz rapido no fim. Linear deixa um
		// borrao pendurado meio segundo, que e justamente quando ele ja atrapalha a leitura da luta.
		foreach (ShaderMaterial m in _mats) m.SetShaderParameter("desfeito", t * t);

		// e sobe um pouco enquanto se desfaz -- ar quente subindo
		Position = new Vector2(Position.X, Position.Y - (float)(delta * 7));
	}
}
