using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Magic;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DOS DESEJOS, DA LINGUA DOS DEUSES E DO PROCURADOR (`--desejoteste`).
///
/// ============================ O QUE SO DAQUI SE RESPONDE ============================
///   1. **A ESCADA DE PODER E A DO DM?** `log()` do BYOND e logaritmo NATURAL. Com `Log10` toda a
///      tabela acima do patamar 3 desapareceria em silencio -- e o jogo continuaria funcionando, com
///      tres desejos. Ancorada num numero conhecido.
///   2. **O PORUNGA E DIFERENTE DO SHENRON?** As duas metades: o set de 3 pedidos **nao** oferece
///      "Revive-All" nem "Kill Somebody", e o de 1 pedido **oferece**. Essa e a unica diferenca
///      mecanica entre os dois dragoes, e so a segunda metade prova que a lista nao esta vazia.
///   3. **O NAO-PORTADO COBRA?** As duas metades: pedir um desejo nao portado **nao** consome, e
///      pedir um portado **consome**. So a primeira ficaria verde num sistema que nunca conta nada.
///   4. **O CRIADOR SE AMPLIFICA?** As duas metades: o criador leva recusa, e um estranho leva o BP.
///   5. **O DESEJO DE REVIVER COBRA O PRECO DO CARGO?** A pergunta que a Fase 0 mandou medir. Medida
///      pelo RESULTADO: o morto volta **e** o `ResurrectedCount` dele NAO subiu -- e quem revive nao
///      cai morto. Mais a outra metade: quem morreu de VELHICE nao volta.
///   6. **O "MAIS FORTE DO UNIVERSO" E UM BUFF?** Nao: o BP dobra, a divida nasce, e adiantado o
///      relogio o **tique de producao** MATA e marca `aged_out`. Sem a terceira, o desejo mais caro
///      do jogo seria um presente.
///   7. **A LINGUA TEM OS DOIS EIXOS?** Cargo divino liga, cargo NAO divino nao liga; sangue Kai liga,
///      sangue comum nao liga. E os onze globais do DM resolvem pra um cargo deste port -- um global
///      orfao faria o cargo sair da lingua **calado**.
///   8. **A LINGUA ESQUECE?** Nao: destituido, ela fica. E o `if(godtongue) return 1` do original.
///   9. **O PORTAO DO SUPER SHENRON MORDE?** Sem lingua, com as sete: recusa **e o ciclo nao anda**
///      (nada foi consumido). Com lingua: concede **e o ciclo anda**.
///  10. **A ORDEM DAS GUARDAS E A DO DM?** Com 3 de 7 e sem lingua, a frase e a DAS SETE. Invertida,
///      ninguem jamais leria a recusa que ENSINA o procurador.
///  11. **QUEM RECEBE O DESEJO?** O PEDINTE. Medido no bolso: o zeni entra no doador e **nao** no
///      falante.
///  12. **O FALANTE PODE TROCAR O DESEJO?** Nao, e esta e a prova que o dono pediu: o falante manda
///      `sdb_invocar sdb_supremo` e o que acontece e a RIQUEZA que o pedinte registrou -- e o pedinte
///      **nao** fica com a divida do supremo. As duas metades do mesmo tiro.
///  13. **A PROCURACAO SE REPASSA?** Nao (buraco A da Fase 0). E o pedinte pode RETOMAR.
///  14. **O PEDINTE FORA DO MUNDO?** Recusa, e **nada e consumido** (buraco B da Fase 0).
///  15. **O CORPO SAIYAJIN ABRE A ESCADA ROSE?** Medido no `Avaliar` de producao: antes a linha esta
///      fechada pra ele, depois nao esta -- e um Saiyajin de OUTRA classe continua fechado.
/// ================================================================================
///
/// ============================ AS TRES FAMILIAS COM DEFEITO INJETADO ============================
///   A. **O PORTAO DA LINGUA** -> reprova se o Super Shenron atender quem nao fala. Defeito injetado:
///      o campo `godtongue` ligado na mao. Prova que o criterio le O PORTAO, e nao "deu erro".
///   B. **A PROCURACAO NAO SE REPASSA** -> reprova se o procurador conseguir transferir. Defeito
///      injetado: a procuracao apagada. Prova que quem barra e a PROCURACAO, e nao "ele nao tem as
///      sete" -- que era a unica pergunta do DM.
///   C. **A VELHICE FECHA O REVIVE** -> reprova se um morto de velhice aparecer como alvo. Defeito
///      injetado: `aged_out` desligado. Prova que o filtro le o campo.
/// ==========================================================================================
///
///     Godot --headless --path . --host --rede 7978 --conta bancada_desejo --senha teste
///            --nome DesejoBanca --raca Namekian --desejoteste
/// </summary>
public partial class GameServer
{
	private bool _desejoDeTeste;

	private delegate void ChecagemDeDesejo(string nome, bool cond, string detalhe = "");

