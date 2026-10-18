using Jandirus.Core.Races;

namespace Jandirus.Core.Ai;

/// <summary>
/// AS ATIVAS RACIAIS DO JEITO QUE A IA AS LE (o dono, 2026-10-17: *"n esqueça de fazer as IAs saberem usar as passivas e ativas da raça, e
/// os boss de saga tb vao ter todas essas passivas e ativas a depender de sua raça"*).
///
/// ============================ AS PASSIVAS NAO PRECISAM DE IA; AS ATIVAS SAO LINHAS DO REPERTORIO ============================
/// A passiva roda sozinha no tique racial de todo corpo que tem kit (`GameServer.TickDasRaciais`) -- ninguem a aperta. As ativas sim, e
/// onze das treze tem exatamente a forma de uma linha do repertorio de luta que o cerebro ja sabe usar (`Cerebro.Repertorio.cs`):
///
///   REFORCO   liga quando a luta comeca. As de PRAZO nao se desligam (Ferocidade, Forca Imparavel, Sede de Sangue, Disrupcao,
///             Sadismo): ligadas, correm ate o prazo vencer, e na recarga somem da tabela. O Poder Maximo e a Ira se LIGAM E DESLIGAM e
///             desligam como todo reforco -- com o Ki baixo (os dois alugam o tanque: um o drena, a outra o encolhe) e quando a luta
///             acaba. Os dois Transbordar tambem nao se desligam pelo caminho comum: o segundo aperto deles e o DESABAFO, e quem o da e
///             a receita propria (`Cerebro.Raciais.cs`) -- o corpo paga o transbordar no tronco, e esperar o Ki baixo o derrubaria.
///   SOCORRO   o Segundo Folego acode o TANQUE (o Ki no fim); a Forca de Vontade, a VIDA (o "nada te derruba" vale com ela caindo).
///
/// As duas que pedem o corpo PARADO -- o Demonic Saiyan (15 s) e a execucao do Ruthless Mind (3 s) -- nao cabem num pulso: moram no
/// `Cerebro.Raciais.cs`, com o desabafo.
///
/// QUEM RECUSA CONTINUA SENDO O VERB (`GameServer.UsarAtivaRacial`), e quem tem cada ativa e a pergunta DELE (`SkillsRaciais.TemAtiva`: o
/// kit, ou -- no bio-androide -- a skill que ele escolheu do sangue): a linha daqui so diz como o cerebro a usa. O servidor so poe na tabela a
/// linha que o corpo tem e que nao esta em recarga (`GameServer.AtivasRaciaisNaMao`) -- o cerebro nao bate na porta de quem ja disse "ainda
/// em recarga".
/// ==============================================================================================================================
/// </summary>
public static class AtivasRaciaisNaIa
{
	/// <summary>Uma ativa racial como linha do repertorio: o verb, o uso e o que ela pede da cena.</summary>
	public readonly record struct Linha(string Id, Uso Uso, Exige Exige);

	private static readonly Linha[] Tudo =
	[
		new(Passivas.Ferocidade, Uso.Reforco, Exige.NaoSeDesliga),
		new(Passivas.Transbordar, Uso.Reforco, Exige.NaoSeDesliga),
		new(Passivas.ForcaImparavel, Uso.Reforco, Exige.NaoSeDesliga),
		new(Passivas.TransbordarKi, Uso.Reforco, Exige.NaoSeDesliga),
		new(Passivas.SedeDeSangue, Uso.Reforco, Exige.NaoSeDesliga),
		new(Passivas.PoderMaximo, Uso.Reforco, Exige.Nada),
		new(Passivas.SegundoFolego, Uso.Socorro, Exige.KiNoFim),
		new(Passivas.ForcaDeVontade, Uso.Socorro, Exige.Nada),
		new(Passivas.Disrupcao, Uso.Reforco, Exige.NaoSeDesliga),
		new(Passivas.Ira, Uso.Reforco, Exige.Nada),
		new(Passivas.Sadismo, Uso.Reforco, Exige.NaoSeDesliga),
	];

	/// <summary>As onze linhas, na ordem da lista do amigo do dono.</summary>
	public static ReadOnlySpan<Linha> Todas => Tudo;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--iaraciaisteste`): a IA nao conhece ativa racial nenhuma -- a v0.4.0, em que so o jogador as apertava.
	/// Lido pelo servidor ao montar o repertorio e as tres receitas proprias. Falso em jogo.
	/// </summary>
	public static bool IaSemAtivasDeTeste;
}
