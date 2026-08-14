namespace Jandirus.Core.Forms;

/// <summary>Por que a transformacao nao rolou. <see cref="Pode"/> = rolou.</summary>
public enum RecusaForma
{
	Pode = 0,

	// ============================ `NaoEhSaiyajin` FOI DELETADA, E ELA ERA UMA CONFISSAO ============================
	// Ela morava aqui desde o primeiro dia, com o nome exato da regra que faltava, e **nunca teve uma
	// unica referencia** -- porque a regra nao existia: o `Catalogo.LinhasAbertas` entregava a escada
	// Saiyajin a qualquer raca que nao fosse Primal, Legendary, Futuro ou Frost Demon. Um Namekuseijin
	// com BP suficiente apertava C e virava Super Saiyajin.
	//
	// A porta foi fechada por raca nesta sessao (ver `LinhasAbertas`) e quem responde por ela e a
	// `LinhaFechada`, que ja era a recusa das outras exclusoes de linha -- e e a recusa CERTA: o
	// `PorQueNao` a filtra da mensagem de proposito ("aquela escada nao e desta pessoa"), e dizer a um
	// Humano que ele "nao e Saiyajin" ensinaria que existe um jeito de ser.
	//
	// Foi deletada em vez de ficar como sinonimo porque a regra da casa e essa (codigo substituido se
	// DELETA), e porque um valor de enum com o nome de uma regra e o jeito mais silencioso que este
	// projeto ja achou de PARECER ter a regra. `RecusaForma` nao vai pra rede nem pra save -- o unico
	// portador dela e o `Capacidades.RecusaDaForma`, em memoria --, entao renumerar nao quebra nada.
	// =========================================================================================================
	SemPoder,        // BP base insuficiente
	SemMaestria,     // grade/degrau que ainda nao abriu
	ForaDeOrdem,     // tentou pular degrau
	SemKi,
	Caido,
	JaEsta,
	LinhaFechada,    // a linha nao e deste personagem (Primal, Futuro, divinas...)
	SemLinhagem,
	SemClasse,
	SemFormaAnterior, // pede uma forma de OUTRA linha (SSJ4 <- Oozaru Dourado)
	NaoConcedida,     // so por concessao, e ninguem concedeu (o Mistico, pelo ritual do Kaioshin)
	SemGodKi,
	SemEnergia,
	FormaErrada,      // pede estar noutra forma AGORA (Blue = SSJ + ki divino)
	SemFuria,         // o despertar pede RAIVA e a que este corpo tem nao chega la (ver NivelDeRaiva)
	SemHabilidade,    // a forma se COMPRA antes de se ter (ver FormaDef.PedeFlag)
}

/// <summary>
/// A FORMA EM QUE O PERSONAGEM ESTA, e o que ele domina de cada uma.
///
/// Vive no Core porque as duas pontas precisam da MESMA resposta: o cliente pra desenhar a aba
/// Formas (e nao oferecer o que vai ser recusado) e o servidor pra decidir. O que o cliente NAO
/// faz e aplicar o multiplicador -- isso e poder, e poder so o servidor calcula.
/// </summary>
public sealed class EstadoDeForma
{
	/// <summary>
	/// OS LIMIARES DESTE PERSONAGEM. Nulo = usa a constante de fabrica.
	///
	/// Vive aqui e nao no <see cref="Jandirus.Core.Stats.Fighter"/> porque e dado de FORMA, nao de
	/// corpo -- quem pergunta "posso virar?" ja tem este objeto na mao.
	/// </summary>
	public LimiaresPessoais? Limiares;

	/// <summary>Id da forma atual. <see cref="Catalogo.IdBase"/> = normal.</summary>
	public string Atual = Catalogo.IdBase;

	public Maestrias Maestria = new();

	// ============================ UM CONJUNTO ERA DUAS COISAS ============================
	// Ate aqui existia UM `JaDespertou` respondendo a duas perguntas diferentes:
	//
	//   * "esta forma esta LIBERADA?"  -- o que os gates leem (`PedeFormaDespertada`);
	//   * "a ESTREIA dela ja tocou?"   -- o que decide se a cinematica roda.
	//
	// Enquanto o unico jeito de liberar uma forma era ENTRAR nela, as duas respostas eram a mesma
	// e nada quebrava. O Oozaru Dourado dominado quebra: sair dele **poe o corpo em SSJ4 sem cena**
	// (pedido do dono: "apenas o ozaru e desfeito e o player cai no estagio de ssj4"), e a cena do
	// SSJ4 tem que tocar depois, na primeira vez que ele SUBIR ate la apertando o C. Com um
	// conjunto so, liberar consumiria a estreia e o jogador perderia a cinematica **em silencio**
	// -- o pior tipo de defeito, porque nada na tela diz que faltou alguma coisa.
	//
	// O nome mudou junto (`JaDespertou` -> `Liberadas`) de proposito: e o compilador achando os
	// consumidores por mim. Quem so quer o gate le `Liberadas`; quem quer a cena le `EstreiaVista`.
	// ==================================================================================

