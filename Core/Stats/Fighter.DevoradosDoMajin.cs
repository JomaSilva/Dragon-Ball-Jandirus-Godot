namespace Jandirus.Core.Stats;

/// <summary>
/// (2026-10-23) OS NPC QUE O MAJIN DEVOROU, NA FICHA -- e por isso no disco (o save serializa os campos publicos).
///
/// O dono: *"bp de majin absorvido de npcs n saem ao sair do jogo mais, players tao reclamando mt disso, player ainda sai ao sair do
/// jogo"*. E a decisao dele: o NPC devorado FICA dentro depois de sair do jogo (o poder que ele rende e a linha dele no menu de
/// expelir); o JOGADOR absorvido continua sendo cuspido na saida (o bolsao dele e da sessao). No DM nada disso vai pro disco -- o
/// `DoLogoutStuff` esvazia a lista antes do save (`Login/Login.dm:198-202`) --, entao a regra e do dono.
///
/// A LISTA E UM ESPELHO, e nao a verdade da sessao: quem manda em jogo e o registro do servidor (`GameServer._dentroDoMajin`, por id
/// de sessao), e esta lista o acompanha a cada mudanca (`GameServer.GuardarOsDevoradosNaFicha`) -- por isso o salvamento automatico
/// e o servidor que cai no meio da sessao tambem a levam. O login a le e RECRIA os registros (`GameServer.AcordarSemAbsorcao`): o
/// nocaute, a morte e o Expelir continuam tirando o NPC pelo caminho de sempre, e o `majin_absorb_bp` sai da SOMA dela -- o numero
/// solto no disco, sem a lista, nao vale nada (senao o poder emprestado ficaria pra sempre, sem nada que o tirasse).
///
/// A VAGA RECICLADA NAO HERDA: a lista mora na ficha do personagem, e o personagem novo da vaga nasce com uma ficha nova.
/// </summary>
public partial class Fighter
{
	/// <summary>Os NPC devorados que este Majin segura, na ordem em que entraram. Nulo = nenhum (e o save de antes da regra).</summary>
	public List<DevoradoPeloMajin>? majin_devorados;
}

/// <summary>UM NPC DEVORADO, como vai pro disco: o nome (a linha do menu de expelir) e o bonus de BP que ele rende.</summary>
public sealed class DevoradoPeloMajin
{
	public string Nome = "";
	public double BonusDeBp;

	/// <summary>O poder emprestado que uma lista destas rende: a soma dos bonus (um negativo de save mexido nao subtrai nada).</summary>
	public static double Soma(IEnumerable<DevoradoPeloMajin>? lista) => lista?.Sum(d => Math.Max(d.BonusDeBp, 0)) ?? 0;
}
