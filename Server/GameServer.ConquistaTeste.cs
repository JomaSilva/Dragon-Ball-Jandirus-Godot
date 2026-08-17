using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA CONQUISTA DE PLANETAS (`--conquistateste`).
///
/// ============================ O QUE SO DAQUI SE RESPONDE ============================
///   1. **A CHAVE E A DO JOGO?** A bancada VARRE o universo ate achar dois mundos gerados com o
///      MESMO NOME -- eles existem, e e por isso que o nome nao serve -- e afirma que a
///      `ChaveDePlaneta` os separa. Um teste que so fincasse uma bandeira passaria com a chave errada.
///   2. **A INVASAO ACONTECE?** Ondas nascem, o defensor ENGAJA (cidadao nao caca ninguem: sem o
///      `MarcarAgressao` a onda ficaria parada olhando), o LIDER entra na ultima, o BP escala com o
///      expresso do invasor e respeita o piso.
///   3. **NOCAUTEAR BASTA, MATAR CUSTA.** As duas metades: a onda cai por KO sem uma morte, e matar
///      um defensor nativo tira reputacao (e o lider tira mais).
///   4. **A CONTESTACAO FUNCIONA?** O canal de 8 s cai por MOVIMENTO, cai por DANO, e quando fecha
///      derruba a invasao e paga quem defendeu. Sem as tres, "existe contestacao" seria promessa.
///   5. **MANTER CUSTA -- E O TETO DISPARA.** Esta regra **nao existe no DM** e por isso e a que mais
///      precisa provar: a carencia de 24 h NAO cobra, a hora seguinte cobra, o tributo mingua junto,
///      a guarnicao de um dominio esquecido enfraquece, e zerada a lealdade o dominio SE PERDE.
///      Corolario 0.7 na veia -- um decaimento que nunca zera e indistinguivel de decaimento nenhum.
///   6. **CONQUISTA E DESTRUICAO CONVIVEM?** Um mundo com dono e destruido de verdade (pelo sistema
///      da outra sessao, nao por um atalho daqui) e o dominio cai junto, com recado pro ex-dono.
///   7. **A API PRAS QUESTS DE CARGO ESTA VIVA?** `Cargos.ExigirDominios` reprova com zero dominios e
///      passa com um, lida da `FichaDeRank` de PRODUCAO -- e as quatro pendencias de reputacao que
///      diziam "o port nao tem reputacao de planeta" viraram requisito que cobra.
/// ================================================================================
///
/// ============================ AS SETE FAMILIAS COM DEFEITO INJETADO ============================
/// As checagens de 1 a 10 acima afirmam. As familias abaixo provam que aquelas afirmacoes **sabem
/// ficar vermelhas**: cada uma mede o codigo de producao com um criterio nomeado, injeta um defeito
/// de verdade, exige que o MESMO criterio reprove, desfaz e exige que ele volte a passar. E o
/// `Mutacao` da `--provateste`, reusado -- ver o porque de ele ser funcao e nao receita escrita.
///
/// Um teto que nunca e atingido e indistinguivel de teto nenhum (corolario 0.7); uma checagem que
/// nunca soube ficar vermelha e indistinguivel de checagem nenhuma, e foi essa a lacuna aqui.
///
///   A. **MUNDO GERADO** -> reprova se o verbo nao conquistar um planeta SORTEADO (nao so um
///      pre-feito), se o plantio de 60 s for instantaneo, ou se dominar um mundo entregar o
///      HOMONIMO junto. Defeito injetado: o dominio reindexado pela seed do gemeo -- que e
///      literalmente o que "indexar por nome" (o `conq_data["Planeta"]` do DM) faz.
///   B. **OS DEFENSORES LUTAM** -> reprova se a onda nascer e ficar parada. O criterio nao mede
///      intencao, mede PANCADA: o `UltimoAgressor` do invasor so e escrito pelo golpe que ACERTA.
///      Defeito injetado: o rancor da onda apagado -- o cidadao volta a ser "pacifico ate apanhar".
///   C. **MANTER CUSTA** -> reprova se a ausencia nao cobrar, e reprova **atravessando um
///      REINICIO**, que era o buraco: o passo da cobranca vinha de um campo de memoria, entao o
///      tempo com o servidor DESLIGADO nunca era cobrado e bastava reiniciar pra manter o mundo.
///      Defeito injetado: o carimbo da ultima cobranca de volta pra fora do disco.
///   D. **CONTESTACAO NOS DOIS SENTIDOS** -> o sentido que faltava: o contestador FALHA e o
///      invasor leva o planeta. Defeito injetado: o retrato do rancor re-lido a cada tique em vez
///      de comparado -- e o canal deixa de cair no golpe.
///   E. **TRIBUTO E FAMA** -> reprova se o tributo nao cair no bolso, se o teto de 24 h nao
///      disparar, se coletar duas vezes pagar duas vezes, e se matar o LIDER custar o mesmo que
///      matar um soldado. Defeitos injetados: o relogio da coleta nao rearmado; o lider rebaixado.
///   F. **SOBREVIVE AO REINICIO** -> ida e volta pelo `conquista.json` de verdade. Reprova se o
///      dominio nao voltar, voltar sem a chave, sem a lealdade ou sem a escolha de renascimento.
///      Defeito injetado: o save mutilado (a SEED zerada), que e o campo que carrega a identidade.
///   G. **CONQUISTA x DESTRUICAO** -> as duas perguntas de convivencia. Reprova se um mundo MORTO
///      puder ser conquistado, e se um dominio sobreviver ao planeta. Defeito injetado: o planeta
///      ressuscitado -- prova que quem responde "morreu?" e o livro dos mortos e mais ninguem.
/// ==========================================================================================
///
///     Godot --headless --path . --host --rede 7975 --conta bancada_conq --senha teste
///            --nome ConqBanca --raca Saiyan --conquistateste
/// </summary>
public partial class GameServer
{
	private bool _conquistaDeTeste;

	/// <summary>Roda uma vez, no primeiro login. MEXE no mundo e no disco -- so com a flag.</summary>
	private void RodarBancadaDaConquista(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA DA CONQUISTA DE PLANETAS =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// ============================ O MUNDO VOLTA COMO ESTAVA ============================
		// Ela finca dominios, poe defensores no mundo, paga e cobra reputacao, adianta o relogio do
		// ceu e DESTROI um planeta gerado. Tudo e fotografado aqui e devolvido no `finally` -- senao
		// quem rodar a bancada uma vez fica dono da Terra pra sempre.
		// ==============================================================================
		var dominiosGuardados = new List<Dominio>(_dominios);
		double ceuGuardado = _adiantoDoCeu, mundoGuardado = _relogioDoMundo;
		double bpGuardado = pl.Ficha.BP, zeniGuardado = pl.Ficha.Zeni;
		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		double repTerraGuardada = ReputacaoDe("Earth", pl.Conta);
		double repVegetaGuardada = ReputacaoDe("Vegeta", pl.Conta);

		// LISTA e nao um campo: as familias novas matam um SEGUNDO mundo (a G), e um campo so
		// devolveria o ultimo -- o primeiro ficaria morto no disco depois da bancada.
		var mundosMortos = new List<ZoneKey>();

		// ============================ E O LIVRO DOS MORTOS NAO CHEGA AO DISCO ============================
		// A lista acima devolve os mundos que ESTA bancada sabe que matou, e ela continua sendo a
		// afirmacao do escopo dela ("eu limpo o que eu sujo"). O que ela nao cobre e a JANELA: entre o
		// `ComecarDestruicao` e o `finally` o `planetas-mortos.json` do dono ja tem o mundo morto
		// gravado, e um Ctrl+C no meio o deixa assim -- pra sempre, porque mundo gerado e chaveado por
		// SEMENTE e volta identico todo boot. O palco fecha a janela: nada e escrito enquanto a bancada
		// roda. Ele nasceu da mesma janela deixada aberta na `--escudoteste`, que custou a Terra do dono.
		// ==========================================================================================
		using PalcoDeMortes palcoDeMortes = PalcoDeMortesDeBancada();

		try
		{
			// =====================================================================
			// 1. A CHAVE: o nome COLIDE, a chave NAO
			// =====================================================================
			// A varredura e o que faz esta checagem valer: sem ela seria uma afirmacao sobre um caso
			// hipotetico. `SistemaSolar.Planeta` monta o nome com `|Sx| % 1000` e `|Sy| % 1000`
			// concatenados -- perde o SINAL e e ambiguo --, entao homonimos existem de verdade e
			// perto de casa.
			bool temGemeos = AcharHomonimosGerados(out PlanetaNoEspaco gemeoA, out PlanetaNoEspaco gemeoB,
												   out int varridos);

			Checa($"existem dois mundos gerados com o MESMO nome ('{(temGemeos ? gemeoA.Nome : "-")}') -- por isso "
				+ "o nome nao pode ser chave de dominio", temGemeos,
				  $"varri {varridos} mundos em 41x41 celulas sem achar colisao");

			if (temGemeos)
				Checa("...e a ChaveDePlaneta separa os dois",
					  ChaveDePlaneta.De(gemeoA) != ChaveDePlaneta.De(gemeoB),
					  $"{ChaveDePlaneta.De(gemeoA).Texto} vs {ChaveDePlaneta.De(gemeoB).Texto}");

			// =====================================================================
			// 2. AS PORTAS DO VERB
			// =====================================================================
			// A TERRA E O CAMPO DE PROVA: e o unico pre-feito com povo, banco e chao conhecido, e o
			// plano de povoamento a lista -- ou seja, `TemPovo` responde verdadeiro por DADO e nao
			// porque a bancada mandou.
			var terra = ZoneKey.Premade("Earth");
			Checa("a Terra tem povo (e o plano do npcs.json que diz, nao esta bancada)", TemPovo("Earth"));
			Checa("um mundo gerado NAO tem povo", !TemPovo("Deserto-000"));

			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));

