using Jandirus.Core.Economia;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DO ROBO DAS ROCHAS (`--diagrochas`, no modo host) -- este arquivo nao roda bancada nenhuma: e o
/// que o robo do cliente (`Client/RoboDeRochas.cs`) precisa encomendar a AUTORIDADE. Por o corpo num mundo
/// sorteado ao pe de uma rocha grande (pelo pouso de producao), move-lo de um lado da pedra pro outro e tirar
/// uma rocha de minerio posta pela bancada. (O saldo de recursos de quem colheu ja tem leitura de bancada:
/// `RecursosDeTeste`, em `GameServer.Lenha.cs`.)
///
/// O resto do roteiro e do jogador e da producao: andar, a tecla E, o botao do menu.
/// </summary>
public partial class GameServer
{
	/// <summary>Tira uma rocha de minerio pelo caminho de producao (conjunto, mapa e anuncio), sem pagar ninguem.</summary>
	internal void TirarRochaDeTeste(ZoneKey zona, int cx, int cy)
	{
		if (_rochas.TryGetValue(zona.Hash, out CampoDeRochas? campo)) TirarRocha(campo, cx, cy);
	}

	/// <summary>Poe o corpo num ponto, pelo funil de todo "o servidor moveu voce" (`CravarPosicao`).</summary>
	internal void CravarDeTeste(int id, Vec2 onde)
	{
		if (_players.TryGetValue(id, out ServerPlayer? p)) CravarPosicao(p, onde);
	}

	/// <summary>
	/// POUSA O CORPO NUM MUNDO SORTEADO, TRES CELULAS ABAIXO DE UMA ROCHA GRANDE, e devolve a ancora dela.
	/// Nulo se o universo nao tem um mundo que sirva por perto.
	///
	/// O MUNDO E ESCOLHIDO, e nao o primeiro: precisa ter uma rocha grande com a coluna da ancora livre na
	/// frente e atras (<see cref="RochaGrandeComRetaLivre"/>), ser pequeno (a bancada nao precisa de um
	/// milhao de celulas) e LEVE -- acima de 2x de gravidade o corpo novato da bancada ja anda arrastado, e
	/// a medida viraria a do esmagamento.
	///
	/// E TEM DE TER DIA: um mundo de noite eterna (4 em 100, `Ceu.DaSeed`) nao clareia com o sol a pino da
	/// bancada. A segunda rodada do robo caiu num deles -- as checagens de pixel passaram, mas com um terco da
	/// folga, e as fotos que o dono ia olhar sairam pretas.
	///
	/// O POUSO E O DE PRODUCAO (`PousarDeVerdade`, da bancada das obras): orbita, mundo nascendo fora do
	/// tique, `PousarEmProcedural`. So o ultimo passo e de palco -- o corpo vai do ponto de chegada pro pe
	/// da pedra pelo `CravarPosicao`.
	/// </summary>
	internal (int X, int Y)? PousarJuntoDeUmaRochaGrandeDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return null;

		for (int i = 1; i <= 600; i++)
			foreach (PlanetaNoEspaco p in Espaco.PorPerto(SeedDoUniverso, new ChunkId(i * 37, i * 53)))
			{
				if (p.Premade || PlanetaMorto(p)) continue;
				MundoProcedural ficha = MundoProcedural.DaSeed(p.Seed, p.Nome);
				if (ficha.Lado > 400 || ficha.Gravidade > 2) continue;
				if (!RelogioDaZona(Espaco.ZonaDe(p)).TemDia) continue;
				if (RochaGrandeComRetaLivre(GeradorDeTerreno.Gerar(ficha.Parametros())) is not { } ancora) continue;

				if (!PousarDeVerdade(pl, p)) return null;
				CravarPosicao(pl, RochaDeMinerio.Ponto(ancora.X, ancora.Y + 3));
				return ancora;
			}
		return null;
	}

	/// <summary>
	/// A PRIMEIRA ROCHA GRANDE DESTE MUNDO COM A RETA LIVRE: a coluna da ancora, de tres celulas abaixo a
	/// quatro acima, sem montanha, agua nem arvore -- e, fora da pegada dela (a ancora e a celula de cima),
	/// sem outra pedra. O que barra quem sobe por ali e so esta rocha.
	///
	/// E SEM ARVORE NA FRENTE DA BASE (duas colunas pra cada lado, da fileira da ancora a cinco abaixo): a
	/// copa de uma arvore sobe ate 180 px e sobra quase tres tiles de cada lado, e cobriria na foto o pe da
	/// pedra que o robo mede.
	/// </summary>
	internal static (int X, int Y)? RochaGrandeComRetaLivre(TerrenoGerado t)
	{
		static bool Arvore(TerrenoGerado t, int x, int y) =>
			t.CoberturaEm(x, y) is CoberturaDeTerreno.Arvore or CoberturaDeTerreno.ArvoreAcento;

		for (int y = 8; y < t.Altura - 8; y++)
			for (int x = 8; x < t.Largura - 8; x++)
			{
				if (t.CoberturaEm(x, y) != CoberturaDeTerreno.RochaGrande) continue;

				bool reta = true;
				for (int dy = -4; dy <= 3 && reta; dy++)
				{
					reta &= t.ClasseEm(x, y + dy) is not (ClasseDeTerreno.Montanha or ClasseDeTerreno.Agua) && !Arvore(t, x, y + dy);
					if (dy is not (0 or -1)) reta &= t.Colisao.ServeDeChao(x, y + dy);
				}
				for (int dy = 0; dy <= 5 && reta; dy++)
					for (int dx = -2; dx <= 2 && reta; dx++)
						reta &= !Arvore(t, x + dx, y + dy);
				if (reta) return (x, y);
			}
		return null;
	}
}
