using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// UMA CONSTRUCAO NO CHAO -- a bancada de pesquisa, o mainframe de androides.
///
/// ============================ AGORA ELA USA O SPRITE DO BYOND ============================
/// Ate aqui era um retangulo desenhado por codigo, e o comentario antigo dizia por que: "os icones
/// das 99 construcoes estao espalhados por dezenas de .dmi que o pipeline ainda nao converte".
/// Isso deixou de ser verdade -- o pipeline resolve 98 das 99 (`tech` no AssetPipeline), e ele
/// resolve o icone do que E ERGUIDO (`create_type`) e nao o do item de loja, que sao objetos
/// diferentes com propriedades diferentes.
///
/// A Research Bench, que o dono citou, tem CINCO QUADROS e 96 px de largura com `pixel_x = -32`.
/// O retangulo nao tinha como mostrar nem a animacao nem o tamanho.
/// =======================================================================================
///
/// O RETANGULO CONTINUA EXISTINDO como reserva: uma construcao sem .dmi convertido (hoje so a
/// Crafting_Bench) e melhor feia do que invisivel -- o jogador ergue uma coisa de meio milhao de
/// zeni e tem que ver onde ela caiu.
///
/// ENTRA NO Y-SORT como qualquer corpo, ancorada na BASE da celula que ela ocupa -- a mesma que o
/// servidor bloqueia (ver <see cref="CatalogoDeObras.Celula"/>). Ancorar no ponto solto onde o
/// jogador estava faria o desenho e a parede ficarem em lugares diferentes.
/// </summary>
public partial class ObraDesenhada : Node2D
{
	public string Tipo = "";
	public string Dono = "";
	public bool Aparafusada;
	public int Lab;

	/// <summary>res:// do SpriteFrames, vindo do servidor. Vazio = cai no retangulo.</summary>
	public string Arte = "";
	public string Estado = "";
	public Vector2 Pixel;

	/// <summary>Metade da largura do retangulo de reserva, em pixels.</summary>
	private const float Largura = 28f;
	private const float Altura = 34f;

	private AnimatedSprite2D? _sprite;

	public override void _Ready()
	{
		// ============================ O MOVEL ORDENA PELA BASE, NAO PELO TOPO ============================
		// Isto era `YSortEnabled = true`, e com o Y-sort ligado NO PROPRIO NODE quem entra na ordenacao
		// do mundo e o filho -- o sprite, desenhado a partir de `-tam.Y` (o topo do movel), e nao a base
		// em que o node esta. O banco do castelo de Vegeta ficava ATRAS da cadeira desenhada na mesma
		// celula: a cadeira e tile da camada `Objetos` (ordenada pelo centro da celula) e o banco era
		// ordenado pelo topo dele, 16 px acima. No BYOND os dois sao objs da mesma camada e o criado
		// depois cobre o anterior -- o banco, que vem depois da cadeira na lista do `.dmm`. O dono:
		// "icones em posicao errada e ficando torto, como a cadeira em cima do bank".
		//
		// Desligado, o movel inteiro e ordenado pela posicao do node -- a BASE, o pe do movel -- como o
		// corpo de um personagem: quem esta abaixo dele passa na frente, quem esta acima fica atras.
		// ==================================================================================================
		YSortEnabled = false;
		ZIndex = Plano(Tipo, Estado);
		MontarSprite();
	}

	/// <summary>
	/// EM QUE PLANO ESTA CONSTRUCAO DESENHA: 0 e o do Y-sort (o movel comum), 1 e por cima de todo corpo,
	/// -1 e por baixo de todo corpo.
	///
	/// ============================ O REGENERADOR TEM DOIS, E TROCA ============================
	/// E o `plane` do original, que ele mexe a cada pulso (`Tier 1.5.dm:127-133`): com alguem dentro,
	/// `plane = 6` -- acima do corpo, do cabelo e da roupa (`BODY_LAYER` 2 a `HAT_LAYER` 5) -- e a campanula
	/// por cima; vazio, `plane = 0`, abaixo de todo mundo. O tanque FECHADO esconde quem esta nele; o tanque
	/// aberto e um disco no chao em que se pisa.
	///
	/// Aqui quem troca e o INTERRUPTOR (a campanula aparece com o tanque ligado -- ver `Regenerador`), e
	/// o plano vai junto com ela. Sem o -1 o disco desligado cobria quem pisasse nele: a base e mais alta
	/// que um tile, e pelo Y-sort ela sempre fica na frente de quem esta na propria celula.
	/// =========================================================================================
	/// </summary>
	private static int Plano(string tipo, string estado) =>
		tipo == Jandirus.Core.Tech.Regenerador.Tipo ? (estado == Jandirus.Core.Tech.Regenerador.ArteLigado ? 1 : -1)
		: AcimaDoJogador(tipo) ? 1 : 0;

