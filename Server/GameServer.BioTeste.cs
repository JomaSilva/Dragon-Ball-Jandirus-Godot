using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Items;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ BANCADA: O ANDROIDE E O BIO-ANDROIDE, DE PONTA A PONTA ============================
/// `--bioteste`. A auditoria deste sistema terminou com uma frase que e o motivo desta bancada
/// existir: *"nao ha caminho de bancada que dirija construir -> lab_bio -> colher_dna -> gestar de
/// ponta a ponta. Enquanto nao houver, nenhum conserto pode ser provado -- so lido."*
///
/// ============================ ELA DIRIGE OS **VERBOS**, E NAO AS FUNCOES ============================
/// Toda linha abaixo entra pelo `ComandoDeTech` ou pelo `UsarHabilidade` -- os dois canais unicos
/// que o cliente usa. Chamar `NascerBioAndroide` direto provaria que o metodo funciona e nao que
/// **um jogador chega nele jogando**, que e a pergunta que esta sessao inteira existe pra responder.
/// Foi exatamente esse o cego que deixou passar seis dados extraidos-sem-consumidor neste porte.
///
/// ============================ E ELA NAO USA O CORPO DO HOST ============================
/// O cientista e um NPC forjado com o `Peer` do host EMPRESTADO -- porque o nascimento exige o
/// criador online (`TickDaGestacao`) e porque virar bio-androide DESTROI a persona: nome, raca,
/// genoma, classe, livro de skills e aparencia. Fazer isso com o personagem de quem roda a bancada
/// seria apagar a conta dele pra provar um ponto. O `Persistir` e inofensivo nele: `Slot = -1`
/// devolve na primeira linha.
///
/// TUDO O QUE ELA TOCA VOLTA no `finally`: os corpos forjados saem do mundo, o laboratorio sai do
/// `_noChao`, o `mundo.json` e regravado e o `Peer` emprestado e devolvido ANTES da limpeza (corpo
/// com dono na tela nao pode ser removido -- `GameServer.Npc.cs:267`).
///
/// COMO RODAR:
///   "Dragon ball Jandirus.exe" --headless -- --servidor --rede 7956 --bioteste
///                      --conta bancada_bio --nome QuemViraBicho
/// ================================================================================================================
/// </summary>
public partial class GameServer
{
	private bool _bioDeTeste;

	/// <summary>Faixa de lugares propria -- longe da dos habitantes, das sagas e da bancada de gente.</summary>
	private ulong _lugarDaBancadaDeBio = 8_400_000;

	/// <summary>
	/// UM CANTO DO MAPA POR FAMILIA que precise de LABORATORIO -- ver o `palco` de
	/// <see cref="ForjarCorpo"/>. Dez tiles entre um e outro: `ObraPerto` tem alcance de 64 px.
	/// </summary>
	private static readonly Vec2 PalcoDoCrivo = new(320, 0);
	private static readonly Vec2 PalcoDoSemDna = new(640, 0);
	private static readonly Vec2 PalcoDaInjecao = new(960, 0);

	/// <summary>
	/// Os TRES cantos da familia 14 -- um bio por raca que o dono nomeou (Majin, pelo PAI do doador,
	/// e Frost Demon, pela raca do doador), mais o que nao herda nada.
	/// </summary>
	private static readonly Vec2 PalcoDoFolego = new(1280, 0);
	private static readonly Vec2 PalcoDoSufoco = new(1600, 0);
	private static readonly Vec2 PalcoDoFrio = new(1920, 0);

	/// <summary>(v0.3.0) O canto da familia 16: o tanque com DOIS doadores -- um Majin e um androide perpetuo.</summary>
	private static readonly Vec2 PalcoDoSangue = new(2240, 0);

	// =====================================================================
	// AS TRES SKILLS COM NOME -- ver o bloco do segundo doador em `MedirONascimento`
	// =====================================================================
	/// <summary>A tecnica que vem DA PESSOA: "Solar Flare", sem gate de raca nenhum.</summary>
	private const string SkillDaPessoa = "/datum/skill/ki/Solar_Flare";
	private const string VerboDaPessoa = "Solar_Flare";

	/// <summary>
	/// A habilidade que vem DA RACA: "Fusion- Namek Style". Ela pendura no `tree/namek` e em
	/// arvore nenhuma alem dela -- entao um bio-androide que a tenha so pode te-la recebido pelo DNA.
	/// </summary>
	private const string SkillDaRaca = "/datum/skill/namek/fusion";
	private const string VerboDaRaca = "Namekian_Fusion";

	/// <summary>
	/// O CONTROLE da heranca: "Stretchy Arms", do MESMO galho racial e **sem verbo**. No DM ela nao atravessava
	/// (`Keyableverbs` e uma lista de botoes, e nao o livro do doador); desde a v0.3.0 ela ATRAVESSA -- o dono: *"todas as
	/// skills da aba learn"* -- e e o controle da regra nova.
	/// </summary>
	private const string SkillPassivaDaRaca = "/datum/skill/namek/Stretchy_Arms";

