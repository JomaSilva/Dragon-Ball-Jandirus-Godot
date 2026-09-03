using System.Reflection;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Skills;

/// <summary>
/// UM DEGRAU: o que acontece quando a skill CHEGA neste nivel.
///
/// No DM isto e o corpo de um `if(N)` dentro do `switch(level)` do `effector()`, e o que mora la
/// e quase sempre um `assignverb` -- e a metade de baixo do sistema de habilidades. Comprar a
/// skill da o buff passivo (`after_learn`); USAR a skill ate subir de nivel e o que entrega o
/// GOLPE. Green Dean sem nivel 2 e so +0,3 de tecnica; com nivel 2 e o Dash Attack.
/// </summary>
public sealed class Degrau
{
	public int Nivel;

	/// <summary>
	/// O DEGRAU PERIODICO: `if(level % 5 == 0)` (Mind.dm:92, Effusion.dm:49). Zero = degrau exato.
	///
	/// ============================ ISTO ERA EXTRAIDO E JOGADO FORA ============================
	/// O `niveis.json` trazia 93 degraus com `periodo` preenchido, e o leitor (`RegrasDoDisco`) os
	/// descartava com um `break` -- "expandir isso em vinte degraus iguais seria inventar uma
	/// estrutura". So que o periodico E a estrutura: nas 30 maestrias de Ki o grosso do ganho mora
	/// nele (`kiawarenessskill += 1` a cada 5 niveis = +20 no nivel 100), e os degraus exatos sao
	/// so os marcos. Sem ele uma maestria subia ate 100 rendendo um quarto do que o DM da.
	///
	/// Um degrau periodico DISPARA em todo nivel multiplo de <see cref="Periodo"/> (nunca no zero:
	/// o `if(levelup)` que o envolve so acende quando o motor SOBE um nivel, skill.dm:94), e por isso
	/// <see cref="RegraDeNivel.Vezes"/> conta quantas vezes ele ja rendeu: `nivel / periodo`.
	/// ========================================================================================
	/// </summary>
	public int Periodo;

	/// <summary>Campo do lutador -> quanto somar ao cruzar este degrau. Mesmo canal aditivo do
	/// <see cref="EfeitosDeSkill"/>, so que disparado por nivel em vez de por compra.</summary>
	public Dictionary<string, double> Buffs = new(StringComparer.Ordinal);

	/// <summary>
	/// O CANAL MULTIPLICATIVO DO DEGRAU: `savant.SpiritBallCost /= 2` (Spirit.dm:321),
	/// `savant.SpiritBallDamage *= 2` (:322), `savant.MedMod *= 1.1` (gray.dm). O extrator ja os
	/// emitia em `mults`; o leitor nao tinha canal e os campos caiam em `Desconhecidos`. Compoe:
	/// dois degraus que dividem por 2 deixam o custo em um quarto.
	/// </summary>
	public Dictionary<string, double> Mults = new(StringComparer.Ordinal);

	/// <summary>
	/// O CANAL GENETICO DO DEGRAU: `savant.genome.add_to_stat("Energy Level", 0.05)` (Mind.dm:99,
	/// Effusion.dm:57). Mesma traducao do <see cref="EfeitosDeSkill"/> ("Energy Level" -> `KiMod`),
	/// feita na hora de aplicar -- 18 skills mexem na genetica por DEGRAU, e o Ki Unlocked sozinho
	/// entrega +1,0 de KiMod ate o nivel 100 (0,2 a cada 20 niveis).
	/// </summary>
	public Dictionary<string, double> Genes = new(StringComparer.Ordinal);

	/// <summary>As habilidades ativas que este degrau destrava (os `assignverb` do DM).</summary>
	public string[] Verbos = [];

	/// <summary>
	/// O VERB QUE ESTE DEGRAU CONCEDE CONFORME A CASA ESCOLHIDA NA SKILL -- o degrau 2 da Holy Trinity
	/// (Bodybuilding.dm:180-185):
	///
	///     switch(TrinityType)
	///         if("Van-sama") assignverb(/mob/keyable/verb/Taunt)
	///         if("Aniki")    assignverb(/mob/keyable/verb/Counter_Taunt)
	///         if("Ricardo")  assignverb(/mob/keyable/verb/Slap)
	///
	/// Cada par e (rotulo da casa, verb), e vale SO o par cuja casa e a que o livro registrou pra esta
	/// skill (`SkillBook.Escolhas`, resolvida por <see cref="EfeitosDeSkill.RotuloDaCasa"/>). Sem casa
	/// escolhida, nenhum -- e assim no DM: `TrinityType` nasce nulo e o `switch` nao entra em ramo nenhum.
	///
	/// ATE ESTE CANAL EXISTIR a linha ia INTEIRA pra `logica` (quarentena do extrator), os tres verbs da
	/// Trindade tinham corpo no lote G10 e PORTA NENHUMA no port, e a bancada os provava com um degrau
	/// sintetico. Quem le isto e <see cref="NiveisDeSkill.VerbosAtivos"/>.
	/// </summary>
	public (string Casa, string Verbo)[] VerbosPorCasa = [];

	/// <summary>
	/// OUTRAS SKILLS QUE ESTE DEGRAU ACENDE -- o `enableskill(/datum/skill/mind/Advanced_Ki_Awareness)`
	/// do nivel 100 (Mind.dm:186). E o `enabled = 1` de uma skill que nasceu `enabled = 0`, e o UNICO
	/// acendedor de toda Advanced_* e Perfect_* de Ki: sem este canal 55 folhas do catalogo eram "sem
	/// acendedor (mortas neste port)". Quem consome e o <see cref="SkillBook.Recalcular"/>, via
	/// <see cref="NiveisDeSkill.Destravadas"/>.
	/// </summary>
	public string[] Destrava = [];

	/// <summary>
	/// SKILLS INTEIRAS QUE ESTE DEGRAU ENTREGA: `var/datum/skill/A = new/datum/skill/sense` +
	/// `A.learn(savant, 1)` (Mind.dm:103-104). A skill entra no livro (nao e comprada) ja no nivel
	/// que o `learn` recebe. E por aqui que o Sense chega -- nivel 5 do Ki Unlocked -- e sem isto o
	/// bit `Poder.Sense` nunca acendia por skill nenhuma.
	/// </summary>
	public (string Path, int Nivel)[] Concede = [];

	/// <summary>
	/// A BARREIRA TROCADA NESTE DEGRAU: `expbarrier = 20` dentro do `if(levelup)` do nivel 1
	/// (Spirit.dm:323). Vale DALI PRA FRENTE, ate outro degrau trocar de novo. Zero = nao troca.
	/// Chegava como flag `expbarrier` e virava um `Escrever(f, "expbarrier")` num campo que o
	/// lutador nao tem -- ver <see cref="RegraDeNivel.BarreiraEm"/>, que e quem le isto.
	/// </summary>
	public double Barreira;

	/// <summary>
	/// CHAVE LIGADA, e nao valor somado: `campo = n` no DM (`savant.canPower = 1`).
	///
	/// TEM QUE SER UM CANAL SEPARADO do <see cref="Buffs"/>. Somar 1 num campo de chave funciona
	/// da primeira vez e mente na segunda -- dois degraus que ligam a mesma chave deixariam o
	/// campo em 2, e qualquer teste de igualdade a 1 passaria a falhar. Chave se ESCREVE.
	///
	/// Foi por aqui que o `canPower` -- a permissao de fazer power-up, a metade da tecla C --
	/// ficou extraida no `niveis.json` e sem ninguem pra consumir: o extrator emitia `flags`, o
	/// leitor lia so `buffs`, e nada na tela dizia que a skill nao tinha feito nada.
	/// </summary>
	public Dictionary<string, double> Flags = new(StringComparer.Ordinal);

	/// <summary>O que o jogador le quando cruza. Vazio = o servidor monta a frase generica.</summary>
	public string Aviso = "";
}

/// <summary>
/// COMO UMA SKILL SOBE DE NIVEL. E dado (vem do DM), nao estado (nao e de ninguem).
///
/// TRES COISAS: quanto custa o proximo nivel, como esse custo cresce, e por onde entra o exp.
/// </summary>
public sealed class RegraDeNivel
{
	public string Path = "";

	/// <summary>`expbarrier` do nivel 0. Padrao do DM = 100 (skill.dm:9).</summary>
	public double Barreira = NiveisDeSkill.BarreiraPadrao;