			// FRACO DEMAIS: a primeira porta. `expressedBP` de um personagem comum nao chega perto de
			// 5.000.000 -- ver a armadilha 6 da PARTE 3, conferida no cabecalho do `Conquista`.
			pl.Ficha.BP = 1000;
			pl.Ficha.Statify();
			pl.Ficha.PowerLevel();
			int dominiosAntes = _dominios.Count;
			EscutaDeAvisos = [];
			ConquistaInvadir(pl, "");
			Checa("um fraco e RECUSADO na Terra (poder minimo de 5.000.000 expresso)",
				  _dominios.Count == dominiosAntes && _invasoes.Count == 0
				  && EscutaDeAvisos.Exists(t => t.Contains("fraco demais")),
				  string.Join(" | ", EscutaDeAvisos));

			// =====================================================================
			// 3. A REIVINDICACAO PACIFICA -- a porta que a reputacao abre
			// =====================================================================
			pl.Ficha.BP = 50_000_000;
			pl.Ficha.Statify();
			pl.Ficha.PowerLevel();
			Checa($"o invasor de teste expressa {pl.Ficha.expressedBP:N0} -- acima do minimo",
				  pl.Ficha.expressedBP >= Conquista.BpMinimoComPovo);

			// SEM ser heroi, o verb NAO reivindica em paz: ele invade. A metade de controle da regra
			// -- sem ela, "heroi reivindica em paz" passaria com a paz valendo pra todo mundo.
			SomarReputacao("Earth", pl, -ReputacaoDe("Earth", pl.Conta), "bancada-zera");
			ConquistaInvadir(pl, "");
			Checa("quem NAO e heroi do povo nao ganha o planeta de presente -- comeca uma INVASAO",
				  _invasoes.Count == 1 && _dominios.Count == dominiosAntes,
				  $"{_invasoes.Count} invasao(oes), {_dominios.Count} dominio(s)");

			Invasao inv = _invasoes.Values.First();
			Checa("...com guarnicao NATIVA (o planeta nao tem dono) e a defesa do proprio povo",
				  inv.Nativa && inv.TemPovo);

			// =====================================================================
			// 4. AS ONDAS
			// =====================================================================
			_relogioDoMundo += Conquista.SegundosDeTensao + 0.5;
			TickDasInvasoes();

			Checa($"a onda 1 nasceu com {Conquista.OndasComPovo[0].Quantos} defensores",
				  inv.Defensores.Count == Conquista.OndasComPovo[0].Quantos,
				  $"nasceram {inv.Defensores.Count}");
			Checa("...e nenhum deles e o LIDER (o lider so entra na ultima onda)",
				  !inv.Defensores.Values.Any(l => l));

			ServerPlayer primeiro = _players[inv.Defensores.Keys.First()];
			double esperado = Math.Max(pl.Ficha.expressedBP * Conquista.OndasComPovo[0].Fator,
									   Conquista.PisoDeBpDeDefensor);
			Checa($"o BP do defensor foi PINADO na conta da onda ({esperado:N0})",
				  Math.Abs(primeiro.Ficha.BP - esperado) < 1,
				  $"a ficha dele diz {primeiro.Ficha.BP:N0}");

			// O ENGAJAMENTO. Esta e a checagem que o port precisa e o DM nao: la o defensor e um NPC
			// que caca; aqui ele e um CIDADAO, que so briga com quem bateu nele.
			Checa("o defensor ENGAJOU o invasor (cidadao nao caca ninguem: sem isto a onda ficaria parada)",
				  primeiro.UltimoAgressor == pl.Id && NowMs() < primeiro.RancorAte);

			// O RE-ENGAJAMENTO: o rancor do cidadao dura 90 s e uma onda pode durar mais.
			primeiro.RancorAte = NowMs() - 1;
			TickDasInvasoes();
			Checa("...e o rancor VENCIDO e renovado no tique (o defensor nao volta a ser habitante)",
				  NowMs() < primeiro.RancorAte);

			// NOCAUTEAR BASTA. Nenhuma morte nesta onda -- e a reputacao tem que ficar intacta.
			double repAntesDoKo = ReputacaoDe("Earth", pl.Conta);
			foreach (int id in inv.Defensores.Keys.ToList()) _players[id].Ficha.KO = true;
			TickDasInvasoes();

			Checa("a onda caiu SO com nocautes -- e a onda 2 esta no respiro",
				  inv.Onda == 1 && inv.Fase == FaseDaInvasao.Respiro,
				  $"onda {inv.Onda}, fase {inv.Fase}");
			Checa("...os nocauteados foram recolhidos do campo", inv.Defensores.Count == 0);
			Checa("...e NOCAUTEAR NAO CUSTOU REPUTACAO",
				  Math.Abs(ReputacaoDe("Earth", pl.Conta) - repAntesDoKo) < 1e-9,
				  $"{repAntesDoKo:0} -> {ReputacaoDe("Earth", pl.Conta):0}");

			// =====================================================================
			// 5. MATAR CUSTA -- e o LIDER custa mais
			// =====================================================================
			_relogioDoMundo += Conquista.SegundosDeRespiro + 0.5;
			TickDasInvasoes();   // onda 2
			_relogioDoMundo += 0.1;

			ServerPlayer vitima = _players[inv.Defensores.Keys.First()];
			double repAntes = ReputacaoDe("Earth", pl.Conta);

			// A SEQUENCIA E A DE PRODUCAO: o `TickCombate` chama `MorreuUmCorpoSemDono` e so depois
			// `RemoverNpc`. Chamar na ordem inversa mediria um corpo que ja saiu do `_players`.
			vitima.Ficha.dead = true;
			MorreuUmCorpoSemDono(vitima);
			RemoverNpc(vitima);

			Checa($"MATAR um defensor nativo custou {Conquista.RepPorMatarDefensor:0} de reputacao",
				  Math.Abs(ReputacaoDe("Earth", pl.Conta) - (repAntes + Conquista.RepPorMatarDefensor)) < 1e-9,
				  $"{repAntes:0} -> {ReputacaoDe("Earth", pl.Conta):0}");
			Checa("...e a invasao contou o morto", inv.Mortos == 1, $"{inv.Mortos}");

			// =====================================================================
			// 6. A CONTESTACAO
			// =====================================================================
			// Um terceiro corpo com ASSINATURA -- sem conta e slot ele nao e ninguem pro sistema, e a
			// recompensa de quem defende cai numa conta vazia.
			ServerPlayer heroi = ForjarContestador(inv.Bandeira);
			try
			{
				// (a) LONGE: recusa antes de abrir canal.
				heroi.Pos = inv.Bandeira + new Vec2(500, 0);
				EscutaDeAvisos = [];
				ConquistaArrancar(heroi);
				Checa("arrancar de LONGE e recusado", _arranques.Count == 0
					  && EscutaDeAvisos.Exists(t => t.Contains("mais perto")));

				// (b) MOVIMENTO cancela.
				heroi.Pos = inv.Bandeira;
				ConquistaArrancar(heroi);
				Checa("o canal de arranque abriu", _arranques.ContainsKey(heroi.Id));
				heroi.Pos = inv.Bandeira + new Vec2(64, 0);
				TickDoArranque(0.1);
				Checa("MOVER-SE derruba o arranque", _arranques.Count == 0 && !inv.Arrancada);

				// (c) DANO cancela. O canal e o `RancorAte`, escrito pelo funil `MarcarAgressao` --
				// tomar um golpe de verdade e exatamente isso.
				heroi.Pos = inv.Bandeira;
				ConquistaArrancar(heroi);
				MarcarAgressao(heroi, pl);
				TickDoArranque(0.1);
				Checa("levar DANO derruba o arranque", _arranques.Count == 0 && !inv.Arrancada);

				// (d) OITO SEGUNDOS PARADO E INTEIRO: arranca.
				heroi.Pos = inv.Bandeira;
				ConquistaArrancar(heroi);
				double repDoHeroiAntes = ReputacaoDe("Earth", heroi.Conta);
				for (int i = 0; i < 100 && _arranques.Count > 0; i++) TickDoArranque(0.1);

				Checa($"{Conquista.SegundosDeArranque:0}s parado e sem dano ARRANCAM a bandeira", inv.Arrancada);
				Checa($"...e quem defendeu o povo ganhou {Conquista.RepPorArrancarBandeira:0} de reputacao",
					  Math.Abs(ReputacaoDe("Earth", heroi.Conta)
							   - (repDoHeroiAntes + Conquista.RepPorArrancarBandeira)) < 1e-9);

				TickDasInvasoes();
				Checa("a invasao FRACASSOU com a bandeira arrancada, e ninguem virou dono",
					  _invasoes.Count == 0 && _dominios.Count == dominiosAntes);
				Checa("...e os defensores que sobraram sairam do mundo",
					  !_players.Values.Any(p => p.Name.StartsWith("Soldado ") || p.Name.StartsWith("General ")));
			}
			finally { if (_players.ContainsKey(heroi.Id)) RemoverNpc(heroi); }