	/// <summary>
	/// ESTA CONSTRUCAO DESENHA POR CIMA DE QUEM PASSA POR BAIXO?
	///
	/// ============================ ORDENAR POR Y NAO RESOLVE ARVORE ============================
	/// O Y-sort poe na frente quem esta mais embaixo, e isso e certo pra uma bancada: ela tem a
	/// altura de um movel e o jogador que esta abaixo dela realmente esta na frente.
	///
	/// A arvore nao. Ela tem tres tiles de altura desenhados PRA CIMA a partir da base, e o corpo
	/// que anda pelo tronco esta visualmente DENTRO dela -- mas com o Y quase igual ao da base, e
	/// entao o desempate vira sorte. Foi o que o dono fotografou: o personagem por cima da copa.
	///
	/// O ORIGINAL RESOLVE COM PLANO, e nao com ordem: `plane = 8` na `AppleTree` (`Plants.dm:58`),
	/// acima da camada dos mobs. A arvore SEMPRE cobre quem passa por baixo -- e e o que faz o
	/// bosque parecer um bosque em vez de um tapete de sprites.
	/// ==========================================================================================
	/// </summary>
	private static bool AcimaDoJogador(string tipo) =>
		tipo == "AppleTree"
		// O ENMA DAIOH: no BYOND todo mob desenha por cima de turf (MOB_LAYER > TURF_LAYER), e o trono
		// dele e turf de tres fileiras -- por Y-sort o pedaco de baixo do trono cobria a barriga do
		// juiz ("o enma ta abaixo da cadeira", o dono, 2026-09-04). A mesa a frente dele e parede,
		// entao ninguem para sobre o sprite: o z acima do jogador nao cobre cabeca nenhuma.
		|| tipo == Jandirus.Core.World.Alem.TipoDoEnma;

	/// <summary>
	/// O sprite do original, ancorado como o BYOND ancora: o canto INFERIOR ESQUERDO do icone
	/// encosta no canto inferior esquerdo do tile, mais o `pixel_x`/`pixel_y`.
	///
	/// O `pixel_y` inverte de sinal: no BYOND o Y cresce PRA CIMA e no Godot pra baixo. Copiar o
	/// numero cru poria a bancada meio tile enterrada no chao em vez de meio tile acima dele.
	/// </summary>
	/// <summary>
	/// Tipos que ja reclamaram nesta sessao. A construcao e redesenhada a cada entrada em zona, e
	/// um erro por quadro afogaria o log que ele deveria destacar.
	/// </summary>
	private static readonly HashSet<string> _jaReclamou = [];

	/// <summary>
	/// A RESERVA CINZA NAO PODE SER MUDA. `_Draw` desenha um retangulo quando o sprite nao carrega,
	/// entao a construcao nunca fica invisivel na tela -- mas ela fica invisivel no LOG, e e assim
	/// que um buraco de asset vira "arte de proposito" e sobrevive meses. Ver a mesma regra do lado
	/// do servidor (`GameServer.CarregarTech`) e do pipeline (`MapConverter`, "PAREDE INVISIVEL").
	/// </summary>
	private void Reclamar(string porque)
	{
		if (!_jaReclamou.Add(Tipo)) return;
		GD.PushError($"[obra] '{Tipo}' sem desenho ({porque}, arte='{Arte}', estado='{Estado}') -- "
					 + "vai aparecer como retangulo cinza. Rode o AssetPipeline ('tech').");
	}