	/// <summary>
	/// ESTA SKILL E UM `/datum/skill/mind`? -- e nao "é da arvore Strength of Mind": o typepath
	/// abriga as 17 da Mente E as 33 das dez familias de pericia de Ki (Beam, Blast, Volley...).
	///
	/// E o que decide DUAS coisas, e as duas sao daquela classe do DM e de nenhuma outra: o
	/// `expbuffer` (`Mind.dm:34`, a var e do `/datum/skill/mind`) e o `KiSkillGains` (`:859`, o proc
	/// tambem). Medido no `niveis.json`: as 66 fontes de exp marcadas `curva` sao EXATAMENTE as 66
	/// deste typepath -- nao ha uma so fora.
	/// </summary>
	public bool Mental => Path.StartsWith("/datum/skill/mind/", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Quanto a barreira INCHA por nivel: `expbarrier = base * Crescimento**level`.
	///
	/// 1 = barreira fixa, que e o caso das 24 skills fisicas semeadas aqui. As arvores de Ki
	/// usam 1,03 (`expbarrier=(5000*1.03**level)`, Effusion.dm:48) -- com maxlevel 100 isso
	/// quase vintuplica o custo do ultimo nivel, e e o que faz uma arvore durar meses.
	/// </summary>
	public double Crescimento = 1;

	/// <summary>
	/// Esta skill sobe SO POR ESTAR VIVA (o `if(level &lt; maxlevel) if(prob(50)) exp++` do DM).
	///
	/// Falso NAO quer dizer "nao sobe": quer dizer que o exp vem de EVENTO, por
	/// <see cref="NiveisDeSkill.Creditar"/> -- que e o `Skill_EXP_Add` do original
	/// (mobhandlers.dm:37-40). As arvores de Ki contam golpes disparados; as fisicas contam tempo.
	/// </summary>
	public bool GanhoPorTempo;

	/// <summary>
	/// EXP POR ESTADO DO CORPO: quanto esta skill ganha por tique quando a condicao vale.
	///
	/// ============================ ISTO ESTAVA EXTRAIDO E MORTO ============================
	/// O `niveis.json` tem 146 blocos `exp`, cada um com `quanto` e `cond` -- lidos direto do DM.
	/// O leitor (`RegrasDoDisco`) abria o bloco, olhava so pra `prob`, e jogava o resto fora.
	/// Resultado: as 122 regras cuja condicao NAO e "por tempo" nunca creditaram nada.
	///
	/// A mais visivel e a raiz da arvore de Ki: `/datum/skill/mind/Ki_Unlocked` ganha
	/// `KiSkillGains(2)` MEDITANDO e `KiSkillGains(2)` VOANDO (Mind.dm:81), e 1 andando por ai.
	/// Nenhum dos tres chegava -- a skill que abre o Ki inteiro estava congelada no nivel em que
	/// tivesse sido comprada, e nada na tela dizia isso.
	///
	/// Mesma familia do `canPower` e do `expbarrier`: o extrator entregou, ninguem consumiu.
	/// =====================================================================================
	/// </summary>
	public sealed class GanhoPorEstado
	{
		/// <summary>Quanto exp por tique.</summary>
		public double Quanto;

		/// <summary>Que estado do corpo liga este ganho.</summary>
		public Estado Quando;

		/// <summary>
		/// A CORRENTE `if / else if / else` (0 = ganho solto). Ganhos da MESMA corrente sao
		/// ALTERNATIVAS em ordem: vale o primeiro cuja condicao valer, e so ele.
		///
		/// ============================ O `else` CREDITAVA POR CIMA ============================
		/// Advanced Ki Circulation e `if(kibuffon) +2 else +1` (Mind.dm:513-519). Sem a corrente, os
		/// dois sao condicoes independentes -- e o `else`, que nao tem condicao nenhuma, valeria
		/// SEMPRE: com o buff de pe a skill renderia 3 em vez de 2, meio a mais pra sempre. Sao
		/// treze `else` so na familia da Mente. Ver `FonteExp.Cadeia` no extrator.
		/// ==================================================================================
		/// </summary>
		public int Cadeia;

		/// <summary>`level &lt; N` na condicao (0 = sem teto): `if(level&lt;10 &amp;&amp; savant.med)`, Mind.dm:190.</summary>
		public int NivelMenorQue;

		/// <summary>
		/// O PISO DE NIVEL do ramo `else` de um portao de nivel (0 = sem piso). `if(level&lt;5) ... else
		/// if(kiratio&gt;1)` (Basic Ki Control, Mind.dm:290-296): o segundo ramo so existe do 5 em diante.
		/// </summary>
		public int NivelMinimo;

		/// <summary>
		/// PASSA PELO <see cref="KiSkillGains"/>? (`exp += KiSkillGains(2)` contra `exp++`.)
		///
		/// ============================ O NUMERO CRU NAO E O NUMERO DO DM ============================
		/// O extrator ja marcava `curva: 1` em 122 das 146 fontes e o leitor ignorava: creditava o
		/// `2` literal onde o DM credita `2 * 2 * Ekiskill * GlobalKiExpRate` -- e, num corpo recem
		/// nascido (`MaxKi <= 300`), um DECIMO disso. Ou seja, o ganho estava errado nos dois
		/// sentidos ao mesmo tempo, e mais errado justamente na arvore de Ki, onde toda fonte tem
		/// curva.
		/// ======================================================================================
		/// </summary>
		public bool Curva;
	}

	/// <summary>
	/// AS CONDICOES QUE ESTE PORT SABE AVALIAR.
	///
	/// O `cond` do extrator e uma EXPRESSAO do DM em texto (`savant.med`,
	/// `savant.kiratio>1&&savant.lssj>=2`, `attackcounter&lt;savant.beamcounter`...). Escrever um
	/// avaliador de expressoes de DM aqui seria construir um interpretador pra usar tres casos --
	/// e um interpretador incompleto erra CALADO, que e pior que nao ter.
	///
	/// Entao so as condicoes reconhecidas viram regra, e o carregador CONTA as que ficaram de fora
	/// (ver o aviso no `RegrasDoDisco`). O que nao entra continua nao creditando, exatamente como
	/// antes -- a diferenca e que agora isso e um numero no log e nao um silencio.
	/// </summary>
	public enum Estado
	{
		/// <summary>Sem condicao: sempre.</summary>
		Sempre,
		/// <summary>`savant.med` -- meditando.</summary>
		Meditando,
		/// <summary>`savant.flight` -- no ar.</summary>
		Voando,
		/// <summary>`savant.train` -- treinando.</summary>
		Treinando,
		/// <summary>`!savant.med&amp;&amp;!savant.flight` -- nem uma coisa nem outra.</summary>
		Ocioso,
		/// <summary>
		/// `savant.IsInFight` -- em luta: o relogio CURTO do combate (`CombatState.LutandoDeVerdade`,
		/// 10 s desde o ultimo golpe), que e o que o DM liga e desliga no `UpdateFightingStatus`. E a
		/// unica fonte de exp da Holy Trinity (Bodybuilding.dm:176: `if(savant.IsInFight) exp++`) --
		/// sem este estado ela NUNCA chegava ao nivel 2, e o verb da casa escolhida nunca vinha.
		/// </summary>
		Lutando,
		/// <summary>`savant.train || savant.IsInFight` -- treinando OU em luta (as artes marciais do corpo).</summary>
		TreinandoOuLutando,

		// ============================ AS SETE DA ARVORE DA MENTE ============================
		// Elas entraram juntas porque juntas elas sao a arvore: das 34 fontes de exp das dezessete
		// skills de "Strength of Mind", TRINTA E UMA dependiam de uma destas. Sem elas quinze das
		// dezessete tinham exatamente ZERO fonte de exp -- compraveis e congeladas no nivel 0 --, e
		// como as Advanced/Perfect so acendem no nivel 100 das Basic, DEZ delas eram inalcancaveis.
		// ==================================================================================

		/// <summary>`savant.studying` -- estudando outra pessoa (o verb `Study_Other`, KiStatsModule.dm:51).</summary>
		Estudando,

		/// <summary>`savant.observingnow` -- projetando a mente em alguem (o verb `Observe`, observe.dm:7).</summary>
		Observando,

		/// <summary>`savant.kibuffon` -- com Focus, Efficiency ou Energy Shield de pe (Ki2.0/KiBuffs.dm).</summary>
		ComBuffDeKi,

		/// <summary>
		/// `savant.kiratio > 1` -- o tanque de Ki acima do que o corpo considera cheio.
		///
		/// ESTE GANHO E ESCALADO PELA PROPRIA RAZAO: o DM escreve `exp += KiSkillGains(1*savant.kiratio)`
		/// nas tres skills de Controle (Mind.dm:296, 555, 762), e o extrator so consegue guardar o
		/// primeiro numero da expressao (o `1`). A escala mora na condicao porque ela e a MESMA nas
		/// tres ocorrencias -- e a assinatura do ganho, nao um parametro dele.
		/// </summary>
		KiAcimaDoNormal,

		/// <summary>
		/// `savant.Ki != lastki &amp;&amp; diffki &lt; 0` -- GASTOU Ki desde o tique anterior. E como as tres
		/// skills de Eficiencia treinam: nao por tempo, por consumo (Mind.dm:352-355).
		///
		/// O `lastki` do DM e `var/tmp` de cada skill, atualizado no mesmo tique nas tres -- entao um
		/// unico "caiu desde o ultimo tique" por corpo diz exatamente a mesma coisa, e sem estado
		/// repetido tres vezes. Quem guarda e o proprio <see cref="NiveisDeSkill"/>.
		/// </summary>
		GastandoKi,

		/// <summary>
		/// `(savant.Ki / savant.MaxKi) &lt; 0.9` -- o tanque abaixo de 90%. As tres skills de Reserva
		/// treinam com o tanque VAZIO, e quanto mais vazio mais rendem.
		///
		/// ESCALADO POR `2 - fracao`, que e o `KiSkillGains(3*(2-(savant.Ki/savant.MaxKi)))` do DM
		/// (Mind.dm:412, 669, 851): 1,1x com o tanque quase cheio e 2x com ele zerado.
		/// </summary>
		TanqueAbaixoDe90,

		/// <summary>
		/// `savant.deepmeditation` -- e ela NUNCA VALE, aqui nem no original.
		///
		/// ============================ UM DEFEITO DO DM, MANTIDO A VISTA ============================
		/// `deepmeditation` aparece 23 vezes no `Code/` e em NENHUMA delas alguem escreve 1: a reforma
		/// da meditacao trocou o minigame de equilibrio pela Dimensao Mental (`Meditate.dm:44-46`) e
		/// deixou so os `deepmeditation = 0`. O `else if(savant.deepmeditation)` das tres skills de
		/// Reserva e, portanto, codigo morto no jogo original.
		///
		/// Ele entra assim mesmo -- como estado que o servidor sempre responde "nao" -- porque o
		/// contrario seria escolher entre duas coisas piores: descartar a regra (e o relatorio diria
		/// "condicao que o port nao entende", mentindo sobre a causa) ou LIGAR a Dimensao Mental nela
		/// (e ai o port renderia exp que o original nao rende). Ver a bancada `--menteskills`.
		/// ======================================================================================
		/// </summary>
		MeditacaoProfunda,

		/// <summary>
		/// `else` -- o ramo final de uma corrente. Vale SEMPRE; quem o segura e a
		/// <see cref="GanhoPorEstado.Cadeia"/>, que so o deixa render se nenhum ramo anterior rendeu.
		/// </summary>
		Senao,
	}

	/// <summary>Os ganhos por estado desta skill.</summary>
	public List<GanhoPorEstado> PorEstado = [];

	/// <summary>
	/// EXP POR CONTADOR DE EVENTO: quanto esta skill ganha por unidade de contador.
	///
	/// ============================ ISTO ERA CONTADO E JOGADO FORA ============================
	/// O `niveis.json` tem 30 blocos `exp` com `contador` preenchido -- as 10 familias de pericia de
	/// Ki (beam, blast, buff, debuff, defense, guided, homing, kiai, targeted, volley) vezes os tres
	/// degraus (Basic/Advanced/Perfect). O carregador reconhecia o bloco, INCREMENTAVA um contador de
	/// diagnostico e dava `break` -- a regra nunca chegava a existir em memoria.
	///
	/// Resultado medido: essas 30 skills tem `prob: 0` e nenhuma regra por estado, entao o exp delas
	/// era exatamente ZERO pra sempre e o nivel ficava travado em 0. Elas destravam 18 verbos, e 14
	/// deles nao tem nenhuma outra rota -- Masenko, Kienzan, Hellzone Grenade, Scattershot,
	/// Shockwave, Deflection e companhia estavam implementados em C# e inalcancaveis no jogo.
	///
	/// ============================ QUEM CREDITA: O EVENTO, NAO O RELOGIO ============================
	/// No DM o `effector()` de cada uma faz MARCA-D'AGUA (`Mind.dm`, e o irmao `Effusion.dm:74-77`):
	///
	///     if(attackcounter &lt; savant.beamcounter)
	///         exp += KiSkillGains(10*(savant.beamcounter-attackcounter))
	///         attackcounter = savant.beamcounter
	///
	/// -- ou seja, o mob acumula `beamcounter` e a skill "consome" a diferenca no proximo tique.
	/// Este port credita NO EVENTO (ver <see cref="NiveisDeSkill.CreditarPorContador"/>), que da o
	/// mesmo total sem precisar guardar marca-d'agua nenhuma no save. A diferenca de comportamento
	/// esta documentada la, e ela e deliberada.
	/// =============================================================================================
	/// </summary>
	public sealed class GanhoPorContador
	{
		/// <summary>Nome do contador do DM (`beamcounter`, `blastcounter`, ...).</summary>
		public string Contador = "";

		/// <summary>O fator do `KiSkillGains(N*delta)` -- 10 pro beam, 80 pro debuff, 40 pro targeted.</summary>
		public double Quanto;
	}

	/// <summary>Os ganhos por contador desta skill.</summary>
	public List<GanhoPorContador> PorContador = [];

	public Degrau[] Degraus = [];

	/// <summary>Quanto exp falta pra sair de <paramref name="nivel"/> pro seguinte.</summary>
	public double BarreiraEm(int nivel)
	{
		// A BARREIRA TROCADA NUM DEGRAU VENCE A CURVA. `expbarrier = 20` no `if(levelup)` do nivel 1
		// (Spirit.dm:323) e uma atribuicao que fica: do nivel 1 em diante a barreira e 20, e nao os
		// 10 declarados na propriedade. O degrau mais alto ja cruzado e o que vale (o DM sobrescreve
		// a cada subida). Skill sem degrau de barreira cai na curva de sempre.
		double trocada = 0;
		int em = -1;
		foreach (Degrau d in Degraus)
			if (d.Barreira > 0 && d.Periodo == 0 && d.Nivel <= nivel && d.Nivel > em) { em = d.Nivel; trocada = d.Barreira; }
		if (em >= 0) return trocada;

		// MULTIPLICACAO REPETIDA, e nao `Math.Pow`. Cliente e servidor precisam chegar no MESMO
		// numero, e `Math.Pow` nao tem resultado identico garantido entre implementacoes de
		// libm -- o `GeradorDeTerreno` deste mesmo lote se proibe disso pelo mesmo motivo, e
		// duas regras opostas no mesmo commit e a semente de uma divergencia sutil.
		//
		// O teto e `maxlevel` (100 no pior caso), entao o laco custa nada.
		double b = Barreira;
		for (int i = 0; i < nivel && i < 1000; i++) b *= Crescimento;
		return b;
	}

	/// <summary>O degrau EXATO deste nivel (o que carrega o aviso). Periodico nao entra aqui.</summary>
	public Degrau? DegrauDe(int nivel)
	{
		foreach (Degrau d in Degraus) if (d.Periodo == 0 && d.Nivel == nivel) return d;
		return null;
	}

	/// <summary>
	/// TODOS OS DEGRAUS QUE DISPARAM AO CHEGAR EM <paramref name="nivel"/>: o exato, e cada
	/// periodico de que o nivel e multiplo. No nivel 100 do Ki Unlocked sao QUATRO de uma vez
	/// (`==100`, `%5`, `%10`, `%20`, Mind.dm:92-125) -- e e assim no DM, porque os `if` sao
	/// irmaos, nao alternativas.
	/// </summary>
	public IEnumerable<Degrau> DegrausEm(int nivel)
	{
		foreach (Degrau d in Degraus)
			if (Vezes(d, nivel) > 0 && (d.Periodo > 0 ? nivel % d.Periodo == 0 : d.Nivel == nivel))
				yield return d;
	}

	/// <summary>
	/// QUANTAS VEZES este degrau ja rendeu pra quem esta no <paramref name="nivel"/>: o exato rende
	/// uma vez a partir do proprio nivel; o periodico rende `nivel / periodo` vezes (nivel 35 com
	/// periodo 5 = 7 vezes). E esta conta que faz o `Aplicar` ser idempotente com periodico dentro.
	/// </summary>
	public static int Vezes(Degrau d, int nivel)
	{
		if (d.Periodo > 0) return nivel <= 0 ? 0 : nivel / d.Periodo;
		return nivel >= d.Nivel ? 1 : 0;
	}
}

/// <summary>O estado de nivel de um personagem, no formato que vai pro disco.</summary>
public sealed class NivelSave
{
	/// <summary>typepath -> [nivel, exp].</summary>
	public Dictionary<string, double[]> Skills = [];

