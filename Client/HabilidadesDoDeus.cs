using Jandirus.Core.Ranks;

namespace Jandirus.Client;

/// <summary>
/// OS BOTOES DO KIT DO DEUS DA DESTRUICAO (aba Skills) -- o `god_apply_powers` do DM
/// (`GodOfDestruction.dm:119-133`), que la entrega os seis verbs ao coroar e os tira ao destituir.
///
/// ============================ REGISTRA SEMPRE, E A TABELA ESCONDE ============================
/// Como todo o `Habilidades`: este arquivo so REGISTRA. Quem decide se o botao existe pra este personagem e o
/// requisito de cada chave em `Core/Verbos/RequisitosDosVerbos.cs` (carregar o cargo `godofdestruction`), lido AO
/// VIVO pelo `Verbos.Visivel` contra a lista de cargos que o servidor empurra -- entao os botoes nascem na coroacao
/// e somem na destituicao sem relogar, que e o "perde TUDO na hora" do original.
///
/// E ele nao e permissao: o servidor confere o trono em cada verb (`GameServer.KitDoDeus.cs`).
/// =============================================================================================
///
/// Os numeros das descricoes saem do <see cref="KitDoDeus"/>, que e onde o servidor os le.
/// </summary>
public static class HabilidadesDoDeus
{
	public static void Registrar()
	{
		Botao("Fury", KitDoDeus.IdDaFury,
			$"A transformação do Deus da Destruição: {KitDoDeus.FuryMult:0}x, com uma aura violeta colada ao corpo. "
			+ "Não gasta Ki e não tem maestria -- é do TÍTULO: só liga pra quem o carrega, e cai na hora em que "
			+ "ele é perdido.\n\nNão entra por cima de outra transformação (recolha a outra antes), e com ela de "
			+ "pé nenhuma outra entra. Aperte de novo pra recolher.");

		Botao("Hakai", KitDoDeus.VerbHakai,
			"APAGA quem você está agarrando (ou quem está colado à sua frente). Só pega quando o TORSO do alvo está em "
			+ $"{KitDoDeus.HakaiTorsoPct:0}% ou menos e ele é ao menos {(1 - KitDoDeus.HakaiFracaoDeBp) * 100:0}% mais fraco "
			+ "que você agora.\n\nQuem é apagado assim paga ao Enma "
			+ $"{KitDoDeus.HakaiPrecoDoRevive:0}x o preço pra voltar à vida.");

		Botao("Power Flick", KitDoDeus.VerbFlick,
			"O peteleco: solta quem você está AGARRANDO com o dano de "
			+ $"{KitDoDeus.FlickGolpes:0} golpes pesados sem carga no torso, e o arremessa pra onde você olha.");

		Botao("Destruction God's Fury", KitDoDeus.VerbFuria,
			"Um tiro de dedo que não se deflete: mira o TORSO, e onde fere DERRUBA a guarda do alvo. Carrega em um "
			+ $"terço do tempo de um tiro comum. Custa {KitDoDeus.FuriaKi:0} de Ki (vezes o seu dreno).");

		Botao("Destroyer Sphere", KitDoDeus.VerbEsfera,
			"Uma esfera de pura Destruição que não se deflete e aplica o HAKAI em quem ferir, pelos mesmos dois "
			+ $"critérios do agarrão (torso em {KitDoDeus.HakaiTorsoPct:0}% ou menos, e "
			+ $"{(1 - KitDoDeus.HakaiFracaoDeBp) * 100:0}% mais fraco que você). Custa {KitDoDeus.EsferaKi:0} de Ki "
			+ "(vezes o seu dreno).");

		Botao("Energy Nullification", KitDoDeus.VerbAnular,
			$"Apaga os ataques de ki dos outros a até {KitDoDeus.AnulacaoTiles} tiles que carregam MENOS poder do que "
			+ $"você tem agora. Recarga de {KitDoDeus.AnulacaoRecargaSegundos / 60:0} minutos -- e ela só começa se "
			+ "alguma coisa foi anulada.");

		// OS DOIS QUE O TITULO EMPRESTA A QUEM TRILHA O INSTINTO (`GodOfDestruction.dm:134-138`). A tabela so os
		// mostra a esse Deus; quem trilha a Destruicao tem os botoes da propria disciplina.
		string emprestimo = $"{KitDoDeus.EmprestimoDoCaminho * 100:0}%";
		Botao("Aura of Destruction (emprestada)", KitDoDeus.VerbAuraEmprestada,
			$"O título lhe EMPRESTA a Aura of Destruction, a {emprestimo} da eficiência: quem te acerta se machuca, o ki "
			+ "que chega perde força, você sofre menos dano, e o golpe fatal é negado uma vez por luta.\n\nEla é movida "
			+ "pela PRECISÃO do seu Ultra Instinto -- e a gasta enquanto estiver ligada. Aperte de novo pra recolher.");
		Botao("Hakai Infusion (emprestada)", KitDoDeus.VerbInfusaoEmprestada,
			$"O título lhe EMPRESTA a Hakai Infusion, a {emprestimo} da eficiência: por um minuto os seus ataques de ki "
			+ "penetram mais e são mais difíceis de defletir. Pede a faixa de 80% -- medida pela maestria do seu "
			+ "Ultra Instinto.");
	}

	/// <summary>Um verb do kit: o id viaja pelo canal de habilidade, e a chave dele na tabela de requisitos e `hab:` + o id.</summary>
	private static void Botao(string nome, string id, string descricao) =>
		Verbos.Registrar(new Verbo(nome, Verbos.Skills, descricao,
			() => GameClient.Instance?.SendHabilidade(id)) { Chave = Jandirus.Core.Verbos.RequisitosDosVerbos.PrefixoDeHabilidade + id });
}
