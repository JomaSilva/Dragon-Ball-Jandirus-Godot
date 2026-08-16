namespace Jandirus.Core.Magic;

/// <summary>O que o desejo precisa saber ANTES de acontecer -- e o que o menu tem que pedir.</summary>
public enum AlvoDoDesejo
{
	/// <summary>Cai em quem pediu. A maioria.</summary>
	Nenhum,

	/// <summary>Alguem VIVO ao alcance do dragao.</summary>
	Vivo,

	/// <summary>Alguem MORTO -- e o `for(var/mob/M) if(M.dead)` do original.</summary>
	Morto,

	/// <summary>Um planeta destruido.</summary>
	Planeta,

	/// <summary>Um Saiyajin vivo, pra servir de molde.</summary>
	Saiyajin,
}

/// <summary>
/// UMA LINHA DA TABELA DE DESEJOS. Dado puro -- o EFEITO e codigo de servidor e mora no
/// `GameServer.Desejos.cs`, pelo mesmo recorte que <see cref="Skills.Tecnicas"/> ja usa.
/// </summary>
public sealed class DesejoDef
{
	/// <summary>Id estavel, o que viaja no verbo. Nunca traduzido.</summary>
	public string Id = "";

	/// <summary>O nome do desejo no `switch` do DM -- a chave literal de `WishTable.dm:180`.</summary>
	public string Dm = "";

	/// <summary>Onde ele mora no original. Sempre preenchido: e a fonte, e ela se confere.</summary>
	public string Linha = "";

	/// <summary>Como o jogador le.</summary>
	public string Nome = "";

	public string Desc = "";

	/// <summary>
	/// O `TrueWishPower` MINIMO -- o degrau em que ele entra na lista (`WishTable.dm:125-146`).
	/// Zero = sempre aparece.
	/// </summary>
	public double Patamar;

	/// <summary>
	/// O GATE QUE **DE VERDADE** MORDE -- `if(TrueWishPower>=7 && Wishs<=2)` (:142).
	///
	/// E a UNICA diferenca mecanica entre Shenron e Porunga: o set eterno de Namek tem `Wishs = 3`,
	/// entao "Revive-All" e "Kill Somebody" **nao existem nele**. Ver <see cref="Desejos.Menu"/>.
	/// </summary>
	public bool SoAteDoisPedidos;

	/// <summary>So aparece se o criador comprou o desejo supremo (`HasStrongestWish`, :147-148).</summary>
	public bool PedeSupremo;

	/// <summary>So aparece pro Kaioshin da classe Golden Apple (`kai_body_wish_ok`, :149).</summary>
	public bool PedeKaioshin;

	/// <summary>So aparece pra quem tem 25% ou mais de genoma Saiyajin (:141).</summary>
	public bool PedeSangueSaiyajin;

	public AlvoDoDesejo Alvo = AlvoDoDesejo.Nenhum;

	/// <summary>
	/// O EFEITO ESTA PORTADO? Falso = o desejo APARECE na lista e diz que ainda nao foi trazido, sem
	/// consumir pedido.
	///
	/// ============================ POR QUE ELE APARECE, EM VEZ DE SUMIR ============================
	/// E a mesma escolha (e o mesmo argumento) do <see cref="Skills.Modo.NaoPortada"/>: *"e tentador
	/// listar so o que funciona, mas ai o jogo fica sem como responder 'isso existe e ainda nao faz
	/// nada'"*. Sumir com a linha faria a tabela do port parecer completa -- e faria o jogador que leu
	/// o guia do jogo antigo procurar pra sempre um desejo que ele nunca vai achar.
	///
	/// E ele **nao consome o pedido**: um botao que promete e pior que um botao que nao existe, mas um
	/// botao que promete E COBRA e o pior dos tres.
	/// ========================================================================================
	/// </summary>
	public bool Portado = true;

