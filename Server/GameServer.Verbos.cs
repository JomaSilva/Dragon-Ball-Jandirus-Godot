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
	/// HOSPEDOU, E ADMIN.
	///
	/// O pedido do dono foi direto: "o host sempre e adm". Faz sentido e e o que o original ja
	/// fazia por outro caminho (`AdminCheck` dava nivel 6 pra quem subia o mundo): quem esta com o
	/// servidor rodando na propria maquina ja pode desligar tudo -- negar-lhe os verbs seria teatro.
	///
	/// QUEM E O HOST: a conexao que vem do LOOPBACK enquanto o servidor foi subido pelo botao
	/// "Hospedar" (nao pelo `--server` dedicado). Num servidor dedicado ninguem ganha admin de
	/// graca, que e o certo -- la o dono da maquina nao esta jogando.
	/// </summary>
	public bool SubidoPeloJogador;

	private bool EhHost(NetPeer? peer) =>
		SubidoPeloJogador && peer != null
		&& (peer.Address.ToString().Contains("127.0.0.1", StringComparison.Ordinal)
			|| peer.Address.ToString().Contains("::1", StringComparison.Ordinal));

	private static bool EhAdmin(ServerPlayer pl) => (pl.Poderes & Protocol.Poder.Admin) != 0;

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

	/// <summary>Quem esta no mundo agora. E o `Who` do original.</summary>
	private void VerboQuem(ServerPlayer pl)
	{
		Avisar(pl, $"-- {_players.Count} no mundo --");
		foreach (ServerPlayer o in _players.Values)
			Avisar(pl, $"  {o.Name} ({o.Race}) em {o.Zone.Name}");
	}

	/// <summary>
	/// O canal unico. Comando + argumento.
	/// </summary>
	private void Verbo(ServerPlayer pl, string cmd, string arg)
	{
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
			// A CONFERENCIA E AQUI. O cliente esconde a aba de quem nao e admin, mas esconder
			// botao nao e permissao -- um cliente modificado manda o pacote do mesmo jeito.
			case "admin_curar" when EhAdmin(pl): AdminCurar(pl, arg); break;
			case "admin_ir" when EhAdmin(pl): AdminIr(pl, arg); break;
			case "admin_trazer" when EhAdmin(pl): AdminTrazer(pl, arg); break;
			case "admin_kb" when EhAdmin(pl): AdminNocautear(pl, arg); break;
			case "admin_quem" when EhAdmin(pl): VerboQuem(pl); break;

			default:
				if (cmd.StartsWith("admin_", StringComparison.Ordinal))
					Avisar(pl, "isso e coisa de administrador.");
				break;
		}
	}

	/// <summary>
	/// Acha alguem pelo ID que o cliente mandou -- e o ALVO MARCADO dele.
	///
	/// Id e nao nome: nome se repete e nome se digita errado, e o jogador ja marca gente com duplo
	/// clique pra tudo o mais. Nulo/0 = "eu mesmo", que e o que os verbs que curam querem.
	/// </summary>
	private ServerPlayer? PorNome(string idTexto) =>
		int.TryParse(idTexto, out int id) && id != 0 && _players.TryGetValue(id, out ServerPlayer? p) ? p : null;

	private void AdminCurar(ServerPlayer adm, string alvo)
	{
		ServerPlayer? p = PorNome(alvo) ?? adm;   // sem alvo marcado, cura quem pediu

		p.Ficha.HP = 100;
		p.Ficha.Ki = p.Ficha.MaxKi;
		p.Ficha.KO = false;
		p.Ficha.dead = false;
		foreach (Jandirus.Core.Combat.BodyPart parte in p.Combate.Corpo.Partes)
		{
			parte.Vida = 100;
			parte.Decepado = false;
		}
		Avisar(adm, $"{p.Name} curado.");
		if (p != adm) Avisar(p, "voce se sente inteiro de novo.");
	}

	private void AdminIr(ServerPlayer adm, string alvo)
	{
		ServerPlayer? p = PorNome(alvo);
		if (p == null) { Avisar(adm, $"nao achei '{alvo}'."); return; }
		MoveToZone(adm.Id, p.Zone, p.Pos);
		Avisar(adm, $"voce vai ate {p.Name}.");
	}

	private void AdminTrazer(ServerPlayer adm, string alvo)
	{
		ServerPlayer? p = PorNome(alvo);
		if (p == null) { Avisar(adm, $"nao achei '{alvo}'."); return; }
		MoveToZone(p.Id, adm.Zone, adm.Pos);
		Avisar(adm, $"voce traz {p.Name}.");
		Avisar(p, "uma forca invisivel te puxa.");
	}

	private void AdminNocautear(ServerPlayer adm, string alvo)
	{
		ServerPlayer? p = PorNome(alvo);
		if (p == null) { Avisar(adm, $"nao achei '{alvo}'."); return; }
		p.Ficha.KO = true;
		p.Ficha.HP = 1;
		Avisar(adm, $"{p.Name} cai no chao.");
	}
}
