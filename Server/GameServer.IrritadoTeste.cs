using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Npc;
using Jandirus.Core.Social;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// <c>--irritadoteste</c> -- o degrau IRRITADO (dono, 2026-10-21). Roda no <c>Entrar</c>, no host.
/// Prova o gatilho do corpo, a exclusao do que nao e luta, a porta x2,5, o bonus do "Annoyed" sem salto de Ki,
/// os 120 s, a meditacao e o retrato do HUD. Cada regra tem o defeito injetado que a faz REPROVAR.
/// </summary>
public sealed partial class GameServer
{
	private void RodarBancadaDoIrritadoSePedida(ServerPlayer pl)
	{
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--irritadoteste") < 0) return;
		RodarBancadaDoIrritado(pl);
	}

	private void RodarBancadaDoIrritado(ServerPlayer pl)
	{
		int ok = 0, falha = 0;
		var reprovadas = new List<string>();
		void Checa(string oque, bool passou, string detalhe = "")
		{
			GD.Print("[irritado] " + (passou ? "  OK    " : "  FALHA ") + oque
					 + (detalhe.Length > 0 ? "   " + detalhe : ""));
			if (passou) ok++;
			else { falha++; reprovadas.Add(oque); }
		}

		CombatState c = pl.Combate!;
		var vidas = c.Corpo.Partes.Select(p => (p.Vida, p.Decepado)).ToList();
		long ext = pl.FuriaExtremaAte, com = pl.RaivaComumAte, irr = pl.IrritadoAte;
		double hp = pl.Ficha.HP, anger = pl.Ficha.Anger, bp = pl.Ficha.BP, ki = pl.Ficha.Ki;
		string forma = pl.Forma.Atual;
		ZoneKey zona = pl.Zone;
		var inimigo = new ServerPlayer
		{
			Id = pl.Id + 50_000, Name = "Inimigo", Conta = "inimigo_irritado", Slot = 0, Peer = null,
		};

		void RestaurarCorpo()
		{
			for (int i = 0; i < c.Corpo.Partes.Count; i++)
			{
				c.Corpo.Partes[i].Vida = vidas[i].Vida;
				c.Corpo.Partes[i].Decepado = vidas[i].Decepado;
			}
			c.SincronizarVida();
		}

		void Calmo()
		{
			RestaurarCorpo();
			pl.FuriaExtremaAte = pl.RaivaComumAte = pl.IrritadoAte = 0;
			pl.Ficha.Anger = 100;
			pl.Zone = zona;
			inimigo.Papel = null;
			inimigo.Id = pl.Id + 50_000;
		}

		try
		{
			GD.Print("[irritado] ================ o degrau IRRITADO ================");

			// ---- o gatilho puro, num corpo de mesa (nao o do host) ----
			Body mesa = Body.Novo();
			Checa("corpo inteiro, vida cheia, sem perda: NAO irrita", !GatilhoDoIrritado.Acende(mesa, 100, false));
			Checa("vida 50 NAO e abaixo de 50", !GatilhoDoIrritado.Acende(mesa, 50, false));
			Checa("vida abaixo de 50 acende", GatilhoDoIrritado.Acende(mesa, 49.9, false));
			mesa.Achar("Cabeca")!.Vida = 40;
			Checa("cabeca em 40% NAO e abaixo de 40", !GatilhoDoIrritado.Acende(mesa, 100, false));
			mesa.Achar("Cabeca")!.Vida = 39;
			Checa("cabeca abaixo de 40% acende, mesmo com a vida media alta", GatilhoDoIrritado.Acende(mesa, 100, false));
			mesa.Achar("Cabeca")!.Vida = 100;
			mesa.Achar(Body.NomeDoTronco)!.Vida = 39;
			Checa("tronco abaixo de 40% acende sozinho", GatilhoDoIrritado.Acende(mesa, 100, false));
			mesa.Achar(Body.NomeDoTronco)!.Vida = 100;
			mesa.Achar("Braco esquerdo")!.Vida = 19;
			Checa("UM membro quebrado (abaixo dos 20% do Quebrado) nao basta", !GatilhoDoIrritado.Acende(mesa, 100, false));
			mesa.Achar("Perna direita")!.Vida = 19;
			Checa("DOIS membros gravemente feridos acendem", GatilhoDoIrritado.Acende(mesa, 100, false));
			mesa.Achar("Braco esquerdo")!.Vida = 100;
			mesa.Achar("Perna direita")!.Vida = 100;
			Checa("perder um membro acende NA HORA, com a vida cheia", GatilhoDoIrritado.Acende(mesa, 100, perdeuMembroAgora: true));
			mesa.Achar("Braco esquerdo")!.Decepado = true;
			mesa.Achar("Braco esquerdo")!.Vida = 0;
			Checa("membro que JA estava decepado nao reacende sozinho", !GatilhoDoIrritado.Acende(mesa, 100, false));

			GatilhoDoIrritado.NuncaAcendeDeTeste = true;
			try
			{
				Checa("(defeito injetado: apanhar nunca irrita) a mesma regua REPROVA",
					  !GatilhoDoIrritado.Acende(mesa, 10, true));
			}
			finally { GatilhoDoIrritado.NuncaAcendeDeTeste = false; }

			// ---- a porta x2,5, no funil de verdade ----
			FormaDef ssj1 = Catalogo.Def("ssj1")!;
			FormaDef beast = Catalogo.Def("beast")!;
			var est = new EstadoDeForma();
			double porta = est.PortaDeBp(ssj1);
			var irritado = PerfilDeFormas.Comum with { Raiva = NivelDeRaiva.Irritado };
			var comum = PerfilDeFormas.Comum with { Raiva = NivelDeRaiva.Comum };
			var furia = PerfilDeFormas.Comum with { Raiva = NivelDeRaiva.Extrema };
			RecusaForma Av(string id, double bpBase, PerfilDeFormas p) => est.Avaliar(id, bpBase, 1, false, p);

			Checa("a porta do SSJ1 existe", porta > 0, $"{porta:0}");
			Checa("IRRITADO na porta (x1, o preco da furia) NAO entra",
				  Av("ssj1", porta, irritado) == RecusaForma.SemPoderPraIrritado);
			Checa("IRRITADO um fio abaixo de x2,5 tambem nao",
				  Av("ssj1", porta * Catalogo.PortaDoIrritado * 0.999, irritado) == RecusaForma.SemPoderPraIrritado);
			Checa("IRRITADO entra em x2,5 (PortaDoIrritado)",
				  Av("ssj1", porta * Catalogo.PortaDoIrritado, irritado) == RecusaForma.Pode
				  && Math.Abs(Catalogo.PortaDoIrritado - 2.5) < 1e-12);
			Checa("a raiva COMUM continua no agio dela (1,2), nao no x2,5",
				  Av("ssj1", porta, comum) == RecusaForma.SemPoderPraRaivaComum
				  && Av("ssj1", porta * ssj1.AgioNaRaivaComum, comum) == RecusaForma.Pode);
			Checa("a FURIA entra pela porta, x1", Av("ssj1", porta, furia) == RecusaForma.Pode);
			Checa("a estreia automatica pela raiva comum tambem vale pro irritado, no mesmo preco",
				  SubidaPelaRaiva.Planejar(est, porta * Catalogo.PortaDoIrritado, irritado)?.Alvo.Id == "ssj1"
				  && SubidaPelaRaiva.Planejar(est, porta * Catalogo.PortaDoIrritado * 0.999, irritado)?.Alvo.Id != "ssj1");

			var prodigio = PerfilDeFormas.Comum with
			{
				Classe = Catalogo.ClasseProdigial, GodKi = 100, Raiva = NivelDeRaiva.Irritado,
			};
			RecusaForma beastIrritado = Av("beast", 1e15, prodigio);
			RecusaForma beastComum = Av("beast", 1e15, prodigio with { Raiva = NivelDeRaiva.Comum });
			// A raiva e a ULTIMA porta. Este perfil de prodigio nao subiu a escada do Beast, entao a recusa
			// chega como ForaDeOrdem antes do SemFuria. O que o dono pediu e que a forma nao abra.
			Checa("o BEAST nao abre no irritado nem na raiva comum",
				  beastIrritado != RecusaForma.Pode && beastComum != RecusaForma.Pode,
				  $"irritado {beastIrritado}, comum {beastComum}");

			EstadoDeForma.IrritadoSemPortaDeTeste = true;
			try
			{
				Checa("(defeito injetado: o irritado paga a porta x1) a regua do x2,5 REPROVA",
					  Av("ssj1", porta, irritado) == RecusaForma.Pode);
			}
			finally { EstadoDeForma.IrritadoSemPortaDeTeste = false; }

			EstadoDeForma.IrritadoNaoAbreFormaDeTeste = true;
			try
			{
				Checa("(defeito injetado: o irritado nao abre forma) com o preco pago a regua REPROVA",
					  Av("ssj1", porta * Catalogo.PortaDoIrritado, irritado) == RecusaForma.SemFuria);
			}
			finally { EstadoDeForma.IrritadoNaoAbreFormaDeTeste = false; }

			// ---- o bonus do Annoyed, sem salto de Ki ----
			double folga = 20;
			double topo = RaivaEmDegraus.Topo(NivelDeRaiva.Irritado, 120);
			Checa("o bonus e o Annoyed do DM (Anger = 100 + folga/4,25), ~1,05x, abaixo da raiva comum",
				  Math.Abs(topo - (100 + folga / 4.25)) < 1e-9
				  && topo / 100 < 1.06 && topo / 100 > 1.04
				  && topo < RaivaEmDegraus.Topo(NivelDeRaiva.Comum, 120),
				  $"{topo:0.###}");
			var ficha = new Fighter { MaxKi = 1000, Ki = 50, powerupcap = 1.4, MeditateGivesKiRegen = 1, canPower = 1 };
			Checa("o irritado NAO salta o Ki", !RaivaEmDegraus.SaltarOKi(NivelDeRaiva.Irritado, ficha) && ficha.Ki == 50);

			RaivaEmDegraus.IrritadoSemBonusDeTeste = true;
			try
			{
				Checa("(defeito injetado: o irritado nao da bonus) a regua do Annoyed REPROVA",
					  RaivaEmDegraus.Topo(NivelDeRaiva.Irritado, 120) == 100);
			}
			finally { RaivaEmDegraus.IrritadoSemBonusDeTeste = false; }

			// ---- no corpo do host, pela porta de producao ----
			Calmo();
			foreach (BodyPart p in c.Corpo.Partes) p.Vida = p.VidaMax * 0.49;
			c.SincronizarVida();
			List<string> falas = Ouvir(() => TalvezIrritar(pl, inimigo, perdeuMembro: false));
			Checa("vida abaixo de 50% por golpe de inimigo deixa IRRITADO e avisa no chat",
				  NivelDaRaivaDe(pl) == NivelDeRaiva.Irritado
				  && falas.Any(t => t.Contains("irritado"))
				  && pl.Ficha.Anger > 100,
				  $"{NivelDaRaivaDe(pl)}, Anger {pl.Ficha.Anger:0.##}, {string.Join(" | ", falas)}");
			Checa("o retrato do HUD e o bit IRRITADO, nao COM RAIVA nem FURIOSO",
				  (PoderEmUsoDe(pl).Bits & BitsDoPoderEmUso.Irritado) != 0
				  && (PoderEmUsoDe(pl).Bits & (BitsDoPoderEmUso.ComRaiva | BitsDoPoderEmUso.Furioso)) == 0);

			long prazo = pl.IrritadoAte;
			Checa("dura 120 s", Math.Abs(prazo - NowMs() - SegundosDeRaiva * 1000) < 2_000, $"{prazo - NowMs()}");
			pl.IrritadoAte = NowMs() + 5_000;
			TalvezIrritar(pl, inimigo, false);
			Checa("cada golpe que acende RENOVA os 120 s", pl.IrritadoAte > NowMs() + 60_000);

			IrritadoNaoRenovaDeTeste = true;
			try
			{
				long parado = pl.IrritadoAte = NowMs() + 5_000;
				TalvezIrritar(pl, inimigo, false);
				Checa("(defeito injetado: nao renova) a regua da renovacao REPROVA", pl.IrritadoAte == parado);
			}
			finally { IrritadoNaoRenovaDeTeste = false; }

			Calmo();
			pl.FuriaExtremaAte = NowMs() + 60_000;
			ProjetarRaiva(pl);
			foreach (BodyPart p in c.Corpo.Partes) p.Vida = p.VidaMax * 0.49;
			c.SincronizarVida();
			TalvezIrritar(pl, inimigo, false);
			Checa("quem ja esta FURIOSO nao desce pro irritado", NivelDaRaivaDe(pl) == NivelDeRaiva.Extrema);

			Calmo();
			List<string> mudo = Ouvir(() => { });
			IrritadoMudoDeTeste = true;
			try
			{
				foreach (BodyPart p in c.Corpo.Partes) p.Vida = p.VidaMax * 0.49;
				c.SincronizarVida();
				mudo = Ouvir(() => TalvezIrritar(pl, inimigo, false));
				Checa("(defeito injetado: sem aviso) a regua do chat REPROVA",
					  NivelDaRaivaDe(pl) == NivelDeRaiva.Irritado && !mudo.Any(t => t.Contains("irritado")),
					  string.Join(" | ", mudo));
			}
			finally { IrritadoMudoDeTeste = false; }

			// exclusoes
			Calmo();
			foreach (BodyPart p in c.Corpo.Partes) p.Vida = p.VidaMax * 0.49;
			c.SincronizarVida();
			TalvezIrritar(pl, pl, false);
			Checa("AUTODANO nao irrita", NivelDaRaivaDe(pl) == NivelDeRaiva.Nenhuma);

			pl.Zone = ZoneKey.Interior(DimensaoMental.Zona, 1);
			TalvezIrritar(pl, inimigo, false);
			Checa("na MENTE nao irrita", NivelDaRaivaDe(pl) == NivelDeRaiva.Nenhuma);
			pl.Zone = zona;

			inimigo.Papel = new PapelDeNpc(new MoldeDeNpc { Tipo = TipoDeNpc.Animal }, 1);
			TalvezIrritar(pl, inimigo, false);
			Checa("apanhar de BICHO nao irrita", NivelDaRaivaDe(pl) == NivelDeRaiva.Nenhuma);
			inimigo.Papel = new PapelDeNpc(new MoldeDeNpc(), 1);
			TalvezIrritar(pl, inimigo, false);
			Checa("spar de habitante pacifico nao irrita", NivelDaRaivaDe(pl) == NivelDeRaiva.Nenhuma);
			inimigo.Papel = null;

			Convivio.DeclararSemConvivioDeTeste = true;
			try
			{
				pl.Social.Fotografar(inimigo.Assinatura, inimigo.Name, "Human", "Male", 25, NowMs());
				pl.Social.Declarar(inimigo.Assinatura, Relacao.Bom);
				TalvezIrritar(pl, inimigo, false);
				Checa("treino/spar com amigo declarado nao irrita", NivelDaRaivaDe(pl) == NivelDeRaiva.Nenhuma);
			}
			finally
			{
				Convivio.DeclararSemConvivioDeTeste = false;
				pl.Social.Conhecidos.Remove(inimigo.Assinatura);
			}

			var npc = new ServerPlayer { Id = 1, Name = "Npc", Peer = null };
			npc.Combate = new CombatState(npc.Ficha);
			foreach (BodyPart p in npc.Combate.Corpo.Partes) p.Vida = p.VidaMax * 0.2;
			npc.Combate.SincronizarVida();
			TalvezIrritar(npc, inimigo, true);
			Checa("NPC nao fica irritado (a etapa dele e outra)", NivelDaRaivaDe(npc) == NivelDeRaiva.Nenhuma);

			IrritadoContaOQueNaoELutaDeTeste = true;
			try
			{
				pl.Zone = ZoneKey.Interior(DimensaoMental.Zona, 1);
				TalvezIrritar(pl, inimigo, false);
				Checa("(defeito injetado: conta o que nao e luta) a exclusao da mente REPROVA",
					  NivelDaRaivaDe(pl) == NivelDeRaiva.Irritado);
			}
			finally
			{
				IrritadoContaOQueNaoELutaDeTeste = false;
				pl.Zone = zona;
			}

			// meditar apaga
			Calmo();
			pl.IrritadoAte = NowMs() + 60_000;
			ProjetarRaiva(pl);
			Checa("com a janela aberta, meditar acalma",
				  NivelDaRaivaDe(pl) == NivelDeRaiva.Irritado && AcalmarPelaMeditacao(pl)
				  && NivelDaRaivaDe(pl) == NivelDeRaiva.Nenhuma && pl.IrritadoAte == 0 && pl.Ficha.Anger <= 100);

			// perder o membro no host, vida cheia
			Calmo();
			BodyPart braco = c.Corpo.Achar("Braco esquerdo")!;
			braco.Decepado = true;
			braco.Vida = 0;
			c.SincronizarVida();
			TalvezIrritar(pl, inimigo, perdeuMembro: true);
			Checa("perder um membro no host acende com a vida que sobrou alta",
				  NivelDaRaivaDe(pl) == NivelDeRaiva.Irritado && pl.Ficha.HP > 50, $"HP {pl.Ficha.HP:0.#}");
		}
		finally
		{
			GatilhoDoIrritado.NuncaAcendeDeTeste = false;
			EstadoDeForma.IrritadoSemPortaDeTeste = false;
			EstadoDeForma.IrritadoNaoAbreFormaDeTeste = false;
			RaivaEmDegraus.IrritadoSemBonusDeTeste = false;
			IrritadoContaOQueNaoELutaDeTeste = false;
			IrritadoNaoRenovaDeTeste = false;
			IrritadoMudoDeTeste = false;
			Convivio.DeclararSemConvivioDeTeste = false;
			pl.Social.Conhecidos.Remove(inimigo.Assinatura);
			RestaurarCorpo();
			pl.Ficha.HP = hp;
			pl.Ficha.Anger = anger;
			pl.Ficha.BP = bp;
			pl.Ficha.Ki = ki;
			pl.FuriaExtremaAte = ext;
			pl.RaivaComumAte = com;
			pl.IrritadoAte = irr;
			pl.Zone = zona;
			if (pl.Forma.Atual != forma) pl.Forma.Atual = forma;
			GD.Print($"[irritado] ================ {ok} OK, {falha} FALHA(S) ================");
			foreach (string r in reprovadas) GD.Print("[irritado]   FALHA  " + r);
		}
	}
}