	/// <summary>
	/// O QUE OS DEGRAUS JA SOMARAM NO CORPO (campo -> quanto). E o razao da idempotencia, o
	/// mesmo papel de <c>Fighter.BuffsDeSkill</c>.
	///
	/// PRECISA ir pro disco, e nao ser recalculado no login a partir dos niveis: o
	/// <see cref="RegrasDeNivel"/> vai CRESCER (hoje ele cobre 24 skills, o DM tem 200+). Se o
	/// razao fosse deduzido do nivel na hora de entrar, um degrau portado DEPOIS do save do
	/// jogador nasceria ja marcado como "aplicado" -- e o buff novo nunca chegaria no corpo de
	/// quem ja tinha o nivel. O bug seria invisivel: a ficha existe, o nivel existe, o numero
	/// nao aparece.
	/// </summary>
	public Dictionary<string, double> Somados = [];

	/// <summary>
	/// O QUE OS DEGRAUS JA MULTIPLICARAM (campo -> fator). O irmao multiplicativo do
	/// <see cref="Somados"/>, pelo mesmo motivo e com o mesmo destino: desfaz-se por DIVISAO no
	/// login, e nao por subtracao. Save antigo chega sem isto (= fator 1 em tudo), que e a
	/// resposta certa: nenhum degrau multiplicativo tinha sido aplicado ate hoje.
	/// </summary>
	public Dictionary<string, double> Multiplicados = [];
}

/// <summary>
/// O NIVEL DAS SKILLS -- o motor que faltava pro <see cref="SkillBook"/>.
///
/// O livro sabia SE voce aprendeu. O DM sabe QUANTO: cada `/datum/skill` carrega `level`, `exp`,
/// `expbarrier` e `maxlevel` (skill.dm:7-10), e o `effector()` (skill.dm:86-95) e um lacinho de
/// quatro linhas que roda pra sempre enquanto a skill tem dono:
///
///     level = min(level, maxlevel)
///     exp   = max(exp, 0)
///     if(exp >= expbarrier &amp;&amp; level &lt; maxlevel) { exp = 0; level += 1; levelup = 1 }
///
/// POR QUE ISSO IMPORTA MAIS DO QUE PARECE: das 24 skills fisicas semeadas aqui, NENHUMA entrega
/// o golpe na compra -- o `assignverb` mora dentro do `switch(level)`, no degrau. O catalogo
/// extraido confirma: `/datum/skill/MartialSkill/Green_Dean` sai do extrator com
/// `"verbos": []`, porque o Dash Attack nao esta no `after_learn()`, esta no efetor. Sem este
/// arquivo, comprar a arvore de Martial Skill inteira nao da UM golpe a ninguem.
///
/// A CADENCIA E O PRECO. Ver <see cref="SegundosPorTique"/>: quem chama isto na cadencia errada
/// nao quebra nada visivelmente, so muda o jogo de escala -- uma skill que devia levar 40
/// segundos passa a levar 4 minutos, e isso ninguem reporta como bug.
/// </summary>
public sealed class NiveisDeSkill
{
	/// <summary>`expbarrier = 100` e o valor de fabrica do `/datum/skill` (skill.dm:9).</summary>
	public const double BarreiraPadrao = 100;

	/// <summary>
	/// A CADENCIA DO EFETOR, EM SEGUNDOS. O `learn()` do DM larga um laco eterno por skill --
	/// `spawn while(savant) { effector(); sleep(2) }` (skill.dm:39-42), repetido no `login()`
	/// (skill.dm:50-52). `sleep(2)` no BYOND sao 2 decimos: 0,2 s.
	///
	/// NAO ACHEI NENHUM "Unitimer" no codigo -- a unica ocorrencia da palavra em toda a arvore e
	/// um COMENTARIO (speedy.dm:94, "The process' loop, handled in Unitimer"), sobra de um
	/// agendador central que nao existe mais. O laco por skill do skill.dm E o Unitimer hoje.
	///
	/// (A UNIDADE ESTA EXPLICADA E PROVADA EM <see cref="TempoDoDm"/> -- este arquivo foi um dos
	/// poucos que sempre a leu certo, e o resto do port dividia por `world.fps`.)
	///
	/// UMA RESSALVA HONESTA: o mundo roda a 12 fps (World.dm:5), entao um tick vale 0,833
	/// decimos e um `sleep(2)` cai no primeiro tick DEPOIS de 0,2 s -- 0,25 s de verdade. Os
	/// 0,2 s daqui sao a leitura do que o codigo PEDE; a diferenca e artefato do relogio do
	/// BYOND, que este port nao tem. Em numero fechado: com prob(50) isso da 2,5 exp/s, e uma
	/// barreira de 100 vira ~40 s por nivel (no BYOND real, ~50 s).
	/// </summary>
	public const double SegundosPorTique = 0.2;

	/// <summary>`prob(50)` -- a chance de ganhar 1 de exp em cada tique (Body.dm:180 e 23 irmas).</summary>
	public const double ChanceDeGanho = 0.5;

	private readonly Dictionary<string, Progresso> _p = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>O que os degraus ja somaram no corpo. Ver <see cref="NivelSave.Somados"/>.</summary>
	private Dictionary<string, double> _somados = new(StringComparer.Ordinal);

