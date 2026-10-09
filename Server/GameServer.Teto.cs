using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// QUEM ESTA SOB TETO, pro servidor.
///
/// A regra e do Core (<see cref="CelulaInterna"/>, onde estao as linhas do DM). Aqui mora so a ponte
/// entre ela e as duas coisas que so o servidor tem: onde o corpo esta, e o que ja caiu.
///
/// ============================ O QUE O SERVIDOR FAZ COM A RESPOSTA ============================
/// No original o mob le a area em que pisa a cada tique (`current_area = GetArea()`,
/// `Stats.dm:158`), e dentro de casa o `CheckTime()` enxerga hora 0 e lua 0
/// (`Weather.dm:179-186`, porque a area interna tem `daylightcycle` e `mooncycle` zerados). Duas
/// coisas saem dai, e sao as que perguntam aqui:
///
///   * a LUA nao se anuncia nem transforma quem esta dentro (`Weather.dm:195-206`; o proprio DM
///     ainda repete a guarda na `:201`, `current_area.name!="Inside"`) -- ver `OlharProCeu`,
///     `OlharParaALua` e `ALuaPegaOSaiyajin`;
///   * o RAIO da tempestade nao e sorteado por quem esta dentro (`doWeatherEffects`,
///     `Weather.dm:243-254`, le o clima da area DO MOB) -- ver `SortearQuemPuxaORaio`.
///
/// O QUE **NAO** PERGUNTA, de proposito: a fera que ja existe. `AFonteDaFera` continua olhando o
/// ceu da ZONA -- o dono pediu que o Oozaru caia quando a lua SAI do ceu, e entrar numa casa nao
/// tira a lua de la.
///
/// E OS ASTROS DO MAKYO (`AstrosDoMakyoG11`) tambem leem o ceu da zona. No DM eles leem
/// `savant.currentDaylight`, que dentro de casa vale 0 e CONGELA o ultimo bonus (o `switch` nao tem
/// ramo pro zero, `makyo.dm:78` e `:121`). Divergencia declarada: o dono decidiu deixar como esta
/// (2026-10-08) -- e balanco de raca, e os astros seguem o ceu da zona dentro e fora de casa.
/// ==============================================================================================
///
/// SEM ESTADO NOVO. "A celula caiu?" sai do <see cref="_cenarioCaido"/>, o conjunto que o
/// `DerrubarCelula` ja escreve e que o admin ja esvazia; nao ha uma segunda lista pra sair de
/// sincronia quando o cenario e refeito.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>ESTE CORPO ESTA SOB TETO AGORA?</summary>
	private bool SobTeto(ServerPlayer pl) => SobTeto(pl.Zone, pl.Pos);

	/// <summary>
	/// A mesma pergunta pra um ponto qualquer da zona (o CENTRO de um corpo; a celula que conta e a
	/// dos pes -- ver <see cref="CelulaInterna.CelulaDoCorpo"/>).
	///
	/// O CAMINHO COMUM SAI NO PRIMEIRO TESTE: mundo gerado, espaco e metade dos andares pre-feitos
	/// nao tem plano nenhum.
	/// </summary>
	private bool SobTeto(ZoneKey zona, Vec2 centro)
	{
		(int cx, int cy) = CelulaInterna.CelulaDoCorpo(centro);
		return CelulaSobTeto(zona, cx, cy);
	}

	/// <summary>ESTA CELULA DA ZONA ESTA SOB TETO AGORA? O plano do mapa, menos o que ja caiu.</summary>
	private bool CelulaSobTeto(ZoneKey zona, int cx, int cy)
	{
		if (MapaDaZonaOuCatalogo(zona) is not { TemInterior: true } mapa) return false;

		bool caiu = _cenarioCaido.TryGetValue(zona.Name, out HashSet<(int X, int Y)>? caidas)
					&& caidas.Contains((cx, cy));
		return CelulaInterna.SobTeto(mapa, cx, cy, caiu);
	}
}