	/// <summary>
	/// Formas que este personagem tem LIBERADAS. Guardado pelo <see cref="FormaDef.IdRede"/> -- o
	/// mesmo inteiro de sempre, pra o save antigo carregar sem conversao.
	///
	/// Libera-se ENTRANDO na forma (o caminho normal) ou por conquista de outra linha (o Oozaru
	/// Dourado dominado libera o SSJ4). Ver <see cref="Liberar"/>.
	/// </summary>
	public HashSet<int> Liberadas = [];

	/// <summary>
	/// Formas cuja ESTREIA ja foi assistida. Subconjunto de <see cref="Liberadas"/> na pratica --
	/// nao da pra ver a cena de uma forma sem estar nela, e estar nela libera.
	///
	/// Persiste junto com as liberadas: a cinematica roda uma vez POR PERSONAGEM, nao por sessao.
	/// </summary>
	public HashSet<int> EstreiaVista = [];

	/// <summary>
	/// AS PORTAS QUE UM MESTRE JA CORTOU PELA METADE, pelo <see cref="FormaDef.IdRede"/>.
	///
	/// ============================ O DM CORTA O LIMIAR; AQUI SE ANOTA O CORTE ============================
	/// No original a transformacao assistida e PERMANENTE por um efeito colateral: as procs
	/// canonicas fazem `ssjat /= 2` na primeira vez (`SSj()`: *"if(!hasssj) ssjat/=2"*), e o
	/// `mst_form_seal` (`MasterStudent.dm:376`) completa o que elas esquecem (`ssj3at *= 0.5`,
	/// `ssjat *= 0.5` do lssj1). Ou seja: la o numero sorteado no nascimento e REESCRITO.
	///
	/// Aqui ele nao pode ser: o <see cref="LimiaresPessoais"/> e sorteado uma vez e a promessa
	/// dele -- "isto rola uma vez e nunca mais" -- e o que faz o SSJ de cada um custar diferente.
	/// Entao o que se guarda e o FATO ("um mestre abriu esta porta pra mim"), e o
	/// <see cref="Avaliar"/> aplica o corte toda vez que ela for consultada.
	///
	/// **E ISTO NAO E DETALHE DE GOSTO, E O CONSERTO DE UM BUG CONHECIDO DO DM.** O proprio
	/// `mst_form_apply` (`:358-360`) explica por que nao seta as flags de posse antes de chamar a
	/// proc canonica: cravar `hasssj` pulava o corte e o aluno ficava *"com uma forma na qual nao
	/// conseguia reentrar"*. Aqui aconteceria o mesmo se o corte valesse so na tentativa: o passo 8
	/// cobra a porta SEMPRE (inclusive de forma ja liberada), entao o aluno despertaria com o
	/// mestre, voltaria pra base e nao subiria mais ate dobrar o BP.
	/// ================================================================================================
	/// </summary>
	public HashSet<int> PortasCortadas = [];

	/// <summary>
	/// ANOTA QUE UM MESTRE ABRIU ESTA PORTA. Devolve TRUE se foi esta chamada que a abriu.
	/// Chamado uma vez, no sucesso da transformacao assistida (`GameServer.Mestre.cs`).
	/// </summary>
	public bool CortarPorta(string id) => PortasCortadas.Add(Catalogo.Rede(id));

	/// <summary>
	/// HA QUANTO TEMPO EM COMBATE CONTINUO, em segundos. E o `combatTime` do DM.
	///
	/// Mora aqui e nao no combate porque so as formas o consomem: a rampa do Legendary e o bonus do
	/// Legendary Primal. Quem zera e a tag de combate expirando.
	/// </summary>
	public double CombateSegundos;