	/// <summary>O que os degraus ja multiplicaram. Ver <see cref="NivelSave.Multiplicados"/>.</summary>
	private Dictionary<string, double> _multiplicados = new(StringComparer.Ordinal);

	/// <summary>
	/// O Ki do tique anterior -- o `var/tmp/lastki` das skills de Eficiencia (Mind.dm:338).
	/// `tmp` no DM e "morre com a sessao", e aqui tambem: nao vai pro save.
	/// </summary>
	private double _ultimoKi;

	public struct Progresso
	{
		public int Nivel;
		public double Exp;

		/// <summary>
		/// O `expbuffer` DA SKILL (`/datum/skill/mind/var/expbuffer`, Mind.dm:34) -- um banco de exp
		/// ADIANTADO que multiplica os ganhos seguintes em vez de virar exp de uma vez.
		///
		/// ============================ ELE ERA "NAO TEM QUEM ENCHA" E AGORA TEM ============================
		/// O <see cref="KiSkillGains"/> deste port dizia, por escrito, que o buffer ficava de fora
		/// *"porque nao ha campo nem quem o encha"*. Quem enche sao DUAS coisas, e as duas sao da
		/// arvore da Mente: o efetor base de `/datum/skill/mind` (`prob(10)` por tique enquanto o dono
		/// esta lutando, meditando ou treinando, Mind.dm:44-47) e o verb `Study_Other` -- estudar
		/// alguem mais adiantado que voce naquela skill (`KiStatsModule.dm:70-78`), que deposita ate
		/// DEZ VEZES mais quando ha uma skill em foco.
		///
		/// Sem ele o `Study_Other` nao teria onde entregar o que aprendeu, e a diferenca de taxa entre
		/// treinar sozinho e treinar olhando um mestre simplesmente nao existiria.
		/// ============================================================================================
		/// </summary>
		public double Buffer;
	}

	/// <summary>
	/// Uma subida de nivel acontecida NESTE tique. E o `levelup=1` do DM, ja consumido.
	/// <paramref name="Degrau"/> e o degrau EXATO (quem carrega o aviso); <paramref name="Degraus"/>
	/// sao TODOS os que dispararam, periodicos inclusive -- e por eles que o servidor concede skill
	/// e avisa o que acendeu.
	/// </summary>
	public readonly record struct Subida(string Path, string Nome, int Nivel, Degrau? Degrau, Degrau[]? Degraus = null);

	public int Nivel(string path) => _p.TryGetValue(path, out Progresso pr) ? pr.Nivel : 0;
	public double Exp(string path) => _p.TryGetValue(path, out Progresso pr) ? pr.Exp : 0;
	public IEnumerable<(string Path, int Nivel, double Exp)> Todas =>
		_p.Select(kv => (kv.Key, kv.Value.Nivel, kv.Value.Exp));

	/// <summary>
	/// Quanto falta pro proximo nivel desta skill: (exp atual, barreira). (0,0) = nao progride.
	/// E o que a aba "Check Skill Stats" do original mostrava (skill.dm:127-128).
	/// </summary>
	public (double Exp, double Barreira) Andamento(SkillCatalog cat, string path)
	{
		RegraDeNivel? r = RegrasDeNivel.Get(path);
		if (r == null) return (0, 0);
		int n = Nivel(path);
		if (n >= MaxNivel(cat, path)) return (0, 0);
		return (Exp(path), r.BarreiraEm(n));
	}

	private static int MaxNivel(SkillCatalog cat, string path) => Math.Max(1, cat.Get(path)?.MaxNivel ?? 1);

	// =====================================================================
	// O EFETOR
	// =====================================================================
	/// <summary>
	/// UM TIQUE DO EFETOR pra todas as skills deste personagem. CHAME A CADA
	/// <see cref="SegundosPorTique"/> SEGUNDOS -- ver o comentario da constante.
	///
	/// Faz, na ordem do DM: acerta a lista com o livro (skill esquecida perde o nivel), paga o
	/// ganho por tempo, e roda o `effector()` base (skill.dm:86-95). Devolve as subidas do tique
	/// -- lista vazia (o caso normal) nao custa alocacao.
	/// </summary>
	/// <summary>
	/// O QUE O CORPO ESTA FAZENDO neste tique -- o que decide quais ganhos por estado valem.
	///
	/// Um struct e nao tres parametros porque a lista vai crescer (o DM tem `currush`, `kiratio`,
	/// contadores de golpe...), e trocar a assinatura de um metodo chamado de tres lugares toda vez
	/// que uma condicao nova entra e como se perde chamada pelo caminho.
	/// </summary>
	/// <param name="Estudando">`savant.studying` -- o laco do verb `Study_Other` esta de pe.</param>
	/// <param name="Observando">`savant.observingnow` -- o verb `Observe` esta projetando a mente.</param>
	/// <param name="ComBuffDeKi">`savant.kibuffon` -- Focus, Efficiency ou Energy Shield ligados.</param>
	/// <param name="MeditacaoProfunda">
	/// `savant.deepmeditation`. **Sempre falso**, e nao por esquecimento: ver
	/// <see cref="RegraDeNivel.Estado.MeditacaoProfunda"/>.
	/// </param>
	/// <param name="Ficha">
	/// A FICHA, pros ganhos que sao uma CONTA e nao uma chave: `kiratio`, `Ki/MaxKi` e a propria
	/// curva do <see cref="KiSkillGains"/> (que le `MaxKi` e `Ekiskill`).
	///
	/// Opcional porque as bancadas de mesa exercitam o motor sem corpo nenhum; sem ficha esses
	/// ganhos simplesmente nao valem (e a curva nao se aplica), que e o lado seguro.
	/// </param>
	public readonly record struct EstadoDoCorpo(bool Meditando, bool Voando, bool Treinando, bool Lutando = false,
											    bool Estudando = false, bool Observando = false,
											    bool ComBuffDeKi = false, bool MeditacaoProfunda = false,
											    Fighter? Ficha = null);

	public List<Subida> Efetor(Random rng, SkillCatalog cat, SkillBook livro,
							   EstadoDoCorpo corpo = default)
	{
		Sincronizar(livro);
		List<Subida>? subidas = null;

		// ============================ "GASTOU KI DESDE O ULTIMO TIQUE" ============================
		// O `var/tmp/lastki` das tres skills de Eficiencia (Mind.dm:338-339), medido UMA vez por
		// tique em vez de tres: as tres leem e escrevem no mesmo instante, entao o resultado e o
		// mesmo. `tmp` no DM quer dizer "nao vai pro save", e este campo tambem nao vai.
		bool gastouKi = false;
		if (corpo.Ficha is { } fichaDoTique)
		{
			gastouKi = fichaDoTique.Ki < _ultimoKi;
			_ultimoKi = fichaDoTique.Ki;
		}

		foreach (string path in _p.Keys.ToList())
		{
			RegraDeNivel r = RegrasDeNivel.Get(path)!;
			int max = MaxNivel(cat, path);
			Progresso pr = _p[path];

			// skill.dm:90-91 -- as duas travas vem ANTES da subida, e nesta ordem. Sem o
			// clamp de nivel, baixar o maxlevel numa atualizacao deixaria gente acima do teto.
			if (pr.Nivel > max) pr.Nivel = max;
			if (pr.Exp < 0) pr.Exp = 0;

			// `if(level < maxlevel) if(prob(50)) exp++` -- so quem ainda tem pra onde subir
			// acumula. Deixar o exp correr no teto faria a skill "estourar" no dia em que o
			// maxlevel subisse.
			if (r.GanhoPorTempo && pr.Nivel < max && rng.NextDouble() < ChanceDeGanho) pr.Exp++;

			// GANHO POR ESTADO DO CORPO -- o que estava extraido e nunca creditado. Ver
			// `RegraDeNivel.PorEstado`. Mesma trava do ganho por tempo: quem ja esta no teto nao
			// acumula, senao a skill "estouraria" no dia em que o maxlevel subisse.
			//
			// ============================ A CORRENTE `if / else` ============================
			// Ganhos da MESMA <see cref="RegraDeNivel.GanhoPorEstado.Cadeia"/> sao ALTERNATIVAS: vale
			// o primeiro que casar. Eles chegam CONTIGUOS (a ordem do JSON e a ordem do DM, e um
			// `if/else if/else` sao linhas seguidas), entao basta lembrar da corrente ANTERIOR --
			// nao ha conjunto pra alocar 5 vezes por segundo por skill por jogador.
			// ==============================================================================
			if (pr.Nivel < max)
			{
				int cadeiaAtual = 0;
				bool jaRendeu = false;
				foreach (RegraDeNivel.GanhoPorEstado g in r.PorEstado)
				{
					if (g.Cadeia != cadeiaAtual) { cadeiaAtual = g.Cadeia; jaRendeu = false; }
					if (g.Cadeia != 0 && jaRendeu) continue;

					// os portoes de NIVEL: `if(level<10 && ...)` e o piso do `else` de um deles
					if (g.NivelMenorQue > 0 && pr.Nivel >= g.NivelMenorQue) continue;
					if (pr.Nivel < g.NivelMinimo) continue;

					if (Escala(g.Quando, corpo, gastouKi) is not { } escala) continue;
					double ganho = g.Quanto * escala;
					pr.Exp += g.Curva ? GastarDoBuffer(ref pr, KiSkillGains(ganho, corpo.Ficha)) : ganho;
					if (g.Cadeia != 0) jaRendeu = true;
				}
			}

			// ============================ O EFETOR BASE DE `/datum/skill/mind` ============================
			// `if(savant && (IsInFight || med || train) && prob(10) && !afk) if(expbuffer <= 100*rate) expbuffer++`
			// (Mind.dm:44-47) -- a skill da Mente guarda um pouquinho de exp adiantado so por o dono
			// estar FAZENDO alguma coisa. E ele que o `Study_Other` enche aos montes.
			//
			// `!savant.afk` NAO TEM PAR AQUI: este port nao tem detector de ausencia (o DM marca `afk`
			// por tempo sem input). Quem esta parado tambem nao esta lutando, meditando nem treinando,
			// entao a guarda que sobra ja e a que importa.
			if (r.Mental && (corpo.Lutando || corpo.Meditando || corpo.Treinando)
				&& rng.NextDouble() < 0.10 && pr.Buffer <= 100 * GainKnobs.GlobalKiExpRate)
				pr.Buffer++;

			// skill.dm:92-95 -- a subida em si
			double barreira = r.BarreiraEm(pr.Nivel);
			if (pr.Exp >= barreira && pr.Nivel < max)
			{
				pr.Exp = 0;
				pr.Nivel++;
				// `if(levelup) expbuffer = 0` (Mind.dm:48-49): o banco NAO atravessa a subida de
				// nivel. E o freio do estudo -- ele adianta UM degrau, nao a arvore inteira.
				pr.Buffer = 0;
				(subidas ??= []).Add(new Subida(path, cat.Get(path)?.Nome ?? path, pr.Nivel,
												r.DegrauDe(pr.Nivel), [.. r.DegrausEm(pr.Nivel)]));
			}

			_p[path] = pr;
		}

		return subidas ?? [];
	}