	/// <summary>Roda uma vez, no primeiro login. MEXE no mundo e no disco -- so com a flag.</summary>
	private void RodarBancadaDosDesejos(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA DOS DESEJOS, DA LINGUA E DO PROCURADOR =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// ============================ O MUNDO VOLTA COMO ESTAVA ============================
		// Ela ergue estatuas, mata e ressuscita gente, troca a raca e a classe do testador, forja
		// corpos, mexe nos tronos, adianta o relogio do ceu e escreve nos dois arquivos das esferas.
		// Tudo fotografado aqui e devolvido no `finally`.
		// ==============================================================================
		var setsGuardados = new List<SetDeEsferas>(_sets);
		var esferasGuardadas = new List<Esfera>(_esferas);
		var supersGuardadas = _supers.Select(s => (s.Numero, s.Dono, s.DonoNome)).ToList();
		int cicloGuardado = _cicloDasSupers;
		(string bs, string bn, string bp2, string ba) benefGuardado =
			(_benefSig, _benefNome, _pedidoDoBenef, _alvoDoPedido);
		double ceuGuardado = _adiantoDoCeu;
		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		string racaGuardada = pl.Race, classeGuardada = pl.Class;
		string racaMaeGuardada = pl.Ficha.ParentRace;
		double bpGuardado = pl.Ficha.BP, zeniGuardado = pl.Ficha.Zeni;
		int idadeGuardada = pl.Idade;
		bool linguaGuardada = pl.Ficha.godtongue;
		var tronosGuardados = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		var forjados = new List<ServerPlayer>();

		EscutaDosDesejos = [];
		EscutaDasSupers = [];
		List<string>? escutaDeAvisosAntes = EscutaDeAvisos;

		try
		{
			// =====================================================================
			// 1. A ESCADA DE PODER -- e ela e `log` NATURAL
			// =====================================================================
			// ANCORA CONHECIDA: com Wishs=1 e WishPower = e^2, `log(e^2)^2 + 1` = 2^2 + 1 = 5.
			double ancora = Desejos.PoderVerdadeiro(Math.E * Math.E, 1);
			Checa("o patamar sai de `log(max(Poder/Pedidos,1))^2 + 1` (WishTable.dm:117)",
				  Math.Abs(ancora - 5) < 0.0001, $"deu {ancora:0.####}, esperado 5");

			// A METADE QUE PEGA O `Log10`: com 100 e 1 pedido, o natural da ~22,2 e o decimal daria 5.
			// Com 5, a tabela pararia no patamar 5 e "Super Saiyajin", "Revive-All", "Kill Somebody" e
			// "Imortalidade" sumiriam do jogo sem uma linha de erro.
			double cem = Desejos.PoderVerdadeiro(100, 1);
			Checa("...e e logaritmo NATURAL, e nao base 10 (com Log10 a tabela pararia no patamar 5)",
				  cem > 20 && cem < 24, $"deu {cem:0.##} (Log10 daria 5)");

			// MAIS PEDIDOS = PEDIDOS MAIS FRACOS: e a unica coisa que o divisor faz.
			Checa("...e mais pedidos por invocacao BAIXAM o patamar de cada um",
				  Desejos.PoderVerdadeiro(1_000_000, 3) < Desejos.PoderVerdadeiro(1_000_000, 1));

			// =====================================================================
			// 2. O MENU -- a diferenca SHENRON x PORUNGA, as duas metades
			// =====================================================================
			const double poderAlto = 2_000_000;
			string[] shenron = [.. Desejos.Menu(poderAlto, 1, false, false, 100).Select(d => d.Id)];
			string[] porunga = [.. Desejos.Menu(poderAlto, 3, false, false, 100).Select(d => d.Id)];

			Checa("um set de 1 pedido OFERECE 'Revive-All' e 'Kill Somebody' (WishTable.dm:142)",
				  shenron.Contains("reviver_todos") && shenron.Contains("matar"),
				  string.Join(",", shenron));

			// ESTA E A METADE QUE VALE: o gate `Wishs<=2` e a UNICA diferenca mecanica entre os dois
			// dragoes, e o Porunga eterno tem 3 por define.
			Checa("...e o PORUNGA (3 pedidos) NAO oferece nenhum dos dois -- e a diferenca entre eles",
				  !porunga.Contains("reviver_todos") && !porunga.Contains("matar"),
				  string.Join(",", porunga));

			Checa("o desejo supremo so aparece se o criador o COMPROU (WishTable.dm:147-148)",
				  !shenron.Contains("supremo")
				  && Desejos.Menu(poderAlto, 1, true, false, 100).Any(d => d.Id == "supremo"));

			Checa("o Super Saiyajin so aparece com 25%+ de genoma Saiyajin (WishTable.dm:141)",
				  !Desejos.Menu(poderAlto, 1, false, false, 10).Any(d => d.Id == "super_saiyajin")
				  && shenron.Contains("super_saiyajin"));

			Checa("o 'Corpo de um Saiyajin' some do menu do Super quando ha PROCURACAO (:1598)",
				  Desejos.MenuDoSuper(true, false).Any(d => d.Id == "sdb_corpo_saiyajin")
				  && !Desejos.MenuDoSuper(true, true).Any(d => d.Id == "sdb_corpo_saiyajin"));

			Checa($"a tabela conta {Desejos.Portados} desejos portados e {Desejos.NaoPortados} nao portados",
				  Desejos.Portados > 0 && Desejos.NaoPortados > 0);

			// TODO NAO-PORTADO DIZ O PORQUE. Um "nao foi portado" sem motivo e uma recusa que nao ensina
			// nada -- e vira uma linha que ninguem sabe se ainda e verdade daqui a um ano.
			Checa("...e todo nao-portado carrega o MOTIVO por escrito",
				  Desejos.Todos.Where(d => !d.Portado).All(d => d.Falta.Length > 20),
				  string.Join(", ", Desejos.Todos.Where(d => !d.Portado && d.Falta.Length <= 20)
										  .Select(d => d.Id)));

			// =====================================================================
			// 3. O SET DE VERDADE -- ergue, acorda, invoca
			// =====================================================================
			_sets.RemoveAll(s => !s.Eterno);
			_esferas.RemoveAll(e => !_sets.Any(s => s.Id == e.Set));
			var terra = ZoneKey.Premade("Earth");
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));

			pl.Race = "Namekian";
			pl.Class = "Dragon clan";
			_tronos["guardian"] = pl.Conta;
			pl.Ficha.BP = 5_000_000;
			pl.Ficha.Statify();

			ErguerEstatua(pl, "");
			SetDeEsferas? set = _sets.Find(s => !s.Eterno && s.Zona.Hash == terra.Hash);
			Checa("o verbo de producao ergueu a estatua e criou as sete",
				  set != null && _esferas.Count(e => e.Set == set.Id) == Esferas.Total);

			if (set == null) throw new InvalidOperationException("sem set: o resto da bancada nao roda");

			// TRES PEDIDOS, pra medir o consumo mais de uma vez sem re-erguer o set a cada desejo.
			set.Desejos = 3;
			_adiantoDoCeu += (set.AtivoEm - TempoDoMundo) + 5;
			Checa("...e passada a espera de nascimento elas ACORDAM", SetAtivo(set));

			// ---------------------------------------------------------------- o dragao de pe
			JuntarAsSete(pl, set);
			InvocarODragao(pl);
			Checa("com as sete reunidas o dragao SOBE (o plugue da Fase 1, agora ligado)",
				  _invocacoes.ContainsKey(set.Id));

			// =====================================================================
			// 4. O NAO-PORTADO NAO COBRA -- as duas metades
			// =====================================================================
			int pedidosAntes = set.Pedidos;
			PedirDesejo(pl, "alma");
			Checa("um desejo NAO PORTADO nao consome pedido (o caminho das tecnicas nao portadas)",
				  set.Pedidos == pedidosAntes, $"{pedidosAntes} -> {set.Pedidos}");
			Checa("...e o dragao continua de pe pra ele pedir outra coisa",
				  _invocacoes.ContainsKey(set.Id));

			// A OUTRA METADE: um desejo PORTADO consome. Sem ela, "nao consome" seria indistinguivel de
			// um contador quebrado.
			double zeniAntes = pl.Ficha.Zeni;
			EscutaDosDesejos!.Clear();
			PedirDesejo(pl, "dinheiro");
			Checa("...e um desejo PORTADO consome (senao 'nao consome' nao quer dizer nada)",
				  set.Pedidos == pedidosAntes + 1, $"{pedidosAntes} -> {set.Pedidos}");
			Checa($"...e a riqueza entrou: +{Desejos.ZeniDoDesejo:N0} zeni",
				  Math.Abs(pl.Ficha.Zeni - (zeniAntes + Desejos.ZeniDoDesejo)) < 1,
				  $"{zeniAntes:N0} -> {pl.Ficha.Zeni:N0}");

