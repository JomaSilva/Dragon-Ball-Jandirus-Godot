using Jandirus.Core.Stats;

namespace Jandirus.Core.Combat;

/// <summary>O que aconteceu quando o soco chegou.</summary>
public enum Desfecho : byte
{
	// O SOCO NO VAZIO -- nao havia corpo nenhum na frente. NAO e "a pontaria falhou": pontaria que
	// falha contra alguem e esquiva (ver o passo 1 do `Resolver`), e o `Resolver` nunca devolve isto.
	Errou,
	// O ALVO SAIU DA FRENTE. Duas portas chegam aqui, como no original: a passiva (a pontaria de
	// quem bateu nao alcancou a velocidade de quem apanhou -- de graca) e a ativa, que custa Ki e
	// marca `GolpeResultado.EsquivaAtiva`.
	Esquivou,
	Aparou,       // bloqueou com um membro
	Contra,       // aparou NA HORA certa e devolveu
	Acertou,
	Critico,
}

/// <summary>
/// O relato de um golpe. O servidor resolve UMA vez e conta a mesma historia pros dois lados
/// -- nenhum cliente recalcula dano.
/// </summary>
public struct GolpeResultado
{
	public Desfecho Desfecho;
	public double Dano;
	public string Membro;
	public bool Quebrou, Decepou, Nocauteou, Morreu;
	public double Stun;

	/// <summary>O rabo foi arrancado por este golpe (regra separada -- ver o passo 7).</summary>
	public bool RaboArrancado;

	// AS PECAS QUE CAIRAM NAO VIAJAM NESTE RELATO. Havia aqui uma `List<PecaDeCorpo>? PecasCaidas`,
	// preenchida so pelo soco e lida so pelo anuncio do soco -- e por isso o dano em area e o dano
	// direto decepavam sem que peca nenhuma nascesse. Quem diz o que caiu agora e o
	// `CombatState.AoDecepar`, no instante do `LopLimb`, pra TODO funil de dano (ver `CombatState.Ferir`).
	// O que sobra aqui e o `Decepou`, que e o bit do jato de sangue e da plateia.

	/// <summary>
	/// A esquiva foi a ATIVA (a que custa Ki e depende de <see cref="CombatState.ChanceEsquiva"/>),
	/// e nao a passiva por velocidade.
	///
	/// As duas saem como <see cref="Desfecho.Esquivou"/> de proposito -- no original elas caem no
	/// MESMO ramo `if(0)` e desenham a MESMA coisa (`CombatMovement.dm:269-289`). O que muda e
	/// quem paga e quem aprende: so a ativa alimenta a maestria do Instinto Superior
	/// (`GameServer.Combat.cs`, `AoEsquivarPorInstinto`) e so ela e a analoga do combo dodge do DM
	/// (`:290-307`), o unico lugar onde o `haszanzo` decide se ha vulto.
	/// </summary>
	public bool EsquivaAtiva;

	public bool Encostou => Desfecho is Desfecho.Acertou or Desfecho.Critico or Desfecho.Aparou;
}

