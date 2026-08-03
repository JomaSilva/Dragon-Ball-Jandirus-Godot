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

	private const string Codigo = """
		shader_type canvas_item;

		// 0 = inteiro, 1 = sumiu. E o relogio da miragem.
		uniform float desfeito : hint_range(0.0, 1.0) = 0.0;
		uniform vec4  cor_da_borda : source_color = vec4(0.55, 0.85, 1.0, 1.0);

		// A CAIXA DO QUADRO dentro do atlas. Sem ela a ondulacao abaixo amostra FORA do quadro e
		// puxa a pose vizinha da folha -- foi o defeito que o dono viu como "uma leve bugadinha
		// virado pra direita e pra baixo". Qual pose vaza depende de quem esta ao lado na folha, e
		// isso muda com a direcao; por isso funcionava pra esquerda e pra cima.
		uniform vec2 quadro_min = vec2(0.0);
		uniform vec2 quadro_max = vec2(1.0);

		// RUIDO BARATO E SEM TEXTURA. Nao vale a pena carregar um NoiseTexture2D pra um efeito de
		// meio segundo: esta soma de senos ja da um padrao irregular o bastante pro olho ler como
		// "desfazendo em pedacos" em vez de "cortando em listras".
		float granulado(vec2 p) {
			return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
		}

		void fragment() {
			// ONDULACAO: cresce junto com o desfazimento -- comeca parado (e o corpo que acabou de
			// sair) e treme mais conforme vira ar.
			vec2 uv = UV;
			uv.x += sin(uv.y * 40.0 + TIME * 18.0) * 0.006 * desfeito;
			uv = clamp(uv, quadro_min, quadro_max);

			vec4 c = texture(TEXTURE, uv);

			// DE BAIXO PRA CIMA: o `UV.y` entra no limiar, entao os pes se desfazem primeiro e a
			// cabeca por ultimo. Some tudo junto le como fade; em ordem le como dissipar.
			float g = granulado(floor(UV * 48.0));
			float limiar = desfeito * 1.35 - UV.y * 0.28;

			if (g < limiar - 0.16) { COLOR = vec4(0.0); }
			else {
				// A BORDA DA DISSOLUCAO ACENDE. E o unico lugar em que o vulto tem cor propria, e e
				// o que faz ele parecer feito de ki e nao um fantasma cinza.
				float fio = 1.0 - smoothstep(0.0, 0.16, g - (limiar - 0.16));
				c.rgb = mix(c.rgb, cor_da_borda.rgb, fio * 0.85);
				c.a  *= (1.0 - desfeito * 0.85);
				COLOR = c;
			}
		}
		""";

	private static Shader? _shader;
	private static Shader Sh => _shader ??= new Shader { Code = Codigo };

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
			GlobalPosition = onde ?? corpo.GlobalPosition,
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

		// O SHADER VAI EM CADA CAMADA da foto (corpo, roupa, cabelo, rabo). A `Fotografar` ja poe um
		// material com a TINTA de cada uma; aqui ele e trocado pelo da miragem -- a tinta do cabelo
		// de Super Saiyajin se perde, e vale a pena: o que importa no vulto e a silhueta se desfazendo.
		foreach (Node n in foto.GetChildren())
		{
			if (n is not Sprite2D s) continue;
			var m = new ShaderMaterial { Shader = Sh };
			(Vector2 min, Vector2 max) = BorraoDirecional.Caixa(s.Texture);
			m.SetShaderParameter("quadro_min", min);
			m.SetShaderParameter("quadro_max", max);
			s.Material = m;
			v._mats.Add(m);
		}

		palco.AddChild(v);
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