			// =====================================================================
			// 7. O HEROI REIVINDICA EM PAZ
			// =====================================================================
			// A recarga pessoal (`conq_next_try`) e uma porta de verdade -- e a bancada mede que ela
			// DISPARA antes de desarma-la, senao ela seria um numero sem consequencia.
			EscutaDeAvisos = [];
			ConquistaInvadir(pl, "");
			Checa($"a recarga de {Conquista.SegundosEntreCampanhas / 60:0} min entre campanhas DISPARA",
				  _invasoes.Count == 0 && EscutaDeAvisos.Exists(t => t.Contains("se reorganizam")),
				  string.Join(" | ", EscutaDeAvisos));
			_proximaCampanha.Remove(pl.Assinatura);

			SomarReputacao("Earth", pl,
				Reputacao.LimiarDeHeroi - ReputacaoDe("Earth", pl.Conta), "bancada-vira-heroi");
			double repDeHeroi = ReputacaoDe("Earth", pl.Conta);

			ConquistaInvadir(pl, "");
			Checa("o HEROI do povo recebe o planeta SEM LUTA",
				  _invasoes.Count == 0 && _dominios.Count == dominiosAntes + 1,
				  $"{_invasoes.Count} invasao(oes), {_dominios.Count} dominio(s)");
			Checa($"...e a reivindicacao pacifica pagou {Conquista.RepPorReivindicarEmPaz:0} de reputacao",
				  Math.Abs(ReputacaoDe("Earth", pl.Conta)
						   - (repDeHeroi + Conquista.RepPorReivindicarEmPaz)) < 1e-9);

			Dominio dominio = _dominios.Last();
			Checa("o dominio guarda a CHAVE do planeta, e nao so o nome",
				  dominio.Chave == ChaveDePlaneta.Da(terra)!.Value, dominio.Chave.Texto);

			// O TETO DE DOMINIOS -- e ele dispara de verdade.
			//
			// EM VEGETA E NAO NA TERRA: a Terra ja e dele agora, e a guarda "ja e seu dominio" vem
			// ANTES do teto -- medindo ali, a bancada leria a recusa errada e chamaria de sucesso.
			// (Foi o que ela leu na primeira rodada; a ordem das guardas e a do DM e esta certa.)
			var vegeta = ZoneKey.Premade("Vegeta");
			MoveToZone(pl.Id, vegeta, PontoDeNascimento(vegeta));
			_proximaCampanha.Remove(pl.Assinatura);

