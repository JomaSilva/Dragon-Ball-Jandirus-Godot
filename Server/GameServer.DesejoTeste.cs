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
			(pl.Livro ??= new Jandirus.Core.Skills.SkillBook()).Dar(Jandirus.Core.Skills.AcendedoresDoPort.EstatuaDoDragao);   // (2026-10-13) a estatua pede a skill dela -- ver `ErguerEstatua`
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
			// (2026-10-22) A FORCA DIVIDIDA: este set tem 3 pedidos, e cada um vale um terco da forca dele
			double riquezaDoSet = Desejos.ZeniDoDesejo * ConfigDaEstatua.ForcaDeCadaPedido(set.Desejos);
			Checa($"...e a riqueza entrou: +{riquezaDoSet:N0} zeni -- um terco dos {Desejos.ZeniDoDesejo:N0}, a forca dividida pelos 3 pedidos do set",
				  Math.Abs(pl.Ficha.Zeni - (zeniAntes + riquezaDoSet)) < 1,
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

			// ============================ O NUMERO SO COM SCOUTER (dono, 2026-10-12) ============================
			// A frase do desejo dizia "sobe de A para B" a qualquer um. A regra principal do BP: em numero so pra
			// quem tem medidor; os outros ouvem o quanto cresceram (`PoderNaFrase`). Medido no texto que o
			// `DesejoDePoder` de producao manda, tres vezes -- cada rodada anota o antes e o depois DELA, porque o
			// desejo mexe no poder de novo.
			// ====================================================================================================
			var rodadas = new List<(double Antes, double Depois)>();
			var (semScouter, comScouter, comDefeito) = AsTresLeiturasDoPoder(estranho, () =>
			{
				estranho.Ficha.Tick();   // o teto de treino acompanha o BP novo -- ver a nota do `Tick`, acima
				double a = estranho.Ficha.BP;
				DesejoDePoder(estranho, set);
				rodadas.Add((a, estranho.Ficha.BP));
			});
			Checa("SEM scouter, o desejo de poder diz o quanto ele cresceu e NAO traz o numero de antes nem o de depois",
				  Disse(semScouter, "você está") && !TrazOPoder(semScouter, rodadas[0].Antes)
				  && !TrazOPoder(semScouter, rodadas[0].Depois), Ultimos(semScouter));
			Checa("COM o scouter ligado, a mesma frase traz os dois numeros",
				  TrazOPoder(comScouter, rodadas[1].Antes) && TrazOPoder(comScouter, rodadas[1].Depois), Ultimos(comScouter));
			Checa("(defeito injetado: o numero sem medidor) sem scouter a frase volta a trazer o numero -- a regua de cima reprovaria",
				  TrazOPoder(comDefeito, rodadas[2].Depois), Ultimos(comDefeito));

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
			// (2026-10-22) os 2 Marcos do DM na forca dividida deste set de 3 pedidos: 1 (`Desejos.MarcosDoFator`, nunca menos de 1)
			Checa($"o desejo de Marcos da {MarcosNoDragao(set)} neste set de 3 pedidos (os {Desejos.MarcosDoDesejo} do WishTable.dm:367-370, na forca dividida)",
				  (pl.Livro?.MarcosLivres ?? 0) == marcosAntes + MarcosNoDragao(set) && MarcosNoDragao(set) == 1,
				  $"{marcosAntes} -> {pl.Livro?.MarcosLivres}");

			int pedidosDepois = set.Pedidos;
			PedirDesejo(pl, "marcos");
			Checa("...e a SEGUNDA vez e recusada, sem consumir pedido (uma vez por vida)",
				  (pl.Livro?.MarcosLivres ?? 0) == marcosAntes + MarcosNoDragao(set)
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

			// O SUPREMO TAMBEM SO DIZ O NUMERO A QUEM TEM SCOUTER -- as mesmas tres leituras, no `DesejoSupremo` de
			// producao. O corpo e o que acabou de morrer do proprio desejo (a frase nao pergunta se ele esta vivo);
			// o poder e o prazo dele voltam ao que eram no fim, pra as secoes de baixo nao herdarem o dobro do dobro.
			double bpDoSupremo = estranho.Ficha.BP, prazoDoSupremo = estranho.Ficha.sw_doom_year;
			rodadas.Clear();
			(semScouter, comScouter, comDefeito) = AsTresLeiturasDoPoder(estranho, () =>
			{
				// (2026-10-21) CADA LEITURA PARTE DO MESMO PODER. O supremo dobra o mais forte, e as tres em cadeia (91 mi, 183 mi, 366 mi)
				// passavam do teto do vidro (`Scouter.TetoDeLeitura`, 100 mi, desde 2026-10-18) justo na leitura COM scouter: ele marcava
				// "???", a frase saia sem numero (o certo) e esta bancada ficou vermelha desde 2026-10-19.
				estranho.Ficha.BP = bpDoSupremo;
				estranho.Ficha.Statify();
				double a = estranho.Ficha.BP;
				DesejoSupremo(estranho, set, estranho);
				rodadas.Add((a, estranho.Ficha.BP));
			});
			estranho.Ficha.BP = bpDoSupremo;
			estranho.Ficha.sw_doom_year = prazoDoSupremo;
			estranho.Ficha.Statify();
			Checa("SEM scouter, o supremo diz quantas vezes ele ficou mais forte e NAO traz o numero do poder absoluto",
				  Disse(semScouter, "queima nas suas veias") && Disse(semScouter, "mais forte")
				  && !TrazOPoder(semScouter, rodadas[0].Depois), Ultimos(semScouter));
			Checa("COM o scouter ligado, o numero aparece", TrazOPoder(comScouter, rodadas[1].Depois), Ultimos(comScouter));
			Checa("(defeito injetado: o numero sem medidor) sem scouter o numero volta -- a regua de cima reprovaria",
				  TrazOPoder(comDefeito, rodadas[2].Depois), Ultimos(comDefeito));

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
			GD.Print("  -- o Super Shenlong --");

			// (palco) O ELENCO DAQUI ESTA NA TERRA, e o dragao so e chamado ao lado de uma Super Esfera (`set src in
			// oview(2)`, `ProceduralSpace.dm:1580`): quem mede o LUGAR e a `--validacoesteste`, e a corrente inteira,
			// de esfera em esfera, e a `--avessoteste`. Daqui ate o fim esta bancada mede as sete, a lingua e a
			// procuracao com o portao de lugar aberto -- e ele volta a fechar no `finally`.
			SuperShenronDeQualquerLugarDeTeste = true;

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
				"sem a LINGUA o Super Shenlong recusa, e NADA e consumido (:1587-1589)",
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

			// ============================ O ZENKAI DO KAIO NAO SE APOSENTA (dono, 2026-10-24) ============================
			// *"o unico q n deveria ter cap de zenkai é o kaioshin q roubou corpo de saiyajin pelas esferas"*. O DM ja dizia isso
			// (`combatgains.dm:36`), e o port tinha o parametro sem chamador: o Kaio se aposentava no teto de todo Saiyajin. Medido no
			// `GainZenkai` DE PRODUCAO, com o BP base no DOBRO do teto e um inimigo muito mais forte: o Kaio ganha, e o molde -- como
			// Saiyajin comum e depois como Legendary -- se aposenta no mesmo BP.
			// ===========================================================================================================
			// (2026-10-26) PELO BANCO DO ZENKAI: com o BP base no dobro do teto e um banco do tamanho do teto, o que o nocaute solta.
			double ZenkaiNoDobroDoTeto(ServerPlayer quem, string classe)
			{
				string classeDaFicha = quem.Ficha.Class;
				double bpDaFicha = quem.Ficha.BP, bancoDaFicha = quem.Ficha.zenkaiGuardado;
				bool mortoDaFicha = quem.Ficha.dead;
				try
				{
					if (classe.Length > 0) quem.Ficha.Class = classe;
					double tetoDaFicha = quem.Ficha.ZenkaiCeiling();
					quem.Ficha.BP = tetoDaFicha * 2;
					quem.Ficha.zenkaiGuardado = tetoDaFicha;
					quem.Ficha.dead = false;
					return quem.Ficha.SoltarOZenkai(morreuAgora: false, maioriaCritica: false, requisitoDoSsj3: 0);
				}
				finally
				{
					quem.Ficha.Class = classeDaFicha; quem.Ficha.BP = bpDaFicha; quem.Ficha.zenkaiGuardado = bancoDaFicha; quem.Ficha.dead = mortoDaFicha;
				}
			}
			double zenkaiDoKaio = ZenkaiNoDobroDoTeto(zamasu, "");
			double zenkaiDoComum = ZenkaiNoDobroDoTeto(molde, "Normal");
			double zenkaiDoLendario = ZenkaiNoDobroDoTeto(molde, "Legendary");
			Checa("ZENKAI SEM TETO SO NO KAIO (dono, 2026-10-24): com o BP base no dobro do teto, o Kaioshin no corpo Saiyajin solta o banco do Zenkai, e o Saiyajin comum e o Legendary no mesmo BP se aposentam (nada sai)",
				  zenkaiDoKaio > 0 && zenkaiDoComum == 0 && zenkaiDoLendario == 0,
				  $"Kaio {zenkaiDoKaio:N0}; comum {zenkaiDoComum:N0}; Legendary {zenkaiDoLendario:N0}");
			Jandirus.Core.Stats.Fighter.KaioSeAposentaDeTeste = true;
			try
			{
				double zenkaiDoKaioComDefeito = ZenkaiNoDobroDoTeto(zamasu, "");
				Checa("   (defeito injetado: `KaioSeAposentaDeTeste`, o jogo de antes) o Kaio se APOSENTA no teto -- a regua de cima REPROVA",
					  zenkaiDoKaioComDefeito == 0, $"Kaio {zenkaiDoKaioComDefeito:N0}");
			}
			finally { Jandirus.Core.Stats.Fighter.KaioSeAposentaDeTeste = false; }

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

			// =====================================================================
			// (2026-10-26) O "AUMENTAR O MEU POTENCIAL" SAIU DO JOGO -- o dono, ao tirar o potencial antigo: ele somava no estoque uma parte
			// de "um ano de vida", a conta da idade que nao soma mais. O "Liberar o meu potencial" fica (solta o banco 1,5x).
			// =====================================================================
			Checa("**O 'AUMENTAR O MEU POTENCIAL' SAIU DO JOGO** (o dono, 2026-10-26): nem na tabela do Shenron, nem na do Super Shenlong -- e o 'Liberar o meu potencial' fica",
				  !Desejos.Todos.Any(d => d.Id == "aumentar_potencial") && !Desejos.DoSuper.Any(d => d.Id == "sdb_aumentar_potencial")
				  && Desejos.Todos.Any(d => d.Id == "potencial") && !Desejos.Menu(Esferas.PoderDoEterno, 1, false, false, 0).Any(d => d.Id.Contains("aumentar")),
				  string.Join(",", Desejos.Todos.Select(d => d.Id).Concat(Desejos.DoSuper.Select(d => d.Id)).Where(i => i.Contains("potencial"))));
			// o set FRACO das reguas do Super, logo abaixo (o poder do set mais fraco de um Shenron de jogador)
			var fraco = new SetDeEsferas { Id = 990, Poder = 1e4, Desejos = 1, NomeDoDragao = "SHENRON" };

			// =====================================================================
			// (2026-10-21) O SUPER SHENLONG: A LISTA INTEIRA DAS BLACK STAR, A 4x -- regra do dono
			// =====================================================================
			// *"super shenlong seria 4 vezes o poder da black star em tudo, menos no potencial, super shenlong sempre vai ter o pedido supremo"*
			// e *"ele daria 15 marcos pra n ficar muito broken"*. Medido pelo `ConcederDoSuper` de producao, com o testador de pedinte.
			string[] doSuper = [.. Desejos.MenuDoSuper(false, false, 0).Select(d => d.Id)];
			string[] precisaTer = ["sdb_riqueza", "marcos", "tecnologia", "juventude", "poder", "inteligencia", "juventude_alheia",
								   "curar_planeta", "potencial", "reviver_todos", "matar", "sdb_reviver", "sdb_supremo"];
			bool listaInteira(string[] ids) => precisaTer.All(ids.Contains) && !ids.Contains("dinheiro") && !ids.Contains("reviver")
											   && !ids.Contains("supremo") && !ids.Contains("aumentar_potencial") && !ids.Contains("nada")
											   && !ids.Contains("cancelar");
			Checa("**O SUPER SHENLONG TEM A LISTA INTEIRA DAS BLACK STAR** (os desejos do Shenron que ele nao tinha) e o Supremo SEMPRE -- sem os "
				  + "repetidos: a riqueza, o reviver, o potencial e o supremo so na versao dele", listaInteira(doSuper), string.Join(",", doSuper));
			Desejos.SuperSoComOsDeleDeTeste = true;
			string[] soOsDele;
			try { soOsDele = [.. Desejos.MenuDoSuper(false, false, 0).Select(d => d.Id)]; }
			finally { Desejos.SuperSoComOsDeleDeTeste = false; }
			Checa("   CONTRA-EXEMPLO (defeito injetado: o Super so com os cinco dele, o jogo de antes) -- a regua de cima REPROVA",
				  !listaInteira(soOsDele), string.Join(",", soOsDele));
			Checa("...e o 'Super Saiyajin' do Super segue o sangue de quem pede, como no Shenron",
				  !doSuper.Contains("super_saiyajin") && Desejos.MenuDoSuper(false, false, 100).Any(d => d.Id == "super_saiyajin"));

			DesejoDef DefDe(string id) => Desejos.Todos.Concat(Desejos.DoSuper).First(d => d.Id == id);
			double zeniDoTeste = pl.Ficha.Zeni;
			ConcederDoSuper(pl, pl, DefDe("sdb_riqueza"), "");
			double riquezaDoSuper = pl.Ficha.Zeni - zeniDoTeste;
			pl.Ficha.Zeni = zeniDoTeste;
			Checa("**A RIQUEZA DO SUPER VALE 4 VEZES A DAS BLACK STAR**: 1 bilhao de zeni (as Black Star dao 250 milhoes, o Shenron 50)",
				  Math.Abs(riquezaDoSuper - 1e9) < 1 && Math.Abs(riquezaDoSuper - 4 * EsferasNegras.MultiplicadorDaBlackStar * Desejos.ZeniDoDesejo) < 1,
				  $"{riquezaDoSuper:N0}");

			double techDoTeste = pl.Ficha.techmod;
			double IntelDo(Func<bool> pedir) { pl.Ficha.techmod = techDoTeste; pedir(); double d = pl.Ficha.techmod - techDoTeste; pl.Ficha.techmod = techDoTeste; return d; }
			double intelSuper = IntelDo(() => ConcederDoSuper(pl, pl, DefDe("inteligencia"), ""));
			double intelNegra = IntelDo(() => DesejoDeInteligencia(pl, _setNegro));
			Checa("**A INTELIGENCIA DO SUPER (UM DESEJO DO SHENRON QUE ELE NAO TINHA) VALE 4 VEZES A DAS BLACK STAR**: +40 (as Black Star dao +10, o "
				  + "Shenron +2)", Math.Abs(intelSuper - 40) < 1e-9 && Math.Abs(intelSuper - 4 * intelNegra) < 1e-9, $"super {intelSuper}, negra {intelNegra}");
			double intelSemFator;
			SuperSemMultiplicadorDeTeste = true;
			try { intelSemFator = IntelDo(() => ConcederDoSuper(pl, pl, DefDe("inteligencia"), "")); }
			finally { SuperSemMultiplicadorDeTeste = false; }
			Checa("   CONTRA-EXEMPLO (defeito injetado: o Super com os numeros do Shenron): +2 -- a regua de cima REPROVA",
				  Math.Abs(intelSemFator - 40) > 1e-9, $"{intelSemFator}");

			// os marcos: UMA vez por vida (`wishedpoints`) -- zerados entre um dragao e outro, e devolvidos no fim
			int livresDoTeste = pl.Livro?.MarcosLivres ?? 0;
			var pedidosDoTeste = pl.Ficha.wishedpoints;
			double MarcosDo(Func<bool> pedir) { pl.Ficha.wishedpoints = 0; pedir(); double m = pl.Ficha.wishedpoints; pl.Ficha.wishedpoints = 0; return m; }
			double marcosSuper = MarcosDo(() => ConcederDoSuper(pl, pl, DefDe("marcos"), ""));
			double marcosNegra = MarcosDo(() => DesejoDeMarcos(pl, _setNegro));
			double marcosShenron = MarcosDo(() => DesejoDeMarcos(pl, fraco));
			pl.Ficha.wishedpoints = pedidosDoTeste;
			if (pl.Livro != null) pl.Livro.MarcosLivres = livresDoTeste;
			Checa("**OS MARCOS DO SUPER SAO 15** (*\"ele daria 15 marcos pra n ficar muito broken\"*), e nao os 40 do fator: as Black Star dao 10, "
				  + "o Shenron 2", marcosSuper == Desejos.MarcosDoSuper && marcosSuper == 15 && marcosNegra == 10 && marcosShenron == 2,
				  $"super {marcosSuper}, negra {marcosNegra}, shenron {marcosShenron}");

			// a janela: os numeros DESTE dragao (antes as Black Star diziam "50.000.000" e davam 250 milhoes)
			string Texto(string id, SetDeEsferas s) => Desejos.DescricaoNoDragao(DefDe(id), FatorDoDragao(s), MarcosNoDragao(s), false);
			bool numerosCertos() => Texto("dinheiro", _setNegro).Contains("250.000.000") && Texto("dinheiro", fraco).Contains("50.000.000")
									// ...e a LINHA da janela, pelo construtor de producao (e nao so a funcao do texto)
									&& EntradaDoDesejo(pl, _setNegro, DefDe("dinheiro"), posso: true, juntas: false).Desc.Contains("250.000.000")
									&& Texto("marcos", SetDoSuper).StartsWith("15 Marcos", StringComparison.Ordinal)
									&& Texto("marcos", _setNegro).StartsWith("10 Marcos", StringComparison.Ordinal)
									&& Texto("inteligencia", SetDoSuper).Contains("+40", StringComparison.Ordinal);
			Checa("**A JANELA DIZ OS NUMEROS DE CADA DRAGAO**: a riqueza das Black Star em 250.000.000, a do Shenron em 50.000.000, os marcos do "
				  + "Super em 15 e os das Black Star em 10, a inteligencia do Super em +40", numerosCertos(),
				  $"{Texto("dinheiro", _setNegro)} | {Texto("marcos", SetDoSuper)} | {Texto("inteligencia", SetDoSuper)}");
			bool numerosComDefeito;
			Desejos.DescricaoFixaDeTeste = true;
			try { numerosComDefeito = numerosCertos(); }
			finally { Desejos.DescricaoFixaDeTeste = false; }
			Checa("   CONTRA-EXEMPLO (defeito injetado: o texto fixo, o jogo de antes) -- a regua de cima REPROVA", !numerosComDefeito);

			// o nome: o set desta bancada foi erguido na Terra
			Checa("**O DRAGAO DA TERRA SE CHAMA SHENLONG** (*\"shenlong e so o dragao da terra\"*): o set erguido aqui nasce com esse nome",
				  set.NomeDoDragao == Esferas.NomeDoShenlong && Esferas.NomeDoShenlong == "SHENLONG", set.NomeDoDragao);

			// =====================================================================
			// (2026-10-21) O NAMEKUSEIJIN QUE JA LIBEROU O POTENCIAL PEDE DE NOVO -- regra do dono
			// =====================================================================
			// *"namek q ja teve potencial liberado, pode pedir pro shenlong liberar o potencial, mas ele n vai ganhar o potential unlocked, ele
			// so vai ganhar o mistico/potential unleashed se ele n tiver e a forma orange"*. O poder do ki divino continua sendo o portao do
			// Namekuseijin (a regra da v0.3.0): este tem o dobro dele.
			ServerPlayer doGuru = Forjar("bancada: o namek do Guru", pl.Pos, terra, "bancada_desejo_g");
			forjados.Add(doGuru);
			doGuru.Race = "Namekian"; doGuru.Ficha.Race = "Namekian";
			doGuru.Ficha.unlockPotential = 1;                    // o Guru ja o despertou
			doGuru.Ficha.hiddenpotential = 0;                    // (2026-10-26) e o banco esta vazio: o desejo da so as formas
			doGuru.Ficha.BP = Math.Max(doGuru.Ficha.BP, RequisitoDeGodKi(doGuru) * 2);
			doGuru.Ficha.Statify();
			double bpDoGuru = doGuru.Ficha.BP;
			bool podiaPedir = PorQueNaoLiberaOPotencial(doGuru) == null;
			bool liberou = DesejoDePotencial(doGuru, set);
			Checa("**O NAMEKUSEIJIN QUE JA LIBEROU O POTENCIAL PODE PEDIR DE NOVO** (*\"ele so vai ganhar o mistico ... e a forma orange\"*): ganha o "
				  + "Mistico e o Orange, e o potencial NAO desperta outra vez (o BP fica onde estava)",
				  podiaPedir && liberou && doGuru.Forma.Despertou(Jandirus.Core.Forms.Catalogo.IdMistico)
				  && doGuru.Forma.Despertou(Jandirus.Core.Forms.Catalogo.IdOrange) && doGuru.Ficha.unlockPotential == 1
				  && Math.Abs(doGuru.Ficha.BP - bpDoGuru) < 1e-6,
				  $"podia {podiaPedir}, liberou {liberou}, potencial {doGuru.Ficha.unlockPotential}, BP {bpDoGuru:N0} -> {doGuru.Ficha.BP:N0}");
			Checa("...e com os tres na mao o dragao nao tem mais o que dar: a linha apaga, e o pedido nao gasta",
				  PorQueNaoLiberaOPotencial(doGuru) != null && !DesejoDePotencial(doGuru, set));
			// (2026-10-26) O DESEJO SOLTA O BANCO 1,5x, quantas vezes houver banco (o dono, o pedido 95) -- sem a hora de recarga do Guru
			doGuru.Ficha.hiddenpotential = 1000;
			double bpComBanco = doGuru.Ficha.BP;
			bool podiaComBanco = PorQueNaoLiberaOPotencial(doGuru) == null;
			bool soltou = DesejoDePotencial(doGuru, set);
			Checa("**COM POTENCIAL GUARDADO O DESEJO SOLTA O BANCO 1,5x** (o dono: \"desejo libera 1.5x\"): mil guardados, +1.500 de BP base, o banco zera -- e a linha acende de novo pra quem ja tem tudo",
				  podiaComBanco && soltou && Math.Abs(doGuru.Ficha.BP - (bpComBanco + 1500)) < 1e-6 && doGuru.Ficha.hiddenpotential == 0,
				  $"podia {podiaComBanco}, soltou {soltou}, BP {bpComBanco:N0} -> {doGuru.Ficha.BP:N0}");

			// =====================================================================
			// (2026-10-22) O MENU DA ESTATUA -- regra do dono
			// =====================================================================
			// *"e ao criar um set de esferas crie um menu bem intuitivo e bem feito pro jogador q criou o set escolher os desejos, a quantidade, a
			// força etc"* -- e, perguntado: o nome escolhido fora da Terra e de Namek, a FORCA DIVIDIDA (1 pedido = 100%, 2 = 50% cada, 3 = 33%)
			// e a lista LIGA E DESLIGA. A janela (`Client/JanelaDaEstatua.cs`) so monta o texto do verbo; aqui e o verbo de producao, num
			// mundo que o testador domina (Vegeta, com a bandeira posta pela bancada).
			var vegeta = ZoneKey.Premade("Vegeta");
			_sets.RemoveAll(s => !s.Eterno && s.Zona.Hash == vegeta.Hash);
			_esferas.RemoveAll(e => !_sets.Any(s => s.Id == e.Set));
			MoveToZone(pl.Id, vegeta, PontoDeNascimento(vegeta));
			var dominioDaBancada = new Dominio { PreFeito = true, Planeta = "Vegeta", Assinatura = pl.Assinatura, Conta = pl.Conta, Nome = pl.Name };
			_dominios.Add(dominioDaBancada);
			try
			{
				SetDeEsferas? DeVegeta() => _sets.Find(s => !s.Eterno && s.Zona.Hash == vegeta.Hash);

				// ---- as recusas ANTES de erguer: nada sobe e nada se cobra
				pl.Ficha.Zeni = Desejos.PrecoDoSupremo - 1;
				var semBolso = new ConfigDaEstatua { Pedidos = 1, Supremo = ConfigDaEstatua.SupremoEmZeni };
				List<string> nomeRuim = Ouvir(() => ErguerEstatua(pl, new ConfigDaEstatua { Nome = "A", Pedidos = 1 }.Codificar()));
				var tudoDesligado = new ConfigDaEstatua { Pedidos = 1 };
				foreach (DesejoDef d in ConfigDaEstatua.Ajustaveis) tudoDesligado.Desligados.Add(d.Id);
				List<string> semDesejo = Ouvir(() => ErguerEstatua(pl, tudoDesligado.Codificar()));
				List<string> semDinheiro = Ouvir(() => ErguerEstatua(pl, semBolso.Codificar()));
				Checa("**O MENU DA ESTATUA RECUSA ANTES DE ERGUER**: o nome de uma letra so, o dragao sem desejo nenhum ligado e o supremo marcado sem "
					  + "dinheiro pra ele -- nada sobe, e nada se cobra",
					  DeVegeta() == null && Disse(nomeRuim, "pelo menos 2 letras") && Disse(semDesejo, "pelo menos um desejo")
					  && Disse(semDinheiro, "não subiu") && Math.Abs(pl.Ficha.Zeni - (Desejos.PrecoDoSupremo - 1)) < 1e-6,
					  $"{Ultimos(nomeRuim)} | {Ultimos(semDesejo)} | {Ultimos(semDinheiro)}");
				SupremoSemConferirOBolsoDeTeste = true;
				try { Ouvir(() => ErguerEstatua(pl, semBolso.Codificar())); }
				finally { SupremoSemConferirOBolsoDeTeste = false; }
				Checa("   CONTRA-EXEMPLO (defeito injetado: o menu nao confere o bolso do supremo) -- a estatua sobe SEM o desejo que ele marcou, e a "
					  + "regua de cima REPROVA", DeVegeta() is { TemSupremo: false }, $"{DeVegeta()?.TemSupremo}");
				_sets.RemoveAll(s => !s.Eterno && s.Zona.Hash == vegeta.Hash);
				_esferas.RemoveAll(e => !_sets.Any(s => s.Id == e.Set));

				// ---- erguer pelo menu: o nome, 2 pedidos, o Matar desligado, o supremo em zeni
				pl.Ficha.Zeni = 3 * Desejos.PrecoDoSupremo;
				var escolhida = new ConfigDaEstatua { Nome = "  mega   shenron ", Pedidos = 2, Supremo = ConfigDaEstatua.SupremoEmZeni };
				escolhida.Desligados.Add("matar");
				List<string> ergueu = Ouvir(() => ErguerEstatua(pl, escolhida.Codificar()));
				SetDeEsferas? doMenu = DeVegeta();
				Checa("**O MENU DA ESTATUA ERGUE O SET QUE O CRIADOR ESCOLHEU** (*\"o player q criar o set de esferas escolhe o nome\"*): MEGA SHENRON "
					  + "(o nome limpo, em maiusculas), 2 pedidos, o \"Matar alguém\" desligado e o supremo gravado -- e os 2.000.000 de zeni cobrados",
					  doMenu is { NomeDoDragao: "MEGA SHENRON", Desejos: 2, TemSupremo: true } && doMenu.Desligados.SequenceEqual(["matar"])
					  && Math.Abs(pl.Ficha.Zeni - 2 * Desejos.PrecoDoSupremo) < 1e-6 && _esferas.Count(e => e.Set == doMenu.Id) == Esferas.Total,
					  $"set {doMenu?.NomeDoDragao}/{doMenu?.Desejos}/{string.Join(",", doMenu?.Desligados ?? [])}/{doMenu?.TemSupremo}; zeni {pl.Ficha.Zeni:N0}; "
					  + Ultimos(ergueu));
				if (doMenu == null) throw new InvalidOperationException("sem o set do menu: o resto da familia nao roda");

				// ---- a forca dividida: o fator, a riqueza, os marcos, o potencial e a frase da janela
				double zeniAntesDaRiqueza = pl.Ficha.Zeni;
				DesejoDeDinheiro(pl, doMenu);
				double riquezaDividida = pl.Ficha.Zeni - zeniAntesDaRiqueza;
				SetDeEsferas? eterno = _sets.Find(s => s.Eterno);
				bool eternoInteiro = eterno == null || Math.Abs(FatorDoDragao(eterno) - 1) < 1e-9;
				Checa("**A FORCA DIVIDIDA** (*\"1 pedido = 100%, 2 = 50% cada, 3 = 33% cada\"*, a escolha do dono): no set de 2 pedidos o fator e 1/2 e "
					  + "a riqueza 25.000.000 -- e o Porunga ancestral, que nao e de jogador, continua com a forca inteira",
					  Math.Abs(FatorDoDragao(doMenu) - 0.5) < 1e-9 && Math.Abs(riquezaDividida - Desejos.ZeniDoDesejo / 2) < 1 && eternoInteiro,
					  $"fator {FatorDoDragao(doMenu)}; riqueza {riquezaDividida:N0}; eterno {(eterno == null ? "-" : FatorDoDragao(eterno).ToString())}");
				double riquezaInteira;
				ForcaInteiraDeTeste = true;
				try
				{
					double z = pl.Ficha.Zeni;
					DesejoDeDinheiro(pl, doMenu);
					riquezaInteira = pl.Ficha.Zeni - z;
				}
				finally { ForcaInteiraDeTeste = false; }
				Checa("   CONTRA-EXEMPLO (defeito injetado: a forca inteira em cada pedido, o jogo de antes) -- 50.000.000, e a regua de cima REPROVA",
					  Math.Abs(riquezaInteira - Desejos.ZeniDoDesejo) < 1, $"{riquezaInteira:N0}");
				Checa("...e o potencial usa a forca de UM pedido (a do set pela metade), e a janela diz os numeros divididos: 25.000.000, 1 Marco, 12,5% do teto",
					  Math.Abs(PoderDeCadaPedido(doMenu) - doMenu.Poder / 2) < 1e-6 && MarcosNoDragao(doMenu) == 1
					  && EntradaDoDesejo(pl, doMenu, DefDe("dinheiro"), posso: true, juntas: false).Desc.Contains("25.000.000")
					  && Desejos.DescricaoNoDragao(DefDe("poder"), FatorDoDragao(doMenu), MarcosNoDragao(doMenu), true).Contains("12,5%"),
					  $"poder de um pedido {PoderDeCadaPedido(doMenu):N0} de {doMenu.Poder:N0}; marcos {MarcosNoDragao(doMenu)}");

				// ---- o desligado: fora do menu, e o pedido por verbo recusado sem gastar
				doMenu.AtivoEm = TempoDoMundo - 1;
				PorODragaoDePe(pl, doMenu);
				int pedidosAntesDoMatar = doMenu.Pedidos;
				ServerPlayer vitima = Forjar("bancada: a vitima do desligado", pl.Pos, vegeta, "bancada_desejo_v");
				forjados.Add(vitima);
				PedirDesejo(pl, "matar " + vitima.Name);
				Checa("**O DESEJO DESLIGADO NAO ESTA NO DRAGAO**: o \"Matar alguém\" fora do menu dele -- e pedido por verbo, recusado sem gastar pedido",
					  _invocacoes.ContainsKey(doMenu.Id) && !MenuDaqui(pl, doMenu).Any(d => d.Id == "matar") && MenuDaqui(pl, doMenu).Any(d => d.Id == "dinheiro")
					  && doMenu.Pedidos == pedidosAntesDoMatar && !vitima.Ficha.dead,
					  $"dragao de pe {_invocacoes.ContainsKey(doMenu.Id)}; pedidos {pedidosAntesDoMatar} -> {doMenu.Pedidos}; vitima morta {vitima.Ficha.dead}");
				bool matarNoMenu;
				DesligadosIgnoradosDeTeste = true;
				try { matarNoMenu = MenuDaqui(pl, doMenu).Any(d => d.Id == "matar"); }
				finally { DesligadosIgnoradosDeTeste = false; }
				Checa("   CONTRA-EXEMPLO (defeito injetado: o dragao ignora o desligado) -- o Matar volta ao menu, e a regua de cima REPROVA", matarNoMenu);
				FecharAInvocacao(doMenu.Id, "");

				// ---- refazer pelo menu: outro nome, 1 pedido, o Matar de volta -- e o supremo fica (ele nao se compra nem se perde aqui)
				var refeita = new ConfigDaEstatua { Nome = "Shenron Dourado", Pedidos = 1, Supremo = ConfigDaEstatua.SupremoEmZeni };
				double zeniAntesDoRefazer = pl.Ficha.Zeni;
				List<string> refez = Ouvir(() => RefazerOSet(pl, refeita.Codificar()));
				Checa("**REFAZER PELO MENU** (o \"Configurar o dragão\"): SHENRON DOURADO, 1 pedido, nenhum desligado -- o supremo continua gravado e "
					  + "nao se cobra de novo (ele so se compra ao erguer)",
					  doMenu is { NomeDoDragao: "SHENRON DOURADO", Desejos: 1, TemSupremo: true } && doMenu.Desligados.Count == 0
					  && Math.Abs(pl.Ficha.Zeni - zeniAntesDoRefazer) < 1e-6,
					  $"{doMenu.NomeDoDragao}/{doMenu.Desejos}/{string.Join(",", doMenu.Desligados)}/{doMenu.TemSupremo}; zeni {zeniAntesDoRefazer:N0} -> "
					  + $"{pl.Ficha.Zeni:N0}; {Ultimos(refez)}");

				// ---- o disco: o nome e os desligados atravessam o arquivo
				var paraODisco = new ConfigDaEstatua { Nome = "Shenron Dourado", Pedidos = 3 };
				paraODisco.Desligados.Add("tecnologia");
				Ouvir(() => RefazerOSet(pl, paraODisco.Codificar()));
				SalvarEsferas();
				LivroDasEsferas? lido = System.Text.Json.JsonSerializer.Deserialize<LivroDasEsferas>(System.IO.File.ReadAllText(CaminhoDasEsferas),
					new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
				SetDeEsferas? doDisco = lido?.Sets.Find(s => s.Id == doMenu.Id);
				Checa("...e O DISCO GUARDA A CONFIGURACAO: o nome, os 3 pedidos e o desejo desligado voltam do esferas.json",
					  doDisco is { NomeDoDragao: "SHENRON DOURADO", Desejos: 3 } && doDisco.Desligados.SequenceEqual(["tecnologia"]),
					  $"{doDisco?.NomeDoDragao}/{doDisco?.Desejos}/{string.Join(",", doDisco?.Desligados ?? [])}");

				Checa("...e o nome so e livre fora da Terra e de Namek: o Shenlong e o Porunga nao mudam pelo menu",
					  ConfigDaEstatua.NomeNoPlaneta("Earth", "Outro") == Esferas.NomeDoShenlong && ConfigDaEstatua.NomeNoPlaneta("Namek", "Outro") == "PORUNGA");
			}
			finally
			{
				_dominios.Remove(dominioDaBancada);
				SupremoSemConferirOBolsoDeTeste = ForcaInteiraDeTeste = DesligadosIgnoradosDeTeste = false;
			}
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
			SuperShenronDeQualquerLugarDeTeste = false;   // o palco da secao 10
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
