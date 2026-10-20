using Jandirus.Core.Combat;
using Jandirus.Core.Races;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DA `--diaglimite` (`Client/RoboDoLimite.cs`, 2026-10-19): o Shattering the Limit so no degrau SSJ4, e o raio dele VERMELHO.
/// O robo e o jogador do host; o palco so o deixa na condicao da foto -- o sangue (Primal ou Lendario Primal), a forma sem cinematica, a
/// cor do ki cravada e o Ki do tiro -- e quem atira e o BOTAO (`GatilhoDaVariedade` -> `UsarHabilidade`, a porta do clique).
/// As regras sao da `--passivasteste` (familia C).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O SANGUE do robo: Primal ou Lendario Primal -- a classe e a linhagem da ficha, que e de onde o kit sai (`KitDoCorpo`).</summary>
	internal void PalcoL_Sangue(int id, bool lendario)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Class = pl.Ficha.Class = lendario ? Passivas.ClasseLendariaPrimal : "Normal Primal Saiyan";
		pl.Ficha.SaiyanLineage = "Primal Saiyan";
	}

	/// <summary>
	/// A COR DO KI CRAVADA: o APPDATA limpo da bancada recria o personagem a cada rodada e sorteia a cor (`CorDoTiro.DeSemente`) -- uma
	/// rodada que sorteasse vermelho fotografaria o raio comum da cor do Shattering. Pelo mesmo caminho do `ca_cor`.
	/// </summary>
	internal void PalcoL_CorDoKi(int id, Jandirus.Core.Appearance.Rgb cor)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Visual.CorKi = cor;
		ReapresentarAparencia(pl);
	}

	/// <summary>
	/// A FORMA, SEM CINEMATICA -- o funil do `admin_forma` (`Entrar`, o tanque cheio, `AplicarForma`) com o anuncio de producao sem cena:
	/// a cena prenderia o corpo e o tiro sairia depois dela. Devolve a forma em que o corpo ficou.
	///
	/// E DOMINADA: a forma lendaria sem maestria TOMA O CORPO (a furia lendaria, "o seu corpo nao te obedece"), e o corpo possuido vira pra
	/// onde quer -- na r3 o Kamehameha da LSSJ4 FP saiu pro sul. A maestria cheia e o que a impede, como no jogo.
	/// </summary>
	internal string PalcoL_Forma(int id, string forma)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return "";
		string anterior = pl.Forma.Atual;
		if (anterior == forma) return anterior;
		if (forma != Jandirus.Core.Forms.Catalogo.IdBase) pl.Forma.Maestria.Por(forma, 100);
		bool estreia = pl.Forma.Entrar(forma);
		pl.Ficha.Ki = Math.Max(pl.Ficha.MaxKi, pl.Ficha.Ki);
		AplicarForma(pl);
		AnunciarForma(pl, anterior, forma, estreia, semCena: true);
		return pl.Forma.Atual;
	}

	/// <summary>
	/// O TIRO QUE CUSTA <paramref name="fracao"/> DO KI DE AGORA, pelo botao. O custo e MEDIDO no proprio verbo (aperta com o tanque cheio, le
	/// o que ele cobrou e desfaz o canal) e o Ki fica em custo / fracao antes do aperto de verdade -- o Shattering pede 10% ou mais, menos no
	/// Kamehameha x10. Devolve o que o servidor respondeu ao aperto de verdade e o custo.
	/// </summary>
	internal (string Resposta, double Custo) PalcoL_Atirar(int id, string verbo, double fracao)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return ("corpo fora do mundo", 0);
		RegarOKiDaVariedade(id);
		LimparOsTirosDaBoca(id);
		ReporNaBoca(id);
		// SEM ALVO MARCADO: com alguem marcado o tiro sai NELE e vira o corpo (`RumoDoTiro`) -- na r1 um NPC que passou falando levou o
		// Galick Ho pro oeste, pra fora da janela medida
		pl.AlvoId = 0;
		double cheio = pl.Ficha.Ki;
		GatilhoDaVariedade(id, verbo);
		double custo = cheio - pl.Ficha.Ki;
		LimparOsTirosDaBoca(id);
		pl.Ficha.Ki = custo / fracao;
		_ultimaFala.Remove(id);   // (o grito do tiro nao e engolido pela fala da medida -- `MsEntreFalas`)
		return (GatilhoDaVariedade(id, verbo), custo);
	}

	/// <summary>
	/// O RAIO NA MAO, na conta do servidor: o id (o nome do node no cliente e `Tiro{id}`), a marca do vermelho, a onda, quanto andou e o
	/// RUMO (o robo mede a tinta do corpo pra leste; um raio virado pra outro lado nao e o que a foto mostra).
	/// </summary>
	internal (bool Achou, int Id, bool DoLimite, double Onda, double Andou, Vec2 Rumo) PalcoL_Raio(int id) =>
		_canais.TryGetValue(id, out CanalDeKi? c) && c.Raio is { Vivo: true } r
			? (true, r.Id, r.DoLimite, r.MultDeOnda, r.AndouTiles, r.Rumo)
			: (false, 0, false, 0, 0, default);

	/// <summary>
	/// O PALCO SEM POVO: os habitantes a menos de 30 tiles do robo saem do mundo (`RemoverNpc`). Na r2 um cidadao que passou na frente do
	/// raio levou o tiro e revidou (`Kakarot NOCAUTEOU Roshi`), e do L08 em diante os raios sairam pro norte e pro sul. Devolve quantos sairam.
	/// </summary>
	internal int PalcoL_SemPovo(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		float longe = 30 * ZoneCollision.TileSize;
		List<ServerPlayer> perto = ZoneList(pl.Zone.Hash).Where(o => o.Peer == null && o != pl && Vec2.Distance(o.Pos, pl.Pos) < longe).ToList();
		foreach (ServerPlayer o in perto) RemoverNpc(o);
		return perto.Count;
	}

	/// <summary>
	/// A FORMA SEGURA NA ESPERA: o tanque cheio de novo, e a forma de volta se ela caiu. Sem maestria, a LSSJ4 FP esvaziou o Ki nos 0,8 s
	/// que o robo espera o cliente pintar a forma, e a r1 atirou da base. Devolve a forma em que o corpo esta.
	/// </summary>
	internal string PalcoL_SegurarForma(int id, string forma)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return "";
		pl.Ficha.Ki = Math.Max(pl.Ficha.MaxKi, pl.Ficha.Ki);
		return pl.Forma.Atual == forma ? forma : PalcoL_Forma(id, forma);
	}

	/// <summary>Ainda carregando (o canal aberto e o raio por nascer)?</summary>
	internal bool PalcoL_Carregando(int id) => _canais.TryGetValue(id, out CanalDeKi? c) && c.Raio == null;

	/// <summary>A forma, o kit e o degrau SSJ4 que o servidor ve no corpo -- a linha de cada cena no log.</summary>
	internal string PalcoL_Ficha(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl)
			? $"forma {pl.Forma.Atual}, kit {pl.Racial.Entrada.Kit}, degrau SSJ4 {pl.Racial.Entrada.DegrauSsj4}, Ki {pl.Ficha.Ki:0}/{pl.Ficha.MaxKi:0}"
			: "corpo fora do mundo";

	/// <summary>Fecha o canal, apaga os tiros e volta o corpo pra base.</summary>
	internal void PalcoL_Limpar(int id)
	{
		LimparOsTirosDaBoca(id);
		PalcoL_Forma(id, Jandirus.Core.Forms.Catalogo.IdBase);
	}
}