	/// <summary>
	/// GASTA O BANCO DE EXP ADIANTADO nesta skill e devolve o ganho JA MULTIPLICADO -- o segundo
	/// bloco do `KiSkillGains` do DM (Mind.dm:864-870):
	///
	///     if(expbuffer)
	///         if(expbuffer-gain > 0)
	///             expbuffer -= gain
	///             gain *= sqrt(max(4, (expbuffer/gain)*4))
	///         else
	///             gain += expbuffer
	///             expbuffer = 0
	///
	/// LEIA COM CUIDADO: quando o banco cobre o ganho, ele e DEBITADO e o que multiplica e o SALDO
	/// QUE SOBROU -- no minimo 2x (`sqrt(4)`), e mais quanto maior o banco. Quando nao cobre, o
	/// resto e simplesmente somado. O banco nao vira exp: ele acelera.
	/// </summary>
	private static double GastarDoBuffer(ref Progresso pr, double ganho)
	{
		if (pr.Buffer <= 0 || ganho <= 0) return ganho;
		if (pr.Buffer - ganho > 0)
		{
			pr.Buffer -= ganho;
			return ganho * Math.Sqrt(Math.Max(4, pr.Buffer / ganho * 4));
		}
		ganho += pr.Buffer;
		pr.Buffer = 0;
		return ganho;
	}

	/// <summary>
	/// DEPOSITA no banco de exp adiantado desta skill -- e o que o `Study_Other` faz
	/// (`KiStatsModule.dm:74-78`). Devolve false pra skill que a pessoa nao tem.
	/// </summary>
	public bool Depositar(string path, double quanto)
	{
		if (quanto <= 0 || !_p.TryGetValue(path, out Progresso pr)) return false;
		pr.Buffer += quanto;
		_p[path] = pr;
		return true;
	}

	/// <summary>Quanto de exp adiantado esta guardado nesta skill. Diagnostico e bancada.</summary>
	public double Buffer(string path) => _p.TryGetValue(path, out Progresso pr) ? pr.Buffer : 0;

	/// <summary>
	/// CREDITA EXP JA PASSANDO PELA CURVA E PELO BANCO desta skill -- o `S.exp += S.KiSkillGains(exp)`
	/// do `Study_Book` (`KiStatsModule.dm:198`). Devolve quanto entrou de verdade (zero pra quem nao
	/// tem a skill), que e o numero que o livro anuncia ao leitor.
	/// </summary>
	public double CreditarComCurva(string path, double bruto, Fighter? f)
	{
		if (bruto <= 0 || !_p.TryGetValue(path, out Progresso pr)) return 0;
		double ganho = GastarDoBuffer(ref pr, KiSkillGains(bruto, f));
		_p[path] = pr;
		return Creditar(path, ganho) ? ganho : 0;
	}

	/// <summary>
	/// A CONDICAO VALE? E, quando vale, POR QUANTO o ganho e multiplicado.
	///
	/// Devolve nulo quando nao vale -- e nao zero: zero seria um ganho de valor zero, e um ganho de
	/// valor zero SATISFAZ a corrente (o `else` seguinte nunca renderia). Nulo diz "este ramo nao
	/// e o meu", que e o que o `if` do DM diz.
	///
	/// A ESCALA E DA CONDICAO e nao do numero porque e assim que ela aparece no DM: as tres skills
	/// de Controle escrevem `KiSkillGains(1*savant.kiratio)` e as tres de Reserva escrevem
	/// `KiSkillGains(3*(2-(savant.Ki/savant.MaxKi)))`. O extrator so guarda o primeiro numero da
	/// expressao (`1` e `3`); o resto e a assinatura do estado, e mora aqui.
	/// </summary>
	private static double? Escala(RegraDeNivel.Estado quando, EstadoDoCorpo corpo, bool gastouKi)
	{
		switch (quando)
		{
			case RegraDeNivel.Estado.Sempre: return 1;
			case RegraDeNivel.Estado.Senao: return 1;   // quem segura o `else` e a cadeia
			case RegraDeNivel.Estado.Meditando: return corpo.Meditando ? 1 : null;
			case RegraDeNivel.Estado.Voando: return corpo.Voando ? 1 : null;
			case RegraDeNivel.Estado.Treinando: return corpo.Treinando ? 1 : null;
			case RegraDeNivel.Estado.Ocioso: return !corpo.Meditando && !corpo.Voando ? 1 : null;
			case RegraDeNivel.Estado.Lutando: return corpo.Lutando ? 1 : null;
			case RegraDeNivel.Estado.TreinandoOuLutando: return corpo.Treinando || corpo.Lutando ? 1 : null;
			case RegraDeNivel.Estado.Estudando: return corpo.Estudando ? 1 : null;
			case RegraDeNivel.Estado.Observando: return corpo.Observando ? 1 : null;
			case RegraDeNivel.Estado.ComBuffDeKi: return corpo.ComBuffDeKi ? 1 : null;
			case RegraDeNivel.Estado.MeditacaoProfunda: return corpo.MeditacaoProfunda ? 1 : null;
			case RegraDeNivel.Estado.GastandoKi: return gastouKi ? 1 : null;

			case RegraDeNivel.Estado.KiAcimaDoNormal:
				return corpo.Ficha is { kiratio: > 1 } f ? f.kiratio : null;

			case RegraDeNivel.Estado.TanqueAbaixoDe90:
			{
				if (corpo.Ficha is not { } t || t.MaxKi <= 0) return null;
				double fracao = t.Ki / t.MaxKi;
				return fracao < 0.9 ? 2 - fracao : null;
			}

			default: return null;
		}
	}

	/// <summary>
	/// EXP POR EVENTO. E o `mob/proc/Skill_EXP_Add(stype, n)` do original (mobhandlers.dm:37-40),
	/// e e por aqui que as arvores de Ki sobem: elas nao contam tempo, contam golpe disparado
	/// (`exp += KiSkillGains(10*(effusioncounter-attackcounter))`, Effusion.dm:76).
	///
	/// So credita quem tem regra e ja foi aprendida -- creditar numa skill que a pessoa nao tem
	/// criaria progresso fantasma que aparece do nada no dia em que ela comprar.
	/// </summary>
	public bool Creditar(string path, double quanto)
	{
		if (quanto <= 0 || !_p.TryGetValue(path, out Progresso pr)) return false;
		pr.Exp += quanto;
		_p[path] = pr;
		return true;
	}

	/// <summary>
	/// O `KiSkillGains(exp)` do original (`Mind.dm:859-871`) -- a conversao de "unidades de contador"
	/// em exp de arvore de Ki.
	///
	///     if(savant.MaxKi &lt;= 300) exp *= savant.MaxKi/3000
	///     gain = exp * 2 * savant.Ekiskill * GlobalKiExpRate
	///
	/// A PENALIDADE DE TANQUE PEQUENO E 1:1 e nao arredondamento: com `MaxKi` 300 o exp sai a um
	/// decimo, e ela existe pra impedir que um personagem recem-criado suba as pericias de Ki na
	/// mesma velocidade de quem ja abriu o tanque.
	///
	/// O `expbuffer` ENTRA, MAS NAO AQUI. Esta funcao e estatica e nao sabe de QUAL skill e o ganho;
	/// o banco e por skill (`Progresso.Buffer`) e quem o gasta e o <see cref="GastarDoBuffer"/>, que
	/// envolve toda chamada desta. A separacao e de proposito: assim a curva continua sendo uma
	/// funcao pura, testavel com um numero e uma ficha.
	///
	/// (Ate o lote da Mente o banco simplesmente nao existia -- "nao ha campo nem quem o encha".
	/// Quem enche e o efetor base de `/datum/skill/mind` e o verb `Study_Other`, e os dois entraram
	/// com este lote. Ver `Progresso.Buffer`.)
	/// </summary>
	public static double KiSkillGains(double exp, Fighter? f)
	{
		// SEM FICHA A CURVA NAO SE APLICA (bancada de mesa, cliente): devolve o numero cru. Chutar
		// um `Ekiskill` medio aqui daria um ganho que nao e o de ninguem.
		if (f == null) return exp;
		if (f.MaxKi <= 300) exp *= f.MaxKi / 3000.0;
		return exp * 2 * f.Ekiskill * GainKnobs.GlobalKiExpRate;
	}

