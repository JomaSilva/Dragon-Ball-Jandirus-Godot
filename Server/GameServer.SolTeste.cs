using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DO SOL LETAL E DO ARREMESSO NO ESPACO (`--solteste`).
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// A bancada `sol` do AssetPipeline prova as CONTAS (a curva de dano, o piso, o teto suave, a
/// geometria da coroa). Ela nao pode provar a CORRENTE, que e onde este port ja quebrou muitas
/// vezes -- e a spec pede exatamente a corrente:
///
///     estar no espaco -> `TickDoEspaco` perguntar pela estrela -> o corpo ser ferido pelo funil de
///     verdade -> `DeveMorrer` -> `Morrer()` -> o prazo de renascer.
///
/// E o item 4 do pedido do dono e uma regra da casa (0.7): *"um teto que nunca dispara e
/// indistinguivel de teto nenhum -- a bancada tem que fazer alguem morrer no sol de verdade"*. Aqui
/// morrem oito corpos, e o mais forte deles tem BP de 1e13.
/// ==============================================================================
///
/// ============================ E ELA MEDE O TEMPO COM A CURA LIGADA ============================
/// A regeneracao passiva devolve 1,67 de vida por segundo (`CombatKnobs.RegenPorSegundo`) e ela NAO e desligada
/// pela estrela. Uma bancada que ignorasse isso mediria outro jogo -- no caso mais leve a cura vale
/// um terco do dano. Por isso o laco chama <see cref="RegenerarPassivo"/>, que e a MESMA funcao que
/// o `TickCombate` chama, e nao uma copia das cinco linhas.
/// ========================================================================================
///
///     Godot --headless -- --server --solteste
/// </summary>
public partial class GameServer
{
	private bool _solDeTeste;

	/// <summary>
	/// OS DECALQUES QUE SAIRAM, por zona e tipo. Ver a linha de escuta em
	/// <see cref="MandarDecalque"/>.
	///
	/// Ela existe pra uma pergunta que so se responde por AUSENCIA: o arremesso no vacuo carimbava
	/// sulco de terra e cratera no nada, e mandava o pacote pro `ZoneList` do espaco -- que e o
	/// universo inteiro. Um pacote que NAO devia sair nao deixa rastro em campo nenhum.
	/// </summary>
	internal static List<(ulong Zona, Protocol.Decal Tipo)>? EscutaDeDecalques;

	/// <summary>Faixa de ids desta bancada -- longe do `_nextId` e da faixa do convivio (90.100).</summary>
	private const int IdBaseDoSolDeTeste = 90_400;

	/// <summary>
	/// Teto de tiques por medicao. 30 Hz x 6.000 = 200 s in-game, bem acima dos ~75 s que o corpo
	/// mais forte do jogo dura na estrela mais fraca. Se a medicao bater no teto, ela FALHA -- e o
	/// teto e o que impede um laco infinito no dia em que o piso do dano for mexido pra baixo.
	/// </summary>
	private const int TiquesMaximosDaMedicao = 6_000;

