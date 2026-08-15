using Godot;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DOS DOIS RELATOS DO DONO (`--presoteste`) -- e cada familia obrigada a ficar VERMELHA
/// com o defeito dentro.
///
/// ============================ POR QUE ELA EXISTE, TENDO A `agua-prova` ============================
/// A `Tools/AssetPipeline -- agua-prova` ja cobre o ESCAPE (familias 8, 9 e 10 de la): zero pixel no
/// meio do lago, quem nasce na pedra saindo, a beira que nao congela. Ela e C# puro e mede o
/// `MoveRules` de producao, que e onde o buraco morava.
///
/// **Tres coisas do pedido nao cabem la, e as tres sao servidor:**
///
///   1. **a exaustao deixa o corpo ONDE ESTA** -- quem decide isso e o `TickDoNado`, e o que o dono
///      mandou tirar (`LevarProSeco`) era uma linha DELE. O `MoveRules` nunca soube que ela existia:
///      uma bancada de funcao pura fica verde com o teleporte de volta no lugar;
///   2. **recarregando o Ki ele volta a nadar E CONTINUA** -- o ciclo atravessa TRES funis do
///      servidor (a regeneracao no `TickDaCarga`, o verb no `AlternarNado`, o custo no `TickDoNado`)
///      e o `MoveRules` no meio. Nenhum deles sozinho responde "ele sai daqui?";
///   3. **a meditacao leva pra DIMENSAO BRANCA** -- o relato A. O lugar e uma planta do Core, mas
///      quem escolhe entre ela e o z24 do BYOND e o `MapaDaZonaOuCatalogo`, e a escolha e uma ORDEM
///      de `??` dentro de um `switch` do servidor.
///
/// ============================ E DEPOIS A MENTE PERDEU A PAREDE (familia 3b) ============================
/// *"faca tb o MAPA DA MENTE ser INFINITO SEM BORDAS e CARREGAR POR CHUNK, tb o FUNDO BRANCO, pq as
/// BORDAS PRETAS sao estranhas e as vezes o NPC VOA PRA FORA e ele TELEPORTA DE VOLTA"*.
///
/// A familia 3 virou do avesso por causa disso -- ela dizia "ele PARA em todos os rumos, e um quarto
/// fechado" e hoje exige o oposto, com o mesmo numero de corte. E nasceu a **3b**, que mede o preco:
/// sem parede, ninguem devolve quem foge, e o reflexo corre na velocidade do dono (ele copia a ficha
/// dele). A coleira (`DimensaoMental.RaioDaColeira`) e a resposta, e ela e do SERVIDOR -- e por isso
/// esta aqui e nao numa bancada de funcao pura.
/// ====================================================================================================
///
/// ============================ O DESENHO E O DA `--provateste` ============================
///     1. o criterio e uma FUNCAO NOMEADA e mede o codigo de PRODUCAO   -> tem que passar
///     2. injeta-se um defeito de verdade                               -> tem que REPROVAR
///     3. desfaz-se o defeito                                           -> tem que passar de novo
///
/// **O criterio e o MESMO objeto nas tres vezes** (ver <see cref="Mutacao"/>). Os defeitos nao sao
/// inventados: o do relato B e o `LevarProSeco` LITERAL que foi deletado desta fase, e o do ciclo de
/// Ki e o `if` que estava escrito no verb antes de o `Nado.KiParaComecar` existir.
///
/// ============================ O QUE ELA MEXE ============================
/// Ela forja corpos sem `Peer` num planeta onde nao ha ninguem conectado, nada com eles e os tira no
/// `finally`. Nao escreve em disco nenhum, nao toca conta e nao manda byte no fio.
///
///     Godot --headless --path . --server --rede 7926 --presoteste
/// </summary>
public partial class GameServer
{
	/// <summary>Faixa de ids de bancada -- longe do `_nextId` e das outras bancadas.</summary>
	private const int IdBaseDoPreso = 90_900;

	/// <summary>
	/// QUANTOS TILES DE AGUA ABERTA A LESTE a bancada exige do mapa. Ver <see cref="AguaAbertaEm"/>:
	/// 40 e folgado sobre os ~10 que uma rodada de nado percorre, e a folga e o que garante que o que
	/// termina a rodada e o KI e nao a margem.
	/// </summary>
	private const int CorridaDeMarAberto = 40;

	private int _presoOk, _presoFalhou;