	/// <summary>
	/// EXP DAS 10 FAMILIAS DE PERICIA DE KI -- a porta dos 30 contadores que estavam extraidos e
	/// mortos. Credita todas as skills (Basic/Advanced/Perfect) que observam
	/// <paramref name="contador"/>, e devolve quantas foram creditadas.
	///
	/// <paramref name="vezes"/> e o DELTA DO CONTADOR DO DM, nao o numero de golpes: um segmento de
	/// raio e `beamcounter += 3`, entao vale 3; um blast solto e `blastcounter++`, entao vale 1.
	///
	/// ============ POR QUE ISTO CREDITA NO EVENTO E NAO POR MARCA-D'AGUA ============
	/// No DM cada uma das 30 guarda o proprio `attackcounter` e, no `effector()` (5 Hz), consome a
	/// diferenca pro contador do mob:
	///
	///     if(attackcounter &lt; savant.beamcounter)
	///         exp += KiSkillGains(10*(savant.beamcounter-attackcounter))
	///         attackcounter = savant.beamcounter
	///
	/// O TOTAL E O MESMO nos dois desenhos -- creditar 3 agora ou creditar 3 no proximo tique da o
	/// mesmo exp. O que muda sao duas coisas, e as duas pesam a favor do evento:
	///
	///   1) A MARCA-D'AGUA PRECISARIA IR PRO SAVE. Sem isso, relogar zera o `attackcounter` da skill
	///      enquanto o contador do mob volta cheio, e o primeiro tique depois do login credita a
	///      vida inteira de golpes de uma vez. Creditar no evento nao tem estado nenhum pra salvar.
	///   2) O DM CREDITA HISTORICO RETROATIVO. Quem compra `Basic_Beam_Mastery` hoje comeca com
	///      `attackcounter = 0` e o `beamcounter` do mob ja grande, entao o primeiro tique entrega
	///      todo o passado de uma vez. Este port ja decidiu contra isso: o `Creditar` recusa skill
	///      nao aprendida justamente pra nao criar "progresso fantasma que aparece do nada no dia em
	///      que ela comprar". Creditar no evento e a mesma decisao, aplicada.
	///
	/// DIVERGENCIA DECLARADA, entao: aqui so conta o que se dispara DEPOIS de ter a skill.
	/// ==============================================================================
	///
	/// CUSTO: uma busca em dicionario por chamada, e no maximo 3 skills por contador (os tres
	/// degraus da familia). Chamado por golpe, e nao por tique -- ver `GameServer.CreditarContador`.
	/// </summary>
	public int CreditarPorContador(string contador, double vezes, Fighter f)
	{
		if (vezes <= 0 || contador.Length == 0) return 0;

		int n = 0;
		foreach (RegrasDeNivel.CreditoDeContador c in RegrasDeNivel.DoContador(contador))
		{
			// o `10*(savant.beamcounter-attackcounter)` de dentro do `KiSkillGains` -- e o banco de
			// exp adiantado tambem vale aqui: as 30 pericias de Ki sao `/datum/skill/mind` como as
			// da Mente, e la o `KiSkillGains` e um proc so pras duas familias. Ver `Progresso.Buffer`.
			if (!_p.TryGetValue(c.Path, out Progresso pr)) continue;
			double ganho = GastarDoBuffer(ref pr, KiSkillGains(c.Quanto * vezes, f));
			_p[c.Path] = pr;
			if (Creditar(c.Path, ganho)) n++;
		}
		return n;
	}

	/// <summary>
	/// Poe uma skill num nivel na marra. E o `learn(trainee, baselevel)` do DM (skill.dm:34-36):
	/// quem foi ENSINADO as vezes ja nasce no nivel 1 (Mind.dm:104 passa baselevel=1).
	/// </summary>
	public void Por(string path, int nivel, double exp = 0)
	{
		if (RegrasDeNivel.Get(path) == null) return;
		_p[path] = new Progresso { Nivel = Math.Max(0, nivel), Exp = Math.Max(0, exp) };
	}

	/// <summary>
	/// Acerta a lista com o livro: skill nova entra no nivel 0, skill esquecida sai.
	///
	/// SAIR IMPORTA. O `forget()` do DM deleta o datum inteiro (skill.dm:78), e junto vai o
	/// nivel. Guardar o nivel de uma skill devolvida seria um jeito barato de reaprender no topo.
	/// </summary>
	private void Sincronizar(SkillBook livro)
	{
		foreach (string path in livro.Aprendidas)
			if (!_p.ContainsKey(path) && RegrasDeNivel.Get(path) != null)
				_p[path] = default;

		foreach (string path in _p.Keys.ToList())
			if (!livro.Sabe(path)) _p.Remove(path);
	}

	// =====================================================================
	// OS EFEITOS DE DEGRAU
	// =====================================================================
	/// <summary>
	/// As habilidades ativas que os niveis ATUAIS destravam.
	///
	/// E o `login()` do DM virado do avesso. La, cada skill reatribui os proprios verbs na
	/// entrada (`if(level >= 2) assignverb(...)`, Martial Skill.dm:49) porque o verb some com a
	/// sessao. Aqui nao existe "verb do mob": existe a pergunta "voce pode usar isto?", e a
	/// resposta e recalculada do nivel toda vez. Nada pra reatribuir, nada pra esquecer.
	/// </summary>
	/// <param name="casaDe">
	/// A CASA ESCOLHIDA numa skill de escolha unica (typepath -> rotulo, nulo = sem escolha) -- o que o
	/// <see cref="Degrau.VerbosPorCasa"/> pergunta. Quem tem o livro passa
	/// `path => EfeitosDeSkill.RotuloDaCasa(cat, livro.Escolhas, path)`; sem resolvedor, nenhum verb por
	/// casa vale (o que e o certo pra quem nao escolheu nada).
	/// </param>
	public IEnumerable<string> VerbosAtivos(Func<string, string?>? casaDe = null)
	{
		foreach ((string path, Progresso pr) in _p)
		{
			RegraDeNivel? r = RegrasDeNivel.Get(path);
			if (r == null) continue;
			string? casa = null;
			bool casaLida = false;
			foreach (Degrau d in r.Degraus)
			{
				if (RegraDeNivel.Vezes(d, pr.Nivel) <= 0) continue;
				foreach (string v in d.Verbos) yield return v;

				// O VERB POR CASA: so o par da casa que o livro registrou -- ver `Degrau.VerbosPorCasa`.
				// A casa e lida UMA vez por skill (e so quando algum degrau cruzado a pede).
				if (d.VerbosPorCasa.Length == 0) continue;
				if (!casaLida) { casa = casaDe?.Invoke(path); casaLida = true; }
				if (casa == null) continue;
				foreach ((string c, string v) in d.VerbosPorCasa)
					if (string.Equals(c, casa, StringComparison.OrdinalIgnoreCase)) yield return v;
			}
		}
	}

	/// <summary>
	/// AS SKILLS QUE OS NIVEIS ATUAIS ACENDERAM -- o `enableskill()` disparado por degrau
	/// (Mind.dm:186: `if(level == 100) enableskill(/datum/skill/mind/Advanced_Ki_Awareness)`).
	///
	/// E a mesma pergunta do <see cref="VerbosAtivos"/> ("o que os niveis de agora ja deram?"),
	/// recalculada do nivel toda vez em vez de guardada: quem chegou ao 100 offline, ou por
	/// concessao de admin, acende igual. O <see cref="SkillBook.Recalcular"/> le isto pelo
	/// <see cref="ContextoDeRegra.DestravadasPorDegrau"/> e poe o alvo em `Acesas` da arvore que o
	/// pendura -- e o veredito da loja passa de "sem acendedor" a "pode".
	/// </summary>
	public IEnumerable<string> Destravadas()
	{
		foreach ((string path, Progresso pr) in _p)
		{
			RegraDeNivel? r = RegrasDeNivel.Get(path);
			if (r == null) continue;
			foreach (Degrau d in r.Degraus)
				if (RegraDeNivel.Vezes(d, pr.Nivel) > 0)
					foreach (string alvo in d.Destrava) yield return alvo;
		}
	}

	/// <summary>
	/// AS SKILLS QUE UM DEGRAU JA CRUZADO ENTREGA E O LIVRO AINDA NAO TEM -- (o que conceder, em que
	/// nivel). E o `A.learn(savant, 1)` do Mind.dm:104, lido do nivel e nao do momento: um save que
	/// passou do nivel 5 antes de este canal existir recebe o Sense no proximo login, em vez de
	/// nunca. Quem concede e o servidor (ele e quem tem o livro E os poderes que dependem dele).
	/// </summary>
	public IEnumerable<(string Path, int Nivel)> ConcessoesPendentes(SkillBook livro)
	{
		foreach ((string path, Progresso pr) in _p)
		{
			RegraDeNivel? r = RegrasDeNivel.Get(path);
			if (r == null) continue;
			foreach (Degrau d in r.Degraus)
				if (RegraDeNivel.Vezes(d, pr.Nivel) > 0)
					foreach ((string alvo, int nivel) in d.Concede)
						if (!livro.Sabe(alvo)) yield return (alvo, nivel);
		}
	}

	/// <summary>
	/// POE NO CORPO os buffs de todos os degraus ja cruzados -- nem mais, nem de novo.
	///
	/// MESMO PADRAO DO <see cref="EfeitosDeSkill"/>, e de proposito: totaliza o que DEVERIA estar
	/// aplicado, desconta o que ja estava, mexe so na diferenca. Chamar duas vezes seguidas nao
	/// faz nada na segunda; chamar no login nao empilha. O razao (<see cref="NivelSave.Somados"/>)
	/// e o que torna isso possivel, e por isso ele vai pro disco junto.
	///
	/// CANAL SEPARADO do <c>Fighter.BuffsDeSkill</c>: aquele razao e da COMPRA e o
	/// <see cref="EfeitosDeSkill.Aplicar"/> o reescreve inteiro a cada chamada. Somar degrau la
	/// dentro faria a proxima compra de skill apagar o ganho de nivel de todo mundo.
	///
	/// Devolve quantos campos foram efetivamente mexidos.
	/// </summary>
	public int Aplicar(Fighter f)
	{
		var alvo = new Dictionary<string, double>(StringComparer.Ordinal);
		var fator = new Dictionary<string, double>(StringComparer.Ordinal);
		var chaves = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach ((string path, Progresso pr) in _p)
		{
			RegraDeNivel? r = RegrasDeNivel.Get(path);
			if (r == null) continue;
			foreach (Degrau d in r.Degraus)
			{
				// O PERIODICO RENDE `vezes` VEZES. `kiawarenessskill += 1` a cada 5 niveis, no nivel
				// 35, e +7 -- e nao +1 (que era o que um degrau exato daria) nem zero (que era o que
				// o leitor dava, descartando o periodico inteiro).
				int vezes = RegraDeNivel.Vezes(d, pr.Nivel);
				if (vezes == 0) continue;
				foreach ((string campo, double v) in d.Buffs)
					alvo[campo] = alvo.GetValueOrDefault(campo) + v * vezes;

				// O GENE CAI NO MESMO BALDE DO ADITIVO depois de traduzido ("Energy Level" -> KiMod),
				// exatamente como no `EfeitosDeSkill.Totalizar` -- um so razao pra desfazer no login.
				foreach ((string stat, double v) in d.Genes)
				{
					string? campo = EfeitosDeSkill.TraduzirGene(stat);
					if (campo == null) continue;
					alvo[campo] = alvo.GetValueOrDefault(campo) + v * vezes;
				}

				// O MULTIPLICATIVO COMPOE: `/= 2` no nivel 1 e `/= 2` no nivel 2 deixam o custo em
				// 1/4. Multiplicacao repetida (e nao `Math.Pow`) pelo mesmo motivo do `BarreiraEm`.
				foreach ((string campo, double v) in d.Mults)
				{
					double acc = fator.GetValueOrDefault(campo, 1);
					for (int i = 0; i < vezes; i++) acc *= v;
					fator[campo] = acc;
				}

				// CHAVE SE ESCREVE, nao se acumula -- e por isso ela nao entra no razao
				// `_somados`: nao ha o que desfazer. Duas skills que ligam a mesma chave chegam
				// no mesmo estado, que e o que "ligado" quer dizer.
				foreach ((string campo, double v) in d.Flags) chaves[campo] = v;
			}
		}

		int mexidos = 0;
		foreach ((string campo, double v) in chaves)
			if (Escrever(f, campo, v)) mexidos++;
		foreach ((string campo, double antigo) in _somados)
			if (!alvo.ContainsKey(campo) && Somar(f, campo, -antigo)) mexidos++;

		foreach ((string campo, double v) in alvo)
		{
			double delta = v - _somados.GetValueOrDefault(campo);
			if (Math.Abs(delta) > 1e-9 && Somar(f, campo, delta)) mexidos++;
		}

		// --- MULTIPLICATIVO: DIVIDE o fator antigo antes de multiplicar o novo ---
		foreach ((string campo, double antigo) in _multiplicados)
			if (!fator.ContainsKey(campo) && antigo != 0 && Multiplicar(f, campo, 1 / antigo)) mexidos++;
		foreach ((string campo, double v) in fator)
		{
			double antigo = _multiplicados.GetValueOrDefault(campo, 1);
			if (antigo == 0) antigo = 1;
			double raz = v / antigo;
			if (Math.Abs(raz - 1) > 1e-9 && Multiplicar(f, campo, raz)) mexidos++;
		}

		// ============================ O RAZAO SO GUARDA O QUE PEGOU ============================
		// Campo que o lutador ainda nao tem NAO entra no razao. Se entrasse, o save carregaria
		// "ja somei 3 em KaiokenMastery" sobre um campo que nao existia -- e no dia em que o campo
		// nascesse, o delta daria zero e o buff nunca chegaria em ninguem com save antigo. Foi
		// exatamente o que teria acontecido com `pitted`, `HPregenbuff` e `KaiokenMastery` neste
		// lote: os tres ja constavam no `FlagsDeSkill`/`BuffsDeSkill` de saves como aplicados.
		// ======================================================================================
		_somados = SoOsQueExistem(alvo);
		_multiplicados = SoOsQueExistem(fator);
		return mexidos;
	}

