using Jandirus.Core.Stats;

namespace Jandirus.Core.Combat;

/// <summary>
/// O LUTADOR EM COMBATE: a ficha (<see cref="Fighter"/>) mais tudo que so existe enquanto
/// se troca golpe -- o corpo em partes, a guarda, o cronometro do proximo soco.
///
/// Fica separado da ficha de proposito. O <see cref="Fighter"/> tem 235 campos e vai inteiro
/// pro savefile; nada daqui deve ser gravado, porque nada daqui sobrevive a desconexao. Uma
/// guarda erguida ou um atordoamento nao sao patrimonio do personagem.
/// </summary>
public sealed class CombatState
{
	public Fighter F;
	public Body Corpo;

	// --- guarda ----------------------------------------------------------
	/// <summary>Guarda erguida (a tecla de bloqueio segurada).</summary>
	public bool Bloqueando;

	/// <summary>Ha quantos segundos a guarda subiu. Bloquear NA HORA vira contra-ataque.</summary>
	public double TempoDeGuarda;

	/// <summary>
	/// O contra-ataque desta guarda ainda esta disponivel.
	///
	/// UM POR SUBIDA DE GUARDA, e com recarga. Sem as duas travas bastava martelar a tecla de
	/// bloqueio pra devolver TODO golpe que chegasse -- a janela reabria a cada toque, e
	/// defender virava a jogada dominante.
	/// </summary>
	public bool ContraPronto;

	/// <summary>Quanto falta pra poder contra-atacar de novo.</summary>
	public double RecargaContra;

	// --- esquiva e mira --------------------------------------------------
	/// <summary>Deflexao: derruba a pontaria de quem ataca. O Zanzoken soma +20 aqui.</summary>
	public double Deflexao;

	/// <summary>Precisao: soma na propria pontaria.</summary>
	public double Precisao;

	/// <summary>
	/// Chance de ESQUIVA AUTONOMA, em porcento. Zero pra todo mundo -- e o Ultra Instinct que
	/// enche este numero. No BYOND a esquiva existia mas nunca era consultada contra socos
	/// (ver o defeito 1 no <see cref="MeleeResolver"/>).
	/// </summary>
	public double ChanceEsquiva;

	/// <summary>
	/// REDUCAO DE DANO em porcento, 0-100. Zero pra todo mundo -- e a Aura of Destruction que enche
	/// este numero (`UE_DR_BASE 15` + `UE_DR_PER_EN 0.10` x energia).
	///
	/// ============================ POR QUE UM CAMPO E NAO UM DESCONTO DEPOIS ============================
	/// A tentacao e reduzir o dano DEPOIS do resolvedor, no servidor. Nao funciona: quando o
	/// resolvedor devolve, o membro JA foi ferido, o corpo ja decidiu se quebrou e a morte ja foi
	/// avaliada -- devolver vida depois seria curar, nao reduzir, e um golpe fatal continuaria fatal.
	///
	/// Entao a reducao mora aqui, igual a esquiva: o Core le, o servidor escreve. E o Core continua
	/// sem saber que existe uma disciplina divina.
	/// ============================================================================================
	/// </summary>
	public double ReducaoDeDano;

	/// <summary>Zona que este lutador esta mirando: "cabeca", "torso", "bracos", "pernas".</summary>
	public string? ZonaMirada;

	/// <summary>
	/// O `murderToggle`. DESLIGADO por padrao: um soco nao-letal nunca leva um membro abaixo
	/// do limiar de nocaute, entao da pra treinar e brigar sem matar sem querer.
	/// </summary>
	public bool Letal;

	// --- cronometros (em segundos) ---------------------------------------
	/// <summary>Quanto falta pra poder golpear de novo.</summary>
	public double Recarga;

	/// <summary>
	/// GOLPES SEGUIDOS que encostaram. Serve pro NIVEL do som de impacto -- o original
	/// escolhe entre baque pequeno, medio e grande com `min(3, tipo + min(1, combo_count))`,
	/// e e isso que faz uma sequencia de socos ESQUENTAR em vez de virar um metronomo.
	/// Zera ao errar, ao ser aparado e sozinho depois de <see cref="JanelaDeCombo"/>.
	/// </summary>
	public int Combo;
	public double ComboAte;

