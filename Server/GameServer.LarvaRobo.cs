using Jandirus.Core.Races;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// OS GANCHOS DO ROBO `--diaglarva` (cliente, com o servidor neste processo): a LARVA do bio-androide na tela. A PREMISSA (o corpo do robo
/// vira um bio de laboratorio PERFEITO, e "cinco minutos se passaram enterrada") e escrita aqui; os gestos sao os de producao, pelo fio do
/// cliente (`larva`, `voar`, `enterrar`), e quem amadurece a larva e o tique de producao (`TickDaLarva`).
/// </summary>
public partial class GameServer
{
	/// <summary>O corpo do robo vira um bio-androide de laboratorio neste degrau e marco, careca e com o corpo do degrau.</summary>
	internal void VirarBioDeLaboratorioDeTeste(int id, int estagio, int marco)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		Fighter f = pl.Ficha;
		pl.Race = f.Race = f.ParentRace = BioAndroids.Raca;
		f.bio_lab_born = true;
		f.bio_stage = estagio;
		f.bio_marco = marco;
		f.bio_larva = false;
		// UM TANQUE DE KI DE BIO, e nao o de um corpo recem-criado (100): o voo come ~14 de Ki por segundo e a primeira rodada caiu exausta.
		f.baseKi = Math.Max(f.baseKi, 2000);
		pl.Visual.Cabelo = "Bald";
		pl.Visual.CorCabelo = null;
		pl.Visual.FormasDeFrost.Clear();
		pl.Visual.Roupa.Clear();
		pl.Visual.Corpo = BioAndroids.IndiceDoCorpo(estagio);
		_visual?.Sanear(pl.Visual, pl.Race, pl.Genero);
		f.Statify();
		AplicarForma(pl);
		f.Ki = f.MaxKi;
		pl.SigAtributos = "";
		TrocarAparencias(pl);
	}

	/// <summary>O que o robo le da larva, num retrato.</summary>
	internal readonly record struct RetratoDaLarva(bool Larva, bool Enterrado, double Expresso, bool Sentido, bool Oculto, bool Intocavel,
		bool Anda, int Estagio, int Corpo, bool Voando, bool PodeVoar);

	internal RetratoDaLarva? LarvaDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl)
			? new RetratoDaLarva(BioAndroids.EmLarva(pl.Ficha), pl.EnterradoDesde >= 0, pl.Ficha.expressedBP, TemEnergiaQueSeSente(pl),
									EstaOculto(pl.Id), pl.Combate?.Intocavel == true, PodeMexerOCorpo(pl), pl.Ficha.bio_stage, pl.Visual.Corpo, pl.Voando,
									PodeVoar(pl))
			: null;

	/// <summary>"Cinco minutos se passaram enterrada": o comeco da terra recua no tempo -- a premissa. Quem amadurece e o tique de producao.</summary>
	internal void EnvelhecerAEnterradaDeTeste(int id, double segundos)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && pl.EnterradoDesde >= 0) pl.EnterradoDesde -= (long)(segundos * 1000);
	}
}
