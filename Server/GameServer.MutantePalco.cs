using Jandirus.Core.Forms;
using Jandirus.Core.Races;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagmutante` (2026-10-19) -- ver `Client/RoboDoMutante.cs`. O robo sobe o servidor deste processo e
/// passa pelas telas de producao (o login, a selecao, a criacao) e pelas teclas do jogador; o palco so faz o que o acaso e o relogio
/// fariam: poe o MUTANTE na vaga (o 1% do `Class_Spread` que a bancada nao pode esperar sair), ensina a reunir energia (o Ki Unlocked
/// que um personagem novo ainda nao comprou) e queima o fusivel na hora, em vez de esperar os doze minutos da 2a Forma. Quem trava o
/// ki, manda os pacotes e responde as teclas sao os funis de producao.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A VAGA DESTA CONTA LOGADA SAI MUTANTE: a classe da vaga (`AccountSave.ClassesDoFrost`) vira o Mutante e a lista de vagas vai de
	/// novo pro cliente -- o mesmo `MandarSlots` que a selecao recebe depois de apagar um personagem. Devolve se achou a conta.
	/// </summary>
	internal bool PalcoM_VagaMutante(string conta, int vaga)
	{
		foreach ((LiteNetLib.NetPeer peer, AccountSave acc) in _logados)
		{
			if (!string.Equals(acc.Conta, conta, StringComparison.OrdinalIgnoreCase)) continue;
			ClasseDoFrostDaVaga(acc, vaga);   // a conta de antes do campo ganha o arranjo
			acc.ClassesDoFrost[vaga] = FormasDeFrost.ClasseMutante;
			_store?.Gravar(acc);
			MandarSlots(peer, acc);
			return true;
		}
		return false;
	}

	/// <summary>
	/// O QUE O SERVIDOR SABE DESTE CORPO E O CLIENTE NAO: a classe (segredo), a forma, os corpos, a trava, a maestria da base, o que
	/// falta da cena e se ele esta reunindo energia. Nulo quando o corpo nao existe.
	/// </summary>
	internal (string Classe, string Forma, List<string> Corpos, bool Travado, double Maestria, double Cena, bool Carregando)?
		PalcoM_Estado(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return null;
		return (pl.Ficha.Class, pl.Forma.Atual, new List<string>(pl.Visual.FormasDeFrost), pl.Ficha.fd_ki_locked,
				MaestriaDaBaseDeFrost(pl), pl.CenaSegundos, pl.Carregando);
	}

	/// <summary>
	/// Ensina (ou desensina) a reunir energia -- o efeito do Ki Unlocked (`MeditateGivesKiRegen`), que um personagem novo ainda nao tem.
	/// O robo desensina logo depois de medir a tecla C travada: com ela, o C duplo de depois tambem acenderia a carga por uns quadros, o
	/// Ki passaria de 100% e o X seguinte so o devolveria aos 100% em vez de recuar.
	/// </summary>
	internal void PalcoM_SabeReunir(int id, bool sabe = true)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Ficha.MeditateGivesKiRegen = sabe ? 1 : 0;
	}

	/// <summary>
	/// O FUSIVEL QUEIMA AGORA: o relogio da forma instavel passa do limite de qualquer degrau, e o PROXIMO tique do motor (`TickDoFrost`,
	/// de producao) trava o ki -- com o aviso, o anuncio da aura e a carga derrubada. So arma numa forma que ele nao segura.
	/// </summary>
	internal bool PalcoM_QueimarOFusivel(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !EhMutanteDeFrost(pl)) return false;
		if (Catalogo.DegrauDoFrost(pl.Forma.Def) <= FormasDeFrost.DegrauEstavel(MaestriaDaBaseDeFrost(pl))) return false;
		pl.FrostInstavelSegundos = 1e6;
		return true;
	}

	/// <summary>O nocaute de producao (`CombatState.Nocautear`, o funil de todo golpe que derruba) por estes segundos.</summary>
	internal void PalcoM_Nocautear(int id, double segundos)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Combate.Nocautear(segundos);
	}

	/// <summary>De pe de novo agora (`CombatState.Levantar`), sem esperar o relogio do nocaute.</summary>
	internal void PalcoM_Levantar(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Combate.Levantar();
	}

	/// <summary>Desliga os defeitos injetados do servidor que o robo liga -- em qualquer saida dele.</summary>
	internal static void PalcoM_Limpar()
	{
		AuraDoDescontroleMudaDeTeste = false;
		PainelDoMutanteMudoDeTeste = false;
		NasceSemAVersaoDosCorposDeTeste = false;
		NocauteDesfazOFrostDeTeste = false;
	}
}
