using Godot;

namespace Jandirus.Client;

/// <summary>
/// UM PLANETA DESENHADO A MAO -- a Terra, Namek, Vegeta, o Inferno.
///
/// O chao dele ja esta no .tscn, convertido do `.dmm` do jogo antigo. Esta classe nao GERA nada;
/// ela existe pra carregar a ficha do lugar (nome, gravidade, tipo) junto do mapa, e pra achar o
/// ponto de chegada sem chutar.
///
/// POR QUE UMA SUBCLASSE, e nao so o bool `Procedural = false`: porque as duas coisas divergem no
/// que precisam guardar e no que sabem fazer. Um pre-feito tem `TileMapLayer` com dados; um
/// procedural tem tamanho, bioma e um gerador. Deixar tudo na mesma classe daria um node com
/// metade dos campos sempre vazios no inspetor -- e campo vazio no inspetor e convite pra alguem
/// preencher e esperar que funcione.
/// </summary>
[GlobalClass]
public partial class PlanetaPreFeito : Planeta
{
	public override void _Ready()
	{
		Procedural = false;   // e o que ele E; nao da pra marcar no inspetor e virar outra coisa
		base._Ready();
	}

	/// <summary>
	/// Um pre-feito nao nasce -- ele ja estava la. O ponto de chegada e o que o servidor mandar,
	/// entao aqui nao ha o que fazer, e isso e um comportamento, nao um esquecimento.
	/// </summary>
	protected override void Nascer() { }
}
