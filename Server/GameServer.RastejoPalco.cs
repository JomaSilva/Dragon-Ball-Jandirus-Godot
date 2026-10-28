using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// OS GANCHOS DO ROBO DO RASTEJO (`--diagrastejo`, `Client/RoboDoRastejo.cs`). SO MONTAM A CENA E LEEM O ESTADO: a camara e assentada
/// pelo funil de producao (`MaquinaDaFoto`, da `--diagmaquinas`), quem derruba e o tique de producao da gravidade, e o passo e o do
/// jogador pela tela -- a tecla, a previsao do cliente e o `AplicarInput`.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A CAMARA DA CENA LIGADA em `grav`, com alcance `alcance`, aparafusada (a tela a desenha presa) e o campo relido na hora (sem os
	/// cinco segundos da troca: o que a cena mostra e o rastejo, e nao o menu). O estado de gravidade nasce no primeiro `grav_info`,
	/// como na `--maquinasteste` -- pela boca do dono, que ve a frase no chat.
	/// </summary>
	internal bool CampoDoRastejo(int idDono, int obraId, double grav, int alcance)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono) || ObraDaFotoDasMaquinas(obraId) is not { } o) return false;
		if (o.Gravidade == null) ComandoDeInteracao(dono, "grav_info", "");
		if (o.Gravidade is not { } g) return false;
		if (!o.Aparafusada)
		{
			o.Aparafusada = true;
			MandarObras(o.Zona);
		}
		g.Range = alcance;
		g.Energia = Math.Max(g.Energia, 1);
		g.EnergiaMax = Math.Max(g.EnergiaMax, g.Energia);
		g.Grav = grav;
		g.MudaEm = 0;
		g.Pedida = 0;
		g.Cheias = null;
		LerOsCampos();
		return Math.Abs(g.Grav - grav) < 1e-9;
	}

	/// <summary>
	/// OS HABITANTES DE PERTO SAEM DA CENA -- o remedio da `--diagraio` (o acaso das bancadas): um cidadao passeando no caminho do rastejo
	/// pararia o corpo (ninguem atravessa ninguem) e a regua do passo mediria o habitante, e nao o rastejo. Sai quem nao tem dono, a menos
	/// de `tiles` do corpo (pelo `RemoverNpc`, que avisa a tela); o povoamento so repoe de cinco em cinco minutos. Devolve quantos sairam.
	/// </summary>
	internal int SemHabitantesNaCenaDoRastejo(int id, int tiles)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		float raio = tiles * ZoneCollision.TileSize;
		int n = 0;
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash).ToList())
			if (o != pl && o.Peer == null && (o.Pos - pl.Pos).LengthSquared < raio * raio) { RemoverNpc(o); n++; }
		return n;
	}

	/// <summary>O CORPO DA CENA NA BEIRA DO DESMAIO: o proximo tique de 1 Hz da gravidade o derruba (o mesmo preparo da `--rastejoteste`).</summary>
	internal bool NaBeiraDoDesmaioNaFoto(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate == null) return false;
		NaBeiraDoDesmaioRj(pl);
		pl.CorpoEnviado = "";
		return true;
	}

	/// <summary>
	/// (2026-10-28, o pedido 143) O PRESO DE PE BLINDADO PELA CARENCIA: acima de 5x a maestria o nocaute e na hora, e a razao 8 da cena A
	/// derrubaria o corpo no primeiro segundo (e o mataria no quinto). Com a carencia (o `Blindado` do funil sem algoz) o chao continua
	/// prendendo, avisando e cansando -- so o dano e o nocaute ficam de fora. Zero tira a carencia: a cena B precisa do tique que derruba.
	/// </summary>
	internal void CarenciaNaCenaDoRastejo(int id, double segundos)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate != null) pl.Combate.Carencia = segundos;
	}

	/// <summary>O que o SERVIDOR sabe do corpo da cena: o desmaio e a marca dele, o rastejo, o passo, a razao, o campo, onde esta, quantas
	/// correcoes ja levou, se corre e pra onde a cabeca deitada aponta.</summary>
	internal (bool Existe, bool Ko, bool DaGravidade, bool SoRasteja, float Passo, double Razao, double Campo, Vec2 Pos, int Correcoes,
		bool Correndo, Facing Cabeca) RastejoNaFoto(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl)
			? (true, pl.Ficha.KO, pl.Combate?.DesmaioDaGravidade == true, SoAGravidadeSegura(pl), pl.SpeedStat, Esmagamento.Razao(pl.Ficha),
			   pl.Ficha.gravmult, pl.Pos, pl.Corrections, pl.Correndo, pl.DirecaoDeitado)
			: default;

	/// <summary>A cena acabou: o corpo inteiro e de pe (o `Curar` de sempre), e a camara sai com o resto da foto das maquinas.</summary>
	internal void LimparOPalcoDoRastejo(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate != null)
		{
			pl.Combate.Curar();
			pl.CorpoEnviado = "";
		}
		LimparAFotoDasMaquinas();
	}
}
