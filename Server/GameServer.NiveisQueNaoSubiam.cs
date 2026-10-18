using Jandirus.Core.Skills;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// AS SKILLS QUE NAO TINHAM COMO SUBIR DE NIVEL (adendo 18, 2026-10-16) -- o dono: *"tem varias skills q n tem como subir de nivel,
/// traga pro port formas de upar elas, tipo ki effusion etc"*.
///
/// O censo (o `niveis.json` contra o que o servidor credita) achou as skills de degrau sem NENHUMA fonte de exp viva: o DM as treinava
/// por um caminho que o extrator nao le, ou que o port nunca ligou. As fontes, cada uma onde o evento acontece:
///
///   * as tres KI EFFUSION: o SOMATORIO de oito contadores (`effusioncounter`, `Effusion.dm:74`) -- `NiveisDeSkill.ContadorDaEfusao`,
///     creditado no funil `CreditarContador`; e o `chargedcounter` do tiro carregado (`blasts.dm:101`), que ninguem creditava;
///   * o SPIRIT GUN e o SPIRIT FIST: um ponto por uso (`SpiritBallFireCount`, `SpiritFistFireCount`: `Spirit.dm:352`, `:447`), no
///     proprio verb (G7, G2);
///   * a RAPID MOVEMENT: virar o corpo (`savant.dir != lastdir`, `speedy.dm:99`) -- o estado `Virando` do efetor;
///   * a lamina, a espada e o escudo de Ki (`Blade_Runner`, `Ki_Knight`, `Ki_Shield` -- `Cultivation.dm`): de pe com a ferramenta,
///     os estados `ComLaminaDeKi`, `ComEspadaDeKi`, `ComEscudoDeKi`;
///   * o FULL POWER dos Grays: queimar Ki (`kiblock`, `grays.dm:24-35`) -- aqui;
///   * a FORMA GIGANTE do Namekuseijin: o banco de exp do gigante (`Giant Form.dm:34-38`) -- aqui.
///
/// E O EFEITO DO NIVEL, onde o port tinha um numero cravado no lugar dele: a mordida da lamina e da espada
/// (<see cref="MordidaDaArmaDeKi"/>), o custo do escudo (<see cref="CustoDoEscudoPeloNivel"/>) e o folego do gigante
/// (`Fighter.giant_form_efficiency`).
///
/// FICARAM DE FORA, e por que: o voo (`/datum/skill/flying` -- o port nao concede a skill: o voo abre no nivel 50 do Ki Unlocked,
/// `DegrausDoPort`), a skill do Kaio-ken (a maestria do port e outra, `Core/Skills/Kaioken.cs`), as tres de espada (`Swordmaster`,
/// `Sword_Routine`, `Weighted_Sword_Style` -- o port nao tem espada pra empunhar) e a `Tree_Mastery` (fechada), a `Magic_Absorption`
/// (o degrau dela nao faz nada), as tecnicas customizadas (sem nivel no port) e as do Makyo e das formas (o nivel delas nao e lido
/// por nada).
/// </summary>
public sealed partial class GameServer
{
	internal const string PathDoSpiritGun = "/datum/skill/Spirit_Ball";
	internal const string PathDoSpiritFist = "/datum/skill/Spirit_Fist";
	internal const string PathDaRapidMovement = "/datum/skill/rapidmovement";
	internal const string PathDaLamina = "/datum/skill/Cultivation/Blade_Runner";
	internal const string PathDaEspada = "/datum/skill/Cultivation/Ki_Knight";
	internal const string PathDoEscudoDeKi = "/datum/skill/Cultivation/Ki_Shield";
	internal const string PathDoFullPower = "/datum/skill/fullpower";
	internal const string PathDoGigante = "/datum/skill/namek/bigform";

	/// <summary>O `var/kiblock` do Full Power (`grays.dm:20`): quanto Ki ainda falta queimar pro proximo ponto.</summary>
	private readonly Dictionary<int, double> _kiblockDoFullPower = [];

	/// <summary>O `var/tmp/lastki` do Full Power (`grays.dm:21`).</summary>
	private readonly Dictionary<int, double> _ultimoKiDoFullPower = [];

	/// <summary>O `var/expbuffer` do gigante (`Giant Form.dm:10`).</summary>
	private readonly Dictionary<int, double> _bancoDoGigante = [];

	/// <summary>
	/// DEFEITO INJETADO (`--niveisteste`): os efeitos de nivel voltam aos numeros cravados de antes -- a mordida da lamina e da espada
	/// inteira, o escudo pelo preco cheio, o gigante a 1. Falso em jogo, sempre.
	/// </summary>
	internal static bool EfeitosDeNivelCravadosDeTeste;