	/// <summary>
	/// A TECLA C PASSA PELOS GRADES DO SSJ1? E preferencia do JOGADOR -- o verb `graus` (aba Other,
	/// ver `GameServer.Formas.cs`) e o pedido do dono: *"com eles LIGADOS, no ssj1 (masterizado ou
	/// nao) apertar C duas vezes passa pelos grades antes do ssj2; DESLIGADOS, pula direto pro
	/// ssj2"*.
	///
	/// ============================ SAO TRES ESTADOS, E O NULO NAO E PREGUICA ============================
	/// `null` = **ninguem opinou**, e e o que todo corpo SEM DONO carrega: o NPC sorteado
	/// (`SorteioDeNpc.AbrirFormas`) e o cerebro da IA (`GameServer.Ia.cs`) sobem a escada por este
	/// mesmo <see cref="Proxima"/>, e nenhum dos dois tem um jogador pra apertar botao. Com nulo eles
	/// ficam com a regra de sempre -- o degrau MAIS FORTE alcancavel --, que e o que faziam ontem.
	///
	/// **E OS DOIS OUTROS VALORES MEXERIAM NO NPC, cada um pro seu lado.** Com `true`, um NPC de
	/// maestria 100 (ha dois no `npcs.json`) desceria de 6x (SSJ1 dominado) pra 3x (Grade 2) NO MEIO
	/// DA LUTA, porque a preferencia poe o ramo na frente do tronco. Com `false`, ele nunca abriria
	/// os grades na biografia -- e o SSJ2 dele sairia mais fraco, porque o piso do SSJ2 conta o
	/// MAIOR RAMO ABERTO (`Catalogo.MaiorRamoAberto`): apagar o Grade 3 da escada apaga 2x do degrau
	/// seguinte. Um booleano nao tem onde por "corpo sem dono".
	/// ================================================================================================
	///
	/// Quem preenche pro jogador e o login (`GameServer.RestaurarFormaEDisciplina`), com `true`
	/// quando o disco nao diz nada: LIGADO e o lado que o jogo ja tinha.
	/// </summary>
	public bool? GradesLigados;

	public FormaDef? Def => Catalogo.Def(Atual);

	public bool NaBase => Atual == Catalogo.IdBase;

	/// <summary>Esta forma esta LIBERADA pra este personagem? E o que os gates perguntam.</summary>
	public bool Despertou(string id) => Liberadas.Contains(Catalogo.Rede(id));

	/// <summary>
	/// LIBERA uma forma SEM consumir a estreia dela.
	///
	/// E a metade do antigo `JaDespertou.Add` que serve pra conquista vinda de fora da escada: o
	/// Oozaru Dourado dominado abre o SSJ4, mas a cena do SSJ4 continua devendo. Devolve TRUE se
	/// esta chamada e que abriu (pra quem quiser anunciar a conquista uma vez so).
	/// </summary>
	public bool Liberar(string id) => Liberadas.Add(Catalogo.Rede(id));

	/// <summary>A estreia desta forma ja foi assistida?</summary>
	public bool JaViuAEstreia(string id) => EstreiaVista.Contains(Catalogo.Rede(id));

	/// <summary>
	/// Multiplicador que o <c>Fighter.ssjBuff</c> recebe.
	///
	/// As formas ABSOLUTAS (divinas) tambem saem por aqui: no DM elas moram em `god_form_mult` e
	/// nao em `ssjBuff`, mas la o `powerlevel()` soma as duas coisas no mesmo ponto. Como aqui so
	/// se esta em UMA forma por vez, o valor final e o mesmo -- e o campo
	/// <see cref="FormaDef.Absoluta"/> existe pra quem for empilhar God Ki sobre SSJ um dia saber
	/// que aquele numero SUBSTITUI, nao multiplica.
	///
	/// O PARAMETRO ERA UM `bool diluido` E VIROU O PERFIL INTEIRO, e nao por gosto: a curva do
	/// Mistico le a LINHAGEM e a maestria de KI DIVINO de quem esta na forma (ver
	/// <see cref="FormaDef.EscalaComGodKi"/>), e o booleano ja era um pedaco do perfil viajando
	/// sozinho. Com o tipo trocado, quem calcula poder sem saber de quem e o poder nao compila --
	/// que era o unico jeito de garantir que o Prodigial nao ganhasse 16x calado.
	/// </summary>
	public double Multiplicador(PerfilDeFormas perfil) =>
		NaBase ? 1 : Catalogo.Multiplicador(Atual, Maestria, perfil, CombateSegundos);

	public double DrenoPorSegundo() => Catalogo.DrenoPorSegundo(Atual, Maestria);

