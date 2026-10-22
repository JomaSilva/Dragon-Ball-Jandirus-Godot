using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Economia;
using Jandirus.Core.Items;
using Jandirus.Core.Npc;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using Aparencia = Jandirus.Core.Appearance.Appearance;
using PecaDeRoupa = Jandirus.Core.Appearance.PecaDeRoupa;
using Rgb = Jandirus.Core.Appearance.Rgb;
using VisualCatalog = Jandirus.Core.Appearance.VisualCatalog;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--v020teste` -- O PEDIDO DE 2026-10-12 (a versao 0.2.0), do lado do servidor e do Core.
///
///     Godot --headless --path . --server --port 8261 --v020teste
///
/// Cada familia e um item do pedido do dono, medido pela porta de PRODUCAO (o `AlternarAgarrao`, o
/// `TickDoAgarrao`, o `Body.Sortear`, o funil do `AoDecepar`...) e com o defeito injetado do jogo de antes:
///
///   1. O PASSO: o corpo no piso da velocidade "saiu do lugar" em qualquer taxa de quadros, e o ritmo da perna
///      acompanha a velocidade (item 18).
///   2. A OBRA SEM PARAFUSO: a macieira nao pisca de "solta" (item 2); e a frase curta da mesa de tecnicas (item 7).
///   3. O AGARRAO: o NPC se debate sozinho; quem aperta a direcao anda no caminho (e a ficha diz quanto); quanto
///      mais forte quem segura, mais demora; caido ninguem se debate (item 9).
///   4. O ESTRANGULAMENTO: dano por segundo na cabeca, pelo gap de poder (item 9).
///   5. O RABO PRESO: por tras pega o rabo, o poder cai, a maestria sobe ate a imunidade (item 11).
///   6. A MIRA NO RABO (item 10).
///   7. O RABO QUE NAO VOLTA: nao-Primal acima de 20 mil (item 22).
///   8. A FICHA: os bits de atordoado e de guarda que o HUD le (item 12).
///   9. O KI CONTRA O CORPO: o raio de um igual FERE; a parede (4x) nao sente; o mais fraco e levado ate o fim do
///      alcance, onde o raio estoura com cratera grande; a guarda leva o dano nos bracos ou nas pernas; todo raio e
///      toda esfera grande estouram no fim do alcance (itens 5, 16 e 17) -- e o estouro FERE EM AREA, quem foi
///      levado e quem so estava perto (o retorno do dono sobre a primeira entrega). A esfera grande estoura tambem
///      no corpo em que acaba, e o de quem e PAREDE pra ela nao e excecao (a regra que o dono fechou no mesmo dia).
///  10. OS PESOS: 50 kg de fabrica, o maximo se compra (zeni ou recursos) e se veste em % dele (item 3).
///  11. A MELHORIA DE UMA VEZ SO: a tabela que tira do menu o que nao da mais pra comprar (item 4) -- a maquina de
///      verdade e medida na `--maquinasteste`.
///  12. O VIGOR DE QUEM SE CONTEM: uma reta pela barra de poder (1x a 100%, 2,5x a 1%) e 3,5x relaxado (item 13,
///      com o retorno do dono de 2026-10-12).
///  13. AS ROUPAS: a pasta `Clothes` inteira no guarda-roupa da criacao, menos a do Beerus e o rabo (item 6).
///  14. A ARMADURA DE TECH: a arte se escolhe, protege no comeco e some ate os 50 mil de poder (itens 6 e 14).
///  15. A ROUPA DE QUEM ESTA NOCAUTEADO: sai do corpo dele pra mochila de quem leva, armadura inclusive (itens 8 e 14).
///  16. A VARIEDADE DOS NPCs: talentos sorteados por corpo (a Afterimage entre eles), cor de ki propria, e a IA que
///      pisca pras costas da presa (item 15).
///
/// A metade de TELA (o HUD, o chat, o menu P, o marcador de alvo, os pontinhos do ar) e da `--diagv020`.
/// </summary>
public sealed partial class GameServer
{
	private int _v2Ok, _v2Falhou;

	private void AfirmarV2(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _v2Ok++; GD.Print($"[v020]   OK    {oque}"); return; }
		_v2Falhou++;
		GD.PrintErr($"[v020]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaV020()
	{
		_v2Ok = _v2Falhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[v020] ================ A VERSAO 0.2.0: O PEDIDO DE 2026-10-12 ================");
		AfirmarV2("a zona da bancada tem colisao carregada", _pjMapa != null);

		try
		{
			OPassoV2();
			AObraSemParafusoV2();
			OAgarraoV2();
			OEstrangulamentoV2();
			ORaboPresoV2();
			AMiraNoRaboV2();
			ORaboQueNaoVoltaV2();
			AFichaV2();
			OKiContraOCorpoV2();
			OsPesosV2();
			AMelhoriaUnicaV2();
			OVigorDeQuemSeContemV2();
			AsRoupasV2();
			AArmaduraV2();
			OSaqueDaRoupaV2();
			AVariedadeDosNpcsV2();
		}
		finally
		{
			DesligarOsDefeitosV2();
			LimparTudoDaBancada();
		}

		GD.Print($"[v020] ================ {_v2Ok} passaram, {_v2Falhou} falharam ================");
	}

	/// <summary>Nenhum botao de defeito sobrevive a bancada.</summary>
	private static void DesligarOsDefeitosV2()
	{
		MoveRules.PassoPorPixelFixoDeTeste = false;
		Interacoes.TudoPedeParafusoDeTeste = false;
		NpcNaoSeDebateDeTeste = false;
		Agarrao.EstrangulamentoPorSorteioDeTeste = false;
		RaboPreso.DesligadoDeTeste = false;
		Body.MiraNoRaboDesligadaDeTeste = false;
		RaboDoSaiyajin.PerdaDefinitivaDesligadaDeTeste = false;
		KiContraCorpo.RegraDeAntesDeTeste = false;
		PesoDeTreino.TetoDoCorpoDeGracaDeTeste = false;
		Interacoes.MelhoriaFeitaFicaNoMenuDeTeste = false;
		PoderEmUso.DescansoNaoRendeDeTeste = false;
		ArmaduraDeTech.NaoProtegeDeTeste = ArmaduraDeTech.ProtegeEmQualquerPoderDeTeste = false;
		Saque.RoupaDoCorpoFicaDeTeste = false;
		SorteioDeNpc.TalentosDesligadosDeTeste = false;
		NpcSemCorDeKiDeTeste = IaNaoPiscaDeTeste = false;
		EstrelaSoNaDisputaDeTeste = false;
		EsferaSomeNaParedeDeTeste = false;
	}

	// =====================================================================
	// A INFRAESTRUTURA
	// =====================================================================
	/// <summary>Um corpo da raca pedida, pela mesma porta do `Forjar` da bancada de projetil (que so faz Humano).</summary>
	private ServerPlayer ForjarV2(string nome, Vec2 onde, double bp, string raca = "Human", string linhagem = "")
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDeProjetil + _pjCorpos++,
			Peer = null,
			Name = nome,
			Race = raca,
			Genero = "Male",
			Idade = 25,
			Zone = ZonaDaBancadaDeProjetil,
			Pos = onde,
			Conta = "bancada_v020",
			Slot = 0,
			Ficha = new Fighter { Race = raca, BP = bp, SaiyanLineage = linhagem },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.Tick(agoraMs: NowMs());
		return novo;
	}

	/// <summary>
	/// Quem agarra (A) e quem e agarrado (D), colados. <paramref name="porTras"/>: D olha pro mesmo lado que A,
	/// ou seja A esta nas costas dele; senao os dois se encaram.
	/// </summary>
	private (ServerPlayer A, ServerPlayer D) ParV2(double bpA, double bpD, bool porTras, string racaDoD = "Human", string linhagem = "")
	{
		Vec2 c = TerraFirmeG12(6);
		ServerPlayer a = ForjarV2("Agarrador", c, bpA);
		ServerPlayer d = ForjarV2("Preso", c + new Vec2(ZoneCollision.TileSize * 0.9f, 0), bpD, racaDoD, linhagem);
		a.Facing = Facing.East;
		d.Facing = porTras ? Facing.East : Facing.West;
		return (a, d);
	}

	/// <summary>Um tique do DM (0,1 s) do agarrao, pelos tiques de producao.</summary>
	private void TiqueDoDmV2()
	{
		for (int i = 0; i < 3; i++) TickDoAgarrao(Protocol.TickSeconds);
	}

	/// <summary>
	/// Corre o agarrao ate o preso se soltar ou o prazo vencer. <paramref name="apertaADirecao"/> faz o que o
	/// `Input` faz quando a tecla de andar chega de um corpo preso: liga o `DebatendoSe` antes de cada tique.
	/// Devolve os segundos ate a soltura, ou -1.
	/// </summary>
	private double LutarV2(ServerPlayer a, ServerPlayer d, double prazo, bool apertaADirecao)
	{
		for (double t = 0; t < prazo; t += Empurrao.SegundosPorTique)
		{
			if (apertaADirecao && !d.Ficha.KO && !d.Ficha.dead) d.DebatendoSe = true;
			TiqueDoDmV2();
			if (a.AgarrandoId == 0) return t + Empurrao.SegundosPorTique;
		}
		return -1;
	}

	/// <summary>Tira um corpo de bancada do mundo, como o `LimparTudoDaBancada` faz com todos no fim.</summary>
	private void TirarDoMundoV2(ServerPlayer pl)
	{
		EsquecerTecnicas(pl.Id);
		_players.Remove(pl.Id);
		ZoneList(pl.Zone.Hash).Remove(pl);
	}

	private void DesfazerV2(params ServerPlayer[] corpos)
	{
		foreach (ServerPlayer p in corpos)
		{
			if (p.AgarrandoId != 0) Soltar(p, MotivoDaSoltura.Tecla);
			TirarDoMundoV2(p);
		}
	}

	// =====================================================================
	// 1) O PASSO
	// =====================================================================
	private void OPassoV2()
	{
		GD.Print("[v020] -- 1) O PASSO: pesado e lento, a perna continua andando (item 18)");
		const float piso = 0.1f;   // `MoveRules.SpeedStatFrom` nao desce disto
		float pxPorSegundo = MoveRules.SpeedPx(piso);

		foreach (int fps in new[] { 30, 60, 144, 240 })
		{
			float dt = 1f / fps;
			var antes = new Vec2(100, 100);
			var depois = new Vec2(100 + pxPorSegundo * dt, 100);
			AfirmarV2($"a {fps} quadros por segundo o corpo no piso da velocidade ({pxPorSegundo:0} px/s) SAIU DO LUGAR no quadro",
					  MoveRules.SaiuDoLugar(antes, depois, dt, piso, false), $"{pxPorSegundo * dt:0.000} px no quadro");
		}

		var parado = new Vec2(100, 100);
		AfirmarV2("CONTRA-EXEMPLO: empurrando a parede (deslocamento zero) o corpo NAO saiu do lugar",
				  !MoveRules.SaiuDoLugar(parado, parado, 1f / 60, 1f, false));

		MoveRules.PassoPorPixelFixoDeTeste = true;
		try
		{
			float dt = 1f / 240;
			AfirmarV2("(defeito injetado: o corte fixo de um decimo de pixel) a 240 quadros o corpo lento volta a contar como PARADO -- a regua sabe reprovar",
					  !MoveRules.SaiuDoLugar(parado, new Vec2(100 + pxPorSegundo * dt, 100), dt, piso, false));
		}
		finally { MoveRules.PassoPorPixelFixoDeTeste = false; }

		AfirmarV2("o ritmo da perna segue a velocidade: 1 no normal, a metade na metade, e nunca abaixo do piso (a perna nao para)",
				  MoveRules.RitmoDoPasso(1f) == 1f && MoveRules.RitmoDoPasso(0.5f) == 0.5f
				  && MoveRules.RitmoDoPasso(piso) == MoveRules.RitmoMinimoDoPasso && MoveRules.RitmoMinimoDoPasso > 0,
				  $"{MoveRules.RitmoDoPasso(1f)} / {MoveRules.RitmoDoPasso(0.5f)} / {MoveRules.RitmoDoPasso(piso)}");
		AfirmarV2("...e acima do normal nao acelera: quem acelera o passo e a corrida", MoveRules.RitmoDoPasso(3f) == 1f);
	}

	// =====================================================================
	// 2) A OBRA SEM PARAFUSO, E A FRASE DA MESA
	// =====================================================================
	private void AObraSemParafusoV2()
	{
		GD.Print("[v020] -- 2) A OBRA SEM PARAFUSO (item 2) e a frase da mesa de tecnicas (item 7)");
		AfirmarV2("a macieira NAO pede parafuso (a `AppleTree` do DM nao tem `Bolt`): nada de contorno de 'solta' nela",
				  !Interacoes.PedeParafuso(Jandirus.Core.Items.CatalogoDeItens.ArvoreDeMaca));
		AfirmarV2("...nem a Drill, que trabalha assentada", !Interacoes.PedeParafuso(Drill.Tipo));
		AfirmarV2("CONTRA-EXEMPLO: o regenerador, a maquina de gravidade e o mainframe pedem",
				  Interacoes.PedeParafuso(Regenerador.Tipo) && Interacoes.PedeParafuso("Gravity")
				  && Interacoes.PedeParafuso("Android_Creation_Mainframe"));

		Interacoes.TudoPedeParafusoDeTeste = true;
		try
		{
			AfirmarV2("(defeito injetado: toda obra pede parafuso) a macieira volta a pedir -- a regua sabe reprovar",
					  Interacoes.PedeParafuso(Jandirus.Core.Items.CatalogoDeItens.ArvoreDeMaca));
		}
		finally { Interacoes.TudoPedeParafusoDeTeste = false; }

		AfirmarV2("a recusa da mesa e a frase do dono, sem a lista do que abre o que",
				  Jandirus.Core.Skills.TecnicaCustomizada.SemControleDeKi.Contains("não pode criar técnica nenhuma")
				  && Jandirus.Core.Skills.TecnicaCustomizada.SemControleDeKi.Contains("controle de ki ainda é muito baixo")
				  && !Jandirus.Core.Skills.TecnicaCustomizada.SemControleDeKi.Contains("Onda de Ki"));
	}

	// =====================================================================
	// 3) O AGARRAO: SE DEBATER
	// =====================================================================
	private void OAgarraoV2()
	{
		GD.Print("[v020] -- 3) O AGARRAO: quem e pego se debate, e a forca decide quanto demora (item 9)");

		// ---- o NPC se debate sozinho
		(ServerPlayer a, ServerPlayer npc) = ParV2(5_000, 5_000, porTras: false);
		npc.Cerebro = new Cerebro();
		AlternarAgarrao(a);
		AfirmarV2("(palco) A agarrou o NPC que estava na frente", a.AgarrandoId == npc.Id && npc.AgarradoPorId == a.Id);
		AfirmarV2("a ficha de quem segura diz SEGURANDO, e a de quem e pego diz AGARRADO (o HUD le as duas)",
				  a.Sheet().Segurando && npc.Sheet().Agarrado && !a.Sheet().Agarrado && !npc.Sheet().Segurando);
		double soltouEm = LutarV2(a, npc, 30, apertaADirecao: false);
		AfirmarV2("**O NPC SE DEBATE SOZINHO**: de poder igual ele se solta em poucos segundos, sem ninguem apertar tecla por ele",
				  soltouEm > 0 && soltouEm < 15, $"{soltouEm:0.0} s");
		AfirmarV2("...e solto, a ficha dele deixa de dizer agarrado (e o andamento volta a zero)",
				  !npc.Sheet().Agarrado && npc.Sheet().Escape == 0 && !a.Sheet().Segurando);
		DesfazerV2(a, npc);

		NpcNaoSeDebateDeTeste = true;
		try
		{
			(a, npc) = ParV2(5_000, 5_000, porTras: false);
			npc.Cerebro = new Cerebro();
			AlternarAgarrao(a);
			double nunca = LutarV2(a, npc, 30, apertaADirecao: false);
			AfirmarV2("(defeito injetado: o NPC nao se debate) trinta segundos depois ele continua na mao -- a regua sabe reprovar",
					  nunca < 0 && a.AgarrandoId == npc.Id, $"{nunca:0.0} s");
			DesfazerV2(a, npc);
		}
		finally { NpcNaoSeDebateDeTeste = false; }

		// ---- o corpo sem cerebro (o jogador) so anda no caminho apertando a direcao
		(a, ServerPlayer d) = ParV2(5_000, 5_000, porTras: false);
		AlternarAgarrao(a);
		double parado = LutarV2(a, d, 5, apertaADirecao: false);
		AfirmarV2("sem apertar direcao nenhuma, o corpo preso NAO anda no caminho: cinco segundos depois continua preso, em 0%",
				  parado < 0 && d.Sheet().Escape == 0 && d.ContadorDaLuta == 0, $"{parado:0.0} s, {d.Sheet().Escape}%");

		d.DebatendoSe = true;
		TiqueDoDmV2();
		byte primeiro = d.Sheet().Escape;
		AfirmarV2("**APERTAR A DIRECAO E SE DEBATER**: um tique de luta e o andamento ja aparece na ficha (o HUD escreve o %)",
				  a.AgarrandoId == 0 || primeiro > 0, $"{primeiro}%");
		double igual = a.AgarrandoId == 0 ? 0.1 : LutarV2(a, d, 30, apertaADirecao: true);
		AfirmarV2("...e de poder igual o preso se solta em poucos segundos", igual > 0 && igual < 15, $"{igual:0.0} s");
		DesfazerV2(a, d);

		// ---- quanto mais forte quem segura, mais demora
		double[] tempos = new double[2];
		double[] vezes = [1, 3];
		for (int i = 0; i < vezes.Length; i++)
		{
			double soma = 0;
			const int rodadas = 5;
			for (int r = 0; r < rodadas; r++)
			{
				(a, d) = ParV2(5_000 * vezes[i], 5_000, porTras: false);
				AlternarAgarrao(a);
				double t = LutarV2(a, d, 120, apertaADirecao: true);
				soma += t < 0 ? 120 : t;
				DesfazerV2(a, d);
			}
			tempos[i] = soma / rodadas;
		}
		AfirmarV2("**A FORCA DECIDE**: contra quem tem o triplo do poder a luta leva bem mais que contra um igual (media de 5)",
				  tempos[1] > tempos[0] * 2.5, $"igual {tempos[0]:0.0} s, triplo {tempos[1]:0.0} s");
		AfirmarV2("...e mesmo assim TERMINA: ninguem fica preso pra sempre (o piso da chance de escapar)",
				  tempos[1] < 120, $"{tempos[1]:0.0} s");

		// ---- caido ninguem se debate
		(a, d) = ParV2(5_000, 5_000, porTras: false);
		AlternarAgarrao(a);
		d.Combate.Nocautear(60);
		d.DebatendoSe = true;   // um cliente mexido mandando o bit: quem recusa e o servidor
		TiqueDoDmV2();
		AfirmarV2("NOCAUTEADO ninguem se debate: o bit ligado a forca num corpo caido nao anda o contador",
				  d.ContadorDaLuta == 0 && a.AgarrandoId == d.Id, $"contador {d.ContadorDaLuta:0.##}");
		DesfazerV2(a, d);
	}

	// =====================================================================
	// 4) O ESTRANGULAMENTO
	// =====================================================================
	/// <summary>A vida da cabeca que o aperto tirou em <paramref name="segundos"/>, e a do torso (que nao pode mexer).</summary>
	private (double Cabeca, double Torso, ServerPlayer A, ServerPlayer D) ApertarV2(double bpA, double segundos)
	{
		(ServerPlayer a, ServerPlayer d) = ParV2(bpA, 5_000, porTras: false);
		AlternarAgarrao(a);
		BodyPart cabeca = d.Combate.Corpo.Achar("Cabeca")!, torso = d.Combate.Corpo.Achar("Torso")!;
		double c0 = cabeca.Vida, t0 = torso.Vida;
		a.AfogamentoLivreEm = 0;
		EstrangularEmVezDeSocar(a);
		for (double t = 0; t < segundos - 1e-9; t += Empurrao.SegundosPorTique) TiqueDoDmV2();
		return (c0 - cabeca.Vida, t0 - torso.Vida, a, d);
	}

	private void OEstrangulamentoV2()
	{
		GD.Print("[v020] -- 4) O ESTRANGULAMENTO: socar com alguem na mao aperta o pescoco, e doi por segundo (item 9)");

		(double cabeca, double torso, ServerPlayer a, ServerPlayer d) = ApertarV2(5_000, 3);
		double esperado = Agarrao.EstrangulamentoPorSegundo(a.Ficha, d.Ficha) * 3;
		AfirmarV2("socar segurando alguem NAO soca: liga o aperto (as duas pontas ficam sabendo -- a ficha de quem e apertado diz ESTRANGULADO)",
				  a.Estrangulando && d.PescocoApertado && d.Sheet().Estrangulado);
		AfirmarV2($"**DANO POR SEGUNDO NA CABECA**: tres segundos de aperto entre iguais tiram o que a regra diz ({esperado:0.0})",
				  cabeca > esperado * 0.6 && cabeca < esperado * 1.15 && esperado > 3, $"cabeca -{cabeca:0.00}");
		AfirmarV2("...e so la: o torso nao levou nada (no DM o dano se espalhava pelo corpo inteiro)", torso < 0.01, $"torso -{torso:0.00}");
		a.AfogamentoLivreEm = 0;
		EstrangularEmVezDeSocar(a);
		AfirmarV2("socar de novo AFROUXA: o aperto desliga nas duas pontas", !a.Estrangulando && !d.PescocoApertado && !d.Sheet().Estrangulado);
		DesfazerV2(a, d);

		(double forte, _, ServerPlayer a2, ServerPlayer d2) = ApertarV2(10_000, 3);
		AfirmarV2("**PELA DIFERENCA DE FORCA**: com o dobro do poder o mesmo aperto tira o dobro",
				  forte > cabeca * 1.7 && forte < cabeca * 2.3, $"igual -{cabeca:0.00}, dobro -{forte:0.00}");
		DesfazerV2(a2, d2);

		(double fraco, _, ServerPlayer a3, ServerPlayer d3) = ApertarV2(500, 3);
		AfirmarV2("...e quem tem um decimo do poder aperta sem machucar (o piso do gap)", fraco < cabeca * 0.2, $"-{fraco:0.000}");
		DesfazerV2(a3, d3);

		Agarrao.EstrangulamentoPorSorteioDeTeste = true;
		try
		{
			(double dm, double torsoDm, ServerPlayer a4, ServerPlayer d4) = ApertarV2(5_000, 3);
			AfirmarV2("(defeito injetado: o `prob(5)` do DM, espalhado) em tres segundos a cabeca quase nao sente -- a regua de cima reprovaria",
					  dm < esperado * 0.6, $"cabeca -{dm:0.000}, torso -{torsoDm:0.000}");
			DesfazerV2(a4, d4);
		}
		finally { Agarrao.EstrangulamentoPorSorteioDeTeste = false; }
	}

	// =====================================================================
	// 5) O RABO PRESO
	// =====================================================================
	private void ORaboPresoV2()
	{
		GD.Print("[v020] -- 5) O RABO PRESO: por tras de um Saiyajin com rabo, e pelo rabo (item 11)");

		(ServerPlayer a, ServerPlayer s) = ParV2(5_000, 5_000, porTras: true, racaDoD: "Saiyan");
		AfirmarV2("(palco) o Saiyajin forjado tem rabo, e maestria zero", s.TemRaboAgora() && s.Ficha.maestriaDoRabo == 0);
		double antes = s.Ficha.expressedBP;
		AlternarAgarrao(a);
		s.Ficha.Tick(agoraMs: NowMs());
		double fator = RaboPreso.FatorDePoder(RaboPreso.MaestriaPorPegada);
		AfirmarV2("**POR TRAS, PEGA O RABO**: o aperto nasce marcado, e a primeira pegada ja vale maestria",
				  a.AgarrandoId == s.Id && s.PresoPeloRabo && s.Ficha.maestriaDoRabo == RaboPreso.MaestriaPorPegada);
		AfirmarV2($"**O PODER SOME**: com o rabo na mao o poder expresso cai a ~{fator * 100:0}% do que era",
				  antes > 0 && Math.Abs(s.Ficha.expressedBP / antes - fator) < 0.02, $"{antes:0} -> {s.Ficha.expressedBP:0}");
		TiqueDoDmV2();
		AfirmarV2("...o corpo nao golpeia (fica atordoado enquanto durar) e a ficha dele diz RABO PRESO",
				  s.Combate.Stun > 0 && !s.Combate.PodeAtacar() && s.Sheet().RaboPreso && s.Sheet().Atordoado);

		double maestria0 = s.Ficha.maestriaDoRabo;
		for (int i = 0; i < 100; i++) TiqueDoDmV2();   // dez segundos na mao
		double ganho = s.Ficha.maestriaDoRabo - maestria0;
		AfirmarV2("**A MAESTRIA SOBE COM O TEMPO**: dez segundos com o rabo preso ensinam o que a regra diz",
				  a.AgarrandoId == s.Id && Math.Abs(ganho - RaboPreso.MaestriaPorSegundo * 10) < 0.2, $"+{ganho:0.00}");
		AfirmarV2("...e o poder volta junto com ela", Math.Abs(s.Ficha.raboPresoMod - RaboPreso.FatorDePoder(s.Ficha.maestriaDoRabo)) < 1e-9
				  && s.Ficha.raboPresoMod > fator);

		s.Ficha.maestriaDoRabo = RaboPreso.MaestriaDaImunidade - 0.01;
		TiqueDoDmV2();
		s.Ficha.Tick(agoraMs: NowMs());
		AfirmarV2("**ATE A IMUNIDADE**: fechada a maestria, o rabo deixa de ser fraqueza -- o aperto vira um agarrao comum e o poder volta inteiro",
				  RaboPreso.Imune(s.Ficha) && !s.PresoPeloRabo && s.Ficha.raboPresoMod == 1 && a.AgarrandoId == s.Id
				  && Math.Abs(s.Ficha.expressedBP / antes - 1) < 0.02, $"poder {s.Ficha.expressedBP:0} de {antes:0}");
		Soltar(a, MotivoDaSoltura.Tecla);
		s.Facing = Facing.East;
		AlternarAgarrao(a);
		AfirmarV2("...e o imune agarrado por tras de novo nao e mais pego pelo rabo", a.AgarrandoId == s.Id && !s.PresoPeloRabo && s.Ficha.raboPresoMod == 1);
		DesfazerV2(a, s);

		// ---- soltar devolve o poder
		(a, s) = ParV2(5_000, 5_000, porTras: true, racaDoD: "Saiyan");
		AlternarAgarrao(a);
		bool cortado = s.Ficha.raboPresoMod < 1;
		Soltar(a, MotivoDaSoltura.Tecla);
		AfirmarV2("SOLTO, o poder volta: o corte e do aperto, e nao do personagem", cortado && s.Ficha.raboPresoMod == 1 && !s.PresoPeloRabo && !s.Sheet().RaboPreso);
		DesfazerV2(a, s);

		// ---- os contra-exemplos
		(a, s) = ParV2(5_000, 5_000, porTras: false, racaDoD: "Saiyan");
		AlternarAgarrao(a);
		AfirmarV2("CONTRA-EXEMPLO: PELA FRENTE e um agarrao comum -- o rabo fica onde esta", a.AgarrandoId == s.Id && !s.PresoPeloRabo && s.Ficha.raboPresoMod == 1 && s.Ficha.maestriaDoRabo == 0);
		DesfazerV2(a, s);

		(a, ServerPlayer h) = ParV2(5_000, 5_000, porTras: true);
		AlternarAgarrao(a);
		AfirmarV2("CONTRA-EXEMPLO: quem nao tem rabo (um Humano), agarrado por tras, e so agarrado", a.AgarrandoId == h.Id && !h.PresoPeloRabo && h.Ficha.raboPresoMod == 1);
		DesfazerV2(a, h);

		RaboPreso.DesligadoDeTeste = true;
		try
		{
			(a, s) = ParV2(5_000, 5_000, porTras: true, racaDoD: "Saiyan");
			AlternarAgarrao(a);
			AfirmarV2("(defeito injetado: o rabo nunca e pego) por tras do Saiyajin sai um agarrao comum -- a regua de cima reprovaria",
					  a.AgarrandoId == s.Id && !s.PresoPeloRabo && s.Ficha.raboPresoMod == 1);
			DesfazerV2(a, s);
		}
		finally { RaboPreso.DesligadoDeTeste = false; }
	}

	// =====================================================================
	// 6) A MIRA NO RABO
	// =====================================================================
	private static double FracaoNoRaboV2(Body corpo, string? zona, int seed)
	{
		var rng = new Random(seed);
		int rabo = 0;
		const int sorteios = 4000;
		for (int i = 0; i < sorteios; i++)
			if (corpo.Sortear(zona, rng)?.Nome == Body.NomeDoRabo) rabo++;
		return rabo / (double)sorteios;
	}

	private void AMiraNoRaboV2()
	{
		GD.Print("[v020] -- 6) A MIRA NO RABO: a sexta zona do seletor (item 10)");
		AfirmarV2("o protocolo ganhou a zona 6 = 'rabo', depois das cinco que ja viajavam (os indices antigos nao mudam)",
				  Protocol.ZonaDe(6) == Body.ZonaDoRabo && Protocol.ZonaDe(5) == "pernas" && Protocol.ZonaDe(3) == "abdomen" && Protocol.ZonaDe(7) == "");

		Body comRabo = Body.Novo(comRabo: true), semRabo = Body.Novo(comRabo: false);
		double mirado = FracaoNoRaboV2(comRabo, Body.ZonaDoRabo, 11);
		double noAbdomen = FracaoNoRaboV2(comRabo, "abdomen", 11);
		double semMira = FracaoNoRaboV2(comRabo, null, 11);
		AfirmarV2("**MIRANDO O RABO**, cerca de um terco dos golpes pega o rabo (a mira pesa, nao garante)",
				  mirado > 0.28 && mirado < 0.42, $"{mirado * 100:0.0}%");
		AfirmarV2("CONTRA-EXEMPLO: mirando o abdomen (o caminho de antes) ou sem mira, o rabo sai bem menos",
				  noAbdomen < mirado * 0.5 && semMira < mirado * 0.3, $"abdomen {noAbdomen * 100:0.0}%, sem mira {semMira * 100:0.0}%");

		var rngA = new Random(5);
		var rngB = new Random(5);
		bool igual = true;
		for (int i = 0; i < 500 && igual; i++)
			igual = semRabo.Sortear(Body.ZonaDoRabo, rngA)?.Nome == semRabo.Sortear("abdomen", rngB)?.Nome;
		AfirmarV2("em quem NAO tem rabo, mirar o rabo e mirar o abdomen (onde ele estaria): o mesmo sorteio, membro a membro", igual);

		comRabo.Achar(Body.NomeDoRabo)!.Decepado = true;
		AfirmarV2("...e com o rabo ja arrancado tambem: nenhum golpe 'acerta' um rabo que nao esta la",
				  FracaoNoRaboV2(comRabo, Body.ZonaDoRabo, 11) == 0);
		comRabo.Achar(Body.NomeDoRabo)!.Decepado = false;

		Body.MiraNoRaboDesligadaDeTeste = true;
		try
		{
			double desligada = FracaoNoRaboV2(comRabo, Body.ZonaDoRabo, 11);
			AfirmarV2("(defeito injetado: a mira no rabo e a do abdomen) o rabo volta a sair pouco -- a regua de cima reprovaria",
					  desligada < 0.2, $"{desligada * 100:0.0}%");
		}
		finally { Body.MiraNoRaboDesligadaDeTeste = false; }
	}

	// =====================================================================
	// 7) O RABO QUE NAO VOLTA
	// =====================================================================
	/// <summary>Forja um Saiyajin, arranca o rabo pelo funil de dano letal e devolve o corpo e o membro.</summary>
	private (ServerPlayer Pl, BodyPart Rabo) ArrancarORaboV2(double bp, string linhagem)
	{
		ServerPlayer pl = ForjarV2("Saiyajin", TerraFirmeG12(4), bp, "Saiyan", linhagem);
		BodyPart rabo = pl.Combate.Corpo.Achar(RaboDoSaiyajin.Membro)!;
		pl.Combate.Ferir(rabo, 10_000, letal: true);
		pl.Combate.SincronizarVida();
		return (pl, rabo);
	}

	private void ORaboQueNaoVoltaV2()
	{
		GD.Print("[v020] -- 7) O RABO QUE NAO VOLTA: nao-Primal acima de 20 mil de poder (item 22)");
		double acima = RaboDoSaiyajin.PoderEmQueORaboNaoVolta * 1.25, abaixo = RaboDoSaiyajin.PoderEmQueORaboNaoVolta * 0.75;

		(ServerPlayer pl, BodyPart rabo) = ArrancarORaboV2(acima, "Saiyan");
		AfirmarV2("(palco) o golpe letal arrancou o rabo pelo funil de producao", rabo.Decepado && !pl.TemRaboAgora());
		AfirmarV2("**PERDIDO DE VEZ**: Saiyajin nao-Primal com 25 mil de poder de base -- o rabo cai marcado", rabo.Perdido);
		AfirmarV2("...o corpo NAO o refaz (a regeneracao da raca e o tanque passam por esta porta)", !pl.Combate.Corpo.Regenerar(rabo) && rabo.Decepado);
		pl.Combate.Corpo.Restaurar();
		AfirmarV2("...nem RENASCENDO: a restauracao do corpo inteiro devolve tudo, menos ele",
				  rabo.Decepado && pl.Combate.Corpo.Partes.TrueForAll(p => p == rabo || !p.Decepado));
		var rng = new Random(3);
		bool gastou = false;
		for (int i = 0; i < 200 && !gastou; i++) gastou = Regeneracao.SortearMembroDoBuffer(pl.Combate.Corpo, rng) == rabo;
		AfirmarV2("...e o sorteio do membro que volta nem o considera (ele nao gasta a vez de membro nenhum)", !gastou && rabo.Decepado);
		AfirmarV2("AS VANTAGENS de nao ter rabo valem: o ritmo de treino sobe pra 1,25 (era 0,5 com ele)", pl.Ficha.tailgain == 1.25);
		AfirmarV2("...e o rabo perdido de vez nao conta como ferimento no boneco do HUD", pl.Sheet().MembrosRuins == 0, $"{pl.Sheet().MembrosRuins}");
		double[] foto = FotografarCorpo(pl.Combate)[RaboDoSaiyajin.Membro];
		// (2026-10-15) e o QUARTO numero e o relogio da ferida do perdido de vez -- dez minutos, ver `--v030teste` (familia R)
		AfirmarV2("a marca vai pro save junto do corpo (o terceiro numero da foto do membro; o quarto e a ferida que falta fechar)",
				  foto.Length == 4 && foto[1] > 0.5 && foto[2] > 0.5 && foto[3] == RaboDoSaiyajin.SegundosDaFerida, $"[{string.Join(", ", foto)}]");
		TirarDoMundoV2(pl);

		(pl, rabo) = ArrancarORaboV2(abaixo, "Saiyan");
		AfirmarV2("CONTRA-EXEMPLO: ABAIXO de 20 mil o rabo arrancado volta como sempre",
				  rabo.Decepado && !rabo.Perdido && pl.Combate.Corpo.Regenerar(rabo) && !rabo.Decepado);
		TirarDoMundoV2(pl);

		(pl, rabo) = ArrancarORaboV2(acima, Jandirus.Core.Forms.Oozaru.LinhagemPrimal);
		AfirmarV2("CONTRA-EXEMPLO: a linhagem PRIMAL (a do Oozaru e do SSJ4) nunca perde de vez, com poder nenhum",
				  rabo.Decepado && !rabo.Perdido && pl.Combate.Corpo.Regenerar(rabo));
		TirarDoMundoV2(pl);

		RaboDoSaiyajin.PerdaDefinitivaDesligadaDeTeste = true;
		try
		{
			(pl, rabo) = ArrancarORaboV2(acima, "Saiyan");
			AfirmarV2("(defeito injetado: o rabo volta sempre, como no DM) o nao-Primal de 25 mil o refaz -- a regua de cima reprovaria",
					  !rabo.Perdido && pl.Combate.Corpo.Regenerar(rabo));
			TirarDoMundoV2(pl);
		}
		finally { RaboDoSaiyajin.PerdaDefinitivaDesligadaDeTeste = false; }
	}

	// =====================================================================
	// 8) A FICHA QUE O HUD LE
	// =====================================================================
	private void AFichaV2()
	{
		GD.Print("[v020] -- 8) A FICHA: os bits de atordoado e de guarda que o HUD escreve (item 12)");
		ServerPlayer pl = ForjarV2("Avisado", TerraFirmeG12(4), 5_000);
		AfirmarV2("de pe e inteiro, a ficha nao diz nada: nem atordoado, nem guarda, nem agarrado",
				  pl.Sheet() is { Atordoado: false, Guarda: false, Agarrado: false, Segurando: false, RaboPreso: false, Estrangulado: false, Escape: 0 });
		pl.Combate.Stun = 1.5;
		AfirmarV2("ATORDOADO: com o relogio do atordoamento correndo, o bit vai na ficha", pl.Sheet().Atordoado);
		pl.Combate.Stun = 0;
		AfirmarV2("...e sai quando o relogio zera", !pl.Sheet().Atordoado);
		ErguerAGuarda(pl, true);
		AfirmarV2("GUARDA: erguida pela porta de producao, o bit vai na ficha", pl.Sheet().Guarda);
		ErguerAGuarda(pl, false);
		AfirmarV2("...e baixa com ela", !pl.Sheet().Guarda);
		TirarDoMundoV2(pl);
	}

	// =====================================================================
	// 9) O KI CONTRA O CORPO
	// =====================================================================
	/// <summary>O que um raio fez a um corpo, do disparo ate o raio acabar (ou o prazo vencer).</summary>
	/// <param name="AndouEmUmSegundo">Quanto o corpo andou nos primeiros <see cref="TiquesDaMedidaV2"/> tiques depois do contato (a regua da VELOCIDADE do arrasto).</param>
	/// <param name="Prensou">Em algum tique o raio esteve prensado contra o corpo (o bit que acende a estrela de um lado so).</param>
	private readonly record struct RaioMedidoV2(
		Projetil? Raio, ServerPlayer Atirador, ServerPlayer Alvo, double VidaAntes, float AndouEmUmSegundo, float Andou,
		bool Atordoou, bool AbriuEmbate, float PassoSobORaio, float PassoNormal, bool Prensou);

	/// <summary>A janela da medida de velocidade do arrasto: oito tiques (o arrasto inteiro de um raio curto dura menos de meio segundo).</summary>
	private const int TiquesDaMedidaV2 = 8;

	/// <summary>
	/// A ONDA DE KI DE PRODUCAO (base 1 -- entre iguais, abaixo do corte antigo de 10 de dano) de um atirador contra um
	/// corpo parado a cinco tiles, num corredor de terra firme. <paramref name="guarda"/> = a guarda SEGURADA (fora da
	/// janela do deflect). Corre pelos tiques de producao ate o raio acabar.
	/// </summary>
	private RaioMedidoV2 RaioContraCorpoV2(double bpDoAtirador, double bpDoAlvo, bool guarda, double alcanceTiles = 12)
	{
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		Vec2 chao = TerraFirmeG12((int)alcanceTiles + 6);
		ServerPlayer a = ForjarV2("Atirador", chao, bpDoAtirador);
		ServerPlayer d = ForjarV2("Alvo", chao + new Vec2(5 * ZoneCollision.TileSize, 0), bpDoAlvo);
		a.Facing = Facing.East;
		d.Facing = Facing.West;
		a.Ficha.Ki = a.Ficha.MaxKi;
		if (guarda) SegurarAGuarda(d);
		RecalcularVelocidade(d);
		float passoNormal = d.SpeedStat;
		double vida = d.Combate.Corpo.Vida();
		Vec2 deOnde = d.Pos;

		ReceitaDeProjetil receita = SemDeflexao();
		receita.AlcanceTiles = alcanceTiles;
		Canalizar(a, "Ki_Wave", 10 * a.Ficha.BaseDrain(), receita);

		Projetil? raio = null;
		bool atordoou = false, embate = false, prensou = false;
		float emUmSegundo = 0, passoSobORaio = passoNormal;
		int desdeOContato = -1;
		for (int i = 0; i < 30 * 20; i++)
		{
			raio ??= ProjeteisDaZona(a.Zone.Hash).FirstOrDefault(q => q.Dono == a.Id && q.Tipo == TipoDeProjetil.Beam);
			TiqueDoParry(d);
			atordoou |= d.Combate.Stun > 0;
			embate |= _emEmbateDeKi.ContainsKey(d.Id);
			prensou |= raio is { Vivo: true } && PrensadoContraUmCorpo(raio);
			if (NowMs() < d.SobORaioAteMs) passoSobORaio = Math.Min(passoSobORaio, d.SpeedStat);
			if (desdeOContato < 0 && (d.Combate.Corpo.Vida() < vida - 1e-9 || (d.Pos - deOnde).Length > 0.5f || raio is { Encostado: true })) desdeOContato = 0;
			else if (desdeOContato >= 0 && ++desdeOContato == TiquesDaMedidaV2) emUmSegundo = (d.Pos - deOnde).Length;
			if (raio is { Vivo: false }) break;
		}
		return new RaioMedidoV2(raio, a, d, vida, emUmSegundo, (d.Pos - deOnde).Length, atordoou, embate, passoSobORaio, passoNormal, prensou);
	}

	private static List<BodyPart> FeridosV2(ServerPlayer pl) => [.. pl.Combate.Corpo.Partes.Where(p => p.Vida < p.VidaMax - 1e-9)];

	private void OKiContraOCorpoV2()
	{
		GD.Print("[v020] -- 9) O KI CONTRA O CORPO: a escada por diferenca de poder (itens 5, 16 e 17)");
		const float tile = ZoneCollision.TileSize;

		// ---- a regra pura
		AfirmarV2("a escada: mais fraco abaixo de 1x, mais forte de 1x a 4x, parede de 4x pra cima",
				  KiContraCorpo.Posto(0.99) == PostoContraKi.MaisFraco && KiContraCorpo.Posto(1) == PostoContraKi.MaisForte
				  && KiContraCorpo.Posto(3.99) == PostoContraKi.MaisForte && KiContraCorpo.Posto(4) == PostoContraKi.Parede);
		AfirmarV2("DEFLETIR pede o DOBRO do poder do tiro: com 1,99x nao, com 2x sim",
				  !KiContraCorpo.PodeDefletir(1.99) && KiContraCorpo.PodeDefletir(2) && !KiContraCorpo.PodeDefletir(1));
		AfirmarV2("o arrasto e mais rapido quanto maior a diferenca, mais lento defendendo, e nenhum pra quem nao e mais fraco",
				  KiContraCorpo.FatorDoArrasto(0.1, false) > KiContraCorpo.FatorDoArrasto(0.9, false)
				  && KiContraCorpo.FatorDoArrasto(0.5, true) < KiContraCorpo.FatorDoArrasto(0.5, false)
				  && KiContraCorpo.FatorDoArrasto(0.9, true) > 0 && KiContraCorpo.FatorDoArrasto(1, false) == 0);

		// ---- (a) o raio de um IGUAL fere (o vilao do playtest)
		RaioMedidoV2 igual = RaioContraCorpoV2(5_000, 5_000, guarda: false);
		AfirmarV2("**O RAIO DE UM IGUAL FERE**: a Onda de Ki de quem tem o MESMO poder tira vida (no jogo de antes ela caia no corte e nao feria)",
				  igual.Alvo.Combate.Corpo.Vida() < igual.VidaAntes - 0.5, $"vida {igual.VidaAntes:0.##} -> {igual.Alvo.Combate.Corpo.Vida():0.##}");
		AfirmarV2("...quem tem poder igual NAO e levado pelo raio, nem atordoado, nem abre embate sem a guarda",
				  igual.Andou < 4 && !igual.Atordoou && !igual.AbriuEmbate, $"andou {igual.Andou:0.#} px");
		AfirmarV2($"...mas anda DEVAGAR enquanto apanha: o passo cai a {KiContraCorpo.AndarSobORaio * 100:0}% (da pra sair do raio, com custo)",
				  Math.Abs(igual.PassoSobORaio - igual.PassoNormal * KiContraCorpo.AndarSobORaio) < 0.01f * Math.Max(igual.PassoNormal, 1),
				  $"{igual.PassoNormal:0.###} -> {igual.PassoSobORaio:0.###}");

		KiContraCorpo.RegraDeAntesDeTeste = true;
		try
		{
			RaioMedidoV2 deAntes = RaioContraCorpoV2(5_000, 5_000, guarda: false);
			AfirmarV2("(defeito injetado: o corte por dano do DM) o mesmo raio do mesmo igual NAO fere -- era o vilao atirando sem efeito",
					  deAntes.Alvo.Combate.Corpo.Vida() >= deAntes.VidaAntes - 1e-9, $"vida {deAntes.Alvo.Combate.Corpo.Vida():0.##}");
		}
		finally { KiContraCorpo.RegraDeAntesDeTeste = false; }

		// ---- (b) a parede
		RaioMedidoV2 parede = RaioContraCorpoV2(5_000, 25_000, guarda: false);
		AfirmarV2("**A PAREDE**: quem tem cinco vezes o poder do tiro nem precisa defender -- o raio para nele sem ferir, sem levar e sem atrasar o passo",
				  parede.Alvo.Combate.Corpo.Vida() >= parede.VidaAntes - 1e-9 && parede.Andou < 1 && !parede.Atordoou
				  && parede.PassoSobORaio == parede.PassoNormal,
				  $"vida {parede.Alvo.Combate.Corpo.Vida():0.##}, andou {parede.Andou:0.#} px");

		// ---- (c) o mais fraco, sem defender: levado ate o fim do alcance, onde o raio estoura
		int explosoes = ExplosoesDoFimDoAlcanceDeTeste, crateras = CraterasDeExplosaoDeTeste;
		RaioMedidoV2 fraco = RaioContraCorpoV2(20_000, 5_000, guarda: false);
		// ATE O FIM DO ALCANCE, E NAO ALEM: a cabeca estoura onde o alcance acaba (ele e conferido uma vez por tique: ela
		// pode passar dele o que anda num tique), com o corpo na frente dela. Esta linha aceitava "de sete a doze tiles
		// adiante": o tique em que o raio BATE -- um em cada seis -- nao pagava o alcance, e a cabeca ia parar a 14 tiles
		// da mao num raio de 12. A medida do conserto (e o defeito injetado) moram na `--projetilteste`, familia 9.
		double cabecaDaMao = fraco.Raio == null ? 0 : Vec2.Distance(fraco.Raio.Pos, BocaDeCano.De(fraco.Atirador.Pos, fraco.Raio.Rumo)) / tile;
		double alcanceDoRaio = fraco.Raio?.MaxDistancia ?? 0, umTiqueDeRaio = fraco.Raio == null ? 0 : Protocol.TickSeconds / fraco.Raio.SegundosPorTile;
		double corpoDaCabeca = fraco.Raio == null ? 0 : Vec2.Distance(fraco.Alvo.Pos, fraco.Raio.Pos);
		AfirmarV2("**O MAIS FRACO E LEVADO**: sem defender ele fica atordoado e o raio o arrasta ate o fim do alcance -- e nao alem: a cabeca estoura onde o alcance acaba",
				  fraco.Atordoou && fraco.Raio != null && cabecaDaMao >= alcanceDoRaio - 0.02 && cabecaDaMao <= alcanceDoRaio + umTiqueDeRaio + 0.02
				  && Math.Abs(corpoDaCabeca - Feixe.ContatoNoCorpo(fraco.Raio)) < 1,
				  $"andou {fraco.Andou / tile:0.##} tiles; a cabeca estourou a {cabecaDaMao:0.##} tiles da mao (alcance {alcanceDoRaio:0}, um tique de raio sao {umTiqueDeRaio:0.##}), "
				  + $"com o corpo {corpoDaCabeca:0.#} px a frente dela");
		AfirmarV2("**NO FIM DO ALCANCE O RAIO ESTOURA**: o tiro acaba como explosao, e o chao ganha a cratera GRANDE",
				  fraco.Raio is { Vivo: false, Fim: FimDeProjetil.Explodiu } && ExplosoesDoFimDoAlcanceDeTeste == explosoes + 1
				  && CraterasDeExplosaoDeTeste == crateras + 1, $"fim {fraco.Raio?.Fim}, crateras +{CraterasDeExplosaoDeTeste - crateras}");
		List<BodyPart> feridos = FeridosV2(fraco.Alvo);
		AfirmarV2("...a explosao pega o corpo TODO por fora (diluida): quase todos os membros externos saem feridos",
				  feridos.Count(p => !p.Aninhado) >= 6, $"{feridos.Count(p => !p.Aninhado)} membro(s): {string.Join(", ", feridos.Select(p => p.Nome))}");

		// ---- (d) o mais fraco, DEFENDENDO: mais devagar, e o dano nos membros da guarda
		RaioMedidoV2 guardando = RaioContraCorpoV2(20_000, 5_000, guarda: true);
		List<BodyPart> naGuarda = FeridosV2(guardando.Alvo);
		AfirmarV2("**A GUARDA DO MAIS FRACO**: ele nao segura o raio nas maos (sem embate) -- e levado, e mais devagar que sem a guarda",
				  !guardando.AbriuEmbate && guardando.AndouEmUmSegundo > 4 && guardando.AndouEmUmSegundo < fraco.AndouEmUmSegundo * 0.75,
				  $"em {TiquesDaMedidaV2} tiques: {guardando.AndouEmUmSegundo:0.#} px defendendo, {fraco.AndouEmUmSegundo:0.#} px sem defender");
		AfirmarV2("**O DANO VAI NOS MEMBROS DA GUARDA**: so bracos e pernas saem feridos (as batidas e a explosao, concentrada)",
				  naGuarda.Count > 0 && naGuarda.TrueForAll(p => p.Zona is "bracos" or "pernas"),
				  string.Join(", ", naGuarda.Select(p => $"{p.Nome} {p.Vida:0}")));
		double piorNaGuarda = naGuarda.Count == 0 ? 100 : naGuarda.Min(p => p.Vida), piorSemGuarda = feridos.Count == 0 ? 100 : feridos.Min(p => p.Vida);
		AfirmarV2("...e concentrado doi mais NO MEMBRO: o pior membro de quem defendeu sai pior que o pior de quem levou no corpo todo, em menos lugares",
				  piorNaGuarda < piorSemGuarda && feridos.Count > naGuarda.Count, $"pior membro: {piorNaGuarda:0.#} defendendo ({naGuarda.Count} membros), {piorSemGuarda:0.#} sem defender ({feridos.Count})");

		// ---- (d2) a estrela de um lado so: o raio prensado contra a guarda, ou contra quem e parede
		AfirmarV2("**A COLISAO SE VE NA GUARDA DO MAIS FRACO**: o raio que o empurra com a guarda erguida viaja como PRENSADO (a estrela de um lado so)",
				  guardando.Prensou && !guardando.AbriuEmbate);
		AfirmarV2("**...E EM QUEM E PAREDE**, mesmo sem defender: o raio parado nele tambem acende a colisao", parede.Prensou);
		AfirmarV2("CONTRA-EXEMPLO: quem leva o raio em cheio (o igual sem guarda, o mais fraco sem guarda) nao tem colisao nenhuma -- e golpe, nao disputa",
				  !igual.Prensou && !fraco.Prensou);
		EstrelaSoNaDisputaDeTeste = true;
		try
		{
			RaioMedidoV2 semEstrela = RaioContraCorpoV2(20_000, 5_000, guarda: true);
			AfirmarV2("(defeito injetado: a estrela so na disputa) o raio empurra a guarda do mais fraco sem colisao nenhuma na tela -- o jogo de antes",
					  !semEstrela.Prensou && semEstrela.Andou > 2 * tile);
		}
		finally { EstrelaSoNaDisputaDeTeste = false; }

		// ---- (e) a velocidade do arrasto varia com a diferenca de forca
		RaioMedidoV2 quaseIgual = RaioContraCorpoV2(6_000, 5_000, guarda: false);
		AfirmarV2("**DEVAGAR PERTO DO EMPATE**: quem e so um pouco mais fraco e empurrado bem mais devagar que quem tem um quarto do poder",
				  quaseIgual.AndouEmUmSegundo > 2 && quaseIgual.AndouEmUmSegundo < fraco.AndouEmUmSegundo * 0.6,
				  $"em {TiquesDaMedidaV2} tiques: {quaseIgual.AndouEmUmSegundo:0.#} px (5/6 do poder) contra {fraco.AndouEmUmSegundo:0.#} px (1/4)");

		// ---- (f) todo raio estoura no fim do alcance, acertando ou nao
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		explosoes = ExplosoesDoFimDoAlcanceDeTeste;
		crateras = CraterasDeExplosaoDeTeste;
		ServerPlayer so = ForjarV2("Sozinho", TerraFirmeG12(14), 5_000);
		so.Facing = Facing.East;
		ReceitaDeProjetil curta = SemDeflexao();
		curta.AlcanceTiles = 8;
		Canalizar(so, "Ki_Wave", 10 * so.Ficha.BaseDrain(), curta);
		Projetil? noVazio = null;
		for (int i = 0; i < 30 * 12; i++)
		{
			noVazio ??= ProjeteisDaZona(so.Zone.Hash).FirstOrDefault(q => q.Dono == so.Id);
			TiqueDoParry(so);
			if (noVazio is { Vivo: false }) break;
		}
		AfirmarV2("**ACERTANDO OU NAO**: o raio que nao encontrou ninguem tambem estoura no fim do alcance, com a cratera grande",
				  noVazio is { Vivo: false, Fim: FimDeProjetil.Explodiu } && ExplosoesDoFimDoAlcanceDeTeste == explosoes + 1
				  && CraterasDeExplosaoDeTeste == crateras + 1, $"fim {noVazio?.Fim}");

		// ---- (g) a esfera grande tambem; a bola comum estoura PEQUENO (ate a v0.2.0 ela so se apagava: dono, 2026-10-13,
		//      *"quando uma esfera de ki se afasta muito ela so some e n explode"* -- medido na `--v021teste`, K1)
		LimparTudoDaBancada();
		explosoes = ExplosoesDoFimDoAlcanceDeTeste;
		crateras = CraterasDeExplosaoDeTeste;
		so = ForjarV2("Sozinho", TerraFirmeG12(14), 5_000);
		so.Facing = Facing.East;
		Projetil grande = Disparar(so, new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 6, EscalaVisual = 2, Deflectivel = false, Nome = "esfera grande" });
		Projetil comum = Disparar(so, new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 6, Deflectivel = false, Nome = "bola comum" });
		for (int i = 0; i < 30 * 12 && (grande.Vivo || comum.Vivo); i++) TickDosProjeteis(Protocol.TickSeconds);
		AfirmarV2("**A ESFERA GRANDE TAMBEM ESTOURA** no fim do alcance (com a cratera grande); a bola comum estoura pequeno, sem o estouro grande nem a cratera dele",
				  grande.EsferaGrande && !comum.EsferaGrande && grande.Fim == FimDeProjetil.Explodiu && comum.Fim == FimDeProjetil.Estourou
				  && ExplosoesDoFimDoAlcanceDeTeste == explosoes + 1 && CraterasDeExplosaoDeTeste == crateras + 1,
				  $"grande {grande.Fim}, comum {comum.Fim}, crateras +{CraterasDeExplosaoDeTeste - crateras}");

		// ---- (h) a esfera grande LEVA o mais fraco ate o fim, e estoura nele
		LimparTudoDaBancada();
		explosoes = ExplosoesDoFimDoAlcanceDeTeste;
		Vec2 corredor = TerraFirmeG12(16);
		ServerPlayer forte = ForjarV2("Forte", corredor, 20_000);
		ServerPlayer leve = ForjarV2("Leve", corredor + new Vec2(4 * tile, 0), 5_000);
		forte.Facing = Facing.East;
		double vidaDoLeve = leve.Combate.Corpo.Vida();
		Vec2 deOndeOLeve = leve.Pos;
		Projetil esfera = Disparar(forte, new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 10, EscalaVisual = 2, Deflectivel = false, Nome = "esfera grande" });
		for (int i = 0; i < 30 * 20 && esfera.Vivo; i++) { TickDosProjeteis(Protocol.TickSeconds); leve.Combate.Tick(Protocol.TickSeconds); }
		AfirmarV2("**A ESFERA GRANDE LEVA O MAIS FRACO** ate o fim do alcance e estoura nele (a bola comum acabaria no primeiro toque)",
				  esfera.Fim == FimDeProjetil.Explodiu && (leve.Pos - deOndeOLeve).Length > 3 * tile && leve.Combate.Corpo.Vida() < vidaDoLeve - 0.5
				  && ExplosoesDoFimDoAlcanceDeTeste == explosoes + 1,
				  $"fim {esfera.Fim}, andou {(leve.Pos - deOndeOLeve).Length / tile:0.#} tiles, vida {vidaDoLeve:0.#} -> {leve.Combate.Corpo.Vida():0.#}");

		// ---- (j) o estouro fere EM AREA (o retorno do dono sobre a primeira entrega)
		AExplosaoEmAreaV2(tile);

		// ---- (i) o jogo de antes: o raio so se apaga
		KiContraCorpo.RegraDeAntesDeTeste = true;
		try
		{
			LimparTudoDaBancada();
			explosoes = ExplosoesDoFimDoAlcanceDeTeste;
			so = ForjarV2("Sozinho", TerraFirmeG12(14), 5_000);
			so.Facing = Facing.East;
			ReceitaDeProjetil velha = SemDeflexao();
			velha.AlcanceTiles = 8;
			Canalizar(so, "Ki_Wave", 10 * so.Ficha.BaseDrain(), velha);
			Projetil? deAntes = null;
			for (int i = 0; i < 30 * 12; i++)
			{
				deAntes ??= ProjeteisDaZona(so.Zone.Hash).FirstOrDefault(q => q.Dono == so.Id);
				TiqueDoParry(so);
				if (deAntes is { Vivo: false }) break;
			}
			AfirmarV2("(defeito injetado: o jogo de antes) o raio que chega ao fim do alcance so se APAGA, sem estouro -- a regua de cima reprovaria",
					  deAntes is { Vivo: false } && deAntes.Fim != FimDeProjetil.Explodiu && ExplosoesDoFimDoAlcanceDeTeste == explosoes, $"fim {deAntes?.Fim}");
		}
		finally { KiContraCorpo.RegraDeAntesDeTeste = false; }
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
	}

	/// <summary>Um Ki Wave disparado pro leste com este alcance, ate acabar. Devolve o raio morto (onde ele parou, e que fim teve).</summary>
	/// <param name="aoNascer">Chamado UMA vez, no tique em que o raio aparece na zona (pra a cena marcar alguma coisa nele).</param>
	private Projetil? RaioAteOFimV2(ServerPlayer atirador, double alcanceTiles, Action<Projetil>? aoNascer = null)
	{
		atirador.Facing = Facing.East;
		ReceitaDeProjetil receita = SemDeflexao();
		receita.AlcanceTiles = alcanceTiles;
		Canalizar(atirador, "Ki_Wave", 10 * atirador.Ficha.BaseDrain(), receita);
		Projetil? raio = null;
		for (int i = 0; i < 30 * 12; i++)
		{
			if (raio == null && ProjeteisDaZona(atirador.Zone.Hash).FirstOrDefault(q => q.Dono == atirador.Id) is { } nasceu)
			{
				raio = nasceu;
				aoNascer?.Invoke(nasceu);
			}
			TiqueDoParry(atirador);
			if (raio is { Vivo: false }) break;
		}
		return raio;
	}

	/// <summary>
	/// 9j) O ESTOURO FERE EM AREA -- o retorno do dono sobre a primeira entrega da v0.2.0, que so feria quem vinha na
	/// ponta do tiro: *"A explosao de ki deveria dar dano em area sim mesmo quem n foi arrastado"*. Tres cenas: o raio
	/// que estoura no fim do alcance com gente em volta, quem atirou dentro do raio da propria explosao, e a esfera
	/// grande que acaba num corpo que ela nao leva -- o de poder igual, que apanha do golpe, e o de quem e PAREDE
	/// pra ela, que nao sente nada (o dono, perguntado em 2026-10-12 se ela estoura ali ou some: *"Estoura ali"*).
	/// </summary>
	private void AExplosaoEmAreaV2(float tile)
	{
		double alcance = KiContraCorpo.TilesDaExplosao(1) * tile;
		static double Perda(ServerPlayer pl) => 100 - pl.Combate.Corpo.Vida();
		static int FeridosPorFora(ServerPlayer pl) => FeridosV2(pl).Count(m => !m.Aninhado);

		// ---- a sonda: onde este raio estoura, sem ninguem por perto (a fisica e a mesma na cena de verdade)
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		Vec2 mao = TerraFirmeG12(16);
		Projetil? sonda = RaioAteOFimV2(ForjarV2("Sonda", mao, 10_000), 8);
		AfirmarV2("(montagem) a sonda: o raio de 8 tiles estoura no fim do alcance, sem ninguem em volta",
				  sonda is { Vivo: false, Fim: FimDeProjetil.Explodiu }, $"fim {sonda?.Fim}");
		if (sonda == null) return;
		Vec2 estouro = sonda.Pos;

		(ServerPlayer Atirador, ServerPlayer Perto, ServerPlayer NaBeira, ServerPlayer Longe, ServerPlayer Muralha,
		 ServerPlayer NoAlto, ServerPlayer DeGuarda, Projetil? Raio) Cena()
		{
			LimparEmbatesDaBancada();
			LimparTudoDaBancada();
			ServerPlayer atirador = ForjarV2("Bombardeiro", mao, 10_000);
			// todos FORA DA LINHA do raio (dois tiles ou mais de lado): nenhum deles e tocado pela cabeca que passa
			ServerPlayer perto = ForjarV2("PertoDoEstouro", estouro + new Vec2(0, 2 * tile), 5_000);
			ServerPlayer naBeira = ForjarV2("NaBeiraDoEstouro", estouro + new Vec2(0, 3.6f * tile), 5_000);
			ServerPlayer longe = ForjarV2("LongeDoEstouro", estouro + new Vec2(0, 6 * tile), 5_000);
			ServerPlayer muralha = ForjarV2("MuralhaNoEstouro", estouro + new Vec2(2 * tile, -2 * tile), 50_000);
			ServerPlayer noAlto = ForjarV2("NoAltoDoEstouro", estouro + new Vec2(-2 * tile, 2 * tile), 5_000);
			noAlto.Altitude = Voo.AlturaQueAtravessa;
			ServerPlayer deGuarda = ForjarV2("DeGuardaNoEstouro", estouro + new Vec2(-2 * tile, -2 * tile), 5_000);
			deGuarda.Combate.Guardar(true);
			return (atirador, perto, naBeira, longe, muralha, noAlto, deGuarda, RaioAteOFimV2(atirador, 8));
		}

		var c = Cena();
		Projetil? raioDaCena = c.Raio;
		AfirmarV2("(montagem) o raio estourou no mesmo ponto da sonda; dois corpos estao DENTRO do raio da explosao (4 tiles) e um FORA; um esta de guarda",
				  raioDaCena is { Vivo: false, Fim: FimDeProjetil.Explodiu } && Vec2.Distance(raioDaCena.Pos, estouro) < 1
				  && Vec2.Distance(c.Perto.Pos, estouro) < alcance && Vec2.Distance(c.NaBeira.Pos, estouro) < alcance
				  && Vec2.Distance(c.Longe.Pos, estouro) > alcance && c.DeGuarda.Combate.Bloqueando,
				  $"fim {raioDaCena?.Fim}; raio da explosao {alcance / tile:0.#} tiles; guarda {c.DeGuarda.Combate.Bloqueando}");
		AfirmarV2("**O ESTOURO FERE EM AREA**: quem estava perto apanha SEM ter sido tocado pelo raio nem arrastado -- no corpo todo, por fora",
				  Perda(c.Perto) > 1e-6 && FeridosPorFora(c.Perto) >= 6, $"perdeu {Perda(c.Perto):0.###}; {FeridosPorFora(c.Perto)} membro(s) de fora feridos");
		AfirmarV2("**QUANTO MAIS LONGE DO CENTRO, MENOS**: a dois tiles chegam 62,5% da explosao, a 3,6 chegam 32,5% -- e alem do raio dela, nada",
				  Perda(c.NaBeira) > 1e-6 && Perda(c.Perto) > Perda(c.NaBeira) * 1.4 && Perda(c.Longe) < 1e-9,
				  $"perto {Perda(c.Perto):0.###}, na beira {Perda(c.NaBeira):0.###}, longe {Perda(c.Longe):0.###}");
		AfirmarV2("quem e PAREDE pro tiro (cinco vezes mais forte) nao sente o estouro, como nao sente o raio", Perda(c.Muralha) < 1e-9, $"{Perda(c.Muralha):0.###}");
		AfirmarV2("quem esta NOUTRA ALTURA nao e pego (a regra de todo tiro: do chao nao se alcanca quem voa)", Perda(c.NoAlto) < 1e-9, $"{Perda(c.NoAlto):0.###}");
		List<BodyPart> naGuarda = FeridosV2(c.DeGuarda);
		AfirmarV2("quem estava DE GUARDA leva o estouro concentrado nos membros da guarda (so bracos ou pernas)",
				  naGuarda.Count > 0 && naGuarda.TrueForAll(m => m.Zona is "bracos" or "pernas"), string.Join(", ", naGuarda.Select(m => $"{m.Nome} {m.Vida:0.#}")));
		AfirmarV2("quem ATIROU nao se fere (aqui ele esta longe; a cena de baixo o poe dentro do raio da explosao)", Perda(c.Atirador) < 1e-9);

		KiContraCorpo.ExplosaoSemAreaDeTeste = true;
		try
		{
			var semArea = Cena();
			AfirmarV2("(defeito injetado: o estouro sem area) a mesma cena nao fere NINGUEM em volta -- a primeira entrega, que a regua de cima reprova",
					  semArea.Raio is { Fim: FimDeProjetil.Explodiu } && Perda(semArea.Perto) < 1e-9 && Perda(semArea.NaBeira) < 1e-9 && Perda(semArea.DeGuarda) < 1e-9,
					  $"perto {Perda(semArea.Perto):0.###}");
		}
		finally { KiContraCorpo.ExplosaoSemAreaDeTeste = false; }

		// ---- quem DEFLETIU o tiro nao e pego pelo estouro dele (`Projetil.Desviador`, a marca que o deflect escreve)
		(ServerPlayer QuemDefletiu, ServerPlayer Outro, Projetil? Raio) CenaDoDefletido()
		{
			LimparEmbatesDaBancada();
			LimparTudoDaBancada();
			ServerPlayer atirador = ForjarV2("DonoDoDefletido", mao, 10_000);
			ServerPlayer quemDefletiu = ForjarV2("QuemDefletiu", estouro + new Vec2(0, 2 * tile), 5_000);
			ServerPlayer outro = ForjarV2("OutroPerto", estouro + new Vec2(0, -2 * tile), 5_000);
			// (a marca e posta A MAO, no raio recem-nascido: quem a escreve em jogo e o deflect, medido na `--embatekiteste` 6b)
			return (quemDefletiu, outro, RaioAteOFimV2(atirador, 8, raio => raio.Desviador = quemDefletiu.Id));
		}

		var d = CenaDoDefletido();
		AfirmarV2("**QUEM DEFLETIU O TIRO FICA FORA DO ESTOURO DELE**: dois corpos a mesma distancia do estouro -- o marcado como quem o defletiu sai ileso, o outro apanha",
				  d.Raio is { Fim: FimDeProjetil.Explodiu } && d.Raio.Desviador == d.QuemDefletiu.Id && Perda(d.QuemDefletiu) < 1e-9 && Perda(d.Outro) > 1e-6,
				  $"quem defletiu perdeu {Perda(d.QuemDefletiu):0.###}; o outro, {Perda(d.Outro):0.###}");
		KiContraCorpo.EstouroPegaQuemDefletiuDeTeste = true;
		try
		{
			var semARegra = CenaDoDefletido();
			AfirmarV2("(defeito injetado: o estouro pega quem defletiu) o deflect certo termina em dano pra quem o fez -- a regua de cima reprova",
					  Perda(semARegra.QuemDefletiu) > 1e-6, $"{Perda(semARegra.QuemDefletiu):0.###}");
		}
		finally { KiContraCorpo.EstouroPegaQuemDefletiuDeTeste = false; }

		// ---- quem atirou, DENTRO do raio da propria explosao
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
		ServerPlayer deCurto = ForjarV2("DeCurto", mao, 10_000);
		ServerPlayer aoLado = ForjarV2("AoLadoDoCurto", mao + new Vec2(2 * tile, 2 * tile), 5_000);
		Projetil? curto = RaioAteOFimV2(deCurto, 2);
		AfirmarV2("**QUEM ATIROU NAO SE FERE COM O PROPRIO ESTOURO**: o raio de 2 tiles estoura com ele DENTRO do raio da explosao -- o vizinho apanha, ele nao",
				  curto is { Vivo: false, Fim: FimDeProjetil.Explodiu } && Vec2.Distance(deCurto.Pos, curto.Pos) < alcance
				  && Perda(deCurto) < 1e-9 && Perda(aoLado) > 1e-6,
				  $"fim {curto?.Fim}; o vizinho perdeu {Perda(aoLado):0.###}, o atirador {Perda(deCurto):0.###}");

		// ---- a esfera grande que acaba num corpo que ela NAO leva: o vizinho apanha da area; o alvo, so do golpe
		(ServerPlayer Alvo, ServerPlayer Vizinho, Projetil Esfera) CenaDaEsfera(double bpDoAlvo = 5_000)
		{
			LimparEmbatesDaBancada();
			LimparTudoDaBancada();
			ServerPlayer lancador = ForjarV2("Lancador", mao, 5_000);
			lancador.Facing = Facing.East;
			ServerPlayer alvo = ForjarV2("AlvoDaEsfera", mao + new Vec2(4 * tile, 0), bpDoAlvo);
			ServerPlayer vizinho = ForjarV2("VizinhoDaEsfera", mao + new Vec2(4 * tile, 3 * tile), 5_000);
			Projetil esfera = Disparar(lancador, new ReceitaDeProjetil
			{
				Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 10, EscalaVisual = 2, Deflectivel = false, Nome = "esfera grande",
			});
			for (int i = 0; i < 30 * 20 && esfera.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
			return (alvo, vizinho, esfera);
		}

		var e = CenaDaEsfera();
		// (O GOLPE EM CHEIO FERE UM MEMBRO SORTEADO -- e o que escorre dele pro membro que ele carrega: o Abdomen leva
		//  o Reprodutor junto, que nao e aninhado (`Body.cs`, `dono: "Abdomen"`). Por isso "um ou dois", e nao "um": a
		//  regua de um so reprovava as corridas em que o sorteio caia no Abdomen, 70 de 450 -- achado pela sessao da
		//  limpeza da dobra, na rodada dela.)
		AfirmarV2("**A ESFERA GRANDE QUE ACABA NUM CORPO TAMBEM FERE EM AREA**: o alvo (de poder igual, que ela nao leva) apanha do golpe num lugar so; o vizinho a tres tiles, do estouro, no corpo todo",
				  e.Esfera.Fim == FimDeProjetil.Acertou && Perda(e.Alvo) > 1e-6 && FeridosPorFora(e.Alvo) is 1 or 2
				  && Perda(e.Vizinho) > 1e-6 && FeridosPorFora(e.Vizinho) >= 6,
				  $"fim {e.Esfera.Fim}; alvo: {FeridosPorFora(e.Alvo)} membro(s) de fora; vizinho: {FeridosPorFora(e.Vizinho)}");
		KiContraCorpo.ExplosaoSemAreaDeTeste = true;
		try
		{
			var semArea = CenaDaEsfera();
			AfirmarV2("(defeito injetado: o estouro sem area) o vizinho da esfera sai ileso",
					  semArea.Esfera.Fim == FimDeProjetil.Acertou && Perda(semArea.Vizinho) < 1e-9, $"{Perda(semArea.Vizinho):0.###}");
		}
		finally { KiContraCorpo.ExplosaoSemAreaDeTeste = false; }

		// ---- ...e a que acaba em quem e PAREDE pra ela. A MESMA cena, com o alvo cinco vezes mais forte: ele nao sente
		//      nada -- nem o golpe (`EstourarSemFerir`), nem a area (`Estourar` pula quem e parede) --, mas o chao e o
		//      vizinho sentem. ATE 2026-10-12 A ESFERA SO SUMIA NO CORPO DELE: o ramo da parede devolvia antes das
		//      chamadas do estouro (medido naquele dia: cratera +0, e o vizinho de 100 a 100 de vida).
		int crateras = CraterasDeExplosaoDeTeste;
		var naParede = CenaDaEsfera(bpDoAlvo: 25_000);
		AfirmarV2("(montagem) o alvo e PAREDE pra esfera grande (cinco vezes o poder dela), e e nele que ela acaba",
				  naParede.Esfera.EsferaGrande && KiContraCorpo.EhParede(VezesOPoderDoTiro(naParede.Esfera, naParede.Alvo))
				  && naParede.Esfera.Fim == FimDeProjetil.Acertou && Vec2.Distance(naParede.Esfera.Pos, naParede.Alvo.Pos) <= Projetil.RaioDeImpacto + 0.5f,
				  $"{VezesOPoderDoTiro(naParede.Esfera, naParede.Alvo):0.###}x o poder do tiro; fim {naParede.Esfera.Fim} a {Vec2.Distance(naParede.Esfera.Pos, naParede.Alvo.Pos):0.#} px do corpo");
		AfirmarV2("**A ESFERA GRANDE ESTOURA TAMBEM NA PAREDE**: o chao ganha a cratera grande e o vizinho a tres tiles apanha do estouro, no corpo todo",
				  CraterasDeExplosaoDeTeste == crateras + 1 && Perda(naParede.Vizinho) > 1e-6 && FeridosPorFora(naParede.Vizinho) >= 6,
				  $"crateras +{CraterasDeExplosaoDeTeste - crateras}; o vizinho perdeu {Perda(naParede.Vizinho):0.###} ({FeridosPorFora(naParede.Vizinho)} membro(s) de fora feridos)");
		AfirmarV2("...e A PAREDE NAO SENTE: no centro do estouro, sai sem um membro ferido -- nem pelo golpe, nem pela area",
				  Perda(naParede.Alvo) < 1e-9 && FeridosV2(naParede.Alvo).Count == 0,
				  $"perdeu {Perda(naParede.Alvo):0.###}; {FeridosV2(naParede.Alvo).Count} membro(s) feridos");

		EsferaSomeNaParedeDeTeste = true;
		try
		{
			crateras = CraterasDeExplosaoDeTeste;
			var some = CenaDaEsfera(bpDoAlvo: 25_000);
			AfirmarV2("(defeito injetado: a esfera grande some na parede) ela acaba no corpo sem cratera e sem ferir o vizinho -- o buraco de 2026-10-12, que a regua de cima reprova",
					  some.Esfera.Fim == FimDeProjetil.Acertou && CraterasDeExplosaoDeTeste == crateras && Perda(some.Vizinho) < 1e-9,
					  $"fim {some.Esfera.Fim}; crateras +{CraterasDeExplosaoDeTeste - crateras}; o vizinho perdeu {Perda(some.Vizinho):0.###}");
		}
		finally { EsferaSomeNaParedeDeTeste = false; }

		// (NO JOGO DE ANTES NENHUMA ESFERA ESTOURAVA, e este alvo caia no MESMO ramo por outro motivo: o corte por dano.
		//  A regua confere as duas coisas que provam que a cena passou por ele -- o dano final desta esfera neste corpo
		//  fica abaixo do corte, e o corpo sai ileso.)
		KiContraCorpo.RegraDeAntesDeTeste = true;
		try
		{
			crateras = CraterasDeExplosaoDeTeste;
			var deAntes = CenaDaEsfera(bpDoAlvo: 25_000);
			double danoDeAntes = DanoFinalDe(deAntes.Esfera, deAntes.Alvo);
			AfirmarV2("(defeito injetado: o jogo de antes) a esfera grande cai no corte por dano e some no corpo, sem estouro nenhum -- a regua de cima reprovaria",
					  deAntes.Esfera.Fim == FimDeProjetil.Acertou && DanoDeKi.FracoDemais(danoDeAntes) && Perda(deAntes.Alvo) < 1e-9
					  && CraterasDeExplosaoDeTeste == crateras && Perda(deAntes.Vizinho) < 1e-9,
					  $"fim {deAntes.Esfera.Fim}; dano final {danoDeAntes:0.###} (o corte e {DanoDeKi.CorteDoFraco:0.#}); o alvo perdeu {Perda(deAntes.Alvo):0.###}; "
					  + $"crateras +{CraterasDeExplosaoDeTeste - crateras}; o vizinho perdeu {Perda(deAntes.Vizinho):0.###}");
		}
		finally { KiContraCorpo.RegraDeAntesDeTeste = false; }
		LimparEmbatesDaBancada();
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 10) OS PESOS
	// =====================================================================
	private void OsPesosV2()
	{
		GD.Print("[v020] -- 10) OS PESOS: 50 kg de fabrica, o maximo se compra, e se veste em % dele (item 3)");

		// ---- a regra pura
		AfirmarV2("de fabrica os pesos aguentam 50 kg; cada melhoria DOBRA; o preco sobe 500 zeni por degrau",
				  PesoDeTreino.Maximo(0) == 50 && PesoDeTreino.ProximoMaximo(0) == 100 && PesoDeTreino.ProximoMaximo(100) == 200
				  && PesoDeTreino.PrecoEmZeni(0) == 500 && PesoDeTreino.PrecoEmZeni(100) == 1000 && PesoDeTreino.PrecoEmZeni(200) == 1500
				  && PesoDeTreino.Nivel(0) == 0 && PesoDeTreino.Nivel(400) == 3);

		// ---- pela porta de producao (`item_ajustar`, `item_melhorar`)
		ServerPlayer pl = ForjarV2("Pesado", TerraFirmeG12(4), 5_000);
		Guardar(pl, CatalogoDeItens.Pesos);
		// (2026-10-18) O PESO E UM ITEM COM ESTADO: melhorar troca o id dele (o maximo vai no id). A bancada segue o id de AGORA.
		string Pesos() => pl.Mochila.Pilhas.Find(p => PesoDeTreino.EhPeso(p.Id)).Id ?? "";
		double Max() => PesoDeTreino.MaximoDe(Pesos());
		pl.Ficha.Zeni = 2_000;
		pl.Ficha.Recursos = 0;
		double limiteDoCorpo = pl.Ficha.weight_cap_hw;

		ComandoDeItem(pl, "item_ajustar", $"{Pesos()}/100");
		AfirmarV2("**OS PESOS VEM LEVES**: recem-fabricados, 100% do maximo sao 50 kg no corpo",
				  Math.Abs(pl.Ficha.Weighted - 50) < 1e-6, $"{pl.Ficha.Weighted:0.###} kg (o corpo aguenta {limiteDoCorpo:0})");
		AfirmarV2("...e 50 kg num corpo de 5 mil de poder nao rendem nada: o ganho de treino fica em 1x (falta melhorar)",
				  Math.Abs(pl.Ficha.weight - 1) < 1e-9 && pl.Ficha.weight_ratio < 0.5, $"ganho {pl.Ficha.weight:0.##}x, razao do peso {pl.Ficha.weight_ratio:0.####}");
		List<string> doAjuste = Ouvir(() => ComandoDeItem(pl, "item_ajustar", $"{Pesos()}/100"));
		AfirmarV2("...a frase do ajuste diz os quilos e que estao LEVES DEMAIS -- em palavra, e nao numa razao que entregaria o poder de quem le",
				  Disse(doAjuste, "50 de 50 kg") && Disse(doAjuste, "leve demais") && !Disse(doAjuste, "x o que seu corpo"), Ultimos(doAjuste));
		ComandoDeItem(pl, "item_ajustar", $"{Pesos()}/40");
		AfirmarV2("**A PORCENTAGEM E DO MAXIMO**: 40% de 50 kg sao 20 kg", Math.Abs(pl.Ficha.Weighted - 20) < 1e-6, $"{pl.Ficha.Weighted:0.###} kg");

		List<string> falas = Ouvir(() => ComandoDeItem(pl, "item_melhorar", Pesos()));
		AfirmarV2("**O MAXIMO SE COMPRA**: a primeira melhoria custa 500 zeni e leva o maximo a 100 kg -- o maximo DESTE peso, no id dele",
				  Max() == 100 && Pesos() == PesoDeTreino.IdDe(100, "") && pl.Ficha.Zeni == 1_500 && Disse(falas, "100 kg"), $"{Pesos()}, zeni {pl.Ficha.Zeni} | {Ultimos(falas)}");
		AfirmarV2("...e o que esta NO CORPO nao muda sozinho: continuam os 20 kg ate o jogador ajustar",
				  Math.Abs(pl.Ficha.Weighted - 20) < 1e-6, $"{pl.Ficha.Weighted:0.###} kg");
		ComandoDeItem(pl, "item_melhorar", Pesos());
		AfirmarV2("a segunda custa 1.000 e leva a 200 kg", Max() == 200 && pl.Ficha.Zeni == 500, $"max {Max()}, zeni {pl.Ficha.Zeni}");

		falas = Ouvir(() => ComandoDeItem(pl, "item_melhorar", Pesos()));
		AfirmarV2("**SEM ZENI NAO MELHORA**: a terceira (1.500) e recusada com o preco nas duas moedas, e o maximo fica onde estava",
				  Max() == 200 && pl.Ficha.Zeni == 500 && Disse(falas, "custa") && Disse(falas, "recursos"), Ultimos(falas));

		pl.Ficha.Recursos = Recurso.CustoEmRecursos(1_500);
		ComandoDeItem(pl, "item_melhorar", $"{Pesos()}/{Recurso.PalavraDeRecursos}");
		AfirmarV2("**OU COM RECURSOS**, pelo dobro: a mesma melhoria sai dos recursos e o zeni fica intacto",
				  Max() == 400 && pl.Ficha.Zeni == 500 && pl.Ficha.Recursos == 0, $"max {Max()}, zeni {pl.Ficha.Zeni}, recursos {pl.Ficha.Recursos}");

		ComandoDeItem(pl, "item_ajustar", $"{Pesos()}/100");
		AfirmarV2("com o maximo em 400 kg, 100% sao 400 kg", Math.Abs(pl.Ficha.Weighted - 400) < 1e-6, $"{pl.Ficha.Weighted:0.###} kg");

		// ---- o limite do CORPO continua onde estava: com pesos a altura dele, o treino rende e o excesso esmaga
		TrocarOPeso(pl, Pesos(), PesoDeTreino.IdDe(limiteDoCorpo * 2, ""));
		ComandoDeItem(pl, "item_ajustar", $"{Pesos()}/50");
		AfirmarV2("com pesos a altura do corpo, a metade do maximo poe o corpo no limite dele: o treino rende 2x (a conta do DM, intacta)",
				  Math.Abs(Esmagamento.Razao(pl.Ficha) - 1) < 0.01 && Math.Abs(pl.Ficha.weight - 2) < 0.02,
				  $"razao {Esmagamento.Razao(pl.Ficha):0.###}, ganho {pl.Ficha.weight:0.###}x");

		// ---- quem ja vestia mais do que os pesos dele aguentam volta com o que eles aguentam
		TrocarOPeso(pl, Pesos(), CatalogoDeItens.Pesos);   // o vestido volta a ser o de fabrica (o `TrocarOPeso` segue o vestido)
		pl.Ficha.Weighted = 80_000;
		PrepararCombate(pl, null);
		AfirmarV2("**O PERSONAGEM DE ANTES** (que vestia o dobro do limite do corpo, de graca) entra com o que os pesos dele aguentam: 50 kg",
				  Math.Abs(pl.Ficha.Weighted - 50) < 1e-6, $"{pl.Ficha.Weighted:0.###} kg");
		TrocarOPeso(pl, Pesos(), PesoDeTreino.IdDe(200, ""));
		pl.Ficha.Weighted = 150;
		PrepararCombate(pl, null);
		AfirmarV2("CONTRA-EXEMPLO: quem veste MENOS que o maximo pago (150 de 200 kg) entra com os mesmos 150",
				  Math.Abs(pl.Ficha.Weighted - 150) < 1e-6, $"{pl.Ficha.Weighted:0.###} kg");

		// ---- o jogo de antes
		PesoDeTreino.TetoDoCorpoDeGracaDeTeste = true;
		try
		{
			TrocarOPeso(pl, Pesos(), CatalogoDeItens.Pesos);
			ComandoDeItem(pl, "item_ajustar", $"{Pesos()}/100");
			AfirmarV2("(defeito injetado: o teto do corpo, de graca) sem pagar nada, 100% voltam a ser o DOBRO do limite do corpo -- a regua de cima reprovaria",
					  pl.Ficha.Weighted > 50 * 100 && Esmagamento.Razao(pl.Ficha) > 1.9, $"{pl.Ficha.Weighted:0} kg, razao {Esmagamento.Razao(pl.Ficha):0.##}");
		}
		finally { PesoDeTreino.TetoDoCorpoDeGracaDeTeste = false; }

		ComandoDeItem(pl, "item_tirar", Pesos());
		TirarDoMundoV2(pl);
	}

	// =====================================================================
	// 11) A MELHORIA DE UMA VEZ SO
	// =====================================================================
	private void AMelhoriaUnicaV2()
	{
		GD.Print("[v020] -- 11) A MELHORIA DE UMA VEZ SO: comprada, sai do menu (item 4)");
		string[] Lista(string tipo, byte feitas) =>
			[.. Interacoes.De($"{tipo}/upgrades").Where(a => !Interacoes.SaiDoMenu(tipo, a, feitas)).Select(a => a.Arg)];

		AfirmarV2("sem nada comprado, o regenerador oferece as cinco melhorias e a maquina de gravidade as seis (a trava de seguranca)",
				  Lista(Regenerador.Tipo, 0).Length == 5 && Lista("Gravity", 0).Length == 6,
				  $"{string.Join(",", Lista(Regenerador.Tipo, 0))} | {string.Join(",", Lista("Gravity", 0))}");
		AfirmarV2("**REGENERADOR**: com 'tratar ferimentos' comprada, so ELA sai da lista",
				  Lista(Regenerador.Tipo, Interacoes.MelhoriaUnicaFeita).SequenceEqual(
					  ["upgrades/recarga", "upgrades/bateria", "upgrades/velocidade", "upgrades/nanites"]),
				  string.Join(",", Lista(Regenerador.Tipo, Interacoes.MelhoriaUnicaFeita)));
		AfirmarV2("**GRAVIDADE**: a estabilizacao comprada sai; o alcance so sai quando chega ao teto",
				  !Lista("Gravity", Interacoes.MelhoriaUnicaFeita).Contains("upgrades/estabilidade")
				  && Lista("Gravity", Interacoes.MelhoriaUnicaFeita).Contains("upgrades/alcance")
				  && !Lista("Gravity", Interacoes.MelhoriaNoTeto).Contains("upgrades/alcance")
				  && Lista("Gravity", Interacoes.MelhoriaNoTeto).Contains("upgrades/estabilidade"));
		// (2026-10-21) A FORCA DO CAMPO TEM BIT DESDE O `ForcaNoTetoDoMundo`: no teto do mundo ela sai do menu, como o alcance no teto
		// dele, e o "255" a escondia -- esta linha ficou vermelha desde 2026-10-19. O resto da pergunta e o mesmo: a bateria e a
		// velocidade nao tem bit nenhum, e a forca so sai pelo dela.
		AfirmarV2("CONTRA-EXEMPLO: o que se compra varias vezes (bateria, velocidade) nunca sai, com bit nenhum -- e a forca do campo so pelo bit do teto do mundo",
				  Lista(Regenerador.Tipo, 255).Contains("upgrades/bateria") && Lista(Regenerador.Tipo, 255).Contains("upgrades/velocidade")
				  && Lista("Gravity", 255).Contains("upgrades/bateria")
				  && Lista("Gravity", (byte)(255 & ~Interacoes.ForcaNoTetoDoMundo)).Contains("upgrades/forca")
				  && !Lista("Gravity", Interacoes.ForcaNoTetoDoMundo).Contains("upgrades/forca"));
		AfirmarV2("...e o bit de uma maquina nao some com botao de OUTRO tipo (a Drill tem melhorias, e nenhuma e de uma vez so)",
				  Interacoes.De($"{Drill.Tipo}/upgrades").All(a => !Interacoes.SaiDoMenu(Drill.Tipo, a, 255)));

		Interacoes.MelhoriaFeitaFicaNoMenuDeTeste = true;
		try
		{
			AfirmarV2("(defeito injetado: a melhoria feita fica no menu) as cinco continuam na lista -- a regua sabe reprovar",
					  Lista(Regenerador.Tipo, Interacoes.MelhoriaUnicaFeita).Length == 5);
		}
		finally { Interacoes.MelhoriaFeitaFicaNoMenuDeTeste = false; }
	}

	// =====================================================================
	// 12) O VIGOR DE QUEM SE CONTEM
	// =====================================================================
	/// <summary>Quanto de vigor UMA passada da digestao devolve a um corpo cansado e de barriga cheia, com este poder em uso.</summary>
	private static double VigorDeUmaPassadaV2(double poderEmUso, bool relaxado = false, bool sumido = false)
	{
		var f = new Fighter { Race = "Human", BP = 5_000 };
		f.Tick(agoraMs: 0);
		f.stamina = f.maxstamina * 0.2;
		f.CurrentNutrition = Nutricao.Tanque(f.Metabolism);
		f.powerMod = poderEmUso;
		f.relaxado = relaxado;
		f.isconcealed = sumido;
		double antes = f.stamina;
		Nutricao.Digerir(f);
		return f.stamina - antes;
	}

	private void OVigorDeQuemSeContemV2()
	{
		GD.Print("[v020] -- 12) O VIGOR DE QUEM SE CONTEM: uma reta pela barra (1x a 2,5x), e 3,5x relaxado (item 13, com o retorno do dono)");
		double cheio = VigorDeUmaPassadaV2(1), metade = VigorDeUmaPassadaV2(0.5), decimo = VigorDeUmaPassadaV2(0.1),
			   piso = VigorDeUmaPassadaV2(PoderEmUso.Piso), relaxado = VigorDeUmaPassadaV2(1, relaxado: true);

		// A reta que o dono descreveu: *"uma função linear em relação a % do poder liberado, com o teto em 2,5x
		// quando chega em 1% do poder total"*. A 50% ela vale 1,758; a 10%, 2,364.
		static double RetaEm(double poder) => 1 + (PoderEmUso.DescansoNoPiso - 1) * (1 - poder) / (1 - PoderEmUso.Piso);

		AfirmarV2("(montagem) com o poder inteiro a digestao devolve vigor (a base da medida)", cheio > 0, $"{cheio:0.######}");
		AfirmarV2("**A 1% DO PODER (o piso da barra) O VIGOR VOLTA 2,5x MAIS DEPRESSA** -- o teto de quem so se segura",
				  PoderEmUso.DescansoNoPiso == 2.5 && Math.Abs(piso / cheio - 2.5) < 0.01, $"{piso / cheio:0.###}x");
		AfirmarV2("**RELAXADO, 3,5x** -- o maximo que ha, acima do piso da barra",
				  PoderEmUso.DescansoRelaxado == 3.5 && Math.Abs(relaxado / cheio - 3.5) < 0.01 && relaxado > piso, $"{relaxado / cheio:0.###}x");
		AfirmarV2("**E UMA RETA PELA % DO PODER LIBERADO**: 100% < 50% < 10% < 1%, e os pontos do meio caem em cima dela",
				  cheio < metade && metade < decimo && decimo < piso
				  && Math.Abs(metade / cheio - RetaEm(0.5)) < 0.005 && Math.Abs(decimo / cheio - RetaEm(0.1)) < 0.005,
				  $"50% {metade / cheio:0.###}x (reta {RetaEm(0.5):0.###}), 10% {decimo / cheio:0.###}x (reta {RetaEm(0.1):0.###}), 1% {piso / cheio:0.###}x");
		AfirmarV2("relaxado a barra nao soma: sao 3,5x com ela em 100% ou no piso",
				  Math.Abs(VigorDeUmaPassadaV2(PoderEmUso.Piso, relaxado: true) - relaxado) < 1e-9);
		AfirmarV2("SUMIR POR TECNICA (`isconcealed`) nao rende vigor por aqui: o que o sigilo da e a folga no DRENO, como no DM",
				  Math.Abs(VigorDeUmaPassadaV2(1, sumido: true) - cheio) < 1e-9);
		AfirmarV2("CONTRA-EXEMPLO: acima de 100% (carregando o ki) o descanso nao vira castigo -- o fator nao desce de 1",
				  Math.Abs(VigorDeUmaPassadaV2(1.5) - cheio) < 1e-9);

		PoderEmUso.DescansoNaoRendeDeTeste = true;
		try
		{
			AfirmarV2("(defeito injetado: baixar o poder nao rende vigor) no piso, relaxado e de poder inteiro recuperam IGUAL -- o jogo de antes, que a regua reprova",
					  Math.Abs(VigorDeUmaPassadaV2(PoderEmUso.Piso) - cheio) < 1e-9 && Math.Abs(VigorDeUmaPassadaV2(1, relaxado: true) - cheio) < 1e-9);
		}
		finally { PoderEmUso.DescansoNaoRendeDeTeste = false; }
	}

	// =====================================================================
	// 13) AS ROUPAS
	// =====================================================================
	/// <summary>As onze folhas da pasta `Clothes` que nao estavam no guarda-roupa ate 2026-10-12.</summary>
	internal static readonly string[] RoupasNovasDaV020 =
		["Black Goku Suit Fixed", "Blue Armor", "Halo", "Mega Buster", "potara", "Potara-Equipped", "potaraleft", "potararight",
		 "Red Armor", "TurlesArmor", "Uncoloured Armour Plating"];

	private void AsRoupasV2()
	{
		GD.Print("[v020] -- 13) AS ROUPAS: a pasta inteira no guarda-roupa da criacao, menos a do Beerus e o rabo (item 6)");
		if (_visual is not { } cat) { AfirmarV2("(montagem) o servidor tem o catalogo de aparencia", false); return; }

		const string pasta = "res://Assets/Sprites/Clothes";
		static bool Fora(string n) => n.StartsWith("Eyes_", StringComparison.OrdinalIgnoreCase)
									  || n.Contains("Tail", StringComparison.OrdinalIgnoreCase) || n.Contains("Beerus", StringComparison.OrdinalIgnoreCase);
		List<string> folhas = [.. DirAccess.GetFilesAt(pasta).Where(f => f.EndsWith(".tres", StringComparison.OrdinalIgnoreCase)).Select(f => f[..^5])];
		List<string> faltando = [.. folhas.Where(n => !Fora(n) && !cat.Roupas.Contains($"{pasta}/{n}.tres"))];
		AfirmarV2($"**TODA FOLHA DA PASTA `Clothes` ESTA NO GUARDA-ROUPA** da criacao ({folhas.Count} folhas na pasta, {cat.Roupas.Count} no catalogo)",
				  folhas.Count >= 170 && faltando.Count == 0, $"faltando: {string.Join(", ", faltando)}");
		AfirmarV2("...inclusive as onze que ficavam de fora: as seis que so tinham o PNG, o halo, o Mega Buster e os brincos",
				  RoupasNovasDaV020.All(n => cat.Roupas.Contains($"{pasta}/{n}.tres")),
				  string.Join(", ", RoupasNovasDaV020.Where(n => !cat.Roupas.Contains($"{pasta}/{n}.tres"))));
		AfirmarV2("**MENOS O RABO E A DO BEERUS** (e os olhos, que nao sao roupa: sao a camada de olhos de todo corpo)",
				  !cat.Roupas.Exists(c => Fora(VisualCatalog.NomeDoArquivo(c))) && folhas.Contains("Tail") && cat.Olhos == $"{pasta}/Eyes_Black.tres",
				  string.Join(", ", cat.Roupas.Where(c => Fora(VisualCatalog.NomeDoArquivo(c)))));
		AfirmarV2("CONTRA-EXEMPLO: armadura nao e guarda-roupa -- nenhuma folha da pasta `Armor` entra na grade da criacao",
				  !cat.Roupas.Exists(c => c.Contains("/Clothes/Armor/")) && cat.Armaduras.TrueForAll(c => c.Contains("/Clothes/Armor/")));

		// TODA FOLHA CARREGA E DESENHA: uma entrada do catalogo que nao abre e um botao em branco na grade.
		var quebradas = new List<string>();
		foreach (string c in cat.Roupas.Concat(cat.Armaduras))
		{
			var f = ResourceLoader.Exists(c) ? ResourceLoader.Load<SpriteFrames>(c) : null;
			if (f == null || !f.GetAnimationNames().Any(a => f.GetFrameCount(a) > 0 && f.GetFrameTexture(a, 0) != null)) quebradas.Add(c);
		}
		AfirmarV2($"toda folha do catalogo CARREGA e tem pelo menos um quadro com imagem ({cat.Roupas.Count} roupas + {cat.Armaduras.Count} armaduras)",
				  quebradas.Count == 0, string.Join(", ", quebradas));

		var ap = new Aparencia();
		foreach (string n in RoupasNovasDaV020.Take(Aparencia.MaxRoupa)) ap.Roupa.Add(new PecaDeRoupa($"{pasta}/{n}.tres", null));
		string queixa = cat.Sanear(ap, "Human", "Male");
		AfirmarV2("o servidor ACEITA as pecas novas na ficha de criacao (o `Sanear` nao tira nenhuma)",
				  ap.Roupa.Count == Aparencia.MaxRoupa && queixa.Length == 0, queixa);
	}

	// =====================================================================
	// 14) A ARMADURA DE TECH
	// =====================================================================
	/// <summary>A defesa fisica efetiva de uma ficha nova de Humano com este poder base, com ou sem armadura no corpo.</summary>
	private static double DefesaV2(double bp, bool armadura)
	{
		var f = new Fighter { Race = "Human", BP = bp, armadura = armadura };
		f.Tick(agoraMs: 0);
		return f.Ephysdef;
	}

	private void AArmaduraV2()
	{
		GD.Print("[v020] -- 14) A ARMADURA DE TECH: a arte se escolhe, protege no comeco e some ate os 50 mil (itens 6 e 14)");
		if (_visual is not { } cat) { AfirmarV2("(montagem) o servidor tem o catalogo de aparencia", false); return; }

		// ---- a regra pura
		AfirmarV2("a protecao e inteira no comeco, metade aos 25 mil e NENHUMA dos 50 mil pra cima",
				  ArmaduraDeTech.Fator(0) == 1 && ArmaduraDeTech.Fator(25_000) == 0.5 && ArmaduraDeTech.Fator(50_000) == 0
				  && ArmaduraDeTech.Fator(5_000_000) == 0);
		double semNoComeco = DefesaV2(100, false), comNoComeco = DefesaV2(100, true), semNoMeio = DefesaV2(25_000, false),
			   comNoMeio = DefesaV2(25_000, true), semNoFim = DefesaV2(60_000, false), comNoFim = DefesaV2(60_000, true);
		AfirmarV2("**A ARMADURA PROTEGE QUEM ESTA COMECANDO**: com 100 de poder a defesa fisica sobe mais da metade",
				  comNoComeco > semNoComeco * 1.5, $"{semNoComeco:0.###} -> {comNoComeco:0.###}");
		AfirmarV2("...rende MENOS no meio do caminho (25 mil de poder)",
				  comNoMeio > semNoMeio && comNoMeio / semNoMeio < comNoComeco / semNoComeco, $"{semNoMeio:0.###} -> {comNoMeio:0.###}");
		AfirmarV2("**PASSOU DOS 50 MIL, NAO FAZ DIFERENCA**: com 60 mil a defesa e a mesma com e sem armadura",
				  Math.Abs(comNoFim - semNoFim) < 1e-9, $"{semNoFim:0.###} / {comNoFim:0.###}");

		ArmaduraDeTech.NaoProtegeDeTeste = true;
		try
		{
			AfirmarV2("(defeito injetado: a armadura nao protege) com 100 de poder a defesa e a mesma com e sem ela -- o item parado de antes",
					  Math.Abs(DefesaV2(100, true) - semNoComeco) < 1e-9);
		}
		finally { ArmaduraDeTech.NaoProtegeDeTeste = false; }
		ArmaduraDeTech.ProtegeEmQualquerPoderDeTeste = true;
		try
		{
			AfirmarV2("(defeito injetado: a protecao nao some com o poder) aos 60 mil a armadura ainda protege -- a regua de cima reprovaria",
					  DefesaV2(60_000, true) > semNoFim * 1.2, $"{semNoFim:0.###} -> {DefesaV2(60_000, true):0.###}");
		}
		finally { ArmaduraDeTech.ProtegeEmQualquerPoderDeTeste = false; }

		// ---- as artes
		List<(string Nome, string Caminho)> artes = ArmaduraDeTech.Escolhiveis(cat);
		AfirmarV2($"a tela oferece as artes da pasta `Clothes/Armor`, com as subpastas ({artes.Count}), e NAO a do Beerus (que continua no catalogo: e a roupa do Bills)",
				  artes.Count >= 45 && artes.Exists(a => a.Nome == "Armor 8") && artes.Exists(a => a.Caminho.Contains("/Mandalorian/"))
				  && artes.Exists(a => a.Caminho.Contains("/Phoenix Armour/")) && artes.Exists(a => a.Caminho.Contains("/Shields/"))
				  && !artes.Exists(a => a.Nome.Contains("Beerus")) && !artes.Exists(a => a.Nome.StartsWith("unused"))
				  && cat.Armaduras.Exists(c => c.Contains("Beerus")), $"{artes.Count} artes");
		AfirmarV2("a 'GinsDynastyArmorRed' existe nas DUAS listas, e sao duas coisas: a do guarda-roupa nao e armadura, a da pasta Armor e",
				  cat.Peca("GinsDynastyArmorRed") is { } roupaGin && !cat.EhArmadura(roupaGin)
				  && cat.PecaDeArmadura("GinsDynastyArmorRed") is { } armaduraGin && cat.EhArmadura(armaduraGin) && roupaGin != armaduraGin);

		// ---- pela porta de producao: fabricada, moldada, vestida
		ServerPlayer pl = ForjarV2("Blindado", TerraFirmeG12(4), 100);
		Guardar(pl, ArmaduraDeTech.Id);
		AfirmarV2("a armadura recem-fabricada e um item CRU, e a unica acao dela e escolher a arte",
				  CatalogoDeItens.Get(ArmaduraDeTech.Id) is { } crua && crua.AcoesDoItem.SequenceEqual([ArmaduraDeTech.AcaoMoldar]));

		List<string> falas = Ouvir(() => ComandoDeItem(pl, $"item_{ArmaduraDeTech.AcaoMoldar}", $"{ArmaduraDeTech.Id}/Beerus Inspired Suit (DU)"));
		AfirmarV2("a do Beerus e recusada mesmo pedida pelo nome, e a armadura crua continua na mochila",
				  pl.Mochila.Quantos(ArmaduraDeTech.Id) == 1 && Disse(falas, "não existe"), Ultimos(falas));
		falas = Ouvir(() => ComandoDeItem(pl, $"item_{ArmaduraDeTech.AcaoMoldar}", $"{ArmaduraDeTech.Id}/Clothes_GiTop"));
		AfirmarV2("uma ROUPA comum nao e arte de armadura: recusada", pl.Mochila.Quantos(ArmaduraDeTech.Id) == 1 && Disse(falas, "não existe"), Ultimos(falas));

		const string peca = "Armadura|Armor 8|";
		falas = Ouvir(() => ComandoDeItem(pl, $"item_{ArmaduraDeTech.AcaoMoldar}", $"{ArmaduraDeTech.Id}/Armor 8"));
		AfirmarV2("**ESCOLHIDA A ARTE, O ITEM CRU VIRA A PECA** `Armadura|Armor 8|`, que se veste pela mochila",
				  pl.Mochila.Quantos(ArmaduraDeTech.Id) == 0 && pl.Mochila.Quantos(peca) == 1
				  && CatalogoDeItens.Get(peca) is { } pronta && pronta.AcoesDoItem.Contains(RoupaGuardada.AcaoVestir)
				  && pronta.Arte.EndsWith("/Clothes/Armor/Armor 8.tres") && Disse(falas, "molda a armadura"), Ultimos(falas));

		TickFichas();
		double antes = pl.Ficha.Ephysdef;
		falas = Ouvir(() => ComandoDeItem(pl, $"item_{RoupaGuardada.AcaoVestir}", peca));
		TickFichas();
		AfirmarV2("**VESTIDA, ELA PROTEGE**: a peca vai pro corpo, e no tique de ficha a defesa fisica sobe",
				  pl.Visual.Roupa.Exists(p => p.Caminho.EndsWith("/Clothes/Armor/Armor 8.tres")) && pl.Mochila.Quantos(peca) == 0
				  && pl.Ficha.armadura && pl.Ficha.Ephysdef > antes * 1.5 && Disse(falas, "protege"),
				  $"defesa {antes:0.###} -> {pl.Ficha.Ephysdef:0.###} | {Ultimos(falas)}");

		Guardar(pl, ArmaduraDeTech.Id);
		ComandoDeItem(pl, $"item_{ArmaduraDeTech.AcaoMoldar}", $"{ArmaduraDeTech.Id}/Nappa Armor");
		falas = Ouvir(() => ComandoDeItem(pl, $"item_{RoupaGuardada.AcaoVestir}", "Armadura|Nappa Armor|"));
		AfirmarV2("**UMA ARMADURA POR CORPO**: a segunda e recusada e continua na mochila",
				  pl.Mochila.Quantos("Armadura|Nappa Armor|") == 1 && pl.Visual.Roupa.Count == 1 && Disse(falas, "já veste uma armadura"), Ultimos(falas));

		ComandoDeItem(pl, $"item_{RoupaGuardada.AcaoTirar}", peca);
		TickFichas();
		AfirmarV2("TIRADA, a protecao sai com ela (no tique seguinte) e a peca volta pra mochila",
				  !pl.Ficha.armadura && Math.Abs(pl.Ficha.Ephysdef - antes) < 1e-9 && pl.Mochila.Quantos(peca) == 1 && pl.Visual.Roupa.Count == 0,
				  $"defesa {pl.Ficha.Ephysdef:0.###}");

		// ---- no NPC ela e desenho
		ServerPlayer npc = ForjarV2("Guarda", TerraFirmeG12(4), 100, "Saiyan");
		npc.Cerebro = new Cerebro();
		npc.Visual.Roupa.Add(new PecaDeRoupa(cat.PecaDeArmadura("Armor 8")!, null));
		TickFichas();
		AfirmarV2("no NPC a armadura continua sendo DESENHO, como no DM: o corpo com cerebro de IA nao ganha a defesa",
				  !npc.Ficha.armadura && VesteArmadura(pl) == false && !VesteArmadura(npc));

		// ---- ninguem nasce de armadura
		var naCriacao = new Aparencia();
		naCriacao.Roupa.Add(new PecaDeRoupa(cat.Peca("Clothes_GiTop")!, null));
		naCriacao.Roupa.Add(new PecaDeRoupa(cat.PecaDeArmadura("Armor 8")!, null));
		cat.Sanear(naCriacao, "Human", "Male");
		bool oSanearDeixa = naCriacao.Roupa.Count == 2;   // e por ele que a roupa do NPC passa
		int tiradas = TirarArmaduraDaCriacao(naCriacao);
		AfirmarV2("**NINGUEM NASCE DE ARMADURA**: a ficha de criacao que chega com uma (cliente mexido) perde so ela",
				  oSanearDeixa && tiradas == 1 && naCriacao.Roupa.Count == 1 && !cat.EhArmadura(naCriacao.Roupa[0].Caminho));

		TirarDoMundoV2(pl);
		TirarDoMundoV2(npc);
	}

	// =====================================================================
	// 15) A ROUPA DE QUEM ESTA NOCAUTEADO
	// =====================================================================
	private void OSaqueDaRoupaV2()
	{
		GD.Print("[v020] -- 15) A ROUPA DE QUEM ESTA NOCAUTEADO: sai do corpo dele pra mochila de quem leva, armadura inclusive (itens 8 e 14)");
		if (_visual is not { } cat) { AfirmarV2("(montagem) o servidor tem o catalogo de aparencia", false); return; }

		(int Ladrao, int Caido, List<Pilha> Leva)? lista = null;
		EscutaDeSaque = (ladrao, caido, leva) => lista = (ladrao, caido, [.. leva]);
		try
		{
			const float tile = ZoneCollision.TileSize;
			Vec2 praca = TerraFirmeG12(6);
			ServerPlayer ladrao = ForjarV2("Ladrao", praca, 1_000);
			ServerPlayer vitima = ForjarV2("Vitima", praca + new Vec2(tile, 0), 1_000, "Saiyan");
			vitima.Cerebro = new Cerebro();   // o Saiyajin de armadura do mundo: um NPC
			string gi = cat.Peca("Clothes_GiTop")!, armadura = cat.PecaDeArmadura("Armor 8")!;
			var tinta = new Rgb(0x2A, 0x2A, 0x33);
			vitima.Visual.Roupa.Add(new PecaDeRoupa(gi, tinta));
			vitima.Visual.Roupa.Add(new PecaDeRoupa(armadura, null));
			const string idDoGi = "Roupa|Clothes_GiTop|2A2A33", idDaArmadura = "Armadura|Armor 8|";

			List<string> falas = Ouvir(() => Verbo(ladrao, Saque.Verbo, Saque.NoCorpo + idDoGi));
			AfirmarV2("DE PE ninguem e despido: o verbo recusa e a roupa fica",
					  vitima.Visual.Roupa.Count == 2 && ladrao.Mochila.Quantos(idDoGi) == 0 && Disse(falas, "nocauteado"), Ultimos(falas));

			vitima.Combate.Nocautear(60);
			lista = null;
			Verbo(ladrao, Saque.Verbo, "");
			AfirmarV2("**NOCAUTEADO, A ROUPA DO CORPO ENTRA NA LISTA**: as duas pecas, marcadas como do corpo -- a camisa tingida e a ARMADURA do NPC",
					  lista is { } l1 && l1.Caido == vitima.Id && l1.Leva.Count == 2 && l1.Leva.Exists(p => p.Id == Saque.NoCorpo + idDoGi)
					  && l1.Leva.Exists(p => p.Id == Saque.NoCorpo + idDaArmadura),
					  lista is { } l1b ? string.Join(", ", l1b.Leva.Select(p => p.Id)) : "sem lista");

			Verbo(ladrao, Saque.Verbo, Saque.NoCorpo + idDoGi);
			AfirmarV2("**A PECA SAI DO CORPO DELE E VAI PRA MOCHILA DE QUEM LEVA**, com a cor que tinha",
					  ladrao.Mochila.Quantos(idDoGi) == 1 && !vitima.Visual.Roupa.Exists(p => p.Caminho == gi) && vitima.Visual.Roupa.Count == 1,
					  $"ladrao tem {ladrao.Mochila.Quantos(idDoGi)}, a vitima veste {vitima.Visual.Roupa.Count}");
			AfirmarV2("...e a lista nova vai junto, so com a armadura",
					  lista is { } l2 && l2.Leva.Count == 1 && l2.Leva[0].Id == Saque.NoCorpo + idDaArmadura);

			falas = Ouvir(() => Verbo(ladrao, Saque.Verbo, Saque.NoCorpo + idDaArmadura));
			AfirmarV2("O RESPIRO vale pra roupa: o clique seguinte, dentro de um segundo, nao despe (e avisa)",
					  vitima.Visual.Roupa.Count == 1 && Disse(falas, "um de cada vez"), Ultimos(falas));
			ladrao.ProximoSaqueMs = 0;

			Verbo(ladrao, Saque.Verbo, Saque.NoCorpo + idDaArmadura);
			AfirmarV2("**A ARMADURA DO NPC TAMBEM**: sai do corpo dele como a peca de armadura",
					  ladrao.Mochila.Quantos(idDaArmadura) == 1 && vitima.Visual.Roupa.Count == 0);
			TickFichas();
			double antes = ladrao.Ficha.Ephysdef;
			ComandoDeItem(ladrao, $"item_{RoupaGuardada.AcaoVestir}", idDaArmadura);
			TickFichas();
			AfirmarV2("...e PROTEGE o ladrao que a veste (no NPC ela era so desenho)",
					  ladrao.Ficha.armadura && ladrao.Ficha.Ephysdef > antes * 1.2 && !vitima.Ficha.armadura,
					  $"defesa {antes:0.###} -> {ladrao.Ficha.Ephysdef:0.###}");

			// ---- a mesma peca na mochila E no corpo: duas linhas, e cada uma sai de onde esta
			TirarDoMundoV2(vitima);
			ServerPlayer outro = ForjarV2("Outro", praca + new Vec2(tile, 0), 1_000);
			outro.Visual.Roupa.Add(new PecaDeRoupa(gi, null));
			outro.Mochila.Guardar("Roupa|Clothes_GiTop|");
			outro.Combate.Nocautear(60);
			ladrao.ProximoSaqueMs = 0;
			lista = null;
			Verbo(ladrao, Saque.Verbo, "");
			AfirmarV2("a MESMA peca na mochila e no corpo do caido sao DUAS linhas: a da mochila e a marcada",
					  lista is { } l3 && l3.Leva.Count == 2 && l3.Leva.Exists(p => p.Id == "Roupa|Clothes_GiTop|")
					  && l3.Leva.Exists(p => p.Id == Saque.NoCorpo + "Roupa|Clothes_GiTop|"),
					  lista is { } l3b ? string.Join(", ", l3b.Leva.Select(p => p.Id)) : "sem lista");
			Verbo(ladrao, Saque.Verbo, Saque.NoCorpo + "Roupa|Clothes_GiTop|");
			AfirmarV2("...e pedir a do corpo DESPE: a da mochila dele continua la",
					  outro.Visual.Roupa.Count == 0 && outro.Mochila.Quantos("Roupa|Clothes_GiTop|") == 1 && ladrao.Mochila.Quantos("Roupa|Clothes_GiTop|") == 1);

			// ---- a do Beerus fica
			outro.Visual.Roupa.Add(new PecaDeRoupa(cat.PecaDeArmadura("Beerus Inspired Suit (DU)")!, null));
			ladrao.ProximoSaqueMs = 0;
			lista = null;
			Verbo(ladrao, Saque.Verbo, "");
			AfirmarV2("a roupa do Beerus FICA no corpo: nao entra na lista",
					  lista is { } l4 && !l4.Leva.Exists(p => p.Id.Contains("Beerus")) && outro.Visual.Roupa.Count == 1);

			// ---- mochila cheia: nada some no ar
			outro.Visual.Roupa.Clear();
			outro.Visual.Roupa.Add(new PecaDeRoupa(gi, tinta));
			for (int i = 0; i < Inventario.Slots && !ladrao.Mochila.Cheio; i++) ladrao.Mochila.Guardar(CatalogoDeItens.Scouter);
			ladrao.ProximoSaqueMs = 0;
			falas = Ouvir(() => Verbo(ladrao, Saque.Verbo, Saque.NoCorpo + idDoGi));
			AfirmarV2("MOCHILA CHEIA: a peca continua no corpo do caido (nada some no ar)",
					  ladrao.Mochila.Cheio && outro.Visual.Roupa.Count == 1 && Disse(falas, "cheia"), Ultimos(falas));

			// ---- o saque de antes
			Saque.RoupaDoCorpoFicaDeTeste = true;
			try
			{
				ladrao.ProximoSaqueMs = 0;
				lista = null;
				Verbo(ladrao, Saque.Verbo, "");
				AfirmarV2("(defeito injetado: a roupa do corpo fica) a lista volta a nao ter peca vestida nenhuma -- o saque de antes, que a regua reprova",
						  lista is { } l5 && !l5.Leva.Exists(p => p.Id.StartsWith(Saque.NoCorpo)),
						  lista is { } l5b ? string.Join(", ", l5b.Leva.Select(p => p.Id)) : "sem lista");
			}
			finally { Saque.RoupaDoCorpoFicaDeTeste = false; }

			TirarDoMundoV2(outro);
			TirarDoMundoV2(ladrao);
		}
		finally { EscutaDeSaque = null; }
	}

	// =====================================================================
	// 16) A VARIEDADE DOS NPCs
	// =====================================================================
	/// <summary>As tecnicas que a IA sabe USAR: os quatro tiros da tabela de longe e a piscada.</summary>
	private static readonly string[] TecnicasDaIaV2 = ["Ki_Wave", "Basic_Blast", "Guided_Ball", "Scattering_Bullet"];

	/// <summary>O kit de luta de um corpo, escrito: os tiros que ele sabe e se sabe a Afterimage.</summary>
	private string KitV2(ServerPlayer npc) =>
		string.Join("+", TecnicasDaIaV2.Where(t => SabeTecnica(npc, t)).Append(npc.Livro?.Sabe(PathDoZanzoken) == true ? "Afterimage" : "")
							   .Where(t => t.Length > 0));

	/// <summary>Nasce `quantos` corpos do molde pela porta de producao (`NascerNpc`), um por lugar, e os devolve.</summary>
	private List<ServerPlayer> NascerVariosV2(string molde, int quantos, ulong primeiroLugar)
	{
		var corpos = new List<ServerPlayer>();
		Vec2 onde = TerraFirmeG12(4);
		for (int i = 0; i < quantos; i++)
			if (NascerNpc(molde, ZonaDaBancadaDeProjetil, onde, primeiroLugar + (ulong)i) is { } npc) corpos.Add(npc);
		return corpos;
	}

	private void AVariedadeDosNpcsV2()
	{
		GD.Print("[v020] -- 16) A VARIEDADE DOS NPCs: talentos por corpo, cor de ki propria e a IA que pisca (item 15)");
		if (_moldes?.Get(VilaoAvulso.Molde) is not { } vilao || _moldes.Get("cidadao") is not { } cidadao || _skills == null)
		{ AfirmarV2("(montagem) o servidor tem os moldes do vilao e do cidadao, e o catalogo de skills", false); return; }

		// ---- o dado e a regra pura
		AfirmarV2("o vilao avulso e o habitante do mundo tem pool de talentos no `npcs.json`, e todo talento existe no catalogo de skills",
				  vilao.Talentos.Length >= 3 && vilao.QuantosTalentos > 1 && cidadao.Talentos.Length >= 4 && cidadao.QuantosTalentos > 0
				  && vilao.Talentos.Concat(cidadao.Talentos).All(t => _skills.Get(SorteioDeNpc.LerTalento(t).Path) != null),
				  string.Join(", ", vilao.Talentos.Concat(cidadao.Talentos).Where(t => _skills.Get(SorteioDeNpc.LerTalento(t).Path) == null)));
		AfirmarV2("`/datum/skill/x@30` e a skill x no degrau 30; sem arroba, sem degrau proprio",
				  SorteioDeNpc.LerTalento("/datum/skill/mind/Basic_Ki_Control@30") == ("/datum/skill/mind/Basic_Ki_Control", 30)
				  && SorteioDeNpc.LerTalento("/datum/skill/ki/Afterimage") == ("/datum/skill/ki/Afterimage", 0));

		var contagem = new Dictionary<string, int>();
		int menor = int.MaxValue, maior = 0;
		bool repetiu = false, estavel = true;
		for (ulong s = 1; s <= 400; s++)
		{
			List<(string Path, int Nivel)> t = SorteioDeNpc.SortearTalentos(vilao, s, _skills);
			menor = Math.Min(menor, t.Count);
			maior = Math.Max(maior, t.Count);
			repetiu |= t.Select(x => x.Path).Distinct().Count() != t.Count;
			estavel &= t.SequenceEqual(SorteioDeNpc.SortearTalentos(vilao, s, _skills));
			foreach ((string p, _) in t) contagem[p] = contagem.GetValueOrDefault(p) + 1;
		}
		AfirmarV2("o sorteio do vilao da de UM a DOIS talentos por corpo (1,5 em media), sem repetir, e o mesmo corpo sorteia sempre os mesmos",
				  menor == 1 && maior == 2 && !repetiu && estavel, $"de {menor} a {maior}");
		AfirmarV2("...e TODO talento da pool aparece, cada um em boa parte dos corpos (nenhum e enfeite da lista)",
				  contagem.Count == vilao.Talentos.Length && contagem.Values.All(n => n > 120 && n < 280),
				  string.Join(", ", contagem.Select(kv => $"{kv.Key[(kv.Key.LastIndexOf('/') + 1)..]} {kv.Value}/400")));
		AfirmarV2("CONTRA-EXEMPLO: molde sem pool (o saibaman) nao sorteia nada",
				  _moldes.Get("saibaman") is { } saibaman && SorteioDeNpc.SortearTalentos(saibaman, 7, _skills).Count == 0);

		// ---- pela porta de producao: quarenta viloes e sessenta habitantes
		List<ServerPlayer> viloes = NascerVariosV2(VilaoAvulso.Molde, 40, 91_000);
		List<ServerPlayer> gente = NascerVariosV2("cidadao", 60, 92_000);
		try
		{
			List<string> kits = [.. viloes.Select(KitV2)];
			int comAfterimage = viloes.Count(v => v.Livro?.Sabe(PathDoZanzoken) == true);
			AfirmarV2($"**OS VILOES NAO LUTAM TODOS IGUAL**: em 40 nascidos ha pelo menos quatro kits diferentes ({kits.Distinct().Count()})",
					  viloes.Count == 40 && kits.Distinct().Count() >= 4,
					  string.Join(" | ", kits.GroupBy(k => k).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()}x {g.Key}")));
			AfirmarV2($"**HA VILAO COM AFTERIMAGE** -- por volta da metade deles ({comAfterimage} de 40)", comAfterimage >= 10 && comAfterimage <= 30);
			AfirmarV2("...ha vilao com a Bala Dispersa e vilao com a Bola Teleguiada (o talento com degrau abre o verb)",
					  viloes.Exists(v => SabeTecnica(v, "Scattering_Bullet")) && viloes.Exists(v => SabeTecnica(v, "Guided_Ball")));

			List<string> kitsDaGente = [.. gente.Select(KitV2)];
			AfirmarV2($"**OS HABITANTES DO MUNDO TAMBEM VARIAM**: em 60 ha quem atire raio, quem atire bola, quem pisque -- e quem so soque ({kitsDaGente.Distinct().Count()} kits)",
					  gente.Count == 60 && kitsDaGente.Distinct().Count() >= 5 && kitsDaGente.Contains("")
					  && gente.Exists(g => g.Livro?.Sabe(PathDoZanzoken) == true) && gente.Exists(g => SabeTecnica(g, "Ki_Wave")),
					  string.Join(" | ", kitsDaGente.GroupBy(k => k).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()}x {(g.Key.Length == 0 ? "(so soco)" : g.Key)}")));

			// ---- a cor do ki
			List<Rgb?> cores = [.. viloes.Concat(gente).Select(n => n.Visual.CorKi)];
			AfirmarV2($"**CADA NPC TEM A SUA COR DE KI**: os 100 nasceram com cor, e quase todas diferentes ({cores.Distinct().Count()})",
					  cores.TrueForAll(c => c != null) && cores.Distinct().Count() >= 90);
			AfirmarV2("...e ela nao anda junto com a cor da aura (sao dois sorteios)",
					  viloes.Count(v => Equals(v.Visual.CorKi, v.Visual.CorAura)) <= 1);
		}
		finally
		{
			foreach (ServerPlayer n in viloes.Concat(gente)) RemoverNpc(n);
		}

		SorteioDeNpc.TalentosDesligadosDeTeste = true;
		NpcSemCorDeKiDeTeste = true;
		List<ServerPlayer> deAntes = NascerVariosV2(VilaoAvulso.Molde, 40, 91_000);
		try
		{
			AfirmarV2("(defeito injetado: sem talentos) os MESMOS 40 viloes nascem sem Afterimage nenhuma e com um kit so -- o povoamento de antes",
					  deAntes.Count == 40 && !deAntes.Exists(v => v.Livro?.Sabe(PathDoZanzoken) == true) && deAntes.Select(KitV2).Distinct().Count() <= 2,
					  string.Join(" | ", deAntes.Select(KitV2).Distinct()));
			AfirmarV2("(defeito injetado: sem cor de ki) todo NPC atira da mesma cor: nenhum nasce com cor propria",
					  deAntes.TrueForAll(v => v.Visual.CorKi == null));
		}
		finally
		{
			SorteioDeNpc.TalentosDesligadosDeTeste = false;
			NpcSemCorDeKiDeTeste = false;
			foreach (ServerPlayer n in deAntes) RemoverNpc(n);
		}

		// ---- a IA pisca
		const float tile = ZoneCollision.TileSize;
		Vec2 corredor = TerraFirmeG12(14);
		ServerPlayer cacador = ForjarV2("Cacador", corredor, 50_000);
		ServerPlayer presa = ForjarV2("Presa", corredor + new Vec2(6 * tile, 0), 50_000);
		cacador.Cerebro = new Cerebro();
		cacador.Livro!.Dar(PathDoZanzoken);
		cacador.Ficha.technique = cacador.Ficha.kiskill = cacador.Ficha.speed = 4;   // pericia de quem ja treinou: o alcance da piscada cresce com ela
		cacador.Ficha.Tick(agoraMs: NowMs());
		cacador.Ficha.Ki = cacador.Ficha.MaxKi;
		cacador.Facing = Facing.East;
		presa.Facing = Facing.East;   // de costas pro cacador: as costas dela sao o tile do lado de ca
		int alcance = ZanzorangeG10(cacador.Ficha);
		AfirmarV2($"(montagem) o cacador sabe a Afterimage e alcanca a presa a seis tiles (alcance da piscada: {alcance})", alcance >= 6);

		IaNaoPiscaDeTeste = true;
		PiscarPelaIa(cacador, presa, Plano.Pressionar);
		AfirmarV2("(defeito injetado: a IA nao pisca) o corpo que sabe a tecnica fica onde esta -- o NPC de antes, que a carregava sem usar",
				  Vec2.Distance(cacador.Pos, corredor) < 1 && PiscadasDaIaDeTeste == 0);
		IaNaoPiscaDeTeste = false;

		PiscarPelaIa(cacador, presa, Plano.Fugir);
		AfirmarV2("CONTRA-EXEMPLO: fugindo ele nao pisca PRA CIMA da presa", Vec2.Distance(cacador.Pos, corredor) < 1);

		double kiAntes = cacador.Ficha.Ki;
		int anuncios = _saltosAnunciados;
		PiscarPelaIa(cacador, presa, Plano.Pressionar);
		AfirmarV2("**A IA PISCA PRAS COSTAS DA PRESA**: fechando a distancia, o corpo some e reaparece a um tile, atras dela, encarando-a",
				  Vec2.Distance(cacador.Pos, presa.Pos - new Vec2(tile, 0)) < 2 && cacador.Facing == Facing.East && PiscadasDaIaDeTeste == 1,
				  $"ficou a {Vec2.Distance(cacador.Pos, presa.Pos) / tile:0.#} tile(s) da presa, olhando {cacador.Facing}");
		AfirmarV2("...pela porta do jogador: cobra o Ki por tile saltado e deixa a miragem pra tras (o anuncio com o vulto)",
				  cacador.Ficha.Ki < kiAntes && _saltosAnunciados == anuncios + 1 && _ultimoSalto is { Vulto: true } salto && salto.Quem == cacador.Id,
				  $"ki {kiAntes:0.#} -> {cacador.Ficha.Ki:0.#}");

		cacador.Pos = corredor;
		PiscarPelaIa(cacador, presa, Plano.Pressionar);
		AfirmarV2("A RECARGA: logo depois de piscar ele nao pisca de novo (a maquina nao vira um alvo que ninguem acerta)",
				  Vec2.Distance(cacador.Pos, corredor) < 1 && PiscadasDaIaDeTeste == 1);

		_proximaPiscadaDaIa.Remove(cacador.Id);
		cacador.Pos = presa.Pos - new Vec2(1.5f * tile, 0);
		Vec2 colado = cacador.Pos;
		PiscarPelaIa(cacador, presa, Plano.Pressionar);
		AfirmarV2("CONTRA-EXEMPLO: colado na presa ele soca, e nao pisca", Vec2.Distance(cacador.Pos, colado) < 1 && PiscadasDaIaDeTeste == 1);

		ServerPlayer leigo = ForjarV2("Leigo", corredor, 50_000);
		leigo.Cerebro = new Cerebro();
		PiscarPelaIa(leigo, presa, Plano.Pressionar);
		AfirmarV2("CONTRA-EXEMPLO: quem nao sabe a Afterimage nao pisca", Vec2.Distance(leigo.Pos, corredor) < 1 && PiscadasDaIaDeTeste == 1);

		_proximaPiscadaDaIa.Remove(cacador.Id);
		cacador.Pos = corredor;
		cacador.Perfil = PerfilDeCombate.SoCorpo;
		PiscarPelaIa(cacador, presa, Plano.Pressionar);
		AfirmarV2("CONTRA-EXEMPLO: o corpo de perfil 'so corpo' sabe a Afterimage e NAO a usa -- o perfil corta a piscada como corta o tiro",
				  Vec2.Distance(cacador.Pos, corredor) < 1 && PiscadasDaIaDeTeste == 1);
		cacador.Perfil = PerfilDeCombate.Completo;

		_proximaPiscadaDaIa.Remove(cacador.Id);
		_proximaPiscadaDaIa.Remove(leigo.Id);
		TirarDoMundoV2(cacador);
		TirarDoMundoV2(presa);
		TirarDoMundoV2(leigo);
	}

	/// <summary>
	/// Poe ao lado do jogador um corpo de bancada NOCAUTEADO e VESTIDO (uma camisa tingida e uma armadura), e devolve o
	/// id dele. So pra bancada de tela (`--diagv020`): o menu de revistar tem de mostrar a roupa do corpo.
	/// </summary>
	internal int CaidoVestidoDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _visual is not { } cat) return 0;
		var novo = new ServerPlayer
		{
			Id = IdBaseDeProjetil + 700 + _pjCorpos++,
			Peer = null,
			Name = "Caido de bancada",
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = pl.Zone,
			Pos = pl.Pos + MeleeArea.Frente(pl.Facing) * (ZoneCollision.TileSize * 0.9f),
			Conta = "bancada_v020",
			Slot = 0,
			Ficha = new Fighter { Race = "Human", BP = 100 },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		novo.Visual.Roupa.Add(new PecaDeRoupa(cat.Peca("Clothes_GiTop")!, new Rgb(0x2A, 0x2A, 0x33)));
		novo.Visual.Roupa.Add(new PecaDeRoupa(cat.PecaDeArmadura("Armor 8")!, null));
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.Tick(agoraMs: NowMs());
		novo.Combate.Nocautear(600);
		return novo.Id;
	}

	/// <summary>O que este corpo veste agora, como ids de peca guardada. So pra bancada de tela (`--diagv020`).</summary>
	internal List<string> RoupaDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? [.. pl.Visual.Roupa.Select(p => RoupaGuardada.De(p).Id)] : [];

	/// <summary>A defesa fisica efetiva e a marca de armadura deste corpo. So pra bancada de tela (`--diagv020`).</summary>
	internal (double Defesa, bool Armadura) DefesaDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? (pl.Ficha.Ephysdef, pl.Ficha.armadura) : default;

	/// <summary>Tira do mundo um corpo de bancada posto por uma porta de teste. So pra bancada de tela (`--diagv020`).</summary>
	internal void TirarOCorpoDeTeste(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? corpo) && corpo.Peer == null) TirarDoMundoV2(corpo);
	}

	/// <summary>Atordoa um corpo de jogador por alguns segundos. So pra bancada de tela (`--diagv020`): o HUD tem de dizer.</summary>
	internal bool AtordoarDeTeste(int id, double segundos)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate == null) return false;
		pl.Combate.Stun = segundos;
		return true;
	}

	/// <summary>
	/// Poe um corpo de bancada ao lado do jogador e o faz agarra-lo, pela porta de producao (`AlternarAgarrao`).
	/// <paramref name="vezesOPoder"/> e o poder de quem agarra, em vezes o do jogador. Devolve o id de quem agarrou.
	/// So pra bancada de tela (`--diagv020`).
	/// </summary>
	internal int AgarrarOJogadorDeTeste(int id, double vezesOPoder, bool porTras)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		// PELA FRENTE o corpo de bancada fica do lado pra onde o jogador olha, encarando-o; POR TRAS, nas costas
		// dele, olhando pro mesmo lado (e ai, se o jogador tem rabo, a pegada e pelo rabo).
		Vec2 frente = MeleeArea.Frente(pl.Facing);
		Vec2 onde = pl.Pos + frente * (ZoneCollision.TileSize * 0.9f) * (porTras ? -1 : 1);
		Facing olhar = porTras ? pl.Facing : pl.Facing switch
		{
			Facing.North => Facing.South,
			Facing.South => Facing.North,
			Facing.East => Facing.West,
			_ => Facing.East,
		};
		var novo = new ServerPlayer
		{
			Id = IdBaseDeProjetil + 500 + _pjCorpos++,
			Peer = null,
			Name = "Agarrador de bancada",
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = pl.Zone,
			Pos = onde,
			Conta = "bancada_v020",
			Slot = 0,
			Ficha = new Fighter { Race = "Human", BP = Math.Max(pl.Ficha.expressedBP * vezesOPoder, 10) },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.Tick(agoraMs: NowMs());
		novo.Facing = olhar;
		AlternarAgarrao(novo);
		return novo.AgarrandoId == pl.Id ? novo.Id : 0;
	}

	/// <summary>Quem segura este jogador, e o contador da luta dele. So pra bancada de tela (`--diagv020`).</summary>
	internal (int PorQuem, double Contador, bool Debatendo)? AgarraoDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? (pl.AgarradoPorId, pl.ContadorDaLuta, pl.DebatendoSe) : null;

	/// <summary>Tira do mundo o corpo de bancada que agarrou o jogador. So pra bancada de tela (`--diagv020`).</summary>
	internal void TirarOAgarradorDeTeste(int idDoAgarrador)
	{
		if (!_players.TryGetValue(idDoAgarrador, out ServerPlayer? a)) return;
		if (a.AgarrandoId != 0) Soltar(a, MotivoDaSoltura.Tecla);
		TirarDoMundoV2(a);
	}
}
