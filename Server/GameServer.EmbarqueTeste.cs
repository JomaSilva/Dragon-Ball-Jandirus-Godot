using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A METADE DE SERVIDOR DA BANCADA DO EMBARQUE (`--embarqueteste`) -- **e ela nao pontua nada**.
///
/// ============================ POR QUE ESTE ARQUIVO NAO TEM UM SO `Checa` ============================
/// A pergunta do dono e sobre um GESTO: *"ao apertar E perto delas... abrindo um menu dela, e ai vai
/// ter a opcao de entrar ou sair etc"*. Quem responde por um gesto e o cliente -- o menu que abriu, o
/// botao que estava nele, o dedo que o apertou. Uma checagem de servidor que afirmasse "o menu tem
/// Embarcar" estaria lendo a TABELA (`Interacoes.De`), e o arquivo `dbclimax-port-bancada-mede-
/// intencao` conta exatamente o que essa leitura vale: quatro defeitos visuais passaram por quatro
/// mil checagens verdes porque a bancada media a tabela e o defeito morava no widget.
///
/// Entao o placar inteiro mora no `RoboDeEmbarque`, e este arquivo e so o que o robo NAO consegue
/// fazer de dentro do jogo:
///
///   * dar a tecnologia e o zeni pra a Capital Ship caber (2.000.000z / tech 55) -- e o mesmo que
///     as outras bancadas de nave fazem, e pela mesma razao: um teste que exija farmar antes de
///     comecar nao e rodado;
///   * passar a nave pro nome de OUTRA PESSOA e tranca-la, que e a unica forma de exercitar as duas
///     recusas de dono e a de senha sem uma segunda conta ao vivo;
///   * derrubar o casco com alguem dentro, que e o caminho que nenhum verbo de jogador alcanca;
///   * levar o corpo a um PALCO -- chao seco e livre na Terra, longe de toda coisa interativa --, que
///     e a premissa de que o percurso inteiro depende e que o berco deixou de cumprir (ver
///     `PalcoDoEmbarque`).
///
/// OS QUATRO SO EXISTEM COM A FLAG (ver `Verbo`), e os tres ultimos passam pelos MESMOS metodos de
/// producao que o mundo usa (`EstragarNave`, o campo `Senha` que o `EmbarcarNaNaveGrande` le, o
/// `MoveToZone` de toda viagem). Uma bancada que forjasse a recusa na mao mediria a propria mentira.
/// ================================================================================================
///
///     Godot --headless --path . --host --rede 7995 --embarqueteste --diagembarque
///           --conta bancemb --nome BancEmb
/// </summary>
public partial class GameServer
{
	private bool _embarqueDeTeste;

	/// <summary>
	/// A SENHA QUE A BANCADA POE NA NAVE ALHEIA. Seis algarismos DE PROPOSITO: o botao promete "um
	/// código de até 6 dígitos" (`Interacoes.De(Capital_Ship)`, `Forma.Numero, 0, 999999`) e o
	/// teclado do menu tinha teto de quatro -- o quinto dedo nao entrava e nada reclamava. Uma senha
	/// de quatro aqui deixaria essa porta fechada passar verde pra sempre.
	/// </summary>
	public const string SenhaDaBancada = "271828";

	/// <summary>A conta e o nome de quem a bancada finge ser o dono da nave.</summary>
	public const string ContaAlheiaDaBancada = "conta_de_fulano";
	public const string DonoAlheioDaBancada = "Fulano";

	/// <summary>
	/// PREPARA O CORPO PRA BANCADA -- tecnologia e zeni, e mais nada.
	///
	/// NAO ASSENTA A NAVE: quem a fabrica e a assenta e o robo, pela aba Tech e pelo verbo
	/// `posicionar`, que e o caminho do jogador. Uma nave posta aqui pularia o primeiro elo da
	/// corrente que esta sob teste.
	///
	/// E NAO LEVA O CORPO PRO PALCO: isto roda no meio do login, antes de o corpo entrar na lista da
	/// zona e antes do `JoinAccepted` -- o `MoveToZone` mexeria numa lista em que ele ainda nao esta e
	/// mandaria `ZoneChanged` a um cliente que nem entrou. Quem pede o palco e o robo, ja em jogo
	/// (`emb_palco`).
	/// </summary>
	private void PrepararBancadaDeEmbarque(ServerPlayer pl)
	{
		pl.Ficha.techskill = 60;
		pl.Ficha.Zeni = 5_000_000;
		MandarFicha(pl);
		GD.Print($"[server] BANCADA DE EMBARQUE: {pl.Name} com tech 60 e 5.000.000z. "
				 + "O placar sai do lado do cliente (`--diagembarque`).");
	}