/// <summary>
/// A RESOLUCAO DE UM SOCO -- o `hitProc` do BYOND, reescrito.
///
/// A ordem importa e e esta: pontaria -> bloqueio -> esquiva -> critico -> dano -> corpo.
/// Cada passo pode encerrar o golpe. Roda so no SERVIDOR: o `prob()` da comparacao de
/// estilos torna o dano nao-deterministico, entao nao ha como as duas pontas concordarem
/// calculando cada uma por si.
///
/// OS QUATRO DEFEITOS DO ORIGINAL, CONSERTADOS AQUI (decisao do dono do projeto):
///
///  1. Os dois chamadores do `hitProc` passavam CINCO argumentos pra uma assinatura de SEIS.
///     O `Type` caia no slot do `forcehit` e o ultimo parametro ficava nulo. Efeito no jogo:
///     o soco NUNCA critava, NUNCA atordoava (`stunCount = 100 * null = 0`) e a esquiva
///     automatica do Ultra Instinct NUNCA disparava contra melee. Aqui os tres funcionam.
///
///  2. `parentlimb` nunca era atribuido, entao decepar o braco nao levava a mao. Corrigido
///     no <see cref="Body.Decepar"/>, que leva junto o que estava dentro.
///
///  3. O contra-ataque de bloqueio perfeito lia o tempo de guarda do ATACANTE, e por isso era
///     inalcancavel. Aqui le o do DEFENSOR, que e quem esta bloqueando.
///
///  4. `AttackMultiple` passava seis argumentos pra cinco parametros e embaralhava tudo (o
///     `iscrit` virava `vampdamage`). Aqui os golpes multiplos chamam esta mesma funcao, uma
///     vez por golpe, sem caminho paralelo.
/// </summary>
public static class MeleeResolver
{
	/// <summary>Janela, em segundos, pra um bloqueio virar contra-ataque.</summary>
	public const double JanelaContra = 0.25;

	/// <summary>
	/// ============================ O TETO DO NOCAUTE, e ele nao e o prazo dele ============================
	/// **ERA 12 SEGUNDOS CRAVADOS PRA TODO MUNDO, E ISSO NAO E DO DM.** O nocaute de luta do original
	/// nao tem prazo nenhum: `Injuries.dm:283` chama `KO(-1)`, e `-1` nao casa com `if(KOtimer>0)`
	/// nem com `else if(!KOtimer)` (`KO.dm:112-116`) -- nenhum `spawn` e agendado. O corpo acorda
	/// quando o nucleo ferido volta acima da linha (`Injuries.dm:286-289`), ou seja **pela CURA**.
	/// Medido contra o laco do DM, uma raca comum leva **~142 s** (90 s de tag de combate parada mais
	/// ~52 s subindo) e o Majin **~20 s**, porque ele cura em combate. O port dava 12 s aos dois.
	///
	/// O que sobra aqui e o TETO -- a rede pra quem nao consegue subir (o corpo dentro de uma
	/// estrela, o esmagado por gravidade) --, e **o numero e do original**: `KO.dm:116` agenda
	/// `rand(2000,2500)` decisegundos pro nocaute cronometrado de jogador, ou seja 200 a 250 s.
	/// 225 e o meio dessa faixa. (O NPC tem faixa propria e mais longa la, `rand(3000,5000)` =
	/// 300-500 s; **nao foi portada de proposito** -- com 151 habitantes no mundo, um corpo parado
	/// por sete minutos e uma decisao nova, e nao uma heranca.)
	///
	/// Quem consulta este numero esta pedindo o TETO, e nao "quanto dura um nocaute": a resposta
	/// disso mora em <see cref="CombatState.NocautePorVital"/>.
	/// ================================================================================================
	/// </summary>
	public const double TetoDoNocaute = 225;

