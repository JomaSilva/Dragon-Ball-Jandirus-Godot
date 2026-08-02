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
	/// O `ICON_ADD` do BYOND em GLSL: soma a cor depois da textura, preservando o alfa.
	/// Compartilhado por todas as camadas de todos os personagens -- cada uma tem so o seu
	/// material, com o seu valor de `tinta`.
	/// </summary>
	private const string CodigoTinta = """
		shader_type canvas_item;
		uniform vec3 tinta = vec3(0.0);
		void fragment() {
			COLOR.rgb = clamp(COLOR.rgb + tinta, 0.0, 1.0);
		}
		""";

	private static Shader? _shaderTinta;
	private static Shader ShaderTinta => _shaderTinta ??= new Shader { Code = CodigoTinta };

	private readonly List<AnimatedSprite2D> _camadas = [];
	private AnimatedSprite2D? _corpo;
	private AnimatedSprite2D? _cabelo, _olhos;
	private readonly List<AnimatedSprite2D> _roupa = [];

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

	private AnimatedSprite2D NovaCamada(int ordem)
	{
		var s = new AnimatedSprite2D
		{
			Centered = true,
			ZIndex = ordem,
			Material = new ShaderMaterial { Shader = ShaderTinta },
		};
		AddChild(s);
		_camadas.Add(s);
		return s;
	}

	/// <summary>Cor SOMADA a esta camada. Nulo = a cor natural do sprite.</summary>
	private static void Tingir(AnimatedSprite2D s, Rgb? cor)
	{
		if (s.Material is not ShaderMaterial m) return;
		Vector3 t = cor is { } c ? new Vector3(c.R / 255f, c.G / 255f, c.B / 255f) : Vector3.Zero;
		m.SetShaderParameter("tinta", t);
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

		// --- corpo ---
		(Rgb? soma, float brilho) = cat.TintaDoCorpo(ap, raca);
		Trocar(_corpo!, cat.CorpoSprite(ap, raca, genero));
		_corpo!.SelfModulate = new Color(brilho, brilho, brilho);   // so o tom do Namekuseijin
		Tingir(_corpo, soma);

		// --- roupa: uma camada por peca, na ordem em que foi vestida ---
		while (_roupa.Count > ap.Roupa.Count)
		{
			AnimatedSprite2D velha = _roupa[^1];
			_roupa.RemoveAt(_roupa.Count - 1);
			_camadas.Remove(velha);
			velha.QueueFree();
		}
		for (int i = 0; i < ap.Roupa.Count; i++)
		{
			if (i >= _roupa.Count) _roupa.Add(NovaCamada(1 + i));
			Trocar(_roupa[i], ap.Roupa[i]);
		}

		// --- cabelo: acima da roupa, como no jogo (plano 4 contra 3) ---
		string? cabelo = VisualCatalog.TemCabelo(raca) ? cat.SpriteDoCabelo(ap.Cabelo) : null;
		if (cabelo == null)
		{
			if (_cabelo != null) { _camadas.Remove(_cabelo); _cabelo.QueueFree(); _cabelo = null; }
		}
		else
		{
			_cabelo ??= NovaCamada(10);
			Trocar(_cabelo, cabelo);
			Tingir(_cabelo, VisualCatalog.CabeloNatural(raca) ? null : ap.CorCabelo);
		}

		// --- olhos: um sprite so -- o jogo tem exatamente um arquivo ---
		if (cat.Olhos == null)
		{
			if (_olhos != null) { _camadas.Remove(_olhos); _olhos.QueueFree(); _olhos = null; }
		}
		else
		{
			_olhos ??= NovaCamada(11);
			Trocar(_olhos, cat.Olhos);
			Tingir(_olhos, ap.CorOlho);
		}

		Aplicar(force: true);
	}

	private static void Trocar(AnimatedSprite2D alvo, string caminho)
	{
		if (alvo.GetMeta("src", "").AsString() == caminho) return;   // ja e esse: nao reinicia a animacao
		var f = ResourceLoader.Load<SpriteFrames>(caminho);
		if (f == null) { GD.PushWarning($"[visual] sprite ausente: {caminho}"); return; }
		alvo.SpriteFrames = f;
		alvo.SetMeta("src", caminho);
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

	public void SetState(string state)
	{
		if (_state == state) return;
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
		_state = state;
		_ritmo = 1;
		Aplicar(force: true);   // ja zera o relogio: o golpe recomeca do primeiro quadro
		if (duracaoAlvo <= 0 || _corpo?.SpriteFrames is not { } f) return;

		// ENCAIXA A ANIMACAO NO TEMPO DO GOLPE. O .dmi traz o soco na cadencia do BYOND
		// (~0,67 s); com a cadencia nova de ~0,33 s a animacao nao terminaria antes do
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
		if (_facing == facing && _moving == moving) return;
		_facing = facing;
		_moving = moving;
		Aplicar(force: false);
	}

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
	// A piscada de dano vai no `Modulate` da RAIZ, nao no de cada camada: o tom de pele
	// mora no `SelfModulate` do corpo e a cor de cabelo/roupa mora no shader de tinta.
	// Mexer neles pra piscar apagaria a aparencia do personagem -- e ela nao volta sozinha.
	private double _flash;
	private Color _flashCor = Colors.White;

	/// <summary>Pisca o boneco inteiro. Vermelho = levou dano, branco = aparou.</summary>
	public void Impacto(Color cor, double segundos = 0.15)
	{
		_flashCor = cor;
		_flash = segundos;
	}

	public override void _Process(double delta)
	{
		if (_flash > 0)
		{
			_flash -= delta;
			Modulate = _flash > 0 ? _flashCor : Colors.White;
		}

		if (_corpo?.SpriteFrames == null) return;

		// TUDO anima, inclusive parado: a pose parada e um estado proprio com ciclo proprio
		// (a "respiracao"). Quem NAO anima e a camada que caiu numa pose emprestada.
		SpriteFrames? corpoF = _corpo.SpriteFrames;
		double ciclo = corpoF == null ? 0 : Ciclo(corpoF, _corpo.Animation);
		_relogio = ciclo > 0 ? (_relogio + delta * _ritmo) % ciclo : 0;

		double fase = ciclo > 0 ? _relogio / ciclo : 0;   // 0..1 dentro do ciclo do corpo

		foreach (AnimatedSprite2D s in _camadas)
		{
			SpriteFrames? f = s.SpriteFrames;
			if (f == null || !s.Visible || !f.HasAnimation(s.Animation)) continue;
			if (!s.GetMeta("sync", true).AsBool()) { if (s.Frame != 0) s.Frame = 0; continue; }

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
	private string? Escolher(AnimatedSprite2D sprite, string? doCorpo)
	{
		SpriteFrames? f = sprite.SpriteFrames;
		if (f == null) return null;

		string dir = MoveRules.FacingSuffix(_facing);
		string fam = Familia();

		if (doCorpo != null && f.HasAnimation(doCorpo)) return doCorpo;
		if (f.HasAnimation($"{fam}_{dir}")) return $"{fam}_{dir}";
		if (f.HasAnimation(fam)) return fam;

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
		sprite.Visible = true;

		// "sincronizada" = esta na MESMA pose do corpo. Quem esta numa substituta fica
		// congelada no primeiro quadro em vez de acompanhar um ciclo que nao e o dela.
		bool naPose = nome.StartsWith(Familia(), StringComparison.Ordinal);
		sprite.SetMeta("sync", naPose);

		if (force || sprite.Animation != nome) sprite.Animation = nome;
		sprite.Stop();          // ninguem toca sozinho: o relogio desta classe manda em todos
		sprite.Frame = 0;
	}
}