	/// <summary>Quanto tempo um golpe conta como "seguido" do anterior.</summary>
	public const double JanelaDeCombo = 1.2;

	/// <summary>Marca mais um golpe encaixado.</summary>
	public void SomarCombo()
	{
		Combo = ComboAte > 0 ? Combo + 1 : 1;
		ComboAte = JanelaDeCombo;
	}

	public void ZerarCombo() { Combo = 0; ComboAte = 0; }

	/// <summary>Quanto falta de atordoamento. Acima de zero, nao ataca nem anda direito.</summary>
	public double Stun;

	/// <summary>
	/// A TAG DE "EM COMBATE" (90 s no original) -- e ela e so DISPLAY e MUSICA.
	///
	/// Nao confundir com <see cref="LutandoDeVerdade"/>: sao dois relogios com duracoes e
	/// propositos diferentes, e o DM os mantem separados de proposito (ver `CombatKnobs`).
	/// </summary>
	public double EmCombate;

	/// <summary>
	/// O RELOGIO DA MECANICA: 10 s desde o ultimo golpe. E ele que alimenta o `IsInFight` --
	/// velocidade de combate, regeneracao de Ki pela metade, ganho de skill.
	/// </summary>
	public double LutandoDeVerdade;

	/// <summary>
	/// ============================ O NOCAUTE E UM COMA COM TETO, E NAO UM CRONOMETRO ============================
	/// **O DM NAO DA PRAZO AO NOCAUTE DE LUTA.** Ele nasce em `Injuries.dm:283` como `KO(-1)`, e em
	/// `KO.dm:112-117` os dois ramos que agendariam o despertar sao `if(KOtimer>0)` e
	/// `else if(!KOtimer)` -- **`-1` nao e nenhum dos dois**, entao nenhum `spawn` casa e ninguem
	/// agenda o `Un_KO`. Quem acorda o corpo e `Injuries.dm:286-289`: o nucleo ferido subir de volta
	/// acima da linha de 20%. **A CURA E QUE DECIDE A HORA DE LEVANTAR** -- ver
	/// <see cref="NocautePorVital"/>.
	///
	/// Este campo continua existindo como **TETO** (ver `MeleeResolver.TetoDoNocaute`) e como o
	/// relogio dos nocautes que o DM realmente cronometra: o de HP zerado (`KO.dm:116`), o do
	/// `Death.dm:117` (`KO(20)`) e o do reflexo da mente.
	/// =====================================================================================================
	/// </summary>
	public double NocauteRestante;

	/// <summary>
	/// O `vitalKOd` do DM (`Injuries.dm:280-289`). LIGADO: este corpo caiu porque um NUCLEO cedeu, e
	/// entao ele levanta quando o nucleo voltar acima da linha -- e nao quando um relogio vencer.
	/// DESLIGADO: e um nocaute cronometrado (o verb de admin, o reflexo da mente), e so o relogio o
	/// levanta.
	///
	/// **A DISTINCAO E DO ORIGINAL E NAO E DETALHE**: sem ela, o `else if(vitalKOd)` de
	/// `Injuries.dm:287` acordaria de imediato todo mundo que foi nocauteado por outra razao, porque
	/// o nucleo desses corpos nunca esteve abaixo da linha.
	/// </summary>
	public bool NocautePorVital;

	/// <summary>
	/// O `limbregenbuffer` (`Injuries.dm:296-300`): pontos juntados rumo a devolver UM membro
	/// perdido. So o Majin, o Bio e as racas de `canheallopped` juntam -- ver
	/// <see cref="Regeneracao.PontosPorSegundo"/>. Nao vai pro savefile: um membro pela metade nao e
	/// patrimonio, e o DM tambem o perde no relog.
	/// </summary>
	public double BufferDeMembro;

	/// <summary>
	/// CARENCIA DE RENASCIMENTO, em segundos. Quem acabou de voltar nao pode ser atingido.
	///
	/// Nao e cortesia: sem isto, morrer ao lado de quem matou e renascer no mesmo ponto vira
	/// laco -- o teste de rede com dois robos mostrou exatamente isso, morte atras de morte
	/// no mesmo lugar. A carencia CAI no instante em que o renascido golpeia: escudo pra
	/// levantar e ir embora, nao pra voltar batendo.
	/// </summary>
	public double Carencia;