	/// <summary>Filtra um razao aos campos que o <see cref="Fighter"/> de verdade tem.</summary>
	internal static Dictionary<string, double> SoOsQueExistem(Dictionary<string, double> d)
	{
		var r = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach ((string campo, double v) in d)
			if (EfeitosDeSkill.Campo(campo) != null) r[campo] = v;
		return r;
	}

	private static bool Multiplicar(Fighter f, string campo, double razao)
	{
		System.Reflection.FieldInfo? fi = EfeitosDeSkill.Campo(campo);
		if (fi == null) return false;
		fi.SetValue(f, (double)fi.GetValue(f)! * razao);
		return true;
	}

	private static readonly Dictionary<string, FieldInfo?> Cache = new(StringComparer.Ordinal);

	/// <summary>
	/// Reflexao pro campo do lutador. Copia curta do helper privado de
	/// <see cref="EfeitosDeSkill"/> -- vale a pena tornar aquele `internal` e apagar este; ate
	/// la, o canal de diagnostico e O MESMO (<see cref="EfeitosDeSkill.Desconhecidos"/>), pra
	/// que campo que falta apareca numa lista so em vez de duas.
	/// </summary>
	/// <summary>
	/// Soma num campo do lutador reusando o cache de reflexao de <see cref="EfeitosDeSkill"/>.
	///
	/// Havia um segundo cache aqui, com a mesma regra e o mesmo destino. Dois caches sobre o
	/// mesmo `Fighter` nao quebram nada hoje -- e amanha um deles ganha um tratamento que o
	/// outro nao tem, e o campo passa a valer coisas diferentes dependendo de quem somou.
	/// A lista de campos desconhecidos tambem tem que ser UMA so, senao o relatorio mente.
	/// </summary>
	private static bool Somar(Fighter f, string campo, double delta)
	{
		System.Reflection.FieldInfo? fi = EfeitosDeSkill.Campo(campo);
		if (fi == null) return false;
		fi.SetValue(f, (double)fi.GetValue(f)! + delta);
		return true;
	}

	/// <summary>
	/// ESCREVE uma chave (`campo = n`), em vez de somar. Idempotente por natureza: chamar dez
	/// vezes deixa o mesmo valor, entao nao ha razao a manter nem nada a desfazer no login.
	/// </summary>
	private static bool Escrever(Fighter f, string campo, double valor)
	{
		System.Reflection.FieldInfo? fi = EfeitosDeSkill.Campo(campo);
		if (fi == null) return false;
		if ((double)fi.GetValue(f)! == valor) return false;
		fi.SetValue(f, valor);
		return true;
	}


	// =====================================================================
	// DISCO
	// =====================================================================
	public NivelSave ParaSave()
	{
		var s = new NivelSave
		{
			Somados = new Dictionary<string, double>(_somados, StringComparer.Ordinal),
			Multiplicados = new Dictionary<string, double>(_multiplicados, StringComparer.Ordinal),
		};
		foreach ((string path, Progresso pr) in _p)
			// O TERCEIRO NUMERO E O `expbuffer` (ver `Progresso.Buffer`). Save antigo tem dois e
			// continua valendo -- o `DoSave` le o terceiro so quando ele existe.
			if (pr.Nivel > 0 || pr.Exp > 0 || pr.Buffer > 0) s.Skills[path] = [pr.Nivel, pr.Exp, pr.Buffer];
		return s;
	}

	public void DoSave(NivelSave? s)
	{
		_p.Clear();
		_somados = new Dictionary<string, double>(StringComparer.Ordinal);
		_multiplicados = new Dictionary<string, double>(StringComparer.Ordinal);
		if (s == null) return;
		foreach ((string path, double[] v) in s.Skills)
		{
			// SO ENTRA QUEM TEM REGRA. O save guarda o progresso de tudo que ja subiu; se uma
			// regra for retirada numa atualizacao (ou se o save vier de uma versao que tinha
			// mais), o path continua no disco. O `Efetor` faz `RegrasDeNivel.Get(path)!` --
			// aquele `!` vira NullReferenceException no primeiro tique depois do login, e o
			// laco de nivel morre pra AQUELE jogador sem barulho nenhum.
			//
			// Filtrar na ENTRADA, e nao no laco: assim o progresso orfao simplesmente nao
			// existe, em vez de existir e ter que ser desviado em todo lugar que o percorre.
			if (v.Length >= 2 && RegrasDeNivel.Get(path) != null)
				_p[path] = new Progresso { Nivel = (int)v[0], Exp = v[1], Buffer = v.Length >= 3 ? v[2] : 0 };
		}
		foreach ((string campo, double v) in s.Somados) _somados[campo] = v;
		if (s.Multiplicados != null)
			foreach ((string campo, double v) in s.Multiplicados) _multiplicados[campo] = v;
	}

	// =====================================================================
	// DIAGNOSTICO
	// =====================================================================
	/// <summary>
	/// SKILLS QUE DEVIAM SUBIR E NAO SOBEM: o catalogo diz `maxnivel > 1` e nao ha regra portada.
	///
	/// Mesmo espirito do <see cref="Skill.SemEfeito"/>: o buraco vira numero em vez de silencio.
	/// Uma skill de maxlevel 3 parada no nivel 0 pra sempre parece funcionar.
	/// </summary>
	public static IEnumerable<string> SemRegra(SkillCatalog cat) =>
		cat.Todas.Where(s => !s.Arvore && s.MaxNivel > 1 && RegrasDeNivel.Get(s.Path) == null)
			.Select(s => s.Path);
}

/// <summary>
/// AS REGRAS DE NIVEL, POR SKILL. Registro global -- e dado do jogo, igual pra todo personagem.
///
/// ISTO AINDA E SEMENTE, E ASSUMO. O certo e o `Tools/AssetPipeline` emitir `expbarrier` e os
/// degraus no `skills.json`, como ja faz com `buffs` e `verbos` -- sao 200+ skills com
/// `expbarrier` declarado no DM e transcrever isso a mao seria repetir o erro que o catalogo
/// extraido existe pra evitar. O que esta aqui sao as 24 skills que sobem por TEMPO
/// (`prob(50) exp++`), que e uma lista fechada e conferida uma a uma no DM: um grep exato, sem
/// ambiguidade de parser. As arvores de Ki (Mind, Effusion) sobem por EVENTO e entram por
/// <see cref="NiveisDeSkill.Creditar"/> quando o extrator trouxer os numeros delas.
///
/// Enquanto o extrator nao vem, <see cref="Registrar"/> e a porta: um lote extraido pode ser
/// despejado aqui no boot sem que uma linha deste arquivo mude.
/// </summary>
public static class RegrasDeNivel
{
	private static readonly Dictionary<string, RegraDeNivel> Mapa = new(StringComparer.OrdinalIgnoreCase);

	public static RegraDeNivel? Get(string path) => Mapa.GetValueOrDefault(path);
	public static int Total => Mapa.Count;

	/// <summary>
	/// TODOS OS VERBOS QUE ALGUM DEGRAU CONCEDE, sem repetir.
	///
	/// Existe pro <see cref="CensoDeSkills"/>, e a razao de ele precisar disto e a mesma que ja
	/// custou dois dos tres verbos de tiro do jogo: um verb concedido por NIVEL nao aparece em
	/// `Skill.Verbos`, entao um censo que so olhasse o catalogo daria 127 verbos quando o jogo
	/// concede 186 -- e os 59 que faltassem seriam justamente os que ninguem sabe que existem.
	/// </summary>
	public static IEnumerable<string> VerbosDeDegrau =>
		// os verbs POR CASA entram na mesma conta: o degrau os concede (a uma casa de cada vez), e um
		// censo que nao os visse chamaria a Trindade de "sem porta" de novo
		Mapa.Values.SelectMany(r => r.Degraus)
			.SelectMany(d => d.Verbos.Concat(d.VerbosPorCasa.Select(p => p.Verbo)))
			.Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

