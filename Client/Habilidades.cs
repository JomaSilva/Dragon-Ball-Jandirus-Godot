using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// O QUE ESTE PERSONAGEM PODE ACIONAR, virando botao no menu (tecla P, aba Skills).
///
/// Cada habilidade aqui e um <see cref="Verbo"/> que manda um id pelo canal unico de habilidade
/// (ver `GameServer.Raciais.cs`). O cliente NAO decide se pode: ele oferece, o servidor valida e
/// responde pelo chat. Botao que aparece e e recusado com um motivo ensina o jogo; botao que nao
/// aparece esconde o jogo.
///
/// COMO PLUGAR UMA TECNICA NOVA (e o caminho que as 319 skills vao usar):
///   1. um `case` no `UsarHabilidade` do servidor;
///   2. uma linha aqui dizendo quem ve o botao.
/// </summary>
public static class Habilidades
{
	/// <summary>
	/// Refaz a lista a partir da raca e do que foi aprendido. Chamado quando a ficha lenta
	/// chega -- e ela que traz a raca, e ela so chega depois de entrar no mundo.
	/// </summary>
	public static void Montar(string raca)
	{
		Verbos.Limpar();

		// A MENTE E DE TODO MUNDO: treinar contra si mesmo nao e dom de raca nenhuma.
		Verbos.Registrar(new Verbo(
			"Treinar com o clone",
			Verbos.Aprendizado,
			"Medite (M) e entre na propria mente pra lutar contra uma copia exata de voce. "
			+ "Mesmo poder, mesma velocidade: a luta mede a sua habilidade, nao o seu BP.",
			() => GameClient.Instance?.SendHabilidade("mente"),
			() => GameClient.Instance?.Atividade == Jandirus.Net.Protocol.Activity.Meditando));

		Verbos.Registrar(new Verbo(
			"Sair da mente",
			Verbos.Aprendizado,
			"Abre os olhos e volta pra onde estava.",
			() => GameClient.Instance?.SendHabilidade("sairdamente")));

		Verbos.Registrar(new Verbo(
			"Decolar",
			Verbos.Outros,
			"Deixa a superficie e sobe ao espaco. So funciona de um planeta que esta no mapa do "
			+ "universo -- do Outro Mundo nao se decola.",
			() => GameClient.Instance?.SendHabilidade("decolar")));

		if (raca is "Namekian" or "Majin" or "BioAndroid" or "Shapeshifter")
			Verbos.Registrar(new Verbo(
				"Regenerar",
				Verbos.Skills,
				"Faz um membro decepado voltar a crescer, ou cura por inteiro o mais ferido. "
				+ "Custa 70% do Ki maximo e tem 10s de espera.",
				() => GameClient.Instance?.SendHabilidade("regenerar"),
				// APAGA quando falta Ki, mas continua na lista: saber que a tecnica existe e que
				// falta Ki e informacao util; sumir com ela e esconder o jogo.
				() => GameClient.Instance is { } c && c.Sheet.MaxKi > 0 && c.Sheet.Ki >= c.Sheet.MaxKi * 0.7));

		DasSkills();
		DosEstilos();
	}

	/// <summary>
	/// UM BOTAO POR ESTILO APRENDIDO, mais um pra soltar a postura.
	///
	/// O estilo entra no menu como VERBO e nao como aba propria porque e exatamente o que ele e
	/// no original: uma acao ("Pick Current Style"), nao uma ficha. Quem tem um estilo so nem
	/// precisa abrir nada -- ve o botao, aperta, e o multiplicador ja esta valendo.
	/// </summary>
	public static void DosEstilos()
	{
		if (GameClient.Instance is not { } cli || cli.Estilos.Count == 0) return;

		foreach (GameClient.EstiloInfo e in cli.Estilos)
		{
			string id = e.Id;
			bool ativo = string.Equals(cli.EstiloAtual, id, StringComparison.OrdinalIgnoreCase);
			Verbos.Registrar(new Verbo(
				ativo ? $"{e.Nome}  (em uso)" : e.Nome,
				Verbos.Skills,
				$"Assume a postura de {e.Nome}. Maestria {e.Maestria:0.#} de {e.Teto:0}. "
				+ "A maestria sobe lutando e treinando NESTE estilo, e enferruja nos que voce deixa "
				+ "parados -- por isso escolher importa.",
				() => GameClient.Instance?.SendEstilo(id),
				() => !ativo));
		}

		Verbos.Registrar(new Verbo(
			"Soltar a postura",
			Verbos.Skills,
			"Luta sem estilo nenhum. Perde os multiplicadores e o bonus de dano da disputa.",
			() => GameClient.Instance?.SendEstilo("-"),
			() => GameClient.Instance is { } c && c.EstiloAtual.Length > 0));
	}

	/// <summary>
	/// OS BOTOES QUE VIERAM DAS SKILLS APRENDIDAS.
	///
	/// Nenhuma linha aqui conhece tecnica nenhuma: le o que foi aprendido, pergunta ao catalogo
	/// quais verbs aquilo destrava e registra um botao por verb. Portar a proxima tecnica nao
	/// mexe neste arquivo -- so no `UsarTecnica` do servidor e na tabela do <see cref="Tecnicas"/>.
	///
	/// A TECNICA AINDA NAO PORTADA TAMBEM VIRA BOTAO, apagado e dizendo por que. Esconde-la
	/// deixaria o jogador achando que perdeu os marcos; um botao cinza com "o efeito ainda nao
	/// foi trazido do jogo antigo" e a verdade, e some sozinho no dia em que a tecnica sair.
	/// </summary>
	public static void DasSkills()
	{
		SkillCatalog? cat = MenuJogo.CatalogoPublico();
		GameClient? cli = GameClient.Instance;
		if (cat == null || cli == null) return;

		var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string path in cli.SkillsAprendidas)
		{
			Skill? s = cat.Get(path);
			if (s == null) continue;
			foreach (string id in s.Verbos)
			{
				if (!vistos.Add(id)) continue;
				Tecnicas.Tecnica? t = Tecnicas.Get(id);
				if (t == null) continue;

				bool pronta = t.Modo != Modo.NaoPortada;
				string idLocal = id;
				Verbos.Registrar(new Verbo(
					pronta ? t.Nome : $"{t.Nome} (nao portada)",
					t.Aba,
					t.Desc,
					() => GameClient.Instance?.SendHabilidade(idLocal),
					pronta ? null : () => false));
			}
		}
	}
}
