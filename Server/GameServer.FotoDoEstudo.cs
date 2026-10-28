using Jandirus.Core.Tech;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-27) AS PORTAS DO ROBO DE FOTOS DO ESTUDO (`--diagestudo`, o pedido 120): pôr uma Research Station aparafusada ao lado do jogador,
/// abrir um minijogo de um tipo (e de uma molecula escolhida) sem esperar o minuto, e ler a resposta e o bonus. So o robo as chama.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Uma Research Station aparafusada nos pes do jogador (se ainda nao ha uma ao alcance), e o estudo ligado.</summary>
	internal void PrepararEstudoNaFoto(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		if (BancadaDeEstudoPerto(pl) == null)
		{
			var mesa = new Obra { Id = 990_830, Tipo = "Research_Station", X = pl.Pos.X + 24, Y = pl.Pos.Y, DonoNome = pl.Name, Aparafusada = true };
			mesa.PorZona(pl.Zone);
			_noChao.Add(mesa);
		}
		pl.Estudando = true;
	}

	/// <summary>Abre um minijogo deste tipo agora; com <paramref name="molecula"/>, a molecula desta formula.</summary>
	internal void AbrirMinijogoNaFoto(int id, TipoDeMinijogo tipo, string molecula = "")
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		Minijogo m = AbrirMinijogo(pl, tipo);
		if (tipo == TipoDeMinijogo.Molecula && molecula.Length > 0)
		{
			m.Indice = Math.Max(0, Array.FindIndex(Minijogos.Moleculas, x => x.Formula == molecula));
			MandarMinijogo(pl, m, 0);
		}
	}

	/// <summary>A resposta certa do minijogo aberto (vazio sem minijogo, ou na molecula).</summary>
	internal string RespostaNaFoto(int id) => _minijogoDoEstudo.TryGetValue(id, out Minijogo? m) ? m.Resposta : "";

	/// <summary>Quantos bonus de estudo valem.</summary>
	internal int BonusNaFoto(int id) => BonusDoEstudoAtivos(id);
}