			// ============================ E O MUNDO OUVIU ============================
			// Todo desejo do original e anunciado (`to_chat(view(originator), ...)` em cada ramo, e
			// `to_chat(world, ...)` nos grandes) -- e isso **e mecanica**, e nao enfeite: um desejo que
			// acontece em silencio nao da a ninguem a chance de reagir a ele. Esta e a linha que le a
			// `EscutaDosDesejos`; sem ela o canal seria mais um campo escrito pra ninguem.
			// =====================================================================
			Checa("...e o MUNDO ouviu o pedido (o anuncio de cada desejo, e nao so o efeito)",
				  EscutaDosDesejos.Any(t => t.Contains("RIQUEZA", StringComparison.OrdinalIgnoreCase)
										 && t.Contains(pl.Name, StringComparison.Ordinal)),
				  string.Join(" | ", EscutaDosDesejos));

			// =====================================================================
			// 5. O BLOQUEIO DO CRIADOR -- as duas metades
			// =====================================================================
			// O testador E o criador deste set: ele nao pode pedir "poder".
			double bpAntes = pl.Ficha.BP;
			PedirDesejo(pl, "poder");
			Checa("o CRIADOR do set nao amplifica a si mesmo (WishTable.dm:199 e :207)",
				  Math.Abs(pl.Ficha.BP - bpAntes) < 1, $"{bpAntes:N0} -> {pl.Ficha.BP:N0}");
			Checa("...e a recusa ENCERRA a invocacao, como o `break` do original (:173)",
				  !_invocacoes.ContainsKey(set.Id));

			// A OUTRA METADE: um ESTRANHO recebe. Forjado com assinatura propria -- e a assinatura que
			// o bloqueio compara (`CreatorSig`), e nao o nome.
			ServerPlayer estranho = Forjar("bancada: o estranho", pl.Pos, terra, "bancada_desejo_b");
			forjados.Add(estranho);
			estranho.Ficha.BP = 1_000_000;

			// `Tick()` E NAO `Statify()`: quem recalcula o `relBPmax` (o teto pessoal de treino) e o
			// `PowerLevel`, e o `CapCheck` do desejo de "Poder" LE esse teto. Escrever o BP e chamar so o
			// `Statify` deixa o teto colado no BP antigo -- e o `CapCheck` devolve ZERO. A bancada leu
			// "1.000.000 -> 1.000.000" e chamou o desejo de quebrado; o quebrado era o corpo de teste.
			// (Em jogo isso nao aparece: o tique do servidor roda o `PowerLevel` todo quadro.)
			estranho.Ficha.Tick();

			// as sete vao pro estranho e ele invoca -- pelo helper, que DERRUBA o dragao anterior
			PorODragaoDePe(estranho, set);
			double bpEstranhoAntes = estranho.Ficha.BP;
			PedirDesejo(estranho, "poder");
			Checa("...e quem NAO e o criador recebe o poder (senao o bloqueio seria 'ninguem pede')",
				  estranho.Ficha.BP > bpEstranhoAntes,
				  $"{bpEstranhoAntes:N0} -> {estranho.Ficha.BP:N0}");

			// =====================================================================
			// 6. O DESEJO DE REVIVER -- a pergunta que a Fase 0 mandou medir
			// =====================================================================
			ServerPlayer morto = Forjar("bancada: o caido", pl.Pos, terra, "bancada_desejo_c");
			forjados.Add(morto);
			morto.Combate.Morrer(ignorarSeguro: true);
			int contagemAntes = morto.Ficha.ResurrectedCount;

			PorODragaoDePe(pl, set);
			PedirDesejo(pl, "reviver " + morto.Name);

			Checa("o desejo RESSUSCITA de verdade (pelo `Combate.Reviver` de producao)",
				  !morto.Ficha.dead);

			// ============================ A METADE QUE A FASE 0 PEDIU, E ELA E A DECISAO ============================
			// O DM chama `ReviveMe()` direto (`WishTable.dm:229`) e **nao encosta** no `ResurrectedCount`;
			// quem soma nele e so o verb de CARGO (`OtherworldRankSkills.dm:237`), e e la que mora o
			// preco ("a partir da 2a volta, quem ressuscita morre no lugar"). Se a Fase 2 tivesse reusado
			// o `RessuscitarG4` "porque ja existe", esta linha ficaria vermelha -- e a de baixo tambem.
			// ==================================================================================================
			Checa("...e NAO cobra o preco do cargo: `ResurrectedCount` nao subiu (ProceduralSpace.dm:1614)",
				  morto.Ficha.ResurrectedCount == contagemAntes,
				  $"{contagemAntes} -> {morto.Ficha.ResurrectedCount}");
			Checa("...e quem pediu NAO morreu no lugar (o castigo do cargo nao alcanca a esfera)",
				  !pl.Ficha.dead);

			// FAMILIA C -- a velhice fecha o revive, com defeito injetado.
			ServerPlayer velho = Forjar("bancada: o ancião", pl.Pos, terra, "bancada_desejo_d");
			forjados.Add(velho);
			velho.Combate.Morrer(ignorarSeguro: true);
			velho.Ficha.aged_out = true;

			bool VelhiceNaoVolta()
			{
				EscutaDeAvisos = [];
				try
				{
					PorODragaoDePe(pl, set);
					PedirDesejo(pl, "reviver " + velho.Name);
					return velho.Ficha.dead;   // continua morto: nem o dragao traz de volta
				}
				finally { EscutaDeAvisos = null; }
			}

			MutacaoDeDesejo(Checa,
				"quem morreu de VELHICE nao volta por desejo nenhum (WishTable.dm:222)",
				"a marca `aged_out` desligada",
				VelhiceNaoVolta,
				() => velho.Ficha.aged_out = false,
				() => { velho.Ficha.aged_out = true; velho.Combate.Morrer(ignorarSeguro: true); });

			// =====================================================================
			// 7. MARCOS -- uma vez por vida, as duas metades
			// =====================================================================
			pl.Ficha.wishedpoints = 0;
			int marcosAntes = pl.Livro?.MarcosLivres ?? 0;
			PorODragaoDePe(pl, set);
			PedirDesejo(pl, "marcos");
			Checa($"o desejo de Marcos da {Desejos.MarcosDoDesejo} (WishTable.dm:367-370)",
				  (pl.Livro?.MarcosLivres ?? 0) == marcosAntes + Desejos.MarcosDoDesejo,
				  $"{marcosAntes} -> {pl.Livro?.MarcosLivres}");

			int pedidosDepois = set.Pedidos;
			PedirDesejo(pl, "marcos");
			Checa("...e a SEGUNDA vez e recusada, sem consumir pedido (uma vez por vida)",
				  (pl.Livro?.MarcosLivres ?? 0) == marcosAntes + Desejos.MarcosDoDesejo
				  && set.Pedidos == pedidosDepois,
				  $"marcos {pl.Livro?.MarcosLivres}, pedidos {pedidosDepois} -> {set.Pedidos}");

			// =====================================================================
			// 8. "O MAIS FORTE DO UNIVERSO" -- as TRES metades
			// =====================================================================
			set.TemSupremo = true;
			PorODragaoDePe(estranho, set);

