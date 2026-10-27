using Jandirus.Core.World;

namespace Jandirus.Core.Combat;

/// <summary>
/// UMA PECA DE CORPO NO CHAO, como o SERVIDOR a guarda -- o `/obj/bodyparts` do BYOND
/// (`mobparts.dm:328-477`) reduzido ao que o mundo precisa lembrar dele.
///
/// ============================ POR QUE O SERVIDOR PASSOU A GUARDAR ISTO ============================
/// A peca era so um DECALQUE: o `SpawnLop` virava um `S2C.Decalque` disparado UMA vez pra quem estava
/// na zona naquele instante, e mais nada. Quem chegava depois, quem relogava e quem voltava do Outro
/// Mundo nunca via o braco que tinha caido ali -- e no jogo isso se le como "o membro nao spawnou".
/// E a mesma familia de defeito que este projeto ja pegou nas construcoes, nas portas, no cenario
/// caido e nas feridas: o pacote existe, sai uma vez, e quem nao estava presente nunca soube.
///
/// No BYOND a peca e um OBJETO no turf, e um objeto no turf e visto por quem entrar la enquanto ele
/// existir -- por 600 s (`spawn(6000) src.loc = null`, `mobparts.dm:395-397`). Esta classe e o que
/// faz o port lembrar a peca pelo mesmo prazo: o servidor guarda a lista por zona, expira pelo relogio
/// e a REAPRESENTA a quem entra (`S2C.Pecas`), do mesmo jeito que reapresenta o cenario derrubado.
///
/// O QUE ELA NAO E: um item. Nao se pega, nao se larga, nao se come (`Get`/`Drop`/`Eat`,
/// `mobparts.dm:336-394`). O port nao tem objeto-no-chao com dono (`Core/Items` e so inventario), e a
/// divergencia continua declarada em `Protocol.Decal.Membro`.
/// ================================================================================================
/// </summary>
public sealed class PecaNoChao
{
	public PecaDeCorpo Peca;

	/// <summary>Onde ela caiu, ja com o espalhamento aplicado. E o que o cliente desenha.</summary>
	public Vec2 Onde;

	/// <summary>Quando caiu (relogio do servidor, ms). E dele que sai o prazo -- ver <see cref="PecasNoChao.Venceu"/>.</summary>
	public long CaiuEm;
}

/// <summary>As regras da peca no chao: o prazo, o teto, e o espalhamento. Todas do DM, menos o teto.</summary>
public static class PecasNoChao
{
	/// <summary>
	/// QUANTO TEMPO A PECA FICA NO CHAO: `spawn(6000) src.loc = null` no `New()` do `/obj/bodyparts`
	/// (`mobparts.dm:395-397`) -- 6000 decisegundos, dez minutos. **O mesmo numero nas duas pontas**: o
	/// cliente le esta constante pro prazo do decalque (`Decalques.Prazo`), e o servidor a le pra
	/// expirar a lista. Dois numeros seria o braco sumindo da tela de um e continuando no retrato do
	/// outro.
	/// </summary>
	public const long MsNoChao = 600_000;

	/// <summary>
	/// QUANTAS PECAS UMA ZONA LEMBRA. O DM nao tem teto -- cada peca e um objeto que vive os 600 s
	/// dela --, e o teto aqui e o mesmo que o cliente ja aplicava ao desenho (`Decalques.MaxPecas`):
	/// 32 pecas vivas por vez, e a 33a empurra a MAIS VELHA, que e a que o DM apagaria primeiro por
	/// ordem de `spawn`. Lembrar mais do que o cliente desenha seria mandar um retrato que a tela
	/// nao consegue honrar.
	/// </summary>
	public const int TetoPorZona = 32;

	/// <summary>
	/// O ESPALHAMENTO EM VOLTA DO CORPO: `pixel_x += rand(-32,32); pixel_y += rand(-32,32)` no `New()`
	/// de cada peca (`mobparts.dm:404-405` e as nove irmas) -- um tile em pixels, nos dois eixos.
	/// </summary>
	public const int Espalhamento = 32;

	/// <summary>
	/// O `rand(-32,32)` do DM, SEMEADO -- funcao pura de (zona, ponto, peca, ordem de queda).
	///
	/// ============================ POR QUE SEMEADO, E NAO O `Random` DO SERVIDOR ============================
	/// O `_rng` do servidor nasce sem semente (`new Random()`): duas rodadas da mesma bancada dao
	/// pecas em lugares diferentes, e "caiu longe do corpo" deixa de ser reproduzivel. Determinismo
	/// por seed e lei neste projeto (o ceu, o universo, o chao danificado e as Super Esferas ja saem
	/// de funcoes puras), e a peca entra na mesma regra.
	///
	/// O ORDINAL E O QUE SEPARA DUAS PECAS IGUAIS NO MESMO PONTO: um corpo que perde os dois bracos sem
	/// sair do lugar derruba duas pecas `Braco` na mesma coordenada, e sem ele as duas cairiam no
	/// mesmo pixel -- exatamente o sintoma que a familia 3 da `--pecateste` existe pra pegar ("todas
	/// no mesmo ponto"). E a ordem de queda na zona, que e o que o `spawn` do DM tambem tem.
	///
	/// O embaralhador e o FNV-1a de 32 bits, o mesmo do `World.Decalques.Embaralhar` do cliente.
	/// =====================================================================================================
	/// </summary>
	public static Vec2 Espalhar(ulong zona, Vec2 onde, PecaDeCorpo peca, int ordinal)
	{
		uint h = Embaralhar(zona, onde, peca, ordinal);
		int faixa = Espalhamento * 2 + 1;   // -32..32 sao 65 valores, como o rand(-32,32)
		int dx = (int)(h % (uint)faixa) - Espalhamento;
		int dy = (int)((h >> 8) % (uint)faixa) - Espalhamento;
		return new Vec2(dx, dy);
	}

