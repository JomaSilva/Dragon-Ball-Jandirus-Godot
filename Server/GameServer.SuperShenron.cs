using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// UMA PROCURACAO EM ABERTO -- a oferta de guarda das sete, esperando o "aceito".
///
/// O DM resolve isso num `alert(R, ...)` que PRENDE o procurador (`ProceduralSpace.dm:1667`). Num
/// servidor autoritativo nao ha caixa bloqueante, entao a oferta vira estado com prazo -- o mesmo
/// desenho (e pelo mesmo argumento) do convite de Anciao e da oferta de juventude do lote G8. Uma
/// oferta sem prazo vira uma promessa que ninguem lembra de ter feito.
/// </summary>
public sealed class OfertaDeGuarda
{
	/// <summary>Conta de quem oferece -- o DOADOR, que vai virar o beneficiario.</summary>
	public string DeConta = "";
	public string DeNome = "";

	/// <summary>Assinatura do doador. E ela que vira `_benefSig`: o desejo e do PERSONAGEM.</summary>
	public string DeAssinatura = "";

	/// <summary>Relogio real (ms) em que a oferta vence.</summary>
	public long Ate;
}

/// <summary>
/// **O SUPER SHENRON: A LINGUA, O PROCURADOR E O DESEJO.** Porte de
/// `Code/Modules/Tech/ProceduralSpace.dm:1578-1700` + `Code/Modules/Magic/WishTable.dm:22-43`.
///
/// ============================ A PERGUNTA DIFICIL, RESPONDIDA: **QUEM RECEBE O DESEJO?** ============================
/// **O PEDINTE.** Nunca o falante.
///
/// A fonte responde sozinha e sem ambiguidade -- `ProceduralSpace.dm:1605` resolve
/// `var/mob/alvo = benef ? benef : U` e **todo** efeito pessoal cai em `alvo`: a riqueza (`:1640`), o
/// supremo (`sw_strongest_wish(alvo)`, `:1637`) e ate o alerta que cobra a vida (`alert(alvo, ...)`,
/// `:1632`). O titulo do dialogo do proprio DM diz *"SUPER SHENRON atende UM desejo -- em nome de
/// [benef.name]"*. O falante e um **PORTA-VOZ**, e o dono do jogo usou exatamente essa palavra:
/// *"as pessoas q n sabem a lingua dos deuses tem q emprestar/pedir pra outra pessoa q saiba a lingua
/// dos deuses falar com o super shenlong"*.
///
/// O DM ja traz CINCO travas contra o porta-voz infiel, e todas foram portadas:
///   1. a riqueza vai pro `alvo` e o anuncio nomeia o `alvo` (`:1640-1641`);
///   2. o supremo pede a confirmacao AO `alvo` -- quem paga com a vida consente em pessoa (`:1632`);
///   3. o "Corpo de um Saiyajin" **some do menu** enquanto houver procuracao (`:1598`): privilegio de
///      cargo nao se procura;
///   4. beneficiario offline recusa o desejo (`:1602-1604`);
///   5. esfera tomada A FORCA **quebra a procuracao** (`:1571-1573`) -- nao da pra lavar a procuracao
///      por disputa.
/// ============================================================================================================
///
/// ============================ E O FALANTE NAO PODE **TROCAR** O DESEJO. ISSO E DAQUI ============================
/// As cinco travas acima protegem QUEM RECEBE. Nenhuma delas protege **QUAL** desejo: no original o
/// menu abre pro falante (`input(U, ...)`, `:1600`) e e ELE quem escolhe entre riqueza, revive e o
/// supremo -- e no revive e ele quem escolhe o morto (`:1613`). Um porta-voz infiel nao consegue
/// roubar o desejo, mas consegue **gastar o desejo alheio no que ele quiser**.
///
/// O dono pediu o contrario, com todas as letras: *"o pedinte escolhe o desejo, o falante so
/// PRONUNCIA, e o beneficiario e o pedinte. E o falante NAO pode trocar o desejo no meio do caminho --
/// prove isso"*. Entao, **quando ha procuracao**, o pedido e REGISTRADO pelo pedinte
/// (<see cref="RegistrarPedido"/>) e o `sdb_invocar` do falante **nao aceita argumento nenhum**: ele
/// executa o que esta escrito. A prova nao e um comentario -- e a assinatura do metodo: nao ha por
/// onde o falante passar uma escolha. A bancada afirma isso mandando o falante pedir OUTRA coisa e
/// medindo o que o pedinte recebeu.
///
/// Sem procuracao (dono das sete que fala a lingua) nada muda: ele escolhe no `sdb_invocar <id>`,
/// como no DM.
/// ==========================================================================================================
///
/// ============================ E OS DOIS BURACOS QUE A FASE 0 MEDIU, FECHADOS ============================
///   **(A) O PROCURADOR INFIEL SUB-DELEGAVA.** No DM `Transferir_Super_Esferas` so exige "ter as 7" --
///   entao o procurador R, que agora tem as sete, chamava o verb de novo, passava a um comparsa R2, e
///   `sdb_benef_sig` era **sobrescrito pra R** (`:1676`). O doador original perdia as esferas E o
///   desejo, sem consentimento e sem registro. Nao ha pilha, nao ha travamento.
///   **FECHADO**: procuracao nao se repassa (ver <see cref="TransferirAGuarda"/>). E o doador ganhou a
///   outra metade que faltava -- <see cref="RevogarAGuarda"/>: quem EMPRESTA pode tomar de volta.
///
///   **(B) O REVIVE IGNORAVA O BENEFICIARIO POR COMPLETO.** O alvo do revive e escolhido pelo FALANTE
///   (`:1607-1617`) e a linha `:1602` **isenta justamente esse desejo** da exigencia de o doador estar
///   online. Um procurador com o doador offline queimava o set inteiro num revive da escolha dele.
///   **FECHADO** pelas duas pontas: o pedido registrado ja carrega o alvo, e **todo** desejo exige o
///   beneficiario online -- sem excecao pro revive.
/// ==================================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// A PROCURACAO
	// =====================================================================
	/// <summary>
	/// **QUEM E O PEDINTE** -- o `sdb_benef_sig` do DM (`ProceduralSpace.dm:57`). Vazio = ninguem
	/// emprestou nada, e as sete sao de quem as tem.
	///
	/// Por ASSINATURA (personagem) e nao por conta, como o claim e como o `Dominio`: quem emprestou as
	/// esferas foi um personagem, e e ele que recebe.
	///
	/// **SLOT UNICO, e isso nao e simplificacao**: so existem SETE Super Esferas no universo, entao so
	/// pode haver um dono das sete -- e portanto um pedinte. Dois pedintes ao mesmo tempo e
	/// estruturalmente impossivel, e a bancada afirma isso em vez de deixa-lo implicito.
	/// </summary>
	private string _benefSig = "", _benefNome = "";

	/// <summary>
	/// **O PEDIDO REGISTRADO PELO PEDINTE** -- id do desejo e o alvo, quando ele pede alvo.
	///
	/// Nao existe no DM: la o falante escolhe no menu. Ver o cabecalho -- e o pedido literal do dono, e
	/// e o que faz "o falante so PRONUNCIA" ser verdade e nao promessa.
	/// </summary>
	private string _pedidoDoBenef = "", _alvoDoPedido = "";

	/// <summary>As ofertas de guarda em aberto, por conta de QUEM RECEBE a oferta.</summary>
	private readonly Dictionary<string, OfertaDeGuarda> _ofertasDeGuarda =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// QUANTO TEMPO UMA OFERTA DE GUARDA FICA DE PE. O DM nao tem prazo (o `alert` prende), e aqui uma
	/// promessa sem prazo e uma promessa esquecida. Mesmo numero da oferta de juventude do lote G8.
	/// </summary>
	private const long PrazoDaOfertaDeGuardaMs = 60_000;

	/// <summary>`get_dist(usr, R) &gt; 1` (`:1665`) -- o gesto e PRESENCIAL, corpo a corpo.</summary>
	private const float AlcanceDaProcuracao = 1.5f * ZoneCollision.TileSize;

	/// <summary>Ha uma procuracao valendo?</summary>
	private bool HaProcuracao => _benefSig.Length > 0;

	// =====================================================================
	// A INVOCACAO -- `Invocar_Super_Shenron` (:1578-1644)
	// =====================================================================
	/// <summary>
	/// **CHAMAR O SUPER SHENRON.** A Fase 1 entregou as sete e a posse; aqui entra o pedido.
	///
	/// ============================ A ORDEM DAS GUARDAS E DO DM, E ELA IMPORTA ============================
	/// As sete PRIMEIRO (`:1584`), a lingua DEPOIS (`:1587`). Invertida, quem nao fala levaria "voce tem
	/// 3 de 7" e **nunca leria a recusa da lingua** -- que e a unica linha do jogo inteiro que ensina
	/// que o procurador existe. A Fase 1 ja tinha escrito isso no contrato do plugue; aqui ele se cumpre.
	/// ================================================================================================
	/// </summary>
	private void ChamarOSuperShenron(ServerPlayer pl, string arg)
	{
		// `if(!U.client || U.dead || U.KO) return` (:1582)
		if (pl.Ficha.dead || pl.Ficha.KO)
		{ Avisar(pl, "você não está em condições de chamar coisa nenhuma."); return; }

		// ---------------------------------------------------------- 1. as sete
		int minhas = QuantasSupersTem(pl.Assinatura);
		if (minhas < SuperEsferas.Total)
		{
			Avisar(pl, $"você sente o poder da esfera... mas reivindicou {minhas} de "
					 + $"{SuperEsferas.Total}. O dragão exige TODAS.");
			return;
		}

		// ---------------------------------------------------------- 2. A LINGUA DOS DEUSES
		// `if(!U.godtongue_check())` (:1587). A checagem REAVALIA antes de recusar -- e o que faz um
		// Kai recem-criado (ou um Kaio recem-coroado que nao relogou) nao levar uma recusa injusta.
		ConferirALingua(pl);
		if (!pl.Ficha.godtongue)
		{
			Avisar(pl, LinguaDosDeuses.FraseDaRecusa);
			// RECUSA LIMPA: nada e consumido, os claims continuam seus, as esferas nao se espalham.
			// E o `return` seco do original, e ele e a diferenca entre "tente outra coisa" e "perdeu".
			return;
		}

		// ---------------------------------------------------------- 3. quem recebe
		// `if(sdb_benef_sig && sdb_benef_sig != U.signature)` (:1592) -- procuracao so existe quando o
		// pedinte NAO e quem esta falando.
		bool procurando = HaProcuracao
					   && !string.Equals(_benefSig, pl.Assinatura, StringComparison.Ordinal);

		// `for(var/mob/P in player_list) if(P.client && P.signature == sdb_benef_sig)` (:1593-1596).
		// `Pessoas` e o recorte deste port pra "quem esta no mundo com identidade propria" -- ver o
		// cabecalho dele em `GameServer.Desejos.cs`. Nulo aqui = o pedinte nao esta presente.
		ServerPlayer? benef = procurando
			? Pessoas.FirstOrDefault(p => string.Equals(p.Assinatura, _benefSig, StringComparison.Ordinal))
			: null;

		ServerPlayer alvo = benef ?? pl;

		// ============================ O BENEFICIARIO **PRECISA** ESTAR ONLINE ============================
		// `:1602-1604` faz isso pra todos os desejos **menos** o revive -- e essa excecao e o buraco (B)
		// do cabecalho: com o doador offline, o procurador queimava as sete num revive da escolha dele.
		// Aqui nao ha excecao: um desejo que o pedinte nao ve acontecer nao e o desejo dele.
		// ============================================================================================
		if (procurando && benef == null)
		{
			Avisar(pl, $"o pedinte ({_benefNome}) precisa estar ONLINE para receber o desejo. "
					 + "Você carrega a guarda das sete -- espere por ele.");
			return;
		}

		// ---------------------------------------------------------- 4. QUAL desejo
		// ============================ AQUI O FALANTE PERDE A ESCOLHA ============================
		// Com procuracao, o `arg` **e ignorado** e o desejo sai do que o pedinte registrou. Nao ha
		// caminho por onde o falante passe uma escolha -- e e isso que "ele nao pode trocar o desejo"
		// quer dizer. Sem procuracao, o dono das sete escolhe normalmente.
		// ==================================================================================
		string id = procurando ? _pedidoDoBenef : arg.Trim().Split(' ', 2)[0].Trim();
		string alvoDoDesejo = procurando
			? _alvoDoPedido
			: arg.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries) is { Length: > 1 } p2
				? p2[1].Trim() : "";

		if (procurando && id.Length == 0)
		{
			Avisar(pl, $"{_benefNome} ainda não disse o que pedir. Só ele escolhe -- peça que ele use "
					 + "o verbo 'sdb_pedido'. Você apenas pronuncia.");
			if (benef != null)
				Avisar(benef, $"{pl.Name} está diante das esferas e espera o SEU pedido "
							+ "(verbo sdb_pedido).");
			return;
		}

		List<DesejoDef> menu = [.. Desejos.MenuDoSuper(EhKaioshinDoCorpo(pl), procurando)];

		if (id.Length == 0)
		{
			Avisar(pl, "-- SUPER SHENRON atende UM desejo --");
			foreach (DesejoDef d in menu) Avisar(pl, $"  [{d.Id}] {d.Nome}  ({d.Desc})");
			Avisar(pl, "peça com: sdb_invocar <id> [alvo]");
			return;
		}

		DesejoDef? escolha = menu.Find(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
		if (escolha == null)
		{
			Avisar(pl, procurando
				? $"o pedido registrado ('{id}') não está entre os que o Super Shenron atende agora. "
				  + $"{_benefNome} precisa registrar outro."
				: "o Super Shenron não conhece esse desejo. Use sdb_invocar sem argumento pra ver a lista.");
			return;
		}

		// ---------------------------------------------------------- 5. o desejo
		if (!ConcederDoSuper(pl, alvo, escolha, alvoDoDesejo)) return;

		// `pspace_sdb_scatter()` (:1642) -- consome os claims E a procuracao, e re-espalha.
		ConsumirAsSupers();
		AnunciarSupers("O desejo foi realizado... e as Super Esferas, drenadas, se espalharam "
					 + "novamente pela galáxia.");
	}

	/// <summary>
	/// OS QUATRO DESEJOS DO SUPER SHENRON. Devolve `true` quando foi CONCEDIDO (e so entao as sete se
	/// consomem).
	///
	/// `alvo` E O PEDINTE, sempre -- e o `var/mob/alvo = benef ? benef : U` do DM (:1605). Nenhum ramo
	/// daqui usa `pl` pra receber coisa nenhuma; `pl` so serve pra falar e pra ser o lugar de onde o
	/// ressuscitado aparece.
	/// </summary>
	private bool ConcederDoSuper(ServerPlayer pl, ServerPlayer alvo, DesejoDef d, string nome)
	{
		switch (d.Id)
		{
			// -------------------------------------------------- riqueza (:1638-1641)
			case "sdb_riqueza":
				AnunciarInvocacaoDoSuper(pl);
				alvo.Ficha.Zeni += Desejos.ZeniDoSuperDesejo;
				Persistir(alvo);
				AnunciarSupers($"SUPER SHENRON concedeu riqueza colossal a {alvo.Name}!");
				if (alvo != pl) Avisar(pl, $"a riqueza foi para {alvo.Name} -- você apenas pronunciou.");
				return true;

			// -------------------------------------------------- reviver (:1606-1618)
			case "sdb_reviver":
			{
				// `if(M2.client && M2.dead && !M2.aged_out)` (:1609) -- e o comentario do DM na propria
				// linha e literal: *"morte de VELHICE nem o Super Shenron reverte"*.
				//
				// E COMO O SHENRON COMUM, ELE **IGNORA O `ResurrectedCount`**: o DM chama `Revive(T, 1)`
				// direto (:1616), sem tocar no contador e sem cobrar vida de ninguem. A esfera e a excecao
				// que o preco do cargo nao alcanca -- ver o cabecalho do `DesejoDeReviver`.
				//
				// **QUEM ESCOLHE O MORTO E O PEDINTE**, e nao o falante: o nome vem do pedido registrado.
				// E o fechamento do buraco (B).
				List<ServerPlayer> mortos = [.. Pessoas.Where(o => o.Ficha.dead && !o.Ficha.aged_out)];
				if (mortos.Count == 0)
				{ Avisar(pl, "nenhum guerreiro caído que possa voltar."); return false; }

				if (nome.Length == 0)
				{
					Avisar(pl, "quem? " + string.Join(", ", mortos.Select(o => o.Name))
							 + " -- e quem morreu de VELHICE não volta.");
					return false;
				}

				ServerPlayer? t = mortos.Find(o => string.Equals(o.Name, nome, StringComparison.OrdinalIgnoreCase));
				if (t == null) { Avisar(pl, $"'{nome}' não está entre os que podem voltar."); return false; }

				AnunciarInvocacaoDoSuper(pl);
				// `T.loc = locate(U.x + 1, U.y, U.z)` (:1617) -- ao lado de QUEM FALOU, e nao do pedinte:
				// e o dragao que esta ali, e a alma volta onde o dragao esta.
				Ressuscitar(t, pl.Zone, new Vec2(pl.Pos.X + ZoneCollision.TileSize, pl.Pos.Y));
				AnunciarSupers($"SUPER SHENRON trouxe {t.Name} de volta à vida!");
				return true;
			}

			// -------------------------------------------------- o supremo (:1631-1637)
			case "sdb_supremo":
			{
				// ============================ QUEM PAGA CONSENTE EM PESSOA -- SEMPRE ============================
				// `alert(alvo, "...", "ACEITO O PRECO", "Recusar")` (:1632) -- o alerta e mostrado ao
				// **ALVO**, e nao ao falante. E a trava mais importante das cinco: o preco deste desejo e
				// a VIDA, e um porta-voz nao assina a sentenca de quem confiou nele.
				//
				// **E ELE APARECE MESMO SEM PROCURACAO**, porque no DM `alvo` e `U` quando nao ha
				// beneficiario -- ou seja, o dono das sete tambem e perguntado. A primeira versao disto
				// pulava a confirmacao nesse caso ("ele mesmo pediu, ja consentiu"), e estaria errada por
				// dois motivos: o original pergunta, e um verbo digitado por engano nao pode custar um
				// personagem inteiro. **Este e o unico desejo do jogo que mata quem o pede.**
				//
				// Sem modal, a confirmacao vira um segundo verbo (`sdb_aceito`) com o mesmo efeito, e a
				// palavra literal do original ("ACEITO O PREÇO") vira o argumento -- uma frase dificil de
				// digitar por acidente e parte do desenho. Ver `AceitarOPreco`.
				// ============================================================================
				_precoPendente = (alvo.Conta, pl.Id, NowMs() + PrazoDaOfertaDeGuardaMs);
				Avisar(alvo, (alvo == pl ? "você está" : $"{pl.Name} está")
						   + " prestes a pedir" + (alvo == pl ? "" : " em seu nome")
						   + ": trocar TODO o seu tempo de vida por poder absoluto. Você viverá SÓ MAIS "
						   + "UM ANO -- com o DOBRO do maior poder do jogo -- e dessa morte não há "
						   + "revive, apenas reencarnação. Responda com: sdb_aceito ACEITO O PREÇO");
				if (alvo != pl)
					Avisar(pl, $"{alvo.Name} precisa aceitar o preço em pessoa. Nada foi consumido.");
				return false;
			}

			// -------------------------------------------------- o corpo (:1619-1630)
			case "sdb_corpo_saiyajin":
				// O menu ja o removeu se houver procuracao (`Desejos.MenuDoSuper`), entao quando ele chega
				// aqui o falante E o pedinte. Nao ha alvo a proteger: o molde continua inteiro.
				AnunciarInvocacaoDoSuper(pl);
				return DesejoDeCorpoSaiyajin(pl, null, nome);

			default: return false;
		}
	}

	/// <summary>
	/// O PRECO DO SUPREMO ESPERANDO A PALAVRA: (conta de quem paga, id de quem fala, vencimento).
	/// Um so, porque so ha um conjunto de sete no universo.
	/// </summary>
	private (string Conta, int Falante, long Ate) _precoPendente;

	/// <summary>
	/// **`sdb_aceito ACEITO O PRECO`** -- a resposta do `alert(alvo, ...)` do DM (`:1632-1635`).
	///
	/// A FRASE INTEIRA E O BOTAO. No original a opcao literalmente se chama "ACEITO O PRECO", e nao
	/// "Sim": e um desejo que mata, e uma palavra dificil de digitar por acidente e parte do desenho.
	/// Digitar qualquer outra coisa e recusar.
	/// </summary>
	private void AceitarOPreco(ServerPlayer pl, string arg)
	{
		if (!string.Equals(_precoPendente.Conta, pl.Conta, StringComparison.OrdinalIgnoreCase)
			|| NowMs() > _precoPendente.Ate)
		{
			_precoPendente = default;
			Avisar(pl, "ninguém está pedindo nada em seu nome (ou o prazo venceu).");
			return;
		}

		ServerPlayer? falante = _players.GetValueOrDefault(_precoPendente.Falante);
		_precoPendente = default;

		if (!string.Equals(arg.Trim(), "ACEITO O PREÇO", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(arg.Trim(), "ACEITO O PRECO", StringComparison.OrdinalIgnoreCase))
		{
			Avisar(pl, "o desejo foi recusado. Nada aconteceu.");
			if (falante != null) Avisar(falante, $"{pl.Name} recusou o preço.");
			return;
		}

		if (falante == null || QuantasSupersTem(falante.Assinatura) < SuperEsferas.Total)
		{ Avisar(pl, "quem carregava as sete não está mais diante delas."); return; }

		AnunciarInvocacaoDoSuper(falante);
		DesejoSupremo(falante, null, pl);
		ConsumirAsSupers();
		AnunciarSupers("O desejo foi realizado... e as Super Esferas, drenadas, se espalharam "
					 + "novamente pela galáxia.");
	}

	/// <summary>
	/// `sdb_announce_summon` (`:1646-1648`) -- *"o ceu de TODA a galaxia escurece"*.
	///
	/// O ceu de verdade escurece so onde ha alguem pra ver (o clima e por zona neste port, e forcar
	/// tempestade em toda zona carregada seria caro e invisivel); o ANUNCIO e mundial, como no DM.
	/// O `thunderclap.wav` fica de fora: o canal de som deste port toca por zona e por evento, e um som
	/// global sem consumidor seria mais um campo escrito pra ninguem.
	/// </summary>
	private void AnunciarInvocacaoDoSuper(ServerPlayer quem)
	{
		AnunciarSupers($"O céu de TODA a galáxia escurece... {quem.Name} invocou SUPER SHENRON na "
					 + "língua dos deuses!");
		ForcarClima(quem.Zone, TipoDeClima.Tempestade, 30);
	}

	// =====================================================================
	// O PROCURADOR -- `Transferir_Super_Esferas` (:1651-1680)
	// =====================================================================
	/// <summary>
	/// **EMPRESTAR AS SETE A QUEM FALA** -- o verbo que o dono descreveu: *"tem q emprestar/pedir pra
	/// outra pessoa q saiba a lingua dos deuses falar com o super shenlong"*.
	///
	/// AS TRES EXIGENCIAS DO DM, TODAS AQUI: ter as SETE (`:1655`), o escolhido estar ADJACENTE
	/// (`oview(1)`, `:1659`) e ele ACEITAR (`:1667`). O gesto e presencial e de dois lados -- ninguem
	/// recebe a guarda sem querer, e nao ha menu de servidor inteiro.
	///
	/// ============================ **PROCURACAO NAO SE REPASSA** -- o buraco (A), fechado ============================
	/// No DM o verb so pergunta "voce tem as 7?", e o procurador tem. Entao ele repassava a um comparsa
	/// e `sdb_benef_sig` era sobrescrito **pra ele mesmo** (`:1676`): o doador original perdia as sete e
	/// o desejo, sem consentimento e sem rastro. Nao ha pilha de procuracao, nao ha travamento.
	///
	/// A recusa abaixo e o fecho: **quem carrega procuracao alheia nao transfere**. E a definicao de
	/// procuracao -- um porta-voz fala pelo outorgante, e nao outorga.
	/// ==========================================================================================================
	/// </summary>
	private void TransferirAGuarda(ServerPlayer pl, string nome)
	{
		if (pl.Ficha.dead) { Avisar(pl, "um morto não confia esferas a ninguém."); return; }

		if (QuantasSupersTem(pl.Assinatura) < SuperEsferas.Total)
		{
			Avisar(pl, $"você precisa das {SuperEsferas.Total} Super Esferas reivindicadas para "
					 + $"transferir a guarda. ({QuantasSupersTem(pl.Assinatura)}/{SuperEsferas.Total})");
			return;
		}

		// O FECHO DO BURACO (A). Ver o cabecalho.
		if (HaProcuracao && !string.Equals(_benefSig, pl.Assinatura, StringComparison.Ordinal))
		{
			Avisar(pl, $"estas esferas são uma PROCURAÇÃO de {_benefNome} -- procuração não se repassa. "
					 + "Você pode pronunciar o pedido dele, e nada mais.");
			return;
		}

		List<ServerPlayer> cands = [.. Pessoas.Where(o => o != pl && !o.Ficha.dead
			&& o.Zone.Hash == pl.Zone.Hash
			&& Math.Abs(o.Pos.X - pl.Pos.X) <= AlcanceDaProcuracao
			&& Math.Abs(o.Pos.Y - pl.Pos.Y) <= AlcanceDaProcuracao)];

		if (cands.Count == 0)
		{ Avisar(pl, "chame o guardião escolhido para o seu lado (adjacente)."); return; }

		if (nome.Length == 0)
		{ Avisar(pl, "a quem? " + string.Join(", ", cands.Select(o => o.Name))); return; }

		ServerPlayer? r = cands.Find(o => string.Equals(o.Name, nome, StringComparison.OrdinalIgnoreCase));
		if (r == null) { Avisar(pl, $"'{nome}' não está ao seu lado."); return; }

		// `if(!R.godtongue_check(1))` (:1666) -- em modo QUIETO: so avisa o doador, sem ensinar a lingua
		// a ninguem por engano. E um AVISO e nao uma recusa: o DM deixa transferir mesmo assim, e faz
		// sentido -- o escolhido pode estar a um cargo de distancia de aprender.
		ConferirALingua(r);
		if (!r.Ficha.godtongue)
			Avisar(pl, $"AVISO: {r.Name} também NÃO fala a língua dos deuses -- ele(a) não conseguirá "
					 + "invocar o dragão.");

		_ofertasDeGuarda[r.Conta] = new OfertaDeGuarda
		{
			DeConta = pl.Conta,
			DeNome = pl.Name,
			DeAssinatura = pl.Assinatura,
			Ate = NowMs() + PrazoDaOfertaDeGuardaMs,
		};

		Avisar(pl, $"você oferece a guarda das sete a {r.Name}. Agora é com {r.Name}.");
		Avisar(r, $"{pl.Name} quer confiar as {SuperEsferas.Total} SUPER ESFERAS a você, para invocar o "
				+ $"dragão EM NOME DELE(A) -- o pedido é de {pl.Name}, e você apenas pronuncia. "
				+ $"(sdb_guarda_aceitar / sdb_guarda_recusar, em {PrazoDaOfertaDeGuardaMs / 1000}s)");
	}

	/// <summary>
	/// A RESPOSTA DO ESCOLHIDO -- o `switch(alert(R, ...))` do DM (`:1667-1670`), do lado de quem recebe.
	///
	/// A RE-CHECAGEM DAS SETE DEPOIS DO DIALOGO E LITERAL (`:1671`): o doador pode ter perdido uma
	/// esfera numa disputa enquanto o outro pensava, e transferir seis seria transferir um set quebrado.
	/// </summary>
	private void ResponderAGuarda(ServerPlayer pl, bool aceitou)
	{
		if (!_ofertasDeGuarda.TryGetValue(pl.Conta, out OfertaDeGuarda? o) || NowMs() > o.Ate)
		{
			_ofertasDeGuarda.Remove(pl.Conta);
			Avisar(pl, "ninguém te ofereceu guarda nenhuma (ou a oferta já venceu).");
			return;
		}
		_ofertasDeGuarda.Remove(pl.Conta);

		ServerPlayer? doador = OnlinePorConta(o.DeConta);

		if (!aceitou)
		{
			Avisar(pl, "você recusa a guarda das esferas.");
			if (doador != null) Avisar(doador, $"{pl.Name} recusou a guarda das esferas.");
			return;
		}

		if (QuantasSupersTem(o.DeAssinatura) < SuperEsferas.Total)
		{
			Avisar(pl, $"{o.DeNome} não tem mais as sete. A guarda não muda de mãos.");
			if (doador != null) Avisar(doador, "você não tem mais as sete -- a transferência caiu.");
			return;
		}

		// AS SETE PASSAM (`:1672-1675`), e o BENEFICIARIO E O DOADOR (`:1676-1677`).
		foreach (SuperEsfera s in _supers)
		{
			if (!string.Equals(s.Dono, o.DeAssinatura, StringComparison.Ordinal)) continue;
			s.Dono = pl.Assinatura;
			s.DonoNome = pl.Name;
		}

		_benefSig = o.DeAssinatura;
		_benefNome = o.DeNome;
		_pedidoDoBenef = "";
		_alvoDoPedido = "";
		SalvarSupers();

		AnunciarSupers($"{o.DeNome} confiou as {SuperEsferas.Total} Super Esferas a {pl.Name}!");
		Avisar(pl, $"você carrega a guarda das sete. Invoque o SUPER SHENRON perto de qualquer uma -- "
				 + $"e o desejo será de {o.DeNome}, que é quem o escolhe.");
		if (doador != null)
		{
			Avisar(doador, $"{pl.Name} aceitou a guarda. Agora DIGA o seu pedido (verbo sdb_pedido) -- "
						 + "só você escolhe, e ele não pode trocá-lo.");
			MandarSupers(doador);
		}
		MandarSupers(pl);
		GD.Print($"[super] {o.DeNome} transferiu as sete a {pl.Name} (beneficiario: {o.DeNome})");
	}

	/// <summary>
	/// **TOMAR A GUARDA DE VOLTA.** Nao existe no DM -- e a outra metade de "EMPRESTAR", que e a palavra
	/// que o dono usou.
	///
	/// ============================ POR QUE ELA PRECISOU EXISTIR ============================
	/// Sem ela, entregar as sete e IRREVERSIVEL: um procurador que simplesmente **nao invoca** (por
	/// birra, por esquecimento, porque morreu e ficou no Outro Mundo, porque deslogou pra sempre) prende
	/// o unico conjunto de Super Esferas do universo pra sempre. O DM so tinha uma saida pra isso, e ela
	/// e a disputa de 5 minutos -- ou seja, o doador teria que **tomar a forca** o que ele emprestou,
	/// sete vezes, e ainda perderia a procuracao no caminho (`:1571-1573`).
	///
	/// So o PEDINTE revoga, e a qualquer momento. E o remedio das bordas que a tarefa pediu pra pensar:
	/// o falante recusa, o falante morre, o falante e inimigo.
	/// ================================================================================
	/// </summary>
	private void RevogarAGuarda(ServerPlayer pl)
	{
		if (!HaProcuracao || !string.Equals(_benefSig, pl.Assinatura, StringComparison.Ordinal))
		{ Avisar(pl, "você não emprestou esferas a ninguém."); return; }

		string quem = _supers.FirstOrDefault(s => s.Dono.Length > 0)?.DonoNome ?? "ninguém";

		foreach (SuperEsfera s in _supers)
		{
			s.Dono = pl.Assinatura;
			s.DonoNome = pl.Name;
		}
		_benefSig = "";
		_benefNome = "";
		_pedidoDoBenef = "";
		_alvoDoPedido = "";
		SalvarSupers();

		AnunciarSupers($"{pl.Name} retomou a guarda das {SuperEsferas.Total} Super Esferas.");
		foreach (ServerPlayer p in Pessoas) MandarSupers(p);
		GD.Print($"[super] {pl.Name} revogou a procuracao (estava com {quem})");
	}

	/// <summary>
	/// **O PEDIDO DO PEDINTE** -- `sdb_pedido &lt;id&gt; [alvo]`.
	///
	/// Nao existe no DM (la o falante escolhe no menu), e existe aqui porque o dono pediu que *"o
	/// pedinte escolhe o desejo, o falante so PRONUNCIA"*. **So o beneficiario escreve**: qualquer outro
	/// leva recusa, inclusive quem carrega as sete.
	///
	/// ELE PODE SER REESCRITO ate a invocacao acontecer -- e de proposito: quem empresta pode mudar de
	/// ideia enquanto o dragao nao subiu. Quem NAO pode mudar e o falante, e e o `sdb_invocar` dele que
	/// nao tem por onde receber uma escolha.
	/// </summary>
	private void RegistrarPedido(ServerPlayer pl, string arg)
	{
		if (!HaProcuracao || !string.Equals(_benefSig, pl.Assinatura, StringComparison.Ordinal))
		{
			Avisar(pl, "você não emprestou as sete a ninguém -- não há pedido a registrar. "
					 + "(Quem tem as sete e fala a língua pede direto, com sdb_invocar.)");
			return;
		}

		List<DesejoDef> menu = [.. Desejos.MenuDoSuper(ehKaioshin: false, haProcuracao: true)];

		string[] p = arg.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
		if (p.Length == 0)
		{
			Avisar(pl, "-- o que você quer pedir ao SUPER SHENRON? --");
			foreach (DesejoDef d in menu) Avisar(pl, $"  [{d.Id}] {d.Nome}  ({d.Desc})");
			Avisar(pl, "registre com: sdb_pedido <id> [alvo]"
					 + (_pedidoDoBenef.Length > 0 ? $"  (agora: {_pedidoDoBenef} {_alvoDoPedido})" : ""));
			return;
		}

		DesejoDef? d2 = menu.Find(x => string.Equals(x.Id, p[0], StringComparison.OrdinalIgnoreCase));
		if (d2 == null) { Avisar(pl, "o Super Shenron não atende esse pedido."); return; }

		_pedidoDoBenef = d2.Id;
		_alvoDoPedido = p.Length > 1 ? p[1].Trim() : "";
		SalvarSupers();

		Avisar(pl, $"pedido registrado: {d2.Nome}"
				 + (_alvoDoPedido.Length > 0 ? $" ({_alvoDoPedido})" : "")
				 + ". Quem carrega as sete só pode pronunciá-lo -- não trocá-lo.");

		ServerPlayer? falante = Pessoas.FirstOrDefault(
			o => QuantasSupersTem(o.Assinatura) >= SuperEsferas.Total);
		if (falante != null)
			Avisar(falante, $"{pl.Name} registrou o pedido dele(a): {d2.Nome}"
						  + (_alvoDoPedido.Length > 0 ? $" ({_alvoDoPedido})" : "")
						  + ". Você pode pronunciá-lo com sdb_invocar.");
	}

	/// <summary>
	/// A PROCURACAO CAI QUANDO AS ESFERAS SAO TOMADAS A FORCA -- `:1571-1573`.
	///
	/// *"um claim tomado A FORCA quebra a transferencia pendente"*: e a quinta trava do original, e ela
	/// existe pra que ninguem LAVE uma procuracao por disputa. Sem ela, o procurador infiel combinava
	/// com um comparsa, deixava-o "tomar" uma esfera, e a procuracao... continuaria valendo pro
	/// beneficiario errado.
	///
	/// Chamada de dentro do `FecharADisputa` (Fase 1), que e o funil das duas vitorias.
	/// </summary>
	private void QuebrarAProcuracao(string motivo)
	{
		if (!HaProcuracao) return;

		string quem = _benefNome;
		_benefSig = "";
		_benefNome = "";
		_pedidoDoBenef = "";
		_alvoDoPedido = "";
		SalvarSupers();

		AnunciarSupers($"A procuração de {quem} sobre as Super Esferas se desfez: {motivo}.");
		GD.Print($"[super] procuracao de {quem} quebrada -- {motivo}");
	}

	// =====================================================================
	// VERBOS
	// =====================================================================
	/// <summary>
	/// OS VERBOS DO SUPER SHENRON. Prefixo `sdb_`, como o resto das Super Esferas.
	///
	/// Chamado de dentro do `ComandoDeSuperEsferas` (Fase 1) pra que o roteador continue tendo uma
	/// entrada so -- dois `switch` do mesmo prefixo em arquivos diferentes seriam duas listas que
	/// divergem no dia em que alguem renomeasse um verbo.
	/// </summary>
	private bool ComandoDoSuperShenron(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "sdb_transferir": TransferirAGuarda(pl, arg.Trim()); return true;
			case "sdb_guarda_aceitar": ResponderAGuarda(pl, aceitou: true); return true;
			case "sdb_guarda_recusar": ResponderAGuarda(pl, aceitou: false); return true;
			case "sdb_revogar": RevogarAGuarda(pl); return true;
			case "sdb_pedido": RegistrarPedido(pl, arg); return true;
			case "sdb_aceito": AceitarOPreco(pl, arg); return true;
			default: return false;
		}
	}
}