			double maiorDoMundo = _players.Values.Max(o => o.Ficha.BP);
			PedirDesejo(estranho, "supremo");

			Checa($"o supremo poe o BP no DOBRO do maior do jogo (SW_STRONGEST_MULT 2)",
				  estranho.Ficha.BP >= maiorDoMundo * Desejos.MultiplicadorDoSupremo - 1,
				  $"maior {maiorDoMundo:N0} -> {estranho.Ficha.BP:N0}");

			Checa("...e a DIVIDA nasce junto (`sw_doom_year`) -- sem ela seria um buff de graca",
				  estranho.Ficha.sw_doom_year > TempoDoMundo);

			// A TERCEIRA METADE, E A QUE IMPORTA: adiantar o relogio e deixar o TIQUE DE PRODUCAO cobrar.
			// Sem ela, "tem prazo" seria indistinguivel de "o prazo nunca chega" (corolario 0.7).
			_adiantoDoCeu += (estranho.Ficha.sw_doom_year - TempoDoMundo) + 5;
			TickDasEsferas();
			Checa("...e vencido o prazo o TIQUE cobra: ele MORRE",
				  estranho.Ficha.dead);
			Checa("...e a morte e de VELHICE (`aged_out`): nem as Super Esferas a desfazem",
				  estranho.Ficha.aged_out);

			// =====================================================================
			// 9. A LINGUA DOS DEUSES -- os DOIS eixos, cada um com as duas metades
			// =====================================================================
			GD.Print("  -- a lingua dos deuses --");

			// (a) OS ONZE GLOBAIS DO DM RESOLVEM PRA UM CARGO DESTE PORT.
			// Um global orfao nao quebra nada: o cargo continua existindo e simplesmente sai da lingua,
			// em silencio. E exatamente por isso esta linha existe.
			string[] orfaos = [.. LinguaDosDeuses.GlobaisSemCargo];
			Checa($"os {LinguaDosDeuses.GlobaisDivinos.Length} cargos divinos do `divine_rank_now` "
				+ "acham cargo neste port", orfaos.Length == 0, "orfaos: " + string.Join(", ", orfaos));

			// (b) EIXO CARGO -- as duas metades.
			ServerPlayer kaio = Forjar("bancada: o Kaio", pl.Pos, terra, "bancada_desejo_e");
			forjados.Add(kaio);
			kaio.Race = "Human";
			kaio.Ficha.ParentRace = "";

			_tronos["turtle"] = kaio.Conta;
			ConferirALingua(kaio);
			Checa("um cargo NAO divino (Eremita Tartaruga) NAO ensina a lingua",
				  !kaio.Ficha.godtongue);

			_tronos.Remove("turtle");
			_tronos["nkai"] = kaio.Conta;
			ConferirALingua(kaio);
			Checa("...e um cargo DIVINO (Kaio do Norte) ensina", kaio.Ficha.godtongue);

			// (c) ELA NUNCA SE ESQUECE -- `if(godtongue) return 1` (WishTable.dm:38).
			_tronos.Remove("nkai");
			ConferirALingua(kaio);
			Checa("...e DESTITUIDO ele continua falando ('rank divino atual ou PASSADO')",
				  kaio.Ficha.godtongue);

			// (d) EIXO SANGUE -- as duas metades. E o eixo que a Fase 0 avisou que seria esquecido.
			ServerPlayer humano = Forjar("bancada: o humano", pl.Pos, terra, "bancada_desejo_f");
			forjados.Add(humano);
			humano.Race = "Human";
			ConferirALingua(humano);
			Checa("sangue comum NAO fala a lingua", !humano.Ficha.godtongue);

			ServerPlayer sangue = Forjar("bancada: o Kai de sangue", pl.Pos, terra, "bancada_desejo_g");
			forjados.Add(sangue);
			sangue.Race = "Kai";
			sangue.Ficha.Race = "Kai";
			ConferirALingua(sangue);
			Checa("...e a RACA Kai nasce falando, sem cargo nenhum (WishTable.dm:39)",
				  sangue.Ficha.godtongue);

			ServerPlayer meio = Forjar("bancada: o meio-Demigod", pl.Pos, terra, "bancada_desejo_h");
			forjados.Add(meio);
			meio.Race = "Human";
			meio.Ficha.ParentRace = "Demigod";
			ConferirALingua(meio);
			Checa("...e o `Parent_Race` conta tambem (metade do teste do DM, e a que se esquece)",
				  meio.Ficha.godtongue);

			// =====================================================================
			// 10. O PORTAO DO SUPER SHENRON
			// =====================================================================
			GD.Print("  -- o Super Shenron --");

			ServerPlayer mudo = Forjar("bancada: o mudo", pl.Pos, terra, "bancada_desejo_i");
			forjados.Add(mudo);
			mudo.Race = "Human";
			mudo.Ficha.ParentRace = "";
			mudo.Ficha.godtongue = false;

			// A ORDEM DAS GUARDAS: com 3 de 7 e sem lingua, a frase tem que ser a DAS SETE.
			DarSupers(mudo, 3);
			EscutaDeAvisos = [];
			ChamarOSuperShenron(mudo, "");
			bool falouDasSete = EscutaDeAvisos.Any(t => t.Contains("de 7") || t.Contains("das 7"));
			bool falouDaLingua = EscutaDeAvisos.Any(t => t.Contains("LÍNGUA DOS DEUSES"));
			EscutaDeAvisos = null;
			Checa("com 3 de 7 a recusa e a DAS SETE, e nao a da lingua (a ordem do DM, :1584 antes de :1587)",
				  falouDasSete && !falouDaLingua);

			// SEM LINGUA, COM AS SETE: recusa, e o ciclo NAO anda.
			DarSupers(mudo, SuperEsferas.Total);

			bool SuperRecusaSemLingua()
			{
				EscutaDeAvisos = [];
				try
				{
					int c = _cicloDasSupers;
					ChamarOSuperShenron(mudo, "sdb_riqueza");
					return _cicloDasSupers == c;   // nada consumido
				}
				finally { EscutaDeAvisos = null; }
			}

			// FAMILIA A -- o defeito injetado e a flag ligada na mao.
			MutacaoDeDesejo(Checa,
				"sem a LINGUA o Super Shenron recusa, e NADA e consumido (:1587-1589)",
				"o campo `godtongue` ligado na mao",
				SuperRecusaSemLingua,
				() => mudo.Ficha.godtongue = true,
				() => { mudo.Ficha.godtongue = false; DarSupers(mudo, SuperEsferas.Total); });

