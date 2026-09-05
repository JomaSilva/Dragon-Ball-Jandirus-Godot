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

	/// <summary>Um pacote de input como o cliente o monta, chegando 100 ms depois do anterior.</summary>
	private void PacoteEmVoo(ServerPlayer pl, uint seq, Vec2 claimed, byte flags)
	{
		pl.LastInputMs = NowMs() - 100;
		AplicarInput(pl, seq, (uint)RelogioDeQuadrosMs(), claimed, flags);
	}
}
