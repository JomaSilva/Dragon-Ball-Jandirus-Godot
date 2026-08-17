using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DO ESCUDO DA CINEMATICA (`--escudoteste`) -- roda no BOOT, sem ninguem em jogo.
///
///     Godot --headless --path . --server --rede 7950 --escudoteste
///
/// ============================ O PEDIDO QUE ELA MEDE ============================
/// *"durante cinematicas de transformacao, o personagem ficaria IMUNE A DANOS pra ninguem poder
/// MATAR o player ou NPCs enquanto ele transforma"*.
///
/// **IMUNIDADE PARCIAL E PIOR QUE NENHUMA**: se o soco nao machuca e o raio mata, o jogador morre
/// transformando e reporta "as vezes" -- o defeito que nao se acha. Entao a pergunta desta bancada
/// nao e "o escudo existe?", e sim "QUANTAS fontes de dano ele cobre, uma por uma, com o nome de
/// cada uma na frente". Uma checagem generica ficaria VERDE com metade das fontes passando direto.
/// ==============================================================================
///
/// ============================ AS TRES FAMILIAS QUE SE SEGURAM ============================
/// Cada fonte da <see cref="TabelaDeFontes"/> e exercitada TRES vezes, pela MESMA funcao de
/// disparo, e as tres respostas juntas e que valem:
///
///   1. **EM CENA, nao machuca.** Sozinha ela e fraca: uma fonte que nao faz nada (um disparo mal
///      montado, uma tecnica que recusou por outro motivo) daria verde aqui pra sempre.
///   2. **FORA DA CENA, machuca.** O contra-exemplo. Sem ele, "imune pra sempre" -- que quebra o
///      jogo de um jeito muito pior que o bug original -- passaria verde.
///   3. **EM CENA COM O ESCUDO ARRANCADO, machuca** (<see cref="ModoDaMedicao.EmCenaSemEscudo"/>). E a
///      INJECAO DO DEFEITO: `Combate.EmCinematica` volta a ser nulo -- que e literalmente o estado
///      pre-correcao deste port -- e o `CenaSegundos` CONTINUA correndo. Se a linha 1 daquela fonte
///      ficasse verde por acidente, esta fica verde tambem e o par denuncia. Ela e o motivo pelo
///      qual uma checagem generica nao serviria: uma so nao consegue ficar vermelha uma fonte por
///      vez.
///
/// O QUE A INJECAO PROVA, EXATAMENTE: que a linha 1 daquela fonte e verde POR CAUSA DO ESCUDO, e
/// nao porque o disparo nao chegou no corpo. Ela funciona pra todas as fontes de uma vez so porque
/// **todo crivo do jogo pergunta pelo `CombatState.Intocavel`** -- inclusive as guardas que ficam
/// ANTES do laco (`Sol.Queimar`, `EspalharDanoG2`, `EstourarG2`, `EspalharDanoDaCarga`,
/// `LimitarVida`, `ConsumarDestruicao`, o sorteio do `DetonarG3`). Se um dia alguem escrever um
/// crivo que pergunta pelo `EmCena` direto, a injecao deixa de avermelhar aquela fonte -- e isso
/// apareceria aqui como uma linha 3 verde, que e falha.
/// ====================================================================================
///
/// ============================ E A INJECAO UMA FONTE POR VEZ, NO CODIGO DE PRODUCAO ============================
/// A familia 3 arranca o escudo do CORPO. A familia 8 permite arrancar a guarda de UMA FONTE, no
/// arquivo dela, e ver so a linha dela ficar vermelha. Foi feito, e o resultado esta aqui:
///
///   * `Sol.Queimar`, guarda removida      -> 1 vermelha, e so a da ESTRELA          (109/110)
///   * `EspalharDanoG2`, guarda removida   -> 1 vermelha, e so a do KAIO-KEN         (109/110)
///   * `LimitarVida`, guarda removida      -> 2 vermelhas, e so as do SORTEIO POR ESTAGIO
///                                            (a do jogador e a do NPC)              (108/110)
///
/// A do `LimitarVida` reprovou com **"vida 100 -> vida 100, MORTO"**: ela mata sem tirar um ponto
/// de vida. E a razao de a <see cref="Marca"/> fotografar morte, nocaute e membros alem da barra --
/// uma foto so da vida teria ficado verde em cima de um NPC morto.
/// ==========================================================================================================
///
/// ============================ O QUE ELA NAO PODE MEDIR ============================
/// Todo corpo desta bancada tem `Peer` nulo, porque no boot nao ha cliente. Isso **nao** enfraquece
/// a pergunta do NPC: o escudo nao olha `Peer` em linha nenhuma, e quem e NPC aos olhos do jogo e
/// quem tem `Papel` (o `isNPC` deste port). Por isso a familia 5 roda a tabela inteira de novo num
/// corpo com `Papel` de verdade, forjado do molde `cidadao` do `npcs.json`.
/// ==============================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Faixa de ids desta bancada -- longe do `_nextId`, do sol (90.400) e do projetil (90.800).</summary>
	private const int IdBaseDoEscudo = 91_200;

	private int _escCorpos;
	private int _escudoOk, _escudoFalhou;

	/// <summary>
	/// A ZONA DAS FONTES QUE PRECISAM DE CHAO. A Terra, porque ela tem colisao de verdade no
	/// catalogo -- o projetil que VOA e a unica fonte que se importa, e ela se importa muito
	/// (um tiro que bate numa pedra a dois tiles do atirador daria "nao machucou" por engano).
	/// </summary>
	private static ZoneKey ZonaDoEscudo => new(ZoneKey.KindPremade, "Earth");

	/// <summary>
	/// A ZONA DA DESTRUICAO DE PLANETA. Arlia, e a escolha e a mesma da `--destruicaoteste`: ela e
	/// pre-feita, esta no manifesto e **ninguem nasce nela por padrao**.
	///
	/// Isso e requisito e nao preferencia: o `LimitarVida` varre a zona e mata TODO NPC sorteado, e
	/// o `ConsumarDestruicao` mata todo habitante sem chance. Rodar isso na Terra mataria os
	/// cidadaos de verdade do mundo do dono -- uma bancada que cobra o preco do jogo.
	/// </summary>
	private static ZoneKey ZonaDaDestruicaoDoEscudo => ZoneKey.Premade("Arlia");

	// =====================================================================
	// A FONTE
	// =====================================================================
	/// <summary>
	/// UMA FONTE DE DANO -- o nome que sai no placar, o palco que ela precisa e o disparo.
	///
	/// **`Palco` roda ANTES da transformacao e `Vespera` DEPOIS**, e a separacao nao e enfeite: o
	/// `Palco` mexe em coisas que a transformacao le (zona, gravidade, papel de NPC) e a `Vespera`
	/// mexe em coisas que a transformacao RECUSARIA (o nocaute -- corpo caido nao se transforma).
	/// Sem os dois ganchos, as duas fontes que dependem do nocaute nao teriam como ser montadas
	/// sem escrever `CenaSegundos` a mao, que e testar a bancada.
	///
	/// `Disparar` chama **codigo de producao e so ele**: nao ha uma cadeia de dano paralela aqui.
	/// </summary>
	private sealed record FonteDeDano(
		string Nome,
		Action<ServerPlayer>? Palco,
		Action<ServerPlayer>? Vespera,
		Action<ServerPlayer> Disparar,
		bool NoEspaco = false,
		bool NaZonaDaDestruicao = false);

	/// <summary>Como o corpo entra na medicao. Ver o cabecalho -- as tres familias.</summary>
	private enum ModoDaMedicao { EmCena, ForaDeCena, EmCenaSemEscudo }

	/// <summary>
	/// A FOTO DO CORPO. Vida, morte, nocaute e membros arrancados -- as quatro maneiras de um corpo
	/// sair pior do que entrou.
	///
	/// AS QUATRO E NAO SO A VIDA: tres das dezesseis fontes matam SEM tirar vida nenhuma (o sorteio
	/// por estagio da destruicao, o habitante que morre direto e o sorteio suicida da Final
	/// Explosion). Uma foto que olhasse so a barra daria verde nas tres com o escudo desligado.
	/// </summary>
	private readonly record struct Marca(double Vida, bool Morto, bool Ko, int Decepados)
	{
		public bool Piorou(Marca antes) =>
			Vida < antes.Vida - 1e-9 || (Morto && !antes.Morto)
			|| (Ko && !antes.Ko) || Decepados > antes.Decepados;

		public override string ToString() =>
			$"vida {Vida:0.##}{(Morto ? ", MORTO" : "")}{(Ko ? ", KO" : "")}"
			+ (Decepados > 0 ? $", {Decepados} membro(s) fora" : "");
	}

	private static Marca Ler(ServerPlayer pl) => new(
		pl.Combate!.Corpo.Vida(), pl.Ficha.dead, pl.Ficha.KO,
		pl.Combate.Corpo.Partes.Count(p => p.Decepado));

	// =====================================================================
	// A BANCADA
	// =====================================================================
	private void EscudoCheca(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _escudoOk++; GD.Print($"[escudo]   OK    {nome}"); return; }
		_escudoFalhou++;
		GD.PrintErr($"[escudo]   FALHA {nome}   {detalhe}");
	}

	public void RodarBancadaDoEscudo()
	{
		_escudoOk = _escudoFalhou = 0;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDoEscudo);

		GD.Print("[escudo] ============ O ESCUDO DA CINEMATICA DE TRANSFORMACAO ============");
		EscudoCheca("a zona de chao da bancada tem colisao carregada (o tiro precisa de chao livre)",
					_pjMapa != null);

		// ============================ O PALCO: ESTA BANCADA MATA UM PLANETA DE VERDADE ============================
		// A fonte 12 (`DispararFinalExplosionDeTerceiro`) forja um corpo de BP 1e12 e detona uma Final
		// Explosion de RAIO MAXIMO na Terra -- e raio maximo + BP acima de dez milhoes leva o planeta
		// (`misc.dm:324-335`, portado em `GameServer.Tecnicas.G3.cs:644`). Isto **e o certo**: a bancada
		// tem que exercitar a fonte inteira, pelo funil de producao.
		//
		// O que estava errado era o resto: a destruicao seguia ate o disco e a Terra ficava morta no
		// mundo do dono -- e o berco, corretamente, passava a fazer 13 racas nascerem em Namek. O
		// cabecalho deste arquivo ja PROMETIA "sem escrever uma linha no registro de planetas mortos"
		// (`GameServer.cs`, na chamada desta bancada); agora quem cumpre a promessa e o mecanismo, e nao
		// a memoria de quem escreve a proxima fonte. Ver `PalcoDeMortes`.
		// ======================================================================================================
		string discoAntes = TextoDoLivroDosMortos();
		PalcoDeMortes palco = PalcoDeMortesDeBancada();

		try
		{
			OEstadoEUmSO();
			ACadaFonteEmCena();
			ACadaFonteForaDaCena();
			AInjecaoAvermelhaUmaAUma();
			ACenaInterrompidaDerrubaOEscudo();
			ONpcTemOMesmoEscudo();
			NinguemMorreTransformando();
			OCorpoQueEntraNaCenaJaNoLimiar();
			OPortaoDeMovimentoNaoRegrediu();
		}
		finally
		{
			RecolherOsCorpos();
			palco.Dispose();   // fecha ANTES das duas linhas abaixo: e o fechamento que devolve o mundo
		}

		OPalcoDevolveuOMundo(palco, discoAntes);

		GD.Print($"[escudo] ============ {_escudoOk} passaram, {_escudoFalhou} falharam ============");
	}

	/// <summary>
	/// 9) A BANCADA DEVOLVEU O MUNDO? -- as duas metades, e nenhuma delas sozinha vale nada.
	///
	/// ============================ A LICAO QUE ESTAS TRES LINHAS CARREGAM ============================
	/// A `--bercovivo` tinha 72 provas VERDES enquanto 13 racas acordavam no planeta errado, porque ela
	/// afirmava a REGRA (a funcao pura `Bercos.Onde`, que continuava certa) e so IMPRIMIA o EFEITO (a
	/// zona em que o corpo acordou). Entao aqui a pergunta e feita ao DISCO, que e o efeito: o texto do
	/// `planetas-mortos.json` antes e depois, byte a byte. Perguntar ao registro em memoria seria
	/// perguntar de novo pela intencao -- ele acabou de ser reposto pelo proprio escopo que se quer
	/// medir, e ficaria verde ate com a gravacao ligada.
	///
	/// E a primeira linha e a que da dentes as outras: se a Final Explosion parar de matar planeta (um
	/// numero que mude, um gate novo), a segunda linha continuaria verde para sempre medindo o nada.
	/// ==========================================================================================
	/// </summary>
	private void OPalcoDevolveuOMundo(PalcoDeMortes palco, string discoAntes)
	{
		GD.Print("[escudo] -- 9) O PALCO DEVOLVEU O MUNDO (a bancada nao pode custar um planeta) --");

		EscudoCheca("(contraprova) a Final Explosion de raio maximo MATOU um planeta de verdade aqui dentro",
					palco.MatouAqui > 0,
					"nenhum planeta morreu -- ou a fonte parou de exercitar o gancho de destruicao");

		EscudoCheca($"...e o planeta VOLTOU a existir no fim ({palco.NomesQueMorreram})",
					!ZonaMorta(ZonaDoEscudo) && !ZonaCondenada(ZonaDoEscudo),
					$"{ZonaDoEscudo.Name} continua condenado depois da bancada");

		EscudoCheca("...e o `planetas-mortos.json` do dono nao mudou um byte (medido no DISCO)",
					TextoDoLivroDosMortos() == discoAntes,
					$"antes [{discoAntes}] depois [{TextoDoLivroDosMortos()}]");
	}

	/// <summary>
	/// O TEXTO CRU DO ARQUIVO DE PLANETAS MORTOS -- e "(nao existe)" quando ele nao existe, que e um
	/// estado legitimo (servidor novo) e tem que ser comparavel como qualquer outro.
	/// </summary>
	private string TextoDoLivroDosMortos()
	{
		try
		{
			return System.IO.File.Exists(CaminhoDosMortos)
				? System.IO.File.ReadAllText(CaminhoDosMortos)
				: "(nao existe)";
		}
		catch (Exception e) { return $"(ilegivel: {e.Message})"; }
	}

	// =====================================================================
	// 0) O ESTADO E UM SO
	// =====================================================================
	/// <summary>
	/// A pergunta *"estou em cinematica?"* nao pode ter DUAS respostas -- duas divergem no dia em
	/// que alguem mexer numa. Esta familia afirma que ela tem UMA, que ela e derivada e que ela e a
	/// MESMA que o portao de movimento e os tres relogios de Ki ja consultavam.
	/// </summary>
	private void OEstadoEUmSO()
	{
		GD.Print("[escudo] -- 0) O ESTADO E UM SO, E ELE E DERIVADO --");

		ServerPlayer v = Forjar("bancada: o estado", ZonaDoEscudo, PontoLivre(), 3_000_000);
		try
		{
			EscudoCheca("o `PrepararCombate` instala o gancho da cinematica em todo corpo",
						v.Combate!.EmCinematica != null);
			EscudoCheca("fora de cena o corpo NAO e intocavel", !v.Combate.Intocavel);

			EscudoCheca("(montagem) o corpo entra em cena pela transformacao de VERDADE", PorEmCena(v),
						$"cena {v.CenaSegundos:0.##}s, forma {v.Forma.Atual}");

			EscudoCheca("em cena o corpo E intocavel", v.Combate.Intocavel);
			EscudoCheca("...e a resposta e a MESMA do `EmCena` que o portao de movimento le",
						v.Combate.EmCinematica!() == (v.CenaSegundos > 0));

			// ============================ NAO HA BIT PRA ESQUECER ============================
			// O escudo e DERIVADO do `CenaSegundos`. Se ele fosse um bit guardado, zerar o prazo
			// deixaria a imunidade pendurada -- e o corpo ficaria invulneravel pra sempre com o
			// resto do jogo achando que a cena acabou. Esta linha e a prova de que nao ha esse bit.
			// ==============================================================================
			double guardado = v.CenaSegundos;
			v.CenaSegundos = 0;
			EscudoCheca("zerar o prazo da cena derruba o escudo NO MESMO INSTANTE (nao ha bit guardado)",
						!v.Combate.Intocavel);
			v.CenaSegundos = guardado;
			EscudoCheca("...e devolver o prazo o levanta de novo", v.Combate.Intocavel);

			// ============================ O FUNIL DEVOLVE SE FERIU ============================
			// Quem conta nocaute e morte DEPOIS do laco le o ESTADO do corpo. Se o funil dissesse
			// "feri" pra um corpo protegido, o chamador mataria o protegido pela vida que ele JA
			// TINHA -- o escudo derrubando exatamente quem ele existe pra proteger.
			// ============================================================================
			BodyPart torso = v.Combate.Corpo.Achar("Torso")!;
			double vidaAntes = torso.Vida;
			EscudoCheca("o funil `CombatState.Ferir` DEVOLVE FALSO em cena (o chamador precisa saber)",
						!v.Combate.Ferir(torso, 50, letal: true));
			EscudoCheca("...e o membro nao perdeu um ponto de vida",
						Math.Abs(torso.Vida - vidaAntes) < 1e-9, $"{vidaAntes:0.##} -> {torso.Vida:0.##}");

			// A PORTA CRUA CONTINUA ABERTA, E DE PROPOSITO: `Corpo.Ferir` nao tem crivo porque as
			// bancadas machucam corpo de proposito, e uma bancada que nao consegue mais quebrar um
			// membro nao mede nada. O crivo mora no `CombatState`, que e por onde o JOGO passa.
			v.Combate.Corpo.Ferir(torso, 50, letal: true);
			EscudoCheca("...mas a porta CRUA (`Corpo.Ferir`) continua sem crivo, pras bancadas",
						torso.Vida < vidaAntes, $"{vidaAntes:0.##} -> {torso.Vida:0.##}");
		}
		finally { RecolherOsCorpos(); }
	}

	// =====================================================================
	// A TABELA DAS FONTES
	// =====================================================================
	/// <summary>
	/// TODA FONTE DE DANO QUE ENCOSTA NUM CORPO NESTE PORT, uma linha cada.
	///
	/// A lista nasceu de uma varredura por `CombatState.Ferir`, `Corpo.Ferir`, `EspalharDanoG3` e
	/// `Morrer()` no servidor inteiro -- e nao de memoria. As quatro que **nao passavam por crivo
	/// nenhum** antes desta correcao estao marcadas no comentario de cada uma.
	/// </summary>
	private List<FonteDeDano> TabelaDeFontes() =>
	[
		// ---- 1. O SOCO, pelo caminho da tecla de golpe ----
		new FonteDeDano("soco (`Atacar`, o caminho da tecla de golpe)", null, null, DispararSoco),

		// ---- 2. O DANO DE KI PRONTO: o impacto do projetil E o do embate de ki ----
		// O embate de ki nao tem porta propria: quem vence solta o feixe e o `AndarProjetil` o leva
		// ate o corpo, onde ele vira exatamente esta chamada. Cobrir esta cobre os dois.
		new FonteDeDano("dano de ki pronto (`AplicarDanoPronto` -- o impacto do raio e do embate de ki)",
				  null, null, v => MeleeResolver.AplicarDanoPronto(v.Combate!, 60, letal: true, _rng)),

		// ---- 3. O PROJETIL QUE VOA DE VERDADE ----
		new FonteDeDano("projetil de ki que VOA (`Disparar` + `TickDosProjeteis`)", null, null, DispararProjetil),

		// ---- 4. A TECNICA EM AREA / A EXPLOSAO ----
		new FonteDeDano("tecnica em area e explosao (`EspalharDanoG3`, o `SpreadDamage` deste port)",
				  null, null, DispararTecnicaEmArea),

		// ---- 5. O ESMAGAMENTO POR GRAVIDADE ----
		new FonteDeDano("esmagamento por gravidade (`TickDoEsmagamento`)", PalcoDaGravidade, null,
				  _ => TickDoEsmagamento()),

		// ---- 6. O CALOR DA ESTRELA -- A QUE MATAVA ----
		// Era o unico furo de dano de TERCEIRO no port: o calor nunca passou pelo funil do soco nem
		// pelo do dano em area, entao a imunidade simplesmente nao existia aqui.
		new FonteDeDano("calor da estrela (`TickDoEspaco` -> `Queimar`)", null, null, DispararCalorDaEstrela,
				  NoEspaco: true),

		// ---- 7 e 8. O KAIO-KEN, as duas metades (nenhuma passava por crivo) ----
		new FonteDeDano("Kaio-ken continuo (`EspalharDanoG2`)", null, null,
				  v => EspalharDanoG2(v, 20)),
		new FonteDeDano("Kaio-ken que ESTOURA o corpo (`EstourarG2`)", null, null,
				  v => EstourarG2(v, "bancada: o corpo se desfaz")),

		// ---- 9. A SOBRECARGA DE KI (segurar C) -- nao passava por crivo ----
		new FonteDeDano("sobrecarga de Ki, segurar C (`EspalharDanoDaCarga`)", null, null,
				  v => EspalharDanoDaCarga(v, 20)),

		// ---- 10 e 11. A AURA OF DESTRUCTION, as duas saidas ----
		// O RECUO nao passava por crivo: ele fere quem BATEU, e o crivo do golpe olha quem apanhou.
		new FonteDeDano("recuo da Aura of Destruction (`AplicarAuraDepoisDoGolpe`)", null, null,
				  DispararRecuoDaAura),
		new FonteDeDano("Destruction Explosion, a aura em area (`DestructionExplosion`)", null, null,
				  DispararExplosaoDaAura),

		// ---- 12 e 13. A FINAL EXPLOSION, as duas saidas ----
		new FonteDeDano("Final Explosion: quem esta no raio (`DetonarG3`)", null, null,
				  DispararFinalExplosionDeTerceiro),
		// O SORTEIO SUICIDA nao passava por crivo, e ele le `f.HP`/`f.KO` -- estado VELHO, depois de
		// um dano que o escudo ja recusou. Sem a perna do `Intocavel`, o protegido morria por uma
		// vida que ele nao perdeu.
		new FonteDeDano("Final Explosion: o sorteio SUICIDA de quem a soltou (`DetonarG3`, `misc.dm:319`)",
				  null, PorEmNocaute, DispararFinalExplosionEmSi),

		// ---- 14, 15 e 16. A DESTRUICAO DE PLANETA: tres mortes que nao passam por dano nenhum ----
		// A MAIS TRAICOEIRA das tres, porque e SORTEADA: o mesmo teste rodado de novo daria outro
		// resultado. Aqui ela roda no estagio 3, onde a chance e 100% e o sorteio some.
		new FonteDeDano("destruicao de planeta: o sorteio por estagio (`LimitarVida`, `limit_life()`)",
				  PalcoDeHabitante, null, v => LimitarVida(v.Zone, estagio: 3),
				  NaZonaDaDestruicao: true),
		new FonteDeDano("destruicao de planeta: o habitante que morre DIRETO (`ConsumarDestruicao`)",
				  PalcoDeHabitante, null, DispararDestruicaoDoPlaneta, NaZonaDaDestruicao: true),
		new FonteDeDano("destruicao de planeta: o NOCAUTEADO que morre no fim (`ConsumarDestruicao`)",
				  null, PorEmNocaute, DispararDestruicaoDoPlaneta, NaZonaDaDestruicao: true),
	];

	// =====================================================================
	// 1, 2 e 3) AS TRES FAMILIAS, UMA LINHA POR FONTE
	// =====================================================================
	private void ACadaFonteEmCena()
	{
		GD.Print("[escudo] -- 1) EM CENA, CADA FONTE NAO MACHUCA (uma linha por fonte) --");
		foreach (FonteDeDano f in TabelaDeFontes())
		{
			(bool lesionou, bool montou, string detalhe) = Exercitar(f, ModoDaMedicao.EmCena);
			EscudoCheca($"em cena NAO machuca: {f.Nome}", montou && !lesionou, detalhe);
		}
	}

	private void ACadaFonteForaDaCena()
	{
		GD.Print("[escudo] -- 2) FORA DA CENA, CADA FONTE MACHUCA DE NOVO (o contra-exemplo) --");
		GD.Print("[escudo]      sem esta familia, 'imune pra sempre' passaria verde -- e isso quebra o");
		GD.Print("[escudo]      jogo de um jeito muito pior que o bug original.");
		foreach (FonteDeDano f in TabelaDeFontes())
		{
			(bool lesionou, bool montou, string detalhe) = Exercitar(f, ModoDaMedicao.ForaDeCena);
			EscudoCheca($"fora da cena MACHUCA: {f.Nome}", montou && lesionou, detalhe);
		}
	}

	private void AInjecaoAvermelhaUmaAUma()
	{
		GD.Print("[escudo] -- 3) A INJECAO DO DEFEITO: escudo arrancado, cena CORRENDO --");
		GD.Print("[escudo]      `Combate.EmCinematica = null` e o estado PRE-CORRECAO deste port.");
		GD.Print("[escudo]      Cada linha aqui e a linha 1 da mesma fonte ficando vermelha sozinha.");
		foreach (FonteDeDano f in TabelaDeFontes())
		{
			(bool lesionou, bool montou, string detalhe) = Exercitar(f, ModoDaMedicao.EmCenaSemEscudo);
			EscudoCheca($"com o escudo arrancado (em cena!) MACHUCA: {f.Nome}",
						montou && lesionou, detalhe);
		}
	}

	/// <summary>
	/// UMA MEDICAO: forja o corpo, monta o palco, poe (ou nao) em cena, dispara a fonte e diz se o
	/// corpo saiu pior.
	///
	/// **O CORPO E NOVO A CADA MEDICAO** e recolhido no `finally`. Reaproveitar um corpo entre as
	/// tres familias faria a segunda medir um corpo que a primeira ja machucou -- e a terceira
	/// mediria um cadaver.
	///
	/// `Montou` e a terceira resposta e ela NAO e detalhe: se a transformacao nao acontecer (Ki,
	/// raiva, uma regra nova de forma), o corpo nao entra em cena e a familia 1 daria verde por
	/// AUSENCIA DE CENA -- imunidade nenhuma passando por imunidade perfeita.
	/// </summary>
	private (bool Lesionou, bool Montou, string Detalhe) Exercitar(FonteDeDano f, ModoDaMedicao modo)
	{
		ZoneKey zona = f.NoEspaco ? ZonaDoEspaco
					 : f.NaZonaDaDestruicao ? ZonaDaDestruicaoDoEscudo
					 : ZonaDoEscudo;

		Vec2 onde = f.NoEspaco ? EstrelaDaBancadaDoEscudo().Pos
				  : f.NaZonaDaDestruicao ? new Vec2(2000, 2000)
				  : PontoLivre();

		ServerPlayer v = Forjar("bancada: a cobaia", zona, onde, 3_000_000);
		try
		{
			f.Palco?.Invoke(v);

			if (modo != ModoDaMedicao.ForaDeCena && !PorEmCena(v))
				return (false, false, "nao consegui por o corpo em cena pela transformacao de verdade");

			f.Vespera?.Invoke(v);

			// A INJECAO. O prazo da cena continua correndo -- o que morre e so o escudo.
			if (modo == ModoDaMedicao.EmCenaSemEscudo) v.Combate!.EmCinematica = null;

			Marca antes = Ler(v);
			f.Disparar(v);
			Marca depois = Ler(v);

			return (depois.Piorou(antes), true, $"{antes} -> {depois}");
		}
		finally { RecolherOsCorpos(); }
	}

	// =====================================================================
	// 4) A CENA INTERROMPIDA DERRUBA O ESCUDO
	// =====================================================================
	/// <summary>
	/// O modo de falha oposto ao do pedido, e o mais caro dos dois: um escudo que nao cai e um
	/// personagem invulneravel pra sempre.
	///
	/// As quatro saidas da cena sao medidas pelo CODIGO DE PRODUCAO (`TickDaForma` abatendo o
	/// prazo), e nao apagando campo a mao -- que testaria a bancada.
	/// </summary>
	private void ACenaInterrompidaDerrubaOEscudo()
	{
		GD.Print("[escudo] -- 4) A CENA INTERROMPIDA DERRUBA O ESCUDO --");
		const double Dt = 1.0 / 30.0;

		// ---- a) O PRAZO VENCENDO ----
		ServerPlayer a = Forjar("bancada: o prazo", ZonaDoEscudo, PontoLivre(), 3_000_000);
		try
		{
			if (!PorEmCena(a)) { EscudoCheca("(montagem) o corpo do prazo entra em cena", false); }
			else
			{
				double preso = a.CenaSegundos;
				for (double t = 0; t < preso + 1.0; t += Dt) TiqueDosRelogiosDeKi(a, Dt);
				EscudoCheca($"o PRAZO vencendo derruba o escudo (cena de {preso:0.#}s, pelo `TickDaForma`)",
							!a.Combate!.Intocavel, $"cena {a.CenaSegundos:0.###}");
				EscudoCheca("...e o corpo volta a perder vida pelo funil",
							a.Combate.Ferir(a.Combate.Corpo.Achar("Torso")!, 40, letal: true));
			}
		}
		finally { RecolherOsCorpos(); }

		// ---- b) O NOCAUTE NO MEIO ----
		// O `Reverter` sai pelo `AnunciarForma`, que marca a cena da BASE em ZERO: o escudo cai
		// junto com a forma, no mesmo gesto, sem uma segunda linha pra alguem esquecer.
		ServerPlayer b = Forjar("bancada: o nocaute", ZonaDoEscudo, PontoLivre(), 3_000_000);
		try
		{
			if (!PorEmCena(b)) { EscudoCheca("(montagem) o corpo do nocaute entra em cena", false); }
			else
			{
				EscudoCheca("(montagem) ele esta protegido antes de cair", b.Combate!.Intocavel);
				b.Ficha.KO = true;
				TiqueDosRelogiosDeKi(b, Dt);
				EscudoCheca("o NOCAUTE no meio da cena derruba o escudo (a forma reverte e zera a cena)",
							!b.Combate.Intocavel, $"cena {b.CenaSegundos:0.###}, forma {b.Forma.Atual}");
			}
		}
		finally { RecolherOsCorpos(); }

		// ---- c) A MORTE NO MEIO ----
		ServerPlayer c = Forjar("bancada: a morte", ZonaDoEscudo, PontoLivre(), 3_000_000);
		try
		{
			if (!PorEmCena(c)) { EscudoCheca("(montagem) o corpo da morte entra em cena", false); }
			else
			{
				c.Ficha.dead = true;
				TiqueDosRelogiosDeKi(c, Dt);
				EscudoCheca("a MORTE no meio da cena derruba o escudo (mesmo caminho do nocaute)",
							!c.Combate!.Intocavel, $"cena {c.CenaSegundos:0.###}, forma {c.Forma.Atual}");
			}
		}
		finally { RecolherOsCorpos(); }

		// ---- d) A TROCA DE ZONA ----
		// AQUI A RESPOSTA CERTA E "O ESCUDO CONTINUA", e a linha existe pra dizer POR QUE: o relogio
		// da cena nao para de escorrer numa troca de zona (o proprio `PodeMexerOCorpo` documenta
		// isso), entao a cinematica continua acontecendo -- e o corpo continua preso nela. Um escudo
		// que caisse aqui deixaria o jogador morrer por ter atravessado uma porta no meio da estreia.
		// O que se prova e que ele acaba PELO PRAZO, e nao que ele nunca acaba.
		ServerPlayer d = Forjar("bancada: a troca de zona", ZonaDoEscudo, PontoLivre(), 3_000_000);
		try
		{
			if (!PorEmCena(d)) { EscudoCheca("(montagem) o corpo da troca de zona entra em cena", false); }
			else
			{
				double preso = d.CenaSegundos;
				ZoneList(d.Zone.Hash).Remove(d);
				d.Zone = ZonaDaDestruicaoDoEscudo;
				ZoneList(d.Zone.Hash).Add(d);
				EscudoCheca("a TROCA DE ZONA nao derruba o escudo -- e nao deve: o relogio da cena continua",
							d.Combate!.Intocavel, $"cena {d.CenaSegundos:0.###}");
				for (double t = 0; t < preso + 1.0; t += Dt) TiqueDosRelogiosDeKi(d, Dt);
				EscudoCheca("...e ele acaba PELO PRAZO na zona nova (nao ficou preso pra sempre)",
							!d.Combate.Intocavel, $"cena {d.CenaSegundos:0.###}");
			}
		}
		finally { RecolherOsCorpos(); }

		// ---- e) O LOGOUT ----
		// O gancho e um `Func<bool>` que fecha sobre o `ServerPlayer`. Quem desloga sai do
		// `_players`; quem volta e um `ServerPlayer` NOVO, montado por um `PrepararCombate` novo, com
		// `CenaSegundos` em zero (o campo e de runtime e nao vai pro save). Nao ha escudo pra vazar.
		ServerPlayer e1 = Forjar("bancada: o que deslogou", ZonaDoEscudo, PontoLivre(), 3_000_000);
		bool entrou = PorEmCena(e1);
		RecolherOsCorpos();
		ServerPlayer e2 = Forjar("bancada: o que voltou", ZonaDoEscudo, PontoLivre(), 3_000_000);
		try
		{
			EscudoCheca("(montagem) o corpo que 'deslogou' estava em cena", entrou);
			EscudoCheca("quem VOLTA depois do logout nasce sem cena e sem escudo",
						!e2.Combate!.Intocavel && Math.Abs(e2.CenaSegundos) < 1e-9,
						$"cena {e2.CenaSegundos:0.###}");
		}
		finally { RecolherOsCorpos(); }
	}

	// =====================================================================
	// 5) O NPC TEM O MESMO ESCUDO
	// =====================================================================
	/// <summary>
	/// *"pra ninguem poder MATAR o player **ou NPCs** enquanto ele transforma"*.
	///
	/// **QUEM E NPC AOS OLHOS DO JOGO E QUEM TEM `Papel`** (o `isNPC` deste port), e nao quem tem
	/// `Peer` nulo -- e a diferenca importa muito aqui, porque as tres mortes da destruicao de
	/// planeta perguntam exatamente pelo `Papel`. Por isso o corpo desta familia sai do molde
	/// `cidadao` do `npcs.json`, como o de um habitante de verdade.
	///
	/// A tabela inteira roda de novo. Uma amostra ("o soco tambem nao machuca o NPC") deixaria de
	/// fora justamente as fontes que so pegam NPC.
	/// </summary>
	private void ONpcTemOMesmoEscudo()
	{
		GD.Print("[escudo] -- 5) O NPC TEM O MESMO ESCUDO QUE O JOGADOR --");

		if (_moldes?.Get("cidadao") == null)
		{
			EscudoCheca("o molde `cidadao` esta carregado (sem ele nao ha NPC pra medir)", false,
						"npcs.json nao carregou");
			return;
		}

		ServerPlayer sonda = Forjar("bancada: o NPC", ZonaDoEscudo, PontoLivre(), 3_000_000);
		PalcoDeHabitante(sonda);
		EscudoCheca("o corpo sem dono recebe o gancho pelo MESMO `PrepararCombate` (nao ha crivo por tipo)",
					sonda.Combate!.EmCinematica != null && sonda.Papel != null);
		RecolherOsCorpos();

		foreach (FonteDeDano f in TabelaDeFontes())
		{
			(bool lesionou, bool montou, string detalhe) = Exercitar(f with { Palco = ComoNpc(f.Palco) },
																	 ModoDaMedicao.EmCena);
			EscudoCheca($"NPC em cena NAO machuca: {f.Nome}", montou && !lesionou, detalhe);
		}
	}

	/// <summary>Encadeia o palco da fonte com o carimbo de NPC. O `Papel` entra SEMPRE.</summary>
	private Action<ServerPlayer> ComoNpc(Action<ServerPlayer>? original) => v =>
	{
		original?.Invoke(v);
		PalcoDeHabitante(v);
	};

	// =====================================================================
	// 6) NINGUEM MORRE TRANSFORMANDO -- O PEDIDO LITERAL DO DONO
	// =====================================================================
	/// <summary>
	/// *"pra ninguem poder MATAR o player ou NPCs enquanto ele transforma"*.
	///
	/// ============================ A CENA MAIS LONGA DO JOGO, INTEIRA, DEBAIXO DE FOGO ============================
	/// Nao e uma amostra: e a maior `SegundosPreso` do catalogo (hoje os **140 s** do SSJ3), tique a
	/// tique a 30 Hz, com **nove fontes letais em cima do corpo em cada tique** e o `TickDaForma` de
	/// producao correndo junto. O corpo fica DENTRO de uma estrela o tempo todo -- a fonte que
	/// literalmente matava.
	///
	/// Uma cena curta nao serviria: o defeito que o dono descreveu e o de uma estreia LONGA, e um
	/// escudo que valesse so nos primeiros segundos passaria numa amostra de oito.
	///
	/// **E O CONTRA-EXEMPLO MORA NA MESMA RODADA**: assim que o prazo vence, a mesma barragem, no
	/// mesmo corpo, no mesmo laco, tem que MATAR. Sem isso a familia inteira ficaria verde com um
	/// corpo imortal.
	/// =========================================================================================================
	/// </summary>
	private void NinguemMorreTransformando()
	{
		GD.Print("[escudo] -- 6) NINGUEM MORRE TRANSFORMANDO (a cena mais longa, debaixo de fogo) --");

		Cinematica? maisLonga = Cinematicas.Todas.MaxBy(c => c.SegundosPreso);
		if (maisLonga == null || Catalogo.Def(maisLonga.Forma) is not { } def)
		{
			EscudoCheca("achei a cena mais longa do catalogo", false);
			return;
		}

		Estrela estrela = EstrelaDaBancadaDoEscudo();
		ServerPlayer v = Forjar("bancada: o que nao pode morrer", ZonaDoEspaco, estrela.Pos, 3_000_000);
		ServerPlayer algoz = Forjar("bancada: a barragem", ZonaDoEspaco,
									new Vec2(estrela.Pos.X, estrela.Pos.Y + 20), 1e12);
		try
		{
			// A FORMA E FORCADA PELAS TRES LINHAS DO ADMIN (`Entrar` + `AplicarForma` +
			// `AnunciarForma`), que e o que a `--formasteste` ja faz: subir a escada ate o SSJ3 pela
			// tecla C exigiria BP, maestria e tres estreias, e nada disso e o que se quer provar
			// aqui. O que NAO se pode pular e o `AnunciarForma` -- ele e o funil unico que anota o
			// prazo da cena, e escrever `CenaSegundos` a mao testaria a bancada.
			v.Ficha.Statify();
			v.Ficha.Ki = v.Ficha.MaxKi;
			string antesDaForma = v.Forma.Atual;
			bool estreia = v.Forma.Entrar(def.Id);
			AplicarForma(v);
			v.Ficha.Ki = v.Ficha.MaxKi;
			AnunciarForma(v, antesDaForma, def.Id, estreia);

			double preso = v.CenaSegundos;
			EscudoCheca($"(montagem) a cena mais longa do jogo e a de `{def.Id}` e ela prende o corpo",
						preso > 0, $"{preso:0.#}s");
			if (preso <= 0) return;

			algoz.Facing = Facing.North;
			PrepararAura(algoz);

			const double Dt = 1.0 / 30.0;
			int tiques = 0;
			double andado = 0;
			bool morreuEmCena = false, koEmCena = false;
			double vidaMinimaEmCena = v.Combate!.Corpo.Vida();

			while (andado < preso)
			{
				Barragem(v, algoz, estrela);
				TiqueDosRelogiosDeKi(v, Dt);
				andado += Dt;
				tiques++;

				if (v.CenaSegundos <= 0) break;   // ja saiu da cena: o resto e da segunda metade
				vidaMinimaEmCena = Math.Min(vidaMinimaEmCena, v.Combate.Corpo.Vida());
				morreuEmCena |= v.Ficha.dead;
				koEmCena |= v.Ficha.KO;
			}

			EscudoCheca($"a cena de {preso:0.#}s correu inteira ({tiques} tiques de barragem letal)",
						tiques > preso * 25, $"{tiques} tiques");
			EscudoCheca("NINGUEM MORREU TRANSFORMANDO -- o corpo sai VIVO da cena mais longa do jogo",
						!morreuEmCena && !v.Ficha.dead, "");
			EscudoCheca("...e nem caiu (nocaute em cena tambem seria perder a estreia)", !koEmCena);
			EscudoCheca("...e nao perdeu UM ponto de vida no caminho",
						Math.Abs(vidaMinimaEmCena - v.Combate.Corpo.Vida()) < 1e-9
						&& v.Combate.Corpo.Vida() >= 100 - 1e-9,
						$"minimo {vidaMinimaEmCena:0.###}, fim {v.Combate.Corpo.Vida():0.###}");
			EscudoCheca("...e nenhum membro foi arrancado",
						!v.Combate.Corpo.Partes.Any(p => p.Decepado));

			// ---- E AGORA A MESMA BARRAGEM, NO MESMO CORPO, FORA DA CENA ----
			EscudoCheca("(o prazo venceu) o escudo caiu", !v.Combate.Intocavel,
						$"cena {v.CenaSegundos:0.###}");

			int depois = 0;
			while (!v.Ficha.dead && depois++ < 3000)
			{
				Barragem(v, algoz, estrela);
				TiqueDosRelogiosDeKi(v, Dt);
			}
			EscudoCheca("...e a MESMA barragem, no MESMO corpo, MATA assim que a cena acaba",
						v.Ficha.dead, $"{depois} tiques depois do fim da cena");
		}
		finally { RecolherOsCorpos(); }
	}

	/// <summary>
	/// NOVE FONTES LETAIS NUM TIQUE. As acompanhantes sao forjadas UMA vez pelo chamador -- forjar
	/// dentro do laco custaria 4.200 `PorNoMundo` e a bancada mediria o forjador.
	/// </summary>
	private void Barragem(ServerPlayer v, ServerPlayer algoz, Estrela estrela)
	{
		algoz.Pos = new Vec2(v.Pos.X, v.Pos.Y + 20);
		algoz.Combate!.Recarga = 0;

		Atacar(algoz, Protocol.Golpe.Pesado);                                   // 1. soco
		MeleeResolver.AplicarDanoPronto(v.Combate!, 200, letal: true, _rng);    // 2. raio / embate
		EspalharDanoG3(v, algoz, 200, letal: true);                             // 3. tecnica em area
		TickDoEsmagamento();                                                    // 4. esmagamento
		TickDoEspaco(v);                                                        // 5. calor da estrela
		EspalharDanoG2(v, 50);                                                  // 6. Kaio-ken
		EstourarG2(v, "bancada: o corpo se desfaz");                            // 7. Kaio-ken estoura
		EspalharDanoDaCarga(v, 50);                                             // 8. sobrecarga de Ki
		AplicarAuraDepoisDoGolpe(algoz, v, 5000, ehKi: false);                  // 9. recuo da aura
	}

	// =====================================================================
	// 8) O CORPO QUE ENTRA NA CENA JA NO LIMIAR
	// =====================================================================
	/// <summary>
	/// ============================ A FAMILIA QUE AS GUARDAS ANTES DO LACO EXISTEM PRA ATENDER ============================
	/// Cinco fontes nao decidem nocaute e morte dentro do laco de dano: elas ferem o corpo e, DEPOIS,
	/// perguntam ao ESTADO dele -- `DeveNocautear()`, `DeveMorrer()`, `f.HP &lt;= 10`, `f.KO`. Por isso
	/// elas ganharam guarda ANTES do laco, e nao so o crivo do funil.
	///
	/// **E o unico jeito de ver essa guarda trabalhando e com um corpo JA ABAIXO DO LIMIAR.** E foi
	/// preciso errar isto uma vez pra descobrir: a primeira versao punha o corpo a 21% -- **um ponto
	/// ACIMA** do `LimiarQuebra` -- e com esse fixture arrancar a guarda do `Queimar` da estrela nao
	/// mudava nada, porque `DeveNocautear()` de um corpo a 21% responde nao de qualquer jeito. A
	/// bancada estava verde com e sem a guarda, ou seja nao media a guarda.
	///
	/// O fixture certo e <see cref="PorAbaixoDoLimiar"/>: 9% de vida em cada membro e `KO` ainda
	/// FALSO -- um corpo que ja deveria ter caido e nao caiu porque ninguem perguntou. E o estado em
	/// que qualquer um chega numa briga longa, e o exato estado em que a pergunta feita depois de um
	/// dano que NAO ACONTECEU derruba quem o escudo protege.
	///
	/// As familias 1 a 3 nao cobrem isto e nunca cobririam: elas medem se o corpo PERDEU VIDA, e o
	/// defeito daqui e um nocaute (ou uma morte) **sem perda de vida nenhuma**.
	/// ============================================================================================================
	/// </summary>
	private void OCorpoQueEntraNaCenaJaNoLimiar()
	{
		GD.Print("[escudo] -- 8) O CORPO QUE ENTRA NA CENA JA ABAIXO DO LIMIAR (as guardas ANTES do laco) --");

		// `SoMorte` existe por UMA fonte, e a razao e uma armadilha de bancada que ja mordeu: o
		// nocaute e a PRE-CONDICAO do `ConsumarDestruicao` (`if(M.KO && !M.dead)`), entao o fixture
		// precisa carimba-lo -- e um observavel "caiu ou morreu" leria de volta o proprio carimbo e
		// reprovaria com o jogo certo. Pra essa fonte, e so a morte que conta.
		(string Nome, Action<ServerPlayer> Disparar, bool NoEspaco, bool NaDestruicao, bool SoMorte)[] fontes =
		[
			("calor da estrela (`Queimar` -> `DeveNocautear`/`DeveMorrer`)",
			 DispararCalorDaEstrela, true, false, false),
			("Kaio-ken continuo (`EspalharDanoG2` -> `DeveNocautear`)",
			 v => EspalharDanoG2(v, 20), false, false, false),
			("Kaio-ken que estoura o corpo (`EstourarG2`)",
			 v => EstourarG2(v, "bancada"), false, false, false),
			("sobrecarga de Ki (`EspalharDanoDaCarga` -> `DeveNocautear`)",
			 v => EspalharDanoDaCarga(v, 20), false, false, false),
			("Final Explosion: o sorteio suicida (le `f.HP <= 10`, estado VELHO)",
			 DispararFinalExplosionEmSi, false, false, false),
			("destruicao de planeta: o nocauteado (`ConsumarDestruicao` -> `Morrer`)",
			 v => { v.Ficha.KO = true; DispararDestruicaoDoPlaneta(v); }, false, true, true),
		];

		foreach ((string nome, Action<ServerPlayer> disparar, bool espaco, bool destr, bool soMorte)
				 in fontes)
		{
			ZoneKey zona = espaco ? ZonaDoEspaco : destr ? ZonaDaDestruicaoDoEscudo : ZonaDoEscudo;
			MedirAbaixoDoLimiar(nome, zona, disparar, soMorte);
		}
	}

	/// <summary>
	/// UM CORPO ABAIXO DO LIMIAR, com e sem cena. As duas metades sao obrigatorias: sem a segunda,
	/// uma fonte que nao consegue derrubar nem um corpo em farrapos daria verde na primeira pra
	/// sempre.
	///
	/// O OBSERVAVEL E "CAIU OU MORREU", e nao so um dos dois: o `Queimar` da estrela responde as
	/// DUAS perguntas em sequencia (a morte primeiro, o nocaute depois) e o `EstourarG2` responde a
	/// segunda por dentro. Escolher um dos dois por fonte seria escolher qual metade nao medir.
	/// </summary>
	private void MedirAbaixoDoLimiar(string nome, ZoneKey zona, Action<ServerPlayer> disparar,
									 bool soMorte)
	{
		Vec2 onde = zona.Equals(ZonaDoEspaco) ? EstrelaDaBancadaDoEscudo().Pos
				  : zona.Equals(ZonaDaDestruicaoDoEscudo) ? new Vec2(2000, 2000)
				  : PontoLivre();

		bool Caiu(ServerPlayer p) => p.Ficha.dead || (!soMorte && p.Ficha.KO);

		// ---- EM CENA: nao cai ----
		ServerPlayer v = Forjar("bancada: o que ja estava caindo", zona, onde, 3_000_000);
		bool montou = PorEmCena(v);
		if (montou) { PorAbaixoDoLimiar(v); disparar(v); }
		bool caiuEmCena = montou && Caiu(v);
		string foto = montou ? $"{Ler(v)}" : "nao entrou em cena";
		RecolherOsCorpos();

		EscudoCheca($"abaixo do limiar, EM CENA nao {(soMorte ? "morre" : "cai nem morre")}: {nome}",
					montou && !caiuEmCena, foto);

		// ---- FORA DA CENA: cai ----
		ServerPlayer w = Forjar("bancada: o mesmo corpo sem cena", zona, onde, 3_000_000);
		PorAbaixoDoLimiar(w);
		disparar(w);
		bool caiuFora = Caiu(w);
		string fotoFora = $"{Ler(w)}";
		RecolherOsCorpos();

		EscudoCheca($"...e FORA da cena {(soMorte ? "morre" : "cai ou morre")}: {nome}", caiuFora, fotoFora);
	}

	/// <summary>
	/// TODO MEMBRO A 9% E `KO` AINDA FALSO -- abaixo do `LimiarQuebra` (20%) e abaixo dos dez pontos
	/// de vida que o sorteio suicida da Final Explosion le.
	///
	/// **NAO E UM CORPO IMPOSSIVEL**: ninguem checa `DeveNocautear` por conta propria: quem checa e
	/// quem FERE. Um corpo que levou o ultimo golpe e depois ficou parado esta exatamente assim, e e
	/// esse corpo que aperta C pra virar Super Saiyajin.
	///
	/// A VIDA E ESCRITA E NAO FERIDA: o piso do `Corpo.Ferir` nao-letal para em 19,8% do maximo, e o
	/// letal decepa membro no caminho. Montar o fixture pela porta de dano entregaria outro corpo,
	/// e a medicao comecaria depois da pergunta.
	/// </summary>
	private static void PorAbaixoDoLimiar(ServerPlayer v)
	{
		foreach (BodyPart p in v.Combate!.Corpo.Partes) p.Vida = p.VidaMax * 0.09;
		v.Combate.SincronizarVida();
		v.Ficha.KO = false;
	}

	// =====================================================================
	// 7) O PORTAO DE MOVIMENTO NAO REGREDIU
	// =====================================================================
	/// <summary>
	/// O portao de MOVIMENTO durante a cinematica ja existia e ja funcionava. O risco desta
	/// correcao nunca foi quebra-lo por edicao -- e sim alguem, um dia, dar ao escudo um estado
	/// PROPRIO e os dois passarem a discordar. Estas linhas prendem os dois ao mesmo `EmCena`:
	/// eles tem que subir e cair no MESMO tique.
	/// </summary>
	private void OPortaoDeMovimentoNaoRegrediu()
	{
		GD.Print("[escudo] -- 7) O PORTAO DE MOVIMENTO NAO REGREDIU (e ele e o MESMO estado) --");

		ServerPlayer v = Forjar("bancada: o portao", ZonaDoEscudo, PontoLivre(), 3_000_000);
		try
		{
			EscudoCheca("fora de cena o corpo se mexe", PodeMexerOCorpo(v));
			if (!PorEmCena(v)) { EscudoCheca("(montagem) o corpo entra em cena", false); return; }

			EscudoCheca("em cena o corpo NAO se mexe (o portao continua fechando)", !PodeMexerOCorpo(v));
			EscudoCheca("...e o escudo esta de pe no mesmo instante", v.Combate!.Intocavel);

			// O TIQUE EXATO. Se um dia o escudo ganhar estado proprio, este par se separa aqui e em
			// nenhum outro lugar -- e a divergencia de um tique e a que vira "as vezes".
			const double Dt = 1.0 / 30.0;

			// O PRAZO E FOTOGRAFADO ANTES DO LACO, e nao lido dentro dele. Lido dentro, o teto do
			// `for` ENCOLHE a cada tique enquanto o `t` cresce, e os dois se encontram na METADE da
			// cena: o laco terminava com o corpo ainda preso, e a linha do "volta a se mexer" ficava
			// vermelha com o jogo certo. Foi a propria bancada errando -- e a linha que a pegou.
			double preso = v.CenaSegundos;
			int juntos = 0, separados = 0;
			for (double t = 0; t < preso + 1.0; t += Dt)
			{
				TiqueDosRelogiosDeKi(v, Dt);
				if (PodeMexerOCorpo(v) == !v.Combate.Intocavel) juntos++; else separados++;
			}
			EscudoCheca($"o portao e o escudo caem no MESMO tique, da cena de {preso:0.#}s ate {preso + 1:0.#}s",
						separados == 0, $"{juntos} tiques juntos, {separados} separados");
			EscudoCheca("...e no fim o corpo volta a se mexer", PodeMexerOCorpo(v),
						$"cena {v.CenaSegundos:0.###}, KO {v.Ficha.KO}, morto {v.Ficha.dead}");
		}
		finally { RecolherOsCorpos(); }
	}

	// =====================================================================
	// OS PALCOS E OS DISPAROS
	// =====================================================================
	private void DispararSoco(ServerPlayer v)
	{
		// O algoz nasce ABAIXO olhando pro NORTE -- `MeleeArea.Frente(North)` e (0,-1). Vinte pixels:
		// dentro do alcance de melee sem depender de o `Aproximar` fechar distancia.
		ServerPlayer algoz = Forjar("bancada: quem soca", v.Zone, new Vec2(v.Pos.X, v.Pos.Y + 20), 1e12);
		algoz.Facing = Facing.North;
		Atacar(algoz, Protocol.Golpe.Pesado);
	}

	private void DispararProjetil(ServerPlayer v)
	{
		// ============================ O ATIRADOR FICA NA BOCA DO CORREDOR, E A VITIMA ANDA ============================
		// A primeira versao punha o atirador 200 px a OESTE da cobaia -- e o `CorredorLivre` devolve o
		// COMECO de um corredor que se estende pro LESTE. Ou seja, o atirador nascia fora dele, colado
		// numa pedra da Terra, e o tiro morria no cenario a um tile da mao: as familias 2 e 3 ficaram
		// VERMELHAS e denunciaram que o verde da familia 1 era mentira -- o tiro nunca tinha chegado no
		// corpo. E exatamente o par de linhas que o dono pediu, pegando a propria bancada.
		// =====================================================================================================
		Vec2 bocaDoCorredor = v.Pos;
		v.Pos = new Vec2(bocaDoCorredor.X + 300, bocaDoCorredor.Y);   // 9 tiles adiante, dentro dos 24
		ServerPlayer atirador = Forjar("bancada: quem atira", v.Zone, bocaDoCorredor, 1e12);
		atirador.Facing = Facing.East;

		Disparar(atirador, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 50, Velocidade = 3, AlcanceTiles = 20,
		});

		for (int i = 0; i < 300; i++) TickDosProjeteis(Protocol.TickSeconds);
	}

	private void DispararTecnicaEmArea(ServerPlayer v)
	{
		ServerPlayer autor = Forjar("bancada: quem estoura", v.Zone,
									new Vec2(v.Pos.X + 40, v.Pos.Y), 1e12);
		EspalharDanoG3(v, autor, 200, letal: true);
	}

	private void DispararCalorDaEstrela(ServerPlayer v)
	{
		// UM SEGUNDO DENTRO DA ESTRELA, na cadencia do servidor. Um tique so tambem serviria pro
		// crivo, mas o dano por tique de um corpo forte e pequeno -- e "nao machucou" por dano
		// arredondado a zero seria um verde de mentira.
		for (int i = 0; i < 30; i++) TickDoEspaco(v);
	}

	private void DispararRecuoDaAura(ServerPlayer v)
	{
		// O RECUO FERE QUEM BATEU -- por isso a cobaia entra como ATACANTE aqui. Era o furo mais
		// facil de nao ver: o crivo do golpe olha quem apanhou, e este dano volta pro outro lado.
		ServerPlayer dono = Forjar("bancada: quem tem a aura", v.Zone,
								   new Vec2(v.Pos.X + 20, v.Pos.Y), 3_000_000);
		PrepararAura(dono);
		AplicarAuraDepoisDoGolpe(dono, v, 5000, ehKi: false);
	}

	private void DispararExplosaoDaAura(ServerPlayer v)
	{
		ServerPlayer dono = Forjar("bancada: a aura que estoura", v.Zone,
								   new Vec2(v.Pos.X + 20, v.Pos.Y), 1e12);
		PrepararAura(dono);
		dono.UltimoGolpeRecebido = 100_000;
		DestructionExplosion(dono);
	}

	private void DispararFinalExplosionDeTerceiro(ServerPlayer v)
	{
		ServerPlayer bomba = Forjar("bancada: a bomba", v.Zone, new Vec2(v.Pos.X + 20, v.Pos.Y), 1e12);
		var carga = new CargaG3 { Raio = 25, Contador = 5, Ancora = bomba.Pos };
		_cargaG3[bomba.Id] = carga;
		DetonarG3(bomba, carga);
	}

	private void DispararFinalExplosionEmSi(ServerPlayer v)
	{
		// CONTADOR ACIMA DE 10 + O CORPO CAIDO = MORTE CERTA (`misc.dm:319-323`), sem sorteio. O
		// `prob(70)` do DM daria uma bancada que so acontece 70% das vezes -- e uma bancada que so
		// acontece as vezes nao e bancada. O nocaute e posto pela `Vespera`.
		var carga = new CargaG3 { Raio = 25, Contador = 11, Ancora = v.Pos };
		_cargaG3[v.Id] = carga;
		DetonarG3(v, carga);
	}

	private void DispararDestruicaoDoPlaneta(ServerPlayer v)
	{
		// O ESTADO E PROPRIO DA BANCADA e nao entra no registro `_mortos`: o `ConsumarDestruicao`
		// grava o registro como ele ESTA, entao nada do mundo do dono e escrito. O que ele faz de
		// verdade -- matar habitante, ferir quem esta abaixo do algoz, matar nocauteado e evacuar o
		// resto -- e o que se quer medir.
		var estado = new EstadoDaMorte
		{
			Chave = ChaveDePlaneta.Da(ZonaDaDestruicaoDoEscudo)?.Texto ?? "",
			Nome = ZonaDaDestruicaoDoEscudo.Name,
			Fase = FaseDaMorte.Explodindo,
			Estagio = 3,
			BpDoAlgoz = 1e30,      // ninguem sobrevive por poder: o crivo tem que ser o escudo
			IdDoAlgoz = 0,
			Motivo = "bancada do escudo",
		};
		ConsumarDestruicao(estado, ZonaDaDestruicaoDoEscudo);
	}

	/// <summary>Gravidade muito acima do que o corpo domina: razao 50, dano no teto de 3/s.</summary>
	private static void PalcoDaGravidade(ServerPlayer v)
	{
		v.Ficha.GravMastered = 1;
		v.Ficha.Planetgrav = 50;
	}

	/// <summary>
	/// CARIMBA O CORPO COMO HABITANTE. O `Papel` e o `isNPC` deste port, e ele e o crivo exato que
	/// as tres mortes da destruicao de planeta usam -- sem ele o corpo seria um jogador aos olhos
	/// delas e duas das tres fontes nao teriam o que medir.
	/// </summary>
	private void PalcoDeHabitante(ServerPlayer v)
	{
		if (v.Papel == null && _moldes?.Get("cidadao") is { } molde)
			v.Papel = new PapelDeNpc(molde, (ulong)v.Id);
	}

	/// <summary>
	/// O NOCAUTE POSTO DEPOIS DA CENA (`Vespera`) -- corpo caido nao se transforma, entao poe-lo
	/// antes deixaria a cobaia fora de cena e a familia 1 daria verde por ausencia de cena.
	/// </summary>
	private static void PorEmNocaute(ServerPlayer v) => v.Ficha.KO = true;

	private static void PrepararAura(ServerPlayer pl)
	{
		pl.PoderDaDestruicao.Aprendida = true;
		pl.PoderDaDestruicao.Ligada = true;
		pl.PoderDaDestruicao.Real = 100;
		pl.PoderDaDestruicao.Atual = 100;
	}

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// POE O CORPO EM CENA PELA TRANSFORMACAO DE VERDADE (`Transformar`, a mesma funcao da tecla C).
	///
	/// E ela que passa pelo `AnunciarForma`, que e o unico lugar onde o prazo da cena e anotado --
	/// escrever `CenaSegundos` a mao aqui testaria a bancada, e nao o jogo.
	///
	/// A RAIVA ENTRA PELO GANCHO (`AmigoAbatido`) e nao pelo campo: o tronco Saiyajin cobra o luto
	/// pra sair da base, e um corpo forjado sem raiva nao se transforma -- a bancada mediria o nada.
	/// </summary>
	private bool PorEmCena(ServerPlayer pl)
	{
		AmigoAbatido(pl, "um amigo de bancada", NivelDeRaiva.Extrema);
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		Transformar(pl, subir: true);
		return EmCena(pl);
	}

	/// <summary>
	/// A ESTRELA DESTA BANCADA -- a mesma amarela gerada que a `--solteste` usa, pelo mesmo
	/// `EstrelaDeTeste` (deterministico: mesma seed, mesma estrela).
	/// </summary>
	private Estrela EstrelaDaBancadaDoEscudo() => EstrelaDeTeste(ClasseDeEstrela.Amarela, out _);

	/// <summary>
	/// UM PONTO DE CHAO LIVRE na Terra, pelo `CorredorLivre` da `--projetilteste`.
	///
	/// O CURSOR VOLTA AO COMECO A CADA CHAMADA de medicao porque cada medicao recolhe os proprios
	/// corpos antes da seguinte -- duas medicoes nunca coexistem, entao elas podem usar o mesmo
	/// chao. Sem o reset, quarenta e oito medicoes esgotariam a varredura de 250 linhas.
	/// </summary>
	private Vec2 PontoLivre()
	{
		_pjProximoCorredor = 8;
		return CorredorLivre(24);
	}

	private ServerPlayer Forjar(string nome, ZoneKey zona, Vec2 onde, double bp)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDoEscudo + _escCorpos++,
			Peer = null,
			Name = nome,
			// SAIYAJIN porque a cobaia precisa TRANSFORMAR, e a escada de sangue mais barata de
			// alcancar num corpo forjado e a do Saiyajin (raiva + Ki cheio). Ver `PorEmCena`.
			Race = "Saiyan",
			Genero = "Male",
			Idade = 25,
			Zone = zona,
			Pos = onde,
			Conta = "bancada_escudo",
			Slot = 0,
			Ficha = new Fighter { Race = "Saiyan", BP = bp },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.ChunkAtual = ChunkId.De(onde);

		// `PorNoMundo` chama `Statify` (os ATRIBUTOS) e nao `powerlevel()` (o PODER). Sem esta linha
		// o `expressedBP` nasce ZERO -- e metade das fontes daqui escala com ele. Mesma pegadinha
		// que a bancada do sol e a do projetil ja documentam.
		novo.Ficha.Tick(agoraMs: NowMs());
		return novo;
	}

	/// <summary>
	/// TIRA DO MUNDO TUDO QUE ESTA BANCADA POS NELE -- corpos, cargas de Final Explosion e
	/// projeteis no ar.
	///
	/// A ZONA E LIDA DO CORPO E NAO DA CONSTANTE: o `ConsumarDestruicao` EVACUA quem sobrevive
	/// (e o corpo protegido sobrevive de proposito), entao ele termina a medicao no espaco. Remover
	/// pela zona de origem deixaria um cadaver de bancada na `ZoneList` do universo pra sempre.
	/// </summary>
	private void RecolherOsCorpos()
	{
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			if (pl.Id < IdBaseDoEscudo) continue;
			_cargaG3.Remove(pl.Id);
			_avisoDeEsmagamento.Remove(pl.Id);
			LimparProjeteisDeUmDono(pl.Id, pl.Zone.Hash);
			_players.Remove(pl.Id);
			ZoneList(pl.Zone.Hash).Remove(pl);
		}
	}
}