			for (int i = 0; i < Conquista.MaximoDeDominios; i++)
				_dominios.Add(new Dominio { PreFeito = true, Planeta = $"bancada-{i}", Assinatura = pl.Assinatura });
			EscutaDeAvisos = [];
			ConquistaInvadir(pl, "");
			Checa($"o teto de {Conquista.MaximoDeDominios} dominios DISPARA",
				  EscutaDeAvisos.Exists(t => t.Contains("já rege")), string.Join(" | ", EscutaDeAvisos));
			_dominios.RemoveAll(d => d.Planeta.StartsWith("bancada-"));
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));

			// =====================================================================
			// 8. MANTER CUSTA
			// =====================================================================
			// O RELOGIO E ADIANTADO PELA MANIVELA DO CEU, que e a mesma que a bancada das sagas usa --
			// e por isso isto exercita o codigo de producao em vez de escrever na lealdade a mao.
			double lealdadeCheia = dominio.Lealdade;
			Checa("um dominio recem-fincado nasce leal", Math.Abs(lealdadeCheia - Conquista.LealdadeMaxima) < 1e-9);

			// (a) A CARENCIA NAO COBRA. Sem esta metade, "a lealdade cai" passaria com a carencia
			// apagada -- e o jogador perderia o planeta por ter dormido uma noite.
			MoveToZone(pl.Id, ZoneKey.Premade("Vegeta"), PontoDeNascimento(ZoneKey.Premade("Vegeta")));
			_ultimaCobrancaDeLealdade = 0;
			TickDaConquista();                                    // ancora
			_adiantoDoCeu += (Conquista.HorasDeCarencia - 1) * 3600;
			TickDaConquista();
			Checa($"{Conquista.HorasDeCarencia - 1:0} h de ausencia NAO cobram nada (a carencia e de "
				+ $"{Conquista.HorasDeCarencia:0} h)", Math.Abs(dominio.Lealdade - lealdadeCheia) < 1e-9,
				  $"lealdade {dominio.Lealdade:0.00}");

			// (b) DEPOIS DELA, COBRA.
			_adiantoDoCeu += 10 * 3600;
			TickDaConquista();
			double esperadaApos10h = lealdadeCheia - Conquista.QuedaDeLealdadePorHora * 9;
			Checa($"passada a carencia, a lealdade cai {Conquista.QuedaDeLealdadePorHora:0}/h",
				  Math.Abs(dominio.Lealdade - esperadaApos10h) < 0.01,
				  $"esperava {esperadaApos10h:0.00}, veio {dominio.Lealdade:0.00}");

			// (c) O TRIBUTO MINGUA JUNTO -- negligencia custa DINHEIRO antes de custar o planeta.
			double cheio = Conquista.TributoDevido(Conquista.HorasDeTetoDoTributo, true, Conquista.LealdadeMaxima);
			double cortado = Conquista.TributoDevido(Conquista.HorasDeTetoDoTributo, true, dominio.Lealdade);
			Checa("o tributo de um dominio negligenciado e MENOR", cortado < cheio,
				  $"{cortado:N0} contra {cheio:N0}");

			// (d) A GUARNICAO ENFRAQUECE -- a terceira consequencia, que amarra manter a contestacao.
			Checa("a guarnicao de um dominio esquecido luta pior",
				  Conquista.FatorDaGuarnicao(dominio.Lealdade) < Conquista.FatorDaGuarnicao(Conquista.LealdadeMaxima));

			// (e) A PRESENCA RECUPERA.
			double antesDaVisita = dominio.Lealdade;
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
			_adiantoDoCeu += 3600;
			TickDaConquista();
			Checa("estar no planeta RECUPERA a lealdade", dominio.Lealdade > antesDaVisita,
				  $"{antesDaVisita:0.00} -> {dominio.Lealdade:0.00}");

			// (f) ZEROU: O DOMINIO SE PERDE. Este e o teto que precisava disparar.
			MoveToZone(pl.Id, ZoneKey.Premade("Vegeta"), PontoDeNascimento(ZoneKey.Premade("Vegeta")));
			EscutaDeAvisos = [];
			_adiantoDoCeu += 200 * 3600;
			TickDaConquista();
			Checa("abandonado tempo bastante, o povo DERRUBA a bandeira e o dominio se perde",
				  !_dominios.Contains(dominio), $"lealdade {dominio.Lealdade:0.00}");
			Checa("...e o ex-dono foi avisado do motivo",
				  EscutaDeAvisos.Exists(t => t.Contains("PERDEU")), string.Join(" | ", EscutaDeAvisos));

			// =====================================================================
			// 9. CONQUISTA x DESTRUICAO -- os dois destinos do mesmo planeta
			// =====================================================================
			// UM MUNDO GERADO E VAZIO de proposito: destrui-lo de verdade nao mata ninguem, e o que se
			// mede e a INTEGRACAO (o livro dos mortos respondendo ao livro dos dominios), nao a explosao.
			if (AcharMundoGerado() is { } gerado)
			{
				var zonaGerada = ZoneKey.Procedural(gerado.Nome, gerado.Seed);
				mundosMortos.Add(zonaGerada);

				Dominio doGerado = FincarDominio(pl, gerado, new Vec2(0, 0));
				Checa($"da pra dominar um mundo gerado ({gerado.Nome})", _dominios.Contains(doGerado));
				Checa("...e o endereco no ceu foi resolvido (e o que poe o corpo em orbita ao renascer)",
					  doGerado.K >= 0, doGerado.Endereco);

				EscutaDeAvisos = [];
				bool acendeu = ComecarDestruicao(zonaGerada, 1e12, "bancada da conquista");
				Checa("o planeta comecou a morrer pelo sistema de DESTRUICAO (nao por um atalho daqui)",
					  acendeu && ZonaCondenada(zonaGerada));

				// Invadir um mundo que esta morrendo e recusado -- o DM nao tem este caso porque nao
				// tinha destruicao; aqui ele existe e e o que impede alguem de conquistar um cadaver.
				MoveToZone(pl.Id, zonaGerada, new Vec2(0, 0));
				_proximaCampanha.Remove(pl.Assinatura);
				EscutaDeAvisos = [];
				ConquistaInvadir(pl, "");
				Checa("nao se invade um planeta que esta morrendo",
					  EscutaDeAvisos.Exists(t => t.Contains("morrendo")), string.Join(" | ", EscutaDeAvisos));
				MoveToZone(pl.Id, terra, PontoDeNascimento(terra));

				// O PAVIO INTEIRO, pelo tique de producao.
				EscutaDeAvisos = [];
				for (int i = 0; i < 60 && !ZonaMorta(zonaGerada); i++) TickDaDestruicao(30);
				Checa("o mundo foi destruido de verdade", ZonaMorta(zonaGerada));

				TickDaConquista();
				Checa("DESTRUIR O PLANETA TIRA O DOMINIO junto", !_dominios.Contains(doGerado));
				Checa("...e o ex-dono soube por que",
					  EscutaDeAvisos.Exists(t => t.Contains("DESTRUÍDO")), string.Join(" | ", EscutaDeAvisos));
			}
			else Checa("achei um mundo gerado pra destruir", false, "nenhum sistema perto da Terra");

			// =====================================================================
			// 10. A API PRAS QUESTS DE CARGO
			// =====================================================================
			var exigeUm = new Regra { Texto = "conquistar um planeta", Opcoes = [[Cargos.ExigirDominios(1)]] };

			_dominios.RemoveAll(d => string.Equals(d.Assinatura, pl.Assinatura, StringComparison.Ordinal));
			Checa("uma regra de cargo 'conquiste um planeta' REPROVA quem nao domina nada",
				  !exigeUm.Vale(Ficha(pl)));

			FincarDominio(pl, Espaco.PreFeitos().First(p => p.Nome == "Earth"), pl.Pos);
			Checa("...e PASSA quando ele domina um -- pela FichaDeRank de producao",
				  exigeUm.Vale(Ficha(pl)), $"{Ficha(pl).PlanetasDominados} dominio(s)");

			// A REPUTACAO DE PLANETA CHEGANDO AOS CARGOS: era a pendencia escrita em quatro deles.
			RankDef guardiao = Cargos.Get("guardian")!;
			Checa("o Guardiao da Terra nao tem mais pendencia de reputacao de planeta",
				  !guardiao.Pendencias.Any(p => p.Contains("reputação de planeta")),
				  string.Join(" | ", guardiao.Pendencias));

			// A REGRA E CONFERIDA DIRETO, e nao pelo `OqueFalta`: aquele metodo devolve a PRIMEIRA
			// regra que falha, e o corpo de teste e humano -- ele reprovaria no sangue Namekuseijin e
			// a bancada nunca chegaria a olhar a reputacao. (Foi o que aconteceu na primeira rodada:
			// a checagem media a regra errada e teria ficado verde com a reputacao desligada.)
			Regra repDaTerra = guardiao.Regras.First(r => r.Texto.Contains("HERÓI do povo da Terra"));

			SomarReputacao("Earth", pl, -ReputacaoDe("Earth", pl.Conta) - 10, "bancada-derruba");
			Checa("...e agora ele COBRA: com reputacao baixa, a regra do povo da Terra REPROVA",
				  !repDaTerra.Vale(Ficha(pl)), $"reputacao {ReputacaoDe("Earth", pl.Conta):0}");

			SomarReputacao("Earth", pl,
				Reputacao.LimiarDeHeroi - ReputacaoDe("Earth", pl.Conta), "bancada-vira-heroi-2");
			Checa("...e PASSA quando ele e HEROI do povo da Terra -- a pendencia caducou de verdade",
				  repDaTerra.Vale(Ficha(pl)), $"reputacao {ReputacaoDe("Earth", pl.Conta):0}");

			// =====================================================================
			// AS SETE FAMILIAS COM DEFEITO INJETADO -- ver o cabecalho deste arquivo
			// =====================================================================
			// A ORDEM NAO E ARBITRARIA e as familias se ENTREGAM estado, o que e de proposito: cada
			// uma continua o mundo que a anterior deixou, como uma partida continua. A alternativa
			// (cada familia montando o proprio cenario do zero) mediria sete cenarios de bancada em
			// vez de um jogo.
			//
			//   A finca um mundo GERADO  ->  F o manda pro disco e traz de volta  ->  G o mata
			//   B invade Vegeta          ->  D contesta e VENCE por ela           ->  E cobra o tributo
			//   C cobra a ausencia no dominio da Terra, que sobrou de pe desde a secao 10.
			GD.Print("  ---- as sete familias, cada uma com o defeito injetado ----");

			PlanetaNoEspaco? conquistado = temGemeos
				? FamiliaDoMundoGerado(Checa, pl, gemeoA, gemeoB)
				: null;
			if (!temGemeos)
				Checa("a familia A tinha um par de homonimos pra medir", false,
					  "a varredura da secao 1 nao achou colisao de nome");

			if (conquistado is { } geradoMeu)
			{
				FamiliaDoReinicio(Checa, pl, geradoMeu);
				mundosMortos.Add(ZoneKey.Procedural(geradoMeu.Nome, geradoMeu.Seed));
				FamiliaDaConvivencia(Checa, pl, geradoMeu);
			}

			if (FamiliaDaBrigaDaOnda(Checa, pl) is { } invVegeta)
			{
				FamiliaDaContestacao(Checa, pl, invVegeta);
				FamiliaDoTributo(Checa, pl);
			}

			FamiliaDaAusencia(Checa, pl);
		}
		catch (Exception e) { Checa("a bancada rodou sem estourar", false, e.ToString()); }
		finally
		{
			// ---- DESFAZ TUDO ----
			foreach (Invasao i in _invasoes.Values.ToList()) LimparOnda(i, tudo: true);
			_invasoes.Clear();
			_arranques.Clear();
			_proximaCampanha.Remove(pl.Assinatura);

			// os corpos de defensor que tenham escapado de um caminho de limpeza
			foreach (ServerPlayer p in _players.Values.ToList())
				if (p.Peer == null && (p.Name.StartsWith("Soldado ") || p.Name.StartsWith("General ")))
					RemoverNpc(p);

			// e os contestadores forjados, pelo mesmo motivo (a familia D forja o dela)
			foreach (ServerPlayer p in _players.Values.ToList())
				if (p.Peer == null && p.Name.StartsWith("bancada: ")) RemoverNpc(p);

			foreach (ZoneKey z in mundosMortos) RessuscitarPlaneta(z);

			// O CEU VOLTA **ANTES** DE SALVAR, e a ordem passou a importar quando o carimbo da
			// ultima cobranca virou campo de disco (ver `LivroDeConquista.Cobranca`): salvar com a
			// manivela adiantada gravaria um carimbo no FUTURO, e o proximo boot leria um relogio
			// que recuou. Nao quebra nada -- a guarda de recuo reancora --, mas deixaria a bancada
			// escrevendo no disco um estado que jogo nenhum produz.
			_adiantoDoCeu = ceuGuardado;
			_relogioDoMundo = mundoGuardado;
			_ultimaCobrancaDeLealdade = 0;

			_dominios.Clear();
			_dominios.AddRange(dominiosGuardados);
			SalvarConquista();

			SomarReputacao("Earth", pl, repTerraGuardada - ReputacaoDe("Earth", pl.Conta), "bancada-desfaz");
			SomarReputacao("Vegeta", pl, repVegetaGuardada - ReputacaoDe("Vegeta", pl.Conta), "bancada-desfaz");

			pl.Ficha.BP = bpGuardado;
			pl.Ficha.Zeni = zeniGuardado;
			pl.Ficha.Statify();
			pl.Ficha.PowerLevel();
			MoveToZone(pl.Id, zonaGuardada, posGuardada);

			// A COLETA DE TRIBUTO (familia E) chama `Persistir` -- o zeni da bancada FOI PRO DISCO.
			// Devolver so o campo em memoria deixaria o save com o dinheiro de mentira.
			Persistir(pl);

			EscutaDeAvisos = null;
			EscutaDaConquista = null;

			GD.Print($"===== CONQUISTA: {ok} OK, {falhou} FALHA(S) =====\n");
		}
	}

	/// <summary>
	/// UM TERCEIRO COM ASSINATURA, pra a contestacao. Conta e slot nao sao enfeite: sem eles ele nao
	/// tem <see cref="ServerPlayer.Assinatura"/>, e a reputacao de quem defende o povo cairia num
	/// registro vazio -- a bancada mediria zero e chamaria isso de resultado.
	///
	/// `Peer` nulo (e um corpo forjado) nao atrapalha: o arranque nao exige teclado, so exige estar
	/// vivo, de pe e parado. E o `SomarReputacao` cobra a CONTA, que ele tem.
	/// </summary>
	private ServerPlayer ForjarContestador(Vec2 onde, ZoneKey? zona = null)
	{
		var novo = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,
			Name = "bancada: o contestador",
			Race = "Human",
			Genero = "Male",
			Idade = 25,

			// A ZONA E PARAMETRO desde que a familia D passou a contestar em VEGETA: `PorNoMundo`
			// inscreve o corpo na lista da zona que a ficha diz, e mover o campo depois deixaria o
			// corpo em duas listas -- a IA da onda o enxergaria no planeta errado.
			Zone = zona ?? ZoneKey.Premade("Earth"),
			Pos = onde,
			Conta = "bancada_conquista",
			Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 1_000_000 },
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		return novo;
	}

	// =====================================================================
	// OS HOMONIMOS -- a varredura que faz a secao 1 e a familia A valerem
	// =====================================================================
	/// <summary>
	/// DOIS MUNDOS GERADOS COM O MESMO NOME, ambos VIVOS. Devolve os PLANETAS e nao as chaves porque
	/// a familia A precisa conquistar um deles -- e pra isso precisa da seed, do raio e da posicao.
	///
	/// O corte por <see cref="ZonaMorta"/> nao e detalhe: a secao 9 mata um mundo gerado, e se ele
	/// caisse no par a familia A tentaria conquistar um cadaver e leria a recusa certa como falha.
	/// </summary>
	private bool AcharHomonimosGerados(out PlanetaNoEspaco a, out PlanetaNoEspaco b, out int varridos)
	{
		a = default;
		b = default;
		var vistos = new Dictionary<string, PlanetaNoEspaco>(StringComparer.Ordinal);

		for (int sx = -20; sx <= 20; sx++)
			for (int sy = -20; sy <= 20; sy++)
			{
				if (Sistemas.Do(SeedDoUniverso, sx, sy) is not { } s) continue;
				for (int k = 0; k < s.Orbitas; k++)
				{
					PlanetaNoEspaco p = s.Planeta(k);
					if (p.Premade || ZonaMorta(ZoneKey.Procedural(p.Nome, p.Seed))) continue;

					if (vistos.TryGetValue(p.Nome, out PlanetaNoEspaco outro) && outro.Seed != p.Seed)
					{
						a = outro;
						b = p;
						varridos = vistos.Count;
						return true;
					}
					vistos[p.Nome] = p;
				}
			}

		varridos = vistos.Count;
		return false;
	}

	/// <summary>
	/// **O MUNDO NASCE DE VERDADE**, e devolve a zona -- ou nulo se ele nao ficou pronto.
	///
	/// As duas funcoes sao as MESMAS que o `_Process` chama, na mesma ordem (`MundoDaZona` encomenda,
	/// `TickDasGeracoes` colhe), e e a mesma receita do `PousarDeVerdade` da bancada do berco. Um
	/// atalho que escrevesse em `_zonasGeradas` daqui testaria o atalho: sem a colisao que a thread
	/// produz, o defensor nasceria dentro de pedra e a bandeira ficaria em coordenada bloqueada.
	/// </summary>
	private ZoneKey? ZonaGeradaPronta(PlanetaNoEspaco p)
	{
		var z = ZoneKey.Procedural(p.Nome, p.Seed);
		for (int i = 0; i < 900; i++)
		{
			if (MundoDaZona(z, p) != null) return z;
			TickDasGeracoes();
			System.Threading.Thread.Sleep(1);
		}
		return null;
	}

	// =====================================================================
	// FAMILIA A -- A CHAVE VALE EM JOGO: UM MUNDO **GERADO**, PELO VERBO
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE NAO BASTAVA `FincarDominio` ============================
	/// A secao 9 ja punha um dominio num mundo gerado -- mas ESCREVENDO no livro. Isso prova que o
	/// registro aceita uma seed, e nao prova nada sobre o caminho: o verbo, a porta de poder do mundo
	/// sem povo, o PLANTIO (que e a unica forma de conquista que existe onde nao mora ninguem) e o
	/// dominio nascendo da vitoria.
	///
	/// E o fecho e o HOMONIMO. O DM indexa `conq_data["Planeta"]` por nome; aqui ha dois mundos
	/// chamados igual e vivos ao mesmo tempo. Dominar um NAO pode entregar o outro -- e essa e a
	/// unica checagem do arquivo que so a chave nova sabe passar.
	/// ========================================================================================
	/// </summary>
	private PlanetaNoEspaco? FamiliaDoMundoGerado(Checagem Checa, ServerPlayer pl,
												  PlanetaNoEspaco alvo, PlanetaNoEspaco gemeo)
	{
		if (ZonaGeradaPronta(alvo) is not { } z)
		{
			Checa($"o mundo gerado '{alvo.Nome}' nasceu pra ser conquistado", false,
				  "a geracao nao ficou pronta em 900 voltas");
			return null;
		}

		MoveToZone(pl.Id, z, ChegadaDaZonaGerada(z) ?? Vec2.Zero);
		_proximaCampanha.Remove(pl.Assinatura);

		Checa("o corpo celeste do mundo gerado resolve pela ZONA (sem isto o verbo nem chega no plantio)",
			  CorpoDaZona(z)?.Seed == alvo.Seed, $"{alvo.Nome} #{alvo.Seed}");
		Checa("...e ele NAO tem povo -- entao a porta de poder e a baixa e nao ha onda pra vencer",
			  !TemPovo(alvo.Nome) && Conquista.OndasSemPovo.Length == 0);

		EscutaDeAvisos = [];
		ConquistaInvadir(pl, "");

		Invasao? inv = InvasaoAqui(z);
		Checa("o VERBO abre a conquista de um mundo gerado, e ela nasce em PLANTIO",
			  inv is { Fase: FaseDaInvasao.Plantio }, string.Join(" | ", EscutaDeAvisos));
		if (inv == null) return null;

		// O PLANTIO NAO E INSTANTANEO -- e o que da a um terceiro a chance de aparecer. Sem esta
		// metade, "a bandeira leva 60 s" seria uma frase de chat.
		_relogioDoMundo += Conquista.SegundosDePlantio - 5;
		TickDasInvasoes();
		Checa($"a {Conquista.SegundosDePlantio - 5:0}s a bandeira AINDA nao firmou (o plantio e a janela de contestacao)",
			  _invasoes.ContainsKey(inv.Chave) && DominioDe(ChaveDePlaneta.De(alvo)) == null);

		_relogioDoMundo += 6;
		TickDasInvasoes();

		Dominio? d = DominioDe(ChaveDePlaneta.De(alvo));
		Checa($"passados {Conquista.SegundosDePlantio:0}s a bandeira FIRMA e o mundo gerado vira dominio",
			  d != null && string.Equals(d.Assinatura, pl.Assinatura, StringComparison.Ordinal),
			  $"{_dominios.Count} dominio(s), {_invasoes.Count} invasao(oes)");
		if (d == null) return null;

		Checa("...e o dominio guarda a SEED e nao um nome de mapa (a chave e a do jogo)",
			  !d.PreFeito && d.Seed == alvo.Seed, d.Chave.Texto);
		Checa("...e o endereco no ceu resolveu de volta pro MESMO corpo (posicao, nao identidade)",
			  d.K >= 0 && CorpoDoDominio(d)?.Seed == alvo.Seed, d.Endereco);

		Mutacao(Checa,
			$"dominar um '{alvo.Nome}' NAO entregou o outro '{gemeo.Nome}' -- o livro e por chave, nao por nome",
			$"o dominio reindexado pela seed do gemeo (#{gemeo.Seed}), que e o que 'indexar por nome' faz",
			() => DominioDe(ChaveDePlaneta.De(alvo)) != null
			   && DominioDe(ChaveDePlaneta.De(gemeo)) == null
			   && TagDoDono(ZoneKey.Procedural(gemeo.Nome, gemeo.Seed)).Length == 0,
			() => d.Seed = gemeo.Seed,
			() => d.Seed = alvo.Seed);

		return alvo;
	}

	// =====================================================================
	// FAMILIA F -- A CONQUISTA SOBREVIVE AO REINICIO
	// =====================================================================
	/// <summary>
	/// IDA E VOLTA PELO `conquista.json` DE VERDADE -- `SalvarConquista` -> livro zerado ->
	/// `CarregarConquista`, que e literalmente a sequencia de um boot.
	///
	/// AS DUAS FORMAS DE CHAVE NA MESMA VOLTA (a Terra pre-feita e o mundo gerado) porque elas
	/// gravam campos diferentes: o pre-feito carrega o NOME, o gerado carrega a SEED. Um teste com
	/// so uma das duas passaria com metade do serializador quebrado.
	///
	/// E o DEFEITO INJETADO e a mutilacao do save (a seed zerada) -- o irmao do "save inteiro menos o
	/// vetor do BP pinado" da `--provateste`, e pelo mesmo motivo: apagar o arquivo prova pouco,
	/// porque qualquer coisa reprova; tirar UM campo prova que a checagem le a identidade.
	/// </summary>
	private void FamiliaDoReinicio(Checagem Checa, ServerPlayer pl, PlanetaNoEspaco gerado)
	{
		ChaveDePlaneta chaveG = ChaveDePlaneta.De(gerado);
		ChaveDePlaneta chaveT = ChaveDePlaneta.Da(ZoneKey.Premade("Earth"))!.Value;

		// A ESCOLHA DE RENASCIMENTO PELO VERBO, e nao escrevendo o campo: ele so vale perto da
		// bandeira, e o corpo esta em cima dela (o plantio fincou onde ele estava).
		ConquistaSpawn(pl);
		Dominio? antes = DominioDe(chaveG);
		Checa("o verbo 'renascer aqui' marcou o dominio gerado", antes is { EhOSpawn: true });
		if (antes == null) return;

		antes.Lealdade = 73.5;   // um numero que nao e o padrao: 100 passaria com o campo perdido
		float fx = antes.Fx, fy = antes.Fy;
		SalvarConquista();

		string original = System.IO.File.ReadAllText(CaminhoDaConquista);

		bool VoltaDoDisco()
		{
			_dominios.Clear();
			_recadosDeConquista.Clear();
			CarregarConquista();

			Dominio? g = DominioDe(chaveG);
			return g != null && DominioDe(chaveT) != null
				&& !g.PreFeito && g.Seed == gerado.Seed
				&& g.EhOSpawn && Math.Abs(g.Lealdade - 73.5) < 1e-6
				&& g.Fx == fx && g.Fy == fy;
		}

		Mutacao(Checa,
			"o dominio (pre-feito E gerado) volta INTEIRO do disco: chave, lealdade, bandeira e o renascimento",
			"o save mutilado -- a SEED zerada, que e o campo que carrega a identidade do mundo sorteado",
			VoltaDoDisco,
			() => System.IO.File.WriteAllText(CaminhoDaConquista,
					original.Replace($"\"Seed\": {gerado.Seed}", "\"Seed\": 0")),
			() => System.IO.File.WriteAllText(CaminhoDaConquista, original));

		// O RECADO GUARDADO TAMBEM ATRAVESSA -- e a metade do sistema que so existe porque o dono
		// esta offline quando o mundo dele acontece.
		_recadosDeConquista.Clear();
		RecadoAoDono("bancada-sig-ausente", "Seu domínio caiu enquanto você dormia.");
		SalvarConquista();

		// `_dominios.Clear()` JUNTO, e nao so os recados: `CarregarConquista` faz `AddRange` sobre o
		// que ja esta na memoria -- e sem limpar, cada volta DUPLICA o livro. Foi o que aconteceu na
		// primeira rodada (dois dominios da Terra no log), e o estrago apareceu duas familias depois.
		_recadosDeConquista.Clear();
		_dominios.Clear();
		CarregarConquista();
		Checa("...e o recado do dono AUSENTE tambem volta do disco (ele quase sempre esta offline)",
			  _recadosDeConquista.ContainsKey("bancada-sig-ausente"));
		_recadosDeConquista.Remove("bancada-sig-ausente");
		SalvarConquista();
	}

	// =====================================================================
	// FAMILIA G -- CONQUISTA x DESTRUICAO, AS DUAS PERGUNTAS DE CONVIVENCIA
	// =====================================================================
	/// <summary>
	/// **UM DOMINIO NAO SOBREVIVE AO PLANETA, E UM CADAVER NAO SE CONQUISTA.**
	///
	/// A secao 9 ja mede a primeira metade pelo pavio inteiro. Aqui o que se mede e que a resposta e
	/// **UNICA**: quem sabe se um mundo morreu e o livro dos mortos, e mais ninguem. O defeito
	/// injetado e o planeta RESSUSCITADO -- se a conquista tivesse uma nocao propria de "morto"
	/// (lendo o `Condenado` das sagas, por exemplo), o criterio continuaria verde com o planeta vivo,
	/// e as duas listas divergiriam no primeiro dia em que a destruicao mudasse de ideia.
	/// </summary>
	private void FamiliaDaConvivencia(Checagem Checa, ServerPlayer pl, PlanetaNoEspaco gerado)
	{
		var z = ZoneKey.Procedural(gerado.Nome, gerado.Seed);
		var terra = ZoneKey.Premade("Earth");
		ChaveDePlaneta chave = ChaveDePlaneta.De(gerado);
		Vec2 bandeira = DominioDe(chave) is { } d0 ? new Vec2(d0.Fx, d0.Fy) : pl.Pos;

		// ============================ SAIR DO PLANETA ANTES DE MANDA-LO EXPLODIR ============================
		// A primeira rodada desta familia morreu -- literalmente. O corpo tinha acabado de conquistar
		// aquele mundo e continuava em cima dele; a destruicao mata quem fica ("1 morto(s)" no log), e
		// dali pra frente TODO verbo respondia *"nao da pra fincar bandeira de joelhos"*. Duas familias
		// inteiras reprovaram por causa disso, e nenhuma delas tinha defeito nenhum.
		//
		// O outro sistema estava certo; o cenario e que era impossivel. Fica registrado porque e o
		// jeito mais barato de a proxima pessoa nao gastar uma rodada nisso.
		// ==============================================================================================
		MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
		DePeDeNovo(pl);

		void Matar()
		{
			if (ZonaMorta(z)) return;
			ComecarDestruicao(z, 1e12, "bancada da conquista: familia G");
			for (int i = 0; i < 60 && !ZonaMorta(z); i++) TickDaDestruicao(30);
		}

		Matar();
		Checa("o mundo gerado morreu pelo sistema de DESTRUICAO (nao por um atalho daqui)", ZonaMorta(z));

		bool ODominioNaoSobreviveEOVerboRecusa()
		{
			// O DOMINIO E REFINCADO A CADA MEDIDA: o criterio precisa rodar tres vezes (antes,
			// com o defeito e depois), e o primeiro tique o derruba.
			if (DominioDe(chave) == null) FincarDominio(pl, gerado, bandeira);
			TickDaConquista();
			bool caiu = DominioDe(chave) == null;

			MoveToZone(pl.Id, z, bandeira);
			_proximaCampanha.Remove(pl.Assinatura);
			EscutaDeAvisos = [];
			ConquistaInvadir(pl, "");
			bool recusou = _invasoes.Count == 0 && EscutaDeAvisos.Exists(t => t.Contains("planeta morto"));

			// FORA DAQUI ANTES DO `consertar`: e ele que manda o planeta explodir de novo, e o corpo
			// nao pode estar em cima quando isso acontecer -- ver o bloco acima.
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));

			// o que o defeito deixar de pe sai daqui, senao a proxima volta comeca com dois
			foreach (Dominio d in _dominios.ToList())
				if (d.Chave == chave) _dominios.Remove(d);

			return caiu && recusou;
		}

		Mutacao(Checa,
			"um dominio nao sobrevive ao planeta, e um planeta MORTO nao se conquista",
			"o planeta ressuscitado -- se a conquista tivesse um 'morto' proprio, isto continuaria verde",
			ODominioNaoSobreviveEOVerboRecusa,
			() => RessuscitarPlaneta(z),
			Matar);

		DePeDeNovo(pl);
	}

	/// <summary>
	/// O INVASOR DE VOLTA INTEIRO -- de pe, sem ferida e vivo.
	///
	/// Existe porque metade das familias abaixo comeca com um verbo, e TODO verbo de conquista abre
	/// com `dead || KO` (*"nao da pra fincar bandeira de joelhos"*). Um corpo caido de uma familia
	/// anterior faz a seguinte reprovar sem ter defeito nenhum -- e a recusa e tao generica que o
	/// detalhe no console nao denuncia a causa.
	/// </summary>
	private static void DePeDeNovo(ServerPlayer pl)
	{
		pl.Ficha.dead = false;
		pl.Ficha.KO = false;
		pl.Combate.Corpo.Restaurar();
		pl.Combate.SincronizarVida();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
	}

	// =====================================================================
	// FAMILIA B -- AS ONDAS ACONTECEM E OS DEFENSORES **LUTAM**
	// =====================================================================
	/// <summary>
	/// ============================ O CRITERIO NAO MEDE INTENCAO, MEDE PANCADA ============================
	/// A secao 4 ja afirma que o defensor "engajou" -- ela le o campo que o proprio
	/// <see cref="EngajarDefensor"/> acabou de escrever, o que e pouco mais que ler a si mesma. Aqui
	/// o laco e o par de producao (`TickCombate` + `TickDosCorposSemDono`, na ordem do `_Process`) e
	/// o criterio e o `UltimoAgressor` **do invasor**, que so o golpe que ACERTA escreve, pelo funil
	/// `MarcarAgressao`. Ou seja: a IA nova decidiu, andou, alcancou e bateu.
	///
	/// O DEFEITO INJETADO e o rancor da onda apagado -- o cidadao volta a ser "pacifico ate apanhar"
	/// (`PresaDoNpc` devolve nulo com o prazo vencido) e a onda fica parada olhando, que e
	/// exatamente o que aconteceria se o `foundTarget` do DM nao tivesse sido portado.
	/// ==============================================================================================
	/// </summary>
	private Invasao? FamiliaDaBrigaDaOnda(Checagem Checa, ServerPlayer pl)
	{
		var vegeta = ZoneKey.Premade("Vegeta");
		MoveToZone(pl.Id, vegeta, PontoDeNascimento(vegeta));
		DePeDeNovo(pl);
		_proximaCampanha.Remove(pl.Assinatura);
		EscutaDeAvisos = [];

		Checa("Vegeta tem povo (e o plano de povoamento que diz) -- entao ha onda pra medir", TemPovo("Vegeta"));
		ConquistaInvadir(pl, "");

		if (InvasaoAqui(vegeta) is not { } inv)
		{
			Checa("a invasao de Vegeta comecou", false, string.Join(" | ", EscutaDeAvisos));
			return null;
		}

		_relogioDoMundo += Conquista.SegundosDeTensao + 0.5;
		TickDasInvasoes();

		Checa($"a onda nasceu com {Conquista.OndasComPovo[0].Quantos} corpos, e cada um tem CEREBRO "
			+ "(sem ele o defensor e estatua)",
			  inv.Defensores.Count == Conquista.OndasComPovo[0].Quantos
			  && inv.Defensores.Keys.All(id => _players[id].Cerebro != null),
			  $"{inv.Defensores.Count} defensor(es)");

		if (inv.Defensores.Count == 0) return inv;

		bool ALutaAcontece()
		{
			// O INVASOR VOLTA INTEIRO A CADA MEDIDA. Sem isto, a segunda volta comecaria com um
			// corpo ja quebrado da primeira e o criterio mediria o estrago acumulado.
			pl.Combate.Corpo.Restaurar();
			pl.Combate.SincronizarVida();
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;
			pl.UltimoAgressor = 0;
			pl.RancorAte = 0;

			for (int i = 0; i < 900 && !inv.Defensores.ContainsKey(pl.UltimoAgressor); i++)
			{
				TickCombate(Jandirus.Net.Protocol.TickSeconds);
				TickDosCorposSemDono(Jandirus.Net.Protocol.TickSeconds);
			}

			bool bateram = inv.Defensores.ContainsKey(pl.UltimoAgressor);

			pl.Combate.Corpo.Restaurar();
			pl.Combate.SincronizarVida();
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;
			return bateram;
		}

		Mutacao(Checa,
			"os defensores VAO PRA CIMA e ACERTAM o invasor -- pelo tique de producao, com a IA de verdade",
			"o rancor da onda apagado: o cidadao volta a ser 'pacifico ate apanhar' e a onda fica parada",
			ALutaAcontece,
			() => { foreach (int id in inv.Defensores.Keys) { _players[id].UltimoAgressor = 0; _players[id].RancorAte = 0; } },
			() => { foreach (int id in inv.Defensores.Keys.ToList()) EngajarDefensor(inv, _players[id]); });

		return inv;
	}

	// =====================================================================
	// FAMILIA D -- A CONTESTACAO, NO SENTIDO QUE FALTAVA
	// =====================================================================
	/// <summary>
	/// A secao 6 mede a contestacao que **VENCE**. Este e o outro sentido, e ele e o que faz a regra
	/// ser um risco e nao um botao: o terceiro chega, agarra a bandeira, **leva um golpe**, o canal
	/// cai -- e a invasao segue e o invasor leva o planeta.
	///
	/// O DEFEITO INJETADO e o retrato do rancor RE-LIDO a cada tique em vez de comparado com o do
	/// inicio. E o formato de bug mais provavel deste canal (um `a.RancorNoInicio = m.RancorAte` no
	/// lugar de um `if`), e ele deixa o arranque imune a dano -- ou seja, invadir um mundo com povo
	/// viraria coisa que qualquer transeunte desfaz de graca.
	///
	/// E de quebra a **fama**: matar o LIDER custa mais que matar um soldado, e guarnicao de
	/// OCUPANTE nao custa nada (matar o capanga de um tirano nao ofende o povo).
	/// </summary>
	private void FamiliaDaContestacao(Checagem Checa, ServerPlayer pl, Invasao inv)
	{
		ServerPlayer heroi = ForjarContestador(inv.Bandeira, inv.Zona);
		bool reLerORetrato = false;

		try
		{
			bool OGolpeDerrubaOArranque()
			{
				_arranques.Remove(heroi.Id);
				heroi.Pos = inv.Bandeira;
				heroi.UltimoAgressor = 0;
				heroi.RancorAte = 0;
				heroi.Ficha.KO = false;
				heroi.Ficha.dead = false;

				ConquistaArrancar(heroi);
				if (!_arranques.TryGetValue(heroi.Id, out Arranque? a)) return false;

				// O GOLPE, pelo funil de producao -- o mesmo que o soco escreve.
				MarcarAgressao(heroi, pl);
				if (reLerORetrato) a.RancorNoInicio = heroi.RancorAte;

				TickDoArranque(0.1);
				bool caiu = !_arranques.ContainsKey(heroi.Id) && !inv.Arrancada;

				_arranques.Remove(heroi.Id);
				inv.Arrancada = false;
				return caiu;
			}

			Mutacao(Checa,
				"o contestador FALHA quando leva um golpe: o canal de arranque cai e a bandeira fica",
				"o retrato do rancor RE-LIDO a cada tique em vez de comparado -- o arranque fica imune a dano",
				OGolpeDerrubaOArranque,
				() => reLerORetrato = true,
				() => reLerORetrato = false);

			// ---- A FAMA: matar o LIDER custa mais ----
			double CustoDeMatar(bool lider)
			{
				var jaTinha = new HashSet<int>(inv.Defensores.Keys);
				if (!NascerDefensor(inv, Conquista.PisoDeBpDeDefensor, lider)) return double.NaN;

				int id = inv.Defensores.Keys.First(k => !jaTinha.Contains(k));
				ServerPlayer v = _players[id];
				MarcarAgressao(v, pl);   // o matador e o invasor, pelo funil

				double r0 = ReputacaoDe(inv.Planeta, pl.Conta);
				v.Ficha.dead = true;
				MorreuUmCorpoSemDono(v);
				RemoverNpc(v);
				return ReputacaoDe(inv.Planeta, pl.Conta) - r0;
			}

			Mutacao(Checa,
				$"matar o LIDER da defesa custa {Conquista.RepPorMatarLider:0} e o soldado {Conquista.RepPorMatarDefensor:0} "
				+ "-- nocautear nao custa nada, matar e escolha",
				"a guarnicao virando de OCUPANTE (`Nativa` falsa): matar capanga de tirano nao ofende o povo",
				() => Math.Abs(CustoDeMatar(true) - Conquista.RepPorMatarLider) < 1e-9
				   && Math.Abs(CustoDeMatar(false) - Conquista.RepPorMatarDefensor) < 1e-9,
				() => inv.Nativa = false,
				() => inv.Nativa = true);
		}
		finally { if (_players.ContainsKey(heroi.Id)) RemoverNpc(heroi); }

		// ---- E O RESULTADO ESPERADO: sem contestacao que segure, o invasor LEVA o planeta ----
		for (int volta = 0; volta < 12 && _invasoes.ContainsKey(inv.Chave); volta++)
		{
			foreach (int id in inv.Defensores.Keys.ToList()) _players[id].Ficha.KO = true;
			_relogioDoMundo += Conquista.SegundosDeRespiro + 0.5;
			TickDasInvasoes();
		}

		Dominio? d = DominioDe(inv.Chave);
		Checa("vencidas as tres ondas SO POR NOCAUTE, o invasor vira soberano de Vegeta",
			  _invasoes.Count == 0 && d != null
			  && string.Equals(d?.Assinatura, pl.Assinatura, StringComparison.Ordinal),
			  $"{_invasoes.Count} invasao(oes), dominio {(d == null ? "nenhum" : d.Nome)}");
		Checa("...e o campo ficou limpo (nenhum defensor esquecido de pe)",
			  !_players.Values.Any(p => p.Peer == null
									 && (p.Name.StartsWith("Soldado ") || p.Name.StartsWith("General "))));
	}

	// =====================================================================
	// FAMILIA E -- O TRIBUTO CAI NO BOLSO, E A NEGLIGENCIA O CORTA
	// =====================================================================
	/// <summary>
	/// O tributo do DM (`conq_tribute_pending`, :121) so tinha o TETO de 24 h; a lealdade e deste
	/// port e multiplica DEPOIS dele. As duas cobram a mesma negligencia por caminhos diferentes, e
	/// e por isso que o criterio mede as duas de uma vez, com um numero exato: 48 h de acumulo com
	/// metade da lealdade tem que pagar `24 h x taxa x 0,5`, e nada mais.
	///
	/// O DEFEITO INJETADO e a lealdade de volta pro cheio. Ele reprova o criterio por DOIS motivos ao
	/// mesmo tempo (o corte some e o valor dobra), e e o mais honesto que ha: se o tributo lesse
	/// qualquer outra coisa que nao a lealdade daquele dominio, isto ficaria verde.
	/// </summary>
	private void FamiliaDoTributo(Checagem Checa, ServerPlayer pl)
	{
		var vegeta = ZoneKey.Premade("Vegeta");
		if (DominioDaZona(vegeta) is not { } d) { Checa("ha um dominio em Vegeta pra cobrar tributo", false); return; }

		// LONGE DA BANDEIRA: a mesma distancia de qualquer construcao. Sem esta metade, "colete na
		// bandeira" seria texto de chat.
		pl.Pos = new Vec2(d.Fx + 40 * ZoneCollision.TileSize, d.Fy);
		double zeniAntes = pl.Ficha.Zeni;
		EscutaDeAvisos = [];
		ConquistaTributo(pl);
		Checa("cobrar tributo LONGE da bandeira e recusado",
			  Math.Abs(pl.Ficha.Zeni - zeniAntes) < 1e-9 && EscutaDeAvisos.Exists(t => t.Contains("mais perto")),
			  string.Join(" | ", EscutaDeAvisos));

		pl.Pos = new Vec2(d.Fx, d.Fy);

		// A LEALDADE E VARIAVEL CAPTURADA e nao um valor escrito dentro do criterio: o criterio arma o
		// cenario a cada medida, e se ele mesmo cravasse `d.Lealdade = 50` ele APAGARIA a injecao
		// antes de ler o resultado. Foi o que aconteceu na primeira rodada -- a checagem ficou verde
		// com o defeito no ar, que e exatamente o tipo de checagem que esta bancada existe pra achar.
		double lealdadeDoDominio = 50;

		double OQuePagou()
		{
			d.Lealdade = lealdadeDoDominio;
			d.Coletado = TempoDoMundo - 48 * 3600;   // o DOBRO do teto
			double z0 = pl.Ficha.Zeni;
			ConquistaTributo(pl);
			return pl.Ficha.Zeni - z0;
		}

		double esperado = Conquista.HorasDeTetoDoTributo * Conquista.TributoComPovoPorHora * 0.5;

		Mutacao(Checa,
			$"48 h de acumulo com metade da lealdade pagam {esperado:N0} zeni -- o TETO de "
			+ $"{Conquista.HorasDeTetoDoTributo:0} h dispara e a lealdade corta o resto",
			"a lealdade de volta pro cheio: o corte some e a conta dobra",
			() => Math.Abs(OQuePagou() - esperado) < 1,
			() => lealdadeDoDominio = Conquista.LealdadeMaxima,
			() => lealdadeDoDominio = 50);

		// COLETAR REARMA O RELOGIO. Sem isto o dominio seria uma torneira de zeni: dois cliques
		// seguidos pagariam duas vezes o mesmo tributo.
		double zeniDepois = pl.Ficha.Zeni;
		EscutaDeAvisos = [];
		ConquistaTributo(pl);
		Checa("coletar duas vezes seguidas NAO paga duas vezes (a coleta rearma o relogio)",
			  Math.Abs(pl.Ficha.Zeni - zeniDepois) < 1e-9
			  && EscutaDeAvisos.Exists(t => t.Contains("se acumulando")),
			  string.Join(" | ", EscutaDeAvisos));

		Checa("...e coletar conta como PRESENCA (o soberano estava la pra recolher)",
			  Math.Abs(d.Visto - TempoDoMundo) < 2, $"visto ha {(TempoDoMundo - d.Visto):0} s");

		// A FAMA CHEGANDO NA REPUTACAO -- a outra metade do "tributo/fama".
		double repAntes = ReputacaoDe("Vegeta", pl.Conta);
		SomarReputacao("Vegeta", pl, Conquista.RepPorLibertar, "bancada-libertacao");
		Checa($"a LIBERTACAO paga {Conquista.RepPorLibertar:0} de reputacao com aquele povo, e ela entra "
			+ "no mesmo livro que os cargos leem",
			  Math.Abs(ReputacaoDe("Vegeta", pl.Conta) - (repAntes + Conquista.RepPorLibertar)) < 1e-9
			  && Ficha(pl).ReputacaoDePlaneta?.TryGetValue("Vegeta", out double r) == true
			  && Math.Abs(r - ReputacaoDe("Vegeta", pl.Conta)) < 1e-9,
			  $"{repAntes:0} -> {ReputacaoDe("Vegeta", pl.Conta):0}");
	}

	// =====================================================================
	// FAMILIA C -- MANTER CUSTA, INCLUSIVE ATRAVESSANDO UM REINICIO
	// =====================================================================
	/// <summary>
	/// ============================ O BURACO QUE SO ESTA FAMILIA ACHOU ============================
	/// A secao 8 prova que a ausencia cobra -- com o servidor DE PE o tempo todo. Mas o cabecalho da
	/// <see cref="Conquista.Lealdade"/> promete mais que isso: *"ele anda com o servidor desligado,
	/// que e o unico jeito de 'tres dias sem aparecer' significar tres dias"*.
	///
	/// E nao andava. O <see cref="TempoDoMundo"/> e absoluto (UTC) e a AUSENCIA de fato atravessava o
	/// desligamento -- mas o **passo da cobranca** vinha de `_ultimaCobrancaDeLealdade`, um campo de
	/// memoria que nascia zerado a cada boot: o primeiro tique reancorava e o intervalo desligado
	/// nunca era cobrado. Tres dias sumido, com um reinicio no meio, custavam o uptime e mais nada.
	///
	/// O conserto foi um campo no `conquista.json` (ver `LivroDeConquista.Cobranca`), e o defeito que
	/// esta familia injeta e **um save ANTIGO** -- o arquivo sem aquela linha. Ele reprova, e de
	/// quebra diz o que acontece com quem atualizar o servidor: nao se cobra do passado que ninguem
	/// mediu, o carimbo reancora, e a partir dali vale.
	/// ======================================================================================
	/// </summary>
	private void FamiliaDaAusencia(Checagem Checa, ServerPlayer pl)
	{
		var terra = ZoneKey.Premade("Earth");
		ChaveDePlaneta chaveT = ChaveDePlaneta.Da(terra)!.Value;
		if (DominioDe(chaveT) == null) FincarDominio(pl, Espaco.PreFeitos().First(p => p.Nome == "Earth"), pl.Pos);

		// LONGE DELE: presenca recupera, e medir ausencia com o dono em cima da bandeira mediria o
		// contrario. (Ele esta em Vegeta desde a familia B; a linha e explicita de proposito.)
		var vegeta = ZoneKey.Premade("Vegeta");
		MoveToZone(pl.Id, vegeta, PontoDeNascimento(vegeta));

		double ceuDaFamilia = _adiantoDoCeu;

		// ============================ O DEFEITO E UM SAVE DE VERSAO ANTERIOR ============================
		// Ele nao pode ser "mutilar o arquivo uma vez": o `Armar` reescreve o disco pelo caminho de
		// producao a cada medida e apagaria a injecao antes de ela ser lida. Entao o que se injeta e
		// a CONDICAO -- "o que este servidor grava nao tem o carimbo" --, que e exatamente o que um
		// `conquista.json` escrito antes desta correcao e.
		// ==========================================================================================
		bool saveAntigo = false;

		// SEMPRE O MESMO PONTO DE PARTIDA: o dominio cheio, visto agora, carimbo em dia e no disco.
		void Armar()
		{
			_adiantoDoCeu = ceuDaFamilia;
			Dominio d = DominioDe(chaveT)!;
			d.Lealdade = Conquista.LealdadeMaxima;
			d.Visto = TempoDoMundo;
			_ultimaCobrancaDeLealdade = TempoDoMundo;
			SalvarConquista();

			if (saveAntigo)
				System.IO.File.WriteAllText(CaminhoDaConquista, string.Join("\n",
					System.IO.File.ReadAllLines(CaminhoDaConquista).Where(l => !l.Contains("\"Cobranca\""))));
		}

		/// O BOOT INTEIRO: processo novo (carimbo zerado), livro relido do disco.
		void Reiniciar()
		{
			_ultimaCobrancaDeLealdade = 0;
			_dominios.Clear();
			_recadosDeConquista.Clear();
			CarregarConquista();
		}

		// 60 h com o servidor DESLIGADO no meio: 36 delas passam da carencia e valem 72 pontos.
		bool AAusenciaAtravessaOReinicio()
		{
			Armar();
			_adiantoDoCeu += 60 * 3600;   // o mundo andou enquanto o servidor estava fora
			Reiniciar();
			TickDaConquista();

			double esperada = Conquista.LealdadeMaxima
							- Conquista.QuedaDeLealdadePorHora * (60 - Conquista.HorasDeCarencia);
			Dominio? d = DominioDe(chaveT);
			bool certo = d != null && Math.Abs(d.Lealdade - esperada) < 0.5;
			_adiantoDoCeu = ceuDaFamilia;
			return certo;
		}

		Mutacao(Checa,
			$"{60 - Conquista.HorasDeCarencia:0} h de ausencia alem da carencia sao cobradas mesmo com um "
			+ "REINICIO no meio -- o relogio anda com o servidor desligado, e a cobranca junto",
			"um save de versao ANTERIOR: o arquivo sem o carimbo da ultima cobranca",
			AAusenciaAtravessaOReinicio,
			() => saveAntigo = true,
			() => saveAntigo = false);

		// E O FIM DA LINHA: abandonado tempo bastante, atravessando reinicios, o dominio SE PERDE.
		// O teto tem que disparar (corolario 0.7) -- e disparar do outro lado de varios boots.
		//
		// DE 12 EM 12 H e nao de 24 em 24 de proposito: com o passo grande a lealdade pula faixas
		// inteiras e os avisos de travessia -- que sao UM por passo -- nunca chegam ao ultimo. O
		// jogador que perde o planeta e o que atravessou as faixas uma a uma; e essa a cena.
		Armar();
		EscutaDaConquista = [];
		int meiosDias = 0;
		while (DominioDe(chaveT) != null && meiosDias < 10)
		{
			meiosDias++;
			_adiantoDoCeu += 12 * 3600;
			Reiniciar();
			TickDaConquista();
		}

		Checa($"{meiosDias * 12} h sumido, com um reinicio a cada 12 h, PERDEM o planeta -- "
			+ "o teto dispara mesmo assim", DominioDe(chaveT) == null,
			  $"lealdade {(DominioDe(chaveT)?.Lealdade ?? -1):0.00} depois de {meiosDias * 12} h");
		Checa("...e os avisos de travessia chegaram ANTES (ninguem perde um mundo sem ter lido que ia perder)",
			  EscutaDaConquista.Exists(t => t.Contains("ausência"))
			  && EscutaDaConquista.Exists(t => t.Contains("descontente"))
			  && EscutaDaConquista.Exists(t => t.Contains("REVOLTA")),
			  string.Join(" | ", EscutaDaConquista));
		Checa("...e o ex-dono soube por que perdeu",
			  EscutaDaConquista.Exists(t => t.Contains("PERDEU")), string.Join(" | ", EscutaDaConquista));
		EscutaDaConquista = null;

		_adiantoDoCeu = ceuDaFamilia;
	}

	/// <summary>
	/// UM MUNDO GERADO QUALQUER, perto da Terra. Pela MESMA enumeracao que a carta estelar usa
	/// (`Sistemas.Do` -> `Planeta(k)`), e nao por uma lista propria: um mundo que so a bancada
	/// conhece nao prova nada sobre o mundo em que se joga.
	/// </summary>
	private PlanetaNoEspaco? AcharMundoGerado()
	{
		for (int r = 1; r <= 6; r++)
			for (int sx = -r; sx <= r; sx++)
				for (int sy = -r; sy <= r; sy++)
				{
					if (Sistemas.Do(SeedDoUniverso, sx, sy) is not { } s) continue;
					for (int k = 0; k < s.Orbitas; k++)
					{
						PlanetaNoEspaco p = s.Planeta(k);
						if (!p.Premade && !ZonaMorta(ZoneKey.Procedural(p.Nome, p.Seed))) return p;
					}
				}
		return null;
	}
}
