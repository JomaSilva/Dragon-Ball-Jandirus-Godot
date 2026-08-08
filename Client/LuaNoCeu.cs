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
	/// A PARTIR DE QUANTO A LUA CONTA COMO ENCOBERTA.
	///
	/// Era um `0.75f` escrito duas vezes (no texto e na cor da legenda) e agora e tres, porque o botao
	/// de olhar pra lua tambem depende dele. Tres copias de um numero que decide se a lua "existe" pro
	/// jogador seria o jeito garantido de a legenda dizer "encoberta" com o botao aceso embaixo.
	/// </summary>
	private const float Encoberta = 0.75f;

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
	private Button _olhar = null!;
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

		_olhar = MontarBotao();
		AddChild(_olhar);

		Visible = false;
	}

	/// <summary>
	/// O BOTAO VERMELHO: "olhar pra lua".
	///
	/// ============================ POR QUE ELE E UM BOTAO, E VERMELHO ============================
	/// No jogo antigo virar Oozaru era uma PREFERENCIA guardada (o `Osetting`: "se a lua estiver no
	/// ceu, eu olho") -- e o jogador que esquecia dela ligada virava macaco sem querer, no meio de
	/// outra coisa. O dono cortou isso: a lua cheia nao transforma mais sozinha (`GameServer.Ceu.cs`),
	/// e olhar virou um GESTO. Um gesto precisa de um lugar pra acontecer, e o lugar certo e embaixo
	/// da propria lua -- o mostrador ja dizia "e lua cheia", faltava o "e dai?".
	///
	/// VERMELHO porque isto nao e uma acao a mais no menu: perder o controle do proprio corpo por
	/// quatro minutos e a coisa mais irreversivel que um Saiyajin pode apertar antes de DOMINAR a
	/// fera. A cor e o unico aviso que chega antes do clique -- e ela continua valendo depois da
	/// rampa de maestria (`Oozaru.SegundosDeControle`), porque o que a rampa encurta e o tempo
	/// SEM controle, nao a duracao da forma: ate os 100% ainda se termina a noite como passageiro.
	/// ==========================================================================================
	///
	/// O ESTILO E LOCAL e nao entrou no <see cref="Tema"/>: um `Button` vermelho nao e uma classe de
	/// botao do jogo, e um botao so. Promove-lo a tema criaria uma variacao global que ninguem mais
	/// usa -- e a proxima pessoa teria que adivinhar quando usar a "vermelha".
	/// </summary>
	private Button MontarBotao()
	{
		var b = new Button
		{
			Name = "Olhar",
			Text = "olhar pra lua",
			TooltipText = "Encara a lua cheia e vira Oozaru. Sem dominio sobre a fera, "
						+ "voce nao controla o que ela faz ate ela se cansar.",
			// CLIP DESLIGADO de proposito: a legenda ao lado NAO pode mandar na largura do painel
			// (ver `_nome`) porque o nome da fase muda toda noite e o painel piscaria de tamanho; o
			// botao aparece uma vez por noite de lua cheia e fica. Cortar o texto de uma chamada pra
			// acao pra economizar trinta pixels seria trocar a unica coisa que ele tem a dizer.
			ClipText = false,
			FocusMode = FocusModeEnum.None,   // ninguem quer o TAB parando aqui no meio da luta
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
			Visible = false,
		};
		b.AddThemeFontSizeOverride("font_size", 10);
		b.AddThemeStyleboxOverride("normal", Tema.Caixa(new Color("5a1b15"), Tema.Perigo, 6));
		b.AddThemeStyleboxOverride("hover", Tema.Caixa(new Color("8a2a20"), new Color("ff9c8c"), 6));
		b.AddThemeStyleboxOverride("pressed", Tema.Caixa(new Color("3a100c"), Tema.Perigo, 6));
		b.AddThemeColorOverride("font_color", Tema.Texto);
		b.AddThemeColorOverride("font_hover_color", Colors.White);
		b.AddThemeColorOverride("font_pressed_color", Colors.White);

		// METODO NOMEADO, NUNCA LAMBDA -- e a regra que nasceu das assinaturas vazadas: lambda nao da
		// pra cancelar. Aqui o emissor e filho deste node e morre junto com ele, entao nao ha `-=` a
		// fazer no `_ExitTree`; o metodo nomeado fica assim mesmo pra ninguem precisar reavaliar isso
		// no dia em que este botao virar filho de outra coisa.
		b.Pressed += AoOlhar;
		return b;
	}

	/// <summary>
	/// O CLIQUE. Manda o id e para de decidir: quem valida e o servidor (`GameServer.OlharParaALua`),
	/// que confere de novo a lua, a fase, o rabo e o genoma, e responde pelo chat quando recusa.
	///
	/// O gate desta tela e de CONVENIENCIA (ver <see cref="PodeOlhar"/>) -- ele existe pra o botao nao
	/// aparecer pra quem nunca poderia apertar, e nao pra substituir a autoridade.
	/// </summary>
	private void AoOlhar() => GameClient.Instance?.SendHabilidade("oozaru_olhar");

	/// <summary>
	/// ESTE CORPO TEM O QUE PRECISA? Duas perguntas, e a primeira responde tres.
	///
	/// `Sheet.Rabo` ja e a resposta de RACA **e** de RABO INTEIRO ao mesmo tempo: o membro "Rabo" so
	/// existe no corpo de quem tem rabo (`GameServer.Combat.cs:107,114` -- Saiyan e Halfbreed), e o bit
	/// so vem ligado enquanto ele nao foi decepado (`GameServer.cs:458`). Repetir aqui a lista de racas
	/// criaria uma segunda verdade sobre quem nasce com rabo, e ela envelheceria calada no dia em que
	/// uma raca nova entrasse na lista do servidor.
	///
	/// A segunda e o <see cref="GameClient.MeuOozaru"/>: o botao some enquanto se e a fera. Ele nao
	/// vira "voltar ao normal" porque voltar nem sempre e possivel -- o Dourado nao obedece -- e um
	/// botao que troca de significado no mesmo lugar ensina errado. Voltar mora no menu (tecla P).
	/// </summary>
	private static bool PodeOlhar()
		=> GameClient.Instance is { } cli
		   && cli.Sheet.Rabo
		   && cli.MeuOozaru == Jandirus.Core.Forms.FormaOozaru.Nao;

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
		// O `0.75` DESTA LINHA NAO E O `Encoberta`, apesar do mesmo numero: aqui e o quanto a nuvem
		// pode apagar o disco (sobra 25% de alfa, senao o mostrador some), la e o LIMIAR em que a lua
		// deixa de contar. Unificar os dois amarraria a transparencia do desenho a uma regra de jogo.
		_disco.Modulate = new Color(1, 1, 1, 1f - tapada * 0.75f);

		bool aMostra = tapada <= Encoberta;
		_nome.Text = aMostra ? Ceu.NomeDaFase(ceu.Fase) : "encoberta";
		_nome.AddThemeColorOverride("font_color",
			ceu.Cheia && aMostra ? Tema.Destaque : Tema.TextoFraco);

		// ============================ O BOTAO SEGUE A LEGENDA, NAO O CEU ============================
		// `aMostra` e a MESMA conta que escolhe entre o nome da fase e "encoberta", e e por isso que
		// ela virou variavel: se o botao olhasse so pra `ceu.Cheia`, a nuvem apagaria o disco, a
		// legenda diria "encoberta" e logo abaixo um botao vermelho continuaria prometendo o Oozaru.
		// O mostrador tem que contar UMA verdade -- e a mesma que a janela conta.
		//
		// A lua NO CEU ja esta garantida: o `return` la em cima esconde o painel inteiro sem ela.
		// ==========================================================================================
		_olhar.Visible = ceu.Cheia && aMostra && PodeOlhar();
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