	/// <summary>Por que nao foi portado. Obrigatorio quando <see cref="Portado"/> e falso.</summary>
	public string Falta = "";
}

/// <summary>
/// **A TABELA DE DESEJOS** -- a lista, os patamares e a conta do poder. Porte de
/// `Code/Modules/Magic/WishTable.dm`.
///
/// ============================ A CONTA DO PATAMAR E DECORATIVA, E ISSO E MEDIDO ============================
/// `TrueWishPower = log(max(WishPower/Wishs,1))^2 + 1` (:117), com `log` NATURAL. Invertendo, um
/// patamar T exige `WishPower >= Wishs * e^sqrt(T-1)`: T>=10 pede apenas **20x** o numero de pedidos.
///
/// Como `WishPower` e o BP do criador (milhoes), **todos os patamares sao trivialmente satisfeitos** --
/// ate o set eterno, com 2.000.000 e 3 pedidos, chega a T ~= 180. A escada de poder do original e
/// enfeite, e o unico gate que morde de verdade e o `Wishs<=2` do patamar 7.
///
/// A conta foi portada **assim mesmo**, exatamente como esta la. Nao por preguica: mexer nela seria
/// rebalancear um sistema inteiro por conta propria, e a regra da casa e formula 1:1. O que este
/// comentario faz e garantir que ninguem descubra isso de novo do zero daqui a seis meses.
/// ========================================================================================================
///
/// ============================ DOIS DEFEITOS DE MENU DO ORIGINAL, FECHADOS ============================
///   1. **`var/list/WishList = list()` esta FORA do `while`** (:118) e os `WishList+=` estao DENTRO.
///      No segundo pedido da mesma invocacao o menu vinha com todas as entradas DUPLICADAS, e no
///      terceiro triplicadas -- visivel pro jogador em todo Porunga. Aqui o menu e uma funcao pura
///      que devolve uma lista nova, entao o defeito nao tem onde morar.
///   2. **`WishCount += GenerateWishList(usr)`** (`Dragonballs.dm:229`) soma NULL: a proc nao tem
///      `return` e a contagem real acontece dentro dela. A soma de fora e no-op. Aqui quem conta e
///      `ContarUmDesejo`, um funil so, e ele e chamado de um lugar so.
/// ================================================================================================
/// </summary>
public static class Desejos
{
	// =====================================================================
	// A CONTA
	// =====================================================================
	/// <summary>
	/// `TrueWishPower = log(max(WishPower/Wishs,1))^2 + 1` (`WishTable.dm:117`).
	///
	/// **`log()` DO BYOND E LOGARITMO NATURAL** com um argumento so -- e nao base 10. Trocar por
	/// `Log10` faria todo patamar acima de 3 ficar inalcancavel e a tabela inteira encolheria pra tres
	/// linhas, calada. E o mesmo cuidado que o `expbarrier` ja custou uma sessao inteira neste projeto.
	/// </summary>
	public static double PoderVerdadeiro(double poder, int pedidos)
	{
		double razao = Math.Max(poder / Math.Max(pedidos, 1), 1);
		double l = Math.Log(razao);
		return l * l + 1;
	}

	/// <summary>`WishPower*=1.1` do "Nothing (Waste Wish)" (`WishTable.dm:154`).</summary>
	public const double GordoDoDesejoVazio = 1.1;

	/// <summary>`wishpower = 1.05` da falha do Super Saiyan (`WishTable.dm:445`).</summary>
	public const double GordoDaFalha = 1.05;

	/// <summary>`originator.zenni+=50000000` (`WishTable.dm:308`).</summary>
	public const double ZeniDoDesejo = 50_000_000;