	/// <summary>
	/// ============================ ESTE CORPO ESTA PRESO NUMA CINEMATICA AGORA? ============================
	/// O dono: *"durante cinematicas de transformacao, o personagem ficaria IMUNE A DANOS pra ninguem
	/// poder MATAR o player ou NPCs enquanto ele transforma"*.
	///
	/// **E PORTE, NAO DESENHO NOVO.** No DM a flag existe e se chama `attackable`, e ela nao mora na
	/// proc da cena -- mora nas procs que a CHAMAM, envolvendo a chamada: `supersaiyanbuff.dm:314`
	/// zera, `:320` roda o `SSJCinematic()`, `:350` devolve o um; o mesmo par cerca o USSJ (`:387`/
	/// `:410`), o SSJ2 (`:435`/`:467`), o SSJ3 (`:480`/`:530`) e mais quatro degraus, e se repete em
	/// `lssjbuff.dm`, `IcerTransform.dm:102-104`, `HeranBuff.dm:99`, `perfecttrans.dm:3` e
	/// `imperfecttrans.dm:3`. O `move = 0` das procs de cena NAO e a imunidade: em `attack cmn.dm:98`
	/// o `move` e do ATACANTE e o `attackable` e do ALVO -- portar so o `move` seria portar metade.
	///
	/// **GANCHO E NAO CAMPO**, pelo mesmo motivo do <see cref="NegarMorte"/> la embaixo: quem sabe da
	/// cena e o servidor (`ServerPlayer.CenaSegundos`, escrito so pelo `MarcarCena` e abatido so pelo
	/// `TickDaForma`), e o Core nao conhece forma, cliente nem cinematica. **Ele PERGUNTA, ele nao
	/// guarda.** Um segundo bit aqui precisaria ser apagado em todo jeito de a cena acabar -- o prazo
	/// vencendo, o nocaute no meio dela, a morte, a reversao pra base -- e este port ja perdeu duas
	/// vezes exatamente assim (`Alem.MsNoAlem` e a aureola). Derivado, o escudo cai no MESMO gesto em
	/// que a cena cai, e nao ha linha pra ninguem esquecer.
	///
	/// Instalado uma vez por corpo, no `GameServer.PrepararCombate` -- que e por onde passam o jogador
	/// (`Entrar`) e o corpo sem dono (`PorNoMundo`), entao **NPC tem o mesmo escudo que o jogador**,
	/// sem crivo por tipo de corpo.
	/// ==================================================================================================
	/// </summary>
	public Func<bool>? EmCinematica;

	/// <summary>
	/// NAO PODE SER ATINGIDO AGORA -- e as duas razoes moram na MESMA pergunta de proposito.
	///
	/// Sao dezoito leitores espalhados pelo servidor (a busca do soco, o arranque, a mira, o varredor
	/// do projetil, o cone das tecnicas, o embate de ki, o dano em area). Uma segunda propriedade pra
	/// a imunidade de cinematica significaria visitar os dezoito e acertar todos -- e o que sobrasse
	/// seria o "as vezes" que ninguem consegue reproduzir.
	/// </summary>
	public bool Intocavel => Carencia > 0 || EmCinematica?.Invoke() == true;