	/// <summary>
	/// Resolve um golpe de <paramref name="a"/> em <paramref name="d"/>.
	/// <paramref name="anguloGraus"/> e medido a partir da FRENTE do defensor ate a direcao
	/// de onde o golpe vem: 0 = de frente, 180 = pelas costas.
	/// </summary>
	public static GolpeResultado Resolver(CombatState a, CombatState d, double anguloGraus,
										  Random rng, double tipo = 1, double addDano = 0)
	{
		var r = new GolpeResultado { Membro = "" };
		// O MORTO TAMBEM APANHA (dono, 2026-09-05: "corpos mortos ainda sao corpos de personagem ... deveria
		// dar pra atacar e ferir mais"). Era `d.F.dead || d.Intocavel`: o cadaver e o fantasma do Outro
		// Mundo eram invulneraveis por decreto. Agora so o intocavel (cinematica, imunidade de embate) escapa;
		// o corpo morto perde vida nos membros como qualquer corpo, e o `Morrer()` de quem ja esta morto e
		// vazio (`CombatState.Morrer`) -- ninguem morre duas vezes por um soco no defunto.
		if (d.Intocavel) return r;

		// golpear GASTA a carencia de quem acabou de renascer: o escudo e pra sair de perto,
		// nao pra voltar batendo de graca
		a.Carencia = 0;

		a.EntrarEmCombate();
		d.EntrarEmCombate();

		// Nocauteado nao esquiva nem bloqueia: quem esta no chao leva tudo.
		bool indefeso = d.F.KO;

		// === 1. PONTARIA ===============================================
		// ============================ PONTARIA QUE FALHA E ESQUIVA, NAO "ERRO" ============================
		// `CombatMovement.dm:192`: `if(!prob(bhit) && !M.blocking) hit = 0`. E o `hit = 0` e o ramo
		// rotulado `if(0)//dodge` (`:269-289`) -- com o Zanzoken no defensor, o anel de choque nos pes
		// dele, a faisca em quem bateu, os dois sons e a linha "[M] dodges [src]!". O DM NUNCA teve um
		// desfecho "errou o soco em alguem": ou o alvo estava fora de alcance (e ai nao havia soco), ou
		// o alvo SAIU DA FRENTE.
		//
		// Este ponto devolvia <see cref="Desfecho.Errou"/>, que o cliente desenha como soco no vazio:
		// mudo e invisivel. Resultado no jogo -- e foi assim que o dono percebeu -- um personagem
		// rapido esquivava a luta inteira sem NENHUM sinal de que estava esquivando, porque o unico
		// caminho que produzia `Esquivou` era o Ultra Instinto (o `TentarEsquiva` la embaixo, unico
		// escritor de `ChanceEsquiva`). O desfecho estava certo na conta e errado no nome.
		//
		// `Desfecho.Errou` continua existindo e continua sendo o soco no VAZIO -- ele nao passa por
		// aqui, e anunciado direto por `GameServer.AnunciarSocoNoAr` com `Alvo = 0`.
		//
		// O `!d.Bloqueando` e o `&& !M.blocking` do DM, e o proprio original explica na linha ao lado:
		// quem se comprometeu com a guarda NAO esta desviando -- some com o teste de pontaria e vai
		// direto pro passo 2, onde a guarda ou segura ou cede e o golpe entra inteiro.
		// =================================================================================================
		double bhit = CombatMath.Pontaria(a.F, d.F, indefeso ? 0 : d.Deflexao, a.Precisao);
		if (!indefeso && !d.Bloqueando && !Sorteou(rng, bhit))
		{
			r.Desfecho = Desfecho.Esquivou;
			return r;
		}

		// === 2. BLOQUEIO ==============================================
		if (!indefeso && d.Bloqueando)
		{
			BodyPart? guarda = EscolherGuarda(d.Corpo, rng);
			double custoKi = d.F.MaxKi * CombatKnobs.CustoKiDaGuarda;

			if (guarda == null) d.Guardar(false);        // sem braco nem perna nao ha o que erguer
			else if (d.F.Ki < custoKi) d.Guardar(false);  // sem energia a guarda cai sozinha
			else if (GuardaAguenta(d.Corpo, CombatMath.BpModulus(a.F.expressedBP, d.F.expressedBP), rng))
			{
				d.F.Ki -= custoKi;   // bloquear CUSTA: nao da pra segurar guarda a luta inteira

				// CONSERTO 3: a janela de contra le o tempo de guarda do DEFENSOR.
				if (d.ContraPronto && d.TempoDeGuarda <= JanelaContra)
				{
					d.ContraPronto = false;
					d.RecargaContra = CombatKnobs.RecargaDoContra;
					r.Desfecho = Desfecho.Contra;
					r.Stun = CombatKnobs.DuracaoStun;
					a.Stun = Math.Max(a.Stun, r.Stun);
					return r;
				}

				// O golpe entra TODO no membro que aparou, e ignora a zona mirada: e o preco
				// de bloquear, e o que faz braco de quem bloqueia muito acabar quebrado.
				double dmgB = Calcular(a, d, anguloGraus, tipo, addDano) * ReducaoDaGuarda(d.F);
				r.Desfecho = Desfecho.Aparou;
				r.Dano = dmgB;
				AplicarNoMembro(d, guarda, dmgB, a.Letal, ref r);
				return r;
			}
			// a guarda CEDEU: o golpe passa e entra inteiro
		}

		// === 3. ESQUIVA ================================================
		// CONSERTO 1: a esquiva autonoma agora e consultada de verdade contra socos.
		if (!indefeso && TentarEsquiva(d, rng))
		{
			r.Desfecho = Desfecho.Esquivou;
			r.EsquivaAtiva = true;   // esta paga Ki e paga maestria -- ver `EsquivaAtiva`
			return r;
		}

		// === 4. CRITICO ================================================
		// CONSERTO 1: no original o crit dependia de um parametro que chegava sempre nulo.
		bool crit = !indefeso && rng.NextDouble() * 100 < CombatKnobs.ChanceCrit;

		// === 5. DANO ===================================================
		double dano = Calcular(a, d, anguloGraus, tipo, addDano);

		// A REDUCAO DE DANO DO DEFENSOR (hoje so a Aura of Destruction). Entra AQUI, antes do
		// critico e antes de encostar no corpo -- ver `CombatState.ReducaoDeDano`.
		if (d.ReducaoDeDano > 0) dano *= 1 - Math.Clamp(d.ReducaoDeDano, 0, 100) / 100.0;
		if (crit)
		{
			dano *= (rng.Next(CombatKnobs.CritMin, CombatKnobs.CritMax + 1) + a.F.Etechnique) / 10.0;
			r.Stun = CombatKnobs.DuracaoStun;
			d.Stun = Math.Max(d.Stun, r.Stun);
		}

		// === 6. CORPO ==================================================
		BodyPart? membro = d.Corpo.Sortear(a.ZonaMirada, rng);
		if (membro == null) return r;   // corpo sem nada atingivel

		r.Desfecho = crit ? Desfecho.Critico : Desfecho.Acertou;
		r.Dano = dano;
		AplicarNoMembro(d, membro, dano, a.Letal, ref r);

		// === 7. O RABO ================================================
		ArrancarRabo(a, d, anguloGraus, dano, ref r);
		return r;
	}

