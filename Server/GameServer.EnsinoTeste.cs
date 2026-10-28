using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DO ENSINO DE SKILL -- `--ensinoteste`. Porte de
/// `Code/Modules/Skills/Skills Master/teachable.dm`.
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// As duas regras que sustentam este sistema inteiro sao regras sobre o que **nao** acontece, e
/// coisa que nao acontece nao aparece em tela nenhuma:
///
///   * **o aluno nao paga** -- ele precisa TER os marcos, e continua com todos eles depois;
///   * **quem foi ensinado nao repassa** -- e por isso a skill rara nao vira corrente.
///
/// A primeira so se prova comparando o saldo dos dois lados antes e depois; a segunda so se prova
/// com TRES corpos, porque com dois o segundo elo da corrente nao existe pra ser recusado.
/// ==============================================================================
///
/// AS ONZE SECOES:
///  1. O CATALOGO -- `teacher` e `teachCost` sairam do DM, e o segundo MUDA o preco.
///  2. OS PORTOES DESLIGADOS -- o ensino atravessa arvore, raca e `enabled`, e e assim que tem que ser.
///  3. A CORRENTE NAO SE FORMA -- os dois sentidos, com tres corpos.
///  4. O ALUNO NAO PAGA -- e o limiar DISPARA (senao ele seria indistinguivel de limiar nenhum).
///  5. A RECARGA DE 6 s.
///  6. GEOMETRIA, DUPLICATA E PRAZO.
///  7. PERSISTENCIA -- o `wastaught` atravessa o save.
///  8. ESQUECER A LICAO -- so o que foi ensinado, e sem devolver marco.
///  9. A VARREDURA -- nenhuma linha de producao do ensino mexe em marco de ninguem.
/// 10. A LICAO ALCANCA QUEM ESTA EM CENA -- o escudo da cinematica e da carencia e contra golpe.
/// 11. DO CARGO, SO OS ATAQUES DE KI SE ENSINAM (dono, 2026-10-23) -- a chave da Sala do Tempo nao passa adiante.
///
/// OS CORPOS SAO FORJADOS, no molde do `--mestreteste`: sem `Peer`, numa zona pre-feita sem
/// ninguem conectado, entrando e saindo do `_players`/`ZoneList` no mesmo bloco sincrono.
/// **Nao ha nada a devolver no disco** -- ao contrario do discipulado, o ensino nao grava arquivo
/// de mundo nenhum: o que ele muda mora no livro de cada personagem.
/// </summary>
public partial class GameServer
{
	/// <summary>Faixa de ids de bancada -- longe do `_nextId` e da faixa do `--mestreteste`.</summary>
	private const int IdBaseDoEnsinoDeTeste = 90_400;

	private int _ensOk, _ensFalhou;

