using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A SALA DO TEMPO NA ABA WORLD -- a PISTA de que o mundo em que se esta guarda uma. Pedido do dono de 2026-10-11;
/// a regra esta em `Core/World/SalaDePlaneta.cs`.
///
/// E FUNCAO PURA DA SEED, como a atmosfera divina: o cliente responde sozinho, do mesmo jeito que o servidor, e
/// nenhum byte viaja. A linha so nasce NO mundo que tem a sala -- a carta estelar NAO a marca, de proposito: o
/// dono pediu *"de forma BEM RARA"*, e um anel na carta trocaria achar por ir buscar. Quem pousa num mundo desses e
/// abre a aba fica sabendo; a porta em si esta na parede de montanha mais perto do pouso, e a DILATACAO quem diz e
/// ela ("Ler as regras da porta").
/// </summary>
public partial class MenuJogo
{
	/// <summary>A cor da linha: o azul-claro do vazio da Sala.</summary>
	internal static readonly Color CorDaSalaDoTempo = new("bfe8ff");

	/// <summary>O chao em que o cliente esta guarda uma Sala do Tempo? So mundo sorteado pode guardar.</summary>
	internal static bool MundoComSalaDoTempo(GameClient cli) => SalaDePlaneta.Tem(cli.Zone);

	/// <summary>A LINHA DA SALA no cartao Ceu. So aparece onde ha o que dizer: o mundo comum nao ganha linha.</summary>
	private static void LinhaDaSalaDoTempo(GameClient cli, Control corpoDoCeu)
	{
		if (!MundoComSalaDoTempo(cli)) return;
		Linha("Sala do Tempo", "há uma porta na rocha", CorDaSalaDoTempo, corpoDoCeu);
		Nota("Este mundo guarda uma Sala do Tempo. A porta fica numa parede de montanha perto de onde se pousa; "
			 + "chegue perto dela e aperte E -- as regras da porta dizem quanto o tempo dilata lá dentro.", corpoDoCeu);
	}
}