	/// <summary>
	/// ESTE DEGRAU ESTA NO CAMINHO DA TECLA C DESTE PERSONAGEM? So diz NAO pro RAMO LATERAL de quem
	/// desligou os grades (ver <see cref="GradesLigados"/>).
	///
	/// ============================ PUBLICO PORQUE E FUNIL, E O FUNIL E A MENSAGEM ============================
	/// Quem pergunta sao dois: o <see cref="Proxima"/> (o seletor) e o `PorQueNao` (a frase que o
	/// jogador ouve quando nao ha degrau). Sao exatamente os dois que este arquivo ja avisou que
	/// divergem quando a regra e escrita duas vezes -- e aqui a divergencia teria voz: com os grades
	/// desligados e o SSJ2 ainda trancado, um `PorQueNao` que continuasse enxergando o Grade 2
	/// responderia *"voce precisa de 50% de maestria"* -- falando de um degrau que o jogador acabou de
	/// tirar do caminho -- em vez da recusa do SSJ2, que e a que o dono pediu que ele ouvisse.
	/// ======================================================================================================
	///
	/// **RAMO LATERAL E MAIS GERAL QUE "GRADE" DE PROPOSITO.** As supressoes do Frost Demon tambem sao
	/// <see cref="FormaDef.ForaDoTronco"/> e nao mudam de comportamento nenhum: elas valem MENOS que a
	/// forma em que se esta e o C so sobe, entao nunca foram oferecidas. Escrever "menos o grade2 e o
	/// grade3" daria o mesmo resultado hoje e mentiria no dia do proximo ramo.
	/// </summary>
	public bool NoCaminhoDoC(FormaDef d) => !d.ForaDoTronco || GradesLigados != false;

	/// <summary>
	/// O PROXIMO DEGRAU a partir de onde se esta: o mais FORTE que estiver aberto agora.
	///
	/// Nao e "o de baixo mais um". Do SSJ1 saem tres caminhos (grade 2, grade 3 e o SSJ2), e quem
	/// dominou o SSJ1 (6x) passa direto pelos grades -- eles ficam obsoletos de proposito. Escolher
	/// pelo multiplicador e o que faz isso acontecer sozinho, sem lista de excecoes.
	///
	/// ============================ E A PREFERENCIA DO JOGADOR PASSA NA FRENTE DISSO ============================
	/// O "mais forte vence" e uma boa regra e uma decisao TOMADA POR ALGUEM: com o SSJ1 dominado (6x)
	/// ela apaga os grades (3x e 4x) da escada, e o dono pediu o contrario em voz alta -- *"no ssj1
	/// (masterizado ou nao) apertar C duas vezes passa pelos grades antes do ssj2"*. Nao da pra
	/// atender isso mexendo em multiplicador (o Grade 2 nao PODE valer mais que um SSJ1 dominado, ou
	/// dominar a forma passaria a ser castigo): o que muda e a ORDEM da escolha, e so pra quem pediu.
	/// ========================================================================================================
	/// </summary>
	public FormaDef? Proxima(double bpBase, PerfilDeFormas perfil)
	{
		// COM OS GRADES LIGADOS, O RAMO VEM ANTES DO TRONCO -- e o `== true` e literal: `null` e
		// corpo sem dono (NPC, IA) e cai na regra de sempre. Ver `GradesLigados`.
		if (GradesLigados == true && ProximoRamoLateral(bpBase, perfil) is { } ramo) return ramo;

		FormaDef? melhor = null;
		double melhorMult = Multiplicador(perfil);

		foreach (FormaDef d in Catalogo.Todas)
		{
			if (d.Id == Atual || d.Id == Catalogo.IdBase) continue;
			if (!NoCaminhoDoC(d)) continue;
			if (Avaliar(d.Id, bpBase, kiFracao: 1, caido: false, perfil) != RecusaForma.Pode) continue;
			double m = Catalogo.Multiplicador(d.Id, Maestria, perfil, CombateSegundos);
			if (m <= melhorMult) continue;
			melhorMult = m;
			melhor = d;
		}
		return melhor;
	}

	/// <summary>
	/// O PROXIMO RAMO LATERAL DO MEU PROPRIO DEGRAU -- o MENOR que ainda esta acima de mim. Nulo
	/// quando nao ha (ou quando ele esta trancado, que e a mesma coisa pra quem aperta o C).
	///
	/// ============================ POR QUE O TRONCO E CALCULADO, E NAO E "A FORMA ATUAL" ============================
	/// Estando no Grade 2, a pergunta certa nao e "que ramo sai do Grade 2?" (nenhum -- os dois grades
	/// saem do SSJ1) e sim "que outro ramo sai do MESMO degrau de onde eu sai?". Sem isso o Grade 3
	/// ficaria inalcancavel pelo C: do Grade 2 a escolha pularia direto pro SSJ2, que e exatamente o
	/// que o dono NAO quer com os grades ligados.
	/// ===========================================================================================================
	///
	/// **E ELE SOBE, NUNCA DESCE**: `Ordem` maior que a minha. E o que impede o C de oferecer o Grade
	/// 2 a quem ja esta no Grade 3 -- um vaivem infinito entre dois degraus, com a escada travada
	/// embaixo pra sempre.
	///
	/// NADA AQUI AFROUXA GATE: o candidato passa pelo <see cref="Avaliar"/> inteiro, igual ao do
	/// tronco. Um grade sem a maestria exigida simplesmente nao existe pra esta funcao, e a escolha
	/// escorrega pro caminho normal.
	/// </summary>
	private FormaDef? ProximoRamoLateral(double bpBase, PerfilDeFormas perfil)
	{
		if (Def is not { } agora) return null;   // na base nao ha ramo nenhum pra oferecer

		FormaDef tronco = agora.ForaDoTronco ? Catalogo.Anterior(agora) ?? agora : agora;

		FormaDef? menor = null;
		foreach (FormaDef d in Catalogo.Todas)
		{
			if (!d.ForaDoTronco || d.Linha != tronco.Linha) continue;
			if (Catalogo.Anterior(d)?.Id != tronco.Id) continue;   // ramo de outro degrau
			if (d.Ordem <= agora.Ordem) continue;                  // ja passei por ele
			if (Avaliar(d.Id, bpBase, kiFracao: 1, caido: false, perfil) != RecusaForma.Pode) continue;
			if (menor == null || d.Ordem < menor.Ordem) menor = d;
		}
		return menor;
	}