	/// <summary>Dano minimo que arranca um rabo (o `dmg>5` do original).</summary>
	public const double DanoQueArrancaRabo = 5;

	/// <summary>Fracao de vida abaixo da qual o rabo pode ser arrancado (`hpratio<0.6`).</summary>
	public const double VidaParaPerderRabo = 0.6;

	/// <summary>
	/// ARRANCAR O RABO e uma regra a PARTE do sorteio de membro, e sempre foi.
	///
	/// No original (`CombatMovement.dm:309-314`): o golpe precisa ter ENCOSTADO, o alvo tem
	/// que estar virado pro MESMO lado que o atacante (ou seja: pego de costas), o golpe tem
	/// que ser letal, o alvo tem que estar abaixo de 60% de vida e o dano acima de 5.
	///
	/// ESSA REGRA NUNCA RODOU NO JOGO ORIGINAL. Ela testa `hpratio < 0.6`, e o `hpratio` e
	/// definido como `max(HP/100, 0.6)` -- tem PISO em 0,6, entao a condicao e
	/// matematicamente impossivel. Ninguem nunca teve o rabo arrancado a soco em dez anos de
	/// jogo. Aqui a conta usa a vida REAL do corpo, entao a regra passa a existir de fato.
	///
	/// Nao e detalhe cosmetico: sem rabo o Saiyajin perde o Oozaru e passa a treinar 2,5x
	/// mais rapido (`tailgain`).
	/// </summary>
	private static void ArrancarRabo(CombatState a, CombatState d, double anguloGraus,
									 double dano, ref GolpeResultado r)
	{
		if (!a.Letal || dano <= DanoQueArrancaRabo) return;
		if (anguloGraus < 135) return;                       // so pelas costas
		if (d.Corpo.Vida() >= VidaParaPerderRabo * 100) return;

		BodyPart? rabo = d.Corpo.Achar("Rabo");
		if (rabo == null || rabo.Decepado) return;

		// PELA PORTA UNICA DO `LopLimb` (`CombatState.Arrancar`): e ela que desconta o Ki e avisa quem
		// poe a peca no chao. Este e o unico arranque do jogo que nao passa pelo dano -- o rabo sai
		// sem ter zerado --, e por isso ele chama a porta em vez de o `Ferir` chama-la por ele.
		d.Arrancar(rabo);
		d.SincronizarVida();
		r.RaboArrancado = true;
	}

