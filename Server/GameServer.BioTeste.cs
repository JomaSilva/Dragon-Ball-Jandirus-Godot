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
	/// O CONTROLE da heranca: "Stretchy Arms", do MESMO galho racial e **sem verbo**. Ela nao pode
	/// atravessar -- `Keyableverbs` e uma lista de botoes, e nao o livro do doador.
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

			// A gestacao e a maturacao da larva passam a valer UM segundo. As duas leem esta mesma
			// chave (ver `MaturacaoDaLarvaSegundos`), o que ja e uma medida: se alguem separar os
			// dois prazos, esta bancada trava esperando o segundo.
			_gestacaoDeTeste = 1;

			ServerPlayer? cientista = ForjarCorpo(host, "Human", "Dr. Bancada", forjados);
			if (cientista == null) { Checa("PRECONDICAO: o cientista forjado nasceu", false); return; }

			MedirAPortaDoLaboratorio(Checa, cientista, obras);
			ServerPlayer? bio = MedirONascimento(Checa, host, cientista, obras, forjados);
			if (bio == null) return;

			MedirALarva(Checa, bio);
			MedirAEscadaPorAbsorcao(Checa, host, bio, forjados);
			MedirAsFormasDoBio(Checa, bio);
			MedirOSsj2PelaMorte(Checa, bio);
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
		//   * `namek/Stretchy_Arms` -- do MESMO galho, e **sem verbo**. Ela e o controle: prova que a
		//     agulha copia BOTAO (`Keyableverbs`) e nao "o que a pessoa tinha aprendido". Sem este par,
		//     um dia em que a heranca copiasse o livro inteiro passaria verde.
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
			Checa("...e **nao** guarda a do mesmo galho que nao tem botao (`Stretchy Arms`) -- a agulha "
				  + "copia `Keyableverbs`, que e uma lista de BOTOES, e nao o livro do doador",
				  an != null && !an.Skills.Contains(SkillPassivaDaRaca));
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
		Checa("O BIO-ANDROIDE TEM ZENKAI -- e este ramo do `HasZenkai` era MORTO: ele testava "
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

		// A CARAPACA E TETO DURO E NAO DIVISOR: raiva, Ki alto e buff nao a furam. Aqui isso e
		// medido com o `angerBuff` no maximo -- o caminho por onde o teto vazaria se fosse divisor.
		bio.Ficha.angerBuff = 3;
		bio.Ficha.Ki = bio.Ficha.MaxKi;
		RepercutirPoder(bio);
		double esperado = Math.Max(bio.Ficha.BP / 100, 1);
		Checa("a larva expressa 1% do proprio poder, e nem a RAIVA fura a carapaca (o teto e DURO; "
			  + "os tres comentarios do DM que dizem 5% e 10% e que estao desatualizados)",
			  Math.Abs(bio.Ficha.expressedBP - Math.Round(esperado)) <= 1,
			  $"{bio.Ficha.expressedBP:N0} != {esperado:N0}");
		bio.Ficha.angerBuff = 1;

		// A larva nao absorve -- e e por isso que a escada dela e o TEMPO e nao a boca.
		bio.Ficha.bio_abs_players = 0;
		UsarHabilidade(bio, "absorver");
		Checa("a LARVA nao absorve (os orgaos so se formam ao amadurecer)",
			  bio.Ficha.bio_abs_players == 0);

		// ---- o tempo rompe a carapaca ----
		double bpAntes = bio.Ficha.BP;
		bio.Ficha.bio_mature_em = NowMs() - 1;
		TickDaLarva();
		Checa("passado o prazo, a carapaca ROMPE e ele vira IMPERFEITO",
			  bio.Ficha.bio_stage == BioAndroids.Imperfeito, $"{bio.Ficha.bio_stage}");
		Checa("...sem mexer no BP BASE (o que ele ganha e a carapaca SAINDO, nao poder novo)",
			  Math.Abs(bio.Ficha.BP - bpAntes) < 1, $"{bio.Ficha.BP:N0} != {bpAntes:N0}");
		RepercutirPoder(bio);
		Checa("...e agora ele expressa o poder INTEIRO (o teto de 1% morreu com o degrau)",
			  bio.Ficha.expressedBP > bio.Ficha.BP * 0.5,
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

		// ---- um NPC vale MEIO jogador ----
		ServerPlayer? npc = NascerNpc("cidadao", bio.Zone, bio.Pos + new Vec2(8, 0),
									  ++_lugarDaBancadaDeBio);
		if (npc != null)
		{
			forjados.Add(npc);
			npc.Ficha.KO = true;
			bio.AlvoId = npc.Id;
			UsarHabilidade(bio, "absorver");
			Checa("um NPC absorvido vale METADE de um jogador (20 NPCs = um degrau; e o freio contra "
				  + "evoluir varrendo a populacao de graca de um planeta)",
				  Math.Abs(bio.Ficha.bio_abs_players - BioAndroids.PesoDoNpc) < 1e-9,
				  $"{bio.Ficha.bio_abs_players}");
			Checa("...e ele MORREU: o bio CONSOME, nao sela (o Majin e que sela)", npc.Ficha.dead);
			Checa("...e o `AbsorbBP` passou a existir -- ele e `AbsorbDeterminesBP` nunca eram "
				  + "escritos por ninguem, com a formula da MEDIA ja portada e sem consumidor",
				  bio.Ficha.AbsorbBP > 0 && bio.Ficha.AbsorbDeterminesBP);
		}

		// ---- UM ANDROIDE vale um degrau inteiro: o atalho do Cell ----
		bio.Ficha.bio_abs_players = 0;
		bio.Ficha.bio_abs_androids = 0;
		double bpAntes = bio.Ficha.BP;

		ServerPlayer? androide = ForjarCorpo(host, "Android", "Androide 1", forjados);
		if (androide == null) { Checa("PRECONDICAO: o androide forjado nasceu", false); return; }
		androide.Ficha.KO = true;
		bio.AlvoId = androide.Id;
		_prontoAbsorver.Remove(bio.Id);
		UsarHabilidade(bio, "absorver");

		Checa("UM androide absorvido ja evolui -- o atalho classico do Cell",
			  bio.Ficha.bio_stage == BioAndroids.SemiPerfeito, $"{bio.Ficha.bio_stage}");
		Checa("...e o BP BASE DOBRA (`BP *= 2`, permanente -- por isso o degrau nao e uma forma: o "
			  + "teto de treino, o do Zenkai e o CapCheck leem o BP base)",
			  Math.Abs(bio.Ficha.BP - bpAntes * BioAndroids.MultDoSemiPerfeito) < 1,
			  $"{bio.Ficha.BP:N0} != {bpAntes * 2:N0}");
		Checa("...o contador ZEROU pro proximo degrau", bio.Ficha.bio_abs_androids == 0);
		Checa("...e o marco `bio2` subiu o ganho de treino",
			  Math.Abs(bio.Ficha.bp_milestone_mult - Milestones.Valor("bio2")) < 1e-9,
			  $"{bio.Ficha.bp_milestone_mult}");

		// ---- e o segundo androide leva a FORMA PERFEITA ----
		bpAntes = bio.Ficha.BP;
		ServerPlayer? androide2 = ForjarCorpo(host, "Android", "Androide 2", forjados);
		if (androide2 == null) { Checa("PRECONDICAO: o segundo androide nasceu", false); return; }
		androide2.Ficha.KO = true;
		bio.AlvoId = androide2.Id;
		_prontoAbsorver.Remove(bio.Id);
		UsarHabilidade(bio, "absorver");

		Checa("o segundo androide leva a FORMA PERFEITA",
			  bio.Ficha.bio_stage == BioAndroids.Perfeito, $"{bio.Ficha.bio_stage}");
		Checa("...com o BP BASE QUADRUPLICANDO em cima do degrau anterior",
			  Math.Abs(bio.Ficha.BP - bpAntes * BioAndroids.MultDoPerfeito) < 1,
			  $"{bio.Ficha.BP:N0} != {bpAntes * 4:N0}");
		Checa("...e ela e PERMANENTE (`form3cantrevert`)", bio.Ficha.form3cantrevert);

		// ---- o teto: absorver nao evolui alem do perfeito ----
		bio.Ficha.bio_abs_androids = 0;
		ServerPlayer? sobra = ForjarCorpo(host, "Human", "Sobra", forjados);
		if (sobra != null)
		{
			sobra.Ficha.KO = true;
			bio.AlvoId = sobra.Id;
			_prontoAbsorver.Remove(bio.Id);
			UsarHabilidade(bio, "absorver");
			Checa("passado o PERFEITO a contagem para (dali pra cima o que ha e a Super Perfeita, "
				  + "que e forma e nao degrau)",
				  bio.Ficha.bio_stage == BioAndroids.Perfeito && bio.Ficha.bio_abs_players == 0,
				  $"estagio {bio.Ficha.bio_stage}, jogadores {bio.Ficha.bio_abs_players}");
		}
	}

	// =====================================================================
	// 5. AS FORMAS
	// =====================================================================
	private void MedirAsFormasDoBio(Checagem Checa, ServerPlayer bio)
	{
		GD.Print("--- 5. AS FORMAS ---");

		PerfilDeFormas perfil = Perfil(bio);
		Checa("a linha do BIO-ANDROIDE esta aberta pra ele",
			  Catalogo.LinhasAbertas(perfil).Contains(LinhaDeForma.BioAndroide));
		Checa("...e a SAIYAJIN tambem, pelo `canSSJ` (ele tem as duas: a do corpo dele e a do DNA "
			  + "que o gerou)",
			  Catalogo.LinhasAbertas(perfil).Contains(LinhaDeForma.Saiyajin));

		// ---- a Super Perfeita, com poder de sobra ----
		bio.Ficha.BP = Catalogo.PortaSuperPerfeito * 2;
		bio.Ficha.Ki = bio.Ficha.MaxKi;
		perfil = Perfil(bio);
		Checa("com a FORMA PERFEITA e poder de sobra, a SUPER PERFEITA e alcancavel",
			  bio.Forma.Avaliar("super_perfect", bio.Ficha.BP, 1, false, perfil) == RecusaForma.Pode,
			  $"{bio.Forma.Avaliar("super_perfect", bio.Ficha.BP, 1, false, perfil)}");

		// ---- e ela NAO pede DNA Saiyajin: o dono achava que sim ----
		bool saiyanGuardado = bio.Ficha.bio_saiyan_dna, canGuardado = bio.Ficha.canSSJ;
		bio.Ficha.bio_saiyan_dna = false;
		bio.Ficha.canSSJ = false;
		Checa("a SUPER PERFEITA **nao** pede DNA Saiyajin -- `Cell4()` nao olha raca nem DNA uma "
			  + "unica vez (`CellFormBuff.dm:74`). Ela e do CORPO dele, nao do sangue do doador",
			  bio.Forma.Avaliar("super_perfect", bio.Ficha.BP, 1, false, Perfil(bio)) == RecusaForma.Pode);
		bio.Ficha.bio_saiyan_dna = saiyanGuardado;
		bio.Ficha.canSSJ = canGuardado;

		// ---- entrar nela de verdade, pelo mesmo funil da tecla C ----
		TransformarPara(bio, "super_perfect");
		Checa("ele ENTRA na Super Perfeita pelo funil de producao (`TransformarPara`)",
			  bio.Forma.Atual == "super_perfect", bio.Forma.Atual);
		Checa("...e o multiplicador e 8x", Math.Abs(bio.Ficha.ssjBuff - Catalogo.SuperPerfeitoMult) < 1e-6,
			  $"{bio.Ficha.ssjBuff}");

		// ---- o conserto do bug do DM: o SSJ nao atropela a Super Perfeita ----
		bio.Forma.Liberar("ssj1");
		RecusaForma porCimaDaSuper = bio.Forma.Avaliar("ssj1", bio.Ficha.BP, 1, false, Perfil(bio));
		Checa("estando em SUPER PERFEITA, o Super Saiyajin NAO entra por cima -- no DM as duas "
			  + "escrevem a MESMA `mob/var/ssj` e o SSJ2 SOBRESCREVE a Super Perfeita (8x virando "
			  + "1,75x, calado, no segundo Transform). Aqui a proibicao e dita e vale nos dois lados",
			  porCimaDaSuper == RecusaForma.FormaErrada, $"{porCimaDaSuper}");

		// ---- e o inverso: estando em SSJ, a Super Perfeita nao entra (o `!ssj` do DM) ----
		TransformarPara(bio, Catalogo.IdBase);
		TransformarPara(bio, "ssj1");
		if (bio.Forma.Atual == "ssj1")
		{
			RecusaForma superSobreSsj =
				bio.Forma.Avaliar("super_perfect", bio.Ficha.BP, 1, false, Perfil(bio));
			Checa("...e estando em Super Saiyajin a Super Perfeita tambem nao entra (o `!ssj` do "
				  + "`Cell4()`, este lado o DM tem)",
				  superSobreSsj == RecusaForma.FormaErrada, $"{superSobreSsj}");
		}
		TransformarPara(bio, Catalogo.IdBase);
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

		// --- degrau 1: LARVA -> IMPERFEITO, e o requisito e TEMPO ---
		bio.Ficha.bio_mature_em = NowMs() + 600_000;
		TickDaLarva();
		Checa("LARVA -> IMPERFEITO **NAO** acontece antes do prazo (o degrau e o relogio, e nao o "
			  + "tique) -- sem esta metade, um relogio zerado na conta daria a mesma linha verde",
			  bio.Ficha.bio_stage == BioAndroids.Larva, $"{bio.Ficha.bio_stage}");
		bio.Ficha.bio_mature_em = NowMs() - 1;
		TickDaLarva();
		Checa("...e acontece depois dele", bio.Ficha.bio_stage == BioAndroids.Imperfeito,
			  $"{bio.Ficha.bio_stage}");

		// --- degrau 2: IMPERFEITO -> SEMI, e o requisito sao DEZ JOGADORES ---
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

		bio.Ficha.bio_abs_players = 8.5;
		bio.Ficha.bio_abs_androids = 0;
		double bpAntes = bio.Ficha.BP;
		if (Cidadao("Nono") != null)
		{
			UsarHabilidade(bio, "absorver");
			Checa("IMPERFEITO -> SEMI-PERFEITO **NAO** acontece no NONO jogador (8,5 + meio NPC = 9) "
				  + "-- o numero e dez, e sem esta metade a escada estaria aberta a qualquer contagem",
				  bio.Ficha.bio_stage == BioAndroids.Imperfeito
				  && Math.Abs(bio.Ficha.bio_abs_players - 9) < 1e-9,
				  $"estagio {bio.Ficha.bio_stage}, contagem {bio.Ficha.bio_abs_players}");
			Checa("...e o BP BASE nao se mexeu junto",
				  Math.Abs(bio.Ficha.BP - bpAntes) < 1, $"{bio.Ficha.BP:N0}");
		}

		bio.Ficha.bio_abs_players = 9.5;
		bpAntes = bio.Ficha.BP;
		if (Cidadao("Decimo") != null)
		{
			UsarHabilidade(bio, "absorver");
			Checa("...e ACONTECE no decimo (`DNL_BIO_EVO_PLAYERS`), pelo mesmo verbo",
				  bio.Ficha.bio_stage == BioAndroids.SemiPerfeito, $"{bio.Ficha.bio_stage}");
			Checa("...com o BP BASE DOBRANDO (`DNL_BIO_EVO2_MULT`)",
				  Math.Abs(bio.Ficha.BP - bpAntes * BioAndroids.MultDoSemiPerfeito) < 1,
				  $"{bio.Ficha.BP:N0} != {bpAntes * 2:N0}");
			Checa("...e a contagem ZERADA -- sem isto o decimo primeiro jogador daria o degrau "
				  + "seguinte de brinde", bio.Ficha.bio_abs_players == 0);
		}

		// --- degrau 3: SEMI -> PERFEITO, mesma regra, outro multiplicador ---
		bio.Ficha.bio_abs_players = 9.5;
		bpAntes = bio.Ficha.BP;
		if (Cidadao("Vigesimo") != null)
		{
			UsarHabilidade(bio, "absorver");
			Checa("SEMI -> PERFEITO pela MESMA regra de dez",
				  bio.Ficha.bio_stage == BioAndroids.Perfeito, $"{bio.Ficha.bio_stage}");
			Checa("...com o BP BASE QUADRUPLICANDO (`DNL_BIO_EVO3_MULT`), e nao dobrando de novo",
				  Math.Abs(bio.Ficha.BP - bpAntes * BioAndroids.MultDoPerfeito) < 1,
				  $"{bio.Ficha.BP:N0} != {bpAntes * 4:N0}");
		}
		return bio;
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

		Checa("...e ele **nao** herdou a `Stretchy Arms` do MESMO galho racial, que nao tem botao -- "
			  + "a agulha copia `Keyableverbs`, e nao o livro do doador",
			  !bio.Livro.Sabe(SkillPassivaDaRaca));

		Checa("...e nao herdou a ARVORE do Namekuseijin junto (`generatetrees` despacha por "
			  + "`Parent_Race`, que agora e Bio-Android): um bio feito de DNA Namekuseijin NAO "
			  + "regenera como Namekuseijin",
			  !_skills!.ArvoresDe(bio.Race, "").Any(
				  s => s.Path.Contains("namek", StringComparison.OrdinalIgnoreCase)),
			  string.Join(", ", _skills.ArvoresDe(bio.Race, "").Select(s => s.Path)));

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
	/// E O CONTRA-EXEMPLO E DE OUTRA RACA, e nao um bio sem DNA Saiyajin -- porque no DM o Zenkai do
	/// bio vem da RACA e nao do sangue do doador (`combatgains.dm:14` testa a raca, primeira linha da
	/// funcao). Um bio sem DNA Saiyajin TEM Zenkai, e essa distincao e exatamente o que separa as
	/// duas coisas que o dono pediu na mesma frase.
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
		bio.Ficha.zenkaiReady = 0;
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
		Checa("...e o tamanho e os 10% do poder do vencedor (`ZenkaiPct`), com teto no BP final",
			  Math.Abs(bio.Ficha.UltimoZenkai - algoz.Ficha.BP * Jandirus.Core.Stats.GainKnobs.ZenkaiPct) < 1
			  || bio.Ficha.UltimoZenkaiNoTeto,
			  $"{bio.Ficha.UltimoZenkai:N0}");

		// ---- o bio SEM DNA Saiyajin tambem tem: o Zenkai e da RACA ----
		if (semDna != null)
		{
			double a2 = semDna.Ficha.BP;
			semDna.Ficha.BP = 1_000_000;
			semDna.Ficha.zenkaiReady = 0;
			semDna.Ficha.dead = false;
			semDna.Ficha.KO = true;
			semDna.Ficha.Statify();
			RepercutirPoder(semDna);
			double a3 = semDna.Ficha.BP;
			AoPerderALuta(semDna, algoz, morreu: false);
			Checa("...e o bio SEM DNA Saiyajin **tambem** ganha: no DM o Zenkai do bio vem da RACA "
				  + "(`combatgains.dm:14`, primeira linha) e nao do sangue do doador. Sao duas "
				  + "coisas diferentes na mesma frase do pedido",
				  semDna.Ficha.BP > a3, $"{a3:N0} -> {semDna.Ficha.BP:N0}");
			semDna.Ficha.KO = false;
			semDna.Ficha.BP = a2;
			semDna.Ficha.Statify();
		}

		// ---- O CONTRA-EXEMPLO: outra raca, mesma derrota, mesmo funil ----
		ServerPlayer? humano = ForjarCorpo(host, "Human", "Humano Comum", forjados);
		if (humano != null)
		{
			humano.Ficha.BP = 1_000_000;
			humano.Ficha.zenkaiReady = 0;
			humano.Ficha.Statify();
			RepercutirPoder(humano);
			double antesH = humano.Ficha.BP;
			humano.Ficha.KO = true;
			AoPerderALuta(humano, algoz, morreu: false);
			Checa("O CONTRA-EXEMPLO: um HUMANO que perde a MESMA luta pro MESMO algoz no MESMO funil "
				  + "nao ganha nada -- sem esta linha, um `HasZenkai` que devolvesse `true` pra todo "
				  + "mundo passaria pela linha de cima",
				  Math.Abs(humano.Ficha.BP - antesH) < 1
				  && humano.Ficha.GainZenkai(1e9, NowMs()) == Fighter.ZenkaiResult.SemDnaSaiyajin,
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

		// ---- A SUPER PERFEITA E OUTRA PORTA: ela nao pede o sangue ----
		Checa("...mas a SUPER PERFEITA continua sendo dele: `Cell4()` nao olha raca nem DNA uma unica "
			  + "vez -- o dono acha que as duas sao a mesma porta, e no DM sao duas",
			  semDna.Ficha.bio_stage == BioAndroids.Perfeito
			  && semDna.Forma.Avaliar("super_perfect", semDna.Ficha.BP, 1, false, p) == RecusaForma.Pode,
			  $"estagio {semDna.Ficha.bio_stage}, "
			  + $"{semDna.Forma.Avaliar("super_perfect", semDna.Ficha.BP, 1, false, p)}");

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
		Numero("`DNL_BIO_EVO_PLAYERS` (jogadores por degrau)", BioAndroids.JogadoresPraEvoluir, 10);
		Numero("`DNL_BIO_EVO_NPC_WEIGHT` (um NPC vale meio jogador)", BioAndroids.PesoDoNpc, 0.5);
		Numero("`DNL_BIO_EVO_ANDROIDS` (o atalho do Cell: UM androide)",
			   BioAndroids.AndroidesPraEvoluir, 1);
		Numero("`DNL_BIO_EVO2_MULT` (semi-perfeito dobra o BP BASE)", BioAndroids.MultDoSemiPerfeito, 2);
		Numero("`DNL_BIO_EVO3_MULT` (perfeito quadruplica)", BioAndroids.MultDoPerfeito, 4);
		Numero("`cell4mult` (a Super Perfeita)", Catalogo.SuperPerfeitoMult, 8);
		Numero("`nerfSSJ()` no SSJ1 de quem chegou por `canSSJ`", Catalogo.Ssj1BaseDiluido, 1.35);
		Numero("o marco `bio1` (imperfeito)", Milestones.Valor("bio1"), 1.5);
		Numero("o marco `bio2` (semi-perfeito)", Milestones.Valor("bio2"), 2);
		Numero("o marco `bio3` (perfeito)", Milestones.Valor("bio3"), 3);
		Numero("o marco `bio_ssj2`", Milestones.Valor("bio_ssj2"), 4);
		Numero("`DNL_ABSORB_KI_PER_HIT` (6% do tanque por tiro engolido)", AbsorcaoPorGolpeDeKi, 0.06);
		Numero("o teto do androide de absorcao (o DOBRO do tanque)", TetoDoAndroideAbsorcao, 2);
		Numero("o nucleo infinito (2% por meio segundo = 4% por segundo)", RegenDoNucleoInfinito, 0.04);
		Numero("`DNL_INT_REQ` (tecnologia pro laboratorio)", TechDoLaboratorio, 70);
		Numero("o androide de ABSORCAO, em zeni", CustoAndroideAbsorcao, 1_000_000);
		Numero("o androide de ENERGIA INFINITA, em zeni", CustoAndroideInfinito, 2_000_000);
		Numero("`DNL_BIO_BREW_MONTHS 0.1` de ano, em DIAS in-game", DiasDeGestacao, 30);

		// ============================ A CARAPACA DA LARVA SE MEDE NA FICHA, E NAO NO `const` ============================
		// `BioLarvaRestrict` e privado do `Fighter`, e abrir a visibilidade dele pra uma bancada seria
		// trocar o desenho da classe pra medir um numero -- exatamente o tipo de conserto que deixa a
		// medida verde e o sistema pior. O numero se le pelo EFEITO, numa ficha crua de um milhao: se
		// o teto for 1%, sai dez mil, e nao ha segunda leitura possivel.
		// ==========================================================================================================
		var cru = new Fighter
		{
			Race = BioAndroids.Raca, ParentRace = BioAndroids.Raca,
			BP = 1_000_000, bio_lab_born = true, bio_stage = BioAndroids.Larva,
		};
		cru.Statify();
		cru.PowerLevel();
		Checa("a LARVA expressa 1% do proprio poder -- um milhao de base vira DEZ MIL na tela. Os "
			  + "tres comentarios do DM que dizem 5% e 10% estao desatualizados; o define e o juiz",
			  Math.Abs(cru.expressedBP - 10_000) <= 1, $"{cru.expressedBP:N0}");
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
		Injetar("o teto de 1% da LARVA (com o degrau em larva o poder expresso TEM que cair)",
				() => { bio.Ficha.bio_stage = BioAndroids.Larva; RepercutirPoder(bio); },
				() => bio.Ficha.expressedBP > bio.Ficha.BP * 0.5,
				() => { bio.Ficha.bio_stage = estagioGuardado; RepercutirPoder(bio); });

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
		ServerPlayer? soRaca = semDna ?? bio;
		Injetar("o ramo da RACA no `HasZenkai` (trocando a raca do bio SEM DNA, o predicado TEM que "
				+ "cair) -- e este e o defeito literal que ficou meses escrito e inalcancavel por "
				+ "causa do hifen",
				() => { soRaca.Ficha.Race = "Human"; soRaca.Ficha.ParentRace = "Human"; },
				() => soRaca.Ficha.HasZenkai(),
				() => { soRaca.Ficha.Race = soRaca.Race; soRaca.Ficha.ParentRace = soRaca.Race; });

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

		// ---- 6. A PORTA DA SUPER PERFEITA ----
		Injetar("a porta da SUPER PERFEITA (rebaixando o degrau pra semi-perfeito, ela TEM que "
				+ "fechar) -- e o `cell3 == 1` do `Cell4()`",
				() => bio.Ficha.bio_stage = BioAndroids.SemiPerfeito,
				() => bio.Forma.Avaliar("super_perfect", bio.Ficha.BP, 1, false, Perfil(bio))
					  == RecusaForma.Pode,
				() => bio.Ficha.bio_stage = estagioGuardado);

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
			Injetar("a contagem de ABSORCOES (com o degrau ja no PERFEITO a contagem para -- o teto "
					+ "da escada por absorcao)",
					() => { semDna.Ficha.bio_stage = BioAndroids.Perfeito;
							semDna.Ficha.bio_abs_players = 0; semDna.Ficha.bio_abs_androids = 0; },
					() => { ContarAbsorcaoDoBio(semDna, semDna);
							return semDna.Ficha.bio_abs_players > 0
								   || semDna.Ficha.bio_abs_androids > 0; },
					() => semDna.Ficha.bio_stage = est2);
		}

		bio.Ficha.BP = bpGuardado;
		bio.Ficha.bio_lab_born = nascidoGuardado;
		bio.Ficha.Statify();
	}
}
