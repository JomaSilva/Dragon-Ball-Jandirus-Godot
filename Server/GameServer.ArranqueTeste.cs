using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ O ARRANQUE QUE FICAVA LONGE (`--arranqueteste`) ============================
/// O dono (2026-09-05): *"as vezes o dash de soco, tanto so apertando espaco quanto segurando o shift,
/// acerta o soco no alvo mas o personagem ainda ta longe (o que nao faz sentido) -- ele deveria entrar no
/// range aonde o dash nao e mais ativado"*.
///
/// O ARRANQUE SEMPRE PAROU A UM TILE. O que o dono via era o que vinha DEPOIS dele: o cliente ja tinha
/// despachado inputs com a posicao de ANTES do arranque (um RTT deles esta no cabo), e cada um desses
/// pacotes, ao chegar, era validado contra a posicao NOVA -- longe demais -- e o `ValidateStep` fazia o
/// que faz com um pedido longe: andava o corpo ATE ELE pelo orcamento do tique. Quatro pacotes em voo,
/// ~25 px cada, e o corpo que acabara de chegar a 32 px do alvo estava de volta a 130. O soco ja tinha
/// acertado (foi resolvido no tique do arranque); o boneco e que "voltava". E a foto que ele mandou --
/// e quanto mais rapido o personagem e a conexao, pior, porque o orcamento por pacote cresce com a
/// velocidade.
///
/// A REGRA NOVA mora no `AplicarInput`: dentro da janela de correcao esperada (`CorrecaoEsperadaAte`,
/// os 500 ms que todo salto de posicao abre), um pedido LONGE do corpo e um pacote velho, nao um passo
/// -- o corpo fica onde esta e a correcao e reenviada. Fora da janela nada mudou: pedido longe continua
/// sendo arrastado e contado como correcao (a anti-trapaca de sempre), e a familia 4 mede as duas metades.
/// A familia 5 (2026-09-24) mede a mesma janela na TROCA DE ZONA, o unico salto do servidor que nao a abria.
///
///     Godot --headless --path . --host --rede 7913 --arranqueteste --raca Human --conta bancada_arranque --nome MedidorArranque
///
/// Forja na zona da bancada de projeteis (`Forjar`/`CorredorLivre`/`LimparTudoDaBancada` sao os da
/// `--projetilteste`, como a `--kbteste` ja faz) e manda "pacotes de input" pelo `AplicarInput`, que e
/// exatamente o que o `Input` do fio chama depois de ler o pacote.
/// ==========================================================================================================
/// </summary>
public sealed partial class GameServer
{
	private int _arrOk, _arrFalhou;

