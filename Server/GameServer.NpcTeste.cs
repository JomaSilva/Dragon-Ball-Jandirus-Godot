using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Npc;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DA FICHA DE NPC (`--npcteste`).
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// O sorteio e Core puro e daria pra provar num teste de mesa. O que um teste de mesa NAO prova e
/// justamente o que esta sessao entrega:
///
///   * que a ficha sorteada VIRA UM CORPO NO MUNDO (`_players`, lista da zona, aparencia no fio);
///   * que a guarda do "tem a forma e nao a usa" esta no FUNIL de producao -- ou seja, que chamar
///     `Transformar(npc, true)`, a mesma funcao da tecla C, nao move o chefe;
///   * que o roteiro avanca pelo membro mais ferido e avanca UMA VEZ SO por rajada.
///
/// As tres ja falharam neste projeto na forma "a regra existe e ninguem a chama": o `AmigoAbatido`
/// sem chamador, a API de sigilo de BP 100% orfa. Bancada de Core nao pega nenhuma delas.
/// ==============================================================================
///
///     Godot --headless -- --server --npcteste
/// </summary>
public partial class GameServer
{
	private bool _npcDeTeste;

	/// <summary>Roda uma vez, no primeiro login. MEXE no mundo (spawna e remove corpos) -- so com a flag.</summary>
	private void RodarBancadaDeNpc(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA AO VIVO DA FICHA DE NPC =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// A ZONA VAZIA: um pre-feito onde ninguem esta conectado, pra os anuncios do roteiro nao
		// chegarem a tela de ninguem. Mesmo gesto da bancada de convivio.
		ZoneKey zona = ZoneKey.Premade(
			Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		var nascidos = new List<ServerPlayer>();
		ServerPlayer? Spawnar(string molde, ulong lugar)
		{
			ServerPlayer? n = NascerNpc(molde, zona, new Vec2(64 * lugar, 0), lugar);
			if (n != null) nascidos.Add(n);
			return n;
		}

		try
		{
			// =====================================================================
			// 1. O CATALOGO
			// =====================================================================
			Checa("o npcs.json carregou", _moldes is { Total: > 0 }, $"{_moldes?.Total ?? 0} moldes");
			Checa("o races.json carregou (sem ele nao nasce NPC nenhum)", _racas != null);
			if (_moldes == null || _racas == null) return;

			List<string> problemas = [.. _moldes.Todos.SelectMany(m => m.Problemas())];
			Checa("nenhum molde do arquivo e contraditorio", problemas.Count == 0,
				  string.Join(" | ", problemas));

			// =====================================================================
			// 2. DETERMINISMO -- a pergunta que o dono fez
			// =====================================================================
			// Duas execucoes com a MESMA semente tem que dar o MESMO NPC. Sem isto, "dois servidores
			// com a mesma semente" e promessa, e o proximo campo sorteado a esmo quebraria calado.
			MoldeDeNpc soldado = _moldes.Get("soldado_saiyajin")!;
			string Assinar(FichaSorteada f) =>
				$"{f.Raca}/{f.Classe}/{f.Genero}/{f.Nome}/{f.Ficha.BP:0.000}/"
				+ $"{string.Join(",", f.Livro.Aprendidas.OrderBy(x => x, StringComparer.Ordinal))}";

			FichaSorteada a1 = SorteioDeNpc.Sortear(soldado, 12345, _racas!, _skills, zona.Name, 0);
			FichaSorteada a2 = SorteioDeNpc.Sortear(soldado, 12345, _racas!, _skills, zona.Name, 0);
			FichaSorteada b1 = SorteioDeNpc.Sortear(soldado, 12346, _racas!, _skills, zona.Name, 0);

			Checa("mesma semente = MESMA ficha (raca, classe, nome, BP e skills)",
				  Assinar(a1) == Assinar(a2), Assinar(a1) + "  !=  " + Assinar(a2));
			Checa("semente diferente = ficha diferente", Assinar(a1) != Assinar(b1), Assinar(b1));

			// E O SORTEIO NAO DEPENDE DA ORDEM DAS CHAMADAS: pedir um campo novo amanha nao pode
			// deslocar os de hoje. E o que o gerador POR CAMPO garante -- o mesmo (semente, campo)
			// da sempre a mesma sequencia, e campos diferentes sao sequencias independentes.
			Checa("o gerador por campo e reproduzivel",
				  SorteioDeNpc.Sorteador(999, "raca").Next(1_000_000)
				  == SorteioDeNpc.Sorteador(999, "raca").Next(1_000_000));
			Checa("...e dois campos da MESMA semente nao sao a mesma sequencia",
				  SorteioDeNpc.Sorteador(999, "raca").Next(1_000_000)
				  != SorteioDeNpc.Sorteador(999, "bp").Next(1_000_000));

			// =====================================================================
			// 3. COERENCIA -- o NPC nao sabe o que um jogador nao poderia saber
			// =====================================================================
			if (_skills != null)
				foreach (MoldeDeNpc m in _moldes.Todos)
				{
					// ============================ MOLDE SEM `racas` PRECISA DE UM PLANETA QUE TENHA BERCO ============================
					// A pool vazia passou a sair do BERCO daquele planeta (`Bercos.RacasNascidasEm`) em
					// vez da tabela do menu de criacao, e o `Sortear` GRITA quando ninguem nasce ali --
					// de proposito, pra dado errado nao virar um humano por acidente.
					//
					// A zona desta bancada e "a primeira pre-feita sem ninguem", e com o povoamento
					// ligado isso costuma ser um mapa sem berco nenhum (Arconia). O molde `cidadao`
					// caia justamente nele. A Terra e a resposta porque ela sempre tem quem nasca la.
					// ==========================================================================================================
					string planeta = m.Racas.Length > 0 ? zona.Name : SpawnZone.Name;
					FichaSorteada f = SorteioDeNpc.Sortear(m, Espaco.Hash64(m.Id), _racas!, _skills, planeta, 50_000);
					var proibidas = new List<string>();
					var sabidas = new HashSet<string>(f.Livro.Aprendidas, StringComparer.OrdinalIgnoreCase);

					foreach (string path in f.Livro.Aprendidas)
					{
						if (Array.IndexOf(m.Dadas, path) >= 0) continue;   // ensinada, nao comprada
						Skill? s = _skills.Get(path);
						if (s == null) { proibidas.Add($"{path}=inexistente"); continue; }
						if (!_skills.Permitida(s, f.Raca, f.Classe, false)) { proibidas.Add($"{path}=raca/classe"); continue; }
						if (!f.Livro.PenduraEmArvoreDe(_skills, s, f.Raca, f.Classe)) { proibidas.Add($"{path}=sem arvore"); continue; }
						if (!SkillCatalog.PreReqsOk(s, sabidas)) proibidas.Add($"{path}=pre-requisito");
					}

					Checa($"'{m.Id}': toda skill dele passaria pelo gate de um jogador",
						  proibidas.Count == 0, string.Join(" | ", proibidas));

					// O PODER ANUNCIADO E O PODER SENTIDO. Sem a normalizacao do `init_citizen`, o
					// `staminaratio` deflacionava o BP expresso pra 0,3x -- era o bug do "Sense %%"
					// (PlanetPopulation.dm:325-327). Um NPC que anuncia 530 mil e bate como 160 mil
					// e um inimigo que ninguem entende.
					double razao = f.Ficha.expressedBP / Math.Max(f.Ficha.BP, 1);
					Checa($"'{m.Id}': BP expresso bate com o BP da ficha (normalizacao do init_citizen)",
						  razao is > 0.9 and < 2.5, $"{f.Ficha.expressedBP:N0} / {f.Ficha.BP:N0} = {razao:0.00}");
				}

			// =====================================================================
			// 4. O ORCAMENTO -- poder compra pericia
			// =====================================================================
			// O boneco que o dono descreveu ("BP de SSJ3 e nenhuma skill de Ki") so existe se o
			// numero do soco e a lista de skills forem sorteados separados. Aqui um e funcao do outro.
			if (_skills != null)
			{
				var fraco = new MoldeDeNpc
				{
					Id = "bancada_fraco", Racas = ["Saiyan"], BpMin = 1000, BpMax = 1000,
					MarcosBase = 0, MarcosPorDecada = 1.5, Vocacao = soldado.Vocacao,
				};
				var forte = new MoldeDeNpc
				{
					Id = "bancada_forte", Racas = ["Saiyan"], BpMin = 1e12, BpMax = 1e12,
					MarcosBase = 0, MarcosPorDecada = 1.5, Vocacao = soldado.Vocacao,
				};
				int nFraco = SorteioDeNpc.Sortear(fraco, 7, _racas!, _skills, zona.Name, 0).Livro.Aprendidas.Count;
				int nForte = SorteioDeNpc.Sortear(forte, 7, _racas!, _skills, zona.Name, 0).Livro.Aprendidas.Count;

				Checa("mais BP compra mais pericia (o orcamento de marcos e funcao do poder)",
					  nForte > nFraco, $"{nFraco} skills com 1e3 de BP, {nForte} com 1e12");
				Checa("...e sem orcamento nenhum ele nao sabe nada",
					  SorteioDeNpc.Sortear(new MoldeDeNpc { Id = "z", Racas = ["Saiyan"], BpMin = 1000,
						  BpMax = 1000, MarcosBase = 0, MarcosPorDecada = 0 },
						  7, _racas!, _skills, zona.Name, 0).Livro.Aprendidas.Count == 0);

				// ============================ A ARVORE POR PROGRESSO VIROU ============================
				// Esta checagem dizia o contrario: "o destravamento por progresso continua INALCANCAVEL
				// (2 skills ligadas carregam os contadores, teto 1 contra o `> 2` do gate) -- se esta linha
				// reprovar, o dado mudou e a checagem tem que virar". Ela reprovou, e o dado mudou porque a
				// LEITURA do dado estava errada, nao o dado: `enabled = 0` no DM e "trancada ate o
				// pre-requisito entrar" (`skill.dm:26`), e o port lia como tranca permanente. Com o
				// `SkillBook.Avaliar` lendo o `enabled` como o `testskillprereqs()` le, as 18 skills de
				// corpo com pre-requisito abrem em cadeia, os contadores passam de 2, e o `growbranches()`
				// do Body (extraido em `skills.json`) abre Bodybuilding e Martial Skill -- pro NPC pelo
				// MESMO `Ofertas`/`Aprender` do jogador.
				//
				// O molde `forte` tem 18 marcos (1,5 por decada de BP, 12 decadas) e vocacao de soldado
				// (o corpo primeiro): e o bastante pra atravessar o tronco.
				// ==================================================================================
				FichaSorteada rico = SorteioDeNpc.Sortear(forte, 7, _racas!, _skills, zona.Name, 0);
				Checa("comprar pelo funil do jogador ABRE arvore nova pro NPC (bodyskill/bodyreadiness passam de 2 "
					+ "e o growbranches do Body abre Bodybuilding ou Martial Skill)",
					  rico.Livro.Destravadas.Count > 0
					  && (rico.Ficha.bodyskill > 2 || rico.Ficha.bodyreadiness > 2),
					  $"abertas: {string.Join(", ", rico.Livro.Destravadas)} | bodyskill={rico.Ficha.bodyskill:0.#} "
					  + $"bodyreadiness={rico.Ficha.bodyreadiness:0.#} | {rico.Livro.Aprendidas.Count} skills");
				Checa("...e o tier de vitrine do Body subiu com o investimento (invested >= 4 -> tier 2, Body.dm:20)",
					  rico.Livro.Arvore("/datum/skill/tree/Body") is { Tier: >= 2, Investido: >= 4 },
					  $"{rico.Livro.Arvore("/datum/skill/tree/Body")?.Tier}/{rico.Livro.Arvore("/datum/skill/tree/Body")?.Investido}");

				// AS VOLTAS: cada compra reconsulta `Ofertas`, e a prova disso e a arvore aberta acima --
				// ela so entra na oferta DEPOIS de uma compra ter subido o contador. A afirmacao anterior
				// aqui ("o sorteio para por falta de OFERTA, e sobra marco") descrevia o catalogo TRANCADO:
				// com o tronco do Body aberto em cadeia, 18 marcos compram uma dezena de skills e ACABAM,
				// e sobrar marco deixou de ser o normal. O que continua verdade e que ele gasta ate o
				// fim de um dos dois: oferta ou marco.
				Checa("o sorteio gasta ate acabar a oferta OU o marco, e com o tronco aberto 18 marcos viram uma dezena de skills",
					  rico.Livro.Aprendidas.Count >= 8
					  && (rico.Livro.MarcosLivres == 0
						  || !rico.Livro.Ofertas(_skills, "Saiyan", rico.Classe, false).Any(o => o.Estado == Recusa.Pode)),
					  $"{rico.Livro.Aprendidas.Count} skills, {rico.Livro.MarcosLivres}/{rico.Livro.MarcosTotais} marcos sobrando");
			}

			// =====================================================================
			// 5. AS FORMAS -- so as que aquele BP abriria
			// =====================================================================
			var comEscada = new MoldeDeNpc
			{
				Id = "bancada_escada", Racas = ["Saiyan"], Classe = "Saiyan",
				BpMin = 1e13, BpMax = 1e13, MarcosBase = 0, MarcosPorDecada = 0,
				EscadaAutomatica = true, Maestria = 100,
			};
			{
				FichaSorteada f = SorteioDeNpc.Sortear(comEscada, 4242, _racas!, _skills, zona.Name, 0);
				var corpo = new ServerPlayer
				{
					Id = _nextId++, Peer = null, Name = "bancada: escada", Zone = zona,
					Pos = new Vec2(-256, 0), Race = f.Raca, Class = f.Classe, Genero = f.Genero,
					Ficha = f.Ficha, Livro = f.Livro, Niveis = f.Niveis, Papel = f.Papel,
					LastInputMs = NowMs(),
				};
				PorNoMundo(corpo);
				nascidos.Add(corpo);

				int abertas = SorteioDeNpc.AbrirFormas(corpo.Forma, comEscada, corpo.Ficha.BP, Perfil(corpo));
				Checa("com 1e13 de BP a escada automatica abre varios degraus", abertas >= 3, $"{abertas}");
				Checa("...e ele COMECA NA BASE (ter a forma nao e estar nela)", corpo.Forma.NaBase, corpo.Forma.Atual);

				// A coerencia: nenhuma forma liberada tem porta de BP acima do BP dele.
				List<string> altasDemais = [.. corpo.Forma.Liberadas
					.Select(r => Catalogo.PorRede((ushort)r))
					.Where(d => d != null && d.PortaBp > corpo.Ficha.BP).Select(d => d!.Id)];
				Checa("nenhuma forma liberada esta acima da porta de BP dele",
					  altasDemais.Count == 0, string.Join(", ", altasDemais));
			}

			// E o avesso: um soldado de 45 mil de BP nao tem escada nenhuma (a porta do SSJ1 e 1,5 mi)
			{
				FichaSorteada f = SorteioDeNpc.Sortear(soldado, 99, _racas!, _skills, zona.Name, 0);
				var corpo = new ServerPlayer
				{
					Id = _nextId++, Peer = null, Name = "bancada: soldado", Zone = zona,
					Pos = new Vec2(-320, 0), Race = f.Raca, Class = f.Classe, Genero = f.Genero,
					Ficha = f.Ficha, Livro = f.Livro, Niveis = f.Niveis, Papel = f.Papel,
					LastInputMs = NowMs(),
				};
				PorNoMundo(corpo);
				nascidos.Add(corpo);
				SorteioDeNpc.AbrirFormas(corpo.Forma, soldado, corpo.Ficha.BP, Perfil(corpo));
				Checa("um soldado comum nao ganha forma nenhuma (o BP dele nao abre a porta)",
					  corpo.Forma.Liberadas.Count == 0, $"{corpo.Forma.Liberadas.Count} formas com BP {corpo.Ficha.BP:N0}");
			}

			// =====================================================================
			// 6. "TEM A FORMA E NAO A USA" -- o pedido literal do dono
			// =====================================================================
			ServerPlayer? guardiao = Spawnar("guardiao_saiyajin", 1);
			Checa("o guardiao nasceu", guardiao != null);
			if (guardiao != null)
			{
				Checa("ele TEM o SSJ (o Despertou() dele e verdadeiro)",
					  guardiao.Forma.Despertou("ssj1"), string.Join(",", guardiao.Forma.Liberadas));
				Checa("...e comeca na base", guardiao.Forma.NaBase, guardiao.Forma.Atual);

				// A MESMA FUNCAO DA TECLA C, cinco vezes. Se a guarda estivesse so no comentario (ou
				// so num futuro cerebro), ele subiria aqui.
				for (int i = 0; i < 5; i++) Transformar(guardiao, subir: true);
				Checa("apertar C cinco vezes NAO o transforma (a guarda esta no funil, nao no papel)",
					  guardiao.Forma.NaBase, guardiao.Forma.Atual);

				// O CONTROLE, e ele e o que separa "a regra funciona" de "nada acontece por outro
				// motivo": o MESMO corpo, com o MESMO BP e a MESMA forma liberada, sobe assim que o
				// papel sai. Sem esta metade, a checagem acima passaria ate se `Transformar` tivesse
				// sido apagado.
				PapelDeNpc guardado = guardiao.Papel!;
				guardiao.Papel = null;
				Transformar(guardiao, subir: true);
				Checa("...e o MESMO corpo sobe assim que o papel sai (era a guarda, e nao outra recusa)",
					  !guardiao.Forma.NaBase, guardiao.Forma.Atual);
				Transformar(guardiao, subir: false);
				guardiao.Papel = guardado;
			}

			// =====================================================================
			// 7. O ROTEIRO -- quatro limiares, quatro transicoes
			// =====================================================================
			EscutaDoRoteiro = [];
			ServerPlayer? freeza = Spawnar("freeza_namek", 2);
			Checa("o Freeza de Namek nasceu", freeza != null);
			if (freeza != null)
			{
				MoldeDeNpc m = freeza.Papel!.Molde;
				Checa("a ficha dele tem CINCO degraus", m.Estagios.Length == 5, $"{m.Estagios.Length}");
				Checa("e ele comeca no primeiro, com o BP do primeiro",
					  freeza.Papel.Estagio == 0 && Math.Abs(freeza.Ficha.BP - m.Estagios[0].Bp) < 1,
					  $"degrau {freeza.Papel.Estagio} BP {freeza.Ficha.BP:N0}");

				// desce o pior membro ate um triz ABAIXO de cada limiar, um de cada vez
				for (int passo = 0; passo < 4; passo++)
				{
					double limiar = m.Estagios[passo].GatilhoMembro;
					PorPiorMembroEm(freeza, limiar - 0.01);
					TickDoRoteiro();
					Checa($"cruzar {limiar:0.00} avanca pro degrau {passo + 2}",
						  freeza.Papel.Estagio == passo + 1, $"esta no {freeza.Papel.Estagio + 1}");
					Checa($"...e o BP e o DA FICHA, nao uma media ({m.Estagios[passo + 1].Bp:N0})",
						  Math.Abs(freeza.Ficha.BP - m.Estagios[passo + 1].Bp) < 1, $"{freeza.Ficha.BP:N0}");
					Checa("...e ele volta com o folego cheio (bev_pin_bp enche o Ki)",
						  freeza.Ficha.Ki >= freeza.Ficha.MaxKi - 1e-6);
				}

				Checa("no ultimo degrau nao ha mais gatilho -- nada o encerra",
					  freeza.Papel.GatilhoAtual < 0, $"{freeza.Papel.GatilhoAtual}");
				PorPiorMembroEm(freeza, 0.01);
				TickDoRoteiro();
				Checa("...e nem destruido ele passa do quinto", freeza.Papel.Estagio == 4, $"{freeza.Papel.Estagio}");
				Checa("a zona ouviu um anuncio por degrau", EscutaDoRoteiro!.Count == 4,
					  $"{EscutaDoRoteiro!.Count} anuncios");
			}

			// UMA RAJADA UNICA PRODUZ UMA TRANSICAO SO. E o `+0,01` do `freeza_transform`
			// (BossEvents.dm:662): sem ele, um golpe que leve a vida de 0,72 pra 0,05 curaria pela
			// metade e AINDA estaria abaixo do proximo limiar -- e o chefe pularia metade da ficha.
			EscutaDoRoteiro = [];
			ServerPlayer? freeza2 = Spawnar("freeza_namek", 3);
			if (freeza2 != null)
			{
				PorPiorMembroEm(freeza2, 0.01);      // a rajada que quase o mata, de uma vez
				TickDoRoteiro();
				Checa("uma rajada unica avanca EXATAMENTE UM degrau (o reancoramento de +0,01)",
					  freeza2.Papel!.Estagio == 1, $"pulou pro degrau {freeza2.Papel!.Estagio + 1}");
				Checa("...e o pior membro dele ficou ACIMA do limiar seguinte",
					  PiorMembro(freeza2) > LimiarDoDegrau(freeza2), $"{PiorMembro(freeza2):0.000}");

				// e o tique seguinte, sem dano novo, nao avanca de novo
				TickDoRoteiro();
				Checa("...e o tique seguinte, sem golpe novo, nao avanca", freeza2.Papel!.Estagio == 1);
			}

			// =====================================================================
			// 8. O FREEZA DE VEGETA -- a lista de um item so
			// =====================================================================
			ServerPlayer? freeza1 = Spawnar("freeza_vegeta", 4);
			if (freeza1 != null)
			{
				Checa("a ficha dele tem UM degrau so", freeza1.Papel!.Molde.Estagios.Length == 1);
				Checa("...entao nao ha gatilho nenhum", freeza1.Papel!.GatilhoAtual < 0);
				PorPiorMembroEm(freeza1, 0.01);
				TickDoRoteiro();
				TickDoRoteiro();
				Checa("...e destrui-lo nao o transforma: ele NAO transforma porque a lista acabou "
					+ "(nenhum `if` de nome proprio em lugar nenhum)",
					  freeza1.Papel!.Estagio == 0 && Math.Abs(freeza1.Ficha.BP - 530_000) < 1,
					  $"degrau {freeza1.Papel!.Estagio + 1} BP {freeza1.Ficha.BP:N0}");
			}

			// =====================================================================
			// 9. O CORPO EXISTE MESMO -- ele esta no mundo, nao so na memoria
			// =====================================================================
			if (guardiao != null)
			{
				Checa("o NPC esta no `_players`", _players.ContainsKey(guardiao.Id));
				Checa("...e na lista da zona (fora dela ninguem o ve, e um teste daria verde por ausencia)",
					  ZoneList(zona.Hash).Contains(guardiao));
				Checa("...e ele tem CORPO (membros), como qualquer jogador",
					  guardiao.Combate is { } c && c.Corpo.Partes.Count > 0);
				Checa("...e nao tem dono na tela (`Peer == null` e a definicao de NPC deste servidor)",
					  guardiao.Peer == null);
			}

			// =====================================================================
			// 10. A ROUPA -- *"todo npc ta nascendo SEM ROUPAS"*
			// =====================================================================
			// ============================ POR QUE ESTA SECAO OLHA O CORPO, E NAO A TABELA ============================
			// Porque a falha que o dono viu nao estava na tabela: ela nao existia. E a falha SEGUINTE,
			// a que uma bancada distraida deixa passar, e a peca ser escolhida e depois DESCARTADA em
			// silencio pelo `VisualCatalog.Sanear` -- que foi exatamente o que aconteceria com a
			// armadura antes de ela entrar no catalogo. Conferir o que `RoupaDeNpc` devolve ficaria
			// verde nos dois mundos.
			//
			// Entao o que se mede aqui e `npc.Visual.Roupa` DEPOIS do funil inteiro (tabela ->
			// `Sanear` -> corpo no mundo), que e o mesmo objeto que o `Protocol.PutAppearance`
			// escreve no fio. E o par do "uniform escrito != pixel desenhado".
			// ==================================================================================================
			Checa("o catalogo visual carregou (sem ele ninguem se veste)", _visual != null);
			if (_visual != null)
			{
				// --- a) A ARTE EXISTE? E o "PARE e RELATE" do pedido, e ele e por RACA ---
				// Varre TODA raca do races.json, nao so as tres do DM: a tabela tem linha pra cada uma
				// e uma peca sem arte devolve nulo em vez de sumir. Quem faltar sai nomeado no detalhe.
				var semArte = new List<string>();
				foreach (string raca in _racas!.Protos.Keys)
					foreach (string g in new[] { "Male", "Female" })
						for (ulong s = 1; s <= 24; s++)
						{
							var falta = new List<string>();
							RoupaDeNpc.Vestir(_visual, raca, g, s, null, falta);
							foreach (string f in falta) if (!semArte.Contains($"{raca}: {f}")) semArte.Add($"{raca}: {f}");
						}
				Checa("nenhuma peca da tabela esta sem arte no port", semArte.Count == 0,
					  string.Join(" | ", semArte));

				// --- b) DETERMINISMO: a roupa sai da semente DELE, e nao de um `Random` solto ---
				string Vestido(string raca, string gen, ulong s) => string.Join("+",
					RoupaDeNpc.Vestir(_visual, raca, gen, s).Select(p => $"{p.Caminho}#{p.Cor}"));

				Checa("mesma semente = MESMA roupa (caminho E cor)",
					  Vestido("Saiyan", "Male", 777) == Vestido("Saiyan", "Male", 777),
					  Vestido("Saiyan", "Male", 777));
				// ...e ela VARIA, senao o determinismo estaria sendo cumprido por uma tabela de um item so
				Checa("...e sementes diferentes nao dao todas a mesma roupa",
					  Enumerable.Range(1, 40).Select(i => Vestido("Human", "Male", (ulong)i)).Distinct().Count() > 1);
				Checa("...e a cor da 'Armor 8' tambem sai da semente (nao e sorteada de novo a cada leitura)",
					  Enumerable.Range(1, 60).Select(i => Vestido("Saiyan", "Male", (ulong)i))
							.Count(v => v.Contains("Armor 8", StringComparison.Ordinal)
									 && !v.EndsWith('#')) > 0);

				// --- c) A ROUPA E DA RACA CERTA -- o pedido inteiro do dono numa linha ---
				bool SoDe(string raca, string gen, string[] permitidas) =>
					Enumerable.Range(1, 60).All(i =>
						RoupaDeNpc.Vestir(_visual, raca, gen, (ulong)i)
							.All(p => permitidas.Any(q => p.Caminho.Contains(q, StringComparison.OrdinalIgnoreCase))));

				Checa("SAIYAJIN so veste armadura de batalha (PlanetPopulation.dm:368)",
					  SoDe("Saiyan", "Male", ["/Armor/Armor 8", "Armor Bardock", "Nappa Armor", "RaditzArmor"]));
				Checa("NAMEKUSEIJIN so veste jaqueta/cachecol de Namek (PlanetPopulation.dm:413-414)",
					  SoDe("Namekian", "Male", ["ClothesNamekJacket", "Clothes_NamekianScarf"]));
				Checa("HUMANO so veste Gi ou roupa comum (PlanetPopulation.dm:396-402)",
					  SoDe("Human", "Male", ["Clothes_GiTop", "Clothes_GiBottom", "Clothes_TankTop",
											 "Clothes_ShortSleeveShirt", "Clothes_LongSleeveShirt"]));
				Checa("...e a HUMANA de Gi veste o Gi FEMININO (PlanetPopulation.dm:397)",
					  SoDe("Human", "Female", ["ClothesGiFemale", "Clothes_TankTop",
											   "Clothes_ShortSleeveShirt", "Clothes_LongSleeveShirt"]));
				Checa("o FROST DEMON nao ganha roupa nenhuma (o corpo dele E a forma)",
					  Enumerable.Range(1, 40).All(i => RoupaDeNpc.Vestir(_visual, "Icer", "Male", (ulong)i).Count == 0));

				// --- d) O CORPO NO MUNDO: a peca sobreviveu ao `Sanear` e chegou na aparencia ---
				// Pela linha de POVOAMENTO, que e o caminho de producao do cidadao: planeta de
				// verdade, raca vinda do berco, `NascerNpc` inteiro. So em planeta sem ninguem
				// conectado, pelo mesmo motivo que a zona la de cima e vazia.
				ulong lugar = 900;
				int vestidos = 0, pelados = 0, porDesenho = 0;
				var nus = new List<string>();
				var foraDoCatalogo = new List<string>();
				var racasVistas = new HashSet<string>(StringComparer.Ordinal);
				foreach (LinhaDePovoamento linha in _moldes.Plano)
				{
					if (_players.Values.Any(p => p.Peer != null
						&& string.Equals(p.Zone.Name, linha.Planeta, StringComparison.OrdinalIgnoreCase))) continue;

					// SEIS POR PLANETA, E NAO UM. A raca do cidadao vem do BERCO e e sorteada da
					// semente do lugar: com um corpo por planeta, quais racas a bancada exercita
					// vira loteria -- a primeira rodada desta secao nasceu Meta, Kanassa, Icer,
					// Arlian e Makyo e nao viu **nenhum** Saiyajin, Humano ou Namekuseijin, que sao
					// justamente as tres racas do pedido do dono. Seis lugares diferentes cobrem o
					// berco de cada planeta em vez de sortear uma raca dele.
					for (int k = 0; k < 6; k++)
					{
						ServerPlayer? cid = NascerNpc(linha.Molde, ZoneKey.Premade(linha.Planeta),
													  new Vec2(0, 0), ++lugar);
						if (cid == null) continue;
						nascidos.Add(cid);
						racasVistas.Add(cid.Race);

						// ============================ "PELADO" NAO E UMA COISA SO ============================
						// Ha DUAS razoes pra um corpo sair sem peca, e confundi-las era o defeito da
						// primeira versao desta linha: ela reprovava o cidadao de Icer, que esta CERTO
						// -- o sprite do Frost Demon ja e o bio-traje. Uma bancada que exige o
						// impossivel e tao inutil quanto uma que nao exige nada; a diferenca e que
						// esta aqui reprovava a REGRA em vez do defeito.
						//
						// A pergunta com dente e a outra: alguem que DEVIA estar vestido saiu nu? A
						// resposta de quem se veste sozinho vem do Core (`RoupaDeNpc.OCorpoJaVeste`)
						// e nao de uma segunda lista aqui -- ver o bloco de la.
						// ================================================================================
						bool nu = cid.Visual.Roupa.Count == 0;
						if (!nu) vestidos++;
						else if (RoupaDeNpc.OCorpoJaVeste(cid.Race)) porDesenho++;
						else { pelados++; nus.Add($"{cid.Name}/{cid.Race}@{linha.Planeta}"); }

						// TODA peca que chegou aqui passou pelo catalogo -- se `Sanear` a tivesse
						// descartado, ela nao estaria na lista. Isto confere o contrario: que nenhuma
						// peca esta na lista SEM estar no catalogo (no dia em que alguem afrouxar o
						// `Sanear`, esta linha avisa). Acumula em vez de checar por corpo: 36 linhas
						// iguais no console escondem a que importa.
						foreach (PecaDeRoupa p in cid.Visual.Roupa)
							if (!_visual.Roupas.Contains(p.Caminho) && !_visual.Armaduras.Contains(p.Caminho))
								foraDoCatalogo.Add($"{cid.Race}: {p.Caminho}");
					}
				}
				Checa("os cidadaos do povoamento nascem VESTIDOS (era zero antes desta tarefa)",
					  vestidos > 0 && pelados == 0,
					  $"{vestidos} vestido(s), {pelados} pelado(s) SEM motivo: {string.Join(" | ", nus)}");
				Checa("...e nenhuma peca deles esta fora do catalogo (nada inventado)",
					  foraDoCatalogo.Count == 0, string.Join(" | ", foraDoCatalogo));
				GD.Print($"       ({vestidos} vestidos, {porDesenho} sem peca por DESENHO -- sprite proprio) "
					   + $"racas exercitadas pelo caminho de producao: {string.Join(", ", racasVistas.OrderBy(x => x, StringComparer.Ordinal))}");

				// AS TRES DO PEDIDO, pelo caminho de producao e nao so pela tabela. Se o berco dos
				// seis planetas nao produziu nenhuma delas nesta rodada, isto fica AMARELO no console
				// em vez de verde calado: a secao (c) ja provou a tabela, mas quem prova o funil e
				// um corpo de verdade.
				foreach (string r in new[] { "Saiyan", "Human", "Namekian" })
					if (!racasVistas.Contains(r))
						GD.Print($"       (aviso: nenhum {r} nasceu nesta rodada -- a tabela dele so foi "
							   + "exercitada pela secao (c), nao pelo corpo no mundo)");

				// --- d.2) E ELA CHEGA NO FIO INTEIRA ---
				// ============================ O TETO DE 120 CARACTERES POR CAMINHO ============================
				// `GetAppearance` le cada caminho com `r.GetString(120)`, e o comentario de la avisa do
				// que acontece acima do teto: o LiteNetLib **nao trunca -- ele consome os bytes e
				// devolve string VAZIA**, e a peca e descartada. Ou seja: uma peca de caminho comprido
				// sairia do servidor vestida e chegaria no cliente ausente, sem erro em lugar nenhum.
				//
				// Isso nunca foi problema enquanto a roupa vinha so da pasta `Clothes/`. A armadura
				// mora um nivel mais fundo (`Clothes/Armor/`), e foi este passo que aproximou o teto.
				// Medir a peca mais comprida do CATALOGO (e nao a da roupa que calhou de sair) e o que
				// impede a bancada de ficar verde por sorte.
				// ==========================================================================================
				string maisComprido = _visual.Roupas.Concat(_visual.Armaduras)
					.OrderByDescending(c => c.Length).First();
				Checa("nenhum caminho do catalogo estoura o teto do fio (120)",
					  maisComprido.Length <= 120, $"{maisComprido.Length} chars: {maisComprido}");

				// e o par escrita/leitura de verdade, com uma peca TINGIDA -- a cor viaja separada do
				// caminho, e perde-la calada foi um defeito real deste projeto (ver `PecaDeRoupaConverter`)
				var noFio = new Appearance { Cabelo = "Bald" };
				noFio.Roupa.AddRange(RoupaDeNpc.Vestir(_visual, "Saiyan", "Male", 777));
				var escritor = new LiteNetLib.Utils.NetDataWriter();
				escritor.PutAppearance(noFio);
				Appearance devolta = new LiteNetLib.Utils.NetDataReader(escritor.CopyData()).GetAppearance();
				Checa("a roupa do NPC sobrevive ao fio (caminho E cor, ida e volta)",
					  string.Join("+", noFio.Roupa.Select(p => $"{p.Caminho}#{p.Cor}"))
					  == string.Join("+", devolta.Roupa.Select(p => $"{p.Caminho}#{p.Cor}")),
					  string.Join(",", devolta.Roupa.Select(p => p.Caminho)));

				// --- e) O CHEFE NAO ENTRA NO SORTEIO ---
				if (freeza2 != null)
					Checa("o chefe cujo SPRITE ja o veste nao ganha roupa por sorteio (Freeza de moletom)",
						  freeza2.Visual.Roupa.Count == 0,
						  string.Join(",", freeza2.Visual.Roupa.Select(p => p.Caminho)));
				if (guardiao != null)
					Checa("...e o chefe com `roupa` cravada no molde veste EXATAMENTE aquilo",
						  guardiao.Visual.Roupa.Count == 3
						  && guardiao.Visual.Roupa.Any(p => p.Caminho.Contains("Armor_Elite", StringComparison.Ordinal)),
						  string.Join(",", guardiao.Visual.Roupa.Select(p => p.Caminho)));

				ServerPlayer? a17 = Spawnar("androide_17", 950);
				if (a17 != null)
					Checa("o Androide 17 veste a camisa CASUAL que o DM escreve a mao (BossEvents.dm:499)",
						  a17.Visual.Roupa.Count == 1
						  && a17.Visual.Roupa[0].Caminho.Contains("LongSleeveShirt", StringComparison.Ordinal),
						  string.Join(",", a17.Visual.Roupa.Select(p => p.Caminho)));

				// --- f) SOBREVIVE A REPOSICAO: o NPC nao tem save, entao a roupa e REDEDUZIDA ---
				// A manutencao do povoamento repoe o cidadao a cada 5 min com o MESMO lugar. Se a
				// roupa dependesse de qualquer coisa fora da semente, ele voltaria diferente -- ou
				// pelado, que e onde esta tarefa comecou.
				ServerPlayer? antes = Spawnar("androide_17", 951);
				ServerPlayer? depois = Spawnar("androide_17", 951);
				if (antes != null && depois != null)
					Checa("renascer no MESMO lugar devolve a MESMA roupa (o NPC nao tem save: ela e rededuzida)",
						  string.Join("+", antes.Visual.Roupa.Select(p => $"{p.Caminho}#{p.Cor}"))
						  == string.Join("+", depois.Visual.Roupa.Select(p => $"{p.Caminho}#{p.Cor}")));
			}
		}
		finally
		{
			EscutaDoRoteiro = null;
			foreach (ServerPlayer n in nascidos) RemoverNpc(n);
			GD.Print($"===== FIM: {ok} ok, {falhou} falha(s) =====\n");
			if (falhou > 0) GD.PushError($"[server] bancada de NPC: {falhou} falha(s)");
			Avisar(pl, $"bancada de NPC: {ok} ok, {falhou} falha(s) -- veja o console.");
		}
	}

	/// <summary>O limiar que encerra o degrau ATUAL -- so pra deixar a checagem legivel.</summary>
	private static double LimiarDoDegrau(ServerPlayer pl) => pl.Papel?.GatilhoAtual ?? -1;

	/// <summary>
	/// POE O PIOR MEMBRO NUMA FRACAO EXATA, ferindo pelo caminho de producao (`Body.Ferir`).
	///
	/// Escrever `p.Vida = x` direto seria mais curto e mediria outra coisa: o `Ferir` propaga dano
	/// pros vizinhos e pode decepar, e e nesse estado que o chefe de verdade vai estar quando o
	/// gatilho disparar.
	/// </summary>
	private static void PorPiorMembroEm(ServerPlayer pl, double fracao)
	{
		BodyPart alvo = pl.Combate.Corpo.Partes.OrderByDescending(p => p.Fracao).First();
		double desejada = Math.Clamp(fracao, 0, 1) * alvo.VidaMax;
		if (alvo.Vida > desejada) pl.Combate.Corpo.Ferir(alvo, alvo.Vida - desejada, letal: false);
		pl.Combate.SincronizarVida();
	}
}