	private void MontarSprite()
	{
		if (Arte.Length == 0) { Reclamar("o catalogo nao tem arte pra ela"); return; }
		if (!ResourceLoader.Exists(Arte)) { Reclamar("o .tres nao existe no disco"); return; }
		if (FolhasPresas.Carregar(Arte) is not { } folha)
		{ Reclamar("o .tres nao carregou como SpriteFrames"); return; }

		// ============================ O ESTADO PODE SER UMA PILHA ============================
		// "base+tank" e o estado `base` com o `tank` por cima -- o `overlays += podlayer` do regenerador
		// (`Tier 1.5.dm:129`). O servidor manda a pilha pronta, e o `+` nunca aparece no nome de um estado
		// (o `Sanear` o trocaria por `_`). A PRIMEIRA camada e a que da o tamanho e o contorno de "solta".
		// =====================================================================================
		foreach (string camada in Estado.Split('+'))
		{
			string anim = camada.Length > 0 ? Sanear(camada) : "default";
			if (!folha.HasAnimation(anim))
			{
				// A CAMADA DE CIMA QUE FALTA so deixa de aparecer: a base ja esta de pe.
				if (_sprite != null) { Reclamar($"a camada '{camada}' nao existe na folha"); continue; }

				// o .dmi pode nomear o estado de um jeito que o conversor saneou diferente; sem o
				// estado certo, o primeiro serve mais do que nada
				string[] nomes = [.. folha.GetAnimationNames()];
				if (nomes.Length == 0) { Reclamar("o SpriteFrames nao tem animacao nenhuma"); return; }
				anim = nomes[0];
			}

			if (folha.GetFrameTexture(anim, 0) is not { } quadro)
			{
				Reclamar($"a animacao '{anim}' nao tem quadro 0");
				if (_sprite == null) return;
				continue;
			}
			Vector2 tam = quadro.GetSize();

			var sprite = new AnimatedSprite2D
			{
				SpriteFrames = folha,
				Animation = anim,
				Centered = false,
				// da ancora (a base da celula) pro canto superior esquerdo do desenho
				Position = new Vector2(Pixel.X, -tam.Y - Pixel.Y),
			};
			AddChild(sprite);
			sprite.Play();
			_sprite ??= sprite;
		}
	}

	/// <summary>Quantas camadas de desenho esta obra tem agora. So pra bancada (`--diagmaquinas`).</summary>
	public int CamadasDeTeste => GetChildren().Count(n => n is AnimatedSprite2D);

	/// <summary>Mesmo saneamento de nome que o `SpriteFramesWriter` aplicou ao converter.</summary>
	private static string Sanear(string s)
	{
		var sb = new System.Text.StringBuilder(s.Length);
		foreach (char c in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
		string r = sb.ToString().Trim('_');
		while (r.Contains("__")) r = r.Replace("__", "_");
		return r.Length == 0 ? "state" : r;
	}

	/// <summary>Cor por familia de construcao -- so a reserva usa.</summary>
	private Color Cor => Tipo switch
	{
		"Research_Station" => new Color(0.35f, 0.62f, 0.85f),
		"Fabricator" => new Color(0.55f, 0.75f, 0.45f),
		"Android_Creation_Mainframe" => Lab switch
		{
			1 => new Color(0.85f, 0.72f, 0.30f),   // Android Lab
			2 => new Color(0.55f, 0.80f, 0.45f),   // Bio-Android Lab
			_ => new Color(0.70f, 0.70f, 0.76f),
		},
		_ => new Color(0.62f, 0.60f, 0.68f),
	};

	public override void _Draw()
	{
		// SOLTA PISCA. Uma construcao nao aparafusada nao funciona, e descobrir isso so ao clicar
		// e a diferenca entre um jogo que ensina e um que esconde. Vale com sprite ou sem.
		Rect2 corpo = _sprite != null
			? new Rect2(_sprite.Position, TamanhoDoSprite())
			: new Rect2(-Largura, -Altura, Largura * 2, Altura);

		if (_sprite == null)
		{
			Color c = Cor;

			// SOMBRA NO CHAO. Sem ela a construcao parece flutuar -- e num jogo com Y-sort,
			// "parece flutuar" e indistinguivel de "esta na camada errada".
			DrawCircle(new Vector2(0, -2), Largura * 0.9f, new Color(0, 0, 0, 0.28f));

			DrawRect(corpo, c);
			DrawRect(corpo, c.Darkened(0.5f), filled: false, width: 2f);
			DrawRect(new Rect2(-Largura * 0.6f, -Altura * 0.85f, Largura * 1.2f, Altura * 0.4f),
					 c.Lightened(0.35f));
		}

		if (!Aparafusada)
		{
			float p = 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 250f);
			DrawRect(corpo.Grow(3), new Color(1f, 0.85f, 0.35f, 0.25f + 0.35f * p), filled: false, width: 2f);
		}
	}

	private Vector2 TamanhoDoSprite()
	{
		if (_sprite?.SpriteFrames?.GetFrameTexture(_sprite.Animation, 0) is { } t) return t.GetSize();
		return new Vector2(ZoneCollision.TileSize, ZoneCollision.TileSize);
	}

	public override void _Process(double delta)
	{
		if (!Aparafusada) QueueRedraw();   // so a que pisca precisa de quadro novo
	}
}
