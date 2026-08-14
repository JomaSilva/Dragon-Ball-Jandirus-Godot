using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// O QUE CADA POVO TEM DE PROPRIO -- a parte ATIVA e as ligacoes que faltavam.
///
/// O ZENKAI EM SI JA ESTAVA PORTADO em <see cref="Fighter.GainZenkai"/> (etapa de combate), com
/// teto, recarga de uma hora, bonus de "em frangalhos", aposentadoria por BP e a comparacao por
/// PODER EFETIVO nos dois lados. O que faltava era ele disparar no NOCAUTE tambem -- ver
/// <see cref="ZenkaiPorDerrota"/>.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// SURTO POR DERROTA. "Derrotado" e nocauteado OU morto -- as duas coisas, e no original
	/// isso e explicito.
	///
	/// Ficar so na morte tinha uma consequencia esquisita: numa luta nao-letal (o `murderToggle`
	/// desligado, que e o padrao entre jogadores) NINGUEM morre, entao o Zenkai simplesmente
	/// nunca aconteceria em treino amistoso -- justamente onde a mecanica mais faz sentido.
	///
	/// A recarga de uma hora e que impede o dobro: levar um nocaute e depois o golpe fatal na
	/// mesma sequencia concede UMA vez.
	/// </summary>
	private void ZenkaiPorDerrota(ServerPlayer vitima, ServerPlayer vencedor)
	{
		if (vitima == vencedor || vitima.Combate == null) return;

		Fighter.ZenkaiResult z = vitima.Ficha.GainZenkai(
			Math.Max(vencedor.Ficha.expressedBP, vencedor.Ficha.BP), NowMs(),
			vitima.Combate.Corpo.MuitoFerido());

		if (z != Fighter.ZenkaiResult.Concedido)
		{
			// SO AVISA QUEM TEM O DOM. Dizer "ainda em recarga" pra um humano seria contar de um
			// sistema que nunca vai ser dele.
			if (z == Fighter.ZenkaiResult.EmRecarga && vitima.Ficha.HasZenkai())
				Avisar(vitima, "seu corpo ainda se refaz do ultimo surto.");
			return;
		}

		GD.Print($"[server] Zenkai de {vitima.Name}: +{vitima.Ficha.UltimoZenkai:0} de BP"
				 + (vitima.Ficha.UltimoZenkaiNoTeto ? " (no teto)" : ""));
		Avisar(vitima, Narrar(vitima.Ficha.UltimoZenkai / Math.Max(vitima.Ficha.BP - vitima.Ficha.UltimoZenkai, 1),
							  vitima.Ficha.UltimoZenkaiNoTeto));
	}

	/// <summary>
	/// O TEXTO NAO TRAZ NUMERO, e isso e do original. O que o jogador recebe e a sensacao do
	/// proprio corpo, nao um relatorio -- e o tamanho da frase escala com o quanto o BP subiu.
	/// </summary>
	private static string Narrar(double proporcao, bool noTeto) => noTeto
		? "seu corpo se refaz ate o limite do que poderia suportar. Nao havia mais o que absorver."
		: proporcao >= 1 ? "uma onda COLOSSAL percorre voce. O corpo se refaz muito mais forte."
		: proporcao >= 0.5 ? "uma onda forte percorre voce: o corpo se refaz mais forte."
		: proporcao >= 0.2 ? "voce se levanta visivelmente mais forte do que caiu."
		: "um formigamento fraco: seu corpo aprendeu alguma coisa com a surra.";

	// =====================================================================
	// REGENERACAO -- a habilidade ativa do Namekuseijin (e do Majin)
	// =====================================================================
	/// <summary>70% do Ki maximo (`NAMEK_REGEN_KI_COST`). Regenerar nao e cura de bolso.</summary>
	private const double RegenCustoKi = 0.7;
	private const long RegenRecargaMs = 10_000;

	/// <summary>
	/// Quem regenera membro: pela RAÇA (o `canheallopped`, que e o mesmo conjunto que o combate
	/// usa pra por em coma em vez de matar) **OU** por ter APRENDIDO a skill de regeneracao.
	///
	/// A SEGUNDA METADE FALTAVA, e so apareceu quando as skills passaram a destravar tecnica: a
	/// skill `Regenerate` pende das arvores raciais de Alien, Android e Demon, entao um Alien
	/// COMPRAVA a habilidade, via o botao na tela, apertava, e ouvia "seu corpo nao regenera
	/// assim". Estavam sendo tratados como a mesma pergunta dois gates que sao diferentes: o de
	/// RACA decide se perder um membro vital mata; o de SKILL decide se voce sabe faze-lo voltar.
	/// </summary>
	private bool PodeRegenerar(ServerPlayer pl) => Regenera(pl.Race) || SabeTecnica(pl, "Regenerate");

	/// <summary>
	/// REGENERA. Prefere devolver um membro DECEPADO; sem nenhum, cura o mais ferido ao cheio.
	///
	/// A ORDEM IMPORTA: um Namekuseijin sem braco que gastasse 70% do Ki curando um arranhao na
	/// perna teria jogado a habilidade fora e continuaria sem braco.
	/// </summary>
	private void Regenerar(ServerPlayer pl)
	{
		if (!PodeRegenerar(pl)) { Avisar(pl, "seu corpo nao regenera assim."); return; }
		if (pl.Combate == null || pl.Ficha.dead) { Avisar(pl, "agora nao."); return; }

		long agora = NowMs();
		if (agora < pl.RegenLivreEm)
		{
			Avisar(pl, $"ainda nao ({(pl.RegenLivreEm - agora) / 1000 + 1}s).");
			return;
		}

		double custo = pl.Ficha.MaxKi * RegenCustoKi;
		if (pl.Ficha.Ki < custo)
		{
			Avisar(pl, $"Ki insuficiente: regenerar custa {RegenCustoKi * 100:0}% do maximo.");
			return;
		}

		// 1) um membro DECEPADO, e nunca um cuja raiz tambem caiu -- a mao volta junto com o
		//    braco, entao escolher a mao separada desperdicaria a regeneracao
		BodyPart? alvo = pl.Combate.Corpo.Partes.FirstOrDefault(
			p => p.Decepado && !Caiu(pl.Combate.Corpo, p.Dono));

		// 2) sem nenhum decepado, o mais machucado
		alvo ??= pl.Combate.Corpo.Partes
			.Where(p => !p.Decepado && p.Fracao < 1)
			.OrderBy(p => p.Fracao)
			.FirstOrDefault();

		if (alvo == null) { Avisar(pl, "seu corpo ja esta inteiro."); return; }

		bool voltou = alvo.Decepado;
		if (voltou) pl.Combate.Corpo.Regenerar(alvo);

		// O `Regenerar` do corpo devolve o membro com 70% de vida. A habilidade completa o
		// resto: regenerar de proposito, gastando quase todo o Ki, nao pode deixar o braco
		// pela metade -- essa era a queixa que gerou o rework no original.
		alvo.Vida = alvo.VidaMax;

		pl.Ficha.Ki -= custo;
		pl.RegenLivreEm = agora + RegenRecargaMs;
		pl.Combate.SincronizarVida();
		AjustarGanhoDoRabo(pl);

		GD.Print($"[server] {pl.Name} regenerou {alvo.Nome}{(voltou ? " (DECEPADO)" : "")}");
		Avisar(pl, voltou ? $"seu {alvo.Nome} volta a crescer." : $"seu {alvo.Nome} se refaz por inteiro.");
	}

	private static bool Caiu(Body corpo, string? nome) =>
		nome != null && corpo.Achar(nome) is { Decepado: true };

	// =====================================================================
	// O CANAL DE HABILIDADE
	// =====================================================================
	/// <summary>
	/// UM PACOTE PRA TODA HABILIDADE ATIVA. O cliente manda um ID; quem sabe o que aquilo faz e
	/// o servidor.
	///
	/// E deliberado que nao haja um pacote por tecnica: cada skill nova exigiria um id de
	/// protocolo, uma serializacao e um caso no dispatch -- e sao trezentas. Com um canal so, a
	/// tecnica nova precisa de UMA linha aqui e uma no registro de verbs do cliente.
	/// </summary>
	private void UsarHabilidade(ServerPlayer pl, string id)
	{
		switch (id)
		{
			case "regenerar": Regenerar(pl); break;
			case "mente": EntrarNaMente(pl); break;
			case "sairdamente": SairDaMente(pl, "voce abre os olhos."); break;
			case "decolar": Decolar(pl); break;
			case "voar": AlternarVoo(pl); break;
			// O `Swim` do DM (`Swim.dm:5`). Entra pelo MESMO cano do voo -- uma linha aqui e uma no
			// registro de verbs do cliente, que e o preco que este canal cobra por acao nova.
			case "nadar": AlternarNado(pl); break;
			case "oozaru": ReverterOozaru(pl); break;
			case "oozaru_olhar": OlharParaALua(pl); break;
			// AS TECNICAS DE SKILL entram pelo mesmo cano, pelo nome do verb do DM
			// ("Solar_Flare"). Ver GameServer.Tecnicas.cs.
			default:
				// A LEMBRANCA DE UM CHEFE. Prefixo + argumento, como o `ens_ensinar:<typepath>` do
				// ensino ja fazia: um botao por chefe visto, e nenhum id novo no protocolo. Vem antes
				// das tecnicas porque nao E tecnica -- nao esta no livro que o `UsarTecnica` varre.
				// Ver `GameServer.Mente.cs`.
				if (id.StartsWith("mente_chefe:", StringComparison.Ordinal))
				{ ChamarChefe(pl, id["mente_chefe:".Length..]); break; }
				// AS DISCIPLINAS DIVINAS entram antes das tecnicas de skill: elas nao SAO skills
				// (nao se compram com marco), e por isso nao estao no livro que o UsarTecnica varre.
				if (UsarDisciplina(pl, id)) break;
				// O DISCIPULADO entra pelo mesmo cano, e antes das tecnicas pelo mesmo motivo das
				// disciplinas: ele nao E skill (nao se compra com marco), entao nao esta no livro
				// que o `UsarTecnica` varre. Ver `GameServer.Mestre.cs`.
				if (UsarVerboDeMestre(pl, id)) break;
				// O ENSINO DE SKILL entra aqui e nao no `UsarTecnica` porque ele nao E uma tecnica:
				// o id que chega e `ens_ensinar:<typepath>`, um GESTO sobre uma skill, e nao o verb
				// que a skill destrava. Ver `GameServer.Ensino.cs` -- e o cabecalho de la explica
				// por que ele nao pergunta nada ao discipulado da linha acima.
				if (UsarVerboDeEnsino(pl, id)) break;
				if (!UsarTecnica(pl, id)) Avisar(pl, $"habilidade desconhecida: {id}");
				break;
		}
	}
}
