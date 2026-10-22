using Jandirus.Core.Combat;
using Jandirus.Core.Tech;

namespace Jandirus.Server;

/// <summary>
/// A TRAVA DE SEGURANCA DA MAQUINA DE GRAVIDADE (dono, 2026-10-21).
///
/// Comprada uma vez (1 milhao de zeni, ou 2 milhoes de recursos -- o dobro sai sozinho), se qualquer corpo
/// que nao seja uma simulacao desmaiar dentro do campo daquela maquina, por qualquer causa, ela desliga
/// (gravidade 0 e a mudanca pendente cancelada) e avisa quem esta por perto. A simulacao nao desmaia:
/// ela se desfaz, e nao dispara a trava. O esmagamento em si nao muda.
///
/// O funil e o <see cref="CombatState.Nocautear"/>, armado no _Ready. Decisao minha: a trava olha o
/// DESMAIO (a passagem pelo funil), nao um corpo que ja estava no chao quando a gravidade ligou.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada --gravtravateste): a simulacao nasce sem copiar a maestria de gravidade
	/// de quem pediu -- o jogo de antes de 2026-10-21. Lido pela propria linha da copia, em CriarSimulacao.
	/// Falso em jogo, sempre.
	/// </summary>
	internal bool SimulacaoSemAMaestriaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --gravtravateste): a trava comprada nao desliga a maquina quando alguem
	/// desmaia no campo. Lido pela propria linha que zera a gravidade, em <see cref="AoDesmaiarNaGravidade"/>.
	/// Falso em jogo, sempre.
	/// </summary>
	internal bool TravaQueNaoDesligaDeTeste;

	/// <summary>
	/// Alguem desmaiou. Se esta no campo ligado (ou na area de uma mudanca que ainda vai ligar) de uma
	/// maquina com a trava, aquela maquina desliga. Simulacao nao conta.
	/// </summary>
	private void AoDesmaiarNaGravidade(CombatState c)
	{
		ServerPlayer? corpo = null;
		foreach (ServerPlayer pl in _players.Values)
			if (ReferenceEquals(pl.Combate, c)) { corpo = pl; break; }
		if (corpo == null || _simulacoes.ContainsKey(corpo.Id)) return;

		bool mexeu = false;
		foreach (Obra o in _noChao)
		{
			if (o.Gravidade is not { Trava: true } g) continue;
			if (g.Grav <= 0 && g.MudaEm == 0) continue;
			if (!o.Zona.Equals(corpo.Zone)) continue;
			if (g.Cheias == null) Encher(o, g, NowMs());
			if (g.Cheias == null || !CampoDeGravidade.Alcanca(o.X, o.Y, g.Range, corpo.Pos, g.Cheias)) continue;

			if (!TravaQueNaoDesligaDeTeste)
			{
				g.Grav = 0;
				g.MudaEm = 0;
				g.Pedida = 0;
				AnunciarAVista(o, $"A trava de segurança desligou a gravidade: {NomeVisivel(corpo)} desmaiou.");
				mexeu = true;
			}
		}
		if (mexeu) LerOsCampos();
	}

	/// <summary>O bolso da cena do robo, pra pagar a trava sem minerar.</summary>
	internal void ZeniDaFotoDaTrava(int id, double zeni, double recursos)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.Zeni = zeni;
		pl.Ficha.Recursos = recursos;
	}

	/// <summary>
	/// Liga o campo da maquina da foto sem os cinco segundos: a cena ja mostrou o menu, e o que falta
	/// provar na tela e o aviso do desmaio. A gravidade em si continua a do <see cref="LerOsCampos"/>.
	/// </summary>
	internal bool CampoLigadoNaFotoDaTrava(int obraId, double grav)
	{
		Obra? o = null;
		foreach (Obra x in _noChao)
			if (x.Id == obraId) { o = x; break; }
		if (o?.Gravidade is not { } g) return false;
		o.Aparafusada = true;
		MandarObras(o.Zona);   // a tela desenha a maquina presa, e nao solta
		g.Range = Math.Max(g.Range, 2);
		g.Energia = Math.Max(g.Energia, 1);
		g.EnergiaMax = Math.Max(g.EnergiaMax, g.Energia);
		g.Grav = grav;
		g.MudaEm = 0;
		g.Cheias = null;
		LerOsCampos();
		return g.Grav > 0;
	}

	/// <summary>O desmaio da foto, pelo mesmo funil de um soco. A trava, se comprada, ouve daqui.</summary>
	internal bool DesmaiarNaFotoDaTrava(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate == null) return false;
		pl.Combate.Nocautear(30);
		return pl.Ficha.KO;
	}
}
