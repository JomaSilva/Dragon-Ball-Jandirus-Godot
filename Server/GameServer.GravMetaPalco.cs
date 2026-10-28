using Jandirus.Core.Items;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diaggravmeta` (2026-10-23) -- ver `Client/RoboDaGravMeta.cs`. O robo e o servidor rodam no mesmo processo
/// (`--host`): o palco poe o corpo local em cada estado -- o peso vestido acima de 8x o limite do corpo, a gravidade dominada, o fantasma no
/// Inferno -- pelas portas de producao (o "ajustar" do peso, a troca de zona). O resto e producao: a ficha lenta, a recusa do treino, o
/// tique de 1 Hz do esmagamento e o chat. As regras e os contra-exemplos sao da `--gravmetateste`; aqui e a tela.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O peso de bancada do robo: 20x o limite do corpo, entao a porcentagem do verbo vira a razao dividida por 5 (43% = 8,6x).</summary>
	private string? _pesoDoPalcoGm;

	/// <summary>O corpo vivo na Terra, parado, com o BP dado e a maestria de gravidade em 1. Falso sem o jogador.</summary>
	private bool PalcoGm_Vivo(int id, out ServerPlayer pl)
	{
		if (!_players.TryGetValue(id, out pl!)) return false;
		if (pl.Ficha.dead) pl.Combate.Reviver();
		pl.Combate.Curar();
		pl.MorteJaViajou = false;
		ZoneKey terra = ZoneKey.Premade("Earth");
		if (!pl.Zone.Equals(terra)) MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
		DefinirAtividade(pl, Jandirus.Net.Protocol.Activity.Parado);
		pl.Combate.LutandoDeVerdade = pl.Combate.EmCombate = pl.Combate.BrigandoComAlguem = 0;
		pl.Ficha.GravMastered = 1;
		return true;
	}

	/// <summary>Veste o peso de bancada (20x o limite do corpo) em <paramref name="pct"/> pelo verbo de producao. Falso sem o jogador.</summary>
	internal bool PalcoGm_Peso(int id, string pct)
	{
		if (!PalcoGm_Vivo(id, out ServerPlayer pl)) return false;
		Fighter f = pl.Ficha;
		if (_pesoDoPalcoGm == null || pl.Mochila.Quantos(_pesoDoPalcoGm) <= 0)
		{
			_pesoDoPalcoGm = PesoDeTreino.IdDe(f.weight_cap_hw * 20, "");
			pl.Mochila.Guardar(_pesoDoPalcoGm);
			MandarMochila(pl);
		}
		AjustarPeso(pl, _pesoDoPalcoGm, pct);
		f.stamina = f.maxstamina;
		pl.SigAtributos = "";
		MandarAtributos(pl);
		return true;
	}

	/// <summary>Sem peso, dominando <paramref name="maestria"/>x num chao de 1x (a Terra). Falso sem o jogador.</summary>
	internal bool PalcoGm_Aclimatado(int id, double maestria)
	{
		if (!PalcoGm_Vivo(id, out ServerPlayer pl)) return false;
		if (_pesoDoPalcoGm != null && pl.Ficha.Weighted > 0) AjustarPeso(pl, _pesoDoPalcoGm, "0");
		pl.Ficha.GravMastered = maestria;
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel(agoraMs: NowMs());
		pl.SigAtributos = "";
		MandarAtributos(pl);
		return true;
	}

	/// <summary>
	/// O FANTASMA NO PORTAO DO INFERNO (10x), dominando 1x e sem peso: de pe, de aureola, sem pena correndo. O tique de 1 Hz faz o resto
	/// (o aperto, o dano, o aviso). Falso sem o jogador.
	/// </summary>
	internal bool PalcoGm_MortoNoInferno(int id)
	{
		if (!PalcoGm_Vivo(id, out ServerPlayer pl)) return false;
		if (_pesoDoPalcoGm != null && pl.Ficha.Weighted > 0) AjustarPeso(pl, _pesoDoPalcoGm, "0");
		pl.Ficha.dead = true;
		pl.Ficha.KO = false;
		pl.MorteJaViajou = true;
		pl.RelogioDaMorte = long.MaxValue;
		pl.Ficha.hell_lockout_until = 0;
		pl.Karma = Math.Max(pl.Karma, 0);
		ZoneKey inferno = ZoneKey.Premade(Alem.ZonaDoInferno);
		MoveToZone(pl.Id, inferno, PortaoDoInferno(inferno));
		pl.Ficha.GravMastered = 1;
		_avisoDeEsmagamento.Remove(pl.Id);
		MandarFicha(pl);
		return true;
	}

	/// <summary>
	/// (2026-10-27) VIVO E SEM PESO EM VEGETA (10x), dominando 1x: a gravidade passa de 4x a maestria e o treino nao rende (o pedido 117). O
	/// tique do chao escreve a gravidade sentida, e a ficha lenta leva o zero. Falso sem o jogador.
	/// </summary>
	internal bool PalcoGm_GravidadeDemais(int id)
	{
		if (!PalcoGm_Vivo(id, out ServerPlayer pl)) return false;
		if (_pesoDoPalcoGm != null && pl.Ficha.Weighted > 0) AjustarPeso(pl, _pesoDoPalcoGm, "0");
		ZoneKey vegeta = ZoneKey.Premade("Vegeta");
		MoveToZone(pl.Id, vegeta, PontoDeNascimento(vegeta));
		pl.Ficha.GravMastered = 1;
		// (2026-10-28, o pedido 143) BLINDADO PELA CARENCIA: a 10x contra 1x o nocaute e na hora (e a morte no quinto segundo), e a cena e a
		// do painel de quem esta de pe. O `PalcoGm_Vivo` da cena seguinte a tira (o `Curar`).
		pl.Combate.Carencia = 60;
		pl.SigAtributos = "";
		MandarAtributos(pl);
		return true;
	}

	/// <summary>O corpo de volta vivo, inteiro e sem o peso de bancada, na Terra.</summary>
	internal void PalcoGm_Limpar(int id)
	{
		if (!PalcoGm_Vivo(id, out ServerPlayer pl)) return;
		if (_pesoDoPalcoGm != null)
		{
			if (pl.Ficha.Weighted > 0) AjustarPeso(pl, _pesoDoPalcoGm, "0");
			pl.Mochila.Tirar(_pesoDoPalcoGm);
			MandarMochila(pl);
		}
		_pesoDoPalcoGm = null;
		MandarFicha(pl);
	}
}
