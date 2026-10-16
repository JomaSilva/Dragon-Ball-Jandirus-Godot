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
