using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using KaiokenCore = Jandirus.Core.Skills.Kaioken;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--v030teste` -- AS FAMILIAS DO ADENDO 12 (a noite de 2026-10-15, ainda a v0.3.0). Rodam no FIM da bancada e
/// nesta ordem: as duas primeiras precisam de muro de pe, e o estouro colossal da ultima racha o chao em volta.
///
///   CC. O CANO -- quem esta colado leva o tiro: a bola nasce no corpo e estoura nele; o raio o pega na boca (item 43).
///   GC. A GENKIDAMA EM CIMA DA CABECA -- forma-se acima do corpo, sobe ao crescer, sai NA FRENTE dele, para antes de um
///       muro, e quem esta entre o corpo e a saida dela leva a bola (item 42).
///   KK. O KAIO-KEN -- a maestria na ficha lenta, o multiplicador escolhido (`Kaioken_N`), o X que apaga, e o corpo acima
///       da maestria fazendo o que a tela promete: quebrar no nocaute, quebrar sem Ki (item 37). E (adendo 13) o Deus que
///       nao o sustenta: o botao recusa no SSG, e virar SSG apaga o que estava aceso.
///   GD. A GENKIDAMA QUE ARRASTA -- leva o mais fraco como um raio, estoura no fim com a cratera ENORME, estoura (e nao moi)
///       prensando alguem num muro, e o estouro dela alcanca o dobro e meio do de uma bola comum (item 41).
///   KP. (adendo 16, v0.3.1) O KAIO-KEN PRESO NO SAVE -- o disco nao leva o buff aceso (o Kaio-ken e as tres maos do motor de
///       buffs), o login cura quem ja ficou preso, e a armadilha do amigo do dono reproduzida com a cura desligada (item 47).
///   GS. (adendo 15, v0.3.1) A GENKIDAMA SO SOCA AO SAIR -- a pose de tiro nao arma ao formar nem a cada pulso (a bola nasce
///       NA MAO, e o snapshot diz isso ate ela sair); arma no lancamento, fica a fase 3 inteira com a trava e cai com ela
///       (item 46).
///   DS. (adendo 18, v0.3.2) A DEATH BALL SO SOCA AO SAIR -- a mesma cura: nada de pose ao erguer e a cada estagio (os sons de
///       erguer e de crescer pelos gestos), e o soco do lancamento a guia inteira, caindo com ela; sem guia, o de toda bola.
///   MZ. (adendo 18, v0.3.2) O MORTO DE PE LUTA COMO O VIVO -- no Inferno: o Zanzo Clash que sobrevive ao tique, o preso que se
///       debate e sente o aperto, o mestre que convida, a Danca, o estilo; e a colisao de beam na guarda entre dois mortos.
///   IT. (adendo 18, v0.3.2) A PASSAGEM DO MORTO -- a Transmissao do Outro Mundo pra um vivo: chega de pe e meio transparente,
///       o disco grava a volta, e em 5 s a alma volta pro lugar de onde saiu; cair no meio acaba na mesa do Enma.
///
/// A tela (a escolha do Kaio-ken, a trava da Genkidama no cliente, a pergunta do refugio uma vez so) e da `--diagv030`.
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// CC) O CANO: quem esta colado leva o tiro (item 43)
	// =====================================================================
	private void OCanoV30()
	{
		GD.Print("[v030] --- CC) o tiro em quem esta colado: a bola nasce nele e estoura nele, e o raio o pega na boca (item 43) ---");
		const int T = ZoneCollision.TileSize;
		LimparTudoDaBancada();

		// ---- A REGUA DO CORE ----
		var boca = new Vec2(T, 0);
		bool dentro = BocaDeCano.NoCano(Vec2.Zero, boca, new Vec2(12, 5), Projetil.RaioDeImpacto, out float aoLongo)
					  && Math.Abs(aoLongo - 12) < 1e-3f;
		bool fora = !BocaDeCano.NoCano(Vec2.Zero, boca, new Vec2(-6, 0), Projetil.RaioDeImpacto, out _)
					&& !BocaDeCano.NoCano(Vec2.Zero, boca, new Vec2(12, 20), Projetil.RaioDeImpacto, out _)
					&& !BocaDeCano.NoCano(Vec2.Zero, boca, new Vec2(T + 4, 0), Projetil.RaioDeImpacto, out _)
					&& !BocaDeCano.NoCano(Vec2.Zero, Vec2.Zero, Vec2.Zero, Projetil.RaioDeImpacto, out _);
		AfirmarV30("a regua do cano (`BocaDeCano.NoCano`): dentro e entre o centro de quem atira e a boca, ate a largura do tiro de lado -- atras, de lado, alem da boca e o cano sem comprimento ficam de fora",
				   dentro && fora, $"ao longo {aoLongo:0.##}");

		// ---- A BOLA EM QUEM ESTA COLADO ----
		Vec2 c = CorredorDeTerraFirme(12);
		var receita = new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 3, AlcanceTiles = 10,
			Deflectivel = false,   // o que se mede e o encontro; o raspao e um sorteio que acaba com a bola antes do dano
		};

		(bool Marcada, bool NoCorpo, bool Ferida, string Detalhe) UmaBola(float dx, float dy, bool cego)
		{
			LimparTudoDaBancada();
			ServerPlayer atira = Forjar("Atira CC", c, bp: 500_000);
			atira.Facing = Facing.East;
			Vec2 onde = c + new Vec2(dx, dy);
			ServerPlayer colada = Forjar("Colada CC", onde, bp: 500);
			double vida = colada.Combate.Corpo.Vida();
			CanoCegoDeTeste = cego;
			try
			{
				Projetil bola = Disparar(atira, receita);
				bool marcada = bola.AlvoNoCano == colada.Id;
				Vec2 nasceu = bola.Pos;
				for (int i = 0; i < 20 && bola.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
				bool ferida = colada.Combate.Corpo.Vida() < vida;
				// NO CORPO: nasceu e acabou a um raio de impacto de onde ele estava -- e nao na boca, atras dele
				bool noCorpo = bola.Fim == FimDeProjetil.Acertou && (nasceu - onde).Length <= Projetil.RaioDeImpacto
							   && (bola.Pos - onde).Length <= Projetil.RaioDeImpacto;
				return (marcada, noCorpo, ferida,
						$"nasceu em {nasceu - c}, acabou em {bola.Pos - c} ({bola.Fim}), corpo em {onde - c}; vida {vida:0.##} -> {colada.Combate.Corpo.Vida():0.##}");
			}
			finally { CanoCegoDeTeste = false; }
		}

		var linhas = new List<string>();
		bool todas = true;
		foreach (float d in (float[])[0f, 10f, 20f, 31f])
		{
			var r = UmaBola(d, 0, cego: false);
			todas &= r.Marcada && r.NoCorpo && r.Ferida;
			linhas.Add($"{d:0} px: {r.Detalhe}");
		}
		var torta = UmaBola(12, 8, cego: false);
		AfirmarV30("**A BOLA ACERTA QUEM ESTA COLADO** (*\"se alguem ta mt perto de vc o ataque de ki n acerta essa pessoa, simplesmente o ki aparece atras dela\"*): a 0, 10, 20 e 31 px a frente -- e a 8 px do eixo --, entre o corpo e a boca, ela nasce NO corpo e estoura nele",
				   todas && torta.Marcada && torta.NoCorpo && torta.Ferida, string.Join(" || ", linhas) + " || torta: " + torta.Detalhe);

		var atras = UmaBola(-12, 0, cego: false);
		var deLado = UmaBola(12, 24, cego: false);
		var alem = UmaBola(40, 0, cego: false);
		AfirmarV30("...e o cano olha SO pra frente e so na largura do tiro: quem esta atras e quem esta a 24 px do eixo nao levam; quem esta alem da boca leva pelo voo de sempre",
				   !atras.Marcada && !atras.Ferida && !deLado.Marcada && !deLado.Ferida && !alem.Marcada && alem.Ferida,
				   $"atras: {atras.Detalhe} || de lado: {deLado.Detalhe} || alem: {alem.Detalhe}");

		var cega = UmaBola(10, 0, cego: true);
		AfirmarV30("(defeito injetado: o cano cego, o jogo de antes) a bola nasce na boca, ATRAS de quem esta a 10 px, e nao o acerta -- a regua de cima REPROVA",
				   !cega.Marcada && !cega.Ferida, cega.Detalhe);

		// ---- O CANO E DO BERCO: o tiro marcado e posto noutro lugar antes do primeiro tique nao acerta quem estava nele ----
		// (a familia 7 da `--projetilteste` espalha os tiros assim; com a marca valendo, os 256 morriam no primeiro corpo)
		{
			LimparTudoDaBancada();
			ServerPlayer atira = Forjar("Atira CC berco", c, bp: 500_000);
			atira.Facing = Facing.East;
			ServerPlayer colada = Forjar("Colada CC berco", c + new Vec2(10, 0), bp: 500);
			double vida = colada.Combate.Corpo.Vida();
			Projetil bola = Disparar(atira, receita);
			bool marcada = bola.AlvoNoCano == colada.Id;
			bola.Pos = c + new Vec2(0, 8 * T);
			bola.Rumo = new Vec2(0, 1);
			for (int i = 0; i < 5 && bola.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
			AfirmarV30("...e o cano e do BERCO: o mesmo tiro, posto noutro lugar antes do primeiro tique, nao acerta quem estava colado no atirador",
					   marcada && colada.Combate.Corpo.Vida() >= vida - 1e-9,
					   $"marcada={marcada} vida {vida:0.##} -> {colada.Combate.Corpo.Vida():0.##}");
		}

		// ---- O RAIO EM QUEM ESTA COLADO ----
		(bool Marcado, bool Pegou, bool NaoAtras, string Detalhe) UmRaio(bool cego)
		{
			LimparTudoDaBancada();
			ServerPlayer atira = Forjar("Raio CC", c, bp: BpDoFeixeDaEscada);
			atira.Facing = Facing.East;
			ServerPlayer colado = Forjar("Colado CC", c + new Vec2(12, 0), bp: BpDoMaisFracoDaEscada);
			double[] antes = VidaPorParte(colado);
			CanoCegoDeTeste = cego;
			try
			{
				Projetil raio = RaioDaBancada(atira, BaseQueFere(atira, colado));
				bool marcado = raio.AlvoNoCano == colado.Id && raio.Pos.Equals(BocaDeCano.De(atira.Pos, raio.Rumo));
				bool pegou = false;
				for (int i = 0; i < 6 && raio.Vivo; i++)
				{
					UmTiqueDeArrasto();
					pegou |= raio.Arrastando == colado.Id || FeridaMaisFunda(colado, antes) > 0;
				}
				Vec2 corpo = raio.Pos - raio.Cauda;
				bool naoAtras = corpo.X * raio.Rumo.X + corpo.Y * raio.Rumo.Y >= -0.01f;
				return (marcado, pegou, naoAtras,
						$"arrasto={raio.Arrastando} encostado={raio.Encostado} ferida {FeridaMaisFunda(colado, antes):0.###}; cabeca menos cauda {corpo}");
			}
			finally { CanoCegoDeTeste = false; }
		}

		var raioCerto = UmRaio(cego: false);
		AfirmarV30("**O RAIO PEGA QUEM ESTA COLADO**: a 12 px a frente (atras da boca) ele nasce na boca, como sempre, e o primeiro tique o pega -- o mais fraco vai levado -- sem a cabeca ficar atras da mao",
				   raioCerto.Marcado && raioCerto.Pegou && raioCerto.NaoAtras, raioCerto.Detalhe);
		var raioCego = UmRaio(cego: true);
		AfirmarV30("(defeito injetado: o cano cego) o mesmo raio passa reto por quem esta colado -- a regua de cima REPROVA",
				   !raioCego.Marcado && !raioCego.Pegou, raioCego.Detalhe);
		LimparTudoDaBancada();
	}

	// =====================================================================
	// GC) A GENKIDAMA EM CIMA DA CABECA, E SAINDO NA FRENTE (item 42)
	// =====================================================================
	private void AGenkidamaEmCimaDaCabecaV30()
	{
		GD.Print("[v030] --- GC) a Genkidama se forma em cima da cabeca e sai na frente do corpo (item 42) ---");
		const int T = ZoneCollision.TileSize;

		// O DESENHO NAO COBRE O CORPO: a bola inteira (o raio desenhado x a escala) acima do topo do quadro do corpo -- e,
		// lancada, inteira a frente da beirada dele.
		static bool EmCima(Projetil? b, ServerPlayer pl, double escala) =>
			b != null && Math.Abs(b.Pos.X - pl.Pos.X) < 0.5f
			&& b.Pos.Y + ArteDeProjetil.RaioDaGenkidama * (float)escala <= pl.Pos.Y - T / 2f + 0.5f;
		static bool NaFrente(Projetil? b, ServerPlayer pl, double escala) =>
			b != null && Math.Abs(b.Pos.Y - pl.Pos.Y) < 0.5f
			&& b.Pos.X - ArteDeProjetil.RaioDaGenkidama * (float)escala >= pl.Pos.X + T / 2f - 0.5f;

		(bool Formando, bool Crescida, bool Saiu, string Detalhe) UmaGenkidama(string nome)
		{
			LimparTudoG12();
			ServerPlayer goku = ForjarG12(nome, CorredorDeTerraFirme(24), bp: 50_000, skills: [PathSpiritBombG12]);
			goku.Facing = Facing.East;
			UsarHabilidade(goku, "SpiritBomb");
			if (!_genkidamaG12.TryGetValue(goku.Id, out EstadoDaGenkidamaG12? g)) return (false, false, false, "a Genkidama nao comecou");
			bool formando = EmCima(g.Bola, goku, 1);
			float alturaDeNascenca = g.Bola?.Pos.Y ?? 0;
			TiquesG12(TiquesDeG12(3.1) + TiquesDeG12(4.0));   // forma, e cresce dois pulsos
			bool crescida = g.Escala > 1.15 && EmCima(g.Bola, goku, g.Escala) && g.Bola!.Pos.Y < alturaDeNascenca - 1;
			UsarHabilidade(goku, "SpiritBomb");   // pede o disparo
			for (int i = 0; i < TiquesDeG12(2.5) && g.Fase < 3; i++) TickG12(Protocol.TickSeconds);   // sai, e ainda nao andou
			bool saiu = g.Fase == 3 && NaFrente(g.Bola, goku, g.Escala);
			return (formando, crescida, saiu, $"escala {g.Escala:0.0}; nasceu a {goku.Pos.Y - alturaDeNascenca:0} px acima; agora em {g.Bola?.Pos - goku.Pos} do corpo (fase {g.Fase})");
		}

		var certa = UmaGenkidama("Goku GC");
		AfirmarV30("**EM CIMA DA CABECA** (*\"a genkidama enquanto carrega seria bom ficar em cima da cabeca do personagem\"*): ela se forma com a beirada de baixo no topo do corpo, e crescendo SOBE -- o desenho nunca cobre o corpo",
				   certa.Formando && certa.Crescida, certa.Detalhe);
		AfirmarV30("**E SAI NA FRENTE DELE** (*\"quando lancar ela sai da frente dele\"*): no disparo ela vai pra frente do corpo, inteira a frente da beirada dele",
				   certa.Saiu, certa.Detalhe);

		(bool Formando, bool Crescida, bool Saiu, string Detalhe) antes;
		GenkidamaComoAntesDeTeste = true;
		try { antes = UmaGenkidama("Goku GC de antes"); }
		finally { GenkidamaComoAntesDeTeste = false; }
		AfirmarV30("(defeito injetado: o jogo de antes -- um tile ao norte, e a boca de sempre) a bola de tres tiles de raio COBRE o corpo: as duas reguas de cima REPROVAM",
				   !antes.Formando && !antes.Saiu, antes.Detalhe);

		// ---- O MURO NO CAMINHO: a saida para antes dele ----
		LimparTudoG12();
		(Vec2 ini, float muro, bool achou) = CorredorAteOMuroV30(3);
		if (!achou) AfirmarV30("achei um corredor que termina num muro a tres tiles", false, "varredura falhou");
		else
		{
			ServerPlayer goku = ForjarG12("Goku GC muro", ini, bp: 50_000, skills: [PathSpiritBombG12]);
			goku.Facing = Facing.East;
			Projetil? bomba = LancarAGenkidamaV30(goku);
			float longe = BocaDeCano.AfastamentoDaBolaGrande(new Vec2(1, 0), ArteDeProjetil.RaioDaGenkidama);
			bool antesDoMuro = bomba != null && bomba.Pos.X < muro && !_pjMapa!.BlockedAt(bomba.Pos);
			bool semAndarPassaria = goku.Pos.X + longe >= muro;
			AfirmarV30("...e com um MURO a tres tiles ela nasce ANTES dele, no ultimo ponto livre -- sem andar a saida, a bola de escala 1 (3,5 tiles a frente) nasceria dentro ou alem do muro",
					   antesDoMuro && semAndarPassaria,
					   $"bola a {bomba?.Pos.X - goku.Pos.X:0} px, o muro a {muro - goku.Pos.X:0} px, a saida sem muro a {longe:0} px");
		}

		// ---- QUEM ESTA ENTRE O CORPO E A SAIDA DELA leva a bola (o cano do item 43, na saida longe) ----
		(bool Pegou, string Detalhe) ColadoNaGenkidama(bool cego)
		{
			LimparTudoG12();
			Vec2 c = CorredorDeTerraFirme(24);
			ServerPlayer goku = ForjarG12("Goku GC cano", c, bp: 50_000, skills: [PathSpiritBombG12]);
			goku.Facing = Facing.East;
			ServerPlayer fraco = ForjarV2("Fraco GC cano", c + new Vec2(2 * T, 0), 1_000);
			double vida = fraco.Combate.Corpo.Vida();
			CanoCegoDeTeste = cego;
			try
			{
				Projetil? bomba = LancarAGenkidamaV30(goku);
				bool marcada = bomba != null && bomba.AlvoNoCano == fraco.Id && Math.Abs(bomba.Pos.X - fraco.Pos.X) < 0.5f;
				bool pegou = false;
				for (int i = 0; i < 3 && bomba is { Vivo: true }; i++)
				{
					TiqueComEmpurraoV30();
					pegou |= bomba.Arrastando == fraco.Id || fraco.Combate.Corpo.Vida() < vida;
				}
				return (marcada && pegou, $"marcada={marcada} pegou={pegou} bola a {bomba?.Pos.X - c.X:0} px (corpo a {2 * T} px)");
			}
			finally { CanoCegoDeTeste = false; }
		}
		var colado = ColadoNaGenkidama(cego: false);
		AfirmarV30("...e quem esta ENTRE o corpo e a saida dela (2 tiles a frente) leva a bola no primeiro tique -- o cano do item 43, numa boca longe",
				   colado.Pegou, colado.Detalhe);
		var colado2 = ColadoNaGenkidama(cego: true);
		AfirmarV30("(defeito injetado: o cano cego) a bola nasce 3,5 tiles a frente e deixa pra tras quem estava a 2 -- a regua de cima REPROVA",
				   !colado2.Pegou, colado2.Detalhe);
		LimparTudoG12();
	}

	// =====================================================================
	// GS) A GENKIDAMA SO SOCA AO SAIR (adendo 15, a v0.3.1)
	// =====================================================================
	/// <summary>
	/// O dono (2026-10-16): *"eu percebi q sempre q a genkidama aumenta o personagem faz animaçao de soco oq n deveria ser, a
	/// animaçao de soco é so quando joga ela e diferente das outras técnicas, o personagem fica na animaçao de soco ate ele
	/// sair da paralisia da genkidama"*. Do lado do servidor, tique a tique: a pose de tiro (`ServerPlayer.GestoAte`, que o
	/// snapshot leva como o `blast` do desenho) NAO arma ao formar nem a cada pulso -- a bola nasce NA MAO --; arma no
	/// lancamento e fica a fase 3 inteira, junto da trava; e cai no tique em que a trava solta. E o snapshot diz que a bola esta
	/// na mao (`ProjetilState.Inerte`) ate ela sair -- e o que apaga, na tela, a energia que chega nela (a `--diagv030` GK e a
	/// `--diagartedeki` 8 medem a tela).
	/// </summary>
	private void AGenkidamaSoSocaAoSairV30()
	{
		GD.Print("[v030] --- GS) a Genkidama so soca ao sair: nada de pose ao formar e crescer, e o soco do lancamento ate a trava soltar (adendo 15) ---");

		// UM CORREDOR SO, pras tres rodadas, e o ponteiro da varredura devolvido no fim: a Genkidama daqui nao anda nem estoura
		// (a fase 3 corre so pelo tique da tecnica), entao o chao fica como estava -- e a GD, que vem depois, precisa de corredores.
		int varreduraAntes = _pjProximoCorredor;
		Vec2 lugar = CorredorDeTerraFirme(24);

		(bool CresceuSemSoco, bool Lancou, string Detalhe) UmaGenkidama(string nome)
		{
			LimparTudoG12();
			ServerPlayer goku = ForjarG12(nome, lugar, bp: 50_000, skills: [PathSpiritBombG12]);
			goku.Facing = Facing.East;
			var gestos = new List<(int Quem, byte Gesto)>();
			EscutaDeGestos = gestos;
			bool Posando() => goku.GestoAte > NowMs();
			UsarHabilidade(goku, "SpiritBomb");
			if (!_genkidamaG12.TryGetValue(goku.Id, out EstadoDaGenkidamaG12? g)) { EscutaDeGestos = null; return (false, false, "a Genkidama nao comecou"); }
			bool ergueu = gestos.Exists(x => x.Quem == goku.Id && x.Gesto == (byte)Protocol.GestoDoCorpo.ErgueAGenkidama);
			bool socouCrescendo = Posando();
			bool naMao = true;
			double escala0 = g.Escala;

			// forma (3 s) e cresce dois pulsos: tique a tique, a pose e o que o snapshot diz da bola de agora
			for (int i = 0; i < TiquesDeG12(3.1) + TiquesDeG12(4.0); i++)
			{
				TiquesG12(1);
				socouCrescendo |= Posando();
				naMao &= g.Bola is { } b && NoSnapshotV30(goku, b.Id) is { Inerte: true };
			}
			bool cresceu = g.Escala > escala0 + 0.15;
			UsarHabilidade(goku, "SpiritBomb");   // pede o disparo: os 2 s da espera, e ela sai
			for (int i = 0; i < TiquesDeG12(2.5) && g.Fase < 3; i++)
			{
				socouCrescendo |= Posando();
				TickG12(Protocol.TickSeconds);
			}
			bool saiu = g.Fase == 3;
			bool socou = saiu && Posando();
			bool foraDaMao = saiu && g.Bola is { } lancada && NoSnapshotV30(goku, lancada.Id) is { Inerte: false };

			// a fase 3 inteira: a pose de pe ANTES de cada tique, junto da trava -- e no tique em que a trava solta, a pose sai
			bool segurou = saiu;
			for (int i = 0; i < TiquesDeG12(4) && _genkidamaG12.ContainsKey(goku.Id); i++)
			{
				segurou &= Posando() && PresoPeloG12(goku.Id);
				TickG12(Protocol.TickSeconds);
			}
			bool soltouJunto = !_genkidamaG12.ContainsKey(goku.Id) && !PresoPeloG12(goku.Id) && !Posando();
			EscutaDeGestos = null;
			return (cresceu && !socouCrescendo && naMao && ergueu, socou && segurou && soltouJunto && foraDaMao,
						$"cresceu {cresceu} (escala {g.Escala:0.0}); pose ao formar/crescer {socouCrescendo}; na mao no snapshot {naMao}; "
						+ $"som de erguer {ergueu}; saiu {saiu}; pose na saida {socou}; segurou a fase 3 com a trava {segurou}; "
						+ $"soltou junto {soltouJunto}; fora da mao no snapshot {foraDaMao}");
		}

		var certa = UmaGenkidama("Goku GS");
		AfirmarV30("**CRESCENDO, NADA DE SOCO** (*\"sempre q a genkidama aumenta o personagem faz animaçao de soco oq n deveria ser\"*): a pose de tiro nao arma ao formar nem a cada pulso -- a bola nasce NA MAO, e o snapshot diz isso ate ela sair; ao erguer, o som de erguer (`spiritbomb.wav`)",
				   certa.CresceuSemSoco, certa.Detalhe);
		AfirmarV30("**O SOCO E O DO LANCAMENTO, E FICA ATE A TRAVA SOLTAR** (*\"o personagem fica na animaçao de soco ate ele sair da paralisia da genkidama\"*): a pose arma na saida, fica de pe a fase 3 inteira junto da trava, e cai no tique em que ela solta -- e o snapshot passa a dizer que a bola saiu da mao",
				   certa.Lancou, certa.Detalhe);

		(bool CresceuSemSoco, bool Lancou, string Detalhe) socando, semSoco;
		GenkidamaSocaAoCrescerDeTeste = true;
		try { socando = UmaGenkidama("Goku GS que soca"); }
		finally { GenkidamaSocaAoCrescerDeTeste = false; }
		AfirmarV30("(defeito injetado: renascer com o gesto de tiro, o jogo de antes) a pose arma ao formar e a cada pulso -- a primeira regua REPROVA",
				   !socando.CresceuSemSoco, socando.Detalhe);
		GenkidamaLancaSemSocoDeTeste = true;
		try { semSoco = UmaGenkidama("Goku GS sem soco"); }
		finally { GenkidamaLancaSemSocoDeTeste = false; }
		AfirmarV30("(defeito injetado: o lancamento sem soco, o jogo de antes) nenhuma pose na saida -- a segunda regua REPROVA",
				   !semSoco.Lancou, semSoco.Detalhe);
		LimparTudoG12();
		_pjProximoCorredor = varreduraAntes;
	}

	// =====================================================================
	// KK) O KAIO-KEN (item 37)
	// =====================================================================
	private void OKaiokenV30()
	{
		GD.Print("[v030] --- KK) o Kaio-ken: a maestria na ficha lenta, o multiplicador escolhido, o X que apaga e o corpo acima da maestria (item 37) ---");
		LimparTudoDaBancada();

		// ---- A REGUA DO CORE: o pedido valido, o poder do degrau e o risco que a tela escreve ----
		AfirmarV30("o pedido vira um multiplicador valido: piso x2, o PISO do decimal (o `round()` do DM e piso) e teto x100",
				   KaiokenCore.Valido(1) == 2 && KaiokenCore.Valido(37.9) == 37 && KaiokenCore.Valido(500) == 100,
				   $"{KaiokenCore.Valido(1)} / {KaiokenCore.Valido(37.9)} / {KaiokenCore.Valido(500)}");
		AfirmarV30("...e o poder de cada degrau e a tabela do DM: x2 = 1,2x, x20 = 1,75x, x100 = 4x",
				   Math.Abs(KaiokenCore.Multiplicador(2) - 1.2) < 1e-9 && Math.Abs(KaiokenCore.Multiplicador(20) - 1.75) < 1e-9
				   && Math.Abs(KaiokenCore.Multiplicador(100) - 4) < 1e-9);
		AfirmarV30("o risco que a tela escreve, contra a maestria inicial (x4): x4 na maestria, x5 acima, x7 quebra no nocaute (> 1,6x), x9 quebra sem Ki (> 2x)",
				   KaiokenCore.RiscoDe(4, 4) == KaiokenCore.Risco.NaMaestria && KaiokenCore.RiscoDe(5, 4) == KaiokenCore.Risco.AcimaDaMaestria
				   && KaiokenCore.RiscoDe(7, 4) == KaiokenCore.Risco.QuebraNoNocaute && KaiokenCore.RiscoDe(9, 4) == KaiokenCore.Risco.QuebraSemKi);

		Vec2 c = TerraFirmeG12(4);
		ServerPlayer pl = ForjarKaiokenV30("Kaio", c);
		AfirmarV30("PREPARO: o corpo sabe o Kaio-ken pela skill da licao do Sr. Kaioh (`/datum/skill/kaioken`)", SabeTecnica(pl, "Kaioken"));

		// ---- (a) A MAESTRIA VAI NA FICHA LENTA ----
		Protocol.AtributosState? apagado = FichaLentaV30(pl);
		AfirmarV30("**A MAESTRIA CHEGA NA TELA** (*\"o kaioken ta sem maestria\"*): a ficha lenta leva a maestria (o piso x4 de quem acabou de aprender) e o Kaio-ken apagado",
				   apagado is { KaiokenAceso: false } a0 && Math.Abs(a0.KaiokenMaestria - KaiokenCore.MaestriaInicial) < 0.01,
				   $"maestria {apagado?.KaiokenMaestria} aceso {apagado?.KaiokenAceso}");

		// ---- (b) O MULTIPLICADOR ESCOLHIDO ----
		UsarHabilidade(pl, "Kaioken_7");   // o que a tela manda
		bool aceso7 = TemBuff(pl, BuffKaiokenG2) && _kaiokenAmtG2.GetValueOrDefault(pl.Id) == 7 && pl.Ficha.KaiokenVezes == 7;
		double kaioPcnt7 = pl.Ficha.KaioPcnt;
		Protocol.AtributosState? com7 = FichaLentaV30(pl);
		AfirmarV30("**O MULTIPLICADOR SE ESCOLHE** (*\"n ta dando pra escolher o multiplicador\"*): `Kaioken_7` -- o que a tela manda -- acende o x7 com o poder do degrau, e a ficha lenta diz aceso em x7",
				   aceso7 && Math.Abs(kaioPcnt7 - KaiokenCore.Multiplicador(7)) < 1e-6 && com7 is { KaiokenAceso: true, KaiokenVezes: 7 },
				   $"vezes {pl.Ficha.KaiokenVezes}; KaioPcnt {kaioPcnt7:0.###}; ficha aceso {com7?.KaiokenAceso} x{com7?.KaiokenVezes}");
		UsarHabilidade(pl, "Kaioken");     // aceso, o mesmo botao apaga
		bool apagou = !TemBuff(pl, BuffKaiokenG2);
		UsarHabilidade(pl, "Kaioken");     // apagado, o id nu acende no ultimo escolhido
		bool nuNo7 = TemBuff(pl, BuffKaiokenG2) && _kaiokenAmtG2.GetValueOrDefault(pl.Id) == 7;
		UsarHabilidade(pl, "Kaioken");
		ServerPlayer novato = ForjarKaiokenV30("Novato", c);
		UsarHabilidade(novato, "Kaioken");
		bool novatoNo2 = TemBuff(novato, BuffKaiokenG2) && _kaiokenAmtG2.GetValueOrDefault(novato.Id) == 2;
		UsarHabilidade(novato, "Kaioken");
		AfirmarV30("...aceso, o mesmo botao apaga; o id nu acende no ULTIMO escolhido (x7) -- e quem nunca escolheu, no x2 nativo",
				   apagou && nuNo7 && novatoNo2 && !TemBuff(novato, BuffKaiokenG2), $"apagou {apagou}; nu no 7 {nuNo7}; novato no 2 {novatoNo2}");
		AfirmarV30("...e o `Kaioken_Settings` (o menu de atalhos do DM) explica que a escolha e na tela",
				   Disse(ApertarEOuvir(pl, "Kaioken_Settings"), "tela dele"));

		// ---- (c) O X APAGA ----
		bool XApaga(bool defeito)
		{
			UsarHabilidade(pl, "Kaioken_5");
			pl.Ficha.Ki = Math.Min(pl.Ficha.Ki, pl.Ficha.MaxKi);   // acima de 100% o X so libera o excesso
			XNaoApagaOKaiokenDeTeste = defeito;
			try { ReverterOuLiberar(pl); }
			finally { XNaoApagaOKaiokenDeTeste = false; }
			bool apagado = !TemBuff(pl, BuffKaiokenG2) && !_kaiokenAmtG2.ContainsKey(pl.Id) && pl.Ficha.KaioPcnt <= 1.0001;
			if (TemBuff(pl, BuffKaiokenG2)) UsarHabilidade(pl, "Kaioken");
			return apagado;
		}
		AfirmarV30("**O X APAGA O KAIO-KEN** (*\"apertar X n ta saindo da forma\"*): a tecla do reverter desliga o Kaio-ken aceso -- o `Revert` do DM termina em `KaiokenRevert()`",
				   XApaga(defeito: false));
		AfirmarV30("(defeito injetado: o X que ignora o Kaio-ken, o jogo de antes) ele continua aceso -- a regua de cima REPROVA",
				   !XApaga(defeito: true));

		// ---- (d) ACIMA DA MAESTRIA: o que o DM faz, e o que a tela promete ----
		// O Ki que acaba (`Loop()`, kaioken.dm:199) despedaca quem passou do DOBRO da maestria; o nocaute com ele aceso
		// (`KaiokenRevert`, kaioken.dm:233), quem passou de 1,6x. Cada caso num corpo novo: o que estoura morre.
		(bool Despedacou, string Falas) Acima(int vezes, bool nocaute)
		{
			ServerPlayer k = ForjarKaiokenV30($"Kaio x{vezes}", c);
			UsarHabilidade(k, $"Kaioken_{vezes}");
			if (nocaute) k.Combate.Nocautear(60);
			else k.Ficha.Ki = 100 / Math.Max(k.Ficha.KiMod, 0.01);   // o piso de folego do `Loop()`
			double vida = k.Combate.Corpo.Vida();
			List<string> falas = Ouvir(TickTecnicasG2);
			bool despedacou = Disse(falas, "despedaça") && (EhCadaver(k) || k.Combate.Corpo.Vida() < vida * 0.2);
			if (TemBuff(k, BuffKaiokenG2)) UsarHabilidade(k, "Kaioken");
			return (despedacou, Ultimos(falas));
		}
		var semKi9 = Acima(9, nocaute: false);
		var semKi7 = Acima(7, nocaute: false);
		var caido7 = Acima(7, nocaute: true);
		var caido6 = Acima(6, nocaute: true);
		AfirmarV30("**ACIMA DA MAESTRIA, O QUE O DM FAZ E O QUE A TELA PROMETE** (*\"veja como funcionava usar kaioken acima da maestria no dm\"*): com a maestria x4, o Ki que acaba despedaca o x9 (> 2x) e so cansa o x7; o nocaute despedaca o x7 (> 1,6x) e nao o x6",
				   semKi9.Despedacou && !semKi7.Despedacou && caido7.Despedacou && !caido6.Despedacou,
				   $"sem Ki x9: {semKi9.Falas} || sem Ki x7: {semKi7.Falas} || caido x7: {caido7.Falas} || caido x6: {caido6.Falas}");

		// ---- (e) O DEUS NAO SUSTENTA O KAIO-KEN (adendo 13 do dono: *"o god n pode usar kaioken junto"*) ----
		AfirmarV30("a regra do Core (`Kaioken.CabeNaForma`): o Kaio-ken cabe na base, no Blue e no Rose, e NAO no SSG nem no SSG da linha Rose",
				   KaiokenCore.CabeNaForma(null) && KaiokenCore.CabeNaForma(Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdBase))
				   && KaiokenCore.CabeNaForma(Jandirus.Core.Forms.Catalogo.Def("blue")) && KaiokenCore.CabeNaForma(Jandirus.Core.Forms.Catalogo.Def("rose"))
				   && !KaiokenCore.CabeNaForma(Jandirus.Core.Forms.Catalogo.Def("ssg")) && !KaiokenCore.CabeNaForma(Jandirus.Core.Forms.Catalogo.Def("rose_ssg")));
		(bool Recusou, bool ApagouAoEntrar, bool NoBlue, string Detalhe) NoDeus(bool defeito)
		{
			KaiokenNoDeusDeTeste = defeito;
			try
			{
				// o botao no SSG: a forma escrita no estado (o que se mede e a regra do Kaio-ken, e nao o caminho ate o degrau)
				ServerPlayer deus = ForjarKaiokenV30("Deus KK", c);
				deus.Forma.Entrar("ssg");
				List<string> falas = ApertarEOuvir(deus, "Kaioken_5");
				bool recusou = !TemBuff(deus, BuffKaiokenG2) && Disse(falas, "God");
				if (TemBuff(deus, BuffKaiokenG2)) UsarHabilidade(deus, "Kaioken");

				// quem ja estava aceso e VIRA SSG: pelo funil de producao de toda transformacao
				ServerPlayer aceso = ForjarKaiokenV30("Aceso KK", c);
				UsarHabilidade(aceso, "Kaioken_3");
				bool estava = TemBuff(aceso, BuffKaiokenG2);
				EntrarNaForma(aceso, Jandirus.Core.Forms.Catalogo.Def("ssg")!, semCena: true);
				bool apagou = estava && !TemBuff(aceso, BuffKaiokenG2) && aceso.Ficha.KaioPcnt <= 1.0001;
				if (TemBuff(aceso, BuffKaiokenG2)) UsarHabilidade(aceso, "Kaioken");

				// e o Blue continua aceitando (o teto x20 dele e da familia de cima)
				ServerPlayer blue = ForjarKaiokenV30("Blue KK", c);
				blue.Forma.Entrar("blue");
				UsarHabilidade(blue, "Kaioken_5");
				bool noBlue = TemBuff(blue, BuffKaiokenG2);
				if (noBlue) UsarHabilidade(blue, "Kaioken");
				return (recusou, apagou, noBlue, $"falas no SSG: {Ultimos(falas)}; aceso antes de virar SSG {estava}");
			}
			finally { KaiokenNoDeusDeTeste = false; }
		}
		var noDeus = NoDeus(defeito: false);
		AfirmarV30("**O GOD NAO USA KAIO-KEN** (*\"o god n pode usar kaioken junto\"*): no SSG o botao recusa e diz por que; quem ja estava aceso e vira SSG perde o buff; e no Blue o Kaio-ken continua acendendo",
				   noDeus.Recusou && noDeus.ApagouAoEntrar && noDeus.NoBlue, noDeus.Detalhe);
		var noDeusComDefeito = NoDeus(defeito: true);
		AfirmarV30("(defeito injetado: o Kaio-ken no Deus, o jogo de antes) ele acende no SSG e atravessa a entrada nele -- a regua de cima REPROVA",
				   !noDeusComDefeito.Recusou && !noDeusComDefeito.ApagouAoEntrar, noDeusComDefeito.Detalhe);
		LimparTudoDaBancada();
	}

	// =====================================================================
	// KP) O KAIO-KEN PRESO NO SAVE (adendo 16, a v0.3.1)
	// =====================================================================
	/// <summary>
	/// O dono (2026-10-16): *"meu amigo entrou com a versao errada do jogo, antes das mudanças de aura e kaioken, e agora ele n
	/// consegue mais escolher o kaioken, e fica travado no kaioken times 6, faça com quem teve esse bug volte ao normal"*.
	///
	/// A CAUSA NAO ERA A VERSAO: o save grava a ficha inteira, com o numero do buff aceso nela, e o registro do buff e da sessao
	/// -- quem saia (ou caia) com o Kaio-ken de pe voltava com o `KaioPcnt` preso e sem buff (ver `GameServer.FichaSemOsBuffs`).
	/// Tres provas pela porta de producao, num disco de mentira (`PalcoDeApagamentos`, a pasta de saves nao e tocada):
	///   (a) o SAVE nao leva o que esta aceso -- o Kaio-ken e as tres maos do motor de buffs (soma, dreno, fator) -- e a ficha
	///       viva nao e tocada;
	///   (b) com a cura do login desligada, a ARMADILHA do amigo, do disco gravado como antes ate o botao que nunca abre a escolha;
	///   (c) o LOGIN da v0.3.1 cura esse mesmo disco: o Kaio-ken apagado, a escolha abrindo no x6 de sempre, e o x10 acendendo e
	///       apagando exato.
	/// O "login" e o funil de producao (`AccountStore.ParaJogador`) com o disco lido de novo, no MESMO corpo: a sessao nova nao
	/// tem registro de buff nenhum, e o botao do cliente segue a ficha lenta (`TelaDeKaioken.Pedir`: aceso, manda o id nu pra
	/// apagar; apagado, abre a escolha, que manda `Kaioken_N`).
	/// </summary>
	private void OKaiokenPresoNoSaveV30()
	{
		GD.Print("[v030] --- KP) o Kaio-ken preso no save: o disco nao leva o buff aceso, e o login cura quem ja ficou preso (adendo 16) ---");
		LimparTudoDaBancada();
		using PalcoDeApagamentos palco = PalcoDeApagamentosDeBancada();
		ServerPlayer pl = ForjarKaiokenV30("Preso KP", TerraFirmeG12(4));
		AccountSave acc = palco.Emprestar(pl.Conta, pl.Slot, AccountStore.DeJogador(pl, NowMs()));
		double x6 = KaiokenCore.Multiplicador(6);
		double tkioff = pl.Ficha.Tkioff, dreno = pl.Ficha.DrainMod, forms = pl.Ficha.formsBuff;
		static bool Perto(double a, double b) => Math.Abs(a - b) < 1e-9;

		// ---- (a) O SAVE NAO LEVA O QUE ESTA ACESO: o x6 do amigo, e um buff de bancada com a soma, o dreno e o fator ----
		(bool DiscoLimpo, bool VivaIntacta, string Detalhe) Gravar(bool defeito)
		{
			UsarHabilidade(pl, "Kaioken_6");
			LigarBuff(pl, "Bancada_KP", "buff de bancada", new() { ["Tkioff"] = 7 }, dreno: 3, fatores: new() { ["formsBuff"] = 2 });
			BuffVaiProDiscoDeTeste = defeito;
			try { Persistir(pl, acc); }
			finally { BuffVaiProDiscoDeTeste = false; }
			Jandirus.Core.Stats.Fighter? d = _store?.Carregar(pl.Conta)?.Slots[pl.Slot]?.Ficha;
			bool limpo = d != null && Perto(d.KaioPcnt, 1) && Perto(d.Tkioff, tkioff) && Perto(d.DrainMod, dreno) && Perto(d.formsBuff, forms);
			bool intacta = TemBuff(pl, BuffKaiokenG2) && Perto(pl.Ficha.KaioPcnt, x6) && Perto(pl.Ficha.Tkioff, tkioff + 7)
							   && Perto(pl.Ficha.DrainMod, dreno * 3) && Perto(pl.Ficha.formsBuff, forms * 2);
			UsarHabilidade(pl, "Kaioken");   // aceso, o botao apaga
			DesligarBuff(pl, "Bancada_KP");
			return (limpo, intacta, $"no disco: KaioPcnt {d?.KaioPcnt:0.###}, Tkioff {d?.Tkioff:0.###}, DrainMod {d?.DrainMod:0.###}, "
								   + $"formsBuff {d?.formsBuff:0.###} (sem buff: 1 / {tkioff:0.###} / {dreno:0.###} / {forms:0.###})");
		}
		var certo = Gravar(defeito: false);
		bool voltou = Perto(pl.Ficha.KaioPcnt, 1) && Perto(pl.Ficha.Tkioff, tkioff) && Perto(pl.Ficha.DrainMod, dreno) && Perto(pl.Ficha.formsBuff, forms);
		AfirmarV30("**O SAVE NAO LEVA O QUE ESTA ACESO**: com o Kaio-ken x6 e um buff de soma, dreno e fator de pe, o disco recebe a ficha SEM eles -- e a ficha viva segue acesa, e apagar devolve cada numero exato",
				   certo.DiscoLimpo && certo.VivaIntacta && voltou, $"{certo.Detalhe}; viva intacta {certo.VivaIntacta}; apagada voltou {voltou}");
		var comDefeito = Gravar(defeito: true);   // e este disco, gravado como antes, e o do amigo nas duas provas de baixo
		AfirmarV30("(defeito injetado: o save de antes, com a ficha inteira) o disco leva o x6 aceso e o buff -- a regua de cima REPROVA",
				   !comDefeito.DiscoLimpo, comDefeito.Detalhe);

		// ---- (b) e (c): o LOGIN, com o disco gravado aceso ----
		(bool Curado, bool Armadilha, string Detalhe) Relogar(bool comCura)
		{
			CharacterSave? s = _store?.Carregar(pl.Conta)?.Slots[pl.Slot];   // o disco, lido de novo: o login de verdade le dele
			if (s == null) return (false, false, "o disco nao tem o personagem");
			KaiokenAcesoAtravessaOLoginDeTeste = !comCura;
			try { AccountStore.ParaJogador(s, pl); }
			finally { KaiokenAcesoAtravessaOLoginDeTeste = false; }
			double noLogin = pl.Ficha.KaioPcnt;
			Protocol.AtributosState? f0 = FichaLentaV30(pl);
			bool abre = f0 is { KaiokenAceso: false, KaiokenVezes: 6 };
			UsarHabilidade(pl, abre ? "Kaioken_10" : "Kaioken");   // o botao: apagado abre a escolha (o x10); aceso manda o id nu
			double aceso = pl.Ficha.KaioPcnt;
			bool x10 = TemBuff(pl, BuffKaiokenG2) && Perto(aceso, KaiokenCore.Multiplicador(10));
			bool porCima = TemBuff(pl, BuffKaiokenG2) && Perto(aceso, 2 * x6 - 1);   // OUTRO x6 por cima do preso
			UsarHabilidade(pl, "Kaioken");   // aceso, o botao apaga
			Protocol.AtributosState? f1 = FichaLentaV30(pl);
			bool apagou = f1 is { KaiokenAceso: false } && Perto(pl.Ficha.KaioPcnt, 1);
			bool segueX6 = f1 is { KaiokenAceso: true, KaiokenVezes: 6 } && Perto(pl.Ficha.KaioPcnt, x6);
			return (abre && x10 && apagou, f0 is { KaiokenAceso: true } && porCima && segueX6,
						$"no login KaioPcnt {noLogin:0.###} (ficha lenta: aceso {f0?.KaiokenAceso}, x{f0?.KaiokenVezes}); o botao "
						+ $"{(abre ? "abriu a escolha e mandou Kaioken_10" : "mandou o id nu")} -> KaioPcnt {aceso:0.###}; apertando de novo -> "
						+ $"aceso {f1?.KaiokenAceso}, KaioPcnt {pl.Ficha.KaioPcnt:0.###}");
		}
		var preso = Relogar(comCura: false);
		AfirmarV30("(defeito injetado: o login de antes, sem a cura) o disco gravado aceso e a ARMADILHA do amigo: a ficha lenta diz aceso, o botao manda apagar e o servidor -- sem buff nenhum -- acende OUTRO x6 por cima; apertar de novo devolve o x6 preso, e a escolha nunca abre",
				   preso.Armadilha && !preso.Curado, preso.Detalhe);
		var curado = Relogar(comCura: true);
		AfirmarV30("**O LOGIN CURA QUEM JA FICOU PRESO** (*\"faça com quem teve esse bug volte ao normal\"*): o MESMO disco, pelo login da v0.3.1, volta com o Kaio-ken APAGADO -- a escolha abre no x6 de sempre, e o x10 acende e apaga exato",
				   curado.Curado, curado.Detalhe);
		LimparTudoDaBancada();
	}

	// =====================================================================
	// GD) A GENKIDAMA QUE ARRASTA E ESTOURA ENORME (item 41)
	// =====================================================================
	private void AGenkidamaQueArrastaV30()
	{
		GD.Print("[v030] --- GD) a Genkidama arrasta como um raio, estoura no fim com a cratera ENORME e nao moi num muro (item 41) ---");
		const int T = ZoneCollision.TileSize;

		AfirmarV30("o raio da explosao COLOSSAL: 10 tiles na escala 1 (a de uma bola comum, 4), crescendo com a escala ate o teto de 16",
				   KiContraCorpo.TilesDaExplosao(1, colossal: true) == KiContraCorpo.TilesDaExplosaoColossalDeEscala1
				   && KiContraCorpo.TilesDaExplosao(1, colossal: false) == KiContraCorpo.TilesDaExplosaoDeEscala1
				   && KiContraCorpo.TilesDaExplosao(5, colossal: true) == KiContraCorpo.TetoDeTilesDaExplosaoColossal,
				   $"{KiContraCorpo.TilesDaExplosao(1, colossal: true)} / {KiContraCorpo.TilesDaExplosao(1, colossal: false)} / {KiContraCorpo.TilesDaExplosao(5, colossal: true)}");

		// ---- (1) PRENSANDO ALGUEM NUM MURO ELA ESTOURA -- e com o defeito, moi ----
		(bool Explodiu, bool Viva, bool Levou, string Detalhe) PrensadaNoMuro(bool defeito)
		{
			LimparTudoG12();
			(Vec2 ini, float muro, bool achou) = CorredorAteOMuroV30(10);
			if (!achou) return (false, false, false, "nao achei corredor com muro");
			ServerPlayer goku = ForjarG12("Goku GD muro", ini, bp: 50_000, skills: [PathSpiritBombG12]);
			goku.Facing = Facing.East;
			Vec2 ondeEstava = ini + new Vec2(7 * T, 0);
			ServerPlayer fraco = ForjarV2("Fraco GD muro", ondeEstava, 1_000);
			ColossalMoiNoMuroDeTeste = defeito;
			try
			{
				Projetil? bomba = LancarAGenkidamaV30(goku);
				bool levou = false;
				for (int i = 0; i < TiquesDeG12(4) && bomba is { Vivo: true }; i++)
				{
					TiqueComEmpurraoV30();
					levou |= bomba.Arrastando == fraco.Id || (fraco.Pos - ondeEstava).Length > 1;
				}
				return (bomba is { Vivo: false, Fim: FimDeProjetil.Explodiu }, bomba is { Vivo: true }, levou,
						$"fim {bomba?.Fim}, viva {bomba?.Vivo}, levou {levou}; o corpo andou {fraco.Pos.X - ondeEstava.X:0} px (o muro a {muro - ondeEstava.X:0} px)");
			}
			finally { ColossalMoiNoMuroDeTeste = false; }
		}
		var noMuro = PrensadaNoMuro(defeito: false);
		AfirmarV30("**PRENSANDO ALGUEM NUM MURO ELA ESTOURA**: a Genkidama leva o mais fraco ate o muro e estoura ali -- o fim do caminho dela",
				   noMuro.Levou && noMuro.Explodiu, noMuro.Detalhe);
		var moendo = PrensadaNoMuro(defeito: true);
		AfirmarV30("(defeito injetado: a colossal que moi como um raio, o jogo de antes) prensada, ela fica moendo -- viva depois de quatro segundos: a regua de cima REPROVA",
				   moendo.Levou && moendo.Viva, moendo.Detalhe);

		// ---- (2) PELA PORTA DE PRODUCAO, EM CAMPO ABERTO: leva o mais fraco e estoura no fim, com a cratera ENORME ----
		LimparTudoG12();
		Vec2 c = CorredorDeTerraFirme(24);
		ServerPlayer gk = ForjarG12("Goku GD", c, bp: 50_000, skills: [PathSpiritBombG12]);
		gk.Facing = Facing.East;
		Vec2 deOnde = c + new Vec2(8 * T, 0);
		ServerPlayer alvo = ForjarV2("Fraco GD", deOnde, 1_000);
		int enormesAntes = CraterasEnormesDeTeste;
		Projetil? b = LancarAGenkidamaV30(gk);
		bool levado = false;
		float maisLonge = 0;
		for (int i = 0; i < TiquesDeG12(25) && b is { Vivo: true }; i++)
		{
			TiqueComEmpurraoV30();
			levado |= b.Arrastando == alvo.Id;
			maisLonge = Math.Max(maisLonge, (alvo.Pos - deOnde).Length);
		}
		AfirmarV30("**A GENKIDAMA ARRASTA COMO UM RAIO** (*\"genkidama deveria arrastar assim como um beam\"*): ela pega o mais fraco (oito tiles a frente) e o LEVA -- mais de tres tiles",
				   levado && maisLonge > 3 * T, $"levado {levado}; o corpo foi a {maisLonge / T:0.0} tiles de onde estava");
		AfirmarV30("...e ESTOURA NO FIM (*\"e explodir no final e ter uma cratera ENORME\"*): o tiro acaba em explosao, com a cratera ENORME (o decalque proprio da colossal)",
				   b is { Vivo: false, Fim: FimDeProjetil.Explodiu } && CraterasEnormesDeTeste == enormesAntes + 1,
				   $"fim {b?.Fim}; crateras enormes {enormesAntes} -> {CraterasEnormesDeTeste}");

		// ---- (3) A AREA: o estouro colossal alcanca quem esta a 7 tiles; o de uma bola comum, nao ----
		LimparTudoG12();
		Vec2 praca = PracaLivre();
		ServerPlayer atira = ForjarV2("Atira GD", praca, 50_000);
		double Ferida(bool colossal)
		{
			ServerPlayer vizinha = ForjarV2(colossal ? "Vizinha da colossal" : "Vizinha da comum", praca + new Vec2(0, 7 * T), 1_000);
			double antes = vizinha.Combate.Corpo.Vida();
			Projetil p = Disparar(atira, new ReceitaDeProjetil
			{
				Tipo = TipoDeProjetil.Blast, BaseDano = 30, Velocidade = 3, AlcanceTiles = 30, Deflectivel = false,
				Colossal = colossal, Nome = "a bola da bancada",
			}, rumoDado: Vec2.Zero, deOnde: praca);
			Estourar(p, atira.Zone, Espaco.EhPlaneta(atira.Zone), praca);
			Matar(p, FimDeProjetil.Nenhum);
			double ferida = antes - vizinha.Combate.Corpo.Vida();
			LimparTudoDaBancada(manter: [atira]);
			return ferida;
		}
		double daColossal = Ferida(colossal: true);
		double daComum = Ferida(colossal: false);
		AfirmarV30("**O ESTOURO E ENORME TAMBEM NO CORPO**: quem esta a 7 tiles do centro apanha do estouro da Genkidama -- e o de uma bola comum do mesmo tamanho (4 tiles) nem chega nela",
				   daColossal > 0 && daComum <= 1e-9, $"da colossal {daColossal:0.###}; da comum {daComum:0.###}");
		LimparTudoG12();
	}

	// =====================================================================
	// AS FERRAMENTAS DAS QUATRO
	// =====================================================================
	/// <summary>Um tique do servidor pros tiros que levam gente: a tecnica, o prazo do empurrao, e o avanco dos tiros.</summary>
	private void TiqueComEmpurraoV30()
	{
		TickG12(Protocol.TickSeconds);
		TickDoEmpurrao();
		TickDosProjeteis(Protocol.TickSeconds);
	}

	/// <summary>
	/// A GENKIDAMA INTEIRA PELA PORTA DE PRODUCAO: aperta (ela se forma), aperta de novo (pede o disparo) e anda so o tique
	/// da tecnica ate ela sair -- devolve a bola NO LUGAR em que nasceu, antes do primeiro passo. Nula se ela nao saiu.
	/// </summary>
	private Projetil? LancarAGenkidamaV30(ServerPlayer pl)
	{
		UsarHabilidade(pl, "SpiritBomb");
		TiquesG12(TiquesDeG12(3.1));   // a esfera se forma
		UsarHabilidade(pl, "SpiritBomb");
		if (!_genkidamaG12.TryGetValue(pl.Id, out EstadoDaGenkidamaG12? g)) return null;
		for (int i = 0; i < TiquesDeG12(2.5) && g.Fase < 3; i++) TickG12(Protocol.TickSeconds);
		return g.Fase == 3 ? g.Bola : null;
	}

	/// <summary>
	/// UMA FILEIRA DE `livres` CELULAS LIVRES (terra firme, longe da borda, com as vizinhas de cima e de baixo livres) QUE
	/// TERMINA NUM MURO. Devolve o centro da primeira celula e o X da beirada do muro.
	/// </summary>
	private (Vec2 Inicio, float BeiradaDoMuro, bool Achou) CorredorAteOMuroV30(int livres)
	{
		const int T = ZoneCollision.TileSize;
		if (_pjMapa is not { } mapa) return (CorredorLivre(livres), 0, false);
		for (int y = Math.Max(_pjProximoCorredor, 2); y < mapa.Height - 2; y++)
			for (int x = 4; x < mapa.Width - livres - 2; x++)
			{
				bool ok = mapa.BlockedCell(x + livres, y) && !mapa.NaBorda(x + livres, y);
				for (int i = -1; i < livres && ok; i++)
					for (int dy = -1; dy <= 1 && ok; dy++)
						ok &= !mapa.BlockedCell(x + i, y + dy) && !mapa.EhAgua(x + i, y + dy) && !mapa.NaBorda(x + i, y + dy);
				if (!ok) continue;
				_pjProximoCorredor = y + 3;
				return (new Vec2(x * T + T / 2f, y * T + T / 2f), (x + livres) * T, true);
			}
		return (CorredorLivre(livres), 0, false);
	}

	// =====================================================================
	// DS) A DEATH BALL SO SOCA AO SAIR (adendo 18, a v0.3.2)
	// =====================================================================
	/// <summary>
	/// O dono (2026-10-16): *"death ball corrija os problemas q ele tem igual a genkidama tinha tb"*. Ela renascia a cada estagio
	/// pelo gesto de tiro inteiro (o soco e o `Blast.wav` da saida a cada estagio) e nao fazia nada no lancamento. Pela tecla de
	/// producao: ao erguer e a cada estagio NENHUMA pose (a bola nasce na mao, e o snapshot diz isso ate ela sair), os sons de
	/// erguer e de crescer pelos gestos, e o soco do lancamento de pe a guia inteira junto da trava, caindo com ela no aperto que
	/// larga a guia. Sem guia (o segundo aperto, na carga), o soco e o de toda bola e a trava nao fica.
	/// </summary>
	private void ADeathBallSoSocaAoSairV30()
	{
		GD.Print("[v030] --- DS) a Death Ball so soca ao sair: nada de pose ao erguer e a cada estagio, e o soco do lancamento ate a guia acabar (adendo 18) ---");

		// UM CORREDOR SO pras rodadas, e o ponteiro da varredura devolvido no fim: a GD, que vem depois, precisa de corredores.
		int varreduraAntes = _pjProximoCorredor;
		Vec2 lugar = CorredorDeTerraFirme(24);

		(bool CarregouSemSoco, bool Lancou, string Detalhe) UmaDeathBall(string nome, bool semGuia)
		{
			LimparTudoG12();
			ServerPlayer freeza = ForjarG12(nome, lugar, bp: 50_000, skills: [PathDeathBallG12]);
			freeza.Facing = Facing.East;
			var gestos = new List<(int Quem, byte Gesto)>();
			EscutaDeGestos = gestos;
			bool Posando() => freeza.GestoAte > NowMs();
			int Gestos(Protocol.GestoDoCorpo g) => gestos.Count(x => x.Quem == freeza.Id && x.Gesto == (byte)g);
			try
			{
				UsarHabilidade(freeza, "Death_Ball");
				if (!_deathBallG12.TryGetValue(freeza.Id, out EstadoDaDeathBallG12? db)) return (false, false, "a Death Ball nao comecou");
				if (semGuia) UsarHabilidade(freeza, "Death_Ball");   // o segundo aperto, na carga: larga a guia
				bool socouCarregando = Posando();
				bool naMao = true;
				// a carga inteira, tique a tique: os quatro estagios (1,5 s cada) e o lancamento no fim
				for (int i = 0; i < TiquesDeG12(5.5) && !db.Lancada; i++)
				{
					naMao &= db.Bola is { } b && NoSnapshotV30(freeza, b.Id) is { Inerte: true };
					TiquesG12(1);
					socouCarregando |= Posando() && !db.Lancada;
				}
				bool ergueu = Gestos(Protocol.GestoDoCorpo.ErgueADeathBall) == 1;
				int cresceu = Gestos(Protocol.GestoDoCorpo.CresceADeathBall);
				bool lancou = db.Lancada && db.Estagio == 4;
				bool socou = lancou && Posando();
				bool foraDaMao = lancou && db.Bola is { } solta && NoSnapshotV30(freeza, solta.Id) is { Inerte: false };
				bool carregou = !socouCarregando && naMao && ergueu && cresceu == 3;
				string det = $"pose na carga {socouCarregando}; na mao no snapshot {naMao}; erguer {ergueu}; cresceu {cresceu}x; lancou {lancou}; "
								+ $"pose na saida {socou}; fora da mao {foraDaMao}";

				if (semGuia)
				{
					bool soltou = !_deathBallG12.ContainsKey(freeza.Id) && !PresoPeloG12(freeza.Id);
					return (carregou, socou && soltou && foraDaMao, det + $"; sem guia a trava soltou {soltou}");
				}

				// a guia, menos de um segundo: o soco de pe junto da trava
				bool segurou = lancou;
				for (int i = 0; i < TiquesDeG12(0.8) && _deathBallG12.ContainsKey(freeza.Id); i++)
				{
					TiquesG12(1);
					segurou &= Posando() && PresoPeloG12(freeza.Id);
				}
				bool guiando = _deathBallG12.ContainsKey(freeza.Id);
				if (guiando) UsarHabilidade(freeza, "Death_Ball");   // o aperto que larga a guia
				bool soltouJunto = guiando && !_deathBallG12.ContainsKey(freeza.Id) && !PresoPeloG12(freeza.Id) && !Posando();
				return (carregou, socou && segurou && soltouJunto && foraDaMao,
							det + $"; segurou a guia com a trava {segurou}; a guia durou {guiando}; soltou junto {soltouJunto}");
			}
			finally { EscutaDeGestos = null; }
		}

		var certa = UmaDeathBall("Freeza DS", semGuia: false);
		AfirmarV30("**ERGUENDO E CRESCENDO, NADA DE SOCO** (*\"death ball corrija os problemas q ele tem igual a genkidama tinha tb\"*): a pose de tiro nao arma ao erguer nem a cada estagio -- a bola nasce NA MAO, e o snapshot diz isso ate ela sair; ao erguer, o som de erguer (`deathball_charge`), e a cada um dos tres estagios o de crescer (`chargeincrease.ogg`)",
					   certa.CarregouSemSoco, certa.Detalhe);
		AfirmarV30("**O SOCO E O DO LANCAMENTO, E FICA A GUIA INTEIRA**: a pose arma na saida, fica de pe a guia inteira junto da trava, e cai no aperto que larga a guia -- e o snapshot passa a dizer que a bola saiu da mao",
					   certa.Lancou, certa.Detalhe);
		var semGuia = UmaDeathBall("Freeza DS sem guia", semGuia: true);
		AfirmarV30("...SEM GUIA (o segundo aperto, na carga), o soco e o de toda bola na saida, e a trava nao fica",
					   semGuia.CarregouSemSoco && semGuia.Lancou, semGuia.Detalhe);

		(bool CarregouSemSoco, bool Lancou, string Detalhe) socando, semSoco;
		DeathBallSocaAoCrescerDeTeste = true;
		try { socando = UmaDeathBall("Freeza DS que soca", semGuia: false); }
		finally { DeathBallSocaAoCrescerDeTeste = false; }
		AfirmarV30("(defeito injetado: renascer com o gesto de tiro, o jogo de antes) a pose arma ao erguer e a cada estagio -- a primeira regua REPROVA",
					   !socando.CarregouSemSoco, socando.Detalhe);
		DeathBallLancaSemSocoDeTeste = true;
		try { semSoco = UmaDeathBall("Freeza DS sem soco", semGuia: false); }
		finally { DeathBallLancaSemSocoDeTeste = false; }
		AfirmarV30("(defeito injetado: o lancamento sem soco, o jogo de antes) nenhuma pose na saida nem na guia -- a segunda regua REPROVA",
					   !semSoco.Lancou, semSoco.Detalhe);
		LimparTudoG12();
		_pjProximoCorredor = varreduraAntes;
	}

	// =====================================================================
	// MZ) O MORTO DE PE LUTA COMO O VIVO (adendo 18, a v0.3.2)
	// =====================================================================
	/// <summary>
	/// O dono (2026-10-16): *"n tem zanzoclash nem colisao morto? deveria ter, unica restriçao de morto deveria ser zenkai o resto
	/// todo deveria ficar"*. Dois mortos de pe no INFERNO (onde ele estava), pelas portas de producao: o Zanzo Clash que abre e
	/// SOBREVIVE ao tique (caia no seguinte), o preso que se debate e sente o aperto, o mestre que convida o aluno, a Danca que nao
	/// recusa por "caido" e o estilo que rende -- cada um com o jogo de antes ao lado (`MortoComoCadaverDeTeste`). E a COLISAO: dois
	/// mortos com o passe do Enma (o mesmo `MortoDePe`) num corredor de terra firme, o beam batendo na guarda de um igual -- o embate
	/// de maos, que pedia a guarda que o morto nao erguia (o defeito injetado e o `MortoNaoBloqueiaDeTeste`).
	/// </summary>
	private void OMortoLutaComoOVivoV30()
	{
		GD.Print("[v030] --- MZ) o morto de pe luta como o vivo: Zanzo Clash, agarrao, mestre, Danca, estilo e a colisao de beam na guarda (adendo 18) ---");
		const float T = ZoneCollision.TileSize;
		LimparTudoDaBancada();
		ZoneKey inferno = ZoneKey.Premade(Alem.ZonaDoInferno);
		Vec2 portao = PortaoDoInferno(inferno);
		ServerPlayer a = ForjarV2("Fantasma A", TerraFirmeG12(4), 50_000);
		ServerPlayer d = ForjarV2("Fantasma D", TerraFirmeG12(4), 50_000);
		// A ASSINATURA E A CONTA + O SLOT: mestre e aluno precisam de duas (o `ForjarV2` da a mesma pra todo corpo)
		a.Conta = "bancada_mz_mestre";
		d.Conta = "bancada_mz_aluno";
		foreach (ServerPlayer p in new[] { a, d })
		{
			MoveToZone(p.Id, inferno, portao);
			p.Ficha.dead = true;
			p.Ficha.KO = false;
			p.MorteJaViajou = true;
			p.RelogioDaMorte = long.MaxValue;
			p.Combate.Carencia = 0;
		}
		void Colados()
		{
			// O VOO PENDENTE TERMINA ANTES de os dois irem pro lugar: o golpe de saida do Zanzo Clash empurra o perdedor, e o
			// `ProntoParaOEmbateDaMudez` roda esse empurrao ate o fim -- depois do posicionamento, ele o levava pra longe. E a CARENCIA
			// tambem: o mesmo golpe deixa o perdedor intocavel um instante, e o agarrao nao pega intocavel.
			foreach (ServerPlayer p in new[] { a, d }) { ProntoParaOEmbateDaMudez(p); p.Ficha.Ki = p.Ficha.MaxKi; p.Combate.Carencia = 0; }
			a.Pos = portao;
			d.Pos = portao + new Vec2(T * 0.9f, 0);
			a.Facing = Facing.East;
			d.Facing = Facing.West;
		}
		Colados();
		AfirmarV30("(montagem) os dois MORTOS DE PE no Inferno, colados e de frente -- a cena da queixa",
					   a.Zone.Equals(inferno) && d.Zone.Equals(inferno) && a.MortoDePe && d.MortoDePe && !EhCadaver(a) && !EhCadaver(d)
					   && CorpoNaFrente(a) == d, $"zona {a.Zone.Name}/{d.Zone.Name}; de pe {a.MortoDePe}/{d.MortoDePe}; na frente {CorpoNaFrente(a)?.Name}");

		// ---- (1) O ZANZO CLASH: abre E SOBREVIVE ao tique ----
		(bool Abriu, bool Durou) UmEmbate()
		{
			Colados();
			a.Livro.Dar(PathDoZanzoken);
			d.Livro.Dar(PathDoZanzoken);
			d.UltimoAlvo = a.Id;   // a troca: o outro acabou de me bater
			d.UltimoSocoMs = NowMs();
			bool sorteio = _clashSempre;
			_clashSempre = true;
			try
			{
				bool abriu = TentarEmbate(a, d);
				TickDosEmbates();
				return (abriu, _emEmbate.ContainsKey(a.Id) && _emEmbate.ContainsKey(d.Id));
			}
			finally { _clashSempre = sorteio; SoltarDoEmbate(a.Id); SoltarDoEmbate(d.Id); }
		}

		// ---- (2) O AGARRAO: o preso se debate e o pescoco aperta ----
		string porQueNaoPegou = "";
		(bool Pegou, bool Debateu, bool Apertou) UmAgarrao()
		{
			Colados();
			List<string> disse = Ouvir(() => AlternarAgarrao(a));
			porQueNaoPegou = "";
			if (a.AgarrandoId != d.Id)
			{
				// O PORQUE, NA LINHA DA FALHA: a frase do servidor e o estado dos dois
				porQueNaoPegou = $"| \"{Ultimos(disse)}\"; segura {a.AgarrandoId} ({a.ModoDoAgarrao}); mexe {PodeMexerOCorpo(a)}; na frente "
								   + $"{CorpoNaFrente(a)?.Name ?? "ninguem"}; zonas {a.Zone.Name}/{d.Zone.Name}; dist {(d.Pos - a.Pos).Length:0.#}; preso por "
								   + $"{d.AgarradoPorId}; intocavel {d.Combate.Intocavel}; KO {a.Ficha.KO}/{d.Ficha.KO}; cena {EmCena(a)}; paralisado {Paralisado(a.Id)}";
				return (false, false, false);
			}
			try
			{
				a.Estrangulando = true;
				d.DebatendoSe = true;
				double vida = d.Combate.Corpo.Vida();
				LutaPraEscapar(a, d);
				return (true, d.ContadorDaLuta > 0 || a.AgarrandoId == 0, d.Combate.Corpo.Vida() < vida);
			}
			finally { if (a.AgarrandoId != 0) Soltar(a, MotivoDaSoltura.Tecla); a.Estrangulando = false; }
		}

		// ---- (3) O MESTRE CONVIDA O ALUNO ----
		bool Convidou()
		{
			Colados();
			a.Ficha.BP = 1_000_000;
			d.Ficha.BP = 1_000;
			d.PedidoDoMestre = null;
			try { ConvidarAluno(a); return d.PedidoDoMestre != null; }
			finally { d.PedidoDoMestre = null; a.Ficha.BP = d.Ficha.BP = 50_000; }
		}

		// ---- (4) A DANCA NAO RECUSA POR "CAIDO" (as outras recusas, a skill e o poder, nao sao desta familia) ----
		Jandirus.Core.Social.RecusaDeFusao Danca()
		{
			Colados();
			return AvaliarOConvite(a, d, Jandirus.Core.Social.TipoDeFusao.Danca, NowMs());
		}

		// ---- (5) O ESTILO RENDE ----
		Estilo? estilo = Estilos.Todos.FirstOrDefault();
		bool Treinou()
		{
			if (estilo == null) return false;
			d.Ficha.EstiloAtual = estilo.Id;
			_bufferDeEstilo[d.Id] = 0;
			TickDosEstilos();
			return _bufferDeEstilo.GetValueOrDefault(d.Id) > 0;
		}

		var embate = UmEmbate();
		var agarrao = UmAgarrao();
		string porQueAgarrao = porQueNaoPegou;
		bool convidou = Convidou();
		var danca = Danca();
		bool treinou = Treinou();
		(bool Abriu, bool Durou) embateDeAntes;
		(bool Pegou, bool Debateu, bool Apertou) agarraoDeAntes;
		string porQueAgarraoDeAntes = "";
		bool convidouDeAntes, treinouDeAntes;
		Jandirus.Core.Social.RecusaDeFusao dancaDeAntes;
		MortoComoCadaverDeTeste = true;
		try
		{
			embateDeAntes = UmEmbate();
			agarraoDeAntes = UmAgarrao();
			porQueAgarraoDeAntes = porQueNaoPegou;
			convidouDeAntes = Convidou();
			dancaDeAntes = Danca();
			treinouDeAntes = Treinou();
		}
		finally { MortoComoCadaverDeTeste = false; }

		AfirmarV30("**O MORTO DE PE TEM ZANZO CLASH** (*\"n tem zanzoclash nem colisao morto? deveria ter\"*): no Inferno, o embate de dois mortos abre E SOBREVIVE ao tique -- o `dead` cru o derrubava no tique seguinte",
					   embate.Abriu && embate.Durou, $"abriu {embate.Abriu}; durou {embate.Durou}");
		AfirmarV30("(defeito injetado: o `dead` cru de antes) o embate abre e CAI no tique -- a regua de cima REPROVA",
					   embateDeAntes.Abriu && !embateDeAntes.Durou, $"abriu {embateDeAntes.Abriu}; durou {embateDeAntes.Durou}");
		AfirmarV30("**O MORTO PRESO SE DEBATE E SENTE O APERTO**: o agarrao pega o fantasma, ele se debate (o contador anda) e o pescoco dele aperta",
					   agarrao.Pegou && agarrao.Debateu && agarrao.Apertou, $"pegou {agarrao.Pegou}; debateu {agarrao.Debateu}; apertou {agarrao.Apertou} {porQueAgarrao}");
		AfirmarV30("(defeito injetado: o `dead` cru de antes) o fantasma preso nao se debate nem sente o aperto -- a regua de cima REPROVA",
					   agarraoDeAntes.Pegou && !agarraoDeAntes.Debateu && !agarraoDeAntes.Apertou,
					   $"pegou {agarraoDeAntes.Pegou}; debateu {agarraoDeAntes.Debateu}; apertou {agarraoDeAntes.Apertou} {porQueAgarraoDeAntes}");
		AfirmarV30("**MESTRE E ALUNO MORTOS**: o mestre morto convida o aluno morto (o convite chega), a Danca nao recusa por \"caido\" e o estilo do morto rende",
					   convidou && danca != Jandirus.Core.Social.RecusaDeFusao.Caido && treinou,
					   $"convidou {convidou}; danca {danca}; estilo {estilo?.Id ?? "(nenhum)"} rendeu {treinou}");
		AfirmarV30("(defeito injetado: o `dead` cru de antes) \"nao da, com alguem caido\": nada de convite, a Danca recusa por caido, o estilo nao rende -- a regua de cima REPROVA",
					   !convidouDeAntes && dancaDeAntes == Jandirus.Core.Social.RecusaDeFusao.Caido && !treinouDeAntes,
					   $"convidou {convidouDeAntes}; danca {dancaDeAntes}; rendeu {treinouDeAntes}");

		// ---- (7) (2026-10-17) LARGAR E PEGAR DO CHAO -- o dono: *"morto n ta conseguindo dropar itens nem zeni nem nada"* ----
		// O fantasma larga 100 zeni e uma maca pelos verbos de producao (`chao_largar`, `item_largar`) e cata os dois de volta
		// (`chao_pegar`). No jogo de antes as duas portas liam o `dead` cru: "caído, você não larga nada." e "caído, você não pega
		// nada.". Se o largar e recusado, o monte vai pro chao pelo funil (`PorNoChao`) e o pegar e medido do mesmo jeito.
		(bool Largou, double Zeni, int Macas, string Disse) LargaEPega()
		{
			Colados();
			const string maca = Jandirus.Core.Items.CatalogoDeItens.Maca;
			LargadosDe(a.Zone.Hash).Clear();
			a.Ficha.Zeni = 1_000;
			a.Mochila.Tirar(maca, a.Mochila.Quantos(maca));
			Guardar(a, maca);
			List<string> disse = Ouvir(() =>
			{
				ComandoDoChao(a, Jandirus.Core.Items.Chao.VerboLargar, $"{Jandirus.Core.Economia.Recurso.PalavraDeZeni}/100");
				ComandoDeItem(a, "item_largar", maca);
			});
			bool largou = a.Ficha.Zeni == 900 && a.Mochila.Quantos(maca) == 0;
			if (!largou)
			{
				a.Ficha.Zeni = 900;
				PorNoChao(a.Zone, a.Pos, Jandirus.Core.Items.TipoDeLargado.Zeni, "", 100);
			}
			disse.AddRange(Ouvir(() =>
			{
				ComandoDoChao(a, Jandirus.Core.Items.Chao.VerboPegar, "");
				ComandoDoChao(a, Jandirus.Core.Items.Chao.VerboPegar, "");
			}));
			LargadosDe(a.Zone.Hash).Clear();
			return (largou, a.Ficha.Zeni, a.Mochila.Quantos(maca), Ultimos(disse));
		}
		var noChao = LargaEPega();
		(bool Largou, double Zeni, int Macas, string Disse) noChaoDeAntes;
		MortoComoCadaverDeTeste = true;
		try { noChaoDeAntes = LargaEPega(); }
		finally { MortoComoCadaverDeTeste = false; }
		AfirmarV30("**O MORTO DE PE LARGA E PEGA DO CHAO** (*\"morto n ta conseguindo dropar itens nem zeni nem nada\"*): no Inferno o fantasma larga 100 zeni e uma maca, e cata os dois de volta",
						   noChao.Largou && noChao.Zeni == 1_000 && noChao.Macas == 1, $"largou {noChao.Largou}; zeni {noChao.Zeni:0}; macas {noChao.Macas} | {noChao.Disse}");
		AfirmarV30("(defeito injetado: o `dead` cru de antes) \"caído, você não larga nada.\" e \"caído, você não pega nada.\" -- a regua de cima REPROVA",
						   !noChaoDeAntes.Largou && noChaoDeAntes.Zeni == 900 && noChaoDeAntes.Disse.Contains("não larga") && noChaoDeAntes.Disse.Contains("não pega"),
						   $"largou {noChaoDeAntes.Largou}; zeni {noChaoDeAntes.Zeni:0} | {noChaoDeAntes.Disse}");

		// ---- (6) A COLISAO: o beam na guarda de um morto vira o embate das maos ----
		(bool Embate, string Detalhe) RaioNaGuardaDoMorto()
		{
			LimparEmbatesDaBancada();
			Vec2 raia = CorredorDeTerraFirme(12);
			ServerPlayer atira = ForjarV2("Fantasma do beam", raia, 5_000);
			ServerPlayer guarda = ForjarV2("Fantasma de guarda", raia + new Vec2(8 * T, 0), 5_000);
			foreach (ServerPlayer p in new[] { atira, guarda })
			{
				p.Ficha.dead = true;
				p.MorteJaViajou = true;
				p.RelogioDaMorte = long.MaxValue;
				p.Ficha.passe_do_enma_ate = NowMs() + 3_600_000;   // o passe do Enma: de pe entre os vivos (o mesmo `MortoDePe`)
				p.Combate.Carencia = 0;
				p.Ficha.Ki = p.Ficha.MaxKi;
			}
			atira.Facing = Facing.East;
			guarda.Facing = Facing.West;
			ErguerAGuarda(guarda, true);   // a porta do ALT
			for (int i = 0; i < 30 && guarda.Combate.Bloqueando && guarda.Combate.TempoDeGuarda <= MeleeResolver.JanelaContra; i++)
				guarda.Combate.Tick(Protocol.TickSeconds);
			bool deGuarda = guarda.Combate.Bloqueando;
			Canalizar(atira, "Ki_Wave", 10 * atira.Ficha.BaseDrain(), QueFereUmIgual(SemDeflexao(), atira, deGuarda: true));
			bool embate = false;
			for (int i = 0; i < 30 * 3 && !embate; i++)
			{
				TickDosCanaisDeKi(Protocol.TickSeconds);
				TickDosProjeteis(Protocol.TickSeconds);
				TickDosEmbatesDeKi(Protocol.TickSeconds);
				embate = _emEmbateDeKi.ContainsKey(guarda.Id);
			}
			bool dePe = atira.MortoDePe && guarda.MortoDePe && !EhCadaver(atira) && !EhCadaver(guarda);
			LimparEmbatesDaBancada();
			return (embate && dePe, $"de pe {dePe}; guarda erguida {deGuarda}; embate {embate}");
		}
		var colisao = RaioNaGuardaDoMorto();
		(bool Embate, string Detalhe) colisaoDeAntes;
		CombatState.MortoNaoBloqueiaDeTeste = true;
		try { colisaoDeAntes = RaioNaGuardaDoMorto(); }
		finally { CombatState.MortoNaoBloqueiaDeTeste = false; }
		AfirmarV30("**A COLISAO DO MORTO**: dois mortos de pe (o passe do Enma, o mesmo `MortoDePe` do Outro Mundo), o beam batendo na guarda erguida de um igual -- vira o embate das maos",
					   colisao.Embate, colisao.Detalhe);
		AfirmarV30("(defeito injetado: o morto sem guarda, o jogo de antes) a guarda nao sobe e o beam so acerta -- nada de colisao, a regua de cima REPROVA",
					   !colisaoDeAntes.Embate, colisaoDeAntes.Detalhe);
		LimparTudoDaBancada();
	}

	// =====================================================================
	// IT) A PASSAGEM DO MORTO PELA TRANSMISSAO (adendo 18, a v0.3.2)
	// =====================================================================
	/// <summary>
	/// O dono (2026-10-16): *"caso um player morto use instante transmission pra alguem q ta vivo, ele aparece meio transparente e fica
	/// 5 segundos no mundo dos vivos dps voltando pro outro mundo"*. O morto no Outro Mundo pede a Transmissao (a porta de producao,
	/// `TeletransporteG11`), a concentracao chega no tique dela, e ele aparece ao lado do vivo na Terra: DE PE (anda, nao e cadaver,
	/// o bit da ficha), com a aureola e o bit da passagem no pacote dela (o desenho meio transparente), e o disco gravaria a VOLTA.
	/// Vencidos os 5 s, a alma volta pro lugar exato de onde saiu; caindo no meio, a mesa do Enma.
	/// </summary>
	private void APassagemDoMortoV30()
	{
		GD.Print("[v030] --- IT) o morto transmite pra um vivo: chega de pe e meio transparente, e em 5 s a alma volta (adendo 18) ---");
		const float T = ZoneCollision.TileSize;
		LimparTudoDaBancada();
		_transmissaoG11.Clear();
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		Vec2 mesa = MesaDoEnma(alem);
		Vec2 naTerra = TerraFirmeG12(4);
		ServerPlayer vivo = ForjarV2("Kuririn IT", naTerra, 500_000);
		vivo.Conta = "bancada_it_vivo";
		ServerPlayer morto = ForjarV2("Goku IT", naTerra + new Vec2(T * 6, 0), 50_000);
		morto.Conta = "bancada_it_morto";
		Vec2 deOnde = mesa + new Vec2(T * 2, 0);

		(bool Concentrou, bool Chegou, string Detalhe) Transmitir()
		{
			MoveToZone(morto.Id, alem, deOnde);
			deOnde = morto.Pos;
			morto.Ficha.dead = true;
			morto.Ficha.KO = false;
			morto.MorteJaViajou = true;
			morto.RelogioDaMorte = long.MaxValue;
			morto.PassagemAte = 0;
			morto.Combate.Carencia = 0;
			morto.Ficha.teleskill = 100;   // a espera de 1,5 s (`max(600/teleskill, 15)` decimos)
			morto.Ficha.Ki = morto.Ficha.MaxKi;
			List<string> pedido = Ouvir(() => TeletransporteG11(morto, vivo.Name));
			if (!_transmissaoG11.TryGetValue(morto.Id, out TransmissaoG11? tr)) return (false, false, Ultimos(pedido));
			tr.QuandoMs = 0;   // o relogio de parede nao anda na bancada: a concentracao vence agora
			List<string> chegada = Ouvir(() => TickDaTransmissaoG11(morto, tr, NowMs()));
			bool chegou = morto.Zone.Equals(vivo.Zone) && (morto.Pos - vivo.Pos).Length <= 2 * T;
			return (true, chegou, $"{Ultimos(pedido)} / {Ultimos(chegada)}");
		}

		try
		{
			var ida = Transmitir();
			LiteNetLib.Utils.NetDataWriter fio = PacoteDeAureola(morto);
			bool transparenteNoFio = fio.Length >= 2 && fio.Data[fio.Length - 1] == 1 && fio.Data[fio.Length - 2] == 1;   // aureola, passagem
			bool dePe = morto.Ficha.dead && morto.EmPassagem && morto.MortoDePe && !EhCadaver(morto) && PodeMexerOCorpo(morto) && !morto.Deitado
							  && morto.Sheet().EmVisita && Alem.TemAureola(morto.Ficha.dead, morto.MorteJaViajou);
			long prazo = morto.PassagemAte - NowMs();
			CharacterSave noDisco = AccountStore.DeJogador(morto, NowMs());
			GravarAVoltaDaPassagem(noDisco, morto);
			bool discoVolta = noDisco.Zona == alem.Name && Math.Abs(noDisco.X - deOnde.X) < 1 && Math.Abs(noDisco.Y - deOnde.Y) < 1;
			AfirmarV30("**O MORTO TRANSMITE PRA UM VIVO** (*\"caso um player morto use instante transmission pra alguem q ta vivo\"*): do Outro Mundo a concentracao sai, e ele aparece ao lado do vivo na Terra",
						   ida.Concentrou && ida.Chegou, ida.Detalhe);
			AfirmarV30("**...MEIO TRANSPARENTE, POR 5 s**: continua MORTO e de aureola, mas DE PE (anda, nao e cadaver, o bit da ficha), e o pacote da aureola leva o bit da passagem (o corpo meio transparente na tela de todos); o prazo e de 5 s",
						   dePe && transparenteNoFio && prazo > 4_000 && prazo <= Alem.MsDaPassagem,
						   $"de pe {dePe}; no fio {transparenteNoFio}; prazo {prazo} ms");
			AfirmarV30("...e quem cai no meio dos 5 s acorda no Outro Mundo: o disco grava o lugar da VOLTA, e nao a Terra",
						   discoVolta, $"{noDisco.Zona} ({noDisco.X:0},{noDisco.Y:0}) -- volta em {deOnde}");

			morto.PassagemAte = NowMs() - 1;   // os 5 s venceram
			List<string> volta = Ouvir(() => CumprirAPassagem(morto));
			bool voltou = morto.Zone.Equals(alem) && (morto.Pos - deOnde).Length < T && !morto.EmPassagem && morto.MortoDePe && morto.Ficha.dead;
			LiteNetLib.Utils.NetDataWriter fioDepois = PacoteDeAureola(morto);
			bool opacoNoFio = fioDepois.Data[fioDepois.Length - 1] == 0;
			AfirmarV30("**EM 5 s A ALMA VOLTA**: pro lugar exato de onde saiu no Outro Mundo -- morto, de pe e de aureola, sem a passagem (o pacote volta a dizer opaco)",
						   voltou && opacoNoFio && Disse(volta, "Outro Mundo"), $"zona {morto.Zone.Name}; em {morto.Pos} (saiu de {deOnde}); {Ultimos(volta)}");

			// CAIR ENTRE OS VIVOS NO MEIO: a mesa do Enma, e a passagem acaba ali
			Transmitir();
			bool foi = morto.EmPassagem && morto.Zone.Equals(vivo.Zone);
			MorrerDeNovoNoAlem(morto);
			bool naMesa = foi && morto.Zone.Equals(alem) && !morto.EmPassagem && morto.PassagemAte == 0;
			morto.Combate.Levantar();
			AfirmarV30("...e quem CAI entre os vivos no meio da passagem acorda na mesa do Enma, sem ela (o \"morrer de novo\" de sempre)",
						   naMesa, $"foi {foi}; zona {morto.Zone.Name}; passagem {morto.PassagemAte}");

			// OS CONTRA-EXEMPLOS: a Transmissao que recusa o morto, e a passagem que nao volta
			List<string> recusa;
			MortoNaoTransmiteDeTeste = true;
			try
			{
				MoveToZone(morto.Id, alem, deOnde);
				recusa = Ouvir(() => TeletransporteG11(morto, vivo.Name));
			}
			finally { MortoNaoTransmiteDeTeste = false; }
			AfirmarV30("(defeito injetado: a Transmissao que recusa todo morto, o jogo de 2026-10-15) \"um morto nao sai do Outro Mundo por conta propria\" -- a primeira regua REPROVA",
						   !_transmissaoG11.ContainsKey(morto.Id) && Disse(recusa, "nao sai do Outro Mundo"), Ultimos(recusa));
			Transmitir();
			PassagemSemVoltaDeTeste = true;
			bool ficou;
			try
			{
				morto.PassagemAte = NowMs() - 1;
				CumprirAPassagem(morto);
				ficou = morto.Zone.Equals(vivo.Zone) && morto.EmPassagem;
			}
			finally { PassagemSemVoltaDeTeste = false; }
			AfirmarV30("(defeito injetado: a passagem que nunca vence) o morto ficaria entre os vivos pra sempre -- a regua da volta REPROVA",
						   ficou, $"zona {morto.Zone.Name}; passagem {morto.EmPassagem}");
		}
		finally
		{
			_transmissaoG11.Remove(morto.Id);
			morto.PassagemAte = 0;
			morto.Ficha.dead = false;
			LimparTudoDaBancada();
		}
	}

	/// <summary>O que o snapshot diz agora do tiro `id` -- pelo `TirosDaZona` de producao (nulo se ele nao viaja).</summary>
	private ProjetilState? NoSnapshotV30(ServerPlayer pl, int id)
	{
		foreach (ProjetilState s in TirosDaZona(pl.Zone.Hash))
			if (s.Id == id) return s;
		return null;
	}

	/// <summary>Um corpo de bancada que sabe o Kaio-ken (a skill que a licao do Sr. Kaioh da).</summary>
	private ServerPlayer ForjarKaiokenV30(string nome, Vec2 onde) =>
		ForjarComSkills(nome, onde, 50_000, skills: [NpcsDeConversa.SkillDoKaioken]);

	/// <summary>A FICHA LENTA QUE SAI AGORA pra este corpo (`MandarAtributos`, pela escuta da bancada): a assinatura e apagada pra ela sair.</summary>
	private static Protocol.AtributosState? FichaLentaV30(ServerPlayer pl)
	{
		List<Protocol.AtributosState>? antes = EscutaDeAtributos;
		var escuta = new List<Protocol.AtributosState>();
		EscutaDeAtributos = escuta;
		try
		{
			pl.SigAtributos = "";
			MandarAtributos(pl);
		}
		finally { EscutaDeAtributos = antes; }
		return escuta.Count > 0 ? escuta[^1] : null;
	}
}