			// O CICLO E LIDO **DEPOIS** DA FAMILIA A, e nao antes: o defeito injetado faz o desejo
			// ACONTECER de propósito, e acontecer consome as sete e anda o ciclo. Comparar com o valor
			// de antes da mutacao mediria o estrago da mutacao e chamaria isso de falha da recusa.
			int cicloAntes = _cicloDasSupers;
			ChamarOSuperShenron(mudo, "sdb_riqueza");
			Checa("...e as sete continuam com ele depois da recusa (recusa LIMPA, o `return` seco)",
				  QuantasSupersTem(mudo.Assinatura) == SuperEsferas.Total
				  && _cicloDasSupers == cicloAntes,
				  $"tem {QuantasSupersTem(mudo.Assinatura)}, ciclo {cicloAntes} -> {_cicloDasSupers}");

			// A OUTRA METADE: COM lingua, ele atende e o ciclo ANDA.
			ServerPlayer falante = Forjar("bancada: o falante", pl.Pos, terra, "bancada_desejo_j");
			forjados.Add(falante);
			falante.Race = "Kai";
			falante.Ficha.Race = "Kai";
			ConferirALingua(falante);
			DarSupers(falante, SuperEsferas.Total);

			double zeniFalanteAntes = falante.Ficha.Zeni;
			int cicloAntesDoDesejo = _cicloDasSupers;
			ChamarOSuperShenron(falante, "sdb_riqueza");
			Checa("...e COM a lingua ele atende: a riqueza chega e o ciclo ANDA",
				  falante.Ficha.Zeni > zeniFalanteAntes && _cicloDasSupers == cicloAntesDoDesejo + 1,
				  $"zeni {zeniFalanteAntes:N0} -> {falante.Ficha.Zeni:N0}, "
				  + $"ciclo {cicloAntesDoDesejo} -> {_cicloDasSupers}");

			// =====================================================================
			// 11. O PROCURADOR -- a metade que o dono descreveu com mais detalhe
			// =====================================================================
			GD.Print("  -- o procurador --");

			// O PEDINTE nao fala; o PORTA-VOZ fala. Os dois lado a lado (o gesto e presencial).
			ServerPlayer pedinte = Forjar("bancada: o pedinte", pl.Pos, terra, "bancada_desejo_k");
			forjados.Add(pedinte);
			pedinte.Race = "Human";
			pedinte.Ficha.ParentRace = "";
			pedinte.Ficha.godtongue = false;
			DarSupers(pedinte, SuperEsferas.Total);

			ServerPlayer portaVoz = Forjar("bancada: o porta-voz", pl.Pos, terra, "bancada_desejo_l");
			forjados.Add(portaVoz);
			portaVoz.Race = "Kai";
			portaVoz.Ficha.Race = "Kai";
			ConferirALingua(portaVoz);

			// A oferta e a aceitacao -- os dois lados, como no DM.
			TransferirAGuarda(pedinte, portaVoz.Name);
			Checa("a oferta de guarda fica de pe esperando o SIM (o `alert(R,...)` virou oferta com prazo)",
				  _ofertasDeGuarda.ContainsKey(portaVoz.Conta));

			// A RECUSA NAO MOVE NADA -- a borda "o falante recusa".
			ResponderAGuarda(portaVoz, aceitou: false);
			Checa("...e RECUSADA, as sete ficam com quem as tinha (o falante pode dizer nao)",
				  QuantasSupersTem(pedinte.Assinatura) == SuperEsferas.Total && !HaProcuracao);

			// Agora aceitando.
			TransferirAGuarda(pedinte, portaVoz.Name);
			ResponderAGuarda(portaVoz, aceitou: true);
			Checa("aceita, as SETE passam ao porta-voz",
				  QuantasSupersTem(portaVoz.Assinatura) == SuperEsferas.Total
				  && QuantasSupersTem(pedinte.Assinatura) == 0);
			Checa("...e o BENEFICIARIO gravado e o PEDINTE (:1676-1677)",
				  string.Equals(_benefSig, pedinte.Assinatura, StringComparison.Ordinal));

			// ============================ "DOIS PEDINTES AO MESMO TEMPO" E IMPOSSIVEL ============================
			// A tarefa mandou pensar essa borda. O slot de beneficiario e UNICO (como o `sdb_benef_sig` do
			// DM), e a razao nao e simplificacao: **so existem SETE Super Esferas no universo**, entao so
			// pode haver um dono das sete e portanto um pedinte. Deixar isso implicito seria deixar a
			// unica coisa que sustenta o slot unico fora do teste.
			// ==================================================================================================
			int comSete = _supers.Where(x => x.Dono.Length > 0)
								 .GroupBy(x => x.Dono, StringComparer.Ordinal)
								 .Count(g => g.Count() >= SuperEsferas.Total);
			Checa("DOIS PEDINTES ao mesmo tempo e impossivel: so ha um conjunto de sete no universo",
				  comSete <= 1, $"{comSete} assinaturas com as sete");

			// ---------------------------------------------------------------- buraco (A)
			ServerPlayer comparsa = Forjar("bancada: o comparsa", pl.Pos, terra, "bancada_desejo_m");
			forjados.Add(comparsa);

			bool NaoRepassa()
			{
				EscutaDeAvisos = [];
				try
				{
					TransferirAGuarda(portaVoz, comparsa.Name);
					return !_ofertasDeGuarda.ContainsKey(comparsa.Conta);   // nem oferta ele abre
				}
				finally { EscutaDeAvisos = null; }
			}

			// FAMILIA B -- o defeito injetado e a procuracao apagada. Ela prova que quem barra e a
			// PROCURACAO (o buraco A do DM) e nao "ele nao tem as sete" -- que era a unica pergunta la.
			string sigGuardada = _benefSig, nomeGuardado = _benefNome;
			MutacaoDeDesejo(Checa,
				"PROCURACAO NAO SE REPASSA: o porta-voz nao transfere a ninguem (buraco A da Fase 0)",
				"a procuracao apagada -- que e o estado em que o DM sempre esteve",
				NaoRepassa,
				() => { _benefSig = ""; _benefNome = ""; },
				() => { _benefSig = sigGuardada; _benefNome = nomeGuardado; _ofertasDeGuarda.Clear(); });

			// ---------------------------------------------------------------- o pedido e do PEDINTE
			RegistrarPedido(pedinte, "sdb_riqueza");
			Checa("so o PEDINTE registra o desejo, e ele foi gravado",
				  _pedidoDoBenef == "sdb_riqueza");

			EscutaDeAvisos = [];
			RegistrarPedido(portaVoz, "sdb_supremo");
			EscutaDeAvisos = null;
			Checa("...e o PORTA-VOZ nao consegue registrar nada (o pedido continua o do pedinte)",
				  _pedidoDoBenef == "sdb_riqueza", $"virou '{_pedidoDoBenef}'");

			// ---------------------------------------------------------------- buraco (B)
			// O pedinte SAI DO MUNDO: nada acontece, e nada e consumido.
			_players.Remove(pedinte.Id);
			ZoneList(pedinte.Zone.Hash).Remove(pedinte);
			int cicloComPedinteFora = _cicloDasSupers;
			EscutaDeAvisos = [];
			ChamarOSuperShenron(portaVoz, "");
			EscutaDeAvisos = null;
			Checa("com o PEDINTE fora do mundo o desejo e recusado, e nada e consumido (buraco B)",
				  _cicloDasSupers == cicloComPedinteFora
				  && QuantasSupersTem(portaVoz.Assinatura) == SuperEsferas.Total);

