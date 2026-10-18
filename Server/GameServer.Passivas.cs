using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using Jandirus.Core.Social;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ AS PASSIVAS E AS ATIVAS RACIAIS (v0.4.0) ============================
/// O dono (2026-10-17): *"meu amigo gringo deu sugestoes de passivas pras seguintes raças"* -- a lista inteira, as leituras e os
/// numeros escolhidos estao no `PEDIDO-16.md` e no cabecalho de `Core/Races/Passivas.cs`. Elas VEM COM A RACA (o dono: nao se
/// compram), e o Demonic Saiyan e uma ATIVA do Legendary Primal.
///
/// O CORE CONTA, ESTE ARQUIVO LIGA:
///   1. O TIQUE (<see cref="TickDasRaciais"/>): monta a `EntradaRacial` do corpo, o `EstadoRacial` devolve o `EfeitoRacial`, e o
///      efeito e ESCRITO nos canais -- `Fighter.racial*` e os campos raciais do `CombatState`. Nunca acumulado (a regra do
///      `AplicarForma`).
///   2. OS EVENTOS: o soco (<see cref="AoGolpeRacial"/>), o tiro (<see cref="DanoDeKiRacial"/> e <see cref="AoTiroRacial"/>), o
///      disparo (<see cref="FatorRacialDoDisparo"/>), a morte (<see cref="NegarMorteRacial"/>).
///   3. AS ATIVAS (<see cref="UsarAtivaRacial"/>), pelo canal de habilidade de sempre.
///
/// QUEM TEM KIT (`KitDoCorpo`, `GameServer.RaciaisDaIa.cs`): a PESSOA (o jogador, o corpo de bancada com conta) e -- desde 2026-10-17,
/// a pedido do dono (*"os boss de saga tb vao ter todas essas passivas e ativas a depender de sua raça"*) -- o CORPO DO MUNDO: o povo e
/// os chefes de saga, que a IA faz usar as ativas. Fica de fora a copia do dono (sem papel e sem conta). Os DEBUFFS de skill inimiga
/// (Spirit Disruption, Ruthless Mind) caem em quem quer que seja acertado -- o estado racial existe em todo corpo.
/// ==================================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>Um tique racial de um corpo: entrada -> conta -> canais. Chamado pelo `TickDosRelogiosDoCorpo`, em todo corpo.</summary>
	private void TickDasRaciais(ServerPlayer pl, double dt)
	{
		if (pl.Combate == null || dt <= 0) return;
		EstadoRacial est = pl.Racial;
		long agora = NowMs();
		MontarEntradaRacial(pl, est.Entrada);
		est.Calcular(agora, dt);
		EscreverOEfeitoRacial(pl, est, agora, dt);
		AndarAsRaciaisDoServidor(pl, est, agora);
	}

	private void MontarEntradaRacial(ServerPlayer pl, EntradaRacial e)
	{
		Fighter f = pl.Ficha;
		CombatState c = pl.Combate!;
		e.Kit = KitDoCorpo(pl);
		e.Skills = SkillsDoCorpo(pl);   // (2026-10-17) as dos kits de raca: o kit inteiro, ou a escolha do bio (`SkillsRaciais`)
		e.EmCombate = c.EmCombate > 0;
		e.CombateSegundos = pl.Forma.CombateSegundos;
		e.Forma = pl.Forma.Atual;
		FormaDef? d = pl.Forma.Def;
		e.MultDaForma = Math.Max(f.ssjBuff, 1);
		e.FormaTreinavel = d != null && !pl.Forma.NaBase && Catalogo.SustentarTreina(d);
		e.FormaDominada = !pl.Forma.NaBase && Dominou(pl, e.Forma);
		e.FormaDivina = d is { PedeGodKi: >= 0 };   // (`PedeGodKi = -1` = a forma nao pede God Ki: e mortal)
		e.FormaSaiyajin = d is { Linha: LinhaDeForma.Saiyajin or LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal or LinhaDeForma.Futuro };
		e.FormaLendaria = d is { Linha: LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal };
		e.LssjAberto = pl.Forma.Despertou("primal_legendary");
		e.MaiorMortalDominada = Dominou(pl, "ssj3") || Dominou(pl, "legendary") ? 3
							  : Dominou(pl, "ssj2") || Dominou(pl, "c_type") ? 2
							  : Dominou(pl, "ssj1") || Dominou(pl, Catalogo.IdWrathful) ? 1 : 0;
		e.DegrauSsj4 = Passivas.DegrauSsj4(e.Forma);
		e.Ki = f.MaxKi > 0 ? f.Ki / f.MaxKi : 0;
		e.Vigor = f.maxstamina > 0 ? f.stamina / f.maxstamina : 0;
		e.Vida = f.HP / 100;
		e.Tronco = c.Corpo.Achar(Body.NomeDoTronco) is { } t ? (t.Decepado ? 0 : t.Fracao) : 1;
		e.Barra = f.powerMod;
		e.Relaxado = f.relaxado;
		e.TemRabo = c.Corpo.Achar("Rabo") is { Decepado: false };
		e.Treinando = f.train;
		e.Caido = f.KO || f.dead;
		e.Parado = Vec2.Distance(pl.Pos, pl.PosDoTiqueRacial) < 1;
		pl.PosDoTiqueRacial = pl.Pos;
		e.Corpo = c.Corpo;
	}

	/// <summary>
	/// O EFEITO NOS CANAIS. A soma na forma (False Advantage, Prodigy) vira RAZAO sobre o multiplicador da forma de agora -- o
	/// `ssjBuff` e o unico lugar que o guarda, e somar dentro dele brigaria com o `AplicarForma`, que o reescreve.
	/// </summary>
	private void EscreverOEfeitoRacial(ServerPlayer pl, EstadoRacial est, long agora, double dt)
	{
		EfeitoRacial o = est.Efeito;
		Fighter f = pl.Ficha;
		CombatState c = pl.Combate!;

		double m = Math.Max(f.ssjBuff, 1);
		f.racialBp = o.Bp * (m + o.SomaNaForma) / m;
		f.racialPhysoff = o.Physoff;
		f.racialPhysdef = o.Physdef;
		f.racialSpeed = o.Speed;
		f.racialKioff = o.Kioff;
		f.racialKidef = o.Kidef;
		f.racialKiskill = o.Kiskill;
		f.racialTecnica = o.Tecnica;
		f.racialRegenKiPassiva = o.RegenKiPassiva;
		f.racialRegenKiAtiva = o.RegenKiAtiva;
		f.racialRegenVigor = o.RegenVigor;
		f.racialCustoKi = o.CustoKi;
		f.racialCargaDeTecnica = o.CargaDeTecnica;
		f.racialTetoDaCarga = o.TetoDaCarga;
		f.racialGanhoDeTreino = o.GanhoDeTreino;

		// O TETO DE KI: quem DESCE preserva a razao (o que passa do teto novo se perde -- e o "lowering their ki" da Disrupcao); quem
		// SOBE guarda o Ki que tinha (o tanque cresce, e enche com o tempo). O Demonic enche de uma vez (`EncherReservas`).
		if (Math.Abs(o.MaxKi - est.KiAplicado) > 1e-9)
		{
			double maxAntes = f.MaxKi;
			f.racialMaxKi = o.MaxKi;
			f.Statify();
			if (o.MaxKi < est.KiAplicado && maxAntes > 0) f.Ki *= f.MaxKi / maxAntes;
			est.KiAplicado = o.MaxKi;
		}
		if (Math.Abs(o.MaxVigor - est.VigorAplicado) > 1e-9)
		{
			if (o.MaxVigor < est.VigorAplicado) f.stamina *= o.MaxVigor / est.VigorAplicado;
			est.VigorAplicado = o.MaxVigor;
		}
		if (o.EncherReservas)
		{
			f.Ki = f.MaxKi;
			f.stamina = f.maxstamina * o.MaxVigor;
		}

		c.ReducaoRacial = o.ReducaoDeDano;
		c.DanoDadoRacial = o.DanoDado;
		c.DanoRecebidoRacial = o.DanoRecebido;
		c.Tenaz = o.Tenaz;
		c.Inabalavel = o.Inabalavel;
		c.SemEfeitosNegativos = o.SemEfeitosNegativos;
		c.Invulneravel = o.Invulneravel;
		c.FatorDosMembros = o.FatorDosMembros;
		c.FatorDoNocaute = o.FatorDoNocaute;

		if (o.BarraCheia && (f.powerMod < 1 || f.relaxado))
		{
			f.powerMod = 1;
			f.relaxado = false;
		}
		if (o.TroncoPorSegundo > 0) FerirOTroncoPeloTransbordar(pl, o.TroncoPorSegundo * dt);
		if (o.KiPorSegundo > 0) f.Ki = Math.Min(f.Ki + f.MaxKi * o.KiPorSegundo * dt, Math.Max(f.Ki, f.MaxKi));
		if (o.VigorPorSegundo > 0) f.stamina = Math.Min(f.stamina + f.maxstamina * o.VigorPorSegundo * dt, Math.Max(f.stamina, f.maxstamina));
		if (o.KiDrenadoPorSegundo > 0)
		{
			f.Ki -= f.MaxKi * o.KiDrenadoPorSegundo * o.Dreno * dt;
			if (f.Ki <= 0)
			{
				f.Ki = 0;
				est.PoderMaximo = false;
				Avisar(pl, "o Ki acaba, e o MAX POWER se desfaz.");
			}
		}
		if (est.DemonicoCarregou)
		{
			EstadoRacial.Recarregar(f.RecargasRaciais, Passivas.Demonico, agora, Passivas.RecargaDemonica);
			Avisar(pl, "DEMONIC SAIYAN! A esfera verde se rompe -- e a cada 15 s o seu Ki e o seu fôlego vão a 300%, até a luta acabar.");
			AvisarPertoG3(pl, RaioDaVista, $"a esfera verde em volta de {pl.Name} se rompe: um SAIYAJIN DEMONÍACO!", excetoCentro: true);
		}
	}

	/// <summary>
	/// O QUE O SERVIDOR FAZ POR TIQUE ALEM DOS CANAIS: o Prodigy (a maestria de 50% na forma mortal nova), a Revitalizing
	/// Metamorphosis (a troca de forma com o tronco ferido), a Legendary Resurrection (o nocaute com o tronco quebrado) e a
	/// execucao da Ruthless Mind.
	/// </summary>
	private void AndarAsRaciaisDoServidor(ServerPlayer pl, EstadoRacial est, long agora)
	{
		KitRacial k = est.Entrada.Kit;
		Fighter f = pl.Ficha;
		string forma = pl.Forma.Atual;

		// ---- PRODIGY: forma mortal ja comeca com 50% ----
		if (est.Entrada.Tem(SkillRacial.Prodigio) && !pl.Forma.NaBase && !est.Entrada.FormaDivina
			&& pl.Forma.Maestria.De(forma) < Passivas.MaestriaDoProdigio)
		{
			pl.Forma.Maestria.Por(forma, Passivas.MaestriaDoProdigio);
			pl.SigAtributos = "";
		}

		// ---- REVITALIZING METAMORPHOSIS: transformou com o tronco ferido ----
		// (2026-10-17) NA FORMA DA PROPRIA LINHA: a do Frost Demon, e -- pro bio-androide que escolheu a skill do sangue Icer -- a Super Perfeita, a
		// unica forma que ele veste (ESCOLHA minha: o texto do amigo e "transformar", e o bio nao tem as do Frost Demon).
		if (forma != est.FormaAnterior)
		{
			if (est.Entrada.Tem(SkillRacial.Metamorfose) && !pl.Forma.NaBase
				&& pl.Forma.Def is { Linha: LinhaDeForma.FrostDemon or LinhaDeForma.BioAndroide }
				&& est.Entrada.Tronco < 1 && EstadoRacial.FaltaDaRecarga(f.RecargasRaciais, Passivas.Metamorfose, agora) <= 0)
				Metamorfosear(pl, est, agora);
			est.FormaAnterior = forma;
		}

		// ---- LEGENDARY RESURRECTION: o nocaute com o tronco quebrado ----
		if ((k & KitRacial.LendarioPrimal) != 0 && f.KO && !f.dead && pl.Combate is { } c)
		{
			if (agora >= est.RessurreicaoAnimacaoAte && agora < est.RessurreicaoBuffAte)
			{
				// CAIR NOS 3 MIN E MORRER, por cima de qualquer imortalidade (o `ignorarSeguro`).
				est.RessurreicaoBuffAte = 0;
				Avisar(pl, "o corpo ja tinha voltado uma vez -- desta vez a lenda NAO se levanta.");
				c.Morrer(ignorarSeguro: true);
			}
			else if (TroncoQuebrado(c) && EstadoRacial.FaltaDaRecarga(f.RecargasRaciais, Passivas.Ressurreicao, agora) <= 0)
			{
				c.Levantar();
				Ressuscitar(pl, est, agora);
			}
		}

		// ---- RUTHLESS MIND: a execucao em curso ----
		if (est.ExecucaoAlvo != 0) AndarAExecucao(pl, est, agora);

		// ---- (2026-10-17) O GRITO DO DESABAFO: os tiros sorteados saindo ----
		if (est.GritoDesde >= 0) AndarOGrito(pl, est, agora);
	}

	/// <summary>O tronco esta quebrado ou zerado (o "0%/broken Torso HP" do amigo).</summary>
	private static bool TroncoQuebrado(CombatState c) =>
		c.Corpo.Achar(Body.NomeDoTronco) is not { } t || t.Decepado || t.Quebrado || t.Vida <= 0;

	/// <summary>
	/// O PRECO DO TRANSBORDAR: dano no tronco, nao letal (derruba, nao mata). Pelo funil (`CombatState.Ferir`), e com o nocaute pelo
	/// nucleo como todo dano que encosta no corpo.
	/// </summary>
	private void FerirOTroncoPeloTransbordar(ServerPlayer pl, double porcento)
	{
		CombatState c = pl.Combate!;
		if (pl.Ficha.KO || pl.Ficha.dead || c.Corpo.Achar(Body.NomeDoTronco) is not { Decepado: false } tronco) return;
		bool feriu = c.Ferir(tronco, tronco.VidaMax * porcento / 100, letal: false);
		c.SincronizarVida();
		if (feriu && c.Corpo.DeveNocautear())
		{
			c.Nocautear(MeleeResolver.TetoDoNocaute, porVital: true);
			Avisar(pl, "o poder que transbordava rasga o seu corpo por dentro -- voce cai.");
		}
	}

	// =====================================================================
	// OS EVENTOS
	// =====================================================================
	/// <summary>
	/// O SOCO CHEGOU (o `Resolver` ja decidiu): os dois lados. Quem LEVOU conta o tipo do golpe (Saiyan's Resolve) e gasta um golpe
	/// de armadura (Primal Bloodlust); quem DEU soma o sadismo, conta a Disrupcao (e a aplica no alvo) e paga o soco do Max Power.
	/// </summary>
	private void AoGolpeRacial(ServerPlayer a, ServerPlayer d, GolpeResultado r)
	{
		long agora = NowMs();
		bool limpo = r.Desfecho is Desfecho.Acertou or Desfecho.Critico;
		TipoDeGolpe tipo = TemBuff(a, "Ki_Blade") || TemBuff(a, "Ki_Sword") ? TipoDeGolpe.Corte : TipoDeGolpe.Contusao;
		if (limpo) d.Racial.AoLevarGolpe(tipo, agora);

		if (a.Racial.AoDarGolpe(limpo, agora) && d.Combate is { SemEfeitosNegativos: false })
		{
			double fracao = Passivas.DisrupcaoPorGolpe * d.Racial.RugirContra(Passivas.Disrupcao, agora);
			d.Racial.AplicarDisrupcao(fracao, agora);
			Avisar(d, $"o golpe de {a.Name} injeta ki no seu corpo -- o seu fluxo de ki se desregula (Ki máximo -{d.Racial.KiDesregulado * 100:0}%).");
			Avisar(a, $"SPIRIT DISRUPTION: o ki de {d.Name} se desregula (-{d.Racial.KiDesregulado * 100:0}%).");
		}
		if (!a.Racial.DisrupcaoArmada && a.Racial.DisrupcaoAcertos + a.Racial.DisrupcaoFalhas > 0)
		{
			a.Racial.DisrupcaoAcertos = a.Racial.DisrupcaoFalhas = 0;
			Avisar(a, "a aura da Spirit Disruption se apaga dos seus punhos.");
		}

		if (a.Racial.PoderMaximo)
			a.Ficha.Ki = Math.Max(0, a.Ficha.Ki - a.Ficha.MaxKi * Passivas.CustoDoSocoNoPoderMaximo * a.Racial.Efeito.CustoKi);
	}

	/// <summary>
	/// O DANO DE KI COM AS RACIAIS -- depois da conta do tiro e da Destruicao, antes do corpo: o dano racial de quem atirou (Primal
	/// Evolution, Bloodlust, Sadismo, Long-Awaited, e o +15% de ki do Perfect Control), o ki guardado do Limitless Power, e o que o
	/// RUGIDO do alvo deixa sobrar desta tecnica. A defesa racial do alvo (reducao, tenacidade) e do Core -- `AplicarNoMembro`.
	/// </summary>
	private double DanoDeKiRacial(Projetil p, ServerPlayer dono, ServerPlayer alvo, double dano)
	{
		if (dano <= 0) return dano;
		long agora = NowMs();
		dano *= (dono.Combate?.DanoDadoRacial ?? 1) * dono.Racial.Efeito.DanoDeKi;
		double guardado = dono.Racial.GastarLimitless(agora);
		if (guardado > 0)
		{
			dano += guardado;
			Avisar(dono, $"LIMITLESS POWER: o ki que voce segurou sai junto de {p.Nome}!");
		}
		dano *= alvo.Racial.RugirContra(p.Nome, agora);
		return dano;
	}

	/// <summary>O TIRO CHEGOU: na guarda, o Limitless Power guarda; sem guarda, o Saiyan's Resolve conta um golpe de ki.</summary>
	private void AoTiroRacial(Projetil p, ServerPlayer alvo, GolpeResultado r, double dano, bool naGuarda)
	{
		long agora = NowMs();
		if (naGuarda && !p.Fisico)
		{
			double guardado = alvo.Racial.AoBloquearKi(dano, alvo.Ficha.RecargasRaciais, agora);
			if (guardado > 0)
				Avisar(alvo, $"LIMITLESS POWER: voce segura parte do ki de {p.Nome} -- o seu proximo ataque de ki leva ele junto.");
			return;
		}
		if (r.Dano > 0) alvo.Racial.AoLevarGolpe(p.Fisico ? TipoDeGolpe.Contusao : TipoDeGolpe.Ki, agora);
	}

	/// <summary>
	/// O FATOR RACIAL DE UM DISPARO -- o Hidden Talent (a tecnica dominada; a mesma duas vezes seguidas). O Shattering the Limit e do
	/// canal (ele precisa do custo da carga) e vem por <paramref name="doLimite"/>. Multiplica o BP do tiro E o fator da onda, que e
	/// de onde a disputa refaz o BP do raio que cresce -- so um dos dois e o bonus sumiria no meio do feixe.
	/// </summary>
	private void FatorRacialDoDisparo(ServerPlayer pl, Projetil p, string verbo, double doLimite = 1)
	{
		double fator = doLimite;
		if (verbo.Length > 0) fator *= pl.Racial.FatorDoTalento(verbo, TecnicaDominada(pl, verbo));
		if (Math.Abs(fator - 1) < 1e-9) return;
		p.Bp *= fator;
		p.MultDeOnda *= fator;
	}

	/// <summary>
	/// A TECNICA ESTA DOMINADA? A skill que a CONCEDE (pelo catalogo, `Skill.Verbos`, ou por um degrau de nivel, `Degrau.Verbos`)
	/// esta no nivel maximo -- o "Mastered skill" do Hidden Talent.
	/// </summary>
	private bool TecnicaDominada(ServerPlayer pl, string verbo)
	{
		if (_skills == null) return false;
		foreach ((string path, int nivel, double _) in pl.Niveis.Todas)
		{
			if (_skills.Get(path) is not { } s || nivel < Math.Max(1, s.MaxNivel)) continue;
			if (Array.IndexOf(s.Verbos, verbo) >= 0) return true;
			if (Jandirus.Core.Skills.RegrasDeNivel.Get(path) is { } regra)
				foreach (Jandirus.Core.Skills.Degrau dg in regra.Degraus)
					if (Array.IndexOf(dg.Verbos, verbo) >= 0) return true;
		}
		return false;
	}

	/// <summary>
	/// A MORTE NEGADA PELAS RACIAIS (pendurada no `NegarMorte` do corpo): o Sheer Willpower (de pe por um fio, como a aura) e a
	/// Legendary Resurrection (o golpe fatal no tronco). Durante o buff da ressurreicao a morte NAO e negada -- cair ali e morrer.
	/// </summary>
	private bool NegarMorteRacial(ServerPlayer pl)
	{
		if (pl.Combate is not { } c || EhCadaver(pl)) return false;
		EstadoRacial est = pl.Racial;
		long agora = NowMs();
		if (est.Efeito.NegaMorte)
		{
			foreach (BodyPart p in c.Corpo.Partes)
				if (!p.Decepado && p.Vida < p.VidaMax * PisoDoSeguroDaAura) p.Vida = p.VidaMax * PisoDoSeguroDaAura;
			c.SincronizarVida();
			Avisar(pl, "SHEER WILLPOWER: o golpe era fatal -- e voce simplesmente NAO CAI.");
			return true;
		}
		if ((est.Entrada.Kit & KitRacial.LendarioPrimal) != 0 && agora >= est.RessurreicaoBuffAte && TroncoQuebrado(c)
			&& EstadoRacial.FaltaDaRecarga(pl.Ficha.RecargasRaciais, Passivas.Ressurreicao, agora) <= 0)
		{
			Ressuscitar(pl, est, agora);
			return true;
		}
		return false;
	}

	/// <summary>
	/// LEGENDARY RESURRECTION: a animacao invulneravel (3 s, o corpo preso nela), 30% de Ki, vigor e vida de volta, e +10% em todos
	/// os stats por 3 min. A recarga de 10 min (escolha) comeca aqui.
	/// </summary>
	private void Ressuscitar(ServerPlayer pl, EstadoRacial est, long agora)
	{
		Fighter f = pl.Ficha;
		CombatState c = pl.Combate!;
		est.RessurreicaoAnimacaoAte = agora + (long)(Passivas.SegundosDaAnimacaoDaRessurreicao * 1000);
		est.RessurreicaoBuffAte = est.RessurreicaoAnimacaoAte + (long)(Passivas.SegundosDaRessurreicao * 1000);
		EstadoRacial.Recarregar(f.RecargasRaciais, Passivas.Ressurreicao, agora, Passivas.RecargaDaRessurreicao);
		foreach (BodyPart p in c.Corpo.Partes)
			if (!p.Decepado) p.Vida = Math.Min(p.VidaMax, Math.Max(p.Vida, 0) + p.VidaMax * Passivas.RefilDaRessurreicao);
		c.SincronizarVida();
		f.Ki = Math.Min(Math.Max(f.Ki, f.MaxKi), f.Ki + f.MaxKi * Passivas.RefilDaRessurreicao);
		f.stamina = Math.Min(Math.Max(f.stamina, f.maxstamina), f.stamina + f.maxstamina * Passivas.RefilDaRessurreicao);
		c.Stun = Math.Max(c.Stun, Passivas.SegundosDaAnimacaoDaRessurreicao);   // preso na animacao
		c.Invulneravel = true;
		Avisar(pl, "LEGENDARY RESURRECTION! O corpo se recusa a cair -- e volta MAIS FORTE por 3 minutos. Cair de novo agora é morrer.");
		AvisarPertoG3(pl, RaioDaVista, $"{pl.Name} se ergue do chão envolto em ki verde -- LEGENDARY RESURRECTION!", excetoCentro: true);
		GD.Print($"[server] {pl.Name}: Legendary Resurrection");
	}

	/// <summary>REVITALIZING METAMORPHOSIS: membros +20%, decepados de volta com 10%, carga de Ki +40% por 1 min; recarga 3 min.</summary>
	private void Metamorfosear(ServerPlayer pl, EstadoRacial est, long agora)
	{
		CombatState c = pl.Combate!;
		foreach (BodyPart p in c.Corpo.Partes)
		{
			if (p.Aninhado) continue;
			if (p.Decepado)
			{
				if (c.Corpo.Regenerar(p)) p.Vida = p.VidaMax * Passivas.MembroDaMetamorfose;
			}
			else p.Vida = Math.Min(p.VidaMax, p.Vida + p.VidaMax * Passivas.CuraDaMetamorfose);
		}
		c.SincronizarVida();
		est.MetamorfoseAte = agora + (long)(Passivas.SegundosDaMetamorfose * 1000);
		EstadoRacial.Recarregar(pl.Ficha.RecargasRaciais, Passivas.Metamorfose, agora, Passivas.RecargaDaMetamorfose);
		Avisar(pl, "REVITALIZING METAMORPHOSIS: a casca nova fecha as feridas -- e o seu Ki carrega mais rapido por um minuto.");
	}

	// =====================================================================
	// AS ATIVAS
	// =====================================================================
	/// <summary>A recusa de quem nao tem o sangue -- e o texto que a tabela de verbs cobra (`RequisitosDosVerbos`).</summary>
	public const string RecusaDoSangue = "essa habilidade não é do seu sangue.";

	/// <summary>O canal de habilidade das ativas raciais. Devolve se o id era uma delas.</summary>
	private bool UsarAtivaRacial(ServerPlayer pl, string id)
	{
		if (Passivas.KitDaAtiva(id) == KitRacial.Nenhum) return false;

		// (2026-10-17) as dos kits de RACA pela skill -- no bio-androide, a que ele escolheu do sangue (`SkillsRaciais.TemAtiva`)
		if (!SkillsRaciais.TemAtiva(KitDoCorpo(pl), SkillsDoCorpo(pl), id)) { Avisar(pl, RecusaDoSangue); return true; }
		if (pl.Combate == null || EhCadaver(pl)) return true;
		if (pl.Ficha.KO) { Avisar(pl, "voce precisa estar de pe."); return true; }
		// (2026-10-17) OS 3 S DO GRITO SAO DO GRITO: nenhuma ativa sai no meio dele (nem o transbordar religado por cima).
		if (pl.Racial.GritoDesde >= 0) { Avisar(pl, "você está desabafando: espere o grito acabar."); return true; }

		EstadoRacial est = pl.Racial;
		long agora = NowMs();
		Dictionary<string, long> recargas = pl.Ficha.RecargasRaciais;

		// os dois toggles com segundo toque e a ira desligam SEM olhar a recarga (ela comeca quando eles acabam)
		switch (id)
		{
			case Passivas.Transbordar when est.TransbordarDesde >= 0:
				DesabafarRacial(pl, est, ref est.TransbordarDesde, Passivas.SegundosPraDesabafar, agora);
				return true;
			case Passivas.TransbordarKi when est.KiTransbordandoDesde >= 0:
				DesabafarRacial(pl, est, ref est.KiTransbordandoDesde, Passivas.SegundosPraDesabafarKi, agora);
				return true;
			case Passivas.Ira when est.IraDesde >= 0:
				if (agora - est.IraDesde < Passivas.SegundosMinimosDaIra * 1000)
				{
					Avisar(pl, $"a ira ainda nao assentou -- da pra desligar daqui a {Math.Ceiling(Passivas.SegundosMinimosDaIra - (agora - est.IraDesde) / 1000.0):0} s.");
					return true;
				}
				est.IraDesde = -1;
				EstadoRacial.Recarregar(recargas, Passivas.Ira, agora, Passivas.RecargaDaIra);
				Avisar(pl, "voce engole a ira. (Recarga: 3 min.)");
				return true;
			case Passivas.PoderMaximo:
				est.PoderMaximo = !est.PoderMaximo;
				Avisar(pl, est.PoderMaximo
					? "MAX POWER! Os musculos incham -- ataque, defesa e velocidade 1,5x, enquanto o Ki aguentar."
					: "voce solta o Max Power.");
				if (est.PoderMaximo) AvisarPertoG3(pl, RaioDaVista, $"os musculos de {pl.Name} incham: MAX POWER!", excetoCentro: true);
				return true;
		}

		double falta = EstadoRacial.FaltaDaRecarga(recargas, id, agora);
		if (falta > 0) { Avisar(pl, $"ainda em recarga ({FormatarFalta(falta)})."); return true; }

		int degrau = Passivas.DegrauSsj4(pl.Forma.Atual);
		switch (id)
		{
			case Passivas.Ferocidade:
				est.FerocidadeAte = agora + (long)(Passivas.SegundosDaFerocidade(degrau) * 1000);
				EstadoRacial.Recarregar(recargas, id, agora, Passivas.RecargaDaFerocidade);
				Avisar(pl, $"UNMATCHED FEROCITY! Nada te segura: poder inteiro, sem dreno e sem efeito negativo por {Passivas.SegundosDaFerocidade(degrau):0} s.");
				AvisarPertoG3(pl, RaioDaVista, $"{pl.Name} ruge -- uma FEROCIDADE PRIMAL toma conta dele!", excetoCentro: true);
				return true;

			case Passivas.Transbordar:
				est.TransbordarDesde = agora;
				Avisar(pl, "OVERFLOWING POWER: o poder começa a transbordar -- ele sobe a cada 10 s, e o seu corpo paga. Aperte de novo pra desabafar (depois de 30 s).");
				return true;

			case Passivas.TransbordarKi:
				est.KiTransbordandoDesde = agora;
				Avisar(pl, "OVERFLOWING KI: o seu Ki começa a transbordar -- o tanque cresce a cada 10 s, e o seu corpo paga. Aperte de novo pra desabafar (depois de 60 s).");
				return true;

			case Passivas.ForcaImparavel:
				est.ImparavelAte = agora + (long)(Passivas.SegundosImparavel * 1000);
				EstadoRacial.Recarregar(recargas, id, agora, Passivas.RecargaImparavel);
				Avisar(pl, "UNSTOPPABLE FORCE: 30 s sem recuo e apanhando 10% menos.");
				return true;

			case Passivas.SedeDeSangue:
				est.SedeGolpes = Passivas.GolpesDaSede(degrau);
				est.SedeBonus = 0;
				est.SedeAte = agora + (long)(Passivas.SegundosDaSede * 1000);
				EstadoRacial.Recarregar(recargas, id, agora, Passivas.RecargaDaSede);
				Avisar(pl, $"PRIMAL BLOODLUST: {est.SedeGolpes} golpes sem recuo -- e cada um que voce leva vira +2% de dano seu (por 2 min).");
				return true;

			case Passivas.Demonico:
			{
				string nao = PorQueNaoDemonico(pl, est);
				if (nao.Length > 0) { Avisar(pl, nao); return true; }
				est.DemonicoCargaDesde = agora;
				Avisar(pl, "DEMONIC SAIYAN: uma esfera verde te envolve -- fique PARADO por 15 s. Andar desfaz a carga.");
				AvisarPertoG3(pl, RaioDaVista, $"uma esfera de ki verde se forma em volta de {pl.Name}...", excetoCentro: true);
				return true;
			}

			case Passivas.SegundoFolego:
				est.FolegoAte = agora + (long)(Passivas.SegundosDoFolego * 1000);
				EstadoRacial.Recarregar(recargas, id, agora, Passivas.RecargaDoFolego);
				Avisar(pl, "SECOND WIND: o fôlego volta -- Ki e vigor se recuperam por 20 s.");
				return true;

			case Passivas.ForcaDeVontade:
				est.VontadeAte = agora + (long)(Passivas.SegundosDaVontade * 1000);
				EstadoRacial.Recarregar(recargas, id, agora, Passivas.RecargaDaVontade);
				Avisar(pl, "SHEER WILLPOWER: por 15 s nada te derruba -- nem um golpe fatal.");
				AvisarPertoG3(pl, RaioDaVista, $"{pl.Name} se recusa a cair: SHEER WILLPOWER!", excetoCentro: true);
				return true;

			case Passivas.Disrupcao:
				est.DisrupcaoArmada = true;
				est.DisrupcaoAcertos = est.DisrupcaoFalhas = 0;
				EstadoRacial.Recarregar(recargas, id, agora, Passivas.RecargaDaDisrupcao);
				Avisar(pl, "SPIRIT DISRUPTION: uma aura envolve os seus punhos e pernas -- cada golpe limpo desregula o ki de quem leva.");
				return true;

			case Passivas.Ira:
				est.IraDesde = agora;
				KiaiDaIra(pl);
				Avisar(pl, "UNRESTRAINED WRATH: a ira toma conta -- BP +20%, Ki máximo -20%. Da pra engolir de volta depois de 30 s.");
				return true;

			case Passivas.MenteCruel:
				ComecarExecucao(pl, est, agora);
				return true;

			case Passivas.Sadismo:
				est.SadismoAte = agora + (long)(Passivas.SegundosDoSadismo * 1000);
				est.SadismoBonus = 0;
				EstadoRacial.Recarregar(recargas, id, agora, Passivas.RecargaDoSadismo);
				Avisar(pl, "SADISTIC NATURE: por 1 min, cada golpe que entra sem guarda deixa o proximo mais forte.");
				return true;
		}
		return true;
	}

	private static string FormatarFalta(double segundos) =>
		segundos >= 60 ? $"{Math.Floor(segundos / 60):0} min {Math.Ceiling(segundos % 60):0} s" : $"{Math.Ceiling(segundos):0} s";

	/// <summary>Por que o Demonic Saiyan nao sai agora (vazio = sai).</summary>
	private string PorQueNaoDemonico(ServerPlayer pl, EstadoRacial est)
	{
		if (!pl.Forma.Despertou("primal_legendary")) return "o Demonic Saiyan pede o Legendary Super Saiyan desperto.";
		if (!est.Entrada.FormaLendaria) return "o Demonic Saiyan so sai numa forma Lendaria.";
		if (pl.Combate is not { EmCombate: > 0 }) return "o Demonic Saiyan so sai em combate.";
		if (pl.Forma.CombateSegundos < Passivas.LutaPraDemonico)
			return $"o sangue ainda nao ferveu: faltam {Math.Ceiling(Passivas.LutaPraDemonico - pl.Forma.CombateSegundos):0} s de luta.";
		if (est.DemonicoDesde >= 0 || est.DemonicoCargaDesde >= 0) return "o Demonic Saiyan ja esta de pe.";
		return "";
	}

	/// <summary>
	/// O SEGUNDO TOQUE DO TRANSBORDAR: so depois do prazo; tira o buff e comeca o GRITO -- 3 s dentro da esfera verde, plantado, com os tiros
	/// saindo sozinhos em rumos, instantes e velocidades sorteados (`AndarOGrito`). O dono (2026-10-17): *"n é so kiblasts saindo de forma
	/// organizada de uma vez , seriam 3 segundos do personagem gritando e uma esfera de energia verde parecendo com a do demonic em volta do
	/// personagem e nesses 3 segundos kiblasts de forma nao ordenada /aleatoria saindo pra todo lado sendo ate dificil pros inimigos
	/// desviarem"*. Era a rajada de doze tiros de uma vez, um a cada 30 graus.
	/// </summary>
	private void DesabafarRacial(ServerPlayer pl, EstadoRacial est, ref long desde, double prazo, long agora)
	{
		double t = (agora - desde) / 1000.0;
		if (t < prazo)
		{
			Avisar(pl, $"ainda nao da pra desabafar -- faltam {Math.Ceiling(prazo - t):0} s.");
			return;
		}
		desde = -1;
		est.GritoDesde = agora;
		est.GritoTiros = 0;
		est.GritoInstantes = GritoOrdenadoDeTeste ? new double[Passivas.TirosDoGrito] : Passivas.InstantesDoGrito(_sorteioDoGrito);
		pl.Moving = false;
		// A TRAVA VAI PRO CLIENTE NO APERTO (o efeito da trava de tecnica -- `LocalPlayer.PresoPelaTecnica`): o corpo fica plantado os 3 s
		// (`PresoPeloG12`) e a tela nem tenta o passo. Quem a solta e o laco de sempre (`AvisarAsTravasG12`), quando o grito cala.
		if (pl.Peer != null && _presosAvisadosG12.Add(pl.Id)) MandarEfeito(pl, Protocol.EfeitoDaTravaDeTecnica, -1);
		Falar(pl, Protocol.Fala.Diz, "AAAAAAAAAAAAAAHHHHHH!!!");
		Avisar(pl, $"voce DESABAFA o que transbordava: {Passivas.SegundosDoGrito:0} s gritando, e o ki explode pra todo lado.");
		AvisarPertoG3(pl, RaioDaVista, $"{pl.Name} grita -- e o ki explode pra todo lado!", excetoCentro: true);
	}

	/// <summary>O sorteio do grito: os instantes, os rumos, os bercos e as velocidades dos tiros.</summary>
	private readonly Random _sorteioDoGrito = new();

	/// <summary>
	/// DEFEITO INJETADO (bancada `--passivasteste`): o desabafo de antes -- os tiros todos no primeiro instante, um a cada tanto de graus, do
	/// centro e na mesma velocidade. Falso em jogo, sempre.
	/// </summary>
	internal static bool GritoOrdenadoDeTeste;

	/// <summary>Este corpo esta gritando o desabafo? (A trava de tecnica le -- `PresoPeloG12`.)</summary>
	private bool GritandoODesabafo(int id) => _players.TryGetValue(id, out ServerPlayer? pl) && pl.Racial.GritoDesde >= 0;

	/// <summary>
	/// O GRITO, a cada tique: saem os tiros cujo instante sorteado ja passou; aos 3 s ele cala. Cair cala antes (`EstadoRacial.Calcular`), e os
	/// tiros que faltavam nao saem.
	/// </summary>
	private void AndarOGrito(ServerPlayer pl, EstadoRacial est, long agora)
	{
		double t = (agora - est.GritoDesde) / 1000.0;
		while (est.GritoTiros < est.GritoInstantes.Length && est.GritoInstantes[est.GritoTiros] <= t)
		{
			TiroDoGrito(pl, est.GritoTiros);
			est.GritoTiros++;
		}
		if (t >= Passivas.SegundosDoGrito) est.GritoDesde = -1;
	}

	/// <summary>
	/// UM TIRO DO GRITO: rumo sorteado em QUALQUER angulo (nao os oito), berco sorteado dentro da esfera (ate meio tile do centro) e uma das
	/// cinco velocidades de bola -- o que faz a rajada nao ter ritmo nem desenho. O dano e o da bola basica, o da rajada de antes.
	/// </summary>
	private void TiroDoGrito(ServerPlayer pl, int i)
	{
		double ang = GritoOrdenadoDeTeste ? i * Math.Tau / Passivas.TirosDoGrito : _sorteioDoGrito.NextDouble() * Math.Tau;
		var rumo = new Vec2((float)Math.Cos(ang), (float)Math.Sin(ang));
		double raio = GritoOrdenadoDeTeste ? 0 : _sorteioDoGrito.NextDouble() * Passivas.TilesDoBercoDoGrito * ZoneCollision.TileSize;
		double angDoBerco = _sorteioDoGrito.NextDouble() * Math.Tau;
		Vec2 berco = pl.Pos + new Vec2((float)(Math.Cos(angDoBerco) * raio), (float)(Math.Sin(angDoBerco) * raio));
		double velocidade = GritoOrdenadoDeTeste ? 1 : Passivas.VelocidadesDoGrito[_sorteioDoGrito.Next(Passivas.VelocidadesDoGrito.Length)];
		Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = pl.Ficha.Ekioff * DanoDeKi.Log10Min(pl.Ficha.blastskill, 10),
			Velocidade = velocidade,
			AlcanceTiles = 12,
			Nome = "rajada de ki",
		}, rumoDado: rumo, deOnde: berco, verbo: "Basic_Blast");
	}

	/// <summary>O KIAI DA IRA: quem esta a 3 tiles e jogado pra fora (a onda de choque do Kiai, `OndaDeChoqueG6`, com o raio da ira).</summary>
	private void KiaiDaIra(ServerPlayer pl)
	{
		Falar(pl, Protocol.Fala.Diz, "HAAAAAAA!!");
		float raio = (float)(Passivas.TilesDoKiai * ZoneCollision.TileSize);
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash).ToList())
		{
			if (o == pl || EhCadaver(o) || o.Combate == null || o.Combate.Intocavel) continue;
			Vec2 fora = o.Pos - pl.Pos;
			if (fora.Length > raio) continue;
			Vec2 rumo = fora.LengthSquared > 1e-4f ? fora.Normalized() : MeleeArea.Frente(pl.Facing);
			Arremessar(o, rumo, pl.Ficha.expressedBP, ForcaDoSoproG6(pl.Ficha, o.Ficha));
			MarcarAgressao(o, pl);
			Avisar(o, $"o kiai de {pl.Name} joga voce longe.");
		}
		AnunciarGesto(pl, Protocol.GestoDoCorpo.Kiai);
	}

	// =====================================================================
	// A RUTHLESS MIND
	// =====================================================================
	/// <summary>
	/// A EXECUCAO: alguem caido na frente, e o golpe letal ligado (executar e matar). 3 s parado junto dele; andar, cair ou o alvo
	/// levantar desfaz. A recarga comeca quando ela FECHA.
	/// </summary>
	private void ComecarExecucao(ServerPlayer pl, EstadoRacial est, long agora)
	{
		if (pl.Combate is not { Letal: true }) { Avisar(pl, "executar e matar: ligue o golpe letal primeiro."); return; }
		ServerPlayer? alvo = QuemEstaNaFrente(pl, o => o.Ficha.KO && !o.Ficha.dead && !EhCadaver(o));
		if (alvo == null) { Avisar(pl, "nao ha ninguem caido na sua frente pra executar."); return; }
		est.ExecucaoAlvo = alvo.Id;
		est.ExecucaoFechaEm = agora + (long)(Passivas.SegundosDaExecucao * 1000);
		pl.PosDoTiqueRacial = pl.Pos;
		Avisar(pl, $"RUTHLESS MIND: voce se debruça sobre {alvo.Name}...");
		Avisar(alvo, $"{pl.Name} se debruça sobre voce -- e nao e pra ajudar.");
	}

	private void AndarAExecucao(ServerPlayer pl, EstadoRacial est, long agora)
	{
		_players.TryGetValue(est.ExecucaoAlvo, out ServerPlayer? alvo);
		bool desfez = alvo == null || !alvo.Ficha.KO || alvo.Ficha.dead || pl.Ficha.KO || pl.Ficha.dead
					  || !est.Entrada.Parado || alvo.Zone.Hash != pl.Zone.Hash
					  || Vec2.Distance(alvo.Pos, pl.Pos) > 2 * ZoneCollision.TileSize;
		if (desfez)
		{
			est.ExecucaoAlvo = 0;
			Avisar(pl, "a execucao se desfaz.");
			return;
		}
		if (agora < est.ExecucaoFechaEm) return;

		est.ExecucaoAlvo = 0;
		EstadoRacial.Recarregar(pl.Ficha.RecargasRaciais, Passivas.MenteCruel, agora, Passivas.RecargaDaMenteCruel);
		MarcarAgressao(alvo!, pl);
		alvo!.Combate?.Morrer();
		Avisar(pl, $"RUTHLESS MIND: {alvo.Name} morre devagar -- e todo mundo em volta sente.");
		AvisarPertoG3(pl, (float)(Passivas.TilesDaMenteCruel * ZoneCollision.TileSize), $"{pl.Name} EXECUTA {alvo.Name} com crueldade -- o seu corpo gela.", excetoCentro: true);

		float raio = (float)(Passivas.TilesDaMenteCruel * ZoneCollision.TileSize);
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash).ToList())
		{
			if (o == pl || o == alvo || EhCadaver(o) || o.Combate == null || Vec2.Distance(o.Pos, pl.Pos) > raio) continue;
			if (EAmigoDe(o, pl) || o.Combate.SemEfeitosNegativos) continue;
			double fracao = Passivas.DebuffDaMenteCruel * o.Racial.RugirContra(Passivas.MenteCruel, agora);
			o.Racial.AplicarMenteCruel(fracao, agora);
		}
	}

	/// <summary>O "friend" da Ruthless Mind: quem declarou Bom, Muito Bom ou Amor por quem executa (a aba People).</summary>
	private static bool EAmigoDe(ServerPlayer quem, ServerPlayer de) =>
		de.Assinatura.Length > 0
		&& quem.Social.RelacaoCom(de.Assinatura) is Relacao.Bom or Relacao.MuitoBom or Relacao.Amor;
}