	/// <summary>
	/// A CADEIA DE DANO, na ordem do original. Cada termo esta explicado no
	/// <see cref="CombatMath"/>; o que muda aqui e so a sequencia, que e o que ninguem pode
	/// reordenar sem mudar o balanceamento inteiro.
	/// </summary>
	private static double Calcular(CombatState a, CombatState d, double anguloGraus,
								   double tipo, double addDano)
	{
		double dmg = CombatMath.DanoBase(a.F, d.F);
		dmg = CombatMath.Resistencia(dmg, a.TiposDeDano, d.Resistencias);
		dmg += addDano;

		if (a.F.dashing) dmg += 2;          // entrar correndo soma impacto
		if (dmg < 1) dmg = 1;
		if (d.F.dashing) dmg *= 1.25;       // e ser pego correndo custa mais caro ainda

		// DE ONDE VEM O GOLPE, medido a partir da frente do DEFENSOR: 0 grau e de frente,
		// 180 e pelas costas. Pegar alguem de costas vale 1,5x -- e o que premia flanquear em
		// vez de trocar soco parado.
		dmg *= anguloGraus switch
		{
			>= 135 => 1.5,
			>= 90 => 1.4,
			>= 45 => 1.2,
			_ => 1.0,
		};

		dmg += tipo;                        // golpe pesado ja entra somando

		// A propria tecnica e defesa TIRAM dano do golpe. Parece invertido, mas e o freio que
		// segura a curva no fim do jogo: sem ele, dois veteranos se matam num soco. Ate 4,4x.
		double auto = (a.F.Etechnique * 2 + a.F.Ephysdef * 2) / 10;
		if (auto > 0.01) dmg /= auto;

		// e SO ENTAO o gap de poder entra, multiplicando tudo que sobrou
		dmg *= CombatMath.BpModulus(a.F.expressedBP, d.F.expressedBP);
		dmg = CombatMath.Armadura(dmg, d.F.Esuperkiarmor);

		return Math.Max(dmg, 0);
	}

	/// <summary>
	/// UM DANO JA CALCULADO NUM MEMBRO SORTEADO -- o `DamageLimb(dmg, selectzone, murderToggle, 5)`
	/// do DM, que e como o projetil de ki fere (`objects.dm:440`).
	///
	/// ============================ POR QUE ELE ENTRA POR AQUI, E NAO POR UM CAMINHO PROPRIO ============================
	/// A conta do dano de ki e outra (ver <see cref="DanoDeKi"/>), mas o que acontece DEPOIS do
	/// numero pronto e exatamente o mesmo: sorteia membro, fere, quebra, decepa se for letal e o
	/// membro ja estava zerado, sincroniza a vida, e entao mata OU nocauteia -- nessa ordem, com o
	/// `Morrer()` podendo ser negado. Escrever esse trecho de novo do lado do projetil criaria a
	/// segunda casa de "o que um golpe faz com um corpo", e o dia em que alguem mexer numa delas o
	/// raio e o soco passam a matar por regras diferentes.
	///
	/// O que NAO entra aqui e a pontaria, a guarda, a esquiva e o crit: um raio nao erra por
	/// `Etechnique` (ele erra por nao encostar, que e geometria do servidor) e nao crita. Por isso
	/// isto e um metodo separado e nao um parametro do <see cref="Resolver"/> -- juntar os dois
	/// exigiria um `if` no meio da cadeia de melee pra pular metade dela.
	/// ==========================================================================================================
	/// </summary>
	public static GolpeResultado AplicarDanoPronto(CombatState d, double dano, bool letal,
												   Random rng, string? zona = null)
	{
		var r = new GolpeResultado { Membro = "" };
		if (d.Intocavel || dano <= 0) return r;   // o morto tambem apanha -- ver o `Resolver`

		BodyPart? membro = d.Corpo.Sortear(zona, rng);
		if (membro == null) return r;

		r.Desfecho = Desfecho.Acertou;
		r.Dano = dano;
		AplicarNoMembro(d, membro, dano, letal, ref r);
		return r;
	}

