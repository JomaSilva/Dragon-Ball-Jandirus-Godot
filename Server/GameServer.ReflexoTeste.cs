using Godot;
using Jandirus.Core.Appearance;

namespace Jandirus.Server;

/// <summary>
/// ============================ A SUPERFICIE DE BANCADA DA FICHA DO CORPO ERGUIDO (`--diagreflexo`) ============================
/// Este arquivo nao roda bancada nenhuma: ele e o que o robo do CLIENTE (`Client/RoboDoReflexo.cs`)
/// precisa alcancar do lado da AUTORIDADE, no molde do `GameServer.MergulhoTeste.cs` -- perguntas
/// (`...NoTeste`) e os dois preparos que o jogador sozinho nao encomenda num personagem recem-criado
/// (ter roupa no corpo e saber dividir o corpo).
///
/// NENHUM METODO DAQUI ERGUE CORPO. O reflexo nasce do `EntrarNaMente` e a copia nasce do
/// `DividirOCorpoG12`, os dois pelo pacote que o jogador manda (`C2S.Habilidade`): o que a bancada
/// mede e a ficha de aparencia desses corpos atravessando o fio ate a tela, e um atalho que os
/// criasse por fora pularia justamente a linha que manda a ficha.
///
/// O DEFEITO INJETADO NAO MORA AQUI: e o `GameServer.LookSemTipoDeFusaoDeTeste`, ao lado do
/// `PacoteDeAparencia`, lido pela linha do proprio escritor.
/// ==============================================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>O id do corpo que a mente deste jogador ergueu (0 = nenhum) -- o `CloneId`, que e a fonte unica.</summary>
	internal int ReflexoNoTeste(int donoId) =>
		_players.TryGetValue(donoId, out ServerPlayer? dono) ? dono.CloneId : 0;

	/// <summary>Os ids das copias do Splitform deste jogador, em ordem de nascimento (ids so crescem).</summary>
	internal List<int> CopiasNoTeste(int donoId)
	{
		var ids = new List<int>();
		foreach ((int id, SplitformG12 sf) in _splitformsG12)
			if (sf.Master == donoId) ids.Add(id);
		ids.Sort();
		return ids;
	}

	/// <summary>
	/// O QUE ESTE CORPO E na conta do servidor: os campos DELE, crus.
	///
	/// CRUS, e nao o que o `PacoteDeAparencia` monta, de proposito: a bancada compara o que CHEGOU na
	/// tela com o que o corpo E, e devolver daqui a mesma expressao do escritor (`NomeVisivel`,
	/// `VisualVisivel`) seria conferir o pacote com ele mesmo. Pro reflexo e pra copia as duas coisas
	/// tem que coincidir -- nenhum dos dois tem fusao nem disfarce proprios --, e e isso que a cena cobra.
	/// </summary>
	internal (string Nome, string Raca, string Genero, Appearance Visual)? CorpoNoTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? p) ? (p.Name, p.Race, p.Genero, p.Visual) : null;

	/// <summary>
	/// POE ROUPA NO CORPO DA BANCADA e reapresenta a aparencia pelo caminho de producao
	/// (<see cref="ReapresentarAparencia"/>). Devolve quantas pecas ele veste.
	///
	/// ============================ O ATALHO, DITO EM VOZ ALTA ============================
	/// O personagem que a linha de comando cria nasce SEM ROUPA (`Boot.AutoEscolher`: so o cabelo do
	/// Goku), e um reflexo de um corpo nu nao prova que a roupa atravessa -- nao ha roupa pra atravessar.
	/// A bancada nao mede o guarda-roupa (isso e a `--roupateste`); ela mede a FICHA DO CORPO ERGUIDO, e
	/// pra isso o dono precisa ter o que copiar. Uma peca vai TINGIDA, pra a cor ter que viajar junto.
	/// ==================================================================================
	/// </summary>
	internal int VestirNoTeste(int id)
	{
		if (_visual == null || !_players.TryGetValue(id, out ServerPlayer? pl)) return 0;

		// POR NOME, com o comeco da lista de reserva: as duas cobrem o tronco e as pernas, entao um
		// corpo sem elas e um corpo visivelmente pelado na foto. Se o catalogo mudar, as duas primeiras
		// pecas servem -- o que a cena cobra e a IGUALDADE entre dono e reflexo, nao qual peca e.
		string macacao = _visual.Roupas.Find(r => r.Contains("Blue suit", StringComparison.OrdinalIgnoreCase))
						 ?? (_visual.Roupas.Count > 0 ? _visual.Roupas[0] : "");
		string camisa = _visual.Roupas.Find(r => r.Contains("Kung Fu Shirt", StringComparison.OrdinalIgnoreCase))
						?? (_visual.Roupas.Count > 1 ? _visual.Roupas[1] : "");

		// A CONTA DE BANCADA PODE SER REUSADA (o save fica na pasta de rascunho): so entra o que falta.
		foreach (PecaDeRoupa peca in new[] { new PecaDeRoupa(macacao), new PecaDeRoupa(camisa, new Rgb(0xC8, 0x30, 0x28)) })
			if (peca.Caminho.Length > 0 && pl.Visual.Roupa.Count < Appearance.MaxRoupa
				&& !pl.Visual.Roupa.Exists(p => p.Caminho == peca.Caminho))
				pl.Visual.Roupa.Add(peca);

		ReapresentarAparencia(pl);
		return pl.Visual.Roupa.Count;
	}

	/// <summary>
	/// ENSINA A DIVISAO DE CORPO (a skill do livro que destrava o verb `SplitForm`) e enche o Ki.
	///
	/// OUTRO ATALHO, E DO MESMO TAMANHO: nascer sabendo nao testa aprender, e a bancada nao afirma nada
	/// sobre aprender. O verb continua passando pelo portao de producao (`UsarTecnica` -> `SabeTecnica`),
	/// que e por onde o pacote do robo entra; o Ki cheio existe porque a divisao cobra metade do tanque
	/// e a cena anterior (a mente) pode ter gasto o dele.
	/// </summary>
	internal bool EnsinarADivisaoNoTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Livro == null) return false;
		if (!pl.Livro.Sabe(PathSplitformG12)) pl.Livro.Dar(PathSplitformG12);
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		return SabeTecnica(pl, "SplitForm");
	}

	/// <summary>
	/// LIMPEZA DE FIM DE RODADA: as copias se desfazem, a mente devolve o corpo. E o oposto de uma
	/// excecao -- sem ela, uma rodada interrompida deixa um corpo dirigido parado num bolso pra sempre.
	/// </summary>
	internal void LimparOReflexoNoTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		DestruirSplitformsG12(pl);
		if (NaMente(pl)) SairDaMente(pl, "a bancada acabou.");
		DesfazerOOponente(pl, "");
		GD.Print($"[reflexo] limpeza: #{id} fora da mente, sem reflexo e sem copia");
	}
}
