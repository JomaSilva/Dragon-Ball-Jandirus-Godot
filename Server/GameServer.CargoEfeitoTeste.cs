using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS TRES FAMILIAS QUE FALTAVAM NA `--cargovivo` -- roda dentro dela, no mesmo `--formasteste`.
///
/// ============================ POR QUE UM ARQUIVO NOVO, E NAO MAIS TRES SECOES LA ============================
/// A `--cargovivo` mede o CAMINHO do kit (quem ganha, quem nao ganha, o portao abre, largar desfaz) e
/// ja tem 687 linhas de argumento sobre esse caminho. O que entra aqui e outra pergunta, e ela e a que
/// o dono fez em voz alta: **"o verbo FAZ EFEITO?"**. Sao coisas diferentes o bastante pra merecerem
/// cabecalho proprio -- e a prova disso e que este arquivo forja corpos que aquele nao podia forjar
/// (ver a armadilha do `Peer` emprestado, logo abaixo).
///
/// As tres familias, e o buraco que cada uma fecha:
///
///   5. **O EFEITO NOMEADO, UM POR VERBO VIVO.** A `--cargovivo` afirmava, dos 26 verbos que os kits
///      entregam vivos, que **o portao abre**. Portao aberto nao e efeito: este projeto ja teve 14
///      tecnicas escritas, compiladas, com bancada -- e inalcancaveis. Aqui cada um dos 26 tem UMA
///      linha com o efeito escrito por extenso ("nasce o raio chamado Galick Ho", "a idade cai nos
///      DOIS campos", "o corpo atravessa pro Ceu"), e a lista de 26 e DERIVADA do censo: um verbo de
///      cargo que ganhe corpo amanha reprova esta bancada ate ter a linha dele.
///
///   6. **O KIT CHEGA NA HORA DE REIVINDICAR.** A janela de ate 1 s entre "VOCE E O NOVO EREMITA
///      TARTARUGA" e o Kamehameha aparecer foi achada pela `--cargovivo` e consertada em
///      `GameServer.Ranks.cs` -- **e nenhuma linha a media**. As provas de la chamam `TickDosCargos`
///      logo depois de `ReivindicarCargo`, entao continuariam verdes com o conserto arrancado. Aqui
///      nao ha tique nenhum entre a porta e a medicao.
///
///   7. **O VERBO NAO SOME NO TIQUE SEGUINTE.** A armadilha ja conhecida: `/datum/skill/rank/*` posta
///      no livro pela porta errada e REVOGADA um tique depois, porque a reconciliacao tira o que so um
///      cargo daria e ninguem ensinou. A familia prova as duas metades -- a porta certa sobrevive, a
///      errada some -- e prova tambem que o kit inteiro aguenta cinco tiques seguidos.
/// ==========================================================================================================
///
/// ============================ COMO CADA FAMILIA REPROVA -- MEDIDO, E NAO SUPOSTO ============================
/// Bancada que ninguem viu ficar vermelha e bancada que ninguem sabe se funciona. Cada familia daqui
/// (e as quatro da `--cargovivo`) teve o defeito INJETADO no codigo de producao, a bancada rodada, e o
/// resultado esta escrito abaixo com o placar. Toda injecao foi desfeita depois; nenhuma sobrou.
///
///     familia                          o defeito injetado                                    placar `[cargo]`
///     -------------------------------  ----------------------------------------------------  ----------------
///     1. quem TEM destrava,            `ReconciliarDadiva` entrega o kit do Eremita a         78 OK / 134
///        quem NAO tem, nao             TODO MUNDO (`De("turtle")` no lugar do cargo real)
///                                      -> "o controle SEM CARGO recebeu junto", 4x por cargo
///
///     2. o verbo FAZ EFEITO            `BusterShellG5` dispara UMA bola em vez de quatro      211 OK / 1
///                                      -> so a linha do efeito cai. **A linha do PORTAO
///                                      ("'Buster Shell' e USAVEL") continua VERDE** -- que e
///                                      exatamente a familia de defeito que so esta secao ve
///
///     3. nao some no tique seguinte    `DadivaDeCargo.Revogavel` deixa de olhar o             211 OK / 1
///        (a porta CERTA sobrevive)     `FoiEnsinada` -> "a skill ENSINADA foi revogada"
///
///        (a porta ERRADA some)         `Revogavel` devolve `false` sempre                     159 OK / 53
///                                      -> "a skill ficou -- entao a reconciliacao parou de
///                                      revogar", **e a metade da porta certa fica VERDE**
///
///     4. o kit chega na hora           a linha `ReconciliarDadiva(pl)` sai do                 210 OK / 2
///                                      `ReivindicarCargo` -> caem as DUAS linhas novas...
///                                      **e a secao 3 inteira da `--cargovivo` continua
///                                      VERDE**, que e o motivo de esta familia existir
///
///     5. sair do cargo tira            o mesmo `Revogavel` sempre `false` da linha acima      159 OK / 53
///                                      -> 49 linhas "largar o cargo TIRA 'X'" caem
///
///     6. a condicao extra e cobrada    o bloco `if (Cargos.OqueFalta(...))` sai do            208 OK / 4
///                                      `ReivindicarCargo` -> caem os tres ferrolhos
///                                      (karma, BP, God Ki) e o ponto exato do karma
///
///     +. a delegacao nao apodrece      a frase "REVIVE DE CARGO: a alma volta a vida" e       211 OK / 1
///                                      renomeada na `--alemteste` -> "a afirmacao sumiu de la"
/// ========================================================================================================
///
/// ============================ A ARMADILHA DO `Peer` EMPRESTADO, DE NOVO E COM UM LADO A MAIS ============================
/// O cabecalho da `--cargovivo` ja explica: corpo com o `Peer` do host **nao pode passar por**
/// `Persistir`, senao grava por cima do personagem do host no disco. Tres verbos deste lote persistem
/// (`Keep_Body`, `Restore_Youth` no aceite, `Revive`), e a saida aqui e a mesma que a `--alemteste`
/// usa pelo outro lado: **o ALVO nasce com `Peer` nulo**, e o `Persistir` sai na primeira linha
/// (`GameServer.cs:4097` -- `pl.Peer == null` e sem conta explicita, volta).
///
/// E ha o lado a mais, que e o motivo de o alvo nao ser simplesmente o `controle`: `Peer` nulo faz
/// `EhJogador` devolver falso, mas **`EhPessoa` continua verdadeiro** (`Assinatura` sai de
/// `Conta`+`Slot`, e `EhNpcDoMundo` pede `papel != null`). E exatamente o corpo de que os verbos com
/// alvo precisam -- `Keep_Body`, `Restore_Youth` e a carona do `Holy_Shortcut` pedem `EhPessoa` --, e
/// ao mesmo tempo ele NAO aparece no `Dead` nem na telepatia, que filtram por `EhJogador`. Por isso
/// existem os dois corpos, e nao um: o `defunto` (com `Peer`) e quem o `Dead` tem que achar.
/// ==================================================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// 5. O EFEITO NOMEADO, UM POR VERBO VIVO DE CARGO
	// =====================================================================

	/// <summary>
	/// OS 26 VERBOS QUE OS KITS DE CARGO ENTREGAM VIVOS, E O EFEITO DE CADA UM -- por extenso.
	///
	/// ============================ POR QUE O TEXTO E LONGO, E POR QUE ELE E O TESTE ============================
	/// "o verbo funcionou" e a frase que deixou 31 botoes mudos passarem meses. O que se escreve aqui e
	/// o que um humano consegue CONFERIR contra o DM sem abrir o C#: quem ler `Dodompa` e ver *"nasce um
	/// raio chamado 'Dodon Ray'"* pode abrir `Dodompa.dm:27` e conferir o nome. Uma linha que dissesse
	/// "o Dodompa faz efeito" nao da pra conferir contra nada.
	///
	/// A LISTA E CONFRONTADA COM O CENSO nas duas direcoes (ver <see cref="OEfeitoNomeadoDeCadaVerbo"/>):
	/// verbo vivo de cargo que nao esteja aqui reprova, e entrada daqui que nao seja verbo vivo de cargo
	/// reprova tambem. Sem a segunda metade, a tabela viraria um deposito de linhas mortas -- que e o
	/// defeito que o `DadivaDeCargo` acabou de documentar sobre as 24 concessoes mortas do proprio DM.
	/// ======================================================================================================
	/// </summary>
	private static readonly Dictionary<string, string> EfeitoDeVerboDeCargo = new(StringComparer.OrdinalIgnoreCase)
	{
		// ---- os sete raios canalizados: apertar reune energia, e o RAIO NASCE no mundo ----
		["Kamehameha"] = "o canal abre cobrando Ki e, carregado, NASCE no mundo um raio chamado 'Kamehameha'",
		["GalicGun"] = "o canal abre e nasce o raio chamado 'Galick Ho' (`GalicGun.dm:30`)",
		["Death_Beam"] = "o canal abre e nasce o raio chamado 'Death Beam' (`DeathBeam.dm:29`)",
		["Dodompa"] = "o canal abre e nasce o raio chamado 'Dodon Ray' (`Dodompa.dm:27`)",
		["Enkumei"] = "o canal abre e nasce o raio chamado 'Enkumei' (`Enkumei.dm:33`)",
		["Makkankosappo"] = "o canal abre e nasce o raio chamado 'Makankosappo', e ele ENGROSSA andando (`rangemod` 1,03)",
		["Final_Flash"] = "o canal abre e nasce o raio chamado 'Final Flash', com `wavemult` 4",

		// ---- as bolas instantaneas ----
		["KillDriver"] = "nasce na hora uma bola 'Kill Driver' que NAO se defleta e que PARALISA quem encostar",
		["BusterShell"] = "nascem na hora QUATRO bolas 'Buster Shell' em leque (uma so seria a tecnica errada)",
		["Kikoho"] = "cobra Ki e a silaba anda KI->KO->HO, cada uma mais cara que a anterior",

		// ---- os que mexem em outro corpo ----
		["Heal"] = "o alvo marcado entra na lista de quem esta sendo curado, e apertar de novo para",
		["Telepathy"] = "a frase chega DENTRO da cabeca do outro, sem distancia e sem custo",
		["Restore_Youth"] = "a oferta fica de pe sem mudar nada, e SO o aceite baixa a idade -- nos DOIS campos",

		// ---- os que so leem o mundo, e a frase E o efeito ----
		["Dead"] = "a lista sai com o NOME de quem esta morto agora (e sem os vivos)",
		["Detect_Shard"] = "responde que a Esmeralda Mestra nao existe mais -- o verb inteiro do DM e essa frase",

		// ---- os dois teleportes ----
		["Go_To_Heaven_Or_Hell"] = "o corpo atravessa pro Ceu (ou pro Inferno), na hora",
		["Holy_Shortcut"] = "o corpo atravessa pra Arconia, paga METADE do Ki e LEVA JUNTO quem estava colado",

		// ---- os provados noutra bancada: ver `EfeitoProvadoEmOutraBancada` ----
		["Revive"] = "o morto colado volta a vida, a volta e contada e a SEGUNDA cobra a vida de quem ressuscita",
		["Keep_Body"] = "o corpo do marcado passa a FICAR no mundo dos vivos quando o prazo da morte vence",
		["Paralysis"] = "o tiro tranca as PERNAS do alvo (e nao os bracos: paralisia nao e stun)",
		["Kaioken"] = "o `KaioPcnt` sobe de 1 no corpo, e apertar de novo apaga",
		["Kaioken_Settings"] = "e um alias do proprio Kaio-ken -- o censo o aponta pra la, e e la que ele e medido",
		["Mystic"] = "a FORMA do Mistico abre junto com a skill, e fecha quando o cargo se vai",
		["Permission"] = "a Sala do Tempo passa a aceitar quem o dono do cargo autorizou",
		["RankChat"] = "o canal fechado dos cargos aceita a fala de quem carrega um cargo",
		["Appoint_Elder"] = "o convite de Anciao chega de verdade na conta do namekuseijin escolhido",

		// ---- os doze que os lotes G9/G11/G12 puseram de pe (2026-09-02), provados nas bancadas deles ----
		["BusterBarrage"] = "a barragem LIGA e passa a cuspir esferas em rumos sorteados drenando Ki; desliga sem Ki e no nocaute (`BusterBarrage.dm:26-87`)",
		["Death_Ball"] = "a bola nasce e CARREGA em ate quatro estagios (1,5 s cada, `DeathBall.dm:75`), guiada pelo olhar; o nocaute a desfaz",
		["SpiritBomb"] = "a Genkidama se forma com 90% do Ki, cresce com quem doa meditando e SAI dois segundos depois do segundo aperto (`SpiritBomb.dm:169-173`)",
		["Grow_Senzu_Bean"] = "um minuto depois do aperto (`sleep(600)`, `Food.dm:7`) a Semente Senzu APARECE na mochila de quem cultivou",
		["SplitForm"] = "nasce uma COPIA com cerebro proprio, o nome '<dono> Copy' e metade do poder expresso (`Split Forms.dm:78-103`)",
		["Expand_Body"] = "o grau pedido infla o corpo: Tphysoff/Tphysdef sobem e Tspeed cai pelos numeros do `Loop()` (`Body Expansion.dm`)",
		["Majin"] = "o buff de forma liga em si mesmo: BPadd, physoffMod x1,3, kiregenMod +0,5, angerMod /1,2 (`Magic/Majin.dm:25-50`)",
		["Observe"] = "a mente se projeta ate o alvo e devolve onde ele esta, como esta e quem esta em volta (`observe.dm:1-14`, sem olho remoto)",
		["Self_Destruct"] = "com alguem agarrado, o segundo aperto DETONA: tira `power` de cada membro de quem detona, zera o Ki e mata o agarrado (`Ki/misc.dm:166-268`)",
		["Unlock_Potential"] = "o alvo aceita e o potencial desperta: BP += capcheck(BP*0,25*Potencial), kiskill +0,4, uma vez por vida (`UnlockPotential.dm:21-45`)",
		["Mafuba"] = "com um Pote Selante a vista o alvo e SELADO dentro dele -- e quebrar o pote solta o preso (`Sealing.dm`)",
		["Open_Dead_Zone"] = "o alvo e selado com o BP declarado -- o caminho da Dead Zone (`Sealing.dm`)",
	};

	/// <summary>
	/// OS NOVE QUE SAO MEDIDOS EM OUTRA BANCADA -- e a delegacao e CONFERIDA, nao acreditada.
	///
	/// ============================ POR QUE ISTO NAO E COBERTURA IMAGINARIA ============================
	/// Escrever "isto ja e testado la" e a forma mais barata de fingir cobertura que existe: a outra
	/// bancada pode ter mudado de nome, a linha pode ter sido apagada, e ninguem descobre. Entao a
	/// delegacao aponta pro ARQUIVO e pra FRASE EXATA da afirmacao, e esta bancada **le o fonte** e
	/// confere que a frase continua la (`LerFonteDaBancada`, o mesmo mecanismo com que a `--censoteste`
	/// confere que os 17 canais declarados existem no roteador).
	///
	/// Nao e tao forte quanto rodar a prova -- e nao pretende ser. E forte o bastante pra que apagar a
	/// prova la reprove aqui, que e a unica coisa que a delegacao precisa garantir.
	///
	/// E POR QUE DELEGAR EM VEZ DE MEDIR AQUI, um por um:
	///   * `Revive` e `Keep_Body` -- os dois terminam em `Persistir`, e mais que isso: os dois so
	///     significam alguma coisa DENTRO do percurso da morte, que e o que a `--alemteste` monta (ela
	///     e a unica que mata um jogador de verdade e devolve tudo no `finally`);
	///   * `Paralysis` -- o efeito dela e o alvo parar de ANDAR, e medir isso pede o corredor livre com
	///     colisao que a `--arsenalteste` empresta da `--projetilteste`. E foi la que ela achou o
	///     arremesso empurrando quem nao devia;
	///   * os SEIS restantes sao medidos na propria `--cargovivo`, nas secoes 3b e 4, e repetir aqui
	///     seria a segunda tela que concorda com a primeira -- o modo de falha que esta casa ja pagou.
	/// ============================================================================================
	/// </summary>
	private static readonly Dictionary<string, (string Arquivo, string Marca)> EfeitoProvadoEmOutraBancada =
		new(StringComparer.OrdinalIgnoreCase)
		{
			["Revive"] = ("Server/GameServer.AlemTeste.cs", "REVIVE DE CARGO: a alma volta a vida"),
			["Keep_Body"] = ("Server/GameServer.AlemTeste.cs", "KEEP_BODY: vencido o prazo, o corpo NAO viaja"),
			["Paralysis"] = ("Server/GameServer.ArsenalTeste.cs", "...e ao encostar ela tranca as pernas do alvo"),

			["Kaioken"] = ("Server/GameServer.CargoVivoTeste.cs",
						   "...e com o cargo do Kaio do Norte ele ACENDE no corpo (KaioPcnt sobe de 1)"),
			["Kaioken_Settings"] = ("Server/GameServer.CargoVivoTeste.cs",
									"...e apertar de novo APAGA (o toggle dos cinco verbs do `kaioken.dm`)"),
			["Mystic"] = ("Server/GameServer.CargoVivoTeste.cs",
						  "o Kaioshin recebe a skill do Mistico -- E A FORMA ABRE JUNTO"),
			["Permission"] = ("Server/GameServer.CargoVivoTeste.cs",
							  "...e o Mestre Korin, que recebe a Permission do cargo, autoriza de verdade"),
			["RankChat"] = ("Server/GameServer.CargoVivoTeste.cs",
							"...e com o cargo ele NAO recusa (o RankChat e a unica skill que todo cargo tem)"),
			["Appoint_Elder"] = ("Server/GameServer.CargoVivoTeste.cs",
								 "...e o Grande Anciao, que recebe o Appoint_Elder do cargo, convida de verdade"),

			// OS DOZE DOS LOTES G9/G11/G12: cada um tem familia propria na bancada do lote, com a cena que
			// ele exige (a barragem sustentada, a bola carregando por tiques, o agarrado, o pote selante...).
			// Repetir a cena aqui seria a segunda tela que concorda com a primeira.
			["BusterBarrage"] = ("Server/GameServer.G12Teste.cs", "o aperto liga a barragem"),
			["Death_Ball"] = ("Server/GameServer.G12Teste.cs", "o nocaute durante a carga desfaz a Death Ball e solta o corpo (na medida do nocaute)"),
			["SpiritBomb"] = ("Server/GameServer.G12Teste.cs", "2 s depois a Genkidama SAI: nao inerte, pra frente, um tile por tique, 100 s de prazo, escala mantida"),
			["Grow_Senzu_Bean"] = ("Server/GameServer.G12Teste.cs", "aos 60 s a Semente Senzu APARECE na mochila"),
			["SplitForm"] = ("Server/GameServer.G12Teste.cs", "o aperto poe uma copia NOVA no mundo, com cerebro (IA), o nome '<dono> Copy' e METADE do poder expresso"),
			["Expand_Body"] = ("Server/GameServer.G11Teste.cs", "2o grau: Tphysoff +1,25, Tphysdef +1,125 e Tspeed -(1 - 1/1,125) -- os numeros do `Loop()`"),
			["Majin"] = ("Server/GameServer.G11Teste.cs", "Majin liga: BPadd += BP*1,2*(MaxAnger/100)/10, physoffMod x1,3, kiregenMod +0,5, angerMod /1,2, MajinPcnt 1,2"),
			["Observe"] = ("Server/GameServer.G11Teste.cs", "Observe:<nome> projeta a mente: diz o mundo, o tile e a condicao, e marca `observingnow`"),
			["Self_Destruct"] = ("Server/GameServer.G11Teste.cs", "detonar tira exatamente `power` de CADA membro de quem detona (o `usr.SpreadDamage(power)`)"),
			["Unlock_Potential"] = ("Server/GameServer.G11Teste.cs", "ao aceitar, o potencial desperta: BP += pelo menos capcheck(BP*0,25*Potencial), kiskill +0,4, flag gravada"),
			["Mafuba"] = ("Server/GameServer.SeloTeste.cs", "QUEBRAR O POTE SOLTA O PRESO -- a interacao principal do Mafuba"),
			["Open_Dead_Zone"] = ("Server/GameServer.SeloTeste.cs", "...e selar com BP declarado vale o BP declarado (o caminho da Dead Zone)"),
		};

	/// <summary>
	/// A CENA DE UM VERBO DE CARGO: quem usa, em quem, e o corpo morto que o `Dead` tem que achar.
	/// </summary>
	private sealed class CenaDeCargo
	{
		public required ServerPlayer Dono;      // `Peer` emprestado -- NUNCA pode chegar no `Persistir`
		public required ServerPlayer Alvo;      // `Peer` NULO -- `EhPessoa` sim, `EhJogador` nao
		public required ServerPlayer Defunto;   // `Peer` emprestado e MORTO -- so o `Dead` olha pra ele
		public required ZoneKey Zona;
		public required Vec2 Onde;
	}

	/// <summary>
	/// A FAMILIA 5: UMA LINHA POR VERBO VIVO, COM O EFEITO NOMEADO.
	///
	/// A ordem e: derivar a lista do censo, conferir a tabela contra ela nas duas direcoes, conferir as
	/// nove delegacoes no fonte alheio, e so entao disparar os dezessete que sao medidos aqui.
	///
	/// **CADA VERBO E DISPARADO COM O CARGO QUE O ENTREGA NO TRONO**, e nunca com a skill escrita a
	/// mao no livro. E o que amarra esta familia na anterior: o efeito que se mede e o efeito que o
	/// CARGO destravou, e nao o de uma skill que a bancada se deu.
	/// </summary>
	private void OEfeitoNomeadoDeCadaVerbo(CenaDeCargo cena, Action<string, bool, string> C)
	{
		GD.Print("[cargo] -- 5) O EFEITO NOMEADO, UM POR VERBO VIVO DE CARGO --");

		// ---- a populacao vem do CENSO, e nao de uma lista escrita aqui ----
		CensoDeSkills.Relatorio r = CensoDeSkills.Levantar(_skills!);
		var vivos = r.Verbos
			.Where(l => l.DeCargo && l.Situacao is CensoDeSkills.Situacao.Portada
											   or CensoDeSkills.Situacao.OutroCanal)
			.Select(l => l.Verbo)
			.ToList();

		var semLinha = vivos.Where(v => !EfeitoDeVerboDeCargo.ContainsKey(v)).ToList();
		C($"os {vivos.Count} verbos VIVOS de cargo tem, cada um, uma linha de efeito escrita",
		  semLinha.Count == 0, "sem linha: " + string.Join(", ", semLinha));

		var sobrando = EfeitoDeVerboDeCargo.Keys
			.Where(v => !vivos.Contains(v, StringComparer.OrdinalIgnoreCase)).ToList();
		C("...e a tabela nao carrega linha de verbo que nao seja vivo de cargo (ela nao vira deposito)",
		  sobrando.Count == 0, "sobrando: " + string.Join(", ", sobrando));

		// ---- as nove delegacoes: a prova alheia tem que CONTINUAR EXISTINDO ----
		foreach ((string verbo, (string arquivo, string marca)) in EfeitoProvadoEmOutraBancada)
		{
			string fonte = LerFonteDaBancada(arquivo);
			C($"'{verbo}': {EfeitoDeVerboDeCargo[verbo]} -- provado em {arquivo}",
			  fonte.Length > 1000 && fonte.Contains(marca, StringComparison.Ordinal),
			  fonte.Length <= 1000 ? $"nao consegui ler {arquivo}" : $"a afirmacao '{marca}' sumiu de la");
		}

		// ---- e os dezessete que sao medidos aqui ----
		foreach (string verbo in vivos.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
		{
			if (EfeitoProvadoEmOutraBancada.ContainsKey(verbo)) continue;

			string cargo = PrimeiroCargoQueEntrega(verbo);
			if (cargo.Length == 0)
			{
				C($"'{verbo}': ha um cargo que o entrega", false, "nenhum kit concede este verbo");
				continue;
			}

			RearmarACena(cena, cargo);

			// O QUE O JOGO DISSE ENTRA NO DETALHE, SEMPRE. Uma recusa e o diagnostico: sem esta
			// escuta, um verbo que respondesse "voce ja esta com um raio na mao" reprovava mostrando
			// so o resultado vazio, e a causa ficava pra quem fosse depurar no escuro. (Os tres
			// verbos que sao MEDIDOS pela fala -- `Dead`, `Detect_Shard`, `Telepathy` -- abrem escuta
			// propria por dentro e devolvem o dito no proprio detalhe.)
			List<string>? guarda = EscutaDeAvisos;
			var ditos = new List<string>();
			EscutaDeAvisos = ditos;
			bool passou;
			string detalhe;
			try { passou = MedirOEfeitoDeCargo(verbo, cena, out detalhe); }
			finally { EscutaDeAvisos = guarda; }

			if (!passou && ditos.Count > 0) detalhe += " || o jogo disse: " + string.Join(" | ", ditos);
			C($"[{Cargos.Get(cargo)?.Nome ?? cargo}] '{verbo}': {EfeitoDeVerboDeCargo[verbo]}",
			  passou, detalhe);
		}

		RearmarACena(cena, "");
	}

	/// <summary>
	/// QUAL CARGO ENTREGA ESTE VERBO -- derivado do kit, e nao escrito a mao.
	///
	/// Escrever "Kamehameha vem do Eremita Tartaruga" numa tabela daria uma bancada que envelhece
	/// calada no dia em que o kit mudar: ela continuaria ocupando o trono errado e reprovando por
	/// motivo nenhum. Aqui a pergunta e feita ao `DadivaDeCargo`, que e quem responde em producao.
	/// </summary>
	private string PrimeiroCargoQueEntrega(string verbo)
	{
		foreach (RankDef r in Cargos.Todos)
			foreach (string path in DadivaDeCargo.De(r.Chave))
				if (_skills?.Get(path) is { } s
					&& s.Verbos.Contains(verbo, StringComparer.OrdinalIgnoreCase))
					return r.Chave;
		return "";
	}

	/// <summary>
	/// PoE A CENA DE VOLTA NO LUGAR E OCUPA O TRONO PEDIDO.
	///
	/// Chamada ANTES de cada verbo, e nao depois -- assim um verbo que deixe sujeira (o
	/// `Holy_Shortcut` leva os dois corpos pra outra zona; o `Kikoho` come vida; os raios enchem a
	/// zona de projeteis) nao faz o VIZINHO reprovar. E o mesmo desenho do `ApertarUmVerbo` da
	/// `--catalogoteste`, e ele existe la pelo mesmo motivo escrito.
	/// </summary>
	private void RearmarACena(CenaDeCargo cena, string cargo)
	{
		_tronos.Clear();
		if (cargo.Length > 0) _tronos[cargo] = cena.Dono.Conta;
		TickDosCargos();

		foreach (ServerPlayer p in new[] { cena.Dono, cena.Alvo, cena.Defunto })
		{
			if (!p.Zone.Equals(cena.Zona)) MoveToZone(p.Id, cena.Zona, cena.Onde);
			p.Ficha.KO = false;
			p.Ficha.med = false;
			p.Ficha.train = false;
			p.Ficha.Ki = p.Ficha.MaxKi;
			p.Combate?.Reviver();
			p.Ficha.dead = false;
			p.Combate?.SincronizarVida();
		}

		// O ALVO FICA COLADO (meio tile): o `Heal` pede `Marcado`, e a carona do `Holy_Shortcut` pede
		// um tile e meio. Meio tile atende os dois sem chegar a empurrar ninguem.
		cena.Dono.Pos = cena.Onde;
		cena.Alvo.Pos = cena.Onde + new Vec2(ZoneCollision.TileSize * 0.5f, 0);
		cena.Defunto.Pos = cena.Onde + new Vec2(ZoneCollision.TileSize * 6f, 0);
		cena.Dono.Facing = Facing.East;
		cena.Dono.AlvoId = cena.Alvo.Id;

		// O DEFUNTO VOLTA A SER DEFUNTO. Ele existe pra o `Dead` ter o que achar, e o laco acima
		// acabou de ressuscita-lo junto com os outros dois.
		cena.Defunto.Combate.Morrer(ignorarSeguro: true);

		// AS RECARGAS E OS CANAIS DO VERBO ANTERIOR. Sem isto, o segundo raio da lista ouviria "voce
		// ja esta com um raio na mao" e a bancada acusaria a tecnica pelo que a vizinha deixou.
		_canais.Remove(cena.Dono.Id);
		_blastPronto.Remove(cena.Dono.Id);
		_kikoho.Remove(cena.Dono.Id);
		_curando.Remove(cena.Dono.Id);
		_prontoTelepatiaG4.Remove(cena.Dono.Id);
		_ofertasDeJuventudeG8.Remove(cena.Alvo.Conta);
		LimparOsTirosDaCena(cena.Zona.Hash);

		// ============================ O TANQUE, E POR QUE ELE E FORCADO ============================
		// O corpo forjado nasce com o `MaxKi` que a ficha dele der, e ele e PEQUENO: um Kikoho de
		// segunda silaba pede 115 e o tanque inteiro nao chegava a isso. Sem esta linha, metade dos
		// verbos caros reprovaria por *"isso pede pelo menos N de energia"* -- e a bancada estaria
		// medindo o tamanho do tanque de um boneco em vez do efeito da tecnica.
		//
		// **A RECUSA POR FALTA DE KI JA TEM DONO**: e a `--projetilteste` que a mede, e a
		// `--arsenalteste` forca o mesmo teto pelo mesmo motivo, escrito la (`ForjarArmado`). O que
		// esta bancada afirma e outra coisa -- que o CARGO destravou um verbo que FAZ o que promete.
		// ======================================================================================
		cena.Dono.Ficha.MaxKi = Math.Max(cena.Dono.Ficha.MaxKi, 5_000_000);
		cena.Dono.Ficha.Ki = cena.Dono.Ficha.MaxKi;
	}

	/// <summary>Tira do mundo os projeteis que a cena anterior deixou -- e desconta o contador global.</summary>
	private void LimparOsTirosDaCena(ulong hash)
	{
		List<Projetil> lista = ProjeteisDaZona(hash);
		_projeteisVivos = Math.Max(0, _projeteisVivos - lista.Count);
		lista.Clear();
	}

	/// <summary>
	/// O EFEITO DE UM VERBO, MEDIDO. Devolve falso e o motivo -- e o motivo e o que aparece no log.
	///
	/// Tudo aqui passa por <see cref="UsarHabilidade"/>, que e a MESMA porta que a tecla do jogador
	/// usa. Chamar `RaioNomeadoG6` direto mediria o corpo da tecnica por cima do gate do cargo, e o
	/// gate e metade do que esta bancada existe pra afirmar.
	/// </summary>
	private bool MedirOEfeitoDeCargo(string verbo, CenaDeCargo cena, out string detalhe)
	{
		ServerPlayer pl = cena.Dono, alvo = cena.Alvo;
		detalhe = "";

		switch (verbo)
		{
			// ---- OS SETE RAIOS CANALIZADOS ----
			case "Kamehameha": return NasceORaio(cena, verbo, "Kamehameha", out detalhe);
			case "GalicGun": return NasceORaio(cena, verbo, "Galick Ho", out detalhe);
			case "Death_Beam": return NasceORaio(cena, verbo, "Death Beam", out detalhe);
			case "Dodompa": return NasceORaio(cena, verbo, "Dodon Ray", out detalhe);
			case "Enkumei": return NasceORaio(cena, verbo, "Enkumei", out detalhe);
			case "Final_Flash": return NasceORaio(cena, verbo, "Final Flash", out detalhe);
			case "Makkankosappo": return NasceORaio(cena, verbo, "Makankosappo", out detalhe);

			// ---- AS BOLAS INSTANTANEAS ----
			case "KillDriver":
			{
				UsarHabilidade(pl, "KillDriver");
				List<Projetil> tiros = ProjeteisDaZona(cena.Zona.Hash);
				detalhe = $"{tiros.Count} tiro(s)";
				if (tiros.Count != 1) return false;
				Projetil p = tiros[0];
				detalhe = $"nome '{p.Nome}', deflectivel={p.Deflectivel}, paralisia={p.Paralisia}";
				// `A.deflectable = 0` e `A.paralysis = 1` -- as duas linhas que fazem o Kill Driver
				// ser outra coisa que uma bola comum (`blasts/KillDriver.dm`).
				return p.Nome == "Kill Driver" && !p.Deflectivel && p.Paralisia;
			}

			case "BusterShell":
			{
				UsarHabilidade(pl, "BusterShell");
				List<Projetil> tiros = ProjeteisDaZona(cena.Zona.Hash);
				detalhe = $"{tiros.Count} bola(s): {string.Join(", ", tiros.Select(t => t.Nome))}";
				// QUATRO, e nao "pelo menos uma": `blasts/BusterShell.dm:26` dispara quatro, e uma
				// bancada que aceitasse uma so nao distinguiria o Buster Shell de um tiro qualquer.
				return tiros.Count == 4 && tiros.All(t => t.Nome == "Buster Shell");
			}

			case "Kikoho":
			{
				// A SILABA E A TECNICA (`blasts/Kikoho.dm:26`): `Ki -= 50*BaseDrain * n`, com n = 1
				// (KI), 2 (KO), 3 (HO). Cada uso seguido custa mais que o anterior; passados 6 s a
				// contagem volta pro KI. O `_blastPronto` e limpo entre as silabas porque o que se
				// mede aqui e a ESCADA, e a recarga de 1 s ja tem dono na `--arsenalteste`.
				double ki0 = pl.Ficha.Ki;
				UsarHabilidade(pl, "Kikoho");
				double custo1 = ki0 - pl.Ficha.Ki;
				(int silaba1, _) = _kikoho.GetValueOrDefault(pl.Id);

				_blastPronto.Remove(pl.Id);
				double ki1 = pl.Ficha.Ki;
				UsarHabilidade(pl, "Kikoho");
				double custo2 = ki1 - pl.Ficha.Ki;
				(int silaba2, _) = _kikoho.GetValueOrDefault(pl.Id);

				detalhe = $"silabas {silaba1}->{silaba2}, custos {custo1:0.##} e {custo2:0.##}";
				return silaba1 == 1 && silaba2 == 2 && custo1 > 0 && custo2 > custo1 * 1.9;
			}

			// ---- OS QUE MEXEM EM OUTRO CORPO ----
			case "Heal":
			{
				UsarHabilidade(pl, "Heal");
				bool comecou = _curando.TryGetValue(pl.Id, out int quem) && quem == alvo.Id;
				UsarHabilidade(pl, "Heal");   // `if(_curando.Remove(...))` -- apertar de novo para
				bool parou = !_curando.ContainsKey(pl.Id);
				detalhe = $"comecou={comecou} parou={parou}";
				return comecou && parou;
			}

			case "Telepathy":
			{
				// O ALVO AQUI E O `defunto` E NAO O `alvo`: a telepatia filtra por `EhJogador`
				// (`ListarMentesG4`, `Tecnicas.G4.cs:260`), e o corpo de `Peer` nulo nao passa nesse
				// crivo -- de proposito, ver o cabecalho. O `defunto` tem `Peer`; so precisa estar
				// vivo pra hora da medicao.
				cena.Defunto.Combate.Reviver();
				cena.Defunto.Ficha.dead = false;

				// E O `Tick()` E OBRIGATORIO, nao cosmetico: a telepatia procura ENERGIA, e o crivo
				// dela e `expressedBP > 5` (`AchoAEnergiaG4`, os tres testes de `Communication.dm:53`).
				// Um corpo que acabou de morrer tem `expressedBP` zerado, e reviver nao o recalcula --
				// quem recalcula e o `Tick`. Sem esta linha a bancada acusava a telepatia de nao achar
				// alguem que estava vivo e colado nela.
				cena.Defunto.Ficha.Tick();

				List<string>? guarda = EscutaDeAvisos;
				var ditos = new List<string>();
				EscutaDeAvisos = ditos;
				try { UsarHabilidade(pl, $"Telepathy:{cena.Defunto.Name}:o cargo fala na sua mente"); }
				finally { EscutaDeAvisos = guarda; }

				string tudo = string.Join(" | ", ditos);
				detalhe = $"expressedBP={cena.Defunto.Ficha.expressedBP:0} | {tudo}";
				cena.Defunto.Combate.Morrer(ignorarSeguro: true);
				// AS DUAS PONTAS: quem manda le "voce diz na mente de X" e quem recebe le "X diz na
				// sua mente". Exigir so a primeira deixaria passar uma telepatia que nao chega.
				return tudo.Contains($"na mente de {cena.Defunto.Name}", StringComparison.Ordinal)
					   && tudo.Contains($"{pl.Name} diz na sua mente", StringComparison.Ordinal);
			}

			case "Restore_Youth":
			{
				alvo.Idade = 40;
				alvo.Ficha.Idade = 40;

				// A OFERTA NAO MUDA NADA -- e esta e a metade que importa. O `input()` do DM abre NO
				// ALVO (`OtherworldRankSkills.dm:170`) e a idade so e escrita no "Yes": consentimento
				// e parte da tecnica, porque idade mexe em BP neste port e rejuvenescer um desafeto
				// seria um debuff disfarcado de presente.
				UsarHabilidade(pl, "Restore_Youth:14");
				bool ofertaMuda = alvo.Idade == 40 && alvo.Ficha.Idade == 40;

				ComandoDeCargo(alvo, "juventude_aceitar", "");
				// `M.Age=age` E `M.Body=age` (`:172-173`) -- os DOIS campos. Escrever so um deixaria o
				// poder e a ficha contando idades diferentes ate o proximo login.
				bool caiu = alvo.Idade == 14 && Math.Abs(alvo.Ficha.Idade - 14) < 1e-9;

				detalhe = $"oferta muda={ofertaMuda}, depois do aceite: Idade={alvo.Idade} Ficha={alvo.Ficha.Idade}";
				return ofertaMuda && caiu;
			}

			// ---- OS QUE SO LEEM O MUNDO ----
			case "Dead":
			{
				List<string>? guarda = EscutaDeAvisos;
				var ditos = new List<string>();
				EscutaDeAvisos = ditos;
				try { UsarHabilidade(pl, "Dead"); }
				finally { EscutaDeAvisos = guarda; }

				string tudo = string.Join(" | ", ditos);
				detalhe = tudo;
				// O MORTO APARECE **E O VIVO NAO**. So a primeira metade ficaria verde com um verb
				// que listasse o mundo inteiro -- que e precisamente o que o `for(var/mob/M)` do DM
				// faria neste port, e o motivo de o crivo daqui ser `EhJogador`.
				return tudo.Contains(cena.Defunto.Name, StringComparison.Ordinal)
					   && !tudo.Contains(pl.Name, StringComparison.Ordinal);
			}

			case "Detect_Shard":
			{
				List<string>? guarda = EscutaDeAvisos;
				var ditos = new List<string>();
				EscutaDeAvisos = ditos;
				try { UsarHabilidade(pl, "Detect_Shard"); }
				finally { EscutaDeAvisos = guarda; }

				detalhe = string.Join(" | ", ditos);
				return detalhe.Contains("Esmeralda Mestra", StringComparison.OrdinalIgnoreCase)
					   && detalhe.Contains("nao existe mais", StringComparison.OrdinalIgnoreCase);
			}

			// ---- OS DOIS TELEPORTES ----
			case "Go_To_Heaven_Or_Hell":
			{
				UsarHabilidade(pl, "Go_To_Heaven_Or_Hell:ceu");
				bool foi = string.Equals(pl.Zone.Name, Alem.ZonaDoCeu, StringComparison.Ordinal);
				detalhe = $"acabou em '{pl.Zone.Name}' (queria '{Alem.ZonaDoCeu}')";
				return foi;
			}

			case "Holy_Shortcut":
			{
				double ki0 = pl.Ficha.Ki;
				UsarHabilidade(pl, "Holy_Shortcut:arconia");

				bool foi = string.Equals(pl.Zone.Name, "Arconia", StringComparison.Ordinal);
				bool pagou = pl.Ficha.Ki <= ki0 / 2 + 1e-6;
				// `for(var/mob/V in oview(1))` -- QUEM ESTAVA COLADO VEM JUNTO, sem escolher e sem
				// poder recusar. E a coisa mais estranha do verb, e por isso ela tem que estar na
				// linha: sem esta metade, o Atalho Sagrado seria um teleporte qualquer.
				bool levou = string.Equals(alvo.Zone.Name, "Arconia", StringComparison.Ordinal);

				detalhe = $"dono em '{pl.Zone.Name}', Ki {ki0:0}->{pl.Ficha.Ki:0}, carona em '{alvo.Zone.Name}'";
				return foi && pagou && levou;
			}

			default:
				detalhe = "esta bancada nao sabe medir este verbo";
				return false;
		}
	}

	/// <summary>
	/// O CANAL ABRE, CARREGA, E O RAIO NASCE COM O NOME DELE.
	///
	/// ============================ POR QUE NAO BASTA "O CANAL ABRIU" ============================
	/// A `--arsenalteste` afirma, dos quatro raios dela, que apertar abre o canal e apertar de novo o
	/// fecha. E verdade e e util -- e nao e efeito: o `Canalizar` escreve um dicionario. O projetil so
	/// nasce depois, dentro do <see cref="TickDosCanaisDeKi"/>, quando a carga vence. Um raio cujo
	/// canal abrisse e que nunca parisse nada passaria naquela afirmacao inteira.
	///
	/// O KI E REPOSTO A CADA VOLTA do laco, e fica escrito: o que se mede aqui e o NASCIMENTO, e o
	/// aluguel por ciclo (`lastbeamcost`) ja tem bancada -- e uma delas, o Makankosappo, carrega seis
	/// vezes mais que os outros. Sem repor, os raios caros morreriam de fome antes de nascer e a
	/// bancada acusaria a tecnica pelo tamanho do tanque do corpo forjado.
	/// ======================================================================================
	/// </summary>
	private bool NasceORaio(CenaDeCargo cena, string verbo, string nomeEsperado, out string detalhe)
	{
		ServerPlayer pl = cena.Dono;
		UsarHabilidade(pl, verbo);

		if (!_canais.TryGetValue(pl.Id, out CanalDeKi? canal))
		{
			detalhe = "o canal nem abriu";
			return false;
		}
		double porCiclo = canal.CustoPorCiclo;

		List<Projetil> tiros = ProjeteisDaZona(cena.Zona.Hash);
		for (int i = 0; i < 600 && tiros.Count == 0 && _canais.ContainsKey(pl.Id); i++)
		{
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			TickDosCanaisDeKi(0.05);
		}

		bool nasceu = tiros.Count > 0;
		string nome = nasceu ? tiros[0].Nome : "";
		bool feixe = nasceu && tiros[0].Tipo == TipoDeProjetil.Beam;

		UsarHabilidade(pl, verbo);   // solta -- e o `if(beaming) stopbeaming()` que abre todo verb de raio
		bool fechou = !_canais.ContainsKey(pl.Id);

		detalhe = $"nome '{nome}' (queria '{nomeEsperado}'), feixe={feixe}, "
				  + $"custo/ciclo={porCiclo:0.###}, fechou={fechou}";
		return nasceu && nome == nomeEsperado && feixe && porCiclo > 0 && fechou;
	}

	// =====================================================================
	// 6. O KIT CHEGA NA HORA DE REIVINDICAR -- sem a janela de 1 s
	// =====================================================================
	/// <summary>
	/// **NENHUM TIQUE ENTRE A PORTA E A MEDICAO** -- e isso e o teste inteiro.
	///
	/// ============================ O QUE ESTA FAMILIA GUARDA ============================
	/// A `--cargovivo` achou a janela: `ReivindicarCargo` e `Outorgar` escreviam o trono, gravavam e
	/// anunciavam -- e nao reconciliavam. O jogador lia "VOCE E O NOVO EREMITA TARTARUGA", abria a aba
	/// de skills e o Kamehameha nao estava la; um segundo depois estava. Nao ha como distinguir isso de
	/// jogo quebrado.
	///
	/// O conserto entrou (`GameServer.Ranks.cs`) **e nenhuma linha o media**: as provas da secao 3 de
	/// la chamam `TickDosCargos` logo depois de `ReivindicarCargo`, entao continuariam verdes com a
	/// chamada arrancada -- o tique entregaria o kit e ninguem veria a diferenca. Uma bancada que so
	/// pega o defeito quando ele volta em outro lugar nao esta guardando o conserto.
	///
	/// E A ORDEM TAMBEM E MEDIDA, porque a ordem foi escolhida: a reconciliacao vem ANTES do
	/// `AnunciarCargo` pra o pacote de skills chegar antes do titulo. Como o anuncio e o unico que
	/// escreve na tela dos OUTROS, a bancada afirma a ordem pelo que da pra afirmar de dentro: quando
	/// o dono ouve o anuncio, o kit ja esta no livro.
	/// ==============================================================================
	/// </summary>
	private void OKitChegaNaHoraDeReivindicar(
		CenaDeCargo cena, ServerPlayer outro, Action<string, bool, string> C)
	{
		GD.Print("[cargo] -- 6) O KIT CHEGA NA HORA (a janela de 1 s) --");

		const string kame = "/datum/skill/style/KameStyle";
		ServerPlayer dono = cena.Dono;

		_tronos.Clear();
		TickDosCargos();
		dono.Livro.Esquecer(kame);
		dono.Karma = 25;
		dono.Ficha.BP = 5_000_000;

		// ---- A REIVINDICACAO ----
		var ouviu = new List<string>();
		List<string>? guarda = EscutaDeAvisos;
		EscutaDeAvisos = ouviu;
		bool tinhaAntes = dono.Livro.Sabe(kame);
		try { ReivindicarCargo(dono, "turtle"); }
		finally { EscutaDeAvisos = guarda; }

		C("REIVINDICAR entrega o kit NA HORA -- sem um unico tique no meio",
		  !tinhaAntes && dono.Livro.Sabe(kame),
		  $"cargo='{CargoDe(dono.Conta)}', estilo no livro={dono.Livro.Sabe(kame)} (tinha antes: {tinhaAntes})");

		// E O JOGADOR FOI AVISADO DENTRO DA PORTA. `ContarOQueOCargoDeu` roda dentro da
		// reconciliacao; se ela tivesse ficado pro tique, este aviso sairia depois -- e o jogador
		// teria lido o titulo antes de saber o que ganhou.
		C("...e o aviso do que o cargo entrega saiu DENTRO da propria porta",
		  string.Join(" | ", ouviu).Contains("o cargo te entrega", StringComparison.OrdinalIgnoreCase),
		  string.Join(" | ", ouviu));

		// ============================ A ORDEM E AFIRMADA NO FONTE, E ISSO E DELIBERADO ============================
		// A ordem escolhida foi `ReconciliarDadiva` ANTES de `AnunciarCargo`, pra o pacote de skills
		// chegar antes do "VOCE E O NOVO X". Ela NAO da pra medir daqui: o anuncio sai por `Mandar`
		// (pacote de rede pra todo mundo) e nao por `Avisar`, e a escuta desta bancada so pega o
		// segundo. Medir "as duas coisas aconteceram" seria trocar a ordem por um verde que a ordem
		// invertida tambem daria.
		//
		// Entao a afirmacao e a que da pra fazer com honestidade: as duas chamadas existem, e a
		// reconciliacao vem ANTES no fonte. E o mesmo mecanismo com que a `--censoteste` afirma que
		// os 17 canais declarados existem no roteador.
		// ====================================================================================================
		string fonteRanks = LerFonteDaBancada("Server/GameServer.Ranks.cs");
		int ondeReconcilia = fonteRanks.IndexOf("ReconciliarDadiva(pl);", StringComparison.Ordinal);
		int ondeAnuncia = fonteRanks.IndexOf("AnunciarCargo(r, pl,", StringComparison.Ordinal);
		C("...e no fonte a reconciliacao vem ANTES do anuncio (o kit chega antes do titulo)",
		  ondeReconcilia > 0 && ondeAnuncia > 0 && ondeReconcilia < ondeAnuncia,
		  $"reconcilia@{ondeReconcilia}, anuncia@{ondeAnuncia}");

		// ---- A OUTORGA DE ADMIN: a mesma janela, na outra porta ----
		// Ela nao passa pelos requisitos (e o `Give (Rank)` do original), mas passa pela MESMA
		// reconciliacao -- e tinha a mesma janela, sem a mesma linha.
		if (Cargos.Get("turtle") is { } turtle)
		{
			bool outroTinha = outro.Livro.Sabe(kame);
			Outorgar(turtle, outro);

			C("OUTORGAR entrega o kit NA HORA ao novo dono -- tambem sem tique",
			  !outroTinha && outro.Livro.Sabe(kame),
			  $"cargo='{CargoDe(outro.Conta)}', estilo={outro.Livro.Sabe(kame)}");

			// E O EX-DONO PERDE NA HORA, que e a metade mais facil de esquecer: `Outorgar` reconcilia
			// os DOIS, e uma versao que so reconciliasse quem recebe deixaria o antigo com o kit ate
			// o proximo segundo -- dois Eremitas Tartaruga ao mesmo tempo, do ponto de vista da aba
			// de skills.
			C("...e o EX-dono perde o kit no mesmo instante (dois donos do mesmo kit, nem por 1 s)",
			  !dono.Livro.Sabe(kame),
			  $"o ex-dono ainda tem o estilo; cargo dele agora: '{CargoDe(dono.Conta)}'");
		}

		// ---- A INJECAO, DECLARADA: o que esta familia pega e o que a secao 3 NAO pegaria ----
		// Com a reconciliacao arrancada das duas portas, TODA linha desta familia reprova e a secao 3
		// da `--cargovivo` continua verde inteira (la ha um `TickDosCargos` entre a porta e a
		// medicao). Foi assim que o defeito ficou meses sem dono, e e por isso que esta familia mede
		// o instante em vez do resultado.

		_tronos.Clear();
		TickDosCargos();
		dono.Karma = 0;
		outro.Livro.Esquecer(kame);
		SalvarCargos();
	}

	// =====================================================================
	// 7. O VERBO NAO SOME NO TIQUE SEGUINTE
	// =====================================================================
	/// <summary>
	/// A ARMADILHA DA RECONCILIACAO, MEDIDA NOS DOIS SENTIDOS.
	///
	/// ============================ O DEFEITO QUE ESTA FAMILIA GUARDA ============================
	/// `ReconciliarDadiva` faz duas coisas, e a primeira e TIRAR: tudo que so um cargo poderia ter
	/// dado, que o dono nao tem cargo pra justificar, e que ninguem lhe ENSINOU, sai do livro. E o
	/// `treeshrink` do DM (`OtherworldRanks.dm:23-27`), e ele existe por um motivo real -- o Sr. Kaioh
	/// ensina Kaio-ken de graca, e sem a excecao do "ensinado" um Grande Kaio perdia a tecnica que o
	/// NPC deu.
	///
	/// A consequencia pratica e uma armadilha que este projeto ja pisou: **quem conceder uma skill de
	/// cargo pela porta errada ve o verbo aparecer e sumir um tique depois**. Nao ha erro, nao ha
	/// aviso, e o autor da concessao jura que escreveu a linha -- ela esta escrita, e foi desfeita.
	///
	/// AS DUAS METADES, e a segunda e a que nao deixa a primeira passar de graca:
	///   * a porta CERTA (`DarComoEnsinada`) sobrevive a tiques seguidos;
	///   * a porta ERRADA (`Dar`, sem ensinar, sem cargo) e revogada no PRIMEIRO tique.
	///
	/// Sem a segunda, "a skill sobreviveu" ficaria verde num servidor que simplesmente nao revoga
	/// nada -- e ai o kit do ex-Kaio ficaria com ele pra sempre, que e o buraco oposto e do mesmo
	/// tamanho.
	/// ======================================================================================
	/// </summary>
	private void OVerboNaoSomeNoTiqueSeguinte(
		CenaDeCargo cena, ServerPlayer controle, Action<string, bool, string> C)
	{
		GD.Print("[cargo] -- 7) O VERBO NAO SOME NO TIQUE SEGUINTE --");

		// O CONTROLE AQUI PRECISA TER `Peer`, e a bancada aprendeu isso reprovando: a revogacao que
		// esta familia mede corre dentro do `TickDosCargos`, e o tique so olha pra quem `EhJogador`
		// aprova -- que pede `Peer != null`. Medindo no corpo-alvo (de `Peer` nulo) a skill posta
		// pela porta errada NUNCA sumia, e a bancada acusava a reconciliacao de ter parado de
		// revogar quando ela sequer tinha sido consultada.
		ServerPlayer dono = cena.Dono;
		const string reviveDeCargo = "/datum/skill/rank/Revive";

		// ---- O KIT INTEIRO AGUENTA CINCO TIQUES ----
		// O Kaio do Norte e o cargo certo pra isto: o kit dele e todo `/datum/skill/rank/*` e
		// `kaioken` -- ou seja, e o kit com MAIS candidatos a serem revogados pelo passo 1 da
		// reconciliacao. Um cargo cujo kit fosse todo de skill com dono fora do cargo passaria por
		// ausencia.
		_tronos.Clear();
		_tronos["nkai"] = dono.Conta;
		TickDosCargos();

		string[] kit = [.. DadivaDeCargo.De("nkai").Where(p => _skills?.Get(p) != null)];
		var chegou = kit.Where(dono.Livro.Sabe).ToArray();
		C($"o kit do Kaio do Norte chegou inteiro ({chegou.Length} de {kit.Length})",
		  chegou.Length == kit.Length && kit.Length > 0,
		  string.Join(", ", kit.Except(chegou)));

		for (int i = 0; i < 5; i++) TickDosCargos();
		var sumiu = kit.Where(p => !dono.Livro.Sabe(p)).ToArray();
		C("...e CINCO tiques depois ele continua inteiro (a reconciliacao nao come o proprio kit)",
		  sumiu.Length == 0, "sumiram: " + string.Join(", ", sumiu));

		// E O VERBO CONTINUA RESPONDENDO -- o livro e uma coisa, o portao e outra. Sem esta linha, um
		// livro intacto com o `SabeTecnica` fechado passaria: e exatamente a diferenca entre "a skill
		// esta la" e "o botao funciona", que e a diferenca que abriu esta frente inteira.
		C("...e a tecla do Kaio-ken continua sendo aceita depois dos cinco tiques",
		  SabeTecnica(dono, "Kaioken"), "");

		// ---- A PORTA ERRADA: aparece e some ----
		_tronos.Clear();
		TickDosCargos();
		controle.Livro.Esquecer(reviveDeCargo);

		controle.Livro.Dar(reviveDeCargo);
		C("PORTA ERRADA: `Dar()` sem cargo e sem ensinar poe a skill no livro...",
		  controle.Livro.Sabe(reviveDeCargo), "");

		TickDosCargos();
		C("...e ela SOME no tique seguinte (o `treeshrink` do DM, e a armadilha por escrito)",
		  !controle.Livro.Sabe(reviveDeCargo), "a skill ficou -- entao a reconciliacao parou de revogar");

		// ---- A PORTA CERTA: fica ----
		controle.Livro.DarComoEnsinada(reviveDeCargo);
		C("PORTA CERTA: `DarComoEnsinada()` poe a MESMA skill no livro...",
		  controle.Livro.Sabe(reviveDeCargo), "");

		for (int i = 0; i < 3; i++) TickDosCargos();
		C("...e ela AGUENTA tres tiques sem cargo nenhum (o `wastaught` do DM e respeitado)",
		  controle.Livro.Sabe(reviveDeCargo), "a skill ENSINADA foi revogada");

		controle.Livro.Esquecer(reviveDeCargo);
		_tronos.Clear();
		TickDosCargos();
	}
}
