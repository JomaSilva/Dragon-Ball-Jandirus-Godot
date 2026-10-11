using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O QUE O MAPA DA ABA WORLD PRECISA SABER DA CENA -- so leitura, num parcial proprio pra nao encostar no
/// arquivo do mundo. Quem monta o mapa e o `MapasDosLugares`; quem o desenha, o `MapaDoLugarNaTela`.
/// </summary>
public partial class World
{
	/// <summary>
	/// O LUGAR MONTADO AGORA: a chave da zona e a raiz da cena dela. A chave pode estar ATRAS da do
	/// `GameClient.Zone` por um instante (o pacote de troca chegou e a cena ainda nao trocou) -- quem
	/// pergunta confere as duas.
	/// </summary>
	public (ZoneKey Zona, Node2D? Raiz) LugarMontado => (_zonaDoAtual, _zonaAtual);

	/// <summary>
	/// O TAMANHO DO MAPA DESENHADO A MAO em que estou, em tiles -- do manifesto. Nulo fora de zona
	/// pre-feita: o catalogo resolve por NOME, e a mente ("Interdimension") casaria com o z24 do BYOND.
	/// </summary>
	public (int Largura, int Altura)? TamanhoDoPreFeito =>
		_zonaAtual is PlanetaPreFeito && _catalogo?.Get(_zonaDoAtual) is { } e ? (e.W, e.H) : null;

	/// <summary>Pra que lado o meu corpo olha na minha tela (a seta do marcador do mapa).</summary>
	public Facing OlharLocal => _local != null && IsInstanceValid(_local) ? _local.OlharDeTeste : Facing.South;
}
