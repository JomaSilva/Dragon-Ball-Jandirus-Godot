using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// UMA PORTA DO MAPA, desenhada e animada.
///
/// ============================ POR QUE ELA NAO E UM TILE ============================
/// No BYOND a porta e um turf que muda `density`, `opacity` e `icon_state` em runtime, e toca a
/// abertura com `flick("Opening", src)`. Tile do Godot nao faz nada disso: o TileMapLayer guarda
/// um desenho por celula e a animacao dele e um laco, nao um disparo.
///
/// Pior: a cena da zona fica CACHEADA entre visitas (ver `World.GuardarZonaAtual`). Apagar e repor
/// a celula pra abrir e fechar mutaria um objeto que sobrevive a saida do planeta -- sair com a
/// porta aberta deixaria o buraco la pra sempre.
///
/// Entao o conversor de mapa NAO pinta a celula da porta (ver `MapConverter.EhPorta`) e escreve a
/// lista `zXX.portas`; quem desenha a porta, nos quatro estados, e este node.
/// ==================================================================================
///
/// QUEM MANDA ABRIR E O SERVIDOR. Este node so obedece: <see cref="Definir"/> chega por pacote
/// (`S2C.Porta`). A porta e do mundo -- duas pessoas na mesma casa veem a mesma porta.
/// </summary>
public partial class Porta : AnimatedSprite2D
{
	/// <summary>A celula do mapa. E a chave que o pacote do servidor usa.</summary>
	public int Cx, Cy;

	private bool _aberta;

	/// <summary>Nomes de animacao do `.dmi` convertido -- os `icon_state` do original, em minuscula.</summary>
	private const string Fechada = "closed", Abrindo = "opening", Aberta_ = "open", Fechando = "closing";

	public static Porta Criar(PortaDoMapa d)
	{
		var p = new Porta
		{
			Cx = d.X,
			Cy = d.Y,
			// O CENTRO DA CELULA. O tile que ela substitui ordenava pelo centro (o conversor deixa
			// `y_sort_origin` em 0 de proposito -- ver o comentario dele), e o personagem tambem:
			// os pes ficam 16 px abaixo do no. Nascer no mesmo ponto e o que faz o Y-sort comparar
			// a porta e quem passa por ela na mesma referencia.
			Position = new Vector2(d.X * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f,
								   d.Y * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f),
			SpriteFrames = ResourceLoader.Load<SpriteFrames>(d.Arte),
		};
		return p;
	}

	public override void _Ready()
	{
		if (SpriteFrames == null) return;

		// O `flick` DO BYOND TOCA UMA VEZ E PARA. O conversor de .dmi marca todo estado como laco
		// (o .dmi nao distingue), entao quem sabe que abrir e um DISPARO e este codigo.
		//
		// Mexe no recurso COMPARTILHADO de proposito: o SpriteFrames vem do cache do ResourceLoader
		// e as portas de uma zona sao todas a mesma folha. "Abrir" e "fechar" nao sao laco em
		// desenho nenhum -- nao ha consumidor que queira o contrario.
		foreach (string a in new[] { Abrindo, Fechando })
			if (SpriteFrames.HasAnimation(a)) SpriteFrames.SetAnimationLoopMode(a, SpriteFrames.LoopMode.None);

		AnimationFinished += AoTerminar;
		Play(Fechada);
	}

	/// <summary>
	/// O estado que o servidor mandou.
	///
	/// <paramref name="animar"/> falso e o caso de quem ACABA DE CHEGAR na zona: uma porta que ja
	/// estava aberta antes de eu entrar nao deve abrir de novo na minha frente.
	/// </summary>
	public void Definir(bool aberta, bool animar)
	{
		if (SpriteFrames == null || _aberta == aberta) return;
		_aberta = aberta;

		string alvo = aberta ? Abrindo : Fechando;
		if (!animar) alvo = aberta ? Aberta_ : Fechada;
		if (!SpriteFrames.HasAnimation(alvo)) alvo = aberta ? Aberta_ : Fechada;
		if (!SpriteFrames.HasAnimation(alvo)) return;

		Play(alvo);
	}

	/// <summary>Terminou o disparo: fica no estado final (o `icon_state = "Open"` do DM).</summary>
	private void AoTerminar()
	{
		string fim = _aberta ? Aberta_ : Fechada;
		if (SpriteFrames?.HasAnimation(fim) == true) Play(fim);
	}
}
