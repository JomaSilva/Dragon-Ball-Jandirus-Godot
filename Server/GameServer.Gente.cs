using Jandirus.Core.Npc;

namespace Jandirus.Server;

/// <summary>
/// ============================ A PORTA DO SERVIDOR PRA "QUEM E GENTE" ============================
/// Tres linhas, e elas nao decidem nada: quem decide e <see cref="Gente"/>, no `Core/`. O que mora
/// aqui e so a traducao de `ServerPlayer` pros tres marcadores -- o `Core` nao conhece `NetPeer`.
///
/// **NAO ESCREVA `p.Peer != null` PRA DECIDIR REGRA DE MUNDO.** Ele continua certo pra a pergunta
/// "tem canal pra receber pacote" (todo `Avisar`/`Send` e assim, e ali `Peer` E a pergunta), e
/// continua ERRADO pra "isto conta como jogador": ver o cabecalho do `Core/Npc/Gente.cs`.
/// ==========================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// ISTO E UM JOGADOR? -- o funil unico. Ver <see cref="Gente.EhJogador"/> pros casos dificeis
	/// (Oozaru possuido E jogador; clone, boneco, fera e NPC nao sao).
	/// </summary>
	private static bool EhJogador(ServerPlayer p) =>
		Gente.EhJogador(p.Peer != null, p.Papel, p.DonoDoCorpoLargado);

	/// <summary>ISTO E UM CORPO DO MUNDO (NPC)? Ver <see cref="Gente.EhNpcDoMundo"/>.</summary>
	private static bool EhNpcDoMundo(ServerPlayer p) => Gente.EhNpcDoMundo(p.Peer != null, p.Papel);

	/// <summary>
	/// SO GENTE DE VERDADE.
	///
	/// `_players` guarda tambem os corpos SEM DONO -- o clone da mente, o boneco do corpo largado e os
	/// NPCs do povoamento e das sagas. Eles andam no snapshot como qualquer um, e por isso todo laco
	/// "em massa" que esquecer este filtro vai curar, arrastar e contar robo junto com jogador. "3 no
	/// mundo" quando ha uma pessoa so e um numero que faz o admin duvidar do painel inteiro.
	///
	/// Ela se chamava `Gente` e testava `p.Peer != null` a mao. O nome mudou porque `Gente` agora e a
	/// REGRA (no `Core/`) e nao a lista, e a conta a mao morreu junto: era um marcador so, e o painel
	/// do admin passaria a contar o boneco do corpo largado no dia em que ele entrasse na lista.
	/// </summary>
	private IEnumerable<ServerPlayer> Jogadores => _players.Values.Where(EhJogador);
}