	/// <summary>
	/// ============================ ESTE CORPO ESTA SENDO ARREMESSADO AGORA? ============================
	/// **PORTE QUE FALTAVA, E ELE E LITERAL.** O efeito de knockback do DM escreve DUAS linhas ao entrar
	/// (`Movement Effects.dm:39-40`):
	///
	///     target.KB += 1
	///     target.canfight -= 1        &lt;-- esta
	///
	/// e as devolve ao sair (`:52-53`). E `canfight` e a **primeira** recusa do `testAttack()`
	/// (`attack_bck.dm:175`: `if(attacking || ... || !canfight || KO ...) return FALSE`), que e o portao
	/// de todo `Attack()` do jogo. Ou seja: **no original, quem esta voando pelo golpe do outro NAO SOCA.**
	///
	/// O port tinha portado o `KB` (o <see cref="ServerPlayer.TiquesDeVoo"/>, que ja deita o corpo e
	/// recusa o passo) e **nao** o `canfight`. O resultado media exatamente o relato do dono: o corpo
	/// arremessado continuava aceitando `Atacar` durante os 0,8-0,9 s de voo, e cada um daqueles socos
	/// saia do ramo do vazio (`AlvoNaFrente` devolve nulo porque o inimigo ficou 512 px pra tras) --
	/// **animacao e assobio de soco no ar, sem dodge, sem mensagem e sem acerto**. *"por uns segundos
	/// (1 ou 2) MEUS SOCOS N ACERTAM ELE"*: um ou dois segundos e a duracao do voo.
	///
	/// **GANCHO E NAO CAMPO**, pelo mesmo motivo do <see cref="EmCinematica"/> logo acima: quem sabe do
	/// voo e o servidor (`ServerPlayer.TiquesDeVoo`, escrito so pelo `Arremessar` e abatido so pelo
	/// `TickDoEmpurrao`), e o Core nao conhece tique de voo nem correcao de posicao. Derivado, a recusa
	/// morre no MESMO instante em que o voo morre -- inclusive quando ele acaba na parede, no outro
	/// corpo ou porque o `Arremessar` foi chamado de novo. Nao ha bit pra ninguem esquecer de apagar.
	///
	/// Instalado uma vez por corpo, no `GameServer.PrepararCombate` -- entao **NPC e clone tem a mesma
	/// recusa que o jogador**, sem crivo por tipo de corpo.
	/// ==================================================================================================
	/// </summary>
	public Func<bool>? SendoArremessado;

	/// <summary>
	/// Composicao do golpe por tipo, e o que o corpo resiste. Os defaults sao os do jogo:
	/// soco = 2 de fisico + 1 de energia contra resistencias 1, o que da o 1,5x conhecido.
	/// </summary>
	public Dictionary<string, double> TiposDeDano = new() { ["Physical"] = 2, ["Energy"] = 1 };
	public Dictionary<string, double> Resistencias = new() { ["Physical"] = 1, ["Energy"] = 1 };

	public CombatState(Fighter f, bool comRabo = false, PerfilDeRegen? regen = null)
	{
		F = f;
		Corpo = Body.Novo(comRabo);
		// O EIXO DA CURA ENTRA AQUI E SO AQUI. Era um `bool regenera` com uma lista de racas cravada
		// no servidor; agora e o genoma (`misc_stats["Regeneration"]`) -- ver `PerfilDeRegen`.
		Corpo.Regen = regen ?? PerfilDeRegen.Comum;
		SincronizarVida();
	}

	/// <summary>
	/// A vida da ficha e SEMPRE derivada do corpo. Escrever `HP` em qualquer outro lugar
	/// criaria duas verdades, e a que o jogador ve (a barra) e sempre a do corpo.
	/// </summary>
	public void SincronizarVida() => F.HP = Corpo.Vida();

	/// <summary>
	/// ============================ O FUNIL UNICO DO DANO NUM MEMBRO ============================
	/// Todo dano que ENCOSTA neste corpo entra por aqui: o soco (<see cref="MeleeResolver"/>), o raio
	/// de ki, a explosao e o dano em area (`EspalharDanoG3`), o esmagamento por gravidade, o calor da
	/// estrela, a sobrecarga da carga de Ki, o Kaio-ken que cobra o proprio corpo e o recuo da Aura of
	/// Destruction.
	///
	/// **ELE NASCEU MEDIDO, E O NUMERO E O ARGUMENTO**: havia SETE chamadas de `Corpo.Ferir` fora de
	/// bancada e QUATRO delas nao passavam por crivo nenhum -- o calor da estrela (`GameServer.Sol`), o
	/// recuo da aura, a sobrecarga de Ki e o Kaio-ken. Uma imunidade escrita so no caminho do soco e
	/// uma imunidade que o RAIO ignora, e quem morre transformando reporta "as vezes", que e o defeito
	/// que nao se acha. Com o funil, o crivo e um so e uma fonte nova de dano ja nasce coberta.
	///
	/// **DEVOLVE SE O CORPO REALMENTE PERDEU VIDA.** Quem conta nocaute e morte DEPOIS do laco precisa
	/// saber disso: um corpo que ja estava no limiar do nocaute quando a cena comecou cairia por um
	/// dano que nao aconteceu, e o escudo teria derrubado quem ele protegia.
	///
	/// `Corpo.Ferir` continua publico e sem crivo -- as bancadas machucam corpo de proposito, e uma
	/// bancada que nao consegue mais quebrar um membro nao mede nada.
	/// ========================================================================================
	/// </summary>
	public bool Ferir(BodyPart membro, double dano, bool letal)
	{
		if (Intocavel || dano <= 0) return false;
		Corpo.Ferir(membro, dano, letal);
		return true;
	}