	/// <summary>
	/// `SDB_WISH_ZENNI 5000000` (`ProceduralSpace.dm:43`) -- a riqueza do **Super** Shenron.
	///
	/// ============================ E ELA E DEZ VEZES MENOR QUE A DO SHENRON COMUM ============================
	/// Cinco milhoes contra os cinquenta de <see cref="ZeniDoDesejo"/>, e o Super custa cacar sete
	/// planetoides pela galaxia contra sete esferas num planeta so. **O desequilibrio e do original** e
	/// foi portado 1:1 de proposito -- a regra da casa e formula 1:1, e "consertar" isto seria escolher
	/// um numero novo sem o dono ter pedido. Fica medido aqui pra que a escolha seja dele.
	/// ====================================================================================================
	/// </summary>
	public const double ZeniDoSuperDesejo = 5_000_000;

	/// <summary>`originator.BP+=originator.capcheck(originator.relBPmax/4)` (`WishTable.dm:208`).</summary>
	public const double FracaoDoDesejoDePoder = 0.25;

	/// <summary>`originator.genome.add_to_stat("Tech Modifier",2)` (`WishTable.dm:374`).</summary>
	public const double PontosDeInteligencia = 2;

	/// <summary>`wishedpoints/totalskillpoints/skillpoints/availablepoints += 2` (`WishTable.dm:367-370`).</summary>
	public const int MarcosDoDesejo = 2;

	/// <summary>`revivespecific.Age = 25` (`WishTable.dm:284`) -- e o auge, e o auge e o melhor.</summary>
	public const int IdadeDaJuventude = 25;

	/// <summary>
	/// `Age = 10` do alerta "Make extremely young?" (`WishTable.dm:287`).
	///
	/// ============================ E ELE SO VALE PRA SI MESMO. MEDIDO, NAO ACHADO ============================
	/// Neste port a idade entra na conta de poder: `Envelhecimento.DivisorDeIdade` devolve **1,00 aos
	/// 25 anos e 0,91 aos 10**. Ou seja, "extremamente jovem" e um **debuff de 9%** disfarcado de
	/// presente -- e o DM deixa qualquer um aplica-lo em qualquer jogador do mundo, sem consentimento.
	///
	/// O proprio port ja tomou posicao sobre isso uma vez: o `Restore_Youth` do cargo (lote G8) EXIGE
	/// consentimento, com o comentario *"um Grand Kai poderia rejuvenescer um desafeto pra derrubar o
	/// poder dele"*. Ligar a regra la e esquece-la aqui e literalmente a armadilha que este repo mais
	/// paga. A correcao e de uma linha e nao pede maquina de consentimento nenhuma: **em outro, so 25**
	/// (o otimo). Quem quiser ser crianca escolhe isso pra si.
	/// ====================================================================================================
	/// </summary>
	public const int IdadeDaJuventudeExtrema = 10;

	/// <summary>`best * SW_STRONGEST_MULT`, define 2 (`WishTable.dm:18`).</summary>
	public const double MultiplicadorDoSupremo = 2;

	/// <summary>`Year + SW_STRONGEST_YEARS`, define 1 (`WishTable.dm:19`) -- em `Year` do DM.</summary>
	public const double AnosDoSupremo = 1;

	/// <summary>`SW_WISH_PRICE 2000000` (`WishTable.dm:20`) -- o que o criador paga pra gravar o supremo.</summary>
	public const double PrecoDoSupremo = 2_000_000;

	/// <summary>`usr.genome.race_percent("Saiyan") >= 25` (`WishTable.dm:141`).</summary>
	public const double SangueSaiyajinMinimo = 25;

	/// <summary>`A.IntPower = 100 * originator.techskill**2` (`WishTable.dm:380`).</summary>
	public static double XpDoDesejoDeTecnologia(double techskill) => 100 * techskill * techskill;