	private void AfirmarEns(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _ensOk++; GD.Print($"[ensino]   OK    {oque}"); return; }
		_ensFalhou++;
		GD.PrintErr($"[ensino]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDoEnsino()
	{
		_ensOk = _ensFalhou = 0;
		GD.Print("[ensino] ================ ENSINO DE SKILL ================");

		if (_skills == null)
		{
			AfirmarEns("o catalogo de skills carregou (sem ele nao ha o que ensinar)", false);
			GD.Print($"[ensino] ================ {_ensOk} passaram, {_ensFalhou} falharam ================");
			return;
		}

		ZoneKey zona = ZoneKey.Premade(
			Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		ServerPlayer Forjar(int i, string nome, float tilesX, string raca = "Human")
		{
			var novo = new ServerPlayer
			{
				Id = IdBaseDoEnsinoDeTeste + i,
				Peer = null,
				Name = nome,
				Race = raca,
				Genero = "Male",
				Idade = 25,
				Zone = zona,
				Pos = new Vec2(tilesX * ZoneCollision.TileSize, 0),
				// A ASSINATURA E EXIGIDA pelo `Avaliar` (corpo sem dono nao ensina nem aprende),
				// e ela sai da conta + slot. Ver `EhPessoa`.
				Conta = $"bancada_ensino_{i}",
				Slot = 0,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = raca, BP = 10_000 },
				Livro = new SkillBook(),
			};
			novo.Ficha.Class = "Normal";
			PorNoMundo(novo);
			novo.Ficha.Ki = novo.Ficha.MaxKi;
			return novo;
		}

		// TRES CORPOS, e o terceiro e o que prova a regra: com dois nao ha segundo elo de corrente.
		ServerPlayer a = Forjar(1, "bancada: a origem", 0);
		ServerPlayer b = Forjar(2, "bancada: quem foi ensinado", 1);
		ServerPlayer c = Forjar(3, "bancada: quem queria a corrente", 200);
		// O QUARTO SO SERVE A SECAO 10, e e SAIYAJIN porque la ele precisa ESTREAR uma forma (a cena).
		ServerPlayer d = Forjar(4, "bancada: o aluno que se transforma", 600, "Saiyan");

		try
		{
			OCatalogoTrouxeOsCampos();
			OsPortoesEstaoDesligados(a, b);
			ACorrenteNaoSeForma(a, b, c);
			OAlunoNaoPaga(a, b);
			ARecargaDeSeisSegundos(a, b);
			GeometriaDuplicataEPrazo(a, b);
			OWastaughtAtravessaOSave(a, b);
			EsquecerSoOQueFoiEnsinado(a, b);
			NinguemMexeEmMarco();
			ALicaoAlcancaQuemEstaEmCena(a, d);
			DoCargoSoOAtaqueDeKi(a, b);
			OPotentialUnleashedNaoSeEnsina(a, b);   // (2026-10-23) a familia 11m -- o dono: so o ritual da a forma
			OKaiKaiPedeOKiDivino(a, b);             // (2026-10-27) a familia 11k -- o dono: so Kai ou quem tem o ki divino
		}
		catch (Exception e)
		{
			AfirmarEns($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			foreach (ServerPlayer p in new[] { a, b, c, d })
			{
				_players.Remove(p.Id);
				ZoneList(zona.Hash).Remove(p);
			}
		}

		GD.Print($"[ensino] ================ {_ensOk} passaram, {_ensFalhou} falharam ================");
	}

	// =====================================================================
	// OS ALVOS -- ESCOLHIDOS POR CAMPO, NUNCA POR ID ESCRITO A MAO
	// =====================================================================
	/// <summary>
	/// UMA SKILL ENSINAVEL, DESLIGADA E SEM `teachCost` -- o caso normal, e o mais duro: `enabled=0`
	/// significa que **ninguem consegue compra-la**, entao o ensino e a unica porta dela.
	///
	/// Escolhida por predicado e nao por id: um id aqui envelheceria no dia em que o DM fosse
	/// reextraido, e a bancada passaria a testar uma skill que nao existe mais -- calada, porque
	/// `Get()` devolve null e todo `Avaliar` vira `NaoExiste`. Ordenada pelo path pra a escolha ser
	/// a mesma em toda maquina (o dicionario nao promete ordem).
	/// </summary>
	private Skill? AlvoDesligado() => _skills!.Todas
		.Where(s => s.Ensinavel && !s.Arvore && !s.Ligada && s.Nome.Length > 0 && s.CustoDeEnsino == 0)
		.OrderBy(s => s.Path, StringComparer.Ordinal).FirstOrDefault();

	/// <summary>Uma segunda ensinavel, diferente da de cima -- pras secoes que precisam de duas.</summary>
	private Skill? OutroAlvo(Skill? diferenteDe) => _skills!.Todas
		.Where(s => s.Ensinavel && !s.Arvore && s.Nome.Length > 0 && s.Path != diferenteDe?.Path)
		.OrderBy(s => s.Path, StringComparer.Ordinal).FirstOrDefault();

	// =====================================================================
	// 1) O CATALOGO TROUXE OS CAMPOS
	// =====================================================================
	/// <summary>
	/// `teacher` e `teachCost` sao DADO extraido do DM, nao codigo -- e este sistema inteiro pende
	/// deles: sem `teacher` nao ha o que ensinar, sem `teachCost` as skills de cargo passam a custar
	/// o proprio tier.
	/// </summary>
	private void OCatalogoTrouxeOsCampos()
	{
		GD.Print("[ensino] -- 1) O CATALOGO (teacher e teachCost vieram do DM)");

		var ensinaveis = _skills!.Todas.Where(s => s.Ensinavel && !s.Arvore && s.Nome.Length > 0).ToList();
		AfirmarEns("o catalogo tem skills com `teacher = TRUE` (a lista do Teach_Skill existe)",
				   ensinaveis.Count > 0, $"{ensinaveis.Count}");

		// O TETO DA VARREDURA: se um dia isto cair pra zero, o verbo continua existindo e nao
		// ensina mais nada -- calado. E a armadilha 1 da PARTE 3.
		AfirmarEns("...e sao muitas, nao duas por acaso", ensinaveis.Count >= 50, $"{ensinaveis.Count}");

		var comTeachCost = ensinaveis.Where(s => s.CustoDeEnsino > 0).ToList();
		AfirmarEns("o `teachCost` foi extraido (ele so e DECLARADO em `/datum/skill/rank` -- chega "
				 + "nas outras por HERANCA, e sem herdar seriam zero)",
				   comTeachCost.Count > 0, $"{comTeachCost.Count}");

		// ============================ O CAMPO TEM QUE MUDAR O NUMERO ============================
		// Um `teachCost` extraido que por acaso fosse igual ao custo normal em todas as skills seria
		// indistinguivel de nao te-lo extraido. Aqui ele TEM que divergir em pelo menos uma -- e
		// diverge feio: `Open Dead Zone` e tier 6 e custa 2 pra ser ensinada.
		// ====================================================================================
		Skill? divergente = comTeachCost
			.FirstOrDefault(s => SkillCatalog.CustoDoEnsino(s) != SkillCatalog.CustoDe(s));
		AfirmarEns("...e ele MUDA o preco em pelo menos uma skill (senao extrai-lo teria sido inutil)",
				   divergente != null,
				   divergente == null ? "nenhuma diverge" : "");
		if (divergente != null)
			AfirmarEns($"   '{divergente.Nome}': comprar custa {SkillCatalog.CustoDe(divergente)}, "
					 + $"ser ensinado exige {SkillCatalog.CustoDoEnsino(divergente)}",
					   SkillCatalog.CustoDoEnsino(divergente) == divergente.CustoDeEnsino);

		// E O RAMO SEM `teachCost` CAI NO CUSTO NORMALIZADO PELO TIER, que e o `skillcost` do DM em
		// tempo de execucao (`skill.dm:31`) -- e nao o numero cru escrito no arquivo.
		Skill? semTeachCost = ensinaveis.FirstOrDefault(s => s.CustoDeEnsino == 0 && !s.CustoFixo && s.Tier > 1);
		AfirmarEns("sem `teachCost`, o custo de ensino e o custo NORMALIZADO PELO TIER "
				 + "(o `skillcost` que o `/datum/skill/New()` reescreve, e nao o valor cru)",
				   semTeachCost != null && SkillCatalog.CustoDoEnsino(semTeachCost) == semTeachCost.Tier,
				   semTeachCost == null ? "nenhuma candidata" : $"{semTeachCost.Nome} t{semTeachCost.Tier}");
	}

	// =====================================================================
	// 2) OS PORTOES ESTAO DESLIGADOS, E ISSO E A REGRA
	// =====================================================================
	/// <summary>
	/// O `Study` pergunta `canLearnSkill(S) || S.teacher == TRUE` -- e como a skill sempre chega de
	/// uma lista filtrada por `teacher`, **arvore, raca, classe, `enabled` e pre-requisito nao
	/// valem no caminho do ensino**.
	///
	/// A prova e a mesma skill pelas duas portas: o `SkillBook.PodeAprender` (compra) RECUSA, e o
	/// ensino PASSA. Sem esta secao alguem "consertaria" o ensino pondo os portoes de volta, e as
	/// 35 skills desligadas do catalogo -- que so existem pra ser ensinadas -- sumiriam do jogo em
	/// silencio.
	/// </summary>
	private void OsPortoesEstaoDesligados(ServerPlayer a, ServerPlayer b)
	{
		GD.Print("[ensino] -- 2) OS PORTOES DESLIGADOS (arvore, raca, `enabled`)");

		Skill? alvo = AlvoDesligado();
		if (alvo == null) { AfirmarEns("ha uma skill ensinavel e DESLIGADA pra provar o portao", false); return; }

		Zerar(a, b);
		a.Livro.Dar(alvo.Path);
		b.Livro.Conceder(50);

		Recusa compra = b.Livro.PodeAprender(_skills!, alvo.Path, b.Race, b.Class, vilao: false);
		AfirmarEns($"comprar '{alvo.Nome}' e IMPOSSIVEL pela porta normal ({compra})",
				   compra != Recusa.Pode);

		Encostar(a, b, tiles: 1);
		AfirmarEns("...e ensinar a mesma skill PASSA (o `|| S.teacher == TRUE` do `Study`)",
				   Licao(a, b, alvo));
		AfirmarEns($"   e '{alvo.Nome}' entrou mesmo no livro de quem aprendeu", b.Livro.Sabe(alvo.Path));
	}

	// =====================================================================
	// 3) A CORRENTE NAO SE FORMA
	// =====================================================================
	/// <summary>
	/// A REGRA CENTRAL, e ela precisa de tres corpos: A ensina B, **B nao ensina C**.
	///
	/// Os dois sentidos, porque so o "nao" nao prova nada -- um sistema quebrado em que ninguem
	/// ensina ninguem passaria na metade negativa do teste.
	/// </summary>
	private void ACorrenteNaoSeForma(ServerPlayer a, ServerPlayer b, ServerPlayer c)
	{
		GD.Print("[ensino] -- 3) A CORRENTE NAO SE FORMA (wastaught / teacher = FALSE)");

		Skill? alvo = AlvoDesligado();
		if (alvo == null) { AfirmarEns("ha skill ensinavel pra montar a corrente", false); return; }

		Zerar(a, b, c);
		a.Livro.Dar(alvo.Path);
		b.Livro.Conceder(50);
		c.Livro.Conceder(50);

		// ELO 1: A -> B. A origem COMPROU (ou recebeu) a skill, entao ela repassa.
		Encostar(a, b, tiles: 1);
		AfirmarEns("A ensina B (a origem repassa: ela nao aprendeu de ninguem)", Licao(a, b, alvo));
		AfirmarEns("   e a copia de B entrou marcada como ENSINADA (`wastaught = TRUE`)",
				   b.Livro.FoiEnsinada(alvo.Path));

		// ELO 2: B -> C. **NAO.**
		Estacionar(a, 400);
		Encostar(b, c, tiles: 1);
		AfirmarEns("B NAO ensina C -- a corrente para no primeiro elo (`teacher = FALSE` na copia)",
				   !Licao(b, c, alvo) && !c.Livro.Sabe(alvo.Path));
		AfirmarEns("   e a regra pura diz a mesma coisa, sem servidor nenhum por perto",
				   !EnsinoDeSkill.PodeRepassar(b.Livro, alvo)
				   && EnsinoDeSkill.PodeRepassar(a.Livro, alvo));
		AfirmarEns("   e a recusa e a especifica (nao 'nao sabe', nao 'longe')",
				   EnsinoDeSkill.Avaliar(b.Livro, c.Livro, alvo, true, true, false, 1, false)
				   == RecusaDeLicao.AprendeuDeAlguem);

		// A ORIGEM CONTINUA PODENDO: o "nao repassa" e da COPIA, e nao da skill.
		//
		// B VAI PRA UM ENDERECO **DIFERENTE** DO DE A -- e a licao do `Estacionar` do `--mestreteste`
		// mordendo de novo, e do jeito mais traicoeiro: mandar B pros mesmos 400 tiles em que A ja
		// estava empilha os dois, o `AlvoNaFrente(a)` devolve B (distancia zero) em vez de C, e a
		// checagem reprova com `AlunoJaSabe` -- uma vermelha que nao e do jogo. Cada corpo, um
		// endereco.
		Estacionar(b, 500);
		Encostar(a, c, tiles: 1);

		// ============================ A RECARGA E DO MESTRE, E ELA PEGOU AQUI ============================
		// Esta linha nasceu de uma vermelha: A tinha acabado de ensinar B, e a tentativa com C caiu na
		// recarga de 6 s -- que e **por mestre e nao por par**. A bancada estava medindo o relogio e
		// nao a corrente.
		//
		// Em vez de so zerar e seguir, a vermelha virou afirmacao: primeiro se PROVA que a recarga e
		// do mestre (ele nao pode ensinar a um TERCEIRO tao cedo), e so depois o relogio anda.
		// ==========================================================================================
		AfirmarEns("logo depois de ensinar B, A nao ensina C: a recarga e do MESTRE, nao do par",
				   !Licao(a, c, alvo) && !c.Livro.Sabe(alvo.Path));
		a.RecargaDaLicao = 0;

		AfirmarEns("mas A ainda ensina C -- quem trava e a COPIA de B, nao a skill",
				   Licao(a, c, alvo));

		// A LISTA QUE A INTERFACE LE CONCORDA COM AS TRES AFIRMACOES ACIMA.
		AfirmarEns("a lista de repassaveis de B nao inclui o que ensinaram a ele",
				   !EnsinoDeSkill.Repassaveis(_skills!, b.Livro).Any(s => s.Path == alvo.Path)
				   && EnsinoDeSkill.Repassaveis(_skills!, a.Livro).Any(s => s.Path == alvo.Path));

		Estacionar(c, 200);
	}

	// =====================================================================
	// 4) O ALUNO NAO PAGA (E O LIMIAR DISPARA)
	// =====================================================================
	/// <summary>
	/// A descoberta que muda o sistema: `if(skillpoints >= teachingcost)` e um LIMIAR, e o proprio
	/// DM avisa dentro da caixa que *"learning won't remove Milestones"* (`teachable.dm:48`).
	///
	/// As duas metades tem que ser provadas juntas: que o saldo nao muda **e** que o limiar recusa
	/// de verdade. Sem a segunda o limiar seria indistinguivel de limiar nenhum (armadilha 3 da
	/// PARTE 3), e um sistema que nunca cobra nada passaria na primeira metade sozinho.
	/// </summary>
	private void OAlunoNaoPaga(ServerPlayer a, ServerPlayer b)
	{
		GD.Print("[ensino] -- 4) O ALUNO NAO PAGA, MAS PRECISA TER (o limiar)");

		Skill? alvo = AlvoDesligado();
		if (alvo == null) { AfirmarEns("ha skill ensinavel pra medir o custo", false); return; }
		int custo = EnsinoDeSkill.Custo(alvo);

		// (a) COM MARCOS DE SOBRA: aprende, e o saldo nao se mexe.
		Zerar(a, b);
		a.Livro.Dar(alvo.Path);
		b.Livro.Conceder(custo + 7);
		int livresAntes = b.Livro.MarcosLivres, totaisAntes = b.Livro.MarcosTotais;
		int doMestreAntes = a.Livro.MarcosLivres;

		Encostar(a, b, tiles: 1);
		AfirmarEns($"com {livresAntes} marcos (custo {custo}), a licao acontece", Licao(a, b, alvo));
		AfirmarEns("O ALUNO NAO PERDE NENHUM MARCO -- o custo e limiar, nao preco",
				   b.Livro.MarcosLivres == livresAntes && b.Livro.MarcosTotais == totaisAntes,
				   $"{b.Livro.MarcosLivres}/{b.Livro.MarcosTotais} (era {livresAntes}/{totaisAntes})");
		AfirmarEns("E O MESTRE NAO GANHA NADA -- nem marco, nem total (o `Study` so manda mensagem)",
				   a.Livro.MarcosLivres == doMestreAntes && a.Livro.MarcosTotais == doMestreAntes);

		// (b) COM UM MARCO A MENOS QUE O CUSTO: recusa. O limiar DISPARA.
		Zerar(a, b);
		a.Livro.Dar(alvo.Path);
		if (custo > 0) b.Livro.Conceder(custo - 1);
		Encostar(a, b, tiles: 1);
		AfirmarEns($"com {b.Livro.MarcosLivres} marcos (um a menos que {custo}), a licao e RECUSADA "
				 + "-- o limiar dispara mesmo",
				   !Licao(a, b, alvo) && !b.Livro.Sabe(alvo.Path));
		AfirmarEns("   e a recusa e a de marcos, e nao outra qualquer",
				   EnsinoDeSkill.Avaliar(a.Livro, b.Livro, alvo, true, true, false, 1, false)
				   == RecusaDeLicao.SemMarcos);

		// (c) NA BORDA EXATA: passa. Sem esta linha o `<` e o `<=` seriam indistinguiveis.
		b.Livro.Conceder(1);
		AfirmarEns($"na borda exata ({custo} marcos) a licao acontece",
				   b.Livro.MarcosLivres == custo && Licao(a, b, alvo));
	}

	// =====================================================================
	// 5) A RECARGA DE 6 SEGUNDOS
	// =====================================================================
	private void ARecargaDeSeisSegundos(ServerPlayer a, ServerPlayer b)
	{
		GD.Print("[ensino] -- 5) A RECARGA DE 6 s (`sleep(60)`, e tique do BYOND e 1/10 s)");

		Skill? um = AlvoDesligado();
		Skill? dois = OutroAlvo(um);
		if (um == null || dois == null) { AfirmarEns("ha duas skills ensinaveis pra medir a recarga", false); return; }

		AfirmarEns("a recarga e de 6 s (3000 ticks seriam 5 min -- e isso e o DISCIPULADO, outro sistema)",
				   Math.Abs(EnsinoDeSkill.RecargaSegundos - 6) < 1e-9
				   && Math.Abs(Discipulado.RecargaDeEnsinoSegundos - 300) < 1e-9);

		Zerar(a, b);
		a.Livro.Dar(um.Path);
		a.Livro.Dar(dois.Path);
		b.Livro.Conceder(50);
		Encostar(a, b, tiles: 1);

		AfirmarEns("a primeira licao passa", Licao(a, b, um));
		AfirmarEns("...e a SEGUNDA, no mesmo instante, e recusada pela recarga",
				   !Licao(a, b, dois) && !b.Livro.Sabe(dois.Path));

		// O RELOGIO ANDA (sem dormir 6 s dentro de uma bancada).
		a.RecargaDaLicao = NowMs() - 1;
		AfirmarEns("passada a recarga, a segunda licao acontece", Licao(a, b, dois));

		// E ELA E COBRADA NO GESTO, e nao no "sim": o mestre que oferece e recusado ja pagou.
		Zerar(a, b);
		a.Livro.Dar(um.Path);
		b.Livro.Conceder(50);
		Encostar(a, b, tiles: 1);
		UsarVerboDeEnsino(a, $"ens_ensinar:{um.Path}");
		UsarVerboDeEnsino(b, "ens_licao_nao");
		AfirmarEns("o 'nao' do aluno NAO devolve a recarga do mestre (ela corre desde o gesto)",
				   a.RecargaDaLicao > NowMs() && !b.Livro.Sabe(um.Path));
	}

	// =====================================================================
	// 6) GEOMETRIA, DUPLICATA E PRAZO
	// =====================================================================
	private void GeometriaDuplicataEPrazo(ServerPlayer a, ServerPlayer b)
	{
		GD.Print("[ensino] -- 6) GEOMETRIA, DUPLICATA E PRAZO");

		Skill? alvo = AlvoDesligado();
		if (alvo == null) return;

		// LONGE: o `view(1)` do DM. E aqui a bancada mede a BORDA, senao "1 tile" e so um numero.
		Zerar(a, b);
		a.Livro.Dar(alvo.Path);
		b.Livro.Conceder(50);
		AfirmarEns($"a {EnsinoDeSkill.TilesDaLicao} tile a licao acontece (a mao no ombro)",
				   EnsinoDeSkill.Avaliar(a.Livro, b.Livro, alvo, true, true, false,
										 EnsinoDeSkill.TilesDaLicao, false) == RecusaDeLicao.Pode);
		AfirmarEns("um passo alem, nao",
				   EnsinoDeSkill.Avaliar(a.Livro, b.Livro, alvo, true, true, false,
										 EnsinoDeSkill.TilesDaLicao + 1, false) == RecusaDeLicao.Longe);

		// DE VERDADE, PELO VERB: com o alvo LONGE nao ha nem alvo na frente.
		Estacionar(b, 300);
		AfirmarEns("pelo verb, com o aluno a 300 tiles, nada acontece",
				   !Licao(a, b, alvo) && !b.Livro.Sabe(alvo.Path));
		Encostar(a, b, tiles: 1);

		// NINGUEM ENSINA A SI MESMO, e ninguem reaprende o que ja sabe.
		AfirmarEns("ninguem ensina a si mesmo",
				   EnsinoDeSkill.Avaliar(a.Livro, a.Livro, alvo, true, true, true, 1, false)
				   == RecusaDeLicao.EleMesmo);
		AfirmarEns("a licao acontece uma vez", Licao(a, b, alvo));
		a.RecargaDaLicao = 0;
		AfirmarEns("...e a segunda vez e recusada: o aluno JA SABE (o filtro de alvos do DM)",
				   !Licao(a, b, alvo)
				   && EnsinoDeSkill.Avaliar(a.Livro, b.Livro, alvo, true, true, false, 1, false)
					  == RecusaDeLicao.AlunoJaSabe);

		// O PRAZO -- que nao existe no DM porque la o `input()` congela quem responde.
		Skill? dois = OutroAlvo(alvo);
		if (dois == null) return;
		Zerar(a, b);
		a.Livro.Dar(dois.Path);
		b.Livro.Conceder(50);
		Encostar(a, b, tiles: 1);
		UsarVerboDeEnsino(a, $"ens_ensinar:{dois.Path}");
		if (b.PedidoDeLicao is { } p) b.PedidoDeLicao = p with { Ate = NowMs() - 1 };
		UsarVerboDeEnsino(b, "ens_licao_sim");
		AfirmarEns("uma oferta VENCIDA nao ensina nada (o prazo que substitui o `input()` do BYOND)",
				   !b.Livro.Sabe(dois.Path) && b.PedidoDeLicao == null);

		// E UM 'SIM' SEM OFERTA NENHUMA NAO EXPLODE nem concede nada.
		UsarVerboDeEnsino(b, "ens_licao_sim");
		AfirmarEns("um 'aceitar' sem oferta nenhuma nao concede nada", b.Livro.Aprendidas.Count == 0);
	}

	// =====================================================================
	// 7) O `wastaught` ATRAVESSA O SAVE
	// =====================================================================
	/// <summary>
	/// A regra central do sistema mora numa marca por skill. Se ela nao for pro disco, o jeito de
	/// repassar uma skill rara e **deslogar** -- e a skill continua no livro, entao nada acusa.
	///
	/// A ida e a volta passam pelo caminho de producao (`AccountStore.DeJogador` -> `PrepararSkills`).
	/// </summary>
	private void OWastaughtAtravessaOSave(ServerPlayer a, ServerPlayer b)
	{
		GD.Print("[ensino] -- 7) PERSISTENCIA (o `wastaught` atravessa o logout)");

		Skill? ensinada = AlvoDesligado();
		Skill? comprada = OutroAlvo(ensinada);
		if (ensinada == null || comprada == null) { AfirmarEns("ha duas skills pra separar no save", false); return; }

		Zerar(a, b);
		a.Livro.Dar(ensinada.Path);
		b.Livro.Conceder(50);
		// A SEGUNDA ELE TEM SEM TER SIDO ENSINADO -- e ela e o controle: sem ela, um save que
		// marcasse TUDO como ensinado passaria no teste.
		b.Livro.Dar(comprada.Path);
		Encostar(a, b, tiles: 1);
		AfirmarEns("preparo: A ensina B", Licao(a, b, ensinada));

		CharacterSave save = AccountStore.DeJogador(b, NowMs());
		AfirmarEns("o save leva a lista de ensinadas",
				   save.SkillsEnsinadas.Contains(ensinada.Path)
				   && !save.SkillsEnsinadas.Contains(comprada.Path),
				   string.Join(",", save.SkillsEnsinadas));

		// A VOLTA, pelo caminho de producao.
		var voltou = new ServerPlayer
		{
			Id = IdBaseDoEnsinoDeTeste + 9, Peer = null, Name = "bancada: depois do relog",
			Race = "Human", Genero = "Male", Idade = 25, Zone = b.Zone, Pos = b.Pos,
			Conta = b.Conta, Slot = b.Slot,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 10_000 },
		};
		PrepararSkills(voltou, save);

		AfirmarEns("depois do relog ele ainda SABE as duas",
				   voltou.Livro.Sabe(ensinada.Path) && voltou.Livro.Sabe(comprada.Path));
		AfirmarEns("...e ainda NAO PODE repassar a que lhe ensinaram",
				   !EnsinoDeSkill.PodeRepassar(voltou.Livro, ensinada));
		AfirmarEns("...mas continua podendo repassar a que e dele",
				   EnsinoDeSkill.PodeRepassar(voltou.Livro, comprada));

		// MARCA ORFA E DESCARTADA: save que perdeu a skill e ficou com a marca.
		var manco = new SkillBook();
		manco.Carregar([comprada.Path]);
		manco.CarregarEnsinadas([ensinada.Path, comprada.Path]);
		AfirmarEns("marca de uma skill que o livro nao tem e descartada na leitura",
				   !manco.FoiEnsinada(ensinada.Path) && manco.FoiEnsinada(comprada.Path));
	}

	// =====================================================================
	// 8) ESQUECER A LICAO
	// =====================================================================
	/// <summary>
	/// `Forget_Studied_Skill` (`teachable.dm:25`): so o `wastaught` se esquece.
	///
	/// E SEM DEVOLVER MARCO -- ver `EnsinoDeSkill.Esquecer` pro porque, e para a nota de que isso e
	/// uma divergencia consciente do `forget()` do DM.
	/// </summary>
	private void EsquecerSoOQueFoiEnsinado(ServerPlayer a, ServerPlayer b)
	{
		GD.Print("[ensino] -- 8) ESQUECER A LICAO (so o que foi ensinado, e sem reembolso)");

		Skill? ensinada = AlvoDesligado();
		Skill? propria = OutroAlvo(ensinada);
		if (ensinada == null || propria == null) return;

		Zerar(a, b);
		a.Livro.Dar(ensinada.Path);
		b.Livro.Conceder(50);
		b.Livro.Dar(propria.Path);
		Encostar(a, b, tiles: 1);
		AfirmarEns("preparo: A ensina B", Licao(a, b, ensinada));

		int livres = b.Livro.MarcosLivres, totais = b.Livro.MarcosTotais;

		UsarVerboDeEnsino(b, $"ens_esquecer:{propria.Path}");
		AfirmarEns("o que e DELE nao se esquece por aqui (o `wastaught` e o filtro do DM)",
				   b.Livro.Sabe(propria.Path));

		UsarVerboDeEnsino(b, $"ens_esquecer:{ensinada.Path}");
		AfirmarEns("o que lhe ENSINARAM, sim", !b.Livro.Sabe(ensinada.Path));
		AfirmarEns("NENHUM MARCO VOLTA -- nenhum tinha sido gasto (o `forget()` do DM devolveria, e "
				 + "isso seria uma impressora de marcos)",
				   b.Livro.MarcosLivres == livres && b.Livro.MarcosTotais == totais,
				   $"{b.Livro.MarcosLivres}/{b.Livro.MarcosTotais} (era {livres}/{totais})");
		AfirmarEns("...e a marca vai junto: se ele conseguir a skill por conta propria depois, "
				 + "volta a poder repassa-la",
				   !b.Livro.FoiEnsinada(ensinada.Path));
		b.Livro.Dar(ensinada.Path);
		AfirmarEns("   (provado: com a skill de volta e sem a marca, ele repassa)",
				   EnsinoDeSkill.PodeRepassar(b.Livro, ensinada));
	}

	// =====================================================================
	// 9) A VARREDURA: NINGUEM MEXE EM MARCO
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ESTA CHECAGEM LE O FONTE ============================
	/// As secoes 3 e 4 provam o comportamento de HOJE: o aluno saiu com os mesmos marcos e o mestre
	/// nao ganhou nada. Mas as duas regras sao sobre uma AUSENCIA, e ausencia se perde sem que nada
	/// quebre -- basta a proxima pessoa achar que "faltava" recompensar o mestre e acrescentar uma
	/// linha. O teste de comportamento continuaria verde em tudo, menos naquele numero.
	///
	/// Entao esta varredura afirma a coisa estrutural: **nenhuma linha de producao do ensino toca
	/// em marco de ninguem**. Ela reprova no dia em que alguem escrever `Conceder(` ou
	/// `MarcosLivres -=` nestes dois arquivos -- que e exatamente o dia em que o dono precisa ser
	/// perguntado (ver o cabecalho do `EnsinoDeSkill`).
	///
	/// (2026-10-13) ESSE DIA CHEGOU UMA VEZ, e a varredura a conhece pelo nome: a LICAO DE MESTRE (dono: *"dar pra
	/// ensinar mais skills pros alunos"*) cobra do aluno os marcos da skill que nao se ensina a estranho. E UMA linha,
	/// no `EnsinoDeSkill.Ensinar`, atras do `if (cobrar)`. Uma segunda cobranca -- ou aquela fora do `if` -- continua
	/// reprovando, e o que o comportamento prova (a licao de sempre nao gasta nada) esta nas secoes 3 e 4.
	/// ======================================================================================
	/// </summary>
	private void NinguemMexeEmMarco()
	{
		GD.Print("[ensino] -- 9) A VARREDURA (o ensino nao mexe em marco de ninguem)");

		string[] fontes = ["Core/Skills/EnsinoDeSkill.cs", "Server/GameServer.Ensino.cs"];
		string[] proibidos = ["Conceder(", "MarcosLivres -", "MarcosLivres +", "MarcosTotais -",
							  "MarcosTotais +", "MarcosLivres =", "MarcosTotais ="];

		// A UNICA COBRANCA QUE EXISTE (a licao de mestre), reconhecida pelo texto E pelo `if (cobrar)` logo acima.
		const string aCobrancaDoMestre = "doAluno.MarcosLivres -= Custo(s);";
		static bool AtrasDoCobrar(string[] linhas, int i)
		{
			for (int j = i - 1; j >= Math.Max(0, i - 4); j--)
				if (SemTextoNemComentario(linhas[j]).Contains("if (cobrar)", StringComparison.Ordinal)) return true;
			return false;
		}

		var achados = new List<string>();
		int linhasLidas = 0, cobrancasDeMestre = 0;
		foreach (string f in fontes)
		{
			string[] linhas = Fonte(f);
			// FONTE QUE SUMIU REPROVA. Um caminho errado aqui daria "zero achados" e viraria uma
			// checagem verde que nao le nada -- o pior tipo de bancada.
			if (linhas.Length == 0) { achados.Add($"FONTE NAO ENCONTRADO: {f}"); continue; }
			linhasLidas += linhas.Length;
			for (int i = 0; i < linhas.Length; i++)
			{
				string l = SemTextoNemComentario(linhas[i]);
				if (l.Trim() == aCobrancaDoMestre && f.EndsWith("EnsinoDeSkill.cs", StringComparison.Ordinal) && AtrasDoCobrar(linhas, i))
				{
					cobrancasDeMestre++;
					continue;
				}
				foreach (string p in proibidos)
					if (l.Contains(p, StringComparison.Ordinal)) achados.Add($"{f}:{i + 1}  {l.Trim()}");
			}
		}

		AfirmarEns($"nenhuma linha de producao do ensino credita ou debita marco -- fora a UNICA cobranca da licao de mestre, atras do `if (cobrar)` ({linhasLidas} linhas lidas)",
				   achados.Count == 0 && cobrancasDeMestre == 1, $"{cobrancasDeMestre} cobranca(s) de mestre; {string.Join(" | ", achados.Take(4))}");
		AfirmarEns("...e a mesma linha FORA do `if (cobrar)` nao passaria por ela (a excecao e a linha no lugar, e nao o texto)",
				   !AtrasDoCobrar(["\t\tdoAluno.DarComoEnsinada(s.Path);", "\t\t" + aCobrancaDoMestre], 1));

		// E A VARREDURA TEM QUE SABER REPROVAR -- senao ela e um comentario bonito. A prova roda o
		// mesmo criterio sobre uma linha adulterada.
		const string adulterada = "\t\tdoAluno.Conceder(Custo(s));";
		AfirmarEns("...e a varredura reprovaria se alguem acrescentasse a linha (ela sabe dizer nao)",
				   proibidos.Any(p => SemTextoNemComentario(adulterada).Contains(p, StringComparison.Ordinal)));
	}

	// =====================================================================
	// 10) A LICAO ALCANCA QUEM ESTA EM CENA
	// =====================================================================
	/// <summary>
	/// ============================ O ESCUDO DA CENA E CONTRA GOLPE, E NAO CONTRA A LICAO ============================
	/// O `Teach_Skill` varre `view(1)` (`teachable.dm:12-13`) sem perguntar `attackable`, e o `Study`
	/// (`:38-57`) nao pergunta `transing`: no DM o aluno no meio da estreia do SSJ recebe a oferta e aprende.
	/// Ate 2026-09-24 o port escolhia o aluno pelo `AlvoNaFrente` do SOCO, que pula `Combate.Intocavel` -- e
	/// o mestre ouvia "nao ha ninguem na sua frente" com o corpo parado ali.
	///
	/// AS DUAS METADES DO `Intocavel`, cada uma pela porta de producao dela: a CENA pelo funil do
	/// `admin_forma` (a estreia do SSJ1 marca a cena cheia) e a CARENCIA pelo `Reviver` que o renascimento
	/// chama. A cena escorre pelo relogio de producao (`TickDaForma`) antes da carencia, pra a segunda linha
	/// medir so a carencia.
	///
	/// COMO ELA REPROVA: volte o `AlvoNaFrente(mestre)` no `OferecerLicao` (`GameServer.Ensino.cs`) e a
	/// oferta em cena, o "sim" em cena e a licao na carencia ficam vermelhos.
	/// ==============================================================================================================
	/// </summary>
	private void ALicaoAlcancaQuemEstaEmCena(ServerPlayer a, ServerPlayer aluno)
	{
		GD.Print("[ensino] -- 10) A LICAO ALCANCA QUEM ESTA EM CENA (o escudo e contra golpe)");

		Skill? um = AlvoDesligado();
		Skill? dois = OutroAlvo(um);
		if (um == null || dois == null) { AfirmarEns("ha duas skills ensinaveis pra medir a cena", false); return; }

		// UM ENDERECO SO DELES (a licao do `Estacionar`): com B ou C colados em A, o mais perto seria outro.
		Zerar(a, aluno);
		a.Livro.Dar(um.Path);
		a.Livro.Dar(dois.Path);
		aluno.Livro.Conceder(50);
		Estacionar(a, 700);
		Encostar(a, aluno, tiles: 1);

		// --- a) A CENA DA ESTREIA DO SSJ1 ---
		AdminForcarForma(aluno, "ssj1");
		AfirmarEns("(montagem) o aluno esta na CENA da estreia do SSJ1, intocavel pro SOCO (o `AlvoNaFrente` nao o acha)",
				   aluno.Forma.Atual == "ssj1" && EmCena(aluno) && aluno.Combate.Intocavel && AlvoNaFrente(a) == null,
				   $"forma {aluno.Forma.Atual}, cena {aluno.CenaSegundos:0.#} s, alvo do soco {AlvoNaFrente(a)?.Name ?? "ninguem"}");

		UsarVerboDeEnsino(a, $"ens_ensinar:{um.Path}");
		AfirmarEns("o mestre ACHA o aluno em cena e a oferta chega (o `view(1)` do `Teach_Skill` nao pergunta `attackable`)",
				   aluno.PedidoDeLicao?.Path == um.Path, aluno.PedidoDeLicao?.ToString() ?? "nenhuma oferta");
		UsarVerboDeEnsino(aluno, "ens_licao_sim");
		AfirmarEns("...e o 'sim' EM CENA ensina (o `Study`, `:38-57`, nao pergunta `transing`)",
				   aluno.Livro.Sabe(um.Path) && aluno.Livro.FoiEnsinada(um.Path) && EmCena(aluno),
				   $"sabe={aluno.Livro.Sabe(um.Path)}, cena={EmCena(aluno)}");

		// --- b) A CARENCIA DE RENASCIMENTO ---
		for (int i = 0; i < (int)(60 / Jandirus.Net.Protocol.TickSeconds) && EmCena(aluno); i++)
			TickDaForma(aluno, Jandirus.Net.Protocol.TickSeconds);
		a.RecargaDaLicao = 0;
		aluno.Combate.Reviver(1, SegundosDeCarencia);
		AfirmarEns("(montagem) com a cena acabada, o aluno esta na CARENCIA de renascimento, intocavel pro soco",
				   !EmCena(aluno) && aluno.Combate.Carencia > 0 && aluno.Combate.Intocavel && AlvoNaFrente(a) == null,
				   $"cena {aluno.CenaSegundos:0.#} s, carencia {aluno.Combate.Carencia:0.#} s");
		AfirmarEns("a licao tambem alcanca quem acabou de renascer (a carencia e escudo contra golpe, nao contra gesto)",
				   Licao(a, aluno, dois));
	}

	// =====================================================================
	// 11) DO CARGO, SO OS ATAQUES DE KI SE ENSINAM (dono, 2026-10-23)
	// =====================================================================
	/// <summary>
	/// *"ta dando pra ensinar skills de entrar na sala do tempo pros outros, unica skill de cargo q pode ensinar é a skill de
	/// ataques de ki, fora isso n poderia ensinar mais nada"* -- e o dono escolheu: so o espaco `rank/`; a Fusion Dance, o Mafuba e
	/// a Paralysis continuam ensinaveis; quem ja tinha aprendido perde; o bio nao herda.
	///
	/// AS SETE PERGUNTAS, cada uma pela porta de producao dela (o codigo na frente de cada rotulo e o que o ciclo procura):
	///   11a  a chave da Sala do Tempo pelo VERBO `ens_ensinar`: nem a oferta chega, e a recusa diz a regra inteira;
	///   11b  o controle: o Kamehameha (ataque de ki do cargo) passa pelo MESMO verbo;
	///   11c  a TABELA: toda skill `rank/` que o DM deixava ensinar -- e as que fecham sao exatamente as sete do enunciado;
	///   11d  as LISTAS: `PodeRepassar`, `Repassaveis` e `OQueOMestreEnsina` (esta e a do cartao do aluno);
	///   11e  a LICAO DE MESTRE: o mestre nao abre o `Revive` ao proprio aluno, nem com a skill no corpo dele;
	///   11f  a copia ENSINADA de antes da regra sai na reconciliacao do cargo, com aviso -- e a do Kamehameha fica;
	///   11g  o BIO nao herda a chave do doador, e herda o Kamehameha.
	/// E os dois defeitos injetados, cada um com as reguas dele: `EnsinoDeSkill.CargoSeEnsinaComoAntesDeTeste` (11a-11e) e
	/// `DadivaDeCargo.EnsinadaDoCargoFicaDeTeste` (11f, 11g).
	///
	/// A TABELA NAO PERGUNTA A REGRA QUE MEDE: "ataque de ki" e conferido aqui pela tabela de tiros (`TecnicasDeLonge`), a mao, e
	/// as duas da escolha do dono pelo nome -- se a bancada perguntasse ao `FicaNoCargo`, um defeito nele passaria pelos dois lados.
	/// </summary>
	private void DoCargoSoOAtaqueDeKi(ServerPlayer a, ServerPlayer b)
	{
		GD.Print("[ensino] -- 11) DO CARGO, SO OS ATAQUES DE KI SE ENSINAM (dono, 2026-10-23)");

		const string Cargo = "/datum/skill/rank/";
		Skill? chave = _skills!.Get(SkillDaChaveDaSala);
		Skill? kame = _skills.Get(Cargo + "Kamehameha");
		Skill? revive = _skills.Get(Cargo + "Revive");
		static bool TiroDeKi(Skill s) => s.Verbos.Length > 0 && s.Verbos.All(v => TecnicasDeLonge.Get(v) != null);
		if (chave == null || kame == null || revive == null || !chave.Ensinavel || TiroDeKi(chave) || !kame.Ensinavel || !TiroDeKi(kame) || revive.Ensinavel)
		{
			AfirmarEns("11. PRECONDICAO: a chave da Sala (ensinavel no DM, nao atira), o Kamehameha (ensinavel, tiro de ki) e o Revive (nunca se ensinou)",
					   false, $"{chave?.Nome} / {kame?.Nome} / {revive?.Nome}");
			return;
		}

		// O QUE O ENSINO DIRIA DE CADA SKILL `rank/` QUE O DM DEIXAVA ENSINAR, pela avaliacao de producao: (as que nao saem como a
		// tabela manda, as que fecham). Um mestre que a sabe, um aluno com marcos de sobra, colados, fora da recarga.
		(List<string> Erradas, List<string> Fecham) TabelaDoCargo()
		{
			List<string> errou = [], fechou = [];
			foreach (Skill s in _skills.Todas.Where(x => x.Ensinavel && !x.Arvore && x.Path.StartsWith(Cargo, StringComparison.Ordinal))
						 .OrderBy(x => x.Path, StringComparer.Ordinal))
			{
				bool passa = TiroDeKi(s) || s.Path is Cargo + "Fusion_Dance" or Cargo + "Mafuba";
				var doMestre = new SkillBook();
				doMestre.Dar(s.Path);
				var doAluno = new SkillBook();
				doAluno.Conceder(50);
				RecusaDeLicao r = EnsinoDeSkill.Avaliar(doMestre, doAluno, s, true, true, false, 1, false);
				string nome = s.Path[Cargo.Length..];
				if (passa ? r != RecusaDeLicao.Pode : r != RecusaDeLicao.DoCargo) errou.Add($"{nome}={r}");
				if (r == RecusaDeLicao.DoCargo) fechou.Add(nome);
			}
			return (errou, fechou);
		}

		// A LICAO DE MESTRE, pura: o vinculo de discipulado nao entra -- o que se mede e se a porta do mestre abre o cargo. O
		// `Recusa.Pode` diz que a skill SERIA do corpo do aluno, o caso mais aberto que ha.
		static RecusaDeLicao DeMestre(Skill s)
		{
			var doMestre = new SkillBook();
			doMestre.Dar(s.Path);
			var doAluno = new SkillBook();
			doAluno.Conceder(50);
			return EnsinoDeSkill.Avaliar(doMestre, doAluno, s, true, true, false, 1, false, eMeuAluno: true, doAlunoSozinho: Recusa.Pode);
		}

		string[] doEnunciado = ["Appoint_Elder", "DeadZone", "Grow_Senzu", "KaiPermission", "Permission", "SuperiorSeal", "Unlock_Potential"];

		// ---- 11a) A CHAVE DA SALA PELO VERBO ----
		Zerar(a, b);
		a.Livro.Dar(chave.Path);
		a.Livro.Dar(kame.Path);
		a.Livro.Dar(revive.Path);
		b.Livro.Conceder(50);
		Estacionar(a, 0);
		Encostar(a, b, tiles: 1);

		List<string> falas = Ouvir(() => UsarVerboDeEnsino(a, $"ens_ensinar:{chave.Path}"));
		AfirmarEns($"11a1. a CHAVE DA SALA DO TEMPO ('{chave.Nome}') nao se ensina: o Guardiao aponta pra quem esta colado e a oferta nem chega",
				   b.PedidoDeLicao == null && !b.Livro.Sabe(chave.Path), b.PedidoDeLicao?.ToString() ?? "");
		AfirmarEns("11a2. ...e a recusa diz a regra INTEIRA (do kit de um cargo so passam adiante os ataques de ki, a Fusion Dance e o Mafuba)",
				   Disse(falas, "e do cargo e nao se ensina") && Disse(falas, "os ataques de ki, a Fusion Dance e o Mafuba"), Ultimos(falas));
		AfirmarEns("11a3. ...e ela vem antes do gesto: a recarga de 6 s do Guardiao nao foi gasta", a.RecargaDaLicao == 0, $"{a.RecargaDaLicao}");

		// ---- 11b) O CONTROLE, PELO MESMO VERBO ----
		// (cada medida parte do zero: sem a regra, a 11a teria deixado a oferta pendurada e a recarga correndo)
		a.RecargaDaLicao = 0;
		b.PedidoDeLicao = null;
		AfirmarEns($"11b. CONTROLE: o {kame.Nome} (ataque de ki do cargo do Eremita) passa pelo MESMO verbo e entra no livro do aluno",
				   Licao(a, b, kame) && b.Livro.FoiEnsinada(kame.Path));

		// ---- 11c) A TABELA ----
		(List<string> erradas, List<string> fecham) = TabelaDoCargo();
		AfirmarEns("11c1. A TABELA: toda skill `rank/` que o DM deixava ensinar passa se e so se e ataque de ki -- ou a Fusion Dance e o Mafuba (escolha do dono)",
				   erradas.Count == 0, string.Join(", ", erradas));
		AfirmarEns("11c2. ...e as que fecham sao EXATAMENTE as sete do enunciado (a chave da Sala, a do Kaioh, o Anciao, a Dead Zone, a Agua Sagrada, o Selo Superior e o Liberar Potencial)",
				   fecham.SequenceEqual(doEnunciado), string.Join(",", fecham));

		// ---- 11d) AS LISTAS ----
		AfirmarEns("11d1. AS LISTAS: o Guardiao nao REPASSA a chave (`PodeRepassar`) e repassa o Kamehameha",
				   !EnsinoDeSkill.PodeRepassar(a.Livro, chave) && EnsinoDeSkill.PodeRepassar(a.Livro, kame));
		List<string> repassaveis = [.. EnsinoDeSkill.Repassaveis(_skills, a.Livro).Select(s => s.Path)];
		AfirmarEns("11d2. ...a lista do `Teach_Skill` (`Repassaveis`) tem o Kamehameha e nao tem a chave",
				   repassaveis.Contains(kame.Path) && !repassaveis.Contains(chave.Path), string.Join(",", repassaveis));
		List<string> doCartao = [.. EnsinoDeSkill.OQueOMestreEnsina(_skills, a.Livro.Aprendidas).Select(s => s.Path)];
		AfirmarEns("11d3. ...e a do cartao do aluno (`OQueOMestreEnsina`) tem o Kamehameha e nao tem nem a chave nem o Revive",
				   doCartao.Contains(kame.Path) && !doCartao.Contains(chave.Path) && !doCartao.Contains(revive.Path), string.Join(",", doCartao));

		// ---- 11e) A LICAO DE MESTRE ----
		RecusaDeLicao deMestre = DeMestre(revive);
		AfirmarEns("11e. A LICAO DE MESTRE nao abre o que o cargo guarda: nem o Revive (que nunca se ensinou) pro proprio aluno, com a skill no corpo dele",
				   deMestre == RecusaDeLicao.DoCargo, $"{deMestre}");

		// ---- 11f) A COPIA ENSINADA DE ANTES DA REGRA ----
		Zerar(b);
		b.Livro.DarComoEnsinada(chave.Path);   // a licao que o Guardiao dava antes de 2026-10-23
		b.Livro.DarComoEnsinada(kame.Path);
		List<string> avisos = Ouvir(() => ReconciliarDadiva(b));
		AfirmarEns("11f1. QUEM JA TINHA APRENDIDO a chave da Sala (a licao de antes da regra) a perde na reconciliacao do cargo -- ele nao tem cargo nenhum",
				   !b.Livro.Sabe(chave.Path), $"sabe={b.Livro.Sabe(chave.Path)}");
		AfirmarEns("11f2. ...e o jogo diz por que ela saiu", Disse(avisos, "skill de cargo e saiu do seu livro"), Ultimos(avisos));
		AfirmarEns("11f3. CONTROLE: o Kamehameha ensinado fica (ataque de ki do cargo se ensina, e a licao segura a skill)",
				   b.Livro.Sabe(kame.Path) && b.Livro.FoiEnsinada(kame.Path));

		// ---- 11g) O BIO ----
		Zerar(b);
		int herdadas = HerdarOLivroDosDoadores(b, new Gestacao
		{
			Amostras = { new Amostra { Doador = "bancada: o Guardiao doador" } },
			Tecnicas = { chave.Path, kame.Path },
		});
		AfirmarEns("11g. O BIO nao herda a chave da Sala do doador Guardiao -- e herda o Kamehameha dele (o parto conta UMA skill)",
				   !b.Livro.Sabe(chave.Path) && b.Livro.Sabe(kame.Path) && herdadas == 1,
				   $"herdadas {herdadas}, chave {b.Livro.Sabe(chave.Path)}, Kamehameha {b.Livro.Sabe(kame.Path)}");

		// ---- OS DOIS DEFEITOS INJETADOS ----
		try
		{
			EnsinoDeSkill.CargoSeEnsinaComoAntesDeTeste = true;
			Zerar(a, b);
			a.Livro.Dar(chave.Path);
			a.Livro.Dar(revive.Path);
			b.Livro.Conceder(50);
			Encostar(a, b, tiles: 1);
			UsarVerboDeEnsino(a, $"ens_ensinar:{chave.Path}");
			AfirmarEns("11x1. (defeito injetado: o kit de cargo ensinavel como antes) a mesma regua REPROVA -- a oferta da chave chega ao aluno",
					   b.PedidoDeLicao?.Path == chave.Path, b.PedidoDeLicao?.ToString() ?? "nenhuma oferta");
			(List<string> erradasAntes, List<string> fechamAntes) = TabelaDoCargo();
			AfirmarEns("11x2. (defeito injetado) ...e a tabela reprova: as sete passam, nenhuma fecha",
					   erradasAntes.Count == doEnunciado.Length && fechamAntes.Count == 0, string.Join(", ", erradasAntes));
			AfirmarEns("11x3. (defeito injetado) ...e as listas oferecem a chave e o Revive",
					   EnsinoDeSkill.PodeRepassar(a.Livro, chave)
					   && EnsinoDeSkill.OQueOMestreEnsina(_skills, a.Livro.Aprendidas).Any(s => s.Path == revive.Path));
			RecusaDeLicao deMestreAntes = DeMestre(revive);
			AfirmarEns("11x4. (defeito injetado) ...e a licao de mestre abre o Revive", deMestreAntes == RecusaDeLicao.Pode, $"{deMestreAntes}");
		}
		finally
		{
			EnsinoDeSkill.CargoSeEnsinaComoAntesDeTeste = false;
			a.RecargaDaLicao = 0;
			b.PedidoDeLicao = null;
		}

		try
		{
			Jandirus.Core.Ranks.DadivaDeCargo.EnsinadaDoCargoFicaDeTeste = true;
			Zerar(b);
			b.Livro.DarComoEnsinada(chave.Path);
			ReconciliarDadiva(b);
			AfirmarEns("11y1. (defeito injetado: a copia ensinada de cargo fica) a mesma regua REPROVA -- a chave aprendida antes da regra continua no livro",
					   b.Livro.Sabe(chave.Path));
			Zerar(b);
			int herdadasAntes = HerdarOLivroDosDoadores(b, new Gestacao { Amostras = { new Amostra() }, Tecnicas = { chave.Path } });
			AfirmarEns("11y2. (defeito injetado) ...e o bio herda a chave do doador", b.Livro.Sabe(chave.Path) && herdadasAntes == 1, $"herdadas {herdadasAntes}");
		}
		finally
		{
			Jandirus.Core.Ranks.DadivaDeCargo.EnsinadaDoCargoFicaDeTeste = false;
			Zerar(b);
		}
	}

	/// <summary>
	/// 11m. O POTENTIAL UNLEASHED NAO SE ENSINA (dono, 2026-10-23) -- *"essa forma nao pode ser ensinada diretamente, precisando passar
	/// do ritual"*. A skill que era a forma (`kai/Mystic`, `teacher = TRUE` no DM) fecha por PORTA NENHUMA, cada uma pela porta de
	/// producao dela (o codigo na frente de cada rotulo e o que o ciclo procura):
	///   11m1/11m2  o verbo `ens_ensinar`: nem a oferta chega, e a recusa diz o caminho que sobrou (o ritual);
	///   11m3a      `PodeRepassar` e a lista do `Teach_Skill` (`Repassaveis`) nao a tem;
	///   11m3b      a lista do cartao do aluno (`OQueOMestreEnsina`) nao a tem;
	///   11m4       a licao de mestre nao a abre ao proprio aluno;
	///   11m5       o bio nao a herda do doador.
	/// O defeito injetado e `EnsinoDeSkill.MisticoSeEnsinaComoAntesDeTeste` (11mx: as cinco reguas reprovam com ele). O botao "Ensinar"
	/// do cliente e da F8m da `--diagverbs`; a forma que fica com quem ja a tinha, da `--ritualkaiteste` (C2).
	/// </summary>
	private void OPotentialUnleashedNaoSeEnsina(ServerPlayer a, ServerPlayer b)
	{
		GD.Print("[ensino] -- 11m) O POTENTIAL UNLEASHED NAO SE ENSINA: so o Ritual Mistico do Velho Kaiohshin o da (dono, 2026-10-23)");
		Skill? mistico = _skills!.Get(Jandirus.Core.Ranks.RitualDoKaioshin.SkillDoMistico);
		if (mistico == null || !mistico.Ensinavel)
		{
			AfirmarEns("11m. PRECONDICAO: a skill que era a forma existe no catalogo e o DM a deixava ensinar (`teacher = TRUE`)",
					   false, mistico?.Nome ?? "sem a skill");
			return;
		}

		// A LICAO DE MESTRE, pura (o mesmo molde da 11e): o que se mede e se a porta do mestre abre a skill.
		static RecusaDeLicao DeMestre(Skill s)
		{
			var doMestre = new SkillBook();
			doMestre.Dar(s.Path);
			var doAluno = new SkillBook();
			doAluno.Conceder(50);
			return EnsinoDeSkill.Avaliar(doMestre, doAluno, s, true, true, false, 1, false, eMeuAluno: true, doAlunoSozinho: Recusa.Pode);
		}

		// ---- 11m1/11m2) O VERBO -- com a copia que NINGUEM ensinou, a unica que o DM deixava repassar ----
		Zerar(a, b);
		a.Livro.Dar(mistico.Path);
		b.Livro.Conceder(50);
		Estacionar(a, 0);
		Encostar(a, b, tiles: 1);
		List<string> falas = Ouvir(() => UsarVerboDeEnsino(a, $"ens_ensinar:{mistico.Path}"));
		AfirmarEns($"11m1. O POTENTIAL UNLEASHED ('{mistico.Nome}') NAO SE ENSINA: quem o tem aponta pra quem esta colado e a oferta nem chega",
				   b.PedidoDeLicao == null && !b.Livro.Sabe(mistico.Path), b.PedidoDeLicao?.ToString() ?? "");
		AfirmarEns("11m2. ...e a recusa diz o caminho que sobrou: o Ritual Mistico do Velho Kaiohshin",
				   Disse(falas, "não se ensina") && Disse(falas, "Ritual Místico do Velho Kaiohshin"), Ultimos(falas));

		// ---- 11m3) AS LISTAS ----
		List<string> repassaveis = [.. EnsinoDeSkill.Repassaveis(_skills, a.Livro).Select(s => s.Path)];
		AfirmarEns("11m3a. AS LISTAS: o livro que a tem nao a REPASSA (`PodeRepassar`), e a lista do `Teach_Skill` (`Repassaveis`) nao a oferece",
				   !EnsinoDeSkill.PodeRepassar(a.Livro, mistico) && !repassaveis.Contains(mistico.Path), string.Join(",", repassaveis));
		List<string> doCartao = [.. EnsinoDeSkill.OQueOMestreEnsina(_skills, a.Livro.Aprendidas).Select(s => s.Path)];
		AfirmarEns("11m3b. ...nem a do cartao do aluno (`OQueOMestreEnsina`)", !doCartao.Contains(mistico.Path), string.Join(",", doCartao));

		// ---- 11m4) A LICAO DE MESTRE ----
		RecusaDeLicao deMestre = DeMestre(mistico);
		AfirmarEns("11m4. A LICAO DE MESTRE nao a abre: nem o mestre a mostra ao proprio aluno", deMestre == RecusaDeLicao.SoPeloRitual, $"{deMestre}");

		// ---- 11m5) O BIO ----
		Zerar(b);
		int herdadas = HerdarOLivroDosDoadores(b, new Gestacao
		{
			Amostras = { new Amostra { Doador = "bancada: o doador que a sabia" } },
			Tecnicas = { mistico.Path },
		});
		AfirmarEns("11m5. O BIO nao a herda do doador (a heranca era a ultima porta)", !b.Livro.Sabe(mistico.Path) && herdadas == 0, $"herdadas {herdadas}");

		// ---- O DEFEITO INJETADO ----
		try
		{
			EnsinoDeSkill.MisticoSeEnsinaComoAntesDeTeste = true;
			Zerar(a, b);
			a.Livro.Dar(mistico.Path);
			b.Livro.Conceder(50);
			Encostar(a, b, tiles: 1);
			UsarVerboDeEnsino(a, $"ens_ensinar:{mistico.Path}");
			bool ofertaChegou = b.PedidoDeLicao?.Path == mistico.Path;
			bool listas = EnsinoDeSkill.PodeRepassar(a.Livro, mistico)
						  && EnsinoDeSkill.OQueOMestreEnsina(_skills, a.Livro.Aprendidas).Any(s => s.Path == mistico.Path);
			RecusaDeLicao deMestreAntes = DeMestre(mistico);
			b.PedidoDeLicao = null;
			Zerar(b);
			int herdadasAntes = HerdarOLivroDosDoadores(b, new Gestacao { Amostras = { new Amostra() }, Tecnicas = { mistico.Path } });
			AfirmarEns("11mx. (defeito injetado: o Potential Unleashed se ensina como antes) a mesma regua REPROVA nas portas -- a oferta chega, as "
					   + "listas o oferecem, a licao de mestre nao o recusa e o bio o herda",
					   ofertaChegou && listas && deMestreAntes != RecusaDeLicao.SoPeloRitual && herdadasAntes == 1,
					   $"oferta {ofertaChegou}, listas {listas}, mestre {deMestreAntes}, bio {herdadasAntes}");
		}
		finally
		{
			EnsinoDeSkill.MisticoSeEnsinaComoAntesDeTeste = false;
			a.RecargaDaLicao = 0;
			b.PedidoDeLicao = null;
			Zerar(a, b);
		}
	}

	/// <summary>
	/// 11k. O KAI KAI SO PRA QUEM E KAI OU JA DESPERTOU O KI DIVINO (dono, 2026-10-27) -- *"racas sem serem kai, q nao tem god ki n
	/// poderiam aprender kai kai, kai sem god ki podem tranquilamente"*. O mesmo mestre, o mesmo aluno, pelas portas de producao:
	///   11k1/11k2  o Humano sem ki divino: a oferta nem chega, e a recusa diz a regra inteira;
	///   11k3       o Humano que DESPERTOU o ki divino aprende;
	///   11k4       o Kai sem ki divino aprende (como no DM);
	///   11k5       a licao de mestre tambem recusa o Humano sem ki divino;
	///   11k6       a regra e so do Kai Kai: o mesmo Humano aprende outra skill ensinavel do mesmo mestre;
	///   11k7       o bio sem ki divino nao herda o Kai Kai do doador (a copia dele e ensinada) -- e com o ki divino herda.
	/// </summary>
	private void OKaiKaiPedeOKiDivino(ServerPlayer a, ServerPlayer b)
	{
		GD.Print("[ensino] -- 11k) O KAI KAI SO PRA QUEM E KAI OU JA DESPERTOU O KI DIVINO (dono, 2026-10-27)");
		Skill? kaiKai = _skills!.Get(EnsinoDeSkill.KaiKai);
		Skill? outra = AlvoDesligado();   // a da secao 2, que ensina a este mesmo Humano
		if (kaiKai == null || !kaiKai.Ensinavel || outra == null)
		{
			AfirmarEns("11k. PRECONDICAO: o Kai Kai existe no catalogo e se ensina (`teacher = TRUE`), e ha outra ensinavel",
					   false, kaiKai?.Nome ?? "sem a skill");
			return;
		}

		string racaDoB = b.Race;
		try
		{
			// ---- 11k1/11k2) O HUMANO SEM KI DIVINO ----
			Zerar(a, b);
			b.Ficha.godki = null;
			a.Livro.Dar(kaiKai.Path);
			b.Livro.Conceder(50);
			Estacionar(a, 0);
			Encostar(a, b, tiles: 1);
			List<string> falas = Ouvir(() => UsarVerboDeEnsino(a, $"ens_ensinar:{kaiKai.Path}"));
			AfirmarEns($"11k1. O HUMANO SEM KI DIVINO nao aprende o Kai Kai ('{kaiKai.Nome}'): o mestre aponta pra quem esta colado e a oferta nem chega",
					   b.PedidoDeLicao == null && !b.Livro.Sabe(kaiKai.Path), b.PedidoDeLicao?.ToString() ?? "");
			AfirmarEns("11k2. ...e a recusa diz a regra inteira: so quem e Kai ou ja despertou o ki divino",
					   Disse(falas, "só se ensina a quem é Kai") && Disse(falas, "God Ki"), Ultimos(falas));

			// ---- 11k3) O HUMANO QUE DESPERTOU ----
			Zerar(a, b);
			a.Livro.Dar(kaiKai.Path);
			b.Livro.Conceder(50);
			Jandirus.Core.Forms.DespertarDivino.Despertar(b.Ficha);
			Encostar(a, b, tiles: 1);
			AfirmarEns("11k3. O HUMANO QUE DESPERTOU O KI DIVINO aprende o Kai Kai pela licao de sempre",
					   Licao(a, b, kaiKai), $"ki divino {b.Ficha.godki?.awakened}");

			// ---- 11k4) O KAI SEM KI DIVINO ----
			Zerar(a, b);
			b.Ficha.godki = null;
			b.Race = b.Ficha.Race = EnsinoDeSkill.RacaDoKaiKai;
			a.Livro.Dar(kaiKai.Path);
			b.Livro.Conceder(50);
			Encostar(a, b, tiles: 1);
			AfirmarEns("11k4. O KAI SEM KI DIVINO aprende o Kai Kai, como no DM", Licao(a, b, kaiKai), $"raca {b.Race}");
			b.Race = b.Ficha.Race = racaDoB;

			// ---- 11k5) A LICAO DE MESTRE (pura, o molde da 11m4) ----
			var doMestre = new SkillBook();
			doMestre.Dar(kaiKai.Path);
			var doAluno = new SkillBook();
			doAluno.Conceder(50);
			RecusaDeLicao deMestre = EnsinoDeSkill.Avaliar(doMestre, doAluno, kaiKai, true, true, false, 1, false, eMeuAluno: true,
				doAlunoSozinho: Recusa.Pode,
				alunoAlcancaOKaiKai: EnsinoDeSkill.AlcancaOKaiKai("Human", new Jandirus.Core.Stats.Fighter { Race = "Human" }));
			AfirmarEns("11k5. A LICAO DE MESTRE tambem recusa o Humano sem ki divino", deMestre == RecusaDeLicao.SemKiDivino, $"{deMestre}");

			// ---- 11k6) SO O KAI KAI ----
			Zerar(a, b);
			b.Ficha.godki = null;
			a.Livro.Dar(outra.Path);
			b.Livro.Conceder(50);
			Encostar(a, b, tiles: 1);
			AfirmarEns($"11k6. A regra e so do Kai Kai: o mesmo Humano sem ki divino aprende '{outra.Nome}' do mesmo mestre",
					   Licao(a, b, outra));

			// ---- 11k7) O BIO ----
			Gestacao DoDoadorKai() => new()
			{
				Amostras = { new Amostra { Doador = "bancada: o doador Kai" } },
				Tecnicas = { kaiKai.Path },
			};
			Zerar(b);
			b.Ficha.godki = null;
			int semKi = HerdarOLivroDosDoadores(b, DoDoadorKai());
			Zerar(b);
			Jandirus.Core.Forms.DespertarDivino.Despertar(b.Ficha);
			int comKi = HerdarOLivroDosDoadores(b, DoDoadorKai());
			AfirmarEns("11k7. O BIO sem ki divino nao herda o Kai Kai do doador Kai (a copia dele e ensinada) -- e com o ki divino herda",
					   semKi == 0 && comKi == 1 && b.Livro.Sabe(kaiKai.Path), $"sem {semKi}, com {comKi}");
		}
		finally
		{
			b.Race = b.Ficha.Race = racaDoB;
			b.Ficha.godki = null;
			a.RecargaDaLicao = 0;
			b.PedidoDeLicao = null;
			Zerar(a, b);
		}
	}

	// =====================================================================
	// OS AJUDANTES
	// =====================================================================
	/// <summary>
	/// UMA LICAO INTEIRA PELO CAMINHO DE PRODUCAO: o verb do mestre, o pendente, o verb do aluno.
	///
	/// Passa pelo <see cref="UsarVerboDeEnsino"/> e nao pelos metodos direto pra que o parser do
	/// sufixo (`ens_ensinar:&lt;typepath&gt;`) entre na conta -- ele e o unico caminho que o cliente
	/// tem, e uma bancada que o pulasse testaria um atalho.
	/// </summary>
	private bool Licao(ServerPlayer mestre, ServerPlayer aluno, Skill s)
	{
		aluno.PedidoDeLicao = null;
		UsarVerboDeEnsino(mestre, $"ens_ensinar:{s.Path}");
		if (aluno.PedidoDeLicao == null) return false;
		UsarVerboDeEnsino(aluno, "ens_licao_sim");
		return aluno.Livro.Sabe(s.Path);
	}

	/// <summary>
	/// DEVOLVE OS CORPOS AO ZERO entre uma secao e outra -- livro vazio, sem recarga, sem pendente.
	///
	/// Ela existe pela mesma licao que o `Estacionar` do `--mestreteste`: estado que sobra de uma
	/// secao anterior nao produz uma falha honesta, produz uma falha de bancada. Aqui o que sobrava
	/// era a skill ja aprendida (toda licao seguinte viraria `AlunoJaSabe`) e a recarga de 6 s.
	/// </summary>
	private static void Zerar(params ServerPlayer[] corpos)
	{
		foreach (ServerPlayer p in corpos)
		{
			p.Livro = new SkillBook();
			p.Niveis = new NiveisDeSkill();
			p.RecargaDaLicao = 0;
			p.PedidoDeLicao = null;
			p.SigSkills = "";
		}
	}
}
