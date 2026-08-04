using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A LUA, no canto do HUD -- o disco na fase de hoje, ao lado da mira e do boneco.
///
/// ============================ POR QUE ELA SAIU DO MEIO DA TELA ============================
/// A primeira versão desenhava a lua no céu: um disco que nascia num lado, cruzava a parte de
/// cima da tela e se punha no outro. Era bonito e atrapalhava -- num jogo de câmera de cima, "o
/// céu" é a mesma região da tela em que se joga, então a lua passava por cima do combate.
///
/// O dono pediu ela fixa no canto, junto do resto do que se consulta. É a leitura certa do que
/// ela é: a fase da lua não é cenário, é INFORMAÇÃO -- ela diz quando o Oozaru fica possível, e
/// quem tem rabo precisa saber disso de relance, como olha a vida e o Ki. Cenário se admira;
/// informação fica onde a vista já vai procurar.
/// ==========================================================================================
///
/// SÓ APARECE COM A LUA NO CÉU. De dia (e em Namek, que não anoitece, e no espaço, e dentro de
/// casa) não há o que mostrar, e um mostrador que fica escuro sem explicar nada é pior que
/// nenhum -- ele ocupa espaço prometendo um dado que não existe.
///
/// O DESENHO É O DO JOGO ANTIGO. `Misc/Moonicon.dmi` existe desde sempre: um disco de 128x128 com
/// as crateras já pintadas. As FASES é que não existiam em arte nenhuma -- e não precisam, porque
/// fase é iluminação, não desenho: o mesmo disco com a sombra certa por cima dá as oito.
/// </summary>
public partial class LuaNoCeu : VBoxContainer
{
	private const string Folha = "res://Assets/Sprites/Misc/Moonicon.tres";

	/// <summary>
	/// Lado do disco no HUD, em pixels.
	///
	/// Menor que o boneco ao lado de proposito: a lua e consulta ocasional, o corpo e leitura de
	/// combate. Quem manda na hierarquia de um painel e o tamanho.
	/// </summary>
	private const int Lado = 34;

	/// <summary>
	/// O TERMINADOR -- a fronteira entre a parte acesa e a apagada do disco.
	///
	/// A conta é a projeção de um círculo máximo numa esfera: pra uma fase `f` (0 = nova, 0,5 =
	/// cheia), a linha é a elipse `x = cos(2πf)·√(1-y²)`, e o lado aceso troca na metade do
	/// ciclo. Uma sombra em forma de DISCO deslocado -- que é como quase todo mundo desenha isso
	/// -- só funciona pras fases finas: na gibosa ela teria que estar ATRÁS do disco, e o
	/// crescente sairia com a barriga pro lado errado.
	///
	/// O LADO ESCURO NÃO É PRETO. Ele fica com um resto do próprio disco (o brilho da terra
	/// refletido de volta, que é o que se vê a olho nu numa lua nova), senão o crescente vira uma
	/// foice flutuando sem lua nenhuma em volta.
	/// </summary>
	private const string CodigoDaFase = """
		shader_type canvas_item;

		uniform float fase = 0.5;
		uniform float brilho = 1.0;

		void fragment() {
			vec4 c = texture(TEXTURE, UV);
			vec2 p = UV * 2.0 - 1.0;

			float k = cos(6.28318530718 * fase);
			float meia = sqrt(max(1.0 - p.y * p.y, 0.0));
			float terminador = k * meia;

			// crescendo acende pela direita; minguando, pela esquerda
			float d = fase < 0.5 ? p.x - terminador : -terminador - p.x;
			float aceso = smoothstep(-0.07, 0.07, d);

			vec3 escuro = c.rgb * 0.13;
			COLOR = vec4(mix(escuro, c.rgb * brilho, aceso), c.a);
		}
		""";

	private TextureRect _disco = null!;
	private Label _nome = null!;
	private ShaderMaterial _tinta = null!;

	public override void _Ready()
	{
		AddThemeConstantOverride("separation", 1);
		Alignment = AlignmentMode.Center;

		_tinta = new ShaderMaterial { Shader = new Shader { Code = CodigoDaFase } };
		_disco = new TextureRect
		{
			Name = "Disco",
			Texture = Quadro(),
			Material = _tinta,
			CustomMinimumSize = new Vector2(Lado, Lado),
			StretchMode = TextureRect.StretchModeEnum.Scale,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		AddChild(_disco);

		// A LEGENDA NAO PODE MANDAR NA LARGURA. "quarto minguante" e mais que o dobro do disco, e
		// como o painel agora se dimensiona pelo conteudo (ver `Hud.MontarDireita`), um rotulo
		// solto esticaria o painel inteiro toda vez que a fase trocasse de nome.
		_nome = Tema.Legenda("", Tema.TextoFraco, 10);
		_nome.HorizontalAlignment = HorizontalAlignment.Center;
		_nome.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_nome.CustomMinimumSize = new Vector2(Lado + 18, 0);
		_nome.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		AddChild(_nome);

		Visible = false;
	}

	/// <summary>
	/// MOSTRA A LUA DESTE PLANETA AGORA. Chamado pelo <see cref="Hud"/> a cada quadro -- este node
	/// não tem relógio próprio de propósito: dois relógios pro mesmo céu saem de sincronia sem
	/// ninguém notar.
	/// </summary>
	/// <param name="encoberta">
	/// O quanto o CLIMA está tapando o céu, de 0 a 1. Nuvem carregada apaga a lua -- e num jogo
	/// onde a lua cheia é o gatilho do Oozaru, ver ou não ver a lua é informação de jogo, não
	/// enfeite. Ver `EstadoDoClima.Encobre`.
	/// </param>
	public void Aplicar(EstadoDoCeu ceu, double encoberta)
	{
		if (!ceu.LuaNoCeu) { Visible = false; return; }
		Visible = true;

		_tinta.SetShaderParameter("fase", (ceu.Fase - 1) / (float)Ceu.Fases);

		// A NUVEM ESCURECE O MOSTRADOR TAMBÉM. Se o céu está fechado, a lua está lá mas não se vê
		// -- e o mostrador tem que contar a mesma verdade que a janela, senão ele vira uma
		// promessa que a tela não cumpre.
		var tapada = (float)Mathf.Clamp(encoberta, 0, 1);
		_tinta.SetShaderParameter("brilho", 0.85f + 0.15f * (float)ceu.Aceso);
		_disco.Modulate = new Color(1, 1, 1, 1f - tapada * 0.75f);

		_nome.Text = tapada > 0.75f ? "encoberta" : Ceu.NomeDaFase(ceu.Fase);
		_nome.AddThemeColorOverride("font_color",
			ceu.Cheia && tapada <= 0.75f ? Tema.Destaque : Tema.TextoFraco);
	}

	private static Texture2D? Quadro()
	{
		var frames = ResourceLoader.Load<SpriteFrames>(Folha);
		if (frames != null && frames.HasAnimation("default") && frames.GetFrameCount("default") > 0)
			return frames.GetFrameTexture("default", 0);

		GD.PushWarning("[lua] sem Moonicon.tres -- o mostrador da lua fica vazio");
		return null;
	}
}