	public static void Registrar(RegraDeNivel r)
	{
		if (r.Path.Length == 0) return;
		Mapa[r.Path] = r;
		// o indice por contador vira poeira: a carga do disco troca regras em lote (`RegrasDoDisco`)
		// e uma delas pode ser a que observa um contador novo
		_porContador = null;
		_destravaPor = null;
	}

	/// <summary>Quem acende uma skill por degrau: a skill dona do degrau e o nivel dele.</summary>
	public readonly record struct AcendedorPorDegrau(string Path, int Nivel);

	/// <summary>
	/// QUEM ACENDE <paramref name="alvo"/> POR DEGRAU DE NIVEL -- o indice invertido do
	/// `Degrau.Destrava`. Duas bocas o leem: o veredito da loja (pra dizer "acende no nivel 100 da
	/// Basic Ki Awareness" em vez de "morta neste port") e o censo (pra tirar essas folhas da conta
	/// de "sem acendedor"). Vazio pra quem nenhum degrau acende.
	/// </summary>
	public static IReadOnlyList<AcendedorPorDegrau> DestravadaPor(string alvo)
	{
		_destravaPor ??= IndexarDestrava();
		return _destravaPor.GetValueOrDefault(alvo) ?? [];
	}

	/// <summary>Todo alvo que algum degrau acende -> quem acende (o primeiro, pro texto do censo).</summary>
	public static IReadOnlyDictionary<string, AcendedorPorDegrau> DestravadasPorDegrau
	{
		get
		{
			_destravaPor ??= IndexarDestrava();
			var d = new Dictionary<string, AcendedorPorDegrau>(StringComparer.OrdinalIgnoreCase);
			foreach ((string alvo, List<AcendedorPorDegrau> l) in _destravaPor) if (l.Count > 0) d[alvo] = l[0];
			return d;
		}
	}

	private static Dictionary<string, List<AcendedorPorDegrau>>? _destravaPor;

	private static Dictionary<string, List<AcendedorPorDegrau>> IndexarDestrava()
	{
		var ix = new Dictionary<string, List<AcendedorPorDegrau>>(StringComparer.OrdinalIgnoreCase);
		foreach (RegraDeNivel r in Mapa.Values)
			foreach (Degrau d in r.Degraus)
				foreach (string alvo in d.Destrava)
				{
					if (!ix.TryGetValue(alvo, out List<AcendedorPorDegrau>? l)) ix[alvo] = l = [];
					l.Add(new AcendedorPorDegrau(r.Path, d.Periodo > 0 ? d.Periodo : d.Nivel));
				}
		return ix;
	}

	/// <summary>Uma skill que observa um contador, e o fator dela. Ver <see cref="DoContador"/>.</summary>
	public readonly record struct CreditoDeContador(string Path, double Quanto);

	/// <summary>
	/// QUEM OBSERVA ESTE CONTADOR -- o indice que faz o exp por evento custar uma busca em vez de
	/// uma varredura das 101 regras a cada golpe disparado.
	///
	/// E MONTADO SOB DEMANDA e jogado fora a cada <see cref="Registrar"/>: a carga do `niveis.json`
	/// registra em lote no boot, e reconstruir o indice 101 vezes durante ela seria trabalho jogado
	/// fora. Depois do boot ninguem registra mais nada, entao na pratica ele e montado uma vez.
	///
	/// Devolve lista VAZIA pro contador que ninguem observa -- e o caso de todo contador enquanto o
	/// `niveis.json` nao tiver sido carregado (bancada de unidade, por exemplo).
	/// </summary>
	public static IReadOnlyList<CreditoDeContador> DoContador(string contador)
	{
		_porContador ??= Indexar();
		return _porContador.GetValueOrDefault(contador) ?? [];
	}

	private static Dictionary<string, List<CreditoDeContador>>? _porContador;

	private static Dictionary<string, List<CreditoDeContador>> Indexar()
	{
		var ix = new Dictionary<string, List<CreditoDeContador>>(StringComparer.OrdinalIgnoreCase);
		foreach (RegraDeNivel r in Mapa.Values)
			foreach (RegraDeNivel.GanhoPorContador g in r.PorContador)
			{
				if (g.Contador.Length == 0 || g.Quanto <= 0) continue;
				if (!ix.TryGetValue(g.Contador, out List<CreditoDeContador>? l)) ix[g.Contador] = l = [];
				l.Add(new CreditoDeContador(r.Path, g.Quanto));
			}
		return ix;
	}

	/// <summary>Atalho do caso comum: sobe por tempo, barreira fixa, um verbo por degrau.</summary>
	private static void Tempo(string path, double barreira, params (int Nivel, string Verbo, string Aviso)[] degraus)
	{
		Registrar(new RegraDeNivel
		{
			Path = path,
			Barreira = barreira,
			GanhoPorTempo = true,
			Degraus = [.. degraus.Select(d => new Degrau { Nivel = d.Nivel, Verbos = [d.Verbo], Aviso = d.Aviso })],
		});
	}

	static RegrasDeNivel()
	{
		// =================================================================
		// AS 24 QUE SOBEM POR TEMPO.
		//
		// Todas tem a mesma forma no DM: `if(level < maxlevel) if(prob(50)) exp++` seguido de um
		// `switch(level)` que so faz `assignverb`. O `maxlevel` NAO esta repetido aqui de
		// proposito -- ele ja vem do catalogo extraido (`maxnivel`), e ter o numero em dois
		// lugares e ter dois numeros diferentes daqui a um ano.
		//
		// A barreira, sim, esta: o extrator ainda nao emite `expbarrier`. Onde o DM nao declara,
		// vale o padrao 100 do `/datum/skill` (skill.dm:9) -- e o que passo aqui explicitamente
		// pra que o numero apareca, em vez de sumir num default.
		// =================================================================

		// --- Corpo (Body.dm) ---
		// DEFEITO VIVO DO ORIGINAL, portado como esta: `grace` tem maxlevel = 1 (Body.dm:159) mas
		// o degrau esta no nivel 2 (Body.dm:182-185). Como o efetor so sobe `if(level < maxlevel)`
		// (skill.dm:92), o nivel 2 e inalcancavel e o Falling Kick e codigo morto no BYOND --
		// mesmo com o `before_forget()` se dando ao trabalho de remove-lo (Body.dm:177). Deixo o
		// degrau declarado: o dia que o maxlevel virar 2 no catalogo, o golpe acende sozinho.
		Tempo("/datum/skill/grace", 100,
			(2, "Falling_Kick", "voce coordena um chute basico: Falling Kick, que machuca mais quem voa."));

		Tempo("/datum/skill/MartialSkill/Kicker", 100,     // Body.dm:201
			(2, "Kickup", "seu chute derruba: Kickup atordoa quem esta no chao."),
			(3, "Dropkick", "Dropkick liberado -- voce avanca em linha reta com o golpe."));

		Tempo("/datum/skill/barrel", 100,                  // sem expbarrier no DM -> padrao
			(2, "Seismic_Press", "seu peso virou arma: Seismic Press."),
			(3, "Gigantic_Spike", "Gigantic Spike liberado."));

		Tempo("/datum/skill/MartialSkill/Beserker", 100,   // Body.dm:315
			(2, "Power_Drag", "Power Drag liberado."),
			(3, "Revenge_Demon", "Revenge Demon liberado."));

		Tempo("/datum/skill/boxing", 100,                  // sem expbarrier no DM -> padrao
			(2, "One_Two", "voce encadeia os punhos: One Two."));

		Tempo("/datum/skill/Small_Boxer", 100,             // Body.dm:445
			(2, "One_Two_Five", "One Two Five liberado."),
			(3, "Two_One_Four", "Two One Four liberado."));

		Tempo("/datum/skill/Bigtime_Boxer", 100,           // Body.dm:488
			(2, "KO_Punch", "KO Punch liberado."));

		// --- Assassino (Assassain.dm) -- todas expbarrier = 100, maxlevel = 2, um verbo no 2 ---
		Tempo("/datum/skill/Assassain/Precise_Arts", 100, (2, "Shock", "Shock liberado."));                      // :22
		Tempo("/datum/skill/Assassain/Reverbrate", 100, (2, "Reverb", "Reverb liberado."));                      // :53
		Tempo("/datum/skill/Assassain/Omae_Wa_Moe", 100, (2, "Precise_Explosion", "Precise Explosion liberado."));// :84
		Tempo("/datum/skill/Assassain/Hokuto_no_Shinken", 100,
			(2, "Hokuto_Hyakuretsu_Ken", "Hokuto Hyakuretsu Ken liberado."));                                    // :115
		Tempo("/datum/skill/Assassain/Cutthroat", 100, (2, "Cutthroat", "Cutthroat liberado."));                 // :150
		Tempo("/datum/skill/Assassain/Backstab", 100, (2, "Backstab", "Backstab liberado."));                    // :183
		Tempo("/datum/skill/Assassain/Sneak", 100, (2, "Sneak", "Sneak liberado."));                             // :214
		Tempo("/datum/skill/Assassain/Trip", 100, (2, "Trip", "Trip liberado."));                                // :245

		// --- Marcial (Martial Skill.dm) -- nenhuma declara expbarrier: padrao 100 (skill.dm:9) ---
		Tempo("/datum/skill/MartialSkill/Green_Dean", 100, (2, "Dash_Attack", "Dash Attack liberado."));          // :42-45
		Tempo("/datum/skill/MartialSkill/Catflex", 100, (2, "Flip", "Flip liberado."));                           // :76-79
		Tempo("/datum/skill/MartialSkill/Tiger_Paw", 100, (2, "Takedown", "Takedown liberado."));                  // :112-115
		Tempo("/datum/skill/MartialSkill/Dragon_Sweep", 100, (2, "Stun_Attack", "Stun Attack liberado."));         // :146-149
		Tempo("/datum/skill/MartialSkill/Reflexes_Training", 100, (2, "Spin_Attack", "Spin Attack liberado."));    // :182-185

		// --- Luta livre (Wrestling.dm) -- todas expbarrier = 100, maxlevel = 2 ---
		Tempo("/datum/skill/Wrestling/Grabber", 100, (2, "Hold", "Hold liberado."));          // :20
		Tempo("/datum/skill/Wrestling/Superstar", 100, (2, "Clench", "Clench liberado."));    // :53
		Tempo("/datum/skill/Wrestling/Rasslin", 100, (2, "Power_Slam", "Power Slam liberado."));// :84
		Tempo("/datum/skill/Wrestling/Main_Event", 100, (2, "Suplex", "Suplex liberado."));   // :115
	}
}