	/// <summary>
	/// OS QUATRO VERBOS DE FIXTURE. So respondem com a flag ligada -- sem ela nem chegam aqui.
	///
	/// Eles NAO sao interacao com objeto e nao entram no <see cref="Interacoes"/> de proposito: nada
	/// disto e coisa que um jogador possa fazer, e por-los no catalogo faria o menu da tecla E
	/// oferece-los a quem estivesse perto de uma nave.
	/// </summary>
	private bool ComandoDaBancadaDeEmbarque(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			// O CORPO VAI PRO PALCO, pelo `MoveToZone` -- o funil de toda viagem (berco, morte, nave), com o
			// carimbo de teleporte, o reenvio das obras da zona e o `ZoneChanged` que o cliente trata como
			// qualquer outro. E o primeiro passo do robo, antes do F2.1. Ver `PalcoDoEmbarque`.
			case "emb_palco":
			{
				ZoneKey terra = ZoneKey.Premade("Earth");
				Vec2 berco = PontoDeNascimento(terra);
				if (PalcoDoEmbarque(terra, berco, pl.Id) is not { } palco)
				{
					// EM VOZ ALTA, e sem mexer no corpo: largar o robo no berco calado traria de volta as 38
					// vermelhas em cascata, cada uma culpando um elo que nao foi medido.
					Avisar(pl, $"[bancada] nao achei palco em {terra.Name}: nenhum quadrado de "
							   + $"{2 * MeiaLarguraDoPalco + 1}x{2 * MeiaLarguraDoPalco + 1} celulas de chao seco "
							   + "longe de toda obra, nave e corpo.");
					return true;
				}
				MoveToZone(pl.Id, terra, palco);
				GD.Print($"[server] BANCADA DE EMBARQUE: palco em {terra.Name} @ ({palco.X:0},{palco.Y:0}) "
						 + $"-- o berco fica em ({berco.X:0},{berco.Y:0})");
				Avisar(pl, $"[bancada] palco em {terra.Name} @ ({palco.X:0},{palco.Y:0}).");
				return true;
			}

			// A NAVE PASSA PRO NOME DE OUTRA PESSOA, e trancada. E o unico jeito de uma bancada de
			// uma conta so exercitar "esta nave e de outro" -- e repare que ela mexe SO no dono e na
			// senha: as recusas continuam saindo dos metodos de producao, que leem esses dois campos.
			case "emb_alienar":
			{
				Nave? n = NavePerto(pl);
				if (n == null) { Avisar(pl, "[bancada] nao ha nave por perto pra alienar."); return true; }
				n.DonoConta = ContaAlheiaDaBancada;
				n.DonoNome = DonoAlheioDaBancada;
				n.Senha = SenhaDaBancada;
				GravarNaves();
				MandarObras(pl.Zone);
				Avisar(pl, $"[bancada] a nave #{n.Id} agora e de {DonoAlheioDaBancada} e esta trancada.");
				return true;
			}

			// E VOLTA A SER MINHA, destrancada. Sem isto a bancada teria que fabricar uma segunda
			// nave pra continuar, e o resto do percurso mediria uma nave que nunca foi de ninguem.
			case "emb_devolver":
			{
				Nave? n = NavePerto(pl) ?? NaveDesteInterior(pl);
				if (n == null) { Avisar(pl, "[bancada] nao ha nave por perto pra devolver."); return true; }
				n.DonoConta = pl.Conta;
				n.DonoNome = pl.Name;
				n.Senha = "";
				GravarNaves();
				MandarObras(pl.Zone);
				Avisar(pl, $"[bancada] a nave #{n.Id} voltou a ser sua, destrancada.");
				return true;
			}

			// O CASCO CEDE COM VOCE DENTRO. `autor` nulo e "nao ha algoz", que e o mesmo caminho do
			// corpo arremessado contra o casco -- e o unico que passa pela ejecao.
			case "emb_estragar":
			{
				Nave? n = NaveDesteInterior(pl) ?? NavePerto(pl);
				if (n == null) { Avisar(pl, "[bancada] nao ha nave pra estragar."); return true; }
				EstragarNave(n, n.ArmaduraMax * 5, null);
				return true;
			}

			default: return false;
		}
	}

	/// <summary>
	/// A MEIA LARGURA DO PALCO, em celulas -- e o quanto o percurso de fora anda a partir dele: o robo
	/// recua quatro tiles antes de se aproximar da nave, e a nave cai a dois tiles do OUTRO lado (o
	/// `posicionar` recebe o centro da celula), com o desembarque a um tile e meio alem dela
	/// (`PontoAoLadoDaNave`). Cinco cobre os dois lados com folga. Publica porque o robo
	/// confere o palco com o MESMO numero (`RoboDeEmbarque.PalcoLimpo`), e duas copias dele eram a
	/// chance de o servidor entregar um palco que o robo reprova -- ou o contrario.
	/// </summary>
	public const int MeiaLarguraDoPalco = 5;

	/// <summary>
	/// ============================ O PALCO DA BANCADA -- e por que ela deixou de rodar no berco ============================
	/// O percurso inteiro do robo presume CHAO VAZIO em volta de onde ele comeca: o F2.1 aperta E ali e
	/// cobra "nao abre nada", a nave e assentada a dois tiles, e a borda do alcance e medida andando na
	/// direcao dela. Isso era verdade no ponto unico de nascimento de antes. **Desde o 0b0c8b2 o corpo
	/// acorda no `/obj/SpawnPoint` do BYOND** (`PontoDeNascimento`), e o da Terra fica DENTRO da cidade.
	///
	/// Medido na rodada de 2026-09-24 (34 OK, 38 FALHA, igual com e sem o conserto do `MoveToZone`): a nave
	/// foi assentada em (2416,8336), o E do F2.1 abriu o "Bank", a caminhada abriu o menu a 171 px da nave
	/// porque a "Research Station" estava mais perto, o `emb_alienar` respondeu "nao ha nave por perto" e
	/// nenhum Embarcar foi apertado -- o corpo ficou na Terra e o resto caiu em cascata, inclusive o robo
	/// tentando andar ate a ponte de uma nave em que nunca entrou ("faltavam (-1944,-6720) px").
	///
	/// A REGRA DO PALCO sao as duas coisas que o percurso precisa, e so elas:
	///   * CHAO A PE -- um quadrado de `2 * MeiaLarguraDoPalco + 1` celulas de lado que servem de chao
	///     (`ServeDeChao`: sem parede, sem agua, sem nuvem que derruba, sem beirada -- a mesma pergunta do
	///     passo e do `Assentamento`). Qualquer um dos quatro lados que o robo tente pra nave cai nele, e a
	///     caminhada nao para numa pedra;
	///   * NADA POR PERTO -- nenhuma obra (interativa ou nao: a que nao abre menu ainda pode ser densa e
	///     barrar a caminhada), nenhuma nave parada (o entulho de uma rodada interrompida) e nenhum corpo (um
	///     cadaver tambem e alvo da tecla E, e quem esta de pe no corredor barra o passo) a menos de
	///     `MeiaLarguraDoPalco` tiles + o alcance do menu + um tile, POR EIXO -- que e como o menu e o
	///     `ObraPerto` medem. De qualquer ponto do percurso, a unica coisa ao alcance da mao e a nave dele.
	///
	/// A BUSCA PARTE DO BERCO, anel por anel (o desenho do `PontoLivrePerto`), e por isso o palco e o
	/// pedaco de Terra livre MAIS PERTO de onde o jogador acorda: a bancada continua medindo a Terra de
	/// sempre, so que fora da cidade. Sem palco devolve nulo -- quem chama diz isso em voz alta.
	/// ===================================================================================================================
	/// </summary>
	private Vec2? PalcoDoEmbarque(ZoneKey zona, Vec2 perto, int quem)
	{
		const int T = ZoneCollision.TileSize;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		// SEM MAPA NAO HA PALCO: ninguem saberia dizer se o chao serve, e "serve" e a metade da regra.
		if (mapa == null) return null;

		float longe = MeiaLarguraDoPalco * T + Interacoes.Alcance + T;
		var coisas = new List<Vec2>();
		foreach (Obra o in _noChao) if (o.Zona.Equals(zona)) coisas.Add(new Vec2(o.X, o.Y));
		foreach (Nave n in NavesParadasEm(zona)) coisas.Add(new Vec2(n.X, n.Y));
		foreach (ServerPlayer o in ZoneList(zona.Hash)) if (o.Id != quem) coisas.Add(o.Pos);

		int cx0 = (int)MathF.Floor(perto.X / T), cy0 = (int)MathF.Floor(perto.Y / T);
		int raioMax = Math.Max(mapa.Width, mapa.Height);
		for (int r = 0; r <= raioMax; r++)
			for (int dx = -r; dx <= r; dx++)
				for (int dy = -r; dy <= r; dy++)
				{
					if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;   // so a borda do anel
					int cx = cx0 + dx, cy = cy0 + dy;
					if (!mapa.ServeDeChao(cx, cy)) continue;

					Vec2 centro = mapa.CentroDaCelula(cx, cy);
					bool sozinho = true;
					foreach (Vec2 c in coisas)
						if (Math.Abs(c.X - centro.X) <= longe && Math.Abs(c.Y - centro.Y) <= longe) { sozinho = false; break; }
					if (!sozinho) continue;

					bool chao = true;
					for (int ox = -MeiaLarguraDoPalco; ox <= MeiaLarguraDoPalco && chao; ox++)
						for (int oy = -MeiaLarguraDoPalco; oy <= MeiaLarguraDoPalco && chao; oy++)
							chao = mapa.ServeDeChao(cx + ox, cy + oy);
					if (chao) return centro;
				}
		return null;
	}
}