	// =====================================================================
	// A TABELA
	// =====================================================================
	/// <summary>
	/// **A LISTA COMPLETA**, na ordem em que o `GenerateWishList` a monta (`WishTable.dm:122-149`).
	///
	/// Vinte entradas -- as dezenove do menu mais o "Cancel", que nao e desejo e nao consome nada. A
	/// vigesima-primeira do arquivo do DM ("Gender Change", :383-408) **nao esta aqui** e a ausencia e
	/// fiel: o codigo dela existe completo no original e a string **nunca e inserida no WishList**. Ela
	/// e codigo morto la, e um desejo que o proprio jogo antigo nunca ofereceu nao e divida deste port.
	/// </summary>
	public static readonly DesejoDef[] Todos =
	[
		// -------------------------------------------------- sempre
		new() { Id = "nada", Dm = "Nothing (Waste Wish)", Linha = "WishTable.dm:152-155",
				Nome = "Nada (desperdiçar o pedido)",
				Desc = "Você não pede nada -- e as esferas ficam 10% mais fortes.",
				Patamar = 0 },

		new() { Id = "calcinha", Dm = "Panties", Linha = "WishTable.dm:343-359",
				Nome = "Calcinha", Alvo = AlvoDoDesejo.Vivo,
				Desc = "O desejo mais antigo do jogo. Cria a calcinha de alguém, pra vestir na cabeça.",
				Patamar = 0, Portado = false,
				// A ARTE E A MECANICA, e nao ilustracao: o item existe pra ser VESTIDO
				// (`updateOverlay(/obj/overlay/clothes/panties, 'Clothes_pantsuhat.dmi')`, :518). Nem
				// `Icons/Misc/Panties.png` nem o `.dmi` do chapeu estao no port, e sem o overlay ele
				// seria um item de mochila sem uso nenhum -- que e pior que a recusa honesta.
				Falta = "a arte (Icons/Misc/Panties.png + Clothes_pantsuhat.dmi) não foi importada, e "
					  + "o item existe pra ser VESTIDO na cabeça -- sem o overlay ele não faz nada" },

		new() { Id = "cancelar", Dm = "Cancel", Linha = "WishTable.dm:156-158",
				Nome = "Cancelar",
				Desc = "Sai sem pedir. NÃO gasta o pedido.",
				Patamar = 0 },

		// -------------------------------------------------- TrueWishPower >= 2
		new() { Id = "dinheiro", Dm = "Cash", Linha = "WishTable.dm:306-309",
				Nome = "Riqueza", Patamar = 2,
				Desc = "50.000.000 de zeni no bolso." },

		new() { Id = "marcos", Dm = "Milestones", Linha = "WishTable.dm:360-371",
				Nome = "Marcos", Patamar = 2,
				Desc = "2 Marcos. UMA VEZ por personagem, pra sempre." },

		new() { Id = "tecnologia", Dm = "Technology", Linha = "WishTable.dm:376-382",
				Nome = "Tecnologia", Patamar = 2,
				Desc = "Um tratado que vale 100 x (sua tecnologia)² de estudo." },

		// -------------------------------------------------- TrueWishPower >= 3
		new() { Id = "reviver", Dm = "Revive", Linha = "WishTable.dm:214-237",
				Nome = "Ressuscitar alguém", Patamar = 3, Alvo = AlvoDoDesejo.Morto,
				Desc = "Traz um morto de volta e o puxa pra perto de você. Quem morreu de VELHICE não volta." },

		new() { Id = "juventude", Dm = "Youth", Linha = "WishTable.dm:296-305",
				Nome = "Juventude", Patamar = 3,
				Desc = "Você volta aos 25 anos. Com o argumento 'extremo', aos 10 -- e 10 custa 9% do seu poder." },

		new() { Id = "poder", Dm = "Power", Linha = "WishTable.dm:205-213",
				Nome = "Poder", Patamar = 3,
				Desc = "Um quarto do que ainda cabe no seu teto de treino. O CRIADOR do set não pode pedir." },

		new() { Id = "inteligencia", Dm = "Intelligence", Linha = "WishTable.dm:372-375",
				Nome = "Inteligência", Patamar = 3,
				Desc = "+2 no seu multiplicador de aprendizado de tecnologia." },

		// -------------------------------------------------- TrueWishPower >= 4
		new() { Id = "juventude_alheia", Dm = "Make Somebody Else Young", Linha = "WishTable.dm:275-295",
				Nome = "Rejuvenescer outro", Patamar = 4, Alvo = AlvoDoDesejo.Vivo,
				Desc = "Devolve alguém aos 25 anos -- o auge, nunca menos. Ver o porquê em Desejos.IdadeDaJuventudeExtrema." },

		new() { Id = "alma", Dm = "Give Soul", Linha = "WishTable.dm:456-472",
				Nome = "Dar uma alma", Patamar = 4, Alvo = AlvoDoDesejo.Vivo, Portado = false,
				Desc = "Dá alma a quem não tem.",
				// `HasSoul` no DM e lido pelos RITUAIS DIMENSIONAIS (`Rituals_Dimensional.dm:302-318`) e
				// pela absorcao -- e o sistema de magia nao existe neste port (o censo o chama de
				// `SistMagia`). Escrever o campo sem os leitores seria o defeito que este repo ja pagou
				// oito vezes: dado extraido sem consumidor.
				Falta = "o port não tem alma: no DM o campo `HasSoul` só é lido pelos rituais dimensionais "
					  + "(sistema de magia), e o censo de skills marca a magia como não portada" },

		new() { Id = "magia", Dm = "Gain Magic", Linha = "WishTable.dm:137 e 473-482",
				Nome = "Ganhar magia", Patamar = 4, Portado = false,
				Desc = "Desperta a magia: palavra de poder e ritual.",
				// DEFEITO DO ORIGINAL, e vale registrar: o menu insere **"Gain Magic"** (:137) e o
				// `switch` do proc so tem **`if("Give Magic")`** (:473). Nomes diferentes -- a opcao
				// aparecia, caia no fim do switch, NAO FAZIA NADA e ainda **consumia o pedido**. Ou seja
				// este desejo nunca funcionou nem la.
				Falta = "no próprio DM ele é morto (o menu insere \"Gain Magic\" e o switch só conhece "
					  + "\"Give Magic\" -- consumia o pedido sem efeito), e o port não tem sistema de magia" },

		// -------------------------------------------------- TrueWishPower >= 5
		new() { Id = "curar_planeta", Dm = "Heal Planet", Linha = "WishTable.dm:327-342",
				Nome = "Curar um planeta", Patamar = 5, Alvo = AlvoDoDesejo.Planeta,
				Desc = "Um mundo destruído volta a existir." },

		// -------------------------------------------------- TrueWishPower >= 6
		new() { Id = "super_saiyajin", Dm = "Super Saiyan", Linha = "WishTable.dm:409-455",
				Nome = "Super Saiyajin", Patamar = 6, PedeSangueSaiyajin = true,
				Desc = "Libera o Super Saiyajin. Quem já o tem ganha o SSJ2 -- mas só se alguém no mundo já o tiver alcançado." },

		// -------------------------------------------------- TrueWishPower >= 7 E Wishs <= 2
		new() { Id = "reviver_todos", Dm = "Revive-All", Linha = "WishTable.dm:238-251",
				Nome = "Ressuscitar TODOS", Patamar = 7, SoAteDoisPedidos = true,
				Desc = "Todos os mortos voltam. Quem morreu de velhice fica de fora até deste." },

		new() { Id = "matar", Dm = "Kill Somebody", Linha = "WishTable.dm:310-326",
				Nome = "Matar alguém", Patamar = 7, SoAteDoisPedidos = true, Alvo = AlvoDoDesejo.Vivo,
				Desc = "Mata quem for MAIS FRACO que o poder do set. Contra quem for mais forte, falha." },

		// -------------------------------------------------- TrueWishPower >= 10
		new() { Id = "imortalidade", Dm = "Immortality", Linha = "WishTable.dm:252-274",
				Nome = "Imortalidade", Patamar = 10, Portado = false,
				Desc = "Imortalidade -- e no original é um interruptor, que também a TIRA.",
				// Nao ha `immortal` neste port: nem campo, nem leitor. Inventa-lo pediria que a morte, o
				// envelhecimento e o sol o consultassem -- tres funis, tres regras, e um deles esquecido
				// e um imortal que morre queimado. Isso e um sistema, e nao um desejo.
				Falta = "o port não tem flag de imortalidade: ela teria que ser lida pela morte, pelo "
					  + "envelhecimento e pelo sol -- três funis, e esquecer um é um imortal que morre" },

		// -------------------------------------------------- condicionais, fora da escada
		new() { Id = "supremo", Dm = "Strongest in the Universe", Linha = "WishTable.dm:198-204 e 102-113",
				Nome = "O Mais Forte do Universo", PedeSupremo = true,
				Desc = "O DOBRO do maior poder do jogo -- e UM ANO de vida. Dessa morte não há volta." },

		new() { Id = "corpo_saiyajin", Dm = "Saiyan Body", Linha = "WishTable.dm:181-197 e 58-99",
				Nome = "Corpo de um Saiyajin", PedeKaioshin = true, Alvo = AlvoDoDesejo.Saiyajin,
				Desc = "O desejo do Zamasu: você veste o corpo de um Saiyajin vivo, e o ki divino tinge suas formas de ROSE." },
	];

