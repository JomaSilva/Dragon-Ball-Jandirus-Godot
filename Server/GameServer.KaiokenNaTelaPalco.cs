namespace Jandirus.Server;

/// <summary>
/// O PALCO DA `--diagkaiokenveto` (`Client/RoboDaRecusaDoKaioken.cs`, 2026-10-21): a tela do Kaio-ken diz ANTES do aperto quando a forma
/// nao o sustenta. O robo e o jogador do host; o palco so poe o corpo na condicao de cada foto -- a classe e a maestria divina da ficha, as
/// duas pecas que a regra do Core (`Kaioken.VetoDaForma`) le alem da forma -- e conta o que o servidor responde ao botao, pra a tela ser
/// comparada com ele. A forma vem do palco da `--diaglimite` (`PalcoL_Forma`: dominada, sem cinematica, pelo anuncio de producao).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// A CLASSE E O KI DIVINO do corpo. A classe nos DOIS lugares -- a ficha (que o servidor le) e o `ServerPlayer.Class` (que viaja no
	/// `AtributosState.Classe`) --, e a assinatura da ficha lenta apagada: a classe nao entra nela, e sem isso o pacote nao sairia. O
	/// `S2C.GodKi` sai sozinho no tique dele, quando o retrato muda.
	/// </summary>
	internal void PalcoKV_Corpo(int id, string classe, double maestriaDivina)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Class = pl.Ficha.Class = classe;
		pl.Ficha.godki ??= new Jandirus.Core.Stats.GodKiState();
		pl.Ficha.godki.awakened = true;
		pl.Ficha.godki.mastery = maestriaDivina;
		pl.SigAtributos = "";
	}

	/// <summary>
	/// SO A MAESTRIA DIVINA, SEM a ficha lenta atras: a medida da tela que acompanha o `S2C.GodKi` aberta. Apagar a assinatura aqui
	/// mandaria tambem o `AtributosState`, e a tela se refaria pelo outro ouvido -- a regua passaria sem medir o que diz medir.
	/// </summary>
	internal void PalcoKV_MaestriaDivina(int id, double maestriaDivina)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && pl.Ficha.godki is { } g) g.mastery = maestriaDivina;
	}

	/// <summary>
	/// O BOTAO DO KAIO-KEN NO SERVIDOR (`UsarHabilidade`, a porta do clique) e o que ele disse. Aceso, apaga na hora: a medida e so a
	/// resposta -- a tela recusa exatamente quando o servidor recusa, e com a frase dele.
	/// </summary>
	internal (bool Acendeu, string Falas) PalcoKV_Apertar(int id, string verbo)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return (false, "corpo fora do mundo");
		List<string> falas = ApertarEOuvir(pl, verbo);
		bool acendeu = TemBuff(pl, BuffKaiokenG2);
		if (acendeu) ReverterKaiokenG2(pl, porKO: false);
		return (acendeu, string.Join(" | ", falas));
	}
}
