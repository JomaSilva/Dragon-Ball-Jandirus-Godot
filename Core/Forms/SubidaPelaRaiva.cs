namespace Jandirus.Core.Forms;

/// <summary>
/// O QUE A RAIVA DE AGORA FAZ COM A ESCADA DE UM CORPO: os degraus JA ESTREADOS que ele sobe sem cena (de baixo
/// pra cima; vazio quando a forma nova se alcanca de onde ele esta) e a forma NOVA que estreia no fim.
/// </summary>
public sealed record PlanoDeSubida(FormaDef Alvo, FormaDef[] Degraus);

/// <summary>
/// ============================ A SUBIDA PELA RAIVA -- QUE FORMA ELA DESPERTA, E POR ONDE ============================
/// A regra do dono, em duas mensagens de 2026-10-11:
///
///   * *"ao bater reqs de transformacao q precisam de raiva, o personagem se transforma AUTOMATICAMENTE (...) vai
///     virar ssj1 pela primeira vez de forma forcada com cinematica e tudo"*;
///   * *"se ele tiver os reqs por exemplo do ssj2 e ele estiver na base e ficar com raiva, ele vai pro ssj1 e
///     comeca a cinematica do ssj2"*.
///
/// Aqui mora a PERGUNTA (que forma, e por quais degraus); quem EXECUTA e o servidor (`GameServer.DespertarPelaRaiva`,
/// pelo `EntrarNaForma` de sempre). Pura de proposito: a bancada `raiva` [12] a mede sem servidor nenhum.
///
/// ============================ A FORMA ============================
/// A que NASCE DA RAIVA (<see cref="Catalogo.NasceDaRaiva"/>: o tronco Saiyajin, o Future, o Heran, o Beast e a
/// linha Legendary inteira), que este corpo NUNCA despertou, e pra qual tudo o mais ja esta pago -- linha, sangue,
/// maestria, poder -- com a raiva de AGORA. Quem confere e o <see cref="EstadoDeForma.Avaliar"/>, o funil de
/// sempre: a raiva e o ultimo passo dele, entao "`Pode` numa forma de raiva nunca despertada" quer dizer
/// exatamente "foi a raiva que acabou de abrir isto".
///
/// VALE PROS DOIS DEGRAUS DE RAIVA, e sem um `if` pra isso: a extrema paga todas pela porta de BP, e a comum paga
/// todas menos o Beast por um poder MAIOR (<see cref="FormaDef.AgioNaRaivaComum"/>, o `if("Angry")` do DM) -- e
/// quem cobra e o proprio `Avaliar`, no passo 9.
///
/// A MAIS FORTE, E SO SE FOR MAIS FORTE QUE A FORMA EM QUE ELE ESTA -- a mesma conta do
/// <see cref="EstadoDeForma.Proxima"/> (*"o mais FORTE que estiver aberto agora"*), e pelo mesmo motivo: a raiva
/// SOBE, nunca desce. O `Avaliar` sozinho nao diz isso -- pra ele o Super Saiyajin continua "aberto" pra quem ja
/// esta no Beast, e despertar por ele puxaria o corpo de 56x pra 2x. So nao e o proprio `Proxima` porque aquele
/// obedece a preferencia dos grades (com eles ligados oferece o Grade 2 antes do SSJ2), que e do gesto do jogador.
///
/// O KI NAO E PERGUNTADO (`kiFracao: 1`), como o `Proxima` tambem nao pergunta: isto e sempre uma ESTREIA, e a
/// estreia enche o tanque.
///
/// ============================ OS DEGRAUS -- DIVERGENCIA DECLARADA DO DM ============================
/// Quando a forma nova so se alcanca de um degrau ACIMA de onde o corpo esta (o passo 5 do `Avaliar`,
/// `RecusaForma.ForaDeOrdem`), o corpo SOBE SOZINHO pelos degraus que ja sao dele ate chegar la. No DM ele nao
/// sobe: o despertar do Super Saiyajin 2 exige estar transformado (`supersaiyan.dm:122`,
/// `!savant.hasssj2&amp;&amp;savant.ssj&amp;&amp;...`), e os do Legendary tambem (`:89`, `savant.lssj==1`; `:97`,
/// `savant.lssj==2`) -- la, quem esta na base com o Super Saiyajin desbloqueado e o poder do 2 nao desperta nada.
/// Pedido do dono, e a palavra dele vence o original.
///
/// GENERALIZADO PELA ESCADA DO CATALOGO, sem id escrito: a corrente e a do <see cref="Catalogo.Anterior"/> (o
/// tronco da linha), descida ate um degrau que se alcance de onde o corpo esta, e depois SUBIDA de volta
/// perguntando ao mesmo funil de onde o corpo ESTARA (<see cref="EstadoDeForma.AvaliarEstandoEm"/>). Tres
/// consequencias, e as tres sao de proposito:
///
///   * SO DEGRAU JA ESTREADO (<see cref="EstadoDeForma.JaViuAEstreia"/>). Atravessar calado um degrau que ele
///     nunca vestiu queimaria a estreia dele -- com o Super Saiyajin e o 2 ambos por despertar, a raiva estreia o
///     1; o 2 vem quando a primeira cena soltar o corpo, se a raiva ainda durar (sao duas estreias, uma depois da
///     outra). "Liberado" nao basta: o Mistico dado pelo ritual e nunca vestido ainda deve a cena dele;
///   * OS GRADES NAO ENTRAM NO CAMINHO: o `Anterior` pula os ramos laterais (`FormaDef.ForaDoTronco`). E quem JA
///     esta num grade conta como no Super Saiyajin ou acima (o passo 5 do `Avaliar`) -- estreia o 2 de onde esta;
///   * CADA DEGRAU DO MEIO COBRA A PROPRIA PORTA (poder, classe, o que mais ele pedir). A raiva nao: forma ja
///     despertada dispensa a furia pra sempre (`Avaliar`, passo 9).
///
/// "SO PRA CIMA" CONTINUA VALENDO PELA FORMA DO FIM: os degraus do meio podem valer menos que a forma em que o
/// corpo esta (ele os atravessa no mesmo instante, sem cena), mas a forma que estreia tem que valer mais.
/// ====================================================================================================================
/// </summary>
public static class SubidaPelaRaiva
{
	/// <summary>
	/// DEFEITO INJETADO (bancadas `raiva` [12] e `--raivateste`): a raiva so desperta a forma que se alcanca DE ONDE
	/// O CORPO ESTA -- o port ate 2026-10-11, e o DM (`supersaiyan.dm:122`). Na base, com o Super Saiyajin ja
	/// despertado e o poder do 2 pago, nada acontece. Lido pelo <see cref="DegrausAte"/>.
	/// </summary>
	public static bool NaoSobeDegrausDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancadas `raiva` [12] e `--raivateste`): a raiva desperta a forma que abriu MESMO que ela
	/// seja mais fraca que a forma em que o corpo esta -- a primeira versao deste despertar, que a `--formasteste`
	/// pegou: quem chegava ao Beast (56x) enfurecido era puxado pro Super Saiyajin (2x). Lido pelo
	/// <see cref="Planejar"/>.
	/// </summary>
	public static bool PraBaixoDeTeste;

	/// <summary>O caminho de quem ja esta onde precisa: nenhum degrau a subir. Uma instancia so (isto roda por tique).</summary>
	private static readonly FormaDef[] SemDegraus = [];

	/// <summary>
	/// O PLANO DESTA RAIVA, ou nulo quando ela nao desperta forma nenhuma (calma, nada novo pago, ou tudo o que ela
	/// abriria vale menos que a forma em que o corpo esta).
	/// </summary>
	public static PlanoDeSubida? Planejar(EstadoDeForma est, double bpBase, PerfilDeFormas perfil)
	{
		// QUEM ESTA CALMO SAI AQUI, e e quase todo mundo, quase sempre: isto roda no tique da forma de todo corpo.
		if (perfil.Raiva == NivelDeRaiva.Nenhuma) return null;

		PlanoDeSubida? melhor = null;
		double melhorMult = est.Multiplicador(perfil);
		foreach (FormaDef d in Catalogo.Todas)
		{
			if (!Catalogo.NasceDaRaiva(d) || est.Despertou(d.Id)) continue;
			if (DegrausAte(est, d, bpBase, perfil) is not { } degraus) continue;

			double m = Catalogo.Multiplicador(d.Id, est.Maestria, perfil, est.CombateSegundos);
			if (m <= melhorMult && !(PraBaixoDeTeste && melhor == null)) continue;
			melhorMult = m;
			melhor = new PlanoDeSubida(d, degraus);
		}
		return melhor;
	}

	/// <summary>
	/// OS DEGRAUS QUE ESTE CORPO SOBE PRA CHEGAR A <paramref name="d"/> -- vazio quando ela se alcanca daqui, nulo
	/// quando nao se alcanca (falta outra coisa que nao degrau, ou falta um degrau que ele nunca estreou).
	/// </summary>
	private static FormaDef[]? DegrausAte(EstadoDeForma est, FormaDef d, double bpBase, PerfilDeFormas perfil)
	{
		RecusaForma daqui = est.Avaliar(d.Id, bpBase, kiFracao: 1, caido: false, perfil);
		if (daqui == RecusaForma.Pode) return SemDegraus;

		// SO O "FORA DE ORDEM" SE RESOLVE SUBINDO. Qualquer outra recusa (poder, linha, classe, maestria, furia
		// pequena) continua recusa em qualquer degrau -- ou e de um degrau que nao e este.
		if (daqui != RecusaForma.ForaDeOrdem || NaoSobeDegrausDeTeste) return null;

		// DESCE A CORRENTE ate um degrau que se alcance de onde o corpo esta.
		List<FormaDef>? corrente = null;
		for (FormaDef? a = Catalogo.Anterior(d); ; a = Catalogo.Anterior(a))
		{
			// chegou no chao sem achar por onde subir, ou falta um degrau que ele nunca vestiu
			if (a == null || a.Id == Catalogo.IdBase || !est.JaViuAEstreia(a.Id)) return null;

			(corrente ??= []).Insert(0, a);
			RecusaForma r = est.Avaliar(a.Id, bpBase, kiFracao: 1, caido: false, perfil);
			if (r == RecusaForma.Pode) break;
			if (r != RecusaForma.ForaDeOrdem) return null;
		}

		// E SOBE DE VOLTA, perguntando cada passo ao funil a partir do degrau em que o corpo ESTARA. O primeiro da
		// corrente ja foi perguntado daqui (e o `break` de cima).
		for (int i = 1; i < corrente.Count; i++)
			if (est.AvaliarEstandoEm(corrente[i - 1].Id, corrente[i].Id, bpBase, 1, false, perfil) != RecusaForma.Pode)
				return null;
		if (est.AvaliarEstandoEm(corrente[^1].Id, d.Id, bpBase, 1, false, perfil) != RecusaForma.Pode) return null;

		return [.. corrente];
	}
}
