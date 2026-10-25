using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO RASTEJO (`--rastejoteste`) -- quem cai ou fica preso pela gravidade se arrasta a 1/4 (dono, 2026-10-23). A regra e as
/// escolhas estao em `GameServer.Rastejo.cs`. Corpos forjados no boot, numa praca de chao comum da Terra com uma camara de gravidade
/// assentada pelo funil de producao; o passo vai pelo `AplicarInput` de producao, pacote a pacote como o cliente honesto (o passo que o
/// `SpeedStat` da ficha da), e o desmaio pelo `TickDoEsmagamento` de producao.
///
///     Godot --headless -- --server --port 10850 --rastejoteste
///
/// Cada afirmacao principal comeca com um CODIGO ("P1.", "D4."...): e por ele que a contraprova por mutacao (o `ciclo.py` do agente
/// `arrastar`) sabe onde cada linha tirada reprova. As familias:
///   P. PRESO DE PE (razao 8): anda, a 1/4 do passo sem a gravidade -- e nao o freio de antes (1/8), nem os dois juntos (1/32).
///   C. SO O PASSO: nao corre, nao voa (no ar, preso como antes), nao agarra, e quem ja segurava alguem nao se arrasta ate soltar.
///   D. DESMAIADO PELA GRAVIDADE (razao 2): o desmaio fica marcado, o jogo avisa, a queda e a de sempre, ele se arrasta a 1/4 e nao vira.
///   M. O MAJIN DESFEITO EM PEDACOS nao se arrasta.
///   F. SAI DO CAMPO SE ARRASTANDO, e la fora continua se arrastando ate levantar; de pe, o passo volta ao de sempre.
///   S. O DESMAIADO POR OUTRA COISA CONTINUA PARADO (fora e dentro do campo), e o desmaio novo nao herda a marca do anterior.
///   I. A IA CONTINUA PRESA (decisao minha).
/// Os defeitos injetados -- `RastejoDesligadoDeTeste`, `Esmagamento.RastejoComOFreioDeTeste`, `CombatState.DesmaioHerdaAMarcaDeTeste`,
/// `RastejoCorreDeTeste` e `DeitadoViraNoRastejoDeTeste` -- reprovam as reguas que dependem deles ("...c." no codigo).
///
/// O RELOGIO NAO ANDA: a bancada roda dentro do boot, e cada tique de que ela precisa e chamado pela porta dele.
/// </summary>
public sealed partial class GameServer
{
	private int _rjOk, _rjFalhou;

