using Jandirus.Core.Items;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagpesosfreeza` (2026-10-18) -- e o CHEFE DE SAGA DE BANCADA, que a `--chefesteste` tambem usa.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O CHEFE DE SAGA DE BANCADA: o que o `NascerChefeDaSaga` faz com o corpo -- o BP pinado (na media de agora), a caca do planeta, o Ki de
	/// chefe (o cadeado e o piso do tanque) e o degrau 1. A ficha refeita por ultimo: e o `Statify` que aplica o piso do tanque.
	/// </summary>
	private void ArmarComoChefeDeSaga(ServerPlayer npc, MoldeDeNpc molde)
	{
		npc.Papel!.BpsPinados = Sagas.Pinar(molde, MediaDoServidor());
		npc.Papel.DaSaga = true;
		npc.Ficha.KiDeChefe = true;
		npc.Papel.Estagio = 0;
		EntrarNoDegrau(npc, cura: 0, anunciar: false);
		npc.Ficha.Statify();
		npc.Ficha.Ki = npc.Ficha.MaxKi;
	}

	/// <summary>Uma arte do guarda-roupa que nao e a de fabrica (a primeira) e o caminho da folha dela.</summary>
	internal (string Nome, string Caminho) PalcoPF_UmaArte()
	{
		if (_visual == null) return ("", "");
		string nome = PesoDeTreino.Escolhiveis(_visual).Find(e => e.Nome != PesoDeTreino.ArtePadrao).Nome ?? "";
		return (nome, PesoDeTreino.Folha(nome, _visual) ?? "");
	}

	/// <summary>Tira do chao da zona deste jogador o que caiu por perto (o peso que o robo largou).</summary>
	internal void PalcoPF_LimparOChao(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		LargadosDe(pl.Zone.Hash).Clear();
		MandarOChao(pl.Zone);
	}

	/// <summary>
	/// O Freeza de Namek armado como chefe de saga (`ArmarComoChefeDeSaga`), a (dx, dy) tiles do jogador, e um saco do mesmo poder a (sx, sy)
	/// tiles, imposto como presa (`PresaDoRoteiro`, a porta do torneio e do Cell). Devolve os dois ids (zeros se nao deu).
	/// </summary>
	internal (int Freeza, int Saco) PalcoPF_FreezaContraOSaco(int id, float dx, float dy, float sx, float sy)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _moldes?.Get("freeza_namek") is not { } molde) return (0, 0);
		float t = ZoneCollision.TileSize;
		ServerPlayer? freeza = NascerNpc("freeza_namek", pl.Zone, pl.Pos + new Vec2(dx * t, dy * t), 9_800);
		if (freeza?.Papel == null) return (0, 0);
		ArmarComoChefeDeSaga(freeza, molde);
		ServerPlayer saco = FigurantePalcoV21(pl, "Saco de pancada", "Human", new Vec2(sx * t, sy * t), freeza.Ficha.BP);
		freeza.Papel.PresaDoRoteiro = saco.Id;
		return (freeza.Id, saco.Id);
	}

	/// <summary>O saco inteiro e de pe: quem esta sendo fotografado e o Freeza.</summary>
	internal void PalcoPF_SacoInteiro(int saco)
	{
		if (!_players.TryGetValue(saco, out ServerPlayer? s) || s.Combate == null) return;
		if (s.Ficha.KO) s.Combate.Levantar();
		s.Combate.Corpo.Restaurar();
		s.Combate.SincronizarVida();
	}

	/// <summary>Ha um Death Beam deste corpo no ar agora?</summary>
	internal bool PalcoPF_DeathBeamNoAr(int freeza) =>
		_players.TryGetValue(freeza, out ServerPlayer? f)
		&& ProjeteisDaZona(f.Zone.Hash).Any(p => p.Dono == freeza && p.Vivo && p.Nome == "Death Beam");

	/// <summary>
	/// O FOTOGRAFO FORA DA LUTA: intocavel por <paramref name="segundos"/> (a carencia do `CombatState`, a mesma de quem acabou de nascer) --
	/// o kiai do Freeza (`KiaiDaIra`, 3 tiles) pula quem esta intocavel. Zero devolve o corpo ao jogo.
	/// </summary>
	internal void PalcoPF_Intocavel(int id, double segundos)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate != null) pl.Combate.Carencia = segundos;
	}

	/// <summary>Tira os corpos do palco, avisando a zona.</summary>
	internal void PalcoPF_Tirar(params int[] ids)
	{
		foreach (int i in ids)
			if (_players.TryGetValue(i, out ServerPlayer? c) && c.Peer == null) RemoverNpc(c);
	}
}
