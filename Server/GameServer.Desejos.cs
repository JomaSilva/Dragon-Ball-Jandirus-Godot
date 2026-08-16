using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// **A TABELA DE DESEJOS EM JOGO** -- o que o Shenron (e o Porunga) fazem. Porte de
/// `Code/Modules/Magic/WishTable.dm`.
///
/// ============================ QUANTOS COUBERAM, E QUANTOS NAO ============================
/// Vinte entradas na tabela (`Core/Magic/Desejos.cs`), das quais uma e "Cancelar" e nao e desejo.
/// **Quinze desejos portados, quatro nao portados** -- e os quatro APARECEM na lista, dizem por que
/// nao vieram e **nao consomem pedido**. E o caminho que o port ja usa pras tecnicas
/// (<see cref="Jandirus.Core.Skills.Modo.NaoPortada"/>): o jogador le "ainda nao foi portado" em vez
/// de apertar um botao que mente.
///
/// OS QUATRO, com o motivo curto (o longo esta em cada `DesejoDef.Falta`):
///   * **Calcinha** -- a arte nao foi importada, e o item existe pra ser vestido;
///   * **Dar uma alma** -- `HasSoul` so e lido pelos rituais dimensionais, e magia nao foi portada;
///   * **Ganhar magia** -- morto no PROPRIO DM (o menu diz "Gain Magic", o `switch` so conhece "Give
///     Magic": consumia o pedido e nao fazia nada) e nao ha magia aqui;
///   * **Imortalidade** -- nao ha flag de imortal, e ela teria que ser lida pela morte, pelo
///     envelhecimento e pelo sol.
///
/// A vigesima-primeira entrada do arquivo do DM, "Gender Change" (:383-408), **nao esta na tabela**:
/// o codigo dela existe la e a string nunca entra no menu. E codigo morto no original.
/// ======================================================================================
///
/// ============================ OS CINCO DEFEITOS DO ORIGINAL, DECIDIDOS UM A UM ============================
///   1. **"Kill Somebody" NUNCA MATA** (:310-326): os dois ramos do `if` imprimem a mesma frase e
///      nenhum encosta no alvo. Alem disso a comparacao esta INVERTIDA em relacao ao proprio texto do
///      prompt (*"If their power exceeds the creators power, it won't work!"*), e o `TrueWishPower` que
///      chega la e o `WishPower` CRU (a chamada de :170 passa o bruto). **Decisao: portar a INTENCAO** --
///      mata quem for mais FRACO que o poder do set, falha contra quem for mais forte. Deixar o botao
///      la sem matar seria um botao que mente, e tirar seria esconder um desejo que o dono pediu.
///   2. **"Gain Magic" x "Give Magic"** (:137 vs :473) -- nomes diferentes, desejo morto. Ver acima.
///   3. **O menu DUPLICA** a cada pedido da mesma invocacao (`WishList` declarada fora do `while`,
///      :118). Aqui o menu e funcao pura e nasce novo.
///   4. **`WishCount += GenerateWishList(usr)`** soma `null` (`Dragonballs.dm:229`). Aqui quem conta e
///      o `ContarUmDesejo`, chamado de um lugar so.
///   5. **O `WishPower` que cresce e some**: `WishPower *= max(nl[1],1)` (:172) escreve na ESFERA, e a
///      invocacao seguinte re-copia o valor DA ESTATUA (`Dragonballs.dm:196`). Intencao escrita,
///      resultado zero -- em ambos os sentidos, porque o `TrueWishPower` ja foi calculado antes do
///      laco. Aqui o set **e** a estatua, entao o crescimento fica. E a unica leitura em que "desperdicar
///      o pedido deixa as esferas mais fortes" quer dizer alguma coisa.
/// ======================================================================================================
///
/// ============================ E A LINGUA DOS DEUSES **NAO** ENTRA AQUI ============================
/// Grep da Fase 0, reconferido: `godtongue` nao aparece uma unica vez em `Dragonballs.dm`. Ela e
/// portao EXCLUSIVO do Super Shenron (`ProceduralSpace.dm:1587`). Poe-la neste caminho seria inventar
/// uma regra que o original nao tem -- e fecharia o Shenron pra 22 das 24 racas.
/// ==========================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O que os desejos disseram -- ligada so pela bancada, como a <see cref="EscutaDasEsferas"/>.</summary>
	internal static List<string>? EscutaDosDesejos;

	/// <summary>
	/// QUEM UM DESEJO PODE ALCANCAR -- **`EhPessoa`, e nao `EhJogador`**.
	///
	/// ============================ A DIFERENCA E MEDIDA, E ELA E A CERTA ============================
	/// `EhJogador` exige **dono na tela** (`Peer != null`), e serve pras contas do mundo (lotacao de
	/// planeta, marcos de saga) -- contar um corpo sem dono ali seria contar duas vezes. Aqui a
	/// pergunta e outra: *"isto e uma pessoa, com identidade propria?"*. `EhPessoa` responde a essa, e
	/// e a mesma que o `Restore_Youth` do lote G8 ja faz sobre o alvo dele, pelo mesmo motivo.
	///
	/// O DM pergunta `M.client` (dono na tela) em quatro desejos e **`for(var/mob/M)` cru** nos dois de
	/// revive -- ou seja, la o revive alcanca ate NPC. `EhPessoa` fica no meio: alcanca quem tem
	/// identidade e nao alcanca corpo do mundo. Ressuscitar o chefe de uma saga com um desejo seria
	/// desfazer a saga.
	///
	/// E ELA E O QUE FAZ A BANCADA MEDIR O CAMINHO DE VERDADE: um corpo forjado tem conta e assinatura
	/// e **nao** tem `Peer`. Com `EhJogador`, toda prova de alvo desta fase teria que forjar um
	/// segundo caminho -- e mediria o caminho forjado.
	/// ==========================================================================================
	/// </summary>
	private IEnumerable<ServerPlayer> Pessoas => _players.Values.Where(EhPessoa);

	// =====================================================================
	// O MENU
	// =====================================================================
	/// <summary>
	/// **O PONTO DE PLUGUE DA FASE 1, AGORA LIGADO.** Chamado pelo `InvocarODragao` assim que o dragao
	/// sobe -- e a lista que ele oferece.
	///
	/// O CONTRATO DA FASE 1 FOI CUMPRIDO SEM REABRIR NADA: a entrada continua sendo `(pl, s)`, o gate
	/// de poder sai de `s`, e quem concede chama <see cref="ContarUmDesejo"/>. A unica linha do arquivo
	/// das esferas que mudou foi o corpo deste metodo.
	/// </summary>
	private void AbrirOsDesejos(ServerPlayer pl, SetDeEsferas s)
	{
		double t = Desejos.PoderVerdadeiro(s.Poder, s.Desejos);

		Avisar(pl, $"{s.NomeDoDragao} aguarda. Pedidos nesta ativação: "
				 + $"{s.Desejos - s.Pedidos} de {s.Desejos}.");

		foreach (DesejoDef d in MenuDaqui(pl, s))
			Avisar(pl, $"  [{d.Id}] {d.Nome}"
					 + PedeAlvo(d)
					 + (d.Portado ? "" : "  -- AINDA NÃO PORTADO")
					 + $"  ({d.Desc})");

		Avisar(pl, $"-- peça com: db_desejar <id> [alvo] | poder do set {s.Poder:N0}, patamar {t:0.#}");
	}

	/// <summary>
	/// ESTE DESEJO PEDE UM ALVO? -- e o <see cref="DesejoDef.Alvo"/>, escrito na linha do menu.
	///
	/// ============================ ELE EXISTE PORQUE O CAMPO PRECISAVA DE UM LEITOR ============================
	/// A primeira versao do `DesejoDef` tinha o `Alvo` preenchido nas vinte entradas e **nada o lia**:
	/// cada efeito resolvia o proprio alvo, e o campo era decoracao. Esse e o defeito que este repo ja
	/// pagou oito vezes -- dado escrito sem consumidor --, e a correcao certa nao e apagar o campo: e
	/// que o jogador saiba, ANTES de digitar, que "matar" pede um nome e "riqueza" nao.
	/// ====================================================================================================
	/// </summary>
	private static string PedeAlvo(DesejoDef d) => d.Alvo switch
	{
		AlvoDoDesejo.Vivo => "  <alguém vivo, ao alcance do dragão>",
		AlvoDoDesejo.Morto => "  <alguém morto>",
		AlvoDoDesejo.Planeta => "  <um mundo destruído>",
		AlvoDoDesejo.Saiyajin => "  <um Saiyajin vivo>",
		_ => "",
	};

	/// <summary>
	/// A LISTA QUE ESTE JOGADOR VE. So costura os cinco fatos e chama a funcao pura do Core -- a escada
	/// nao tem uma segunda copia aqui dentro.
	/// </summary>
	private IEnumerable<DesejoDef> MenuDaqui(ServerPlayer pl, SetDeEsferas s) =>
		Desejos.Menu(s.Poder, s.Desejos, s.TemSupremo, EhKaioshinDoCorpo(pl),
					 pl.Ficha.Genoma?.RacePercent("Saiyan") ?? 0);

	/// <summary>
	/// `kai_body_wish_ok(M)` (`WishTable.dm:55-56`) -- os TRES ao mesmo tempo: raca Kai, classe Golden
	/// Apple, **e** ser o Kaioshin de agora.
	///
	/// O terceiro e por CONTA e nao por assinatura, e nao e descuido: o livro de tronos deste port e
	/// por conta (ver `GameServer.Ranks.cs`), e comparar "assinatura contra conta" nunca casaria -- que
	/// e exatamente o defeito que a Fase 0 achou no set eterno do DM. Mesma escolha ja feita no portao
	/// do Guardiao da Terra, em `ErguerEstatua`.
	/// </summary>
	private bool EhKaioshinDoCorpo(ServerPlayer pl) =>
		string.Equals(pl.Race, "Kai", StringComparison.OrdinalIgnoreCase)
		&& string.Equals(pl.Class, "Golden Apple", StringComparison.OrdinalIgnoreCase)
		&& _tronos.TryGetValue("kaioshin", out string? dono)
		&& string.Equals(dono, pl.Conta, StringComparison.OrdinalIgnoreCase);

	// =====================================================================
	// O PEDIDO
	// =====================================================================
	/// <summary>
	/// **`db_desejar <id> [alvo]`** -- o `input("Make your wish.")` do DM (`WishTable.dm:150`).
	///
	/// ============================ O MODAL VIROU ARGUMENTO, COMO O RESTO DO PORT ============================
	/// O original abre ate seis caixas encadeadas por desejo (escolha, alvo, "summon them to you?",
	/// "make extremely young?"). Num servidor autoritativo nao ha caixa bloqueante -- e a escolha ja foi
	/// feita e escrita duas vezes neste sistema (a conquista e o `Redo` da estatua): **o que era caixa
	/// de dialogo virou ARGUMENTO**. Sem alvo, o verbo LISTA os alvos possiveis em vez de recusar.
	/// ==================================================================================================
	/// </summary>
	private void PedirDesejo(ServerPlayer pl, string arg)
	{
		// ============================ SO QUEM INVOCOU PEDE, E ISSO E O CONSERTO DO FURO MAIOR ============================
		// No DM a trava e por ESFERA (`obj/var/tmp/summoned`), entao sete pessoas clicando as sete
		// esferas diferentes abriam sete listas da MESMA ativacao -- `Wishs x 7` desejos, vinte e um no
		// Porunga. A Fase 1 ja mudou a trava pro SET; esta linha fecha a outra metade: a lista e de quem
		// esta diante do dragao, e nao de quem passa por perto.
		// ==========================================================================================================
		KeyValuePair<int, Invocacao> par = _invocacoes.FirstOrDefault(i => i.Value.Invocador == pl.Id);
		if (par.Value is not { } inv || SetPorId(inv.Set) is not { } s)
		{ Avisar(pl, "não há dragão nenhum diante de você. Reúna as sete e invoque."); return; }

		if (s.Pedidos >= s.Desejos)
		{ Avisar(pl, "este set já deu tudo o que tinha nesta ativação."); return; }

		string[] p = arg.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
		if (p.Length == 0) { AbrirOsDesejos(pl, s); return; }

		string alvo = p.Length > 1 ? p[1].Trim() : "";

		DesejoDef? d = MenuDaqui(pl, s).FirstOrDefault(
			x => string.Equals(x.Id, p[0], StringComparison.OrdinalIgnoreCase));

		if (d == null)
		{
			Avisar(pl, $"{s.NomeDoDragao} não conhece esse desejo. Use db_desejar sem argumento pra ver a lista.");
			return;
		}

		// "Cancel" (:156-158): sai SEM consumir. O `break` do original -- e ele encerra a invocacao
		// inteira, mesmo que sobrassem pedidos.
		if (d.Id == "cancelar")
		{
			FecharAInvocacao(s.Id, $"{pl.Name} desiste do pedido, e {s.NomeDoDragao} some no céu.");
			return;
		}

		// ============================ O NAO-PORTADO NAO COBRA ============================
		// Ele e uma recusa e nao um desejo: nada e consumido, o dragao continua de pe, e a frase diz o
		// motivo em vez de "falhou". Ver o cabecalho de `DesejoDef.Portado`.
		// ============================================================================
		if (!d.Portado)
		{
			Avisar(pl, $"{s.NomeDoDragao} escuta \"{d.Nome}\"... e o desejo ainda NÃO FOI PORTADO "
					 + "deste jogo. Nenhum pedido foi gasto.");
			Avisar(pl, $"  motivo: {d.Falta}");
			// O NOME NO `switch` DO ORIGINAL, e nao so a linha: e ele que faz o jogador (ou quem for
			// portar isto um dia) achar o desejo no `WishTable.dm` sem contar linha.
			Avisar(pl, $"  no original: \"{d.Dm}\" ({d.Linha})");
			return;
		}

		// ============================ O BLOQUEIO DO CRIADOR ============================
		// `if(E_G==originator.key)` (:199 e :207) -- **as esferas usam o poder de quem as criou, e esse
		// poder nao amplifica a si mesmo**. Vale pros DOIS desejos que dao poder: "Power" e o supremo.
		//
		// Por ASSINATURA, que e o que o DM grava na estatua (`CreatorSig`, :164-169) justamente pra
		// sobreviver ao relog. E no set ETERNO **ninguem e bloqueado**, porque nao ha criador -- que e
		// o mesmo resultado do original, ainda que la ele saia de um defeito (`blockid =
		// Earth_Guardian`, uma signature, comparada contra `usr.key`: nunca casa).
		// ==========================================================================
		bool ehOCriador = s.CriadorSig.Length > 0
					   && string.Equals(s.CriadorSig, pl.Assinatura, StringComparison.Ordinal);

		if (ehOCriador && d.Id is "poder" or "supremo")
		{
			Avisar(pl, $"{s.NomeDoDragao}: \"as esferas usam o SEU poder -- ele não pode amplificar a si "
					 + "mesmo.\"");
			AnunciarDesejo($"O pedido de {pl.Name} falha: quem criou as esferas não se amplifica com elas.");
			// `return list(wishpower,TRUE)` -> `if(nl[2] == TRUE) break` (:173): a invocacao INTEIRA
			// encerra, mesmo que sobrassem pedidos. E o castigo do original, e ele fica.
			FecharAInvocacao(s.Id, $"{s.NomeDoDragao} se recolhe.");
			return;
		}

		if (!Conceder(pl, s, d, alvo)) return;   // recusa: nada consumido, o dragao continua de pe
		ContarUmDesejo(s);
	}

	/// <summary>
	/// **O FUNIL DE TODOS OS EFEITOS.** Devolve `true` quando o desejo foi CONCEDIDO (e so entao ele e
	/// contado).
	///
	/// Um `switch` e nao um dicionario de delegates: e a mesma forma do `proc/Wish` do original, cada
	/// ramo carrega a linha do DM ao lado, e a leitura lado a lado com a fonte e o que faz uma revisao
	/// 1:1 ser possivel.
	/// </summary>
	private bool Conceder(ServerPlayer pl, SetDeEsferas s, DesejoDef d, string alvo) => d.Id switch
	{
		"nada" => DesejoDeNada(pl, s),
		"dinheiro" => DesejoDeDinheiro(pl, s),
		"marcos" => DesejoDeMarcos(pl, s),
		"tecnologia" => DesejoDeTecnologia(pl, s),
		"reviver" => DesejoDeReviver(pl, s, alvo),
		"juventude" => DesejoDeJuventude(pl, s, alvo),
		"poder" => DesejoDePoder(pl, s),
		"inteligencia" => DesejoDeInteligencia(pl, s),
		"juventude_alheia" => DesejoDeJuventudeAlheia(pl, s, alvo),
		"curar_planeta" => DesejoDeCurarPlaneta(pl, s, alvo),
		"super_saiyajin" => DesejoDeSuperSaiyajin(pl, s),
		"reviver_todos" => DesejoDeReviverTodos(pl, s),
		"matar" => DesejoDeMatar(pl, s, alvo),
		"supremo" => DesejoSupremo(pl, s, pl),
		"corpo_saiyajin" => DesejoDeCorpoSaiyajin(pl, s, alvo),
		_ => false,
	};

	// =====================================================================
	// OS EFEITOS
	// =====================================================================
	/// <summary>
	/// "Nothing (Waste Wish)" -- `WishTable.dm:152-155`. `WishPower*=1.1`.
	///
	/// AQUI ELE FICA, e no DM ele nao ficava (defeito 5 do cabecalho). E a unica leitura em que a opcao
	/// significa alguma coisa -- e o efeito e inofensivo: o `Poder` so sobe patamar, e todo patamar ja
	/// e trivialmente satisfeito.
	/// </summary>
	private bool DesejoDeNada(ServerPlayer pl, SetDeEsferas s)
	{
		s.Poder *= Desejos.GordoDoDesejoVazio;
		SalvarEsferas();
		AnunciarDesejo($"{pl.Name} não pede nada -- e as esferas de {s.NomeDoDragao} ficam mais fortes.");
		Avisar(pl, $"o poder do set sobe para {s.Poder:N0}.");
		return true;
	}

	/// <summary>"Cash" -- `WishTable.dm:306-309`. `originator.zenni+=50000000`.</summary>
	private bool DesejoDeDinheiro(ServerPlayer pl, SetDeEsferas s)
	{
		pl.Ficha.Zeni += Desejos.ZeniDoDesejo;
		Persistir(pl);
		AnunciarDesejo($"{pl.Name} pede RIQUEZA a {s.NomeDoDragao}!");
		Avisar(pl, $"você recebe {Desejos.ZeniDoDesejo:N0} zeni. No bolso: {pl.Ficha.Zeni:N0}.");
		return true;
	}

	/// <summary>
	/// "Milestones" -- `WishTable.dm:360-371`. **UMA VEZ POR PERSONAGEM, PRA SEMPRE.**
	///
	/// O DM mantem dois contadores (`totalskillpoints` e `skillpoints`/`availablepoints`) e soma 2 nos
	/// dois; aqui isso e uma chamada so, porque o <see cref="Jandirus.Core.Skills.SkillBook.Conceder"/>
	/// ja e o funil desses dois contadores -- e o cabecalho dele explica por que eles sao dois.
	///
	/// A RECUSA NAO CONSOME, e nesse ponto o port e mais generoso que o original de proposito: la o
	/// `else` recusa E imprime "cancels [usr]'s wish" **sem devolver `TRUE`**, entao o pedido ja tinha
	/// sido gasto pelo `WishCount+=DidWish` da :176. Cobrar um pedido por uma recusa que o proprio jogo
	/// nao avisa de antemao e defeito, e nao design.
	/// </summary>
	private bool DesejoDeMarcos(ServerPlayer pl, SetDeEsferas s)
	{
		if (pl.Ficha.wishedpoints > 0)
		{
			Avisar(pl, "você já pediu Marcos ao dragão uma vez -- e é uma vez por vida. "
					 + "Nenhum pedido foi gasto.");
			return false;
		}

		pl.Ficha.wishedpoints += Desejos.MarcosDoDesejo;
		pl.Livro?.Conceder(Desejos.MarcosDoDesejo);
		MandarSkills(pl, forcar: true);
		Persistir(pl);

		AnunciarDesejo($"{pl.Name} pede SABEDORIA a {s.NomeDoDragao}!");
		Avisar(pl, $"você ganha {Desejos.MarcosDoDesejo} Marcos. Livres agora: {pl.Livro?.MarcosLivres ?? 0}.");
		return true;
	}

	/// <summary>
	/// "Technology" -- `WishTable.dm:376-382`.
	///
	/// ============================ O LIVRO VIROU O ESTUDO, E O NUMERO E O MESMO ============================
	/// O DM cria um `/obj/items/Research_Book` no chao com `IntPower = 100 * techskill**2` -- ou seja o
	/// item e um recipiente pra esse XP, e estudar e o que o transforma em nivel. Este port **nao tem
	/// item no chao** (`GameServer.Mochila.cs:101`: *"LARGAR APAGA, por enquanto"*), e inventar um
	/// recipiente de mochila que so serve pra ser aberto seria um passo a mais e nenhuma regra a mais.
	///
	/// Entao o desejo entrega **o mesmo XP, pela porta de estudo de producao** (`Fighter.Estudar`, que
	/// e quem aplica o `techmod` e sobe o nivel). A formula e literal; o que mudou foi o embrulho.
	/// ==================================================================================================
	/// </summary>
	private bool DesejoDeTecnologia(ServerPlayer pl, SetDeEsferas s)
	{
		double xp = Desejos.XpDoDesejoDeTecnologia(pl.Ficha.techskill);
		int subiu = pl.Ficha.Estudar(xp);
		Persistir(pl);

		AnunciarDesejo($"{pl.Name} pede CONHECIMENTO a {s.NomeDoDragao}!");
		Avisar(pl, $"um tratado inteiro se derrama na sua cabeça: {xp:N0} de estudo"
				 + (subiu > 0 ? $" -- tecnologia sobe pra {pl.Ficha.techskill:0}." : "."));

		// O DM tambem soma `techcost += 50*techskill` no livro -- e o custo de LER aquele item, um campo
		// do proprio objeto. Sem o objeto nao ha o que encarecer, e por isso ele nao tem porte: nao e um
		// efeito no personagem.
		return true;
	}

	/// <summary>
	/// "Power" -- `WishTable.dm:205-213`. `originator.BP += originator.capcheck(originator.relBPmax/4)`.
	///
	/// O `CapCheck` E A PORTA UNICA DO BP neste port (`Fighter.Training.cs:16`: *"Nada aqui escreve `BP
	/// +=` sem passar por..."*), e o original faz exatamente a mesma coisa -- entao o porte e literal e
	/// de graca. O bloqueio do criador foi cobrado antes, no `PedirDesejo`.
	/// </summary>
	private bool DesejoDePoder(ServerPlayer pl, SetDeEsferas s)
	{
		double antes = pl.Ficha.BP;
		pl.Ficha.BP += pl.Ficha.CapCheck(pl.Ficha.relBPmax * Desejos.FracaoDoDesejoDePoder);
		pl.Ficha.Statify();
		AplicarPoderes(pl);
		MandarFicha(pl);
		Persistir(pl);

		AnunciarDesejo($"{pl.Name} pede PODER a {s.NomeDoDragao}!");
		Avisar(pl, $"o seu poder de luta sobe de {antes:N0} para {pl.Ficha.BP:N0}.");
		return true;
	}

	/// <summary>
	/// "Intelligence" -- `WishTable.dm:372-375`. `genome.add_to_stat("Tech Modifier", 2)`.
	///
	/// O `"Tech Modifier"` do DM ja tem tradução firmada neste port: `EfeitosDeSkill.cs:58` mapeia essa
	/// chave exata pro campo `techmod`. Reusar o mapa e o que impede duas respostas pra mesma pergunta.
	/// </summary>
	private bool DesejoDeInteligencia(ServerPlayer pl, SetDeEsferas s)
	{
		pl.Ficha.techmod += Desejos.PontosDeInteligencia;
		Persistir(pl);

		AnunciarDesejo($"{pl.Name} pede INTELIGÊNCIA a {s.NomeDoDragao}!");
		Avisar(pl, $"tudo parece mais simples: multiplicador de aprendizado agora {pl.Ficha.techmod:0.##}.");
		return true;
	}

	/// <summary>
	/// "Youth" -- `WishTable.dm:296-305`. `Age = 25`, ou 10 no alerta "Make extremely young?".
	///
	/// O ALERTA VIROU ARGUMENTO (`db_desejar juventude extremo`), como o resto. E os DOIS campos de
	/// idade sao escritos: `ServerPlayer.Idade` (o que vai pro disco) e `Fighter.Idade` (o que a conta
	/// de BP le) -- o mesmo par que o `Restore_Youth` do lote G8 ja documenta. Escrever um so deixaria
	/// o poder e a ficha contando idades diferentes ate o proximo login.
	/// </summary>
	private bool DesejoDeJuventude(ServerPlayer pl, SetDeEsferas s, string arg)
	{
		int idade = string.Equals(arg.Trim(), "extremo", StringComparison.OrdinalIgnoreCase)
			? Desejos.IdadeDaJuventudeExtrema
			: Desejos.IdadeDaJuventude;

		PorIdade(pl, idade);
		AnunciarDesejo($"{pl.Name} pede JUVENTUDE a {s.NomeDoDragao}!");
		Avisar(pl, $"os anos escorrem de você: o seu corpo volta a ter {idade}."
				 + (idade < Desejos.IdadeDaJuventude
					? " (e um corpo de criança carrega menos poder que um adulto -- você escolheu isso.)"
					: ""));
		return true;
	}

	/// <summary>
	/// "Make Somebody Else Young" -- `WishTable.dm:275-295`.
	///
	/// ============================ DUAS DIFERENCAS DO DM, E AS DUAS SAO MEDIDAS ============================
	///   1. **O alvo tem que estar AO ALCANCE DO DRAGAO.** O original lista todo mob com client do
	///      mundo inteiro (`for(var/mob/M) if(M.client)`), o que faz do desejo um alcance global sem
	///      consentimento. Aqui ele alcanca quem esta diante do dragao -- o mesmo raio da invocacao.
	///   2. **Nunca 10 anos.** O alerta "Make extremely young?" do DM vale pro ALVO, e neste port isso
	///      seria um debuff: `Envelhecimento.DivisorDeIdade` da 1,00 aos 25 e 0,91 aos 10. O proprio
	///      port ja decidiu isso uma vez, no `Restore_Youth` do cargo (*"um Grand Kai poderia
	///      rejuvenescer um desafeto pra derrubar o poder dele"*), e ligar la e esquecer aqui e a
	///      armadilha nomeada. Em outro: **25, o auge**. Quem quiser ser crianca pede pra si.
	/// ==================================================================================================
	/// </summary>
	private bool DesejoDeJuventudeAlheia(ServerPlayer pl, SetDeEsferas s, string nome)
	{
		ServerPlayer? alvo = AlvoDoDesejoPorNome(pl, nome, o => o != pl && !o.Ficha.dead,
												 "quem você quer rejuvenescer");
		if (alvo == null) return false;

		PorIdade(alvo, Desejos.IdadeDaJuventude);
		AnunciarDesejo($"{pl.Name} pede a {s.NomeDoDragao} a JUVENTUDE de {alvo.Name}!");
		Avisar(alvo, $"os anos escorrem de você: o seu corpo volta a ter {Desejos.IdadeDaJuventude}.");
		Avisar(pl, $"{alvo.Name} volta ao auge.");
		return true;
	}

	/// <summary>OS DOIS CAMPOS DE IDADE, num lugar so -- ver `DesejoDeJuventude`.</summary>
	private void PorIdade(ServerPlayer quem, int idade)
	{
		quem.Idade = idade;
		quem.Ficha.Idade = idade;
		quem.Ficha.Statify();
		AplicarPoderes(quem);
		MandarFicha(quem);
		Persistir(quem);
	}

	/// <summary>
	/// "Revive" -- `WishTable.dm:214-237`.
	///
	/// ============================ ELE **NAO** E O `Revive` DE CARGO, E ISSO FOI MEDIDO ============================
	/// A Fase 0 fez a pergunta certa: o desejo respeita a contagem de ressurreicoes? A fonte responde,
	/// e responde as duas metades:
	///
	///   * **RESPEITA o `aged_out`** -- o DM pula quem morreu de velhice em TODOS os caminhos de revive
	///     (`WishTable.dm:222` aqui, `:245` no Revive-All, `ProceduralSpace.dm:1609` no Super Shenron, e
	///     as duas guardas de `Death.dm:144` e `:164`). Seis lugares dizendo a mesma coisa;
	///   * **IGNORA o `ResurrectedCount`** -- este proc chama `ReviveMe()` direto (:229) e **nao encosta**
	///     no contador. Quem soma 1 nele e so o verb de CARGO (`OtherworldRankSkills.dm:237`), e e la que
	///     mora o preco (*"a partir da 2a volta, quem ressuscita morre no lugar"*).
	///
	/// **A esfera e a excecao que o preco do cargo nao alcanca**, e a decisao esta na fonte e nao num
	/// palpite. E e por isso que este caminho **nao** reusa o `RessuscitarG4`: reusa-lo "porque ja
	/// existe" faria o desejo somar no contador e MATAR quem pediu -- um castigo que o original nao
	/// cobra. Ele entra por `Combate.Reviver` + `Persistir`, como o proprio `RessuscitarG4` faz.
	/// ========================================================================================================
	///
	/// O "Summon them to you?" do DM (:217) tambem virou o padrao: o revivido **e puxado** pra perto,
	/// que e o ramo "Yes". Sem modal nao ha como perguntar, e deixar o ressuscitado onde o cadaver caiu
	/// e o ramo que nao serve pra nada -- ele acordaria sozinho num campo qualquer.
	/// </summary>
	private bool DesejoDeReviver(ServerPlayer pl, SetDeEsferas s, string nome)
	{
		ServerPlayer? alvo = AlvoMortoPorNome(pl, nome);
		if (alvo == null) return false;

		Ressuscitar(alvo, pl.Zone, new Vec2(pl.Pos.X, pl.Pos.Y + 2 * ZoneCollision.TileSize));
		AnunciarDesejo($"{pl.Name} pede a {s.NomeDoDragao} a VIDA de {alvo.Name}!");
		Avisar(alvo, $"{s.NomeDoDragao} te arranca do Outro Mundo. Você está VIVO.");
		return true;
	}

	/// <summary>
	/// "Revive-All" -- `WishTable.dm:238-251`. Todos os mortos, menos os de velhice.
	///
	/// E ele **NAO EXISTE NO PORUNGA**: o gate `Wishs<=2` (:142) o tira de qualquer set de tres
	/// pedidos, e o eterno de Namek tem tres por define. Essa e a diferenca mecanica inteira entre os
	/// dois dragoes, e ela mora no <see cref="DesejoDef.SoAteDoisPedidos"/>.
	/// </summary>
	private bool DesejoDeReviverTodos(ServerPlayer pl, SetDeEsferas s)
	{
		List<ServerPlayer> mortos = [.. Pessoas.Where(o => o.Ficha.dead && !o.Ficha.aged_out)];
		if (mortos.Count == 0)
		{ Avisar(pl, "não há ninguém para trazer de volta. Nenhum pedido foi gasto."); return false; }

		var destino = new Vec2(pl.Pos.X, pl.Pos.Y + 2 * ZoneCollision.TileSize);
		foreach (ServerPlayer m in mortos)
		{
			Ressuscitar(m, pl.Zone, destino);
			Avisar(m, $"{s.NomeDoDragao} chama todos os mortos de volta -- e você atende.");
		}

		AnunciarDesejo($"{pl.Name} pede a {s.NomeDoDragao} a VIDA DE TODOS! "
					 + $"{mortos.Count} guerreiro(s) voltam.");
		return true;
	}

	/// <summary>
	/// O REVIVE DO DESEJO, num lugar so -- as duas chamadas (`Revive` e `Revive-All`) passam por aqui.
	///
	/// `dead=0` + `ReviveMe()` + tirar a auréola + puxar (`WishTable.dm:228-234`). O `Combate.Reviver`
	/// e a porta de producao: e ele que remonta membro a membro, incluindo o decepado. **Sem tocar no
	/// `ResurrectedCount`** -- ver o cabecalho do `DesejoDeReviver`.
	/// </summary>
	private void Ressuscitar(ServerPlayer alvo, ZoneKey zona, Vec2 onde)
	{
		alvo.Combate.Reviver(1, SegundosDeCarencia);
		alvo.Ficha.Ki = alvo.Ficha.MaxKi;
		alvo.Ficha.stamina = alvo.Ficha.maxstamina;
		alvo.RelogioDaMorte = 0;
		AjustarGanhoDoRabo(alvo);
		alvo.Ficha.Statify();
		PuxarParaG4(alvo, zona, onde);
		MandarFicha(alvo);
		Persistir(alvo);
	}

	/// <summary>
	/// "Kill Somebody" -- `WishTable.dm:310-326`. **A INTENCAO, e nao a letra.**
	///
	/// ============================ TRES DEFEITOS EMPILHADOS NO ORIGINAL ============================
	///   1. **nenhum ramo mata** -- os dois `if` imprimem a mesma frase e ninguem encosta no alvo;
	///   2. **a comparacao esta invertida** em relacao ao proprio prompt (*"If their power exceeds the
	///      creators power, it won't work!"*): `if(expressedBP >= TrueWishPower)` cai no ramo dito de
	///      sucesso quando o alvo e o mais FORTE;
	///   3. **o numero comparado nao e o `TrueWishPower`**: a chamada de :170 passa o `WishPower` CRU.
	///
	/// A decisao esta escrita no cabecalho do arquivo: **portar a intencao**. Mata quem for mais fraco
	/// que o poder do set; contra quem for mais forte, falha (e a falha nao devolve o pedido -- e o que
	/// o original faz, e e o risco que o desejo tem).
	///
	/// O `expressedBP` E O NUMERO CERTO, e e do DM: quem esconde o poder (sigilo/supressao) parece mais
	/// fraco e fica mais facil de matar. E a mesma escolha do `DragonObject/Kill()`.
	/// E A MORTE VAI PELA PORTA UNICA (`Combate.Morrer`) -- escrever `dead = true` na mao deixaria um
	/// morto sem relogio da morte, o defeito que o `GameServer.Sol.cs` ja documenta.
	/// ==========================================================================================
	/// </summary>
	private bool DesejoDeMatar(ServerPlayer pl, SetDeEsferas s, string nome)
	{
		ServerPlayer? alvo = AlvoDoDesejoPorNome(pl, nome, o => o != pl && !o.Ficha.dead,
												 "quem você quer matar");
		if (alvo == null) return false;

		AnunciarDesejo($"{pl.Name} pede a {s.NomeDoDragao} a MORTE de {alvo.Name}!");

		if (alvo.Ficha.expressedBP >= s.Poder)
		{
			AnunciarDesejo($"...e {alvo.Name} é forte demais para as esferas. O desejo FALHA.");
			Avisar(alvo, "algo tentou apagar você do mundo -- e não conseguiu.");
			return true;   // consumiu: o pedido foi feito, e falhar e o risco dele
		}

		alvo.Combate?.Morrer(ignorarSeguro: true);
		MandarFicha(alvo);
		Persistir(alvo);
		AnunciarDesejo($"...e {alvo.Name} cai sem um som.");
		GD.Print($"[esferas] {pl.Name} MATOU {alvo.Name} com um desejo (set {s.Etiqueta})");
		return true;
	}

	/// <summary>
	/// "Heal Planet" -- `WishTable.dm:327-342`.
	///
	/// ============================ ELE PASSA PELO `RessuscitarPlaneta`, E ISSO ESTAVA ESCRITO ============================
	/// O `GameServer.Destruicao.cs` ja tinha a ordem, de antes desta fase existir: *"Se a Fase 2 portar
	/// 'Heal Planet', ela tem que passar pelo `RessuscitarPlaneta` daqui e nao por um caminho proprio"*.
	/// E o motivo esta la tambem: o desejo do original zera `isDestroyed` e **nao tira da
	/// `PlanetDisableList`**, entao o planeta volta a morrer no boot seguinte -- foi justamente isso que
	/// o verb de admin (escrito depois) veio consertar. Portar a letra seria portar o bug.
	/// ==========================================================================================================
	/// </summary>
	private bool DesejoDeCurarPlaneta(ServerPlayer pl, SetDeEsferas s, string nome)
	{
		List<(string Nome, ZoneKey Zona)> mortos = [];
		foreach (Jandirus.Core.World.EstadoDaMorte e in _mortos.Todos)
		{
			if (!Jandirus.Core.World.MortePlanetaria.EstaMorto(e.Fase)) continue;
			if (!ChaveDePlaneta.Ler(e.Chave, e.Nome, out ChaveDePlaneta c)) continue;
			mortos.Add((e.Nome, c.PreFeito ? ZoneKey.Premade(c.Nome) : ZoneKey.Procedural(c.Nome, c.Seed)));
		}

		if (mortos.Count == 0)
		{ Avisar(pl, "nenhum mundo foi destruído. Nenhum pedido foi gasto."); return false; }

		if (nome.Length == 0)
		{
			Avisar(pl, "que mundo? " + string.Join(", ", mortos.Select(m => m.Nome)));
			return false;
		}

		(string Nome, ZoneKey Zona) alvo = mortos.FirstOrDefault(
			m => string.Equals(m.Nome, nome, StringComparison.OrdinalIgnoreCase));

		if (alvo.Nome == null)
		{ Avisar(pl, $"'{nome}' não está na lista dos mundos mortos. Nenhum pedido foi gasto."); return false; }

		if (!RessuscitarPlaneta(alvo.Zona))
		{ Avisar(pl, $"{alvo.Nome} não está na lista de mortos. Nenhum pedido foi gasto."); return false; }

		AnunciarDesejo($"{pl.Name} pede a {s.NomeDoDragao} que {NomeDoPlaneta(alvo.Nome)} VOLTE A EXISTIR!");
		return true;
	}

	/// <summary>
	/// "Super Saiyan" -- `WishTable.dm:409-455`. A escada de tres ramos do original.
	///
	/// ============================ OS TRES RAMOS, E O QUE ACONTECEU COM O TERCEIRO ============================
	///   a. **nao tem SSJ** -> libera o SSJ1;
	///   b. **tem SSJ e nao tem SSJ2** -> so libera **se ja existir alguem no mundo com SSJ2**
	///      (`for(var/mob/M) if(M.hasssj2) ssj2exists=1`). E a regra da lore inteira: o dragao nao cria
	///      um degrau que ninguem alcancou -- ele COPIA um que ja existe;
	///   c. **ja tem os dois** (ou o (b) sem ninguem no mundo) -> `badssjwish`, e o DM pergunta a CADA
	///      admin online se aprova, com log.
	///
	/// **O RAMO (c) NAO FOI PORTADO, e o resultado e o do proprio DM**: sem admin online (ou com
	/// negativa) ele imprime *"it fails!!"* e poe `wishpower = 1.05`. E exatamente isso que acontece
	/// aqui. Um `input()` modal em cada admin nao tem equivalente num servidor autoritativo, e os
	/// admins deste port ja tem verbos proprios pra conceder forma -- uma segunda porta pra mesma coisa
	/// seria o segundo eixo que a tarefa proibe.
	/// ======================================================================================================
	///
	/// `ssjdrain = 0.02` / `ssj2drain = 0.03` (:414 e :425) **nao tem porte**: neste port o dreno de cada
	/// forma e do CATALOGO (`FormaDef.Dreno`) e nao um campo por pessoa, e uma forma cujo custo mudasse
	/// por quem a ganhou seria uma segunda fonte de verdade pro mesmo numero.
	/// </summary>
	private bool DesejoDeSuperSaiyajin(ServerPlayer pl, SetDeEsferas s)
	{
		if (!pl.Forma.Despertou("ssj1"))
		{
			pl.Forma.Liberar("ssj1");
			Persistir(pl);
			HabilidadesMudaram(pl);
			AnunciarDesejo($"{pl.Name} pede a {s.NomeDoDragao} o SUPER SAIYAJIN -- e a lenda desperta!");
			Avisar(pl, "a porta do Super Saiyajin está aberta. Aperte C.");
			return true;
		}

		if (!pl.Forma.Despertou("ssj2"))
		{
			// `for(var/mob/M) if(M.hasssj2)` (:418-420) -- alguem no MUNDO ja alcancou? O laco do DM
			// varre todo mob; aqui varre quem esta logado, que e a lista equivalente.
			bool alguemTem = Pessoas.Any(o => o.Forma.Despertou("ssj2"));
			if (alguemTem)
			{
				pl.Forma.Liberar("ssj2");
				Persistir(pl);
				HabilidadesMudaram(pl);
				AnunciarDesejo($"{pl.Name} pede a {s.NomeDoDragao} o SUPER SAIYAJIN 2!");
				Avisar(pl, "o dragão copia um degrau que outro já alcançou. O SSJ2 está aberto.");
				return true;
			}
		}

		// O RAMO (c): o DM pediria aprovacao de admin e, sem ela, falha engordando a esfera 5%.
		s.Poder *= Desejos.GordoDaFalha;
		SalvarEsferas();
		AnunciarDesejo($"{pl.Name} pede a {s.NomeDoDragao} um degrau que ninguém alcançou -- e FALHA.");
		Avisar(pl, pl.Forma.Despertou("ssj2")
			? "você já subiu tão longe quanto o dragão consegue copiar. O pedido se perde."
			: "ninguém neste universo alcançou o Super Saiyajin 2 ainda -- o dragão não tem o que copiar.");
		return true;   // consumiu: e o que o original faz (o `WishCount+=DidWish` roda igual)
	}

	/// <summary>
	/// "Strongest in the Universe" -- `WishTable.dm:198-204` + `proc/sw_strongest_wish` (:102-113).
	///
	/// ============================ ELE NAO E UM BUFF: E UMA SENTENCA ============================
	/// BP = **o DOBRO do maior poder do jogo** (players E NPCs, mais o teto historico `TopBP`), e
	/// `sw_doom_year = Year + 1`. Quando o prazo vence (`Aging.dm:114-122`), o personagem recebe
	/// `aged_out` e MORRE -- a unica morte que nem as Super Esferas desfazem.
	///
	/// Portar o multiplicador sem portar a divida transformaria o desejo mais caro do jogo num presente.
	/// Por isso as duas metades saem daqui juntas, e a bancada exige as duas.
	/// ======================================================================================
	///
	/// O PARAMETRO `quem` EXISTE POR CAUSA DO PROCURADOR: no Super Shenron o desejo cai no BENEFICIARIO
	/// e nao em quem falou (`sw_strongest_wish(alvo)`, `ProceduralSpace.dm:1637`), e um metodo que
	/// assumisse "quem pediu" nao teria como servir aos dois.
	/// </summary>
	private bool DesejoSupremo(ServerPlayer pl, SetDeEsferas? s, ServerPlayer quem)
	{
		double melhor = 0;

		// `for(var/mob/M in mob_list)` (:105-106) -- **players E NPCs**, e nao so gente. E de proposito:
		// o titulo e "o mais forte do universo", e um chefe de saga conta.
		foreach (ServerPlayer o in _players.Values)
			if (o.Ficha.BP > melhor) melhor = o.Ficha.BP;

		// `best = max(best, TopBP)` (:107) -- o teto HISTORICO dos jogadores. Sem ele, matar todo mundo
		// antes de pedir baratearia o desejo.
		melhor = Math.Max(melhor, Jandirus.Core.Stats.GainKnobs.TopBP);

		quem.Ficha.BP = melhor * Desejos.MultiplicadorDoSupremo;
		quem.Ficha.sw_doom_year = TempoDoMundo + Esferas.SegundosDe(Desejos.AnosDoSupremo);
		quem.Ficha.Statify();
		AplicarPoderes(quem);
		MandarFicha(quem);
		Persistir(quem);

		string dragao = s?.NomeDoDragao ?? "SUPER SHENRON";
		AnunciarDesejo($"{quem.Name} trocou a própria VIDA por poder absoluto -- o ser mais forte do "
					 + $"universo caminha entre nós... por um ano. ({dragao})");
		Avisar(quem, $"o poder absoluto queima nas suas veias: {quem.Ficha.BP:N0}. O seu corpo tem "
				   + $"{Esferas.SegundosDe(Desejos.AnosDoSupremo) / 3600.0:0.#} h reais antes de cobrar "
				   + "o preço -- e dessa morte NÃO há retorno, apenas reencarnação.");
		if (quem != pl) Avisar(pl, $"{quem.Name} aceitou o preço.");

		GD.Print($"[esferas] {quem.Name} recebeu o Strongest in the Universe (BP {quem.Ficha.BP:N0}, "
			   + $"vence em {quem.Ficha.sw_doom_year:0})");
		return true;
	}

	/// <summary>
	/// "Saiyan Body" -- `WishTable.dm:181-197` + `proc/kai_take_saiyan_body` (:58-99). O desejo do Zamasu.
	///
	/// ============================ ELE E A UNICA PORTA DE UMA ESCADA INTEIRA QUE ESTAVA ORFA ============================
	/// O port ja tinha a linha Rose completa (`rose_ssg`, `rose`, `rose2`, ...) em `Core/Forms/Formas.cs`,
	/// toda ela gateada por `PedeClasseUmaDe = [ClasseRose]`, com `ClasseRose = "Kaio"` -- e a classe
	/// "Kaio" ja estava no `races.json` do Saiyajin, com bloco de stats proprio. **Nada neste port
	/// escrevia `Class = "Kaio"`.** A escada inteira era inalcancavel: dado extraido sem consumidor, o
	/// defeito que este repo pagou oito vezes.
	///
	/// Este desejo e o consumidor. E o mesmo papel que ele tem no original -- `kai_take_saiyan_body` e o
	/// unico lugar do DM que poe alguem na classe Kaio.
	/// ============================================================================================================
	///
	/// ============================ `godki_mod = 1.5` NAO TEM PORTE, E ELE NAO FAZ FALTA ============================
	/// A linha :90 do DM sobe o `godki_mod` pra 1.5 porque **la** o Rose e decidido por esse numero
	/// (`SaiyanObjects.dm:13-16`, o ramo `if(container.godki_mod > 1)`). **Aqui o Rose e decidido pela
	/// CLASSE**, e `Formas.cs:4111-4113` ja registra essa divergencia por escrito: *"Rose nao e uma
	/// forma a mais -- e a variante da classe"*. Portar o campo seria criar um segundo eixo pra mesma
	/// pergunta, e o primeiro a divergir seria o que ninguem lembrasse de escrever.
	/// ========================================================================================================
	/// </summary>
	private bool DesejoDeCorpoSaiyajin(ServerPlayer pl, SetDeEsferas? s, string nome)
	{
		if (!EhKaioshinDoCorpo(pl))
		{ Avisar(pl, "o dragão não reconhece em você o direito divino a este desejo."); return false; }

		ServerPlayer? molde = AlvoDoDesejoPorNome(pl, nome,
			o => o != pl && !o.Ficha.dead && EhSaiyajin(o), "de qual Saiyajin", mundoInteiro: true);
		if (molde == null) return false;

		VestirCorpoSaiyajin(pl, molde);

		string dragao = s?.NomeDoDragao ?? "SUPER SHENRON";
		AnunciarDesejo($"O céu range... {pl.Name} vestiu a carne de um mortal: o corpo de {molde.Name} "
					 + $"agora é DELE. ({dragao})");
		Avisar(pl, "o corpo é seu. Você é um Saiyajin de classe KAIO -- e o ki divino tinge as suas "
				 + "formas de ROSE em vez de azul.");
		Avisar(molde, $"um arrepio percorre a sua espinha... {pl.Name} agora veste um corpo IGUAL ao seu.");
		return true;
	}

	/// <summary>`S.Race == "Saiyan" || S.Race == "Legendary Saiyan" || S.Parent_Race == "Saiyan"` (:188).</summary>
	private static bool EhSaiyajin(ServerPlayer o) =>
		string.Equals(o.Race, "Saiyan", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(o.Race, "Legendary Saiyan", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(o.Ficha.ParentRace, "Saiyan", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// **A TROCA DE CORPO**, linha a linha do `kai_take_saiyan_body` (`WishTable.dm:58-99`).
	///
	/// O QUE MUDA: raca, raca-mae, linhagem (herdada do molde -- Primal continua Primal), classe,
	/// genoma+stats, aparencia, genero, BP e a idade.
	/// O QUE NAO MUDA, e o DM diz isso por escrito no cabecalho (:47-51): **skills, nome e cargo**. Ele
	/// continua sendo o Kaioshin -- e e isso que faz o desejo ser assustador em vez de so forte.
	/// </summary>
	private void VestirCorpoSaiyajin(ServerPlayer k, ServerPlayer molde)
	{
		// ---- o genoma NOVO, de Saiyajin, com a classe CRAVADA (o `StatRace("Saiyan",1)` do DM) ----
		// `classeForcada` existe exatamente pra isto e o comentario do `Birth.Nascer` ja explica o
		// paralelo: o original tambem crava a classe ANTES do `StatRace` *"so the stat procs skip the
		// input() class roll"*. Sem cravar, o dragao sortearia a classe e o Rose seria sorte.
		string linhagem = molde.Ficha.SaiyanLineage.Length > 0 ? molde.Ficha.SaiyanLineage : "Saiyan";

		if (_racas != null)
		{
			Jandirus.Core.Stats.Fighter novo = Jandirus.Core.Races.Birth.Nascer(
				_racas, "Saiyan", linhagem, new Random(), k.Name,
				classeForcada: Jandirus.Core.Forms.Catalogo.ClasseRose);

			// ENXERTO, e nao substituicao: a ficha do Kaioshin carrega marcos gastos, maestrias, cargo,
			// `godtongue`, `aged_out` e a divida do supremo -- trocar o objeto inteiro apagaria a alma
			// junto com o corpo, que e o oposto do que o desejo faz.
			EnxertarCorpo(k.Ficha, novo);
		}
		else GD.PushWarning("[esferas] corpo saiyajin sem catalogo de racas -- stats nao foram refeitos");

		k.Ficha.Race = "Saiyan";
		k.Ficha.ParentRace = "Saiyan";
		k.Ficha.SaiyanLineage = linhagem;
		k.Ficha.Class = Jandirus.Core.Forms.Catalogo.ClasseRose;
		k.Race = "Saiyan";
		k.Class = Jandirus.Core.Forms.Catalogo.ClasseRose;
		k.Linhagem = linhagem;

		// ---- a carne: aparencia e genero (`K.icon = newicon`, o cabelo e `K.gender = T.gender`) ----
		// A ROUPA NAO VAI JUNTO, e o DM tambem nao a leva: ele copia `oicon` (o corpo BASE, nunca o
		// transformado), o cabelo e o genero. Roupa e do Kaioshin, e ele continua sendo ele.
		Jandirus.Core.Appearance.Appearance nova = molde.Visual.Copiar();
		nova.Roupa = k.Visual.Roupa;
		k.Visual = nova;
		k.Genero = molde.Genero;

		// ---- o poder e a idade ----
		k.Ficha.BP = molde.Ficha.BP;

		// `K.Age = min(K.Age, 25)` (:91-92) -- e o comentario do DM explica: *"um Kai de seculos num
		// corpo Saiyajin (vida ~120) morreria de velhice na hora"*. Neste port isso e literal: o
		// `Envelhecimento.MorreuDeVelhice` compara a idade com o auge DA RACA, e o auge do Kai e outro.
		int idade = Math.Min(k.Idade, (int)Jandirus.Core.Races.Envelhecimento.IdadeAdulta);
		k.Idade = idade;
		k.Ficha.Idade = idade;

		k.Ficha.Statify();
		k.Ficha.Ki = k.Ficha.MaxKi;
		AplicarPoderes(k);
		MandarFicha(k);
		k.SigAtributos = "";
		TrocarAparencias(k);
		Persistir(k);

		GD.Print($"[esferas] {k.Name} vestiu o corpo de {molde.Name}: Saiyajin/{k.Class}, "
			   + $"linhagem {linhagem}, BP {k.Ficha.BP:N0} -- a escada ROSE abriu");
	}

	/// <summary>
	/// COPIA DO CORPO NOVO **so o que e do corpo**: os stats de raca/classe e o genoma.
	///
	/// A lista e explicita e nao um `MemberwiseClone` de proposito -- um clone levaria junto vida,
	/// nocaute, buffs de forma, a divida do supremo e o `godtongue`, e o desejo nao troca a alma.
	/// </summary>
	private static void EnxertarCorpo(Jandirus.Core.Stats.Fighter velho, Jandirus.Core.Stats.Fighter novo)
	{
		velho.Genoma = novo.Genoma;
		velho.physoff = novo.physoff;
		velho.physdef = novo.physdef;
		velho.kioff = novo.kioff;
		velho.kidef = novo.kidef;
		velho.kiskill = novo.kiskill;
		velho.technique = novo.technique;
		velho.speed = novo.speed;
		velho.magiskill = novo.magiskill;
		velho.KiMod = novo.KiMod;
		velho.BPMod = novo.BPMod;
		velho.basekiregen = novo.basekiregen;
		velho.baseAnger = novo.baseAnger;
		velho.UPMod = novo.UPMod;
		velho.TrainMod = novo.TrainMod;
		velho.MedMod = novo.MedMod;
		velho.SparMod = novo.SparMod;
		velho.GravMod = novo.GravMod;
	}

	// =====================================================================
	// ESCOLHER ALVO
	// =====================================================================
	/// <summary>
	/// O ALVO VIVO DE UM DESEJO -- sem nome, LISTA quem serve; com nome, acha.
	///
	/// O RECORTE PADRAO E O ALCANCE DO DRAGAO, e nao o mundo: o original varre `for(var/mob/M)` inteiro,
	/// o que faz de "rejuvenescer" e "matar" armas de alcance global sem que a vitima saiba que ha um
	/// dragao no mundo. Quem esta diante do dragao pelo menos ve o ceu escurecer.
	/// (`mundoInteiro: true` so pro molde Saiyajin, que o DM tambem escolhe do `player_list` -- e ali o
	/// desejo nao fere ninguem: o molde continua inteiro.)
	/// </summary>
	private ServerPlayer? AlvoDoDesejoPorNome(ServerPlayer pl, string nome, Func<ServerPlayer, bool> serve,
											  string pergunta, bool mundoInteiro = false)
	{
		List<ServerPlayer> cands = [.. Pessoas.Where(o => serve(o)
			&& (mundoInteiro
				|| (o.Zone.Hash == pl.Zone.Hash
					&& Math.Abs(o.Pos.X - pl.Pos.X) <= Esferas.AlcanceDaInvocacao
					&& Math.Abs(o.Pos.Y - pl.Pos.Y) <= Esferas.AlcanceDaInvocacao)))];

		return EscolherAlvo(pl, cands, nome, pergunta,
			mundoInteiro ? "" : " (ele precisa estar ao alcance do dragão)");
	}

	/// <summary>
	/// O ALVO MORTO -- `for(var/mob/M) if(M.dead)` (:220-224), **pulando quem morreu de VELHICE**.
	///
	/// O mundo INTEIRO aqui, e nao o alcance: um morto esta no Outro Mundo por definicao, e exigir que
	/// ele esteja ao lado tornaria o desejo impossivel. E o DM faz o mesmo.
	/// </summary>
	private ServerPlayer? AlvoMortoPorNome(ServerPlayer pl, string nome)
	{
		List<ServerPlayer> cands = [.. Pessoas.Where(o => o.Ficha.dead && !o.Ficha.aged_out)];
		return EscolherAlvo(pl, cands, nome, "quem você quer trazer de volta",
			" -- e quem morreu de VELHICE não volta por nenhum desejo");
	}

	/// <summary>Sem nome, lista; com nome, acha. Um lugar so, pras tres perguntas de alvo.</summary>
	private ServerPlayer? EscolherAlvo(ServerPlayer pl, List<ServerPlayer> cands, string nome,
									   string pergunta, string rodape)
	{
		if (cands.Count == 0)
		{ Avisar(pl, $"não há ninguém que sirva{rodape}. Nenhum pedido foi gasto."); return null; }

		if (nome.Length == 0)
		{
			Avisar(pl, $"{pergunta}? {string.Join(", ", cands.Select(o => o.Name))}{rodape}");
			return null;
		}

		ServerPlayer? achado = cands.Find(o => string.Equals(o.Name, nome, StringComparison.OrdinalIgnoreCase));
		if (achado == null)
			Avisar(pl, $"'{nome}' não está na lista. Nenhum pedido foi gasto.");
		return achado;
	}

	// =====================================================================
	// A DIVIDA DO SUPREMO
	// =====================================================================
	/// <summary>
	/// **A COBRANCA DO "MAIS FORTE DO UNIVERSO"** -- `Aging.dm:114-122`.
	///
	/// ============================ ELA PASSA POR CIMA DE TUDO, E O DM DIZ ISSO ============================
	/// O bloco do original roda **antes** de qualquer guarda de nao-envelhecer (o comentario da :114 e
	/// explicito), entao imortal, vampiro e Deus da Destruicao morrem igual no vencimento. Aqui ele e
	/// conferido no mesmo lugar em que a velhice natural ja e -- e ANTES dela, pela mesma razao.
	///
	/// `aged_out = 1` **antes** da morte, como na velhice natural: e o `AoMorrer` que arma o prazo e a
	/// viagem pro Outro Mundo, e marcar depois deixaria esse gancho rodar sem saber que esta e a morte
	/// que nao tem volta.
	/// ================================================================================================
	/// </summary>
	/// <returns>Verdadeiro se ele morreu agora.</returns>
	private bool ConferirDividaDoSupremo(ServerPlayer pl)
	{
		if (pl.Ficha.sw_doom_year <= 0 || pl.Ficha.dead) return false;
		if (TempoDoMundo < pl.Ficha.sw_doom_year) return false;

		pl.Ficha.sw_doom_year = 0;   // a divida se paga uma vez
		pl.Ficha.aged_out = true;

		pl.Combate?.Morrer(ignorarSeguro: true);
		MandarFicha(pl);
		Persistir(pl);

		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			Avisar(o, o == pl
				? "o poder emprestado consome você por completo -- o prazo do desejo venceu."
				: $"o corpo de {pl.Name} se desfaz: o preço do desejo venceu.");

		AnunciarDesejo($"O prazo cobrou {pl.Name}. O ser mais forte do universo não existe mais.");
		GD.Print($"[esferas] {pl.Name} morreu pela divida do Strongest in the Universe");
		return true;
	}

	// =====================================================================
	// A LINGUA -- as duas bocas que a LIGAM
	// =====================================================================
	/// <summary>
	/// **REAVALIA A LINGUA DOS DEUSES** e avisa quem acabou de aprender.
	///
	/// ============================ DOIS CHAMADORES, E OS DOIS SAO OBRIGATORIOS ============================
	/// O DM chama `godtongue_check()` em quatro pontos, e dois deles sao os que importam:
	/// **`RankAssign.dm:144`** (o instante em que se ganha um cargo) e a cadeia do **login**
	/// (`ProceduralSpace.dm:1464`). Ligar so um e a armadilha nomeada: com so o login, um Kaio recem-
	/// coroado teria que deslogar pra falar; com so a coroacao, ninguem de sangue Kai/Demigod aprenderia
	/// nunca (eles nao passam por cargo nenhum).
	///
	/// **E ELA SO LIGA.** Ver `LinguaDosDeuses.Reavaliar`: nao existe caminho que apague o campo.
	/// ================================================================================================
	/// </summary>
	private void ConferirALingua(ServerPlayer pl)
	{
		if (pl.Ficha.godtongue) return;

		if (!LinguaDosDeuses.Reavaliar(false, CargoDe(pl.Conta), pl.Race, pl.Ficha.ParentRace)) return;

		pl.Ficha.godtongue = true;
		Persistir(pl);
		Avisar(pl, LinguaDosDeuses.FraseAoAprender);
		GD.Print($"[server] {pl.Name} aprendeu a Lingua dos Deuses "
			   + $"(cargo '{CargoDe(pl.Conta)}', raca {pl.Race}/{pl.Ficha.ParentRace})");
	}

	// =====================================================================
	// REDE
	// =====================================================================
	/// <summary>Uma linha pra todo mundo. Mesmo canal do `AnunciarNoMundo`; a escuta e da bancada.</summary>
	private void AnunciarDesejo(string texto)
	{
		if (texto.Length == 0) return;
		EscutaDosDesejos?.Add(texto);
		AnunciarNoMundo(texto);
	}
}