	private void AfirmarRj(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _rjOk++; GD.Print($"[rastejo]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _rjFalhou++; GD.PrintErr($"[rastejo]   FALHA {nome}   [{detalhe}]"); }
	}

	/// <summary>A praca da bancada: a camara, o ponto de partida dentro do campo dela e um ponto fora.</summary>
	private sealed class PalcoDoRastejo
	{
		public required Obra Maquina;
		public required GravidadeDaObra Campo;
		/// <summary>Um tile a leste da maquina: dentro do campo de alcance 2 (a caixa vai a 48 px do centro dela).</summary>
		public required Vec2 NoCampo;
		/// <summary>Seis tiles a leste da maquina: fora do campo.</summary>
		public required Vec2 ForaDoCampo;
		public required ServerPlayer Dono;
	}

	public void RodarBancadaDoRastejo()
	{
		_rjOk = _rjFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[rastejo] ================ QUEM CAI OU FICA PRESO PELA GRAVIDADE SE ARRASTA A 1/4 (dono, 2026-10-23) ================");

		ZoneCollision? mapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		int obrasAntes = _noChao.Count;
		try
		{
			AfirmarRj("(montagem) a Terra tem mapa e o catalogo de obras carregou", mapa != null && _obras != null);
			if (mapa == null || _obras == null) return;

			(int px, int py) = PracaDaBancada(mapa);
			const int T = ZoneCollision.TileSize;
			Vec2 No(int cx, int cy) => new(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY);
			(int mx, int my) = (px + 2, py + 3);

			ServerPlayer dono = ForjarRj("rj: o dono", No(mx + 1, my + 1));
			Obra? maq = AssentarDeTeste(dono, "Gravity", mx, my);
			// (o estado de gravidade nasce no primeiro `grav_info`, como na `--maquinasteste`)
			if (maq != null) ComandoDeInteracao(dono, "grav_info", "");
			AfirmarRj("(montagem) a camara de gravidade assentou pelo funil de producao", maq?.Gravidade != null);
			if (maq?.Gravidade is not { } g) return;
			dono.Pos = No(px + 9, py + 1);   // longe do campo: quem assentou nao e corpo da prova

			var p = new PalcoDoRastejo { Maquina = maq, Campo = g, NoCampo = No(mx + 1, my), ForaDoCampo = No(mx + 6, my), Dono = dono };
			PresoDePeAnda(p);
			LimparTudoDaBancada(manter: [dono]);
			ODesmaiadoPelaGravidadeSeArrasta(p);
			LimparTudoDaBancada(manter: [dono]);
			ODesmaiadoPorOutraCoisaFicaParado(p);
			LimparTudoDaBancada(manter: [dono]);
			AIaContinuaPresa(p);
		}
		catch (Exception e)
		{
			AfirmarRj("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			RastejoDesligadoDeTeste = RastejoCorreDeTeste = DeitadoViraNoRastejoDeTeste = false;
			Esmagamento.RastejoComOFreioDeTeste = false;
			CombatState.DesmaioHerdaAMarcaDeTeste = false;
			if (_noChao.Count > obrasAntes) _noChao.RemoveRange(obrasAntes, _noChao.Count - obrasAntes);
			GravarMundo();
			LimparTudoDaBancada();
			LerOsCampos();
			GD.Print($"[rastejo] ================ {_rjOk} OK, {_rjFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// AS PECAS DA BANCADA
	// =====================================================================
	/// <summary>Um corpo de bancada (o `Forjar` da `--projetilteste`: Humano, maestria de gravidade 1, de pe), ja com o campo e o passo lidos.</summary>
	private ServerPlayer ForjarRj(string nome, Vec2 onde)
	{
		ServerPlayer c = Forjar(nome, onde, 1_000);
		c.Conta = "bancada_rastejo";
		c.Facing = Facing.South;
		c.LastInputMs = NowMs();
		LerOsCampos();
		RecalcularVelocidade(c);
		return c;
	}

	/// <summary>A camara ligada em `grav` (alcance 2), e o campo relido -- `grav` 0 = desligada.</summary>
	private void LigarRj(PalcoDoRastejo p, double grav)
	{
		GravidadeDaObra g = p.Campo;
		g.Range = 2;
		g.Energia = Math.Max(g.Energia, 1);
		g.EnergiaMax = Math.Max(g.EnergiaMax, 1);
		g.Grav = grav;
		g.MudaEm = 0;
		g.Pedida = 0;
		g.Cheias = null;
		LerOsCampos();
	}

	/// <summary>
	/// A TECLA SEGURADA, pacote a pacote como o cliente honesto: cada pacote afirma o passo que o `SpeedStat` da ficha da (a previsao do
	/// cliente) num tique de 1/30 s, com o olhar do rumo (ou o `olhar` dado). Devolve quanto o corpo andou no servidor, quanto o cliente
	/// PEDIU (a soma dos passos afirmados) e quantas correcoes isso custou.
	/// </summary>
	private (float Andou, float Pedido, int Correcoes) SegurarATeclaRj(ServerPlayer pl, Vec2 rumo, int pacotes, bool correndo = false,
		Facing? olhar = null)
	{
		Vec2 antes = pl.Pos;
		int correcoes = pl.Corrections;
		float pedido = 0;
		for (int i = 0; i < pacotes; i++)
		{
			float passo = MoveRules.SpeedPx(pl.SpeedStat, correndo) * Protocol.TickSeconds;
			pedido += passo;
			pl.LastInputMs = NowMs() - (long)Math.Round(Protocol.TickSeconds * 1000);
			Facing f = olhar ?? MoveRules.FacingFrom(rumo, pl.Facing);
			byte flags = (byte)((byte)f | Protocol.InputAndando | (correndo ? Protocol.InputCorrendo : 0));
			AplicarInput(pl, pl.SeqInput + 1, (uint)RelogioDeQuadrosMs(), pl.Pos + rumo.Normalized() * passo, flags);
		}
		return ((pl.Pos - antes).Length, pedido, pl.Corrections - correcoes);
	}

	/// <summary>
	/// O CORPO ANDOU O QUE O CLIENTE PEDIU (o passo aceito inteiro) -- medido contra o pedido, e nao contra um numero fixo de pixels: o passo
	/// de quem se arrasta e pequeno por desenho, e a r1 viu uma regua de "1 px" cair so porque o mutante do freio (1/32) andava devagar.
	/// </summary>
	private static bool AndouOPedidoRj(float andou, float pedido) => pedido > 0 && andou > 0.9f * pedido;

	/// <summary>
	/// O CORPO NA BEIRA DO DESMAIO: os nucleos (cabeca, torso, abdomen) meio golpe de esmagamento acima da linha do nocaute -- o proximo
	/// tique de 1 Hz da gravidade derruba. Os membros ficam inteiros.
	/// </summary>
	private static void NaBeiraDoDesmaioRj(ServerPlayer pl)
	{
		double meioGolpe = Esmagamento.DanoPorSegundo(pl.Ficha) / 2;
		foreach (BodyPart parte in pl.Combate.Corpo.Partes)
			if (!parte.Decepado && parte.Papel == Vitalidade.Nucleo) parte.Vida = parte.VidaMax * Regras.LimiarQuebra + meioGolpe;
		pl.Combate.SincronizarVida();
	}

	/// <summary>Um corpo novo dentro do campo de razao 2, derrubado pelo tique de producao da gravidade. Devolve o que o servidor disse.</summary>
	private ServerPlayer DesmaiarPelaGravidadeRj(PalcoDoRastejo p, string nome, out List<string> falas)
	{
		ServerPlayer d = ForjarRj(nome, p.NoCampo);
		LigarRj(p, 1);   // 1 do chao + 1 da maquina, contra a maestria 1: razao 2 -- esmaga, nao prende
		RecalcularVelocidade(d);
		NaBeiraDoDesmaioRj(d);
		falas = Ouvir(() => TickDoEsmagamento());
		return d;
	}

	/// <summary>Levanta o corpo pela porta de sempre (o nucleo sarado acima da linha, sem o piso do desmaio, e o tique do combate).</summary>
	private static void LevantarRj(ServerPlayer pl)
	{
		foreach (BodyPart parte in pl.Combate.Corpo.Partes)
			if (!parte.Decepado) parte.Vida = parte.VidaMax * 0.6;
		pl.Combate.SincronizarVida();
		pl.Combate.DesmaioMinimo = 0;
		pl.Combate.Tick(0.1);
	}

	/// <summary>O passo do corpo SEM a gravidade (a camara em 0) e COM ela (em `grav`), pelo `RecalcularVelocidade` de producao.</summary>
	private (float Sem, float Com) PassosRj(PalcoDoRastejo p, ServerPlayer pl, double grav)
	{
		LigarRj(p, 0);
		RecalcularVelocidade(pl);
		float sem = pl.SpeedStat;
		LigarRj(p, grav);
		RecalcularVelocidade(pl);
		return (sem, pl.SpeedStat);
	}

	/// <summary>
	/// O passo do DESMAIADO: a referencia e o mesmo corpo, com a mesma vida, andando sem gravidade (a camara em 0 e o desmaio sem a marca
	/// -- o passo que ele daria se andasse); o outro, com a marca DE VERDADE de volta (a que o tique de producao pos, ou nao pos) e a camara
	/// em `grav`. A r1 escrevia `true` no lugar dela, e a bancada inteira dali pra baixo passava com a marca posta por ela mesma -- o mutante
	/// sem a marca (b4) passou no F1 assim.
	/// </summary>
	private (float Sem, float Com) PassosDoDesmaiadoRj(PalcoDoRastejo p, ServerPlayer pl, double grav)
	{
		CombatState c = pl.Combate;
		bool marca = c.DesmaioDaGravidade;
		LigarRj(p, 0);
		c.DesmaioDaGravidade = false;
		RecalcularVelocidade(pl);
		float sem = pl.SpeedStat;
		c.DesmaioDaGravidade = marca;
		LigarRj(p, grav);
		RecalcularVelocidade(pl);
		return (sem, pl.SpeedStat);
	}

	/// <summary>Tira um corpo da bancada (o `LimparTudoDaBancada`, so pra ele).</summary>
	private void TirarRj(ServerPlayer pl)
	{
		EsquecerTecnicas(pl.Id);
		_players.Remove(pl.Id);
		ZoneList(pl.Zone.Hash).Remove(pl);
	}

	// =====================================================================
	// P + W + C. O PRESO DE PE
	// =====================================================================
	private void PresoDePeAnda(PalcoDoRastejo p)
	{
		GD.Print("[rastejo] -- P. o preso de pe (razao 8) anda, a 1/4 do passo sem a gravidade --");
		ServerPlayer preso = ForjarRj("rj: o preso", p.NoCampo);
		LigarRj(p, 7);   // 1 do chao + 7 da maquina, contra a maestria 1: razao 8
		RecalcularVelocidade(preso);
		Fighter f = preso.Ficha;
		AfirmarRj("P0. (montagem) no campo de 7x da maquina, o corpo de maestria 1 sente razao 8: PRESO (`Prende`), de pe e inteiro",
			Math.Abs(Esmagamento.Razao(f) - 8) < 1e-6 && Esmagamento.Prende(f) && !f.KO && Math.Abs(f.HP - 100) < 1e-6,
			$"razao {Esmagamento.Razao(f):0.##}; ko {f.KO}; vida {f.HP:0.#}");
		AfirmarRj("P0a. a pergunta do rastejo responde SIM pro preso de pe -- e o funil de vetor, que a IA obedece, continua recusando",
			SoAGravidadeSegura(preso) && !PodeMexerOCorpo(preso), $"so a gravidade {SoAGravidadeSegura(preso)}; funil {PodeMexerOCorpo(preso)}");
		AfirmarRj("P0b. **A FICHA DIZ AO CLIENTE** que ele pode tentar o passo (o bit `SoRasteja` do `Estado3`)",
			preso.Sheet().SoRasteja, $"estado3 {preso.Sheet().Estado3}");

		var (andou, pedido, correcoes) = SegurarATeclaRj(preso, new Vec2(0, -1), 10);
		AfirmarRj("P1. **PRESO DE PE, ANDA**: dez pacotes do cliente honesto (o passo do `SpeedStat` da ficha), e o corpo sai do lugar sem correcao nenhuma",
			AndouOPedidoRj(andou, pedido) && correcoes == 0 && preso.Moving,
			$"andou {andou:0.00} de {pedido:0.00} px pedidos; correcoes {correcoes}; moving {preso.Moving}");

		preso.Pos = p.NoCampo;
		RastejoDesligadoDeTeste = true;
		try
		{
			var (comDefeito, _, _) = SegurarATeclaRj(preso, new Vec2(0, -1), 10);
			AfirmarRj("P1c. (defeito injetado: ninguem se arrasta -- o jogo de antes) a regua P1 REPROVA: o preso nao sai do lugar",
				comDefeito < 0.01f, $"andou {comDefeito:0.00} px");
		}
		finally { RastejoDesligadoDeTeste = false; }

		// O PASSO: a referencia e o MESMO corpo fora do campo -- o passo que ele daria andando sem a gravidade --, pela mesma conta de producao
		(float sem, float com) = PassosRj(p, preso, 7);
		AfirmarRj("P2. **A 1/4 DO PASSO SEM A GRAVIDADE**: o `SpeedStat` do preso (o numero que a ficha leva e o `ValidateStep` confere) e um quarto do que o mesmo corpo tem fora do campo -- nao o freio de antes (1/8), nem os dois juntos (1/32)",
			sem > 0 && Math.Abs(com / sem - Esmagamento.FracaoDoRastejo) < 1e-4,
			$"fora {sem:0.####}; preso {com:0.####}; razao {com / sem:0.####} (o freio de antes daria {1 / Esmagamento.Freio(f):0.###})");
		Esmagamento.RastejoComOFreioDeTeste = true;
		try
		{
			(float sem2, float comFreio) = PassosRj(p, preso, 7);
			AfirmarRj("P2c. (defeito injetado: o freio do esmagamento por cima do quarto) a regua P2 REPROVA",
				Math.Abs(comFreio / sem2 - Esmagamento.FracaoDoRastejo) > 1e-3, $"razao {comFreio / sem2:0.####}");
		}
		finally { Esmagamento.RastejoComOFreioDeTeste = false; RecalcularVelocidade(preso); }

		// (o AVISO do preso -- "mal consegue se arrastar" -- e do corpo com tela: o `AvisarDoEsmagamento` cala pro corpo forjado. Quem o
		// le e o robo `--diagrastejo`, no chat do jogador de verdade.)

		// C1. NAO CORRE
		preso.Pos = p.NoCampo;
		RecalcularVelocidade(preso);
		double kiAntes = preso.Ficha.Ki;
		SegurarATeclaRj(preso, new Vec2(0, -1), 6, correndo: true);
		AfirmarRj("C1. **QUEM SE ARRASTA NAO CORRE**: o preso de pe pede a corrida (o bit do SHIFT), e o servidor nao a concede -- nem cobra o Ki dela",
			!preso.Correndo && !preso.Ficha.dashing && Math.Abs(preso.Ficha.Ki - kiAntes) < 1e-9,
			$"correndo {preso.Correndo}; dashing {preso.Ficha.dashing}; ki {kiAntes:0.#} -> {preso.Ficha.Ki:0.#}");
		RastejoCorreDeTeste = true;
		try
		{
			preso.Pos = p.NoCampo;
			SegurarATeclaRj(preso, new Vec2(0, -1), 6, correndo: true);
			AfirmarRj("C1c. (defeito injetado: quem se arrasta corre) a regua C1 REPROVA: a corrida e concedida", preso.Correndo,
				$"correndo {preso.Correndo}");
		}
		finally { RastejoCorreDeTeste = false; }

		// C2. NAO VOA: no ar, o preso continua preso
		preso.Pos = p.NoCampo;
		preso.Voando = true;
		preso.Altitude = Voo.AlturaDePairar;
		try
		{
			var (noAr, _, _) = SegurarATeclaRj(preso, new Vec2(0, -1), 10);
			AfirmarRj("C2. **NO AR O PRESO CONTINUA PRESO** (o rastejo e so a pe: nao voa) -- pairando no campo de razao 8, o passo e recusado como antes",
				!SoAGravidadeSegura(preso) && noAr < 0.01f, $"so a gravidade {SoAGravidadeSegura(preso)}; andou {noAr:0.00} px");
		}
		finally { preso.Voando = false; preso.Altitude = 0; }

		// C3. NAO AGARRA -- e o mesmo corpo, fora do campo, agarra o mesmo alvo (senao a regua passaria sem medir nada)
		const float T = ZoneCollision.TileSize;
		preso.Pos = p.NoCampo;
		preso.Facing = Facing.East;
		ServerPlayer alvo = ForjarRj("rj: o alvo", p.NoCampo + new Vec2(0.75f * T, 0));
		LigarRj(p, 7);
		AlternarAgarrao(preso);
		bool presoAgarrou = preso.AgarrandoId != 0;
		if (presoAgarrou) Soltar(preso, MotivoDaSoltura.Tecla);
		LigarRj(p, 0);
		preso.Facing = Facing.East;
		AlternarAgarrao(preso);
		bool livreAgarrou = preso.AgarrandoId == alvo.Id;
		AfirmarRj("C3. **NAO AGARRA**: preso no campo, o agarrao continua recusado (o funil de vetor nao afrouxou) -- e o MESMO corpo, fora do campo, agarra o mesmo alvo",
			!presoAgarrou && livreAgarrou, $"preso agarrou {presoAgarrou}; livre agarrou {livreAgarrou}");

		// C4. DE MAOS VAZIAS: quem ja segurava alguem quando a gravidade o prendeu continua preso (o passo dele seria o arremesso)
		LigarRj(p, 7);
		RecalcularVelocidade(preso);
		var (segurando, _, _) = SegurarATeclaRj(preso, new Vec2(0, -1), 10);
		AfirmarRj("C4. **QUEM SEGURA ALGUEM NAO SE ARRASTA**: agarrou fora do campo e a gravidade o prendeu -- o passo continua recusado ate soltar (o passo de quem segura e o arremesso)",
			livreAgarrou && preso.AgarrandoId == alvo.Id && !SoAGravidadeSegura(preso) && segurando < 0.01f,
			$"segurando {preso.AgarrandoId == alvo.Id}; so a gravidade {SoAGravidadeSegura(preso)}; andou {segurando:0.00} px");
		if (preso.AgarrandoId != 0) Soltar(preso, MotivoDaSoltura.Tecla);
		AfirmarRj("C4b. ...e solto, de maos vazias, ele volta a se arrastar", SoAGravidadeSegura(preso), $"agarrando {preso.AgarrandoId}");
		TirarRj(alvo);
		TirarRj(preso);
	}

	// =====================================================================
	// D + M + F. O DESMAIADO PELA GRAVIDADE
	// =====================================================================
	private void ODesmaiadoPelaGravidadeSeArrasta(PalcoDoRastejo p)
	{
		GD.Print("[rastejo] -- D. o desmaiado pela gravidade (razao 2) se arrasta, deitado, a 1/4 --");
		ServerPlayer d = DesmaiarPelaGravidadeRj(p, "rj: o desmaiado", out List<string> falas);
		CombatState c = d.Combate;
		AfirmarRj("D0. (montagem) o tique de producao da gravidade (razao 2: esmaga, nao prende) derrubou o corpo que estava na beira -- desmaio por nucleo",
			d.Ficha.KO && c.NocautePorVital && !Esmagamento.Prende(d.Ficha), $"ko {d.Ficha.KO}; por nucleo {c.NocautePorVital}; razao {Esmagamento.Razao(d.Ficha):0.##}");
		AfirmarRj("D1. **O DESMAIO PELA GRAVIDADE FICA MARCADO**, e a pergunta do rastejo responde sim pro desmaiado",
			c.DesmaioDaGravidade && SoAGravidadeSegura(d), $"marca {c.DesmaioDaGravidade}; so a gravidade {SoAGravidadeSegura(d)}");
		AfirmarRj("D1b. **...e a ficha ja diz ao cliente, no mesmo tique** (o passo e o bit refeitos na hora do desmaio)",
			d.SoRasteja && d.Sheet().SoRasteja, $"bit no corpo {d.SoRasteja}; na ficha {d.Sheet().SoRasteja}");
		AfirmarRj("D2. **...e o jogo diz a quem caiu que ele ainda se arrasta** (caido sempre foi parado: sem a frase ninguem tentaria)",
			Disse(falas, "ainda consegue se arrastar"), Ultimos(falas));
		AfirmarRj("D3. **A QUEDA E A DE SEMPRE**: o teto do nocaute, o desmaio por nucleo e o piso dele -- o rastejo nao mexe em quando se cai",
			Math.Abs(c.NocauteRestante - MeleeResolver.TetoDoNocaute) < 1e-6 && c.NocautePorVital
			&& Math.Abs(c.DesmaioMinimo - CombatState.SegundosMinimosDoDesmaio) < 1e-6,
			$"teto {c.NocauteRestante:0.#}; por nucleo {c.NocautePorVital}; piso {c.DesmaioMinimo:0.#}");

		var (andou, pedido, correcoes) = SegurarATeclaRj(d, new Vec2(0, -1), 10);
		AfirmarRj("D4. **O DESMAIADO PELA GRAVIDADE SE ARRASTA**: deitado no campo, dez pacotes do cliente honesto, e o corpo sai do lugar -- sem correcao e sem levantar",
			AndouOPedidoRj(andou, pedido) && correcoes == 0 && d.Ficha.KO,
			$"andou {andou:0.00} de {pedido:0.00} px pedidos; correcoes {correcoes}; ko {d.Ficha.KO}");
		d.Pos = p.NoCampo;
		RastejoDesligadoDeTeste = true;
		try
		{
			var (comDefeito, _, _) = SegurarATeclaRj(d, new Vec2(0, -1), 10);
			AfirmarRj("D4c. (defeito injetado: ninguem se arrasta) a regua D4 REPROVA: o desmaiado nao sai do lugar", comDefeito < 0.01f,
				$"andou {comDefeito:0.00} px");
		}
		finally { RastejoDesligadoDeTeste = false; }

		// D5. NAO VIRA: o pacote diz "olho pro oeste" (o olhar de um cliente que virasse com a tecla)
		d.Pos = p.NoCampo;
		Facing olharAntes = d.Facing, quedaAntes = d.FacingDaQueda, cabecaAntes = d.DirecaoDeitado;
		SegurarATeclaRj(d, new Vec2(0, -1), 8, olhar: Facing.West);
		AfirmarRj("D5. **DEITADO, NAO VIRA**: arrastando-se com o pacote dizendo oeste, a cabeca deitada, a direcao da queda e o olhar ficam como cairam -- o corpo desliza",
			d.Facing == olharAntes && d.FacingDaQueda == quedaAntes && d.DirecaoDeitado == cabecaAntes,
			$"olhar {olharAntes}->{d.Facing}; queda {quedaAntes}->{d.FacingDaQueda}; cabeca {cabecaAntes}->{d.DirecaoDeitado}");
		d.Pos = p.NoCampo;
		DeitadoViraNoRastejoDeTeste = true;
		try
		{
			SegurarATeclaRj(d, new Vec2(0, -1), 8, olhar: Facing.West);
			AfirmarRj("D5c. (defeito injetado: o deitado vira pro passo) a regua D5 REPROVA: a cabeca ou o olhar seguem a tecla",
				d.DirecaoDeitado != cabecaAntes || d.Facing != olharAntes, $"olhar {d.Facing}; cabeca {d.DirecaoDeitado}");
		}
		finally { DeitadoViraNoRastejoDeTeste = false; d.Facing = olharAntes; d.FacingDaQueda = quedaAntes; }

		(float sem, float com) = PassosDoDesmaiadoRj(p, d, 1);
		AfirmarRj("D6. **A 1/4, DEITADO**: no campo de razao 2, o passo do desmaiado e um quarto do que o mesmo corpo (a mesma vida) daria andando sem a gravidade -- e nao o quarto com o freio da razao 2 por cima (1/8)",
			sem > 0 && Math.Abs(com / sem - Esmagamento.FracaoDoRastejo) < 1e-4, $"sem gravidade {sem:0.####}; rastejando {com:0.####}; razao {com / sem:0.####}");

		// M. O MAJIN DESFEITO EM PEDACOS
		d.Pos = p.NoCampo;
		c.Desfeito = true;
		try
		{
			var (desfeito, _, _) = SegurarATeclaRj(d, new Vec2(0, -1), 10);
			AfirmarRj("M1. **O MAJIN DESFEITO EM PEDACOS NAO SE ARRASTA** (o corpo nem esta la), mesmo com o desmaio marcado pela gravidade",
				c.DesmaioDaGravidade && !SoAGravidadeSegura(d) && desfeito < 0.01f, $"marca {c.DesmaioDaGravidade}; andou {desfeito:0.00} px");
		}
		finally { c.Desfeito = false; }

		SaiDoCampoSeArrastando(p, d);
	}

	private void SaiDoCampoSeArrastando(PalcoDoRastejo p, ServerPlayer d)
	{
		GD.Print("[rastejo] -- F. sai do campo se arrastando, e la fora continua ate levantar --");
		d.Pos = p.NoCampo;
		LigarRj(p, 1);
		RecalcularVelocidade(d);
		Vec2 antes = d.Pos;
		int correcoes = d.Corrections, pacotes = 0;
		while (d.Ficha.gravmult > 0 && pacotes < 900)
		{
			SegurarATeclaRj(d, new Vec2(1, 0), 10);
			pacotes += 10;
			LerOsCampos();
			RecalcularVelocidade(d);
		}
		float andou = (d.Pos - antes).Length;
		AfirmarRj("F1. **SAI DO CAMPO SE ARRASTANDO**: deitado, pacote a pacote pro leste, ate o campo nao o alcancar mais -- sem levantar e sem correcao",
			d.Ficha.gravmult == 0 && d.Ficha.KO && andou > 8f && d.Corrections == correcoes,
			$"campo {d.Ficha.gravmult:0.##}x; ko {d.Ficha.KO}; andou {andou:0.0} px em {pacotes} pacotes; correcoes {d.Corrections - correcoes}");

		var (laFora, pedidoLaFora, correcoesLaFora) = SegurarATeclaRj(d, new Vec2(1, 0), 10);
		AfirmarRj("F2. **FORA DO CAMPO, AINDA DESMAIADO, CONTINUA SE ARRASTANDO**: o desmaio e o mesmo, e ele so acaba quando o corpo levanta (decisao minha)",
			d.Ficha.KO && SoAGravidadeSegura(d) && AndouOPedidoRj(laFora, pedidoLaFora) && correcoesLaFora == 0,
			$"ko {d.Ficha.KO}; andou {laFora:0.00} de {pedidoLaFora:0.00} px pedidos; correcoes {correcoesLaFora}");

		(float sem, float com) = PassosDoDesmaiadoRj(p, d, 1);
		AfirmarRj("F3. **...no mesmo quarto**: la fora o passo do desmaiado continua um quarto do que ele daria andando",
			sem > 0 && Math.Abs(com / sem - Esmagamento.FracaoDoRastejo) < 1e-4, $"sem gravidade {sem:0.####}; rastejando {com:0.####}; razao {com / sem:0.####}");

		LevantarRj(d);
		RecalcularVelocidade(d);
		AfirmarRj("F4. **LEVANTAR E O DE SEMPRE, E DE PE O PASSO VOLTA**: o nucleo sarado ergue o corpo pela porta de sempre, e fora do campo ele nao se arrasta mais",
			!d.Ficha.KO && !SoAGravidadeSegura(d) && !d.SoRasteja && !d.Sheet().SoRasteja,
			$"ko {d.Ficha.KO}; so a gravidade {SoAGravidadeSegura(d)}; bit {d.SoRasteja}");
		TirarRj(d);
	}

	// =====================================================================
	// S. O DESMAIADO POR OUTRA COISA
	// =====================================================================
	private void ODesmaiadoPorOutraCoisaFicaParado(PalcoDoRastejo p)
	{
		GD.Print("[rastejo] -- S. o desmaiado por outra coisa continua parado (so a gravidade) --");
		LigarRj(p, 1);
		ServerPlayer s1 = ForjarRj("rj: o socado", p.ForaDoCampo);
		s1.Combate.Nocautear(MeleeResolver.TetoDoNocaute, porVital: true);   // o ramo do nocaute do soco (`MeleeResolver.AplicarNoMembro`)
		RecalcularVelocidade(s1);
		var (a1, _, _) = SegurarATeclaRj(s1, new Vec2(1, 0), 10);
		AfirmarRj("S1. **O DESMAIADO POR SOCO NAO SE ARRASTA**: fora do campo, derrubado pelo funil do soco, o passo continua recusado",
			s1.Ficha.KO && !s1.Combate.DesmaioDaGravidade && !SoAGravidadeSegura(s1) && a1 < 0.01f,
			$"ko {s1.Ficha.KO}; marca {s1.Combate.DesmaioDaGravidade}; andou {a1:0.00} px");
		TirarRj(s1);

		ServerPlayer s2 = ForjarRj("rj: o socado no campo", p.NoCampo);
		s2.Combate.Nocautear(MeleeResolver.TetoDoNocaute, porVital: true);
		TickDoEsmagamento();   // a gravidade fere o caido -- e nao o marca: ele ja estava no chao
		RecalcularVelocidade(s2);
		var (a2, _, _) = SegurarATeclaRj(s2, new Vec2(0, -1), 10);
		AfirmarRj("S2. **...NEM DENTRO DO CAMPO**: socado no campo de razao 2, e o tique da gravidade que o fere depois nao o marca -- so o desmaio que a gravidade FEZ (a regra literal)",
			s2.Ficha.KO && !s2.Combate.DesmaioDaGravidade && a2 < 0.01f, $"marca {s2.Combate.DesmaioDaGravidade}; andou {a2:0.00} px");
		TirarRj(s2);

		ServerPlayer s3 = DesmaiarPelaGravidadeRj(p, "rj: o que caiu duas vezes", out _);
		bool marcadoAntes = s3.Combate.DesmaioDaGravidade;
		LigarRj(p, 0);
		LevantarRj(s3);
		s3.Combate.Nocautear(MeleeResolver.TetoDoNocaute, porVital: true);   // de pe de novo, o soco
		RecalcularVelocidade(s3);
		var (a3, _, _) = SegurarATeclaRj(s3, new Vec2(1, 0), 10);
		AfirmarRj("S3. **O DESMAIO NOVO NAO HERDA A MARCA**: quem desmaiou pela gravidade, levantou e caiu de um soco nao se arrasta",
			marcadoAntes && s3.Ficha.KO && !s3.Combate.DesmaioDaGravidade && a3 < 0.01f,
			$"marcado antes {marcadoAntes}; marca agora {s3.Combate.DesmaioDaGravidade}; andou {a3:0.00} px");
		CombatState.DesmaioHerdaAMarcaDeTeste = true;
		try
		{
			LevantarRj(s3);
			s3.Combate.DesmaioDaGravidade = true;   // a marca do desmaio anterior, de volta (o soco de cima a tirou)
			s3.Combate.Nocautear(MeleeResolver.TetoDoNocaute, porVital: true);
			RecalcularVelocidade(s3);
			var (comDefeito, pedidoComDefeito, _) = SegurarATeclaRj(s3, new Vec2(1, 0), 10);
			AfirmarRj("S3c. (defeito injetado: o desmaio novo herda a marca) a regua S3 REPROVA: o socado se arrasta",
				s3.Combate.DesmaioDaGravidade && AndouOPedidoRj(comDefeito, pedidoComDefeito),
				$"marca {s3.Combate.DesmaioDaGravidade}; andou {comDefeito:0.00} de {pedidoComDefeito:0.00} px pedidos");
		}
		finally { CombatState.DesmaioHerdaAMarcaDeTeste = false; }
		TirarRj(s3);

		ServerPlayer s4 = DesmaiarPelaGravidadeRj(p, "rj: o socado no chao", out _);
		s4.Combate.Nocautear(MeleeResolver.TetoDoNocaute, porVital: true);   // o soco no desmaiado: ele ja estava no chao
		RecalcularVelocidade(s4);
		var (a4, pedido4, _) = SegurarATeclaRj(s4, new Vec2(0, -1), 10);
		AfirmarRj("S4. (decisao minha) **QUEM DESMAIOU PELA GRAVIDADE E LEVA UM SOCO NO CHAO CONTINUA SE ARRASTANDO**: o desmaio e o mesmo",
			s4.Ficha.KO && s4.Combate.DesmaioDaGravidade && AndouOPedidoRj(a4, pedido4),
			$"marca {s4.Combate.DesmaioDaGravidade}; andou {a4:0.00} de {pedido4:0.00} px pedidos");
		TirarRj(s4);
	}

	// =====================================================================
	// I. A IA CONTINUA PRESA
	// =====================================================================
	private void AIaContinuaPresa(PalcoDoRastejo p)
	{
		GD.Print("[rastejo] -- I. a IA continua presa (decisao minha) --");
		ServerPlayer npc = ForjarRj("rj: o npc", p.NoCampo);
		LigarRj(p, 7);
		RecalcularVelocidade(npc);
		Vec2 antes = npc.Pos;
		for (int i = 0; i < 10; i++) PassoDaIa(npc, new Comando { Rumo = new Vec2(0, -1) }, Protocol.TickSeconds);
		float andou = (npc.Pos - antes).Length;
		AfirmarRj("I1. (decisao minha) **A IA CONTINUA PRESA**: no mesmo campo de razao 8, pelo atuador da IA (`PassoDaIa`), o corpo nao sai do lugar -- o rastejo e licenca do passo do JOGADOR",
			SoAGravidadeSegura(npc) && andou < 0.01f && !npc.Moving, $"so a gravidade {SoAGravidadeSegura(npc)}; andou {andou:0.00} px");
		TirarRj(npc);
	}
}