			// volta pro mundo
			_players[pedinte.Id] = pedinte;
			ZoneList(pedinte.Zone.Hash).Add(pedinte);

			// ---------------------------------------------------------------- **A PROVA CENTRAL**
			// O falante manda `sdb_supremo`. O registrado e `sdb_riqueza`. Os dois lados do mesmo tiro:
			// o que acontece e a RIQUEZA, e ela cai no PEDINTE.
			double zeniPedinteAntes = pedinte.Ficha.Zeni;
			double zeniVozAntes = portaVoz.Ficha.Zeni;
			double dividaPedinteAntes = pedinte.Ficha.sw_doom_year;
			int cicloAntesDaProcuracao = _cicloDasSupers;

			ChamarOSuperShenron(portaVoz, "sdb_supremo");   // <- ele TENTA trocar o desejo

			Checa("O DESEJO CAI NO PEDINTE: a riqueza entrou no bolso de quem emprestou",
				  Math.Abs(pedinte.Ficha.Zeni - (zeniPedinteAntes + Desejos.ZeniDoSuperDesejo)) < 1,
				  $"{zeniPedinteAntes:N0} -> {pedinte.Ficha.Zeni:N0}");

			Checa("...e NAO no porta-voz (a metade que prova que nao foi so 'alguem recebeu')",
				  Math.Abs(portaVoz.Ficha.Zeni - zeniVozAntes) < 1,
				  $"{zeniVozAntes:N0} -> {portaVoz.Ficha.Zeni:N0}");

			// **O FALANTE NAO TROCA O DESEJO** -- ele pediu o supremo e ninguem ficou com a divida.
			Checa("O FALANTE NAO TROCOU O DESEJO: ele pediu 'supremo' e o supremo NAO aconteceu",
				  Math.Abs(pedinte.Ficha.sw_doom_year - dividaPedinteAntes) < 1
				  && portaVoz.Ficha.sw_doom_year <= 0,
				  $"divida do pedinte {dividaPedinteAntes:0} -> {pedinte.Ficha.sw_doom_year:0}, "
				  + $"do porta-voz {portaVoz.Ficha.sw_doom_year:0}");

			Checa("...e as sete foram consumidas e re-espalhadas (o ciclo andou)",
				  _cicloDasSupers == cicloAntesDaProcuracao + 1);

			Checa("...e a PROCURACAO caiu junto (`pspace_sdb_scatter` zera o beneficiario)",
				  !HaProcuracao && _pedidoDoBenef.Length == 0);

			// ---------------------------------------------------------------- o preco da vida
			// O UNICO DESEJO DO JOGO QUE MATA QUEM O PEDE, e por isso o unico que pede a palavra literal
			// do original ("ACEITO O PRECO", :1632-1635). As duas metades: sozinho ele NAO acontece, e com
			// a frase ele acontece -- senao "pede confirmacao" seria indistinguivel de "nao funciona".
			DarSupers(falante, SuperEsferas.Total);
			falante.Ficha.sw_doom_year = 0;
			int cicloAntesDoPreco = _cicloDasSupers;
			EscutaDeAvisos = [];
			ChamarOSuperShenron(falante, "sdb_supremo");
			EscutaDeAvisos = null;
			Checa("o desejo que cobra a VIDA nao acontece sem a palavra (nem pro dono das sete)",
				  falante.Ficha.sw_doom_year <= 0 && _cicloDasSupers == cicloAntesDoPreco);

			AceitarOPreco(falante, "ACEITO O PREÇO");
			Checa("...e com ela acontece, e as sete se consomem",
				  falante.Ficha.sw_doom_year > 0 && _cicloDasSupers == cicloAntesDoPreco + 1,
				  $"divida {falante.Ficha.sw_doom_year:0}, ciclo {cicloAntesDoPreco} -> {_cicloDasSupers}");
			falante.Ficha.sw_doom_year = 0;   // a bancada nao deixa uma sentenca de morte de pe

			// ---------------------------------------------------------------- revogar
			DarSupers(pedinte, SuperEsferas.Total);
			TransferirAGuarda(pedinte, portaVoz.Name);
			ResponderAGuarda(portaVoz, aceitou: true);
			RevogarAGuarda(pedinte);
			Checa("QUEM EMPRESTA PODE RETOMAR: as sete voltam e a procuracao morre",
				  QuantasSupersTem(pedinte.Assinatura) == SuperEsferas.Total && !HaProcuracao);

			// ---------------------------------------------------------------- tomar a forca
			// `:1571-1573` -- o claim tomado a forca QUEBRA a procuracao. Sem isso, o porta-voz infiel
			// lavaria a procuracao combinando uma disputa com um comparsa.
			TransferirAGuarda(pedinte, portaVoz.Name);
			ResponderAGuarda(portaVoz, aceitou: true);
			SuperEsfera alvo1 = _supers[0];
			FecharADisputa(comparsa, alvo1, new DisputaDeSuper
			{
				Numero = alvo1.Numero, Quem = comparsa.Id, Tomada = true, DonoNoInicio = alvo1.Dono,
			});
			Checa("tomar uma esfera A FORCA QUEBRA a procuracao (:1571-1573)", !HaProcuracao);

			// =====================================================================
			// 12. O CORPO SAIYAJIN -- a escada ROSE, que estava orfa
			// =====================================================================
			GD.Print("  -- o corpo saiyajin (e a escada Rose) --");

			ServerPlayer molde = Forjar("bancada: o molde", pl.Pos, terra, "bancada_desejo_n");
			forjados.Add(molde);
			molde.Race = "Saiyan";
			molde.Ficha.Race = "Saiyan";
			molde.Ficha.SaiyanLineage = "Saiyan";
			molde.Ficha.BP = 9_000_000;
			DespertarOKiDivino(molde);
			molde.Ficha.Tick();

			ServerPlayer zamasu = Forjar("bancada: o Kaioshin", pl.Pos, terra, "bancada_desejo_o");
			forjados.Add(zamasu);
			zamasu.Race = "Kai";
			zamasu.Ficha.Race = "Kai";
			zamasu.Class = "Golden Apple";
			zamasu.Ficha.Class = "Golden Apple";
			_tronos["kaioshin"] = zamasu.Conta;

			// ============================ O KI DIVINO E PRE-REQUISITO DAS TRES ESCADAS DIVINAS ============================
			// `LinhasAbertas` so abre GodKi/Rose/Mistico com `p.GodKi >= 0` (ki divino DESPERTO), e a
			// classe so escolhe QUAL das tres. Sem despertar, medir "a Rose abriu" leria `LinhaFechada`
			// dos dois lados e a checagem seria sobre o ki divino, e nao sobre o desejo -- a bancada
			// pegou exatamente isso na primeira rodada.
			//
			// E o DM concorda: `godki_mod` (a "variavel do Rose") e lido em `SaiyanObjects.dm:13` e
			// `:104`, **os dois dentro de `if(container.ssj)`** e depois do despertar. O desejo nao
			// entrega o ki divino -- ele entrega o CORPO que faz o ki divino sair rosa.
			// ========================================================================================================
			DespertarOKiDivino(zamasu);

