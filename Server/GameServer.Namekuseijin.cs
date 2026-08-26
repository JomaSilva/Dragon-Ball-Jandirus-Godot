using Godot;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ A FUSAO NAMEKUSEIJIN: ABSORCAO, E ELA E ETERNA ============================
/// O pedido do dono, literal:
///
///   *"faca a fusao namek, q faz o namekuseijin liberar a transformacao super namek e tb ganhar um bonus
///   baseado no namek absorvido na fusao, o outro namek se for jogador, perde o personagem pra sempre (a
///   fusao e eterna), fundir com npc namek ganha BEM menos bp e outros bonus e nao ganha o super namek.
///   namekuseijins ganham super namek aprox no mesmo requisito do SSJ (mantendo a ideia de cada um ter um
///   requisito pessoal, mas em torno de um valor)"*
///
/// TODOS OS NUMEROS moram em <see cref="AbsorcaoNamekuseijin"/> (o Core), e nao aqui. Este arquivo e o
/// ENCANAMENTO: quem pode absorver quem, o que acontece com o personagem do absorvido, e as duas portas
/// pelas quais o Super Namekuseijin se destrava.
///
/// ============================ ELA NAO PRODUZ UMA `FusaoAtiva`, E O PORQUE E LONGO ============================
/// Esta e a divergencia estrutural deste passe, e a explicacao inteira mora no cabecalho de
/// <see cref="AbsorcaoNamekuseijin"/> -- leia-o antes de mexer aqui. O resumo: uma fusao ETERNA nao cabe
/// no motor de fusao viva deste port, porque `GameServer.Persistir` **se recusa a gravar corpo fundido**
/// (e com razao: o save nao sabe descrever a zona do passageiro, os stats emprestados nem o `FuseBuff`).
/// Uma fusao que nao acaba seria um personagem que nunca mais salva -- e, com a regra N3, um passageiro
/// vivo cujo save foi apagado.
///
/// **O proprio DM ja resolve assim.** `Fusion.dm:301-310`: quando a fusao Namekuseijin vira definitiva,
/// o original ASSA o poder no personagem (`Keeper.BP = FusedBaseBP`, `CompletelyPerm = 1`) e a fusao
/// deixa de existir como objeto. Este arquivo e esse estado terminal, alcancado direto.
/// ======================================================================================================
///
/// ============================ O QUE ELA REUSA, E POR QUE ISSO IMPORTA ============================
/// Nada aqui e um segundo caminho de fusao. O convite, a revalidacao no aceite, a distancia de UM tile,
/// a recarga de 1 h e **a cinematica** sao os mesmos do resto do sistema -- e o mais importante: o
/// `TickDaCenaDeFusao` ja tem as quatro guardas de "entre o aceite e a consumacao" (saiu do mundo,
/// morreu, caiu, mudou de zona) e delas **nao sai fusao nenhuma, nem estragada**. A regra que o dono
/// exigiu pra N3 -- *"se o alvo cair/desconectar/morrer entre o aceite e a consumacao, a fusao NAO
/// acontece"* -- ja estava escrita e provada pela Danca; a absorcao entra no mesmo funil e a herda.
///
/// O que diverge e UM ponto: na virada da cena, o tipo Namekuseijin chama
/// <see cref="AbsorverNamekuseijin"/> em vez de `Fundir`.
/// ==========================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// 1. QUEM PODE SER ABSORVIDO
	// =====================================================================
	/// <summary>
	/// ESTE CORPO E UM NPC DO MUNDO QUE DA PRA ABSORVER? -- a regra N4, e ela e o unico lugar do
	/// sistema de fusao que abre a porta pra corpo sem dono.
	///
	/// ============================ ELA E ESTREITA DE PROPOSITO ============================
	/// O portao que barra NPC em toda fusao e o `EhPessoa` (`GameServer.Convivio.cs`), e ele e **a
	/// mesma linha** que impede um pacote forjado de IA de fundir alguem. Abrir "aceita NPC" no lugar
	/// errado abriria os dois.
	///
	/// Entao o que se abriu foi so isto: o **ALVO** de uma fusao Namekuseijin pode ser um
	/// <see cref="Jandirus.Core.Npc.Gente.EhNpcDoMundo"/> -- um corpo que saiu de um MOLDE e nao tem
	/// dono na tela. Quem CONVIDA continua tendo que ser pessoa, sempre, nos dois caminhos.
	///
	/// **O terceiro grupo fica de fora, e ele e o perigoso**: o clone da mente e o boneco do corpo
	/// largado nao sao jogador NEM NPC do mundo (ver o cabecalho de `Core/Npc/Gente.cs`). Os dois
	/// carregam a Ficha de uma pessoa viva -- absorver um deles seria absorver o dono dela por uma
	/// porta lateral, e o BP contado duas vezes. `EhNpcDoMundo` ja os corta pelas duas pernas.
	/// ==================================================================================
	/// </summary>
	private bool EhNamekNpcAbsorvivel(ServerPlayer alvo) =>
		EhNpcDoMundo(alvo) && !alvo.Ficha.dead && Fusao.EhNamekuseijin(alvo.Race);

	// =====================================================================
	// 2. A ABSORCAO
	// =====================================================================
	/// <summary>
	/// ============================ O GESTO INTEIRO, E ELE E IRREVERSIVEL ============================
	/// Chamado de UM lugar: a virada da cinematica (`TickDaCenaDeFusao`), quando o tipo e
	/// <see cref="TipoDeFusao.Namek"/>. Nao ha segundo chamador de producao, e nao deve haver -- e o
	/// que garante que toda absorcao atravessou o convite, o aceite e as quatro guardas da cena.
	///
	/// A ORDEM AQUI E A REGRA, e ela foi escrita de tras pra frente a partir do que pode dar errado:
	///
	///   1. **conferir se da pra apagar ANTES de mudar qualquer coisa.** Se o personagem do absorvido
	///      nao puder ser identificado com certeza, a absorcao NAO acontece -- ninguem ganha poder e
	///      ninguem perde personagem. Um ganho que ficasse de pe com o apagamento falhando seria a pior
	///      metade das duas;
	///   2. o bonus entra no dono (BP, stats, skills, Super Namekuseijin);
	///   3. o absorvido some -- o NPC sai do mundo, o jogador perde o personagem e cai;
	///   4. a recarga de 1 h e cobrada de quem absorveu (`Fusion.dm:320`).
	/// ==========================================================================================
	/// </summary>
	private void AbsorverNamekuseijin(ServerPlayer dono, ServerPlayer absorvido)
	{
		bool ehJogador = EhPessoa(absorvido);

		// ---- 0. O ALVO E MESMO UMA DAS DUAS COISAS QUE SE ABSORVE? -------------------------------
		// ============================ ISTO E CINTO **E** SUSPENSORIO, E DE PROPOSITO ============================
		// O portao ja recusou o terceiro grupo la no `Fusao.Avaliar` (o clone da mente e o boneco do
		// corpo largado nao sao pessoa NEM NPC do mundo). Esta linha o recusa DE NOVO, no instante da
		// consumacao, porque o que esta em jogo aqui nao e um convite recusado: e um corpo que carrega a
		// Ficha de uma pessoa VIVA sendo comido, ou o `RemoverNpc` tirando do mundo o corpo em que
		// alguem esta meditando.
		//
		// O `else` deste metodo trata "nao e jogador" como "e NPC" -- e um `else` que assume e
		// exatamente onde um caminho novo (um corpo de bancada, um verb de admin, uma raca futura) entra
		// sem ninguem perceber. Aqui ele deixa de assumir.
		// ==================================================================================================
		if (!ehJogador && !EhNamekNpcAbsorvivel(absorvido))
		{
			Avisar(dono, $"nao ha o que absorver em {absorvido.Name}.");
			GD.PushWarning($"[server] ABSORCAO NAMEK: {absorvido.Name} chegou a consumacao sem ser nem "
						 + "pessoa nem NPC do mundo -- o portao do convite deixou passar.");
			return;
		}

		// ---- 1. A PORTA DE SEGURANCA -----------------------------------------------------------
		// Ver <see cref="PodeApagarOPersonagem"/>: ela responde "eu sei EXATAMENTE qual save some se
		// isto continuar?". Um "nao sei" aqui cancela a absorcao inteira.
		if (ehJogador && PodeApagarOPersonagem(absorvido, out string porque) == null)
		{
			Avisar(dono, $"a fusao nao se completa: {porque}");
			Avisar(absorvido, $"a fusao nao se completa: {porque}");
			GD.Print($"[server] ABSORCAO NAMEK CANCELADA ({dono.Name} + {absorvido.Name}): {porque}");
			return;
		}

		double bpAntes = dono.Ficha.BP;
		double bpDele = absorvido.Ficha.BP;

		// ---- 2. O PODER -- `AbsorcaoNamekuseijin.BpDepoisDeAbsorver` -------------------------------
		// ============================ ELE VAI NO `BP` E NAO NO `FuseBuff` ============================
		// O `FuseBuff` e o canal da fusao TEMPORARIA: ele soma na base e o `Separar` o devolve. Aqui
		// nao ha separacao e nao ha o que devolver, entao o numero vai pro BP de verdade -- que e
		// literalmente o `Keeper.BP = FusedBaseBP` do `Fusion.dm:308`, a linha com que o DM torna a
		// fusao Namekuseijin definitiva.
		//
		// **E ELE NAO PASSA PELO `CapCheck`**, de proposito. O `CapCheck` e o teto do TREINO (o funil
		// unico do ganho por esforco, `Fighter.Training.cs:16`), e isto nao e treino: e o mesmo tipo de
		// concessao que o desejo de poder e o Zenkai fazem -- recompensa, e nao ganho por hora.
		// =========================================================================================
		dono.Ficha.BP = AbsorcaoNamekuseijin.BpDepoisDeAbsorver(bpAntes, bpDele, ehJogador);

		// ---- 3. OS OUTROS BONUS (so quando o absorvido e JOGADOR -- regra N4) ----------------------
		if (AbsorcaoNamekuseijin.HerdaOsStats(ehJogador))
		{
			// O MAIOR DE CADA, e e o MESMO passo 2 do `Fundir` -- a mesma leitura de stats CRUS pelo
			// mesmo `StatsDe`/`PorStats`. Ver o `Fundir` pro porque de serem os crus e nao os efetivos.
			double[] meus = StatsDe(dono.Ficha), dele = StatsDe(absorvido.Ficha);
			for (int i = 0; i < meus.Length; i++) meus[i] = Math.Max(meus[i], dele[i]);
			PorStats(dono.Ficha, meus);

			// A GRAVIDADE DOMINADA TAMBEM SOBE -- `Nameks.dm:193` do DU
			// (`if(P.gravity_mastered < gravity_mastered) P.gravity_mastered = gravity_mastered`). Ela
			// nao esta no `StatsDe` porque nao e um dos oito stats de combate; e um treino acumulado, e
			// o desenho do "puxao pra cima" e justamente que nenhum eixo do absorvido se perca.
			dono.Ficha.GravMastered = Math.Max(dono.Ficha.GravMastered, absorvido.Ficha.GravMastered);
		}

		int skillsGanhas = 0;
		if (AbsorcaoNamekuseijin.HerdaAsSkills(ehJogador) && dono.Livro != null && absorvido.Livro != null)
			foreach (string path in absorvido.Livro.Aprendidas)
			{
				if (dono.Livro.Sabe(path)) continue;
				// `Dar` E NAO `DarComoEnsinada`: ninguem ENSINOU nada aqui. A distincao existe no livro
				// (o `wastaught` do DM) e decide quem pode repassar a skill adiante.
				dono.Livro.Dar(path);
				skillsGanhas++;
			}

		// ---- 4. O SUPER NAMEKUSEIJIN (N1) ----------------------------------------------------------
		bool ganhouAForma = AbsorcaoNamekuseijin.DestravaOSuperNamekuseijin(ehJogador)
						 && DarOSuperNamekuseijin(dono);

		// ---- 5. O CORPO SE REFAZ E A CURA VEM JUNTO -----------------------------------------------
		// `Nameks.dm:194-195` do DU: `if(P.Health<100) P.Health=100` e `if(P.Ki<P.max_ki) P.Ki=P.max_ki`.
		// Aqui a cura sai do mesmo `CorpoNovoDaFusao` que a fusao ja usa (`fusion_fresh_body`,
		// `Fusion.dm:36-39`) -- e ele NAO fotografa os membros que faltavam antes, porque nao ha
		// separacao pra devolver a amputacao. Quem absorve sai inteiro, e fica inteiro.
		CorpoNovoDaFusao(dono);
		dono.Ficha.Ki = dono.Ficha.MaxKi;

		AplicarPoderes(dono);
		AplicarEfeitos(dono);
		MandarSkills(dono, forcar: true);
		MandarFicha(dono);
		MandarAtributos(dono);

		// ---- 6. A RECARGA -- `Fusion.dm:320` -------------------------------------------------------
		// SO DE QUEM ABSORVEU. O `Separar` cobra dos dois porque os dois voltam a existir; aqui o outro
		// nao volta, e cobrar recarga de um personagem apagado nao quer dizer nada.
		CobrarARecargaDeFusao(dono, NowMs());

		// ---- 7. O ABSORVIDO SOME --------------------------------------------------------------------
		string nomeDele = absorvido.Name;
		if (ehJogador) ApagarOPersonagemParaSempre(absorvido, $"absorvido por {dono.Name}");
		else RemoverNpc(absorvido);

		// ---- 8. O QUE TODO MUNDO OUVE ---------------------------------------------------------------
		foreach (ServerPlayer o in ZoneList(dono.Zone.Hash))
			Avisar(o, $"{dono.Name} absorve {nomeDele} -- os dois viram um so, e nao ha volta.");

		Avisar(dono, ehJogador
			? $"voce absorveu {nomeDele}. O poder dele agora e SEU, e e pra sempre "
			+ $"(BP {bpAntes:N0} -> {dono.Ficha.BP:N0}"
			+ (skillsGanhas > 0 ? $", {skillsGanhas} habilidade(s) dele" : "")
			+ (ganhouAForma ? ", e o Super Namekuseijin despertou em voce" : "") + ")."
			: $"voce absorveu {nomeDele}, que nao era ninguem: o corpo dele mal tinha o que dar "
			+ $"(BP {bpAntes:N0} -> {dono.Ficha.BP:N0}). Fundir com os seus e outra coisa.");

		GD.Print($"[server] ABSORCAO NAMEK ({(ehJogador ? "JOGADOR" : "NPC")}): {dono.Name} + {nomeDele} "
			   + $"| BP {bpAntes:N0} -> {dono.Ficha.BP:N0} (dele {bpDele:N0}) "
			   + $"| {skillsGanhas} skills | super namek: {(ganhouAForma ? "sim" : "nao")}");
	}

	// =====================================================================
	// 3. O SUPER NAMEKUSEIJIN -- **UMA PORTA, DOIS CAMINHOS**
	// =====================================================================
	/// <summary>
	/// DESTRAVA O SUPER NAMEKUSEIJIN PRA ESTE CORPO. Devolve `true` se foi ESTA chamada que destravou
	/// (pra quem quiser anunciar a conquista uma vez so).
	///
	/// ============================ E POR QUE ISTO E DAR UMA SKILL ============================
	/// A forma `snamek` e UMA entrada do catalogo e o portao dela e UM: `PedeFlag("snamek")`
	/// (`Formas.cs`). Quem escreve essa flag e o `after_learn` da skill `/datum/skill/namek/SuperNamek`
	/// -- **e so ele**, no DM (`namekian.dm:37-38`) e aqui (o canal ATRIBUICAO do `EfeitosDeSkill`,
	/// alimentado por `skills.json`). Entao "destravar a forma" e, literalmente, "por essa skill no
	/// livro".
	///
	/// **ESCREVER `snamek = 1` NA MAO NAO FUNCIONA, E FALHA CALADO** -- medido: `Fighter.FlagsDeSkill` e
	/// RECONSTRUIDO DO ZERO a partir do livro a cada `AplicarEfeitos` (`EfeitosDeSkill.cs:159-167`), e
	/// `AplicarEfeitos` roda no login e em toda compra de skill. O bit escrito a mao sumiria sozinho, e
	/// o jogador perderia a forma sem uma linha na tela dizendo por que.
	///
	/// E A SKILL PERSISTE de graca: ela vai pro `CharacterSave.Skills` como qualquer outra. Um campo
	/// novo no save faria a mesma coisa custando um campo, um leitor e uma migracao.
	/// ====================================================================================
	/// </summary>
	private bool DarOSuperNamekuseijin(ServerPlayer pl)
	{
		if (pl.Livro == null) return false;
		if (pl.Livro.Sabe(AbsorcaoNamekuseijin.PathDaSkillDoSuperNamekuseijin)) return false;

		pl.Livro.Dar(AbsorcaoNamekuseijin.PathDaSkillDoSuperNamekuseijin);
		return true;
	}

	/// <summary>
	/// ============================ N5: O DESPERTAR PELO PROPRIO PODER ============================
	/// Pedido do dono: *"namekuseijins ganham super namek aprox no mesmo requisito do SSJ (mantendo a
	/// ideia de cada um ter um requisito pessoal, mas em torno de um valor)"*.
	///
	/// O NUMERO nao esta aqui: ele e o `snamekat` PESSOAL, sorteado no nascimento por CLA e gravado no
	/// save -- ver `LimiaresPessoais.RolarNamek`, onde a faixa e a decisao moram. Aqui so se pergunta
	/// se o corpo ja o cruzou.
	///
	/// ============================ POR QUE ISTO PRECISA EXISTIR ============================
	/// Porque o Super Namekuseijin do DM se COMPRA (2 pontos de marco na arvore racial) e o SSJ nao se
	/// compra: um Saiyajin vira Super Saiyajin por ter chegado no `ssjat` dele, e mais nada. "No mesmo
	/// requisito do SSJ" so quer dizer alguma coisa se o caminho tambem for o mesmo -- **poder**. Com a
	/// skill sendo a unica porta, um Namekuseijin que treinasse a vida inteira sem gastar aqueles dois
	/// pontos nunca se transformaria, e nada na tela diria isso a ele.
	///
	/// A skill continua valendo e continua sendo o caminho do original: quem a comprou ja tem a forma
	/// antes de chegar no limiar. Os dois caminhos escrevem a MESMA coisa (ver
	/// <see cref="DarOSuperNamekuseijin"/>), entao nao ha duas formas nem dois estados que possam
	/// divergir -- ha uma porta com tres caminhos (a compra, este, e a absorcao).
	/// ==================================================================================
	///
	/// ============================ ONDE ELE RODA, E POR QUE ALI ============================
	/// No topo do <see cref="Treinar"/>, ao lado do marco de ascensao, e pelo mesmo argumento que esta
	/// escrito la: o `Stats()` do DM chama o `Auto_Gain()` **todo tique, fora do galho de treino**, e e
	/// dele que sai o `bp_milestone_check_ascension`. Aqui a cadencia e a mesma (5 Hz por jogador) e a
	/// razao tambem: quem cruza um patamar precisa ser AVISADO na hora, e nao na proxima vez que
	/// apertar alguma coisa.
	/// ==================================================================================
	/// </summary>
	private void ConferirODespertarDoSuperNamekuseijin(ServerPlayer pl)
	{
		// TRES PERGUNTAS BARATAS PRIMEIRO -- este metodo roda 5x por segundo por jogador.
		if (!Fusao.EhNamekuseijin(pl.Race)) return;
		if (pl.Livro == null) return;
		if (pl.Livro.Sabe(AbsorcaoNamekuseijin.PathDaSkillDoSuperNamekuseijin)) return;

		// A PORTA E A PESSOAL, e ela sai do MESMO lugar que a forma consulta (`LimiaresPessoais.Porta`,
		// pela `ChaveDoLimiar` da entrada do catalogo). Ler o `snamekat` na mao aqui seria a segunda
		// copia da regra -- e a que nao saberia do corte de mestre nem do que vier depois.
		Jandirus.Core.Forms.FormaDef? d = Jandirus.Core.Forms.Catalogo.Def(IdDoSuperNamekuseijin);
		if (d == null) return;

		double porta = pl.Forma.PortaDeBp(d);
		if (porta <= 0 || pl.Ficha.BP < porta) return;

		if (!DarOSuperNamekuseijin(pl)) return;

		// O CORPO PRECISA SABER NA HORA: sem esta trinca a skill entraria no livro e **nada
		// aconteceria** -- sem flag, sem forma na aba, sem botao. E o mesmo alerta que o ensino de
		// skill ja escreve, e o defeito que ele descreve e exatamente este.
		AplicarPoderes(pl);
		AplicarEfeitos(pl);
		MandarSkills(pl, forcar: true);
		MandarAtributos(pl);

		Avisar(pl, "alguma coisa se rompe por dentro: o seu poder passou do ponto em que o corpo de um "
				 + "Namekuseijin aguenta ficar do mesmo tamanho. O SUPER NAMEKUSEIJIN despertou.");
		GD.Print($"[server] SUPER NAMEKUSEIJIN despertou em {pl.Name} "
			   + $"(BP {pl.Ficha.BP:N0} >= porta pessoal {porta:N0})");
	}

	/// <summary>O id da forma no catalogo (`Formas.cs`). Uma escrita so -- tres consumidores neste arquivo.</summary>
	internal const string IdDoSuperNamekuseijin = "snamek";

	// =====================================================================
	// 4. N3 -- O PERSONAGEM QUE SE PERDE PARA SEMPRE
	// =====================================================================
	/// <summary>
	/// ============================ DA PRA APAGAR O PERSONAGEM DESTE CORPO? ============================
	/// Devolve a CONTA em que ele mora, ou nulo com o motivo. **Chamada ANTES de qualquer efeito da
	/// absorcao** -- ver o passo 1 do <see cref="AbsorverNamekuseijin"/>.
	///
	/// ============================ O MOLDE E O `DeleteChar`, E O GATILHO E O OPOSTO DELE ============================
	/// O unico gesto deste port inteiro que apaga um personagem e o `DeleteChar` (a tela de excluir), e
	/// o MECANISMO dele e o certo aqui: `Slots[slot] = null`, gravar, `PurgarAssinatura`, e um log do
	/// que se perdeu. Mas as CINCO guardas dele sao todas de "esta pessoa, nesta tela, digitando o
	/// proprio nome, com o mundo vazio" -- e a absorcao e o contrario em todos os eixos: quem perde o
	/// personagem e OUTRA pessoa, os dois corpos estao vivos no mundo, e nao ha campo de digitar nada.
	///
	/// Entao a guarda de identidade foi TRADUZIDA em vez de copiada. La o jogador digita o nome e o
	/// servidor confere; aqui o servidor confere que **o save daquele slot e o deste corpo**, pelo
	/// nome. E a mesma pergunta ("eu sei exatamente qual save some?") feita com o que existe.
	///
	/// A guarda que NAO pode valer aqui e a quinta do `DeleteChar` ("saia do mundo antes de apagar"):
	/// ela existe pra impedir que se apague um save por baixo de um corpo vivo, e este gesto **e**
	/// apagar um save por baixo de um corpo vivo. O que a substitui e o par
	/// <see cref="ServerPlayer.PersonagemConsumido"/> + a queda imediata do peer: o corpo deixa de
	/// poder ser gravado no mesmo instante em que o save morre, e some do mundo em seguida.
	/// ========================================================================================================
	/// </summary>
	private AccountSave? PodeApagarOPersonagem(ServerPlayer alvo, out string porque)
	{
		porque = "";

		if (alvo.PersonagemConsumido)
		{
			porque = $"{alvo.Name} ja foi consumido por outra fusao.";
			return null;
		}

		// SEM (CONTA, SLOT) NAO HA O QUE APAGAR. Corpo de bancada, clone e boneco chegam aqui com
		// `Slot < 0` ou conta vazia -- e o `Persistir` ja os ignora pelo mesmo motivo.
		if (alvo.Conta.Length == 0 || alvo.Slot < 0 || alvo.Slot >= AccountStore.Slots)
		{
			porque = $"{alvo.Name} nao tem um personagem gravado que possa ser consumido.";
			return null;
		}

		AccountSave? acc = ContaViva(alvo.Conta) ?? _store?.Carregar(alvo.Conta);
		if (acc == null)
		{
			porque = $"a conta de {alvo.Name} nao foi encontrada -- e nao se apaga o que nao se achou.";
			return null;
		}

		CharacterSave? c = acc.Slots[alvo.Slot];
		if (c == null)
		{
			porque = $"o personagem de {alvo.Name} nao esta gravado em slot nenhum.";
			return null;
		}

		// ============================ A GUARDA DE IDENTIDADE, TRADUZIDA DO `DeleteChar` ============================
		// La e o nome DIGITADO que tem que bater com o do save; aqui e o nome do CORPO. Sem ela, um
		// `ServerPlayer` com o slot apontando pro lugar errado (um corpo forjado, um verb de admin que
		// mexeu na ficha) apagaria o personagem errado da conta certa -- e nao ha desfazer.
		// ======================================================================================================
		if (!string.Equals(c.Nome, alvo.Name, StringComparison.OrdinalIgnoreCase))
		{
			porque = $"o slot {alvo.Slot + 1} de {alvo.Conta} guarda '{c.Nome}', e nao {alvo.Name} -- "
				   + "a fusao nao consome um personagem que ela nao conseguiu identificar.";
			return null;
		}

		return acc;
	}

	/// <summary>A copia VIVA desta conta (a de quem esta jogando ou na selecao), se houver.</summary>
	private AccountSave? ContaViva(string conta) =>
		_contasEmprestadasDeBancada.GetValueOrDefault(conta)
		?? _contas.Values.Concat(_logados.Values)
				  .FirstOrDefault(v => string.Equals(v.Conta, conta, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// ============================ APAGA O PERSONAGEM. NAO HA DESFAZER. ============================
	/// A regra N3: *"o outro namek se for jogador, perde o personagem pra sempre (a fusao e eterna)"*.
	///
	/// ============================ E A CONTA CONTINUA VIVA -- CONFERIDO ============================
	/// O que morre e o PERSONAGEM (um dos tres slots), e nao a conta. Quem foi absorvido cai na tela de
	/// login, entra de novo com a mesma senha e cria outro personagem no slot que vagou. Isso e o
	/// `DeleteChar` faz e este metodo faz igual: `acc.Slots[slot] = null` e nada mais -- `acc.Banida`
	/// nao e tocada, o arquivo da conta continua no disco, e os outros dois slots continuam onde
	/// estavam.
	/// ======================================================================================
	///
	/// ============================ A ORDEM AQUI E CONTRA O SALVAMENTO PERIODICO ============================
	/// O `Persistir` roda a cada 2 minutos e roda de novo no `Drop`. Se o corpo continuasse gravavel
	/// depois do apagamento, a primeira gravacao o **ressuscitaria** -- e o jogador veria o personagem
	/// voltar do nada. Por isso <see cref="ServerPlayer.PersonagemConsumido"/> e escrito **antes** de
	/// tudo, e nao depois: ele fecha a porta do save no mesmo gesto que apaga o arquivo.
	/// ================================================================================================
	/// </summary>
	private void ApagarOPersonagemParaSempre(ServerPlayer alvo, string motivo)
	{
		AccountSave? acc = PodeApagarOPersonagem(alvo, out string porque);
		if (acc == null)
		{
			// NAO DEVE ACONTECER: o chamador ja perguntou antes de mexer em qualquer coisa. Se
			// acontecer, o corpo continua no mundo e o log grita -- que e melhor que apagar no escuro.
			GD.PushWarning($"[server] ABSORCAO: nao deu pra apagar o personagem de {alvo.Name}: {porque}");
			return;
		}

		CharacterSave? c = acc.Slots[alvo.Slot];

		// A PORTA DO SAVE FECHA PRIMEIRO -- ver o bloco acima.
		alvo.PersonagemConsumido = true;

		acc.Slots[alvo.Slot] = null;

		// AS OUTRAS COPIAS VIVAS DA MESMA CONTA TAMBEM. O `AdminBanir` ja faz isto pelo mesmo motivo:
		// `_contas` e `_logados` podem guardar objetos diferentes da mesma conta (uma segunda conexao,
		// a tela de selecao), e a que ficasse com o slot cheio regravaria o personagem por cima.
		EmTodaCopiaViva(acc.Conta, v =>
		{
			if (alvo.Slot < v.Slots.Length) v.Slots[alvo.Slot] = null;
		});

		GravarAConta(acc);

		// O DISCIPULADO MORRE COM O PERSONAGEM -- o `mst_purge_sig` (`MasterStudent.dm:123`), e aqui ele
		// e obrigatorio pelo mesmo motivo do `DeleteChar`: a assinatura e `hash(conta, slot)`, entao o
		// PROXIMO personagem criado neste slot nasceria com o mestre e os alunos de quem foi absorvido.
		PurgarAssinatura(ServerPlayer.AssinaturaDe(acc.Conta, alvo.Slot));

		// O LOG GUARDA O QUE FOI PERDIDO, palavra por palavra como o `DeleteChar`: nao da pra desfazer,
		// mas da pra saber o que havia -- e um "sumiu meu personagem" sem uma linha no log e impossivel
		// de responder.
		GD.Print($"[server] {acc.Conta} PERDEU o slot {alvo.Slot + 1}: '{c?.Nome ?? alvo.Name}' "
			   + $"({c?.Raca ?? alvo.Race}, BP {c?.Ficha.BP ?? alvo.Ficha.BP:0}) -- {motivo}");

		Avisar(alvo, "voce deixou de existir como pessoa: o seu corpo, a sua forca e o seu nome agora "
				   + "sao de outro. O personagem se foi PARA SEMPRE, e nao ha desfazer. A sua CONTA "
				   + "continua sua -- entre de novo e crie outro.");

		// E O CORPO SAI DO MUNDO. `Disconnect()` nao derruba na hora (o evento chega no proximo
		// `PollEvents`), e ate la o corpo continua de pe -- mas ja com `PersonagemConsumido`, ou seja
		// sem poder ser gravado. O `Drop` que chegar depois faz a limpeza inteira pelo caminho normal.
		alvo.Peer?.Disconnect();
	}

	// =====================================================================
	// 5. O PALCO -- pra bancada exercitar o apagamento SEM tocar no disco do dono
	// =====================================================================
	/// <summary>
	/// As contas que a bancada EMPRESTOU pro apagamento achar, por nome. Ver
	/// <see cref="PalcoDeApagamentos"/>.
	/// </summary>
	private readonly Dictionary<string, AccountSave> _contasEmprestadasDeBancada =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>GRAVA A CONTA -- o unico ponto de escrita em disco deste arquivo.</summary>
	private void GravarAConta(AccountSave acc) => _store?.Gravar(acc);

	/// <summary>
	/// ============================ O PALCO DE APAGAMENTOS ============================
	/// Irmao do `PalcoDeMortes` (`GameServer.Destruicao.cs`) e existe pelo mesmo motivo, so que a coisa
	/// que ele protege e mais grave: **a pasta de saves do dono**. Ontem uma bancada gravou a morte da
	/// Terra no save real dele; uma bancada de N3 que rodasse solta APAGARIA UM PERSONAGEM -- e nao ha
	/// desfazer pra isso.
	///
	/// ============================ ELE TROCA A PASTA, E NAO DESLIGA A ESCRITA ============================
	/// A primeira versao deste palco desviava o `GravarAConta` -- e **ela vazou na primeira rodada**: o
	/// `Persistir` grava pelo `_store` DIRETO, sem passar por aquele metodo, e a bancada deixou um
	/// `bancada_fusao2_93043.json` na pasta do dono. O buraco nao era a linha esquecida: era a ideia de
	/// caçar chamadores.
	///
	/// Entao o palco troca o **destino**: `_store` passa a apontar pra uma pasta temporaria do sistema,
	/// criada aqui e apagada no `Dispose`. Todo mundo que grava continua gravando, de verdade, com o
	/// codigo de producao -- so que noutro lugar. Nao ha chamador a lembrar, e um caminho de escrita
	/// NOVO nasce protegido.
	///
	/// **E isso e o que mantem as provas honestas.** Desligar a escrita deixaria a prova "o `Persistir`
	/// NAO ressuscita o personagem apagado" verde por acidente -- ela ficaria verde num mundo em que o
	/// `Persistir` nao grava nada. Com a pasta trocada, ele grava mesmo, e o slot continua vazio pelo
	/// motivo certo (o `PersonagemConsumido`).
	/// ================================================================================================
	///
	/// O QUE ELE **NAO** DESVIA: o `Slots[slot] = null`, o `PersonagemConsumido`, o `PurgarAssinatura`,
	/// o `EmTodaCopiaViva` e o log sao o codigo de producao, rodando sobre objetos de verdade -- e e
	/// neles que a bancada le o resultado.
	/// ==============================================================================
	/// </summary>
	internal sealed class PalcoDeApagamentos : IDisposable
	{
		private readonly GameServer _s;
		private readonly AccountStore? _storeAntes;
		private readonly string _pastaTemporaria;
		private readonly Dictionary<string, AccountSave> _fotoDasContas;
		private bool _fechado;

		internal PalcoDeApagamentos(GameServer s)
		{
			_s = s;
			_fotoDasContas = new Dictionary<string, AccountSave>(s._contasEmprestadasDeBancada,
																 StringComparer.OrdinalIgnoreCase);

			_storeAntes = s._store;
			_pastaTemporaria = System.IO.Path.Combine(
				System.IO.Path.GetTempPath(), "jandirus-palco-" + Guid.NewGuid().ToString("N"));
			System.IO.Directory.CreateDirectory(_pastaTemporaria);
			s._store = new AccountStore(_pastaTemporaria);
		}

		/// <summary>
		/// A PASTA TEMPORARIA PRA ONDE ESTE PALCO DESVIOU TODA A ESCRITA.
		///
		/// **Ela e medivel de proposito**, pelo mesmo argumento do `PalcoDeMortes.MatouAqui`: um crivo
		/// que nunca corta e indistinguivel de crivo nenhum. A bancada compara isto com a pasta de
		/// verdade -- se um dia o palco parar de trocar o destino, a prova fica vermelha em vez de a
		/// pasta do dono ficar suja.
		/// </summary>
		internal string PastaDeTeste => _pastaTemporaria;

		/// <summary>Poe uma conta de mentira na mesa, com um personagem no slot pedido.</summary>
		internal AccountSave Emprestar(string conta, int slot, CharacterSave personagem)
		{
			var acc = new AccountSave { Conta = conta };
			acc.Slots[slot] = personagem;
			_s._contasEmprestadasDeBancada[conta] = acc;
			return acc;
		}

		public void Dispose()
		{
			if (_fechado) return;
			_fechado = true;

			_s._contasEmprestadasDeBancada.Clear();
			foreach ((string k, AccountSave v) in _fotoDasContas) _s._contasEmprestadasDeBancada[k] = v;

			// A PASTA DE VERDADE VOLTA **ANTES** de a temporaria sumir: se o `Delete` estourar, o
			// servidor ja esta apontando pro lugar certo. Um palco que morresse deixando o `_store`
			// numa pasta apagada seria pior que o vazamento que ele veio evitar.
			_s._store = _storeAntes;

			try { System.IO.Directory.Delete(_pastaTemporaria, recursive: true); }
			catch (Exception e) { GD.PushWarning($"[bancada] o palco nao conseguiu limpar '{_pastaTemporaria}': {e.Message}"); }
		}
	}

	internal PalcoDeApagamentos PalcoDeApagamentosDeBancada() => new(this);
}