	// ============================ NAO HA UM `Get(id)` AQUI, E A FALTA E DE PROPOSITO ============================
	// A tentacao obvia era um `Get` de tabela inteira, como o `Tecnicas.Get`. **Ele seria uma porta
	// lateral perigosa**: quem responde "este jogador pode pedir isto?" e o <see cref="Menu"/>, que
	// aplica os cinco gates (patamar, `Wishs<=2`, supremo comprado, Kaioshin, sangue Saiyajin). Um
	// `Get` cru devolveria o "Imortalidade" de um set de patamar 2 -- e o proximo a chamar seria quem
	// esquecesse de conferir o menu depois.
	//
	// O servidor procura DENTRO do menu daquele jogador, e por isso nao ha como pedir o que nao esta
	// na lista. A primeira versao tinha o `Get` e ele nasceu orfao; apagar foi mais barato que guardar.
	// ========================================================================================================

	public static int Portados => Todos.Count(d => d.Portado && d.Id != "cancelar");
	public static int NaoPortados => Todos.Count(d => !d.Portado);

	// =====================================================================
	// O MENU
	// =====================================================================
	/// <summary>
	/// **A LISTA QUE ESTE JOGADOR VE DIANTE DESTE SET** -- o `GenerateWishList` (`WishTable.dm:115-149`).
	///
	/// Funcao PURA: ela nao le mundo nenhum, so os cinco fatos que decidem cada gate. Por isso a
	/// bancada consegue afirmar "o Porunga de tres pedidos NAO oferece Revive-All" sem subir servidor,
	/// e o servidor nao tem uma segunda copia da escada dentro de um `if`.
	/// </summary>
	/// <param name="poder">O `WishPower` do set.</param>
	/// <param name="pedidos">O `Wishs` do set -- 1 a 3.</param>
	/// <param name="temSupremo">O criador comprou o "Strongest in the Universe"?</param>
	/// <param name="ehKaioshin">`kai_body_wish_ok` -- Kai + Golden Apple + Kaioshin atual.</param>
	/// <param name="sangueSaiyajin">`genome.race_percent("Saiyan")`, 0 a 100.</param>
	public static IEnumerable<DesejoDef> Menu(
		double poder, int pedidos, bool temSupremo, bool ehKaioshin, double sangueSaiyajin)
	{
		double t = PoderVerdadeiro(poder, pedidos);

		foreach (DesejoDef d in Todos)
		{
			if (d.Patamar > t) continue;

			// O GATE QUE SEPARA SHENRON DE PORUNGA, e o unico que morde de verdade neste sistema.
			if (d.SoAteDoisPedidos && pedidos > 2) continue;

			if (d.PedeSupremo && !temSupremo) continue;
			if (d.PedeKaioshin && !ehKaioshin) continue;
			if (d.PedeSangueSaiyajin && sangueSaiyajin < SangueSaiyajinMinimo) continue;

			yield return d;
		}
	}

