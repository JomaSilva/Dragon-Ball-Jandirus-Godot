using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// O SIGILO: o que o servidor NAO conta.
///
/// DUAS REGRAS, e as duas sao DE JOGO -- nao sao de interface:
///
///   1. O PODER DE LUTA E SEGREDO. Sem scouter ninguem le BP em numero -- nem o dos outros,
///      nem o PROPRIO. No BYOND isso e o `mob/var/scouteron` (Inventory.dm:155), aceso pelo
///      verbo Equip do scouter (Modules/Tech/Tier 1.5.dm:355-357) e pelo androide de scouter
///      embutido (Modules/Tech/Cyborgs.dm:930). TODA leitura de BP passa por ele:
///        HUD.dm:28           `if(!usr.scouteron) bptext = "???"`   (o proprio BP no HUD)
///        Statistics.dm:90-91 "Battle Power" real, senao `"???   (base ???)"`
///        Statistics.dm:104   o teto de BP vira "???"
///        HtmlUI.dm:757-758   a barra de BP do HUD em HTML
///        HtmlUI.dm:402-404   a aba Scan so existe com o scouter ligado
///        attack_proc.dm:90   ate a "Punch Lift Calculation", que e um numero DERIVADO do BP
///        Dragonballs.dm:64   ate o anuncio publico do upgrade
///      O QUE NAO SE ESCONDE, e o DM e explicito nisso: os MULTIPLICADORES. "x2,5 de forma",
///      "netBuff 80%", "voce ficou 30% mais forte" sao RELATIVOS -- nao revelam o absoluto, e
///      sao a unica leitura de progresso que sobra pra quem nao tem aparelho.
///
///   2. A CLASSE NUNCA APARECE. Em lugar nenhum, nem com scouter. No DM inteiro `Class` so e
///      LIDA (`if(Class == "Legendary")`, HtmlUI.dm:436-438), nunca IMPRESSA -- nem a tela de
///      selecao de personagem mostra (characterselect.dm:62-69 desenha so nome e slot). A
///      unica pista que o jogador recebe e UMA frase no chat, na criacao: o `class_hint()` de
///      Modules/Character Customization/ClassHint.dm, portado no fim deste arquivo.
///
/// POR QUE ISTO MORA NO SERVIDOR E NAO NA TELA: esconder na interface esconde o ROTULO. Quem
/// abrir o pacote ve o numero do mesmo jeito, e o pacote de ficha voa varias vezes por segundo
/// por jogador. Se o cliente nao pode ver, o servidor NAO MANDA -- e o que ele manda no lugar e
/// AUSENCIA DE DADO (<see cref="SemLeitura"/>), nunca a string "???": "???" e desenho, e desenho
/// e problema de quem desenha.
///
/// ESTE ARQUIVO E SO A DECISAO. Os pontos de ligacao moram em arquivos de outro agente e estao
/// listados um a um no relatorio -- enquanto eles nao chamarem daqui, o corte nao acontece.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// AUSENCIA DE LEITURA. Vai no lugar do BP quando quem recebe nao tem como ler poder.
	///
	/// NaN e a escolha certa e nao um truque: e literalmente "isto nao e um numero". Ele
	/// atravessa o `w.Put(double)` que ja existe (nenhum byte a mais, nenhuma mudanca de
	/// formato de pacote), e do outro lado NAO HA numero nenhum pra um cliente modificado
	/// desenhar. Mandar zero seria mentira -- zero e um BP possivel; mandar "???" seria mandar
	/// texto de interface pela rede.
	///
	/// O cliente reconhece com `double.IsNaN(...)` e desenha "???".
	/// </summary>
	public const double SemLeitura = double.NaN;

	/// <summary>
	/// TEM SCOUTER LIGADO? E o `usr.scouteron` do BYOND, e e a UNICA porta de leitura de BP.
	///
	/// Hoje o bit nunca acende -- o port ainda nao tem o item scouter, entao o mundo inteiro le
	/// "???", que e exatamente a regra pedida. Quando o scouter chegar (item equipavel, ou o
	/// androide de olho embutido do Cyborgs.dm:930), quem o equipar acende
	/// <see cref="Protocol.Poder.Scouter"/> em <c>pl.Poderes</c> e tudo aqui passa a valer sem
	/// mais nenhuma mudanca.
	/// </summary>
	public static bool TemScouter(ServerPlayer pl) => (pl.Poderes & Protocol.Poder.Scouter) != 0;

	/// <summary>
	/// O QUE O CLIENTE PODE SABER QUE SABE FAZER. Uma porta so pros bits de
	/// <see cref="Protocol.Poder"/> que saem no pacote de atributos.
	///
	/// O Nav entra aqui porque so existe NO ESPACO: em terra firme um mapa estelar nao serve
	/// pra nada e a aba apareceria vazia. O scouter ja vem de <c>pl.Poderes</c>; esta funcao
	/// existe pra que exista UM lugar onde se decide o que a interface do outro lado tem
	/// direito de montar -- e o mesmo bit que faz a aba Sense virar Scan (HtmlUI.dm:402-404).
	/// </summary>
	public static Protocol.Poder PoderesVisiveis(ServerPlayer pl)
	{
		// O NAV VALE EM TERRA TAMBEM, desde que ele virou CARTA ESTELAR.
		//
		// A regra anterior era "so no espaco, porque em terra firme um mapa estelar nao serve pra
		// nada e a aba apareceria vazia -- e aba vazia ensina que o sistema nao funciona". O
		// raciocinio estava certo pro que a aba ERA: uma lista dos corpos celestes das tres chunks
		// em volta, que em terra e sempre vazia.
		//
		// Agora ela desenha a galaxia inteira, e consultar a carta ANTES de decolar e metade do uso
		// de uma carta. O que continua valendo so no espaco e VIAJAR -- e disso quem cuida e o
		// botao, que aparece apagado com o motivo escrito nele.
		return pl.Poderes | Protocol.Poder.Nav;
	}

	/// <summary>
	/// A FICHA COMO ELA DEVE SAIR NO FIO. Substitui `pl.Sheet()` nos dois envios de
	/// <see cref="Protocol.S2C.Stats"/> (o do JoinAccepted e o do tick).
	///
	/// Corta DUAS coisas:
	///   - o BP e o BP expresso viram <see cref="SemLeitura"/> pra quem nao tem scouter;
	///   - a CLASSE sai SEMPRE, com scouter ou sem: ela nao e informacao escondida, e
	///     informacao que nao existe pro jogador.
	///
	/// O resto da ficha continua inteiro: vida, Ki, cadencia do soco, membros ruins e o byte
	/// de estado. Nada disso e poder de luta -- um corpo machucado se ve, e o proprio jogo
	/// mostra a barra de vida de todo mundo no snapshot.
	/// </summary>
	public static SheetState FichaVisivel(ServerPlayer pl)
	{
		SheetState s = pl.Sheet();
		if (!TemScouter(pl))
		{
			s.BP = SemLeitura;
			s.ExpressedBP = SemLeitura;
		}
		s.Class = "";
		return s;
	}

	/// <summary>
	/// O RESUMO DE UM SLOT, na tela de selecao de personagem.
	///
	/// Ali nao ha scouter nenhum -- nem ha personagem em jogo pra ter um. O DM desenha so nome
	/// e numero do slot (characterselect.dm:62-69); o BP e a classe que o port acrescentou sao
	/// invencao, e sao o vazamento mais barato que existe: basta abrir a tela de login pra ler
	/// a ficha inteira de tres personagens.
	/// </summary>
	public static SlotInfo SlotVisivel(SlotInfo s)
	{
		s.BP = SemLeitura;
		s.Classe = "";
		return s;
	}

	/// <summary>
	/// UM NUMERO DE BP DENTRO DE UMA FRASE, pros avisos que o servidor escreve no chat.
	///
	/// E o mesmo molde do DM, que repete `[usr.scouteron ? "[BP]" : "???"]` em cada mensagem
	/// que carrega poder (Dragonballs.dm:64, attack_proc.dm:90, MasterStudent.dm:577). Aqui a
	/// pergunta e sempre "quem esta LENDO", nunca "de quem e o numero": o scouter e do olho de
	/// quem le, entao ate o proprio BP passa por este funil.
	/// </summary>
	public static string BpEmTexto(ServerPlayer quemLe, double bp) =>
		TemScouter(quemLe) ? bp.ToString("N0") : "???";

	// =====================================================================
	// A DICA DE CLASSE -- a unica pista, e ela e uma frase
	// =====================================================================

	/// <summary>
	/// Quanto tempo depois de nascer a dica chega. `sleep(15)` = 1,5 s em ClassHint.dm:15.
	///
	/// O atraso e o conserto de um bug do proprio DM: a dica disparava no MEIO das telas de
	/// criacao e ninguem via (o comentario de ClassHint.dm:7-9 conta a historia). Aqui ela cai
	/// depois das mensagens de entrada, quando o chat ja parou de rolar.
	/// </summary>
	private const double AtrasoDaDica = 1.5;

	/// <summary>
	/// A DICA DE CLASSE: uma frase, uma vez, na criacao do personagem.
	///
	/// Porta `mob/proc/class_hint()` (ClassHint.dm:5-99). Chamar UMA vez, quando o personagem e
	/// CRIADO -- nao a cada login: a graca e ser a primeira coisa que se sabe sobre o proprio
	/// sangue, e uma frase repetida toda entrada vira ruido.
	/// </summary>
	public void DicaDeClasse(ServerPlayer pl)
	{
		string frase = FraseDaClasse(pl.Class, pl.Race);
		int id = pl.Id;

		SceneTree? arv = IsInsideTree() ? GetTree() : null;
		if (arv == null) { Avisar(pl, frase); return; }   // servidor fora da arvore: manda na hora

		SceneTreeTimer t = arv.CreateTimer(AtrasoDaDica);
		t.Timeout += () =>
		{
			// o jogador pode ter caido no meio do 1,5 s -- procura pelo id em vez de segurar o
			// objeto, senao a dica ressuscitaria uma referencia morta
			if (_players.TryGetValue(id, out ServerPlayer? p)) Avisar(p, frase);
		};
	}

	/// <summary>
	/// A frase de cada classe, uma a uma, de ClassHint.dm:19-98.
	///
	/// A REGRA DAS FRASES: sugerem o ANDAR em que o personagem nasceu sem dizer o nome da
	/// classe e sem citar numero. "Um poder colossal dorme no seu sangue" e "voce comeca com
	/// pouco" mandam o jogador jogar de jeitos diferentes, que e tudo que a dica precisa fazer.
	///
	/// A RACA DESEMPATA duas frases que o DM compartilha entre racas ("Normal" e "Low-Class"),
	/// exatamente como ClassHint.dm:28-38 faz com Race/Parent_Race.
	///
	/// Texto do jogador vai em portugues e COM acento; o DM escreve em ingles so porque o jogo
	/// dele e em ingles.
	/// </summary>
	private static string FraseDaClasse(string classe, string raca) => classe switch
	{
		// --- Saiyajin ---
		"Legendary" or "Legendary Primal Saiyan" =>
			"Um poder colossal e mal contido dorme no seu sangue. Até os seus tremeriam diante do que você pode vir a ser.",
		"Elite" =>
			"Sangue de elite corre nas suas veias. Você nasceu muito acima dos guerreiros comuns do seu povo.",
		"Normal Primal Saiyan" =>
			"Você é um filho legítimo dos velhos costumes, um guerreiro comum da sua gente, com o poder selvagem da lua ainda adormecido dentro de você.",
		"Low-Class" => raca == "Heran"
			? "Você nasceu entre os mais baixos do seu povo, e ainda assim algo em você cresce mais rápido que nos outros."
			: "Você nasceu um guerreiro de classe baixa, de poder humilde, mas cada roçar na morte só te deixa mais forte.",
		"Normal" => raca switch
		{
			"Saiyan" =>
				"Você é um guerreiro mediano do seu povo, nem fraco nem notável, com tudo ainda por provar.",
			// Kai comum: o destino do Sr. Kaioh
			"Kai" =>
				"Você nasceu do fruto comum da árvore sagrada. Uma vida quieta, vigiando o cosmos como os velhos Kaioh do Norte, parece te esperar.",
			_ =>
				"Você é uma pessoa comum da sua espécie. Começa com pouco, mas a sua vontade não tem teto.",
		},

		// --- Humano ---
		"Ancient Hermit" =>
			"Seu corpo é frágil, mas a sua mente e o seu domínio sobre o ki são os de um verdadeiro sábio ancião.",
		"Peak Human" =>
			"Você está no ápice absoluto do potencial humano. O corpo que você lapidou é a sua maior arma.",
		"Triclops Descendant" =>
			"Corre em você o sangue de uma linhagem veloz e técnica, herdeira dos guerreiros de três olhos.",

		// --- Meio-Saiyajin ---
		"New Generation" =>
			"Você é da nova geração mestiça: equilibrado, carregando o melhor de dois mundos.",
		"Future Lineage" =>
			"Você carrega a linhagem de um futuro sombrio: força física bruta, presa a uma única forma concentrada.",
		"Prodigial" =>
			"Um potencial imenso e escondido dorme em você, esperando o momento em que for destravado.",

		// --- Kaioshin ---
		"Golden Apple" =>
			"Você nasceu do fruto DOURADO da árvore sagrada. O próprio céu parece se curvar sobre o seu berço; um assento muito acima dos Kaioh comuns pode um dia ser seu.",

		// --- Majin ---
		"Majin" =>
			"Sua carne elástica remenda tudo que arrancarem dela. Você é a resistência em pessoa.",
		"Corrupted Majin" =>
			"Algo instável e maligno apodrece dentro de você: um poder ofensivo muito além de um Majin comum.",

		// --- Icer / Frost Demon ---
		"Frost Demon" =>
			"Você pertence à realeza fria dos Demônios do Gelo: nobre, contido e equilibrado.",
		"Mutant Frost Demon" =>
			"Uma mutação rara fez de você um monstro. Seu poder bruto faz o da sua própria espécie parecer pequeno.",

		// --- Namekuseijin ---
		"Warrior clan" =>
			"Você é do clã guerreiro. Trocou a cura por força bruta de combate.",
		"Demon clan" =>
			"O sangue escuro do clã demoníaco corre em você, um conjurador agressivo da linhagem do velho rei.",
		"Dragon clan" =>
			"Você é do clã do dragão: frágil numa briga, mas abençoado com regeneração e potencial sem igual.",

		// --- Bio-Androide ---
		"Majin-Type" =>
			"Seu projeto biológico prefere regeneração e força bruta à evolução sem fim.",

		// --- Semideus ---
		"Demigod" =>
			"Sangue divino corre em você: equilibrado, e ainda assim com um dos maiores potenciais de poder que existem.",
		"Ogre" =>
			"Você é uma parede de músculo, força física esmagadora em forma de gente.",
		"Genie" =>
			"Você é um guardião defensivo: difícil de ferir, mas não é de golpe pesado.",

		// --- Gray ---
		"Gray" =>
			"Sua força vem da meditação e do músculo que incha com ela: um guerreiro do silêncio.",
		"Hermano" =>
			"Seu intelecto alimenta o seu poder. Quanto mais sábio você fica, mais forte você é.",

		// --- Heran ---
		"Epsilon" =>
			"Você é um Heran padrão: equilibrado, sem extremos.",
		"Omega" =>
			"Um golpe raro de sorte fez de você uma elite entre os Herans, com dons excepcionais.",

		// racas de classe unica (e o Bio-Androide tipo Cell, que e o default do DM)
		_ => raca is "BioAndroid" or "Bio-Android"
			? "Você é uma forma de vida perfeita em construção. Absorve, regenera e evolui sem limite."
			: "Você olha pra dentro e mede o próprio poder, a natureza com que nasceu.",
	};
}
