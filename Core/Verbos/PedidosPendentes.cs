namespace Jandirus.Core.Verbos;

/// <summary>
/// **OS PEDIDOS QUE ESPERAM RESPOSTA** -- o convite de mestre, a licao oferecida, a fusao, o potencial... -- e os dois
/// verbs que respondem a cada um.
///
/// Dono, 2026-10-13: *"pedidos como pra ensinar técnica a aluno, convidar um player pra ser aluno, etc deveriam
/// aparecer na tela da pessoa como uma notificaçao (assim como tem de torneio) pra ela aceitar"*.
///
/// ============================ O QUE JA EXISTIA, E O QUE FALTAVA ============================
/// No DM cada pedido destes e um `alert()` ou um `input()` que TRAVA a tela de quem recebe ate ele responder. Num
/// servidor autoritativo nao ha caixa bloqueante, e o port trocou todos por um pendente com prazo, respondido por
/// verb. O pendente existia inteiro -- e so aparecia como uma linha no chat e um botao no fundo de uma aba do menu P
/// (que nascia e sumia com o fato, `RequisitosDosVerbos`). Quem nao estava olhando o chat nao via o convite.
///
/// O QUE ENTROU e a tela: o servidor deriva, do MESMO estado de que ja tirava os fatos, a lista dos pedidos pendentes
/// de cada jogador (o texto e quanto falta do prazo), e o cliente a mostra num cartao no canto, com os dois botoes.
/// Os botoes mandam os MESMOS verbs do menu P -- esta tabela so diz quais sao.
///
/// **E UMA TABELA, E NAO UMA BUSCA NO REGISTRO**, porque o `Requisito` e um predicado opaco: nao da pra perguntar a
/// ele "de que fato voce depende?". A bancada (`--v021teste`) cobra a outra metade: cada chave daqui tem de existir no
/// registro e so se acender com o fato da linha dela.
/// ========================================================================================
/// </summary>
public static class PedidosPendentes
{
	/// <summary>
	/// DEFEITO INJETADO (bancadas `--v021teste` e `--diagv021`): nenhum pedido vira notificacao -- a v0.2.0, em que o
	/// convite era uma linha no chat. Lido pelo `GameServer.PedidosDe`. Falso em jogo, sempre.
	/// </summary>
	public static bool SemNotificacaoDeTeste;

	/// <summary>Os fatos que SAO um pedido esperando resposta, na ordem em que os cartoes se empilham.</summary>
	public static readonly Fatos[] Todos =
	[
		Fatos.ConviteDeMestre, Fatos.LicaoOferecida, Fatos.ConviteDeFusao, Fatos.ConviteDeAbsorcao,
		Fatos.OfertaDePotencial, Fatos.OfertaDeJuventude, Fatos.PedidoDeGenkidama, Fatos.OfertaDeAssento,
		Fatos.DesafioAoTitulo, Fatos.PedidoDeFilho, Fatos.OfertaDeAprendiz,
	];

	/// <summary>
	/// As chaves (do `RequisitosDosVerbos`) dos verbs que respondem SIM e NAO a este pedido. Vazias pra um fato que nao
	/// e pedido.
	/// </summary>
	public static (string Sim, string Nao) Respostas(Fatos pedido) => pedido switch
	{
		Fatos.ConviteDeMestre => (RequisitosDosVerbos.PrefixoDeHabilidade + "mst_aceitar", RequisitosDosVerbos.PrefixoDeHabilidade + "mst_recusar"),
		Fatos.LicaoOferecida => (RequisitosDosVerbos.PrefixoDeHabilidade + "ens_licao_sim", RequisitosDosVerbos.PrefixoDeHabilidade + "ens_licao_nao"),
		Fatos.ConviteDeFusao => (RequisitosDosVerbos.PrefixoDeHabilidade + "fus_sim", RequisitosDosVerbos.PrefixoDeHabilidade + "fus_nao"),
		Fatos.ConviteDeAbsorcao => (RequisitosDosVerbos.PrefixoDeHabilidade + "fus_namek_sim", RequisitosDosVerbos.PrefixoDeHabilidade + "fus_nao"),
		Fatos.OfertaDePotencial => (RequisitosDosVerbos.ChaveDoVerbo("potencial_aceitar"), RequisitosDosVerbos.ChaveDoVerbo("potencial_recusar")),
		Fatos.OfertaDeJuventude => (RequisitosDosVerbos.ChaveDoVerbo("juventude_aceitar"), RequisitosDosVerbos.ChaveDoVerbo("juventude_recusar")),
		Fatos.PedidoDeGenkidama => (RequisitosDosVerbos.ChaveDoVerbo("genkidama_doar"), RequisitosDosVerbos.ChaveDoVerbo("genkidama_negar")),
		Fatos.OfertaDeAssento => (RequisitosDosVerbos.ChaveDoVerbo("cargo_nomear_aceitar"), RequisitosDosVerbos.ChaveDoVerbo("cargo_nomear_recusar")),
		Fatos.DesafioAoTitulo => (RequisitosDosVerbos.ChaveDoVerbo("cargo_duelo_aceitar"), RequisitosDosVerbos.ChaveDoVerbo("cargo_duelo_adiar")),
		Fatos.PedidoDeFilho => (RequisitosDosVerbos.PrefixoDeHabilidade + "filho_sim", RequisitosDosVerbos.PrefixoDeHabilidade + "filho_nao"),
		Fatos.OfertaDeAprendiz => (RequisitosDosVerbos.ChaveDoVerbo("aprendiz_aceitar"), RequisitosDosVerbos.ChaveDoVerbo("aprendiz_recusar")),
		_ => ("", ""),
	};

	/// <summary>
	/// O que os dois botoes do cartao dizem. "Aceitar" e "Recusar" pra quase todos; os tres que fogem disso dizem o
	/// que o gesto FAZ (dar a energia, entregar o personagem, adiar o duelo), porque nesses o "sim" custa.
	/// </summary>
	public static (string Sim, string Nao) Rotulos(Fatos pedido) => pedido switch
	{
		Fatos.ConviteDeAbsorcao => ("Aceitar (perco o personagem)", "Recusar"),
		Fatos.PedidoDeGenkidama => ("Dar a energia", "Negar"),
		Fatos.DesafioAoTitulo => ("Aceitar o duelo", "Adiar"),
		_ => ("Aceitar", "Recusar"),
	};
}
