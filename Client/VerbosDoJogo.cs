namespace Jandirus.Client;

/// <summary>
/// OS VERBS QUE TODO PERSONAGEM TEM -- as abas "Other" e "Admin" do menu.
///
/// ============================ O QUE ENTROU E O QUE NAO ============================
/// O original tem 91 verbs em "Other" e ~90 em "Admin". O dono pediu "os principais e mais
/// importantes... nao precisa colocar verb que muda variavel tipo o que muda ganhos de BP".
///
/// A regra que usei pra cortar: **so entra verb com mecanica pronta**. Um botao que nao faz nada e
/// pior que um botao que nao existe, porque ele promete -- e este projeto ja tropecou nisso mais de
/// uma vez. Ficaram de fora, por enquanto, os que dependem de sistema nao portado (casamento,
/// faccao, torneio, biografia, musica em streaming, itens) e os de balanceamento.
/// ==================================================================================
///
/// OS DE ADMIN AGEM SOBRE O ALVO MARCADO, e nao sobre um nome digitado. O BYOND tinha `input()`
/// bloqueante pra pedir o nome; aqui ja existe um gesto melhor e que o jogador ja usa -- duplo
/// clique marca alguem. Menos codigo, menos caixa de dialogo, e funciona com nome repetido.
///
/// A ABA ADMIN SO EXISTE PRA QUEM E ADMIN, e quem decide isso e o servidor
/// (<see cref="Jandirus.Net.Protocol.Poder.Admin"/>, ver `MenuJogo.Abas`). Esconder o botao e
/// conveniencia; a permissao e conferida DE NOVO no servidor, sempre.
/// </summary>
public static class VerbosDoJogo
{
	private static bool _registrados;

	private static GameClient? C => GameClient.Instance;

	/// <summary>Ha alguem marcado? E o que os verbs de admin exigem.</summary>
	private static bool TemAlvo() => (C?.AlvoId ?? 0) != 0;

	private static void NoAlvo(string cmd)
	{
		int id = C?.AlvoId ?? 0;
		if (id == 0) { Chat.Sistema("marque alguem antes (duplo clique nele)."); return; }
		C?.SendVerbo(cmd, id.ToString());
	}

	/// <summary>Chamado quando o personagem entra no mundo. Idempotente.</summary>
	public static void Registrar()
	{
		if (_registrados) return;
		_registrados = true;

		// =====================================================================
		// OTHER
		// =====================================================================
		Verbos.Registrar(new Verbo("Training Session", Verbos.Outros,
			"Comeca uma sessao de treino: marca o seu poder de agora pra medir o ganho depois.",
			() => C?.SendVerbo("sessao_nova")));

		Verbos.Registrar(new Verbo("View Session", Verbos.Outros,
			"Quanto de poder voce ganhou desde que a sessao comecou.",
			() => C?.SendVerbo("sessao_ver")));

		// O `knockbackon` do original. Existe pra treinar com alguem sem arremessa-lo a cada golpe.
		Verbos.Registrar(new Verbo("Toggle Knockback", Verbos.Outros,
			"Liga e desliga o arremesso dos SEUS golpes.",
			() => C?.SendVerbo("knockback")));

		Verbos.Registrar(new Verbo("Goto Spawn", Verbos.Outros,
			"Volta ao ponto de partida. A saida pra quem ficou preso.",
			() => C?.SendVerbo("spawn")));

		Verbos.Registrar(new Verbo("Clear Buffs", Verbos.Outros,
			"Derruba todos os efeitos temporarios de cima de voce.",
			() => C?.SendVerbo("limpar_buffs")));

		// OS TRES ABAIXO NAO SAO NOVOS NO SERVIDOR -- os canais ja existiam e ninguem os
		// chamava. Eram sistemas inteiros sem porta de entrada.
		Verbos.Registrar(new Verbo("Ranks", Verbos.Outros,
			"Os cargos do mundo: quem ocupa cada um e o que falta pra voce.",
			() => C?.SendCargo("")));

		Verbos.Registrar(new Verbo("Tech Catalog", Verbos.Outros,
			"O catalogo de construcoes, com o custo e o motivo de cada nao.",
			() => C?.SendTech("lista")));

		Verbos.Registrar(new Verbo("Bolt", Verbos.Outros,
			"Aparafusa (ou solta) a construcao ao seu lado. Sem aparafusar, ela nao funciona.",
			() => C?.SendTech("aparafusar")));

		Verbos.Registrar(new Verbo("Study", Verbos.Outros,
			"Debruca-se sobre a bancada de pesquisa. E o unico jeito de ganhar tecnologia.",
			() => C?.SendTech("estudar")));

		Verbos.Registrar(new Verbo("Who", Verbos.Outros,
			"Quem esta no mundo agora, e onde.",
			() => C?.SendVerbo("quem")));

		// =====================================================================
		// ADMIN -- o servidor confere de novo. Ver GameServer.Verbos.cs.
		// =====================================================================
		Verbos.Registrar(new Verbo("Heal", Verbos.Admin,
			"Cura o alvo marcado por completo. Sem alvo, cura voce.",
			() => C?.SendVerbo("admin_curar", (C?.AlvoId ?? 0).ToString())));

		Verbos.Registrar(new Verbo("Go To Target", Verbos.Admin,
			"Vai ate o alvo marcado.",
			() => NoAlvo("admin_ir"), TemAlvo));

		Verbos.Registrar(new Verbo("Bring Target", Verbos.Admin,
			"Traz o alvo marcado ate voce.",
			() => NoAlvo("admin_trazer"), TemAlvo));

		Verbos.Registrar(new Verbo("Knock Out Target", Verbos.Admin,
			"Poe o alvo marcado no chao.",
			() => NoAlvo("admin_kb"), TemAlvo));

		Verbos.Registrar(new Verbo("Who (admin)", Verbos.Admin,
			"A lista de quem esta no mundo, com a zona de cada um.",
			() => C?.SendVerbo("admin_quem")));
	}

	/// <summary>Zera o registro -- o personagem trocou (volta ao menu de slots).</summary>
	public static void Limpar() => _registrados = false;
}
