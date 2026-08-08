using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// OS VERBS -- o canal de comandos soltos que nao merecem opcode proprio.
///
/// ============================ DE ONDE VEM ============================
/// No BYOND todo `mob/verb/X()` virava um botao no painel, agrupado por `set category`. Sao 91
/// verbs so em "Other" e ~90 em "Admin". A maioria e enfeite ou mexe em variavel de balanceamento;
/// o que vale a pena portar e o que o jogador USA -- e o que o dono pediu: "coloque os principais
/// e mais importantes, nao precisa colocar verb que muda variavel tipo o que muda ganhos de BP".
/// =====================================================================
///
/// ============================ A REGRA QUE ME GUIOU ============================
/// SO ENTRA VERB COM MECANICA PRONTA. Um botao que nao faz nada e pior que um botao que nao
/// existe: ele promete. Este projeto ja levou esse tombo varias vezes (o `canPower` extraido e
/// nunca lido, a API do sigilo orfa, os 35 atlas nunca importados), entao cada verb daqui embaixo
/// esta ligado a alguma coisa que ja roda.
/// ==============================================================================
///
/// ADMIN E CONFERIDO AQUI, e nao no cliente. O menu esconde a aba de quem nao e admin porque
/// mostrar seria confuso -- mas esconder botao nunca foi permissao.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// SESSAO DE TREINO (`Modules/Stats/Training/Session.dm`). Guarda o BP de quando comecou e
	/// diz quanto subiu.
	///
	/// O RELATORIO RESPEITA O SIGILO, como no original: com scouter sai o numero absoluto; sem
	/// ele sai a PORCENTAGEM, que diz o quanto voce evoluiu sem entregar o seu proprio BP -- a
	/// mesma regra do painel de Stats.
	/// </summary>
	private void VerboSessao(ServerPlayer pl, bool nova)
	{
		if (nova)
		{
			pl.BpDaSessao = pl.Ficha.BP;
			pl.EmSessao = true;
			Avisar(pl, "sessao de treino iniciada.");
			return;
		}
		if (!pl.EmSessao) { Avisar(pl, "comece uma sessao primeiro."); return; }

		double ganho = pl.Ficha.BP - pl.BpDaSessao;
		if (TemScouter(pl))
			Avisar(pl, $"nesta sessao voce ganhou {ganho:N0} de poder base ({pl.BpDaSessao:N0} -> {pl.Ficha.BP:N0}).");
		else if (pl.BpDaSessao > 0)
			Avisar(pl, $"voce esta cerca de {ganho / pl.BpDaSessao * 100:0.#}% mais forte do que quando comecou.");
		else
			Avisar(pl, "voce sente que evoluiu nesta sessao, mas nao tem como medir o quanto.");
	}

	/// <summary>
	/// Quem esta no mundo agora. E o `Who` do original.
	///
	/// SO GENTE: `_players` guarda tambem os corpos sem dono (o clone da mente, os NPCs). Listar o
	/// clone de alguem aqui nao seria so um numero errado -- seria contar a quem estiver lendo que
	/// aquela pessoa esta dentro da propria mente agora.
	/// </summary>
	private void VerboQuem(ServerPlayer pl)
	{
		var gente = Gente.ToList();
		Avisar(pl, $"-- {gente.Count} no mundo --");
		foreach (ServerPlayer o in gente)
			Avisar(pl, $"  {o.Name} ({o.Race}) em {o.Zone.Name}");
	}

	/// <summary>
	/// O canal unico. Comando + argumento.
	/// </summary>
	private void Verbo(ServerPlayer pl, string cmd, string arg)
	{
		// O BANCO ANTES DO RESTO. Sao tres comandos com prefixo proprio e uma guarda so (estar
		// perto de um caixa), entao eles moram num arquivo separado -- ver GameServer.Banco.cs.
		if (ComandoDeBanco(pl, cmd, arg)) return;
		if (ComandoDeInteracao(pl, cmd, arg)) return;
		if (ComandoDeItem(pl, cmd, arg)) return;

		// O CONVIVIO: conhecidos, amizade, rivais e relacoes. Arquivo proprio pelo mesmo motivo do
		// banco -- e um sistema inteiro com regras proprias, e nao meia duzia de `case`.
		if (ComandoDeConvivio(pl, cmd, arg)) return;

		switch (cmd)
		{
			// ---------------------------------------------------------- todos
			case "sessao_nova": VerboSessao(pl, nova: true); break;
			case "sessao_ver": VerboSessao(pl, nova: false); break;
			case "quem": VerboQuem(pl); break;

			// LIMPAR BUFFS -- o `Clear_Buffs` do original. Existe porque buff preso e um estado que
			// o jogador nao tem como desfazer sozinho, e o motor de buffs ja sabe derrubar tudo.
			case "limpar_buffs":
				DerrubarBuffs(pl);
				Avisar(pl, "voce sacode os efeitos de cima.");
				break;

			// OS CARGOS. O canal `C2S.Cargo` ja fazia as duas coisas (lista com chave vazia,
			// reivindicacao com chave) -- o que faltava era um BOTAO. Sem ele o sistema inteiro de
			// ranks so era alcancavel por quem soubesse que existia.
			case "cargos": MandarCargos(pl); break;

			// KNOCKBACK LIGADO/DESLIGADO -- o `knockbackon` do original, que existe justamente pra
			// treinar com alguem sem arremessar o parceiro pra longe a cada golpe.
			case "knockback":
				pl.Knockback = !pl.Knockback;
				Avisar(pl, pl.Knockback ? "seus golpes voltam a arremessar." : "seus golpes param de arremessar.");
				break;

			// VOLTAR PRO PONTO DE NASCIMENTO. O `Goto_Spawn` do original -- a saida pra quem ficou
			// preso em geometria quebrada, e o motivo de ele existir la tambem.
			case "spawn":
				MoveToZone(pl.Id, SpawnZone, SpawnPos);
				Avisar(pl, "voce volta ao ponto de partida.");
				break;

			// ---------------------------------------------------------- so admin
			// A CONFERENCIA E AQUI, e num lugar so. O cliente esconde a aba de quem nao e admin,
			// mas esconder botao nao e permissao -- um cliente modificado manda o pacote do mesmo
			// jeito. Os comandos moram em `GameServer.Admin.cs`; o que este arquivo garante e que
			// NENHUM deles seja alcancado sem a conferencia acima.
			default:
				if (!cmd.StartsWith("admin_", StringComparison.Ordinal)) break;
				if (!EhAdmin(pl)) { Avisar(pl, "isso e coisa de administrador."); break; }
				if (!VerboDeAdmin(pl, cmd, arg)) Avisar(pl, "esse comando de administrador nao existe.");
				break;
		}
	}
}