	private void AfirmarArranque(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _arrOk++; GD.Print($"[arranque]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _arrFalhou++; GD.PrintErr($"[arranque]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoArranque()
	{
		_arrOk = _arrFalhou = 0;
		_pjProximoCorredor = 8;
		GD.Print("[arranque] ================ O ARRANQUE QUE FICAVA LONGE (pedido do dono, 2026-09-05) ================");
		try
		{
			OArranquePesadoParaAUmTile();
			OPassoCurtoTambemParaAUmTile();
			OPertoDemaisAndaOResto();
			OInputEmVooNaoDesfazOArranque();
			ATrocaDeZonaTambemAbreAJanela();
		}
		finally { EscutaDeGolpes = null; LimparTudoDaBancada(); }
		GD.Print($"[arranque] ================ {_arrOk} OK, {_arrFalhou} FALHA(S) ================");
	}

	/// <summary>Atacante e alvo a `tiles` tiles, o atacante olhando pro alvo, Ki cheio e o arranque livre.</summary>
	private (ServerPlayer A, ServerPlayer D) DuplaDoArranque(string marca, float px, bool marcado)
	{
		Vec2 onde = CorredorLivre(24);
		ServerPlayer a = Forjar($"arr{marca}Bate", onde, 5_551);
		ServerPlayer d = Forjar($"arr{marca}Leva", onde + new Vec2(px, 0), 5_551);
		a.Facing = Facing.East;
		d.Facing = Facing.West;
		a.AlvoId = marcado ? d.Id : 0;
		a.Ficha.Ki = a.Ficha.MaxKi;
		a.DashLivreEm = 0;
		return (a, d);
	}

	private static float Vao(ServerPlayer a, ServerPlayer d) => (d.Pos - a.Pos).Length;

	private void OArranquePesadoParaAUmTile()
	{
		GD.Print("[arranque] --- 1) SHIFT + ESPACO com o alvo marcado a 4 tiles: para a UM tile e o soco acerta ---");
		(ServerPlayer a, ServerPlayer d) = DuplaDoArranque("Pesado", 4 * ZoneCollision.TileSize, marcado: true);
		Vec2 velha = a.Pos;
		EscutaDeGolpes = [];
		Atacar(a, Protocol.Golpe.Pesado);
		AfirmarArranque("o arranque para a DistanciaDeParada (32 px) do alvo -- dentro dos 40 px do soco",
			Math.Abs(Vao(a, d) - DistanciaDeParada) < 1f, $"vao {Vao(a, d):0.0} px (era {(d.Pos - velha).Length:0.0})");
		AfirmarArranque("...e o soco ACERTA no mesmo gesto (um relato de golpe saiu pro fio)",
			EscutaDeGolpes.Count > 0, $"{EscutaDeGolpes.Count} relato(s)");
		AfirmarArranque("...a janela de correcao esperada esta ABERTA (e ela que protege o arranque dos pacotes em voo)",
			a.CorrecaoEsperadaAte > NowMs(), $"{a.CorrecaoEsperadaAte - NowMs()} ms");
		EscutaDeGolpes = null;
	}

	private void OPassoCurtoTambemParaAUmTile()
	{
		GD.Print("[arranque] --- 2) so ESPACO, sem marca, alvo a 2 tiles no cone: o passo curto tambem para a um tile ---");
		(ServerPlayer a, ServerPlayer d) = DuplaDoArranque("Leve", 2 * ZoneCollision.TileSize, marcado: false);
		EscutaDeGolpes = [];
		Atacar(a, Protocol.Golpe.Leve);
		AfirmarArranque("o passo curto para a 32 px do alvo", Math.Abs(Vao(a, d) - DistanciaDeParada) < 1f, $"vao {Vao(a, d):0.0} px");
		AfirmarArranque("...e acerta", EscutaDeGolpes.Count > 0, $"{EscutaDeGolpes.Count} relato(s)");
		EscutaDeGolpes = null;
	}

	private void OPertoDemaisAndaOResto()
	{
		GD.Print("[arranque] --- 3) a 44 px (perto demais pro arranque, longe demais pro soco): o corpo ANDA o resto ---");
		(ServerPlayer a, ServerPlayer d) = DuplaDoArranque("Perto", 44f, marcado: true);
		Atacar(a, Protocol.Golpe.Leve);
		AfirmarArranque("sem investida (deslocamento < meio tile) o corpo fecha o vao ate os 32 px",
			Math.Abs(Vao(a, d) - DistanciaDeParada) < 1f, $"vao {Vao(a, d):0.0} px");
	}

	private void OInputEmVooNaoDesfazOArranque()
	{
		GD.Print("[arranque] --- 4) os pacotes de input que ja estavam no cabo (com a posicao VELHA) nao arrastam o corpo de volta ---");
		(ServerPlayer a, ServerPlayer d) = DuplaDoArranque("Voo", 4 * ZoneCollision.TileSize, marcado: true);
		Vec2 velha = a.Pos;
		Atacar(a, Protocol.Golpe.Pesado);
		AfirmarArranque("PREMISSA: chegou a um tile", Math.Abs(Vao(a, d) - DistanciaDeParada) < 1f, $"{Vao(a, d):0.0}");

		byte flags = (byte)((byte)Facing.East | Protocol.InputAndando);
		uint seq = a.SeqInput;
		for (int i = 1; i <= 4; i++) PacoteEmVoo(a, ++seq, velha, flags);
		AfirmarArranque("QUATRO pacotes em voo com a posicao de antes do arranque: o corpo NAO se move (continua a 32 px)",
			Math.Abs(Vao(a, d) - DistanciaDeParada) < 0.5f, $"vao {Vao(a, d):0.0} px");
		AfirmarArranque("...e nenhum deles conta como correcao de trapaca (a janela os explica)", a.Corrections == 0, $"{a.Corrections}");

		Vec2 honesto = a.Pos + new Vec2(0, 4f);
		PacoteEmVoo(a, ++seq, honesto, flags);
		AfirmarArranque("...um passo HONESTO a partir do destino, dentro da mesma janela, continua aceito",
			(a.Pos - honesto).Length < 0.01f, $"pediu ({honesto.X:0.0},{honesto.Y:0.0}) e ficou em ({a.Pos.X:0.0},{a.Pos.Y:0.0})");

		// O CONTRA-EXEMPLO E A REGRA DE ONTEM: com a janela fechada, o mesmo pacote longe ARRASTA.
		(ServerPlayer b, ServerPlayer e) = DuplaDoArranque("Ontem", 4 * ZoneCollision.TileSize, marcado: true);
		Vec2 velhaB = b.Pos;
		Atacar(b, Protocol.Golpe.Pesado);
		b.CorrecaoEsperadaAte = 0;   // a janela fechada: e como TODO pacote era tratado antes desta bancada
		uint seqB = b.SeqInput;
		float antes = Vao(b, e);
		for (int i = 1; i <= 4; i++) PacoteEmVoo(b, ++seqB, velhaB, flags);
		AfirmarArranque("CONTRA-EXEMPLO (a regra de ontem = fora da janela): os mesmos 4 pacotes ARRASTAM o corpo de volta pela folga do tique",
			Vao(b, e) > antes + 20f, $"vao {antes:0.0} -> {Vao(b, e):0.0} px");
		AfirmarArranque("...e fora da janela isso CONTA como correcao (a anti-trapaca nao mudou)", b.Corrections >= 4, $"{b.Corrections}");
	}

	/// <summary>
	/// 5) A TROCA DE ZONA E O MAIOR SALTO DE TODOS, e era o unico sem a janela. O `MoveToZone` carimbava so a
	/// SEQUENCIA, e ela so cobre o que JA CHEGOU. O cliente numera os pacotes sem nunca recomecar
	/// (`GameClient.SendState`, `++_seq`), e depois de LER o `ZoneChanged` ele ainda passa dois quadros com o
	/// corpo na zona velha -- o `TelaDeCarregamento.Cobrir` espera dois `ProcessFrame` antes do `Teleportar`,
	/// e o `LocalPlayer` manda a posicao a 30 Hz nesse meio tempo. Esse pacote (e os que ja estavam no cabo)
	/// chega com numero MAIOR que o carimbo, passa pelo filtro e era validado contra a posicao nova: contado
	/// como trapaca, e arrastando o corpo pela folga do tique rumo a coordenada da zona velha. A suspeita
	/// nasceu de um `1 correcoes de movimento (dt=422s)` logo depois de um Lendario pousar no mundo do exilio
	/// (`--torneioteste`, 2026-09-24); aquele aviso sozinho nao prova a causa -- esta familia mede a regra.
	///
	/// E O DT VAI JUNTO. Sem o `LastInputMs` renovado, o primeiro pacote depois da troca era pago pelo tempo
	/// passado ANTES dela: o `OrcamentoPx = 0` zerava o credito, e o dt contado desde o ultimo pacote da zona
	/// velha o enchia de volta ate o teto no mesmo instante.
	///
	/// A "zona nova" e a MESMA zona da bancada, seis tiles adiante num corredor seco: o que se mede e o
	/// carimbo do `MoveToZone`, e trocar de planeta de verdade poria na conta o embaralho, a gravidade e o
	/// povo de outro mapa. Os dois contra-exemplos sao o `MoveToZone` de antes, uma metade cada: a mesma
	/// troca com a janela fechada (arrasta e conta) e com o relogio parado na zona velha (o passo e pago).
	/// </summary>
	private void ATrocaDeZonaTambemAbreAJanela()
	{
		GD.Print("[arranque] --- 5) a TROCA DE ZONA: os pacotes com a posicao da zona velha nao arrastam nem contam, e o tempo de antes nao paga passo ---");
		byte flags = (byte)((byte)Facing.East | Protocol.InputAndando);
		var adiante = new Vec2(6 * ZoneCollision.TileSize, 0);

		// CHAO DE VERDADE, e nao o ponto fixo das familias 1-4 (sem mapa o `CorredorLivre` devolve sempre o mesmo
		// ponto, na beirada do mundo): aqui ha passo ACEITO, e passo aceito consulta o mapa -- a pe, agua e parede.
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		AfirmarArranque("PRECONDICAO: o mapa da zona da bancada esta carregado (os corredores saem dele)", _pjMapa != null);

		// ---- a janela: os pacotes montados antes de o cliente trocar de zona ----
		Vec2 velha = CorredorSeco(8);
		ServerPlayer a = Forjar("arrZonaAnda", velha, 5_551);
		Vec2 chegada = velha + adiante;
		MoveToZone(a.Id, a.Zone, chegada);
		uint seq = a.SeqInput;
		for (int i = 1; i <= 4; i++) PacoteEmVoo(a, ++seq, velha, flags);
		AfirmarArranque("QUATRO pacotes com a posicao da zona velha, numerados DEPOIS do carimbo: o corpo continua na chegada",
			(a.Pos - chegada).Length < 0.5f, $"{(a.Pos - chegada).Length:0.0} px da chegada");
		AfirmarArranque("...e nenhum conta como correcao de trapaca (a janela da troca os explica)", a.Corrections == 0, $"{a.Corrections}");

		Vec2 honesto = a.Pos + new Vec2(4f, 0);
		PacoteEmVoo(a, ++seq, honesto, flags);
		AfirmarArranque("...e um passo HONESTO a partir da chegada, dentro da mesma janela, continua aceito",
			(a.Pos - honesto).Length < 0.01f, $"pediu ({honesto.X:0.0},{honesto.Y:0.0}) e ficou em ({a.Pos.X:0.0},{a.Pos.Y:0.0})");

		// O CONTRA-EXEMPLO DA JANELA E O `MoveToZone` DE ANTES: so a sequencia. A mesma troca arrasta e conta.
		Vec2 velhaB = CorredorSeco(8);
		ServerPlayer b = Forjar("arrZonaOntem", velhaB, 5_551);
		Vec2 chegadaB = velhaB + adiante;
		MoveToZone(b.Id, b.Zone, chegadaB);
		b.CorrecaoEsperadaAte = 0;
		uint seqB = b.SeqInput;
		for (int i = 1; i <= 4; i++) PacoteEmVoo(b, ++seqB, velhaB, flags);
		float arrastado = (chegadaB - velhaB).Length - (b.Pos - velhaB).Length;
		AfirmarArranque("CONTRA-EXEMPLO (a troca sem janela): os mesmos 4 pacotes ARRASTAM o corpo rumo a coordenada velha",
			arrastado > 20f, $"{arrastado:0.0} px de volta");
		AfirmarArranque("...e contam como correcao", b.Corrections >= 4, $"{b.Corrections}");

		// ---- o relogio: o primeiro pacote depois da troca ----
		// QUANTO UM RELOGIO VELHO PAGA, perguntado a propria regra e nao copiado dela: credito zerado (como o
		// `MoveToZone` deixa) e um dt de cinco segundos, que o `ValidateStep` prende no `MaxDeltaSeconds`, andam
		// ate o teto do orcamento; a folga de correcao vem por cima. O passo medido e o maior que isso paga.
		float credito = 0f;
		MoveRules.ValidateStep(Vec2.Zero, new Vec2(10_000f, 0), 5f, a.SpeedStat, ref credito, out Vec2 pago);
		float passo = pago.X + MoveRules.MinCorrectionPx - 1f;

		Vec2 velhaC = CorredorSeco(8);
		ServerPlayer c = Forjar("arrZonaRelogio", velhaC, 5_551);
		c.LastInputMs = NowMs() - 5_000;   // o ultimo pacote dele chegou na zona velha, cinco segundos antes da troca
		Vec2 chegadaC = velhaC + adiante;
		MoveToZone(c.Id, c.Zone, chegadaC);
		long dtMs = NowMs() - c.LastInputMs;
		AplicarInput(c, c.SeqInput + 1, (uint)RelogioDeQuadrosMs(), chegadaC + new Vec2(passo, 0), flags);
		AfirmarArranque($"o PRIMEIRO pacote depois da troca nao e pago pelo tempo de ANTES dela: {passo:0.0} px no instante do salto nao andam",
			(c.Pos - chegadaC).Length < 0.5f, $"{(c.Pos - chegadaC).Length:0.0} px andados, dt {dtMs} ms");
		AfirmarArranque("...e a recusa cai na janela da troca (nao conta como trapaca)", c.Corrections == 0, $"{c.Corrections}");

		// O CONTRA-EXEMPLO DO RELOGIO: a mesma troca com o dt ainda contando da zona velha paga o passo inteiro.
		Vec2 velhaD = CorredorSeco(8);
		ServerPlayer d = Forjar("arrZonaRelogioOntem", velhaD, 5_551);
		Vec2 chegadaD = velhaD + adiante;
		MoveToZone(d.Id, d.Zone, chegadaD);
		d.LastInputMs = NowMs() - 5_000;
		Vec2 pedidoD = chegadaD + new Vec2(passo, 0);
		AplicarInput(d, d.SeqInput + 1, (uint)RelogioDeQuadrosMs(), pedidoD, flags);
		AfirmarArranque($"CONTRA-EXEMPLO (o relogio parado na zona velha): os mesmos {passo:0.0} px sao pagos e o corpo ANDA",
			(d.Pos - pedidoD).Length < 0.01f, $"{(d.Pos - chegadaD).Length:0.0} px andados");
	}

	/// <summary>Um pacote de input como o cliente o monta, chegando 100 ms depois do anterior.</summary>
	private void PacoteEmVoo(ServerPlayer pl, uint seq, Vec2 claimed, byte flags)
	{
		pl.LastInputMs = NowMs() - 100;
		AplicarInput(pl, seq, (uint)RelogioDeQuadrosMs(), claimed, flags);
	}
}
