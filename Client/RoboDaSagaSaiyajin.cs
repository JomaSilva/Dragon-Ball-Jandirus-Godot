using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA SAGA SAIYAJIN (`--diagsaiyajin`, 2026-11-01, o pedido 210): as fotos de tudo, como o dono pediu (*"tire print de tudo pra provar e
/// revisar"*). Com janela e com `--host`, nascendo Humano no campo aberto da Terra, a cadeia arrumada com o Freeza no Vegeta ja vencido:
///   S1. o jogador bate 500 de BP na Terra: o anuncio do scouter, a pod do Raditz cai e a cena da chegada dele na tela -- foto
///       `dsaiyajin-1-raditz-chega`;
///   S2. na pod dele: a pod no meio da cratera e o Raditz (cabelo e armadura dele) ao lado -- foto `dsaiyajin-2-raditz-na-pod`;
///   S3. o Raditz vem lutar com o jogador -- foto `dsaiyajin-3-raditz-luta`;
///   S4. o Raditz cai e avisa: Nappa e Vegeta chegam em 7 dias -- foto `dsaiyajin-4-raditz-avisa`;
///   S5. sete dias depois, a cena do Nappa -- foto `dsaiyajin-5-nappa-chega` -- e, quando ela acaba, a do Vegeta -- foto `dsaiyajin-6-vegeta-chega`;
///   S6. o Nappa ao lado da pod dele -- foto `dsaiyajin-7-nappa-na-pod`;
///   S6b. (2026-11-01, o pedido 214) os seis Saibamen ao pe do Nappa, so o primeiro lutando -- foto `dsaiyajin-7b-saibamen`;
///   S6c. um Saibaman de cada vez (foto `dsaiyajin-7c-saibaman-luta`) e, caindo o sexto, o Nappa entra -- foto `dsaiyajin-7d-nappa-entra`;
///   S7. o Nappa luta e o Vegeta, atras dele, so observa -- foto `dsaiyajin-8-vegeta-observa`;
///   S8. o Nappa cai: o Vegeta diz a fala dele e entra na luta -- foto `dsaiyajin-9-vegeta-luta`;
///   S9. o Vegeta cai: a saga Saiyajin acaba e o Freeza parte pra Namek -- foto `dsaiyajin-10-vitoria`;
///   S10. (2026-11-01, os adendos 210k e 210l) os tres lado a lado, de frente: de pele branca e de scouter -- foto `dsaiyajin-11-os-tres`.
/// (2026-11-01, os adendos 210k e 210l) E no S2 e no S7, os tres NA TELA: de pele branca e de scouter no rosto (S2b, S7b); e o RETRATO das
/// tres cenas de chegada tambem de scouter (S5b).
///
/// O contra-exemplo de cada regra e a MUTACAO do ciclo (`ag/saiyajin`), medida na `--saiyajinteste`.
///
///     Godot --path . --host --rede 12761 --campoteste 8 --horateste 0.5 --semfauna --diagsaiyajin --raca Human --conta bancada_saiyajin --nome Kakarotto
/// </summary>
public partial class RoboDaSagaSaiyajin : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ditas = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;
	private const float T = 32f;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Fechar();
	}

	private void Ouvir(Protocol.Fala canal, string quem, string texto) => _ditas.Add(texto);
	private bool Disse(string trecho) => _ditas.Any(t => t.Contains(trecho, StringComparison.Ordinal));
	private string UltimasDitas() => string.Join(" | ", _ditas.TakeLast(3));

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo nasceu, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dsaiyajin] ===== A SAGA SAIYAJIN: RADITZ, NAPPA E VEGETA (dono, 2026-11-01) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);
		cli.Falou += Ouvir;
		try { await ASaga(cli, srv, eu); }
		finally { cli.Falou -= Ouvir; }
	}

	private async Tarefa ASaga(GameClient cli, Jandirus.Server.GameServer srv, int eu)
	{
		// ---------------------------------------------------------------- S1: 500 de BP na Terra
		srv.PalcoSY_Preparar(eu, 400);
		await Segundos(2.5);
		bool calado = srv.PalcoSY_Chefe("raditz") == null;
		srv.PalcoSY_Bp(eu, 500);
		bool cena = await Ate(() => CenaDaChegada.NaTelaDeTeste is { } c && c.NomeDeTeste.Contains("Raditz", StringComparison.OrdinalIgnoreCase), 8);
		await Segundos(1.6);   // o retrato cresce
		Fotografar("user://dsaiyajin-1-raditz-chega.png");
		bool retratoR = RetratoDeScouter("Raditz");
		Conferir(calado && cena && Disse("space pod Saiyajin"),
				 "S1. COM 500 DE BP NA TERRA O RADITZ CHEGA (*\"quando um jogador na terra ... bater 500 de bp , raditz ira pousar na terra\"*): com 400 nada; "
				 + $"com 500, o anuncio do scouter e a cena da chegada dele na tela [400 calado {calado}; cena {cena}; ouvi: {UltimasDitas()}]");
		if (srv.PalcoSY_Chefe("raditz") is not { Pod: { } podR } raditz) { Conferir(false, "(montagem) o Raditz e a pod dele estao na Terra"); return; }
		srv.PalcoSY_Bp(eu, 100_000);   // o jogador aguenta a luta (o marco ja bateu)

		// ---------------------------------------------------------------- S2: a pod na cratera
		await Ate(() => CenaDaChegada.NaTelaDeTeste == null, 8);
		srv.PalcoSY_PertoDe(eu, podR + new Vec2(0, 4 * T));
		await Segundos(1.0);
		srv.PalcoSY_ChefePerto("raditz", podR + new Vec2(T * 1.2f, T));
		await Segundos(0.4);
		Fotografar("user://dsaiyajin-2-raditz-na-pod.png");
		bool podNaTela = cli.Obras.Any(o => o.Tipo == "Spacepod" && (o.Pos - new Vector2(podR.X, podR.Y)).Length() < 2);
		Conferir(podNaTela, $"S2. A POD DO RADITZ ESTA NO CHAO DA TERRA (no meio da cratera), e ele ao lado dela, de cabelo e armadura [pod na tela {podNaTela}]");
		Conferir(NaTela(srv, "raditz") == BrancoDeScouter,
				 "S2b. O RADITZ NA TELA E DE PELE BRANCA E USA SCOUTER (*\"Vegeta, nappa e raditz sao sempre cor de pele branca\"*, *\"Os 3 usam scouter tb "
				 + $"equipado\"*) [{NaTela(srv, "raditz")}]");

		// ---------------------------------------------------------------- S3: ele vem lutar
		bool veio = await Ate(() => srv.PalcoSY_Chefe("raditz") is { } r && DoJogador(srv, eu, r.Corpo.Pos) < 3 * T, 12);
		await Segundos(1.2);
		Fotografar("user://dsaiyajin-3-raditz-luta.png");
		Conferir(veio, "S3. O RADITZ VEM LUTAR: ele caca quem esta no planeta e chega no jogador");

		// ---------------------------------------------------------------- S4: ele cai e avisa
		srv.PalcoSY_Derrubar("raditz", eu);
		bool avisou = await Ate(() => Disse("em 7 dias eles chegam à Terra") && Disse("chegam em 7 dias"), 6);
		await Segundos(0.8);
		Fotografar("user://dsaiyajin-4-raditz-avisa.png");
		Conferir(avisou && srv.PalcoSY_Fase("raditz") == "Vencida" && srv.PalcoSY_Fase("saiyajins") == "Contando",
				 "S4. QUANDO O RADITZ PERDE, ELE AVISA (*\"ele avisara q em 7 dias(do jogo) nappa e vegeta irao pra terra\"*): o chat diz as duas coisas, e o elo "
				 + $"de Nappa e Vegeta conta os dias [raditz {srv.PalcoSY_Fase("raditz")}; saiyajins {srv.PalcoSY_Fase("saiyajins")}; ouvi: {UltimasDitas()}]");

		// ---------------------------------------------------------------- S5: sete dias depois, as duas cenas
		srv.PalcoSY_PularDias(7);
		bool cenaN = await Ate(() => CenaDaChegada.NaTelaDeTeste is { } c && c.NomeDeTeste.Contains("Nappa", StringComparison.OrdinalIgnoreCase), 6);
		await Segundos(1.6);
		Fotografar("user://dsaiyajin-5-nappa-chega.png");
		bool retratoN = RetratoDeScouter("Nappa");
		bool cenaV = await Ate(() => CenaDaChegada.NaTelaDeTeste is { } c && c.NomeDeTeste.Contains("Vegeta", StringComparison.OrdinalIgnoreCase), 12);
		await Segundos(1.6);
		Fotografar("user://dsaiyajin-6-vegeta-chega.png");
		bool retratoV = RetratoDeScouter("Vegeta");
		Conferir(cenaN && cenaV, "S5. SETE DIAS DEPOIS, AS DUAS CHEGADAS: a cena do Nappa, e quando ela acaba a do Vegeta (uma nao tira a outra da tela) "
								 + $"[Nappa {cenaN}; Vegeta {cenaV}]");
		Conferir(retratoR && retratoN && retratoV,
				 $"S5b. O RETRATO DE CADA CHEGADA E DE SCOUTER, como o corpo no mundo [Raditz {retratoR}; Nappa {retratoN}; Vegeta {retratoV}]");

		// ---------------------------------------------------------------- S6: o Nappa na pod dele
		if (srv.PalcoSY_Chefe("nappa") is not { Pod: { } podN } || srv.PalcoSY_Chefe("vegeta_saiyajin") is not { Pod: { } podV })
		{ Conferir(false, "(montagem) o Nappa e o Vegeta e as pods deles estao na Terra"); return; }
		await Ate(() => CenaDaChegada.NaTelaDeTeste == null, 8);
		// A LUTA NA HORIZONTAL: o jogador a leste da pod e o Nappa ao lado dela -- o Vegeta vai observar do lado de la (a oeste), e a tela (40
		// tiles de largura, 22 de altura) pega os tres. Na d1 a luta era na vertical e o Vegeta ficou fora da foto.
		srv.PalcoSY_PertoDe(eu, podN + new Vec2(6 * T, T));
		await Segundos(0.8);
		srv.PalcoSY_ChefePerto("nappa", podN + new Vec2(T * 1.2f, T));
		await Segundos(0.4);
		Fotografar("user://dsaiyajin-7-nappa-na-pod.png");
		float entreAsPods = (new Vector2(podN.X, podN.Y) - new Vector2(podV.X, podV.Y)).Length() / T;
		Conferir(entreAsPods >= 7, $"S6. CADA UM CAIU NA SUA POD, LONGE UMA DA OUTRA [{entreAsPods:0} tiles entre as pods]");

		// ---------------------------------------------------------------- S6b: os seis Saibamen (o pedido 214)
		// Na chegada o Nappa planta os seis na fileira ao pe dele: a foto e o jogador perto do Nappa, os seis a vista e o primeiro vindo.
		string[] saibamen = [.. Enumerable.Range(1, 6).Select(k => $"saibaman_{k}")];
		bool seisNoMundo = saibamen.All(m => srv.PalcoSY_Chefe(m) != null);
		if (srv.PalcoSY_Chefe("nappa") is { } nS)
		{
			srv.PalcoSY_PertoDe(eu, nS.Corpo.Pos + new Vec2(0, 5 * T));
			await Segundos(1.5);
		}
		Fotografar("user://dsaiyajin-7b-saibamen.png");
		bool soOPrimeiro = !srv.PalcoSY_Observando("saibaman_1") && saibamen.Skip(1).All(m => srv.PalcoSY_Observando(m)) && srv.PalcoSY_Observando("nappa");
		Conferir(seisNoMundo && soOPrimeiro && Disse("seis SAIBAMEN brotam"),
				 "S6b. O NAPPA PLANTA SEIS SAIBAMEN (*\"antes do nappa lutar ele ira colocar 6 saibamens\"*): os seis ao pe dele, so o primeiro luta e o "
				 + $"Nappa espera [seis {seisNoMundo}; so o primeiro {soOPrimeiro}; ouvi: {UltimasDitas()}]");

		// ---------------------------------------------------------------- S6c: um de cada vez, e o Nappa por ultimo
		bool emOrdem = true;
		for (int k = 0; k < 5; k++)
		{
			if (k == 2)
			{
				// A FOTO DA LUTA DE UM SAIBAMAN: o terceiro colado no jogador, os que esperam e o Nappa ao fundo
				if (srv.PalcoSY_PosicaoDe(eu) is { } euAgora) srv.PalcoSY_ChefePerto(saibamen[k], euAgora + new Vec2(2 * T, 0));
				await Segundos(0.8);
				Fotografar("user://dsaiyajin-7c-saibaman-luta.png");
			}
			srv.PalcoSY_Derrubar(saibamen[k], eu);
			string proximo = saibamen[k + 1];
			bool entrou = await Ate(() => !srv.PalcoSY_Observando(proximo), 3);
			emOrdem &= entrou && srv.PalcoSY_Observando("nappa");
		}
		srv.PalcoSY_Derrubar(saibamen[5], eu);
		bool nappaEntra = await Ate(() => !srv.PalcoSY_Observando("nappa") && Disse("quem vai brincar sou eu"), 4);
		await Segundos(0.8);
		Fotografar("user://dsaiyajin-7d-nappa-entra.png");
		Conferir(emOrdem && nappaEntra,
				 "S6c. UM SAIBAMAN DE CADA VEZ, E CAINDO O SEXTO O NAPPA ENTRA (*\"quando os 6 forem derrotados, o nappa comeca a lutar\"*) "
				 + $"[em ordem {emOrdem}; o Nappa entrou {nappaEntra}; ouvi: {UltimasDitas()}]");

		// ---------------------------------------------------------------- S7: o Nappa luta, o Vegeta observa
		if (srv.PalcoSY_Chefe("nappa") is { } n0) srv.PalcoSY_ChefePerto("vegeta_saiyajin", n0.Corpo.Pos + new Vec2(-4 * T, 0));
		bool nappaVeio = await Ate(() => srv.PalcoSY_Chefe("nappa") is { } n && DoJogador(srv, eu, n.Corpo.Pos) < 3 * T, 12);
		await Segundos(3.0);   // o Vegeta assenta atras do Nappa
		// A FOTO DO VEGETA OBSERVANDO: o Nappa luta de LONGE (a Boom Wave, a uns 7 tiles), e o ponto de quem observa fica 5 tiles atras dele --
		// fora da tela (a r3 do ag/saiyajin fotografou so o Nappa). O palco poe o Nappa a 2 tiles do jogador e o Vegeta no ponto dele, atras do
		// Nappa (onde a IA o deixa), e a foto sai antes de o Nappa recuar pro tiro de longe.
		// (a r1 do ag/fotosaiyajin: com 0,4 s de espera a IA ja tinha levado os dois embora do quadro) -- o Vegeta vai DIRETO pro ponto de quem
		// observa (5 tiles atras do Nappa, na linha do jogador), e a foto sai quando os dois estao na parte livre da tela (sem o HUD, o chat e o
		// aviso do jornal por cima), em ate seis tentativas.
		bool doisNoQuadro = false;
		for (int tentativa = 0; tentativa < 6 && !doisNoQuadro; tentativa++)
		{
			if (srv.PalcoSY_PosicaoDe(eu) is not { } euAgora) break;
			Vec2 nappaAli = euAgora + new Vec2(2 * T, -T);
			Vec2 rumo = (nappaAli - euAgora).Normalized();
			srv.PalcoSY_ChefePerto("nappa", nappaAli);
			srv.PalcoSY_ChefePerto("vegeta_saiyajin", nappaAli + rumo * (5 * T));
			doisNoQuadro = await Ate(() => NoQuadro(srv, eu, "nappa") && NoQuadro(srv, eu, "vegeta_saiyajin"), 0.6);
		}
		Fotografar("user://dsaiyajin-8-vegeta-observa.png");
		_linhas.Add($"  --     os dois no quadro na foto do S7: {doisNoQuadro}");
		float vegetaDoJogador = srv.PalcoSY_Chefe("vegeta_saiyajin") is { } v1 ? DoJogador(srv, eu, v1.Corpo.Pos) / T : -1;
		Conferir(nappaVeio && srv.PalcoSY_Observando("vegeta_saiyajin") && vegetaDoJogador > 4,
				 "S7. O NAPPA LUTA E O VEGETA SO OBSERVA (*\"Vegeta n luta e fica so observando o nappa lutar\"*): o Nappa chega no jogador, e o Vegeta fica "
				 + $"atras, longe da luta [Nappa veio {nappaVeio}; Vegeta observando {srv.PalcoSY_Observando("vegeta_saiyajin")}; Vegeta a {vegetaDoJogador:0.#} "
				 + "tiles do jogador]");
		Conferir(NaTela(srv, "nappa") == BrancoDeScouter && NaTela(srv, "vegeta_saiyajin") == BrancoDeScouter,
				 $"S7b. O NAPPA E O VEGETA NA TELA SAO DE PELE BRANCA E USAM SCOUTER [Nappa {NaTela(srv, "nappa")}; Vegeta {NaTela(srv, "vegeta_saiyajin")}]");

		// ---------------------------------------------------------------- S8: o Nappa cai, o Vegeta entra
		srv.PalcoSY_Derrubar("nappa", eu);
		bool fala = await Ate(() => Disse("Agora é a minha vez"), 5);
		bool vegetaVeio = await Ate(() => srv.PalcoSY_Chefe("vegeta_saiyajin") is { } v && DoJogador(srv, eu, v.Corpo.Pos) < 4 * T, 12);
		await Segundos(1.0);
		Fotografar("user://dsaiyajin-9-vegeta-luta.png");
		Conferir(fala && !srv.PalcoSY_Observando("vegeta_saiyajin") && vegetaVeio,
				 "S8. O NAPPA PERDE E O VEGETA ENTRA NA LUTA (*\"quando nappa perder ai sim vegeta começa a lutar\"*): a fala dele no chat, e ele vem pro jogador "
				 + $"[fala {fala}; veio {vegetaVeio}; ouvi: {UltimasDitas()}]");

		// ---------------------------------------------------------------- S9: o Vegeta cai, e Namek comeca
		srv.PalcoSY_Derrubar("vegeta_saiyajin", eu);
		bool fim = await Ate(() => Disse("NAPPA E VEGETA FORAM DERROTADOS") && srv.PalcoSY_Fase("freeza_namek") == "Contando", 6);
		await Segundos(0.8);
		Fotografar("user://dsaiyajin-10-vitoria.png");
		Conferir(fim, "S9. O VEGETA CAI: a saga Saiyajin acaba, e o Freeza parte pra Namek (a ordem do anime) "
					  + $"[saiyajins {srv.PalcoSY_Fase("saiyajins")}; namek {srv.PalcoSY_Fase("freeza_namek")}; ouvi: {UltimasDitas()}]");

		// ---------------------------------------------------------------- S10: os tres lado a lado (os adendos 210k e 210l)
		// Na luta eles aparecem pequenos, de lado ou na beira da tela (a r1 do ag/saiyajinpele): a foto da pele e do scouter e uma so, os tres
		// parados acima do jogador, de frente.
		List<int> tres = srv.PalcoSY_OsTresLadoALado(eu);
		int branco = srv.PalcoSY_IndiceDoCorpoBranco();
		bool osTres = await Ate(() => tres.Count == 3 && tres.All(i => World.Instancia?.LookDeTeste(i) is { } l && l.Ap.ScouterNoRosto && l.Ap.Corpo == branco), 6);
		await Segundos(1.2);
		Fotografar("user://dsaiyajin-11-os-tres.png");
		Conferir(osTres, $"S10. OS TRES LADO A LADO, DE FRENTE: de pele branca e de scouter no rosto [{tres.Count} corpos; "
						 + string.Join(", ", tres.Select(i => World.Instancia?.LookDeTeste(i) is { } l ? $"corpo {l.Ap.Corpo} scouter {l.Ap.ScouterNoRosto}" : "fora")) + "]");
		srv.PalcoSY_TirarOsTres(tres);
	}

	/// <summary>
	/// Este chefe esta na parte livre da tela (a camera e o jogador; fora do HUD, do chat e do aviso do jornal)? Pela posicao DESENHADA
	/// (`World.PosicaoDesenhadaDe`), e nao pela do servidor: a r2 do ag/fotosaiyajin viu os dois "no quadro" no servidor e a foto saiu com o
	/// desenho de um instante antes.
	/// </summary>
	private static bool NoQuadro(Jandirus.Server.GameServer srv, int eu, string molde) =>
		srv.PalcoSY_Chefe(molde) is { } c && World.Instancia?.PosicaoDesenhadaDe(c.Corpo.Id) is { } d && World.Instancia?.PosicaoLocal is { } e
		&& d.X - e.X is > -8 * T and < 9 * T && d.Y - e.Y is > -9 * T and < T;

	private const string BrancoDeScouter = "branco, de scouter";

	/// <summary>
	/// (2026-11-01, o adendo 210l) A cena de chegada na tela e a deste chefe, e o retrato dela e de scouter? -- a ficha que o pacote
	/// `S2C.ChegadaDoChefe` trouxe (o byte das marcas depois da aparencia), a mesma que o retrato veste.
	/// </summary>
	private static bool RetratoDeScouter(string nome) =>
		CenaDaChegada.NaTelaDeTeste is { } c && c.NomeDeTeste.Contains(nome, StringComparison.OrdinalIgnoreCase) && c.Chegada.Visual.ScouterNoRosto;

	/// <summary>
	/// (2026-11-01, os adendos 210k e 210l) O boneco deste chefe como o CLIENTE o recebeu (`World.LookDeTeste`, a ficha do pacote da aparencia):
	/// o corpo (o indice do `NewPaleMale` no catalogo = o de pele branca) e o scouter no rosto.
	/// </summary>
	private static string NaTela(Jandirus.Server.GameServer srv, string molde) =>
		srv.PalcoSY_Chefe(molde) is not { } c ? "sem corpo"
		: World.Instancia?.LookDeTeste(c.Corpo.Id) is not { } l ? "fora da tela"
		: (l.Ap.Corpo == srv.PalcoSY_IndiceDoCorpoBranco() ? "branco" : $"corpo {l.Ap.Corpo}") + (l.Ap.ScouterNoRosto ? ", de scouter" : ", sem scouter");

	/// <summary>A distancia (px) do jogador a este ponto, pelo corpo do servidor.</summary>
	private static float DoJogador(Jandirus.Server.GameServer srv, int eu, Vec2 p) =>
		srv.PalcoSY_PosicaoDe(eu) is { } eu2 ? (eu2 - p).Length : float.MaxValue;

	// =====================================================================
	// AS MAOS
	// =====================================================================
	private void Fotografar(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath(destino);
		img.SavePng(caminho);
		_linhas.Add("  --     foto salva em " + caminho);
	}

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + s;
		while (Time.GetTicksMsec() / 1000.0 < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
		while (Time.GetTicksMsec() / 1000.0 < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}

	private void Fechar()
	{
		foreach (string l in _linhas) GD.Print("[dsaiyajin] " + l);
		GD.Print(_falhas.Count == 0 ? "[dsaiyajin] ===== TUDO OK =====" : $"[dsaiyajin] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dsaiyajin]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