	// =====================================================================
	// O MENU DO SUPER SHENRON -- `ProceduralSpace.dm:1597-1599`
	// =====================================================================
	/// <summary>
	/// **OS DESEJOS DO SUPER SHENRON.** Sao UM por invocacao, e a lista e outra: quatro entradas, tres
	/// delas sempre.
	///
	/// ============================ ELES NAO SAO UM RECORTE DA TABELA DO SHENRON ============================
	/// O Super Shenron **nao tem** Imortalidade, Super Saiyajin, Marcos, Inteligencia, Tecnologia,
	/// Juventude, Curar Planeta, Matar, Ressuscitar Todos, Alma, Magia nem Calcinha -- e nao tem escada
	/// de `TrueWishPower` nenhuma. O portao dele nao e PODER, e a LINGUA.
	/// ================================================================================================
	/// </summary>
	public static readonly DesejoDef[] DoSuper =
	[
		new() { Id = "sdb_riqueza", Dm = "Riqueza colossal", Linha = "ProceduralSpace.dm:1638-1641",
				Nome = "Riqueza colossal (5.000.000 zeni)",
				Desc = "Cinco milhões de zeni -- pro BENEFICIÁRIO, se houver um." },

		new() { Id = "sdb_reviver", Dm = "Reviver um guerreiro caido", Linha = "ProceduralSpace.dm:1606-1618",
				Nome = "Reviver um guerreiro caído", Alvo = AlvoDoDesejo.Morto,
				Desc = "Traz um morto de volta. Nem o Super Shenron reverte a morte de velhice." },

		new() { Id = "sdb_supremo", Dm = "Strongest in the Universe", Linha = "ProceduralSpace.dm:1631-1637",
				Nome = "O Mais Forte do Universo (a VIDA por poder)",
				Desc = "O DOBRO do maior poder do jogo, e UM ANO de vida. Quem PAGA precisa aceitar em pessoa." },

		new() { Id = "sdb_corpo_saiyajin", Dm = "Corpo de um Saiyajin (Zero Mortals)",
				Linha = "ProceduralSpace.dm:1619-1630",
				Nome = "Corpo de um Saiyajin (Zero Mortals)",
				PedeKaioshin = true, Alvo = AlvoDoDesejo.Saiyajin,
				Desc = "O desejo do Zamasu. PRIVILÉGIO PRÓPRIO: some do menu enquanto houver procuração." },
	];

	/// <summary>
	/// O MENU DO SUPER SHENRON -- `ProceduralSpace.dm:1597-1598`.
	///
	/// O "Corpo de um Saiyajin" some quando ha PROCURACAO, e o motivo esta escrito no proprio DM
	/// (`if(kai_body_wish_ok(U) && !sdb_benef_sig)`, :1598): *"privilegio proprio do Kaioshin (nao vai
	/// pra beneficiario)"*. E uma das cinco travas que impedem o procurador de virar o desejo pra si --
	/// sem ela, um Kaioshin aceitaria a guarda de qualquer um e trocaria de corpo com o desejo alheio.
	/// </summary>
	public static IEnumerable<DesejoDef> MenuDoSuper(bool ehKaioshin, bool haProcuracao)
	{
		foreach (DesejoDef d in DoSuper)
		{
			if (d.PedeKaioshin && (!ehKaioshin || haProcuracao)) continue;
			yield return d;
		}
	}
}