	private static uint Embaralhar(ulong zona, Vec2 onde, PecaDeCorpo peca, int ordinal)
	{
		unchecked
		{
			uint h = 2166136261u;
			void Mistura(uint v) { h = (h ^ v) * 16777619u; }
			Mistura((uint)zona);
			Mistura((uint)(zona >> 32));
			// PELO `int` ANTES DO `uint`: a conversao direta de um float NEGATIVO pra `uint` nao e
			// definida em C#, e uma coordenada negativa existe (a beirada do mapa, o espaco).
			Mistura((uint)(int)MathF.Round(onde.X));
			Mistura((uint)(int)MathF.Round(onde.Y));
			Mistura((byte)peca);
			Mistura((uint)ordinal);
			h ^= h >> 13;
			h *= 0x5bd1e995u;
			h ^= h >> 15;
			return h;
		}
	}

	/// <summary>Esta peca ja passou dos 600 s?</summary>
	public static bool Venceu(PecaNoChao p, long agora) => agora - p.CaiuEm >= MsNoChao;

	/// <summary>
	/// QUANTO FALTA PRA ELA SUMIR, em ms -- e o que viaja no retrato. Quem chega na zona no minuto 9
	/// de uma peca tem que ve-la sumir no minuto 10, e nao dez minutos depois de ter chegado.
	/// </summary>
	public static long RestanteMs(PecaNoChao p, long agora) => Math.Max(0, p.CaiuEm + MsNoChao - agora);
}

/// <summary>
/// (2026-10-26) UMA MARCA DE DESTRUICAO NO CHAO -- cratera ou pedaco de trilha --, como o SERVIDOR a guarda. A irma da
/// <see cref="PecaNoChao"/>, e pelo mesmo motivo: o dono pediu *"Faca destruicao de cenario como crateras e trilhas de destruicao permanecerem por mt mais tempo, podendo ser 1 dia ingame"*, e uma marca que dura um dia do jogo nao pode viver
/// so na tela de quem estava la no instante -- quem chega, reloga ou volta de outro planeta tem que ve-la.
///
/// O TIPO E A DIRECAO SAO BYTES porque o Core nao conhece o fio: sao o `Protocol.Decal` e o `Facing` que o `S2C.Decalque` levou.
/// </summary>
public sealed class MarcaNoChao
{
	public byte Tipo;

	/// <summary>Onde ela caiu. E o que o cliente desenha.</summary>
	public Vec2 Onde;

	public byte Dir;

	/// <summary>Quando caiu (relogio do servidor, ms). E dele que sai o prazo -- ver <see cref="MarcasNoChao.Venceu"/>.</summary>
	public long CaiuEm;
}

/// <summary>(2026-10-26) As regras da marca de destruicao no chao: o prazo (um dia do jogo) e o teto por zona.</summary>
public static class MarcasNoChao
{
	/// <summary>
	/// QUANTO TEMPO A MARCA FICA NO CHAO: UM DIA DO JOGO (<see cref="Ceu.SegundosPorDia"/>, 24 minutos reais) -- o pedido do dono. Era
	/// 18 s a cratera pequena, 20 s a trilha, 40 s a grande e 90 s a da Genkidama e a do pod, e todas so na tela de quem viu. **O mesmo
	/// numero nas duas pontas**, como o da peca: o cliente o le pro prazo do decalque (`Decalques.Prazo`) e o servidor pra expirar a lista.
	/// </summary>
	public const long MsNoChao = (long)(Ceu.SegundosPorDia * 1000);

	/// <summary>
	/// QUANTAS MARCAS UMA ZONA LEMBRA. Um dia inteiro de briga numa zona deixa MILHARES (cada arremesso risca uma trilha de um decalque
	/// por tile), e lembrar sem teto seria o retrato e o chao crescendo pra sempre. Mil cobrem o dia de uma briga comum; acima disso a
	/// mais VELHA sai, que e a que ja estaria mais apagada. O cliente segura a mesma quantidade na fila propria delas
	/// (`Decalques.MaxMarcas`): lembrar mais do que a tela desenha seria mandar um retrato que ela nao honra.
	/// </summary>
	public const int TetoPorZona = 1000;

	/// <summary>Esta marca ja passou do dia dela?</summary>
	public static bool Venceu(MarcaNoChao m, long agora) => agora - m.CaiuEm >= MsNoChao;

	/// <summary>QUANTO FALTA PRA ELA SUMIR, em ms -- o que viaja no retrato (ver <see cref="PecasNoChao.RestanteMs"/>).</summary>
	public static long RestanteMs(MarcaNoChao m, long agora) => Math.Max(0, m.CaiuEm + MsNoChao - agora);
}
