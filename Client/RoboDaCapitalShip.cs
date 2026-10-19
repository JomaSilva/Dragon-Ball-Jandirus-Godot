using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA CAPITAL SHIP (`--diagcapital`, 2026-10-18): as fotos de quatro pedidos do dono --
///   * *"permita construir dentro da capital ship (gravidade , research etc mas nao paredes etc) e se ela explodir esses itens saem da nave
///     sem estarem parafusados"*;
///   * *"o personagem fica voando quando eu piloto a nave , ele deveria so ficar parado na capital ship e meditando na pod"*;
///   * *"permita q na capital ship ao colocar pra ela ir pra um planeta com nav system e o jogador sair do piloto, ela ainda continua, e avisa
///     no chat quando chegar"*.
///
/// Com janela e com `--host`, nascendo Humano no campo aberto da Terra:
///   C1. dentro da nave grande, a maquina de gravidade (ligada: o filtro vermelho) e a mesa de pesquisa -- foto `capital-1-maquinas-dentro`; o
///       fantasma do cliente recusa a plataforma e a chegada (a mesma pergunta do servidor), e o CONTRA-EXEMPLO (`TelaDeConstrucao.SemZonaDeTeste`,
///       o fantasma de antes) deixa a chegada branca;
///   C2. a nave explode: o corpo e ejetado e as duas maquinas estao no chao de fora, SOLTAS (o contorno dourado) -- foto `capital-2-explodiu`;
///   C3. ao leme de outra nave grande, o meu boneco PARADO e nao na pose de voo -- foto `capital-3-ao-leme`; CONTRA-EXEMPLO
///       (`LocalPlayer.AoLemeVoaDeTeste`): a pose de voo -- foto `capital-3b-defeito-voando`;
///   C4. o nav system pra Namek (o que o "Viajar para" da aba Nav faz ao leme) e o "Voltar a ponte" do menu da tecla E: o destino vai junto, o
///       corpo volta pra ponte e o chat diz que ela sobe sozinha -- foto `capital-4-rumo`;
///   C5. a viagem encurtada pelo palco: ela sobe sem piloto, segue e POUSA em Namek, e o chat diz que chegou -- foto `capital-5-chegou`;
///   C6. pela plataforma, Namek, com a nave pousada ao lado -- foto `capital-6-namek`;
///   C7. um pod que decola: o meu boneco MEDITANDO dentro dele -- foto `capital-7-pod-meditando`;
///   C8. (*"faça a pod e a nave quando chegarem no planeta pousarem em algum local aleatorio com terra firme, e a pod quando pousa faz uma
///       cratera grande"*) o pod sobe pro espaco e encosta em Namek: pousa num lugar sorteado em terra firme, longe do berco, e a cratera esta
///       no chao da minha tela -- o estrondo soou com o mapa ja na tela -- fotos `capital-8-pod-cratera` e `capital-8b-cratera-assentada`;
///       CONTRA-EXEMPLO (`World.DecalqueSemEsperaDoChaoDeTeste`, o decalque plantado na hora): o estrondo soa atras da tela de carregamento; e
///       a segunda descida cai noutro lugar.
///   C9. (2026-10-18, *"ao pilotar qualquer tipo de nave, deve aparecer no canto uma barrinha de combustivel q mostra o nivel dele, comecando
///       verde ... amarelo quando ta na metade e vermelho quanto ta acabando) a barra tb mostra o consumo de combustivel por segundo"*) no pod,
///       no ar e andando: a barra no canto, verde e com o gasto por segundo -- foto `capital-9-combustivel-verde`; na metade, amarela; acabando,
///       vermelha -- fotos `capital-9b`/`9c`; CONTRA-EXEMPLO (`PainelDeCombustivel.CorUnicaDeTeste`, a barra de uma cor so): acabando, verde;
///       seco: "VAZIO", a nave desce e o corpo nao anda -- foto `capital-9d-vazio`; e o "Abastecer" da tecla E enche com recursos -- foto
///       `capital-9e-abastecido`;
///  C11. (*"enquanto vc pilota, deveria dar pra escutar e ver o chat q rola dentro da nave"*) uma nave grande no espaco: o leme pela tecla E do
///       console, e a fala do tripulante la dentro chega no chat de quem pilota -- foto `capital-11-chat-ao-leme`;
///  C10. (*"a opcao de observe, deveria 'assistir' o exterior da nave sem pode controla-la"*) o "Observar" do console: o olho sai, o boneco
///       some, o casco e desenhado no olho, ele anda com a nave que segue sozinha e a barra diz "(observando)" -- fotos `capital-10-observando`
///       e `capital-10b-andou`; CONTRA-EXEMPLO (`LocalPlayer.OlhoComCorpoDeTeste`): o boneco de pe no meio do espaco -- foto
///       `capital-10c-defeito-com-corpo`; e o "Voltar ao corpo" da tecla E devolve a ponte.
///
///     Godot --path . --host --rede 8469 --campoteste 12 --horateste 0.5 --semfauna --diagcapital --raca Human --conta bancada_capital --nome Capitao
/// </summary>
public partial class RoboDaCapitalShip : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ditas = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

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

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null
									  && MenuDeInteracao.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu da tecla E nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dcapital] ===== A CAPITAL SHIP: AS MAQUINAS DENTRO, A EXPLOSAO, O LEME E O RUMO SOZINHO (dono, 2026-10-18) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);
		cli.Falou += Ouvir;
		try
		{
			await AsMaquinasDentro(cli, srv, eu);
			await OLemeEORumo(cli, srv, eu);
			await OPod(cli, srv, eu);
			await OPodCaiDoCeu(cli, srv, eu);
			await OCombustivel(cli, srv, eu);
			await OChatAoLemeEOObservar(cli, srv, eu);
		}
		finally
		{
			cli.Falou -= Ouvir;
			srv.PalcoCS_Limpar(eu);
		}
	}

	/// <summary>O que o servidor disse a este cliente (os avisos chegam pelo canal de fala).</summary>
	private void Ouvir(Protocol.Fala canal, string quem, string texto) => _ditas.Add(texto);

	private bool Disse(string trecho) => _ditas.Any(t => t.Contains(trecho, StringComparison.Ordinal));

	private string UltimasDitas() => string.Join(" | ", _ditas.TakeLast(3));

	// =====================================================================
	// C1-C2) AS MAQUINAS DENTRO, E A EXPLOSAO
	// =====================================================================
	private async Tarefa AsMaquinasDentro(GameClient cli, Jandirus.Server.GameServer srv, int eu)
	{
		int nave = srv.PalcoCS_NaveComMaquinas(eu);
		ZoneKey dentro = NaveGrande.ZonaDoInterior(nave);
		bool chegou = nave > 0 && await Ate(() => cli.Zone.Equals(dentro) && cli.Obras.Any(o => o.Tipo == "Gravity")
												  && cli.Obras.Any(o => o.Tipo == "Research_Station"), 15);
		Conferir(chegou, "C1. DENTRO DA CAPITAL SHIP SE CONSTROI (*\"permita construir dentro da capital ship (gravidade , research etc\"*): o corpo "
						 + "embarcou, e a maquina de gravidade e a mesa de pesquisa chegaram a este cliente no chao do interior");
		if (!chegou) return;
		await Segundos(2.0);   // o campo de gravidade (relido a 5 Hz) e o desenho

		(int X, int Y) ch = NaveGrande.CelDaChegada;
		RecusaDeAssento naPlataforma = TelaDeConstrucao.RecusaEm(NaveGrande.CelDaPlataforma.X, NaveGrande.CelDaPlataforma.Y);
		RecusaDeAssento naChegada = TelaDeConstrucao.RecusaEm(ch.X, ch.Y);
		RecusaDeAssento aoLado = TelaDeConstrucao.RecusaEm(ch.X, ch.Y + 2);
		Conferir(naPlataforma == RecusaDeAssento.PassagemDaNave && naChegada == RecusaDeAssento.PassagemDaNave && aoLado == RecusaDeAssento.Pode,
				 "C1b. O FANTASMA DO CLIENTE FAZ A PERGUNTA DO SERVIDOR: a plataforma e a chegada sao passagem (ele fica vermelho), e o chao dois tiles "
				 + $"abaixo aceita [plataforma {naPlataforma}; chegada {naChegada}; abaixo {aoLado}]");
		TelaDeConstrucao.SemZonaDeTeste = true;
		RecusaDeAssento chegadaDeAntes;
		try { chegadaDeAntes = TelaDeConstrucao.RecusaEm(ch.X, ch.Y); }
		finally { TelaDeConstrucao.SemZonaDeTeste = false; }
		Conferir(chegadaDeAntes == RecusaDeAssento.Pode,
				 $"CONTRA-EXEMPLO (defeito injetado: o fantasma sem a zona, o de antes): a chegada fica branca -- a regua de C1b reprovaria [{chegadaDeAntes}]");
		Fotografar("user://capital-1-maquinas-dentro.png");

		srv.PalcoCS_Explodir(nave);
		bool fora = await Ate(() => !NaveGrande.EhInterior(cli.Zone, out _) && MaquinasPerto(cli).Count >= 2, 15);
		await Segundos(1.5);
		List<GameClient.ObraInfo> soltas = MaquinasPerto(cli);
		Conferir(fora && soltas.Count >= 2 && soltas.All(o => !o.Aparafusada),
				 "C2. A NAVE EXPLODE E AS MAQUINAS SAEM SOLTAS (*\"se ela explodir esses itens saem da nave sem estarem parafusados\"*): o corpo foi "
				 + "ejetado, e as duas estao no chao de fora, perto dele, sem o parafuso (o contorno dourado de \"solta\") "
				 + $"[{string.Join(", ", soltas.Select(o => $"{o.Tipo} aparafusada={o.Aparafusada}"))}]");
		Fotografar("user://capital-2-explodiu.png");
	}

	/// <summary>A maquina de gravidade e a mesa de pesquisa a ate oito tiles de mim -- as da cidade, se houver, ficam de fora.</summary>
	private static List<GameClient.ObraInfo> MaquinasPerto(GameClient cli)
	{
		Vector2 eu = World.Instancia?.PosicaoLocal ?? Vector2.Zero;
		return [.. cli.Obras.Where(o => o.Tipo is "Gravity" or "Research_Station" && o.Pos.DistanceTo(eu) <= 8 * ZoneCollision.TileSize)];
	}

	// =====================================================================
	// C3-C6) O LEME, O RUMO SOZINHO, A CHEGADA E NAMEK
	// =====================================================================
	private async Tarefa OLemeEORumo(GameClient cli, Jandirus.Server.GameServer srv, int eu)
	{
		int nave = srv.PalcoCS_AoLeme(eu);
		bool aoLeme = nave > 0 && await Ate(() => cli.VeiculoMontado == NaveGrande.Tipo, 10);
		await Segundos(2.0);   // o leme liga o voo, e o corpo paira
		string estado = World.Instancia?.VisualLocalDeTeste?.EstadoDeTeste ?? "";
		Conferir(aoLeme && estado == "default",
				 "C3. AO LEME DA CAPITAL SHIP O PERSONAGEM FICA PARADO (*\"o personagem fica voando quando eu piloto a nave , ele deveria so ficar parado "
				 + $"na capital ship\"*): a pose do meu boneco e a de parado, e nao a de voo [ao leme {aoLeme}; pose {estado}]");
		Fotografar("user://capital-3-ao-leme.png");
		LocalPlayer.AoLemeVoaDeTeste = true;
		string deAntes;
		try
		{
			await Quadros(4);
			deAntes = World.Instancia?.VisualLocalDeTeste?.EstadoDeTeste ?? "";
			Fotografar("user://capital-3b-defeito-voando.png");
		}
		finally { LocalPlayer.AoLemeVoaDeTeste = false; }
		await Quadros(4);
		Conferir(deAntes == "flight", $"CONTRA-EXEMPLO (defeito injetado: a pose de antes): ao leme, o meu boneco na pose de voo -- a regua de C3 reprovaria [{deAntes}]");
		if (!aoLeme) return;

		// O NAV SYSTEM PRA NAMEK -- o que o botao "Viajar para" da aba Nav faz ao leme de uma nave pousada (`MenuJogo.Nav.DecolarERumar`) -- e o
		// "Voltar a ponte" do menu da tecla E, apertado como o jogador aperta: o destino vai no argumento do verbo.
		PlanetaNoEspaco namek = Espaco.PreFeitos().First(p => string.Equals(p.Nome, "Namek", StringComparison.OrdinalIgnoreCase));
		LocalPlayer.DecolarERumar(namek.Pos);
		_ditas.Clear();
		ApertarE();
		bool menu = await Ate(() => MenuDeInteracao.Instancia is { NaTela: true }, 3);
		bool apertou = menu && MenuDeInteracao.Instancia!.ApertarDesenhado("Voltar à ponte");
		bool rumou = apertou && await Ate(() => NaveGrande.EhInterior(cli.Zone, out _) && Disse("ela sobe sozinha e segue rumo a"), 10);
		Conferir(rumou, "C4. LARGAR O LEME COM O NAV SYSTEM LIGADO (*\"ao colocar pra ela ir pra um planeta com nav system e o jogador sair do "
						+ "piloto, ela ainda continua\"*): o \"Voltar à ponte\" levou o destino, o corpo voltou pra ponte e o chat diz que ela sobe sozinha "
						+ $"[menu {menu}; apertou {apertou}; ouvi: {UltimasDitas()}]");
		await Segundos(1.0);
		Fotografar("user://capital-4-rumo.png");
		if (!rumou) return;

		srv.PalcoCS_LancarJa(nave);
		bool subiu = await Ate(() => srv.PalcoCS_NoEspaco(nave), 5);
		await Segundos(0.5);
		_ditas.Clear();
		srv.PalcoCS_PertoDoDestino(nave);
		bool pousou = subiu && await Ate(() => Disse("chegou: pousou em"), 15);
		Conferir(pousou, "C5. ELA CHEGA SOZINHA E AVISA NO CHAT (*\"e avisa no chat quando chegar\"*): subiu sem piloto, seguiu o rumo e pousou em "
						 + $"Namek -- e o aviso chegou a quem esta dentro [subiu {subiu}; ouvi: {UltimasDitas()}]");
		await Segundos(0.5);
		Fotografar("user://capital-5-chegou.png");

		srv.PalcoCS_Sair(eu);
		bool emNamek = await Ate(() => cli.Zone.Equals(ZoneKey.Premade(namek.Nome)), 15);
		await Segundos(2.5);
		bool naveLa = cli.Obras.Any(o => o.Tipo == NaveGrande.Tipo);
		Conferir(emNamek && naveLa, $"C6. DESCENDO PELA PLATAFORMA, E NAMEK -- com a capital ship pousada ao lado [zona {cli.Zone}; a nave na tela {naveLa}]");
		Fotografar("user://capital-6-namek.png");
	}

	// =====================================================================
	// C7) O POD
	// =====================================================================
	private async Tarefa OPod(GameClient cli, Jandirus.Server.GameServer srv, int eu)
	{
		int pod = srv.PalcoCS_PodVoando(eu);
		bool noPod = pod > 0 && await Ate(() => cli.VeiculoMontado.Length > 0 && cli.VeiculoMontado != NaveGrande.Tipo, 10);
		await Segundos(2.0);   // decolou: o corpo paira com o pod
		string estado = World.Instancia?.VisualLocalDeTeste?.EstadoDeTeste ?? "";
		Conferir(noPod && estado == "meditate",
				 $"C7. NO POD O PERSONAGEM MEDITA (*\"e meditando na pod\"*): o pod decolou e a pose do meu boneco e a de meditar [{cli.VeiculoMontado}; pose {estado}]");
		Fotografar("user://capital-7-pod-meditando.png");
	}

	// =====================================================================
	// C8) O POD CAI DO CEU: UM LUGAR SORTEADO, E A CRATERA
	// =====================================================================
	private async Tarefa OPodCaiDoCeu(GameClient cli, Jandirus.Server.GameServer srv, int eu)
	{
		ZoneKey namek = ZoneKey.Premade("Namek");
		bool NaTela(ZoneKey z) => cli.Zone.Equals(z) && World.Instancia?.ZonaMontadaDeTeste == z.Hash && TelaDeCarregamento.Instancia?.NoAr != true;

		// UMA DESCIDA: o pod sobe pro espaco ao lado de Namek, o espaco monta, ele encosta no disco -- e o servidor pousa (o lugar e a cratera).
		async System.Threading.Tasks.Task<bool> Descer()
		{
			if (!srv.PalcoCS_PodNoEspaco(eu, "Namek")) return false;
			if (!await Ate(() => NaTela(srv.ZonaDoEspaco), 15)) return false;
			await Segundos(0.5);
			srv.PalcoCS_PodSobre(eu, "Namek");
			return await Ate(() => NaTela(namek), 15);
		}

		World? w = World.Instancia;
		int estrondosAntes = w?.EstrondosDoPodDeTeste ?? 0;
		bool desceu = await Descer();
		await Segundos(0.6);   // a cratera cresceu (0,35 s), e a poeira esta no alto
		(bool firme, float doBerco, (int X, int Y) cel1) = srv.PalcoCS_OndePousou(eu);
		int crateras = w?.DecalquesDeTeste?.CraterasDoPodVivasDeTeste ?? 0;
		bool estrondou = (w?.EstrondosDoPodDeTeste ?? 0) > estrondosAntes;
		bool naFrente = estrondou && w?.UltimoEstrondoAtrasDaTelaDeTeste == false;
		Conferir(desceu && firme && doBerco >= 3 * ZoneCollision.TileSize,
				"C8. O POD CAI NUM LUGAR SORTEADO, EM TERRA FIRME (*\"quando chegarem no planeta pousarem em algum local aleatorio com terra firme\"*): do "
				+ "espaco, ele pousou em Namek num chao a ceu aberto com a folga dele, longe do berco "
				+ $"[desceu {desceu}; firme {firme}; a {doBerco / ZoneCollision.TileSize:0} tiles do berco; celula {cel1}]");
		Conferir(crateras >= 1 && naFrente,
				"C8b. ...E ABRE UMA CRATERA GRANDE (*\"e a pod quando pousa faz uma cratera grande\"*): a cratera esta no chao da minha tela, e o estrondo "
				+ $"soou com o mapa de Namek ja na tela, e nao atras da tela de carregamento [crateras {crateras}; estrondo {estrondou}; na frente {naFrente}]");
		Fotografar("user://capital-8-pod-cratera.png");
		await Segundos(2.5);   // a poeira assenta
		Fotografar("user://capital-8b-cratera-assentada.png");

		// O CONTRA-EXEMPLO: sem a fila do chao novo, a cratera nasce (e o estrondo soa) atras da tela de carregamento.
		World.DecalqueSemEsperaDoChaoDeTeste = true;
		bool desceuDeNovo;
		try
		{
			desceuDeNovo = await Descer();
			await Segundos(0.6);
		}
		finally { World.DecalqueSemEsperaDoChaoDeTeste = false; }
		(bool firme2, _, (int X, int Y) cel2) = srv.PalcoCS_OndePousou(eu);
		Conferir(desceuDeNovo && w?.UltimoEstrondoAtrasDaTelaDeTeste == true,
				"CONTRA-EXEMPLO (defeito injetado: o decalque da troca de zona plantado na hora, o de antes): o estrondo soou atras da tela de "
				+ $"carregamento -- a regua de C8b reprovaria [desceu {desceuDeNovo}; atras da tela {w?.UltimoEstrondoAtrasDaTelaDeTeste}]");
		Conferir(desceuDeNovo && firme2 && cel2 != cel1, $"C8c. ...e a segunda descida caiu NOUTRO lugar sorteado, tambem em terra firme [{cel1} e {cel2}]");
	}

	// =====================================================================
	// C9) A BARRA DO COMBUSTIVEL (2026-10-18)
	// =====================================================================
	private async Tarefa OCombustivel(GameClient cli, Jandirus.Server.GameServer srv, int eu)
	{
		PainelDeCombustivel? barra = PainelDeCombustivel.Instancia;
		string direita = Teclas.NomeNoMotor("move_right");

		// NO AR E ANDANDO: a tecla de andar segurada ate o servidor fechar um segundo de gasto (o "por segundo" e o da ultima volta, como a
		// mineracao) -- e a foto sai com ela ainda apertada.
		bool noAr = srv.PalcoCS_PodNoAr(eu);
		srv.PalcoCS_Tanque(eu, 0.9);
		bool apareceu = await Ate(() => barra is { NaTela: true } && cli.Tanque is { Modo: 1, Capacidade: > 0 }, 5);
		Godot.Input.ActionPress(direita);
		bool gastando;
		try
		{
			gastando = await Ate(() => cli.Tanque.PorSegundo > 0, 5);
			await Quadros(4);
			Fotografar("user://capital-9-combustivel-verde.png");
		}
		finally { Godot.Input.ActionRelease(direita); }
		Color verde = barra?.CorDeTeste ?? default;
		string texto = barra?.TextoDeTeste ?? "";
		Conferir(noAr && apareceu && gastando && Perto(verde, PainelDeCombustivel.Verde) && texto.Contains("/s", StringComparison.Ordinal),
				 "C9. AO PILOTAR, A BARRA DO COMBUSTIVEL NO CANTO (*\"comecando verde ... a barra tb mostra o consumo de combustivel por segundo\"*): "
				 + $"no pod, no ar e andando, ela aparece, VERDE com o tanque quase cheio, e diz quanto gasta por segundo [no ar {noAr}; na tela {apareceu}; "
				 + $"gastando {gastando}; cor {verde}; \"{texto}\"]");

		async System.Threading.Tasks.Task<Color> CorEm(double fracao)
		{
			srv.PalcoCS_Tanque(eu, fracao);
			await Ate(() => cli.Tanque.Capacidade > 0 && Math.Abs(cli.Tanque.Nivel / cli.Tanque.Capacidade - fracao) < 0.005, 3);
			await Quadros(4);
			return barra?.CorDeTeste ?? default;
		}

		Color amarelo = await CorEm(0.5);
		Fotografar("user://capital-9b-combustivel-amarelo.png");
		Color vermelho = await CorEm(0.1);
		Fotografar("user://capital-9c-combustivel-vermelho.png");
		Conferir(Perto(amarelo, PainelDeCombustivel.Amarelo) && Perto(vermelho, PainelDeCombustivel.Vermelho),
				 $"C9b. ...AMARELA NA METADE E VERMELHA ACABANDO (*\"amarelo quando ta na metade e vermelho quanto ta acabando\"*) [metade {amarelo}; 10% {vermelho}]");
		PainelDeCombustivel.CorUnicaDeTeste = true;
		Color umaSo;
		try { umaSo = await CorEm(0.11); }
		finally { PainelDeCombustivel.CorUnicaDeTeste = false; }
		Conferir(Perto(umaSo, PainelDeCombustivel.Verde),
				 $"CONTRA-EXEMPLO (defeito injetado: a barra de uma cor so): acabando, ela continua verde -- a regua de C9b reprovaria [{umaSo}]");

		// SECO: a nave para de vez -- desce, pousa, e o corpo nao anda nem segurando a tecla.
		srv.PalcoCS_Tanque(eu, 0);
		bool seco = await Ate(() => cli.Tanque.Nivel <= 0 && (barra?.TextoDeTeste ?? "").Contains("VAZIO", StringComparison.Ordinal), 4);
		await Segundos(1.5);   // a descida
		Vector2 antes = World.Instancia?.PosicaoLocal ?? Vector2.Zero;
		Godot.Input.ActionPress(direita);
		try { await Segundos(1.0); }
		finally { Godot.Input.ActionRelease(direita); }
		float andou = ((World.Instancia?.PosicaoLocal ?? Vector2.Zero) - antes).Length();
		Fotografar("user://capital-9d-vazio.png");
		Conferir(seco && andou < 2,
				 "C9c. SECO, A NAVE PARA DE VEZ (o dono escolheu *\"para de vez\"*): a barra diz VAZIO e o corpo nao sai do lugar segurando a tecla "
				 + $"[seco {seco}; andou {andou:0.#} px; \"{barra?.TextoDeTeste}\"]");

		// ABASTECER PELA TECLA E, com recursos no bolso.
		srv.PalcoCS_Recursos(eu, 5_000);
		await Segundos(0.3);
		_ditas.Clear();
		bool cheio = await PeloMenu("Abastecer", () => cli.Tanque.Capacidade > 0 && cli.Tanque.Nivel >= cli.Tanque.Capacidade - 1);
		await Quadros(6);
		Fotografar("user://capital-9e-abastecido.png");
		Conferir(cheio && Disse("você enche o tanque"),
				 "C9d. O \"ABASTECER\" DA TECLA E ENCHE O TANQUE COM RECURSOS (*\"pegar recursos e abastecer a nave\"*): o tanque volta cheio e o chat "
				 + $"diz o que custou [cheio {cheio}; ouvi: {UltimasDitas()}]");
	}

	/// <summary>Duas cores iguais a olho (a barra pinta o `StyleBoxFlat` com a cor exata da rampa).</summary>
	private static bool Perto(Color a, Color b) =>
		Math.Abs(a.R - b.R) < 0.02f && Math.Abs(a.G - b.G) < 0.02f && Math.Abs(a.B - b.B) < 0.02f;

	// =====================================================================
	// C10-C11) O CHAT AO LEME E O OBSERVAR (2026-10-18)
	// =====================================================================
	private async Tarefa OChatAoLemeEOObservar(GameClient cli, Jandirus.Server.GameServer srv, int eu)
	{
		int nave = srv.PalcoCS_NaveNoEspacoComTripulante(eu);
		ZoneKey dentro = NaveGrande.ZonaDoInterior(nave);
		bool aBordo = nave > 0 && await Ate(() => NaTela(cli, dentro), 15);
		Conferir(aBordo, "(montagem) uma nave grande parada no espaco: eu embarcado, de pe ao lado do console, e um tripulante perto da ponte");
		if (!aBordo) return;
		await Segundos(1.0);

		// ---------------------------------------------------------------- C11: o leme pela tecla E do console, e a fala do tripulante
		bool aoLeme = await PeloMenu("Pilotar", () => cli.VeiculoMontado == NaveGrande.Tipo && NaTela(cli, srv.ZonaDoEspaco));
		await Segundos(1.5);
		_ditas.Clear();
		srv.PalcoCS_TripulanteFala("piloto, tudo certo ai fora?");
		bool ouviu = await Ate(() => Disse("piloto, tudo certo ai fora?"), 5);
		await Segundos(0.5);
		Fotografar("user://capital-11-chat-ao-leme.png");
		Conferir(aoLeme && ouviu, "C11. AO LEME SE OUVE A NAVE (*\"enquanto vc pilota, deveria dar pra escutar e ver o chat q rola dentro da nave\"*): com a "
								  + $"camera do lado de fora, a fala do tripulante la dentro chega no meu chat [ao leme {aoLeme}; ouvi: {UltimasDitas()}]");
		bool naPonte = await PeloMenu("Voltar à ponte", () => NaTela(cli, dentro));

		// ---------------------------------------------------------------- C10: o observar, primeiro com o defeito (o boneco no olho), depois o certo
		srv.PalcoCS_Seguir(nave);
		LocalPlayer.OlhoComCorpoDeTeste = true;
		bool comCorpo;
		try
		{
			bool saiu = naPonte && await PeloMenu("Observar", () => cli.Observador && NaTela(cli, srv.ZonaDoEspaco));
			await Segundos(1.0);
			comCorpo = saiu && World.Instancia?.VisualLocalDeTeste?.Visible == true;
			Fotografar("user://capital-10c-defeito-com-corpo.png");
		}
		finally { LocalPlayer.OlhoComCorpoDeTeste = false; }
		bool voltou = await PeloMenu("Voltar ao corpo", () => !cli.Observador && NaTela(cli, dentro));
		Conferir(comCorpo, "CONTRA-EXEMPLO (defeito injetado: o olho com o boneco): observando, um personagem de pe no meio do espaco, ao lado do casco -- "
						   + "a regua de C10 reprovaria");

		bool observa = voltou && await PeloMenu("Observar", () => cli.Observador && NaTela(cli, srv.ZonaDoEspaco));
		await Segundos(1.5);
		Vector2 olhoAntes = World.Instancia?.PosicaoLocal ?? Vector2.Zero;
		double vidaAntes = cli.Sheet.HP;
		(bool noServidor, _, Vec2 olho, Vec2 casco) = srv.PalcoCS_Olho(eu, nave);
		bool semCorpo = World.Instancia?.VisualLocalDeTeste?.Visible == false;
		bool cascoNoOlho = World.Instancia?.NaveNoOlhoDeTeste != null;
		bool semCopia = World.Instancia?.CopiaParadaVisivelDeTeste(nave) == false;
		bool barraDoOlho = (PainelDeCombustivel.Instancia?.TextoDeTeste ?? "").Contains("(observando)", StringComparison.Ordinal);
		Fotografar("user://capital-10-observando.png");
		await Segundos(2.0);
		float andou = ((World.Instancia?.PosicaoLocal ?? Vector2.Zero) - olhoAntes).Length();
		double vidaDepois = cli.Sheet.HP;
		Fotografar("user://capital-10b-andou.png");
		Conferir(observa && noServidor && (olho - casco).Length < 1 && semCorpo && cascoNoOlho && semCopia && barraDoOlho && andou > 50,
				 "C10. OBSERVAR LEVA O OLHO PRA FORA (*\"deveria 'assistir' o exterior da nave sem pode controla-la, bom pros jogadores q querem ver a "
				 + "nave ir pelo espaco\"*): o corpo ficou na ponte, o olho esta no casco, o boneco some, a nave e desenhada no olho (UMA so: a copia "
				 + "parada do pacote de construcoes sai da tela), a barra diz \"(observando)\", e a camera anda com a nave que segue sozinha "
				 + $"[observa {observa}; no servidor {noServidor}; olho a {(olho - casco).Length:0} px do casco; sem corpo {semCorpo}; casco no olho "
				 + $"{cascoNoOlho}; sem a copia {semCopia}; barra {barraDoOlho}; andou {andou:0} px em 2 s]");
		Conferir(vidaDepois >= vidaAntes - 1e-6,
				 $"C10a. ...e o VAZIO nao tira a vida de quem observa (o corpo esta abrigado na ponte) [vida {vidaAntes:0.#} -> {vidaDepois:0.#}]");
		World.CopiaParadaNaTelaDeTeste = true;
		bool copiaDeAntes;
		try
		{
			await Segundos(1.2);   // o pacote de construcoes da nave que segue sozinha sai uma vez por segundo
			copiaDeAntes = World.Instancia?.CopiaParadaVisivelDeTeste(nave) == true;
			Fotografar("user://capital-10d-defeito-duas-naves.png");
		}
		finally { World.CopiaParadaNaTelaDeTeste = false; }
		await Quadros(4);
		Conferir(copiaDeAntes, "CONTRA-EXEMPLO (defeito injetado: a copia parada na tela): duas naves, a do olho e a parada um segundo atras -- a regua de "
							   + "C10 reprovaria");

		bool deVolta = await PeloMenu("Voltar ao corpo", () => !cli.Observador && NaTela(cli, dentro));
		await Segundos(0.5);
		Conferir(deVolta && World.Instancia?.VisualLocalDeTeste?.Visible == true && World.Instancia?.NaveNoOlhoDeTeste == null,
				 "C10b. ...e o \"Voltar ao corpo\" da tecla E devolve o olho a ponte, com o boneco de volta e sem o casco no olho");
	}

	/// <summary>A zona esta montada na minha tela (sem a tela de carregamento por cima).</summary>
	private static bool NaTela(GameClient cli, ZoneKey z) =>
		cli.Zone.Equals(z) && World.Instancia?.ZonaMontadaDeTeste == z.Hash && TelaDeCarregamento.Instancia?.NoAr != true;

	/// <summary>A TECLA E, e o botao do menu que ela abriu -- como o jogador: devolve se o efeito chegou (a condicao) em ate 15 s.</summary>
	private async System.Threading.Tasks.Task<bool> PeloMenu(string botao, Func<bool> chegou)
	{
		ApertarE();
		bool menu = await Ate(() => MenuDeInteracao.Instancia is { NaTela: true }, 3);
		bool apertou = menu && MenuDeInteracao.Instancia!.ApertarDesenhado(botao);
		if (!apertou) _linhas.Add($"  --     o botao \"{botao}\" nao estava no menu da tecla E (menu {menu})");
		return apertou && await Ate(chegou, 15);
	}

	// =====================================================================
	// AS MAOS
	// =====================================================================
	/// <summary>A TECLA E de verdade: apertada e solta, empurrada no viewport (o caminho do `_UnhandledInput`, como o robo do embarque).</summary>
	private void ApertarE()
	{
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = true });
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = false });
	}

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
		TelaDeConstrucao.SemZonaDeTeste = false;
		LocalPlayer.AoLemeVoaDeTeste = false;
		World.DecalqueSemEsperaDoChaoDeTeste = false;
		PainelDeCombustivel.CorUnicaDeTeste = false;
		LocalPlayer.OlhoComCorpoDeTeste = false;
		World.CopiaParadaNaTelaDeTeste = false;
		Godot.Input.ActionRelease(Teclas.NomeNoMotor("move_right"));
		foreach (string l in _linhas) GD.Print("[dcapital] " + l);
		GD.Print(_falhas.Count == 0 ? "[dcapital] ===== TUDO OK =====" : $"[dcapital] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dcapital]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
