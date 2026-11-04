namespace Jandirus.Server;

/// <summary>
/// ============================ DO MESMO IP NADA RENDE (dono, 2026-10-27, o pedido 110) ============================
/// O pedido dos jogadores: *"alt farming (BATTLE POWER WISE) shouldnt be allowed, make them give 0 bp to each other if in the same ip"*. E a
/// resposta do dono, mais larga: *"personagens com mesmo ip n conseguiriam fazer nada entre eles, nem gerar raiva, nem dar bp, nem dar
/// unlocked, nem zenkai absolutamente nada pra balancear"*.
///
/// UMA PERGUNTA, NOS FUNIS: dois corpos de PESSOA conectados do mesmo endereco. Ela entra onde um personagem rende alguma coisa a outro:
///   * a LUTA (`FatorDaLuta`, os dois lados de todo golpe -- soco, ki e tecnica): zero;
///   * a DERROTA (`AoPerderALuta`): nem Zenkai, nem o luto de quem viu, nem o do aliado, nem convivio, nem sucessao -- a fusao que se desfaz
///     na queda continua, que e do corpo;
///   * o LUTO DE QUEM ASSISTE (`LutoNaVizinhanca`): ninguem se enfurece por um corpo da propria conexao, caido ou algoz;
///   * LIBERAR O POTENCIAL de outro (`LiberarOPotencial`, o ritual do Kaioshin -- o dono tirou ate as coisas de profissao);
///   * a LICAO (skill e tecnica inventada), o MESTRE e o despertar assistido, e o SAQUE de quem esta caido.
/// (2026-11-01, o pedido 212) E NADA SE USA UM NO OUTRO -- o dono: *"N permita personagens de mesmo ip usarem revive , potential unlock
/// ou qualquer coisa no outro"*. Ver <see cref="UsoNoMesmoIp"/>.
/// O endereco e o do pacote (`NetPeer.Address`); NPC e corpo sem conexao nao tem, e nunca e "do mesmo IP". A bancada da um endereco de
/// mentira aos corpos forjados (<see cref="IpDeTeste"/>).
/// ====================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>SO PRA BANCADA: o endereco de um corpo forjado (que nao tem conexao). Vazio em jogo.</summary>
	internal readonly Dictionary<int, string> IpDeTeste = [];

	/// <summary>O endereco de onde este corpo joga, ou nulo (NPC, corpo sem conexao).</summary>
	private string? IpDe(ServerPlayer p) => p.Peer?.Address?.ToString() ?? IpDeTeste.GetValueOrDefault(p.Id);

	/// <summary>
	/// NO PROCESSO DE BANCADA A REGRA SO VALE QUANDO A BANCADA DELA PEDE (a `--mesmoipteste`): as outras ligam DOIS CLIENTES CRUS pelo
	/// loopback (a `--ganhoteste` mede a troca de golpes entre eles) -- e os dois vem do 127.0.0.1, que e o mesmo IP. E o mesmo molde da
	/// Sala fechada (`SalaFechadaNaBancadaDeTeste`). Em jogo, sempre valendo.
	/// </summary>
	internal static bool MesmoIpNaBancadaDeTeste;

	/// <summary>Estes dois corpos sao personagens jogados da MESMA conexao (o mesmo IP)? Um corpo consigo mesmo nao conta.</summary>
	internal bool MesmoIp(ServerPlayer? a, ServerPlayer? b) =>
		(!ProcessoDeBancada.Value || MesmoIpNaBancadaDeTeste)
		&& a != null && b != null && a != b && EhPessoa(a) && EhPessoa(b) && IpDe(a) is { } ia && ia == IpDe(b);

	/// <summary>O que ouve quem tenta render alguma coisa a um personagem da propria conexao.</summary>
	internal const string FraseDoMesmoIp =
		"vocês jogam da mesma conexão (o mesmo IP): entre personagens da mesma conexão nada rende -- nem treino, nem raiva, nem Zenkai, nem lição.";

	/// <summary>
	/// (2026-11-01, o pedido 212) ESTE CORPO E DA MESMA CONEXAO DE QUEM USA ALGUMA COISA NELE? A pergunta UNICA das portas em que um personagem
	/// usa algo EM outro: a tecnica que acha o alvo sozinha (`AlvoDeTecnica`: o revive de cargo, as absorcoes...) o pula; a cura, a oferta
	/// do potencial, a juventude, o corpo que fica, a chave da Sala, o Anciao, o herdeiro, a telepatia, o observar, o alvo do Instant
	/// Transmission, os brincos, a fusao e os desejos pra outro o recusam com a frase (<see cref="RecusaPeloMesmoIp"/>); as caronas, o agarrado
	/// que viaja, o convite da Genkidama e o aprendiz do Kaioshin o deixam de fora. O que ja era do pedido 110 (a luta, a derrota, o luto, o
	/// ritual do potencial, a licao, o mestre, o saque) continua no <see cref="MesmoIp"/>.
	/// </summary>
	internal bool UsoNoMesmoIp(ServerPlayer quem, ServerPlayer? alvo) => MesmoIp(quem, alvo);

	/// <summary>
	/// (2026-11-01, o pedido 212) A RECUSA NA PORTA: verdadeiro (e quem usou ouve a frase) quando o alvo e da mesma conexao -- quem chama so
	/// sai, antes de mexer em qualquer coisa.
	/// </summary>
	internal bool RecusaPeloMesmoIp(ServerPlayer quem, ServerPlayer? alvo)
	{
		if (!UsoNoMesmoIp(quem, alvo)) return false;
		Avisar(quem, FraseDoUsoNoMesmoIp);
		return true;
	}

	/// <summary>(2026-11-01, o pedido 212) O que ouve quem tenta usar alguma coisa num personagem da propria conexao.</summary>
	internal const string FraseDoUsoNoMesmoIp =
		"vocês jogam da mesma conexão (o mesmo IP): um personagem não usa nada no outro da mesma conexão.";

	/// <summary>(2026-11-01, o pedido 212) O motivo curto, pra lista que ja diz "não dá: ..." (o aprendiz do Kaioshin).</summary>
	internal const string MotivoDoMesmoIp = "vocês jogam da mesma conexão (o mesmo IP)";

	/// <summary>
	/// (2026-11-01, o pedido 212) Quando cada corpo ouviu a frase pela tecnica que acha o alvo sozinha (relogio do servidor): o revive procura
	/// DUAS vezes (o morto, e depois quem esta ao lado), e a frase sai uma.
	/// </summary>
	private readonly Dictionary<int, long> _ultimaFraseDoUsoNoMesmoIp = [];

	/// <summary>A frase do <see cref="FraseDoUsoNoMesmoIp"/>, no maximo uma vez a cada dois segundos por corpo.</summary>
	private void AvisarDoUsoNoMesmoIpUmaVez(ServerPlayer quem)
	{
		long agora = NowMs();
		if (_ultimaFraseDoUsoNoMesmoIp.TryGetValue(quem.Id, out long antes) && agora - antes < 2_000) return;
		_ultimaFraseDoUsoNoMesmoIp[quem.Id] = agora;
		Avisar(quem, FraseDoUsoNoMesmoIp);
	}
}