	private void RodarBancadaDoBio(ServerPlayer host)
	{
		GD.Print("\n===== BANCADA: ANDROIDE E BIO-ANDROIDE =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		var forjados = new List<ServerPlayer>();
		var obras = new List<Obra>();
		double gestacaoGuardada = _gestacaoDeTeste;

		try
		{
			if (_moldes == null || _obras == null || _skills == null)
			{
				Checa("os catalogos carregaram (moldes, construcoes e skills)", false);
				return;
			}

			// A gestacao passa a valer UM segundo. (A larva amadurece ENTERRADA -- 5 min de relogio, que esta bancada anda pelo
			// `AdiantoDoRelogioDeTeste`: ver `GameServer.Larva.cs`.)
			_gestacaoDeTeste = 1;

			ServerPlayer? cientista = ForjarCorpo(host, "Human", "Dr. Bancada", forjados);
			if (cientista == null) { Checa("PRECONDICAO: o cientista forjado nasceu", false); return; }

			MedirAPortaDoLaboratorio(Checa, cientista, obras);
			ServerPlayer? bio = MedirONascimento(Checa, host, cientista, obras, forjados);
			if (bio == null) return;

			MedirALarva(Checa, bio);
			MedirAEscadaPorAbsorcao(Checa, host, bio, forjados);
			MedirAAbsorcaoDeCombate(Checa, host, bio, forjados);
			MedirAsFormasDoBio(Checa, bio);
			MedirALarvaQuandoQuiser(Checa, bio);
			MedirOSsj2PelaMorte(Checa, bio);
			MedirONucleo(Checa, host, forjados);
			MedirAsSkillsDoDna(Checa, host, forjados);
			MedirAsClassesDeAndroide(Checa, host, forjados, obras);
			MedirOLaboratorioDestruido(Checa, host, forjados, obras);

			// ============================ A SEGUNDA METADE: OS CONTRA-EXEMPLOS ============================
			// Tudo acima mede que a coisa ACONTECE. Nada acima mede que ela **nao acontece fora da
			// condicao** -- e uma escada aberta pra todo mundo passaria por todas aquelas linhas. As
			// familias abaixo sao os dois sentidos de cada regra, e a ultima e a bancada se cobrando.
			// ==========================================================================================
			MedirQuemNaoSeColhe(Checa, host, forjados, obras);
			ServerPlayer? semDna = MedirOBioSemDnaSaiyajin(Checa, host, forjados, obras);
			MedirAHeranca(Checa, bio, semDna);
			MedirOZenkai(Checa, host, bio, semDna, forjados);
			MedirOSuperSaiyajinPeloDna(Checa, bio, semDna);
			MedirOFolegoNoVacuoPeloDna(Checa, host, forjados, obras);
			MedirOSangueEOLivroDosDoadores(Checa, host, forjados, obras);
			MedirAsDuasAbsorcoesDoBioMajin(Checa, host, forjados);   // (2026-10-24) a 4c, em `GameServer.BioMajinTeste.cs`
			MedirOsNumerosContraODm(Checa);
			MedirAsInjecoesDeDefeito(Checa, host, bio, semDna, forjados, obras);

			Checa("a bancada chegou ao fim (ver o `catch`: sem ele, abortar no meio reportava '0 falhas')",
				  true);
		}
		catch (Exception ex) { Checa("a bancada rodou ate o fim sem excecao", false, ex.ToString()); }
		finally
		{
			_gestacaoDeTeste = gestacaoGuardada;

			// O `Peer` EMPRESTADO SAI PRIMEIRO -- ver o cabecalho: `RemoverNpc` recusa corpo com dono
			// na tela, e o robo ficaria no mundo pra sempre, invisivel pra limpeza.
			foreach (ServerPlayer p in forjados) p.Peer = null;
			foreach (ServerPlayer p in forjados) RemoverNpc(p);

			foreach (Obra o in obras) _noChao.Remove(o);
			if (obras.Count > 0) { GravarMundo(); AplicarColisaoDasObras(host.Zone); MandarObras(host.Zone); }

			GD.Print($"===== BIO: {ok} ok, {falhou} falha(s) =====\n");
		}
	}

	/// <summary>
	/// UM CORPO FORJADO COM O `Peer` DO HOST EMPRESTADO -- ou seja, um corpo que o crivo
	/// <see cref="EhJogador"/> aceita.
	///
	/// O EMPRESTIMO E A PARTE IMPORTANTE e nao um atalho: `ColherDna` so aceita JOGADOR (foi por isso
	/// que ele ganhou o crivo), e o nascimento so acontece com o criador online. Um NPC puro faria a
	/// bancada medir a recusa e chamar isso de sucesso.
	/// </summary>
	/// <param name="palco">
	/// ============================ CADA FAMILIA NO SEU CANTO DO MAPA, E ISSO E UM ACHADO ============================
	/// Sem este deslocamento a bancada nasceu VERMELHA na primeira rodada das familias novas, e o
	/// motivo nao tinha nada a ver com o bio: `LabDeBio` pergunta ao `ObraPerto`, que devolve a
	/// construcao MAIS PERTO -- e as familias 7 e 8 deixam dois `Android Lab` (Lab=1) de pe no mesmo
	/// ponto de spawn. O laboratorio novo era achado, o vizinho velho vinha primeiro, e a bancada
	/// reprovava "o laboratorio esta de pe" com o laboratorio de pe.
	///
	/// E o defeito nao e da producao: um jogador que erga dois mainframes colados vive a mesma coisa,
	/// e a resposta certa pra isso e ele ergue-los separados. Aqui e a bancada que precisava aprender.
	/// ==========================================================================================================
	/// </param>
	private ServerPlayer? ForjarCorpo(ServerPlayer host, string raca, string nome,
									  List<ServerPlayer> forjados, Vec2 palco = default)
	{
		ServerPlayer? p = NascerNpc("cidadao", host.Zone, host.Pos + new Vec2(8, 0),
									++_lugarDaBancadaDeBio);
		if (p == null) return null;
		if (palco.X != 0 || palco.Y != 0) p.Pos = host.Pos + palco;

		forjados.Add(p);
		p.Peer = host.Peer;
		p.Papel = null;          // sem papel de NPC: ele responde "sou jogador"

		// ============================ CADA CORPO COM CONTA PROPRIA -- E ISTO E UM ACHADO DA BANCADA ============================
		// Corpo forjado nasce com `Conta = ""`, e o `TickDaGestacao` procura o criador por
		// `p.Conta == g.DonoConta`. Com todos em branco, a primeira rodada desta bancada transformou
		// o **DOADOR** em bio-androide em vez do cientista -- e ela so soube disso porque afirma o
		// nome e a raca de quem nasceu, e nao "alguma coisa aconteceu".
		//
		// Nao e defeito de producao (jogador de verdade sempre tem conta), e sim a prova de que o
		// corpo forjado precisa PARECER com o de verdade nos campos que a regra le. `Slot` fica em
		// -1 de proposito: com ele >= 0 o `Persistir` gravaria este boneco DENTRO do slot do host --
		// a bancada apagaria o personagem de quem a roda.
		// ====================================================================================================================
		p.Conta = $"bancada-bio-{p.Id}";
		p.Name = nome;
		p.Race = raca;
		p.Ficha.Race = raca;
		p.Ficha.ParentRace = raca;
		p.Ficha.Statify();
		return p;
	}

	// =====================================================================
	// 1. A PORTA: mainframe -> laboratorio
	// =====================================================================
	private void MedirAPortaDoLaboratorio(Checagem Checa, ServerPlayer pl, List<Obra> obras)
	{
		GD.Print("--- 1. A PORTA DO LABORATORIO ---");

		var mainframe = new Obra
		{
			Id = 984_001, Tipo = "Android_Creation_Mainframe", Aparafusada = true,
			X = pl.Pos.X, Y = pl.Pos.Y, DonoConta = pl.Conta, DonoNome = pl.Name,
		};
		mainframe.PorZona(pl.Zone);
		_noChao.Add(mainframe);
		obras.Add(mainframe);

		// ---- o gate de tecnologia RECUSA antes de tudo ----
		pl.Ficha.techskill = 0;
		ComandoDeTech(pl, "lab_bio", "");
		Checa("com techskill 0 o mainframe NAO vira laboratorio (o gate `DNL_INT_REQ` 70 vale)",
			  mainframe.Lab == 0, $"Lab={mainframe.Lab}");

		// ---- e a raca tambem ----
		pl.Ficha.techskill = 99;
		pl.Race = "Saiyan";
		ComandoDeTech(pl, "lab_bio", "");
		Checa("um Saiyajin com tecnologia de sobra NAO instala o laboratorio (a maquina e humana)",
			  mainframe.Lab == 0, $"Lab={mainframe.Lab}");

		// ---- humano + tecnologia: passa ----
		pl.Race = "Human";
		pl.Ficha.Race = "Human";
		ComandoDeTech(pl, "lab_bio", "");
		Checa("Humano com techskill 70+ instala o Bio-Android Lab pelo VERBO da tecla E",
			  mainframe.Lab == 2, $"Lab={mainframe.Lab}");
		Checa("...e o laboratorio responde ao `LabDeBio` (o alcance e o dono batem)",
			  LabDeBio(pl) != null);

		// ---- (2026-10-21) o desenho: o Bio-Android Lab vira o Cell Pod, e o Android Lab continua o do catalogo (`ArteDoLaboratorio`) ----
		// (os caminhos estao escritos aqui de novo, e nao lidos das constantes; o Android Lab e uma copia do mesmo mainframe com o laboratorio 1)
		Jandirus.Core.Tech.Construcao? doCatalogo = _obras?.Get(mainframe.Tipo);
		var doBio = ArteDaObra(mainframe, doCatalogo);
		var doAndroide = ArteDaObra(new Obra { Tipo = mainframe.Tipo, Aparafusada = true, Lab = 1 }, doCatalogo);
		Checa("O BIO-ANDROID LAB VIRA O CELL POD no pacote das construcoes (a folha, a animacao `default` e o -8 que centra o pod de 48 px no "
			  + "tile) -- e o Android Lab continua no desenho padrao do mainframe (`64x64tech`, `android_pod`)",
			  doBio is ("res://Assets/Sprites/DU/Items/Cell Pod.tres", "default", -8f, 0f)
			  && doAndroide is ("res://Assets/Sprites/Misc/Objects/Technology/64x64tech.tres", "android_pod", 0f, 0f),
			  $"bio {doBio}; androide {doAndroide}");
		Jandirus.Core.Tech.ArteDoLaboratorio.LabSemCellPodDeTeste = true;
		(string Arte, string Estado, float PixelX, float PixelY) semPod;
		try { semPod = ArteDaObra(mainframe, doCatalogo); }
		finally { Jandirus.Core.Tech.ArteDoLaboratorio.LabSemCellPodDeTeste = false; }
		Checa("CONTRA-EXEMPLO (defeito injetado: nenhum laboratorio troca o desenho): o Bio-Android Lab sai no desenho padrao -- a regua do "
			  + "Cell Pod REPROVA", semPod is not ("res://Assets/Sprites/DU/Items/Cell Pod.tres", "default", -8f, 0f), $"{semPod}");
	}

	// =====================================================================
	// 2. DNA -> GESTACAO -> NASCIMENTO
	// =====================================================================
	/// <summary>
	/// A CADEIA INTEIRA, e ela termina com uma CRIATURA EXISTINDO -- que e o buraco numero 1 da
	/// auditoria (*"a gestacao termina, o tanque some, o criador morre -- e nada nasce"*).
	/// </summary>
	private ServerPlayer? MedirONascimento(Checagem Checa, ServerPlayer host, ServerPlayer pl,
										   List<Obra> obras, List<ServerPlayer> forjados)
	{
		GD.Print("--- 2. DNA, GESTACAO E NASCIMENTO ---");
		Obra lab = obras[0];

		// ============================ O DOADOR E O **HOST**, E NAO UM BONECO ============================
		// Uma das medidas desta secao e a que mais importa pra mecanica -- *"o `brew_strongest_bp` le
		// o BP de HOJE do doador online"* --, e ela so existe se o doador tiver ASSINATURA: e por ela
		// que a fornada reencontra a pessoa (`Amostra.Assinatura`). Assinatura e `Conta + Slot`, e
		// corpo forjado tem `Slot = -1` de proposito (ver `ForjarCorpo`: com slot de verdade o
		// `Persistir` gravaria o boneco por cima do personagem de quem roda a bancada).
		//
		// Entao o doador e quem tem conta de verdade: o proprio host. Raca, BP, KO e livro sao
		// devolvidos logo abaixo -- ele emprestou o sangue, nao o personagem.
		// =============================================================================================
		ServerPlayer doador = host;
		string racaGuardada = doador.Race, racaFichaGuardada = doador.Ficha.Race;
		double bpGuardado = doador.Ficha.BP;
		bool koGuardado = doador.Ficha.KO;

		doador.Race = "Saiyan";
		doador.Ficha.Race = "Saiyan";
		Checa("PRECONDICAO: o doador tem ASSINATURA (senao a regra do BP atual seria vacua)",
			  doador.Assinatura.Length > 0);

		// ============================ A TECNICA TEM NOME, E ISSO NAO E ENFEITE DE RELATORIO ============================
		// Ela era `Todas.FirstOrDefault(tem verbo)` -- qualquer uma. Uma bancada assim responde "alguma
		// skill atravessou", e a pergunta do dono e outra: *"nomeie uma skill que veio da PESSOA"*. Com
		// a skill anonima, o dia em que a heranca copiasse a lista ERRADA (a do proprio bio, por
		// exemplo, que ja nasce com marcos) daria o mesmo verde -- porque "alguma" tambem seria
		// verdade. Nomeada, a linha do relatorio vira uma frase que se le sem o codigo do lado.
		// ==========================================================================================================
		Jandirus.Core.Skills.Skill? comVerb = _skills!.Get(SkillDaPessoa);
		Checa($"PRECONDICAO: a tecnica da PESSOA existe no catalogo (`{SkillDaPessoa}`, o verbo "
			  + $"`{VerboDaPessoa}`) -- sem ela a heranca de tecnica seria vacua",
			  comVerb != null && comVerb.Verbos.Contains(VerboDaPessoa));
		bool skillEmprestada = comVerb != null && !doador.Livro.Sabe(comVerb.Path);
		if (skillEmprestada) doador.Livro.Dar(comVerb!.Path);
		doador.Ficha.BP = 4_000_000;

		// ---- de pe, nao se colhe ----
		doador.Ficha.KO = false;
		ComandoDeTech(pl, "colher_dna", "");
		Checa("NAO se colhe DNA de quem esta DE PE (o preco do bio-androide e derrubar alguem)",
			  lab.Fornada == null || lab.Fornada.Amostras.Count == 0);

		// ---- nocauteado, se colhe -- e a amostra vem INTEIRA ----
		doador.Ficha.KO = true;
		ComandoDeTech(pl, "colher_dna", "");
		Amostra? a = lab.Fornada?.Amostras.FirstOrDefault();
		Checa("nocauteado, a agulha pega", a != null);
		if (a == null) return null;

		Checa("a amostra guarda a RACA do doador", a.Raca == "Saiyan", a.Raca);
		Checa("...o NOME dele", a.Doador == doador.Name, a.Doador);
		Checa("...a ASSINATURA dele (e por ela que a fornada o reencontra online)",
			  a.Assinatura == doador.Assinatura, a.Assinatura);
		Checa("...o BP dele", Math.Abs(a.Bp - 4_000_000) < 1, $"{a.Bp}");
		Checa("...e as TECNICAS dele (o `donor_verbs`, que ate aqui nao era nem coletado)",
			  comVerb == null || a.Skills.Contains(comVerb.Path),
			  $"{a.Skills.Count} skill(s)");

		// ---- nao da pra encher o tanque com a mesma pessoa ----
		ComandoDeTech(pl, "colher_dna", "");
		Checa("a MESMA pessoa nao enche o tanque sozinha (uma amostra por assinatura)",
			  lab.Fornada!.Amostras.Count == 1, $"{lab.Fornada.Amostras.Count} amostras");

		// ============================ O SEGUNDO DOADOR E UM NAMEKUSEIJIN, E ELE E A METADE "RACA" DO PEDIDO ============================
		// O dono pediu duas coisas na mesma frase -- *"pegar as SKILLS ... das PESSOAS e RACA q ele tem
		// o dna"* --, e ate aqui a bancada so media a primeira. A segunda precisa de um doador de OUTRA
		// raca que saiba uma habilidade que **so existe na arvore racial dele**, e ela precisa de um
		// par: uma que atravessa e uma que nao.
		//
		//   * `namek/fusion` (verb `Namekian_Fusion`) -- pendura no `tree/namek` e em lugar nenhum
		//     mais. Se ela aparecer no bio, ela so pode ter vindo do sangue do Namekuseijin.
		//   * `namek/Stretchy_Arms` -- do MESMO galho, e **sem verbo**. Ela era o controle do DM (a agulha copiava
		//     BOTAO, `Keyableverbs`); desde a v0.3.0 e o controle da regra do dono, ao contrario: *"todas as skills da
		//     aba learn"* -- a agulha leva o LIVRO inteiro, com botao ou sem.
		//
		// ELE ENTRA **DEPOIS** DA LINHA DE CIMA de proposito: aquela afirma que a mesma pessoa nao enche
		// o tanque sozinha, e ela conta amostras. Um segundo corpo caido no alcance antes dela mediria
		// outra coisa e ficaria verde por acaso.
		// ==========================================================================================================================
		ServerPlayer? namek = ForjarCorpo(host, "Namekian", "Doador Namekuseijin", forjados);
		if (namek != null)
		{
			namek.Ficha.BP = 1_000;            // fraco de proposito: quem manda no BP do filho e o host
			namek.Livro.Dar(SkillDaRaca);
			namek.Livro.Dar(SkillPassivaDaRaca);
			namek.Ficha.KO = true;
			ComandoDeTech(pl, "colher_dna", "");
			Checa("uma SEGUNDA pessoa, de outra raca, entra no mesmo tanque",
				  lab.Fornada!.Amostras.Count == 2, $"{lab.Fornada.Amostras.Count} amostras");

			Amostra? an = lab.Fornada.Amostras.LastOrDefault();
			Checa($"...e a amostra dele guarda a habilidade RACIAL com botao (`{VerboDaRaca}`)",
				  an != null && an.Skills.Contains(SkillDaRaca), $"{an?.Skills.Count} skill(s)");
			Checa("...e guarda TAMBEM a do mesmo galho que nao tem botao (`Stretchy Arms`) -- desde a v0.3.0 a agulha leva "
				  + "o LIVRO inteiro do doador (*\"todas as skills da aba learn\"*), e nao so os botoes (`Keyableverbs`) do DM",
				  an != null && an.Skills.Contains(SkillPassivaDaRaca));
			namek.Ficha.KO = false;
		}
		else Checa("PRECONDICAO: o doador Namekuseijin nasceu", false);

		// ---- o doador TREINA depois da coleta: e o BP de HOJE que conta ----
		doador.Ficha.BP = 10_000_000;
		doador.Ficha.KO = koGuardado;
		ComandoDeTech(pl, "gestar", "");
		Checa("a gestacao comecou", lab.Fornada.PrometidaEm > 0);
		Checa("o `brew_strongest_bp` le o BP ATUAL do doador online, e nao o congelado na agulha "
			  + "(colher cedo e esperar a pessoa treinar PAGA)",
			  Math.Abs(lab.Fornada.MaiorBp - 10_000_000) < 1, $"{lab.Fornada.MaiorBp:N0}");
		Checa("o `brew_has_saiyan` acendeu -- e ele era o SETIMO dado extraido-sem-consumidor deste "
			  + "porte (a lista de racas so virava linha de log)",
			  lab.Fornada.TemSaiyajin);
		Checa("o `brew_verbs` consolidou as tecnicas dos doadores",
			  comVerb == null || lab.Fornada.Tecnicas.Contains(comVerb.Path),
			  $"{lab.Fornada.Tecnicas.Count} tecnica(s)");

		// ---- criador OFFLINE nao pare ----
		NetPeer? peerGuardado = pl.Peer;
		lab.Fornada.PrometidaEm = NowMs() - 1;
		pl.Peer = null;
		TickDaGestacao();
		Checa("com o criador OFFLINE o tanque ESPERA em vez de abrir (deslogar nao escapa da "
			  + "sentenca -- o exploit que o parto implementado criaria)",
			  lab.Fornada != null && _noChao.Contains(lab));
		pl.Peer = peerGuardado;

		// ---- e agora nasce ----
		// O SANGUE VOLTA PRO DONO ANTES DO PARTO: o que a criatura leva ja esta na fornada
		// (consolidada no `gestar`), e o host tem que sair desta bancada como entrou.
		string nomeAntes = pl.Name;
		double bpEsperado = Math.Round(10_000_000 * BioAndroids.FracaoDoDoador);
		doador.Race = racaGuardada;
		doador.Ficha.Race = racaFichaGuardada;
		doador.Ficha.BP = bpGuardado;
		doador.Ficha.KO = koGuardado;
		if (skillEmprestada && comVerb != null) doador.Livro.Esquecer(comVerb.Path);
		TickDaGestacao();

		Checa("O BIO-ANDROIDE NASCEU -- e ele e o MESMO corpo, com outra persona (o `dnl_bio_hatch` "
			  + "opera sobre o proprio criador; nao ha slot novo a criar)",
			  BioAndroids.EhBio(pl.Race), $"raca='{pl.Race}'");
		Checa("...com o nome da criatura", pl.Name == $"Bio-Androide de {nomeAntes}", pl.Name);
		Checa("...nascendo LARVA", pl.Ficha.bio_stage == BioAndroids.Larva, $"{pl.Ficha.bio_stage}");
		Checa("...com METADE do BP do doador mais forte (`DNL_BIO_BP_SHARE`)",
			  Math.Abs(pl.Ficha.BP - bpEsperado) < 1, $"{pl.Ficha.BP:N0} != {bpEsperado:N0}");
		Checa("...com o genoma DESTRUIDO (nenhum stat de doador atravessa)", pl.Ficha.Genoma == null);
		Checa("...CARECA, e em toda forma (`HairObject.dm:168`)", pl.Visual.Cabelo == "Bald");
		Checa("...com o corpo de LARVA na aparencia (o `oicon` do DM, que viaja pela rede como "
			  + "indice de `Appearance.Corpo`)",
			  pl.Visual.Corpo == BioAndroids.IndiceDoCorpo(BioAndroids.Larva), $"{pl.Visual.Corpo}");
		Checa("...com a Ascensao ZERADA (`NoAscension`: o BPBoost do humano nao atravessa)",
			  Math.Abs(pl.Ficha.BPBoost - 1) < 1e-9, $"{pl.Ficha.BPBoost}");
		Checa("...com o `canSSJ` do DNA Saiyajin", pl.Ficha.canSSJ);
		Checa("...com o SSJ1 JA POSSUIDO, sem despertar por raiva (`hasssj = 1`)",
			  pl.Forma.Despertou("ssj1"));
		Checa("...e com a TECNICA do doador no livro",
			  comVerb == null || pl.Livro.Sabe(comVerb.Path));
		Checa("...marcada como ENSINADA -- ele SABE usar e nao sabe REPASSAR (decisao declarada: o "
			  + "laboratorio nao pode virar lavanderia de skill)",
			  comVerb == null || pl.Livro.FoiEnsinada(comVerb.Path));
		Checa("o tanque se rompeu junto (nao ha fornada pra nascer de novo no proximo tique)",
			  !_noChao.Contains(lab));

		// ---- ZENKAI: a linha que estava escrita e inalcancavel por causa do hifen ----
		// (v0.3.0) E AGORA E O SANGUE QUE DA: este bio tem um doador Saiyajin. O que nao tem esta na familia 12.
		Checa("O BIO-ANDROIDE COM SANGUE SAIYAJIN TEM ZENKAI -- e este ramo do `HasZenkai` era MORTO: ele testava "
			  + "\"Bio-Android\" com hifen e a raca do `races.json` e \"BioAndroid\" sem",
			  pl.Ficha.HasZenkai());

		// ---- as arvores raciais: a chave do `skilltrees.json` tambem e a grafia do DM ----
		Checa("...e ele recebe a ARVORE RACIAL dele (o `skilltrees.json` guarda a chave na grafia "
			  + "do DM; quatro racas ficavam sem arvore, em silencio)",
			  _skills!.ArvoresDe(pl.Race, "").Any(s => s.Path.Contains("bioandroid",
																	   StringComparison.OrdinalIgnoreCase)),
			  string.Join(", ", _skills.ArvoresDe(pl.Race, "").Select(s => s.Path)));
		return pl;
	}

	// =====================================================================
	// 3. A LARVA
	// =====================================================================
	private void MedirALarva(Checagem Checa, ServerPlayer bio)
	{
		GD.Print("--- 3. A LARVA ---");

		// (2026-10-17) A LARVA E SEMPRE RELAXADA -- o dono: *"ela fica sempre no modo 'relaxado' com 5 de bp e escondido do sense e
		// rastreadores"*. O poder expresso trava em 5, e nem a RAIVA nem o Ki alto furam a carapaca. (Era o teto de 1% do DM.) Duas
		// repercussoes: a trava do relaxado le o expresso do tique anterior (`Fighter.Power.cs`).
		bio.Ficha.angerBuff = 3;
		bio.Ficha.Ki = bio.Ficha.MaxKi;
		RepercutirPoder(bio);
		RepercutirPoder(bio);
		Checa("a LARVA e sempre RELAXADA: expressa 5 de poder, e nem a RAIVA fura a carapaca (o dono; era o teto de 1% do DM)",
			  bio.Ficha.expressedBP == 5 && PoderEmUso.Relaxado(bio.Ficha), $"{bio.Ficha.expressedBP:N0}");
		Checa("...e ninguem a sente: nem o Sense, nem o scouter, nem a telepatia (`TemEnergiaQueSeSente`, a porta do relaxado)",
			  !TemEnergiaQueSeSente(bio));
		bio.Ficha.angerBuff = 1;
		// ...e NADA a acorda: nem a raiva (o `RelaxCheck`) nem a carga -- as duas portas que soltam quem relaxou pelo botao. (O botao de ficar
		// pronto recusa no servidor, `PorQueNaoMexeNoPoder`; este corpo forjado nao tem redeas, e e por elas que o botao responde primeiro.)
		bio.Ficha.relaxado = true;
		double raiva = bio.Ficha.Anger;
		bio.Ficha.Anger = 1e9;
		bool acordou = PoderEmUso.ARaivaAcorda(bio.Ficha) || PoderEmUso.ACargaAcorda(bio.Ficha);
		bio.Ficha.Anger = raiva;
		Checa("...e nada a ACORDA: nem a furia nem a tecla de carregar soltam a larva (quem a solta e amadurecer)",
			  !acordou && bio.Ficha.relaxado && PoderEmUso.Relaxado(bio.Ficha));
		bio.Ficha.relaxado = false;

		// A larva nao absorve.
		bio.Ficha.bio_passos = 0;
		UsarHabilidade(bio, "absorver");
		Checa("a LARVA nao absorve (o dono: *\"A forma de barata do bio androide n consegue absorver\"*): a execucao nem abre, e a escada nao anda",
			  bio.Ficha.bio_passos == 0 && !_execucoesDoBio.ContainsKey(bio.Id));

		// Nem se transforma: a carapaca sela -- nem o Super Saiyajin que o DNA lhe deu.
		List<string> forma = Ouvir(() => Transformar(bio, subir: true));
		Checa("...nem se transforma: a carapaca SELA (nem o Super Saiyajin que o DNA lhe deu)", bio.Forma.NaBase && Disse(forma, "não se transforma"),
			  Ultimos(forma));
		Checa("...e VOA sem a skill de voo (o adendo: a larva voa)", PodeVoar(bio));

		// ---- o tempo na superficie nao rompe mais a carapaca (a gestacao desta bancada vale 1 s: era tambem o prazo da larva) ----
		double bpAntes = bio.Ficha.BP;
		AdiantoDoRelogioDeTeste += 3_000;
		TickDaLarva();
		Checa("o TEMPO na superficie nao rompe mais a carapaca (era o dia in-game do DM): so ENTERRADA (o adendo do dono)",
			  bio.Ficha.bio_stage == BioAndroids.Larva, $"{bio.Ficha.bio_stage}");

		// ---- enterrada ----
		List<string> cava = Ouvir(() => UsarHabilidade(bio, "enterrar"));
		Checa("ENTERRADA, ninguem a ve (o `Oculto` do snapshot) nem a alcanca (`Intocavel`); ela nao anda, e sai da grade de corpos (passam por cima)",
			  bio.EnterradoDesde >= 0 && EstaOculto(bio.Id) && bio.Combate!.Intocavel && !PodeMexerOCorpo(bio) && !EntraNaGrade(bio), Ultimos(cava));
		AdiantoDoRelogioDeTeste += 240_000;
		TickDaLarva();
		Checa("...aos 4 minutos ela ainda e larva", bio.Ficha.bio_stage == BioAndroids.Larva && bio.EnterradoDesde >= 0);
		Ouvir(() => UsarHabilidade(bio, "enterrar"));
		Checa("SAIR DA TERRA antes dos 5 min a devolve a superficie: vista, alcancavel e andando",
			  bio.EnterradoDesde < 0 && !EstaOculto(bio.Id) && !bio.Combate.Intocavel && PodeMexerOCorpo(bio) && EntraNaGrade(bio));
		Ouvir(() => UsarHabilidade(bio, "enterrar"));
		AdiantoDoRelogioDeTeste += 240_000;
		TickDaLarva();
		Checa("...e a conta ZERA: enterrada de novo, mais 4 min (8 ao todo) e ela ainda nao amadureceu", bio.Ficha.bio_stage == BioAndroids.Larva,
			  $"{bio.Ficha.bio_stage}");
		AdiantoDoRelogioDeTeste += 61_000;
		TickDaLarva();
		Checa("CINCO MINUTOS ENTERRADA SEM SAIR e a carapaca ROMPE: ela vira IMPERFEITO e sai da terra (o adendo: 5 minutos enterrada e ela "
			  + "amadurece)",
			  bio.Ficha.bio_stage == BioAndroids.Imperfeito && bio.EnterradoDesde < 0 && !EstaOculto(bio.Id), $"{bio.Ficha.bio_stage}");
		Checa("...sem mexer no BP BASE (o que ele ganha e a carapaca SAINDO, nao poder novo)",
			  Math.Abs(bio.Ficha.BP - bpAntes) < 1, $"{bio.Ficha.BP:N0} != {bpAntes:N0}");
		RepercutirPoder(bio);
		Checa("...e agora ele expressa o poder INTEIRO e volta a ser sentido (o selo morreu com a carapaca)",
			  bio.Ficha.expressedBP > bio.Ficha.BP * 0.5 && TemEnergiaQueSeSente(bio),
			  $"expresso {bio.Ficha.expressedBP:N0} contra base {bio.Ficha.BP:N0}");
		// O MARCO SE LE NO `bp_milestone_mult` E NAO NO `MultiplicadorDeGanho()` -- o segundo mostra
		// os fatores de AMBIENTE (gravidade, peso, zona) e o marco entra por outro cano, o
		// `BpGainBase()`. Medir o numero errado teria dado a resposta certa por acaso em metade dos
		// planetas.
		Checa("...e o MARCO `bio1` foi concedido -- `ReachMilestone` tinha UM chamador em todo o "
			  + "repo (a Ascensao), entao nenhum marco de forma existia em jogo",
			  Math.Abs(bio.Ficha.bp_milestone_mult - Milestones.Valor("bio1")) < 1e-9,
			  $"{bio.Ficha.bp_milestone_mult}");
		Checa("...e o CORPO acompanhou o degrau",
			  bio.Visual.Corpo == BioAndroids.IndiceDoCorpo(BioAndroids.Imperfeito));
	}

	/// <summary>
	/// (2026-10-17) 6b. O NUCLEO -- o adendo do dono: escolhido uma vez (bracos, pernas ou cabeca -- o tronco nao); reergue o corpo com 5% de tudo
	/// DUAS vezes, e a segunda detona e destroi o membro; com o membro dele ferido ele nao faz nada; a terceira morte e morte.
	/// </summary>
	private void MedirONucleo(Checagem Checa, ServerPlayer host, List<ServerPlayer> forjados)
	{
		GD.Print("--- 6b. O NUCLEO (o adendo do dono) ---");
		ServerPlayer? b = ForjarCorpo(host, BioAndroids.Raca, "Bio do nucleo", forjados);
		if (b == null) { Checa("PRECONDICAO: o bio do nucleo nasceu", false); return; }
		b.Ficha.bio_lab_born = true;
		b.Ficha.bio_stage = BioAndroids.Imperfeito;
		Checa("(antes) o bio de laboratorio sem nucleo tem o fato que acende os botoes da escolha", (FatosDe(b) & Jandirus.Core.Verbos.Fatos.NucleoPorEscolher) != 0);
		List<string> errado = Ouvir(() => UsarHabilidade(b, "nucleo:Torso"));
		Checa("o TRONCO nao e lugar de nucleo (o adendo: os bracos, as pernas e a cabeca)", b.Ficha.bio_nucleo.Length == 0 && Disse(errado, "escolha onde"),
			  Ultimos(errado));
		UsarHabilidade(b, "nucleo:Perna direita");
		Checa("ESCOLHIDO: o nucleo mora na perna direita -- e os botoes somem",
			  b.Ficha.bio_nucleo == "Perna direita" && (FatosDe(b) & Jandirus.Core.Verbos.Fatos.NucleoPorEscolher) == 0);
		List<string> deNovo = Ouvir(() => UsarHabilidade(b, "nucleo:Cabeca"));
		Checa("...e e PRA SEMPRE: escolher de novo e recusado", b.Ficha.bio_nucleo == "Perna direita" && Disse(deNovo, "já tem lugar"), Ultimos(deNovo));

		void Inteiro(ServerPlayer x)
		{
			x.Combate!.Reviver();
			x.Combate.Corpo.Restaurar();
			x.Combate.SincronizarVida();
			x.Ficha.Ki = x.Ficha.MaxKi;
			x.Ficha.stamina = x.Ficha.maxstamina;
		}

		// ---- a primeira morte ----
		Inteiro(b);
		List<string> primeira = Ouvir(() => b.Combate!.Morrer());
		Checa("A MORTE CHEGA E O NUCLEO A NEGA: o corpo volta NOCAUTEADO, com 5% em cada membro e 5% de Ki e de folego",
			  !b.Ficha.dead && b.Ficha.KO && b.Ficha.bio_nucleo_usos == 1
			  && b.Combate!.Corpo.Partes.Where(p => !p.Decepado).All(p => Math.Abs(p.Fracao - BioAndroids.RefilDoNucleo) < 0.005)
			  && Math.Abs(b.Ficha.Ki / b.Ficha.MaxKi - BioAndroids.RefilDoNucleo) < 0.005
			  && Math.Abs(b.Ficha.stamina / b.Ficha.maxstamina - BioAndroids.RefilDoNucleo) < 0.005,
			  $"morto {b.Ficha.dead}, KO {b.Ficha.KO}, usos {b.Ficha.bio_nucleo_usos} | {Ultimos(primeira)}");

		// ---- a segunda: ele reergue e DETONA ----
		Inteiro(b);
		List<string> segunda = Ouvir(() => b.Combate!.Morrer());
		Checa("A SEGUNDA vez ele reergue -- e DETONA: a perna direita (o lugar dele) e destruida",
			  !b.Ficha.dead && b.Ficha.bio_nucleo_usos == 2 && b.Combate!.Corpo.Achar("Perna direita") is { Decepado: true },
			  $"morto {b.Ficha.dead}, usos {b.Ficha.bio_nucleo_usos} | {Ultimos(segunda)}");

		// ---- a terceira e morte ----
		Inteiro(b);
		bool morreuDeVez = b.Combate!.Morrer();
		Checa("...e a TERCEIRA morte e MORTE: o nucleo ja detonou", morreuDeVez && b.Ficha.dead);

		// ---- com o membro do nucleo FERIDO ele nao faz nada ----
		ServerPlayer? b2 = ForjarCorpo(host, BioAndroids.Raca, "Bio do nucleo ferido", forjados);
		if (b2 != null)
		{
			b2.Ficha.bio_lab_born = true;
			b2.Ficha.bio_stage = BioAndroids.Imperfeito;
			UsarHabilidade(b2, "nucleo:Cabeca");
			Inteiro(b2);
			Jandirus.Core.Combat.BodyPart cabeca = b2.Combate!.Corpo.Achar("Cabeca")!;
			cabeca.Vida = cabeca.VidaMax * 0.1;   // QUEBRADA (abaixo de 20%)
			b2.Combate.SincronizarVida();
			bool morreu = false;
			List<string> ferido = Ouvir(() => morreu = b2.Combate.Morrer());
			Checa("COM O MEMBRO DO NUCLEO FERIDO (a cabeca quebrada) ele NAO reergue: a morte e morte (o adendo: desligado se o membro estiver ferido)",
				  morreu && b2.Ficha.dead && b2.Ficha.bio_nucleo_usos == 0 && Disse(ferido, "estava ferido"), Ultimos(ferido));
		}
	}

	/// <summary>
	/// (2026-10-17) 6c. AS SKILLS DO DNA -- o adendo do dono: *"o bio androide pode escolher dentre as raças de seu dna quais skills (passivas e
	/// ativas) ele quer, com limite de 6 skills"*. O bio de sangue Saiyajin + Humano + Namekuseijin escolhe pelo canal do botao; o que ele nao
	/// escolheu NAO vale; a escolha e uma por vez e pra sempre; a sexta fecha; a ficha lenta a leva pro cliente; e o Cell (NPC) sai com as seis
	/// dele. Contra-exemplo: `SkillsRaciais.BioComOSangueInteiroDeTeste` (a uniao automatica que o adendo trocou pela escolha).
	///
	/// O BIO DESTA FAMILIA NAO TEM O `Peer` EMPRESTADO E TEM SLOT: o kit racial e de PESSOA (`TemKitRacial` -- a assinatura pede conta E slot),
	/// e com o `Peer` do host o `Persistir` da escolha gravaria este boneco DENTRO da conta de quem roda a bancada. Sem `Peer`, ele devolve na
	/// primeira linha.
	/// </summary>
	private void MedirAsSkillsDoDna(Checagem Checa, ServerPlayer host, List<ServerPlayer> forjados)
	{
		GD.Print("--- 6c. AS SKILLS DO DNA (o adendo do dono) ---");
		ServerPlayer? b = ForjarCorpo(host, BioAndroids.Raca, "Bio do DNA", forjados);
		ServerPlayer? humano = ForjarCorpo(host, "Human", "Humano sem DNA", forjados);
		if (b == null || humano == null) { Checa("PRECONDICAO: o bio e o humano nasceram", false); return; }
		foreach (ServerPlayer x in new[] { b, humano })
		{
			x.Peer = null;
			x.Conta = x == b ? "bancada_bio_dna" : "bancada_bio_humano";
			x.Slot = 0;
		}
		Fighter f = b.Ficha;
		f.bio_lab_born = true;
		f.bio_stage = BioAndroids.Imperfeito;
		f.bio_racas_dna = ["Saiyan", "Human", "Namekian"];
		Jandirus.Core.Combat.CombatState c = b.Combate!;
		const Jandirus.Core.Verbos.Fatos Vaga = Jandirus.Core.Verbos.Fatos.SkillDoDnaPorEscolher;

		// ---- o sangue e a vaga ----
		SkillRacial sangue = SkillsRaciais.DoSangue(f);
		Checa("O SANGUE DA AS SKILLS DOS KITS DE RACA DAS DOADORAS: Saiyajin (5), Humano (4) e Namekuseijin (3) -- nenhuma do Frost Demon nem do "
			  + "meio-Saiyajin",
			  sangue == (SkillsRaciais.DoKit(KitRacial.Saiyajin) | SkillsRaciais.DoKit(KitRacial.Humano) | SkillsRaciais.DoKit(KitRacial.Namekuseijin))
			  && SkillsRaciais.Quantas(sangue) == 12, SkillsRaciais.Nomes(sangue));
		TickDasRaciais(b, 0.1);
		Checa("(antes) SOZINHO O BIO NAO TEM NADA: nenhuma skill vale antes da escolha -- e o fato da vaga acende os botoes dela",
			  b.Racial.Entrada.Skills == SkillRacial.Nenhuma && SkillsRaciais.AindaEscolhe(f) == SkillsRaciais.LimiteDoBio
			  && (FatosDe(b) & Vaga) != 0 && (FatosDe(humano) & Vaga) == 0, b.Racial.Entrada.Skills.ToString());

		// ---- as recusas ----
		List<string> deFora = Ouvir(() => UsarHabilidade(b, SkillsRaciais.PrefixoDaEscolha + SkillRacial.Controle));
		List<string> doHumano = Ouvir(() => UsarHabilidade(humano, SkillsRaciais.PrefixoDaEscolha + SkillRacial.RacaGuerreira));
		Checa("SO O QUE O SANGUE DA: o Perfect Control (Frost Demon) e recusado -- e quem nao e bio nao escolhe nada",
			  f.bio_skills == 0 && humano.Ficha.bio_skills == 0
			  && Disse(deFora, "não vem de nenhuma raça do seu sangue") && Disse(doHumano, "só o bio-androide"), $"{Ultimos(deFora)} || {Ultimos(doHumano)}");

		// ---- a escolha: uma passiva e uma ativa, pelo canal do botao ----
		List<string> escolheu = Ouvir(() =>
		{
			UsarHabilidade(b, SkillsRaciais.PrefixoDaEscolha + SkillRacial.RacaGuerreira);
			UsarHabilidade(b, SkillsRaciais.PrefixoDaEscolha + SkillRacial.SegundoFolego);
		});
		Checa("ESCOLHIDAS: Warrior Race e Second Wind entram na ficha, e o servidor diz o que ainda falta",
			  (SkillRacial)f.bio_skills == (SkillRacial.RacaGuerreira | SkillRacial.SegundoFolego) && Disse(escolheu, "desperta Warrior Race")
			  && Disse(escolheu, "Ainda dá pra escolher 4"), Ultimos(escolheu));

		// ---- o que vale: a Warrior Race acende, o Prideful (do mesmo sangue, NAO escolhido) nao ----
		// TODA PARTE, os orgaos de dentro tambem: a vida do corpo e a MEDIA delas (`Body.Vida`), e so os membros de fora a 10% deixavam a media
		// acima dos 20% da Warrior Race (a primeira rodada leu ataque x1).
		foreach (Jandirus.Core.Combat.BodyPart p in c.Corpo.Partes)
			if (!p.Decepado) p.Vida = p.VidaMax * 0.10;
		c.SincronizarVida();
		c.EmCombate = 60;
		f.relaxado = false;
		f.powerMod = 0.20;   // a barra a 20%: o Prideful, se valesse, estaria no degrau 3 (ataque e defesa x1,3)
		TickDasRaciais(b, 0.1);
		EfeitoRacial o = b.Racial.Efeito;
		bool soAEscolhida = Math.Abs(o.Physoff - Passivas.AtaqueDoGuerreiro) < 1e-9 && Math.Abs(o.Physdef - Passivas.DefesaDoGuerreiro) < 1e-9
										   && o.Orgulho == 0;
		Checa("O QUE VALE E O QUE FOI ESCOLHIDO: com 10% de vida e a barra a 20%, a Warrior Race acende (ataque x1,2, defesa x1,3) e o Prideful, que "
			  + "o sangue Saiyajin tambem da mas ele NAO escolheu, fica apagado",
			  soAEscolhida, $"ataque {o.Physoff:0.###}, defesa {o.Physdef:0.###}, orgulho {o.Orgulho}, skills {b.Racial.Entrada.Skills}");
		SkillsRaciais.BioComOSangueInteiroDeTeste = true;
		try
		{
			TickDasRaciais(b, 0.1);
			Checa("(defeito injetado: o bio ganha o sangue INTEIRO -- a uniao automatica) o Prideful nao escolhido acende -- a regua de cima reprovaria",
				  b.Racial.Efeito.Orgulho == 3 && b.Racial.Efeito.Physoff > Passivas.AtaqueDoGuerreiro + 0.1, $"orgulho {b.Racial.Efeito.Orgulho}");
		}
		finally { SkillsRaciais.BioComOSangueInteiroDeTeste = false; }
		TickDasRaciais(b, 0.1);

		// ---- a ativa escolhida aperta; a do mesmo sangue que ele nao escolheu, nao ----
		List<string> folego = Ouvir(() => UsarHabilidade(b, Passivas.SegundoFolego));
		List<string> maximo = Ouvir(() => UsarHabilidade(b, Passivas.PoderMaximo));
		Checa("A ATIVA ESCOLHIDA APERTA (Second Wind) -- e a que o sangue humano tambem da e ele NAO escolheu (Max Power) ouve a recusa do sangue",
			  b.Racial.FolegoAte > NowMs() && !b.Racial.PoderMaximo && Disse(maximo, RecusaDoSangue),
			  $"{Ultimos(folego)} || {Ultimos(maximo)}");

		// ---- uma por vez, e pra sempre ----
		List<string> deNovo = Ouvir(() => UsarHabilidade(b, SkillsRaciais.PrefixoDaEscolha + SkillRacial.RacaGuerreira));
		Checa("...e ela e PRA SEMPRE: escolher de novo a mesma e recusado", Disse(deNovo, "já tem essa skill"), Ultimos(deNovo));
		foreach (SkillRacial s in new[] { SkillRacial.GuerreiroVeterano, SkillRacial.Persistencia, SkillRacial.Resiliencia, SkillRacial.Disrupcao })
			UsarHabilidade(b, SkillsRaciais.PrefixoDaEscolha + s);
		SkillRacial seis = SkillRacial.RacaGuerreira | SkillRacial.SegundoFolego | SkillRacial.GuerreiroVeterano | SkillRacial.Persistencia
								   | SkillRacial.Resiliencia | SkillRacial.Disrupcao;
		List<string> setima = Ouvir(() => UsarHabilidade(b, SkillsRaciais.PrefixoDaEscolha + SkillRacial.Vitalidade));
		TickDasRaciais(b, 0.1);
		Checa("A SEXTA FECHA: com seis escolhidas, a setima (Warrior's Vitality) e recusada, o fato da vaga apaga e o corpo vale EXATAMENTE as seis",
			  (SkillRacial)f.bio_skills == seis && b.Racial.Entrada.Skills == seis && SkillsRaciais.AindaEscolhe(f) == 0
			  && (FatosDe(b) & Vaga) == 0 && Disse(setima, "já escolheu as 6"), $"{SkillsRaciais.Nomes((SkillRacial)f.bio_skills)} || {Ultimos(setima)}");

		// ---- o fio e o save ----
		EscutaDeAtributos = [];
		try
		{
			b.SigAtributos = "";
			MandarAtributos(b);
			Jandirus.Net.Protocol.AtributosState saiu = EscutaDeAtributos.Count > 0 ? EscutaDeAtributos[^1] : default;
			var w = new LiteNetLib.Utils.NetDataWriter();
			saiu.Write(w);
			Jandirus.Net.Protocol.AtributosState lido = Jandirus.Net.Protocol.AtributosState.Read(new LiteNetLib.Utils.NetDataReader(w.CopyData()));
			Checa("NO FIO: a ficha lenta leva as seis que valem e as doze que o sangue da (o cliente desenha os botoes por elas), e volta igual da leitura",
				  lido.SkillsRaciais == (uint)seis && lido.SkillsDoSangue == (uint)sangue, $"{lido.SkillsRaciais} / {lido.SkillsDoSangue}");
		}
		finally { EscutaDeAtributos = null; }
		var opcoes = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
		Fighter? volta = System.Text.Json.JsonSerializer.Deserialize<Fighter>(System.Text.Json.JsonSerializer.Serialize(f, opcoes), opcoes);
		Checa("...e a escolha vai pro SAVE (`Fighter.bio_skills`): quem reloga volta com as mesmas seis", volta?.bio_skills == (uint)seis,
			  $"{volta?.bio_skills}");

		// ---- o Cell escolhe sozinho ----
		// (2026-10-19) o sangue que o dono deu a ele: Saiyajin, Namekuseijin e Frost Demon. Sem o Humano sai o Second Wind, e a segunda volta
		// do rodizio chega ao Perfect Control.
		SkillRacial doCell = SkillRacial.GuerreiroVeterano | SkillRacial.Disrupcao | SkillRacial.Ira
									   | SkillRacial.RacaGuerreira | SkillRacial.Resiliencia | SkillRacial.Controle;
		ServerPlayer? cell = NascerNpc("cell", host.Zone, host.Pos + new Vec2(0, 60 * Jandirus.Core.World.ZoneCollision.TileSize), ++_lugarDaBancadaDeBio);
		if (cell == null) { Checa("PRECONDICAO: o Cell nasceu", false); return; }
		try
		{
			Span<byte> naMao = stackalloc byte[Jandirus.Core.Ai.AtivasRaciaisNaIa.Todas.Length];
			AtivasRaciaisNaMao(cell, naMao);
			var naMaoDoCell = new List<string>();
			for (int i = 0; i < naMao.Length; i++)
				if (naMao[i] != 0) naMaoDoCell.Add(Jandirus.Core.Ai.AtivasRaciaisNaIa.Todas[i].Id);
			Checa("O CELL (NPC) ESCOLHE SOZINHO, ao nascer: o rodizio do sangue dele (Saiyajin, Namekuseijin, Icer), as ativas primeiro -- "
				  + "Seasoned Warrior, Spirit Disruption, Unrestrained Wrath, Warrior Race, Namekian Resilience e Perfect Control",
				  (SkillRacial)cell.Ficha.bio_skills == doCell && SkillsRaciais.EscolhaDoNpc(["Saiyan", "Namekian", "Frost Demon"]) == doCell,
				  SkillsRaciais.Nomes((SkillRacial)cell.Ficha.bio_skills));
			Checa("...e a IA dele poe na mao as DUAS ativas escolhidas (Spirit Disruption, Unrestrained Wrath) e nenhuma outra",
				  naMaoDoCell.Count == 2 && naMaoDoCell.Contains(Passivas.Disrupcao) && naMaoDoCell.Contains(Passivas.Ira),
				  string.Join(", ", naMaoDoCell));
		}
		finally { RemoverNpc(cell); }
	}

	/// <summary>
	/// (2026-10-17) 4b. A ABSORCAO DE COMBATE -- o adendo do dono: agarrado, cada aperto do absorver e uma ESTOCADA da cauda, na ordem braco
	/// esquerdo, braco direito, perna esquerda, perna direita, cabeca e tronco (o primeiro ainda inteiro); 5% do tronco, do Ki e do folego pra
	/// quem fura, 5% do Ki e do folego de quem e furado; uma por segundo; o androide de absorcao e imune.
	/// </summary>
	private void MedirAAbsorcaoDeCombate(Checagem Checa, ServerPlayer host, ServerPlayer bio, List<ServerPlayer> forjados)
	{
		GD.Print("--- 4b. A ABSORCAO DE COMBATE (o adendo do dono) ---");
		ServerPlayer? presa = ForjarCorpo(host, "Human", "Presa da cauda", forjados);
		if (presa == null) { Checa("PRECONDICAO: a presa nasceu", false); return; }
		// LADO A LADO, o bio olhando pra ela: o agarrao pega quem esta NA FRENTE (`CorpoNaFrente`).
		presa.Pos = bio.Pos + new Vec2(0.8f * ZoneCollision.TileSize, 0);
		bio.Facing = Facing.East;
		bio.CenaSegundos = 0;   // a cena da ultima evolucao ainda o prendia (o relogio dela anda no tique, e a bancada nao espera)
		// OS CORPOS QUE SOBRARAM DA ESCADA (o androide de absorcao caido, a frase do scouter) ficam a dois tiles do bio: o agarrao pega quem esta
		// NA FRENTE, e a primeira rodada pegou um deles. Vao pra longe.
		foreach (ServerPlayer outro in ZoneList(bio.Zone.Hash).ToList())
			if (outro != bio && outro != presa && Vec2.Distance(outro.Pos, bio.Pos) < 3 * ZoneCollision.TileSize)
				outro.Pos = bio.Pos + new Vec2(0, 40 * ZoneCollision.TileSize);
		presa.Combate!.Corpo.Restaurar();
		presa.Combate.SincronizarVida();
		AlternarAgarrao(bio);
		Checa("(palco) o bio AGARRA a presa pela porta de producao", bio.AgarrandoId == presa.Id, $"agarrando {bio.AgarrandoId}");
		if (bio.AgarrandoId != presa.Id) return;

		// metade de tudo no bio, a presa cheia -- pra medir o que vai e o que volta
		Jandirus.Core.Combat.BodyPart tronco = bio.Combate!.Corpo.Achar(Jandirus.Core.Combat.Body.NomeDoTronco)!;
		tronco.Vida = tronco.VidaMax * 0.5;
		bio.Combate.SincronizarVida();
		bio.Ficha.Ki = bio.Ficha.MaxKi * 0.5;
		bio.Ficha.stamina = bio.Ficha.maxstamina * 0.5;
		presa.Ficha.Ki = presa.Ficha.MaxKi;
		presa.Ficha.stamina = presa.Ficha.maxstamina;
		_prontoEstocar.Remove(bio.Id);
		Jandirus.Core.Combat.BodyPart bracoE = presa.Combate.Corpo.Achar("Braco esquerdo")!;

		UsarHabilidade(bio, "absorver");
		Checa("AGARRADO, o absorver vira ESTOCADA: a cauda fura o BRACO ESQUERDO primeiro (30% dele) -- e nao abre execucao nenhuma",
			  Math.Abs(bracoE.Fracao - 0.7) < 0.01 && !_execucoesDoBio.ContainsKey(bio.Id), $"braco esquerdo {bracoE.Fracao:P0}");
		Checa("...bebe 5% do Ki e do folego da presa",
			  Math.Abs(presa.Ficha.Ki / presa.Ficha.MaxKi - 0.95) < 0.005 && Math.Abs(presa.Ficha.stamina / presa.Ficha.maxstamina - 0.95) < 0.005,
			  $"Ki {presa.Ficha.Ki / presa.Ficha.MaxKi:P1}, folego {presa.Ficha.stamina / presa.Ficha.maxstamina:P1}");
		Checa("...e devolve 5% do TRONCO, do Ki e do folego a quem fura (o adendo)",
			  Math.Abs(tronco.Fracao - 0.55) < 0.005 && Math.Abs(bio.Ficha.Ki / bio.Ficha.MaxKi - 0.55) < 0.005
			  && Math.Abs(bio.Ficha.stamina / bio.Ficha.maxstamina - 0.55) < 0.005,
			  $"tronco {tronco.Fracao:P1}, Ki {bio.Ficha.Ki / bio.Ficha.MaxKi:P1}, folego {bio.Ficha.stamina / bio.Ficha.maxstamina:P1}");
		UsarHabilidade(bio, "absorver");
		Checa("...uma por SEGUNDO: o segundo aperto no mesmo instante nao fura", Math.Abs(bracoE.Fracao - 0.7) < 0.01, $"{bracoE.Fracao:P0}");

		// ---- a ORDEM: braco esquerdo, braco direito, perna esquerda, perna direita, cabeca, e o tronco ----
		var ordem = new List<string>();
		for (int i = 0; i < 16; i++)
		{
			AdiantoDoRelogioDeTeste += 1_050;
			if (BioAndroids.MembroDaEstocada(presa.Combate.Corpo) is { } proximo && (ordem.Count == 0 || ordem[^1] != proximo.Nome))
				ordem.Add(proximo.Nome);
			UsarHabilidade(bio, "absorver");
		}
		string esperada = "Braco esquerdo > Braco direito > Perna esquerda > Perna direita > Cabeca > Torso";
		Checa("A CAUDA SEGUE A ORDEM do adendo -- cada membro ate quebrar (tres estocadas), e o tronco por ultimo",
			  string.Join(" > ", ordem) == esperada, string.Join(" > ", ordem));
		Checa("...e os cinco membros da ordem ficaram QUEBRADOS",
			  BioAndroids.OrdemDaEstocada.All(n => presa.Combate.Corpo.Achar(n) is { Quebrado: true } or { Decepado: true }));
		Soltar(bio, MotivoDaSoltura.Tecla);

		// ---- o androide de ABSORCAO e maquina por dentro ----
		ServerPlayer? maquina = ForjarCorpo(host, "Android", "Androide de absorcao da cauda", forjados);
		if (maquina != null)
		{
			maquina.Ficha.AndroideAbsorcao = true;
			maquina.Pos = bio.Pos + new Vec2(0.8f * ZoneCollision.TileSize, 0);
			presa.Pos = bio.Pos + new Vec2(0, 6 * ZoneCollision.TileSize);
			AlternarAgarrao(bio);
			_prontoEstocar.Remove(bio.Id);
			double antes = maquina.Combate!.Corpo.Achar("Braco esquerdo")!.Fracao;
			List<string> metal = Ouvir(() => UsarHabilidade(bio, "absorver"));
			Checa("o ANDROIDE DE ABSORCAO e imune (o adendo: membro mecanico e imune): agarrado, a cauda bate em metal e nao bebe nada",
				  bio.AgarrandoId == maquina.Id && Math.Abs(maquina.Combate.Corpo.Achar("Braco esquerdo")!.Fracao - antes) < 1e-9
				  && Disse(metal, "máquina por dentro"), Ultimos(metal));
			Soltar(bio, MotivoDaSoltura.Tecla);
		}
		bio.Combate.Corpo.Restaurar();
		bio.Combate.SincronizarVida();
		bio.Ficha.Ki = bio.Ficha.MaxKi;
		bio.Ficha.stamina = bio.Ficha.maxstamina;
	}

	/// <summary>
	/// (2026-10-17) 5b. A LARVA VESTIDA -- o adendo do dono: a larva a qualquer hora, e *"mesmo se vc virar uma larva, vc quando ficar maduro
	/// volta pro estagio onde estava"*. O bio PERFEITO veste a larva pelo verb de
	/// producao, fica selado e voa, se enterra 5 min e volta PERFEITO -- com o BP base e o marco onde estavam.
	/// </summary>
	private void MedirALarvaQuandoQuiser(Checagem Checa, ServerPlayer bio)
	{
		GD.Print("--- 5b. A LARVA QUANDO QUISER (o adendo do dono) ---");
		int estagio = bio.Ficha.bio_stage, marco = bio.Ficha.bio_marco;
		double bp = bio.Ficha.BP;
		List<string> vira = Ouvir(() => UsarHabilidade(bio, "larva"));
		RepercutirPoder(bio);
		RepercutirPoder(bio);
		Checa("o bio PERFEITO veste a LARVA quando quer: a forma de barata POR CIMA do degrau -- o degrau maduro, o marco e o BP base nao se mexem",
			  estagio == BioAndroids.Perfeito && BioAndroids.EmLarva(bio.Ficha) && bio.Ficha.bio_larva && bio.Ficha.bio_stage == estagio
			  && bio.Ficha.bio_marco == marco && Math.Abs(bio.Ficha.BP - bp) < 1, $"degrau {bio.Ficha.bio_stage}, marco {bio.Ficha.bio_marco} | {Ultimos(vira)}");
		Checa("...com o CORPO de larva, SELADA (5 de poder, fora do Sense) e voando sem a skill",
			  bio.Visual.Corpo == BioAndroids.IndiceDoCorpo(BioAndroids.Larva) && bio.Ficha.expressedBP == 5 && !TemEnergiaQueSeSente(bio) && PodeVoar(bio),
			  $"expresso {bio.Ficha.expressedBP:N0}");
		List<string> forma = Ouvir(() => Transformar(bio, subir: true));
		RecusaForma super = bio.Forma.Avaliar("golden_perfect", bio.Ficha.BP, 1, false, Perfil(bio));
		Checa("...e NENHUMA forma entra por cima dela -- nem a Golden Perfect que o degrau perfeito abre (`EstadoDeForma.Avaliar`)",
			  bio.Forma.NaBase && super == RecusaForma.FormaErrada && Disse(forma, "não se transforma"), $"{super} | {Ultimos(forma)}");
		List<string> deNovo = Ouvir(() => UsarHabilidade(bio, "larva"));
		Checa("...e a larva nao se veste duas vezes: pra sair, so enterrada", Disse(deNovo, "enterre-se"), Ultimos(deNovo));

		UsarHabilidade(bio, "enterrar");
		AdiantoDoRelogioDeTeste += (long)(BioAndroids.SegundosEnterradoPraAmadurecer * 1000) + 1_000;
		TickDaLarva();
		RepercutirPoder(bio);
		Checa("ENTERRADA 5 MIN, ela amadurece de volta ao PERFEITO -- o corpo dele, o poder inteiro e sentido, o BP base e o marco onde estavam "
			  + "(o dono: *\"vc quando ficar maduro volta pro estagio onde estava\"*)",
			  !BioAndroids.EmLarva(bio.Ficha) && bio.Ficha.bio_stage == estagio && bio.Ficha.bio_marco == marco && Math.Abs(bio.Ficha.BP - bp) < 1
			  && bio.Visual.Corpo == BioAndroids.IndiceDoCorpo(estagio) && bio.Ficha.expressedBP > 5 && TemEnergiaQueSeSente(bio) && bio.EnterradoDesde < 0,
			  $"degrau {bio.Ficha.bio_stage}, marco {bio.Ficha.bio_marco}, expresso {bio.Ficha.expressedBP:N0}");
	}

	// =====================================================================
	// 4. A ESCADA POR ABSORCAO
	// =====================================================================
	/// <summary>
	/// SEMI-PERFEITO E PERFEITO NAO SE TREINAM: eles vem de COMER gente. Sem este motor, a escada
	/// do bio pararia no imperfeito pra sempre -- tres quartos de uma capacidade, que e pior que
	/// nenhuma.
	/// </summary>
	private void MedirAEscadaPorAbsorcao(Checagem Checa, ServerPlayer host, ServerPlayer bio,
										 List<ServerPlayer> forjados)
	{
		GD.Print("--- 4. A ESCADA POR ABSORCAO ---");

		// ---- A ABSORCAO DE EXECUCAO (o adendo do dono, 2026-10-17): 5 s junto do caido, e so entao ele morre ----
		ServerPlayer? npc = NascerNpc("cidadao", bio.Zone, bio.Pos + new Vec2(8, 0),
													  ++_lugarDaBancadaDeBio);
		if (npc != null)
		{
			forjados.Add(npc);
			npc.Ficha.KO = true;
			bio.AlvoId = npc.Id;
			bio.Ficha.bio_passos = 0;
			// METADE DE TUDO, pra medir o que ela devolve
			foreach (Jandirus.Core.Combat.BodyPart parte in bio.Combate!.Corpo.Partes)
				if (!parte.Decepado) parte.Vida = parte.VidaMax * 0.5;
			bio.Combate.SincronizarVida();
			bio.Ficha.Ki = bio.Ficha.MaxKi * 0.5;
			bio.Ficha.stamina = bio.Ficha.maxstamina * 0.5;
			UsarHabilidade(bio, "absorver");
			Checa("a ABSORCAO DE EXECUCAO abre e NAO consuma na hora (o adendo do dono: ela leva 5 segundos) -- o caido segue vivo",
				  _execucoesDoBio.ContainsKey(bio.Id) && !npc.Ficha.dead);
			AdiantoDoRelogioDeTeste += 2_000;
			TickDasExecucoesDoBio();
			Checa("...aos dois segundos ela ainda corre", _execucoesDoBio.ContainsKey(bio.Id) && !npc.Ficha.dead);
			AdiantoDoRelogioDeTeste += 3_100;
			TickDasExecucoesDoBio();
			Checa("...e aos cinco ela se CONSUMA: o NPC morre", !_execucoesDoBio.ContainsKey(bio.Id) && npc.Ficha.dead);
			Checa("...e o corpo dele SOME sem cadaver (seca e se desfaz -- `SumiuAbsorvido`)", npc.SumiuAbsorvido);
			Checa("um NPC absorvido vale UM QUINTO de passo (o jogador vale um marco inteiro; o dono: *\"npcs dando bem menos progresso\"*)",
				  Math.Abs(bio.Ficha.bio_passos - BioAndroids.PassoDoNpc) < 1e-9, $"{bio.Ficha.bio_passos}");
			Checa("...e ela devolve 20% de vida, folego e Ki (de 50% a 70%, e nao o corpo inteiro e o tanque cheio do DM)",
				  Math.Abs(bio.Ficha.HP - 70) < 1.5 && Math.Abs(bio.Ficha.Ki / bio.Ficha.MaxKi - 0.7) < 0.01
				  && Math.Abs(bio.Ficha.stamina / bio.Ficha.maxstamina - 0.7) < 0.01,
				  $"vida {bio.Ficha.HP:0.#}, Ki {bio.Ficha.Ki / bio.Ficha.MaxKi:P0}, folego {bio.Ficha.stamina / bio.Ficha.maxstamina:P0}");
			Checa("...e o `AbsorbBP` passou a existir -- ele e `AbsorbDeterminesBP` nunca eram "
				  + "escritos por ninguem, com a formula da MEDIA ja portada e sem consumidor",
				  bio.Ficha.AbsorbBP > 0 && bio.Ficha.AbsorbDeterminesBP);
			bio.Combate.Corpo.Restaurar();
			bio.Combate.SincronizarVida();
			bio.Ficha.Ki = bio.Ficha.MaxKi;
			bio.Ficha.stamina = bio.Ficha.maxstamina;
		}

		// ---- UM PASSO DESFAZ ----
		ServerPlayer? fujao = NascerNpc("cidadao", bio.Zone, bio.Pos + new Vec2(8, 0), ++_lugarDaBancadaDeBio);
		if (fujao != null)
		{
			forjados.Add(fujao);
			fujao.Ficha.KO = true;
			bio.AlvoId = fujao.Id;
			_prontoAbsorver.Remove(bio.Id);
			UsarHabilidade(bio, "absorver");
			bool abriu = _execucoesDoBio.ContainsKey(bio.Id);
			Vec2 onde = bio.Pos;
			bio.Pos += new Vec2(6, 0);
			TickDasExecucoesDoBio();
			Checa("...e um PASSO de quem absorve DESFAZ a execucao: o caido continua vivo (como a execucao do Ruthless Mind)",
				  abriu && !_execucoesDoBio.ContainsKey(bio.Id) && !fujao.Ficha.dead);
			bio.Pos = onde;
		}

		// ---- O ANDROIDE DE ENERGIA INFINITA e o atalho: a forma seguinte, direto ----
		bio.Ficha.bio_passos = 0;
		double bpAntes = bio.Ficha.BP;
		ServerPlayer? androide = ForjarCorpo(host, "Android", "Androide 1", forjados);
		if (androide == null) { Checa("PRECONDICAO: o androide forjado nasceu", false); return; }
		androide.Ficha.AndroideInfinito = true;
		androide.Ficha.KO = true;
		bio.AlvoId = androide.Id;
		_prontoAbsorver.Remove(bio.Id);
		AbsorverAteOFimBio(bio);

		Checa("UM ANDROIDE DE ENERGIA INFINITA absorvido pula pra forma seguinte -- o atalho do Cell (o adendo: so o de energia infinita)",
			  bio.Ficha.bio_stage == BioAndroids.SemiPerfeito && bio.Ficha.bio_marco == 0, $"{bio.Ficha.bio_stage} marco {bio.Ficha.bio_marco}");
		Checa("...e o BP BASE vai a 2x o imperfeito (a RAZAO da tabela, permanente -- por isso o degrau nao e uma forma: o teto de treino, o "
			  + "do Zenkai e o CapCheck leem o BP base)",
			  Math.Abs(bio.Ficha.BP - bpAntes * BioAndroids.MultDoSemiPerfeito) < 1,
			  $"{bio.Ficha.BP:N0} != {bpAntes * 2:N0}");
		Checa("...e o marco `bio2` subiu o ganho de treino",
			  Math.Abs(bio.Ficha.bp_milestone_mult - Milestones.Valor("bio2")) < 1e-9,
			  $"{bio.Ficha.bp_milestone_mult}");

		// ---- o segundo leva a FORMA PERFEITA ----
		bpAntes = bio.Ficha.BP;
		ServerPlayer? androide2 = ForjarCorpo(host, "Android", "Androide 2", forjados);
		if (androide2 == null) { Checa("PRECONDICAO: o segundo androide nasceu", false); return; }
		androide2.Ficha.AndroideInfinito = true;
		androide2.Ficha.KO = true;
		bio.AlvoId = androide2.Id;
		_prontoAbsorver.Remove(bio.Id);
		AbsorverAteOFimBio(bio);

		Checa("o segundo androide leva a FORMA PERFEITA",
			  bio.Ficha.bio_stage == BioAndroids.Perfeito && bio.Ficha.bio_marco == 0, $"{bio.Ficha.bio_stage} marco {bio.Ficha.bio_marco}");
		Checa("...com o BP BASE a 4x o imperfeito: do semi (2x) ao perfeito (4x), a razao e 2 -- e nao os 4x do DM por cima",
			  Math.Abs(bio.Ficha.BP - bpAntes * BioAndroids.MultDoPerfeito / BioAndroids.MultDoSemiPerfeito) < 1,
			  $"{bio.Ficha.BP:N0} != {bpAntes * 2:N0}");
		Checa("...e ela e PERMANENTE (`form3cantrevert`)", bio.Ficha.form3cantrevert);

		// ---- e o terceiro, ja no perfeito, leva ao MARCO 5 (*"vc vai pro perfect form milestone 5"*) ----
		bpAntes = bio.Ficha.BP;
		ServerPlayer? androide3 = ForjarCorpo(host, "Android", "Androide 3", forjados);
		if (androide3 == null) { Checa("PRECONDICAO: o terceiro androide nasceu", false); return; }
		androide3.Ficha.AndroideInfinito = true;
		androide3.Ficha.KO = true;
		bio.AlvoId = androide3.Id;
		_prontoAbsorver.Remove(bio.Id);
		AbsorverAteOFimBio(bio);
		Checa("o androide absorvido DEPOIS do perfeito leva ao MARCO 5 do perfeito (o dono, literal) -- 14x o imperfeito",
			  bio.Ficha.bio_stage == BioAndroids.Perfeito && bio.Ficha.bio_marco == BioAndroids.MarcosPorForma
			  && Math.Abs(bio.Ficha.BP - bpAntes * BioAndroids.MultAbsoluto(BioAndroids.Perfeito, 5) / BioAndroids.MultDoPerfeito) < 1,
			  $"{bio.Ficha.bio_stage} marco {bio.Ficha.bio_marco}, BP {bio.Ficha.BP:N0} contra {bpAntes * 3.5:N0}");

		// ---- o teto: absorver nao anda alem do marco 5 do perfeito ----
		ServerPlayer? sobra = ForjarCorpo(host, "Human", "Sobra", forjados);
		if (sobra != null)
		{
			sobra.Ficha.KO = true;
			bio.AlvoId = sobra.Id;
			_prontoAbsorver.Remove(bio.Id);
			bpAntes = bio.Ficha.BP;
			AbsorverAteOFimBio(bio);
			Checa("no MARCO 5 do perfeito a escada para (dali pra cima o que ha e a Super Perfeita, que e forma e nao degrau)",
				  bio.Ficha.bio_stage == BioAndroids.Perfeito && bio.Ficha.bio_marco == BioAndroids.MarcosPorForma
				  && bio.Ficha.bio_passos == 0 && Math.Abs(bio.Ficha.BP - bpAntes) < 1,
				  $"estagio {bio.Ficha.bio_stage}, marco {bio.Ficha.bio_marco}, passos {bio.Ficha.bio_passos}");
		}

		// ---- O ANDROIDE DE ABSORCAO e maquina por dentro: imune ----
		ServerPlayer? maquina = ForjarCorpo(host, "Android", "Androide de absorcao", forjados);
		if (maquina != null)
		{
			maquina.Ficha.AndroideAbsorcao = true;
			maquina.Ficha.KO = true;
			bio.AlvoId = maquina.Id;
			_prontoAbsorver.Remove(bio.Id);
			List<string> recusa = Ouvir(() => UsarHabilidade(bio, "absorver"));
			Checa("o ANDROIDE DE ABSORCAO e imune (o dono: *\"nao funciona em androides de absorçao de energia\"*): a execucao nem abre, e ele segue vivo",
				  !_execucoesDoBio.ContainsKey(bio.Id) && !maquina.Ficha.dead && Disse(recusa, "máquina por dentro"), Ultimos(recusa));
		}

		// ============================ A FRASE NAO DIZ O PODER DA VITIMA SEM SCOUTER (dono, 2026-10-12) ============================
		// "(+N de poder absorvido ...)" entregava o BP INTEIRO de quem foi comido, a qualquer um. A regra principal do
		// BP: em numero so com medidor (`PoderNaFrase`); sem ele a frase diz a regra -- entrou todo o poder dele.
		// Num bio A PARTE, pra a media do `AbsorbBP` do corpo principal nao mudar debaixo das secoes seguintes.
		// ==========================================================================================================================
		ServerPlayer? leitor = ForjarCorpo(host, BioAndroids.Raca, "Leitor", forjados);
		if (leitor == null) { Checa("PRECONDICAO: o bio das tres leituras nasceu", false); return; }
		var comidos = new List<double>();
		var (semScouter, comScouter, comDefeito) = AsTresLeiturasDoPoder(leitor, () =>
		{
			ServerPlayer? prato = ForjarCorpo(host, "Human", $"Prato {comidos.Count + 1}", forjados);
			if (prato == null) return;
			prato.Ficha.BP = 123_457 + comidos.Count;   // um numero que nao se confunde com nenhum outro da frase
			prato.Ficha.KO = true;
			leitor.AlvoId = prato.Id;
			_prontoAbsorver.Remove(leitor.Id);
			comidos.Add(prato.Ficha.BP);
			AbsorverAteOFimBio(leitor);
		});
		Checa("SEM scouter, a absorcao do bio diz que entrou TODO o poder dele -- e nao o BP da vitima em numero",
			  comidos.Count == 3 && Disse(semScouter, "todo o poder dele") && !TrazOPoder(semScouter, comidos[0]),
			  Ultimos(semScouter));
		Checa("COM o scouter ligado, a mesma frase traz o numero",
			  comidos.Count == 3 && TrazOPoder(comScouter, comidos[1]), Ultimos(comScouter));
		Checa("(defeito injetado: o numero sem medidor) sem scouter o numero volta -- a regua de cima reprovaria",
			  comidos.Count == 3 && TrazOPoder(comDefeito, comidos[2]), Ultimos(comDefeito));
	}

	// =====================================================================
	// 5. AS FORMAS
	// =====================================================================
	private void MedirAsFormasDoBio(Checagem Checa, ServerPlayer bio)
	{
		GD.Print("--- 5. AS FORMAS (2026-10-26: a Golden Perfect e a MAX, no lugar da Super Perfeita) ---");

		PerfilDeFormas perfil = Perfil(bio);
		Checa("a linha do BIO-ANDROIDE esta aberta pra ele",
			  Catalogo.LinhasAbertas(perfil).Contains(LinhaDeForma.BioAndroide));
		Checa("...e a SAIYAJIN tambem, pelo `canSSJ` (ele tem as duas: a do corpo dele e a do DNA "
			  + "que o gerou)",
			  Catalogo.LinhasAbertas(perfil).Contains(LinhaDeForma.Saiyajin));

		// ---- a SUPER PERFEITA SAIU, e o nome dela foi pro SSJ2 do bio ----
		FormaDef ssj2 = Catalogo.Def("ssj2")!, ssj3 = Catalogo.Def("ssj3")!;
		Checa("a SUPER PERFEITA (a `Cell4()` do DM) SAIU DO JOGO, e a linha do bio e so das duas divinas -- a escolha do dono",
			  Catalogo.Def("super_perfect") == null && Catalogo.DaLinha(LinhaDeForma.BioAndroide).All(d => d.PedeGodKi >= 0),
			  string.Join(", ", Catalogo.DaLinha(LinhaDeForma.BioAndroide).Select(d => d.Id)));
		Checa("o SSJ2 do bio se chama \"Super Perfeito\" e o SSJ3 \"Super Perfeito 2\" (o dono) -- e os de um Saiyajin continuam Super Saiyajin",
			  Catalogo.NomeDe(ssj2, false, bio.Race) == "Super Perfeito" && Catalogo.NomeDe(ssj3, false, bio.Race) == "Super Perfeito 2"
			  && Catalogo.NomeDe(ssj2, false, "Saiyan") == ssj2.Nome && Catalogo.NomeDe(ssj3, false, "Saiyan") == ssj3.Nome,
			  $"{Catalogo.NomeDe(ssj2, false, bio.Race)} / {Catalogo.NomeDe(ssj3, false, bio.Race)} / {Catalogo.NomeDe(ssj2, false, "Saiyan")}");

		GodKiState? kiDivinoGuardado = bio.Ficha.godki;
		int estagioGuardado = bio.Ficha.bio_stage;
		bool saiyanGuardado = bio.Ficha.bio_saiyan_dna, canGuardado = bio.Ficha.canSSJ;
		bio.Forma.Entrar(Catalogo.IdBase);
		AplicarForma(bio);
		bio.Ficha.Ki = bio.Ficha.MaxKi;

		// ---- A GOLDEN PERFECT: o ki divino desperto, a forma perfeita, e nada de sangue ----
		bio.Ficha.godki = null;
		RecusaForma semKiDivino = bio.Forma.Avaliar("golden_perfect", bio.Ficha.BP, 1, false, Perfil(bio));
		Checa("SEM o ki divino desperto a GOLDEN PERFECT e recusada pelo ki divino (`PedeGodKi` 0: e o SSG do bio)",
			  semKiDivino == RecusaForma.SemGodKi, $"{semKiDivino}");
		bio.Ficha.godki = new GodKiState { awakened = true, mastery = 0 };
		RecusaForma desperto = bio.Forma.Avaliar("golden_perfect", bio.Ficha.BP, 1, false, Perfil(bio));
		Checa("com o ki divino DESPERTO e a FORMA PERFEITA ela abre -- o dono: *\"o bio androide pode ter god ki ao qual ele libera a forma "
			  + "Golden Perfect\"*", desperto == RecusaForma.Pode, $"{desperto}");
		bio.Ficha.bio_saiyan_dna = false;
		bio.Ficha.canSSJ = false;
		RecusaForma semSangue = bio.Forma.Avaliar("golden_perfect", bio.Ficha.BP, 1, false, Perfil(bio));
		Checa("...e ela NAO pede DNA Saiyajin: e do CORPO dele -- o bio sem sangue Saiyajin fica com as duas divinas",
			  semSangue == RecusaForma.Pode, $"{semSangue}");
		bio.Ficha.bio_saiyan_dna = saiyanGuardado;
		bio.Ficha.canSSJ = canGuardado;
		bio.Ficha.bio_stage = BioAndroids.SemiPerfeito;
		RecusaForma goldenNoSemi = bio.Forma.Avaliar("golden_perfect", bio.Ficha.BP, 1, false, Perfil(bio));
		bio.Ficha.godki = new GodKiState { awakened = true, mastery = Catalogo.GodkiRoyalePct };
		RecusaForma maxNoSemi = bio.Forma.Avaliar("bio_max", bio.Ficha.BP, 1, false, Perfil(bio));
		bio.Ficha.bio_stage = estagioGuardado;
		Checa("...e as duas SO NO ESTAGIO PERFEITO (o dono: \"So o Perfeito\") -- o semi-perfeito e recusado nas duas, com o ki divino maduro",
			  goldenNoSemi == RecusaForma.SemFormaAnterior && maxNoSemi == RecusaForma.SemFormaAnterior,
			  $"Golden {goldenNoSemi}, MAX {maxNoSemi}");

		// ---- entrar de verdade, e o multiplicador pela maestria divina ----
		bio.Ficha.godki = new GodKiState { awakened = true, mastery = 0 };
		TransformarPara(bio, "golden_perfect");
		Checa("ele ENTRA na Golden Perfect pelo funil de producao (`TransformarPara`)", bio.Forma.Atual == "golden_perfect", bio.Forma.Atual);
		double degrau = BioAndroids.MultAbsoluto(bio.Ficha.bio_stage, bio.Ficha.bio_marco);
		FormaDef ssg = Catalogo.Def("ssg")!, blue = Catalogo.Def("blue")!, evolution = Catalogo.Def("blue_evolution")!;
		double meio = Catalogo.GodkiBluePct / 2;
		foreach ((double maestria, double esperado, string nivel) in new[]
			{ (0.0, ssg.Mult[0], "o ki divino DESPERTO vale o do SSG"),
			  (meio, (ssg.Mult[0] + blue.Mult[0]) / 2, $"a meio caminho ({meio:0.#}%) vale o meio entre os dois"),
			  (Catalogo.GodkiBluePct, blue.Mult[0], "aos 33% vale o do BLUE"),
			  (Catalogo.GodkiRoyalePct, blue.Mult[0], "e aos 50% continua no do Blue -- o degrau de cima e a MAX") })
		{
			bio.Ficha.godki = new GodKiState { awakened = true, mastery = maestria };
			AplicarForma(bio);
			Checa($"...{nivel}: {esperado:0.##}x o IMPERFEITO (o dono: *\"a golden tem a mesma forca do ssjg mas vai subindo o multiplicador "
				  + "ate chegar no mesmo multiplicador do ssj blue quando atinger 33% de ki divino\"*) -- a forma vale o que falta sobre o degrau "
				  + "que ja mora no BP base",
				  Math.Abs(bio.Ficha.ssjBuff * degrau - esperado) < 1e-6,
				  $"forma {bio.Ficha.ssjBuff:0.###} x degrau {degrau:0.#} = {bio.Ficha.ssjBuff * degrau:0.###}");
		}
		bio.Ficha.godki = new GodKiState { awakened = true, mastery = 0 };
		TickDaMaestriaDivina(bio, 60);
		Checa("...e e NELA que o bio amadurece o ki divino: um minuto na Golden Perfect sobe a maestria divina (`UsaOKiDivino`)",
			  bio.Ficha.godki.mastery > 0, $"{bio.Ficha.godki.mastery:0.####}%");

		// ---- A MAX: os 50%, da Golden ou da base ----
		bio.Ficha.godki = new GodKiState { awakened = true, mastery = Catalogo.GodkiRoyalePct - 1 };
		RecusaForma antesDos50 = bio.Forma.Avaliar("bio_max", bio.Ficha.BP, 1, false, Perfil(bio));
		Checa("a MAX e recusada abaixo dos 50% de ki divino", antesDos50 == RecusaForma.SemGodKi, $"{antesDos50}");
		bio.Ficha.godki = new GodKiState { awakened = true, mastery = Catalogo.GodkiRoyalePct };
		bio.Ficha.Ki = bio.Ficha.MaxKi;
		TransformarPara(bio, "bio_max");
		Checa("aos 50% ele ENTRA na MAX a partir da GOLDEN (o dono: *\"ao chegar em 50% de god ki, ele libera a forma MAX\"*)",
			  bio.Forma.Atual == "bio_max", bio.Forma.Atual);
		Checa("...e ela vale os 56x do BLUE EVOLUTION sobre o imperfeito (*\"a max tem o mesmo bp do blue evolved\"*)",
			  Math.Abs(bio.Ficha.ssjBuff * degrau - evolution.Mult[0]) < 1e-6, $"{bio.Ficha.ssjBuff * degrau:0.###}");
		bio.Forma.Entrar(Catalogo.IdBase);
		AplicarForma(bio);
		bio.Ficha.Ki = bio.Ficha.MaxKi;
		RecusaForma daBase = bio.Forma.Avaliar("bio_max", bio.Ficha.BP, 1, false, Perfil(bio));
		Checa("...e tambem DA BASE, sem passar pela Golden", bio.Forma.NaBase && daBase == RecusaForma.Pode, $"{bio.Forma.Atual}: {daBase}");

		// ---- o consumo, o corpo e a aura ----
		FormaDef golden = Catalogo.Def("golden_perfect")!, max = Catalogo.Def("bio_max")!;
		Checa("o CONSUMO de Ki e o das irmas -- a Golden o do SSG, a MAX o do Blue Evolution (o dono: *\"Mesma coisa do god e blue evolution "
			  + "atual\"*)",
			  golden.Dreno.SequenceEqual(ssg.Dreno) && max.Dreno.SequenceEqual(evolution.Dreno),
			  $"Golden [{string.Join(", ", golden.Dreno)}] / SSG [{string.Join(", ", ssg.Dreno)}]; MAX [{string.Join(", ", max.Dreno)}] / "
			  + $"Evolution [{string.Join(", ", evolution.Dreno)}]");
		Checa("os CORPOS sao os que o dono apontou: `Bio Android 6` na Golden e `Bio Android - Form 5` na MAX",
			  golden.Corpo == CorpoDeForma.BioGolden && max.Corpo == CorpoDeForma.BioMax
			  && BioAndroids.CorpoGoldenPerfect.Contains("Bio Android 6") && BioAndroids.CorpoMax.Contains("Bio Android - Form 5"),
			  $"{golden.Corpo} {BioAndroids.CorpoGoldenPerfect} / {max.Corpo} {BioAndroids.CorpoMax}");
		Checa("e as AURAS: a Golden o fogo do SSG em OURO (folha propria) e a MAX a chama do Blue em VERMELHO",
			  Catalogo.Folha(golden) == FolhaDeAura.DeusDourado && Catalogo.Folha(max) == Catalogo.Folha(blue)
			  && Catalogo.Folha(max) == FolhaDeAura.DeusFrio,
			  $"{Catalogo.Folha(golden)} / {Catalogo.Folha(max)}");

		// ---- e o UI e o UE, no requisito de ki divino (o pedido 88) ----
		PerfilDeFormas abaixo = Perfil(bio) with { GodKi = Catalogo.GodkiUiUePct - 1 };
		PerfilDeFormas noRequisito = Perfil(bio) with { GodKi = Catalogo.GodkiUiUePct };
		Checa("o bio-androide tem o ULTRA INSTINTO e o ULTRA EGO quando chega no requisito de ki divino (o dono: *\"bio androides podem ter UI "
			  + "e UE quando atingem o requisito de god ki\"*) -- e nao antes",
			  Catalogo.LinhasAbertas(noRequisito).Contains(LinhaDeForma.UltraInstinct) && Catalogo.LinhasAbertas(noRequisito).Contains(LinhaDeForma.UltraEgo)
			  && !Catalogo.LinhasAbertas(abaixo).Contains(LinhaDeForma.UltraInstinct) && !Catalogo.LinhasAbertas(abaixo).Contains(LinhaDeForma.UltraEgo),
			  $"{Catalogo.GodkiUiUePct:0}%: {string.Join(", ", Catalogo.LinhasAbertas(noRequisito))}");

		bio.Ficha.godki = kiDivinoGuardado;
		AplicarForma(bio);
		// A CENA DA ESTREIA DA MAX ainda o prendia: a volta desta secao e pelo `Entrar` cru, e nao pela descida de producao que zera a
		// cena -- e a secao seguinte (a larva) recusaria tudo com "espere a cena acabar". O mesmo idioma da absorcao de combate.
		bio.CenaSegundos = 0;
	}

	// =====================================================================
	// 6. O SSJ2 PELA MORTE
	// =====================================================================
	private void MedirOSsj2PelaMorte(Checagem Checa, ServerPlayer bio)
	{
		GD.Print("--- 6. O SSJ2 PELA MORTE ---");

		bio.Ficha.dead = false;
		bio.Ficha.KO = false;

		// ---- a RAIVA nao abre: e a regra que o DM escreve em tres lugares ----
		bio.Forma.Maestria.Por("ssj1", 100);
		bio.Ficha.BP = 1e12;
		PerfilDeFormas comFuria = Perfil(bio) with { Raiva = NivelDeRaiva.Extrema };
		RecusaForma porRaiva = bio.Forma.Avaliar("ssj2", bio.Ficha.BP, 1, false, comFuria);
		Checa("mesmo com FURIA EXTREMA, SSJ1 dominado e poder de sobra, o SSJ2 e RECUSADO -- no bio "
			  + "de laboratorio ele so vem pela MORTE, e sem esta trava a raiva pularia a forma "
			  + "perfeita, o SSJ1 dominado e a propria morte",
			  porRaiva == RecusaForma.NaoConcedida, $"{porRaiva}");

		// ---- e o SSJ3 fica atras da mesma porta, de graca ----
		RecusaForma ssj3 = bio.Forma.Avaliar("ssj3", bio.Ficha.BP, 1, false, comFuria);
		Checa("...e o SSJ3 fica atras da MESMA porta sem uma linha a mais (ele pede 50% de maestria "
			  + "no SSJ2, e nao se treina uma forma que nao se tem)",
			  ssj3 != RecusaForma.Pode, $"{ssj3}");

		// ---- morrer converte ----
		double ssj2atAntes = bio.Forma.Limiares?.ssj2at ?? 0;
		bool morreu = bio.Combate!.Morrer();
		Checa("A MORTE E CANCELADA e vira o despertar (`Death.dm:10-12`, o PRIMEIRO `if` do Death)",
			  !morreu && !bio.Ficha.dead);
		Checa("...o SSJ2 foi liberado", bio.Forma.Despertou("ssj2"));
		Checa("...o corpo se recompos no lugar (vivo, de pe, Ki cheio)",
			  !bio.Ficha.KO && bio.Ficha.Ki >= bio.Ficha.MaxKi - 1);
		Checa("...com o desconto de despertar `ssj2at /= 2`, o mesmo da via saiyajin",
			  ssj2atAntes <= 0 || Math.Abs((bio.Forma.Limiares?.ssj2at ?? 0) - ssj2atAntes / 2) < 1,
			  $"{bio.Forma.Limiares?.ssj2at} != {ssj2atAntes / 2}");
		Checa("...e o marco `bio_ssj2` subiu o ganho de treino pra 4x",
			  Math.Abs(bio.Ficha.bp_milestone_mult - Milestones.Valor("bio_ssj2")) < 1e-9,
			  $"{bio.Ficha.bp_milestone_mult}");

		// ---- e agora ele ENTRA nela ----
		// DE DENTRO DO SSJ1, e nao da base: o SSJ2 e degrau de escada e sempre exigiu o anterior
		// (`ForaDeOrdem`). A pergunta aqui e outra -- a trava do BIO (`NaoConcedida`) saiu do
		// caminho? --, e mede-la da base mediria a regra da escada em vez da regra do bio.
		TransformarPara(bio, "ssj1");
		RecusaForma depois = bio.Forma.Avaliar("ssj2", bio.Ficha.BP, 1, false, comFuria);
		Checa("...e dali em diante a forma e dele: subindo do SSJ1 ela ACEITA (o bug que o proprio "
			  + "DM documenta em `mst_form_apply` -- 'ficava com uma forma na qual nao conseguia "
			  + "reentrar')",
			  depois == RecusaForma.Pode, $"{depois} (forma atual: {bio.Forma.Atual})");

		// (2026-10-26) E ELA SE ANUNCIA "SUPER PERFEITO" -- o dono: *"bio androide quando vira ssj2 n deveria aparecer super saiyan 2"*.
		List<string> anuncio = Ouvir(() => TransformarPara(bio, "ssj2"));
		Checa("...e o SSJ2 do bio se ANUNCIA \"Super Perfeito\", e nao \"Super Saiyajin 2\" (o anuncio do servidor, pelo funil do nome)",
			  bio.Forma.Atual == "ssj2" && Disse(anuncio, Catalogo.NomeDoSsj2DoBio) && !Disse(anuncio, Catalogo.Def("ssj2")!.Nome),
			  $"{bio.Forma.Atual} | {Ultimos(anuncio)}");
		bio.Forma.Entrar(Catalogo.IdBase);
		AplicarForma(bio);
		TransformarPara(bio, Catalogo.IdBase);

		// ---- uma vez na vida ----
		bio.Ficha.dead = false;
		bool segunda = bio.Combate.Morrer();
		Checa("a segunda morte MATA: o despertar acontece UMA vez na vida", segunda && bio.Ficha.dead);
		bio.Combate.Reviver();
	}

	// =====================================================================
	// 7. AS DUAS CLASSES DE ANDROIDE
	// =====================================================================
	private void MedirAsClassesDeAndroide(Checagem Checa, ServerPlayer host,
										  List<ServerPlayer> forjados, List<Obra> obras)
	{
		GD.Print("--- 7. AS CLASSES DE ANDROIDE ---");

		ServerPlayer? cobaia = ForjarCorpo(host, "Human", "Cobaia Android", forjados);
		if (cobaia == null) { Checa("PRECONDICAO: a cobaia nasceu", false); return; }

		var pod = new Obra
		{
			Id = 984_002, Tipo = "Android_Creation_Mainframe", Aparafusada = true, Lab = 1,
			X = cobaia.Pos.X, Y = cobaia.Pos.Y, DonoConta = cobaia.Conta, DonoNome = cobaia.Name,
		};
		pod.PorZona(cobaia.Zone);
		_noChao.Add(pod);
		obras.Add(pod);

		cobaia.Ficha.techskill = 99;
		cobaia.Ficha.Zeni = 5_000_000;
		cobaia.Ficha.BP = 100;
		ComandoDeTech(cobaia, "androide_infinito", "");
		Checa("a conversao em ANDROIDE DE ENERGIA INFINITA acontece pelo verbo",
			  cobaia.Ficha.AndroideInfinito);
		Checa("...com o BP alcado ao piso de 2 milhoes (`dnl_apply_android_bp`: vira, nao soma, "
			  + "quando esta abaixo)",
			  Math.Abs(cobaia.Ficha.BP - 2_000_000) < 1, $"{cobaia.Ficha.BP:N0}");

		// ---- E ELE PRECISA FAZER ALGUMA COISA: era o campo escrito e sem UM leitor no repo ----
		cobaia.Ficha.Statify();
		cobaia.Ficha.stamina = 0;
		cobaia.Ficha.CurrentNutrition = 0;
		cobaia.Ficha.Ki = 0;
		TickDoNucleoInfinito();
		Checa("O NUCLEO INFINITO FUNCIONA -- `AndroideInfinito` era escrito, ia pro disco e **nao "
			  + "tinha um unico leitor no repo**: 2 milhoes de zeni por fome, cansaco e Ki normais",
			  cobaia.Ficha.stamina >= cobaia.Ficha.maxstamina
			  && cobaia.Ficha.CurrentNutrition > 0 && cobaia.Ficha.Ki > 0,
			  $"folego {cobaia.Ficha.stamina:0}/{cobaia.Ficha.maxstamina:0}, "
			  + $"nutricao {cobaia.Ficha.CurrentNutrition:0}, ki {cobaia.Ficha.Ki:0}");

		// ---- a postura e do de ABSORCAO, e ela recusa quem nao e ----
		UsarHabilidade(cobaia, "absorver_ki");
		Checa("o androide de ENERGIA INFINITA nao tem coletores (o verbo diz qual das duas "
			  + "conversoes ele fez, em vez de sumir)",
			  !cobaia.Ficha.ki_absorb_stance);

		// ---- o de absorcao ----
		ServerPlayer? sugador = ForjarCorpo(host, "Human", "Cobaia Absorcao", forjados);
		if (sugador == null) { Checa("PRECONDICAO: a segunda cobaia nasceu", false); return; }
		var pod2 = new Obra
		{
			Id = 984_003, Tipo = "Android_Creation_Mainframe", Aparafusada = true, Lab = 1,
			X = sugador.Pos.X, Y = sugador.Pos.Y, DonoConta = sugador.Conta, DonoNome = sugador.Name,
		};
		pod2.PorZona(sugador.Zone);
		_noChao.Add(pod2);
		obras.Add(pod2);

		sugador.Ficha.techskill = 99;
		sugador.Ficha.Zeni = 5_000_000;
		ComandoDeTech(sugador, "androide_absorcao", "");
		Checa("a conversao em ANDROIDE DE ABSORCAO acontece pelo verbo", sugador.Ficha.AndroideAbsorcao);

		sugador.Ficha.Statify();
		Checa("PRECONDICAO: de coletores fechados, um ataque de ki NAO e engolido",
			  !EngoliuOAtaqueDeKi(sugador, "Kamehameha", fisico: false));

		UsarHabilidade(sugador, "absorver_ki");
		Checa("a POSTURA abre pelo verbo", sugador.Ficha.ki_absorb_stance);
		Checa("...e o preco dela e nao andar (`canmove = 0`), pelo funil de vetor do jogo",
			  !PodeMexerOCorpo(sugador));

		sugador.Ficha.Ki = 0;
		Checa("...e ai o ataque de ki e ENGOLIDO INTEIRO -- sem sorteio: e uma CERTEZA comprada com "
			  + "imobilidade, e nao mais uma chance em cima da deflexao",
			  EngoliuOAtaqueDeKi(sugador, "Kamehameha", fisico: false));
		Checa("...devolvendo 6% do tanque por golpe (`DNL_ABSORB_KI_PER_HIT`)",
			  Math.Abs(sugador.Ficha.Ki - sugador.Ficha.MaxKi * 0.06) < 1,
			  $"{sugador.Ficha.Ki:0} != {sugador.Ficha.MaxKi * 0.06:0}");

		sugador.Ficha.Ki = sugador.Ficha.MaxKi * 10;
		EngoliuOAtaqueDeKi(sugador, "Kamehameha", fisico: false);
		Checa("...com teto no DOBRO do tanque (`min(MaxKi * 2, ...)`)",
			  sugador.Ficha.Ki <= sugador.Ficha.MaxKi * 2 + 1, $"{sugador.Ficha.Ki:0}");

		Checa("um SOCO nao e engolido (a postura e imune a ENERGIA e indefesa contra punho)",
			  !EngoliuOAtaqueDeKi(sugador, "soco", fisico: true));

		sugador.Ficha.KO = true;
		TickDaPostura();
		Checa("cair NOCAUTEADO fecha os coletores sozinho -- sem isto a postura viraria uma trava "
			  + "permanente: nocauteado nao aperta verbo",
			  !sugador.Ficha.ki_absorb_stance);
		sugador.Ficha.KO = false;
	}

	// =====================================================================
	// 8. DESTRUIR O LABORATORIO
	// =====================================================================
	private void MedirOLaboratorioDestruido(Checagem Checa, ServerPlayer host,
											List<ServerPlayer> forjados, List<Obra> obras)
	{
		GD.Print("--- 8. DESTRUIR O LABORATORIO CANCELA ---");

		ServerPlayer? dono = ForjarCorpo(host, "Human", "Dono do Lab", forjados);
		if (dono == null) { Checa("PRECONDICAO: o dono nasceu", false); return; }

		var lab = new Obra
		{
			Id = 984_004, Tipo = "Android_Creation_Mainframe", Aparafusada = true, Lab = 2,
			X = dono.Pos.X, Y = dono.Pos.Y, DonoConta = dono.Conta, DonoNome = dono.Name,
			Fornada = new Gestacao
			{
				DonoConta = dono.Conta, MaiorBp = 1000, PrometidaEm = NowMs() + 600_000,
				Amostras = { new Amostra { Raca = "Human", Doador = "qualquer", Bp = 1000 } },
			},
		};
		lab.PorZona(dono.Zone);
		_noChao.Add(lab);
		obras.Add(lab);

		// Uma pancada muito acima da armadura: o que se mede aqui e o CANCELAMENTO, e nao a conta
		// de armadura (que tem bancada propria).
		Estragar(lab, lab.ArmaduraMax * 100, dono);

		Checa("destruir o laboratorio CANCELA a gestacao (o preco de doze horas e ter que DEFENDER "
			  + "o tanque -- e ate aqui ele caia CALADO, sem aviso ao dono e sem anuncio)",
			  lab.Fornada == null && !_noChao.Contains(lab));
	}

	// =====================================================================
	// 9. DE QUEM **NAO** SE COLHE
	// =====================================================================
	/// <summary>
	/// ============================ O CRIVO SO E CRIVO SE ELE RECUSAR ALGUEM ============================
	/// A linha "nocauteado, a agulha pega" fica verde com o filtro <see cref="EhJogador"/>
	/// **deletado**. Ela nao mede o crivo: mede a agulha. O que mede o crivo e o contra-exemplo, e ele
	/// tem que ser tres, porque `Gente.EhJogador` e uma conjuncao de tres campos e cada um deles fecha
	/// uma porta diferente do jogo:
	///
	///   * `papel != null`     -- o CIDADAO do povoamento. Sem esta metade o tanque de quatro se enche
	///                            visitando quatro planetas e nocauteando um habitante em cada, de
	///                            graca e pra sempre (a `Manutencao` repoe a populacao a cada 5 min);
	///   * `donoDoCorpoLargado`-- o BONECO que fica no chao quando alguem projeta a consciencia. Ele
	///                            tem a ficha inteira do dono: seria colher DNA de um jogador FORTE
	///                            sem lutar com ele, com o proprio dono assistindo de outro corpo;
	///   * `peer == null`      -- o REFLEXO DA MENTE e todo corpo sem dono na tela. Este e o pior dos
	///                            tres: dentro da propria cabeca da pra fabricar um adversario com a
	///                            SUA ficha, derruba-lo e agulha-lo -- DNA do proprio jogador, de
	///                            graca, sem risco, quantas vezes quiser.
	///
	/// OS TRES CORPOS DIFEREM DE UM JOGADOR VALIDO EM **UM** CAMPO CADA, e e por isso que eles provam
	/// alguma coisa: um NPC cru falharia por dois motivos ao mesmo tempo (sem dono E com papel), e a
	/// recusa nao diria qual das duas metades esta viva.
	/// =============================================================================================
	/// </summary>
	private void MedirQuemNaoSeColhe(Checagem Checa, ServerPlayer host,
									 List<ServerPlayer> forjados, List<Obra> obras)
	{
		GD.Print("--- 9. DE QUEM **NAO** SE COLHE ---");

		ServerPlayer? cientista = ForjarCorpo(host, "Human", "Dr. Crivo", forjados, PalcoDoCrivo);
		if (cientista == null) { Checa("PRECONDICAO: o cientista do crivo nasceu", false); return; }
		cientista.Ficha.techskill = 99;

		Obra lab = PorUmLabDeBio(cientista, 984_010, obras);
		Checa("PRECONDICAO: o laboratorio do crivo esta de pe", LabDeBio(cientista) != null);

		// ---- 1. O CIDADAO: papel de NPC, e so isso ----
		// O `Peer` do host e EMPRESTADO de proposito: sem ele o cidadao falharia por dois motivos ao
		// mesmo tempo e a recusa nao provaria qual metade da conjuncao esta viva.
		ServerPlayer? cidadao = NascerNpc("cidadao", cientista.Zone, cientista.Pos,
										  ++_lugarDaBancadaDeBio);
		if (cidadao != null)
		{
			forjados.Add(cidadao);
			cidadao.Peer = host.Peer;                  // tudo igual a um jogador...
			Checa("PRECONDICAO: o cidadao tem PAPEL de NPC (e a unica coisa que o separa de gente)",
				  cidadao.Papel != null);
			cidadao.Ficha.KO = true;
			ComandoDeTech(cientista, "colher_dna", "");
			Checa("NAO se colhe DNA de um CIDADAO do povoamento -- e ele difere de um jogador valido "
				  + "em UM campo (`papel`): sem esta recusa o tanque de quatro se enche em quatro "
				  + "planetas, sem tocar em ninguem",
				  lab.Fornada == null || lab.Fornada.Amostras.Count == 0,
				  $"{lab.Fornada?.Amostras.Count} amostra(s)");
			cidadao.Ficha.KO = false;
			cidadao.Peer = null;
		}

		// ---- 2. O BONECO LARGADO ----
		ServerPlayer? boneco = ForjarCorpo(host, "Saiyan", "Corpo Largado", forjados, PalcoDoCrivo);
		if (boneco != null)
		{
			boneco.DonoDoCorpoLargado = host.Id;       // o unico campo diferente
			boneco.Ficha.BP = 999_000_000;             // e ele e FORTE: o premio que o exploit daria
			boneco.Ficha.KO = true;
			ComandoDeTech(cientista, "colher_dna", "");
			Checa("NAO se colhe DNA do BONECO que sobra quando alguem projeta a consciencia -- ele "
				  + "carrega a ficha inteira do dono, entao valeria o DNA de um jogador forte sem "
				  + "lutar com ele",
				  lab.Fornada == null || lab.Fornada.Amostras.Count == 0,
				  $"{lab.Fornada?.Amostras.Count} amostra(s)");
			boneco.Ficha.KO = false;
			boneco.DonoDoCorpoLargado = 0;
		}

		// ---- 3. O REFLEXO DA MENTE (e todo corpo sem dono na tela) ----
		ServerPlayer? reflexo = ForjarCorpo(host, "Saiyan", "Reflexo da Mente", forjados, PalcoDoCrivo);
		if (reflexo != null)
		{
			reflexo.Peer = null;                       // o unico campo diferente
			reflexo.Ficha.BP = 999_000_000;
			reflexo.Ficha.KO = true;
			ComandoDeTech(cientista, "colher_dna", "");
			Checa("NAO se colhe DNA do REFLEXO DA MENTE -- la dentro da pra fabricar um adversario "
				  + "com a SUA propria ficha, derruba-lo e agulha-lo: DNA de graca, sem risco, "
				  + "quantas vezes quiser",
				  lab.Fornada == null || lab.Fornada.Amostras.Count == 0,
				  $"{lab.Fornada?.Amostras.Count} amostra(s)");
			reflexo.Ficha.KO = false;
			reflexo.Peer = host.Peer;
		}

		// ---- 4. LONGE ----
		ServerPlayer? longe = ForjarCorpo(host, "Human", "Caido Longe", forjados, PalcoDoCrivo);
		if (longe != null)
		{
			longe.Pos = cientista.Pos + new Vec2(AlcanceDeUso * 4, 0);
			longe.Ficha.KO = true;
			ComandoDeTech(cientista, "colher_dna", "");
			Checa("...e nao se colhe de longe: a agulha e de contato (`oview(1)` no original)",
				  lab.Fornada == null || lab.Fornada.Amostras.Count == 0);

			// ---- 5. O CONTROLE: o MESMO corpo, perto, colhe ----
			// Ele vem por ultimo e e o mesmo boneco: se as quatro recusas acima viessem de o
			// laboratorio estar quebrado, esta linha ficaria vermelha e denunciaria a bancada.
			longe.Pos = cientista.Pos;
			ComandoDeTech(cientista, "colher_dna", "");
			Checa("O CONTROLE: o MESMO corpo, agora ao alcance e sem nenhum dos tres defeitos, E "
				  + "colhido -- sem esta linha as quatro recusas acima poderiam ser um laboratorio "
				  + "quebrado",
				  lab.Fornada is { } fo && fo.Amostras.Count == 1
				  && fo.Amostras[0].Doador == "Caido Longe",
				  $"{lab.Fornada?.Amostras.Count} amostra(s)");
			longe.Ficha.KO = false;
		}
	}

	/// <summary>Um mainframe aparafusado com o Bio-Android Lab ja instalado, no lugar de quem pediu.</summary>
	private Obra PorUmLabDeBio(ServerPlayer dono, int id, List<Obra> obras)
	{
		var o = new Obra
		{
			Id = id, Tipo = "Android_Creation_Mainframe", Aparafusada = true, Lab = 2,
			X = dono.Pos.X, Y = dono.Pos.Y, DonoConta = dono.Conta, DonoNome = dono.Name,
		};
		o.PorZona(dono.Zone);
		_noChao.Add(o);
		obras.Add(o);
		return o;
	}

	// =====================================================================
	// 10. O SEGUNDO BIO -- **SEM** DNA SAIYAJIN
	// =====================================================================
	/// <summary>
	/// ============================ O CONTRA-EXEMPLO PRECISA SER UM BIO DE VERDADE ============================
	/// Tudo o que o dono pediu sobre Saiyajin ("capacidade de virar SUPER SAIYAJIN se tiver dna
	/// saiyajin") tem duas metades, e a segunda nao se mede num humano: um humano nao vira Super
	/// Saiyajin por dez motivos, e nove deles nao sao o DNA. O contra-exemplo honesto e **outro
	/// bio-androide de laboratorio**, nascido do mesmo tanque, pela mesma porta, diferindo do primeiro
	/// em UMA coisa: o sangue do doador.
	///
	/// ELE TAMBEM SERVE PRA OUTRA COISA que o primeiro nao podia medir: a escada por JOGADORES. O
	/// primeiro bio subiu pelo atalho do androide (um corpo, um degrau), e com isso a regra dos dez
	/// -- a que um jogador de verdade vai viver -- nunca era exercitada, nem no sentido de subir nem
	/// no de NAO subir com nove.
	/// ====================================================================================================
	/// </summary>
	private ServerPlayer? MedirOBioSemDnaSaiyajin(Checagem Checa, ServerPlayer host,
												  List<ServerPlayer> forjados, List<Obra> obras)
	{
		GD.Print("--- 10. O SEGUNDO BIO, SEM DNA SAIYAJIN ---");

		ServerPlayer? cientista = ForjarCorpo(host, "Human", "Dr. Sem Sangue", forjados, PalcoDoSemDna);
		if (cientista == null) { Checa("PRECONDICAO: o segundo cientista nasceu", false); return null; }
		cientista.Ficha.techskill = 99;
		Obra lab = PorUmLabDeBio(cientista, 984_011, obras);

		// O DOADOR NAO TEM NADA: nem sangue Saiyajin, nem uma tecnica no livro. Ele e o zero contra o
		// qual a heranca do primeiro bio se le.
		ServerPlayer? doador = ForjarCorpo(host, "Human", "Doador Vazio", forjados, PalcoDoSemDna);
		if (doador == null) { Checa("PRECONDICAO: o doador vazio nasceu", false); return null; }
		doador.Livro = new Jandirus.Core.Skills.SkillBook();
		doador.Ficha.BP = 8_000_000;
		doador.Ficha.KO = true;

		ComandoDeTech(cientista, "colher_dna", "");
		ComandoDeTech(cientista, "gestar", "");
		Checa("a segunda fornada fechou SEM sangue Saiyajin (`brew_has_saiyan` apagado)",
			  lab.Fornada is { TemSaiyajin: false }, $"{lab.Fornada?.TemSaiyajin}");
		Checa("...e sem tecnica nenhuma pra herdar (`brew_verbs` vazio)",
			  lab.Fornada is { } f0 && f0.Tecnicas.Count == 0, $"{lab.Fornada?.Tecnicas.Count}");

		doador.Ficha.KO = false;
		lab.Fornada!.PrometidaEm = NowMs() - 1;
		TickDaGestacao();

		if (!BioAndroids.EhBio(cientista.Race))
		{ Checa("o segundo bio-androide nasceu", false, cientista.Race); return null; }
		Checa("o SEGUNDO bio-androide nasceu, pela mesma porta e do mesmo tanque",
			  BioAndroids.EhBio(cientista.Race));
		Checa("...e ele nasce SEM `canSSJ` -- e o UNICO campo em que ele difere do primeiro",
			  !cientista.Ficha.canSSJ);
		Checa("...e sem o SSJ1 possuido de nascenca", !cientista.Forma.Despertou("ssj1"));

		// ============================ A ESCADA POR JOGADORES, OS DOIS SENTIDOS EM CADA DEGRAU ============================
		ServerPlayer bio = cientista;

		// --- degrau 1: LARVA -> IMPERFEITO, e o requisito e ENTERRADA 5 MIN (o adendo do dono; era o dia in-game do DM) ---
		UsarHabilidade(bio, "enterrar");
		AdiantoDoRelogioDeTeste += (long)(BioAndroids.SegundosEnterradoPraAmadurecer * 1000) - 30_000;
		TickDaLarva();
		Checa("LARVA -> IMPERFEITO **NAO** acontece antes dos 5 min enterrada -- sem esta metade, uma conta zerada daria a mesma linha "
			  + "verde",
			  bio.Ficha.bio_stage == BioAndroids.Larva, $"{bio.Ficha.bio_stage}");
		AdiantoDoRelogioDeTeste += 31_000;
		TickDaLarva();
		Checa("...e acontece neles", bio.Ficha.bio_stage == BioAndroids.Imperfeito,
			  $"{bio.Ficha.bio_stage}");

		// --- degrau 2: IMPERFEITO -> SEMI, e o requisito e o SEXTO PASSO (o adendo do dono) ---
		ServerPlayer? Cidadao(string nome)
		{
			ServerPlayer? n = NascerNpc("cidadao", bio.Zone, bio.Pos, ++_lugarDaBancadaDeBio);
			if (n == null) return null;
			forjados.Add(n);
			n.Name = nome;
			n.Ficha.KO = true;
			bio.AlvoId = n.Id;
			_prontoAbsorver.Remove(bio.Id);
			return n;
		}

		bio.Ficha.bio_marco = BioAndroids.MarcosPorForma;
		bio.Ficha.bio_passos = 0.6;
		double bpAntes = bio.Ficha.BP;
		if (Cidadao("Quinto") != null)
		{
			AbsorverAteOFimBio(bio);
			Checa("IMPERFEITO -> SEMI-PERFEITO **NAO** acontece antes do SEXTO passo (marco 5 e 0,6 + um NPC = 0,8) -- sem esta metade a "
				  + "escada estaria aberta a qualquer contagem",
				  bio.Ficha.bio_stage == BioAndroids.Imperfeito && bio.Ficha.bio_marco == BioAndroids.MarcosPorForma
				  && Math.Abs(bio.Ficha.bio_passos - 0.8) < 1e-9,
				  $"estagio {bio.Ficha.bio_stage}, marco {bio.Ficha.bio_marco}, passos {bio.Ficha.bio_passos}");
			Checa("...e o BP BASE nao se mexeu (o imperfeito nao tem marcos: os passos andam, o multiplicador nao)",
				  Math.Abs(bio.Ficha.BP - bpAntes) < 1, $"{bio.Ficha.BP:N0}");
		}

		bpAntes = bio.Ficha.BP;
		if (Cidadao("Sexto") != null)
		{
			AbsorverAteOFimBio(bio);
			Checa("...e ACONTECE no passo que fecha o sexto (0,8 + 0,2), pelo mesmo verbo",
				  bio.Ficha.bio_stage == BioAndroids.SemiPerfeito && bio.Ficha.bio_marco == 0, $"{bio.Ficha.bio_stage} marco {bio.Ficha.bio_marco}");
			Checa("...com o BP BASE dobrando (do imperfeito, 1x, ao semi, 2x)",
				  Math.Abs(bio.Ficha.BP - bpAntes * BioAndroids.MultDoSemiPerfeito) < 1, $"{bio.Ficha.BP:N0} != {bpAntes * 2:N0}");
			Checa("...e os passos ZERADOS -- sem isto o proximo NPC daria o marco seguinte de brinde", bio.Ficha.bio_passos < 1e-9);
		}

		// --- um MARCO dentro do semi: 2x -> 2,2x ---
		bio.Ficha.bio_passos = 0.8;
		bpAntes = bio.Ficha.BP;
		if (Cidadao("Marco") != null)
		{
			AbsorverAteOFimBio(bio);
			Checa("um MARCO dentro do semi-perfeito: o marco 1 leva o corpo de 2x a 2,2x -- o BP base anda pela RAZAO (1,1)",
				  bio.Ficha.bio_stage == BioAndroids.SemiPerfeito && bio.Ficha.bio_marco == 1
				  && Math.Abs(bio.Ficha.BP - bpAntes * 1.1) < 1, $"marco {bio.Ficha.bio_marco}, BP {bio.Ficha.BP:N0} contra {bpAntes * 1.1:N0}");
		}

		// --- degrau 3: SEMI -> PERFEITO, a mesma escada, e a razao do marco 5 do semi (3x) pro perfeito (4x) ---
		bio.Ficha.bio_marco = BioAndroids.MarcosPorForma;
		bio.Ficha.bio_passos = 0.8;
		bpAntes = bio.Ficha.BP;
		if (Cidadao("Vigesimo") != null)
		{
			AbsorverAteOFimBio(bio);
			Checa("SEMI -> PERFEITO pela mesma escada (o sexto passo depois do marco 5)",
				  bio.Ficha.bio_stage == BioAndroids.Perfeito && bio.Ficha.bio_marco == 0, $"{bio.Ficha.bio_stage} marco {bio.Ficha.bio_marco}");
			Checa("...com o BP BASE pela razao do semi no marco 5 (3x) pro perfeito (4x): 4/3 -- e nao os 4x do DM",
				  Math.Abs(bio.Ficha.BP - bpAntes * BioAndroids.MultDoPerfeito / BioAndroids.MultAbsoluto(BioAndroids.SemiPerfeito, 5)) < 1,
				  $"{bio.Ficha.BP:N0} != {bpAntes * 4 / 3:N0}");
		}
		return bio;
	}

	/// <summary>
	/// (2026-10-17) A ABSORCAO DE EXECUCAO, PELA PORTA E ATE O FIM: o verb abre os 5 s (`AbsorverBio`), o relogio anda e o tique de producao a
	/// consuma (`TickDasExecucoesDoBio`). Devolve se ela se consumou.
	/// </summary>
	private bool AbsorverAteOFimBio(ServerPlayer bio)
	{
		UsarHabilidade(bio, "absorver");
		if (!_execucoesDoBio.ContainsKey(bio.Id)) return false;
		AdiantoDoRelogioDeTeste += (long)(BioAndroids.SegundosDaExecucao * 1000) + 50;
		TickDasExecucoesDoBio();
		return !_execucoesDoBio.ContainsKey(bio.Id);
	}

	// =====================================================================
	// 11. A HERANCA: O QUE VEIO DA PESSOA E O QUE VEIO DA RACA
	// =====================================================================
	/// <summary>
	/// ============================ AS DUAS METADES DA FRASE DO DONO, NOMEADAS ============================
	/// *"pegar as SKILLS, HABILIDADES RACIAIS etc das PESSOAS e RACA q ele tem o dna"*. Aqui elas tem
	/// nome, e a pergunta e feita pelo <see cref="SabeTecnica"/> -- que e o gate de PRODUCAO, o mesmo
	/// que decide se o botao responde quando o jogador aperta. Perguntar ao `Livro.Sabe` provaria que
	/// o typepath foi copiado; perguntar ao `SabeTecnica` prova que a HABILIDADE existe.
	///
	/// E O CONTRA-EXEMPLO E O SEGUNDO BIO, que nasceu do mesmo tanque com um doador vazio: se as duas
	/// aparecessem nele tambem, elas nao teriam vindo do DNA -- viriam do nascimento.
	/// =================================================================================================
	/// </summary>
	private void MedirAHeranca(Checagem Checa, ServerPlayer bio, ServerPlayer? semDna)
	{
		GD.Print("--- 11. A HERANCA (a skill da PESSOA e a racial da RACA) ---");

		Checa($"A SKILL QUE VEIO DA PESSOA: o bio responde ao verbo `{VerboDaPessoa}` (Solar Flare), "
			  + "que o doador Saiyajin sabia -- e a pergunta e feita pelo `SabeTecnica`, o mesmo gate "
			  + "que atende o botao do jogador",
			  SabeTecnica(bio, VerboDaPessoa));

		Checa($"A HABILIDADE QUE VEIO DA RACA: o bio responde ao verbo `{VerboDaRaca}` (Fusion- Namek "
			  + "Style), que pendura no `tree/namek` e em arvore nenhuma alem dela -- ele so pode "
			  + "te-la pelo sangue do doador Namekuseijin",
			  SabeTecnica(bio, VerboDaRaca));

		// (v0.3.0) O LIVRO INTEIRO ATRAVESSA, e nao so os botoes: o dono (2026-10-15) -- *"todas as skills da aba learn dos
		// jogadores q tiveram o dna usado, do mesmo nivel q eles"*. Ate aqui esta linha dizia o contrario (o `Keyableverbs` do DM).
		Checa("...e herdou TAMBEM a `Stretchy Arms` do MESMO galho racial, que nao tem botao -- a agulha copia o LIVRO do "
			  + "doador inteiro (regra do dono, 2026-10-15; no DM so iam os `Keyableverbs`)",
			  bio.Livro.Sabe(SkillPassivaDaRaca));

		// (v0.3.0) E A ARVORE DO NAMEKUSEIJIN VEM PELO SANGUE: a raca com sangue do bio e "BioAndroid+...+Namekian", e a
		// racial de cada parte abre (`SkillCatalog.ArvoresDe`). Ate aqui esta linha dizia o contrario (o `generatetrees` do DM).
		string comSangue = Jandirus.Core.Races.SangueDoBio.RacaComSangue(bio.Race, bio.Ficha);
		Checa("...e herdou a ARVORE do Namekuseijin pelo sangue (regra do dono: *\"ele pega as passivas e skills das racas "
			  + "q foram usadas pra criar ele\"*) -- a raca com sangue abre a racial de cada doadora, e a do proprio bio continua",
			  _skills!.ArvoresDe(comSangue, "").Any(s => s.Path.Contains("namek", StringComparison.OrdinalIgnoreCase))
			  && _skills.ArvoresDe(comSangue, "").Any(s => s.Path.Contains("bioandroid", StringComparison.OrdinalIgnoreCase)),
			  $"{comSangue}: " + string.Join(", ", _skills.ArvoresDe(comSangue, "").Select(s => s.Path)));

		Checa("...e as duas herdadas estao marcadas como ENSINADAS: ele SABE usar e nao sabe "
			  + "REPASSAR (o laboratorio nao pode virar lavanderia de skill)",
			  bio.Livro.FoiEnsinada(SkillDaPessoa) && bio.Livro.FoiEnsinada(SkillDaRaca));

		if (semDna == null) { Checa("PRECONDICAO: ha um bio SEM DNA pra servir de contra-exemplo", false); return; }

		Checa($"O CONTRA-EXEMPLO: o bio nascido do doador VAZIO **nao** responde a `{VerboDaPessoa}` "
			  + "-- sem o DNA a tecnica nao aparece, e e isso que prova que ela veio dele",
			  !SabeTecnica(semDna, VerboDaPessoa));
		Checa($"...nem a `{VerboDaRaca}`", !SabeTecnica(semDna, VerboDaRaca));
	}

	// =====================================================================
	// 12. O ZENKAI
	// =====================================================================
	/// <summary>
	/// ============================ "TEM ZENKAI" NAO E `HasZenkai() == true` ============================
	/// A linha que ja existia nesta bancada le o predicado. Um predicado que devolve `true` e uma
	/// opiniao: quem paga o Zenkai e o <see cref="ZenkaiPorDerrota"/>, e ele so e chamado pelo funil
	/// <see cref="AoPerderALuta"/>. Entre o predicado e o BP subindo ha quatro guardas (inimigo mais
	/// forte, teto de aposentadoria, recarga de uma hora, morte) e um funil inteiro -- e uma delas
	/// bastaria pra deixar o bio sem Zenkai com o predicado verde.
	///
	/// Aqui o bio PERDE uma luta de verdade e a bancada mede o **BP subindo**.
	///
	/// E HA DOIS CONTRA-EXEMPLOS: um HUMANO, e o bio SEM DNA Saiyajin. No DM o Zenkai do bio vinha da RACA
	/// (`combatgains.dm:14` testa a raca, primeira linha da funcao) e todo bio o tinha; desde a v0.3.0 ele vem do
	/// SANGUE (dono, 2026-10-15: *"zenkai se tiver saiyajin"* e *"bio androide sozinho N tem nada"*).
	/// </summary>
	private void MedirOZenkai(Checagem Checa, ServerPlayer host, ServerPlayer bio,
							  ServerPlayer? semDna, List<ServerPlayer> forjados)
	{
		GD.Print("--- 12. O ZENKAI (o efeito, e nao o predicado) ---");

		ServerPlayer? algoz = ForjarCorpo(host, "Saiyan", "O Algoz", forjados);
		if (algoz == null) { Checa("PRECONDICAO: o algoz nasceu", false); return; }
		algoz.Ficha.BP = 1e9;
		algoz.Ficha.Statify();

		// O bio volta pra um BP pequeno: com 1e12 ele estaria APOSENTADO (o teto do Zenkai e o
		// `ssj3LearnReq`), e a bancada mediria a aposentadoria achando que mediu a raca.
		double bpGuardado = bio.Ficha.BP;
		bool koGuardado = bio.Ficha.KO, mortoGuardado = bio.Ficha.dead;
		bio.Ficha.BP = 1_000_000;
		bio.Ficha.dead = false;
		bio.Ficha.zenkaiGuardado = 100_000;   // (2026-10-26) o banco do Zenkai que a derrota solta
		bio.Ficha.Statify();

		// ============================ `Statify()` NAO RECALCULA O PODER EXPRESSO, E ISSO CUSTOU UMA RODADA ============================
		// A primeira rodada desta familia saiu VERMELHA com o sistema certo: o `GainZenkai` compara o
		// poder do vencedor com o `Math.Max(expressedBP, BP)` da vitima, e o `expressedBP` do bio ainda
		// carregava o 1e12 que a familia 6 tinha posto. O algoz de um bilhao virava "mais fraco" e a
		// recusa era `InimigoMaisFraco` -- ou seja, a bancada teria reprovado o Zenkai do bio por um
		// numero velho na ficha dela mesma. Quem recalcula e o `RepercutirPoder`, e ele e a porta de
		// producao (o mesmo que o combate chama).
		// ==========================================================================================================================
		RepercutirPoder(bio);

		double antes = bio.Ficha.BP;
		bio.Ficha.KO = true;
		AoPerderALuta(bio, algoz, morreu: false);
		Checa("O BIO GANHA ZENKAI DE VERDADE: perdendo pro funil de producao (`AoPerderALuta`), o "
			  + "BP BASE dele SOBE -- e ate esta sessao o ramo do bio no `HasZenkai` testava "
			  + "\"Bio-Android\" com hifen contra uma raca gravada \"BioAndroid\" sem: escrito e "
			  + "inalcancavel",
			  bio.Ficha.BP > antes, $"{antes:N0} -> {bio.Ficha.BP:N0}");
		double fatorDoBio = Fighter.FatorDoTetoDoZenkai(antes, RequisitoDoSsj3De(bio));
		Checa("...e o tamanho e o do banco do Zenkai (2026-10-26): de 20 a 80% dos 100.000 guardados, pesados pelo requisito do SSJ3",
			  bio.Ficha.UltimoZenkai >= 20_000 * fatorDoBio - 1 && bio.Ficha.UltimoZenkai <= 80_000 * fatorDoBio + 1,
			  $"{bio.Ficha.UltimoZenkai:N0} (fator {fatorDoBio:0.####})");

		// ---- (v0.3.0) o bio SEM DNA Saiyajin NAO tem: o Zenkai e do SANGUE (dono, 2026-10-15) ----
		if (semDna != null)
		{
			double a2 = semDna.Ficha.BP;
			semDna.Ficha.BP = 1_000_000;
			semDna.Ficha.zenkaiGuardado = 100_000;
			semDna.Ficha.dead = false;
			semDna.Ficha.KO = true;
			semDna.Ficha.Statify();
			RepercutirPoder(semDna);
			double a3 = semDna.Ficha.BP;
			AoPerderALuta(semDna, algoz, morreu: false);
			Checa("...e o bio SEM DNA Saiyajin **nao** ganha: o Zenkai do bio e do SANGUE (dono, 2026-10-15: *\"zenkai se "
				  + "tiver saiyajin\"*). No DM vinha da RACA (`combatgains.dm:14`, primeira linha) e todo bio ganhava",
				  Math.Abs(semDna.Ficha.BP - a3) < 1, $"{a3:N0} -> {semDna.Ficha.BP:N0}");
			semDna.Ficha.KO = false;
			semDna.Ficha.BP = a2;
			semDna.Ficha.Statify();
		}

		// ---- O CONTRA-EXEMPLO: outra raca, mesma derrota, mesmo funil ----
		ServerPlayer? humano = ForjarCorpo(host, "Human", "Humano Comum", forjados);
		if (humano != null)
		{
			humano.Ficha.BP = 1_000_000;
			humano.Ficha.zenkaiGuardado = 100_000;
			humano.Ficha.Statify();
			RepercutirPoder(humano);
			double antesH = humano.Ficha.BP;
			humano.Ficha.KO = true;
			AoPerderALuta(humano, algoz, morreu: false);
			Checa("O CONTRA-EXEMPLO: um HUMANO que perde a MESMA luta pro MESMO algoz no MESMO funil "
				  + "nao ganha nada -- sem esta linha, um `HasZenkai` que devolvesse `true` pra todo "
				  + "mundo passaria pela linha de cima",
				  Math.Abs(humano.Ficha.BP - antesH) < 1
				  && !humano.Ficha.HasZenkai() && humano.Ficha.SoltarOZenkai(false, false, 0) == 0,
				  $"{antesH:N0} -> {humano.Ficha.BP:N0}");
			humano.Ficha.KO = false;
		}

		bio.Ficha.KO = koGuardado;
		bio.Ficha.dead = mortoGuardado;
		bio.Ficha.BP = bpGuardado;
		bio.Ficha.Statify();
	}

	// =====================================================================
	// 13. O SUPER SAIYAJIN, E O SSJ2 QUE O DONO CHAMA DE "SUPER PERFEITO"
	// =====================================================================
	/// <summary>
	/// ============================ O PEDIDO E A MEDIDA NAO SAO A MESMA COISA, E ISSO E O ACHADO ============================
	/// O dono escreveu *"SUPER PERFEITO (ssj2, tem q ter DNA SAIYAJIN)"* -- ou seja, na cabeca dele a
	/// Super Perfeita e o SSJ2 sao a mesma porta e as duas pedem o sangue. **No DM sao duas portas
	/// diferentes e so uma delas pede o sangue:**
	///
	///   * `Cell4()` (`CellFormBuff.dm:73-82`) -- a Super Perfeita. Ela pede a FORMA PERFEITA, poder e
	///     `!ssj`. Nao ha um unico teste de raca nem de DNA no corpo dela. Ela e do CORPO do bio;
	///   * `bio_ssj2_death_check` (`DNALabs.dm:649-700`) -- o SSJ2. **Esse** pede `canSSJ`, que so
	///     existe se houve DNA Saiyajin no tanque -- e pede mais: a forma perfeita, o SSJ1 100%
	///     dominado, e MORRER.
	///
	/// Entao as duas metades que o dono pediu ("com, sobe; sem, recusa") sao medidas aqui na porta em
	/// que elas existem, e a outra e medida dizendo que ela NAO pede -- que e a informacao que ele
	/// nao tem. Mudar o codigo pra caber na frase seria inventar uma regra e chamar de porte.
	/// =================================================================================================================
	/// </summary>
	private void MedirOSuperSaiyajinPeloDna(Checagem Checa, ServerPlayer bio, ServerPlayer? semDna)
	{
		GD.Print("--- 13. O SUPER SAIYAJIN PELO DNA (as duas metades) ---");

		// ---- COM DNA: ele ENTRA na forma, e o multiplicador e o da linha DILUIDA ----
		bio.Ficha.dead = false;
		bio.Ficha.KO = false;
		bio.Ficha.BP = 1e12;
		bio.Ficha.Ki = bio.Ficha.MaxKi;
		bio.Ficha.Statify();
		// ============================ A MAESTRIA VOLTA A ZERO PRA A CONTA SER A DO **DEGRAU CERTO** ============================
		// O SSJ1 tem DOIS multiplicadores (`Mult = [Ssj1Base, 6]`), e a maestria escolhe qual: 1,35x no
		// comeco e 6x no Full Power. A familia 6 desta bancada deixa a maestria em 100% (ela precisa
		// disso pro SSJ2), e a primeira rodada desta linha saiu VERMELHA acusando "6" -- que era a
		// resposta CERTA pra pergunta ERRADA. O que o `nerfSSJ()` rebaixa e a entrada do degrau, entao
		// e nela que se mede.
		// ==================================================================================================================
		double maestriaGuardada = bio.Forma.Maestria.De("ssj1");
		bio.Forma.Maestria.Por("ssj1", 0);
		TransformarPara(bio, Catalogo.IdBase);
		TransformarPara(bio, "ssj1");
		Checa("COM DNA SAIYAJIN o bio ENTRA em Super Saiyajin pelo funil de producao",
			  bio.Forma.Atual == "ssj1", bio.Forma.Atual);
		Checa("...com o multiplicador da linha DILUIDA no PRIMEIRO degrau (1,35x e nao 2x) -- e o "
			  + "`nerfSSJ()` do original, que rebaixa a escada de quem chegou nela por `canSSJ`",
			  Math.Abs(bio.Ficha.ssjBuff - Catalogo.Ssj1BaseDiluido) < 1e-6, $"{bio.Ficha.ssjBuff}");
		TransformarPara(bio, Catalogo.IdBase);
		bio.Forma.Maestria.Por("ssj1", maestriaGuardada);

		if (semDna == null) { Checa("PRECONDICAO: ha um bio SEM DNA pra a outra metade", false); return; }

		// ---- SEM DNA: a linha inteira esta fechada ----
		semDna.Ficha.dead = false;
		semDna.Ficha.KO = false;
		semDna.Ficha.BP = 1e12;
		semDna.Ficha.Ki = semDna.Ficha.MaxKi;
		semDna.Ficha.Statify();
		PerfilDeFormas p = Perfil(semDna);
		Checa("SEM DNA SAIYAJIN a linha Saiyajin nem aparece pra ele (`LinhasAbertas`)",
			  !Catalogo.LinhasAbertas(p).Contains(LinhaDeForma.Saiyajin));
		RecusaForma r = semDna.Forma.Avaliar("ssj1", semDna.Ficha.BP, 1, false, p);
		Checa("...e com poder de sobra ele e RECUSADO na porta", r != RecusaForma.Pode, $"{r}");
		TransformarPara(semDna, "ssj1");
		Checa("...e apertando o verbo ele continua na base -- as duas metades da frase do dono",
			  semDna.Forma.Atual == Catalogo.IdBase, semDna.Forma.Atual);

		// ---- (2026-10-26) A GOLDEN PERFECT E OUTRA PORTA: ela nao pede o sangue, pede o ki divino ----
		GodKiState? kiDivinoDoSemDna = semDna.Ficha.godki;
		semDna.Ficha.godki = new GodKiState { awakened = true, mastery = 0 };
		RecusaForma goldenSemDna = semDna.Forma.Avaliar("golden_perfect", semDna.Ficha.BP, 1, false, Perfil(semDna));
		semDna.Ficha.godki = kiDivinoDoSemDna;
		Checa("...mas a GOLDEN PERFECT continua sendo dele, com o ki divino desperto: e a forma do CORPO, e o bio sem sangue Saiyajin fica "
			  + "com as duas divinas (a escolha do dono)",
			  semDna.Ficha.bio_stage == BioAndroids.Perfeito && goldenSemDna == RecusaForma.Pode,
			  $"estagio {semDna.Ficha.bio_stage}, {goldenSemDna}");

		// ---- E O SSJ2 PELA MORTE: **ESSE** pede o sangue, e a diferenca e UM campo ----
		semDna.Forma.Maestria.Por("ssj1", 100);
		semDna.Ficha.dead = false;
		bool morreu = semDna.Combate!.Morrer();
		Checa("O SSJ2 PELA MORTE **exige** o DNA: com a forma perfeita, SSJ1 100% dominado e poder "
			  + "de sobra, um bio SEM DNA que morre simplesmente MORRE",
			  morreu && semDna.Ficha.dead && !semDna.Forma.Despertou("ssj2"));

		// A MESMA MORTE, MUDANDO **UM** CAMPO -- e por isso ela prova o campo e nao o roteiro.
		semDna.Combate.Reviver();
		semDna.Ficha.dead = false;
		semDna.Ficha.canSSJ = true;
		semDna.Ficha.bio_saiyan_dna = true;
		bool morreu2 = semDna.Combate.Morrer();
		Checa("...e a MESMA morte, com o DNA ligado e mais nada mudado, vira o DESPERTAR do SSJ2 -- "
			  + "as duas metades da porta que o dono chama de 'Super Perfeito (ssj2)'",
			  !morreu2 && !semDna.Ficha.dead && semDna.Forma.Despertou("ssj2"));

		// ============================ E ELE VOLTA **DENTRO** DA FORMA, QUE E O PEDIDO LITERAL ============================
		// O dono: *"ele vai CANCELAR A MORTE e voltar a vida de forma INSTANTANEA so q agr no SSJ2"*.
		//
		// A LINHA DE CIMA SOZINHA DAVA VERDE COM O DEFEITO NA TELA, e foi o que aconteceu por uma
		// auditoria inteira: `Despertou` responde pelo `Liberar` -- a forma DESTRAVADA no menu --, e o
		// port parava exatamente ai. O jogador se recompunha na BASE, com um item novo pra apertar.
		// O DM nao para: `bio_ssj2_awaken` termina em `SSj2()` (`DNALabs.dm:697`), que aplica a forma.
		//
		// SAO DUAS PERGUNTAS E NAO UMA, porque elas quebram separadas: `Atual` prova que ele ENTROU, e
		// o `ssjBuff` prova que o poder da forma foi aplicado ao corpo (um `Atual` escrito sem o
		// `AplicarForma` seria um rotulo).
		// ==========================================================================================================
		Checa("...e ele volta a vida **DENTRO** do SSJ2, e nao so com a forma destravada no menu",
			  semDna.Forma.Atual == "ssj2", semDna.Forma.Atual);
		Checa("...com o multiplicador da forma ja no corpo (o `SSj2()` do DM, e nao um rotulo)",
			  semDna.Ficha.ssjBuff > 1, $"ssjBuff {semDna.Ficha.ssjBuff:0.###}");

		TransformarPara(semDna, Catalogo.IdBase);
		semDna.Ficha.canSSJ = false;
		semDna.Ficha.bio_saiyan_dna = false;
	}

	// =====================================================================
	// 14. OS NUMEROS, CONTRA O DM
	// =====================================================================
	// =====================================================================
	// 14. O FOLEGO NO VACUO VEM DO DNA -- AS DUAS METADES, DOIS PARTOS
	// =====================================================================
	/// <summary>
	/// *"bio androides pegam a capacidade de respirar no espaco caso uma das racas q esta em seu dna
	/// consiga (lembrando q as racas q podem respirar no espaco sao majin e frost demon)"*.
	///
	/// ============================ ELA NASCE DOIS BIOS, E NAO REAPROVEITA OS DE CIMA ============================
	/// Os dois bios das familias 2 e 10 chegariam aqui depois de morrer, ressuscitar, absorver dez
	/// cidadaos e trocar de forma quatro vezes -- e uma familia que medisse "o primeiro bio sufoca"
	/// em cima daquele estado responderia por acidente no dia em que alguem mexesse na familia 6.
	/// Aqui os dois nascem do zero, no mesmo minuto, pela mesma porta, e diferem em UMA coisa: o
	/// sangue do doador. E isso e a frase inteira do pedido.
	/// =======================================================================================================
	///
	/// ============================ O DOADOR QUE RESPIRA E UM MEIO-SANGUE, DE PROPOSITO ============================
	/// Ele tem `Race = "Human"` e `Parent_Race = "Majin"`, e e o caso DIFICIL: um doador Majin puro
	/// passaria mesmo se a agulha jogasse fora a raca do pai (foi o que ela fazia antes deste
	/// trabalho -- `ColherDna` so gravava `vitima.Race`). O meio-Majin so respira se a cadeia inteira
	/// estiver honesta: a agulha guardando o pai, a derivacao perguntando pelos dois e a regra do Core
	/// respondendo `Race == X || Parent_Race == X`.
	/// =========================================================================================================
	///
	/// ============================ E A ULTIMA PERGUNTA E A DO TIQUE, NAO A DO CAMPO ============================
	/// `bio_dna_respira == true` e INTENCAO. O que o jogador vive e `SufocaAgora`, a pergunta que o
	/// `TickDoVacuo` faz uma vez por segundo -- e e ela que esta familia chama, com o corpo posto no
	/// vacuo. Quanta vida isso custa e medido na outra bancada (`--vacuoteste`, familia 11), que tem a
	/// maquina de fotografar o mundo antes de bater nele.
	/// ======================================================================================================
	/// </summary>
	private void MedirOFolegoNoVacuoPeloDna(Checagem Checa, ServerPlayer host,
											List<ServerPlayer> forjados, List<Obra> obras)
	{
		GD.Print("--- 14. O FOLEGO NO VACUO VEM DO DNA ---");

		// AS PRECONDICOES SAO A METADE QUE IMPEDE A FAMILIA DE SER VACUA: se um dia "Majin" sair da
		// lista do dono, tudo abaixo continuaria "funcionando" e medindo nada.
		Checa("PRECONDICAO: a regra do Core diz que MAJIN respira no vacuo",
			  Vacuo.RacaRespira("Majin"), string.Join(", ", Vacuo.RacasQueRespiram));
		Checa("PRECONDICAO: ...e que FROST DEMON respira -- a SEGUNDA raca que o dono nomeou, na "
			  + "grafia do proto ('Icer'), que e a que um doador de verdade carrega no `Race`",
			  Vacuo.RacaRespira(FormasDeFrost.Raca));
		Checa("PRECONDICAO: ...e que HUMANO nao respira (a outra metade)", !Vacuo.RacaRespira("Human"));
		Checa("PRECONDICAO: e a raca 'BioAndroid' NAO esta nessa lista -- o folego tem que vir do DNA "
			  + "e nao do cracha, senao os dois bios abaixo respirariam pelo mesmo motivo",
			  !Vacuo.RacaRespira(BioAndroids.Raca) && !Vacuo.RacaRespira(BioAndroids.RacaDoDm));

		// UM BIO POR RACA QUE O DONO NOMEOU, e os dois entram por PORTAS DIFERENTES da mesma regra:
		// o Majin chega pelo PAI do doador (`Parent_Race`, o meio-sangue) e o Frost Demon chega pela
		// RACA do doador. Uma familia com so um deles ficaria verde com metade do `Race == X ||
		// Parent_Race == X` apagada -- e cada metade e a que cobre uma das duas racas do pedido.
		ServerPlayer? comAr = UmBioDeUmDoadorSo(Checa, host, "com DNA que respira", PalcoDoFolego,
												984_012, forjados, obras, "Human", "Majin");
		ServerPlayer? doFrio = UmBioDeUmDoadorSo(Checa, host, "de doador Frost Demon", PalcoDoFrio,
												 984_014, forjados, obras, FormasDeFrost.Raca, "");
		ServerPlayer? semAr = UmBioDeUmDoadorSo(Checa, host, "de doador que nao respira", PalcoDoSufoco,
												984_013, forjados, obras, "Human", "");
		if (comAr == null || semAr == null || doFrio == null) return;

		Checa("o bio de DNA que respira nasce com o folego herdado (`bio_dna_respira`)",
			  comAr.Ficha.bio_dna_respira);
		Checa("...e o de doador FROST DEMON tambem -- a outra raca do pedido, e pela outra metade da "
			  + "regra (a `Race` do doador, e nao a `Parent_Race`)",
			  doFrio.Ficha.bio_dna_respira);
		Checa("...e o de DNA humano nasce SEM ele -- no DM os DOIS respirariam, porque la o folego e "
			  + "da raca e vem de graca (`statbiodroid.dm:51`, `\"Space Breath\" = 1` no proto)",
			  !semAr.Ficha.bio_dna_respira);

		Checa("...e no corpo de nenhum dos dois sobrou vestigio do doador: raca e raca-do-pai viraram "
			  + "'BioAndroid' e o genoma foi destruido. Ou o bit e gravado no parto, ou a informacao "
			  + "deixa de existir -- e e por isso que ele e um campo e nao uma pergunta",
			  comAr.Ficha.Race == BioAndroids.Raca && comAr.Ficha.ParentRace == BioAndroids.Raca
			  && comAr.Ficha.Genoma == null
			  && semAr.Ficha.Race == semAr.Ficha.ParentRace,
			  $"{comAr.Ficha.Race}/{comAr.Ficha.ParentRace}");

		// ---- E AGORA O QUE O JOGADOR VIVE ----------------------------------
		void NoVacuo(string oQue, ServerPlayer pl, bool esperaSufocar)
		{
			if (pl.Combate == null || pl.Ficha.dead)
			{ Checa($"{oQue} [PRECONDICAO: o corpo esta vivo e montado]", false); return; }

			ZoneKey guardada = pl.Zone;
			try
			{
				pl.Zone = ZonaDoEspaco;
				bool sufoca = SufocaAgora(pl);
				Checa(oQue, sufoca == esperaSufocar, $"SufocaAgora={sufoca}");
			}
			finally { pl.Zone = guardada; }
		}

		NoVacuo("NO VACUO, o servidor decide que o bio de DNA que respira **nao** sufoca "
				+ "(`SufocaAgora`, a pergunta que o tique faz uma vez por segundo)", comAr, false);
		NoVacuo("...e que o de DNA FROST DEMON tambem **nao** sufoca -- as duas racas que o dono "
				+ "nomeou, medidas no mesmo lugar e pela mesma pergunta", doFrio, false);
		NoVacuo("...e que o de DNA humano SUFOCA, ali do lado -- mesma raca, mesma ParentRace, mesmo "
				+ "estagio, mesma zona: a unica diferenca entre os dois esta no tanque de onde sairam",
				semAr, true);

		// ---- E O TRAJE CONTINUA VALENDO PRO QUE NAO HERDOU -----------------
		// O folego novo entra por um `||`, entao ele nao pode ter FECHADO nenhuma das outras portas.
		// Sem esta linha, um `if/else` escrito no lugar do `||` passaria em tudo acima.
		if (semAr.Mochila.Quantos(CatalogoDeItens.Traje) == 0)
		{
			semAr.Mochila.Guardar(CatalogoDeItens.Traje, 1);
			NoVacuo("...e o que nao herdou continua salvo pela ROUPA ESPACIAL (o folego do DNA entrou "
					+ "num `||`, e nao no lugar dos outros abrigos)", semAr, false);
			semAr.Mochila.Tirar(CatalogoDeItens.Traje, 1);
			NoVacuo("...e volta a sufocar quando a roupa sai da mochila", semAr, true);
		}
		else Checa("PRECONDICAO: o bio nasceu sem roupa espacial na mochila", false);
	}

	/// <summary>
	/// UM BIO-ANDROIDE INTEIRO PELA CADEIA DE PRODUCAO, num canto do mapa so dele: cientista forjado,
	/// laboratorio, UM doador com a raca pedida, `colher_dna`, `gestar` e o parto pelo `TickDaGestacao`.
	///
	/// **UM DOADOR SO**, e isso e o que torna a resposta legivel: com dois no tanque, um
	/// `bio_dna_respira` aceso nao diz de quem veio. E o canto proprio nao e capricho -- `LabDeBio`
	/// pergunta ao `ObraPerto`, e dois laboratorios colados fazem o novo achar o vizinho velho (ver a
	/// nota do `palco` em <see cref="ForjarCorpo"/>).
	/// </summary>
	private ServerPlayer? UmBioDeUmDoadorSo(Checagem Checa, ServerPlayer host, string rotulo,
											Vec2 palco, int idObra, List<ServerPlayer> forjados,
											List<Obra> obras, string racaDoDoador, string paiDoDoador)
	{
		ServerPlayer? cientista = ForjarCorpo(host, "Human", $"Dr. {rotulo}", forjados, palco);
		if (cientista == null) { Checa($"PRECONDICAO: o cientista ({rotulo}) nasceu", false); return null; }
		cientista.Ficha.techskill = 99;
		Obra lab = PorUmLabDeBio(cientista, idObra, obras);

		ServerPlayer? doador = ForjarCorpo(host, racaDoDoador, $"Doador {rotulo}", forjados, palco);
		if (doador == null) { Checa($"PRECONDICAO: o doador ({rotulo}) nasceu", false); return null; }
		doador.Ficha.ParentRace = paiDoDoador;          // `ForjarCorpo` iguala os dois; aqui ele e meio-sangue
		doador.Livro = new Jandirus.Core.Skills.SkillBook();
		doador.Ficha.BP = 6_000_000;
		doador.Ficha.KO = true;

		ComandoDeTech(cientista, "colher_dna", "");
		Amostra? a = lab.Fornada?.Amostras.FirstOrDefault();
		Checa($"({rotulo}) a agulha pegou o doador", a != null);
		if (a == null) return null;

		Checa($"({rotulo}) a amostra guarda a raca do doador E a do PAI dele -- sem a segunda, um "
			  + "meio-Majin entraria no tanque rotulado 'Human' e o DNA se perderia na agulha",
			  a.Raca == racaDoDoador && a.RacaDoPai == paiDoDoador, $"{a.Raca}/{a.RacaDoPai}");

		ComandoDeTech(cientista, "gestar", "");
		doador.Ficha.KO = false;
		if (lab.Fornada == null) { Checa($"({rotulo}) a gestacao comecou", false); return null; }

		lab.Fornada.PrometidaEm = NowMs() - 1;
		TickDaGestacao();

		if (!BioAndroids.EhBio(cientista.Race))
		{ Checa($"({rotulo}) o bio-androide nasceu", false, cientista.Race); return null; }
		Checa($"({rotulo}) o bio-androide nasceu, pela mesma porta e do mesmo tanque",
			  BioAndroids.EhBio(cientista.Race));
		return cientista;
	}

	// =====================================================================
	// 16. O SANGUE E O LIVRO DOS DOADORES (v0.3.0, itens 24, 25 e 25b)
	// =====================================================================
	/// <summary>
	/// O BIO HERDA O SANGUE E O LIVRO DOS DOADORES, pela cadeia inteira (agulha, gestacao, parto) e com DOIS doadores no mesmo
	/// tanque: um MAJIN que sabe o Kicker no nivel 3 e inventou uma tecnica, e um ANDROIDE de energia infinita. O dono
	/// (2026-10-15): *"bio androide sozinho N tem nada, ele pega as passivas e skills das racas q foram usadas pra criar ele,
	/// zenkai se tiver saiyajin, regeneracao e semi imortalidade se tiver majin"*, e *"vem com todas as skills da aba learn dos
	/// jogadores q tiveram o dna usado, do mesmo nivel q eles, e as tecnicas customizadas tb e as liberadas"*.
	///
	/// AS REGRAS PURAS (a raca com sangue, o empate da regeneracao, o sangue da fornada) sao da `--v030teste`, familia X.
	/// </summary>
	private void MedirOSangueEOLivroDosDoadores(Checagem Checa, ServerPlayer host, List<ServerPlayer> forjados, List<Obra> obras)
	{
		GD.Print("--- 16. O SANGUE E O LIVRO DOS DOADORES (v0.3.0) ---");
		const string Kicker = "/datum/skill/MartialSkill/Kicker";

		ServerPlayer? cientista = ForjarCorpo(host, "Human", "Dr. Sangue", forjados, PalcoDoSangue);
		ServerPlayer? majin = ForjarCorpo(host, "Majin", "Doador Majin", forjados, PalcoDoSangue);
		ServerPlayer? droide = ForjarCorpo(host, "Android", "Doador Perpetuo", forjados, PalcoDoSangue);
		if (cientista == null || majin == null || droide == null) { Checa("PRECONDICAO: o cientista e os dois doadores nasceram", false); return; }
		cientista.Ficha.techskill = 99;
		// AS DO CRIADOR MORREM COM ELE: uma tecnica inventada por ele, pra o parto ter o que apagar.
		cientista.Customizadas.Add(new Jandirus.Core.Skills.TecnicaCustomizada { Id = 1, Nome = "Do Criador", Criada = true });
		Obra lab = PorUmLabDeBio(cientista, 984_030, obras);

		// O LIVRO DO MAJIN: o Kicker comprado na aba de aprendizado, no nivel 3, uma tecnica inventada e um rascunho que ele
		// nunca confirmou (este nao atravessa a agulha).
		majin.Livro = new Jandirus.Core.Skills.SkillBook();
		majin.Livro.Dar(Kicker);
		majin.Niveis = new Jandirus.Core.Skills.NiveisDeSkill();
		majin.Niveis.Por(Kicker, 3);
		majin.Customizadas.Add(new Jandirus.Core.Skills.TecnicaCustomizada { Id = 2, Nome = "Raio do Majin", Criada = true });
		majin.Customizadas.Add(new Jandirus.Core.Skills.TecnicaCustomizada { Id = 3, Nome = "Rascunho do Majin" });
		droide.Livro = new Jandirus.Core.Skills.SkillBook();
		droide.Ficha.AndroideInfinito = true;

		majin.Ficha.KO = true;
		ComandoDeTech(cientista, "colher_dna", "");
		majin.Ficha.KO = false;
		// O SEGUNDO DOADOR ENTRA PELA MESMA `AmostraDe` DA AGULHA, posto direto no tanque: a agulha o recusaria por ASSINATURA --
		// corpo forjado nao tem uma (`ServerPlayer.Assinatura` e vazia com `Slot < 0`, e e o certo: com `Slot >= 0` o `Persistir`
		// gravaria o boneco dentro da conta do host), e "uma amostra por pessoa" ve dois vazios iguais. A familia 7 contorna isso
		// com o proprio host de doador; aqui o que se mede e o PARTO.
		lab.Fornada?.Amostras.Add(AmostraDe(droide));
		Amostra? daMajin = lab.Fornada?.Amostras.FirstOrDefault(a => a.Doador == majin.Name);
		Checa("(16) a agulha pegou os dois -- e a amostra do Majin guarda o LIVRO, o NIVEL e a tecnica CONFIRMADA (o rascunho fica com ele)",
			  lab.Fornada?.Amostras.Count == 2 && daMajin != null && daMajin.Skills.Contains(Kicker)
			  && daMajin.Niveis.GetValueOrDefault(Kicker) == 3 && daMajin.Customizadas.Count == 1 && daMajin.Customizadas[0].Nome == "Raio do Majin",
			  $"{lab.Fornada?.Amostras.Count} amostra(s); skills [{string.Join(", ", daMajin?.Skills ?? [])}]; nivel {daMajin?.Niveis.GetValueOrDefault(Kicker)}; customizadas {daMajin?.Customizadas.Count}");
		if (lab.Fornada == null || daMajin == null) return;

		ComandoDeTech(cientista, "gestar", "");
		if (lab.Fornada == null) { Checa("(16) a gestacao comecou", false); return; }
		lab.Fornada.PrometidaEm = NowMs() - 1;
		List<string> parto = Ouvir(TickDaGestacao);
		if (!BioAndroids.EhBio(cientista.Race)) { Checa("(16) o bio-androide nasceu", false, cientista.Race); return; }
		ServerPlayer bio = cientista;

		Checa("**O SANGUE DAS DOADORAS**: o bio de um Majin e de um androide perpetuo carrega as duas racas, na ordem das amostras",
			  SangueDoBio.Texto(bio.Ficha) == "Majin+Android", SangueDoBio.Texto(bio.Ficha));
		Checa("**O MAJIN NO SANGUE**: a semi-imortalidade (se desfaz em vez de morrer, fora do ki), o nao sangrar, o nao mancar e o vigor no cheio -- *\"regeneracao e semi imortalidade se tiver majin\"*",
			  CorpoDeMajin(bio) && Jandirus.Core.Combat.Feridas.NaoSangra(RacaComSangue(bio))
			  && Jandirus.Core.Combat.DebuffsDeMembro.SemAtrasoDeFerimento(RacaComSangue(bio)) && Nutricao.VigorSempreCheio(bio.Ficha));
		Checa("...e a REGENERACAO dele: cura em combate e o membro volta (o perfil do Majin, a doadora que mais regenera)",
			  bio.Combate.Corpo.Regen is { CuraEmCombate: true } && bio.Combate.Corpo.RegeneraDecepado, $"{bio.Combate.Corpo.Regen}");
		Checa("**O MOTOR DO ANDROIDE NO SANGUE**: o vigor E o Ki nunca baixam de 100%", Nutricao.KiSempreCheio(bio.Ficha));
		Checa("**SEM SANGUE SAIYAJIN, SEM ZENKAI** (*\"zenkai se tiver saiyajin\"*): nenhuma das doadoras era", !bio.Ficha.HasZenkai());
		Checa("**O LIVRO DO DOADOR, NO NIVEL DELE** (*\"do mesmo nivel q eles\"*): o Kicker entra ENSINADO (ele sabe e nao repassa) e no nivel 3",
			  bio.Livro.Sabe(Kicker) && bio.Livro.FoiEnsinada(Kicker) && bio.Niveis.Nivel(Kicker) == 3,
			  $"sabe={bio.Livro.Sabe(Kicker)} ensinada={bio.Livro.FoiEnsinada(Kicker)} nivel={bio.Niveis.Nivel(Kicker)}");
		Checa("**AS TECNICAS CUSTOMIZADAS DO DOADOR** (*\"e as tecnicas customizadas tb\"*): a confirmada do Majin entra como copia ENSINADA -- e a do criador morreu com ele",
			  bio.Customizadas.Count == 1 && bio.Customizadas[0].Nome == "Raio do Majin" && bio.Customizadas[0].Ensinada && bio.Customizadas[0].Criada,
			  string.Join(", ", bio.Customizadas.Select(c => $"{c.Id}:{c.Nome}{(c.Ensinada ? " (ensinada)" : "")}")));
		Checa("...e o parto conta o que veio: as skills no nivel dos doadores, as tecnicas inventadas e o sangue -- e nenhuma palavra de Zenkai (nenhum Saiyajin)",
			  Disse(parto, "no nível em que eles") && Disse(parto, "que eles mesmos inventaram") && Disse(parto, "corre em você") && !Disse(parto, "Zenkai"),
			  string.Join(" | ", parto));

		// O DEFEITO DO NIVEL: a mesma funcao do parto, num corpo novo, com a amostra que a agulha gravou
		ServerPlayer? semNivel = ForjarCorpo(host, "Human", "Sem Nivel", forjados, PalcoDoSangue);
		if (semNivel != null)
		{
			semNivel.Livro = new Jandirus.Core.Skills.SkillBook();
			semNivel.Niveis = new Jandirus.Core.Skills.NiveisDeSkill();
			var g = new Gestacao { Amostras = { daMajin }, Tecnicas = { Kicker } };
			NivelDoDoadorFicaDeForaDeTeste = true;
			try { HerdarOLivroDosDoadores(semNivel, g); }
			finally { NivelDoDoadorFicaDeForaDeTeste = false; }
			Checa("(defeito injetado: o nivel do doador fica de fora) o Kicker entra no nivel ZERO -- a regua do nivel sabe reprovar",
				  semNivel.Livro.Sabe(Kicker) && semNivel.Niveis.Nivel(Kicker) == 0, $"nivel={semNivel.Niveis.Nivel(Kicker)}");
		}

		SangueDoBio.SangueNaoPassaDeTeste = true;
		bool semNada;
		try
		{
			semNada = !CorpoDeMajin(bio) && !Jandirus.Core.Combat.Feridas.NaoSangra(RacaComSangue(bio))
						  && !Nutricao.VigorSempreCheio(bio.Ficha) && !Nutricao.KiSempreCheio(bio.Ficha);
		}
		finally { SangueDoBio.SangueNaoPassaDeTeste = false; }
		Checa("(defeito injetado: o sangue nao passa) o mesmo bio fica sem semi-imortalidade, sangrando e com o vigor e o Ki comuns -- sozinho o bio nao tem nada", semNada);
	}

	/// <summary>
	/// Uma linha por numero que o original crava. Elas nao dirigem nada -- sao a tabela de conversao
	/// do sistema, e existem porque um multiplicador trocado nao quebra bancada nenhuma das de cima:
	/// a escada continua subindo, a forma continua entrando, e o bicho fica com o poder errado pra
	/// sempre.
	/// </summary>
	private void MedirOsNumerosContraODm(Checagem Checa)
	{
		GD.Print("--- 15. OS NUMEROS CONTRA O DM ---");

		void Numero(string oque, double achado, double esperado) =>
			Checa($"{oque} = {esperado:0.####}", Math.Abs(achado - esperado) < 1e-9,
				  $"achei {achado:0.####}");

		Numero("`DNL_BIO_BP_SHARE` (o bio nasce com metade do doador mais forte)",
			   BioAndroids.FracaoDoDoador, 0.5);
		Numero("`DNL_BIO_MAX_DNA` (amostras por tanque)", BioAndroids.MaxDna, 4);
		// (2026-10-17) A ESCADA E DO ADENDO DO DONO, e nao mais do DM (`DNL_BIO_EVO_*`); (2026-10-26) as divinas sao do pedido dele (a Super
		// Perfeita, `cell4mult = 8`, saiu do jogo).
		Numero("o PASSO de um jogador absorvido (o adendo: um marco inteiro)", BioAndroids.PassoDoJogador, 1);
		Numero("o PASSO de um NPC (o adendo: *\"bem menos\"* -- escolha: um quinto)", BioAndroids.PassoDoNpc, 0.2);
		Numero("os MARCOS por forma (o sexto passo sobe a forma)", BioAndroids.MarcosPorForma, 5);
		Numero("o semi-perfeito no marco 0", BioAndroids.MultAbsoluto(BioAndroids.SemiPerfeito, 0), 2);
		Numero("o semi-perfeito no marco 5", BioAndroids.MultAbsoluto(BioAndroids.SemiPerfeito, 5), 3);
		Numero("o perfeito no marco 0", BioAndroids.MultAbsoluto(BioAndroids.Perfeito, 0), 4);
		Numero("o perfeito no marco 5", BioAndroids.MultAbsoluto(BioAndroids.Perfeito, 5), 14);
		Numero("a GOLDEN PERFECT ao despertar o ki divino (o SSG), absoluta sobre o imperfeito", Catalogo.GoldenPerfectDoBio.AoDespertar, 22);
		Numero("...no nivel do Blue, o teto da rampa", Catalogo.GoldenPerfectDoBio.NoTopo, 32);
		Numero("...e o nivel do Blue e os 33% de ki divino", Catalogo.GoldenPerfectDoBio.TopoEm, 33);
		Numero("a MAX (o Blue Evolution), absoluta sobre o imperfeito", Catalogo.MultDaMaxDoBio, 56);
		Numero("a EXECUCAO de absorcao, em segundos", BioAndroids.SegundosDaExecucao, 5);
		Numero("...e o que ela devolve (vida, folego e Ki)", BioAndroids.DevolveAExecucao, 0.2);
		Numero("`nerfSSJ()` no SSJ1 de quem chegou por `canSSJ`", Catalogo.Ssj1BaseDiluido, 1.35);
		Numero("o marco `bio1` (imperfeito)", Milestones.Valor("bio1"), 1.5);
		Numero("o marco `bio2` (semi-perfeito)", Milestones.Valor("bio2"), 2);
		Numero("o marco `bio3` (perfeito)", Milestones.Valor("bio3"), 3);
		Numero("o marco `bio_ssj2`", Milestones.Valor("bio_ssj2"), 4);
		Numero("`DNL_ABSORB_KI_PER_HIT` (6% do tanque por tiro engolido)", AbsorcaoPorGolpeDeKi, 0.06);
		Numero("o teto do androide de absorcao (o DOBRO do tanque)", TetoDoAndroideAbsorcao, 2);
		Numero("`DNL_INT_REQ` (tecnologia pro laboratorio)", TechDoLaboratorio, 70);
		Numero("o androide de ABSORCAO, em zeni", CustoAndroideAbsorcao, 1_000_000);
		Numero("o androide de ENERGIA INFINITA, em zeni", CustoAndroideInfinito, 2_000_000);
		Numero("`DNL_BIO_BREW_MONTHS 0.1` de ano, em DIAS in-game", DiasDeGestacao, 30);

		// ============================ A LARVA SE MEDE NA FICHA ============================
		// (2026-10-17) A larva e SEMPRE RELAXADA (o dono) -- 5 de poder, pela mesma trava do relaxado. Era o teto de 1% do DM. O numero se le
		// pelo EFEITO, numa ficha crua de um milhao: duas leituras, porque a trava le o expresso da anterior.
		// ================================================================================
		var cru = new Fighter
		{
			Race = BioAndroids.Raca, ParentRace = BioAndroids.Raca,
			BP = 1_000_000, bio_lab_born = true, bio_stage = BioAndroids.Larva,
		};
		cru.Statify();
		cru.PowerLevel();
		cru.PowerLevel();
		Checa("a LARVA expressa 5 -- SEMPRE RELAXADA (o dono, 2026-10-17: *\"fica sempre no modo 'relaxado' com 5 de bp\"*); um milhao de base "
			  + "vira 5 na tela (era o 1% do DM)",
			  cru.expressedBP == 5, $"{cru.expressedBP:N0}");
		Numero("os minutos ENTERRADA pra larva amadurecer (o adendo)", BioAndroids.SegundosEnterradoPraAmadurecer / 60, 5);
	}

	// =====================================================================
	// 15. A BANCADA SE COBRA -- **A INJECAO DE DEFEITO**
	// =====================================================================
	/// <summary>
	/// ============================ UMA REGRA QUE PASSA VERDE COM O PROPRIO DEFEITO E FALHA DA BANCADA ============================
	/// Todas as 100 e tantas linhas acima medem o sistema. Estas medem AS LINHAS: cada familia recebe,
	/// de proposito, exatamente o defeito que ela existe pra pegar, e a afirmacao TEM que virar
	/// vermelha. Se ela continuar verde, a linha la de cima nao estava olhando o que diz que olha.
	///
	/// Este projeto ja pagou por nao ter isto: seis dados extraidos-sem-consumidor, uma API de sigilo
	/// 100% orfa, trinta regras de exp carregadas e descartadas. Todas passavam por bancada verde --
	/// porque a bancada media a existencia da funcao, e nao o efeito dela.
	///
	/// O DEFEITO E SEMPRE **UM CAMPO**, e sempre devolvido no fim. Injetar dois de uma vez daria um
	/// vermelho que nao diz qual dos dois a regra estava lendo.
	/// ==========================================================================================================================
	/// </summary>
	private void MedirAsInjecoesDeDefeito(Checagem Checa, ServerPlayer host, ServerPlayer bio,
										  ServerPlayer? semDna, List<ServerPlayer> forjados,
										  List<Obra> obras)
	{
		GD.Print("--- 16. A BANCADA SE COBRA (injecao de defeito) ---");

		// A afirmacao INVERTIDA: `prova()` e o predicado da familia la de cima; com o defeito posto,
		// ele TEM que devolver falso.
		void Injetar(string familia, Action por, Func<bool> prova, Action devolver)
		{
			por();
			bool aindaVerde;
			try { aindaVerde = prova(); }
			catch (Exception) { aindaVerde = false; }
			devolver();
			Checa($"[injecao] {familia}", !aindaVerde,
				  "a regra continuou VERDE com o proprio defeito posto -- a linha la de cima nao "
				  + "esta medindo o que ela diz que mede");
		}

		// ---- 1. O CRIVO DA AGULHA ----
		ServerPlayer? cientista = ForjarCorpo(host, "Human", "Dr. Injecao", forjados, PalcoDaInjecao);
		if (cientista != null)
		{
			cientista.Ficha.techskill = 99;
			Obra lab = PorUmLabDeBio(cientista, 984_020, obras);
			ServerPlayer? vitima = ForjarCorpo(host, "Human", "Cobaia da Injecao", forjados, PalcoDaInjecao);
			if (vitima != null)
			{
				// O DEFEITO: a vitima de pe. A familia 2 afirma que a agulha exige NOCAUTE.
				Injetar("o crivo do NOCAUTE (vitima DE PE nao pode virar amostra)",
						() => vitima.Ficha.KO = false,
						() => { ComandoDeTech(cientista, "colher_dna", "");
								return lab.Fornada is { } g && g.Amostras.Count > 0; },
						() => { lab.Fornada = null; });

				// O DEFEITO: o papel de NPC. A familia 9 afirma que cidadao nao serve.
				Injetar("o crivo do `EhJogador` -- com o corpo caido e SEM papel de NPC a agulha "
						+ "pega, entao a recusa do cidadao la em cima e do PAPEL e nao do acaso",
						() => vitima.Ficha.KO = true,
						() => { ComandoDeTech(cientista, "colher_dna", "");
								return lab.Fornada == null || lab.Fornada.Amostras.Count == 0; },
						() => { lab.Fornada = null; vitima.Ficha.KO = false; });
			}
		}

		// ---- 2. A CARAPACA DA LARVA ----
		int estagioGuardado = bio.Ficha.bio_stage;
		bool nascidoGuardado = bio.Ficha.bio_lab_born;
		double bpGuardado = bio.Ficha.BP;
		bio.Ficha.BP = 1_000_000;
		Injetar("o SELO da LARVA (com o degrau em larva o poder expresso TEM que cair -- a 5, ela e sempre relaxada)",
				() => { bio.Ficha.bio_stage = BioAndroids.Larva; RepercutirPoder(bio); },
				() => bio.Ficha.expressedBP > bio.Ficha.BP * 0.5,
				() => { bio.Ficha.bio_stage = estagioGuardado; RepercutirPoder(bio); });

		// (2026-10-17) O ESCONDERIJO DA LARVA E O DO RELAXADO: com o relaxado deixando de esconder (`RelaxadoNaoEscondeDeTeste`), a larva
		// vestida volta a ser sentida -- e a prova da familia 5b ("selada, fora do Sense") cai.
		Injetar("o esconderijo da LARVA (a carapaca sem a trava do relaxado: ela apareceria no Sense e no scouter)",
				() => { bio.Ficha.bio_larva = true; PoderEmUso.RelaxadoNaoEscondeDeTeste = true; RepercutirPoder(bio); RepercutirPoder(bio); },
				() => bio.Ficha.expressedBP == 5 && !TemEnergiaQueSeSente(bio),
				() => { bio.Ficha.bio_larva = false; PoderEmUso.RelaxadoNaoEscondeDeTeste = false; RepercutirPoder(bio); });

		// ============================ 3. O ZENKAI -- E ELE PRECISA DO BIO **SEM** DNA ============================
		// A primeira rodada desta injecao ficou VERDE, e a bancada estava certa: o `HasZenkai` tem SEIS
		// ramos, e o do `canSSJ` responde `true` sozinho. Trocando so a raca do primeiro bio, o
		// predicado continuava verdadeiro pelo DNA Saiyajin -- ou seja, a injecao nao estava injetando
		// nada, so mudando o motivo da resposta.
		//
		// O bio SEM DNA nao tem esse ramo aceso, e por isso e nele que o ramo da RACA se mede sozinho.
		// Este e exatamente o tipo de coisa que a injecao existe pra achar: uma medida que parece
		// verde por um caminho que ninguem estava olhando.
		// =====================================================================================================
		// (v0.3.0) O RAMO DO BIO NO `HasZenkai` E O SANGUE: tirando o Saiyajin das doadoras do bio que o tem, o predicado TEM
		// que cair -- mesmo com o `canSSJ` aceso, que e o caminho que uma regra preguicosa usaria pra parecer certa.
		List<string> sangueGuardado = [.. bio.Ficha.bio_racas_dna];
		bool saiyajinGuardado = bio.Ficha.bio_saiyan_dna;
		Injetar("o ramo do SANGUE no `HasZenkai` (tirando o Saiyajin das doadoras do bio, o predicado TEM que cair -- "
				+ "com o `canSSJ` dele ainda aceso)",
				() => { bio.Ficha.bio_racas_dna = [.. sangueGuardado.Where(r => !Catalogo.EhSaiyajin(r))]; bio.Ficha.bio_saiyan_dna = false; },
				() => bio.Ficha.HasZenkai(),
				() => { bio.Ficha.bio_racas_dna = sangueGuardado; bio.Ficha.bio_saiyan_dna = saiyajinGuardado; });

		// ---- 4. O SUPER SAIYAJIN PELO DNA ----
		Injetar("o `canSSJ` (apagando o DNA, a linha Saiyajin TEM que fechar pro bio que a tinha)",
				() => bio.Ficha.canSSJ = false,
				() => Catalogo.LinhasAbertas(Perfil(bio)).Contains(LinhaDeForma.Saiyajin),
				() => bio.Ficha.canSSJ = true);

		// ---- 5. A HERANCA DE TECNICA ----
		Injetar($"a heranca da PESSOA (tirando `{SkillDaPessoa}` do livro, o `SabeTecnica` TEM que "
				+ "parar de responder)",
				() => bio.Livro.Esquecer(SkillDaPessoa),
				() => SabeTecnica(bio, VerboDaPessoa),
				() => bio.Livro.DarComoEnsinada(SkillDaPessoa));

		Injetar($"a heranca da RACA (tirando `{SkillDaRaca}`, idem)",
				() => bio.Livro.Esquecer(SkillDaRaca),
				() => SabeTecnica(bio, VerboDaRaca),
				() => bio.Livro.DarComoEnsinada(SkillDaRaca));

		// ---- 6. A PORTA DA GOLDEN PERFECT (2026-10-26, no lugar da Super Perfeita) ----
		GodKiState? kiDivinoDaInjecao = bio.Ficha.godki;
		Injetar("a porta da GOLDEN PERFECT (com o ki divino desperto, rebaixando o degrau pra semi-perfeito ela TEM que fechar) -- o "
				+ "\"So o Perfeito\" do dono",
				() => { bio.Ficha.godki = new GodKiState { awakened = true, mastery = 0 }; bio.Ficha.bio_stage = BioAndroids.SemiPerfeito; },
				() => bio.Forma.Avaliar("golden_perfect", bio.Ficha.BP, 1, false, Perfil(bio))
					  == RecusaForma.Pode,
				() => { bio.Ficha.bio_stage = estagioGuardado; bio.Ficha.godki = kiDivinoDaInjecao; });

		// ---- 7. O NUCLEO INFINITO ----
		ServerPlayer? robo = ForjarCorpo(host, "Human", "Cobaia do Nucleo", forjados);
		if (robo != null)
		{
			robo.Ficha.Statify();
			Injetar("o nucleo do ANDROIDE DE ENERGIA INFINITA (sem o bit da classe, o tique nao pode "
					+ "encher folego nenhum) -- era o campo escrito e sem um leitor no repo",
					() => { robo.Ficha.AndroideInfinito = false; robo.Ficha.stamina = 0; },
					() => { TickDoNucleoInfinito(); return robo.Ficha.stamina >= robo.Ficha.maxstamina; },
					() => robo.Ficha.stamina = robo.Ficha.maxstamina);
		}

		// ---- 8. A POSTURA DE ABSORCAO ----
		ServerPlayer? sugador = ForjarCorpo(host, "Human", "Cobaia da Postura", forjados);
		if (sugador != null)
		{
			sugador.Ficha.Statify();
			sugador.Ficha.AndroideAbsorcao = true;
			sugador.Ficha.ki_absorb_stance = true;
			Injetar("a POSTURA de coletores (fechando a postura, o tiro TEM que voltar a machucar)",
					() => sugador.Ficha.ki_absorb_stance = false,
					() => EngoliuOAtaqueDeKi(sugador, "Kamehameha", fisico: false),
					() => sugador.Ficha.ki_absorb_stance = true);
		}

		// ---- 9. O CONTADOR DA ESCADA ----
		if (semDna != null)
		{
			int est2 = semDna.Ficha.bio_stage;
			int marco2 = semDna.Ficha.bio_marco;
			Injetar("a escada por ABSORCAO (com o degrau no MARCO 5 do perfeito ela para -- o teto da escada)",
					() => { semDna.Ficha.bio_stage = BioAndroids.Perfeito; semDna.Ficha.bio_marco = BioAndroids.MarcosPorForma;
							semDna.Ficha.bio_passos = 0; },
					() => { ContarAbsorcaoDoBio(semDna, semDna);
							return semDna.Ficha.bio_passos > 0 || semDna.Ficha.bio_marco != BioAndroids.MarcosPorForma; },
					() => { semDna.Ficha.bio_stage = est2; semDna.Ficha.bio_marco = marco2; semDna.Ficha.bio_passos = 0; });
		}

		bio.Ficha.BP = bpGuardado;
		bio.Ficha.bio_lab_born = nascidoGuardado;
		bio.Ficha.Statify();
	}
}
