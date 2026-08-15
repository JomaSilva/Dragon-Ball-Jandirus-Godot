using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--borraoteste` -- OS DOIS RELATOS DO DONO SOBRE O DASH DO NPC, **SO MEDIDOS**.
///
/// Ela nao conserta nada e nao afirma quase nada: ela DESPEJA DISTRIBUICOES. A fase e de medicao,
/// e a pergunta que o dono levantou tem tres respostas possiveis que so um numero separa.
///
/// ============================ O QUE O DONO DISSE, PALAVRA POR PALAVRA ============================
///   1. *"npcs quando usam DASH n ficam com o EFEITO DE BLUR igual os jogadores"*;
///   2. *"o RANGE DO TELEPORTE DO DASH ta mt grande, parece EXTREMAMENTE MAIOR q os dos jogadores"*.
/// ==================================================================================================
///
/// ============================ AS SEIS FAMILIAS, E O QUE CADA UMA RESPONDE ============================
///   A. O SALTO, EM PIXELS, dos dois lados -- varredura de distancia por distancia, pelo MESMO
///      <see cref="Aproximar"/> que os dois usam. Ela separa os tres casos que o jogador tem (leve,
///      pesado sem marca, pesado COM marca) do unico que a IA tem (pesado, sempre marcado).
///   B. O `Correndo` DO CEREBRO -- quantos comandos por mil pedem corrida, perseguindo e fugindo. E
///      o bit que o `EntityState.Correndo` carrega e que liga o `RastroDeCorrida` (o motion blur) no
///      corpo REMOTO. Se ele for zero na perseguicao, nenhum NPC pode borrar enquanto arranca.
///   C. A IMAGEM REMANESCENTE NO SORTEIO -- de cada cem NPCs tirados pelo funil de producao
///      (`SorteioDeNpc.Sortear`, o mesmo do `NascerNpc`), quantos sabem `/datum/skill/ki/Afterimage`?
///      E o gate do vulto do arranque (`zanzo = investiu &amp;&amp; a.Livro.Sabe(...)`).
///   D. O ANUNCIO DO SALTO -- o que sai pelo fio quando um corpo arranca, lido no CARIMBO do escritor
///      de verdade (`w.Length`), pelos tres corpos: jogador, NPC e corpo POSSUIDO.
///   E. OS SETE DEFEITOS INJETADOS -- cada afirmacao central posta contra o defeito que ela existe pra
///      pegar, incluindo o portao ANTIGO do borrao (`if (zanzo)`), que era o relato 1 inteiro.
///   F. O MESMO VAO, O MESMO PIXEL -- a IA e o jogador medidos no MESMO vao, e comparados contra a
///      REGRA (`vao - DistanciaDeParada`) e nao so um contra o outro.
/// ======================================================================================================
///
/// ============================ A IRMA DELA E A `--diagborrao`, E ELA NAO PODE SER ESTA ============================
/// Tudo aqui e estado de SERVIDOR. **Esta bancada fecharia verde com o borrao nunca desenhado na tela**:
/// entre o `w.Length` do pacote e o pixel ha o fio, o `GameClient`, o `World.AoPiscar`, a escolha da
/// origem no `LocalPlayer` e o `RastroDeCorrida` esperando a posicao de CHEGADA. Ver `RoboDeBorrao` e
/// `ver-o-borrao.bat`.
/// ==============================================================================================================
/// </summary>
public partial class GameServer
{
	private int _borraoOk, _borraoFalhou;

	private void AfirmarBorrao(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _borraoOk++; GD.Print($"[borrao]   OK    {oque}"); return; }
		_borraoFalhou++;
		GD.PrintErr($"[borrao]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDoBorrao()
	{
		_borraoOk = _borraoFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[borrao] ======= O DASH DO NPC: ALCANCE E BORRAO, MEDIDOS =======");
		AfirmarBorrao("a zona da bancada tem colisao carregada", _pjMapa != null);

		FamiliaSaltoDosDoisLados();
		FamiliaOBitDeCorrida();
		FamiliaVultoNoSorteio();
		FamiliaOAnuncioDoSalto();
		FamiliaOMesmoVaoOMesmoPixel();
		FamiliaODefeitoInjetado();

		LimparTudoDaBancada();
		GD.Print($"[borrao] ===== FIM: {_borraoOk} ok, {_borraoFalhou} falha(s) =====");
	}

	// =====================================================================
	// A -- O SALTO, EM PIXELS, DOS DOIS LADOS
	// =====================================================================
	/// <summary>
	/// A DISTRIBUICAO DO ARRANQUE, e nao um caso. Varre a distancia entre os dois corpos de 40 a 520
	/// px de 8 em 8 e mede quanto o corpo ANDOU em cada uma, nos quatro modos que o jogo tem.
	///
	/// O corpo e SEMPRE o mesmo (mesmo BP, mesmo Ki, mesmo corredor): o unico que muda entre as
	/// colunas e a TECLA e a MARCA. E o que separa "a regra e outra pra IA" de "a IA aperta outra
	/// tecla".
	/// </summary>
	private void FamiliaSaltoDosDoisLados()
	{
		GD.Print("[borrao] ---- A: o salto do arranque, pixel a pixel, dos dois lados ----");

		Vec2 onde = CorredorLivre(30);

		// UM ARRANQUE ISOLADO. Devolve (andou, foi investida de verdade). O segundo campo vem do
		// `DashLivreEm`, que so o `return true` do `Aproximar` escreve -- e o que separa a INVESTIDA
		// do passinho de ajuste que o ramo do `DeslocamentoMinimo` da.
		(float Andou, bool Investiu) UmSalto(float distancia, bool marcando, Protocol.Golpe golpe)
		{
			ServerPlayer a = Forjar("BorraoA", onde, 5_000);
			ServerPlayer b = Forjar("BorraoB", onde + new Vec2(distancia, 0), 5_000);
			a.Facing = Facing.East;
			b.Facing = Facing.West;
			if (marcando) Mirar(a, b.Id);
			a.Combate.Recarga = 0; a.Combate.Stun = 0; a.AtaqueAte = 0;
			a.DashLivreEm = 0; a.Ficha.Ki = a.Ficha.MaxKi;

			Vec2 partiu = a.Pos;
			long dashAntes = a.DashLivreEm;
			Atacar(a, golpe);
			float andou = (a.Pos - partiu).Length;
			bool investiu = a.DashLivreEm != dashAntes;

			LimparTudoDaBancada();
			return (andou, investiu);
		}

		// A VARREDURA, e ela e o produto desta familia.
		var linhas = new List<string>();
		float maxLeve = 0, maxPesadoSemMarca = 0, maxPesadoMarcado = 0;
		int nLeve = 0, nPesadoSemMarca = 0, nPesadoMarcado = 0;
		float somaLeve = 0, somaSem = 0, somaCom = 0;

		for (float d = 40; d <= 520; d += 8)
		{
			(float aL, bool iL) = UmSalto(d, marcando: false, Protocol.Golpe.Leve);
			(float aS, bool iS) = UmSalto(d, marcando: false, Protocol.Golpe.Pesado);
			(float aC, bool iC) = UmSalto(d, marcando: true, Protocol.Golpe.Pesado);

			if (iL) { nLeve++; somaLeve += aL; maxLeve = Math.Max(maxLeve, aL); }
			if (iS) { nPesadoSemMarca++; somaSem += aS; maxPesadoSemMarca = Math.Max(maxPesadoSemMarca, aS); }
			if (iC) { nPesadoMarcado++; somaCom += aC; maxPesadoMarcado = Math.Max(maxPesadoMarcado, aC); }

			if (d % 40 == 0)
				linhas.Add($"    vao {d,4:0} px | leve {(iL ? aL : 0),4:0} | pesado s/marca {(iS ? aS : 0),4:0}"
						   + $" | pesado c/marca {(iC ? aC : 0),4:0}");
		}

		foreach (string l in linhas) GD.Print($"[borrao] {l}");
		GD.Print($"[borrao]    JOGADOR leve........: {nLeve} vaos investem, maior salto {maxLeve:0} px, "
				 + $"medio {(nLeve > 0 ? somaLeve / nLeve : 0):0} px");
		GD.Print($"[borrao]    JOGADOR pesado s/marca: {nPesadoSemMarca} vaos investem, maior salto "
				 + $"{maxPesadoSemMarca:0} px, medio {(nPesadoSemMarca > 0 ? somaSem / nPesadoSemMarca : 0):0} px");
		GD.Print($"[borrao]    PESADO COM MARCA......: {nPesadoMarcado} vaos investem, maior salto "
				 + $"{maxPesadoMarcado:0} px, medio {(nPesadoMarcado > 0 ? somaCom / nPesadoMarcado : 0):0} px");

		AfirmarBorrao("a coluna 'pesado com marca' salta MAIS LONGE que a 'pesado sem marca' -- a regra "
					  + "e a mesma funcao, o que muda e a MARCA", maxPesadoMarcado > maxPesadoSemMarca,
					  $"{maxPesadoMarcado:0} x {maxPesadoSemMarca:0}");

		// ---- E AGORA A IA, PELO FUNIL DE PRODUCAO INTEIRO ----
		List<float> saltosDaIa = SaltosDeUmaPerseguicao(vao: 150f, segundos: 6.0);
		List<float> saltosDaIaLonge = SaltosDeUmaPerseguicao(vao: 300f, segundos: 6.0);

		void Despejar(string rotulo, List<float> s)
		{
			if (s.Count == 0) { GD.Print($"[borrao]    {rotulo}: NENHUM arranque"); return; }
			s.Sort();
			GD.Print($"[borrao]    {rotulo}: {s.Count} arranques | min {s[0]:0} | mediana "
					 + $"{s[s.Count / 2]:0} | max {s[^1]:0} px | lista: "
					 + string.Join(", ", s.Select(v => v.ToString("0"))));
		}

		Despejar("IA perseguindo a 150 px", saltosDaIa);
		Despejar("IA perseguindo a 300 px", saltosDaIaLonge);

		AfirmarBorrao("a IA arranca de verdade na perseguicao (senao esta familia nao mede nada)",
					  saltosDaIa.Count > 0, $"{saltosDaIa.Count} arranques");
	}

	/// <summary>
	/// UMA PERSEGUICAO EM TEMPO REAL, devolvendo o TAMANHO DE CADA SALTO em pixels.
	///
	/// Copiada em espirito da <see cref="Perseguir"/> da `--tresteste` -- a presa e reposta a cada
	/// tique pra que a medida seja do CACADOR e nao de quanto tempo uma vitima passa no chao --, mas
	/// esta guarda a POSICAO antes do tique pra poder medir o deslocamento, que aquela nao precisava.
	/// </summary>
	private List<float> SaltosDeUmaPerseguicao(float vao, double segundos)
	{
		Vec2 onde = CorredorDeCaminhada(40);
		ServerPlayer cacador = Forjar("BorraoCacador", onde, 5_000);
		ServerPlayer presa = Forjar("BorraoPresa", onde + new Vec2(vao, 0), 5_000);
		cacador.Facing = Facing.East;
		presa.Facing = Facing.West;

		// O CEREBRO DE FABRICA, menos quando a familia E lhe tira a TECLA PESADA. Ver `_bkIaSoLeve`:
		// zerar as duas chances e a unica forma de a IA so conseguir pedir LEVE, porque a investida
		// (`ChanceDeInvestir`) e pesada por construcao -- `Cerebro:1889`.
		var mente = new Cerebro();
		if (_bkIaSoLeve) { mente.ChanceDeInvestir = 0; mente.ChanceDePesado = 0; }
		cacador.Cerebro = mente;

		var saltos = new List<float>();
		long dashAntes = cacador.DashLivreEm;
		Vec2 antes = cacador.Pos;

		RodarEmTempoReal(segundos,
			antesDoTique: () =>
			{
				presa.Pos = cacador.Pos + new Vec2(vao, 0);
				presa.Facing = Facing.West;
				if (presa.Ficha.KO) presa.Combate.Levantar();
				presa.Combate.Corpo.Restaurar();
				presa.Combate.SincronizarVida();
				antes = cacador.Pos;
			},
			depoisDoTique: () =>
			{
				// O `DashLivreEm` E O CARIMBO: so o `return true` do `Aproximar` o escreve. Sem ele,
				// o passo normal do tique (que tambem move o corpo) entraria na conta como arranque.
				if (cacador.DashLivreEm == dashAntes) return;
				dashAntes = cacador.DashLivreEm;
				saltos.Add((cacador.Pos - antes).Length);
			});

		// ============================ O ESTADO DO CACADOR SAI JUNTO, E ELE NAO E ENFEITE ============================
		// "Zero arranques" tem quatro causas que se parecem: o laco nao rodou (tiques), o corpo ficou sem
		// combustivel (Ki), o corpo esta CAIDO (KO) ou o cerebro nunca pediu golpe. Sem estes numeros um
		// zero manda a leitura direto pra hipotese errada -- foi o que aconteceu na primeira rodada da
		// familia F, e nenhuma das minhas duas primeiras suspeitas era a certa.
		// ==========================================================================================================
		GD.Print($"[borrao]       (perseguicao a {vao:0} px: {saltos.Count} arranques, "
				 + $"Ki final {cacador.Ficha.Ki / Math.Max(cacador.Ficha.MaxKi, 1) * 100:0}%, "
				 + $"KO={cacador.Ficha.KO}, vida {cacador.Ficha.HP:0}, andou {(cacador.Pos - onde).Length:0} px)");

		LimparTudoDaBancada();
		return saltos;
	}

	// =====================================================================
	// B -- O BIT DE CORRIDA, QUE E O QUE LIGA O BORRAO
	// =====================================================================
	/// <summary>
	/// QUEM PEDE CORRIDA, E QUANDO. O motion blur do port e o <see cref="Client.RastroDeCorrida"/>, e
	/// ele e ligado por UM bit: `EntityState.Correndo`, que o `RemotePlayer` le do snapshot e que o
	/// servidor escreve a partir de `npc.Correndo` -- o qual, por sua vez, sai do
	/// <see cref="Comando.Correndo"/> do cerebro (`PassoDaIa`).
	///
	/// Entao a pergunta "por que o NPC nao borra" tem uma resposta que se mede sem cliente nenhum:
	/// **quantos comandos por mil pedem corrida?**
	/// </summary>
	private void FamiliaOBitDeCorrida()
	{
		GD.Print("[borrao] ---- B: quem pede `Correndo` -- o bit que liga o rastro ----");

		(int Correndo, int Total, int Golpes) Sondar(float distancia, double vidaFrac,
													double vidaCautelosa = 0.45)
		{
			var c = new Cerebro { VidaCautelosa = vidaCautelosa };
			var rng = new Random(20260815);
			int correndo = 0, golpes = 0;

			for (int t = 0; t < 1000; t++)
			{
				var p = new Percepcao
				{
					TemAlvo = true, IdDoAlvo = 4242,
					Minha = Vec2.Zero, DoAlvo = new Vec2(distancia, 0),
					VidaFrac = vidaFrac, KiFrac = 1, FolegoFrac = 1,
					VidaDoAlvo = 1, MeuPoder = 5_000, PoderDoAlvo = 5_000,
				};
				Comando cm = c.Pensar(p, Protocol.TickSeconds, rng);
				if (cm.Correndo) correndo++;
				if (cm.Leve || cm.Pesado) golpes++;
			}
			return (correndo, 1000, golpes);
		}

		(int c150, int t150, int g150) = Sondar(150, 1.0);
		(int c300, int t300, int g300) = Sondar(300, 1.0);
		(int c40, int t40, int g40) = Sondar(40, 1.0);
		// A FUGA E O UNICO ESTADO QUE PEDE CORRIDA, e ela tem PORTAO DUPLO: vida <= 25% **e**
		// `CautelaExpressa > 0,585` (`Cerebro:1227`). Com a cautela de fabrica (0,45) e um cerebro sem
		// medo acumulado o portao nunca abre -- por isso a segunda sonda sobe a cautela: o que se quer
		// saber e se o bit tem ALGUM dono, nao se este corpo especifico foge hoje.
		(int cFugaFab, int tFugaFab, _) = Sondar(150, 0.10);
		(int cFuga, int tFuga, _) = Sondar(150, 0.10, vidaCautelosa: 0.90);

		GD.Print($"[borrao]    perseguindo a 150 px: {c150}/{t150} comandos com `Correndo` "
				 + $"({g150} golpes pedidos)");
		GD.Print($"[borrao]    perseguindo a 300 px: {c300}/{t300} com `Correndo` ({g300} golpes)");
		GD.Print($"[borrao]    colado a 40 px......: {c40}/{t40} com `Correndo` ({g40} golpes)");
		GD.Print($"[borrao]    vida 10%, cautela de FABRICA (0,45): {cFugaFab}/{tFugaFab} com `Correndo` "
				 + "-- o portao duplo da fuga nem abre");
		GD.Print($"[borrao]    vida 10%, cautela 0,90 (medroso)...: {cFuga}/{tFuga} com `Correndo`");

		AfirmarBorrao("existe ALGUM estado em que o cerebro pede corrida (senao o bit seria morto)",
					  cFuga > 0, $"{cFuga}/{tFuga} na fuga");
	}

	// =====================================================================
	// C -- A IMAGEM REMANESCENTE NO SORTEIO DE NPC
	// =====================================================================
	/// <summary>
	/// O GATE DO VULTO DO ARRANQUE e uma skill: `zanzo = investiu &amp;&amp;
	/// a.Livro?.Sabe("/datum/skill/ki/Afterimage")` (`GameServer.Combat.cs:380`). Sem ela o servidor
	/// nao manda o `S2C.Zanzo`, e sem esse pacote nenhum cliente desenha miragem nenhuma.
	///
	/// Entao mede-se o SORTEIO: de cada cem corpos tirados pelo funil de producao, quantos a tem?
	/// </summary>
	private void FamiliaVultoNoSorteio()
	{
		GD.Print("[borrao] ---- C: quantos NPCs nascem sabendo a Imagem Remanescente ----");

		if (_moldes == null || _racas == null || _skills == null)
		{
			AfirmarBorrao("ha moldes, racas e catalogo de skills carregados", false,
						  "sem eles o sorteio de producao nao roda");
			return;
		}

		void Amostrar(string idDoMolde, int quantos)
		{
			if (_moldes.Get(idDoMolde) is not { } molde)
			{
				GD.Print($"[borrao]    molde '{idDoMolde}' nao existe no npcs.json");
				return;
			}

			int comZanzo = 0, comLivro = 0;
			string primeiroKit = "";
			for (int i = 0; i < quantos; i++)
			{
				ulong semente = SorteioDeNpc.SementeDe(SeedDoUniverso, molde.Id,
													   Espaco.Misturar(1234u, (ulong)i, 0));
				FichaSorteada s = SorteioDeNpc.Sortear(molde, semente, _racas, _skills, "Earth",
													   MediaDoServidor());
				if (s.Livro != null)
				{
					comLivro++;
					if (s.Livro.Sabe(PathDoZanzoken)) comZanzo++;
					if (i == 0) primeiroKit = string.Join(", ", s.Livro.Aprendidas);
				}
			}
			GD.Print($"[borrao]    molde '{idDoMolde}': {comZanzo}/{quantos} sabem "
					 + $"`{PathDoZanzoken}` ({comLivro} nasceram com livro)");
			GD.Print($"[borrao]       kit do primeiro sorteado: {primeiroKit}");
		}

		Amostrar("cidadao", 200);
		Amostrar("rival_do_mundo", 200);
		Amostrar("freeza_vegeta", 50);

		AfirmarBorrao("o gate do vulto e uma skill com NOME, e ela e a mesma dos dois lados",
					  PathDoZanzoken == "/datum/skill/ki/Afterimage", PathDoZanzoken);
	}

	// =====================================================================
	// D -- O ANUNCIO DO SALTO: O FUNIL DO BORRAO, DEPOIS DO CONSERTO
	// =====================================================================
	/// <summary>
	/// O QUE SAI PELO FIO QUANDO UM CORPO ARRANCA -- e a familia que separa "o borrao foi consertado"
	/// de "eu escrevi que ele foi".
	///
	/// ============================ O QUE MUDOU, E O QUE ISTO CONFERE ============================
	/// O borrao saiu do INPUT e desceu pro funil do MOVIMENTO. Antes, o unico borrao do jogo era o de
	/// CORRER, ligado pelo bit `EntityState.Correndo` -- que o jogador so tem por segurar SHIFT e que o
	/// cerebro so pede na FUGA (a familia B mediu: 0 em 1000 comandos de perseguicao). Agora quem
	/// anuncia e o `Aproximar`, que e o unico que sabe se houve deslocamento, pelo `S2C.Zanzo` que ja
	/// existia pra miragem.
	///
	/// Entao ha exatamente tres perguntas, e as tres se medem SEM CLIENTE:
	///   1. o portao mudou mesmo de SKILL pra DESLOCAMENTO? (corpo sem Afterimage tem que anunciar)
	///   2. os tres corpos que arrancam (jogador, NPC, POSSUIDO) saem pelo mesmo lugar?
	///   3. quanto custa, em bytes de verdade?
	///
	/// ============================ POR QUE ELA LE UM CARIMBO E NAO O PACOTE ============================
	/// O destino do `AnunciarZanzo` e um `Peer`, e corpo de bancada nao tem nenhum -- nao ha socket pra
	/// escutar. Entao o proprio `AnunciarZanzo` carimba o que MONTOU (`_ultimoSalto`, com o `w.Length`
	/// do escritor de verdade). Conferir uma COPIA da condicao do `Atacar` aqui seria a bancada ficando
	/// verde por concordar consigo mesma -- o cego que este projeto ja pagou.
	/// ==============================================================================================
	/// </summary>
	private void FamiliaOAnuncioDoSalto()
	{
		GD.Print("[borrao] ---- D: o anuncio do salto -- quem sai pelo fio, e por quantos bytes ----");

		Vec2 onde = CorredorLivre(50);

		// UM ARRANQUE ISOLADO, LENDO O CARIMBO. Devolve quantos anuncios sairam e como era o ultimo.
		(int Quantos, int Quem, Vec2 De, bool Vulto, int Bytes, int Atacante, int Alvo)
			UmAnuncio(float vao, bool comAfterimage)
		{
			ServerPlayer a = Forjar("SaltoA", onde, 5_000);
			ServerPlayer b = Forjar("SaltoB", onde + new Vec2(vao, 0), 5_000);
			a.Facing = Facing.East;
			b.Facing = Facing.West;
			if (comAfterimage) a.Livro.Dar(PathDoZanzoken);
			Mirar(a, b.Id);   // MARCADO: e o ramo de alcance longo, o mesmo que a IA usa sempre
			a.Combate.Recarga = 0; a.Combate.Stun = 0; a.AtaqueAte = 0;
			a.DashLivreEm = 0; a.Ficha.Ki = a.Ficha.MaxKi;

			_saltosAnunciados = 0;
			_ultimoSalto = null;
			Vec2 partiu = a.Pos;
			Atacar(a, Protocol.Golpe.Pesado);

			int n = _saltosAnunciados;
			(int quem, Vec2 de, bool vulto, int bytes) = _ultimoSalto ?? (0, partiu, false, 0);
			(int atacante, int alvo) = (a.Id, b.Id);
			LimparTudoDaBancada();
			return (n, quem, de, vulto, bytes, atacante, alvo);
		}

		// --- 1. O PORTAO: DESLOCAMENTO, NAO SKILL -----------------------------
		var comSkill = UmAnuncio(300f, comAfterimage: true);
		var semSkill = UmAnuncio(300f, comAfterimage: false);
		// PERTO DEMAIS: a 40 px o `Aproximar` cai no ramo do `DeslocamentoMinimo` (40 - 32 = 8 < 16) e
		// devolve `false`. Nao houve salto, entao nao ha trajeto que borrar.
		var coladinho = UmAnuncio(40f, comAfterimage: true);

		GD.Print($"[borrao]    COM Afterimage, vao 300 px: {comSkill.Quantos} anuncio(s), "
				 + $"vulto={comSkill.Vulto}, {comSkill.Bytes} bytes");
		GD.Print($"[borrao]    SEM Afterimage, vao 300 px: {semSkill.Quantos} anuncio(s), "
				 + $"vulto={semSkill.Vulto}, {semSkill.Bytes} bytes");
		GD.Print($"[borrao]    coladinho (vao 40 px).....: {coladinho.Quantos} anuncio(s)");

		AfirmarBorrao("o corpo SEM Afterimage passou a anunciar o salto -- o portao e o DESLOCAMENTO, "
					  + "e era ele que faltava pro borrao", semSkill.Quantos == 1,
					  $"{semSkill.Quantos} anuncios");
		AfirmarBorrao("...e ele anuncia SEM vulto: borrao nao e tecnica, miragem e",
					  semSkill.Quantos == 1 && !semSkill.Vulto, $"vulto={semSkill.Vulto}");
		AfirmarBorrao("quem TEM a Afterimage continua deixando miragem (nao se perdeu nada)",
					  comSkill.Quantos == 1 && comSkill.Vulto, $"vulto={comSkill.Vulto}");
		AfirmarBorrao("um 'salto' que NAO deslocou o corpo nao anuncia nada -- borrao de quem nao "
					  + "saiu do lugar e o defeito que o vulto ja teve", coladinho.Quantos == 0,
					  $"{coladinho.Quantos} anuncios");

		// A ORIGEM E DE ONDE O CORPO SAIU, e nao pra onde ele foi. E o unico dado do pacote que o
		// cliente nao teria de outro jeito -- o destino ele ja recebe no snapshot.
		float recuo = (comSkill.De - onde).Length;
		GD.Print($"[borrao]    a origem anunciada esta a {recuo:0} px de onde o corpo comecou, "
				 + $"e o pacote diz que quem saltou foi o id {comSkill.Quem}");
		AfirmarBorrao("a origem anunciada e o PONTO DE PARTIDA (o borrao desenha o trajeto de la "
					  + "ate aqui)", recuo < 1f, $"{recuo:0.0} px de erro");

		// O ID E DE QUEM SALTOU, e nao do alvo. E por ele que o cliente acha o corpo (`World.Corpo`);
		// trocado, o borrao sairia no corpo de quem apanhou -- que e literalmente o defeito que o bit
		// `HitEvent.ZanzoEsquiva` existe pra evitar do outro lado.
		AfirmarBorrao("o anuncio identifica QUEM SALTOU, e nao em quem se bateu",
					  comSkill.Quem == comSkill.Atacante && comSkill.Quem != comSkill.Alvo,
					  $"anunciou {comSkill.Quem}, atacante {comSkill.Atacante}, alvo {comSkill.Alvo}");

		// --- 2. OS TRES CORPOS, PELO MESMO FUNIL ------------------------------
		// O jogador ja foi medido acima (o `Atacar` e literalmente o que o `case C2S.Action` chama).
		// Faltam os dois que o dono citou.
		int naIa = AnunciosDeUmaPerseguicao(vao: 200f, segundos: 4.0, possuido: false);
		int noPossuido = AnunciosDeUmaPerseguicao(vao: 200f, segundos: 4.0, possuido: true);

		GD.Print($"[borrao]    NPC (corpo sem dono, cerebro proprio): {naIa} anuncios em 4 s");
		GD.Print($"[borrao]    CORPO POSSUIDO (`AssumirOCorpo` -- a fera do Oozaru, a furia "
				 + $"lendaria): {noPossuido} anuncios em 4 s");

		AfirmarBorrao("o NPC anuncia o salto pelo funil de producao inteiro "
					  + "(TickDosCorposSemDono -> Pensar -> AplicarComando -> Atacar -> Aproximar)",
					  naIa > 0, $"{naIa} anuncios");
		AfirmarBorrao("o CORPO POSSUIDO tambem -- nao ha `if` de tipo de corpo em lugar nenhum "
					  + "deste caminho", noPossuido > 0, $"{noPossuido} anuncios");

		// --- 3. O CUSTO EM BYTES ---------------------------------------------
		// opcode(1) + id(4) + Vec2(8) + vulto(1). O `w.Length` e do escritor de verdade: se alguem
		// puser um campo aqui amanha, esta linha e que muda de cor, nao um comentario.
		const int BytesEsperados = 14;
		GD.Print($"[borrao]    o pacote `S2C.Zanzo` mede {comSkill.Bytes} bytes "
				 + $"(era {comSkill.Bytes - 1} antes do bool `vulto`)");
		GD.Print($"[borrao]    teto por corpo: {1000.0 / RecargaDashMs:0.0} anuncios/s "
				 + $"(`RecargaDashMs` = {RecargaDashMs} ms)");
		AfirmarBorrao($"o anuncio custa {BytesEsperados} bytes -- UM a mais que antes",
					  comSkill.Bytes == BytesEsperados, $"{comSkill.Bytes} bytes");
	}

	/// <summary>
	/// UMA PERSEGUICAO EM TEMPO REAL contando ANUNCIOS DE SALTO em vez de pixels.
	///
	/// Irma da <see cref="SaltosDeUmaPerseguicao"/>, e separada dela de proposito: aquela mede o
	/// DESLOCAMENTO (a pergunta do relato 2) e esta mede o ANUNCIO (a do relato 1). Um salto que
	/// acontecesse sem anunciar passaria naquela e reprovaria nesta -- que e exatamente o estado em
	/// que o jogo estava.
	/// </summary>
	/// <param name="possuido">
	/// O corpo entra pela porta unica da POSSE (<c>AssumirOCorpo</c>), e nao com o cerebro pendurado a
	/// mao. E o que faz esta medida valer pra fera do Oozaru e pra furia lendaria: se um dia a posse
	/// passar a fazer uma quarta coisa, esta bancada faz a quarta coisa junto.
	/// </param>
	private int AnunciosDeUmaPerseguicao(float vao, double segundos, bool possuido)
	{
		Vec2 onde = CorredorDeCaminhada(60);
		ServerPlayer cacador = Forjar(possuido ? "SaltoPossuido" : "SaltoNpc", onde, 5_000);
		ServerPlayer presa = Forjar("SaltoPresa", onde + new Vec2(vao, 0), 5_000);
		cacador.Facing = Facing.East;
		presa.Facing = Facing.West;

		if (possuido) AssumirOCorpo(cacador, new Cerebro());
		else cacador.Cerebro = new Cerebro();

		_saltosAnunciados = 0;

		RodarEmTempoReal(segundos,
			antesDoTique: () =>
			{
				presa.Pos = cacador.Pos + new Vec2(vao, 0);
				presa.Facing = Facing.West;
				if (presa.Ficha.KO) presa.Combate.Levantar();
				presa.Combate.Corpo.Restaurar();
				presa.Combate.SincronizarVida();
			});

		int n = _saltosAnunciados;
		LimparTudoDaBancada();
		return n;
	}

	// =====================================================================
	// O CHAO DAS FAMILIAS E e F
	// =====================================================================
	/// <summary>
	/// ============================ UM CORREDOR LIVRE **PRA QUEM ANDA**, e nao so sem parede ============================
	/// O <see cref="CorredorLivre"/> pergunta <see cref="ZoneCollision.BlockedCell"/>, que significa
	/// PAREDE e so. Quem anda -- e quem investe -- consulta `MoveRules.Occupied` com
	/// <see cref="ModoDeTravessia.APe"/>, e nesse modo **a agua tambem barra** (`ClasseDeAgua.Bloqueia`).
	///
	/// A diferenca nao e teorica: as familias E e F nasceram medindo "0 arranques, 0 px andados, Ki
	/// intacto" nas quatro distancias, com as familias A e D verdes ao lado. Os corpos estavam num
	/// corredor sem parede **dentro de um lago** -- o `AplicarComando` recusava todo passo e o
	/// `Aproximar` recusava toda investida pelo `PathOccupied`, os dois com razao. A bancada estava
	/// medindo o mapa.
	///
	/// E o erro tem historico: e literalmente o mesmo que o `PalcoDaFotoDeCorpos` ja registra ter pago
	/// ("aquela bancada nasceu perguntando so por parede e reprovou tres linhas em Namek com o corpo
	/// parado na beira de um lago"). Por isso este helper nao e uma opcao do outro: quem forja corpo pra
	/// ANDAR tem que perguntar a pergunta de quem anda.
	/// ==============================================================================================================
	/// </summary>
	private Vec2 CorredorDeCaminhada(int tiles)
	{
		if (_pjMapa is not { } mapa) return CorredorLivre(tiles);

		for (int y = _pjProximoCorredor; y < 250; y++)
		{
			for (int x = 4; x + tiles < 250; x++)
			{
				bool livre = true;
				for (int d = 0; d <= tiles && livre; d++)
					livre &= !mapa.Bloqueia(x + d, y, ModoDeTravessia.APe);
				if (!livre) continue;

				_pjProximoCorredor = y + 3;
				return new Vec2(x * ZoneCollision.TileSize + 16, y * ZoneCollision.TileSize + 16);
			}
		}

		AfirmarBorrao($"achei um corredor CAMINHAVEL de {tiles} tiles (parede E agua)", false,
					  "a varredura falhou -- as familias E e F mediriam o mapa e nao o dash");
		return CorredorLivre(tiles);
	}

	// =====================================================================
	// F -- OS DOIS LADOS, O MESMO VAO, O MESMO PIXEL
	// =====================================================================
	/// <summary>
	/// **A PROVA DE QUE NAO HA DOIS ALCANCES**, e ela e uma IGUALDADE e nao uma comparacao de maximos.
	///
	/// ============================ POR QUE A FAMILIA A NAO BASTAVA ============================
	/// A familia A despeja duas distribuicoes -- a do jogador (varredura de 40 a 520 px) e a da IA (uma
	/// perseguicao) -- e compara os MAXIMOS. So que os maximos foram tirados de vaos diferentes: o
	/// jogador chegou a 448 px porque a varredura o levou a um vao de 480, e a IA parou em 268 porque a
	/// perseguicao dela foi presa a 300. Comparar esses dois numeros nao responde a pergunta do dono --
	/// ele viu ALCANCES diferentes, e dois numeros diferentes medidos em vaos diferentes sao compativeis
	/// tanto com "e a mesma regra" quanto com "sao duas regras".
	///
	/// Aqui o vao e o MESMO nos dois lados, tique a tique, e o que se cobra e igualdade em PIXEL. Se
	/// alguem escrever um segundo caminho de arranque pra IA amanha -- que e a hipotese (b) do enunciado
	/// --, esta familia fica vermelha na primeira linha, e nao "um pouco diferente".
	///
	/// O NUMERO ESPERADO NAO SAI DE NENHUM DOS DOIS: e `vao - DistanciaDeParada`, escrito da regra
	/// (`Aproximar`: `destino = a.Pos + d.Normalizado * (dist - DistanciaDeParada)`). Comparar a IA com o
	/// jogador e so metade -- se os dois estivessem errados igual, a comparacao ficaria verde. E o cego
	/// que a memoria "a bancada mede INTENCAO" catalogou com todas as letras: *"as duas telas concordam"
	/// fica verde com as duas erradas igual*.
	/// ====================================================================================
	/// </summary>
	/// <remarks>
	/// OS VAOS PARAM EM 300 px porque a IA so investe ate <see cref="Cerebro.AlcanceDaInvestida"/> = 320.
	/// Acima disso ela nao pede investida nenhuma -- e a bancada mediria "a IA nao salta", que e verdade
	/// e nao e sobre alcance. O jogador marcado alcanca 480 (`AlcanceDoDashMarcado`), e essa diferenca ja
	/// esta medida na familia A: **e ele quem passa da IA, e nao o contrario.**
	/// </remarks>
	private void FamiliaOMesmoVaoOMesmoPixel()
	{
		GD.Print("[borrao] ---- F: o MESMO vao nos dois lados -- o salto tem que dar o MESMO pixel ----");

		Vec2 onde = CorredorDeCaminhada(24);

		// O SALTO DO JOGADOR num vao exato, pelo `Atacar` -- literalmente o que o `case C2S.Action` chama.
		float SaltoDoJogador(float vao, Protocol.Golpe golpe, bool marcando)
		{
			ServerPlayer a = Forjar("VaoA", onde, 5_000);
			ServerPlayer b = Forjar("VaoB", onde + new Vec2(vao, 0), 5_000);
			a.Facing = Facing.East;
			b.Facing = Facing.West;
			if (marcando) Mirar(a, b.Id);
			a.Combate.Recarga = 0; a.Combate.Stun = 0; a.AtaqueAte = 0;
			a.DashLivreEm = 0; a.Ficha.Ki = a.Ficha.MaxKi;

			Vec2 partiu = a.Pos;
			Atacar(a, golpe);
			float andou = (a.Pos - partiu).Length;
			LimparTudoDaBancada();
			return andou;
		}

		float[] vaos = [80, 150, 220, 300];
		int iguais = 0, medidos = 0;

		foreach (float vao in vaos)
		{
			float esperado = vao - DistanciaDeParada;
			float doJogador = SaltoDoJogador(vao, Protocol.Golpe.Pesado, marcando: true);

			// A IA VEM PELO FUNIL DE PRODUCAO INTEIRO (tique de mundo -> `Pensar` -> `AplicarComando` ->
			// `Atacar` -> `Aproximar`), e nao por uma chamada direta: e justamente o caminho dela que
			// esta sob suspeita. Oito segundos dao ~96 tiques, e com a recarga de 500 ms cabem ate 16
			// arranques -- folga de sobra pra os 35% de pesado do temperamento de fabrica aparecerem.
			List<float> daIa = SaltosDeUmaPerseguicao(vao, segundos: 8.0);
			float maxIa = daIa.Count > 0 ? daIa.Max() : 0;

			GD.Print($"[borrao]    vao {vao,4:0} px | regra diz {esperado,4:0} | JOGADOR pesado+marca "
					 + $"{doJogador,4:0} | IA (funil de producao) {maxIa,4:0} px em {daIa.Count} arranque(s)");

			if (daIa.Count == 0) continue;
			medidos++;

			bool bate = Math.Abs(doJogador - maxIa) < 1f
					 && Math.Abs(doJogador - esperado) < 1f;
			if (bate) iguais++;
			AfirmarBorrao($"vao {vao:0} px: a IA e o jogador saltam o MESMO pixel, e ele e o da REGRA "
						  + $"({esperado:0} px)", bate,
						  $"jogador {doJogador:0.0}, IA {maxIa:0.0}, regra {esperado:0.0}");
		}

		AfirmarBorrao("...e isso foi medido em TODOS os vaos, e nao num caso feliz",
					  medidos == vaos.Length && iguais == vaos.Length,
					  $"{iguais} iguais de {medidos} medidos, {vaos.Length} pedidos");
	}

	// =====================================================================
	// E -- O DEFEITO INJETADO: COMO CADA AFIRMACAO REPROVA
	// =====================================================================
	/// <summary>
	/// ============================ UMA CHECAGEM QUE SO FOI VISTA PASSANDO E `Checa("...", true)` ============================
	/// As familias A a F medem o jogo consertado e ficam verdes. Verde e barato: uma afirmacao que nunca
	/// foi vista VERMELHA nao se distingue de uma constante. Esta familia poe cada criterio central de pe
	/// contra o defeito que ele existe pra pegar -- mede, ESTRAGA, exige que o MESMO criterio reprove,
	/// desfaz e exige que ele volte a passar (o <see cref="Mutacao"/>, o mesmo helper da `--provateste`).
	///
	/// SETE DEFEITOS, e seis deles sao ESTADO DE MUNDO -- Ki, recarga, sigilo, livro, marca e
	/// temperamento. O setimo e o unico que precisa de interruptor em producao, e ele e o mais
	/// importante: **o portao antigo do borrao**, `if (zanzo)` no lugar de `if (investiu)`, que era o
	/// relato 1 do dono inteiro. Ver `_borraoSoComSkill`, no `GameServer.Combat.cs`.
	/// =========================================================================================================================
	/// </summary>
	private void FamiliaODefeitoInjetado()
	{
		GD.Print("[borrao] ---- E: os sete defeitos injetados -- como cada afirmacao REPROVA ----");

		Vec2 onde = CorredorDeCaminhada(24);

		// UM ARRANQUE ISOLADO, com os interruptores da familia aplicados ENTRE a montagem e o golpe.
		// Ele forja corpos novos a cada chamada de proposito: o `Mutacao` roda o criterio TRES vezes
		// (antes, com o defeito, depois) e um corpo reaproveitado carregaria a recarga da vez anterior.
		(int Anuncios, bool Vulto, float Andou) Arranque(float vao, bool comSkill)
		{
			ServerPlayer a = Forjar("DefA", onde, 5_000);
			ServerPlayer b = Forjar("DefB", onde + new Vec2(vao, 0), 5_000);
			a.Facing = Facing.East;
			b.Facing = Facing.West;
			if (comSkill && !_bkSemSkill) a.Livro.Dar(PathDoZanzoken);
			if (!_bkSemMarca) Mirar(a, b.Id);
			a.Combate.Recarga = 0; a.Combate.Stun = 0; a.AtaqueAte = 0;
			a.DashLivreEm = 0; a.Ficha.Ki = a.Ficha.MaxKi;

			// ---- OS DEFEITOS DE ESTADO DE MUNDO ----
			if (_bkSemKi) a.Ficha.Ki = 0;
			if (_bkEmRecarga) a.DashLivreEm = NowMs() + 10_000;
			if (_bkOculto) _invisiveis.Add(a.Id);

			_saltosAnunciados = 0;
			_ultimoSalto = null;
			Vec2 partiu = a.Pos;
			Atacar(a, Protocol.Golpe.Pesado);

			int n = _saltosAnunciados;
			bool vulto = _ultimoSalto?.Vulto ?? false;
			float andou = (a.Pos - partiu).Length;

			_invisiveis.Remove(a.Id);
			LimparTudoDaBancada();
			return (n, vulto, andou);
		}

		// ---- 1. O PORTAO DO BORRAO: DESLOCAMENTO, E NAO SKILL ----------------
		// O defeito e o codigo ANTERIOR, letra por letra. Sem esta mutacao, "o corpo sem Afterimage
		// anuncia" e uma frase que eu escrevi e que a bancada repetiu.
		Mutacao(AfirmarBorrao,
			"UM CORPO SEM A AFTERIMAGE ANUNCIA O SALTO -- e o borrao do NPC, e o portao e o DESLOCAMENTO",
			"o portao ANTIGO de volta: `if (zanzo)` no lugar de `if (investiu)`",
			() => Arranque(300f, comSkill: false).Anuncios == 1,
			() => _borraoSoComSkill = true,
			() => _borraoSoComSkill = false);

		// ---- 2. AS DUAS CAMADAS SAO INDEPENDENTES ---------------------------
		// Tirar a skill tem que matar a MIRAGEM e **nao** o borrao. As duas linhas medem o mesmo mundo
		// estragado por lados opostos: sem a segunda, um portao que voltasse a ser da skill passaria na
		// primeira (o vulto morreria junto, como manda) sem ninguem notar que o anuncio morreu tambem.
		Mutacao(AfirmarBorrao,
			"quem TEM a Afterimage deixa MIRAGEM (o `vulto` do pacote)",
			"o livro sem a Afterimage",
			() => Arranque(300f, comSkill: true).Vulto,
			() => _bkSemSkill = true,
			() => _bkSemSkill = false);

		_bkSemSkill = true;
		(int semLivro, bool vultoSemLivro, _) = Arranque(300f, comSkill: true);
		_bkSemSkill = false;
		AfirmarBorrao("   ...e com o MESMO defeito de pe o ANUNCIO sobrevive: borrao e miragem sao duas "
					  + "camadas de um funil so, e nao uma coisa so", semLivro == 1 && !vultoSemLivro,
					  $"{semLivro} anuncio(s), vulto={vultoSemLivro}");

		// ---- 3, 4, 5. AS TRES RECUSAS QUE MATAM O SALTO ---------------------
		// Ki, recarga e sigilo. As duas primeiras matam a INVESTIDA (o `Aproximar` devolve `false`, e
		// sem deslocamento nao ha trajeto que borrar); a terceira mata o ANUNCIO com o salto acontecido
		// -- e ela e regra de verdade, e nao acidente: miragem e borrao sao fotos do corpo, e um jogador
		// escondido que investisse entregava a propria posicao com elas (ver `AnunciarZanzo`).
		Mutacao(AfirmarBorrao,
			"um arranque com Ki no tanque anuncia",
			"o tanque de Ki vazio",
			() => Arranque(300f, comSkill: false).Anuncios == 1,
			() => _bkSemKi = true,
			() => _bkSemKi = false);

		Mutacao(AfirmarBorrao,
			"um arranque FORA da recarga anuncia",
			"`DashLivreEm` dez segundos no futuro (a recarga de 500 ms de pe)",
			() => Arranque(300f, comSkill: false).Anuncios == 1,
			() => _bkEmRecarga = true,
			() => _bkEmRecarga = false);

		Mutacao(AfirmarBorrao,
			"quem esta a VISTA anuncia o salto",
			"o corpo INVISIVEL (`EstaOculto`) -- e o borrao nao pode entregar quem sumiu",
			() => Arranque(300f, comSkill: false).Anuncios == 1,
			() => _bkOculto = true,
			() => _bkOculto = false);

		// ---- 6. A MARCA E O QUE ESTICA O ALCANCE ----------------------------
		// **Esta e a linha do relato 2.** O que separa um salto de 268 px de salto nenhum, no mesmo vao
		// e com a mesma tecla, e ter marcado o alvo -- e nao ser NPC. A IA marca todo tique
		// (`Cerebro.Montar`) e o jogador marca por duplo clique: e essa a assimetria que o dono viu.
		Mutacao(AfirmarBorrao,
			"o jogador PESADO E MARCADO atravessa um vao de 300 px (o alcance de 480 do marcado)",
			"a marca desfeita -- o pesado sem marca busca so 160 px",
			() => Arranque(300f, comSkill: false).Andou > 200f,
			() => _bkSemMarca = true,
			() => _bkSemMarca = false);

		// ---- 7. E O ALCANCE DA IA E A TECLA, NAO O TIPO DE CORPO ------------
		// A IA nao tem um segundo caminho: ela tem a tecla pesada. Tirada a tecla, o alcance dela cai
		// pro do LEVE (80 px de busca), e num vao de 150 px ela para de arrancar -- exatamente como o
		// jogador leve, que a familia A ja mediu parando em 48 px de salto.
		Mutacao(AfirmarBorrao,
			"a IA arranca num vao de 150 px, pelo funil de producao",
			"o temperamento sem PESADO nem INVESTIDA -- a IA so consegue pedir leve",
			() => SaltosDeUmaPerseguicao(150f, segundos: 5.0).Count > 0,
			() => _bkIaSoLeve = true,
			() => _bkIaSoLeve = false);
	}

	// =====================================================================
	// OS INTERRUPTORES DA FAMILIA E
	// =====================================================================
	// Todos FALSOS fora do `Mutacao`, e todos desfeitos no `finally` dele. Eles descrevem ESTADO DE
	// MUNDO (tanque, relogio, sigilo, livro, marca, temperamento) e por isso moram aqui; o setimo
	// defeito -- o portao antigo do borrao -- descreve CODIGO, e por isso mora na producao
	// (`_borraoSoComSkill`, `GameServer.Combat.cs`), onde o criterio o encontra sem uma copia da regra.
	private bool _bkSemKi, _bkEmRecarga, _bkOculto, _bkSemSkill, _bkSemMarca, _bkIaSoLeve;

	/// <summary>
	/// O PORTAO ANTIGO DO BORRAO, ligavel. **Falso em jogo, sempre.**
	///
	/// Ligado, o <see cref="Atacar"/> volta a anunciar o salto so pra quem sabe a Afterimage -- que era o
	/// estado do jogo quando o dono escreveu *"npcs quando usam DASH n ficam com o EFEITO DE BLUR igual
	/// os jogadores"*. Ver a familia E.
	/// </summary>
	private bool _borraoSoComSkill;
}