	/// <summary>Passagem de tempo: cronometros, guarda, saida do nocaute.</summary>
	public void Tick(double dt)
	{
		if (Recarga > 0) Recarga = Math.Max(0, Recarga - dt);
		if (RecargaContra > 0) RecargaContra = Math.Max(0, RecargaContra - dt);
		if (ComboAte > 0 && (ComboAte = Math.Max(0, ComboAte - dt)) == 0) Combo = 0;
		if (Carencia > 0) Carencia = Math.Max(0, Carencia - dt);
		if (Stun > 0) Stun = Math.Max(0, Stun - dt);
		if (EmCombate > 0) EmCombate = Math.Max(0, EmCombate - dt);
		if (LutandoDeVerdade > 0) LutandoDeVerdade = Math.Max(0, LutandoDeVerdade - dt);

		TempoDeGuarda = Bloqueando ? TempoDeGuarda + dt : 0;

		F.IsInFight = LutandoDeVerdade > 0;   // o CURTO, nao a tag -- ver CombatKnobs.LutaDeVerdade

		if (!F.KO) return;

		// O RELOGIO SO ANDA SE ALGUEM O ARMOU. `Nocautear()` e a porta unica que arma; uma bancada
		// que escreve `Ficha.KO = true` na mao continua com o corpo caido ate mandar levanta-lo, que
		// e o que ela quis dizer.
		bool armado = NocauteRestante > 0;
		if (armado) NocauteRestante = Math.Max(0, NocauteRestante - dt);

		// ============================ QUEM ACORDA O CORPO E A CURA, E DEPOIS O TETO ============================
		// A ordem e a de `Injuries.dm:277-289`, e ela e ESTA e nao a inversa: o coma por nucleo acaba
		// quando o nucleo volta acima da linha, e o relogio e so a rede de seguranca de quem nao
		// consegue subir (ninguem regenera dentro de uma estrela em chamas, por exemplo).
		//
		// `DeveMorrer()` entra junto porque um nucleo DECEPADO nao satisfaz `DeveNocautear()` -- ele
		// nao esta "abaixo do limiar", ele nao esta. E o coma do Majin sem cabeca, e ele nao pode
		// terminar pela primeira pergunta.
		if (NocautePorVital && !Corpo.DeveNocautear() && !Corpo.DeveMorrer()) Levantar();
		else if (armado && NocauteRestante == 0) Levantar();
	}

	/// <summary>
	/// Pode golpear agora? Nocauteado, morto, atordoado, ARREMESSADO, ou ainda em recarga: nao.
	///
	/// A ordem e a do `testAttack()` do DM (`attack_bck.dm:175`), e as quatro primeiras recusas dele
	/// estao todas aqui: `attacking` (a <see cref="Recarga"/>), `!canfight` (o <see cref="Stun"/> **e**
	/// o <see cref="SendoArremessado"/> -- os dois escrevem `canfight -= 1` la, `Movement Effects.dm:40`
	/// e `:93`), `KO` e a morte.
	/// </summary>
	public bool PodeAtacar() =>
		!F.dead && !F.KO && Stun <= 0 && Recarga <= 0 && SendoArremessado?.Invoke() != true;

	/// <summary>
	/// Ergue ou baixa a guarda. Subir REARMA o cronometro (e ele que decide o contra-ataque)
	/// e so libera o contra se a recarga ja tiver passado.
	/// </summary>
	public void Guardar(bool erguida)
	{
		if (erguida && !Bloqueando)
		{
			TempoDeGuarda = 0;
			ContraPronto = RecargaContra <= 0;
		}
		Bloqueando = erguida && !F.KO && !F.dead;
		if (!Bloqueando) ContraPronto = false;
	}

	/// <summary>Marca o inicio (ou a renovacao) da luta pros dois lados.</summary>
	public void EntrarEmCombate()
	{
		EmCombate = CombatKnobs.TagDeCombate;          // 90 s: a tag que o jogador VE e OUVE
		LutandoDeVerdade = CombatKnobs.LutaDeVerdade;  // 10 s: o que a mecanica sente
	}

