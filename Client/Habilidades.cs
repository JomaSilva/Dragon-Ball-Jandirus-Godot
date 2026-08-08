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
		// `Verbos.Limpar()` apaga TUDO, inclusive os verbs fixos de Other/Admin. Sem isto eles
		// sumiriam na primeira troca de personagem e so voltariam no relog.
		VerbosDoJogo.Limpar();
		VerbosDoJogo.Registrar();

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

		// ============================ O VOO NAO TEM VERB, E NAO DEVE TER ============================
		// Eu tinha posto dois botoes aqui -- "Voar" e "Velocidade de voo" -- atras da skill
		// `/datum/skill/flying`. O dono cortou os dois:
		//
		//   * voar LIBERA SOZINHO em metade da maestria de Ki (que se treina MEDITANDO), e antes
		//     disso nem deve aparecer. E tecla V, nao botao: quem chegou la ja sabe, porque o
		//     servidor avisa no degrau.
		//   * "velocidade de voo" nao existe -- e o SHIFT, a mesma tecla de correr no chao.
		//
		// Fica anotado pra ninguem "consertar" isto de volta: um verb aqui reintroduziria uma
		// compra pra uma coisa que e consequencia de treinar, e uma marcha que se esquece ligada
		// drenando o Ki mais caro do jogo. Ver `GameServer.Voo.cs`.
		// ==========================================================================================

		// ============================ O OOZARU TINHA IDA E NAO TINHA VOLTA ============================
		// `case "oozaru": ReverterOozaru(pl)` existe em `GameServer.Raciais.cs:159` desde que o Oozaru
		// foi portado, e NINGUEM no jogo conseguia manda-lo: nao havia tecla, botao nem verb. Era o
		// caso classico deste port -- a regra escrita de um lado do fio e sem chamador do outro.
		//
		// A IDA mora embaixo da lua (o botao vermelho do `LuaNoCeu`); a VOLTA mora aqui, e nao la, de
		// proposito: virar e um gesto que se faz OLHANDO pra lua, voltar e uma acao que se procura no
		// menu. Um mesmo lugar que troca de significado ensinaria que os dois sao a mesma coisa.
		//
		// A raca decide quem VE (o mesmo par de `GameServer.Combat.cs:107` -- e o unico jeito de haver
		// um rabo pra a lua morder), e o estado decide quem pode APERTAR. Apagado e visivel de novo
		// pelo motivo de sempre: quem nunca virou macaco descobre pelo botao cinza que da pra virar.
		if (raca is "Saiyan" or "Halfbreed")
			Verbos.Registrar(new Verbo(
				"Voltar ao normal",
				Verbos.Skills,
				"Domina o Oozaru e volta ao seu corpo. Exige dominio sobre a fera -- e o Oozaru "
				+ "Dourado nao volta por vontade: ele se cansa, ou vira Super Saiyajin 4.",
				() => GameClient.Instance?.SendHabilidade("oozaru"),
				() => GameClient.Instance is { } c
					  && c.MeuOozaru != Jandirus.Core.Forms.FormaOozaru.Nao));

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

		DasDisciplinas();
		DasSkills();
		DosEstilos();
	}

	/// <summary>
	/// OS BOTOES DAS DISCIPLINAS DIVINAS -- Ultra Instinto e Poder da Destruicao.
	///
	/// ============================ CADA FAIXA APARECE QUANDO DESTRAVA ============================
	/// A lista sai do <see cref="Jandirus.Core.Forms.Disciplinas"/>, entao acrescentar uma faixa la
	/// e ve-la aqui e a mesma coisa -- pela mesma razao do catalogo de formas: uma habilidade que
	/// existe no servidor e nao tem botao e uma habilidade que ninguem usa.
	///
	/// As PASSIVAS entram como texto, sem acao: elas nao tem o que apertar, mas o jogador precisa
	/// saber que as tem. Passiva que ninguem sabe que ganhou e indistinguivel de passiva quebrada.
	/// ========================================================================================
	/// </summary>
	public static void DasDisciplinas()
	{
		if (GameClient.Instance is not { } cli) return;
		Jandirus.Net.Protocol.AtributosState a = cli.Atributos;
		if (a.Disciplina == 0) return;

		Jandirus.Core.Forms.DisciplinaDef def = a.Disciplina == 1
			? Jandirus.Core.Forms.Disciplinas.UltraInstinct
			: Jandirus.Core.Forms.Disciplinas.PoderDaDestruicao;
		string pre = a.Disciplina == 1 ? "ui" : "ue";

		foreach (Jandirus.Core.Forms.Degrau d in def.Degraus)
		{
			if (a.DiscReal < d.Pct) continue;   // ainda nao destravou

			// O TOGGLE (faixa 0%) e a unica passiva com acao: ligar e desligar.
			if (d.Pct <= 0)
			{
				Verbos.Registrar(new Verbo(
					a.DiscLigada ? $"{d.Nome}  (ligada)" : d.Nome,
					Verbos.Skills,
					$"{d.Desc}\n\nEnergia atual {a.DiscAtual:0}% de {a.DiscReal:0}% -- ela CAI "
					+ "enquanto estiver ligada e nao volta em combate. Transformar renova.",
					() => GameClient.Instance?.SendHabilidade($"{pre}_toggle")));
				continue;
			}

			if (!d.Ativa)
			{
				// PASSIVA JA GANHA: sem acao, so a ficha. `null` de acao = so leitura.
				Verbos.Registrar(new Verbo($"{d.Nome}  ({d.Pct:0}%)", Verbos.Skills, d.Desc, null));
				continue;
			}

			string id = d.Nome.Contains("Godly", StringComparison.OrdinalIgnoreCase)
				? "ui_godlydisplay" : "ue_hakai";
			Verbos.Registrar(new Verbo(d.Nome, Verbos.Skills, d.Desc,
				() => GameClient.Instance?.SendHabilidade(id)));
		}

		// ENSINAR: quem sabe pode passar adiante. E o unico caminho de a disciplina existir no
		// servidor -- ver a cadeia de ensino em `GameServer.Disciplinas.cs`.
		Verbos.Registrar(new Verbo(
			$"Ensinar {def.Nome}",
			Verbos.Skills,
			$"Ensina o {def.Nome} a quem estiver na sua frente. O aluno precisa de "
			+ $"{Jandirus.Core.Forms.Disciplinas.GodKiParaAprender:0}% de maestria no ki divino e "
			+ "nao pode ter trilhado a outra disciplina -- as duas se excluem, e nao ha volta.",
			() => GameClient.Instance?.SendHabilidade($"{pre}_ensinar")));
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