	private void AfirmarPreso(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _presoOk++; GD.Print($"[preso]   OK    {oque}"); return; }
		_presoFalhou++;
		GD.PrintErr($"[preso]   FALHA {oque}   {detalhe}");
	}

	// =====================================================================
	// AS REGRAS -- e e isto que permite injetar o defeito de verdade
	// =====================================================================
	/// <summary>
	/// OS FUNIS DE PRODUCAO QUE ESTA BANCADA MEDE, cada campo comecando no metodo do jogo.
	///
	/// Trocar um campo aqui e trocar a implementacao debaixo das provas -- que e a unica forma
	/// honesta de provar que uma familia pegaria uma mudanca de CODIGO. Com os valores iniciais, a
	/// bancada verde e a bancada medindo o servidor.
	/// </summary>
	private sealed class RegrasDoPreso
	{
		/// <summary>`TickDoNado` -- o custo, a exaustao e o desligar sozinho.</summary>
		public required Action<ServerPlayer, float> TiqueDoNado;

		/// <summary>`AlternarNado` -- o verb, com o gate de <see cref="Nado.KiParaComecar"/>.</summary>
		public required Action<ServerPlayer> Verb;

		/// <summary>`MapaDaZonaOuCatalogo` -- o funil unico de "onde ha parede nesta zona".</summary>
		public required Func<ZoneKey, ZoneCollision?> Mapa;

		/// <summary>`GameServer.PosMental` -- onde o corpo nasce dentro da mente.</summary>
		public required Func<Vec2> Nascimento;

		/// <summary>`DimensaoMental.Planta` -- o quarto branco em si.</summary>
		public required Func<TerrenoGerado> Planta;

		/// <summary>`Ceu.RelogioDaZona` -- que ceu tem este lugar. E o que decide a COR da cena.</summary>
		public required Func<ZoneKey, RelogioDoPlaneta> Relogio;
	}

	private RegrasDoPreso NovasRegras() => new()
	{
		TiqueDoNado = TickDoNado,
		Verb = AlternarNado,
		Mapa = MapaDaZonaOuCatalogo,
		Nascimento = () => PosMental,
		Planta = DimensaoMental.Planta,
		Relogio = RelogioDaZona,
	};

	// =====================================================================
	// A RODADA
	// =====================================================================
	public void RodarBancadaDoPreso()
	{
		_presoOk = _presoFalhou = 0;
		GD.Print("[preso] ======== OS DOIS RELATOS: A AGUA QUE PRENDE E A DIMENSAO BRANCA ========");

		var forjados = new List<ServerPlayer>();
		try
		{
			// A ZONA DA AGUA: a primeira pre-feita com lago -- e a bancada DIZ qual, porque uma prova
			// que nao conta em que mapa rodou nao da pra reproduzir.
			ZoneKey? comLago = null;
			(int Cx, int Cy)? lago = null;
			foreach (PlanetaNoEspaco pn in Espaco.PreFeitos())
			{
				ZoneKey zk = ZoneKey.Premade(pn.Nome);
				if (AguaAbertaEm(MapaDaZonaOuCatalogo(zk), CorridaDeMarAberto) is not { } achado) continue;
				comLago = zk; lago = achado; break;
			}

			// FALHA, E NAO "PULADO": uma bancada que se desliga sozinha quando o cenario nao coopera
			// fica verde sem medir nada -- e o assunto aqui e agua.
			AfirmarPreso("ha planeta pre-feito com agua funda pra medir (o `.agua` carregou)",
						 lago != null, "nenhuma zona pre-feita tem lago -- rode o AssetPipeline");

			if (lago is { } L && comLago is { } zona)
			{
				GD.Print($"[preso] lago: '{zona.Name}' na celula ({L.Cx},{L.Cy})");
				AExaustaoDeixaOCorpoOndeEsta(forjados, zona, L);
				ORecarregarFazOCicloAndar(forjados, zona, L);
			}

			AMeditacaoLevaProQuartoBranco(forjados);
		}
		catch (Exception e)
		{
			AfirmarPreso($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			foreach (ServerPlayer p in forjados)
			{
				ZoneList(p.Zone.Hash).Remove(p);
				_players.Remove(p.Id);
			}
		}

		GD.Print($"[preso] ======== {_presoOk} OK, {_presoFalhou} FALHA(S) ========");
	}

	/// <summary>
	/// MAR ABERTO: a ponta oeste de uma faixa com <paramref name="corrida"/> tiles de agua a leste e
	/// as vizinhas de cima e de baixo tambem molhadas.
	///
	/// ============================ POR QUE NAO BASTA "UMA CELULA DE AGUA" ============================
	/// Duas razoes, e as duas foram MEDIDAS nesta bancada:
	///
	///   * a primeira agua do mapa costuma ser BEIRA, e um corpo largado na beira sai andando **com
	///     razao** (e a regra da quina, `MoveRules.QuinaValida`). Medir "ele nao anda" ali reprovaria
	///     a regra certa;
	///   * a primeira versao disto pedia so as OITO VIZINHAS e caiu na celula (333,2) da Terra --
	///     uma poca. A rodada de nado morreu em 2,2 tiles porque a AGUA acabou, e a bancada leu isso
	///     como "o ciclo nao anda". Era o cenario que nao dava dois passos, nao a regra.
	///
	/// A CORRIDA E MAIOR QUE A RODADA DE PROPOSITO: se ela fosse do tamanho do que o corpo percorre,
	/// a prova mediria de novo o fim do lago em vez do fim do Ki.
	/// ============================================================================================
	/// </summary>
	private static (int Cx, int Cy)? AguaAbertaEm(ZoneCollision? m, int corrida)
	{
		if (m == null || !m.TemAgua) return null;
		for (int cy = 4; cy < m.Height - 4; cy++)
			for (int cx = 4; cx < m.Width - corrida - 4; cx++)
			{
				if (!m.EhAgua(cx, cy)) continue;
				bool toda = true;
				for (int dx = 0; dx <= corrida && toda; dx++)
					for (int dy = -1; dy <= 1 && toda; dy++)
						if (!m.EhAgua(cx + dx, cy + dy)) toda = false;
				if (toda) return (cx, cy);
			}
		return null;
	}

	private ServerPlayer ForjarPreso(List<ServerPlayer> forjados, int i, string nome,
									 ZoneKey zona, Vec2 pos, double bp)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDoPreso + i,
			Peer = null,
			Name = nome,
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = zona,
			Pos = pos,
			Conta = $"bancada_preso_{i}",
			Slot = 0,
			Ficha = new Fighter { Race = "Human", BP = bp },
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		RecalcularVelocidade(novo);
		forjados.Add(novo);
		return novo;
	}

	private static Vec2 NoCentroDaCelula(int cx, int cy)
		=> new(cx * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f,
			   cy * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f - MoveRules.FeetOffsetY);

	// =====================================================================
	// 1. A EXAUSTAO DEIXA O CORPO ONDE ESTA -- o relato B, primeira metade
	// =====================================================================
	/// <summary>
	/// *"entao TIRA ISSO DE TELEPORTAR PRA MARGEM"*.
	///
	/// ============================ SAO DUAS AFIRMACOES, E ELAS SE SEPARAM ============================
	/// "o corpo nao atravessa o lago" e "o corpo nao foi MOVIDO" parecem a mesma coisa e nao sao: o
	/// `LevarProSeco` cumpria a primeira com nota cheia -- ele tirava o corpo da agua -- e era
	/// exatamente o que o dono mandou tirar. Uma bancada que so medisse a travessia ficaria VERDE com
	/// o teleporte de volta no lugar.
	///
	/// Por isso a prova central aqui e a POSICAO, comparada bit a bit antes e depois do tique da
	/// exaustao. E o defeito injetado e o `LevarProSeco` LITERAL, restaurado no mutante: 12 aneis de
	/// `ChaoLivrePerto` e a mensagem *"voce e levado pra margem"*.
	/// =========================================================================================
	/// </summary>
	private void AExaustaoDeixaOCorpoOndeEsta(List<ServerPlayer> forjados, ZoneKey zona, (int Cx, int Cy) lago)
	{
		GD.Print("[preso] --- 1. a exaustao deixa o corpo ONDE ESTA (nao teleporta) ---");

		ServerPlayer pl = ForjarPreso(forjados, 1, "bancada: o nadador exausto", zona,
									  NoCentroDaCelula(lago.Cx, lago.Cy), 1_000);
		RegrasDoPreso r = NovasRegras();

		// O ESTADO DE PRODUCAO DE QUEM VAI EXAUSTAR: nadando, ja molhado, com o tanque no fio do corte.
		void Preparar()
		{
			pl.Pos = NoCentroDaCelula(lago.Cx, lago.Cy);
			pl.Nadando = true;
			pl.NadoJaMolhou = true;
			pl.Ficha.Ki = Nado.KiQuePara(pl.Ficha.MaxKi) * 0.5;
			RecalcularVelocidade(pl);
		}

		// O CRITERIO E UM SO E MEDE O FUNIL: um tique, e a posicao tem que ser a mesma.
		Vec2 onde = Vec2.Zero;
		bool NaoSeMexeu()
		{
			Preparar();
			Vec2 antes = pl.Pos;
			r.TiqueDoNado(pl, Jandirus.Net.Protocol.TickSeconds);
			onde = pl.Pos;
			return pl.Pos.X == antes.X && pl.Pos.Y == antes.Y;
		}

		Mutacao(AfirmarPreso,
				"o tique da exaustao NAO move o corpo (a posicao e a mesma, bit a bit)",
				"o `LevarProSeco` de volta -- o remedio que o dono mandou tirar",
				NaoSeMexeu,
				() => r.TiqueDoNado = (p, dt) =>
				{
					TickDoNado(p, dt);
					// O CORPO DO `LevarProSeco` DELETADO, literal: a margem mais proxima em 12 aneis.
					if (p.Nadando || p.Ficha.Ki > 0) return;
					if (MapaDaZonaOuCatalogo(p.Zone) is { } m && ChaoLivrePerto(m, p.Pos) is { } seco)
						p.Pos = seco;
				},
				() => r.TiqueDoNado = TickDoNado);

		// E O RESTO DO RAMO DA EXAUSTAO, que e o `Stats.dm:405-410` literal.
		Preparar();
		EscutaDeAvisos = [];
		r.TiqueDoNado(pl, Jandirus.Net.Protocol.TickSeconds);
		List<string> falou = EscutaDeAvisos ?? [];
		EscutaDeAvisos = null;

		AfirmarPreso("...e ele para de nadar", !pl.Nadando);
		AfirmarPreso("...e o Ki vai a ZERO (o `f.Ki = 0` literal do `Stats.dm:405-410`)",
					 pl.Ficha.Ki == 0, $"{pl.Ficha.Ki}");
		AfirmarPreso("...e ele continua EM CIMA DA AGUA (e ai que o dono pediu que ele ficasse)",
					 SobreAgua(pl), $"({onde.X:0},{onde.Y:0})");

		// A FRASE E PARTE DA REGRA. O dono pediu "ficar preso ate RECARREGAR": um corpo boiando sem
		// nada na tela dizendo o que fazer e indistinguivel de um corpo travado por bug -- e a
		// primeira coisa que o jogador faz e martelar a tecla, que era justamente o que nao andava.
		AfirmarPreso("...e o aviso diz O QUE FAZER (esperar o Ki e nadar de novo)",
					 falou.Exists(a => a.Contains("Ki", StringComparison.OrdinalIgnoreCase)
									   && a.Contains("nade", StringComparison.OrdinalIgnoreCase)),
					 string.Join(" | ", falou));

		// ============================ E O `LevarProSeco` NAO EXISTE MAIS EM LUGAR NENHUM ============================
		// Regra da casa: codigo substituido se DELETA. A prova e estrutural porque a alternativa e
		// medir por ausencia -- e verdade por ausencia e a que morre calada no dia em que alguem
		// reescrever a funcao com o mesmo nome "so pro caso do arremesso".
		// ========================================================================================
		AfirmarPreso("o `LevarProSeco` nao existe mais no fonte do nado (codigo substituido se DELETA)",
					 !FonteDoServidor("GameServer.Nado.cs").Contains("private void LevarProSeco",
																	StringComparison.Ordinal));

		// E QUEM ESTA NOCAUTEADO TAMBEM FICA. Era o outro chamador do teleporte, e o pedido vale
		// igual: quem acorda liga o nado de novo.
		Preparar();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		Vec2 antesKo = pl.Pos;
		pl.Combate.Nocautear(5);
		r.TiqueDoNado(pl, Jandirus.Net.Protocol.TickSeconds);
		AfirmarPreso("nocauteado dentro da agua ele tambem fica onde esta (so o nado cai)",
					 !pl.Nadando && pl.Pos.X == antesKo.X && pl.Pos.Y == antesKo.Y,
					 $"({pl.Pos.X:0},{pl.Pos.Y:0}) x ({antesKo.X:0},{antesKo.Y:0})");
		pl.Combate.Levantar();
	}

	// =====================================================================
	// 2. RECARREGANDO, ELE VOLTA A NADAR E CONTINUA -- o relato B, segunda metade
	// =====================================================================
	/// <summary>
	/// *"tendo q RECARREGAR O KI pra voltar a nadar e CONTINUAR"* -- e o "continuar" e a palavra que
	/// esta familia existe pra cobrar.
	///
	/// ============================ "PRESO" E "PRESO PRA SEMPRE" SAO COISAS DIFERENTES ============================
	/// A exaustao **zera** o Ki (`f.Ki = 0`, literal do DM). Sem uma guarda no verb, quem religa o
	/// nado com o tanque um fio acima do corte exausta no MESMO decimo e perde de novo tudo o que a
	/// regeneracao devolveu -- medido, **0,3 tile por rodada, pra sempre**. Martelando a tecla ele
	/// nunca sai, e isso nao e o que o dono pediu: e uma armadilha com um botao que apaga o proprio
	/// progresso.
	///
	/// O que fecha e o <see cref="Nado.KiParaComecar"/> -- o corte MAIS
	/// <see cref="Nado.SegundosMinimosDeNado"/> segundos de custo. E o defeito injetado e o `if` que
	/// estava escrito antes dele existir: o corte cru.
	///
	/// ============================ O CICLO INTEIRO, PELOS FUNIS DE PRODUCAO ============================
	/// espera (`TickDaCarga`, que e onde o `RegenDeKi` tem chamador) -> verb (`AlternarNado`) ->
	/// nado (`MoveRules.Advance` + `TickDoNado`) -> exaustao -> espera de novo. Nada aqui e copia: a
	/// bancada so aperta a tecla e conta os pixels.
	/// ============================================================================================
	/// </summary>
	private void ORecarregarFazOCicloAndar(List<ServerPlayer> forjados, ZoneKey zona, (int Cx, int Cy) lago)
	{
		GD.Print("[preso] --- 2. recarregando o Ki ele volta a nadar E CONTINUA ---");

		ServerPlayer pl = ForjarPreso(forjados, 2, "bancada: o novato no oceano", zona,
									  NoCentroDaCelula(lago.Cx, lago.Cy), 100);
		RegrasDoPreso r = NovasRegras();
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);

		const float dt = 1f / 30f;
		const double TetoDaEspera = 180;   // segundos: acima disto o ciclo nao "anda", ele agoniza

		/// Uma rodada: espera o Ki, liga o nado, nada ate exaustar.
		(float Px, double Espera, double Nadou) UmaRodada(Vec2 rumo)
		{
			// ESPERA. O `TickDaCarga` e o funil da regeneracao (`RegenDeKi.Passo` so tem chamador ali).
			double esperou = 0;
			while (!pl.Nadando && esperou < TetoDaEspera)
			{
				TickDaCarga(pl, dt);
				esperou += dt;
				r.Verb(pl);   // martela a tecla, que e o que o jogador faz
			}
			if (!pl.Nadando) return (0f, esperou, 0);

			// NADA. O passo e o `Advance` de producao com o modo que o servidor concede.
			float px = 0f;
			double nadou = 0;
			int guarda = 0;
			while (pl.Nadando && guarda++ < 30 * 600)
			{
				Vec2 antes = pl.Pos;
				pl.Pos = MoveRules.Advance(pl.Pos, rumo, dt, pl.SpeedStat, mapa, out _,
										   false, ModoDeTravessiaDe(pl));
				px += (pl.Pos - antes).Length;
				r.TiqueDoNado(pl, dt);
				nadou += dt;
			}
			return (px, esperou, nadou);
		}

		void Reiniciar()
		{
			pl.Pos = NoCentroDaCelula(lago.Cx, lago.Cy);
			pl.Nadando = false;
			pl.NadoJaMolhou = false;
			pl.Ficha.Ki = 0;                       // o estado em que a exaustao deixa o corpo
			pl.Ficha.stamina = pl.Ficha.maxstamina;
			pl.Ficha.swimmastery = Nado.MaestriaInicial;
			pl.Facing = Facing.East;
			RecalcularVelocidade(pl);
		}

		// ============================ O CRITERIO E O **TEMPO DE NADO**, E ISSO CUSTOU UMA RODADA VERMELHA ============================
		// A primeira versao pedia OITO TILES por rodada, com o argumento de que o defeito anda 0,3 e a
		// regra certa anda ~10. Ela nasceu VERMELHA com o codigo certo, e o motivo e instrutivo: **a
		// distancia mede a VELOCIDADE do corpo, e o gate garante TEMPO.**
		//
		// O corpo forjado desta bancada tem `Epspeed = 1` -- o piso --, e o `Nado.FatorDePasso` cobra
		// exatamente ali o seu maximo (0,25 do passo, contra 0,64 de um `Epspeed = 2`, que e o "corpo
		// comum" do cabecalho daquela funcao). Os mesmos tres segundos de nado que rendem ~10 tiles pra
		// um personagem de verdade rendem ~2 pra ele. Nenhum dos dois numeros esta errado; o que
		// estava errado era a prova, que mediu o stat de velocidade e chamou de ciclo.
		//
		// O QUE O `Nado.KiParaComecar` PROMETE, LITERAL, sao <see cref="Nado.SegundosMinimosDeNado"/>
		// segundos de nado comprados por rodada -- e e isso que se cobra. Com o defeito injetado a
		// rodada nada **zero** segundos (o corpo exausta no mesmo decimo em que religa), entao a
		// separacao continua sendo entre tudo e nada. A distancia segue medida e RELATADA logo abaixo,
		// como informacao e nao como criterio.
		// ==========================================================================================================================
		float px1 = 0f; double espera1 = 0, nadou1 = 0;
		bool ARodadaAnda()
		{
			Reiniciar();
			(px1, espera1, nadou1) = UmaRodada(new Vec2(1, 0));
			return nadou1 >= Nado.SegundosMinimosDeNado * 0.9 && px1 > ZoneCollision.TileSize;
		}

		Mutacao(AfirmarPreso,
				$"uma rodada de espera+nado compra os {Nado.SegundosMinimosDeNado:0} s de nado que o gate promete",
				"o gate do verb voltou ao corte CRU da exaustao (sem os 3 s de custo)",
				ARodadaAnda,
				() => r.Verb = VerbComOCorteCru,
				() => r.Verb = AlternarNado);

		AfirmarPreso($"   ...a rodada medida: {espera1:0.0} s de espera compraram {nadou1:0.0} s de nado "
					 + $"= {px1 / ZoneCollision.TileSize:0.0} tiles "
					 + $"(MaxKi {pl.Ficha.MaxKi:0}, Epspeed {pl.Ficha.Epspeed:0.0}, "
					 + $"passo x{Nado.FatorDePasso(pl.Ficha.Epspeed):0.00})",
					 px1 > 0);
		AfirmarPreso("   ...e a espera e FINITA (nao e uma prisao com uma tecla inutil)",
					 espera1 < TetoDaEspera, $"{espera1:0.0} s");

		// ============================ E CADA CICLO GUARDA O TERRENO ANDADO ============================
		// E a diferenca entre "preso ate recarregar" e "preso": se a travessia nao acumulasse, tres
		// rodadas dariam a mesma distancia da primeira e o jogador ficaria oscilando no mesmo ponto do
		// oceano pra sempre -- com a bancada de cima VERDE, porque cada rodada isolada andou.
		// ==========================================================================================
		Reiniciar();
		Vec2 partida = pl.Pos;
		var acumulado = new List<float>();
		float total = 0f;
		for (int i = 0; i < 3; i++)
		{
			pl.Nadando = false;
			pl.NadoJaMolhou = false;
			(float px, _, _) = UmaRodada(new Vec2(1, 0));
			total += px;
			acumulado.Add(total);
		}
		bool cresce = acumulado[1] > acumulado[0] && acumulado[2] > acumulado[1];
		AfirmarPreso($"tres rodadas seguidas ACUMULAM terreno "
					 + $"({string.Join(" -> ", acumulado.Select(a => $"{a / 32:0.0}t"))})", cresce);

		// O CORPO ESTA MAIS LONGE DO QUE UMA RODADA O LEVARIA -- e a prova de que o ciclo COMPOE.
		// Sem ela, um mecanismo que devolvesse o corpo ao ponto de partida a cada exaustao (uma
		// "correcao" de posicao, por exemplo) passaria na acumulacao acima, que soma distancias.
		AfirmarPreso($"...e o corpo esta MAIS LONGE do que uma rodada sozinha o levaria "
					 + $"({(pl.Pos - partida).Length / 32:0.0} tiles x {px1 / 32:0.0} de uma rodada)",
					 (pl.Pos - partida).Length > px1 * 1.5f);

		// O FOLEGO NAO E O GARGALO -- a regeneracao cobra stamina, e se ela fosse o limite real o
		// numero de cima seria verdade so na primeira rodada de uma sessao.
		AfirmarPreso($"...e o folego nao e o gargalo ({pl.Ficha.stamina:0}/{pl.Ficha.maxstamina:0})",
					 pl.Ficha.stamina > pl.Ficha.maxstamina * 0.5);

		// ============================ E O VETERANO: O OUTRO EXTREMO DO TANQUE ============================
		// O corte da exaustao e RAZAO (`MaxKi * 0.01`) e o custo do nado tambem cresce com o `MaxKi`
		// (`Stats.dm:403`), entao os dois lados da conta andam juntos e nao ha garantia nenhuma de que
		// um tanque grande ajude. Sem esta linha, um gate que exigisse um valor ABSOLUTO passaria na
		// prova do novato e deixaria o veterano esperando minutos por rodada.
		//
		// O TANQUE VEM DO `baseKi` E NAO DO BP, e isso custou uma rodada vermelha: `MaxKi = baseKi *
		// KiMod * ...` (`Fighter.Statify.cs:122`) -- BP nao entra na conta. Forjado com BP 5.000.000 o
		// "veterano" saiu com o MESMO MaxKi 100 do novato, e a prova comparou o corpo com ele mesmo.
		// ============================================================================================
		ServerPlayer v = ForjarPreso(forjados, 3, "bancada: o veterano no oceano", zona,
									 NoCentroDaCelula(lago.Cx, lago.Cy), 5_000_000);
		v.Ficha.baseKi = 5_000;
		v.Ficha.Statify();
		RecalcularVelocidade(v);
		ServerPlayer guardado = pl;
		pl = v;
		Reiniciar();
		(float pxV, double esperaV, double nadouV) = UmaRodada(new Vec2(1, 0));
		pl = guardado;
		AfirmarPreso($"o veterano (MaxKi {v.Ficha.MaxKi:0}) espera MENOS que o novato "
					 + $"({esperaV:0.0} s x {espera1:0.0} s) e compra os mesmos "
					 + $"{nadouV:0.0} s de nado ({pxV / 32:0.0} tiles)",
					 esperaV <= espera1 && nadouV >= Nado.SegundosMinimosDeNado * 0.9
					 && pxV > ZoneCollision.TileSize);
	}

	/// <summary>
	/// O VERB COM O GATE ANTIGO -- o corte CRU da exaustao, que e literalmente o `if` que estava
	/// escrito antes de o <see cref="Nado.KiParaComecar"/> existir.
	///
	/// Ele nao reimplementa o verb: ele reproduz a CONDICAO antiga e deixa o `AlternarNado` de
	/// producao fazer todo o resto (o desligar do voo, o bit de molhado, o aviso, a velocidade). O
	/// truque do Ki cheio existe so pra furar o gate NOVO -- e o Ki e devolvido na linha seguinte,
	/// entao o que a rodada gasta continua sendo o tanque de verdade.
	/// </summary>
	private void VerbComOCorteCru(ServerPlayer pl)
	{
		if (pl.Nadando) { AlternarNado(pl); return; }
		if (pl.Ficha.Ki < Nado.KiQuePara(pl.Ficha.MaxKi)) return;

		double guardado = pl.Ficha.Ki;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		AlternarNado(pl);
		pl.Ficha.Ki = guardado;
	}

	// =====================================================================
	// 3. A MEDITACAO LEVA PRO QUARTO BRANCO -- o relato A
	// =====================================================================
	/// <summary>
	/// *"a MEDITACAO PROFUNDA ta me levando pra um LUGAR NADA A VER, era pra ser a DIMENSAO BRANCA
	/// assim como era no byond"*.
	///
	/// ============================ O DEFEITO ERA UMA ORDEM, E NAO UM DADO ============================
	/// A mente e `ZoneKey.Interior("Interdimension", id)` e o catalogo resolve zona pelo **NOME** --
	/// entao ela casava com a entrada `z24_Interdimension` do manifesto, o z24 REAL do BYOND: 500x500,
	/// meio mosaico azul-petroleo e meio nebulosa roxa. O nascimento (`250*32+16` em pixel cru) caia
	/// bem na emenda dos dois.
	///
	/// O conserto sao duas linhas: uma PLANTA no Core (`DimensaoMental.Planta`, o `build_mind_z` do
	/// DM celula por celula) e a planta **antes** do catalogo no `MapaDaZonaOuCatalogo`. Os dois
	/// defeitos injetados aqui sao exatamente esses dois passos desfeitos -- e o z24 continua no disco,
	/// entao a injecao reproduz o bug do dono e nao uma aproximacao dele.
	///
	/// ============================ E "BRANCA" TAMBEM E MEDIDO ============================
	/// Um quarto de 100x100 com a parede pintada de outra coisa passaria em tudo o que se mede por
	/// colisao. `turf/MindFloor` e `turf/MindWall` declaram os DOIS `icon = 'White.dmi'`
	/// (`MindMeditate.dm:27` e `:34`) -- o limite da mente e invisivel, so esbarra --, entao o pincel
	/// do chao e o da parede tem que ser o MESMO objeto.
	/// ==================================================================================
	/// </summary>
	private void AMeditacaoLevaProQuartoBranco(List<ServerPlayer> forjados)
	{
		GD.Print("[preso] --- 3. a meditacao leva pra DIMENSAO BRANCA ---");

		RegrasDoPreso r = NovasRegras();
		ZoneKey mente = DimensaoMental.De(IdBaseDoPreso + 10);

		// ---- O LUGAR: 100x100, e nao os 500x500 do z24.
		bool EOQuartoBranco()
		{
			ZoneCollision? m = r.Mapa(mente);
			return m != null && m.Width == DimensaoMental.Lado && m.Height == DimensaoMental.Lado;
		}

		Mutacao(AfirmarPreso,
				$"a mente resolve pra planta de {DimensaoMental.Lado}x{DimensaoMental.Lado} (e nao pro z24 do BYOND)",
				"a planta saiu do funil: o catalogo volta a resolver \"Interdimension\" pelo NOME",
				EOQuartoBranco,
				() => r.Mapa = z => _catalogo?.Get(z)?.Mapa,
				() => r.Mapa = MapaDaZonaOuCatalogo);

		ZoneCollision? mapa = r.Mapa(mente);
		AfirmarPreso("...e o z24 do BYOND ainda EXISTE no catalogo (senao a injecao acima nao provaria nada)",
					 _catalogo?.Get(DimensaoMental.Zona)?.Mapa is { } z24
					 && (z24.Width != DimensaoMental.Lado || z24.Height != DimensaoMental.Lado),
					 $"{_catalogo?.Get(DimensaoMental.Zona)?.Mapa?.Width ?? -1}");

		// ---- O NASCIMENTO: `locate(cx-3, cy)`, e nao um numero escrito a mao.
		bool NasceNoLugarDoDm()
		{
			Vec2 p = r.Nascimento();
			int cx = (int)MathF.Floor(p.X / ZoneCollision.TileSize);
			int cy = (int)MathF.Floor((p.Y + MoveRules.FeetOffsetY) / ZoneCollision.TileSize);
			return cx == DimensaoMental.CelDeQuemMedita.X && cy == DimensaoMental.CelDeQuemMedita.Y;
		}

		Mutacao(AfirmarPreso,
				$"quem medita nasce na celula ({DimensaoMental.CelDeQuemMedita.X},"
				+ $"{DimensaoMental.CelDeQuemMedita.Y}) -- o `locate(cx-3, cy)` do DM",
				"o nascimento voltou ao literal (250,250) em pixel cru -- o meio do z24",
				NasceNoLugarDoDm,
				() => r.Nascimento = () => new Vec2(250 * 32 + 16, 250 * 32 + 16),
				() => r.Nascimento = () => PosMental);

		// AS TRES CELULAS NAO SAO ENFEITE: o oponente nasce a `DistanciaDoOponente` a leste e tem que
		// cair no CENTRO do quarto. Um nascimento "perto o bastante" quebraria isso em silencio.
		Vec2 oponente = r.Nascimento() + new Vec2(DistanciaDoOponente, 0);
		AfirmarPreso($"...e o oponente, a {DistanciaDoOponente:0} px a leste, cai no centro exato "
					 + $"({DimensaoMental.Lado / 2},{DimensaoMental.Lado / 2})",
					 (int)MathF.Floor(oponente.X / 32) == DimensaoMental.Lado / 2
					 && (int)MathF.Floor((oponente.Y + MoveRules.FeetOffsetY) / 32) == DimensaoMental.Lado / 2);

		// ============================ E ELE ANDA E **NAO** BATE EM NADA ============================
		// Esta prova ja existiu ao contrario, e a virada e o pedido do dono: *"faca tb o MAPA DA MENTE
		// ser INFINITO SEM BORDAS [...] pq as BORDAS PRETAS sao estranhas e as vezes o NPC VOA PRA FORA
		// e ele TELEPORTA DE VOLTA"*. Ela dizia "ele PARA em todos os rumos -- e um quarto fechado,
		// como no DM"; hoje ela exige o oposto, com o MESMO numero de corte, e por isso o texto guarda
		// os dois: quem ler o log daqui a um ano precisa saber que a mudanca foi escolhida.
		//
		// Sessenta segundos em cada rumo -- ~300 tiles, tres vezes o lado da chapa. O corte continua
		// sendo "meia chapa + 2": era o limite que o quarto impunha, e agora e o piso que prova que ele
		// nao existe mais.
		//
		// ELA LE `r.Planta().Colisao` A CADA VOLTA (e nao o `mapa` de cima) de proposito: e o que
		// permite injetar o quarto de volta e ver o corpo parar. Com a colisao capturada uma vez, o
		// defeito injetado nao chegaria ao `MoveRules` e a prova ficaria verde com a parede no ar.
		ServerPlayer andarilho = ForjarPreso(forjados, 10, "bancada: quem medita", mente, r.Nascimento(), 1_000);

		float LongeEmTodoRumo()
		{
			ZoneCollision c = r.Planta().Colisao;
			float pior = float.MaxValue;
			foreach (Vec2 rumo in (Vec2[])[new(1, 0), new(-1, 0), new(0, 1), new(0, -1)])
			{
				Vec2 pos = r.Nascimento();
				for (int i = 0; i < 60 * 60; i++)
					pos = MoveRules.Advance(pos, rumo, 1f / 60, andarilho.SpeedStat, c, out _);
				pior = Math.Min(pior, (pos - r.Nascimento()).Length / ZoneCollision.TileSize);
			}
			return pior;
		}

		Mutacao(AfirmarPreso,
				$"andando 60 s ele NAO PARA em rumo nenhum (> {DimensaoMental.Lado / 2f + 2:0} tiles "
				+ "em todos os quatro) -- a mente nao acaba",
				"a planta ganhou o anel de `MindWall` de volta (o quarto de 100x100 que o dono derrubou)",
				() => LongeEmTodoRumo() > DimensaoMental.Lado / 2f + 2,
				() => r.Planta = () => ComParedeDeVolta(DimensaoMental.Planta()),
				() => r.Planta = DimensaoMental.Planta);

		// ---- E O CHAO E BRANCO: um pincel so, e ele e o mesmo que pinta o infinito.
		//
		// O criterio continua comparando `Planicie` com `Montanha` mesmo sem parede nenhuma, e nao e
		// resto: as DEZ entradas da paleta apontam pro mesmo branco (ver `DimensaoMental.PaletaDaMente`)
		// justamente pra que o pintor nunca tenha um pincel vazio na mao, e a `Montanha` e a que o
		// defeito injetado consegue sujar sem mexer no resto.
		bool UmPincelSo()
		{
			TerrenoGerado t = r.Planta();
			return t.Paleta.Planicie == t.Paleta.Montanha
				&& t.Paleta.Planicie.Atlas.Length > 0;
		}

		Mutacao(AfirmarPreso,
				"a paleta inteira e o MESMO branco (o limite da mente e invisivel -- `MindMeditate.dm:27,34`)",
				"uma classe de terreno ganhou pincel proprio (o branco deixa de ser um so)",
				UmPincelSo,
				() => r.Planta = () => ComParedeDeOutraCor(DimensaoMental.Planta()),
				() => r.Planta = DimensaoMental.Planta);

		// ============================ NAO HA UMA CELULA DENSA, E O `SemBorda` ESTA LIGADO ============================
		// AS DUAS COISAS NA MESMA PROVA, e isso e deliberado: elas sao **metades de uma borda so** e uma
		// sem a outra nao entrega nada. Sem o anel mas sem o `SemBorda`, o corpo atravessaria as 100
		// celulas da chapa e bateria numa parede invisivel uma celula depois -- o fim do bitset, que e
		// solido em todo mapa do jogo. Foi exatamente essa a armadilha da leitura desta fase, e uma
		// prova que cobrasse so o anel ficaria verde com o jogador preso do mesmo jeito.
		// ========================================================================================================
		bool NadaDensoESemBeirada()
		{
			ZoneCollision c = r.Planta().Colisao;
			for (int y = 0; y < c.Height; y++)
				for (int x = 0; x < c.Width; x++)
					if (c.BlockedCell(x, y)) return false;

			// E FORA DA CHAPA TAMBEM E CHAO -- a metade que o anel sozinho nao cobre.
			return c.SemBorda
				&& !c.BlockedCell(-1, c.Height / 2)
				&& !c.BlockedCell(c.Width + 500, c.Height / 2)
				&& !c.NaBorda(0, 0);
		}

		Mutacao(AfirmarPreso,
				"nenhuma celula e densa E a colisao diz SEM BEIRADA (fora do bitset tambem e chao)",
				"a planta voltou a ser o quarto fechado: anel de parede e `SemBorda` desligado",
				NadaDensoESemBeirada,
				() => r.Planta = () => ComParedeDeVolta(DimensaoMental.Planta()),
				() => r.Planta = DimensaoMental.Planta);

		// ---- E O QUE **ESCONDE** CONCORDA COM O QUE BLOQUEIA sobre onde o mundo acaba.
		//
		// Sem esta linha o defeito nao seria parede: seria ESCURIDAO. O olho e a voz correm o DDA sobre
		// o `QueEsconde`, um segundo mapa derivado do mesmo terreno -- e se ele achasse que o mundo
		// acaba na chapa, todo raio que saisse dela bateria na "borda do mundo" e a tela fecharia dez
		// tiles depois da origem, com o chao branco desenhado normalmente por baixo.
		AfirmarPreso("o mapa do que ESCONDE herda a falta de beirada (senao o branco vira escuridao "
					 + "a dez tiles da origem)",
					 r.Planta().QueEsconde.SemBorda
					 && !r.Planta().QueEsconde.BlockedCell(-1, DimensaoMental.Lado / 2));

		// ============================ E A LUZ -- E ESTA LINHA FOI ESCRITA POR UMA FOTO ============================
		// Todas as provas acima ja estavam VERDES quando a foto saiu MALVA (`R0,38 G0,26 B0,30`, zero
		// pixel quase-branco, o HUD escrito "poente"). O quarto estava certo e a luz estava errada: a
		// mente e um `KindInterior`, e interior neste port cai no `Ceu.SemCeu` -- *"crepusculo
		// parado"* --, que multiplicado por chao branco da malva.
		//
		// **NENHUMA MEDIDA DE GEOMETRIA PEGA ISSO**, e por isso a regra virou campo de Core
		// (`RelogioDoPlaneta.LuzPropria`) em vez de um ajuste no desenho do cliente: aqui da pra
		// cobrar, e o defeito injetado e a linha exata que existia antes.
		//
		// O CONTRA-EXEMPLO E OBRIGATORIO: "luz propria" satisfeito por TODO lugar seria o jogo inteiro
		// sem dia e sem noite, com esta prova verde.
		// ======================================================================================================
		bool SoAMenteTemLuzPropria()
		{
			ZoneKey terra = ZoneKey.Premade("Earth");
			ZoneKey sala = ZoneKey.Premade(SalaDoTempo.Zona);
			return r.Relogio(mente).LuzPropria
				&& !r.Relogio(terra).LuzPropria
				&& !r.Relogio(sala).LuzPropria;
		}

		Mutacao(AfirmarPreso,
				"a mente tem LUZ PROPRIA (e a Terra e a Sala do Tempo NAO) -- sem isso o branco sai malva",
				"a mente voltou a cair no interior generico (`Ceu.SemCeu`: crepusculo parado)",
				SoAMenteTemLuzPropria,
				() => r.Relogio = z => DimensaoMental.EhAMente(z) ? Ceu.SemCeu : RelogioDaZona(z),
				() => r.Relogio = RelogioDaZona);

		AfirmarPreso("...e a mente nao tem dia, nem noite, nem lua (nao ha ceu la dentro)",
					 !r.Relogio(mente).TemDia && !r.Relogio(mente).TemNoite
					 && r.Relogio(mente).Lua.Tamanho <= 0,
					 $"dia={r.Relogio(mente).TemDia} noite={r.Relogio(mente).TemNoite}");

		// ---- E NAO HA AGUA NA MENTE (o assunto das familias 1 e 2 nao existe aqui dentro).
		AfirmarPreso("nao ha agua na dimensao mental (nao ha lago pra o nado descobrir la dentro)",
					 !r.Planta().Colisao.TemAgua);

		// ---- A PLANTA E UMA SO, E O QUE SEPARA AS MENTES E A CHAVE.
		AfirmarPreso("duas mentes diferentes compartilham a MESMA planta (o bolso e a `ZoneKey`)",
					 ReferenceEquals(MapaDaMente(DimensaoMental.De(1)), MapaDaMente(DimensaoMental.De(2)))
					 && DimensaoMental.De(1).Hash != DimensaoMental.De(2).Hash);

		SemParedeQuemSeguraEAColeira(forjados, mente);
	}

	// =====================================================================
	// 3b. SEM PAREDE, QUEM SEGURA O REFLEXO
	// =====================================================================
	/// <summary>
	/// ============================ O BURACO QUE DERRUBAR A PAREDE ABRIU ============================
	/// A familia acima prova que a mente nao acaba. Esta prova o PRECO disso, e ele nao aparece em
	/// desenho nenhum -- aparece na aritmetica:
	///
	///   * o corpo que a mente ergue persegue **sem raio e sem desistencia** (`TicarUmCorpo`, o ramo
	///     `DonoDoClone != 0`: `presa = dono; destino = dono.Pos;`);
	///   * mas ele copia a SUA ficha (`EspelharODono`), e portanto a sua velocidade.
	///
	/// **Perseguidor com a mesma velocidade nunca alcanca quem foge.** No quarto de 100 tiles isso
	/// nunca importou: a parede devolvia voce em tres segundos. Num plano sem fim, quem nao quiser
	/// lutar foge pra sempre, e a sessao mental deixa de ter saida por VITORIA -- sobram o verb de
	/// sair, o nocaute do corpo la fora e o golpe no corpo real. **Um combate que nao fecha e pior que
	/// a borda feia que o dono mandou tirar**, e nada na tela diria o que aconteceu.
	///
	/// ============================ POR QUE SAO TRES PROVAS E NAO UMA ============================
	/// A ponta a ponta de verdade seria rodar o `TicarUmCorpo` inteiro, que arrasta a arvore de decisao,
	/// o contra-feixe e o ataque -- e o que ela mediria, no fim, sao as duas metades abaixo mais um
	/// `if`. Entao elas sao medidas separadas e DECLARADAS separadas: a REGRA (quando a coleira
	/// dispara), o EFEITO (a producao `ReaparecerNaFrente`, chamada de verdade) e a FOLGA (que a
	/// coleira nao morde no meio de uma luta normal). A terceira e a que pega o erro provavel: um raio
	/// apertado poria o reflexo saltando dentro do combate.
	/// ======================================================================================
	/// </summary>
	private void SemParedeQuemSeguraEAColeira(List<ServerPlayer> forjados, ZoneKey mente)
	{
		GD.Print("[preso] --- 3b. sem parede, quem segura o reflexo ---");

		Vec2 origem = DimensaoMental.PixelDe(DimensaoMental.CelDeQuemMedita);
		ServerPlayer dono = ForjarPreso(forjados, 20, "bancada: quem sonha", mente, origem, 1_000);
		ServerPlayer reflexo = ForjarPreso(forjados, 21, "bancada: o reflexo", mente,
										   origem + new Vec2(DistanciaDoOponente, 0), 1_000);
		dono.Facing = Facing.East;
		reflexo.DonoDoClone = dono.Id;

		// ---- 1. A REGRA: ela cala no combate e morde na fuga.
		float longe = DimensaoMental.RaioDaColeira + 1;
		AfirmarPreso($"a coleira NAO dispara na distancia de combate ({DistanciaDoOponente:0} px)",
					 !DimensaoMental.FugiuDoDono(reflexo.Pos, dono.Pos));
		AfirmarPreso($"...e DISPARA passando de {DimensaoMental.RaioDaColeira:0} px "
					 + $"({DimensaoMental.RaioDaColeira / ZoneCollision.TileSize:0} tiles)",
					 DimensaoMental.FugiuDoDono(dono.Pos + new Vec2(longe, 0), dono.Pos));

		// ---- 2. O EFEITO: a producao poe o corpo NA FRENTE, a distancia de sempre.
		//
		// NA FRENTE E NAO ATRAS e a metade que fecha o combate: a `Facing` do dono e o rumo da fuga, e
		// reaparecer nas costas dele seria a perseguicao recomecando do zero -- a coleira dispararia de
		// novo daqui a quarenta tiles, pra sempre.
		reflexo.Pos = dono.Pos - new Vec2(longe, 0);   // ele ficou pra tras, a oeste
		ReaparecerNaFrente(reflexo, dono);

		float depois = (reflexo.Pos - dono.Pos).Length;
		AfirmarPreso($"o reflexo que ficou a {longe / ZoneCollision.TileSize:0} tiles reaparece a "
					 + $"{DistanciaDoOponente:0} px -- a MESMA distancia da entrada na mente",
					 Math.Abs(depois - DistanciaDoOponente) < 0.5f, $"{depois:0.0}");
		AfirmarPreso("...e a LESTE do dono, que e pra onde ele estava olhando (na frente, cortando o "
					 + "caminho -- nao nas costas)",
					 reflexo.Pos.X > dono.Pos.X && Math.Abs(reflexo.Pos.Y - dono.Pos.Y) < 0.5f);

		dono.Facing = Facing.North;
		reflexo.Pos = dono.Pos + new Vec2(0, longe);
		ReaparecerNaFrente(reflexo, dono);
		AfirmarPreso("...e a frente ACOMPANHA o olhar (virado pro norte, ele reaparece ao norte)",
					 reflexo.Pos.Y < dono.Pos.Y && Math.Abs(reflexo.Pos.X - dono.Pos.X) < 0.5f);

		// ---- 3. A FOLGA: a coleira nao pode morder no meio de uma luta.
		//
		// O ERRO PROVAVEL AQUI E O RAIO APERTADO, e ele nao daria erro nenhum: daria um reflexo
		// saltando pra dentro do combate cada vez que a briga se espalhasse um pouco. O alcance mais
		// longo que uma luta usa e a rajada, e a coleira tem que estar CONFORTAVELMENTE acima dele.
		//
		// O NUMERO SAI DO PROPRIO `Projetil` (o `range` padrao, 20 tiles) e nao de uma constante
		// escrita aqui -- se alguem dobrar o alcance das rajadas, e ESTA prova que tem que reclamar.
		// Uma tecnica customizada pode passar disso (a Onda de Ki vai a 30 tiles), e o fator 1,5 e o
		// que da conta das duas: 40 tiles de coleira contra 30 de rajada mais longa ainda sobra.
		float rajada = (float)(new Jandirus.Core.Combat.ReceitaDeProjetil().AlcanceTiles * ZoneCollision.TileSize);
		AfirmarPreso($"o raio da coleira ({DimensaoMental.RaioDaColeira:0} px) e folgado sobre o "
					 + $"alcance da rajada ({rajada:0} px) -- ela nao morde no meio da luta",
					 DimensaoMental.RaioDaColeira > rajada * 1.5f,
					 $"folga {DimensaoMental.RaioDaColeira / Math.Max(rajada, 1):0.00}x");
	}

	/// <summary>A planta com a parede pintada de outra coisa -- o defeito "deixou de ser branca".</summary>
	private static TerrenoGerado ComParedeDeOutraCor(TerrenoGerado t)
	{
		var paleta = new PaletaDeBioma
		{
			Bioma = t.Paleta.Bioma,
			LimiarAgua = t.Paleta.LimiarAgua, LimiarPraia = t.Paleta.LimiarPraia,
			LimiarColina = t.Paleta.LimiarColina, LimiarMontanha = t.Paleta.LimiarMontanha,
			VegetacaoPct = t.Paleta.VegetacaoPct, PlantaPct = t.Paleta.PlantaPct,
			MinerioPct = t.Paleta.MinerioPct,
			Agua = t.Paleta.Agua, Praia = t.Paleta.Praia, Planicie = t.Paleta.Planicie,
			Colina = t.Paleta.Colina,
			Montanha = new TileVisual("Turfs 1", "rock"),   // <- a parede deixa de ser branca
			Arvore = t.Paleta.Arvore, ArvoreAcento = t.Paleta.ArvoreAcento,
			Planta = t.Paleta.Planta, Minerio = t.Paleta.Minerio, Gema = t.Paleta.Gema,
		};
		return Copiar(t, paleta, t.BytesDeColisao);
	}

	/// <summary>
	/// A PLANTA COMO ELA ERA ATE ONTEM: o quarto de 100x100 com o anel de `MindWall` e sem o
	/// `SemBorda` -- o defeito "a mente voltou a ser uma sala".
	///
	/// ============================ E O DEFEITO E O CODIGO LITERAL QUE FOI DELETADO ============================
	/// O anel e o `if(xx==1 || yy==1 || xx==sz || yy==sz) new/turf/MindWall` (`MindMeditate.dm:76-79`)
	/// que o `DimensaoMental.Montar` escrevia palavra por palavra, e o `SemBorda` desligado e o valor
	/// padrao que a colisao tinha antes da linha nova. Nao ha aproximacao nenhuma aqui: injetar isto e
	/// voltar no tempo, e e por isso que a prova que fica vermelha com ele prova alguma coisa.
	///
	/// **AS DUAS METADES JUNTAS**, porque a borda e uma so: um defeito que so pusesse o anel deixaria
	/// a prova do `SemBorda` verde, e um que so desligasse o bit deixaria a do anel verde. Ver o
	/// comentario da familia.
	/// ====================================================================================================
	/// </summary>
	private static TerrenoGerado ComParedeDeVolta(TerrenoGerado t)
	{
		int n = t.Largura;
		var bits = new byte[(n * n + 7) / 8];
		var chao = (byte[])t.Chao.Clone();

		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				if (x != 0 && y != 0 && x != n - 1 && y != n - 1) continue;
				int i = y * n + x;
				chao[i] = (byte)ClasseDeTerreno.Montanha;
				bits[i >> 3] |= (byte)(1 << (i & 7));
			}

		TerrenoGerado velho = Copiar(t, t.Paleta, GeradorDeTerreno.MontarJcol(n, n, bits), chao);
		velho.Colisao.SemBorda = false;   // e o valor de antes: fora do bitset volta a ser parede
		return velho;
	}

	/// <summary>
	/// A MESMA PLANTA COM UMA COISA TROCADA -- a base dos dois defeitos desta familia.
	///
	/// ELA CARREGA O `SemBorda` DO ORIGINAL, e isso importa: sem a linha, o defeito "o branco deixou de
	/// ser um so" tambem apagaria o infinito, e a prova que ficasse vermelha nao diria qual das duas
	/// coisas quebrou. Um defeito injetado tem que quebrar UMA coisa. Quem quer o quarto de volta pede
	/// por ele em <see cref="ComParedeDeVolta"/>.
	/// </summary>
	private static TerrenoGerado Copiar(TerrenoGerado t, PaletaDeBioma paleta, byte[] jcol,
										byte[]? chao = null)
	{
		ZoneCollision colisao = ZoneCollision.Load(jcol)!;
		colisao.SemBorda = t.Colisao.SemBorda;

		return new TerrenoGerado
		{
			Largura = t.Largura,
			Altura = t.Altura,
			Bioma = t.Bioma,
			Seed = t.Seed,
			Paleta = paleta,
			Chao = chao ?? t.Chao,
			Cobertura = t.Cobertura,
			BytesDeColisao = jcol,
			BytesDeAgua = t.BytesDeAgua,
			Colisao = colisao,
			SpawnCelX = t.SpawnCelX,
			SpawnCelY = t.SpawnCelY,
			ClareiraEscavada = t.ClareiraEscavada,
		};
	}

	/// <summary>
	/// O FONTE DE UM ARQUIVO DO SERVIDOR -- pra prova estrutural do `LevarProSeco`.
	///
	/// Devolve string VAZIA quando nao acha, e a prova que o usa fica verde nesse caso: o que ela
	/// afirma e "este texto NAO esta la", e um arquivo ausente satisfaz isso sem mentir. O aviso sai
	/// no console pra a ausencia nao passar em silencio.
	/// </summary>
	private static string FonteDoServidor(string arquivo)
	{
		string caminho = $"res://Server/{arquivo}";
		if (Godot.FileAccess.FileExists(caminho)) return Godot.FileAccess.GetFileAsString(caminho);
		GD.PushWarning($"[preso] fonte '{arquivo}' nao encontrado -- a prova estrutural nao mediu nada");
		return "";
	}
}
