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
		var gente = Jogadores.ToList();
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

		// OS TRES VERBOS DE FIXTURE DA BANCADA DO EMBARQUE, e so com a flag: sem ela este `if` e
		// falso antes de olhar o `cmd`, e um cliente mexido que os mande num servidor de verdade cai
		// em "comando desconhecido" como qualquer outra palavra inventada. Ver
		// GameServer.EmbarqueTeste.cs.
		if (_embarqueDeTeste && ComandoDaBancadaDeEmbarque(pl, cmd, arg)) return;

		if (ComandoDeInteracao(pl, cmd, arg)) return;
		if (ComandoDeItem(pl, cmd, arg)) return;

		// O CONVIVIO: conhecidos, amizade, rivais e relacoes. Arquivo proprio pelo mesmo motivo do
		// banco -- e um sistema inteiro com regras proprias, e nao meia duzia de `case`.
		if (ComandoDeConvivio(pl, cmd, arg)) return;

		// A CRIACAO DE TECNICAS DE KI. Arquivo proprio, e prefixo `ca_` proprio, pela mesma razao do
		// banco: sao dez comandos com um estado compartilhado (a mesa aberta) -- ver
		// `GameServer.Customizadas.cs`.
		if (ComandoDeTecnicaCustomizada(pl, cmd, arg)) return;

		// A CONQUISTA DE PLANETAS: prefixo `conq_`, dois arquivos proprios (o livro dos dominios e a
		// invasao). Mesma razao do banco e das tecnicas -- e um sistema com estado compartilhado
		// (dominios vivos, invasoes em andamento, canais de arranque) e nao meia duzia de `case`.
		if (ComandoDeConquista(pl, cmd, arg)) return;

		// O REFUGIO: para onde vai quem ficou sem planeta natal, e a escolha entre o dominio
		// conquistado e a vizinhanca de casa. Vem LOGO DEPOIS da conquista porque escreve no mesmo
		// campo que ela (`Dominio.EhOSpawn`) -- ver `GameServer.Refugio.cs`.
		if (ComandoDeRefugio(pl, cmd, arg)) return;

		// AS ESFERAS DO DRAGAO: prefixo `db_`, e as SUPER com prefixo `sdb_`. Dois arquivos proprios
		// pela mesma razao do banco e da conquista -- sao dois sistemas com estado compartilhado
		// (sets, esferas espalhadas, invocacoes de um lado; claims e canais de disputa do outro) e
		// nao meia duzia de `case`. Sao DOIS e nao um porque no original tambem sao: a esfera comum
		// se carrega na mao, a Super tem o tamanho de um planetoide e se reivindica parado.
		if (ComandoDeEsferas(pl, cmd, arg)) return;
		if (ComandoDeSuperEsferas(pl, cmd, arg)) return;

		// AS PORTAS DOS CARGOS: prefixo `cargo_`, arquivo proprio (`GameServer.CargoPortas.cs` e
		// `GameServer.CargoDuelo.cs`) pela mesma razao do banco -- nomeacao, sucessao e duelo formal
		// sao um sistema com estado compartilhado (tronos, convites, linha de sucessao, o relogio do
		// titulo) e nao meia duzia de `case`.
		if (ComandoDeCargo(pl, cmd, arg)) return;

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

			// A FICHA MORAL. Ela nasce junto com o karma ter finalmente PRODUTORES (ver
			// `GameServer.Karma.cs`): um eixo que decide nove cargos, que nao aparece em barra
			// nenhuma e que so se mexe em combate precisa de um lugar onde a pessoa possa olha-lo.
			// No original nao ha este verb -- o numero so aparece nas falas do Enma e do Sr. Kaioh,
			// e quem nunca morreu joga o jogo inteiro sem saber que o eixo existe.
			case "karma": VerboVerKarma(pl); break;

			// A ESCOLHA UNICA DE UMA SKILL -- `skill_escolha <typepath> <casa>`.
			//
			// ============================ POR QUE ISTO E UM VERBO E NAO UM OPCODE ============================
			// O DM pergunta com um `input()` NA HORA de aprender (`meta.dm:105`) e guarda a resposta
			// no datum. Aqui a pergunta nao pode viajar no `C2S.Aprender`: ele carrega um typepath e
			// mais nada, e o jogador tem que poder responder DEPOIS -- inclusive num relog em que a
			// skill ja esta no livro e a escolha ainda nao foi feita.
			//
			// O `C2S.Verbo` ja existe pra exatamente isto: comando + argumento, "verb novo e uma
			// linha no switch e o contrato de rede nao muda". Uma skill no jogo inteiro usa este
			// canal; abrir um opcode pra ela seria caro e nao ficaria mais claro.
			// ==============================================================================================
			case "skill_escolha": VerboEscolhaDeSkill(pl, arg); break;

			// ============================ A TECLA DE UMA FORMA -- `forma <id>` ============================
			// O jogador ligou uma tecla a uma transformacao (ver `Client/TelaDeTeclas.cs`). Ate aqui
			// nao havia como PEDIR uma forma: o duplo toque no C manda uma direcao (`C2S.Transformar`
			// carrega um bool) e o servidor escolhe o degrau mais forte que couber.
			//
			// **NAO ABRIU OPCODE**, e isso e a metade da decisao. `C2S.Verbo` ja e comando + argumento
			// e ja atravessa o jogo inteiro; um opcode novo pra carregar um id de texto seria contrato
			// de rede novo pra nao ganhar nada. A outra metade e onde ele cai: `TransformarPara` passa
			// pelo `EstadoDeForma.Avaliar` como qualquer subida, com as recusas valendo e a frase que a
			// tecla C diria -- ver o cabecalho dela.
			//
			// FORA DO PREFIXO `admin_` de proposito: e comando de JOGADOR. O `admin_forma` existe e faz
			// outra coisa -- ele ignora os requisitos, porque serve pra testar as formas trancadas
			// (`GameServer.Admin.cs`). Uma tecla que caisse nele seria um atalho que pula o portao.
			// ==========================================================================================
			case "forma": TransformarPara(pl, arg); break;

			// OS GRAUS DO SSJ NO CAMINHO DA TECLA C -- liga, desliga e conta em que pe ficou. Mora
			// junto do `knockback` la embaixo por parentesco: os dois sao preferencia de JOGADOR
			// sobre uma mecanica que ja existe, e nenhum dos dois muda o que e permitido. A
			// diferenca e que este PERSISTE (ver `VerboGrades`) -- o knockback nasce ligado a cada
			// sessao porque atrapalhar o treino do parceiro e temporario; por onde a sua escada
			// sobe, nao.
			case "graus": VerboGrades(pl); break;

			// A ASSINATURA DO UNIVERSO PELA PONTA DO SERVIDOR -- ver GameServer.Universo.cs.
			//
			// FORA DA FAIXA DE ADMIN de proposito: ela nao revela nada que o cliente ja nao tenha
			// (a seed chega no login, e o universo e funcao pura dela). O que ela faz e deixar o
			// cliente COMPARAR a enumeracao dele com a do servidor -- que e a regra 0.2 virando
			// checagem em vez de promessa. O teto do tamanho da regiao mora la.
			case "universo_assinatura": VerboAssinaturaDoUniverso(pl, arg); break;

			// KNOCKBACK LIGADO/DESLIGADO -- o `knockbackon` do original, que existe justamente pra
			// treinar com alguem sem arremessar o parceiro pra longe a cada golpe.
			case "knockback":
				pl.Knockback = !pl.Knockback;
				Avisar(pl, pl.Knockback ? "seus golpes voltam a arremessar." : "seus golpes param de arremessar.");
				break;

			// ============================ O `Goto Spawn` SAIU DAQUI -- DECISAO DO DONO ============================
			// Ele era `case "spawn"` e mandava QUALQUER jogador pro proprio berco (`MandarProBerco`).
			// O dono mandou tira-lo das maos do jogador e deixa-lo so pra admin, e o motivo e a Sala
			// do Tempo: a regra 13.6c diz que a sala **prende** quem passa dos 50 minutos, e a porta
			// e a passagem ja recusavam o preso -- mas este verb teleportava sem perguntar nada a
			// ninguem. Enquanto ele existisse pro jogador, a tranca era decorativa: bastava apertar
			// um botao do menu "Other" pra sair andando de dentro da prisao.
			//
			// **NAO HA `case "spawn"` NENHUM MAIS.** Ele nasceu de novo como `admin_spawn`
			// (`GameServer.Admin.cs`), que cai no funil de admin logo abaixo -- e o funil e o unico
			// lugar do jogo que confere `EhAdmin`. Um cliente mexido que mande "spawn" cai no
			// `default`, nao comeca com `admin_`, e nao acontece nada.
			//
			// A SAIDA DE EMERGENCIA DO JOGADOR (o motivo de o `Goto_Spawn` existir no DM -- ficar
			// preso em geometria quebrada) passou a ser pedir a um admin. E o preco de a prisao da
			// Sala ser de verdade; ver `GameServer.SalaDoTempo.cs`.
			// ==================================================================================================

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
