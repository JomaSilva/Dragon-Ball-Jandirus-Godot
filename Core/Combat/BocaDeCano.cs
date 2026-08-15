using Jandirus.Core.World;

namespace Jandirus.Core.Combat;

/// <summary>
/// DE ONDE SAI O TIRO -- a boca do cano, no Core porque as DUAS pontas precisam da mesma resposta:
/// o servidor pra saber de onde a cabeca comeca a colidir, o cliente pra saber onde desenhar o
/// primeiro pedaco do trem. Duas contas parecidas dariam um feixe que aparece num lugar e acerta
/// noutro -- que e pior que o defeito que este arquivo conserta.
///
/// ============================ O BYOND E EXPLICITO: O TIRO NUNCA OCUPA O TILE DO MOB ============================
/// `A.loc = src.loc` e, no MESMO tique, `step(A, A.dir)` (`beams.dm:177-185`). O beam nasce no tile
/// de quem atirou e SAI DELE antes de qualquer outra coisa. Nao e so o raio: as bolas fazem igual
/// (`blasts.dm:109`+`:128`, `:238-239`, `:300-301`, `:563-572`).
///
/// E o `origin` -- o pedaco colado na mao (`objects.dm:117-126`) -- e definido justamente por isso:
/// ele e o segmento cujo tile DE TRAS e o do dono, ou seja ele mora um tile A FRENTE do corpo, nunca
/// em cima dele.
///
/// SEM ESTE PASSO o quadro de 32x32 da cabeca do feixe fica carimbado CENTRADO no centro do sprite
/// do dono: cobre o personagem inteiro, sobra 6 px acima da cabeca dele e metade da largura fica
/// ATRAS das costas. Foi exatamente o que o dono fotografou -- *"os beams tao saindo DE CIMA do
/// personagem"*.
/// ==========================================================================================================
///
/// ============================ E O PASSO MUDA COM A DIRECAO ============================
/// Um numero fixo so acertaria as cardeais. O passo e um TILE projetado no eixo do tiro --
/// `32*(|ux|+|uy|)` --, que da 32 px nas quatro cardeais e 45,25 nas diagonais: a distancia real
/// entre centros de tiles vizinhos na diagonal, o mesmo √2 que o `dir_matrix` (`objects.dm:52-54`)
/// embute nas quatro entradas diagonais da matriz.
///
/// E a MESMA formula que o desenho do trem ja usa pra espacar os pedacos
/// (`ProjetilDesenhado.MedirTrem`). Nao ha um segundo "quanto e um tile neste rumo" no projeto.
/// ===================================================================================
///
/// ============================ A ALTURA NAO ENTRA AQUI, E ISSO E DE PROPOSITO ============================
/// O <see cref="Vec2"/> deste projeto e o plano do CHAO; quem voa carrega a altura num campo
/// separado (`Projetil.Altitude`, copiado do dono no nascimento) e e ele que o `Voo.PodeAcertar` e o
/// `Voo.AtravessaCenario` leem. O DM nao tem altura nenhuma -- la o feixe nasce no centro da celula
/// e pronto (medido: a faixa da `Beam3` e da `Kamehameha` ocupa o meio da celula nas duas).
///
/// Quem levanta o DESENHO e o cliente, com a mesma escala de tela dos corpos
/// (`Voo.EscalaNaTela`) -- ver `ProjetilDesenhado.Altitude`. Levantar aqui moveria a colisao pra uma
/// altura que o servidor ja conta por outro caminho, e o tiro passaria a acertar duas vezes mais
/// alto do que deveria.
/// ================================================================================================
/// </summary>
public static class BocaDeCano
{
	/// <summary>
	/// QUANTO ANDA UM `step()` NESTE RUMO, em pixel. 32 nas cardeais, 45,25 nas diagonais.
	///
	/// Rumo nulo devolve zero -- e a resposta certa: a mina da `Ki_Bomb` nasce sem rumo nenhum
	/// (`blasts.dm:404-450`), e ela nasce EM VOLTA DO ALVO, nao na mao de ninguem.
	/// </summary>
	public static float Passo(Vec2 rumo)
	{
		Vec2 u = rumo.Normalized();
		return ZoneCollision.TileSize * (MathF.Abs(u.X) + MathF.Abs(u.Y));
	}

	/// <summary>
	/// A BOCA DO CANO: um tile a frente do centro do corpo, no rumo do tiro.
	/// </summary>
	/// <param name="centro">
	/// O centro do SPRITE de quem atira (o `Pos` do corpo). Nao os pes: a folha do personagem e
	/// `Centered` no node (`CharacterVisual`), entao o centro do quadro E este ponto, e o DM tambem
	/// centra o segmento do feixe na celula (`pixel_x = -((W/2)-16)`, `objects.dm:85`).
	/// </param>
	public static Vec2 De(Vec2 centro, Vec2 rumo)
	{
		float d = Passo(rumo);
		return d <= 0 ? centro : centro + rumo.Normalized() * d;
	}
}