	private void EsquecerNiveisDoAdendo18(int id)
	{
		_kiblockDoFullPower.Remove(id);
		_ultimoKiDoFullPower.Remove(id);
		_bancoDoGigante.Remove(id);
	}

	/// <summary>
	/// AS DUAS FONTES COM ESTADO PROPRIO, no tique do efetor (5 Hz, `TicarNiveisDe`) e ANTES dele -- o ponto deste tique conta na subida
	/// deste tique.
	/// </summary>
	private void FontesDeExpComEstado(ServerPlayer pl)
	{
		if (NiveisDeSkill.FontesDoAdendo18DesligadasDeTeste || pl.Livro == null) return;
		Fighter f = pl.Ficha;

		// O FULL POWER, so no nivel 0 (o `switch(level)` do DM so treina ali): cada Ki que CAI conta, e a cada `round(1000/exp)`
		// queimados sobe um ponto -- os blocos encolhem (1000, 1000, 500, 333...). O exp do DM nasce em 1 (`exp = 1`) e o daqui em 0:
		// o bloco e medido pelo exp DEPOIS do ponto, que e o do DM antes dele. DIVERGENCIA DECLARADA: o `kiblock` do DM vai pro save;
		// aqui e de sessao (quem reloga perde o pedaco de bloco que faltava).
		if (pl.Livro.Sabe(PathDoFullPower) && pl.Niveis.Nivel(PathDoFullPower) == 0)
		{
			double antes = _ultimoKiDoFullPower.GetValueOrDefault(pl.Id, f.Ki);
			_ultimoKiDoFullPower[pl.Id] = f.Ki;
			double bloco = _kiblockDoFullPower.GetValueOrDefault(pl.Id, 1000);
			if (f.Ki < antes) bloco -= antes - f.Ki;
			if (bloco <= 0 && pl.Niveis.Creditar(PathDoFullPower, 1))
				bloco = Math.Round(1000 / Math.Max(pl.Niveis.Exp(PathDoFullPower), 1));
			_kiblockDoFullPower[pl.Id] = bloco;
		}

		// A FORMA GIGANTE: `if(level < 2 && bigform) { expbuffer += rand(1,7); if(expbuffer >= 20) { exp += 1*giant_form_efficiency;
		// expbuffer = 0 } }` (`Giant Form.dm:34-38`).
		if (pl.Livro.Sabe(PathDoGigante) && pl.Niveis.Nivel(PathDoGigante) < 2 && TemBuff(pl, BuffGiganteG2))
		{
			double banco = _bancoDoGigante.GetValueOrDefault(pl.Id) + _rng.Next(1, 8);
			if (banco >= 20)
			{
				pl.Niveis.Creditar(PathDoGigante, 1 * f.giant_form_efficiency);
				banco = 0;
			}
			_bancoDoGigante[pl.Id] = banco;
		}
	}

	/// <summary>
	/// A MORDIDA DA LAMINA E DA ESPADA DE KI pelo nivel da skill que as da -- o `superdrain` (`Cultivation.dm:69-79` e `:187-197`): o
	/// preco cheio no nivel 0, metade no 1, nada no 2. DIVERGENCIA DECLARADA: no DM o efetor copia o `superdrain` pro `KiBladeDrain`
	/// num `prob(10)` por tique (a mordida cai alguns tiques depois da subida); aqui ela le o nivel na hora.
	/// </summary>
	private double MordidaDaArmaDeKi(ServerPlayer pl, string idDaArma) =>
		EfeitosDeNivelCravadosDeTeste ? 1
		: FatorDoSuperdrain(pl.Niveis.Nivel(idDaArma == "Ki_Sword" ? PathDaEspada : PathDaLamina));

	/// <summary>
	/// O CUSTO DO ESCUDO DE KI pelo nivel da `Ki_Shield`: inteiro, metade, nada -- o que as frases dos degraus prometem (*"Ki shield drain
	/// has been halved!"*). DIVERGENCIA DECLARADA: no DM o efetor do escudo escreve o `KiBladeDrain` (`Cultivation.dm:335`, a linha
	/// copiada da lamina) e o escudo nunca barateia; aqui barateia o escudo.
	/// </summary>
	private double CustoDoEscudoPeloNivel(ServerPlayer pl) =>
		EfeitosDeNivelCravadosDeTeste ? 1 : FatorDoSuperdrain(pl.Niveis.Nivel(PathDoEscudoDeKi));

	/// <summary>O `superdrain` de cada nivel (`Cultivation.dm`): 1, 0,5, 0.</summary>
	private static double FatorDoSuperdrain(int nivel) => nivel >= 2 ? 0 : nivel >= 1 ? 0.5 : 1;
}
