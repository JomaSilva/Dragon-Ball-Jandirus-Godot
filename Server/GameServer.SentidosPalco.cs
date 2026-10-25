using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagsentidos` (2026-10-18): os corpos em volta do jogador (os do scouter e um por nivel do Sense), o
/// scouter, o poder do proprio corpo e o nivel do Sense -- e (2026-10-23) o Desperto do passo S4 e o ki divino do proprio jogador. O que
/// cada um ve e producao: `SentidosDe`, `FichaVisivel` e o tique de 1 Hz.
/// </summary>
public partial class GameServer
{
	/// <summary>O BP do jogador antes do palco mexer -- pra devolver.</summary>
	private double _bpAntesDoPalcoSN = -1;

	/// <summary>
	/// OS CORPOS DO PALCO, a leste do jogador e todos CONHECIDOS dele (a aba escreve o nome de quem se conhece): o Abaixo (60 milhoes) e o
	/// Acima (160 milhoes) pro teto do scouter; o Dourado (Golden Apple) e um Supremo Kaioh NPC pro ki divino; e um corpo por nivel do Sense
	/// -- o Vizinho (4 tiles), o NaRegiao (40), o NoPlaneta (120), o NoEspaco (no sistema da Terra) e o EmNamek. Falso sem jogador.
	/// </summary>
	internal bool PalcoSN_Montar(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu)) return false;
		Vec2 c = eu.Pos;
		ServerPlayer Conhecido(ServerPlayer o)
		{
			eu.Social.Fotografar(o.Assinatura, o.Name, o.Race, "Male", 25, NowMs());
			return o;
		}
		Conhecido(StCorpo("Abaixo", Em(c, 3, -2), 60_000_000));
		Conhecido(StCorpo("Acima", Em(c, 5, -2), 160_000_000));
		Conhecido(StCorpo("Dourado", Em(c, 7, -2), 20_000)).Ficha.Class = Jandirus.Core.Forms.DespertarDivino.ClasseDeKiDivino;
		if (_moldes?.Todos.FirstOrDefault(m => m.Id == "supremo_kaioh") is { } mk)
		{
			ServerPlayer k = StCorpo("Kaioh", Em(c, 9, -2), 20_000);
			k.Papel = new PapelDeNpc(mk, 3);
			k.Conta = "";
		}
		Conhecido(StCorpo("Vizinho", Em(c, 4, 2), 9_000));
		Conhecido(StCorpo("NaRegiao", Em(c, 40, 0), 9_000));
		Conhecido(StCorpo("NoPlaneta", Em(c, 120, 0), 9_000));
		ServerPlayer noEspaco = Conhecido(StCorpo("NoEspaco", c, 9_000));
		PlanetaNoEspaco terra = Espaco.PreFeitos().First(p => string.Equals(p.Nome, "Earth", StringComparison.OrdinalIgnoreCase));
		ZoneList(noEspaco.Zone.Hash).Remove(noEspaco);
		noEspaco.Zone = ZonaDoEspaco;
		noEspaco.Pos = terra.Pos + new Vec2((float)terra.Raio * 2, 0);
		ZoneList(noEspaco.Zone.Hash).Add(noEspaco);
		Conhecido(StCorpoEm("EmNamek", "Namek", 9_000));
		return true;
	}

	/// <summary>Liga ou desliga o scouter do jogador -- o bit `Poder.Scouter`, como o equipar o acende.</summary>
	internal void PalcoSN_Scouter(int id, bool ligado)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		if (ligado) pl.PoderesConcedidos |= Protocol.Poder.Scouter;
		else pl.PoderesConcedidos &= ~Protocol.Poder.Scouter;
		AplicarPoderes(pl);
		MandarFicha(pl);
	}

	/// <summary>O BP base do jogador (pra ficha passar do teto do scouter); negativo devolve o de antes.</summary>
	internal void PalcoSN_MeuPoder(int id, double bp)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		if (bp < 0)
		{
			if (_bpAntesDoPalcoSN < 0) return;
			bp = _bpAntesDoPalcoSN;
			_bpAntesDoPalcoSN = -1;
		}
		else if (_bpAntesDoPalcoSN < 0) _bpAntesDoPalcoSN = pl.Ficha.BP;
		pl.Ficha.BP = bp;
		pl.Ficha.PowerLevel();
		MandarFicha(pl);
	}

	/// <summary>Manda a ficha de novo -- o robo liga um defeito injetado e quer ver a ficha que ele produz.</summary>
	internal void PalcoSN_Ficha(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) MandarFicha(pl);
	}

	/// <summary>A skill Sense no livro do jogador, no nivel pedido (o bit `Poder.Sense` acende pelo `AplicarPoderes`).</summary>
	internal void PalcoSN_Sense(int id, int nivel)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Livro ??= new Jandirus.Core.Skills.SkillBook();
		if (!pl.Livro.Sabe(Jandirus.Core.Skills.SenseEmNiveis.Path)) pl.Livro.Dar(Jandirus.Core.Skills.SenseEmNiveis.Path);
		AplicarPoderes(pl);
		pl.Niveis.Por(Jandirus.Core.Skills.SenseEmNiveis.Path, nivel);
		MandarSkills(pl, forcar: true);
	}

	/// <summary>
	/// (2026-10-23) O DESPERTO DO PASSO S4: um corpo CONHECIDO a um tile e pouco do jogador, com o ki divino despertado pelo funil do Core
	/// (`DespertarDivino.Despertar`) e na forma base -- quem so despertou, que o Sense de quem nao tem God Ki deixou de sentir
	/// (`KiDivinoForaDoSense`). Falso sem jogador, ou se o corpo nao saiu desperto e na base.
	/// </summary>
	internal bool PalcoSN_Desperto(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu)) return false;
		ServerPlayer d = StCorpo("Desperto", Em(eu.Pos, 1, 1), 20_000);
		eu.Social.Fotografar(d.Assinatura, d.Name, d.Race, "Male", 25, NowMs());
		Jandirus.Core.Forms.DespertarDivino.Despertar(d.Ficha);
		return Jandirus.Core.Forms.DespertarDivino.JaDespertou(d.Ficha) && d.Forma.NaBase;
	}

	/// <summary>O ki divino do jogador antes do passo S4 -- pra devolver.</summary>
	private Jandirus.Core.Stats.GodKiState? _godKiAntesDoPalcoSN;
	private bool _palcoSNDespertou;

	/// <summary>
	/// (2026-10-23) O ki divino DESPERTO no proprio jogador (o fim do passo S4: quem tem God Ki sente o Desperto), ou de volta ao que era. So
	/// o `awakened` -- o medidor e o marco do despertar nao entram na ficha do robo.
	/// </summary>
	internal void PalcoSN_Despertar(int id, bool desperto)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		if (desperto && !_palcoSNDespertou)
		{
			_godKiAntesDoPalcoSN = pl.Ficha.godki;
			pl.Ficha.godki = new Jandirus.Core.Stats.GodKiState { awakened = true };
			_palcoSNDespertou = true;
		}
		else if (!desperto && _palcoSNDespertou)
		{
			pl.Ficha.godki = _godKiAntesDoPalcoSN;
			_godKiAntesDoPalcoSN = null;
			_palcoSNDespertou = false;
		}
	}

	/// <summary>Tira os corpos do palco, o scouter e a skill, e devolve o BP (e o ki divino do passo S4).</summary>
	internal void PalcoSN_Limpar(int id)
	{
		PalcoSN_Despertar(id, false);
		PalcoSN_MeuPoder(id, -1);
		PalcoSN_Scouter(id, false);
		if (_players.TryGetValue(id, out ServerPlayer? pl) && pl.Livro?.Sabe(Jandirus.Core.Skills.SenseEmNiveis.Path) == true)
		{
			pl.Livro.Esquecer(Jandirus.Core.Skills.SenseEmNiveis.Path);
			AplicarPoderes(pl);
		}
		LimparTudoDaBancada();
	}
}