			Checa("o portao do desejo do Zamasu e TRIPLO: raca Kai + Golden Apple + Kaioshin atual",
				  EhKaioshinDoCorpo(zamasu) && !EhKaioshinDoCorpo(molde));

			// A ESCADA ROSE ANTES: fechada por IDENTIDADE, com o ki divino ja desperto -- entao o que
			// esta fechado e a linha do Kaio, e nao "ele nao tem ki divino".
			RecusaForma antes = zamasu.Forma.Avaliar("rose_ssg", zamasu.Ficha.BP, 1, false, Perfil(zamasu));
			Checa("ANTES do desejo a escada ROSE esta fechada pra ele (com o ki divino JA desperto)",
				  antes is RecusaForma.LinhaFechada or RecusaForma.SemClasse, $"recusa: {antes}");

			DesejoDeCorpoSaiyajin(zamasu, null, molde.Name);

			Checa("o desejo troca a RACA e a CLASSE (`Class = \"Kaio\"`, WishTable.dm:65)",
				  string.Equals(zamasu.Race, "Saiyan", StringComparison.Ordinal)
				  && string.Equals(zamasu.Class, Catalogo.ClasseRose, StringComparison.Ordinal),
				  $"{zamasu.Race}/{zamasu.Class}");

			Checa("...e leva o PODER do molde (`K.BP = T.BP`, :89)",
				  Math.Abs(zamasu.Ficha.BP - molde.Ficha.BP) < 1,
				  $"{zamasu.Ficha.BP:N0} vs {molde.Ficha.BP:N0}");

			Checa("...e corta a idade em 25 (:91-92 -- um Kai de séculos morreria de velhice na hora)",
				  zamasu.Idade <= Jandirus.Core.Races.Envelhecimento.IdadeAdulta,
				  $"idade {zamasu.Idade}");

			// ============================ E AQUI A ESCADA ORFA ACORDA ============================
			// Medido no `Avaliar` DE PRODUCAO, e nao no campo: a linha Rose inteira (`rose_ssg`, `rose`,
			// `rose2`, ...) vive em `Formas.cs` gateada por `PedeClasseUmaDe = [ClasseRose]`, e nada
			// neste port escrevia `Class = "Kaio"`. Este desejo e o unico consumidor -- como no DM.
			// ================================================================================
			RecusaForma depois = zamasu.Forma.Avaliar("rose_ssg", zamasu.Ficha.BP, 1, false, Perfil(zamasu));
			Checa("...e a escada ROSE deixa de estar fechada pra ele (era uma linha inteira ORFA)",
				  depois is not (RecusaForma.LinhaFechada or RecusaForma.SemClasse),
				  $"recusa: {depois}");

			// A METADE NEGATIVA: um Saiyajin de OUTRA classe continua fechado. Sem ela, "abriu" poderia
			// significar "esta aberta pra todo mundo, e sempre esteve".
			RecusaForma doMolde = molde.Forma.Avaliar("rose_ssg", molde.Ficha.BP, 1, false, Perfil(molde));
			Checa("...e um Saiyajin de outra classe CONTINUA fechado (o portao e real)",
				  doMolde is RecusaForma.LinhaFechada or RecusaForma.SemClasse, $"recusa: {doMolde}");

			// =====================================================================
			// 13. SOBREVIVE AO REINICIO?
			// =====================================================================
			DarSupers(pedinte, SuperEsferas.Total);
			TransferirAGuarda(pedinte, portaVoz.Name);
			ResponderAGuarda(portaVoz, aceitou: true);
			RegistrarPedido(pedinte, "sdb_riqueza");
			SalvarSupers();

			string sigNoDisco = _benefSig, pedidoNoDisco = _pedidoDoBenef;
			_benefSig = ""; _benefNome = ""; _pedidoDoBenef = ""; _alvoDoPedido = "";
			_supers.Clear();
			CarregarSupers();