	// =====================================================================
	// A BANCADA
	// =====================================================================
	private void RodarBancadaDoSol()
	{
		GD.Print("\n===== BANCADA AO VIVO DO SOL LETAL E DO ARREMESSO NO ESPACO =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		AGeometriaDaEstrela(Checa);
		OAvisoChegaAntesDoDano(Checa);
		AlguemMorreNoSol(Checa);
		OForteDuraMaisQueOFraco(Checa);
		SairFunciona(Checa);
		APortaDaMorteEUnica(Checa);
		OArremessoAndaNoEspaco(Checa);

		GD.Print($"===== BANCADA DO SOL: {ok} OK, {falhou} FALHA(S) =====\n");
	}

	// =====================================================================
	// 1) A GEOMETRIA
	// =====================================================================
	/// <summary>
	/// O `EstrelaPerto` responde as tres perguntas certas, e o caso ADVERSARIAL entra: um ponto na
	/// divisa de duas celulas, onde a estrela "certa" e a da celula vizinha.
	/// </summary>
	private void AGeometriaDaEstrela(Action<string, bool, string> Checa)
	{
		GD.Print("--- 1) A GEOMETRIA DA ESTRELA ---");

		Estrela e = EstrelaDeTeste(ClasseDeEstrela.AnaVermelha, out SistemaSolar sis);
		Checa("acho uma ana vermelha gerada perto da origem",
			  e.Raio > 0 && !sis.Ancorado, $"raio {e.Raio}");

		bool no = Sistemas.EstrelaPerto(SeedDoUniverso, e.Pos, out Estrela achada, out double d0);
		Checa("no CENTRO dela, `EstrelaPerto` devolve ela mesma a distancia zero",
			  no && achada.Pos.Equals(e.Pos) && d0 < 1, $"{d0:0.##}");

		Checa("o centro esta DENTRO",
			  CalorDaEstrela.Onde(e, 0) == ProximidadeDaEstrela.Dentro, "");
		Checa("um pixel dentro da borda ainda esta DENTRO",
			  CalorDaEstrela.Onde(e, e.Raio - 1) == ProximidadeDaEstrela.Dentro, "");
		Checa("um pixel FORA da borda ja e so COROA (nao queima)",
			  CalorDaEstrela.Onde(e, e.Raio + 1) == ProximidadeDaEstrela.Coroa, "");
		Checa($"a coroa acaba em {CalorDaEstrela.RazaoDaCoroa}x o raio",
			  CalorDaEstrela.Onde(e, e.Raio * (float)CalorDaEstrela.RazaoDaCoroa + 1)
				  == ProximidadeDaEstrela.Longe, "");

		// A HISTERESE: quem JA foi avisado so sai da coroa em 2,0x. Sem ela um corpo parado em cima
		// da divisa entraria e sairia a cada tique e o aviso viraria metralhadora.
		Checa("com a histerese, quem ja foi avisado continua na coroa em 1,9x o raio",
			  CalorDaEstrela.Onde(e, e.Raio * 1.9, jaAvisado: true) == ProximidadeDaEstrela.Coroa, "");
		Checa("...e so sai em 2,1x",
			  CalorDaEstrela.Onde(e, e.Raio * 2.1, jaAvisado: true) == ProximidadeDaEstrela.Longe, "");

		// ============================ O CASO ADVERSARIAL: A DIVISA DA CELULA ============================
		// Este e o unico jeito de a deteccao errar: um corpo na quina da celula, com a estrela do
		// outro lado da divisa. Se a varredura fosse de UMA celula (a propria), ele nao veria nada --
		// e essa e a falha que aparece numa fracao fina do universo, ou seja nunca em teste.
		// ==========================================================================================
		var naDivisa = new Vec2((float)(sis.Id.Sx * Sistemas.CelulaPx), (float)(sis.Id.Sy * Sistemas.CelulaPx));
		Checa("na QUINA da celula a varredura ainda acha alguma estrela (3x3 celulas)",
			  Sistemas.EstrelaPerto(SeedDoUniverso, naDivisa, out _, out _), "");

		// E O TETO DO RAIO CABE NA CELULA -- a razao pela qual 3x3 basta e aritmetica, nao empirica.
		Checa("o alcance maximo de uma coroa cabe folgado numa celula",
			  Sistemas.RaioLetalMaximo * CalorDaEstrela.RazaoDaCoroa < Sistemas.CelulaPx / 2,
			  $"{Sistemas.RaioLetalMaximo * CalorDaEstrela.RazaoDaCoroa:0} px contra {Sistemas.CelulaPx / 2:0}");
	}

	// =====================================================================
	// 2) O AVISO CHEGA ANTES DO DANO
	// =====================================================================
	/// <summary>
	/// O item 3 do pedido do dono: *"um sol que mata sem sinal e um bug pro jogador"*.
	///
	/// A prova tem DUAS metades e as duas importam: o aviso SAI, e a vida NAO ANDA no mesmo instante.
	/// Um aviso que ja custa vida nao e aviso, e o comeco da punicao.
	/// </summary>
	private void OAvisoChegaAntesDoDano(Action<string, bool, string> Checa)
	{
		GD.Print("--- 2) O AVISO DA COROA CHEGA ANTES DO PRIMEIRO PONTO DE VIDA ---");

		Estrela e = EstrelaDeTeste(ClasseDeEstrela.Amarela, out _);
		// 1,2x o raio: dentro da coroa e bem abaixo da orbita mais interna (raio + 900), pra o
		// `PlanetaSob` do mesmo tique nao ter chance de encontrar um mundo neste ponto.
		ServerPlayer pl = Forjar(1, "bancada: na coroa", NoRaio(e, 1.2f), 5_000_000);

		try
		{
			EscutaDeAvisos = [];
			double antes = pl.Combate!.Corpo.Vida();
			TickDoEspaco(pl);
			List<string> ditos = EscutaDeAvisos!;
			EscutaDeAvisos = null;

			Checa("na coroa o servidor AVISA", ditos.Count > 0, "nenhuma linha");
			Checa("...e o aviso diz quanto tempo o corpo aguenta la dentro",
				  ditos.Any(l => l.Contains(" s") && l.Contains("cede")), string.Join(" | ", ditos));
			Checa("...e a vida NAO andou (coroa avisa, nao queima)",
				  Math.Abs(pl.Combate.Corpo.Vida() - antes) < 1e-9,
				  $"{antes:0.###} -> {pl.Combate.Corpo.Vida():0.###}");

			// E ELE NAO SE REPETE: o segundo tique na coroa nao pode dizer nada.
			EscutaDeAvisos = [];
			for (int i = 0; i < 60; i++) TickDoEspaco(pl);
			int repetidos = EscutaDeAvisos!.Count;
			EscutaDeAvisos = null;
			Checa("...e 60 tiques depois ele nao repetiu (o aviso nao vira metralhadora)",
				  repetidos == 0, $"{repetidos} linha(s)");
		}
		finally { Recolher(pl); }
	}

	// =====================================================================
	// 3) ALGUEM MORRE NO SOL DE VERDADE
	// =====================================================================
	private void AlguemMorreNoSol(Action<string, bool, string> Checa)
	{
		GD.Print("--- 3) ALGUEM MORRE NO SOL (pelo funil de verdade) ---");

		Estrela e = EstrelaDeTeste(ClasseDeEstrela.Amarela, out _);
		ServerPlayer pl = Forjar(1, "bancada: o incinerado", e.Pos, 1_000_000);

		try
		{
			EscutaDeAvisos = [];
			(bool morreu, double s, double queimou) = Cozinhar(pl);
			List<string> ditos = EscutaDeAvisos!;
			EscutaDeAvisos = null;

			Checa($"o corpo MORREU dentro da estrela em {s:0.0} s", morreu, $"{s:0.0} s");
			Checa("...e perdeu vida ANTES de morrer (foi dano, nao morte instantanea)",
				  queimou > 0 && s > 1, $"{queimou:0.#} de vida em {s:0.00} s");
			Checa("...com a bandeira `dead` levantada pelo `Morrer()`", pl.Ficha.dead, "");
			Checa("...e com o prazo de renascer marcado (o mesmo de qualquer morte)",
				  pl.RenasceEm > 0, $"{pl.RenasceEm}");
			Checa("...e o jogador foi avisado do que o matou",
				  ditos.Any(l => l.Contains("consumido")), string.Join(" | ", ditos.TakeLast(3)));

			// A ESTIMATIVA DA TELA TEM QUE BATER COM O RELOGIO. Ela e o numero que o jogador le no
			// aviso da coroa e no painel; se ela mentir, o aviso engana em vez de ensinar.
			double previsto = CalorDaEstrela.SegundosAteCeder(e, 1_000_000, 100, CombatKnobs.RegenPorSegundo);
			Checa($"a estimativa da tela ({previsto:0.0} s) bate com o relogio ({s:0.0} s)",
				  Math.Abs(previsto - s) < previsto * 0.15 + 0.5, $"previsto {previsto:0.00}, medido {s:0.00}");
		}
		finally { Recolher(pl); }
	}

	// =====================================================================
	// 4) O FORTE DURA MAIS -- E O MAIS FORTE DE TODOS AINDA MORRE
	// =====================================================================
	/// <summary>
	/// O CORACAO DO DESENHO, e a razao de o dano nao ser plano: com 100 de vida por membro pra todo
	/// mundo, dano plano daria o MESMO tempo de vida pro recem-nascido e pro Super Saiyajin.
	///
	/// E o teto de verdade esta na ultima linha: o corpo de 1e13 de BP -- o mesmo que a
	/// `--formasteste` usa como "mais forte do jogo" -- tem que MORRER. Se ele nao morresse, o piso
	/// do dano estaria abaixo da regeneracao e o sol seria um teto que nunca dispara.
	/// </summary>
	private void OForteDuraMaisQueOFraco(Action<string, bool, string> Checa)
	{
		GD.Print("--- 4) O FORTE DURA MAIS QUE O FRACO (e o mais forte tambem morre) ---");

		Estrela e = EstrelaDeTeste(ClasseDeEstrela.Amarela, out _);
		GD.Print($"       estrela: {e.Classe}, raio {e.Raio:0} px, poder {CalorDaEstrela.PoderDe(e.Classe):N0}");

		(string rotulo, double bp)[] corpos =
		[
			("recem-nascido", 25),
			("lutador feito", 100_000),
			("Super Saiyajin novo", 2_000_000),
			("veterano", 50_000_000),
			("o mais forte do jogo", 1e13),
		];

		var tempos = new List<double>();
		foreach ((string rotulo, double bp) in corpos)
		{
			ServerPlayer pl = Forjar(1, $"bancada: {rotulo}", e.Pos, bp);
			try
			{
				(bool morreu, double s, _) = Cozinhar(pl);
				tempos.Add(s);
				GD.Print($"       {rotulo,-22} BP {bp,12:0.0e+0}  dps {CalorDaEstrela.DanoPorSegundo(e, pl.Ficha.expressedBP),6:0.0}  morreu em {s,6:0.0} s");
				Checa($"{rotulo}: morreu dentro da estrela", morreu, $"{s:0.0} s");
			}
			finally { Recolher(pl); }
		}

		for (int i = 1; i < tempos.Count; i++)
			Checa($"{corpos[i].rotulo} dura mais que {corpos[i - 1].rotulo}",
				  tempos[i] > tempos[i - 1], $"{tempos[i]:0.00} s contra {tempos[i - 1]:0.00} s");

		// ============================ NEM INSTANTANEO NEM ETERNO ============================
		// Os dois extremos sao afirmados com NUMERO e nao com "parece razoavel", porque os dois
		// extremos sao os dois jeitos de este sistema deixar de ser o que o dono pediu: menos de
		// meio segundo e morte instantanea com outro nome; nunca morrer e nao ter sistema.
		// ==============================================================================
		Checa("o corpo mais fraco ainda leva mais de meio segundo (nao e morte instantanea)",
			  tempos[0] > 0.5, $"{tempos[0]:0.00} s");
		Checa("e mesmo o mais forte nao passa do teto da medicao (ninguem e imune)",
			  tempos[^1] < TiquesMaximosDaMedicao * Protocol.TickSeconds, $"{tempos[^1]:0.0} s");

		// ============================ A PERGUNTA QUE VALE E "DA PRA SAIR?", E ELA E DERIVADA ============================
		// A primeira versao desta linha afirmava "mais de 30 s" -- um numero escolhido a mao, e ele
		// REPROVOU com 22,6 s. A medicao estava certa e a afirmacao e que era arbitraria: 30 saiu do
		// tempo que o corpo dura numa ANA VERMELHA, e esta secao roda numa AMARELA.
		//
		// A pergunta que importa nao e "quantos segundos": e se o tempo que o corpo aguenta e maior que
		// o tempo de ATRAVESSAR aquela estrela. Isso e uma comparacao entre dois numeros do proprio
		// sistema, e ela e o que o jogador sente -- "da pra sair nadando ou nao".
		//
		// E O CONTRASTE E A METADE MAIS IMPORTANTE: numa gigante azul a resposta tem que ser NAO, pro
		// mesmo corpo. Se as duas dessem "sim", a escada de oito classes nao estaria fazendo nada.
		// ==========================================================================================================
		double travessia = Espaco.SegundosDeViagem(e.Raio * 2);
		Checa($"o mais forte atravessa um sol amarelo ({tempos[^1]:0.0} s de vida contra {travessia:0.0} s de travessia)",
			  tempos[^1] > travessia, $"{tempos[^1]:0.0} s contra {travessia:0.0} s");

		Estrela gigante = EstrelaDeTeste(ClasseDeEstrela.GiganteAzul, out _);
		ServerPlayer naGigante = Forjar(1, "bancada: o mais forte na gigante", gigante.Pos, 1e13);
		try
		{
			(bool morreu, double s, _) = Cozinhar(naGigante);
			double travessiaG = Espaco.SegundosDeViagem(gigante.Raio * 2);
			GD.Print($"       gigante azul: raio {gigante.Raio:0} px, travessia {travessiaG:0.0} s"
					 + $" | o corpo de 1e13 dura {s:0.0} s");
			Checa("o mais forte tambem morre numa gigante azul", morreu, $"{s:0.0} s");
			Checa($"...e NAO a atravessa ({s:0.0} s de vida contra {travessiaG:0.0} s de travessia)",
				  s < travessiaG, $"{s:0.0} s contra {travessiaG:0.0} s");
			Checa("...ou seja: a classe da estrela decide quem passa e quem nao passa",
				  tempos[^1] > travessia && s < travessiaG, "");
		}
		finally { Recolher(naGigante); }
	}

	// =====================================================================
	// 5) SAIR FUNCIONA
	// =====================================================================
	/// <summary>
	/// *"dano por segundo deixa o jogador tentar sair"*. Entao sair tem que FUNCIONAR: o dano para,
	/// o corpo nao morre, e o servidor diz o preco.
	/// </summary>
	private void SairFunciona(Action<string, bool, string> Checa)
	{
		GD.Print("--- 5) SAIR DA ESTRELA PARA O DANO ---");

		Estrela e = EstrelaDeTeste(ClasseDeEstrela.Amarela, out _);
		ServerPlayer pl = Forjar(1, "bancada: o que escapou", e.Pos, 5_000_000);

		try
		{
			// meio segundo dentro
			for (int i = 0; i < 15; i++) { RegenerarPassivo(pl, Protocol.TickSeconds); TickDoEspaco(pl); }
			double dentro = pl.Combate!.Corpo.Vida();
			Checa("meio segundo dentro ja custou vida", dentro < 100, $"{dentro:0.##}");

			// SAI PELA BORDA -- pra COROA e nao pro infinito: e a saida que um corpo nadando faria.
			EscutaDeAvisos = [];
			pl.Pos = NoRaio(e, 1.2f);
			for (int i = 0; i < 30; i++) { RegenerarPassivo(pl, Protocol.TickSeconds); TickDoEspaco(pl); }
			List<string> ditos = EscutaDeAvisos!;
			EscutaDeAvisos = null;

			Checa("fora do raio a vida PARA de cair (e volta a subir pela regeneracao)",
				  pl.Combate.Corpo.Vida() >= dentro, $"{dentro:0.##} -> {pl.Combate.Corpo.Vida():0.##}");
			Checa("...e o servidor diz o preco da escapada",
				  ditos.Any(l => l.Contains("escapa")), string.Join(" | ", ditos));
			Checa("...e o corpo esta vivo", !pl.Ficha.dead, "");
		}
		finally { Recolher(pl); }
	}

	// =====================================================================
	// 6) A PORTA DA MORTE E UNICA
	// =====================================================================
	/// <summary>
	/// O sol nao pode ser uma SEGUNDA maneira de morrer. Quem se pendura no
	/// <see cref="CombatState.NegarMorte"/> -- hoje a Aura of Destruction -- tem que barrar a morte
	/// pelo sol tambem, e o servidor nao pode anunciar a morte de quem nao morreu.
	/// </summary>
	private void APortaDaMorteEUnica(Action<string, bool, string> Checa)
	{
		GD.Print("--- 6) O SEGURO CONTRA A MORTE VALE CONTRA O SOL ---");

		Estrela e = EstrelaDeTeste(ClasseDeEstrela.GiganteAzul, out _);
		ServerPlayer pl = Forjar(1, "bancada: o que nega a morte", e.Pos, 1000);

		try
		{
			int negacoes = 0;
			pl.Combate!.NegarMorte = _ => { negacoes++; return true; };

			EscutaDeAvisos = [];
			for (int i = 0; i < 300; i++) { RegenerarPassivo(pl, Protocol.TickSeconds); TickDoEspaco(pl); }
			List<string> ditos = EscutaDeAvisos!;
			EscutaDeAvisos = null;

			Checa("o gancho FOI consultado (o sol passa pela porta unica)", negacoes > 0, $"{negacoes}");
			Checa("...e o corpo NAO morreu", !pl.Ficha.dead, "");
			Checa("...e o servidor NAO anunciou a morte de quem nao morreu",
				  !ditos.Any(l => l.Contains("consumido")), string.Join(" | ", ditos.TakeLast(3)));
			Checa("...e ele continua queimando (a aura barra a morte, nao tira do fogo)",
				  pl.Combate.Corpo.Vida() < 100, $"{pl.Combate.Corpo.Vida():0.##}");
		}
		finally { Recolher(pl); }
	}

	// =====================================================================
	// 7) O ARREMESSO ANDA NO ESPACO -- E JOGA ALGUEM NO SOL
	// =====================================================================
	/// <summary>
	/// O item 2 do pedido do dono, e a "Prova" que a propria spec escreveu: *"uma [bancada] que
	/// arremessa um corpo no sol e confere a morte"*.
	///
	/// ============================ O QUE ELA DESCOBRIU SOBRE O CAMINHO ============================
	/// A spec suspeitava que o `TickDoEmpurrao` nao andasse no espaco por causa de `_colisao = null`.
	/// A suspeita nao se confirma e esta secao e quem MEDE isso: o `mapa` nulo faz o laco de parede
	/// ser PULADO, nao o passo -- o corpo anda os 640 px do arremesso maximo no vacuo como andaria em
	/// terra. (`_colisao` e campo do CLIENTE, `Client/World.cs`; o servidor le
	/// `_catalogo.Get(zona)?.Mapa`, que devolve nulo pra "Espaco" porque a zona nao esta no manifesto.)
	///
	/// O QUE ESTAVA QUEBRADO ERA OUTRA COISA, e sem esta bancada ninguem veria: o arremesso carimbava
	/// SULCO DE TERRA e CRATERA no vacuo, e mandava o pacote pro `ZoneList` do espaco -- que e o
	/// universo inteiro. Ver `RastroVale`.
	/// =========================================================================================
	/// </summary>
	private void OArremessoAndaNoEspaco(Action<string, bool, string> Checa)
	{
		GD.Print("--- 7) O ARREMESSO ANDA NO ESPACO, E JOGA ALGUEM NO SOL ---");

		Estrela e = EstrelaDeTeste(ClasseDeEstrela.Amarela, out _);

		// ============================ GEOMETRIA DO ARREMESSO, E O QUE ELA REVELA ============================
		// O alvo comeca ABAIXO da estrela e quem bate esta abaixo dele olhando pro NORTE --
		// `MeleeArea.Frente(North)` e (0,-1), ou seja o corpo sobe direto pro centro.
		//
		// E AQUI ESTA O NUMERO QUE DECIDE O QUE "JOGAR ALGUEM NO SOL" SIGNIFICA NESTE JOGO: o
		// arremesso mais longo que existe vale `TilesPorTique * TileSize * TiquesMax` = **640 px**, e
		// e um teto do proprio DU ("mantem o lado forte sendo arremessado, mas SEM voar o mapa
		// inteiro" -- `Empurrao`). Nenhum golpe atravessa a coroa de uma gigante (3.584 px): jogar
		// alguem no sol e empurra-lo pelos ultimos metros, nao lanca-lo de longe. Por isso o alvo
		// nasce a 300 px da superficie.
		// =============================================================================================
		var ondeOAlvoComeca = new Vec2(e.Pos.X, e.Pos.Y + e.Raio + 300);

		ServerPlayer alvo = Forjar(1, "bancada: o arremessado", ondeOAlvoComeca, 200_000);
		ServerPlayer bruto = Forjar(2, "bancada: quem arremessa",
									new Vec2(ondeOAlvoComeca.X, ondeOAlvoComeca.Y + 40), 1e12);

		try
		{
			bruto.Facing = Facing.North;

			EscutaDeDecalques = [];
			Vec2 saiuDe = alvo.Pos;

			// A FUNCAO DE PRODUCAO, e nao um atalho: e a mesma que o `Atacar` chama depois de o dano
			// ser resolvido (`GameServer.Combat.cs:319`).
			TentarEmpurrar(bruto, alvo, dmg: 30, Protocol.Golpe.Pesado);
			Checa("um golpe pesado de 1e12 de BP ARREMESSA no espaco",
				  alvo.TiquesDeVoo > 0, $"{alvo.TiquesDeVoo} tiques");

			// O ESPERADO SAI DOS TIQUES QUE O `Avaliar` DEU, e nao do teto da classe: com dano 30 a
			// formula do DU devolve 9 e nao 10 (`round(min(max(30,5)/1.5, 9))`), e cravar 10 aqui
			// faria a bancada reprovar um arremesso perfeito por 64 px.
			double esperado = alvo.TiquesIniciaisDoVoo * Empurrao.TilesPorTique * ZoneCollision.TileSize;

			// E O ARREMESSO ANDA: `TickDoEmpurrao` e o laco de producao, e ele varre `_players`.
			int voltas = 0;
			while (alvo.TiquesDeVoo > 0 && voltas++ < 200) TickDoEmpurrao();

			double andou = (alvo.Pos - saiuDe).Length;
			Checa($"o corpo ANDOU {andou:0} px no vacuo (o mapa nulo pula a parede, nao o passo)",
				  andou > esperado * 0.98, $"{andou:0} px de {esperado:0}");
			Checa("...e a direcao foi a do golpe (pra dentro da estrela)",
				  alvo.Pos.Y < saiuDe.Y, $"{saiuDe.Y:0} -> {alvo.Pos.Y:0}");

			double aoCentro = (alvo.Pos - e.Pos).Length;
			Checa($"...e ele parou DENTRO do raio letal ({aoCentro:0} px de {e.Raio:0})",
				  aoCentro <= e.Raio, $"{aoCentro:0} px");

			// ============================ NADA DE CRATERA NO VACUO ============================
			// A guarda antiga era `Altitude <= 0`, e no espaco a altitude E zero -- a altitude e um
			// sistema de planeta. Todo arremesso de 4 tiques ou mais carimbava sulco e cratera no
			// nada, pro universo inteiro. Ver `RastroVale`.
			// ==============================================================================
			List<(ulong Zona, Protocol.Decal Tipo)> decalques = EscutaDeDecalques!;
			EscutaDeDecalques = null;
			Checa("...e NAO carimbou sulco nem cratera no vacuo",
				  decalques.Count == 0,
				  string.Join(",", decalques.Select(d => d.Tipo.ToString()).Distinct()));

			// E AGORA ELE COZINHA. E a frase da spec, fechada: arremessou, caiu no sol, morreu.
			EscutaDeAvisos = [];
			(bool morreu, double s, double queimou) = Cozinhar(alvo);
			EscutaDeAvisos = null;

			Checa($"...e MORREU no sol {s:0.0} s depois de cair nele", morreu, $"{s:0.0} s");
			Checa("...tendo perdido vida no caminho", queimou > 0, $"{queimou:0.#}");
		}
		finally { Recolher(alvo); Recolher(bruto); }
	}

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// COZINHA O CORPO ate ele morrer, e devolve quanto tempo levou.
	///
	/// ============================ TRES FUNCOES DE PRODUCAO, NA CADENCIA DE PRODUCAO ============================
	/// Este laco NAO e um tique inventado: ele repete, na ordem e no ritmo do servidor de verdade, as
	/// tres coisas que decidem quanto tempo um corpo dura dentro de uma estrela.
	///
	///   * <see cref="RegenerarPassivo"/>, a 30 Hz -- e o que o `TickCombate` chama. Sem ela a
	///     medicao seria de outro jogo: no caso mais leve a cura vale um terco do dano;
	///   * <see cref="TickDoEspaco"/>, a 30 Hz -- quem pergunta pela estrela e fere o corpo;
	///   * `Ficha.Tick()`, a 5 Hz (`TicksPorFicha` = 6 de 30) -- **e ela e a que ninguem lembraria**.
	///     `expressedBP` nao e um campo estavel: ele e RECALCULADO, e cai pela metade quando o corpo
	///     se machuca (`hpratio`) e pra um DECIMO no nocaute (`Fighter.Power.cs:125`). Como o dano da
	///     estrela escala com o `expressedBP`, o corpo queima cada vez MAIS RAPIDO conforme cede --
	///     e uma bancada com o BP congelado mediria um tempo maior do que o jogo entrega.
	/// =====================================================================================================
	/// </summary>
	private (bool Morreu, double Segundos, double Queimou) Cozinhar(ServerPlayer pl)
	{
		double vidaInicial = pl.Combate!.Corpo.Vida();
		double maisQueimado = 0;

		for (int t = 0; t < TiquesMaximosDaMedicao; t++)
		{
			if (t % TicksPorFicha == 0) pl.Ficha.Tick(agoraMs: NowMs());
			RegenerarPassivo(pl, Protocol.TickSeconds);
			TickDoEspaco(pl);
			maisQueimado = Math.Max(maisQueimado, vidaInicial - pl.Combate.Corpo.Vida());
			if (pl.Ficha.dead) return (true, (t + 1) * Protocol.TickSeconds, maisQueimado);
		}
		return (false, TiquesMaximosDaMedicao * Protocol.TickSeconds, maisQueimado);
	}

	/// <summary>
	/// A PRIMEIRA ESTRELA GERADA DESTA CLASSE, varrendo celulas em espiral quadrada a partir da
	/// origem. Deterministica: mesma seed, mesma estrela, mesma bancada.
	///
	/// SO GERADAS (`!Ancorado`): um sistema ancorado nasce em cima de um pre-feito, e cozinhar
	/// corpos ao lado da Terra e o tipo de teste que um dia mexe em quem esta jogando.
	/// </summary>
	private Estrela EstrelaDeTeste(ClasseDeEstrela classe, out SistemaSolar sistema)
	{
		for (int anel = 0; anel < 40; anel++)
			for (int dy = -anel; dy <= anel; dy++)
				for (int dx = -anel; dx <= anel; dx++)
				{
					if (Math.Abs(dx) != anel && Math.Abs(dy) != anel) continue;   // so a casca
					if (Sistemas.Do(SeedDoUniverso, dx, dy) is not { } s) continue;
					if (s.Ancorado || s.Estrela.Classe != classe) continue;
					sistema = s;
					return s.Estrela;
				}

		// FALHA ALTO: uma classe que nao existe em 6.400 celulas e um sinal de que a tabela de
		// pesos mudou, e uma bancada que devolvesse `default` mediria uma estrela de raio zero.
		GD.PushError($"[bancada] nao achei nenhuma estrela {classe} gerada em 40 aneis de celulas");
		sistema = default;
		return default;
	}

	/// <summary>Um ponto a `fator` vezes o raio da estrela, na vertical. Deterministico.</summary>
	private static Vec2 NoRaio(Estrela e, float fator) => new(e.Pos.X, e.Pos.Y + e.Raio * fator);

	/// <summary>
	/// UM CORPO DE BANCADA no espaco, com o BP pedido. Mesmo padrao do `GameServer.ConvivioTeste`:
	/// entra no `_players` e na `ZoneList` porque as funcoes de producao varrem as duas, e sai no
	/// `finally` -- entra e sai dentro do mesmo bloco sincrono, entao nenhum tique do jogo o ve.
	/// </summary>
	private ServerPlayer Forjar(int i, string nome, Vec2 onde, double bp)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDoSolDeTeste + i,
			Peer = null,
			Name = nome,
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = ZonaDoEspaco,
			Pos = onde,
			Conta = $"bancada_sol_{i}",
			Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = bp },
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.ChunkAtual = ChunkId.De(onde);

		// `PorNoMundo` chama `Statify` (os ATRIBUTOS) e nao `PowerLevel` (o PODER). Sem esta linha
		// o `expressedBP` nasce ZERO, e como o dano da estrela e uma razao contra ele, a bancada
		// mediria todo mundo -- do recem-nascido ao de 1e13 -- morrendo no teto do fator.
		novo.Ficha.Tick(agoraMs: NowMs());
		return novo;
	}

	private void Recolher(ServerPlayer pl)
	{
		_players.Remove(pl.Id);
		ZoneList(pl.Zone.Hash).Remove(pl);
	}
}
