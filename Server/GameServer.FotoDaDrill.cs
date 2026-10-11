using Jandirus.Core.Tech;

namespace Jandirus.Server;

/// <summary>
/// OS GANCHOS DA FOTO DA DRILL (`--diagdrill`, `Client/RoboDaDrill.cs`).
///
/// SO MONTAM A CENA E LEEM O ESTADO. A Drill e assentada pelo `posicionar` (o `MaquinaDaFoto` da foto das
/// maquinas, que ja serve pra qualquer tipo); quem coleta e melhora e a bancada, PELA TELA -- o menu da tecla
/// E -- e quem conta a producao e o codigo de producao. A regra que a `--drillteste` segue.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// O PALCO: a dona com zeni e recursos pra pagar as duas melhorias da cena, e a Drill com um tanto de
	/// trabalho DEVIDO -- o carimbo dela volta <paramref name="segundosDevidos"/>, e a proxima conta (a de
	/// producao, quando o painel do menu perguntar) paga esse tempo. Sem isto a foto do menu mostraria um
	/// estoque de tres recursos, e a coleta nao teria o que mostrar.
	/// </summary>
	internal bool PalcoDaDrillNaFoto(int idDono, int obraId, double zeni, double recursos, double segundosDevidos)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? pl) || ObraDaFotoDasMaquinas(obraId) is not { } o) return false;

		DrillDaObra d = EstadoDaDrill(o, NowMs());
		d.ContadoAteMs -= (long)(segundosDevidos * 1000);
		pl.Ficha.Zeni = zeni;
		pl.Ficha.Recursos = recursos;
		return true;
	}

	/// <summary>A Drill da cena e os dois saldos da dona, na conta do SERVIDOR. A reserva e a fracao do planeta (-1 = sem reserva).</summary>
	internal (bool Existe, int Nivel, double Estoque, double Zeni, double Recursos, double Reserva) DrillNaFoto(int idDono, int obraId)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? pl) || ObraDaFotoDasMaquinas(obraId) is not { Drill: { } d } o) return default;
		return (true, d.Nivel, d.Estoque, pl.Ficha.Zeni, pl.Ficha.Recursos, ReservaDe(o.Zona)?.Fracao ?? -1);
	}
}