	/// <summary>
	/// DA PRA IR PRA ESTA FORMA AGORA?
	///
	/// ============================ A ORDEM DAS CHECAGENS E A MENSAGEM ============================
	/// Ela e a do original de proposito: dizer "falta poder" pra quem so precisa treinar a forma
	/// anterior manda a pessoa fazer a coisa errada por horas. O caso mais caro e o SSJ4 -- quem
	/// ouvir "falta BP" vai treinar, quando o que falta e ter virado Oozaru Dourado uma vez.
	/// =========================================================================================
	/// </summary>
	/// <param name="fatorDaPorta">
	/// QUANTO DA PORTA DE BP ESTA TENTATIVA PAGA. 1 = a porta inteira (todo mundo, sempre);
	/// <see cref="Jandirus.Core.Skills.Discipulado.FatorAssistido"/> = metade, e e a transformacao
	/// assistida por um mestre (`MST_HALF`).
	///
	/// ============================ ELE ENTRA NO FUNIL, E NAO DEPOIS ============================
	/// A tentacao e cobrar a porta cheia aqui e dar o desconto no chamador ("se recusou por poder e
	/// tem mestre, deixa passar"). Isso faz a MENSAGEM mentir: o `PorQueNao` continuaria dizendo
	/// "ainda esta alem do seu alcance" pra uma forma que o mestre acabou de abrir, e a aba Formas
	/// do cliente pintaria de cinza um degrau alcancavel. Uma casa so -- esta.
	/// ====================================================================================
	/// </param>
	public RecusaForma Avaliar(string alvo, double bpBase, double kiFracao, bool caido,
							   PerfilDeFormas perfil, double fatorDaPorta = 1)
	{
		if (alvo == Atual) return RecusaForma.JaEsta;
		if (alvo == Catalogo.IdBase) return RecusaForma.Pode;   // descer sempre pode
		if (caido) return RecusaForma.Caido;

		FormaDef? d = Catalogo.Def(alvo);
		if (d == null) return RecusaForma.ForaDeOrdem;

		// ============================ 0. NAO SE SOBE PRO OOZARU ============================
		// "n transforma apertando C, ele n e da linha do ssj" -- e era literalmente o que acontecia:
		// `Proxima()` varre `Catalogo.Todas`, a linha do Oozaru esta ABERTA pra qualquer Saiyajin
		// (`LinhasAbertas`, Formas.cs:1078) e a entrada `oozaru` nao tem porta nenhuma -- sem
		// `PortaBp`, sem `PedeMaestria`, com o degrau anterior sendo a base. Ela avaliava `Pode` e,
		// valendo 1,5x contra 1x da base, era o degrau MAIS FORTE disponivel pra quem ainda nao
		// alcancou o SSJ1. Apertar C numa noite qualquer punha `Atual = "oozaru"` -- o slot da
		// ESCADA -- enquanto `ServerPlayer.Oozaru` (o estado paralelo de verdade) continuava `Nao`.
		//
		// A guarda mora AQUI e nao no `Proxima` porque `Avaliar` e o funil: `Proxima` (tecla C),
		// `PorQueNao` (a mensagem), o `DirectSSJ` do admin e a aba Formas do cliente passam todos
		// por ela. Ligar num chamador e esquecer do outro e o erro que mais se repetiu neste port.
		//
		// `LinhaFechada` e a recusa CERTA e nao um atalho: o `PorQueNao` ja a filtra da mensagem
		// ("aquela escada nao e desta pessoa"), e dizer "voce nao pode subir pro Oozaru" ensinaria
		// que existe um jeito de subir. Nao existe -- existe a lua.
		// ==================================================================================
		if (Catalogo.NaoSeSobePraEla(d)) return RecusaForma.LinhaFechada;

		// 0b. A FORMA CONCEDIDA. Ver `FormaDef.SoPorConcessao`.
		//
		// ============================ A CONCESSAO VEM ANTES DA LINHA, E NO LUGAR DELA ============================
		// O Mistico e dado pelo ritual de um Kaioshin a QUALQUER raca -- inclusive a quem nao tem
		// escada nenhuma aberta. Perguntar `LinhasAbertas` primeiro devolveria `LinhaFechada` pro
		// Namekiano que acabou de receber o dom, e a mensagem diria "aquela escada nao e desta
		// pessoa" sobre uma forma que e dele por decisao de outro jogador.
		//
		// E a ordem tambem escolhe a MENSAGEM certa pro caso oposto -- o Prodigial com ki divino,
		// que TEM a linha aberta e mesmo assim nao tem o Mistico: ele precisa ouvir "ninguem te
		// concedeu isso" e nao "falta poder", senao vai treinar por horas atras de uma forma que
		// nao se treina.
		// ====================================================================================================
		if (d.SoPorConcessao && !Despertou(d.Id)) return RecusaForma.NaoConcedida;

		// 1. A LINHA E DESTE PERSONAGEM? Um Saiyajin comum nao tem o ladder do Legendary Primal, e
		//    quem nao despertou o ki divino nao tem as divinas. A forma CONCEDIDA pula esta
		//    pergunta: quem concedeu ja respondeu por ela.
		if (!d.SoPorConcessao && !Catalogo.LinhasAbertas(perfil).Contains(d.Linha))
			return RecusaForma.LinhaFechada;

		// 2. LINHAGEM E CLASSE.
		if (d.PedeLinhagem.Length > 0
			&& !string.Equals(perfil.Linhagem, d.PedeLinhagem, StringComparison.OrdinalIgnoreCase))
			return RecusaForma.SemLinhagem;

		if (d.PedeClasseUmaDe.Length > 0 && !Bate(d.PedeClasseUmaDe, perfil.Classe))
			return RecusaForma.SemClasse;

		// A ORIGEM CASA COM LINHAGEM **OU** CLASSE -- ver FormaDef.PedeOrigemUmaDe. E o que faz
		// "God e Blue sao do Saiyajin de linhagem normal e do meio-Saiyajin New Generation" caber
		// num campo, sendo que a primeira metade mora no SaiyanLineage e a segunda no Class.
		if (d.PedeOrigemUmaDe.Length > 0
			&& !Bate(d.PedeOrigemUmaDe, perfil.Linhagem) && !Bate(d.PedeOrigemUmaDe, perfil.Classe))
			return RecusaForma.SemLinhagem;

		// A CLASSE QUE TEM A VARIANTE PERDE A ORIGINAL. "Rose NO LUGAR do Blue"
		// (`statsaiyan.dm:157`), e o Prodigial "NAO tem SSG/Blue" (`godki.dm:349`). Sem esta
		// checagem a classe teria as duas -- e escolher entre 32x e 32x nao e escolha, e ruido.
		if (d.ProibidoParaClasse.Any(c => string.Equals(perfil.Classe, c, StringComparison.OrdinalIgnoreCase)))
			return RecusaForma.SemClasse;

		// ============================ 2b. A FORMA QUE SE COMPRA -- ver FormaDef.PedeFlag ============================
		// LOGO DEPOIS DA CLASSE E BEM ANTES DA PORTA DE BP, e a ordem e a mensagem (cabecalho desta
		// funcao). O Namekuseijin que nao comprou a `SuperNamek` esta a UM ponto de marco da forma; se a
		// porta de BP falasse primeiro, ele ouviria "ainda esta alem do seu alcance" e iria TREINAR --
		// exatamente o erro que o cabecalho descreve pro SSJ4, e aqui ele custaria mais caro, porque o
		// Super Namekuseijin nem tem degrau anterior pra treinar.
		//
		// Depois da classe e nao antes: "esta escada nao e da sua raca" e uma verdade mais grave que
		// "voce nao comprou", e dizer a segunda pra quem esbarra na primeira ensinaria que gastar um
		// ponto resolveria.
		// ==========================================================================================================
		if (d.PedeFlag is { } flag && perfil.Flag(flag.Campo) < flag.Minimo)
			return RecusaForma.SemHabilidade;

		// 3. A FORMA DE OUTRA LINHA. E o pre-requisito do SSJ4 -- ver FormaDef.PedeFormaDespertada.
		if (d.PedeFormaDespertada.Length > 0 && !Despertou(d.PedeFormaDespertada))
			return RecusaForma.SemFormaAnterior;

		// 4. A FORMA EM QUE O CORPO ESTA AGORA (as divinas: SSJ + ki divino). Ver PedeFormaAtual.
		//
		// A CAMADA DE BAIXO TAMBEM VALE, e sem isso a linha divina seria um beco: quem esta em Blue
		// (32x) nunca desceria voluntariamente pro Grade 2 (3x) so pra poder subir de novo -- o
		// seletor automatico so oferece o que e MAIS FORTE, entao o Blue Evolution nunca sairia.
		// Subir a partir do Blue empurra as duas metades de uma vez, que e o que o DM faz: la `ssj`
		// e `godki` sao vars separadas e ascender mexe so na primeira.
		if (d.PedeFormaAtual.Length > 0
			&& !Bate(d.PedeFormaAtual, Atual) && Atual != Catalogo.IdAnterior(d))
			return RecusaForma.FormaErrada;

		// 5. O DEGRAU ANTERIOR DA PROPRIA LINHA. Nao da pra pular.
		//
		// QUANDO A FORMA E UMA CAMADA (PedeFormaAtual preenchido), o anterior e cobrado por ter
		// sido DESPERTADO e nao por estar nele: quem vai virar Blue esta em SSJ1, nao em SSG --
		// exigir "estar no SSG" tornaria o Blue inalcancavel, que e o defeito que esta bancada ja
		// pegou duas vezes nesta sessao.
		FormaDef? ant = Catalogo.Anterior(d);
		if (ant != null && ant.Id != Catalogo.IdBase)
		{
			bool ok = d.PedeFormaAtual.Length > 0 ? Despertou(ant.Id) : EstaEmOuAcimaDe(ant);
			if (!ok) return RecusaForma.ForaDeOrdem;
		}

		// 6. MAESTRIA -- no degrau anterior, ou na forma que o campo apontar (ver PedeMaestriaDe).
		string deQuem = d.PedeMaestriaDe.Length > 0 ? d.PedeMaestriaDe : ant?.Id ?? Catalogo.IdBase;
		if (d.PedeMaestria > 0 && Maestria.De(deQuem) < d.PedeMaestria)
			return RecusaForma.SemMaestria;

		// 7. AS PORTAS DIVINAS.
		if (d.PedeGodKi >= 0 && perfil.GodKi < d.PedeGodKi) return RecusaForma.SemGodKi;
		if (d.PedeEnergiaUe > 0 && perfil.EnergiaUe < d.PedeEnergiaUe) return RecusaForma.SemEnergia;
		if (d.PedeProficienciaUi > 0 && perfil.ProficienciaUi < d.PedeProficienciaUi)
			return RecusaForma.SemMaestria;

		// 8. A PORTA E O BP BASE. Ver o cabecalho do Catalogo -- gatear pelo expresso deixaria a
		//    propria forma anterior "pagar" o requisito da seguinte.
		//
		//    E A PORTA E DESTE PERSONAGEM: no original cada um sorteia o proprio limiar ao nascer
		//    (`statsaiyan.dm:50-56`), e por isso um Saiyajin vira SSJ antes do irmao com o mesmo
		//    poder. Sem `Limiares` (save antigo, NPC) vale a constante -- o mesmo numero de antes.
		if (d.PortaBp > 0)
		{
			double porta = Limiares?.Porta(d) is > 0 and var p ? p : d.PortaBp;

			// O MENOR CORTE MANDA, e nunca os dois. `fatorDaPorta` e a TENTATIVA de agora (o mestre
			// esta ali, provocando) e `PortasCortadas` e o corte que UM mestre ja fez um dia -- as
			// duas metades sao a MESMA metade, e multiplica-las daria um quarto da porta pra quem
			// desperta assistido duas vezes. Ver `PortasCortadas` pra por que o corte persiste.
			double fator = Math.Min(fatorDaPorta,
				PortasCortadas.Contains(d.IdRede) ? Jandirus.Core.Skills.Discipulado.FatorAssistido : 1);
			if (bpBase < porta * fator) return RecusaForma.SemPoder;
		}

		// entrar numa forma no fio do Ki e cair dela no segundo seguinte
		if (kiFracao < 0.1) return RecusaForma.SemKi;

		// ============================ 9. A RAIVA -- POR ULTIMO, E SO PRO DESPERTAR ============================
		// Ver `Catalogo.RaivaExigida` pra derivacao (e pros dois degraus), e `supersaiyan.dm:163-170`
		// pro original: la o despertar checa o `Emotion` e, quando pega, escreve `hasbeast = 1` e
		// ENTREGA O VERB -- ou seja, a raiva paga a entrada UMA VEZ e depois a forma vira toggle.
		// `!Despertou(d.Id)` e essa frase: `Liberadas` e o `hasbeast` daqui.
		//
		// **COBRAR A RAIVA TODA VEZ SERIA OUTRA REGRA, E UMA REGRA PIOR**: o SSJ1 so voltaria com um
		// segundo amigo morto, e o que o dono descreveu como despertar viraria consumivel. E o
		// precedente ja estava escrito la em cima -- o `SoPorConcessao` do Mistico tambem so vale
		// enquanto ninguem concedeu (passo 0b).
		//
		// `>=` E NAO `==`, e e a metade da regra: quem esta em furia EXTREMA abre o Wrathful tambem,
		// porque quem viu um amigo morrer certamente viu um amigo cair. Com igualdade estrita o luto
		// FECHARIA a linha Legendary -- o oposto do desconto que o dono pediu pra ela.
		//
		// ============================ POR QUE ELA E A ULTIMA DE TODAS ============================
		// Porque a ordem desta funcao E a mensagem (ver o cabecalho), e esta e a unica recusa da
		// lista que o jogador **nao pode ir resolver**. Todas as outras mandam fazer alguma coisa:
		// treinar BP, dominar o degrau anterior, amadurecer o ki divino, recuperar o folego. Se a
		// raiva viesse antes, um Saiyajin com um decimo do BP do SSJ1 ouviria "isso nao se alcanca
		// querendo" e pararia de treinar -- a mensagem certa pra ele e "ainda esta alem do seu
		// alcance", e ela so aparece se a porta de BP for perguntada primeiro.
		//
		// Deixando-a por ultimo, `SemFuria` passa a significar exatamente uma frase: **voce ja tem
		// TUDO o que se busca**. Que e o que o texto do `PorQueNao` diz, e a ordem e o que o torna
		// verdade em vez de promessa.
		// ====================================================================================================
		NivelDeRaiva pedeRaiva = Catalogo.RaivaExigida(d);
		if (pedeRaiva != NivelDeRaiva.Nenhuma && !Despertou(d.Id) && perfil.Raiva < pedeRaiva)
			return RecusaForma.SemFuria;

		return RecusaForma.Pode;
	}