	/// <summary>
	/// Cai. A guarda cai junto -- quem esta desmaiado nao bloqueia -- e o corpo fica um tempo
	/// desligado antes de responder de novo.
	/// </summary>
	/// <param name="porVital">
	/// Este nocaute veio de um NUCLEO que cedeu? Ver <see cref="NocautePorVital"/>.
	///
	/// **O PADRAO E `false`, e a escolha do lado importa.** Com `true` no padrao, qualquer chamada
	/// sobre um corpo INTEIRO vira no-op: o `Tick` pergunta ao corpo, o corpo responde "estou bem", e
	/// o nocaute se desfaz no quadro seguinte. Foi o que a bancada da mente mostrou -- tres provas
	/// vermelhas, todas em pontos que derrubam um corpo saudavel de proposito (o palco do filme do
	/// Bio, o verb de admin, a segunda queda do reflexo, o velorio). Com `false` no padrao a falha e
	/// pro lado seguro: o corpo fica no chao o tempo pedido, que e sempre o que quem chamou quis.
	///
	/// Passa `true` **so quem acabou de consultar <see cref="Body.DeveNocautear"/>** -- o soco, o
	/// raio, o dano em area, o sol e a sobrecarga de Ki. Esses sao os unicos lugares do port onde
	/// existe um nucleo abaixo da linha pra subir de volta.
	/// </param>
	public void Nocautear(double segundos, bool porVital = false)
	{
		F.KO = true;
		Bloqueando = false;
		ContraPronto = false;
		TempoDeGuarda = 0;
		NocauteRestante = segundos;
		NocautePorVital = porVital;
	}

	/// <summary>
	/// ============================ LEVANTAR NAO E CURAR -- `Un_KO()` (`KO.dm:126-134`) ============================
	/// O DM da **`SpreadHeal(25, 1, 1)`**: vinte e cinco pontos, **so nos VITAIS**, e so nos que
	/// estao abaixo de 70%. Braco, perna e rabo nao estao nessa lista -- **quem levanta do nocaute
	/// levanta com o braco quebrado do jeito que ele estava**, e e essa a metade do pedido do dono
	/// que o port desfazia sozinho.
	///
	/// Porque o port empurrava **toda** parte pra 1,5x o limiar (30% da vida) de graca ao fim dos
	/// 12 s. Somado ao prazo curto, o nocaute nao era so barato: era o jeito MAIS RAPIDO de curar um
	/// membro quebrado -- 12 s contra os 598 s que o original cobra por um braco inteiro.
	///
	/// O PISO SOBROU, mas so como rede e so nos NUCLEOS: se o TETO do nocaute vencer com um nucleo
	/// ainda abaixo da linha (o corpo dentro de uma estrela, que queima mais rapido do que sara), o
	/// `Tick` o derrubaria no quadro seguinte, pra sempre. Com os 25 pontos do `Un_KO` na frente
	/// isso quase nunca dispara -- um nucleo em 5% sai em 30%.
	/// =========================================================================================================
	/// </summary>
	public void Levantar()
	{
		F.KO = false;
		NocauteRestante = 0;
		NocautePorVital = false;

		Corpo.CurarVitais(CuraDeAcordar);

		// A REDE, e so ela: sem isto o teto vencido devolveria um corpo que cai de novo no proximo
		// tique. Nucleo, e nao "toda parte" -- e o nucleo que decide o nocaute.
		if (Corpo.DeveNocautear())
			foreach (BodyPart p in Corpo.Partes)
			{
				if (p.Decepado || p.Papel != Vitalidade.Nucleo) continue;
				double min = p.VidaMax * Regras.LimiarQuebra * 1.05;
				if (p.Vida < min) p.Vida = min;
			}

		SincronizarVida();
	}

	/// <summary>O `SpreadHeal(25,1,1)` de `Un_KO` (`KO.dm:133`). Vinte e cinco, e so nos vitais.</summary>
	public const double CuraDeAcordar = 25;