			Checa("a PROCURACAO volta do disco (o DM tambem a persiste, :186-187)",
				  string.Equals(_benefSig, sigNoDisco, StringComparison.Ordinal)
				  && _pedidoDoBenef == pedidoNoDisco,
				  $"'{_benefSig}'/'{_pedidoDoBenef}' vs '{sigNoDisco}'/'{pedidoNoDisco}'");
		}
		catch (Exception ex)
		{
			// ============================ UMA EXCECAO E UMA FALHA, E NAO UM FIM ============================
			// Esta bancada roda dentro do tratador de pacote da rede, e **ele engole excecao num aviso**
			// (`GameServer.cs`, o `catch` do `Wire`). Sem este bloco a bancada parava no meio e imprimia
			// "N OK, 0 FALHA" -- um relatorio verde de um teste que nao terminou. Ja aconteceu nesta
			// sessao, com um `Livro` nulo num corpo forjado.
			// ==========================================================================================
			falhou++;
			GD.PrintErr($"  FALHA (EXCECAO -- a bancada parou aqui): {ex}");
		}
		finally
		{
			// ---- o mundo volta ----
			foreach (ServerPlayer f in forjados)
			{
				_players.Remove(f.Id);
				ZoneList(f.Zone.Hash).Remove(f);
			}

			_sets.Clear();
			_sets.AddRange(setsGuardados);
			_esferas.Clear();
			_esferas.AddRange(esferasGuardadas);
			_invocacoes.Clear();

			foreach ((int n, string dono, string nome) in supersGuardadas)
				if (_supers.Find(s => s.Numero == n) is { } s) { s.Dono = dono; s.DonoNome = nome; }
			_cicloDasSupers = cicloGuardado;
			(_benefSig, _benefNome, _pedidoDoBenef, _alvoDoPedido) = benefGuardado;
			_disputasDeSuper.Clear();
			_ofertasDeGuarda.Clear();
			_precoPendente = default;

			_adiantoDoCeu = ceuGuardado;

			pl.Race = racaGuardada;
			pl.Class = classeGuardada;
			pl.Ficha.ParentRace = racaMaeGuardada;
			pl.Ficha.BP = bpGuardado;
			pl.Ficha.Zeni = zeniGuardado;
			pl.Idade = idadeGuardada;
			pl.Ficha.Idade = idadeGuardada;
			pl.Ficha.godtongue = linguaGuardada;
			pl.Ficha.wishedpoints = 0;
			pl.Ficha.Statify();
			MoveToZone(pl.Id, zonaGuardada, posGuardada);

			_tronos.Clear();
			foreach ((string k, string v) in tronosGuardados) _tronos[k] = v;

			SalvarEsferas();
			SalvarSupers();

			// A BANCADA CHAMA `Persistir` (o revive, o zeni, a idade): o disco levou os numeros de
			// mentira. Devolver so o campo em memoria deixaria o save com eles.
			Persistir(pl);

			EscutaDosDesejos = null;
			EscutaDasSupers = null;
			EscutaDeAvisos = escutaDeAvisosAntes;
		}

		GD.Print($"===== BANCADA DOS DESEJOS: {ok} OK, {falhou} FALHA =====\n");
	}

	// =====================================================================
	// AS FERRAMENTAS DA BANCADA
	// =====================================================================
	/// <summary>
	/// UM CORPO COM IDENTIDADE PROPRIA. Conta e slot nao sao enfeite: sem eles nao ha
	/// <see cref="ServerPlayer.Assinatura"/>, e a assinatura e o que a procuracao, o claim e o bloqueio
	/// do criador comparam -- a bancada mediria "vazio contra vazio" e chamaria isso de resultado.
	///
	/// `Peer` NULO: e por isso que os alvos de desejo perguntam <see cref="EhPessoa"/> e nao
	/// `EhJogador` -- ver o cabecalho de `Pessoas`. Um segundo caminho "so pra teste" mediria o
	/// caminho de teste.
	/// </summary>
	private ServerPlayer Forjar(string nome, Vec2 onde, ZoneKey zona, string conta)
	{
		var novo = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,
			Name = nome,
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = zona,
			Pos = onde,
			Conta = conta,
			Slot = 0,

			// ============================ A FICHA NASCE PELA PORTA DE NASCIMENTO ============================
			// `new Fighter { BP = ... }` parece bastar e **nao basta**: o `UPMod` (o "Potential" da raca)
			// fica no default, o `relBPmax` cola no BP atual, e **o `CapCheck` devolve ZERO**. A bancada
			// pegou isso na cara: o desejo de "Poder" concedia e o BP nao mexia -- exatamente o bug do
			// `UPMod = 0` que a memoria deste projeto ja registra.
			//
			// `Birth.Nascer` e a porta unica de nascimento (servidor, NPCs e ferramentas usam so ela),
			// entao um corpo de bancada montado por ela responde `CapCheck`, `Statify` e envelhecimento
			// como qualquer personagem -- e a bancada mede o jogo, e nao uma ficha de mentira.
			// ==========================================================================================
			Ficha = _racas != null
				? Jandirus.Core.Races.Birth.Nascer(_racas, "Human", "", new Random(1234), nome)
				: new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 100_000 },

			// ============================ O LIVRO NAO E ENFEITE ============================
			// `ServerPlayer.Livro` nasce `null!` e quem o preenche e o login. Sem ele, o `AplicarPoderes`
			// (que TODO desejo que mexe em stat chama) estoura num `pl.Livro.Aprendidas` -- e a excecao
			// sobe pelo tratador de pacote da rede, que a engole num aviso. **A bancada parava no meio e
			// dizia "N OK, 0 FALHA"**, que e o pior jeito de um teste falhar.
			// ==========================================================================
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Race = "Human";
		novo.Ficha.Class = "Normal";
		novo.Ficha.BP = 100_000;
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		return novo;
	}

	/// <summary>
	/// O DRAGAO DE PE PRA `quem`, do zero -- derruba o que estiver la, junta as sete na mao dele e
	/// invoca **pelo verbo de producao**.
	///
	/// ============================ ELA EXISTE PORQUE A BANCADA JA MENTIU SEM ELA ============================
	/// `InvocarODragao` recusa quando ja ha invocacao de pe (a trava por SET, que e o conserto do furo
	/// maior do original). Sem derrubar a anterior, a segunda metade da bancada pedia desejos **sem
	/// dragao nenhum**: `PedirDesejo` recusava calado, os campos nao mudavam, e tres checagens ficavam
	/// vermelhas por um motivo que nao tinha nada a ver com o que elas mediam -- e a familia do
	/// `aged_out` ficava **verde por vacuidade**, que e pior.
	/// ==================================================================================================
	/// </summary>
	private void PorODragaoDePe(ServerPlayer quem, SetDeEsferas s)
	{
		FecharAInvocacao(s.Id, "");
		s.Pedidos = 0;
		foreach (Esfera e in _esferas.Where(x => x.Set == s.Id))
		{
			e.PorZona(quem.Zone);
			e.Portador = quem.Id;
			e.X = quem.Pos.X;
			e.Y = quem.Pos.Y;
		}
		InvocarODragao(quem);
	}

	/// <summary>
	/// DESPERTA O KI DIVINO com maestria cheia. **A bancada precisa dele por causa do `LinhasAbertas`**:
	/// as tres escadas divinas (GodKi, Rose e Mistico) so existem pra quem despertou, e a CLASSE apenas
	/// escolhe qual das tres. Sem isto, "a Rose abriu" leria `LinhaFechada` antes e depois.
	/// </summary>
	private static void DespertarOKiDivino(ServerPlayer quem)
	{
		quem.Ficha.godki ??= new Jandirus.Core.Stats.GodKiState();
		quem.Ficha.godki.awakened = true;
		quem.Ficha.godki.mastery = 100;
	}

	/// <summary>
	/// PoE `quantas` Super Esferas na mao de alguem, e TIRA o resto dele. Sem a segunda metade, uma
	/// checagem que espera "3 de 7" leria as sete de um teste anterior.
	/// </summary>
	private void DarSupers(ServerPlayer quem, int quantas)
	{
		foreach (SuperEsfera s in _supers)
			if (string.Equals(s.Dono, quem.Assinatura, StringComparison.Ordinal))
			{ s.Dono = ""; s.DonoNome = ""; }

		foreach (SuperEsfera s in _supers.Take(quantas))
		{
			s.Dono = quem.Assinatura;
			s.DonoNome = quem.Name;
		}
		SalvarSupers();
	}

	/// <summary>
	/// O `Mutacao` da `--provateste`, na mesma forma e pelo mesmo motivo: **uma checagem que nunca soube
	/// ficar vermelha e indistinguivel de checagem nenhuma**.
	///
	/// (E uma copia da `MutacaoDeEsfera` de proposito -- ela e do arquivo da Fase 1, e importar o
	/// delegate de la amarraria as duas bancadas pelo tipo do `Checa`. Sao quinze linhas.)
	/// </summary>
	private static void MutacaoDeDesejo(ChecagemDeDesejo Checa, string oQue, string oDefeito,
										Func<bool> criterio, Action estragar, Action consertar)
	{
		Checa(oQue, criterio(),
			  "o criterio ja reprova ANTES do defeito -- nao ha nada sendo injetado aqui");

		bool caiu;
		try
		{
			estragar();
			caiu = !criterio();
		}
		finally { consertar(); }

		Checa($"   DEFEITO INJETADO ({oDefeito}): o MESMO criterio REPROVA", caiu,
			  "a checagem de cima e decoracao -- ela nao sabe ficar vermelha");
		Checa("   ...e desfeito o defeito ele volta a passar (era a causa, e nao um estrago que ficou)",
			  criterio());
	}
}