	private static void AplicarNoMembro(CombatState d, BodyPart membro, double dano, bool letal,
										ref GolpeResultado r)
	{
		bool eraQuebrado = membro.Quebrado;
		// PELO FUNIL (`CombatState.Ferir`), e nao pelo corpo direto: as duas portas de melee ja
		// recusam quem esta <see cref="CombatState.Intocavel"/> la em cima, mas o dia em que uma
		// terceira porta chamar isto sem conferir, o crivo continua aqui.
		//
		// E O FUNIL E QUEM ARRANCA. O `if (letal && membro.Vida <= 0 && ...)` que morava aqui embaixo
		// -- com a cascata, o Ki e a lista de pecas -- era a cauda que SO o soco tinha, e o dano em
		// area e o dano direto passavam pelo mesmo `Ferir` sem ela. Ela virou o `LopLimb` de dentro do
		// `DamageMe` (`CombatState.Ferir`), e este relato so LE o que sobrou do membro.
		d.Ferir(membro, dano, letal);

		r.Membro = membro.Nome;
		// O membro sorteado nunca chega aqui decepado (`Body.Sortear` pula os decepados), entao
		// "esta decepado agora" e "este golpe o arrancou". E o arrancado conta como QUEBRADO neste
		// golpe pela mesma razao de sempre: ele cruzou o limiar de quebra a caminho do zero, e o
		// `Quebrado` do membro so deixa de dizer isso porque um decepado nao tem fracao.
		r.Decepou = membro.Decepado;
		r.Quebrou = !eraQuebrado && (membro.Quebrado || membro.Decepado);

		d.SincronizarVida();

		// MORTE antes de NOCAUTE: quem morreu nao precisa cair primeiro. Raca que regenera
		// membro perdido entra em coma no lugar de morrer.
		// `Morrer()` pode ser NEGADO (ver `CombatState.NegarMorte`) -- e ai nao houve morte, entao o
		// resultado nao pode dizer que houve: quem le `r.Morreu` marca prazo de renascer e paga
		// Zenkai por uma morte que nao aconteceu.
		if (d.Corpo.DeveMorrer() && !d.Corpo.RegeneraDecepado)
		{
			r.Morreu = d.Morrer();
		}
		else if (!d.F.KO && d.Corpo.DeveNocautear())
		{
			r.Nocauteou = true;
			d.Nocautear(TetoDoNocaute, porVital: true);
		}
	}

	/// <summary>
	/// O membro que apara: braco tem tres vezes mais chance que perna -- e o reflexo natural.
	/// Membro quebrado ou perdido nao entra no rodizio.
	/// </summary>
	public static BodyPart? EscolherGuarda(Body corpo, Random rng)
	{
		var candidatos = new List<(BodyPart P, double Peso)>();
		foreach (BodyPart p in corpo.Partes)
		{
			if (p.Decepado || p.Quebrado || p.Aninhado) continue;
			if (p.Zona == "bracos") candidatos.Add((p, 3));
			else if (p.Zona == "pernas") candidatos.Add((p, 1));
		}
		if (candidatos.Count == 0) return null;

		double total = 0;
		foreach ((BodyPart _, double peso) in candidatos) total += peso;

		double sorte = rng.NextDouble() * total;
		foreach ((BodyPart p, double peso) in candidatos)
		{
			sorte -= peso;
			if (sorte <= 0) return p;
		}
		return candidatos[^1].P;
	}