	private static bool Bate(string[] lista, string valor) =>
		lista.Any(x => string.Equals(x, valor, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// O degrau exigido ja foi alcancado?
	///
	/// Os grades contam como SSJ1 pra quem vem depois -- eles SAO o SSJ1, so que inchado. Isso caiu
	/// naturalmente com a <see cref="FormaDef.Ordem"/>: o grade 2 e ordem 15, entao quem esta nele
	/// ja passou da ordem 10.
	/// </summary>
	private bool EstaEmOuAcimaDe(FormaDef exigido)
	{
		FormaDef? agora = Def;
		if (agora == null) return false;
		if (agora.Id == exigido.Id) return true;

		// ============================ ">=" E NAO ">" ============================
		// FORMAS IRMAS OCUPAM A MESMA ORDEM: o Rose e o Blue sao o mesmo degrau da escada divina,
		// um pra cada classe. Com ">" estrito, quem estava em Rose (ordem 20) nunca satisfazia
		// "acima do Blue (ordem 20)", e o Rose 2 ficava INALCANCAVEL -- sem mensagem de erro
		// nenhuma, so uma forma que nunca vinha. Quem achou foi a bancada, subindo a linha inteira.
		//
		// E nao abre buraco: o `exigido` vem sempre do `Catalogo.Anterior`, que so devolve entrada
		// de ordem ESTRITAMENTE menor. Estar na ordem dele significa estar num irmao dele, nunca
		// num degrau saltado.
		// =====================================================================
		return agora.Linha == exigido.Linha && agora.Ordem >= exigido.Ordem;
	}

	/// <summary>
	/// Entra na forma. Devolve se e a PRIMEIRA VEZ QUE A CENA ROLA -- e so isso.
	///
	/// Entrar tambem LIBERA (quem esteve numa forma sabe faze-la), mas as duas coisas saem por
	/// portas diferentes: o retorno e da ESTREIA. Quem entrar numa forma que ja foi liberada por
	/// fora -- o SSJ4 aberto pelo Oozaru Dourado dominado -- ganha a cena aqui, que e exatamente o
	/// que o dono pediu: "a cinematica q fizemos toca na primeira vez q ele se transformar em ssj4
	/// apertando o C".
	/// </summary>
	public bool Entrar(string id)
	{
		Atual = id;
		if (id == Catalogo.IdBase) return false;
		Liberar(id);
		return EstreiaVista.Add(Catalogo.Rede(id));
	}
}