	/// <summary>
	/// ALGUEM PODE NEGAR ESTA MORTE. Devolve true quando negou -- e ai o corpo NAO morre.
	///
	/// ============================ POR QUE UM GANCHO E NAO UM `if` ============================
	/// Antes deste gancho havia SETE lugares chamando `Morrer()`: o resolvedor de socos, o dano em
	/// area, a Final Explosion, o Kaio-ken que estoura, a volta no tempo, o verb de admin. Um seguro
	/// contra a morte escrito em UM deles seria um seguro que funciona contra soco e nao contra
	/// Kamehameha -- e o jogador nao teria como saber por que.
	///
	/// Entao a morte passou a ter uma porta so, e quem quiser barra-la se pendura AQUI. Hoje quem
	/// se pendura e a Aura of Destruction (`GameServer.Disciplinas.TentarNegarMorte`), mas o desenho
	/// nao sabe disso: o Core continua sem conhecer disciplina divina nenhuma.
	/// ====================================================================================
	/// </summary>
	public Func<CombatState, bool>? NegarMorte;

	/// <summary>
	/// ============================ ESTE CORPO ACABOU DE MORRER -- O OUTRO LADO DO `NegarMorte` ============================
	/// O irmao do gancho acima, e ele nasceu pelo mesmo motivo, medido: **`RenasceEm = agora +
	/// MsAteRenascer` estava escrito a mao em OITO lugares** (o resolvedor de socos, a explosao do
	/// planeta, o calor da estrela, a Final Explosion, o Kaio-ken que estoura, a gestacao do
	/// bio-androide, o dano espalhado do G3, o verb de admin) -- e o `PrepararCombate` era o nono,
	/// pra quem loga ja morto.
	///
	/// Quando a morte ganhou uma SEGUNDA consequencia (a viagem pro Outro Mundo e a aureola),
	/// acrescentar a linha nova em sete dos oito lugares e literalmente o defeito que este port
	/// registra como o mais repetido dele. Entao a consequencia virou gancho: `Morrer()` ja era a
	/// porta unica da morte, e agora ela AVISA.
	///
	/// ============================ O GANCHO SO PODE ENFILEIRAR ============================
	/// `Morrer()` e chamado de dentro do `MeleeResolver`, do laco de dano em area e do tique de
	/// combate -- todos percorrendo a lista de uma zona. Um gancho que chamasse `MoveToZone` daqui
	/// mexeria nas listas de DUAS zonas no meio dessa varredura, que e o "Collection was modified"
	/// que o `_npcsPraTirar` e o `TickDeQuemVolta` ja existem pra evitar. Ver
	/// `GameServer.Alem.AMorteAconteceu`: ele escreve um relogio e mais nada.
	/// ==================================================================================
	/// </summary>
	public Action<CombatState>? AoMorrer;

	/// <summary>
	/// MORRER. Devolve **false** quando a morte foi NEGADA -- e ai nada mudou no corpo além do que
	/// quem negou tenha feito.
	///
	/// Todo chamador precisa olhar o retorno antes de fazer o que vem depois (pagar Zenkai,
	/// anunciar). Anunciar a morte de quem nao morreu e pior que nao ter o seguro: o jogador ve
	/// "voce morreu" e continua de pe.
	///
	/// O PRAZO DE RENASCER NAO E MAIS CONTA DO CHAMADOR -- ver <see cref="AoMorrer"/>.
	/// </summary>
	/// <param name="ignorarSeguro">
	/// Pula o <see cref="NegarMorte"/>. E pro verb de admin: uma ferramenta que uma mecanica de
	/// jogador consegue bloquear deixa de ser ferramenta.
	/// </param>
	public bool Morrer(bool ignorarSeguro = false)
	{
		if (F.dead) return false;
		if (!ignorarSeguro && NegarMorte?.Invoke(this) == true) return false;

		F.dead = true;
		F.KO = false;
		Bloqueando = false;
		NocauteRestante = 0;
		EmCombate = 0;
		LutandoDeVerdade = 0;

		// DEPOIS DE TUDO ESCRITO, e nao antes: quem ouve este aviso le `F.dead` pra decidir, e um
		// gancho chamado no meio veria o corpo ainda vivo.
		AoMorrer?.Invoke(this);
		return true;
	}

	public void Reviver(double fracaoDeVida = 1, double carencia = 0)
	{
		F.dead = false;
		F.KO = false;
		Carencia = carencia;
		Corpo.Restaurar();
		if (fracaoDeVida < 1)
			foreach (BodyPart p in Corpo.Partes) p.Vida = p.VidaMax * fracaoDeVida;
		SincronizarVida();
	}
}