	/// <summary>
	/// A guarda aguenta? Tres coisas a furam, e as tres importam:
	///
	///   * uma chance BASE -- sem ela o banco de prova mostrou 100% de golpes aparados com o
	///     corpo inteiro, e segurar o bloqueio virava a jogada dominante do jogo;
	///   * o GAP DE PODER -- quem e muito mais forte passa por cima da guarda, que e o que
	///     todo Dragon Ball mostra;
	///   * cada braco ou perna quebrado/perdido, 25% -- com o corpo em frangalhos nao ha
	///     guarda que segure.
	/// </summary>
	public static bool GuardaAguenta(Body corpo, double gapDoAtacante, Random rng)
	{
		int ruins = 0;
		foreach (BodyPart p in corpo.Partes)
			if (!p.Aninhado && (p.Zona == "bracos" || p.Zona == "pernas") && (p.Decepado || p.Quebrado))
				ruins++;

		double falha = CombatKnobs.FalhaBaseGuarda
					 + ruins * 25
					 + Math.Max(0, gapDoAtacante - 1) * CombatKnobs.FalhaGuardaPorGap;

		return rng.NextDouble() * 100 >= Math.Min(falha, 95);
	}

	/// <summary>
	/// Quanto do golpe a guarda deixa passar. Escala pela tecnica de QUEM BLOQUEIA -- no
	/// original escalava pela do ATACANTE, e por isso um oponente de tecnica baixa fazia o
	/// bloqueio AMPLIFICAR o dano em vez de reduzir.
	/// </summary>
	private static double ReducaoDaGuarda(Fighter defensor)
	{
		double t = Math.Clamp(defensor.Etechnique / 10.0, 0, 1);
		return Math.Clamp(0.6 - t * 0.35, 0.25, 0.6);   // deixa passar de 60% ate 25%
	}

	/// <summary>
	/// A esquiva ATIVA: custa Ki e so existe pra quem tem <see cref="CombatState.ChanceEsquiva"/>
	/// acima de zero.
	///
	/// ============================ O BURACO FOI PREENCHIDO ============================
	/// Este texto dizia *"hoje ninguem tem -- e o buraco por onde o Ultra Instinct entra depois"*.
	/// O Ultra Instinto ENTROU: `GameServer.Disciplinas.cs` escreve `pl.Combate.ChanceEsquiva` a
	/// partir da proficiencia ATUAL de quem esta com a forma ligada, e `GameServer.Combat.cs` ja
	/// conta com isso ao gastar o Ki da esquiva. O campo tem UM escritor de producao, que era
	/// exatamente o desenho: quem quiser um segundo (Zanzoken avancado, por exemplo) soma nele em
	/// vez de abrir uma segunda porta de esquiva.
	///
	/// O `<= 0` continua sendo a porta, e continua fechada por padrao -- quem nao tem a disciplina
	/// nao esquiva, e nao paga Ki por isso.
	/// ================================================================================
	/// </summary>
	private static bool TentarEsquiva(CombatState d, Random rng)
	{
		if (d.ChanceEsquiva <= 0) return false;
		double custo = 0.05 * d.F.MaxKi / Math.Max(d.F.Etechnique, 0.1);
		if (d.F.Ki < custo) return false;
		if (rng.NextDouble() * 100 >= d.ChanceEsquiva) return false;

		d.F.Ki -= custo;
		return true;
	}

	private static bool Sorteou(Random rng, double pct) => rng.NextDouble() * 100 < pct;
}
